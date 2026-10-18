package dev.myceliumcraft.mixin;

import dev.myceliumcraft.MycOverlay;
import net.minecraft.client.renderer.LevelRenderer;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * Mycelium draws the world. While the overlay is on, Minecraft draws nothing of its own level (no
 * sky, terrain, fog), so its picture is just the hand, HUD and screens on a transparent background
 * (as SkyCraft does).
 */
@Mixin(LevelRenderer.class)
public abstract class LevelRendererMixin {
	@Inject(
		method = "render(Lcom/mojang/blaze3d/resource/GraphicsResourceAllocator;ZLnet/minecraft/client/renderer/state/level/CameraRenderState;Lcom/mojang/renderpearl/api/buffers/GpuBufferSlice;Lorg/joml/Vector4f;ZZ)V",
		at = @At("HEAD"),
		cancellable = true
	)
	private void myceliumcraft$skipLevel(CallbackInfo ci) {
		if (MycOverlay.active()) {
			ci.cancel();
		}
	}
}
