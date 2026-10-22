package dev.myceliumcraft;

import dev.myceliumcraft.link.MycLink;
import java.lang.reflect.Proxy;
import net.minecraft.client.Minecraft;
import net.minecraft.core.component.DataComponentType;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.resources.Identifier;

/**
 * Better Combat, seen from our side. Only its public API is used, by name (reflection), so
 * nothing happens without it, and Better Combat itself is never changed (AGENTS.md rule 7).
 *
 * Its attacks don't always go through Minecraft's startAttack, where our full-charge rule and
 * Mycelium's attack events (swingStarted) hook in (MinecraftMixin): Better Combat cancels
 * startAttack for the weapons it animates and runs its own attack. So for those weapons, the
 * swing is counted from its ATTACK_START event instead.
 */
public final class MycBetterCombat {
	private static DataComponentType<?> presetComponent;
	private static boolean looked;
	private static int starts;
	private static long lastLogMs;

	private MycBetterCombat() {
	}

	/** Once, at client start: listen to Better Combat's attack starts. */
	public static void init() {
		try {
			Class<?> events = Class.forName("net.bettercombat.api.client.BetterCombatClientEvents");
			Class<?> listener = Class.forName("net.bettercombat.api.client.BetterCombatClientEvents$PlayerAttackStart");
			Object publisher = events.getField("ATTACK_START").get(null);
			Object handler = Proxy.newProxyInstance(listener.getClassLoader(), new Class<?>[] { listener }, (proxy, method, args) -> {
				if (method.getName().equals("onPlayerAttackStart")) {
					attackStarted();
					return null;
				}
				return switch (method.getName()) {
					case "hashCode" -> System.identityHashCode(proxy);
					case "equals" -> proxy == args[0];
					case "toString" -> "MyceliumCraft attack listener";
					default -> null;
				};
			});
			publisher.getClass().getMethod("register", Object.class).invoke(publisher, handler);
			MycLink.LOG.info("MyceliumCraft: Better Combat found; its attack starts count as Mycelium swings");
		} catch (ReflectiveOperationException | RuntimeException | LinkageError e) {
			MycLink.LOG.info("MyceliumCraft: no Better Combat attack events ({})", e.toString());
		}
	}

	/** Does Better Combat animate the main-hand item (it has a "bettercombat:preset_id")? */
	public static boolean animatesMainHand(Minecraft minecraft) {
		if (!looked) {
			looked = true;
			presetComponent = BuiltInRegistries.DATA_COMPONENT_TYPE.getValue(Identifier.fromNamespaceAndPath("bettercombat", "preset_id"));
		}
		return presetComponent != null && minecraft.player != null && minecraft.player.getMainHandItem().has(presetComponent);
	}

	private static void attackStarted() {
		MyceliumCraftClient.swingStarted(); // Mycelium's attack events (weapon attributes)
		starts++;
		long now = System.currentTimeMillis();
		if (now - lastLogMs >= 10_000) {
			lastLogMs = now;
			MycLink.LOG.info("MyceliumCraft: Better Combat attack started ({} in the last 10 s): Mycelium swing sent", starts);
			starts = 0;
		}
	}
}
