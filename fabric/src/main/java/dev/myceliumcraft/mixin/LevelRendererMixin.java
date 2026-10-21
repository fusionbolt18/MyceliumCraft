package dev.myceliumcraft.mixin;

import com.mojang.blaze3d.resource.GraphicsResourceAllocator;
import com.mojang.renderpearl.api.buffers.GpuBufferSlice;
import dev.myceliumcraft.MycFirstPersonArms;
import dev.myceliumcraft.MycOverlay;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.LevelRenderer;
import net.minecraft.client.renderer.state.level.CameraRenderState;
import net.minecraft.client.renderer.state.level.LevelRenderState;
import org.joml.Vector4f;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * Mycelium draws the world. While the overlay is on, Minecraft draws nothing of its own level (no
 * sky, terrain, fog), so its picture is just the hand, HUD and screens on a transparent background
 * (as SkyCraft does). The one exception: your own arms when Better Combat's animation library
 * draws them as part of the world in first person (MycFirstPersonArms).
 */
@Mixin(LevelRenderer.class)
public abstract class LevelRendererMixin {
	@Shadow
	@Final
	private LevelRenderState levelRenderState;

	@Inject(
		method = "render(Lcom/mojang/blaze3d/resource/GraphicsResourceAllocator;ZLnet/minecraft/client/renderer/state/level/CameraRenderState;Lcom/mojang/renderpearl/api/buffers/GpuBufferSlice;Lorg/joml/Vector4f;ZZ)V",
		at = @At("HEAD"),
		cancellable = true
	)
	private void myceliumcraft$skipLevel(GraphicsResourceAllocator allocator, boolean renderOutline, CameraRenderState camera, GpuBufferSlice fog,
			Vector4f fogColor, boolean a, boolean b, CallbackInfo ci) {
		if (MycOverlay.active()) {
			MycFirstPersonArms.draw(Minecraft.getInstance(), this.levelRenderState, camera, fog);
			ci.cancel();
		}
	}
}
