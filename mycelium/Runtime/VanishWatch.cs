using System.Collections.Generic;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Trophic Cascade's vanish, for your Minecraft body (your request, 2026-10-26).
    ///
    /// TrophicCascade.Vanish switches off (Renderer.enabled) every renderer on the spore while it
    /// jumps between enemies (0.4 to 1.4 s), then switches them back on. Nothing else in Mycelium
    /// switches the spore's renderers off, and our own hiding of the spore uses forceRenderingOff,
    /// so the spore's body renderers all being off means it has vanished. MinecraftDriver then hides
    /// your Minecraft body the same way (McPlayerModel) and tells Minecraft (MycVanished), which
    /// goes to third person behind for it if you're in first person.
    /// </summary>
    sealed class VanishWatch
    {
        readonly List<SkinnedMeshRenderer> found = new List<SkinnedMeshRenderer>();
        bool vanished;
        float since;

        /// <summary>Every frame: has the spore vanished?</summary>
        public bool Update()
        {
            GameObject player = GameObject.FindWithTag("currentPlayer");
            bool now = false;
            if (player != null)
            {
                player.GetComponentsInChildren(true, found);
                int body = 0, off = 0;
                foreach (SkinnedMeshRenderer r in found)
                {
                    if (r == null || IsWeapon(r.transform, player.transform)) continue;
                    body++;
                    if (!r.enabled) off++;
                }
                now = body > 0 && off == body;
            }
            if (now != vanished)
            {
                vanished = now;
                if (now) since = Time.time;
                McLog.Info(now ? "vanish: the spore vanished (Trophic Cascade); your Minecraft body too"
                               : $"vanish: the spore is back after {Time.time - since:F2} s; your Minecraft body too");
            }
            return vanished;
        }

        /// <summary>Part of a weapon in the spore's hand (its renderers are switched for other reasons).</summary>
        static bool IsWeapon(Transform t, Transform stop)
        {
            for (; t != null && t != stop; t = t.parent)
                if (t.CompareTag("currentWeapon") || t.CompareTag("Weapon")) return true;
            return false;
        }
    }
}
