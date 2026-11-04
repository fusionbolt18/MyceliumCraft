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
	private static SporeBody body;
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
			super(level, new GameProfile(UUID.nameUUIDFromBytes("myceliumcraft-spore-body".getBytes(StandardCharsets.UTF_8)), "Spore"));
			this.setId(MADE_UP_ID); // the world gives its entities their ids; Minecraft's renderer asks for one
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
		// Your player is scaled to the spore's height (MyceliumCraftClient); the others are the same size.
		float scale = minecraft.player.getScale();
		if (scale != lastScale) {
			lastScale = scale;
			MycLink.LOG.info("MyceliumCraft: spore bodies: scaled x{} like your player", String.format("%.3f", scale));
		}
		int shown = 0;
		for (int i = 0; i < colony.count; i++) {
			if (i == colony.current) {
				clear(i); // that's you
				continue;
			}
			float swing = Math.min(1f, Math.max(0f, colony.speeds[i]) * SWING_PER_BLOCK_PER_SECOND);
			SWING[i] += (swing - SWING[i]) * Math.min(1f, dt * 10f);
			if (SWING[i] < 0.01f && swing == 0f) {
				SWING[i] = 0f;
			}
			WALKED[i] += SWING[i] * dt * 20f; // Minecraft's animation moves on per tick, 20 a second
			float emerge = colony.emerges[i];
			boolean moving = SWING[i] > 0f || emerge >= 0f;
			String colours = colony.caps[i] + "/" + colony.colors[i] + "/" + Math.round(scale * 1000f);
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
			if (!send(i, skin, scale, emerge, minecraft, partialTick)) {
				return; // logged; the next frame clears the slots
			}
			SENT[i] = colours;
			shown++;
		}
		clearFrom(colony.count);
		logShown(shown, colony.count);
	}

	private static boolean send(int slot, PlayerSkin skin, float scale, float emerge, Minecraft minecraft, float partialTick) {
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
			EmergePose.setCapturing(emerge >= 0f ? emerge : Float.NaN);
			try {
				if (MycPlayerModel.sendBody(slot, state, minecraft)) {
					return true;
				}
			} finally {
				EmergePose.setCapturing(Float.NaN);
			}
			MycLink.LOG.warn("MyceliumCraft: spore bodies: nothing came out for slot {}; the other spores stay as they are", slot);
		} catch (RuntimeException e) {
			MycLink.LOG.warn("MyceliumCraft: spore bodies: couldn't pose a body; the other spores stay as they are", e);
		}
		failed = true;
		return false;
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
