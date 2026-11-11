package dev.myceliumcraft.mixin;

import dev.myceliumcraft.MycWorldMode;
import java.util.Arrays;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.ModifyArg;

/**
 * The Game tab's game mode button cycles Survival, Hardcore, Creative; "Mycelium" is added after
 * them (MycWorldMode). The first CycleButton.Builder.withValues call in the tab is the game mode's.
 */
@Mixin(targets = "net.minecraft.client.gui.screens.worldselection.CreateWorldScreen$GameTab")
public abstract class CreateWorldGameTabMixin {
	@ModifyArg(method = "<init>", at = @At(value = "INVOKE",
		target = "Lnet/minecraft/client/gui/components/CycleButton$Builder;withValues([Ljava/lang/Object;)Lnet/minecraft/client/gui/components/CycleButton$Builder;",
		ordinal = 0), index = 0)
	private Object[] myceliumcraft$addMycelium(Object[] values) {
		if (MycWorldMode.MYCELIUM == null) {
			return values;
		}
		Object[] more = Arrays.copyOf(values, values.length + 1, values.getClass().asSubclass(Object[].class));
		more[values.length] = MycWorldMode.MYCELIUM;
		return more;
	}
}
