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
        static PropertyInfo pausedProperty;

        MyceliumLink link;
        WorldMapping map;
        readonly GroundScanner ground = new GroundScanner();
        readonly CameraSync cameraSync = new CameraSync();
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
        float stuckSince = -1f; // when error first went above StuckDistance, or -1
        int resyncs;
        float nextLogTime;
        bool shakeTest;
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
            link.WriteMycState(flags, pos.x, pos.y, pos.z, yaw, frame, tp, rb != null ? playerHeightBlocks : 0f);

            bool alive = link.MinecraftAlive;
            if (alive != mcWasAlive)
            {
                mcWasAlive = alive;
                McLog.Info(alive ? $"Minecraft linked (pid {link.MinecraftPid})" : "Minecraft gone (heartbeat stopped); Mycelium controls the spore again");
            }

            // Camera: every rendered frame (not the 50 Hz physics step), so turning stays smooth.
            McState camMc = default;
            bool look = alive && phase == Phase.Driving && link.TryReadMcState(out camMc) && camMc.InWorld;
            cameraSync.Update(look, camMc);
            if (shakeTest && look && Time.realtimeSinceStartup >= nextShakeTime) FireTestShake();

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

        void FixedUpdate()
        {
            driving = false;
            if (link == null || rb == null) return;
            if (!link.MinecraftAlive || !link.TryReadMcState(out mc) || !mc.InWorld)
            {
                SetPhase(Phase.Idle, "Minecraft not in a world");
                return;
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
                haveLastMc = false;
                mcVel = Vector3.zero;
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

            map.ToMyc(mc.X, mc.Y, mc.Z, out double tx, out double ty, out double tz);
            Vector3 target = new Vector3((float)tx, (float)ty + feetOffset, (float)tz);
            Vector3 error = target - pos;
            lastError = error.magnitude;
            lastTarget = target;
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

            rb.velocity = Vector3.ClampMagnitude(mcVel + error * Gain, MaxSpeed);
            rb.AddForce(-MyceliumGravity, ForceMode.Acceleration); // cancel PlayerController's gravity: Minecraft does gravity now
            if (rb.useGravity) rb.AddForce(-Physics.gravity, ForceMode.Acceleration);
            rb.MoveRotation(Quaternion.Euler(0f, WorldMapping.MycYaw(mc.Yaw), 0f));
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
                       $"camera={(cameraSync.Controlling ? $"synced view={cameraSync.Mode} x={cameraSync.X:F0} y={cameraSync.Y:F2} want down={cameraSync.WantDown:F0} tilt={cameraSync.LastTilt:F0} shake={cameraSync.ShakePos:F2}/{cameraSync.ShakeAngle:F1}deg" : "Mycelium")} mcView={mc.CameraMode} pitch={mc.Pitch:F0} {ViewText()} " +
                       $"| spore={(rb != null ? sporePos.ToString("F2") : "none")} target={lastTarget:F2} error={lastError:F2} vel={(rb != null ? rb.velocity.ToString("F2") : "-")} resyncs={resyncs} grid=v{ground.Version}");
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
