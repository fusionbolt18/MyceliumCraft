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
                bodies[i].Root.transform.SetPositionAndRotation(spore.transform.position - Vector3.up * feetOffset,
                    Quaternion.Euler(0f, spore.transform.eulerAngles.y + 180f, 0f));
                spore.GetComponentsInChildren(true, under);
                foreach (Renderer r in under)
                {
                    // The popup when you look at a spore hangs under the spore too: its happiness
                    // faces are sprites on a canvas, and stay drawn (the spore's own parts aren't on one).
                    if (r.GetComponentInParent<Canvas>() != null) continue;
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
            Object.DontDestroyOnLoad(root);
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
