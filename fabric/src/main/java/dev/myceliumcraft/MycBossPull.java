package dev.myceliumcraft;

import dev.myceliumcraft.link.MycLink;
import net.minecraft.client.Minecraft;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.world.entity.MoverType;
import net.minecraft.world.phys.Vec3;

/**
 * The final boss's sand pull moves Minecraft's player (your report, 2026-11-09: it didn't move you
 * at all). Mycelium sends the speed its pull would add to your walking (Boss block, PullWatch.cs), in
 * blocks per second. At the start of every client tick the player is moved by a tick's worth of it
 * (Entity.move, so Minecraft's collisions apply), on top of its own movement. Its own velocity isn't
 * changed: added there, Minecraft's friction would make it build up to about twice as much. Client
 * thread.
 */
public final class MycBossPull {
	private static final float[] PULL = new float[2];
	private static boolean pulling;
	private static int ticks;
	private static Vec3 from;
	private static double moved;

	private MycBossPull() {
	}

	/** Start of a client tick (MyceliumCraftClient.beforeTick). on: Mycelium is in a level. */
	public static void tick(Minecraft minecraft, boolean on) {
		LocalPlayer player = minecraft.player;
		float x = 0f, z = 0f;
		if (on && MycLink.readBossPull(PULL)) {
			x = PULL[0];
			z = PULL[1];
		}
		boolean now = player != null && (x != 0f || z != 0f) && !player.isPassenger() && !player.isSpectator();
		if (now) {
			if (!pulling) {
				pulling = true;
				ticks = 0;
				moved = 0;
				from = player.position();
				MycLink.LOG.info("MyceliumCraft: the boss's pull moves the player ({} blocks/s)", String.format("%.2f", Math.hypot(x, z)));
			}
			Vec3 before = player.position();
			player.move(MoverType.SELF, new Vec3(x / 20.0, 0.0, z / 20.0));
			moved += player.position().subtract(before).horizontalDistance();
			ticks++;
		} else if (pulling) {
			pulling = false;
			double net = player != null && from != null ? Math.hypot(player.getX() - from.x, player.getZ() - from.z) : 0;
			MycLink.LOG.info("MyceliumCraft: the boss's pull is over: it moved the player {} blocks over {} ticks ({} blocks from where it started, walking included)",
				String.format("%.2f", moved), ticks, String.format("%.2f", net));
		}
	}
}
