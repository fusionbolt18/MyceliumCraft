package dev.myceliumcraft.link;

import java.util.ArrayList;
import java.util.List;

/**
 * Pretends to be Minecraft, for testing the link without starting the game. Run tools\FakeMycelium
 * first. It checks the ground grid FakeMycelium publishes (a step from y 10 to y 10.5 at x = 0, one
 * hole, and a tunnel: roof from y 13 to 14 over x -22..-17), confirms the teleport, then walks 10
 * blocks east and 10 blocks north from there.
 */
public final class LinkTestMain {
	public static void main(String[] args) throws InterruptedException {
		long deadline = System.currentTimeMillis() + 15000;
		MycLink.MycState myc = new MycLink.MycState();
		GroundGrid grid = null;
		while (grid == null || !MycLink.readMycState(myc) || myc.teleportSeq == 0) {
			MycLink.poll();
			// Like the real mod: say "in a world" every frame, or Mycelium never starts scanning.
			MycLink.writeMcState(Proto.MC_IN_WORLD | Proto.MC_ON_GROUND, 0, -60, 0, 0f, 0f, 0, 1, 0);
			grid = MycLink.readGround(grid);
			if (System.currentTimeMillis() > deadline) {
				System.out.println("FAIL: no Mycelium ground grid / teleport after 15 s");
				System.exit(1);
			}
			Thread.sleep(50);
		}
		System.out.printf("linked; grid v%d %dx%d cell %.2f origin (%.1f, %.1f); teleport %d to (%.2f, %.2f, %.2f) yaw %.0f%n",
			grid.version, grid.n, grid.n, grid.cell, grid.originX, grid.originZ, myc.teleportSeq, myc.tpX, myc.tpY, myc.tpZ, myc.tpYaw);

		boolean ok = true;
		ok &= check("ground west of the step", grid.groundAt(-5, 10, 0), 10.0);
		ok &= check("ground east of the step", grid.groundAt(5, 10.5, 0), 10.5);
		ok &= check("ground in the hole", grid.groundAt(-26.75, 10, -26.75), Double.NaN);
		ok &= check("ground inside the tunnel", grid.groundAt(-20, 10, 0), 10.0);
		ok &= check("ground on the tunnel roof", grid.groundAt(-20, 14, 0), 14.0);
		ok &= check("boxes in block (0,10,0)", boxes(grid, 0, 10, 0), 4);
		ok &= check("boxes in block (-1,10,0), ground exactly at its bottom", boxes(grid, -1, 10, 0), 0);
		ok &= check("boxes in block (-1,9,0), fully under ground", boxes(grid, -1, 9, 0), 4);
		ok &= check("boxes in tunnel block (-20,11,0), open space", boxes(grid, -20, 11, 0), 0);
		ok &= check("boxes in tunnel block (-20,12,0), open space", boxes(grid, -20, 12, 0), 0);
		ok &= check("boxes in tunnel roof block (-20,13,0)", boxes(grid, -20, 13, 0), 4);
		ok &= check("boxes in tunnel floor block (-20,9,0)", boxes(grid, -20, 9, 0), 4);
		List<double[]> top = new ArrayList<>();
		grid.forEachBox(0, 10, 0, (a, b, c, d, e, f) -> top.add(new double[] {a, b, c, d, e, f}));
		ok &= check("box top in block (0,10,0)", top.isEmpty() ? -1 : top.get(0)[4], 0.5);
		ok &= check("wanted player height (blocks)", Math.round(myc.playerHeight * 1000) / 1000.0, 0.964);

		int ack = myc.teleportSeq;
		double x = myc.tpX, y = myc.tpY, z = myc.tpZ;
		int frames = 0;
		long start = System.currentTimeMillis();
		while (true) {
			double t = (System.currentTimeMillis() - start) / 1000.0;
			if (t > 10.5) {
				break;
			}
			// 0-5 s: east (+X). 5-10 s: north (-Z). 2 blocks per second.
			double px = x + 2 * Math.min(t, 5);
			double pz = z - 2 * Math.max(0, Math.min(t - 5, 5));
			float yaw = t < 5 ? -90f : 180f;
			MycLink.poll();
			MycLink.writeMcState(Proto.MC_IN_WORLD | Proto.MC_ON_GROUND, px, y, pz, yaw, 0f, frames++, 1, ack);
			if (frames % 60 == 0) {
				boolean read = MycLink.readMycState(myc);
				System.out.printf("t=%.1f mc=(%.2f, %.2f, %.2f) | mycelium alive=%b read=%b flags=%d spore=(%.2f, %.2f, %.2f)%n",
					t, px, y, pz, MycLink.myceliumAlive(), read, myc.flags, myc.x, myc.y, myc.z);
			}
			Thread.sleep(16);
		}
		System.out.println(ok ? "ground checks PASS" : "ground checks FAIL");
		System.exit(ok ? 0 : 1);
	}

	private static int boxes(GroundGrid grid, int x, int y, int z) {
		int[] count = {0};
		grid.forEachBox(x, y, z, (a, b, c, d, e, f) -> count[0]++);
		return count[0];
	}

	private static boolean check(String what, double got, double expected) {
		boolean pass = Double.isNaN(expected) ? Double.isNaN(got) : Math.abs(got - expected) < 1e-6;
		System.out.printf("  %s %s: got %s, expected %s%n", pass ? "ok  " : "FAIL", what, got, expected);
		return pass;
	}
}
