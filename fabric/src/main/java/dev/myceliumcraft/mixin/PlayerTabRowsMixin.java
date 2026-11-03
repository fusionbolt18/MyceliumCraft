package dev.myceliumcraft.mixin;

import dev.myceliumcraft.MycColony;
import java.util.ArrayList;
import java.util.List;
import net.minecraft.client.gui.components.PlayerTabOverlay;
import net.minecraft.client.multiplayer.PlayerInfo;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * The player list's rows come from PlayerTabOverlay.getPlayerInfos (the players the server lists).
 * The other spores of your colony are added after them, as rows like any player's (MycColony).
 */
@Mixin(PlayerTabOverlay.class)
public abstract class PlayerTabRowsMixin {
	@Inject(method = "getPlayerInfos", at = @At("RETURN"), cancellable = true)
	private void myceliumcraft$colonyRows(CallbackInfoReturnable<List<PlayerInfo>> cir) {
		List<PlayerInfo> spores = MycColony.otherSporeRows();
		if (spores.isEmpty()) {
			return;
		}
		List<PlayerInfo> rows = new ArrayList<>(cir.getReturnValue());
		rows.addAll(spores);
		cir.setReturnValue(rows);
	}
}
