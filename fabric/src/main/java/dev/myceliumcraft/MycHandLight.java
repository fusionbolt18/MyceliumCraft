package dev.myceliumcraft;

import dev.myceliumcraft.link.MycLink;
import net.minecraft.client.renderer.entity.state.AvatarRenderState;
import net.minecraft.util.LightCoordsUtil;

/**
 * Minecraft's normal hand and held items in the overlay are always at full light level (your
 * report, 2026-11-04: they looked too dark at times). Minecraft lights them from the light level where your player stands,
 * but its world here is only Mycelium's ground as invisible blocks: under a cap or a rib, those
 * blocks shade you though Mycelium's picture around the hand may be bright. Render thread only.
 */
public final class MycHandLight {
	private static int lastRaw = -1;
	private static long lastLog;

	private MycHandLight() {
	}

	public static void brighten(AvatarRenderState avatar) {
		int raw = avatar.lightCoords;
		if (raw == LightCoordsUtil.FULL_BRIGHT) {
			return;
		}
		avatar.lightCoords = LightCoordsUtil.FULL_BRIGHT;
		long now = System.currentTimeMillis();
		if (raw != lastRaw && now - lastLog >= 1000) {
			lastRaw = raw;
			lastLog = now;
			MycLink.LOG.info("MyceliumCraft: hand light: Minecraft's was block {} sky {} of 15; drawn fully lit", LightCoordsUtil.block(raw), LightCoordsUtil.sky(raw));
		}
	}
}
