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

        const string Template =
            "# MyceliumCraft settings. Restart MyceliumCraft.exe after changing them.\n" +
            "\n" +
            "# Mycelium units per Minecraft block. 1.4 makes Minecraft's walking speed (4.3 blocks/s)\n" +
            "# about the spore's base speed (6 units/s). Bigger = the spore covers more ground per block.\n" +
            "scale=1.4\n" +
            "\n" +
            "# 1: make Minecraft's player as tall as the spore (Minecraft's own scale setting), so it fits\n" +
            "# under everything the spore fits under. Speed, jumping and step-up stay the same. 0: normal size.\n" +
            "matchSporeSize=1\n";

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
            McLog.Info($"config: scale={Scale.ToString(CultureInfo.InvariantCulture)} Mycelium units per block, matchSporeSize={(MatchSporeSize ? 1 : 0)}");
        }
    }
}
