package dev.myceliumcraft.world;

/**
 * Mycelium's health is in charge of Minecraft's (your choice, 2026-10-19): Minecraft health =
 * Mycelium health / 5 (5 Mycelium health = half a heart), max health likewise. Set from the client
 * every frame; read on the integrated server's thread by HealthFloorMixin, which keeps a player's
 * Minecraft health above 0 while Mycelium's is: only Mycelium's death kills the Minecraft player.
 */
public final class MycHealth {
	/** Mycelium health per Minecraft health point (half a heart). */
	public static final float PER_POINT = 5f;
	/** The lowest Minecraft health while Mycelium's is above 0 (still shows half a heart). */
	public static final float FLOOR = 0.01f;

	private static volatile boolean syncing;
	private static volatile float target; // Minecraft points

	private MycHealth() {
	}

	public static void set(boolean on, float minecraftPoints) {
		syncing = on;
		target = minecraftPoints;
	}

	/** True while Minecraft health must stay above 0 (Mycelium alive, synced). */
	public static boolean protecting() {
		return syncing && target > 0f;
	}
}
