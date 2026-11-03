package dev.myceliumcraft.mixin;

import dev.myceliumcraft.link.MycLink;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.Hud;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

/**
 * Minecraft's Hud.extractTabList shows the player list (List Players, Tab) only when you're not on
 * your own world's server, or when it lists more than one player, or a scoreboard is shown in the
 * list. So in single player, alone, holding the key showed nothing (your request, 2026-11-03: make
 * it work in single player). Here that check sees "not your own server", so the list always shows
 * while the key is held: just you.
 */
@Mixin(Hud.class)
public abstract class HudTabListMixin {
	@Unique
	private static boolean myceliumcraft$logged;

	@Redirect(method = "extractTabList", at = @At(value = "INVOKE", target = "Lnet/minecraft/client/Minecraft;isLocalServer()Z"))
	private boolean myceliumcraft$listInSinglePlayer(Minecraft minecraft) {
		if (!myceliumcraft$logged && minecraft.isLocalServer()) {
			myceliumcraft$logged = true;
			MycLink.LOG.info("MyceliumCraft: player list: shown in single player too (List Players held; logged once)");
		}
		return false;
	}
}
