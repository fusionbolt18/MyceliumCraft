package dev.myceliumcraft.link;

/** Byte layout of the shared memory. Mirrors protocol/PROTOCOL.md and mycelium/Runtime/Link/Proto.cs. */
public final class Proto {
	public static final String MAPPING_NAME = "Local\\MyceliumCraft_v1";
	// The overlay's 3 picture slots come after the first 1 MB.
	public static final int MAX_OVERLAY_W = 3840;
	public static final int MAX_OVERLAY_H = 2160;
	public static final long OVERLAY_SLOT_BYTES = (long) MAX_OVERLAY_W * MAX_OVERLAY_H * 4;
	public static final int OVERLAY_SLOTS = 3;
	public static final long OFF_OVERLAY_PIXELS = 0x100000;
	// Model (Minecraft -> Mycelium, seqlock): the player's posed body for third person.
	public static final long OFF_MODEL = OFF_OVERLAY_PIXELS + OVERLAY_SLOTS * OVERLAY_SLOT_BYTES;
	public static final long MODEL_BYTES = 0x100000;
	public static final long M_SEQ = 0x00;
	public static final long M_FLAGS = 0x04;
	public static final long M_BATCHES = 0x08;
	public static final long M_VERTICES = 0x0C;
	public static final long M_FRAME = 0x10;
	public static final long M_BATCH_TABLE = 0x40; // M_MAX_BATCHES x (u32 texture id, u32 first vertex, u32 vertex count, u32 pad)
	public static final int M_MAX_BATCHES = 32;
	public static final long M_VERTEX_DATA = 0x400;
	public static final int M_VERTEX_BYTES = 24; // f32 x, y, z, u, v; u8 r, g, b, a
	public static final int M_MAX_VERTICES = (int) ((MODEL_BYTES - M_VERTEX_DATA) / M_VERTEX_BYTES);
	public static final int M_VISIBLE = 1 << 0;
	public static final int M_FIRST_PERSON = 1 << 1; // Minecraft is in first person: draw only the body's shadow

	// Textures (Minecraft -> Mycelium): written once each; an entry is valid when its version is set.
	public static final long OFF_TEXTURES = OFF_MODEL + MODEL_BYTES;
	public static final long TEXTURES_BYTES = 0x800000;
	public static final long T_TABLE = 0x00; // T_MAX x 0x20: u32 version, u32 width, u32 height, u32 pad, u64 data offset (from T_DATA)
	public static final long T_ENTRY_SIZE = 0x20;
	public static final int T_MAX = 32;
	public static final long T_DATA = 0x400;
	public static final long T_DATA_BYTES = TEXTURES_BYTES - T_DATA;

	public static final long MAPPING_BYTES = OFF_TEXTURES + TEXTURES_BYTES;
	public static final int MAGIC = 0x4343594D; // "MYCC"
	public static final int VERSION = 12;
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
	public static final int MYC_OVERLAY = 1 << 2; // Mycelium shows Minecraft's overlay: Minecraft skips drawing its world

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
	public static final long MC_BODY_YAW = 0x3C; // where the body faces (Minecraft degrees, interpolated)
	public static final long MC_TICK_QPC = 0x40; // QueryPerformanceCounter at the latest tick
	public static final long MC_PREV_X = 0x48, MC_PREV_Y = 0x50, MC_PREV_Z = 0x58; // feet at the previous tick
	public static final long MC_CUR_X = 0x60, MC_CUR_Y = 0x68, MC_CUR_Z = 0x70; // feet at the latest tick
	public static final long MC_TICK_MS = 0x78; // milliseconds per tick; 0 = no tick data
	public static final long MC_BODY_YAW_O = 0x7C, MC_BODY_YAW_CUR = 0x80; // body yaw at the previous / latest tick
	public static final int MC_IN_WORLD = 1 << 0;
	public static final int MC_SCREEN_OPEN = 1 << 1;
	public static final int MC_ON_GROUND = 1 << 2;
	public static final int MC_SNEAKING = 1 << 3;
	public static final int MC_SPRINTING = 1 << 4;

	// MycInput @0x300 (Mycelium -> Minecraft, seqlock): the input bridge
	public static final long OFF_INPUT = 0x300;
	public static final long IN_SEQ = 0x00;
	public static final long IN_FLAGS = 0x04;
	public static final long IN_REASON = 0x08;
	public static final long IN_BUTTONS = 0x0C;
	public static final long IN_KEYS = 0x10; // 64 bytes, bit per SDL scancode
	public static final long IN_TOTAL_DX = 0x50;
	public static final long IN_TOTAL_DY = 0x58;
	public static final long IN_TOTAL_SCROLL = 0x60;
	public static final long IN_TEXT_COUNT = 0x68;
	public static final long IN_TEXT = 0x70; // 64 x u32
	public static final int IN_KEY_BYTES = 64;
	public static final int IN_TEXT_RING = 64;
	public static final int IN_ACTIVE = 1 << 0;
	public static final String[] IN_REASONS = {"active", "Mycelium window not focused", "Mycelium has the controls (menu, pause or cutscene)", "Minecraft isn't driving the spore", "bridge turned off"};

	// Bindings @0x500 (Minecraft -> Mycelium, seqlock): what Mycelium's actions are bound to
	public static final long OFF_BINDINGS = 0x500;
	public static final long B_SEQ = 0x00;
	public static final long B_COUNT = 0x04; // 0 = Minecraft hasn't sent any yet
	public static final long B_ENTRIES = 0x10; // B_MAX x (u32 kind, u32 code), index = action id
	public static final int B_MAX = 32;
	public static final int BIND_NONE = 0, BIND_KEY = 1, BIND_MOUSE = 2; // key: SDL scancode; mouse: SDL button (1 left, 2 middle, 3 right)

	// Overlay @0x600 (Minecraft -> Mycelium): triple buffer of Minecraft's picture (hand, HUD,
	// screens on a transparent background), like SkyCraft's. state bits 0-1: the "middle" slot;
	// bit 2: the middle slot holds a picture Mycelium hasn't taken yet.
	public static final long OFF_OVERLAY_CTL = 0x600;
	public static final long OC_STATE = 0x00;
	public static final long OC_FRAMES_PUBLISHED = 0x08;
	public static final long OFF_OVERLAY_SLOT_HDR = 0x640; // 3 x 0x40
	public static final long SLOT_HDR_SIZE = 0x40;
	public static final long SH_WIDTH = 0x00;
	public static final long SH_HEIGHT = 0x04;
	public static final long SH_FLAGS = 0x08; // bit0: rows bottom-up
	public static final long SH_FRAME_ID = 0x10;
	public static final int OVERLAY_DIRTY = 1 << 2;

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
