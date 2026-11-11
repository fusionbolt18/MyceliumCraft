using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Our shaders (Runtime/Resources/MyceliumCraft*.shader). In our own build they're in the game's
    /// Resources. As a BepInEx plugin they can't be: Unity only compiles shaders into a build, so they
    /// come from an AssetBundle built with the same Unity (2022.3.0f1) by tools/build-bundle.ps1,
    /// "myceliumcraft.shaders" in the plugin's folder (McPaths). Loaded once, on first use.
    /// </summary>
    static class McShaders
    {
        public const string BundleName = "myceliumcraft.shaders";
        static AssetBundle bundle;
        static bool looked;

        public static Shader Load(string name)
        {
            Shader s = Resources.Load<Shader>(name);
            if (s != null) return s;
            if (!looked)
            {
                looked = true;
                string path = McPaths.File(BundleName);
                bundle = System.IO.File.Exists(path) ? AssetBundle.LoadFromFile(path) : null;
                McLog.Info(bundle != null ? $"shaders: loaded {path} ({string.Join(", ", bundle.GetAllAssetNames())})"
                                          : $"shaders: none in the game, and no bundle at {path}");
            }
            if (bundle == null) return null;
            // By file name (MyceliumCraftOverlay.shader); the shader's own name is "Hidden/MyceliumCraft/Overlay".
            Shader byFile = bundle.LoadAsset<Shader>(name);
            if (byFile != null) return byFile;
            string own = "Hidden/MyceliumCraft/" + (name.StartsWith("MyceliumCraft") ? name.Substring("MyceliumCraft".Length) : name);
            foreach (Shader candidate in bundle.LoadAllAssets<Shader>())
                if (candidate.name == own) return candidate;
            McLog.Info($"shaders: '{name}' isn't in the bundle");
            return null;
        }
    }
}
