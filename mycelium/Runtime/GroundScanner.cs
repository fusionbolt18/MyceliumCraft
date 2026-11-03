using System.Collections.Generic;
using System.Diagnostics;
using MyceliumCraft.Link;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Measures Mycelium's solid ground around the spore as layered columns (SkyCraft's
    /// "CollisionField stage A", with several hits per column), on a grid laid out in Minecraft
    /// coordinates.
    ///
    /// Per column: rays going down find every surface (the top of something solid), highest first.
    /// Everything below the lowest surface is solid. Each higher surface is the top of a solid piece;
    /// its bottom is found by a ray going up from the surface below it. That way tunnels, overhangs
    /// and tree canopies keep the open space under them. Some surfaces, like Unity terrain, can only
    /// be hit from above. When nothing is found from below, the piece is a slab SlabBlocks thick,
    /// never thicker than the gap below it. But if that gap is smaller than FillBelowOneSided, it is
    /// filled: in the tutorial a walkway mesh often sits 0.1-3 units above the hidden terrain tile,
    /// and those slivers aren't real passages (the smallest one-sided gap the spore ever walked in
    /// was 5.79 units). A small Minecraft player could otherwise drop into them and get wedged.
    ///
    /// Up to MaxSurfaces surfaces are found per column (a tree can stack leaves, trunk and terrain
    /// over the path). The protocol carries GPieces pieces per cell, so if there are more, the
    /// pieces with the smallest gap between them are merged first; small gaps can't fit a player.
    /// </summary>
    sealed class GroundScanner
    {
        public const int N = 128;            // cells per side
        public const double CellBlocks = 0.5; // Minecraft blocks per cell -> 64 x 64 blocks
        const int MaxSurfaces = 10;          // surfaces looked for per column
        const float RayStartAbove = 60f;     // Mycelium units above the spore
        const float RayLength = 400f;
        const float SlabBlocks = 0.5f;       // thickness of a surface with no underside found
        const float PlayerBlocks = 1.8f;     // Minecraft player height, for the "too low" count
        const float ThinUnderside = 0.001f;  // an underside at least this far below its surface is real (units); an oyster cap edge 0.009 thin was missed at 0.01 and became a pillar (2026-11-03)

        // Layers the spore collides with but which are not ground (characters, weapons, effects).
        static readonly string[] NotGround =
        {
            "Player", "Enemy", "Weapon", "EnemyProjectile", "currentWeapon", "Boss", "MenuSpore",
            "ArmLayer", "BodyLayer", "DeadEnemy", "Ignore Raycast", "UI",
        };

        /// <summary>Per cell (j*N + i): GPieces (bottom, top) pairs in Minecraft Y, lowest first.</summary>
        public readonly float[] Pieces = new float[N * N * Proto.GFloatsPerCell];
        public double OriginX, OriginZ;    // Minecraft coords of the grid's west / north edge
        public double CenterX, CenterZ;    // Minecraft coords the grid was centred on
        public uint Version;
        public int LayerMask;
        public string LayerNames = "";
        /// <summary>Gaps under a surface with no underside smaller than this (Mycelium units) are filled.</summary>
        public float FillBelowOneSided = 4.25f;

        readonly float[] surfaces = new float[MaxSurfaces];
        readonly float[] undersides = new float[MaxSurfaces]; // undersides[k]: first hit going up from surfaces[k], or NaN
        readonly float[] bottoms = new float[MaxSurfaces];    // pieces of one column (Mycelium Y), lowest first
        readonly float[] tops = new float[MaxSurfaces];

        public void ChooseLayers(int sporeLayer)
        {
            var excluded = new HashSet<string>(NotGround);
            LayerMask = 0;
            var names = new List<string>();
            for (int l = 0; l < 32; l++)
            {
                string name = UnityEngine.LayerMask.LayerToName(l);
                if (string.IsNullOrEmpty(name) || excluded.Contains(name)) continue;
                if (Physics.GetIgnoreLayerCollision(sporeLayer, l)) continue;
                LayerMask |= 1 << l;
                names.Add(name);
            }
            LayerNames = string.Join(",", names);
        }

        /// <summary>Scan around the spore. Returns a one-line summary for the log.</summary>
        public string Scan(Vector3 spore, WorldMapping map)
        {
            var watch = Stopwatch.StartNew();
            map.ToMc(spore.x, spore.y, spore.z, out double mcX, out _, out double mcZ);
            CenterX = mcX;
            CenterZ = mcZ;
            double half = N * CellBlocks / 2;
            OriginX = System.Math.Floor(mcX - half);
            OriginZ = System.Math.Floor(mcZ - half);
            float scale = (float)map.Scale;
            float rayTop = spore.y + RayStartAbove;
            bool backfaces = Physics.queriesHitBackfaces;
            int columnsHit = 0, layered = 0, slabs = 0, filled = 0, tooLow = 0, merged = 0, capped = 0, rays = 0, thinFound = 0;
            float min = float.MaxValue, max = float.MinValue;

            for (int j = 0; j < N; j++)
            {
                double cz = OriginZ + (j + 0.5) * CellBlocks;
                for (int i = 0; i < N; i++)
                {
                    double cx = OriginX + (i + 0.5) * CellBlocks;
                    map.ToMyc(cx, 0, cz, out double x, out _, out double z);

                    // Surfaces going down, highest first.
                    Physics.queriesHitBackfaces = false;
                    var origin = new Vector3((float)x, rayTop, (float)z);
                    float remaining = RayLength;
                    int n = 0;
                    while (n < MaxSurfaces && remaining > 0)
                    {
                        rays++;
                        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, remaining, LayerMask, QueryTriggerInteraction.Ignore)) break;
                        surfaces[n++] = hit.point.y;
                        // Continue just below this surface. Mesh colliders are one-sided for rays,
                        // so a ray starting inside one finds the next surface below it.
                        float step = origin.y - hit.point.y + 0.05f;
                        origin.y -= step;
                        remaining -= step;
                    }

                    // Undersides: going up from each lower surface, back faces counted (an
                    // underside seen from below is often a mesh's back face).
                    Physics.queriesHitBackfaces = true;
                    for (int k = 1; k < n; k++)
                    {
                        undersides[k] = float.NaN;
                        // Stop 0.1 below the surface above: a one-sided surface's own back face is
                        // not an underside (hitting it gave zero-thickness pieces Minecraft ignores,
                        // so walkways vanished in Minecraft).
                        float length = surfaces[k - 1] - surfaces[k] - 0.15f;
                        if (length <= 0f) continue;
                        rays++;
                        var up = new Vector3((float)x, surfaces[k] + 0.05f, (float)z);
                        if (Physics.Raycast(up, Vector3.up, out RaycastHit u, length, LayerMask, QueryTriggerInteraction.Ignore))
                            undersides[k] = u.point.y;
                        else
                        {
                            // Nothing that far: look right up to the surface. Where a solid tapers to
                            // a thin edge its underside is closer than 0.15 (a rock in the hub was 0.03
                            // thick at one column, missed, and that column became a pillar blocking the
                            // way to the run start). Its own back face is exactly at the surface (0.000
                            // below) and still doesn't count.
                            rays++;
                            if (Physics.Raycast(up, Vector3.up, out RaycastHit thin, surfaces[k - 1] - up.y + 0.01f, LayerMask, QueryTriggerInteraction.Ignore)
                                && thin.point.y < surfaces[k - 1] - ThinUnderside)
                            {
                                undersides[k] = thin.point.y;
                                thinFound++;
                            }
                        }
                    }

                    // Pieces, lowest first (Mycelium Y).
                    int count = 0;
                    if (n > 0)
                    {
                        columnsHit++;
                        if (n > 1) layered++;
                        if (n == MaxSurfaces) capped++;
                        bottoms[count] = Proto.FarBelow;
                        tops[count++] = surfaces[n - 1];
                        for (int k = n - 2; k >= 0; k--)
                        {
                            float top = surfaces[k];
                            float floor = surfaces[k + 1];
                            float bottom = undersides[k + 1];
                            if (float.IsNaN(bottom) || bottom > top - ThinUnderside)
                            {
                                if (top - floor < FillBelowOneSided)
                                {
                                    bottom = floor; // a sliver under a one-sided surface: hidden ground, fill it
                                    filled++;
                                }
                                else
                                {
                                    bottom = top - SlabBlocks * scale;
                                    slabs++;
                                }
                            }
                            bottom = Mathf.Max(bottom, floor);
                            bottoms[count] = bottom;
                            tops[count++] = top;
                        }
                        // Too many for the protocol: close the smallest gaps first.
                        while (count > Proto.GPieces)
                        {
                            int best = 0;
                            for (int k = 1; k < count - 1; k++)
                                if (bottoms[k + 1] - tops[k] < bottoms[best + 1] - tops[best]) best = k;
                            tops[best] = tops[best + 1];
                            for (int k = best + 1; k < count - 1; k++)
                            {
                                bottoms[k] = bottoms[k + 1];
                                tops[k] = tops[k + 1];
                            }
                            count--;
                            merged++;
                        }
                        for (int k = 1; k < count; k++)
                            if ((bottoms[k] - tops[k - 1]) / scale < PlayerBlocks) tooLow++;
                        min = Mathf.Min(min, surfaces[n - 1] / scale);
                        max = Mathf.Max(max, surfaces[0] / scale);
                    }
                    int c = (j * N + i) * Proto.GFloatsPerCell, p = 0;
                    for (int k = 0; k < count; k++)
                    {
                        Pieces[c + p++] = k == 0 ? Proto.FarBelow : bottoms[k] / scale;
                        Pieces[c + p++] = tops[k] / scale;
                    }
                    while (p < Proto.GFloatsPerCell) Pieces[c + p++] = Proto.Unused;
                }
            }
            Physics.queriesHitBackfaces = backfaces;
            Version++;

            map.ToMc(spore.x, spore.y, spore.z, out _, out double sporeMcY, out _);
            float startGround = GroundAt(mcX, mcZ, (float)sporeMcY);
            return $"ground scan v{Version}: {columnsHit}/{N * N} columns hit, {layered} layered (open space under something), " +
                   $"{thinFound} thin undersides found (< 0.15 below their surface), {filled} small gaps under one-sided surfaces filled (< {FillBelowOneSided:F2} units), {slabs} one-sided pieces made {SlabBlocks}-block slabs, {merged} small gaps closed to fit {Proto.GPieces} pieces, {capped} columns with {MaxSurfaces}+ surfaces, {tooLow} gaps lower than the Minecraft player ({PlayerBlocks} blocks); " +
                   $"MC height {(columnsHit > 0 ? $"{min:F1}..{max:F1}" : "-")}, ground at spore {startGround:F2} vs spore {sporeMcY:F2}; " +
                   $"grid MC x {OriginX}..{OriginX + N * CellBlocks} z {OriginZ}..{OriginZ + N * CellBlocks}; {rays} rays, {watch.Elapsed.TotalMilliseconds:F1} ms";
        }

        /// <summary>
        /// Test aid (-mcColumn x,z): every surface and underside the scan finds in the column at this
        /// Minecraft position, with what it hit, and the pieces sent for that cell.
        /// </summary>
        public string DescribeColumn(double mcX, double mcZ, Vector3 spore, WorldMapping map)
        {
            var sb = new System.Text.StringBuilder($"column at Minecraft ({mcX}, {mcZ}):");
            map.ToMyc(mcX, 0, mcZ, out double x, out _, out double z);
            bool backfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = false;
            var origin = new Vector3((float)x, spore.y + RayStartAbove, (float)z);
            float remaining = RayLength;
            var found = new List<float>();
            while (found.Count < MaxSurfaces && remaining > 0 && Physics.Raycast(origin, Vector3.down, out RaycastHit hit, remaining, LayerMask, QueryTriggerInteraction.Ignore))
            {
                found.Add(hit.point.y);
                sb.Append($"\n  surface {hit.point.y:F3} ({hit.point.y / map.Scale:F3} blocks) on '{hit.collider.name}' {hit.collider.GetType().Name} layer={UnityEngine.LayerMask.LayerToName(hit.collider.gameObject.layer)} normal {hit.normal:F2}");
                float step = origin.y - hit.point.y + 0.05f;
                origin.y -= step;
                remaining -= step;
            }
            Physics.queriesHitBackfaces = true;
            for (int k = 1; k < found.Count; k++)
            {
                var up = new Vector3((float)x, found[k] + 0.05f, (float)z);
                float full = found[k - 1] - found[k] + 0.01f;
                sb.Append($"\n  going up from {found[k]:F3}:");
                foreach (float length in new[] { found[k - 1] - found[k] - 0.15f, full })
                {
                    if (length <= 0f) { sb.Append($" [length {length:F3}: no ray]"); continue; }
                    if (Physics.Raycast(up, Vector3.up, out RaycastHit u, length, LayerMask, QueryTriggerInteraction.Ignore))
                        sb.Append($" [length {length:F3}: hit {u.point.y:F3} on '{u.collider.name}', {found[k - 1] - u.point.y:F3} below the surface above]");
                    else
                        sb.Append($" [length {length:F3}: nothing]");
                }
            }
            Physics.queriesHitBackfaces = backfaces;
            int i = (int)System.Math.Floor((mcX - OriginX) / CellBlocks), j = (int)System.Math.Floor((mcZ - OriginZ) / CellBlocks);
            if (i >= 0 && j >= 0 && i < N && j < N)
            {
                int c = (j * N + i) * Proto.GFloatsPerCell;
                sb.Append("\n  pieces sent (blocks):");
                for (int k = 0; k < Proto.GPieces && Pieces[c + 2 * k + 1] < Proto.Unused; k++)
                    sb.Append($" ({(Pieces[c + 2 * k] <= Proto.FarBelow ? "far below" : Pieces[c + 2 * k].ToString("F2"))}, {Pieces[c + 2 * k + 1]:F2})");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Top of the highest solid piece at or below mcY + 1 in the cell under this Minecraft
        /// position (the ground you'd stand on there), or NaN.
        /// </summary>
        public float GroundAt(double mcX, double mcZ, float mcY)
        {
            int i = (int)System.Math.Floor((mcX - OriginX) / CellBlocks);
            int j = (int)System.Math.Floor((mcZ - OriginZ) / CellBlocks);
            if (Version == 0 || i < 0 || j < 0 || i >= N || j >= N) return float.NaN;
            int c = (j * N + i) * Proto.GFloatsPerCell;
            float best = float.NaN;
            for (int k = 0; k < Proto.GPieces; k++)
            {
                float top = Pieces[c + 2 * k + 1];
                if (top >= Proto.Unused) break;
                if (top <= mcY + 1f) best = top;
            }
            return best;
        }
    }
}
