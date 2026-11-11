# MODLOG

## 2026-11-10 The Mycelium game mode plays as Survival

**Your request:** make the Mycelium game mode Survival, with cheats still on.

**Changed:** `SelectedGameModeMixin` makes the entry with `GameType.SURVIVAL` instead of Creative.
Cheats stay on, because `MycWorldMode` sets them on when you choose it. The menu description is
updated.

**Tested how:** the jar builds. **Not tested in Minecraft.**

## 2026-11-10 A "Mycelium" game mode in Create New World

**Your request:** a "Mycelium" game mode in the world creation menu that gives a world your
world's game rules, difficulty, time of day and everything else.

**Read from your "New World"** (not changed):
- Creative, Peaceful, cheats on.
- Superflat with Minecraft's default layers: bedrock, 2 dirt, grass; plains; villages and
  strongholds; no lakes or decorations. Structures on, no bonus chest.
- Game rules: `immediate_respawn` on and `advance_time` off; all others are the defaults.
- Time of day 6182; clear weather. Default data packs, no experiments.

**Changed:**
- New `MycWorldMode.java`, plus mixins `SelectedGameModeAccessor`, `SelectedGameModeMixin`,
  `CreateWorldGameTabMixin` and `CreateWorldScreenMixin`.
- The Game Mode button now has "Mycelium" after Creative. Choosing it fills in all of the above.
- Creating the world sets the time (6182) and clear weather once it has started.
- The seed stays random.
- Menu text is in `en_us.json`.

**Tested how:** the jar builds. **Not tested in Minecraft.** Adding an entry to Minecraft's game
mode list and the screen hooks are only checked when Minecraft starts, so if one doesn't fit,
Minecraft shows an error at startup.

## 2026-11-10 Your world's game rules and time of day in any world while linked

**Your request:** apply the game rules and time of day of your single-player world when linked to
Mycelium, in case friends try it.

**Found** (read from `saves/New World`, not changed): only two rules differ from Minecraft's
defaults.
- `immediate_respawn` true (default false).
- `advance_time` false (default true): the day-night cycle is off.
- The overworld clock stands at 102182 ticks, so the time of day is 6182, just after noon.

**Changed:** new `MycWorldRules.java`. When Mycelium links, it remembers the world's own values for
those two rules, sets yours, and runs `/time set 6182`. When Mycelium goes away, the two rules go
back to what the world had. The time isn't put back: it just runs on again if the cycle was on.

**Tested how:** the jar builds. **Not tested in Minecraft** (the stand-in has no Minecraft world).
Your own world already has these values, so in it you'll only see the log lines.

## 2026-11-10 Spores answer any Emotecraft emote with the same emote

**Your request:** like waving, if you do any Emotecraft emote towards a spore, it does the same
emote back.

**Changed:**
- `MycEmotes.emotingYou` (was `wavingYou`): McState `EMOTE_WAVE` is now on while any Emotecraft
  emote you started plays on you. It isn't on for the dances the beetle drum starts. A new emote
  straight after another turns it off for 0.1 s, so Mycelium sees a new start. No protocol change.
- Mycelium (`CurioKey.EmoteWave`) still calls its own `GoadWaveBack`, so the same spores answer as
  for a wave: near you, in front of you, and happy enough. Only its log line changed.
- `MycEmotes.answer` / `MycSporeBodies`: when a spore waves and you're doing an emote (or did in
  the last 3 s), its body plays your emote instead of the wave. A copy stops once you've stopped
  and it has played as long as yours did, so looping emotes end; 20 s at most.
- Log lines: "you're doing Emotecraft's '<name>'", "<spore> does your '<name>' back", "your
  '<name>' is over after N s".

**Tested how:** stand-in run in The Carcass (`LINKTEST_EMOTEWAVE=1`). Mycelium logged "you do an
Emotecraft emote: the spores near you ... may answer", and two spores started waving 1.2 and 1.6 s
later. The jar builds.

**Not tested:** in real Minecraft (that a spore's body plays your emote, and when copies stop).
Known: spores also greet each other by themselves, and one doing that within 3 s of your emote
copies it too.

## 2026-11-10 The level's goal on Minecraft's locator bar (protocol v61)

**Your request:** use Minecraft's locator bar to show the level's goal.

**Changed:**
- New `GoalWatch.cs` (Mycelium):
  - The goal is what Mycelium's own Navigate Camera turns to (`VCamRotator.levelGoal`): the boss
    in Impact Barrens, `BackToHub` in Daybreak Arboretum and Delta Crag, `==StartRun==` in The
    Carcass, and the light in New Tutorial.
  - Its position goes to Minecraft every frame in the Boss block (new fields 0xA0..0xAF).
    Protocol version 61.
- New `MycGoal.java` (Minecraft):
  - Puts the goal on the locator bar as a green bow-tie marker. It's added on your own screen
    only, not through the server.
  - Minecraft normally swaps the locator bar for the XP bar for 5 s whenever experience changes.
    Your nutrients are the experience level and change all the time, so that swap is skipped
    while there's a goal. The level number still shows.
- The stand-in prints the goal it receives.

**Tested how:** stand-in runs (`LINKTEST_STILL=1`).
- Impact Barrens: Mycelium logged "goal: 'Rival Sporemother' ... (Navigate Camera's goal)", and
  the stand-in got "goal: at (27, -6, -289)".
- The Carcass: '==StartRun==', at (-13, 1, -2).

**Your test:** the locator bar showed, but it was empty.

**Why:** the bar draws the marker's colour as ARGB (alpha, red, green, blue). I gave it 0x7FFF00
with no alpha, which is fully transparent. **Fixed:** 0xFF7FFF00. Minecraft's log line now also
says how many degrees left or right of where you look the goal is. The bar only shows markers
within 60 degrees of your view, so a goal behind you only appears once you turn.

**Then your request:** make it red, and put it behind the experience level number.
- `MycGoal.java`: the colour is now 0xFFFF0000.
- New `HudLevelNumberMixin`: Minecraft draws the level number first and the bar's markers after,
  so a marker in the middle covered your nutrients. While the goal is on the bar, the number is
  drawn after the bar instead. Without a goal, Minecraft draws it as usual. Log line, once:
  "the level number is drawn over the locator bar, so the marker goes behind it".

**Not tested:** in real Minecraft (whether the marker shows now, where it sits on the bar, the XP
bar swap, the number on top), and Daybreak Arboretum, Delta Crag and New Tutorial. The jar builds,
but the new mixin's hook points are only checked when Minecraft starts.

## 2026-11-10 Attack (Mycelium), Navigate Camera and Reset Camera are gone from Key Binds

**Your request:** remove those three from the Mycelium section of Minecraft's Key Binds.

**Changed:**
- `MyceliumKeys.java` doesn't register those three any more.
- Their places in the list stay, so every other action keeps its number in the protocol. Mycelium
  is always told they're unbound, which was already their default. If you had bound one of them
  yourself, it's now unbound.
- Their names are removed from `en_us.json`. No protocol change.

**Tested how:** the jar builds. **Not tested in Minecraft.**

## 2026-11-10 The boss's pull when you stand still: measured, logged each second

**Your report:** standing still with no movement keys, the pull didn't move you.

**Found (measured, not guessed):**
- Mycelium's own pull, with nobody driving (new test aid `-mcBossPullNative`, run without
  Minecraft at 60 fps): a spore standing still moves about 1.8 units a second, which is about 1.3
  blocks a second. The first second is slower because the pull builds up.
- Your 19:32 session in Minecraft: about 1.36 blocks a second on average. In the one full second
  with no keys held (19:32:44) you moved 1.2 blocks. So Minecraft does what Mycelium does.
- The pull at 19:32:59 started while you stood still, and you moved only 0.34 blocks in the first
  second. Mycelium's own pull is that slow in its first second too.
- The pull is weak: your spore walks at about 11.4 units a second, so the pull is about a fifth
  of that. It's easy to miss.
- I could not find a moment where you stood still, the pull was on, and you didn't move. Mycelium's
  log for your session is gone: my test runs overwrote it. Minecraft's log has no per-second pull
  numbers, so a short stuck moment would not show.
- The run went back to The Carcass because the spore stood next to the boss and died.

**Changed:**
- `MycBossPull.java`: logs every second of a pull. It records how far Mycelium asked to move you,
  how far you moved, how many ticks something blocked the move, how many ticks you had no
  movement keys held and how far you moved in those, and whether you were on the ground or
  against a wall.
- `MinecraftDriver.cs`: new test aid `-mcBossPullNative` (described above).

**Tested how:** the native measurement above (`-mcScene "Impact Barrens" -mcBossPullNative -mcFps
60`, without the stand-in). The jar builds. The new log line is **not tested in Minecraft**.

## 2026-11-10 You can jump during the boss's sand pull

**Your report:** during the boss's pull you can't jump.

**Why:** `MycBossPull` moves you sideways at the start of every tick. Minecraft's move only counts
you as on the ground when a downward move hits the floor, so a sideways-only move always left you
"in the air". The jump check comes right after in the same tick and needs the ground.

**Changed:** `MycBossPull.java` keeps the on-ground state you had before its move. Your own
movement later in the tick sets it properly (walking off a ledge still makes you fall). Log line,
once per pull: "the boss's pull keeps the player on the ground, so it can jump".

**Tested how:** the jar builds. **Not tested in Minecraft** (the stand-in can't jump).

## 2026-11-10 Mycelium's rain is a thunderstorm in Minecraft; lightning can't hit you (protocol v60)

**Your request:** when it rains in Mycelium, Minecraft should have a thunderstorm. Lightning should
still strike, so the thunder is heard, but never hit the player.

**Changed:**
- Mycelium (`MinecraftDriver.Raining()`): reads Mycelium's rain (`WeatherSettingsScript.isRaining`)
  and sends it as the new MycState flag bit 12 `RAIN`. Protocol version 60.
- New `MycWeather.java`: while Mycelium is in a level, Minecraft's world gets a thunderstorm while
  it rains in Mycelium and clear weather while it doesn't (as `/weather thunder` / `/weather clear`),
  set again every 30 s. Not linked: Minecraft's weather is left alone.
- New `world/MycLightning.java` with two hooks:
  - `LightningAimMixin`: a strike aimed within 10 blocks of a player strikes 20 blocks away instead.
  - `LightningHitMixin`: lightning never hurts a player, as a safety net.
- Log lines: Mycelium "weather: it's raining in Mycelium" / "rain stopped"; Minecraft "weather: it
  rains in Mycelium: Minecraft has a thunderstorm" / "no rain in Mycelium: ... clear", "lightning: a
  strike aimed at ... was too close to a player; struck at ... instead", "lightning: a bolt reached
  ...; no hit".
- New test aid `-mcRainTest` (Mycelium's own rain on 10 s, off 10 s). The stand-in prints the flag.

**Tested how:** stand-in run (`-mcScene "The Carcass" -mcBackground -mcRainTest`). Mycelium's rain
went on and off, and the stand-in saw the flag follow each time. The jar builds. **Not tested in
Minecraft:** the thunderstorm, the lightning being moved, and the no-hit hook. If a hook didn't fit
Minecraft's code, Minecraft would refuse to start, so a normal start already says they're in place.

## 2026-11-10 Curio indicators show your Curio key

**Your report:** the key shown above curios isn't the key they're set to.

**Why:** a curio is used with Mycelium's Curio action (in Minecraft's Controls, Mycelium section;
middle mouse for you). Mycelium's indicator above a curio always shows the *Attack* hint, and of
whichever controller Mycelium thinks you used last: in the test it said "RT" (an Xbox trigger).
Our key hints never covered curios, because Mycelium has no curio hint to change.

**Changed:**
- `KeyHints.cs`: while Minecraft drives, every curio's indicator text is set to the key bound to
  Curio in Minecraft (same colours as Mycelium's hints), checked every 0.1 s, since Mycelium writes
  its own text again whenever the indicator pops in.
- Log lines: "key hints: curio indicators show the Curio key [..]" and "N curio indicator(s) set
  to [..]".
- New test aid `-mcKeyHintTest` (see DESIGN, key hints).

**Dead end on the way:** the first version found no indicators: `interactCanvas` is a private
field of Mycelium's `Curio`, and asking a bed's or chair's own type for it finds nothing. It's
looked up on `Curio` now.

**Tested how:** stand-in run in The Carcass (`-mcBackground -mcKeyHintTest`, Curio bound to middle
mouse, then Q after 5 s). The 7 curio indicators said "[RT]" from Mycelium, "[MMB]" right after,
then "[Q]" after the change. Not tested in Minecraft.

## 2026-11-10 New weapons aren't selected in the hotbar

**Your request:** new weapons shouldn't be selected automatically.

**Changed:**
- `WeaponBag.AskSelect` (Mycelium): after a pickup, Minecraft is only asked to select the new
  carrot when its main hand holds none of your weapons (a run's first Stick, or a pickup after you
  threw your weapon), so you're never left empty-handed. Otherwise nothing is selected; Mycelium
  put the new weapon in the spore's hand, so the spore switches straight back to the weapon in
  Minecraft's hand, and the new one waits in the bag with its carrot in the inventory.
- Log line (Mycelium): "weapons: #N is new; not selected: Minecraft's hand holds #M ..., the spore
  goes back to it".

**Tested how:** both builds pass. Not tested: there's no stand-in test for a pickup, and not tested
in Minecraft.

## 2026-11-09 The final boss's sand pull moves you (protocol v59)

**Your report:** the boss's move that should pull you towards it doesn't move you at all.

**Why:** the boss (`MonsterBossAttack.ApplyPullForce`, 5 s out of every 10, starting 5 s after its
attacks come on) pushes the spore's physics body towards itself every frame. While Minecraft
drives, Mycelium's spore is set to follow Minecraft's player every physics step, so the push went
nowhere. In Mycelium the pull is an extra speed on top of your walking, and it's stronger the
higher the frame rate (the boss pushes once per frame, the physics steps 50 times a second).

**Changed:**
- New `PullWatch.cs`: at the start of each physics step it reads the force gathered on the spore
  (`Rigidbody.GetAccumulatedForce`), turns it into the speed Mycelium would have added, and takes
  the force off the spore. Only while the boss's attacks are on, its sand-pull particles are out,
  you're not being knocked back, and the push points towards the boss. In the first test run
  something else pushed once, away from the boss; that's left alone now.
- The speed goes to Minecraft in the Boss block (0x90/0x94, blocks per second).
- New `MycBossPull.java`: at the start of every tick, the player is moved by a tick's worth of it
  (Minecraft's collisions apply), on top of its own walking.
- Log lines: Mycelium "boss pull: '<boss>' pulls you" and "over after ... s, at most ... units/s";
  Minecraft "the boss's pull moves the player (... blocks/s)" and "is over: it moved the player ...
  blocks over ... ticks".
- New test aid `-mcBossPullTest` (Impact Barrens: switches the boss's attacks on). The stand-in
  with `LINKTEST_STILL=1` now also moves by the pull, as Minecraft does.

**Tested how:** stand-in run (`-mcScene "Impact Barrens" -mcBossPullTest`, `LINKTEST_STILL=1`).
Nothing until the sand particles came out 5 s after the attacks, then about 14 blocks/s towards the
boss. The stand-in's player moved by it and the spore followed: 388 units from the boss, then 377,
then 357. That speed is high because the background test runs at a very high frame rate. At a
normal frame rate it should be roughly 1.5 to 4 blocks/s (worked out, not measured). One re-sync
teleport happened as the pull dragged the stand-in up a slope (the stand-in doesn't follow the
ground's height).
**Not tested:** in Minecraft.

## 2026-11-09 Dancing at the beetle drum plays an Emotecraft dance

**Your request:** spores dancing at the beetle drum, and you, pick one of Club Penguin, Kazotsky
Kick, Roblox Potion Dance and Over here at random.

**Found:** all four are built into Emotecraft (its jar's `assets/emotecraft/emotes`:
`club_penguin_dance`, `kazotsky_kick`, `roblox_potion_dance`, `here`). Read by uuid, nothing copied.
The first three loop; "Over here" doesn't, so it's played again while the dance goes on.

**Changed:**
- Mycelium: Colony `poses` now also sends 4 for a spore dancing at the drum (Mycelium's Bustin
  Moves), where it sent 0 before.
- Minecraft, other spores (`MycSporeBodies`): a dancing spore gets its own made-up player, as for
  waving, and a random dance plays on it until it stops dancing. Then its wave plays as before.
- Minecraft, you (`MycEmotes.danceYou`, every client tick): while you're at the dance pad (MycState
  AT_DECORATION), a random dance plays on you. It stops when you leave.
- Log lines: "spore bodies: <name> dances at the beetle drum: Emotecraft's '<dance>'" and
  "emotes: you dance at the beetle drum: Emotecraft's '<dance>'" / "you stop dancing".
- `-mcSporeCurioTest` now starts with the dance pad.

**Tested how:** stand-in run: the spore on the dance pad came through as pose 4. Both sides build.
**Not tested:** the emotes in Minecraft, for the spores or for you.

## 2026-11-09 Other spores sit and lie down at the curios too (protocol v58)

**Your request:** the other spores should do the sitting and lying down poses at the curios.

**Changed:**
- Mycelium (`ColonyWatch.PoseOf`): each other spore's decoration pose, read from its Animator as
  for yours (InActionState plus SitFloor / SitStump / Sleep / Lay), goes out in the new Colony
  field `poses` (0x798), with each spore's facing as a Minecraft yaw in `yaws` (0x7B0).
- Minecraft (`MycPlayerModel.sporeDecorationPose`, called from `MycSporeBodies`): sitting is your
  sitting pose (lowered to the floor). Lying is your bed pose: I moved that code into one shared
  `lie` method, so a spore's body is fitted to the bed where it stands (the SporeMoves positions
  Minecraft already had for pushing) and the way it faces: the head on the pillow, the body resting
  on the bed, the head tilted forward. Dancing spores aren't posed, same as you.
- Log lines: Mycelium "colony: <name> sits at a decoration / lies down ... / gets up"; Minecraft
  "spore bodies: <name> sits ... / lies ... / stands again" and "<name> on the bed, head towards yaw
  ...: pillow top ..., feet end ...; tilted up ... degrees".
- New test aid `-mcSporeCurioTest`: puts another spore on the bed (Sleep, then Lay) and the chair.

**Tested how:** stand-in run (`-mcScene "The Carcass" -mcSporeCurioTest` with `gradlew linkTest`).
The stand-in read pose 2 (on its back) then 5 (head ahead) for the spore on the bed, and pose 1 for
a spore that sat down on its own. The run ended before the test's chair step. Both sides build.
**Not tested:** what the bodies look like in Minecraft; your own bed pose now goes through the
shared code (it should look the same).

## 2026-11-09 Drum hit: the hand hits the drum, not the face

**Your report (screen recording):** it looks like the spore hits the drum with its face.

**Why:** in frames from the recording, the body folded forward about 75 degrees while the arms
swept out to the sides, so the head came down first. Mycelium's spore does the same fold, but it's
holding its Smash weapon then, and the weapon is what hits the drum.

**Changed (`DrumPose` keys from the slam on, t 0.436):** the wind-up is still as measured. In the
slam, the right arm swings over the top (up, ahead, then ahead and down) and lands on the drum with
the hand. The body leans forward 35 degrees instead of 75, the legs stay under it, and the hips hop
half as high. Worked out by hand: at the bottom of the slam the right hand is about 10 model pixels
ahead of the hips at hip height, and the chin is about 10 pixels higher than the hand.

**Tested how:** the jar builds. **Not tested:** in Minecraft.

## 2026-11-09 Spores hitting the beetle drum are animated (protocol v57)

**Your request:** an animation for when a spore plays the beetle drum, like Mycelium's spore does.

**What Mycelium does:** on the beat of The Carcass's music, the drum (`DrumCurio`) triggers
"DrumHit" on the spore playing it. Its Animator plays "Drum Hit", which is the Smash weapon attack
animation (SmashAnim) at double speed, about 1.5 s with its blends. The spore leans back with its
right arm raised behind its head and its left arm reaching forward, holds that, then throws itself
forward onto the drum. The right arm swings over and down, the left sweeps out to the side, the
torso folds forward about 75 degrees, the legs kick back and the hips hop up.

**How I copied it:** I measured it rather than guessing. A new test aid, `-mcDrumTest`, makes a
spore hit the drum and logs where its hands, feet, chest and head point every 0.025 s, already
converted to Minecraft's arm and leg angles. Those numbers are the keyframes in the new
`DrumPose.java`.
- Mycelium (`ColonyWatch.DrumOf`) sends how far through "Drum Hit" each other spore is, in the
  new Colony field `drums` (0x738, -1 = not drumming), every frame while one is drumming.
- Minecraft (`MycSporeBodies`) poses that spore's body with `DrumPose` while it's drumming (through
  our own `HumanoidEmergeMixin`, as the dig-out does). Arms and legs turn the shortest way to each
  keyed direction, and the whole body leans about the hips. Mycelium's spore holds its arms out to
  the sides where Minecraft's hang down, so the pose fades in at the start of each hit and out at
  the end.
- Log lines: Mycelium "colony: N other spore(s) hitting the beetle drum"; Minecraft "spore
  bodies: <name> hits the beetle drum" and "is done with that drum hit".

**Tested how:** stand-in run (`-mcScene "The Carcass" -mcDrumTest` with `gradlew linkTest`). A spore
that was already drumming on its own, and the test's hits, came through the link going smoothly
from about 0.04 to 0.74, about 15 times a second. Both sides build.
**Not tested:** what the pose looks like in Minecraft. The stand-in doesn't draw bodies, so
`DrumPose` has only been checked by working the angle rules through by hand.

## 2026-11-09 The happiness up and down particles show again

**Your report:** the particles when happiness goes up or down aren't showing.

**Why:** Mycelium makes them (`CharacterStats.ModifyHappiness`: "HappyParticles" at the feet,
"SadParticles" over the head) as children of the spore. While your Minecraft body stands in for a
spore, we hide everything under that spore, so the particles were hidden with it. This was true for
you (`CameraSync`) and for the other spores shown as Minecraft characters (`McColonyBodies`).

**Changed:** `CameraSync.MoodEffect` picks out those particles by name, and both places that hide a
spore now leave them drawn, in every view. New test aid `-mcMoodTest`: 4 s into driving, it raises
and then lowers the happiness of you and one other spore, and logs how many particle parts are drawn.

**Tested how:** stand-in run (`-mcScene "The Carcass" -mcMoodTest` with `gradlew linkTest`). Up and
down, for you and for another spore: "1 happy/sad particle part(s) drawn, 0 not drawn" at 0.5, 1.0
and 1.5 s. I didn't run the test before the fix to see them hidden; that comes from reading the code.
**Not tested:** in Minecraft.

## 2026-11-09 On the bed: the body rests on the bed, not just the head

**Your report:** the head is right, but the body floats.

**Why:** the body is a straight line from the feet to the head. The feet end was held at your
standing height, but the bed slopes down towards its foot, so the legs were in the air.

**Changed (`MycPlayerModel.decorationPose`):** the head stays where it was, half its depth above the
pillow's top. The feet end goes as low as the bed allows: the lowest straight line from the head
down that keeps the body's underside on or above the ground at every point along it (every 0.125
blocks, up to 0.85 of the way, from Mycelium's ground). Past the bed's edge the bed itself holds the
body, so the legs can't dip into it. The log line now says "pillow top … (… above where you stand),
feet end … above where you stand; tilted up … degrees".

**Expected,** from the bed's shape measured with `-mcCurioTest`: the feet end about 0.17 blocks
lower than before, about 28 degrees of tilt, the head at the same height.

**Tested how:** the jar builds; the numbers worked out by hand from the measured shape.
**Not tested:** in Minecraft.

## 2026-11-09 On the bed: not floating after the slide

**Your report:** after sliding down the bed, the body floated.

**Why:** the tilt compared the pillow's height with the ground at the slid feet. Slid 0.3 blocks,
they can be past the bed's edge, where the ground is the floor. So the head seemed much higher than
the feet, and the body tilted up too steeply.

**Changed (`MycPlayerModel.decorationPose`):** the pillow is compared with your own height, where
you stand on the bed (the player's feet), not with the ground under the slid feet.

**Tested how:** the jar builds. **Not tested:** in Minecraft. The log line "on the bed, head towards
yaw …: ground … at the feet, highest … ; tilted up … degrees" shows the numbers it used.

## 2026-11-09 On the bed: slid down a little, so the head is on the pillow

**Your report:** much better, but the back was on the pillow rather than the head.

**Changed (`MycPlayerModel.decorationPose`):** the lying body is moved 0.3 blocks towards the foot
of the bed (away from the head's end). The pillow's height for the tilt is read from there too. The
pillow's top is about 1 block from your feet and the head's middle about 1.2, so this puts the head
on it.

**Tested how:** the jar builds. **Not tested:** in Minecraft. If it's too far or not far enough,
it's one number (`SLIDE_DOWN_BED`).

## 2026-11-08 Lying on the bed: the head rests on the pillow; no face-down version

**Your report:** your head was now at the pillow but inside it. You don't need the face-down
version.

**Why the head sank in:**
- The body lay flat at the height of your feet.
- `-mcCurioTest` probed the bed's surface along your body. It rises from about 0.7 blocks out to
  0.27–0.39 blocks above your feet at 0.9–1.07 blocks out (the pillow), then drops away.
- The head's middle lies about 1.18 blocks out (1.75 × your size).

**Changed (`MycPlayerModel.decorationPose`):**
- Always on your back. "Lay" keeps its head ahead (Mycelium's spore lies on its front there), but
  your body isn't turned over.
- The body is tilted up from the feet so the back of the head rests on the highest surface between
  halfway along the body and the head. That surface is read from Mycelium's ground on the Minecraft
  side (`GroundGrid.groundAt`, in half-block cells, every 0.125 blocks), against the ground at your
  feet, plus the head's extra depth. At most 35 degrees.
  - The log says "on the bed, head towards yaw …: ground … at the feet, highest … between halfway
    and the head …; tilted up … degrees".
  - From the probe, about 19 degrees is to be expected on your bed.
- I checked the tilt's direction with JOML: the head end goes up by 1.75 × sin(tilt).

**Tested how:** the bed's shape from Mycelium's physics (`-mcCurioTest`); the tilt's direction with
JOML.
**Not tested:** in Minecraft (how it looks; whether the half-block ground cells catch your bed's
pillow as the probe did).

## 2026-11-08 Lying on the bed: head on the pillow, the right way up, head still and tilted (protocol v56)

**Your report:** lying on the bed was still wrong: your head wasn't on the pillow. And on the bed,
the head shouldn't move, and should tilt a little forward.

**Why:**
- Your body took Minecraft's sleeping pose along one of its four bed directions, picked from where
  you face (`Direction.fromYRot`), always on its back.
- Mycelium's two bed animations differ. `-mcCurioTest` measured the spore's head bone ("head") while
  it lay down:
  - **"Sleep":** on its back, the head 178 degrees from where the spore faces (behind). Sleeping
    faces away from the bed, so the head goes onto it.
  - **"Lay":** on its front, the head 16 degrees from its facing (ahead, towards the bed).

**Changed:**
- **Mycelium:** "Lay" is sent as LIE + the new MycState flag `LIE_FRONT` (v56); "Sleep" is LIE
  alone.
- **Minecraft (`MycPlayerModel.decorationPose`):**
  - The head goes behind you for "Sleep" and ahead for "Lay", at any angle. Minecraft's sleeping
    pose with no bed direction turns the body by its body yaw, and the head then points along yaw
    90 − body yaw. I read that from `LivingEntityRenderer.setupRotations` and checked it with JOML
    for 0, 45, 90, 180 and 270 degrees.
  - "Lay" turns the body over along its length, face down.
  - The body is lifted by half its thickness, so it lies on the bed rather than half in it.
  - On the bed, the head doesn't follow your look: straight, and tilted 20 degrees towards the
    chest.

**Tested how:** the stand-in, `-mcCurioTest`. The fake Minecraft got "lie down on your back" for
Sleep and "on your front" for Lay. The head direction was checked with JOML; the rest is Minecraft's
renderer, which the stand-in can't run.
**Not tested:** in Minecraft: that the head is now on the pillow, the turn-over for Lay, the lift,
and the tilt. Seen once in the test: the very first sit dropped its pose for a single frame at the
start; not looked into.

## 2026-11-08 The other decorations: on their spots, facing the right way (protocol v55)

**Your request:** what the stumps and bed do (on the seat, the same facing every time), for all the
other decorations.

**What the others are** (locked furniture in your Carcass; `-mcCurioTest all` switches them on to
try them):
- the Bonfire: sit; 6 spots in a ring;
- the Game Board: sit; 2 spots;
- the Dance Pad: dance, two dances, while the drum plays; several spots;
- the Beetle Drum: has no animation for you, so Mycelium does nothing on your Curio key there
  (unchanged). A spore playing it opens the dance pad.

Mycelium's rule for its spores at each: bonfire and game board, face it; dance pad, face its drum.

**Changed (`MinecraftDriver.FaceDecoration`):**
- The bonfire and game board turn you towards them, from the spot. The dance pad turns you towards
  its drum. Before, anything that wasn't a stump or the bed used the bed's "away" rule; the test
  showed you facing away from the fire.
- Dancing (Bustin Moves, Bustin Metal Moves) counts as being at a decoration, standing. It's sent as
  the new MycState flag `AT_DECORATION` (v55). Minecraft switches to third person and holds your
  facing, as for sitting and lying; your body stands. The overlay stays, and mouse look works.

**Tested how:** stand-in, The Carcass, `-mcCurioTest all`. Each of the three is brought next to you
in front, to your right and behind.

**Result:**
- Bonfire: on the nearest of its 6 spots, facing the fire (95 or 154, by spot).
- Dance pad: on its nearest spot, facing the Beetle Drum (91 to 92), "stands there".
- Game board: on its nearest spot, facing it (82 or 265, by spot).
- All within 0.04 units of the spot, still there after 3 s, overlay on. No errors.

**Not tested:** in Minecraft. Your body only stands while you dance; Emotecraft has dances it could
play, which isn't done.

## 2026-11-08 Decorations: you're put on the seat, always facing the same way

**Your report:** sitting on a stump, you sometimes didn't end up on it; and if you already faced the
right way when you sat down, you were turned the wrong way.

**Why:**
- Mycelium plays the sit animation where you stand. Its other spores first walk to one of the
  decoration's seats (`Curio.traversalTransforms`). A stump chair has one seat, on top of the stump
  (about 0.65 up).
- My facing copied Mycelium's rule, "away from the decoration's middle, plus its offset", from where
  you stood. On a stump the seat is almost at the middle, so that direction depended on where
  exactly you stood: arbitrary.
- `-mcCurioTest` measured that the sit animation doesn't turn the model itself (its root bone stays
  at 0 degrees from the spore). `-mcSeatTest` watched colony spores walk onto the seats: they stand
  within 0.2 of them.

**Changed:** `MinecraftDriver.FaceDecoration`. At the decoration Mycelium used
(`FindClosestCurioInRange`), you're moved onto its seat nearest you.
- On a stump you face the seat's own direction (your choice, of three offered). For your three
  stumps that's 339, 120 and 84 degrees.
- On the bed: Mycelium's rule from the seat, away from the bed to sleep, towards it to lie. The bed
  has two places; you get the nearer one.
- Minecraft gets a teleport there with that facing.

**Tested how:** stand-in, The Carcass, `-mcCurioTest`. Each decoration is brought next to you three
times: in front, to your right and behind.

**Result:**
- All three stumps: on the seat (0.00 units across, feet 0.09 under the seat point, on the stump's
  top), facing exactly the seat's direction, from every side. Still there after 3 s.
- The bed: on one of its two places, facing 256 or 189 depending on which is nearer. Each the same
  every time.
- No errors.

**Not tested:** in Minecraft (how you look sitting on the stump top; Minecraft's ground there).

## 2026-11-08 At a decoration: the camera turns, and you face the way Mycelium's spores do (protocol v54)

**Your requests:** move the camera around while at a decoration; face the right way when sitting.

**Why the camera was stuck:** Mycelium has the controls while you're at a decoration (canAct off
until you move). The input bridge then stopped passing your keyboard and mouse to Minecraft, so its
look, which the camera follows, couldn't turn.

**Changed:**
- **Mouse look only** (new bridge reason 6, protocol v54). At a decoration, Mycelium sends "at a
  decoration (mouse look only)". Minecraft (`MycInputBridge`) keeps following the mouse's movement,
  so the camera orbits, and lets go of keys and buttons. A movement key still just gets you up in
  Mycelium.
- **You keep facing one way:** your body and head facing is held while you're there, as in an emote
  (`MycEmotes.holdFacing`, now also for SIT/LIE).
- **Facing** (`MinecraftDriver.FaceDecoration`): Mycelium turns its other spores before they sit or
  lie, but not you. Now you're turned the same way, at the decoration Mycelium used (its own
  `FindClosestCurioInRange`):
  - a stump chair: your back to it, turned by its `rotationOffset` (10 degrees here);
  - the bed to sleep: away from it;
  - the bed to lie: towards it.

  The spore is turned, and Minecraft gets a teleport where you stand with that facing. A teleport
  that turns you now also turns your body and head (`MyceliumCraftClient.teleport`), and the held
  facing takes it (`MycEmotes.faceTo`).
- `-mcCurioTest` now brings each decoration next to you, with the others out of the way, and logs
  what Mycelium's lookup finds.

**Tested how:** stand-in, The Carcass, `-mcCurioTest`, `LINKTEST_OVERLAY=1 LINKTEST_MODEL=1
LINKTEST_STILL=1`.

**Result:**
- Facing north (yaw 0) with a stump in front: turned to yaw 190 (back to it, plus 10). Facing east
  (90): turned to 281. The bed to sleep: 192 (away from it).
- Minecraft confirmed each teleport in 0.02–0.03 s.
- The bridge said "at a decoration (mouse look only)" while you were there, and "ON" after. No
  errors.
- Two mistakes the test caught first: the bed faced a stump the test had left beside you (it now
  faces the decoration Mycelium used), and a variable that wasn't always set (a compile error).

**Not tested:** in Minecraft: turning the camera with the mouse while seated, and how the turn looks.
The stand-in can't move the mouse.

## 2026-11-08 Curio key: no wave with nothing near; third person at decorations; spores wave back at your emote (protocol v53)

**Your requests:**
- no wave from the Curio key when no decoration is near (you have the emote wheel);
- third person while you're at a decoration;
- spores wave back at Emotecraft's wave as they did at Mycelium's.

**Changed:**
- **No wave** (new `CurioKey.cs`): in the early step before Mycelium's scripts (`ForgeCatch`), a
  Curio press is checked as `PlayerController` checks it: no weapon in hand, the nearest decoration
  (`FindClosestCurioInRange`) within its `playerInteractRange`, a dance spot only with `canDance`.
  - With none, the press is cleared from Mycelium's Curio action with `InputAction.Reset()`, so it
    does nothing. Salvage on the same button isn't touched.
  - `Disable()` wouldn't have worked: I read Unity's Input System 1.5.1 code, and only a "hard
    reset" clears "pressed this frame", which `Reset()` does and `Disable()` doesn't.
  - The wave pose and Minecraft's wave for it (MYC_WAVE, `MycEmotes.waveYou`) are gone.
- **Third person** (`MyceliumCraftClient.updateViewOverride`): while you sit or lie down
  (MYC_SIT / MYC_LIE), first person becomes third person behind, as for Living Cyclone. Your view
  comes back when you get up. Already in a third-person view, it stays.
- **Waving back:** Minecraft sends McState `EMOTE_WAVE` while Emotecraft's "Waving" is the emote
  playing on you (`MycEmotes.wavingYou`). When it starts, Mycelium calls its own
  `PlayerController.GoadWaveBack`, as after its own wave: each wandering spore near you and within 60
  degrees of where you face may wave back, more likely the happier it is.
- Test aids: `-mcCurioKeyTest`, `LINKTEST_EMOTEWAVE=1`.

**Tested how:** stand-in, The Carcass, `-mcCurioKeyTest` with `LINKTEST_EMOTEWAVE=1` (the wave from
6 to 8.5 s).

**Result:**
- Mycelium found all it needs ("Curio action True, FindClosestCurioInRange True, GoadWaveBack
  True").
- On the emote wave: "you wave (Emotecraft): the spores near you and in front of you may wave back".
  Two spores waved within a second, but spores wave on their own too, so that isn't proof. No errors.

**Not tested:**
- The Curio key with nothing near: the test's middle mouse press didn't reach Mycelium (its window
  has no focus in a test run), as with earlier key tests.
- In Minecraft: the third-person switch, and real spores waving back at your emote.

## 2026-11-08 The Carcass's decorations work: Curio key, overlay, sitting and lying (protocol v52)

**Your request:** make the interactable decorations around The Carcass work.

**What they are:** Mycelium's curios. In your Carcass these are the Stump Chairs (sit on the floor)
and the Mushroom Bed (sleep or lie down); the dance, drum, bonfire and game ones are locked furniture
there. You use one with Mycelium's Curio key (`PlayerController`):
- it fires the decoration's animation on your spore, gives the colony happiness, and holds you
  (canAct off) until you press a movement key;
- with no decoration near, the Curio key makes your spore wave.

**What was wrong** (`-mcCurioTest` uses each one as the key does):
1. The Curio key had no default in Minecraft's Controls (Mycelium's own is left click, Minecraft's
   attack).
2. While you sat or lay down, the overlay (hand, hotbar, hearts) went away: Mycelium has the
   controls then, as in its menus.
3. Your Minecraft body kept standing: the animation plays on the hidden spore.

**Changed:**
- `MyceliumKeys`: the Curio key defaults to the middle mouse button (your choice). It's shared with
  Salvage. In The Carcass you hold no weapon and there's nothing to salvage. In a run Mycelium only
  uses decorations with no weapon in hand. Minecraft keeps a saved binding, so an "unbound" saved
  before stays until you press Reset on it.
- `MinecraftDriver.DecorationPose`: while the Animator's `InActionState` is on, the state your spore
  is in says what you're doing:
  - SitFloor/SitStump → sitting;
  - Sleep/Lay → lying;
  - Hello → waving.

  The pose is kept between two of its states. It's sent as MycState SIT / LIE / WAVE (v52), and the
  overlay stays on meanwhile.
- `MycPlayerModel`: your body sent to Mycelium takes Minecraft's own riding pose, lowered 0.62 blocks
  (times your size) so you sit on the floor; or its sleeping pose, lying the way you face.
- `MycEmotes.waveYou`: the plain wave plays Emotecraft's wave on you (as its wheel would). During it,
  your facing is held as for any emote.
- The animation names are triggers in Mycelium's Animator, used up at once. A first version read
  them as true/false values and saw nothing; the test caught it.

**Tested how:** stand-in, The Carcass, `-mcCurioTest`, `LINKTEST_OVERLAY=1 LINKTEST_MODEL=1
LINKTEST_STILL=1`. It uses the 3 Stump Chairs and the Mushroom Bed, then the Curio key with nothing
near.

**Result:**
- The fake Minecraft got "you sit" for 3 s at each chair, "you lie down" for 3 s at the bed, and
  "you wave" for 1.5 s, each ending when the test moved you, with no gaps.
- The overlay stayed on all the time (0 "overlay False").
- Before the fixes: overlay off, no pose sent. No errors.

**Not tested:**
- In Minecraft: how the sitting and lying poses look on your body and where they sit relative to
  the stump and bed, the middle mouse button, and your wave.
- The camera doesn't turn while you're seated: Mycelium has the controls then, so your mouse doesn't
  reach Minecraft.
- The locked dance, drum, bonfire and game decorations get no pose of their own.

## 2026-11-08 You keep facing one way during an emote, and the camera orbits around you

**Your request:** your player doesn't turn during an emote, so you can orbit the camera around them.

**Why it turned:**
- The spore faces your Minecraft body (McState body yaw, `rb.MoveRotation` while driving).
- Minecraft turns its body after your look once you look more than about 50 degrees away.
- So turning the camera during an emote turned the spore and your body with it.

**Changed:** `MycEmotes.holdFacing`, at the end of every client tick. While Emotecraft plays an emote
on you (its `isPlayingEmote`), your body and head yaw are held where the body faced when the emote
started (`yBodyRot`, `yHeadRot`, and their previous-tick copies, so the frames between ticks agree).
Your look still turns, and Mycelium's camera follows it, so it orbits. When the emote ends, you turn
with the camera again. The log says "emotes: you're doing an emote; you keep facing …" and "your
emote is over …".

**Tested how:** the jar builds.
**Not tested:** in Minecraft; the stand-in can't play an emote. If an emote animates the head itself,
that wins over the held head.

## 2026-11-08 Waving spores use Emotecraft's wave (protocol v51)

**Your request:** spores in The Carcass sometimes wave; when they do, they use Emotecraft's wave.

**How Mycelium does it:** its Wave trigger (`WaveCurio`, a spore waving back, the dance and drum
curios) puts the spore's Animator in the "Hello" state for about 3 s.

**Changed:**
- **Mycelium (`ColonyWatch`):** a bit per spore while its Animator is in "Hello", sent in the Colony
  block (0x734, protocol v51). The log says "colony: X waves" and "X stops waving".
- **Minecraft (`MycSporeBodies`, new `MycEmotes`):**
  - When a spore starts waving, it gets a made-up player of its own, and Emotecraft's built-in
    "Waving" emote (2.5 s) plays on it. Emotecraft is reached by name, unchanged (rule 7), and
    without it nothing happens.
  - That body isn't in the world, so the mod does what Minecraft's tick would: counts its ticks and
    moves its animation on through Player Animation Library.
  - It's posed every frame while the emote plays. Then the spore goes back to the shared body.
- `-mcWaveTest` makes another spore wave 4 s into driving. The fake Minecraft prints the waving
  bits.
- Also fixed on the way: a second place writing the Colony block (no spore) missed the new field and
  broke the build. The build script then hung after Unity's error, and was stopped.

**Tested how:** stand-in, The Carcass, `-mcWaveTest`, `LINKTEST_MODEL=1 LINKTEST_STILL=1`.
**Result:**
- Suzie's wave (the test's) was in "Hello" for about 3 s, and her bit was set from +0.5 s until
  "Hello" ended.
- Spores waved by themselves too: 10 or more waves in 16 s, every one set and cleared.
- The fake Minecraft read the bits (80, 82, c2, 446 …). No errors.

**Not tested:** in Minecraft: the emote on the spores' bodies, its timing, and going back to the
normal pose. The jar builds.

## 2026-11-08 Emotecraft's emote wheel shows your spore's skin

**Your report:** with Emotecraft added, its emote wheel shows your normal Minecraft skin, not your
spore's.

**Why:** Emotecraft draws you on the wheel as one of Minecraft's own mannequins (`ClientMannequin`;
its `UnsafeMannequin` subclass, `PlayerPreview`), made from your profile. A mannequin looks its skin
up from the profile. The mod gives you the spore's skin through `PlayerInfo.getSkin`
(`PlayerInfoSkinMixin`), which the mannequin never asks.

**Changed:** new `MannequinSkinMixin` on Minecraft's `ClientMannequin.getSkin`. A mannequin of you
wears the spore's skin while Mycelium has a spore (`MycSkins.forPlayer`, the same as your body).
Other mannequins keep their own. Emotecraft isn't changed (AGENTS.md rule 7): its mannequin doesn't
replace `getSkin`, so this reaches it. The log says "skins: a mannequin of you … wears your spore's
skin" the first time.

**Tested how:** the jar builds. I read Emotecraft's classes (unpacked into the scratch folder, not
the project) to see how it draws you.
**Not tested:** in Minecraft.

**Careful:** `Apply Mods.bat` empties the instance's mods folder and copies in `dist\minecraft-mods`.
Emotecraft isn't in `dist`, so put its jar there first or it's deleted.

## 2026-11-08 No flicker of the overlay and Mycelium's HUD when swapping spores

**Your report:** swapping spores, the Minecraft overlay disappears for a split second, and
Mycelium's UI comes back for that same moment.

**Why:**
- A swap made the mod start over as with a brand new player: phase Idle until the next physics step
  (FixedUpdate), which scans the new spore's ground and sends Minecraft there.
- In the log that was about 50 ms (13.64 s → 13.69 s), several frames at these frame rates.
- In Idle everything that means "Minecraft drives" switched off: the overlay, your Minecraft body
  (the spore was drawn), the input bridge, and the hiding of the nutrient panel, health bar and
  colony meter. All of it came back a few frames later.

**Changed:**
- `MinecraftDriver.FindPlayer`: a swap while Minecraft drives goes straight to WaitingForTeleport,
  and `FixedUpdate` sends the teleport to the new spore at its next step ("spore swapped").
- The nutrient panel, health bar and colony meter count WaitingForTeleport as driving (`Steering`),
  as the overlay and your body already did.
- `-mcSporeSwapTest` counts a pop-up's happiness faces apart from the spore's own renderers. The
  first run counted a pop-up on the spore next to you as "5 spore renderers drawn".

**Tested how:** stand-in, The Carcass, `-mcSporeSwapTest` (two swaps), `LINKTEST_MODEL=1`.

**Result:**
- **Before:** "phase Driving -> Idle (new player)", then in the same frame "nutrients … back as it
  was", "health … back as it was", "colony meter back as it was", "player model: hidden",
  "input bridge … Minecraft isn't driving".
- **After:** "phase Driving -> WaitingForTeleport (spore swapped …)", the teleport 40 ms later,
  confirmed 20–30 ms after that, then Driving. None of those lines in between.
- Every other spore: 0 of its renderers drawn after each swap (one had its pop-up open, 5 faces,
  drawn on purpose). No errors.
- One earlier run had an error inside the test's own pop-up step (opening a pop-up on a spore whose
  pop-up was already up); the next run didn't.

**Not tested:** in Minecraft (how it looks; a real Minecraft confirms the teleport in its own time,
during which the overlay now stays).

## 2026-11-08 Mycelium's health bar hidden while Minecraft's hearts show your health

**Your request:** remove Mycelium's health bar from its HUD, and leave the name part.

**Changed:** new `HealthPanel.cs`, the same rule as the nutrient panel. While Minecraft drives with
the overlay on, two objects under Mycelium's `HUD` are switched off:
- `HealthBarPanelCutoutMask`: the bar and its background;
- `HPNumber`: the "240/240" beside it.

The spore's name and trait (`SporeName`, `SporeTraitName`) stay. Mycelium keeps setting the bar and
number while they're off, and if it switches them on, they're switched off again that frame. They
come back as Mycelium had them when Minecraft stops driving, or with the overlay off. Test aid
`-mcHudDump` logs the health HUD's objects and what's shown.

**Tested how:** stand-in, Daybreak Arboretum, `-mcHudDump`, the fake Minecraft for 10 s, then gone.
**Result:**
- While it drove: "bar hidden, number hidden, name shown, trait shown", every second for 6 s.
  Health kept changing underneath (240 → 226 from enemy hits, sent to Minecraft as hearts).
- When the fake Minecraft stopped: "health bar back as it was (bar on: True, number on: True)".
- No errors.

**Not tested:** in Minecraft (how the HUD looks with the bar gone; the name may sit alone at the top
left).

## 2026-11-08 Defense Mechanism's bubble and green particles are drawn

**Your request:** Defense Mechanism's shield bubble shows in both third person views, and its green
particles show in every view.

**Why they weren't:** the skill puts its green particles (`DefenseBuildup`, then `DefenseBurst` 1 s
later) under itself, in the spore's skills. Its `DefenseChange` effect puts the bubble
(`DefenseParticles`) under the spore's model. While your Minecraft body stands in for the spore, the
mod hides everything under the spore (`CameraSync`), so these went with it.

**Changed:** `CameraSync.Shown` leaves them out of the hiding:
- the green particles in every view;
- the bubble in third person behind and in front;
- not the bubble in first person, where the camera is inside it.

When you switch views it's put right within the frame. Test aid `-mcDefenseTest` makes the bubble
(`DefenseChange`, 150%, 4 s long here) and both particle effects as the skill does, and logs which
parts are drawn.

**Tested how:** stand-in, Daybreak Arboretum, `-mcDefenseTest`, once per view (`LINKTEST_VIEW=0/1/2`,
`LINKTEST_MODEL=1` so your Minecraft body stands in).

**Result:**
- Behind and in front: the build-up particles, the burst particles and the bubble were all drawn.
- First person: both particle effects were drawn, and the bubble wasn't.
- No errors.

**Not tested:** in Minecraft (how it looks around your Minecraft body), and using the skill from its
key (the test makes its effects directly).

## 2026-11-08 Speed boosts widen the view past Minecraft's x1.5 limit

**Your report:** using Mycotoxins while already sprinting doesn't change the FOV.

**Why:**
- Minecraft widens its view with movement speed, (speed ÷ walking speed + 1) ÷ 2, then clamps it to
  at most x1.5 (`Camera.tickFov`). So it stops widening at twice walking speed.
- In your last run your Speed stat already made you x1.84. Walking gave x1.42, and sprinting (x2.39
  speed) would have been x1.70 but was cut to x1.5.
- Mycotoxins (+50% for 3 s) couldn't widen it any further.

**Changed** (your choice of three options, "raise the cap"): new `FovCapMixin`. While Mycelium is
playing, the limit is x2.5 instead of x1.5. Outside its levels it stays Minecraft's own. Mycelium's
camera follows the same number, as before (it allows a lens up to 179 degrees). The log says
"fov: the speed view limit is x2.5 while Mycelium plays".

**What to expect** with x1.84 from Speed, on Minecraft's default 70 degrees:
- walking x1.42;
- sprinting x1.70;
- Mycotoxins walking x1.88;
- Mycotoxins sprinting x2.29.

Minecraft's "FOV Effects" setting scales all of these down.

**Tested how:** the jar builds. I checked in Minecraft's code that 1.5 is the only such limit in
that method.
**Not tested:** in Minecraft. If the hook didn't fit, Minecraft would refuse to start with a mixin
error naming `FovCapMixin`.

## 2026-11-08 Relentless Fury does what its description says; red potion particles (protocol v50)

**Your request:** make Relentless Fury do what your game's description says: "Go into a frenzy
gaining 30% attack speed. While active lose 5% of current health every second. Attacks restore
health equal to 25% of weapon damage." Red potion particles on you while it lasts.

**Changed:**
- New `FuryWatch.cs`. While Minecraft drives, Mycelium's Fury effect (what the skill adds) keeps its
  8 s, sound, icon timer and cooldown, but:
  - its kill stacks are off: its hooks are taken off, so no damage bonus, and kills don't change
    its length;
  - every weapon's swings per second are x1.3 (`WeaponBag.AttackSpeedBonus`), which is your
    Minecraft attack speed;
  - 5% of current health goes every second. This is taken straight from `currentHealth`, with the
    bar sliding down: no hurt sound, blink, camera tilt or Minecraft red flash for it. A real hit
    still does all of those;
  - `RelentlessFury.isFrenzied` is on, and each Minecraft hit heals a quarter of its damage
    (`EnemyCombat`).
- New MycState flag `FURY` (protocol v50). New `MycFury.java`: red potion-effect particles around
  your player, about 7 a second. They're drawn over Mycelium's picture like Better Combat's swing
  trails. Mycelium's own red particles stay hidden with the spore.
- `-mcFuryTest` now also logs health and swings per second.

**Tested how:** stand-in, Daybreak Arboretum, `-mcFuryTest` with `LINKTEST_HIT=1 LINKTEST_HOLD=1`
(the fake hits the nearest enemy for 2 points every 2 s). The test kills an enemy at 2 s and 4 s.

**Result:**
- Swings per second went from 1.95 to 2.53 while it was on, then back to 1.95.
- Health went 240 → 228 → 220.9 … (5% of current each second), 7 drains in 8 s.
- Each 10-damage hit healed 2.5.
- The kills changed nothing: 1 stack, 8 s, damage still 45.0.
- It was over at 8.0 s. No hurt tilt, no errors.

**Not tested:**
- In Minecraft: the particles, the faster swings, and that the drain doesn't flash red. The jar
  builds.
- Using the skill itself: the test adds Fury as the skill does.
- While Mycelium plays without Minecraft, Mycelium's own Fury is left as it is.

## 2026-11-08 Relentless Fury looked into (no game change yet)

**Your report:** Relentless Fury seems to do nothing but play a sound.

**What Mycelium does:** the skill adds a `Fury` effect to you for 8 s, with a sound, red particles
and a timer on the skill icon. Each kill while it's on adds a stack, refreshes the timer and makes
it shorter (x0.8). Each stack adds 10% damage, but the first stack, the one you get for using it,
adds 0%: `Fury.AddBonus` multiplies by the stack count before counting it up. Its code comment ("so
they have something") and the skill's description ("Fury increases damage by 10%") say it was meant
to give 10% right away. So in Mycelium too, it does nothing until a kill.

**What the mod changes:** the particles are made under the spore's model. The mod hides every
renderer under the spore while it draws your Minecraft body (`CameraSync.KeepHidden`), so you don't
see them.

**Tested how:** stand-in, Daybreak Arboretum, new test aid `-mcFuryTest` (`LINKTEST_HOLD=1`). It
adds Fury as the skill does, kills the nearest enemy 2 s in through `EnemyTakeDamage` (as the mod's
Minecraft hits do), and logs every 0.5 s.

**Result:**
- Before the kill: 1 stack, the held Stick at x1.00, 45.0 damage sent to Minecraft.
- After the kill: 2 stacks, timer 6.4 s, x1.10, 49.5 damage sent.
- After 8 s: Fury gone, back to x1.00 and 45.0.
- So kills by the mod's hits do count, and the bonus reaches your Minecraft weapon. No errors.

**Not changed yet:** making the first stack count, and showing the particles. Asked you first.

## 2026-11-08 Stat icons in weapon tooltips; "+X All Stats"

**Your request:** "All Stats +X" becomes "+X All Stats", and the stat names in item tooltips become
Mycelium's stat icons.

**Changed:**
- `WeaponBag.StatText`: "+2 All Stats".
- `fabric/build.gradle`, new `statIcons` step: when the jar is built, it cuts Mycelium's four stat
  icons out of its own icon sheet (`2DArt/UI/TextIconSpriteSheet.png` in the Mycelium install).
  - The sheet is only read. The icons go into `fabric/build` and the jar, not the repository, and
    the jar in `dist` isn't tracked.
  - Which picture is which stat comes from Mycelium itself. Its spore pop-up writes `<sprite=0>`
    Primal, 1 Speed, 2 Sentience and 3 Vitality, and its sprite asset and the sheet's settings give
    each picture's place.
  - They become a small Minecraft font, `myceliumcraft:stat_icons`.
- `MycWeapons.attributeLine`: in each attribute line, a stat name right after an amount ("+3 Primal")
  is drawn as its icon, in its own colours. Other words stay ("Double attack speed" is untouched).
  "+X All Stats" stays as words. The Blacksmith's preview uses the same lines.
  - A jar built without the Mycelium install has no icons, and the names stay. The log says which:
    "stat icons in tooltips: yes/no".
- Weapon items are rewritten once after the update (their stamp changed), to get the new lines.

**Tested how:**
- The build log: 4 of 4 icons cut (Primal, Sentience, Speed, Vitality). I looked at the four
  pictures: the flexing red spore, the one with glasses, the running one, the heart.
- The fake Minecraft (`LINKTEST_ICONS=1`) built the tooltip lines:
  - "Beefy: +3 Primal, +3 Vitality" became "Beefy: +3 [Primal icon], +3 [Vitality icon]".
  - Savvy gave the Sentience and Speed icons.
  - "Golden: +2 All Stats" and "Rapid: Double attack speed, half damage" were unchanged.
- Stand-in, `-mcAttribute Golden`: "Golden: +2 All Stats".

**Not tested:** in Minecraft. How the icons look there (size, how they line up with the text) isn't
checked. They're 8 pixels tall, like letters.

## 2026-11-08 Stat attributes say what they give on every weapon item

**Your request:** in a weapon's hover box, an attribute that changes stats says by how much:
"Beefy: +3 Primal, +3 Vitality" instead of "Beefy". One that gives all four the same says
"All Stats +X".

**Changed:** `WeaponBag.ReadLive` reads each attribute's stat amounts (`primalAmount`,
`sentienceAmount`, `speedAmount`, `vitalityAmount`, which Mycelium sets for its stat attributes)
into its line. The stats are listed in the order Primal, Sentience, Speed, Vitality. An attribute
with its own description and stat amounts gets both. This holds for all weapons, including the
Blacksmith's preview and forged weapons, since they all come from the same text. The log now says
when a weapon's attribute text changes ("weapons: #N's attributes now: ...").

**Tested how:** stand-in, Daybreak Arboretum, `-mcAttribute <Name>` on the held Stick
(`LINKTEST_HOLD=1`).
**Result:**
- Golden: "Golden: All Stats +2"
- Beefy: "Beefy: +3 Primal, +3 Vitality"
- Strong: "Strong: +5 Primal"
- Nasty (no stats): "Nasty: 20% chance to apply poison", unchanged

**Not tested:** in Minecraft's hover box. A weapon's attribute text is cut at 159 bytes, as before,
so a weapon with three long attributes may lose the end of the last one.

## 2026-11-08 The Blacksmith's result preview shows the forged weapon's numbers (protocol v49)

**Your request:** the result preview works out the forged weapon's stats.

**Changed:**
- With all three slots full, Minecraft asks Mycelium for the result's numbers (the Forge block's new
  preview request).
- Mycelium builds the forged weapon out of sight, as the forge does (`WeaponBag.BuildPreview`).
- It works out its numbers like any of your weapons, every frame, with the spore as it is now:
  damage per swing, swings per second, knockback, multipliers, and reach (the base's, the same
  prefab).
- The result item gets the same name, numbers and attribute lines as a weapon item
  (`MycWeapons.applyPreview`), under "Forged by Blacksmith Ironspore" and its base. Until Mycelium
  answers (about 0.1 s) it says "Working out its numbers...".
- **Taking the result forges that same built weapon,** so you get exactly what the preview showed.
  Attributes are given fresh when a weapon is built, so a second build could differ.
- Protocol v49: the Forge block is 0x400 bytes, with a preview request and answer
  (`protocol/PROTOCOL.md`).

**Tested how:** stand-in, `-mcForgeTest`, `LINKTEST_FORGE=1 LINKTEST_STILL=1`. The fake asks for the
preview, then takes the result, and compares the two.

**Result:**
- Preview: 'Feline Vigorous Avocado Flamberge' 63.0 dmg, 2.15/s, knockback 4.5, x1.40 damage,
  reach 2.82 blocks, [Vigorous; Feline: meow].
- The forged weapon was exactly the same ("the preview you saw" in Mycelium's log).
- The base swung 1.95/s; Vigorous raises it, as it does when held.

**Not tested:** in Minecraft (the result item itself, and its refresh when the numbers arrive).

## 2026-11-08 The Blacksmith's result preview has the forged weapon's name

**Your report:** the result preview's name had the base weapon's own attribute in it.

**Why:** the preview was a copy of the base weapon's item, whose name is Mycelium's name for it. Mycelium
puts each attribute's name in front of the plain name ("Vigorous Avocado Flamberge").

**Changed:** new `ForgeName.java`. The preview is named the way Mycelium names the forged weapon: the
base's attribute names taken off the front, then the second donor's attribute and the first donor's in
front ("Smart Tough Avocado Flamberge"). Its "Base:" line shows the plain name too. The fake Minecraft
(`LINKTEST_FORGE=1`) works the name out for its three weapons and compares it with Mycelium's.

**Tested how:** stand-in, `-mcForgeTest`, `LINKTEST_FORGE=1 LINKTEST_STILL=1`.
**Result:** given 'Undergrown Avocado Flamberge' (base), 'Tough Rose Mace', 'Smart Opal Rapier'. The
preview name was 'Smart Tough Avocado Flamberge', and Mycelium's forged name was the same.

**Not tested:** in Minecraft (the preview item itself). An attribute whose name is empty would put the
names out of step; none was seen.

## 2026-11-08 Blacksmith Ironspore opens a smithing screen in Minecraft (protocol v48)

**Your request:** interacting with Blacksmith Ironspore (the first level) brings up Minecraft's
smithing table screen. The first slot is the weapon base, the next two are the attribute donors.

**How Mycelium's own Blacksmith works** (`ForgeShroom`): each interact takes the weapon you hold.
The first is the base. The next two each give their first attribute. The forged weapon is the base's
kind with only those two attributes, rated "Forged", and he forges only once. He takes no Stick,
nothing forged already, and nothing without an attribute.

**Changed:**
- New `ForgeWatch.cs` (Mycelium):
  - While Minecraft drives, your Interact on the Blacksmith asks Minecraft to open the screen.
  - Mycelium's own Interact is skipped for that press. This is done by a small component that runs
    before all of Mycelium's scripts in that frame.
  - It sends Minecraft which of your weapons he takes, and answers Minecraft's forge request.
- `WeaponBag.cs`:
  - `Forge` builds the weapon as `ForgeShroom.ProvideWeaponReward` does: the base's prefab with its
    own attributes taken off, then each donor's first attribute, all rated Forged. It puts the new
    weapon in your bag and takes the three you gave out.
  - A forged weapon keeps only its own attributes when it's rebuilt in your hand or thrown, as
    Mycelium's forged weapon does.
- New `MycForge.java` (Minecraft):
  - A menu with the smithing table's picture and slot places: base, donor, donor, then the result.
  - The slots take only weapons the Blacksmith takes.
  - The result shows the base with the two attributes it will get.
  - Taking the result asks Mycelium to forge. The item you took becomes the new weapon once Mycelium
    answers. If Mycelium refuses, you get a chat line saying why, and your three weapons come back.
  - Hovering an empty slot says what it's for.
- `MycWeapons.java`: weapons in the Blacksmith's slots count as yours (no duplicates), and the three
  you gave aren't put back while Mycelium forges.
- Protocol v48: the Forge block (`protocol/PROTOCOL.md`). Test aid `-mcForgeTest`, and the fake
  Minecraft's `LINKTEST_FORGE=1`.

**Tested how:** stand-in (`LINKTEST_FORGE=1 LINKTEST_STILL=1`), Daybreak Arboretum, `-mcForgeTest`.
The test:
1. Picks up an Avocado Flamberge, a Rose Mace and an Opal Rapier.
2. Brings the Blacksmith in front of the spore.
3. Asks for his screen.
4. The fake Minecraft puts the three into the slots in that order and takes the result, then tries
   the same three again.

**Result:**
- The Blacksmith took #2, #3 and #4, not the Stick.
- Mycelium's closest thing was the Blacksmith. Mycelium's own Blacksmith took 0 weapons.
- Forged #5 "Undergrown Feline Avocado Flamberge": the Avocado Flamberge with Feline (from the Rose
  Mace) and Undergrown (from the Opal Rapier), 0.1 s after the request. The three given were gone.
- The same three again: refused ("a weapon given isn't in your bag").
- He was done after that ("a Blacksmith here false"). No errors in Unity's log.

**Not tested:**
- The Minecraft side as a whole: the screen, its slots, the result, the item becoming your weapon,
  and the chat line. The stand-in can't show a Minecraft screen. The jar builds.
- The R key itself on the Blacksmith. In the stand-in the key press doesn't reach Mycelium (its
  window has no focus), so the test called the same code the key would.

## 2026-11-08 Interacting works again after picking up a weapon

**Found while testing the Blacksmith:** after a weapon pickup, Mycelium's code that finds what's
near enough to interact with (`SporeInteractableFinder`) threw an error every frame.
- The weapon you swap out goes into your bag, and this mod removes its object from the world.
- But Mycelium still had that weapon on its list of nearby things. It only takes a thing off the
  list when the thing walks out of range, and a removed thing never does.
- From then on nothing near you became the thing to interact with. In the test, the Blacksmith
  standing right in front was never picked.

**Changed:** `ForgeWatch.PruneFinder` takes removed things off that list every frame, before
Mycelium's code reads it. The log says "interact: N removed thing(s) taken off ...".

**Tested how:** stand-in, the same forge test (three pickups).
**Result:**
- Before: dozens of `NullReferenceException at SporeInteractableFinder.Update` in Unity's log, and
  the closest thing stayed "none".
- After: 0 errors. 3 removed weapons were taken off the list, and the Blacksmith became the closest
  thing in 0.1 s.

**Not tested:** in Minecraft by hand. If you noticed pop-ups or Interact stop working after
picking up a weapon, this was the likely cause.

## 2026-11-08 Skills go into cooldown again after lots of spore swaps

**Your report:** in a run with a different spore (Nala), your skills weren't going into cooldown.
Your log shows Death Blossom used about 40 times in 10 seconds.

**Why:** two of Mycelium's own bugs, made much worse by swapping spores many times (about 100 swaps
in The Carcass before that run).
- A trait hooks itself onto Mycelium's events (the `Actions` class) when its spore becomes yours,
  and should unhook when it stops. The Smarty Pants trait (Bub has it) hooks on again instead of
  unhooking. Each hook gives a 15% chance to cut a skill's cooldown to a quarter.
- The skill menu makes a copy of your spore every time it shows one. That copy's traits also hook on
  as if the copy were yours, and some never unhook. A destroyed Clumsy trait copy threw an error on
  every dodge in your run.
- So dozens of Smarty Pants hooks were on, for whichever spore was yours. Together they cut every
  cooldown to almost nothing.

**Changed:** new `TraitLeaks.cs`. After each swap (and once a second), trait hooks on Mycelium's
events stay only if their trait still exists, is on your spore, and isn't a repeat. Every spore's
traits stay hooked to `SwappedCharacter`, because that's how a trait hears its spore became yours.
A first version removed those too, and no trait came on after a swap (seen in the test, fixed before
this entry). Test aids: `-mcTraitLeakTest`, `-mcSkillCooldownTest`, `-mcNoTraitFix` (switches the fix
off).

**Tested how:** stand-in (`LINKTEST_MODEL=1`). In The Carcass, `-mcTraitLeakTest` swaps 20 times in
the skill menu, then uses the spore's first skill 10 times. It reads each cooldown after every trait
has had its go. `-mcSkillCooldownTest` in Daybreak checks Nala's three skills without swapping.
**Result:**
- **Fix off:** 29 Smarty Pants hooks after 20 swaps. Zombify (normally 10 s) got 0.00 to 0.63 s.
  This is your bug.
- **Fix on:** 36 leftover hooks were removed, and exactly 1 Smarty Pants hook was left (the spore
  that was yours has it). The cooldowns were 10 s, or 2.5 s now and then (that trait's own 15%
  chance).
- **No swaps:** Nala's Death Blossom, Blitz and Living Cyclone all cooled down normally (3.5 s,
  3.5 s, 7.0 s).
- The only errors were Mycelium's own, from its spore copies (`ComboTrait`, `EnduringTrait`), as
  before.

**Not tested:** in Minecraft by hand. Only events in `Actions` are cleaned. A trait that hooks onto
something else (ComboTrait hooks onto your attacks) can still be left behind, and isn't handled.

## 2026-11-08 A spore's own Living Cyclone no longer shows the overlay over the shop

**Your report:** if a spore did a move and you then picked it in the shop, the Minecraft overlay
showed until the move ended.

**Why:** the other spores use their skills on their own as moves (Mycelium's `IdleWalking` and
`SkillCurio`). If you take one over while its Living Cyclone is still going, the no-weapon fix from
earlier today (`UnarmedCyclone`) took that for your own spin. A spin counts as play, not Mycelium
taking the controls, so the overlay showed over the shop until the spin was over.

**Changed:** `UnarmedCyclone.cs`. A use counts as your spin only if the mod saw the skill ready
while that spore was yours. A move that was already going when the spore became yours doesn't count.
Its cooldown is still started when it ends, so the skill isn't stuck. New test aid:
`-mcSporeMoveTest`.

**Tested how:** stand-in (`LINKTEST_MODEL=1`), The Carcass.
- `-mcSporeMoveTest`: another spore uses Living Cyclone, then the controls go off as the shop has
  them, and that spore becomes yours as the shop's spore list does.
- `-mcUnarmedCycloneTest`, to check your own spin still counts.

**Result:**
- The move wasn't counted as your spin, and the overlay stayed off for all 8 s. Its cooldown started
  6 s in (no spins were seen), and no exceptions.
- Your own spin still counted: 1.25 s, then its cooldown.

**Not sure:** when that spore's move ends, Mycelium itself switches your controls back on
(`Skill.EndSkill` calls `EnableController` for whichever spore is yours now). The test switched them
off again, as the shop has them. Whether the real shop does the same is **not tested**. If the
overlay flashes when the move ends, that's the cause.

## 2026-11-08 No flash of the Mycelium spore when swapping in the skill menu

**Your report:** swapping spores in the shop's skill menu showed the Mycelium spore for a split
second.

**Why:** the menu's swap buttons (`SwapNext` / `SwapLast`) make a new copy of the spore. They run
later in the frame than `MenuBody.Update`, so the new copy was drawn for one frame before it was
hidden.

**Changed:** `MenuBody.cs`. Just before any camera draws (`RenderPipelineManager.beginCameraRendering`),
a copy the menu made since Update gets its renderers off. This only happens while your body is being
shown there. `-mcSkillMenuTest` now also swaps spores twice while the menu is open.

**Tested how:** stand-in (`LINKTEST_MODEL=1`), The Carcass, `-mcSkillMenuTest`.
**Result:**
- On both swaps (and on opening the menu), the new copy still had its 3 renderers on just before
  drawing. It was hidden in that same frame, so it was never drawn. Your body was shown again next
  frame.
- There was 1 exception, from Mycelium's own code (`EnduringTrait.SporeSelected`, while Mycelium makes
  the copy). Not from this mod.

**Not tested:** in Minecraft by eye. Your body may lag one frame behind the swap, at the same spot.

## 2026-11-08 Your Minecraft player in the shop's skill menu

**Your request:** in the shop's skill menu, show the display spore as your Minecraft player.

**Changed:** new `MenuBody.cs`, plus `McPlayerModel.BodyMesh` and `BodyMaterials`.
- Mycelium's skill menu shows a bigger copy of your spore to its own camera. While it's there, the
  copy isn't drawn, and your Minecraft body is drawn in its place.
- It's the same body Minecraft sends for third person, so it wears what you wear.
- It's sized as the copy is, turned as the copy is, and stands at its feet.
- Test aid: `-mcSkillMenuTest`.

**Tested how:** stand-in with its fake body (`LINKTEST_MODEL=1`), The Carcass, `-mcSkillMenuTest`.
**Result:**
- The body was shown as the menu opened, at 2.65x, facing the menu's camera (0° off), with the
  copy's 5 renderers off.
- It was hidden once the menu closed. No exceptions.

**Not tested:** in Minecraft with your real body, and how it looks. The facing assumes your Minecraft
body faces the way your spore does, which is true while Minecraft drives.

## 2026-11-08 Living Cyclone with no weapon (The Carcass)

**Your report:** Living Cyclone in The Carcass didn't do what it does in a run, and it couldn't be
used again while you stayed there.

**Why:** you hold no weapon in The Carcass.
- Mycelium's Living Cyclone holds its cooldown back while it spins, and starts it after the third
  spin, but only when you hold a weapon. With none, it stays switched off until the scene changes.
  That's Mycelium's own bug.
- This mod knew a cyclone spins only from the weapon (`WeaponCollision.isCycloning`). So with no
  weapon, Minecraft didn't go to third person or play the spin. Your log shows no "cyclone" lines.

**Changed:** new `UnarmedCyclone.cs`. With no weapon, the skill counts as spinning from being used
until the spore's animator starts again (Living Cyclone stops it for the spins). Then its cooldown is
started, as it would be with a weapon. `MinecraftDriver.Cycloning` includes it. Test aid:
`-mcUnarmedCycloneTest`.

A first try used `PlayerController.looking` for the spins' end. It went on and off during the
spins, so it was dropped.

**Tested how:** stand-in, The Carcass, `-mcUnarmedCycloneTest`. **Result:**
- Spinning for 1.26 s, and the fake Minecraft saw the spin start and end.
- The cooldown started, and the skill could be used again 8.5 s after it was used.

No exceptions. **Not tested:** in Minecraft, by hand.

## 2026-11-08 The player list in 2 columns

**Your request:** split the players in the player list into 2 columns.

**Changed:** new `PlayerTabColumnsMixin.java`. Minecraft adds a column to the list only when one
column would hold more than 20 rows. Now that limit is half the rows, rounded up, so 2 or more rows
always make 2 columns. A list longer than 40 still gets a third column, as before. A single row
(you alone) stays 1 column.

**Tested how:** the mod jar builds. **Not tested:** in Minecraft (I can't run it). The log line
"player list: N rows in 2 column(s)" will show whether it worked.

## 2026-11-08 Leaving the shop puts max health back

**Your report:** changing the health stat in the shop without committing still changed your max health.

**Why:** a bug in Mycelium's own shop (`LevelUpManagerNew`):
- Raising a stat works out the new stats at once, and that sets max health and walking speed too.
- Closing the shop puts back the levels and the base health, but not max health or walking speed.
- Lowering a stat in the shop doesn't work anything out, so "up, then down, then commit" also kept
  the higher health.

It shows in Minecraft because max health is sent there.

**Changed:** new `ShopUndo.cs`. When the shop closes (by Close or Commit), the stats are worked out
again from the levels as they are then (`CharacterStats.StartCalculateAttributes`). Current health
keeps its share of max health, as before. Test aid: `-mcShopTest`. It buys nothing and puts
nutrients back.

**Tested how:** stand-in, The Carcass, `-mcShopTest`. **Result:**
- Vitality up took max health from 100 to 110. Close took it back to 100.
- Vitality up then down, then Commit, also ended at 100.

No exceptions. **Not tested:** buying a level for real (Commit after only raising), and the real shop
in Minecraft. I also didn't run the test without the fix to see the bug in the stand-in; the cause
above comes from reading Mycelium's code.

## 2026-11-07 Your Minecraft body in cutscenes

**Your report:** the Minecraft player model still isn't visible during the boss's death cutscene.

**Why:** your log shows the fix below working ("a cutscene's camera is live; the spore is drawn" at
21:08:59), but in first person your Minecraft body stands in for the spore and is drawn as shadow
only, so nothing showed.

**Changed:** `McPlayerModel.cs`: the whole body is drawn while a cutscene's camera is live
(`CameraSync.CutsceneLive`, set each frame by `MinecraftDriver.cs`).

**Tested how:** stand-in, first person, `-mcGrowTest` (the new spore's cutscene; the stand-in can't
start the boss's). **Result:** "your whole Minecraft body is drawn (a cutscene's camera)" as the
cutscene's camera came on, and back to shadow only 2.49 s later as it ended. No exceptions.
**Not tested:** the boss's death cutscene itself.

## 2026-11-07 The boss bar waits for the fight

**Your report:** the boss bar shows up before the cutscene is even triggered.

**Why:** Mycelium opens its bar when you come near the boss (`BossHealth`'s trigger). In your log
that was at 20:31:17, and the intro took the controls at 20:31:25. My intro-camera check from the
entry below didn't cover the 8 s before the intro.

**Changed:** `BossBar.cs`: shown only once the boss's attacks are on (`MonsterBossAttack.enabled`).
`StartBoss` switches them off at the level's start, and `BossCam` switches them on at the intro's end.
The intro-camera check is gone. `-mcBossBarTest` now starts the fight 2 s after the meeting.

**Tested how:** stand-in, Impact Barrens, `-mcBossBarTest`. **Result:** nothing in Minecraft for the 2 s
after the meeting, then "'Rival Sporemother' at 100%" as the attacks came on, 60%, 0%, gone. No exceptions.
**Not tested:** a real intro.

## 2026-11-07 Stuck pop-up, boss bar after the intro, you're drawn in cutscenes

**Your report:** after throwing a weapon and salvaging it quickly the pop-up never went; the boss bar
showed a bit too early; you were invisible in the boss's death cutscene.

**Why:**
- Pop-up: your log has the salvage 34 ms after the throw, and no error. How the pop-up outlived the
  weapon isn't known, so the fix catches the result rather than the cause.
- Boss bar: your log has the intro taking the controls one frame after Mycelium opened its bar
  (19:26:55.024 and .040); the overlay only hides 0.3 s later.
- Invisible: in first person the spore is hidden (the camera is inside it), and that stayed while
  the cutscene's own camera looked at you.

**Changed:**
- `WeaponBag.cs`: `DropOrphanPopUp`, every frame: a pop-up whose thing has been destroyed goes.
- `BossBar.cs`: not shown while the intro camera is on or Mycelium has the controls (not for a
  knockback).
- `CameraSync.cs`: in first person the spore is hidden only while its own camera is the live one.
- Test aids `-mcPopUpTest`, `-mcBossIntroTest`; `-mcBossDeathTest` logs the live camera.

**Tested how:** stand-in.
**Result:**
- `-mcPopUpTest`: pop-up made, its thing destroyed, the pop-up taken away within 0.5 s.
- `-mcBossDeathTest`: the Minecraft bar went 0.01 s after the kill (one frame at 0% first).
- **Didn't test what it should:** `-mcBossIntroTest` walked into the boss's trigger but the intro camera
  never came on, so the bar showed at once there. In `-mcBossDeathTest` the boss never reached its
  "Death" animation, so the death camera never came on and the spore stayed hidden (the main camera
  was live, as it should be then). Both need a real fight.

## 2026-11-07 The overlay goes during the final boss's death cutscene

**Your report:** the overlay stayed during the boss death cutscene, and you could swing your weapon.

**Why:** your log at 19:27:23.76 shows the boss's death switching Mycelium's "Player" input map off
(the hold began), then at 23.96 it was on again (`PlayerController.EnableController`, by the look
of it), so the hold ended and the overlay never went.

**Changed:** `MinecraftDriver.cs`: on Impact Barrens, a beaten Rival Sporemother counts as Mycelium
having the controls until the level ends. New test aid `-mcBossDeathTest`.

**Tested how:** stand-in, Impact Barrens, `-mcBossDeathTest` (the real BossHealth.EnemyTakeDamage).
**Result:** the hold began 0.02 s after the kill and lasted all 10 s checked; the overlay hid
0.3 s after the kill and stayed hidden. No exceptions.
**Not tested:** a real fight to the end of the cutscene and the level change after it.

## 2026-11-07 The final boss's health is Minecraft's boss bar (protocol v47)

**Your request:** on the final level, use a Minecraft boss bar instead of Mycelium's boss health UI.

**Changed:**
- New `BossBar.cs`: in Impact Barrens while Minecraft drives, Mycelium's bar (`HUDBoss`'s holder) is
  switched off, and its name and fill go to Minecraft. Put back as it was otherwise.
- New `MycBossBar.java`: one red Minecraft boss bar, shown while Mycelium's would be.
- New Boss block, test aid `-mcBossBarTest` (meets the boss, then 600/1000, then beaten).
  Protocol 46 -> 47: install the new jar.

**Tested how:** stand-in, Impact Barrens, `-mcBossBarTest`.
**Result:** Mycelium's bar hidden once driving began; the stand-in read none, then "'Rival
Sporemother' at 100%", 60%, 0%, then none 0.08 s later as Mycelium's shrank shut. No exceptions.
**Not tested:** Minecraft's bar on screen, and a real fight (the boss's own trigger and damage).
Minibosses on other levels keep Mycelium's bar.

## 2026-11-04 The Stickbug Spear's shot starts on the crosshair line

**Your report:** the hand lighting is good now; the spear's shot looks a little off-centre.

**Why:** I kept Mycelium's spot, 1 up and 1 right of the spore, and the shot flew parallel to your aim
from there, so it never met the middle of the screen.

**Changed:** `EnemyCombat.cs`: the shot starts on the line through the middle of the screen, 4 units
past the spore.

**Tested how:** stand-in with `-mcSpearTest`, 2 swings. **Result:** both shots logged 0.000 units off
the screen's middle at the start, no exceptions. The upward one kept its line sideways.
**Still off:** the shot sinks slowly, about 0.5 units/s of speed lost per second upward, as Mycelium's
own projectile does, so it drops a little under the crosshair at long range. Looking steeply down,
the shot can start under the ground (y -0.25 in the test) and vanish at once.
**Not tested:** the real spear in Minecraft's first person.

## 2026-11-04 The Stickbug Spear shoots; your hand's light turns with your view

**Your report:** one side of your hand goes dark looking straight up, and the Stickbug Spear doesn't
shoot.

**Why the spear:** Mycelium shoots it inside `PlayerAttack.Attack`, which Minecraft's swings don't use.
**Why the hand:** Minecraft's two lights are fixed in the world, and the hand turns with your view.

**Changed:**
- `EnemyCombat.cs`: `ShootSpear`, on each swing of the spear when its wind-up is over. Same projectile,
  material, tag, spot and speed as Mycelium's, but it goes where you look, not where the body faces.
- `HandLightMixin.java`, new `LightingAccessor.java`: for the normal hand the lights turn with your
  view, and are put back after.
- New test aid `-mcSpearTest`: the held weapon goes by the spear's name.

**Tested how:** stand-in, Daybreak Arboretum, `-mcSpearTest`, `LINKTEST_FUNGAL=1` (2 swings).
**Result:** 2 shots. One aimed downward stopped at the ground straight away. One aimed up flew from
(3.24, 1.29, 272.36) to (3.24, 17.75, 229.11) in 5 s at about 10 units/s, rising less as it went. No
exceptions in the log.
**Not tested:** the real spear (its object's name is assumed to start with "Stickbug Spear"), a shot
hitting an enemy, and the hand lighting, which the stand-in can't run at all. If the new mixin is
wrong, Minecraft may not start.

## 2026-11-04 The real lighting of your normal hand

**Your report:** you meant your normal hand, not Better Combat's arms, and it looks the same.

**My first fix was aimed wrongly.** Your log showed "block 0 sky 15": your hand wasn't in shade, so
the light level wasn't the cause in that session.

**Likely cause:** Minecraft sets the world's lighting directions in the level drawing, which the
overlay skips, so the hand used what was left from the HUD's items. Only frames where Better Combat's
arms or swing trails were drawn set the world's. That's read from Minecraft's code, not measured.

**Changed:**
- `HandLightMixin.java`: sets the world's lighting before the normal hand.
- `MycFirstPersonArms.java`: my change is taken back out; Better Combat's arms are as they were.
- `MycHandLight.java` stays, for the normal hand only: full light level, which also covers
  Minecraft's night.

**Tested how:** the jar builds. **Not tested:** whether the hand now looks right.

## 2026-11-04 Your hand is always fully lit in the overlay

**Your report:** your hand and held items in the overlay look too dark at times.

**Why (likely, not confirmed):** Minecraft lights them from the light level where your player
stands, and the invisible ground blocks can shade that spot. The new log line will show it.

**Changed:**
- New `MycHandLight.java`, `HandLightMixin.java`: full light while the overlay is on.
- `MycFirstPersonArms.java`: the same for Better Combat's arms.

**Tested how:** the jar builds. **Not tested:** all of it. The stand-in has no Minecraft renderer,
so the new mixin hasn't run at all yet.
**Also seen:** your Minecraft log from 09:22 shows the spore collision working: "shoved your player
13 time(s)" and "you pushed spores 0.26 blocks".

## 2026-11-04 You collide with the other spores by Minecraft's rules (protocol v46)

**Your request:** you could still just walk through them; use Minecraft's collision system.

**Changed:**
- New `SporeProxy.java`, `SporeProxies.java`: an invisible creature per other spore, sized like your
  player, on that spore every server tick. Minecraft's own creature collision shoves it and you.
- New `ColonyPush.cs`: sends the spores' feet, and moves a real spore by what its stand-in was shoved.
- `ColonyPassThrough.cs`: the push of the entry below is gone. Mycelium's physics still ignores the
  spores, or they'd pin you again.
- New SporeMoves block, and `LINKTEST_PUSH=1`. Protocol 45 -> 46: install the new jar.

**Tested how:** stand-in, The Carcass, `LINKTEST_PUSH=1`: a fake push of 1 block on one spore.
**Result:** 10 slots reached the stand-in, and Mycelium logged "Pudd pushed 1.40 units", which is 1
block at this scale. Its position was also changing as it wandered, so that line is the evidence
rather than the position.
**Not tested:** everything in real Minecraft: that the jar loads with the new creature, that the
creature shoves you and you shove it, and how strong that feels. The stand-in has no creatures.
**Failed along the way:** one edit stopped when `MinecraftDriver.cs` couldn't be written ("Invalid
argument", cause not known), and the build started on the half-done code ran past 10 minutes and was
stopped. Writing the file again worked, and so did the next build.

## 2026-11-04 You nudge the spores you walk through

**Your request:** you'd still like to push the other spores around a little.

**Changed:**
- `ColonyPassThrough.cs`: within 1.3 units, a spore you walk toward slides away at half your speed,
  6 units/s at most. It's moved directly, because a standing spore weighs 10000 in Mycelium. Spores
  at a curio or digging out stay put. The collision switch is now set again on every look, since a
  collider switched off and on can forget it.
- `MinecraftDriver.cs`: calls the push every physics step. New log line `held back:` names what's
  near your spore when a 6-unit re-sync fires.
- `-mcBumpTest` no longer holds the spore in place, so it can be pushed.

**Tested how:** stand-in walking in The Carcass, 14 runs of `-mcBumpTest` with the push in.
**Result:** with open ground ahead, you fell 0.24 and 0.51 units behind, and the spore went 1.26 and
0.98 units. In the other runs your spore passed or overlapped the test spore, then stopped against
the Carcass's Terrain or Carcass_RibCage: the stand-in walks a fixed path, blind to scenery. Named by
the log in most of those runs; 3 earlier runs stopped at the same spots before the log could name it.
**Not explained:** in one run the test spore went 4.66 units, more than a push should give. It was
placed beside the rib cage, so physics may have shoved it out. Not checked.
**Not tested:** how the push feels in real play, and pushing a walking spore off a ledge.

## 2026-11-04 You walk through the other spores

**Your report:** you can get stuck when bumping into other spores.

**Why:** your log at 08:30:15 shows your spore frozen at (5.95, 0.22, 0.63) with Bub 1.3 units away,
while Minecraft's player walked on. Minecraft doesn't know about the spores. The gap reached 6.1
units, then a re-sync teleport put your player back.

**Changed:**
- New `ColonyPassThrough.cs`: your spore's solid colliders ignore the other spores' while Minecraft
  drives. Put back when it stops, or when you swap spores.
- `MinecraftDriver.cs`: calls it every frame. New test aids `-mcBumpTest` and `-mcBumpTestSolid`
  (the same with spores solid, to compare).

**Tested how:** stand-in walking in The Carcass, another spore held 2 units ahead for 3 s.
**Result:** spores solid (`-mcBumpTestSolid`): fell 6.03 units behind, 1 re-sync teleport. With the
fix (`-mcBumpTest`): at most 0.20 units behind, 0 re-sync teleports, 9 collider pairs off.
**Not tested:** real play in Minecraft, swapping spores with this on, and whether anything in
Mycelium relied on spores bumping into yours.

## 2026-11-04 Nutrient Multiplier in the player list during a run (protocol v45)

**Your request:** during a run, show "Nutrient Multiplier: X%" in the player list instead of Colony
Happiness, with no bar.

**Changed:**
- `ColonyWatch.cs`: outside The Carcass, sends Mycelium's bonus, worked out as its HUD does.
- `MycColony.java`: shows that one line in place of the bar. In The Carcass it's as before.
- New test aid `-mcLoop <n>`. Protocol 44 -> 45: install the new jar.

**Tested how:** stand-in, reading the number Minecraft receives.
**Result:** The Carcass -1 (happiness stays), Daybreak Arboretum 0, Daybreak Arboretum with
`-mcLoop 3` 100.
**Not tested:** the footer text in real Minecraft, and the change when a run starts or ends.
**Found:** Mycelium's own "+X%" is too high on even loops. It says +50% on loop 2, but the amounts
are multiplied by (loop + 1) / 2 in whole numbers, which is x1 there, so no bonus. Loop 4 says +150%
and gives +100%. The list shows the same figure as Mycelium's HUD.

## 2026-11-04 Mycelium's nutrient panel is hidden

**Your request:** get rid of Mycelium's nutrient count, since Minecraft's experience level shows it.

**Changed:**
- New `NutrientPanel.cs`: switches off `ResourcePanels/NutrientsPanel` on the HUD while Minecraft
  drives and the overlay setting is on, and puts it back as it was otherwise.
- `MinecraftDriver.cs`: calls it every frame, and on a new scene.

**Tested how:** stand-in, The Carcass. Log: "panel hidden ... (on before: True)" once driving began,
"panel back as it was (on: True)" when the stand-in left. The nutrient count was still read (411516).
**Not tested:** how it looks in the real game, and going from one level to another with the panel hidden.
**Side effect:** the "+50%" nutrient bonus of later loops is on the same panel, so it's hidden too.

## 2026-11-03 No flash at the cutscene's start; smoother new spore

**Your report:** the UI and overlay showed for a split second as the cutscene started, and the new
spore's animation looked like a lower framerate.

**Why the flash:** your log showed the cutscene on, off 0.0 s later and on again 0.06 s later. Closing
the shop starts a re-sync teleport, and `BirthCutscene` counted only "Driving", so it switched off
for that teleport. The stand-in couldn't show this: its grow doesn't go through the shop.

**Why the lower framerate:** `MycSporeBodies` posed at most 30 times a second; your own body is posed
every Minecraft frame.

**Changed:**
- `MinecraftDriver.cs`: the cutscene holds through the teleport, and is checked again just before
  drawing, because the shop's button turns the HUD on after our Update.
- `MycSporeBodies.java`: no limit: a pose every Minecraft frame while a spore walks or sprouts.
- `-mcGrowTest` now also sends a teleport right after the grow, as the shop closing does.

**Tested how:** stand-in, `-mcGrowTest`: the cutscene went on once and over once 2.5 s later, right
through the teleport, with the overlay hidden the whole time. The jar builds. **Not tested:** buying
a spore in the real shop, and how smooth the pose rate looks in Minecraft.

## 2026-11-03 No HUD or overlay during the spore birth cutscene

**Your request:** hide both the UI and the overlay during the cutscene.

**Changed (Mycelium only):** new `BirthCutscene.cs`: while Mycelium's "GrowCamera" is the live
camera, the HUD's alpha is held at 0 and the overlay is off, and both come back after. Mycelium's
"was born" notification stays (own canvas): it's the one place the new spore's name shows.

**Tested how:** stand-in with a test overlay picture, `-mcGrowTest`: the cutscene started 0.01 s after
the grow, HUD alpha 1.00 -> 0 and overlay hidden; after 2.5 s the HUD was back at 1.00 and the overlay
showing. Before the grow the grow camera's priority was 0 and the main's 10, so normal play isn't
affected. The test spores don't reach your save: Rose from the last test wasn't in this run's colony.
**Not tested in real play:** opening the shop and buying a spore.

## 2026-11-03 A spore grown in the shop digs out of the ground (protocol v44)

**Your request:** the new spore should crawl out of the ground during its spawn cutscene, with the
same animation you use when spawning in The Carcass.

**Changed:**
- `ColonyWatch.cs`: sends how far through Mycelium's "Sprout" each spore is (new Colony field), every
  frame while one is sprouting.
- `MycSporeBodies.java`: poses that body with `EmergePose`, and sends it on every pose meanwhile.
- New test aid `-mcGrowTest`: grows a spore the way the shop does. The stand-in prints the sprout.
  Protocol 43 -> 44: install the new jar.

**Tested how:** stand-in, The Carcass, `-mcGrowTest`: new spore "Rose" grew, its progress reached
the stand-in from 0.01 to 0.89 in even steps, the Sprout ended after 2.84 s, and its body was shown
within 0.05 s of the spawn. The test spore isn't saved: Mycelium was killed, and it saves on pause or quit.
**Not tested in Minecraft:** the real dig-out on that body, seen from the cutscene's camera.

## 2026-11-03 The happiness faces are back on the spore popup

**Your report:** the popup when looking at other spores has lost its little happiness faces.

**Why:** my own bug. The popup hangs under the spore it's about, and its faces are sprites
(`HappinessSlider.prefab`, "Faces"). `McColonyBodies` hid every renderer under a spore, so they went
too; the rest of the popup is UI, which isn't a renderer, so it stayed.

**Changed (Mycelium only):** `McColonyBodies.cs` leaves alone anything on a canvas.
`-mcSporeSwapTest` also opens a popup on another spore and counts the faces drawn.

**Tested how:** stand-in, The Carcass, `-mcSporeSwapTest`: a real popup on Cline had 5 of 5 faces drawn, and the spores themselves stayed hidden (0 renderers drawn). **Not tested** by looking at a spore in real play.

## 2026-11-03 The other spores are your size; the cap matches in brightness too

**Your report:** the other spores look like Minecraft characters now, but bigger than you.
**Your request:** match the cap's brightness as well as its hue.

**Why bigger:** your player is scaled down to the spore's height (Minecraft's scale attribute); the
made-up player that poses the bodies was left at full size.

**Changed (Minecraft only):**
- `MycSporeBodies.java`: each body takes your player's scale, and is sent again if it changes.
- `MycSkins.java`: every clearly coloured pixel of `spore_cap.png` has its brightness scaled too, so
  the picture's main colour has the cap's brightness. The white spots stay as drawn.

**Tested how:** the jar builds. **Not tested in Minecraft.**

## 2026-11-03 The other spores' bodies work; the cap picture is hue shifted

**Your report:** the other spores still looked like Mycelium spores. **Your request:** `spore_cap.png`
now has colour, so hue shift it instead of colouring a grey picture.

**Why they stayed spores:** your `latest.log` showed "couldn't pose a body", caused by "Tried to
access entity ID before ID assignment". The world gives entities their ids, and the made-up player
is never in the world, so Minecraft's renderer refused it and the feature switched itself off.
The stand-in couldn't catch this: it sends boxes, not Minecraft's renderer.

**Changed (Minecraft only):**
- `MycSporeBodies.java`: the made-up player gets a made-up id. After a failure it stops at once
  (it logged three times), and tries again on a new link.
- `MycSkins.java`: every pixel of `spore_cap.png` has its hue turned so the picture's main colour
  (#CD1331) takes the cap's hue. Brightness, colour strength and the white spots stay.

**Tested how:** the jar builds. **Not tested in Minecraft.**

## 2026-11-03 Every spore is a Minecraft character in its own colours (protocol v43)

**Your request:** make each spore in the colony a Minecraft character like the player, with your
two skin parts coloured to match its cap and body and combined into a skin; the player too.

**Changed:**
- New `MycSkins.java`: tints `dist/skin/spore_cap.png` and `spore_body.png` and combines them;
  `build.gradle` puts the two pictures in the jar. New `PlayerInfoSkinMixin`: your player wears
  the skin of the spore you control. The player list's rows use the spores' skins.
- New `MycSporeBodies.java` and `McColonyBodies.cs`: Minecraft poses a body per other spore (new
  Bodies block), and Mycelium draws it at the spore's feet instead of the spore.
- `ColonyWatch.cs` also sends cap colours and walking speed, every 0.1 s instead of 0.5 s.
- **Bug fixed:** swapping spores left Minecraft driving the old spore (`rb` was found only once).
  The new test aid `-mcSporeSwapTest` showed it. `MinecraftDriver` now follows the swap.
- `McPlayerModel.cs`: its mesh building is shared with the bodies. Protocol 42 -> 43.

**Tested how:** stand-in in The Carcass with `LINKTEST_MODEL=1`, which sends a box as each body: 4
spores with their cap and body colours came through, 3 boxes stood at the spores' feet (y 1.01, the
same as your feet) and followed them as they wandered, facing and speed logged (1.6 blocks/s). Two
swaps: 0 spore renderers drawn on any spore after each. All spores drawn again when Minecraft went.
The stand-in's FAIL lines are its checks for FakeMycelium's test level.
**Not tested in Minecraft:** the skins, the real posed bodies, walking legs, heads in the list.

**Failed first:** the swap test showed the new spore drawn under your body, 1 s after the swap.
Cause: the old `rb` bug above. Unity's first build after adding a file failed again (CS0246); the
second was clean.

## 2026-11-03 The player list footer says "Colony Happiness"

**Your request:** change the word Colony to Colony Happiness.

**Changed (Minecraft only):** `MycColony.java`, the footer's title line.

**Tested how:** the jar builds. **Not tested in Minecraft.**

## 2026-11-03 The other spores are rows in the player list, like players

**Your request:** the happiness meter looks good; make the other spores look like other players in
the list rather than names in text.

**Changed (Minecraft only):**
- New `PlayerTabRowsMixin`: the player list's rows (`PlayerTabOverlay.getPlayerInfos`) get the
  other spores added after the real players.
- `MycColony.java` makes a `PlayerInfo` per spore: a fixed made-up id from its name, the name
  (at most 16 characters), and one of Minecraft's built-in skins via the new `PlayerInfoAccessor`
  (Minecraft's own lookup can ask Mojang's skin servers; this stays offline).
- The footer keeps "Colony", the bar and the word; the names are gone from it.

**Tested how:** the jar builds. **Not tested in Minecraft** (the stand-in has no player list).

## 2026-11-03 Your colony in the player list instead of Mycelium's colony meter (protocol v42)

**Your request:** get rid of Mycelium's colony count UI; put every spore's name in your colony in
Minecraft's player list, with a text bar under the names that fills up or down like the
happiness bar.

**Changed:**
- New `ColonyWatch.cs`: hides `HUDHappiness`'s colony text and slider while Minecraft drives
  (back as Mycelium had them after), and every 0.5 s sends every spore's name, body colour and
  happiness, which one you control and the average (new Colony block, after Thrown).
- New `MycColony.java`: the player list's footer shows "Colony", the names in their colours
  (yours bold), a 40-segment `|` bar of the average that moves toward it at 0.6 per second, and
  Happy / Content / Unhappy.
- The stand-in prints the colony it gets. Protocol 41 -> 42: install the new jar.

**Tested how:** stand-in, The Carcass: Mycelium sent "2 spore(s), average 0.25: Cline (you) 0.00
#E3DEFF, Gerf 0.50 #D7D378", the fake Minecraft got the same, and the meter was hidden (both parts
were on before). **Not tested:** the footer in Minecraft, the bar's fill animation, the meter
coming back after Minecraft stops driving.

**Note:** Unity's first build after adding a new file failed (CS0246, ColonyWatch not found: it
compiled before importing the file); a second build was clean.

## 2026-11-03 Your player goes by the spore's name (protocol v41)

**Your request:** change the Minecraft player's display name to the name of the spore you control.

**Changed:**
- Mycelium sends `CharacterStats.sporeName` of the current spore (MycState 0xC0, looked up every
  0.5 s, so a swap shows within half a second).
- Minecraft: `MycName.java` holds it. `PlayerDisplayNameMixin` puts it into
  `Player.getDisplayName` (chat, death messages), `PlayerTabNameMixin` into the player list.
  Only while Mycelium has a spore in a level, and only your own player. Your real Minecraft name
  (profile) is untouched.
- The stand-in prints the name it gets. Protocol 40 -> 41: install the new jar.

**Tested how:** stand-in, Daybreak Arboretum: Mycelium logged "the spore you control is 'Cline'",
the fake Minecraft got "called 'Cline'". **Not tested in Minecraft** (the mixins only run there).
Spore swaps not tested. The stand-in's "weapons version: got 3, expected 7" check failed in this
level; it expects another level's weapons and this change doesn't touch weapons.

## 2026-11-03 The player list shows in single player

**Your request:** make the player list work in single-player worlds.

**Why it didn't:** Minecraft's `Hud.extractTabList` (read from its compiled code, nothing saved)
shows the list only when you're not on your own world's server, or more than one player is listed,
or a scoreboard is shown in the list. Alone in single player, none of those holds.

**Changed:** new `HudTabListMixin.java` (in the client mixins): that check's `isLocalServer()`
answers false, so holding List Players always shows the list (just you). Logs "player list: shown
in single player too" once. Minecraft side only.

**Tested how:** the jar builds, and the call it patches is there in 26.3's code. **Not tested in
Minecraft** (the stand-in has no real Minecraft, so the mixin never runs there).

## 2026-11-03 Show Stats and List Players can share a key (protocol v40)

**Your request:** let Show Stats and Minecraft's List Players both work when they're on the same key.

**Before:** a key bound to a Mycelium action (Show Stats on Tab) was never sent to Minecraft, so
Minecraft's List Players on Tab did nothing.

**Changed:** `MyceliumKeys.java` also sends Minecraft's List Players key (Bindings id 12, count
13). `KeyOwnership.cs` keeps a Mycelium key that's also List Players as Mycelium's action but
sends it to Minecraft too. Protocol version 39 -> 40 (both sides: install the new jar).

**Tested how:** both build. **Not tested** at all otherwise: Minecraft was open, and the stand-in
doesn't send key binds. Not sure: in single-player, Minecraft may only show the player list when
there's more than one player (or a scoreboard), so Tab might still show nothing there.

## 2026-11-03 Blitz goes the way the camera looks

**Your request:** Blitz should go in the direction of the camera; leave the dodge roll as it is.

**Changed:** `DashWatch` now redirects Blitz (a dash without the "Roll" animation) along Mycelium's
camera's forward, flattened, keeping its speed. The spore faces that way while it lasts. The roll
still goes the way you move. `MinecraftDriver` passes the camera direction (`CameraFlatForward`)
and has a new test aid, `-mcBlitzTest`. `RollDirection` is now `DashDirection` (it covers both).

**Tested how:** stand-in, Daybreak Arboretum, `-mcBlitzTest`. Log: "dash: Blitz along the camera
(90 deg) instead of the spore's facing (90 deg…)", dash sent for 0.40 s, view widened and back.
The camera and body faced the same way there, so **Blitz going a different way than the body was
not tested.** **Not tested in Minecraft.** Known: Blitz's particles still start along the body.

## 2026-11-03 The view widens a little during a dodge roll or Blitz

**Your request:** make the FOV increase slightly whenever you dodge roll or use Blitz.

**Changed:** `DashWatch` now says when a dash is moving you (and whether it's the roll or Blitz).
`CameraSync` multiplies the lens by the new `dashFov` setting (default 1.1, so 10% wider) while it
does, easing out fast and back slowly. The setting is read from `MyceliumCraft.cfg`. Your existing
file doesn't have the line, so the default is used until you add `dashFov=...`.

**Tested how:** stand-in, Daybreak Arboretum, `-mcDashTest` (a dodge roll). Log: "dodge roll: view
widened x1.10 (lens 60.0 -> 66.0 degrees)", then 0.39 s after the roll ended "view back to x1.00
(camera now 60.1 degrees)". **Blitz not tested** (it goes through the same dash watch). **Not
tested in Minecraft.**

## 2026-11-03 The invisible wall in the Carcass: a pillar under a mushroom cap's thin edge

**Your report:** the same invisible thing was still there after the rescan change. You walked into
it and quit.

**What the logs showed:** both runs, you stopped at Minecraft x = 7.30 facing east. With your
body's half-width (0.2 blocks) that's a face at x = 7.5, a cell edge. Probing those cells: the one
at (7.75, 0.75) was sent as a solid piece from the ground (0.72) to 3.50 blocks. Above it is an
`OysterCap` (a big flat mushroom cap) whose underside there is only 0.009 units below its top.
The scanner only counted an underside more than 0.01 below (to skip a surface's own back face,
which is exactly 0.000), so the cap looked one-sided, and the 3.9-unit gap under it was filled
solid (gaps under one-sided surfaces smaller than 4.25 units are filled).

**Changed (`GroundScanner.cs`):** that limit is now 0.001 units. The cell is now just the cap,
(3.49, 3.50), with open space under it. Across the whole Carcass scan, 123 thin undersides are found
instead of about 110, so other cells had the same problem.

**The rescan change from earlier today** wasn't the fix. I'm keeping it: it's harmless (one scan,
about 35 ms, each time you get the controls back), and its log helps when the ground changes.

**Tested how:** stand-in Carcass, column probe. **Not tested in Minecraft.**

## 2026-11-03 Ground scanned again after the sprout (invisible wall where you appear)

**Your report:** an invisible wall in the Carcass, near where you appear, not near the level's own
invisible walls.

**What the logs showed:** the ground is scanned once on arrival, 0.1 s in, while the spore is still
sprouting. In the stand-in, the fixed level has nothing at body height within 3 blocks of where you
appeared (169 columns checked). So the wall was most likely something there only during the
arrival, kept as a block in Minecraft. **I did not find which object it was.** Your arrival spot
(Mycelium (20.1, 5.1)) isn't where the stand-in arrives, so I couldn't repeat it exactly.

**Changed (`MinecraftDriver.cs`):** the ground is scanned again each time Mycelium gives the
controls back (after the sprout, a cutscene, a menu). The log lists how many cells changed, the 8
nearest the spore, and on the first scan the solid things within 6 units of the spore. The
`-mcColumn` test aid now also probes with the rays 50 units higher.

**Tested how:** stand-in Carcass with `-mcHoldTest`. The first rescan changed 52 cells, including
a floating piece 1.06–1.44 blocks high at Minecraft (17.25, -7.25) that went away. A second rescan
changed 0 cells. **Not tested in Minecraft.**

**Dead end, recorded:** the first rescan attempt didn't compile (`List` needed its full name).
Also seen: the Carcass's 61-unit edge walls can go missing in Minecraft when the scan's rays
start inside them. Not fixed.

## 2026-11-02 No Minecraft toasts

**Your request:** prevent all of Minecraft's toast pop-ups.

**Changed:** new `mixin/ToastManagerMixin.java` (a mixin is a small patch our mod applies to
Minecraft's own code as it loads), listed in the mixins file. It cancels `ToastManager.addToast`,
which every toast goes through, and `showNowPlayingToast`, the music pop-up. Each kind of toast held
back is logged once. Advancements and recipes are still earned; only the pop-ups are gone.

**Tested how:** built. Both method shapes were checked against Minecraft 26.3's own class with
javap: `addToast(Toast)` and `showNowPlayingToast()`. **Not tested in Minecraft.** A wrong mixin
would stop Minecraft at start-up, so starting it is the first test.

## 2026-11-02 Thrown items don't clump together

**Your request:** thrown items on the Minecraft side shouldn't clump together.

**What the log showed:** weapons #7 and #8, thrown a second apart, ended 0.5 blocks apart.
Minecraft doesn't merge them (each has its own id), but items thrown from one spot land in one spot.

**Changed:** new `MycThrownSpread.java` (registered in `MyceliumCraftClient.java`). Every server
tick, two of your thrown weapons or resources closer than 0.8 blocks across get pushed apart
sideways, harder the closer they are. The motion is sent to the client (`needsSync`). The first
push of each pair is logged.

**Tested how:** only built. The stand-in Minecraft has no item physics, so this can't be tested
there. **Not tested.**

**Seen in passing, not changed:** most thrown weapons in your log were removed "still flying after
10 s" rather than "landed". My guess, not checked: an item on the ground keeps bouncing a tiny bit,
so it rarely counts as resting, and the real weapon becomes a pickup only after 10 s.

## 2026-11-02 Level-end pictures centred

**Your request:** centre the pictures.

**Changed:** `ResourceBag.LevelEndNotice` centres the row on where Mycelium's own picture sits,
instead of starting there and going away from the text.

**Tested how:** `-mcResourceTest` with the stand-in: Calcite Deposit x2 at (-30, 0) and Flesh x1 at
(30, 0), around Mycelium's picture at (0, 0).

**Not tested:** how it looks; whether a row of 3 or 4 reaches the text.

## 2026-11-02 Level-end pictures at full size, in a row

**Your report:** the pictures looked squished and small; keep them their normal size, next to each
other.

**Changed:** `ResourceBag.LevelEndNotice` keeps each picture at Mycelium's own size (and its own
aspect setting). The first sits where Mycelium's picture is; the rest go beside it, 1.1 picture
widths apart, on the side away from the text (worked out from where the text is). The count is a
little smaller relative to the picture.

**Tested how:** `-mcResourceTest` with the stand-in. The log said Mycelium's picture is 55 x 55 with
the text to its right, so the row goes left: Calcite Deposit x2 at (0, 0), Flesh x1 at (-60, 0),
both 55. The pictures were gone after the notification.

**Not tested:** how it looks. With 3 or 4 types the row reaches 2 or 3 pictures further left; not
checked that it fits on screen.

## 2026-11-02 The level-end notification shows every resource stored

**Your request:** between levels, with more than one resource, say "Resources stored at The
Carcass", and instead of the one picture show each type stored, with a small count at its bottom
right.

**Changed:** `ResourceBag.LevelEndNotice` (called in `MinecraftDriver.LateUpdate`) changes Mycelium's
own notification once it shows "... stored at the Carcass" with 2 or more carried. It sets the new
line, hides the picture, and puts one picture per type (Mycelium's own sprites) in a grid in the
same square, each with a count. They're removed when the notification ends. `-mcResourceTest` now
picks up 4 (a second Calcite) and shows the notification as LevelEnd does.

**Tested how:** `-mcResourceTest` with the stand-in. After the throw, Calcite, Flesh and Calcite
were carried. The log said the line became "Resources stored at The Carcass", with pictures Calcite
Deposit x2 and Flesh x1 side by side, and the pictures were gone 6 s later. The level end then
stored all three.

**Not tested:** how it looks (sizes, the count's place and readability), and a real level end.
With one resource the notification is Mycelium's own, unchanged.

**A slip, fixed:** a text replace in the test briefly changed every 4 s wait in
`MinecraftDriver.cs` to 6 s; they were put back before the final build (the diff shows only the
test's own wait changed).

## 2026-11-02 A thrown resource's pop-up moves with it

**Your report:** a thrown resource's pop-up stayed where it was, as thrown weapons' used to.

**Changed:** `ResourceBag.cs` moves the pop-up along with the flying resource, with the weapons' own
`WeaponBag.MovePopUp` (now shared). `-mcResourceTest` gives the thrown one a pop-up first, made as
Mycelium's `GetMaterial` makes it.

**Tested how:** `-mcResourceTest` with the stand-in (`LINKTEST_THROW_RESOURCE=1 LINKTEST_LENGTH=20`):
"its pop-up moved with it for 91 frames, now at (4.9, 2.8, 280.8)", the same place the Rotten Log
landed.

**Not tested:** in real Minecraft, with a pop-up you made by looking at a real resource.

## 2026-11-02 Several resources at once, throwable; the resource panel kept hidden (protocol v39)

**Your report and request:**
- The resource panel still showed in later levels.
- You want to hold several resources, throw them like the weapons, and have each take its own
  inventory slot.

**Changed:**
- **New `ResourceBag.cs` (Mycelium), replacing `ResourceWatch.cs`:** a bag of the resources you
  pick up while Minecraft drives. The one Mycelium puts back down on a pickup is hidden again and
  stays in the bag. At a level's end the rest of the bag is stored at the Carcass too, and
  Mycelium's save is written again. Dying loses them all. Thrown ones follow their Minecraft item
  and stay on the ground as pickups.
- **The panel:** every frame (after Mycelium's own slide), every resource panel is put at its
  out-of-sight spot while Minecraft is linked, instead of being slid away once per pickup.
- **`MycResources.java`:** one item per resource id, max stack 1. Thrown ones fly, can't be picked
  up, and are removed once landed (as the weapons). `MycWeapons.thrownFrame` sends them in the
  Thrown block with a new "resource" flag; `WeaponBag.cs` skips those.
- **Protocol v39:** MycState 0x74 is now the count, 0x78 the list; Thrown flag bit1.
- **Stand-in:** `LINKTEST_THROW_RESOURCE`, `LINKTEST_LENGTH`; `-mcResourceTest` does three pickups,
  waits for a throw, and does a level end without saving.
- PROTOCOL, DESIGN §5.0.4.1, README.

**Tested how:** `-mcResourceTest` with the stand-in in Daybreak Arboretum (stand-in objects, since
the level has none lying around):
- The stand-in saw 1, 2, then 3 resources, then 2 after throwing the first, then 0 at the level end.
- The two put back down stayed hidden; the thrown Rotten Log was left on the ground where it landed.
- The level end stored the held Flesh (Mycelium) and the Calcite (the bag): 0/8/2/1 → 0/8/3/2.
- The panel was pushed out of sight after each pickup.

**Not tested:** real Minecraft, real pickups, a real level end with the save written, a real throw.
I don't know why the old slide-away failed in later levels; the log now names the HUD whenever the
panel has to be pushed out.

## 2026-11-02 Resources as Minecraft items (protocol v38)

**Your request:** turn the resources into Minecraft items, like the weapons:
- Rotten Log as a poisonous potato;
- Fresh Exoskeleton as a nautilus shell;
- Calcite Deposit as tube coral;
- Flesh as rotten flesh.

Named correctly, no textures yet. They don't need to show on Mycelium's UI any more when picked up.

**Changed:**
- **New `ResourceWatch.cs` (Mycelium):** reads which resource you carry (Mycelium carries one at a
  time) and sends it in MycState 0x74. After a pickup, it slides Mycelium's resource panel away with
  Mycelium's own `HUDItem.LostItem`.
- **New `MycResources.java` (Minecraft):** keeps exactly one named, marked item for it, or none.
  The item can't be eaten or placed, and a dropped one comes back. It's taken away before
  Minecraft's death drop.
- **Wiring:** `MinecraftDriver.cs`, `MyceliumLink.cs` and both `Proto` files (v38),
  `MycLink.java`, `MyceliumCraftClient.java`. Test aid `-mcResourceTest`; the stand-in logs the
  resource.
- PROTOCOL, DESIGN §5.0.4.1, README.

**Tested how:** `-mcResourceTest` with the stand-in in Daybreak Arboretum. There were none lying
around, so the test does what a pickup does.
- The stand-in saw "carries resource 1", then 3, then 0 after "as dying does".
- "Mycelium's resource panel slid away" after each pickup; the panel ended at its "out" place.

**Not tested:** the items in real Minecraft (names, the description, can't eat, place or drop), and a
real pickup from the ground.

**Left as Mycelium's:** the resources stored at the Carcass (for the grow and upgrade menus) aren't
items.

## 2026-11-02 The combat roll animation redone (it looked like teleporting)

**Your report:** it looked bad; the character seemed to teleport around. You asked for something
like Mixamo's "Stand to Roll", for inspiration.

**Cause:** my mistake in units. The library's whole-body bone takes its position in **blocks**
(Better Combat's slam lowers it 0.17), and I wrote pixel-sized numbers. "+5" and "−6" threw the
body 5 blocks up and 6 down within a few ticks.

**Changed:** `dodge_roll.json`, rewritten (made by a script, in blocks), now 12 ticks:
- a step and a dive, arms reaching for the ground (ticks 1 to 3);
- a tight forward roll, low, chin tucked (ticks 3 to 9);
- a crouched landing leaning forward, then up (ticks 9 to 12).

`MycSkillAnimation` stops it after 12 ticks (was 8). That's 0.6 s, inside the roll's 0.77 s.
DESIGN §5.0.7.

**Tested how:** the jar builds and contains the new file (45 keyframes). **Not seen in game.**

## 2026-11-02 A combat roll animation for the dodge roll (protocol v37)

**Your request:** an animation for the dodge roll, like a classic combat roll.

**Changed:**
- **New `player_animations/dodge_roll.json`** (our own, from scratch): an 8-tick forward
  somersault around the hips, with arms hugged in, legs tucked, chin down, and a little lift while
  upside down.
- **`MycSkillAnimation.java`:** plays it when the roll starts and stops it after 8 ticks. Living
  Cyclone's spin takes precedence.
- **`MycDash.faceRoll`:** while the roll's dash moves you, your Minecraft body and head face the
  roll's direction, so a sideways or backwards roll is still a forward somersault. The camera is
  unchanged.
- **Mycelium (`MinecraftDriver.cs`):** MycState flag bit5 `ROLL` while the Animator's "Roll" is
  on. Protocol v37.
- **Stand-in:** logs the roll flag.
- PROTOCOL, DESIGN §5.0.7, README.

**Tested how:** `-mcDashTest` with the stand-in: "Mycelium's dodge roll on (dash 14.3, 0.0
blocks/s)" with the dash, "over" 0.77 s later. The jar contains the animation.

**Not tested:** everything visible.
- The animation is my best guess at the library's whole-body pivot and directions: forward is
  negative pitch, as Better Combat's slam leans forward. It could roll backwards, sink into the
  ground or float.
- The body facing the roll.
- First person.

## 2026-11-02 The dodge roll's sound plays as you press the key

**Your request:** play the roll sound as soon as the key is pressed.

**Cause:** Mycelium itself plays it at the roll's end (`PlayerController.Dodging`: "Stab" with
the landing dust, about 0.4 s in).

**Changed:**
- **New `RollSound.cs` (Mycelium):** in the frame the roll starts, plays the same sound through
  Mycelium's `SoundEffectManager`. Mycelium's late copy is stopped as it starts (a new sound on the
  spore playing one of "Stab"'s clips during that roll). Only while Minecraft drives.
- **`MinecraftDriver.cs`:** calls it in LateUpdate.
- DESIGN §5.0.7, README.

**Tested how:** `-mcDashTest` with the stand-in: "'Stab' played as the roll starts (1 sound)", the
same frame as the roll. Then "Mycelium's own roll sound stopped as it started, 0.39 s into the
roll".

**Not tested:** heard in play.

**Mistake on the way (again):** one test run linked to your open Minecraft and teleported your
player (yaw 180), because my check for Minecraft only printed a warning. The check now stops the
test.

## 2026-11-02 Held movement keys aim the dodge roll first (protocol v36)

**Your rule:** if you're holding movement keys, they decide the roll's direction.

**Why it mattered:** the roll followed your smoothed walking velocity. Press a new direction and
roll at once, and the velocity still points the old way.

**Changed:**
- **Minecraft (`MyceliumCraftClient.java`, `MycLink.moveDirection`):** sends the way the held
  movement keys walk you. That's `player.input.keyPresses` turned by your look yaw, as Minecraft's
  own movement does. McState 0x98/0x9C, protocol v36.
- **Mycelium (`MinecraftDriver.RollToward`):** held keys first, else the way you last moved. The
  roll's log line says which it used.
- **Stand-in:** `LINKTEST_KEYS=forward,left,...` holds keys.
- PROTOCOL, DESIGN §5.0.7, README.

**Tested how:** `-mcDashTest` with the stand-in strafing east (90°):
- holding right (looking 0°, so west): "dodge roll along the movement keys you hold (270 deg)";
- no keys: "along the way you last moved (90 deg)".
- The stand-in against FakeMycelium: "ground checks PASS". FakeMycelium's own summary wasn't
  waited for.

**Not tested:** in play, and whether Minecraft's `keyPresses` shows keys arriving through the input
bridge. It should: the bridge presses Minecraft's own key binds. Both sides need updating: the new
jar and the new Mycelium together.

## 2026-11-02 The dodge roll goes the way you move

**Your request:** the dodge roll should go in the direction of your last or current movement.

**Cause:** Mycelium rolls where the spore faces when the roll starts. While Minecraft drives, the
spore faces Minecraft's body, which strafing or walking backwards turns away from the walk.

**Changed:**
- **`MinecraftDriver.cs`:** keeps the direction you last walked in Minecraft (over 1 block/s, not
  during a dash), and turns the spore to face the roll while it lasts.
- **`DashWatch.cs`:** a dash with the Animator's "Roll" on (the dodge roll, not Blitz) keeps its
  speed but goes along that direction. It's logged at the roll's start, and the end line now gives
  the heading.
- DESIGN §5.0.7, README.

**Tested how:** `-mcDashTest` with the fake Minecraft.
- Strafing (looking 180°, walking toward 90°, body at 135°): "dodge roll along your movement (90
  deg) instead of the spore's facing (135 deg)".
- Standing still from the start: "along the spore's facing (90 deg): you haven't moved yet".

**Not tested:** in play. The fake Minecraft doesn't move for a dash, so where the roll took you,
and the spore turning to face it, weren't seen.

**Mistake on the way:** my first two test runs linked to your open Minecraft instead of the fake
one. They teleported your player to the test spore's spot, and your pause menu froze the test. I
stopped and asked you to close Minecraft. Before a stand-in test, check that no `javaw.exe`
(Minecraft) is running.

## 2026-11-02 Your swings smack the levels' mushrooms

**Your request:** weapon attacks should hit the mushrooms in the levels, which bounce with a sound
in Mycelium.

**Cause:** Mycelium's mushrooms react when its weapon's hitbox touches them during Mycelium's own
swing animation, which doesn't play while Minecraft attacks.

**Changed:**
- **New `ShroomSmack.cs` (Mycelium):** at each Minecraft swing's hit moment, the mushrooms whose
  attack hitbox is inside the swing are found. That's the weapon's reach × 1.25; Slash 180° ahead of your look; Stab and
  Smash a box ahead. Each one's own `OnTriggerEnter` is called with the held weapon's collider,
  so the cooldown, sound, bounce and nutrients are Mycelium's own.
- **`EnemyCombat.cs`:** a `Swung` event.
- **`WeaponBag.cs`:** `HeldSwing` (type, reach, attacks per second).
- **`MinecraftDriver.cs`:** wiring, and the test aid `-mcShroomTest`.
- DESIGN §5.0.3, README.

**Tested how:** the real Mycelium build in Daybreak Arboretum with the fake Minecraft.
- `-mcShroomTest`: facing a mushroom: 1 smacked, Attackable now False. Again at once: 0 (its
  cooldown). After the cooldown, facing away with a stab: 0; facing it: 1.
- The log lists one mushroom's colliders: "ATTACK HITBOX, 1.37 wide x 1.37 tall; 'DA_Glow
  Shrooms' CapsuleCollider glow sensor, 13.51 wide x 13.51 tall; 'pCone1' MeshCollider solid…".
  Only the attack hitbox is measured to (your note: mushrooms have a separate glow trigger).
- `LINKTEST_FUNGAL`'s two swings smack nothing from where the fake Minecraft stands.

**Failures on the way:**
- At first the reach was measured to all the mushroom's colliders, the glow sensor included, which
  is ten times wider. The fake Minecraft's swings "smacked" a mushroom 4.45 units away through it.
  Fixed after your note.
- A mushroom 0.67 units behind you (133°) was smacked too: the slash's half circle let a little
  behind through. It's now strictly the front half.

**Not tested:** in play (seeing and hearing the bounce). Only glow mushrooms were in the tested
level, not the plain kind.

## 2026-11-02 The Speed stat's boost comes back after dying

**Your report:** after dying and going back to the Carcass, the speed boost from the Speed stat
was gone.

**Cause:** in your logs, Mycelium kept sending ×1.63 after the death. Minecraft put ×1.63 on the
player in the same second (10:04:25) it respawned it. A respawn makes a new player, without the
modifier. Our mod only sets it when the factor changes, and it hadn't changed, so it was never put
back.

**Changed:** `MyceliumCraftClient.syncSpeed`: as well as on a change, once a second the
integrated server checks that the player's "myceliumcraft:speed" modifier is there at the right
amount, and puts it back if not. That's logged as "speed: the player had lost the x1.63 (a respawn
makes a new player); put back". DESIGN §5.0.2.2, README.

**Tested how:** the jar builds. **Not tested:** a death and respawn in real Minecraft. The stand-in
Minecraft doesn't respawn.

## 2026-11-02 Living Cyclone: the flat weapon flipped over

**Your request:** flip the weapon 180 degrees.

**Changed:** `living_cyclone.json`: the held item's roll goes from −1.5708 to +1.5708 (the other
flat face on top), and its sideways grip shift from x −1.1 to x +1.1, mirrored as Better Combat's
`one_handed_stab` / `two_handed_slash_horizontal_left` do for that roll. DESIGN §5.0.8.

**Tested how:** the jar builds. Not tested in game.

## 2026-11-02 The menu between levels hides the overlay and holds the spore

**Your report:** during the menu between levels, the overlay stayed and you could still move.

**Cause:** `LevelEnd` switches the spore's controls off (`DisableController`) for the menu, and our
mod took that as "Mycelium has the controls". But something under the menu switched them on
again: in your log at 127.22 s, a knockback's end (`StartKnockback` → `EnableController`). Attacks
ending do the same. From then on it looked like play: the overlay came back, and Minecraft drove
the spore.

**Changed:** `MinecraftDriver.cs`: `MyceliumHasControls` also says yes while
`EndOfLevelCanvasStuff`'s black or menu canvas is showing ("end-of-level menu"). New test aid
`-mcLevelEndTest`. DESIGN §5.2, README.

**Tested how:** `-mcLevelEndTest` in Daybreak Arboretum with the fake Minecraft (overlay on): the
spore entered the level end ('BackToHub'). "end-of-level menu: showing", the hold took the spore,
and the overlay was hidden. Then the controls were switched back on as a knockback's end does.
For the next 5 s it still read "Mycelium has the controls True (end-of-level menu) … hold True,
overlay False", through Mycelium's freeze (timeScale 0).

**Not tested:** in play with real Minecraft. Choosing CONTINUE or RETURN loads a scene, which ends
the hold as before. The checkpoint version ("This leads back to The Carcass") fades the menu away
and should give the controls back. Not tried.

## 2026-11-02 Living Cyclone: the weapon is held flat

**Your request:** turn the weapon 90 degrees during the Living Cyclone animation, so its flat
faces point up and down instead of side to side.

**Changed:** `living_cyclone.json`: the held item gets `roll` −1.5708 (a quarter turn about the
blade), and its grip shift changes from (0, −3.15, −0.3) to (−1.1, −2.0, −0.5) px so it stays in
the hand. Both copied from how Better Combat's flat swings (`one_handed_stab`,
`two_handed_slash_horizontal_right`) hold the item at the same tilt. DESIGN §5.0.8.

**Tested how:** the jar builds and contains the new file. **Not tested in game:** whether the grip
sits exactly in the hand. If the weapon floats off the hand, the x/y/z numbers need nudging. If
you'd rather the other face on top, the roll becomes +1.5708 and x +1.1.

## 2026-11-01 No other attacks or skills during Living Cyclone

**Your rule:** no other attacks or skills while Living Cyclone spins.

**What Mycelium does on its own:** it stops the dodge and interact (`canAct` off), but not the skill
keys. Living Cyclone switches the controller on (`canUseSkill` true), and the skill keys only
check that. Since the hotbar fix, Minecraft's attacks also went through.

**Changed:**
- **Mycelium (`MinecraftDriver.BlockSkillsWhileCycloning`):** `canUseSkill` is kept off while the
  held weapon is cycloning. The skill's own `EndSkill` switches it back on.
- **Minecraft (`MyceliumCraftClient.blockAttackWhileCycloning`):** while Mycelium's Living Cyclone
  flag is on, the Attack key is held up and its clicks dropped, at each tick's start and after each
  frame. `MinecraftMixin` also refuses `startAttack`. This covers Minecraft's attack and Better
  Combat's own. The log says when and how many clicks were dropped.
- **`-mcCycloneTest`** now ends as the skill does (cycloning off, then `EnableController`).
- DESIGN §5.0.8.

**Tested how:** the real Mycelium build with the fake Minecraft holding the Stick
(`-mcCycloneTest`). "other skills off while Living Cyclone spins", "1 s in, canUseSkill False",
then after the ending "canUseSkill now True". Both builds succeed.

**Still broken / not tested:**
- **The attack block isn't tested** (it needs real Minecraft and Better Combat).
- An attack key held through the spin needs pressing again afterwards.

## 2026-11-01 Living Cyclone no longer hides the hotbar

**Your report:** during Living Cyclone, the Minecraft hotbar disappears.

**Cause:** Living Cyclone switches the spore's `canAct` off while it spins. Our mod reads "canAct
off" as Mycelium taking the controls (like a menu). After 0.3 s it hid the overlay (hotbar, hand,
HUD), and it also switched the input bridge off, so your keys went quiet during the spin.

**Changed:** `MinecraftDriver.MyceliumHasControls`: "canAct off" doesn't count while the held weapon
is cycloning (`WeaponCollision.isCycloning`, as Living Cyclone sets it). `-mcCycloneTest` now also
switches `canAct` off, as the skill does. DESIGN §5.0.8.

**Tested how:** the real Mycelium build with the fake Minecraft holding the Stick and sending an
overlay picture (`-mcCycloneTest`: cycloning with canAct off for 2 s). Throughout: the bridge
stayed "ON", no "Mycelium has the controls" hold, and the overlay stayed shown. It was only
hidden when the fake Minecraft stopped at the end.

**Still broken / not tested:**
- **Not seen with the real skill.** Attacks during the spin now reach Minecraft, which Mycelium
  itself blocks while cycloning.

## 2026-11-01 The stat panel slides away after picking up a stat weapon

**Your report:** picking up a weapon that changes your stats, the stat panel never slides away.

**Cause, from Mycelium's code:**
- Near such a weapon, its pop-up slides the stat panel in (`HUDStats.ShowStats`).
- The panel slides away when a weapon's pop-up closes, except when it closes because you picked
  the weapon up (`DestroyTooltip(…, isFromInteracting: true)`).
- In Mycelium the weapon you let go of then lies there. It becomes the next pop-up, and walking
  away from it hides the panel. Our mod puts that weapon in the bag instead, so nothing hid it.

**Changed:**
- **`WeaponBag.cs` (`HideStatsAfterPickup`):** 1 s after a pickup where the old or the new weapon
  has a stat-changing attribute, `HUDStats.HideStats` (Mycelium's own). This also covers
  picking up with an empty hand. If another weapon's pop-up is open then, it's left to that one.
  The log says which.
- **Test aid:** `-mcPickupTest <Type/Prefab>` picks up a chosen weapon (default
  Smash/CactusMace) and logs where the stat panel is.
- DESIGN §5.0.4.

**Tested how:** the real Mycelium build with the fake Minecraft holding the Stick, picking up
Bert's Club, Razorvine and Perfect Partisan (each has a stat-changing attribute). Each time:
"stat panel slid away after the pickup", 1 s after it.

**Still broken / not tested:**
- **The stuck panel didn't happen in the test.** Mycelium's own pop-up brought the panel in and
  out again within 0.6 s, before the pickup, for the weapon lying in front of a spore that isn't
  moving. So the test shows the hide firing, not the panel coming unstuck.
- Holding Show Stats right then: the panel slides away anyway.

## 2026-11-01 Skill bar icons filled again after the skills are loaded (slot 2's icon)

**Your report:** loading into the game the first time, the icon of the skill in slot 2 doesn't
show.

**What the log shows (new `SkillIconWatch.cs`, in The Carcass and Daybreak Arboretum):**
- Mycelium fills the skill bar from the SkillLoadout's children by position, 0.2 s after its HUD
  starts. The bar was still [NoSkill, NoSkill, NoSkill] then.
- The saved skills arrive one by one 0.6 to 1.0 s into the scene. Each new one is added before the
  NoSkill it replaces is removed, e.g. [FungalMight, Blitz, LivingCyclone, NoSkill] for a frame,
  with slot 2's skill 4th.
- A fill at such a moment gives slot 2 "no skill". That's the likely cause.

**Changed:**
- **New `SkillIconWatch.cs`:** for 8 s after each scene load it watches the loadout's names. On
  each change it asks Mycelium to fill the bar again (`HUDSkills.UpdateHUDIcons`, its own method,
  which waits 0.2 s). The last fill comes after the last change. It logs the loadout and the bar
  at 0.1, 0.3, 0.6, 1, 2, 4 and 8 s.
- **Test aid `-mcNoIconRefresh`:** log only.
- **`MinecraftDriver.cs`:** starts it on each scene load.
- DESIGN §5.3.

**Tested how:** the real Mycelium build, `-mcScene` straight into The Carcass and into Daybreak
Arboretum:
- With the watch: the bar ended "[FungalMight, Blitz, LivingCyclone]" from 1 s on.
- With `-mcNoIconRefresh`: Mycelium got it right on its own too, in this run.

**Still broken / not tested:**
- **Not reproduced.** The missing icon didn't happen in my direct loads. Your first load goes
  through the main menu and the save, whose timing differs. If it still happens, the "skill
  icons:" lines in `work/build/MyceliumCraft.log` will show what the bar read and when.

## 2026-11-01 The skill menu assigns slots with your Skill 1 / Skill 2 keys

**Your report:** the buttons used in the shop to equip skills don't follow the assigned keys.

**Cause:** the skill menu (`SkillMenuManagerUpdated`) assigns a skill to slot 1 or 2 with its own
keyboard actions, "Assign Y KB" (Q) and "Assign B KB" (E), in Mycelium's UISub map. Our overrides
only covered the Player map.

**Changed:**
- **`KeyOwnership.cs`:** those two actions get the Skill 1 and Skill 2 keys too, on every copy of
  Mycelium's input asset. They're taken off with the rest when Minecraft stops driving.
  - An unbound skill key leaves Mycelium's Q or E.
  - Gamepad buttons stay.
  - The asset log line now shows them.
- DESIGN §5.3, README.

**Tested how:** the real Mycelium build with the fake Minecraft's binds (Skill 1 F, Skill 2 G). On
all 7 input assets: "skill menu: Assign Y KB=<Keyboard>/f, Assign B KB=<Keyboard>/g". The scans
before Minecraft's binds arrived still showed Q and E, as expected.

**Still broken / not tested:**
- **Not tried in the menu itself.**

## 2026-11-01 Key hints: extra mouse buttons as MB4, MB5…

**Your request:** anything like "Mouse Button <x>" should be MB<x>.

**Changed:** `KeyHints.cs`: mouse buttons other than left, middle and right are named MB4, MB5
and so on (they were "Mouse 4", "Mouse 5"). LMB, MMB, RMB stay. DESIGN §5.3.

**Tested how:** the real Mycelium build with the fake Minecraft binding Skill 1 to button 4 and
Skill 2 to button 5: "statSkill1Hint=[MB4], statSkill2Hint=[MB5]".

## 2026-11-01 Mycelium's key hints show your Minecraft key binds (protocol v35)

**Your request:** the UI's controls should match what they're bound to: a loot cache said [RMB]
with interact on R; the skills said Q and E.

**Cause:** every hint is built from the current controller's `ColoredHint` text in Mycelium's
`InputManager`, fixed in its settings.

**Changed:**
- **New `KeyHints.cs` (Mycelium):** while Minecraft drives, the Keyboard controller's hints
  (interact, show stats, subspecies skill, salvage, attack, dodge, skill 1, skill 2, navigate and
  reset camera) get the names of the keys bound in Minecraft's Controls menu.
  - The skill bar is redrawn, and Mycelium's texts come back when Minecraft stops driving.
  - Re-applied on a binding change or a new `InputManager`.
  - The log shows the labels, and the keyboard's loot cache text.
- **`MyceliumKeys.java`:** also sends Minecraft's own Attack key (Bindings id 11, protocol v35).
  The attack hint shows it while Mycelium's Attack is unbound.
- **`MinecraftDriver.cs`:** calls it.
- PROTOCOL, DESIGN §5.3, README.

**Tested how:** the real Mycelium build with the fake Minecraft sending binds (`LINKTEST_BINDS`),
in Daybreak Arboretum: "interactHint=[R], showStatsHint=[Tab], subspeciesSkillHint=[V],
salvageHint=[MMB], attackHint=[LMB], dodgeHint=[Shift], statSkill1Hint=[F], statSkill2Hint=[G],
goalCameraHint=[Unbound], orientationCameraHint=[Unbound]". The keyboard's loot cache would say
"Press [R] to Open".

**Still broken / not tested:**
- **Not seen in play.** In the test Mycelium's current controller was still the Xbox one: it
  starts that way and switches to the keyboard when a key is pressed, then redraws the skill bar
  from these hints.
- Gamepad hints aren't changed.
- Hints only change while Minecraft drives the spore.

## 2026-11-01 Living Cyclone animation: the weapon stays in the hand

**Your report:** the weapon floated a little above the hand.

**Cause:** tilting the held item (`rightItem` pitch −1.4) turns it about a point that isn't the
grip, so it moved off the hand.

**How the fix was found:** Better Combat's own animations shift the held item when they tilt it.
Its `dual_handed_stab` tilts it by the same angle (−1.32 to −1.40, no other turn) and shifts it
y −3.15, z −0.3 px. Fitting a pivot to that puts it about 2.5 px from the grip, which matches the
drift.

**Changed (`living_cyclone.json`):** the held item keeps its tilt and gets that shift (x 0, y
−3.15, z −0.3). Nothing else changed.

**Tested how:** the library's loader still reads it (8 ticks, loops from tick 3, with the held
item). The jar is rebuilt. **Not seen in Minecraft.**

## 2026-11-01 Living Cyclone animation: a little slower spin, arms and weapon held out

**Your request:** keep the wind-up; slow the main spin down a little; hold the arms and the weapon
out and away from the body.

**Changed (`living_cyclone.json`):**
- **Spin:** a full turn every 5 ticks (0.25 s), was 4. One keyframe a tick, a fifth of a turn
  each, so the speed is steady.
- **Arms:** straight out at shoulder height, forward and angled to the right. They were pulled in
  close in front. The left arm reaches across toward the right one.
- **Weapon (`rightItem`):** tilted to point along the arms, out from the body.
- **Wind-up:** still 3 ticks, and the loop still restarts at the grip (`returnTick` 4).
- DESIGN §5.0.8.

**Tested how:** the library's own loader read it as 8 ticks long, playing again from tick 3.0,
with body, arms, legs and the held item. The jar is rebuilt. **Not seen in Minecraft.**

**Still broken / not tested:**
- The angles are my guess at "held out"; the weapon tilt especially may need a turn the other
  way.

## 2026-11-01 Our own Living Cyclone animation: quicker wind-up, faster spins

**Your request:** a custom animation like two_handed_spin, with a faster wind-up and faster spins,
in the mod's built-in resource pack.

**Changed:**
- **New `assets/myceliumcraft/player_animations/living_cyclone.json`** (Emotecraft format, which
  Player Animation Library reads):
  - **Written from scratch.** Better Combat's jar says "All Rights Reserved", so its keyframes
    aren't copied.
  - **Wind-up:** 3 ticks into a two-handed grip, arms out in front and angled to one side, a
    slightly wide stance, the torso turned back a little. Better Combat's takes 10 ticks.
  - **Spins:** a full turn of the torso every 4 ticks, at a steady speed. Better Combat's takes
    about 6.
  - **Loop:** from the grip on (`returnTick` 4, because the library restarts one tick early).
- **`MycSkillAnimation.java`:** plays ours with its own loop (`thenPlay`). Better Combat's
  two_handed_spin, looped, is the fallback if ours isn't found.
- DESIGN §5.0.8, README.

**Tested how:**
- **Loader check:** the library's own loader (`UniversalAnimLoader`, run outside Minecraft on
  the file) read it as "length 7.0 ticks, plays again true, restarts from tick 3.0", with body,
  arms and legs. Better Combat's spin read as 30 ticks, play once, as a comparison.
- **The first try** restarted from tick 2 (a hitch each loop): fixed with `returnTick` 4.
- The jar holds the file.

**Still broken / not tested:**
- **Not seen in Minecraft.** The pose is my guess at a two-handed grip; it may need adjusting by
  eye.
- 4 ticks a turn is much faster than Mycelium's own spin (about 1 s a turn at Speed 1).

## 2026-11-01 Living Cyclone loops Better Combat's two_handed_spin

**Your request:** loop the `two_handed_spin` animation during Living Cyclone.

**Changed:**
- **New `MycSkillAnimation.java`:** plays it through Player Animation Library, the library Better
  Combat animates with, using only its public API by reflection. Neither mod is changed (rule 7),
  and nothing happens if they're missing.
  - At start it registers our own animation layer above Better Combat's. Its controller plays
    nothing by itself.
  - While Mycelium's Living Cyclone flag (bit 4) is on, `bettercombat:two_handed_spin` is looped
    there (`RawAnimation.begin().thenLoop`); when it goes off, it's stopped.
  - The log says when it starts and how long it ran.
- **`MyceliumCraftClient.java`:** calls it at client start and every client tick.
- DESIGN §5.0.8, README.

**Tested how:** built only. Every library function, constructor and type it uses was checked
against the library's own class files with `javap`. **Not tested**: the fake Minecraft has no player
animations.

**Still broken / not tested:**
- **Not tested in Minecraft.** Not sure the animation shows in first person, but Living Cyclone
  goes to third person from there anyway.

## 2026-11-01 Living Cyclone: third person while it spins (protocol v34)

**Your request:** give Living Cyclone the same camera change as Trophic Cascade.

**How Mycelium marks it:** Living Cyclone sets the held weapon's `WeaponCollision.isCycloning`
from the start of the skill until its third spin ends.

**Changed:**
- **`MinecraftDriver.cs`:** while that's on, MycState flag bit 4 (`CYCLONE`) is set. The log says
  when it starts and how long it lasted.
- **Minecraft (`updateViewOverride`):** the same as the vanish. From first person, third person
  behind until it's over, then your view back. A third-person view stays as it is.
- **Test aid `-mcCycloneTest`:** `isCycloning` on for 2 s.
- **The fake Minecraft** prints the flag.
- PROTOCOL, DESIGN §5.0.8, README.

**Tested how:** the real Mycelium build with the fake Minecraft holding the Stick
(`LINKTEST_HOLD=1`, `-mcCycloneTest`). Mycelium: "Living Cyclone started", then "over after 2.00
s". The fake Minecraft: "spinning (third person behind)" at 4.0 s, "over" at 6.0 s.

**Still broken / not tested:**
- **Not tested in Minecraft**, and Living Cyclone itself wasn't used (this spore doesn't have it).
- It needs a weapon in hand: Mycelium only sets `isCycloning` on one.
- Living Cyclone spins the spore, but you see your Minecraft body, which faces where Minecraft's
  body faces. In third person your body probably doesn't spin with it. Not changed.

## 2026-10-26 Trophic Cascade: your body vanishes, third person meanwhile (protocol v33)

**Your request:** when using Trophic Cascade from first person, go to third person behind, make the
Minecraft player model disappear and reappear as the spore does, then go back to first person.

**How Mycelium does it:** `TrophicCascade.Vanish` switches off every renderer on the spore,
waits while it jumps between enemies (0.4 to 1.4 s), then switches them back on.

**Changed:**
- **New `VanishWatch.cs`:** the spore's body renderers all off means it has vanished (only Trophic
  Cascade does that). Logs how long it lasted.
- **`McPlayerModel.cs`:** `SetVanished` hides your Minecraft body and its shadow meanwhile.
- **`MinecraftDriver.cs`:** sets MycState flag bit 3 (`VANISHED`, protocol v33).
- **Minecraft (`MyceliumCraftClient.updateViewOverride`):** while it's set, first person becomes
  third person behind, and your view comes back after. A third-person view stays as it is.
- **Test aid `-mcVanishTest`:** every spore renderer off for 1 s.
- **The fake Minecraft** prints the flag.
- PROTOCOL, DESIGN §5.0.8, README.

**Tested how:** the real Mycelium build with the fake Minecraft in Daybreak Arboretum
(`-mcVanishTest`). Mycelium: "the spore vanished", then "back after 1.00 s". The fake Minecraft:
"vanished" at 4.0 s, "is back" at 5.0 s.

**Still broken / not tested:**
- **Not tested in Minecraft:** the view change, and the body actually disappearing (only the code
  path; the test's body was a fake one).
- Trophic Cascade itself wasn't used (this spore doesn't have it).
- While vanished, Mycelium freezes the spore (its rigidbody kinematic), but Minecraft's player can
  still walk. Not changed; not sure how that plays.

## 2026-10-25 Blitz (and the dodge roll) move you (protocol v32)

**Your report:** Blitz isn't sending you forwards.

**Cause:** Blitz sets the spore's velocity (40 units/s ahead, every frame for 0.3 s) from a
coroutine. The dodge roll does the same at 20. But Minecraft is in charge of where you are, and the
driver sets the spore's velocity every physics step to follow Minecraft, cancelling the dash.

**Changed:**
- **New `DashWatch.cs` (Mycelium):** while `PlayerController.activeDodge` is on, a velocity change
  between our Update and LateUpdate is Mycelium's dash (coroutines run in between). It's sent to
  Minecraft in blocks/s, until 0.1 s after the last write. The log says how long and how far.
- **New `MycDash.java` (Minecraft):** at the start of each client tick, the player's horizontal
  movement is set to the dash. The spore follows Minecraft as usual.
- MycState 0x6C/0x70 (protocol v32).
- **Test aid `-mcDashTest`:** Mycelium's own dodge roll 4 s into driving.
- **The fake Minecraft** prints any dash it receives.
- PROTOCOL, DESIGN §5.0.7, README.

**Tested how:** the real Mycelium build with the fake Minecraft in Daybreak Arboretum
(`-mcDashTest`). The fake Minecraft got "dash: (14.3, 0.0) blocks/s" (20 units/s ÷ 1.4, facing
east), over after 12 of its frames. Mycelium: "dash (20.0 units/s) sent to Minecraft for 0.36 s".

**Still broken / not tested:**
- **Not tested in Minecraft:** the player actually moving.
- Blitz itself wasn't fired (this spore has no Blitz). It works the same way as the roll.
- The dash goes the way the body faces, as in Mycelium (not where you look).

## 2026-10-25 Projectile skills fire where you look

**Your report:** abilities that shoot something (Spineshot) should go where the camera/head looks;
they sometimes went off to the side.

**Cause:** the spore faces where Minecraft's body faces. Minecraft's body lags behind the head (it
turns only past a certain angle, and toward the walk when strafing). Spineshot, Undergrowth and
Unstable Puffball give their projectile the skill object's rotation, which follows the spore.

**Changed:**
- **New `SkillAim.cs`:** while Minecraft drives, the spore's `SkillLoadout` (the skills' parent)
  faces Minecraft's look yaw, every frame (Update and LateUpdate). It's turned back when Minecraft
  doesn't drive. The body keeps facing Minecraft's body.
- **`MinecraftDriver.cs`:** calls it.
- DESIGN §5.0.7, README.

**Tested how:** the real Mycelium build with the fake Minecraft strafing (`LINKTEST_STRAFE=1`,
body 45° from the look): "skills face 180 deg (your look), the body 135 deg: 45 deg apart". It was
turned back when the fake Minecraft stopped.

**Still broken / not tested:**
- **No skill was fired in the test** (this spore has none to fire); only the aim was checked.
  Not tested in Minecraft.
- Only the yaw: looking up or down doesn't tilt them.
- Mycotoxins' cloud now lands behind where you look rather than behind the body. Blitz still
  dashes the body's way.

## 2026-10-25 Minecraft's window takes the size of Mycelium's (protocol v31)

**Your request:** make the Minecraft window copy the size of the Mycelium window.

**Why it matters:** the overlay is Minecraft's picture stretched over Mycelium's window. Different
sizes or shapes stretched the hand, HUD and screens.

**Changed:**
- **Mycelium (`MinecraftDriver.cs`, `Link/MyceliumLink.cs`, `Link/Proto.cs`):** MycState carries
  Mycelium's window size in pixels (0x64, 0x68).
- **Minecraft, new `MycWindowSize.java`:** once Mycelium's size has stayed the same for 0.3 s,
  Minecraft's window is resized so its picture has that many pixels (corrected for display
  scaling).
  - Not while Minecraft is full screen.
  - It logs what it asked for and what it got. A size Windows won't give isn't asked for again
    until Mycelium's changes.
- **`LinkTestMain`** prints the size it reads. PROTOCOL, DESIGN §5.4, README.

**Tested how:** the real Mycelium build in a 1280x720 window (Unity's `-screen-width 1280
-screen-height 720 -screen-fullscreen 0`) with the fake Minecraft. It read "window 1280x720" every
second. Both builds succeed.

**Still broken / not tested:**
- **Not tested in Minecraft:** the resize itself (`Window.setWindowed`), the display-scaling
  correction, and how it behaves with a maximized Minecraft window.
- Minecraft full screen is left as it is.

## 2026-10-25 The respawn animation isn't sped up by the Speed stat

**Your report:** the Speed stat changes the speed of the respawn animation; it shouldn't.

**Cause:** the respawn (emerge) follows Mycelium's "Sprout" animation on the spore. Mycelium runs
the spore's whole animator at its Speed level (`CharacterStats`: `animator.speed =
animatorSpeed`), so a high Speed sped up the Sprout, the emerge with it, and the moment you get
control back.

**Changed:**
- **`SproutWatch.cs`:** while the Sprout plays, the spore's animator is held at ×1. When it ends,
  it goes back to the Speed value (unless something else changed it meanwhile).
- **Test aid `-mcSproutSpeed n`:** the animator at ×n before the Sprout.
- DESIGN §5.0.2 (emerge), README.

**Tested how:** the real Mycelium build with the fake Minecraft in The Carcass, twice.
- Without the aid: this save's spore was already at ×1.99 from its Speed level.
- With `-mcSproutSpeed 2`: ×2.00.
- Both runs: "animator held at x1", the sprout done "2.99 s after arriving" (as at normal speed
  before), and the animator back to ×1.99 / ×2.00 afterwards.

**Still broken / not tested:**
- **Not seen in Minecraft.**

## 2026-10-25 Your last three weapon textures

**Changed:** Carpal Sais, Femur Club and The Smacker copied from `dist/assets` into
`fabric/src/main/weapon-textures/`. All 19 weapons are hand-made now.

**Tested how:** rebuilt. All 19 textures in the jar are identical to your files in `dist/assets`.
**Not seen in Minecraft.**

## 2026-10-24 Mycelium's window can be resized

**Your request:** make the Mycelium window resizable.

**Changed:**
- **`mycelium/Editor/BuildMyceliumCraft.cs`:** sets `PlayerSettings.resizableWindow = true` on the
  build's private copy of Mycelium. Your Mycelium project is untouched; it has the setting off.
- **`BackgroundWindow.cs`:** one log line 1 s after start. It reads the real window's style from
  Windows (resizable border, maximize button) and gives its size and Unity's window mode.
- DESIGN §2 (the build), README.

**Tested how:** built ("resizable window: True" in the Unity build log). Then run windowed
(`-screen-fullscreen 0`, Unity's own option): "window: resizable border yes, maximize button yes;
960x540, Windowed".

**Still broken / not tested:**
- Not dragged by hand.
- Full screen is still the default; Alt+Enter gives the window.
- The overlay picture is Minecraft's window size stretched to Mycelium's. A Mycelium window of a
  different shape from Minecraft's stretches the hand and HUD (the log already warns).

## 2026-10-24 Six more of your weapon textures

**Your request:** rebuild the mod with the new art.

**Changed:** Bert's Club, Coal Axe, Eternal Torch, Geode Hammer, Mandible Sickle and Razorvine
copied from `dist/assets` into `fabric/src/main/weapon-textures/`. 16 of the 19 weapons are now
hand-made; Femur Club, The Smacker and Carpal Sais keep their dummies.

**Tested how:** rebuilt. All 19 textures in the jar are identical to your files in `dist/assets`.
**Not seen in Minecraft.**

## 2026-10-22 Fungal Might ends only after a swing that hits

**Your rule:** Fungal Might should end at the end of a swing that hits enemies; a swing that hits
nothing keeps it on.

**Before:** nothing ended it in our mod. Mycelium's own attack clears it in every swing
(`PlayerAttack.Attack` → `ClearAllFungalMights`), but Minecraft's swings don't run that code.

**Changed:**
- **`EnemyCombat.cs`:** each swing remembers whether it hit. When a swing that hit finishes, if
  Fungal Might is on, Mycelium's own `ClearAllFungalMights` runs (the skill's border goes off and
  its cooldown starts, as in Mycelium), then `FinishedAttack`.
- **Test aids:** `-mcFungalTest` (Mycelium) and `LINKTEST_FUNGAL=1` (fake Minecraft: a swing at 5
  s that misses, a swing at 7 s that hits).
- DESIGN §5.0.3, README.

**Tested how:** the real Mycelium build with the fake Minecraft in Daybreak Arboretum:
- "Fungal Might bonus x2.00" when switched on;
- the missed swing (logged at 9.6 s) left it on;
- the swing that hit a Stickbug (11.73 s) ended it 0.25 s later: "Fungal Might (x2.00) ended
  with the swing that hit", then "bonus x1.00".

**Still broken / not tested:**
- **Not tested in Minecraft** with the real skill and Better Combat.

## 2026-10-22 More of your weapon textures

**Your request:** add your new texture changes.

**Changed:** Avocado Flamberge, Cactus Mace, Opal Rapier, Rose Mace and Stickbug Spear copied into
`fabric/src/main/weapon-textures/`. Your new Stick was already there. 10 of the 19 weapons are now
hand-made.

**Tested how:** built. All 19 textures in the jar are identical to your files in `dist/assets`.
**Not seen in Minecraft.**

## 2026-10-22 Your weapon textures in the mod

**Your request:** apply your texture changes from `dist/assets` to the mod.

**What changed in `dist/assets`:** five textures: Stick, Obsidian Scimitar, Bamboo Partisan, Perfect
Partisan and THE LOG. The other 14 textures, the models, the bread / beetroot / carrot item
definitions and the language file are byte-for-byte what the mod already had.

**Changed:**
- **New folder `fabric/src/main/weapon-textures/`:** your five textures.
- **`build.gradle` (`weaponResources`):** a weapon with a texture in that folder uses it; the
  others keep their drawn dummy. The build logs which are hand-made.
- **`.gitignore`:** the whitelist lets in `/fabric/src/main/weapon-textures/*.png` (your own art,
  your choice), so git tracks them. Only that folder: other .png files stay ignored.
- DESIGN §5.0.6.

**Tested how:** built. The jar's textures for those five (and Razorvine, unchanged) are identical
to your files in `dist/assets`. **Not seen in Minecraft.**

## 2026-10-22 Built-in resource pack: a dummy texture for each weapon (protocol v30)

**Your request:** embed a resource pack in the mod, and give each weapon its own dummy 16x16
texture through the `custom_model_data` component, so two Slash weapons look different although
both are bread.

**Changed:**
- **New `fabric/src/main/weapon-models.json`:** the 19 weapon prefabs, by type.
- **`build.gradle`, new task `weaponResources`** (runs before `processResources`). It draws a
  16x16 texture for each weapon (Slash a blade, Smash a club, Stab a spear, each its own colour)
  and writes its model. It also writes bread / beetroot / carrot item definitions that choose the
  model by `custom_model_data`, falling back to the normal look. Drawn at build time because .png
  isn't a source type in the repository.
- **Key:** the weapon's prefab name, because two prefabs share the name "Rose Mace".
  - Mycelium (`WeaponBag`, `MyceliumLink`, `Proto.cs`) sends it in the Weapons table. Entries grew
    to 0x120 bytes and the key is at 0x100 (protocol v30).
  - `MycWeapons` puts it on the item as `custom_model_data` (`modelKey`: "Bert's Club" →
    `bert_s_club`).
- **The fake Mycelium** sends keys, and `LinkTestMain` checks them.
- PROTOCOL, DESIGN §5.0.6, README.

**Tested how:**
- `gradlew linkTest` with the fake Mycelium: all checks ok, including "weapon 2 look (key 'Bert's
  Club' -> bert_s_club)" and "weapon 1 look (stick)".
- The generated textures are 16x16 and all 19 differ.
- The jar holds the 19 textures, 19 models and 3 item definitions.
- Mycelium builds.

**Still broken / not tested:**
- **Not seen in Minecraft.** I haven't looked at the textures: they're simple shapes in a colour.
- A weapon not in the list (a new prefab) keeps the plain bread / beetroot / carrot look.

## 2026-10-22 Holding attack keeps attacking through a hit (protocol v29)

**Your report:** Better Combat keeps attacking while left click is held, but getting hit stops it.

**Cause:** a hit's knockback takes the spore's controls in Mycelium until its "Hurt" animation is
half done. The input bridge then switched off, and switching off lets go of every key and mouse
button, including your held left click.

**Changed:**
- **Mycelium (`MinecraftDriver.cs`, `Link/Proto.cs`, `InputBridge.cs`):** a new bridge reason, 5
  "knockback", instead of the general "Mycelium has the controls".
- **Minecraft (`MycInputBridge.java`):** on that reason the bridge stays on, but it lets go of the
  keys and ignores mouse movement, scroll and typing (Mycelium is moving the spore). The mouse
  buttons keep following what you hold. When it ends, the log says how many frames it lasted and
  which buttons were held.
- **Test aid `-mcKnockTest`** (with `-mcHurtTest`): each test hit also calls Mycelium's
  `PlayerController.Knockback`.
- PROTOCOL (reason 5, v29), DESIGN §5.3.

**Tested how:** the real Mycelium build with the fake Minecraft, `-mcHurtTest -mcKnockTest` in
Daybreak Arboretum. On each of the 4 knocked-back hits the bridge reason went to "a hit's knockback
has the spore (mouse buttons kept)", then back to ON. Before `-mcKnockTest`, the test hits didn't
knock back, so they never showed this.

**Still broken / not tested:**
- **Not tested in Minecraft:** that Better Combat keeps attacking. The fake Minecraft has no
  Better Combat.

## 2026-10-22 First-person Better Combat arms: no washed-out colours (no fog)

**Your report:** the colours in Better Combat's first-person animations look a bit washed out.

**Cause:** Minecraft draws its normal hand with no fog. My arms step (`MycFirstPersonArms`)
passed in the world's fog, which blends colours toward the fog colour. The swing particles too.

**Changed:**
- **`MycFirstPersonArms.java`:** the arms and swing particles use Minecraft's "no fog" setting.
- **New `GameRendererAccessor.java`:** reads Minecraft's fog renderer to get that setting.
- DESIGN §5.4.

**Tested how:** built only. **Not tested.** Not sure the fog was the whole cause. The arms also
use the world's lighting, as Minecraft uses for players seen in the world.

## 2026-10-21 Crabs flash red (and green) all over

**Your report:** crab enemies don't flash red when taking damage (or green from poison).

**Cause, from the log:** the crab's body is one renderer with 4 sub-meshes (parts with their own
material). The flash adds its tint as an extra material, and Unity draws an extra material over
the last sub-mesh only. So only one small part of the crab was tinted. The new log line
showed it: "'Crab' SkinnedMeshRenderer 4 sub-meshes, 4 materials". "SporeModel" enemies have 2
sub-meshes and had the same problem.

**Changed:**
- **`HurtFlash.cs`:** a renderer with several sub-meshes gets an overlay copy, made the first
  time it flashes. It's a child renderer with the same mesh and bones, the tint on every
  sub-mesh, and no shadow, switched on only while flashing. One-part models are unchanged.
- **New log:** the first time each kind of enemy flashes, its renderers, sub-meshes and
  materials are listed.
- **Test aid `-mcFlashAll`:** flashes every enemy once and counts the overlay copies drawn.
- DESIGN §5.0.3 (hurt flash), README.

**Tested how:** the real Mycelium build with the fake Minecraft.
- **Daybreak Arboretum** (`-mcFlashAll`): beetles and stickbugs are all one sub-mesh per
  renderer, so they're unchanged. No crab in that map.
- **Delta Crag:** 78 enemies flashed. Overlay copies were made for 5 crabs (4 sub-meshes) and 10
  SporeModels (2). "15 of 15 overlay copies drawn, 0.1 s later" and "0 of 15 … 0.6 s later".

**Still broken / not tested:**
- **Not seen:** what the crab looks like flashing (log only).

## 2026-10-21 Attack ranges 25% bigger

**Your request:** make all the attack ranges about 25% bigger.

**Changed:** `MycWeapons.java`: the range given to Better Combat is the measured reach × 1.25
(`REACH_SCALE`). A weapon never held yet gets 3 × 1.25 = 3.75 blocks. The item description and the
Minecraft log show the range used. Minecraft side only.

**Tested how:** built into `dist/` (after you closed Minecraft, which had the old jar open).
**Not tested.**

## 2026-10-21 Better Combat animations, timing and reach for weapons (protocol v28)

**Your request:** give the carrots, beetroots and bread Better Combat animations through an
embedded datapack and `bettercombat:preset_id`: Slash `one_handed_slash_horizontal_right` and
`_left`, Stab `one_handed_stab`, Smash `two_handed_slam`. Timing from the weapon's and player's
stats, hitboxes like each weapon's.

**Changed:**
- **Presets:** `data/myceliumcraft/weapon_attributes/slash.json`, `stab.json` and `smash.json` in
  the mod. Slash is a 180° flat arc (measured Mycelium swings span about 180°); Stab and Smash are
  a box straight ahead. Upswing 0.5.
- **`MycWeapons.java`:**
  - Every weapon item gets `bettercombat:preset_id` for its type.
  - It also gets an entity interaction range modifier: the weapon's reach. Better Combat uses that
    as the attack range.
  - Its description shows the reach.
- **Timing:** nothing new. Better Combat's attack lasts the attack cooldown, which is already the
  weapon's real swings per second from Mycelium's stats.
- **Reach, Mycelium side (`WeaponBag.MeasureReach`):** the held weapon's attack is stepped
  through on the spore's animator, and the farthest point its collider reaches is sent. New field
  in the Weapons table (0x1C, protocol v28).
- **Test aid `-mcReachTest`:** measures every weapon prefab.
- **`MycBetterCombat.java`:** Better Combat runs its own attacks instead of Minecraft's
  `startAttack`. So Mycelium's attack events (weapon attributes) now come from Better Combat's
  public `ATTACK_START` event for its weapons. `MinecraftMixin` skips them, so a swing isn't
  counted twice.
- DESIGN §5.0.5, PROTOCOL, README.

**Tested how:**
- The real Mycelium build with the fake Minecraft in The Carcass (`-mcReachTest`): all 19 weapon
  prefabs measured, 0.64 to 3.73 blocks (list in DESIGN §5.0.5).
- In Daybreak Arboretum with the Stick really held (`LINKTEST_HOLD=1`): 1.65 blocks, the same as
  the test.
- The mod jar built (the presets are in it), and the fake Mycelium still builds.

**Still broken / not tested:**
- **Not tested in Minecraft:** the animations, the reach, the event listener.
- Coal Axe and Eternal Torch measured only 0.64 blocks in the test. Their prefabs are built
  differently, so the test may not place them as the game does. In play, the held weapon is
  measured as it really is.
- A weapon you've never held yet keeps Minecraft's 3 blocks until you first select it.
- Not sure whether Better Combat's own attack also respects our "full charge only" rule. Its
  attacks don't always pass through `startAttack`.

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
