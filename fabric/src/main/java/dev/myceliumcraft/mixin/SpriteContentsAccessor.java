package dev.myceliumcraft.mixin;

import com.mojang.blaze3d.platform.NativeImage;
import net.minecraft.client.renderer.texture.SpriteContents;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

/** A sprite's own pixels (all its animation frames, stacked), as SkyCraft reads them. */
@Mixin(SpriteContents.class)
public interface SpriteContentsAccessor {
	@Accessor("originalImage")
	NativeImage myceliumcraft$originalImage();
}
