using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using Mono.Cecil;

namespace MyceliumCraft
{
    /// <summary>
    /// Mycelium's newer code in the Steam release (your request, 2026-11-10/11; you're one of its
    /// developers). The release is Mycelium's GitHub state before d64bf8d9; d64bf8d9 and c5c14682
    /// (StinkCloud, EnemyHealth's invincibility, enemy attack and lunge fixes) came after.
    ///
    /// tools/transplant makes BepInEx\patchers\MyceliumCraft\newer\Assembly-CSharp.dll: the release's
    /// own Assembly-CSharp with the newer code of the classes those commits change, its saved-field
    /// layout untouched (a plain swap of the newer DLL crashed reading the game's data). As the game
    /// starts, BepInEx hands this patcher the game's Assembly-CSharp, and it's replaced by that one,
    /// in memory only: the game's file is never changed, removing BepInEx gives the original back, and
    /// Steam's file check doesn't touch it.
    ///
    /// Only the exact release it was made for is replaced (its module id, in replaces.txt): after a
    /// Steam update the game's own code is kept.
    /// </summary>
    public static class Patcher
    {
        public static IEnumerable<string> TargetDLLs { get; } = new[] { "Assembly-CSharp.dll" };

        static readonly ManualLogSource Log = Logger.CreateLogSource("MyceliumCraft.Patcher");

        public static void Patch(ref AssemblyDefinition assembly)
        {
            string dir = Path.GetDirectoryName(typeof(Patcher).Assembly.Location);
            string merged = Path.Combine(dir, "newer", "Assembly-CSharp.dll");
            string replacesFile = Path.Combine(dir, "replaces.txt");
            try
            {
                if (!File.Exists(merged) || !File.Exists(replacesFile))
                {
                    Log.LogWarning($"the merged Assembly-CSharp or replaces.txt is missing in {dir}: the game's own code is used");
                    return;
                }
                string expected = File.ReadAllText(replacesFile).Trim();
                string actual = assembly.MainModule.Mvid.ToString();
                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                {
                    Log.LogWarning($"the game's Assembly-CSharp ({actual}) isn't the release this was made for ({expected}), maybe a Steam update: the game's own code is used");
                    return;
                }
                var parameters = new ReaderParameters { AssemblyResolver = assembly.MainModule.AssemblyResolver, ReadingMode = ReadingMode.Immediate };
                assembly = AssemblyDefinition.ReadAssembly(merged, parameters);
                Log.LogInfo("Mycelium's newer code (commits d64bf8d9 and c5c14682) merged into the release's Assembly-CSharp, in memory only");
            }
            catch (Exception e)
            {
                Log.LogError($"couldn't use the merged Assembly-CSharp, the game's own code is used: {e}");
            }
        }
    }
}
