using System;
using System.Reflection;
using MyceliumCraft.Link;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// The final boss's sand pull for Minecraft's player (your report, 2026-11-09: it didn't move
    /// you at all).
    ///
    /// Mycelium's boss (MonsterBossAttack.ApplyPullForce, every 10 s for 5 s) calls
    /// rb.AddForce(towards the boss x 95, ForceMode.Force) on the spore every frame. In Mycelium
    /// itself PlayerController sets the spore's velocity to your walk every physics step, and the
    /// physics step then adds the force gathered since the last one: the pull is an extra speed of
    /// (force gathered) x step / mass on top of walking. Here MinecraftDriver sets the spore's
    /// velocity to follow Minecraft's player every step, so that extra speed went nowhere.
    ///
    /// So at the start of every physics step (before the driver sets the velocity) the force the
    /// spore has gathered is read (Rigidbody.GetAccumulatedForce), only while a boss's attacks are
    /// on, its pull's sand particles (BossSandPullParticles) are out, you're not being knocked back,
    /// and only when it points towards the boss (within about 37 degrees): an attack's lunge or a
    /// hit isn't the pull. Its horizontal part, turned into that extra speed and
    /// smoothed over a quarter second (frames and physics steps don't line up, so a step can gather
    /// 0, 1 or 2 frames' worth), goes to Minecraft (Boss block 0x90/0x94, blocks per second), which
    /// moves its player by it every tick on top of its walking (MycBossPull.java). The force is then
    /// taken off the spore, which follows Minecraft as always.
    ///
    /// The boss adds its force once per frame and the physics step uses what's gathered since the
    /// last step, so in Mycelium the pull is stronger the more frames there are per step (95 force a
    /// frame: about 1.9 units/s at 50 frames a second, 17 frames a step in the background test). Reading
    /// what's gathered keeps that as Mycelium has it.
    /// </summary>
    sealed class PullWatch
    {
        const float Smooth = 0.25f;     // seconds
        const float MinSpeed = 0.05f;   // units/s: below this there's no pull

        static MethodInfo accumulated;
        static bool looked;
        Behaviour boss;
        float nextFind, nextRaw, nextParticles;
        bool sandOut; // the boss's BossSandPullParticles are out: it's pulling
        Vector3 pull;       // smoothed, Mycelium units/s, horizontal
        bool pulling;
        float start, peak;
        Vector3 startPos;

        /// <summary>Start of every physics step, before the driver sets the velocity. knockback: a hit is knocking you back (its impulse isn't the pull).</summary>
        public void Step(Rigidbody rb, bool driving, bool knockback)
        {
            if (!looked)
            {
                looked = true;
                accumulated = typeof(Rigidbody).GetMethod("GetAccumulatedForce", new[] { typeof(float) });
                McLog.Info(accumulated != null ? "boss pull: Rigidbody.GetAccumulatedForce found; the boss's pull moves Minecraft's player"
                                               : "boss pull: Rigidbody.GetAccumulatedForce not found; the boss's pull won't move Minecraft's player");
            }
            if (Time.time >= nextFind)
            {
                nextFind = Time.time + 1f;
                Type t = Type.GetType("MonsterBossAttack, Assembly-CSharp");
                boss = t != null ? UnityEngine.Object.FindObjectOfType(t) as Behaviour : null;
            }
            if (Time.time >= nextParticles)
            {
                nextParticles = Time.time + 0.25f;
                sandOut = boss != null && GameObject.Find("BossSandPullParticles(Clone)") != null;
            }
            Vector3 now = Vector3.zero;
            if (driving && rb != null && accumulated != null && boss != null && boss.isActiveAndEnabled && sandOut && !knockback && rb.mass > 0f)
            {
                Vector3 force = (Vector3)accumulated.Invoke(rb, new object[] { Time.fixedDeltaTime });
                force.y = 0f;
                // Only a push towards the boss is its pull (an attack's lunge, a hit, anything else isn't).
                Vector3 toBoss = boss.transform.position - rb.position;
                toBoss.y = 0f;
                if (force.sqrMagnitude > 1e-6f && toBoss.sqrMagnitude > 1e-6f && Vector3.Dot(force.normalized, toBoss.normalized) < 0.8f)
                {
                    if (Time.time >= nextRaw)
                    {
                        nextRaw = Time.time + 0.5f;
                        McLog.Info($"boss pull: a push of {force:F1} not towards the boss; not its pull, left to Mycelium");
                    }
                    force = Vector3.zero;
                }
                now = force * Time.fixedDeltaTime / rb.mass;
                // Minecraft moves you by it now: taken off the spore, so it doesn't jump ahead of Minecraft's
                // player this step (that was far enough for a re-sync teleport in the stand-in run).
                rb.AddForce(-force, ForceMode.Force);
                if (Time.time >= nextRaw && force.sqrMagnitude > 1e-4f)
                {
                    nextRaw = Time.time + 0.5f;
                    McLog.Info($"boss pull: force gathered this step {force:F1} (mass {rb.mass:F2})");
                }
            }
            pull = Vector3.Lerp(pull, now, Mathf.Min(1f, Time.fixedDeltaTime / Smooth));
            if (pull.magnitude < MinSpeed && now == Vector3.zero) pull = Vector3.zero;

            bool on = pull.magnitude >= MinSpeed;
            if (on && !pulling)
            {
                pulling = true;
                start = Time.time;
                peak = 0f;
                startPos = rb.position;
                McLog.Info($"boss pull: '{boss?.name}' pulls you (mass {rb.mass:F2}); Minecraft's player is moved towards it");
            }
            if (on) peak = Mathf.Max(peak, pull.magnitude);
            if (!on && pulling)
            {
                pulling = false;
                Vector3 moved = rb != null ? rb.position - startPos : Vector3.zero;
                moved.y = 0f;
                McLog.Info($"boss pull: over after {Time.time - start:F1} s, at most {peak:F2} units/s; the spore went {moved.magnitude:F2} units in that time");
            }
        }

        /// <summary>The pull for the Boss block, in Minecraft blocks per second (x, z); zero when none.</summary>
        public void ForMinecraft(WorldMapping map, out float x, out float z)
        {
            x = z = 0f;
            if (!pulling || map == null) return;
            map.ToMc(pull.x, 0, pull.z, out double mx, out _, out double mz);
            x = (float)mx;
            z = (float)mz;
        }
    }
}
