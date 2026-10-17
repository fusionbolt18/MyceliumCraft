package dev.myceliumcraft.mixin;

import dev.myceliumcraft.MyceliumCraftClient;
import net.minecraft.client.Minecraft;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Hooks the end of every rendered frame (same hook point SkyCraft uses on 26.3). */
@Mixin(Minecraft.class)
public abstract class MinecraftMixin {
	@Inject(method = "renderFrame", at = @At("TAIL"))
	private void myceliumcraft$afterFrame(boolean advanceGameTime, CallbackInfo ci) {
		MyceliumCraftClient.afterFrame((Minecraft) (Object) this);
	}
}
