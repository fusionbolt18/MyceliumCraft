package dev.myceliumcraft;

import dev.myceliumcraft.link.MycLink;
import net.minecraft.client.Minecraft;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.world.phys.Vec3;

/**
 * Mycelium's dashes (Blitz, the dodge roll) move Minecraft's player (your report, 2026-10-25).
 * Mycelium sends the dash's speed in blocks per second (MycState 0x6C/0x70, DashWatch.cs); at the
 * start of every client tick, before the player moves, its horizontal movement is set to that
 * (blocks per tick), keeping its vertical movement. Minecraft's own collisions apply, and the
 * spore follows Minecraft as always. Client thread.
 */
public final class MycDash {
	private static boolean dashing;
	private static int ticks;
	private static Vec3 from;

	private MycDash() {
	}

	/** Start of a client tick (MinecraftMixin). x, z: blocks per second; both 0 = no dash. */
	public static void tick(Minecraft minecraft, float x, float z) {
		LocalPlayer player = minecraft.player;
		boolean now = player != null && (x != 0f || z != 0f) && !player.isPassenger();
		if (now) {
			if (!dashing) {
				dashing = true;
				ticks = 0;
				from = player.position();
			}
			Vec3 d = player.getDeltaMovement();
			player.setDeltaMovement(x / 20.0, d.y, z / 20.0);
			ticks++;
		} else if (dashing) {
			dashing = false;
			double moved = player != null && from != null ? Math.hypot(player.getX() - from.x, player.getZ() - from.z) : 0;
			MycLink.LOG.info("MyceliumCraft: Mycelium's dash moved the player for {} ticks, {} blocks", ticks, String.format("%.2f", moved));
		}
	}
}
