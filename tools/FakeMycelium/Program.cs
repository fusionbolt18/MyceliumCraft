// Pretends to be Mycelium: creates the shared memory, keeps a fake spore at (5, 0, 5), and applies
// the same AnchorMapping the real game uses to whatever Minecraft (or `gradlew linkTest`) sends.
// At the end it prints PASS/FAIL for the scripted walk in LinkTestMain (10 east, then 10 north).
using System;
using System.Threading;
using MyceliumCraft.Link;

var link = MyceliumLink.Create();
Console.WriteLine($"created {Proto.MappingName} (already existed={link.AlreadyExisted}); waiting up to 60 s for Minecraft");
var mapping = new AnchorMapping();
double sporeX = 5, sporeY = 0, sporeZ = 5;
double tx = sporeX, tz = sporeZ;
ulong frame = 0;
bool linked = false;
bool everLinked = false;
var deadline = DateTime.Now.AddSeconds(60);
var lastPrint = DateTime.MinValue;
while (DateTime.Now < deadline)
{
    link.Beat();
    link.WriteMycState(Proto.MycInLevel, tx, sporeY, tz, 0, frame++);
    bool alive = link.MinecraftAlive;
    if (alive != linked)
    {
        linked = alive;
        Console.WriteLine(alive ? $"Minecraft linked (pid {link.MinecraftPid})" : "Minecraft gone");
        if (alive) everLinked = true; else if (everLinked) break;
    }
    if (alive && link.TryReadMcState(out var mc) && mc.InWorld)
    {
        if (!mapping.HasAnchor || mapping.Epoch != mc.Epoch)
        {
            mapping.SetAnchor(mc.X, mc.Z, sporeX, sporeY, sporeZ, mc.Epoch);
            Console.WriteLine($"anchor mc=({mc.X:F2}, {mc.Z:F2}) <-> spore=({sporeX}, {sporeZ})");
        }
        mapping.Target(mc.X, mc.Z, out tx, out tz);
        if ((DateTime.Now - lastPrint).TotalSeconds >= 1)
        {
            lastPrint = DateTime.Now;
            Console.WriteLine($"mc=({mc.X:F2}, {mc.Y:F2}, {mc.Z:F2}) yaw={mc.Yaw:F0} -> spore target=({tx:F2}, {tz:F2}) yaw={AnchorMapping.Yaw(mc.Yaw):F0} frame={mc.Frame}");
        }
    }
    Thread.Sleep(16);
}
// LinkTestMain walks +10 X then -10 Z in Minecraft, which should be +10 X then +10 Z for the spore.
bool pass = everLinked && Math.Abs(tx - 15) < 0.3 && Math.Abs(tz - 15) < 0.3;
Console.WriteLine($"final spore target=({tx:F2}, {tz:F2}); expected (15, 15) after linkTest -> {(pass ? "PASS" : "FAIL")}");
link.Dispose();
return pass ? 0 : 1;
