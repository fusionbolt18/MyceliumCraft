package dev.myceliumcraft.mixin;

import com.mojang.blaze3d.platform.Lighting;
import com.mojang.renderpearl.api.textures.GpuTextureView;
import dev.myceliumcraft.MycHandLight;
import dev.myceliumcraft.MycOverlay;
import net.minecraft.client.renderer.GameRenderer;
import net.minecraft.client.renderer.state.level.CameraRenderState;
import net.minecraft.client.renderer.state.level.PlayerRenderState;
import org.joml.Matrix4f;
import org.joml.Vector3f;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * Minecraft's normal first-person hand while the overlay is on. Minecraft sets the world's lighting
 * in LevelRenderer.render, which the overlay skips (LevelRendererMixin), so the hand was lit with
 * whatever was set last (your report, 2026-11-04). This sets the world's. Those two lights are fixed
 * in the world, though, and the hand turns with your view, so one side went dark looking straight up
 * (your report, same day). So for the hand they turn with your view too, and it's shaded the same
 * wherever you look; put back after, for anything else that's drawn with them. The light level:
 * MycHandLight.
 */
@Mixin(GameRenderer.class)
public abstract class HandLightMixin {
	@Unique
	private boolean myceliumcraft$lightsTurned;

	@Inject(
		method = "renderItemInHand(Lnet/minecraft/client/renderer/state/level/CameraRenderState;Lnet/minecraft/client/renderer/state/level/PlayerRenderState;Lcom/mojang/renderpearl/api/textures/GpuTextureView;)V",
		at = @At("HEAD")
	)
	private void myceliumcraft$lightHand(CameraRenderState camera, PlayerRenderState player, GpuTextureView target, CallbackInfo ci) {
		if (MycOverlay.active() && player.avatarRenderState != null) {
			Lighting lighting = ((GameRenderer) (Object) this).lighting();
			Matrix4f turn = new Matrix4f(camera.viewRotationMatrix).invert();
			((LightingAccessor) lighting).myceliumcraft$updateBuffer(Lighting.Entry.LEVEL,
				turn.transformDirection(LightingAccessor.myceliumcraft$light0(), new Vector3f()),
				turn.transformDirection(LightingAccessor.myceliumcraft$light1(), new Vector3f()));
			myceliumcraft$lightsTurned = true;
			lighting.setupFor(Lighting.Entry.LEVEL);
			MycHandLight.brighten(player.avatarRenderState);
		}
	}

	@Inject(
		method = "renderItemInHand(Lnet/minecraft/client/renderer/state/level/CameraRenderState;Lnet/minecraft/client/renderer/state/level/PlayerRenderState;Lcom/mojang/renderpearl/api/textures/GpuTextureView;)V",
		at = @At("RETURN")
	)
	private void myceliumcraft$lightsBack(CameraRenderState camera, PlayerRenderState player, GpuTextureView target, CallbackInfo ci) {
		if (myceliumcraft$lightsTurned) {
			myceliumcraft$lightsTurned = false;
			((LightingAccessor) ((GameRenderer) (Object) this).lighting()).myceliumcraft$updateBuffer(Lighting.Entry.LEVEL,
				LightingAccessor.myceliumcraft$light0(), LightingAccessor.myceliumcraft$light1());
		}
	}
}
