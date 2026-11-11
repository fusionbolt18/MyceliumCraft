package dev.myceliumcraft;

import com.mojang.blaze3d.platform.Window;
import dev.myceliumcraft.link.MycLink;
import dev.myceliumcraft.link.Proto;
import net.minecraft.client.Minecraft;

/**
 * Minecraft's window takes the size of Mycelium's (your request, 2026-10-25), so the overlay
 * picture (Minecraft's window, stretched over Mycelium's) has the same shape and size and isn't
 * stretched.
 *
 * Mycelium sends its window's size in pixels (MycState 0x64/0x68). Once it has stayed the same for
 * a moment (not while you drag an edge), Minecraft's window is resized so its picture has that
 * many pixels: Window.setWindowed takes the window's own units, which differ from pixels on a
 * scaled display, so the request is corrected by Minecraft's current pixels-per-unit. Only while
 * Minecraft is in a window (its full screen is left alone), and a size Windows doesn't give is
 * not asked for again until Mycelium's size changes. Render thread.
 */
public final class MycWindowSize {
	private static final long SETTLE_MS = 300; // Mycelium's size unchanged this long before following it

	private static int seenW, seenH;
	private static long seenSinceMs;
	private static int askedW, askedH; // the last target asked for (0: none)
	private static long askedAtMs;
	private static boolean checked = true;
	private static boolean loggedFullscreen;

	private MycWindowSize() {
	}

	private static int fullscreenWas = -1; // Mycelium's full screen as last matched: -1 none yet, 0 window, 1 full screen

	/**
	 * Every frame while linked: Minecraft full screen while Mycelium is, in a window while it isn't
	 * (your request, 2026-11-10). Only when Mycelium's changes (and once on linking), so F11 in
	 * Minecraft still works in between. Minecraft's own full screen setting is used (borderless unless
	 * you turned on its exclusive full screen).
	 */
	public static void matchFullscreen(Minecraft minecraft, boolean linked, boolean myceliumFullscreen) {
		if (!linked) {
			fullscreenWas = -1;
			return;
		}
		int now = myceliumFullscreen ? 1 : 0;
		if (now == fullscreenWas) {
			return;
		}
		fullscreenWas = now;
		boolean mc = minecraft.options.fullscreen().get();
		if (mc == myceliumFullscreen) {
			MycLink.LOG.info("MyceliumCraft: window: Mycelium is {}; Minecraft already is too", myceliumFullscreen ? "full screen" : "in a window");
			return;
		}
		minecraft.options.fullscreen().set(myceliumFullscreen);
		MycLink.LOG.info("MyceliumCraft: window: Mycelium went {}, so Minecraft does too (now {})", myceliumFullscreen ? "full screen" : "into a window",
			minecraft.options.fullscreen().get() ? "full screen" : "in a window");
	}

	/** Every frame: follow Mycelium's window size (w, h in pixels; 0 = not known). */
	public static void follow(Minecraft minecraft, int w, int h) {
		if (w <= 0 || h <= 0) {
			return;
		}
		// The overlay can't carry more than this; keep the shape within it.
		if (w > Proto.MAX_OVERLAY_W || h > Proto.MAX_OVERLAY_H) {
			double k = Math.min((double) Proto.MAX_OVERLAY_W / w, (double) Proto.MAX_OVERLAY_H / h);
			w = (int) Math.floor(w * k);
			h = (int) Math.floor(h * k);
		}
		long now = System.currentTimeMillis();
		if (w != seenW || h != seenH) {
			seenW = w;
			seenH = h;
			seenSinceMs = now;
			return;
		}
		if (now - seenSinceMs < SETTLE_MS) {
			return;
		}
		Window window = minecraft.getWindow();
		int pixelsW = window.getScreenWidth(), pixelsH = window.getScreenHeight();
		if (!checked && now - askedAtMs > 500) {
			checked = true;
			MycLink.LOG.info("MyceliumCraft: window: asked for {}x{} pixels (Mycelium's), got {}x{}{}", askedW, askedH, pixelsW, pixelsH,
				pixelsW == askedW && pixelsH == askedH ? "" : " (Windows didn't give that size; not asking again until Mycelium's changes)");
			if (pixelsW == askedW && pixelsH == askedH) {
				askedW = askedH = 0; // got it: should it change later (you resize Minecraft's), it's put back
			}
		}
		if (pixelsW == w && pixelsH == h) {
			return;
		}
		if (askedW == w && askedH == h) {
			return; // asked already; Windows gave something else
		}
		if (minecraft.options.fullscreen().get()) {
			if (!loggedFullscreen) {
				loggedFullscreen = true;
				MycLink.LOG.info("MyceliumCraft: window: Minecraft is full screen, so it keeps its size (Mycelium's is {}x{})", w, h);
			}
			return;
		}
		loggedFullscreen = false;
		double perUnitW = window.getWidth() > 0 ? (double) pixelsW / window.getWidth() : 1.0;
		double perUnitH = window.getHeight() > 0 ? (double) pixelsH / window.getHeight() : 1.0;
		int unitsW = (int) Math.round(w / (perUnitW > 0 ? perUnitW : 1.0));
		int unitsH = (int) Math.round(h / (perUnitH > 0 ? perUnitH : 1.0));
		MycLink.LOG.info("MyceliumCraft: window: Mycelium's is {}x{} pixels, Minecraft's {}x{}; resizing Minecraft's to {}x{} (window units)", w, h, pixelsW, pixelsH, unitsW, unitsH);
		window.setWindowed(unitsW, unitsH);
		askedW = w;
		askedH = h;
		askedAtMs = now;
		checked = false;
	}
}
