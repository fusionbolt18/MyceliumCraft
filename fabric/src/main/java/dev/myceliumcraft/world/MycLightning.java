package dev.myceliumcraft.world;

import dev.myceliumcraft.link.MycLink;
import net.minecraft.core.BlockPos;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.level.levelgen.Heightmap;

/**
 * Lightning never strikes a player (your request, 2026-11-10: thunderstorms follow Mycelium's rain,
 * MycWeather). A thunderstorm's bolt (ServerLevel.tickThunder) aims at a random spot and moves onto
 * a creature nearby (findLightningTargetAround), so it would often hit you. A spot within SAFE
 * blocks of a player is moved AWAY blocks from them, on the ground there (LightningAimMixin): it
 * still strikes, so the thunder is heard, and its fire and its 3-block hit stay away. As well, a bolt
 * that reaches a player anyway (a trident, a command) doesn't hurt them (LightningHitMixin).
 * Server thread.
 */
public final class MycLightning {
	private static final double SAFE = 10.0;  // blocks: no strike this close to a player
	private static final double AWAY = 20.0;  // blocks: where a strike that was too close goes instead
	private static long nextLog;
	private static int moved;

	private MycLightning() {
	}

	/** The spot a thunderstorm's bolt strikes, moved away from every player if it was too close. */
	public static BlockPos awayFromPlayers(ServerLevel level, BlockPos pos) {
		BlockPos at = pos;
		for (int attempt = 0; attempt < 4; attempt++) {
			ServerPlayer near = null;
			double best = SAFE;
			for (ServerPlayer p : level.players()) {
				if (p.isSpectator()) {
					continue;
				}
				double d = Math.hypot(at.getX() + 0.5 - p.getX(), at.getZ() + 0.5 - p.getZ());
				if (d < best) {
					best = d;
					near = p;
				}
			}
			if (near == null) {
				break;
			}
			double dx = at.getX() + 0.5 - near.getX(), dz = at.getZ() + 0.5 - near.getZ();
			double d = Math.hypot(dx, dz);
			double angle = d > 0.5 ? Math.atan2(dz, dx) : level.getRandom().nextDouble() * Math.PI * 2;
			if (attempt > 0) {
				angle += level.getRandom().nextDouble() * Math.PI; // still near someone: try elsewhere
			}
			BlockPos flat = BlockPos.containing(near.getX() + Math.cos(angle) * AWAY, near.getY(), near.getZ() + Math.sin(angle) * AWAY);
			at = level.getHeightmapPos(Heightmap.Types.MOTION_BLOCKING, flat);
		}
		if (at != pos) {
			moved++;
			long now = System.currentTimeMillis();
			if (now >= nextLog) {
				nextLog = now + 10000;
				MycLink.LOG.info("MyceliumCraft: lightning: a strike aimed at {} was too close to a player; struck at {} instead ({} moved so far)", pos.toShortString(), at.toShortString(), moved);
			}
		}
		return at;
	}

	/** A bolt reached a player anyway: no hit. */
	public static void hitPrevented(ServerPlayer player) {
		MycLink.LOG.info("MyceliumCraft: lightning: a bolt reached {}; no hit (lightning can't hurt players)", player.getName().getString());
	}
}
