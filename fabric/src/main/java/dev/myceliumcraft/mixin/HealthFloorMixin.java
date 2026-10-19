package dev.myceliumcraft.mixin;

import dev.myceliumcraft.world.MycHealth;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.LivingEntity;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.ModifyVariable;

/**
 * While Mycelium's health is above 0, a player's Minecraft health never goes below a sliver
 * (MycHealth.FLOOR): Minecraft damage (lava, drowning, mobs) can't kill you on its own, and
 * Minecraft's death only comes from Mycelium's. Server side only (the client just shows it).
 */
@Mixin(LivingEntity.class)
public abstract class HealthFloorMixin {
	@ModifyVariable(method = "setHealth", at = @At("HEAD"), argsOnly = true)
	private float myceliumcraft$healthFloor(float health) {
		if ((Object) this instanceof ServerPlayer && MycHealth.protecting() && health < MycHealth.FLOOR) {
			return MycHealth.FLOOR;
		}
		return health;
	}
}
