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
