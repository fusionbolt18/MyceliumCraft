using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Living Cyclone lasts the same at any Speed (your request, 2026-11-10: it ended too quickly with
    /// a higher Speed stat).
    ///
    /// Mycelium's LivingCyclone does 3 spins, each baseSpinDuration / (animatorSpeed x
    /// speedAnimationScalar) long (0.5 / (animatorSpeed x 0.75) in its prefab). The animator speed
    /// is attackSpeedBase + (Speed level - 1) x attackSpeedIncrement (1 + 0.071 a level, on
    /// PlayerParent's SporeAttributeRanges), so the spin got shorter as Speed went up: 2.0 s at level
    /// 1, about 1.0 s at level 15. Its skill bar timer always counts baseSpinDuration x 3.
    ///
    /// Mycelium's code isn't changed: every frame each LivingCyclone's speedAnimationScalar is set to
    /// its own value x attackSpeedBase / the spore's animatorSpeed, so the spin is as long as at Speed
    /// level 1 (2.0 s) whatever the Speed. It's read when each spin starts, so a change of Speed
    /// mid-cyclone shows from the next spin.
    /// </summary>
    sealed class CycloneLength
    {
        static readonly Type CycloneType = Type.GetType("LivingCyclone, Assembly-CSharp");
        static readonly FieldInfo ScalarField = CycloneType?.GetField("speedAnimationScalar", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo StatsField = CycloneType?.GetField("characterStats", BindingFlags.Public | BindingFlags.Instance);
        static readonly Type RangesType = Type.GetType("SporeAttributeRanges, Assembly-CSharp");
        static readonly FieldInfo BaseField = RangesType?.GetField("attackSpeedBase");
        static readonly Type StatsType = Type.GetType("CharacterStats, Assembly-CSharp");
        static readonly FieldInfo AnimSpeedField = StatsType?.GetField("animatorSpeed");

        readonly Dictionary<Component, float> original = new Dictionary<Component, float>();
        Component[] cyclones = new Component[0];
        float nextFind;
        bool looked;
        string logged = "";

        /// <summary>Every frame.</summary>
        public void Update()
        {
            if (!looked)
            {
                looked = true;
                McLog.Info($"cyclone length: LivingCyclone {CycloneType != null}, its speedAnimationScalar {ScalarField != null}, characterStats {StatsField != null}, attackSpeedBase {BaseField != null}, animatorSpeed {AnimSpeedField != null}");
            }
            if (ScalarField == null || StatsField == null || BaseField == null || AnimSpeedField == null) return;
            if (Time.unscaledTime >= nextFind)
            {
                nextFind = Time.unscaledTime + 1f;
                cyclones = Array.ConvertAll(UnityEngine.Object.FindObjectsOfType(CycloneType), o => (Component)o);
            }
            Component ranges = null;
            foreach (Component c in cyclones)
            {
                if (c == null) continue;
                var stats = StatsField.GetValue(c) as Component;
                if (stats == null) continue;
                if (ranges == null)
                {
                    GameObject parent = GameObject.FindWithTag("PlayerParent");
                    ranges = parent != null && RangesType != null ? parent.GetComponent(RangesType) : null;
                    if (ranges == null) return;
                }
                float baseSpeed = (float)BaseField.GetValue(ranges);
                float animSpeed = (float)AnimSpeedField.GetValue(stats);
                if (baseSpeed <= 0f || animSpeed <= 0f) continue;
                if (!original.TryGetValue(c, out float own))
                {
                    own = (float)ScalarField.GetValue(c);
                    original[c] = own;
                }
                float want = own * baseSpeed / animSpeed;
                if (Mathf.Abs((float)ScalarField.GetValue(c) - want) > 1e-5f)
                {
                    ScalarField.SetValue(c, want);
                    string line = $"animator speed {animSpeed:F3} (base {baseSpeed:F3}): spin scalar {own:F3} -> {want:F3}, so Living Cyclone lasts as at Speed level 1";
                    if (line != logged)
                    {
                        logged = line;
                        McLog.Info($"cyclone length: {line}");
                    }
                }
            }
        }
    }
}
