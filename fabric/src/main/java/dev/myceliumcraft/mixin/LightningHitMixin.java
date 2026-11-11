package dev.myceliumcraft.mixin;

import dev.myceliumcraft.world.MycLightning;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.LightningBolt;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Lightning never hurts a player (MycLightning). */
@Mixin(Entity.class)
public abstract class LightningHitMixin {
	@Inject(method = "thunderHit", at = @At("HEAD"), cancellable = true)
	private void myceliumcraft$notPlayers(ServerLevel level, LightningBolt bolt, CallbackInfo ci) {
		if ((Object) this instanceof ServerPlayer player) {
			MycLightning.hitPrevented(player);
			ci.cancel();
		}
	}
}
