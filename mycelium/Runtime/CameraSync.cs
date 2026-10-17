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
    /// Everything is restored when Minecraft stops driving. Cutscene and "reset/navigate" cameras
    /// still take over as they normally do (no tilt is applied while another camera is live).
    /// </summary>
    sealed class CameraSync
    {
        const float HighOrbitDown = 80f; // degrees down for the raised high orbit
        const int Samples = 101;         // points along the orbit curve for angle -> Y

        CinemachineFreeLook freeLook;
        CinemachineFreeLook.Orbit[] savedOrbits;
        string savedXInput, savedYInput;
        bool controlling;
        bool listening;
        readonly float[] curveAngle = new float[Samples]; // downward angle at Y = i / (Samples - 1)
        float wantDown;    // wanted camera angle this frame (degrees, positive = down)
        float lastTilt;    // tilt applied after Cinemachine last frame
        float lastPitch;   // Minecraft pitch this frame (for the trace)
        float smoothPitch; // Minecraft pitch eased over CameraSmoothing seconds
        ulong lastMcFrame; // Minecraft frame counter this frame (for the trace)
        System.IO.StreamWriter trace; // "-mcCameraTrace": one line per rendered frame

        public float X => freeLook != null ? freeLook.m_XAxis.Value : float.NaN;
        public float Y => freeLook != null ? freeLook.m_YAxis.Value : float.NaN;
        public float WantDown => wantDown;
        public float LastTilt => lastTilt;
        public bool Controlling => controlling;

        /// <summary>Forget the camera (new scene; the old one is gone with its scene).</summary>
        public void Reset()
        {
            freeLook = null;
            controlling = false;
        }

        /// <summary>Call every frame. driving: Minecraft is driving the spore and mc is fresh.</summary>
        public void Update(bool driving, in McState mc)
        {
            if (!McConfig.SyncCamera) driving = false;
            if (freeLook == null && driving) Find();
            if (freeLook == null) return;

            if (!driving)
            {
                if (controlling) Release("Minecraft not driving");
                return;
            }
            if (!controlling)
            {
                Take();
                smoothPitch = mc.Pitch;
            }

            float heading = WorldMapping.MycYaw(mc.Yaw);
            if (heading > 180f) heading -= 360f;
            freeLook.m_XAxis.Value = heading;
            lastPitch = mc.Pitch;
            lastMcFrame = mc.Frame;
            // Minecraft sends a new pitch once per Minecraft frame; snapping to each one stutters
            // against Mycelium's own frame rate. Ease toward it instead (horizontal turning is
            // already smoothed by Cinemachine's own damping).
            float tau = McConfig.CameraSmoothing;
            smoothPitch = tau > 0f ? Mathf.Lerp(smoothPitch, mc.Pitch, 1f - Mathf.Exp(-Time.unscaledDeltaTime / tau)) : mc.Pitch;
            wantDown = PitchToAngle(smoothPitch);
            freeLook.m_YAxis.Value = AngleToY(wantDown);
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
            McLog.Info($"camera: found FreeLook on '{main.name}' (binding {freeLook.m_BindingMode})");
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
            savedXInput = freeLook.m_XAxis.m_InputAxisName;
            savedYInput = freeLook.m_YAxis.m_InputAxisName;
            freeLook.m_XAxis.m_InputAxisName = "";
            freeLook.m_YAxis.m_InputAxisName = "";
            freeLook.m_XAxis.m_InputAxisValue = 0;
            freeLook.m_YAxis.m_InputAxisValue = 0;

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
            McLog.Info($"camera: following Minecraft's look (Mycelium mouse input off; was '{savedXInput}'/'{savedYInput}'). " +
                       $"High orbit raised from {savedOrbits[0].m_Height:F2}/{savedOrbits[0].m_Radius:F2} to {freeLook.m_Orbits[0].m_Height:F2}/{freeLook.m_Orbits[0].m_Radius:F2} (height/radius); " +
                       $"orbit curve looks down {curveAngle[0]:F1}..{curveAngle[Samples / 2]:F1}..{curveAngle[Samples - 1]:F1} degrees at Y 0/0.5/1; " +
                       $"Minecraft pitch -90/0/90 -> camera {PitchToAngle(-90):F0}/{PitchToAngle(0):F0}/{PitchToAngle(90):F0} degrees");
        }

        /// <summary>Test aid: per-frame camera numbers to camera_trace.csv next to the exe.</summary>
        void WriteTrace(Transform t, float downBeforeTilt)
        {
            if (trace == null)
            {
                if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-mcCameraTrace") < 0) return;
                string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "camera_trace.csv"));
                trace = new System.IO.StreamWriter(path, false);
                trace.WriteLine("frame,time,mcFrame,pitch,wantDown,y,camX,camY,camZ,downBeforeTilt,tilt,targetY");
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
            }));
        }

        void Release(string why)
        {
            if (freeLook != null)
            {
                freeLook.m_XAxis.m_InputAxisName = savedXInput;
                freeLook.m_YAxis.m_InputAxisName = savedYInput;
                if (savedOrbits != null) freeLook.m_Orbits = savedOrbits;
            }
            savedOrbits = null;
            controlling = false;
            lastTilt = 0f;
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
            float down = -Mathf.Asin(Mathf.Clamp(t.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            lastTilt = wantDown - down;
            WriteTrace(t, down);
            // Positive rotation about the camera's own X axis tilts it down. Rotate about the
            // horizontal right axis so the heading stays the same.
            Vector3 right = Vector3.Cross(Vector3.up, t.forward);
            if (right.sqrMagnitude < 1e-6f) right = t.right;
            t.rotation = Quaternion.AngleAxis(lastTilt, right.normalized) * t.rotation;
        }
    }
}
