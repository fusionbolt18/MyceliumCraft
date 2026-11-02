package dev.myceliumcraft;

import dev.myceliumcraft.link.MycLink;
import java.lang.reflect.Constructor;
import java.lang.reflect.Method;
import java.lang.reflect.Proxy;
import net.minecraft.client.Minecraft;
import net.minecraft.resources.Identifier;
import net.minecraft.world.entity.Avatar;

/**
 * Living Cyclone's animation on your Minecraft player while it spins (your request, 2026-11-01):
 * our own myceliumcraft:living_cyclone (assets/myceliumcraft/player_animations, in the mod's built-in
 * resource pack: a 3-tick wind-up, then a full turn every 4 ticks, looping from the first turn, as
 * its file says), or Better Combat's two_handed_spin looped if ours can't be found.
 *
 * Played through Player Animation Library (PAL), the library Better Combat animates with, using
 * only its public API, by name (reflection), so nothing happens without it and neither mod is
 * changed (AGENTS.md rule 7):
 * - our own animation layer (PlayerAnimationFactory.registerFactory), above Better Combat's, whose
 *   controller plays nothing by itself (its handler answers PlayState.STOP), only what we trigger;
 * - while Mycelium's Living Cyclone flag is on, it's triggered there (RawAnimation.begin().thenPlay:
 *   the file's own loop; Better Combat's with thenLoop); when it goes off, it's stopped.
 * Mycelium's picture of your body (MycPlayerModel) is posed by the same renderer, so it spins its
 * arms there too. Client thread.
 */
public final class MycSkillAnimation {
	private static final Identifier LAYER = Identifier.fromNamespaceAndPath("myceliumcraft", "skill");
	private static final Identifier CYCLONE = Identifier.fromNamespaceAndPath("myceliumcraft", "living_cyclone");
	private static final Identifier SPIN = Identifier.fromNamespaceAndPath("bettercombat", "two_handed_spin"); // if ours is missing
	private static final Identifier ROLL = Identifier.fromNamespaceAndPath("myceliumcraft", "dodge_roll");
	private static final int ROLL_TICKS = 12; // the roll ends upright at tick 12 (its file); stopped there
	private static final int PRIORITY = 2000; // above Better Combat's own layers

	private static boolean ready, failed;
	private static boolean playing;
	private static long startedMs;
	private static Method getLayer, getAnimation, begin, thenPlay, thenLoop, trigger, stop;
	private static Identifier played;
	private static boolean rollWas, rolling, rollMissing;
	private static int rollTicks;

	private MycSkillAnimation() {
	}

	/** Once, at client start: our layer in PAL, if PAL is there. */
	public static void init() {
		try {
			ClassLoader cl = MycSkillAnimation.class.getClassLoader();
			Class<?> factoryType = Class.forName("com.zigythebird.playeranim.api.PlayerAnimationFactory", true, cl);
			Class<?> handlerType = Class.forName("com.zigythebird.playeranimcore.animation.AnimationController$AnimationStateHandler", true, cl);
			Class<?> controllerType = Class.forName("com.zigythebird.playeranim.animation.PlayerAnimationController", true, cl);
			Class<?> playState = Class.forName("com.zigythebird.playeranimcore.enums.PlayState", true, cl);
			Class<?> access = Class.forName("com.zigythebird.playeranim.api.PlayerAnimationAccess", true, cl);
			Class<?> resources = Class.forName("com.zigythebird.playeranim.animation.PlayerAnimResources", true, cl);
			Class<?> raw = Class.forName("com.zigythebird.playeranimcore.animation.RawAnimation", true, cl);
			Class<?> animation = Class.forName("com.zigythebird.playeranimcore.animation.Animation", true, cl);

			Object stopState = playState.getField("STOP").get(null);
			Object handler = Proxy.newProxyInstance(cl, new Class<?>[] { handlerType }, (proxy, m, args) -> switch (m.getName()) {
				case "handle" -> stopState; // nothing of its own; only triggered animations
				case "hashCode" -> System.identityHashCode(proxy);
				case "equals" -> proxy == args[0];
				default -> "MyceliumCraft skill animation handler";
			});
			Constructor<?> make = controllerType.getConstructor(Avatar.class, handlerType);
			Object factory = Proxy.newProxyInstance(cl, new Class<?>[] { factoryType }, (proxy, m, args) -> switch (m.getName()) {
				case "invoke" -> make.newInstance(args[0], handler);
				case "hashCode" -> System.identityHashCode(proxy);
				case "equals" -> proxy == args[0];
				default -> "MyceliumCraft skill animation factory";
			});
			Object holder = factoryType.getField("ANIMATION_DATA_FACTORY").get(null);
			holder.getClass().getMethod("registerFactory", Identifier.class, int.class, factoryType).invoke(holder, LAYER, PRIORITY, factory);

			getLayer = access.getMethod("getPlayerAnimationLayer", Avatar.class, Identifier.class);
			getAnimation = resources.getMethod("getAnimation", Identifier.class);
			begin = raw.getMethod("begin");
			thenPlay = raw.getMethod("thenPlay", animation);
			thenLoop = raw.getMethod("thenLoop", animation);
			trigger = controllerType.getMethod("triggerAnimation", raw);
			stop = controllerType.getMethod("stopTriggeredAnimation");
			ready = true;
			MycLink.LOG.info("MyceliumCraft: skill animations ready (Player Animation Library layer {}, priority {})", LAYER, PRIORITY);
		} catch (ReflectiveOperationException | RuntimeException | LinkageError e) {
			MycLink.LOG.info("MyceliumCraft: no skill animations (Player Animation Library missing or changed: {})", e.toString());
		}
	}

	/**
	 * Every client tick: loop the spin while Living Cyclone is on; play the combat roll
	 * (myceliumcraft:dodge_roll, your request 2026-11-02) when Mycelium's dodge roll starts, and stop
	 * it once it's upright again (its last frame is a full turn, the same as standing).
	 */
	public static void tick(Minecraft minecraft, boolean cyclone, boolean roll) {
		if (!ready || failed || minecraft.player == null) {
			return;
		}
		boolean rollStarts = roll && !rollWas;
		rollWas = roll;
		if (!cyclone && !playing) {
			tickRoll(minecraft, rollStarts);
		}
		if (cyclone == playing) {
			return;
		}
		rolling = false; // the spin replaces a roll
		try {
			Object controller = getLayer.invoke(null, minecraft.player, LAYER);
			if (controller == null) {
				return;
			}
			if (cyclone) {
				Object ours = getAnimation.invoke(null, CYCLONE);
				Object spin = ours != null ? ours : getAnimation.invoke(null, SPIN);
				if (spin == null) {
					failed = true;
					MycLink.LOG.info("MyceliumCraft: neither {} nor {} found; Living Cyclone isn't animated", CYCLONE, SPIN);
					return;
				}
				played = ours != null ? CYCLONE : SPIN;
				trigger.invoke(controller, (ours != null ? thenPlay : thenLoop).invoke(begin.invoke(null), spin));
				startedMs = System.currentTimeMillis();
				MycLink.LOG.info("MyceliumCraft: Living Cyclone: playing {} on the player{}", played, ours != null ? " (its own loop)" : " looped (ours wasn't found)");
			} else {
				stop.invoke(controller);
				MycLink.LOG.info("MyceliumCraft: Living Cyclone over: {} stopped after {} s", played, String.format("%.2f", (System.currentTimeMillis() - startedMs) / 1000.0));
			}
			playing = cyclone;
		} catch (ReflectiveOperationException | RuntimeException e) {
			failed = true;
			MycLink.LOG.warn("MyceliumCraft: couldn't play the Living Cyclone animation", e);
		}
	}

	private static void tickRoll(Minecraft minecraft, boolean starts) {
		try {
			Object controller = getLayer.invoke(null, minecraft.player, LAYER);
			if (controller == null) {
				return;
			}
			if (starts && !rollMissing) {
				Object anim = getAnimation.invoke(null, ROLL);
				if (anim == null) {
					rollMissing = true;
					MycLink.LOG.info("MyceliumCraft: {} not found; the dodge roll isn't animated", ROLL);
					return;
				}
				trigger.invoke(controller, thenPlay.invoke(begin.invoke(null), anim));
				rolling = true;
				rollTicks = 0;
				startedMs = System.currentTimeMillis();
				MycLink.LOG.info("MyceliumCraft: dodge roll: playing {} on the player", ROLL);
			} else if (rolling && ++rollTicks >= ROLL_TICKS) {
				stop.invoke(controller);
				rolling = false;
				MycLink.LOG.info("MyceliumCraft: dodge roll: {} done and stopped after {} ticks ({} s)", ROLL, rollTicks,
					String.format("%.2f", (System.currentTimeMillis() - startedMs) / 1000.0));
			}
		} catch (ReflectiveOperationException | RuntimeException e) {
			rollMissing = true;
			MycLink.LOG.warn("MyceliumCraft: couldn't play the dodge roll animation", e);
		}
	}
}
