package dev.myceliumcraft.link;

import java.util.ArrayList;
import java.util.List;

/**
 * Pretends to be Minecraft, for testing the link without starting the game. Run tools\FakeMycelium
 * first. It checks the ground grid FakeMycelium publishes (a step from y 10 to y 10.5 at x = 0, one
 * hole, and a tunnel: roof from y 13 to 14 over x -22..-17), confirms the teleport, then walks 10
 * blocks east and 10 blocks north from there, sweeping its pitch from -90 (up) to 90 (down) and
 * going through the F5 views (first person, behind, in front).
 */
public final class LinkTestMain {
	/** Fake Minecraft frame interval; set LINKTEST_FRAME_MS to test other frame rates (default 16 = ~60 fps). */
	private static final long FRAME_MS = Long.parseLong(System.getenv().getOrDefault("LINKTEST_FRAME_MS", "16"));
	/**
	 * LINKTEST_TURN=1: keep turning at 60 degrees/s in uneven steps (0, 2, 1, 0, 2, 1 frames' worth),
	 * the way bridged mouse movement reaches Minecraft, instead of the two fixed headings.
	 */
	private static final boolean TURN = "1".equals(System.getenv("LINKTEST_TURN"));
	private static final int[] TURN_STEPS = {0, 2, 1, 0, 2, 1};
	/** LINKTEST_STRAFE=1: look south (yaw 0) the whole time, so walking east then north is sideways, then backwards. */
	private static final boolean STRAFE = "1".equals(System.getenv("LINKTEST_STRAFE"));
	/** LINKTEST_VIEW=n: stay in one F5 view (0 first person, 1 behind, 2 in front) instead of cycling. */
	private static final int VIEW = Integer.parseInt(System.getenv().getOrDefault("LINKTEST_VIEW", "-1"));
	/** LINKTEST_HICCUP=1: report "not in a world" for one frame every 2 s. */
	private static final boolean HICCUP = "1".equals(System.getenv("LINKTEST_HICCUP"));
	/** LINKTEST_STILL=1: stand still at the teleport spot. */
	private static final boolean STILL = "1".equals(System.getenv("LINKTEST_STILL"));
	/** LINKTEST_PITCH_WAVE=1: look smoothly up and down between 30 and 80 degrees down (2.5 s period). */
	private static final boolean PITCH_WAVE = "1".equals(System.getenv("LINKTEST_PITCH_WAVE"));
	/** LINKTEST_PITCH=p: hold this pitch instead of sweeping. */
	private static final String PITCH = System.getenv("LINKTEST_PITCH");
	/** Key binds to send, as Minecraft's Controls menu would: "kind:code" per Mycelium action, in order (0 unbound, 1 key scancode, 2 mouse button). */
	private static final String BINDS = System.getenv("LINKTEST_BINDS");
	/** "1": send a 640x360 test picture as the overlay: an opaque red box, a half-transparent white box, the rest transparent. */
	private static final boolean OVERLAY = "1".equals(System.getenv("LINKTEST_OVERLAY"));
	private static long overlayFrames;
	/** "1": send a test player model (a 0.6 x 1.2 x 0.3 block body with a nose on its +Z side) in third person, like Minecraft's. */
	private static final boolean MODEL = "1".equals(System.getenv("LINKTEST_MODEL"));
	/** "1": pretend Minecraft's pause menu is open from 4 to 7 s (Mycelium should freeze). */
	private static final boolean PAUSE = "1".equals(System.getenv("LINKTEST_PAUSE"));
	/** "1": sprint FOV and crouch eye height from 4 to 7 s. */
	private static final boolean FOVEYE = "1".equals(System.getenv("LINKTEST_FOVEYE"));
	/** "1": log Mycelium's enemy table once a second, and hit the nearest enemy for 2 points (10 Mycelium damage) every 2 s. */
	private static final boolean HIT = "1".equals(System.getenv("LINKTEST_HIT"));
	private static final MycLink.Enemy[] ENEMIES = new MycLink.Enemy[Proto.E_MAX];
	private static long nextHit, nextEnemyLog;
	private static boolean modelTextureSent;
	/** Optional: switch to these key binds 5 s in, as if you changed them in the Controls menu while playing. */
	private static final String BINDS2 = System.getenv("LINKTEST_BINDS2");
	private static long bindsStart;

	/** Like Minecraft hitting a stand-in: the nearest live enemy, 2 points every 2 s. */
	private static void hitNearest(double px, double py, double pz) {
		if (ENEMIES[0] == null) {
			for (int i = 0; i < ENEMIES.length; i++) {
				ENEMIES[i] = new MycLink.Enemy();
			}
		}
		int n = MycLink.readEnemies(ENEMIES);
		long now = System.currentTimeMillis();
		MycLink.Enemy best = null;
		double bestD = Double.MAX_VALUE;
		for (int i = 0; i < n; i++) {
			MycLink.Enemy e = ENEMIES[i];
			double d = (e.x - px) * (e.x - px) + (e.y - py) * (e.y - py) + (e.z - pz) * (e.z - pz);
			if ((e.flags & Proto.EN_ALIVE) != 0 && d < bestD) {
				bestD = d;
				best = e;
			}
		}
		// The stand-ins' boxes, as Minecraft's client would have them (centred, square footprint).
		int[] boxIds = new int[Math.max(n, 0)];
		float[] boxes = new float[Math.max(n, 0) * 6];
		for (int i = 0; i < n; i++) {
			MycLink.Enemy e = ENEMIES[i];
			boxIds[i] = e.id;
			boxes[i * 6] = e.x - e.width / 2;
			boxes[i * 6 + 1] = e.y;
			boxes[i * 6 + 2] = e.z - e.width / 2;
			boxes[i * 6 + 3] = e.x + e.width / 2;
			boxes[i * 6 + 4] = e.y + e.height;
			boxes[i * 6 + 5] = e.z + e.width / 2;
		}
		MycLink.writeHitBoxes(Math.max(n, 0), boxIds, boxes);
		if (now >= nextEnemyLog) {
			nextEnemyLog = now + 1000;
			System.out.printf("enemies: %d; nearest %s%n", n, best == null ? "none" : String.format("#%d at (%.1f, %.1f, %.1f), %.2f x %.2f blocks, health %.1f/%.1f, %.1f blocks away",
				best.id, best.x, best.y, best.z, best.width, best.height, best.health, best.maxHealth, Math.sqrt(bestD)));
		}
		if (best != null && now >= nextHit) {
			nextHit = now + 2000;
			MycLink.writeHit(best.id, 2f, 0);
			System.out.printf("hit enemy #%d for 2 points%n", best.id);
		}
	}

	/** The test player model: two boxes relative to the feet, Minecraft blocks and axes. Marked first person in first person, as Minecraft's is. */
	private static void writeTestModel(boolean thirdPerson) {
		boolean firstPerson = !thirdPerson;
		if (!modelTextureSent) {
			byte[] tex = new byte[4 * 4 * 4];
			for (int i = 0; i < 16; i++) { // 4x4 test texture: a red top row, the rest white
				tex[i * 4] = (byte) 255;
				tex[i * 4 + 1] = (byte) (i < 4 ? 0 : 255);
				tex[i * 4 + 2] = (byte) (i < 4 ? 0 : 255);
				tex[i * 4 + 3] = (byte) 255;
			}
			modelTextureSent = MycLink.writeTexture(1, 4, 4, 0, tex);
		}
		java.util.List<Integer> v = new java.util.ArrayList<>();
		box(v, -0.3f, 0f, -0.15f, 0.3f, 1.2f, 0.15f); // body
		box(v, -0.05f, 1.0f, 0.15f, 0.05f, 1.1f, 0.35f); // nose, on the +Z side (the way yaw 0 faces)
		int[] data = v.stream().mapToInt(Integer::intValue).toArray();
		int vertices = data.length / 6;
		MycLink.writeModel(Proto.M_VISIBLE | (firstPerson ? Proto.M_FIRST_PERSON : 0), new int[] { 1, 0, vertices }, 1, data, vertices);
	}

	private static void box(java.util.List<Integer> out, float x0, float y0, float z0, float x1, float y1, float z1) {
		float[][] c = { { x0, y0, z0 }, { x1, y0, z0 }, { x1, y1, z0 }, { x0, y1, z0 }, { x0, y0, z1 }, { x1, y0, z1 }, { x1, y1, z1 }, { x0, y1, z1 } };
		int[][] faces = { { 0, 1, 2, 3 }, { 5, 4, 7, 6 }, { 4, 0, 3, 7 }, { 1, 5, 6, 2 }, { 3, 2, 6, 7 }, { 4, 5, 1, 0 } };
		float[][] uv = { { 0, 1 }, { 1, 1 }, { 1, 0 }, { 0, 0 } };
		for (int[] f : faces) {
			for (int k : new int[] { 0, 1, 2, 0, 2, 3 }) {
				float[] p = c[f[k]];
				out.add(Float.floatToRawIntBits(p[0]));
				out.add(Float.floatToRawIntBits(p[1]));
				out.add(Float.floatToRawIntBits(p[2]));
				out.add(Float.floatToRawIntBits(uv[k][0]));
				out.add(Float.floatToRawIntBits(uv[k][1]));
				out.add(-1); // white, opaque
			}
		}
	}

	/** The overlay test picture, as Minecraft's would be: RGBA, premultiplied alpha, rows bottom-up. */
	private static void writeTestPicture() {
		int w = 640, h = 360;
		var s = MycLink.segment();
		long base = MycLink.overlayBackSlotOffset();
		byte[] row = new byte[w * 4];
		for (int y = 0; y < h; y++) {
			int top = h - 1 - y; // bottom-up: row 0 is the bottom of the picture
			java.util.Arrays.fill(row, (byte) 0);
			if (top >= 40 && top < 120) {
				for (int x = 40; x < 200; x++) { // opaque red
					row[x * 4] = (byte) 255;
					row[x * 4 + 3] = (byte) 255;
				}
				for (int x = 440; x < 600; x++) { // white at half coverage, premultiplied: 128,128,128,128
					row[x * 4] = row[x * 4 + 1] = row[x * 4 + 2] = row[x * 4 + 3] = (byte) 128;
				}
			}
			java.lang.foreign.MemorySegment.copy(row, 0, s, java.lang.foreign.ValueLayout.JAVA_BYTE, base + (long) y * w * 4, row.length);
		}
		MycLink.publishOverlay(w, h, true, ++overlayFrames);
	}

	public static void main(String[] args) throws InterruptedException {
		long deadline = System.currentTimeMillis() + 15000;
		MycLink.MycState myc = new MycLink.MycState();
		GroundGrid grid = null;
		while (grid == null || !MycLink.readMycState(myc) || myc.teleportSeq == 0) {
			MycLink.poll();
			// Like the real mod: say "in a world" every frame, or Mycelium never starts scanning.
			MycLink.writeMcState(Proto.MC_IN_WORLD | Proto.MC_ON_GROUND, 0, -60, 0, 0f, 0f, 0, 1, 0, 1, 0f, null);
			grid = MycLink.readGround(grid);
			if (System.currentTimeMillis() > deadline) {
				System.out.println("FAIL: no Mycelium ground grid / teleport after 15 s");
				System.exit(1);
			}
			Thread.sleep(50);
		}
		System.out.printf("linked; grid v%d %dx%d cell %.2f origin (%.1f, %.1f); teleport %d to (%.2f, %.2f, %.2f) yaw %.0f%n",
			grid.version, grid.n, grid.n, grid.cell, grid.originX, grid.originZ, myc.teleportSeq, myc.tpX, myc.tpY, myc.tpZ, myc.tpYaw);

		boolean ok = true;
		ok &= check("ground west of the step", grid.groundAt(-5, 10, 0), 10.0);
		ok &= check("ground east of the step", grid.groundAt(5, 10.5, 0), 10.5);
		ok &= check("ground in the hole", grid.groundAt(-26.75, 10, -26.75), Double.NaN);
		ok &= check("ground inside the tunnel", grid.groundAt(-20, 10, 0), 10.0);
		ok &= check("ground on the tunnel roof", grid.groundAt(-20, 14, 0), 14.0);
		ok &= check("boxes in block (0,10,0)", boxes(grid, 0, 10, 0), 4);
		ok &= check("boxes in block (-1,10,0), ground exactly at its bottom", boxes(grid, -1, 10, 0), 0);
		ok &= check("boxes in block (-1,9,0), fully under ground", boxes(grid, -1, 9, 0), 4);
		ok &= check("boxes in tunnel block (-20,11,0), open space", boxes(grid, -20, 11, 0), 0);
		ok &= check("boxes in tunnel block (-20,12,0), open space", boxes(grid, -20, 12, 0), 0);
		ok &= check("boxes in tunnel roof block (-20,13,0)", boxes(grid, -20, 13, 0), 4);
		ok &= check("boxes in tunnel floor block (-20,9,0)", boxes(grid, -20, 9, 0), 4);
		List<double[]> top = new ArrayList<>();
		grid.forEachBox(0, 10, 0, (a, b, c, d, e, f) -> top.add(new double[] {a, b, c, d, e, f}));
		ok &= check("box top in block (0,10,0)", top.isEmpty() ? -1 : top.get(0)[4], 0.5);
		ok &= check("wanted player height (blocks)", Math.round(myc.playerHeight * 1000) / 1000.0, 0.964);
		ok &= check("Mycelium health", myc.health, 37.5);
		ok &= check("Mycelium max health", myc.maxHealth, 150);
		ok &= check("Mycelium nutrients", myc.nutrients, 42);
		MycLink.MycInput in = new MycLink.MycInput();
		ok &= check("input block read", MycLink.readInput(in) ? 1 : 0, 1);
		ok &= check("input bridge active", in.active() ? 1 : 0, 1);
		ok &= check("W held (scancode 26)", in.key(26) ? 1 : 0, 1);
		ok &= check("S not held (scancode 22)", in.key(22) ? 1 : 0, 0);
		ok &= check("left mouse button held", (in.buttons >> 1) & 1, 1);
		ok &= check("mouse total X", in.totalDX, 123.5);
		ok &= check("mouse total Y", in.totalDY, -40);
		ok &= check("wheel total", in.totalScroll, 2);
		ok &= check("typed text", in.textCount == 2 && in.text[0] == 'h' && in.text[1] == 'i' ? 1 : 0, 1);
		ok &= check("cursor sent", in.cursor() ? 1 : 0, 1);
		ok &= check("cursor x", in.cursorX, 0.25);
		ok &= check("cursor y", in.cursorY, 0.75);

		int ack = myc.teleportSeq;
		double x = myc.tpX, y = myc.tpY, z = myc.tpZ;
		int frames = 0;
		float turnYaw = 0f;
		double lookTime = 0;
		long start = System.currentTimeMillis();
		// Like Minecraft: the player moves in 20-per-second ticks; frames show it interpolated, and
		// each frame publishes the two latest ticks and when the latest one happened.
		long qpcStart = MycLink.qpc();
		long qpcFreq = MycLink.qpcFrequency();
		MycLink.Tick tick = new MycLink.Tick();
		while (true) {
			double t = (System.currentTimeMillis() - start) / 1000.0;
			if (t > 10.5) {
				break;
			}
			// Like Minecraft: apply any new teleport from Mycelium (move there) and confirm it.
			if (MycLink.readMycState(myc) && myc.teleportSeq != 0 && myc.teleportSeq != ack) {
				ack = myc.teleportSeq;
				x = myc.tpX;
				y = myc.tpY;
				z = myc.tpZ;
			}
			// 0-5 s: east (+X). 5-10 s: north (-Z). 2 blocks per second, moved in 50 ms ticks.
			double qt = (MycLink.qpc() - qpcStart) / (double) qpcFreq;
			long k = (long) Math.floor(qt * 20);
			double partial = qt * 20 - k;
			double tc = k / 20.0, tp = (k - 1) / 20.0;
			tick.qpc = qpcStart + (long) (k / 20.0 * qpcFreq);
			tick.ms = 50f;
			if (STILL) {
				tc = 0;
				tp = 0;
			}
			tick.prevX = x + 2 * Math.max(0, Math.min(tp, 5));
			tick.prevZ = z - 2 * Math.max(0, Math.min(tp - 5, 5));
			tick.curX = x + 2 * Math.min(tc, 5);
			tick.curZ = z - 2 * Math.max(0, Math.min(tc - 5, 5));
			tick.prevY = y;
			tick.curY = y;
			double px = tick.prevX + (tick.curX - tick.prevX) * partial;
			double pz = tick.prevZ + (tick.curZ - tick.prevZ) * partial;
			float yaw = t < 5 ? -90f : 180f;
			if (STRAFE) {
				yaw = 0f;
			}
			if (TURN && !PITCH_WAVE) {
				turnYaw += 60f * FRAME_MS / 1000f * TURN_STEPS[frames % TURN_STEPS.length];
				yaw = turnYaw;
			}
			// Look from straight up (-90) to straight down (90) over the 10 s, to exercise the camera.
			// With LINKTEST_TURN=1 the look advances in uneven steps, as bridged mouse movement does.
			if (TURN) {
				lookTime += FRAME_MS / 1000.0 * TURN_STEPS[frames % TURN_STEPS.length];
			} else {
				lookTime = t;
			}
			float pitch = PITCH_WAVE ? (float) (55 + 25 * Math.sin(2 * Math.PI * lookTime / 2.5))
				: PITCH != null ? Float.parseFloat(PITCH) : (float) Math.max(-90, Math.min(90, -90 + 18 * t));
			MycLink.poll();
			// F5 views: first person for 0-3.5 s, behind 3.5-7 s, in front after that.
			int cameraMode = VIEW >= 0 ? VIEW : t < 3.5 ? 0 : t < 7 ? 1 : 2;
			// Body faces the look direction, except when strafing: then 45 degrees toward the walk, as Minecraft does.
			float bodyYaw = STRAFE ? yaw - 45f : yaw;
			tick.bodyO = bodyYaw;
			tick.body = bodyYaw;
			// LINKTEST_HICCUP=1: one frame of "not in a world" every 2 s, like the moment seen in a real log.
			int flags = HICCUP && frames % (int) (2000 / FRAME_MS) == 0 && frames > 0 ? 0 : Proto.MC_IN_WORLD | Proto.MC_ON_GROUND;
			// LINKTEST_PAUSE=1: Minecraft's pause menu open from 4 to 7 s.
			if (PAUSE && t >= 4 && t < 7) {
				flags |= Proto.MC_PAUSED | Proto.MC_SCREEN_OPEN;
			}
			// LINKTEST_FOVEYE=1: sprint FOV (x1.15) and crouch eye height (x0.78) from 4 to 7 s.
			boolean feel = FOVEYE && t >= 4 && t < 7;
			MycLink.writeMcState(flags, px, y, pz, yaw, pitch, frames++, 1, ack, cameraMode, bodyYaw, tick, feel ? 1.15f : 1f, feel ? 0.78f : 1f);
			if (OVERLAY) {
				writeTestPicture();
			}
			if (MODEL) {
				writeTestModel(cameraMode != 0);
			}
			if (HIT) {
				hitNearest(px, y, pz);
			}
			if (BINDS != null) {
				if (bindsStart == 0) {
					bindsStart = System.currentTimeMillis();
				}
				String binds = BINDS2 != null && System.currentTimeMillis() - bindsStart > 5000 ? BINDS2 : BINDS;
				String[] parts = binds.split(",");
				int[] kinds = new int[parts.length], codes = new int[parts.length];
				for (int i = 0; i < parts.length; i++) {
					String[] kc = parts[i].trim().split(":");
					kinds[i] = Integer.parseInt(kc[0]);
					codes[i] = Integer.parseInt(kc[1]);
				}
				MycLink.writeBindings(kinds, codes, parts.length);
			}
			if (frames % 60 == 0) {
				boolean read = MycLink.readMycState(myc);
				System.out.printf("t=%.1f pitch=%.0f mc=(%.2f, %.2f, %.2f) | mycelium alive=%b read=%b flags=%d spore=(%.2f, %.2f, %.2f)%n",
					t, pitch, px, y, pz, MycLink.myceliumAlive(), read, myc.flags, myc.x, myc.y, myc.z);
			}
			Thread.sleep(FRAME_MS);
		}
		System.out.println(ok ? "ground checks PASS" : "ground checks FAIL");
		System.exit(ok ? 0 : 1);
	}

	private static int boxes(GroundGrid grid, int x, int y, int z) {
		int[] count = {0};
		grid.forEachBox(x, y, z, (a, b, c, d, e, f) -> count[0]++);
		return count[0];
	}

	private static boolean check(String what, double got, double expected) {
		boolean pass = Double.isNaN(expected) ? Double.isNaN(got) : Math.abs(got - expected) < 1e-6;
		System.out.printf("  %s %s: got %s, expected %s%n", pass ? "ok  " : "FAIL", what, got, expected);
		return pass;
	}
}
