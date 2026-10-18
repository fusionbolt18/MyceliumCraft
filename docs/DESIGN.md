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
  underside of the piece above. It stops **0.1 units short of the surface above**, and a hit
  within 0.05 of that surface counts as "none". Otherwise it hits a one-sided surface's own back
  face and reports "underside = top". Those zero-thickness pieces give Minecraft no box at all, so
  the narrow passage's walkway simply **wasn't there in Minecraft**: you fell through it onto the
  hidden terrain 1.8 units below and got wedged. In the tutorial scan, 7,670 pieces were affected.
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
  are physical positions, so a key means the same physical key in both games. **Escape and P are
  never sent**: they stay Mycelium's pause keys.
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
| Esc, P | **Mycelium** | pause | |
| WASD, 1–9, F5, … | Minecraft | as in Minecraft | WASD still read by Mycelium, overridden by the driver |
| V | **Mycelium** | subspecies skill | (not in your list; unchanged) |
| Middle click | **Mycelium** | salvage | (not in your list; unchanged). Minecraft's pick block no longer gets it |
| (unbound) | | Mycelium's attack, curio, dodge roll, skill 1, skill 2, navigate camera, reset camera | bind them in Minecraft's menu |

**How:**
- **Mycelium:** Unity Input System *binding overrides* on its "Player" actions. About 20
  Mycelium scripts each create their own copy of its input asset; every copy with Player actions
  in use is found through `InputSystem.ListEnabledActions()` (11 in the tutorial) and changed.
  The overrides are removed when Minecraft stops driving. Mycelium's code is unchanged.
- **Cheat panel:** Mycelium's developer cheat panel (`TestingManager`, in `ManagerBundle`) reads
  the digits, K, L, Z, X and Minus directly, and digits are Minecraft's hotbar. It's switched off
  while Minecraft drives.
- **Minecraft:** the bridge doesn't send the keys bound to Mycelium actions (R, Tab, V and middle
  click by default).

**Checked** by holding E, Q, Space, Shift, Ctrl, Tab and R in Mycelium (`-mcHoldKeys`), and
logging which Mycelium actions register as pressed:
- `keyOwnership=0`: `[Dodge, HideStats, NavigateCamera, ResetCamera, ShowStats, Stat_Skill_1, Stat_Skill_2]`
- `keyOwnership=1`: `[HideStats, Interact, ShowStats]`, i.e. only R and Tab.

**Consequence:** Mycelium's own attack is unbound, so until combat (Phase 3) **nothing damages
Mycelium's enemies**. Minecraft's attack has no Mycelium target yet.

**Not yet:** Minecraft's screens show in Mycelium (§5.4), but its cursor can't be used from
Mycelium yet, and Escape doesn't reach Minecraft (to close chat).

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
- **Hidden** while a Mycelium menu, pause or cutscene has the controls, while Minecraft isn't
  driving, and if no new picture has come for 0.5 s.
- **Size:** the picture is Minecraft's window size, stretched to Mycelium's window. It looks right
  when both windows have the same shape; the log warns if they don't.

**Checked** with the fake Minecraft's test picture (`LINKTEST_OVERLAY=1`: an opaque red box, a
half-transparent white box, the rest transparent) and `-mcOverlayCheck`, which reads back Mycelium's
finished screen with the overlay on and off:
- red box (255,0,0)
- half white box 128–129, the right blend over Mycelium's dark background
- the empty spot unchanged
- about 60 pictures a second, 0.03–0.08 ms each to upload at 640×360

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
