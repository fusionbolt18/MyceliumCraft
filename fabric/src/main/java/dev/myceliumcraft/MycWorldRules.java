package dev.myceliumcraft;

import dev.myceliumcraft.link.MycLink;
import net.minecraft.client.Minecraft;
import net.minecraft.server.MinecraftServer;
import net.minecraft.world.level.gamerules.GameRules;

/**
 * Your own world's settings, in any world, while linked to Mycelium (your request, 2026-11-10: so
 * friends trying this play as you do). Read from your "New World" on 2026-11-10; everything else
 * there is Minecraft's default:
 * - game rule immediate_respawn on (no death screen; MyceliumCraftClient holds the death back until
 *   Mycelium's own death is over, as before);
 * - game rule advance_time off, and the time of day 6182 (just after noon), so it stays day.
 * On linking, the world's own values are remembered; when Mycelium goes away they're put back (the
 * time of day isn't: it just runs on from there again). Integrated server only (single player).
 */
public final class MycWorldRules {
	private static final boolean IMMEDIATE_RESPAWN = true;
	private static final boolean ADVANCE_TIME = false;
	private static final int TIME_OF_DAY = 6182;

	private static MinecraftServer appliedOn;
	private static Boolean hadRespawn, hadAdvance;

	private MycWorldRules() {
	}

	/** Every client tick. linked: Mycelium is running and linked. */
	public static void tick(Minecraft minecraft, boolean linked) {
		MinecraftServer server = minecraft.getSingleplayerServer();
		if (linked && server != null && minecraft.player != null && appliedOn != server) {
			appliedOn = server;
			server.execute(() -> apply(server));
		} else if ((!linked || server == null) && appliedOn != null) {
			MinecraftServer was = appliedOn;
			appliedOn = null;
			if (was == server) {
				server.execute(() -> restore(server));
			} else {
				hadRespawn = hadAdvance = null; // that world is closed: nothing to put back
			}
		}
	}

	private static void apply(MinecraftServer server) {
		GameRules rules = server.getGameRules();
		hadRespawn = rules.get(GameRules.IMMEDIATE_RESPAWN);
		hadAdvance = rules.get(GameRules.ADVANCE_TIME);
		rules.set(GameRules.IMMEDIATE_RESPAWN, IMMEDIATE_RESPAWN, server);
		rules.set(GameRules.ADVANCE_TIME, ADVANCE_TIME, server);
		int result;
		try {
			result = server.getCommands().getDispatcher().execute("time set " + TIME_OF_DAY, server.createCommandSourceStack().withSuppressedOutput());
		} catch (Exception e) {
			result = -1;
			MycLink.LOG.warn("MyceliumCraft: world rules: 'time set {}' failed: {}", TIME_OF_DAY, e.getMessage());
		}
		MycLink.LOG.info("MyceliumCraft: world rules: linked to Mycelium: immediate_respawn {} (was {}), advance_time {} (was {}), time of day set to {} ({})",
			IMMEDIATE_RESPAWN, hadRespawn, ADVANCE_TIME, hadAdvance, TIME_OF_DAY, result >= 0 ? "ok, result " + result : "failed");
	}

	private static void restore(MinecraftServer server) {
		if (hadRespawn == null || hadAdvance == null) {
			return;
		}
		GameRules rules = server.getGameRules();
		rules.set(GameRules.IMMEDIATE_RESPAWN, hadRespawn, server);
		rules.set(GameRules.ADVANCE_TIME, hadAdvance, server);
		MycLink.LOG.info("MyceliumCraft: world rules: Mycelium gone: immediate_respawn back to {}, advance_time back to {}", hadRespawn, hadAdvance);
		hadRespawn = hadAdvance = null;
	}
}
