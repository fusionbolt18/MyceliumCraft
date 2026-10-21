package dev.myceliumcraft.mixin;

import com.mojang.blaze3d.platform.FramerateLimitTracker;
import dev.myceliumcraft.MycInputBridge;
import dev.myceliumcraft.MycOverlay;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * While bridged, Minecraft's window isn't the focused one, and Minecraft throttles itself to 30 fps
 * when it thinks it's inactive (seen in your log), and to 10 when minimized. Use your own frame-rate
 * setting instead, like SkyCraft's FramerateLimitTrackerMixin (which uses a fixed 260).
 *
 * Also whenever Mycelium is in a level showing Minecraft's overlay, not only while the input bridge
 * is on: your Minecraft body is captured once per Minecraft frame, and during the death fall and the
 * respawn emerge Mycelium has the controls (bridge off), so a minimized Minecraft sent it 10 times a
 * second and those animations were choppy (your report, 2026-10-21).
 */
@Mixin(FramerateLimitTracker.class)
public abstract class FramerateLimitTrackerMixin {
	@Shadow
	private int framerateLimit;

	@Inject(method = "getFramerateLimit", at = @At("HEAD"), cancellable = true)
	private void myceliumcraft$noInactiveThrottle(CallbackInfoReturnable<Integer> cir) {
		if (MycInputBridge.active() || MycOverlay.active()) {
			cir.setReturnValue(this.framerateLimit);
		}
	}
}
