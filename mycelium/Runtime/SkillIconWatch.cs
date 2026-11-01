using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// The skill bar's icons after a scene loads (your report, 2026-11-01: on the first load, the
    /// skill in slot 2 had no icon).
    ///
    /// Mycelium fills the bar once, 0.2 s after its HUD starts (HUDSkills.UpdateHUDIcons), from the
    /// names of the spore's SkillLoadout children (GetChild(0..2)). If the loadout's skills are only
    /// in place later than that, a slot keeps "no skill" until a menu refreshes it. This watches the
    /// loadout for the first seconds of a scene, logs what it and the bar show, and when the
    /// children's names change after the bar was filled, asks Mycelium to fill it again
    /// (UpdateHUDIcons, its own method).
    /// </summary>
    sealed class SkillIconWatch
    {
        const float WatchSeconds = 8f;
        // Test aid "-mcNoIconRefresh": only watch and log, as Mycelium does on its own.
        static readonly bool NoRefresh = Array.IndexOf(Environment.GetCommandLineArgs(), "-mcNoIconRefresh") >= 0;

        /// <summary>Started on every scene load (MonoBehaviour host for the coroutine).</summary>
        public IEnumerator Watch(string scene)
        {
            float start = Time.realtimeSinceStartup;
            string last = null;
            int refreshes = 0;
            var logAt = new List<float> { 0.1f, 0.3f, 0.6f, 1f, 2f, 4f, WatchSeconds };
            while (Time.realtimeSinceStartup - start < WatchSeconds)
            {
                float t = Time.realtimeSinceStartup - start;
                string names = LoadoutNames();
                if (names != null && last != null && names != last && t > 0.25f && !NoRefresh)
                {
                    // Changed after Mycelium's 0.2 s fill: fill again.
                    if (Refresh()) refreshes++;
                    McLog.Info($"skill icons: '{scene}' +{t:F2} s: the loadout changed ({last} -> {names}); the skill bar is filled again");
                }
                if (names != null) last = names;
                if (logAt.Count > 0 && t >= logAt[0])
                {
                    logAt.RemoveAt(0);
                    McLog.Info($"skill icons: '{scene}' +{t:F2} s: loadout {names ?? "(no spore yet)"}; bar {BarIcons() ?? "(no HUD yet)"}");
                }
                yield return null;
            }
            if (refreshes > 0) McLog.Info($"skill icons: '{scene}': the skill bar was filled again {refreshes} times");
        }

        static string LoadoutNames()
        {
            GameObject player = GameObject.FindWithTag("currentPlayer");
            Transform loadout = player != null ? player.transform.Find("SkillLoadout") : null;
            if (loadout == null) return null;
            var names = new List<string>();
            for (int i = 0; i < loadout.childCount; i++) names.Add(loadout.GetChild(i).name);
            return "[" + string.Join(", ", names) + "]";
        }

        static Component Hud()
        {
            GameObject hud = GameObject.Find("HUD");
            Type t = Type.GetType("HUDSkills, Assembly-CSharp");
            return hud != null && t != null ? hud.GetComponent(t) : null;
        }

        static bool Refresh()
        {
            Component hud = Hud();
            if (hud == null) return false;
            try { hud.GetType().GetMethod("UpdateHUDIcons")?.Invoke(hud, null); return true; }
            catch (Exception e) { McLog.Info($"skill icons: refreshing failed: {(e.InnerException ?? e).Message}"); return false; }
        }

        static string BarIcons()
        {
            Component hud = Hud();
            if (hud == null) return null;
            try
            {
                object sprites = hud.GetType().GetMethod("GetAllSkillSprites")?.Invoke(hud, null);
                if (!(sprites is IEnumerable list)) return null;
                var names = new List<string>();
                foreach (object s in list) names.Add(s is Sprite sp && sp != null ? sp.name : "none");
                return "[" + string.Join(", ", names) + "]";
            }
            catch (Exception e) { return "(couldn't read: " + (e.InnerException ?? e).Message + ")"; }
        }
    }
}
