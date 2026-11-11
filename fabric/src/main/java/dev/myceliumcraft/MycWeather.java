package dev.myceliumcraft;

import dev.myceliumcraft.link.MycLink;
import net.minecraft.client.Minecraft;

/**
 * Minecraft's weather follows Mycelium's (your request, 2026-11-10): while it rains in Mycelium
 * (MycState RAIN, WeatherSettingsScript.isRaining) Minecraft has a thunderstorm, otherwise clear
 * weather. Set on the integrated server as /weather does (MinecraftServer.setWeatherParameters),
 * when Mycelium's weather changes and again every 30 s (each setting lasts 10 minutes, so
 * Minecraft's own weather cycle never takes over while linked). Not linked or not in a level:
 * Minecraft's weather is left alone. Lightning never hits a player (world/MycLightning).
 * Client thread; the change itself runs on the server thread.
 */
public final class MycWeather {
	private static final int DURATION = 12000; // ticks (10 minutes)
	private static final long REFRESH_MS = 30000;
	private static Boolean applied; // the weather set last (true: thunderstorm), null = not set since linking
	private static long nextRefresh;

	private MycWeather() {
	}

	/** Every client tick. on: Mycelium is in a level. raining: it rains there. */
	public static void tick(Minecraft minecraft, boolean on, boolean raining) {
		var server = minecraft.getSingleplayerServer();
		if (!on || server == null) {
			applied = null;
			return;
		}
		long now = System.currentTimeMillis();
		if (applied != null && applied == raining && now < nextRefresh) {
			return;
		}
		boolean changed = applied == null || applied != raining;
		applied = raining;
		nextRefresh = now + REFRESH_MS;
		server.execute(() -> server.setWeatherParameters(raining ? 0 : DURATION, raining ? DURATION : 0, raining, raining));
		if (changed) {
			MycLink.LOG.info("MyceliumCraft: weather: {}", raining ? "it rains in Mycelium: Minecraft has a thunderstorm (lightning can't hit you)" : "no rain in Mycelium: Minecraft's weather is clear");
		}
	}
}
