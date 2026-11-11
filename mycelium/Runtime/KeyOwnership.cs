using System;
using System.Collections.Generic;
using System.Linq;
using MyceliumCraft.Link;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MyceliumCraft
{
    /// <summary>
    /// Which game owns which key while Minecraft drives the spore.
    ///
    /// Mycelium's actions are key binds in Minecraft's Options -> Controls -> Key Binds, section
    /// "Mycelium" (MyceliumKeys.java). Minecraft sends what each is bound to (Bindings block in
    /// protocol/PROTOCOL.md). Here those become binding overrides on Mycelium's "Player" input
    /// actions (Unity Input System): each action's first keyboard/mouse binding is set to the chosen
    /// key, its other keyboard/mouse bindings are switched off, and gamepad bindings stay. They're
    /// applied to every copy of Mycelium's input asset (about 20 scripts make their own), found
    /// through InputSystem.ListEnabledActions(), and removed again when Minecraft stops driving.
    /// Mycelium's code is unchanged.
    ///
    /// A key bound to a Mycelium action belongs to Mycelium: the input bridge doesn't send it to
    /// Minecraft (OwnedScancodes, OwnedButtons), except one that's also Minecraft's List Players key
    /// (id 12, since v40; your request, 2026-11-03: Tab both shows stats and lists players): that
    /// one goes to both. Everything else goes to Minecraft. Escape and P
    /// always stay Mycelium's pause keys. Until Minecraft sends bindings, Defaults are used (your
    /// choice of 2026-10-17: R interact, Tab show stats).
    ///
    /// Mycelium's developer cheat panel (TestingManager reads digits, K, L, Z, X, Minus directly;
    /// digits are Minecraft's hotbar) is switched off while Minecraft drives.
    /// </summary>
    sealed class KeyOwnership
    {
        // Action id (the order in MyceliumKeys.java) -> Mycelium's action names. Show stats drives
        // both ShowStats (press) and HideStats (release).
        static readonly (string id, string[] actions)[] Actions =
        {
            ("interact", new[] { "Interact" }),
            ("show_stats", new[] { "ShowStats", "HideStats" }),
            ("subspecies_skill", new[] { "Subspecies_Skill" }),
            ("salvage", new[] { "Salvage" }),
            ("attack", new[] { "Attack" }),
            ("curio", new[] { "Curio" }),
            ("dodge", new[] { "Dodge" }),
            ("skill_1", new[] { "Stat_Skill_1" }),
            ("skill_2", new[] { "Stat_Skill_2" }),
            ("navigate_camera", new[] { "NavigateCamera" }),
            ("reset_camera", new[] { "ResetCamera" }),
        };

        // Same as the defaults in MyceliumKeys.java (2026-11-10): right click interact, Tab show stats,
        // Z subspecies skill, middle click salvage, attack none, middle click curio, Left Alt dodge roll,
        // X skill 1, C skill 2, navigate and reset camera none. SDL scancodes / SDL mouse buttons.
        static readonly (uint kind, uint code)[] Defaults =
        {
            (Proto.BindMouse, 3), (Proto.BindKey, 43), (Proto.BindKey, 29), (Proto.BindMouse, 2),
            (Proto.BindNone, 0), (Proto.BindMouse, 2), (Proto.BindKey, 226), (Proto.BindKey, 27),
            (Proto.BindKey, 6), (Proto.BindNone, 0), (Proto.BindNone, 0),
        };

        static readonly string[] MouseNames = { null, "leftButton", "middleButton", "rightButton", "backButton", "forwardButton" };

        // The skill menu assigns a skill to slot 1 or 2 with its own keyboard actions (UISub map,
        // SkillMenuManagerUpdated; Q and E): those follow Skill 1 and Skill 2 too (your report,
        // 2026-11-01). An unbound skill key leaves Mycelium's Q or E, so you can still assign.
        static readonly (string action, int id)[] MenuActions = { ("Assign Y KB", 7), ("Assign B KB", 8) };

        readonly HashSet<InputActionAsset> done = new HashSet<InputActionAsset>();
        readonly List<(InputAction action, int index)> applied = new List<(InputAction, int)>();
        readonly string[] paths = new string[Actions.Length];   // Unity binding path per action id; "" = unbound
        readonly HashSet<int> ownedScancodes = new HashSet<int>();
        uint ownedButtons;
        string signature;
        bool fromMinecraft;
        Behaviour cheats;
        bool cheatsWereEnabled;
        bool active;
        float nextScan;
        string lastPressed = "";

        public bool Active => active;
        const float ReleaseAfterSeconds = 0.5f;
        float notDrivingSince = -1f;
        public bool HasBindings => signature != null;
        public int Assets => done.Count;
        /// <summary>SDL scancodes bound to a Mycelium action: not sent to Minecraft.</summary>
        public HashSet<int> OwnedScancodes => ownedScancodes;
        /// <summary>Mouse buttons (bit n = SDL button n) bound to a Mycelium action: not sent to Minecraft.</summary>
        public uint OwnedButtons => ownedButtons;

        public KeyOwnership()
        {
            SetBindings(0, null, null);
        }

        /// <summary>
        /// Call every frame with what Minecraft sent (count 0: nothing yet, use the defaults).
        /// Re-applies the overrides if anything changed.
        /// </summary>
        public void SetBindings(int count, uint[] kinds, uint[] codes)
        {
            bool mc = count > 0;
            Keyboard kb = Keyboard.current;
            if (kb == null) return; // no keyboard yet: can't name keys; tried again next frame
            var newPaths = new string[Actions.Length];
            var newScancodes = new HashSet<int>();
            uint newButtons = 0;
            for (int i = 0; i < Actions.Length; i++)
            {
                uint kind = mc ? (i < count ? kinds[i] : Proto.BindNone) : Defaults[i].kind;
                uint code = mc ? (i < count ? codes[i] : 0) : Defaults[i].code;
                string path = "";
                if (kind == Proto.BindKey && InputBridge.KeyOf((int)code, out Key key))
                {
                    path = "<Keyboard>/" + kb[key].name;
                    newScancodes.Add((int)code);
                }
                else if (kind == Proto.BindMouse && code < MouseNames.Length && MouseNames[code] != null)
                {
                    path = "<Mouse>/" + MouseNames[code];
                    newButtons |= 1u << (int)code;
                }
                newPaths[i] = path;
            }
            // Minecraft's List Players key (after Mycelium's actions and Minecraft's Attack): shared.
            string shared = "";
            int list = Actions.Length + 1;
            if (mc && count > list)
            {
                if (kinds[list] == Proto.BindKey && newScancodes.Remove((int)codes[list])) shared = $"scancode {codes[list]}";
                else if (kinds[list] == Proto.BindMouse && codes[list] < 32 && (newButtons & (1u << (int)codes[list])) != 0)
                {
                    newButtons &= ~(1u << (int)codes[list]);
                    shared = $"mouse button {codes[list]}";
                }
            }
            string sig = string.Join("|", newPaths) + "|" + shared;
            if (sig == signature && mc == fromMinecraft) return;
            signature = sig;
            fromMinecraft = mc;
            Array.Copy(newPaths, paths, paths.Length);
            ownedScancodes.Clear();
            ownedScancodes.UnionWith(newScancodes);
            ownedButtons = newButtons;
            McLog.Info($"keys: Mycelium key binds {(mc ? "from Minecraft's Controls menu" : "(defaults; Minecraft hasn't sent any)")}: " +
                       string.Join(", ", Actions.Select((a, i) => $"{a.id}={(paths[i] == "" ? "unbound" : paths[i])}")) +
                       $". Not sent to Minecraft: scancodes [{string.Join(",", ownedScancodes.OrderBy(c => c))}], mouse buttons mask {ownedButtons}" +
                       (shared != "" ? $". Sent to both (also Minecraft's List Players): {shared}" : ""));
            if (active)
            {
                // Bindings changed while driving: take the old overrides off and apply the new ones.
                RemoveOverrides();
                nextScan = 0f;
            }
        }

        /// <summary>
        /// Call every frame. driving: Minecraft drives the spore (and the bridge rules apply).
        /// mcScreenOpen: a Minecraft screen (chat, inventory, pause menu) has the keyboard and mouse.
        /// </summary>
        public void Update(bool driving, bool mcScreenOpen)
        {
            LogPressed();
            if (!McConfig.KeyOwnership) driving = false;
            if (!driving)
            {
                // Give Mycelium its keys back only once Minecraft has really stopped driving for a
                // moment (as the camera does), never for a brief gap.
                if (active)
                {
                    if (notDrivingSince < 0f) notDrivingSince = Time.realtimeSinceStartup;
                    else if (Time.realtimeSinceStartup - notDrivingSince >= ReleaseAfterSeconds || !McConfig.KeyOwnership) Release();
                }
                return;
            }
            notDrivingSince = -1f;
            if (!active)
            {
                active = true;
                SwitchCheatsOff();
                nextScan = 0f;
            }
            if (Time.realtimeSinceStartup >= nextScan)
            {
                nextScan = Time.realtimeSinceStartup + 0.5f;
                ScanAssets();
            }
            Silence(mcScreenOpen);
        }

        // Mycelium's action maps switched off while a Minecraft screen is open, to switch back on after.
        readonly HashSet<InputActionMap> silenced = new HashSet<InputActionMap>();
        bool silencing;

        /// <summary>This map is off because a Minecraft screen is open (we switched it off), not because of Mycelium.</summary>
        public bool SilencedHere(InputActionMap map) => silencing && silenced.Contains(map);

        /// <summary>
        /// While a Minecraft screen is open, none of Mycelium's controls react: typing "r", "v" or "p"
        /// in Minecraft's chat triggered Mycelium's interact, skill and pause (your report,
        /// 2026-10-18). Every map of Mycelium's input assets that's on gets switched off, again every
        /// frame (in case Mycelium switches one back on), and the same maps back on afterwards.
        /// </summary>
        void Silence(bool on)
        {
            if (on)
            {
                foreach (InputActionAsset asset in done)
                {
                    if (asset == null) continue;
                    foreach (InputActionMap map in asset.actionMaps)
                    {
                        if (!map.enabled) continue;
                        silenced.Add(map);
                        map.Disable();
                    }
                }
                if (!silencing)
                {
                    silencing = true;
                    McLog.Info($"keys: Minecraft screen open, Mycelium's controls off ({silenced.Count} action maps)");
                }
            }
            else if (silencing)
            {
                silencing = false;
                int back = 0;
                foreach (InputActionMap map in silenced)
                    if (map != null) { map.Enable(); back++; }
                silenced.Clear();
                McLog.Info($"keys: Minecraft screen closed, Mycelium's controls back ({back} action maps)");
            }
        }

        void ScanAssets()
        {
            // Every copy of Mycelium's input asset with its "Player" actions in use.
            foreach (InputActionAsset asset in InputSystem.ListEnabledActions()
                         .Select(a => a.actionMap?.asset).Where(a => a != null).Distinct())
            {
                if (done.Contains(asset)) continue;
                InputActionMap map = asset.FindActionMap("Player");
                if (map == null || map.FindAction("Dodge") == null) continue; // not Mycelium's player asset
                done.Add(asset);
                int changed = 0;
                for (int id = 0; id < Actions.Length; id++)
                {
                    foreach (string name in Actions[id].actions)
                    {
                        InputAction action = map.FindAction(name);
                        if (action == null) continue;
                        bool first = true;
                        for (int i = 0; i < action.bindings.Count; i++)
                        {
                            InputBinding binding = action.bindings[i];
                            if (binding.isComposite || binding.isPartOfComposite) continue;
                            string from = binding.path ?? "";
                            if (!from.StartsWith("<Keyboard>") && !from.StartsWith("<Mouse>")) continue; // gamepad etc. stay
                            string to = first ? paths[id] : "";
                            first = false;
                            action.ApplyBindingOverride(i, to);
                            applied.Add((action, i));
                            changed++;
                        }
                    }
                }
                InputActionMap sub = asset.FindActionMap("UISub");
                foreach (var (name, id) in MenuActions)
                {
                    InputAction action = sub?.FindAction(name);
                    if (action == null || paths[id] == "") continue;
                    bool first = true;
                    for (int i = 0; i < action.bindings.Count; i++)
                    {
                        string from = action.bindings[i].path ?? "";
                        if (action.bindings[i].isComposite || action.bindings[i].isPartOfComposite) continue;
                        if (!from.StartsWith("<Keyboard>") && !from.StartsWith("<Mouse>")) continue;
                        action.ApplyBindingOverride(i, first ? paths[id] : "");
                        first = false;
                        applied.Add((action, i));
                        changed++;
                    }
                }
                // Escape is Minecraft's pause menu now; P stays Mycelium's pause and menu.
                InputAction pause = map.FindAction("Pause");
                if (pause != null)
                    for (int i = 0; i < pause.bindings.Count; i++)
                    {
                        if (pause.bindings[i].path != "<Keyboard>/escape") continue;
                        pause.ApplyBindingOverride(i, "");
                        applied.Add((pause, i));
                        changed++;
                    }
                McLog.Info($"keys: Mycelium input asset #{done.Count} ({asset.name}): {changed} bindings set. Now: {Describe(map)}" +
                           $"; skill menu: {string.Join(", ", MenuActions.Select(m => $"{m.action}={(sub?.FindAction(m.action)?.bindings.Select(b => b.effectivePath).FirstOrDefault(p => p.StartsWith("<Keyboard>") || p.StartsWith("<Mouse>")) ?? "?")}"))}");
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
            foreach (string name in Actions.SelectMany(a => a.actions).Append("Pause"))
            {
                InputAction a = map.FindAction(name);
                if (a == null) continue;
                var keys = a.bindings.Where(b => !b.isComposite && !b.isPartOfComposite)
                                     .Select(b => b.effectivePath ?? "")
                                     .Where(p => p.StartsWith("<Keyboard>") || p.StartsWith("<Mouse>"))
                                     .ToList();
                parts.Add($"{name}=[{string.Join(" ", keys)}]");
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

        int RemoveOverrides()
        {
            int restored = 0;
            foreach (var (action, index) in applied)
            {
                if (action == null) continue;
                try { action.RemoveBindingOverride(index); restored++; } catch (Exception) { }
            }
            applied.Clear();
            done.Clear();
            return restored;
        }

        void Release()
        {
            Silence(false);
            int restored = RemoveOverrides();
            if (cheats != null) cheats.enabled = cheatsWereEnabled;
            cheats = null;
            active = false;
            notDrivingSince = -1f;
            McLog.Info($"keys: Mycelium's own key bindings back ({restored} restored)");
        }
    }
}
