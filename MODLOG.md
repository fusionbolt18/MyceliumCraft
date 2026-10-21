# MODLOG

## 2026-10-21 Better Combat's swing particles in the overlay

**Your request:** Better Combat's particle effects for weapon swings should be visible too.

**What was wrong:** they're ordinary Minecraft particles (`SlashParticle`), drawn with the world,
which the overlay skips.

**Changed:**
- **New: `QuadParticleGroupMixin.java`** (a mixin on Minecraft's particle code, not on Better
  Combat). While the overlay is on, only Better Combat's particles (`net.bettercombat.*`) are
  gathered for drawing. Minecraft's own (dust, crits) would float over Mycelium's world.
- **`MycFirstPersonArms.java`:** draws those particles in the same pass as the arms, in every
  camera view. No pass at all when there's nothing to draw. The 10-second log line now says in
  how many frames particles were drawn.
- DESIGN §5.4, README.

**Tested how:** built only. **Not tested.**

**Still broken / not tested:**
- **Not tested in Minecraft.**
- Particles are drawn on top of Mycelium's picture, so a wall between you and a trail won't hide it.

## 2026-10-21 Better Combat first person, third try: depth cleared the right way

**Your report:** still invisible.

**From your log:** this time the arms *were* drawn: 580 frames during your attacks, all from
Minecraft's world list (0 posed by us). So the first test's missing player was likely just no
attack in that run, and the real fault was in the drawing.

**Cause:** Minecraft 26.3 counts depth backwards: its depth buffer starts at 0 for "nothing drawn
yet" (`GameRenderer` clears it to 0). I cleared it to 1.0, which meant "something is already in
front of every pixel", so every pixel of the arms was thrown away.

**Changed:** `MycFirstPersonArms.java` clears depth to 0.

**Tested how:** built only. **Not tested.**

## 2026-10-21 Better Combat first person, second try: we pose the arms when PAL wants them

**Your report:** the arm and weapon became invisible during Better Combat's animations.

**From your log:** the arms were drawn only in 14 frames at startup. During your attacks
Minecraft's world list held 0 things, so the player PAL should have put there never arrived. Yet
PAL hid the hand, and it uses the same test for both. Why the player was missing isn't known.

**Changed (`MycFirstPersonArms.java`):**
- Asks PAL's own test (`ClientUtil.shouldBeFirstPersonPass`) whether to draw the arms.
- If the world's list has no player then, it poses your player itself and marks it first-person
  the way PAL does (`playerAnimLib$setFirstPersonPass`). These are PAL's public methods, called
  by reflection; PAL isn't changed.
- **Correction:** last time I wrote that Minecraft's draw list grew every frame while the world
  was skipped. That was wrong: Minecraft empties it itself at the start of every frame
  (`LevelRenderState.reset`). The emptying I added is removed.
- The 10-second log line says how many frames used the list and how many we posed.

**Tested how:** built only. **Not tested.**

## 2026-10-21 Better Combat: first-person attack animations in the overlay

**Your request:** get the overlay working with Better Combat's animations in first person (third
person already worked).

**What was wrong:** Better Combat animates through Player Animation Library (PAL). In first person,
during an animation, PAL hides Minecraft's normal hand and draws your arms as part of the world
instead. The overlay skips Minecraft's world, so nothing was drawn.

**Changed:**
- **New: `MycFirstPersonArms.java`.** In place of the world, it draws only your own player from
  Minecraft's list of things to draw, with the world's camera, into the overlay picture. It's
  only in that list when PAL wants it drawn.
- **`LevelRendererMixin.java`:** calls it while the overlay is on.
- Minecraft's draw list is emptied every frame. (Wrong: it never grew; see the entry above.)
- Better Combat and PAL are untouched (only read with `javap` to see how they work).
- DESIGN §5.4, README.

**Tested how:** built only. **Not tested**: the fake Minecraft can't run Better Combat.

**Still broken / not tested:**
- **Not tested in Minecraft.**
- Not sure the arms are lit exactly as in a normal world. They use the world's lighting setup.

## 2026-10-21 A thrown weapon's pop-up moves with it (it stays interactable)

**Your note:** it took far too long for the weapon to become interactable again, so make the
pop-up move with the weapon instead.

**Changed:**
- **`WeaponBag.cs`:**
  - A thrown weapon's collider is on again from the start, so it's always interactable.
  - New: `MovePopUp`. While it flies, if Mycelium's open pop-up is this weapon's
    (`TooltipManager.currentParent`), the pop-up (`currentTooltip`) moves by as much as the weapon
    each frame.
  - The "lies at" line says for how many frames the pop-up was moved, and where it ended.
- **`LinkTestMain.java`:** `LINKTEST_THROW` now throws where the fake player faces (east before).
  Mycelium only offers things in a 90° cone in front of the spore.
- DESIGN §5.0.4.

**Tested how:** the real build with the fake Minecraft throwing the held Stick forward
(`LINKTEST_HOLD=1`, `LINKTEST_THROW=1`), using `-mcInteractWatch`.

**Result:**
- Mycelium offered the flying Stick at once (its pop-up opened).
- The pop-up moved with it for 45 frames and ended on the landed weapon: both at (4.4, 0.6, 281.0).

**Dead ends on the way:**
1. Earlier throws went east, outside the spore's 90° cone, so no pop-up ever opened.
2. A run after `-mcPickupTest` kept Mycelium's finder stuck on the picked-up mace. The test aid
   calls `Interact` directly, which skips the finder's cleanup.

**Still broken / not tested:**
- **Not tested in Minecraft.**

## 2026-10-21 A thrown weapon's pop-up no longer stays behind in the air

**Problem (your report):** when you threw a weapon, its pop-up appeared but didn't follow it.

**Cause:**
- Mycelium's `TooltipManager` places the pop-up once, where the thing is at that moment
  (`tooltipPosition = parent.transform.position`). Mycelium's lying weapons never move.
- A thrown weapon starts right next to you, so it was noticed (by its collider, through
  `SporeInteractableFinder`) and got its pop-up at once.

**Changed:**
- **`WeaponBag.cs`:** a thrown weapon's collider is off while it flies, and switched on when the
  item is removed after landing. So no pop-up and no pickup in flight; Mycelium notices it where it
  lies.
- **New test aid:** `-mcInteractWatch` logs whenever Mycelium's closest interactable thing changes.
- DESIGN §5.0.4.

**Tested how:** the real build with the fake Minecraft throwing the held mace
(`LINKTEST_HOLD=2`, `LINKTEST_THROW=2`), using `-mcPickupTest -mcInteractWatch`.

**Result:**
- The mace was noticed lying on the ground before the pickup.
- During the flight (18.2 to 20.2 s), it never became the closest interactable thing.
- It landed 5 units away, out of reach, so "nothing interactable near".

**Still broken / not tested:**
- **Not tested in Minecraft.**
- Not tested with the weapon landing within reach: that the pop-up appears where it lies. The fake
  throw always lands 3 blocks away.

## 2026-10-21 Throwing a weapon throws the real one (protocol v27)

**What you asked:** a weapon thrown from the inventory should fly into the world with the Minecraft
item's speed and path. The item is removed, leaving the weapon on the ground to pick up again.
Before, a thrown weapon item was removed at once and put back in the inventory.

**Changed:**
- **Protocol v27:** the Thrown block (after Weapons, 0x400): weapon id, flags (landed) and position
  for each weapon item in Minecraft's world.
- **`MycWeapons.java`:**
  - `thrownFrame` (client, every frame) writes it.
  - `init` (every server tick): no pickup, and the item is removed after 5 ticks resting on the
    ground (or 10 s).
  - `sync` counts weapons in the world as present, and no longer removes ground copies.
- **`WeaponBag.cs`:** `FollowThrown`, `ThrowOut`, `MakeWorldWeapon`, `AddAttributes`.
  - The weapon leaves the bag. The held one leaves the hand (attributes unequipped) and the hand
    is emptied; one from the bag is built from its prefab with its attributes.
  - It follows the item, and stays as a pickup.
  - Switching and emptying the hand never remove a flying weapon.
- **Stand-ins and test aids:** `LINKTEST_THROW=<id>` (the fake Minecraft throws that weapon in an
  arc from 5 s) and `-mcRepickTest` (picks up the nearest weapon lying in the world).
- DESIGN §5.0.4, PROTOCOL, README.

**Tested how:** the real build with the fake Minecraft in Daybreak Arboretum, with `-mcPickupTest`
(Stick #1 and a mace #2), `LINKTEST_HOLD=2`, and `LINKTEST_THROW=1` or `2`.

**Result:**
- **From the bag (the Stick):** "thrown … out of the bag", then "lies at (9.6, 0.6, 275.7), ready
  to pick up". The mace stayed in the hand.
- **The held mace:** it left the hand, the hand was emptied, and it lay at (9.5, 0.6, 276.0).
- **Picking it back up** (`-mcRepickTest`): layer Weapon, collider on, picked up through Mycelium's
  `Interact`. It came back as #3 with the same attributes and numbers (×1.60, ×1.15, Sharp
  Hazardous Nasty).

**Dead end on the way:** the first held throw lost the mace. Emptying the hand runs at once, inside
the throw, before the mace was marked as flying, so it was removed as an ordinary held weapon. Now
it's marked first.

**Still broken / not tested:**
- **The Minecraft side isn't tested in Minecraft:** the item flying and landing, its removal,
  not being put back in the inventory, and nobody picking it up.

## 2026-10-21 Mycelium's Speed stat changes your walking speed (protocol v26)

**Your question:** does the Speed stat change your speed? No, it didn't. While Minecraft drives, the
spore follows Minecraft's player, which walked at Minecraft's normal speed.

**Changed:**
- **Protocol v26:** MycState 0x60, the spore's walking speed as a multiple of its base speed.
- **`MinecraftDriver.cs`:** `SpeedFactor`, which is `PlayerController.moveSpeed` (base + the
  Speed level's bonus, changed by speed effects) ÷ `SporeAttributeRanges.moveSpeedBase`. Changes
  are logged at most twice a second.
- **`MyceliumCraftClient.java`:** `syncSpeed`, a movement-speed modifier on the player
  (`ADD_MULTIPLIED_TOTAL`, factor − 1), taken off outside Mycelium's levels.
- **Stand-ins:** both pass the factor (1.25).
- DESIGN §5.0.2.2, PROTOCOL, README.

**Tested how:**
1. `tools\FakeMycelium` + `gradlew linkTest`.
2. The real build with the fake Minecraft's hits, using `-mcAttribute Nimble`.

**Result:**
1. "Mycelium speed factor: got 1.25", PASS.
2. ×1.00 at the start (6.00 ÷ 6.00), then ×1.30 after a hit, fading back to ×1.

**Still broken / not tested:**
- **Not tested in Minecraft:** that you actually walk faster or slower.
- Minecraft widens its view a little when you're faster, as with a Speed potion, and the camera
  follows that.

## 2026-10-21 Late, weak hits at the end of a level: fixed (enemy stand-in resizing stalled the server)

**Problem (your report):**
- Enemies took damage a split second after you clicked, at the end of the level.
- Those hits did far less than your weapon should.

**Found:**
- `ServerStallWatch` logged 54 server ticks of 0.26–1.6 s (13:27:58 to 13:28:17).
- Minecraft's "Can't keep up! … 2160ms behind".
- All 23 captured stacks were the same chain: `EnemyProxies.tick`, then `EnemyProxy.setSize`, then
  the synced size changing (`onSyncedDataUpdated`, `refreshDimensions`), then Minecraft's
  `fudgePositionAfterSizeChange` and `findFreePosition`.
- That last step gathers and merges every collision shape around the entity, Mycelium's ground
  included.
- Enemies' boxes change as they animate, so it ran again and again. While the server is frozen,
  hits wait, and its attack charge lags your screen, so they come out scaled down.

**Changed:**
- **`EnemyProxy.java`:** `fudgePositionAfterSizeChange` does nothing (a stand-in is put exactly
  onto its enemy every tick and never moves by physics). Its size is only updated when it changes
  by more than 0.05 blocks (was 0.01).
- DESIGN §5.9.

**Tested how:** the jar builds. The stand-ins don't run Minecraft's server.

**Still broken / not tested:**
- **Not tested in Minecraft.** The proof is the "server stall" lines: there should be none at the
  end of a level now.

## 2026-10-21 Poisoned enemies flash green

**Changed:**
- **`HurtFlash.cs`:** a second tint material, green, from the same shader (multiply 0.72/1/0.72,
  add 0.03/0.12/0.03). It's used when Mycelium's `Poison` effect is on the enemy as its health
  drops. A flash already running swaps colour if the enemy is poisoned or cured meanwhile. The log
  counts the green flashes.
- **`MinecraftDriver.cs`:** `-mcAttribute` adds the attribute 1 s into driving, not 4 s, so the
  fake Minecraft is still hitting.
- DESIGN §5.0.3, README.

**Why:** you asked for green instead of red on poisoned enemies.

**Tested how:** the real build with the fake Minecraft's hits (`LINKTEST_HIT=1`,
`LINKTEST_HOLD=1`), using `-mcAttribute Repulsive` (its hits poison) in Daybreak Arboretum.

**Result:**
- The Repulsive hits did 0 damage, so no flash.
- The poison ticks after them flashed green: "1 … 5 … 23 flashes or re-flashes green (poisoned)".
- The hits before the attribute flashed red.

**Still broken / not tested:**
- **How the green looks on screen isn't checked.**

## 2026-10-21 Stuck-spore changes undone

**Your note:** you were mistaken about being stuck, so you asked to undo both changes from that
report.

**Undone:**
- the "stuck:" diagnosis line;
- the walk-through (it was already removed);
- the `-mcWedgeTest` test aid;
- `LINKTEST_SPEED`.

The resync rules are as before.

**Tested how:** both builds.

## 2026-10-21 Weapon attributes work with Minecraft's attacks (protocol v25)

**Survey:**
- Number attributes (Heavy, Rapid…) and stat attributes (Agile, Strong…) already worked.
- Attributes that react to a hit (`WeaponCollision.HitEnemy`: Critical, Repulsive, Nasty,
  Hazardous, Vampiric, Feline…) didn't. Minecraft's hits went straight to `EnemyTakeDamage`.
- Neither did ones that react to a swing (`PlayerAttack.StartedAttack`/`FinishedAttack`: Fragile,
  Nimble, Enigmatic, Flourishing) or skill procs (rolled after a swing that hit). Minecraft's swings
  raised no Mycelium events.

**Changed:**
- **Protocol v25:** McState 0x94, the number of melee attacks Minecraft has started.
- **`MinecraftMixin`, `MyceliumCraftClient`:** count each attack that goes ahead.
- **`EnemyCombat.cs`:**
  - For each new swing, raise Mycelium's `StartedAttack`, then `FinishedAttack` 0.35 s later.
  - Each hit goes through the held weapon as its own collision does: `dmgDealt` (with the
    Sentience and reflect bonuses), `HitEnemy` (attributes may change the damage), then the damage,
    Relentless Fury's heal, knockback, and the "Impact" sound.
  - A listener that fails is logged with where.
- **`WeaponBag.cs`:** a held weapon replaced in the middle of a level (Fragile broke it, Enigmatic
  changed it) keeps its bag entry and its slot.
- **New test aids:** `-mcAttribute <Name>` (gives the held weapon an attribute) and
  `LINKTEST_HOLD=<id>` (the fake Minecraft holds that weapon).
- DESIGN §5.0.4, PROTOCOL, README.

**Tested how:** the real build with the fake Minecraft's hits (`LINKTEST_HIT=1`) in Daybreak
Arboretum.

**Result:**
- **Repulsive:** damage 10 → 0, and a Poison effect on the enemy.
- **Nimble:** the spore's SpeedChange appeared 0.45 s after each hit and lasted 1.5 s.
- **Enigmatic** (held Stick): it became a Bamboo Partisan, then an Avocado Flamberge, then a Rose
  Mace, all as #1, with its numbers (24 damage with +50 %).

**Dead ends on the way:**
1. The first Nimble check looked every 2 s, but the effect lasts 1.5 s.
2. The first Enigmatic run was on the empty-hand stand-in. The fake Minecraft never selects the
   Stick, so it was put away. That gave a NullReferenceException in Enigmatic; with a real weapon
   there was none.

**Still broken / not tested:**
- **Not tested in Minecraft.**
- Not every attribute is tested; skill procs are random.
- Mycelium's hit-stop (its animation pause on a hit) isn't copied: it would pause the hidden spore.

## 2026-10-21 Slash weapons are bread, Smash weapons beetroot (Stab stays carrots)

**Changed:**
- **`MycWeapons.java`:** the item comes from the weapon's type. A weapon whose item is wrong for
  its type (a carrot made before this) is swapped in place, in the same slot, with its name and
  numbers. A weapon item is any of the three with the mod's id on it.
- DESIGN §5.0.4, README.

**Why:** you asked for bread for Slash and beetroot for Smash.

**Tested how:** the jar builds. The stand-ins don't make Minecraft items.

**Still broken / not tested:**
- **Not tested in Minecraft.**

## 2026-10-21 Death and respawn animations smooth with Minecraft minimized

**Problem (your report):** dying or respawning while Minecraft's window was minimized gave choppy
animations.

**Cause:**
- A minimized Minecraft caps itself at 10 fps. Your log has "player model: sent 10 times in the
  last second" for 88 seconds.
- `FramerateLimitTrackerMixin` only lifted that cap while the input bridge was on. During the
  death fall and the emerge, Mycelium has the controls, so the bridge is off.
- Your body is captured once per Minecraft frame, so it moved 10 times a second.

**Changed:**
- **`FramerateLimitTrackerMixin.java`:** your own frame-rate setting also applies whenever Mycelium
  is in a level showing Minecraft's overlay (`MycOverlay.active()`).
- DESIGN (input bridge).

**Tested how:** the jar builds. The stand-ins don't run Minecraft's renderer.

**Still broken / not tested:**
- **Not tested in Minecraft.** Not sure Minecraft doesn't skip other work while minimized. If the
  death or emerge is still choppy, the "player model: sent N times" lines will say how often the
  body was captured.

## 2026-10-21 Late hits: Minecraft's server stalls (diagnosis; not fixed yet)

**Problem (your report):** some enemies took damage a bit after you attacked, near the end of the
session.

**What the logs show:**
- Mycelium applies a hit the moment it reads it. `EnemyTakeDamage` has no delay either.
- Minecraft's log: "Can't keep up! Is the server overloaded? Running 2028ms or 40 ticks behind"
  (08:03:26) and "2721ms or 54 ticks behind" (08:03:45).
- The client's frame counter rose a steady 120 per second through both, so only the integrated
  server froze. Hits made then were handled up to ~2.7 s late.
- The under-strength hits in the same seconds (5.07 at 08:03:25, 1.39 at :38, 4.57 at :41) fit:
  the stalled server's attack charge lags your screen's.
- **Just before:** a re-sync teleport plus "standing on air - force-sending blocks below" (first
  stall), and a new ground grid, v27 (second).

**Ruled out:** collision shape building. A scratch program ran the mod's own shape code over a
real dumped grid (`-mcDumpGrid`, `work/build/grid_v1.csv`, git-ignored): 446,464 blocks in
1045 ms the first time and 235 ms cached, so 1–4 µs per block. A 2 s stall would take ~a million
blocks.

**Changed:**
- **New `ServerStallWatch.java`:** times every server tick. A tick still running at 0.4 s (and
  again at 1.5 s) gets the server thread's stack logged; ticks of 250 ms or more are logged when
  they end.
- DESIGN §5.9.

**Tested how:** the jar builds. The stand-ins don't run Minecraft's server, so the watch only
reports in real play.

**Next:** play until enemies take damage late again, then send `logs/latest.log`. The "server
stall" lines will say what the server was doing.

## 2026-10-21 A new run gives the Stick again (empty-hand fix)

**Problem (your report):** you started a run and didn't get the Stick.

**Cause (your log, 07:46):**
- In The Carcass the spore held no weapon (Mycelium removes it at a run's end), and Minecraft's
  hand was empty.
- The new empty-hand code made its invisible stand-in anyway.
- Mycelium's first level (`LevelGenerator`) only gives the Stick when the spore holds nothing, so
  it gave none: "new level, the spore's hand is still empty".

**Changed:**
- **`WeaponBag.cs`:** an empty Minecraft hand only puts away a weapon of yours that the spore holds.
- **`MinecraftDriver.cs`:** `-mcLoadTest` takes a scene name (the default is still the tutorial).
- DESIGN §5.0.4.

**Tested how:** the real build with the fake Minecraft (hand empty) using
`-mcScene "The Carcass" -mcLoadTest "Daybreak Arboretum"`: 4 s of driving in The Carcass, then
Mycelium's own scene change.

**Result:**
- No stand-in in The Carcass.
- Daybreak Arboretum: "'Stick' (#1) into the bag (the run's first weapon)".
- The fake Minecraft never selects it, so 3 s later the empty hand put it away, as designed.

**Still broken / not tested:**
- **Not tested in Minecraft:** that the Stick's carrot appears and gets selected at the run's start.

## 2026-10-20 Weapon switches: no one-frame flash; an empty hand really is empty

**Problems (your report):**
1. When switching weapons, the weapon's model showed for a frame.
2. Switching to an empty hand kept using the last weapon.
- Also seen in your log: while you switched, a carrot was rewritten every half second.

**Causes:**
1. The new weapon is built in a coroutine, after that frame's spore hiding, so it was drawn once.
2. An empty hand (Minecraft hand id 0) was simply ignored, so the last weapon, with its
   attributes and knockback, stayed.
- While a switch runs, the held weapon's stat attributes are off for a moment, so the other
  carrots' numbers wobbled and were rewritten.

**Changed:**
- **`CameraSync.cs`:** `RefreshHidden`. The spore hiding runs again late in the frame (driver
  `LateUpdate`), before drawing.
- **`WeaponBag.cs`:**
  - `EmptyHand`: the held weapon into the bag, and an invisible `Slash/Stick` stand-in with only
    the "Nothing" attribute (Mycelium needs a held weapon, and its level change expects an
    attribute). It's hidden again after a new level, and removed by a pickup.
  - No number updates while a switch runs.
- **`LinkTestMain.java`:** `LINKTEST_HAND=1` now goes #1, empty, #2.
- DESIGN §5.0.4.

**Tested how:** the real build with the fake Minecraft in third person with the test body
(`LINKTEST_VIEW=1`, `LINKTEST_MODEL=1`, `LINKTEST_HAND=1`), using `-mcPickupTest` in Daybreak
Arboretum.

**Result:**
- Every weapon built in a switch was logged "not drawn (caught before drawing)".
- Empty hand: "'Stick' back in the bag, the spore holds an invisible stand-in (Nothing attribute)".
- The pickup with an empty hand removed the stand-in and bagged the mace.
- Switching back brought the mace with its 3 attributes.

**Still broken / not tested:**
- **Not seen in Minecraft:** that the flash is gone.
- A level change with an empty hand isn't tested: the stand-in is rebuilt by Mycelium and hidden
  again, but the first frame of the new level may show it.

## 2026-10-20 Max health changes keep the current/max share

**Changed:**
- **`MinecraftDriver.cs`:** `KeepHealthRatio`. When Mycelium's max health changes, current health
  is set to the share it had of the old max, and Mycelium's bar is redrawn. Not when Mycelium set
  it to the new max itself, and not while dead.
- **`MyceliumCraftClient.java`:** Minecraft's red hurt flash only when max health didn't change.
  The hurt tilt already worked that way.
- **New test aid:** `-mcHealthTest`.
- DESIGN §5.0.2, README.

**Why:** you asked that current health keep its ratio when max health changes. Weapons with
Vitality attributes now change it whenever you switch.

**Tested how:** the real Mycelium build with the fake Minecraft in Daybreak Arboretum, using
`-mcHealthTest`.

**Result:**
- At half health, Vitality +5 took max from 100 to 150 and current from 50.3 to 75.4 (50 %).
- Vitality −5 took 150 to 100 and 77.1 to 51.4 (51 %; regeneration added a little in between).
- No hurt tilt.

**Still broken / not tested:**
- **The no-flash change on Minecraft's side isn't seen in Minecraft.**

## 2026-10-20 Getting hit with the inventory open no longer moves the cursor to the middle

**Problem (your report):** when you got hit while the inventory was open, the cursor jumped to the
middle of the window.
- A hit's knockback briefly takes Mycelium's controls ("canAct off, knockback"), which turned the
  input bridge off.
- Turning it off gives Windows' pointer back to Mycelium locked, and a locked pointer sits in the
  middle of the window.
- The bridge came back on a moment later and freed the pointer, but in the middle.
- The re-sync teleport after a knockback also counted as "not driving" for a few frames, which
  did the same.

**Changed:**
- **`MinecraftDriver.cs`:**
  - a knockback doesn't take the controls from the bridge while a Minecraft screen is open;
  - re-sync teleports count as driving for the bridge (as for key ownership);
  - test runs log what the bridge would do with focus.
- **`LinkTestMain.java`:** `LINKTEST_SCREEN=1` keeps a screen open from 4 to 10 s, without pausing.
- DESIGN §5 (cursor for Minecraft's screens).

**Tested how:** the real build with the fake Minecraft (`LINKTEST_SCREEN=1`, `LINKTEST_STILL=1`),
using `-mcHitTest` (a knockback every 4 s) in Daybreak Arboretum.

**Result:**
- The screen was open from 17.40 to 23.40 s (keys log). The knockbacks at 17.42 and 21.42 s left
  the bridge on.
- The knockbacks at 13.42 and 25.42 s, with no screen, took the controls for a moment as before.

**Still broken / not tested:**
- **Not tested with your mouse in the real games:** test runs never turn the bridge on.
- Without a screen open, a knockback still turns the bridge off for a moment, as before. The
  pointer is locked then anyway.

## 2026-10-20 Attacks only at full charge (the 5 % rule removed)

**Changed:**
- **`MinecraftMixin.java`:** `Minecraft.startAttack` is stopped while the attack indicator isn't
  full, as long as Mycelium is playing. There's no swing, no hit and no block mining.
- **`MyceliumCraftClient.java`:** `attackNotCharged`, which counts and logs each attack it stops.
- **Removed:** the 5 % weak-hit rule (`AttackCharge.java`, `PlayerAttackChargeMixin.java`, and the
  rule in `EnemyProxy.java`).
- DESIGN §5.0.3, README.

**Why:** the 5 % rule worked (your test), but you changed your mind: a weapon can't be used until
the cooldown is full.

**Tested how:** the jar builds. Neither stand-in runs Minecraft's attack.

**Still broken / not tested:**
- **Not tested in Minecraft.**
- Bows and other things that don't go through `startAttack` are unchanged.

## 2026-10-20 Weak hits do 5 % damage

**Changed:**
- **New `mixin/PlayerAttackChargeMixin.java`:** at the start of a player's attack (server),
  remembers how charged the attack indicator was and the full attack damage. Minecraft resets the
  charge before the target is hurt.
- **New `combat/AttackCharge.java`:** holds that, per player, for the current game tick.
- **`EnemyProxy.java`:** a direct melee hit under 90 % charge sends 5 % of the full attack damage
  to Mycelium, instead of Minecraft's 20–100 %. It's logged.
- DESIGN §5.0.3, README.

**Why:** you asked for under-90 % attacks to do 5 % damage.

**Tested how:** the jar builds. Neither stand-in runs Minecraft's attack.

**Still broken / not tested:**
- **Not tested in Minecraft.** Not sure the charge read at the start of `attack` is the same as the
  bar on screen; it should be, as Minecraft reads it the same way.
- Arrows and other ranged hits are unchanged.

## 2026-10-20 Weapons: one carrot each in Minecraft's inventory (protocol v24)

**What you asked:**
- Picking up a weapon adds it to the Minecraft inventory instead of swapping (carrots for now).
- Attribute modifiers that match each weapon's stats, counting player and weapon attributes,
  multipliers and stats.
- Runs start with a Stick.

Your choices:
1. Carrots are rewritten when the spore changes.
2. The spore holds the selected weapon.
3. Weapons are cleared on death, and each run starts with a Stick.

**Changed:**
- **New `mycelium/Runtime/WeaponBag.cs`:**
  - the bag (pickups keep the old weapon);
  - rebuilding the selected weapon in the hand, after Mycelium's own between-levels recipe;
  - the per-weapon numbers;
  - emptied in The Carcass.
- **New `fabric/.../MycWeapons.java`:** one carrot per weapon (name, attack damage and speed
  modifiers, description, not food), selection after a pickup, copies and dropped carrots
  removed, carrots taken out before the death drop.
- **`EnemyCombat.cs`:** Minecraft's hits also apply the held weapon's knockback.
- **Protocol v24:** a Weapons block (after HitBoxes, 0x4000), and McState 0x90 for the main-hand
  weapon. Both stand-ins check the block.
- **New test aids:** `-mcPickupTest`, `-mcSwingTest`, and `LINKTEST_HAND=1`.
- DESIGN §5.0.4, PROTOCOL, README.

**Tested how:**
- `tools\FakeMycelium` + `gradlew linkTest`.
- The real Mycelium build in Daybreak Arboretum with the fake Minecraft (`LINKTEST_HAND=1`), using
  `-mcPickupTest -mcSwingTest`.

**Result:**
- Link: all weapon fields read back (UTF-8 name included), PASS.
- In Mycelium:
  - Stick #1: 10 damage, 0.98 swings/s.
  - Pickup: the Cactus Mace became #2 and the Stick stayed. The mace was ×1.6 damage, ×1.15
    speed, 16 damage, 0.61/s, with Toxicating, Hazardous and Nasty.
  - Following the stand-in's hand: Stick, then the mace again, with 3 attributes (2 the
    prefab's own).

**Dead ends on the way (fixed):**
1. Rebuilding added the prefab's built-in attributes a second time: "Heavy Hazardous Nasty
   Hazardous Nasty Rose Mace", and it grows each switch.
2. Swing time from the clips was 1.5–2× too slow. The states play faster than their clips: Slash
   1.71 → 1.14 s, Smash 4.17 → 2.08, Stab 1.08 → 0.87. Measured with `-mcSwingTest`; the state
   lengths are read from the animator now.
3. Timing your real swings can't work while Minecraft drives (Mycelium's attack key is unbound),
   so it was removed again.

**Still broken / not tested:**
- **The Minecraft side isn't tested in Minecraft at all:** the carrots, their tooltips,
  selecting after a pickup, removal on death, removing dropped copies.
- If your inventory is full, a new weapon's carrot isn't added (logged).
- When a weapon is rebuilt, its damage multiplier is redone for the current loop. Mycelium's own
  level change keeps the old one.
- Weapon looks: carrots for now. Special attribute effects work in Mycelium, through the held
  weapon; they aren't shown in Minecraft beyond the description.

## 2026-10-20 Weapon swaps: dropped weapon visible, new one hidden behind your Minecraft body

**Problem (your report):** after grabbing a new weapon, the old one stayed invisible and the new
one stayed visible.
- While your Minecraft body stands in for the spore, the spore's renderers are hidden, its weapon
  included. That list was made once, when hiding started.
- Mycelium's pickup (`WeaponInteraction`) lets the old weapon go into the world
  (`parent = null`) and puts a new one in the hand slot. The old one stayed on the list (hidden
  on the ground), and the new one was never on it.

**Changed:**
- **`CameraSync.cs`:** `KeepHidden`, every frame while the spore is hidden (or shadow only in
  first person). Renderers that left the spore are drawn again, and new ones under it are hidden.
  Each change is logged.
- **`MinecraftDriver.cs`:** test aid `-mcSwapTest`.
- DESIGN.

**Tested how:** the real build with the fake Minecraft in third person with the test body
(`LINKTEST_VIEW=1`, `LINKTEST_MODEL=1`), using `-mcSwapTest` in Daybreak Arboretum.

**Result:** "'Stick(Clone)' left the spore; drawn again", then "'Stick(Clone) (picked up)' is new on
the spore; not drawn". After 0.5 s: dropped drawn=True, held drawn=False, both as wanted.

**Still broken / not tested:**
- **Not tested with a real pickup in Minecraft:** the test copies `WeaponInteraction`'s steps.

## 2026-10-20 Test keys removed (0 and \)

**Changed:**
- **`MinecraftDriver.cs`, `SproutWatch.cs`:** the 0 and \ sprout-replay keys are gone.
- **Both sides:** the `EMERGE_BEHIND` flag is gone (MycState bit 3 is unused again).
- **`MyceliumCraftClient.java`:** the emerge always uses the front view.
- PROTOCOL, DESIGN. The protocol stays at version 23.

**Why:** you were happy with the emerge and asked for the keys to go.

**Tested how:**
- Both builds.
- `tools\FakeMycelium` + `gradlew linkTest`.
- Real Mycelium, `-mcScene "The Carcass"`.

**Result:**
- Builds succeed. Link check: "sprout progress: got 0.25", PASS.
- In The Carcass, the sprout started 0.12 s after arriving and was done at 2.99 s, as before.
- No test-key code is left (searched for it).

## 2026-10-20 Head tilts further forward in the emerge; TEMPORARY \ key (emerge in third person behind; protocol v23)

**Changed:**
- **`EmergePose.java`:** the head nods further forward. It now goes up to 0.55 rad (was 0.2) on
  top of the torso's lean while pulling, then 0.35, then back to 0.
- **`MinecraftDriver.cs`, `SproutWatch.cs`:** the \ key replays the sprout like the
  0 key, and sets MycState flag bit 3 `EMERGE_BEHIND` while it runs.
- **`MyceliumCraftClient.java`:** with that flag, the emerge uses third person behind instead of in front.
- **Protocol v23:** the new flag. Both keys are marked TEMPORARY.
- DESIGN, PROTOCOL.

**Why:** you asked for more head tilt, and a second test key with the behind view.

**Tested how:** the real build with the fake Minecraft, holding \ through
`-mcHoldKeys Backslash,Backslash`.

**Result:** "\ key: replaying Mycelium's sprout, emerge in third person behind". The
Sprout ran 0 → 0.70+ as before. Both builds succeed.

**Still broken / not tested:**
- **Not tested in Minecraft.** The fake Minecraft doesn't read the flag, so the behind view
  switch is only checked by compiling.
- The new head tilt isn't seen yet.

## 2026-10-20 Emerge, second version: arms pull and push, less torso tilt

**Problem (your test):** the torso tilted too much, and the arms didn't seem to move.
- I had played Minecraft's own Warden emerge on the player. The Warden is about 3 blocks tall,
  so on a player-sized body all of its clawing (arms overhead, 1.2–2.9 s in) happened with even
  the hands underground.
- When the arms came out, the Warden's animation barely moves them. Its torso leans up to
  1.2 rad (70°).

**Changed:**
- **`EmergePose.java`:** its own dig-out, made for a player body, by sprout progress (0 to 1):
  1. Arms up; hands and head break out.
  2. Pull: each hand is planted on the ground in front and held there while the torso rises
     (the arm angle is worked out from the shoulder's height every frame). The left plants a
     little after the right.
  3. Push: arms down at the sides, hips out, then the legs step up.
  - The torso leans at most 0.35 rad (20°).
  - Minecraft's Warden animation isn't used any more.
- **`MyceliumCraftClient.java`, `MycPlayerModel.java`, `HumanoidEmergeMixin.java`:** progress
  instead of Warden seconds.
- DESIGN §5.0.2, README.

**Tested how:** offline. A scratch program ran it on Minecraft's standard humanoid model every 5 %,
printing where the hands, the top of the head and the hips are. Then the jar was built.

**Result:**
- Hands at y 23.4–24.3 (the ground is 24) from 22 % to 55 %, while the torso rises from 28 to
  12 and the hands slide back from 9.5 to 3 pixels in front.
- Hips out at 50 %. Normal pose at 100 %.
- Hands and head come out together, at about 18 %. In Minecraft's proportions, raised hands only
  reach 2 pixels above the head.

**Still broken / not tested:**
- **Not seen in Minecraft:** whether it reads as pulling and pushing.
- The numbers are first guesses, easy to tune.

## 2026-10-20 Emerge in the third-person front view

**Changed:**
- **`MyceliumCraftClient.java`:** the view switch takes the view to use. The emerge uses third
  person in front, facing you as you dig out. The death keeps third person behind.
- DESIGN, README.

**Why:** you asked for the respawn animation in the front view.

**Tested how:** the jar builds. Mycelium already follows Minecraft's front view (F5, tested
earlier), so nothing changed there.

**Still broken / not tested:**
- **Not tested in Minecraft.**

## 2026-10-20 TEMPORARY: the 0 key replays the sprout (to try the emerge)

**Changed:**
- **`MinecraftDriver.cs`:** pressing 0 does what Mycelium does on arriving in The Carcass, right
  where you are: controls off, the spore's "Sprout" animation, controls back. Nothing is saved
  and no scene changes.
- **`SproutWatch.cs`:** `ArmNow`, so Minecraft's emerge follows that sprout.
- **Marked TEMPORARY** in the code and DESIGN. Remove when done.

**Why:** you asked for a quick key to trigger a respawn, to try the emerge without dying.

**Tested how:** the real build with the fake Minecraft in Daybreak Arboretum, holding 0 through
`-mcHoldKeys Digit0,Digit0`.

**Result:** the Sprout started 0.02 s after the key. Progress went 0 → 0.88 over 2.87 s, then
"done", and the controls came back.
- Two dead ends on the way:
  1. `wasPressedThisFrame` never fired, so the key's up/down is tracked by hand now.
  2. `DisableController` takes one optional argument, so calling it with none failed silently.

**Still broken / not tested:**
- **The emerge itself isn't tested in Minecraft.**
- The 0 key also reaches Minecraft through the input bridge. 0 isn't bound in Minecraft by default.

## 2026-10-20 Emerge: the Warden's dig-out when Mycelium sprouts you (protocol v22)

**Changed:**
- **New `mycelium/Runtime/SproutWatch.cs`:** on arriving in The Carcass (unless the spore died for
  good), sends how far through Mycelium's sprout it is: 0 while waiting, then the "Sprout"
  animation's progress up to 1, then −1.
- **New `fabric/.../EmergePose.java`:** plays Minecraft's own Warden emerge animation, read from
  the game at runtime, on the player model. Head and arms are combined with the body's movement,
  as in the Warden.
- **New `mixin/HumanoidEmergeMixin.java`:** applies it in Minecraft's own posing, only while the
  mod captures your body.
- **`MycPlayerModel.java`:** switches that on around the capture.
- **`MyceliumCraftClient.java`:** one third-person-behind switch, shared by the death and the
  emerge. Logs the emerge.
- **Protocol v22:** MycState 0x5C. Both stand-ins check it.
- DESIGN §5.0.2, README.

**Why:** you asked for the Warden's crawl out of the ground when respawning in The Carcass.

**Tested how:**
1. **Offline:** a scratch program ran `EmergePose` on Minecraft's standard humanoid model at 7
   moments. Nothing from the game went into the repo.
2. **Link:** `tools\FakeMycelium` + `gradlew linkTest`. "sprout progress: got 0.25, expected
   0.25", ground checks PASS.
3. **Real Mycelium,** `-mcScene "The Carcass"`.

**Result:**
1. At 0 s, body, head, arms and legs are all 63 pixels down (the player is 32 tall). They rise
   together, and at 6.68 s they're back in the normal pose.
2. Passes.
3. The sprout started 0.12 s after arriving. Progress went 0 → 0.96 smoothly over 2.87 s, then
   "done" at 2.99 s.

**First try, changed:** the first version ended the emerge when Mycelium's sprout ended, at the
Warden's own speed. That assumed a 10 s wait, but The Carcass waits 0.1 s and the sprout lasts about
2.9 s, so it would have started halfway through and skipped the buried part. Now the whole emerge
plays over the sprout, about 2.3× as fast.

**Still broken / not tested:**
- **Not tested in Minecraft at all.** Not checked: the look, that Mycelium's ground hides the
  buried body, held items, armour, cape.
- Mid-emerge the head tips very far forward. That may look odd on a player.
- It runs on every arrival in The Carcass, not only after a death, because Mycelium sprouts every
  time.
- After a death, Minecraft's respawn comes when you arrive, so the first part of the emerge may
  be missed while Minecraft reloads.
- No dig particles or sounds.

## 2026-10-20 Hits no longer turn you (resyncs keep your facing; protocol v21)

**Problem (your report):** after getting hit, your body was sometimes turned a little.
- After a hit's knockback, Mycelium moves the spore, so the mod teleports Minecraft's player back
  onto it (a resync).
- That teleport also set Minecraft's facing to the spore's. The spore faces where Minecraft's body
  points, which lags up to about 50° behind where you look.
- Your log, 2026-10-20 08:00:18: Minecraft faced 322.8°, and the resync set 273°.

**Changed:**
- **`MinecraftDriver.cs`:** only the first teleport (Minecraft starts driving, or a new world)
  sends a facing. Resyncs send NaN, meaning "keep".
- **`MyceliumCraftClient.java`:** a NaN yaw leaves the player's facing alone, on the client and
  on the server.
- **Protocol v21:** PROTOCOL.md describes the NaN.
- DESIGN §4 step 5.

**Tested how:** the real Mycelium build with the fake Minecraft, using `-mcHoldTest` (takes the
controls away and pushes the spore 1 unit, as a knockback does).

**Result:**
- The start teleport still sends a facing ("yaw=180").
- The resync after the push sent "yaw=kept", and Minecraft confirmed it in 0.02 s. Driving went on.

**Still broken / not tested:**
- **Not tested in Minecraft:** the stand-in ignores facings, so it can't show that you aren't
  turned any more.
- `-mcHitTest`'s knockbacks didn't move the spore far enough for a resync in this run, so that
  path is only covered by `-mcHoldTest`.

## 2026-10-19 Death animation, third try: closing a screen respawned you too

**Problem (your test):** still standing and untinted.
- Minecraft's log showed the death message hold worked ("immediate respawn held back").
- But within the same second the level sync ran again, which it only does for a living player.
  And every enemy stand-in on Minecraft's side was sent again, as after a respawn.
- In Minecraft's code (26.3), `Gui.setScreen` does this: when a screen closes while the player is
  dead, it opens the death screen, or with immediate respawn on, respawns. Something closed a screen
  right after the death. I don't know what yet; the new log line will say.

**Changed:**
- **New `mixin/GuiDeathScreenMixin.java`:** while the death is held, closing a screen just closes
  it (the player doesn't count as dead there).
- **`MyceliumCraftClient.java`:** `deathHeld`; logs the first such screen close per death, with
  the methods that called it.
- DESIGN §5.0.2.

**Tested how:**
- Read `Gui.setScreen`'s code in Minecraft 26.3: it checks `LocalPlayer.isDeadOrDying` after
  `setScreen(null)`, then calls `shouldShowDeathScreen` and `respawn`.
- The jar builds.

**Result:** compiles.

**Still broken / not tested:**
- **Not tested in Minecraft.**
- Not sure these are the only two ways Minecraft respawns. I searched Minecraft's client for
  every call to `respawn()`. The only others are the death screen's own buttons and one in the
  game-event handler, which shouldn't run at death.

## 2026-10-19 Death animation, second try: hold back Minecraft's immediate respawn

**Problem (your test):** the view switched to third person behind, but the body stayed standing and
untinted.
- Minecraft's log showed why. Your world has the "immediate respawn" game rule on, so Minecraft
  respawned the player at world spawn (2.5, -60, 2.5) within a second of the kill.
- The body sent to Mycelium was the new, alive player, so there was no death animation to show.
- The mod's own "respawn when Mycelium is alive again" never ran: the player wasn't dead any more.

**Changed:**
- **New `mixin/PlayerCombatKillMixin.java`:** when Mycelium's death caused Minecraft's "you died",
  Minecraft's death screen and its immediate respawn are both held back. The body stays dying
  (falls over, red) until Mycelium is alive again, when the mod respawns it as before.
- **`MyceliumCraftClient.java`:** `holdDeath` and `releaseDeath`. If Mycelium goes away, or isn't
  back in a level within 30 s, Minecraft gets its death screen or immediate respawn back.
  Hardcore worlds are left alone.
- DESIGN §5.0.2.

**Tested how:**
- Checked the hook point and every method it uses against Minecraft 26.3's own code:
  `handlePlayerCombatKill` opens `DeathScreen` or calls `respawn()` when `shouldShowDeathScreen`
  is false.
- The jar builds.

**Result:** compiles.

**Still broken / not tested:**
- **Not tested in Minecraft.** Not sure the hook attaches at start-up: a mixin that can't find
  its spot stops Minecraft from loading, and the log would say so.
- Not sure the dying body stays lying for the whole 3 s, rather than vanishing with a "poof".

## 2026-10-19 Death: third person behind and Minecraft's death animation

**Changed:**
- **`MyceliumCraftClient.java`:**
  - When Mycelium's death kills the Minecraft player, Minecraft's view switches to third person
    behind. Mycelium's camera follows it.
  - Your own view comes back when Mycelium is alive again, or when Mycelium leaves the level.
  - While dead, the eye-height ratio sent to Mycelium is held at 1.
- **The death animation itself needed no new code.** The body Mycelium draws is posed by
  Minecraft's own player renderer, which tips a dying player over and tints it red.
- DESIGN §5.0.2, README.

**Why:** you asked to see the Minecraft death animation in third person behind when you die.

**Tested how:** the jar builds. That's all.

**Result:** compiles.

**Still broken / not tested:**
- **Not tested at all.** Neither stand-in runs Minecraft's real player or renderer. I didn't kill
  the spore in real Mycelium either: its death saves your profile and drops your held material.
- Not sure: whether the dying body lies still or vanishes with Minecraft's "poof" after a second.
  I expect it to stay lying (the client keeps its own player), but I haven't checked.
- Minecraft's small death camera roll isn't copied.

## 2026-10-19 Hurt tilt a little less strong

**Changed:**
- **`HurtTilt.cs`:** the full lean is 10° instead of Minecraft's 14°. It's still scaled by
  Minecraft's "Damage Tilt" setting.
- DESIGN §5.0.3.

**Why:** you found the tilt slightly too intense.

**Tested how:** the real build with the fake Minecraft in Daybreak Arboretum, using `-mcHurtTest`.

**Result:** four hits, from in front, right, behind and left. Each peaked at 10.0° and ended after 0.5 s.

**Still broken / not tested:**
- **How it looks on screen isn't checked**, and it isn't tested in Minecraft.

## 2026-10-19 Camera tilts toward what hurt you, as Minecraft's (protocol v20)

**Changed:**
- **New `mycelium/Runtime/HurtTilt.cs`:**
  - When the spore's health drops, finds the hitter: the nearest enemy or attack-looking collider
    within 4 units, else the nearest live enemy within 15, else none (rolls left).
  - Leans the view toward it with Minecraft's curve: sin(f⁴π) × 14° over 0.5 s.
- **`CameraSync.cs`:** puts the lean on the finished view, in first person and both third-person views.
- **`MinecraftDriver.cs`:** feeds it the health each frame; new test aid `-mcHurtTest`.
- **Protocol v20:** McState 0x8C carries Minecraft's "Damage Tilt" setting
  (`MyceliumCraftClient`, `MycLink`, `Proto` on both sides, PROTOCOL.md).
- DESIGN §5.0.3, README.

**Why:** you asked for Minecraft's hurt camera tilt toward the source of damage.

**Tested how:** the real Mycelium build with the fake Minecraft (`LINKTEST_STILL=1`) in Daybreak
Arboretum, `-mcHurtTest`, in first person (`LINKTEST_VIEW=0`) and third person (`LINKTEST_VIEW=1`).

**Result:**
- Hits from in front, right, behind and left were each found on the right side, while the player
  was facing different ways. Each lean peaked at 14.0° and ended after 0.5 s.
- In the first run a real beetle attacked. It was found as the hitter, but its direction came from
  a point 0.1 units from the spore. Fixed: the direction now comes from the enemy's own position.
- With Minecraft gone, no lean is put on Mycelium's own camera (0 frames), as intended.

**Still broken / not tested:**
- **Not tested in Minecraft.** Reading the real "Damage Tilt" setting is untested: the stand-in
  always sends 100 %.
- **How the lean looks on screen isn't checked.**
- **The beetle's direction after the fix isn't checked:** no beetle attacked in the second run.
- The hitter is a guess and can be wrong.
- Minecraft's death roll isn't copied.

## 2026-10-19 Enemy hurt flash a little brighter

**Changed:**
- **`Resources/MyceliumCraftHurtTint.shader`:** multiply by (1, 0.72, 0.72) instead of (1, 0.6, 0.6),
  then add (0.12, 0.03, 0.03) of red light. Two passes, tagged for URP.
- **`HurtFlash.cs`:** logs the tint's pass count.
- DESIGN §5.0.3.

**Why:** you liked the hue and see-through look but found it a little dark. A multiply only takes
light away.

**Tested how:** the real build in Daybreak Arboretum with fake hits.

**Result:** "hurt flash: tint ready, 2 passes"; flashes on every hit.

**Still broken / not tested:**
- **The look on screen isn't checked.**

## 2026-10-19 Enemy hurt flash keeps the model's details visible

**Changed:**
- **`Resources/MyceliumCraftHurtTint.shader`:** the flash now multiplies the enemy's colours by
  (1, 0.6, 0.6) (`Blend DstColor Zero`), instead of drawing see-through red over them (40 %).
- **New `-mcFlashCheck` test aid:** reads back the screen around a flashing enemy, during and after
  the flash (colour and neighbouring-pixel contrast). `HurtFlash` remembers the last enemy flashed.
- DESIGN §5.0.3.

**Why:** you wanted the red tint to be see-through enough that the model's details still show.

**Tested how:** the real build in Daybreak Arboretum with the fake Minecraft's hits and
`-mcFlashCheck`.

**Result:** **inconclusive.** The fake Minecraft can't face the enemies: they were 1 pixel wide in
the distance, or behind the camera. The flashes themselves still happen ("hurt flash" lines as
before).

**Still broken / not tested:**
- **The look on screen isn't checked.** Running `-mcFlashCheck` while facing an enemy logs real
  numbers.

## 2026-10-19 Red hurt flash on your body and on enemies, as in Minecraft

**Changed:**
- **`MyceliumCraftClient.syncHealth`:** when Mycelium's health drops, Minecraft's hurt animation
  starts on the client (`hurtTime`/`hurtDuration` 10). The captured body shows Minecraft's red tint.
- **New `HurtFlash.cs`** and `Resources/MyceliumCraftHurtTint.shader`: an enemy whose health
  dropped (any cause) gets a see-through light-red copy of its mesh drawn on top for 0.5 s.
- **`EnemyCombat.cs`:** watches every enemy's health each frame and flashes those that dropped.
- DESIGN §5.0.3.

**Why:** you asked for the light red flash when enemies and the player take damage, as Minecraft
does.

**Tested how:** the real build in Daybreak Arboretum, minimized, fake Minecraft hitting the nearest
enemy every 2 s.

**Result:** after every hit, "hurt flash: 1 enemy flashes in the last second", with no errors
(stickbug, then beetles). The mod builds.

**Still broken / not tested:**
- **The look on screen isn't checked** (the colour and strength of the red, and enemies whose model
  has several parts).
- **Your body's flash** isn't tested in Minecraft.

## 2026-10-19 A click right after a hit became Mycelium's attack

**Changed:**
- **`MinecraftDriver.cs`:** key ownership stays on through re-sync teleports (`phase != Idle`, was
  `== Driving`).
- **`KeyOwnership.cs`:** Mycelium gets its own keys back only after 0.5 s without Minecraft driving.
- **`-mcHoldTest`:** also pushes the spore 1 unit while Mycelium has the controls, so a re-sync
  teleport follows.
- DESIGN §5.3.

**Why:** you got hit right before clicking, and the click did a Mycelium attack.

**Cause:** your log (saved as `work\user-attack-slip.log`):
- **284.22 s:** after the hit's knockback, "Mycelium moved the spore 0.5 units", so a re-sync
  teleport.
- **284.26 s:** "Mycelium's own key bindings back" (not "driving" during the teleport).
- **284.27 s:** "Mycelium actions pressed now: [Attack,Curio]".
- **284.28 s:** Minecraft's keys set again.

That's a 20 ms window.

**Tested how:** the real build in the tutorial, minimized, `-mcHoldTest` with the push, fake
Minecraft.

**Result:** "Mycelium moved the spore 0.9 units" → WaitingForTeleport → Driving in 20 ms, and **no**
"own key bindings back" (before the fix, the same sequence gave one).

**Still broken / not tested:**
- **Not tested with real hits in your play.**

## 2026-10-19 Minecraft experience level = Mycelium's nutrients

**Changed:**
- **Protocol v19:** MycState 0x58 nutrients (i32, −1 unknown).
- **`MinecraftDriver.ReadNutrients`:** reads `NutrientTracker.currentNutrients`.
- **`MyceliumCraftClient.syncLevel`:** sets Minecraft's level to it with an empty bar on the
  integrated server, and puts it back if it drifts.
- **Stand-ins:** FakeMycelium sends 42; linkTest checks it.
- PROTOCOL, DESIGN §5.0.2.1, README.

**Why:** you asked for Minecraft's player level to match the nutrients.

**Tested how and result:**
- **Stand-ins:** "Mycelium nutrients: got 42.0"; PASS.
- **Real build in the hub:** "found Mycelium's NutrientTracker on 'NutrientCounter'",
  "nutrients: 3587 -> Minecraft experience level 3587".

**Still broken / not tested:**
- **Not tested in Minecraft:** the level shown.
- **Spending levels in Minecraft** (enchanting, anvil) doesn't spend nutrients; the level is just
  put back. XP orbs don't add nutrients.

## 2026-10-19 Fast enemies' hitboxes lagged behind them

**Changed:**
- **`EnemyProxies.clientSnap`:** every client copy of a stand-in is put exactly at Mycelium's
  current enemy position and its glide cancelled. It runs:
  - at the start of every client tick (new `MinecraftMixin` hook on `Minecraft.tick`, before the
    crosshair pick);
  - before the boxes are sent for `-mcShowHitboxes`.

  It logs how far off they were before each snap, as a once-a-second average.
- **The stand-in type** is updated every tick from the server (`updateInterval(1)`, was 3).
- DESIGN §5.0.3.

**Why:** you saw fast enemies' real hitboxes lag behind them.

**Cause:** Minecraft moves a client copy from server updates, every 3 ticks for this kind, and
glides it there over a few more ticks. That's a few hundred ms behind a fast enemy.

**Tested how:** the mod builds. Minecraft's pick happens at the start of `Minecraft.tick`, before
`tickEntities` (checked with javap).

**Still broken / not tested:**
- **Not tested in Minecraft.** Minecraft's log shows "client stand-ins snapped ... N blocks off on
  average before the snap"; that's the lag the snap removes.
- **The server's copy** can still be up to a tick (50 ms) behind. Minecraft's own reach check
  allows some slack.

## 2026-10-19 Hitboxes: show what Minecraft really hits, and give the client the right size

**Changed:**
- **`EnemyProxy.java`:** the enemy id, width and height are synced creature data, so Minecraft's
  client knows them and resizes its copy (`onSyncedDataUpdated`).
- **Protocol v18:** new HitBoxes block after the textures (Minecraft → Mycelium).
- **`MyceliumCraftClient.sendHitBoxes`:** every frame, each stand-in's box as the client has it
  (bounding box + pick radius).
- **`EnemyCombat`/`HitboxView`:** `-mcShowHitboxes` draws those boxes instead of Mycelium's own
  numbers.
- **`LinkTestMain`:** with `LINKTEST_HIT=1`, also sends boxes built the way the client would.
- PROTOCOL, DESIGN §5.0.3.

**Why:** you asked for the hitbox of the part Minecraft's hits can actually reach.

**Found:** the stand-in's size was only set on the integrated server. The client, where the
crosshair picks, never got it and kept Minecraft's default 0.6 × 0.6 box. That's likely why hits
seemed hard to land.

**Tested how:** the real build in Daybreak Arboretum, minimized, fake Minecraft with `LINKTEST_HIT=1`.

**Result:** "hitboxes: 7 shown", then down to 6, 5, 4, 3 and back up as enemies came and went,
matching the boxes the fake sent back; hits applied as before. Stand-ins and builds pass.

**Still broken / not tested:**
- **Not tested in Minecraft:**
  - the client's copy resizing;
  - the real boxes sent back (Minecraft's log: "N enemy stand-ins on the client; first #… box W x H
    x D blocks").

## 2026-10-19 -mcShowHitboxes: see the enemies' Minecraft hitboxes in Mycelium

**Changed:**
- **New `HitboxView.cs`** and `Resources/MyceliumCraftLine.shader`: with `-mcShowHitboxes`, each
  enemy's stand-in is drawn as a wireframe box in Mycelium's window, exactly as Minecraft sizes it:
  - green, red for 0.3 s after a hit lands;
  - drawn through walls.
- **`EnemyCombat.cs`:** feeds it the same numbers it sends to Minecraft.
- DESIGN §5.0.3.

**Why:** you asked for an argument that shows the enemies' hitboxes.

**Tested how:** the real build in Daybreak Arboretum, minimized, fake Minecraft with `LINKTEST_HIT=1`.

**Result:**
- "hitboxes: 2 shown" (a stickbug, 1.25 × 0.57 blocks, and a beetle). The stickbug was hit
  20 → 10 → 0, then "1 shown". The beetle 30 → 20 → 10 → 0, then "0 shown".
- **What the boxes look like on screen** wasn't checked (log only).

## 2026-10-19 Combat, step 1: your Minecraft attacks hurt Mycelium's enemies

**Changed:**
- **Protocol v17:** Enemies table at 0x700 (Mycelium → Minecraft), Hits ring at 0xF00
  (Minecraft → Mycelium).
- **New `EnemyCombat.cs`:** sends live enemies within 64 units (id, feet, size from solid
  colliders, health), and applies hits with `EnemyTakeDamage(damage × damageScale, false, true)`.
  It logs why no enemies are sent.
- **New `combat/EnemyProxy.java`:** an invisible stand-in creature that reports player hits and
  stays alive.
- **New `combat/EnemyProxies.java`:** registers it (`myceliumcraft:enemy_proxy`, NoopRenderer),
  and keeps one per enemy every server tick.
- **New `damageScale=` setting** (default 5).
- **`LinkTestMain.java`:** `LINKTEST_HIT=1` hits the nearest enemy for 2 points every 2 s.
- PROTOCOL, DESIGN §5.0.3, README.

**Why:** you asked to start the combat step. Enemy hits on you already worked through the health
sync; this is the other way round.

**Damage scale chosen:** ×5, the same rate as health (beetle 30 health, spore base damage 10, iron
sword 6 → 30).

**Tested how and result:**
- **Tutorial:** no enemies exist at its start ("0 enemy objects in the scene").
- **Daybreak Arboretum:** "1 Mycelium enemies sent; first 'Beetle' 1.61 x 0.71 blocks, health
  30/30". The fake Minecraft saw it 36 blocks away and hit it.
  - "Minecraft hit 'Beetle' for 2.00 points -> 10.0 Mycelium damage; health now 20.0", then 10.0,
    then 0.0.
  - Then "no enemies near: 17 enemy objects, 16 live, nearest 86 units away".
- Stand-ins PASS; the mod builds.

**Still broken / not tested:**
- **The Minecraft side isn't tested in Minecraft at all:** the creature registration, stand-ins
  appearing and following, hits reaching them. If the registration fails, Minecraft won't start;
  its log will say why.
- **Critical hits** aren't marked in the hit (the damage already includes them).
- **Knockback** on Mycelium's enemies isn't sent.
- **Enemies further than 64 units** have no stand-in (an arrow from further away can't hit them).

## 2026-10-19 Still stuck on "Loading Terrain": the old jar was installed

**Changed:**
- **`build.gradle` and `fabric.mod.json`:** the jar carries its build time (`myceliumcraft:built`).
- **`MyceliumCraftClient.java`:** logs it at start, "jar built 2026-10-19 10:10", so an old jar in
  the mods folder is easy to spot.

**Why:** you were still stuck after dying.

**What the logs showed** (Minecraft's `latest.log`, read only):
- **The jar in the Prism mods folder** was the 09:21 build (76,606 bytes). The "Loading terrain" fix
  was in the 10:05 build (77,727 bytes), and its log line never appears. So the fix wasn't tested yet.
- **10:07:51:** "the Minecraft player dies too", "was killed". Then the death screen.
- **10:08:04:** with Mycelium back in the hub, the player was already alive (health set to 20, no
  "respawning" line). Most likely Respawn was clicked on Minecraft's death screen through the
  overlay, and the old build then got stuck on "Loading terrain".

**Tested how:** the jar builds and its `fabric.mod.json` has "myceliumcraft:built":
"2026-10-19 10:10".

**Still broken / not tested:**
- **The "Loading terrain" fix** is still not tested in Minecraft.

## 2026-10-19 Minecraft stuck on "Loading Terrain" after dying

**Changed:**
- **New `WaitingForPlayerChunkMixin`** (`LevelLoadTracker$WaitingForPlayerChunk.isReady` answers
  true while the overlay is on or Mycelium is running), as SkyCraft does.
- DESIGN §5.0.2.

**Why:** after you died, Minecraft respawned and then stayed on its "Loading Terrain" screen.

**Cause:** that screen waits until the player's chunk section has been prepared for drawing.
While the overlay is on, Minecraft doesn't draw its world (`LevelRendererMixin`), so that never
happens. SkyCraft has the same mixin for the same reason. Your log (saved as
`work\user-death.log`) ends with "Minecraft not in a world" at 86.89 s.

**Tested how:** the mod builds; the class and method checked with javap (`isReady()`, private, in
26.3).

**Still broken / not tested:**
- **Not tested in Minecraft:** the respawn after a Mycelium death.
- **Joining a world** with Mycelium running now also skips that wait. The terrain may pop in a
  moment later in Minecraft's own window; that window isn't used for playing.

## 2026-10-19 Minecraft health follows Mycelium's (÷ 5)

**Changed:**
- **Protocol v16:** MycState 0x50 health, 0x54 max health (Mycelium units).
- **`MinecraftDriver.cs`:** reads Mycelium's `PlayerHealth` (found on 'PlayerParent') and sends it.
- **New `world/MycHealth.java`** and **`HealthFloorMixin`:** a player's Minecraft health stays at
  least 0.01 while Mycelium's is above 0.
- **`MyceliumCraftClient.syncHealth`:**
  - health and max health ÷ 5 on the integrated server;
  - kills the player when Mycelium's health has been 0 for 0.25 s;
  - respawns it when Mycelium has health again;
  - max health back to 20 outside Mycelium's levels.
- **Stand-ins:** FakeMycelium sends 37.5 of 150; linkTest checks it.
- PROTOCOL, DESIGN §5.0.2, README.

**Why:** you asked for Minecraft's health to follow Mycelium's (5 Mycelium health = half a heart,
max health too), and for Minecraft's health to reach 0 only when Mycelium's does.

**Tested how and result:**
- **Stand-ins:** "Mycelium health: got 37.5", "max health: got 150.0"; PASS.
- **The real build in the tutorial:** "found Mycelium's PlayerHealth on 'PlayerParent'", then
  "Mycelium 100.0 of 100.0 -> Minecraft 20.00 of 20.00".
- **The first frame read 0 of 0** while the player was set up. A max of 0 isn't synced, and a 0
  health now needs 0.25 s before it kills (added after seeing that).

**Still broken / not tested:**
- **The Minecraft side isn't tested in Minecraft at all:** setting health and max health, the floor,
  the death, the respawn.
- **Minecraft's death drops the player's items** (normal Minecraft). Not changed.
- **`/kill` can't kill you** while Mycelium's health is above 0 (the floor).

## 2026-10-19 Fall damage and regeneration off always, not just with Mycelium

**Changed:**
- **`MycRules.java`:** always on (no longer tied to Mycelium being alive and in a level).
- **`MyceliumCraftClient.java`:** no longer sets it; logs the rules once at start.
- Mixin comments, DESIGN §5.0.1, README.

**Why:** you said these don't need to depend on Mycelium; always on while the mod is installed is
fine.

**Tested how:** the mod builds.

**Still broken / not tested:**
- **Not tested in Minecraft** (as in the entry below).

## 2026-10-19 No fall damage and no natural regeneration in Minecraft (for combat)

**Changed:**
- **New `world/MycRules.java`:** on while Mycelium is alive and has you in a level. Logs prevented
  falls and heals (once a second at most).
- **New mixins (common, so the integrated server gets them):**
  - `FallDamageMixin` (`LivingEntity.causeFallDamage`, players only);
  - `PlayerFallDamageMixin` (`Player.causeFallDamage`);
  - `FoodDataMixin` (skips the two `heal` calls in `FoodData.tick`);
  - `ServerPlayerRegenMixin` (cancels `ServerPlayer.tickRegeneration`, Peaceful's healing).
- **`MyceliumCraftClient.java`:** sets `MycRules` every frame.
- DESIGN §5.0.1, README.

**Why:** you asked for fall damage and passive health regeneration off in Minecraft, in
preparation for combat.

**Why not Minecraft's game rules:** `fallDamage` and `naturalRegeneration` are saved into the world;
these changes only apply while Mycelium drives.

**Tested how:** the mod builds; the Minecraft names checked with javap
(`causeFallDamage(DFLDamageSource;)Z` on LivingEntity and Player, two `ServerPlayer.heal(F)` calls in
`FoodData.tick`, `ServerPlayer.tickRegeneration()`).

**Still broken / not tested:**
- **Not tested in Minecraft at all.** The fake Minecraft doesn't run Minecraft's code. Whether the
  mixins attach is only checked when Minecraft starts; if one doesn't, Minecraft won't start, and
  its log will say which.

## 2026-10-19 Overlay still hid on some hits (longer than before)

**Changed:**
- **`MinecraftDriver.MyceliumHasControls`:** a "canAct off" while `PlayerController.isInvincible`
  is set is reported as "canAct off, knockback".
- **The overlay** never hides for that, however long it lasts (the 0.3 s grace stays for other
  takeovers).
- DESIGN §5.4.

**Why:** you saw the overlay disappear for longer than before.

**Cause:** your log (saved as `work\user-hit-overlay-2.log`). A hit at 42.17 s held the controls for
0.42 s, past the 0.3 s grace, so the overlay hid from 42.46 to 42.59 s. Mycelium's
`PlayerController.Knockback` keeps the controls off until the "Hurt" animation is half done, which
varies: 16 ms in my tests, 0.42 s in yours. So a fixed grace can't cover it. The previous fix
assumed hits were always about 30 ms.

**Tested how:** the real build in the tutorial, minimized, fake Minecraft with its overlay picture,
`-mcHitTest`.

**Result:** "hold: Mycelium has the controls (canAct off, knockback)". The overlay stayed shown, no
"hidden" line.

**Still broken / not tested:**
- **My test hits are short (16 ms)**, so your long case isn't reproduced. It's not certain that your
  0.42 s hold was a knockback (that log predates the knockback label). If the overlay still
  disappears, your next log's "hold:" line will say why.

## 2026-10-19 Minecraft overlay blinked when the spore took damage

**Changed:**
- **`MinecraftDriver.cs`:** Mycelium taking the controls hides the overlay only after it has had
  them for 0.3 s (`OverlayControlsGrace`). Its pause still hides the overlay at once.
- DESIGN §5.4.

**Why:** you saw the overlay vanish for a frame on every hit.

**Cause:** your log (saved as `work\user-hit-overlay.log`). On each hit: "overlay: hidden (not
normal play)", "hold: Mycelium has the controls (canAct off)", then about 30 ms later "gave the
controls back" and "overlay: showing". The knockback switches Mycelium's "can act" off for that
moment.

**Tested how:** the real build in the tutorial, minimized, fake Minecraft with its overlay picture,
`-mcHitTest` (a knockback every 4 s).

**Result:** the knockback gave a 16 ms "canAct off" hold, and the overlay stayed on: no "hidden"
line through 3 hits.

**Still broken / not tested:**
- **Not tested with real hits in your play.**
- **A real Mycelium menu prompt or cutscene** now hides the overlay 0.3 s after it starts, instead of
  at once.

## 2026-10-19 Minecraft overlay no longer shows over Mycelium's loading screen

**Changed:**
- **New `ScreenCover.cs`:** is one of Mycelium's full-screen covers showing? Read by reflection:
  - SceneLoader's black, loading, startup and death canvas groups (alpha above 0.01) and its
    `isLoading`;
  - SceneLoadingManager's `isLoading`.
- **`MinecraftDriver.cs`:** the overlay stays off while one is showing. New `-mcLoadTest` test aid
  starts a scene change through Mycelium's own SceneLoader.
- DESIGN §5.4.

**Why:** you saw the Minecraft overlay on top of the loading screen when starting a run. It's
drawn above all of Mycelium's UI, and a loading screen didn't count as "Mycelium has the controls".

**Tested how:** the real build in the hub, minimized, fake Minecraft with its overlay picture,
`-mcLoadTest`.

**Result:**
- **The scene change:** overlay shown, then "kept off while Mycelium is loading a scene" at 15.61 s,
  through the load into the tutorial and its fade-in, then back on at 18.43 s ("screen cover is
  gone").
- **At game start:** also kept off "while Mycelium's loading screen is showing".
- All 4 cover screens and both `isLoading` flags were found.

**Still broken / not tested:**
- **Not tested with the real "start a run" flow** (Explore button).
- **The level-name title card** still shows under the overlay (on purpose).

## 2026-10-19 Held items and blocks on your Minecraft body

**Changed:**
- **`MycPlayerModel.java`:** `submitItem` and `submitBlockModel` now take the quads.
  - Each sprite they use is sent once as its own texture (`spriteTextureId`, first animation frame).
  - Texture coordinates are turned from the sheet's into the sprite's.
  - Tints are kept.
  - The texture sending is shared with the body (`sendPixels`).
- **New `SpriteContentsAccessor.java`:** reads a sprite's pixels.
- **Protocol v15:** the texture table holds 256 textures (data from 0x2000).
- DESIGN §5.4.1, PROTOCOL, README.

**Why:** you asked to see held items in third person.

**Tested how:** builds; stand-ins PASS. The real build in the hub with the fake Minecraft's test
body (texture through the new table): "texture 1 from Minecraft, 4x4", body shown, 24 triangles.

**Still broken / not tested:**
- **The held items themselves aren't tested:** the fake Minecraft can't hold items, so this needs
  real Minecraft.
- **Not done:** enchantment glint, animated item textures (first frame only), Fabric-mesh (modded)
  items.
- **Each new sprite stays in the shared memory** until Minecraft or Mycelium restarts (256 textures,
  8 MB). That's plenty for normal play.

## 2026-10-18 Sprint FOV and crouch camera, like Minecraft

**Changed:**
- **Protocol v14:** McState 0x84 FOV multiplier, 0x88 eye height ÷ standing.
- **New `CameraAccessor.java`:** reads Minecraft's camera `fovModifier`/`oldFovModifier` and
  `eyeHeight`/`eyeHeightOld`.
- **`MyceliumCraftClient.java`:** sends both, interpolated, with eye height relative to
  `getEyeHeight(Pose.STANDING)`.
- **`CameraSync.cs`:**
  - lens FOV = Mycelium's own × the multiplier (put back on release);
  - first-person eyes × the ratio;
  - third-person camera lowered by the same amount.
- **`LinkTestMain.java`:** `LINKTEST_FOVEYE=1`.
- PROTOCOL, DESIGN §5.2, README.

**Why:** you asked for Minecraft's sprint FOV change and its crouch camera drop.

**Tested how:** the real build, minimized, fake Minecraft with `LINKTEST_FOVEYE=1` (×1.15 FOV and
×0.78 eye from 4 to 7 s), with `-mcCameraTrace`:
- the behind view and first person in the tutorial;
- the behind view in the hub.

**Result:**
- **FOV:** "camera feel FOV x1.15, eye x0.78 (lens 69.0 degrees)", then back to 60.
- **First person:** camera 1.513 → 1.180 → 1.513 above the spore (exactly × 0.78).
- **Behind view, hub:** 2.114 → 1.792 → 2.125 above the spore, no collision push.
- **Behind view, tutorial:** the drop, then a further 0.66 from Mycelium's camera collision, which
  stayed after the crouch ended. That's the camera add-on reacting to a nearby surface, not this
  change.

**Still broken / not tested:**
- **Not tested with real Minecraft.** It should follow Minecraft's own smoothing, but that's not
  checked.

## 2026-10-18 Blurred Mycelium UI looked pixelated

**Changed:**
- **New `Resources/MyceliumCraftBlur.shader`:** a 9-tap Gaussian blur in one direction.
- **`McPauseBlur.cs`:** the UI picture is halved, then blurred across and down twice, instead of
  shrunk to 1/4 and stretched back.
- **`-mcBlurCheck`:** also logs roughness (how sharply brightness bends between neighbouring
  pixels).
- DESIGN §5.3.

**Why:** you saw the blurred UI as very pixelated. Stretching a 1/4-size picture back to full screen
shows steps on sharp edges like text.

**Tested how:** the real build in the hub, minimized, fake Minecraft with `LINKTEST_PAUSE=1`, pitch
−30, `-mcBlurCheck`.

**Result:**
- **Roughness of the HUD corner:** 0.38 blurred, against 5.9 sharp before the pause. The blurred
  world measures 0.55.
- The UI picture is 7% non-transparent.
- The corner still differs with and without the UI: (71,90,60) against (79,81,66).

**Still broken / not tested:**
- **Not tested in your play.**
- **The old pixelated version wasn't measured,** so there's no before-and-after roughness for it.
- **Blur strength** is about 6 screen pixels. Say if you want more or less.

## 2026-10-18 Blurred Mycelium UI vanished in front of the sky

**Changed:**
- **`McPauseBlur.cs`:** Mycelium's UI is no longer routed through the world's depth-of-field blur.
  - A hidden camera of ours draws its overlay canvases into a transparent picture.
  - The picture is shrunk to 1/4 and stretched back, and shown just under the Minecraft overlay.
  - Mycelium's render effects (SSAO, Fog, RenderIgnoreFogLayer) are switched off just while that
    camera draws.
- **`MinecraftDriver.cs`:** updates it every frame.
- **`MyceliumCraft.Runtime.asmdef`:** references `Unity.RenderPipelines.Universal.Runtime`.
- DESIGN §5.3.

**Why:** you saw the blurred UI blend into the sky and disappear.

**Cause:** the depth-of-field blur works by depth. UI has none, so it got the blur of the sky behind
it, and light parts dissolved into the bright sky. My first guess (Mycelium's fog) was wrong: the fog
is drawn before transparent things like UI.

**Tested how:** the real build in the hub, minimized, fake Minecraft with `LINKTEST_PAUSE=1`,
`-mcBlurCheck`, at camera pitch 20 and −30 (−30 puts sky behind the health bar).

**Result:**
- **First try, 1/8 shrink:** the UI picture was 100% solid and covered the screen. Mycelium's
  single renderer ran its Fog and SSAO on our camera too. Fixed by switching those off for that
  draw.
- **Now:** the picture is 5% non-transparent (just the HUD).
- **Health-bar corner over the sky:** (155,186,169) with the blurred UI, (217,236,239) without.
- **Level view:** (92,112,71) with, (109,108,82) without.
- **The world** still blurs (middle sharpness about 8 → 1.8).

**Still broken / not tested:**
- **Not tested in your play.**
- **The UI blur is milder** (1/4) than the world's, so light text keeps some outline. Say if you
  want it stronger.

## 2026-10-18 Mycelium's UI blurs too during Minecraft's pause

**Changed:**
- **`McPauseBlur.cs`:** while blurred, Mycelium's root overlay canvases (not ours) are drawn by the
  main camera just in front of everything, so the blur covers them. Their layers are added to the
  camera's culling mask. Everything is put back afterwards.
- **`-mcBlurCheck`:**
  - also measures the health-bar corner (sharpness and average colour);
  - while blurred, compares that corner with the UI hidden for one frame.
- DESIGN §5.3, README.

**Why:** you asked for Mycelium's UI to blur too. Screen effects skip overlay UI, which is what
Mycelium's HUD is.

**Tested how:** the real build in the hub, minimized, fake Minecraft with `LINKTEST_PAUSE=1`,
`-mcBlurCheck`.

**Result:**
- "9 of its UI canvases with it".
- **Health-bar corner sharpness:** about 20 before, 1.5–2.6 during, 18 after (the view had moved a
  little).
- **The blurred HUD is really drawn:** the corner's average colour was (108,122,88) with it and
  (120,125,91) with it hidden.
- **The world** still blurs (about 9 → 2).

**Still broken / not tested:**
- **Not tested with real Minecraft's pause menu, or in your own play.** The HUD's position and size
  while blurred weren't looked at; only that it's blurred and present.

## 2026-10-18 Mycelium blurs during Minecraft's pause menu

**Changed:**
- **New `McPauseBlur.cs`:** a global Volume with Mycelium's own "BlurVolume Profile", set like the
  one in Mycelium's PauseMenu prefab (global, priority 0, weight 1). It's on while Minecraft's pause
  menu is open.
- **`MinecraftDriver.cs`:** switches the blur with the pause freeze. New `-mcBlurCheck` test aid
  (screen sharpness twice a second).
- **`MyceliumCraft.Runtime.asmdef`:** references `Unity.RenderPipelines.Core.Runtime` (for `Volume`).
- DESIGN §5.3, README.

**Why:** you asked for Mycelium to blur during Minecraft's pause, as it does in its own pause.

**How Mycelium does it:** `PauseMenu.Pause()` switches on the PauseMenu prefab. That has a global
Volume with "BlurVolume Profile": a depth-of-field blur focused 0.1 units away, so everything is out
of focus, plus a colour adjustment. Its pause also mutes the sound and fades its HUD; those aren't
copied.

**Tested how:** the real build in the hub, minimized, fake Minecraft with `LINKTEST_PAUSE=1`,
`-mcBlurCheck`.

**Result:** screen sharpness was 8.2–9.6 before the pause, 2.1–2.5 during it, and about 10.2 after.
The blur came on and went off with the pause.

**Still broken / not tested:**
- **Not tested with real Minecraft's pause menu.**
- **Not copied from Mycelium's pause:** the sound muffle and the HUD fade.

## 2026-10-18 Windows' own mouse pointer in Minecraft's screens

**Changed:**
- **`InputBridge.cs`:** while a Minecraft screen is open, the pointer is unlocked and shown
  (`Cursor.lockState = None`, `visible = true`; again every frame), and its real position is sent.
  Afterwards it goes back to how Mycelium had it.
- **`McOverlay.cs`:** the drawn arrow is gone.
- **`MinecraftDriver.cs`:** no longer draws it.
- DESIGN §5.3, README.

**Why:** you asked for the mouse to be able to leave the window while the cursor shows, and for the
normal Windows pointer.

**Checked:**
- Mycelium locks the pointer only once, in `PlayerController` when the player is created; its
  `TestingManager` also does in the editor only. Nothing re-locks it every frame.
- The build runs the fake screen test without errors: overlay shown throughout, controls off during
  the screen.

**Still broken / not tested:**
- **The pointer itself isn't tested:** test runs keep the bridge off, so they never touch your mouse.
- **Clicking outside Mycelium's window** takes the focus away. The bridge stops, and the pointer is
  set back to locked (Windows applies it when you click back in, and then the screen frees it
  again).

## 2026-10-18 Overlay vanished when chat opened (my mistake in the previous change)

**Changed:**
- **`MinecraftDriver.MyceliumHasControls`:** "Player input map off" no longer counts when the map
  is off because a Minecraft screen is open (`KeyOwnership.SilencedHere`).

**Why:** you saw the whole overlay disappear on T.

**Cause:** turning Mycelium's controls off for Minecraft's screens (previous entry) also turned off
the Player map. The driver reads that as "Mycelium has the controls" (as in its own menus), so it
hid the overlay and switched the input bridge off. Your log at 84.31 s: "controls off", then
"input bridge: off", "overlay: hidden", "hold: ... (Player input map off)".

**My test missed it:** I only looked at the key lines, not the hold and overlay lines.

**Tested how:** same run as before (`-mcHoldKeys "R,V,Tab"`, `LINKTEST_PAUSE=1`), plus the fake
overlay picture (`LINKTEST_OVERLAY=1`), now also checking the hold and overlay lines.

**Result:**
- No hold during the screen.
- The overlay stayed shown from start to end. It hid only after the fake Minecraft stopped sending
  pictures.
- Controls still off during the screen (`[]`).

**Still broken / not tested:**
- **Not tested with your real chat.**

## 2026-10-18 Mycelium's controls off while a Minecraft screen is open

**Changed:**
- **`KeyOwnership.cs`:** `Silence` switches off every enabled action map of Mycelium's input
  assets while a Minecraft screen is open (again each frame), and the same maps back on afterwards.
  `Update` takes `mcScreenOpen`.
- **`InputBridge.cs`:** while a Minecraft screen is open, P and the Mycelium-bound keys and buttons
  go to Minecraft too.
- **`MinecraftDriver.cs`:** passes the screen state on.
- DESIGN §5.3, README.

**Why:** you saw Mycelium's hotkeys trigger while typing in Minecraft's chat.

**Tested how:** the real build, minimized, holding R, V and Tab (`-mcHoldKeys "R,V,Tab"`), with the
fake Minecraft's screen "open" from 4 to 7 s (`LINKTEST_PAUSE=1`).

**Result:**
- **Before:** Mycelium actions pressed `[HideStats, Interact, ShowStats, Subspecies_Skill]`.
- **Screen open:** "controls off (32 action maps)", then pressed `[]`.
- **Screen closed:** "controls back (32 action maps)". Stats showed again at once, since Tab was
  still held; Interact and the skill need a fresh press (button actions).

**Still broken / not tested:**
- **Not tested with your real chat.**
- **Possible side effect:** if Mycelium itself switched one of its maps off while a Minecraft screen
  was open, closing the screen switches it back on. Not seen.

## 2026-10-18 Overlay step 2: cursor in Minecraft's screens; Escape and P pause

**Changed:**
- **Protocol v13:**
  - McState flag `PAUSED` (bit 5);
  - MycInput flag `CURSOR` (bit 1), with the cursor position at 0x170/0x178 (0..1 of the picture).
- **`InputBridge.cs`:**
  - Escape is sent to Minecraft (P still isn't).
  - While a Minecraft screen is open, the mouse moves a cursor (starts in the middle, kept on
    screen) and its position is sent.
- **`McOverlay.cs`:** draws the cursor (a 12 × 19 arrow, scaled with the window).
- **`KeyOwnership.cs`:** Mycelium's Pause action loses its Escape binding while Minecraft drives
  (P stays).
- **`MinecraftDriver.cs`:** `FreezeForMinecraftPause` sets Mycelium's time scale to 0 while
  Minecraft's pause menu is open.
- **`MycInputBridge.java`:** absolute cursor moves into Minecraft's mouse handler while a screen is
  open.
- **`MyceliumCraftClient.java`:**
  - reports `PAUSED`;
  - freezes the world's ticking while Mycelium is paused.
- **Stand-ins:**
  - FakeMycelium sends a cursor; linkTest checks it.
  - New `LINKTEST_PAUSE=1`: Minecraft's pause menu "open" from 4 to 7 s.
- PROTOCOL, DESIGN §5.3, README.

**Why:** you asked for overlay step 2 (using Minecraft's screens from Mycelium), and for Escape to
pause with Minecraft's menu and P with Mycelium's.

**Tested how and result:**
- **Stand-ins together:** cursor flag, x 0.25, y 0.75 read back exactly; ground checks PASS, PASS.
- **Real Mycelium, minimized, fake Minecraft with `LINKTEST_PAUSE=1`:**
  - "time stopped" at 14.57 s, "runs again" at 17.58 s (the 3 s of the fake pause).
  - Holding Escape (`-mcHoldKeys "Escape,Escape"`): Mycelium's Pause binding is `[<Keyboard>/p]`,
    and no Pause press.
- **First attempt didn't count:** `-mcHoldKeys Escape` was read as the letters E,S,C,A,P,E, so it
  held P. That correctly paused Mycelium (`[Pause]`) and hid the rest of the test. Use a comma for
  key names.

**Still broken / not tested:**
- **Not tested in real Minecraft:**
  - clicking in Minecraft's screens with the cursor;
  - Escape opening Minecraft's pause menu;
  - Minecraft's world freezing during Mycelium's pause.
- **The cursor can't be tested here:** the bridge is forced off in test runs, so it never uses your
  keyboard and mouse.
- **Escape inside Mycelium's own menus** does whatever Mycelium does with it. It doesn't reach
  Minecraft there.

## 2026-10-18 Invisible wall in front of the run start (hub)

**Changed:**
- **`GroundScanner.cs`:**
  - When the short upward ray finds no underside, a second ray goes right up to the surface. A hit
    at least 0.01 below the surface is a real (thin) underside.
  - The "no underside" threshold went from 0.05 to 0.01. The scan summary counts thin undersides.
- **New test aids:**
  - `-mcColumn "x,z;..."` logs what the scan finds in a column (`GroundScanner.DescribeColumn`);
  - `-mcColliders` lists solid colliders with nothing visible drawn (`ColliderSurvey.cs`).
- DESIGN §5 and §7.

**Why:** you couldn't reach the trigger that starts a run: an invisible wall stood in front of it
in Minecraft.

**How I found it:**
1. **Your first hub log** (saved as `work\user-hub-geometry.log`): no clear spot.
2. **`-mcColliders`:** listed Mycelium's own invisible fences (`InvisibleWall` ×12,
   `InvisibleBarrier`, `StartRunBarrier`). The spore collides with their layers, so in principle
   they belong in Minecraft. That was a side track.
3. **Your second log** (`work\user-hub-wall.log`), where you pushed into the wall: Minecraft
   stopped at x −8.80, z −1.5.
4. **The grid there:** one column (x −9.5..−9.0, z −2.0..−1.5) was solid from the ground to 2.98
   blocks. Its neighbours had a rock floating at 2.4–3.9 blocks with open space below.
5. **`-mcColumn` on that column:** a rock `Rok_low` with its top at 4.171 units and its underside
   at 4.140, only 0.03 thick there. The upward ray stops 0.15 short, so the underside was missed,
   and the "fill small gaps under one-sided surfaces" rule turned 0–4.17 units into solid.

**Result:**
- **That column now:** (2.96, 2.98) blocks, a thin rock edge with open space below.
- **Hub:** 103 thin undersides found. 55 cells around the run start (x −14 to −12.5) got more
  open space near the ground.
- **Your walked spots** (where the spore stood), checked against the new grids:
  - hub: 0 of 272 and 0 of 17 closed;
  - tutorial (152 thin undersides): 0 of 187, 74, 28 and 90 closed.

**Still broken / not tested:**
- **Not tested in your real play:** whether you now reach the trigger.
- **Climbable fences:** Mycelium's invisible fences are still in Minecraft. The big rounded
  `InvisibleBarrier` can be climbed like stairs in Minecraft (your first hub log, about 84 s, up to
  9.75 blocks high), while the spore can't climb it in Mycelium. Not fixed.

## 2026-10-18 Your Minecraft body's shadow in first person too

**Changed:**
- **Protocol v12:** the Model block's flags have `FIRST_PERSON` (bit 1).
- **`MycPlayerModel.java`:** sends the body in every view, marked `FIRST_PERSON` in first person.
- **`McPlayerModel.cs`:** in first person the body is drawn as shadow only (`ShadowsOnly`). The
  spore stays fully off in every view while the body is there.
- **`LinkTestMain.java`:** the test body is sent in first person too.
- **`-mcModelCheck`:** skips the depth part in first person. Its "shadow off" state now hides the
  body completely there.
- PROTOCOL, DESIGN §5.4.1, README.

**Why:** you asked for the correct shadow in first person too.

**Tested how:** the real build in the hub with the fake Minecraft in first person, then the
stand-ins together.

**Result:**
- **Looking 30° down:** the shadow's spot was off screen (not usable).
- **Looking 80° down:**
  - brightness 46 where the shadow should fall, with the body's shadow;
  - 80 without (sunlit grass);
  - spore: 0 renderers drawn.
- **The body is invisible from the eyes:** the reading with the shadow is shaded grass, (57,59,23).
- **A flaw in my test aid, found and fixed:** at first the "shadow off" reading was grey
  (195,194,193). The test's "no shadow" mode draws an object normally, so the camera saw the
  inside of the test body. Real play never uses that mode in first person.
- Stand-ins: ground checks PASS, PASS.

**Still broken / not tested:**
- **Not tested in your real play.**
- **The body still doesn't receive shadows** from trees and other objects.

## 2026-10-18 The shadow is your Minecraft body's, not the spore's

**Changed:**
- **`Resources/MyceliumCraftModel.shader`:** new "ShadowCaster" pass. It uses URP's depth bias and
  leaves out the skin's see-through pixels.
- **`McPlayerModel.cs`:** the body casts shadows.
- **`CameraSync.cs`:** while the body is shown, the spore's renderers aren't drawn at all
  (`forceRenderingOff`), instead of shadow only. In first person it's still shadow only.
- **`-mcModelCheck`:**
  - first switches the body's shadow on and off, reading the screen where the shadow should land;
  - then does the depth check as before.
  - It finds the active directional light: the hub's scene "sun" setting points at an inactive
    "Dawn Light".
- DESIGN §5.4.1, README.

**Why:** you saw the spore's shadow under your Minecraft body.

**Tested how:** the real build in the hub (`-mcScene "The Carcass" -mcBackground -mcModelCheck`)
with the fake Minecraft's test body, behind view, at two camera angles.

**Result:**
- **Where the shadow should land:** brightness 54–60 with the body's shadow, 89–98 without.
- **Spore:** 0 renderers drawn throughout ("3 spore renderers not drawn at all").
- **Fog check still holds:** depth 2.72 with the passes, 6.35 without.
- **First attempt didn't measure anything:** it looked for the light before the hub had loaded,
  then used the inactive Dawn Light. Fixed in the test aid.

**Still broken / not tested:**
- **Not tested in your real play.**
- **The body doesn't receive shadows** (for example from trees). Neither did it before.
- **First person** still casts the spore's shadow, since Minecraft sends no body in first person.

## 2026-10-18 Minecraft body washed out in front of the sky

**Changed:**
- **`Resources/MyceliumCraftModel.shader`:** added the "DepthOnly" and "DepthNormals" passes.
- **New `-mcModelCheck` test aid** (`MinecraftDriver.cs`) and `Resources/MyceliumCraftDepthProbe.shader`.
  It reads the scene depth at the body's head, with those passes switched off and on in turn.
- DESIGN §5.4.1.

**Why:** you saw the body blend into the sky in third person (screenshot).

**Cause:**
- Mycelium's High Fidelity renderer runs SSAO from depth and normals. So Unity fills its depth
  texture from separate passes that every object needs, and the body's shader had none.
- Mycelium's Fog effect (`Depth.shadergraph`: up to 75% white, by the depth texture's distance)
  therefore fogged the body as if it were whatever was behind it. Behind it was sky, so the fog
  was full.

**Tested how and result:**
- **Screen colour (2 attempts, inconclusive):** read back the screen colour at the body's head with
  the passes off and on, in the tutorial and the hub at 6 camera angles. Nothing differed. None of
  those views had sky behind the head (just above it was dark rock, wall or foliage), and the fog
  hardly changes at 10–30 units.
- **Depth probe (decisive):** read the depth texture itself at the head's pixel, in the hub. The
  head's centre was 3.66 units away.
  - passes on: 3.24 and 3.17 (the body's front face);
  - passes off: 10.86 and 30.90 (the wall behind).
- So the body is now in the depth that the fog reads.

**Still broken / not tested:**
- **Not tested in your real play** in front of the sky. I couldn't find a test view with sky right
  behind the body.
- **SSAO** now also sees the body. It may darken the ground around its feet a little, as it does
  for other objects.

## 2026-10-18 Your Minecraft body instead of the spore in third person

**Changed:**
- **Protocol v11:** new Model block (the posed body as triangles) and Textures block, after the
  overlay slots. The mapping is now about 105 MB.
- **New `MycPlayerModel.java`:** takes Minecraft's own posed player model (skin, armour, cape,
  animations) as triangles and sends it with its textures, in third person. Adapted from the player
  part of SkyCraft's `AvatarExporter`.
- New accessor mixins `RenderTypeAccessor`, `RenderSetupAccessor`, `TextureBindingAccessor`,
  `TextureManagerAccessor` (from SkyCraft), to find each part's texture.
- **New `McPlayerModel.cs`** and `Resources/MyceliumCraftModel.shader`: draws the body at the
  spore's feet.
- **`CameraSync.cs`:** the spore is drawn as shadow only while the body is shown (`ReplaceSpore`),
  as in first person.
- New `playerModel=` setting (default 1). `LinkTestMain.java`: `LINKTEST_MODEL=1` test body.
- PROTOCOL, DESIGN §5.4.1, README.

**Why:** you asked for the Minecraft player to be there instead of the spore in third person.

**Tested how:** the real Mycelium build, minimized, with the fake Minecraft sending its test body
through first person, then behind, then in front. Then the stand-ins together.

**Result:**
- **First person:** no body, spore shadow only.
- **Third person (both views):** the body shown and the spore's 4 renderers shadow only.
- **Shape:** box from (−0.42, 0.00, −0.49) to (0.42, 1.68, 0.21) units around the spore's feet,
  as expected from 1.4 × the block size. The nose is on −Z, which is Minecraft's +Z (the facing
  side).
- 60 updates a second.
- Stand-ins: ground checks PASS, PASS.

**Still broken / not tested:**
- **Not tested in real Minecraft:**
  - capturing the real player model and its skin and armour textures;
  - whether the skin comes out right side up.
- **Not built:**
  - held items and blocks (they need Minecraft's texture atlases, as in SkyCraft);
  - enchantment glint;
  - the body's own shadow (the spore's shadow is still cast).
- **Lighting:** the body is shaded Minecraft-style, not by Mycelium's lights. In a dark cave it
  may look too bright.

## 2026-10-18 Overlay, step 1: Minecraft's picture on top of Mycelium

**Changed:**
- **Protocol v10:**
  - The shared memory grows to about 96 MB: 3 picture slots after the first 1 MB.
  - New Overlay block at 0x600 (a triple buffer, as SkyCraft's).
  - New `OVERLAY` flag in Mycelium's state.
- **New `MycOverlay.java`:** copies Minecraft's picture back from the graphics card after every
  frame and ships it. Adapted from SkyCraft's `FrameExporter`.
- **New `LevelRendererMixin.java`:** Minecraft skips drawing its world while the overlay is on.
- **New `McOverlay.cs`** and `Resources/MyceliumCraftOverlay.shader`: draws the picture over
  Mycelium's screen. New `overlay=` setting (default 1).
- `MyceliumCraft.Runtime.asmdef`: unsafe code allowed (needed for the shared hand-over counter), and
  `UnityEngine.UI` referenced.
- `.gitignore`: `*.shader` added to the whitelist (a source file type).
- `LinkTestMain.java`: `LINKTEST_OVERLAY=1` test picture. New `-mcOverlayCheck` test aid.
- PROTOCOL, DESIGN §5.4, README.

**Why:** you asked to start on the overlay, so Minecraft's inventory and HUD show in Mycelium's
window.

**Tested how:** real Mycelium build, minimized (`-mcScene "New Tutorial" -mcBackground
-mcOverlayCheck`), with the fake Minecraft sending the test picture. Then the stand-ins together.

**Result:**
- Mycelium's screen, read back:
  - **Overlay on:** red box (255,0,0); half white box (128–129), the correct blend over the dark
    background; the empty spot unchanged.
  - **Overlay off:** all three show only Mycelium.
- 60 pictures a second, 0.03–0.08 ms each to upload (640×360).
- Stand-ins: ground checks PASS, PASS.

**Still broken / not tested:**
- **Not tested in real Minecraft:**
  - the capture and the world skip (the code follows SkyCraft's on the same Minecraft 26.3);
  - whether Minecraft's background really comes out transparent;
  - the upload time at full window size.
- **The picture is stretched** if Minecraft's and Mycelium's windows have different shapes.
- **No cursor in Minecraft's screens yet**, and Escape doesn't reach Minecraft. That's step 2.
- Minecraft's own window shows only its HUD over black while the overlay is on.

## 2026-10-17 Mycelium's keys in Minecraft's Controls menu

**Changed:**
- **New `MyceliumKeys.java`:** registers Mycelium's 11 actions as Minecraft key binds (Fabric's
  `KeyMappingHelper`), in a "Mycelium" section of Options → Controls → Key Binds. Labels in
  `assets/myceliumcraft/lang/en_us.json`. Sends what they're bound to every frame.
- **Protocol v9:** new Bindings block at 0x500 (Minecraft → Mycelium). Both sides need the new
  build.
- **`KeyOwnership.cs`:** applies the binds Minecraft sends instead of a fixed list, and re-applies
  them when they change. Defaults until Minecraft sends any.
- **`InputBridge.cs`:** doesn't send the keys and buttons bound to Mycelium actions (instead of a
  fixed R and Tab). Escape and P stay Mycelium's.
- **`LinkTestMain.java`:** `LINKTEST_BINDS` and `LINKTEST_BINDS2` (switched to 5 s in).
- PROTOCOL, DESIGN §5.3, README.

**Why:** you asked to edit both Minecraft's and Mycelium's key binds from Minecraft's options
menu, in preparation for the overlay.

**Tested how:** the real Mycelium build (`-mcScene "New Tutorial" -mcBackground -mcHoldKeys
"R,F,G,Tab,V,Space"`) with the fake Minecraft sending interact=F, dodge=G, subspecies=V, show
stats unbound, then after 5 s interact=R, show stats=Tab, salvage=middle click.

**Result:**
- Before the switch, Mycelium reacted with `[Dodge,Interact,Subspecies_Skill]`: F, G and V, not R,
  Tab or Space. Not sent to Minecraft: scancodes 9, 10, 25.
- After the switch the new binds were applied to all 11 input copies within a frame, and Tab
  showed stats. Not sent to Minecraft: scancodes 21, 43 and middle click.
- **R didn't fire Interact after the switch.** I think that's the test, not the feature: R was
  already held when it became the bind, and Unity only reacts to a key going down. Not checked
  further.
- Both builds compile; the mod jar contains the key binds and labels.

**Still broken / not tested:**
- **Not tested in real Minecraft:** the Mycelium section in the Controls menu, saving, and Reset.
- **Not tested:** that the bridge really holds back the Mycelium keys. The bridge is forced off
  in test runs so it can't use your keyboard.
- Middle click is now Mycelium's salvage only; Minecraft's pick block no longer gets it (it did
  both before). Change either bind in the menu if you want it back.

## 2026-10-17 One-frame third-person flash in first person

**Changed:**
- **`MinecraftDriver.cs`:**
  - Bridges hiccups: up to 0.25 s of an unreadable Minecraft state or "not in a world", using the
    last good state and logging `hiccup:`.
  - The camera keeps following Minecraft through teleports and hiccups.
  - New `-mcHitTest` test aid: `PlayerController.Knockback()` every 4 s.
- **`CameraSync.cs`:**
  - Gives the camera back only after 0.5 s of Minecraft not driving.
  - New flash check, right before drawing, which logs `camera FLASH`.
- **`LinkTestMain.java`:** confirms and applies every teleport, like real Minecraft. New
  `LINKTEST_HICCUP=1`.
- DESIGN §5.2.

**Why:** you reported that when a beetle hit you, and very rarely at other times, the camera went
to a third-person view for a single frame.

**Tested how:**
1. Read your log (saved as `work\user-flash.log`) and Mycelium's hit code (`PlayerTakeDamage`,
   `Knockback`, `HitStopManager`), plus Cinemachine's brain.
2. Ran `-mcHitTest -mcBackground -mcFps 60` with the fake Minecraft in first person, then again with
   `LINKTEST_HICCUP=1`.

**Result:**
- **Your log showed both causes:**
  - **Beetle hit (20:22:23.522):** the knockback ended and the spore had moved, so
    `teleport ... (Mycelium moved the spore 0.5 units while it had the controls)`, and in the same
    instant `camera: back to Mycelium's mouse and orbits`. The camera was taken back 48 ms later.
  - **Random (20:21:12):** `phase Driving -> Idle (Minecraft not in a world)` for a moment, which
    released the camera.
- **Ruled out:** Cinemachine fires its camera-updated event every time it places the camera, so
  that wasn't it.
- **After the fix:**
  - 4 knockbacks with resync teleports (`Mycelium moved the spore 1.1 units`)
  - 7 hiccups bridged
  - **0 camera releases, 0 drops to Idle, 0 flash frames of 713 checked**
- Stand-ins PASS. 0 focus events in the test runs.

**Still broken / not tested:**
- **Not tested in your real play.** If a flash happens, the log now says `camera FLASH: frame …`
  with details.
- **Why Minecraft occasionally reports "not in a world" isn't known.** It's bridged now, and the log
  counts how often (`hiccup:`).
- Uncommitted, with the key ownership.

**Next:** you test.

### Test procedure

1. **No jar copy needed:** only Mycelium changed.
2. Start Minecraft, then
   `work\build\MyceliumCraft.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720`,
   and play in first person. Get hit by a beetle a few times.
3. **You should see:** no flash of the third-person camera.
4. **In `work\build\MyceliumCraft.log`:**
   - no `camera FLASH` lines
   - no `camera: back to Mycelium's mouse` while you're playing
   - after hits, `teleport ... (Mycelium moved the spore ...)` is fine
   - an occasional `hiccup:` line is fine, and tells me how often it happens

## 2026-10-17 Which game owns which key

**Changed:**
- **`mycelium/Runtime/KeyOwnership.cs` (new):** while Minecraft drives, it rebinds Mycelium's
  "Player" actions in every copy of its input asset, using Input System binding overrides:
  - Attack, Curio, Dodge, Skill 1, Skill 2, Navigate camera and Reset camera are unbound.
  - Interact moves from right click to R.
  - Show/Hide stats move from Space to Tab.
  - It switches off Mycelium's developer cheat panel (`TestingManager`).
  - It undoes all of this when Minecraft stops driving.
  - It logs which Mycelium actions are pressed.
- **`InputBridge.cs`:** R and Tab are no longer sent to Minecraft.
- **`McConfig.cs`:** new `keyOwnership=1`. Added to your `work\build\MyceliumCraft.cfg`.
- **`MinecraftDriver.cs`:** `-mcHoldKeys` now also takes key names (`"space,leftShift,tab"`).
- DESIGN §5.3.
- Before this: committed the input bridge and camera work as `fb8e46c`.

**Why:** your key list:
- **Minecraft:** left click (attack), right click (use), Shift (sneak), Space (jump), E (inventory),
  Q (drop), Ctrl (sprint).
- **Mycelium:** R (interact), Tab (show stats).
- **Unbound:** dodge, skills 1 and 2, navigate camera, reset camera, player list.

**Tested how:**
1. Read Mycelium's bindings and searched its scripts for direct key reads.
2. Held E, Q, Space, Shift, Ctrl, Tab and R in Mycelium (`-mcHoldKeys`, `-mcBackground`), with
   `keyOwnership=0` and `=1`.

**Result:**
- **11 copies** of Mycelium's input asset were found and changed, 10 bindings each:
  `Attack=[], Curio=[], Interact=[<Keyboard>/r], Dodge=[], Stat_Skill_1=[], Stat_Skill_2=[],
  NavigateCamera=[], ResetCamera=[], ShowStats=[<Keyboard>/tab <Keyboard>/backspace]`.
- **Actions that reacted:** with `keyOwnership=0`, `[Dodge, HideStats, NavigateCamera, ResetCamera,
  ShowStats, Stat_Skill_1, Stat_Skill_2]`. With `keyOwnership=1`, only `[HideStats, Interact,
  ShowStats]` (R and Tab).
- **Direct key reads:** only Mycelium's developer cheat panel (`TestingManager`) reads keys
  directly: digits 1–0, K, L, Z, X and Minus. It's in `ManagerBundle`, so it's live in builds, and
  digits are Minecraft's hotbar.
- **0 focus events** in the test runs.

**Still broken / not tested:**
- **Not tested with real Minecraft:** clicks, Shift, Space, E, Q and Ctrl doing their Minecraft
  thing from Mycelium's window.
- **The cheat panel switch-off isn't seen in my test.** `-mcScene` skips the main menu, where that
  manager may be loaded. Look for the log line in your play (below).
- **Nothing damages Mycelium's enemies right now.** Mycelium's attack is unbound, and Minecraft's
  attack has nothing to hit until combat (Phase 3).
- **V and middle click** weren't in your list: both games still react to them (Mycelium: subspecies
  skill, salvage; Minecraft: none, pick block).
- Uncommitted.

**Next:** you test. Then the later-level checks, or combat.

### Test procedure

1. **No jar copy needed:** only Mycelium changed.
2. Start Minecraft, then
   `work\build\MyceliumCraft.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720`,
   and play from Mycelium's window.
3. **You should see:**
   - **Minecraft's actions:** clicking attacks or uses in Minecraft, with no Mycelium attack swing.
     Shift sneaks with no roll, Space jumps without showing stats, E opens Minecraft's inventory
     (in Minecraft's window), Q drops, and Ctrl sprints.
   - **Mycelium's actions:** R interacts with Mycelium things (where right click used to), and
     holding Tab shows Mycelium's stats.
   - **The hotbar:** 1–9 change Minecraft's hotbar slot without Mycelium cheats firing.
4. **In `work\build\MyceliumCraft.log`:**
   - `keys: Mycelium input asset #… 10 bindings changed`
   - `keys: Mycelium's developer cheat keys (TestingManager) off while Minecraft drives`
   - `keys: Mycelium actions pressed now: [...]`, which should only ever list Interact, ShowStats or
     HideStats (or Subspecies_Skill / Salvage for V / middle click)

## 2026-10-17 Camera jitter: Mycelium's own mouse input was still moving the camera

**Changed:**
- **`CameraSync.cs`:**
  - While Minecraft drives, unplugs the `CinemachineInputProvider` from Mycelium's camera axes and
    clears their built-up speed (`AxisState.Reset()`). Plugs it back in afterwards.
  - Logs whether the provider was found.
- **`MinecraftDriver.cs`:** new `-mcMouseTest` test aid, which moves the mouse up and down
  through Unity's Input System, as your real mouse does in Mycelium's window.
- DESIGN §5.2.

**Why:** you reported the jitter still there and recorded a trace of your own play
(`-mcCameraTrace`). It's saved as `work\user-trace-1.csv` and `.log`.

**Tested how:**
1. Analysed your trace frame by frame.
2. Rebuilt the angle-to-height conversion offline from the curve in your log and compared.
3. Read Cinemachine's `AxisState` and `CinemachineFreeLook` code.
4. Checked the camera prefab for components by their script ID.
5. Reproduced with `-mcMouseTest -mcBackground -mcFps 60`, then fixed and re-measured.

**Result:**
- **In your trace:** Mycelium ran at a steady 60 fps, Minecraft at ~120. The final view angle was
  fairly even (0.17), but the camera's orbit height, height and distance were not (0.93–0.94).
  The orbit height ran ahead of the angle: +0.02 looking down, −0.02 looking up, up to 0.065 off.
  The big single jumps were your F5 switches.
- **Ruled out:** the camera never froze while the look moved (0 frames), so it isn't Cinemachine's
  update timing.
- **Cause:**
  - Mycelium's camera has a `CinemachineInputProvider` (script ID `fb7194dc…`) with
    `AutoEnableInputs`. It feeds the mouse ("Look") into the axes even with the axis names blank.
  - With the bridge, Mycelium has the focus, so your real mouse moved Mycelium's camera too.
  - Before the bridge Mycelium never saw the mouse, which is why it started with the bridge.
- **Reproduced:** with fake mouse movement, orbit height evenness 0.50, camera height 0.40, camera
  distance 0.54.
- **After unplugging:** 0.21 / 0.16 / 0.25. Orbit height off from the angle: worst **0.0016**
  (was 0.035 in the test, 0.065 in your play).
- **Test windows:** the runs used `-mcBackground`, with 0 focus events.

**Still broken / not tested:**
- **Not tested in your real play.** If anything still jitters, another `-mcCameraTrace` run will
  show it.
- Your trace also showed uneven turning (view yaw 0.76). The same provider fed the X axis too, so
  this should fix that as well. Not measured separately.
- Uncommitted, with the input bridge and all fixes since.

**Next:** you test.

### Test procedure

1. **No jar copy needed:** only Mycelium changed.
2. Start Minecraft, then
   `work\build\MyceliumCraft.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720`,
   and play from Mycelium's window in third person.
3. **You should see:** moving the mouse up and down while looking down, and turning, are smooth.
4. **In `work\build\MyceliumCraft.log`:**
   - `camera: found FreeLook ... input provider on it: CinemachineInputProvider (enabled)`
   - `camera: following Minecraft's look (... input provider unplugged)`
5. **If it still jitters,** add `-mcCameraTrace` again and tell me.

## 2026-10-17 Camera jitter looking down (collision add-on), and out-of-the-way test runs

**Changed:**
- **`CameraSync.cs`:** while Minecraft drives, raises Mycelium's `CinemachineCollider`
  `DampingWhenOccluded` (0) to `cameraCollisionSmoothing`, and restores it afterwards.
- **`ShakeTap.cs`:** also records the collision push, for the trace (new `collisionPush` column).
- **`McConfig.cs`:** new `cameraCollisionSmoothing=0.2`. Added to your
  `work\build\MyceliumCraft.cfg` at 0.2.
- **`BackgroundWindow.cs` (new test aid, `-mcBackground`):**
  - minimizes test runs, which hands the focus back
  - repeats for 10 s and whenever Mycelium gets the focus
  - in this mode the input bridge is always off
- **`LinkTestMain.java`:** new `LINKTEST_STILL`, `LINKTEST_PITCH_WAVE`, and uneven up/down look
  with `LINKTEST_TURN`.
- **`tools/evenness_check.py` (new).**
- **Log:** the orbit curve.
- DESIGN §5.2 and §7.

**Why:** you reported that the third-person jitter was still there, when moving the camera up and
down while looking down. You also asked that my test runs keep Mycelium's window unfocused and
behind other windows.

**Tested how:**
- Traced (`-mcCameraTrace -mcFps 60 -mcBackground`) the fake Minecraft standing still in "behind"
  view, looking smoothly up and down between 30° and 80°, then with uneven steps like bridged mouse
  movement.
- Checked the orbit curve. Measured evenness of the view angle, orbit height, camera height and
  distance, and the collision push.

**Result:**
- **Orbit curve:** smooth and always increasing (`points where it turns back: 0`), so it's not the
  cause.
- **Smoothing:** works in my tests. With uneven input (0.72), the view comes out 0.14 at
  `cameraSmoothing=0.05`, and 0.08 at 0.1.
- **Cause found:** Mycelium's collision add-on snaps the camera in front of a wall instantly. At the
  tutorial start it pushed on 372 of 785 frames, up to 0.7 units, mostly while looking down 40–60°.
  - With the 0.2 s ease-in: biggest one-frame push change 0.445 → **0.097** units; frames jumping
    over 0.05 units **26 → 4**; biggest camera height jump 0.225 → **0.098**.
- **Test windows:**
  - **My mistake:** the first `-mcBackground` version only pushed the window back, and Windows gave
    it the focus anyway. **For 13 s (18:20:49–18:21:02) Mycelium had your keyboard and mouse, with
    the bridge on.**
  - **Now:** it minimizes. The log shows `minimized 1 times ... Mycelium focused now: False`. In
    later runs Windows still handed it the focus once at launch, and it handed it straight back,
    with the bridge off.

**Still broken / not tested:**
- **Not tested in your real play.** My tests only reproduce part of what you see. If it's still
  jittery, a trace from your own play will show the rest (step 4 below).
- The camera can show a wall's edge for a moment before moving in front of it. Lower
  `cameraCollisionSmoothing` if that bothers you more than the jumps.
- Uncommitted, with the input bridge and the earlier fixes.

**Next:** you test.

### Test procedure

1. **No jar copy needed:** only Mycelium changed this time. Your jars from the previous step are
   current.
2. Start Minecraft, then
   `work\build\MyceliumCraft.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720`,
   and play from Mycelium's window.
3. **You should see:** in third person, looking down from above and moving the camera up and down,
   especially near walls and trees, the camera glides instead of jumping.
4. **If it still jitters,** run once with `-mcCameraTrace` added to that command:
   - Reproduce it for a few seconds, then close Mycelium.
   - `work\build\camera_trace.csv` then has every frame of your real play, and I can measure exactly
     what moved.
   - Don't add `-mcBackground`: it's for my tests, and it switches the input bridge off.
5. **In `work\build\MyceliumCraft.log`:** `camera: collision add-on found (... ) ease-in when
   occluded 0 -> 0.2 s`.

## 2026-10-17 Spore facing flicker, and juddery movement (protocol v7 + v8)

**Changed:**
- **Protocol v7:** McState 0x3C carries Minecraft's body yaw.
- **Protocol v8:** McState 0x40.. carries the raw ticks: previous and latest feet, previous and
  latest body yaw, milliseconds per tick, and the latest tick's QueryPerformanceCounter time.
  Updated in `PROTOCOL.md`, both `Proto` files and both links.
- **Minecraft:**
  - `MyceliumCraftClient` publishes the tick.
  - `MinecraftMixin` stamps the frame-start clock (`frameStart()`).
  - `MycLink` gains `qpc()` and `qpcFrequency()`.
- **Mycelium:**
  - `Link/TickHistory.cs` (new): the last 16 ticks as Minecraft draws them.
  - `MinecraftDriver.cs`:
    - each physics step targets Minecraft's position at that step's moment and lands on it
    - the spore faces Minecraft's body
    - Mycelium's own turning (`looking`) is off while driving
    - the status line shows `ticks=`
  - `MyceliumLink`: new `Qpc()` and `QpcFrequency`.
- **Test aids:**
  - `-mcHoldKeys <letters>` fakes held keys in Mycelium.
  - `LINKTEST_STRAFE`, `LINKTEST_VIEW`, `LINKTEST_PITCH`.
  - The fake Minecraft now moves in 20-per-second ticks.
  - The trace gains spore and target columns.
  - New `tools/facing_check.py` and `tools/screen_jitter_check.py`.
- DESIGN §4.1.

**Why:** you reported two things:
- the third-person camera was still a bit jittery, especially looking down from above
- the player model "glitched" when moving in any direction except forward

**Tested how:** traced the "behind" view at pitch 60 and 85, with Mycelium at 60 fps and the fake
Minecraft at ~120 fps, strafing, with D faked as held in Mycelium. Measured the spore's facing, its
on-screen wobble and its speed, frame by frame.

**Result:**
- **Facing glitch:** Mycelium's own `PlayerController.LookAt` turned the spore toward its WASD,
  which it now receives through the bridge, while my code turned it to Minecraft's look.
  - With D held: **75 direction reversals** in 9 s and up to 15.8°/frame. Without D: 0.
  - After the fix (Mycelium's turning off, facing Minecraft's body): steady 135°, **0 reversals**.
- **Judder:** the camera itself was steady; the spore moved unevenly. Its speed jumped between
  1.8 and 4.1 u/s at a constant walk (unevenness 0.159), and the camera follows it.
  - **First try** (latest two ticks): broke it. `a=0.00` on every step, so the spore froze between
    ticks. I blamed physics lag and added the 16-tick history.
  - **Second try:** still `a=-13,612,480`. The real cause was that Unity's `Stopwatch` isn't
    Windows' QueryPerformanceCounter (about 7.9 days apart). Mycelium now calls QPC itself.
  - **Now:** open-ground speed 2.62–2.92 u/s, **unevenness 0.011**. The spore moves on every frame
    (533/533), and on-screen wobble is 0.017° (was 0.07–0.20°).
- Stand-ins: ground checks PASS, FakeMycelium PASS.

**Still broken / not tested:**
- **Not tested with real Minecraft:** the real tick timing (`frameStart` and partial) and how it
  feels.
- The spore lags Minecraft's drawn position by about one physics step (20 ms) plus the one-tick
  delay Minecraft itself has.
- Unevenness against walls is Mycelium's collision; not changed.
- Uncommitted, together with the input bridge and the third-person fix.

**Next:** you test.

### Test procedure

1. **Copy the jars:** both sides changed (protocol v8), so copy them again:
   `copy /Y dist\minecraft-mods\*.jar "C:\Users\Ryan\AppData\Roaming\PrismLauncher\instances\Mycelium 26.3\minecraft\mods\"`
2. Start Minecraft, then Mycelium, and play from Mycelium's window.
3. **Movement:** walk in all directions in third person (F5). **You should see:** the spore doesn't
   flicker when strafing or walking backwards. It turns its body partly toward the walk, like
   Minecraft's player.
4. **Looking down:** look down from above while walking. **You should see:** the ground slides past
   smoothly.
5. **In `work\build\MyceliumCraft.log`:**
   - `facing: Mycelium's own turning (PlayerController.looking) off while Minecraft drives`
   - status lines with `ticks=yes a=...`, where `a` is a small number around −1 to 0.5. A huge
     number would mean a clock problem.

## 2026-10-17 Third-person camera jitter with the input bridge

**Changed:**
- **`CameraSync.cs`:** the eased yaw is now used for the third-person heading too, as it already
  was in first person.
- **`FramerateLimitTrackerMixin.java` (new):** while bridged, Minecraft uses your own frame-rate
  setting instead of throttling as "inactive".
- **`MycInputBridge.java`:** bridged input calls `onInputReceived()`.
- **`LinkTestMain.java`:** new `LINKTEST_TURN=1`, which turns continuously in uneven steps.
- DESIGN §5.2 and §5.3.

**Why:** you reported the camera jittery again, only in the two third-person views, after the input
bridge.

**Tested how:**
1. Read your Minecraft `latest.log` (read-only) for frames per second.
2. Traced Mycelium (`-mcCameraTrace -mcFps 60`) with the fake Minecraft at ~120 fps turning
   unevenly, with `cameraSmoothing=0` (the old heading) and `0.05` (the fix).

**Result:**
- **Cause 1:** with the bridge, mouse movement reaches Minecraft in whichever of its frames comes
  next, so it turns in uneven steps. Third person passed Minecraft's yaw straight through, so the
  steps showed. First person was already smoothed, which is why it looked fine.
  - Unevenness behind: **0.75 → 0.13**. In front: **0.71 → 0.22**.
- **Cause 2:** your Minecraft log shows it mostly at 119–120 fps while bridged, but at **30 fps**
  from 16:34:52 to 16:35:20 and again from 16:38:22. That's Minecraft's inactive-window throttle,
  which the new mixin bypasses.

**Still broken / not tested:**
- **Not tested with real Minecraft:** the frame-rate mixin, and how the smoothed third-person
  turning feels.
- Turning in third person now lags about 0.05 s, as first person already did. Lower
  `cameraSmoothing` if that feels sluggish.
- The input bridge commit is still pending (with this).

**Next:** you test.

### Test procedure

1. **Copy the jars:** the Minecraft side changed, so copy them again:
   `copy /Y dist\minecraft-mods\*.jar "C:\Users\Ryan\AppData\Roaming\PrismLauncher\instances\Mycelium 26.3\minecraft\mods\"`
2. Start Minecraft, then Mycelium, and play from Mycelium's window. Press F5 for the third-person
   views.
3. **You should see:** turning is smooth in both third-person views, as it is in first person.
4. **In Minecraft's log:** the `frame=` number in the `MyceliumCraft:` lines goes up about 120 per
   second (or your frame-rate setting), with no 30 fps stretches while you play from Mycelium.

## 2026-10-17 Input bridge: play from Mycelium's window (protocol v6), and the hub-menu fix

**Changed:**
- **Protocol v6:** new `MycInput` block at 0x300 (keys as SDL scancodes, mouse buttons, mouse and
  wheel totals, typed text, active flag and reason). It's in `PROTOCOL.md`, both `Proto` files,
  `MyceliumLink.WriteInput` and `MycLink.readInput`.
- **Mycelium:**
  - `InputBridge.cs` (new) reads the keyboard and mouse.
  - `MinecraftDriver.cs`:
    - `MyceliumHasControls()`: paused, `canAct` off, or the Player input map off.
    - The new **hold** leaves the spore alone while Mycelium has the controls, and resyncs
      afterwards.
    - Sets the bridge's active flag and reason.
    - The status line shows `input=` and `hold=`.
    - New `-mcHoldTest` test aid.
  - `McConfig.cs`: new `inputBridge=1`.
  - The asmdef references `Unity.InputSystem`.
- **Minecraft:**
  - `MycInputBridge.java` (new) replays the input at the start of every frame.
  - New `WindowMixin` and `InputConstantsMixin`.
  - `MinecraftMixin`: new `runTick` hook and `isWindowActive`.
- **Stand-ins:** test the input block.
- DESIGN §5.3.

**Why:** you found that in the hub, with the Explore/Cancel buttons up, WASD still moved the player
when it should move through the buttons. That happened because Minecraft had the keyboard and the
spore always followed Minecraft. The bridge fixes both: Mycelium has the keyboard and keeps it for
its own menus.

**Tested how:**
1. Read Minecraft 26.3's `MouseHandler`, `KeyboardHandler`, `InputConstants`, `Minecraft` and
   `Window` with `javap`.
2. Read Mycelium's input bindings and every place it takes the controls away.
3. FakeMycelium plus `gradlew linkTest`.
4. `MyceliumCraft.exe -mcScene "New Tutorial" -mcHoldTest` with `gradlew linkTest`.

**Result:**
- **Minecraft's code:**
  - `onMove(handle, x, y, dx, dy)` adds `dx, dy` to its look accumulator only when
    `isWindowActive()` and the mouse is grabbed.
  - `grabMouse()` needs `isWindowActive()`.
  - Unbridged clicks only try to grab.
  - So the bridge makes the window "active" and lets Minecraft grab normally, with the real
    cursor untouched.
- **Stand-ins:** 9 new input checks all OK (`W held (scancode 26)`, `mouse total X: got 123.5`,
  `typed text`, …), ground checks PASS, FakeMycelium PASS.
- **Real level:**
  - `hold test: called PlayerController.DisableController()` → `hold: Mycelium has the controls
    (canAct off)`. The spore stayed at x 107.77 for 3 s while the fake Minecraft kept walking.
  - `EnableController()` → `hold: Mycelium gave the controls back` → `teleport ... (Mycelium moved
    the spore 6.2 units while it had the controls)`.
  - The bridge logged `input bridge: off (Mycelium window not focused)`, correctly, because the test
    window wasn't focused.

**Still broken / not tested:**
- **The bridge with real Minecraft and real keys: not tested at all.** I can't press keys, and the
  fake Minecraft doesn't run Minecraft's input code. These all compile against 26.3 and match
  SkyCraft, but only your test will show they work:
  - the mixins
  - `keyPress` and `onButton`
  - mouse look through `onMove`
- Both games react to the shared keys (Shift, Space, E, Q, clicks, Ctrl, Tab). See the table in
  DESIGN §5.3. Which game owns which action is still to be decided.
- Minecraft's screens (inventory, chat) open in Minecraft's window, and you can't click in them from
  Mycelium.
- Knockback briefly takes the controls in Mycelium, so Minecraft's player stops for that moment.

**Next:** you test. Then decide which game owns which action.

### Test procedure

1. **Copy the jars:** the protocol changed, so both sides must be the new build:
   `copy /Y dist\minecraft-mods\*.jar "C:\Users\Ryan\AppData\Roaming\PrismLauncher\instances\Mycelium 26.3\minecraft\mods\"`
2. **Start both:** start Minecraft (Creative Superflat), enter the world, then start
   `work\build\MyceliumCraft.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720`.
3. **Click into Mycelium's window** and play from there. **You should see:**
   - WASD, mouse look, Space (jump) and F5 work through Mycelium's window, with Minecraft's window
     in the background.
   - Click Minecraft's window and it works there as before (the bridge switches off). Click back
     into Mycelium and it switches on again.
4. **In the hub (The Carcass),** start a new run. While Explore/Cancel are shown, **WASD moves
   through the buttons and the player stays still.** Picking one works, and afterwards you can walk
   again.
5. **In `work\build\MyceliumCraft.log`:**
   - `input bridge: ON ...`, and `input bridge: off (...)` with a reason, as you switch windows or
     open menus
   - `hold: Mycelium has the controls (canAct off)` at the prompt
   - `hold: Mycelium gave the controls back`
6. **In Minecraft's log:**
   - `MyceliumCraft: input bridge ON: playing from Mycelium's keyboard and mouse`
   - once a second, `input bridge: N keys held, ... mouse moved N px ...; grabbed=true screen=none`
7. **If keys or the mouse don't reach Minecraft,** send me both logs. Set `inputBridge=0` in
   `work\build\MyceliumCraft.cfg` to go back to playing from Minecraft's window. The hub-menu fix
   (the hold) still works that way.

## 2026-10-17 Camera shake works again with the synced camera

**Changed:**
- **`mycelium/Runtime/ShakeTap.cs` (new):** a Cinemachine extension that records what Mycelium's
  impulse listener (the shake) added to the camera each frame.
- **`CameraSync.cs`:**
  - Adds the tap to the main FreeLook.
  - Third person works out its tilt from the unshaken view.
  - First person applies the recorded shake to the eye camera.
  - The trace gains `shakePos`, `shakeAngle` and `offAim` columns, and is now written after the
    tilt.
- **`MinecraftDriver.cs`:** new `-mcShakeTest` test aid. The status line shows `shake=`.
- DESIGN §5.2.
- Before this: committed the F5 views as `6a9709f`.

**Why:** you reported that the trigger in the tutorial that plays a sound and shakes the camera
didn't shake it.

**Tested how:** `-mcScene "New Tutorial" -mcCameraTrace -mcFps 60 -mcShakeTest` with
`gradlew linkTest` (first person, then behind, then in front). The test fires the tutorial's own
`EventTrigger 2` shake through `CameraShakeManager.ShakeCamera` every 3 s. I compared the listener's
shake with what reached the final camera in the trace.

**Result:**
- Log: `camera: shake tap added; Mycelium impulse listener found (gain 1, apply after Noise,
  secondary noise yes)`, then `shake test: fired 'EventTrigger 2' (duration 3.00 s)` in each view.
- **Cause:**
  - First person overwrote the camera, so no shake at all. Minecraft's default view is first
    person, so this is likely what you saw.
  - Third person's tilt cancelled the shake's up/down.
- **After the fix,** the shake reaching the camera matches the listener's:
  - first person: max 1.00° vs 1.00°
  - behind: 0.81° vs 0.81°
  - in front: 0.97° vs 1.00°
  - calm frames ≤ 0.16°
- My first measurement of third person was wrong: the trace was written before the tilt, so it
  showed nonsense (up to 23° off "while calm"). I moved it after the tilt and re-measured.

**Still broken / not tested:**
- **Not tested at the real trigger, with real Minecraft.**
- I can't tell how strong the shake looks compared with plain Mycelium. The tap passes through
  exactly what Mycelium's listener produces, but I haven't measured unmodified Mycelium.
- Uncommitted. Revert point: `6a9709f`.

**Next:** you test at the trigger.

### Test procedure

1. Only Mycelium changed, so no jar copy is needed. Start Minecraft, then
   `work\build\MyceliumCraft.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720`.
2. Walk into the tutorial trigger that plays the sound. Try it in first person, or press F5 and
   try again from the start of the level.
3. **You should see:** the camera shakes about 0.65 s after the sound, in whichever view you're in.
4. **In `work\build\MyceliumCraft.log`:** `camera: shake tap added ...`, and while it shakes,
   status lines with `shake=` above 0 (e.g. `shake=0.05/0.6deg`).

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
