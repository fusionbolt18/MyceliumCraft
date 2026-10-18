using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MyceliumCraft
{
    /// <summary>
    /// Which game owns which key while Minecraft drives the spore (your choice, 2026-10-17):
    ///
    ///   Minecraft: left click (attack), right click (use), Shift (sneak), Space (jump), E (inventory),
    ///              Q (drop), Ctrl (sprint), and everything else the bridge sends.
    ///   Mycelium:  R (interact), Tab (show stats), plus Escape/P (pause), which the bridge never sends.
    ///   Unbound:   dodge roll, skill 1, skill 2, navigate camera, reset camera, Minecraft's player list.
    ///
    /// Mycelium side: binding overrides on its "Player" input actions (Unity Input System), applied
    /// to every copy of its input asset (about 20 scripts make their own), found through
    /// InputSystem.ListEnabledActions(), and removed again when Minecraft stops driving. Mycelium's
    /// code is unchanged. Its developer cheat panel (TestingManager reads digits, K, L, Z, X, Minus
    /// directly; digits are Minecraft's hotbar) is switched off while Minecraft drives.
    /// Minecraft side: InputBridge doesn't send R and Tab.
    /// </summary>
    sealed class KeyOwnership
    {
        // (action, binding path in Mycelium's asset, new path; "" = unbound)
        static readonly (string action, string from, string to)[] Rules =
        {
            ("Attack", "<Mouse>/leftButton", ""),          // left click is Minecraft's attack
            ("Curio", "<Mouse>/leftButton", ""),           // Mycelium's curio (wave) also used left click
            ("Interact", "<Mouse>/rightButton", "<Keyboard>/r"), // right click is Minecraft's use; interact moves to R
            ("Dodge", "<Keyboard>/leftShift", ""),         // Shift is Minecraft's sneak
            ("Stat_Skill_1", "<Keyboard>/q", ""),          // Q is Minecraft's drop
            ("Stat_Skill_2", "<Keyboard>/e", ""),          // E is Minecraft's inventory
            ("NavigateCamera", "<Keyboard>/leftCtrl", ""), // Ctrl is Minecraft's sprint
            ("ResetCamera", "<Keyboard>/tab", ""),         // Tab moves to show stats
            ("ShowStats", "<Keyboard>/space", "<Keyboard>/tab"), // Space is Minecraft's jump; stats move to Tab
            ("HideStats", "<Keyboard>/space", "<Keyboard>/tab"),
        };

        readonly HashSet<InputActionAsset> done = new HashSet<InputActionAsset>();
        readonly List<(InputAction action, int index)> applied = new List<(InputAction, int)>();
        Behaviour cheats;
        bool cheatsWereEnabled;
        bool active;
        float nextScan;
        string lastPressed = "";

        public bool Active => active;
        public int Assets => done.Count;

        /// <summary>Call every frame. driving: Minecraft drives the spore (and the bridge rules apply).</summary>
        public void Update(bool driving)
        {
            LogPressed();
            if (!McConfig.KeyOwnership) driving = false;
            if (!driving)
            {
                if (active) Release();
                return;
            }
            if (!active)
            {
                active = true;
                SwitchCheatsOff();
                nextScan = 0f;
            }
            if (Time.realtimeSinceStartup < nextScan) return;
            nextScan = Time.realtimeSinceStartup + 0.5f;
            // Every copy of Mycelium's input asset with its "Player" actions in use.
            foreach (InputActionAsset asset in InputSystem.ListEnabledActions()
                         .Select(a => a.actionMap?.asset).Where(a => a != null).Distinct())
            {
                if (done.Contains(asset)) continue;
                InputActionMap map = asset.FindActionMap("Player");
                if (map == null || map.FindAction("Dodge") == null) continue; // not Mycelium's player asset
                done.Add(asset);
                int changed = 0;
                foreach (var rule in Rules)
                {
                    InputAction action = map.FindAction(rule.action);
                    if (action == null) continue;
                    for (int i = 0; i < action.bindings.Count; i++)
                    {
                        if (action.bindings[i].path != rule.from) continue;
                        action.ApplyBindingOverride(i, rule.to);
                        applied.Add((action, i));
                        changed++;
                    }
                }
                McLog.Info($"keys: Mycelium input asset #{done.Count} ({asset.name}): {changed} bindings changed. Now: {Describe(map)}");
            }
        }

        /// <summary>Log whenever the set of Mycelium "Player" actions being pressed changes (which game reacted to a key).</summary>
        void LogPressed()
        {
            var pressed = new SortedSet<string>();
            foreach (InputAction a in InputSystem.ListEnabledActions())
            {
                if (a.actionMap?.name != "Player" || a.name == "Move" || a.name == "Look") continue;
                if (a.IsPressed()) pressed.Add(a.name);
            }
            string now = string.Join(",", pressed);
            if (now == lastPressed) return;
            lastPressed = now;
            McLog.Info($"keys: Mycelium actions pressed now: [{now}]");
        }

        static string Describe(InputActionMap map)
        {
            var parts = new List<string>();
            foreach (var rule in Rules.Select(r => r.action).Distinct())
            {
                InputAction a = map.FindAction(rule);
                if (a == null) continue;
                var keys = a.bindings.Where(b => !b.isComposite && !b.isPartOfComposite && (b.effectivePath ?? "").StartsWith("<Keyboard>") || (b.effectivePath ?? "").StartsWith("<Mouse>"))
                                     .Select(b => b.effectivePath).ToList();
                parts.Add($"{rule}=[{string.Join(" ", keys)}]");
            }
            return string.Join(", ", parts);
        }

        void SwitchCheatsOff()
        {
            Type t = Type.GetType("TestingManager, Assembly-CSharp");
            cheats = t != null ? UnityEngine.Object.FindObjectOfType(t) as Behaviour : null;
            if (cheats == null) return;
            cheatsWereEnabled = cheats.enabled;
            cheats.enabled = false;
            McLog.Info($"keys: Mycelium's developer cheat keys (TestingManager) off while Minecraft drives (was {(cheatsWereEnabled ? "on" : "off")})");
        }

        void Release()
        {
            int restored = 0;
            foreach (var (action, index) in applied)
            {
                if (action == null) continue;
                try { action.RemoveBindingOverride(index); restored++; } catch (Exception) { }
            }
            applied.Clear();
            done.Clear();
            if (cheats != null) cheats.enabled = cheatsWereEnabled;
            cheats = null;
            active = false;
            McLog.Info($"keys: Mycelium's own key bindings back ({restored} restored)");
        }
    }
}
