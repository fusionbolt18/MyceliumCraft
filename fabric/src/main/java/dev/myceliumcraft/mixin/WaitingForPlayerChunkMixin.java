package dev.myceliumcraft.mixin;

import dev.myceliumcraft.MycOverlay;
import dev.myceliumcraft.link.MycLink;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Minecraft's "Loading terrain" screen (after joining or respawning) waits until the player's
 * chunk section has been prepared for drawing. While the overlay is on Minecraft draws no world
 * (LevelRendererMixin), so that never happens and the screen stayed forever after a respawn (your
 * report, 2026-10-19). With Mycelium running, don't wait for it (as SkyCraft does).
 */
@Mixin(targets = "net.minecraft.client.multiplayer.LevelLoadTracker$WaitingForPlayerChunk")
public abstract class WaitingForPlayerChunkMixin {
	private static boolean logged;

	@Inject(method = "isReady", at = @At("HEAD"), cancellable = true)
	private void myceliumcraft$ready(CallbackInfoReturnable<Boolean> cir) {
		if (MycOverlay.active() || MycLink.myceliumAlive()) {
			if (!logged) {
				logged = true;
				MycLink.LOG.info("MyceliumCraft: 'Loading terrain' doesn't wait for Minecraft's world drawing (Mycelium draws the world)");
			}
			cir.setReturnValue(true);
		}
	}
}
