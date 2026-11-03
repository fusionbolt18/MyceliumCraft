package dev.myceliumcraft;

import dev.myceliumcraft.link.MycLink;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Set;
import net.minecraft.world.entity.EntityTypes;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.phys.Vec3;

/**
 * Thrown weapons and resources don't clump together (your request, 2026-11-02): two thrown from the
 * same spot landed in the same spot (your log: #7 and #8 0.5 blocks apart), and Mycelium then shows
 * the real ones inside each other. Every server tick, two of your thrown items closer than SPREAD
 * blocks across (and within a block up or down) are nudged away from each other, harder the closer
 * they are, until they're apart. Minecraft never merges them (each has its own id).
 */
public final class MycThrownSpread {
	private static final double SPREAD = 0.8; // blocks between thrown items, across
	private static final double PUSH = 0.04; // blocks/tick added per tick when right on top of each other
	private static final Set<String> LOGGED = new HashSet<>();

	private MycThrownSpread() {
	}

	private static int id(ItemEntity e) {
		int w = MycWeapons.weaponId(e.getItem());
		return w != 0 ? w : -MycResources.resourceId(e.getItem()); // resources negative: they share numbers with weapons
	}

	public static void init() {
		net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents.END_SERVER_TICK.register(server -> {
			for (var level : server.getAllLevels()) {
				List<ItemEntity> items = new ArrayList<>(level.getEntities(EntityTypes.ITEM, e -> id(e) != 0));
				for (int i = 0; i < items.size(); i++) {
					for (int j = i + 1; j < items.size(); j++) {
						spread(items.get(i), items.get(j));
					}
				}
			}
		});
	}

	private static void spread(ItemEntity a, ItemEntity b) {
		double dx = a.getX() - b.getX(), dz = a.getZ() - b.getZ();
		if (Math.abs(a.getY() - b.getY()) > 1.0) {
			return;
		}
		double dist = Math.sqrt(dx * dx + dz * dz);
		if (dist >= SPREAD) {
			return;
		}
		double ux, uz; // the direction from b to a
		if (dist < 1e-3) {
			// Right on top of each other: apart along a direction of their own.
			double angle = (id(a) * 2.399963) % (2 * Math.PI);
			ux = Math.cos(angle);
			uz = Math.sin(angle);
		} else {
			ux = dx / dist;
			uz = dz / dist;
		}
		double push = PUSH * (1 - dist / SPREAD);
		Vec3 away = new Vec3(ux * push, 0, uz * push);
		a.setDeltaMovement(a.getDeltaMovement().add(away));
		b.setDeltaMovement(b.getDeltaMovement().subtract(away));
		a.needsSync = true; // send the new motion to the client
		b.needsSync = true;
		String pair = id(a) + "/" + id(b);
		if (LOGGED.add(pair)) {
			MycLink.LOG.info("MyceliumCraft: thrown items {} and {} are {} blocks apart: pushed apart (to {} blocks)",
				name(a), name(b), String.format("%.2f", dist), SPREAD);
		}
		if (LOGGED.size() > 200) {
			LOGGED.clear();
		}
	}

	private static String name(ItemEntity e) {
		int id = id(e);
		return id > 0 ? "weapon #" + id : "resource #" + -id;
	}
}
