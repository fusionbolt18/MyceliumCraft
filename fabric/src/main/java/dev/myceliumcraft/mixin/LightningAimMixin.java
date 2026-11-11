package dev.myceliumcraft.mixin;

import dev.myceliumcraft.world.MycLightning;
import net.minecraft.core.BlockPos;
import net.minecraft.server.level.ServerLevel;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** A thunderstorm's lightning doesn't strike near a player (MycLightning). */
@Mixin(ServerLevel.class)
public abstract class LightningAimMixin {
	@Inject(method = "findLightningTargetAround", at = @At("RETURN"), cancellable = true)
	private void myceliumcraft$notAtPlayers(BlockPos around, CallbackInfoReturnable<BlockPos> cir) {
		BlockPos pos = cir.getReturnValue();
		BlockPos away = MycLightning.awayFromPlayers((ServerLevel) (Object) this, pos);
		if (away != pos) {
			cir.setReturnValue(away);
		}
	}
}
