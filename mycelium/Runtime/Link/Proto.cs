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
        public const int MappingBytes = OffOverlayPixels + OverlaySlots * OverlaySlotBytes;
        public const uint Magic = 0x4343594D; // "MYCC"
        public const uint Version = 10;
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
        public const uint McInWorld = 1u << 0;
        public const uint McScreenOpen = 1u << 1;
        public const uint McOnGround = 1u << 2;
        public const uint McSneaking = 1u << 3;
        public const uint McSprinting = 1u << 4;

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
