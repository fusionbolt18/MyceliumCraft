using System.IO;
using BepInEx;
using UnityEngine.SceneManagement;

namespace MyceliumCraft
{
    /// <summary>
    /// MyceliumCraft inside someone's own Mycelium install, through BepInEx 5 (your request,
    /// 2026-11-10: friends add it to their own copy). BepInEx loads this DLL from
    /// BepInEx\plugins\MyceliumCraft; it starts the same MinecraftDriver our own build starts (Unity
    /// calls MinecraftDriver.Boot there; here nothing does, so this does). Our files (log, config, the
    /// shader bundle) live in the plugin's own folder (McPaths).
    ///
    /// Started on the first scene load, not at once: BepInEx runs this before Unity has loaded any
    /// scene, and a driver made then was destroyed straight away (first test, 2026-11-10).
    /// </summary>
    [BepInPlugin("dev.myceliumcraft.mycelium", "MyceliumCraft", "0.1.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        void Awake()
        {
            McPaths.PluginDir = Path.GetDirectoryName(Info.Location);
            Logger.LogInfo($"MyceliumCraft: loaded (files in {McPaths.PluginDir}); starts with the first scene");
            SceneManager.sceneLoaded += Start;
        }

        static void Start(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= Start;
            var log = BepInEx.Logging.Logger.CreateLogSource("MyceliumCraft");
            log.LogInfo($"MyceliumCraft: starting with scene '{scene.name}'");
            // Which game code runs: the release's own, or merged with the newer commits (the patcher).
            bool newer = System.Type.GetType("EnemyHealth, Assembly-CSharp")?.GetField("isInvincible") != null;
            log.LogInfo(newer ? "MyceliumCraft: Mycelium's code: the release with the newer commits (d64bf8d9, c5c14682)" : "MyceliumCraft: Mycelium's code: the release's own");
            MinecraftDriver.Boot();
        }
    }
}
