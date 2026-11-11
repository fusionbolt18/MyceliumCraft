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
            // A window you can resize (your request, 2026-10-24); Mycelium's own project has it off.
            // Only this private copy's settings change.
            PlayerSettings.resizableWindow = true;
            Debug.Log("[MyceliumCraft] resizable window: " + PlayerSettings.resizableWindow + ", fullscreen mode " + PlayerSettings.fullScreenMode);

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

        /// <summary>
        /// Our shaders as an AssetBundle, for the BepInEx plugin (McShaders): a plugin can't add shaders
        /// to someone's Mycelium install, so they come from this bundle, built by the same Unity.
        /// Called by toolsuild-mycelium.ps1 -BundleOnly:  -executeMethod MyceliumCraft.Editor.BuildMyceliumCraft.BuildShaderBundle -mcOut <folder>
        /// </summary>
        public static void BuildShaderBundle()
        {
            string outDir = Arg("-mcOut") ?? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "bundle"));
            Directory.CreateDirectory(outDir);
            string[] shaders = AssetDatabase.FindAssets("t:Shader", new[] { "Packages/dev.myceliumcraft/Runtime/Resources" })
                .Select(AssetDatabase.GUIDToAssetPath).ToArray();
            Debug.Log("[MyceliumCraft] shader bundle: " + shaders.Length + " shaders: " + string.Join(", ", shaders));
            var build = new AssetBundleBuild { assetBundleName = "myceliumcraft.shaders", assetNames = shaders };
            var manifest = BuildPipeline.BuildAssetBundles(outDir, new[] { build }, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
            bool ok = manifest != null && File.Exists(Path.Combine(outDir, "myceliumcraft.shaders"));
            Debug.Log("[MyceliumCraft] shader bundle " + (ok ? "built" : "FAILED") + " -> " + outDir);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
