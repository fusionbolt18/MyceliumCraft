using System;
using System.Reflection;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Leaving the level-up shop without committing puts your max health back (your report,
    /// 2026-11-08: it stayed changed).
    ///
    /// Mycelium's own shop (LevelUpManagerNew) does this: raising a stat there works it out at once
    /// (CharacterStats.CalculateAttributes), which also sets PlayerHealth.maxHealth and the walking
    /// speed. Closing it puts back the stat levels and CharacterStats.baseHealth, but not maxHealth or
    /// the walking speed, so they stay as if you'd bought the levels. Lowering a stat in the shop
    /// doesn't work anything out at all, so committing after "up, then down" kept the higher health.
    /// This is Mycelium's own bug, not ours; it shows in Minecraft because max health is sent there.
    ///
    /// So when the shop closes (Close, Commit, or swapping to another menu and back), the stats are
    /// worked out again from the levels as they are then: CharacterStats.StartCalculateAttributes.
    /// Current health then keeps its share of max health (MinecraftDriver.KeepHealthRatio).
    /// </summary>
    sealed class ShopUndo
    {
        Type managerType;
        Component manager;
        FieldInfo menuField, statsField;
        bool wasOpen;
        float nextLook;

        public void Update()
        {
            if (manager == null)
            {
                wasOpen = false;
                if (Time.realtimeSinceStartup < nextLook) return;
                nextLook = Time.realtimeSinceStartup + 1f;
                if (!Find()) return;
            }
            GameObject menu = menuField.GetValue(manager) as GameObject;
            bool open = menu != null && menu.activeInHierarchy;
            if (open == wasOpen) return;
            wasOpen = open;
            if (open) { McLog.Info("shop: open"); return; }
            Component stats = statsField.GetValue(manager) as Component;
            MethodInfo calc = stats != null ? stats.GetType().GetMethod("StartCalculateAttributes", BindingFlags.Public | BindingFlags.Instance) : null;
            if (stats == null || calc == null || !stats.gameObject.activeInHierarchy)
            {
                McLog.Info("shop: closed; no CharacterStats to work out again");
                return;
            }
            try
            {
                calc.Invoke(stats, null); // runs up to its first wait at once: maxHealth is set by the time this returns
                McLog.Info($"shop: closed; stats worked out again from the levels (Vitality {Field(stats, "vitalityLevel")}, base health {Field(stats, "baseHealth")})");
            }
            catch (Exception e)
            {
                McLog.Info($"shop: working the stats out again failed: {(e.InnerException ?? e).Message}");
            }
        }

        static object Field(Component c, string name) =>
            c.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(c);

        bool Find()
        {
            if (managerType == null)
            {
                managerType = Type.GetType("LevelUpManagerNew, Assembly-CSharp");
                if (managerType == null) return false;
                menuField = managerType.GetField("UIenable", BindingFlags.Public | BindingFlags.Instance);
                statsField = managerType.GetField("currentstats", BindingFlags.Public | BindingFlags.Instance);
            }
            if (menuField == null || statsField == null) return false;
            // The shop's object is switched off while it's shut, so look among the switched-off ones too.
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(managerType))
                if (o is Component c && c.gameObject.scene.IsValid())
                {
                    manager = c;
                    McLog.Info($"shop: found Mycelium's level-up shop on '{c.gameObject.name}'");
                    return true;
                }
            return false;
        }

        /// <summary>A new scene: its shop is a new one.</summary>
        public void SceneLoaded()
        {
            manager = null;
            nextLook = 0f;
        }

        /// <summary>For the test aid.</summary>
        public Component Manager => manager;
    }
}
