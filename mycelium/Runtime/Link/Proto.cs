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

        public const int MappingBytes = OffTextures + TexturesBytes;
        public const uint Magic = 0x4343594D; // "MYCC"
        public const uint Version = 16;
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
        public const uint MycInLevel = 1u << 0;
        public const uint MycPaused = 1u << 1;
        public const uint MycOverlay = 1u << 2; // Mycelium shows Minecraft's overlay: Minecraft skips drawing its world

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
