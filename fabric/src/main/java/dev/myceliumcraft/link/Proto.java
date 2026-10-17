package dev.myceliumcraft.link;

/** Byte layout of the shared memory. Mirrors protocol/PROTOCOL.md and mycelium/Runtime/Link/Proto.cs. */
public final class Proto {
	public static final String MAPPING_NAME = "Local\\MyceliumCraft_v1";
	public static final long MAPPING_BYTES = 0x1000;
	public static final int MAGIC = 0x4343594D; // "MYCC"
	public static final int VERSION = 1;
	/** A side is alive if its heartbeat is younger than this. */
	public static final long HEARTBEAT_TIMEOUT_MS = 3000;

	// Header @0x000
	public static final long OFF_HEADER = 0x000;
	public static final long H_MAGIC = 0x00;
	public static final long H_VERSION = 0x04;
	public static final long H_MYC_PID = 0x08;
	public static final long H_MC_PID = 0x0C;
	public static final long H_MYC_HEARTBEAT = 0x10;
	public static final long H_MC_HEARTBEAT = 0x18;

	// MycState @0x100 (Mycelium -> Minecraft, seqlock)
	public static final long OFF_MYC_STATE = 0x100;
	public static final long MY_SEQ = 0x00;
	public static final long MY_FLAGS = 0x04;
	public static final long MY_X = 0x08;
	public static final long MY_Y = 0x10;
	public static final long MY_Z = 0x18;
	public static final long MY_YAW = 0x20;
	public static final long MY_FRAME = 0x28;
	public static final int MYC_IN_LEVEL = 1 << 0;
	public static final int MYC_PAUSED = 1 << 1;

	// McState @0x200 (Minecraft -> Mycelium, seqlock)
	public static final long OFF_MC_STATE = 0x200;
	public static final long MC_SEQ = 0x00;
	public static final long MC_FLAGS = 0x04;
	public static final long MC_X = 0x08;
	public static final long MC_Y = 0x10;
	public static final long MC_Z = 0x18;
	public static final long MC_YAW = 0x20;
	public static final long MC_PITCH = 0x24;
	public static final long MC_FRAME = 0x28;
	public static final long MC_EPOCH = 0x30;
	public static final int MC_IN_WORLD = 1 << 0;
	public static final int MC_SCREEN_OPEN = 1 << 1;
	public static final int MC_ON_GROUND = 1 << 2;
	public static final int MC_SNEAKING = 1 << 3;
	public static final int MC_SPRINTING = 1 << 4;

	private Proto() {
	}
}
