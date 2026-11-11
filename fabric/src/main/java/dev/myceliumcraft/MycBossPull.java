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
	private static boolean groundKeptLogged;
	// The last second of the pull (logged each second): what Mycelium asked for, what the move did,
	// and how much of it was with no movement keys held (your report, 2026-11-10: standing still,
	// you weren't pulled).
	private static int secTicks, secIdle, secBlocked;
	private static double secAsked, secMoved, secIdleMoved;

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
				groundKeptLogged = false;
				secTicks = secIdle = secBlocked = 0;
				secAsked = secMoved = secIdleMoved = 0;
				ticks = 0;
				moved = 0;
				from = player.position();
				MycLink.LOG.info("MyceliumCraft: the boss's pull moves the player ({} blocks/s)", String.format("%.2f", Math.hypot(x, z)));
			}
			Vec3 before = player.position();
			boolean grounded = player.onGround();
			player.move(MoverType.SELF, new Vec3(x / 20.0, 0.0, z / 20.0));
			// A move with no downward part always leaves the player "not on the ground" (Entity.move:
			// only a downward move that hits the floor counts), and the jump just after in this tick
			// needs the ground: you couldn't jump during the pull (your report, 2026-11-10). Kept as it
			// was; the player's own movement later in the tick sets it properly.
			if (grounded && !player.onGround()) {
				player.setOnGround(true);
				if (!groundKeptLogged) {
					groundKeptLogged = true;
					MycLink.LOG.info("MyceliumCraft: the boss's pull keeps the player on the ground, so it can jump");
				}
			}
			double step = player.position().subtract(before).horizontalDistance();
			double asked = Math.hypot(x, z) / 20.0;
			moved += step;
			ticks++;
			boolean idle = player.input.getMoveVector().lengthSquared() < 1e-6f;
			secTicks++;
			secAsked += asked;
			secMoved += step;
			if (idle) {
				secIdle++;
				secIdleMoved += step;
			}
			if (step < asked * 0.5) secBlocked++;
			if (secTicks >= 20) {
				MycLink.LOG.info("MyceliumCraft: the boss's pull, last second: asked {} blocks, moved {} ({} ticks blocked by something); {} of {} ticks with no movement keys, moved {} in those; on the ground {}, against a wall {}",
					String.format("%.2f", secAsked), String.format("%.2f", secMoved), secBlocked, secIdle, secTicks,
					String.format("%.2f", secIdleMoved), player.onGround(), player.horizontalCollision);
				secTicks = secIdle = secBlocked = 0;
				secAsked = secMoved = secIdleMoved = 0;
			}
		} else if (pulling) {
			pulling = false;
			double net = player != null && from != null ? Math.hypot(player.getX() - from.x, player.getZ() - from.z) : 0;
			MycLink.LOG.info("MyceliumCraft: the boss's pull is over: it moved the player {} blocks over {} ticks ({} blocks from where it started, walking included)",
				String.format("%.2f", moved), ticks, String.format("%.2f", net));
		}
	}
}
