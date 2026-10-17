package dev.myceliumcraft.link;

/**
 * One scan of Mycelium's solid ground, in Minecraft coordinates (see GroundGrid in
 * protocol/PROTOCOL.md). Each cell has up to G_PIECES solid pieces (bottom, top), lowest first;
 * everything else is open space, so tunnels and overhangs stay walkable. Immutable; no Minecraft
 * classes, so the link test can check it outside the game.
 */
public final class GroundGrid {
	public final int version;
	public final int n;
	public final double originX, originZ, cell;
	private final float[] pieces; // [(j * n + i) * G_FLOATS_PER_CELL + 2k] = bottom, +1 = top

	public GroundGrid(int version, int n, double originX, double originZ, double cell, float[] pieces) {
		this.version = version;
		this.n = n;
		this.originX = originX;
		this.originZ = originZ;
		this.cell = cell;
		this.pieces = pieces;
	}

	/**
	 * Top of the highest solid piece at or below {@code y + 1} in the cell containing this
	 * Minecraft (x, z): the ground you'd stand on there. NaN if outside the grid or none.
	 */
	public double groundAt(double x, double y, double z) {
		int i = (int) Math.floor((x - originX) / cell);
		int j = (int) Math.floor((z - originZ) / cell);
		if (i < 0 || j < 0 || i >= n || j >= n) {
			return Double.NaN;
		}
		int c = (j * n + i) * Proto.G_FLOATS_PER_CELL;
		double best = Double.NaN;
		for (int k = 0; k < Proto.G_PIECES; k++) {
			float top = pieces[c + 2 * k + 1];
			if (top >= Proto.UNUSED) {
				break;
			}
			if (top <= y + 1) {
				best = top;
			}
		}
		return best;
	}

	public interface BoxConsumer {
		/** A box in block-local coordinates (0..1 inside the block). */
		void accept(double minX, double minY, double minZ, double maxX, double maxY, double maxZ);
	}

	/** The solid parts of block (bx, by, bz): one box per solid piece per grid cell overlapping it. */
	public void forEachBox(int bx, int by, int bz, BoxConsumer out) {
		int i0 = (int) Math.floor((bx - originX) / cell);
		int i1 = (int) Math.ceil((bx + 1 - originX) / cell) - 1;
		int j0 = (int) Math.floor((bz - originZ) / cell);
		int j1 = (int) Math.ceil((bz + 1 - originZ) / cell) - 1;
		for (int j = Math.max(0, j0); j <= Math.min(n - 1, j1); j++) {
			for (int i = Math.max(0, i0); i <= Math.min(n - 1, i1); i++) {
				double minX = clamp01(originX + i * cell - bx), maxX = clamp01(originX + (i + 1) * cell - bx);
				double minZ = clamp01(originZ + j * cell - bz), maxZ = clamp01(originZ + (j + 1) * cell - bz);
				if (maxX - minX <= 1e-6 || maxZ - minZ <= 1e-6) {
					continue;
				}
				int c = (j * n + i) * Proto.G_FLOATS_PER_CELL;
				for (int k = 0; k < Proto.G_PIECES; k++) {
					float bottom = pieces[c + 2 * k], top = pieces[c + 2 * k + 1];
					if (top >= Proto.UNUSED) {
						break;
					}
					double y0 = clamp01(bottom - by), y1 = clamp01(top - by);
					if (y1 - y0 > 1e-4) {
						out.accept(minX, y0, minZ, maxX, y1, maxZ);
					}
				}
			}
		}
	}

	private static double clamp01(double v) {
		return v < 0 ? 0 : v > 1 ? 1 : v;
	}
}
