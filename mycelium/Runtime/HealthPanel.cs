using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Mycelium's health bar isn't shown while Minecraft's hearts show the same health (your request,
    /// 2026-11-08); the spore's name and trait above it stay. Under the "HUD" object (HUDHealth, seen
    /// with -mcHudDump): "HealthBarPanelCutoutMask" is the bar with its background, "HPNumber" the
    /// "120/150" beside it; "SporeName" and "SporeTraitName" are the name part, left alone. Only those
    /// two objects are switched off: Mycelium keeps setting the bar and number while they're off. They
    /// come back as Mycelium had them when Minecraft stops driving, or with the overlay off (no hearts
    /// to see then), as the nutrient panel does (NutrientPanel).
    /// </summary>
    sealed class HealthPanel
    {
        static readonly string[] Paths = { "HealthBarPanelCutoutMask", "HPNumber" };

        readonly GameObject[] parts = new GameObject[Paths.Length];
        readonly bool[] wasOn = new bool[Paths.Length];
        bool hiding, reportedMissing;
        float nextFind;

        /// <summary>Every frame. hide: Minecraft's hearts are on show in its place.</summary>
        public void Update(bool hide)
        {
            if (!hide)
            {
                if (!hiding) return;
                hiding = false;
                for (int i = 0; i < parts.Length; i++) if (parts[i] != null) parts[i].SetActive(wasOn[i]);
                McLog.Info($"health: Mycelium's health bar back as it was (bar on: {wasOn[0]}, number on: {wasOn[1]})");
                return;
            }
            if (parts[0] == null && parts[1] == null)
            {
                hiding = false;
                if (Time.realtimeSinceStartup < nextFind) return;
                nextFind = Time.realtimeSinceStartup + 1f;
                Transform hud = GameObject.Find("HUD")?.transform;
                for (int i = 0; i < Paths.Length; i++) parts[i] = hud != null ? hud.Find(Paths[i])?.gameObject : null;
                if (parts[0] == null && parts[1] == null)
                {
                    if (!reportedMissing) McLog.Info("health: no health bar on the HUD yet");
                    reportedMissing = true;
                    return;
                }
            }
            if (!hiding)
            {
                hiding = true;
                for (int i = 0; i < parts.Length; i++) wasOn[i] = parts[i] != null && parts[i].activeSelf;
                McLog.Info($"health: Mycelium's health bar and number hidden while Minecraft's hearts show your health; the spore's name stays (on before: bar {wasOn[0]}, number {wasOn[1]})");
            }
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] != null && parts[i].activeSelf)
                {
                    wasOn[i] = true; // Mycelium showed it meanwhile: that's what comes back
                    parts[i].SetActive(false);
                }
            }
        }

        /// <summary>A new scene: its HUD may be a new one. If the old one stays, it's put back first.</summary>
        public void SceneLoaded()
        {
            if (hiding) for (int i = 0; i < parts.Length; i++) if (parts[i] != null) parts[i].SetActive(wasOn[i]);
            for (int i = 0; i < parts.Length; i++) parts[i] = null;
            hiding = false;
            reportedMissing = false;
            nextFind = 0f;
        }

        /// <summary>For the test aid: what's drawn of the health HUD now.</summary>
        public static string Report()
        {
            Transform hud = GameObject.Find("HUD")?.transform;
            if (hud == null) return "no HUD";
            string State(string n) { Transform t = hud.Find(n); return t == null ? "missing" : t.gameObject.activeInHierarchy ? "shown" : "hidden"; }
            return $"bar {State("HealthBarPanelCutoutMask")}, number {State("HPNumber")}, name {State("SporeName")}, trait {State("SporeTraitName")}";
        }
    }
}
