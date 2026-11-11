using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Test aid ("-mcBackground"): keep test runs of Mycelium out of the way: minimized (so behind
    /// every other window, and Windows hands the focus back to the window you were using), and
    /// never using your keyboard or mouse (the input bridge stays off in this mode, see
    /// MinecraftDriver).
    ///
    /// Unity creates and shows its window before any script runs, and Windows may give it the
    /// focus at launch (it did once: 13 s with the bridge on). Pushing it to the back of the window
    /// stack did not take the focus away; minimizing does. Repeated for the first few seconds,
    /// in case Unity shows the window again while loading.
    /// </summary>
    static class BackgroundWindow
    {
        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        const uint SWP_NOMOVE = 0x2, SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_FRAMECHANGED = 0x20, SWP_NOACTIVATE = 0x10;
        static float nextResizableCheck;
        static bool resizableLogged;

        const int GWL_STYLE = -16;
        const int WS_THICKFRAME = 0x00040000, WS_MAXIMIZEBOX = 0x00010000; // a border you can drag; the maximize button
        static bool windowLogged;

        const int SW_MINIMIZE = 6; // minimize and activate the next window (gives the focus back)

        public static bool Enabled { get; private set; }
        static float until;
        static float next;
        static int minimized;
        static bool everFocused;

        public static void Init()
        {
            Enabled = Array.IndexOf(Environment.GetCommandLineArgs(), "-mcBackground") >= 0;
            if (!Enabled) return;
            until = Time.realtimeSinceStartup + 10f;
            Minimize();
        }

        /// <summary>Call every frame: keeps the window minimized for the first 10 s; logs any time it had the focus.</summary>
        public static void Update()
        {
            if (!windowLogged && Time.realtimeSinceStartup > 1f) LogWindow();
            if (McPaths.AsPlugin && Time.realtimeSinceStartup >= nextResizableCheck)
            {
                nextResizableCheck = Time.realtimeSinceStartup + 1f;
                MakeResizable();
            }
            if (!Enabled) return;
            if (Application.isFocused && !everFocused)
            {
                everFocused = true;
                McLog.Info("-mcBackground: Mycelium got the focus; minimizing to hand it back (input bridge stays off in this mode)");
                Minimize();
            }
            if (Time.realtimeSinceStartup > until || Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + 0.25f;
            Minimize();
            if (Time.realtimeSinceStartup + 0.25f > until)
                McLog.Info($"-mcBackground: minimized {minimized} times in the first 10 s; Mycelium focused now: {Application.isFocused}");
        }

        /// <summary>Once, 1 s in: can the window be resized (Windows' own style bits), its size and Unity's mode.</summary>
        static void LogWindow()
        {
            windowLogged = true;
            uint me = (uint)Process.GetCurrentProcess().Id;
            string found = "no window found";
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid != me || !IsWindowVisible(hwnd)) return true;
                int style = GetWindowLong(hwnd, GWL_STYLE);
                found = $"resizable border {((style & WS_THICKFRAME) != 0 ? "yes" : "no")}, maximize button {((style & WS_MAXIMIZEBOX) != 0 ? "yes" : "no")}";
                return false;
            }, IntPtr.Zero);
            McLog.Info($"window: {found}; {Screen.width}x{Screen.height}, {Screen.fullScreenMode} (Alt+Enter switches between full screen and a window)");
        }

        /// <summary>
        /// As a BepInEx plugin: a window you can resize, as our own build has (your request, 2026-10-24;
        /// there it's a build setting, PlayerSettings.resizableWindow, which a plugin can't change). Once
        /// a second, while Mycelium is in a window, the border and maximize button are added if missing
        /// (Unity sets the style again when switching with Alt+Enter).
        /// </summary>
        static void MakeResizable()
        {
            if (Screen.fullScreenMode != FullScreenMode.Windowed) return;
            uint me = (uint)Process.GetCurrentProcess().Id;
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid != me || !IsWindowVisible(hwnd)) return true;
                int style = GetWindowLong(hwnd, GWL_STYLE);
                if ((style & (WS_THICKFRAME | WS_MAXIMIZEBOX)) != (WS_THICKFRAME | WS_MAXIMIZEBOX))
                {
                    SetWindowLong(hwnd, GWL_STYLE, style | WS_THICKFRAME | WS_MAXIMIZEBOX);
                    SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED | SWP_NOACTIVATE);
                    if (!resizableLogged)
                    {
                        resizableLogged = true;
                        McLog.Info("window: plugin: Mycelium's window can be resized now (border and maximize button added)");
                    }
                }
                return false;
            }, IntPtr.Zero);
        }

        static void Minimize()
        {
            uint me = (uint)Process.GetCurrentProcess().Id;
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == me && IsWindowVisible(hwnd) && !IsIconic(hwnd))
                {
                    ShowWindow(hwnd, SW_MINIMIZE);
                    minimized++;
                }
                return true;
            }, IntPtr.Zero);
        }
    }
}
