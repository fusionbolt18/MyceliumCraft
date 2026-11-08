package dev.myceliumcraft;

import dev.myceliumcraft.link.MycLink;
import java.nio.charset.StandardCharsets;
import java.util.UUID;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.minecraft.network.chat.Component;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerBossEvent;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.BossEvent;

/**
 * Mycelium's final boss as Minecraft's own boss bar (your request, 2026-11-07; BossBar.cs). One
 * bar on the integrated server, shown to the players while Mycelium says a boss bar is up, with its
 * name and health. Minecraft's client eases the bar to new health itself. Server thread.
 */
public final class MycBossBar {
	private static final ServerBossEvent BAR = new ServerBossEvent(
		UUID.nameUUIDFromBytes("myceliumcraft-boss".getBytes(StandardCharsets.UTF_8)),
		Component.literal("Boss"), BossEvent.BossBarColor.RED, BossEvent.BossBarOverlay.PROGRESS);
	private static final MycLink.Boss BOSS = new MycLink.Boss();
	private static String lastName = "";
	private static boolean lastShown;

	private MycBossBar() {
	}

	public static void init() {
		BAR.setVisible(false);
		ServerTickEvents.END_SERVER_TICK.register(MycBossBar::tick);
	}

	private static void tick(MinecraftServer server) {
		boolean shown = MycLink.myceliumAlive() && MycLink.readBoss(BOSS) && BOSS.shown;
		if (shown) {
			if (!BOSS.name.equals(lastName)) {
				lastName = BOSS.name;
				BAR.setName(Component.literal(BOSS.name.isEmpty() ? "Boss" : BOSS.name));
			}
			BAR.setProgress(Math.max(0f, Math.min(1f, BOSS.progress)));
			for (ServerPlayer player : server.getPlayerList().getPlayers()) {
				if (!BAR.getPlayers().contains(player)) {
					BAR.addPlayer(player);
				}
			}
		}
		if (shown != lastShown) {
			lastShown = shown;
			BAR.setVisible(shown);
			if (!shown) {
				BAR.removeAllPlayers();
			}
			MycLink.LOG.info("MyceliumCraft: boss bar: {}", shown ? "'" + BOSS.name + "' shown at " + Math.round(BOSS.progress * 100) + "%" : "gone");
		}
	}
}
