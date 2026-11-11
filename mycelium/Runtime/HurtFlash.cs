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
    ///
    /// A model made of several sub-meshes (the crab's body has 4) can't take the tint as an extra
    /// material: Unity draws extra materials over the last sub-mesh only, so crabs hardly flashed
    /// (your report, 2026-10-21). Those get a copy of the renderer on top instead (same mesh, same
    /// bones), with the tint on every sub-mesh, switched on only while flashing.
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
                Shader shader = McShaders.Load("MyceliumCraftHurtTint");
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
                    foreach (Renderer r in f.renderers) if (r != null) TintOn(r, want);
                }
                flashing[enemy] = (f.renderers, until, want);
                if (want == poisonTint) greenFlashes++;
                return;
            }
            Renderer[] renderers = enemy.GetComponentsInChildren<Renderer>()
                .Where(r => r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer) && r.name != OverlayName).ToArray();
            Describe(enemy, renderers);
            foreach (Renderer r in renderers) TintOn(r, want);
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

        const string OverlayName = "MyceliumCraft hurt tint";
        readonly Dictionary<Renderer, Renderer> overlays = new Dictionary<Renderer, Renderer>();
        int overlaysMade;

        static int SubMeshes(Renderer r) =>
            r is SkinnedMeshRenderer s ? (s.sharedMesh != null ? s.sharedMesh.subMeshCount : 0)
            : r.GetComponent<MeshFilter>() is MeshFilter mf && mf.sharedMesh != null ? mf.sharedMesh.subMeshCount : 0;

        /// <summary>Tint one renderer: an extra material, or its overlay copy if it has several sub-meshes.</summary>
        void TintOn(Renderer r, Material want)
        {
            int subMeshes = SubMeshes(r);
            Renderer overlay = subMeshes > 1 ? Overlay(r) : null;
            if (overlay == null)
            {
                var mats = r.sharedMaterials.Where(m => m != tint && m != poisonTint);
                r.sharedMaterials = mats.Append(want).ToArray();
                return;
            }
            overlay.sharedMaterials = Enumerable.Repeat(want, subMeshes).ToArray();
            overlay.enabled = true;
        }

        void TintOff(Renderer r)
        {
            var mats = r.sharedMaterials;
            if (mats.Contains(tint) || mats.Contains(poisonTint)) r.sharedMaterials = mats.Where(m => m != tint && m != poisonTint).ToArray();
            if (overlays.TryGetValue(r, out Renderer overlay) && overlay != null) overlay.enabled = false;
        }

        /// <summary>
        /// The renderer's overlay copy, made the first time: a child with the same mesh (and, for a
        /// skinned one, the same bones, so it moves exactly with it). No shadows. Null if it can't be.
        /// </summary>
        Renderer Overlay(Renderer r)
        {
            if (overlays.TryGetValue(r, out Renderer overlay) && overlay != null) return overlay;
            var go = new GameObject(OverlayName) { layer = r.gameObject.layer };
            go.transform.SetParent(r.transform, false);
            if (r is SkinnedMeshRenderer s)
            {
                var o = go.AddComponent<SkinnedMeshRenderer>();
                o.sharedMesh = s.sharedMesh;
                o.bones = s.bones;
                o.rootBone = s.rootBone;
                o.quality = s.quality;
                o.updateWhenOffscreen = s.updateWhenOffscreen;
                o.localBounds = s.localBounds;
                overlay = o;
            }
            else if (r.GetComponent<MeshFilter>() is MeshFilter mf)
            {
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                overlay = go.AddComponent<MeshRenderer>();
            }
            else
            {
                UnityEngine.Object.Destroy(go);
                return null;
            }
            overlay.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            overlay.receiveShadows = false;
            overlay.enabled = false;
            overlays[r] = overlay;
            overlaysMade++;
            McLog.Info($"hurt flash: overlay copy made for '{r.name}' ({SubMeshes(r)} sub-meshes; {overlaysMade} so far)");
            return overlay;
        }

        readonly HashSet<string> described = new HashSet<string>();

        /// <summary>
        /// Once per kind of enemy (its name without "(Clone)" and numbers): which renderers the flash
        /// tints, with their sub-meshes and materials, and every renderer it skipped (diagnosis for
        /// crabs not flashing, 2026-10-21).
        /// </summary>
        void Describe(Component enemy, Renderer[] used)
        {
            string kind = System.Text.RegularExpressions.Regex.Replace(enemy.gameObject.name, @"\(Clone\)|\s*\(\d+\)|\d+$", "").Trim();
            if (!described.Add(kind)) return;
            var parts = new List<string>();
            foreach (Renderer r in enemy.GetComponentsInChildren<Renderer>(true))
            {
                int subMeshes = r is SkinnedMeshRenderer sk && sk.sharedMesh != null ? sk.sharedMesh.subMeshCount
                    : r.GetComponent<MeshFilter>() is MeshFilter mf && mf.sharedMesh != null ? mf.sharedMesh.subMeshCount : -1;
                string why = used.Contains(r) ? "tinted" : !r.enabled ? "skipped: disabled" : !r.gameObject.activeInHierarchy ? "skipped: inactive" : "skipped: " + r.GetType().Name;
                parts.Add($"'{r.name}' {r.GetType().Name} {subMeshes} sub-meshes, {r.sharedMaterials.Length} materials ({string.Join("/", r.sharedMaterials.Select(m => m != null ? m.shader.name : "none"))}): {why}");
            }
            Transform top = enemy.transform;
            while (top.parent != null) top = top.parent;
            int outside = top.GetComponentsInChildren<Renderer>(true).Count(r => !r.transform.IsChildOf(enemy.transform));
            McLog.Info($"hurt flash: '{kind}' (health on '{enemy.gameObject.name}', under '{top.name}'): {parts.Count} renderers under its health, {outside} more elsewhere in '{top.name}'. " + string.Join("; ", parts));
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
                foreach (Renderer r in flashing[enemy].renderers) if (r != null) TintOff(r);
                flashing.Remove(enemy);
            }
            if (done.Count > 0)
            {
                // Overlays of enemies that are gone (destroyed with them).
                var gone = overlays.Keys.Where(k => k == null).ToList();
                foreach (Renderer k in gone) overlays.Remove(k);
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
