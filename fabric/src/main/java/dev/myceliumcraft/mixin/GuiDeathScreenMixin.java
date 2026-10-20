package dev.myceliumcraft.mixin;

import dev.myceliumcraft.MyceliumCraftClient;
import net.minecraft.client.gui.Gui;
import net.minecraft.client.player.LocalPlayer;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

/**
 * Minecraft's Gui.setScreen: closing a screen (setScreen(null)) while the player is dead opens the
 * death screen instead, or with "immediate respawn" on, respawns at once. That respawned you right
 * after Mycelium's death even with the death message held back (your second test, 2026-10-19:
 * every enemy stand-in was sent again, as after a respawn). While MyceliumCraftClient holds the
 * death, a closed screen just closes: the player counts as not dead there.
 */
@Mixin(Gui.class)
public abstract class GuiDeathScreenMixin {
	@Redirect(method = "setScreen", at = @At(value = "INVOKE", target = "Lnet/minecraft/client/player/LocalPlayer;isDeadOrDying()Z"))
	private boolean myceliumcraft$deadForScreens(LocalPlayer player) {
		boolean dead = player.isDeadOrDying();
		if (dead && MyceliumCraftClient.deathHeld()) {
			MyceliumCraftClient.screenClosedWhileDeathHeld();
			return false;
		}
		return dead;
	}
}
