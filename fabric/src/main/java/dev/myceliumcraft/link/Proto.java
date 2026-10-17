package dev.myceliumcraft.link;

/** Byte layout of the shared memory. Mirrors protocol/PROTOCOL.md and mycelium/Runtime/Link/Proto.cs. */
public final class Proto {
	public static final String MAPPING_NAME = "Local\\MyceliumCraft_v1";
	public static final long MAPPING_BYTES = 0x100000;
	public static final int MAGIC = 0x4343594D; // "MYCC"
	public static final int VERSION = 5;
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
	public static final long MY_TELEPORT_SEQ = 0x24;
	public static final long MY_FRAME = 0x28;
	public static final long MY_TP_X = 0x30;
	public static final long MY_TP_Y = 0x38;
	public static final long MY_TP_Z = 0x40;
	public static final long MY_TP_YAW = 0x48;
	public static final long MY_PLAYER_HEIGHT = 0x4C;
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
	public static final long MC_TELEPORT_ACK = 0x34;
	public static final long MC_CAMERA_MODE = 0x38; // Minecraft CameraType ordinal: 0 first person, 1 behind, 2 in front
	public static final int MC_IN_WORLD = 1 << 0;
	public static final int MC_SCREEN_OPEN = 1 << 1;
	public static final int MC_ON_GROUND = 1 << 2;
	public static final int MC_SNEAKING = 1 << 3;
	public static final int MC_SPRINTING = 1 << 4;

	// GroundGrid @0x1000 (Mycelium -> Minecraft, seqlock)
	public static final long OFF_GROUND = 0x1000;
	public static final long G_SEQ = 0x00;
	public static final long G_VERSION = 0x04;
	public static final long G_N = 0x08;
	public static final long G_ORIGIN_X = 0x10;
	public static final long G_ORIGIN_Z = 0x18;
	public static final long G_CELL = 0x20;
	public static final long G_HEIGHTS = 0x40;
	public static final int G_MAX_N = 128;
	public static final int G_PIECES = 4; // solid pieces per cell
	public static final int G_FLOATS_PER_CELL = G_PIECES * 2; // (bottom, top) per piece
	public static final float FAR_BELOW = -1e30f; // a piece's bottom: "from far below"
	public static final float UNUSED = 1e30f; // bottom and top of an unused piece

	private Proto() {
	}
}
