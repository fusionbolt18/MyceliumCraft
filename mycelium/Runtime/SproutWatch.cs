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
    ///
    /// The Sprout plays at normal speed (your rule, 2026-10-25). Mycelium runs the spore's whole
    /// animator at its Speed level (CharacterStats: animator.speed = animatorSpeed), so a high Speed
    /// sped the Sprout up, and with it the emerge. The animator is held at 1 while the Sprout plays
    /// (GameManager gives the controls back when it ends, so that stays in step), then put back.
    /// Test aid "-mcSproutSpeed n": the animator at n before the Sprout, as a Speed level would.
    /// </summary>
    sealed class SproutWatch
    {
        const string Carcass = "The Carcass";
        bool armed, sawSprout;
        float arrived, wait;
        Animator animator;
        float nextLog;
        float savedSpeed = -1f; // the animator's speed before the Sprout (-1: not held)
        float testSpeed = -1f;

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
            savedSpeed = -1f;
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-mcSproutSpeed");
            testSpeed = i >= 0 && i + 1 < args.Length && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float n) ? n : -1f;
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
            if (testSpeed > 0f && animator != null && !sawSprout && animator.speed != testSpeed)
            {
                animator.speed = testSpeed;
                McLog.Info($"sprout: test: the spore's animator at x{testSpeed:F2}, as a Speed level would set it");
            }
            float progress = 0f;
            if (animator != null && animator.isActiveAndEnabled && animator.GetCurrentAnimatorStateInfo(0).IsName("Sprout"))
            {
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                if (!sawSprout)
                {
                    sawSprout = true;
                    savedSpeed = animator.speed;
                    animator.speed = 1f;
                    McLog.Info($"sprout: Mycelium's Sprout started {Time.time - arrived:F2} s after arriving ({state.length:F2} s long); animator held at x1 (was x{savedSpeed:F2} from Speed)");
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
            if (savedSpeed >= 0f && animator != null)
            {
                // Back to the Speed level's, unless something else set it meanwhile.
                if (animator.speed == 1f) animator.speed = savedSpeed;
                McLog.Info($"sprout: animator back to x{animator.speed:F2}");
            }
            savedSpeed = -1f;
            McLog.Info($"sprout: done ({why}), {Time.time - arrived:F2} s after arriving");
            return -1f;
        }
    }
}
