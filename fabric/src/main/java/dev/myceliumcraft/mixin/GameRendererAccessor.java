package dev.myceliumcraft.mixin;

import net.minecraft.client.renderer.GameRenderer;
import net.minecraft.client.renderer.fog.FogRenderer;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

/** Minecraft's fog, for the "no fog" setting its hand is drawn with (MycFirstPersonArms). */
@Mixin(GameRenderer.class)
public interface GameRendererAccessor {
	@Accessor("fogRenderer")
	FogRenderer myceliumcraft$fogRenderer();
}
