using System.Collections.Generic;
using MyceliumCraft.Link;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Your Minecraft player's body in Mycelium's world, in place of the spore, in third person.
    ///
    /// Minecraft poses and animates the model itself (MycPlayerModel.java) and sends it every frame
    /// as triangles relative to its feet, with the textures they use (skin, armour, cape). Here it
    /// becomes a mesh standing at the spore's feet, so Mycelium's walls hide it and it moves exactly
    /// with the spore. Minecraft coordinates map as the world does: (x, y, z) blocks ->
    /// (x, y, -z) * scale units. The spore itself is drawn as shadow only meanwhile (CameraSync).
    /// </summary>
    sealed class McPlayerModel
    {
        GameObject root;
        Mesh mesh;
        MeshRenderer meshRenderer;
        Shader shader;
        bool failed;
        bool shown;
        readonly ModelData data = new ModelData();
        long lastFrame = -1;
        readonly Dictionary<int, (uint version, Material material)> textures = new Dictionary<int, (uint, Material)>();
        Material fallback;

        // Once-a-second log
        float nextLog;
        int updates;
        Bounds lastBounds;

        public bool Shown => shown;
        /// <summary>Minecraft is in first person: only the body's shadow is drawn.</summary>
        public bool ShadowOnly => shadowOnly;
        bool shadowOnly;
        bool castsShadow = true; // test aid can switch the shadow off
        bool vanished; // Trophic Cascade: nothing drawn, not even the shadow (as Mycelium does to the spore)

        /// <summary>The spore has vanished (VanishWatch): the body and its shadow go too, until it's back.</summary>
        public void SetVanished(bool on)
        {
            if (on == vanished) return;
            vanished = on;
            ApplyRenderMode();
        }

        /// <summary>Third person: body and shadow. First person: only the shadow (the camera is inside the body).</summary>
        void ApplyRenderMode()
        {
            if (meshRenderer == null) return;
            // (Test aid, no shadow in first person: draw nothing. "Off" alone would show the body.)
            meshRenderer.enabled = !vanished && (castsShadow || !shadowOnly);
            meshRenderer.shadowCastingMode = !castsShadow ? UnityEngine.Rendering.ShadowCastingMode.Off
                : shadowOnly ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                : UnityEngine.Rendering.ShadowCastingMode.On;
        }
        /// <summary>World point at the top of the head (red on the fake test body's texture), for the -mcModelCheck test aid.</summary>
        public Vector3 HeadPoint => root != null ? root.transform.position + Vector3.up * (lastBounds.max.y * 0.95f) : Vector3.zero;

        /// <summary>For the -mcModelCheck test aid: where the body stands, and how tall it is.</summary>
        public Vector3 FeetPoint => root != null ? root.transform.position : Vector3.zero;
        public float Height => lastBounds.max.y;

        /// <summary>Test aid: the body casts its shadow, or not.</summary>
        public void SetCastsShadow(bool on)
        {
            castsShadow = on;
            ApplyRenderMode();
        }

        /// <summary>Test aid: switch the depth passes (DepthOnly, DepthNormals) on or off on every body material.</summary>
        public void SetDepthPasses(bool on)
        {
            var all = new List<Material>();
            if (fallback != null) all.Add(fallback);
            foreach (var t in textures.Values) if (t.material != null) all.Add(t.material);
            foreach (Material m in all)
            {
                m.SetShaderPassEnabled("DepthOnly", on);
                m.SetShaderPassEnabled("DepthNormals", on);
            }
        }

        /// <summary>
        /// Call every frame. want: Minecraft drives the spore (the model is shown if Minecraft sends
        /// one, i.e. in third person). feet: the spore's feet. scale: Mycelium units per block.
        /// </summary>
        public void Update(MyceliumLink link, bool want, Vector3 feet, float scale)
        {
            if (failed) return;
            if (!want || !McConfig.PlayerModel)
            {
                SetShown(false, "Minecraft not driving the spore");
                return;
            }
            if (root == null && !Create()) return;

            if (link.TryReadModel(data) && data.Frame != lastFrame)
            {
                lastFrame = data.Frame;
                if (data.Visible && data.Batches > 0) Rebuild(link, scale);
                if (data.FirstPerson != shadowOnly)
                {
                    shadowOnly = data.FirstPerson;
                    ApplyRenderMode();
                    McLog.Info(shadowOnly ? "player model: first person, only your Minecraft body's shadow is drawn" : "player model: third person, your Minecraft body and its shadow are drawn");
                }
                SetShown(data.Visible && data.Batches > 0, data.Visible ? "empty model" : "Minecraft sent no body");
            }
            if (shown) root.transform.SetPositionAndRotation(feet, Quaternion.identity);

            if (Time.realtimeSinceStartup >= nextLog)
            {
                nextLog = Time.realtimeSinceStartup + 1f;
                if (shown)
                {
                    McLog.Info($"player model: {updates} updates in the last second, {data.Vertices / 3} triangles, {data.Batches} textures, " +
                               $"box from ({lastBounds.min.x:F2}, {lastBounds.min.y:F2}, {lastBounds.min.z:F2}) to ({lastBounds.max.x:F2}, {lastBounds.max.y:F2}, {lastBounds.max.z:F2}) units around the feet, at feet ({feet.x:F2}, {feet.y:F2}, {feet.z:F2})");
                }
                updates = 0;
            }
        }

        void SetShown(bool now, string why)
        {
            if (now == shown) return;
            shown = now;
            if (root != null) root.SetActive(now);
            McLog.Info(now ? "player model: showing your Minecraft body instead of the spore" : $"player model: hidden ({why})");
        }

        void Rebuild(MyceliumLink link, float scale)
        {
            int n = data.Vertices;
            var vertices = new Vector3[n];
            var uvs = new Vector2[n];
            var colors = new Color32[n];
            byte[] raw = data.Raw;
            for (int i = 0; i < n; i++)
            {
                int o = i * Proto.MVertexBytes;
                float x = System.BitConverter.ToSingle(raw, o);
                float y = System.BitConverter.ToSingle(raw, o + 4);
                float z = System.BitConverter.ToSingle(raw, o + 8);
                vertices[i] = new Vector3(x * scale, y * scale, -z * scale);
                // Minecraft's textures run from the top row; Unity's from the bottom.
                uvs[i] = new Vector2(System.BitConverter.ToSingle(raw, o + 12), 1f - System.BitConverter.ToSingle(raw, o + 16));
                colors[i] = new Color32(raw[o + 20], raw[o + 21], raw[o + 22], raw[o + 23]);
            }
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.colors32 = colors;
            mesh.subMeshCount = data.Batches;
            var materials = new Material[data.Batches];
            for (int b = 0; b < data.Batches; b++)
            {
                int first = data.First[b], count = data.Count[b];
                var tris = new int[count];
                // Same triangles, opposite winding: z is mirrored into Unity's axes.
                for (int t = 0; t + 2 < count; t += 3)
                {
                    tris[t] = first + t;
                    tris[t + 1] = first + t + 2;
                    tris[t + 2] = first + t + 1;
                }
                mesh.SetTriangles(tris, b, false);
                materials[b] = MaterialFor(link, data.Texture[b]);
            }
            mesh.RecalculateBounds();
            lastBounds = mesh.bounds;
            meshRenderer.sharedMaterials = materials;
            updates++;
        }

        Material MaterialFor(MyceliumLink link, int id)
        {
            if (link.TryReadTexture(id, out uint version, out int w, out int h, out System.IntPtr pixels))
            {
                if (textures.TryGetValue(id, out var known) && known.version == version) return known.material;
                if (known.material != null)
                {
                    Object.Destroy(known.material.mainTexture);
                    Object.Destroy(known.material);
                }
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
                {
                    name = $"Minecraft texture {id}",
                    filterMode = FilterMode.Point, // Minecraft's pixels stay sharp
                    wrapMode = TextureWrapMode.Clamp,
                };
                // Minecraft's rows run top-down; flip them so Unity's bottom-up rows match the flipped v.
                var bytes = new byte[w * h * 4];
                System.Runtime.InteropServices.Marshal.Copy(pixels, bytes, 0, bytes.Length);
                var flipped = new byte[bytes.Length];
                int row = w * 4;
                for (int y = 0; y < h; y++) System.Buffer.BlockCopy(bytes, y * row, flipped, (h - 1 - y) * row, row);
                tex.LoadRawTextureData(flipped);
                tex.Apply(false, false);
                var material = new Material(shader) { mainTexture = tex, name = $"Minecraft model {id}" };
                textures[id] = (version, material);
                McLog.Info($"player model: texture {id} from Minecraft, {w}x{h}");
                return material;
            }
            return fallback;
        }

        bool Create()
        {
            shader = Resources.Load<Shader>("MyceliumCraftModel");
            if (shader == null || !shader.isSupported)
            {
                failed = true;
                McLog.Info("player model: OFF, its shader (Resources/MyceliumCraftModel) is missing or not supported");
                return false;
            }
            fallback = new Material(shader) { name = "Minecraft model (no texture)" };
            root = new GameObject("MyceliumCraft Minecraft player");
            Object.DontDestroyOnLoad(root);
            mesh = new Mesh { name = "Minecraft player" };
            mesh.MarkDynamic();
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = root.AddComponent<MeshRenderer>();
            ApplyRenderMode(); // its own shadow, in place of the spore's
            meshRenderer.receiveShadows = false;
            root.SetActive(false);
            McLog.Info("player model: ready");
            return true;
        }
    }
}
