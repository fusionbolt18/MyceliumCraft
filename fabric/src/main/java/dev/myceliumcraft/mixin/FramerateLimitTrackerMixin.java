package dev.myceliumcraft.mixin;

import com.mojang.blaze3d.platform.FramerateLimitTracker;
import dev.myceliumcraft.MycInputBridge;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * While bridged, Minecraft's window isn't the focused one, and Minecraft throttles itself to 30 fps
 * when it thinks it's inactive (seen in your log). Use your own frame-rate setting instead, like
 * SkyCraft's FramerateLimitTrackerMixin (which uses a fixed 260).
 */
@Mixin(FramerateLimitTracker.class)
public abstract class FramerateLimitTrackerMixin {
	@Shadow
	private int framerateLimit;

	@Inject(method = "getFramerateLimit", at = @At("HEAD"), cancellable = true)
	private void myceliumcraft$noInactiveThrottle(CallbackInfoReturnable<Integer> cir) {
		if (MycInputBridge.active()) {
			cir.setReturnValue(this.framerateLimit);
		}
	}
}
