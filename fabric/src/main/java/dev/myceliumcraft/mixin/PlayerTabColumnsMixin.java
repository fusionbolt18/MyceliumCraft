package dev.myceliumcraft.mixin;

import com.llamalad7.mixinextras.sugar.Local;
import dev.myceliumcraft.link.MycLink;
import java.util.List;
import net.minecraft.client.gui.components.PlayerTabOverlay;
import net.minecraft.client.multiplayer.PlayerInfo;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.Constant;
import org.spongepowered.asm.mixin.injection.ModifyConstant;

/**
 * The player list in 2 columns (your request, 2026-11-08). Minecraft's
 * PlayerTabOverlay.extractRenderState starts with 1 column and adds one while a column would hold
 * more than 20 rows. Here that limit is half the rows (rounded up), so 2 or more rows always make
 * 2 columns: you and your colony's spores side by side. Never more than 20, so a list longer than
 * 40 still gets a third column as Minecraft would. One row stays 1 column.
 */
@Mixin(PlayerTabOverlay.class)
public abstract class PlayerTabColumnsMixin {
	@Unique
	private static int myceliumcraft$lastRows = -1;

	@ModifyConstant(method = "extractRenderState", constant = @Constant(intValue = 20, ordinal = 0))
	private int myceliumcraft$twoColumns(int perColumn, @Local(ordinal = 0) List<PlayerInfo> rows) {
		int n = rows.size();
		int limit = Math.max(1, Math.min(perColumn, (n + 1) / 2));
		if (n != myceliumcraft$lastRows) {
			myceliumcraft$lastRows = n;
			int columns = 1;
			for (int r = n; r > limit; r = (n + columns - 1) / columns) {
				columns++;
			}
			MycLink.LOG.info("MyceliumCraft: player list: {} rows in {} column(s), at most {} a column", n, columns, limit);
		}
		return limit;
	}
}
