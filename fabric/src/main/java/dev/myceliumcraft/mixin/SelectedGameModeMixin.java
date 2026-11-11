package dev.myceliumcraft.mixin;

import dev.myceliumcraft.MycWorldMode;
import java.util.Arrays;
import net.minecraft.client.gui.screens.worldselection.WorldCreationUiState;
import net.minecraft.world.level.GameType;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Mutable;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * A fifth entry in the world creation menu's game mode list: "Mycelium" (your request, 2026-11-10;
 * MycWorldMode). It plays as Survival (its GameType; your choice, 2026-11-10), with the name and description from our lang
 * file (selectWorld.gameMode.mycelium / .info, as Minecraft's own entries do). Nothing in Minecraft
 * switches over this list; it only compares with HARDCORE, CREATIVE and SURVIVAL by identity.
 */
@Mixin(WorldCreationUiState.SelectedGameMode.class)
public abstract class SelectedGameModeMixin {
	@Shadow
	@Final
	@Mutable
	private static WorldCreationUiState.SelectedGameMode[] $VALUES;

	@Inject(method = "<clinit>", at = @At("TAIL"))
	private static void myceliumcraft$addMycelium(CallbackInfo ci) {
		WorldCreationUiState.SelectedGameMode mycelium = SelectedGameModeAccessor.myceliumcraft$create("MYCELIUM", $VALUES.length, "mycelium", GameType.SURVIVAL);
		$VALUES = Arrays.copyOf($VALUES, $VALUES.length + 1);
		$VALUES[$VALUES.length - 1] = mycelium;
		MycWorldMode.MYCELIUM = mycelium;
	}
}
