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
        readonly ColonyWatch colony = new ColonyWatch();
        readonly BirthCutscene birth = new BirthCutscene();
        readonly VanishWatch vanish = new VanishWatch();
        readonly KeyHints keyHints = new KeyHints();
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
        static void Boot()
        {
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

            SceneManager.sceneLoaded += (scene, mode) =>
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
                SetPhase(Phase.Idle, "new scene");
            };
        }

        void Update()
        {
            BackgroundWindow.Update();
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
            bool cycloning = rb != null && Cycloning();
            if (cycloning) flags |= Proto.MycCyclone;
            BlockSkillsWhileCycloning(cycloning);
            // The dodge roll: Minecraft plays its combat roll animation on your body (MycSkillAnimation.java).
            if (rb != null && animator != null && animator.isActiveAndEnabled && animator.GetBool("Roll")) flags |= Proto.MycRoll;
            // Mycelium's health, which Minecraft's follows (/ 5).
            float health = 0f, maxHealth = 0f;
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

            // Your colony: in Minecraft's player list instead of Mycelium's colony meter (ColonyWatch, MycColony.java).
            if (colony.Update(rb != null && phase == Phase.Driving && link.MinecraftAlive, (float)map.Scale))
            {
                if (rb != null) link.WriteColony(colony.Count, colony.Colors, colony.Caps, colony.Speeds, colony.Emerges, colony.Happiness, colony.Names, colony.Average, colony.Current);
                else link.WriteColony(0, colony.Colors, colony.Caps, colony.Speeds, colony.Emerges, colony.Happiness, colony.Names, -1f, -1);
            }

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
            keyHints.Update(keys.Active, hintCount, bindKinds, bindCodes);
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
            bool overlayBlocked = controls && !controlsWhy.EndsWith("knockback")
                                  && (controlsWhy == "paused" || Time.realtimeSinceStartup - controlsSince >= OverlayControlsGrace);
            birth.Update(alive && phase != Phase.Idle); // a spore grown in the shop: no HUD, no overlay
            overlay.Update(link, McConfig.Overlay && alive && phase != Phase.Idle && !overlayBlocked && !covered && !birth.On);
            // Minecraft's pause menu (Escape) pauses Mycelium too.
            FreezeForMinecraftPause(alive && phase == Phase.Driving && camFresh2 && (lastCamMc.Flags & Proto.McPaused) != 0);
            pauseBlur.Update();
            // Third person: your Minecraft body at the spore's feet, instead of the spore.
            bool bodyWanted = McConfig.Overlay && alive && phase != Phase.Idle && rb != null && cameraSync.Controlling;
            playerModel.Update(link, bodyWanted, rb != null ? rb.transform.position - Vector3.up * feetOffset : Vector3.zero, (float)map.Scale);
            playerModel.SetVanished(vanished);
            cameraSync.ReplaceSpore = playerModel.Shown;
            // The other spores as Minecraft characters too, while yours is one.
            colonyBodies.Update(link, playerModel.Shown, colony, playerModel, feetOffset, (float)map.Scale);
            // Which game owns which key, while Minecraft drives the spore.
            // (Re-sync teleports count as driving: giving Mycelium its keys back for those few frames
            // let a click become Mycelium's attack right after a hit, your report 2026-10-19.)
            keys.Update(alive && phase != Phase.Idle, mcScreenOpen);
            // Combat: Mycelium's enemies to Minecraft's stand-ins, and their hits back.
            // Your weapons: the bag, Minecraft's carrots, the one Minecraft's hand holds.
            if (rb != null) weapons.Update(this, link, alive && phase == Phase.Driving && camFresh2, camFresh2 ? lastCamMc.HandWeapon : 0u, map);
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

            if (phase == Phase.Idle || mc.Epoch != lastEpoch)
            {
                bool newWorld = phase != Phase.Idle;
                lastEpoch = mc.Epoch;
                SendTeleport(newWorld ? "Minecraft entered a new world" : "start", setFacing: true);
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
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", $"grid_v{ground.Version}.csv"));
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
            SetPhase(Phase.Idle, "new player");
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

        const float OverlayControlsGrace = 0.3f; // Mycelium must keep the controls this long before the overlay hides
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
            bool now = wc != null && t.GetField("isCycloning")?.GetValue(wc) is bool c && c;
            if (now != wasCycloning)
            {
                wasCycloning = now;
                if (now) cycloneSince = Time.time;
                McLog.Info(now ? "cyclone: Living Cyclone started; Minecraft goes to third person from first person"
                               : $"cyclone: Living Cyclone over after {Time.time - cycloneSince:F2} s");
            }
            return now;
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
                Shader probe = Resources.Load<Shader>("MyceliumCraftDepthProbe");
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
                    int drawn = 0;
                    foreach (Renderer r in colony.Spores[i].GetComponentsInChildren<Renderer>(true)) if (!r.forceRenderingOff) drawn++;
                    McLog.Info($"spore swap test: {colony.Names[i]}{(i == colony.Current ? " (you)" : "")}: {drawn} spore renderers drawn (want 0)");
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
            link?.Dispose();
        }
    }
}
