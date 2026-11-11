package dev.myceliumcraft;

import dev.myceliumcraft.link.MycLink;
import java.util.Optional;
import java.util.UUID;
import net.minecraft.client.Minecraft;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.client.waypoints.ClientWaypointManager;
import net.minecraft.core.BlockPos;
import net.minecraft.world.waypoints.TrackedWaypoint;
import net.minecraft.world.waypoints.Waypoint;
import net.minecraft.world.waypoints.WaypointStyleAssets;

/**
 * The level's goal on Minecraft's locator bar (your request, 2026-11-10). Mycelium sends where it is
 * (GoalWatch.cs: what its Navigate Camera turns to, Boss block since v61), and it's added to the
 * client's own waypoint list as a red bow-tie marker, drawn behind the level number
 * (HudLevelNumberMixin). Nothing goes through the server: it's only
 * on your screen. The locator bar turns with Minecraft's look, which Mycelium's camera follows.
 *
 * Minecraft shows the XP bar instead of the locator bar for 5 s after your experience changes, and
 * your experience level is Mycelium's nutrients, which change all the time. While there's a goal
 * that wait is skipped, so the locator bar stays; the level number is drawn over it as usual (the
 * XP bar itself is always empty). Client thread.
 */
public final class MycGoal {
	private static final UUID ID = UUID.nameUUIDFromBytes("myceliumcraft:level_goal".getBytes(java.nio.charset.StandardCharsets.UTF_8));
	// Red (your choice, 2026-11-10), fully opaque: the bar draws the colour as ARGB, and without the
	// FF the marker was invisible (your report, 2026-11-10: the locator bar was empty).
	private static final int COLOR = 0xFFFF0000;
	private static final float[] GOAL = new float[3];
	private static ClientWaypointManager trackedIn;
	private static BlockPos trackedAt;
	private static boolean barLogged;
	private static long nextLog;

	private MycGoal() {
	}

	/** The goal is on the locator bar now (HudLevelNumberMixin). */
	public static boolean onBar() {
		return trackedIn != null;
	}

	/** Start of a client tick (MyceliumCraftClient.beforeTick). on: Mycelium is in a level. */
	public static void tick(Minecraft minecraft, boolean on) {
		LocalPlayer player = minecraft.player;
		ClientWaypointManager manager = player != null && player.connection != null ? player.connection.getWaypointManager() : null;
		boolean shown = on && manager != null && MycLink.readGoal(GOAL);
		if (!shown) {
			clear(manager);
			return;
		}
		BlockPos at = BlockPos.containing(GOAL[0], GOAL[1], GOAL[2]);
		if (manager != trackedIn || !at.equals(trackedAt)) {
			boolean first = trackedIn == null;
			if (trackedIn != null) {
				trackedIn.untrackWaypoint(TrackedWaypoint.empty(ID));
			}
			Waypoint.Icon icon = new Waypoint.Icon();
			icon.style = WaypointStyleAssets.BOWTIE;
			icon.color = Optional.of(COLOR);
			manager.trackWaypoint(TrackedWaypoint.setPosition(ID, icon, at));
			trackedIn = manager;
			trackedAt = at;
			long now = System.currentTimeMillis();
			if (first || now >= nextLog) {
				nextLog = now + 10_000;
				// The bar only shows markers within 60 degrees of where you look.
				double toGoal = Math.toDegrees(Math.atan2(-(GOAL[0] - player.getX()), GOAL[2] - player.getZ()));
				double off = net.minecraft.util.Mth.wrapDegrees(toGoal - player.getYRot());
				MycLink.LOG.info("MyceliumCraft: goal: on the locator bar at ({}, {}, {}), {} blocks away, {} degrees {} of where you look ({}){}", at.getX(), at.getY(), at.getZ(),
					String.format("%.0f", Math.sqrt(player.distanceToSqr(GOAL[0], GOAL[1], GOAL[2]))), String.format("%.0f", Math.abs(off)), off < 0 ? "left" : "right",
					Math.abs(off) <= 60 ? "on the bar" : "off the bar's ends until you turn", first ? " (new)" : "");
			}
		}
		// Keep the locator bar up rather than the XP bar's 5 s after a nutrient change (see above).
		if (player.experienceDisplayStartTick > player.tickCount - 101) {
			player.experienceDisplayStartTick = player.tickCount - 101;
			if (!barLogged) {
				barLogged = true;
				MycLink.LOG.info("MyceliumCraft: goal: the locator bar stays up when your nutrients change (the level number still shows)");
			}
		}
	}

	private static void clear(ClientWaypointManager manager) {
		if (trackedIn == null) {
			return;
		}
		if (trackedIn == manager) {
			trackedIn.untrackWaypoint(TrackedWaypoint.empty(ID));
		}
		MycLink.LOG.info("MyceliumCraft: goal: none, taken off the locator bar");
		trackedIn = null;
		trackedAt = null;
		barLogged = false;
	}
}
