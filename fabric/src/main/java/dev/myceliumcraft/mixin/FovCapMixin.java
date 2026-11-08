package dev.myceliumcraft.mixin;

import dev.myceliumcraft.MyceliumCraftClient;
import dev.myceliumcraft.link.MycLink;
import net.minecraft.client.Camera;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.Constant;
import org.spongepowered.asm.mixin.injection.ModifyConstant;

/**
 * Minecraft widens its view with movement speed, (speed / walking speed + 1) / 2, but stops at x1.5
 * (Camera.tickFov clamps it to 0.1..1.5). Mycelium's Speed stat makes you fast (x1.84 in your run),
 * so sprinting alone reached x1.5 and Mycotoxins' +50% changed nothing (your report, 2026-11-08). Your
 * choice: while Mycelium is playing, the limit is x2.5. Outside its levels it's Minecraft's own 1.5.
 * Mycelium's camera follows the same number (McState's FOV multiplier).
 */
@Mixin(Camera.class)
public abstract class FovCapMixin {
	@Unique
	private static final float MYCELIUM_CAP = 2.5f;
	@Unique
	private static boolean myceliumcraft$lastRaised;

	@ModifyConstant(method = "tickFov", constant = @Constant(floatValue = 1.5f, ordinal = 0))
	private float myceliumcraft$raiseFovCap(float cap) {
		boolean raised = MyceliumCraftClient.myceliumPlaying();
		if (raised != myceliumcraft$lastRaised) {
			myceliumcraft$lastRaised = raised;
			MycLink.LOG.info("MyceliumCraft: fov: the speed view limit is x{}", raised ? MYCELIUM_CAP + " while Mycelium plays" : cap + " (Minecraft's own)");
		}
		return raised ? MYCELIUM_CAP : cap;
	}
}
