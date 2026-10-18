// Pretends to be Mycelium. Run this first, then `fabric\gradlew linkTest`.
//  - Publishes a known ground grid (Minecraft y 10 west of x = 0, y 10.5 east of it, one hole,
//    and a tunnel: roof from y 13 to 14 over x -22..-17).
//  - Asks Minecraft to teleport onto a fake spore at Mycelium (0.35, 14.7, -0.35) = Minecraft (0.25, 10.5, 0.25).
//  - Waits for the teleport to be confirmed, then maps Minecraft's walk back to Mycelium with the
//    same WorldMapping the game uses, and prints PASS/FAIL at the end.
using System;
using System.Threading;
using MyceliumCraft.Link;

const double Scale = 1.4;
const int N = 128;
var map = new WorldMapping(Scale);
var link = MyceliumLink.Create();
Console.WriteLine($"created {Proto.MappingName} v{Proto.Version} (already existed={link.AlreadyExisted}); waiting up to 60 s for Minecraft");

const int F = Proto.GFloatsPerCell;
var pieces = new float[N * N * F];
for (int j = 0; j < N; j++)
    for (int i = 0; i < N; i++)
    {
        int c = (j * N + i) * F;
        for (int k = 0; k < F; k++) pieces[c + k] = Proto.Unused;
        if (i == 10 && j == 10) continue; // a hole
        pieces[c] = Proto.FarBelow;
        pieces[c + 1] = i >= 64 ? 10.5f : 10f; // origin -32, cell 0.5: cell 64 starts at x = 0
        if (i >= 20 && i < 30)
        {
            pieces[c + 2] = 13f; // a tunnel: roof from 13 to 14 over x -22..-17, open from 10 to 13
            pieces[c + 3] = 14f;
        }
    }
link.WriteGround(1, N, -32, -32, 0.5, pieces);

// Input bridge: W held (SDL scancode 26), left mouse button, mouse moved 123.5 / -40 px, scrolled 2,
// typed "hi", cursor a quarter across and three quarters down.
var keys = new byte[Proto.InKeyBytes];
keys[26 >> 3] |= (byte)(1 << (26 & 7));
var textRing = new uint[Proto.InTextRing];
textRing[0] = 'h';
textRing[1] = 'i';
link.WriteInput(Proto.InActive | Proto.InCursor, Proto.ReasonActive, 1u << 1, keys, 123.5, -40, 2, 2, textRing, 0.25, 0.75);

map.ToMc(0.35, 14.7, -0.35, out double tx0, out double ty0, out double tz0);
var tp = new Teleport { Seq = 1000, X = tx0, Y = ty0, Z = tz0, Yaw = 90 };
Console.WriteLine($"teleport {tp.Seq} -> mc ({tp.X:F2}, {tp.Y:F2}, {tp.Z:F2})");

double sx = 0.35, sy = 14.7, sz = -0.35;
ulong frame = 0;
bool linked = false, everLinked = false, acked = false;
var deadline = DateTime.Now.AddSeconds(60);
var lastPrint = DateTime.MinValue;
while (DateTime.Now < deadline)
{
    link.Beat();
    link.WriteMycState(Proto.MycInLevel, sx, sy, sz, 0, frame++, tp, 0.964f); // a spore 1.35 units tall at scale 1.4
    bool alive = link.MinecraftAlive;
    if (alive != linked)
    {
        linked = alive;
        Console.WriteLine(alive ? $"Minecraft linked (pid {link.MinecraftPid})" : "Minecraft gone");
        if (alive) everLinked = true; else if (everLinked) break;
    }
    if (alive && link.TryReadMcState(out var mc) && mc.InWorld)
    {
        if (!acked && mc.TeleportAck == tp.Seq)
        {
            acked = true;
            Console.WriteLine($"teleport {tp.Seq} confirmed; mc=({mc.X:F2}, {mc.Y:F2}, {mc.Z:F2})");
        }
        if (acked)
        {
            map.ToMyc(mc.X, mc.Y, mc.Z, out sx, out sy, out sz);
            if ((DateTime.Now - lastPrint).TotalSeconds >= 1)
            {
                lastPrint = DateTime.Now;
                Console.WriteLine($"mc=({mc.X:F2}, {mc.Y:F2}, {mc.Z:F2}) -> spore ({sx:F2}, {sy:F2}, {sz:F2}) yaw {WorldMapping.MycYaw(mc.Yaw):F0}");
            }
        }
    }
    Thread.Sleep(16);
}
// Minecraft walked from (0.25, 10.5, 0.25) to (10.25, 10.5, -9.75): Mycelium (14.35, 14.7, 13.65).
bool pass = acked && Math.Abs(sx - 14.35) < 0.3 && Math.Abs(sy - 14.7) < 0.01 && Math.Abs(sz - 13.65) < 0.3;
Console.WriteLine($"teleport confirmed={acked}; final spore ({sx:F2}, {sy:F2}, {sz:F2}), expected (14.35, 14.70, 13.65) -> {(pass ? "PASS" : "FAIL")}");
link.Dispose();
return pass ? 0 : 1;
