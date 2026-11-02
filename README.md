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

- **The camera tilts toward what hit you, as Minecraft's does: not tested in Minecraft.** It follows
  Minecraft's "Damage Tilt" setting. Mycelium doesn't say what hit you, so the mod guesses (the
  nearest enemy or hitbox) and can be wrong. Minecraft's death roll isn't copied.
- **Weapons are food items in your Minecraft inventory** (Slash weapons bread, Smash beetroot, Stab
  carrots; not tested in Minecraft).
  - **Pickup:** picking up a weapon keeps the one you had; the new one gets a carrot and is
    selected.
  - **Switching:** the spore holds the weapon whose carrot you select.
  - **The carrot:** its name, attack damage (Mycelium damage ÷ 5) and attack speed match the
    weapon, with the spore's level and the weapon's attributes counted. Its description lists
    the type, damage, swings per second, knockback and attributes. Carrots can't be eaten.
  - **Runs:** carrots don't drop on death; a run starts with just the Stick.
  - **Throwing a weapon** (Q, or out of the inventory) throws the real weapon. It flies like the
    Minecraft item, then lies on Mycelium's ground ready to pick up again. Tested in Mycelium with a
    fake throw.
  - **Knockback:** your Minecraft hits now also knock enemies back by the held weapon's knockback.
  - **Each weapon has its own (dummy) texture: not tested in Minecraft.** From our built-in resource
    pack, picked by the item's custom_model_data. Slash weapons look like blades, Smash like
    clubs, Stab like spears, each its own colour.
  - **Better Combat animations: not tested.** Slash weapons swing right then left, Stab weapons stab,
    Smash weapons slam two-handed. Each weapon's attack range is its real reach in Mycelium
    plus 25% (measured; a weapon gets it the first time you hold it).
  - **Your swings smack the levels' mushrooms** (bounce and sound, as in Mycelium): tested in
    Mycelium with the fake Minecraft's swings; not tested in play.
  - **Poisoned enemies flash green** instead of red (tested in Mycelium).
  - **Crabs (and other enemies whose model has several parts) flash fully** now (tested in Mycelium:
    the tinted copies switch on and off; not looked at).
  - **Attributes work:** every weapon attribute, including on-hit ones (Critical, Repulsive,
    Nasty, Vampiric…), swing ones (Fragile, Nimble, Enigmatic, Flourishing) and skill procs. Tested
    in Mycelium with a fake Minecraft for Repulsive, Nimble and Enigmatic.
  - **Fungal Might** ends only after a swing that hits enemies; a miss keeps it on (tested in
    Mycelium with a fake Minecraft).
  - The Mycelium half is tested with a fake Minecraft.
- **You can only attack when the attack indicator is full** (while Mycelium is playing): not tested
  in Minecraft. Bows are unchanged.
- **Your Minecraft attacks hurt Mycelium's enemies: not tested in Minecraft.** Each nearby enemy has
  an invisible stand-in in Minecraft. Minecraft's own attack rules (weapon, cooldown, crits,
  enchantments, arrows) decide the damage, ×5 in Mycelium (`damageScale=` in `MyceliumCraft.cfg`).
  The Mycelium half is tested with a fake Minecraft.
- **Minecraft's experience level = Mycelium's nutrients: not tested in Minecraft.** XP orbs and
  enchanting don't change it (put back).
- **Minecraft health follows Mycelium's: not tested in Minecraft.**
  - Every 5 Mycelium health is half a heart; Minecraft's max health follows Mycelium's max.
  - When max health changes (Vitality, a weapon's attribute), current health keeps the same share of
    it (tested in Mycelium).
- **Mycelium's Speed stat and speed effects change your walking speed in Minecraft:** tested in
  Mycelium, not in Minecraft. Since 2026-11-02 it's put back after a respawn (not tested).
  - Minecraft can't kill you while Mycelium's health is above 0.
  - Mycelium's death kills the Minecraft player (it drops its items, as Minecraft does), and it
    respawns when Mycelium has health again.
  - When you die, the view goes to third person behind, and your Minecraft body does Minecraft's
    death animation. Your own view comes back afterwards.
- **Emerge when Mycelium sprouts you in The Carcass: not tested in Minecraft.** In the third-person front
  view, your Minecraft body digs up out of the ground (hands out, pulling on the ground, pushing up,
  stepping out; like the Warden's emerge), over Mycelium's ~3 s sprout. It happens after a death and after a run.
- **No fall damage and no natural health regeneration in Minecraft, always while the mod is
  installed: not tested in Minecraft.** Done in the mod, so nothing is saved to your worlds.

- **Tunnels in real Minecraft: not tested** since the fix. See the newest MODLOG entry for the test.
- **Body size: not tested in Minecraft.** Minecraft's player is now shrunk to the spore's height
  (scale 0.674, 1.214 blocks) using Minecraft's own scale setting; `matchSporeSize=0` in
  `MyceliumCraft.cfg` turns it off. Offline, the player fits at 241 of 246 walked spots. The rest
  are 3 low overhangs where the spore's rounded top squeezes under, and 2 ledge edges
  (DESIGN §5.1).
- One-way surfaces (Unity terrain seen from below) are ½-block slabs: you can't jump up through them.
- **Camera follows Minecraft's look, over the full −90..90 range: not tested with real Minecraft.**
  With a fake Minecraft sweeping its pitch, the rendered camera matched the wanted angle within 1°
  from −72° to 72°, and its heading matched the spore's facing. Up/down jitter (reported by you)
  is fixed and measured smooth with a per-frame trace. `syncCamera=0` turns it off;
  `cameraPitchOffset=` sets the straight-ahead view, and `cameraSmoothing=` trades smoothness for lag.
- **Sprint FOV and crouch camera: not tested with real Minecraft.** Mycelium's camera widens its
  field of view when Minecraft's does (sprinting, speed, bow; spyglass zooms in), and drops when you
  crouch, in every view. Tested with a fake Minecraft.
- **Camera copies Minecraft's F5 view (first person / behind / in front): not tested with real
  Minecraft.** With a fake Minecraft, all three place and aim the camera correctly. In first person
  the spore is drawn as shadow only. `matchF5View=0` keeps the camera behind.
- **Mycelium's camera shakes** (triggers, bosses) work in all three views. Measured with the
  tutorial's own trigger shake: the camera shakes exactly as much as Mycelium's shake produces. Not
  tested at the real trigger with real Minecraft.
- **Play from Mycelium's window (input bridge): not tested with real Minecraft.** Mycelium's
  keyboard and mouse are passed on to Minecraft during normal play, except P (Mycelium's pause and
  menu). Escape opens Minecraft's pause menu. Stand-in tests pass. `inputBridge=0` goes back to playing from Minecraft's window.
- **Mycelium menus keep the controls** (e.g. the hub's Explore/Cancel): the spore is no longer driven
  while a Mycelium menu, pause or cutscene has the controls, and Minecraft gets no input. Tested with
  Mycelium's own `DisableController()`/`EnableController()`, not yet at the real prompt.
- **The menu between levels hides the overlay and stops you moving**, even if the controls come
  back on under it (a hit's knockback ending). Tested in Mycelium with the fake Minecraft
  (`-mcLevelEndTest`); not tested in play.
- **Mycelium's keys are set in Minecraft's menu: not tested in real Minecraft.** Options →
  Controls → Key Binds has a "Mycelium" section (Interact, Show Stats, Subspecies Skill, Salvage,
  Attack, Curio, Dodge Roll, Skill 1, Skill 2, Navigate Camera, Reset Camera). A key bound there
  belongs to Mycelium and isn't sent to Minecraft; every other key goes to Minecraft. Defaults:
  R interact, Tab show stats, V subspecies skill, middle click salvage, the rest unbound. Tested
  with a fake Minecraft: Mycelium followed the binds it sent, and a change during play was picked
  up within a frame. See DESIGN §5.3.
- **Mycelium's key hints show your Minecraft binds** ("Press [R] to Open", the skill bar's keys,
  the attack key for curios), and the skill menu assigns slots with your Skill 1 / Skill 2 keys.
  Tested in Mycelium with a fake Minecraft's binds; not seen in play.
- **Your Minecraft body in third person: not tested in real Minecraft.** In both F5 third-person
  views, your Minecraft player (skin, armour, cape, its own walking and swinging) stands where the
  spore is, and the spore is hidden. The body casts its own shadow (the spore's is gone), in first
  person too, where only the shadow is drawn (third person tested by you). It
  isn't washed out by the fog any more (tested by you). Held items and blocks show in its hands (not tested in real Minecraft; no enchantment glint yet). `playerModel=0` keeps the spore.
  Tested with a fake Minecraft's test body: right size, right place, right facing.
- **Living Cyclone:** from first person the view goes to third person behind while it spins, and
  your body loops our own Living Cyclone animation (a quicker wind-up and faster spins than Better
  Combat's two_handed_spin) (not tested in Minecraft; the view flag tested in
  Mycelium with a fake spin).
- **Trophic Cascade:** your Minecraft body vanishes and reappears with the spore, and from first
  person the view goes to third person behind meanwhile (not tested in Minecraft; tested in
  Mycelium with a fake vanish).
- **Blitz and the dodge roll move your Minecraft player** (not tested in Minecraft; the dash reaches
  a fake Minecraft).
- **Projectile skills fire where you look** (Spineshot, Undergrowth, Unstable Puffball, Death
  Blossom), not where Minecraft's body faces. Tested in Mycelium with a fake Minecraft (aim only;
  no skill fired).
- **The respawn (emerge) animation always takes the same time,** whatever your Speed stat (tested
  in Mycelium with a fake Minecraft).
- **Every weapon has your own texture** (19 of 19).
- **Minecraft's window takes the size of Mycelium's** (so the overlay isn't stretched), unless
  Minecraft is full screen. Not tested in Minecraft: tested only that the size arrives (1280x720
  with a fake Minecraft).
- **Mycelium's window can be resized** (in windowed mode; Alt+Enter switches from full screen).
  Checked from the log: the window has Windows' resizable border and maximize button. Not tried
  by hand.
- **Overlay: tested by you** (you saw it working in real play). Minecraft's hand, hotbar, hearts and screens
  (inventory, chat) are drawn on top of Mycelium, and Minecraft stops drawing its own world
  (`overlay=0` in `MyceliumCraft.cfg` turns it off). Tested with a fake Minecraft's test picture,
  which showed with exact colors.
- **Better Combat's attack animations in first person: tested by you.** Your arms swinging the weapon
  are drawn in the overlay (third person already worked, you saw it).
- **Better Combat's swing particles: not tested.** Drawn in the overlay; Minecraft's other particles stay hidden.
- **Cursor in Minecraft's screens, and pausing: not tested in real Minecraft.**
  - **Mouse pointer:** while a Minecraft screen is open, Windows' own mouse pointer is free (it can
    leave the window), and you can click in Minecraft's inventory, chat and menus.
  - **Escape** pauses with Minecraft's menu, and Mycelium freezes and blurs meanwhile, its HUD
    included (blur tested with a fake Minecraft).
  - **P** pauses with Mycelium's menu, and Minecraft's world freezes meanwhile.
  - While a Minecraft screen is open (chat, inventory), Mycelium's controls are off and every key
    goes to Minecraft.
  - **Tested with stand-ins:** the cursor position reaches Minecraft exactly; Mycelium froze for
    exactly the 3 s the fake Minecraft's pause menu was "open"; holding Escape no longer pauses
    Mycelium.
- Nothing damages Mycelium's enemies yet: its own attack is unbound by default, and Minecraft's
  attack has no Mycelium target.
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
