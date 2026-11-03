package dev.myceliumcraft.mixin;

import dev.myceliumcraft.link.MycLink;
import net.minecraft.client.gui.components.toasts.Toast;
import net.minecraft.client.gui.components.toasts.ToastManager;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * No toasts at all (your request, 2026-11-02): Minecraft's pop-ups in the top right corner
 * (advancements, new recipes, tutorial hints, system messages, and the "now playing" music one)
 * never show. ToastManager.addToast and showNowPlayingToast are cancelled. Each kind of toast is
 * logged the first time one is held back.
 */
@Mixin(ToastManager.class)
public abstract class ToastManagerMixin {
	@Unique
	private static final java.util.Set<String> myceliumcraft$logged = new java.util.HashSet<>();

	@Inject(method = "addToast", at = @At("HEAD"), cancellable = true)
	private void myceliumcraft$noToast(Toast toast, CallbackInfo ci) {
		String kind = toast == null ? "null" : toast.getClass().getSimpleName();
		if (myceliumcraft$logged.add(kind)) {
			MycLink.LOG.info("MyceliumCraft: toasts: a {} held back (no toasts; logged once per kind)", kind);
		}
		ci.cancel();
	}

	@Inject(method = "showNowPlayingToast", at = @At("HEAD"), cancellable = true)
	private void myceliumcraft$noNowPlaying(CallbackInfo ci) {
		if (myceliumcraft$logged.add("NowPlayingToast")) {
			MycLink.LOG.info("MyceliumCraft: toasts: the 'now playing' music toast held back (logged once)");
		}
		ci.cancel();
	}
}
