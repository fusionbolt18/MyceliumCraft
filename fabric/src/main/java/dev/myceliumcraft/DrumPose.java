package dev.myceliumcraft;

import net.minecraft.client.model.HumanoidModel;
import net.minecraft.client.model.geom.ModelPart;
import org.joml.Quaternionf;
import org.joml.Vector3f;

/**
 * A spore hitting the beetle drum (your request, 2026-11-09), as Mycelium's spore does it. Mycelium's
 * DrumCurio triggers "DrumHit" on the beat of The Carcass's music; the spore's Animator plays "Drum
 * Hit": its SmashAnim (the Smash weapons' attack) at double speed. The spore leans back with its
 * right arm raised up behind its head and its left reaching forward, holds that, then throws itself
 * forward and down onto the drum: the right arm swings over and down, the left sweeps out to the
 * side, the torso folds forward 75 degrees and a little to the left, the legs kick back and the hips
 * hop up. Mycelium's spore holds its Smash weapon then, and that's what hits the drum.
 *
 * Up to the slam (t 0.43) the keys are as measured. From there they're changed (your report,
 * 2026-11-09: it hit the drum with its face): without a weapon the folded body led with the head.
 * Now the right arm swings over the top (up, ahead, then ahead and down) and lands on the drum with
 * the hand, the body leans forward only 0.6 rad (35 degrees) and a little to the left, the legs stay
 * under it (0.3 rad back from the torso's line) and the hips hop half as high.
 *
 * The keys below were measured from Mycelium's own animation (-mcDrumTest: a spore's bones sampled
 * every 0.025 s), by how far through "Drum Hit" it is (Mycelium's normalised time, the same number
 * this gets from Colony drums):
 *  - each limb's direction (shoulder to hand, hip to foot) in the torso's frame, in Minecraft's
 *    model space (x = the body's left, y = down, z = back). Minecraft's arm and leg are turned the
 *    shortest way from hanging straight down to it; directions in between are blended, so the swing
 *    goes round, not through the body;
 *  - the head's nod against the torso (radians; + = down);
 *  - the torso's lean (radians; + = forward) and its lean to the side (+ = to the right), turning
 *    the whole body about the hips;
 *  - how far the hips rise, as a share of the spore's height (Minecraft's body is 32 model pixels).
 * The spore's arms hang out to the sides (about 47 degrees) where Minecraft's hang down, so at the
 * start and end of a hit the pose fades in from and out to Minecraft's own (WEIGHT), as Mycelium
 * blends into and out of it. Render thread only.
 */
public final class DrumPose {
	private static final int N = 17;
	// t, right arm (x, y, z), left arm, right leg, left leg, head nod, lean, side, lift
	private static final float[] KEYS = {
		0.037f, -0.53f, 0.79f, -0.3f, 0.56f, 0.83f, 0.05f, 0.26f, 0.89f, 0.38f, -0.28f, 0.94f, 0.17f, -0.03f, 0.03f, 0f, 0f,
		0.062f, -0.85f, 0.4f, 0.35f, 0.53f, 0.85f, -0.01f, 0.27f, 0.87f, 0.42f, -0.29f, 0.91f, 0.31f, 0.03f, -0.07f, 0f, 0f,
		0.086f, -0.55f, -0.39f, 0.74f, 0.47f, 0.85f, -0.24f, 0.29f, 0.73f, 0.62f, -0.25f, 0.88f, 0.41f, 0.08f, -0.26f, -0.02f, 0.002f,
		0.111f, -0.48f, -0.58f, 0.66f, 0.39f, 0.82f, -0.43f, 0.31f, 0.59f, 0.74f, -0.17f, 0.88f, 0.44f, 0.12f, -0.39f, -0.05f, 0.006f,
		0.148f, -0.5f, -0.63f, 0.59f, 0.26f, 0.74f, -0.61f, 0.32f, 0.47f, 0.82f, -0.06f, 0.89f, 0.45f, -0.23f, -0.5f, -0.1f, 0.011f,
		0.198f, -0.5f, -0.62f, 0.61f, 0.16f, 0.65f, -0.74f, 0.33f, 0.46f, 0.82f, -0.06f, 0.89f, 0.45f, -0.48f, -0.52f, -0.11f, 0.012f,
		0.298f, -0.5f, -0.61f, 0.61f, 0.13f, 0.57f, -0.81f, 0.33f, 0.45f, 0.83f, -0.12f, 0.88f, 0.46f, -0.61f, -0.52f, -0.11f, 0.012f,
		0.411f, -0.53f, -0.62f, 0.58f, 0.13f, 0.54f, -0.83f, 0.33f, 0.44f, 0.84f, -0.23f, 0.87f, 0.43f, -0.63f, -0.52f, -0.12f, 0.012f,
		0.424f, -0.55f, -0.6f, 0.58f, 0.13f, 0.53f, -0.84f, 0.34f, 0.44f, 0.83f, -0.24f, 0.88f, 0.41f, -0.62f, -0.51f, -0.12f, 0.011f,
		0.436f, -0.4f, -0.9f, 0.15f, 0.2f, 0.45f, -0.87f, 0.34f, 0.54f, 0.77f, -0.27f, 0.95f, 0.15f, -0.63f, -0.43f, -0.11f, 0.008f,
		0.449f, -0.3f, -0.75f, -0.6f, 0.39f, 0.28f, -0.88f, 0.3f, 0.75f, 0.55f, -0.15f, 0.95f, -0.25f, -0.6f, -0.26f, -0.12f, 0.003f,
		0.461f, -0.25f, -0.1f, -0.95f, 0.67f, 0.05f, -0.74f, 0.15f, 0.9f, 0.3f, -0.05f, 0.95f, -0.3f, -0.55f, -0.04f, -0.15f, 0.002f,
		0.474f, -0.2f, 0.4f, -0.9f, 0.92f, -0.11f, -0.37f, 0f, 0.95f, -0.3f, 0f, 0.95f, -0.3f, -0.5f, 0.25f, -0.2f, 0.01f,
		0.486f, -0.2f, 0.45f, -0.87f, 1f, -0.08f, 0f, 0f, 0.95f, -0.3f, 0f, 0.95f, -0.3f, -0.5f, 0.45f, -0.25f, 0.03f,
		0.511f, -0.2f, 0.45f, -0.87f, 0.97f, 0.18f, 0.18f, 0f, 0.95f, -0.3f, 0f, 0.95f, -0.3f, -0.5f, 0.6f, -0.3f, 0.05f,
		0.561f, -0.2f, 0.45f, -0.87f, 0.9f, 0.43f, 0.08f, 0f, 0.95f, -0.3f, 0f, 0.95f, -0.3f, -0.5f, 0.6f, -0.3f, 0.05f,
	};
	/** How much of the pose shows over Minecraft's own: { t, weight, ... }. Mycelium blends back to its idle from about 0.51 to 0.74. */
	private static final float[] WEIGHT = { 0.03f, 0f, 0.086f, 1f, 0.55f, 1f, 0.74f, 0f };
	/** The hips, where the body turns when it leans (model pixels); the body is 32 tall. */
	private static final float HIP_Y = 12f, HEIGHT = 32f;

	private static float capturing = Float.NaN;
	private static final float[] AT = new float[N];
	private static final Quaternionf FIGURE = new Quaternionf();
	private static final Quaternionf OWN = new Quaternionf();
	private static final Quaternionf NORMAL = new Quaternionf();
	private static final Vector3f MOVE = new Vector3f();
	private static final Vector3f ANGLES = new Vector3f();

	private DrumPose() {
	}

	/** How far through the drum hit while MycSporeBodies captures a spore's body; NaN otherwise (HumanoidEmergeMixin). */
	public static float capturing() {
		return capturing;
	}

	public static void setCapturing(float progress) {
		capturing = progress;
	}

	/** The keys at t, blended in a straight line between the two around it. */
	private static void sample(float t) {
		int rows = KEYS.length / N;
		if (t <= KEYS[0]) {
			System.arraycopy(KEYS, 0, AT, 0, N);
			return;
		}
		for (int r = 1; r < rows; r++) {
			if (t <= KEYS[r * N]) {
				float a = (t - KEYS[(r - 1) * N]) / (KEYS[r * N] - KEYS[(r - 1) * N]);
				for (int k = 0; k < N; k++) {
					AT[k] = KEYS[(r - 1) * N + k] + (KEYS[r * N + k] - KEYS[(r - 1) * N + k]) * a;
				}
				return;
			}
		}
		System.arraycopy(KEYS, (rows - 1) * N, AT, 0, N);
	}

	/** Pose a player-shaped model 't' (Mycelium's "Drum Hit" time, 0..1) through a drum hit, over its normal pose. */
	public static void apply(HumanoidModel<?> model, float t) {
		float w = EmergePose.at(WEIGHT, t);
		if (w <= 0f) {
			return;
		}
		sample(t);
		float lift = AT[16] * HEIGHT * w;
		FIGURE.rotationZYX(-AT[15] * w, 0f, AT[14] * w);
		turned(model.body, w, null);
		OWN.rotationX(AT[13]);
		turned(model.head, w, OWN);
		limb(model.rightArm, w, 1, lift);
		limb(model.leftArm, w, 4, lift);
		limb(model.rightLeg, w, 7, lift);
		limb(model.leftLeg, w, 10, lift);
		model.body.y -= lift;
		model.head.y -= lift;
	}

	/** An arm or leg: turned the shortest way from hanging down to its keyed direction (at AT[k..k+2]). */
	private static void limb(ModelPart part, float w, int k, float lift) {
		float x = AT[k], y = AT[k + 1], z = AT[k + 2];
		float length = (float) Math.sqrt(x * x + y * y + z * z);
		if (length < 1e-4f) {
			OWN.identity();
		} else {
			OWN.rotationTo(0f, 1f, 0f, x / length, y / length, z / length);
		}
		turned(part, w, OWN);
		part.y -= lift;
	}

	/**
	 * A part carried by the whole body's lean about the hips: its own turn (own; null = its normal
	 * one) blended in by w from Minecraft's, then the lean (already scaled by w).
	 */
	private static void turned(ModelPart part, float w, Quaternionf own) {
		NORMAL.rotationZYX(part.zRot, part.yRot, part.xRot);
		if (own != null) {
			NORMAL.slerp(own, w);
		}
		FIGURE.transform(part.x, part.y - HIP_Y, part.z, MOVE);
		part.x = MOVE.x;
		part.y = MOVE.y + HIP_Y;
		part.z = MOVE.z;
		new Quaternionf(FIGURE).mul(NORMAL).getEulerAnglesZYX(ANGLES);
		part.xRot = ANGLES.x;
		part.yRot = ANGLES.y;
		part.zRot = ANGLES.z;
	}
}
