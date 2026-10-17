package dev.myceliumcraft.mixin;

import com.mojang.blaze3d.platform.Window;
import dev.myceliumcraft.MycInputBridge;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** While bridged, Mycelium's window has the real focus; pretend ours does (as SkyCraft's WindowMixin). */
@Mixin(Window.class)
public abstract class WindowMixin {
	@Inject(method = "isFocused", at = @At("HEAD"), cancellable = true)
	private void myceliumcraft$focused(CallbackInfoReturnable<Boolean> cir) {
		if (MycInputBridge.active()) {
			cir.setReturnValue(true);
		}
	}
}
