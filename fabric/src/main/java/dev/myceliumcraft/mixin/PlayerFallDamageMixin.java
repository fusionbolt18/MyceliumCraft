package dev.myceliumcraft.mixin;

import dev.myceliumcraft.world.MycRules;
import net.minecraft.world.damagesource.DamageSource;
import net.minecraft.world.entity.player.Player;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Player has its own causeFallDamage (flying, stats): stop it there too, before anything happens. */
@Mixin(Player.class)
public abstract class PlayerFallDamageMixin {
	@Inject(method = "causeFallDamage", at = @At("HEAD"), cancellable = true)
	private void myceliumcraft$noFallDamage(double fallDistance, float damageModifier, DamageSource source, CallbackInfoReturnable<Boolean> cir) {
		if (MycRules.active()) {
			MycRules.fallPrevented(fallDistance);
			cir.setReturnValue(false);
		}
	}
}
