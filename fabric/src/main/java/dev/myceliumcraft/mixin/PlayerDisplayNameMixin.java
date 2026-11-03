package dev.myceliumcraft.mixin;

import dev.myceliumcraft.MycName;
import net.minecraft.network.chat.Component;
import net.minecraft.world.entity.player.Player;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

/**
 * Player.getDisplayName builds your name from getName() (your Minecraft name) with your team's
 * colours. While Mycelium has a spore, the name part is the spore's instead (MycName): chat, death
 * messages and the rest that ask for a display name show it. Minecraft's own name (getName, the
 * profile) is unchanged, so commands and saves still know you by it.
 */
@Mixin(Player.class)
public abstract class PlayerDisplayNameMixin {
	@Redirect(method = "getDisplayName", at = @At(value = "INVOKE", target = "Lnet/minecraft/world/entity/player/Player;getName()Lnet/minecraft/network/chat/Component;"))
	private Component myceliumcraft$sporeName(Player player) {
		Component spore = MycName.forPlayer(player.getGameProfile().id());
		return spore != null ? spore : player.getName();
	}
}
