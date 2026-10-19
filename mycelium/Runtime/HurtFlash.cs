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
    /// </summary>
    sealed class HurtFlash
    {
        const float Seconds = 0.5f;
        Material tint;
        bool failed;
        readonly Dictionary<Component, (Renderer[] renderers, float until)> flashing = new Dictionary<Component, (Renderer[], float)>();
        readonly List<Component> done = new List<Component>();
        int flashes;
        /// <summary>For the -mcFlashCheck test aid: the last enemy that started flashing, and its renderers' bounds.</summary>
        public Component LastFlashed;
        public Bounds LastBounds;
        float nextLog;

        /// <summary>Flash this enemy (again, if it's already flashing).</summary>
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
                McLog.Info($"hurt flash: tint ready, {tint.passCount} passes");
            }
            float until = Time.time + Seconds;
            if (flashing.TryGetValue(enemy, out var f))
            {
                flashing[enemy] = (f.renderers, until);
                return;
            }
            Renderer[] renderers = enemy.GetComponentsInChildren<Renderer>()
                .Where(r => r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer)).ToArray();
            foreach (Renderer r in renderers)
            {
                var mats = r.sharedMaterials;
                if (mats.Contains(tint)) continue;
                r.sharedMaterials = mats.Append(tint).ToArray();
            }
            flashing[enemy] = (renderers, until);
            flashes++;
            LastFlashed = enemy;
            if (renderers.Length > 0)
            {
                LastBounds = renderers[0].bounds;
                foreach (Renderer r in renderers) LastBounds.Encapsulate(r.bounds);
            }
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
                    if (mats.Contains(tint)) r.sharedMaterials = mats.Where(m => m != tint).ToArray();
                }
                flashing.Remove(enemy);
            }
            if (flashes > 0 && Time.realtimeSinceStartup >= nextLog)
            {
                nextLog = Time.realtimeSinceStartup + 1f;
                McLog.Info($"hurt flash: {flashes} enemy flashes in the last second");
                flashes = 0;
            }
        }
    }
}
