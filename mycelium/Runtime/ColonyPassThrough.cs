using System.Collections.Generic;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Mycelium's physics doesn't hold you against the other spores while Minecraft drives (your report,
    /// 2026-11-04: you could get stuck bumping into them).
    ///
    /// Minecraft doesn't know they're there, so its player walked on while Mycelium's physics held
    /// your spore against one, until the two were 6 units apart and a re-sync teleport put you back.
    /// Physics.IgnoreCollision switches off each pair of solid colliders, yours against theirs. Trigger
    /// colliders are left alone: Mycelium uses those to notice things. Looked at again twice a
    /// second, for new spores and new colliders. All put back when Minecraft stops driving, and when
    /// you swap spores (your old spore is one of the others then).
    ///
    /// You still bump into them, by Minecraft's rules, where your player really is: ColonyPush.
    /// </summary>
    sealed class ColonyPassThrough
    {
        const float LookEvery = 0.5f;

        readonly HashSet<(Collider yours, Collider theirs)> ignored = new HashSet<(Collider, Collider)>();
        readonly List<Collider> mine = new List<Collider>();
        readonly List<Collider> theirs = new List<Collider>();
        Rigidbody you;
        float nextLook;

        /// <summary>Test aid (-mcBumpTestSolid): spores stay solid, to compare.</summary>
        public bool Off;

        /// <summary>Every frame. spore: yours, or null when Minecraft isn't driving (a re-sync teleport counts as driving).</summary>
        public void Update(Rigidbody spore, ColonyWatch colony)
        {
            if (Off) spore = null;
            if (spore != you)
            {
                Restore();
                you = spore;
                nextLook = 0f;
            }
            if (spore == null || Time.realtimeSinceStartup < nextLook) return;
            nextLook = Time.realtimeSinceStartup + LookEvery;

            spore.GetComponentsInChildren(true, mine);
            int before = ignored.Count, others = 0;
            for (int i = 0; i < colony.Listed; i++) // your colony only (not the scenery spores after it)
            {
                GameObject other = colony.Spores[i];
                if (other == null || other == spore.gameObject) continue;
                others++;
                other.GetComponentsInChildren(true, theirs);
                foreach (Collider a in mine)
                {
                    if (a.isTrigger) continue;
                    foreach (Collider b in theirs)
                    {
                        if (b.isTrigger) continue;
                        ignored.Add((a, b));
                        Physics.IgnoreCollision(a, b, true); // every look: a collider switched off and on forgets it
                    }
                }
            }
            if (ignored.Count != before) McLog.Info($"pass through: your spore walks through {others} other spore(s), {ignored.Count} collider pairs off");
        }

        void Restore()
        {
            if (ignored.Count == 0) return;
            int back = 0;
            foreach (var (a, b) in ignored)
                if (a != null && b != null) { Physics.IgnoreCollision(a, b, false); back++; }
            ignored.Clear();
            McLog.Info($"pass through: {back} collider pairs back on");
        }
    }
}
