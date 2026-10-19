package dev.myceliumcraft.mixin;

import dev.myceliumcraft.world.MycRules;
import net.minecraft.server.level.ServerPlayer;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** No Peaceful-difficulty healing (MycRules). */
@Mixin(ServerPlayer.class)
public abstract class ServerPlayerRegenMixin {
	@Inject(method = "tickRegeneration", at = @At("HEAD"), cancellable = true)
	private void myceliumcraft$noPeacefulRegen(CallbackInfo ci) {
		if (MycRules.active()) {
			ci.cancel();
		}
	}
}
