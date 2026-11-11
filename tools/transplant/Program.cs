using Mono.Cecil;

namespace MyceliumCraftTools
{
    static class Program
    {
        // transplant diff <release.dll> <newer.dll>                 lists what differs
        // transplant merge <release.dll> <newer.dll> <out.dll>      writes the merged assembly
        static int Main(string[] args)
        {
            if (args.Length < 3) { Console.WriteLine("usage: diff <release> <newer> | merge <release> <newer> <out>"); return 2; }
            var rp = new ReaderParameters { ReadingMode = ReadingMode.Immediate, AssemblyResolver = Resolver(args[1]) };
            var release = AssemblyDefinition.ReadAssembly(args[1], rp);
            var newer = AssemblyDefinition.ReadAssembly(args[2], new ReaderParameters { ReadingMode = ReadingMode.Immediate, AssemblyResolver = Resolver(args[1]) });
            if (args[0] == "diff") { Report(release.MainModule, newer.MainModule); return 0; }
            if (args[0] == "merge" && args.Length >= 4) return Merge.Run(release, newer, args[3]);
            Console.WriteLine("unknown command"); return 2;
        }

        static IAssemblyResolver Resolver(string near)
        {
            var r = new DefaultAssemblyResolver();
            r.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(near)));
            return r;
        }

        static void Report(ModuleDefinition release, ModuleDefinition newer)
        {
            var rTypes = Diff.AllTypes(release).ToDictionary(t => t.FullName);
            foreach (var n in Diff.AllTypes(newer))
            {
                if (!rTypes.TryGetValue(n.FullName, out var r)) { Console.WriteLine($"new type {n.FullName}"); continue; }
                var rf = r.Fields.ToDictionary(Diff.Key);
                foreach (var f in n.Fields) if (!rf.ContainsKey(Diff.Key(f))) Console.WriteLine($"  new field {n.FullName}: {Diff.Key(f)} {f.Attributes}");
                var nf = n.Fields.Select(Diff.Key).ToHashSet();
                foreach (var f in r.Fields) if (!nf.Contains(Diff.Key(f))) Console.WriteLine($"  release-only field {r.FullName}: {Diff.Key(f)} {f.Attributes}");
                var rm = r.Methods.ToDictionary(Diff.Key);
                foreach (var m in n.Methods)
                {
                    if (!rm.TryGetValue(Diff.Key(m), out var old)) Console.WriteLine($"  new method {m.FullName}");
                    else if (Diff.Body(old) != Diff.Body(m)) Console.WriteLine($"  changed {m.FullName}");
                }
            }
            var nTypes = Diff.AllTypes(newer).Select(t => t.FullName).ToHashSet();
            foreach (var r in Diff.AllTypes(release)) if (!nTypes.Contains(r.FullName)) Console.WriteLine($"release-only type {r.FullName}");
        }
    }
}
