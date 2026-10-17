using Cinemachine;
using UnityEngine;
using UnityEngine.Scripting;

namespace MyceliumCraft
{
    /// <summary>
    /// Records what Mycelium's camera shake added to the camera this frame, so CameraSync can keep
    /// it after overriding the camera.
    ///
    /// Mycelium shakes the camera with Cinemachine impulses: a CinemachineImpulseListener on the
    /// main FreeLook adds a position and rotation "correction" right after the Noise stage, including
    /// its secondary-noise reaction, which has internal timing that can't be recomputed outside the
    /// listener. This extension is added to the same FreeLook after the listener, so for every stage
    /// it runs after it: it notes the corrections when Aim is done and again when Noise is done,
    /// and the difference is the shake (plus any rig noise).
    /// </summary>
    [Preserve]
    public sealed class ShakeTap : CinemachineExtension
    {
        Vector3 posAfterAim;
        Quaternion rotAfterAim = Quaternion.identity;

        /// <summary>Shake position offset this frame (world space, as the listener applied it).</summary>
        public Vector3 Position { get; private set; }

        /// <summary>
        /// Position corrections made before the shake this frame: Cinemachine's collision add-on
        /// (CinemachineCollider) pushing the camera out of the way of level geometry. For the trace.
        /// </summary>
        public Vector3 CollisionPush { get; private set; }

        /// <summary>Shake rotation this frame (applied after the camera's own orientation).</summary>
        public Quaternion Rotation { get; private set; } = Quaternion.identity;

        protected override void PostPipelineStageCallback(
            CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
        {
            if (stage == CinemachineCore.Stage.Aim)
            {
                posAfterAim = state.PositionCorrection;
                rotAfterAim = state.OrientationCorrection;
                CollisionPush = state.PositionCorrection;
            }
            else if (stage == CinemachineCore.Stage.Noise)
            {
                Position = state.PositionCorrection - posAfterAim;
                Rotation = Quaternion.Inverse(rotAfterAim) * state.OrientationCorrection;
            }
        }
    }
}
