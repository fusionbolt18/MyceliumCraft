using System;
using System.Collections.Generic;
using System.Linq;
using MyceliumCraft.Link;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MyceliumCraft
{
    /// <summary>
    /// The input bridge, Mycelium side: reads the keyboard and mouse as Mycelium's (focused) window
    /// sees them and publishes them for Minecraft to play from (MycInput in protocol/PROTOCOL.md),
    /// like SkyCraft's InputBridge. Keys are sent as SDL scancodes (USB HID usage numbers), which
    /// is what Minecraft 26.x uses; Unity's Key values are physical key positions, so a key means the
    /// same physical key in both games.
    ///
    /// Mycelium still sees all its own keys too (it has the focus); KeyOwnership decides which of
    /// them its actions react to. Never sent: P (Mycelium's pause and menu), and the keys and
    /// buttons bound to Mycelium actions in Minecraft's Controls menu (KeyOwnership.OwnedScancodes,
    /// OwnedButtons). Escape is sent: it's Minecraft's pause menu (and closes Minecraft's screens).
    ///
    /// While a Minecraft screen is open (inventory, chat, pause menu), Windows' own mouse pointer is
    /// unlocked and shown (it can leave the window), and its position goes to Minecraft as a
    /// fraction of the picture. Whether Minecraft should act on the rest is decided by the driver (Active),
    /// e.g. not while a Mycelium menu has the controls.
    /// </summary>
    sealed class InputBridge
    {
        // Unity physical key -> SDL scancode (USB HID usage page 0x07), and back.
        static readonly Dictionary<Key, int> Scancodes = BuildScancodes();
        static readonly Dictionary<int, Key> KeysByScancode = Scancodes.ToDictionary(p => p.Value, p => p.Key);

        /// <summary>The Unity key for an SDL scancode (what Minecraft's key binds store).</summary>
        public static bool KeyOf(int scancode, out Key key) => KeysByScancode.TryGetValue(scancode, out key);

        static Dictionary<Key, int> BuildScancodes()
        {
            var m = new Dictionary<Key, int>();
            for (int i = 0; i < 26; i++) m[Key.A + i] = 4 + i;                 // A..Z
            for (int i = 1; i <= 9; i++) m[Key.Digit1 + (i - 1)] = 29 + i;     // 1..9 -> 30..38
            m[Key.Digit0] = 39;
            m[Key.Enter] = 40; m[Key.Escape] = 41; m[Key.Backspace] = 42; m[Key.Tab] = 43; m[Key.Space] = 44;
            m[Key.Minus] = 45; m[Key.Equals] = 46; m[Key.LeftBracket] = 47; m[Key.RightBracket] = 48;
            m[Key.Backslash] = 49; m[Key.Semicolon] = 51; m[Key.Quote] = 52; m[Key.Backquote] = 53;
            m[Key.Comma] = 54; m[Key.Period] = 55; m[Key.Slash] = 56; m[Key.CapsLock] = 57;
            for (int i = 0; i < 12; i++) m[Key.F1 + i] = 58 + i;                // F1..F12
            m[Key.PrintScreen] = 70; m[Key.ScrollLock] = 71; m[Key.Pause] = 72; m[Key.Insert] = 73;
            m[Key.Home] = 74; m[Key.PageUp] = 75; m[Key.Delete] = 76; m[Key.End] = 77; m[Key.PageDown] = 78;
            m[Key.RightArrow] = 79; m[Key.LeftArrow] = 80; m[Key.DownArrow] = 81; m[Key.UpArrow] = 82;
            m[Key.NumLock] = 83; m[Key.NumpadDivide] = 84; m[Key.NumpadMultiply] = 85; m[Key.NumpadMinus] = 86;
            m[Key.NumpadPlus] = 87; m[Key.NumpadEnter] = 88;
            for (int i = 1; i <= 9; i++) m[Key.Numpad1 + (i - 1)] = 88 + i;     // 89..97
            m[Key.Numpad0] = 98; m[Key.NumpadPeriod] = 99; m[Key.ContextMenu] = 101; m[Key.NumpadEquals] = 103;
            m[Key.LeftCtrl] = 224; m[Key.LeftShift] = 225; m[Key.LeftAlt] = 226; m[Key.LeftMeta] = 227;
            m[Key.RightCtrl] = 228; m[Key.RightShift] = 229; m[Key.RightAlt] = 230; m[Key.RightMeta] = 231;
            return m;
        }

        readonly byte[] keys = new byte[Proto.InKeyBytes];
        readonly uint[] text = new uint[Proto.InTextRing];
        uint textCount;
        double totalDX, totalDY, totalScroll;
        Keyboard hookedKeyboard;
        bool wasActive;
        uint lastReason = uint.MaxValue;
        int keysDown;

        public bool Active => wasActive;
        /// <summary>Windows' mouse pointer is free (a Minecraft screen is open).</summary>
        public bool CursorShown => cursorShown;
        bool cursorShown;
        Vector2 cursor;            // pointer position, Unity screen pixels from the bottom left
        CursorLockMode lockBefore; // how Mycelium had the pointer, to put back
        bool visibleBefore;
        public uint Reason => lastReason;
        public int KeysDown => keysDown;
        public double TotalDX => totalDX;
        public double TotalDY => totalDY;

        /// <summary>Call every frame: read the devices and publish. active: Minecraft should play from it.</summary>
        /// owned: keys and buttons that belong to Mycelium actions (not sent).
        /// mcScreenOpen: a Minecraft screen is open (the mouse moves a cursor, not the view).
        public void Update(MyceliumLink link, bool active, uint reason, HashSet<int> ownedScancodes, uint ownedButtons, bool mcScreenOpen, bool freecamRight = false, bool freecamMouse = false)
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (kb != hookedKeyboard)
            {
                if (hookedKeyboard != null) hookedKeyboard.onTextInput -= OnText;
                hookedKeyboard = kb;
                if (kb != null) kb.onTextInput += OnText;
            }

            Array.Clear(keys, 0, keys.Length);
            keysDown = 0;
            if (kb != null)
            {
                foreach (var pair in Scancodes)
                {
                    // P: Mycelium's pause key. Owned: bound to a Mycelium action. Both still go to
                    // Minecraft while one of its screens is open (Mycelium's controls are off then,
                    // and chat needs Tab, for example).
                    if (!mcScreenOpen && (pair.Key == Key.P || ownedScancodes.Contains(pair.Value))) continue;
                    if (!kb[pair.Key].isPressed) continue;
                    keys[pair.Value >> 3] |= (byte)(1 << (pair.Value & 7));
                    keysDown++;
                }
            }

            uint buttons = 0;
            if (mouse != null)
            {
                if (mouse.leftButton.isPressed) buttons |= 1u << 1;
                if (mouse.middleButton.isPressed) buttons |= 1u << 2;
                if (mouse.rightButton.isPressed) buttons |= 1u << 3;
                if (mouse.backButton.isPressed) buttons |= 1u << 4;
                if (mouse.forwardButton.isPressed) buttons |= 1u << 5;
                if (!mcScreenOpen) buttons &= ~ownedButtons;
                // The freecam's: its right button is never Minecraft's while it's on; held, the mouse turns it, not Minecraft's look.
                if (freecamRight) buttons &= ~(1u << 3);
                Vector2 d = freecamMouse ? Vector2.zero : mouse.delta.ReadValue();
                totalDX += d.x;
                totalDY -= d.y; // Unity: up is +; Minecraft (screen): down is +
                totalScroll += mouse.scroll.ReadValue().y / 120.0; // Windows: 120 per wheel notch

                // Minecraft's screens: Windows' own pointer, unlocked and shown, so it can leave the
                // window. It starts in the middle (Mycelium had it locked there), which is where
                // Minecraft puts its cursor when a screen opens.
                bool cursorNow = active && mcScreenOpen;
                if (cursorNow != cursorShown) SetPointerFree(cursorNow);
                if (cursorNow)
                {
                    // Set every frame in case something in Mycelium locks it again.
                    UnityEngine.Cursor.lockState = CursorLockMode.None;
                    UnityEngine.Cursor.visible = true;
                    cursor = mouse.position.ReadValue();
                }
            }
            else if (cursorShown) SetPointerFree(false);

            if (active) reason = Proto.ReasonActive;
            if (active != wasActive || reason != lastReason)
            {
                wasActive = active;
                lastReason = reason;
                McLog.Info($"input bridge: {(active ? "ON, Minecraft plays from Mycelium's keyboard and mouse" : $"off ({ReasonText(reason)})")}");
            }
            link.WriteInput((active ? Proto.InActive : 0u) | (cursorShown ? Proto.InCursor : 0u), active ? Proto.ReasonActive : reason, buttons, keys,
                            totalDX, totalDY, totalScroll, textCount, text,
                            Mathf.Clamp01(cursor.x / Mathf.Max(1, Screen.width)), 1.0 - Mathf.Clamp01(cursor.y / Mathf.Max(1, Screen.height)));
        }

        /// <summary>Free Windows' mouse pointer for Minecraft's screens, or give it back to Mycelium as it was.</summary>
        void SetPointerFree(bool free)
        {
            cursorShown = free;
            if (free)
            {
                lockBefore = UnityEngine.Cursor.lockState;
                visibleBefore = UnityEngine.Cursor.visible;
                UnityEngine.Cursor.lockState = CursorLockMode.None;
                UnityEngine.Cursor.visible = true;
                McLog.Info($"input bridge: Minecraft screen open, Windows' mouse pointer freed (was {lockBefore}, {(visibleBefore ? "shown" : "hidden")})");
            }
            else
            {
                UnityEngine.Cursor.lockState = lockBefore;
                UnityEngine.Cursor.visible = visibleBefore;
                McLog.Info($"input bridge: Minecraft screen closed, mouse pointer back to {lockBefore}, {(visibleBefore ? "shown" : "hidden")}; the mouse turns the view again");
            }
        }

        void OnText(char c)
        {
            text[textCount % Proto.InTextRing] = c;
            textCount++;
        }

        public static string ReasonText(uint reason) => reason switch
        {
            Proto.ReasonActive => "active",
            Proto.ReasonNotFocused => "Mycelium window not focused",
            Proto.ReasonMyceliumControls => "Mycelium has the controls: menu, pause or cutscene",
            Proto.ReasonNotDriving => "Minecraft isn't driving the spore",
            Proto.ReasonKnockback => "a hit's knockback has the spore (mouse buttons kept)",
            Proto.ReasonLookOnly => "at a decoration (mouse look only)",
            _ => "bridge turned off (inputBridge=0)",
        };
    }
}
