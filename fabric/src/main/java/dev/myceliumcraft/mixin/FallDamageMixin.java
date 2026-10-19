package dev.myceliumcraft.mixin;

import dev.myceliumcraft.world.MycRules;
import net.minecraft.world.damagesource.DamageSource;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.player.Player;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** No fall damage for players (MycRules). Mobs keep theirs. */
@Mixin(LivingEntity.class)
public abstract class FallDamageMixin {
	@Inject(method = "causeFallDamage", at = @At("HEAD"), cancellable = true)
	private void myceliumcraft$noFallDamage(double fallDistance, float damageModifier, DamageSource source, CallbackInfoReturnable<Boolean> cir) {
		if (MycRules.active() && (Object) this instanceof Player) {
			MycRules.fallPrevented(fallDistance);
			cir.setReturnValue(false);
		}
	}
}
