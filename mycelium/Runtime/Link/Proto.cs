// Byte layout of the shared memory. Mirrors protocol/PROTOCOL.md and
// fabric/src/main/java/dev/myceliumcraft/link/Proto.java. No UnityEngine here, so
// tools/FakeMycelium can compile this file too.
namespace MyceliumCraft.Link
{
    public static class Proto
    {
        public const string MappingName = "Local\\MyceliumCraft_v1";
        public const int MappingBytes = 0x1000;
        public const uint Magic = 0x4343594D; // "MYCC"
        public const uint Version = 1;
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
        public const int MyFrame = 0x28;
        public const uint MycInLevel = 1u << 0;
        public const uint MycPaused = 1u << 1;

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
        public const uint McInWorld = 1u << 0;
        public const uint McScreenOpen = 1u << 1;
        public const uint McOnGround = 1u << 2;
        public const uint McSneaking = 1u << 3;
        public const uint McSprinting = 1u << 4;
    }
}
