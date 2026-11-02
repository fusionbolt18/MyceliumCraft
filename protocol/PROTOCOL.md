# MyceliumCraft shared-memory protocol

This file is the single source of truth for the byte layout. Two files mirror it:

- Java: `fabric/src/main/java/dev/myceliumcraft/link/Proto.java`
- C#: `mycelium/Runtime/Link/Proto.cs`

If you change anything here, change both files and bump `VERSION`.

## Basics

- Mapping name: `Local\MyceliumCraft_v1`, size `0x68EC000` bytes (about 105 MB):
  - the small blocks in the first 1 MB;
  - 3 overlay picture slots of 3840 × 2160 × 4 bytes each (since version 10);
  - the player model (1 MB) and its textures (8 MB), since version 11.

  Windows only uses memory for the parts that are written.
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

## Layout (version 27)

### Header @ 0x000

| Off | Type | Field |
|---|---|---|
| 0x00 | u32 | magic `0x4343594D` ("MYCC") |
| 0x04 | u32 | version (currently **8**) |
| 0x08 | u32 | Mycelium process id |
| 0x0C | u32 | Minecraft process id (0 until Minecraft opens the mapping) |
| 0x10 | u64 | Mycelium heartbeat (GetTickCount64 at the last Mycelium frame) |
| 0x18 | u64 | Minecraft heartbeat (GetTickCount64 at the last Minecraft frame) |

### MycState @ 0x100 (Mycelium → Minecraft, seqlock)

| Off | Type | Field |
|---|---|---|
| 0x00 | u32 | seq |
| 0x04 | u32 | flags: bit0 `IN_LEVEL` (a spore player exists), bit1 `PAUSED`, bit2 `OVERLAY` (Mycelium shows Minecraft's overlay, so Minecraft skips drawing its world; since version 10), bit3 `VANISHED` (the spore has vanished in Trophic Cascade: your Minecraft body isn't drawn, and Minecraft goes from first person to third person behind until it's back; since version 33), bit4 `CYCLONE` (Living Cyclone is spinning: Minecraft goes from first person to third person behind until it's over; since version 34), bit5 `ROLL` (the dodge roll is on, the spore's Animator "Roll": Minecraft plays its combat roll animation and turns the body to the roll's dash; since version 37) |
| 0x08 | f64 | spore X (**Mycelium** coords) |
| 0x10 | f64 | spore Y |
| 0x18 | f64 | spore Z |
| 0x20 | f32 | spore yaw (Unity degrees) |
| 0x24 | u32 | teleportSeq: when this changes, Minecraft moves its player to the teleport target |
| 0x28 | u64 | Mycelium frame counter |
| 0x30 | f64 | teleport target X (**Minecraft** coords, feet) |
| 0x38 | f64 | teleport target Y |
| 0x40 | f64 | teleport target Z |
| 0x48 | f32 | teleport target yaw (Minecraft degrees). NaN = keep Minecraft's own facing (re-syncs; since version 21) |
| 0x4C | f32 | wanted Minecraft player height in blocks, to match the spore's body (0 = leave Minecraft's size alone). Since version 4 |
| 0x50 | f32 | Mycelium's current health (its own units); since version 16 |
| 0x54 | f32 | Mycelium's max health; 0 = no spore health known (Minecraft's health isn't synced then) |
| 0x58 | i32 | Mycelium's nutrients; −1 = not known (Minecraft's experience level follows it); since version 19 |
| 0x5C | f32 | how far through Mycelium's sprout in The Carcass: 0 while waiting for it, then the Sprout animation's progress up to 1 (Minecraft plays the Warden's emerge over it); −1 = no sprout; since version 22 |
| 0x60 | f32 | the spore's walking speed ÷ its base speed (`PlayerController.moveSpeed` ÷ `SporeAttributeRanges.moveSpeedBase`): the Speed stat and speed effects. Minecraft's movement speed is multiplied by it. 0 = not known. Since version 26 |
| 0x64 | i32 | Mycelium's window width in pixels (`Screen.width`); Minecraft resizes its window to match. 0 = not known. Since version 31 |
| 0x68 | i32 | Mycelium's window height in pixels (`Screen.height`) |
| 0x6C | f32 | Mycelium's dash (Blitz, the dodge roll) along Minecraft's x, blocks per second; Minecraft's player gets this horizontal speed while it's set. 0 and 0 = no dash. Since version 32 |
| 0x70 | f32 | the dash along Minecraft's z |

### McState @ 0x200 (Minecraft → Mycelium, seqlock)

Positions are in **Minecraft coordinates**: blocks, Y up, +Z is south.

| Off | Type | Field |
|---|---|---|
| 0x00 | u32 | seq |
| 0x04 | u32 | flags: bit0 `IN_WORLD`, bit1 `SCREEN_OPEN`, bit2 `ON_GROUND`, bit3 `SNEAKING`, bit4 `SPRINTING`, bit5 `PAUSED` (Minecraft's pause menu has paused the game; Mycelium freezes too; since version 13) |
| 0x08 | f64 | feet X (interpolated to the rendered frame) |
| 0x10 | f64 | feet Y |
| 0x18 | f64 | feet Z |
| 0x20 | f32 | yaw (Minecraft degrees) |
| 0x24 | f32 | pitch (Minecraft degrees) |
| 0x28 | u64 | Minecraft frame counter |
| 0x30 | u32 | world epoch: goes up by 1 every time Minecraft enters a world |
| 0x34 | u32 | teleportAck: the last MycState teleportSeq Minecraft has applied |
| 0x38 | u32 | cameraMode: Minecraft's F5 view, 0 first person, 1 third person behind, 2 third person in front. Since version 5 |
| 0x3C | f32 | body yaw (Minecraft degrees, interpolated to the rendered frame): where the player's body faces, which turns toward the walking direction when strafing. Since version 7 |
| 0x40 | i64 | tickQpc: Windows QueryPerformanceCounter at the moment of Minecraft's latest physics tick. Since version 8 |
| 0x48 | f64 ×3 | feet at the previous tick (x, y, z) |
| 0x60 | f64 ×3 | feet at the latest tick (x, y, z) |
| 0x78 | f32 | milliseconds per tick (50 unless /tick rate changed); 0 = no tick data |
| 0x7C | f32 | body yaw at the previous tick |
| 0x80 | f32 | body yaw at the latest tick |
| 0x84 | f32 | FOV multiplier now: Minecraft's own (sprinting, speed, bow, spyglass; scaled by its "FOV Effects" setting). 0 = not sent (since version 14) |
| 0x88 | f32 | eye height now ÷ standing eye height: Minecraft's smoothed camera eye height (crouching about 0.78). 0 = not sent |
| 0x8C | f32 | Minecraft's "Damage Tilt" accessibility setting, 0 (off) to 1 (full): how far Mycelium's camera tilts toward a hit. Since version 20 |
| 0x90 | u32 | the weapon (Weapons table id) whose carrot is in the main hand; 0 = none. The spore holds that weapon. Since version 24 |
| 0x94 | u32 | melee attacks Minecraft has started (counts up): Mycelium raises its own attack-started/finished events for each, so weapon attributes work. Since version 25 |
| 0x98 | f32 ×2 | the held movement keys' direction in the world (x, z; unit length), as Minecraft would walk you with them (forward along the look yaw, left to its left); 0, 0 = none held, or they cancel out. Mycelium's dodge roll goes this way. Since version 36 |

Like SkyCraft: with the raw ticks and their time, Mycelium can work out where Minecraft's player is at
*any* moment on its own clock, as Minecraft's renderer does with partial ticks:
`pos(t) = lerp(prev, latest, (t - tickQpc) / tickLength)`. That's smooth whatever the two games'
frame rates are. The interpolated feet at 0x08 are sampled at Minecraft's frame times, which alias
against Mycelium's physics steps. Both games read the same QueryPerformanceCounter (Windows'
high-precision clock).

### MycInput @ 0x300 (Mycelium → Minecraft, seqlock). Since version 6

The keyboard and mouse as Mycelium's window sees them, for Minecraft to play from (the input
bridge). It's *state*, not events: Minecraft compares it with what it last applied, so nothing is
lost if a read is missed.

| Off | Type | Field |
|---|---|---|
| 0x00 | u32 | seq |
| 0x04 | u32 | flags: bit0 `ACTIVE`, meaning Minecraft should play from this input (Mycelium is focused, and it's normal play); bit1 `CURSOR`, a Minecraft screen is open and the cursor position below is valid (since version 13) |
| 0x08 | u32 | reason when not active: 0 active, 1 Mycelium window not focused, 2 Mycelium has the controls (its menu, pause or cutscene), 3 Minecraft isn't driving the spore, 4 bridge turned off, 5 a hit's knockback has the spore (since version 29: Minecraft lets go of the keys and ignores mouse movement, but keeps following the mouse buttons) |
| 0x0C | u32 | mouse buttons held: bit n = SDL button n (1 left, 2 middle, 3 right, 4 back, 5 forward) |
| 0x10 | u8[64] | keys held: bit n = SDL scancode n (USB HID usage), 0..511 |
| 0x50 | f64 | total mouse movement X since Mycelium started (pixels, right +) |
| 0x58 | f64 | total mouse movement Y (pixels, **down** +, like screen coordinates) |
| 0x60 | f64 | total wheel (notches, up +) |
| 0x68 | u32 | text count: characters typed so far (total) |
| 0x6C | u32 | pad |
| 0x70 | u32[64] | text ring: character n (Unicode) at index n % 64 |
| 0x170 | f64 | cursor X: 0..1 from the left of the overlay picture (since version 13) |
| 0x178 | f64 | cursor Y: 0..1 from the top |

P is never sent: it's Mycelium's pause and menu. Neither are the keys and buttons bound to
Mycelium actions (Bindings block). Escape is sent (since version 13): it's Minecraft's pause menu.

### Bindings @ 0x500 (Minecraft → Mycelium, seqlock). Since version 9

What Mycelium's actions are bound to, as set in Minecraft's Options → Controls → Key Binds,
section "Mycelium". Written every frame.

| Off | Type | Field |
|---|---|---|
| 0x00 | u32 | seq |
| 0x04 | u32 | count: number of actions (0 = Minecraft hasn't sent any yet; Mycelium uses its defaults) |
| 0x08 | 8 bytes | reserved |
| 0x10 | 32 × (u32 kind, u32 code) | per action id: kind 0 unbound, 1 key (code = SDL scancode), 2 mouse (code = SDL button: 1 left, 2 middle, 3 right, 4 back, 5 forward) |

Action ids: 0 interact, 1 show stats (Mycelium's ShowStats and HideStats), 2 subspecies skill,
3 salvage, 4 attack, 5 curio, 6 dodge, 7 skill 1, 8 skill 2, 9 navigate camera, 10 reset camera.
The order is fixed in `MyceliumKeys.java` and `KeyOwnership.cs`. Since version 35, id 11 is
Minecraft's own Attack key (`options.keyAttack`, count 12): not a Mycelium action, only for
Mycelium's hints (`KeyHints.cs`: the attack hint shows it while Mycelium's Attack is unbound).

A key or button bound to a Mycelium action is left out of MycInput (Minecraft never sees it).

### Overlay @ 0x600 (Minecraft → Mycelium, triple buffer). Since version 10

Minecraft's picture: its hand, HUD and open screens on a transparent background, while
`MycState.flags` has `OVERLAY`. Same scheme as SkyCraft's overlay.

| Off | Type | Field |
|---|---|---|
| 0x600 | u32 | state: bits 0–1 = the "middle" slot; bit 2 = the middle slot holds a picture Mycelium hasn't taken |
| 0x604 | u32 | pad |
| 0x608 | u64 | pictures published so far |
| 0x640 + 0x40·n | slot header n (0..2) | 0x00 u32 width, 0x04 u32 height, 0x08 u32 flags (bit0: rows bottom-up), 0x10 u64 frame id |
| 0x100000 + n·0x1FA4000 | slot n pixels | width × height RGBA bytes, **premultiplied alpha** |

- **Minecraft** writes into its own slot (it starts with 1), then swaps it into the middle:
  `old = exchange(state, mySlot | 4)`, and takes `old & 3` as its next slot.
- **Mycelium**, when bit 2 is set, swaps its own slot (it starts with 2) into the middle:
  `old = exchange(state, mySlot)`, and reads slot `old & 3`. Neither side ever touches the other's
  slot, so there's no tearing and no waiting.

### Model @ 0x5FEC000 (Minecraft → Mycelium, seqlock). Since version 11

Your Minecraft player's body, posed and animated by Minecraft, for Mycelium to draw instead of
the spore. Written every Minecraft frame while the overlay is on, in every view. In first person it
has `FIRST_PERSON` set, and Mycelium draws only its shadow (since version 12).

| Off | Type | Field |
|---|---|---|
| 0x00 | u32 | seq |
| 0x04 | u32 | flags: bit0 `VISIBLE`, bit1 `FIRST_PERSON` (draw only the shadow; since version 12) |
| 0x08 | u32 | batches (at most 32) |
| 0x0C | u32 | vertices |
| 0x10 | u64 | frame: goes up on every write |
| 0x40 | 32 × 16 bytes | per batch: u32 texture id, u32 first vertex, u32 vertex count, u32 pad |
| 0x400 | vertices × 24 bytes | triangles: f32 x, y, z (blocks, Minecraft axes, relative to the feet, already turned to where the body faces), f32 u, v (0..1, v from the top), u8 r, g, b, a (tint) |

### Textures @ 0x60EC000 (Minecraft → Mycelium). Since version 11

The textures the model uses (skin, armour, cape), each written once.

| Off | Type | Field |
|---|---|---|
| 0x00 + 0x20·(id−1) | entry for texture id 1..256 (32 before version 15) | 0x00 u32 version (0 = not there yet; set last), 0x04 u32 width, 0x08 u32 height, 0x10 u64 offset of its pixels from 0x2000 |
| 0x2000 | | pixels: RGBA bytes, top row first, not premultiplied (from 0x400 before version 15) |

Since version 15 a texture can also be one picture ("sprite") of Minecraft's item and block sheets,
for held items. The model's u, v then run 0..1 across that sprite.

A new Minecraft run uses new version numbers, so Mycelium reloads a texture whose version changed.

### Enemies @ 0x700 (Mycelium → Minecraft, seqlock). Since version 17

Mycelium's live enemies within 64 units of the spore, for Minecraft's invisible stand-ins.

| Off | Type | Field |
|---|---|---|
| 0x00 | u32 | seq |
| 0x04 | u32 | count (at most 40) |
| 0x10 + 0x30·i | entry | u32 id (Unity instance id), u32 flags (bit0 alive), f32 x, y, z (feet, Minecraft coordinates), f32 width, height (blocks), f32 health, max health (Mycelium units), f32 yaw (Minecraft degrees) |

### Hits @ 0xF00 (Minecraft → Mycelium). Since version 17

A ring of the last 8 hits on stand-ins.

| Off | Type | Field |
|---|---|---|
| 0x00 | u32 | hits written so far |
| 0x10 + 0x10·(n % 8) | entry | u32 enemy id, f32 damage (Minecraft points, after Minecraft's own sums), u32 flags (bit0 critical; not set yet), pad |

Minecraft writes the entry, then the count. Mycelium handles every entry up to the count, and
multiplies the damage by `damageScale` (default 5).

### HitBoxes @ 0x68EC000 (Minecraft → Mycelium, seqlock). Since version 18

Each enemy stand-in's box as Minecraft's client has it (what its crosshair picks: bounding box
grown by its pick radius), for `-mcShowHitboxes`. After the textures; the mapping grows by 0x1000.

| Off | Type | Field |
|---|---|---|
| 0x00 | u32 | seq |
| 0x04 | u32 | count (at most 64) |
| 0x10 + 0x20·i | entry | u32 enemy id, f32 min x, y, z, max x, y, z (Minecraft coordinates), pad |

### Weapons @ after HitBoxes (Mycelium → Minecraft, seqlock). Since version 24

Your Mycelium weapons, one carrot each in Minecraft's inventory (`WeaponBag.cs`, `MycWeapons.java`).
The mapping grows by 0x4000. Strings are UTF-8, zero-terminated.

| Off | Type | Field |
|---|---|---|
| 0x00 | u32 | seq |
| 0x04 | u32 | count (at most 32) |
| 0x08 | u32 | version: changes whenever a weapon or any number changes |
| 0x0C | u32 | select seq: changes when Mycelium asks Minecraft to select a weapon (after a pickup) |
| 0x10 | u32 | select id: that weapon |
| 0x14 | u32 | the weapon the spore holds |
| 0x18 | f32 | damage scale: Mycelium damage per Minecraft damage point (`damageScale=`) |
| 0x20 + 0x120·i | entry (0x100·i before version 30) | below |

| Entry off | Type | Field |
|---|---|---|
| 0x00 | u32 | id (1, 2, …; never reused in a session) |
| 0x04 | u32 | type: 0 Slash, 1 Smash, 2 Stab |
| 0x08 | f32 | damage per swing (Mycelium), with the spore as it is now |
| 0x0C | f32 | swings per second |
| 0x10 | f32 | knockback (Mycelium) |
| 0x14 | f32 | damage multiplier (weapon and attributes) |
| 0x18 | f32 | attack speed modifier |
| 0x1C | f32 | reach in blocks: how far the weapon's collider gets from the spore's middle in its attack; 0 = not measured yet (since version 28) |
| 0x20 | utf8[64] | name |
| 0x60 | utf8[160] | attributes, "Name: description; Name: description" |
| 0x100 | utf8[32] | key: the weapon's prefab name (`Resources/<Type>/<key>`), which picks its look in our resource pack; entries are 0x120 bytes since version 30 (0x100 before) |

### Thrown @ after Weapons (Minecraft → Mycelium, seqlock). Since version 27

Your weapons flying through Minecraft's world as items after you throw them. Mycelium shows the real
weapon on the same path and leaves it on the ground as a pickup once Minecraft removes the item. The
mapping grows by 0x400.

| Off | Type | Field |
|---|---|---|
| 0x00 | u32 | seq |
| 0x04 | u32 | count (at most 16) |
| 0x10 + 0x20·i | entry | u32 weapon id (Weapons table), u32 flags (bit0 landed), f64 x, y, z (Minecraft, the item's feet, interpolated to the frame) |

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
