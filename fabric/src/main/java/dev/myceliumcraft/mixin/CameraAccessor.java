package dev.myceliumcraft.mixin;

import net.minecraft.client.Camera;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

/** Minecraft's own smoothed FOV multiplier and eye height, for Mycelium's camera. */
@Mixin(Camera.class)
public interface CameraAccessor {
	@Accessor("fovModifier")
	float myceliumcraft$fovModifier();

	@Accessor("oldFovModifier")
	float myceliumcraft$oldFovModifier();

	@Accessor("eyeHeight")
	float myceliumcraft$eyeHeight();

	@Accessor("eyeHeightOld")
	float myceliumcraft$eyeHeightOld();
}
