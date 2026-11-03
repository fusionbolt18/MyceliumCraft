package dev.myceliumcraft;

import dev.myceliumcraft.link.MycLink;
import java.util.UUID;
import net.minecraft.network.chat.Component;

/**
 * Your Minecraft player goes by the name of the spore you control in Mycelium (your request,
 * 2026-11-03), from MycState's spore name (protocol v41). Used by PlayerDisplayNameMixin (chat,
 * death messages, anything that asks for your display name) and PlayerTabNameMixin (the player
 * list). Only your own player, and only while Mycelium has a spore; otherwise your Minecraft name.
 * Set on the client's thread, read by the built-in server's too.
 */
public final class MycName {
	private static volatile String name = "";
	private static volatile UUID you;
	private static volatile Component component;

	private MycName() {
	}

	/** Every frame: the spore's name ("" = none) and your player's id. */
	public static void update(String sporeName, UUID player) {
		you = player;
		String now = sporeName == null ? "" : sporeName;
		if (now.equals(name)) {
			return;
		}
		MycLink.LOG.info("MyceliumCraft: name: your player goes by {}", now.isEmpty() ? "its Minecraft name again" : "'" + now + "'");
		name = now;
		component = now.isEmpty() ? null : Component.literal(now);
	}

	/** The spore's name for this player, or null to keep Minecraft's. */
	public static Component forPlayer(UUID id) {
		Component c = component;
		return c != null && id != null && id.equals(you) ? c : null;
	}

	/** The same, as text. */
	public static String textForPlayer(UUID id) {
		return forPlayer(id) != null ? name : null;
	}
}
