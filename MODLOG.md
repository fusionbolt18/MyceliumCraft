# MODLOG

## 2026-10-17 Camera copies Minecraft's F5 view (protocol v5)

**Changed:**
- **Protocol v5:** McState 0x38 carries Minecraft's camera type. It's in `PROTOCOL.md`, both
  `Proto` files, `MycLink.writeMcState`, `MyceliumLink`'s `McState.CameraMode`, and
  `MyceliumCraftClient` (`options.getCameraType().ordinal()`).
- **`CameraSync.cs`:**
  - *Behind:* as before.
  - *In front:* heading + 180, pitch mirrored.
  - *First person:* camera at the spore's eyes looking exactly where Minecraft looks, with yaw and
    pitch eased, and the spore drawn as shadow only.
- **`MinecraftDriver.cs`:**
  - gives CameraSync the spore and its eye height (90% of body height)
  - the status line shows `view=`, `mcView=`, `camDist=`, `camSide=` and `camHeight=`
- **`McConfig.cs`:** new `matchF5View=1`.
- **`LinkTestMain.java`:** cycles first person (0–3.5 s), behind (3.5–7 s), in front (7 s on).
- DESIGN §5.2.
- Before this: committed the full-range smooth camera as `54b37f9`.

**Why:** your request. Minecraft is normally first person and Mycelium third person, so
Mycelium's camera should follow whichever F5 view Minecraft is in.

**Tested how:**
1. Checked Minecraft 26.3's `CameraType` order with `javap`: FIRST_PERSON, THIRD_PERSON_BACK,
   THIRD_PERSON_FRONT.
2. Ran `-mcScene "New Tutorial" -mcCameraTrace -mcFps 60` with `gradlew linkTest`, and read the
   log and trace.

**Result:**
- Log: `camera: first-person eye 1.51 units above the spore's position`, `Minecraft view third
  person behind -> first person`, `first person, 4 spore renderers drawn as shadow only`, then
  `-> third person behind`, `-> third person in front`.
- First person: `view yaw=90 down=-73 sporeYaw=90 (diff 0) camDist=1.51 ... camHeight=1.51`, with
  want −73.
- Front: `view yaw=-180 ... sporeYaw=0 (diff 180) camSide=front`.
- Behind: unchanged (`diff 0`).
- Trace: the view moved on every frame in all three views, with 0 freeze-then-jumps.

**Still broken / not tested:**
- **Not tested with real Minecraft.** In particular, whether pressing F5 there switches Mycelium,
  and how first person feels.
- In first person, the spore's own particle effects (they're renderers too) are also shadow only.
  Things it holds might not be, if they aren't children of the spore. Not checked.
- The front view uses the low orbit plus tilt when looking down in Minecraft, so the camera sits
  low and close (`camDist` 0.93 in the test, near a wall). It may feel cramped.
- Uncommitted. Revert point: `54b37f9`.

**Next:** you test. Then input from Mycelium's window.

### Test procedure

1. **Copy the jars:** this changed the Minecraft side too, so copy them over:
   `copy /Y dist\minecraft-mods\*.jar "C:\Users\Ryan\AppData\Roaming\PrismLauncher\instances\Mycelium 26.3\minecraft\mods\"`
2. Start Minecraft, then
   `work\build\MyceliumCraft.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720`
   and play into the tutorial.
3. **Press F5 in Minecraft** to cycle the views. **You should see** in Mycelium:
   - **First person** (Minecraft's default): you look out of the spore's eyes and don't see the
     spore, only its shadow.
   - **F5 once:** Mycelium's usual camera behind the spore.
   - **F5 twice:** the camera in front of the spore, looking at it.
4. **In `work\build\MyceliumCraft.log`:** `camera: Minecraft view ... -> ...` each time you press
   F5, and status lines with `view=0/1/2` matching `mcView=`.
5. `matchF5View=0` in `work\build\MyceliumCraft.cfg` keeps the camera behind.

## 2026-10-17 Camera: up/down jitter fixed

**Changed:**
- **`CameraSync.cs`:**
  - The orbit height is interpolated between the 101 curve samples instead of rounded to the
    nearest.
  - Minecraft's pitch is eased toward over `cameraSmoothing` seconds.
  - New per-frame trace, `-mcCameraTrace`.
- **`McConfig.cs`:** new `cameraSmoothing=0.05`.
- **`MinecraftDriver.cs`:** new `-mcFps <n>` test aid.
- **`LinkTestMain.java`:** new `LINKTEST_FRAME_MS` setting.
- **`tools/camera_trace_check.py` (new).**
- DESIGN §5.2.

**Why:** you reported the up/down camera movement as very jittery after the full-range change.

**Tested how:**
1. Recorded every frame (`-mcCameraTrace`) during the fake Minecraft's pitch sweep in "New Tutorial".
2. Analysed it with `camera_trace_check.py`.
3. Repeated with Mycelium capped at 60 fps, and with the fake Minecraft at 60 and ~43 fps.

**Result:**
- **Cause 1, the main one:** the orbit height was rounded to the nearest of 101 samples. The trace
  showed Y moving on only 78 of 6,042 frames, in 0.01 steps (up to 0.05), and the camera hopping
  about 0.08 units each time.
  - Now Y changes about 0.0017 per frame.
- **Cause 2:** the view angle only changed when Minecraft drew a new frame, then snapped. With
  Minecraft at ~43 fps and Mycelium at 60, that gave 160 freeze-then-jump frames in 9 s.
  - With `cameraSmoothing=0.05` there are 0, and the view moves on 498 of 498 frames.
- The one remaining big jump (~0.6 units) is Cinemachine's collision add-on moving the camera when
  the spore walks into a wall. It's not related to looking.

**Still broken / not tested:**
- **Not tested with real Minecraft and a real mouse.**
- Smoothing adds about 0.05 s of lag to up/down look. Set `cameraSmoothing=0` for none, or bigger
  for smoother.
- The full-range camera is still uncommitted. Revert point: `63fafff`.

**Next:** you test, and keep or revert.

### Test procedure

1. Only Mycelium changed, so no jar copy is needed. Start Minecraft, then
   `work\build\MyceliumCraft.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720`.
2. **You should see:** moving the mouse up and down moves the camera smoothly over the whole range,
   from overhead down to looking up at the sky.
3. **If it still stutters,** run it once with `-mcCameraTrace` added to the command, look around for
   a few seconds, close it, and tell me. `work\build\camera_trace.csv` lets me measure what you saw.
4. **To trade smoothness for lag,** change `cameraSmoothing=` in `work\build\MyceliumCraft.cfg`.
   It's already in your file.

## 2026-10-17 Camera: Minecraft's full look range

**Changed:**
- **`mycelium/Runtime/CameraSync.cs`:** while Minecraft drives the spore, it now:
  - raises Mycelium's high orbit to 80° down
  - picks the orbit position from Minecraft's pitch over the full −90..90 range
  - tilts the rendered camera afterwards to the exact wanted angle, including looking straight up
    from the low orbit
  - restores the orbits afterwards
- **`MinecraftDriver.cs`:** the status line shows `want down=` and `tilt=`.
- **`McConfig.cs`:** `cameraPitchOffset` now means "camera angle when Minecraft looks straight
  ahead". The default 16 is unchanged.
- **`LinkTestMain.java`:** the fake Minecraft sweeps pitch −90 → 90.
- DESIGN §5.2.
- Before this: committed the first camera sync as `63fafff`. Revert to it if you don't like this.

**Why:** you asked for Minecraft's whole camera range. Mycelium's camera only went from about −2°
to 45° down, so looking further up or down in Minecraft did nothing.

**Tested how:** `MyceliumCraft.exe -mcScene "New Tutorial"` with `gradlew linkTest` (pitch sweep),
reading `MyceliumCraft.log`.

**Result:**
- `High orbit raised from 3.00/3.00 to 4.18/0.74 (height/radius); orbit curve looks down
  -1.9..15.9..80.0 degrees; Minecraft pitch -90/0/90 -> camera -89/16/89 degrees`.
- The rendered camera's real angle matched the wanted one within 1° at every sample from −72° to
  72°, e.g. `y=0.00 want down=-71 tilt=-64 pitch=-75 view ... down=-72` and
  `y=0.90 want down=72 tilt=1 pitch=69 view ... down=72`.
- The heading still matched (`diff 0`).
- `camera: back to Mycelium's mouse and orbits` when driving stopped.

**Still broken / not tested:**
- **Not tested with real Minecraft and a real mouse:** how it feels, and how the collision add-on
  behaves near walls and ceilings with the higher orbit, for example in tunnels.
- The exact ends (±89°) weren't reached in the test run; the run ended at pitch 69.
- Looking up uses a tilt, not an orbit, so the spore drops out of view at the bottom of the screen.
  That's normal for a third-person camera.

**Next:** you test, and keep or revert (`63fafff`). Then input from Mycelium's window.

### Test procedure

1. Only Mycelium changed, so no jar copy is needed. Start Minecraft (Creative Superflat), then
   `work\build\MyceliumCraft.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720`
   and play into the tutorial.
2. **You should see:**
   - **Looking straight ahead** gives Mycelium's usual view.
   - **Looking down** swings the camera up behind and over the spore, until it looks almost
     straight down on it.
   - **Looking up** keeps the camera low behind the spore and tilts it up to the sky.
   - **Turning** still works as before.
3. **In `work\build\MyceliumCraft.log`:** status lines where `want down=` and the `view ... down=`
   number are within a degree or two.
4. To change the default view, edit `cameraPitchOffset=` in `work\build\MyceliumCraft.cfg`: 16 is
   Mycelium's normal view, 0 is level with Minecraft.

## 2026-10-17 Camera follows Minecraft's look

**Changed:**
- **`mycelium/Runtime/CameraSync.cs` (new):** drives Mycelium's main FreeLook camera from
  Minecraft's yaw and pitch while Minecraft drives the spore, with Mycelium's mouse input to that
  camera off, and restores it afterwards.
- **`MinecraftDriver.cs`:** calls it every frame. The status line now shows `camera=synced x= y=`
  and the rendered camera's real direction, `view yaw= down= sporeYaw= (diff)`.
- **`McConfig.cs`:** new `syncCamera=1` and `cameraPitchOffset=16`.
- **`MyceliumCraft.Runtime.asmdef`:** now references the Cinemachine package.
- DESIGN §5.2.
- Earlier the same day: committed the ground and body-size work as `51f42cb`, including your
  `STATUS-handoff.md` template.

**Why:** your request. Mycelium's camera stayed wherever Mycelium's mouse left it, so Minecraft's
"forward" could be any direction on screen.

**Tested how:** `MyceliumCraft.exe -mcScene "New Tutorial"` with `gradlew linkTest`, which looks
west (yaw −90), then north (180), at pitch 0. I read `MyceliumCraft.log`.

**Result:**
- Startup: `camera: found FreeLook on 'Main Camera' (binding WorldSpace); orbits look down
  -1.9/15.9/45.0 degrees`, then `camera: following Minecraft's look (... was 'Mouse X'/'Mouse Y')`.
- While driving: `camera=synced x=90 y=0.50 view yaw=90 down=10 sporeYaw=90 (diff 0)`, then
  `x=0 ... view yaw=0 down=12 sporeYaw=0 (diff 0)`. The rendered camera points where the spore
  faces. One sample showed `diff -7` as the spore hit a wall.
- When driving stopped: `camera: back to Mycelium's mouse`.
- Unity's log showed `error CS0246: ... 'CameraSync' could not be found` during the build. That was
  an early pass before the new file was imported; Unity reruns itself, and the final result was
  `build Succeeded: 0 errors`. The built DLL contains `CameraSync`.
- **My mistake:** I deleted `work\build\MyceliumCraft.cfg` to get a fresh template without looking
  at it first. Your last log shows it held only defaults (`scale=1.4`, `matchSporeSize=1`), so no
  setting was lost. It was regenerated with the new camera settings.

**Still broken / not tested:**
- **Not tested with real Minecraft and real mouse look**, so it's unknown how it feels: mouse
  sensitivity, the pitch range, whether the camera clips into walls.
- Vertical look only uses Minecraft pitch −18..29. Looking further up or down doesn't change the
  camera.
- Input still goes to Minecraft's own window. You still play with Minecraft focused.

**Next:** you test. Then input: play from Mycelium's window.

### Test procedure

1. Only Mycelium changed, so no jar copy is needed.
2. Start Minecraft (Creative Superflat), then
   `work\build\MyceliumCraft.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720`
   and play into the tutorial.
3. **You should see:**
   - With Minecraft focused, moving the mouse turns Mycelium's camera too. It stays behind the
     spore and looks where you look.
   - W moves the spore into the screen, A/D sideways.
   - Looking down a bit raises the camera into Mycelium's high view; looking up lowers it.
   - Close Minecraft and Mycelium's own mouse controls the camera again.
4. **In `work\build\MyceliumCraft.log`:**
   - `camera: found FreeLook ...`
   - `camera: following Minecraft's look ...`
   - status lines with `camera=synced` and `(diff 0)` or close to it
5. If the vertical feel is wrong, change `cameraPitchOffset=` in `work\build\MyceliumCraft.cfg`
   (no rebuild needed). Bigger means a higher camera at the same Minecraft pitch.

## 2026-10-17 Narrow passage: walkways were missing in Minecraft

**Changed:**
- **`GroundScanner.cs`:**
  - **The bug fix:** the upward "underside" ray now stops 0.1 short of the surface above, and a hit
    within 0.05 of a surface's top no longer counts as its underside.
  - **The fill:** gaps under a one-sided surface smaller than 2.5 × spore height (4.25 units) are now
    filled solid.
- **`MinecraftDriver.cs`:**
  - New resync when spore and Minecraft disagree by more than 1.5 units for 2 s.
  - The fill size is set from the measured spore height.
  - New test aids: `-mcDumpGrid` and `-mcNoFill`.
- **`tools/grid_check.py` (new):** checks log positions against a dumped grid.
- **`tools/survey_check.py`:** new `--fill-below` option, plus reports of one-way gap sizes and
  Minecraft positions.
- DESIGN §4.1, §5, §7.

**Why:** you got stuck near the end of the tutorial in a narrow passage. Your log (saved as
`work\user-test-scale.log`) showed:
- Minecraft pinned at x 60.20, y 91.59 from 93 s until you quit at 123 s.
- The spore 1.8 units *above* it at Mycelium y 130.0, with `error` around 2.
- At 90–93 s, Minecraft dropped 1.6 blocks while the spore stayed up.

**Tested how:** I read the survey at that spot and the game's actual grid (`-mcDumpGrid`). Then I
ran `grid_check.py` on all three of your logs, with and without the fix.

**Result:**
- **Cause:**
  - There, the walkway (`Terrain` mesh, y ≈ 130) sits just 1.8 units above the hidden terrain tile
    (128.2).
  - The game's upward ray hit the walkway's own back face and reported its underside equal to its
    top: a zero-thickness piece, e.g. `(92.85, 92.85)` in Minecraft blocks.
  - Minecraft makes no box for zero thickness, so **the walkway didn't exist in Minecraft**. You fell
    through it onto the tile, where the shrunken player (1.70 units) just fit under nothing.
  - My offline survey stopped its upward rays short, so it never showed this. I wrongly blamed only
    the gap, and my first fix (the fill) changed nothing in the real grid.
- **After the fix, tutorial scan:**
  - 7,670 one-sided pieces are now real ½-block slabs.
  - 2,545 slivers under them are filled.
- **Spore positions against the new real grid:** 0/188 (`phase1a` log) and 0/90 (latest log)
  closed. Every place the spore stood is open.
- **Where you were wedged:** the grid now has solid ground 91.59..92.85 where you dropped through
  at 90.2 s. Minecraft stays on top of the walkway.
- In the oldest log, 5/49 spore positions near spawn are flagged at a rock edge 0.69 blocks high,
  just over Minecraft's 0.6 step. That's the same before and after this change.

**Still broken / not tested:**
- **Not tested in real Minecraft** (the passage, and the new 2-second resync).
- Fill size 4.25 comes from one level. A real passage under a one-sided surface smaller than that
  would be closed off. In the tutorial the smallest walked one is 5.79.

**Next:** you test the passage. Then Phase 1b, camera and input.

### Test procedure

1. Only Mycelium changed this time. Your Minecraft jars from the previous step are still current,
   so no copy is needed.
2. Start Minecraft (Creative Superflat, not flying). Then start
   `work\build\MyceliumCraft.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720`
   and play through the tutorial to the narrow passage near the end.
3. **You should see:** you walk along the passage *on top* of the walkway, with the spore. If you
   ever get wedged anyway, within about 2 seconds Minecraft's player jumps back onto the spore.
4. **In `work\build\MyceliumCraft.log`:**
   - `ground scan ...` showing a few thousand `small gaps under one-sided surfaces filled` and
     thousands of `one-sided pieces made 0.5-block slabs`
   - in the passage, status lines with `groundUnderMc` within about 0.1 of Minecraft's y, and
     `error` under 0.5
   - if a resync happens: `teleport ... (spore and Minecraft's player X units apart for 2 s (one is
     wedged): mc=(…))`. That line says where; please send it.

## 2026-10-17 Body size: Minecraft's player shrunk to the spore's height (protocol v4)

**Changed:**
- **Protocol v4:** MycState 0x4C carries the wanted Minecraft player height in blocks. It's in
  `PROTOCOL.md`, both `Proto` files, `MyceliumLink.cs` and `MycLink.java`.
- **`MinecraftDriver.cs`:** `SporeHeight()` measures the spore's own colliders that collide with the
  ground layers and logs each one. It sends `height / scale`.
- **`McConfig.cs`:** new `matchSporeSize=1` (0 turns it off).
- **`MyceliumCraftClient.java`:** sets Minecraft's vanilla `minecraft:scale` attribute to
  `height / 1.8` on the integrated server. It goes back to 1 when Mycelium has no spore in a level
  or is gone. The status line now shows `scale=` and `height=`.
- **Tests and docs:** `tools/survey_check.py` has a new `--player-scale` option. The stand-ins check
  the height value. DESIGN §5.1.

**Why:** you chose option 1. Minecraft's player (2.52 units at scale 1.4) was nearly twice as tall
as Mycelium expects, so low overhangs the spore walks under blocked it.

**Tested how:**
1. Read Minecraft 26.3's code (`javap`) for what the scale attribute affects.
2. Offline check against the 246 walked positions.
3. FakeMycelium plus `gradlew linkTest`.
4. `-mcScene "New Tutorial"`, reading the spore collider lines in the log.

**Result:**
- In `LivingEntity`, `getScale()` is used only by `getDimensions` (hitbox, eye height) and the
  riding position. `maxUpStep()` reads `Attributes.STEP_HEIGHT`. Jump, speed, gravity, safe fall
  distance and reach are separate attributes, so movement shouldn't change.
- The real spore: `spore collider 'Spore' CapsuleCollider layer=Player ... (1.70 tall) -> counts`,
  and the 12.56-unit trigger sphere is ignored. So `Minecraft player height 1.214 blocks (scale 0.674)`.
  The prefab said 1.35; the game makes it bigger.
- Offline fits: 232/246 at normal size, **241/246 at 0.674**, 246/246 at 0.536 (the prefab size).
  The 5 left are 3 overhangs 1.5–1.6 units up, where the spore's rounded capsule top squeezes under
  but a box can't, and 2 at a ledge edge.
- Stand-ins: `ok wanted player height (blocks): got 0.964, expected 0.964`, ground checks PASS,
  FakeMycelium PASS.

**Still broken / not tested:**
- **The scale in real Minecraft: not tested.** That covers whether the player shrinks, whether it
  goes back to normal when Mycelium closes, and how jumping and step-up feel at that size.
- The player is now narrower than the spore (0.57 vs 1.0 units), so it can squeeze through gaps the
  spore can't. The resync should catch those. Not tested.
- If Minecraft quits while the player is small, the world stays small until the next link. The
  attribute is saved with the world.
- Your existing `work\build\MyceliumCraft.cfg` was written before this setting existed, so it doesn't
  list `matchSporeSize`. It's on by default. Add `matchSporeSize=0` to turn it off.

**Next:** you test. Then Phase 1b: camera and input, so you play in one window.

### Test procedure

1. Copy the new jars over the old ones. The protocol changed, so both sides must be the new build:
   `copy /Y dist\minecraft-mods\*.jar "C:\Users\Ryan\AppData\Roaming\PrismLauncher\instances\Mycelium 26.3\minecraft\mods\"`
2. Start Minecraft (Creative Superflat, not flying). Then start
   `work\build\MyceliumCraft.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720`
   and play into the tutorial.
3. **You should see:**
   - When the spore appears, Minecraft's player becomes about two-thirds its normal height. In
     third person (F5), it's visibly small.
   - Walking speed and jumping feel the same as before.
   - You fit under the low mushroom caps and through the tunnels.
   - When you go back to Mycelium's main menu or close Mycelium, Minecraft's player returns to
     normal size.
4. **In `work\build\MyceliumCraft.log`:**
   - `spore body: 1.70 units tall -> Minecraft player height 1.214 blocks (scale 0.674)`
   - status lines in tight spots with low `resyncs`
5. **In Minecraft's log:**
   - `MyceliumCraft: player scale set to 0.674 (height 1.214 blocks)`
   - status lines with `scale=0.674 height=1.214`
   - when Mycelium leaves the level, `player scale set to 1.000`

## 2026-10-17 Tunnels: layered ground (protocol v3)

**Changed:**
- **Protocol v3** (`protocol/PROTOCOL.md`, both `Proto` files): each grid cell now carries up to 4
  solid pieces (bottom, top) instead of one height. Mapping size is 1 MB.
- **`mycelium/Runtime/GroundScanner.cs` rewritten:**
  - It finds up to 10 surfaces going down, plus each piece's underside going up (back faces
    counted).
  - When no underside is found, a piece becomes a ½-block slab.
  - Above 4 pieces, the smallest gaps are merged first.
  - The "pick one surface per cell" fill is gone.
- **Minecraft side:** `fabric/.../GroundGrid.java` builds boxes per piece and has a new
  `groundAt(x, y, z)`. `MycLink.java` reads the new layout.
- **Teleport fix** (`MyceliumCraftClient.java`): it also resets the "previous position" Minecraft
  blends from, so the confirmation reports the new position. `MinecraftDriver.cs` also waits, up
  to 2 s, until Minecraft's position really is at the target.
- **New test aid:** `-mcSurvey` (`LevelSurvey.cs`) records the level's surfaces and ceilings to
  `work\build\survey.csv`. `tools/survey_check.py` (new) checks ground models against walked
  positions from the logs.
- Stand-in tests now include a tunnel.

**Why:** you couldn't get through the tutorial's tunnels. Your log (saved as
`work\user-test-phase1a.log`) also showed the first teleport confirmed with Minecraft still at its
old position, `mc=(47.50, 39.34, 110.94)`. A resync fixed it within 0.02 s.

**Tested how:**
1. Ran `-mcSurvey` on "New Tutorial".
2. Ran `tools/survey_check.py` against the 246 distinct spore positions in your two logs.
3. FakeMycelium plus `gradlew linkTest`.
4. `-mcScene "New Tutorial"` with `gradlew linkTest`, reading `MyceliumCraft.log`.

**Result:**
- **Cause, measured:**
  - 121 of the 246 walked spots have something overhead. Mostly it's
    `Terrain_(146.90 47.03 -324.77)`, a Unity terrain tile about 10 units above the walkway. Rays
    from below never hit it (not even back faces): it's solid only from above.
  - Headroom there is at least 4 units in 119 of 121 cases, so Minecraft's player fits.
  - One height per cell couldn't represent that.
- **Model scores** (player fits at walked spots):

  | Model | Fits |
  |---|---|
  | Top surface only | 99/246 |
  | Layered, top 4 surfaces only | 208/246 |
  | Layered, 10 surfaces merged to 4 (now) | **232/246** |

  The middle row was my first layered build. A real-level run showed `groundUnderMc=NaN` at a tree:
  6 surfaces (3 × `Leaves`, `Trunk`, terrain tile, floor), so 4 never reached the floor.
- **Stand-ins:** 13/13 ground checks PASS, including 4 tunnel checks, and FakeMycelium PASS.
  The teleport confirmed at `mc=(0.29, 10.50, 0.25)`, the new position.
- **Real level:**
  - Scan: `14080/16384 columns hit, 6900 layered, 415 slabs, 657 small gaps closed, 17 columns
    with 10+ surfaces`, 49,404 rays, 30.5 ms.
  - Teleport confirmed at the right position in 0.09 s.
  - Along the walked path `groundUnderMc=88.99`, including the tree spot that was solid before.
  - Error 0.01–0.03.
  - Going north, the ground model says solid exactly where Mycelium's own collision stopped the
    spore (z −266), so the two games agree there's a wall.

**Still broken / not tested:**
- **The tunnels in real Minecraft: not tested.** The offline check and the real-level scan say they
  should be open now.
- **14 of 246 walked spots still don't fit Minecraft's player.** 13 are low overhangs (mostly
  `pCone1`, 1.5–2.1 units up). The spore is 1.35 units tall; Minecraft's player is 2.52 at scale 1.4.
  That's an open design choice (DESIGN §5.1).
- One-way surfaces are ½-block slabs: you can't jump up through them from below.

**Next:** you test the tunnels. Then decide the body-size question.

### Test procedure

1. Copy the new jars over the old ones. The protocol changed, so **both** sides must be the new
   build:
   `copy /Y dist\minecraft-mods\*.jar "C:\Users\Ryan\AppData\Roaming\PrismLauncher\instances\Mycelium 26.3\minecraft\mods\"`
2. Start Minecraft (Creative Superflat, not flying). Then start
   `work\build\MyceliumCraft.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720`
   and play into the tutorial.
3. **You should see:** you can walk through the tunnels in Minecraft, and the spore follows. Walls
   still stop you. Some very low mushroom caps may still bump Minecraft's head.
4. **In `work\build\MyceliumCraft.log`:**
   - `ground scan v1: ... layered ... slabs ... small gaps closed`
   - `teleport ... confirmed by Minecraft after ... s: mc=(…)` showing the *new* position, about
     y 89 in the tutorial
   - status lines in the tunnels with `groundUnderMc` close to `mc=(…, y, …)`, and few `resyncs`
5. Send me the log either way. If a tunnel still blocks you, stand at the blocked spot for a few
   seconds first, so the log shows where.

## 2026-10-17 Phase 1a: Minecraft walks on Mycelium's ground

**Changed:**
- **Protocol v2** (`protocol/PROTOCOL.md`, both `Proto` files): adds a teleport request and
  confirmation, and a 128 × 128 ground grid at 0x1000. Mapping size is 0x20000.
- **Mycelium side:**
  - `GroundScanner.cs` (new): raycast grid, up to 4 surfaces per column, picks the walkable one.
  - `Link/WorldMapping.cs` (new) replaces `AnchorMapping.cs` (deleted). The mapping is now fixed.
  - `MinecraftDriver.cs` rewritten. It runs Idle, then WaitingForTeleport, then Driving, and
    resyncs when needed. It drives X, Y, Z and cancels Mycelium's gravity.
  - New test-only launch option `-mcScene <level>`.
- **Minecraft side:**
  - `link/GroundGrid.java` (new): the grid and its boxes, pure Java.
  - `world/MycCollision.java` (new): collision shapes per block.
  - `mixin/BlockCollisionsMixin.java` (new): SkyCraft's hook, registered for client and server.
  - `MyceliumCraftClient.java`: reads the grid, applies teleports on client and server, and confirms.
- **Test stand-ins:** `LinkTestMain.java` and `tools/FakeMycelium/Program.cs` now test grid,
  boxes, teleport and mapping.

**Why:** next step from DESIGN Phase 1. Minecraft physics should run against Mycelium's real
terrain: hills, walls, holes, jumping and falling.

**Tested how:**
1. `dotnet run --project tools\FakeMycelium` and `fabric\gradlew linkTest`.
2. Checked in Minecraft 26.3's own code (`javap`) that `BlockCollisions.computeNext` calls
   `CollisionContext.getCollisionShape(BlockState, CollisionGetter, BlockPos)`, the Mixin target.
   The check that skips positions only skips the extra ring around the player, never the blocks it
   overlaps.
3. `MyceliumCraft.exe -mcScene "New Tutorial"` with `gradlew linkTest` (a fake Minecraft with no
   physics), four runs, reading `MyceliumCraft.log`.

**Result:**
- Stand-ins: "ground checks PASS" with 7/7 checks (heights, hole, box counts, box top 0.5).
  FakeMycelium printed `teleport confirmed=True; final spore (14.35, 14.70, 13.65) ... -> PASS`.
- Real level, first run: scan `14336/16384 cells hit`, 10 ms. Teleport confirmed in 0.11 s.
  Tracking error 0.00–0.05, where Phase 0 was 0.35, so the feed-forward works. One resync when
  Mycelium blocked the spore.
- **Found and fixed a real bug:** the spore walked on level ground at Minecraft y 89.00, but the
  grid said 94–105 under it. The rays were taking the top surface, which is often terrain or
  canopy the spore walks *under* (`Terrain x3112, Blue Shroom x366, FatTree_LOD0 x203, Leaves x112`
  skipped). I tried two fixes:
  - Picking the surface *closest to the neighbour*: still wrong from x ≈ 77 onward, because the
    upper layer spread.
  - Taking the *highest surface at most 1 block above the walking height* (now in place): along
    the walked path `groundUnderMc=88.99` vs feet 89.00, with one exception at x 75.24 (96.91).
- After the fix: 6,404 cells "walk under something". 12 one-cell spikes (`Leaves x5, FatTree_LOD0 x5`,
  one invisible wall). 38,774 rays in 31 ms.
- The spore was blocked going north where the grid rises 2.2–2.7 blocks in one step. Minecraft
  can't climb that either, so the two games agree. The fake Minecraft has no physics, so it walked
  on and Mycelium resynced, as designed.

**Still broken / not tested:**
- **Real Minecraft with the new mod: not tested.** These are untested:
  - that the Mixin actually applies (it's checked against Minecraft's own code, but only Minecraft
    starting proves it)
  - that the player stands on the ground and doesn't fall through
  - that the server doesn't pull the player back after a teleport
- Jumping in Minecraft should now lift the spore. Not tested.
- The 12 spikes might snag Minecraft's player, ½ block wide. Not tested.
- Bridges and other places you can both walk on and walk under: only one surface per cell.
- Water: Mycelium water is counted as ground if the spore collides with it.

**Next:** you run the test below. Then Phase 1b: Mycelium's camera follows Minecraft's look, and
input goes from Mycelium's window to Minecraft, so you play in one window.

### Test procedure (Phase 1a)

1. Replace the jar in your Prism instance. The old one must go; they have the same name, so copy
   over it:
   `copy /Y dist\minecraft-mods\*.jar "C:\Users\Ryan\AppData\Roaming\PrismLauncher\instances\Mycelium 26.3\minecraft\mods\"`
2. Start "Mycelium 26.3" in Prism and enter a **Creative Superflat** world. Creative, so a fall
   through a hole can't kill you. Don't fly: press space twice quickly to stop flying if you are.
3. Start Mycelium in a window and play until you control the spore:
   `work\build\MyceliumCraft.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720`
4. **You should see:**
   - **Teleport:** within a second of the spore appearing, Minecraft's player jumps up to about y 89
     in the tutorial. It's standing on nothing visible, because Minecraft can't see Mycelium's
     ground; Mycelium draws it. It should **not** fall.
   - **Following the terrain:** walk with W/A/S/D in Minecraft. The spore follows, going up and
     down Mycelium's slopes, at about its normal speed.
   - **Walls:** walls and steep rocks stop you *in Minecraft* too.
   - **Jumping:** space in Minecraft makes the spore jump.
   - **Canopies:** you can walk under tree canopies.
5. **In `work\build\MyceliumCraft.log`:**
   - `ground scan v1: ...` (the numbers above)
   - `teleport ... confirmed by Minecraft`
   - `phase ... -> Driving`
   - status lines with `phase=Driving`, `groundUnderMc` within about 0.1 of `mc=(…, y, …)` while
     you're on the ground, and `error` below about 0.5
   - `resyncs` should stay low. Each one is a spot where Mycelium and Minecraft disagree; the line
     before it says where.
6. **In Minecraft's log:** `MyceliumCraft: ground grid v1`, `MyceliumCraft: teleport`, and no
   "moved wrongly" warnings.
7. If Minecraft won't start at all, the Mixin didn't apply. Send me the crash report or `latest.log`.

## 2026-10-17 Scale setting (1.4) and less lag

**Changed:**
- `mycelium/Runtime/McConfig.cs` (new) reads `MyceliumCraft.cfg` next to the exe, and writes it
  with defaults the first time.
- `MinecraftDriver.cs`:
  - uses that scale
  - adds Minecraft's own velocity to the pull toward the target. Engineers call this "feed-forward":
    the spore moves *with* Minecraft instead of only chasing it.
  - logs `sporeOwnSpeed`, which is Mycelium's own walk speed for the current spore.
  - the speed cap went from 30 to 40.

**Why:** your Phase 0 log (saved as `work\user-test-phase0.log`) showed two things:
- The spore moved 3.96 units/s while Minecraft walked 3.98 blocks/s. The spore's normal Mycelium
  speed is about 6 units/s (`moveSpeedBase: 6` in `PlayerBundle.prefab`), so it moved slower than
  in the normal game.
- It trailed Minecraft by a steady ~0.35 units (`error=0.35`).

**Tested how:** rebuilt and launched it to the main menu, then read the log.

**Result:** `config: wrote defaults to ...MyceliumCraft.cfg`, then `config: scale=1.4 Mycelium units
per block`.

**Still broken / not tested:**
- Not tested in a level. I expect `error` to drop well below 0.35 while walking, and `vel` to be
  about 1.4 × `mcSpeed`.
- 1.4 is still a judgement call. Edit `scale=` in `work\build\MyceliumCraft.cfg` and restart, no
  rebuild needed.

**Next:** Phase 1, Mycelium's ground in Minecraft.

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
