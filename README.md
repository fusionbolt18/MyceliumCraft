# MyceliumCraft

Play Mycelium as a Minecraft player. It uses the same approach as
[SkyCraft](https://github.com/chasmlol/SkyCraft):
- Neither game is rewritten.
- A Minecraft Fabric mod and a Mycelium add-on talk through shared memory.
- Minecraft controls the player, and Mycelium shows the world.

Single-player and offline only.

> **Status: Phase 0 (link).** Walking in Minecraft moves your spore in Mycelium. Nothing else yet.

## What works

- **Link (tested with stand-ins):** the Java link code and the C# link code find each other through
  shared memory and exchange positions. In the test, `tools\FakeMycelium` plus `fabric\gradlew linkTest`
  printed `PASS`: Minecraft walked 10 east and 10 north, and the spore target moved +10 X and +10 Z.
- **Fabric mod builds:** `dist\minecraft-mods\myceliumcraft-0.1.0.jar` plus `fabric-api-0.161.0+26.3.jar`.

## What doesn't work / not tested

- **In the real games: not tested.** Nobody has yet walked in Minecraft and watched the spore move.
  See "Testing Phase 0" below.
- The Mycelium build (`work\build\MyceliumCraft.exe`): see MODLOG for whether it finished.
- Height is Mycelium's own (the spore stays on Mycelium's ground). Jumping in Minecraft does
  nothing in Mycelium yet.
- No shared collision. Mycelium walls stop the spore while Minecraft keeps walking, so the spore
  re-anchors.
- Mycelium's camera doesn't follow Minecraft's look. Spore attacks, dodges and skills still use
  Mycelium's own keys, in Mycelium's window.
- Minecraft's window is visible, and you need it focused to walk.
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

## Testing Phase 0

See MODLOG.md's latest entry for the exact steps and what you should see.

## Layout

| Folder | |
|---|---|
| `fabric/` | Minecraft Fabric mod (Java) |
| `mycelium/` | Mycelium add-on, as a Unity package (C#) |
| `protocol/` | Shared-memory layout both sides follow |
| `tools/` | Build script and test stand-ins |
| `docs/DESIGN.md` | How it works |
| `reference/`, `work/`, `dist/` | Not tracked: SkyCraft clone, private Mycelium copy and build, built jars |
