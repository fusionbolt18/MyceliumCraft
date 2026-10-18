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

	/** Seqlock write of McState. */
	public static void writeMcState(int flags, double x, double y, double z, float yaw, float pitch, long frame, int epoch, int teleportAck, int cameraMode, float bodyYaw, Tick tick) {
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
		INT.setRelease(s, b + MC_SEQ, seq + 2); // even: done
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
				Thread.onSpinWait();
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
