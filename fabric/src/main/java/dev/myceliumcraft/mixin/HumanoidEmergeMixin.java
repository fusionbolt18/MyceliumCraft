package dev.myceliumcraft.mixin;

import dev.myceliumcraft.DrumPose;
import dev.myceliumcraft.EmergePose;
import net.minecraft.client.model.HumanoidModel;
import net.minecraft.client.renderer.entity.state.HumanoidRenderState;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * While MycPlayerModel captures your body during Mycelium's sprout, every humanoid model Minecraft
 * poses for it (the body, armour, the cape) gets the dig-out (EmergePose) on top of its normal pose.
 * Here, in Minecraft's own posing, rather than in the capture, so held items (placed from the
 * arm's pose) follow the arms. setupAnim starts from the default pose each time, so posing twice
 * in a frame doesn't add up. The same for a spore hitting the beetle drum (DrumPose).
 */
@Mixin(HumanoidModel.class)
public abstract class HumanoidEmergeMixin {
	@Inject(method = "setupAnim(Lnet/minecraft/client/renderer/entity/state/HumanoidRenderState;)V", at = @At("TAIL"))
	private void myceliumcraft$emerge(HumanoidRenderState state, CallbackInfo ci) {
		float progress = EmergePose.capturing();
		if (!Float.isNaN(progress)) {
			EmergePose.apply((HumanoidModel<?>) (Object) this, progress);
		}
		float drum = DrumPose.capturing();
		if (!Float.isNaN(drum)) {
			DrumPose.apply((HumanoidModel<?>) (Object) this, drum);
		}
	}
}
