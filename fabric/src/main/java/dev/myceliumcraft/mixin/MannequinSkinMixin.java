package dev.myceliumcraft.mixin;

import dev.myceliumcraft.MycSkins;
import dev.myceliumcraft.link.MycLink;
import net.minecraft.client.entity.ClientMannequin;
import net.minecraft.world.entity.decoration.Mannequin;
import net.minecraft.world.entity.player.PlayerSkin;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * A mannequin of you wears your spore's skin too (your report, 2026-11-08: Emotecraft's emote wheel
 * showed your normal skin). Emotecraft draws you there as one of Minecraft's own mannequins
 * (ClientMannequin) made from your profile, which looks its skin up from the profile, not through
 * PlayerInfo (PlayerInfoSkinMixin). So the mannequin's getSkin gives the spore's skin when the
 * mannequin is of you and Mycelium has a spore (MycSkins). Emotecraft itself isn't changed
 * (AGENTS.md rule 7); its mannequin doesn't override getSkin.
 */
@Mixin(ClientMannequin.class)
public abstract class MannequinSkinMixin {
	@Unique
	private static boolean myceliumcraft$logged;

	@Inject(method = "getSkin", at = @At("HEAD"), cancellable = true)
	private void myceliumcraft$sporeSkin(CallbackInfoReturnable<PlayerSkin> result) {
		var profile = ((Mannequin) (Object) this).getProfile();
		PlayerSkin skin = profile != null ? MycSkins.forPlayer(profile.partialProfile().id()) : null;
		if (skin != null) {
			if (!myceliumcraft$logged) {
				myceliumcraft$logged = true;
				MycLink.LOG.info("MyceliumCraft: skins: a mannequin of you ({}) wears your spore's skin", this.getClass().getName());
			}
			result.setReturnValue(skin);
		}
	}
}
