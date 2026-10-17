# MyceliumCraft: design

> Play Mycelium as the main game while *being* a Minecraft player. Same approach as
> [SkyCraft](https://github.com/chasmlol/SkyCraft) (Skyrim + Minecraft), with Mycelium in Skyrim's place.

Status: Phase 0 (link) · 2026-10-17

## 1. Core principle (taken from SkyCraft)

**Neither game is rewritten.** Minecraft runs its own movement, physics, inventory and combat.
Mycelium runs its own world, enemies, levels and saves. The two mods only **translate** between
them:

- Minecraft tells Mycelium *where the player is* (and later: what it hit, what to draw).
- Mycelium tells Minecraft *what the world is shaped like* (later) and *where the enemies are* (later).

**Minecraft is authoritative for the player** (AGENTS.md: "Which game is authoritative: B").

## 2. The two halves

```
┌────────── MyceliumCraft.exe (Unity 2022.3, Mono) ──────────┐        ┌──────── javaw.exe (Minecraft 26.3 + Fabric) ────────┐
│  dev.myceliumcraft package (mycelium/)                      │        │  myceliumcraft Fabric mod (fabric/)                 │
│                                                             │        │                                                     │
│  MinecraftDriver ◀── McState: feet pos, yaw, flags ─────────┼─────── │  MinecraftMixin → MyceliumCraftClient.afterFrame    │
│     moves the spore's Rigidbody                             │        │     (end of every rendered frame)                   │
│  MinecraftDriver ─── MycState: spore pos, yaw, flags ───────┼──────▶ │  logged only, for now                               │
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
   `work\build\MyceliumCraft.exe`.

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

## 4. Coordinate mapping (Phase 0)

Phase 0 doesn't know where Mycelium's level is relative to the Minecraft world. So it moves the
spore by **the same amount** Minecraft's player moves, starting from an *anchor*. The anchor is
both positions recorded at the same moment. It's taken when the link starts, when Minecraft enters
a new world, and when a new Mycelium scene spawns a player.

```
spore.x = anchorSpore.x + SCALE * (mc.x - anchorMc.x)
spore.z = anchorSpore.z - SCALE * (mc.z - anchorMc.z)     Minecraft +Z is south
spore.y = Mycelium's own gravity and ground (not driven yet)
spore.yaw = mc.yaw + 180
SCALE = 1 Mycelium unit per block (a guess; tune from the logs)
```

The spore is pulled toward that target with a velocity of `error × 12/s`, capped at 30 units/s.
That keeps Mycelium's own collision working. If a Mycelium wall holds the spore more than 6 units
behind, it re-anchors where it stands and logs `reanchor`. This is the Phase 0 stand-in for real
collision sharing.

## 5. Logs ("log, don't look")

| File | What |
|---|---|
| `work\build\MyceliumCraft.log` | Overwritten each launch. Startup checks (types found by reflection), link up/down, anchors, scene loads, and a status line every second: Minecraft pos, spore pos, target, error, velocity, re-anchor count, fps |
| `%USERPROFILE%\AppData\LocalLow\Overgrowth Games\MyceliumCraft\Player.log` | Unity's own log: the same lines prefixed `[MyceliumCraft]`, plus Mycelium's own messages and errors |
| Prism instance `minecraft\logs\latest.log` | Lines starting `MyceliumCraft:`: link state and a status line every second |

## 6. Test stand-ins

Like SkyCraft's `fake_skyrim.py`:

- `tools\FakeMycelium`: a .NET console app built from the **same** link files as the Unity package.
  It creates the mapping and applies the same `AnchorMapping`.
- `fabric\gradlew linkTest`: runs the **real** Java link code outside Minecraft. It walks a fake
  player 10 blocks east, then 10 blocks north.
- Run FakeMycelium first, then linkTest. FakeMycelium prints PASS when the spore target ends
  10 units +X and 10 units +Z from the start.

## 7. Phases (adapted from SkyCraft §12)

| # | Phase | Done when |
|---|---|---|
| 0 | **Link** | Walking in a flat Minecraft world moves the spore in Mycelium. ← *now* |
| 1 | **Walk Mycelium in Minecraft physics** | Mycelium's level collision is exported to Minecraft (SkyCraft's CollisionField). Minecraft drives Y too. Mycelium's camera follows Minecraft's look. Input goes from Mycelium's window to Minecraft. |
| 2 | **Overlay** | Minecraft's hand, hotbar and inventory are drawn over Mycelium's frame. |
| 3 | **Combat** | Mycelium enemies become hittable stand-in entities in Minecraft. Hits carry over both ways. |
| 4+ | Blocks, persistence, polish | |

## 8. Things SkyCraft has that we deliberately leave out

- **e4mc and "Playing with friends":** online play. This project is single-player and offline only.
- **Mycelium's Steam code** (achievements, stats, overlay pause) stays exactly as Mycelium has it.
  The private build doesn't include `steam_appid.txt`, so Steam achievements probably won't
  initialise in it. Not tested.
