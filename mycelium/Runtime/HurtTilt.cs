using System;
using System.Reflection;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Minecraft's hurt tilt on Mycelium's camera: when the spore loses health, the view leans toward
    /// whatever hit it and swings back, over Minecraft's half second (10 ticks), as far as Minecraft's
    /// "Damage Tilt" setting says (10 degrees at full; Minecraft's own is 14, which felt too strong).
    ///
    /// Minecraft (GameRenderer.bobHurt): with f = time left / 0.5 s, the lean is sin(f^4 * pi) * 14
    /// degrees: it peaks in the first 0.08 s and eases back. It turns the view about a level axis
    /// at right angles to the hit's direction (measured from where the player faces, not from the
    /// camera), so the top of the view leans toward the hit: a hit from the left rolls the view left,
    /// a hit from in front nods it down.
    ///
    /// Mycelium doesn't say what did the damage (PlayerHealth.PlayerTakeDamage takes only an amount),
    /// so at the moment health drops the hitter is looked for:
    ///  1. the nearest collider within NearRadius of the spore that belongs to an enemy, or carries an
    ///     attack-looking script (a hitbox, a projectile, a cactus' CactusDamage...), not the spore's own;
    ///  2. else the nearest live enemy within EnemyRange;
    ///  3. else none: the view rolls to the left, as Minecraft's does when its hurt direction is 0.
    /// </summary>
    sealed class HurtTilt
    {
        const float Seconds = 0.5f;    // Minecraft's hurt time: 10 ticks
        const float MaxDegrees = 10f;  // Minecraft's is 14 at "Damage Tilt" 100 %; a little less (your note, 2026-10-19)
        const float NearRadius = 4f;   // where a hitter is looked for, from the spore's middle (units)
        const float EnemyRange = 15f;  // else the nearest live enemy this close
        static readonly string[] HitterWords = { "Attack", "Hitbox", "Projectile", "Damage", "Shell", "Tail", "Collision" };

        float lastHealth = float.NaN, lastMax = float.NaN;
        float started = -1f;
        float sourceYaw = float.NaN; // world heading (Unity degrees) from the spore to the hitter; NaN = not found
        float strength = 1f;
        float peak;
        int frames;
        readonly Collider[] near = new Collider[64];
        Type enemyType;
        FieldInfo deadField;
        bool typesLooked;

        /// <summary>Leaning now (for the trace).</summary>
        public bool Active => started >= 0f;

        /// <summary>Health lost since the last Update that isn't a hit (Relentless Fury's drain, FuryWatch).</summary>
        public float IgnoreDrop;

        /// <summary>
        /// Every frame, with Mycelium's health (0/0 when not known) and the spore. damageTilt is
        /// Minecraft's setting (0..1). Starts a tilt when health drops (not to 0: Minecraft shows its
        /// death fall instead).
        /// </summary>
        public void Update(float health, float max, Rigidbody spore, Component playerHealth, float damageTilt)
        {
            if (started >= 0f && Time.unscaledTime - started >= Seconds)
            {
                McLog.Info($"hurt tilt: done, peak {peak:F1} degrees over {frames} frames");
                started = -1f;
            }
            bool hurt = spore != null && max > 0f && max == lastMax && health < lastHealth - IgnoreDrop - 0.001f && health > 0f;
            IgnoreDrop = 0f;
            if (hurt)
            {
                strength = damageTilt >= 0f && damageTilt <= 1f ? damageTilt : 1f;
                Vector3 centre = spore.worldCenterOfMass;
                string how = FindHitter(centre, spore.transform, playerHealth != null ? playerHealth.transform : null, out Vector3 at);
                Vector3 d = at - centre;
                d.y = 0f;
                sourceYaw = how != null && d.sqrMagnitude > 1e-4f ? Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg : float.NaN;
                float facing = spore.rotation.eulerAngles.y;
                McLog.Info($"hurt tilt: health {lastHealth:F1} -> {health:F1}; " +
                           (float.IsNaN(sourceYaw) ? "hitter not found, rolling left" :
                            $"hit from {how}, {d.magnitude:F1} units away, {Side(Mathf.DeltaAngle(facing, sourceYaw))} of the spore") +
                           $"; Minecraft's Damage Tilt {strength * 100f:F0} %");
                if (started >= 0f) McLog.Info($"hurt tilt: (the last one ended early: peak {peak:F1} degrees over {frames} frames)");
                started = Time.unscaledTime;
                peak = 0f;
                frames = 0;
            }
            lastHealth = health;
            lastMax = max;
        }

        /// <summary>
        /// The lean to put on the camera now, in the camera's own space (camera.rotation * this).
        /// playerYaw: where the player faces (Unity degrees), which the hit's direction is measured from.
        /// </summary>
        public Quaternion Rotation(float playerYaw)
        {
            if (started < 0f) return Quaternion.identity;
            float left = 1f - (Time.unscaledTime - started) / Seconds;
            if (left <= 0f) return Quaternion.identity;
            float f = left * left * left * left;
            float degrees = Mathf.Sin(f * Mathf.PI) * MaxDegrees * strength;
            // The hit's direction in the view's level plane: 0 ahead, 90 right, -90 left.
            float rel = float.IsNaN(sourceYaw) ? -90f : Mathf.DeltaAngle(playerYaw, sourceYaw);
            Vector3 toward = new Vector3(Mathf.Sin(rel * Mathf.Deg2Rad), 0f, Mathf.Cos(rel * Mathf.Deg2Rad));
            float a = degrees * Mathf.Deg2Rad;
            peak = Mathf.Max(peak, degrees);
            frames++;
            // Turn the view's up toward the hit by that much.
            return Quaternion.FromToRotation(Vector3.up, Vector3.up * Mathf.Cos(a) + toward * Mathf.Sin(a));
        }

        static string Side(float rel) =>
            rel >= -45f && rel <= 45f ? $"in front ({rel:F0} degrees)" :
            rel > 45f && rel < 135f ? $"to the right ({rel:F0} degrees)" :
            rel < -45f && rel > -135f ? $"to the left ({rel:F0} degrees)" : $"behind ({rel:F0} degrees)";

        /// <summary>What hit the spore, and where it is; null if nothing likely is near.</summary>
        string FindHitter(Vector3 centre, Transform spore, Transform player, out Vector3 at)
        {
            at = centre;
            if (!typesLooked)
            {
                typesLooked = true;
                enemyType = Type.GetType("EnemyHealth, Assembly-CSharp");
                if (enemyType != null) deadField = enemyType.GetField("alreadyDead", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }
            string best = null;
            float bestDist = float.MaxValue;
            int n = Physics.OverlapSphereNonAlloc(centre, NearRadius, near, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                Collider c = near[i];
                if (c == null || !c.enabled) continue;
                Transform ct = c.transform;
                if (ct.IsChildOf(spore) || (player != null && ct.IsChildOf(player))) continue;
                if (c.CompareTag("currentWeapon") || c.CompareTag("Weapon")) continue;
                Component enemy = enemyType != null ? c.GetComponentInParent(enemyType) : null;
                string why = enemy != null ? "enemy" : AttackScript(c);
                if (why == null) continue;
                // Nearest by the collider's surface; the direction from the enemy itself (or the
                // middle of the hitbox): a beetle's body touching the spore gave a point 0.1 units
                // from its middle, which says little about which side the beetle is on.
                float dist = (c.bounds.ClosestPoint(centre) - centre).sqrMagnitude;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = $"'{(enemy != null ? enemy.gameObject.name : c.gameObject.name)}' ({why})";
                    at = enemy != null ? enemy.transform.position : c.bounds.center;
                }
            }
            if (best != null || enemyType == null) return best;
            foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(enemyType))
            {
                var e = (Component)o;
                if (e == null || !e.gameObject.activeInHierarchy) continue;
                try { if (deadField != null && (bool)deadField.GetValue(e)) continue; }
                catch (Exception) { }
                float dist = (e.transform.position - centre).sqrMagnitude;
                if (dist < bestDist && dist <= EnemyRange * EnemyRange)
                {
                    bestDist = dist;
                    best = $"'{e.gameObject.name}' (nearest enemy)";
                    at = e.transform.position;
                }
            }
            return best;
        }

        /// <summary>The name of an attack-looking script on this collider's object, or null.</summary>
        static string AttackScript(Collider c)
        {
            foreach (MonoBehaviour m in c.GetComponents<MonoBehaviour>())
            {
                if (m == null) continue;
                string name = m.GetType().Name;
                if (name == "WeaponCollision") continue; // the spore's own weapon
                foreach (string w in HitterWords)
                    if (name.IndexOf(w, StringComparison.Ordinal) >= 0) return name;
            }
            return null;
        }
    }
}
