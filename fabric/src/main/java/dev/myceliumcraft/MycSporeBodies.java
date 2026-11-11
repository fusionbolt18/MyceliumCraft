package dev.myceliumcraft;

import com.mojang.authlib.GameProfile;
import dev.myceliumcraft.link.MycLink;
import dev.myceliumcraft.link.Proto;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import java.util.UUID;
import net.minecraft.client.Minecraft;
import net.minecraft.client.multiplayer.ClientLevel;
import net.minecraft.client.player.RemotePlayer;
import net.minecraft.client.renderer.entity.state.AvatarRenderState;
import net.minecraft.client.renderer.entity.state.EntityRenderState;
import net.minecraft.world.entity.player.PlayerSkin;

/**
 * The other spores of your colony as Minecraft characters (your request, 2026-11-03). One made-up
 * player, never added to the world, is posed by Minecraft's own renderer once per spore: in that
 * spore's skin (MycSkins), facing south, its legs and arms swinging with the speed Mycelium sends.
 * MycPlayerModel takes the triangles, and they go out in the spore's Bodies slot. Mycelium stands
 * them at the spore's feet and turns them to where the spore faces (McColonyBodies.cs), so where
 * it walks never comes through here. A spore grown in the shop digs out of the ground as yours does
 * in The Carcass (EmergePose), for as long as Mycelium's Sprout plays on it. A spore standing still
 * isn't sent again. Render thread only.
 *
 * Waving (your request, 2026-11-08): when Mycelium says a spore waves (Colony's waving bits: its
 * Animator in "Hello"), that spore gets a made-up player of its own and Emotecraft's "Waving" emote
 * plays on it (MycEmotes; nothing without Emotecraft). It's ticked here once per game tick, as the
 * world would tick it, and posed every frame until the emote is over (2.5 s); then the spore goes
 * back to the shared body.
 *
 * Your emote back (your request, 2026-11-10): if you're doing an Emotecraft emote, or did within
 * the last 3 s, a spore that waves back does that emote instead (MycEmotes.answer). Which spores
 * answer is Mycelium's own wave-back rule (near you, in front of you, happy enough). A copy stops
 * once you've stopped and it has played as long as yours did.
 *
 * The beetle drum (your request, 2026-11-09): while Mycelium says a spore is in its "Drum Hit"
 * (Colony drums: how far through, 0..1), its body is posed as DrumPose, on the shared body.
 *
 * Decorations (your request, 2026-11-09): a spore sitting or lying at one (Colony poses) is posed
 * as you are (MycPlayerModel.sporeDecorationPose): on its bed, fitted to Mycelium's ground where it
 * stands (SporeMoves positions) and the way it faces (Colony yaws).
 *
 * Dancing at the beetle drum (Colony poses 4, your request, 2026-11-09): like waving, the spore gets
 * its own made-up player and one of MycEmotes.DANCES, picked at random, plays on it for as long as
 * it dances (played again if it ends: "Over here" doesn't loop).
 */
public final class MycSporeBodies {
	/** Minecraft's walking animation: 1 at 5 blocks a second, a little over its walking speed. */
	private static final float SWING_PER_BLOCK_PER_SECOND = 0.2f;
	private static final int MADE_UP_ID = -2_000_001; // real entities count up from 1

	private static final String[] SENT = new String[Proto.COL_MAX]; // each slot's colours as sent; null = empty
	private static final float[] SWING = new float[Proto.COL_MAX];
	private static final float[] WALKED = new float[Proto.COL_MAX];
	private static final boolean[] MOVING = new boolean[Proto.COL_MAX];
	private static final boolean[] EMERGING = new boolean[Proto.COL_MAX];
	private static final boolean[] DRUMMING = new boolean[Proto.COL_MAX];
	private static final float[] POSITIONS = new float[Proto.COL_MAX * 3];
	private static SporeBody body;
	private static final SporeBody[] WAVER = new SporeBody[Proto.COL_MAX]; // a waving spore's own body, while the emote plays
	private static final boolean[] WAVE_WAS = new boolean[Proto.COL_MAX];
	private static final Object[] COPIED = new Object[Proto.COL_MAX]; // your emote a spore does back (MycEmotes.answer), null = its own wave
	private static final long[] COPIED_SINCE = new long[Proto.COL_MAX];
	private static final boolean[] STARTED = new boolean[1];
	private static final int[] DANCE = new int[Proto.COL_MAX]; // the dance on WAVER (MycEmotes.DANCES), -1 = none
	static {
		Arrays.fill(DANCE, -1);
	}
	private static long lastGameTime = Long.MIN_VALUE;
	private static long lastNanos;
	private static int lastShown = -1;
	private static float lastScale = -1f;
	private static boolean failed;

	private MycSporeBodies() {
	}

	/** A made-up player in a spore's skin. */
	private static final class SporeBody extends RemotePlayer {
		PlayerSkin skin;

		SporeBody(ClientLevel level) {
			this(level, "myceliumcraft-spore-body", MADE_UP_ID);
		}

		SporeBody(ClientLevel level, String key, int id) {
			super(level, new GameProfile(UUID.nameUUIDFromBytes(key.getBytes(StandardCharsets.UTF_8)), "Spore"));
			this.setId(id); // the world gives its entities their ids; Minecraft's renderer asks for one
		}

		@Override
		public PlayerSkin getSkin() {
			return this.skin;
		}
	}

	/** New link: Mycelium has no bodies, send them all again. */
	public static void reset() {
		Arrays.fill(SENT, null);
		Arrays.fill(MOVING, false);
		Arrays.fill(EMERGING, false);
		Arrays.fill(DRUMMING, false);
		Arrays.fill(WAVER, null);
		Arrays.fill(WAVE_WAS, false);
		Arrays.fill(DANCE, -1);
		lastShown = -1;
		failed = false;
	}

	/** After every frame, after your own body. */
	public static void frame(Minecraft minecraft, boolean wanted, float partialTick) {
		MycLink.Colony colony = wanted ? MycColony.colony() : null;
		if (colony == null || minecraft.player == null || minecraft.level == null || failed) {
			clearFrom(0);
			logShown(0, 0);
			return;
		}
		long now = System.nanoTime();
		float dt = lastNanos == 0 ? 0f : Math.min(0.25f, (now - lastNanos) / 1e9f);
		lastNanos = now;
		if (body == null || body.level() != minecraft.level) {
			body = new SporeBody(minecraft.level);
		}
		body.setPos(minecraft.player.position());
		// Game ticks since the last frame: the waving bodies' animations move on by as many.
		long gameTime = minecraft.level.getGameTime();
		int ticks = lastGameTime == Long.MIN_VALUE ? 0 : (int) Math.max(0, Math.min(10, gameTime - lastGameTime));
		lastGameTime = gameTime;
		// Your player is scaled to the spore's height (MyceliumCraftClient); the others are the same size.
		float scale = minecraft.player.getScale();
		if (scale != lastScale) {
			lastScale = scale;
			MycLink.LOG.info("MyceliumCraft: spore bodies: scaled x{} like your player", String.format("%.3f", scale));
		}
		int shown = 0;
		int placed = MycLink.readSporePositions(POSITIONS); // where the other spores stand (feet, Minecraft blocks)
		for (int i = 0; i < colony.count; i++) {
			if (i == colony.current) {
				clear(i); // that's you
				endWave(i, "is you now");
				continue;
			}
			boolean dancing = colony.poses[i] == 4;
			if (DANCE[i] >= 0 && !dancing) {
				endWave(i, "stops dancing");
				MOVING[i] = true;
			}
			if (dancing && WAVER[i] == null && MycEmotes.available()) {
				SporeBody own = new SporeBody(minecraft.level, "myceliumcraft-spore-wave-" + i, MADE_UP_ID - 1 - i);
				int which = MycEmotes.pickDance();
				if (MycEmotes.dance(own, which)) {
					WAVER[i] = own;
					DANCE[i] = which;
					MycLink.LOG.info("MyceliumCraft: spore bodies: {} dances at the beetle drum: Emotecraft's '{}'", colony.names[i], MycEmotes.DANCE_NAMES[which]);
				}
			}
			boolean waving = (colony.waving & (1 << i)) != 0;
			if (waving && !WAVE_WAS[i] && WAVER[i] == null) {
				SporeBody own = new SporeBody(minecraft.level, "myceliumcraft-spore-wave-" + i, MADE_UP_ID - 1 - i);
				// Your emote back if you're doing one (or just did), else Emotecraft's wave (your request, 2026-11-10).
				COPIED[i] = MycEmotes.answer(own, colony.names[i], STARTED);
				COPIED_SINCE[i] = System.currentTimeMillis();
				if (STARTED[0]) {
					WAVER[i] = own;
				}
			}
			WAVE_WAS[i] = waving;
			if (WAVER[i] != null) {
				WAVER[i].setPos(minecraft.player.position());
				for (int t = 0; t < ticks; t++) {
					MycEmotes.tick(WAVER[i]);
				}
				if (COPIED[i] != null && MycEmotes.playing(WAVER[i]) && MycEmotes.copyOver(COPIED[i], COPIED_SINCE[i])) {
					endWave(i, "stops doing your emote (you stopped, and it did it as long as you)");
					MOVING[i] = true;
				} else if (!MycEmotes.playing(WAVER[i])) {
					if (DANCE[i] >= 0 && dancing) {
						MycEmotes.dance(WAVER[i], DANCE[i]); // "Over here" doesn't loop: again while it dances
					} else {
						endWave(i, "is done waving");
						MOVING[i] = true; // posed once more on the shared body, out of the wave
					}
				}
			}
			float swing = Math.min(1f, Math.max(0f, colony.speeds[i]) * SWING_PER_BLOCK_PER_SECOND);
			SWING[i] += (swing - SWING[i]) * Math.min(1f, dt * 10f);
			if (SWING[i] < 0.01f && swing == 0f) {
				SWING[i] = 0f;
			}
			WALKED[i] += SWING[i] * dt * 20f; // Minecraft's animation moves on per tick, 20 a second
			float emerge = colony.emerges[i];
			float drum = colony.drums[i];
			int pose = colony.poses[i];
			net.minecraft.world.phys.Vec3 stand = i < placed && !Float.isNaN(POSITIONS[i * 3 + 1])
				? new net.minecraft.world.phys.Vec3(POSITIONS[i * 3], POSITIONS[i * 3 + 1], POSITIONS[i * 3 + 2]) : null;
			boolean moving = SWING[i] > 0f || emerge >= 0f || drum >= 0f || WAVER[i] != null;
			// The pose is part of what was sent: a body sitting down or getting up is sent again. Lying,
			// it's fitted to the bed where the spore stands, to the tenth of a block.
			String colours = colony.caps[i] + "/" + colony.colors[i] + "/" + Math.round(scale * 1000f) + "/" + pose
				+ (pose >= 2 && stand != null ? "/" + Math.round(stand.x * 10) + "," + Math.round(stand.y * 10) + "," + Math.round(stand.z * 10) + "," + Math.round(colony.yaws[i]) : "");
			if (colours.equals(SENT[i]) && !moving && !MOVING[i]) {
				shown++;
				continue;
			}
			MOVING[i] = moving;
			PlayerSkin skin = MycSkins.skin(colony.caps[i], colony.colors[i]);
			if (skin == null) {
				clear(i);
				continue;
			}
			if (emerge >= 0f != EMERGING[i]) {
				EMERGING[i] = emerge >= 0f;
				MycLink.LOG.info("MyceliumCraft: spore bodies: {} {}", colony.names[i], EMERGING[i] ? "digs out of the ground" : "is out of the ground");
			}
			if (drum >= 0f != DRUMMING[i]) {
				DRUMMING[i] = drum >= 0f;
				MycLink.LOG.info("MyceliumCraft: spore bodies: {} {}", colony.names[i], DRUMMING[i] ? "hits the beetle drum (Mycelium's Drum Hit, from " + String.format("%.2f", drum) + ")" : "is done with that drum hit");
			}
			if (!send(WAVER[i] != null ? WAVER[i] : body, i, skin, scale, emerge, drum, pose, stand, colony.yaws[i], colony.names[i], minecraft, partialTick)) {
				return; // logged; the next frame clears the slots
			}
			SENT[i] = colours;
			shown++;
		}
		clearFrom(colony.count);
		logShown(shown, colony.count);
	}

	private static boolean send(SporeBody body, int slot, PlayerSkin skin, float scale, float emerge, float drum, int pose,
			net.minecraft.world.phys.Vec3 stand, float yaw, String name, Minecraft minecraft, float partialTick) {
		try {
			body.skin = skin;
			EntityRenderState state = minecraft.getEntityRenderDispatcher().extractEntity(body, partialTick);
			state.nameTag = null;
			if (state instanceof AvatarRenderState avatar) {
				avatar.bodyRot = 0f;
				avatar.yRot = 0f;
				avatar.xRot = 0f;
				avatar.scale = scale;
				avatar.walkAnimationPos = WALKED[slot];
				avatar.walkAnimationSpeed = SWING[slot];
				avatar.showHat = true; // the cap's outer layer (a made-up player has its outer layers off)
				avatar.showJacket = avatar.showLeftSleeve = avatar.showRightSleeve = avatar.showLeftPants = avatar.showRightPants = true;
			}
			com.mojang.blaze3d.vertex.PoseStack poses = new com.mojang.blaze3d.vertex.PoseStack();
			MycPlayerModel.sporeDecorationPose(state, poses, slot, name, pose, stand, yaw);
			EmergePose.setCapturing(emerge >= 0f ? emerge : Float.NaN);
			DrumPose.setCapturing(drum >= 0f ? drum : Float.NaN);
			try {
				if (MycPlayerModel.sendBody(slot, state, minecraft, poses)) {
					return true;
				}
			} finally {
				EmergePose.setCapturing(Float.NaN);
				DrumPose.setCapturing(Float.NaN);
			}
			MycLink.LOG.warn("MyceliumCraft: spore bodies: nothing came out for slot {}; the other spores stay as they are", slot);
		} catch (RuntimeException e) {
			MycLink.LOG.warn("MyceliumCraft: spore bodies: couldn't pose a body; the other spores stay as they are", e);
		}
		failed = true;
		return false;
	}

	private static void endWave(int slot, String why) {
		DANCE[slot] = -1;
		COPIED[slot] = null;
		if (WAVER[slot] != null) {
			MycEmotes.stop(WAVER[slot]);
			WAVER[slot] = null;
			MycLink.LOG.info("MyceliumCraft: spore bodies: slot {} {}; back to the shared body", slot, why);
		}
	}

	private static void clear(int slot) {
		if (SENT[slot] != null) {
			SENT[slot] = null;
			MOVING[slot] = false;
			MycLink.writeBody(slot, 0, null, 0);
		}
	}

	private static void clearFrom(int first) {
		for (int i = first; i < Proto.COL_MAX; i++) {
			clear(i);
		}
	}

	private static void logShown(int shown, int count) {
		if (shown != lastShown) {
			lastShown = shown;
			MycLink.LOG.info("MyceliumCraft: spore bodies: {} sent to Mycelium ({} spore(s) in the colony)", shown, count);
		}
	}
}
