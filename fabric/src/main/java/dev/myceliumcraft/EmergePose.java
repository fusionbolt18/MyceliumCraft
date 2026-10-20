package dev.myceliumcraft;

import net.minecraft.client.model.HumanoidModel;
import net.minecraft.client.model.geom.ModelPart;
import org.joml.Quaternionf;
import org.joml.Vector3f;

/**
 * Digging up out of the ground, for Mycelium's sprout in The Carcass (your idea, 2026-10-20: like
 * the Warden's emerge, with the arms pulling and pushing the body out).
 *
 * The first version played Minecraft's own Warden emerge. The Warden is about 3 blocks tall, so on
 * a player-sized body all its clawing happened with even the hands still underground, and its
 * torso leaned up to 70 degrees. This one is made for a player body (model pixels; the feet are at
 * 24 and that's the ground):
 *  - arms straight up, the hands and head break out (in Minecraft's proportions raised hands
 *    only reach 2 pixels above the head, so they come out together);
 *  - pull: each hand is planted on the ground in front, and stays there while the torso rises
 *    (its arm angle worked out every frame from the shoulder's height), the left a little after
 *    the right (clawing);
 *  - push: the arms push down beside the body to lift the hips out, then the legs step up;
 *  - the torso leans forward at most 0.35 rad (20 degrees).
 * All by how far through the sprout we are (0..1), smoothed between the key points.
 *
 * Part poses are added to the player's normal pose. In the player model every part hangs from the
 * root, so the head and arms are carried with the torso here (as if attached at the neck): move =
 * torso + torsoTurn x own move, turn = torsoTurn x own turn. Render thread only.
 */
public final class EmergePose {
	// Key points: { progress, value, progress, value, ... }
	/** How deep everything is (model pixels down). 42: even raised hands are under; 30: hands out, head still under. */
	private static final float[] DEPTH = { 0f, 42f, 0.08f, 36f, 0.18f, 28f, 0.35f, 20f, 0.55f, 11f, 0.75f, 4f, 0.9f, 1f, 1f, 0f };
	/** Torso lean forward (radians). */
	private static final float[] LEAN = { 0f, 0f, 0.2f, 0.15f, 0.45f, 0.35f, 0.7f, 0.25f, 0.9f, 0.05f, 1f, 0f };
	/** Arm swing when not planted (radians, about the shoulder; -2.9 is straight up, 0 hanging): up, then down to push. */
	private static final float[] RIGHT_ARM = { 0f, -2.9f, 0.15f, -2.9f, 0.25f, -2.0f, 0.5f, -0.6f, 0.62f, -0.3f, 0.8f, 0.1f, 1f, 0f };
	private static final float[] LEFT_ARM = { 0f, -2.9f, 0.2f, -2.9f, 0.3f, -2.0f, 0.55f, -0.6f, 0.67f, -0.3f, 0.85f, 0.1f, 1f, 0f };
	/** How much each hand is planted on the ground (1) instead of following the swing above (0). */
	private static final float[] RIGHT_PLANT = { 0f, 0f, 0.15f, 0f, 0.22f, 1f, 0.5f, 1f, 0.6f, 0f, 1f, 0f };
	private static final float[] LEFT_PLANT = { 0f, 0f, 0.2f, 0f, 0.27f, 1f, 0.55f, 1f, 0.65f, 0f, 1f, 0f };
	/** Where a planted hand is: just at the ground (the feet's height, 24), arm length 10 to the hand. */
	private static final float GROUND = 23.5f, ARM = 10f;
	/** Arms spread out to the sides (radians; the left arm mirrors it). */
	private static final float[] SPREAD = { 0f, 0.1f, 0.3f, 0.3f, 0.6f, 0.15f, 1f, 0f };
	/** Head nod (radians, on top of the torso's lean): a little up, then well down at the ground it pulls on (more: your note, 2026-10-20). */
	private static final float[] HEAD = { 0f, -0.1f, 0.15f, 0.1f, 0.4f, 0.55f, 0.7f, 0.35f, 1f, 0f };
	/** Legs stepping up out of the hole (radians forward), right then left. */
	private static final float[] RIGHT_LEG = { 0f, 0f, 0.7f, 0f, 0.8f, -0.7f, 0.9f, 0f, 1f, 0f };
	private static final float[] LEFT_LEG = { 0f, 0f, 0.82f, 0f, 0.92f, -0.5f, 1f, 0f };

	private static float capturing = Float.NaN;
	private static final Quaternionf TORSO_TURN = new Quaternionf();
	private static final Quaternionf TURN = new Quaternionf();
	private static final Vector3f MOVE = new Vector3f();
	private static final Vector3f ANGLES = new Vector3f();

	private EmergePose() {
	}

	/** How far through the emerge while MycPlayerModel captures your body; NaN otherwise (HumanoidEmergeMixin). */
	public static float capturing() {
		return capturing;
	}

	/** Set by MycPlayerModel around its capture: NaN = no emerge. */
	public static void setCapturing(float progress) {
		capturing = progress;
	}

	/** The value at progress p, eased (smoothstep) between key points. */
	static float at(float[] keys, float p) {
		if (p <= keys[0]) {
			return keys[1];
		}
		for (int i = 2; i < keys.length; i += 2) {
			if (p <= keys[i]) {
				float t = (p - keys[i - 2]) / (keys[i] - keys[i - 2]);
				t = t * t * (3f - 2f * t);
				return keys[i - 1] + (keys[i + 1] - keys[i - 1]) * t;
			}
		}
		return keys[keys.length - 1];
	}

	/** Pose a player-shaped model 'progress' (0..1) through the emerge, on top of its normal pose. */
	public static void apply(HumanoidModel<?> model, float progress) {
		float p = Math.max(0f, Math.min(1f, progress));
		float depth = at(DEPTH, p);
		float spread = at(SPREAD, p);
		ModelPart torso = model.body;
		// The head and arms relative to the torso, before the torso moves.
		float tx = torso.x, ty = torso.y, tz = torso.z;
		torso.y += depth;
		torso.xRot += at(LEAN, p);
		TORSO_TURN.rotationZYX(torso.zRot, torso.yRot, torso.xRot);
		carried(model.head, tx, ty, tz, torso, at(HEAD, p), 0f);
		float lean = torso.xRot;
		carried(model.rightArm, tx, ty, tz, torso, arm(model.rightArm, tx, ty, tz, torso, lean, at(RIGHT_ARM, p), at(RIGHT_PLANT, p)), spread);
		carried(model.leftArm, tx, ty, tz, torso, arm(model.leftArm, tx, ty, tz, torso, lean, at(LEFT_ARM, p), at(LEFT_PLANT, p)), -spread);
		model.rightLeg.y += depth;
		model.rightLeg.xRot += at(RIGHT_LEG, p);
		model.leftLeg.y += depth;
		model.leftLeg.xRot += at(LEFT_LEG, p);
	}

	/**
	 * An arm's extra swing: the keyed swing, blended (by 'plant') toward the swing that puts the hand
	 * on the ground in front, reaching forward from the shoulder: world angle = -acos((ground -
	 * shoulder) / arm), minus the torso's lean it's already carried by. Out of reach (the shoulder
	 * more than an arm's length above the ground): straight down, pushing.
	 */
	private static float arm(ModelPart part, float tx, float ty, float tz, ModelPart torso, float lean, float keyed, float plant) {
		if (plant <= 0f) {
			return keyed;
		}
		float shoulderY = TORSO_TURN.transform(part.x - tx, part.y - ty, part.z - tz, MOVE).y + torso.y;
		float c = Math.max(-1f, Math.min(1f, (GROUND - shoulderY) / ARM));
		float planted = -(float) Math.acos(c) - lean;
		return keyed + (planted - keyed) * plant;
	}

	/** A part carried by the torso: its own extra turn, then the torso's move and turn. */
	private static void carried(ModelPart part, float tx, float ty, float tz, ModelPart torso, float xTurn, float zTurn) {
		TORSO_TURN.transform(part.x - tx, part.y - ty, part.z - tz, MOVE).add(torso.x, torso.y, torso.z);
		TURN.set(TORSO_TURN).mul(new Quaternionf().rotationZYX(part.zRot + zTurn, part.yRot, part.xRot + xTurn));
		TURN.getEulerAnglesZYX(ANGLES);
		part.x = MOVE.x;
		part.y = MOVE.y;
		part.z = MOVE.z;
		part.xRot = ANGLES.x;
		part.yRot = ANGLES.y;
		part.zRot = ANGLES.z;
	}
}
