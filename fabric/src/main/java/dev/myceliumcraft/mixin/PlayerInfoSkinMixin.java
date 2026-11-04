package dev.myceliumcraft.mixin;

import dev.myceliumcraft.MycSkins;
import net.minecraft.client.multiplayer.PlayerInfo;
import net.minecraft.world.entity.player.PlayerSkin;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Your player wears the skin of the spore you control (your request, 2026-11-03; MycSkins). Every
 * place Minecraft draws your player asks PlayerInfo for the skin: the body, the first person arms,
 * the head in the player list. Only your own player, and only while Mycelium has a spore.
 */
@Mixin(PlayerInfo.class)
public abstract class PlayerInfoSkinMixin {
	@Inject(method = "getSkin", at = @At("HEAD"), cancellable = true)
	private void myceliumcraft$sporeSkin(CallbackInfoReturnable<PlayerSkin> result) {
		PlayerSkin skin = MycSkins.forPlayer(((PlayerInfo) (Object) this).getProfile().id());
		if (skin != null) {
			result.setReturnValue(skin);
		}
	}
}
