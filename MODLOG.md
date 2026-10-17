# MODLOG

## 2026-10-17 Phase 0: link Minecraft's player to Mycelium's spore

**Changed:** new project. The files:
- `fabric/`: Fabric mod. It hooks `Minecraft.renderFrame` and writes the player's position every
  frame. Its build files are copied from SkyCraft.
- `mycelium/`: Unity package. `MinecraftDriver` starts itself, finds `PlayerController` by name
  and moves the spore's Rigidbody.
- `protocol/PROTOCOL.md`: the shared-memory layout.
- `tools/build-mycelium.ps1`: copies Mycelium into `work\` and builds `MyceliumCraft.exe`.
- `tools/FakeMycelium`: a test stand-in for Mycelium.
- `.gitignore` (a whitelist), `docs/DESIGN.md`, `README.md`.

**Why:** first proof that the SkyCraft approach works for Mycelium. Walking in Minecraft should
move the spore.

**Tested how:**
1. `fabric\gradlew dist` (JDK 25 from Prism).
2. `dotnet run --project tools\FakeMycelium`, then `fabric\gradlew linkTest`. This is a fake
   Minecraft walking 10 east, then 10 north.
3. `tools\build-mycelium.ps1`.
4. Started the real `work\build\MyceliumCraft.exe`, ran `gradlew linkTest` against it, and read
   `work\build\MyceliumCraft.log`.

**Result:**
- The Fabric jar builds. The first try failed because `minecraft.screen` is now
  `minecraft.gui.screen()` in 26.3; fixed.
- FakeMycelium ended with `final spore target=(15.00, 15.00); expected (15, 15) after linkTest -> PASS`.
- The Unity build succeeded: `build Succeeded: 0 errors`, 1 min 49 s the first time, 20 s after.
  `MyceliumCraft.Runtime.dll` is in the build.
- In the real exe, the log shows:
  - `PlayerController found=True rb=True animator=True GlobalData.isGamePaused=True`
  - `shared memory Local\MyceliumCraft_v1 created`
  - `Minecraft linked`, then e.g. `mc=(110.50, 64.00, 190.92) yaw=180 flags=5 epoch=1` while the fake walk ran
  - `Minecraft gone` when it stopped
  - About 60 fps in the main menu
  - Steam did not stop the build from starting.
- The first exe test showed Minecraft's position as `(0,0,0)` in menus. That was only a logging gap:
  it read Minecraft's data only while a spore existed. Fixed and re-tested.

**Still broken / not tested:**
- **The spore actually moving in a level: not tested.** I can't click through Mycelium's menus. The
  code that moves the spore has never run, only the link and the player lookup.
- **The real Minecraft with the mod: not tested.** I can't install into your Prism instance (rule 4),
  and the walk needs a player.
- SCALE = 1 unit per block is a guess.
- Height isn't driven. There's no shared collision and no camera or input bridge.
- Steam achievements in the private build: not tested, probably off.

**Next:**
- You run the test procedure below and send `work\build\MyceliumCraft.log`.
- Then tune SCALE from the `mcSpeed` and `vel` numbers in the log.
- Then Phase 1: export Mycelium's level collision to Minecraft.

### Test procedure

1. Copy the two jars into your Prism instance (you do this; I don't write to game installs):
   `copy dist\minecraft-mods\*.jar "C:\Users\Ryan\AppData\Roaming\PrismLauncher\instances\Mycelium 26.3\minecraft\mods\"`
2. Start "Mycelium 26.3" in Prism. Create a **Creative, Superflat** world and enter it.
3. Start Mycelium in a window:
   `work\build\MyceliumCraft.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720`
   Play until you control a spore in a level.
4. Put the windows side by side. Click the Minecraft window and walk with W/A/S/D.
5. **You should see:** the spore walks the same way, at about walking pace, and turns to face where
   Minecraft faces. Close Minecraft and the spore goes back to Mycelium's own keys.
6. **In `work\build\MyceliumCraft.log` you should see:**
   - `found PlayerController on ...`
   - `anchor (start): ...`
   - Status lines with `driving=True`, `error` below about 1, and `reanchors` staying at 0 in open
     ground
7. **In Minecraft's log** (Prism → Edit → Minecraft Log): `MyceliumCraft: linked to Mycelium`.
