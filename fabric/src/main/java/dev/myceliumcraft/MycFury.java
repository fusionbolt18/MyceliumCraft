package dev.myceliumcraft;

import dev.myceliumcraft.link.MycLink;
import java.util.Collections;
import java.util.Set;
import java.util.WeakHashMap;
import net.minecraft.client.Minecraft;
import net.minecraft.client.particle.Particle;
import net.minecraft.core.particles.ColorParticleOption;
import net.minecraft.core.particles.ParticleTypes;
import net.minecraft.util.RandomSource;

/**
 * Relentless Fury's frenzy (FuryWatch.cs, MycState's FURY flag): red potion particles around your
 * player for as long as it lasts (your request, 2026-11-08). Spawned the way Minecraft spawns a
 * potion effect's particles around a creature (LivingEntity: an ENTITY_EFFECT particle in the body,
 * here about 7 a second), on the client. Mycelium draws the world, so Minecraft's own particles are
 * hidden while the overlay is on (QuadParticleGroupMixin); these are kept, as Better Combat's swing
 * trails are, and drawn on top of Mycelium's picture (MycFirstPersonArms).
 */
public final class MycFury {
	private static final ColorParticleOption RED = ColorParticleOption.create(ParticleTypes.ENTITY_EFFECT, 0xFFF82423);
	private static final Set<Particle> OURS = Collections.newSetFromMap(new WeakHashMap<>());
	private static final RandomSource RANDOM = RandomSource.create();
	private static boolean lastOn;
	private static int spawned;

	private MycFury() {
	}

	/** Every client tick. */
	public static void tick(Minecraft minecraft, boolean on) {
		if (on != lastOn) {
			MycLink.LOG.info("MyceliumCraft: fury: Relentless Fury's frenzy {}", on ? "on: red potion particles" : "over (" + spawned + " particles)");
			lastOn = on;
			spawned = 0;
		}
		var player = minecraft.player;
		if (!on || player == null || minecraft.level == null || RANDOM.nextInt(3) != 0) {
			return;
		}
		Particle p = minecraft.particleEngine.createParticle(RED, player.getRandomX(0.5), player.getRandomY(), player.getRandomZ(0.5), 1.0, 1.0, 1.0);
		if (p != null) {
			OURS.add(p);
			spawned++;
		}
	}

	/** One of ours: drawn over Mycelium's picture (QuadParticleGroupMixin, MycFirstPersonArms). */
	public static boolean ours(Object particle) {
		return particle instanceof Particle p && OURS.contains(p);
	}
}
