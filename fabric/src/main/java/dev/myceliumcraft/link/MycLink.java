package dev.myceliumcraft.link;

import static dev.myceliumcraft.link.Proto.*;
import static java.lang.foreign.ValueLayout.*;

import java.lang.foreign.Arena;
import java.lang.foreign.FunctionDescriptor;
import java.lang.foreign.Linker;
import java.lang.foreign.MemorySegment;
import java.lang.foreign.SymbolLookup;
import java.lang.invoke.MethodHandle;
import java.lang.invoke.VarHandle;
import java.nio.charset.StandardCharsets;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

/**
 * The Minecraft end of the shared-memory link. Mycelium owns the mapping; we open it when it
 * appears and treat Mycelium as gone when its heartbeat stops. No Minecraft classes in here, so
 * the link test can run it outside the game.
 */
public final class MycLink {
	public static final Logger LOG = LoggerFactory.getLogger("MyceliumCraft");

	private static final int FILE_MAP_ALL_ACCESS = 0xF001F;
	private static final VarHandle INT = JAVA_INT.varHandle();
	private static final VarHandle LONG = JAVA_LONG.varHandle();

	private static final MethodHandle OPEN_FILE_MAPPING;
	private static final MethodHandle MAP_VIEW_OF_FILE;
	private static final MethodHandle GET_TICK_COUNT64;
	private static final MethodHandle GET_CURRENT_PROCESS_ID;
	private static final MethodHandle QUERY_PERFORMANCE_COUNTER;
	private static final MethodHandle QUERY_PERFORMANCE_FREQUENCY;
	private static final MemorySegment QPC_OUT = Arena.global().allocate(JAVA_LONG);
	private static final java.lang.foreign.StructLayout CALL_STATE = Linker.Option.captureStateLayout();
	private static final VarHandle LAST_ERROR = CALL_STATE.varHandle(java.lang.foreign.MemoryLayout.PathElement.groupElement("GetLastError"));
	private static final MemorySegment OPEN_STATE = Arena.global().allocate(CALL_STATE);

	static {
		Linker linker = Linker.nativeLinker();
		SymbolLookup k32 = SymbolLookup.libraryLookup("kernel32", Arena.global());
		OPEN_FILE_MAPPING = linker.downcallHandle(
			k32.find("OpenFileMappingW").orElseThrow(), FunctionDescriptor.of(ADDRESS, JAVA_INT, JAVA_INT, ADDRESS), Linker.Option.captureCallState("GetLastError")
		);
		MAP_VIEW_OF_FILE = linker.downcallHandle(
			k32.find("MapViewOfFile").orElseThrow(), FunctionDescriptor.of(ADDRESS, ADDRESS, JAVA_INT, JAVA_INT, JAVA_INT, JAVA_LONG)
		);
		GET_TICK_COUNT64 = linker.downcallHandle(k32.find("GetTickCount64").orElseThrow(), FunctionDescriptor.of(JAVA_LONG));
		GET_CURRENT_PROCESS_ID = linker.downcallHandle(k32.find("GetCurrentProcessId").orElseThrow(), FunctionDescriptor.of(JAVA_INT));
		QUERY_PERFORMANCE_COUNTER = linker.downcallHandle(k32.find("QueryPerformanceCounter").orElseThrow(), FunctionDescriptor.of(JAVA_INT, ADDRESS));
		QUERY_PERFORMANCE_FREQUENCY = linker.downcallHandle(k32.find("QueryPerformanceFrequency").orElseThrow(), FunctionDescriptor.of(JAVA_INT, ADDRESS));
	}

	private static volatile MemorySegment shm;
	private static long lastOpenAttempt;
	private static int lastOpenError = -1;
	private static int mycPid;
	private static boolean wasAlive;

	private MycLink() {
	}

	/** Open the mapping if we haven't yet (at most once a second) and beat our heartbeat. Call every frame. */
	public static void poll() {
		MemorySegment s = shm;
		if (s != null) {
			s.set(JAVA_INT, OFF_HEADER + H_MC_PID, pid());
			LONG.setRelease(s, OFF_HEADER + H_MC_HEARTBEAT, tickCount());
			int pid = s.get(JAVA_INT, OFF_HEADER + H_MYC_PID);
			if (pid != mycPid) {
				mycPid = pid;
				LOG.info("MyceliumCraft: Mycelium instance changed (pid {})", pid);
			}
			boolean alive = myceliumAlive();
			if (alive != wasAlive) {
				wasAlive = alive;
				LOG.info("MyceliumCraft: Mycelium is {}", alive ? "alive" : "gone (heartbeat stopped)");
			}
			return;
		}
		long now = System.currentTimeMillis();
		if (now - lastOpenAttempt < 1000) {
			return;
		}
		lastOpenAttempt = now;
		try (Arena arena = Arena.ofConfined()) {
			MemorySegment name = arena.allocateFrom(MAPPING_NAME, StandardCharsets.UTF_16LE);
			MemorySegment handle = (MemorySegment) OPEN_FILE_MAPPING.invokeExact(OPEN_STATE, FILE_MAP_ALL_ACCESS, 0, name);
			if (handle.address() == 0) {
				int error = (int) LAST_ERROR.get(OPEN_STATE, 0L);
				if (error != lastOpenError) {
					lastOpenError = error;
					LOG.info("MyceliumCraft: can't open Mycelium's shared memory yet (Windows error {}{})", error,
						error == 2 ? ": Mycelium isn't running yet" : "");
				}
				return;
			}
			MemorySegment view = (MemorySegment) MAP_VIEW_OF_FILE.invokeExact(handle, FILE_MAP_ALL_ACCESS, 0, 0, 0L);
			if (view.address() == 0) {
				LOG.error("MyceliumCraft: MapViewOfFile failed");
				return;
			}
			MemorySegment seg = view.reinterpret(MAPPING_BYTES);
			int magic = seg.get(JAVA_INT, OFF_HEADER + H_MAGIC);
			int version = seg.get(JAVA_INT, OFF_HEADER + H_VERSION);
			if (magic != MAGIC || version != VERSION) {
				LOG.error("MyceliumCraft: protocol mismatch (magic {} version {}); expected version {}", Integer.toHexString(magic), version, VERSION);
				return;
			}
			seg.set(JAVA_INT, OFF_HEADER + H_MC_PID, pid());
			LONG.setRelease(seg, OFF_HEADER + H_MC_HEARTBEAT, tickCount());
			mycPid = seg.get(JAVA_INT, OFF_HEADER + H_MYC_PID);
			shm = seg;
			linkCount++;
			LOG.info("MyceliumCraft: linked to Mycelium (pid {})", mycPid);
		} catch (Throwable t) {
			LOG.error("MyceliumCraft: failed to open shared memory", t);
		}
	}

	public static boolean mapped() {
		return shm != null;
	}

	/** True when a live Mycelium is on the other end. */
	public static boolean myceliumAlive() {
		MemorySegment s = shm;
		if (s == null) {
			return false;
		}
		long beat = (long) LONG.getAcquire(s, OFF_HEADER + H_MYC_HEARTBEAT);
		return tickCount() - beat < HEARTBEAT_TIMEOUT_MS;
	}

	/**
	 * The way held movement keys walk you, in the world (x, z; unit), as Minecraft moves the player
	 * (Entity.moveRelative): forward along the look yaw, left 90 degrees to its left. {0, 0} when
	 * none are held, or they cancel out.
	 */
	public static float[] moveDirection(boolean forward, boolean backward, boolean left, boolean right, float yaw) {
		float f = (forward ? 1f : 0f) - (backward ? 1f : 0f);
		float s = (left ? 1f : 0f) - (right ? 1f : 0f);
		if (f == 0f && s == 0f) {
			return new float[] { 0f, 0f };
		}
		double r = Math.toRadians(yaw), sin = Math.sin(r), cos = Math.cos(r);
		double dx = s * cos - f * sin, dz = f * cos + s * sin;
		double len = Math.sqrt(dx * dx + dz * dz);
		return new float[] { (float) (dx / len), (float) (dz / len) };
	}

	/** Seqlock write of McState. */
	public static void writeMcState(int flags, double x, double y, double z, float yaw, float pitch, long frame, int epoch, int teleportAck, int cameraMode, float bodyYaw, Tick tick) {
		writeMcState(flags, x, y, z, yaw, pitch, frame, epoch, teleportAck, cameraMode, bodyYaw, tick, 1f, 1f);
	}

	/** Seqlock write of McState, with Minecraft's FOV multiplier and eye-height ratio for Mycelium's camera. */
	public static void writeMcState(int flags, double x, double y, double z, float yaw, float pitch, long frame, int epoch, int teleportAck, int cameraMode, float bodyYaw, Tick tick,
		float fovScale, float eyeScale) {
		writeMcState(flags, x, y, z, yaw, pitch, frame, epoch, teleportAck, cameraMode, bodyYaw, tick, fovScale, eyeScale, 1f);
	}

	/** Seqlock write of McState, with Minecraft's camera feel and its "Damage Tilt" setting (0..1). */
	public static void writeMcState(int flags, double x, double y, double z, float yaw, float pitch, long frame, int epoch, int teleportAck, int cameraMode, float bodyYaw, Tick tick,
		float fovScale, float eyeScale, float damageTilt) {
		writeMcState(flags, x, y, z, yaw, pitch, frame, epoch, teleportAck, cameraMode, bodyYaw, tick, fovScale, eyeScale, damageTilt, 0);
	}

	/** Seqlock write of McState, with the weapon in the main hand (Weapons id, 0 = none). */
	public static void writeMcState(int flags, double x, double y, double z, float yaw, float pitch, long frame, int epoch, int teleportAck, int cameraMode, float bodyYaw, Tick tick,
		float fovScale, float eyeScale, float damageTilt, int handWeapon) {
		writeMcState(flags, x, y, z, yaw, pitch, frame, epoch, teleportAck, cameraMode, bodyYaw, tick, fovScale, eyeScale, damageTilt, handWeapon, 0);
	}

	/** Seqlock write of McState, with the count of melee attacks started (for Mycelium's attack events). */
	public static void writeMcState(int flags, double x, double y, double z, float yaw, float pitch, long frame, int epoch, int teleportAck, int cameraMode, float bodyYaw, Tick tick,
		float fovScale, float eyeScale, float damageTilt, int handWeapon, int swings) {
		writeMcState(flags, x, y, z, yaw, pitch, frame, epoch, teleportAck, cameraMode, bodyYaw, tick, fovScale, eyeScale, damageTilt, handWeapon, swings, 0f, 0f);
	}

	/** Seqlock write of McState, with the held movement keys' direction (world x, z; unit, or 0, 0 for none). */
	public static void writeMcState(int flags, double x, double y, double z, float yaw, float pitch, long frame, int epoch, int teleportAck, int cameraMode, float bodyYaw, Tick tick,
		float fovScale, float eyeScale, float damageTilt, int handWeapon, int swings, float moveX, float moveZ) {
		MemorySegment s = shm;
		if (s == null) {
			return;
		}
		long b = OFF_MC_STATE;
		int seq = s.get(JAVA_INT, b + MC_SEQ);
		INT.setRelease(s, b + MC_SEQ, seq + 1); // odd: writing
		VarHandle.storeStoreFence();
		s.set(JAVA_INT, b + MC_FLAGS, flags);
		s.set(JAVA_DOUBLE, b + MC_X, x);
		s.set(JAVA_DOUBLE, b + MC_Y, y);
		s.set(JAVA_DOUBLE, b + MC_Z, z);
		s.set(JAVA_FLOAT, b + MC_YAW, yaw);
		s.set(JAVA_FLOAT, b + MC_PITCH, pitch);
		s.set(JAVA_LONG, b + MC_FRAME, frame);
		s.set(JAVA_INT, b + MC_EPOCH, epoch);
		s.set(JAVA_INT, b + MC_TELEPORT_ACK, teleportAck);
		s.set(JAVA_INT, b + MC_CAMERA_MODE, cameraMode);
		s.set(JAVA_FLOAT, b + MC_BODY_YAW, bodyYaw);
		s.set(JAVA_LONG, b + MC_TICK_QPC, tick == null ? 0L : tick.qpc);
		s.set(JAVA_DOUBLE, b + MC_PREV_X, tick == null ? x : tick.prevX);
		s.set(JAVA_DOUBLE, b + MC_PREV_Y, tick == null ? y : tick.prevY);
		s.set(JAVA_DOUBLE, b + MC_PREV_Z, tick == null ? z : tick.prevZ);
		s.set(JAVA_DOUBLE, b + MC_CUR_X, tick == null ? x : tick.curX);
		s.set(JAVA_DOUBLE, b + MC_CUR_Y, tick == null ? y : tick.curY);
		s.set(JAVA_DOUBLE, b + MC_CUR_Z, tick == null ? z : tick.curZ);
		s.set(JAVA_FLOAT, b + MC_TICK_MS, tick == null ? 0f : tick.ms);
		s.set(JAVA_FLOAT, b + MC_BODY_YAW_O, tick == null ? bodyYaw : tick.bodyO);
		s.set(JAVA_FLOAT, b + MC_BODY_YAW_CUR, tick == null ? bodyYaw : tick.body);
		s.set(JAVA_FLOAT, b + MC_FOV_SCALE, fovScale);
		s.set(JAVA_FLOAT, b + MC_EYE_SCALE, eyeScale);
		s.set(JAVA_FLOAT, b + MC_DAMAGE_TILT, damageTilt);
		s.set(JAVA_INT, b + MC_HAND_WEAPON, handWeapon);
		s.set(JAVA_INT, b + MC_SWINGS, swings);
		s.set(JAVA_FLOAT, b + MC_MOVE_X, moveX);
		s.set(JAVA_FLOAT, b + MC_MOVE_Z, moveZ);
		putUtf8(s, b + MC_RENAME_NAME, MC_RENAME_BYTES, renameName);
		s.set(JAVA_INT, b + MC_RENAME_SEQ, renameSeq);
		INT.setRelease(s, b + MC_SEQ, seq + 2); // even: done
	}

	/** /changename (MycChangeName): the latest request's number and name, sent with every McState. Since v65. */
	public static volatile int renameSeq;
	public static volatile String renameName = "";

	private static void putUtf8(MemorySegment s, long off, int max, String text) {
		byte[] data = (text == null ? "" : text).getBytes(java.nio.charset.StandardCharsets.UTF_8);
		int n = Math.min(data.length, max - 1);
		while (n > 0 && n < data.length && (data[n] & 0xC0) == 0x80) n--; // don't cut a character in half
		for (int i = 0; i < n; i++) s.set(java.lang.foreign.ValueLayout.JAVA_BYTE, off + i, data[i]);
		s.set(java.lang.foreign.ValueLayout.JAVA_BYTE, off + n, (byte) 0);
	}

	/** Seqlock write of the Bindings block: kind and code per Mycelium action. */
	public static void writeBindings(int[] kinds, int[] codes, int count) {
		MemorySegment s = shm;
		if (s == null) {
			return;
		}
		count = Math.min(count, B_MAX);
		long b = OFF_BINDINGS;
		int seq = s.get(JAVA_INT, b + B_SEQ);
		INT.setRelease(s, b + B_SEQ, seq + 1);
		VarHandle.storeStoreFence();
		s.set(JAVA_INT, b + B_COUNT, count);
		for (int i = 0; i < count; i++) {
			s.set(JAVA_INT, b + B_ENTRIES + 8L * i, kinds[i]);
			s.set(JAVA_INT, b + B_ENTRIES + 8L * i + 4, codes[i]);
		}
		INT.setRelease(s, b + B_SEQ, seq + 2);
	}

	private static int linkCount;
	private static final int SESSION = (int) (System.nanoTime() | 1); // texture versions from this Minecraft run

	/** Goes up every time the shared memory is (re)opened. */
	public static int linkCount() {
		return linkCount;
	}

	/** Seqlock write of the player model: table = (texture, first, count) per batch; data = 6 ints per vertex. */
	public static void writeModel(int flags, int[] table, int batches, int[] data, int vertices) {
		MemorySegment s = shm;
		if (s == null) {
			return;
		}
		long b = OFF_MODEL;
		int seq = s.get(JAVA_INT, b + M_SEQ);
		INT.setRelease(s, b + M_SEQ, seq + 1);
		VarHandle.storeStoreFence();
		s.set(JAVA_INT, b + M_FLAGS, flags);
		s.set(JAVA_INT, b + M_BATCHES, batches);
		s.set(JAVA_INT, b + M_VERTICES, vertices);
		s.set(JAVA_LONG, b + M_FRAME, s.get(JAVA_LONG, b + M_FRAME) + 1);
		for (int i = 0; i < batches; i++) {
			long e = b + M_BATCH_TABLE + 16L * i;
			s.set(JAVA_INT, e, table[i * 3]);
			s.set(JAVA_INT, e + 4, table[i * 3 + 1]);
			s.set(JAVA_INT, e + 8, table[i * 3 + 2]);
		}
		if (vertices > 0) {
			MemorySegment.copy(data, 0, s, JAVA_INT, b + M_VERTEX_DATA, vertices * 6);
		}
		INT.setRelease(s, b + M_SEQ, seq + 2);
	}

	/** Seqlock write of one colony spore's body: data = 6 ints per vertex, all with one texture. 0 vertices: no body. */
	public static void writeBody(int slot, int texture, int[] data, int vertices) {
		MemorySegment s = shm;
		if (s == null || slot < 0 || slot >= COL_MAX || vertices > BO_MAX_VERTICES) {
			return;
		}
		long b = OFF_BODIES + slot * BODY_SLOT_BYTES;
		int seq = s.get(JAVA_INT, b + BO_SEQ);
		INT.setRelease(s, b + BO_SEQ, seq + 1);
		VarHandle.storeStoreFence();
		s.set(JAVA_INT, b + BO_TEXTURE, texture);
		s.set(JAVA_INT, b + BO_VERTICES, vertices);
		s.set(JAVA_LONG, b + BO_FRAME, s.get(JAVA_LONG, b + BO_FRAME) + 1);
		if (vertices > 0) {
			MemorySegment.copy(data, 0, s, JAVA_INT, b + BO_VERTEX_DATA, vertices * 6);
		}
		INT.setRelease(s, b + BO_SEQ, seq + 2);
	}

	/** Where the other spores stand: x, y, z per slot (y NaN = no stand-in). Returns how many slots, or -1 if unreadable. */
	private static volatile int sporeStandIns;

	/** How many of the last read positions get stand-ins (your colony's; since v63, the scenery spores after them don't). */
	public static int sporeStandIns() {
		return sporeStandIns;
	}

	public static int readSporePositions(float[] out) {
		MemorySegment s = shm;
		if (s == null) {
			return 0;
		}
		long b = OFF_SPORE_MOVES;
		for (int attempt = 0; attempt < 16; attempt++) {
			int seq1 = (int) INT.getAcquire(s, b + SM_SEQ);
			if ((seq1 & 1) != 0) {
				Thread.onSpinWait();
				continue;
			}
			int count = Math.max(0, Math.min(COL_MAX, s.get(JAVA_INT, b + SM_COUNT)));
			int standIns = Math.max(0, Math.min(count, s.get(JAVA_INT, b + SM_STAND_INS)));
			MemorySegment.copy(s, JAVA_FLOAT, b + SM_POSITIONS, out, 0, count * 3);
			VarHandle.loadLoadFence();
			if (seq1 == (int) INT.getVolatile(s, b + SM_SEQ)) {
				sporeStandIns = standIns;
				return count;
			}
		}
		return -1;
	}

	/** The final boss's bar, as Mycelium would show it. */
	public static final class Boss {
		public boolean shown;
		public float progress;
		public String name = "";
	}

	public static boolean readBoss(Boss out) {
		MemorySegment s = shm;
		if (s == null) {
			return false;
		}
		long b = OFF_BOSS;
		for (int attempt = 0; attempt < 16; attempt++) {
			int seq1 = (int) INT.getAcquire(s, b + BS_SEQ);
			if ((seq1 & 1) != 0) {
				Thread.onSpinWait();
				continue;
			}
			out.shown = s.get(JAVA_INT, b + BS_SHOWN) != 0;
			out.progress = s.get(JAVA_FLOAT, b + BS_PROGRESS);
			out.name = utf8(s, b + BS_NAME, BS_NAME_BYTES);
			VarHandle.loadLoadFence();
			if (seq1 == (int) INT.getVolatile(s, b + BS_SEQ)) {
				return true;
			}
		}
		return false;
	}

	/** The boss's pull on you (Boss block), blocks per second: out[0] x, out[1] z. False if not linked or unreadable. */
	/** The level's goal (Boss block, since v61): out = x, y, z in Minecraft coordinates. False = none (or unreadable). */
	public static boolean readGoal(float[] out) {
		MemorySegment s = shm;
		if (s == null) {
			return false;
		}
		long b = OFF_BOSS;
		for (int attempt = 0; attempt < 16; attempt++) {
			int seq1 = (int) INT.getAcquire(s, b + BS_SEQ);
			if ((seq1 & 1) != 0) {
				Thread.onSpinWait();
				continue;
			}
			int shown = s.get(JAVA_INT, b + BS_GOAL_SHOWN);
			out[0] = s.get(JAVA_FLOAT, b + BS_GOAL_X);
			out[1] = s.get(JAVA_FLOAT, b + BS_GOAL_Y);
			out[2] = s.get(JAVA_FLOAT, b + BS_GOAL_Z);
			VarHandle.loadLoadFence();
			if (seq1 == (int) INT.getVolatile(s, b + BS_SEQ)) {
				return shown == 1;
			}
		}
		return false;
	}

	public static boolean readBossPull(float[] out) {
		MemorySegment s = shm;
		if (s == null) {
			return false;
		}
		long b = OFF_BOSS;
		for (int attempt = 0; attempt < 16; attempt++) {
			int seq1 = (int) INT.getAcquire(s, b + BS_SEQ);
			if ((seq1 & 1) != 0) {
				Thread.onSpinWait();
				continue;
			}
			out[0] = s.get(JAVA_FLOAT, b + BS_PULL_X);
			out[1] = s.get(JAVA_FLOAT, b + BS_PULL_Z);
			VarHandle.loadLoadFence();
			if (seq1 == (int) INT.getVolatile(s, b + BS_SEQ)) {
				return true;
			}
		}
		return false;
	}

	/** The Blacksmith, as Mycelium says (Forge block). */
	public static final class Forge {
		public int openSeq, handled, result, count, flags, previewHandled;
		public final Weapon preview = new Weapon(); // the forged weapon as it would be (since v49)
		public final int[] ids = new int[FG_MAX];
		public String reason = "";
	}

	public static boolean readForge(Forge out) {
		MemorySegment s = shm;
		if (s == null) {
			return false;
		}
		long b = OFF_FORGE;
		for (int attempt = 0; attempt < 16; attempt++) {
			int seq1 = (int) INT.getAcquire(s, b + FG_SEQ);
			if ((seq1 & 1) != 0) {
				Thread.onSpinWait();
				continue;
			}
			out.openSeq = s.get(JAVA_INT, b + FG_OPEN_SEQ);
			out.handled = s.get(JAVA_INT, b + FG_HANDLED);
			out.result = s.get(JAVA_INT, b + FG_RESULT);
			out.count = Math.max(0, Math.min(FG_MAX, s.get(JAVA_INT, b + FG_COUNT)));
			out.flags = s.get(JAVA_INT, b + FG_FLAGS);
			MemorySegment.copy(s, JAVA_INT, b + FG_IDS, out.ids, 0, out.count);
			out.reason = utf8(s, b + FG_REASON, FG_REASON_BYTES);
			out.previewHandled = s.get(JAVA_INT, b + FG_PREVIEW_HANDLED);
			readWeaponEntry(s, b + FG_PREVIEW, out.preview);
			VarHandle.loadLoadFence();
			if (seq1 == (int) INT.getVolatile(s, b + FG_SEQ)) {
				return true;
			}
		}
		return false;
	}

	/** Ask Mycelium to forge: the ids first, the request seq last. */
	public static void writeForgeRequest(int seq, int baseId, int donor1, int donor2) {
		MemorySegment s = shm;
		if (s == null) {
			return;
		}
		long b = OFF_FORGE;
		s.set(JAVA_INT, b + FG_BASE, baseId);
		s.set(JAVA_INT, b + FG_DONOR1, donor1);
		s.set(JAVA_INT, b + FG_DONOR2, donor2);
		INT.setRelease(s, b + FG_REQUEST_SEQ, seq);
	}

	/** Ask Mycelium for the forged weapon's numbers as it would be: the ids first, the seq last. */
	public static void writeForgePreview(int seq, int baseId, int donor1, int donor2) {
		MemorySegment s = shm;
		if (s == null) {
			return;
		}
		long b = OFF_FORGE;
		s.set(JAVA_INT, b + FG_PREVIEW_BASE, baseId);
		s.set(JAVA_INT, b + FG_PREVIEW_DONOR1, donor1);
		s.set(JAVA_INT, b + FG_PREVIEW_DONOR2, donor2);
		INT.setRelease(s, b + FG_PREVIEW_SEQ, seq);
	}

	public static int forgePreviewSeq() {
		MemorySegment s = shm;
		return s == null ? 0 : (int) INT.getAcquire(s, OFF_FORGE + FG_PREVIEW_SEQ);
	}

	/** Minecraft's own last request seq as written (for the stand-in). */
	public static int forgeRequestSeq() {
		MemorySegment s = shm;
		return s == null ? 0 : (int) INT.getAcquire(s, OFF_FORGE + FG_REQUEST_SEQ);
	}

	/** How far Minecraft's collision has pushed this spore in all, in blocks. */
	public static void writeSporePush(int slot, float totalX, float totalZ) {
		MemorySegment s = shm;
		if (s == null || slot < 0 || slot >= COL_MAX) {
			return;
		}
		long e = OFF_SPORE_MOVES + SM_PUSHES + slot * 8L;
		s.set(JAVA_FLOAT, e, totalX);
		s.set(JAVA_FLOAT, e + 4, totalZ);
	}

	/** Write texture id (1-based) once: pixels (RGBA, top row first) first, then its entry. */
	public static boolean writeTexture(int id, int width, int height, long offset, byte[] rgba) {
		MemorySegment s = shm;
		if (s == null || id < 1 || id > T_MAX || offset + rgba.length > T_DATA_BYTES) {
			return false;
		}
		long e = OFF_TEXTURES + T_TABLE + (id - 1) * T_ENTRY_SIZE;
		INT.setRelease(s, e, 0); // not valid while rewriting
		MemorySegment.copy(rgba, 0, s, JAVA_BYTE, OFF_TEXTURES + T_DATA + offset, rgba.length);
		s.set(JAVA_INT, e + 4, width);
		s.set(JAVA_INT, e + 8, height);
		s.set(JAVA_LONG, e + 16, offset);
		VarHandle.storeStoreFence();
		INT.setRelease(s, e, SESSION + id);
		return true;
	}

	/** The whole shared memory (null when not linked), for the overlay's picture copy. */
	public static MemorySegment segment() {
		return shm;
	}

	private static int overlayBack = 1; // our private slot; Mycelium's starts at 2, the middle at 0

	/** Where the next overlay picture goes. */
	public static long overlayBackSlotOffset() {
		return OFF_OVERLAY_PIXELS + overlayBack * OVERLAY_SLOT_BYTES;
	}

	/** Publish the picture just written into our slot, and take the old middle slot as the next one. */
	public static void publishOverlay(int width, int height, boolean bottomUp, long frameId) {
		MemorySegment s = shm;
		if (s == null) {
			return;
		}
		long hdr = OFF_OVERLAY_SLOT_HDR + overlayBack * SLOT_HDR_SIZE;
		s.set(JAVA_INT, hdr + SH_WIDTH, width);
		s.set(JAVA_INT, hdr + SH_HEIGHT, height);
		s.set(JAVA_INT, hdr + SH_FLAGS, bottomUp ? 1 : 0);
		s.set(JAVA_LONG, hdr + SH_FRAME_ID, frameId);
		int old = (int) INT.getAndSet(s, OFF_OVERLAY_CTL + OC_STATE, overlayBack | OVERLAY_DIRTY);
		overlayBack = old & 3;
		LONG.getAndAdd(s, OFF_OVERLAY_CTL + OC_FRAMES_PUBLISHED, 1L);
	}

	/** One of Mycelium's enemies, as Minecraft's stand-in should be. */
	/** Seqlock write of the Thrown table: weapon ids, flags, and positions (x, y, z per item). */
	public static void writeThrown(int count, int[] ids, int[] flags, double[] xyz) {
		MemorySegment s = shm;
		if (s == null) {
			return;
		}
		long b = OFF_THROWN;
		int seq = s.get(JAVA_INT, b + THR_SEQ);
		INT.setRelease(s, b + THR_SEQ, seq + 1);
		VarHandle.storeStoreFence();
		count = Math.min(count, THR_MAX);
		s.set(JAVA_INT, b + THR_COUNT, count);
		for (int i = 0; i < count; i++) {
			long e = b + THR_LIST + i * THR_SIZE;
			s.set(JAVA_INT, e, ids[i]);
			s.set(JAVA_INT, e + 4, flags[i]);
			s.set(JAVA_DOUBLE, e + 8, xyz[i * 3]);
			s.set(JAVA_DOUBLE, e + 16, xyz[i * 3 + 1]);
			s.set(JAVA_DOUBLE, e + 24, xyz[i * 3 + 2]);
		}
		INT.setRelease(s, b + THR_SEQ, seq + 2);
	}

	/** One of your weapons (Weapons table). */
	public static final class Weapon {
		public int id, type;
		public float damage, perSecond, knockback, damageMult, speedMod, reach;
		public String name = "", attributes = "", key = "";
	}

	/** The Weapons table's header: version, select request (seq, id), the held weapon. */
	public static final class WeaponsHeader {
		public int version, selectSeq, selectId, heldId;
		public float damageScale;
	}

	/** Seqlock read of the Weapons table. Count, or -1. */
	public static int readWeapons(Weapon[] out, WeaponsHeader header) {
		MemorySegment s = shm;
		if (s == null) {
			return -1;
		}
		long b = OFF_WEAPONS;
		for (int attempt = 0; attempt < 16; attempt++) {
			int seq1 = (int) INT.getAcquire(s, b + W_SEQ);
			if ((seq1 & 1) != 0) {
				Thread.onSpinWait();
				continue;
			}
			int count = Math.min(Math.max(0, s.get(JAVA_INT, b + W_COUNT)), Math.min(W_MAX, out.length));
			header.version = s.get(JAVA_INT, b + W_VERSION);
			header.selectSeq = s.get(JAVA_INT, b + W_SELECT_SEQ);
			header.selectId = s.get(JAVA_INT, b + W_SELECT_ID);
			header.heldId = s.get(JAVA_INT, b + W_HELD_ID);
			header.damageScale = s.get(JAVA_FLOAT, b + W_DAMAGE_SCALE);
			for (int i = 0; i < count; i++) {
				readWeaponEntry(s, b + W_LIST + i * W_SIZE, out[i]);
			}
			VarHandle.loadLoadFence();
			if ((int) INT.getVolatile(s, b + W_SEQ) == seq1) {
				return count;
			}
		}
		return -1;
	}

	private static void readWeaponEntry(MemorySegment s, long e, Weapon w) {
		w.id = s.get(JAVA_INT, e + WE_ID);
		w.type = s.get(JAVA_INT, e + WE_TYPE);
		w.damage = s.get(JAVA_FLOAT, e + WE_DAMAGE);
		w.perSecond = s.get(JAVA_FLOAT, e + WE_PER_SECOND);
		w.knockback = s.get(JAVA_FLOAT, e + WE_KNOCKBACK);
		w.damageMult = s.get(JAVA_FLOAT, e + WE_DAMAGE_MULT);
		w.speedMod = s.get(JAVA_FLOAT, e + WE_SPEED_MOD);
		w.reach = s.get(JAVA_FLOAT, e + WE_REACH);
		w.name = utf8(s, e + WE_NAME, WE_NAME_BYTES);
		w.attributes = utf8(s, e + WE_ATTS, WE_ATTS_BYTES);
		w.key = utf8(s, e + WE_KEY, WE_KEY_BYTES);
	}

	private static String utf8(MemorySegment s, long off, int max) {
		int n = 0;
		while (n < max && s.get(java.lang.foreign.ValueLayout.JAVA_BYTE, off + n) != 0) {
			n++;
		}
		byte[] bytes = new byte[n];
		for (int i = 0; i < n; i++) {
			bytes[i] = s.get(java.lang.foreign.ValueLayout.JAVA_BYTE, off + i);
		}
		return new String(bytes, java.nio.charset.StandardCharsets.UTF_8);
	}

	public static final class Enemy {
		public int id, flags;
		public float x, y, z, width, height, health, maxHealth, yaw;
	}

	/** Seqlock read of the enemy table into out (reused). Returns the count, or -1 if not readable. */
	public static int readEnemies(Enemy[] out) {
		MemorySegment s = shm;
		if (s == null) {
			return -1;
		}
		long b = OFF_ENEMIES;
		for (int attempt = 0; attempt < 16; attempt++) {
			int seq1 = (int) INT.getAcquire(s, b + E_SEQ);
			if ((seq1 & 1) != 0) {
				Thread.onSpinWait();
				continue;
			}
			int count = Math.min(Math.max(0, s.get(JAVA_INT, b + E_COUNT)), Math.min(E_MAX, out.length));
			for (int i = 0; i < count; i++) {
				long e = b + E_LIST + i * E_SIZE;
				Enemy en = out[i];
				en.id = s.get(JAVA_INT, e + EN_ID);
				en.flags = s.get(JAVA_INT, e + EN_FLAGS);
				en.x = s.get(JAVA_FLOAT, e + EN_X);
				en.y = s.get(JAVA_FLOAT, e + EN_Y);
				en.z = s.get(JAVA_FLOAT, e + EN_Z);
				en.width = s.get(JAVA_FLOAT, e + EN_WIDTH);
				en.height = s.get(JAVA_FLOAT, e + EN_HEIGHT);
				en.health = s.get(JAVA_FLOAT, e + EN_HEALTH);
				en.maxHealth = s.get(JAVA_FLOAT, e + EN_MAX_HEALTH);
				en.yaw = s.get(JAVA_FLOAT, e + EN_YAW);
			}
			VarHandle.loadLoadFence();
			if ((int) INT.getVolatile(s, b + E_SEQ) == seq1) {
				return count;
			}
		}
		return -1;
	}

	/** Report a hit on a stand-in: entry first, then the count (any thread; synchronized). */
	public static synchronized void writeHit(int enemyId, float damage, int flags) {
		MemorySegment s = shm;
		if (s == null) {
			return;
		}
		int n = s.get(JAVA_INT, OFF_HITS + H_COUNT);
		long e = OFF_HITS + H_LIST + (Integer.remainderUnsigned(n, H_RING)) * H_SIZE;
		s.set(JAVA_INT, e, enemyId);
		s.set(JAVA_FLOAT, e + 4, damage);
		s.set(JAVA_INT, e + 8, flags);
		VarHandle.storeStoreFence();
		INT.setRelease(s, OFF_HITS + H_COUNT, n + 1);
	}

	/** Seqlock write of the stand-ins' hit boxes: ids[i] and 6 floats per box (min x, y, z, max x, y, z). */
	public static void writeHitBoxes(int count, int[] ids, float[] boxes) {
		MemorySegment s = shm;
		if (s == null) {
			return;
		}
		count = Math.min(count, HB_MAX);
		long b = OFF_HIT_BOXES;
		int seq = s.get(JAVA_INT, b + HB_SEQ);
		INT.setRelease(s, b + HB_SEQ, seq + 1);
		VarHandle.storeStoreFence();
		s.set(JAVA_INT, b + HB_COUNT, count);
		for (int i = 0; i < count; i++) {
			long e = b + HB_LIST + i * HB_SIZE;
			s.set(JAVA_INT, e, ids[i]);
			for (int k = 0; k < 6; k++) {
				s.set(JAVA_FLOAT, e + 4 + 4L * k, boxes[i * 6 + k]);
			}
		}
		INT.setRelease(s, b + HB_SEQ, seq + 2);
	}

	/** Plain snapshot of the Colony block: every spore in your colony. */
	public static final class Colony {
		public int count;
		/** How many of the count entries are your colony (since v63); the rest are scenery spores (the tutorial's sleepers): bodies only. */
		public int listed;
		/** Average happiness 0..1 (-1 = no colony). */
		public float average = -1f;
		/** The spore you control (-1 = none). */
		public int current = -1;
		public final int[] colors = new int[COL_MAX];
		public final int[] caps = new int[COL_MAX];
		/** How fast each spore walks, blocks per second. */
		public final float[] speeds = new float[COL_MAX];
		/** Mycelium's nutrient bonus in percent; -1 = not in a run. */
		public int nutrientBonus = -1;
		/** Bit i: spore i is waving (since v51). */
		public int waving;
		/** How far through its sprout each spore is, 0..1 (-1 = none). */
		public final float[] emerges = new float[COL_MAX];
		/** How far through a beetle drum hit each spore is, 0..1 (-1 = none; since v57). */
		public final float[] drums = new float[COL_MAX];
		/** Each spore at a decoration: 0 none, 1 sitting, 2 lying on its back, 5 lying head ahead (since v58). */
		public final int[] poses = new int[COL_MAX];
		/** Each spore's facing, Minecraft yaw (since v58). */
		public final float[] yaws = new float[COL_MAX];
		public final float[] happiness = new float[COL_MAX];
		public final String[] names = new String[COL_MAX];
	}

	/** Seqlock read of the Colony block. Returns false if not linked or the writer kept interrupting. */
	public static boolean readColony(Colony out) {
		MemorySegment s = shm;
		if (s == null) {
			return false;
		}
		long b = OFF_COLONY;
		for (int attempt = 0; attempt < 16; attempt++) {
			int seq1 = (int) INT.getAcquire(s, b + COL_SEQ);
			if ((seq1 & 1) != 0) {
				// Mycelium is writing. After 8 quick tries, let it finish (it may have been paused mid-write).
				if (attempt < 8) {
					Thread.onSpinWait();
				} else {
					Thread.yield();
				}
				continue;
			}
			out.count = Math.max(0, Math.min(COL_MAX, s.get(JAVA_INT, b + COL_COUNT)));
			out.listed = Math.max(0, Math.min(out.count, s.get(JAVA_INT, b + COL_LISTED)));
			out.average = s.get(JAVA_FLOAT, b + COL_AVERAGE);
			out.current = s.get(JAVA_INT, b + COL_CURRENT);
			out.nutrientBonus = s.get(JAVA_INT, b + COL_NUTRIENT_BONUS);
			out.waving = s.get(JAVA_INT, b + COL_WAVING);
			for (int i = 0; i < out.count; i++) {
				long e = b + COL_LIST + COL_SIZE * i;
				out.colors[i] = s.get(JAVA_INT, e + CE_COLOR);
				out.caps[i] = s.get(JAVA_INT, e + CE_CAP);
				out.speeds[i] = s.get(JAVA_FLOAT, e + CE_SPEED);
				out.emerges[i] = s.get(JAVA_FLOAT, e + CE_EMERGE);
				out.drums[i] = s.get(JAVA_FLOAT, b + COL_DRUMS + 4L * i);
				out.poses[i] = s.get(JAVA_BYTE, b + COL_POSES + i) & 0xFF;
				out.yaws[i] = s.get(JAVA_SHORT, b + COL_YAWS + 2L * i) / 10f;
				out.happiness[i] = s.get(JAVA_FLOAT, e + CE_HAPPINESS);
				out.names[i] = utf8(s, e + CE_NAME, CE_NAME_BYTES);
			}
			VarHandle.loadLoadFence();
			if (seq1 == (int) INT.getVolatile(s, b + COL_SEQ)) {
				return true;
			}
		}
		return false;
	}

	/** Plain snapshot of MycState. */
	public static final class MycState {
		public int flags;
		public double x, y, z;
		public float yaw;
		public long frame;
		/** When this changes, move the player to tpX/tpY/tpZ (Minecraft coords). */
		public int teleportSeq;
		public double tpX, tpY, tpZ;
		public float tpYaw;
		/** Wanted Minecraft player height in blocks (0 = leave it alone). */
		public float playerHeight;
		/** Mycelium's health and max health, in its own units (max 0 = not known). */
		public float health, maxHealth;
		/** Mycelium's nutrients (-1 = not known). */
		public int nutrients = -1;
		/** How far through Mycelium's sprout in The Carcass (0..1; -1 = none). */
		public float emerge = -1f;
		/** The spore's walking speed / its base speed (0 = not known). */
		public float speed;
		/** Mycelium's window size in pixels (0 = not known). */
		public int windowW, windowH;
		/** Mycelium's dash (Blitz, the roll), Minecraft blocks per second; 0 and 0 = none. */
		public float dashX, dashZ;
		/** The resources you carry: how many, and each one's id and type (1 Rotten Log, 2 Fresh Exoskeleton, 3 Calcite Deposit, 4 Flesh). */
		public int resourceCount;
		public final int[] resourceIds = new int[RES_MAX], resourceTypes = new int[RES_MAX];
		/** The name of the spore you control ("" = none). */
		public String sporeName = "";

		public boolean inLevel() {
			return (this.flags & MYC_IN_LEVEL) != 0;
		}
	}

	/** Seqlock read of MycState into {@code out}. Returns false if not linked or the writer kept interrupting. */
	public static boolean readMycState(MycState out) {
		MemorySegment s = shm;
		if (s == null) {
			return false;
		}
		long b = OFF_MYC_STATE;
		for (int attempt = 0; attempt < 16; attempt++) {
			int seq1 = (int) INT.getAcquire(s, b + MY_SEQ);
			if ((seq1 & 1) != 0) {
				// Mycelium is writing. After 8 quick tries, let it finish (it may have been paused mid-write).
				if (attempt < 8) {
					Thread.onSpinWait();
				} else {
					Thread.yield();
				}
				continue;
			}
			out.flags = s.get(JAVA_INT, b + MY_FLAGS);
			out.x = s.get(JAVA_DOUBLE, b + MY_X);
			out.y = s.get(JAVA_DOUBLE, b + MY_Y);
			out.z = s.get(JAVA_DOUBLE, b + MY_Z);
			out.yaw = s.get(JAVA_FLOAT, b + MY_YAW);
			out.frame = s.get(JAVA_LONG, b + MY_FRAME);
			out.teleportSeq = s.get(JAVA_INT, b + MY_TELEPORT_SEQ);
			out.tpX = s.get(JAVA_DOUBLE, b + MY_TP_X);
			out.tpY = s.get(JAVA_DOUBLE, b + MY_TP_Y);
			out.tpZ = s.get(JAVA_DOUBLE, b + MY_TP_Z);
			out.tpYaw = s.get(JAVA_FLOAT, b + MY_TP_YAW);
			out.playerHeight = s.get(JAVA_FLOAT, b + MY_PLAYER_HEIGHT);
			out.health = s.get(JAVA_FLOAT, b + MY_HEALTH);
			out.maxHealth = s.get(JAVA_FLOAT, b + MY_MAX_HEALTH);
			out.nutrients = s.get(JAVA_INT, b + MY_NUTRIENTS);
			out.emerge = s.get(JAVA_FLOAT, b + MY_EMERGE);
			out.speed = s.get(JAVA_FLOAT, b + MY_SPEED);
			out.windowW = s.get(JAVA_INT, b + MY_WINDOW_W);
			out.windowH = s.get(JAVA_INT, b + MY_WINDOW_H);
			out.dashX = s.get(JAVA_FLOAT, b + MY_DASH_X);
			out.dashZ = s.get(JAVA_FLOAT, b + MY_DASH_Z);
			out.resourceCount = Math.max(0, Math.min(RES_MAX, s.get(JAVA_INT, b + MY_RESOURCE_COUNT)));
			for (int i = 0; i < out.resourceCount; i++) {
				int e = s.get(JAVA_INT, b + MY_RESOURCES + 4L * i);
				out.resourceIds[i] = e >>> 4;
				out.resourceTypes[i] = e & 0xF;
			}
			out.sporeName = utf8(s, b + MY_SPORE_NAME, MY_SPORE_NAME_BYTES);
			VarHandle.loadLoadFence();
			int seq2 = (int) INT.getVolatile(s, b + MY_SEQ);
			if (seq1 == seq2) {
				return true;
			}
		}
		return false;
	}

	/**
	 * Seqlock read of the ground grid. Returns {@code current} unchanged if Mycelium hasn't published a
	 * newer scan (cheap: only the version is read), or null if there is no grid at all.
	 */
	public static GroundGrid readGround(GroundGrid current) {
		MemorySegment s = shm;
		if (s == null) {
			return current;
		}
		long b = OFF_GROUND;
		for (int attempt = 0; attempt < 16; attempt++) {
			int seq1 = (int) INT.getAcquire(s, b + G_SEQ);
			if ((seq1 & 1) != 0) {
				Thread.onSpinWait();
				continue;
			}
			int version = s.get(JAVA_INT, b + G_VERSION);
			if (version == 0 || (current != null && current.version == version)) {
				return current;
			}
			int n = s.get(JAVA_INT, b + G_N);
			if (n <= 0 || n > G_MAX_N) {
				return current;
			}
			double ox = s.get(JAVA_DOUBLE, b + G_ORIGIN_X);
			double oz = s.get(JAVA_DOUBLE, b + G_ORIGIN_Z);
			double cell = s.get(JAVA_DOUBLE, b + G_CELL);
			float[] pieces = new float[n * n * G_FLOATS_PER_CELL];
			MemorySegment.copy(s, JAVA_FLOAT, b + G_HEIGHTS, pieces, 0, pieces.length);
			VarHandle.loadLoadFence();
			if ((int) INT.getVolatile(s, b + G_SEQ) == seq1) {
				return new GroundGrid(version, n, ox, oz, cell, pieces);
			}
		}
		return current;
	}

	/** Plain snapshot of the input bridge block. */
	public static final class MycInput {
		public int flags;
		public int reason;
		public int buttons;
		public final byte[] keys = new byte[IN_KEY_BYTES];
		public double totalDX, totalDY, totalScroll;
		public int textCount;
		public final int[] text = new int[IN_TEXT_RING];
		public double cursorX, cursorY;

		public boolean active() {
			return (this.flags & IN_ACTIVE) != 0;
		}

		/** A Minecraft screen is open and Mycelium sends a cursor position (0..1 across, 0..1 down). */
		public boolean cursor() {
			return (this.flags & IN_CURSOR) != 0;
		}

		public boolean key(int scancode) {
			return scancode >= 0 && scancode < IN_KEY_BYTES * 8 && (this.keys[scancode >> 3] & (1 << (scancode & 7))) != 0;
		}
	}

	/** Seqlock read of the input bridge block. False if not linked or the writer kept interrupting. */
	public static boolean readInput(MycInput out) {
		MemorySegment s = shm;
		if (s == null) {
			return false;
		}
		long b = OFF_INPUT;
		for (int attempt = 0; attempt < 16; attempt++) {
			int seq1 = (int) INT.getAcquire(s, b + IN_SEQ);
			if ((seq1 & 1) != 0) {
				Thread.onSpinWait();
				continue;
			}
			out.flags = s.get(JAVA_INT, b + IN_FLAGS);
			out.reason = s.get(JAVA_INT, b + IN_REASON);
			out.buttons = s.get(JAVA_INT, b + IN_BUTTONS);
			MemorySegment.copy(s, JAVA_BYTE, b + IN_KEYS, out.keys, 0, IN_KEY_BYTES);
			out.totalDX = s.get(JAVA_DOUBLE, b + IN_TOTAL_DX);
			out.totalDY = s.get(JAVA_DOUBLE, b + IN_TOTAL_DY);
			out.totalScroll = s.get(JAVA_DOUBLE, b + IN_TOTAL_SCROLL);
			out.textCount = s.get(JAVA_INT, b + IN_TEXT_COUNT);
			MemorySegment.copy(s, JAVA_INT, b + IN_TEXT, out.text, 0, IN_TEXT_RING);
			out.cursorX = s.get(JAVA_DOUBLE, b + IN_CURSOR_X);
			out.cursorY = s.get(JAVA_DOUBLE, b + IN_CURSOR_Y);
			VarHandle.loadLoadFence();
			if ((int) INT.getVolatile(s, b + IN_SEQ) == seq1) {
				return true;
			}
		}
		return false;
	}

	/** Minecraft's latest physics tick and when it happened (QueryPerformanceCounter). */
	public static final class Tick {
		public long qpc;
		public double prevX, prevY, prevZ, curX, curY, curZ;
		public float ms, bodyO, body;
	}

	/** Windows QueryPerformanceCounter: the clock Mycelium (Stopwatch) reads too. */
	public static synchronized long qpc() {
		try {
			int ok = (int) QUERY_PERFORMANCE_COUNTER.invokeExact(QPC_OUT);
			return QPC_OUT.get(JAVA_LONG, 0);
		} catch (Throwable t) {
			throw new RuntimeException(t);
		}
	}

	/** Ticks per second of {@link #qpc()}. */
	public static synchronized long qpcFrequency() {
		try {
			int ok = (int) QUERY_PERFORMANCE_FREQUENCY.invokeExact(QPC_OUT);
			return QPC_OUT.get(JAVA_LONG, 0);
		} catch (Throwable t) {
			throw new RuntimeException(t);
		}
	}

	public static long tickCount() {
		try {
			return (long) GET_TICK_COUNT64.invokeExact();
		} catch (Throwable t) {
			throw new RuntimeException(t);
		}
	}

	private static int pid() {
		try {
			return (int) GET_CURRENT_PROCESS_ID.invokeExact();
		} catch (Throwable t) {
			throw new RuntimeException(t);
		}
	}
}
