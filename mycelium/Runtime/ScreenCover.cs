using System;
using System.Reflection;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Is one of Mycelium's full-screen covers showing (scene change fade, loading screen, startup
    /// screen, death screen)? The Minecraft overlay hides then: it's drawn above all of Mycelium's UI,
    /// so it showed on top of the loading screen when starting a run (your report, 2026-10-19).
    ///
    /// Read by reflection from Mycelium's SceneLoader (its canvas groups' alpha, isLoading) and
    /// SceneLoadingManager (isLoading). The level-name title card isn't counted: it shows during play.
    /// </summary>
    static class ScreenCover
    {
        static readonly string[] Covers = { "blackCanvasGroup", "loadingCanvasGroup", "startupCanvasGroup", "deathCanvasGroup" };
        static Type loaderType, managerType;
        static FieldInfo loaderInstance, loaderLoading, managerInstance, managerLoading;
        static FieldInfo[] coverFields;
        static bool looked;

        /// <summary>True while a cover is up; why says which.</summary>
        public static bool Showing(out string why)
        {
            why = "";
            Find();
            try
            {
                object loader = loaderInstance?.GetValue(null);
                if (loader as UnityEngine.Object != null)
                {
                    if (loaderLoading != null && (bool)loaderLoading.GetValue(loader)) { why = "Mycelium is loading a scene"; return true; }
                    for (int i = 0; i < Covers.Length; i++)
                    {
                        var group = coverFields[i]?.GetValue(loader) as CanvasGroup;
                        if (group != null && group.isActiveAndEnabled && group.alpha > 0.01f) { why = $"Mycelium's {Covers[i].Replace("CanvasGroup", "")} screen is showing"; return true; }
                    }
                }
                object manager = managerInstance?.GetValue(null);
                if (manager as UnityEngine.Object != null && managerLoading != null && (bool)managerLoading.GetValue(manager)) { why = "Mycelium is loading a scene"; return true; }
            }
            catch (Exception) { }
            return false;
        }

        static void Find()
        {
            if (looked) return;
            looked = true;
            const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            loaderType = Type.GetType("SceneLoader, Assembly-CSharp");
            managerType = Type.GetType("SceneLoadingManager, Assembly-CSharp");
            loaderInstance = loaderType?.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
            loaderLoading = loaderType?.GetField("isLoading", any);
            coverFields = new FieldInfo[Covers.Length];
            for (int i = 0; i < Covers.Length; i++) coverFields[i] = loaderType?.GetField(Covers[i], any);
            managerInstance = managerType?.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
            managerLoading = managerType?.GetField("isLoading", any);
            int found = 0;
            foreach (FieldInfo f in coverFields) if (f != null) found++;
            McLog.Info($"screen cover: SceneLoader {(loaderType != null ? "found" : "MISSING")} ({found}/{Covers.Length} cover screens, isLoading {(loaderLoading != null ? "found" : "missing")}), SceneLoadingManager {(managerType != null ? "found" : "missing")}");
        }
    }
}
