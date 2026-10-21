package dev.myceliumcraft;

import dev.myceliumcraft.link.MycLink;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;

/**
 * Finds what stalls Minecraft's (integrated) server. Your Mycelium enemies took damage late at
 * times, and Minecraft's log had "Can't keep up! Running 2028ms or 40 ticks behind" (2026-10-21):
 * hits are handled by the server, which had frozen for ~2 s while drawing went on at 120 fps. The
 * cause isn't in the logs, so: every server tick is timed, and while one runs longer than 0.4 s a
 * watcher thread logs what the server thread is doing (its stack: the chain of code it's in),
 * again at 1.5 s; when it ends, how long it took. A diagnostic: it changes nothing.
 */
public final class ServerStallWatch {
	private static final long FIRST_LOOK_MS = 400, SECOND_LOOK_MS = 1500, REPORT_MS = 250;
	private static volatile Thread serverThread;
	private static volatile long tickStart; // System.nanoTime() at the start of the running tick; 0 = none
	private static volatile long tickNumber;
	private static long lookedTick = -1;
	private static int looks;

	private ServerStallWatch() {
	}

	public static void init() {
		ServerTickEvents.START_SERVER_TICK.register(server -> {
			serverThread = Thread.currentThread();
			tickNumber++;
			tickStart = System.nanoTime();
		});
		ServerTickEvents.END_SERVER_TICK.register(server -> {
			long start = tickStart;
			tickStart = 0;
			long ms = (System.nanoTime() - start) / 1_000_000;
			if (start != 0 && ms >= REPORT_MS) {
				MycLink.LOG.info("MyceliumCraft: server stall: tick {} took {} ms", tickNumber, ms);
			}
		});
		Thread watcher = new Thread(ServerStallWatch::watch, "MyceliumCraft server stall watch");
		watcher.setDaemon(true);
		watcher.start();
	}

	private static void watch() {
		while (true) {
			try {
				Thread.sleep(100);
			} catch (InterruptedException e) {
				return;
			}
			long start = tickStart;
			Thread t = serverThread;
			if (start == 0 || t == null) {
				continue;
			}
			long ms = (System.nanoTime() - start) / 1_000_000;
			long tick = tickNumber;
			if (tick != lookedTick) {
				lookedTick = tick;
				looks = 0;
			}
			if ((looks == 0 && ms >= FIRST_LOOK_MS) || (looks == 1 && ms >= SECOND_LOOK_MS)) {
				looks++;
				StringBuilder sb = new StringBuilder();
				StackTraceElement[] stack = t.getStackTrace();
				for (int i = 0; i < Math.min(stack.length, 40); i++) {
					sb.append("\n    at ").append(stack[i]);
				}
				MycLink.LOG.info("MyceliumCraft: server stall: tick {} has run {} ms so far; the server thread is in:{}", tick, ms, sb);
			}
		}
	}
}
