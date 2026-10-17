package dev.myceliumcraft;

import dev.myceliumcraft.link.MycLink;
import dev.myceliumcraft.link.Proto;
import net.fabricmc.api.ClientModInitializer;
import net.minecraft.client.Minecraft;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.world.phys.Vec3;

public final class MyceliumCraftClient implements ClientModInitializer {
	private static long frame;
	private static int epoch;
	private static Object lastLevel;
	private static long lastLogMs;
	private static final MycLink.MycState MYC = new MycLink.MycState();

	@Override
	public void onInitializeClient() {
		MycLink.LOG.info("MyceliumCraft: loaded (protocol v{}, mapping {})", Proto.VERSION, Proto.MAPPING_NAME);
	}

	/** Called by MinecraftMixin once per rendered frame. */
	public static void afterFrame(Minecraft minecraft) {
		MycLink.poll();
		if (!MycLink.mapped()) {
			return;
		}
		frame++;
		LocalPlayer player = minecraft.player;
		if (player == null || minecraft.level == null) {
			lastLevel = null;
			MycLink.writeMcState(0, 0, 0, 0, 0, 0, frame, epoch);
			logOncePerSecond(null, 0, 0, 0);
			return;
		}
		if (minecraft.level != lastLevel) {
			// New world (or dimension): Mycelium re-anchors so the spore doesn't jump.
			lastLevel = minecraft.level;
			epoch++;
			MycLink.LOG.info("MyceliumCraft: entered a level, epoch {}", epoch);
		}
		float partial = minecraft.getDeltaTracker().getGameTimeDeltaPartialTick(false);
		Vec3 feet = player.getPosition(partial);
		int flags = Proto.MC_IN_WORLD;
		if (minecraft.gui.screen() != null) {
			flags |= Proto.MC_SCREEN_OPEN;
		}
		if (player.onGround()) {
			flags |= Proto.MC_ON_GROUND;
		}
		if (player.isShiftKeyDown()) {
			flags |= Proto.MC_SNEAKING;
		}
		if (player.isSprinting()) {
			flags |= Proto.MC_SPRINTING;
		}
		MycLink.writeMcState(flags, feet.x, feet.y, feet.z, player.getYRot(), player.getXRot(), frame, epoch);
		logOncePerSecond(feet, player.getYRot(), flags, epoch);
	}

	private static void logOncePerSecond(Vec3 feet, float yaw, int flags, int ep) {
		long now = System.currentTimeMillis();
		if (now - lastLogMs < 1000) {
			return;
		}
		lastLogMs = now;
		boolean read = MycLink.readMycState(MYC);
		MycLink.LOG.info("MyceliumCraft: frame={} mc={} yaw={} flags={} epoch={} | mycelium alive={} read={} inLevel={} spore=({}, {}, {}) yaw={}",
			frame,
			feet == null ? "none" : String.format("(%.2f, %.2f, %.2f)", feet.x, feet.y, feet.z),
			String.format("%.1f", yaw), flags, ep,
			MycLink.myceliumAlive(), read, MYC.inLevel(),
			String.format("%.2f", MYC.x), String.format("%.2f", MYC.y), String.format("%.2f", MYC.z), String.format("%.1f", MYC.yaw));
	}
}
