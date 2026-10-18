package dev.myceliumcraft.mixin;

import java.util.Map;
import net.minecraft.client.renderer.texture.AbstractTexture;
import net.minecraft.client.renderer.texture.TextureManager;
import net.minecraft.resources.Identifier;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

@Mixin(TextureManager.class)
public interface TextureManagerAccessor {
	/** Registered textures (downloaded skins live here), without loading anything that isn't. */
	@Accessor("byPath")
	Map<Identifier, AbstractTexture> myceliumcraft$byPath();
}
