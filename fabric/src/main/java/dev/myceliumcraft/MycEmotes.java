package dev.myceliumcraft;

import dev.myceliumcraft.link.MycLink;
import java.lang.reflect.Method;
import java.util.UUID;
import net.minecraft.world.entity.Avatar;

/**
 * Emotecraft's emotes on the other spores' made-up bodies (MycSporeBodies), by name (reflection), so
 * nothing happens without Emotecraft and it isn't changed (AGENTS.md rule 7). Emotecraft gives every
 * Avatar its emote layer (IPlayerEntity: emotecraft$playEmote, isPlayingEmote, stopEmote) through
 * Player Animation Library, whose AvatarAnimManager moves animations on when the avatar ticks
 * (handleAnimations, timed by the avatar's tickCount). The bodies aren't in the world, so nothing
 * ticks them: tick() does what Avatar.tick does for that.
 */
final class MycEmotes {
	/** Emotecraft's built-in "Waving" (assets/emotecraft/emotes/waving.json): 50 ticks, no loop. */
	private static final UUID WAVING = UUID.fromString("33b912f8-0aa0-45e6-a2d4-9b5677e6f35c");
	/**
	 * Dancing at the beetle drum (your request, 2026-11-09), one picked at random each time: Emotecraft's
	 * built-in "Club Penguin Dance", "Kazotsky Kick", "Roblox Potion Dance" (all looping) and "Over
	 * here" (here.json, not looping: played again while the dance goes on).
	 */
	static final UUID[] DANCES = {
		UUID.fromString("ffdcfa49-0000-0000-0000-000000000000"),
		UUID.fromString("01a53a42-2fd2-418c-8e46-e4ee1ff9ee6a"),
		UUID.fromString("f9f6669d-f1b3-4170-8bc5-475a9a600439"),
		UUID.fromString("3045b335-12ca-4ddb-aca5-0aef450a5e4c"),
	};
	static final String[] DANCE_NAMES = { "Club Penguin Dance", "Kazotsky Kick", "Roblox Potion Dance", "Over here" };
	private static final java.util.Random RANDOM = new java.util.Random();

	private static boolean looked, missing;
	private static Method fromUuid, play, playing, stop, getManager, handle;
	private static java.lang.reflect.Field emoteField;

	private MycEmotes() {
	}

	private static boolean look() {
		if (looked) {
			return !missing;
		}
		looked = true;
		try {
			ClassLoader cl = MycEmotes.class.getClassLoader();
			Class<?> holder = Class.forName("io.github.kosmx.emotes.main.EmoteHolder", true, cl);
			Class<?> entity = Class.forName("io.github.kosmx.emotes.main.mixinFunctions.IPlayerEntity", true, cl);
			Class<?> animation = Class.forName("com.zigythebird.playeranimcore.animation.Animation", true, cl);
			Class<?> animated = Class.forName("com.zigythebird.playeranim.accessors.IAnimatedAvatar", true, cl);
			Class<?> manager = Class.forName("com.zigythebird.playeranim.animation.AvatarAnimManager", true, cl);
			fromUuid = holder.getMethod("getEmoteFromUuid", UUID.class);
			emoteField = holder.getField("emote");
			play = entity.getMethod("emotecraft$playEmote", animation, float.class, boolean.class);
			playing = entity.getMethod("isPlayingEmote");
			stop = entity.getMethod("stopEmote");
			getManager = animated.getMethod("playerAnimLib$getAnimManager");
			handle = manager.getMethod("handleAnimations", float.class, boolean.class, boolean.class);
			MycLink.LOG.info("MyceliumCraft: emotes: Emotecraft found; waving spores use its wave");
		} catch (ReflectiveOperationException | LinkageError e) {
			missing = true;
			MycLink.LOG.info("MyceliumCraft: emotes: no Emotecraft ({}); waving spores aren't animated", e.toString());
		}
		return !missing;
	}

	/** Start Emotecraft's wave on a body. False if it can't (no Emotecraft, no wave emote). */
	static boolean wave(Avatar body) {
		return play(body, WAVING, "Waving");
	}

	/** Is Emotecraft there. */
	static boolean available() {
		return look();
	}

	/** A random one of the dances (an index into DANCES). */
	static int pickDance() {
		return RANDOM.nextInt(DANCES.length);
	}

	/** Start dance 'which' (DANCES) on a body. False if it can't. */
	static boolean dance(Avatar body, int which) {
		return play(body, DANCES[which], DANCE_NAMES[which]);
	}

	/** Start one of Emotecraft's emotes, by its uuid, on a body. False if it can't (no Emotecraft, emote not loaded). */
	private static boolean play(Avatar body, UUID id, String name) {
		if (!look()) {
			return false;
		}
		try {
			Object holder = fromUuid.invoke(null, id);
			if (holder == null) {
				MycLink.LOG.info("MyceliumCraft: emotes: Emotecraft has no '{}' emote loaded", name);
				return false;
			}
			play.invoke(body, emoteField.get(holder), 0f, false);
			return true;
		} catch (ReflectiveOperationException | RuntimeException e) {
			MycLink.LOG.warn("MyceliumCraft: emotes: couldn't start '{}'", name, e);
			missing = true;
			return false;
		}
	}

	private static int yourDance = -1;

	/**
	 * End of every client tick: while you dance at the beetle drum (MycState AT_DECORATION: Mycelium's
	 * Bustin Moves on your spore), one of the dances plays on you, picked at random when you start; a
	 * dance that ends while you're still there plays again. When you stop, it stops.
	 */
	static void danceYou(net.minecraft.client.Minecraft minecraft) {
		var player = minecraft.player;
		boolean dancing = player != null && (MyceliumCraftClient.mycFlags() & dev.myceliumcraft.link.Proto.MYC_AT_DECORATION) != 0;
		if (dancing && look()) {
			if (yourDance < 0) {
				yourDance = pickDance();
				if (dance(player, yourDance)) {
					MycLink.LOG.info("MyceliumCraft: emotes: you dance at the beetle drum: Emotecraft's '{}'", DANCE_NAMES[yourDance]);
				}
			} else if (!playing(player)) {
				dance(player, yourDance); // "Over here" doesn't loop
			}
		} else if (!dancing && yourDance >= 0) {
			MycLink.LOG.info("MyceliumCraft: emotes: you stop dancing ('{}' stopped)", DANCE_NAMES[yourDance]);
			yourDance = -1;
			if (player != null) {
				stop(player);
			}
		}
	}

	/** Is an emote still playing on it. */
	static boolean playing(Avatar body) {
		if (!look()) {
			return false;
		}
		try {
			return (boolean) playing.invoke(body);
		} catch (ReflectiveOperationException | RuntimeException e) {
			return false;
		}
	}

	static void stop(Avatar body) {
		if (!look()) {
			return;
		}
		try {
			stop.invoke(body);
		} catch (ReflectiveOperationException | RuntimeException e) {
			// gone already
		}
	}

	private static Method getEmote, currentAnimation;
	private static boolean lastWavingYou;

	/** Is Emotecraft's wave playing on you now (for Mycelium: its spores may wave back). */
	static boolean wavingYou(net.minecraft.client.player.AbstractClientPlayer player) {
		boolean now = false;
		if (player != null && look() && playing(player)) {
			try {
				if (getEmote == null) {
					getEmote = Class.forName("io.github.kosmx.emotes.main.mixinFunctions.IPlayerEntity", true, MycEmotes.class.getClassLoader()).getMethod("emotecraft$getEmote");
					currentAnimation = getEmote.getReturnType().getMethod("getCurrentAnimationInstance");
				}
				Object holder = fromUuid.invoke(null, WAVING);
				Object current = currentAnimation.invoke(getEmote.invoke(player));
				now = holder != null && current != null && current.equals(emoteField.get(holder));
			} catch (ReflectiveOperationException | RuntimeException e) {
				now = false;
			}
		}
		if (now != lastWavingYou) {
			lastWavingYou = now;
			MycLink.LOG.info("MyceliumCraft: emotes: {}", now ? "you're doing Emotecraft's wave: Mycelium's spores in front of you may wave back" : "your wave is over");
		}
		return now;
	}

	private static boolean holding;
	private static float heldYaw;

	/**
	 * End of every client tick: while Emotecraft plays an emote on you, your body and head keep
	 * facing where they did when it started (your request, 2026-11-08). Minecraft turns the body
	 * after your look once it's more than about 50 degrees off, and the spore faces your body (McState
	 * body yaw), so turning the camera turned you. Mycelium's camera follows your look, so it orbits
	 * around you meanwhile.
	 */
	static void holdFacing(net.minecraft.client.Minecraft minecraft) {
		var player = minecraft.player;
		// At a decoration (sitting, lying down) the same: you keep facing the way it turned you.
		boolean decoration = (MyceliumCraftClient.mycFlags() & (dev.myceliumcraft.link.Proto.MYC_SIT | dev.myceliumcraft.link.Proto.MYC_LIE | dev.myceliumcraft.link.Proto.MYC_AT_DECORATION)) != 0;
		boolean emoting = player != null && (playing(player) || decoration);
		if (emoting && !holding) {
			holding = true;
			heldYaw = player.yBodyRot;
			MycLink.LOG.info("MyceliumCraft: emotes: you're {}; you keep facing {} while the camera turns freely", decoration ? "at a decoration" : "doing an emote", String.format("%.0f", heldYaw));
		} else if (!emoting && holding) {
			holding = false;
			MycLink.LOG.info("MyceliumCraft: emotes: your emote or decoration is over; you turn with the camera again");
		}
		if (holding) {
			player.yBodyRot = player.yBodyRotO = heldYaw;
			player.yHeadRot = player.yHeadRotO = heldYaw;
		}
	}

	/** A teleport turned you: the facing you keep is that one now. */
	static void faceTo(float yaw) {
		if (holding) {
			heldYaw = yaw;
			MycLink.LOG.info("MyceliumCraft: emotes: turned to {} (Mycelium's decoration); you keep facing that way", String.format("%.0f", yaw));
		}
	}

	/** One game tick for a body nothing else ticks: what Avatar.tick does for its animations. */
	static void tick(Avatar body) {
		if (!look()) {
			return;
		}
		body.tickCount++;
		try {
			handle.invoke(getManager.invoke(body), 0f, true, false);
		} catch (ReflectiveOperationException | RuntimeException e) {
			MycLink.LOG.warn("MyceliumCraft: emotes: couldn't move the animation on", e);
			missing = true;
		}
	}
}
