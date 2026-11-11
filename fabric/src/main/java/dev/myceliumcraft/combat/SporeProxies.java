package dev.myceliumcraft.combat;

import dev.myceliumcraft.link.MycLink;
import dev.myceliumcraft.link.Proto;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.fabricmc.fabric.api.object.builder.v1.entity.FabricDefaultAttributeRegistry;
import net.minecraft.core.Registry;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.EntitySpawnReason;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.MobCategory;
import net.minecraft.world.phys.Vec3;

/**
 * One SporeProxy per other spore of your colony, in the world you're in, put on that spore every
 * server tick (ColonyPush.cs sends where they are). What Minecraft shoves a stand-in by becomes a
 * speed that fades as a creature's does on the ground, and the distance goes to Mycelium, which
 * moves the real spore. Integrated server thread.
 */
public final class SporeProxies {
	public static final ResourceKey<EntityType<?>> KEY = ResourceKey.create(Registries.ENTITY_TYPE, Identifier.fromNamespaceAndPath("myceliumcraft", "spore_proxy"));
	public static final EntityType<SporeProxy> TYPE = Registry.register(BuiltInRegistries.ENTITY_TYPE, KEY,
		EntityType.Builder.<SporeProxy>of(SporeProxy::new, MobCategory.MISC).sized(0.6f, 1.8f).noSummon().noSave().clientTrackingRange(8)
			.updateInterval(1) // collisions need it where the spore is, not 3 ticks ago
			.build(KEY));

	private static final float FADE = 0.546f; // what a creature on ordinary ground keeps of its speed each tick
	private static final float SHARE = 0.5f;  // "a little bit": the spore takes this much of a shove

	private static final SporeProxy[] PROXIES = new SporeProxy[Proto.COL_MAX];
	private static final float[] POSITIONS = new float[Proto.COL_MAX * 3];
	private static final float[] SPEED_X = new float[Proto.COL_MAX], SPEED_Z = new float[Proto.COL_MAX];
	private static final float[] TOTAL_X = new float[Proto.COL_MAX], TOTAL_Z = new float[Proto.COL_MAX];
	private static int lastCount = -1;
	private static float lastWidth = -1f;
	private static double pushed;
	private static long lastLog;

	private SporeProxies() {
	}

	public static void init() {
		FabricDefaultAttributeRegistry.register(TYPE, Mob.createMobAttributes());
		ServerTickEvents.END_LEVEL_TICK.register(SporeProxies::tick);
		MycLink.LOG.info("MyceliumCraft: spore stand-ins registered ({})", KEY.identifier());
	}

	private static void tick(ServerLevel level) {
		if (level.players().isEmpty()) {
			return; // only the world the player is in
		}
		int n = MycLink.myceliumAlive() ? MycLink.readSporePositions(POSITIONS) : 0;
		if (n < 0) {
			return; // unreadable this tick: leave them where they are
		}
		n = Math.min(n, MycLink.sporeStandIns()); // not the scenery spores (the tutorial's sleepers) after your colony
		ServerPlayer player = level.players().get(0);
		float width = player.getBbWidth(), height = player.getBbHeight();
		int count = 0;
		for (int i = 0; i < Proto.COL_MAX; i++) {
			float x = POSITIONS[i * 3], y = POSITIONS[i * 3 + 1], z = POSITIONS[i * 3 + 2];
			if (i >= n || Float.isNaN(y)) {
				remove(i);
				continue;
			}
			SporeProxy proxy = PROXIES[i];
			if (proxy == null || proxy.isRemoved() || proxy.level() != level) {
				remove(i);
				proxy = TYPE.create(level, EntitySpawnReason.EVENT);
				if (proxy == null) {
					continue;
				}
				proxy.setSize(width, height);
				proxy.snapTo(x, y, z, 0f, 0f);
				level.addFreshEntity(proxy);
				PROXIES[i] = proxy;
			}
			count++;
			proxy.setSize(width, height);
			proxy.setPos(x, y, z);
			proxy.setDeltaMovement(Vec3.ZERO);
			SPEED_X[i] += (float) proxy.shoveX * SHARE;
			SPEED_Z[i] += (float) proxy.shoveZ * SHARE;
			proxy.shoveX = proxy.shoveZ = 0;
			if (Math.abs(SPEED_X[i]) + Math.abs(SPEED_Z[i]) > 0.0005f) {
				TOTAL_X[i] += SPEED_X[i];
				TOTAL_Z[i] += SPEED_Z[i];
				pushed += Math.sqrt(SPEED_X[i] * SPEED_X[i] + SPEED_Z[i] * SPEED_Z[i]);
				MycLink.writeSporePush(i, TOTAL_X[i], TOTAL_Z[i]);
				SPEED_X[i] *= FADE;
				SPEED_Z[i] *= FADE;
			} else {
				SPEED_X[i] = SPEED_Z[i] = 0f;
			}
		}
		if (count != lastCount || width != lastWidth) {
			lastCount = count;
			lastWidth = width;
			MycLink.LOG.info("MyceliumCraft: spore stand-ins: {} in the world, {} x {} blocks like your player", count, String.format("%.2f", width), String.format("%.2f", height));
		}
		long now = System.currentTimeMillis();
		if (pushed > 0 && now - lastLog >= 1000) {
			lastLog = now;
			MycLink.LOG.info("MyceliumCraft: spore stand-ins: you pushed spores {} blocks since the last line", String.format("%.2f", pushed));
			pushed = 0;
		}
	}

	private static void remove(int slot) {
		if (PROXIES[slot] != null) {
			PROXIES[slot].discard();
			PROXIES[slot] = null;
		}
		SPEED_X[slot] = SPEED_Z[slot] = 0f;
	}
}
