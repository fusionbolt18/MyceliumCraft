using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MyceliumCraft.Editor
{
    /// <summary>
    /// Builds MyceliumCraft.exe from the private copy of Mycelium in work\Mycelium.
    /// Called by tools\build-mycelium.ps1:  Unity.exe -batchmode -executeMethod MyceliumCraft.Editor.BuildMyceliumCraft.Build -mcOut <folder>
    /// </summary>
    public static class BuildMyceliumCraft
    {
        public static void Build()
        {
            string outDir = Arg("-mcOut") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "build"));
            // Own name, so the build keeps its own save folder (AppData\LocalLow\<company>\MyceliumCraft)
            // and never touches your normal Mycelium saves.
            PlayerSettings.productName = "MyceliumCraft";

            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            Debug.Log("[MyceliumCraft] building " + scenes.Length + " scenes: " + string.Join(", ", scenes) + " -> " + outDir);
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(outDir, "MyceliumCraft.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[MyceliumCraft] build {report.summary.result}: {report.summary.totalErrors} errors, {report.summary.totalWarnings} warnings, {report.summary.totalTime}");
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
