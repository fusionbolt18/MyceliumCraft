package dev.myceliumcraft.combat;

import dev.myceliumcraft.link.MycLink;
import dev.myceliumcraft.link.Proto;
import java.util.HashMap;
import java.util.HashSet;
import java.util.Map;
import java.util.Set;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.fabricmc.fabric.api.object.builder.v1.entity.FabricDefaultAttributeRegistry;
import net.minecraft.core.Registry;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.MobCategory;
import net.minecraft.world.entity.ai.attributes.Attributes;

/**
 * Keeps one invisible stand-in (EnemyProxy) per Mycelium enemy in the table Mycelium sends, in
 * the world the player is in: spawned when an enemy appears, moved and resized every server tick,
 * removed when it dies or leaves the table (or Mycelium goes away). Integrated server thread.
 */
public final class EnemyProxies {
	public static final ResourceKey<EntityType<?>> KEY = ResourceKey.create(Registries.ENTITY_TYPE, Identifier.fromNamespaceAndPath("myceliumcraft", "enemy_proxy"));
	public static final EntityType<EnemyProxy> TYPE = Registry.register(BuiltInRegistries.ENTITY_TYPE, KEY,
		EntityType.Builder.<EnemyProxy>of(EnemyProxy::new, MobCategory.MISC).sized(0.6f, 0.6f).noSummon().noSave().clientTrackingRange(8)
			.updateInterval(1) // the server's copy (hit checks) told every tick, not every 3
			.build(KEY));

	private static final Map<Integer, EnemyProxy> PROXIES = new HashMap<>();
	private static final MycLink.Enemy[] TABLE = new MycLink.Enemy[Proto.E_MAX];
	private static final MycLink.Enemy[] CLIENT_TABLE = new MycLink.Enemy[Proto.E_MAX]; // client thread's own copy
	private static final Map<Integer, MycLink.Enemy> CLIENT_BY_ID = new HashMap<>();
	private static long lastSnapLog;
	private static double snapMoved;
	private static int snaps;
	private static int lastCount = -1;

	private EnemyProxies() {
	}

	/** Register the attributes and the per-tick update (once, at start). */
	public static void init() {
		for (int i = 0; i < TABLE.length; i++) {
			TABLE[i] = new MycLink.Enemy();
			CLIENT_TABLE[i] = new MycLink.Enemy();
		}
		FabricDefaultAttributeRegistry.register(TYPE, Mob.createMobAttributes().add(Attributes.MAX_HEALTH, 1024.0));
		ServerTickEvents.END_LEVEL_TICK.register(EnemyProxies::tick);
		MycLink.LOG.info("MyceliumCraft: combat: enemy stand-ins registered ({})", KEY.identifier());
	}

	/**
	 * Client side, at the start of every client tick (when Minecraft picks what the crosshair is on)
	 * and before the boxes are sent for -mcShowHitboxes: put every stand-in's client copy exactly
	 * where Mycelium's enemy is now, and cancel the glide towards the server's (older) position.
	 * Minecraft otherwise moves client copies from server updates and glides over a few ticks, so
	 * fast enemies' hitboxes lagged behind them (your report, 2026-10-19).
	 */
	public static void clientSnap(net.minecraft.client.multiplayer.ClientLevel level) {
		if (level == null || !MycLink.myceliumAlive()) {
			return;
		}
		int n = MycLink.readEnemies(CLIENT_TABLE);
		if (n <= 0) {
			return;
		}
		CLIENT_BY_ID.clear();
		for (int i = 0; i < n; i++) {
			CLIENT_BY_ID.put(CLIENT_TABLE[i].id, CLIENT_TABLE[i]);
		}
		for (var entity : level.entitiesForRendering()) {
			if (!(entity instanceof EnemyProxy proxy)) {
				continue;
			}
			MycLink.Enemy e = CLIENT_BY_ID.get(proxy.enemyId());
			if (e == null) {
				continue;
			}
			snapMoved += Math.sqrt(proxy.distanceToSqr(e.x, e.y, e.z));
			snaps++;
			proxy.getInterpolation().cancel();
			proxy.setPos(e.x, e.y, e.z);
			proxy.setYRot(e.yaw);
		}
		long now = System.currentTimeMillis();
		if (now - lastSnapLog >= 1000 && snaps > 0) {
			lastSnapLog = now;
			MycLink.LOG.info("MyceliumCraft: combat: client stand-ins snapped to Mycelium {} times in the last second, {} blocks off on average before the snap",
				snaps, String.format("%.3f", snapMoved / snaps));
			snaps = 0;
			snapMoved = 0;
		}
	}

	private static void tick(ServerLevel level) {
		// Only the world the player is in.
		if (level.players().isEmpty()) {
			return;
		}
		int n = MycLink.myceliumAlive() ? MycLink.readEnemies(TABLE) : 0;
		if (n < 0) {
			return; // unreadable this tick: leave them where they are
		}
		Set<Integer> seen = new HashSet<>();
		for (int i = 0; i < n; i++) {
			MycLink.Enemy e = TABLE[i];
			if ((e.flags & Proto.EN_ALIVE) == 0) {
				continue;
			}
			seen.add(e.id);
			EnemyProxy proxy = PROXIES.get(e.id);
			if (proxy == null || proxy.isRemoved() || proxy.level() != level) {
				proxy = TYPE.create(level, net.minecraft.world.entity.EntitySpawnReason.EVENT);
				if (proxy == null) {
					continue;
				}
				proxy.setEnemyId(e.id);
				proxy.setSize(e.width, e.height);
				proxy.snapTo(e.x, e.y, e.z, e.yaw, 0f);
				level.addFreshEntity(proxy);
				PROXIES.put(e.id, proxy);
			}
			proxy.setSize(e.width, e.height);
			proxy.setPos(e.x, e.y, e.z);
			proxy.setYRot(e.yaw);
			proxy.setYHeadRot(e.yaw);
			proxy.setDeltaMovement(net.minecraft.world.phys.Vec3.ZERO);
		}
		PROXIES.entrySet().removeIf(entry -> {
			if (seen.contains(entry.getKey()) && !entry.getValue().isRemoved()) {
				return false;
			}
			entry.getValue().discard();
			return true;
		});
		if (PROXIES.size() != lastCount) {
			lastCount = PROXIES.size();
			MycLink.LOG.info("MyceliumCraft: combat: {} enemy stand-ins in the world", lastCount);
		}
	}
}
