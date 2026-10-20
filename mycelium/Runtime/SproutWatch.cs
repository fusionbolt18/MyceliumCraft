using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MyceliumCraft
{
    /// <summary>
    /// Mycelium's sprout in The Carcass, for Minecraft's emerge (EmergePose.java).
    ///
    /// Every time the spore arrives in The Carcass (unless it died for good), Mycelium's GameManager
    /// takes the controls away, waits timeUntilHubSprout seconds (0.1 there), plays the spore's
    /// "Sprout" animation (a 3.58 s clip that runs in about 2.9 s), and gives the controls back when
    /// it's done. This tells Minecraft how far through that it is: 0 while waiting (the body stays
    /// buried), then the Sprout animation's progress up to 1 (Minecraft plays the whole Warden's
    /// emerge over it). -1 once it's over, or when the Sprout doesn't start within the wait + 3 s.
    /// </summary>
    sealed class SproutWatch
    {
        const string Carcass = "The Carcass";
        bool armed, sawSprout;
        float arrived, wait;
        Animator animator;
        float nextLog;

        /// <summary>A scene has loaded: arm on The Carcass when Mycelium will sprout the spore there.</summary>
        public void SceneLoaded(Scene scene)
        {
            armed = false;
            if (scene.name != Carcass) return;
            object permaDied = Type.GetType("GlobalData, Assembly-CSharp")?.GetField("sporePermaDied", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (permaDied != null)
            {
                McLog.Info($"sprout: '{permaDied}' died for good; Mycelium doesn't sprout, so no emerge");
                return;
            }
            wait = 10f;
            Type gm = Type.GetType("GameManager, Assembly-CSharp");
            var manager = gm != null ? UnityEngine.Object.FindObjectOfType(gm) : null;
            FieldInfo f = gm?.GetField("timeUntilHubSprout", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (manager != null && f != null) wait = (float)f.GetValue(manager);
            else McLog.Info("sprout: GameManager.timeUntilHubSprout not found; assuming 10 s");
            armed = true;
            sawSprout = false;
            arrived = Time.time;
            animator = null;
            McLog.Info($"sprout: arrived in The Carcass; Mycelium sprouts the spore in {wait:F1} s");
        }

        /// <summary>Every frame: how far through the sprout (0..1), or -1 when there's none.</summary>
        public float Update()
        {
            if (!armed) return -1f;
            if (animator == null)
            {
                GameObject player = GameObject.FindWithTag("currentPlayer");
                animator = player != null ? player.GetComponent<Animator>() : null;
            }
            float progress = 0f;
            if (animator != null && animator.isActiveAndEnabled && animator.GetCurrentAnimatorStateInfo(0).IsName("Sprout"))
            {
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                if (!sawSprout)
                {
                    sawSprout = true;
                    McLog.Info($"sprout: Mycelium's Sprout started {Time.time - arrived:F2} s after arriving ({state.length:F2} s long at its speed)");
                }
                if (state.normalizedTime >= 1f) return Done("the Sprout animation finished");
                progress = Mathf.Clamp01(state.normalizedTime);
            }
            else if (sawSprout)
            {
                return Done("the Sprout animation is over");
            }
            else if (Time.time - arrived > wait + 3f)
            {
                return Done("the Sprout never started");
            }
            if (Time.realtimeSinceStartup >= nextLog)
            {
                nextLog = Time.realtimeSinceStartup + 0.5f;
                McLog.Info($"sprout: {progress:F2} of the way ({Time.time - arrived:F2} s after arriving)");
            }
            return progress;
        }

        float Done(string why)
        {
            armed = false;
            McLog.Info($"sprout: done ({why}), {Time.time - arrived:F2} s after arriving");
            return -1f;
        }
    }
}
