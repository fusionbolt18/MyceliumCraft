# MyceliumCraft shared-memory protocol

This file is the single source of truth for the byte layout. Two files mirror it:

- Java: `fabric/src/main/java/dev/myceliumcraft/link/Proto.java`
- C#: `mycelium/Runtime/Link/Proto.cs`

If you change anything here, change both files and bump `VERSION`.

## Basics

- Mapping name: `Local\MyceliumCraft_v1`, size `0x100000` bytes (1 MB).
- **Mycelium creates the mapping. Minecraft opens it.** This matches SkyCraft, where the host
  game (Skyrim) owns the mapping.
- Everything is little-endian.
- Heartbeats use Windows `GetTickCount64()` (milliseconds since boot), which both processes can read.
  A side is "alive" if its heartbeat is younger than 3000 ms.

## Seqlock

Per-frame blocks use a seqlock. A seqlock is a counter that lets one writer and one reader share
a block without locks.
- **Writer:** add 1 to `seq` (it becomes odd), write the fields, then add 1 again (it becomes even).
- **Reader:** read `seq`. If it's odd, try again. Read the fields, then read `seq` again. If it
  changed, try again.

## Layout (version 4)

### Header @ 0x000

| Off | Type | Field |
|---|---|---|
| 0x00 | u32 | magic `0x4343594D` ("MYCC") |
| 0x04 | u32 | version (currently **4**) |
| 0x08 | u32 | Mycelium process id |
| 0x0C | u32 | Minecraft process id (0 until Minecraft opens the mapping) |
| 0x10 | u64 | Mycelium heartbeat (GetTickCount64 at the last Mycelium frame) |
| 0x18 | u64 | Minecraft heartbeat (GetTickCount64 at the last Minecraft frame) |

### MycState @ 0x100 (Mycelium → Minecraft, seqlock)

| Off | Type | Field |
|---|---|---|
| 0x00 | u32 | seq |
| 0x04 | u32 | flags: bit0 `IN_LEVEL` (a spore player exists), bit1 `PAUSED` |
| 0x08 | f64 | spore X (**Mycelium** coords) |
| 0x10 | f64 | spore Y |
| 0x18 | f64 | spore Z |
| 0x20 | f32 | spore yaw (Unity degrees) |
| 0x24 | u32 | teleportSeq: when this changes, Minecraft moves its player to the teleport target |
| 0x28 | u64 | Mycelium frame counter |
| 0x30 | f64 | teleport target X (**Minecraft** coords, feet) |
| 0x38 | f64 | teleport target Y |
| 0x40 | f64 | teleport target Z |
| 0x48 | f32 | teleport target yaw (Minecraft degrees) |
| 0x4C | f32 | wanted Minecraft player height in blocks, to match the spore's body (0 = leave Minecraft's size alone). Since version 4 |

### McState @ 0x200 (Minecraft → Mycelium, seqlock)

Positions are in **Minecraft coordinates**: blocks, Y up, +Z is south.

| Off | Type | Field |
|---|---|---|
| 0x00 | u32 | seq |
| 0x04 | u32 | flags: bit0 `IN_WORLD`, bit1 `SCREEN_OPEN`, bit2 `ON_GROUND`, bit3 `SNEAKING`, bit4 `SPRINTING` |
| 0x08 | f64 | feet X (interpolated to the rendered frame) |
| 0x10 | f64 | feet Y |
| 0x18 | f64 | feet Z |
| 0x20 | f32 | yaw (Minecraft degrees) |
| 0x24 | f32 | pitch (Minecraft degrees) |
| 0x28 | u64 | Minecraft frame counter |
| 0x30 | u32 | world epoch: goes up by 1 every time Minecraft enters a world |
| 0x34 | u32 | teleportAck: the last MycState teleportSeq Minecraft has applied |

### GroundGrid @ 0x1000 (Mycelium → Minecraft, seqlock)

Mycelium's solid ground around the spore, in **Minecraft coordinates**, as *layered columns*.
Cell `(i, j)` covers X from `originX + i*cell` to `originX + (i+1)*cell`, and Z the same way with `j`.
Each cell has up to 4 solid pieces, each a bottom and top height. Everything else in the column is
empty space. That's how tunnels, overhangs and canopies are represented (since version 3).

| Off | Type | Field |
|---|---|---|
| 0x00 | u32 | seq |
| 0x04 | u32 | gridVersion: goes up by 1 on every new scan (0 = no grid yet) |
| 0x08 | u32 | n: cells per side (at most 128) |
| 0x0C | u32 | pad |
| 0x10 | f64 | originX (Minecraft X of the grid's west edge) |
| 0x18 | f64 | originZ (Minecraft Z of the grid's north edge) |
| 0x20 | f64 | cell size (blocks) |
| 0x28 | 0x18 bytes | reserved |
| 0x40 | f32[n*n*8] | per cell, index `(j*n + i)*8`: 4 pieces of (bottom, top), lowest piece first. `bottom = -1e30` means "from far below". Unused pieces have `bottom = top = 1e30`. A cell with no pieces is a hole |

## Coordinate mapping (since version 2: fixed)

```
mc.x  =  myc.x / SCALE        myc.x =  mc.x * SCALE
mc.y  =  myc.y / SCALE        myc.y =  mc.y * SCALE
mc.z  = -myc.z / SCALE        myc.z = -mc.z * SCALE     (MC +Z is south; Unity +Z is "north")
mc.yaw = myc.yaw + 180        (Minecraft yaw 0 faces +Z)
```

`SCALE` is Mycelium units per Minecraft block (`scale=` in `MyceliumCraft.cfg`, default 1.4).
Only Mycelium uses it. Everything it sends Minecraft is already in Minecraft coordinates.
