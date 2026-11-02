# MyceliumCraft: design

> Play Mycelium as the main game while *being* a Minecraft player. Same approach as
> [SkyCraft](https://github.com/chasmlol/SkyCraft) (Skyrim + Minecraft), with Mycelium in Skyrim's place.

Status: Phase 1, step 1 (Minecraft walks on Mycelium's ground; layered, protocol v3) · 2026-10-17

## 1. Core principle (taken from SkyCraft)

**Neither game is rewritten.** Minecraft runs its own movement, physics, inventory and combat.
Mycelium runs its own world, enemies, levels and saves. The two mods only **translate** between
them:

- Minecraft tells Mycelium *where the player is* (and later: what it hit, what to draw).
- Mycelium tells Minecraft *what the world is shaped like* (the ground grid, §5) and *where the enemies are* (later).

**Minecraft is authoritative for the player** (AGENTS.md: "Which game is authoritative: B").

## 2. The two halves

```
┌────────── MyceliumCraft.exe (Unity 2022.3, Mono) ──────────┐        ┌──────── javaw.exe (Minecraft 26.3 + Fabric) ────────┐
│  dev.myceliumcraft package (mycelium/)                      │        │  myceliumcraft Fabric mod (fabric/)                 │
│                                                             │        │                                                     │
│  MinecraftDriver ◀── McState: feet pos, yaw, flags, ack ────┼─────── │  MinecraftMixin → MyceliumCraftClient.afterFrame    │
│     moves the spore's Rigidbody in X, Y, Z                  │        │     (end of every rendered frame)                   │
│  MinecraftDriver ─── MycState: spore pos, teleport ─────────┼──────▶ │  teleports the player (client + integrated server)  │
│  GroundScanner  ──── GroundGrid: 128x128 heights ───────────┼──────▶ │  MycCollision → BlockCollisionsMixin                │
│     raycasts around the spore                               │        │     Mycelium's ground in every collision query      │
└─────────────────────────────────────────────────────────────┘        └─────────────────────────────────────────────────────┘
                        shared memory  Local\MyceliumCraft_v1  (layout: protocol/PROTOCOL.md)
```

### 2.1 Minecraft side: a Fabric mod

- Fabric Loader 0.19.5 and Fabric API 0.161.0+26.3, the same versions SkyCraft uses. Build files
  are copied from SkyCraft's `fabric/`.
- Minecraft 26.x ships with readable names, so the Mixin (a hook into Minecraft's own code)
  targets `Minecraft.renderFrame` by name.
- Every frame it writes the player's **interpolated** feet position, meaning the position Minecraft
  actually draws that frame, between its 20-per-second physics ticks. It also writes yaw, pitch and
  flags. The Java code that talks to Windows uses Java 25's built-in foreign-function API, like
  SkyCraft's `SkyLink`.
- Build: `fabric\gradlew dist` with JDK 25. Prism's bundled Java at
  `PrismLauncher\java\java-runtime-epsilon` is a full JDK, so it works. The jars go to `dist\minecraft-mods\`.

### 2.2 Mycelium side: a Unity package compiled into a private build

Mycelium has **no mod loader**. SkyCraft uses SKSE, an existing plugin loader for Skyrim; there is
nothing like it for Mycelium. The general Unity option is BepInEx. Instead, because we have
Mycelium's source and the matching Unity (2022.3.0f1):

1. `tools\build-mycelium.ps1` mirrors Mycelium's `Assets`, `Packages` and `ProjectSettings` into
   `work\Mycelium`, which is gitignored. The first time, it also copies `Library` (Unity's import
   cache, so it doesn't re-import 1 GB of assets). **The real Mycelium folder is only read.**
2. It adds `"dev.myceliumcraft": "file:<repo>/mycelium"` to the copy's `Packages\manifest.json`.
3. It runs Unity in batch mode with `MyceliumCraft.Editor.BuildMyceliumCraft.Build`, which builds
   `work\build\MyceliumCraft.exe`. It sets two of the copy's player settings first: the product
   name "MyceliumCraft" (its own save folder) and, since 2026-10-24, a **resizable window**
   (`resizableWindow`; Mycelium's project has it off). Full screen stays the default; Alt+Enter
   switches to a window. The log says at start whether the window has a resizable border
   ("window: resizable border yes …").

The package changes none of Mycelium's code:
- `MinecraftDriver` starts itself through `[RuntimeInitializeOnLoadMethod]` and survives scene
  changes.
- It finds Mycelium's `PlayerController` **by name, using reflection**. Reflection means looking a
  class up by its name while the game runs. We need it because a package can't link directly
  against the game's own scripts.
- `[DefaultExecutionOrder(10000)]` makes its `FixedUpdate` run after `PlayerController`'s, so the
  velocity it sets is the one that sticks.
- It sets `Application.runInBackground = true`. Mycelium normally freezes when its window loses
  focus, and Minecraft has the focus while you play.
- The build's product name is `MyceliumCraft`. Unity's save folder (`persistentDataPath`) follows
  that name, so this build never touches your normal Mycelium saves.

## 3. The link

Shared memory is a block of RAM both processes map, like SkyCraft's `Local\SkyCraft_v1`.
**Mycelium creates it, and Minecraft opens it**, because the host game owns it. Each side writes a
heartbeat every frame. If the other side's heartbeat is older than 3 s, that side counts as gone:
- If Minecraft goes, Mycelium controls the spore again.
- If Mycelium goes, Minecraft logs it.

Per-frame blocks use a seqlock. That's a counter that is odd while the block is being written, so
the reader never sees half an update. Full byte layout: `protocol/PROTOCOL.md`.

## 4. Coordinate mapping (fixed, since Phase 1)

Phase 0 moved the spore *by the same amount* as Minecraft's player, starting from an anchor.
Sharing the ground needs one fixed formula instead, so a Mycelium height means the same thing to
both games:

```
mc.x =  myc.x / SCALE      mc.y = myc.y / SCALE      mc.z = -myc.z / SCALE   (Minecraft +Z is south)
mc.yaw = myc.yaw + 180
SCALE = scale= in work\build\MyceliumCraft.cfg (default 1.4 Mycelium units per block)
```

Only Mycelium uses SCALE. Everything it sends Minecraft is already in Minecraft coordinates.
1.4 was chosen from the Phase 0 log: Minecraft walks at 4.3 blocks/s, and the spore's base speed
is 6 units/s (`moveSpeedBase: 6` in `PlayerBundle.prefab`). The tutorial's ground is at
Mycelium y ≈ 125, which is Minecraft y ≈ 89, well inside Minecraft's height range and far above a
superflat world's own ground at y −60.

### 4.1 Each level, in order (MinecraftDriver phases)

1. **Idle:** Minecraft isn't linked, or isn't in a world. Mycelium controls the spore as normal.
2. **Scan and teleport:** scan the ground (§5) and send it. Then ask Minecraft to put its player
   where the spore's feet are (`teleportSeq` + target). Teleport numbers start from the clock, so a
   restarted Mycelium never reuses one.
3. **WaitingForTeleport:** wait until Minecraft reports it applied that teleport (`teleportAck`).
   Minecraft teleports only once it has a ground grid, so it never lands in empty air. It moves both
   its own player and the integrated server's copy, as SkyCraft does, so the server doesn't pull
   the player back.
4. **Driving:** every physics step:
   - **Target, timed by Minecraft's ticks** (protocol v8, as SkyCraft does):
     - Minecraft sends its last two physics ticks (it moves the player 20 times a second), plus
       the moment of the latest tick on Windows' QueryPerformanceCounter. That moment is the
       counter at the start of Minecraft's frame, minus `partial × 50 ms`.
     - `TickHistory` keeps the last 16 ticks as the points Minecraft's renderer passes through.
       Each physics step works out its own moment on that clock (frame counter +
       `fixedUnscaledTime − unscaledTime`) and takes the position there, interpolated, or
       continued straight for up to half a tick past the newest.
     - The spore's velocity is then `(target − position) / step time`, so it lands on the target
       each step, capped at 40.
     - **Why:** reading Minecraft's latest position each step aliased against Unity's physics
       steps (0, 1 or 2 per frame, often back to back), so the spore moved unevenly. The camera
       follows the spore, so the whole world juddered, worst when looking down at the ground.
       Open-ground speed unevenness went **0.159 → 0.011** (`tools/screen_jitter_check.py`).
     - **Clock trap:** Unity's (Mono's) `System.Diagnostics.Stopwatch` is *not* the same counter;
       it was about 7.9 days off. Mycelium calls `QueryPerformanceCounter` itself.
     - **Fallback:** without tick data (an older Minecraft side), the old pull is used: smoothed
       velocity + `error × 12/s`.
   - **Gravity:** the gravity Mycelium itself adds is cancelled, because Minecraft does gravity now.
     That's PlayerController's −25 plus Unity's own, since `useGravity` is on for the spore.
   - **Facing:** the spore faces where **Minecraft's body** faces (`yBodyRot`, interpolated the same
     way), which turns toward the walk when strafing, while the head keeps looking ahead.
     - Mycelium's own "face where I walk" (`PlayerController.LookAt`, gated by its public
       `looking`) is switched off while Minecraft drives, and back on afterwards.
     - **Why:** with the input bridge, Mycelium also gets WASD, and the two turned the spore against
       each other. With D held while strafing: 75 direction reversals in 9 s and up to 15.8° per
       frame, against 0 and 0° now.
   - **Walk animation:** follows Minecraft's speed.
5. **Resync:** Mycelium teleports Minecraft's player back onto the spore (back to step 3) when:
   - Mycelium's own collision holds the spore more than 6 units away, for something Minecraft
     doesn't know about, or
   - they disagree by more than **1.5 units for 2 seconds in a row**, meaning one is wedged
     somewhere only its own game thinks is open. Added after you were stuck for 30 s with them
     only about 2 units apart, so the 6-unit rule never fired. Normal walking stays under ~0.5.

   A new Minecraft world or a new Mycelium scene also starts over.

   **Resyncs only move you; they never turn you** (since 2026-10-20, protocol v21). Only the
   first teleport, when Minecraft starts driving, sets where Minecraft's player faces. Resyncs send
   a NaN yaw ("keep"). The spore faces where Minecraft's *body* points, which lags up to about 50°
   behind where you look. So sending it after a hit's knockback turned you: 323° to 273° in your log.

## 5. Ground grid: layered columns (SkyCraft's CollisionField, stage A)

**What the tutorial level is made of** (measured with `-mcSurvey` and `tools/survey_check.py`, §7):
- The walkway is a mesh called `Terrain`.
- Over about half of it, roughly 10 units up, there's a Unity terrain tile,
  `Terrain_(146.90 47.03 -324.77)`. Unity terrain is solid only from above: rays from below never
  hit it, even counting back faces. That's why the spore walks freely under it, and it's what makes
  the tutorial's tunnels.
- Trees stack up to 6 surfaces over the path: three layers of `Leaves`, a `Trunk`, the terrain
  tile, then the floor.

So one height per cell can't describe the level. Version 1 of the grid tried that, and blocked
the tunnels. Each cell is a **column of up to 4 solid pieces** instead; everything else is open
space.

**Mycelium side (`GroundScanner`):**
- **Grid:** 128 × 128 cells, ½ block each, so 64 × 64 blocks centred on the spore. It's laid out in
  Minecraft coordinates.
- **Surfaces:** each cell is raycast straight down from 60 units above the spore. After each hit
  the ray carries on just below it, finding up to 10 surfaces, each the top of something solid.
- **Undersides:** a ray going *up* from each lower surface, with back faces counted, finds the
  underside of the piece above. It stops **0.15 units short of the surface above**, and a hit
  within 0.01 of that surface counts as "none". Otherwise it hits a one-sided surface's own back
  face and reports "underside = top". Those zero-thickness pieces give Minecraft no box at all, so
  the narrow passage's walkway simply **wasn't there in Minecraft**: you fell through it onto the
  hidden terrain 1.8 units below and got wedged. In the tutorial scan, 7,670 pieces were affected.
- **Thin undersides** (since 2026-10-18):
  - **What's checked:** if that ray finds nothing, a second one goes right up to the surface. A hit
    at least 0.01 below the surface counts as a thin underside. A surface's own back face is exactly
    at the surface (0.000 below) and still doesn't.
  - **Why:** where a solid tapers to an edge, its underside can be closer than 0.15. In the hub, a
    rock (`Rok_low`) in front of the run start was 0.03 thick at one column. The underside was
    missed there, the gap below was "filled", and that column became a 3-block pillar. It blocked
    the way to the run-start trigger in Minecraft, while the spore walks under the rock in Mycelium.
  - **Found:** 103 thin undersides in the hub scan and 152 in the tutorial.
- **Pieces:**
  - Everything below the lowest surface is solid.
  - Each higher surface is a piece from its underside to its top.
  - If no underside is found (one-way surfaces like a walkway mesh or the terrain tile):
    - if the gap below it is smaller than **2.5 × the spore's height** (4.25 units), it's
      **filled**. Those slivers are hidden ground just under a walkway. The smallest one-way gap
      the spore ever walked in was 5.79 units, while gaps of 0–5 units occur about 3,300 times in
      the survey and were never walked. Tutorial: 2,545 filled.
    - otherwise it's a slab ½ block thick (a tunnel roof).
- **More than 4 pieces:** the pieces with the **smallest gaps** between them are merged first,
  for example layers of leaves. Gaps that small can't fit a player anyway.
- **Layers counted as ground:** every physics layer the spore's layer collides with (read from
  Mycelium's own collision settings), except characters and weapons. In the tutorial that's
  Default, TransparentFX, Water, Enviroment, Sun, Wall, Background, IgnoreFog, Interactable and
  Furniture.
- **When:** when the spore is more than 16 blocks from the grid's centre, and at each new level or
  player. In the tutorial: about 49,000 rays, **30 ms**, once per 16 blocks walked.

**Checked offline** against 246 positions the spore really walked (your two logs) and the
survey: does Minecraft's 0.6 × 1.8-block player fit there, stepping up at most 0.6 blocks?

| Model | Fits |
|---|---|
| One height per cell, top surface | 99 / 246 |
| Layered, but only the top 4 surfaces (the first layered build) | 208 / 246 |
| **Layered, 10 surfaces, merged to 4 pieces (now)** | **232 / 246** |

That table is for a normal-size Minecraft player. Of the 14 left, 13 are low overhangs (mostly
`pCone1`, 1.5–2.1 units above the floor) that the shorter spore walks under. §5.1 shrinks the
player to fix that.

**Minecraft side:**
- `MycCollision` keeps the latest grid and builds a collision shape per block position when asked:
  one box per piece per ½-block cell inside the block.
- `BlockCollisionsMixin` (the same hook as SkyCraft's) adds that shape to every block-collision
  query. It's registered for both sides, so the integrated server's movement checks see the same
  ground as the client.
- Minecraft's own walking, step-up (0.6), jumping, falling and fall damage then work against
  Mycelium's ground, unchanged.

**Known limits:**
- One-way surfaces become ½-block slabs, so you can't jump up through them from below like the
  spore could.
- Things that move (doors, platforms) aren't rescanned.
- Holes go all the way down to the superflat world's own ground.

### 5.0.1 Minecraft rules changed by the mod (for combat)

Always, while the mod is installed (your choice, 2026-10-19; `MycRules`, read on the integrated
server's thread), Minecraft:
- **doesn't hurt players from falls:** `LivingEntity`/`Player.causeFallDamage` return false.
  Mobs keep their fall damage.
- **doesn't heal players naturally:**
  - `FoodData.tick`'s two `heal` calls are skipped (hunger itself still works);
  - `ServerPlayer.tickRegeneration`, the Peaceful-difficulty healing, is skipped.

This is in the mod, not Minecraft's game rules (`fallDamage`, `naturalRegeneration`): those are
saved into your world, and these changes aren't. Without the mod, Minecraft is normal again. Each
prevented fall or heal is logged at most once a second.

### 5.0.2 Health: Mycelium's is in charge (your choice, 2026-10-19)

- **Mycelium** reads its `PlayerHealth.currentHealth` and `maxHealth` (by reflection) and sends
  them in MycState (0x50, 0x54).
- **Minecraft** (`MyceliumCraftClient.syncHealth`, applied on the integrated server):
  - health = Mycelium's ÷ 5, so 5 Mycelium health is half a heart;
  - max health = Mycelium's max ÷ 5, through Minecraft's max-health attribute (100 → 20, the
    normal 10 hearts);
  - sent when it changes, or when Minecraft's own health has drifted (its own damage), at most
    every 0.25 s.
- **Never 0 early:** while Mycelium's health is above 0, `HealthFloorMixin` keeps a player's
  Minecraft health at least 0.01 (still half a heart on screen). So Minecraft's own damage, `/kill`
  included, can't kill you. Mycelium's small values still show: 3 health is 0.6, half a heart.
- **Death:** Mycelium's health at 0 for 0.25 s (so a set-up frame can't do it) kills the Minecraft
  player (`kill`), with Minecraft's normal death and its item drop.
- **Death view** (since 2026-10-19): at that moment Minecraft's view is switched to third person
  behind, and Mycelium's camera follows it as it follows F5.
  - Mycelium waits 3 s before its death screen fades in. Meanwhile you see your Minecraft body do
    Minecraft's own death animation: it tips over sideways and turns red. Nothing extra is needed
    for that, because the body Mycelium draws is posed by Minecraft's own player renderer
    (`MycPlayerModel`), and that renderer does it for a dying player.
  - Your own view comes back when Mycelium is alive again, or when Mycelium leaves the level.
    Minecraft doesn't save the F5 view to its options, so a crash while dead leaves nothing behind.
  - While dead, the eye-height ratio sent to Mycelium is held at 1, so the dying pose can't move
    the camera the way crouching does.
- **Minecraft's own "you died" is held back** (`PlayerCombatKillMixin`). On death Minecraft either
  opens its death screen, or with the "immediate respawn" game rule on, respawns at once. Your
  world has that rule on: the first try had the player back on its feet at world spawn within a
  second, so there was no dying body to show (your report, 2026-10-19).
  - When the death came from Mycelium's, neither happens. The body lies dying, and the respawn
    comes when Mycelium is alive again (`LocalPlayer.respawn`, as before).
  - If Mycelium goes away, or isn't back in a level 30 s after the death, Minecraft gets its death
    screen or immediate respawn back, so you can't be left dead with no way out.
  - In hardcore worlds this doesn't happen: Minecraft handles the death as normal.
  - **There are two ways in.** `Gui.setScreen(null)`, which is any screen closing, also opens the
    death screen or respawns at once while the player is dead. That respawned you on the second
    try. `GuiDeathScreenMixin` makes a closed screen just close while the death is held. The first
    such close per death is logged with who did it.
- **Respawn:** Mycelium with health again (back in the hub) respawns it (`LocalPlayer.respawn`).
  Minecraft's "Loading terrain" screen after that would wait for the player's chunk to be prepared
  for drawing, which never happens while the overlay skips Minecraft's world. So
  `WaitingForPlayerChunkMixin` makes it not wait while Mycelium is running (as SkyCraft does).
- **Emerge: the Warden's dig-out on your body when Mycelium sprouts you** (since 2026-10-20).
  - **When:** every time the spore arrives in The Carcass (after a death, and after a run), unless
    it died for good. Mycelium then takes the controls away, waits `timeUntilHubSprout` (0.1 s in
    The Carcass), plays the spore's "Sprout" animation (about 2.9 s), and gives the controls back.
  - **Mycelium side** (`SproutWatch`): sends how far through that it is (MycState 0x5C, protocol
    v22). That's 0 while waiting, then the Sprout animation's progress up to 1, and −1 when there's
    no sprout.
  - **Not sped up by Speed** (since 2026-10-25, your rule). Mycelium runs the spore's whole
    animator at its Speed level (`CharacterStats`: `animator.speed = animatorSpeed`), which sped up
    the Sprout and so the emerge. `SproutWatch` holds the animator at ×1 while the Sprout plays,
    then puts the Speed value back. Mycelium gives the controls back when the Sprout ends, so the
    two stay in step. `-mcSproutSpeed n` sets the animator to n before the Sprout, as a Speed level
    would.
  - **Minecraft side:** Minecraft's view goes to third person *in front* for the emerge (facing
    you as you dig out; your choice, 2026-10-20), and back after. It's the same switch the death
    uses, which picks third person behind.
  - **The animation** (`EmergePose`, since 2026-10-20 second version): made for a player body, by
    how far through the sprout (0 to 1), eased between key points. In model pixels, the feet and
    the ground are at 24.
    - **Break out:** everything starts 42 pixels down, with the arms straight up. Raised hands
      only reach 2 pixels above the head, so hands and head come out together.
    - **Pull (about 22–55 %):** each hand is planted on the ground in front and stays there while
      the torso rises. Its arm angle is worked out every frame from the shoulder's height:
      world angle = −acos((ground − shoulder) / arm length), minus the torso's lean. The left
      plants a little after the right, like clawing.
    - **Push:** the arms go down beside the body to lift the hips out, then the right leg and the
      left leg step up.
    - **The head** nods down at the ground up to 0.55 rad on top of the torso's lean (more since
      your note, 2026-10-20).
    - **The torso leans at most 0.35 rad (20°).** The head and arms are carried with it, as if
      attached at the neck: move = torso + torsoTurn × own move, turn = torsoTurn × own turn.
    - **The first version** played Minecraft's own Warden emerge (6.68 s, read at runtime). The
      Warden is about 3 blocks tall, so on a player all its clawing happened with even the hands
      underground, and its torso leaned up to 70°. You saw arms that didn't move and too much tilt.
    - **Checked offline** on Minecraft's standard humanoid model, every 5 %: the hands stay at
      y 23.4–24.3 from 22 % to 55 % while the torso rises from 28 to 12, then the hips come out at
      50 %. At 100 % it's the normal pose.
  - **Where it's applied** (`HumanoidEmergeMixin`): in Minecraft's own posing of every humanoid
    model (the body, armour, the cape), but only while `MycPlayerModel` captures your body. Held
    items are placed from the arm's pose, so they follow.
  - **Hidden underground:** Mycelium's ground hides the buried parts, because the body is drawn at
    the spore's feet like any other mesh.
- **Max health changes keep the share** (since 2026-10-20, your rule). Mycelium changes max health
  on its own: a Vitality level, a weapon's stat attribute while held, a spore's stats. When it does,
  Mycelium leaves current health where it was.
  - `MinecraftDriver.KeepHealthRatio` sets current health to the share it had of the old max, then
    redraws Mycelium's bar (`displayHealth`, `UpdateHudHealthUI`). Minecraft follows as usual.
  - Not when Mycelium set current health to the new max itself (`ResetHealth`), and not while dead.
  - A lower max lowers current health, but that isn't a hit: Minecraft's red flash and the hurt
    tilt only react when max health didn't change.
  - **Checked** with `-mcHealthTest` in Daybreak Arboretum: at half health, Vitality +5 took max
    from 100 to 150 and current from 50.3 to 75.4 (50 %); Vitality −5 took 150 to 100 and 77.1 to
    51.4 (51 %, after some regeneration). No hurt tilt.
- **Outside Mycelium's levels** (max 0 or no spore): not synced, and max health is put back to 20.

### 5.0.2.2 Walking speed follows Mycelium's Speed stat (since 2026-10-21)

- While Minecraft drives, the spore follows Minecraft's player. So Mycelium's Speed stat, and its
  speed effects (Nimble's boost, slows), changed nothing until now.
- **Mycelium sends** the spore's walking speed as a multiple of its base speed (MycState 0x60,
  protocol v26): `PlayerController.moveSpeed` ÷ `SporeAttributeRanges.moveSpeedBase`.
  `moveSpeed` is the base plus the Speed level's bonus, and speed effects change it while they
  last.
- **Minecraft multiplies** the player's movement speed by it: a modifier "myceliumcraft:speed",
  `ADD_MULTIPLIED_TOTAL` (factor − 1), on the integrated server when it changes by more than 1 %.
  It's taken off outside Mycelium's levels. Sprinting and Minecraft's own effects multiply on top.
  Minecraft widens its FOV with movement speed, as with a Speed potion, and Mycelium's camera
  follows that.
- **Respawns** (since 2026-11-02): a respawn makes a new player without the modifier. The factor
  hadn't changed, so it was never put back: after a death and the trip to the Carcass, the boost
  was gone (your report; Minecraft set ×1.63 in the same second it respawned the player). So once
  a second the server also checks the modifier is there at the right amount, and puts it back if
  not ("the player had lost the ×… ; put back").
- **Checked:**
  - In Daybreak Arboretum the spore walks at 6.00 with base 6.00: ×1.00.
  - With `-mcAttribute Nimble` and the fake Minecraft's hits: ×1.30 after a hit, fading back to
    ×1.
  - Both stand-ins pass the 0x60 check (1.25).

### 5.0.2.1 Experience level = nutrients (since 2026-10-19)

Mycelium reads `NutrientTracker.currentNutrients` (found by name; on 'NutrientCounter') and sends it
(MycState 0x58). Minecraft sets its experience level to that number with an empty bar
(`setExperienceLevels`, `setExperiencePoints(0)`). Mycelium is in charge: if Minecraft's level
drifts (XP orbs, enchanting), it's put back within 0.25 s.

### 5.0.3 Combat: your Minecraft attacks hit Mycelium's enemies (since 2026-10-19)

Following SkyCraft's actor proxies.
- **Mycelium** (`EnemyCombat`): every frame it sends its live enemies within 64 units (`EnemyHealth`,
  found by name; up to 40). Each has an id, feet position, size and health; the size comes from the
  enemy's solid colliders.
- **Minecraft** (`combat/EnemyProxies`, every server tick): keeps one invisible stand-in
  (`EnemyProxy`, a real creature: no AI, no gravity, not pushable, not saved, drawn by nothing) per
  enemy, moved and resized to match.
  - Minecraft's own attack rules work on it: weapon damage, cooldown, critical hits, sweeping,
    enchantments, arrows.
  - When a player hurts it, the final damage goes to Mycelium (Hits ring). The stand-in loses
    nothing, and other damage (fire, mobs) is ignored.
- **Back in Mycelium:** each hit becomes `EnemyTakeDamage(damage × damageScale, wasFromWeapon:
  true)`, so Mycelium's armour, damage numbers, blood, sound and death all happen as for the spore's
  own hits.
- **`damageScale`** defaults to 5, the same rate as health: 1 Minecraft point = 5 Mycelium damage,
  an iron sword (6) does 30, a beetle has 30.
- **Enemies hitting you** already work through Mycelium: its health drops, and Minecraft's follows
  (§5.0.2).

**The stand-in's size reaches Minecraft's client** (since 2026-10-19): the enemy id, width and
height are synced creature data (`SynchedEntityData`), and the client resizes its copy when they
change. The crosshair picks on the client's copy, which before this kept Minecraft's default
0.6 × 0.6 box.

**Fast enemies' hitboxes keep up** (since 2026-10-19):
- **The problem:** Minecraft moves a creature's client copy from server updates (by default every 3
  ticks for this kind) and glides it over a few ticks, so fast enemies' hitboxes lagged behind.
- **The fix:** at the start of every client tick (before Minecraft picks what the crosshair is on),
  `EnemyProxies.clientSnap` puts each client copy exactly at Mycelium's current enemy position
  (read straight from the enemy table) and cancels the glide.
- **The server's copy,** which Minecraft uses to check a hit, is updated every tick
  (`updateInterval(1)`).

**Red hurt flash, as Minecraft's** (since 2026-10-19):
- **Your Minecraft body:** when Mycelium's health drops, Minecraft's client hurt animation is
  started (`hurtTime = hurtDuration = 10`). The captured player model then carries Minecraft's own
  red tint for those 10 ticks. Minecraft's own `hurtTo` on a lower synced health does this too;
  this makes sure of it.
- **Mycelium's enemies** (`HurtFlash`): when an enemy's health drops (from anything), its renderers
  get an extra material for 0.5 s that draws the mesh again in two steps
  (`Resources/MyceliumCraftHurtTint.shader`):
  1. multiply the colours already there by (1, 0.72, 0.72), so red stays and green and blue dim,
     and every detail of the enemy's look stays visible (a flat 40 % red layer covered them);
  2. add (0.12, 0.03, 0.03) of red light, so the flash isn't darker than the enemy.

  The two passes are tagged SRPDefaultUnlit and UniversalForward, so URP draws both.
  **Poisoned enemies flash green** (since 2026-10-21, your choice). If Mycelium's `Poison` effect
  is on the enemy when its health drops, the same shader is used with red and green swapped:
  multiply (0.72, 1, 0.72), add (0.03, 0.12, 0.03). Poison hurts every 0.5 s for about 5 s, so a
  poisoned enemy stays green while it lasts. An enemy poisoned or cured mid-flash has its colour
  swapped in place. Taking it away removes only that material. `-mcFlashCheck` logs the colour and
  detail on screen around an enemy during and after its flash.

  **Models made of several sub-meshes** (since 2026-10-21). A sub-mesh is one part of a model
  with its own material: the crab's body has 4, "SporeModel" enemies 2. Unity draws an extra
  material over the last sub-mesh only, so those enemies hardly flashed (your report: crabs). They
  get an **overlay copy** instead, made the first time they flash: a child renderer with the same
  mesh (and, if skinned, the same bones, so it moves exactly with them). It has the tint on every
  sub-mesh and no shadow, and is switched on only while flashing. One-part models keep the extra
  material.
  - The first time each kind of enemy flashes, the log lists its renderers, their sub-meshes and
    materials, and which are tinted.
  - `-mcFlashAll` flashes every enemy in the level once and counts the overlay copies drawn, 0.1 s
    and 0.6 s later.

**Hurt camera tilt, as Minecraft's** (since 2026-10-19, `HurtTilt`): when the spore loses health
(and doesn't die), Mycelium's camera leans toward whatever hit it and swings back, in every view.
- **The shape is Minecraft's** (`GameRenderer.bobHurt`): over 0.5 s (10 ticks), with f = time left
  ÷ 0.5 s, the lean is sin(f⁴ × π) × 10°, so it peaks within the first 0.08 s and eases back.
  Minecraft's is 14°; you found that a little strong, so it's 10° here.
  The top of the view leans toward the hit: a hit from the left rolls the view left, from in
  front it nods down. The direction is measured from where the player faces.
- **How far** is Minecraft's own "Damage Tilt" accessibility setting (0–100 %), sent in McState
  (0x8C, protocol v20).
- **What hit you:** Mycelium's `PlayerTakeDamage` takes only an amount, so at the moment health
  drops the mod looks for the hitter:
  1. the nearest collider within 4 units that belongs to an enemy, or has an attack-looking script
     (Attack, Hitbox, Projectile, Damage, Shell, Tail or Collision in its name, not the spore's own
     weapon). The direction is to the enemy itself (or the middle of the hitbox): a beetle touching
     the spore gave a nearest point only 0.1 units from the spore's middle.
  2. Otherwise, the nearest live enemy within 15 units.
  3. Otherwise none: the view rolls to the left (what Minecraft does with hurt direction 0).

  It can guess wrong. Falling into a death plane near an enemy leans toward the enemy.
- **Applied** after Cinemachine and the pitch correction, as a turn in the camera's own space
  (`CameraSync.AfterCinemachine`). Cinemachine sets the camera again every frame, so nothing builds up.
- **Not copied:** Minecraft's death roll (the view tips over sideways when you die).
- **`-mcHurtTest`** (test aid): 1 damage every 3 s through `PlayerTakeDamage`, from a test hitter
  1.5 units in front, right, behind, then left of the spore.

**`-mcShowHitboxes`** (test aid, `HitboxView`): the box Minecraft's client really has for each
stand-in (its bounding box grown by its pick radius) is sent back every frame (HitBoxes block) and
drawn in Mycelium's window as a wireframe. Green, red for 0.3 s after a hit lands, on top of
everything (`Resources/MyceliumCraftLine.shader`).

**Checked** with the fake Minecraft (`LINKTEST_HIT=1`) in Daybreak Arboretum: the beetle 36 blocks
away arrived (1.6 × 0.7 blocks, 30/30); three 2-point hits did 10 damage each (30 → 20 → 10 → 0),
and the dead beetle left the table. The tutorial has no enemies at its start.

**Attacks only at full charge** (since 2026-10-20, your rule): while Mycelium is playing, an attack
can't start until Minecraft's attack indicator is full.
- `MinecraftMixin` stops `Minecraft.startAttack` (on the client), so there's no swing, no hit and
  no block mining. Each block is counted and logged (at most once a second).
- Ranged weapons (bows) don't use `startAttack`, so they're unchanged.
- The rule before it (for a few hours) made hits under 90 % charge do 5 % damage. It worked, but
  you changed your mind and it was removed.

### 5.0.4 Weapons: one carrot each in Minecraft's inventory (since 2026-10-20)

Your choices: carrots for now, with the true numbers rewritten when the spore changes; the spore
holds the weapon whose carrot is selected; weapons are cleared on death and each run starts with a
Stick.

**Mycelium side** (`WeaponBag`):
- **Picking up** still goes through Mycelium (`WeaponInteraction.Interact`: the new one in the
  hand, attributes unequipped/equipped). The weapon you held, which Mycelium lets go into the
  world, is saved into the bag instead and its object removed. The save copies
  `LevelEnd.GoToNextLevel`: attribute type names, values and rarities. Then Minecraft is asked to
  select the new carrot. Until it does (at most 3 s), Minecraft's hand isn't followed.
- **Switching:** the spore holds the weapon whose carrot is in Minecraft's main hand (McState
  0x90). The held one is saved (attributes unequipped) and removed. The wanted one is rebuilt much
  as `LevelGenerator` rebuilds the held weapon in a new level:
  1. the prefab from `Resources/<type>/<name>`, no random attribute (`acceptingAttribute`), the
     scale fix, tags and layers;
  2. a frame later, its attributes added back (`AttributeAssigner.PickAttFromString`), value and
     rarity set;
  3. 0.1 s later, `ApplyWeaponPositionAndRotation` and each attribute's `Equipped`.

  It's set through `SwapWeapon.O_curWeapon`, so Mycelium's buffs that follow the weapon (Fury,
  Bonus Damage) move with it. Not mid-swing: it waits for `canUseAttack`.
  - **Unlike `LevelGenerator`,** attributes the prefab already has aren't added again (a Cactus
    Mace has Hazardous and Nasty built in), and the weapon keeps its own stat list. Re-adding them
    doubled them on every switch ("Heavy Hazardous Nasty Hazardous Nasty Rose Mace").
  - A weapon's damage multiplier is worked out again for the current loop when it's rebuilt.
    Mycelium's own between-levels keeps the old one.
- **Empty hand** (since 2026-10-20): when Minecraft's main hand holds no weapon carrot, the held
  weapon goes into the bag (attributes unequipped). The spore then holds an invisible stand-in:
  `Slash/Stick` with no random attribute, only Mycelium's "Nothing" attribute.
  - So no weapon effects and no knockback apply. Minecraft's hits are its fist (1 point, 5 in
    Mycelium).
  - Why a stand-in: Mycelium always needs a held weapon (its level end and death read it), and its
    next level rebuilds the held weapon's attributes and expects at least one.
  - The stand-in's renderers are switched off (`Renderer.enabled`, which the camera's hiding
    doesn't touch). It's hidden again when a new level rebuilds it.
  - A pickup with an empty hand removes the stand-in instead of bagging it.
  - **Only when the spore holds one of your weapons.** In The Carcass after a run it holds none,
    and making the stand-in there anyway meant Mycelium's first level gave no Stick: it only gives
    one when the spore holds nothing (your report, 2026-10-21).
  - Before, the last weapon simply stayed in the hand (your report).
- **No flash on a switch** (since 2026-10-20): the new weapon is built in a coroutine, after the
  frame's spore hiding, so it was drawn for one frame. `CameraSync.RefreshHidden` runs again in
  `LateUpdate`, before drawing.
- **No number churn mid-switch:** while a switch runs, the numbers aren't worked out again. The
  held weapon's stat attributes are off for that moment, and every other carrot was rewritten
  ("1 rewritten" every half second in your log while switching).
- **Runs:** arriving in The Carcass (a death or a finished run) empties the bag. The next weapon
  Mycelium gives the spore (the run's Stick from `LevelGenerator`) is added. Between levels the
  rebuilt held weapon is matched to its bag entry.
- **Numbers, per weapon, with the spore as it is now:** that weapon's stat attributes count
  instead of the held one's (`primalAmount`, `speedAmount`).
  - **Damage per swing** = round(attackDamageBase + (Primal − 1) × increment) × Fungal Might ×
    `statNums.advDamage.MultValue`, as `PlayerAttack` works it out.
  - **Swings per second** = (attackSpeedBase + (Speed − 1) × increment) × `wpnAttackSpeedModifier`
    ÷ (0.9 × the attack state's length). `PlayerAttack` gives the controls back at 90 % of the
    Slash, Smash or Stab state.
  - **State lengths** are measured on the spore's animator: each state is put on for no time
    (`Play`, `Update(0)`), its length read, and the animator put back, within one frame. The
    states run faster than their clips: Slash 1.14 s (clip 1.71), Smash 2.08 (4.17), Stab 0.87
    (1.08). `-mcSwingTest` timed the real animations to 90 %: 1.02, 1.87, 0.78 s, which matches.
  - **Knockback:** the weapon's `advKnockback`. Minecraft's hits now also knock the enemy back by
    the held weapon's value (`EnemyKnockback.Knockback`, as `WeaponCollision` does).
- Sent in the Weapons table (protocol v24) with a version number that changes on any change.

**Weapon attributes work with Minecraft's attacks** (since 2026-10-21). Mycelium's attributes hook
in three ways:

| Hook | Attributes | With Minecraft's attacks |
|---|---|---|
| `Initialize`: the weapon's own numbers | Heavy, Light, Rapid, Massive, Tiny, Bouncy, Nutritious, Stickbuggy… | in the item's numbers (worked already) |
| `Equipped`/`Unequipped`: the spore's stats while held | Agile, Athletic, Beefy, Golden, Savvy, Sharp, Smart, Strong, Tenacious, Tough, Vigorous, Resourceful, Stinky, Vampiric | done by the weapon switching (worked already) |
| `Hit`: the weapon's `WeaponCollision.HitEnemy` | Critical, Repulsive, Nasty, Hazardous, Sticky, Feline, Vampiric, Fragile, Nimble, Enigmatic, Flourishing | **now:** every Minecraft hit goes through the held weapon |
| `StartAttack`/`StopAttack`: `PlayerAttack.StartedAttack`/`FinishedAttack` | Fragile, Nimble, Enigmatic, Flourishing; skill procs (Bursting, Erupting, Toxicating, Undergrown, Mycelic) roll in `WeaponStats` after a swing that hit | **now:** raised for every Minecraft swing |

- **Swings:** Minecraft counts melee attacks that go ahead (`MinecraftMixin.startAttack`) and
  sends the count (McState 0x94, protocol v25). For each new one, Mycelium raises
  `StartedAttack`, then `FinishedAttack` 0.35 s later, or at the next swing (`EnemyCombat`).
- **Fungal Might** (since 2026-10-22, your rule): it ends at the end of a swing that hit enemies.
  A swing that misses keeps it. Mycelium's own attack ends it in every swing, so that call
  (`PlayerAttack.ClearAllFungalMights`, which also turns off the skill's border and starts its
  real cooldown) is made when a swing that hit finishes, before `FinishedAttack`. A hit with no
  swing open (Better Combat lands its hits partway into a longer attack) opens one, so it ends
  0.35 s after that hit.
  - Its damage is in the weapon items while it's on (`WeaponBag.Compute` counts
    `fungalMightBonus`), so the hit that ends it is still boosted.
  - `-mcFungalTest` with `LINKTEST_FUNGAL=1` switches it on, then sends a swing that misses and
    one that hits.
- **Hits:** each one is put through the held weapon as `WeaponCollision.OnTriggerEnter` does:
  1. Minecraft's damage × `damageScale`, plus the weapon's Sentience and reflect bonuses, goes into
     `dmgDealt`;
  2. `HitEnemy` is raised, with the enemy's object;
  3. what's left in `dmgDealt` is the damage (Critical can double it; Repulsive makes it 0 and
     poisons);
  4. then Relentless Fury's frenzy heal (a quarter of it), the knockback, and the "Impact" sound.

  A hit with no swing open (an arrow) opens one.
- **Fragile and Enigmatic replace the held weapon themselves.** Fragile breaks into a Stick;
  Enigmatic turns into a random weapon with the same attributes. They do it by destroying it
  in the middle of a level. The bag keeps the new one in the old one's place: same id, same slot,
  its item and numbers updated.
- **Checked** with the fake Minecraft's hits (`LINKTEST_HIT=1`) and `-mcAttribute <Name>` (gives
  the held weapon that attribute):
  - Repulsive: 10 → 0 damage, and a Poison effect on the enemy.
  - Nimble: the spore's speed effect appeared 0.45 s after each hit (the swing's end) for 1.5 s.
  - Enigmatic (`LINKTEST_HOLD=1`, so the Stick was really held): it turned into a Bamboo
    Partisan, then an Avocado Flamberge, then a Rose Mace, all as #1.
- Not every attribute is tested; skill procs are random (15 %). "Sticky" isn't in Mycelium's
  attribute lists, so the game never gives it out.

**Minecraft side** (`MycWeapons`, on the integrated server; the client only reads and selects):
- **The item depends on the weapon type** (since 2026-10-21, your choice): Slash weapons are
  bread, Smash weapons beetroot, Stab weapons carrots. Below, "carrot" means any of them. A weapon
  with the wrong item for its type (made before this) is swapped in place, in the same slot.
- **One carrot per weapon,** marked with the weapon's id and a stamp of its numbers in custom data.
  Made, rewritten (when the stamp differs), or removed (weapons Mycelium doesn't have; copies) on
  any table change and twice a second.
  - **Name:** the weapon's name.
  - **Attribute modifiers** for the main hand, replacing Minecraft's base ones, so the tooltip
    shows totals:
    - attack damage = damage per swing ÷ `damageScale` (sent with the table), so a full-strength
      hit does that damage in Mycelium;
    - attack speed = swings per second.
  - **Description:** type, damage (×multiplier), swings per second (×modifier), knockback, then
    the attributes.
  - Not food: `FOOD` and `CONSUMABLE` are removed.
- **Throwing a weapon** (since 2026-10-21, your choice; before, a dropped carrot was removed and
  put back in the inventory):
  1. The item flies through Minecraft's world as usual: Minecraft's own throw speed and physics, on
     Mycelium's ground. Nobody can pick it up (`setNeverPickUp`, every server tick), and while it's
     in the world its weapon counts as present, so it isn't made again in the inventory.
  2. Every frame, the client sends each weapon item's id, position (interpolated) and "landed"
     (on the ground, not moving) to Mycelium: the Thrown block, protocol v27.
  3. Mycelium (`WeaponBag.FollowThrown`) takes the weapon out of the bag and puts the real one in
     the world as Mycelium drops one: tag and layer "Weapon", collider on, so it can be interacted
     with all the time. **Its pop-up moves with it.** `TooltipManager` places the pop-up once,
     where the thing is at that moment (Mycelium's lying weapons never move), so a thrown weapon's
     pop-up was left behind in the air (your report). While it flies, its pop-up is moved by as
     much as the weapon each frame, when the pop-up is the weapon's
     (`TooltipManager.currentParent`). A first fix kept the collider off until it landed, but that
     took too long, so it was replaced.
     - **The held one** leaves the hand with its attributes unequipped, and the hand is emptied
       (the invisible stand-in). It's marked as flying first, because emptying the hand removes
       whatever the spore holds unless it's flying; the first test lost the mace that way.
     - **One from the bag** is built from its prefab in the hand slot (for the hand's scale, which
       Mycelium's dropped weapons keep), let go, and its attributes added back a frame later (not
       Equipped).
  4. It's moved onto the item every frame, 0.25 blocks up, and spins as Mycelium's lying weapons do.
  5. Once the item has rested on the ground for 5 ticks (or after 10 s), Minecraft removes it. The
     weapon stays where it is as a pickup. Picking it up is an ordinary pickup: it comes back as a
     new weapon with its attributes.
- The carrot on the cursor in an open inventory counts as held.
- **Pickups:** the new carrot is selected once it's in the hotbar (within 3 s; a full hotbar
  means it isn't).
- **Death:** the carrots are taken out just before Mycelium's death kills the Minecraft player,
  so they don't drop. The run's end then empties the table.

**Checked:**
- `tools\FakeMycelium` + `gradlew linkTest`: 2 weapons, the UTF-8 name, damage, swings per
  second, attributes and damage scale all read back.
- The real Mycelium build with the fake Minecraft, `-mcPickupTest -mcSwingTest` and
  `LINKTEST_HAND=1`:
  - the run's Stick went in as #1 (10 damage, 0.98/s);
  - picking up a Cactus Mace kept the Stick and added the mace as #2 (×1.6 damage, ×1.15 speed,
    16 damage, 0.61/s);
  - following Minecraft's hand switched to the Stick and back to the mace, with its 3
    attributes, 2 of them the prefab's own.
- **The Minecraft side isn't tested in Minecraft.**

**The stat panel after a pickup** (since 2026-11-01). Near a weapon whose attributes change stats,
Mycelium's pop-up slides the stat panel in (`HUDStats.ShowStats`). It slides it away when a weapon
pop-up closes, but not when the pop-up closes because you picked the weapon up
(`DestroyTooltip(..., isFromInteracting)`). In Mycelium the weapon you let go of then lies there and
hides it when you walk away. Here it goes into the bag, so the panel stayed (your report).
`WeaponBag.HideStatsAfterPickup`: 1 s after a pickup where the old or new weapon changes stats,
`HUDStats.HideStats` (Mycelium's), unless another weapon's pop-up is open (that one hides it when it
closes). `-mcPickupTest <Type/Prefab>` picks up a chosen weapon and logs where the panel is.

### 5.0.5 Better Combat animations, timing and reach for weapons (since 2026-10-21)

**Presets:** our mod carries three Better Combat weapon presets as data
(`fabric/src/main/resources/data/myceliumcraft/weapon_attributes/`; a mod's `data` folder is
loaded like a built-in datapack). Every weapon item gets Better Combat's `bettercombat:preset_id`
component naming one (`MycWeapons.setPreset`; the component is looked up by name, so nothing
happens without Better Combat):
- **Slash** (`myceliumcraft:slash`): `one_handed_slash_horizontal_right`, then `..._left`
  (alternating combo), a 180° flat arc. Measured Mycelium Slash swings span about -90°..+94°.
- **Stab** (`myceliumcraft:stab`): `one_handed_stab`, a box straight ahead.
- **Smash** (`myceliumcraft:smash`): `two_handed_slam`, a box straight ahead, two-handed.
- Upswing (when in the attack the hit lands) is Better Combat's usual 0.5, to match its animations.

**Timing:** Better Combat's attack lasts Minecraft's attack cooldown, which comes from the item's
attack speed. That is already the weapon's real Mycelium swings per second (state length, the
spore's Speed level, the weapon's speed modifier; §5.0.4), so it follows the stats.

**Reach:** Better Combat's attack range is the player's entity interaction range when the held
item changes it. Each item sets it to the weapon's reach × 1.25 (your choice: ranges 25% bigger;
`REACH_SCALE` in `MycWeapons`), through the modifier `myceliumcraft:weapon_reach`.
- **Measured in Mycelium** (`WeaponBag.MeasureReach`), on the weapon object really in the spore's
  hand, the first frame it's held. The spore's attack state is stepped through in 40 steps
  (Play, Update(0), animation events off, all put back in the same frame). The weapon collider's
  farthest corner from the spore's middle, flat on the ground, is the reach, divided by the world
  scale.
- A weapon never held yet has no reach (0). Its item gets Minecraft's 3 blocks × 1.25 until you
  first select it.
- **Measured with `-mcReachTest`** (every prefab put in the hand as Equip builds it), in blocks:
  - Slash: Stick 1.65, Mandible Sickle 2.00, Razorvine 2.00, Obsidian Scimitar 1.83, Avocado
    Flamberge 2.82. Coal Axe and Eternal Torch 0.64: built differently (root scale 1, not ~31),
    so the test may not hold them as the game does. Not sure.
  - Smash: Femur Club 2.09, Cactus/Rose Mace 2.74, Geode Hammer 2.91, Bert's Club/The Smacker 3.08,
    THE LOG 3.73.
  - Stab: Carpal Sais 1.52, Opal Rapier 2.26, Stickbug Spear 2.93, Bamboo/Perfect Partisan 3.04.
  - The Stick really held in a level measured 1.65, the same as the test.

**Attack events:** Better Combat cancels Minecraft's `startAttack` for the weapons it animates and
runs its own attack. So Mycelium's attack events (`swingStarted`, which weapon attributes use) come
from Better Combat's public `ATTACK_START` event for those (`MycBetterCombat`). `MinecraftMixin`
skips them, so a swing isn't counted twice.

### 5.0.6 Weapon looks: our built-in resource pack (since 2026-10-22)

A Fabric mod's `assets/` folder is loaded like a resource pack that's always on, so the mod jar
carries our weapon looks. Each weapon has its own dummy 16x16 texture, even when two share an
item (both Slash weapons are bread).
- **The list:** `fabric/src/main/weapon-models.json`, Mycelium's weapon prefabs by type (19).
- **Made at build time** (`weaponResources` task in `build.gradle`, into
  `build/generated/weapon-resources`, packed into the jar):
  - a texture per weapon (`myceliumcraft:item/weapon/<key>`). Your hand-made ones are in
    `fabric/src/main/weapon-textures/<key>.png` (since 2026-10-22; all 19 by 2026-10-25). The rest are dummies drawn by the build: Slash a blade,
    Smash a club, Stab a spear, each weapon its own colour (hue spread over the type's weapons).
    The build logs which are hand-made. To change one, put a 16x16 PNG with that name there;
  - an item model per weapon (`parent: minecraft:item/handheld`);
  - `minecraft:items/bread.json`, `beetroot.json` and `carrot.json`: a `minecraft:select` on
    `minecraft:custom_model_data` (string 0), one case per weapon of that type. The fallback is
    the normal bread / beetroot / carrot, so plain food items and unknown weapons look as usual.
- **The key:** the prefab's name, not the weapon's name, because two prefabs (CactusMace,
  RoseMace) are both called "Rose Mace". Mycelium sends it in the Weapons table (0x100,
  protocol v30; entries are 0x120 bytes now). `MycWeapons.modelKey` makes it lower case, with every
  run of other characters as one "_" ("Bert's Club" → `bert_s_club`), as the build does. It's put
  on the item as `custom_model_data` strings[0].
- **Checked** with the fake Mycelium (`gradlew linkTest`): the keys "Stick" and "Bert's Club"
  arrive and become `stick` and `bert_s_club`. The 19 textures are 16x16 and all different.

### 5.0.7 Skills fire where you look (since 2026-10-25)

The spore faces where Minecraft's **body** faces. Minecraft's body only turns once the head is far
enough to the side, or toward the walk when strafing, so projectile skills sometimes went off to
the side (your report: Spineshot).
- **How Mycelium aims them:** Spineshot, Undergrowth and Unstable Puffball give their projectile
  the skill object's own rotation. Death Blossom appears in front of it, Mycotoxins behind it.
  The skill objects are children of the spore's `SkillLoadout`.
- **So** (`SkillAim.cs`): while Minecraft drives, `SkillLoadout` is turned to Minecraft's look
  (head) yaw, in Update and again in LateUpdate (after the body turns). The body and its
  animations keep Minecraft's body yaw. When Minecraft doesn't drive, it's turned back.
- Only the yaw: looking up or down doesn't tilt them (Mycelium's projectiles fly flat).
- Blitz still dashes the way the body faces (`rb.transform.forward`): a dash, not a projectile.

**Dashes: Blitz and the dodge roll move you** (since 2026-10-25, protocol v32). Both set
`PlayerController.activeDodge` and write the spore's velocity from a coroutine every frame (Blitz
40 units/s for 0.3 s, the roll `rollSpeed` 20). The driver sets the spore's velocity every physics
step to follow Minecraft, which cancelled them.
- **Seen** (`DashWatch.cs`): coroutines run after every Update and before LateUpdate. So the
  velocity is noted in Update, and a change by LateUpdate while `activeDodge` is on is Mycelium
  writing its dash.
- **Sent** in MycState (0x6C/0x70, Minecraft blocks/s), until 0.1 s after the last write.
- **Minecraft** (`MycDash.java`, from `MinecraftMixin`'s tick start): sets the player's
  horizontal movement to it each tick (blocks per tick), keeping the vertical. Its collisions
  apply, and the spore follows Minecraft as always.
- **Checked** with `-mcDashTest`, which starts Mycelium's own `Dodging` coroutine: the fake
  Minecraft got (14.3, 0) blocks/s for 0.36 s (20 units/s ÷ 1.4, facing east).
- **Checked** with the fake Minecraft strafing (`LINKTEST_STRAFE=1`: look 0°, body 45° off):
  "skills face 180 deg (your look), the body 135 deg: 45 deg apart".

### 5.0.8 Trophic Cascade: you vanish too (since 2026-10-26, protocol v33)

Trophic Cascade's vanish switches off every renderer on the spore while it jumps between enemies
(0.4 to 1.4 s), then back on (`TrophicCascade.Vanish`). Your Minecraft body in Mycelium is drawn
separately, so it stayed visible.
- **Seen** (`VanishWatch.cs`): all the spore's body renderers (not a held weapon's) have
  `enabled` off. Nothing else in Mycelium does that to the spore, and our own hiding of it uses
  `forceRenderingOff`.
- **Your body** (`McPlayerModel.SetVanished`): not drawn, shadow included, as the spore.
- **Minecraft** (MycState flag bit 3, `VANISHED`): from first person, the view goes to third
  person behind for as long as it lasts, then back (`updateViewOverride`, as for death and the
  emerge). Already in a third-person view: it stays.
- **Checked** with `-mcVanishTest` (every spore renderer off for 1 s, as Trophic Cascade does):
  vanished and back after 1.00 s, and the fake Minecraft saw the flag for 1.0 s.

**Living Cyclone: the same view change** (since 2026-11-01, protocol v34). It sets the held weapon's
`WeaponCollision.isCycloning` from the start of the skill until its third spin ends. While that's
on, MycState flag bit 4 (`CYCLONE`) is set, and Minecraft treats it as the vanish: from first
person, third person behind until it's over. The body isn't hidden.
- **Not a control takeover** (since 2026-11-01): Living Cyclone switches `canAct` off while it
  spins, which counted as Mycelium having the controls. After 0.3 s that hid the overlay (your
  hotbar, your report) and stopped the bridge. `MyceliumHasControls` now ignores "canAct off"
  while `isCycloning` is on.
- **No other attacks or skills while it spins** (since 2026-11-01, your rule):
  - **Skills:** Mycelium itself allows them. Living Cyclone calls `EnableController`, so
    `canUseSkill` is true, and the skill keys only check that (its `canAct` off stops only the
    dodge and interact). `MinecraftDriver.BlockSkillsWhileCycloning` keeps `canUseSkill` off
    while it spins; the skill's own `EndSkill` switches it back on.
  - **Attacks** (`MyceliumCraftClient.blockAttackWhileCycloning`, at each client tick's start and
    after each frame): Minecraft's Attack key is held up and its clicks dropped, so neither
    Minecraft's attack nor Better Combat's (which reads the key) can start. `MinecraftMixin`
    refuses `startAttack` meanwhile too. An attack key held through the spin needs pressing again
    afterwards. `-mcCycloneTest` sets
`isCycloning` for 2 s: the fake Minecraft saw the flag from 4.0 to 6.0 s.
- **Its animation** (since 2026-11-01, `MycSkillAnimation.java`): Better Combat's
  `two_handed_spin`, looped on your Minecraft player while the flag is on. It's played through
  Player Animation Library's public API, by reflection, so neither mod is changed and nothing
  happens without them:
  - At start, our own layer `myceliumcraft:skill` is registered (priority 2000, above Better
    Combat's). Its controller plays nothing by itself (its handler answers `PlayState.STOP`).
  - When the flag comes on, `RawAnimation.begin().thenLoop(two_handed_spin)` is triggered there;
    when it goes off, `stopTriggeredAnimation`.
  - Mycelium's picture of your body is posed by the same renderer, so it shows the spin too.
  - **Our own animation** (since 2026-11-01): `myceliumcraft:living_cyclone`, in the mod's
    built-in resource pack (`assets/myceliumcraft/player_animations/living_cyclone.json`,
    Emotecraft format, which the library reads).
    - It's written from scratch, not copied from Better Combat (its jar says "All Rights
      Reserved").
    - A 3-tick wind-up into a two-handed grip (Better Combat's: 10), then a full turn of the
      torso every 5 ticks (Better Combat's: about 6; 4 at first, slowed a little at your request).
    - Arms straight out at shoulder height, forward and angled to the right, and the held item
      tilted to point along them, out away from the body (your request, 2026-11-01).
    - **Grip:** tilting the held item turns it about a point about 2.5 px from the grip, so on its
      own it floated above the hand (your report). It's also shifted (y −3.15, z −0.3 px), the
      correction Better Combat's `dual_handed_stab` uses at the same tilt (−1.4).
    - **Flat faces up and down** (your request, 2026-11-02): the held item is also rolled a
      quarter turn (roll +1.5708; first −1.5708, flipped to the other face at your request), so
      the blade lies flat like a horizontal cut. With the roll the grip correction becomes x +1.1, y −2.0, z −0.5 px, the values Better Combat's own flat
      swings use at this tilt (`one_handed_stab`, `two_handed_slash_horizontal_*`: roll ±1.56,
      x ±1.1, y −2.0, z about −0.5).
    - It loops from the grip. `isLoop` with `returnTick` 4: the library restarts a loop one tick
      before `returnTick`, so this is tick 3.
    - Played with `thenPlay`, which keeps the file's own loop. Better Combat's `two_handed_spin`,
      looped, is only the fallback if ours isn't found.
    - **Checked** with the library's own loader (`UniversalAnimLoader.loadAnimations`, run outside
      the game): 8 ticks long, plays again, restarts from tick 3.0, with body, arms, legs and the
      held item.

### 5.1 Body size: Minecraft's player is shrunk to the spore's height

**The problem:** Minecraft's player is always 1.8 blocks tall. At scale 1.4 that's 2.52 units. The
spore's collision capsule is **1.70 units** tall in the game: the prefab says 1.35, but the game
sizes it up. Overhangs the spore fits under blocked Minecraft. One `scale` can't match both
walking speed and body size, so **speed is matched by `scale`, and size by shrinking Minecraft's
player** (decided 2026-10-17, your choice of option 1).

**How:**
- **Mycelium measures the body:** `MinecraftDriver.SporeHeight()` takes the height of the spore's
  own non-trigger colliders whose layer collides with the ground layers. Each one is logged; the
  12.6-unit trigger sphere is ignored. It sends `height / scale` in blocks (MycState 0x4C,
  protocol v4): 1.70 / 1.4 = **1.214 blocks**.
- **Minecraft applies it:** the Fabric mod sets the player's **vanilla `minecraft:scale` attribute**
  to `height / 1.8` (0.674) on the integrated server, which syncs it to the client.
- **Checked in Minecraft 26.3's code** (`javap`): scale is only used for the body size
  (`getDimensions`, which sets hitbox and eye height) and for riding. Step-up uses the separate
  `STEP_HEIGHT` attribute; jump strength, movement speed, gravity, safe fall distance and reach are
  separate too. So movement feels the same; only the body shrinks. Width shrinks with it: 0.6 →
  0.40 blocks = 0.57 units, narrower than the spore's 1.0.
- **Back to normal size:** the player goes back to scale 1 whenever Mycelium has no spore in a level,
  or Mycelium's heartbeat stops. So a world isn't left with a tiny player. The attribute is saved
  with the world, so if Minecraft quits while small, it stays small until the next link.
- **Switch:** `matchSporeSize=0` in `MyceliumCraft.cfg` turns it off.

**Offline check** (`tools/survey_check.py --player-scale`) at the 246 walked positions:

| Player scale | Fits |
|---|---|
| 1 (normal) | 232 / 246 |
| **0.674 (spore's measured height, now)** | **241 / 246** |
| 0.536 (the prefab's 1.35) | 246 / 246 |

The 5 left at 0.674: 3 are overhangs 1.5–1.6 units up. The spore's *rounded* capsule top squeezes
under them, but Minecraft's square box can't. 2 are at the edge of a ledge. They might be worth a
small allowance later. For now the measured real height is used, unchanged.

**Narrower than the spore:** Minecraft's player can fit through gaps the spore can't. The resync
(§4.1) moves it back onto the spore when that happens.

## 5.2 Camera: Mycelium's camera follows Minecraft's look (`CameraSync`)

**Sprint FOV and crouching** (since 2026-10-18):
- **What Minecraft sends:** two ratios, read from its own camera (`CameraAccessor`), interpolated
  like the position:
  - **FOV multiplier:** `fovModifier`. It covers sprinting, speed effects, drawing a bow and the
    spyglass, and already follows your "FOV Effects" setting.
  - **Eye height ÷ standing eye height:** `eyeHeight`, which Minecraft smooths over a few ticks
    when you crouch. About 0.78 when crouched.
- **FOV in Mycelium:** the FreeLook's lens FOV becomes Mycelium's own FOV (60°) times the
  multiplier, and goes back when the camera is released.
- **Eye height in Mycelium:** in first person the camera sits at the eyes times the ratio. In
  third person it moves down by the same amount, as Minecraft's does (its third-person camera turns
  around the eyes).
- **Checked with the fake Minecraft (`LINKTEST_FOVEYE=1`: ×1.15 FOV and ×0.78 eye from 4 to 7 s):**
  - FOV 60° → 69° → 60°;
  - first person: 1.513 → 1.180 → 1.513 above the spore;
  - third person in the hub: 2.114 → 1.792 → 2.125, with no collision push.
  - In the tutorial's tight spot, Mycelium's camera collision moved the third-person camera as well.

**Mycelium's camera:**
- A Cinemachine **FreeLook**, an orbit camera around the spore, on the object tagged `MainCamera`
  (`Prefabs/Camera/Main Camera.prefab`).
- Binding mode **World Space**, so its X axis is a world heading (−180..180, wraps). Its Y axis
  (0..1) moves it between three orbits:

  | Orbit | Height | Radius | Looks down |
  |---|---|---|---|
  | Low | −0.1 | 3 | −1.9° |
  | Middle | 1 | 3.5 | 15.9° |
  | High | 3 | 3 | 45.0° |

- No auto-recentering. It's turned by the legacy input axes "Mouse X"/"Mouse Y". Other cameras
  (reset, navigate, dramatic, cutscenes) blend in on top of it as they normally do.
- It has Cinemachine's collision add-on (`CinemachineCollider`), which pushes the camera out of
  walls and ground.
- Mycelium's own range is about −2° to 45° down, much less than Minecraft's −90 (straight up) to
  +90 (straight down).

**While Minecraft drives the spore** (phase Driving, every rendered frame, not the 50 Hz physics
step), the camera gets **Minecraft's full range**:
- **Heading:** X = Minecraft yaw + 180, the same heading the spore faces.
- **Smoothing:** Minecraft sends a new pitch once per Minecraft frame. Snapping to each one
  stutters against Mycelium's own frame rate (measured: 160 freeze-then-jump frames in 9 s with
  Minecraft at ~43 fps and Mycelium at 60). So the pitch is eased toward over `cameraSmoothing`
  seconds (default 0.05, an exponential ease; 0 = off). **The yaw is eased the same way in every
  view.** At first, third person passed Minecraft's yaw straight to Cinemachine. That was smooth
  while you played from Minecraft's window. With the input bridge, though, mouse movement reaches
  Minecraft in whichever Minecraft frame comes next, so Minecraft turns in uneven steps, and third
  person showed them as jitter. Measured, with a fake Minecraft at ~120 fps turning in 0/2/1 steps
  and Mycelium at 60 fps (`LINKTEST_TURN=1`, unevenness 0 = perfectly even):

  | View | Raw yaw | Eased yaw |
  |---|---|---|
  | Behind | 0.75 | 0.13 |
  | In front | 0.71 | 0.22 |
- **Wanted angle:** the smoothed Minecraft pitch −90..0..90 becomes a camera angle of
  −89..`cameraPitchOffset`..89. The default offset is 16, Mycelium's normal view, at "straight
  ahead". The halves above and below are stretched to fit, so straight up or down in Minecraft is
  straight up or down for the camera.
- **Swing:** the high orbit is raised from height/radius 3/3 (45°) to 4.18/0.74 (80°), at the same
  distance from the spore. The orbit curve now runs −1.9° → 15.9° → 80°. Y is set to the point on
  that curve with the wanted angle: 101 samples of `GetLocalPositionForCameraFromInput`,
  **interpolated** between neighbours, clamped to the curve. The first version rounded to the
  nearest sample, which made the camera hop 0.08 units every time Y ticked over by 0.01; that was
  the up/down jitter you saw.
- **Tilt:** after Cinemachine has placed the camera (`CinemachineCore.CameraUpdatedEvent`), the
  view is turned about its horizontal axis by whatever is still missing:
  - Looking up past the low orbit, the camera stays low behind the spore and tilts up, the way a
    third-person camera normally does, rather than going underground.
  - It also cancels the few degrees Mycelium's aim adds.
  - No tilt is applied while another camera is live or blending, such as a cutscene.
- **Mouse:** Mycelium's own mouse input to this camera is switched off in two places:
  - **The legacy axis names** ("Mouse X"/"Mouse Y") are blanked.
  - **The `CinemachineInputProvider`** on the camera feeds the new Input System's "Look" action
    (`<Mouse>/delta`) into both axes, whatever the axis names say. It's unplugged from the axes
    (`SetInputAxisProvider(…, null)`), and their built-up speed is cleared (`AxisState.Reset()`,
    which keeps the position). It's plugged back in with `UpdateInputAxisProvider()` afterwards.
  - **Why the provider matters:** before the input bridge, Mycelium never had the focus, so it
    never saw the mouse. With the bridge, your real mouse moved Mycelium's camera **as well as**
    Minecraft's look. The FreeLook moves its Y axis at the end of its update, for the next frame,
    so the orbit height ran ahead of the wanted angle (+0.02 looking down, −0.02 looking up). That
    made the camera hop when moving it up and down. Found in your own trace (`-mcCameraTrace`):
    the orbit height was off from the angle by up to 0.065. Reproduced with `-mcMouseTest`, and
    after unplugging:

    | | Orbit height evenness | Camera height evenness | Camera distance evenness | Height off from angle |
    |---|---|---|---|---|
    | Provider on | 0.50 | 0.40 | 0.54 | up to 0.035 |
    | Unplugged | 0.21 | 0.16 | 0.25 | up to 0.0016 |
- **Restore:** the orbits and mouse input go back to normal once Minecraft has stopped driving for
  **0.5 s**.
  - **Brief interruptions don't count:** teleports (WaitingForTeleport) and hiccups. A hiccup is
    up to 0.25 s of an unreadable state or "not in a world"; the driver then uses Minecraft's last
    good state.
  - **Why:** the camera used to be released at once. A knockback (an enemy hit) moves the spore,
    and the resync teleport after it released the camera for ~3 frames. A one-off "not in a world"
    from Minecraft did the same. Each showed Mycelium's third-person camera for a frame in first
    person (your log: 20:22:23 and 20:21:12).
  - **Check (`-mcHitTest`, plus `LINKTEST_HICCUP=1`):** right before each frame is drawn,
    `CameraSync.CheckFlash` counts first-person frames where the camera isn't at the eyes.
    Across 4 knockbacks with resync teleports and 7 hiccups: **0 flash frames** of 713, 0 camera
    releases, 0 drops to Idle.
- **Switch:** `syncCamera=0` in `MyceliumCraft.cfg` turns it all off.
- **F5 view** (protocol v5: McState 0x38 = Minecraft's `options.getCameraType().ordinal()`, which
  in 26.3 is 0 `FIRST_PERSON`, 1 `THIRD_PERSON_BACK`, 2 `THIRD_PERSON_FRONT`). Mycelium copies it:
  - **Third person behind:** everything above.
  - **Third person in front:** the orbit heading + 180° and the pitch mirrored. That's what
    Minecraft's own front view does (`setRotation(yRot + 180, -xRot)`). The camera sits in front of
    the spore, looking back at it.
  - **First person:** after Cinemachine, the camera is put at the spore's eyes, at 90% of its body
    height (Minecraft's 1.62 of 1.8). The tutorial spore's eye is 1.51 units above its position.
    - It looks at exactly Minecraft's pitch (no stretching) and heading, both eased over
      `cameraSmoothing`. Cinemachine's damping doesn't apply here.
    - The spore's renderers (4 in the tutorial) are switched to "shadow only", so the camera isn't
      inside its model, but you still see your shadow, as in Minecraft. They're restored when you
      leave first person.
    - The FreeLook keeps running underneath at Mycelium's normal view, so switching back is
      seamless.
  - **Switch:** `matchF5View=0` keeps the camera behind whatever Minecraft does.
- **Camera collision** (`CinemachineCollider` on Mycelium's main camera):
  - **Mycelium's settings:** it collides with the "Wall" layer only (ignoring tag
    `InvisibleWalls`), pulls the camera forward in front of a wall, and has
    `DampingWhenOccluded = 0`. So the camera snaps in front of a wall instantly, and eases back out
    over `Damping = 1` s.
  - **Problem:** with the full look range, the camera swings up past walls and through the
    tutorial's canopy far more often. At the tutorial start, the add-on pushed the camera on
    372 of 785 frames, up to 0.7 units, and each snap was a jump (up to 0.45 units in one frame).
    That's the jitter you saw when moving the camera up and down while looking down.
  - **Fix:** while Minecraft drives, `DampingWhenOccluded` is raised to `cameraCollisionSmoothing`
    (default 0.2 s), and restored afterwards. Measured with an uneven up/down look:

    | | Instant (Mycelium's) | 0.2 s ease-in |
    |---|---|---|
    | Biggest single-frame push change | 0.445 units | 0.097 |
    | Frames jumping over 0.05 units | 26 | 4 |
    | Biggest camera height jump | 0.225 | 0.098 |

    The trade-off: a wall's edge can be visible for a moment before the camera moves in front of it.
- **Camera shake** is kept in every view (`ShakeTap`).
  - **How Mycelium shakes:** level triggers (`AreaTrigger`), bosses and crabs call
    `CameraShakeManager.ShakeCamera(source)`, which fires a Cinemachine impulse. The
    `CinemachineImpulseListener` on the main FreeLook (gain 1, applied after the Noise stage, with a
    *secondary noise* reaction at amplitude and frequency ×100) adds a position and rotation
    "correction" to the camera.
  - **What broke it:** first person replaced the camera's position and rotation, which dropped the
    shake completely. In third person, the tilt was measured from the shaken view, so it cancelled
    the shake's up/down part.
  - **The tap:** `ShakeTap` is a Cinemachine extension added to the FreeLook *after* Mycelium's own,
    so at every stage it runs after the listener. It records the corrections when Aim is done and
    again when Noise is done; the difference is exactly the shake, secondary noise included, which
    can't be recomputed outside the listener.
  - **Third person:** the tilt is worked out from the unshaken view, so the shake stays on top.
  - **First person:** the shake's rotation is applied after the eye rotation, and its offset is
    moved from the orbit camera's space into the eye camera's.
  - **Checked** with `-mcShakeTest`, which fires the tutorial's own `EventTrigger 2` shake (3 s)
    every 3 s, in all three views. The trace measures the shake itself (`shakeAngle`) against how
    far the final view is from its unshaken aim (`offAim`):

    | View | Listener rotation, max / avg | Camera off its aim, max / avg | Calm |
    |---|---|---|---|
    | First person | 1.00 / 0.24° | 1.00 / 0.25° | ≤ 0.16° |
    | Behind | 0.81 / 0.22° | 0.81 / 0.25° | ≤ 0.15° |
    | In front | 1.00 / 0.32° | 0.97 / 0.34° | ≤ 0.14° |

    The "calm" value is the up/down smoothing lagging a moving look. The shake's position part is
    up to 0.068 units.

  **Checked** with the fake Minecraft going through the three views while sweeping its pitch:

  | View | Position | Angle | Heading |
  |---|---|---|---|
  | First person | camera 1.51 above the spore, at its eyes | want −73 / actual −73, want −36 / −36 | `diff 0` |
  | Behind | behind | matches | `diff 0` |
  | In front | `camSide=front` | e.g. pitch 37 → camera −27 (looking up at the spore) | `diff 180` |

  In all three, the view angle moved on every frame with 0 freeze-then-jump frames
  (`-mcCameraTrace`, the trace has a `mode` column).

**Checked** in "New Tutorial" with the fake Minecraft sweeping its pitch from −90 to 90. The status
line logs where the rendered camera really points (`Camera.main`, after everything):

| Minecraft pitch | Wanted (down°) | Actual (down°) | Y | Tilt |
|---|---|---|---|---|
| −75 | −71 | −72 | 0.00 | −64 |
| −39 | −30 | −30 | 0.00 | −21 |
| −3 | 13 | 13 | 0.47 | 5 |
| 33 | 43 | 43 | 0.69 | 8 |
| 69 | 72 | 72 | 0.90 | 1 |

The heading matched the spore's facing throughout (`diff 0`). The run ended before exactly ±90.
The first version, with Mycelium's own range only, is commit `63fafff`.

**Test runs stay out of the way (`-mcBackground`):** every test launch of Mycelium is minimized,
which puts it behind every window and makes Windows hand the focus back to the window you were
using. That's repeated for the first 10 s and again if Mycelium ever gets the focus, and in this
mode the input bridge is always off, so a test never uses your keyboard or mouse.
- **First version:** pushed the window to the back of the window stack. That did not take the
  focus away, and once Mycelium had it, with the bridge on, for 13 s.
- **Now:** Windows still hands Mycelium the focus at launch about every other run. The log says
  `Mycelium got the focus; minimizing to hand it back`, and the bridge stays off.

**Smoothness is measured, not eyeballed:**
- `-mcCameraTrace` writes one line per rendered frame to `work\build\camera_trace.csv`: pitch,
  wanted angle, Y, camera position, angle before tilt, tilt, target height.
- `-mcFps 60` caps Mycelium's frame rate, as in real play.
- `LINKTEST_FRAME_MS=23` makes the fake Minecraft run at another rate.
- `python -I tools/camera_trace_check.py <trace>` counts uneven steps, the biggest jumps and
  freeze-then-jump frames.

With Minecraft at ~43 fps and Mycelium at 60 fps:

| `cameraSmoothing` | Freeze-then-jump frames (9 s) | Frames the view moved |
|---|---|---|
| 0 | 160 | 383 / 543 |
| 0.05 | 0 | 498 / 498 |

Orbit Y now changes by about 0.0017 per frame instead of 0.01 hops. The camera's single biggest
jump (~0.6 units) is Cinemachine's collision add-on moving it sideways when the spore walks into a
wall, not the look.

## 5.3 Input bridge: play from Mycelium's window (SkyCraft's InputBridge)

**Idea:** Mycelium's window has the focus, and its keyboard and mouse are passed on to Minecraft.
Minecraft's window can sit in the background or minimised; later it can be hidden.

**When Minecraft plays from it** (`MycInput.flags ACTIVE`; otherwise a reason is sent and logged):
1. `inputBridge=1` in `MyceliumCraft.cfg`, otherwise reason 4.
2. Mycelium's window is focused (`Application.isFocused`), otherwise reason 1.
3. Minecraft is driving the spore (phase Driving), otherwise reason 3.
4. **Mycelium doesn't have the controls**, otherwise reason 2. It has them when any of these is true:
   - `GlobalData.isGamePaused`
   - **The menu between levels** ("CONTINUE your adventure or RETURN home?") is up or fading in:
     `EndOfLevelCanvasStuff`'s black or menu canvas is showing (since 2026-11-02). `LevelEnd`
     switches `canAct` off for it, but it can come back on under the menu: a hit's knockback
     ending, or an attack ending, calls `EnableController()`. Then the overlay came back and
     Minecraft moved the spore (your report). So the menu counts by itself.
   - `PlayerController.canAct` is off. `DisableController()` does this for the hub's
     Explore/Cancel prompt (`CharSelectManagerNew`), cutscenes, credits, level end, the run-start
     trigger, level-up, curio interactions and knockback.
   - Mycelium's "Player" input map is off (boss intros, the enemy-kill freeze).

While Mycelium has the controls, Minecraft's player gets no input, and Mycelium's own UI gets WASD
and Enter (it has the focus), so you can navigate its menus.

**Hold** (independent of the bridge): while Mycelium has the controls, the driver doesn't touch the
spore at all. Mycelium may move it, as cutscenes do. When Mycelium gives the controls back and the
spore is more than 0.5 units from Minecraft's player, Minecraft's player is teleported onto the
spore. This also fixes the spore following Minecraft through a Mycelium menu when you play from
Minecraft's window.

**Mycelium side (`InputBridge.cs`):**
- **Keys:** each frame it reads every key through Unity's Input System and maps it to an **SDL
  scancode**, the USB HID usage number that Minecraft 26.x's SDL input uses. Unity `Key` values
  are physical positions, so a key means the same physical key in both games. **P is never sent**:
  it's Mycelium's pause and menu. Escape is sent: it's Minecraft's pause menu (since 2026-10-18).
- **Mouse:** buttons (SDL 1 left, 2 middle, 3 right, 4/5 side), running totals of movement (screen
  pixels, Y down) and the wheel (notches).
- **Text:** a 64-character ring of typed text, for Minecraft chat.
- It's all published as *state* in `MycInput` (protocol v6, 0x300).

**Minecraft side (`MycInputBridge.java`, at the start of every frame via `Minecraft.runTick`):**
- **Keys:** compares the held keys with what it last applied and replays only changes into
  `KeyboardHandler.keyPress` (keycodes from `SDL_GetKeyFromScancode`, Shift/Ctrl/Alt as modifiers),
  as SkyCraft does.
- **Buttons:** into `MouseHandler.onButton`.
- **Look:** movement goes into `MouseHandler.onMove(handle, x, y, dx, dy)`. Minecraft's own code
  adds `dx, dy` to its look accumulator and turns the player with all its settings (sensitivity,
  smooth camera, spyglass, invert). The wheel goes into `onScroll`, text into `textInput` (only when
  a Minecraft screen is open).
- **Pretending to be focused**, only while active:
  - `Minecraft.isWindowActive()` and `Window.isFocused()` return true.
  - `InputConstants.isKeyDown` answers from the bridged keys.
  - `InputConstants.grabMouse/releaseMouse` do nothing, so Minecraft never takes the real cursor
    from Mycelium.
  - Minecraft "grabs" the mouse the normal way, which mouse look needs. Its own screens release
    and re-grab as usual.
  - Your `pauseOnLostFocus` option is turned off and put back afterwards.
  - Minecraft's "inactive" frame-rate limit is bypassed (`FramerateLimitTrackerMixin` returns your
    own frame-rate setting), and bridged input counts as input for its idle timer. Your log showed
    Minecraft dropping from 120 to 30 fps for stretches while bridged.
  - **Also whenever Mycelium is in a level showing Minecraft's overlay** (since 2026-10-21), bridge
    on or off. A minimized Minecraft caps itself at 10 fps. During the death fall and the respawn
    emerge, Mycelium has the controls, so the bridge is off. Your body is captured once per
    Minecraft frame, so those animations updated 10 times a second ("player model: sent 10 times",
    88 seconds of your log) and looked choppy.
- **Stopping:** when it stops being active, every held key and button is released.

**Which game owns which key** (`KeyOwnership.cs`, while Minecraft drives the spore;
`keyOwnership=0` turns it off). **The keys are set in Minecraft's own menu** (since 2026-10-17):
Options → Controls → Key Binds has a "Mycelium" section, registered by `MyceliumKeys.java` as
ordinary Minecraft key binds. Minecraft does the editing, saving (its `options.txt`), the red
conflict marks and "Reset"; the binds do nothing in Minecraft itself. Every frame Minecraft sends
what they're bound to (Bindings block, protocol v9). Mycelium then:
- sets each action's first keyboard/mouse binding to that key, and switches its other
  keyboard/mouse bindings off (gamepad bindings stay);
- re-does this within a frame if you change a bind while playing;
- keeps those keys and buttons out of the input bridge. **A key bound to a Mycelium action
  belongs to Mycelium only**, even if a Minecraft bind uses it too (Minecraft shows that clash in
  red). Everything else goes to Minecraft.

Key codes: Minecraft 26.3 stores keyboard binds as SDL scancodes (its W is 26, E is 8, Left Ctrl
224) and mouse binds as SDL buttons (attack 1, pick block 2, use 3), the same numbers the bridge
sends, so no translation is needed.

Until Minecraft sends binds (or with an older mod), Mycelium uses the same defaults. The defaults
(your choice of 2026-10-17):

| Key | Goes to | Does | Mycelium's old action there |
|---|---|---|---|
| Left click | Minecraft | attack | attack, curio (both unbound) |
| Right click | Minecraft | use | interact (moved to R) |
| R | **Mycelium** | interact | (none) |
| Shift | Minecraft | sneak | dodge roll (unbound) |
| Space | Minecraft | jump | show stats (moved to Tab) |
| E | Minecraft | inventory | skill 2 (unbound) |
| Q | Minecraft | drop item | skill 1 (unbound) |
| Ctrl | Minecraft | sprint | navigate camera (unbound) |
| Tab | **Mycelium** | show stats (hold) | reset camera (unbound); Minecraft's player list unbound |
| Esc | Minecraft | pause menu, closes Minecraft's screens; Mycelium freezes meanwhile | Mycelium's pause (its Escape binding is switched off) |
| P | **Mycelium** | pause and Mycelium's menu; Minecraft's world freezes meanwhile | |
| WASD, 1–9, F5, … | Minecraft | as in Minecraft | WASD still read by Mycelium, overridden by the driver |
| V | **Mycelium** | subspecies skill | (not in your list; unchanged) |
| Middle click | **Mycelium** | salvage | (not in your list; unchanged). Minecraft's pick block no longer gets it |
| (unbound) | | Mycelium's attack, curio, dodge roll, skill 1, skill 2, navigate camera, reset camera | bind them in Minecraft's menu |

**How:**
- **Mycelium:** Unity Input System *binding overrides* on its "Player" actions. About 20
  Mycelium scripts each create their own copy of its input asset; every copy with Player actions
  in use is found through `InputSystem.ListEnabledActions()` (11 in the tutorial) and changed.
  The overrides are removed when Minecraft stops driving, but only after 0.5 s without it.
  Re-sync teleports count as driving (since 2026-10-19: a 20 ms gap after a hit's re-sync let a
  click become Mycelium's attack). Mycelium's code is unchanged.
- **Cheat panel:** Mycelium's developer cheat panel (`TestingManager`, in `ManagerBundle`) reads
  the digits, K, L, Z, X and Minus directly, and digits are Minecraft's hotbar. It's switched off
  while Minecraft drives.
- **Minecraft:** the bridge doesn't send the keys bound to Mycelium actions (R, Tab, V and middle
  click by default).

**Checked** by holding E, Q, Space, Shift, Ctrl, Tab and R in Mycelium (`-mcHoldKeys`), and
logging which Mycelium actions register as pressed:
- `keyOwnership=0`: `[Dodge, HideStats, NavigateCamera, ResetCamera, ShowStats, Stat_Skill_1, Stat_Skill_2]`
- `keyOwnership=1`: `[HideStats, Interact, ShowStats]`, i.e. only R and Tab.

**Mycelium's hints show these keys** (since 2026-11-01, `KeyHints.cs`; protocol v35).
- **Where hints come from:** every hint Mycelium shows ("Press [R] to Open" on pop-ups, the skill
  bar, curios) is built from the current controller's `ColoredHint` in `InputManager`
  (`interactHint`, `statSkill1Hint`…), whose text is fixed in Mycelium's settings. So it said
  [RMB] with interact on R (your report).
- **While Minecraft drives,** the Keyboard controller's hints get the bound keys' names: letters
  as "R", keys by Unity's display name ("Tab", "Shift"), mouse buttons as LMB/MMB/RMB, the others as MB4, MB5… (your choice),
  "Unbound" for an unbound action. The skill bar is redrawn (`HUDControls.ChangeHUDControls`),
  and Mycelium's own texts are put back when Minecraft stops driving.
- **Re-applied** when the bindings change, or when Mycelium makes a new `InputManager` (scene
  changes).
- **Hints:** interact, show stats, subspecies skill, salvage, attack, dodge, skill 1, skill 2,
  navigate camera (`goalCameraHint`), reset camera (`orientationCameraHint`).
- **Attack:** while Mycelium's Attack is unbound, its hint shows Minecraft's own Attack key, which
  is what attacks. Minecraft sends that key as Bindings id 11.
- **Gamepad controllers' hints** aren't changed.
- **The skill bar's icons after a load** (since 2026-11-01, `SkillIconWatch.cs`). Mycelium fills
  the bar once, 0.2 s after its HUD starts (`HUDSkills.UpdateHUDIcons`), from the names of the
  SkillLoadout's children, by position.
  - The saved skills are put in one by one, 0.6 to 1.0 s into the scene. Each is added before the
    "NoSkill" it replaces is removed, so for a moment the loadout has 4 entries with the new
    slot-2 skill 4th.
  - A fill at that moment gives slot 2 "no skill", the likely cause of your missing slot-2 icon
    on the first load.
  - For 8 s after each scene load, a change of the loadout's names asks Mycelium to fill the bar
    again (its own `UpdateHUDIcons`). Both the loadout and the bar are logged ("skill icons: …").
  - `-mcNoIconRefresh` only logs.
- **The skill menu's assign keys** (since 2026-11-01): it puts a skill in slot 1 or 2 with its own
  keyboard actions, "Assign Y KB" and "Assign B KB" (UISub map, Q and E;
  `SkillMenuManagerUpdated`). Its hints already show Skill 1 / Skill 2, but the keys stayed Q and
  E (your report). `KeyOwnership` overrides them with the Skill 1 and Skill 2 keys, as it does the
  Player actions. An unbound skill key leaves Q or E, so a skill can still be assigned. The
  gamepad's assign buttons stay. Checked: with Skill 1 = F and Skill 2 = G, every input asset's
  log line said "skill menu: Assign Y KB=<Keyboard>/f, Assign B KB=<Keyboard>/g".
- **Checked** with the fake Minecraft's binds (R, Tab, V, middle, -, -, Left Shift, F, G, -, -,
  left as Minecraft's attack): "interactHint=[R] … attackHint=[LMB], dodgeHint=[Shift],
  statSkill1Hint=[F], statSkill2Hint=[G] …", and the keyboard's loot cache text "Press [R] to
  Open".

**Consequence:** Mycelium's own attack is unbound, so until combat (Phase 3) **nothing damages
Mycelium's enemies**. Minecraft's attack has no Mycelium target yet.

**Pausing** (your choice, 2026-10-18): Escape pauses with Minecraft's menu, P with Mycelium's.
Whichever game shows its menu, the other one waits:
- **Minecraft's pause menu:** Minecraft reports `PAUSED` (`Minecraft.isPaused()`). Mycelium sets
  its game speed to 0 (`Time.timeScale`, `MinecraftDriver.FreezeForMinecraftPause`), so enemies,
  physics and animations stop, without Mycelium's own menu. It's set again every frame in case
  Mycelium changes it meanwhile (its hit-stop does), and put back afterwards.
  - **Blur:** it also looks like Mycelium's own pause (`McPauseBlur`). A global Volume of ours uses
    Mycelium's "BlurVolume Profile" (depth-of-field focused 0.1 units away, plus a colour
    adjustment) with the settings of the Volume in Mycelium's PauseMenu prefab (global, priority 0,
    weight 1). Minecraft's overlay is drawn after it, so Minecraft's menu stays sharp. Checked with
    `-mcBlurCheck` (average difference between neighbouring pixels): 8–10 before, 2.1–2.5 during,
    about 10 after.
  - **Mycelium's UI blurs too** (your request), separately from the world:
    - **Why separately:** the world's blur is a depth-of-field effect. UI has no depth, so routed
      through it, it took the blur of whatever was behind it and vanished in front of the bright sky.
    - **How:** while blurred, Mycelium's root overlay canvases (9 in the hub; ours excluded) are
      drawn by a hidden camera of ours (far below the world, drawn by hand, HDR off) into a
      transparent picture. That's halved and given a Gaussian blur (`Resources/MyceliumCraftBlur.shader`,
      9 taps, across then down, twice: about 6 screen pixels), and shown with premultiplied alpha
      just under the Minecraft overlay. Everything is put back afterwards.
    - **Shrink-and-stretch looked pixelated** (your report): stretching a 1/4 picture back up shows
      steps. Roughness (`-mcBlurCheck`, how sharply brightness bends between neighbouring pixels):
      blurred HUD 0.38, blurred world 0.55, sharp HUD 5.9.
    - **Mycelium's render effects** (SSAO, Fog, RenderIgnoreFogLayer) are switched off just while
      that camera draws. Mycelium has a single renderer, and its fog painted over the picture's
      transparent background (the picture was 100% solid).
    - **Checked:** the UI picture is 5% non-transparent. Looking up, with sky behind the health bar,
      the corner is (155,186,169) with the blurred UI and (217,236,239) without, so it shows clearly
      over the sky.
- **Mycelium's pause menu:** Mycelium reports `PAUSED`. Minecraft freezes its world's ticking on the
  built-in server (`tickRateManager().setFrozen`, what `/tick freeze` does), and undoes only a
  freeze it made itself.
- **Key ownership:** Mycelium's Pause action keeps P and loses Escape while Minecraft drives the
  spore. While Mycelium's own menu is open, the bridge is off, so Escape there does whatever
  Mycelium does with it (not Minecraft's menu).

**While a Minecraft screen is open** (chat, inventory, pause menu):
- **Mycelium's controls are off:** every action map of Mycelium's input assets that's on gets
  switched off, again every frame, and the same maps come back on when the screen closes
  (`KeyOwnership.Silence`). Before this, typing "r", "v" or "p" in chat triggered Mycelium's
  interact, skill and pause.
- **Every key goes to Minecraft,** P and the Mycelium-bound keys included (chat needs Tab, for
  example).

**Cursor for Minecraft's screens:**
- **Mycelium:** while a Minecraft screen is open (inventory, chat, pause menu, crafting), Windows'
  own mouse pointer is unlocked and shown (your choice, 2026-10-18), so it can also leave the window.
  - It starts in the middle (Mycelium had it locked there), which is where Minecraft puts its
    cursor when a screen opens.
  - When the screen closes, the pointer goes back to how Mycelium had it. Mycelium locks and hides it
    once, when the player is created (`PlayerController`), and nothing re-locks it every frame.
  - The unlock is set again every frame anyway, in case.
  - **Hits don't recentre it** (since 2026-10-20). Turning the bridge off gives the pointer back to
    Mycelium locked, and Windows puts a locked pointer in the middle of the window. A hit while the
    inventory was open did that, and it came back freed but in the middle (your report). So:
    - a hit's knockback ("canAct off, knockback") doesn't take the controls from the bridge while
      a Minecraft screen is open;
    - re-sync teleports (`WaitingForTeleport`) count as driving for the bridge, as for key
      ownership.
  - **A hit's knockback keeps the mouse buttons** (since 2026-10-22, protocol v29). Mycelium sends
    a reason of its own for it (5, "knockback"). Minecraft keeps the bridge on (focus, grabbed
    mouse), lets go of the keys, and skips mouse movement, scroll and typing, because Mycelium
    moves the spore. The mouse buttons keep following what you hold. Letting go of them stopped
    Better Combat's hold-to-attack on every hit (your report). `-mcKnockTest` (with `-mcHurtTest`)
    adds Mycelium's own `PlayerController.Knockback` to each test hit.
  - **Test runs** (`-mcBackground`) never turn the bridge on. They log what it would do with your
    focus ("input bridge (test run, off): with focus it would be …"). `LINKTEST_SCREEN=1` keeps a
    Minecraft screen open from 4 to 10 s without pausing.
- **Sent as:** a fraction of the picture (MycInput `CURSOR`, 0x170/0x178).
- **Minecraft:** turns it into the same fraction of its window and gives it to its mouse handler as
  an absolute position, as a real mouse over its window would. Clicks, dragging items and the wheel
  then work as in Minecraft.

## 5.4 Overlay: Minecraft's hand, HUD and screens on top of Mycelium (SkyCraft's overlay)

While Mycelium shows a level (and `overlay=1`), Mycelium sets `OVERLAY` in its state:

- **Minecraft** skips drawing its own world (`LevelRendererMixin` cancels `LevelRenderer.render`,
  as SkyCraft does). What's left in its picture is the first-person hand and held item, the HUD
  (hotbar, hearts, hunger, XP, crosshair) and any open screen, on a transparent background.
  Minecraft's own window then shows only those over black.
- **Copy:** right after `GameRenderer.render()`, `MycOverlay.java` asks the graphics card to copy
  the picture into one of 3 staging buffers, and ships the newest finished copy into shared
  memory (usually a frame later). Adapted from SkyCraft's `FrameExporter` (MIT).
- **Hand-over:** a triple buffer (PROTOCOL.md, Overlay). Each side has its own slot and swaps it
  with a shared "middle" slot, so neither waits for the other and a picture is never half-written.
- **Mycelium** (`McOverlay.cs`) uploads the newest picture into a texture and draws it over the
  whole screen, above Mycelium's own UI, with a shader for premultiplied alpha
  (`Resources/MyceliumCraftOverlay.shader`). Premultiplied means the colors are already multiplied
  by their coverage, which is what Minecraft's drawing produces on a transparent background.
- **Hidden:**
  - while a Mycelium menu, pause or cutscene has the controls (2026-10-19):
    - its pause hides it at once;
    - other control takeovers only after 0.3 s;
    - a hit's knockback never does. Mycelium switches "can act" off until the "Hurt" animation is
      half done (from a few ms to about 0.4 s seen) and sets `isInvincible` for exactly that time;
      that's how it's recognised.
  - while Minecraft isn't driving;
  - if no new picture has come for 0.5 s;
  - while one of Mycelium's full-screen covers is up (since 2026-10-19; `ScreenCover`). That's its
    SceneLoader's black fade, loading, startup or death screen (canvas alpha above 0.01), or
    SceneLoader/SceneLoadingManager `isLoading`. The level-name title card doesn't count: it shows
    during play.
- **Size:** the picture is Minecraft's window size, stretched to Mycelium's window. It looks right
  when both windows have the same shape; the log warns if they don't.
  - **Minecraft's window follows Mycelium's** (since 2026-10-25, `MycWindowSize.java`; protocol
    v31). Mycelium sends its window size in pixels (MycState 0x64/0x68, `Screen.width/height`).
  - Once that size has stayed the same for 0.3 s, so it doesn't chase a drag, Minecraft's window is
    resized with `Window.setWindowed`. That takes window units, which differ from pixels on a
    scaled display, so the request is divided by Minecraft's current pixels per unit.
  - Capped at the overlay's 3840x2160, keeping the shape.
  - Not while Minecraft is full screen (its option): its size is left alone, and the log says so.
  - After asking, the log says what size Minecraft got. If Windows gave a different size (a
    window larger than the screen, say), it isn't asked again until Mycelium's size changes. If
    Minecraft got it, a later difference (you resize Minecraft's window yourself) is put back.
- **Better Combat in first person** (since 2026-10-21; `MycFirstPersonArms.java`):
  - **How Better Combat draws it:** its attack animations run through Player Animation Library
    (PAL). In first person, while an animation plays, PAL hides Minecraft's normal hand and puts
    your whole player into the world's list of things to draw, marked so only the arms and held
    items show.
  - **Why it was missing:** the overlay skips Minecraft's world, so those arms were never drawn.
  - **Now:** in place of the world, our mod draws just your own player, with the world's camera,
    into Minecraft's picture (the same steps Minecraft uses for the normal hand).
  - **Which player:** the one PAL put in the world's list if it's there. In your first test it never
    was during attacks, though PAL had hidden the hand. Both use PAL's own test
    (`ClientUtil.shouldBeFirstPersonPass`). So when that test says yes and the list has no player,
    our mod poses your player itself and marks it first-person as PAL does
    (`playerAnimLib$setFirstPersonPass`). Both are PAL's public methods, called by reflection.
    Better Combat and PAL aren't changed (AGENTS.md rule 7).
  - **Swing particles** (since 2026-10-21): Better Combat's swing trails are ordinary Minecraft
    particles (`SlashParticle`), drawn with the world. While the overlay is on,
    `QuadParticleGroupMixin` keeps only particles whose class is Better Combat's (`net.bettercombat.*`)
    when Minecraft gathers what to draw. Minecraft's own particles in its stand-in world (dust,
    crits) would float over Mycelium's world. `MycFirstPersonArms` draws the kept ones in the same
    pass as the arms, in every camera view. They're on top of Mycelium's picture, so a wall
    between you and a trail doesn't hide it.
  - **No fog:** they're drawn with Minecraft's "no fog" setting, as its own hand is
    (`GameRenderer.render3dHud`). The world fog washed out their colours (your report,
    2026-10-22); Mycelium draws the world, so Minecraft's fog doesn't belong on them.
  - **Depth:** 26.3's depth is reversed (0 = nothing drawn yet). Clearing to 1.0 hid the arms
    completely (your second test).
  - **Log** (every 10 s): frames the arms were drawn, how many came from the world's list and how
    many we posed, and the most things in that list.

### 5.4.1 Your Minecraft body instead of the spore, in third person

Following SkyCraft (its `AvatarExporter`). Minecraft doesn't send a picture of the player here;
it sends the **model itself**:
- **Posing:** Minecraft's own player renderer poses and animates the body: skin, second skin
  layer, armour, cape, walking, swinging, sneaking, the red hurt flash. It "submits" the parts to
  a collector; `MycPlayerModel.java` is that collector, and takes the parts as triangles instead
  of drawing them.
- **Sending:** every Minecraft frame, the triangles go out with their texture ids (Model block),
  and each texture once (Textures block).
- **Positions:** relative to the player's feet, in blocks, already turned to where the body faces.
- **Drawing:** `McPlayerModel.cs` builds a mesh from them. It stands at the spore's feet and maps
  like the world: (x, y, −z) × scale. So it's as tall as the spore, and it moves exactly with the
  spore.
- **In Mycelium's world:** it's a real object there, so Mycelium's walls hide it correctly.
- **Shader:** `Resources/MyceliumCraftModel.shader`. It keeps the skin's sharp pixels and is
  see-through where the skin is. It shades each face by which way it points (top brightest,
  bottom darkest), as Minecraft does.
- **Depth:** the shader also has the two passes Unity's renderer (URP) uses to fill its depth and
  normals textures before drawing ("DepthOnly", "DepthNormals").
  - **Why it needs them:** Mycelium's High Fidelity renderer runs SSAO from depth and normals, so
    the depth texture comes from those passes. Mycelium's Fog effect (`Depth.shadergraph`) then
    blends in up to 75% white fog by that depth.
  - **What went wrong without them:** the body was missing from the depth texture, so the fog used
    the distance of whatever was behind it. With sky behind, the body got washed out (you saw this
    on 2026-10-18).
  - **Checked** with `-mcModelCheck`, which reads the depth texture at the head's pixel:
    - with the passes: 3.2 units, the body's front face;
    - without them: 10.9 or 30.9 units, the wall behind.
- **Shadow:** the body casts its own shadow. Its shader has a "ShadowCaster" pass, which URP uses
  to draw the sun's shadow map; the skin's see-through pixels are left out, as in the colour pass.
- **First person:** Minecraft sends the body too, marked `FIRST_PERSON`. Mycelium draws it as
  shadow only: the camera is inside it, but its shadow is on the ground.
- **The spore** isn't drawn at all while the body is shown, in every view (`CameraSync.ReplaceSpore`;
  its renderers get `forceRenderingOff`), so it casts no shadow either. It's drawn as shadow only
  in first person only if there's no Minecraft body (for example `playerModel=0`).
  - **Kept up to date every frame** (`CameraSync.KeepHidden`, since 2026-10-20). Renderers newly
    under the spore are hidden too, and renderers that left it are drawn again. Picking up a
    weapon lets the old one go into the world and puts a new one in the hand
    (`WeaponInteraction`). With a list made once, the dropped weapon stayed invisible and the new
    one showed (your report).
  - **`-mcSwapTest`** (test aid) does such a swap 4 s into driving and logs which weapon is drawn.
  - **Checked** with `-mcModelCheck` in the hub, under its active "Day Light". The check works out
    where the shadow should land (from the middle of the body, along the light, down to the feet's
    height) and reads the screen brightness there:
    - with the body's shadow on: 54–60;
    - with it off: 89–98 (two camera views);
    - the spore had 0 renderers drawn throughout.
    - **First person, looking down:** 46 with the shadow, 80 without. The body itself isn't seen
      from the eyes: the reading with the shadow is shaded grass, not the body.
- **First person:** Minecraft sends an empty model, and the spore stays hidden as before.
- `playerModel=0` keeps the spore.

**Held items and blocks** (since 2026-10-19): Minecraft's renderer submits them as quads from its
item and block sheets (atlases). Each sprite (one picture on a sheet) a quad uses is sent once as a
texture of its own: its first animation frame, from `SpriteContents.originalImage`. The quad's
sheet coordinates become the sprite's 0..1 (`(u - u0) / (u1 - u0)`), and Minecraft's tints are kept
(leather, potions, grass). This is a simpler form of SkyCraft's combined atlas. The texture table
holds 256 textures (protocol v15).

**Not yet:** enchantment glint, animated item textures (first frame only), items from mods that use
Fabric meshes, and Mycelium's lighting on the body (it doesn't receive shadows either).

**Checked** with the fake Minecraft's test model (`LINKTEST_MODEL=1`: a 0.6 × 1.2 × 0.3 block body
with a nose on its facing side):
- **Size:** it arrived 0.84 × 1.68 × 0.42 units around the spore's feet, which is 1.4 × the block size.
- **Facing:** the nose came out on the side that matches Minecraft's facing.
- **Views:** shown in both third-person views and hidden in first person, with the spore drawn
  as shadow only whenever the body shows.
- **Updates:** 60 a second.

**Checked** with the fake Minecraft's test picture (`LINKTEST_OVERLAY=1`: an opaque red box, a
half-transparent white box, the rest transparent) and `-mcOverlayCheck`, which reads back Mycelium's
finished screen with the overlay on and off:
- red box (255,0,0)
- half white box 128–129, the right blend over Mycelium's dark background
- the empty spot unchanged
- about 60 pictures a second, 0.03–0.08 ms each to upload at 640×360

## 5.9 Server stalls (found and fixed, 2026-10-21)

**Late hits come from Minecraft's integrated server stalling.** All hits on enemy stand-ins are
handled on that server.
- Your report: some enemies took damage a bit after you attacked.
- Minecraft's log had "Can't keep up! Running 2028 ms / 2721 ms behind" at 08:03:26 and
  08:03:45, while drawing went on at a steady 120 frames per second. So only the server thread
  froze.
- The same stalls explain a few under-strength hits (5.07, 4.57, 1.39 against 6 or 9). While the
  server is behind, its attack charge lags the one on your screen.
- **Just before:** a re-sync teleport ("standing on air - force-sending blocks below"), and a new
  ground grid.
- **Ruled out:** building collision shapes from the grid. A scratch program timed the mod's own
  code on a real dumped grid (128 × 128 cells, up to 4 pieces each): 1–4 µs per block, so 2 s
  would take about a million blocks.
- **Found** (`ServerStallWatch`, your next session, 13:27–13:28): 54 slow ticks of 0.26–1.6 s at
  the end of a level. All 23 stacks caught were in the same chain:
  1. `EnemyProxies.tick`, then `EnemyProxy.setSize`;
  2. the synced size changing, which calls `refreshDimensions`;
  3. Minecraft's `fudgePositionAfterSizeChange`, then `CollisionGetter.findFreePosition`: it
     gathers every collision shape around the entity (Mycelium's ground included) and merges them.

  Enemies' boxes wobble as they animate, so this ran over and over.
- **Fixed:** a stand-in doesn't get pushed out of blocks (`fudgePositionAfterSizeChange` does
  nothing). It's put exactly onto its enemy every tick and never moves by physics. Its size is
  only updated when it changes by more than 0.05 blocks (was 0.01).
- **`ServerStallWatch`** stays, as a diagnostic that changes nothing:
  - every server tick is timed;
  - while one has run 0.4 s (and again at 1.5 s), a watcher thread logs the server thread's
    stack ("server stall: tick N has run X ms so far; the server thread is in: …");
  - ticks of 250 ms or more are logged when they end.

## 6. Logs ("log, don't look")

| File | What |
|---|---|
| `work\build\MyceliumCraft.log` | Overwritten each launch. Startup checks (types found by reflection), config, link up/down, scene loads, ground scans (hits, layered columns, skipped surfaces, spikes, time), phase changes, teleports, and a status line every second: phase, Minecraft pos, `groundUnderMc`, teleport seq/ack, spore pos, target, error, velocity, resyncs, fps |
| `%USERPROFILE%\AppData\LocalLow\Overgrowth Games\MyceliumCraft\Player.log` | Unity's own log: the same lines prefixed `[MyceliumCraft]`, plus Mycelium's own messages and errors |
| Prism instance `minecraft\logs\latest.log` | Lines starting `MyceliumCraft:`: link state, ground grid versions, teleports, and a status line every second with `groundHere` |

## 7. Test stand-ins

Like SkyCraft's `fake_skyrim.py`:

- `tools\FakeMycelium`: a .NET console app built from the **same** link files as the Unity package.
  It publishes a known ground grid (a step from y 10 to y 10.5 at x = 0, one hole, and a tunnel)
  and a teleport. Then it maps Minecraft's walk back with the same `WorldMapping`.
- `fabric\gradlew linkTest`: runs the **real** Java link code outside Minecraft. It checks the
  heights and collision boxes it reads, confirms the teleport, then walks 10 east and 10 north.
- Run FakeMycelium first, then linkTest. They print "ground checks PASS" (Java) and "PASS" (C#).
- **`MyceliumCraft.exe -mcScene "New Tutorial" -mcSurvey`** also records every surface and
  ceiling within 220 units of the spawn to `work\build\survey.csv` (395,641 columns, 0.6 s), then
  quits. It's for checking ground models offline against walked positions from the logs, without
  rebuilding Unity.
- **`-mcDumpGrid`** writes each grid **exactly as sent to Minecraft** to `work\build\grid_vN.csv`.
  `-mcNoFill` turns the sliver fill off, for comparison. Then `python -I tools/grid_check.py
  <grid> <log> --player-scale 0.674 [--spore] [--from T --to T]` checks positions from a log
  against it:
  - Minecraft positions, which flag traps Minecraft got into.
  - With `--spore`, where the spore really stood. Those must all be open.

- **`-mcColumn "x,z;x,z"`** (Minecraft coordinates) logs every surface and underside the scan
  finds in those columns: what it hit (object, collider type, layer), both upward rays, and the
  pieces sent. This is how the thin rock edge was found.
- **`-mcColliders`** lists the solid colliders around the spore that have nothing visible drawn
  where they are (the hub has a group named `InvisibleColliders`, and a `StartRunBarrier`). The
  sizes are the axis-aligned boxes around them, which are bigger than the real shape for rotated walls.

  **Lesson:** `survey_check.py` checks a *model* of the scanner. The zero-thickness walkway bug was
  in the scanner itself and only showed up in the real grid, so check grid dumps too.
- **`MyceliumCraft.exe -mcScene "New Tutorial"`** skips the menus and loads that level directly.
  With `gradlew linkTest` (no FakeMycelium), this exercises the real ground scan, teleport and
  driving without clicking through the game. linkTest's ground checks FAIL then, which is
  expected: they're written for FakeMycelium's grid.

## 8. Phases (adapted from SkyCraft §12)

| # | Phase | Done when |
|---|---|---|
| 0 | **Link** | Walking in a flat Minecraft world moves the spore in Mycelium. ✔ (your test, 2026-10-17) |
| 1a | **Ground** | Mycelium's ground is in Minecraft's collision, and Minecraft drives X, Y and Z. ← *now* |
| 1b | **Camera and input** | Mycelium's camera follows Minecraft's look and F5 view (§5.2, tested by you). Input goes from Mycelium's window to Minecraft, so you play in one window (§5.3, built; your test next). |
| 2 | **Overlay** | Minecraft's hand, hotbar and inventory are drawn over Mycelium's frame (§5.4: picture built, cursor next). |
| 3 | **Combat** | Mycelium enemies become hittable stand-in entities in Minecraft. Hits carry over both ways. |
| 4+ | Blocks, persistence, polish | |

## 9. Things SkyCraft has that we deliberately leave out

- **e4mc and "Playing with friends":** online play. This project is single-player and offline only.
- **Mycelium's Steam code** (achievements, stats, overlay pause) stays exactly as Mycelium has it.
  The private build doesn't include `steam_appid.txt`, so Steam achievements probably won't
  initialise in it. Not tested.
