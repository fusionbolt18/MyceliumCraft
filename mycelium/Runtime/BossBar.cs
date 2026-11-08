using System;
using System.Reflection;
using System.Text.RegularExpressions;
using MyceliumCraft.Link;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MyceliumCraft
{
    /// <summary>
    /// On the final level (Impact Barrens) the boss's health is Minecraft's boss bar instead of
    /// Mycelium's (your request, 2026-11-07).
    ///
    /// Mycelium's bar is HUDBoss on the "HUD" object: bossHealthHolder grows open when you meet the
    /// boss (BossHealth's trigger) and shrinks shut once it's beaten; bossHealthBar.fillAmount is its
    /// health. While Minecraft drives, the holder is switched off, and what it would show goes to
    /// Minecraft (Boss block, MycBossBar.java): the name without its colour tags, and the fill.
    /// Shown while the holder is open more than half way, as Mycelium's grows and shrinks.
    /// </summary>
    sealed class BossBar
    {
        const string FinalLevel = "Impact Barrens";

        Component hud;
        GameObject holder;
        Image fill;
        Component nameText; // a TextMeshPro text: its "text" read by name (this assembly doesn't reference TextMeshPro)
        bool hiding, wasOn, lastShown;
        float nextFind;

        /// <summary>driving: Minecraft drives (re-sync teleports and Mycelium holding the controls count). cutscene: Mycelium has the controls, not for a knockback.</summary>
        public void Update(MyceliumLink link, bool driving, bool cutscene)
        {
            bool here = driving && SceneManager.GetActiveScene().name == FinalLevel;
            if (here && hud == null && Time.realtimeSinceStartup >= nextFind)
            {
                nextFind = Time.realtimeSinceStartup + 1f;
                Find();
            }
            if (!here || hud == null)
            {
                Restore();
                Send(link, false, "", 0f);
                return;
            }
            if (!hiding)
            {
                hiding = true;
                wasOn = holder.activeSelf;
                McLog.Info($"boss bar: Mycelium's hidden while Minecraft drives (on before: {wasOn}); Minecraft's shows it");
            }
            if (holder.activeSelf)
            {
                wasOn = true;
                holder.SetActive(false);
            }
            // Only once the fight is on (your reports, 2026-11-07: it showed too early). Mycelium opens
            // its bar when you come near (BossHealth's trigger), 8 s before the intro in your log, and
            // switches the boss's attacks on (MonsterBossAttack) only when the intro is over. Nor while
            // Mycelium has the controls (its death takes them).
            bool open = holder.transform.localScale.x > 0.5f && FightOn() && !cutscene;
            string raw = nameText != null ? nameText.GetType().GetProperty("text")?.GetValue(nameText) as string : null;
            string name = Regex.Replace(raw ?? "", "<[^>]*>", "").Trim();
            Send(link, open, name, fill != null ? Mathf.Clamp01(fill.fillAmount) : 0f);
        }

        void Send(MyceliumLink link, bool shown, string name, float progress)
        {
            link.WriteBoss(shown, name, progress);
            if (shown != lastShown)
            {
                lastShown = shown;
                McLog.Info(shown ? $"boss bar: '{name}' at {progress:P0} shown in Minecraft" : "boss bar: none in Minecraft");
            }
        }

        /// <summary>The boss's attacks are on: StartBoss switches them off, BossCam on at the intro's end.</summary>
        bool FightOn()
        {
            if (attacks == null)
            {
                GameObject boss = GameObject.Find("Rival Sporemother");
                Type t = Type.GetType("MonsterBossAttack, Assembly-CSharp");
                attacks = boss != null && t != null ? boss.GetComponent(t) as Behaviour : null;
                if (attacks == null) return false;
            }
            bool on = attacks.enabled;
            if (on != lastFightOn)
            {
                lastFightOn = on;
                McLog.Info(on ? "boss bar: the boss's attacks are on: the fight has begun" : "boss bar: the boss's attacks are off");
            }
            return on;
        }
        Behaviour attacks;
        bool lastFightOn;

        void Find()
        {
            Type t = Type.GetType("HUDBoss, Assembly-CSharp");
            GameObject h = GameObject.Find("HUD");
            Component c = h != null && t != null ? h.GetComponent(t) : null;
            if (c == null) return;
            const BindingFlags f = BindingFlags.NonPublic | BindingFlags.Instance;
            holder = t.GetField("bossHealthHolder", f)?.GetValue(c) as GameObject;
            fill = t.GetField("bossHealthBar", f)?.GetValue(c) as Image;
            nameText = t.GetField("bossNameText", f)?.GetValue(c) as Component;
            if (holder == null) { McLog.Info("boss bar: HUDBoss has no bossHealthHolder; Mycelium's bar stays"); return; }
            hud = c;
            McLog.Info($"boss bar: found Mycelium's (bar {(fill != null)}, name {(nameText != null)})");
        }

        void Restore()
        {
            if (!hiding) return;
            hiding = false;
            if (holder != null) holder.SetActive(wasOn);
            McLog.Info($"boss bar: Mycelium's back as it was (on: {wasOn})");
        }

        /// <summary>A new scene: its HUD may be a new one.</summary>
        public void SceneLoaded()
        {
            Restore();
            hud = null;
            holder = null;
            attacks = null;
            lastFightOn = false;
            nextFind = 0f;
        }
    }
}
