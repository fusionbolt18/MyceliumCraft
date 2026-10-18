package dev.myceliumcraft;

import dev.myceliumcraft.link.GroundGrid;
import dev.myceliumcraft.link.MycLink;
import dev.myceliumcraft.link.Proto;
import dev.myceliumcraft.world.MycCollision;
import net.fabricmc.api.ClientModInitializer;
import net.minecraft.client.Minecraft;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.util.Mth;
import net.minecraft.world.entity.ai.attributes.AttributeInstance;
import net.minecraft.world.entity.ai.attributes.Attributes;
import net.minecraft.world.phys.Vec3;

public final class MyceliumCraftClient implements ClientModInitializer {
	private static long frame;
	private static int epoch;
	private static Object lastLevel;
	private static long lastLogMs;
	private static int lastTeleportSeq;
	private static int teleportAck;
	private static double appliedScale = Double.NaN; // last scale we asked the server for, in this level
	private static final MycLink.MycState MYC = new MycLink.MycState();
	private static final MycLink.Tick TICK = new MycLink.Tick();
	private static long qpcFreq;
	private static volatile long frameStartQpc;

	/**
	 * Start of every frame (Minecraft.runTick HEAD), the moment Minecraft works out its tick timing:
	 * remember the clock, so the tick's time can be given exactly (reading it at the end of the
	 * frame would add a render-time-dependent error).
	 */
	public static void frameStart() {
		frameStartQpc = MycLink.qpc();
	}

	@Override
	public void onInitializeClient() {
		MycLink.LOG.info("MyceliumCraft: loaded (protocol v{}, mapping {})", Proto.VERSION, Proto.MAPPING_NAME);
		MyceliumKeys.init();
	}

	/** Called by MinecraftMixin once per rendered frame. */
	public static void afterFrame(Minecraft minecraft) {
		MycLink.poll();
		if (!MycLink.mapped()) {
			return;
		}
		frame++;
		MyceliumKeys.publish();

		GroundGrid before = MycCollision.grid();
		GroundGrid grid = MycLink.readGround(before);
		if (grid != null && grid != before) {
			MycCollision.setGrid(grid);
			MycLink.LOG.info("MyceliumCraft: ground grid v{}: {}x{} cells of {} blocks, x {}..{} z {}..{}",
				grid.version, grid.n, grid.n, grid.cell, grid.originX, grid.originX + grid.n * grid.cell, grid.originZ, grid.originZ + grid.n * grid.cell);
		}

		boolean haveMyc = MycLink.readMycState(MYC);
		LocalPlayer player = minecraft.player;
		if (player == null || minecraft.level == null) {
			lastLevel = null;
			MycLink.writeMcState(0, 0, 0, 0, 0, 0, frame, epoch, teleportAck, minecraft.options.getCameraType().ordinal(), 0, null);
			logOncePerSecond(null, 0, 0);
			return;
		}
		if (minecraft.level != lastLevel) {
			// New world (or dimension): Mycelium sends a fresh teleport.
			lastLevel = minecraft.level;
			appliedScale = Double.NaN;
			epoch++;
			MycLink.LOG.info("MyceliumCraft: entered a level, epoch {}", epoch);
		}

		// Teleport only once we have ground to stand on, so the player doesn't fall through it.
		if (haveMyc && MYC.teleportSeq != 0 && MYC.teleportSeq != lastTeleportSeq && grid != null) {
			lastTeleportSeq = MYC.teleportSeq;
			teleport(minecraft, player, MYC.tpX, MYC.tpY, MYC.tpZ, MYC.tpYaw);
			teleportAck = MYC.teleportSeq;
		}

		// Body size: as tall as the spore while Mycelium has one in a level; normal size otherwise.
		boolean sporeActive = haveMyc && MycLink.myceliumAlive() && MYC.inLevel() && MYC.playerHeight > 0;
		double wantedScale = sporeActive ? Mth.clamp(MYC.playerHeight / 1.8, 0.0625, 16.0) : 1.0;
		if (Double.isNaN(appliedScale) || Math.abs(wantedScale - appliedScale) > 0.001) {
			appliedScale = wantedScale;
			setScale(minecraft, player, wantedScale);
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
		// Where the body faces (it turns toward the walking direction when strafing), interpolated like the position.
		float bodyYaw = net.minecraft.util.Mth.rotLerp(partial, player.yBodyRotO, player.yBodyRot);
		// The raw physics tick and when it happened (as SkyCraft does): the latest tick was 'partial'
		// ticks before this frame started. Mycelium interpolates on its own clock from these.
		if (qpcFreq == 0) {
			qpcFreq = MycLink.qpcFrequency();
		}
		TICK.ms = minecraft.level.tickRateManager().millisecondsPerTick();
		TICK.qpc = frameStartQpc - (long) (partial * TICK.ms * qpcFreq / 1000.0);
		TICK.prevX = player.xo;
		TICK.prevY = player.yo;
		TICK.prevZ = player.zo;
		TICK.curX = player.getX();
		TICK.curY = player.getY();
		TICK.curZ = player.getZ();
		TICK.bodyO = player.yBodyRotO;
		TICK.body = player.yBodyRot;
		MycLink.writeMcState(flags, feet.x, feet.y, feet.z, player.getYRot(), player.getXRot(), frame, epoch, teleportAck, minecraft.options.getCameraType().ordinal(), bodyYaw, TICK);
		logOncePerSecond(feet, player.getYRot(), flags);
	}

	/** Move the player on the client and on the integrated server (as SkyCraft does), so the server doesn't pull it back. */
	private static void teleport(Minecraft minecraft, LocalPlayer player, double x, double y, double z, float yaw) {
		player.setPos(x, y, z);
		// The rendered position blends from xo/yo/zo to the current one; reset it too, or the next
		// frames still report (and draw) the old position.
		player.xo = x;
		player.yo = y;
		player.zo = z;
		player.setYRot(yaw);
		player.setDeltaMovement(Vec3.ZERO);
		player.resetFallDistance();
		var server = minecraft.getSingleplayerServer();
		if (server != null) {
			var uuid = player.getUUID();
			server.execute(() -> {
				ServerPlayer sp = server.getPlayerList().getPlayer(uuid);
				if (sp != null) {
					sp.teleportTo(x, y, z);
					sp.setYRot(yaw);
					sp.resetFallDistance();
				}
			});
		}
		MycLink.LOG.info("MyceliumCraft: teleport {} to ({}, {}, {}) yaw {}", MYC.teleportSeq,
			String.format("%.2f", x), String.format("%.2f", y), String.format("%.2f", z), String.format("%.0f", yaw));
	}

	/**
	 * Minecraft's own scale attribute: changes only the body (hitbox and eye height). Step-up, jump,
	 * speed and reach are separate attributes and stay the same. Set on the integrated server, which
	 * syncs it to the client.
	 */
	private static void setScale(Minecraft minecraft, LocalPlayer player, double scale) {
		var server = minecraft.getSingleplayerServer();
		if (server == null) {
			MycLink.LOG.warn("MyceliumCraft: not a singleplayer world, can't set the player scale");
			return;
		}
		var uuid = player.getUUID();
		server.execute(() -> {
			ServerPlayer sp = server.getPlayerList().getPlayer(uuid);
			AttributeInstance attribute = sp == null ? null : sp.getAttribute(Attributes.SCALE);
			if (attribute != null) {
				attribute.setBaseValue(scale);
				MycLink.LOG.info("MyceliumCraft: player scale set to {} (height {} blocks)", String.format("%.3f", scale), String.format("%.3f", 1.8 * scale));
			}
		});
	}

	private static void logOncePerSecond(Vec3 feet, float yaw, int flags) {
		long now = System.currentTimeMillis();
		if (now - lastLogMs < 1000) {
			return;
		}
		lastLogMs = now;
		GroundGrid grid = MycCollision.grid();
		double ground = feet == null || grid == null ? Double.NaN : grid.groundAt(feet.x, feet.y, feet.z);
		LocalPlayer player = Minecraft.getInstance().player;
		MycLink.LOG.info("MyceliumCraft: frame={} mc={} yaw={} flags={} epoch={} groundHere={} grid=v{} tp={}/ack {} scale={} height={} | mycelium alive={} inLevel={} spore=({}, {}, {})",
			frame,
			feet == null ? "none" : String.format("(%.2f, %.2f, %.2f)", feet.x, feet.y, feet.z),
			String.format("%.1f", yaw), flags, epoch,
			String.format("%.2f", ground), grid == null ? 0 : grid.version, MYC.teleportSeq, teleportAck,
			player == null ? "-" : String.format("%.3f", player.getScale()), player == null ? "-" : String.format("%.3f", player.getBbHeight()),
			MycLink.myceliumAlive(), MYC.inLevel(),
			String.format("%.2f", MYC.x), String.format("%.2f", MYC.y), String.format("%.2f", MYC.z));
	}
}
