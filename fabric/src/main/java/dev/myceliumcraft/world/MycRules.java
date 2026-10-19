package dev.myceliumcraft.world;

import dev.myceliumcraft.link.MycLink;

/**
 * Minecraft rules changed by the mod, always (your choice, 2026-10-19; in preparation for combat,
 * where Mycelium decides about health): no fall damage for players, and no natural health
 * regeneration (from food, or Peaceful's).
 *
 * Done in the mod, not with Minecraft's game rules, so nothing is saved into your worlds: without
 * the mod installed, Minecraft is normal again. Read by the mixins on the integrated server's thread.
 */
public final class MycRules {
	private static long lastFallLog, lastHealLog;

	private MycRules() {
	}

	/** The rules are on (always, while the mod is installed). */
	public static boolean active() {
		return true;
	}

	/** Log a prevented fall (at most once a second). */
	public static void fallPrevented(double distance) {
		long now = System.currentTimeMillis();
		if (now - lastFallLog >= 1000) {
			lastFallLog = now;
			MycLink.LOG.info("MyceliumCraft: fall damage prevented (fell {} blocks)", String.format("%.1f", distance));
		}
	}

	/** Log a prevented heal (at most once a second). */
	public static void healPrevented(String how, float amount) {
		long now = System.currentTimeMillis();
		if (now - lastHealLog >= 1000) {
			lastHealLog = now;
			MycLink.LOG.info("MyceliumCraft: natural regeneration prevented ({}, {} health)", how, amount);
		}
	}
}
