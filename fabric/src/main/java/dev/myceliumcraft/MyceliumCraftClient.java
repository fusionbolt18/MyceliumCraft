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
	private static boolean frozeWorld; // we froze the world's ticking (Mycelium paused)
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
		String built = net.fabricmc.loader.api.FabricLoader.getInstance().getModContainer("myceliumcraft")
			.map(m -> m.getMetadata().getCustomValue("myceliumcraft:built"))
			.map(v -> v.getAsString()).orElse("unknown");
		MycLink.LOG.info("MyceliumCraft: loaded (protocol v{}, mapping {}), jar built {}", Proto.VERSION, Proto.MAPPING_NAME, built);
		MyceliumKeys.init();
		// Combat: invisible stand-ins for Mycelium's enemies, drawn by nothing.
		dev.myceliumcraft.combat.EnemyProxies.init();
		net.fabricmc.fabric.api.client.rendering.v1.EntityRendererRegistry.register(dev.myceliumcraft.combat.EnemyProxies.TYPE, net.minecraft.client.renderer.entity.NoopRenderer::new);
		MycLink.LOG.info("MyceliumCraft: rules: no fall damage for players, no natural health regeneration (always, while the mod is installed)");
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
		MycOverlay.setActive(haveMyc && MycLink.myceliumAlive() && MYC.inLevel() && (MYC.flags & Proto.MYC_OVERLAY) != 0
			&& player != null && minecraft.level != null);
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

		// Mycelium paused (P, its menu): freeze Minecraft's world too, the way /tick freeze does.
		freezeWorld(minecraft, haveMyc && MycLink.myceliumAlive() && MYC.inLevel() && (MYC.flags & Proto.MYC_PAUSED) != 0);

		syncHealth(minecraft, player, haveMyc && MycLink.myceliumAlive() && MYC.inLevel() && MYC.maxHealth > 0f);
		syncLevel(minecraft, player, haveMyc && MycLink.myceliumAlive() && MYC.inLevel() && MYC.nutrients >= 0);

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
		if (minecraft.isPaused()) {
			flags |= Proto.MC_PAUSED; // Escape's pause menu: Mycelium freezes too
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
		// Minecraft's own camera feel: its FOV multiplier (sprinting, speed, bow, spyglass; already
		// scaled by your "FOV Effects" setting) and its smoothed eye height (crouching lowers it).
		var camera = (dev.myceliumcraft.mixin.CameraAccessor) minecraft.gameRenderer.mainCamera();
		float fovScale = Mth.lerp(partial, camera.myceliumcraft$oldFovModifier(), camera.myceliumcraft$fovModifier());
		float standingEye = player.getEyeHeight(net.minecraft.world.entity.Pose.STANDING);
		float eye = Mth.lerp(partial, camera.myceliumcraft$eyeHeightOld(), camera.myceliumcraft$eyeHeight());
		// (Dead: no eye-height change, so the dying pose can't move Mycelium's camera.)
		float eyeScale = player.isDeadOrDying() ? 1f : standingEye > 0.01f ? eye / standingEye : 1f;
		MycLink.writeMcState(flags, feet.x, feet.y, feet.z, player.getYRot(), player.getXRot(), frame, epoch, teleportAck, minecraft.options.getCameraType().ordinal(), bodyYaw, TICK, fovScale, eyeScale,
			minecraft.options.damageTiltStrength().get().floatValue()); // Mycelium's camera tilts on hits by this much
		dev.myceliumcraft.combat.EnemyProxies.clientSnap(minecraft.level);
		sendHitBoxes(minecraft);
		// Third person: your Minecraft body, for Mycelium to draw instead of the spore.
		MycPlayerModel.frame(minecraft, MycOverlay.active(), partial);
		logOncePerSecond(feet, player.getYRot(), flags);
	}

	private static final int[] BOX_IDS = new int[Proto.HB_MAX];
	private static final float[] BOXES = new float[Proto.HB_MAX * 6];
	private static int lastBoxCount = -1;

	/**
	 * Each enemy stand-in's box as this client has it: what the crosshair actually picks (its
	 * bounding box, grown by its pick radius). Mycelium draws these with -mcShowHitboxes.
	 */
	private static void sendHitBoxes(Minecraft minecraft) {
		int n = 0;
		for (var entity : minecraft.level.entitiesForRendering()) {
			if (n >= Proto.HB_MAX) {
				break;
			}
			if (!(entity instanceof dev.myceliumcraft.combat.EnemyProxy proxy) || proxy.isRemoved()) {
				continue;
			}
			var box = proxy.getBoundingBox().inflate(proxy.getPickRadius());
			BOX_IDS[n] = proxy.enemyId();
			int o = n * 6;
			BOXES[o] = (float) box.minX;
			BOXES[o + 1] = (float) box.minY;
			BOXES[o + 2] = (float) box.minZ;
			BOXES[o + 3] = (float) box.maxX;
			BOXES[o + 4] = (float) box.maxY;
			BOXES[o + 5] = (float) box.maxZ;
			n++;
		}
		MycLink.writeHitBoxes(n, BOX_IDS, BOXES);
		if (n != lastBoxCount) {
			lastBoxCount = n;
			MycLink.LOG.info("MyceliumCraft: combat: {} enemy stand-ins on the client{}", n,
				n > 0 ? String.format("; first #%d box %.2f x %.2f x %.2f blocks", BOX_IDS[0], BOXES[3] - BOXES[0], BOXES[4] - BOXES[1], BOXES[5] - BOXES[2]) : "");
		}
	}

	private static int sentLevel = -1;
	private static long lastLevelFix;

	/**
	 * Minecraft's experience level = Mycelium's nutrients (your choice, 2026-10-19), with an empty
	 * XP bar. Mycelium is in charge: Minecraft's own XP (orbs, enchanting) is put back. Applied on the
	 * integrated server when it changes, or when Minecraft's has drifted (at most every 0.25 s).
	 */
	private static void syncLevel(Minecraft minecraft, LocalPlayer player, boolean sync) {
		var server = minecraft.getSingleplayerServer();
		if (!sync || server == null || player.isDeadOrDying()) {
			sentLevel = -1;
			return;
		}
		int level = MYC.nutrients;
		long now = System.currentTimeMillis();
		boolean drifted = (player.experienceLevel != level || player.experienceProgress != 0f) && now - lastLevelFix > 250;
		if (level == sentLevel && !drifted) {
			return;
		}
		lastLevelFix = now;
		if (level != sentLevel) {
			MycLink.LOG.info("MyceliumCraft: level: Minecraft experience level {} (Mycelium nutrients)", level);
		} else {
			MycLink.LOG.info("MyceliumCraft: level: Minecraft's level had drifted to {} ({} of the bar); back to {}", player.experienceLevel,
				String.format("%.2f", player.experienceProgress), level);
		}
		sentLevel = level;
		var uuid = player.getUUID();
		server.execute(() -> {
			ServerPlayer sp = server.getPlayerList().getPlayer(uuid);
			if (sp != null) {
				sp.setExperienceLevels(level);
				sp.setExperiencePoints(0); // empty bar within the level
			}
		});
	}

	/** Minecraft's death message while its death screen / immediate respawn is held back; null = none held. */
	private static net.minecraft.network.chat.Component heldDeath;
	private static long heldDeathAt;

	/**
	 * Minecraft's "you died" (PlayerCombatKillMixin): when Mycelium's death caused it, hold back
	 * Minecraft's death screen and its immediate respawn, so the body lies dying (Minecraft's fall,
	 * red) while Mycelium plays its own death; the respawn comes when Mycelium is alive again. Not in
	 * hardcore. True: held.
	 */
	public static boolean holdDeath(net.minecraft.network.chat.Component message) {
		Minecraft minecraft = Minecraft.getInstance();
		if (!killSent || minecraft.level == null || minecraft.player == null || minecraft.level.getLevelData().isHardcore()) {
			return false;
		}
		heldDeath = message;
		heldDeathAt = System.currentTimeMillis();
		screenCloseLogged = false;
		MycLink.LOG.info("MyceliumCraft: death: Minecraft's {} held back; your body lies dying until Mycelium is alive again",
			minecraft.player.shouldShowDeathScreen() ? "death screen" : "immediate respawn");
		return true;
	}

	/** True while Minecraft's death screen / immediate respawn is held back (GuiDeathScreenMixin). */
	public static boolean deathHeld() {
		return heldDeath != null;
	}

	private static boolean screenCloseLogged;

	/** A screen closed while the death is held: Minecraft would have respawned you here. Logs who did it, once. */
	public static void screenClosedWhileDeathHeld() {
		if (screenCloseLogged) {
			return;
		}
		screenCloseLogged = true;
		StringBuilder who = new StringBuilder();
		StackTraceElement[] stack = Thread.currentThread().getStackTrace();
		for (int k = 3; k < Math.min(stack.length, 9); k++) {
			who.append(k > 3 ? " <- " : "").append(stack[k].getClassName().replaceAll(".*\\.", "")).append('.').append(stack[k].getMethodName());
		}
		MycLink.LOG.info("MyceliumCraft: death: a screen was closed while dead ({}); Minecraft's respawn there held back too", who);
	}

	/** Give a held death back to Minecraft: its death screen, or its immediate respawn. */
	private static void releaseDeath(Minecraft minecraft, LocalPlayer player, String why) {
		var message = heldDeath;
		heldDeath = null;
		if (!player.isDeadOrDying()) {
			return;
		}
		MycLink.LOG.info("MyceliumCraft: death: handed back to Minecraft ({})", why);
		if (player.shouldShowDeathScreen()) {
			minecraft.gui.setScreen(new net.minecraft.client.gui.screens.DeathScreen(message, false, player));
		} else {
			player.respawn();
		}
	}

	/** Your view before Mycelium's death switched it to third person behind; null = not switched. */
	private static net.minecraft.client.CameraType viewBeforeDeath;

	/**
	 * Mycelium's death: Minecraft's view goes to third person behind (Mycelium's camera follows it), so
	 * you see your Minecraft body do Minecraft's own death fall: the captured model is posed by
	 * Minecraft's renderer, which tips a dying player over sideways and tints it red. Not saved to
	 * your options (Minecraft doesn't save the F5 view).
	 */
	private static void viewForDeath(Minecraft minecraft) {
		if (viewBeforeDeath != null) {
			return;
		}
		viewBeforeDeath = minecraft.options.getCameraType();
		if (viewBeforeDeath != net.minecraft.client.CameraType.THIRD_PERSON_BACK) {
			minecraft.options.setCameraType(net.minecraft.client.CameraType.THIRD_PERSON_BACK);
		}
		MycLink.LOG.info("MyceliumCraft: death: view {} -> third person behind, for Minecraft's death animation", viewBeforeDeath);
	}

	/** Mycelium alive again (or left the level): your own view back. */
	private static void viewAfterDeath(Minecraft minecraft, String why) {
		if (viewBeforeDeath == null) {
			return;
		}
		minecraft.options.setCameraType(viewBeforeDeath);
		MycLink.LOG.info("MyceliumCraft: death: view back to {} ({})", viewBeforeDeath, why);
		viewBeforeDeath = null;
	}

	private static float sentMax = Float.NaN, sentHealth = Float.NaN;
	private static float lastMycHealth = Float.NaN;
	private static boolean killSent;
	private static long lastHealthFix, lastRespawn, lastHealthLog, zeroSince;

	/**
	 * Mycelium's health in charge of Minecraft's: health and max health / 5 (5 Mycelium health = half
	 * a heart). Applied on the integrated server (which syncs it to the client) when it changes, or
	 * when Minecraft's drifted (its own damage); MycHealth keeps it above 0 meanwhile. Mycelium at 0
	 * kills the Minecraft player; Mycelium alive again respawns it.
	 */
	private static void syncHealth(Minecraft minecraft, LocalPlayer player, boolean sync) {
		var server = minecraft.getSingleplayerServer();
		if (!sync || server == null) {
			dev.myceliumcraft.world.MycHealth.set(false, 0f);
			if (!Float.isNaN(sentMax) && server != null) {
				// Back to Minecraft's normal 10 hearts outside Mycelium's levels.
				setMaxHealth(server, player, 20f);
				MycLink.LOG.info("MyceliumCraft: health: not synced any more (Mycelium not in a level); max health back to 20");
			}
			sentMax = sentHealth = Float.NaN;
			lastMycHealth = Float.NaN;
			viewAfterDeath(minecraft, "Mycelium not in a level");
			if (heldDeath != null && (!MycLink.myceliumAlive() || System.currentTimeMillis() - heldDeathAt > 30000)) {
				releaseDeath(minecraft, player, MycLink.myceliumAlive() ? "Mycelium still not in a level 30 s after the death" : "Mycelium gone");
			}
			killSent = false;
			zeroSince = 0;
			return;
		}
		// Mycelium's health dropped: Minecraft's red hurt flash on the body (its own animation; the
		// captured player model shows it), as when Minecraft itself hurts you.
		if (!Float.isNaN(lastMycHealth) && MYC.health < lastMycHealth - 0.01f && MYC.health > 0f) {
			player.hurtDuration = 10;
			player.hurtTime = 10;
			MycLink.LOG.info("MyceliumCraft: health: Mycelium took {} damage; Minecraft's hurt flash", String.format("%.1f", lastMycHealth - MYC.health));
		}
		lastMycHealth = MYC.health;
		float max = Math.max(1f, MYC.maxHealth / dev.myceliumcraft.world.MycHealth.PER_POINT);
		float health = Math.max(0f, Math.min(MYC.health, MYC.maxHealth)) / dev.myceliumcraft.world.MycHealth.PER_POINT;
		dev.myceliumcraft.world.MycHealth.set(true, health);
		long now = System.currentTimeMillis();
		if (Float.isNaN(sentMax) || Math.abs(max - sentMax) > 0.001f) {
			sentMax = max;
			setMaxHealth(server, player, max);
			MycLink.LOG.info("MyceliumCraft: health: max health {} (Mycelium {})", String.format("%.2f", max), String.format("%.1f", MYC.maxHealth));
		}
		if (health <= 0f) {
			// Mycelium died: so does the Minecraft player (once), after its health has stayed 0 for
			// a moment, so a single setup frame at 0 can't kill you.
			if (zeroSince == 0) {
				zeroSince = now;
			}
			if (!killSent && !player.isDeadOrDying() && now - zeroSince >= 250) {
				killSent = true;
				viewForDeath(minecraft);
				var uuid = player.getUUID();
				server.execute(() -> {
					ServerPlayer sp = server.getPlayerList().getPlayer(uuid);
					if (sp != null) {
						sp.kill(sp.level());
					}
				});
				MycLink.LOG.info("MyceliumCraft: health: Mycelium's health is 0, the Minecraft player dies too");
			}
			sentHealth = 0f;
			return;
		}
		killSent = false;
		zeroSince = 0;
		viewAfterDeath(minecraft, "Mycelium is alive again");
		if (player.isDeadOrDying()) {
			// Mycelium alive again (new run, back in the hub): respawn.
			if (now - lastRespawn > 1000) {
				lastRespawn = now;
				player.respawn();
				heldDeath = null;
				MycLink.LOG.info("MyceliumCraft: health: Mycelium is alive ({}), respawning the Minecraft player", String.format("%.1f", MYC.health));
			}
			return;
		}
		boolean drifted = Math.abs(player.getHealth() - health) > 0.01f && now - lastHealthFix > 250;
		if (Float.isNaN(sentHealth) || Math.abs(health - sentHealth) > 0.001f || drifted) {
			sentHealth = health;
			lastHealthFix = now;
			var uuid = player.getUUID();
			server.execute(() -> {
				ServerPlayer sp = server.getPlayerList().getPlayer(uuid);
				if (sp != null) {
					sp.setHealth(health);
				}
			});
			if (now - lastHealthLog > 1000) {
				lastHealthLog = now;
				MycLink.LOG.info("MyceliumCraft: health: {} of {} (Mycelium {} of {}){}", String.format("%.2f", health), String.format("%.2f", max),
					String.format("%.1f", MYC.health), String.format("%.1f", MYC.maxHealth), drifted ? String.format("; Minecraft had drifted to %.2f", player.getHealth()) : "");
			}
		}
	}

	private static void setMaxHealth(net.minecraft.server.MinecraftServer server, LocalPlayer player, float max) {
		var uuid = player.getUUID();
		server.execute(() -> {
			ServerPlayer sp = server.getPlayerList().getPlayer(uuid);
			AttributeInstance attribute = sp == null ? null : sp.getAttribute(Attributes.MAX_HEALTH);
			if (attribute != null) {
				attribute.setBaseValue(max);
			}
		});
	}

	/**
	 * Freeze or unfreeze the integrated server's world ticking (what /tick freeze does). Only undoes
	 * a freeze it made itself.
	 */
	private static void freezeWorld(Minecraft minecraft, boolean freeze) {
		if (freeze == frozeWorld) {
			return;
		}
		var server = minecraft.getSingleplayerServer();
		if (server == null) {
			return;
		}
		frozeWorld = freeze;
		server.execute(() -> server.tickRateManager().setFrozen(freeze));
		MycLink.LOG.info("MyceliumCraft: {}", freeze ? "Mycelium is paused: Minecraft's world frozen" : "Mycelium unpaused: Minecraft's world runs again");
	}

	/** Move the player on the client and on the integrated server (as SkyCraft does), so the server doesn't pull it back. */
	private static void teleport(Minecraft minecraft, LocalPlayer player, double x, double y, double z, float yaw) {
		player.setPos(x, y, z);
		// The rendered position blends from xo/yo/zo to the current one; reset it too, or the next
		// frames still report (and draw) the old position.
		player.xo = x;
		player.yo = y;
		player.zo = z;
		// NaN (a re-sync): keep where you face; only the first teleport into a level turns you.
		boolean turn = !Float.isNaN(yaw);
		if (turn) {
			player.setYRot(yaw);
		}
		player.setDeltaMovement(Vec3.ZERO);
		player.resetFallDistance();
		var server = minecraft.getSingleplayerServer();
		if (server != null) {
			var uuid = player.getUUID();
			server.execute(() -> {
				ServerPlayer sp = server.getPlayerList().getPlayer(uuid);
				if (sp != null) {
					sp.teleportTo(x, y, z);
					if (turn) {
						sp.setYRot(yaw);
					}
					sp.resetFallDistance();
				}
			});
		}
		MycLink.LOG.info("MyceliumCraft: teleport {} to ({}, {}, {}) yaw {}", MYC.teleportSeq,
			String.format("%.2f", x), String.format("%.2f", y), String.format("%.2f", z), turn ? String.format("%.0f", yaw) : "kept");
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
