package dev.myceliumcraft;

import com.mojang.blaze3d.platform.InputConstants;
import dev.myceliumcraft.link.MycLink;
import dev.myceliumcraft.link.Proto;
import net.fabricmc.fabric.api.client.keymapping.v1.KeyMappingHelper;
import net.minecraft.client.KeyMapping;
import net.minecraft.resources.Identifier;

/**
 * Mycelium's actions as ordinary Minecraft key binds, in their own "Mycelium" section of
 * Options -> Controls -> Key Binds. Minecraft does the editing, saving (options.txt), conflict
 * marking and "Reset"; this only tells Mycelium what each one is bound to (Bindings block in
 * protocol/PROTOCOL.md). These key binds do nothing in Minecraft itself.
 *
 * The order here is the action id in the protocol; Mycelium's KeyOwnership has the same list.
 */
public final class MyceliumKeys {
	private static final KeyMapping.Category CATEGORY = KeyMapping.Category.register(Identifier.fromNamespaceAndPath("myceliumcraft", "mycelium"));
	private static final int NONE = InputConstants.UNKNOWN.getValue();

	// Defaults are what you chose on 2026-10-17: R interact, Tab show stats, the rest left as
	// Mycelium had them or unbound.
	private static final KeyMapping[] KEYS = {
		key("interact", InputConstants.Type.KEYBOARD, InputConstants.KEY_R),
		key("show_stats", InputConstants.Type.KEYBOARD, InputConstants.KEY_TAB),
		key("subspecies_skill", InputConstants.Type.KEYBOARD, InputConstants.KEY_V),
		key("salvage", InputConstants.Type.MOUSE, InputConstants.MOUSE_BUTTON_MIDDLE),
		key("attack", InputConstants.Type.KEYBOARD, NONE),
		key("curio", InputConstants.Type.KEYBOARD, NONE),
		key("dodge", InputConstants.Type.KEYBOARD, NONE),
		key("skill_1", InputConstants.Type.KEYBOARD, NONE),
		key("skill_2", InputConstants.Type.KEYBOARD, NONE),
		key("navigate_camera", InputConstants.Type.KEYBOARD, NONE),
		key("reset_camera", InputConstants.Type.KEYBOARD, NONE),
	};

	// Two more after them: Minecraft's own Attack key, for Mycelium's hints (since v35; KeyHints.cs),
	// and its List Players key: a key bound to it and a Mycelium action works in both games (since v40).
	private static final int[] KINDS = new int[KEYS.length + 2];
	private static final int[] CODES = new int[KEYS.length + 2];
	private static String lastLogged = "";

	private MyceliumKeys() {
	}

	private static KeyMapping key(String name, InputConstants.Type type, int code) {
		return KeyMappingHelper.registerKeyMapping(new KeyMapping("key.myceliumcraft." + name, type, code, CATEGORY));
	}

	/** Load the class (registers the key binds; must happen before Minecraft reads options.txt). */
	public static void init() {
		MycLink.LOG.info("MyceliumCraft: {} Mycelium key binds registered", KEYS.length);
	}

	/** One of Minecraft's own key binds into slot a (after Mycelium's actions). */
	private static void minecraftKey(int a, KeyMapping mapping, String name, StringBuilder log) {
		InputConstants.Key bound = KeyMappingHelper.getBoundKeyOf(mapping);
		if (mapping.isUnbound() || bound.getValue() == NONE) {
			KINDS[a] = Proto.BIND_NONE;
			CODES[a] = 0;
		} else {
			KINDS[a] = bound.getType() == InputConstants.Type.MOUSE ? Proto.BIND_MOUSE : Proto.BIND_KEY;
			CODES[a] = bound.getValue();
		}
		log.append(", ").append(name).append('=').append(KINDS[a] == Proto.BIND_NONE ? "unbound" : (KINDS[a] == Proto.BIND_MOUSE ? "mouse " : "key ") + CODES[a]);
	}

	/** Every frame: tell Mycelium what its actions are bound to. */
	public static void publish() {
		StringBuilder log = new StringBuilder();
		for (int i = 0; i < KEYS.length; i++) {
			InputConstants.Key bound = KeyMappingHelper.getBoundKeyOf(KEYS[i]);
			if (KEYS[i].isUnbound() || bound.getValue() == NONE) {
				KINDS[i] = Proto.BIND_NONE;
				CODES[i] = 0;
			} else {
				KINDS[i] = bound.getType() == InputConstants.Type.MOUSE ? Proto.BIND_MOUSE : Proto.BIND_KEY;
				CODES[i] = bound.getValue();
			}
			log.append(i == 0 ? "" : ", ").append(KEYS[i].getName().substring("key.myceliumcraft.".length())).append('=')
				.append(KINDS[i] == Proto.BIND_NONE ? "unbound" : (KINDS[i] == Proto.BIND_MOUSE ? "mouse " : "key ") + CODES[i]);
		}
		var options = net.minecraft.client.Minecraft.getInstance().options;
		minecraftKey(KEYS.length, options.keyAttack, "minecraft_attack", log);
		minecraftKey(KEYS.length + 1, options.keyPlayerList, "minecraft_player_list", log);
		MycLink.writeBindings(KINDS, CODES, KEYS.length + 2);
		String now = log.toString();
		if (!now.equals(lastLogged)) {
			lastLogged = now;
			MycLink.LOG.info("MyceliumCraft: Mycelium key binds: {}", now);
		}
	}
}
