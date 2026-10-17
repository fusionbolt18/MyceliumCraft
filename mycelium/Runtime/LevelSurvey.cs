using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Test aid ("-mcSurvey"): records every surface and ceiling of the level around the spore to
    /// survey.csv next to the exe, then quits. Used to check collision models offline against the
    /// positions the spore really walked (from MyceliumCraft.log), without rebuilding Unity.
    ///
    /// One line per column: x,z (Mycelium units) then, per surface found going down (highest first):
    ///   y|collider|ceilingFront|ceilingAny
    /// ceilingFront: first hit going up from just above that surface, front faces only (Unity default);
    /// ceilingAny: the same with back faces counted too. "-" = nothing hit.
    /// </summary>
    static class LevelSurvey
    {
        const int MaxSurfaces = 6;
        const float HalfSize = 220f;   // Mycelium units each way from the spore
        const float Above = 120f;      // rays start this far above the spore
        const float Length = 500f;

        public static void Run(Vector3 spore, int layerMask, float cell)
        {
            var watch = Stopwatch.StartNew();
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "survey.csv"));
            bool backfaces = Physics.queriesHitBackfaces;
            int columns = 0, rays = 0;
            var inv = CultureInfo.InvariantCulture;
            using (var w = new StreamWriter(path, false))
            {
                w.WriteLine($"# survey spore={spore.x.ToString(inv)},{spore.y.ToString(inv)},{spore.z.ToString(inv)} cell={cell.ToString(inv)} mask={layerMask}");
                var sb = new StringBuilder();
                float top = spore.y + Above;
                for (float z = spore.z - HalfSize; z <= spore.z + HalfSize; z += cell)
                {
                    for (float x = spore.x - HalfSize; x <= spore.x + HalfSize; x += cell)
                    {
                        sb.Clear();
                        sb.Append(x.ToString("F2", inv)).Append(',').Append(z.ToString("F2", inv));
                        var origin = new Vector3(x, top, z);
                        float remaining = Length;
                        int k = 0;
                        while (k < MaxSurfaces && remaining > 0)
                        {
                            Physics.queriesHitBackfaces = false;
                            rays++;
                            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, remaining, layerMask, QueryTriggerInteraction.Ignore)) break;
                            float s = hit.point.y;
                            // Ceiling above this surface, up to where the ray came from.
                            float span = origin.y - s - 0.1f;
                            var up = new Vector3(x, s + 0.05f, z);
                            string front = "-", any = "-";
                            if (span > 0)
                            {
                                rays += 2;
                                if (Physics.Raycast(up, Vector3.up, out RaycastHit c1, span, layerMask, QueryTriggerInteraction.Ignore)) front = c1.point.y.ToString("F2", inv);
                                Physics.queriesHitBackfaces = true;
                                if (Physics.Raycast(up, Vector3.up, out RaycastHit c2, span, layerMask, QueryTriggerInteraction.Ignore)) any = c2.point.y.ToString("F2", inv);
                                Physics.queriesHitBackfaces = false;
                            }
                            sb.Append(',').Append(s.ToString("F2", inv)).Append('|').Append(hit.collider.name.Replace(',', ' ').Replace('|', ' '))
                              .Append('|').Append(front).Append('|').Append(any);
                            k++;
                            float step = origin.y - s + 0.05f;
                            origin.y -= step;
                            remaining -= step;
                        }
                        w.WriteLine(sb.ToString());
                        columns++;
                    }
                }
            }
            Physics.queriesHitBackfaces = backfaces;
            McLog.Info($"survey: {columns} columns, {rays} rays, {watch.Elapsed.TotalSeconds:F1} s -> {path}");
        }
    }
}
