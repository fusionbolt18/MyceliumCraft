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
	/** LINKTEST_KEYS=forward,left...: these movement keys held the whole time (sent as McState's move direction, since v36). */
	private static final String KEYS = System.getenv("LINKTEST_KEYS");
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
	/** LINKTEST_HIDEHUD=1: Minecraft's HUD hidden (F1) from 4 to 8 s. */
	private static final boolean HIDEHUD = "1".equals(System.getenv("LINKTEST_HIDEHUD"));
	/** LINKTEST_RENAME=name: as /changename name at 5 s (Mycelium renames your spore only with -freecam). */
	private static final String RENAME = System.getenv("LINKTEST_RENAME");
	private static boolean renamed;
	/** "1": sprint FOV and crouch eye height from 4 to 7 s. */
	private static final boolean FOVEYE = "1".equals(System.getenv("LINKTEST_FOVEYE"));
	/** "1": log Mycelium's enemy table once a second, and hit the nearest enemy for 2 points (10 Mycelium damage) every 2 s. */
	private static final boolean HIT = "1".equals(System.getenv("LINKTEST_HIT"));
	private static int dashFrames;
	private static boolean vanishedSeen, cycloneSeen, rollSeen;
	/** LINKTEST_LENGTH=s: run this many seconds instead of 10.5. */
	private static final double LENGTH = Double.parseDouble(System.getenv().getOrDefault("LINKTEST_LENGTH", "10.5"));
	private static String resourceSeen = "";
	private static String sporeNameSeen = "";
	private static String colonySeen = "";
	/** LINKTEST_PUSH=1: from 3 s, the first other spore is pushed 1 block east over 1 s, as Minecraft's collision would. */
	private static final boolean PUSH = "1".equals(System.getenv("LINKTEST_PUSH"));
	private static final float[] sporePositions = new float[Proto.COL_MAX * 3];
	private static final MycLink.Boss boss = new MycLink.Boss();
	// LINKTEST_FORGE=1: when Mycelium asks for the Blacksmith's screen, put the first three weapons he takes in its slots and take
	// the result (a forge request); once answered, ask again with the same three (they're gone now: want a refusal).
	private static final boolean FORGE = "1".equals(System.getenv("LINKTEST_FORGE"));
	private static int decorationSeen;
	// LINKTEST_EMOTEWAVE=1: you do Emotecraft's wave from 6 s to 8.5 s (McState's EMOTE_WAVE).
	private static final boolean EMOTE_WAVE = "1".equals(System.getenv("LINKTEST_EMOTEWAVE"));
	private static final MycLink.Forge forge = new MycLink.Forge();
	private static int forgeOpenSeen = Integer.MIN_VALUE, forgeSent, forgeIds[], forgeStage;
	private static String forgeSeen = "", forgePreview = "", forgePreviewNumbers = "";
	private static int forgePreviewSent;
	private static final MycLink.Weapon[] forgeTable = new MycLink.Weapon[Proto.W_MAX];
	private static final MycLink.WeaponsHeader forgeHeader = new MycLink.WeaponsHeader();
	private static String bossSeen = "";
	private static double nextSproutPrint;
	private static final MycLink.Colony colony = new MycLink.Colony();
	private static final float[] bossPull = new float[2];
	private static final float[] goal = new float[3];
	private static String goalWas = "";
	private static boolean rainWas;
	private static int fullscreenWas = -1;
	private static double nextPullPrint;
	private static long pullTick = -1;
	// LINKTEST_THROW_RESOURCE=1: once Mycelium lists 3 resources, the first is thrown 1 s later, flying
	// 3 blocks forward until 1.5 s after, landed for 0.5 s, then gone, as Minecraft reports a thrown item.
	private static final boolean THROW_RESOURCE = "1".equals(System.getenv("LINKTEST_THROW_RESOURCE"));
	private static double throwResourceAt = -1;
	private static int throwResourceId;
	// LINKTEST_FUNGAL=1: a swing at 5 s that hits nothing, then a swing at 7 s that hits the nearest
	// enemy once (from 7.1 s), for Fungal Might ending only with a swing that hits (-mcFungalTest).
	private static final boolean FUNGAL = "1".equals(System.getenv("LINKTEST_FUNGAL"));
	// LINKTEST_SCREEN=1: a Minecraft screen (an inventory) open from 4 to 10 s, without pausing.
	private static final boolean SCREEN = "1".equals(System.getenv("LINKTEST_SCREEN"));
	// LINKTEST_HAND=1: the main hand holds weapon #1 from 7.5 s, nothing (empty) from 8.5 s, #2 from 9.5 s.
	private static final boolean HAND = "1".equals(System.getenv("LINKTEST_HAND"));
	// LINKTEST_HOLD=<id>: the main hand holds that weapon the whole time.
	private static final int HOLD = Integer.parseInt(System.getenv().getOrDefault("LINKTEST_HOLD", "0"));
	// LINKTEST_THROW=<id>: that weapon thrown from 5 s: flying 3 blocks forward (where the player faces) in an arc until 6.5 s,
	// landed until 7 s, then gone (removed), as Minecraft reports a thrown weapon item.
	private static final int THROW = Integer.parseInt(System.getenv().getOrDefault("LINKTEST_THROW", "0"));
	private static final int[] THROW_IDS = new int[1], THROW_FLAGS = new int[1];
	private static final double[] THROW_XYZ = new double[3];
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
		// The same box as the other spores' bodies.
		if (MycLink.readColony(colony)) {
			for (int i = 0; i < colony.count; i++) {
				MycLink.writeBody(i, 1, data, i == colony.current ? 0 : vertices);
			}
		}
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
		if ("1".equals(System.getenv("LINKTEST_ICONS"))) {
			// LINKTEST_ICONS=1: how weapon tooltips' attribute lines come out (stat icons), then stop.
			for (String line : new String[] { "Beefy: +3 Primal, +3 Vitality", "Golden: +2 All Stats", "Savvy: +3 Sentience, +3 Speed", "Rapid: Double attack speed, half damage" }) {
				var c = dev.myceliumcraft.MycWeapons.attributeLine(line, net.minecraft.network.chat.Style.EMPTY);
				StringBuilder parts = new StringBuilder();
				for (var part : c.getSiblings()) {
					String t = part.getString();
					boolean icon = part.getStyle().getFont() instanceof net.minecraft.network.chat.FontDescription.Resource r && r.id().getPath().equals("stat_icons");
					parts.append(icon ? String.format("[icon U+%04X]", (int) t.charAt(0)) : "'" + t + "'");
				}
				System.out.println("icons: '" + line + "' -> " + parts);
			}
			return;
		}
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
		ok &= check("Mycelium sprout progress", myc.emerge, 0.25);
		ok &= check("Mycelium speed factor", myc.speed, 1.25);
		MycLink.Weapon[] weapons = new MycLink.Weapon[Proto.W_MAX];
		for (int i = 0; i < weapons.length; i++) {
			weapons[i] = new MycLink.Weapon();
		}
		MycLink.WeaponsHeader wh = new MycLink.WeaponsHeader();
		int wn = MycLink.readWeapons(weapons, wh);
		ok &= check("weapons", wn, 2);
		ok &= check("weapons version", wh.version, 7);
		ok &= check("weapon select request id", wh.selectId, 2);
		ok &= check("weapons damage scale", wh.damageScale, 5);
		ok &= check("weapon 2 name (UTF-8)", wn >= 2 && weapons[1].name.equals("Bert's Club \u2713") ? 1 : 0, 1);
		ok &= check("weapon 2 damage", wn >= 2 ? weapons[1].damage : -1, 22.5);
		ok &= check("weapon 2 swings/s", wn >= 2 ? weapons[1].perSecond : -1, 0.8);
		ok &= check("weapon 2 attributes", wn >= 2 && weapons[1].attributes.startsWith("Heavy") ? 1 : 0, 1);
		ok &= check("weapon 2 look (key 'Bert's Club' -> bert_s_club)", wn >= 2 && dev.myceliumcraft.MycWeapons.modelKey(weapons[1].key).equals("bert_s_club") ? 1 : 0, 1);
		ok &= check("weapon 1 look (stick)", wn >= 1 && dev.myceliumcraft.MycWeapons.modelKey(weapons[0].key).equals("stick") ? 1 : 0, 1);
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
			if (t > LENGTH) {
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
				// Like MycBossPull: the boss's pull moves the player a tick's worth per tick.
				if (k > pullTick && MycLink.readBossPull(bossPull)) {
					x += bossPull[0] / 20.0 * (pullTick < 0 ? 1 : k - pullTick);
					z += bossPull[1] / 20.0 * (pullTick < 0 ? 1 : k - pullTick);
				}
				pullTick = k;
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
			if (SCREEN && t >= 4 && t < 10) {
				flags |= Proto.MC_SCREEN_OPEN;
			}
			// LINKTEST_PAUSE=1: Minecraft's pause menu open from 4 to 7 s.
			if (HIDEHUD && t >= 4 && t < 8) {
				flags |= Proto.MC_HUD_HIDDEN;
			}
			if (PAUSE && t >= 4 && t < 7) {
				flags |= Proto.MC_PAUSED | Proto.MC_SCREEN_OPEN;
			}
			if (EMOTE_WAVE && t >= 6 && t < 8.5) {
				flags |= Proto.MC_EMOTE_WAVE; // as Emotecraft's 2.5 s wave on you
			}
			// LINKTEST_FOVEYE=1: sprint FOV (x1.15) and crouch eye height (x0.78) from 4 to 7 s.
			boolean feel = FOVEYE && t >= 4 && t < 7;
			int hand = HOLD != 0 ? HOLD : !HAND || t < 7.5 ? 0 : t < 8.5 ? 1 : t < 9.5 ? 0 : 2;
			int swings = FUNGAL ? (t >= 5 ? 1 : 0) + (t >= 7 ? 1 : 0) : 0;
			String keys = KEYS == null ? "" : KEYS;
			float[] move = MycLink.moveDirection(keys.contains("forward"), keys.contains("back"), keys.contains("left"), keys.contains("right"), yaw);
			if (RENAME != null && !renamed && t >= 5) {
				renamed = true;
				MycLink.renameName = RENAME;
				MycLink.renameSeq++;
				System.out.printf("t=%.2f as /changename %s (request %d)%n", t, RENAME, MycLink.renameSeq);
			}
			MycLink.writeMcState(flags, px, y, pz, yaw, pitch, frames++, 1, ack, cameraMode, bodyYaw, tick, feel ? 1.15f : 1f, feel ? 0.78f : 1f, 1f, hand, swings, move[0], move[1]);
			if (FUNGAL && t >= 7.1 && t < 8) {
				hitNearest(px, y, pz); // once: the next would be 2 s later
			}
			if (THROW != 0) {
				int n = 0;
				if (t >= 5 && t < 7) {
					double f = Math.min(1, (t - 5) / 1.5); // 0..1 through the flight
					THROW_IDS[0] = THROW;
					THROW_FLAGS[0] = f >= 1 ? Proto.THR_LANDED : 0;
					double fx = -Math.sin(Math.toRadians(yaw)), fz = Math.cos(Math.toRadians(yaw)); // Minecraft: yaw 0 faces +Z
					THROW_XYZ[0] = px + (0.3 + 3 * f) * fx;
					THROW_XYZ[1] = y + 1.2 + 1.5 * f - 2.7 * f * f; // up from the hand, then down onto the ground
					THROW_XYZ[2] = pz + (0.3 + 3 * f) * fz;
					n = 1;
				}
				MycLink.writeThrown(n, THROW_IDS, THROW_FLAGS, THROW_XYZ);
			}
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
			boolean cycloneNow = MycLink.readMycState(myc) && (myc.flags & Proto.MYC_CYCLONE) != 0;
			boolean rainNow = MycLink.readMycState(myc) && (myc.flags & Proto.MYC_RAIN) != 0;
			int fullscreenNow = MycLink.readMycState(myc) && (myc.flags & Proto.MYC_FULLSCREEN) != 0 ? 1 : 0;
			if (fullscreenNow != fullscreenWas) {
				fullscreenWas = fullscreenNow;
				System.out.printf("t=%.2f window: Mycelium says %s%n", t, fullscreenNow == 1 ? "full screen" : "in a window");
			}
			if (rainNow != rainWas) {
				rainWas = rainNow;
				System.out.printf("t=%.2f weather: %s%n", t, rainNow ? "Mycelium says rain: Minecraft would have a thunderstorm" : "Mycelium says no rain: clear");
			}
			int decorationNow = MycLink.readMycState(myc) ? myc.flags & (Proto.MYC_SIT | Proto.MYC_LIE | Proto.MYC_LIE_FRONT | Proto.MYC_WAVE | Proto.MYC_AT_DECORATION) : 0;
			if (decorationNow != decorationSeen) {
				decorationSeen = decorationNow;
				System.out.printf("t=%.1f decoration: %s%n", t, decorationNow == Proto.MYC_SIT ? "you sit" : decorationNow == Proto.MYC_LIE ? "you lie down on your back" : decorationNow == (Proto.MYC_LIE | Proto.MYC_LIE_FRONT) ? "you lie down on your front" : decorationNow == Proto.MYC_AT_DECORATION ? "you stand at a decoration" : decorationNow == 0 ? "none" : "flags " + Integer.toHexString(decorationNow));
			}
			if (cycloneNow != cycloneSeen) {
				cycloneSeen = cycloneNow;
				System.out.printf("t=%.1f Mycelium's Living Cyclone %s%n", t, cycloneNow ? "spinning (third person behind)" : "over");
			}
			if (MycLink.readMycState(myc)) {
				StringBuilder list = new StringBuilder();
				for (int i = 0; i < myc.resourceCount; i++) {
					list.append(i > 0 ? ", " : "").append("type ").append(myc.resourceTypes[i]).append(" #").append(myc.resourceIds[i]);
				}
				if (FORGE && MycLink.readForge(forge)) {
					forgeFrame(t);
				}
				if (MycLink.readBoss(boss)) {
					String seen = boss.shown ? "'" + boss.name + "' at " + Math.round(boss.progress * 100) + "%" : "none";
					if (!seen.equals(bossSeen)) {
						bossSeen = seen;
						System.out.printf("t=%.2f boss bar: %s%n", t, seen);
					}
				}
				if (PUSH && t >= 3 && t <= 4.5) {
					int n = MycLink.readSporePositions(sporePositions);
					for (int i = 0; i < n; i++) {
						if (!Float.isNaN(sporePositions[i * 3 + 1])) {
							float total = (float) Math.min(1.0, t - 3);
							MycLink.writeSporePush(i, total, 0f);
							if (t >= nextSproutPrint) {
								nextSproutPrint = t + 0.5;
								System.out.printf("t=%.2f push: %d stand-in slots, slot %d at (%.2f, %.2f, %.2f), pushed %.2f blocks%n", t, n, i, sporePositions[i * 3], sporePositions[i * 3 + 1], sporePositions[i * 3 + 2], total);
							}
							break;
						}
					}
				}
				String goalNow = MycLink.readGoal(goal) ? String.format("at (%.0f, %.0f, %.0f)", goal[0], goal[1], goal[2]) : "none";
				if (!goalNow.equals(goalWas)) {
					goalWas = goalNow;
					System.out.printf("t=%.2f goal: %s%n", t, goalNow);
				}
				if (MycLink.readBossPull(bossPull) && (bossPull[0] != 0f || bossPull[1] != 0f) && t >= nextPullPrint) {
					nextPullPrint = t + 0.5;
					System.out.printf("t=%.2f boss pull: %.2f, %.2f blocks/s (%.2f)%n", t, bossPull[0], bossPull[1], Math.hypot(bossPull[0], bossPull[1]));
				}
				if (MycLink.readColony(colony)) {
					for (int i = 0; i < colony.count; i++) {
						if (colony.emerges[i] >= 0f && t >= nextSproutPrint) {
							nextSproutPrint = t + 0.5;
							System.out.printf("t=%.2f colony: %s sprouting, %.2f of the way%n", t, colony.names[i], colony.emerges[i]);
						}
						if (colony.poses[i] != 0 && t >= nextSproutPrint) {
							nextSproutPrint = t + 0.5;
							System.out.printf("t=%.2f colony: %s at a decoration, pose %d, facing yaw %.1f%n", t, colony.names[i], colony.poses[i], colony.yaws[i]);
						}
						if (colony.drums[i] >= 0f) {
							System.out.printf("t=%.2f colony: %s hitting the beetle drum, %.3f of the way%n", t, colony.names[i], colony.drums[i]);
						}
					}
					StringBuilder c = new StringBuilder();
					for (int i = 0; i < colony.count; i++) {
						c.append(i > 0 ? ", " : "").append(colony.names[i]).append(i == colony.current ? " (you)" : "")
							.append(String.format(" %.2f body #%06X cap #%06X", colony.happiness[i], colony.colors[i], colony.caps[i]));
					}
					String seen = colony.count + " spore(s), average " + String.format("%.2f", colony.average) + ", nutrient bonus " + colony.nutrientBonus + ", waving bits " + Integer.toHexString(colony.waving) + ": " + c;
					if (!seen.equals(colonySeen)) {
						colonySeen = seen;
						System.out.printf("t=%.2f colony: %s%n", t, seen);
					}
				}
				if (!myc.sporeName.equals(sporeNameSeen)) {
					sporeNameSeen = myc.sporeName;
					System.out.printf("t=%.2f the spore you control is called '%s' (freecam flag %s)%n", t, sporeNameSeen, (myc.flags & Proto.MYC_FREECAM) != 0 ? "on" : "off");
				}
				if (!list.toString().equals(resourceSeen)) {
					resourceSeen = list.toString();
					System.out.printf("t=%.2f Mycelium carries %d resource(s): %s%n", t, myc.resourceCount, resourceSeen.isEmpty() ? "none" : resourceSeen);
				}
				if (THROW_RESOURCE && throwResourceAt < 0 && myc.resourceCount >= 3) {
					throwResourceAt = t + 1;
					throwResourceId = myc.resourceIds[0];
				}
			}
			if (THROW_RESOURCE && throwResourceAt >= 0) {
				int n = 0;
				double since = t - throwResourceAt;
				if (since >= 0 && since < 2) {
					double f = Math.min(1, since / 1.5);
					THROW_IDS[0] = throwResourceId;
					THROW_FLAGS[0] = Proto.THR_RESOURCE | (f >= 1 ? Proto.THR_LANDED : 0);
					double fx = -Math.sin(Math.toRadians(yaw)), fz = Math.cos(Math.toRadians(yaw));
					THROW_XYZ[0] = px + (0.3 + 3 * f) * fx;
					THROW_XYZ[1] = y + 1.2 + 1.5 * f - 2.7 * f * f;
					THROW_XYZ[2] = pz + (0.3 + 3 * f) * fz;
					n = 1;
					if (since < 0.02) {
						System.out.printf("t=%.2f throwing resource #%d%n", t, throwResourceId);
					}
				}
				MycLink.writeThrown(n, THROW_IDS, THROW_FLAGS, THROW_XYZ);
			}
			boolean rollNow = MycLink.readMycState(myc) && (myc.flags & Proto.MYC_ROLL) != 0;
			if (rollNow != rollSeen) {
				rollSeen = rollNow;
				System.out.printf("t=%.2f Mycelium's dodge roll %s (dash %.1f, %.1f blocks/s)%n", t, rollNow ? "on" : "over", myc.dashX, myc.dashZ);
			}
			boolean vanishedNow = MycLink.readMycState(myc) && (myc.flags & Proto.MYC_VANISHED) != 0;
			if (vanishedNow != vanishedSeen) {
				vanishedSeen = vanishedNow;
				System.out.printf("t=%.1f Mycelium's spore %s%n", t, vanishedNow ? "vanished (third person behind, body hidden)" : "is back");
			}
			if (MycLink.readMycState(myc) && (myc.dashX != 0f || myc.dashZ != 0f)) {
				if (dashFrames++ == 0) {
					System.out.printf("t=%.1f Mycelium's dash: (%.1f, %.1f) blocks/s%n", t, myc.dashX, myc.dashZ);
				}
			} else if (dashFrames > 0) {
				System.out.printf("t=%.1f Mycelium's dash over after %d frames%n", t, dashFrames);
				dashFrames = 0;
			}
			if (frames % 60 == 0) {
				boolean read = MycLink.readMycState(myc);
				System.out.printf("t=%.1f pitch=%.0f mc=(%.2f, %.2f, %.2f) | mycelium alive=%b read=%b flags=%d spore=(%.2f, %.2f, %.2f) window %dx%d%n",
					t, pitch, px, y, pz, MycLink.myceliumAlive(), read, myc.flags, myc.x, myc.y, myc.z, myc.windowW, myc.windowH);
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

	private static void forgeFrame(double t) {
		StringBuilder takes = new StringBuilder();
		for (int i = 0; i < forge.count; i++) {
			takes.append(i > 0 ? ", #" : "#").append(forge.ids[i]);
		}
		String seen = "a Blacksmith here " + ((forge.flags & Proto.FG_AVAILABLE) != 0) + ", he takes " + forge.count + " weapon(s) " + takes;
		if (!seen.equals(forgeSeen)) {
			forgeSeen = seen;
			System.out.printf("t=%.2f forge: %s%n", t, seen);
		}
		if (forgeOpenSeen == Integer.MIN_VALUE) {
			forgeOpenSeen = forge.openSeq;
		} else if (forge.openSeq != forgeOpenSeen) {
			forgeOpenSeen = forge.openSeq;
			System.out.printf("t=%.2f forge: Mycelium asks for the Blacksmith's screen (#%d)%n", t, forge.openSeq);
			if (forgeStage == 0 && forge.count >= 3) {
				forgeIds = new int[] { forge.ids[0], forge.ids[1], forge.ids[2] };
				MycLink.Weapon[] pre = new MycLink.Weapon[3];
				for (int i = 0; i < forgeTable.length; i++) {
					if (forgeTable[i] == null) {
						forgeTable[i] = new MycLink.Weapon();
					}
				}
				int wn = MycLink.readWeapons(forgeTable, forgeHeader);
				for (int i = 0; i < wn; i++) {
					for (int k = 0; k < 3; k++) {
						if (forgeTable[i].id == forgeIds[k]) {
							pre[k] = forgeTable[i];
						}
					}
				}
				if (pre[0] != null && pre[1] != null && pre[2] != null) {
					forgePreview = dev.myceliumcraft.ForgeName.forged(pre[0].name, pre[0].attributes, pre[1].attributes, pre[2].attributes);
					System.out.printf("t=%.2f forge: given '%s' (base), '%s', '%s'; the result preview's name: '%s'%n", t, pre[0].name, pre[1].name, pre[2].name, forgePreview);
				}
				forgePreviewSent = MycLink.forgePreviewSeq() + 1;
				MycLink.writeForgePreview(forgePreviewSent, forgeIds[0], forgeIds[1], forgeIds[2]);
				forgeStage = 3;
				System.out.printf("t=%.2f forge: slots #%d (base), #%d, #%d; asking for the result's numbers: preview #%d%n", t, forgeIds[0], forgeIds[1], forgeIds[2], forgePreviewSent);
			}
		}
		if (forgeStage == 3 && forge.previewHandled == forgePreviewSent && !forge.preview.name.isEmpty()) {
			MycLink.Weapon p = forge.preview;
			forgePreviewNumbers = String.format("'%s' %.1f dmg %.2f/s knockback %.1f x%.2f dmg x%.2f speed reach %.2f [%s]", p.name, p.damage, p.perSecond, p.knockback, p.damageMult, p.speedMod, p.reach, p.attributes);
			System.out.printf("t=%.2f forge: preview #%d answered: %s%n", t, forgePreviewSent, forgePreviewNumbers);
			forgeSent = MycLink.forgeRequestSeq() + 1;
			MycLink.writeForgeRequest(forgeSent, forgeIds[0], forgeIds[1], forgeIds[2]);
			forgeStage = 1;
			System.out.printf("t=%.2f forge: result taken: request #%d%n", t, forgeSent);
		}
		if (forgeSent != 0 && forge.handled == forgeSent) {
			for (int i = 0; i < forgeTable.length; i++) {
				if (forgeTable[i] == null) {
					forgeTable[i] = new MycLink.Weapon();
				}
			}
			int n = MycLink.readWeapons(forgeTable, forgeHeader);
			StringBuilder table = new StringBuilder();
			for (int i = 0; i < n; i++) {
				table.append(i > 0 ? "; " : "").append('#').append(forgeTable[i].id).append(' ').append(forgeTable[i].name)
					.append(String.format(" %.1f dmg x%.2f [%s]", forgeTable[i].damage, forgeTable[i].damageMult, forgeTable[i].attributes));
			}
			System.out.printf("t=%.2f forge: request #%d answered: weapon #%d%s; your weapons now: %s%n", t, forgeSent, forge.result,
				forge.reason.isEmpty() ? "" : " (refused: " + forge.reason + ")", table);
			for (int i = 0; i < n; i++) {
				if (forge.result != 0 && forgeTable[i].id == forge.result) {
					System.out.printf("t=%.2f forge: preview name '%s', Mycelium's forged name '%s': %s%n", t, forgePreview, forgeTable[i].name, forgeTable[i].name.equals(forgePreview) ? "SAME" : "DIFFERENT");
					MycLink.Weapon g = forgeTable[i];
					String got = String.format("'%s' %.1f dmg %.2f/s knockback %.1f x%.2f dmg x%.2f speed reach %.2f [%s]", g.name, g.damage, g.perSecond, g.knockback, g.damageMult, g.speedMod, g.reach, g.attributes);
					System.out.printf("t=%.2f forge: preview numbers %s%nt=%.2f forge: forged numbers  %s: %s%n", t, forgePreviewNumbers, t, got, got.equals(forgePreviewNumbers) ? "SAME" : "DIFFERENT");
				}
			}
			forgeSent = 0;
			if (forgeStage == 1) {
				forgeStage = 2;
				forgeSent = MycLink.forgeRequestSeq() + 1;
				MycLink.writeForgeRequest(forgeSent, forgeIds[0], forgeIds[1], forgeIds[2]);
				System.out.printf("t=%.2f forge: the same three again (gone now): request #%d%n", t, forgeSent);
			}
		}
	}

	private static boolean check(String what, double got, double expected) {
		boolean pass = Double.isNaN(expected) ? Double.isNaN(got) : Math.abs(got - expected) < 1e-6;
		System.out.printf("  %s %s: got %s, expected %s%n", pass ? "ok  " : "FAIL", what, got, expected);
		return pass;
	}
}
