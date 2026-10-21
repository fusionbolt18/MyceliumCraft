package dev.myceliumcraft;

import com.mojang.blaze3d.platform.Lighting;
import com.mojang.blaze3d.systems.RenderSystem;
import com.mojang.blaze3d.vertex.PoseStack;
import com.mojang.renderpearl.api.buffers.GpuBufferSlice;
import com.mojang.renderpearl.api.commands.RenderPass;
import dev.myceliumcraft.link.MycLink;
import java.lang.reflect.Method;
import java.util.Optional;
import java.util.OptionalDouble;
import net.minecraft.client.Camera;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.SubmitNodeStorage;
import net.minecraft.client.renderer.entity.state.AvatarRenderState;
import net.minecraft.client.renderer.entity.state.EntityRenderState;
import net.minecraft.client.renderer.feature.FeatureRenderDispatcher;
import net.minecraft.client.renderer.state.level.CameraRenderState;
import net.minecraft.client.renderer.state.level.LevelRenderState;
import net.minecraft.world.phys.Vec3;
import org.joml.Matrix4fStack;

/**
 * First person with Better Combat: your arms swinging the weapon, in the overlay.
 *
 * Better Combat animates attacks through Player Animation Library (PAL). In first person, while
 * an animation plays, PAL hides Minecraft's normal hand and instead puts your whole player into
 * the list of things the world draws, marked so only the arms and held items show (its
 * LevelExtractorMixin and LivingEntityRendererMixin). The overlay skips Minecraft's world
 * (LevelRendererMixin), so those arms were never drawn.
 *
 * So while the overlay is on, this draws just your own player, with the world's camera, into
 * Minecraft's picture: the same steps Minecraft uses for the normal hand
 * (GameRenderer.renderItemInHand) and for entities (LevelRenderer.submitEntities).
 *
 * Which player: the one PAL put in the world's list. In your first test (2026-10-21) it never
 * was there during attacks, though PAL hid the hand (both use the same test,
 * ClientUtil.shouldBeFirstPersonPass). So when PAL's test says yes and the list has no player,
 * this extracts your player itself and marks it first-person the way PAL's LevelExtractorMixin
 * does (IAvatarAnimationState.playerAnimLib$setFirstPersonPass). Both are PAL's public methods,
 * called by reflection; nothing of Better Combat or PAL is changed. Render thread only.
 */
public final class MycFirstPersonArms {
	private static final SubmitNodeStorage STORAGE = new SubmitNodeStorage();
	private static boolean warned, palMissing;
	private static boolean drawing;
	private static int frames, drawn, fromList, ourOwn, mostEntities;
	private static long lastLogMs;
	private static Method shouldBeFirstPersonPass, setFirstPersonPass;

	private MycFirstPersonArms() {
	}

	/** In place of LevelRenderer.render while the overlay is on. */
	public static void draw(Minecraft minecraft, LevelRenderState level, CameraRenderState camera, GpuBufferSlice fog) {
		frames++;
		mostEntities = Math.max(mostEntities, level.entityRenderStates.size());
		var player = minecraft.player;
		EntityRenderState mine = null;
		boolean own = false;
		if (player != null && palWantsBody(minecraft)) {
			int id = player.getId();
			for (EntityRenderState s : level.entityRenderStates) {
				if (s instanceof AvatarRenderState a && a.id == id) {
					mine = s;
					break;
				}
			}
			if (mine == null) {
				try {
					mine = minecraft.getEntityRenderDispatcher().extractEntity(player, camera.cameraEntityPartialTicks);
					setFirstPersonPass.invoke(mine, true);
					mine.shadowPieces.clear(); // as PAL does
					mine.shadowRadius = 0f;
					mine.nameTag = null;
					own = true;
				} catch (ReflectiveOperationException | RuntimeException e) {
					mine = null;
					if (!warned) {
						warned = true;
						MycLink.LOG.warn("MyceliumCraft: couldn't pose the first-person arms", e);
					}
				}
			}
		}
		log(mine != null, own);
		if (mine == null) {
			return;
		}
		if (own) {
			ourOwn++;
		} else {
			fromList++;
		}
		Matrix4fStack modelView = RenderSystem.getModelViewStack();
		modelView.pushMatrix();
		try {
			modelView.mul(camera.viewRotationMatrix);
			Vec3 at = camera.pos;
			minecraft.getEntityRenderDispatcher().submit(mine, camera, mine.x - at.x(), mine.y - at.y(), mine.z - at.z(), new PoseStack(), STORAGE);
			RenderSystem.setShaderFog(fog);
			minecraft.gameRenderer.lighting().setupFor(Lighting.Entry.LEVEL);
			var target = minecraft.gameRenderer.mainRenderTarget();
			try (FeatureRenderDispatcher.PreparedFrame frame = minecraft.gameRenderer.featureRenderDispatcher().prepareFrame(STORAGE);
					RenderPass pass = RenderSystem.getDevice().createCommandEncoder().createRenderPass(() -> "MyceliumCraft first-person arms",
							target.getColorTextureView(), Optional.empty(), target.getDepthTextureView(), OptionalDouble.of(0.0))) {
				// 26.3's depth is reversed: 0 is "nothing drawn yet" (GameRenderer clears to 0). 1.0 hid every pixel.
				RenderSystem.bindDefaultUniforms(pass);
				FeatureRenderDispatcher.renderAllFeatures(pass, frame);
			}
			drawn++;
		} catch (RuntimeException e) {
			if (!warned) {
				warned = true;
				MycLink.LOG.warn("MyceliumCraft: couldn't draw the first-person arms", e);
			}
		} finally {
			modelView.popMatrix();
		}
	}

	/** PAL's own test for "hide the hand, draw the body's arms" (false without PAL). */
	private static boolean palWantsBody(Minecraft minecraft) {
		if (palMissing) {
			return false;
		}
		try {
			if (shouldBeFirstPersonPass == null) {
				shouldBeFirstPersonPass = Class.forName("com.zigythebird.playeranim.util.ClientUtil").getMethod("shouldBeFirstPersonPass", Camera.class);
				setFirstPersonPass = Class.forName("com.zigythebird.playeranim.accessors.IAvatarAnimationState").getMethod("playerAnimLib$setFirstPersonPass", boolean.class);
				MycLink.LOG.info("MyceliumCraft: Player Animation Library found; first-person arms follow its animations");
			}
			return (Boolean) shouldBeFirstPersonPass.invoke(null, minecraft.gameRenderer.mainCamera());
		} catch (ReflectiveOperationException | RuntimeException e) {
			palMissing = true;
			MycLink.LOG.info("MyceliumCraft: no Player Animation Library (or it changed): {}", e.toString());
			return false;
		}
	}

	private static void log(boolean now, boolean own) {
		if (now != drawing) {
			drawing = now;
			MycLink.LOG.info("MyceliumCraft: first-person arms {}", now
					? "drawn in the overlay (Player Animation Library shows the body for an animation; posed " + (own ? "by us" : "by Minecraft's world list") + ")"
					: "back to Minecraft's normal hand");
		}
		long ms = System.currentTimeMillis();
		if (ms - lastLogMs >= 10_000) {
			lastLogMs = ms;
			MycLink.LOG.info("MyceliumCraft: overlay world skipped {} frames, arms drawn in {} ({} from Minecraft's world list, {} posed by us); at most {} things in that list",
					frames, drawn, fromList, ourOwn, mostEntities);
			frames = drawn = fromList = ourOwn = mostEntities = 0;
		}
	}
}
