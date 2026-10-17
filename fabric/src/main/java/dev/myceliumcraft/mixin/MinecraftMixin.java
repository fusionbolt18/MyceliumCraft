package dev.myceliumcraft.mixin;

import dev.myceliumcraft.MycInputBridge;
import dev.myceliumcraft.MyceliumCraftClient;
import net.minecraft.client.Minecraft;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Frame hooks (same points SkyCraft uses on 26.3) and the input bridge's "window active". */
@Mixin(Minecraft.class)
public abstract class MinecraftMixin {
	/** Start of every frame: replay Mycelium's input before anything else runs. */
	@Inject(method = "runTick", at = @At("HEAD"))
	private void myceliumcraft$beginFrame(boolean advanceGameTime, CallbackInfo ci) {
		MyceliumCraftClient.frameStart();
		MycInputBridge.beginFrame((Minecraft) (Object) this);
	}

	@Inject(method = "renderFrame", at = @At("TAIL"))
	private void myceliumcraft$afterFrame(boolean advanceGameTime, CallbackInfo ci) {
		MyceliumCraftClient.afterFrame((Minecraft) (Object) this);
	}

	/** Mycelium's window has the real focus while bridged; act as if ours did (mouse look, grabbing). */
	@Inject(method = "isWindowActive", at = @At("HEAD"), cancellable = true)
	private void myceliumcraft$windowActive(CallbackInfoReturnable<Boolean> cir) {
		if (MycInputBridge.active()) {
			cir.setReturnValue(true);
		}
	}
}
