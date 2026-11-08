package dev.myceliumcraft.combat;

import dev.myceliumcraft.link.MycLink;
import net.minecraft.network.syncher.EntityDataAccessor;
import net.minecraft.network.syncher.EntityDataSerializers;
import net.minecraft.network.syncher.SynchedEntityData;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.damagesource.DamageSource;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EntityDimensions;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.Pose;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.level.Level;

/**
 * An invisible stand-in for another spore of your colony, so you collide with it the way Minecraft
 * creatures collide (your request, 2026-11-04). Minecraft's collision between a player and a
 * creature is a soft shove each way, not a wall: its client copy shoves you, and you shove the
 * server's copy. This never moves itself: what it's shoved by is collected here, SporeProxies sends
 * that to Mycelium, and the real spore moves. Can't be aimed at or hurt, drawn by nothing, never
 * saved, and stand-ins don't shove each other.
 */
public class SporeProxy extends Mob {
	private static final EntityDataAccessor<Float> DATA_WIDTH = SynchedEntityData.defineId(SporeProxy.class, EntityDataSerializers.FLOAT);
	private static final EntityDataAccessor<Float> DATA_HEIGHT = SynchedEntityData.defineId(SporeProxy.class, EntityDataSerializers.FLOAT);
	private static long lastShoveLog;
	private static int shoves;

	double shoveX, shoveZ;

	public SporeProxy(EntityType<? extends SporeProxy> type, Level level) {
		super(type, level);
		this.setNoAi(true);
		this.setNoGravity(true);
		this.setInvisible(true);
		this.setSilent(true);
	}

	@Override
	protected void defineSynchedData(SynchedEntityData.Builder builder) {
		super.defineSynchedData(builder);
		builder.define(DATA_WIDTH, 0.6f);
		builder.define(DATA_HEIGHT, 1.8f);
	}

	public void setSize(float w, float h) {
		if (Math.abs(w - this.entityData.get(DATA_WIDTH)) > 0.01f || Math.abs(h - this.entityData.get(DATA_HEIGHT)) > 0.01f) {
			this.entityData.set(DATA_WIDTH, w);
			this.entityData.set(DATA_HEIGHT, h);
			this.refreshDimensions();
		}
	}

	@Override
	public void onSyncedDataUpdated(EntityDataAccessor<?> key) {
		super.onSyncedDataUpdated(key);
		if (DATA_WIDTH.equals(key) || DATA_HEIGHT.equals(key)) {
			this.refreshDimensions();
		}
	}

	/** As EnemyProxy: put exactly on its spore every tick, so no searching for a free position. */
	@Override
	public boolean fudgePositionAfterSizeChange(EntityDimensions previous) {
		return false;
	}

	@Override
	protected EntityDimensions getDefaultDimensions(Pose pose) {
		if (this.entityData == null) {
			return super.getDefaultDimensions(pose); // during construction
		}
		return EntityDimensions.scalable(this.entityData.get(DATA_WIDTH), this.entityData.get(DATA_HEIGHT));
	}

	@Override
	public boolean isPushable() {
		return true;
	}

	/** Minecraft's shove on this one: collected, not moved by. Only the server's copy counts. */
	@Override
	public void push(double x, double y, double z) {
		if (!this.level().isClientSide()) {
			this.shoveX += x;
			this.shoveZ += z;
		}
	}

	/** It shoves players only. The client's copy shoving yours is what you feel. */
	@Override
	protected void doPush(Entity entity) {
		if (!(entity instanceof Player)) {
			return;
		}
		super.doPush(entity);
		if (this.level().isClientSide()) {
			shoves++;
			long now = System.currentTimeMillis();
			if (now - lastShoveLog >= 1000) {
				lastShoveLog = now;
				MycLink.LOG.info("MyceliumCraft: spore stand-ins: shoved your player {} time(s) since the last line", shoves);
				shoves = 0;
			}
		}
	}

	@Override
	public boolean isPickable() {
		return false;
	}

	@Override
	public boolean isAttackable() {
		return false;
	}

	@Override
	public boolean hurtServer(ServerLevel level, DamageSource source, float amount) {
		return false;
	}

	@Override
	public boolean shouldShowName() {
		return false;
	}
}
