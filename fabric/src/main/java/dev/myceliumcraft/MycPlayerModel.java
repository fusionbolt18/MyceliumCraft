package dev.myceliumcraft;

import com.mojang.blaze3d.platform.NativeImage;
import com.mojang.blaze3d.vertex.PoseStack;
import com.mojang.blaze3d.vertex.VertexConsumer;
import dev.myceliumcraft.link.MycLink;
import dev.myceliumcraft.link.Proto;
import dev.myceliumcraft.mixin.RenderSetupAccessor;
import dev.myceliumcraft.mixin.RenderTypeAccessor;
import dev.myceliumcraft.mixin.SpriteContentsAccessor;
import dev.myceliumcraft.mixin.TextureBindingAccessor;
import dev.myceliumcraft.mixin.TextureManagerAccessor;
import java.util.HashMap;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import net.fabricmc.fabric.api.client.renderer.v1.mesh.MeshView;
import net.minecraft.client.Camera;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.Font;
import net.minecraft.client.model.Model;
import net.minecraft.client.renderer.OrderedSubmitNodeCollector;
import net.minecraft.client.renderer.SubmitNodeCollector;
import net.minecraft.client.renderer.block.MovingBlockRenderState;
import net.minecraft.client.renderer.block.dispatch.BlockStateModelPart;
import net.minecraft.client.renderer.chunk.ChunkSectionLayer;
import net.minecraft.client.renderer.entity.state.EntityRenderState;
import net.minecraft.client.renderer.feature.ModelFeatureRenderer;
import net.minecraft.client.renderer.gizmos.DrawableGizmoPrimitives;
import net.minecraft.client.renderer.item.ItemStackRenderState;
import net.minecraft.client.renderer.rendertype.RenderType;
import net.minecraft.client.renderer.state.level.CameraRenderState;
import net.minecraft.client.renderer.state.level.QuadParticleRenderState;
import net.minecraft.client.renderer.texture.DynamicTexture;
import net.minecraft.client.renderer.texture.OverlayTexture;
import net.minecraft.client.renderer.texture.UvMapping;
import net.minecraft.client.model.geom.builders.UVPair;
import net.minecraft.client.renderer.texture.SpriteContents;
import net.minecraft.client.renderer.texture.TextureAtlasSprite;
import net.minecraft.client.resources.model.geometry.BakedQuad;
import net.minecraft.client.resources.model.geometry.ItemQuads;
import net.minecraft.core.Direction;
import org.joml.Matrix4f;
import org.joml.Vector3f;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.Identifier;
import net.minecraft.util.FormattedCharSequence;
import net.minecraft.world.item.ItemDisplayContext;
import net.minecraft.world.phys.Vec3;
import net.minecraft.world.phys.shapes.VoxelShape;
import org.joml.Quaternionf;
import org.jspecify.annotations.Nullable;

/**
 * Your Minecraft player's body, for Mycelium to draw instead of the spore in third person.
 *
 * Minecraft's own player renderer poses and animates the model (skin, second skin layer, armour,
 * cape, walking, swinging, sneaking) and "submits" it; this collector takes the submitted model
 * parts as triangles instead of drawing them, and sends them with their textures to Mycelium
 * (Model and Textures blocks in protocol/PROTOCOL.md). Positions are relative to the player's
 * feet, in Minecraft blocks and axes, already turned to where the body faces.
 *
 * Held items and blocks come as quads from Minecraft's big texture sheets (atlases); each picture
 * ("sprite") they use is sent once as a small texture of its own, and their texture coordinates
 * are turned from the sheet's into the sprite's (as SkyCraft does with its combined atlas).
 *
 * Adapted from the player part of SkyCraft's AvatarExporter (MIT, chasmlol). Not yet: enchantment
 * glint, animated item textures (first frame only), Fabric-mesh items, name tags.
 * Render thread only.
 */
public final class MycPlayerModel implements SubmitNodeCollector {
	private static final MycPlayerModel INSTANCE = new MycPlayerModel();

	private static final Map<Identifier, Integer> TEXTURE_IDS = new HashMap<>();
	private static final Map<SpriteContents, Integer> SPRITE_IDS = new HashMap<>(); // held items' pictures
	private static final Direction[] FACES_AND_NONE = { null, Direction.DOWN, Direction.UP, Direction.NORTH, Direction.SOUTH, Direction.WEST, Direction.EAST };
	private static int itemLogs;
	private static final Set<Identifier> UNUSABLE = new HashSet<>();
	private static int nextTextureId = 1;
	private static long nextTextureOffset;
	private static boolean shown;
	private static int linkSeen;
	private static boolean warned;
	private static long lastLogMs;
	private static int sent, lastVertices, lastBatches;

	private final Map<Integer, Batch> batches = new LinkedHashMap<>();
	private final Capture capture = new Capture();

	private MycPlayerModel() {
	}

	/** New link (or Mycelium restarted): its textures are gone, send them again. */
	public static void reset() {
		TEXTURE_IDS.clear();
		SPRITE_IDS.clear();
		UNUSABLE.clear();
		nextTextureId = 1;
		nextTextureOffset = 0;
		shown = false;
		MycSporeBodies.reset();
	}

	/** After every frame: send the body (in every view; in first person it's only for its shadow). */
	public static void frame(Minecraft minecraft, boolean wanted, float partialTick) {
		if (MycLink.linkCount() != linkSeen) {
			// New shared memory: Mycelium has none of our textures.
			linkSeen = MycLink.linkCount();
			reset();
		}
		var player = minecraft.player;
		Camera camera = minecraft.gameRenderer.mainCamera();
		if (!wanted || player == null) {
			if (shown) {
				MycLink.writeModel(0, null, 0, null, 0);
				shown = false;
				MycLink.LOG.info("MyceliumCraft: player model hidden (overlay off)");
			}
			return;
		}
		INSTANCE.capture(minecraft, camera, partialTick);
	}

	/** Take one colony spore's body from its render state and send it (slot = its place in the colony). False: nothing came out. */
	static boolean sendBody(int slot, EntityRenderState state, Minecraft minecraft, PoseStack poses) {
		return INSTANCE.captureBody(slot, state, minecraft, poses);
	}

	private boolean captureBody(int slot, EntityRenderState state, Minecraft minecraft, PoseStack poses) {
		for (Batch b : this.batches.values()) {
			b.count = 0;
		}
		CameraRenderState cameraState = minecraft.gameRenderer.gameRenderState().levelRenderState.cameraRenderState;
		minecraft.getEntityRenderDispatcher().submit(state, cameraState, 0.0, 0.0, 0.0, poses, this);
		this.capture.flush();
		Batch skin = null;
		for (Batch b : this.batches.values()) {
			if (b.count >= 4 && (skin == null || b.count > skin.count)) {
				skin = b; // a spore wears nothing: its skin is the only texture
			}
		}
		if (skin == null || skin.count / 4 * 6 > Proto.BO_MAX_VERTICES) {
			return false;
		}
		int vertices = skin.count / 4 * 6;
		int[] data = new int[vertices * 6];
		int f = 0;
		for (int q = 0; q + 4 <= skin.count; q += 4) {
			for (int k : new int[] { 0, 1, 2, 0, 2, 3 }) {
				System.arraycopy(skin.data, (q + k) * 6, data, f, 6);
				f += 6;
			}
		}
		MycLink.writeBody(slot, skin.texture, data, vertices);
		return true;
	}

	private static int lastDecoration = -1;
	private static final float BODY_HALF_THICKNESS = 0.125f; // a body lying flat: half its 4-pixel depth, in blocks before scaling
	private static final float HEAD_TILT_ON_BED = 20f; // degrees, towards the chest
	private static final float HEAD_FROM_FEET = 1.75f; // blocks before scaling: the head's middle, lying (28 of 32 model pixels)
	private static final float HEAD_HALF_DEPTH = 0.25f; // blocks before scaling: half the head's 8 pixels
	private static final float MAX_TILT = 35f; // degrees
	private static final float SLIDE_DOWN_BED = 0.3f; // blocks: the body moved this far from the head's end
	private static int lastLie = -1;
	private static float lastTilt = -1f;

	/**
	 * A decoration in use in Mycelium (MycState SIT / LIE, your requests, 2026-11-08): your body in
	 * Minecraft's own sitting pose (the riding pose, lowered so you sit on the floor as at Mycelium's
	 * stump chairs) or sleeping pose, as Mycelium's spore lies on its bed:
	 * - "Sleep" (LIE): on your back, the head behind where you face (measured: Mycelium's head bone
	 *   ends 178 degrees from the spore's facing), so it's on the bed's pillow side;
	 * - "Lay" (LIE + LIE_FRONT): the head ahead (Mycelium's spore lies on its front then; you stay on
	 *   your back, your choice).
	 * Minecraft's sleeping pose with no bed direction turns the body by its body yaw at any angle (no
	 * four-way snap, no shift), and its head then points along yaw 90 - bodyRot (LivingEntityRenderer.
	 * setupRotations: Y(bodyRot), Z(90), Y(270)). It's lifted by half its thickness so it lies on the
	 * bed, not half in it, and tilted up from the feet so the back of the head rests on what's under
	 * it: the pillow, read from Mycelium's ground (GroundGrid, which has the bed's shape) where the
	 * head lies, against the ground under the feet (your report: the head was inside the pillow). On
	 * the bed the head doesn't follow your look: straight, tilted a little forward (towards the chest).
	 */
	private static void decorationPose(EntityRenderState state, PoseStack poses, Minecraft minecraft, float partialTick) {
		int flags = MyceliumCraftClient.mycFlags();
		boolean front = (flags & Proto.MYC_LIE_FRONT) != 0;
		int now = (flags & Proto.MYC_SIT) != 0 ? 1 : (flags & Proto.MYC_LIE) != 0 ? (front ? 3 : 2) : 0;
		if (now != lastDecoration) {
			lastDecoration = now;
			MycLink.LOG.info("MyceliumCraft: player model: {}", now == 1 ? "sitting at a decoration" : now == 2 ? "lying on its back on a decoration, head behind" : now == 3 ? "lying on its front on a decoration, head ahead" : "standing");
		}
		if (now == 1 && state instanceof net.minecraft.client.renderer.entity.state.HumanoidRenderState humanoid) {
			humanoid.isPassenger = true;
			// The riding pose keeps the hips at standing height (12 of 32 model pixels up): down by that, so you sit on the floor.
			poses.translate(0.0, -0.62 * humanoid.scale, 0.0);
		} else if (now >= 2 && state instanceof net.minecraft.client.renderer.entity.state.LivingEntityRenderState living) {
			// Your body is drawn turned to where you face, so its own facing is the world's.
			var stand = minecraft.player != null ? minecraft.player.getPosition(partialTick) : null;
			float facing = living.bodyRot; // your held facing (MycEmotes), the spore's
			double[] fit = lie(living, poses, stand, facing, facing, front);
			if (fit != null && (now != lastLie || Math.abs(fit[2] - lastTilt) > 2f)) {
				lastLie = now;
				lastTilt = (float) fit[2];
				MycLink.LOG.info("MyceliumCraft: player model: on the bed, head towards yaw {}: pillow top {} above where you stand, feet end {} above where you stand; tilted up {} degrees",
					String.format("%.0f", fit[3]), Double.isNaN(fit[0]) ? "none" : String.format("%.2f", fit[0]), String.format("%.2f", fit[1]), String.format("%.0f", fit[2]));
			}
		}
	}

	/**
	 * Lying on a decoration (Minecraft's sleeping pose), for you and the other spores. 'local' is the
	 * facing the body is drawn with, 'world' the facing in the world (the same for you; a spore's body
	 * is drawn facing south and Mycelium turns it to the spore's facing). 'stand': where the body
	 * stands in the world (null = unknown: it lies flat, lifted by half its thickness).
	 *
	 * The body as a straight line from its feet to its head (your reports, 2026-11-09):
	 * - slid 0.3 blocks towards the foot of the bed, so the head, not the upper back, is on the pillow;
	 * - the head's middle half the head's depth above the pillow's top: the highest ground from
	 *   halfway along the body to the head (Mycelium's ground, GroundGrid, half-block cells);
	 * - the feet end as low as the bed lets it: the lowest line from the head down that keeps the
	 *   body's underside (half its thickness below its middle line) on top of the ground everywhere
	 *   along it. Holding the feet at your standing height floated the legs over the bed's slope.
	 * Returns { pillow top above the stand (NaN = none), feet end above it, tilt degrees, head's world
	 * yaw }, or null without the ground or a stand.
	 */
	private static double[] lie(net.minecraft.client.renderer.entity.state.LivingEntityRenderState living, PoseStack poses,
			net.minecraft.world.phys.Vec3 stand, float local, float world, boolean front) {
		float head = front ? local : local + 180f; // where the head goes, as drawn
		float headWorld = front ? world : world + 180f;
		living.pose = net.minecraft.world.entity.Pose.SLEEPING;
		living.bedOrientation = null; // turned by bodyRot, at any angle
		living.bodyRot = 90f - head;
		double rad = Math.toRadians(head);
		float hx = (float) -Math.sin(rad), hz = (float) Math.cos(rad); // the head's direction as drawn
		double wrad = Math.toRadians(headWorld);
		double wx = -Math.sin(wrad), wz = Math.cos(wrad); // and in the world, for the ground
		float s = living.scale;
		double lift = BODY_HALF_THICKNESS * s, tilt = 0.0;
		double[] fit = null;
		var grid = dev.myceliumcraft.world.MycCollision.grid();
		if (grid != null && stand != null) {
			var feet = stand.add(-wx * SLIDE_DOWN_BED, 0.0, -wz * SLIDE_DOWN_BED);
			double reach = HEAD_FROM_FEET * s;
			double headGround = Double.NaN;
			for (double along = 0.5 * reach; along <= reach + 1e-6; along += 0.125) {
				double g = grid.groundAt(feet.x + wx * along, stand.y + 0.6, feet.z + wz * along);
				if (!Double.isNaN(g) && (Double.isNaN(headGround) || g > headGround)) {
					headGround = g;
				}
			}
			double headY = (Double.isNaN(headGround) ? stand.y : headGround) + HEAD_HALF_DEPTH * s; // the head's middle
			// The lowest feet height (middle line) that keeps the underside above the ground at each point.
			double feetY = Double.NEGATIVE_INFINITY;
			for (double along = 0.0; along <= 0.85 * reach + 1e-6; along += 0.125) {
				double g = grid.groundAt(feet.x + wx * along, stand.y + 0.6, feet.z + wz * along);
				if (Double.isNaN(g)) {
					continue;
				}
				double t = along / reach;
				feetY = Math.max(feetY, (g + BODY_HALF_THICKNESS * s - headY * t) / (1.0 - t));
			}
			if (feetY == Double.NEGATIVE_INFINITY) {
				feetY = stand.y + BODY_HALF_THICKNESS * s;
			}
			feetY = Math.min(feetY, headY); // the feet never above the head
			lift = feetY - stand.y;
			tilt = Math.toDegrees(Math.atan2(headY - feetY, reach));
			tilt = Math.max(-MAX_TILT, Math.min(MAX_TILT, tilt));
			fit = new double[] { Double.isNaN(headGround) ? Double.NaN : headGround - stand.y, lift, tilt, ((headWorld % 360f) + 360f) % 360f };
		}
		poses.translate(-hx * SLIDE_DOWN_BED, lift, -hz * SLIDE_DOWN_BED);
		if (tilt != 0.0) {
			// About the line across the body at the feet; the head end goes up.
			poses.mulPose(new org.joml.Matrix4f().rotation((float) -Math.toRadians(tilt), hz, 0f, -hx));
		}
		if (living instanceof net.minecraft.client.renderer.entity.state.HumanoidRenderState humanoid) {
			humanoid.yRot = 0f; // the head doesn't turn with the look on the bed
			humanoid.xRot = HEAD_TILT_ON_BED;
		}
		return fit;
	}

	private static final int[] SPORE_POSE = new int[Proto.COL_MAX];
	private static final float[] SPORE_TILT = new float[Proto.COL_MAX];

	/**
	 * Another spore at a decoration (your request, 2026-11-09), as yours: 1 sitting (the riding pose,
	 * lowered to the floor), 2 lying on its back with the head behind, 5 lying with the head ahead.
	 * The body is drawn facing south (local facing 0); 'world' is the spore's facing, 'stand' where it
	 * stands (null = unknown).
	 */
	static void sporeDecorationPose(EntityRenderState state, PoseStack poses, int slot, String name, int pose,
			net.minecraft.world.phys.Vec3 stand, float world) {
		if (pose == 1 && state instanceof net.minecraft.client.renderer.entity.state.HumanoidRenderState humanoid) {
			humanoid.isPassenger = true;
			poses.translate(0.0, -0.62 * humanoid.scale, 0.0);
		} else if ((pose == 2 || pose == 5) && state instanceof net.minecraft.client.renderer.entity.state.LivingEntityRenderState living) {
			double[] fit = lie(living, poses, stand, 0f, world, pose == 5);
			float tilt = fit != null ? (float) fit[2] : 0f;
			if (pose != SPORE_POSE[slot] || Math.abs(tilt - SPORE_TILT[slot]) > 2f) {
				SPORE_TILT[slot] = tilt;
				MycLink.LOG.info("MyceliumCraft: spore bodies: {} on the bed, head towards yaw {}: {}",
					name, fit != null ? String.format("%.0f", fit[3]) : "?",
					fit == null ? "where it stands isn't known, lying flat" : String.format("pillow top %s above where it stands, feet end %.2f above; tilted up %.0f degrees",
						Double.isNaN(fit[0]) ? "none" : String.format("%.2f", fit[0]), fit[1], fit[2]));
			}
		}
		if (pose != SPORE_POSE[slot]) {
			SPORE_POSE[slot] = pose;
			MycLink.LOG.info("MyceliumCraft: spore bodies: {} {}", name, pose == 1 ? "sits at a decoration" : pose == 2 ? "lies on its back at a decoration, head behind" : pose == 5 ? "lies at a decoration, head ahead" : pose == 4 ? "dances at the beetle drum" : "stands again");
		}
	}

	private void capture(Minecraft minecraft, Camera camera, float partialTick) {
		for (Batch b : this.batches.values()) {
			b.count = 0;
		}
		try {
			var dispatcher = minecraft.getEntityRenderDispatcher();
			dispatcher.prepare(camera, minecraft.crosshairPickEntity);
			EntityRenderState state = dispatcher.extractEntity(minecraft.player, partialTick);
			state.nameTag = null;
			PoseStack poses = new PoseStack();
			decorationPose(state, poses, minecraft, partialTick);
			CameraRenderState cameraState = minecraft.gameRenderer.gameRenderState().levelRenderState.cameraRenderState;
			// At the origin: positions come out relative to the player's feet.
			// Mycelium's sprout: digging out of the ground (HumanoidEmergeMixin, while this runs).
			EmergePose.setCapturing(MyceliumCraftClient.emergeProgress());
			try {
				dispatcher.submit(state, cameraState, 0.0, 0.0, 0.0, poses, this);
			} finally {
				EmergePose.setCapturing(Float.NaN);
			}
		} catch (RuntimeException e) {
			if (!warned) {
				warned = true;
				MycLink.LOG.warn("MyceliumCraft: couldn't capture the player model", e);
			}
			return;
		}
		this.capture.flush();

		int used = 0, vertices = 0;
		for (Batch b : this.batches.values()) {
			if (b.count >= 4) {
				used++;
				vertices += b.count / 4 * 6;
			}
		}
		if (used > Proto.M_MAX_BATCHES || vertices > Proto.M_MAX_VERTICES) {
			if (!warned) {
				warned = true;
				MycLink.LOG.warn("MyceliumCraft: player model too big ({} batches, {} vertices)", used, vertices);
			}
			return;
		}
		int[] table = new int[used * 3];
		int[] data = new int[vertices * 6];
		int t = 0, first = 0, f = 0;
		for (Batch b : this.batches.values()) {
			if (b.count < 4) {
				continue;
			}
			int count = b.count / 4 * 6;
			table[t++] = b.texture;
			table[t++] = first;
			table[t++] = count;
			first += count;
			for (int q = 0; q + 4 <= b.count; q += 4) {
				for (int k : new int[] { 0, 1, 2, 0, 2, 3 }) {
					System.arraycopy(b.data, (q + k) * 6, data, f, 6);
					f += 6;
				}
			}
		}
		// First person: Mycelium draws only its shadow (the camera is inside it).
		boolean firstPerson = !camera.isDetached();
		MycLink.writeModel(Proto.M_VISIBLE | (firstPerson ? Proto.M_FIRST_PERSON : 0), table, used, data, vertices);
		if (!shown) {
			MycLink.LOG.info("MyceliumCraft: player model shown ({} triangles in {} textures)", vertices / 3, used);
		}
		shown = true;
		sent++;
		lastVertices = vertices;
		lastBatches = used;
		long now = System.currentTimeMillis();
		if (now - lastLogMs >= 1000) {
			lastLogMs = now;
			MycLink.LOG.info("MyceliumCraft: player model: sent {} times in the last second, {} triangles, {} textures", sent, lastVertices / 3, lastBatches);
			sent = 0;
		}
	}

	/** Triangles for one texture. Vertices arrive as quads: x, y, z, u, v (float bits), RGBA. */
	private static final class Batch {
		final int texture;
		int[] data = new int[6 * 256];
		int count;

		Batch(int texture) {
			this.texture = texture;
		}

		void add(float x, float y, float z, float u, float v, int argb, int overlay) {
			if ((this.count + 1) * 6 > this.data.length) {
				this.data = java.util.Arrays.copyOf(this.data, this.data.length * 2);
			}
			// Minecraft's red "hurt" flash comes in as an overlay coordinate: tint it red (as SkyCraft).
			if (((overlay >>> 16) & 0xFFFF) < 8) {
				int r = (argb >> 16) & 0xFF, g = (int) (((argb >> 8) & 0xFF) * 0.55F), b = (int) ((argb & 0xFF) * 0.55F);
				argb = (argb & 0xFF000000) | (r << 16) | (g << 8) | b;
			}
			int o = this.count * 6;
			this.data[o] = Float.floatToRawIntBits(x);
			this.data[o + 1] = Float.floatToRawIntBits(y);
			this.data[o + 2] = Float.floatToRawIntBits(z);
			this.data[o + 3] = Float.floatToRawIntBits(u);
			this.data[o + 4] = Float.floatToRawIntBits(v);
			// RGBA bytes in memory order (little-endian int: A<<24 | B<<16 | G<<8 | R)
			int rgba = ((argb >>> 24) << 24) | ((argb & 0xFF) << 16) | (((argb >> 8) & 0xFF) << 8) | ((argb >> 16) & 0xFF);
			this.data[o + 5] = rgba;
			this.count++;
		}
	}

	/** A VertexConsumer that records into the current batch (models call addVertex, then the setters). */
	private final class Capture implements VertexConsumer {
		private Batch batch;
		private boolean pending;
		private float x, y, z, u, v;
		private int color, overlay;

		void begin(Batch b) {
			this.flush();
			this.batch = b;
		}

		void flush() {
			if (this.pending && this.batch != null) {
				this.batch.add(this.x, this.y, this.z, this.u, this.v, this.color, this.overlay);
			}
			this.pending = false;
		}

		@Override
		public VertexConsumer addVertex(float x, float y, float z) {
			this.flush();
			this.x = x;
			this.y = y;
			this.z = z;
			this.color = -1;
			this.overlay = OverlayTexture.NO_OVERLAY;
			this.pending = true;
			return this;
		}

		@Override
		public VertexConsumer setColor(int r, int g, int b, int a) {
			this.color = (a << 24) | (r << 16) | (g << 8) | b;
			return this;
		}

		@Override
		public VertexConsumer setColor(int color) {
			this.color = color;
			return this;
		}

		@Override
		public VertexConsumer setUv(float u, float v) {
			this.u = u;
			this.v = v;
			return this;
		}

		@Override
		public VertexConsumer setUv1(int u, int v) {
			this.overlay = (u & 0xFFFF) | (v << 16);
			return this;
		}

		@Override
		public VertexConsumer setUv2(int u, int v) {
			return this;
		}

		@Override
		public VertexConsumer setUv3(float u, float v) {
			return this;
		}

		@Override
		public VertexConsumer setNormal(float x, float y, float z) {
			return this;
		}

		@Override
		public VertexConsumer setLineWidth(float width) {
			return this;
		}
	}

	/** The batch for a render type's texture, sending the texture to Mycelium the first time. Null: can't show it. */
	private @Nullable Batch batchFor(RenderType renderType) {
		RenderTypeAccessor type = (RenderTypeAccessor) renderType;
		String name = type.myceliumcraft$name();
		if (name.contains("glint") || name.contains("outline") || name.contains("shadow")) {
			return null;
		}
		Object binding = ((RenderSetupAccessor) (Object) type.myceliumcraft$state()).myceliumcraft$textures().get("Sampler0");
		if (binding == null) {
			return null;
		}
		int id = textureId(((TextureBindingAccessor) binding).myceliumcraft$location());
		return id < 0 ? null : this.batches.computeIfAbsent(id, Batch::new);
	}

	private static int textureId(Identifier texture) {
		Integer known = TEXTURE_IDS.get(texture);
		if (known != null) {
			return known;
		}
		if (UNUSABLE.contains(texture)) {
			return -1;
		}
		NativeImage image = readTexture(texture);
		if (image == null) {
			UNUSABLE.add(texture);
			MycLink.LOG.info("MyceliumCraft: texture {} can't be sent to Mycelium; what uses it is left out", texture);
			return -1;
		}
		try (image) {
			int id = sendPixels(texture.toString(), image, image.getWidth(), image.getHeight());
			if (id < 0) {
				UNUSABLE.add(texture);
			} else {
				TEXTURE_IDS.put(texture, id);
			}
			return id;
		}
	}

	/** A held item's picture, sent the first time (its first animation frame). -1 if it can't be. */
	private static int spriteTextureId(TextureAtlasSprite sprite) {
		SpriteContents contents = sprite.contents();
		Integer known = SPRITE_IDS.get(contents);
		if (known != null) {
			return known;
		}
		NativeImage image = ((SpriteContentsAccessor) contents).myceliumcraft$originalImage();
		int w = Math.min(contents.width(), image.getWidth()), h = Math.min(contents.height(), image.getHeight());
		int id = sendPixels("sprite " + contents.name(), image, w, h);
		SPRITE_IDS.put(contents, id); // also remembers -1, so a failure is tried only once
		return id;
	}

	/** Send the top-left w x h pixels of an image as a new texture. Returns its id, or -1. */
	private static int sendPixels(String name, NativeImage image, int w, int h) {
		long bytes = (long) w * h * 4;
		int id = nextTextureId;
		if (id > Proto.T_MAX || nextTextureOffset + bytes > Proto.T_DATA_BYTES) {
			MycLink.LOG.info("MyceliumCraft: no room for texture {} ({}x{}) in the shared memory; left out", name, w, h);
			return -1;
		}
		byte[] pixels = new byte[(int) bytes];
		int i = 0;
		for (int y = 0; y < h; y++) {
			for (int x = 0; x < w; x++) {
				int argb = image.getPixel(x, y);
				pixels[i++] = (byte) (argb >> 16);
				pixels[i++] = (byte) (argb >> 8);
				pixels[i++] = (byte) argb;
				pixels[i++] = (byte) (argb >>> 24);
			}
		}
		if (!MycLink.writeTexture(id, w, h, nextTextureOffset, pixels)) {
			return -1;
		}
		nextTextureId++;
		nextTextureOffset += bytes;
		MycLink.LOG.info("MyceliumCraft: sent texture {} to Mycelium (id {}, {}x{})", name, id, w, h);
		return id;
	}

	/**
	 * One quad of a held item or block: positioned by the pose, its sheet coordinates turned into
	 * its sprite's own 0..1, and tinted (leather, potions, grass) as Minecraft tints it.
	 */
	private void addQuad(Matrix4f pose, BakedQuad quad, int[] tintLayers, int overlayCoords) {
		var material = quad.materialInfo();
		TextureAtlasSprite sprite = material.sprite();
		int id = spriteTextureId(sprite);
		if (id < 0) {
			return;
		}
		Batch batch = this.batches.computeIfAbsent(id, Batch::new);
		int layer = material.isTinted() ? material.tintIndex() : -1;
		int color = layer >= 0 && layer < tintLayers.length ? tintLayers[layer] : -1;
		if ((color >>> 24) == 0) {
			color |= 0xFF000000; // tints come without alpha
		}
		float u0 = sprite.getU0(), du = sprite.getU1() - u0, v0 = sprite.getV0(), dv = sprite.getV1() - v0;
		Vector3f p = new Vector3f();
		for (int k = 0; k < 4; k++) {
			pose.transformPosition(quad.position(k), p);
			long uv = quad.packedUV(k);
			float u = du != 0 ? (UVPair.unpackU(uv) - u0) / du : 0f;
			float v = dv != 0 ? (UVPair.unpackV(uv) - v0) / dv : 0f;
			batch.add(p.x(), p.y(), p.z(), u, v, color, overlayCoords);
		}
	}

	/** A texture's pixels: from resource packs, or a runtime texture (downloaded skins). Caller closes it. */
	private static @Nullable NativeImage readTexture(Identifier texture) {
		Minecraft minecraft = Minecraft.getInstance();
		var resource = minecraft.getResourceManager().getResource(texture);
		if (resource.isPresent()) {
			try (var in = resource.get().open()) {
				return NativeImage.read(in);
			} catch (java.io.IOException e) {
				MycLink.LOG.warn("MyceliumCraft: couldn't read {}", texture, e);
				return null;
			}
		}
		var registered = ((TextureManagerAccessor) minecraft.getTextureManager()).myceliumcraft$byPath().get(texture);
		if (registered instanceof DynamicTexture dynamic && dynamic.getPixels() != null) {
			NativeImage copy = new NativeImage(dynamic.getPixels().getWidth(), dynamic.getPixels().getHeight(), false);
			copy.copyFrom(dynamic.getPixels());
			return copy;
		}
		return null;
	}

	// ---- SubmitNodeCollector: models (body, armour, cape) and held items and blocks ----

	@Override
	public OrderedSubmitNodeCollector order(int order) {
		return this;
	}

	@Override
	public <S> void submitModel(Model<? super S> model, S state, PoseStack poseStack, RenderType renderType, int lightCoords, int overlayCoords,
		int tintedColor, @Nullable UvMapping uvMapping, int outlineColor) {
		Batch batch = this.batchFor(renderType);
		if (batch == null) {
			return;
		}
		this.capture.begin(batch);
		VertexConsumer buffer = uvMapping != null ? uvMapping.wrap(this.capture) : this.capture;
		model.setupAnim(state);
		model.renderToBuffer(poseStack, buffer, lightCoords, overlayCoords, tintedColor);
		this.capture.flush();
	}

	@Override
	public void submitItem(PoseStack poseStack, ItemDisplayContext displayContext, int lightCoords, int overlayCoords, int outlineColor, int[] tintLayers,
		ItemQuads quads, ItemStackRenderState.FoilType foilType) {
		this.capture.flush();
		Matrix4f pose = poseStack.last().pose();
		for (BakedQuad quad : quads.all()) {
			this.addQuad(pose, quad, tintLayers, overlayCoords);
		}
		if (itemLogs < 5) {
			itemLogs++;
			MycLink.LOG.info("MyceliumCraft: player model: held item ({}), {} quads", displayContext, quads.all().size());
		}
	}

	@Override
	public void submitItem(PoseStack poseStack, ItemDisplayContext displayContext, int lightCoords, int overlayCoords, int outlineColor, int[] tintLayers,
		ItemQuads quads, @Nullable MeshView mesh, ItemStackRenderState.FoilType foilType) {
		this.submitItem(poseStack, displayContext, lightCoords, overlayCoords, outlineColor, tintLayers, quads, foilType);
	}

	@Override
	public void submitBlockModel(PoseStack poseStack, RenderType renderType, List<BlockStateModelPart> parts, int[] tintLayers, int lightCoords, int overlayCoords,
		int outlineColor) {
		this.capture.flush();
		Matrix4f pose = poseStack.last().pose();
		for (BlockStateModelPart part : parts) {
			for (Direction face : FACES_AND_NONE) {
				for (BakedQuad quad : part.getQuads(face)) {
					this.addQuad(pose, quad, tintLayers, overlayCoords);
				}
			}
		}
	}

	@Override
	public void submitBlockModel(PoseStack poseStack, java.util.function.Function<ChunkSectionLayer, RenderType> renderTypes, boolean translucentLayer,
		List<BlockStateModelPart> parts, net.fabricmc.fabric.api.client.renderer.v1.mesh.@Nullable Mesh mesh, int[] tintLayers, int lightCoords, int overlayCoords,
		int outlineColor) {
		this.submitBlockModel(poseStack, (RenderType) null, parts, tintLayers, lightCoords, overlayCoords, outlineColor);
	}

	@Override
	public void submitBreakingBlockModel(PoseStack poseStack, List<BlockStateModelPart> parts, net.fabricmc.fabric.api.client.renderer.v1.mesh.@Nullable Mesh mesh,
		int progress, boolean isBlockTranslucent) {
	}

	@Override
	public void submitBreakingBlockModel(PoseStack poseStack, List<BlockStateModelPart> parts, int progress, boolean isBlockTranslucent) {
	}

	@Override
	public void submitMovingBlock(PoseStack poseStack, MovingBlockRenderState state, int outlineColor) {
	}

	@Override
	public void submitQuadParticleGroup(QuadParticleRenderState particles) {
	}

	@Override
	public void submitShadow(PoseStack poseStack, float radius, List<EntityRenderState.ShadowPiece> pieces) {
	}

	@Override
	public void submitNameTag(PoseStack poseStack, @Nullable Vec3 nameTagAttachment, int offset, Component name, boolean seeThrough, int lightCoords,
		CameraRenderState camera) {
	}

	@Override
	public void submitText(PoseStack poseStack, float x, float y, FormattedCharSequence string, boolean dropShadow, Font.DisplayMode displayMode, int lightCoords,
		int color, int backgroundColor, int outlineColor) {
	}

	@Override
	public void submitTextBackground(PoseStack poseStack, float x0, float y0, float x1, float y1, int color, Font.DisplayMode displayMode, int lightCoords) {
	}

	@Override
	public void submitFlame(PoseStack poseStack, EntityRenderState renderState, Quaternionf rotation) {
	}

	@Override
	public void submitLeash(PoseStack poseStack, EntityRenderState.LeashState leashState) {
	}

	@Override
	public <S> void submitCrumblingOverlay(Model<? super S> model, S state, PoseStack poseStack, RenderType renderType, int lightCoords, int overlayCoords,
		int tintedColor, ModelFeatureRenderer.CrumblingOverlay crumblingOverlay) {
	}

	@Override
	public void submitShapeOutline(PoseStack poseStack, VoxelShape shape, RenderType renderType, int color, float width, boolean afterTerrain) {
	}

	@Override
	public void submitCustomGeometry(PoseStack poseStack, RenderType renderType, SubmitNodeCollector.CustomGeometryRenderer customGeometryRenderer) {
	}

	@Override
	public void submitGizmoPrimitives(DrawableGizmoPrimitives.Group group, CameraRenderState camera, boolean onTop) {
	}
}
