using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// MyceliumCraft.cfg next to MyceliumCraft.exe: simple "key=value" lines, "#" for comments.
    /// Written with the defaults the first time. Read once at startup.
    /// </summary>
    static class McConfig
    {
        /// <summary>Mycelium units per Minecraft block.</summary>
        public static float Scale = 1.4f;

        /// <summary>Shrink Minecraft's player to the spore's height (Minecraft's scale attribute).</summary>
        public static bool MatchSporeSize = true;

        /// <summary>Point Mycelium's camera where Minecraft's player looks.</summary>
        public static bool SyncCamera = true;

        /// <summary>
        /// Degrees Mycelium's camera looks down when Minecraft looks straight ahead (16 = Mycelium's
        /// normal middle view). Minecraft's full -90..90 is stretched around it to -89..89.
        /// </summary>
        public static float CameraPitchOffset = 16f;

        /// <summary>Minecraft plays from Mycelium's keyboard and mouse while Mycelium has the focus.</summary>
        public static bool InputBridge = true;

        /// <summary>Seconds the third-person camera takes to move in front of a wall (Mycelium's own: 0, instant).</summary>
        public static float CameraCollisionSmoothing = 0.2f;

        /// <summary>Split the keys between the games while Minecraft drives (KeyOwnership).</summary>
        public static bool KeyOwnership = true;

        /// <summary>In third person, draw your Minecraft body instead of the spore (McPlayerModel).</summary>
        public static bool PlayerModel = true;

        /// <summary>Show Minecraft's hand, HUD and screens on top of Mycelium (McOverlay).</summary>
        public static bool Overlay = true;

        /// <summary>Copy Minecraft's F5 view (first person / behind / in front).</summary>
        public static bool MatchF5View = true;

        /// <summary>Seconds over which the camera eases toward Minecraft's latest pitch (0 = none).</summary>
        public static float CameraSmoothing = 0.05f;

        const string Template =
            "# MyceliumCraft settings. Restart MyceliumCraft.exe after changing them.\n" +
            "\n" +
            "# Mycelium units per Minecraft block. 1.4 makes Minecraft's walking speed (4.3 blocks/s)\n" +
            "# about the spore's base speed (6 units/s). Bigger = the spore covers more ground per block.\n" +
            "scale=1.4\n" +
            "\n" +
            "# 1: make Minecraft's player as tall as the spore (Minecraft's own scale setting), so it fits\n" +
            "# under everything the spore fits under. Speed, jumping and step-up stay the same. 0: normal size.\n" +
            "matchSporeSize=1\n" +
            "\n" +
            "# 1: Mycelium's camera turns with Minecraft's mouse look. 0: Mycelium's own camera.\n" +
            "syncCamera=1\n" +
            "\n" +
            "# Degrees Mycelium's camera looks down when Minecraft looks straight ahead. 16 is Mycelium's\n" +
            "# normal view; 0 makes the camera level with Minecraft. Straight up/down in Minecraft is\n" +
            "# always straight up/down for the camera.\n" +
            "cameraPitchOffset=16\n" +
            "\n" +
            "# Seconds the camera takes to ease toward Minecraft's up/down look. Smooths out the steps of\n" +
            "# Minecraft's frames; bigger = smoother but laggier. 0 = follow exactly.\n" +
            "cameraSmoothing=0.05\n" +
            "\n" +
            "# 1: Mycelium's camera copies Minecraft's F5 view (first person, behind, in front). 0: always behind.\n" +
            "matchF5View=1\n" +
            "\n" +
            "# 1: play from Mycelium's window: its keyboard and mouse are passed on to Minecraft (except\n" +
            "# Escape and P, Mycelium's pause keys), and not while a Mycelium menu has the controls.\n" +
            "# 0: play from Minecraft's window as before.\n" +
            "inputBridge=1\n" +
            "\n" +
            "# 1: show Minecraft's hand, hotbar, hearts and screens (inventory, chat) on top of Mycelium.\n" +
            "# Minecraft then stops drawing its own world. 0: Minecraft draws its world in its own window.\n" +
            "overlay=1\n" +
            "\n" +
            "# 1: in third person, show your Minecraft body (skin, armour) instead of the spore. Needs overlay=1.\n" +
            "playerModel=1\n" +
            "\n" +
            "# Seconds the third-person camera takes to move in front of a wall that gets in the way.\n" +
            "# Mycelium's own is 0 (an instant jump); bigger is smoother but may show a wall's edge briefly.\n" +
            "cameraCollisionSmoothing=0.2\n" +
            "\n" +
            "# 1: while Minecraft drives, clicks, Shift, Space, E, Q and Ctrl are Minecraft's; R (interact) and Tab\n" +
            "# (show stats) are Mycelium's; dodge, skills and camera keys are unbound. 0: both games react to every key.\n" +
            "keyOwnership=1\n";

        public static void Load()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "MyceliumCraft.cfg"));
            try
            {
                if (!File.Exists(path))
                {
                    File.WriteAllText(path, Template);
                    McLog.Info($"config: wrote defaults to {path}");
                }
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string value = line.Substring(eq + 1).Trim();
                    if (key == "scale" && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float s) && s > 0.01f)
                    {
                        Scale = s;
                    }
                    else if (key == "matchsporesize" && (value == "0" || value == "1"))
                    {
                        MatchSporeSize = value == "1";
                    }
                    else if (key == "synccamera" && (value == "0" || value == "1"))
                    {
                        SyncCamera = value == "1";
                    }
                    else if (key == "camerapitchoffset" && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float off))
                    {
                        CameraPitchOffset = off;
                    }
                    else if (key == "cameracollisionsmoothing" && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float cs) && cs >= 0f)
                    {
                        CameraCollisionSmoothing = cs;
                    }
                    else if (key == "keyownership" && (value == "0" || value == "1"))
                    {
                        KeyOwnership = value == "1";
                    }
                    else if (key == "inputbridge" && (value == "0" || value == "1"))
                    {
                        InputBridge = value == "1";
                    }
                    else if (key == "playermodel" && (value == "0" || value == "1"))
                    {
                        PlayerModel = value == "1";
                    }
                    else if (key == "overlay" && (value == "0" || value == "1"))
                    {
                        Overlay = value == "1";
                    }
                    else if (key == "matchf5view" && (value == "0" || value == "1"))
                    {
                        MatchF5View = value == "1";
                    }
                    else if (key == "camerasmoothing" && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float sm) && sm >= 0f)
                    {
                        CameraSmoothing = sm;
                    }
                    else
                    {
                        McLog.Info($"config: ignored line '{line}'");
                    }
                }
            }
            catch (Exception e)
            {
                McLog.Info($"config: couldn't read {path} ({e.Message}); using defaults");
            }
            McLog.Info($"config: scale={Scale.ToString(CultureInfo.InvariantCulture)} Mycelium units per block, matchSporeSize={(MatchSporeSize ? 1 : 0)}, syncCamera={(SyncCamera ? 1 : 0)}, cameraPitchOffset={CameraPitchOffset.ToString(CultureInfo.InvariantCulture)}, cameraSmoothing={CameraSmoothing.ToString(CultureInfo.InvariantCulture)}, matchF5View={(MatchF5View ? 1 : 0)}, inputBridge={(InputBridge ? 1 : 0)}, cameraCollisionSmoothing={CameraCollisionSmoothing.ToString(CultureInfo.InvariantCulture)}, keyOwnership={(KeyOwnership ? 1 : 0)}, overlay={(Overlay ? 1 : 0)}, playerModel={(PlayerModel ? 1 : 0)}");
        }
    }
}
