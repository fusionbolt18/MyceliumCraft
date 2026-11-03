package dev.myceliumcraft.mixin;

import com.mojang.authlib.GameProfile;
import dev.myceliumcraft.MycName;
import net.minecraft.client.gui.components.PlayerTabOverlay;
import net.minecraft.client.multiplayer.PlayerInfo;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

/**
 * The player list (PlayerTabOverlay.getNameForDisplay) names a player by their profile's name, not
 * Player.getDisplayName. Yours shows the spore's name instead while Mycelium has one (MycName).
 */
@Mixin(PlayerTabOverlay.class)
public abstract class PlayerTabNameMixin {
	@Redirect(method = "getNameForDisplay", at = @At(value = "INVOKE", target = "Lcom/mojang/authlib/GameProfile;name()Ljava/lang/String;"))
	private String myceliumcraft$sporeName(GameProfile profile, PlayerInfo info) {
		String spore = MycName.textForPlayer(profile.id());
		return spore != null ? spore : profile.name();
	}
}
