# MyceliumCraft shared-memory protocol

This file is the single source of truth for the byte layout. Two files mirror it:

- Java: `fabric/src/main/java/dev/myceliumcraft/link/Proto.java`
- C#: `mycelium/Runtime/Link/Proto.cs`

If you change anything here, change both files and bump `VERSION`.

## Basics

- Mapping name: `Local\MyceliumCraft_v1`, size `0x1000` bytes.
- **Mycelium creates the mapping. Minecraft opens it.** This matches SkyCraft, where the host
  game (Skyrim) owns the mapping.
- Everything is little-endian.
- Heartbeats use Windows `GetTickCount64()` (milliseconds since boot), which both processes can read.
  A side is "alive" if its heartbeat is younger than 3000 ms.

## Seqlock

Per-frame state blocks use a seqlock. A seqlock is a counter that lets one writer and one reader
share a block without locks.
- **Writer:** add 1 to `seq` (it becomes odd), write the fields, then add 1 again (it becomes even).
- **Reader:** read `seq`. If it's odd, try again. Read the fields, then read `seq` again. If it
  changed, try again.

## Layout

### Header @ 0x000

| Off | Type | Field |
|---|---|---|
| 0x00 | u32 | magic `0x4343594D` ("MYCC") |
| 0x04 | u32 | version (currently **1**) |
| 0x08 | u32 | Mycelium process id |
| 0x0C | u32 | Minecraft process id (0 until Minecraft opens the mapping) |
| 0x10 | u64 | Mycelium heartbeat (GetTickCount64 at the last Mycelium frame) |
| 0x18 | u64 | Minecraft heartbeat (GetTickCount64 at the last Minecraft frame) |

### MycState @ 0x100 (Mycelium → Minecraft, seqlock)

Positions are in **Mycelium (Unity) coordinates**: Y up, Z forward, left-handed.

| Off | Type | Field |
|---|---|---|
| 0x00 | u32 | seq |
| 0x04 | u32 | flags: bit0 `IN_LEVEL` (a spore player exists), bit1 `PAUSED` |
| 0x08 | f64 | spore X |
| 0x10 | f64 | spore Y |
| 0x18 | f64 | spore Z |
| 0x20 | f32 | spore yaw (Unity degrees) |
| 0x24 | u32 | pad |
| 0x28 | u64 | Mycelium frame counter |

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
| 0x30 | u32 | world epoch: goes up by 1 every time Minecraft enters a world, so Mycelium re-anchors |
| 0x34 | u32 | pad |

## Coordinate mapping (Phase 0)

Minecraft movement is applied to Mycelium as a **difference from an anchor**. When the link comes
up, or the world epoch changes, Mycelium records both positions as the anchor:

```
myc.x = anchorMyc.x + SCALE * (mc.x - anchorMc.x)
myc.z = anchorMyc.z - SCALE * (mc.z - anchorMc.z)   (MC +Z is south; Unity +Z is "north")
myc.y = Mycelium's own physics (Phase 0 does not drive height)
myc.yaw = mc.yaw + 180
```

`SCALE` is 1 Mycelium unit per Minecraft block for now.
