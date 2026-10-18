using System;
using System.Reflection;
using MyceliumCraft.Link;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;

namespace MyceliumCraft
{
    /// <summary>
    /// Phase 1: Minecraft walks on Mycelium's ground, and the spore follows Minecraft in X, Y and Z.
    ///
    /// Mycelium's code is not changed. This component starts itself before the first scene, finds
    /// Mycelium's PlayerController by name (reflection), and runs its FixedUpdate after
    /// PlayerController's (execution order 10000) so the velocity it sets wins.
    ///
    /// Each level: scan the ground around the spore and send it to Minecraft, teleport Minecraft's
    /// player onto the spore, wait until Minecraft confirms, then drive the spore from Minecraft.
    /// </summary>
    [Preserve]
    [DefaultExecutionOrder(10000)]
    public sealed class MinecraftDriver : MonoBehaviour
    {
        // How hard the spore is pulled toward where Minecraft says it should be (1/s), and caps.
        const float Gain = 12f;
        const float MaxSpeed = 40f;
        // If Mycelium's own collision holds the spore further than this from Minecraft's player,
        // move Minecraft's player back onto the spore (Mycelium knows something Minecraft doesn't).
        const float ResyncDistance = 6f;
        // ...or if they disagree by more than StuckDistance for StuckSeconds in a row (wedged
        // somewhere only one game thinks is open, which the 6-unit rule never notices).
        const float StuckDistance = 1.5f;
        const float StuckSeconds = 2f;
        // Gaps under one-sided surfaces smaller than this many spore heights are filled (GroundScanner).
        const float FillBelowSporeHeights = 2.5f;
        // Rescan the ground when the spore is this many blocks from the grid's centre (grid half-width is 32).
        const double RescanBlocks = 16;
        // PlayerController adds this acceleration itself every physics step (gravityForce = -25).
        static readonly Vector3 MyceliumGravity = new Vector3(0f, -25f, 0f);

        enum Phase { Idle, WaitingForTeleport, Driving }

        static Type playerControllerType;
        static FieldInfo rbField;
        static FieldInfo animatorField;
        static FieldInfo moveSpeedField;
        static FieldInfo canActField;
        static FieldInfo lookingField;
        bool lookingOff; // we switched PlayerController.looking off
        static FieldInfo actionsField;
        static PropertyInfo pausedProperty;

        MyceliumLink link;
        WorldMapping map;
        readonly GroundScanner ground = new GroundScanner();
        readonly CameraSync cameraSync = new CameraSync();
        readonly InputBridge input = new InputBridge();
        readonly KeyOwnership keys = new KeyOwnership();
        readonly McOverlay overlay = new McOverlay();
        readonly McPlayerModel playerModel = new McPlayerModel();
        readonly uint[] bindKinds = new uint[Proto.BMax];
        readonly uint[] bindCodes = new uint[Proto.BMax];
        bool heldByMycelium;   // Mycelium has the controls (menu, pause, cutscene): don't drive the spore
        bool checkAfterHold;   // just got the controls back: resync if Mycelium moved the spore
        string controlsWhy = "";
        bool needScan = true;
        Component controller;
        Rigidbody rb;
        Animator animator;
        float nextFindTime;
        ulong frame;
        bool mcWasAlive;
        bool driving;
        Phase phase = Phase.Idle;

        McState mc;
        uint lastEpoch;
        Teleport tp;
        float tpSentTime;
        float feetOffset; // rb.position.y minus the bottom of the spore's collider
        float playerHeightBlocks; // what Minecraft's player height should be (0 = leave it)
        bool haveLastMc;
        double lastMcX, lastMcY, lastMcZ;
        Vector3 mcVel; // Minecraft velocity mapped to Mycelium units/s, smoothed
        float mcSpeed; // Minecraft horizontal speed, blocks/s, smoothed
        Vector3 lastTarget;
        float lastError;
        // Brief interruptions (a torn read, one "not in a world" frame) are bridged with the last
        // good state instead of dropping everything: that dropped the camera to Mycelium's third
        // person for a frame (seen at 20:21:12 in your log).
        const float HiccupSeconds = 0.25f;
        float lastGoodMcTime = -1f;
        McState lastCamMc;
        float lastCamMcTime = -1f;
        int hiccups;
        float nextHiccupLog;
        // Clock: Mycelium's unscaled time <-> Windows QueryPerformanceCounter, read at
        // each frame, so a physics step's time can be turned into Minecraft's clock.
        long frameQpc;
        double frameUnscaled;
        float lastAlpha; // ticks past the newest stored tick point at the last physics step (log; negative = inside history)
        readonly TickHistory ticks = new TickHistory();
        int tickSteps, fallbackSteps; // physics steps that used tick timing / the fallback (log)
        float stuckSince = -1f; // when error first went above StuckDistance, or -1
        int resyncs;
        float nextLogTime;
        bool shakeTest;
        bool holdTest;
        bool hitTest;
        float nextHitTime;
        GameObject fakeHitter;
        bool mouseTest;
        readonly System.Collections.Generic.List<UnityEngine.InputSystem.Key> heldTestKeys = new System.Collections.Generic.List<UnityEngine.InputSystem.Key>();
        float holdTestStart = -1f;
        int holdTestStep;
        float nextShakeTime;
        int framesSinceLog;

        [Preserve]
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("MyceliumCraft");
            DontDestroyOnLoad(go);
            go.AddComponent<MinecraftDriver>();
        }

        void Awake()
        {
            McLog.Open();
            BackgroundWindow.Init();
            McConfig.Load();
            map = new WorldMapping(McConfig.Scale);
            // Teleport numbers start from the clock, so a restarted Mycelium never reuses a number a
            // still-running Minecraft has already applied.
            tp.Seq = (uint)Environment.TickCount & 0x7FFFFF00u;
            // Mycelium normally freezes when its window loses focus. Minecraft has the focus while
            // you play, so keep running.
            Application.runInBackground = true;

            playerControllerType = Type.GetType("PlayerController, Assembly-CSharp");
            if (playerControllerType != null)
            {
                rbField = playerControllerType.GetField("rb", BindingFlags.Public | BindingFlags.Instance);
                animatorField = playerControllerType.GetField("animator", BindingFlags.Public | BindingFlags.Instance);
                moveSpeedField = playerControllerType.GetField("moveSpeed", BindingFlags.Public | BindingFlags.Instance);
                canActField = playerControllerType.GetField("canAct", BindingFlags.Public | BindingFlags.Instance);
                lookingField = playerControllerType.GetField("looking", BindingFlags.Public | BindingFlags.Instance);
                actionsField = playerControllerType.GetField("playerActionsAsset", BindingFlags.Public | BindingFlags.Instance);
            }
            Type globalData = Type.GetType("GlobalData, Assembly-CSharp");
            pausedProperty = globalData?.GetProperty("isGamePaused", BindingFlags.Public | BindingFlags.Static);
            McLog.Info($"loaded, protocol v{Proto.Version}. PlayerController found={playerControllerType != null} rb={rbField != null} animator={animatorField != null} moveSpeed={moveSpeedField != null} GlobalData.isGamePaused={pausedProperty != null}");

            try
            {
                link = MyceliumLink.Create();
                McLog.Info($"shared memory {Proto.MappingName} created (already existed={link.AlreadyExisted})");
            }
            catch (Exception e)
            {
                McLog.Info("FAILED to create shared memory: " + e.Message);
            }

            // Test aid: "-mcScene <name>" skips the menus and loads that level directly.
            string[] args = Environment.GetCommandLineArgs();
            int sceneArg = Array.IndexOf(args, "-mcScene");
            string directScene = sceneArg >= 0 && sceneArg + 1 < args.Length ? args[sceneArg + 1] : null;

            // Test aid: "-mcShakeTest" fires a level's own camera shake every 3 s while Minecraft drives.
            shakeTest = Array.IndexOf(args, "-mcShakeTest") >= 0;
            // Test aid: "-mcHoldTest" takes the controls away the way Mycelium's menus do
            // (PlayerController.DisableController) 3 s into driving, and gives them back 3 s later.
            holdTest = Array.IndexOf(args, "-mcHoldTest") >= 0;
            // Test aid: "-mcHitTest" knocks the spore back every 4 s, as an enemy hit does
            // (PlayerController.Knockback, from in front of the spore).
            hitTest = Array.IndexOf(args, "-mcHitTest") >= 0;
            // Test aid: "-mcOverlayCheck" reads back Mycelium's finished screen once a second where
            // the fake Minecraft's test picture (LINKTEST_OVERLAY=1) has its boxes, switching the
            // overlay on and off each time, and logs the colors.
            if (Array.IndexOf(args, "-mcOverlayCheck") >= 0) StartCoroutine(OverlayCheck());
            // Test aid: "-mcModelCheck" reads back Mycelium's finished screen once a second at the
            // Minecraft body's head, with its depth passes off and on in turn, and logs the colour.
            if (Array.IndexOf(args, "-mcModelCheck") >= 0) StartCoroutine(ModelCheck());
            // Test aid: "-mcBlurCheck" logs how sharp Mycelium's finished screen is twice a second
            // (average difference between neighbouring pixels in the middle), to see the pause blur.
            if (Array.IndexOf(args, "-mcBlurCheck") >= 0) StartCoroutine(BlurCheck());
            // Test aid: "-mcHoldKeys wasd..." makes Mycelium believe those keys are held (through
            // Unity's Input System), as real keys in Mycelium's focused window would be.
            // Test aid: "-mcMouseTest" moves the mouse up and down (through Unity's Input System), as
            // your real mouse does in Mycelium's focused window.
            mouseTest = Array.IndexOf(args, "-mcMouseTest") >= 0;
            if (mouseTest)
            {
                UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
                McLog.Info("-mcMouseTest: moving the mouse up and down once Minecraft drives");
            }
            int keysArg = Array.IndexOf(args, "-mcHoldKeys");
            if (keysArg >= 0 && keysArg + 1 < args.Length)
            {
                // Letters ("wasd") or comma-separated Input System key names ("space,leftShift,tab").
                string spec = args[keysArg + 1];
                if (spec.Contains(","))
                {
                    foreach (string name in spec.Split(','))
                        if (Enum.TryParse(name.Trim(), true, out UnityEngine.InputSystem.Key k)) heldTestKeys.Add(k);
                }
                else
                {
                    foreach (char c in spec.ToLowerInvariant())
                        if (c >= 'a' && c <= 'z') heldTestKeys.Add(UnityEngine.InputSystem.Key.A + (c - 'a'));
                }
                // The test window usually isn't focused; let the Input System take events anyway.
                UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
                McLog.Info($"-mcHoldKeys: holding {string.Join(",", heldTestKeys)} once Minecraft drives");
            }

            // Test aid: "-mcFps <n>" caps Mycelium's frame rate (re-applied per scene, which may set its own).
            int fpsArg = Array.IndexOf(args, "-mcFps");
            int testFps = fpsArg >= 0 && fpsArg + 1 < args.Length && int.TryParse(args[fpsArg + 1], out int f) ? f : 0;

            SceneManager.sceneLoaded += (scene, mode) =>
            {
                if (testFps > 0)
                {
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = testFps;
                }
                if (directScene != null && scene.name != directScene)
                {
                    McLog.Info($"-mcScene: loading '{directScene}' directly (test aid)");
                    string next = directScene;
                    directScene = null;
                    SceneManager.LoadScene(next);
                }
                McLog.Info($"scene loaded: '{scene.name}'");
                controller = null;
                rb = null;
                nextFindTime = 0;
                cameraSync.Reset();
                SetPhase(Phase.Idle, "new scene");
            };
        }

        void Update()
        {
            BackgroundWindow.Update();
            frameQpc = MyceliumLink.Qpc();
            frameUnscaled = Time.unscaledTimeAsDouble;
            if (link == null) return;
            frame++;
            framesSinceLog++;
            link.Beat();
            FindPlayer();

            uint flags = 0;
            Vector3 pos = Vector3.zero;
            float yaw = 0;
            if (rb != null)
            {
                flags |= Proto.MycInLevel;
                pos = rb.position;
                yaw = rb.rotation.eulerAngles.y;
            }
            if (IsPaused()) flags |= Proto.MycPaused;
            if (McConfig.Overlay && rb != null) flags |= Proto.MycOverlay;
            link.WriteMycState(flags, pos.x, pos.y, pos.z, yaw, frame, tp, rb != null ? playerHeightBlocks : 0f);

            bool alive = link.MinecraftAlive;
            if (alive != mcWasAlive)
            {
                mcWasAlive = alive;
                McLog.Info(alive ? $"Minecraft linked (pid {link.MinecraftPid})" : "Minecraft gone (heartbeat stopped); Mycelium controls the spore again");
            }

            // Camera: every rendered frame (not the 50 Hz physics step), so turning stays smooth.
            // The camera keeps following Minecraft through teleports (WaitingForTeleport) and brief
            // hiccups: releasing it, even for one frame, showed Mycelium's third-person camera
            // (seen after a beetle's knockback, at 20:22:23 in your log).
            if (alive && link.TryReadMcState(out McState camFresh) && camFresh.InWorld)
            {
                lastCamMc = camFresh;
                lastCamMcTime = Time.realtimeSinceStartup;
            }
            bool camFresh2 = lastCamMcTime >= 0f && Time.realtimeSinceStartup - lastCamMcTime <= HiccupSeconds;
            bool look = alive && phase != Phase.Idle && camFresh2;
            cameraSync.Update(look, lastCamMc);

            // Input bridge: Minecraft plays from Mycelium's keyboard and mouse during normal play.
            // In test runs (-mcBackground) the bridge never uses your keyboard and mouse, even if
            // Windows hands Mycelium the focus.
            uint reason = !McConfig.InputBridge || BackgroundWindow.Enabled ? Proto.ReasonOff
                        : !Application.isFocused ? Proto.ReasonNotFocused
                        : !(alive && phase == Phase.Driving) ? Proto.ReasonNotDriving
                        : MyceliumHasControls(out _) ? Proto.ReasonMyceliumControls
                        : Proto.ReasonActive;
            // Mycelium's key binds, as set in Minecraft's Controls menu (kept if Minecraft goes away).
            if (alive)
            {
                int bindCount = link.TryReadBindings(bindKinds, bindCodes);
                if (bindCount >= 0) keys.SetBindings(bindCount, bindKinds, bindCodes);
            }
            else if (!keys.HasBindings) keys.SetBindings(0, null, null); // defaults until Minecraft sends some
            bool mcScreenOpen = camFresh2 && (lastCamMc.Flags & Proto.McScreenOpen) != 0;
            input.Update(link, reason == Proto.ReasonActive, reason, keys.OwnedScancodes, keys.OwnedButtons, mcScreenOpen);
            // Minecraft's hand, HUD and screens on top, during play (not over Mycelium's own menus).
            overlay.Update(link, McConfig.Overlay && alive && phase != Phase.Idle && !MyceliumHasControls(out _));
            // Minecraft's pause menu (Escape) pauses Mycelium too.
            FreezeForMinecraftPause(alive && phase == Phase.Driving && camFresh2 && (lastCamMc.Flags & Proto.McPaused) != 0);
            pauseBlur.Update();
            // Third person: your Minecraft body at the spore's feet, instead of the spore.
            bool bodyWanted = McConfig.Overlay && alive && phase != Phase.Idle && rb != null && cameraSync.Controlling;
            playerModel.Update(link, bodyWanted, rb != null ? rb.transform.position - Vector3.up * feetOffset : Vector3.zero, (float)map.Scale);
            cameraSync.ReplaceSpore = playerModel.Shown;
            // Which game owns which key, while Minecraft drives the spore.
            keys.Update(alive && phase == Phase.Driving, mcScreenOpen);
            if (shakeTest && look && Time.realtimeSinceStartup >= nextShakeTime) FireTestShake();
            if (holdTest && controller != null && (phase == Phase.Driving || holdTestStep > 0)) RunHoldTest();
            if (hitTest && controller != null && phase == Phase.Driving && Time.realtimeSinceStartup >= nextHitTime) RunHitTest();
            if (mouseTest && phase == Phase.Driving && UnityEngine.InputSystem.Mouse.current != null)
            {
                // About what a hand does: up and down, 20 px per frame at most, 1.5 s period.
                float dy = 20f * Mathf.Sin(Time.unscaledTime * 2f * Mathf.PI / 1.5f);
                var ms = new UnityEngine.InputSystem.LowLevel.MouseState { delta = new Vector2(0f, dy) };
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(UnityEngine.InputSystem.Mouse.current, ms);
            }
            if (heldTestKeys.Count > 0 && UnityEngine.InputSystem.Keyboard.current != null)
            {
                var state = phase == Phase.Driving
                    ? new UnityEngine.InputSystem.LowLevel.KeyboardState(heldTestKeys.ToArray())
                    : new UnityEngine.InputSystem.LowLevel.KeyboardState();
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(UnityEngine.InputSystem.Keyboard.current, state);
            }

            if (Time.realtimeSinceStartup >= nextLogTime)
            {
                // FixedUpdate only reads Minecraft while a spore exists; read here too so the log
                // shows Minecraft's numbers in menus.
                if (alive && !driving) link.TryReadMcState(out mc);
                LogStatus(pos);
                nextLogTime = Time.realtimeSinceStartup + 1f;
                framesSinceLog = 0;
            }
        }

        /// <summary>Switch Mycelium's own "face where I walk" (PlayerController.looking) off while Minecraft drives, and back on afterwards.</summary>
        void SetMyceliumTurning(bool on)
        {
            if (controller == null || lookingField == null) return;
            if (!on)
            {
                if ((bool)lookingField.GetValue(controller)) lookingField.SetValue(controller, false);
                if (!lookingOff) McLog.Info("facing: Mycelium's own turning (PlayerController.looking) off while Minecraft drives");
                lookingOff = true;
            }
            else if (lookingOff)
            {
                lookingField.SetValue(controller, true);
                lookingOff = false;
                McLog.Info("facing: Mycelium's own turning back on");
            }
        }

        void FixedUpdate()
        {
            if (!driving) SetMyceliumTurning(true);
            driving = false;
            if (link == null || rb == null) return;
            bool alive = link.MinecraftAlive;
            McState fresh = default;
            bool read = alive && link.TryReadMcState(out fresh) && fresh.InWorld;
            if (read)
            {
                mc = fresh;
                lastGoodMcTime = Time.realtimeSinceStartup;
            }
            else if (!alive || lastGoodMcTime < 0f || Time.realtimeSinceStartup - lastGoodMcTime > HiccupSeconds)
            {
                SetPhase(Phase.Idle, alive ? "Minecraft not in a world" : "Minecraft gone");
                return;
            }
            else
            {
                // A hiccup: keep going with Minecraft's last good state.
                hiccups++;
                if (Time.realtimeSinceStartup >= nextHiccupLog)
                {
                    nextHiccupLog = Time.realtimeSinceStartup + 1f;
                    McLog.Info($"hiccup: Minecraft's state unreadable or 'not in a world' for a moment ({hiccups} so far); using its last good state");
                }
            }
            if (IsPaused()) return;

            Vector3 pos = rb.position;
            map.ToMc(pos.x, pos.y, pos.z, out double sporeMcX, out _, out double sporeMcZ);
            if (needScan || Math.Abs(sporeMcX - ground.CenterX) > RescanBlocks || Math.Abs(sporeMcZ - ground.CenterZ) > RescanBlocks)
            {
                needScan = false;
                string summary = ground.Scan(pos, map);
                link.WriteGround(ground.Version, GroundScanner.N, ground.OriginX, ground.OriginZ, GroundScanner.CellBlocks, ground.Pieces);
                McLog.Info(summary);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-mcDumpGrid") >= 0) DumpGrid();
                // Test aid: "-mcColumn x,z;x,z" logs what the scan finds in those columns (Minecraft coordinates).
                string[] cmd = Environment.GetCommandLineArgs();
                int colArg = Array.IndexOf(cmd, "-mcColumn");
                if (colArg >= 0 && colArg + 1 < cmd.Length)
                    foreach (string xz in cmd[colArg + 1].Split(';'))
                    {
                        string[] p = xz.Split(',');
                        if (p.Length == 2 && double.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double cx)
                                          && double.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double cz))
                            McLog.Info(ground.DescribeColumn(cx, cz, pos, map));
                    }
            }

            if (phase == Phase.Idle || mc.Epoch != lastEpoch)
            {
                bool newWorld = phase != Phase.Idle;
                lastEpoch = mc.Epoch;
                SendTeleport(newWorld ? "Minecraft entered a new world" : "start");
                return;
            }

            if (phase == Phase.WaitingForTeleport)
            {
                if (mc.TeleportAck != tp.Seq) return;
                // Belt and braces: wait (up to 2 s) until Minecraft's reported position is really there.
                double off = Math.Sqrt((mc.X - tp.X) * (mc.X - tp.X) + (mc.Y - tp.Y) * (mc.Y - tp.Y) + (mc.Z - tp.Z) * (mc.Z - tp.Z));
                if (off > 2 && Time.realtimeSinceStartup - tpSentTime < 2f) return;
                McLog.Info($"teleport {tp.Seq} confirmed by Minecraft after {Time.realtimeSinceStartup - tpSentTime:F2} s: mc=({mc.X:F2}, {mc.Y:F2}, {mc.Z:F2})");
                SetPhase(Phase.Driving, "teleport confirmed");
                stuckSince = -1f;
                ticks.Clear(); // the teleport is a jump; don't interpolate across it
                haveLastMc = false;
                mcVel = Vector3.zero;
            }

            // Mycelium has the controls (its menu, pause or a cutscene): leave the spore to it. This
            // comes before the distance checks, so Mycelium moving the spore isn't taken for a
            // disagreement.
            if (MyceliumHasControls(out string why))
            {
                if (!heldByMycelium || why != controlsWhy)
                {
                    heldByMycelium = true;
                    controlsWhy = why;
                    McLog.Info($"hold: Mycelium has the controls ({why}); the spore is Mycelium's until it gives them back");
                }
                haveLastMc = false;
                stuckSince = -1f;
                return;
            }
            if (heldByMycelium)
            {
                heldByMycelium = false;
                checkAfterHold = true;
                McLog.Info("hold: Mycelium gave the controls back; Minecraft drives the spore again");
            }

            // Smoothed Minecraft velocity: the walk animation, the log, and feed-forward so the
            // spore moves with Minecraft instead of trailing behind it.
            if (haveLastMc)
            {
                double dx = mc.X - lastMcX, dy = mc.Y - lastMcY, dz = mc.Z - lastMcZ;
                mcSpeed = Mathf.Lerp(mcSpeed, (float)Math.Sqrt(dx * dx + dz * dz) / Time.fixedDeltaTime, 0.2f);
                var vNow = new Vector3((float)dx, (float)dy, (float)-dz) * (float)(map.Scale / Time.fixedDeltaTime);
                mcVel = Vector3.Lerp(mcVel, vNow, 0.3f);
            }
            haveLastMc = true;
            lastMcX = mc.X;
            lastMcY = mc.Y;
            lastMcZ = mc.Z;

            // Where Minecraft's player is at the moment this physics step stands for, worked out from
            // Minecraft's raw ticks and their time, exactly as Minecraft's renderer does. Reading the
            // latest interpolated position instead aliased against the physics steps (Unity runs 0,
            // 1 or 2 per frame, often back to back): the spore moved in uneven steps and the camera,
            // which follows it, made the world judder, worst when looking down at the ground.
            float bodyYaw = mc.BodyYaw;
            double mx = mc.X, my = mc.Y, mz = mc.Z;
            ticks.Observe(mc, MyceliumLink.QpcFrequency);
            if (mc.HasTicks && frameQpc != 0 && ticks.Count > 0)
            {
                // The moment this physics step stands for, on Minecraft's clock. Unity runs physics a
                // little behind the frame, so this is often before Minecraft's latest tick; the
                // history of recent ticks covers that (only the latest two did not: the first try
                // froze the spore between ticks).
                long stepQpc = frameQpc + (long)((Time.fixedUnscaledTimeAsDouble - frameUnscaled) * MyceliumLink.QpcFrequency);
                lastAlpha = (float)ticks.Sample(stepQpc, out mx, out my, out mz, out bodyYaw);
                double tdx = mc.CurX - mc.PrevX, tdz = mc.CurZ - mc.PrevZ;
                mcSpeed = (float)(Math.Sqrt(tdx * tdx + tdz * tdz) / (mc.TickMs / 1000.0));
                tickSteps++;
            }
            else
            {
                fallbackSteps++;
            }
            map.ToMyc(mx, my, mz, out double tx, out double ty, out double tz);
            Vector3 target = new Vector3((float)tx, (float)ty + feetOffset, (float)tz);
            Vector3 error = target - pos;
            lastError = error.magnitude;
            lastTarget = target;
            if (checkAfterHold)
            {
                checkAfterHold = false;
                if (lastError > 0.5f)
                {
                    resyncs++;
                    SendTeleport($"Mycelium moved the spore {lastError:F1} units while it had the controls");
                    return;
                }
            }
            if (lastError > ResyncDistance)
            {
                resyncs++;
                stuckSince = -1f;
                SendTeleport($"spore {lastError:F1} units from Minecraft's player (Mycelium collision?)");
                return;
            }
            if (lastError <= StuckDistance) stuckSince = -1f;
            else if (stuckSince < 0) stuckSince = Time.time;
            else if (Time.time - stuckSince >= StuckSeconds)
            {
                resyncs++;
                stuckSince = -1f;
                SendTeleport($"spore and Minecraft's player {lastError:F1} units apart for {StuckSeconds} s (one is wedged): mc=({mc.X:F2}, {mc.Y:F2}, {mc.Z:F2})");
                return;
            }

            // With tick timing the target is exact, so land on it this step; without (an older
            // Minecraft side), the old smoothed pull.
            rb.velocity = mc.HasTicks && ticks.Count > 0
                ? Vector3.ClampMagnitude(error / Time.fixedDeltaTime, MaxSpeed)
                : Vector3.ClampMagnitude(mcVel + error * Gain, MaxSpeed);
            rb.AddForce(-MyceliumGravity, ForceMode.Acceleration); // cancel PlayerController's gravity: Minecraft does gravity now
            if (rb.useGravity) rb.AddForce(-Physics.gravity, ForceMode.Acceleration);
            // Face where Minecraft's body faces (it turns toward the walk when strafing). Mycelium's own
            // PlayerController.LookAt also turns the spore toward its WASD input; with the input
            // bridge it gets WASD too, and the two fought (the spore flickered when not walking
            // forward), so its turning is switched off while Minecraft drives.
            SetMyceliumTurning(false);
            rb.MoveRotation(Quaternion.Euler(0f, WorldMapping.MycYaw(bodyYaw), 0f));
            if (animator != null) animator.SetBool("Walk", mcSpeed > 0.5f);
            driving = true;
        }

        /// <summary>Test aid ("-mcDumpGrid"): the grid exactly as sent to Minecraft, to grid_v{N}.csv next to the exe.</summary>
        void DumpGrid()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", $"grid_v{ground.Version}.csv"));
            using (var w = new System.IO.StreamWriter(path, false))
            {
                w.WriteLine($"# n={GroundScanner.N} originX={ground.OriginX.ToString(inv)} originZ={ground.OriginZ.ToString(inv)} cell={GroundScanner.CellBlocks.ToString(inv)} fillBelow={ground.FillBelowOneSided.ToString(inv)}");
                for (int c = 0; c < GroundScanner.N * GroundScanner.N; c++)
                {
                    var line = new System.Text.StringBuilder();
                    line.Append(c % GroundScanner.N).Append(',').Append(c / GroundScanner.N);
                    for (int k = 0; k < Proto.GFloatsPerCell; k++)
                        line.Append(',').Append(ground.Pieces[c * Proto.GFloatsPerCell + k].ToString("R", inv));
                    w.WriteLine(line.ToString());
                }
            }
            McLog.Info($"grid dumped to {path}");
        }

        /// <summary>Ask Minecraft to put its player where the spore stands.</summary>
        void SendTeleport(string why)
        {
            Vector3 pos = rb.position;
            float feet = FeetY(pos.y);
            feetOffset = pos.y - feet;
            map.ToMc(pos.x, feet, pos.z, out double x, out double y, out double z);
            float groundY = ground.GroundAt(x, z, (float)y);
            if (!float.IsNaN(groundY) && groundY > y - 2 && groundY < y + 2) y = Math.Max(y, groundY);
            tp.Seq++;
            tp.X = x;
            tp.Y = y + 0.01;
            tp.Z = z;
            tp.Yaw = WorldMapping.McYaw(rb.rotation.eulerAngles.y);
            tpSentTime = Time.realtimeSinceStartup;
            SetPhase(Phase.WaitingForTeleport, why);
            McLog.Info($"teleport {tp.Seq} ({why}): spore=({pos.x:F2}, {pos.y:F2}, {pos.z:F2}) feet y={feet:F2} (offset {feetOffset:F2}) -> mc=({tp.X:F2}, {tp.Y:F2}, {tp.Z:F2}) yaw={tp.Yaw:F0}, ground there={groundY:F2}");
        }

        /// <summary>Bottom of the spore's own (non-trigger) colliders, or its position if it has none.</summary>
        float FeetY(float fallback)
        {
            float min = float.MaxValue;
            foreach (Collider c in rb.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger || !c.enabled || c.attachedRigidbody != rb) continue;
                min = Mathf.Min(min, c.bounds.min.y);
            }
            return min == float.MaxValue ? fallback : min;
        }

        /// <summary>
        /// Height of the spore's body: its own non-trigger colliders whose layer collides with the
        /// ground layers (others pass through the level, so they don't limit where it fits).
        /// Logs every collider it looked at. 0 if none.
        /// </summary>
        float SporeHeight()
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (Collider c in rb.GetComponentsInChildren<Collider>())
            {
                if (c.attachedRigidbody != rb) continue;
                int layer = c.gameObject.layer;
                bool hitsGround = false;
                for (int l = 0; l < 32; l++)
                    if ((ground.LayerMask & (1 << l)) != 0 && !Physics.GetIgnoreLayerCollision(layer, l)) { hitsGround = true; break; }
                bool counts = !c.isTrigger && c.enabled && hitsGround;
                McLog.Info($"  spore collider '{c.name}' {c.GetType().Name} layer={LayerMask.LayerToName(layer)} trigger={c.isTrigger} enabled={c.enabled} " +
                           $"y {c.bounds.min.y:F2}..{c.bounds.max.y:F2} ({c.bounds.size.y:F2} tall) -> {(counts ? "counts" : "ignored")}");
                if (!counts) continue;
                min = Mathf.Min(min, c.bounds.min.y);
                max = Mathf.Max(max, c.bounds.max.y);
            }
            return min == float.MaxValue ? 0f : max - min;
        }

        void SetPhase(Phase next, string why)
        {
            if (phase == next) return;
            McLog.Info($"phase {phase} -> {next} ({why})");
            phase = next;
        }

        void FindPlayer()
        {
            if (controller != null || playerControllerType == null || Time.realtimeSinceStartup < nextFindTime) return;
            nextFindTime = Time.realtimeSinceStartup + 0.5f;
            controller = (Component)FindObjectOfType(playerControllerType);
            rb = null;
            animator = null;
            if (controller == null) return;
            rb = rbField?.GetValue(controller) as Rigidbody ?? controller.GetComponentInChildren<Rigidbody>();
            animator = animatorField?.GetValue(controller) as Animator;
            if (rb == null) return;
            ground.ChooseLayers(rb.gameObject.layer);
            float sporeHeight = SporeHeight();
            ground.FillBelowOneSided = FillBelowSporeHeights * (sporeHeight > 0.1f ? sporeHeight : 1.7f);
            // First person looks out at 90% of the body height (Minecraft: eyes 1.62 of 1.8).
            float eyeAbovePosition = FeetY(rb.position.y) + 0.9f * (sporeHeight > 0.1f ? sporeHeight : 1.7f) - rb.position.y;
            cameraSync.SetSpore(rb.transform, eyeAbovePosition);
            McLog.Info($"camera: first-person eye {eyeAbovePosition:F2} units above the spore's position");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-mcNoFill") >= 0) ground.FillBelowOneSided = 0f; // test aid: compare without the fill
            playerHeightBlocks = McConfig.MatchSporeSize && sporeHeight > 0.1f ? (float)(sporeHeight / map.Scale) : 0f;
            McLog.Info($"spore body: {sporeHeight:F2} units tall -> Minecraft player height {(playerHeightBlocks > 0 ? $"{playerHeightBlocks:F3} blocks (scale {playerHeightBlocks / 1.8f:F3})" : "unchanged (matchSporeSize=0)")}");
            needScan = true;
            SetPhase(Phase.Idle, "new player");
            McLog.Info($"found PlayerController on '{controller.gameObject.name}', rigidbody on '{rb.gameObject.name}' (layer {LayerMask.LayerToName(rb.gameObject.layer)}, useGravity={rb.useGravity}, interpolation={rb.interpolation}) at {rb.position:F2}, feet y={FeetY(rb.position.y):F2}");
            McLog.Info($"ground layers: {ground.LayerNames}");

            // Test aid: "-mcColliders" lists the solid colliders around the spore with nothing visible drawn there.
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-mcColliders") >= 0)
                ColliderSurvey.Run(rb.position, 60f, ground.LayerMask);

            // Test aid: "-mcSurvey" records the level's surfaces and ceilings, then quits.
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-mcSurvey") >= 0)
            {
                LevelSurvey.Run(rb.position, ground.LayerMask, (float)(GroundScanner.CellBlocks * map.Scale));
                Application.Quit();
            }
        }

        float OwnSpeed()
        {
            if (controller == null || moveSpeedField == null) return 0;
            return (float)moveSpeedField.GetValue(controller);
        }

        /// <summary>
        /// True when Mycelium has taken the controls from the player: paused (GlobalData.isGamePaused),
        /// PlayerController.canAct off (DisableController: menus like Explore/Cancel, cutscenes, level
        /// end, knockback), or its "Player" input map switched off (boss intros, level-up menus).
        /// </summary>
        bool MyceliumHasControls(out string why)
        {
            why = "";
            if (IsPaused()) { why = "paused"; return true; }
            if (controller == null) return false;
            try
            {
                if (canActField != null && !(bool)canActField.GetValue(controller)) { why = "canAct off"; return true; }
                object actions = actionsField?.GetValue(controller);
                var asset = actions?.GetType().GetProperty("asset")?.GetValue(actions) as UnityEngine.InputSystem.InputActionAsset;
                var map = asset?.FindActionMap("Player");
                // (Not when it's off because a Minecraft screen is open: KeyOwnership switched it off,
                // and counting that hid the overlay and stopped the bridge as soon as chat opened.)
                if (map != null && !map.enabled && !keys.SilencedHere(map)) { why = "Player input map off"; return true; }
            }
            catch (Exception e)
            {
                why = "check failed: " + e.Message;
            }
            return false;
        }

        static bool IsPaused()
        {
            try { return pausedProperty != null && (bool)pausedProperty.GetValue(null); }
            catch { return false; }
        }

        void LogStatus(Vector3 sporePos)
        {
            string scene = SceneManager.GetActiveScene().name;
            int fps = framesSinceLog; // frames since the last status line, about 1 s ago
            if (!link.MinecraftAlive)
            {
                McLog.Info($"status: scene='{scene}' fps={fps} minecraft=not linked spore={(rb != null ? sporePos.ToString("F2") : "none")}");
                return;
            }
            float groundUnderMc = ground.GroundAt(mc.X, mc.Z, (float)mc.Y);
            McLog.Info($"status: scene='{scene}' fps={fps} phase={phase} mc=({mc.X:F2}, {mc.Y:F2}, {mc.Z:F2}) yaw={mc.Yaw:F0} flags={mc.Flags} epoch={mc.Epoch} " +
                       $"groundUnderMc={groundUnderMc:F2} tp={tp.Seq}/ack {mc.TeleportAck} mcSpeed={mcSpeed:F2} sporeOwnSpeed={OwnSpeed():F1} " +
                       $"input={(input.Active ? $"on keys={input.KeysDown}" : $"off({input.Reason})")} hold={heldByMycelium} " +
                       $"ticks={(mc.HasTicks ? $"yes a={lastAlpha:F2} steps={tickSteps}/{tickSteps + fallbackSteps}" : "no")} " +
                       $"camera={(cameraSync.Controlling ? $"synced view={cameraSync.Mode} x={cameraSync.X:F0} y={cameraSync.Y:F2} want down={cameraSync.WantDown:F0} tilt={cameraSync.LastTilt:F0} shake={cameraSync.ShakePos:F2}/{cameraSync.ShakeAngle:F1}deg" : "Mycelium")} mcView={mc.CameraMode} pitch={mc.Pitch:F0} {ViewText()} " +
                       $"| spore={(rb != null ? sporePos.ToString("F2") : "none")} target={lastTarget:F2} error={lastError:F2} vel={(rb != null ? rb.velocity.ToString("F2") : "-")} resyncs={resyncs} grid=v{ground.Version}");
        }

        bool frozenByMinecraft;
        float timeScaleBefore = 1f;
        readonly McPauseBlur pauseBlur = new McPauseBlur();

        /// <summary>
        /// While Minecraft's pause menu is open, Mycelium's game time stops (Time.timeScale 0), so
        /// enemies, physics and animations wait, without Mycelium's own pause menu. Set again every
        /// frame in case Mycelium changes the time scale meanwhile (its hit-stop does); put back after.
        /// </summary>
        void FreezeForMinecraftPause(bool freeze)
        {
            if (freeze)
            {
                if (!frozenByMinecraft)
                {
                    frozenByMinecraft = true;
                    timeScaleBefore = Time.timeScale > 0f ? Time.timeScale : 1f;
                    McLog.Info($"pause: Minecraft's pause menu is open, Mycelium's time stopped (time scale was {timeScaleBefore:F2})");
                    pauseBlur.Set(true);
                }
                else if (Time.timeScale > 0f) timeScaleBefore = Time.timeScale;
                Time.timeScale = 0f;
            }
            else if (frozenByMinecraft)
            {
                frozenByMinecraft = false;
                pauseBlur.Set(false);
                if (Time.timeScale == 0f) Time.timeScale = timeScaleBefore;
                McLog.Info($"pause: Minecraft's pause menu closed, Mycelium's time runs again (time scale {Time.timeScale:F2})");
            }
        }

        /// <summary>Test aid: see "-mcBlurCheck" in Awake.</summary>
        System.Collections.IEnumerator BlurCheck()
        {
            for (int n = 0; n < 60; n++)
            {
                yield return new WaitForSecondsRealtime(0.5f);
                yield return new WaitForEndOfFrame();
                Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
                int w = shot.width, h = shot.height;
                Color32[] px = shot.GetPixels32();
                Destroy(shot);
                // The middle (the world), and the top-left corner (Mycelium's health bar).
                double middle = Sharpness(px, w, w / 4, w * 3 / 4, h / 4, h * 3 / 4);
                double hud = Sharpness(px, w, w / 50, w * 3 / 10, h * 8 / 10, h * 49 / 50);
                // Average colour of the HUD corner: a blur keeps it, a missing HUD would change it.
                long r = 0, g = 0, b = 0, k = 0;
                for (int y = h * 8 / 10; y < h * 49 / 50; y++)
                    for (int x = w / 50; x < w * 3 / 10; x++) { Color32 c = px[y * w + x]; r += c.r; g += c.g; b += c.b; k++; }
                k = System.Math.Max(1, k);
                McLog.Info($"blur check: roughness middle {Roughness(px, w, w / 4, w * 3 / 4, h / 4, h * 3 / 4):F2}, HUD corner {Roughness(px, w, w / 50, w * 3 / 10, h * 8 / 10, h * 49 / 50):F2}");
                McLog.Info($"blur check: sharpness middle {middle:F2}, HUD corner {hud:F2}, HUD corner average colour ({r / k},{g / k},{b / k}) (blur {(pauseBlur.On ? "ON" : "off")}, time scale {Time.timeScale:F2})");
                if (pauseBlur.On)
                {
                    // Same corner with Mycelium's UI hidden for one frame: if it differs, the blurred UI is really drawn.
                    pauseBlur.TestHideUi(true);
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    Texture2D without = ScreenCapture.CaptureScreenshotAsTexture();
                    Color32[] q = without.GetPixels32();
                    Destroy(without);
                    pauseBlur.TestHideUi(false);
                    long r2 = 0, g2 = 0, b2 = 0, k2 = 0;
                    for (int y = h * 8 / 10; y < h * 49 / 50; y++)
                        for (int x = w / 50; x < w * 3 / 10; x++) { Color32 c = q[y * w + x]; r2 += c.r; g2 += c.g; b2 += c.b; k2++; }
                    k2 = System.Math.Max(1, k2);
                    McLog.Info($"blur check:   same corner with Mycelium's UI hidden: average colour ({r2 / k2},{g2 / k2},{b2 / k2})");
                }
            }
        }

        /// <summary>
        /// Roughness: how sharply brightness bends from pixel to pixel (|left - 2 x middle + right|),
        /// in a region. Blocky, stepped pictures score high; a smooth blur scores low.
        /// </summary>
        static double Roughness(Color32[] px, int w, int x0, int x1, int y0, int y1)
        {
            double sum = 0;
            int count = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0 + 1; x < x1 - 1; x++)
                {
                    Color32 a = px[y * w + x - 1], b = px[y * w + x], c = px[y * w + x + 1];
                    sum += Mathf.Abs((a.r + a.g + a.b) - 2 * (b.r + b.g + b.b) + (c.r + c.g + c.b)) / 3.0;
                    count++;
                }
            return sum / Mathf.Max(1, count);
        }

        /// <summary>Average colour difference between horizontally neighbouring pixels in a region (rows from the bottom).</summary>
        static double Sharpness(Color32[] px, int w, int x0, int x1, int y0, int y1)
        {
            double sum = 0;
            int count = 0;
            for (int y = y0; y < y1; y += 2)
                for (int x = x0; x < x1 - 1; x += 2)
                {
                    Color32 a = px[y * w + x], b = px[y * w + x + 1];
                    sum += Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
                    count++;
                }
            return sum / Mathf.Max(1, count);
        }

        /// <summary>Test aid: see "-mcModelCheck" in Awake.</summary>
        System.Collections.IEnumerator ModelCheck()
        {
            McLog.Info($"model check: quality level '{QualitySettings.names[QualitySettings.GetQualityLevel()]}', pipeline '{(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.name : "built-in")}'");
            UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += ProbeDepth;
            // First: the body's shadow on and off in turn, with the screen brightness where the
            // shadow should fall (from the middle of the body, along the sun, down to the feet's height).
            // Find the sun once the level (and the body) is there.
            while (!playerModel.Shown) yield return new WaitForSecondsRealtime(0.2f);
            Light sun = RenderSettings.sun != null && RenderSettings.sun.isActiveAndEnabled ? RenderSettings.sun : null;
            foreach (Light l in FindObjectsOfType<Light>())
            {
                if (l.type != LightType.Directional) continue;
                McLog.Info($"model check: directional light '{l.name}', enabled {l.isActiveAndEnabled}, shadows {l.shadows}, intensity {l.intensity:F2}, pointing {l.transform.forward:F2}");
                if (sun == null && l.isActiveAndEnabled && l.shadows != LightShadows.None) sun = l;
            }
            McLog.Info($"model check: sun {(sun != null ? $"'{sun.name}', shadows {sun.shadows}, pointing {sun.transform.forward:F2}" : "not found")}");
            bool shadowOn = true;
            int shadowChecks = 0;
            while (shadowChecks < 8 && sun != null && sun.transform.forward.y < -0.05f)
            {
                yield return new WaitForSecondsRealtime(0.5f);
                if (!playerModel.Shown || Camera.main == null) continue;
                playerModel.SetCastsShadow(shadowOn);
                yield return null;
                yield return new WaitForEndOfFrame();
                Vector3 feet = playerModel.FeetPoint;
                Vector3 middle = feet + Vector3.up * playerModel.Height * 0.5f;
                Vector3 dir = sun.transform.forward;
                Vector3 ground = middle + dir * ((middle.y - feet.y) / -dir.y);
                Vector3 g = Camera.main.WorldToScreenPoint(ground);
                Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
                bool onScreen = g.z > 0 && g.x >= 0 && g.y >= 0 && g.x < Screen.width && g.y < Screen.height;
                Color32 c = shot.GetPixel(Mathf.Clamp((int)(g.x / Screen.width * shot.width), 0, shot.width - 1),
                                          Mathf.Clamp((int)(g.y / Screen.height * shot.height), 0, shot.height - 1));
                Destroy(shot);
                McLog.Info($"model check: body shadow {(shadowOn ? "ON " : "OFF")}, where it should fall ({ground.x:F2}, {ground.y:F2}, {ground.z:F2}){(onScreen ? "" : " (off screen)")}: " +
                           $"colour ({c.r},{c.g},{c.b}), brightness {(c.r + c.g + c.b) / 3}; spore renderers still drawn: {SporeRenderersDrawn()}");
                shadowOn = !shadowOn;
                shadowChecks++;
            }
            playerModel.SetCastsShadow(true);

            // Then: the depth passes off and on in turn, with the depth the Fog effect reads at the head.
            bool passesOn = true;
            int checks = 0;
            while (checks < 8)
            {
                yield return new WaitForSecondsRealtime(0.5f);
                if (!playerModel.Shown || playerModel.ShadowOnly || Camera.main == null) continue; // first person: not in the depth, by design
                playerModel.SetDepthPasses(passesOn);
                yield return null; // one frame drawn with the new setting
                yield return new WaitForEndOfFrame();
                Vector3 s = Camera.main.WorldToScreenPoint(playerModel.HeadPoint);
                Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
                int px = Mathf.Clamp((int)(s.x / Screen.width * shot.width), 0, shot.width - 1);
                int py = Mathf.Clamp((int)(s.y / Screen.height * shot.height), 0, shot.height - 1);
                Color32 c = shot.GetPixel(px, py);
                int shotW = shot.width, shotH = shot.height;
                Destroy(shot);
                // The depth Mycelium's Fog effect reads at that pixel, on the next frame drawn.
                probeUV = new Vector2(s.x / Screen.width, s.y / Screen.height);
                probeResult = float.NaN;
                probePending = true;
                float waitUntil = Time.realtimeSinceStartup + 1f;
                while (float.IsNaN(probeResult) && Time.realtimeSinceStartup < waitUntil) yield return null;
                McLog.Info($"model check: depth passes {(passesOn ? "ON " : "OFF")}, head at screen ({px}, {py}) of {shotW}x{shotH}, really {s.z:F2} units away; " +
                           $"the depth texture says {(float.IsNaN(probeResult) ? "nothing (probe failed)" : probeResult.ToString("F2"))}; colour ({c.r},{c.g},{c.b})");
                passesOn = !passesOn;
                checks++;
            }
            playerModel.SetDepthPasses(true);
            UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= ProbeDepth;
        }

        /// <summary>Test aid: how many of the spore's renderers are drawn at all (0 while your Minecraft body is shown).</summary>
        int SporeRenderersDrawn()
        {
            if (rb == null) return 0;
            int n = 0;
            foreach (Renderer r in rb.GetComponentsInChildren<Renderer>())
                if (r.enabled && !r.forceRenderingOff) n++;
            return n;
        }

        Vector2 probeUV;
        bool probePending;
        float probeResult = float.NaN;
        Material probeMaterial;
        RenderTexture probeTarget;

        /// <summary>Test aid: after the main camera draws, read the depth texture at probeUV (MyceliumCraftDepthProbe shader).</summary>
        void ProbeDepth(UnityEngine.Rendering.ScriptableRenderContext context, Camera cam)
        {
            if (!probePending || cam == null || !cam.CompareTag("MainCamera")) return;
            probePending = false;
            if (probeMaterial == null)
            {
                Shader probe = Resources.Load<Shader>("MyceliumCraftDepthProbe");
                if (probe == null) { McLog.Info("model check: depth probe shader missing"); return; }
                probeMaterial = new Material(probe);
                probeTarget = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat);
            }
            probeMaterial.SetVector("_ProbeUV", new Vector4(probeUV.x, probeUV.y, 0, 0));
            Graphics.Blit(null, probeTarget, probeMaterial);
            UnityEngine.Rendering.AsyncGPUReadback.Request(probeTarget, 0, TextureFormat.RGBAFloat, r =>
            {
                if (r.hasError) { McLog.Info("model check: depth probe readback failed"); return; }
                probeResult = r.GetData<Color>()[0].r;
            });
        }

        /// <summary>Test aid: see "-mcOverlayCheck" in Awake.</summary>
        System.Collections.IEnumerator OverlayCheck()
        {
            // Spots in the test picture (640x360, from the top left): red box, half-transparent
            // white box, and an empty spot.
            var spots = new (string name, float x, float y)[] { ("red box", 120, 80), ("half white box", 520, 80), ("empty", 320, 250) };
            int checks = 0;
            while (checks < 12)
            {
                yield return new WaitForSecondsRealtime(1f);
                if (overlay.Width == 0) continue;
                yield return new WaitForEndOfFrame();
                bool shown = overlay.Visible;
                Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
                var parts = new System.Collections.Generic.List<string>();
                foreach (var spot in spots)
                {
                    int px = Mathf.Clamp((int)(spot.x / 640f * shot.width), 0, shot.width - 1);
                    int py = Mathf.Clamp((int)((1f - spot.y / 360f) * shot.height), 0, shot.height - 1);
                    Color32 c = shot.GetPixel(px, py);
                    parts.Add($"{spot.name}=({c.r},{c.g},{c.b})");
                }
                McLog.Info($"overlay check: overlay {(shown ? "SHOWN" : "hidden")}, screen {shot.width}x{shot.height}: {string.Join(", ", parts)}");
                Destroy(shot);
                overlay.ForceHidden = !overlay.ForceHidden;
                checks++;
            }
            overlay.ForceHidden = false;
        }

        /// <summary>Test aid: an enemy-style knockback from 1 unit in front of the spore.</summary>
        void RunHitTest()
        {
            nextHitTime = Time.realtimeSinceStartup + 4f;
            if (fakeHitter == null) fakeHitter = new GameObject("MyceliumCraft fake hitter");
            fakeHitter.transform.position = rb.position + rb.transform.forward * 1f;
            MethodInfo knock = playerControllerType.GetMethod("Knockback", BindingFlags.Public | BindingFlags.Instance);
            try
            {
                knock.Invoke(controller, new object[] { fakeHitter, 60f, false });
                McLog.Info($"hit test: Knockback called (flash frames so far {cameraSync.FlashFrames} of {cameraSync.FramesChecked} checked)");
            }
            catch (Exception e)
            {
                McLog.Info("hit test: Knockback failed: " + (e.InnerException?.Message ?? e.Message));
            }
        }

        /// <summary>Test aid: DisableController after 3 s of driving, EnableController 3 s later, as Mycelium's menus do.</summary>
        void RunHoldTest()
        {
            if (holdTestStart < 0f) holdTestStart = Time.realtimeSinceStartup;
            float t = Time.realtimeSinceStartup - holdTestStart;
            string method = holdTestStep == 0 && t >= 3f ? "DisableController" : holdTestStep == 1 && t >= 6f ? "EnableController" : null;
            if (method == null) return;
            holdTestStep++;
            MethodInfo m = playerControllerType.GetMethod(method, BindingFlags.Public | BindingFlags.Instance);
            object[] callArgs = m != null && m.GetParameters().Length == 1 ? new object[] { null } : new object[0];
            m?.Invoke(controller, callArgs);
            McLog.Info($"hold test: called PlayerController.{method}() (found={m != null})");
        }

        /// <summary>Test aid: shake the camera the way the level's own triggers do (CameraShakeManager.ShakeCamera).</summary>
        void FireTestShake()
        {
            nextShakeTime = Time.realtimeSinceStartup + 3f;
            var source = FindObjectOfType<Cinemachine.CinemachineImpulseSource>();
            Type managerType = Type.GetType("CameraShakeManager, Assembly-CSharp");
            object manager = managerType?.GetField("instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            MethodInfo shakeMethod = managerType?.GetMethod("ShakeCamera", new[] { typeof(Cinemachine.CinemachineImpulseSource), typeof(bool) });
            if (source == null || manager == null || shakeMethod == null)
            {
                McLog.Info($"shake test: can't shake (impulse source {(source != null ? "found" : "missing")}, CameraShakeManager {(manager != null ? "found" : "missing")})");
                return;
            }
            shakeMethod.Invoke(manager, new object[] { source, false });
            McLog.Info($"shake test: fired '{source.name}' (duration {source.m_ImpulseDefinition.m_ImpulseDuration:F2} s), camera view {cameraSync.Mode}");
        }

        /// <summary>Where the rendered camera really points (after Cinemachine), next to the spore's facing.</summary>
        string ViewText()
        {
            Camera cam = Camera.main;
            if (cam == null) return "view=none";
            Vector3 f = cam.transform.forward;
            float viewYaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            float viewDown = -Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
            float sporeYaw = rb != null ? rb.rotation.eulerAngles.y : float.NaN;
            float diff = Mathf.DeltaAngle(viewYaw, sporeYaw);
            string where = "";
            if (rb != null)
            {
                // Where the camera sits: distance from the spore, and in front of or behind it.
                Vector3 rel = cam.transform.position - rb.transform.position;
                Vector3 facing = rb.transform.forward;
                float along = Vector3.Dot(new Vector3(rel.x, 0f, rel.z), new Vector3(facing.x, 0f, facing.z).normalized);
                where = $" camDist={rel.magnitude:F2} camSide={(rel.magnitude < 0.5f ? "inside" : along > 0 ? "front" : "behind")} camHeight={rel.y:F2}";
            }
            return $"view yaw={viewYaw:F0} down={viewDown:F0} sporeYaw={sporeYaw:F0} (diff {diff:F0}){where}";
        }

        void OnDestroy()
        {
            link?.Dispose();
        }
    }
}
