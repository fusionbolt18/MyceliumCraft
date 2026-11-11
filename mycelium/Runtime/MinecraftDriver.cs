using System;
using System.Reflection;
using MyceliumCraft.Link;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;

namespace MyceliumCraft
{
    /// <summary>
    /// Phase 1: Minecraft walks on Mycelium's ground, and the spore follows Minecraft in X, Y and Z.
    ///
    /// Mycelium's code is not changed. This component starts itself before the first scene, finds
    /// Mycelium's PlayerController by name (reflection), and runs its FixedUpdate after
    /// PlayerController's (execution order 10000) so the velocity it sets wins.
    ///
    /// Each level: scan the ground around the spore and send it to Minecraft, teleport Minecraft's
    /// player onto the spore, wait until Minecraft confirms, then drive the spore from Minecraft.
    /// </summary>
    [Preserve]
    [DefaultExecutionOrder(10000)]
    public sealed class MinecraftDriver : MonoBehaviour
    {
        // How hard the spore is pulled toward where Minecraft says it should be (1/s), and caps.
        const float Gain = 12f;
        const float MaxSpeed = 40f;
        // If Mycelium's own collision holds the spore further than this from Minecraft's player,
        // move Minecraft's player back onto the spore (Mycelium knows something Minecraft doesn't).
        const float ResyncDistance = 6f;
        // ...or if they disagree by more than StuckDistance for StuckSeconds in a row (wedged
        // somewhere only one game thinks is open, which the 6-unit rule never notices).
        const float StuckDistance = 1.5f;
        const float StuckSeconds = 2f;
        // Gaps under one-sided surfaces smaller than this many spore heights are filled (GroundScanner).
        const float FillBelowSporeHeights = 2.5f;
        // Rescan the ground when the spore is this many blocks from the grid's centre (grid half-width is 32).
        const double RescanBlocks = 16;
        // PlayerController adds this acceleration itself every physics step (gravityForce = -25).
        static readonly Vector3 MyceliumGravity = new Vector3(0f, -25f, 0f);

        enum Phase { Idle, WaitingForTeleport, Driving }

        static Type playerControllerType;
        static FieldInfo rbField;
        static FieldInfo animatorField;
        static FieldInfo moveSpeedField;
        static FieldInfo canActField;
        static FieldInfo invincibleField; // PlayerController.isInvincible: set for exactly a hit's knockback
        static FieldInfo activeDodgeField; // PlayerController.activeDodge: a dash (the roll, Blitz) is on
        static FieldInfo lookingField;
        bool lookingOff; // we switched PlayerController.looking off
        static FieldInfo actionsField;
        static PropertyInfo pausedProperty;

        MyceliumLink link;
        WorldMapping map;
        readonly GroundScanner ground = new GroundScanner();
        readonly CameraSync cameraSync = new CameraSync();
        readonly HurtTilt hurtTilt = new HurtTilt();
        readonly SproutWatch sprout = new SproutWatch();
        readonly WeaponBag weapons = new WeaponBag();
        readonly SkillAim skillAim = new SkillAim();
        readonly DashWatch dash = new DashWatch();
        readonly CycloneLength cycloneLength = new CycloneLength(); // Living Cyclone as long at any Speed
        readonly GoalWatch goal = new GoalWatch(); // the level's goal, on Minecraft's locator bar
        readonly PullWatch pull = new PullWatch(); // the final boss's sand pull, for Minecraft's player
        readonly ColonyWatch colony = new ColonyWatch();
        readonly BirthCutscene birth = new BirthCutscene();
        readonly NutrientPanel nutrientPanel = new NutrientPanel();
        readonly ColonyPassThrough passThrough = new ColonyPassThrough();
        readonly ColonyPush colonyPush = new ColonyPush();
        readonly BossBar bossBar = new BossBar();
        readonly ShopUndo shopUndo = new ShopUndo();
        readonly TraitLeaks traitLeaks = new TraitLeaks();
        // Blacksmith Ironspore opens Minecraft's smithing screen (ForgeWatch).
        readonly ForgeWatch forge = new ForgeWatch();
        readonly FuryWatch furyWatch = new FuryWatch();
        readonly HealthPanel healthPanel = new HealthPanel();
        readonly CurioKey curioKey = new CurioKey();
        bool forgeDriving, forgeScreenOpen; // as of this frame's Update, for the next frame's early catch
        // Test aid "-mcNoTraitFix": leave Mycelium's leftover trait hooks alone, as Mycelium does.
        static readonly bool NoTraitFix = Array.IndexOf(Environment.GetCommandLineArgs(), "-mcNoTraitFix") >= 0;
        readonly UnarmedCyclone unarmedCyclone = new UnarmedCyclone();
        readonly MenuBody menuBody = new MenuBody();
        readonly VanishWatch vanish = new VanishWatch();
        readonly KeyHints keyHints = new KeyHints();
        int keyHintTest = -1; // -mcKeyHintTest: 1 on, 0 off, -1 not looked yet
        readonly SkillIconWatch skillIcons = new SkillIconWatch();
        bool vanished;
        bool aimDriving;
        float aimYaw;
        readonly InputBridge input = new InputBridge();
        readonly KeyOwnership keys = new KeyOwnership();
        readonly McOverlay overlay = new McOverlay();
        readonly McPlayerModel playerModel = new McPlayerModel();
        readonly McColonyBodies colonyBodies = new McColonyBodies();
        readonly EnemyCombat combat = new EnemyCombat();
        readonly ShroomSmack shrooms = new ShroomSmack();
        readonly RollSound rollSound = new RollSound();
        readonly ResourceBag resources = new ResourceBag();
        readonly uint[] bindKinds = new uint[Proto.BMax];
        readonly uint[] bindCodes = new uint[Proto.BMax];
        bool heldByMycelium;   // Mycelium has the controls (menu, pause, cutscene): don't drive the spore
        bool checkAfterHold;   // just got the controls back: resync if Mycelium moved the spore
        string controlsWhy = "";
        bool needScan = true;
        bool rescanCompare;    // the next scan is a rescan after Mycelium had the controls: log what changed
        bool firstScan;        // the next scan is the first in this level: log what solid things are near the spore
        Component controller;
        Rigidbody rb;
        Animator animator;
        float nextFindTime;
        ulong frame;
        bool mcWasAlive;
        bool driving;
        Phase phase = Phase.Idle;
        bool swapWhileDriving, startAfterSwap; // a spore swap while Minecraft drives: no Idle in between
        /// <summary>Minecraft drives the spore, or is being moved to it (a re-sync or a swap's teleport).</summary>
        bool Steering => phase == Phase.Driving || phase == Phase.WaitingForTeleport;

        McState mc;
        uint lastEpoch;
        Teleport tp;
        float tpSentTime;
        float feetOffset; // rb.position.y minus the bottom of the spore's collider
        float playerHeightBlocks; // what Minecraft's player height should be (0 = leave it)
        bool haveLastMc;
        double lastMcX, lastMcY, lastMcZ;
        Vector3 mcVel; // Minecraft velocity mapped to Mycelium units/s, smoothed
        Vector3 lastMoveDir; // the way you last walked in Minecraft (Mycelium frame, horizontal unit; zero until you do), for the dodge roll
        float mcSpeed; // Minecraft horizontal speed, blocks/s, smoothed
        Vector3 lastTarget;
        float lastError;
        // Brief interruptions (a torn read, one "not in a world" frame) are bridged with the last
        // good state instead of dropping everything: that dropped the camera to Mycelium's third
        // person for a frame (seen at 20:21:12 in your log).
        const float HiccupSeconds = 0.25f;
        float lastGoodMcTime = -1f;
        McState lastCamMc;
        float lastCamMcTime = -1f;
        int hiccups;
        float nextHiccupLog;
        // Clock: Mycelium's unscaled time <-> Windows QueryPerformanceCounter, read at
        // each frame, so a physics step's time can be turned into Minecraft's clock.
        long frameQpc;
        double frameUnscaled;
        float lastAlpha; // ticks past the newest stored tick point at the last physics step (log; negative = inside history)
        readonly TickHistory ticks = new TickHistory();
        int tickSteps, fallbackSteps; // physics steps that used tick timing / the fallback (log)
        float stuckSince = -1f; // when error first went above StuckDistance, or -1
        int resyncs;
        float nextLogTime;
        bool shakeTest;
        bool holdTest;
        bool hitTest;
        float nextHitTime;
        GameObject fakeHitter;
        bool mouseTest;
        readonly System.Collections.Generic.List<UnityEngine.InputSystem.Key> heldTestKeys = new System.Collections.Generic.List<UnityEngine.InputSystem.Key>();
        float holdTestStart = -1f;
        int holdTestStep;
        float nextShakeTime;
        int framesSinceLog;

        [Preserve]
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        internal static void Boot()
        {
            // Unity calls this in our own build; the BepInEx plugin calls it (bepinex/Plugin.cs).
            if (GameObject.Find("MyceliumCraft") != null) return;
            var go = new GameObject("MyceliumCraft");
            DontDestroyOnLoad(go);
            go.AddComponent<MinecraftDriver>();
        }

        void Awake()
        {
            McLog.Open();
            BackgroundWindow.Init();
            McConfig.Load();
            map = new WorldMapping(McConfig.Scale);
            // Teleport numbers start from the clock, so a restarted Mycelium never reuses a number a
            // still-running Minecraft has already applied.
            tp.Seq = (uint)Environment.TickCount & 0x7FFFFF00u;
            // Mycelium normally freezes when its window loses focus. Minecraft has the focus while
            // you play, so keep running.
            Application.runInBackground = true;

            playerControllerType = Type.GetType("PlayerController, Assembly-CSharp");
            if (playerControllerType != null)
            {
                rbField = playerControllerType.GetField("rb", BindingFlags.Public | BindingFlags.Instance);
                animatorField = playerControllerType.GetField("animator", BindingFlags.Public | BindingFlags.Instance);
                moveSpeedField = playerControllerType.GetField("moveSpeed", BindingFlags.Public | BindingFlags.Instance);
                canActField = playerControllerType.GetField("canAct", BindingFlags.Public | BindingFlags.Instance);
                invincibleField = playerControllerType.GetField("isInvincible", BindingFlags.Public | BindingFlags.Instance);
                activeDodgeField = playerControllerType.GetField("activeDodge", BindingFlags.Public | BindingFlags.Instance);
                lookingField = playerControllerType.GetField("looking", BindingFlags.Public | BindingFlags.Instance);
                actionsField = playerControllerType.GetField("playerActionsAsset", BindingFlags.Public | BindingFlags.Instance);
            }
            Type globalData = Type.GetType("GlobalData, Assembly-CSharp");
            pausedProperty = globalData?.GetProperty("isGamePaused", BindingFlags.Public | BindingFlags.Static);
            McLog.Info($"loaded, protocol v{Proto.Version}. PlayerController found={playerControllerType != null} rb={rbField != null} animator={animatorField != null} moveSpeed={moveSpeedField != null} GlobalData.isGamePaused={pausedProperty != null}");

            try
            {
                link = MyceliumLink.Create();
                McLog.Info($"shared memory {Proto.MappingName} created (already existed={link.AlreadyExisted})");
            }
            catch (Exception e)
            {
                McLog.Info("FAILED to create shared memory: " + e.Message);
            }

            // The Blacksmith's Interact is caught before PlayerController reads it (ForgeWatch).
            var forgeCatch = new GameObject("MyceliumCraft forge catch").AddComponent<ForgeCatch>();
            DontDestroyOnLoad(forgeCatch.gameObject);
            forgeCatch.EarlyUpdate = () =>
            {
                forge.Early(forgeDriving, forgeScreenOpen);
                curioKey.Early(forgeDriving); // the Curio key with no decoration near: no wave
            };
            forgeCatch.LateAfter = forge.Late;

            // Test aid: "-mcScene <name>" skips the menus and loads that level directly.
            string[] args = Environment.GetCommandLineArgs();
            int sceneArg = Array.IndexOf(args, "-mcScene");
            string directScene = sceneArg >= 0 && sceneArg + 1 < args.Length ? args[sceneArg + 1] : null;

            // Test aid: "-mcShakeTest" fires a level's own camera shake every 3 s while Minecraft drives.
            shakeTest = Array.IndexOf(args, "-mcShakeTest") >= 0;
            // Test aid: "-mcHoldTest" takes the controls away the way Mycelium's menus do
            // (PlayerController.DisableController) 3 s into driving, and gives them back 3 s later.
            holdTest = Array.IndexOf(args, "-mcHoldTest") >= 0;
            // Test aid: "-mcHitTest" knocks the spore back every 4 s, as an enemy hit does
            // (PlayerController.Knockback, from in front of the spore).
            hitTest = Array.IndexOf(args, "-mcHitTest") >= 0;
            // Test aid: "-mcHurtTest" damages the spore by 1 every 3 s from a test hitter 1.5 units
            // away, going round it: in front, right, behind, left (for the hurt tilt).
            hurtTest = Array.IndexOf(args, "-mcHurtTest") >= 0;
            // Test aid: "-mcKnockTest" (with -mcHurtTest): each test hit also knocks the spore back
            // through Mycelium's own PlayerController.Knockback, as enemy hits do.
            knockTest = Array.IndexOf(args, "-mcKnockTest") >= 0;
            // Test aid: "-mcOverlayCheck" reads back Mycelium's finished screen once a second where
            // the fake Minecraft's test picture (LINKTEST_OVERLAY=1) has its boxes, switching the
            // overlay on and off each time, and logs the colors.
            if (Array.IndexOf(args, "-mcOverlayCheck") >= 0) StartCoroutine(OverlayCheck());
            // Test aid: "-mcModelCheck" reads back Mycelium's finished screen once a second at the
            // Minecraft body's head, with its depth passes off and on in turn, and logs the colour.
            if (Array.IndexOf(args, "-mcModelCheck") >= 0) StartCoroutine(ModelCheck());
            // Test aid: "-mcLoadTest [scene]" starts a scene change through Mycelium's own SceneLoader
            // (with its fade and loading screen) after 4 s of driving, to the tutorial or that scene.
            int loadArg = Array.IndexOf(args, "-mcLoadTest");
            if (loadArg >= 0)
            {
                string target = loadArg + 1 < args.Length && !args[loadArg + 1].StartsWith("-") ? args[loadArg + 1] : "New Tutorial";
                StartCoroutine(LoadTest(target));
            }
            // Test aid: "-mcFlashCheck" reads back the screen around an enemy while its hurt flash is
            // on and just after, and logs colour and detail (neighbouring-pixel contrast) for both.
            if (Array.IndexOf(args, "-mcFlashCheck") >= 0) StartCoroutine(FlashCheck());
            // Test aid: "-mcFlashAll" flashes every enemy in the level once, 4 s into driving, so the
            // log describes each kind's renderers (HurtFlash.Describe).
            if (Array.IndexOf(args, "-mcFlashAll") >= 0) StartCoroutine(FlashAll());
            // Test aid: "-mcFungalTest" switches Fungal Might on (x2, PlayerAttack.ActivateFungalMight)
            // 0.5 s into driving and logs each change of it for 15 s (with LINKTEST_FUNGAL=1).
            if (Array.IndexOf(args, "-mcFungalTest") >= 0) StartCoroutine(FungalTest());
            // Test aid: "-mcDashTest" starts Mycelium's own dodge roll (PlayerController.Dodging) 4 s
            // into driving, as the Dodge key would; DashWatch sends it to Minecraft.
            if (Array.IndexOf(args, "-mcDashTest") >= 0) StartCoroutine(DashTest());
            // Test aid: "-mcBlitzTest" does what Blitz's coroutine does (activeDodge on, the velocity
            // 40 units/s along the spore's facing for 0.3 s) 4 s into driving; DashWatch sends it along the camera.
            if (Array.IndexOf(args, "-mcBlitzTest") >= 0) StartCoroutine(BlitzTest());
            // Test aid: "-mcVanishTest" does what Trophic Cascade's vanish does to the spore (every
            // renderer off for 1 s, then on again, not particles) 4 s into driving.
            if (Array.IndexOf(args, "-mcVanishTest") >= 0) StartCoroutine(VanishTest());
            // Test aid: "-mcCycloneTest" sets the held weapon's isCycloning for 2 s, as Living Cyclone
            // does while it spins, 4 s into driving.
            if (Array.IndexOf(args, "-mcCycloneTest") >= 0) StartCoroutine(CycloneTest());
            // Test aid: "-mcUnarmedCycloneTest" uses Living Cyclone (if equipped) 4 s into driving, as its
            // key does, then logs every second whether it can be used again. For The Carcass (no weapon).
            if (Array.IndexOf(args, "-mcUnarmedCycloneTest") >= 0) StartCoroutine(UnarmedCycloneTest());
            // Test aid: "-mcSporeMoveTest" (The Carcass) has another spore use Living Cyclone on its own,
            // then, with the controls off as the shop has them, makes that spore yours as the shop's
            // spore list does, and logs whether the overlay would show.
            if (Array.IndexOf(args, "-mcSporeMoveTest") >= 0) StartCoroutine(SporeMoveTest());
            // Test aid: "-mcSkillCooldownTest" uses each of your spore's skills, as its key does, and logs
            // their cooldowns.
            if (Array.IndexOf(args, "-mcSkillCooldownTest") >= 0) StartCoroutine(SkillCooldownTest());
            // Test aid: "-mcTraitLeakTest" (The Carcass) swaps spores 20 times in the shop's skill menu,
            // then counts the trait hooks on Mycelium's events and uses a skill 10 times, logging its cooldown.
            if (Array.IndexOf(args, "-mcTraitLeakTest") >= 0) StartCoroutine(TraitLeakTest());
            // Test aid: "-mcLevelEndTest" walks the spore into the level's end 4 s into driving
            // (LevelEnd's own trigger), then, 1.5 s later, switches its controls back on as a hit's
            // knockback ending does.
            if (Array.IndexOf(args, "-mcLevelEndTest") >= 0) StartCoroutine(LevelEndTest());
            // Test aid: "-mcShroomTest" swings at the nearest mushroom from just in front of it 4 s
            // into driving, again at once (its cooldown), then facing away after the cooldown.
            if (Array.IndexOf(args, "-mcShroomTest") >= 0) StartCoroutine(ShroomTest());
            // Test aid: "-mcResourceTest" picks up three resources from 4 s into driving, 1 s apart (the
            // level's own with GetMaterial.Interact, else stand-ins as AddMaterial does), logs the bag and
            // the panel, waits 5 s (for LINKTEST_THROW_RESOURCE), then does a level end without saving
            // and puts the stored counts back.
            if (Array.IndexOf(args, "-mcResourceTest") >= 0) StartCoroutine(ResourceTest());
            combat.Swung += () =>
            {
                weapons.HeldSwing(out int type, out float reach, out float perSecond);
                shrooms.Swung(type, reach, perSecond);
            };
            // Test aid: "-mcSwapTest" swaps weapons the way picking one up does (WeaponInteraction: the
            // old one is let go into the world, a new one goes in the hand) 4 s into driving, and logs
            // which of the two Mycelium draws.
            if (Array.IndexOf(args, "-mcSwapTest") >= 0) StartCoroutine(SwapTest());
            // Test aid: "-mcSporeSwapTest" swaps to the next spore, then logs which spores are still drawn.
            if (Array.IndexOf(args, "-mcSporeSwapTest") >= 0) StartCoroutine(SporeSwapTest());
            // Test aid: "-mcBumpTest" stands another spore in your way; "-mcBumpTestSolid" the same, with spores solid as before.
            bool solid = Array.IndexOf(args, "-mcBumpTestSolid") >= 0;
            passThrough.Off = solid;
            if (solid || Array.IndexOf(args, "-mcBumpTest") >= 0) StartCoroutine(BumpTest());
            // Test aid: "-mcSpearTest" calls the held weapon the Stickbug Spear, to see its shots from swings (LINKTEST_FUNGAL=1).
            if (Array.IndexOf(args, "-mcSpearTest") >= 0) StartCoroutine(SpearTest());
            // Test aid: "-mcBossBarTest" opens Mycelium's boss bar 3 s into driving, then takes health off, as meeting and hitting the boss does.
            if (Array.IndexOf(args, "-mcBossBarTest") >= 0) StartCoroutine(BossBarTest());
            // Test aid: "-mcBossDeathTest" beats the final boss 4 s into driving, with damage as a hit's.
            if (Array.IndexOf(args, "-mcBossDeathTest") >= 0) StartCoroutine(BossDeathTest());
            // Test aid: "-mcPopUpTest" gives a thing a pop-up, then destroys the thing without taking the pop-up away.
            if (Array.IndexOf(args, "-mcPopUpTest") >= 0) StartCoroutine(PopUpTest());
            // Test aid: "-mcBossIntroTest" walks you into the final boss's trigger 3 s into driving, as reaching it does.
            if (Array.IndexOf(args, "-mcBossIntroTest") >= 0) StartCoroutine(BossIntroTest());
            // Test aid: "-mcBossPullTest" switches the final boss's attacks on 3 s into driving, as the intro's end
            // does: its sand pull starts 5 s later and lasts 5 s (PullWatch logs it).
            if (Array.IndexOf(args, "-mcBossPullTest") >= 0) StartCoroutine(BossPullTest());
            // Test aid: "-mcBossPullNative" (run without Minecraft): the boss's attacks on with nobody driving,
            // then how far Mycelium's own pull moves a spore that stands still, every second.
            if (Array.IndexOf(args, "-mcBossPullNative") >= 0) StartCoroutine(BossPullNativeTest());
            // Test aid: "-mcLoop <n>" starts on that loop, to see its nutrient bonus.
            int loopArg = Array.IndexOf(args, "-mcLoop");
            if (loopArg >= 0 && loopArg + 1 < args.Length && int.TryParse(args[loopArg + 1], out int loop))
            {
                Type.GetType("GlobalData, Assembly-CSharp")?.GetField("currentLoop")?.SetValue(null, loop);
                McLog.Info($"loop test: on loop {loop}");
            }
            // Test aid: "-mcGrowTest" grows a new spore as the shop does, 4 s into driving.
            if (Array.IndexOf(args, "-mcGrowTest") >= 0) StartCoroutine(GrowTest());
            // Test aid: "-mcPickupTest" puts a Cactus Mace on the ground in front of the spore 4 s into
            // driving and picks it up through Mycelium's own WeaponInteraction.Interact.
            if (Array.IndexOf(args, "-mcPickupTest") >= 0) StartCoroutine(PickupTest());
            // Test aid: "-mcReachTest" measures every Slash, Smash and Stab weapon's reach 3 s into
            // driving (each put in the spore's hand as Mycelium holds it, then removed; see the log).
            if (Array.IndexOf(args, "-mcReachTest") >= 0) StartCoroutine(ReachTest());
            // Test aid: "-mcRepickTest" picks up the nearest weapon lying in the world (tag "Weapon")
            // 8 s into driving, through WeaponInteraction.Interact (for thrown weapons).
            if (Array.IndexOf(args, "-mcRepickTest") >= 0) StartCoroutine(RepickTest());
            // Test aid: "-mcCurioTest" (The Carcass) uses each decoration as your Curio key does and logs what follows.
            if (Array.IndexOf(args, "-mcCurioTest") >= 0) StartCoroutine(CurioTest());
            // Test aid: "-mcCurioKeyTest" (The Carcass) presses the Curio key away from any decoration.
            if (Array.IndexOf(args, "-mcCurioKeyTest") >= 0) StartCoroutine(CurioKeyTest());
            // Test aid: "-mcSeatTest" (The Carcass) has colony spores use the stump chairs and the bed, and logs where they sit and face.
            if (Array.IndexOf(args, "-mcSeatTest") >= 0) StartCoroutine(SeatTest());
            // Test aid: "-mcWaveTest" (The Carcass) has another spore wave 4 s into driving.
            if (Array.IndexOf(args, "-mcWaveTest") >= 0) StartCoroutine(WaveTest());
            // Test aid: "-mcHudDump" logs the health HUD's objects (what draws where) 3 s into driving.
            if (Array.IndexOf(args, "-mcHudDump") >= 0) StartCoroutine(HudDump());
            // Test aid: "-mcDefenseTest" makes Defense Mechanism's bubble and green particles 4 s into driving
            // and logs which of their parts are drawn (run with LINKTEST_VIEW=0, 1 or 2).
            if (Array.IndexOf(args, "-mcDefenseTest") >= 0) StartCoroutine(DefenseTest());
            // Test aid: "-mcMoodTest" raises then lowers the happiness of you and one other spore 4 s into
            // driving, as Mycelium does, and logs whether their happy and sad particles are drawn.
            if (Array.IndexOf(args, "-mcMoodTest") >= 0) StartCoroutine(MoodTest());
            // Test aid: "-mcDrumTest" makes one other spore hit the beetle drum (Mycelium's DrumHit trigger)
            // 4 s into driving, and logs its Animator state and its bones' turns every 0.05 s.
            if (Array.IndexOf(args, "-mcDrumTest") >= 0) StartCoroutine(DrumTest());
            // Test aid: "-mcKeyHintTest" also clears the area 3 s into driving (as LevelEnd does), so the
            // curios make their indicators, pops them in as when you come near, and logs their text.
            if (Array.IndexOf(args, "-mcKeyHintTest") >= 0) StartCoroutine(CurioHintTest());
            // Test aid: "-mcRainTest" turns Mycelium's rain on for 10 s, then off for 10 s, and so on, while driving.
            rainTest = Array.IndexOf(args, "-mcRainTest") >= 0;
            // Test aid: "-mcSporeCurioTest" puts one other spore on the dance pad (BustMoves), the bed (Sleep,
            // then Lay) and the chair (SitFloor) 4 s into driving, 4 s each, as those curios do, and logs its pose.
            if (Array.IndexOf(args, "-mcSporeCurioTest") >= 0) StartCoroutine(SporeCurioTest());
            // Test aid: "-mcFuryTest" switches Relentless Fury's effect on 4 s into driving, as the skill
            // does, and logs its stacks and the held weapon's damage every 0.5 s (run with LINKTEST_HIT=1).
            if (Array.IndexOf(args, "-mcFuryTest") >= 0) StartCoroutine(FuryTest());
            // Test aid: "-mcForgeTest" (the first level) picks up three weapons, brings the Blacksmith in
            // front of the spore and presses Mycelium's Interact key; run with LINKTEST_FORGE=1.
            if (Array.IndexOf(args, "-mcForgeTest") >= 0) StartCoroutine(ForgeTest());
            // Test aid: "-mcInteractWatch" logs whenever Mycelium's closest interactable thing (what its
            // pop-up and Interact key are for) changes, with where it is.
            if (Array.IndexOf(args, "-mcInteractWatch") >= 0) StartCoroutine(InteractWatch());
            // Test aid: "-mcSwingTest" plays the spore's Slash, Smash and Stab animations at its attack
            // speed 4 s into driving and times each to 90 % (when PlayerAttack gives the controls back).
            if (Array.IndexOf(args, "-mcSwingTest") >= 0) StartCoroutine(SwingTest());
            // Test aid: "-mcAttribute <Name>" gives the held weapon that attribute (as Mycelium adds one:
            // AttributeAssigner.PickAttFromString, then Equipped) 1 s into driving.
            int attArg = Array.IndexOf(args, "-mcAttribute");
            if (attArg >= 0 && attArg + 1 < args.Length) StartCoroutine(AttributeTest(args[attArg + 1]));
            // Test aid: "-mcHealthTest" halves the spore's health 4 s into driving, then raises its
            // Vitality by 5 and takes it away again (max health up, then down), 1.5 s apart. Not in
            // The Carcass (Vitality there can earn achievements).
            if (Array.IndexOf(args, "-mcHealthTest") >= 0) StartCoroutine(HealthTest());
            // Test aid: "-mcShopTest" (The Carcass, where the shop is) opens the level-up shop 4 s into
            // driving, raises Vitality and closes it; then raises and lowers it and commits. Max health
            // after each. Nothing is bought: nutrients are put back as they were.
            if (Array.IndexOf(args, "-mcShopTest") >= 0) StartCoroutine(ShopTest());
            // Test aid: "-mcSkillMenuTest" (The Carcass) opens the shop's skill menu 4 s into driving for 3 s.
            if (Array.IndexOf(args, "-mcSkillMenuTest") >= 0) StartCoroutine(SkillMenuTest());
            // Test aid: "-mcBlurCheck" logs how sharp Mycelium's finished screen is twice a second
            // (average difference between neighbouring pixels in the middle), to see the pause blur.
            if (Array.IndexOf(args, "-mcBlurCheck") >= 0) StartCoroutine(BlurCheck());
            // Test aid: "-mcHoldKeys wasd..." makes Mycelium believe those keys are held (through
            // Unity's Input System), as real keys in Mycelium's focused window would be.
            // Test aid: "-mcMouseTest" moves the mouse up and down (through Unity's Input System), as
            // your real mouse does in Mycelium's focused window.
            mouseTest = Array.IndexOf(args, "-mcMouseTest") >= 0;
            if (mouseTest)
            {
                UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
                McLog.Info("-mcMouseTest: moving the mouse up and down once Minecraft drives");
            }
            int keysArg = Array.IndexOf(args, "-mcHoldKeys");
            if (keysArg >= 0 && keysArg + 1 < args.Length)
            {
                // Letters ("wasd") or comma-separated Input System key names ("space,leftShift,tab").
                string spec = args[keysArg + 1];
                if (spec.Contains(","))
                {
                    foreach (string name in spec.Split(','))
                        if (Enum.TryParse(name.Trim(), true, out UnityEngine.InputSystem.Key k)) heldTestKeys.Add(k);
                }
                else
                {
                    foreach (char c in spec.ToLowerInvariant())
                        if (c >= 'a' && c <= 'z') heldTestKeys.Add(UnityEngine.InputSystem.Key.A + (c - 'a'));
                }
                // The test window usually isn't focused; let the Input System take events anyway.
                UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
                McLog.Info($"-mcHoldKeys: holding {string.Join(",", heldTestKeys)} once Minecraft drives");
            }

            // Test aid: "-mcFps <n>" caps Mycelium's frame rate (re-applied per scene, which may set its own).
            int fpsArg = Array.IndexOf(args, "-mcFps");
            int testFps = fpsArg >= 0 && fpsArg + 1 < args.Length && int.TryParse(args[fpsArg + 1], out int f) ? f : 0;

            UnityEngine.Events.UnityAction<Scene, LoadSceneMode> onSceneLoaded = (scene, mode) =>
            {
                StartCoroutine(skillIcons.Watch(scene.name)); // the skill bar's icons after loading
                if (testFps > 0)
                {
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = testFps;
                }
                if (directScene != null && scene.name != directScene)
                {
                    McLog.Info($"-mcScene: loading '{directScene}' directly (test aid)");
                    string next = directScene;
                    directScene = null;
                    SceneManager.LoadScene(next);
                }
                McLog.Info($"scene loaded: '{scene.name}'");
                controller = null;
                rb = null;
                nextFindTime = 0;
                cameraSync.Reset();
                sprout.SceneLoaded(scene);
                weapons.SceneLoaded(scene);
                shrooms.SceneLoaded();
                resources.SceneLoaded(scene);
                colony.SceneLoaded();
                birth.SceneLoaded();
                nutrientPanel.SceneLoaded();
                healthPanel.SceneLoaded();
                bossBar.SceneLoaded();
                shopUndo.SceneLoaded();
                menuBody.SceneLoaded();
                forge.SceneLoaded();
                SetPhase(Phase.Idle, "new scene");
            };
            SceneManager.sceneLoaded += onSceneLoaded;
            // As a BepInEx plugin we start after the first scene (Main Menu) has loaded: run it for that one.
            if (McPaths.AsPlugin && SceneManager.GetActiveScene().isLoaded) onSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        // Pause timing (your report, 2026-11-10: the pause screen lagged in the BepInEx plugin): our own
        // Update and LateUpdate per frame, and the scene's object count, once a second while paused.
        readonly System.Diagnostics.Stopwatch frameWatch = new System.Diagnostics.Stopwatch();
        double ourMs;
        int ourFrames;
        float nextPauseTimeLog;

        void Update()
        {
            frameWatch.Restart();
            try { UpdateInner(); }
            finally { ourMs += frameWatch.Elapsed.TotalMilliseconds; }
        }

        void PauseTiming()
        {
            ourFrames++;
            if (!frozenByMinecraft) { nextPauseTimeLog = 0f; ourMs = 0; ourFrames = 0; return; }
            if (Time.realtimeSinceStartup < nextPauseTimeLog) return;
            if (nextPauseTimeLog > 0f)
            {
                int objects = UnityEngine.Object.FindObjectsOfType<GameObject>().Length;
                McLog.Info($"pause timing: our Update+LateUpdate {ourMs / Mathf.Max(1, ourFrames):F2} ms a frame over {ourFrames} frames, whole frame {Time.unscaledDeltaTime * 1000f:F1} ms, {objects} objects in the scene, time scale {Time.timeScale}");
            }
            nextPauseTimeLog = Time.realtimeSinceStartup + 1f;
            ourMs = 0;
            ourFrames = 0;
        }

        void UpdateInner()
        {
            BackgroundWindow.Update();
            cycloneLength.Update(); // always, linked or not (your request, 2026-11-10)
            frameQpc = MyceliumLink.Qpc();
            frameUnscaled = Time.unscaledTimeAsDouble;
            if (link == null) return;
            frame++;
            framesSinceLog++;
            link.Beat();
            FindPlayer();

            uint flags = 0;
            Vector3 pos = Vector3.zero;
            float yaw = 0;
            if (rb != null)
            {
                flags |= Proto.MycInLevel;
                pos = rb.position;
                yaw = rb.rotation.eulerAngles.y;
            }
            if (IsPaused()) flags |= Proto.MycPaused;
            if (McConfig.Overlay && rb != null) flags |= Proto.MycOverlay;
            // Trophic Cascade's vanish: your Minecraft body goes too, and Minecraft goes to third person.
            vanished = rb != null && vanish.Update();
            if (vanished) flags |= Proto.MycVanished;
            // Living Cyclone: the same view change while it spins, and no other skill meanwhile.
            if (rb != null) unarmedCyclone.Update(); // no weapon (The Carcass): its cooldown, and it counts as spinning
            bool cycloning = rb != null && Cycloning();
            if (cycloning) flags |= Proto.MycCyclone;
            BlockSkillsWhileCycloning(cycloning);
            // The dodge roll: Minecraft plays its combat roll animation on your body (MycSkillAnimation.java).
            if (rb != null && animator != null && animator.isActiveAndEnabled && animator.GetBool("Roll")) flags |= Proto.MycRoll;
            // A decoration in use (PlayerController.InteractWithCurio): Minecraft poses your body the same way.
            string decorationState = null;
            int decoration = rb != null ? DecorationPose(out decorationState) : 0;
            if (decoration != 0 && decorationFaced == 0 && forgeDriving) FaceDecoration(decorationState);
            decorationFaced = decoration;
            if (decoration == 1) flags |= Proto.MycSit;
            else if (decoration == 2) flags |= Proto.MycLie;
            else if (decoration == 5) flags |= Proto.MycLie | Proto.MycLieFront;
            else if (decoration == 4) flags |= Proto.MycAtDecoration;
            // Relentless Fury as its description says: the frenzy (FuryWatch); Minecraft shows red potion particles.
            furyWatch.Update(forgeDriving, weapons);
            if (furyWatch.On) flags |= Proto.MycFury;
            // Mycelium's rain (RainEvent -> WeatherSettingsScript.UpdateRain): Minecraft has a thunderstorm meanwhile (MycWeather.java).
            if (Raining()) flags |= Proto.MycRain;
            // Full screen or in a window: Minecraft's window does the same (MycWindowSize.java; your request, 2026-11-10).
            if (Screen.fullScreenMode != FullScreenMode.Windowed) flags |= Proto.MycFullscreen;
            combat.Frenzied = furyWatch.On;
            hurtTilt.IgnoreDrop += furyWatch.TakeDrained(); // the frenzy's drain isn't a hit
            // Mycelium's health, which Minecraft's follows (/ 5).
            float health = 0f, maxHealth = 0f;
            shopUndo.Update(); // the shop shut without committing: max health back (ShopUndo)
            if (!NoTraitFix) traitLeaks.Update(); // traits act only for the spore that's yours (TraitLeaks)
            if (rb != null) ReadHealth(out health, out maxHealth);
            dash.ForMinecraft(map, out float dashX, out float dashZ); // Blitz, the roll: Minecraft's player dashes (DashWatch)
            int nutrients = rb != null ? ReadNutrients() : -1;
            // The resources you carry: items in Minecraft's inventory, thrown ones follow theirs (ResourceBag, MycResources.java).
            int resourceCount = resources.Update(rb != null ? nutrientTracker : null, phase == Phase.Driving && link.MinecraftAlive, link, map);
            link.WriteMycState(flags, pos.x, pos.y, pos.z, yaw, frame, tp, rb != null ? playerHeightBlocks : 0f, health, maxHealth,
                               nutrients, sprout.Update(), rb != null ? SpeedFactor() : 0f,
                               Screen.width, Screen.height, // Minecraft's window follows this size (MycWindowSize.java)
                               dashX, dashZ, resourceCount, resources.Entries,
                               rb != null ? SporeName() : ""); // Minecraft's player goes by it (MycName.java)

            // Your nutrients: Minecraft's experience level, on the overlay, instead of Mycelium's panel.
            nutrientPanel.Update(McConfig.Overlay && rb != null && Steering && link.MinecraftAlive);
            // Mycelium's health bar and number: Minecraft's hearts show your health (the name stays).
            healthPanel.Update(McConfig.Overlay && rb != null && Steering && link.MinecraftAlive);
            // The final boss's health: Minecraft's boss bar instead of Mycelium's (BossBar, MycBossBar.java).
            bossBar.Update(link, McConfig.Overlay && rb != null && phase != Phase.Idle && link.MinecraftAlive,
                           heldByMycelium && !this.controlsWhy.EndsWith("knockback"));
            // The boss's sand pull moves Minecraft's player (PullWatch, MycBossPull.java).
            pull.ForMinecraft(map, out float pullX, out float pullZ);
            if (link != null) link.WriteBossPull(pullX, pullZ);
            // The level's goal (what Navigate Camera turns to): Minecraft's locator bar (GoalWatch, MycGoal.java).
            goal.Update(link, map, rb != null && phase == Phase.Driving && link.MinecraftAlive);

            // Your colony: in Minecraft's player list instead of Mycelium's colony meter (ColonyWatch, MycColony.java).
            if (colony.Update(rb != null && Steering && link.MinecraftAlive, (float)map.Scale))
            {
                if (rb != null) link.WriteColony(colony.Count, colony.Colors, colony.Caps, colony.Speeds, colony.Emerges, colony.Happiness, colony.Names, colony.Average, colony.Current, colony.NutrientBonus, colony.Waving, colony.Drums, colony.Poses, colony.Yaws);
                else link.WriteColony(0, colony.Colors, colony.Caps, colony.Speeds, colony.Emerges, colony.Happiness, colony.Names, -1f, -1, -1, 0u, colony.Drums, colony.Poses, colony.Yaws);
            }

            // You and the other spores collide in Minecraft (ColonyPush), not in Mycelium's physics (ColonyPassThrough).
            passThrough.Update(link.MinecraftAlive && phase != Phase.Idle ? rb : null, colony);
            colonyPush.Update(link, link.MinecraftAlive && phase != Phase.Idle ? rb : null, colony, map, feetOffset);

            bool alive = link.MinecraftAlive;
            if (alive != mcWasAlive)
            {
                mcWasAlive = alive;
                McLog.Info(alive ? $"Minecraft linked (pid {link.MinecraftPid})" : "Minecraft gone (heartbeat stopped); Mycelium controls the spore again");
            }

            // Camera: every rendered frame (not the 50 Hz physics step), so turning stays smooth.
            // The camera keeps following Minecraft through teleports (WaitingForTeleport) and brief
            // hiccups: releasing it, even for one frame, showed Mycelium's third-person camera
            // (seen after a beetle's knockback, at 20:22:23 in your log).
            if (alive && link.TryReadMcState(out McState camFresh) && camFresh.InWorld)
            {
                lastCamMc = camFresh;
                lastCamMcTime = Time.realtimeSinceStartup;
            }
            bool camFresh2 = lastCamMcTime >= 0f && Time.realtimeSinceStartup - lastCamMcTime <= HiccupSeconds;
            bool look = alive && phase != Phase.Idle && camFresh2;
            cameraSync.Dashing = dash.Dashing; // the dash kick: the view widens a little (CameraSync)
            if (dash.Dashing) cameraSync.DashName = dash.Rolling ? "dodge roll" : "Blitz";
            cameraSync.Update(look, lastCamMc);
            // Minecraft's hurt tilt: when the spore loses health, lean the view toward what hit it.
            cameraSync.Hurt = hurtTilt;
            hurtTilt.Update(health, maxHealth, rb, playerHealth, camFresh2 ? lastCamMc.DamageTilt : 1f);

            // Input bridge: Minecraft plays from Mycelium's keyboard and mouse during normal play.
            // In test runs (-mcBackground) the bridge never uses your keyboard and mouse, even if
            // Windows hands Mycelium the focus.
            // Turning it off gives Windows' pointer back to Mycelium, locked, which puts it in the
            // middle of the window: a hit while Minecraft's inventory was open moved your cursor to
            // the middle (your report, 2026-10-20). So:
            //  - re-sync teleports (WaitingForTeleport, a few frames after a knockback) count as
            //    driving, as they do for key ownership;
            //  - a hit's knockback doesn't take the controls while a Minecraft screen is open (there
            //    you only move the cursor; nothing in Mycelium moves).
            bool mcScreenOpen = camFresh2 && (lastCamMc.Flags & Proto.McScreenOpen) != 0;
            bool myceliumControls = MyceliumHasControls(out string bridgeWhy) && !(mcScreenOpen && bridgeWhy.EndsWith("knockback"));
            uint playReason = !(alive && phase != Phase.Idle) ? Proto.ReasonNotDriving
                            : myceliumControls && bridgeWhy.EndsWith("knockback") ? Proto.ReasonKnockback
                            : myceliumControls && DecorationPose(out _) != 0 ? Proto.ReasonLookOnly
                            : myceliumControls ? Proto.ReasonMyceliumControls
                            : Proto.ReasonActive;
            uint reason = !McConfig.InputBridge || BackgroundWindow.Enabled ? Proto.ReasonOff
                        : !Application.isFocused ? Proto.ReasonNotFocused
                        : playReason;
            if (BackgroundWindow.Enabled && playReason != lastPlayReason)
            {
                // Test runs: the bridge is always off; log what it would do with your focus.
                lastPlayReason = playReason;
                McLog.Info($"input bridge (test run, off): with focus it would be {(playReason == Proto.ReasonActive ? "ON" : InputBridge.ReasonText(playReason))}{(mcScreenOpen ? ", a Minecraft screen is open" : "")}");
            }
            // Mycelium's key binds, as set in Minecraft's Controls menu (kept if Minecraft goes away).
            int hintCount = 0;
            if (alive)
            {
                int bindCount = link.TryReadBindings(bindKinds, bindCodes);
                if (bindCount >= 0) keys.SetBindings(bindCount, bindKinds, bindCodes);
                hintCount = bindCount;
            }
            else if (!keys.HasBindings) keys.SetBindings(0, null, null); // defaults until Minecraft sends some
            input.Update(link, reason == Proto.ReasonActive, reason, keys.OwnedScancodes, keys.OwnedButtons, mcScreenOpen);
            // Mycelium's hints ("Press [R] to Open", the skill bar) show those keys while Minecraft drives.
            // Test aid: "-mcKeyHintTest" puts the hints on in a background run (no focus, so keys aren't Minecraft's).
            if (keyHintTest < 0) keyHintTest = Array.IndexOf(Environment.GetCommandLineArgs(), "-mcKeyHintTest") >= 0 ? 1 : 0;
            keyHints.Update(keys.Active || (keyHintTest == 1 && phase == Phase.Driving), hintCount, bindKinds, bindCodes);
            // Minecraft's hand, HUD and screens on top, during play (not over Mycelium's own menus).
            // ...and not over Mycelium's loading, fade, startup or death screens (it's drawn above all its UI).
            bool covered = ScreenCover.Showing(out string coverWhy);
            if (covered != wasCovered)
            {
                wasCovered = covered;
                McLog.Info(covered ? $"overlay: kept off while {coverWhy}" : "overlay: Mycelium's screen cover is gone");
            }
            // Mycelium taking the controls hides it, but a brief "can act" dip (a hit's knockback,
            // about 30 ms) doesn't: that made the overlay blink on every hit (your report, 2026-10-19).
            // Its pause still hides it at once.
            bool controls = MyceliumHasControls(out string controlsWhy);
            if (!controls) controlsSince = -1f;
            else if (controlsSince < 0f) controlsSince = Time.realtimeSinceStartup;
            // A hit's knockback never hides it (it lasts until the "Hurt" animation is half done, up to
            // about half a second: the grace alone wasn't enough).
            // Using a decoration takes the controls too (canAct off until you move), but it's still
            // play: the overlay stays (your request, 2026-11-08).
            // A menu (the shop, choosing a spore, pause) fades Mycelium's HUD out as it takes the controls;
            // a skill takes them too but leaves the HUD. So with the HUD fading out the overlay hides at
            // once, not after the grace (your report, 2026-11-10: it took too long to go away).
            bool menu = controls && HudFadingOut();
            if (menu != wasMenuFade)
            {
                wasMenuFade = menu;
                if (menu) McLog.Info($"overlay: Mycelium's HUD is fading out with the controls taken ({controlsWhy}): a menu, so hidden at once");
            }
            bool overlayBlocked = controls && !controlsWhy.EndsWith("knockback") && DecorationPose(out _) == 0
                                  && (controlsWhy == "paused" || menu || Time.realtimeSinceStartup - controlsSince >= OverlayControlsGrace);
            birth.Update(alive && phase != Phase.Idle); // a spore grown in the shop: no HUD, no overlay
            overlay.Update(link, McConfig.Overlay && alive && phase != Phase.Idle && !overlayBlocked && !covered && !birth.On);
            // Minecraft's pause menu (Escape) pauses Mycelium too.
            // Emotecraft's wave on you: spores near you and in front of you may wave back (CurioKey).
            curioKey.EmoteWave(forgeDriving && (lastCamMc.Flags & Proto.McEmoteWave) != 0);
            FreezeForMinecraftPause(alive && phase == Phase.Driving && camFresh2 && (lastCamMc.Flags & Proto.McPaused) != 0);
            pauseBlur.Update();
            // Third person: your Minecraft body at the spore's feet, instead of the spore.
            bool bodyWanted = McConfig.Overlay && alive && phase != Phase.Idle && rb != null && cameraSync.Controlling;
            playerModel.Update(link, bodyWanted, rb != null ? rb.transform.position - Vector3.up * feetOffset : Vector3.zero, (float)map.Scale);
            if (McConfig.Overlay && alive && phase != Phase.Idle) menuBody.Update(playerModel, feetOffset); // the skill menu's spore: your Minecraft body
            playerModel.SetVanished(vanished);
            cameraSync.ReplaceSpore = playerModel.Shown;
            playerModel.Cutscene = cameraSync.CutsceneLive; // your whole body in a cutscene's view
            // The other spores as Minecraft characters too, while yours is one.
            colonyBodies.Update(link, playerModel.Shown, colony, playerModel, feetOffset, (float)map.Scale);
            // Which game owns which key, while Minecraft drives the spore.
            // (Re-sync teleports count as driving: giving Mycelium its keys back for those few frames
            // let a click become Mycelium's attack right after a hit, your report 2026-10-19.)
            keys.Update(alive && phase != Phase.Idle, mcScreenOpen);
            // Combat: Mycelium's enemies to Minecraft's stand-ins, and their hits back.
            // Your weapons: the bag, Minecraft's carrots, the one Minecraft's hand holds.
            WeaponBag.DropOrphanPopUp(); // a pop-up for something salvaged or gone
            if (rb != null) weapons.Update(this, link, alive && phase == Phase.Driving && camFresh2, camFresh2 ? lastCamMc.HandWeapon : 0u, map);
            // Blacksmith Ironspore: Minecraft's smithing screen, and forging what it asks for.
            // A re-sync teleport (WaitingForTeleport, a moment after bumping into something) counts as driving, as for the keys.
            forgeDriving = rb != null && alive && (phase == Phase.Driving || phase == Phase.WaitingForTeleport) && camFresh2;
            forgeScreenOpen = mcScreenOpen;
            forge.Update(this, link, weapons, forgeDriving);
            combat.HeldKnockback = weapons.HeldKnockback;
            // Mycelium's attack events for Minecraft's swings (weapon attributes).
            if (rb != null) combat.FollowSwings(lastCamMc.Swings, camFresh2 && alive);
            // ...and the levels' mushrooms bounce when a swing reaches them (ShroomSmack).
            if (rb != null) shrooms.Update(rb.position, WorldMapping.MycYaw(lastCamMc.Yaw), map.Scale);
            dash.BeforeCoroutines(rb); // Mycelium's dash coroutines write the velocity after this (DashWatch)
            // Skills fire where Minecraft's head looks, not where its body faces.
            aimDriving = rb != null && alive && phase == Phase.Driving && camFresh2;
            aimYaw = lastCamMc.Yaw;
            skillAim.Update(aimDriving, aimYaw);
            if (rb != null) combat.Update(link, rb.position, map);
            if (shakeTest && look && Time.realtimeSinceStartup >= nextShakeTime) FireTestShake();
            if (holdTest && controller != null && (phase == Phase.Driving || holdTestStep > 0)) RunHoldTest();
            if (hitTest && controller != null && phase == Phase.Driving && Time.realtimeSinceStartup >= nextHitTime) RunHitTest();
            if (hurtTest && playerHealth != null && phase == Phase.Driving && Time.realtimeSinceStartup >= nextHurtTime) RunHurtTest();
            if (mouseTest && phase == Phase.Driving && UnityEngine.InputSystem.Mouse.current != null)
            {
                // About what a hand does: up and down, 20 px per frame at most, 1.5 s period.
                float dy = 20f * Mathf.Sin(Time.unscaledTime * 2f * Mathf.PI / 1.5f);
                var ms = new UnityEngine.InputSystem.LowLevel.MouseState { delta = new Vector2(0f, dy) };
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(UnityEngine.InputSystem.Mouse.current, ms);
            }
            if (heldTestKeys.Count > 0 && UnityEngine.InputSystem.Keyboard.current != null)
            {
                var state = phase == Phase.Driving
                    ? new UnityEngine.InputSystem.LowLevel.KeyboardState(heldTestKeys.ToArray())
                    : new UnityEngine.InputSystem.LowLevel.KeyboardState();
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(UnityEngine.InputSystem.Keyboard.current, state);
            }

            if (Time.realtimeSinceStartup >= nextLogTime)
            {
                // FixedUpdate only reads Minecraft while a spore exists; read here too so the log
                // shows Minecraft's numbers in menus.
                if (alive && !driving) link.TryReadMcState(out mc);
                LogStatus(pos);
                nextLogTime = Time.realtimeSinceStartup + 1f;
                framesSinceLog = 0;
            }
        }

        /// <summary>
        /// Where a dodge roll starting now goes (Mycelium frame, horizontal unit; zero: as Mycelium
        /// aims it). Movement keys you hold come first (your rule, 2026-11-02: they take priority),
        /// then the way you last walked.
        /// </summary>
        Vector3 RollToward(out string from)
        {
            if (map != null && (lastCamMc.MoveX != 0f || lastCamMc.MoveZ != 0f))
            {
                map.ToMyc(lastCamMc.MoveX, 0, lastCamMc.MoveZ, out double kx, out _, out double kz);
                var keys = new Vector3((float)kx, 0f, (float)kz);
                if (keys.sqrMagnitude > 1e-8f) { from = "the movement keys you hold"; return keys.normalized; }
            }
            from = "the way you last moved";
            return lastMoveDir;
        }

        /// <summary>Where Mycelium's camera looks, flattened (horizontal unit; zero without a camera or looking straight up/down). For Blitz.</summary>
        static Vector3 CameraFlatForward()
        {
            Camera cam = Camera.main;
            if (cam == null) return Vector3.zero;
            Vector3 f = cam.transform.forward;
            f.y = 0f;
            return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.zero;
        }

        /// <summary>Switch Mycelium's own "face where I walk" (PlayerController.looking) off while Minecraft drives, and back on afterwards.</summary>
        void SetMyceliumTurning(bool on)
        {
            if (controller == null || lookingField == null) return;
            if (!on)
            {
                if ((bool)lookingField.GetValue(controller)) lookingField.SetValue(controller, false);
                if (!lookingOff) McLog.Info("facing: Mycelium's own turning (PlayerController.looking) off while Minecraft drives");
                lookingOff = true;
            }
            else if (lookingOff)
            {
                lookingField.SetValue(controller, true);
                lookingOff = false;
                McLog.Info("facing: Mycelium's own turning back on");
            }
        }

        void LateUpdate()
        {
            frameWatch.Restart();
            try { LateUpdateInner(); }
            finally { ourMs += frameWatch.Elapsed.TotalMilliseconds; PauseTiming(); }
        }

        void LateUpdateInner()
        {
            cameraSync.RefreshHidden();
            // Again before drawing: the shop's button runs after our Update, and turns the HUD on.
            birth.Update(link != null && link.MinecraftAlive && phase != Phase.Idle);
            skillAim.Update(aimDriving, aimYaw); // again, after the body's turn this frame
            // Mycelium's dash coroutines (Blitz, the roll) have written the spore's velocity by now.
            dash.LateUpdate(controller, rb, link != null && link.MinecraftAlive && phase == Phase.Driving,
                            animator != null && animator.isActiveAndEnabled && animator.GetBool("Roll"), RollToward(out string rollFrom), rollFrom, CameraFlatForward());
            // The roll's sound as you press the key, not at its end (RollSound).
            rollSound.Update(link != null && link.MinecraftAlive && phase == Phase.Driving, animator != null && animator.isActiveAndEnabled && animator.GetBool("Roll"));
            // Mycelium's resource panel stays out of sight: your resources are in Minecraft's inventory.
            resources.KeepPanelOut(link != null && link.MinecraftAlive);
            resources.LevelEndNotice(); // "Resources stored at The Carcass", a picture per type
        }

        void FixedUpdate()
        {
            if (!driving) SetMyceliumTurning(true);
            driving = false;
            if (link == null || rb == null) return;
            // The boss's pull: the force it gathered on the spore since the last step, before the velocity is set below (PullWatch).
            pull.Step(rb, link.MinecraftAlive && phase == Phase.Driving, controlsWhy.EndsWith("knockback"));
            bool alive = link.MinecraftAlive;
            McState fresh = default;
            bool read = alive && link.TryReadMcState(out fresh) && fresh.InWorld;
            if (read)
            {
                mc = fresh;
                lastGoodMcTime = Time.realtimeSinceStartup;
            }
            else if (!alive || lastGoodMcTime < 0f || Time.realtimeSinceStartup - lastGoodMcTime > HiccupSeconds)
            {
                SetPhase(Phase.Idle, alive ? "Minecraft not in a world" : "Minecraft gone");
                return;
            }
            else
            {
                // A hiccup: keep going with Minecraft's last good state.
                hiccups++;
                if (Time.realtimeSinceStartup >= nextHiccupLog)
                {
                    nextHiccupLog = Time.realtimeSinceStartup + 1f;
                    McLog.Info($"hiccup: Minecraft's state unreadable or 'not in a world' for a moment ({hiccups} so far); using its last good state");
                }
            }
            if (IsPaused()) return;

            Vector3 pos = rb.position;
            map.ToMc(pos.x, pos.y, pos.z, out double sporeMcX, out _, out double sporeMcZ);
            if (needScan || Math.Abs(sporeMcX - ground.CenterX) > RescanBlocks || Math.Abs(sporeMcZ - ground.CenterZ) > RescanBlocks)
            {
                needScan = false;
                float[] before = rescanCompare ? (float[])ground.Pieces.Clone() : null;
                double beforeX = ground.OriginX, beforeZ = ground.OriginZ;
                string summary = ground.Scan(pos, map);
                link.WriteGround(ground.Version, GroundScanner.N, ground.OriginX, ground.OriginZ, GroundScanner.CellBlocks, ground.Pieces);
                McLog.Info(summary);
                if (before != null) LogScanChanges(before, beforeX, beforeZ);
                rescanCompare = false;
                if (firstScan) LogSolidsNear(pos);
                firstScan = false;
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-mcDumpGrid") >= 0) DumpGrid();
                // Test aid: "-mcColumn x,z;x,z" logs what the scan finds in those columns (Minecraft coordinates).
                string[] cmd = Environment.GetCommandLineArgs();
                int colArg = Array.IndexOf(cmd, "-mcColumn");
                if (colArg >= 0 && colArg + 1 < cmd.Length)
                    foreach (string xz in cmd[colArg + 1].Split(';'))
                    {
                        string[] p = xz.Split(',');
                        if (p.Length == 2 && double.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double cx)
                                          && double.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double cz))
                        {
                            McLog.Info(ground.DescribeColumn(cx, cz, pos, map));
                            // Again with the rays starting 50 units higher: a box the rays start inside isn't seen.
                            McLog.Info("(rays 50 higher) " + ground.DescribeColumn(cx, cz, pos + Vector3.up * 50f, map));
                        }
                    }
            }

            if (phase == Phase.Idle || mc.Epoch != lastEpoch || startAfterSwap)
            {
                bool newWorld = phase != Phase.Idle && mc.Epoch != lastEpoch;
                lastEpoch = mc.Epoch;
                SendTeleport(newWorld ? "Minecraft entered a new world" : startAfterSwap ? "spore swapped" : "start", setFacing: true);
                startAfterSwap = false;
                return;
            }

            if (phase == Phase.WaitingForTeleport)
            {
                if (mc.TeleportAck != tp.Seq) return;
                // Belt and braces: wait (up to 2 s) until Minecraft's reported position is really there.
                double off = Math.Sqrt((mc.X - tp.X) * (mc.X - tp.X) + (mc.Y - tp.Y) * (mc.Y - tp.Y) + (mc.Z - tp.Z) * (mc.Z - tp.Z));
                if (off > 2 && Time.realtimeSinceStartup - tpSentTime < 2f) return;
                McLog.Info($"teleport {tp.Seq} confirmed by Minecraft after {Time.realtimeSinceStartup - tpSentTime:F2} s: mc=({mc.X:F2}, {mc.Y:F2}, {mc.Z:F2})");
                SetPhase(Phase.Driving, "teleport confirmed");
                stuckSince = -1f;
                ticks.Clear(); // the teleport is a jump; don't interpolate across it
                haveLastMc = false;
                mcVel = Vector3.zero;
            }

            // Mycelium has the controls (its menu, pause or a cutscene): leave the spore to it. This
            // comes before the distance checks, so Mycelium moving the spore isn't taken for a
            // disagreement.
            if (MyceliumHasControls(out string why))
            {
                if (!heldByMycelium || why != controlsWhy)
                {
                    heldByMycelium = true;
                    controlsWhy = why;
                    McLog.Info($"hold: Mycelium has the controls ({why}); the spore is Mycelium's until it gives them back");
                }
                haveLastMc = false;
                stuckSince = -1f;
                return;
            }
            if (heldByMycelium)
            {
                heldByMycelium = false;
                checkAfterHold = true;
                // Scan the ground again: while Mycelium had the controls (the sprout on arrival, a
                // cutscene, a menu) things can appear and go. The first scan happens 0.1 s after
                // arriving, mid-sprout, and something solid there then stays a block in Minecraft
                // after it's gone (your report, 2026-11-03: an invisible wall where you appear).
                needScan = true;
                rescanCompare = true;
                McLog.Info("hold: Mycelium gave the controls back; Minecraft drives the spore again");
            }

            // Smoothed Minecraft velocity: the walk animation, the log, and feed-forward so the
            // spore moves with Minecraft instead of trailing behind it.
            if (haveLastMc)
            {
                double dx = mc.X - lastMcX, dy = mc.Y - lastMcY, dz = mc.Z - lastMcZ;
                mcSpeed = Mathf.Lerp(mcSpeed, (float)Math.Sqrt(dx * dx + dz * dz) / Time.fixedDeltaTime, 0.2f);
                var vNow = new Vector3((float)dx, (float)dy, (float)-dz) * (float)(map.Scale / Time.fixedDeltaTime);
                mcVel = Vector3.Lerp(mcVel, vNow, 0.3f);
                // Your walk's direction, for the dodge roll: not while a dash moves you (that's not you walking).
                Vector3 flatVel = new Vector3(mcVel.x, 0f, mcVel.z);
                if (dash.DashDirection == Vector3.zero && !(activeDodgeField != null && controller != null && activeDodgeField.GetValue(controller) is bool dodging && dodging)
                    && flatVel.magnitude > (float)(1.0 * map.Scale))
                    lastMoveDir = flatVel.normalized;
            }
            haveLastMc = true;
            lastMcX = mc.X;
            lastMcY = mc.Y;
            lastMcZ = mc.Z;

            // Where Minecraft's player is at the moment this physics step stands for, worked out from
            // Minecraft's raw ticks and their time, exactly as Minecraft's renderer does. Reading the
            // latest interpolated position instead aliased against the physics steps (Unity runs 0,
            // 1 or 2 per frame, often back to back): the spore moved in uneven steps and the camera,
            // which follows it, made the world judder, worst when looking down at the ground.
            float bodyYaw = mc.BodyYaw;
            double mx = mc.X, my = mc.Y, mz = mc.Z;
            ticks.Observe(mc, MyceliumLink.QpcFrequency);
            if (mc.HasTicks && frameQpc != 0 && ticks.Count > 0)
            {
                // The moment this physics step stands for, on Minecraft's clock. Unity runs physics a
                // little behind the frame, so this is often before Minecraft's latest tick; the
                // history of recent ticks covers that (only the latest two did not: the first try
                // froze the spore between ticks).
                long stepQpc = frameQpc + (long)((Time.fixedUnscaledTimeAsDouble - frameUnscaled) * MyceliumLink.QpcFrequency);
                lastAlpha = (float)ticks.Sample(stepQpc, out mx, out my, out mz, out bodyYaw);
                double tdx = mc.CurX - mc.PrevX, tdz = mc.CurZ - mc.PrevZ;
                mcSpeed = (float)(Math.Sqrt(tdx * tdx + tdz * tdz) / (mc.TickMs / 1000.0));
                tickSteps++;
            }
            else
            {
                fallbackSteps++;
            }
            map.ToMyc(mx, my, mz, out double tx, out double ty, out double tz);
            Vector3 target = new Vector3((float)tx, (float)ty + feetOffset, (float)tz);
            Vector3 error = target - pos;
            lastError = error.magnitude;
            lastTarget = target;
            if (checkAfterHold)
            {
                checkAfterHold = false;
                if (lastError > 0.5f)
                {
                    resyncs++;
                    SendTeleport($"Mycelium moved the spore {lastError:F1} units while it had the controls");
                    return;
                }
            }
            if (lastError > ResyncDistance)
            {
                resyncs++;
                stuckSince = -1f;
                McLog.Info($"held back: {BlockedBy(error)}");
                SendTeleport($"spore {lastError:F1} units from Minecraft's player (Mycelium collision?)");
                return;
            }
            if (lastError <= StuckDistance) stuckSince = -1f;
            else if (stuckSince < 0) stuckSince = Time.time;
            else if (Time.time - stuckSince >= StuckSeconds)
            {
                resyncs++;
                stuckSince = -1f;
                SendTeleport($"spore and Minecraft's player {lastError:F1} units apart for {StuckSeconds} s (one is wedged): mc=({mc.X:F2}, {mc.Y:F2}, {mc.Z:F2})");
                return;
            }

            // With tick timing the target is exact, so land on it this step; without (an older
            // Minecraft side), the old smoothed pull.
            rb.velocity = mc.HasTicks && ticks.Count > 0
                ? Vector3.ClampMagnitude(error / Time.fixedDeltaTime, MaxSpeed)
                : Vector3.ClampMagnitude(mcVel + error * Gain, MaxSpeed);
            rb.AddForce(-MyceliumGravity, ForceMode.Acceleration); // cancel PlayerController's gravity: Minecraft does gravity now
            if (rb.useGravity) rb.AddForce(-Physics.gravity, ForceMode.Acceleration);
            // Face where Minecraft's body faces (it turns toward the walk when strafing). Mycelium's own
            // PlayerController.LookAt also turns the spore toward its WASD input; with the input
            // bridge it gets WASD too, and the two fought (the spore flickered when not walking
            // forward), so its turning is switched off while Minecraft drives.
            SetMyceliumTurning(false);
            // ...except while a dash is redirected (the roll: your movement; Blitz: the camera): it faces the dash (DashWatch).
            Vector3 dashDir = dash.DashDirection;
            rb.MoveRotation(dashDir != Vector3.zero ? Quaternion.LookRotation(dashDir) : Quaternion.Euler(0f, WorldMapping.MycYaw(bodyYaw), 0f));
            if (animator != null) animator.SetBool("Walk", mcSpeed > 0.5f);
            driving = true;
        }

        /// <summary>Test aid ("-mcDumpGrid"): the grid exactly as sent to Minecraft, to grid_v{N}.csv next to the exe.</summary>
        /// <summary>
        /// After a rescan: how many cells changed since the scan before, and the first few (where,
        /// pieces before and now). A piece that was there and is gone was an invisible block.
        /// </summary>
        void LogScanChanges(float[] before, double beforeX, double beforeZ)
        {
            const int N = GroundScanner.N, F = Proto.GFloatsPerCell;
            int di = (int)Math.Round((ground.OriginX - beforeX) / GroundScanner.CellBlocks);
            int dj = (int)Math.Round((ground.OriginZ - beforeZ) / GroundScanner.CellBlocks);
            int changed = 0;
            var near = new System.Collections.Generic.List<(double d, string text)>();
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    int oi = i + di, oj = j + dj; // the same spot in the scan before
                    if (oi < 0 || oj < 0 || oi >= N || oj >= N) continue;
                    int a = (j * N + i) * F, b = (oj * N + oi) * F;
                    bool same = true;
                    for (int k = 0; k < F && same; k++)
                    {
                        float x = ground.Pieces[a + k], y = before[b + k];
                        same = x == y || Math.Abs(x - y) < 0.05f;
                    }
                    if (same) continue;
                    changed++;
                    double cx = ground.OriginX + (i + 0.5) * GroundScanner.CellBlocks, cz = ground.OriginZ + (j + 0.5) * GroundScanner.CellBlocks;
                    double d = Math.Sqrt((cx - ground.CenterX) * (cx - ground.CenterX) + (cz - ground.CenterZ) * (cz - ground.CenterZ));
                    near.Add((d, $"\n  at Minecraft ({cx:F2}, {cz:F2}), {d:F1} blocks from the spore: before {Pieces(before, b)}, now {Pieces(ground.Pieces, a)}"));
                }
            near.Sort((p, q) => p.d.CompareTo(q.d));
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < near.Count && k < 8; k++) sb.Append(near[k].text);
            McLog.Info($"ground rescan after Mycelium gave the controls back: {changed} of {N * N} cells changed{(changed > 8 ? " (the 8 nearest the spore shown)" : "")}{sb}");
        }

        static string Pieces(float[] p, int at)
        {
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < Proto.GPieces; k++)
            {
                float bottom = p[at + 2 * k], top = p[at + 2 * k + 1];
                if (top >= Proto.Unused) break;
                sb.Append(bottom <= Proto.FarBelow ? $"(far below, {top:F2})" : $"({bottom:F2}, {top:F2})");
            }
            return sb.Length > 0 ? sb.ToString() : "(nothing)";
        }

        /// <summary>First scan in a level: the solid colliders (not the terrain) within 6 units of the spore.</summary>
        void LogSolidsNear(Vector3 at)
        {
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (Collider c in Physics.OverlapSphere(at, 6f, ground.LayerMask, QueryTriggerInteraction.Ignore))
            {
                if (c is TerrainCollider) continue;
                n++;
                Bounds b = c.bounds;
                sb.Append($"\n  '{c.name}' {c.GetType().Name} layer={LayerMask.LayerToName(c.gameObject.layer)} from ({b.min.x:F1}, {b.min.y:F1}, {b.min.z:F1}) to ({b.max.x:F1}, {b.max.y:F1}, {b.max.z:F1})");
            }
            McLog.Info($"ground: {n} solid things (not the terrain) within 6 units of the spore at the first scan{sb}");
        }

        void DumpGrid()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string path = McPaths.File($"grid_v{ground.Version}.csv");
            using (var w = new System.IO.StreamWriter(path, false))
            {
                w.WriteLine($"# n={GroundScanner.N} originX={ground.OriginX.ToString(inv)} originZ={ground.OriginZ.ToString(inv)} cell={GroundScanner.CellBlocks.ToString(inv)} fillBelow={ground.FillBelowOneSided.ToString(inv)}");
                for (int c = 0; c < GroundScanner.N * GroundScanner.N; c++)
                {
                    var line = new System.Text.StringBuilder();
                    line.Append(c % GroundScanner.N).Append(',').Append(c / GroundScanner.N);
                    for (int k = 0; k < Proto.GFloatsPerCell; k++)
                        line.Append(',').Append(ground.Pieces[c * Proto.GFloatsPerCell + k].ToString("R", inv));
                    w.WriteLine(line.ToString());
                }
            }
            McLog.Info($"grid dumped to {path}");
        }

        /// <summary>
        /// Ask Minecraft to put its player where the spore stands. setFacing: also turn it to face
        /// where the spore faces (only when Minecraft starts driving). Re-syncs keep Minecraft's own
        /// facing: the spore faces where Minecraft's body points, which lags up to ~50 degrees behind
        /// where you look, so sending it turned you after hits (your report, 2026-10-20: 323 -> 273).
        /// </summary>
        void SendTeleport(string why, bool setFacing = false)
        {
            Vector3 pos = rb.position;
            float feet = FeetY(pos.y);
            feetOffset = pos.y - feet;
            map.ToMc(pos.x, feet, pos.z, out double x, out double y, out double z);
            float groundY = ground.GroundAt(x, z, (float)y);
            if (!float.IsNaN(groundY) && groundY > y - 2 && groundY < y + 2) y = Math.Max(y, groundY);
            tp.Seq++;
            tp.X = x;
            tp.Y = y + 0.01;
            tp.Z = z;
            tp.Yaw = setFacing ? WorldMapping.McYaw(rb.rotation.eulerAngles.y) : float.NaN; // NaN: keep Minecraft's facing
            tpSentTime = Time.realtimeSinceStartup;
            SetPhase(Phase.WaitingForTeleport, why);
            McLog.Info($"teleport {tp.Seq} ({why}): spore=({pos.x:F2}, {pos.y:F2}, {pos.z:F2}) feet y={feet:F2} (offset {feetOffset:F2}) -> mc=({tp.X:F2}, {tp.Y:F2}, {tp.Z:F2}) yaw={(float.IsNaN(tp.Yaw) ? "kept" : tp.Yaw.ToString("F0"))}, ground there={groundY:F2}");
        }

        /// <summary>Bottom of the spore's own (non-trigger) colliders, or its position if it has none.</summary>
        float FeetY(float fallback)
        {
            float min = float.MaxValue;
            foreach (Collider c in rb.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger || !c.enabled || c.attachedRigidbody != rb) continue;
                min = Mathf.Min(min, c.bounds.min.y);
            }
            return min == float.MaxValue ? fallback : min;
        }

        /// <summary>
        /// Height of the spore's body: its own non-trigger colliders whose layer collides with the
        /// ground layers (others pass through the level, so they don't limit where it fits).
        /// Logs every collider it looked at. 0 if none.
        /// </summary>
        float SporeHeight()
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (Collider c in rb.GetComponentsInChildren<Collider>())
            {
                if (c.attachedRigidbody != rb) continue;
                int layer = c.gameObject.layer;
                bool hitsGround = false;
                for (int l = 0; l < 32; l++)
                    if ((ground.LayerMask & (1 << l)) != 0 && !Physics.GetIgnoreLayerCollision(layer, l)) { hitsGround = true; break; }
                bool counts = !c.isTrigger && c.enabled && hitsGround;
                McLog.Info($"  spore collider '{c.name}' {c.GetType().Name} layer={LayerMask.LayerToName(layer)} trigger={c.isTrigger} enabled={c.enabled} " +
                           $"y {c.bounds.min.y:F2}..{c.bounds.max.y:F2} ({c.bounds.size.y:F2} tall) -> {(counts ? "counts" : "ignored")}");
                if (!counts) continue;
                min = Mathf.Min(min, c.bounds.min.y);
                max = Mathf.Max(max, c.bounds.max.y);
            }
            return min == float.MaxValue ? 0f : max - min;
        }

        // What your spore does at a decoration: PlayerController.InteractWithCurio fires the curio's
        // animation trigger and sets the Animator's InActionState until you move. The triggers are
        // used up at once, so the state it's in tells: 0 none, 1 sit, 2 lie, 3 wave.
        internal static readonly (string state, int pose)[] DecorationAnims = {
            ("SitFloor(Beg)", 1), ("SitFloor(Mid)", 1), ("SitFloor(End)", 1), ("SitStump(Beg)", 1), ("SitStump(Mid)", 1), ("SitStump(End)", 1),
            ("Sleep(Beg)", 2), ("Sleep(Mid)", 2), ("Sleep(End)", 2), ("Lay(Beg)", 5), ("Lay(Mid)", 5), ("Lay(End)", 5), // 2 on your back, 5 on your front
            ("Bustin Moves", 4), ("Bustin Metal Moves", 4) }; // 4: standing there (dancing)
        Animator decorationAnimator;
        readonly System.Collections.Generic.HashSet<string> decorationParams = new System.Collections.Generic.HashSet<string>();
        int lastDecoration, decorationFaced;

        /// <summary>
        /// You sit or lie at a decoration on its seat, facing the same way every time, wherever you
        /// stood and looked (your requests, 2026-11-08). Mycelium plays the animation where you stand
        /// and doesn't turn you; its other spores walk to one of the decoration's seats
        /// (Curio.traversalTransforms) first. A stump chair's seat is on top of the stump.
        /// - Where: the seat nearest you, of the decoration Mycelium used (FindClosestCurioInRange).
        /// - Facing on a stump: the seat's own direction (your choice; Mycelium's "away from the middle"
        ///   rule is near random there, as the seat is almost at the middle).
        /// - Facing on the bed: Mycelium's rule from the seat, away from the bed to sleep, towards it to lie.
        /// - Facing at the bonfire and the game board: towards it; on the dance pad: towards its drum
        ///   (Mycelium's lookTarget for its spores there), from the seat.
        /// The spore is moved and turned, and Minecraft gets a teleport there with that facing, which
        /// turns its look, body and head (your camera then looks the way you face).
        /// </summary>
        void FaceDecoration(string state)
        {
            Type ct = Type.GetType("Curio, Assembly-CSharp");
            // The decoration Mycelium used: the one its PlayerController finds (FindClosestCurioInRange).
            Component curio = controller != null ? playerControllerType.GetMethod("FindClosestCurioInRange", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(controller, null) as Component : null;
            if (curio == null) { McLog.Info($"decoration: Mycelium found no decoration near ({state}); you stay as you are"); return; }
            Transform seat = null;
            float best = float.MaxValue;
            if (ct?.GetField("traversalTransforms")?.GetValue(curio) is System.Collections.IList seats)
                foreach (object s in seats)
                {
                    var t = s.GetType().GetField("transform")?.GetValue(s) as Transform;
                    if (t == null) continue;
                    float d = Vector3.Distance(t.position, rb.position);
                    if (d < best) { best = d; seat = t; }
                }
            Vector3 at = seat != null ? seat.position + Vector3.up * feetOffset : rb.position;
            Vector3 face;
            string how;
            if (curio.GetType().Name == "ChairCurio" && seat != null)
            {
                face = Vector3.ProjectOnPlane(seat.forward, Vector3.up);
                how = $"the seat's direction (yaw {seat.eulerAngles.y:F0})";
            }
            else if (curio.GetType().Name == "BedCurio")
            {
                Vector3 away = at - curio.transform.position;
                away.y = 0f;
                bool towards = state != null && state.StartsWith("Lay");
                face = towards ? -away : away;
                how = towards ? "towards it, from the seat" : "away from it, from the seat";
            }
            else
            {
                // BonfireCurio, GameCurio: towards it (Mycelium's lookTarget). DanceCurio: towards its drum.
                Transform target = curio.transform;
                if (curio.GetType().Name == "DanceCurio" && curio.GetType().GetField("drumTransform", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(curio) is Transform drum && drum != null) target = drum;
                face = target.position - at;
                face.y = 0f;
                how = target == curio.transform ? "towards it, from the seat" : $"towards '{target.name}', from the seat";
            }
            if (face.sqrMagnitude < 1e-6f) face = rb.transform.forward;
            Quaternion turn = Quaternion.LookRotation(face.normalized, Vector3.up);
            Vector3 from = rb.position;
            rb.position = at;
            rb.transform.position = at;
            rb.velocity = Vector3.zero;
            rb.rotation = turn;
            rb.MoveRotation(turn);
            McLog.Info($"decoration: on '{(seat != null ? seat.name : "no seat")}' of '{curio.gameObject.name}' ({curio.GetType().Name}), moved {Vector3.Distance(from, at):F2} units to {at:F2}, facing {how}, yaw {turn.eulerAngles.y:F0}; Minecraft moved and turned to match");
            SendTeleport("a decoration places you", setFacing: true);
        }

        int DecorationPose(out string which)
        {
            which = null;
            Animator a = rb != null ? rb.GetComponent<Animator>() : null;
            if (a == null || !a.isActiveAndEnabled) return 0;
            if (!ReferenceEquals(a, decorationAnimator))
            {
                decorationAnimator = a;
                decorationParams.Clear();
                foreach (AnimatorControllerParameter p in a.parameters) if (p.type == AnimatorControllerParameterType.Bool) decorationParams.Add(p.name);
            }
            int pose = 0;
            if (decorationParams.Contains("InActionState") && a.GetBool("InActionState"))
            {
                AnimatorStateInfo s = a.GetCurrentAnimatorStateInfo(0);
                // Mid-blend from one part to the next (Beg to Mid), the state it's going to counts too.
                AnimatorStateInfo next = a.IsInTransition(0) ? a.GetNextAnimatorStateInfo(0) : s;
                foreach (var (name, p) in DecorationAnims)
                    if (s.IsName(name) || next.IsName(name)) { pose = p; which = name; break; }
                // Still at the decoration (InActionState stays on until you move), between two of its states: keep the pose.
                if (pose == 0) pose = lastDecoration;
            }
            if (pose != lastDecoration)
            {
                lastDecoration = pose;
                McLog.Info(pose == 0 ? "decoration: done (you moved): your Minecraft body stands again" : $"decoration: '{which}': your Minecraft body {(pose == 1 ? "sits" : pose == 2 ? "lies down on its back" : pose == 5 ? "lies down on its front" : "stands there")}; the overlay stays");
            }
            return pose;
        }

        void SetPhase(Phase next, string why)
        {
            if (phase == next) return;
            McLog.Info($"phase {phase} -> {next} ({why})");
            phase = next;
        }

        void FindPlayer()
        {
            // Swapped to another spore (PlayerController.GetStats): start over with it, as with a new
            // player, so Minecraft drives that one and moves to where it stands.
            if (controller != null && rb != null && rbField?.GetValue(controller) is Rigidbody now && now != rb)
            {
                McLog.Info($"spore swapped: Minecraft follows '{now.name}' now");
                controller = null;
                nextFindTime = 0f;
                // Minecraft keeps driving through the swap (your report, 2026-11-08): no Idle in
                // between, which for a few frames hid the overlay and your Minecraft body and brought
                // Mycelium's HUD back. Its move to the new spore is sent at the next physics step.
                swapWhileDriving = phase != Phase.Idle;
            }
            if (controller != null || playerControllerType == null || Time.realtimeSinceStartup < nextFindTime) return;
            nextFindTime = Time.realtimeSinceStartup + 0.5f;
            controller = (Component)FindObjectOfType(playerControllerType);
            rb = null;
            animator = null;
            if (controller == null) return;
            rb = rbField?.GetValue(controller) as Rigidbody ?? controller.GetComponentInChildren<Rigidbody>();
            animator = animatorField?.GetValue(controller) as Animator;
            if (rb == null) return;
            ground.ChooseLayers(rb.gameObject.layer);
            float sporeHeight = SporeHeight();
            ground.FillBelowOneSided = FillBelowSporeHeights * (sporeHeight > 0.1f ? sporeHeight : 1.7f);
            // First person looks out at 90% of the body height (Minecraft: eyes 1.62 of 1.8).
            float eyeAbovePosition = FeetY(rb.position.y) + 0.9f * (sporeHeight > 0.1f ? sporeHeight : 1.7f) - rb.position.y;
            cameraSync.SetSpore(rb.transform, eyeAbovePosition);
            McLog.Info($"camera: first-person eye {eyeAbovePosition:F2} units above the spore's position");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-mcNoFill") >= 0) ground.FillBelowOneSided = 0f; // test aid: compare without the fill
            playerHeightBlocks = McConfig.MatchSporeSize && sporeHeight > 0.1f ? (float)(sporeHeight / map.Scale) : 0f;
            McLog.Info($"spore body: {sporeHeight:F2} units tall -> Minecraft player height {(playerHeightBlocks > 0 ? $"{playerHeightBlocks:F3} blocks (scale {playerHeightBlocks / 1.8f:F3})" : "unchanged (matchSporeSize=0)")}");
            needScan = true;
            firstScan = true;
            rescanCompare = false;
            if (swapWhileDriving)
            {
                swapWhileDriving = false;
                startAfterSwap = true;
                SetPhase(Phase.WaitingForTeleport, "spore swapped; Minecraft goes on driving, its move to the new spore comes at the next physics step");
            }
            else SetPhase(Phase.Idle, "new player");
            McLog.Info($"found PlayerController on '{controller.gameObject.name}', rigidbody on '{rb.gameObject.name}' (layer {LayerMask.LayerToName(rb.gameObject.layer)}, useGravity={rb.useGravity}, interpolation={rb.interpolation}) at {rb.position:F2}, feet y={FeetY(rb.position.y):F2}");
            McLog.Info($"ground layers: {ground.LayerNames}");

            // Test aid: "-mcColliders" lists the solid colliders around the spore with nothing visible drawn there.
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-mcColliders") >= 0)
                ColliderSurvey.Run(rb.position, 60f, ground.LayerMask);

            // Test aid: "-mcSurvey" records the level's surfaces and ceilings, then quits.
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-mcSurvey") >= 0)
            {
                LevelSurvey.Run(rb.position, ground.LayerMask, (float)(GroundScanner.CellBlocks * map.Scale));
                Application.Quit();
            }
        }

        float OwnSpeed()
        {
            if (controller == null || moveSpeedField == null) return 0;
            return (float)moveSpeedField.GetValue(controller);
        }

        /// <summary>
        /// True when Mycelium has taken the controls from the player: paused (GlobalData.isGamePaused),
        /// PlayerController.canAct off (DisableController: menus like Explore/Cancel, cutscenes, level
        /// end, knockback), or its "Player" input map switched off (boss intros, level-up menus).
        /// </summary>
        bool MyceliumHasControls(out string why)
        {
            why = "";
            if (IsPaused()) { why = "paused"; return true; }
            if (EndOfLevelMenu()) { why = "end-of-level menu"; return true; }
            if (FinalBossBeaten()) { why = "boss death cutscene"; return true; }
            if (controller == null) return false;
            try
            {
                if (canActField != null && !(bool)canActField.GetValue(controller))
                {
                    // Living Cyclone switches canAct off while it spins (its controls stay on). That's
                    // play, not Mycelium taking the controls: counting it hid the overlay (your hotbar)
                    // and stopped the bridge (your report, 2026-11-01).
                    if (Cycloning()) return false;
                    // A hit's knockback switches the controls off until the "Hurt" animation is half
                    // done, and marks the player invincible for exactly that time.
                    bool knockback = invincibleField != null && (bool)invincibleField.GetValue(controller);
                    why = knockback ? "canAct off, knockback" : "canAct off";
                    return true;
                }
                object actions = actionsField?.GetValue(controller);
                var asset = actions?.GetType().GetProperty("asset")?.GetValue(actions) as UnityEngine.InputSystem.InputActionAsset;
                var map = asset?.FindActionMap("Player");
                // (Not when it's off because a Minecraft screen is open: KeyOwnership switched it off,
                // and counting that hid the overlay and stopped the bridge as soon as chat opened.)
                if (map != null && !map.enabled && !keys.SilencedHere(map)) { why = "Player input map off"; return true; }
            }
            catch (Exception e)
            {
                why = "check failed: " + e.Message;
            }
            return false;
        }

        static FieldInfo endBlackField, endMenuField;
        static PropertyInfo endInstanceProp;
        static FieldInfo endInstanceField;
        static bool endLooked;
        bool wasEndMenu;

        /// <summary>
        /// Mycelium's menu between levels ("CONTINUE your adventure or RETURN home?") is up, or fading
        /// in: EndOfLevelCanvasStuff's black or menu canvas is showing. LevelEnd switches the
        /// controls off for it (DisableController), but they can come back on under the menu: a
        /// hit's knockback ending, or an attack's, calls EnableController. That made it look like
        /// play: the overlay came back and Minecraft moved the spore (your report, 2026-11-02; at
        /// 127.22 s in that log). So the menu itself counts as Mycelium having the controls.
        /// </summary>
        /// <summary>
        /// The final boss is beaten: its death cutscene plays until the level ends. Mycelium switches
        /// its "Player" input map off at the death (BossHealth.BossDeath), but something of Mycelium's
        /// (PlayerController.EnableController) switched it back on 0.2 s later, so the overlay stayed
        /// and you could swing through the cutscene (your report, 2026-11-07; 19:27:23.76 to 23.96 in
        /// that log). So the beaten boss itself counts as Mycelium having the controls.
        /// </summary>
        bool FinalBossBeaten()
        {
            if (finalBoss == null)
            {
                if (Time.realtimeSinceStartup < nextBossLook) return false;
                nextBossLook = Time.realtimeSinceStartup + 1f;
                if (SceneManager.GetActiveScene().name != "Impact Barrens") return false;
                GameObject boss = GameObject.Find("Rival Sporemother");
                Type t = Type.GetType("EnemyHealth, Assembly-CSharp");
                finalBoss = boss != null && t != null ? boss.GetComponent(t) : null;
                bossHealthField = t?.GetField("currentHealth", BindingFlags.Public | BindingFlags.Instance);
                if (finalBoss == null) return false;
                McLog.Info("boss: found the final boss, Rival Sporemother; its death cutscene will have the controls");
            }
            return bossHealthField != null && bossHealthField.GetValue(finalBoss) is float health && health <= 0f;
        }
        Component finalBoss;
        FieldInfo bossHealthField;
        float nextBossLook;

        bool EndOfLevelMenu()
        {
            if (!endLooked)
            {
                endLooked = true;
                Type t = Type.GetType("EndOfLevelCanvasStuff, Assembly-CSharp");
                endInstanceField = t?.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                endBlackField = t?.GetField("blackCanvas", BindingFlags.NonPublic | BindingFlags.Instance);
                endMenuField = t?.GetField("endOfLevelCanvas", BindingFlags.NonPublic | BindingFlags.Instance);
                if (endInstanceField == null || endBlackField == null || endMenuField == null)
                    McLog.Info("end-of-level menu: couldn't find EndOfLevelCanvasStuff's canvases; it won't hold the spore");
            }
            bool up = false;
            try
            {
                object inst = endInstanceField?.GetValue(null);
                if (inst as UnityEngine.Object != null)
                {
                    var black = endBlackField.GetValue(inst) as CanvasGroup;
                    var menu = endMenuField.GetValue(inst) as CanvasGroup;
                    up = (black != null && black.isActiveAndEnabled && black.alpha > 0.01f)
                      || (menu != null && menu.isActiveAndEnabled && menu.alpha > 0.01f);
                }
            }
            catch (Exception) { }
            if (up != wasEndMenu)
            {
                wasEndMenu = up;
                McLog.Info(up ? "end-of-level menu: showing; Mycelium has the controls (overlay off, Minecraft doesn't move the spore)"
                              : "end-of-level menu: gone");
            }
            return up;
        }

        static bool IsPaused()
        {
            try { return pausedProperty != null && (bool)pausedProperty.GetValue(null); }
            catch { return false; }
        }

        void LogStatus(Vector3 sporePos)
        {
            string scene = SceneManager.GetActiveScene().name;
            int fps = framesSinceLog; // frames since the last status line, about 1 s ago
            if (!link.MinecraftAlive)
            {
                McLog.Info($"status: scene='{scene}' fps={fps} minecraft=not linked spore={(rb != null ? sporePos.ToString("F2") : "none")}");
                return;
            }
            float groundUnderMc = ground.GroundAt(mc.X, mc.Z, (float)mc.Y);
            McLog.Info($"status: scene='{scene}' fps={fps} phase={phase} mc=({mc.X:F2}, {mc.Y:F2}, {mc.Z:F2}) yaw={mc.Yaw:F0} flags={mc.Flags} epoch={mc.Epoch} " +
                       $"groundUnderMc={groundUnderMc:F2} tp={tp.Seq}/ack {mc.TeleportAck} mcSpeed={mcSpeed:F2} sporeOwnSpeed={OwnSpeed():F1} " +
                       $"input={(input.Active ? $"on keys={input.KeysDown}" : $"off({input.Reason})")} hold={heldByMycelium} " +
                       $"ticks={(mc.HasTicks ? $"yes a={lastAlpha:F2} steps={tickSteps}/{tickSteps + fallbackSteps}" : "no")} " +
                       $"camera={(cameraSync.Controlling ? $"synced view={cameraSync.Mode} x={cameraSync.X:F0} y={cameraSync.Y:F2} want down={cameraSync.WantDown:F0} tilt={cameraSync.LastTilt:F0} shake={cameraSync.ShakePos:F2}/{cameraSync.ShakeAngle:F1}deg" : "Mycelium")} mcView={mc.CameraMode} pitch={mc.Pitch:F0} {ViewText()} " +
                       $"| spore={(rb != null ? sporePos.ToString("F2") : "none")} target={lastTarget:F2} error={lastError:F2} vel={(rb != null ? rb.velocity.ToString("F2") : "-")} resyncs={resyncs} grid=v{ground.Version}");
        }

        bool wasCovered;
        // Mycelium's PlayerHealth (currentHealth, maxHealth), found by name.
        Component playerHealth;
        FieldInfo currentHealthField, maxHealthField;
        float nextHealthLook;
        string lastHealthLog = "";

        // Mycelium's NutrientTracker.currentNutrients, found by name: Minecraft's experience level follows it.
        Component nutrientTracker;
        FieldInfo nutrientsField;
        float nextNutrientLook;
        int lastNutrientsLog = int.MinValue;

        int ReadNutrients()
        {
            if (nutrientTracker == null && Time.realtimeSinceStartup >= nextNutrientLook)
            {
                nextNutrientLook = Time.realtimeSinceStartup + 1f;
                Type t = Type.GetType("NutrientTracker, Assembly-CSharp");
                if (t != null)
                {
                    nutrientTracker = UnityEngine.Object.FindObjectOfType(t) as Component;
                    nutrientsField = t.GetField("currentNutrients", BindingFlags.Public | BindingFlags.Instance);
                    if (nutrientTracker != null) McLog.Info($"nutrients: found Mycelium's NutrientTracker on '{nutrientTracker.gameObject.name}'");
                }
            }
            if (nutrientTracker == null || nutrientsField == null) return -1;
            int n;
            try { n = (int)nutrientsField.GetValue(nutrientTracker); }
            catch (Exception) { return -1; }
            if (n != lastNutrientsLog)
            {
                lastNutrientsLog = n;
                McLog.Info($"nutrients: {n} -> Minecraft experience level {n}");
            }
            return n;
        }

        void ReadHealth(out float health, out float max)
        {
            health = max = 0f;
            if (playerHealth == null && Time.realtimeSinceStartup >= nextHealthLook)
            {
                nextHealthLook = Time.realtimeSinceStartup + 1f;
                Type t = Type.GetType("PlayerHealth, Assembly-CSharp");
                if (t != null)
                {
                    playerHealth = (rb != null ? rb.GetComponentInParent(t) ?? rb.GetComponentInChildren(t) : null) ?? UnityEngine.Object.FindObjectOfType(t) as Component;
                    currentHealthField = t.GetField("currentHealth", BindingFlags.Public | BindingFlags.Instance);
                    maxHealthField = t.GetField("maxHealth", BindingFlags.Public | BindingFlags.Instance);
                    if (playerHealth != null) McLog.Info($"health: found Mycelium's PlayerHealth on '{playerHealth.gameObject.name}'");
                }
            }
            if (playerHealth == null || currentHealthField == null || maxHealthField == null) return;
            try
            {
                health = (float)currentHealthField.GetValue(playerHealth);
                max = (float)maxHealthField.GetValue(playerHealth);
            }
            catch (Exception) { return; }
            KeepHealthRatio(ref health, max);
            string now = $"{health:F0}/{max:F0}";
            if (now != lastHealthLog)
            {
                lastHealthLog = now;
                McLog.Info($"health: Mycelium {health:F1} of {max:F1} -> Minecraft {health / 5f:F2} of {max / 5f:F2} (half hearts)");
            }
        }
        float lastSpeedLog = -1f, nextSpeedLog;

        /// <summary>
        /// The spore's walking speed as a multiple of its base speed, for Minecraft's movement speed
        /// (your request, 2026-10-21): PlayerController.moveSpeed (base + the Speed level's bonus,
        /// changed while speed effects last: Nimble's boost, slows) / SporeAttributeRanges.moveSpeedBase.
        /// 1 at Speed level 1 with no effects. 0 when not known.
        /// </summary>
        /// <summary>
        /// The name of the spore you control (CharacterStats.sporeName on "currentPlayer"; it changes
        /// when you swap spores). Looked up every 0.5 s; "" if there's none.
        /// </summary>
        string SporeName()
        {
            if (Time.realtimeSinceStartup < nextNameLook) return sporeName;
            nextNameLook = Time.realtimeSinceStartup + 0.5f;
            GameObject player = GameObject.FindWithTag("currentPlayer");
            Type ct = Type.GetType("CharacterStats, Assembly-CSharp");
            Component stats = player != null && ct != null ? player.GetComponent(ct) : null;
            string name = (stats != null ? ct.GetField("sporeName")?.GetValue(stats) as string : null) ?? "";
            name = name.Trim();
            if (name != sporeName)
            {
                McLog.Info($"name: the spore you control is '{name}' (was '{sporeName}'); Minecraft's player goes by it");
                sporeName = name;
            }
            return sporeName;
        }
        string sporeName = "";
        float nextNameLook;

        float SpeedFactor()
        {
            GameObject parent = GameObject.Find("PlayerParent");
            Type rt = Type.GetType("SporeAttributeRanges, Assembly-CSharp");
            Component ranges = parent != null && rt != null ? parent.GetComponent(rt) : null;
            object b = ranges != null ? rt.GetField("moveSpeedBase")?.GetValue(ranges) : null;
            float baseSpeed = b is float f ? f : 0f, speed = OwnSpeed();
            if (baseSpeed <= 0.01f || speed <= 0f) return 0f;
            float factor = speed / baseSpeed;
            if (Mathf.Abs(factor - lastSpeedLog) > 0.02f && Time.realtimeSinceStartup >= nextSpeedLog)
            {
                lastSpeedLog = factor;
                nextSpeedLog = Time.realtimeSinceStartup + 0.5f; // a fading effect changes it every frame
                McLog.Info($"speed: the spore walks at {speed:F2} (base {baseSpeed:F2}) -> Minecraft's movement speed x{factor:F2}");
            }
            return factor;
        }

        Component ratioFrom;
        float ratioLastHealth, ratioLastMax;

        /// <summary>
        /// When Mycelium's max health changes (a Vitality level, a weapon's stat attribute, Fungal
        /// Might...), keep current health at the same share of it (your rule, 2026-10-20): Mycelium
        /// itself leaves current health where it was. Not when Mycelium set current health to the
        /// new max itself (ResetHealth), and not while dead. Mycelium's health bar is redrawn.
        /// </summary>
        void KeepHealthRatio(ref float health, float max)
        {
            bool same = ReferenceEquals(playerHealth, ratioFrom);
            if (same && ratioLastMax > 0f && max > 0f && Mathf.Abs(max - ratioLastMax) > 0.001f
                && ratioLastHealth > 0f && health > 0f && Mathf.Abs(health - max) > 0.001f)
            {
                float share = ratioLastHealth / ratioLastMax;
                float kept = Mathf.Clamp(share * max, 0.01f, max);
                try
                {
                    currentHealthField.SetValue(playerHealth, kept);
                    playerHealth.GetType().GetField("displayHealth", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)?.SetValue(playerHealth, kept);
                    playerHealth.GetType().GetMethod("UpdateHudHealthUI", BindingFlags.Public | BindingFlags.Instance)?.Invoke(playerHealth, null);
                    McLog.Info($"health: max {ratioLastMax:F1} -> {max:F1}; current {health:F1} -> {kept:F1}, kept at {share * 100f:F0} %");
                    health = kept;
                }
                catch (Exception e)
                {
                    McLog.Info($"health: keeping the share failed: {(e.InnerException ?? e).Message}");
                }
            }
            ratioFrom = playerHealth;
            ratioLastHealth = health;
            ratioLastMax = max;
        }

        const float OverlayControlsGrace = 0.3f; // Mycelium must keep the controls this long before the overlay hides (unless its HUD fades out: a menu)

        CanvasGroup hudGroup;
        string hudGroupScene;
        bool wasMenuFade;

        /// <summary>Mycelium's own HUD ("HUD" CanvasGroup) is fading out or faded: HUDController.FadeHUD(false), which its menus call.</summary>
        bool HudFadingOut()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (hudGroup == null || hudGroupScene != scene)
            {
                hudGroupScene = scene;
                GameObject h = GameObject.Find("HUD");
                hudGroup = h != null ? h.GetComponent<CanvasGroup>() : null;
            }
            return hudGroup != null && hudGroup.alpha < 0.99f;
        }
        float controlsSince = -1f;
        bool frozenByMinecraft;
        float timeScaleBefore = 1f;
        readonly McPauseBlur pauseBlur = new McPauseBlur();

        /// <summary>
        /// While Minecraft's pause menu is open, Mycelium's game time stops (Time.timeScale 0), so
        /// enemies, physics and animations wait, without Mycelium's own pause menu. Set again every
        /// frame in case Mycelium changes the time scale meanwhile (its hit-stop does); put back after.
        /// </summary>
        void FreezeForMinecraftPause(bool freeze)
        {
            if (freeze)
            {
                if (!frozenByMinecraft)
                {
                    frozenByMinecraft = true;
                    timeScaleBefore = Time.timeScale > 0f ? Time.timeScale : 1f;
                    McLog.Info($"pause: Minecraft's pause menu is open, Mycelium's time stopped (time scale was {timeScaleBefore:F2})");
                    pauseBlur.Set(true);
                }
                else if (Time.timeScale > 0f) timeScaleBefore = Time.timeScale;
                Time.timeScale = 0f;
            }
            else if (frozenByMinecraft)
            {
                frozenByMinecraft = false;
                pauseBlur.Set(false);
                if (Time.timeScale == 0f) Time.timeScale = timeScaleBefore;
                McLog.Info($"pause: Minecraft's pause menu closed, Mycelium's time runs again (time scale {Time.timeScale:F2})");
            }
        }

        bool wasCycloning;
        float cycloneSince;
        static FieldInfo canUseSkillField;
        bool skillsBlocked;

        /// <summary>
        /// No other skill while Living Cyclone spins (your rule, 2026-11-01). Mycelium itself allows
        /// them: Living Cyclone switches the controller back on (EnableController, so canUseSkill
        /// true) and only sets canAct off, which stops the dodge and interact but not the skill keys
        /// (PlayerController checks canUseSkill for those). So canUseSkill is kept off while it
        /// spins; its own EndSkill switches it on again.
        /// </summary>
        void BlockSkillsWhileCycloning(bool cycloning)
        {
            if (controller == null) return;
            if (canUseSkillField == null) canUseSkillField = controller.GetType().GetField("canUseSkill", BindingFlags.Public | BindingFlags.Instance);
            if (canUseSkillField == null) return;
            if (cycloning)
            {
                if (canUseSkillField.GetValue(controller) is bool on && on) canUseSkillField.SetValue(controller, false);
                if (!skillsBlocked) McLog.Info("cyclone: other skills off while Living Cyclone spins");
                skillsBlocked = true;
            }
            else if (skillsBlocked)
            {
                skillsBlocked = false;
                McLog.Info("cyclone: Living Cyclone over; skills no longer held off (its EndSkill switches them on)");
            }
        }

        /// <summary>
        /// Living Cyclone is on: it sets the held weapon's WeaponCollision.isCycloning from the start
        /// of the skill until its third spin ends (your request, 2026-11-01: third person meanwhile,
        /// as for Trophic Cascade).
        /// </summary>
        bool Cycloning()
        {
            GameObject weapon = GameObject.FindWithTag("currentWeapon");
            Type t = Type.GetType("WeaponCollision, Assembly-CSharp");
            Component wc = weapon != null && t != null ? weapon.GetComponent(t) : null;
            bool now = (wc != null && t.GetField("isCycloning")?.GetValue(wc) is bool c && c) || unarmedCyclone.Spinning;
            if (now != wasCycloning)
            {
                wasCycloning = now;
                if (now) cycloneSince = Time.time;
                McLog.Info(now ? "cyclone: Living Cyclone started; Minecraft goes to third person from first person"
                               : $"cyclone: Living Cyclone over after {Time.time - cycloneSince:F2} s");
            }
            return now;
        }

        /// <summary>Test aid: see "-mcUnarmedCycloneTest" in Awake.</summary>
        System.Collections.IEnumerator UnarmedCycloneTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(4f);
            Component skill = unarmedCyclone.Skill;
            if (skill == null) { McLog.Info("cyclone test: Living Cyclone isn't equipped"); yield break; }
            FieldInfo canSkill = skill.GetType().GetField("canSkill");
            McLog.Info($"cyclone test: using Living Cyclone; weapon held: {GameObject.FindWithTag("currentWeapon") != null}, canSkill {canSkill.GetValue(skill)}");
            skill.GetType().GetMethod("ActivateSkill").Invoke(skill, new object[] { 2, false });
            for (int i = 1; i <= 20; i++)
            {
                yield return new WaitForSeconds(0.5f);
                McLog.Info($"cyclone test: +{i * 0.5f:F1} s: canSkill {canSkill.GetValue(skill)}, cycloning {wasCycloning}, animator speed {GameObject.FindWithTag("currentPlayer")?.GetComponent<Animator>()?.speed}");
            }
        }

        /// <summary>Test aid: see "-mcTraitLeakTest" in Awake.</summary>
        System.Collections.IEnumerator TraitLeakTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(4f);
            Type actions = Type.GetType("Actions, Assembly-CSharp");
            Type traitType = Type.GetType("TraitBase, Assembly-CSharp");
            Type skillType = Type.GetType("Skill, Assembly-CSharp");
            string Hooks()
            {
                var counts = new System.Collections.Generic.SortedDictionary<string, int>();
                foreach (FieldInfo f in actions.GetFields(BindingFlags.Public | BindingFlags.Static))
                    if (f.GetValue(null) is Delegate d)
                        foreach (Delegate h in d.GetInvocationList())
                            if (h.Target != null && traitType.IsInstanceOfType(h.Target))
                            {
                                string k = $"{f.Name}<-{h.Target.GetType().Name}";
                                counts[k] = counts.TryGetValue(k, out int c) ? c + 1 : 1;
                            }
                var parts = new System.Collections.Generic.List<string>();
                foreach (var kv in counts) parts.Add($"{kv.Key} x{kv.Value}");
                return parts.Count == 0 ? "none" : string.Join(", ", parts);
            }
            McLog.Info($"trait leak test: trait hooks at the start: {Hooks()}");
            Component shop = null;
            for (int i = 0; i < 10 && (shop = shopUndo.Manager) == null; i++) yield return new WaitForSeconds(0.5f);
            var skillMenu = shop != null ? shop.GetType().GetField("SkillMenu").GetValue(shop) as GameObject : null;
            if (skillMenu == null) { McLog.Info("trait leak test: no skill menu"); yield break; }
            skillMenu.SetActive(true);
            yield return new WaitForSeconds(0.5f);
            Component menu = menuBody.Menu;
            if (menu == null) { McLog.Info("trait leak test: no skill menu manager"); skillMenu.SetActive(false); yield break; }
            for (int i = 0; i < 20; i++)
            {
                menu.GetType().GetMethod("SwapNext").Invoke(menu, null);
                yield return new WaitForSeconds(0.15f);
            }
            skillMenu.SetActive(false);
            yield return new WaitForSeconds(1.5f);
            GameObject spore = GameObject.FindWithTag("currentPlayer");
            McLog.Info($"trait leak test: after 20 swaps, yours is '{spore?.name}' (trait {spore?.GetComponent(traitType)?.GetType().Name}); trait hooks: {Hooks()}; removed by the fix so far: {traitLeaks.TotalRemoved}{(NoTraitFix ? " (fix off)" : "")}");
            Transform loadout = spore != null ? spore.transform.Find("SkillLoadout") : null;
            Component skill = loadout != null && loadout.childCount > 0 ? loadout.GetChild(0).GetComponent(skillType) : null;
            if (skill == null) { McLog.Info("trait leak test: no skill in slot 0"); yield break; }
            FieldInfo canSkill = skillType.GetField("canSkill");
            FieldInfo cooling = skillType.GetField("cooldownCoroutine");
            FieldInfo final = skillType.GetField("finalSkillCooldown", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            float normal = (float)skillType.GetMethod("GetFinalCooldown").Invoke(skill, null);
            var cds = new System.Collections.Generic.List<string>();
            // A probe on the end of ActivatedSkill: the cooldown after every trait hook has had its go.
            FieldInfo activated = actions.GetField("ActivatedSkill");
            probeFinal = final;
            probeCooldowns = cds;
            Delegate probe = Delegate.CreateDelegate(activated.FieldType, this, GetType().GetMethod(nameof(TraitLeakProbe), BindingFlags.NonPublic | BindingFlags.Instance));
            activated.SetValue(null, Delegate.Combine(activated.GetValue(null) as Delegate, probe));
            for (int i = 0; i < 10; i++)
            {
                // Stop any cooldown so each use goes through, as the key would once it's over.
                if (cooling.GetValue(skill) is Coroutine c) ((MonoBehaviour)skill).StopCoroutine(c);
                cooling.SetValue(skill, null);
                canSkill.SetValue(skill, true);
                skillType.GetMethod("ActivateSkill").Invoke(skill, new object[] { 0, false });
                yield return new WaitForSeconds(0.3f);
            }
            activated.SetValue(null, Delegate.Remove(activated.GetValue(null) as Delegate, probe));
            if (cooling.GetValue(skill) is Coroutine c2) ((MonoBehaviour)skill).StopCoroutine(c2);
            cooling.SetValue(skill, null);
            canSkill.SetValue(skill, true);
            McLog.Info($"trait leak test: {skill.GetType().Name} used 10 times; its normal cooldown {normal:F2} s; the cooldowns it got: {string.Join(", ", cds)} s");
        }

        FieldInfo probeFinal;
        System.Collections.Generic.List<string> probeCooldowns;
        void TraitLeakProbe(Component skill) => probeCooldowns?.Add($"{(float)probeFinal.GetValue(skill):F2}");

        /// <summary>Test aid: see "-mcSkillCooldownTest" in Awake.</summary>
        System.Collections.IEnumerator SkillCooldownTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(6f);
            GameObject spore = GameObject.FindWithTag("currentPlayer");
            Transform loadout = spore != null ? spore.transform.Find("SkillLoadout") : null;
            Type skillType = Type.GetType("Skill, Assembly-CSharp");
            if (loadout == null || skillType == null) { McLog.Info("skill cooldown test: no loadout"); yield break; }
            FieldInfo canSkill = skillType.GetField("canSkill"), cooling = skillType.GetField("cooldownCoroutine");
            FieldInfo final = skillType.GetField("finalSkillCooldown", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            for (int slot = 0; slot < loadout.childCount; slot++)
            {
                Component s = loadout.GetChild(slot).GetComponent(skillType);
                if (s == null) continue;
                string State() => $"canSkill {canSkill.GetValue(s)}, cooling {cooling.GetValue(s) != null}, cooldown {final?.GetValue(s)} s, active {s.gameObject.activeInHierarchy}, enabled {((Behaviour)s).enabled}";
                McLog.Info($"skill cooldown test: '{spore.name}' slot {slot} {s.GetType().Name}: before: {State()}");
                if (!(bool)canSkill.GetValue(s)) continue;
                s.GetType().GetMethod("ActivateSkill").Invoke(s, new object[] { slot, false });
                McLog.Info($"skill cooldown test: slot {slot}: just used: {State()}");
                for (int i = 1; i <= 8; i++)
                {
                    yield return new WaitForSeconds(0.5f);
                    McLog.Info($"skill cooldown test: slot {slot}: +{i * 0.5f:F1} s: {State()}");
                }
            }
        }

        /// <summary>Test aid: see "-mcSporeMoveTest" in Awake.</summary>
        System.Collections.IEnumerator SporeMoveTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(4f);
            GameObject parent = GameObject.FindWithTag("PlayerParent");
            Type swapType = Type.GetType("SwapCharacter, Assembly-CSharp");
            Type cycType = Type.GetType("LivingCyclone, Assembly-CSharp");
            Component swap = parent != null && swapType != null ? parent.GetComponent(swapType) : null;
            var list = swap != null ? swapType.GetField("characters")?.GetValue(swap) as System.Collections.IList : null;
            if (list == null || cycType == null) { McLog.Info("spore move test: no SwapCharacter list"); yield break; }
            GameObject mine = GameObject.FindWithTag("currentPlayer");
            int index = -1;
            Component skill = null;
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i] as GameObject;
                if (s == null || s == mine) continue;
                skill = s.GetComponentInChildren(cycType, true);
                if (skill != null) { index = i; break; }
            }
            if (skill == null) { McLog.Info("spore move test: no other spore has Living Cyclone"); yield break; }
            GameObject other = (GameObject)list[index];
            McLog.Info($"spore move test: '{other.name}' (#{index}) uses Living Cyclone on its own, as its moves do");
            skill.GetType().GetMethod("ActivateSkill").Invoke(skill, new object[] { skill.transform.GetSiblingIndex(), false });
            yield return new WaitForSeconds(0.3f);
            MethodInfo disable = controller?.GetType().GetMethod("DisableController");
            disable?.Invoke(controller, new object[] { null });
            McLog.Info("spore move test: controls off, as the shop has them; making that spore yours, as the shop's spore list does");
            swapType.GetMethod("SwitchCharacterGrowMenu").Invoke(swap, new object[] { index });
            for (int i = 1; i <= 16; i++)
            {
                yield return new WaitForSeconds(0.5f);
                bool controls = MyceliumHasControls(out string why);
                McLog.Info($"spore move test: +{i * 0.5f:F1} s: counted as your spin {unarmedCyclone.Spinning}, Mycelium has the controls {controls} ({why}), overlay visible {overlay.Visible}, canSkill {skill.GetType().GetField("canSkill").GetValue(skill)}, cooling {skill.GetType().GetField("cooldownCoroutine").GetValue(skill) != null}");
                disable?.Invoke(controller, new object[] { null }); // the skill's end turns them on again (Skill.EndSkill); the shop would still be open
            }
            controller?.GetType().GetMethod("EnableController", Type.EmptyTypes)?.Invoke(controller, null);
            McLog.Info("spore move test: controls back on");
        }

        /// <summary>Test aid: see "-mcCycloneTest" in Awake.</summary>
        System.Collections.IEnumerator CycloneTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(4f);
            GameObject weapon = GameObject.FindWithTag("currentWeapon");
            Type t = Type.GetType("WeaponCollision, Assembly-CSharp");
            Component wc = weapon != null && t != null ? weapon.GetComponent(t) : null;
            FieldInfo f = t?.GetField("isCycloning");
            if (wc == null || f == null) { McLog.Info("cyclone test: no held weapon with WeaponCollision.isCycloning"); yield break; }
            McLog.Info($"cyclone test: '{weapon.name}' cycloning for 2 s with canAct off, as Living Cyclone does");
            f.SetValue(wc, true);
            canActField?.SetValue(controller, false);
            yield return new WaitForSeconds(1f);
            McLog.Info($"cyclone test: 1 s in, canUseSkill {controller?.GetType().GetField("canUseSkill")?.GetValue(controller)}");
            yield return new WaitForSeconds(1f);
            if (wc != null) f.SetValue(wc, false);
            // As Living Cyclone ends: isCycloning off, then Skill.EndSkill's EnableController.
            yield return null;
            controller?.GetType().GetMethod("EnableController", Type.EmptyTypes)?.Invoke(controller, null);
            yield return null;
            McLog.Info($"cyclone test: ended as the skill does; canUseSkill now {controller?.GetType().GetField("canUseSkill")?.GetValue(controller)}");
        }

        /// <summary>Test aid: see "-mcResourceTest" in Awake.</summary>
        System.Collections.IEnumerator ResourceTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(4f);
            resources.NoSave = true; // the level end below must not touch your save
            Type t = Type.GetType("GetMaterial, Assembly-CSharp");
            var all = t != null ? UnityEngine.Object.FindObjectsOfType(t) : new UnityEngine.Object[0];
            McLog.Info($"resource test: {all.Length} resources in the level");
            GameObject player = GameObject.FindWithTag("currentPlayer");
            Type nt = Type.GetType("NutrientTracker, Assembly-CSharp");
            var tracker = nt != null ? UnityEngine.Object.FindObjectOfType(nt) as Component : null;
            GameObject hud = GameObject.Find("HUD");
            Type ht = Type.GetType("HUDItem, Assembly-CSharp");
            Component hudItem = hud != null && ht != null ? hud.GetComponent(ht) : null;
            if (tracker == null || hudItem == null || rb == null) { McLog.Info("resource test: no NutrientTracker, HUDItem or spore"); yield break; }
            var picked = new System.Collections.Generic.List<GameObject>();
            int done = 0;
            foreach (UnityEngine.Object o in all)
            {
                if (!(o is Component c) || !c.gameObject.activeInHierarchy || done >= 4) continue;
                McLog.Info($"resource test: picking up '{c.gameObject.name}'");
                t.GetMethod("Interact")?.Invoke(c, new object[] { player });
                picked.Add(c.gameObject);
                done++;
                yield return new WaitForSeconds(1f);
            }
            // None (or too few) lying in the level: what GetMaterial.AddMaterial does, with stand-in objects
            // (the one Mycelium held goes back on the ground where the new one lay).
            var fakes = new[] { ("heldLog", "Rotten Log", "RottenLog"), ("heldCalcite", "Calcite Deposit", "Calcite"), ("heldFlesh", "Flesh", "Flesh"), ("heldCalcite", "Calcite Deposit", "Calcite") };
            for (int i = done; i < 4; i++)
            {
                var (field, name, objName) = fakes[i];
                var o = new GameObject(objName);
                o.transform.position = rb.position + rb.transform.forward * (1.5f + i);
                McLog.Info($"resource test: as a pickup of {name} does");
                nt.GetMethod("LoseMaterials")?.Invoke(tracker, null);
                nt.GetField(field)?.SetValue(tracker, 1);
                var before = nt.GetField("heldItem")?.GetValue(tracker) as GameObject;
                if (before != null) { before.transform.position = o.transform.position; before.SetActive(true); }
                nt.GetField("heldItem")?.SetValue(tracker, o);
                ht.GetMethod("PickUpItem")?.Invoke(hudItem, new object[] { name });
                o.SetActive(false);
                picked.Add(o);
                yield return new WaitForSeconds(1f);
            }
            var shown = new System.Collections.Generic.List<string>();
            foreach (GameObject o in picked) shown.Add($"'{o.name}' {(o.activeSelf ? "ON THE GROUND" : "hidden")}");
            McLog.Info($"resource test: after 4 pickups the bag has {resources.Count}; {string.Join(", ", shown)}; held by Mycelium: {HeldName(tracker)}; {resources.PanelReport()}");
            // LINKTEST_THROW_RESOURCE throws the first one now (see the stand-in's log); wait for it to land.
            // It gets a pop-up as Mycelium's GetMaterial makes one when you look at it, to see it follow.
            Type tm = Type.GetType("TooltipManager, Assembly-CSharp");
            object manager = tm?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            MethodInfo create = null;
            if (tm != null)
                foreach (MethodInfo m in tm.GetMethods())
                    if (m.Name == "CreateTooltip" && m.GetParameters().Length >= 4 && m.GetParameters()[0].ParameterType == typeof(GameObject)) create = m;
            if (manager != null && create != null && picked.Count > 0)
            {
                ParameterInfo[] ps = create.GetParameters(); // the rest are optional: their defaults
                var callArgs = new object[ps.Length];
                object[] given = { picked[0], picked[0].name, "test pop-up", "" };
                for (int i = 0; i < ps.Length; i++) callArgs[i] = i < given.Length ? given[i] : ps[i].DefaultValue;
                create.Invoke(manager, callArgs);
                var popUp = tm.GetField("currentTooltip")?.GetValue(manager) as GameObject;
                McLog.Info($"resource test: pop-up made for '{picked[0].name}' at {(popUp != null ? popUp.transform.position.ToString("F1") : "none")}");
            }
            else McLog.Info("resource test: no TooltipManager.CreateTooltip: no pop-up");
            yield return new WaitForSeconds(5f);
            McLog.Info($"resource test: after the throw the bag has {resources.Count}; held by Mycelium: {HeldName(tracker)}; {string.Join(", ", ActiveList(picked))}");
            // A level end as LevelEnd.SceneChangeSetup does it (not saved: NoSave), then put back.
            // The notification LevelEnd shows as you reach the end (ResourceBag.LevelEndNotice changes it).
            Type nm = Type.GetType("NotificationManager, Assembly-CSharp");
            object notices = nm?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            string heldNow = HeldName(tracker);
            if (notices != null)
                nm.GetMethod("Notification")?.Invoke(notices, new object[] { "<color=#7FFF00>AREA COMPLETE</color>", heldNow + " stored at the Carcass", null, heldNow, -1f });
            yield return null;
            yield return null;
            var helperNow = nm?.GetField("helperText", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(notices) as Component;
            McLog.Info($"resource test: the notification says '{helperNow?.GetType().GetProperty("text")?.GetValue(helperNow)}'");
            yield return new WaitForSeconds(6f); // the notification lasts about 4.25 s
            McLog.Info($"resource test: after the notification: {GameObject.FindObjectsOfType<UnityEngine.UI.Image>().Length} images, stand-in pictures left: {(GameObject.Find("MyceliumCraft stored Calcite Deposit") != null)}");
            int[] storedBefore = Stored(nt, tracker);
            McLog.Info("resource test: as a level end does (KeepMaterials, LoseMaterials)");
            nt.GetMethod("KeepMaterials")?.Invoke(tracker, null);
            nt.GetMethod("LoseMaterials")?.Invoke(tracker, null);
            yield return null;
            yield return null;
            int[] storedAfter = Stored(nt, tracker);
            McLog.Info($"resource test: stored at the Carcass went from {string.Join("/", storedBefore)} to {string.Join("/", storedAfter)} (log/exoskeleton/calcite/flesh); the bag has {resources.Count}");
            string[] names = { "storedLog", "storedExoskeleton", "storedCalcite", "storedFlesh" };
            for (int i = 0; i < 4; i++) nt.GetField(names[i])?.SetValue(tracker, storedBefore[i]);
            McLog.Info("resource test: stored counts put back");
        }

        static int[] Stored(Type nt, Component tracker)
        {
            string[] names = { "storedLog", "storedExoskeleton", "storedCalcite", "storedFlesh" };
            var v = new int[4];
            for (int i = 0; i < 4; i++) v[i] = nt.GetField(names[i])?.GetValue(tracker) is int n ? n : -1;
            return v;
        }

        static string HeldName(Component tracker)
        {
            string s = tracker.GetType().GetMethod("GetCurrentHeldMaterial")?.Invoke(tracker, null) as string;
            return string.IsNullOrEmpty(s) ? "nothing" : s;
        }

        static System.Collections.Generic.List<string> ActiveList(System.Collections.Generic.List<GameObject> objs)
        {
            var l = new System.Collections.Generic.List<string>();
            foreach (GameObject o in objs) l.Add($"'{o.name}' {(o.activeSelf ? $"on the ground at {o.transform.position:F1}" : "hidden")}");
            return l;
        }

        /// <summary>Test aid: see "-mcShroomTest" in Awake.</summary>
        System.Collections.IEnumerator ShroomTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(4f);
            Component s = rb != null ? shrooms.NearestShroom(rb.position) : null;
            if (s == null) { McLog.Info("shroom test: no smackable mushroom in this level"); yield break; }
            Vector3 c = s.transform.position;
            Vector3 dir = rb.position - c; dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            dir.Normalize();
            float radius = 0.5f; // outside its attack hitbox
            foreach (Collider col in ShroomSmack.Hitbox(s))
                radius = Mathf.Max(radius, Vector3.Distance(new Vector3(col.bounds.center.x, 0f, col.bounds.center.z), new Vector3(c.x, 0f, c.z)) + Mathf.Max(col.bounds.extents.x, col.bounds.extents.z));
            Vector3 from = c + dir * (radius + 1f);
            float toward = Quaternion.LookRotation(-dir).eulerAngles.y;
            float reach = (float)(3.0 * 1.25 * map.Scale);
            McLog.Info($"shroom test: '{s.gameObject.name}' ({s.GetType().Name}) at {c:F2}, {(rb.position - c).magnitude:F1} units from the spore; swinging from {from:F2} facing it (reach {reach:F2} units)");
            int a = shrooms.Smack(from, toward, reach, 0, 0.75f * (float)map.Scale, "test swing");
            McLog.Info($"shroom test: facing it: {a} smacked; Attackable now {ShroomSmack.Attackable(s)}");
            int b = shrooms.Smack(from, toward, reach, 0, 0.75f * (float)map.Scale, "test swing");
            McLog.Info($"shroom test: again at once: {b} smacked (its own 1 s cooldown)");
            yield return new WaitForSeconds(2.5f);
            int d = shrooms.Smack(from, toward + 180f, reach, 2, 0.75f * (float)map.Scale, "test swing");
            McLog.Info($"shroom test: after the cooldown, facing away with a stab: {d} smacked; Attackable {ShroomSmack.Attackable(s)}");
            int e = shrooms.Smack(from, toward, reach, 2, 0.75f * (float)map.Scale, "test swing");
            McLog.Info($"shroom test: facing it with a stab: {e} smacked");
        }

        /// <summary>Test aid: see "-mcLevelEndTest" in Awake.</summary>
        System.Collections.IEnumerator LevelEndTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSecondsRealtime(4f);
            Type t = Type.GetType("LevelEnd, Assembly-CSharp");
            var end = t != null ? UnityEngine.Object.FindObjectOfType(t) as Component : null;
            GameObject player = GameObject.FindWithTag("currentPlayer");
            Collider col = player != null ? player.GetComponentInChildren<Collider>() : null;
            if (end == null || col == null) { McLog.Info($"level end test: no LevelEnd ({end != null}) or spore collider ({col != null})"); yield break; }
            McLog.Info($"level end test: the spore enters '{end.gameObject.name}' (checkpoint {t.GetField("isCheckpoint")?.GetValue(end)})");
            t.GetMethod("OnTriggerEnter", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(end, new object[] { col });
            yield return new WaitForSecondsRealtime(1.5f);
            McLog.Info($"level end test: controls back on under the menu, as a knockback's end does (has controls before: {MyceliumHasControls(out string w1)} {w1})");
            controller?.GetType().GetMethod("EnableController", Type.EmptyTypes)?.Invoke(controller, null);
            for (int i = 0; i < 5; i++)
            {
                yield return new WaitForSecondsRealtime(1f);
                McLog.Info($"level end test: +{2.5f + i:F1} s: Mycelium has the controls {MyceliumHasControls(out string w)} ({w}), timeScale {Time.timeScale}, hold {heldByMycelium}, overlay {overlay.Visible}");
            }
        }

        /// <summary>Test aid: see "-mcVanishTest" in Awake.</summary>
        System.Collections.IEnumerator VanishTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(4f);
            GameObject player = GameObject.FindWithTag("currentPlayer");
            if (player == null) { McLog.Info("vanish test: no spore"); yield break; }
            Renderer[] all = player.GetComponentsInChildren<Renderer>();
            McLog.Info($"vanish test: {all.Length} spore renderers off for 1 s, as Trophic Cascade does");
            foreach (Renderer r in all) r.enabled = false;
            yield return new WaitForSeconds(1f);
            foreach (Renderer r in all) if (r != null && !(r is ParticleSystemRenderer)) r.enabled = true;
            McLog.Info($"vanish test: renderers on again; body drawn: {playerModel.Shown}");
        }

        /// <summary>Test aid: see "-mcDashTest" in Awake.</summary>
        System.Collections.IEnumerator DashTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(4f);
            MethodInfo dodging = controller?.GetType().GetMethod("Dodging", BindingFlags.NonPublic | BindingFlags.Instance);
            if (dodging == null || !(controller is MonoBehaviour mb)) { McLog.Info("dash test: no PlayerController.Dodging"); yield break; }
            McLog.Info($"dash test: Mycelium's dodge roll, facing {rb.rotation.eulerAngles.y:F0} deg");
            mb.StartCoroutine((System.Collections.IEnumerator)dodging.Invoke(controller, null));
        }

        /// <summary>Test aid: see "-mcBlitzTest" in Awake.</summary>
        System.Collections.IEnumerator BlitzTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(4f);
            if (activeDodgeField == null || controller == null) { McLog.Info("blitz test: no PlayerController.activeDodge"); yield break; }
            Vector3 cam = CameraFlatForward();
            McLog.Info($"blitz test: Blitz, facing {rb.rotation.eulerAngles.y:F0} deg, camera {Mathf.Repeat(Mathf.Atan2(cam.x, cam.z) * Mathf.Rad2Deg, 360f):F0} deg");
            activeDodgeField.SetValue(controller, true);
            Vector3 v = rb.transform.forward * 40f;
            for (float t = 0f; t < 0.3f; t += Time.deltaTime)
            {
                rb.velocity = v;
                yield return null;
            }
            activeDodgeField.SetValue(controller, false);
        }

        /// <summary>Test aid: see "-mcFungalTest" in Awake.</summary>
        System.Collections.IEnumerator FungalTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(0.5f);
            Type t = Type.GetType("PlayerAttack, Assembly-CSharp");
            GameObject parent = GameObject.Find("PlayerParent");
            Component attack = parent != null && t != null ? parent.GetComponent(t) : null;
            FieldInfo bonus = t?.GetField("fungalMightBonus", BindingFlags.NonPublic | BindingFlags.Instance);
            if (attack == null || bonus == null) { McLog.Info("fungal test: no PlayerAttack.fungalMightBonus"); yield break; }
            t.GetMethod("ActivateFungalMight")?.Invoke(attack, new object[] { 2f });
            float start = Time.realtimeSinceStartup, last = -1f;
            while (Time.realtimeSinceStartup - start < 15f && attack != null)
            {
                float now = (float)bonus.GetValue(attack);
                if (now != last) McLog.Info($"fungal test: Fungal Might bonus x{now:F2} ({Time.realtimeSinceStartup - start:F2} s after switching it on)");
                last = now;
                yield return null;
            }
        }

        /// <summary>Test aid: see "-mcFlashAll" in Awake.</summary>
        System.Collections.IEnumerator FlashAll()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(4f);
            Type t = Type.GetType("EnemyHealth, Assembly-CSharp");
            if (t == null) { McLog.Info("flash all: no EnemyHealth"); yield break; }
            int n = 0;
            foreach (UnityEngine.Object o in FindObjectsOfType(t)) { combat.Hurt.Flash((Component)o); n++; }
            McLog.Info($"flash all: flashed {n} enemies");
            // The overlay copies (multi-part models such as the crab): on during the flash, off after.
            foreach (float wait in new[] { 0.1f, 0.6f })
            {
                yield return new WaitForSeconds(wait);
                int on = 0, all = 0;
                foreach (Renderer r in FindObjectsOfType<Renderer>(true))
                    if (r.name == "MyceliumCraft hurt tint") { all++; if (r.enabled) on++; }
                McLog.Info($"flash all: {on} of {all} overlay copies drawn, {wait:F1} s later");
            }
        }

        /// <summary>Test aid: see "-mcFlashCheck" in Awake.</summary>
        System.Collections.IEnumerator FlashCheck()
        {
            Component seen = null;
            int checks = 0;
            while (checks < 6)
            {
                yield return null;
                Component enemy = combat.Hurt.LastFlashed;
                if (enemy == null || enemy == seen || Camera.main == null) continue;
                seen = enemy;
                yield return new WaitForSecondsRealtime(0.05f);
                yield return new WaitForEndOfFrame();
                string during = ScreenAround(combat.Hurt.LastBounds);
                yield return new WaitForSecondsRealtime(0.6f); // the flash is 0.5 s
                yield return new WaitForEndOfFrame();
                string after = ScreenAround(combat.Hurt.LastBounds);
                McLog.Info($"flash check: '{enemy.name}' during the flash {during}; after it {after}");
                checks++;
                seen = null; // the next hit on the same enemy counts too
                combat.Hurt.LastFlashed = null;
            }
        }

        /// <summary>Average colour and neighbouring-pixel contrast of the screen inside these world bounds.</summary>
        string ScreenAround(Bounds b)
        {
            Camera cam = Camera.main;
            Vector3 lo = new Vector3(float.MaxValue, float.MaxValue), hi = new Vector3(float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 s = cam.WorldToScreenPoint(corner);
                if (s.z <= 0f) return "(behind the camera)";
                lo = Vector3.Min(lo, s);
                hi = Vector3.Max(hi, s);
            }
            Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
            int w = shot.width, h = shot.height;
            Color32[] px = shot.GetPixels32();
            Destroy(shot);
            int x0 = Mathf.Clamp((int)(lo.x / Screen.width * w), 0, w - 2), x1 = Mathf.Clamp((int)(hi.x / Screen.width * w), x0 + 1, w - 1);
            int y0 = Mathf.Clamp((int)(lo.y / Screen.height * h), 0, h - 1), y1 = Mathf.Clamp((int)(hi.y / Screen.height * h), y0 + 1, h);
            long r = 0, g = 0, bl = 0, k = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++) { Color32 c = px[y * w + x]; r += c.r; g += c.g; bl += c.b; k++; }
            k = Math.Max(1, k);
            double contrast = Sharpness(px, w, x0, x1, y0, y1);
            return $"colour ({r / k},{g / k},{bl / k}), detail {contrast:F2} ({x1 - x0}x{y1 - y0} px)";
        }

        /// <summary>Test aid: see "-mcLoadTest" in Awake.</summary>
        System.Collections.IEnumerator LoadTest(string target)
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSecondsRealtime(4f);
            Type t = Type.GetType("SceneLoader, Assembly-CSharp");
            object loader = t?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            MethodInfo begin = t?.GetMethod("BeginLoadScene", new[] { typeof(string), typeof(bool), typeof(bool) });
            if (loader == null || begin == null) { McLog.Info("load test: SceneLoader not found"); yield break; }
            McLog.Info($"load test: starting Mycelium's scene change to {target} (overlay visible now: {overlay.Visible})");
            begin.Invoke(loader, new object[] { target, true, false });
        }

        /// <summary>Test aid: see "-mcBlurCheck" in Awake.</summary>
        System.Collections.IEnumerator BlurCheck()
        {
            for (int n = 0; n < 60; n++)
            {
                yield return new WaitForSecondsRealtime(0.5f);
                yield return new WaitForEndOfFrame();
                Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
                int w = shot.width, h = shot.height;
                Color32[] px = shot.GetPixels32();
                Destroy(shot);
                // The middle (the world), and the top-left corner (Mycelium's health bar).
                double middle = Sharpness(px, w, w / 4, w * 3 / 4, h / 4, h * 3 / 4);
                double hud = Sharpness(px, w, w / 50, w * 3 / 10, h * 8 / 10, h * 49 / 50);
                // Average colour of the HUD corner: a blur keeps it, a missing HUD would change it.
                long r = 0, g = 0, b = 0, k = 0;
                for (int y = h * 8 / 10; y < h * 49 / 50; y++)
                    for (int x = w / 50; x < w * 3 / 10; x++) { Color32 c = px[y * w + x]; r += c.r; g += c.g; b += c.b; k++; }
                k = System.Math.Max(1, k);
                McLog.Info($"blur check: roughness middle {Roughness(px, w, w / 4, w * 3 / 4, h / 4, h * 3 / 4):F2}, HUD corner {Roughness(px, w, w / 50, w * 3 / 10, h * 8 / 10, h * 49 / 50):F2}");
                McLog.Info($"blur check: sharpness middle {middle:F2}, HUD corner {hud:F2}, HUD corner average colour ({r / k},{g / k},{b / k}) (blur {(pauseBlur.On ? "ON" : "off")}, time scale {Time.timeScale:F2})");
                if (pauseBlur.On)
                {
                    // Same corner with Mycelium's UI hidden for one frame: if it differs, the blurred UI is really drawn.
                    pauseBlur.TestHideUi(true);
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    Texture2D without = ScreenCapture.CaptureScreenshotAsTexture();
                    Color32[] q = without.GetPixels32();
                    Destroy(without);
                    pauseBlur.TestHideUi(false);
                    long r2 = 0, g2 = 0, b2 = 0, k2 = 0;
                    for (int y = h * 8 / 10; y < h * 49 / 50; y++)
                        for (int x = w / 50; x < w * 3 / 10; x++) { Color32 c = q[y * w + x]; r2 += c.r; g2 += c.g; b2 += c.b; k2++; }
                    k2 = System.Math.Max(1, k2);
                    McLog.Info($"blur check:   same corner with Mycelium's UI hidden: average colour ({r2 / k2},{g2 / k2},{b2 / k2})");
                }
            }
        }

        /// <summary>
        /// Roughness: how sharply brightness bends from pixel to pixel (|left - 2 x middle + right|),
        /// in a region. Blocky, stepped pictures score high; a smooth blur scores low.
        /// </summary>
        static double Roughness(Color32[] px, int w, int x0, int x1, int y0, int y1)
        {
            double sum = 0;
            int count = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0 + 1; x < x1 - 1; x++)
                {
                    Color32 a = px[y * w + x - 1], b = px[y * w + x], c = px[y * w + x + 1];
                    sum += Mathf.Abs((a.r + a.g + a.b) - 2 * (b.r + b.g + b.b) + (c.r + c.g + c.b)) / 3.0;
                    count++;
                }
            return sum / Mathf.Max(1, count);
        }

        /// <summary>Average colour difference between horizontally neighbouring pixels in a region (rows from the bottom).</summary>
        static double Sharpness(Color32[] px, int w, int x0, int x1, int y0, int y1)
        {
            double sum = 0;
            int count = 0;
            for (int y = y0; y < y1; y += 2)
                for (int x = x0; x < x1 - 1; x += 2)
                {
                    Color32 a = px[y * w + x], b = px[y * w + x + 1];
                    sum += Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
                    count++;
                }
            return sum / Mathf.Max(1, count);
        }

        /// <summary>Test aid: see "-mcModelCheck" in Awake.</summary>
        System.Collections.IEnumerator ModelCheck()
        {
            McLog.Info($"model check: quality level '{QualitySettings.names[QualitySettings.GetQualityLevel()]}', pipeline '{(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.name : "built-in")}'");
            UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += ProbeDepth;
            // First: the body's shadow on and off in turn, with the screen brightness where the
            // shadow should fall (from the middle of the body, along the sun, down to the feet's height).
            // Find the sun once the level (and the body) is there.
            while (!playerModel.Shown) yield return new WaitForSecondsRealtime(0.2f);
            Light sun = RenderSettings.sun != null && RenderSettings.sun.isActiveAndEnabled ? RenderSettings.sun : null;
            foreach (Light l in FindObjectsOfType<Light>())
            {
                if (l.type != LightType.Directional) continue;
                McLog.Info($"model check: directional light '{l.name}', enabled {l.isActiveAndEnabled}, shadows {l.shadows}, intensity {l.intensity:F2}, pointing {l.transform.forward:F2}");
                if (sun == null && l.isActiveAndEnabled && l.shadows != LightShadows.None) sun = l;
            }
            McLog.Info($"model check: sun {(sun != null ? $"'{sun.name}', shadows {sun.shadows}, pointing {sun.transform.forward:F2}" : "not found")}");
            bool shadowOn = true;
            int shadowChecks = 0;
            while (shadowChecks < 8 && sun != null && sun.transform.forward.y < -0.05f)
            {
                yield return new WaitForSecondsRealtime(0.5f);
                if (!playerModel.Shown || Camera.main == null) continue;
                playerModel.SetCastsShadow(shadowOn);
                yield return null;
                yield return new WaitForEndOfFrame();
                Vector3 feet = playerModel.FeetPoint;
                Vector3 middle = feet + Vector3.up * playerModel.Height * 0.5f;
                Vector3 dir = sun.transform.forward;
                Vector3 ground = middle + dir * ((middle.y - feet.y) / -dir.y);
                Vector3 g = Camera.main.WorldToScreenPoint(ground);
                Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
                bool onScreen = g.z > 0 && g.x >= 0 && g.y >= 0 && g.x < Screen.width && g.y < Screen.height;
                Color32 c = shot.GetPixel(Mathf.Clamp((int)(g.x / Screen.width * shot.width), 0, shot.width - 1),
                                          Mathf.Clamp((int)(g.y / Screen.height * shot.height), 0, shot.height - 1));
                Destroy(shot);
                McLog.Info($"model check: body shadow {(shadowOn ? "ON " : "OFF")}, where it should fall ({ground.x:F2}, {ground.y:F2}, {ground.z:F2}){(onScreen ? "" : " (off screen)")}: " +
                           $"colour ({c.r},{c.g},{c.b}), brightness {(c.r + c.g + c.b) / 3}; spore renderers still drawn: {SporeRenderersDrawn()}");
                shadowOn = !shadowOn;
                shadowChecks++;
            }
            playerModel.SetCastsShadow(true);

            // Then: the depth passes off and on in turn, with the depth the Fog effect reads at the head.
            bool passesOn = true;
            int checks = 0;
            while (checks < 8)
            {
                yield return new WaitForSecondsRealtime(0.5f);
                if (!playerModel.Shown || playerModel.ShadowOnly || Camera.main == null) continue; // first person: not in the depth, by design
                playerModel.SetDepthPasses(passesOn);
                yield return null; // one frame drawn with the new setting
                yield return new WaitForEndOfFrame();
                Vector3 s = Camera.main.WorldToScreenPoint(playerModel.HeadPoint);
                Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
                int px = Mathf.Clamp((int)(s.x / Screen.width * shot.width), 0, shot.width - 1);
                int py = Mathf.Clamp((int)(s.y / Screen.height * shot.height), 0, shot.height - 1);
                Color32 c = shot.GetPixel(px, py);
                int shotW = shot.width, shotH = shot.height;
                Destroy(shot);
                // The depth Mycelium's Fog effect reads at that pixel, on the next frame drawn.
                probeUV = new Vector2(s.x / Screen.width, s.y / Screen.height);
                probeResult = float.NaN;
                probePending = true;
                float waitUntil = Time.realtimeSinceStartup + 1f;
                while (float.IsNaN(probeResult) && Time.realtimeSinceStartup < waitUntil) yield return null;
                McLog.Info($"model check: depth passes {(passesOn ? "ON " : "OFF")}, head at screen ({px}, {py}) of {shotW}x{shotH}, really {s.z:F2} units away; " +
                           $"the depth texture says {(float.IsNaN(probeResult) ? "nothing (probe failed)" : probeResult.ToString("F2"))}; colour ({c.r},{c.g},{c.b})");
                passesOn = !passesOn;
                checks++;
            }
            playerModel.SetDepthPasses(true);
            UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= ProbeDepth;
        }

        /// <summary>Test aid: how many of the spore's renderers are drawn at all (0 while your Minecraft body is shown).</summary>
        int SporeRenderersDrawn()
        {
            if (rb == null) return 0;
            int n = 0;
            foreach (Renderer r in rb.GetComponentsInChildren<Renderer>())
                if (r.enabled && !r.forceRenderingOff) n++;
            return n;
        }

        Vector2 probeUV;
        bool probePending;
        float probeResult = float.NaN;
        Material probeMaterial;
        RenderTexture probeTarget;

        /// <summary>Test aid: after the main camera draws, read the depth texture at probeUV (MyceliumCraftDepthProbe shader).</summary>
        void ProbeDepth(UnityEngine.Rendering.ScriptableRenderContext context, Camera cam)
        {
            if (!probePending || cam == null || !cam.CompareTag("MainCamera")) return;
            probePending = false;
            if (probeMaterial == null)
            {
                Shader probe = McShaders.Load("MyceliumCraftDepthProbe");
                if (probe == null) { McLog.Info("model check: depth probe shader missing"); return; }
                probeMaterial = new Material(probe);
                probeTarget = new RenderTexture(1, 1, 0, RenderTextureFormat.ARGBFloat);
            }
            probeMaterial.SetVector("_ProbeUV", new Vector4(probeUV.x, probeUV.y, 0, 0));
            Graphics.Blit(null, probeTarget, probeMaterial);
            UnityEngine.Rendering.AsyncGPUReadback.Request(probeTarget, 0, TextureFormat.RGBAFloat, r =>
            {
                if (r.hasError) { McLog.Info("model check: depth probe readback failed"); return; }
                probeResult = r.GetData<Color>()[0].r;
            });
        }

        /// <summary>Test aid: see "-mcOverlayCheck" in Awake.</summary>
        System.Collections.IEnumerator OverlayCheck()
        {
            // Spots in the test picture (640x360, from the top left): red box, half-transparent
            // white box, and an empty spot.
            var spots = new (string name, float x, float y)[] { ("red box", 120, 80), ("half white box", 520, 80), ("empty", 320, 250) };
            int checks = 0;
            while (checks < 12)
            {
                yield return new WaitForSecondsRealtime(1f);
                if (overlay.Width == 0) continue;
                yield return new WaitForEndOfFrame();
                bool shown = overlay.Visible;
                Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
                var parts = new System.Collections.Generic.List<string>();
                foreach (var spot in spots)
                {
                    int px = Mathf.Clamp((int)(spot.x / 640f * shot.width), 0, shot.width - 1);
                    int py = Mathf.Clamp((int)((1f - spot.y / 360f) * shot.height), 0, shot.height - 1);
                    Color32 c = shot.GetPixel(px, py);
                    parts.Add($"{spot.name}=({c.r},{c.g},{c.b})");
                }
                McLog.Info($"overlay check: overlay {(shown ? "SHOWN" : "hidden")}, screen {shot.width}x{shot.height}: {string.Join(", ", parts)}");
                Destroy(shot);
                overlay.ForceHidden = !overlay.ForceHidden;
                checks++;
            }
            overlay.ForceHidden = false;
        }

        /// <summary>Test aid: an enemy-style knockback from 1 unit in front of the spore.</summary>
        void RunHitTest()
        {
            nextHitTime = Time.realtimeSinceStartup + 4f;
            if (fakeHitter == null) fakeHitter = new GameObject("MyceliumCraft fake hitter");
            fakeHitter.transform.position = rb.position + rb.transform.forward * 1f;
            MethodInfo knock = playerControllerType.GetMethod("Knockback", BindingFlags.Public | BindingFlags.Instance);
            try
            {
                knock.Invoke(controller, new object[] { fakeHitter, 60f, false });
                McLog.Info($"hit test: Knockback called (flash frames so far {cameraSync.FlashFrames} of {cameraSync.FramesChecked} checked)");
            }
            catch (Exception e)
            {
                McLog.Info("hit test: Knockback failed: " + (e.InnerException?.Message ?? e.Message));
            }
        }

        /// <summary>For the log: the solid thing your spore runs into going this way.</summary>
        string BlockedBy(Vector3 way)
        {
            if (!rb.SweepTest(way.normalized, out RaycastHit hit, 1f, QueryTriggerInteraction.Ignore))
            {
                // Already touching doesn't show in a sweep: name what's close instead.
                var near = new System.Collections.Generic.List<string>();
                foreach (Collider c in Physics.OverlapSphere(rb.position, 1.5f, ~0, QueryTriggerInteraction.Ignore))
                    if (!c.transform.IsChildOf(rb.transform))
                        near.Add($"'{c.name}' {c.GetType().Name} (under '{c.transform.root.name}', layer {LayerMask.LayerToName(c.gameObject.layer)}, closest point {c.ClosestPoint(rb.position):F2})");
                return $"no sweep hit; rb kinematic={rb.isKinematic} constraints={rb.constraints} velocity={rb.velocity:F2}; solid within 1.5: {string.Join("; ", near)}";
            }
            Transform top = hit.collider.transform.root;
            return $"'{hit.collider.name}' {hit.collider.GetType().Name} (under '{top.name}', layer {LayerMask.LayerToName(hit.collider.gameObject.layer)}, tag {hit.collider.tag}) {hit.distance:F2} units ahead at {hit.point:F2}";
        }

        /// <summary>Test aid (-mcBumpTest): once you're walking, stand another spore 2 units ahead of you for 3 s, and log how far your spore falls behind and how far that spore went. Solid: it's held there.</summary>
        System.Collections.IEnumerator BumpTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(1f);
            while (new Vector3(mcVel.x, 0f, mcVel.z).magnitude < 1f) yield return new WaitForFixedUpdate();
            GameObject other = null;
            for (int i = 0; i < colony.Count && other == null; i++)
                if (colony.Spores[i] != null && colony.Spores[i] != rb.gameObject) other = colony.Spores[i];
            if (other == null) { McLog.Info("bump test: no other spore"); yield break; }
            Vector3 spot = rb.position + new Vector3(mcVel.x, 0f, mcVel.z).normalized * 2f;
            Rigidbody body = other.GetComponent<Rigidbody>();
            bool held = passThrough.Off;
            if (body != null) body.isKinematic = held;
            var wander = other.GetComponent(Type.GetType("WanderingSpore, Assembly-CSharp")) as Behaviour;
            if (wander != null) wander.enabled = false; // only your push moves it
            other.transform.position = spot;
            McLog.Info($"bump test: {other.name} stands at {spot:F2}, in the way of your spore at {rb.position:F2}");
            float end = Time.time + 3f, worst = 0f;
            int resyncsBefore = resyncs;
            while (Time.time < end)
            {
                if (held) other.transform.position = spot;
                if (lastError > 1f && worst <= 1f) McLog.Info($"bump test: falling behind at {rb.position:F2}, {other.name} at {other.transform.position:F2}; running into {BlockedBy(lastTarget - rb.position)}");
                worst = Mathf.Max(worst, lastError);
                yield return new WaitForFixedUpdate();
            }
            Vector3 went = other.transform.position - spot;
            went.y = 0f;
            McLog.Info($"bump test: done, your spore fell at most {worst:F2} units behind, {resyncs - resyncsBefore} re-sync teleport(s); {other.name} went {went.magnitude:F2} units");
            if (body != null) body.isKinematic = false;
            if (wander != null) wander.enabled = true;
        }

        /// <summary>Test aid (-mcSpearTest): the held weapon goes by the spear's name; 1.5 s after a shot, says where projectiles are.</summary>
        System.Collections.IEnumerator SpearTest()
        {
            while (phase != Phase.Driving) yield return null;
            GameObject weapon = GameObject.FindWithTag("currentWeapon");
            if (weapon == null) { McLog.Info("spear test: no weapon"); yield break; }
            weapon.name = "Stickbug Spear";
            McLog.Info("spear test: the held weapon is called Stickbug Spear now");
            for (int i = 0; i < 12; i++)
            {
                yield return new WaitForSeconds(1f);
                foreach (GameObject shot in GameObject.FindGameObjectsWithTag("ReversedProjectile"))
                    McLog.Info($"spear test: projectile at {shot.transform.position:F2}, velocity {shot.GetComponent<Rigidbody>()?.velocity:F2}");
            }
        }

        /// <summary>Test aid (-mcBossBarTest): HUDBoss.EncounterBoss, then UpdateBossHealthUI twice.</summary>
        System.Collections.IEnumerator BossBarTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(3f);
            Type t = Type.GetType("HUDBoss, Assembly-CSharp");
            Component hudBoss = t != null ? GameObject.Find("HUD")?.GetComponent(t) : null;
            if (hudBoss == null) { McLog.Info("boss bar test: no HUDBoss"); yield break; }
            McLog.Info("boss bar test: meeting 'Rival Sporemother' (1000/1000)");
            t.GetMethod("EncounterBoss", new[] { typeof(string), typeof(float), typeof(float) }).Invoke(hudBoss, new object[] { "Rival Sporemother", 1000f, 1000f });
            yield return new WaitForSeconds(2f);
            McLog.Info("boss bar test: the fight begins (the boss's attacks on, as the intro's end does)");
            var attacks = GameObject.Find("Rival Sporemother")?.GetComponent(Type.GetType("MonsterBossAttack, Assembly-CSharp")) as Behaviour;
            if (attacks != null) attacks.enabled = true;
            yield return new WaitForSeconds(1f);
            McLog.Info("boss bar test: 600/1000");
            t.GetMethod("UpdateBossHealthUI").Invoke(hudBoss, new object[] { 600f, 1000f });
            yield return new WaitForSeconds(2f);
            McLog.Info("boss bar test: beaten (0/1000)");
            t.GetMethod("UpdateBossHealthUI").Invoke(hudBoss, new object[] { 0f, 1000f });
        }

        /// <summary>Test aid (-mcBossPullTest): the final boss's attacks on, then where the spore and the boss are every second.</summary>
        System.Collections.IEnumerator BossPullTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(3f);
            GameObject boss = GameObject.Find("Rival Sporemother");
            var attacks = boss != null ? boss.GetComponent(Type.GetType("MonsterBossAttack, Assembly-CSharp")) as Behaviour : null;
            if (attacks == null) { McLog.Info("boss pull test: no Rival Sporemother with MonsterBossAttack"); yield break; }
            attacks.enabled = true;
            McLog.Info($"boss pull test: the boss's attacks on; you're {Vector3.Distance(rb.position, boss.transform.position):F1} units from it");
            for (int i = 1; i <= 14; i++)
            {
                yield return new WaitForSeconds(1f);
                pull.ForMinecraft(map, out float x, out float z);
                object paused = Type.GetType("GlobalData, Assembly-CSharp")?.GetField("isGamePaused")?.GetValue(null);
                var target = attacks.GetType().GetField("player", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(attacks) as Transform;
                McLog.Info($"boss pull test: +{i} s: {Vector3.Distance(rb.position, boss.transform.position):F1} units from the boss; pull for Minecraft {Mathf.Sqrt(x * x + z * z):F2} blocks/s; " +
                    $"pull particles {(GameObject.Find("BossSandPullParticles(Clone)") != null)}, paused {paused}, boss pulls '{(target != null ? target.name : "nobody")}' (you: '{rb.name}'), attacks on {attacks.isActiveAndEnabled}, timeScale {Time.timeScale}; " +
                    $"phase {phase}, controls '{controlsWhy}', held by Mycelium {heldByMycelium}, spore at {rb.position:F1} velocity {rb.velocity:F1} kinematic {rb.isKinematic}, boss at {boss.transform.position:F1}");
            }
        }

        /// <summary>Test aid (-mcBossPullNative): Mycelium's own pull on a spore that stands still, with nobody driving.</summary>
        System.Collections.IEnumerator BossPullNativeTest()
        {
            GameObject boss = null;
            Rigidbody spore = null;
            while (boss == null || spore == null)
            {
                yield return new WaitForSeconds(0.5f);
                boss = GameObject.Find("Rival Sporemother");
                Type pc = Type.GetType("PlayerController, Assembly-CSharp");
                var c = pc != null ? UnityEngine.Object.FindObjectOfType(pc) as Component : null;
                spore = c != null ? c.GetComponent<Rigidbody>() ?? c.GetComponentInChildren<Rigidbody>() : null;
                if (Time.frameCount % 5 == 0) McLog.Info($"boss pull native: waiting (boss {(boss != null)}, PlayerController type {(pc != null)}, found {(c != null)}, rigidbody {(spore != null)})");
            }
            yield return new WaitForSeconds(3f);
            var attacks = boss.GetComponent(Type.GetType("MonsterBossAttack, Assembly-CSharp")) as Behaviour;
            if (attacks == null) { McLog.Info("boss pull native: no MonsterBossAttack"); yield break; }
            attacks.enabled = true;
            McLog.Info($"boss pull native: the boss's attacks on, nobody driving; spore {Vector3.Distance(spore.position, boss.transform.position):F1} units from the boss, fixed step {Time.fixedDeltaTime:F3} s");
            Vector3 last = spore.position;
            int lastFrame = Time.frameCount;
            for (int i = 1; i <= 16; i++)
            {
                yield return new WaitForSeconds(1f);
                Vector3 d = spore.position - last;
                d.y = 0f;
                McLog.Info($"boss pull native: +{i} s: spore moved {d.magnitude:F2} units ({d.magnitude / map.Scale:F2} blocks) in the last second, {Vector3.Distance(spore.position, boss.transform.position):F1} units from the boss, " +
                    $"{Time.frameCount - lastFrame} frames, pull particles {(GameObject.Find("BossSandPullParticles(Clone)") != null)}, velocity {spore.velocity:F2}");
                last = spore.position;
                lastFrame = Time.frameCount;
            }
        }

        /// <summary>Test aid (-mcBossDeathTest): BossHealth.EnemyTakeDamage with more than its health, then the hold and overlay every second.</summary>
        System.Collections.IEnumerator BossDeathTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(4f);
            GameObject boss = GameObject.Find("Rival Sporemother");
            Type t = Type.GetType("BossHealth, Assembly-CSharp");
            Component health = boss != null && t != null ? boss.GetComponent(t) : null;
            if (health == null) { McLog.Info("boss death test: no Rival Sporemother"); yield break; }
            McLog.Info("boss death test: beating Rival Sporemother");
            t.GetMethod("EnemyTakeDamage").Invoke(health, new object[] { 1e7f, false, true });
            for (int i = 0; i < 10; i++)
            {
                yield return new WaitForSeconds(1f);
                var brain = Camera.main != null ? Camera.main.GetComponent<Cinemachine.CinemachineBrain>() : null;
                int off = 0, all = 0;
                if (rb != null) foreach (Renderer r in rb.GetComponentsInChildren<Renderer>()) { all++; if (r.forceRenderingOff) off++; }
                McLog.Info($"boss death test: +{i + 1} s: Mycelium has the controls {heldByMycelium} ({controlsWhy}), overlay shown {overlay.Visible}, live camera '{brain?.ActiveVirtualCamera?.Name}', spore renderers not drawn {off}/{all}, boss animator in Death {boss.GetComponent<Animator>()?.GetCurrentAnimatorStateInfo(0).IsName("Death")}");
            }
        }

        /// <summary>Test aid (-mcPopUpTest): TooltipManager.CreateTooltip for a made-up thing, Destroy it, then whether the pop-up is there.</summary>
        System.Collections.IEnumerator PopUpTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(2f);
            Type tm = Type.GetType("TooltipManager, Assembly-CSharp");
            object manager = tm?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            MethodInfo create = null;
            if (tm != null)
                foreach (MethodInfo m in tm.GetMethods())
                    if (m.Name == "CreateTooltip" && m.GetParameters().Length >= 4 && m.GetParameters()[0].ParameterType == typeof(GameObject)) create = m;
            if (manager == null || create == null) { McLog.Info("pop-up test: no TooltipManager"); yield break; }
            var thing = new GameObject("pop-up test thing");
            thing.transform.position = rb.position + rb.transform.forward * 2f;
            ParameterInfo[] ps = create.GetParameters();
            var callArgs = new object[ps.Length];
            object[] given = { thing, "Test", "test pop-up", "" };
            for (int i = 0; i < ps.Length; i++) callArgs[i] = i < given.Length ? given[i] : ps[i].DefaultValue;
            create.Invoke(manager, callArgs);
            McLog.Info($"pop-up test: made, there: {tm.GetField("currentTooltip")?.GetValue(manager) as GameObject != null}");
            Destroy(thing);
            yield return new WaitForSeconds(0.5f);
            McLog.Info($"pop-up test: 0.5 s after its thing was destroyed, there: {tm.GetField("currentTooltip")?.GetValue(manager) as GameObject != null}");
        }

        /// <summary>Test aid (-mcBossIntroTest): BossHealth.OnTriggerEnter with your spore's collider, then the intro camera and boss bar every 0.5 s.</summary>
        System.Collections.IEnumerator BossIntroTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(3f);
            GameObject boss = GameObject.Find("Rival Sporemother");
            Type t = Type.GetType("BossHealth, Assembly-CSharp");
            Component health = boss != null && t != null ? boss.GetComponent(t) : null;
            Collider mine = rb != null ? rb.GetComponent<Collider>() : null;
            if (health == null || mine == null) { McLog.Info("boss intro test: no boss or no collider"); yield break; }
            McLog.Info("boss intro test: into the boss's trigger");
            t.GetMethod("OnTriggerEnter", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(health, new object[] { mine });
            Type bc = Type.GetType("BossCam, Assembly-CSharp");
            for (int i = 0; i < 40; i++)
            {
                yield return new WaitForSeconds(0.5f);
                var cam = bc?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as Component;
                var intro = cam != null ? bc.GetField("introVCam", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(cam) as Behaviour : null;
                McLog.Info($"boss intro test: +{(i + 1) * 0.5f:F1} s: intro camera {(intro != null && intro.enabled ? "on" : "off")}, Mycelium has the controls {heldByMycelium} ({controlsWhy})");
            }
        }

        /// <summary>Test aid (-mcGrowTest): the shop's SpawnNewCharacter, then the Sprout on the new spore is sent (ColonyWatch).</summary>
        System.Collections.IEnumerator GrowTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(4f);
            GameObject parent = GameObject.FindWithTag("PlayerParent");
            Type t = Type.GetType("SpawnCharacter, Assembly-CSharp");
            Component spawn = parent != null && t != null ? parent.GetComponent(t) : null;
            if (spawn == null) { McLog.Info("grow test: no SpawnCharacter"); yield break; }
            McLog.Info($"grow test: growing a spore ({colony.Count} in the colony)");
            t.GetMethod("SpawnNewCharacter").Invoke(spawn, new object[] { "Default", null, null, true, true });
            SendTeleport("grow test: as the shop closing does"); // the cutscene must hold through this
        }

        /// <summary>Test aid (-mcSporeSwapTest): swap to the next spore; no spore should be drawn after it, each is a Minecraft body.</summary>
        System.Collections.IEnumerator SporeSwapTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(4f);
            GameObject parent = GameObject.FindWithTag("PlayerParent");
            Type t = Type.GetType("SwapCharacter, Assembly-CSharp");
            Component swap = parent != null && t != null ? parent.GetComponent(t) : null;
            if (swap == null) { McLog.Info("spore swap test: no SwapCharacter"); yield break; }
            for (int round = 0; round < 2; round++)
            {
                McLog.Info($"spore swap test: swap {round + 1}, from '{GameObject.FindWithTag("currentPlayer")?.name}' ({colony.Names[Mathf.Max(0, colony.Current)]})");
                t.GetMethod("SwitchToNextCharacter").Invoke(swap, null);
                yield return new WaitForSeconds(1f);
                for (int i = 0; i < colony.Count; i++)
                {
                    int drawn = 0, popUp = 0;
                    foreach (Renderer r in colony.Spores[i].GetComponentsInChildren<Renderer>(true))
                    {
                        if (r.forceRenderingOff) continue;
                        if (r.GetComponentInParent<Canvas>() != null) popUp++; // a pop-up's happiness faces: drawn on purpose
                        else drawn++;
                    }
                    McLog.Info($"spore swap test: {colony.Names[i]}{(i == colony.Current ? " (you)" : "")}: {drawn} spore renderers drawn (want 0){(popUp > 0 ? $", {popUp} pop-up parts" : "")}");
                }
            }
            // The popup on another spore: its happiness faces are sprites under that spore.
            int other = colony.Current == 0 ? 1 : 0;
            GameObject looked = colony.Spores[other];
            Type popup = Type.GetType("SporeInteraction, Assembly-CSharp");
            Component interaction = popup != null ? looked.GetComponent(popup) : null;
            if (interaction == null) { McLog.Info("spore swap test: no SporeInteraction"); yield break; }
            popup.GetMethod("CreateTooltip").Invoke(interaction, new object[] { GameObject.FindWithTag("currentPlayer") });
            yield return new WaitForSeconds(1f);
            int faces = 0, facesDrawn = 0;
            foreach (SpriteRenderer s in looked.GetComponentsInChildren<SpriteRenderer>(false))
            {
                faces++;
                if (!s.forceRenderingOff) facesDrawn++;
            }
            McLog.Info($"spore swap test: popup on {colony.Names[other]}: {facesDrawn} of {faces} happiness faces drawn (want all)");
        }

        /// <summary>Test aid (-mcSwapTest): a weapon swap like a pickup, then which weapon is drawn.</summary>
        System.Collections.IEnumerator SwapTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(4f);
            GameObject old = GameObject.FindWithTag("currentWeapon");
            GameObject slot = GameObject.FindWithTag("WeaponSlot");
            if (old == null || slot == null)
            {
                McLog.Info($"swap test: no weapon ({old != null}) or weapon slot ({slot != null}); nothing to swap");
                yield break;
            }
            McLog.Info($"swap test: before: '{old.name}' drawn={Drawn(old)}, Minecraft body shown={playerModel.Shown}");
            GameObject fresh = Instantiate(old, slot.transform);
            fresh.name = old.name + " (picked up)";
            old.transform.parent = null; // let go into the world, as WeaponInteraction does
            old.tag = "Weapon";
            old.transform.position += Vector3.up * 0.5f;
            fresh.tag = "currentWeapon";
            yield return new WaitForSeconds(0.5f);
            McLog.Info($"swap test: after: dropped '{old.name}' drawn={Drawn(old)} (want True), held '{fresh.name}' drawn={Drawn(fresh)} (want {!playerModel.Shown})");
        }

        /// <summary>Test aid (-mcSkillMenuTest): LevelUpManagerNew's SkillMenu switched on, as its menu swap does, then off.</summary>
        System.Collections.IEnumerator SkillMenuTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(4f);
            Component shop = null;
            for (int i = 0; i < 10 && (shop = shopUndo.Manager) == null; i++) yield return new WaitForSeconds(0.5f);
            var skillMenu = shop != null ? shop.GetType().GetField("SkillMenu").GetValue(shop) as GameObject : null;
            if (skillMenu == null) { McLog.Info("skill menu test: no skill menu"); yield break; }
            McLog.Info($"skill menu test: opening '{skillMenu.name}'");
            skillMenu.SetActive(true);
            for (int i = 1; i <= 6; i++)
            {
                yield return new WaitForSeconds(0.5f);
                McLog.Info($"skill menu test: +{i * 0.5f:F1} s: your body shown there {menuBody.Showing}");
            }
            // Swap spores as the menu's arrow buttons do (after this frame's Update, as a click is).
            Component menu = menuBody.Menu;
            for (int i = 0; i < 2 && menu != null; i++)
            {
                int caught = menuBody.CaughtBeforeDrawing;
                McLog.Info($"skill menu test: swapping spores ({(i == 0 ? "SwapNext" : "SwapLast")}) at frame {Time.frameCount}");
                menu.GetType().GetMethod(i == 0 ? "SwapNext" : "SwapLast").Invoke(menu, null);
                yield return new WaitForSeconds(1f);
                McLog.Info($"skill menu test: after the swap: new copy hidden before drawing {menuBody.CaughtBeforeDrawing - caught} time(s), your body shown there {menuBody.Showing}");
            }
            skillMenu.SetActive(false);
            yield return new WaitForSeconds(0.5f);
            McLog.Info($"skill menu test: closed; your body shown there {menuBody.Showing}");
        }

        /// <summary>Test aid (-mcShopTest): LevelUpManagerNew's VitalityUP, VitalityDown, Close and Commit, as its buttons call them.</summary>
        System.Collections.IEnumerator ShopTest()
        {
            while (phase != Phase.Driving || playerHealth == null) yield return null;
            yield return new WaitForSeconds(4f);
            Component shop = null;
            for (int i = 0; i < 10 && (shop = shopUndo.Manager) == null; i++) yield return new WaitForSeconds(0.5f);
            if (shop == null) { McLog.Info("shop test: no level-up shop"); yield break; }
            Type st = shop.GetType();
            var menu = st.GetField("UIenable").GetValue(shop) as GameObject;
            Component tracker = GameObject.FindWithTag("Tracker")?.GetComponent(Type.GetType("NutrientTracker, Assembly-CSharp"));
            FieldInfo nutrientsField = tracker?.GetType().GetField("currentNutrients");
            if (nutrientsField == null) { McLog.Info("shop test: no NutrientTracker.currentNutrients"); yield break; }
            int nutrientsBefore = (int)nutrientsField.GetValue(tracker);
            nutrientsField.SetValue(tracker, nutrientsBefore + 100000);
            float Max() => (float)maxHealthField.GetValue(playerHealth);
            string Now() => $"max health {Max():F1}, current {(float)currentHealthField.GetValue(playerHealth):F1}";
            void Call(string m) { McLog.Info($"shop test: {m}"); st.GetMethod(m).Invoke(shop, null); }
            string[] rounds = { "up, close", "up, down, commit" };
            foreach (string round in rounds)
            {
                McLog.Info($"shop test: {round}: before, {Now()}");
                menu.SetActive(true);
                if (!shop.gameObject.activeInHierarchy) shop.gameObject.SetActive(true);
                yield return new WaitForSeconds(1f);
                Call("VitalityUP");
                yield return new WaitForSeconds(0.5f);
                McLog.Info($"shop test: {round}: Vitality up, {Now()}");
                if (round.Contains("down")) { Call("VitalityDown"); yield return new WaitForSeconds(0.5f); }
                Call(round.EndsWith("close") ? "Close" : "Commit");
                yield return new WaitForSeconds(1f);
                McLog.Info($"shop test: {round}: after, {Now()}");
            }
            nutrientsField.SetValue(tracker, nutrientsBefore);
            McLog.Info($"shop test: nutrients put back to {nutrientsBefore}");
        }

        /// <summary>Test aid (-mcHealthTest): max health changes with current health at half.</summary>
        System.Collections.IEnumerator HealthTest()
        {
            while (phase != Phase.Driving || playerHealth == null) yield return null;
            yield return new WaitForSeconds(4f);
            if (SceneManager.GetActiveScene().name == "The Carcass") { McLog.Info("health test: not in The Carcass"); yield break; }
            GameObject player = GameObject.FindWithTag("currentPlayer");
            Type ct = Type.GetType("CharacterStats, Assembly-CSharp");
            Component stats = player != null && ct != null ? player.GetComponent(ct) : null;
            MethodInfo add = ct?.GetMethod("AddStat", BindingFlags.Public | BindingFlags.Instance);
            if (stats == null || add == null) { McLog.Info("health test: no CharacterStats.AddStat"); yield break; }
            float max = (float)maxHealthField.GetValue(playerHealth);
            currentHealthField.SetValue(playerHealth, max * 0.5f);
            McLog.Info($"health test: current set to half of {max:F1}");
            yield return new WaitForSeconds(1.5f);
            McLog.Info("health test: Vitality +5");
            add.Invoke(stats, new object[] { "Vitality", 5 });
            yield return new WaitForSeconds(1.5f);
            McLog.Info($"health test: now {(float)currentHealthField.GetValue(playerHealth):F1} of {(float)maxHealthField.GetValue(playerHealth):F1}; Vitality -5");
            add.Invoke(stats, new object[] { "Vitality", -5 });
            yield return new WaitForSeconds(1.5f);
            McLog.Info($"health test: now {(float)currentHealthField.GetValue(playerHealth):F1} of {(float)maxHealthField.GetValue(playerHealth):F1}");
        }

        /// <summary>Test aid (-mcAttribute): give the held weapon an attribute.</summary>
        System.Collections.IEnumerator AttributeTest(string name)
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(1f);
            GameObject weapon = GameObject.FindWithTag("currentWeapon");
            Type at = Type.GetType("AttributeAssigner, Assembly-CSharp");
            object assigner = at?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            MethodInfo pick = at?.GetMethod("PickAttFromString");
            if (weapon == null || assigner == null || pick == null) { McLog.Info("attribute test: no weapon or AttributeAssigner"); yield break; }
            var att = pick.Invoke(assigner, new object[] { weapon, name }) as Component;
            if (att == null) { McLog.Info($"attribute test: '{name}' not found"); yield break; }
            yield return null;
            att.GetType().GetMethod("Equipped")?.Invoke(att, null);
            McLog.Info($"attribute test: '{weapon.name}' has '{name}' now");
            // What the attribute put on the spore and on enemies afterwards (effects are components).
            GameObject player = GameObject.FindWithTag("currentPlayer");
            string lastSeen = null;
            for (int i = 0; i < 40; i++)
            {
                yield return new WaitForSeconds(0.25f);
                var onPlayer = new System.Collections.Generic.List<string>();
                if (player != null)
                    foreach (string effect in new[] { "SpeedChange", "Poison", "Root", "Vulnerable" })
                    {
                        Type et = Type.GetType(effect + ", Assembly-CSharp");
                        if (et != null && player.GetComponent(et) != null) onPlayer.Add(effect);
                    }
                int poisoned = 0;
                Type pt = Type.GetType("Poison, Assembly-CSharp");
                if (pt != null) poisoned = FindObjectsOfType(pt).Length;
                string seen = $"spore effects [{string.Join(", ", onPlayer)}], Poison effects in the level {poisoned}";
                if (seen != lastSeen) McLog.Info("attribute test: " + seen);
                lastSeen = seen;
            }
        }

        /// <summary>Test aid (-mcSwingTest): how long each attack animation really runs to 90 %.</summary>
        System.Collections.IEnumerator SwingTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(4f);
            GameObject player = GameObject.FindWithTag("currentPlayer");
            Animator a = player != null ? player.GetComponent<Animator>() : null;
            if (a == null) { McLog.Info("swing test: no animator"); yield break; }
            float speed = a.speed;
            foreach (string state in new[] { "Slash", "Smash", "Stab" })
            {
                a.speed = speed;
                a.Play(state);
                yield return null;
                float start = Time.time;
                AnimatorStateInfo s = a.GetCurrentAnimatorStateInfo(0);
                float stateLength = s.length;
                while (Time.time - start < 10f)
                {
                    s = a.GetCurrentAnimatorStateInfo(0);
                    if (!s.IsName(state) || s.normalizedTime >= 0.9f) break;
                    yield return null;
                }
                McLog.Info($"swing test: '{state}' at animator speed {speed:F2}: reached 90 % after {Time.time - start:F2} s (state length {stateLength:F2} s, still in it: {s.IsName(state)})");
                a.Rebind();
                a.speed = speed;
                yield return new WaitForSeconds(0.5f);
            }
        }

        /// <summary>Test aid (-mcInteractWatch): log changes of SporeInteractableFinder.closestInteractableObject.</summary>
        System.Collections.IEnumerator InteractWatch()
        {
            GameObject last = null;
            bool first = true;
            while (true)
            {
                yield return new WaitForSeconds(0.05f);
                GameObject parent = GameObject.Find("PlayerParent");
                Type ft = Type.GetType("SporeInteractableFinder, Assembly-CSharp");
                Component finder = null;
                if (parent != null && ft != null) finder = parent.GetComponentInChildren(ft);
                if (finder == null && ft != null) finder = FindObjectOfType(ft) as Component;
                var now = finder != null ? ft.GetField("closestInteractableObject")?.GetValue(finder) as GameObject : null;
                if (!first && ReferenceEquals(now, last)) continue;
                first = false;
                last = now;
                McLog.Info(now != null ? $"interact watch: closest interactable is '{now.name}' at {now.transform.position:F1}" : "interact watch: nothing interactable near");
            }
        }

        /// <summary>Test aid (-mcRepickTest): pick up the nearest weapon lying in the world.</summary>
        System.Collections.IEnumerator RepickTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(8f);
            GameObject nearest = null;
            float best = 20f;
            foreach (GameObject o in GameObject.FindGameObjectsWithTag("Weapon"))
            {
                float d = rb != null ? Vector3.Distance(o.transform.position, rb.position) : float.MaxValue;
                if (d < best) { best = d; nearest = o; }
            }
            Type t = Type.GetType("WeaponInteraction, Assembly-CSharp");
            Component interact = nearest != null && t != null ? nearest.GetComponent(t) : null;
            if (interact == null) { McLog.Info("repick test: no weapon lying within 20 units"); yield break; }
            Collider col = nearest.GetComponent<Collider>();
            McLog.Info($"repick test: picking up '{nearest.name}' {best:F1} units away (layer {LayerMask.LayerToName(nearest.layer)}, collider {(col != null && col.enabled ? "on" : "off")})");
            try { t.GetMethod("Interact", BindingFlags.Public | BindingFlags.Instance).Invoke(interact, new object[] { nearest }); }
            catch (Exception e) { McLog.Info("repick test: Interact failed: " + (e.InnerException ?? e).Message); }
        }

        /// <summary>
        /// Test aid (-mcReachTest): each weapon prefab is built in the spore's hand as WeaponBag.Equip
        /// and WeaponInteraction.ApplyWeaponPositionAndRotation place it, measured (WeaponBag.MeasureOn),
        /// and destroyed, all in one frame. The held weapon stays as it was.
        /// </summary>
        System.Collections.IEnumerator ReachTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(3f);
            Type swapType = Type.GetType("SwapWeapon, Assembly-CSharp");
            GameObject parent = GameObject.Find("PlayerParent");
            Component swap = swapType != null && parent != null ? parent.GetComponent(swapType) : null;
            Transform holder = swap != null ? swapType.GetField("weaponHolder")?.GetValue(swap) as Transform : null;
            if (holder == null) holder = GameObject.FindWithTag("WeaponSlot")?.transform; // as Equip builds weapons
            McLog.Info($"reach test: weapon holder '{(holder != null ? holder.name : "none")}' ({(swap != null ? "SwapWeapon found" : "no SwapWeapon")})");
            Type statsType = Type.GetType("WeaponStats, Assembly-CSharp");
            if (holder == null || statsType == null) { McLog.Info("reach test: no weapon holder or WeaponStats"); yield break; }
            Vector3 holderAngles = holder.localEulerAngles;
            var summary = new System.Collections.Generic.List<string>();
            string[] folders = { "Slash", "Smash", "Stab" };
            for (int type = 0; type < 3; type++)
            {
                foreach (GameObject prefab in Resources.LoadAll<GameObject>(folders[type]))
                {
                    var o = Instantiate(prefab, holder) as GameObject;
                    if (prefab.name != "Stick") o.transform.localScale = o.transform.localScale / 0.03f / 100f / 1.2563f; // as Equip
                    Collider col = o.GetComponent<Collider>();
                    if (col != null) col.enabled = false;
                    Component stats = o.GetComponent(statsType);
                    o.transform.rotation = holder.rotation;
                    o.transform.localPosition = stats != null && statsType.GetField("holdPositionOffset")?.GetValue(stats) is Vector3 p ? p : Vector3.zero;
                    holder.localEulerAngles = stats != null && statsType.GetField("holdRotationOffset")?.GetValue(stats) is Vector3 r ? r : holderAngles;
                    bool? ok = WeaponBag.MeasureOn(o, type, prefab.name, map, out float reach);
                    summary.Add($"{prefab.name} {(ok == true ? reach.ToString("F2") : "?")}");
                    holder.localEulerAngles = holderAngles;
                    DestroyImmediate(o);
                }
                McLog.Info($"reach test: {folders[type]}: {string.Join(", ", summary)} blocks");
                summary.Clear();
            }
        }

        /// <summary>Test aid (-mcFuryTest): Relentless Fury's effect switched on as its skill does, then watched.</summary>
        System.Collections.IEnumerator FuryTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(4f);
            Type ft = Type.GetType("Fury, Assembly-CSharp");
            Type ht = Type.GetType("PlayerHealth, Assembly-CSharp");
            Type et = Type.GetType("EnemyHealth, Assembly-CSharp");
            var health = ht != null ? FindObjectOfType(ht) as Component : null;
            if (ft == null || health == null) { McLog.Info($"fury test: no Fury ({ft != null}) or PlayerHealth ({health != null})"); yield break; }
            const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            string Held()
            {
                GameObject w = GameObject.FindWithTag("currentWeapon");
                Component stats = null;
                if (w != null) foreach (Component c in w.GetComponents<Component>()) if (c != null && c.GetType().Name == "WeaponStats") stats = c;
                object nums = stats?.GetType().GetField("statNums")?.GetValue(stats);
                object adv = nums?.GetType().GetField("advDamage")?.GetValue(nums);
                object mult = adv?.GetType().GetProperty("MultValue")?.GetValue(adv) ?? adv?.GetType().GetField("MultValue")?.GetValue(adv);
                return $"held '{(w != null ? w.name : "none")}' damage x{mult}";
            }
            McLog.Info($"fury test: before: health {ht.GetField("currentHealth")?.GetValue(health)}; {Held()}; {weapons.HeldReport()}");
            Component fury = health.gameObject.AddComponent(ft);
            McLog.Info($"fury test: Fury added to '{health.gameObject.name}' as Relentless Fury does (still there: {fury != null})");
            const BindingFlags inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            MethodInfo take = et?.GetMethod("EnemyTakeDamage", inst, null, new[] { typeof(float), typeof(bool), typeof(bool) }, null);
            for (int i = 1; i <= 24; i++)
            {
                yield return new WaitForSeconds(0.5f);
                if ((i == 4 || i == 8) && take != null && rb != null)
                {
                    // A kill, as the mod's Minecraft hits do it (EnemyCombat: EnemyTakeDamage, from a weapon).
                    Component nearest = null;
                    float best = float.MaxValue;
                    foreach (UnityEngine.Object o in FindObjectsOfType(et))
                        if (o is Component c && c.gameObject.activeInHierarchy && Vector3.Distance(c.transform.position, rb.position) < best) { best = Vector3.Distance(c.transform.position, rb.position); nearest = c; }
                    if (nearest != null)
                    {
                        McLog.Info($"fury test: killing '{nearest.gameObject.name}' {best:F1} units away with 9999 weapon damage, as a Minecraft hit does");
                        try { take.Invoke(nearest, new object[] { 9999f, true, false }); }
                        catch (Exception e) { McLog.Info("fury test: the hit failed: " + (e.InnerException ?? e).Message); }
                    }
                }
                int alive = 0;
                if (et != null) foreach (UnityEngine.Object o in FindObjectsOfType(et)) if (o is Component c && c.gameObject.activeInHierarchy) alive++;
                string state = fury != null ? $"stacks {ft.GetField("currBonusNum", any)?.GetValue(fury)}, {ft.GetField("currTimer", any)?.GetValue(fury)} s" : "gone (timed out)";
                McLog.Info($"fury test: +{i * 0.5f:F1} s: Fury {state}; frenzy {furyWatch.On}; health {ht.GetField("currentHealth")?.GetValue(health)}; {Held()}; {weapons.HeldReport()}; enemies alive {alive}");
            }
        }

        /// <summary>Test aid (-mcDefenseTest): Defense Mechanism's effects, as its DoSkill makes them, and what's drawn.</summary>
        System.Collections.IEnumerator DefenseTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(4f);
            GameObject parent = GameObject.Find("PlayerParent");
            GameObject spore = GameObject.FindWithTag("currentPlayer");
            Type dct = Type.GetType("DefenseChange, Assembly-CSharp");
            Type pmt = Type.GetType("ParticleManager, Assembly-CSharp");
            object pm = pmt?.GetField("Instance")?.GetValue(null);
            Transform loadout = spore != null ? spore.transform.Find("SkillLoadout") : null;
            if (parent == null || dct == null || pm == null || loadout == null) { McLog.Info($"defense test: PlayerParent {parent != null}, DefenseChange {dct != null}, ParticleManager {pm != null}, SkillLoadout {loadout != null}"); yield break; }
            // As DefenseMechanism.DoSkill, 4 s long here (the skill's is 1 s) to look at it.
            Component dc = parent.AddComponent(dct);
            dct.GetMethod("InitializeDefenseChange")?.Invoke(dc, new object[] { 4f, 150f, false, null });
            Transform skill = loadout.childCount > 0 ? loadout.GetChild(0) : loadout;
            Vector3 at = skill.position + Vector3.up * 0.5f;
            pmt.GetMethod("SpawnParticles")?.Invoke(pm, new object[] { "DefenseBuildup", at, Quaternion.identity, skill.gameObject, null });
            McLog.Info($"defense test: Defense Mechanism's bubble (DefenseChange, 150%) and green build-up particles under '{skill.name}', in Minecraft's view {cameraSync.Mode}");
            for (int i = 1; i <= 6; i++)
            {
                yield return new WaitForSeconds(0.5f);
                if (i == 3) pmt.GetMethod("SpawnParticles")?.Invoke(pm, new object[] { "DefenseBurst", skill.position + Vector3.up * 0.5f, Quaternion.identity, skill.gameObject, null });
                var parts = new System.Collections.Generic.List<string>();
                foreach (Renderer r in spore.GetComponentsInChildren<Renderer>(true))
                {
                    string root = null;
                    for (Transform t = r.transform; t != null && t != spore.transform; t = t.parent)
                        if (t.name.StartsWith("Defense")) root = t.name;
                    if (root == null) continue;
                    bool drawn = r.enabled && r.gameObject.activeInHierarchy && !r.forceRenderingOff && r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                    parts.Add($"{root}/{r.name} {(drawn ? "DRAWN" : "not drawn")} (enabled {r.enabled}, off {r.forceRenderingOff}, {r.shadowCastingMode})");
                }
                McLog.Info($"defense test: +{i * 0.5f:F1} s, view {cameraSync.Mode}: {(parts.Count == 0 ? "no Defense parts on the spore" : string.Join("; ", parts))}");
            }
        }

        static Type weatherType;
        static FieldInfo weatherInstance, weatherRaining;
        bool rainWas;

        /// <summary>Is it raining in Mycelium (WeatherSettingsScript.Instance.isRaining). Logged when it changes.</summary>
        bool Raining()
        {
            if (weatherType == null)
            {
                weatherType = Type.GetType("WeatherSettingsScript, Assembly-CSharp");
                weatherInstance = weatherType?.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                weatherRaining = weatherType?.GetField("isRaining", BindingFlags.Public | BindingFlags.Instance);
                if (weatherType == null) weatherType = typeof(void); // not found: don't look again
            }
            object w = weatherInstance?.GetValue(null);
            if (rainTest && phase == Phase.Driving && w is UnityEngine.Object tw && tw != null)
            {
                // -mcRainTest: Mycelium's own rain on for 10 s, off for 10 s (as RainEvent turns it on and off).
                bool want = Time.time % 20f < 10f;
                if (weatherRaining?.GetValue(w) is bool cur && cur != want)
                    weatherType.GetMethod("UpdateRain")?.Invoke(w, new object[] { want });
            }
            bool raining = w is UnityEngine.Object o && o != null && weatherRaining?.GetValue(w) is bool r && r;
            if (raining != rainWas)
            {
                rainWas = raining;
                McLog.Info(raining ? "weather: it's raining in Mycelium; Minecraft gets a thunderstorm" : "weather: Mycelium's rain stopped; Minecraft's weather clears");
            }
            return raining;
        }
        bool rainTest;

        /// <summary>Test aid (-mcKeyHintTest): the curios' indicators, as after a cleared area.</summary>
        System.Collections.IEnumerator CurioHintTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(3f);
            Type gd = Type.GetType("GlobalData, Assembly-CSharp"), ct = Type.GetType("Curio, Assembly-CSharp");
            Type tmp = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro");
            if (gd == null || ct == null || tmp == null) { McLog.Info("curio hint test: no GlobalData, Curio or TMP_Text"); yield break; }
            gd.GetField("areaCleared")?.SetValue(null, true);
            UnityEngine.Object[] curios = FindObjectsOfType(ct);
            foreach (UnityEngine.Object c in curios)
            {
                var b = (MonoBehaviour)c;
                b.gameObject.SetActive(false);
                b.gameObject.SetActive(true); // OnEnable: makes its indicator (it's cleared now)
                ct.GetProperty("isPlayerInInteractTextRange")?.SetValue(c, true);
            }
            yield return null;
            foreach (UnityEngine.Object c in curios)
                if (ct.GetField("canBeActivated")?.GetValue(c) is bool can && can)
                    ((MonoBehaviour)c).StartCoroutine((System.Collections.IEnumerator)ct.GetMethod("PopInteractCanvas").Invoke(c, new object[] { true, false }));
            McLog.Info($"curio hint test: area cleared, {curios.Length} curio(s) popped in");
            for (int round = 0; round < 3; round++)
            {
                yield return new WaitForSeconds(round == 0 ? 0f : 3f);
                foreach (UnityEngine.Object c in curios)
                {
                    var canvas = ct.GetField("interactCanvas", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(c) as GameObject;
                    Component text = canvas != null ? canvas.GetComponentInChildren(tmp, true) : null;
                    McLog.Info($"curio hint test: '{c.name}' shows {(text != null ? tmp.GetProperty("text").GetValue(text) : "(no indicator)")}");
                }
            }
        }

        /// <summary>Test aid (-mcSporeCurioTest): another spore on the bed and the chair, as BedCurio and ChairCurio do.</summary>
        System.Collections.IEnumerator SporeCurioTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(4f);
            GameObject you = GameObject.FindWithTag("currentPlayer"), spore = null;
            for (int i = 0; i < colony.Count; i++)
                if (colony.Spores[i] != null && colony.Spores[i] != you) { spore = colony.Spores[i]; break; }
            if (spore == null) { McLog.Info("spore curio test: no other spore"); yield break; }
            Animator animator = spore.GetComponent<Animator>();
            Rigidbody body = spore.GetComponent<Rigidbody>();
            Behaviour wander = spore.GetComponent(Type.GetType("WanderingSpore, Assembly-CSharp")) as Behaviour;
            if (wander != null) wander.enabled = false; // the test moves it, not its wandering
            RigidbodyConstraints was = body != null ? body.constraints : RigidbodyConstraints.None;
            if (body != null) body.constraints = RigidbodyConstraints.FreezeAll;
            Transform Seat(string type)
            {
                Type t = Type.GetType(type + ", Assembly-CSharp");
                UnityEngine.Object c = t != null ? FindObjectOfType(t) : null;
                if (c == null) return null;
                var seats = t.GetField("traversalTransforms")?.GetValue(c) as System.Collections.IList;
                object first = seats != null && seats.Count > 0 ? seats[0] : null;
                return first?.GetType().GetField("transform")?.GetValue(first) as Transform ?? ((Component)c).transform;
            }
            foreach (var (curio, trigger) in new[] { ("DanceCurio", "BustMoves"), ("BedCurio", "Sleep"), ("BedCurio", "Lay"), ("ChairCurio", "SitFloor") })
            {
                Transform seat = Seat(curio);
                if (seat == null) { McLog.Info($"spore curio test: no {curio}"); continue; }
                spore.transform.position = seat.position;
                if (body != null) body.position = seat.position;
                McLog.Info($"spore curio test: '{spore.name}' on {curio}'s seat at {seat.position:F2}, {trigger}");
                animator.SetBool("InActionState", true);
                animator.SetTrigger(trigger);
                yield return new WaitForSeconds(4f);
                animator.SetBool("InActionState", false);
                yield return new WaitForSeconds(1.5f);
            }
            if (body != null) body.constraints = was;
            if (wander != null) wander.enabled = true;
            McLog.Info("spore curio test: done");
        }

        /// <summary>Test aid (-mcDrumTest): a spore's drum hit, as Mycelium's DrumCurio triggers it, sampled.</summary>
        System.Collections.IEnumerator DrumTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(4f);
            GameObject you = GameObject.FindWithTag("currentPlayer"), spore = null;
            for (int i = 0; i < colony.Count; i++)
                if (colony.Spores[i] != null && colony.Spores[i] != you) { spore = colony.Spores[i]; break; }
            if (spore == null) { McLog.Info("drum test: no other spore"); yield break; }
            Animator animator = spore.GetComponent<Animator>();
            SkinnedMeshRenderer sk = spore.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (animator == null || sk == null) { McLog.Info($"drum test: animator {animator != null}, skinned mesh {sk != null}"); yield break; }
            // Hold it still, as DrumCurio does (its rigidbody frozen).
            Rigidbody body = spore.GetComponent<Rigidbody>();
            if (body != null) body.constraints = RigidbodyConstraints.FreezeAll;
            Transform Bone(string n) { foreach (Transform t in sk.bones) if (t != null && t.name == n) return t; return null; }
            Transform armL = Bone("DEF-upper_arm.L"), handL = Bone("DEF-hand.L"), armR = Bone("DEF-upper_arm.R"), handR = Bone("DEF-hand.R");
            Transform hips = Bone("DEF-spine"), chest = Bone("DEF-spine.004"), head = Bone("head");
            Transform thighL = Bone("DEF-thigh.L"), footL = Bone("DEF-foot.L"), thighR = Bone("DEF-thigh.R"), footR = Bone("DEF-foot.R");
            if (armL == null || handL == null || armR == null || handR == null || hips == null || chest == null || head == null)
            { McLog.Info("drum test: missing a bone"); yield break; }
            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
                if (clip.name.Contains("Smash")) { McLog.Info($"drum test: clip '{clip.name}' {clip.length:F3} s at {clip.frameRate} frames/s"); break; }
            Transform me = spore.transform;
            Renderer model = spore.transform.Find("SporeModel")?.GetComponent<Renderer>();
            float height = model != null ? model.bounds.size.y : 1f;
            float baseHeight = me.InverseTransformPoint(hips.position).y;
            McLog.Info($"drum test: spore {height:F2} units tall; Minecraft angles (radians) in the torso's frame, the torso's lean in the spore's");
            // The torso's frame: up from the hips to the chest, right as the spore's right, forward from those.
            Vector3 up = Vector3.up, right = Vector3.right, fwd = Vector3.forward;
            // A limb as Minecraft's xRot/zRot (rotationZYX): its direction d in Minecraft's model space
            // (x = left, y = down, z = back) is (-sin z cos x, cos z cos x, sin x).
            string Mc(Transform from, Transform to)
            {
                if (from == null || to == null) return "-";
                Vector3 w = me.InverseTransformDirection(to.position - from.position).normalized;
                float R = Vector3.Dot(w, right), U = Vector3.Dot(w, up), F = Vector3.Dot(w, fwd);
                float x = Mathf.Asin(Mathf.Clamp(-F, -1f, 1f)), z = Mathf.Atan2(R, -U);
                if (Mathf.Abs(z) > Mathf.PI / 2f) { z -= Mathf.Sign(z) * Mathf.PI; x = (x >= 0f ? Mathf.PI : -Mathf.PI) - x; }
                return $"{x:F2}/{z:F2}";
            }
            for (int hit = 0; hit < 2; hit++)
            {
                McLog.Info($"drum test: DrumHit {hit + 1}");
                animator.SetTrigger("DrumHit");
                float start = Time.time;
                while (Time.time - start < 1.8f)
                {
                    AnimatorStateInfo now = animator.GetCurrentAnimatorStateInfo(0), next = animator.GetNextAnimatorStateInfo(0);
                    float t = now.IsName("Drum Hit") ? now.normalizedTime : next.IsName("Drum Hit") ? next.normalizedTime : -1f;
                    Vector3 torso = me.InverseTransformDirection(chest.position - hips.position).normalized;
                    up = torso;
                    right = Vector3.ProjectOnPlane(Vector3.right, up).normalized;
                    fwd = Vector3.Cross(right, up);
                    float lean = Mathf.Atan2(torso.z, torso.y), side = Mathf.Atan2(torso.x, torso.y);
                    Vector3 headUp = me.InverseTransformDirection(head.up);
                    float nod = Vector3.SignedAngle(up, headUp, right) * Mathf.Deg2Rad;
                    McLog.Info($"drum test: t {t:F3}{(animator.IsInTransition(0) ? " blending" : "")}: rightArm {Mc(armR, handR)} leftArm {Mc(armL, handL)} rightLeg {Mc(thighR, footR)} leftLeg {Mc(thighL, footL)} " +
                        $"head {nod:F2} lean {lean:F2} side {side:F2} lift {(me.InverseTransformPoint(hips.position).y - baseHeight) / height:F3}");
                    yield return new WaitForSeconds(0.025f);
                }
            }
        }

        /// <summary>Test aid (-mcMoodTest): happiness up then down on you and one other spore, and whether the particles are drawn.</summary>
        System.Collections.IEnumerator MoodTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(4f);
            Type cst = Type.GetType("CharacterStats, Assembly-CSharp");
            var spores = new System.Collections.Generic.List<GameObject>();
            GameObject you = GameObject.FindWithTag("currentPlayer");
            if (you != null) spores.Add(you);
            for (int i = 0; i < colony.Count; i++)
                if (colony.Spores[i] != null && colony.Spores[i] != you) { spores.Add(colony.Spores[i]); break; }
            if (cst == null || spores.Count == 0) { McLog.Info($"mood test: CharacterStats {cst != null}, {spores.Count} spore(s)"); yield break; }
            foreach (float amount in new[] { 0.05f, -0.05f })
            {
                foreach (GameObject s in spores)
                {
                    Component stats = s.GetComponent(cst);
                    if (stats == null) { McLog.Info($"mood test: '{s.name}' has no CharacterStats"); continue; }
                    cst.GetMethod("ModifyHappiness")?.Invoke(stats, new object[] { amount });
                }
                McLog.Info($"mood test: happiness {(amount > 0 ? "up" : "down")} {Mathf.Abs(amount):F2} on {spores.Count} spore(s), view {cameraSync.Mode}");
                for (int k = 1; k <= 3; k++)
                {
                    yield return new WaitForSeconds(0.5f);
                    foreach (GameObject s in spores)
                    {
                        int drawn = 0, notDrawn = 0;
                        foreach (Renderer r in s.GetComponentsInChildren<Renderer>(true))
                        {
                            if (!CameraSync.MoodEffect(r.transform, s.transform)) continue;
                            if (r.enabled && r.gameObject.activeInHierarchy && !r.forceRenderingOff && r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) drawn++;
                            else notDrawn++;
                        }
                        McLog.Info($"mood test: +{k * 0.5f:F1} s, '{s.name}'{(s == you ? " (you)" : "")}: {drawn} happy/sad particle part(s) drawn, {notDrawn} not drawn");
                    }
                }
                yield return new WaitForSeconds(3f); // Mycelium makes no new ones while the old ones are still there
            }
        }

        /// <summary>Test aid (-mcHudDump): the health HUD's objects, what draws in each, and where they are.</summary>
        System.Collections.IEnumerator HudDump()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(3f);
            Type ht = Type.GetType("HUDHealth, Assembly-CSharp");
            var hud = ht != null ? FindObjectOfType(ht) as Component : null;
            if (hud == null) { McLog.Info("hud dump: no HUDHealth"); yield break; }
            const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var bar = ht.GetField("healthBar", any)?.GetValue(hud) as Component;
            var num = ht.GetField("healthNumberText", any)?.GetValue(hud) as Component;
            var name = ht.GetField("sporeNameText", any)?.GetValue(hud) as Component;
            string P(Component c)
            {
                if (c == null) return "none";
                string p = c.name;
                for (Transform t = c.transform.parent; t != null; t = t.parent) p = t.name + "/" + p;
                return p;
            }
            McLog.Info($"hud dump: HUDHealth on '{P(hud)}'; bar '{P(bar)}'; number '{P(num)}'; name '{P(name)}'");
            // From the HUDHealth object's parent down, every object with what draws in it.
            Transform root = hud.transform;
            foreach (Component c in new[] { bar, num, name })
            {
                if (c == null) continue;
                // Climb root until it holds all three.
                while (root.parent != null && !c.transform.IsChildOf(root)) root = root.parent;
            }
            void Dump(Transform t, int depth)
            {
                var rt = t as RectTransform;
                var draws = new System.Collections.Generic.List<string>();
                foreach (Component c in t.GetComponents<Component>())
                    if (c is UnityEngine.UI.Graphic g) draws.Add($"{c.GetType().Name}{(g.enabled ? "" : " (off)")}");
                McLog.Info($"hud dump: {new string(' ', depth * 2)}'{t.name}' active {t.gameObject.activeSelf}{(rt != null ? $", at {rt.anchoredPosition.x:F0},{rt.anchoredPosition.y:F0} size {rt.rect.width:F0}x{rt.rect.height:F0}" : "")}{(draws.Count > 0 ? ", draws " + string.Join(", ", draws) : "")}");
                if (depth < 8) for (int i = 0; i < t.childCount; i++) Dump(t.GetChild(i), depth + 1);
            }
            Dump(root, 0);
            for (int i = 0; i < 6; i++)
            {
                McLog.Info($"hud dump: +{i} s: {HealthPanel.Report()}");
                yield return new WaitForSeconds(1f);
            }
        }

        /// <summary>Test aid (-mcWaveTest): another spore waves, as Mycelium's curios make it (its Animator's Wave trigger).</summary>
        System.Collections.IEnumerator WaveTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(4f);
            int other = colony.Current == 0 ? 1 : 0;
            if (colony.Count < 2) { McLog.Info("wave test: no other spore"); yield break; }
            Animator a = colony.Spores[other].GetComponent<Animator>();
            if (a == null) { McLog.Info("wave test: no Animator"); yield break; }
            McLog.Info($"wave test: {colony.Names[other]} waves (Wave trigger)");
            a.SetTrigger("Wave");
            for (int i = 1; i <= 8; i++)
            {
                yield return new WaitForSeconds(0.5f);
                AnimatorStateInfo s = a.GetCurrentAnimatorStateInfo(0);
                McLog.Info($"wave test: +{i * 0.5f:F1} s: in Hello {s.IsName("Hello")}, waving bits {colony.Waving:X}");
            }
        }

        /// <summary>Test aid (-mcCurioTest): each decoration in The Carcass used as your Curio key does, and what follows.</summary>
        System.Collections.IEnumerator CurioTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(4f);
            Type ct = Type.GetType("Curio, Assembly-CSharp");
            if (ct == null || controller == null || rb == null) { McLog.Info("curio test: no Curio type, controller or spore"); yield break; }
            var all = new System.Collections.Generic.List<Component>();
            // Locked furniture is switched off in the level: switched on here to try it ("-mcCurioTest all").
            bool everything = Array.IndexOf(Environment.GetCommandLineArgs(), "all") >= 0;
            foreach (UnityEngine.Object o in Resources.FindObjectsOfTypeAll(ct))
            {
                if (!(o is Component c) || !c.gameObject.scene.IsValid()) continue;
                if (!c.gameObject.activeInHierarchy)
                {
                    if (!everything) continue;
                    for (Transform t = c.transform; t != null; t = t.parent) t.gameObject.SetActive(true);
                    McLog.Info($"curio test: '{c.gameObject.name}' ({c.GetType().Name}) was switched off (locked); on for the test");
                }
                all.Add(c);
            }
            if (everything)
            {
                // The dance spot is open only while the drum plays (DrumCurio.AllowPlayerDancing): open it.
                playerControllerType.GetField("canDance")?.SetValue(controller, true);
                all.Sort((x, y) => string.CompareOrdinal(x.GetType().Name, y.GetType().Name));
                all.RemoveAll(x => x.GetType().Name == "ChairCurio" || x.GetType().Name == "BedCurio" || !(ct.GetField("interactAnimationStrings")?.GetValue(x) is System.Collections.Generic.List<string> l && l.Count > 0));
            }
            McLog.Info($"curio test: before: overlay {overlay.Visible}");
            McLog.Info($"curio test: {all.Count} decorations: " + string.Join("; ", all.ConvertAll(c =>
            {
                var anims = ct.GetField("interactAnimationStrings")?.GetValue(c) as System.Collections.Generic.List<string>;
                return $"'{c.gameObject.name}' {c.GetType().Name} [{(anims != null ? string.Join(", ", anims) : "?")}] range {ct.GetField("playerInteractRange")?.GetValue(c)} self {ct.GetField("selfCurio")?.GetValue(c)}, {Vector3.Distance(c.transform.position, rb.position):F0} units away";
            })));
            MethodInfo use = playerControllerType.GetMethod("InteractWithCurio", BindingFlags.NonPublic | BindingFlags.Instance);
            Animator a = rb.GetComponent<Animator>();
            var rounds = new System.Collections.Generic.List<(Component c, int side)>();
            foreach (Component c in all) for (int side = 0; side < 3; side++) rounds.Add((c, side));
            foreach (var (c, side) in rounds)
            {
                var anims = ct.GetField("interactAnimationStrings")?.GetValue(c) as System.Collections.Generic.List<string>;
                if (anims == null || anims.Count == 0 || (ct.GetField("selfCurio")?.GetValue(c) is bool self && self)) continue;
                string anim = anims[c.GetType().Name == "BedCurio" ? (Time.frameCount % 2) : 0];
                foreach (Component other in all) if (other != c && other.transform.position.y > -900f && !(ct.GetField("selfCurio")?.GetValue(other) is bool os && os)) other.transform.position += Vector3.down * 1000f; // the others out of the way
                Vector3[] sides = { rb.transform.forward, rb.transform.right, -rb.transform.forward };
                c.transform.position = rb.position + sides[side] * 1.2f; // next to you, as when you walk up to it: in front, to the right, behind
                yield return new WaitForSeconds(0.2f);
                var found = playerControllerType.GetMethod("FindClosestCurioInRange", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(controller, null) as Component;
                McLog.Info($"curio test: Mycelium's own lookup (FindClosestCurioInRange) finds '{(found != null ? found.gameObject.name : "nothing")}'{(found != null ? $" {Vector3.Distance(found.transform.position, rb.position):F1} units away" : "")}; colliders on '{c.gameObject.name}': {string.Join(", ", System.Array.ConvertAll(c.GetComponentsInChildren<Collider>(), k => $"{k.gameObject.name} layer {LayerMask.LayerToName(k.gameObject.layer)}{(k.GetComponent(ct) != null ? " (has the Curio)" : "")}"))}");
                McLog.Info($"curio test: your facing before: yaw {rb.rotation.eulerAngles.y:F0}");
                var seats = ct.GetField("traversalTransforms")?.GetValue(c) as System.Collections.IList;
                if (seats != null)
                    foreach (object seat in seats)
                    {
                        var st = seat.GetType().GetField("transform")?.GetValue(seat) as Transform;
                        if (st != null) McLog.Info($"curio test: seat '{st.name}' at {(st.position - c.transform.position):F2} from '{c.gameObject.name}', yaw {st.eulerAngles.y:F0}");
                    }
                McLog.Info($"curio test: using '{c.gameObject.name}' ({c.GetType().Name}) with '{anim}', as PlayerController does after your Curio key");
                var routine = use.Invoke(controller, new object[] { c, anim }) as System.Collections.IEnumerator;
                (controller as MonoBehaviour).StartCoroutine(routine);
                for (int i = 1; i <= 4; i++)
                {
                    yield return new WaitForSeconds(0.75f);
                    var s = a != null ? a.GetCurrentAnimatorStateInfo(0) : default;
                    string state = "?";
                    if (a != null) foreach (string n in new[] { "Idle", "Walk", "Hello", "Bustin Moves", "Bustin Metal Moves", "SitStump(Beg)", "SitStump(Mid)", "SitFloor(Beg)", "SitFloor(Mid)", "Lay(Beg)", "Lay(Mid)", "Sleep(Beg)", "Sleep(Mid)", "Drum Hit", "Bustin Moves", "Bustin Metal Moves" }) if (s.IsName(n)) state = n;
                    // Where the spore's head is while it lies: from its root bone to the bone named like a head, across the ground, against its facing.
                    {
                        var sk = rb.GetComponentInChildren<SkinnedMeshRenderer>();
                        Transform headBone = null, rootBone = sk != null ? sk.rootBone : null;
                        if (sk != null) foreach (Transform b in sk.bones) if (b != null && b.name.ToLowerInvariant().Contains("head") && (headBone == null || b.name.Length < headBone.name.Length)) headBone = b;
                        if (i == 1 && sk != null) McLog.Info($"curio test: bones with 'head': {string.Join(", ", System.Array.ConvertAll(System.Array.FindAll(sk.bones, b => b != null && b.name.ToLowerInvariant().Contains("head")), b => b.name))}");
                        if (i == 3 && c.GetType().Name == "BedCurio")
                        {
                            // The surface along where your Minecraft body lies (head behind you for Sleep, ahead for Lay), as the ground scan sees it.
                            Vector3 dir = (anim == "Lay" ? 1f : -1f) * new Vector3(rb.transform.forward.x, 0f, rb.transform.forward.z).normalized;
                            var heights = new System.Collections.Generic.List<string>();
                            for (float along = 0f; along <= 2.01f; along += 0.25f)
                            {
                                Vector3 top = rb.position + dir * along + Vector3.up * 3f;
                                string h = "-";
                                if (Physics.Raycast(top, Vector3.down, out RaycastHit hit, 6f, ground.LayerMask, QueryTriggerInteraction.Ignore))
                                    h = $"{hit.point.y - (rb.position.y - feetOffset):F2} ({hit.collider.name})";
                                heights.Add($"{along:F2}: {h}");
                            }
                            McLog.Info($"curio test: the bed's surface along your body ({anim}), units along: height above your feet: {string.Join("; ", heights)}; ground grid cells {GroundScanner.CellBlocks} blocks");
                        }
                        if (headBone != null && rootBone != null)
                        {
                            Vector3 d = headBone.position - rb.position;
                            Vector3 flat = new Vector3(d.x, 0f, d.z);
                            float headYaw = flat.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(flat).eulerAngles.y : float.NaN;
                            McLog.Info($"curio test: side {side}, +{i * 0.75f:F2} s: head '{headBone.name}' {flat.magnitude:F2} across and {d.y:F2} up from the spore's feet point, towards yaw {headYaw:F0}; the spore faces {rb.rotation.eulerAngles.y:F0}: head {Mathf.DeltaAngle(rb.rotation.eulerAngles.y, headYaw):F0} from its facing");
                        }
                    }
                    var skin = rb.GetComponentInChildren<SkinnedMeshRenderer>();
                    Transform bone = skin != null ? skin.rootBone : null;
                    float boneTurn = bone != null ? Mathf.DeltaAngle(rb.rotation.eulerAngles.y, Quaternion.LookRotation(Vector3.ProjectOnPlane(bone.forward, Vector3.up).sqrMagnitude > 1e-4f ? Vector3.ProjectOnPlane(bone.forward, Vector3.up) : rb.transform.forward).eulerAngles.y) : float.NaN;
                    Transform nearSeat = null;
                    if (seats != null) foreach (object seat in seats) { var st2 = seat.GetType().GetField("transform")?.GetValue(seat) as Transform; if (st2 != null && (nearSeat == null || Vector3.Distance(st2.position, rb.position) < Vector3.Distance(nearSeat.position, rb.position))) nearSeat = st2; }
                    McLog.Info($"curio test: side {side}, +{i * 0.75f:F2} s: {(nearSeat != null ? Vector3.Distance(new Vector3(rb.position.x, 0, rb.position.z), new Vector3(nearSeat.position.x, 0, nearSeat.position.z)) : float.NaN):F2} units from the seat across, feet {rb.position.y - feetOffset - (nearSeat != null ? nearSeat.position.y : 0f):F2} above it, yaw {rb.rotation.eulerAngles.y:F0} (seat yaw {(nearSeat != null ? nearSeat.eulerAngles.y : float.NaN):F0})");
                    McLog.Info($"curio test: +{i * 0.75f:F2} s: spore yaw {rb.rotation.eulerAngles.y:F0}, its root bone '{(bone != null ? bone.name : "none")}' turned {boneTurn:F0} from it (model faces {rb.rotation.eulerAngles.y + boneTurn:F0}), state time {(a != null ? a.GetCurrentAnimatorStateInfo(0).normalizedTime : 0f):F2}");
                    McLog.Info($"curio test: +{i * 0.75f:F2} s: spore animation {state}, canAct {canActField?.GetValue(controller)}, Mycelium has the controls {MyceliumHasControls(out string why)} ({why}), phase {phase}, overlay {overlay.Visible}, your Minecraft body shown {playerModel.Shown}");
                }
                // As your movement key does: Mycelium's InteractWithCurio waits for its Move action, then ends.
                a.SetBool(anim, false);
                a.SetBool("InActionState", false);
                a.Play("Idle");
                controller.GetType().GetMethod("EnableController", Type.EmptyTypes)?.Invoke(controller, null);
                (controller as MonoBehaviour).StopCoroutine(routine);
                yield return new WaitForSeconds(1f);
            }
        }

        /// <summary>Test aid (-mcCurioKeyTest): the Curio key pressed far from any decoration, through Unity's input system.</summary>
        System.Collections.IEnumerator CurioKeyTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(4f);
            var action = playerControllerType?.GetField("curioInteract", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(controller) as UnityEngine.InputSystem.InputAction;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (action == null || mouse == null) { McLog.Info($"curio key test: Curio action {action != null}, mouse {mouse != null}"); yield break; }
            var paths = new System.Collections.Generic.List<string>();
            foreach (var b in action.bindings) paths.Add(b.effectivePath);
            McLog.Info($"curio key test: the Curio action is bound to {string.Join(", ", paths)}; pressing the middle mouse button");
            Animator a = rb != null ? rb.GetComponent<Animator>() : null;
            for (int round = 0; round < 2; round++)
            {
                var press = new UnityEngine.InputSystem.LowLevel.MouseState();
                press = press.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Middle, true);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, press);
                UnityEngine.InputSystem.InputSystem.Update();
                bool seen = action.triggered;
                McLog.Info($"curio key test: round {round + 1}: right after the press, triggered {seen}");
                if (!seen)
                {
                    McLog.Info("curio key test: the press didn't reach Mycelium's input (its window has no focus in a test run): not tested this way");
                }
                yield return new WaitForSeconds(1.5f);
                var s = a != null ? a.GetCurrentAnimatorStateInfo(0) : default;
                McLog.Info($"curio key test: 1.5 s later: spore waving {(a != null && s.IsName("Hello"))}, InActionState {(a != null && a.GetBool("InActionState"))}, canAct {canActField?.GetValue(controller)}");
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, new UnityEngine.InputSystem.LowLevel.MouseState());
                UnityEngine.InputSystem.InputSystem.Update();
                yield return new WaitForSeconds(0.5f);
            }
        }

        /// <summary>Test aid (-mcSeatTest): a colony spore uses each stump chair and the bed, as it would by itself; where it ends up and which way it faces.</summary>
        System.Collections.IEnumerator SeatTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(3f);
            Type ct = Type.GetType("Curio, Assembly-CSharp");
            Type wt = Type.GetType("WanderingSpore, Assembly-CSharp");
            if (ct == null || wt == null) yield break;
            var curios = new System.Collections.Generic.List<Component>();
            foreach (UnityEngine.Object o in FindObjectsOfType(ct))
                if (o is Component c && c.gameObject.activeInHierarchy && (c.GetType().Name == "ChairCurio" || c.GetType().Name == "BedCurio")) curios.Add(c);
            // What each seat faces: the first thing its forward line passes near.
            foreach (Component curio in curios)
            {
                var list = ct.GetField("traversalTransforms")?.GetValue(curio) as System.Collections.IList;
                if (list == null) continue;
                foreach (object seatObj in list)
                {
                    var st = seatObj.GetType().GetField("transform")?.GetValue(seatObj) as Transform;
                    if (st == null) continue;
                    var hits = new System.Collections.Generic.List<string>();
                    for (float dist = 1f; dist <= 8f; dist += 1f)
                    {
                        Vector3 p = st.position + Vector3.ProjectOnPlane(st.forward, Vector3.up).normalized * dist;
                        foreach (Collider col in Physics.OverlapSphere(p, 0.8f))
                        {
                            string n = col.transform.root.name + "/" + col.name;
                            if (!hits.Contains(n) && col.gameObject != curio.gameObject && !col.transform.IsChildOf(curio.transform) && hits.Count < 6) hits.Add($"{n} at {dist:F0}");
                        }
                    }
                    McLog.Info($"seat test: '{curio.gameObject.name}' ({curio.GetType().Name}) seat '{st.name}' faces yaw {st.eulerAngles.y:F0}: along it: {(hits.Count > 0 ? string.Join(", ", hits) : "nothing within 8")}");
                }
            }
            for (int k = 0; k < curios.Count; k++)
            {
                Component curio = curios[k];
                int pick = (colony.Current + 1 + k) % Mathf.Max(1, colony.Count);
                GameObject spore = colony.Spores[pick];
                Component ws = spore != null ? spore.GetComponent(wt) : null;
                if (ws == null || !(ws is Behaviour wb) || !wb.enabled) { McLog.Info($"seat test: {colony.Names[pick]} isn't wandering"); continue; }
                // Close to it first, as a spore that chose it would walk up.
                spore.transform.position = curio.transform.position + (spore.transform.position - curio.transform.position).normalized * 2.5f;
                var seats = ct.GetField("traversalTransforms")?.GetValue(curio) as System.Collections.IList;
                Transform seat = null;
                if (seats != null && seats.Count > 0) seat = seats[0].GetType().GetField("transform")?.GetValue(seats[0]) as Transform;
                McLog.Info($"seat test: {colony.Names[pick]} uses '{curio.gameObject.name}' ({curio.GetType().Name}) at {curio.transform.position:F2}; its seat at {(seat != null ? seat.position.ToString("F2") : "?")} yaw {(seat != null ? seat.eulerAngles.y : float.NaN):F0}");
                var routine = wt.GetMethod("InteractWithCurio").Invoke(ws, new object[] { curio }) as System.Collections.IEnumerator;
                (ws as MonoBehaviour).StartCoroutine(routine);
                Animator a = spore.GetComponent<Animator>();
                for (int i = 1; i <= 16; i++)
                {
                    yield return new WaitForSeconds(0.5f);
                    bool acting = a != null && a.GetBool("InActionState");
                    if (i % 2 == 0 || acting)
                        McLog.Info($"seat test: +{i * 0.5f:F1} s: {colony.Names[pick]} at {(spore.transform.position - curio.transform.position):F2} from it ({(seat != null ? Vector3.Distance(spore.transform.position, seat.position) : float.NaN):F2} from the seat), yaw {spore.transform.eulerAngles.y:F0}, acting {acting}");
                    if (acting && i >= 6) break;
                }
                yield return new WaitForSeconds(1f);
            }
        }

        /// <summary>Test aid (-mcForgeTest): three weapons picked up, the Blacksmith brought in front, Interact pressed.</summary>
        System.Collections.IEnumerator ForgeTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(4f);
            Type wi = Type.GetType("WeaponInteraction, Assembly-CSharp");
            MethodInfo pick = wi?.GetMethod("Interact", BindingFlags.Public | BindingFlags.Instance);
            foreach (string path in new[] { "Slash/AvocadoFlamberge", "Smash/RoseMace", "Stab/OpalRapier" })
            {
                UnityEngine.Object prefab = Resources.Load(path);
                if (prefab == null || rb == null || pick == null) { McLog.Info($"forge test: no {path} prefab, spore or WeaponInteraction"); yield break; }
                var o = Instantiate(prefab, rb.position + rb.transform.forward * 1.5f, Quaternion.identity) as GameObject;
                o.tag = "Weapon";
                yield return new WaitForSeconds(0.5f); // its Start: stats, a random attribute
                McLog.Info($"forge test: picking up '{o.name}'");
                try { pick.Invoke(o.GetComponent(wi), new object[] { o }); }
                catch (Exception e) { McLog.Info("forge test: pickup failed: " + (e.InnerException ?? e).Message); }
                yield return new WaitForSeconds(1f);
            }
            yield return new WaitForSeconds(4f); // the fake Minecraft's hand is empty: the last one goes into the bag too

            McLog.Info($"forge test: the Blacksmith takes: {weapons.ForgeReport()}");
            GameObject spore = GameObject.FindWithTag("currentPlayer");
            Component smith = spore != null ? forge.Nearest(spore.transform.position) : null;
            if (smith == null) { McLog.Info("forge test: no Blacksmith (ForgeShroom) in this level"); yield break; }
            McLog.Info($"forge test: the Blacksmith '{smith.gameObject.name}' was {Vector3.Distance(spore.transform.position, smith.transform.position):F1} units away; brought 1.5 units in front of the spore");
            smith.transform.position = spore.transform.position + spore.transform.forward * 1.5f;
            Type ft = Type.GetType("SporeInteractableFinder, Assembly-CSharp");
            Component finder = ft != null ? spore.GetComponent(ft) : null;
            FieldInfo closestF = ft?.GetField("closestInteractableObject");
            GameObject closest = null;
            for (int i = 0; i < 30 && finder != null; i++)
            {
                closest = closestF.GetValue(finder) as GameObject;
                if (closest == smith.gameObject) break;
                yield return new WaitForSeconds(0.1f);
            }
            McLog.Info($"forge test: Mycelium's closest thing is '{(closest != null ? closest.name : "none")}' (the Blacksmith: {closest == smith.gameObject})");
            FieldInfo salvaged = smith.GetType().GetField("salvagedWeapons", BindingFlags.NonPublic | BindingFlags.Instance);
            uint before = forge.Opened;
            // Press the key Mycelium's Interact is bound to, as your keyboard would.
            var action = playerControllerType?.GetField("interact", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(controller) as UnityEngine.InputSystem.InputAction;
            UnityEngine.InputSystem.Controls.KeyControl key = null;
            if (action != null) foreach (var c in action.controls) if (c is UnityEngine.InputSystem.Controls.KeyControl k) { key = k; break; }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (key != null && kb != null)
            {
                McLog.Info($"forge test: pressing '{key.keyCode}' (Mycelium's Interact)");
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(key.keyCode));
                yield return null;
                yield return null;
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
                yield return new WaitForSeconds(0.5f);
            }
            else McLog.Info($"forge test: no key for Interact (action {action != null}, keyboard {kb != null})");
            if (forge.Opened == before)
            {
                McLog.Info("forge test: the key press didn't reach the catch (the window has no focus?); calling it as the catch would");
                forge.Interacted(smith, false);
                yield return null;
                forge.Late();
            }
            McLog.Info($"forge test: screens asked for {forge.Opened - before}; Mycelium's own Blacksmith took {salvaged?.GetValue(smith)} weapon(s) (want 0)");
            for (int i = 0; i < 20; i++)
            {
                yield return new WaitForSeconds(0.5f);
                if (forge.Handled != 0) break;
            }
            yield return new WaitForSeconds(3f);
            McLog.Info($"forge test: done; last answer #{forge.Handled} gave weapon #{forge.LastResult}; Mycelium's own Blacksmith took {salvaged?.GetValue(smith)} weapon(s); finished: {smith.GetType().GetField("weaponGoalReached", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(smith)}");
        }

        /// <summary>Test aid (-mcPickupTest): a weapon pickup through Mycelium's own code.</summary>
        System.Collections.IEnumerator PickupTest()
        {
            while (phase != Phase.Driving) yield return null;
            yield return new WaitForSeconds(4f);
            // "-mcPickupTest <Type/Prefab>" picks that one up instead (default Smash/CactusMace).
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-mcPickupTest");
            string path = at >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("-") ? args[at + 1] : "Smash/CactusMace";
            UnityEngine.Object prefab = Resources.Load(path);
            if (prefab == null || rb == null) { McLog.Info($"pickup test: no {path} prefab"); yield break; }
            var mace = Instantiate(prefab, rb.position + rb.transform.forward * 1.5f, Quaternion.identity) as GameObject;
            mace.tag = "Weapon";
            yield return new WaitForSeconds(0.5f); // its Start: stats, a random attribute
            Type t = Type.GetType("WeaponInteraction, Assembly-CSharp");
            Component interact = t != null ? mace.GetComponent(t) : null;
            MethodInfo m = t?.GetMethod("Interact", BindingFlags.Public | BindingFlags.Instance);
            if (interact == null || m == null) { McLog.Info("pickup test: no WeaponInteraction.Interact"); yield break; }
            Type hs = Type.GetType("HUDStats, Assembly-CSharp");
            GameObject hud = GameObject.Find("HUD");
            Component stats = hud != null && hs != null ? hud.GetComponent(hs) : null;
            bool statWeapon = false;
            foreach (Component c in mace.GetComponents<Component>())
                for (Type ct = c != null ? c.GetType() : null; ct != null; ct = ct.BaseType)
                    if (ct.Name == "AttributeBase" && ct.GetField("statChange")?.GetValue(c) is bool sc && sc) statWeapon = true;
            // Mycelium's own pop-up for it (SporeInteractableFinder) decides whether the stat panel comes in.
            for (int i = 0; i < 4; i++)
            {
                yield return new WaitForSeconds(0.3f);
                McLog.Info($"pickup test: lying in front for {0.3f * (i + 1):F1} s: stat panel at {PanelX(stats)}");
            }
            McLog.Info($"pickup test: picking up '{mace.name}' through WeaponInteraction.Interact (changes stats: {statWeapon}; stat panel at {PanelX(stats)})");
            try { m.Invoke(interact, new object[] { mace }); }
            catch (Exception e) { McLog.Info("pickup test: Interact failed: " + (e.InnerException ?? e).Message); }
            yield return new WaitForSeconds(2.5f);
            McLog.Info($"pickup test: 2.5 s after the pickup, the stat panel is at {PanelX(stats)}");
        }

        /// <summary>Where the stat panel is, and where its in / out spots are (test aid).</summary>
        static string PanelX(Component stats)
        {
            if (stats == null) return "(no HUDStats)";
            RectTransform R(string n) => stats.GetType().GetField(n, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)?.GetValue(stats) as RectTransform;
            RectTransform holder = R("statsHolder"), inside = R("statsInsideTarget"), outside = R("statsOutsideTarget");
            if (holder == null) return "(no statsHolder)";
            return $"x {holder.localPosition.x:F0} (in {(inside != null ? inside.localPosition.x.ToString("F0") : "?")}, out {(outside != null ? outside.localPosition.x.ToString("F0") : "?")})";
        }

        static bool Drawn(GameObject o)
        {
            foreach (Renderer r in o.GetComponentsInChildren<Renderer>(true))
                if (r.enabled && !r.forceRenderingOff && r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) return true;
            return false;
        }

        uint lastPlayReason = uint.MaxValue;
        bool hurtTest, knockTest;
        float nextHurtTime;
        int hurtTestStep;
        GameObject testHitter;

        /// <summary>
        /// Test aid: 1 damage through Mycelium's own PlayerTakeDamage, from a test hitter (a small
        /// trigger with a "TestDamageSource" script, so the hurt tilt finds it as it finds an
        /// enemy's hitbox) 1.5 units in front, right, behind, then left of where the spore faces.
        /// </summary>
        void RunHurtTest()
        {
            nextHurtTime = Time.realtimeSinceStartup + 3f;
            if (testHitter == null)
            {
                testHitter = new GameObject("MyceliumCraft test hitter");
                var sphere = testHitter.AddComponent<SphereCollider>();
                sphere.isTrigger = true;
                sphere.radius = 0.2f;
                testHitter.AddComponent<TestDamageSource>();
            }
            string[] sides = { "in front", "right", "behind", "left" };
            int side = hurtTestStep++ % 4;
            Vector3 dir = Quaternion.Euler(0f, rb.rotation.eulerAngles.y + 90f * side, 0f) * Vector3.forward;
            testHitter.transform.position = rb.worldCenterOfMass + dir * 1.5f;
            testHitter.SetActive(true);
            Physics.SyncTransforms();
            MethodInfo take = playerHealth.GetType().GetMethod("PlayerTakeDamage", BindingFlags.Public | BindingFlags.Instance);
            try
            {
                take.Invoke(playerHealth, new object[] { 1f });
                McLog.Info($"hurt test: 1 damage from the test hitter {sides[side]} of the spore");
                if (knockTest)
                {
                    Type ct = Type.GetType("PlayerController, Assembly-CSharp");
                    Component controller = ct != null ? FindObjectOfType(ct) as Component : null;
                    ct?.GetMethod("Knockback", BindingFlags.Public | BindingFlags.Instance)?.Invoke(controller, new object[] { testHitter, 10f, false });
                    McLog.Info($"hurt test: knocked back ({(controller != null ? "PlayerController.Knockback" : "no PlayerController")})");
                }
            }
            catch (Exception e)
            {
                McLog.Info("hurt test: PlayerTakeDamage failed: " + (e.InnerException?.Message ?? e.Message));
            }
        }

        /// <summary>Marks -mcHurtTest's hitter as something that deals damage (its name has "Damage" in it).</summary>
        sealed class TestDamageSource : MonoBehaviour { }

        /// <summary>Test aid: DisableController after 3 s of driving, EnableController 3 s later, as Mycelium's menus do.</summary>
        void RunHoldTest()
        {
            if (holdTestStart < 0f) holdTestStart = Time.realtimeSinceStartup;
            float t = Time.realtimeSinceStartup - holdTestStart;
            string method = holdTestStep == 0 && t >= 3f ? "DisableController" : holdTestStep == 1 && t >= 6f ? "EnableController" : null;
            if (method == null) return;
            holdTestStep++;
            MethodInfo m = playerControllerType.GetMethod(method, BindingFlags.Public | BindingFlags.Instance);
            object[] callArgs = m != null && m.GetParameters().Length == 1 ? new object[] { null } : new object[0];
            m?.Invoke(controller, callArgs);
            // While Mycelium has the controls, push the spore 1 unit (as a knockback does), so giving
            // them back needs a re-sync teleport.
            if (method == "DisableController" && rb != null) rb.position += rb.transform.right * 1f;
            McLog.Info($"hold test: called PlayerController.{method}() (found={m != null})");
        }

        /// <summary>Test aid: shake the camera the way the level's own triggers do (CameraShakeManager.ShakeCamera).</summary>
        void FireTestShake()
        {
            nextShakeTime = Time.realtimeSinceStartup + 3f;
            var source = FindObjectOfType<Cinemachine.CinemachineImpulseSource>();
            Type managerType = Type.GetType("CameraShakeManager, Assembly-CSharp");
            object manager = managerType?.GetField("instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            MethodInfo shakeMethod = managerType?.GetMethod("ShakeCamera", new[] { typeof(Cinemachine.CinemachineImpulseSource), typeof(bool) });
            if (source == null || manager == null || shakeMethod == null)
            {
                McLog.Info($"shake test: can't shake (impulse source {(source != null ? "found" : "missing")}, CameraShakeManager {(manager != null ? "found" : "missing")})");
                return;
            }
            shakeMethod.Invoke(manager, new object[] { source, false });
            McLog.Info($"shake test: fired '{source.name}' (duration {source.m_ImpulseDefinition.m_ImpulseDuration:F2} s), camera view {cameraSync.Mode}");
        }

        /// <summary>Where the rendered camera really points (after Cinemachine), next to the spore's facing.</summary>
        string ViewText()
        {
            Camera cam = Camera.main;
            if (cam == null) return "view=none";
            Vector3 f = cam.transform.forward;
            float viewYaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            float viewDown = -Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
            float sporeYaw = rb != null ? rb.rotation.eulerAngles.y : float.NaN;
            float diff = Mathf.DeltaAngle(viewYaw, sporeYaw);
            string where = "";
            if (rb != null)
            {
                // Where the camera sits: distance from the spore, and in front of or behind it.
                Vector3 rel = cam.transform.position - rb.transform.position;
                Vector3 facing = rb.transform.forward;
                float along = Vector3.Dot(new Vector3(rel.x, 0f, rel.z), new Vector3(facing.x, 0f, facing.z).normalized);
                where = $" camDist={rel.magnitude:F2} camSide={(rel.magnitude < 0.5f ? "inside" : along > 0 ? "front" : "behind")} camHeight={rel.y:F2}";
            }
            return $"view yaw={viewYaw:F0} down={viewDown:F0} sporeYaw={sporeYaw:F0} (diff {diff:F0}){where}";
        }

        void OnDestroy()
        {
            McLog.Info($"driver destroyed (scene '{SceneManager.GetActiveScene().name}'), the link closes; from: {Environment.StackTrace}");
            link?.Dispose();
        }
    }
}
