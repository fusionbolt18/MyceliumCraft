using System.Collections.Generic;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Test aid (-mcShowHitboxes): each enemy's Minecraft stand-in drawn in Mycelium's window as a
    /// wireframe box: the box Minecraft's client really has (what its crosshair picks), sent back
    /// by Minecraft. Green; red for a moment after a hit lands.
    /// Drawn on top of everything (Resources/MyceliumCraftLine.shader).
    /// </summary>
    sealed class HitboxView
    {
        const float FlashSeconds = 0.3f;
        static readonly Color Normal = new Color(0.2f, 1f, 0.3f, 0.9f);
        static readonly Color Hit = new Color(1f, 0.15f, 0.1f, 1f);

        readonly Dictionary<uint, LineRenderer> boxes = new Dictionary<uint, LineRenderer>();
        readonly Dictionary<uint, float> flashUntil = new Dictionary<uint, float>();
        readonly HashSet<uint> shown = new HashSet<uint>();
        readonly List<uint> gone = new List<uint>();
        readonly Vector3[] path = new Vector3[16];
        Material material;
        GameObject root;
        bool failed;

        public void Begin() => shown.Clear();

        /// <summary>One enemy's stand-in box, corners in Mycelium coordinates.</summary>
        public void Box(uint id, Vector3 min, Vector3 max)
        {
            if (failed || !Create()) return;
            if (!boxes.TryGetValue(id, out LineRenderer line) || line == null)
            {
                var go = new GameObject($"hitbox {id}");
                go.transform.SetParent(root.transform, false);
                line = go.AddComponent<LineRenderer>();
                line.sharedMaterial = material;
                line.useWorldSpace = true;
                line.positionCount = path.Length;
                line.widthMultiplier = 0.03f;
                line.numCapVertices = 0;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                boxes[id] = line;
            }
            shown.Add(id);
            Vector3 b0 = new Vector3(min.x, min.y, min.z), b1 = new Vector3(max.x, min.y, min.z), b2 = new Vector3(max.x, min.y, max.z), b3 = new Vector3(min.x, min.y, max.z);
            Vector3 up = Vector3.up * (max.y - min.y);
            Vector3 t0 = b0 + up, t1 = b1 + up, t2 = b2 + up, t3 = b3 + up;
            // One path along all 12 edges (some twice).
            path[0] = b0; path[1] = b1; path[2] = b2; path[3] = b3; path[4] = b0; path[5] = t0; path[6] = t1; path[7] = b1;
            path[8] = t1; path[9] = t2; path[10] = b2; path[11] = t2; path[12] = t3; path[13] = b3; path[14] = t3; path[15] = t0;
            line.SetPositions(path);
            bool flashing = flashUntil.TryGetValue(id, out float until) && Time.realtimeSinceStartup < until;
            Color c = flashing ? Hit : Normal;
            line.startColor = c;
            line.endColor = c;
        }

        /// <summary>Remove the boxes of enemies not given since Begin.</summary>
        public void End()
        {
            gone.Clear();
            foreach (var pair in boxes)
                if (!shown.Contains(pair.Key)) gone.Add(pair.Key);
            foreach (uint id in gone)
            {
                if (boxes[id] != null) Object.Destroy(boxes[id].gameObject);
                boxes.Remove(id);
                flashUntil.Remove(id);
            }
            if (boxes.Count != lastCount)
            {
                lastCount = boxes.Count;
                McLog.Info($"hitboxes: {lastCount} shown");
            }
        }

        int lastCount = -1;

        public void Flash(uint id) => flashUntil[id] = Time.realtimeSinceStartup + FlashSeconds;

        public int Count => boxes.Count;

        bool Create()
        {
            if (root != null) return true;
            Shader shader = Resources.Load<Shader>("MyceliumCraftLine");
            if (shader == null || !shader.isSupported)
            {
                failed = true;
                McLog.Info("hitboxes: line shader missing; not shown");
                return false;
            }
            material = new Material(shader);
            root = new GameObject("MyceliumCraft hitboxes");
            Object.DontDestroyOnLoad(root);
            McLog.Info("hitboxes: showing each enemy stand-in's box as Minecraft's client has it (green; red when hit)");
            return true;
        }
    }
}
