using System.IO;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Where our own files go: the log, MyceliumCraft.cfg, the shader bundle and the test aids' .csv
    /// files. In our own build that's next to MyceliumCraft.exe, as always. As a BepInEx plugin in
    /// someone's own Mycelium install (bepinex/Plugin.cs, since 2026-11-10) it's the plugin's own
    /// folder, BepInEx\plugins\MyceliumCraft, so nothing is written beside the game's files.
    /// </summary>
    static class McPaths
    {
        /// <summary>Set by the BepInEx plugin before anything starts; null in our own build.</summary>
        public static string PluginDir;

        /// <summary>True when running as a BepInEx plugin inside Mycelium's own install.</summary>
        public static bool AsPlugin => PluginDir != null;

        public static string Dir => PluginDir ?? Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        public static string File(string name) => Path.Combine(Dir, name);
    }
}
