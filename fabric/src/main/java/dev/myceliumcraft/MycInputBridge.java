package dev.myceliumcraft;

import dev.myceliumcraft.link.MycLink;
import dev.myceliumcraft.link.Proto;
import net.minecraft.client.Minecraft;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.input.MouseButtonInfo;
import org.lwjgl.sdl.SDLKeyboard;

/**
 * The input bridge, Minecraft side (like SkyCraft's InputBridge): while Mycelium's window has the
 * focus and it's normal play, replay Mycelium's keyboard and mouse into Minecraft's own input
 * handlers, as if Minecraft's window had the focus.
 *
 * Mycelium sends the input as *state* (keys and buttons held, running mouse totals); this compares
 * it with what it last applied and replays only the changes, at the start of every frame.
 * While active, the mixins make Minecraft believe its window is focused and active, keep it off the
 * real cursor, and answer isKeyDown() from these keys. Mouse look then goes through Minecraft's own
 * turning (sensitivity, smooth camera, spyglass all apply), and clicks are ordinary attack/use.
 */
public final class MycInputBridge {
	private static final boolean[] KEYS = new boolean[Proto.IN_KEY_BYTES * 8];
	private static final boolean[] BUTTONS = new boolean[6];
	private static final MycLink.MycInput IN = new MycLink.MycInput();
	private static volatile boolean active;
	private static int lastReason = -2;
	private static double lastDX, lastDY, lastScroll, virtualX, virtualY;
	private static int lastText;
	private static int modifiers;
	private static Boolean savedPauseOnLostFocus;
	private static long lastLogMs;
	private static int keyEvents, buttonEvents;
	private static double moved;

	private MycInputBridge() {
	}

	/** True while Minecraft plays from Mycelium's input. Cheap; read by the mixins. */
	public static boolean active() {
		return active;
	}

	public static boolean isKeyDown(int scancode) {
		return scancode >= 0 && scancode < KEYS.length && KEYS[scancode];
	}

	/** Start of every Minecraft frame (Minecraft.runTick HEAD). */
	public static void beginFrame(Minecraft minecraft) {
		MycLink.poll();
		boolean read = MycLink.mapped() && MycLink.myceliumAlive() && MycLink.readInput(IN);
		boolean nowActive = read && IN.active();
		int reason = read ? IN.reason : -1;
		if (reason != lastReason) {
			lastReason = reason;
			MycLink.LOG.info("MyceliumCraft: input bridge {}", reason == -1 ? "off (no Mycelium)"
				: reason == 0 ? "ON: playing from Mycelium's keyboard and mouse"
				: "off (" + (reason > 0 && reason < Proto.IN_REASONS.length ? Proto.IN_REASONS[reason] : "reason " + reason) + ")");
		}
		if (nowActive != active) {
			if (nowActive) {
				// Start from Mycelium's current totals, so nothing done while off is replayed.
				lastDX = IN.totalDX;
				lastDY = IN.totalDY;
				lastScroll = IN.totalScroll;
				lastText = IN.textCount;
				// Mycelium's window has the real focus now; don't let Minecraft pause over it.
				savedPauseOnLostFocus = minecraft.options.pauseOnLostFocus;
				minecraft.options.pauseOnLostFocus = false;
				active = true;
			} else {
				releaseAll(minecraft);
				active = false;
				if (savedPauseOnLostFocus != null) {
					minecraft.options.pauseOnLostFocus = savedPauseOnLostFocus;
					savedPauseOnLostFocus = null;
				}
				if (minecraft.mouseHandler.isMouseGrabbed()) {
					minecraft.mouseHandler.releaseMouse();
				}
			}
		}
		if (!active) {
			return;
		}

		long handle = minecraft.getWindow().handle();
		// Minecraft only turns the player from mouse movement while it has "grabbed" the mouse.
		// Grab it the normal way (the real cursor stays with Mycelium; see InputConstantsMixin).
		if (minecraft.gui.screen() == null && !minecraft.mouseHandler.isMouseGrabbed()) {
			minecraft.mouseHandler.grabMouse();
		}

		for (int sc = 1; sc < KEYS.length; sc++) {
			boolean down = IN.key(sc);
			if (down != KEYS[sc]) {
				key(minecraft, handle, sc, down);
			}
		}
		for (int button = 1; button < BUTTONS.length; button++) {
			boolean down = (IN.buttons & (1 << button)) != 0;
			if (down != BUTTONS[button]) {
				BUTTONS[button] = down;
				buttonEvents++;
				minecraft.mouseHandler.onButton(handle, new MouseButtonInfo(button, modifiers), down ? 1 : 0);
			}
		}

		double dx = IN.totalDX - lastDX, dy = IN.totalDY - lastDY;
		lastDX = IN.totalDX;
		lastDY = IN.totalDY;
		if (dx != 0 || dy != 0) {
			virtualX += dx;
			virtualY += dy;
			moved += Math.abs(dx) + Math.abs(dy);
			minecraft.mouseHandler.onMove(handle, virtualX, virtualY, dx, dy);
		}
		double scroll = IN.totalScroll - lastScroll;
		lastScroll = IN.totalScroll;
		if (scroll != 0) {
			minecraft.mouseHandler.onScroll(handle, 0.0, scroll);
		}

		// Typed characters, for chat and text fields (only when a Minecraft screen is open).
		if (IN.textCount - lastText > Proto.IN_TEXT_RING) {
			lastText = IN.textCount - Proto.IN_TEXT_RING;
		}
		while (lastText != IN.textCount) {
			int c = IN.text[Integer.remainderUnsigned(lastText, Proto.IN_TEXT_RING)];
			lastText++;
			if (minecraft.gui.screen() != null && c > 0) {
				minecraft.keyboardHandler.textInput(handle, new String(Character.toChars(c)));
			}
		}

		if (keyEvents + buttonEvents > 0 || dx != 0 || dy != 0 || scroll != 0) {
			// Real input resets Minecraft's "inactive" timer; bridged input should too.
			minecraft.getFramerateLimitTracker().onInputReceived();
		}

		long now = System.currentTimeMillis();
		if (now - lastLogMs >= 1000) {
			lastLogMs = now;
			int held = 0;
			for (boolean k : KEYS) {
				if (k) {
					held++;
				}
			}
			MycLink.LOG.info("MyceliumCraft: input bridge: {} keys held, {} key and {} button changes, mouse moved {} px in the last second; grabbed={} screen={}",
				held, keyEvents, buttonEvents, String.format("%.0f", moved), minecraft.mouseHandler.isMouseGrabbed(), minecraft.gui.screen() == null ? "none" : minecraft.gui.screen().getClass().getSimpleName());
			keyEvents = 0;
			buttonEvents = 0;
			moved = 0;
		}
	}

	private static void key(Minecraft minecraft, long handle, int scancode, boolean down) {
		KEYS[scancode] = down;
		updateModifiers();
		keyEvents++;
		int keycode = SDLKeyboard.SDL_GetKeyFromScancode(scancode, (short) modifiers, true);
		minecraft.keyboardHandler.keyPress(handle, down ? 1 : 0, new KeyEvent(scancode, keycode, modifiers));
	}

	private static void updateModifiers() {
		int m = 0;
		if (KEYS[225]) m |= 0x0001; // SDL_KMOD_LSHIFT
		if (KEYS[229]) m |= 0x0002; // SDL_KMOD_RSHIFT
		if (KEYS[224]) m |= 0x0040; // SDL_KMOD_LCTRL
		if (KEYS[228]) m |= 0x0080; // SDL_KMOD_RCTRL
		if (KEYS[226]) m |= 0x0100; // SDL_KMOD_LALT
		if (KEYS[230]) m |= 0x0200; // SDL_KMOD_RALT
		modifiers = m;
	}

	/** Lift every key and button we think is held (bridge turned off, Mycelium menu, focus moved). */
	private static void releaseAll(Minecraft minecraft) {
		long handle = minecraft.getWindow().handle();
		for (int sc = 0; sc < KEYS.length; sc++) {
			if (KEYS[sc]) {
				KEYS[sc] = false;
				updateModifiers();
				minecraft.keyboardHandler.keyPress(handle, 0, new KeyEvent(sc, SDLKeyboard.SDL_GetKeyFromScancode(sc, (short) 0, true), modifiers));
			}
		}
		for (int button = 1; button < BUTTONS.length; button++) {
			if (BUTTONS[button]) {
				BUTTONS[button] = false;
				minecraft.mouseHandler.onButton(handle, new MouseButtonInfo(button, 0), 0);
			}
		}
	}
}
