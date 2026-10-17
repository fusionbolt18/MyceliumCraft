package dev.myceliumcraft.mixin;

import com.mojang.blaze3d.platform.InputConstants;
import com.mojang.blaze3d.platform.Window;
import dev.myceliumcraft.MycInputBridge;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * While bridged (as SkyCraft's InputConstantsMixin): key state comes from Mycelium, and Minecraft
 * never captures or moves the real cursor, which belongs to Mycelium's window.
 */
@Mixin(InputConstants.class)
public abstract class InputConstantsMixin {
	@Inject(method = "isKeyDown", at = @At("HEAD"), cancellable = true)
	private static void myceliumcraft$isKeyDown(int key, CallbackInfoReturnable<Boolean> cir) {
		if (MycInputBridge.active()) {
			cir.setReturnValue(MycInputBridge.isKeyDown(key));
		}
	}

	@Inject(method = "grabMouse", at = @At("HEAD"), cancellable = true)
	private static void myceliumcraft$grabMouse(Window window, double xpos, double ypos, CallbackInfo ci) {
		if (MycInputBridge.active()) {
			ci.cancel();
		}
	}

	@Inject(method = "releaseMouse", at = @At("HEAD"), cancellable = true)
	private static void myceliumcraft$releaseMouse(Window window, double xpos, double ypos, CallbackInfo ci) {
		if (MycInputBridge.active()) {
			ci.cancel();
		}
	}
}
