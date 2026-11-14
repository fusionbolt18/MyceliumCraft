package dev.myceliumcraft;

import com.mojang.brigadier.arguments.StringArgumentType;
import dev.myceliumcraft.link.MycLink;
import dev.myceliumcraft.link.Proto;
import net.fabricmc.fabric.api.client.command.v2.ClientCommands;
import net.fabricmc.fabric.api.client.command.v2.ClientCommandRegistrationCallback;
import net.minecraft.client.Minecraft;
import net.minecraft.network.chat.Component;

/**
 * /changename &lt;name&gt; (your request, 2026-11-12): renames the spore you play in Mycelium. Only when
 * Mycelium was started with -freecam (MycState's FREECAM flag); otherwise the command says so and
 * nothing is sent. A client command: it runs in your game, not on the world's server, so it works
 * in any world without cheats.
 *
 * The request goes in McState (rename number and name, protocol v65); Mycelium's Freecam.Rename
 * does it. It worked when MycState's spore name becomes the new name; the chat says so, or after 3
 * seconds that it didn't (Mycelium's log says why).
 */
final class MycChangeName {
	private MycChangeName() {}

	private static volatile boolean linked, freecam, inLevel;
	private static volatile String current = "";
	private static String waitingFor;
	private static long waitUntil;

	static void init() {
		ClientCommandRegistrationCallback.EVENT.register((dispatcher, context) -> dispatcher.register(
			ClientCommands.literal("changename")
				.then(ClientCommands.argument("name", StringArgumentType.greedyString())
					.executes(c -> {
						String name = StringArgumentType.getString(c, "name").trim();
						if (!linked) {
							c.getSource().sendError(Component.literal("Mycelium isn't running."));
							return 0;
						}
						if (!freecam) {
							c.getSource().sendError(Component.literal("/changename only works when Mycelium is started with -freecam."));
							return 0;
						}
						if (!inLevel) {
							c.getSource().sendError(Component.literal("You don't have a spore in Mycelium right now."));
							return 0;
						}
						if (name.isEmpty()) {
							c.getSource().sendError(Component.literal("Give a name: /changename <name>"));
							return 0;
						}
						byte[] bytes = name.getBytes(java.nio.charset.StandardCharsets.UTF_8);
						if (bytes.length > Proto.MC_RENAME_BYTES - 1) {
							c.getSource().sendError(Component.literal("That name is too long (at most " + (Proto.MC_RENAME_BYTES - 1) + " bytes)."));
							return 0;
						}
						MycLink.renameName = name;
						MycLink.renameSeq = MycLink.renameSeq + 1;
						waitingFor = name;
						waitUntil = System.currentTimeMillis() + 3000;
						MycLink.LOG.info("MyceliumCraft: /changename: asked Mycelium to rename '{}' to '{}' (request {})", current, name, MycLink.renameSeq);
						c.getSource().sendFeedback(Component.literal("Renaming " + current + " to " + name + "..."));
						return 1;
					}))));
	}

	/** Every frame, from MyceliumCraftClient: what Mycelium says now, and the pending request's outcome. */
	static void update(Minecraft minecraft, boolean mycelium, int flags, boolean level, String sporeName) {
		linked = mycelium;
		freecam = mycelium && (flags & Proto.MYC_FREECAM) != 0;
		inLevel = mycelium && level;
		current = sporeName == null ? "" : sporeName;
		if (waitingFor == null) return;
		if (current.equals(waitingFor)) {
			MycLink.LOG.info("MyceliumCraft: /changename: Mycelium says your spore is now '{}'", current);
			say(minecraft, "Your spore is now called " + current + ".");
			waitingFor = null;
		} else if (System.currentTimeMillis() > waitUntil) {
			MycLink.LOG.info("MyceliumCraft: /changename: Mycelium didn't rename it to '{}' within 3 s (its name is '{}')", waitingFor, current);
			say(minecraft, "Mycelium didn't rename your spore (its log says why).");
			waitingFor = null;
		}
	}

	private static void say(Minecraft minecraft, String text) {
		if (minecraft.player != null) minecraft.player.sendSystemMessage(Component.literal(text));
	}
}
