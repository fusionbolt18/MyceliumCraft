using System;
using System.Collections.Generic;
using System.Reflection;
using MyceliumCraft.Link;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Combat, Mycelium side (like SkyCraft's actor proxies):
    ///  - Mycelium's live enemies near the spore (EnemyHealth, found by name) go to Minecraft every
    ///    frame: position (feet), size (their solid colliders), health. Minecraft keeps an invisible
    ///    stand-in for each, so its own attack rules (weapon, cooldown, crits, sweeping, enchantments,
    ///    arrows) work on them.
    ///  - Hits on those stand-ins come back (Hits ring) and are applied with Mycelium's own
    ///    EnemyTakeDamage(damage x damageScale, wasFromWeapon: true): armour, damage numbers, blood,
    ///    sound and death as for the spore's own hits.
    /// </summary>
    sealed class EnemyCombat
    {
        const float Range = 64f;            // Mycelium units around the spore
        Type enemyType;
        FieldInfo currentHealthField, maxHealthField, deadField;
        MethodInfo takeDamage;
        readonly List<Component> enemies = new List<Component>();
        readonly Dictionary<uint, Component> byId = new Dictionary<uint, Component>();
        float nextFind;
        bool looked;
        readonly uint[] ids = new uint[Proto.EMax];
        readonly uint[] flags = new uint[Proto.EMax];
        readonly float[] values = new float[Proto.EMax * 8];
        uint hitsSeen;
        bool hitsStarted;
        int lastCount = -1;
        int hitsApplied;
        float nextEmptyLog;
        // Test aid "-mcShowHitboxes": draw each enemy's stand-in box in Mycelium.
        readonly HitboxView hitboxes = Array.IndexOf(Environment.GetCommandLineArgs(), "-mcShowHitboxes") >= 0 ? new HitboxView() : null;

        public int Sent => lastCount;
        public int HitsApplied => hitsApplied;

        /// <summary>Call every frame while the spore exists.</summary>
        public void Update(MyceliumLink link, Vector3 spore, WorldMapping map)
        {
            Find();
            if (enemyType == null) return;
            if (Time.realtimeSinceStartup >= nextFind)
            {
                nextFind = Time.realtimeSinceStartup + 0.5f;
                enemies.Clear();
                foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(enemyType)) enemies.Add((Component)o);
            }
            SendEnemies(link, spore, map);
            ApplyHits(link);
            FlashHurt();
        }

        // Minecraft's red hurt flash on enemies that lost health this frame (from anything).
        readonly HurtFlash hurt = new HurtFlash();
        public HurtFlash Hurt => hurt;
        /// <summary>The held weapon's knockback (WeaponBag), put on enemies Minecraft hits; NaN = none known.</summary>
        public float HeldKnockback = float.NaN;
        MethodInfo knockback;
        bool knockbackLooked;
        readonly Dictionary<Component, float> lastHealth = new Dictionary<Component, float>();
        readonly List<Component> forget = new List<Component>();

        void FlashHurt()
        {
            foreach (Component c in enemies)
            {
                if (c == null) continue;
                float health;
                try { health = (float)currentHealthField.GetValue(c); } catch (Exception) { continue; }
                if (lastHealth.TryGetValue(c, out float before) && health < before - 0.001f) hurt.Flash(c);
                lastHealth[c] = health;
            }
            forget.Clear();
            foreach (Component c in lastHealth.Keys) if (c == null) forget.Add(c);
            foreach (Component c in forget) lastHealth.Remove(c);
            hurt.Update();
        }

        void SendEnemies(MyceliumLink link, Vector3 spore, WorldMapping map)
        {
            byId.Clear();
            int n = 0;
            float scale = (float)map.Scale;

            foreach (Component c in enemies)
            {
                if (n >= Proto.EMax) break;
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                float health, max;
                bool dead;
                try
                {
                    health = (float)currentHealthField.GetValue(c);
                    max = (float)maxHealthField.GetValue(c);
                    dead = deadField != null && (bool)deadField.GetValue(c);
                }
                catch (Exception) { continue; }
                if (dead || health <= 0f) continue;
                if (!SolidBounds(c, out Bounds b)) continue;
                if ((b.center - spore).sqrMagnitude > Range * Range) continue;
                uint id = (uint)c.GetInstanceID();
                byId[id] = c;
                map.ToMc(b.center.x, b.min.y, b.center.z, out double x, out double y, out double z);
                ids[n] = id;
                flags[n] = Proto.EnAlive;
                int v = n * 8;
                values[v] = (float)x;
                values[v + 1] = (float)y;
                values[v + 2] = (float)z;
                values[v + 3] = Mathf.Max(0.1f, Mathf.Max(b.size.x, b.size.z) / scale);
                values[v + 4] = Mathf.Max(0.1f, b.size.y / scale);
                values[v + 5] = health;
                values[v + 6] = max;
                values[v + 7] = WorldMapping.MycYaw(c.transform.eulerAngles.y) ; // same flip both ways
                n++;
            }
            link.WriteEnemies(n, ids, flags, values);
            if (hitboxes != null) DrawMinecraftBoxes(link, map);
            if (n != lastCount || (n == 0 && Time.realtimeSinceStartup >= nextEmptyLog))
            {
                if (n == 0)
                {
                    // Why none: how many enemies exist at all, and how far the nearest live one is.
                    nextEmptyLog = Time.realtimeSinceStartup + 5f;
                    float nearest = float.MaxValue;
                    int live = 0;
                    foreach (Component c in enemies)
                    {
                        if (c == null || !c.gameObject.activeInHierarchy) continue;
                        try { if ((float)currentHealthField.GetValue(c) <= 0f) continue; } catch (Exception) { continue; }
                        live++;
                        nearest = Mathf.Min(nearest, Vector3.Distance(c.transform.position, spore));
                    }
                    McLog.Info($"combat: no enemies near: {enemies.Count} enemy objects in the scene, {live} live and active{(live > 0 ? $", nearest {nearest:F0} units away" : "")}");
                }
                lastCount = n;
                McLog.Info($"combat: {n} Mycelium enemies sent to Minecraft (live, within {Range:F0} units){(n > 0 ? $"; first #{ids[0]} '{byId[ids[0]].gameObject.name}' {values[3]:F2} x {values[4]:F2} blocks, health {values[5]:F0}/{values[6]:F0}" : "")}");
            }
        }

        readonly uint[] boxIds = new uint[Proto.HbMax];
        readonly float[] boxValues = new float[Proto.HbMax * 6];

        /// <summary>
        /// -mcShowHitboxes: the boxes Minecraft's client actually has for the stand-ins (what its
        /// crosshair picks), sent back by Minecraft, drawn in Mycelium's coordinates.
        /// </summary>
        void DrawMinecraftBoxes(MyceliumLink link, WorldMapping map)
        {
            int n = link.TryReadHitBoxes(boxIds, boxValues);
            if (n < 0) return;
            hitboxes.Begin();
            for (int i = 0; i < n; i++)
            {
                int o = i * 6;
                map.ToMyc(boxValues[o], boxValues[o + 1], boxValues[o + 2], out double ax, out double ay, out double az);
                map.ToMyc(boxValues[o + 3], boxValues[o + 4], boxValues[o + 5], out double bx, out double by, out double bz);
                var min = new Vector3((float)Math.Min(ax, bx), (float)Math.Min(ay, by), (float)Math.Min(az, bz));
                var max = new Vector3((float)Math.Max(ax, bx), (float)Math.Max(ay, by), (float)Math.Max(az, bz));
                hitboxes.Box(boxIds[i], min, max);
            }
            hitboxes.End();
        }

        /// <summary>The enemy's solid colliders together (not triggers): where Minecraft's stand-in goes.</summary>
        static bool SolidBounds(Component c, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (Collider col in c.GetComponentsInChildren<Collider>())
            {
                if (col.isTrigger || !col.enabled) continue;
                if (!any) { bounds = col.bounds; any = true; }
                else bounds.Encapsulate(col.bounds);
            }
            return any;
        }

        void ApplyHits(MyceliumLink link)
        {
            uint count = link.HitCount;
            if (!hitsStarted)
            {
                hitsStarted = true;
                hitsSeen = count; // older hits (another session) aren't ours to apply
                return;
            }
            if (count - hitsSeen > Proto.HRing) hitsSeen = count - Proto.HRing; // missed some: only the ring's are left
            while (hitsSeen != count)
            {
                link.ReadHit(hitsSeen, out uint id, out float damage, out uint hitFlags);
                hitsSeen++;
                if (!byId.TryGetValue(id, out Component enemy) || enemy == null)
                {
                    McLog.Info($"combat: hit on enemy #{id}, which isn't there any more");
                    continue;
                }
                float mycDamage = damage * McConfig.DamageScale;
                try
                {
                    // Through the held weapon first, as its own collision does: its attributes see
                    // the hit and may change the damage (Critical, Repulsive...).
                    string through = "";
                    float before = mycDamage;
                    mycDamage = WeaponHit(enemy, mycDamage, ref through);
                    takeDamage.Invoke(enemy, new object[] { mycDamage, false, true });
                    hitsApplied++;
                    AfterWeaponHit(enemy, mycDamage);
                    Knockback(enemy);
                    PlaySound("Impact", enemy.transform);
                    if (through.Length > 0 || Mathf.Abs(before - mycDamage) > 0.01f)
                        McLog.Info($"combat: through '{through}': {before:F1} -> {mycDamage:F1} Mycelium damage");
                    if (hitboxes != null) hitboxes.Flash(id);
                    float left = (float)currentHealthField.GetValue(enemy);
                    McLog.Info($"combat: Minecraft hit '{enemy.gameObject.name}' #{id} for {damage:F2} points{((hitFlags & Proto.HitCrit) != 0 ? " (critical)" : "")} -> {mycDamage:F1} Mycelium damage; health now {left:F1}");
                }
                catch (Exception e)
                {
                    McLog.Info($"combat: applying a hit failed: {(e.InnerException ?? e).Message}");
                }
            }
        }

        // --- Mycelium's own attack events, for Minecraft's swings and hits, so weapon attributes work
        // (your request, 2026-10-21). Attributes react to PlayerAttack.StartedAttack/FinishedAttack
        // (Fragile, Nimble, Enigmatic, Flourishing; WeaponStats rolls skill procs on FinishedAttack
        // after a hit) and to the weapon's WeaponCollision.HitEnemy (Critical, Repulsive, Sticky,
        // Nasty, Hazardous, Vampiric, Feline...). Minecraft's attacks raised none of them.
        const float SwingSeconds = 0.35f; // a swing "finishes" this long after it starts (or at the next)
        uint swingsSeen;
        bool swingsKnown;
        float swingEnds = -1f;
        int swingsRaised, hitsThroughWeapon;
        float nextSwingLog;

        /// <summary>Every frame: Minecraft's melee attack count (McState); raise Mycelium's attack events.</summary>
        public void FollowSwings(uint count, bool fresh)
        {
            if (fresh)
            {
                if (!swingsKnown) { swingsKnown = true; swingsSeen = count; }
                else if (count != swingsSeen)
                {
                    swingsSeen = count;
                    StartSwing();
                }
            }
            if (swingEnds >= 0f && Time.time >= swingEnds) FinishSwing();
            if (swingsRaised > 0 && Time.realtimeSinceStartup >= nextSwingLog)
            {
                nextSwingLog = Time.realtimeSinceStartup + 10f;
                McLog.Info($"combat: Mycelium's attack events raised for {swingsRaised} Minecraft swings, {hitsThroughWeapon} hits through the held weapon (last 10 s)");
                swingsRaised = hitsThroughWeapon = 0;
            }
        }

        void StartSwing()
        {
            if (swingEnds >= 0f) FinishSwing();
            RaiseAttack("StartedAttack");
            swingEnds = Time.time + SwingSeconds;
            swingsRaised++;
        }

        void FinishSwing()
        {
            swingEnds = -1f;
            RaiseAttack("FinishedAttack");
        }

        void RaiseAttack(string name)
        {
            GameObject parent = GameObject.Find("PlayerParent");
            Type t = Type.GetType("PlayerAttack, Assembly-CSharp");
            Component attack = parent != null && t != null ? parent.GetComponent(t) : null;
            if (attack == null) return;
            try { (t.GetField(name)?.GetValue(attack) as Action)?.Invoke(); }
            catch (Exception e)
            {
                Exception inner = e.InnerException ?? e;
                string where = inner.StackTrace ?? "";
                int cut = 0;
                for (int i = 0, lines = 0; i < where.Length && lines < 4; i++) if (where[i] == '\n') { lines++; cut = i; }
                McLog.Info($"combat: PlayerAttack.{name} failed in a listener: {inner.Message}\n{(cut > 0 ? where.Substring(0, cut) : where)}");
            }
        }

        /// <summary>The held weapon's WeaponCollision, or null.</summary>
        static Component HeldCollision()
        {
            GameObject parent = GameObject.Find("PlayerParent");
            Type st = Type.GetType("SwapWeapon, Assembly-CSharp"), ct = Type.GetType("WeaponCollision, Assembly-CSharp");
            Component swap = parent != null && st != null ? parent.GetComponent(st) : null;
            var weapon = swap != null ? st.GetProperty("O_curWeapon")?.GetValue(swap) as GameObject : null;
            return weapon != null && ct != null ? weapon.GetComponent(ct) : null;
        }

        /// <summary>
        /// A Minecraft hit through the held weapon, as WeaponCollision.OnTriggerEnter does: damage
        /// plus its Sentience and reflect bonuses into dmgDealt, HitEnemy raised (attributes may
        /// change dmgDealt), and what's left is the damage. A hit without a swing open (an arrow, a
        /// sweep's late part) opens one, so the swing-end attributes see it.
        /// </summary>
        float WeaponHit(Component enemy, float damage, ref string through)
        {
            if (swingEnds < 0f) StartSwing();
            Component wc = HeldCollision();
            if (wc == null) return damage;
            Type t = wc.GetType();
            float total = damage + Num(t.GetField("sentienceBonusDamage")?.GetValue(wc)) + Num(t.GetField("reflectBonusDamage")?.GetValue(wc));
            FieldInfo dealt = t.GetField("dmgDealt");
            dealt?.SetValue(wc, total);
            try { (t.GetField("HitEnemy")?.GetValue(wc) as Action<GameObject, float>)?.Invoke(enemy.gameObject, total); }
            catch (Exception e) { McLog.Info($"combat: a weapon attribute failed on the hit: {(e.InnerException ?? e).Message}"); }
            float after = dealt != null ? Num(dealt.GetValue(wc)) : total;
            t.GetField("reflectBonusDamage")?.SetValue(wc, 0f);
            through = wc.gameObject.name;
            hitsThroughWeapon++;
            return after;
        }

        /// <summary>After the damage, as WeaponCollision: Relentless Fury's frenzy heals a quarter of it.</summary>
        void AfterWeaponHit(Component enemy, float damage)
        {
            Component wc = HeldCollision();
            if (wc == null) return;
            object fury = wc.GetType().GetField("relentlessFury", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(wc);
            if (fury == null || !(fury.GetType().GetField("isFrenzied")?.GetValue(fury) is bool frenzied) || !frenzied) return;
            GameObject parent = GameObject.Find("PlayerParent");
            Type ht = Type.GetType("PlayerHealth, Assembly-CSharp");
            Component health = parent != null && ht != null ? parent.GetComponent(ht) : null;
            ht?.GetMethod("PlayerHeal")?.Invoke(health, new object[] { damage / 4f });
        }

        /// <summary>SoundEffectManager.Instance.PlaySound(name, at), as Mycelium's weapon does on a hit.</summary>
        static void PlaySound(string name, Transform at)
        {
            Type t = Type.GetType("SoundEffectManager, Assembly-CSharp");
            object manager = t?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                          ?? t?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (manager == null) return;
            foreach (MethodInfo m in t.GetMethods())
            {
                ParameterInfo[] ps = m.GetParameters();
                if (m.Name != "PlaySound" || ps.Length < 2 || ps[0].ParameterType != typeof(string) || ps[1].ParameterType != typeof(Transform)) continue;
                var args = new object[ps.Length];
                args[0] = name;
                args[1] = at;
                bool ok = true;
                for (int i = 2; i < ps.Length; i++)
                {
                    if (ps[i].HasDefaultValue) args[i] = ps[i].DefaultValue;
                    else { ok = false; break; }
                }
                if (!ok) continue;
                try { m.Invoke(manager, args); } catch (Exception) { }
                return;
            }
        }

        static float Num(object v) => v is float f ? f : v is double d ? (float)d : v is int i ? i : 0f;

        /// <summary>
        /// The held weapon's knockback on an enemy Minecraft hit, as Mycelium's own swing does
        /// (WeaponCollision: EnemyKnockback.Knockback(knockback, the spore, the enemy, false)).
        /// </summary>
        void Knockback(Component enemy)
        {
            if (float.IsNaN(HeldKnockback) || HeldKnockback <= 0f) return;
            Type kt = Type.GetType("EnemyKnockback, Assembly-CSharp");
            if (!knockbackLooked)
            {
                knockbackLooked = true;
                knockback = kt?.GetMethod("Knockback", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(float), typeof(Transform), typeof(Transform), typeof(bool) }, null);
                if (knockback == null) McLog.Info("combat: EnemyKnockback.Knockback not found; Minecraft's hits don't knock enemies back");
            }
            GameObject player = GameObject.FindWithTag("currentPlayer");
            Component k = kt != null ? enemy.GetComponent(kt) : null;
            if (knockback == null || k == null || player == null) return;
            try { knockback.Invoke(k, new object[] { HeldKnockback, player.transform, enemy.transform, false }); }
            catch (Exception e) { McLog.Info($"combat: knockback failed: {(e.InnerException ?? e).Message}"); }
        }

        void Find()
        {
            if (looked) return;
            looked = true;
            enemyType = Type.GetType("EnemyHealth, Assembly-CSharp");
            if (enemyType == null) { McLog.Info("combat: Mycelium's EnemyHealth not found; no combat"); return; }
            const BindingFlags inst = BindingFlags.Public | BindingFlags.Instance;
            currentHealthField = enemyType.GetField("currentHealth", inst);
            maxHealthField = enemyType.GetField("maxHealth", inst);
            deadField = enemyType.GetField("alreadyDead", inst);
            takeDamage = enemyType.GetMethod("EnemyTakeDamage", inst, null, new[] { typeof(float), typeof(bool), typeof(bool) }, null);
            if (currentHealthField == null || maxHealthField == null || takeDamage == null)
            {
                McLog.Info("combat: EnemyHealth's currentHealth/maxHealth/EnemyTakeDamage not found; no combat");
                enemyType = null;
                return;
            }
            McLog.Info($"combat: ready (damage scale {McConfig.DamageScale}: 1 Minecraft point = {McConfig.DamageScale} Mycelium damage)");
        }
    }
}
