using System;
using System.IO;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Writes MyceliumCraft.log next to MyceliumCraft.exe (overwritten each launch), and copies each
    /// line to Unity's Player.log.
    /// </summary>
    static class McLog
    {
        static StreamWriter writer;
        public static string FilePath { get; private set; }

        public static void Open()
        {
            try
            {
                FilePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "MyceliumCraft.log"));
                writer = new StreamWriter(FilePath, false) { AutoFlush = true };
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MyceliumCraft] can't open log file: " + e.Message);
            }
        }

        public static void Info(string message)
        {
            string line = string.Format("[{0:HH:mm:ss.fff} +{1,8:F2}s] {2}", DateTime.Now, Time.realtimeSinceStartup, message);
            writer?.WriteLine(line);
            Debug.Log("[MyceliumCraft] " + message);
        }
    }
}
