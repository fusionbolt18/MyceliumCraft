using System;
using System.Collections.Generic;
using System.Reflection;
using MyceliumCraft.Link;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Your colony in Minecraft's player list instead of Mycelium's colony meter (your request,
    /// 2026-11-03).
    ///
    /// Mycelium's meter is HUDHappiness on the "HUD" object: a "Colony x N" text and a slider with
    /// the colony's average happiness (HappinessManager.GetAverageColonyHappiness: the mean of
    /// every spore's CharacterStats.sporeHappiness, 0..1). While Minecraft drives, both are switched
    /// off, and switched back to how Mycelium had them afterwards (Mycelium shows or hides the
    /// meter itself: only at the Carcass with two spores or more, so its choice made meanwhile is
    /// kept). Every spore in the colony (SwapCharacter.characters on "PlayerParent"): name, body
    /// colour (DesignTracker.bodyColor) and happiness go to Minecraft (Colony block), which lists
    /// them under the players with a text bar of the average (MycColony.java).
    /// </summary>
    sealed class ColonyWatch
    {
        const float LookEvery = 0.5f;

        public readonly uint[] Colors = new uint[Proto.ColMax];
        public readonly float[] Happiness = new float[Proto.ColMax];
        public readonly string[] Names = new string[Proto.ColMax];
        public int Count;
        public int Current = -1;
        public float Average = -1f;

        float nextLook;
        string lastLogged = "";

        // Mycelium's meter while we hide it: the objects, and whether Mycelium had each on.
        readonly List<(GameObject obj, bool wasOn)> hidden = new List<(GameObject, bool)>();
        Component hud;
        bool hiding;

        /// <summary>Every frame. driving: Minecraft drives the spore. Returns true when the colony was looked at again (send it).</summary>
        public bool Update(bool driving)
        {
            HideMeter(driving);
            if (Time.realtimeSinceStartup < nextLook) return false;
            nextLook = Time.realtimeSinceStartup + LookEvery;
            Look();
            return true;
        }

        void Look()
        {
            Count = 0;
            Current = -1;
            Average = -1f;
            GameObject parent = GameObject.FindWithTag("PlayerParent");
            Type swapType = Type.GetType("SwapCharacter, Assembly-CSharp");
            Type statsType = Type.GetType("CharacterStats, Assembly-CSharp");
            Type designType = Type.GetType("DesignTracker, Assembly-CSharp");
            Component swap = parent != null && swapType != null ? parent.GetComponent(swapType) : null;
            var list = swap != null ? swapType.GetField("characters")?.GetValue(swap) as List<GameObject> : null;
            if (list == null || statsType == null) { LogOnce("colony: none (no SwapCharacter on PlayerParent)"); return; }
            GameObject current = GameObject.FindWithTag("currentPlayer");
            FieldInfo nameField = statsType.GetField("sporeName"), happyField = statsType.GetField("sporeHappiness");
            FieldInfo colorField = designType?.GetField("bodyColor");
            float total = 0f;
            int all = 0;
            foreach (GameObject spore in list)
            {
                if (spore == null) continue;
                Component stats = spore.GetComponent(statsType);
                if (stats == null) continue;
                float happy = happyField?.GetValue(stats) is float h ? h : 0.5f;
                total += happy;
                all++;
                if (Count >= Proto.ColMax) continue;
                Component design = designType != null ? spore.GetComponent(designType) : null;
                Color c = design != null && colorField?.GetValue(design) is Color col ? col : Color.white;
                Names[Count] = ((nameField?.GetValue(stats) as string) ?? "").Trim();
                Happiness[Count] = happy;
                Colors[Count] = ((uint)Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f) << 16) | ((uint)Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f) << 8) | (uint)Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f);
                if (spore == current) Current = Count;
                Count++;
            }
            if (all > 0) Average = total / all;
            var parts = new List<string>();
            for (int i = 0; i < Count; i++) parts.Add($"{Names[i]}{(i == Current ? " (you)" : "")} {Happiness[i]:F2} #{Colors[i]:X6}");
            LogOnce($"colony: {all} spore(s), average happiness {Average:F2}: {string.Join(", ", parts)}");
        }

        void LogOnce(string text)
        {
            if (text == lastLogged) return;
            lastLogged = text;
            McLog.Info(text);
        }

        void HideMeter(bool driving)
        {
            if (!driving)
            {
                if (!hiding) return;
                hiding = false;
                int back = 0;
                foreach (var (obj, wasOn) in hidden)
                    if (obj != null) { obj.SetActive(wasOn); if (wasOn) back++; }
                hidden.Clear();
                McLog.Info($"colony: Mycelium's colony meter back as it was ({back} part(s) on)");
                return;
            }
            if (hud == null)
            {
                Type t = Type.GetType("HUDHappiness, Assembly-CSharp");
                GameObject h = GameObject.Find("HUD");
                hud = h != null && t != null ? h.GetComponent(t) : null;
                if (hud == null) return;
                hidden.Clear();
                hiding = false;
            }
            if (!hiding)
            {
                hiding = true;
                foreach (string field in new[] { "happinessSlider", "colonyText" })
                {
                    var part = hud.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(hud) as Component;
                    if (part != null) hidden.Add((part.gameObject, part.gameObject.activeSelf));
                }
                McLog.Info($"colony: Mycelium's colony meter hidden while Minecraft drives ({hidden.Count} part(s); on before: {string.Join(", ", hidden.ConvertAll(p => $"{p.obj.name}={p.wasOn}"))})");
            }
            // Mycelium may show it meanwhile (a new spore grown): note that, and keep it hidden.
            for (int i = 0; i < hidden.Count; i++)
            {
                var (obj, _) = hidden[i];
                if (obj == null || !obj.activeSelf) continue;
                hidden[i] = (obj, true);
                obj.SetActive(false);
            }
        }

        /// <summary>A new scene: Mycelium's HUD is a new one.</summary>
        public void SceneLoaded()
        {
            hud = null;
            hidden.Clear();
            hiding = false;
            nextLook = 0f;
        }
    }
}
