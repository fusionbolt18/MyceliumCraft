package dev.myceliumcraft.mixin;

import dev.myceliumcraft.MycWorldMode;
import net.minecraft.client.gui.screens.worldselection.CreateWorldScreen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * The "Mycelium" game mode (MycWorldMode): choosing it fills in your world's settings, and creating
 * the world with it chosen sets the time of day and weather once it has started.
 */
@Mixin(CreateWorldScreen.class)
public abstract class CreateWorldScreenMixin {
	@Inject(method = "<init>", at = @At("TAIL"))
	private void myceliumcraft$watchGameMode(CallbackInfo ci) {
		((CreateWorldScreen) (Object) this).getUiState().addListener(MycWorldMode::onChanged);
	}

	@Inject(method = "onCreate", at = @At("HEAD"))
	private void myceliumcraft$creating(CallbackInfo ci) {
		MycWorldMode.creating(((CreateWorldScreen) (Object) this).getUiState());
	}
}
