package dev.myceliumcraft.mixin;

import dev.myceliumcraft.MycInputBridge;
import dev.myceliumcraft.MyceliumCraftClient;
import dev.myceliumcraft.MycOverlay;
import net.minecraft.client.Minecraft;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Frame hooks (same points SkyCraft uses on 26.3) and the input bridge's "window active". */
@Mixin(Minecraft.class)
public abstract class MinecraftMixin {
	/** Start of every frame: replay Mycelium's input before anything else runs. */
	@Inject(method = "runTick", at = @At("HEAD"))
	private void myceliumcraft$beginFrame(boolean advanceGameTime, CallbackInfo ci) {
		MyceliumCraftClient.frameStart();
		MycInputBridge.beginFrame((Minecraft) (Object) this);
	}

	/** Right after the frame is drawn (before it goes to the window): copy the overlay picture. */
	@Inject(
		method = "renderFrame",
		at = @At(value = "INVOKE", target = "Lnet/minecraft/client/renderer/GameRenderer;render()V", shift = At.Shift.AFTER)
	)
	private void myceliumcraft$afterRender(boolean advanceGameTime, CallbackInfo ci) {
		MycOverlay.capture((Minecraft) (Object) this);
	}

	/**
	 * No attack (no swing, no hit, no block mining) until the attack indicator is full, while
	 * Mycelium is playing (your rule, 2026-10-20).
	 */
	@Inject(method = "startAttack", at = @At("HEAD"), cancellable = true)
	private void myceliumcraft$fullChargeOnly(CallbackInfoReturnable<Boolean> cir) {
		if (MyceliumCraftClient.cycloning() || MyceliumCraftClient.attackNotCharged((Minecraft) (Object) this)) { // no attacks during Living Cyclone either
			cir.setReturnValue(false);
		} else if (!dev.myceliumcraft.MycBetterCombat.animatesMainHand((Minecraft) (Object) this)) {
			MyceliumCraftClient.swingStarted(); // Mycelium's attack events (weapon attributes); Better Combat's own attacks: MycBetterCombat
		}
	}

	/** Start of every client tick, before the crosshair pick: enemy stand-ins exactly where Mycelium's enemies are. */
	@Inject(method = "tick", at = @At("HEAD"))
	private void myceliumcraft$beforeTick(CallbackInfo ci) {
		dev.myceliumcraft.combat.EnemyProxies.clientSnap(((Minecraft) (Object) this).level);
		MyceliumCraftClient.beforeTick((Minecraft) (Object) this);
	}

	@Inject(method = "renderFrame", at = @At("TAIL"))
	private void myceliumcraft$afterFrame(boolean advanceGameTime, CallbackInfo ci) {
		MyceliumCraftClient.afterFrame((Minecraft) (Object) this);
	}

	/** Mycelium's window has the real focus while bridged; act as if ours did (mouse look, grabbing). */
	@Inject(method = "isWindowActive", at = @At("HEAD"), cancellable = true)
	private void myceliumcraft$windowActive(CallbackInfoReturnable<Boolean> cir) {
		if (MycInputBridge.active()) {
			cir.setReturnValue(true);
		}
	}
}
