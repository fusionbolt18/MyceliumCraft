using Cinemachine;
using MyceliumCraft.Link;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Points Mycelium's camera where Minecraft's player looks.
    ///
    /// Mycelium's main camera is a Cinemachine FreeLook (an orbit camera around the spore) on the
    /// object tagged "MainCamera", in World Space binding: its X axis is a world heading
    /// (-180..180), its Y axis (0..1) moves it between a low, a middle and a high orbit. While
    /// Minecraft drives the spore, the X axis follows Minecraft's yaw and the Y axis follows
    /// Minecraft's pitch, and Mycelium's own mouse input to the camera is switched off (restored
    /// afterwards). Cutscene and "reset/navigate" cameras still take over as they normally do.
    /// </summary>
    sealed class CameraSync
    {
        CinemachineFreeLook freeLook;
        string savedXInput, savedYInput;
        bool controlling;
        // Downward look angle (degrees) of the low, middle and high orbit, for pitch -> Y.
        float angleLow, angleMid, angleHigh;

        public float X => freeLook != null ? freeLook.m_XAxis.Value : float.NaN;
        public float Y => freeLook != null ? freeLook.m_YAxis.Value : float.NaN;
        public bool Controlling => controlling;

        /// <summary>Forget the camera (new scene).</summary>
        public void Reset()
        {
            freeLook = null;
            controlling = false;
        }

        /// <summary>Call every frame. driving: Minecraft is driving the spore and mc is fresh.</summary>
        public void Update(bool driving, in McState mc)
        {
            if (!McConfig.SyncCamera) driving = false;
            if (freeLook == null && driving) Find();
            if (freeLook == null) return;

            if (!driving)
            {
                if (controlling) Release("Minecraft not driving");
                return;
            }
            if (!controlling)
            {
                savedXInput = freeLook.m_XAxis.m_InputAxisName;
                savedYInput = freeLook.m_YAxis.m_InputAxisName;
                freeLook.m_XAxis.m_InputAxisName = "";
                freeLook.m_YAxis.m_InputAxisName = "";
                freeLook.m_XAxis.m_InputAxisValue = 0;
                freeLook.m_YAxis.m_InputAxisValue = 0;
                controlling = true;
                McLog.Info($"camera: following Minecraft's look (Mycelium mouse input to the camera off; was '{savedXInput}'/'{savedYInput}')");
            }

            float heading = WorldMapping.MycYaw(mc.Yaw);
            if (heading > 180f) heading -= 360f;
            freeLook.m_XAxis.Value = heading;
            freeLook.m_YAxis.Value = PitchToY(mc.Pitch + McConfig.CameraPitchOffset);
        }

        /// <summary>Minecraft pitch (degrees, positive = looking down) to the FreeLook Y axis, piecewise linear over the orbits.</summary>
        public float PitchToY(float angle)
        {
            if (angle <= angleLow) return 0f;
            if (angle >= angleHigh) return 1f;
            if (angle <= angleMid) return 0.5f * Mathf.InverseLerp(angleLow, angleMid, angle);
            return 0.5f + 0.5f * Mathf.InverseLerp(angleMid, angleHigh, angle);
        }

        void Find()
        {
            GameObject main = GameObject.FindWithTag("MainCamera");
            freeLook = main != null ? main.GetComponent<CinemachineFreeLook>() : null;
            if (freeLook == null)
            {
                McLog.Info("camera: no CinemachineFreeLook on the MainCamera object; camera not synced");
                return;
            }
            var o = freeLook.m_Orbits;
            angleLow = Mathf.Atan2(o[2].m_Height, o[2].m_Radius) * Mathf.Rad2Deg;
            angleMid = Mathf.Atan2(o[1].m_Height, o[1].m_Radius) * Mathf.Rad2Deg;
            angleHigh = Mathf.Atan2(o[0].m_Height, o[0].m_Radius) * Mathf.Rad2Deg;
            McLog.Info($"camera: found FreeLook on '{main.name}' (binding {freeLook.m_BindingMode}); orbits look down {angleLow:F1}/{angleMid:F1}/{angleHigh:F1} degrees; " +
                       $"pitch offset {McConfig.CameraPitchOffset:F1}, so Minecraft pitch {angleLow - McConfig.CameraPitchOffset:F1}..{angleHigh - McConfig.CameraPitchOffset:F1} covers low..high");
            if (freeLook.m_BindingMode != CinemachineTransposer.BindingMode.WorldSpace)
                McLog.Info("camera: WARNING binding is not WorldSpace; the X axis is not a world heading, so the camera direction will be off");
        }

        void Release(string why)
        {
            if (freeLook != null)
            {
                freeLook.m_XAxis.m_InputAxisName = savedXInput;
                freeLook.m_YAxis.m_InputAxisName = savedYInput;
            }
            controlling = false;
            McLog.Info($"camera: back to Mycelium's mouse ({why})");
        }
    }
}
