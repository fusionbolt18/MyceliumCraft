using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Traits act only for the spore that's yours (your report, 2026-11-08: in a run with Nala, your
    /// skills went into no cooldown).
    ///
    /// Mycelium's traits (TraitBase subclasses, Assets/Resources/Traits) hook themselves onto
    /// Mycelium's static events (the Actions class: ActivatedSkill, ActivatedDodge, ...) when their
    /// spore becomes yours (SporeSelected), and are meant to unhook when it stops (SporeUnselected).
    /// Two of Mycelium's own bugs leave hooks behind:
    /// - SmartyPantsTrait's SporeUnselected hooks again (+= where it means -=). Each hook is a 15%
    ///   chance to cut a skill's cooldown to a quarter, so with many of them a cooldown is cut to
    ///   almost nothing on every use.
    /// - The skill menu makes a copy of your spore each time it shows one (SkillMenuManagerUpdated.
    ///   InstantiateCurrentSpore). The copy is still tagged "currentPlayer" while its traits wake up,
    ///   so they hook themselves on as if it were yours. Not all of them unhook when it's destroyed:
    ///   a destroyed ClumsyTrait copy threw on every dodge in your log.
    ///
    /// So after each swap (and once a second), every hook in Actions whose target is a trait is
    /// checked. It stays only if that trait still exists, is on the spore that's yours, and isn't a
    /// repeat of a hook already kept. Hooks that aren't traits' are never touched.
    ///
    /// SwappedCharacter is the exception: every spore's traits hook onto it all the time, to hear when
    /// their spore becomes yours. A first version removed those too, so no trait came on when you swapped.
    /// There, only hooks of destroyed traits (the skill menu's copies) and repeats are removed.
    /// </summary>
    sealed class TraitLeaks
    {
        Type traitType;
        FieldInfo[] events;
        bool looked;
        GameObject lastSpore;
        float nextCheck;
        int totalRemoved;

        /// <summary>For the test aid: hooks removed so far.</summary>
        public int TotalRemoved => totalRemoved;

        /// <summary>Every frame.</summary>
        public void Update()
        {
            GameObject spore = GameObject.FindWithTag("currentPlayer");
            if (spore == lastSpore && Time.realtimeSinceStartup < nextCheck) return;
            lastSpore = spore;
            nextCheck = Time.realtimeSinceStartup + 1f;
            Clean(spore);
        }

        /// <summary>Removes trait hooks that aren't your spore's. Returns how many were removed.</summary>
        public int Clean(GameObject spore)
        {
            if (!looked)
            {
                looked = true;
                traitType = Type.GetType("TraitBase, Assembly-CSharp");
                Type actions = Type.GetType("Actions, Assembly-CSharp");
                var list = new List<FieldInfo>();
                if (actions != null)
                    foreach (FieldInfo f in actions.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                        if (typeof(Delegate).IsAssignableFrom(f.FieldType)) list.Add(f);
                events = list.ToArray();
                McLog.Info($"traits: watching {events.Length} of Mycelium's events for trait hooks left behind (TraitBase found: {traitType != null})");
            }
            if (traitType == null || events.Length == 0) return 0;

            int removed = 0;
            var notes = new List<string>();
            foreach (FieldInfo f in events)
            {
                var d = f.GetValue(null) as Delegate;
                if (d == null) continue;
                bool everySpore = f.Name == "SwappedCharacter";
                Delegate[] hooks = d.GetInvocationList();
                var kept = new List<Delegate>(hooks.Length);
                var seen = new HashSet<(object, MethodInfo)>();
                int here = 0;
                foreach (Delegate h in hooks)
                {
                    object target = h.Target;
                    if (target == null || !traitType.IsInstanceOfType(target)) { kept.Add(h); continue; }
                    var trait = (Component)target;
                    // A destroyed trait compares equal to null in Unity.
                    bool yours = trait != null && (everySpore || (spore != null && trait.gameObject == spore));
                    if (yours && seen.Add((target, h.Method))) { kept.Add(h); continue; }
                    here++;
                    if (notes.Count < 6) notes.Add($"{target.GetType().Name}.{h.Method.Name} ({(trait == null ? "destroyed" : !yours ? $"on '{trait.gameObject.name}', not yours" : "a repeat")})");
                }
                if (here == 0) continue;
                removed += here;
                Delegate rebuilt = kept.Count == 0 ? null : Delegate.Combine(kept.ToArray());
                f.SetValue(null, rebuilt);
                notes.Insert(0, $"{f.Name}: {here} of {hooks.Length}");
            }
            if (removed > 0)
            {
                totalRemoved += removed;
                McLog.Info($"traits: removed {removed} trait hook(s) that weren't your spore's ('{(spore != null ? spore.name : "none")}'): {string.Join("; ", notes)}");
            }
            return removed;
        }
    }
}
