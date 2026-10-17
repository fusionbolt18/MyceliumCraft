package dev.myceliumcraft.link;

/**
 * Pretends to be Minecraft, for testing the link without starting the game. It walks a fake player
 * 10 blocks east over 5 seconds, then 10 blocks north, and prints what Mycelium's side reports.
 * Run tools\FakeMycelium (or the real MyceliumCraft.exe) first.
 */
public final class LinkTestMain {
	public static void main(String[] args) throws InterruptedException {
		long deadline = System.currentTimeMillis() + 15000;
		while (!MycLink.mapped()) {
			MycLink.poll();
			if (System.currentTimeMillis() > deadline) {
				System.out.println("FAIL: no Mycelium shared memory after 15 s");
				System.exit(1);
			}
			Thread.sleep(100);
		}
		System.out.println("linked");
		MycLink.MycState myc = new MycLink.MycState();
		double x = 100.5, y = 64, z = 200.5;
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
			MycLink.writeMcState(Proto.MC_IN_WORLD | Proto.MC_ON_GROUND, px, y, pz, yaw, 0f, frames++, 1);
			if (frames % 60 == 0) {
				boolean ok = MycLink.readMycState(myc);
				System.out.printf("t=%.1f mc=(%.2f, %.2f, %.2f) yaw=%.0f | mycelium alive=%b read=%b flags=%d pos=(%.2f, %.2f, %.2f) yaw=%.0f frame=%d%n",
					t, px, y, pz, yaw, MycLink.myceliumAlive(), ok, myc.flags, myc.x, myc.y, myc.z, myc.yaw, myc.frame);
			}
			Thread.sleep(16);
		}
		System.out.println("done");
	}
}
