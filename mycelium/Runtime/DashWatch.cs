using System.Reflection;
using MyceliumCraft.Link;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Mycelium's dashes (Blitz, the dodge roll) for Minecraft's player (your report, 2026-10-25:
    /// Blitz didn't send you forwards).
    ///
    /// Both set PlayerController.activeDodge and then write the spore's velocity from a coroutine,
    /// every frame (Blitz: 40 units/s ahead for 0.3 s; the roll: rollSpeed ahead). Minecraft is in
    /// charge of where you are, and MinecraftDriver sets the spore's velocity every physics step to
    /// follow it, which cancelled the dash. So the dash goes to Minecraft instead. Coroutines run
    /// after every Update and before LateUpdate, so the velocity is noted in Update and looked at
    /// again in LateUpdate: while activeDodge is on, a change in between is Mycelium writing its dash
    /// this frame (whatever the driver set, which also follows the dash once Minecraft moves). It's
    /// sent to Minecraft (MycState 0x6C/0x70, blocks per second), which gives its player that speed;
    /// the spore follows Minecraft as always. It stops 0.1 s after Mycelium stops writing it (the
    /// driver changes the velocity every physics step, so a write is seen at least every 20 ms).
    /// </summary>
    sealed class DashWatch
    {
        const float MinSpeed = 1f;       // units/s: a write slower than this isn't a dash
        const float HoldSeconds = 0.1f;  // keep sending this long after the last seen write

        FieldInfo activeDodge;
        bool looked;
        Vector3 dash;      // Mycelium units/s, horizontal
        float until = -1f;
        bool dashing;
        int frames;
        float start;
        Vector3 startPos;

        Vector3 inUpdate;

        /// <summary>Update (before coroutines run): the velocity as it is now.</summary>
        public void BeforeCoroutines(Rigidbody rb)
        {
            if (rb != null) inUpdate = rb.velocity;
        }

        /// <summary>LateUpdate: has Mycelium written a dash velocity this frame?</summary>
        public void LateUpdate(Component controller, Rigidbody rb, bool driving)
        {
            if (!looked && controller != null)
            {
                looked = true;
                activeDodge = controller.GetType().GetField("activeDodge", BindingFlags.Public | BindingFlags.Instance);
                if (activeDodge == null) McLog.Info("dash: PlayerController.activeDodge not found; Blitz and the roll won't move Minecraft's player");
            }
            if (driving && rb != null && controller != null && activeDodge != null && activeDodge.GetValue(controller) is bool on && on)
            {
                Vector3 v = rb.velocity;
                v.y = 0f;
                Vector3 before = inUpdate;
                before.y = 0f;
                if ((v - before).sqrMagnitude > 1e-6f && v.magnitude > MinSpeed)
                {
                    if (!dashing)
                    {
                        dashing = true;
                        frames = 0;
                        start = Time.time;
                        startPos = rb.position;
                    }
                    dash = v;
                    until = Time.time + HoldSeconds;
                    frames++;
                }
            }
            if (dashing && Time.time > until)
            {
                dashing = false;
                Vector3 moved = rb != null ? rb.position - startPos : Vector3.zero;
                moved.y = 0f;
                McLog.Info($"dash: Mycelium's dash ({dash.magnitude:F1} units/s) sent to Minecraft for {Time.time - start:F2} s ({frames} frames); the spore went {moved.magnitude:F1} units");
            }
        }

        /// <summary>The dash for MycState, in Minecraft blocks per second (x, z); zero when none.</summary>
        public void ForMinecraft(WorldMapping map, out float x, out float z)
        {
            x = z = 0f;
            if (!dashing || map == null) return;
            map.ToMc(dash.x, 0, dash.z, out double mx, out _, out double mz);
            x = (float)mx;
            z = (float)mz;
        }
    }
}
