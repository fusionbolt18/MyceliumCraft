using System;
using System.Reflection;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Relentless Fury does what its description in your game says (your request, 2026-11-08):
    /// "Go into a frenzy gaining 30% attack speed. While active lose 5% of current health every
    /// second. Attacks restore health equal to 25% of weapon damage."
    ///
    /// Mycelium's code is a later version: the skill (RelentlessFury.DoSkill) adds a Fury effect to
    /// PlayerParent for 8 s, with its sound, particles and the timer on the skill icon, and its
    /// cooldown starts when the effect ends. Fury gives 10% damage per stack, a stack per kill, and
    /// the first stack is worth 0% (so it did nothing until a kill). Leftovers of the frenzy version
    /// are still there: RelentlessFury.isFrenzied, which the weapon's hit code reads to heal a quarter
    /// of its damage (WeaponCollision; EnemyCombat does the same for Minecraft's hits).
    ///
    /// While Minecraft drives, Fury's effect is kept for its length, sound, icon timer and cooldown,
    /// but turned into the frenzy:
    /// - its kill stacks are taken away (its hooks on Actions.EnemyKilled and SwapWeapon.SwappedWeapon
    ///   removed, its damage modifiers cleared), so no damage bonus and kills don't lengthen it;
    /// - attack speed x1.3: every weapon's swings per second (WeaponBag.AttackSpeedBonus), so the
    ///   Minecraft items' attack speed, which sets how fast you swing;
    /// - 5% of current health lost every second, straight from PlayerHealth.currentHealth (with the
    ///   bar's animation): not PlayerTakeDamage, which plays the hurt sound and blink every time. The
    ///   camera's hurt tilt and Minecraft's red flash leave it out;
    /// - RelentlessFury.isFrenzied on, and EnemyCombat heals a quarter of each Minecraft hit;
    /// - MycState's FURY flag: Minecraft shows red potion particles around you (MycFury.java).
    /// </summary>
    sealed class FuryWatch
    {
        const float SpeedBonus = 1.3f, DrainShare = 0.05f, DrainEvery = 1f;

        Type furyType, rfType, healthType, actionsType;
        bool looked;
        Component fury, tamed;
        bool on;
        float nextDrain, since, drained;
        int drains;

        /// <summary>The frenzy is on (while Minecraft drives).</summary>
        public bool On => on;

        /// <summary>Health taken by the drain since the last call (for the hurt tilt to leave out).</summary>
        public float TakeDrained()
        {
            float d = drained;
            drained = 0f;
            return d;
        }

        bool Look()
        {
            if (looked) return furyType != null;
            looked = true;
            furyType = Type.GetType("Fury, Assembly-CSharp");
            rfType = Type.GetType("RelentlessFury, Assembly-CSharp");
            healthType = Type.GetType("PlayerHealth, Assembly-CSharp");
            actionsType = Type.GetType("Actions, Assembly-CSharp");
            McLog.Info($"fury: Fury {furyType != null}, RelentlessFury {rfType != null}, PlayerHealth {healthType != null}, Actions {actionsType != null}");
            return furyType != null;
        }

        /// <summary>Every frame.</summary>
        public void Update(bool minecraftDriving, WeaponBag bag)
        {
            if (!Look()) return;
            GameObject parent = GameObject.Find("PlayerParent");
            Component f = parent != null ? parent.GetComponent(furyType) : null;
            if (f != null && f is Behaviour b && !b.enabled) f = null;
            fury = f;
            bool want = minecraftDriving && fury != null;
            if (want && !ReferenceEquals(tamed, fury)) Tame(fury, parent);
            if (want != on)
            {
                on = want;
                if (on)
                {
                    since = Time.time;
                    nextDrain = Time.time + DrainEvery;
                    drains = 0;
                    McLog.Info($"fury: Relentless Fury's frenzy on: attack speed x{SpeedBonus}, {DrainShare * 100f:F0}% of current health lost every second, hits heal 25% of their damage");
                }
                else
                {
                    McLog.Info($"fury: frenzy over after {Time.time - since:F1} s ({drains} drains){(fury == null ? "" : ": Minecraft stopped driving")}");
                }
                SetFrenzied(on);
            }
            bag.AttackSpeedBonus = on ? SpeedBonus : 1f;
            if (on && Time.time >= nextDrain && parent != null)
            {
                nextDrain += DrainEvery;
                Drain(parent);
            }
        }

        /// <summary>Fury's own effect without its stacks: its hooks off, its damage modifiers cleared.</summary>
        void Tame(Component f, GameObject parent)
        {
            tamed = f;
            int removed = Unhook(actionsType?.GetField("EnemyKilled", BindingFlags.Public | BindingFlags.Static), null, f);
            Component swap = null;
            foreach (Component c in parent.GetComponents<Component>()) if (c != null && c.GetType().Name == "SwapWeapon") swap = c;
            if (swap != null)
            {
                FieldInfo ev = swap.GetType().GetField("SwappedWeapon", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                removed += Unhook(ev, swap, f);
            }
            object stats = furyType.GetField("stats", BindingFlags.Public | BindingFlags.Instance)?.GetValue(f);
            stats?.GetType().GetMethod("ClearAllStatsFrom")?.Invoke(stats, new object[] { f });
            McLog.Info($"fury: Mycelium's Fury effect found on '{parent.name}': {removed} of its hooks (kill stacks, weapon swaps) taken off, its damage bonus cleared; it keeps its length ({furyType.GetField("currTimerMax")?.GetValue(f)} s), icon timer and cooldown");
        }

        static int Unhook(FieldInfo ev, object owner, Component target)
        {
            if (ev == null || !(ev.GetValue(owner) is Delegate d)) return 0;
            int n = 0;
            Delegate kept = d;
            foreach (Delegate h in d.GetInvocationList())
                if (ReferenceEquals(h.Target, target)) { kept = Delegate.Remove(kept, h); n++; }
            if (n > 0) ev.SetValue(owner, kept);
            return n;
        }

        void SetFrenzied(bool value)
        {
            GameObject spore = GameObject.FindWithTag("currentPlayer");
            Transform loadout = spore != null ? spore.transform.Find("SkillLoadout") : null;
            Component rf = loadout != null && rfType != null ? loadout.GetComponentInChildren(rfType, true) : null;
            rfType?.GetField("isFrenzied")?.SetValue(rf, value);
            if (rf == null && value) McLog.Info("fury: no RelentlessFury in your spore's skills (isFrenzied not set; Minecraft's hits still heal)");
        }

        void Drain(GameObject parent)
        {
            Component health = healthType != null ? parent.GetComponent(healthType) : null;
            FieldInfo cur = healthType?.GetField("currentHealth");
            if (health == null || cur == null || !(cur.GetValue(health) is float now) || now <= 0f) return;
            float loss = now * DrainShare;
            cur.SetValue(health, now - loss);
            drained += loss;
            drains++;
            // The bar slides down as it does for a hit (PlayerHealth.AnimateHealth, private).
            healthType.GetMethod("AnimateHealth", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(health, new object[] { -loss });
            McLog.Info($"fury: frenzy drain: health {now:F1} -> {now - loss:F1}");
        }
    }
}
