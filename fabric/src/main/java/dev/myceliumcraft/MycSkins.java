package dev.myceliumcraft;

import com.mojang.blaze3d.platform.NativeImage;
import dev.myceliumcraft.link.MycLink;
import java.awt.Color;
import java.io.IOException;
import java.util.HashMap;
import java.util.Map;
import java.util.UUID;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.texture.DynamicTexture;
import net.minecraft.core.ClientAsset;
import net.minecraft.resources.Identifier;
import net.minecraft.world.entity.player.PlayerModelType;
import net.minecraft.world.entity.player.PlayerSkin;
import org.jspecify.annotations.Nullable;

/**
 * A Minecraft skin for each spore, in its colours (your request, 2026-11-03). Your two pictures
 * (dist/skin, built into the jar) are coloured and put together as one 64x64 skin; they don't share
 * any pixels. spore_cap is in colour: every coloured pixel's hue is turned and its brightness scaled
 * by the same amounts, so that the picture's main colour takes the hue and brightness of the cap
 * (DesignTracker.capColor). Its strength of colour and its white spots stay as you drew them.
 * spore_body is grey, and is multiplied by the body colour (bodyColor). Each pair of colours is made once and kept as its own
 * texture, so a spore that changes colour gets a new one.
 */
public final class MycSkins {
	private static final float COLOURED = 0.25f; // strength of colour from which a pixel counts as coloured, not a white spot
	private static final Map<Long, PlayerSkin> SKINS = new HashMap<>();
	private static NativeImage bodyPart, capPart;
	private static float[] capMain;
	private static boolean failed;
	private static volatile UUID you;
	private static volatile PlayerSkin yours;

	private MycSkins() {
	}

	/** The skin for these colours (0xRRGGBB), made the first time. Null if your pictures can't be read. Render thread only. */
	public static @Nullable PlayerSkin skin(int cap, int body) {
		cap &= 0xFFFFFF;
		body &= 0xFFFFFF;
		long key = ((long) cap << 24) | body;
		PlayerSkin known = SKINS.get(key);
		if (known != null) {
			return known;
		}
		if (failed || !loadParts()) {
			return null;
		}
		int w = bodyPart.getWidth(), h = bodyPart.getHeight();
		float[] wanted = hsb(cap);
		float turn = wanted[0] - capMain[0], brighten = wanted[2] / capMain[2];
		NativeImage image = new NativeImage(w, h, false);
		int capPixels = 0, bodyPixels = 0;
		for (int y = 0; y < h; y++) {
			for (int x = 0; x < w; x++) {
				int c = capPart.getPixel(x, y), b = bodyPart.getPixel(x, y);
				if ((c >>> 24) != 0) {
					capPixels++;
				} else if ((b >>> 24) != 0) {
					bodyPixels++;
				}
				image.setPixel(x, y, (c >>> 24) != 0 ? recolour(c, turn, brighten) : tint(b, body));
			}
		}
		String name = String.format("spore_skin/%06x_%06x", cap, body);
		Identifier id = Identifier.fromNamespaceAndPath("myceliumcraft", name);
		Minecraft.getInstance().getTextureManager().register(id, new DynamicTexture(() -> name, image));
		PlayerSkin skin = new PlayerSkin(new ClientAsset.ResourceTexture(id, id), null, null, PlayerModelType.WIDE, true);
		SKINS.put(key, skin);
		int shown = Color.HSBtoRGB(capMain[0] + turn, capMain[1], Math.min(1f, capMain[2] * brighten)) & 0xFFFFFF;
		MycLink.LOG.info("MyceliumCraft: skin: made {} ({}x{}: cap #{} on {} pixels, its main colour turned to #{}; body #{} on {})", id, w, h,
			String.format("%06X", cap), capPixels, String.format("%06X", shown), String.format("%06X", body), bodyPixels);
		return skin;
	}

	private static float[] hsb(int rgb) {
		return Color.RGBtoHSB((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF, null);
	}

	/** The same pixel with its hue turned (1 = all the way round) and, if clearly coloured, its brightness scaled: the white spots stay white. */
	private static int recolour(int argb, float turn, float brighten) {
		float[] c = hsb(argb);
		float brightness = c[1] >= COLOURED ? Math.min(1f, c[2] * brighten) : c[2];
		return (argb & 0xFF000000) | (Color.HSBtoRGB(c[0] + turn, c[1], brightness) & 0xFFFFFF);
	}

	/** Hue, strength and brightness of a picture's commonest clearly coloured pixel (the white spots don't count). */
	private static float[] mainColour(NativeImage image) {
		Map<Integer, Integer> counts = new HashMap<>();
		int best = 0, bestCount = 0;
		for (int y = 0; y < image.getHeight(); y++) {
			for (int x = 0; x < image.getWidth(); x++) {
				int argb = image.getPixel(x, y);
				if ((argb >>> 24) != 0 && hsb(argb)[1] >= COLOURED) {
					int count = counts.merge(argb & 0xFFFFFF, 1, Integer::sum);
					if (count > bestCount) {
						bestCount = count;
						best = argb & 0xFFFFFF;
					}
				}
			}
		}
		MycLink.LOG.info("MyceliumCraft: skin: spore_cap's main colour is #{} ({} pixels)", String.format("%06X", best), bestCount);
		return hsb(best);
	}

	/** Grey times colour, channel by channel; see-through stays see-through. */
	private static int tint(int argb, int rgb) {
		int r = (((argb >> 16) & 0xFF) * ((rgb >> 16) & 0xFF) + 127) / 255;
		int g = (((argb >> 8) & 0xFF) * ((rgb >> 8) & 0xFF) + 127) / 255;
		int b = ((argb & 0xFF) * (rgb & 0xFF) + 127) / 255;
		return (argb & 0xFF000000) | (r << 16) | (g << 8) | b;
	}

	private static boolean loadParts() {
		if (bodyPart != null) {
			return true;
		}
		NativeImage body = read("spore_body"), cap = read("spore_cap");
		if (body == null || cap == null || body.getWidth() != cap.getWidth() || body.getHeight() != cap.getHeight()) {
			failed = true;
			MycLink.LOG.warn("MyceliumCraft: skin: your two skin pictures are missing or not the same size; spores keep Minecraft's skins");
			return false;
		}
		bodyPart = body;
		capPart = cap;
		capMain = mainColour(cap);
		return true;
	}

	private static @Nullable NativeImage read(String part) {
		Identifier id = Identifier.fromNamespaceAndPath("myceliumcraft", "textures/spore_skin/" + part + ".png");
		var resource = Minecraft.getInstance().getResourceManager().getResource(id);
		if (resource.isEmpty()) {
			return null;
		}
		try (var in = resource.get().open()) {
			return NativeImage.read(in);
		} catch (IOException e) {
			MycLink.LOG.warn("MyceliumCraft: skin: couldn't read {}", id, e);
			return null;
		}
	}

	/** Every frame: your player's id and the skin of the spore you control (nulls: your own Minecraft skin). */
	public static void setYours(@Nullable UUID player, @Nullable PlayerSkin skin) {
		if (skin != yours) {
			MycLink.LOG.info("MyceliumCraft: skin: your player wears {}", skin != null ? skin.body().texturePath() : "its Minecraft skin again");
		}
		you = player;
		yours = skin;
	}

	/** The spore's skin for this player, or null to keep Minecraft's. */
	public static @Nullable PlayerSkin forPlayer(UUID id) {
		PlayerSkin skin = yours;
		return skin != null && id != null && id.equals(you) ? skin : null;
	}
}
