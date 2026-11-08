using System;
using MyceliumCraft.Link;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// You and the other spores collide by Minecraft's rules (your request, 2026-11-04). Their feet
    /// go to Minecraft every frame, where each has an invisible creature standing in for it
    /// (SporeProxies.java): Minecraft's own collision shoves your player and that creature apart.
    /// What the creature was shoved by comes back as a running total per spore, and the new part of
    /// it moves the real spore here, eased over a few frames as Minecraft's ticks are only 20 a
    /// second. It's moved directly, as a standing spore weighs 10000 (WanderingSpore.rbActiveMass).
    /// One at a curio or digging out stays put. Mycelium's physics plays no part: ColonyPassThrough.
    /// </summary>
    sealed class ColonyPush
    {
        readonly float[] positions = new float[Proto.ColMax * 3];
        readonly float[] lastX = new float[Proto.ColMax], lastZ = new float[Proto.ColMax];
        readonly Vector3[] waiting = new Vector3[Proto.ColMax];
        bool sent, haveTotals;
        string pushedName;
        float pushedTotal, lastPush;

        /// <summary>Every frame. you: your spore, or null when Minecraft isn't driving (a re-sync teleport counts as driving).</summary>
        public void Update(MyceliumLink link, Rigidbody you, ColonyWatch colony, WorldMapping map, float feetOffset)
        {
            if (pushedName != null && Time.time - lastPush > 0.5f)
            {
                McLog.Info($"colony push: {pushedName} pushed {pushedTotal:F2} units");
                pushedName = null;
                pushedTotal = 0f;
            }
            if (you == null)
            {
                if (sent) link.WriteSporePositions(0, positions);
                sent = haveTotals = false;
                Array.Clear(waiting, 0, waiting.Length);
                return;
            }

            for (int i = 0; i < colony.Count; i++)
            {
                GameObject spore = colony.Spores[i];
                if (spore == null || spore == you.gameObject || !spore.activeInHierarchy)
                {
                    positions[i * 3 + 1] = float.NaN; // no stand-in: that's you, or it's gone
                    continue;
                }
                Vector3 p = spore.transform.position;
                map.ToMc(p.x, p.y - feetOffset, p.z, out double x, out double y, out double z);
                positions[i * 3] = (float)x;
                positions[i * 3 + 1] = (float)y;
                positions[i * 3 + 2] = (float)z;
            }
            link.WriteSporePositions(colony.Count, positions);
            sent = true;

            float ease = Mathf.Min(1f, Time.deltaTime * 20f);
            for (int i = 0; i < Proto.ColMax; i++)
            {
                link.ReadSporePush(i, out float totalX, out float totalZ);
                float dx = totalX - lastX[i], dz = totalZ - lastZ[i];
                lastX[i] = totalX;
                lastZ[i] = totalZ;
                if (!haveTotals || i >= colony.Count) continue; // the totals so far are from before
                waiting[i] += new Vector3(dx, 0f, -dz) * (float)map.Scale;
                if (waiting[i] == Vector3.zero) continue;
                GameObject spore = colony.Spores[i];
                Rigidbody body = spore != null ? spore.GetComponent<Rigidbody>() : null;
                if (body == null || body == you || body.isKinematic || colony.Emerges[i] >= 0f || AtCurio(spore))
                {
                    waiting[i] = Vector3.zero;
                    continue;
                }
                Vector3 step = waiting[i].sqrMagnitude < 0.000001f ? waiting[i] : waiting[i] * ease;
                body.position += step;
                waiting[i] -= step;
                pushedName = colony.Names[i];
                pushedTotal += step.magnitude;
                lastPush = Time.time;
            }
            haveTotals = true;
        }

        static bool AtCurio(GameObject spore)
        {
            Type t = Type.GetType("WanderingSpore, Assembly-CSharp");
            Component wander = t != null ? spore.GetComponent(t) : null;
            return wander != null && t.GetField("currentState")?.GetValue(wander)?.ToString() == "Interacting";
        }
    }
}
