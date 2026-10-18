using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Test aid (-mcColliders): lists the colliders the ground scan treats as solid around the spore
    /// that have nothing visible drawn where they are (no enabled renderer on them, their parents or
    /// children whose bounds overlap theirs). In Minecraft those become blocks you can't see.
    /// </summary>
    static class ColliderSurvey
    {
        public static void Run(Vector3 center, float radius, int layerMask)
        {
            Collider[] all = Physics.OverlapSphere(center, radius, layerMask, QueryTriggerInteraction.Ignore);
            var visible = new List<Renderer>();
            int invisible = 0;
            var sb = new StringBuilder();
            foreach (Collider c in all)
            {
                visible.Clear();
                visible.AddRange(c.GetComponentsInChildren<Renderer>());
                visible.AddRange(c.GetComponentsInParent<Renderer>());
                bool seen = false;
                foreach (Renderer r in visible)
                {
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r.forceRenderingOff) continue;
                    if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                    if (r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) continue;
                    if (r.bounds.Intersects(c.bounds)) { seen = true; break; }
                }
                if (seen) continue;
                invisible++;
                Bounds b = c.bounds;
                sb.Append($"\n  '{Path(c.transform)}' {c.GetType().Name} layer={LayerMask.LayerToName(c.gameObject.layer)} " +
                          $"from ({b.min.x:F1}, {b.min.y:F1}, {b.min.z:F1}) to ({b.max.x:F1}, {b.max.y:F1}, {b.max.z:F1})");
            }
            McLog.Info($"collider survey: {all.Length} solid colliders within {radius:F0} units of ({center.x:F1}, {center.y:F1}, {center.z:F1}), {invisible} with nothing visible drawn there:{sb}");
        }

        static string Path(Transform t)
        {
            string p = t.name;
            for (Transform up = t.parent; up != null; up = up.parent) p = up.name + "/" + p;
            return p;
        }
    }
}
