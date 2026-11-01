using MyceliumCraft.Link;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Skills fire where you look (your report, 2026-10-25: Spineshot sometimes went off to the side).
    ///
    /// The spore faces where Minecraft's body faces (MinecraftDriver), and Minecraft's body only turns
    /// once the head is far enough to the side, or toward the walk when strafing. Mycelium's skills
    /// aim with their own object's rotation (Spineshot, Undergrowth, Unstable Puffball: the
    /// projectile gets the skill's transform.rotation; Death Blossom: in front of it; Mycotoxins:
    /// behind it), and those objects are children of the spore's "SkillLoadout". So while Minecraft
    /// drives, the SkillLoadout is turned to face Minecraft's look (head) yaw, every frame; the body
    /// and its animations stay as they are. Given back (no turn) when Minecraft doesn't drive.
    /// </summary>
    sealed class SkillAim
    {
        Transform loadout;
        bool turned;
        float nextLog;

        /// <summary>Every frame (Update and LateUpdate): face Minecraft's look yaw, or let go.</summary>
        public void Update(bool driving, float lookYawMc)
        {
            if (loadout == null)
            {
                GameObject player = GameObject.FindWithTag("currentPlayer");
                loadout = player != null ? player.transform.Find("SkillLoadout") : null;
                if (loadout == null) { turned = false; return; }
            }
            if (!driving)
            {
                if (turned)
                {
                    loadout.localRotation = Quaternion.identity;
                    turned = false;
                    McLog.Info("skill aim: Minecraft isn't driving; skills face the spore's way again");
                }
                return;
            }
            if (!turned) McLog.Info("skill aim: skills fire where you look (Minecraft's head yaw), not where the body faces");
            turned = true;
            loadout.rotation = Quaternion.Euler(0f, WorldMapping.MycYaw(lookYawMc), 0f);
            if (Time.realtimeSinceStartup >= nextLog)
            {
                nextLog = Time.realtimeSinceStartup + 10f;
                Transform body = loadout.parent;
                float off = body != null ? Mathf.DeltaAngle(body.eulerAngles.y, loadout.eulerAngles.y) : 0f;
                McLog.Info($"skill aim: skills face {loadout.eulerAngles.y:F0} deg (your look), the body {(body != null ? body.eulerAngles.y : 0f):F0} deg: {off:F0} deg apart");
            }
        }
    }
}
