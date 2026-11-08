using System;
using System.Reflection;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Living Cyclone with no weapon, as in The Carcass (your report, 2026-11-08: it didn't do what
    /// it does in a run, and couldn't be used again while you stayed there).
    ///
    /// Mycelium's LivingCyclone holds its cooldown back while it spins (StartCooldown only sets
    /// canSkill off), and starts it after the third spin (ActualCooldownStart). But it does that only
    /// when you hold a weapon, so with none the skill stays off until the scene changes. It's also only
    /// the weapon (WeaponCollision.isCycloning) that tells this mod a cyclone spins, which is what
    /// sends Minecraft to third person and plays the spin on your Minecraft body.
    ///
    /// So, with no weapon: from the skill being used (canSkill off, no cooldown running) until its
    /// spins end counts as spinning, and then its cooldown is started, as it would be with a weapon.
    /// The spins are while the spore's animator is stopped (LivingCyclone sets its speed to 0 for
    /// them, and back after). PlayerController.looking wasn't usable: it went on and off during the
    /// spins in the test. If the spins never seem to start, 6 s after it was used counts as the end.
    ///
    /// The other spores use their skills too, on their own, as moves (IdleWalking, SkillCurio). If
    /// you take one over while its Living Cyclone is on (in the shop, say), that isn't your spin: it
    /// counted as play, so the overlay showed over the shop until the move ended (your report,
    /// 2026-11-08). So it counts as spinning only if this mod saw the skill ready while the spore was
    /// yours. One that was already on when the spore became yours still gets its cooldown started
    /// once its spins end, so it isn't stuck, but it doesn't count as spinning.
    /// </summary>
    sealed class UnarmedCyclone
    {
        const float GiveUp = 6f;

        Type skillType;
        Component skill;
        GameObject skillOwner;
        FieldInfo canSkillField, cooldownField;
        float nextLook, since;
        bool active, sawFrozen;
        bool seenReady; // the skill was ready (canSkill on) while this spore was yours
        bool yours;     // the use now going on is yours, not a move the spore made on its own

        /// <summary>Living Cyclone spins with no weapon, used by you (part of "cycloning" for the rest of the mod).</summary>
        public bool Spinning => active && yours;

        /// <summary>Every frame.</summary>
        public void Update()
        {
            GameObject player = GameObject.FindWithTag("currentPlayer");
            if (player == null) { End(null, "no spore"); return; }
            if (skill == null || skillOwner != player)
            {
                skill = null;
                active = false;
                if (Time.realtimeSinceStartup < nextLook) return;
                nextLook = Time.realtimeSinceStartup + 1f;
                if (!Find(player)) return;
            }
            if (!(canSkillField.GetValue(skill) is bool canSkill)) return;
            bool cooling = cooldownField.GetValue(skill) != null;
            Animator animator = player.GetComponent<Animator>();
            bool frozen = animator != null && animator.speed == 0f;

            if (!active)
            {
                if (canSkill) seenReady = true;
                // Used with no weapon: canSkill off and no cooldown yet.
                if (!canSkill && !cooling && GameObject.FindWithTag("currentWeapon") == null)
                {
                    active = true;
                    yours = seenReady;
                    sawFrozen = false;
                    since = Time.time;
                    McLog.Info(yours ? "cyclone: Living Cyclone used with no weapon; it counts as spinning until its spins end"
                                     : $"cyclone: '{player.name}' was already using Living Cyclone on its own when it became yours (a move of its own); not your spin, so not counted as play. Its cooldown starts once its spins end");
                }
                return;
            }
            if (canSkill || cooling) { End(null, "Mycelium started its cooldown itself"); return; }
            if (frozen) sawFrozen = true;
            if (sawFrozen && !frozen) End(skill, $"its spins ended after {Time.time - since:F2} s");
            else if (Time.time - since > GiveUp) End(skill, $"no spins seen in {GiveUp:F0} s");
        }

        void End(Component startCooldown, string why)
        {
            if (!active) return;
            active = false;
            if (startCooldown == null) { McLog.Info($"cyclone: no-weapon Living Cyclone over: {why}"); return; }
            seenReady = false; // ready again only once its cooldown is over
            try
            {
                startCooldown.GetType().GetMethod("ActualCooldownStart", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(startCooldown, null);
                McLog.Info($"cyclone: no-weapon Living Cyclone over: {why}; its cooldown started, as with a weapon (cooling now: {cooldownField.GetValue(startCooldown) != null})");
            }
            catch (Exception e)
            {
                McLog.Info($"cyclone: starting Living Cyclone's cooldown failed: {(e.InnerException ?? e).Message}");
            }
        }

        bool Find(GameObject player)
        {
            if (skillType == null)
            {
                skillType = Type.GetType("LivingCyclone, Assembly-CSharp");
                Type baseType = Type.GetType("Skill, Assembly-CSharp");
                if (skillType == null || baseType == null) return false;
                canSkillField = baseType.GetField("canSkill", BindingFlags.Public | BindingFlags.Instance);
                cooldownField = baseType.GetField("cooldownCoroutine", BindingFlags.Public | BindingFlags.Instance);
            }
            if (canSkillField == null || cooldownField == null) return false;
            // The loadout's skills are on the spore (SkillLoadout); Living Cyclone only when equipped.
            Component c = player.GetComponentInChildren(skillType, true);
            if (c == null) return false;
            skill = c;
            skillOwner = player;
            seenReady = false;
            McLog.Info($"cyclone: found Living Cyclone on '{player.name}' ('{c.gameObject.name}')");
            return true;
        }

        /// <summary>For the test aid.</summary>
        public Component Skill => skill;
    }
}
