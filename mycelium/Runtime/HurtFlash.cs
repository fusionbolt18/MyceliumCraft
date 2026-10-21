using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Minecraft's red hurt flash on Mycelium's enemies: for half a second (Minecraft's 10 ticks)
    /// after an enemy loses health, its model is drawn again on top of itself in see-through light
    /// red (Resources/MyceliumCraftHurtTint.shader, added as an extra material on its renderers).
    /// Taking it away removes only that material, so whatever Mycelium changes meanwhile (a death
    /// effect) stays.
    ///
    /// A poisoned enemy (Mycelium's Poison effect on it) flashes green instead, the same way (your
    /// choice, 2026-10-21). Poison hurts every 0.5 s for about 5 s, so it stays green while poisoned.
    /// </summary>
    sealed class HurtFlash
    {
        const float Seconds = 0.5f;
        Material tint, poisonTint;
        bool failed;
        Type poisonType;
        bool poisonLooked;
        readonly Dictionary<Component, (Renderer[] renderers, float until, Material material)> flashing = new Dictionary<Component, (Renderer[], float, Material)>();
        readonly List<Component> done = new List<Component>();
        int flashes, greenFlashes;
        /// <summary>For the -mcFlashCheck test aid: the last enemy that started flashing, and its renderers' bounds.</summary>
        public Component LastFlashed;
        public Bounds LastBounds;
        float nextLog;

        /// <summary>Flash this enemy (again, if it's already flashing): green if it's poisoned, else red.</summary>
        public void Flash(Component enemy)
        {
            if (failed || enemy == null) return;
            if (tint == null)
            {
                Shader shader = Resources.Load<Shader>("MyceliumCraftHurtTint");
                if (shader == null || !shader.isSupported)
                {
                    failed = true;
                    McLog.Info("hurt flash: tint shader missing; no red flash on enemies");
                    return;
                }
                tint = new Material(shader) { name = "MyceliumCraft hurt tint" };
                // Green: the red tint's numbers with red and green swapped (same see-through look).
                poisonTint = new Material(shader) { name = "MyceliumCraft poison tint" };
                poisonTint.SetColor("_Multiply", new Color(0.72f, 1f, 0.72f, 1f));
                poisonTint.SetColor("_Add", new Color(0.03f, 0.12f, 0.03f, 1f));
                McLog.Info($"hurt flash: tint ready, {tint.passCount} passes (red, and green for poisoned enemies)");
            }
            Material want = Poisoned(enemy) ? poisonTint : tint;
            float until = Time.time + Seconds;
            if (flashing.TryGetValue(enemy, out var f))
            {
                if (f.material != want)
                {
                    // Poisoned (or cured) meanwhile: swap the colour.
                    foreach (Renderer r in f.renderers)
                    {
                        if (r == null) continue;
                        r.sharedMaterials = r.sharedMaterials.Select(m => m == f.material ? want : m).ToArray();
                    }
                }
                flashing[enemy] = (f.renderers, until, want);
                if (want == poisonTint) greenFlashes++;
                return;
            }
            Renderer[] renderers = enemy.GetComponentsInChildren<Renderer>()
                .Where(r => r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer)).ToArray();
            foreach (Renderer r in renderers)
            {
                var mats = r.sharedMaterials;
                if (mats.Contains(tint) || mats.Contains(poisonTint)) continue;
                r.sharedMaterials = mats.Append(want).ToArray();
            }
            flashing[enemy] = (renderers, until, want);
            flashes++;
            if (want == poisonTint) greenFlashes++;
            LastFlashed = enemy;
            if (renderers.Length > 0)
            {
                LastBounds = renderers[0].bounds;
                foreach (Renderer r in renderers) LastBounds.Encapsulate(r.bounds);
            }
        }

        /// <summary>Has this enemy got Mycelium's Poison effect on it (a component on its object)?</summary>
        bool Poisoned(Component enemy)
        {
            if (!poisonLooked)
            {
                poisonLooked = true;
                poisonType = Type.GetType("Poison, Assembly-CSharp");
                if (poisonType == null) McLog.Info("hurt flash: Mycelium's Poison not found; poisoned enemies flash red too");
            }
            return poisonType != null && enemy.GetComponent(poisonType) is Behaviour b && b.enabled;
        }

        /// <summary>Every frame: take the tint off enemies whose flash is over (or that are gone).</summary>
        public void Update()
        {
            done.Clear();
            foreach (var pair in flashing)
                if (pair.Key == null || Time.time >= pair.Value.until) done.Add(pair.Key);
            foreach (Component enemy in done)
            {
                foreach (Renderer r in flashing[enemy].renderers)
                {
                    if (r == null) continue;
                    var mats = r.sharedMaterials;
                    if (mats.Contains(tint) || mats.Contains(poisonTint)) r.sharedMaterials = mats.Where(m => m != tint && m != poisonTint).ToArray();
                }
                flashing.Remove(enemy);
            }
            if (flashes > 0 && Time.realtimeSinceStartup >= nextLog)
            {
                nextLog = Time.realtimeSinceStartup + 1f;
                McLog.Info($"hurt flash: {flashes} enemy flashes started in the last second; {greenFlashes} flashes or re-flashes green (poisoned)");
                flashes = 0;
                greenFlashes = 0;
            }
        }
    }
}
