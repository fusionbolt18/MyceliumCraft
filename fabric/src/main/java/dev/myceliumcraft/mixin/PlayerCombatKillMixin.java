package dev.myceliumcraft.mixin;

import dev.myceliumcraft.MyceliumCraftClient;
import net.minecraft.client.multiplayer.ClientPacketListener;
import net.minecraft.network.protocol.game.ClientboundPlayerCombatKillPacket;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * "You died" on the client: Minecraft either opens its death screen or, with the "immediate
 * respawn" game rule on, respawns you at once. Your world has it on, so the player was back on its
 * feet at world spawn within a second of Mycelium's death and there was no death animation to
 * show (your report, 2026-10-19). When the death came from Mycelium's, neither happens: the body
 * stays dying (Minecraft's fall over, red) and MyceliumCraftClient respawns it when Mycelium is alive
 * again. Runs on the render thread (after Minecraft moves the packet there).
 */
@Mixin(ClientPacketListener.class)
public abstract class PlayerCombatKillMixin {
	@Inject(method = "handlePlayerCombatKill", at = @At(value = "INVOKE",
		target = "Lnet/minecraft/network/protocol/PacketUtils;ensureRunningOnSameThread(Lnet/minecraft/network/protocol/Packet;Lnet/minecraft/network/PacketListener;Lnet/minecraft/network/PacketProcessor;)V",
		shift = At.Shift.AFTER), cancellable = true)
	private void myceliumcraft$holdDeath(ClientboundPlayerCombatKillPacket packet, CallbackInfo ci) {
		if (MyceliumCraftClient.holdDeath(packet.message())) {
			ci.cancel();
		}
	}
}
