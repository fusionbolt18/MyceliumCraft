using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Mycelium's nutrient count isn't shown while Minecraft's experience level shows the same number
    /// (your request, 2026-11-04). The panel is "ResourcePanels/NutrientsPanel" under the "HUD" object:
    /// its background, icon, number and the "+50%" of later loops. Only the panel is switched off:
    /// the count itself is on NutrientTracker, and the number keeps being set while it's off. It comes
    /// back as Mycelium had it when Minecraft stops driving, or with the overlay off (no experience
    /// level to see then).
    /// </summary>
    sealed class NutrientPanel
    {
        const string Path = "ResourcePanels/NutrientsPanel";

        GameObject panel;
        bool wasOn, hiding, reportedMissing;
        float nextFind;

        /// <summary>Every frame. hide: Minecraft's experience level is on show in its place.</summary>
        public void Update(bool hide)
        {
            if (!hide)
            {
                if (!hiding) return;
                hiding = false;
                if (panel != null) panel.SetActive(wasOn);
                McLog.Info($"nutrients: Mycelium's panel back as it was (on: {wasOn})");
                return;
            }
            if (panel == null)
            {
                hiding = false;
                if (Time.realtimeSinceStartup < nextFind) return;
                nextFind = Time.realtimeSinceStartup + 1f;
                panel = GameObject.Find("HUD")?.transform.Find(Path)?.gameObject;
                if (panel == null)
                {
                    if (!reportedMissing) McLog.Info($"nutrients: no {Path} on the HUD yet");
                    reportedMissing = true;
                    return;
                }
            }
            if (!hiding)
            {
                hiding = true;
                wasOn = panel.activeSelf;
                McLog.Info($"nutrients: Mycelium's panel hidden while Minecraft's experience level shows them (on before: {wasOn})");
            }
            if (panel.activeSelf)
            {
                wasOn = true; // Mycelium showed it meanwhile: that's what comes back
                panel.SetActive(false);
            }
        }

        /// <summary>A new scene: its HUD may be a new one. If the old one stays, it's put back first, so what's noted next is Mycelium's own choice.</summary>
        public void SceneLoaded()
        {
            if (hiding && panel != null) panel.SetActive(wasOn);
            panel = null;
            hiding = false;
            reportedMissing = false;
            nextFind = 0f;
        }
    }
}
