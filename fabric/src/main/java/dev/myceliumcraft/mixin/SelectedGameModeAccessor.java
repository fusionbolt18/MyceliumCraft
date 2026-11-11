package dev.myceliumcraft.mixin;

import net.minecraft.client.gui.screens.worldselection.WorldCreationUiState;
import net.minecraft.world.level.GameType;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Invoker;

/** The game mode list's private constructor, to make the "Mycelium" entry (SelectedGameModeMixin, MycWorldMode). */
@Mixin(WorldCreationUiState.SelectedGameMode.class)
public interface SelectedGameModeAccessor {
	@Invoker("<init>")
	static WorldCreationUiState.SelectedGameMode myceliumcraft$create(String name, int ordinal, String id, GameType gameType) {
		throw new AssertionError("mixin");
	}
}
