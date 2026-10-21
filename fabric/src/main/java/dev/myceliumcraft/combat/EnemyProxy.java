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
 * An invisible stand-in for one of Mycelium's enemies (SkyCraft's ActorProxy, simplified).
 *
 * It's a real Minecraft creature, so Minecraft's own attack rules apply to it: your weapon, attack
 * cooldown, critical hits, sweeping, enchantments, arrows. When a player hurts it, the damage
 * Minecraft worked out is sent to Mycelium (MycLink.writeHit), which hurts the real enemy; the
 * stand-in itself loses nothing and stays alive. Everything else (fire, lava, other mobs) is
 * ignored. No AI, no gravity, can't be pushed, drawn by nothing (NoopRenderer), never saved.
 *
 * Its enemy id and size are synced to Minecraft's client (SynchedEntityData): the crosshair picks
 * on the client's copy, which otherwise kept Minecraft's default 0.6 x 0.6 box (your report
 * 2026-10-19 led to this).
 */
public class EnemyProxy extends Mob {
	private static final EntityDataAccessor<Integer> DATA_ENEMY_ID = SynchedEntityData.defineId(EnemyProxy.class, EntityDataSerializers.INT);
	private static final EntityDataAccessor<Float> DATA_WIDTH = SynchedEntityData.defineId(EnemyProxy.class, EntityDataSerializers.FLOAT);
	private static final EntityDataAccessor<Float> DATA_HEIGHT = SynchedEntityData.defineId(EnemyProxy.class, EntityDataSerializers.FLOAT);
	private static long lastLog;

	public EnemyProxy(EntityType<? extends EnemyProxy> type, Level level) {
		super(type, level);
		this.setNoAi(true);
		this.setNoGravity(true);
		this.setInvisible(true);
		this.setSilent(true);
	}

	@Override
	protected void defineSynchedData(SynchedEntityData.Builder builder) {
		super.defineSynchedData(builder);
		builder.define(DATA_ENEMY_ID, 0);
		builder.define(DATA_WIDTH, 0.6f);
		builder.define(DATA_HEIGHT, 0.6f);
	}

	public int enemyId() {
		return this.entityData.get(DATA_ENEMY_ID);
	}

	public void setEnemyId(int id) {
		this.entityData.set(DATA_ENEMY_ID, id);
	}

	/**
	 * The enemy's size in blocks; the hitbox follows it, on the server and (synced) on the client.
	 * Only when it really changes (0.05 blocks): enemies' boxes wobble as they animate.
	 */
	public void setSize(float w, float h) {
		if (Math.abs(w - this.entityData.get(DATA_WIDTH)) > 0.05f || Math.abs(h - this.entityData.get(DATA_HEIGHT)) > 0.05f) {
			this.entityData.set(DATA_WIDTH, w);
			this.entityData.set(DATA_HEIGHT, h);
			this.refreshDimensions();
		}
	}

	@Override
	public void onSyncedDataUpdated(EntityDataAccessor<?> key) {
		super.onSyncedDataUpdated(key);
		if (DATA_WIDTH.equals(key) || DATA_HEIGHT.equals(key)) {
			this.refreshDimensions(); // the client's copy takes the new size too
		}
	}

	/**
	 * After a size change Minecraft pushes an entity out of any block it now overlaps
	 * (findFreePosition): it gathers and merges the collision shapes all around it, here with
	 * Mycelium's ground. That took 0.3-1.6 s on the server near the end of a level, many times,
	 * so hits waited and came out weak (your report, 2026-10-21; ServerStallWatch caught it).
	 * A stand-in is put exactly onto its enemy every tick and never moves by physics: no push.
	 */
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
	public boolean hurtServer(ServerLevel level, DamageSource source, float amount) {
		Entity attacker = source.getEntity();
		if (!(attacker instanceof Player) || amount <= 0f) {
			return false;
		}
		MycLink.writeHit(this.enemyId(), amount, 0);
		long now = System.currentTimeMillis();
		if (now - lastLog > 250) {
			lastLog = now;
			MycLink.LOG.info("MyceliumCraft: combat: hit Mycelium enemy #{} for {} points ({})", this.enemyId(), String.format("%.2f", amount), source.getMsgId());
		}
		return true; // Minecraft's hit effects (knockback, sweep, sounds); the next tick puts it back in place
	}

	@Override
	public boolean isPushable() {
		return false;
	}

	@Override
	protected void doPush(Entity entity) {
	}

	@Override
	protected void pushEntities() {
	}

	@Override
	public boolean shouldShowName() {
		return false;
	}
}
