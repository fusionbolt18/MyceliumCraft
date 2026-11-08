package dev.myceliumcraft.mixin;

import com.mojang.blaze3d.platform.Lighting;
import org.joml.Vector3fc;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;
import org.spongepowered.asm.mixin.gen.Invoker;

/** Minecraft's two light directions, for your hand in the overlay (HandLightMixin). */
@Mixin(Lighting.class)
public interface LightingAccessor {
	@Accessor("DIFFUSE_LIGHT_0")
	static Vector3fc myceliumcraft$light0() {
		throw new AssertionError();
	}

	@Accessor("DIFFUSE_LIGHT_1")
	static Vector3fc myceliumcraft$light1() {
		throw new AssertionError();
	}

	@Invoker("updateBuffer")
	void myceliumcraft$updateBuffer(Lighting.Entry entry, Vector3fc light0, Vector3fc light1);
}
