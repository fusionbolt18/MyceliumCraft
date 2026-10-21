using Cinemachine;
using MyceliumCraft.Link;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Points Mycelium's camera where Minecraft's player looks, over Minecraft's full look range.
    ///
    /// Mycelium's main camera is a Cinemachine FreeLook (an orbit camera around the spore) on the
    /// object tagged "MainCamera", in World Space binding: its X axis is a world heading
    /// (-180..180), its Y axis (0..1) moves it along a curve through a low, a middle and a high
    /// orbit. While Minecraft drives the spore:
    ///  - X follows Minecraft's yaw.
    ///  - Minecraft's pitch (-90 up .. 90 down) becomes a wanted camera angle (-89 .. 89), with
    ///    "straight ahead" at CameraPitchOffset (Mycelium's normal middle view).
    ///  - The high orbit is raised to HighOrbitDown degrees so the camera can swing almost overhead;
    ///    Y is set to the point on the orbit curve with the wanted angle (clamped to the curve).
    ///  - After Cinemachine has placed the camera, the view is tilted by whatever is still missing,
    ///    so the camera looks at exactly the wanted angle: beyond the curve (looking up from the
    ///    low orbit, or straight down) and to cancel the small offsets Mycelium's aim adds.
    ///  - Mycelium's own mouse input to the camera is off.
    ///  - Minecraft's F5 view is copied: third person behind is the orbit above; third person in
    ///    front swings the orbit round to face the spore (heading + 180, pitch mirrored, as
    ///    Minecraft does); first person puts the camera at the spore's eyes looking exactly where
    ///    Minecraft looks, with the spore's own body drawn as shadow only (like Minecraft).
    ///  - Mycelium's camera shakes (Cinemachine impulses) are kept: ShakeTap records what the
    ///    shake added, the tilt is worked out from the unshaken view, and first person adds the
    ///    shake to the eye camera.
    ///  - Minecraft's hurt tilt (HurtTilt) leans the finished view toward a hit, in every view.
    /// Everything is restored when Minecraft stops driving. Cutscene and "reset/navigate" cameras
    /// still take over as they normally do (no tilt is applied while another camera is live).
    /// </summary>
    sealed class CameraSync
    {
        const float HighOrbitDown = 80f; // degrees down for the raised high orbit
        const int Samples = 101;         // points along the orbit curve for angle -> Y
        const float ReleaseAfterSeconds = 0.5f; // Minecraft must stop driving this long before the camera is given back
        float notDrivingSince = -1f;

        CinemachineFreeLook freeLook;
        ShakeTap shake;
        CinemachineCollider collider;
        float savedOccludedDamping;
        CinemachineFreeLook.Orbit[] savedOrbits;
        string savedXInput, savedYInput;
        bool controlling;
        bool listening;
        readonly float[] curveAngle = new float[Samples]; // downward angle at Y = i / (Samples - 1)
        float wantDown;    // wanted camera angle this frame (degrees, positive = down)
        float lastTilt;    // tilt applied after Cinemachine last frame
        float lastShakePos, lastShakeAngle; // size of Mycelium's shake this frame (units, degrees)
        float lastPitch;   // Minecraft pitch this frame (for the trace)
        float smoothPitch; // Minecraft pitch eased over CameraSmoothing seconds
        ulong lastMcFrame; // Minecraft frame counter this frame (for the trace)
        System.IO.StreamWriter trace; // "-mcCameraTrace": one line per rendered frame

        // F5 view
        public const uint FirstPerson = 0, Behind = 1, InFront = 2;
        uint mode = Behind;
        float smoothYaw;          // first person: Minecraft yaw eased like the pitch (no Cinemachine damping there)
        Transform spore;          // the spore's (interpolated) transform, for the eye position
        float eyeOffset;          // eye height above spore.position
        // Minecraft's camera feel: FOV multiplier (sprinting) and eye height / standing (crouching).
        float fovScale = 1f, eyeScale = 1f;
        float savedFov;           // Mycelium's own FOV, to multiply and to put back
        float nextFeelLog;
        string lastFeel = "";
        public float FovScale => fovScale;
        public float EyeScale => eyeScale;
        readonly System.Collections.Generic.List<(Renderer r, UnityEngine.Rendering.ShadowCastingMode mode, bool off)> hidden =
            new System.Collections.Generic.List<(Renderer, UnityEngine.Rendering.ShadowCastingMode, bool)>();

        public uint Mode => mode;

        // Flash check: right before each frame is drawn, in first person, is the camera at the eyes?
        uint wantedMode = Behind;     // the view Minecraft asks for (even while not controlling)
        bool flashCheckHooked;
        int framesChecked, flashFrames;
        public int FlashFrames => flashFrames;
        public int FramesChecked => framesChecked;
        public static string ModeName(uint m) => m == FirstPerson ? "first person" : m == InFront ? "third person in front" : "third person behind";

        public float X => freeLook != null ? freeLook.m_XAxis.Value : float.NaN;
        public float Y => freeLook != null ? freeLook.m_YAxis.Value : float.NaN;
        public float WantDown => wantDown;
        public float LastTilt => lastTilt;
        public float ShakePos => lastShakePos;
        public float ShakeAngle => lastShakeAngle;
        public bool Controlling => controlling;
        /// <summary>Set while your Minecraft body is drawn instead of the spore (McPlayerModel): hide the spore then too.</summary>
        public bool ReplaceSpore;
        /// <summary>Minecraft's hurt tilt, put on top of the finished view (set by the driver).</summary>
        public HurtTilt Hurt;
        int sporeHidden; // 0 drawn, 1 shadow only, 2 not drawn at all

        /// <summary>Forget the camera (new scene; the old one is gone with its scene).</summary>
        public void Reset()
        {
            freeLook = null;
            controlling = false;
            hidden.Clear();
            sporeHidden = 0;
            spore = null;
            mode = Behind;
        }

        /// <summary>The spore to look out of in first person: its transform and eye height above its position.</summary>
        public void SetSpore(Transform sporeTransform, float eyeHeightAbovePosition)
        {
            if (spore != sporeTransform)
            {
                ShowSpore();
                sporeHidden = 0;
            }
            spore = sporeTransform;
            eyeOffset = eyeHeightAbovePosition;
        }

        /// <summary>Call every frame. driving: Minecraft is driving the spore and mc is fresh.</summary>
        public void Update(bool driving, in McState mc)
        {
            wantedMode = McConfig.MatchF5View && mc.CameraMode <= InFront ? mc.CameraMode : Behind;
            if (!flashCheckHooked)
            {
                flashCheckHooked = true;
                UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += CheckFlash;
            }
            if (!McConfig.SyncCamera) driving = false;
            if (freeLook == null && driving) Find();
            if (freeLook == null) return;

            if (!driving)
            {
                // Only give the camera back after Minecraft has really stopped driving for a while;
                // a one-frame release showed Mycelium's third-person camera for that frame.
                if (controlling)
                {
                    if (notDrivingSince < 0f) notDrivingSince = Time.realtimeSinceStartup;
                    else if (Time.realtimeSinceStartup - notDrivingSince >= ReleaseAfterSeconds) Release("Minecraft not driving");
                }
                return;
            }
            notDrivingSince = -1f;
            if (!controlling)
            {
                Take();
                smoothPitch = mc.Pitch;
                smoothYaw = WorldMapping.MycYaw(mc.Yaw);
            }

            uint wanted = McConfig.MatchF5View && mc.CameraMode <= InFront ? mc.CameraMode : Behind;
            if (wanted != mode)
            {
                McLog.Info($"camera: Minecraft view {ModeName(mode)} -> {ModeName(wanted)}");
                mode = wanted;
            }
            // The spore's body: not drawn at all while your Minecraft body stands in for it (that
            // body casts the shadow, in first person too); otherwise shadow only in first person
            // (the camera is inside it).
            int hide = ReplaceSpore ? 2 : mode == FirstPerson ? 1 : 0;
            if (hide != sporeHidden)
            {
                sporeHidden = hide;
                if (hide > 0) HideSpore(hide == 2); else ShowSpore();
            }
            else if (hide > 0) KeepHidden(hide == 2);

            fovScale = mc.FovScale > 0.05f && mc.FovScale < 10f ? mc.FovScale : 1f;
            eyeScale = mc.EyeScale > 0.05f && mc.EyeScale < 2f ? mc.EyeScale : 1f;
            if (savedFov > 0f) freeLook.m_Lens.FieldOfView = Mathf.Clamp(savedFov * fovScale, 1f, 179f);
            string feel = $"FOV x{fovScale:F2}, eye x{eyeScale:F2}";
            if (feel != lastFeel && Time.realtimeSinceStartup >= nextFeelLog)
            {
                lastFeel = feel;
                nextFeelLog = Time.realtimeSinceStartup + 0.5f;
                McLog.Info($"camera: Minecraft's camera feel {feel} (lens {savedFov * fovScale:F1} degrees, camera now {(Camera.main != null ? Camera.main.fieldOfView : 0f):F1})");
            }

            lastPitch = mc.Pitch;
            lastMcFrame = mc.Frame;
            // Minecraft sends a new look once per Minecraft frame; snapping to each one stutters
            // against Mycelium's own frame rate. Ease toward it instead. In third person the
            // heading is smoothed by Cinemachine's own damping; first person bypasses Cinemachine,
            // so the yaw is eased here too.
            float tau = McConfig.CameraSmoothing;
            float k = tau > 0f ? 1f - Mathf.Exp(-Time.unscaledDeltaTime / tau) : 1f;
            smoothPitch = Mathf.Lerp(smoothPitch, mc.Pitch, k);
            smoothYaw = Mathf.LerpAngle(smoothYaw, WorldMapping.MycYaw(mc.Yaw), k);

            // Third person in front: the camera faces the spore, so heading + 180 and the pitch
            // mirrored, as Minecraft's own front view does. The eased yaw is used in every view:
            // with the input bridge, Minecraft turns in uneven steps (mouse movement reaches it in
            // whatever Minecraft frame comes next), and Cinemachine passes the heading straight
            // through, so third person showed it as jitter.
            float heading = smoothYaw + (mode == InFront ? 180f : 0f);
            heading = Mathf.Repeat(heading + 180f, 360f) - 180f;
            freeLook.m_XAxis.Value = heading;
            wantDown = mode == FirstPerson ? Mathf.Clamp(smoothPitch, -89.9f, 89.9f)
                     : PitchToAngle(mode == InFront ? -smoothPitch : smoothPitch);
            freeLook.m_YAxis.Value = AngleToY(mode == FirstPerson ? McConfig.CameraPitchOffset : wantDown);
        }

        /// <summary>
        /// Right before a camera draws: if Minecraft is in first person and the main camera isn't
        /// at the spore's eyes, that frame shows something else (a "flash"). Counted and logged.
        /// </summary>
        void CheckFlash(UnityEngine.Rendering.ScriptableRenderContext context, Camera cam)
        {
            if (spore == null || freeLook == null || cam == null || !cam.CompareTag("MainCamera")) return;
            if (wantedMode != FirstPerson || !McConfig.SyncCamera) return;
            framesChecked++;
            float off = Vector3.Distance(cam.transform.position, spore.position + Vector3.up * eyeOffset * eyeScale);
            if (off > 0.3f)
            {
                flashFrames++;
                if (flashFrames <= 20)
                    McLog.Info($"camera FLASH: frame {Time.frameCount}, first person wanted but the camera is {off:F2} units from the eyes (controlling={controlling}, view={ModeName(mode)})");
            }
        }

        /// <summary>
        /// First person: draw the spore's own body as shadow only, so the camera isn't inside it.
        /// fully: don't draw it at all, not even its shadow (your Minecraft body is shown instead).
        /// </summary>
        void HideSpore(bool fully)
        {
            ShowSpore();
            if (spore == null) return;
            foreach (Renderer r in spore.GetComponentsInChildren<Renderer>(true))
            {
                if (fully)
                {
                    if (r.forceRenderingOff) continue;
                    hidden.Add((r, r.shadowCastingMode, true));
                    r.forceRenderingOff = true;
                }
                else
                {
                    if (r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) continue;
                    hidden.Add((r, r.shadowCastingMode, false));
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                }
                hiddenSet.Add(r);
            }
            McLog.Info(fully ? $"camera: Minecraft body shown, {hidden.Count} spore renderers not drawn at all (no spore shadow)"
                             : $"camera: first person, {hidden.Count} spore renderers drawn as shadow only");
        }

        readonly System.Collections.Generic.List<Renderer> under = new System.Collections.Generic.List<Renderer>();
        readonly System.Collections.Generic.HashSet<Renderer> hiddenSet = new System.Collections.Generic.HashSet<Renderer>();

        /// <summary>
        /// Every frame while the spore is hidden: what the spore holds changes (a new weapon picked up,
        /// the old one dropped into the world). Hide renderers that are newly under the spore, and give
        /// back those that left it. Before, the list was made once: a picked-up weapon stayed visible
        /// and the dropped one stayed invisible on the ground (your report, 2026-10-20).
        /// </summary>
        void KeepHidden(bool fully, string when = "")
        {
            if (spore == null) return;
            for (int i = hidden.Count - 1; i >= 0; i--)
            {
                var (r, m, off) = hidden[i];
                if (r != null && r.transform.IsChildOf(spore)) continue;
                if (r != null)
                {
                    if (off) r.forceRenderingOff = false;
                    else r.shadowCastingMode = m;
                    McLog.Info($"camera: '{r.name}' left the spore; drawn again");
                }
                hiddenSet.Remove(r);
                hidden.RemoveAt(i);
            }
            spore.GetComponentsInChildren(true, under);
            foreach (Renderer r in under)
            {
                if (hiddenSet.Contains(r)) continue;
                if (fully)
                {
                    if (r.forceRenderingOff) continue;
                    hidden.Add((r, r.shadowCastingMode, true));
                    r.forceRenderingOff = true;
                }
                else
                {
                    if (r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) continue;
                    hidden.Add((r, r.shadowCastingMode, false));
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                }
                hiddenSet.Add(r);
                McLog.Info($"camera: '{r.name}' is new on the spore; {(fully ? "not drawn" : "shadow only")}{when}");
            }
        }

        /// <summary>
        /// Late in the frame (after coroutines, before drawing): hide renderers that came onto the spore
        /// since Update, so a weapon built in a coroutine isn't drawn for a frame (your report,
        /// 2026-10-20: switching weapons flashed the model).
        /// </summary>
        public void RefreshHidden()
        {
            if (sporeHidden > 0 && spore != null) KeepHidden(sporeHidden == 2, " (caught before drawing)");
        }

        void ShowSpore()
        {
            foreach (var (r, m, off) in hidden)
            {
                if (r == null) continue;
                if (off) r.forceRenderingOff = false;
                else r.shadowCastingMode = m;
            }
            hidden.Clear();
            hiddenSet.Clear();
        }

        /// <summary>Minecraft pitch to the wanted camera angle: -90..0..90 -> -89..offset..89.</summary>
        public static float PitchToAngle(float pitch)
        {
            float mid = Mathf.Clamp(McConfig.CameraPitchOffset, -80f, 80f);
            pitch = Mathf.Clamp(pitch, -90f, 90f);
            return pitch <= 0f ? Mathf.Lerp(mid, -89f, -pitch / 90f) : Mathf.Lerp(mid, 89f, pitch / 90f);
        }

        /// <summary>
        /// The Y value whose point on the orbit curve looks down by this angle, interpolated between
        /// the samples (rounding to the nearest sample made the camera hop in 0.08-unit steps).
        /// Clamped to the curve's ends; the tilt covers the rest.
        /// </summary>
        float AngleToY(float angle)
        {
            if (angle <= curveAngle[0]) return 0f;
            if (angle >= curveAngle[Samples - 1]) return 1f;
            for (int i = 0; i < Samples - 1; i++)
            {
                float a = curveAngle[i], b = curveAngle[i + 1];
                if ((angle >= a && angle <= b) || (angle <= a && angle >= b))
                    return (i + (Mathf.Abs(b - a) > 1e-5f ? (angle - a) / (b - a) : 0f)) / (Samples - 1);
            }
            return 1f;
        }

        void Find()
        {
            GameObject main = GameObject.FindWithTag("MainCamera");
            freeLook = main != null ? main.GetComponent<CinemachineFreeLook>() : null;
            if (freeLook == null)
            {
                McLog.Info("camera: no CinemachineFreeLook on the MainCamera object; camera not synced");
                return;
            }
            McLog.Info($"camera: found FreeLook on '{main.name}' (binding {freeLook.m_BindingMode}); Mycelium input provider on it: {(freeLook.GetComponent<AxisState.IInputAxisProvider>() is Component ip ? ip.GetType().Name + (((Behaviour)ip).enabled ? " (enabled)" : " (disabled)") : "none")}");
            // Added after Mycelium's own extensions (impulse listener, collider), so it runs after them.
            shake = freeLook.GetComponent<ShakeTap>() ?? freeLook.gameObject.AddComponent<ShakeTap>();
            var listener = freeLook.GetComponent<CinemachineImpulseListener>();
            McLog.Info($"camera: shake tap added; Mycelium impulse listener {(listener != null ? $"found (gain {listener.m_Gain}, apply after {listener.m_ApplyAfter}, secondary noise {(listener.m_ReactionSettings.m_SecondaryNoise != null ? "yes" : "no")})" : "NOT found")}");
            if (freeLook.m_BindingMode != CinemachineTransposer.BindingMode.WorldSpace)
                McLog.Info("camera: WARNING binding is not WorldSpace; the X axis is not a world heading, so the camera direction will be off");
            if (!listening)
            {
                CinemachineCore.CameraUpdatedEvent.AddListener(AfterCinemachine);
                listening = true;
            }
        }

        void Take()
        {
            // Mycelium's collision add-on snaps the camera in front of a wall instantly
            // (DampingWhenOccluded 0) and eases back out over 1 s. With the full look range the
            // camera swings past walls far more often, and each snap showed as a jump (up to 0.24
            // units in one frame). Ease it in a little while Minecraft drives.
            collider = freeLook.GetComponent<CinemachineCollider>();
            if (collider != null)
            {
                savedOccludedDamping = collider.m_DampingWhenOccluded;
                collider.m_DampingWhenOccluded = Mathf.Max(collider.m_DampingWhenOccluded, McConfig.CameraCollisionSmoothing);
                McLog.Info($"camera: collision add-on found (collides with layers {collider.m_CollideAgainst.value}, damping {collider.m_Damping}); ease-in when occluded {savedOccludedDamping} -> {collider.m_DampingWhenOccluded} s");
            }
            savedFov = freeLook.m_Lens.FieldOfView;
            savedXInput = freeLook.m_XAxis.m_InputAxisName;
            savedYInput = freeLook.m_YAxis.m_InputAxisName;
            freeLook.m_XAxis.m_InputAxisName = "";
            freeLook.m_YAxis.m_InputAxisName = "";
            // Mycelium's camera also has a CinemachineInputProvider: it feeds the mouse ("Look",
            // new Input System) straight into both axes, whatever the axis names say. With the input
            // bridge Mycelium has the focus, so your real mouse moved the camera on top of
            // Minecraft's look, partly a frame late (the FreeLook moves its Y axis at the end of its
            // update, for the next frame). That was the remaining up/down jitter. Unplug it from the
            // axes, and clear any speed they had built up (Reset keeps the axis position).
            freeLook.m_XAxis.SetInputAxisProvider(0, null);
            freeLook.m_YAxis.SetInputAxisProvider(1, null);
            freeLook.m_XAxis.Reset();
            freeLook.m_YAxis.Reset();

            // Raise the high orbit to nearly overhead, at the same distance from the spore.
            savedOrbits = (CinemachineFreeLook.Orbit[])freeLook.m_Orbits.Clone();
            var high = freeLook.m_Orbits[0];
            float distance = Mathf.Sqrt(high.m_Height * high.m_Height + high.m_Radius * high.m_Radius);
            freeLook.m_Orbits[0].m_Height = distance * Mathf.Sin(HighOrbitDown * Mathf.Deg2Rad);
            freeLook.m_Orbits[0].m_Radius = distance * Mathf.Cos(HighOrbitDown * Mathf.Deg2Rad);

            // Downward angle along the (new) orbit curve, for angle -> Y.
            for (int i = 0; i < Samples; i++)
            {
                Vector3 p = freeLook.GetLocalPositionForCameraFromInput(i / (float)(Samples - 1));
                curveAngle[i] = Mathf.Atan2(p.y, new Vector2(p.x, p.z).magnitude) * Mathf.Rad2Deg;
            }
            controlling = true;
            var curve = new System.Text.StringBuilder();
            int turnsBack = 0;
            for (int i = 0; i < Samples; i++)
            {
                if (i % 5 == 0) curve.Append($" {curveAngle[i]:F1}");
                if (i > 0 && curveAngle[i] < curveAngle[i - 1] - 1e-3f) turnsBack++;
            }
            McLog.Info($"camera: orbit curve angle every 0.05 of Y:{curve}; points where it turns back: {turnsBack}");
            McLog.Info($"camera: following Minecraft's look (Mycelium mouse input off: axis names were '{savedXInput}'/'{savedYInput}', input provider unplugged). " +
                       $"High orbit raised from {savedOrbits[0].m_Height:F2}/{savedOrbits[0].m_Radius:F2} to {freeLook.m_Orbits[0].m_Height:F2}/{freeLook.m_Orbits[0].m_Radius:F2} (height/radius); " +
                       $"orbit curve looks down {curveAngle[0]:F1}..{curveAngle[Samples / 2]:F1}..{curveAngle[Samples - 1]:F1} degrees at Y 0/0.5/1; " +
                       $"Minecraft pitch -90/0/90 -> camera {PitchToAngle(-90):F0}/{PitchToAngle(0):F0}/{PitchToAngle(90):F0} degrees");
        }

        /// <summary>Test aid: per-frame camera numbers to camera_trace.csv next to the exe.</summary>
        void WriteTrace(Transform t, float downBeforeTilt, float unshakenYaw)
        {
            if (trace == null)
            {
                if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-mcCameraTrace") < 0) return;
                string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "camera_trace.csv"));
                trace = new System.IO.StreamWriter(path, false);
                trace.WriteLine("frame,time,mcFrame,pitch,wantDown,y,camX,camY,camZ,downBeforeTilt,tilt,targetY,mode,viewYaw,shakePos,shakeAngle,offAim,sporeYaw,sporeX,sporeY,sporeZ,targetX,targetZ,collisionPush");
                McLog.Info($"camera: tracing every frame to {path}");
            }
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Vector3 p = t.position;
            Transform follow = freeLook.Follow;
            trace.WriteLine(string.Join(",", new[]
            {
                Time.frameCount.ToString(inv), Time.unscaledTime.ToString("F4", inv), lastMcFrame.ToString(inv),
                lastPitch.ToString("F3", inv), wantDown.ToString("F3", inv), freeLook.m_YAxis.Value.ToString("F4", inv),
                p.x.ToString("F4", inv), p.y.ToString("F4", inv), p.z.ToString("F4", inv),
                downBeforeTilt.ToString("F3", inv), lastTilt.ToString("F3", inv),
                (follow != null ? follow.position.y : float.NaN).ToString("F4", inv),
                mode.ToString(inv), t.eulerAngles.y.ToString("F3", inv),
                lastShakePos.ToString("F4", inv), lastShakeAngle.ToString("F3", inv),
                // How far the final view is from the wanted, unshaken direction: the visible shake
                // (about 0 when calm, about shakeAngle while shaking).
                Vector3.Angle(t.forward, Quaternion.Euler(wantDown, unshakenYaw, 0f) * Vector3.forward).ToString("F3", inv),
                (spore != null ? spore.eulerAngles.y : float.NaN).ToString("F3", inv),
                (spore != null ? spore.position.x : float.NaN).ToString("F4", inv),
                (spore != null ? spore.position.y : float.NaN).ToString("F4", inv),
                (spore != null ? spore.position.z : float.NaN).ToString("F4", inv),
                (follow != null ? follow.position.x : float.NaN).ToString("F4", inv),
                (follow != null ? follow.position.z : float.NaN).ToString("F4", inv),
                (shake != null ? shake.CollisionPush.magnitude : 0f).ToString("F4", inv),
            }));
        }

        void Release(string why)
        {
            if (freeLook != null)
            {
                freeLook.m_XAxis.m_InputAxisName = savedXInput;
                freeLook.m_YAxis.m_InputAxisName = savedYInput;
                freeLook.UpdateInputAxisProvider(); // plug Mycelium's mouse provider back in
                if (savedOrbits != null) freeLook.m_Orbits = savedOrbits;
                if (collider != null) collider.m_DampingWhenOccluded = savedOccludedDamping;
                if (savedFov > 0f) freeLook.m_Lens.FieldOfView = savedFov;
            }
            savedFov = 0f;
            fovScale = eyeScale = 1f;
            savedOrbits = null;
            controlling = false;
            notDrivingSince = -1f;
            lastTilt = 0f;
            ShowSpore();
            sporeHidden = 0;
            mode = Behind;
            McLog.Info($"camera: back to Mycelium's mouse and orbits ({why})");
        }

        /// <summary>Cinemachine has just placed the camera: tilt it to exactly the wanted angle.</summary>
        void AfterCinemachine(CinemachineBrain brain)
        {
            if (!controlling || freeLook == null || brain == null || brain.OutputCamera == null) return;
            if (brain.IsBlending || !ReferenceEquals(brain.ActiveVirtualCamera, freeLook))
            {
                lastTilt = 0f; // a cutscene / reset camera is live: leave it alone
                return;
            }
            Transform t = brain.OutputCamera.transform;
            Vector3 shakePos = shake != null ? shake.Position : Vector3.zero;
            Quaternion shakeRot = shake != null ? shake.Rotation : Quaternion.identity;
            lastShakePos = shakePos.magnitude;
            lastShakeAngle = Quaternion.Angle(Quaternion.identity, shakeRot);
            if (mode == FirstPerson && spore != null)
            {
                // Out of the spore's eyes, exactly where Minecraft looks (eased), plus Mycelium's
                // shake. The listener put its offset in the orbit camera's space; move it into the
                // eye camera's space so it shakes the same way relative to the view.
                Quaternion eyeRot = Quaternion.Euler(wantDown, smoothYaw, 0f);
                Vector3 localShake = Quaternion.Inverse(t.rotation) * shakePos;
                t.SetPositionAndRotation(spore.position + Vector3.up * eyeOffset * eyeScale + eyeRot * localShake, eyeRot * shakeRot);
                if (Hurt != null) t.rotation *= Hurt.Rotation(smoothYaw);
                lastTilt = 0f;
                WriteTrace(t, wantDown, smoothYaw);
                return;
            }
            // Measure the view without the shake, so the tilt corrects only Cinemachine's aim and
            // leaves the shake on top (measuring the shaken view cancelled the shake's up/down).
            Vector3 unshaken = (t.rotation * Quaternion.Inverse(shakeRot)) * Vector3.forward;
            float down = -Mathf.Asin(Mathf.Clamp(unshaken.y, -1f, 1f)) * Mathf.Rad2Deg;
            lastTilt = wantDown - down;
            float unshakenYaw = Mathf.Atan2(unshaken.x, unshaken.z) * Mathf.Rad2Deg;
            // Positive rotation about the camera's own X axis tilts it down. Rotate about the
            // horizontal right axis so the heading stays the same.
            Vector3 right = Vector3.Cross(Vector3.up, t.forward);
            if (right.sqrMagnitude < 1e-6f) right = t.right;
            t.rotation = Quaternion.AngleAxis(lastTilt, right.normalized) * t.rotation;
            // Crouching: Minecraft's third-person camera turns around the eyes, so it drops as much as they do.
            t.position += Vector3.up * eyeOffset * (eyeScale - 1f);
            // Minecraft's hurt tilt, measured from where the player faces (in front view too, as Minecraft does).
            if (Hurt != null) t.rotation *= Hurt.Rotation(smoothYaw);
            WriteTrace(t, down, unshakenYaw);
        }
    }
}
