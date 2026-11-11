package dev.myceliumcraft.mixin;

import dev.myceliumcraft.MycGoal;
import dev.myceliumcraft.link.MycLink;
import net.minecraft.client.DeltaTracker;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.Hud;
import net.minecraft.client.gui.contextualbar.ContextualBar;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.Redirect;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * The level goal's marker behind the experience level number (your request, 2026-11-10).
 * Hud.extractHotbarAndDecorations draws the bar's background, then the level number, then the bar
 * itself (the locator bar's markers), so a marker in the middle covered your nutrients. While the
 * goal is on the bar (MycGoal) the number is held back and drawn after the bar instead. Otherwise
 * it's drawn where Minecraft draws it.
 */
@Mixin(Hud.class)
public abstract class HudLevelNumberMixin {
	@Unique
	private static Font myceliumcraft$font;
	@Unique
	private static int myceliumcraft$level;
	@Unique
	private static boolean myceliumcraft$logged;

	@Redirect(method = "extractHotbarAndDecorations", at = @At(value = "INVOKE",
		target = "Lnet/minecraft/client/gui/contextualbar/ContextualBar;extractExperienceLevel(Lnet/minecraft/client/gui/GuiGraphicsExtractor;Lnet/minecraft/client/gui/Font;I)V"))
	private void myceliumcraft$holdLevelNumber(GuiGraphicsExtractor graphics, Font font, int level) {
		if (MycGoal.onBar()) {
			myceliumcraft$font = font;
			myceliumcraft$level = level;
		} else {
			ContextualBar.extractExperienceLevel(graphics, font, level);
		}
	}

	@Inject(method = "extractHotbarAndDecorations", at = @At(value = "INVOKE",
		target = "Lnet/minecraft/client/gui/contextualbar/ContextualBar;extractRenderState(Lnet/minecraft/client/gui/GuiGraphicsExtractor;Lnet/minecraft/client/DeltaTracker;)V",
		shift = At.Shift.AFTER))
	private void myceliumcraft$levelNumberOnTop(GuiGraphicsExtractor graphics, DeltaTracker delta, CallbackInfo ci) {
		if (myceliumcraft$font == null) {
			return;
		}
		ContextualBar.extractExperienceLevel(graphics, myceliumcraft$font, myceliumcraft$level);
		myceliumcraft$font = null;
		if (!myceliumcraft$logged) {
			myceliumcraft$logged = true;
			MycLink.LOG.info("MyceliumCraft: goal: the level number is drawn over the locator bar, so the marker goes behind it (logged once)");
		}
	}
}
