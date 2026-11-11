package dev.myceliumcraft;

import com.mojang.blaze3d.pipeline.RenderTarget;
import com.mojang.blaze3d.systems.RenderSystem;
import com.mojang.renderpearl.api.buffers.GpuBuffer;
import com.mojang.renderpearl.api.buffers.GpuBufferSlice;
import com.mojang.renderpearl.api.textures.GpuTexture;
import dev.myceliumcraft.link.MycLink;
import dev.myceliumcraft.link.Proto;
import java.lang.foreign.MemorySegment;
import net.minecraft.client.Minecraft;

/**
 * The overlay, Minecraft side. While Mycelium shows a level and asks for it, Minecraft skips its
 * own world (LevelRendererMixin), so its picture is the hand, HUD and any open screen on a
 * transparent background. That picture is copied back from the graphics card after every frame
 * and published to Mycelium (Overlay block in protocol/PROTOCOL.md), which draws it on top.
 *
 * Adapted from SkyCraft's FrameExporter (MIT, chasmlol). The copy is asynchronous: a frame goes
 * into one of 3 staging buffers and is shipped once the graphics card has finished copying it,
 * usually a frame later.
 */
public final class MycOverlay {
	private static final int STAGING = 3;
	private static final int FREE = 0;
	private static final int PENDING = 1;
	private static final int READY = 2;

	private static final Staging[] staging = new Staging[STAGING];
	private static volatile boolean active;
	private static long nextFrameId = 1;
	private static boolean loggedFormat;
	private static long lastLogMs;
	private static int shipped, skipped, tooBig;
	// Blip watch (your report, 2026-11-10): slow Minecraft frames, logged only when they happen.
	private static long lastCaptureNs;
	private static long lastSlowLogMs;

	private static final class Staging {
		GpuBuffer buffer;
		int width;
		int height;
		volatile int state = FREE;
		long frameId;
	}

	private MycOverlay() {
	}

	/** True while Minecraft draws only its overlay (no world). Read by LevelRendererMixin. */
	public static boolean active() {
		return active;
	}

	/** Once a frame: should the overlay be on (Mycelium alive, in a level, asking for it, and we're in a world)? */
	public static void setActive(boolean on) {
		if (on != active) {
			active = on;
			MycLink.LOG.info("MyceliumCraft: overlay {}", on ? "ON: Minecraft draws only its hand, HUD and screens, for Mycelium" : "off: Minecraft draws its own world");
		}
	}

	/** Right after GameRenderer.render(): copy this frame's picture, and ship any finished copy. */
	public static void capture(Minecraft minecraft) {
		if (!active || MycLink.segment() == null) {
			return;
		}
		shipReadyFrames();
		long nowNs = System.nanoTime();
		if (lastCaptureNs != 0 && nowNs - lastCaptureNs > 100_000_000L && System.currentTimeMillis() - lastSlowLogMs > 500) {
			lastSlowLogMs = System.currentTimeMillis();
			MycLink.LOG.info("MyceliumCraft: overlay blip: Minecraft's frame took {} ms", (nowNs - lastCaptureNs) / 1_000_000L);
		}
		lastCaptureNs = nowNs;

		RenderTarget target = minecraft.gameRenderer.mainRenderTarget();
		GpuTexture color = target.getColorTexture();
		if (color == null) {
			return;
		}
		int width = target.width;
		int height = target.height;
		if (width > Proto.MAX_OVERLAY_W || height > Proto.MAX_OVERLAY_H) {
			tooBig++;
			return;
		}
		if (!loggedFormat) {
			loggedFormat = true;
			MycLink.LOG.info("MyceliumCraft: overlay picture {}x{}, format {}", width, height, color.getFormat());
		}

		Staging slot = null;
		for (int i = 0; i < STAGING && slot == null; i++) {
			if (staging[i] == null) {
				staging[i] = new Staging();
			}
			if (staging[i].state == FREE) {
				slot = staging[i];
			}
		}
		if (slot == null) {
			skipped++; // all copies still in flight
			return;
		}
		long bytes = (long) width * height * 4L;
		if (slot.buffer == null || slot.width != width || slot.height != height) {
			if (slot.buffer != null) {
				slot.buffer.close();
			}
			// 9 = GpuBuffer usage MAP_READ | COPY_DST (as SkyCraft)
			slot.buffer = RenderSystem.getDevice().createBuffer(() -> "MyceliumCraft overlay readback", 9, bytes);
			slot.width = width;
			slot.height = height;
		}
		final Staging captured = slot;
		captured.state = PENDING;
		captured.frameId = nextFrameId++;
		RenderSystem.getDevice().createCommandEncoder().copyTextureToBuffer(color, captured.buffer, 0L, () -> captured.state = READY, 0);

		long now = System.currentTimeMillis();
		if (now - lastLogMs >= 1000) {
			lastLogMs = now;
			MycLink.LOG.info("MyceliumCraft: overlay: {} pictures sent in the last second ({}x{}), {} skipped (copies still busy){}",
				shipped, width, height, skipped, tooBig > 0 ? ", " + tooBig + " too big (over 3840x2160)" : "");
			shipped = 0;
			skipped = 0;
			tooBig = 0;
		}
	}

	/** Copy the newest finished picture into shared memory. */
	private static void shipReadyFrames() {
		Staging newest = null;
		for (Staging s : staging) {
			if (s != null && s.state == READY && (newest == null || s.frameId > newest.frameId)) {
				newest = s;
			}
		}
		if (newest == null) {
			return;
		}
		MemorySegment shm = MycLink.segment();
		if (shm != null) {
			long bytes = (long) newest.width * newest.height * 4L;
			try (GpuBufferSlice.MappedView view = newest.buffer.map(true, false)) {
				MemorySegment src = MemorySegment.ofBuffer(view.data());
				MemorySegment.copy(src, 0, shm, MycLink.overlayBackSlotOffset(), Math.min(bytes, src.byteSize()));
			}
			MycLink.publishOverlay(newest.width, newest.height, true, newest.frameId);
			shipped++;
		}
		for (Staging s : staging) {
			if (s != null && s.state == READY && s.frameId <= newest.frameId) {
				s.state = FREE;
			}
		}
	}
}
