package dev.myceliumcraft.mixin;

import dev.myceliumcraft.world.MycRules;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.food.FoodData;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

/**
 * No healing from a full hunger bar (MycRules). Hunger
 * itself (exhaustion, starving) is unchanged: only FoodData.tick's two heal calls are skipped.
 */
@Mixin(FoodData.class)
public abstract class FoodDataMixin {
	@Redirect(method = "tick", at = @At(value = "INVOKE", target = "Lnet/minecraft/server/level/ServerPlayer;heal(F)V"))
	private void myceliumcraft$noNaturalRegen(ServerPlayer player, float amount) {
		if (MycRules.active()) {
			MycRules.healPrevented("food", amount);
			return;
		}
		player.heal(amount);
	}
}
