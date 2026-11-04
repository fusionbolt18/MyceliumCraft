using Cinemachine;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// The little cutscene when a spore is grown in the shop: neither Mycelium's HUD nor Minecraft's
    /// overlay shows during it (your request, 2026-11-03).
    ///
    /// Mycelium's SpawnCharacter makes the "GrowCamera" the live camera for 2.5 s (NewSporeCam gives
    /// it the higher priority), so the cutscene is while that camera outranks the main one. The HUD is
    /// the CanvasGroup on the "HUD" object: its alpha is held at 0 and put back after. The driver
    /// keeps the overlay off meanwhile. Mycelium's "was born" notification is on its own canvas, and
    /// stays: it says the new spore's name.
    /// </summary>
    sealed class BirthCutscene
    {
        CinemachineFreeLook grow, main;
        CanvasGroup hud;
        float hudAlpha;
        float nextFind;

        public bool On { get; private set; }

        /// <summary>Every frame, in Update and again before drawing. driving: Minecraft has the spore (a re-sync teleport counts: the shop closing starts one).</summary>
        public void Update(bool driving)
        {
            bool now = driving && Live();
            if (now != On)
            {
                On = now;
                if (now) Begin();
                else End();
            }
            if (On && hud != null && hud.alpha != 0f)
            {
                hudAlpha = hud.alpha; // Mycelium set it meanwhile (the shop closing): that's what comes back
                hud.alpha = 0f;
            }
        }

        bool Live()
        {
            if ((grow == null || main == null) && Time.realtimeSinceStartup >= nextFind)
            {
                nextFind = Time.realtimeSinceStartup + 1f;
                grow = GameObject.FindWithTag("GrowCamera")?.GetComponent<CinemachineFreeLook>();
                main = GameObject.FindWithTag("MainCamera")?.GetComponent<CinemachineFreeLook>();
                if (grow != null && main != null) McLog.Info($"birth cutscene: cameras found, grow priority {grow.Priority}, main {main.Priority}");
            }
            return grow != null && main != null && grow.Priority > main.Priority;
        }

        void Begin()
        {
            GameObject h = GameObject.Find("HUD");
            hud = h != null ? h.GetComponent<CanvasGroup>() : null;
            hudAlpha = hud != null ? hud.alpha : 1f;
            McLog.Info($"birth cutscene: on; HUD {(hud != null ? $"hidden (alpha was {hudAlpha:F2})" : "not found")}, overlay off");
        }

        void End()
        {
            if (hud != null) hud.alpha = hudAlpha;
            McLog.Info($"birth cutscene: over; HUD back (alpha {hudAlpha:F2}), overlay back");
        }

        /// <summary>A new scene: its cameras and HUD are new ones.</summary>
        public void SceneLoaded()
        {
            On = false;
            grow = main = null;
            hud = null;
            nextFind = 0f;
        }
    }
}
