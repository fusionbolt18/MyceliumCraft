package dev.myceliumcraft;

import dev.myceliumcraft.link.MycLink;
import net.minecraft.client.gui.screens.worldselection.WorldCreationUiState;
import net.minecraft.server.MinecraftServer;
import net.minecraft.world.Difficulty;
import net.minecraft.world.level.gamerules.GameRules;
import net.minecraft.world.level.levelgen.presets.WorldPresets;

/**
 * The "Mycelium" game mode in Create New World (your request, 2026-11-10): a world like your own
 * "New World", read from it on 2026-11-10 (level.dat, game_rules.dat, world_gen_settings.dat,
 * world_clocks.dat, weather.dat):
 * - plays as Survival (your choice, 2026-11-10; your own world is Creative), Peaceful, cheats allowed;
 * - world type Superflat with Minecraft's default layers (bedrock, 2 dirt, grass; plains; villages
 *   and strongholds; no lakes or decorations: exactly what yours has), structures on, no bonus chest;
 * - game rules: immediate_respawn on, advance_time off (everything else there is the default);
 * - once the world has started: time of day 6182 (just after noon) and clear weather.
 * The seed stays random. Choosing the mode fills these in; you can still change them before creating.
 * The entry itself is added by SelectedGameModeMixin and CreateWorldGameTabMixin.
 */
public final class MycWorldMode {
	/** The "Mycelium" game mode entry, set when Minecraft makes its list (SelectedGameModeMixin). */
	public static WorldCreationUiState.SelectedGameMode MYCELIUM;

	private static final int TIME_OF_DAY = 6182;
	private static WorldCreationUiState watching;
	private static boolean wasMycelium, applying, pending;

	private MycWorldMode() {
	}

	/** Every change on the Create New World screen. */
	public static void onChanged(WorldCreationUiState ui) {
		if (applying) {
			return;
		}
		if (ui != watching) {
			watching = ui;
			wasMycelium = false;
		}
		boolean now = MYCELIUM != null && ui.getGameMode() == MYCELIUM;
		if (now && !wasMycelium) {
			wasMycelium = true;
			apply(ui);
		} else if (!now) {
			wasMycelium = false;
		}
	}

	private static void apply(WorldCreationUiState ui) {
		applying = true;
		try {
			ui.setDifficulty(Difficulty.PEACEFUL);
			ui.setAllowCommands(true);
			ui.setGenerateStructures(true);
			ui.setBonusChest(false);
			String type = "not found";
			for (var list : java.util.List.of(ui.getNormalPresetList(), ui.getAltPresetList())) {
				for (WorldCreationUiState.WorldTypeEntry entry : list) {
					if (entry.preset().is(WorldPresets.FLAT)) {
						ui.setWorldType(entry);
						type = "Superflat";
					}
				}
			}
			GameRules rules = ui.getGameRules();
			rules.set(GameRules.IMMEDIATE_RESPAWN, true, null);
			rules.set(GameRules.ADVANCE_TIME, false, null);
			ui.setGameRules(rules);
			MycLink.LOG.info("MyceliumCraft: world mode: Mycelium chosen: Survival, Peaceful, cheats on, world type {}, immediate_respawn on, advance_time off; the time ({}) and clear weather once it starts",
				type, TIME_OF_DAY);
		} finally {
			applying = false;
		}
	}

	/** Create New World's Create button. */
	public static void creating(WorldCreationUiState ui) {
		pending = MYCELIUM != null && ui.getGameMode() == MYCELIUM;
		if (pending) {
			MycLink.LOG.info("MyceliumCraft: world mode: creating '{}' as a Mycelium world", ui.getName());
		}
	}

	/** The integrated server has started (ServerLifecycleEvents.SERVER_STARTED, server thread). */
	public static void started(MinecraftServer server) {
		if (!pending) {
			return;
		}
		pending = false;
		var source = server.createCommandSourceStack().withSuppressedOutput();
		for (String command : new String[] { "time set " + TIME_OF_DAY, "weather clear" }) {
			try {
				int result = server.getCommands().getDispatcher().execute(command, source);
				MycLink.LOG.info("MyceliumCraft: world mode: new Mycelium world: '{}' (result {})", command, result);
			} catch (Exception e) {
				MycLink.LOG.warn("MyceliumCraft: world mode: new Mycelium world: '{}' failed: {}", command, e.getMessage());
			}
		}
	}
}
