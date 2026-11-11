using System;
using System.Collections.Generic;
using MyceliumCraft.Link;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// The other spores of your colony as Minecraft characters, in place of the spores (your request,
    /// 2026-11-03), like your own body (McPlayerModel).
    ///
    /// Minecraft poses each one in its own skin, facing south, and sends it in the spore's Bodies
    /// slot (MycSporeBodies.java). Here each becomes a mesh at that spore's feet, turned to where the
    /// spore faces, and the spore isn't drawn (its renderers get forceRenderingOff, as CameraSync does
    /// to yours). The spores keep wandering in Mycelium as they always do: only how they look changes.
    /// When your own body goes, or a spore becomes yours, it's drawn as before.
    /// </summary>
    sealed class McColonyBodies
    {
        sealed class Body
        {
            public GameObject Root;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public long Frame = -1;
            public int Vertices;
        }

        readonly Body[] bodies = new Body[Proto.ColMax];
        readonly ModelData data = new ModelData();
        HashSet<Renderer> hidden = new HashSet<Renderer>();
        HashSet<Renderer> hiddenNow = new HashSet<Renderer>();
        readonly List<Renderer> under = new List<Renderer>();
        int lastShown = -1;
        float nextLog;
        int updates;

        /// <summary>Every frame. want: your own Minecraft body is shown. feetOffset: a spore's middle to its feet.</summary>
        public void Update(MyceliumLink link, bool want, ColonyWatch colony, McPlayerModel model, float feetOffset, float scale)
        {
            Layout(colony, model, scale);
            hiddenNow.Clear();
            int shown = 0;
            for (int i = 0; i < Proto.ColMax; i++)
            {
                GameObject spore = want && i < colony.Count && i != colony.Current ? colony.Spores[i] : null;
                bool on = spore != null && spore.activeInHierarchy && Refresh(link, i, model, scale);
                if (bodies[i]?.Root != null && bodies[i].Root.activeSelf != on) bodies[i].Root.SetActive(on);
                if (!on) continue;
                shown++;
                // Minecraft's south is Mycelium's -z, the opposite of a spore with no turn at all.
                // A scenery spore's (tutorial sleeper's) transform is at its feet already; a colony spore's is its middle.
                bodies[i].Root.transform.SetPositionAndRotation(spore.transform.position - Vector3.up * (i < colony.Listed ? feetOffset : 0f) + colony.Shift[i],
                    colony.FullTurn[i]
                        ? spore.transform.rotation * Quaternion.Euler(0f, 180f, 0f) // an impaled spore: tilted with the mandible too
                        : Quaternion.Euler(0f, spore.transform.eulerAngles.y + 180f, 0f));
                // A sleeper's own size (the mesh is the colony spores' size; scaled about its feet).
                bodies[i].Root.transform.localScale = Vector3.one * (colony.Sizes[i] > 0f ? colony.Sizes[i] : 1f);
                spore.GetComponentsInChildren(true, under);
                foreach (Renderer r in under)
                {
                    // The popup when you look at a spore hangs under the spore too: its happiness
                    // faces are sprites on a canvas, and stay drawn (the spore's own parts aren't on one).
                    if (r.GetComponentInParent<Canvas>() != null) continue;
                    // Its happiness going up or down (happy and sad particles) stays drawn too.
                    if (CameraSync.MoodEffect(r.transform, spore.transform)) continue;
                    // A sleeper's "Zzz" stays drawn.
                    if (i >= colony.Listed && r is ParticleSystemRenderer) continue;
                    r.forceRenderingOff = true;
                    hiddenNow.Add(r);
                }
            }
            foreach (Renderer r in hidden)
                if (r != null && !hiddenNow.Contains(r)) r.forceRenderingOff = false;
            (hidden, hiddenNow) = (hiddenNow, hidden);

            if (shown != lastShown)
            {
                lastShown = shown;
                McLog.Info($"colony bodies: {shown} other spore(s) shown as Minecraft characters, {hidden.Count} spore renderers not drawn");
            }
            if (shown > 0 && Time.realtimeSinceStartup >= nextLog)
            {
                nextLog = Time.realtimeSinceStartup + 5f;
                for (int i = 0; i < colony.Count; i++)
                    if (bodies[i]?.Root != null && bodies[i].Root.activeSelf)
                    {
                        Vector3 p = bodies[i].Root.transform.position;
                        McLog.Info($"colony bodies: {colony.Names[i]}: {bodies[i].Vertices / 3} triangles at ({p.x:F2}, {p.y:F2}, {p.z:F2}), facing {bodies[i].Root.transform.eulerAngles.y - 180f:F0}, walking {colony.Speeds[i]:F1} blocks/s");
                    }
                McLog.Info($"colony bodies: {updates} new poses in the last 5 s");
                updates = 0;
            }
        }

        string laidOut = "";

        /// <summary>
        /// The lying scenery spores (the tutorial's sleepers), your report 2026-11-11: SleepingSpore 2 (1)
        /// and 2 (2) overlapped. A Minecraft lying body starts at its feet and lies a body's length
        /// behind them, but Mycelium's sleeper mesh is centred on its transform, so the body was half a
        /// body off, and those two, 1.1 units apart and turned 135 degrees to each other, crossed.
        /// Every frame: each body's footprint on the ground (a rectangle: its mesh's bounds once
        /// Minecraft has sent it, else a body of Minecraft's size) is first centred on the original
        /// mesh's middle, then any two that overlap are pushed apart along the line between them until
        /// they don't (5 cm apart at least). The shift goes into ColonyWatch.Shift: drawn here, and sent
        /// to Minecraft as the feet (ColonyPush), so it fits the body to the ground where it now lies.
        /// </summary>
        void Layout(ColonyWatch colony, McPlayerModel model, float scale)
        {
            int n = 0;
            for (int i = colony.Listed; i < colony.Count; i++)
            {
                colony.Shift[i] = Vector3.zero;
                GameObject s = colony.Spores[i];
                if (s == null || !s.activeInHierarchy || colony.FullTurn[i] || colony.Poses[i] != 2) continue;
                float size = colony.Sizes[i] > 0f ? colony.Sizes[i] : 1f;
                Quaternion turn = Quaternion.Euler(0f, s.transform.eulerAngles.y + 180f, 0f);
                Vector3 centre, half;
                if (bodies[i] != null && bodies[i].Vertices > 0)
                {
                    centre = bodies[i].Mesh.bounds.center;
                    half = bodies[i].Mesh.bounds.extents;
                }
                else
                {
                    // Not sent yet: a lying body of the player's size (0.6 x 1.8 blocks, Mycelium's spore scale) behind its feet.
                    float h = 1.8f * PlayerScale * scale, w = 0.6f * PlayerScale * scale;
                    centre = new Vector3(0f, 0f, -h / 2f);
                    half = new Vector3(w / 2f, 0f, h / 2f);
                }
                Vector3 c = s.transform.position + turn * (centre * size);
                var r = s.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Vector3 target = r != null ? r.bounds.center : s.transform.position;
                Vector3 shift = new Vector3(target.x - c.x, 0f, target.z - c.z);
                foot[n] = (i, new Vector2(c.x + shift.x, c.z + shift.z), Flat(turn * Vector3.right * (half.x * size)), Flat(turn * Vector3.forward * (half.z * size)), shift);
                n++;
            }
            int pushes = 0;
            for (int pass = 0; pass < 60; pass++)
            {
                bool moved = false;
                for (int a = 0; a < n; a++)
                    for (int b = a + 1; b < n; b++)
                    {
                        if (!Overlap(foot[a], foot[b])) continue;
                        Vector2 d = foot[b].c - foot[a].c;
                        d = d.sqrMagnitude > 1e-6f ? d.normalized : Vector2.right;
                        foot[a].c -= d * 0.03f;
                        foot[b].c += d * 0.03f;
                        foot[a].shift -= new Vector3(d.x, 0f, d.y) * 0.03f;
                        foot[b].shift += new Vector3(d.x, 0f, d.y) * 0.03f;
                        moved = true;
                        pushes++;
                    }
                if (!moved) break;
            }
            int left = 0;
            for (int a = 0; a < n; a++) for (int b = a + 1; b < n; b++) if (Overlap(foot[a], foot[b])) left++;
            var parts = new List<string>();
            for (int k = 0; k < n; k++)
            {
                colony.Shift[foot[k].slot] = foot[k].shift;
                parts.Add($"{colony.Names[foot[k].slot]} moved ({foot[k].shift.x:F2}, {foot[k].shift.z:F2})");
            }
            string line = n == 0 ? "" : $"{n} lying spore(s) laid out ({(bodies[foot[0].slot]?.Vertices > 0 ? "from Minecraft's bodies" : "Minecraft's size, no body yet")}), {pushes} push(es) apart, {left} pair(s) still overlapping: {string.Join(", ", parts)}";
            if (line.Length > 0 && Changed(line, laidOut))
            {
                laidOut = line;
                McLog.Info("colony bodies: " + line);
            }
        }

        /// <summary>Mycelium's spore as a Minecraft player: x0.674 (MinecraftDriver's spore size match), for a body not sent yet.</summary>
        const float PlayerScale = 0.674f;

        readonly (int slot, Vector2 c, Vector2 a, Vector2 b, Vector3 shift)[] foot = new (int, Vector2, Vector2, Vector2, Vector3)[Proto.ColMax];

        static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

        /// <summary>Two footprints (centre and half-axes) closer than 5 cm, by the separating axis test.</summary>
        static bool Overlap((int slot, Vector2 c, Vector2 a, Vector2 b, Vector3 shift) p, (int slot, Vector2 c, Vector2 a, Vector2 b, Vector3 shift) q)
        {
            Vector2 d = q.c - p.c;
            foreach (Vector2 axis in new[] { p.a, p.b, q.a, q.b })
            {
                if (axis.sqrMagnitude < 1e-8f) continue;
                Vector2 u = axis.normalized;
                float rp = Mathf.Abs(Vector2.Dot(p.a, u)) + Mathf.Abs(Vector2.Dot(p.b, u));
                float rq = Mathf.Abs(Vector2.Dot(q.a, u)) + Mathf.Abs(Vector2.Dot(q.b, u));
                if (Mathf.Abs(Vector2.Dot(d, u)) > rp + rq + 0.05f) return false;
            }
            return true;
        }

        /// <summary>Log a new layout only when a number moved more than a centimetre (they wobble a little).</summary>
        static bool Changed(string now, string before)
        {
            if (before.Length == 0) return true;
            var a = System.Text.RegularExpressions.Regex.Matches(now, @"-?\d+\.\d+");
            var b = System.Text.RegularExpressions.Regex.Matches(before, @"-?\d+\.\d+");
            if (a.Count != b.Count || !now.Substring(0, Math.Min(40, now.Length)).Equals(before.Substring(0, Math.Min(40, before.Length)))) return true;
            for (int i = 0; i < a.Count; i++)
                if (Mathf.Abs(float.Parse(a[i].Value, System.Globalization.CultureInfo.InvariantCulture) - float.Parse(b[i].Value, System.Globalization.CultureInfo.InvariantCulture)) > 0.011f) return true;
            return false;
        }

        /// <summary>The newest pose in this slot into its mesh. False: Minecraft has no body there.</summary>
        bool Refresh(MyceliumLink link, int slot, McPlayerModel model, float scale)
        {
            Body body = bodies[slot] ?? (bodies[slot] = Create(slot));
            if (link.TryReadBody(slot, data) && data.Frame != body.Frame)
            {
                body.Frame = data.Frame;
                body.Vertices = data.Vertices;
                if (data.Vertices > 0)
                {
                    McPlayerModel.FillMesh(body.Mesh, data.Raw, data.Vertices, scale);
                    body.Mesh.subMeshCount = 1;
                    body.Mesh.SetTriangles(McPlayerModel.Triangles(0, data.Vertices), 0, false);
                    body.Mesh.RecalculateBounds();
                    body.Renderer.sharedMaterial = model.MaterialFor(link, data.Texture[0]);
                    updates++;
                }
            }
            return body.Vertices > 0;
        }

        static Body Create(int slot)
        {
            var root = new GameObject($"MyceliumCraft colony body {slot}");
            UnityEngine.Object.DontDestroyOnLoad(root);
            var mesh = new Mesh { name = $"Colony body {slot}" };
            mesh.MarkDynamic();
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.receiveShadows = false;
            root.SetActive(false);
            return new Body { Root = root, Mesh = mesh, Renderer = renderer };
        }
    }
}
