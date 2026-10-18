package dev.myceliumcraft.mixin;

import net.minecraft.client.renderer.rendertype.RenderSetup;
import net.minecraft.client.renderer.rendertype.RenderType;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

/** A render type's name and setup, to find which texture a model part is drawn with (as SkyCraft). */
@Mixin(RenderType.class)
public interface RenderTypeAccessor {
	@Accessor("state")
	RenderSetup myceliumcraft$state();

	@Accessor("name")
	String myceliumcraft$name();
}
