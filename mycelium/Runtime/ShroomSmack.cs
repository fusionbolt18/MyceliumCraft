using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Minecraft's swings smack the levels' mushrooms (your request, 2026-11-02: in Mycelium you hit
    /// them and they bounce with a sound).
    ///
    /// Mycelium's mushrooms (SmackableShroomController, and SmackableGlowShroomController, which
    /// also glows and lets out nutrients the first time) bounce from their own OnTriggerEnter when
    /// the "currentWeapon" collider touches them. That collider only moves through them in
    /// Mycelium's own swing animation, which doesn't play while Minecraft attacks. So, at each
    /// Minecraft swing's hit moment, the mushrooms inside that swing are found here the way Better
    /// Combat finds targets (the weapon's reach x 1.25, as MycWeapons.java gives it, and its
    /// category's shape: slashes 180 degrees wide, stabs and smashes a narrow box ahead), and their
    /// own OnTriggerEnter is called with the held weapon's collider. Their own cooldowns, sounds,
    /// animations and nutrients stay Mycelium's; nothing in Mycelium is changed.
    /// </summary>
    sealed class ShroomSmack
    {
        static readonly string[] Types = { "SmackableShroomController", "SmackableGlowShroomController" };
        const double ReachScale = 1.25;    // as MycWeapons.REACH_SCALE
        const double BaseReach = 3.0;      // as MycWeapons.BASE_REACH: before the weapon's reach is measured
        const float BoxHalfWidthBlocks = 0.75f; // stabs and smashes (Better Combat's FORWARD_BOX)

        readonly List<Component> shrooms = new List<Component>();
        float nextFind;
        float smackAt = -1f;
        int smackType;
        float smackReachBlocks;
        int swings, smacked;
        float nextLog;

        public void SceneLoaded()
        {
            shrooms.Clear();
            nextFind = 0f;
            smackAt = -1f;
        }

        /// <summary>A Minecraft swing started. type: 0 slash, 1 smash, 2 stab. Its hit comes after the upswing.</summary>
        public void Swung(int type, float reachBlocks, float perSecond)
        {
            // Better Combat hits at the upswing: half the attack's duration (our presets' "upswing" 0.5).
            float duration = perSecond > 0.05f ? 1f / perSecond : 0.6f;
            smackAt = Time.time + Mathf.Min(0.5f * duration, 0.6f);
            smackType = type;
            smackReachBlocks = reachBlocks;
            swings++;
        }

        /// <summary>Every frame. from: the spore's position; lookYawMyc: Minecraft's look yaw in Mycelium's frame.</summary>
        public void Update(Vector3 from, float lookYawMyc, double scale)
        {
            if (Time.realtimeSinceStartup >= nextFind) Find();
            if (smackAt >= 0f && Time.time >= smackAt)
            {
                smackAt = -1f;
                double reach = (smackReachBlocks > 0.1f ? smackReachBlocks : BaseReach) * ReachScale;
                Smack(from, lookYawMyc, (float)(reach * scale), smackType, (float)(BoxHalfWidthBlocks * scale), "swing");
            }
            if ((swings > 0 || smacked > 0) && Time.realtimeSinceStartup >= nextLog)
            {
                nextLog = Time.realtimeSinceStartup + 10f;
                McLog.Info($"shrooms: {smacked} mushrooms smacked by {swings} Minecraft swings (last 10 s; {shrooms.Count} in the level)");
                swings = smacked = 0;
            }
        }

        /// <summary>Smack every mushroom inside the swing: returns how many it reached.</summary>
        public int Smack(Vector3 from, float lookYawMyc, float reach, int type, float halfWidth, string why)
        {
            GameObject weapon = GameObject.FindWithTag("currentWeapon");
            Collider weaponCol = weapon != null ? weapon.GetComponent<Collider>() : null;
            if (weaponCol == null) return 0;
            Vector3 ahead = Quaternion.Euler(0f, lookYawMyc, 0f) * Vector3.forward;
            int n = 0;
            foreach (Component s in shrooms)
            {
                if (s == null || !s.gameObject.activeInHierarchy) continue;
                Vector3 p = Nearest(s, from);
                Vector3 d = p - from;
                Vector3 flat = new Vector3(d.x, 0f, d.z);
                float dist = flat.magnitude;
                if (dist > reach || Mathf.Abs(d.y) > reach) continue;
                float forward = Vector3.Dot(flat, ahead);
                bool inside = type == 0
                    ? forward >= 0f                                             // slash: the half circle ahead (180 degrees)
                    : forward >= 0f && Mathf.Abs(Vector3.Cross(ahead, flat).y) <= halfWidth; // stab, smash: a box ahead
                if (!inside) continue;
                if (Hit(s, weaponCol))
                {
                    n++;
                    smacked++;
                    McLog.Info($"shrooms: {why} smacked '{s.gameObject.name}' ({s.GetType().Name}) {dist:F2} units away, {Vector3.SignedAngle(ahead, flat, Vector3.up):F0} deg from your look");
                }
            }
            return n;
        }

        /// <summary>Its own OnTriggerEnter, with the held weapon's collider: it bounces only if its cooldown allows.</summary>
        static bool Hit(Component s, Collider weaponCol)
        {
            Type t = s.GetType();
            bool before = Attackable(s);
            try { t.GetMethod("OnTriggerEnter", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(s, new object[] { weaponCol }); }
            catch (Exception e) { McLog.Info($"shrooms: smacking '{s.gameObject.name}' failed: {(e.InnerException ?? e).Message}"); return false; }
            return before && !Attackable(s);
        }

        public static bool Attackable(Component s) =>
            s.GetType().GetField("Attackable", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(s) is bool b && b;

        /// <summary>
        /// The mushroom's attack hitbox: the trigger colliders on its own object, the only ones
        /// whose touch reaches its OnTriggerEnter (it has no Rigidbody). Not the glow sensor, a
        /// separate trigger on a child of its model (MushroomPlayerSensorController: it lights up
        /// when the spore walks near), nor solid ones. (Your note, 2026-11-02; in the prefabs the
        /// hitbox is a capsule, radius 1, height 1, 1.62 up, scaled with the mushroom.)
        /// </summary>
        public static List<Collider> Hitbox(Component s)
        {
            var list = new List<Collider>();
            foreach (Collider c in s.GetComponents<Collider>())
                if (c != null && c.enabled && c.isTrigger) list.Add(c);
            return list;
        }

        /// <summary>The point of the mushroom's attack hitbox (else its position) nearest to from.</summary>
        static Vector3 Nearest(Component s, Vector3 from)
        {
            Vector3 best = s.transform.position;
            float bestD = float.MaxValue;
            foreach (Collider c in Hitbox(s))
            {
                Vector3 p = c is MeshCollider m && !m.convex ? c.bounds.ClosestPoint(from) : c.ClosestPoint(from);
                float d = (p - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        void Find()
        {
            nextFind = Time.realtimeSinceStartup + 3f; // levels are generated: mushrooms can appear later
            int before = shrooms.Count;
            shrooms.Clear();
            foreach (string name in Types)
            {
                Type t = Type.GetType(name + ", Assembly-CSharp");
                if (t == null) continue;
                foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(t)) if (o is Component c) shrooms.Add(c);
            }
            if (shrooms.Count != before)
            {
                McLog.Info($"shrooms: {shrooms.Count} smackable mushrooms in the level");
                if (shrooms.Count > 0) LogColliders(shrooms[0]);
            }
        }

        /// <summary>Every collider of one mushroom, and which one is its attack hitbox, for the log.</summary>
        static void LogColliders(Component s)
        {
            var hit = Hitbox(s);
            var parts = new List<string>();
            foreach (Collider c in s.GetComponentsInChildren<Collider>(true))
            {
                string role = hit.Contains(c) ? "ATTACK HITBOX" : c.GetComponent("MushroomPlayerSensorController") != null ? "glow sensor" : c.isTrigger ? "other trigger" : "solid";
                parts.Add($"'{c.gameObject.name}' {c.GetType().Name} {role}, {c.bounds.size.x:F2} wide x {c.bounds.size.y:F2} tall");
            }
            McLog.Info($"shrooms: '{s.gameObject.name}' colliders: {string.Join("; ", parts)}");
        }

        /// <summary>For the test aid: the mushroom nearest to from.</summary>
        public Component NearestShroom(Vector3 from)
        {
            Find();
            Component best = null;
            float bestD = float.MaxValue;
            foreach (Component s in shrooms)
            {
                if (s == null) continue;
                float d = (s.transform.position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }
    }
}
