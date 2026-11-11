using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MyceliumCraft.Link;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MyceliumCraft
{
    /// <summary>
    /// Mycelium's key hints show the keys you bound in Minecraft (your request, 2026-11-01: a loot
    /// cache said [RMB] with interact on R; the skill bar said Q and E).
    ///
    /// Every hint Mycelium shows (pop-ups' "Press [..] to ...", the skill bar, curios) is built from
    /// the current controller's ColoredHint in InputManager (interactHint, statSkill1Hint...), whose
    /// text is fixed in Mycelium's settings. While Minecraft drives, the Keyboard controller's hints
    /// get the names of the keys bound in Minecraft's Controls menu (the Bindings block, the same as
    /// KeyOwnership uses), the skill bar is refreshed (HUDControls.ChangeHUDControls), and Mycelium's
    /// own texts are put back when it stops. Re-applied whenever the bindings change or Mycelium makes
    /// a new InputManager (a scene change). Mycelium's code is unchanged.
    ///
    /// Curios (your report, 2026-11-10: their indicator didn't show the key): a curio is used with
    /// Mycelium's Curio action (PlayerController: curioInteract), but the indicator above it
    /// (Curio.PopInteractCanvas) always shows the Attack hint, and there's no curio hint to change.
    /// So while Minecraft drives, every curio's indicator text (its interactCanvas's TMP_Text) is set
    /// to the key bound to Curio in Minecraft, in the same colours, whenever Mycelium writes it again.
    /// </summary>
    sealed class KeyHints
    {
        // Hint field -> the binding it shows (Bindings index, as MyceliumKeys.java / KeyOwnership).
        static readonly (string hint, int id)[] Hints =
        {
            ("interactHint", 0), ("showStatsHint", 1), ("subspeciesSkillHint", 2), ("salvageHint", 3),
            ("attackHint", 4), ("dodgeHint", 6), ("statSkill1Hint", 7), ("statSkill2Hint", 8),
            ("goalCameraHint", 9), ("orientationCameraHint", 10),
        };
        const int MinecraftAttack = 11; // Minecraft's own attack key: what attacks while Mycelium's Attack is unbound
        const int CurioId = 5;          // Mycelium's Curio action (MyceliumKeys.java "curio")
        const float CurioLook = 0.1f;   // seconds between looks at the curios' indicators
        const float CurioFind = 1f;     // seconds between finding the curios again

        string curioLabel, curioText;   // the Curio key, and the indicator text it makes
        float nextCurioLook, nextCurioFind;
        UnityEngine.Object[] curios = new UnityEngine.Object[0];
        static readonly Type CurioType = Type.GetType("Curio, Assembly-CSharp");
        static readonly Type TmpText = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro");
        // Curio's own private field: looked up on Curio (a bed's or chair's type doesn't list its base's private fields).
        static readonly FieldInfo CanvasField = CurioType?.GetField("interactCanvas", BindingFlags.NonPublic | BindingFlags.Instance);

        static readonly string[] MouseNames = { null, "LMB", "MMB", "RMB", "MB4", "MB5" }; // other buttons: MB<n> (your choice, 2026-11-01)

        object manager;   // the InputManager the texts are on
        object keyboard;  // its Keyboard controller
        readonly Dictionary<string, (string text, bool sprite)> original = new Dictionary<string, (string, bool)>();
        string applied;   // the labels applied (null: Mycelium's own)

        /// <summary>Every frame. driving: Minecraft drives (KeyOwnership.Active). count, kinds, codes: Minecraft's bindings.</summary>
        public void Update(bool driving, int count, uint[] kinds, uint[] codes)
        {
            object now = Instance();
            if (!ReferenceEquals(now, manager))
            {
                // A new InputManager (or none): its texts are Mycelium's own.
                manager = now;
                keyboard = KeyboardController(now);
                original.Clear();
                applied = null;
            }
            if (keyboard == null) return;
            if (!driving || count <= 0)
            {
                if (applied != null) Restore();
                return;
            }
            var labels = new string[Hints.Length];
            for (int i = 0; i < Hints.Length; i++)
            {
                int id = Hints[i].id;
                string label = Label(id, count, kinds, codes);
                if (id == 4 && label == null) label = Label(MinecraftAttack, count, kinds, codes); // Mycelium's Attack unbound: Minecraft's attack does it
                labels[i] = label ?? "Unbound";
            }
            string sig = string.Join("|", labels);
            if (sig != applied)
            {
                for (int i = 0; i < Hints.Length; i++) Set(Hints[i].hint, labels[i]);
                applied = sig;
                RefreshHud();
                var parts = new List<string>();
                for (int i = 0; i < Hints.Length; i++) parts.Add($"{Hints[i].hint}=[{labels[i]}]");
                McLog.Info("key hints: Mycelium's hints show your Minecraft key binds: " + string.Join(", ", parts) + ". " + AsPopUpWouldSay());
            }
            Curios(Label(CurioId, count, kinds, codes) ?? "Unbound");
        }

        /// <summary>Every curio's indicator shows the Curio key (Mycelium writes the Attack hint there).</summary>
        void Curios(string label)
        {
            if (CurioType == null || TmpText == null) return;
            if (label != curioLabel)
            {
                curioLabel = label;
                // Mycelium's look: "[<color=#..>key</color>]" in the keyboard attack hint's colour.
                object attack = Field(keyboard, "attackHint");
                string made = null;
                try
                {
                    string key = attack?.GetType().GetField("controlText")?.GetValue(attack) as string;
                    string whole = attack?.GetType().GetMethod("GenerateColoredHintString")?.Invoke(attack, new object[] { false }) as string;
                    if (whole != null && key != null && whole.Contains(">" + key + "</color>"))
                        made = whole.Replace(">" + key + "</color>", ">" + label + "</color>");
                }
                catch (Exception e) { McLog.Info($"key hints: couldn't build the curio hint in Mycelium's colours: {(e.InnerException ?? e).Message}"); }
                curioText = made ?? $"[<color=#BDFFD3>{label}</color>]";
                McLog.Info($"key hints: curio indicators show the Curio key [{label}] (Mycelium shows the Attack hint there): {curioText}");
            }
            float now = Time.unscaledTime;
            if (now >= nextCurioFind)
            {
                nextCurioFind = now + CurioFind;
                curios = UnityEngine.Object.FindObjectsOfType(CurioType);
            }
            if (now < nextCurioLook) return;
            nextCurioLook = now + CurioLook;
            int changed = 0;
            foreach (UnityEngine.Object c in curios)
            {
                if (c == null) continue;
                if (!(CanvasField?.GetValue(c) is GameObject canvas) || canvas == null) continue;
                Component text = canvas.GetComponentInChildren(TmpText, true);
                if (text == null) continue;
                PropertyInfo p = TmpText.GetProperty("text");
                if (p == null || p.GetValue(text) as string == curioText) continue;
                p.SetValue(text, curioText);
                changed++;
            }
            if (changed > 0 && now >= nextCurioLog)
            {
                nextCurioLog = now + 5f;
                McLog.Info($"key hints: {changed} curio indicator(s) set to [{curioLabel}] ({curios.Length} curio(s) in the level)");
            }
        }
        float nextCurioLog;

        /// <summary>The interact hint as a pop-up builds it (LootCache: GetLatestController().interactHint), for the log.</summary>
        string AsPopUpWouldSay()
        {
            try
            {
                object current = manager.GetType().GetMethod("GetLatestController")?.Invoke(manager, null);
                object hint = Field(keyboard, "interactHint");
                object text = hint?.GetType().GetMethod("GenerateColoredHintString")?.Invoke(hint, new object[] { false });
                return $"With the keyboard, a loot cache would say \"Press {text} to Open\" (Mycelium's current controller: {Field(current, "controllerName")}; it switches to the keyboard when you press a key)";
            }
            catch (Exception e) { return "(couldn't build a pop-up's hint: " + (e.InnerException ?? e).Message + ")"; }
        }

        void Restore()
        {
            foreach (var pair in original)
            {
                object hint = Field(keyboard, pair.Key);
                if (hint == null) continue;
                hint.GetType().GetField("controlText")?.SetValue(hint, pair.Value.text);
                hint.GetType().GetField("isHintSprite")?.SetValue(hint, pair.Value.sprite);
            }
            applied = null;
            RefreshHud();
            McLog.Info("key hints: Minecraft isn't driving; Mycelium's own hints are back");
        }

        void Set(string name, string label)
        {
            object hint = Field(keyboard, name);
            if (hint == null) return;
            Type t = hint.GetType();
            if (!original.ContainsKey(name))
                original[name] = ((string)t.GetField("controlText")?.GetValue(hint), t.GetField("isHintSprite")?.GetValue(hint) is bool s && s);
            t.GetField("controlText")?.SetValue(hint, label);
            t.GetField("isHintSprite")?.SetValue(hint, false); // a picture of Mycelium's key would be wrong now
        }

        /// <summary>The skill bar copies its hints once: tell it again (only if the keyboard is what it shows).</summary>
        void RefreshHud()
        {
            Type gd = Type.GetType("GlobalData, Assembly-CSharp");
            object latest = gd?.GetField("latestController", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (latest == null || latest.ToString() != "Keyboard") return;
            GameObject hud = GameObject.Find("HUD");
            Type ht = Type.GetType("HUDControls, Assembly-CSharp");
            Component controls = hud != null && ht != null ? hud.GetComponent(ht) : null;
            if (controls == null) return;
            try { ht.GetMethod("ChangeHUDControls")?.Invoke(controls, new[] { keyboard }); }
            catch (Exception e) { McLog.Info($"key hints: refreshing the skill bar failed: {(e.InnerException ?? e).Message}"); }
        }

        /// <summary>A binding's name as Mycelium writes them (R, Tab, Left Shift, LMB...); null if unbound.</summary>
        static string Label(int id, int count, uint[] kinds, uint[] codes)
        {
            if (id >= count || kinds == null || codes == null) return null;
            uint kind = kinds[id], code = codes[id];
            if (kind == Proto.BindMouse) return code < MouseNames.Length && MouseNames[code] != null ? MouseNames[code] : $"MB{code}";
            if (kind != Proto.BindKey || !InputBridge.KeyOf((int)code, out Key key)) return null;
            Keyboard kb = Keyboard.current;
            string name = kb != null ? kb[key].displayName : key.ToString();
            return name.Length == 1 ? name.ToUpperInvariant() : name;
        }

        static object Instance() =>
            Type.GetType("InputManager, Assembly-CSharp")?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is UnityEngine.Object o && o != null ? o : null;

        static object KeyboardController(object manager)
        {
            if (manager == null) return null;
            if (!(Field(manager, "controllers") is IEnumerable list)) return null;
            foreach (object c in list)
                if (c != null && Field(c, "controllerName")?.ToString() == "Keyboard") return c;
            return null;
        }

        static object Field(object o, string name) =>
            o?.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(o);
    }
}
