package dev.myceliumcraft;

import com.mojang.blaze3d.platform.NativeImage;
import com.mojang.blaze3d.vertex.PoseStack;
import com.mojang.blaze3d.vertex.VertexConsumer;
import dev.myceliumcraft.link.MycLink;
import dev.myceliumcraft.link.Proto;
import dev.myceliumcraft.mixin.RenderSetupAccessor;
import dev.myceliumcraft.mixin.RenderTypeAccessor;
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
import net.minecraft.client.resources.model.geometry.ItemQuads;
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
 * Adapted from the player part of SkyCraft's AvatarExporter (MIT, chasmlol). Not yet: held items
 * and blocks (they're drawn from Minecraft's texture atlases), glint, name tags, shadows.
 * Render thread only.
 */
public final class MycPlayerModel implements SubmitNodeCollector {
	private static final MycPlayerModel INSTANCE = new MycPlayerModel();

	private static final Map<Identifier, Integer> TEXTURE_IDS = new HashMap<>();
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
		UNUSABLE.clear();
		nextTextureId = 1;
		nextTextureOffset = 0;
		shown = false;
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

	private void capture(Minecraft minecraft, Camera camera, float partialTick) {
		for (Batch b : this.batches.values()) {
			b.count = 0;
		}
		try {
			var dispatcher = minecraft.getEntityRenderDispatcher();
			dispatcher.prepare(camera, minecraft.crosshairPickEntity);
			EntityRenderState state = dispatcher.extractEntity(minecraft.player, partialTick);
			state.nameTag = null;
			CameraRenderState cameraState = minecraft.gameRenderer.gameRenderState().levelRenderState.cameraRenderState;
			// At the origin: positions come out relative to the player's feet.
			dispatcher.submit(state, cameraState, 0.0, 0.0, 0.0, new PoseStack(), this);
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
			int w = image.getWidth(), h = image.getHeight();
			long bytes = (long) w * h * 4;
			int id = nextTextureId;
			if (id > Proto.T_MAX || nextTextureOffset + bytes > Proto.T_DATA_BYTES) {
				UNUSABLE.add(texture);
				MycLink.LOG.info("MyceliumCraft: no room for texture {} ({}x{}) in the shared memory; left out", texture, w, h);
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
			TEXTURE_IDS.put(texture, id);
			MycLink.LOG.info("MyceliumCraft: sent texture {} to Mycelium (id {}, {}x{})", texture, id, w, h);
			return id;
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

	// ---- SubmitNodeCollector: only models (body, armour, cape) are taken for now ----

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
	}

	@Override
	public void submitItem(PoseStack poseStack, ItemDisplayContext displayContext, int lightCoords, int overlayCoords, int outlineColor, int[] tintLayers,
		ItemQuads quads, @Nullable MeshView mesh, ItemStackRenderState.FoilType foilType) {
	}

	@Override
	public void submitBlockModel(PoseStack poseStack, RenderType renderType, List<BlockStateModelPart> parts, int[] tintLayers, int lightCoords, int overlayCoords,
		int outlineColor) {
	}

	@Override
	public void submitBlockModel(PoseStack poseStack, java.util.function.Function<ChunkSectionLayer, RenderType> renderTypes, boolean translucentLayer,
		List<BlockStateModelPart> parts, net.fabricmc.fabric.api.client.renderer.v1.mesh.@Nullable Mesh mesh, int[] tintLayers, int lightCoords, int overlayCoords,
		int outlineColor) {
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
