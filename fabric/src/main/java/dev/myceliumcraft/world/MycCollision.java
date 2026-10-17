package dev.myceliumcraft.world;

import dev.myceliumcraft.link.GroundGrid;
import java.util.concurrent.ConcurrentHashMap;
import net.minecraft.core.BlockPos;
import net.minecraft.world.phys.shapes.Shapes;
import net.minecraft.world.phys.shapes.VoxelShape;
import org.jspecify.annotations.Nullable;

/**
 * Mycelium's ground as Minecraft collision shapes, per block position. Read by BlockCollisionsMixin
 * on both the client and the integrated server thread, so the server's movement checks see the
 * same ground the client walks on.
 */
public final class MycCollision {
	private record Active(GroundGrid grid, ConcurrentHashMap<Long, VoxelShape> shapes) {
	}

	private static volatile @Nullable Active active;

	private MycCollision() {
	}

	/** Swap in a new scan. Shapes are rebuilt lazily as Minecraft asks for them. */
	public static void setGrid(GroundGrid grid) {
		active = new Active(grid, new ConcurrentHashMap<>());
	}

	public static @Nullable GroundGrid grid() {
		Active a = active;
		return a == null ? null : a.grid;
	}

	/** Mycelium's ground inside this block, or null if none. */
	public static @Nullable VoxelShape shapeAt(BlockPos pos) {
		Active a = active;
		if (a == null) {
			return null;
		}
		if (a.shapes.size() > 200_000) {
			a.shapes.clear();
		}
		VoxelShape shape = a.shapes.computeIfAbsent(pos.asLong(), k -> build(a.grid, pos.getX(), pos.getY(), pos.getZ()));
		return shape.isEmpty() ? null : shape;
	}

	private static VoxelShape build(GroundGrid grid, int bx, int by, int bz) {
		VoxelShape[] shape = {Shapes.empty()};
		grid.forEachBox(bx, by, bz, (x0, y0, z0, x1, y1, z1) -> shape[0] = Shapes.or(shape[0], Shapes.box(x0, y0, z0, x1, y1, z1)));
		return shape[0];
	}
}
