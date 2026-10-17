# MyceliumCraft

Play Mycelium as a Minecraft player. It uses the same approach as
[SkyCraft](https://github.com/chasmlol/SkyCraft):
- Neither game is rewritten.
- A Minecraft Fabric mod and a Mycelium add-on talk through shared memory.
- Minecraft controls the player, and Mycelium shows the world.

Single-player and offline only.

> **Status: Phase 1a (ground).** Minecraft's player walks on Mycelium's terrain and the spore follows
> it (you tested it, 2026-10-17). It's now layered, so tunnels and overhangs should be open; that fix
> isn't tested in Minecraft yet.

## What works

- **Walking moves the spore (Phase 0): tested in both games** (2026-10-17). Walking in Minecraft
  moved the spore in "New Tutorial" for 2.5 minutes. The spore moved at 3.96 units/s while
  Minecraft walked at 3.98 blocks/s.
- **Scale setting:** `scale=` in `work\build\MyceliumCraft.cfg`, default 1.4 Mycelium units per block.
  Tested: it's read at startup.
- **Walking on Mycelium's ground in Minecraft (Phase 1a): tested by you** in the tutorial. It
  worked as described, except the tunnels.
- **Layered ground (tunnels, overhangs, under trees): tested by you.** Tunnels got better. The
  narrow passage near the end of the tutorial still trapped you, because its walkway was missing
  in Minecraft (zero-thickness pieces). That's fixed but **not tested in Minecraft yet**. Against
  the game's actual grid, every position the spore stood in your last two logs is open (0/188,
  0/90 closed).
- **Stuck safety net (not tested in Minecraft):** if the spore and Minecraft's player disagree by
  more than 1.5 units for 2 seconds, Minecraft's player is moved back onto the spore.
- **Teleport handshake and tracking:** tested with a fake Minecraft. Teleport confirmed in under
  0.11 s, tracking error 0.00–0.05.
- **Link and ground protocol:** `tools\FakeMycelium` + `fabric\gradlew linkTest` both PASS.

## What doesn't work / not tested

- **Tunnels in real Minecraft: not tested** since the fix. See the newest MODLOG entry for the test.
- **Body size: not tested in Minecraft.** Minecraft's player is now shrunk to the spore's height
  (scale 0.674, 1.214 blocks) using Minecraft's own scale setting; `matchSporeSize=0` in
  `MyceliumCraft.cfg` turns it off. Offline, the player fits at 241 of 246 walked spots. The rest
  are 3 low overhangs where the spore's rounded top squeezes under, and 2 ledge edges
  (DESIGN §5.1).
- One-way surfaces (Unity terrain seen from below) are ½-block slabs: you can't jump up through them.
- Mycelium's camera doesn't follow Minecraft's look. You need two windows, with Minecraft focused.
- Spore attacks, dodges and skills still use Mycelium's own keys.
- Steam achievements probably don't work in the private build (not tested).

## Requirements

- Mycelium source at `C:\Users\Ryan\Documents\ClonedGithubRepos\Mycelium` and Unity **2022.3.0f1**
- Minecraft Java 26.3 with Fabric Loader 0.19.5 (the Prism instance "Mycelium 26.3")
- JDK 25 to build the Fabric mod (Prism's `java-runtime-epsilon` works)
- .NET SDK (for the `FakeMycelium` test tool only)

## Building

```bat
:: Minecraft side -> dist\minecraft-mods\
cd fabric
set JAVA_HOME=C:\Users\Ryan\AppData\Roaming\PrismLauncher\java\java-runtime-epsilon
gradlew dist

:: Mycelium side -> work\build\MyceliumCraft.exe
cd ..
powershell -ExecutionPolicy Bypass -File tools\build-mycelium.ps1
```

## Testing

See MODLOG.md's newest entry for the exact steps and what you should see.

- Test aid: `work\build\MyceliumCraft.exe -mcScene "New Tutorial"` skips the menus and loads that
  level directly. Add `-mcSurvey` to record the level's surfaces to `work\build\survey.csv` and quit.
  `python -I tools/survey_check.py work/build/survey.csv <logs>` then checks ground models offline.

## Layout

| Folder | |
|---|---|
| `fabric/` | Minecraft Fabric mod (Java) |
| `mycelium/` | Mycelium add-on, as a Unity package (C#) |
| `protocol/` | Shared-memory layout both sides follow |
| `tools/` | Build script and test stand-ins |
| `docs/DESIGN.md` | How it works |
| `reference/`, `work/`, `dist/` | Not tracked: SkyCraft clone, private Mycelium copy and build, built jars |
