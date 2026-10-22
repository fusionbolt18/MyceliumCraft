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
	public static final int T_MAX = 256;
	public static final long T_DATA = 0x2000;
	public static final long T_DATA_BYTES = TEXTURES_BYTES - T_DATA;

	// HitBoxes (Minecraft -> Mycelium, seqlock): each stand-in's box as Minecraft's client has it
	// (what the crosshair picks), for -mcShowHitboxes. Minecraft coordinates.
	public static final long OFF_HIT_BOXES = OFF_TEXTURES + TEXTURES_BYTES;
	public static final long HIT_BOXES_BYTES = 0x1000;
	public static final long HB_SEQ = 0x00;
	public static final long HB_COUNT = 0x04;
	public static final long HB_LIST = 0x10;
	public static final int HB_MAX = 64;
	public static final long HB_SIZE = 0x20; // u32 enemy id, f32 min x, y, z, max x, y, z, pad

	// Weapons (Mycelium -> Minecraft, seqlock): your weapons, one carrot each (since v24). UTF-8 strings, zero-terminated.
	public static final long OFF_WEAPONS = OFF_HIT_BOXES + HIT_BOXES_BYTES;
	public static final long WEAPONS_BYTES = 0x4000;
	public static final long W_SEQ = 0x00, W_COUNT = 0x04, W_VERSION = 0x08, W_SELECT_SEQ = 0x0C, W_SELECT_ID = 0x10, W_HELD_ID = 0x14;
	public static final long W_DAMAGE_SCALE = 0x18; // f32: Mycelium damage per Minecraft damage point
	public static final long W_LIST = 0x20;
	public static final int W_MAX = 32;
	public static final long W_SIZE = 0x100;
	public static final long WE_ID = 0x00, WE_TYPE = 0x04, WE_DAMAGE = 0x08, WE_PER_SECOND = 0x0C, WE_KNOCKBACK = 0x10, WE_DAMAGE_MULT = 0x14, WE_SPEED_MOD = 0x18;
	public static final long WE_REACH = 0x1C; // f32 blocks: how far the weapon reaches in its attack (0 = not measured yet); since v28
	public static final long WE_NAME = 0x20, WE_ATTS = 0x60;
	public static final int WE_NAME_BYTES = 0x40, WE_ATTS_BYTES = 0xA0;

	// Thrown (Minecraft -> Mycelium, seqlock): your weapons flying through Minecraft's world as items,
	// for Mycelium to show the real weapon on the same path (since v27).
	public static final long OFF_THROWN = OFF_WEAPONS + WEAPONS_BYTES;
	public static final long THROWN_BYTES = 0x400;
	public static final long THR_SEQ = 0x00, THR_COUNT = 0x04, THR_LIST = 0x10;
	public static final int THR_MAX = 16;
	public static final long THR_SIZE = 0x20; // u32 weapon id, u32 flags, f64 x, y, z (Minecraft, the item's feet)
	public static final int THR_LANDED = 1 << 0; // resting on the ground

	public static final long MAPPING_BYTES = OFF_THROWN + THROWN_BYTES;
	public static final int MAGIC = 0x4343594D; // "MYCC"
	public static final int VERSION = 29;
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
	public static final long MY_HEALTH = 0x50;     // Mycelium's current health (its own units); since v16
	public static final long MY_MAX_HEALTH = 0x54; // Mycelium's max health; 0 = no spore health known
	public static final long MY_NUTRIENTS = 0x58;  // Mycelium's nutrients (i32); -1 = not known; since v19
	public static final long MY_EMERGE = 0x5C; // how far through Mycelium's sprout (0..1); -1 = no sprout; since v22
	public static final long MY_SPEED = 0x60; // the spore's walking speed / its base speed (Speed stat, speed effects); 0 = not known; since v26
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
	public static final long MC_FOV_SCALE = 0x84; // Minecraft's FOV multiplier now (sprint, speed, bow, spyglass); 0 = not sent
	public static final long MC_EYE_SCALE = 0x88; // eye height now / standing eye height (crouching ~0.78); 0 = not sent
	public static final long MC_DAMAGE_TILT = 0x8C; // Minecraft's "Damage Tilt" setting, 0 (off) .. 1; since v20
	public static final long MC_HAND_WEAPON = 0x90; // u32: the weapon whose carrot is in the main hand (Weapons id); 0 = none; since v24
	public static final long MC_SWINGS = 0x94; // u32: melee attacks Minecraft has started (counts up); since v25
	public static final int MC_IN_WORLD = 1 << 0;
	public static final int MC_SCREEN_OPEN = 1 << 1;
	public static final int MC_ON_GROUND = 1 << 2;
	public static final int MC_SNEAKING = 1 << 3;
	public static final int MC_SPRINTING = 1 << 4;
	public static final int MC_PAUSED = 1 << 5; // Minecraft's pause menu has paused the game (Mycelium freezes too)

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
	public static final int IN_CURSOR = 1 << 1; // a Minecraft screen is open: cursor position below is valid
	public static final long IN_CURSOR_X = 0x170; // f64, 0..1 from the left of the picture
	public static final long IN_CURSOR_Y = 0x178; // f64, 0..1 from the top
	public static final String[] IN_REASONS = {"active", "Mycelium window not focused", "Mycelium has the controls (menu, pause or cutscene)", "Minecraft isn't driving the spore", "bridge turned off",
		"a hit's knockback has the spore: keys let go, mouse buttons kept"};
	public static final int IN_REASON_KNOCKBACK = 5; // since v29

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

	// Enemies @0x700 (Mycelium -> Minecraft, seqlock): Mycelium's enemies near the spore, for
	// Minecraft's invisible stand-ins. Positions in Minecraft coordinates (feet), sizes in blocks.
	public static final long OFF_ENEMIES = 0x700;
	public static final long E_SEQ = 0x00;
	public static final long E_COUNT = 0x04;
	public static final long E_LIST = 0x10;
	public static final int E_MAX = 40;
	public static final long E_SIZE = 0x30; // u32 id, u32 flags, f32 x, y, z, width, height, health, max health, yaw
	public static final long EN_ID = 0x00, EN_FLAGS = 0x04, EN_X = 0x08, EN_Y = 0x0C, EN_Z = 0x10, EN_WIDTH = 0x14, EN_HEIGHT = 0x18,
		EN_HEALTH = 0x1C, EN_MAX_HEALTH = 0x20, EN_YAW = 0x24;
	public static final int EN_ALIVE = 1 << 0;

	// Hits @0xF00 (Minecraft -> Mycelium): a ring of the last 8 hits on stand-ins. Minecraft writes
	// the entry, then bumps the count; Mycelium handles every entry up to the count.
	public static final long OFF_HITS = 0xF00;
	public static final long H_COUNT = 0x00; // hits written so far (u32)
	public static final long H_LIST = 0x10;
	public static final int H_RING = 8;
	public static final long H_SIZE = 0x10; // u32 enemy id, f32 damage (Minecraft points, after Minecraft's own sums), u32 flags, pad
	public static final int HIT_CRIT = 1 << 0;

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
