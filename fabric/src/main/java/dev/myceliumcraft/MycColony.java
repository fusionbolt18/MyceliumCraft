package dev.myceliumcraft;

import dev.myceliumcraft.link.MycLink;
import net.minecraft.ChatFormatting;
import com.mojang.authlib.GameProfile;
import dev.myceliumcraft.mixin.PlayerInfoAccessor;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;
import java.util.UUID;
import net.minecraft.client.Minecraft;
import net.minecraft.client.multiplayer.PlayerInfo;
import net.minecraft.world.entity.player.PlayerSkin;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.MutableComponent;
import net.minecraft.network.chat.Style;

/**
 * Your Mycelium colony in the player list, instead of Mycelium's colony meter (your requests,
 * 2026-11-03; Colony block, protocol v42; ColonyWatch.cs).
 * - The other spores are rows like other players' (PlayerTabRowsMixin): a PlayerInfo each, with a
 *   made-up id from its name and its own skin in its colours (MycSkins, so nothing is asked of
 *   Mojang's skin servers). You are your own row already (MycName), in your spore's skin.
 * - The footer: a bar of text for the colony's average happiness that fills up or down toward
 *   it, as Mycelium's meter does, and Mycelium's word for it (Happy over half, Unhappy under,
 *   Content at half: where its stat bonus is above, below or at none). During a run, one line
 *   instead: Mycelium's nutrient bonus, which it shows as "+50%" (your request, 2026-11-04).
 */
public final class MycColony {
	private static final int SEGMENTS = 40;
	/** The bar moves this much of its length per second toward the colony's happiness. */
	private static final float FILL_PER_SECOND = 0.6f;
	private static final int HAPPY = 0x38DB35, CONTENT = 0xFFD700, UNHAPPY = 0xC83434;

	// As last read well; swapped with SCRATCH after a good read, so a failed read leaves nothing half-copied.
	private static MycLink.Colony COLONY = new MycLink.Colony();
	private static MycLink.Colony SCRATCH = new MycLink.Colony();
	/** A failed read keeps the last good colony this long (your report, 2026-11-10: blips at 22:48 and 22:50). */
	private static final long KEEP_LAST_GOOD_MS = 250;
	private static long lastGoodMs;
	private static int failedReads;
	private static long nextFailLogMs;
	private static float shown = -1f;
	private static long lastNanos;
	private static String lastList = "";
	private static int lastFilled = -1;
	private static boolean footerSet;
	private static List<PlayerInfo> rows = List.of();
	private static String rowsKey = "";
	private static boolean present;

	private MycColony() {
	}

	/** Every frame. active: Mycelium has a spore in a level. */
	public static void update(Minecraft minecraft, boolean active) {
		long now = System.nanoTime();
		float dt = lastNanos == 0 ? 0f : Math.min(0.25f, (now - lastNanos) / 1e9f);
		lastNanos = now;
		boolean have = active && readColony() && COLONY.count > 0 && COLONY.average >= 0f;
		present = have;
		if (!have) {
			if (footerSet) {
				minecraft.gui.hud.getTabList().setFooter(null);
				MycSkins.setYours(null, null);
				footerSet = false;
				rows = List.of();
				rowsKey = "";
				shown = -1f;
				lastList = "";
				lastFilled = -1;
				MycLink.LOG.info("MyceliumCraft: colony: no colony, player list footer cleared");
			}
			return;
		}
		boolean haveCurrent = COLONY.current >= 0 && COLONY.current < COLONY.count && minecraft.player != null;
		MycSkins.setYours(haveCurrent ? minecraft.player.getUUID() : null,
			haveCurrent ? MycSkins.skin(COLONY.caps[COLONY.current], COLONY.colors[COLONY.current]) : null);
		float target = Math.max(0f, Math.min(1f, COLONY.average));
		if (shown < 0f) {
			shown = target; // first time: where it is, no fill from empty
		} else {
			float step = FILL_PER_SECOND * dt;
			shown = Math.abs(target - shown) <= step ? target : shown + Math.signum(target - shown) * step;
		}
		int filled = Math.round(shown * SEGMENTS);
		boolean inRun = COLONY.nutrientBonus >= 0;
		StringBuilder key = new StringBuilder().append(COLONY.current).append('|').append(COLONY.nutrientBonus).append('|').append(inRun ? "" : word(target));
		for (int i = 0; i < COLONY.count; i++) {
			key.append('|').append(COLONY.names[i]).append('#').append(COLONY.colors[i]).append('#').append(COLONY.caps[i]);
		}
		String list = key.toString();
		boolean listChanged = !list.equals(lastList);
		if (listChanged) {
			updateRows();
		}
		if (!listChanged && (inRun || filled == lastFilled)) {
			return; // nothing changed on screen
		}
		lastList = list;
		lastFilled = filled;
		minecraft.gui.hud.getTabList().setFooter(inRun ? runFooter(COLONY.nutrientBonus) : footer(filled, target));
		footerSet = true;
		if (listChanged) {
			MycLink.LOG.info("MyceliumCraft: colony: {} spore(s), {} other row(s) in the player list, average happiness {} ({}), footer {}",
				COLONY.count, rows.size(), String.format("%.2f", target), word(target), inRun ? "Nutrient Multiplier: +" + COLONY.nutrientBonus + "%" : "bar " + filled + "/" + SEGMENTS);
		}
	}

	/**
	 * Read Mycelium's colony. A read that fails (Mycelium mid-write the whole time: it's a big block)
	 * keeps the last good colony for up to 0.25 s. Before, it counted as "no colony" for that frame:
	 * your skin went back to Minecraft's and every spore body vanished (22:48:11, 22:49:57 and 22:50:00
	 * on 2026-11-10, while Mycelium's own log had 13 spores throughout).
	 */
	private static boolean readColony() {
		long now = System.currentTimeMillis();
		if (MycLink.readColony(SCRATCH)) {
			MycLink.Colony t = COLONY;
			COLONY = SCRATCH;
			SCRATCH = t;
			lastGoodMs = now;
		} else if (lastGoodMs > 0 && now - lastGoodMs <= KEEP_LAST_GOOD_MS) {
			failedReads++;
		} else {
			return false;
		}
		if (failedReads > 0 && now >= nextFailLogMs) {
			nextFailLogMs = now + 1000;
			MycLink.LOG.info("MyceliumCraft: link: {} read(s) of Mycelium's colony failed (Mycelium mid-write); the last good one was kept", failedReads);
			failedReads = 0;
		}
		return true;
	}

	/** Your colony as last read, or null when there is none. */
	public static MycLink.Colony colony() {
		return present ? COLONY : null;
	}

	/** The other spores' rows for the player list (not the one you control: that's you). */
	public static List<PlayerInfo> otherSporeRows() {
		return rows;
	}

	/** Rows for the other spores, made again only when the colony changes (a row keeps its id and skin). */
	private static void updateRows() {
		StringBuilder key = new StringBuilder();
		for (int i = 0; i < COLONY.count; i++) {
			if (i != COLONY.current) {
				key.append(i).append(':').append(COLONY.names[i]).append('#').append(COLONY.colors[i]).append('#').append(COLONY.caps[i]).append('|');
			}
		}
		if (key.toString().equals(rowsKey)) {
			return;
		}
		rowsKey = key.toString();
		List<PlayerInfo> made = new ArrayList<>();
		for (int i = 0; i < COLONY.count; i++) {
			if (i == COLONY.current) {
				continue;
			}
			String name = COLONY.names[i].isEmpty() ? "Spore" : COLONY.names[i];
			if (name.length() > 16) {
				name = name.substring(0, 16); // Minecraft's longest player name
			}
			UUID id = UUID.nameUUIDFromBytes(("myceliumcraft-spore:" + name).getBytes(StandardCharsets.UTF_8));
			PlayerInfo info = new PlayerInfo(new GameProfile(id, name), false);
			PlayerSkin skin = MycSkins.skin(COLONY.caps[i], COLONY.colors[i]);
			if (skin != null) {
				((PlayerInfoAccessor) info).myceliumcraft$setSkinLookup(() -> skin);
			}
			info.setShowHat(true); // the cap's outer layer, on the list's little head
			made.add(info);
		}
		rows = List.copyOf(made);
	}

	private static Component runFooter(int nutrientBonus) {
		return Component.literal("Nutrient Multiplier: +" + nutrientBonus + "%").withStyle(ChatFormatting.GRAY);
	}

	private static Component footer(int filled, float happiness) {
		int color = colorOf(happiness);
		MutableComponent out = Component.literal("Colony Happiness").withStyle(ChatFormatting.GRAY);
		out.append(Component.literal("\n"));
		out.append(Component.literal("[").withStyle(ChatFormatting.DARK_GRAY));
		out.append(Component.literal("|".repeat(filled)).withStyle(Style.EMPTY.withColor(color)));
		out.append(Component.literal("|".repeat(SEGMENTS - filled)).withStyle(ChatFormatting.DARK_GRAY));
		out.append(Component.literal("]").withStyle(ChatFormatting.DARK_GRAY));
		out.append(Component.literal("\n")).append(Component.literal(word(happiness)).withStyle(Style.EMPTY.withColor(color)));
		return out;
	}

	private static String word(float happiness) {
		return happiness > 0.505f ? "Happy" : happiness < 0.495f ? "Unhappy" : "Content";
	}

	private static int colorOf(float happiness) {
		return happiness > 0.505f ? HAPPY : happiness < 0.495f ? UNHAPPY : CONTENT;
	}
}
