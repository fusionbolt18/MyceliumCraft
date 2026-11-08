// Byte layout of the shared memory. Mirrors protocol/PROTOCOL.md and
// fabric/src/main/java/dev/myceliumcraft/link/Proto.java. No UnityEngine here, so
// tools/FakeMycelium can compile this file too.
namespace MyceliumCraft.Link
{
    public static class Proto
    {
        public const string MappingName = "Local\\MyceliumCraft_v1";
        // The overlay's 3 picture slots come after the first 1 MB.
        public const int MaxOverlayW = 3840;
        public const int MaxOverlayH = 2160;
        public const int OverlaySlotBytes = MaxOverlayW * MaxOverlayH * 4;
        public const int OverlaySlots = 3;
        public const int OffOverlayPixels = 0x100000;
        // Model (Minecraft -> Mycelium, seqlock): the player's posed body for third person.
        public const int OffModel = OffOverlayPixels + OverlaySlots * OverlaySlotBytes;
        public const int ModelBytes = 0x100000;
        public const int MSeq = 0x00;
        public const int MFlags = 0x04;
        public const int MBatches = 0x08;
        public const int MVertices = 0x0C;
        public const int MFrame = 0x10;
        public const int MBatchTable = 0x40;  // MMaxBatches x (u32 texture id, u32 first vertex, u32 vertex count, u32 pad)
        public const int MMaxBatches = 32;
        public const int MVertexData = 0x400;
        public const int MVertexBytes = 24;   // f32 x, y, z, u, v; u8 r, g, b, a
        public const int MMaxVertices = (ModelBytes - MVertexData) / MVertexBytes;
        public const uint MVisible = 1u << 0;
        public const uint MFirstPerson = 1u << 1; // Minecraft is in first person: draw only the body's shadow

        // Textures (Minecraft -> Mycelium): written once each; an entry is valid when its version is set.
        public const int OffTextures = OffModel + ModelBytes;
        public const int TexturesBytes = 0x800000;
        public const int TTable = 0x00;       // TMax x 0x20: u32 version, u32 width, u32 height, u32 pad, u64 data offset (from TData)
        public const int TEntrySize = 0x20;
        public const int TMax = 256;
        public const int TData = 0x2000;
        public const int TDataBytes = TexturesBytes - TData;

        // HitBoxes (Minecraft -> Mycelium, seqlock): each stand-in's box as Minecraft's client has it
        // (what the crosshair picks), for -mcShowHitboxes. Minecraft coordinates.
        public const int OffHitBoxes = OffTextures + TexturesBytes;
        public const int HitBoxesBytes = 0x1000;
        public const int HbSeq = 0x00;
        public const int HbCount = 0x04;
        public const int HbList = 0x10;
        public const int HbMax = 64;
        public const int HbSize = 0x20;       // u32 enemy id, f32 min x, y, z, max x, y, z, pad

        // Weapons (Mycelium -> Minecraft, seqlock): your weapons, one carrot each in Minecraft's
        // inventory (WeaponBag; since v24). Strings are UTF-8, zero-terminated.
        public const int OffWeapons = OffHitBoxes + HitBoxesBytes;
        public const int WeaponsBytes = 0x4000;
        public const int WSeq = 0x00;
        public const int WCount = 0x04;
        public const int WVersion = 0x08;     // changes whenever a weapon or a number changes
        public const int WSelectSeq = 0x0C;   // changes when Mycelium asks Minecraft to select a weapon (after a pickup)
        public const int WSelectId = 0x10;    // ... this one
        public const int WHeldId = 0x14;      // the weapon the spore holds
        public const int WDamageScale = 0x18; // f32: Mycelium damage per Minecraft damage point (damageScale=)
        public const int WList = 0x20;
        public const int WMax = 32;
        public const int WSize = 0x120;       // u32 id, u32 type (0 Slash, 1 Smash, 2 Stab), f32 damage per swing (Mycelium), swings/s, knockback, damage mult, speed mod, reach, name, attributes, key (0x120 since v30)
        public const int WeId = 0x00, WeType = 0x04, WeDamage = 0x08, WePerSecond = 0x0C, WeKnockback = 0x10, WeDamageMult = 0x14, WeSpeedMod = 0x18;
        public const int WeReach = 0x1C; // f32 blocks: how far the weapon reaches in its attack (0 = not measured yet); since v28
        public const int WeName = 0x20, WeNameBytes = 0x40, WeAtts = 0x60, WeAttsBytes = 0xA0;
        public const int WeKey = 0x100, WeKeyBytes = 0x20; // utf8: the weapon's prefab name (Resources/<Type>/<key>), for its look; since v30

        // Thrown (Minecraft -> Mycelium, seqlock): your weapons flying through Minecraft's world as
        // items, for Mycelium to show the real weapon on the same path (since v27).
        public const int OffThrown = OffWeapons + WeaponsBytes;
        public const int ThrownBytes = 0x400;
        public const int ThrSeq = 0x00, ThrCount = 0x04, ThrList = 0x10;
        public const int ThrMax = 16;
        public const int ThrSize = 0x20;        // u32 weapon id, u32 flags, f64 x, y, z (Minecraft, the item's feet)
        public const uint ThrLanded = 1u << 0;  // resting on the ground
        public const uint ThrResource = 1u << 1; // a resource of yours (MycState's list), not a weapon; since v39

        // Colony (Mycelium -> Minecraft, seqlock): every spore in your colony, for Minecraft's player
        // list (since v42).
        public const int OffColony = OffThrown + ThrownBytes;
        public const int ColonyBytes = 0x800;
        public const int ColSeq = 0x00, ColCount = 0x04, ColAverage = 0x08, ColCurrent = 0x0C, ColList = 0x10;
        public const int ColMax = 24;
        public const int ColNutrientBonus = 0x730; // i32, just after the list: Mycelium's nutrient bonus in percent; -1 = not in a run
        public const int ColSize = 0x4C;           // u32 body colour 0xRRGGBB, f32 happiness 0..1, utf8[56] name, u32 cap colour, f32 walking speed (blocks/s), f32 sprout 0..1 (-1 = none)
        public const int CeColor = 0x00, CeHappiness = 0x04, CeName = 0x08, CeNameBytes = 0x38, CeCap = 0x40, CeSpeed = 0x44, CeEmerge = 0x48;

        // Bodies (Minecraft -> Mycelium, a seqlock per slot): the other spores' posed bodies, slot =
        // colony index (since v43). Vertices as in the Model block, all with one texture.
        public const int OffBodies = OffColony + ColonyBytes;
        public const int BodySlotBytes = 0x4000;
        public const int BodiesBytes = ColMax * BodySlotBytes;
        public const int BoSeq = 0x00, BoTexture = 0x04, BoVertices = 0x08, BoFrame = 0x10, BoVertexData = 0x40;
        public const int BoMaxVertices = (BodySlotBytes - BoVertexData) / MVertexBytes;

        // SporeMoves: where the other spores stand (Mycelium -> Minecraft, seqlock), and how far
        // Minecraft's collision has pushed each (Minecraft -> Mycelium), since v46.
        public const int OffSporeMoves = OffBodies + BodiesBytes;
        public const int SporeMovesBytes = 0x400;
        public const int SmSeq = 0x00, SmCount = 0x04, SmPositions = 0x10, SmPushes = 0x200; // f32 x, y, z feet (y NaN = no stand-in); f32 total x, z in blocks

        // Boss (Mycelium -> Minecraft, seqlock): the final boss's bar, since v47.
        public const int OffBoss = OffSporeMoves + SporeMovesBytes;
        public const int BossBytes = 0x100;
        public const int BsSeq = 0x00, BsShown = 0x04, BsProgress = 0x08, BsName = 0x10, BsNameBytes = 0x80;

        // Forge: Blacksmith Ironspore as Minecraft's smithing screen, since v48. Mycelium writes 0x00..0xFF
        // (seqlock), Minecraft writes 0x100.. (its request; the request seq last).
        public const int OffForge = OffBoss + BossBytes;
        public const int ForgeBytes = 0x400; // 0x200 before v49
        public const int FgSeq = 0x00, FgOpenSeq = 0x04, FgHandled = 0x08, FgResult = 0x0C, FgCount = 0x10, FgFlags = 0x14, FgIds = 0x20, FgReason = 0xA0;
        public const int FgPreviewHandled = 0x18, FgPreview = 0x200; // since v49: the preview request answered, and the forged weapon as it would be (a Weapons entry)
        public const int FgMax = 32, FgReasonBytes = 0x60;
        public const uint FgAvailable = 1; // a Blacksmith that hasn't forged yet is in this level
        public const int FgRequestSeq = 0x100, FgBase = 0x104, FgDonor1 = 0x108, FgDonor2 = 0x10C;
        public const int FgPreviewSeq = 0x110, FgPreviewBase = 0x114, FgPreviewDonor1 = 0x118, FgPreviewDonor2 = 0x11C; // since v49

        public const int MappingBytes = OffForge + ForgeBytes;
        public const uint Magic = 0x4343594D; // "MYCC"
        public const uint Version = 49;
        public const ulong HeartbeatTimeoutMs = 3000;

        // Header @0x000
        public const int OffHeader = 0x000;
        public const int HMagic = 0x00;
        public const int HVersion = 0x04;
        public const int HMycPid = 0x08;
        public const int HMcPid = 0x0C;
        public const int HMycHeartbeat = 0x10;
        public const int HMcHeartbeat = 0x18;

        // MycState @0x100 (Mycelium -> Minecraft, seqlock)
        public const int OffMycState = 0x100;
        public const int MySeq = 0x00;
        public const int MyFlags = 0x04;
        public const int MyX = 0x08;
        public const int MyY = 0x10;
        public const int MyZ = 0x18;
        public const int MyYaw = 0x20;
        public const int MyTeleportSeq = 0x24;
        public const int MyFrame = 0x28;
        public const int MyTpX = 0x30;
        public const int MyTpY = 0x38;
        public const int MyTpZ = 0x40;
        public const int MyTpYaw = 0x48;
        public const int MyPlayerHeight = 0x4C;
        public const int MyHealth = 0x50;      // Mycelium's current health (its own units); since v16
        public const int MyMaxHealth = 0x54;   // Mycelium's max health; 0 = no spore health known
        public const int MyNutrients = 0x58;   // Mycelium's nutrients (i32); -1 = not known; since v19
        public const int MyEmerge = 0x5C;      // how far through Mycelium's sprout (0..1); -1 = no sprout; since v22
        public const int MySpeed = 0x60;       // the spore's walking speed / its base speed (Speed stat, speed effects); 0 = not known; since v26
        public const int MyWindowW = 0x64;     // u32: Mycelium's window size in pixels (Minecraft's follows it); 0 = not known; since v31
        public const int MyWindowH = 0x68;
        public const int MyDashX = 0x6C;       // f32: Mycelium's dash (Blitz, the roll), Minecraft blocks/s along x; 0 and 0 = none; since v32
        public const int MyDashZ = 0x70;       // f32: the same along z
        public const int MyResourceCount = 0x74; // u32: how many resources you carry (at most ResMax); since v39 (v38: the one resource)
        public const int MyResources = 0x78;   // u32[ResMax]: each (id << 4) | type, type 1 Rotten Log, 2 Fresh Exoskeleton, 3 Calcite Deposit, 4 Flesh; since v39
        public const int ResMax = 16;
        public const int MySporeName = 0xC0;   // utf8[64]: the name of the spore you control (CharacterStats.sporeName); "" = none; since v41
        public const int MySporeNameBytes = 64;
        public const uint MycInLevel = 1u << 0;
        public const uint MycPaused = 1u << 1;
        public const uint MycOverlay = 1u << 2; // Mycelium shows Minecraft's overlay: Minecraft skips drawing its world
        public const uint MycVanished = 1u << 3; // the spore has vanished (Trophic Cascade): third person behind from first person; since v33
        public const uint MycCyclone = 1u << 4;  // Living Cyclone is spinning: third person behind from first person; since v34
        public const uint MycRoll = 1u << 5;     // the dodge roll is on (the spore's Animator "Roll"): Minecraft plays its roll animation; since v37

        // McState @0x200 (Minecraft -> Mycelium, seqlock)
        public const int OffMcState = 0x200;
        public const int McSeq = 0x00;
        public const int McFlagsOff = 0x04;
        public const int McX = 0x08;
        public const int McY = 0x10;
        public const int McZ = 0x18;
        public const int McYaw = 0x20;
        public const int McPitch = 0x24;
        public const int McFrame = 0x28;
        public const int McEpoch = 0x30;
        public const int McTeleportAck = 0x34;
        public const int McCameraMode = 0x38; // 0 first person, 1 third person behind, 2 third person in front
        public const int McBodyYaw = 0x3C;    // where the body faces (Minecraft degrees)
        public const int McTickQpc = 0x40;    // QueryPerformanceCounter at the latest tick
        public const int McPrevX = 0x48, McPrevY = 0x50, McPrevZ = 0x58; // feet at the previous tick
        public const int McCurX = 0x60, McCurY = 0x68, McCurZ = 0x70;    // feet at the latest tick
        public const int McTickMs = 0x78;     // milliseconds per tick; 0 = no tick data
        public const int McBodyYawO = 0x7C, McBodyYawCur = 0x80;        // body yaw at the previous / latest tick
        public const int McFovScale = 0x84;   // Minecraft's FOV multiplier now (sprint, speed, bow, spyglass); 0 = not sent
        public const int McEyeScale = 0x88;   // eye height now / standing eye height (crouching ~0.78); 0 = not sent
        public const int McDamageTilt = 0x8C; // Minecraft's "Damage Tilt" setting, 0 (off) .. 1; since v20
        public const int McHandWeapon = 0x90; // u32: the weapon (Weapons table id) whose carrot is in Minecraft's main hand; 0 = none; since v24
        public const int McSwings = 0x94;     // u32: melee attacks Minecraft has started (counts up); since v25
        public const int McMoveX = 0x98, McMoveZ = 0x9C; // f32 x2: the held movement keys' direction (Minecraft world, unit; 0, 0 = none); since v36
        public const uint McInWorld = 1u << 0;
        public const uint McScreenOpen = 1u << 1;
        public const uint McOnGround = 1u << 2;
        public const uint McSneaking = 1u << 3;
        public const uint McSprinting = 1u << 4;
        public const uint McPaused = 1u << 5;    // Minecraft's pause menu has paused the game (Mycelium freezes too)

        // MycInput @0x300 (Mycelium -> Minecraft, seqlock): the input bridge
        public const int OffInput = 0x300;
        public const int InSeq = 0x00;
        public const int InFlags = 0x04;
        public const int InReason = 0x08;
        public const int InButtons = 0x0C;
        public const int InKeys = 0x10;      // 64 bytes, bit per SDL scancode
        public const int InTotalDX = 0x50;
        public const int InTotalDY = 0x58;
        public const int InTotalScroll = 0x60;
        public const int InTextCount = 0x68;
        public const int InText = 0x70;      // 64 x u32
        public const int InKeyBytes = 64;
        public const int InTextRing = 64;
        public const uint InActive = 1u << 0;
        public const uint InCursor = 1u << 1;  // a Minecraft screen is open: cursor position below is valid
        public const int InCursorX = 0x170;    // f64, 0..1 from the left of the picture
        public const int InCursorY = 0x178;    // f64, 0..1 from the top
        public const uint ReasonActive = 0, ReasonNotFocused = 1, ReasonMyceliumControls = 2, ReasonNotDriving = 3, ReasonOff = 4;
        public const uint ReasonKnockback = 5; // a hit's knockback has the spore: keys let go, mouse buttons kept (since v29)

        // Bindings @0x500 (Minecraft -> Mycelium, seqlock): what Mycelium's actions are bound to,
        // as set in Minecraft's Controls menu
        public const int OffBindings = 0x500;
        public const int BSeq = 0x00;
        public const int BCount = 0x04;      // 0 = Minecraft hasn't sent any yet
        public const int BEntries = 0x10;    // BMax x (u32 kind, u32 code), index = action id
        public const int BMax = 32;
        public const uint BindNone = 0, BindKey = 1, BindMouse = 2; // key: SDL scancode; mouse: SDL button (1 left, 2 middle, 3 right)

        // Overlay @0x600 (Minecraft -> Mycelium): triple buffer of Minecraft's picture (hand, HUD,
        // screens on a transparent background). state bits 0-1: the "middle" slot; bit 2: the
        // middle slot holds a picture we haven't taken yet.
        public const int OffOverlayCtl = 0x600;
        public const int OcState = 0x00;
        public const int OcFramesPublished = 0x08;
        public const int OffOverlaySlotHdr = 0x640; // 3 x 0x40
        public const int SlotHdrSize = 0x40;
        public const int ShWidth = 0x00;
        public const int ShHeight = 0x04;
        public const int ShFlags = 0x08;     // bit0: rows bottom-up
        public const int ShFrameId = 0x10;
        public const int OverlayDirty = 1 << 2;

        // Enemies @0x700 (Mycelium -> Minecraft, seqlock): Mycelium's enemies near the spore, for
        // Minecraft's invisible stand-ins. Positions in Minecraft coordinates (feet), sizes in blocks.
        public const int OffEnemies = 0x700;
        public const int ESeq = 0x00;
        public const int ECount = 0x04;
        public const int EList = 0x10;
        public const int EMax = 40;
        public const int ESize = 0x30;          // u32 id, u32 flags, f32 x, y, z, width, height, health, max health, yaw
        public const int EnId = 0x00, EnFlags = 0x04, EnX = 0x08, EnY = 0x0C, EnZ = 0x10, EnWidth = 0x14, EnHeight = 0x18,
                         EnHealth = 0x1C, EnMaxHealth = 0x20, EnYaw = 0x24;
        public const uint EnAlive = 1u << 0;

        // Hits @0xF00 (Minecraft -> Mycelium): a ring of the last 8 hits on stand-ins.
        public const int OffHits = 0xF00;
        public const int HCount = 0x00;          // hits written so far (u32)
        public const int HList = 0x10;
        public const int HRing = 8;
        public const int HSize = 0x10;           // u32 enemy id, f32 damage (Minecraft points), u32 flags, pad
        public const uint HitCrit = 1u << 0;

        // GroundGrid @0x1000 (Mycelium -> Minecraft, seqlock)
        public const int OffGround = 0x1000;
        public const int GSeq = 0x00;
        public const int GVersion = 0x04;
        public const int GN = 0x08;
        public const int GOriginX = 0x10;
        public const int GOriginZ = 0x18;
        public const int GCell = 0x20;
        public const int GHeights = 0x40;
        public const int GMaxN = 128;
        public const int GPieces = 4;        // solid pieces per cell
        public const int GFloatsPerCell = GPieces * 2; // (bottom, top) per piece
        public const float FarBelow = -1e30f; // a piece's bottom: "from far below"
        public const float Unused = 1e30f;    // bottom and top of an unused piece
    }
}
