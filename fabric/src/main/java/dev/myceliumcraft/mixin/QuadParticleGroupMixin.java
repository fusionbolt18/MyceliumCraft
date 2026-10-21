package dev.myceliumcraft.mixin;

import com.llamalad7.mixinextras.injector.v2.WrapWithCondition;
import dev.myceliumcraft.MycFirstPersonArms;
import dev.myceliumcraft.MycOverlay;
import net.minecraft.client.Camera;
import net.minecraft.client.particle.QuadParticleGroup;
import net.minecraft.client.particle.SingleQuadParticle;
import net.minecraft.client.renderer.state.level.QuadParticleRenderState;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/**
 * While the overlay is on, only Better Combat's particles (its weapon swing trails) are kept for
 * drawing; Mycelium draws the world, so Minecraft's own particles there (dust, crits) stay hidden.
 * MycFirstPersonArms draws what's kept.
 */
@Mixin(QuadParticleGroup.class)
public abstract class QuadParticleGroupMixin {
	@WrapWithCondition(
		method = "extractRenderState",
		at = @At(value = "INVOKE", target = "Lnet/minecraft/client/particle/SingleQuadParticle;extract(Lnet/minecraft/client/renderer/state/level/QuadParticleRenderState;Lnet/minecraft/client/Camera;F)V")
	)
	private boolean myceliumcraft$onlyOverlayParticles(SingleQuadParticle particle, QuadParticleRenderState state, Camera camera, float partialTick) {
		return !MycOverlay.active() || MycFirstPersonArms.keepParticle(particle);
	}
}
