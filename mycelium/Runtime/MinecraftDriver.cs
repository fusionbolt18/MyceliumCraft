using System;
using System.Reflection;
using MyceliumCraft.Link;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;

namespace MyceliumCraft
{
    /// <summary>
    /// Phase 0: moves Mycelium's spore the way the Minecraft player moves.
    ///
    /// Mycelium's code is not changed. This component starts itself before the first scene, finds
    /// Mycelium's PlayerController by name (reflection), and runs its FixedUpdate after
    /// PlayerController's (execution order 10000) so the velocity it sets wins.
    /// </summary>
    [Preserve]
    [DefaultExecutionOrder(10000)]
    public sealed class MinecraftDriver : MonoBehaviour
    {
        // How hard the spore is pulled toward where Minecraft says it should be (1/s), and caps.
        const float Gain = 12f;
        const float MaxSpeed = 30f;
        // If Mycelium's own collision holds the spore back further than this, give up on catching up
        // and re-anchor where it is (Phase 0 has no shared collision yet).
        const float ReanchorDistance = 6f;

        static Type playerControllerType;
        static FieldInfo rbField;
        static FieldInfo animatorField;
        static PropertyInfo pausedProperty;

        MyceliumLink link;
        readonly AnchorMapping mapping = new AnchorMapping();
        Component controller;
        Rigidbody rb;
        Animator animator;
        float nextFindTime;
        ulong frame;
        bool mcWasAlive;
        bool driving;

        McState mc;
        double lastMcX, lastMcZ;
        float mcSpeed; // blocks per second, smoothed
        Vector3 lastTarget;
        float lastError;
        int reanchors;
        float nextLogTime;
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
            // Mycelium normally freezes when its window loses focus. Minecraft has the focus while
            // you play, so keep running.
            Application.runInBackground = true;

            playerControllerType = Type.GetType("PlayerController, Assembly-CSharp");
            if (playerControllerType != null)
            {
                rbField = playerControllerType.GetField("rb", BindingFlags.Public | BindingFlags.Instance);
                animatorField = playerControllerType.GetField("animator", BindingFlags.Public | BindingFlags.Instance);
            }
            Type globalData = Type.GetType("GlobalData, Assembly-CSharp");
            pausedProperty = globalData?.GetProperty("isGamePaused", BindingFlags.Public | BindingFlags.Static);
            McLog.Info($"loaded, protocol v{Proto.Version}. PlayerController found={playerControllerType != null} rb={rbField != null} animator={animatorField != null} GlobalData.isGamePaused={pausedProperty != null}");

            try
            {
                link = MyceliumLink.Create();
                McLog.Info($"shared memory {Proto.MappingName} created (already existed={link.AlreadyExisted})");
            }
            catch (Exception e)
            {
                McLog.Info("FAILED to create shared memory: " + e.Message);
            }

            SceneManager.sceneLoaded += (scene, mode) =>
            {
                McLog.Info($"scene loaded: '{scene.name}'");
                controller = null;
                nextFindTime = 0;
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
            link.WriteMycState(flags, pos.x, pos.y, pos.z, yaw, frame);

            bool alive = link.MinecraftAlive;
            if (alive != mcWasAlive)
            {
                mcWasAlive = alive;
                McLog.Info(alive ? $"Minecraft linked (pid {link.MinecraftPid})" : "Minecraft gone (heartbeat stopped); Mycelium controls the spore again");
                if (!alive) mapping.Clear();
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

        void FixedUpdate()
        {
            driving = false;
            if (link == null || rb == null || !link.MinecraftAlive) return;
            if (!link.TryReadMcState(out mc) || !mc.InWorld) { mapping.Clear(); return; }
            if (IsPaused()) return;

            // Smoothed Minecraft horizontal speed, for the walk animation and the log.
            if (mapping.HasAnchor)
            {
                double dx = mc.X - lastMcX, dz = mc.Z - lastMcZ;
                float instant = (float)Math.Sqrt(dx * dx + dz * dz) / Time.fixedDeltaTime;
                mcSpeed = Mathf.Lerp(mcSpeed, instant, 0.2f);
            }
            lastMcX = mc.X;
            lastMcZ = mc.Z;

            Vector3 pos = rb.position;
            if (!mapping.HasAnchor || mapping.Epoch != mc.Epoch)
            {
                Anchor(pos, "start");
            }

            mapping.Target(mc.X, mc.Z, out double tx, out double tz);
            Vector3 target = new Vector3((float)tx, pos.y, (float)tz);
            Vector3 error = target - pos;
            error.y = 0;
            lastError = error.magnitude;
            if (lastError > ReanchorDistance)
            {
                reanchors++;
                Anchor(pos, $"spore {lastError:F1} units behind (Mycelium collision?)");
                target = pos;
                error = Vector3.zero;
            }
            lastTarget = target;

            Vector3 v = Vector3.ClampMagnitude(error * Gain, MaxSpeed);
            rb.velocity = new Vector3(v.x, rb.velocity.y, v.z);
            rb.MoveRotation(Quaternion.Euler(0f, AnchorMapping.Yaw(mc.Yaw), 0f));
            if (animator != null) animator.SetBool("Walk", mcSpeed > 0.5f);
            driving = true;
        }

        void Anchor(Vector3 sporePos, string why)
        {
            mapping.SetAnchor(mc.X, mc.Z, sporePos.x, sporePos.y, sporePos.z, mc.Epoch);
            McLog.Info($"anchor ({why}): mc=({mc.X:F2}, {mc.Y:F2}, {mc.Z:F2}) epoch={mc.Epoch} <-> spore=({sporePos.x:F2}, {sporePos.y:F2}, {sporePos.z:F2})");
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
            mapping.Clear();
            McLog.Info($"found PlayerController on '{controller.gameObject.name}', rigidbody on '{(rb != null ? rb.gameObject.name : "none")}' at {(rb != null ? rb.position.ToString("F2") : "-")}");
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
                McLog.Info($"status: scene='{scene}' fps={fps:F0} minecraft=not linked spore={(rb != null ? sporePos.ToString("F2") : "none")}");
                return;
            }
            McLog.Info($"status: scene='{scene}' fps={fps:F0} driving={driving} mc=({mc.X:F2}, {mc.Y:F2}, {mc.Z:F2}) yaw={mc.Yaw:F0} flags={mc.Flags} epoch={mc.Epoch} mcSpeed={mcSpeed:F2} " +
                       $"| spore={(rb != null ? sporePos.ToString("F2") : "none")} target={lastTarget.ToString("F2")} error={lastError:F2} vel={(rb != null ? rb.velocity.ToString("F2") : "-")} reanchors={reanchors}");
        }

        void OnDestroy()
        {
            link?.Dispose();
        }
    }
}
