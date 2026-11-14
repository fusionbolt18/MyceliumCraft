package dev.myceliumcraft;

import dev.myceliumcraft.link.MycLink;
import net.minecraft.client.Minecraft;

/**
 * Minecraft options this mod sets every time Minecraft starts with it (your request, 2026-11-12).
 * Set once at the start of each run; you can still change them while playing, until the next start.
 *
 * <ul>
 * <li><b>Pause on Lost Focus</b> off (Minecraft's default is on). You play with Mycelium's window in
 * front, so Minecraft's window is never the focused one; with the option on, Minecraft opens its
 * pause menu whenever Mycelium isn't driving it (MycInputBridge only holds it off while it is).</li>
 * </ul>
 *
 * Done on the first client tick (Minecraft's options are loaded by then) and saved to options.txt.
 */
final class MycDefaults {
	private MycDefaults() {}

	private static boolean done;

	static void tick(Minecraft minecraft) {
		if (done || minecraft.options == null) return;
		done = true;
		boolean was = minecraft.options.pauseOnLostFocus;
		minecraft.options.pauseOnLostFocus = false;
		if (was) minecraft.options.save();
		MycLink.LOG.info("MyceliumCraft: defaults: Pause on Lost Focus set off at this start (it was {})", was ? "on" : "off");
	}
}
