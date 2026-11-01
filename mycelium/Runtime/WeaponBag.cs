using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MyceliumCraft.Link;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MyceliumCraft
{
    /// <summary>
    /// Your weapons live in Minecraft's inventory, one carrot each (your choice, 2026-10-20); this is
    /// Mycelium's side of it.
    ///
    ///  - Picking a weapon up still goes through Mycelium (WeaponInteraction.Interact: the new one in
    ///    the hand, attributes unequipped/equipped), but the one you held isn't left on the ground:
    ///    it's saved into the bag, the way Mycelium saves the held weapon between levels
    ///    (LevelEnd.GoToNextLevel: attribute names, values and rarities, their stats cleared from the
    ///    weapon, its stat list kept), and its object is removed.
    ///  - The spore holds the weapon whose carrot Minecraft has selected: switching rebuilds it much
    ///    the way Mycelium does in a new level (LevelGenerator: the prefab from Resources, no random
    ///    attribute, the attributes added back and Equipped), so attribute effects (stat attributes,
    ///    Vampiric...) and temporary buffs (they follow SwapWeapon.O_curWeapon) work as Mycelium's
    ///    own. Unlike LevelGenerator, attributes the prefab already has (a Cactus Mace's Hazardous and
    ///    Nasty) aren't added again, and the weapon keeps its own stat list: re-adding them doubled
    ///    them on every switch ("Heavy Hazardous Nasty Hazardous Nasty Rose Mace").
    ///  - An empty hand in Minecraft (no weapon carrot): the held weapon goes into the bag (its
    ///    attributes unequipped) and the spore holds an invisible Stick with only Mycelium's
    ///    "Nothing" attribute (Mycelium always needs a held weapon, and its level change expects at
    ///    least one attribute): no weapon effects, no knockback. Before, the last weapon stayed
    ///    (your report, 2026-10-20).
    ///  - After a pickup Minecraft is asked to select the new weapon's carrot.
    ///  - Throwing one in Minecraft (your choice, 2026-10-21): the item flies through Minecraft's
    ///    world (its own physics, on Mycelium's ground) and Minecraft sends where it is every frame
    ///    (Thrown block). The real weapon leaves the bag and is put in the world (the held one leaves
    ///    the hand, attributes unequipped, and the hand is emptied; one from the bag is built from its
    ///    prefab with its attributes), follows the item, and stays as a pickup when Minecraft removes
    ///    the item after it lands.
    ///  - Arriving in The Carcass (a death or a finished run) empties the bag; the next run's Stick
    ///    is added when the spore gets it.
    ///  - For each weapon the numbers a swing has with it, with the spore as it is now (Primal and
    ///    Speed levels with that weapon's stat attributes instead of the held one's):
    ///      damage = Primal damage x Fungal Might x the weapon's damage multiplier (PlayerAttack),
    ///      swings per second = the spore's attack speed x the weapon's speed modifier / (0.9 x the
    ///      attack state's length): PlayerAttack gives the controls back at 90 % of the Slash, Smash
    ///      or Stab state.
    ///    Minecraft writes them onto the carrots (MycWeapons.java) and rewrites them when they change.
    /// Everything Mycelium is reached by name (reflection), like the rest of the mod.
    /// </summary>
    sealed class WeaponBag
    {
        sealed class Weapon
        {
            public uint Id;
            public string Path;        // Resources path, e.g. "Slash/Stick"
            public string Name;        // WeaponStats.wpnName
            public int Type;           // 0 Slash, 1 Smash, 2 Stab
            public readonly List<(string type, float value, object rating)> Atts = new List<(string, float, object)>();
            public string AttText = "";
            public float DamageMult = 1f, SpeedMod = 1f, Knockback;
            public int PrimalAmount, SpeedAmount; // from its stat attributes, applied while held
            public GameObject Live;    // its object while held
            // Numbers sent to Minecraft
            public float Damage, PerSecond;
            public float Reach;          // blocks, from MeasureReach; 0 = not measured yet
            public GameObject ReachFrom; // the held object it was measured on
        }

        const string Carcass = "The Carcass";
        const float FollowWaitSeconds = 3f;
        readonly List<Weapon> bag = new List<Weapon>();
        uint nextId = 1, heldId, version = 1;
        bool emptyHand; // the spore holds the invisible stand-in (heldId is 0 then)
        // Thrown weapons: Minecraft's Thrown table, and the real weapons flying with their items.
        readonly uint[] thrIds = new uint[Proto.ThrMax], thrFlags = new uint[Proto.ThrMax];
        readonly double[] thrXyz = new double[Proto.ThrMax * 3];
        readonly Dictionary<uint, GameObject> flying = new Dictionary<uint, GameObject>();
        readonly HashSet<uint> seenThrown = new HashSet<uint>();
        const float ThrownLift = 0.25f; // blocks: the weapon floats this far above the item's feet
        const string EmptyHandPath = "Slash/Stick";
        GameObject lastCur;
        bool busy;
        GameObject expected;
        uint selectSeq, selectId;
        uint pendingSelect;
        float pendingSince;
        float nextLog;
        readonly Dictionary<string, float> clipLength = new Dictionary<string, float>();
        Animator clipsFrom;

        /// <summary>The held weapon's knockback (Mycelium units), for Minecraft's hits; NaN if not known.</summary>
        public float HeldKnockback
        {
            get
            {
                Weapon w = Find(heldId);
                return w != null ? w.Knockback : float.NaN;
            }
        }

        public void SceneLoaded(Scene scene)
        {
            lastCur = null;
            busy = false;
            expected = null;
            clipsFrom = null;
            if (scene.name != Carcass || bag.Count == 0) return;
            McLog.Info($"weapons: arrived in The Carcass (a death or a finished run): the bag's {bag.Count} weapons are gone; the next run starts with a Stick");
            bag.Clear();
            heldId = 0;
            emptyHand = false;
            flying.Clear();
            pendingSelect = 0;
            version++;
        }

        /// <summary>Every frame: watch the held weapon, follow Minecraft's selection, send the table.</summary>
        public void Update(MonoBehaviour host, MyceliumLink link, bool minecraftDriving, uint minecraftHand, WorldMapping map)
        {
            this.host = host;
            Component swap = FindSwap();
            if (swap == null) { link.WriteWeapons(version, 0, null, selectSeq, selectId, 0, McConfig.DamageScale); return; }
            GameObject cur = Get(swap, "O_curWeapon") as GameObject;
            if (cur == null) cur = null; // destroyed objects compare equal to null
            if (!ReferenceEquals(cur, lastCur) && cur != null) Watch(cur);
            lastCur = cur;

            // Not mid-switch: the held weapon's stat attributes are off for a moment then, and every
            // other carrot's numbers wobbled and were rewritten.
            Weapon held = Find(heldId);
            if (!busy && held != null && held.Live != null) ReadLive(held, held.Live);
            bool changed = !busy && Compute(held);
            if (!busy && held != null && held.Live != null && !ReferenceEquals(held.ReachFrom, held.Live) && MeasureReach(held, map)) changed = true;
            if (changed) version++;

            if (minecraftDriving) Follow(host, minecraftHand);
            if (minecraftDriving) FollowThrown(host, link, map);
            link.WriteWeapons(version, bag.Count, Table(), selectSeq, selectId, heldId, McConfig.DamageScale);

            if (Time.realtimeSinceStartup >= nextLog && bag.Count > 0)
            {
                nextLog = Time.realtimeSinceStartup + 10f;
                var parts = new List<string>();
                foreach (Weapon w in bag) parts.Add($"#{w.Id} {w.Name} {w.Damage:F1} dmg {w.PerSecond:F2}/s{(w.Id == heldId ? " (held)" : "")}");
                McLog.Info($"weapons: {string.Join(", ", parts)}; Minecraft's hand #{minecraftHand}");
            }
        }

        /// <summary>The held weapon object changed: a pickup, a level's restore, or our own switch.</summary>
        void Watch(GameObject cur)
        {
            if (ReferenceEquals(cur, expected))
            {
                expected = null;
                Weapon w = Find(heldId);
                if (w != null) w.Live = cur;
                return;
            }
            bool pickup = lastCur != null && lastCur.CompareTag("Weapon") && lastCur.transform.parent == null;
            foreach (uint key in new List<uint>(flying.Keys))
                if (ReferenceEquals(flying[key], cur)) flying.Remove(key); // caught in flight: yours again
            if (pickup) HideStatsAfterPickup(lastCur, cur);
            if (pickup && emptyHand && heldId == 0)
            {
                // Picked up with an empty hand: the stand-in was let go; it's not a weapon of yours.
                UnityEngine.Object.Destroy(lastCur);
                emptyHand = false;
                Weapon got = NewWeapon(cur, "picked up with an empty hand");
                heldId = got.Id;
                AskSelect(got.Id);
                version++;
                return;
            }
            if (pickup)
            {
                // WeaponInteraction.Interact: the held weapon was let go (its attributes already
                // unequipped) and the picked-up one is in the hand. Into the bag with the old one.
                Weapon old = Find(heldId) ?? NewWeapon(lastCur, "the weapon you held");
                Stash(old, lastCur, unequip: false);
                UnityEngine.Object.Destroy(lastCur);
                Weapon fresh = NewWeapon(cur, "picked up");
                heldId = fresh.Id;
                AskSelect(fresh.Id);
                McLog.Info($"weapons: picked up '{fresh.Name}' (#{fresh.Id}); '{old.Name}' (#{old.Id}) kept in the bag instead of dropped");
                version++;
                return;
            }
            if (emptyHand && heldId == 0 && lastCur == null)
            {
                // A new level with an empty hand: Mycelium rebuilt the stand-in; hide it again.
                HideStandIn(cur);
                McLog.Info("weapons: new level, the spore's hand is still empty");
                return;
            }
            Weapon same = Find(heldId);
            if (same != null && lastCur == null)
            {
                same.Live = cur;
                if (ReferenceEquals(lastCur, null))
                {
                    // A new level: Mycelium rebuilt the held weapon from what it saved.
                    McLog.Info($"weapons: new level, the spore holds '{same.Name}' (#{same.Id}) again");
                }
                else
                {
                    // In the middle of a level Mycelium destroyed the held weapon and put another in
                    // the hand: Fragile broke (a Stick now) or Enigmatic changed it (a random weapon,
                    // same attributes). It takes the old one's place in the bag, and its carrot.
                    ReadLive(same, cur);
                    version++;
                    McLog.Info($"weapons: Mycelium replaced the held weapon (Fragile broke it, or Enigmatic changed it): #{same.Id} is '{same.Name}' ({same.Path}) now");
                }
                return;
            }
            Weapon w2 = NewWeapon(cur, bag.Count == 0 ? "the run's first weapon" : "given by Mycelium");
            heldId = w2.Id;
            AskSelect(w2.Id);
            version++;
        }

        MonoBehaviour host;

        /// <summary>
        /// The stat panel slides away after picking up a weapon that changes stats (your report,
        /// 2026-11-01: it never did). Mycelium slides it in when you near such a weapon
        /// (WeaponInteraction.CreateTooltip: HUDStats.ShowStats) and away when a weapon's pop-up
        /// closes, but not when it closes because you picked the weapon up. In Mycelium the weapon you
        /// let go of then lies there, becomes the next pop-up, and hides it when you walk away. Here
        /// it goes into the bag instead, so nothing did. So 1 s after such a pickup (the stats'
        /// flash shows meanwhile) HUDStats.HideStats, Mycelium's own, unless another weapon's pop-up
        /// is open: that one hides it when it closes, as in Mycelium.
        /// </summary>
        void HideStatsAfterPickup(GameObject from, GameObject to)
        {
            if (host == null || !(ChangesStats(from) || ChangesStats(to))) return;
            host.StartCoroutine(HideStatsSoon());
        }

        IEnumerator HideStatsSoon()
        {
            yield return new WaitForSeconds(1f);
            Type tm = Type.GetType("TooltipManager, Assembly-CSharp");
            object manager = tm?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var open = manager != null ? tm.GetField("currentParent", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(manager) as GameObject : null;
            if (open != null && open.CompareTag("Weapon"))
            {
                McLog.Info($"weapons: stat panel left up: '{open.name}''s pop-up is open and hides it when it closes");
                yield break;
            }
            Component stats = FindComp("HUD", "HUDStats");
            if (stats == null) yield break;
            Call(stats, "HideStats", 0f);
            McLog.Info("weapons: stat panel slid away after the pickup (Mycelium leaves it up when the dropped weapon isn't there to walk away from)");
        }

        /// <summary>Has this weapon an attribute that changes the spore's stats (AttributeBase.statChange)?</summary>
        static bool ChangesStats(GameObject o)
        {
            if (o == null) return false;
            foreach (Component c in o.GetComponents<Component>())
                if (c != null && IsA(c.GetType(), "AttributeBase") && Get(c, "statChange") is bool b && b) return true;
            return false;
        }

        Weapon NewWeapon(GameObject o, string why)
        {
            var w = new Weapon { Id = nextId++, Live = o };
            ReadLive(w, o);
            bag.Add(w);
            McLog.Info($"weapons: '{w.Name}' (#{w.Id}, {w.Path}) into the bag ({why}): damage x{w.DamageMult:F2}, speed x{w.SpeedMod:F2}, knockback {w.Knockback:F1}{(w.AttText.Length > 0 ? ", " + w.AttText : "")}");
            return w;
        }

        /// <summary>A held weapon's numbers and attributes, read from its object.</summary>
        void ReadLive(Weapon w, GameObject o)
        {
            Component stats = Comp(o, "WeaponStats");
            if (stats == null) return;
            string type = Get(stats, "weaponType")?.ToString() ?? "Slash";
            w.Type = type == "Smash" ? 1 : type == "Stab" ? 2 : 0;
            w.Name = Get(stats, "wpnName") as string ?? o.name;
            w.Path = type + "/" + o.name.Replace("(Clone)", "").Trim();
            object nums = Get(stats, "statNums");
            w.DamageMult = F(Get(Get(nums, "advDamage"), "MultValue"), 1f);
            w.Knockback = F(Get(Get(nums, "advKnockback"), "Value"), 0f);
            w.SpeedMod = F(Get(stats, "wpnAttackSpeedModifier"), 1f);
            w.PrimalAmount = w.SpeedAmount = 0;
            var text = new List<string>();
            foreach (Component c in o.GetComponents<Component>())
            {
                if (c == null || !IsA(c.GetType(), "AttributeBase")) continue;
                w.PrimalAmount += I(Get(c, "primalAmount"));
                w.SpeedAmount += I(Get(c, "speedAmount"));
                string name = Get(c, "attName") as string;
                string desc = Get(c, "attDesc") as string;
                if (!string.IsNullOrEmpty(name)) text.Add(string.IsNullOrEmpty(desc) ? name : $"{name}: {desc}");
            }
            w.AttText = string.Join("; ", text);
        }

        /// <summary>
        /// Save a weapon into the bag the way LevelEnd.GoToNextLevel saves the held one: its attributes
        /// (type, value, rarity), unequipped unless they already are. Its object is removed after.
        /// </summary>
        void Stash(Weapon w, GameObject o, bool unequip)
        {
            ReadLive(w, o);
            Component stats = Comp(o, "WeaponStats");
            w.Atts.Clear();
            foreach (Component c in o.GetComponents<Component>())
            {
                if (c == null || !IsA(c.GetType(), "AttributeBase")) continue;
                w.Atts.Add((c.GetType().Name, F(Get(c, "specialAttNum"), 0f), Get(c, "rating")));
                if (unequip) Call(c, "Unequipped");
            }
            w.Live = null;
        }

        void AskSelect(uint id)
        {
            selectSeq++;
            selectId = id;
            pendingSelect = id;
            pendingSince = Time.realtimeSinceStartup;
        }

        /// <summary>Hold the weapon whose carrot Minecraft has selected.</summary>
        void Follow(MonoBehaviour host, uint hand)
        {
            if (pendingSelect != 0)
            {
                // After a pickup, until Minecraft has selected the new carrot (or gave up).
                if (hand != pendingSelect && Time.realtimeSinceStartup - pendingSince < FollowWaitSeconds) return;
                if (hand != pendingSelect) McLog.Info($"weapons: Minecraft didn't select #{pendingSelect} within {FollowWaitSeconds:F0} s; following its hand (#{hand})");
                pendingSelect = 0;
            }
            if (busy) return;
            Weapon want = hand != 0 ? Find(hand) : null;
            if (hand != 0 && (want == null || hand == heldId)) return;
            // Only put away a weapon of yours that the spore holds. With none (The Carcass, after a
            // run), doing it anyway gave the spore the stand-in, and Mycelium's next level then gave
            // no Stick: it only does when the spore holds nothing (your report, 2026-10-21).
            if (hand == 0 && (emptyHand || Find(heldId) == null)) return;
            Component attack = FindComp("PlayerParent", "PlayerController");
            if (attack != null && Get(attack, "canUseAttack") is bool can && !can) return; // mid-swing: after it
            host.StartCoroutine(want != null ? Equip(want) : EmptyHand());
        }

        /// <summary>Put the held weapon into the bag and build 'want' in the hand, as LevelGenerator does.</summary>
        IEnumerator Equip(Weapon want)
        {
            busy = true;
            Component swap = FindSwap();
            GameObject slot = GameObject.FindWithTag("WeaponSlot");
            Weapon held = Find(heldId);
            GameObject cur = swap != null ? Get(swap, "O_curWeapon") as GameObject : null;
            if (swap == null || slot == null)
            {
                McLog.Info($"weapons: can't switch to '{want.Name}': no SwapWeapon ({swap != null}) or weapon slot ({slot != null})");
                busy = false;
                yield break;
            }
            if (cur != null && !Thrown(cur))
            {
                if (held != null) Stash(held, cur, unequip: true);
                cur.tag = "Untagged";
                UnityEngine.Object.Destroy(cur); // the held weapon (in the bag now), or the empty hand's stand-in
            }
            emptyHand = false;
            UnityEngine.Object prefab = Resources.Load(want.Path);
            if (prefab == null)
            {
                McLog.Info($"weapons: no prefab at Resources/{want.Path}; can't switch to '{want.Name}'");
                busy = false;
                yield break;
            }
            var o = UnityEngine.Object.Instantiate(prefab, slot.transform) as GameObject;
            Component stats = Comp(o, "WeaponStats");
            Set(stats, "acceptingAttribute", false);
            if (want.Name != "Stick") o.transform.localScale = o.transform.localScale / 0.03f / 100f / 1.2563f; // as LevelGenerator does
            o.layer = LayerMask.NameToLayer("currentWeapon");
            Collider col = o.GetComponent<Collider>();
            if (col != null) col.enabled = false;
            o.tag = "currentWeapon";
            heldId = want.Id;
            expected = o;
            Set(swap, "O_curWeapon", o);
            yield return null;
            if (o == null) { busy = false; yield break; }
            var added = new List<Component>();
            var builtIn = new List<Component>();
            foreach (Component c in o.GetComponents<Component>())
                if (c != null && IsA(c.GetType(), "AttributeBase")) builtIn.Add(c);
            object assigner = GetStatic("AttributeAssigner", "Instance");
            int reused = 0;
            foreach (var (type, value, rating) in want.Atts)
            {
                Component att = builtIn.Find(c => c.GetType().Name == type);
                if (att != null) { builtIn.Remove(att); reused++; } // the prefab has it already
                else att = assigner != null ? Call(assigner, "PickAttFromString", o, type) as Component : null;
                if (att == null) continue;
                Set(att, "specialAttNum", value);
                if (rating != null) Set(att, "rating", rating);
                added.Add(att);
            }
            yield return new WaitForSeconds(0.1f);
            if (o != null)
            {
                Component interact = Comp(o, "WeaponInteraction");
                if (interact != null) Call(interact, "ApplyWeaponPositionAndRotation");
                foreach (Component att in added) if (att != null) Call(att, "Equipped");
                want.Live = o;
                McLog.Info($"weapons: the spore holds '{want.Name}' (#{want.Id}) now (Minecraft's selection), {added.Count} attributes back ({reused} the prefab had)");
            }
            version++;
            busy = false;
        }

        /// <summary>
        /// Minecraft's hand is empty: the held weapon into the bag, and an invisible Stick with only
        /// the "Nothing" attribute in the spore's hand (built as Equip builds a weapon).
        /// </summary>
        IEnumerator EmptyHand()
        {
            busy = true;
            Component swap = FindSwap();
            GameObject slot = GameObject.FindWithTag("WeaponSlot");
            UnityEngine.Object prefab = Resources.Load(EmptyHandPath);
            if (swap == null || slot == null || prefab == null)
            {
                McLog.Info($"weapons: can't empty the hand: SwapWeapon {swap != null}, weapon slot {slot != null}, {EmptyHandPath} {prefab != null}");
                busy = false;
                yield break;
            }
            Weapon held = Find(heldId);
            GameObject cur = Get(swap, "O_curWeapon") as GameObject;
            if (cur != null && !Thrown(cur))
            {
                if (held != null) Stash(held, cur, unequip: true);
                cur.tag = "Untagged";
                UnityEngine.Object.Destroy(cur);
            }
            var o = UnityEngine.Object.Instantiate(prefab, slot.transform) as GameObject;
            HideStandIn(o);
            Component stats = Comp(o, "WeaponStats");
            Set(stats, "acceptingAttribute", false);
            o.layer = LayerMask.NameToLayer("currentWeapon");
            Collider col = o.GetComponent<Collider>();
            if (col != null) col.enabled = false;
            o.tag = "currentWeapon";
            heldId = 0;
            emptyHand = true;
            expected = o;
            Set(swap, "O_curWeapon", o);
            yield return null;
            if (o == null) { busy = false; yield break; }
            // One attribute, "Nothing": Mycelium's next level rebuilds the held weapon's attributes and
            // expects at least one.
            object assigner = GetStatic("AttributeAssigner", "Instance");
            Component nothing = assigner != null ? Call(assigner, "PickAttFromString", o, "Nothing") as Component : null;
            if (nothing == null)
            {
                Type nt = Type.GetType("Nothing, Assembly-CSharp");
                if (nt != null) nothing = o.AddComponent(nt);
            }
            yield return new WaitForSeconds(0.1f);
            if (o != null)
            {
                Component interact = Comp(o, "WeaponInteraction");
                if (interact != null) Call(interact, "ApplyWeaponPositionAndRotation");
                HideStandIn(o);
                McLog.Info($"weapons: Minecraft's hand is empty: '{held?.Name ?? "?"}' back in the bag, the spore holds an invisible stand-in ({(nothing != null ? "Nothing attribute" : "NO attribute")})");
            }
            version++;
            busy = false;
        }

        bool Thrown(GameObject o)
        {
            foreach (GameObject f in flying.Values) if (ReferenceEquals(f, o)) return true;
            return false;
        }

        /// <summary>Every frame: your weapons flying in Minecraft's world; the real ones follow them.</summary>
        void FollowThrown(MonoBehaviour host, MyceliumLink link, WorldMapping map)
        {
            int n = link.TryReadThrown(thrIds, thrFlags, thrXyz);
            if (n < 0) return;
            seenThrown.Clear();
            for (int i = 0; i < n; i++)
            {
                uint id = thrIds[i];
                seenThrown.Add(id);
                if (!flying.TryGetValue(id, out GameObject o))
                {
                    Weapon w = Find(id);
                    if (w == null || busy) continue; // already out in the world, or mid-switch (next frame)
                    o = ThrowOut(host, w);
                    if (o == null) continue;
                    flying[id] = o;
                }
                if (o == null) continue;
                map.ToMyc(thrXyz[i * 3], thrXyz[i * 3 + 1] + ThrownLift, thrXyz[i * 3 + 2], out double x, out double y, out double z);
                Vector3 from = o.transform.position;
                o.transform.position = new Vector3((float)x, (float)y, (float)z);
                if (MovePopUp(o, o.transform.position - from, out Vector3 at)) { popUpMoves++; popUpAt = at; }
            }
            foreach (uint id in new List<uint>(flying.Keys))
            {
                if (seenThrown.Contains(id)) continue;
                GameObject o = flying[id];
                flying.Remove(id);
                if (o == null) continue;
                McLog.Info($"weapons: thrown #{id} '{o.name}' lies at {o.transform.position:F1}, ready to pick up" +
                           (popUpMoves > 0 ? $"; its pop-up moved with it for {popUpMoves} frames, now at {popUpAt:F1}" : "; it had no pop-up in flight"));
                popUpMoves = 0;
            }
        }

        /// <summary>
        /// Mycelium's pop-up for the thing you can interact with (TooltipManager.currentTooltip) is
        /// placed once, where the thing is then, and never moves: Mycelium's lying weapons don't. A
        /// thrown weapon moves, so its pop-up goes with it (your choice, 2026-10-21; keeping it from
        /// being interactable until it landed took too long).
        /// </summary>
        static bool MovePopUp(GameObject weapon, Vector3 by, out Vector3 at)
        {
            at = Vector3.zero;
            if (by.sqrMagnitude < 1e-8f) return false;
            Type t = Type.GetType("TooltipManager, Assembly-CSharp");
            object manager = t?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (manager == null) return false;
            var parent = t.GetField("currentParent", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(manager) as GameObject;
            var popUp = t.GetField("currentTooltip", BindingFlags.Public | BindingFlags.Instance)?.GetValue(manager) as GameObject;
            if (popUp == null || !ReferenceEquals(parent, weapon) || popUp.transform.parent != null) return false;
            popUp.transform.position += by;
            at = popUp.transform.position;
            return true;
        }

        int popUpMoves;
        Vector3 popUpAt;

        /// <summary>A weapon of yours leaves the bag for the world, as Mycelium drops one: tag and layer
        /// "Weapon", collider on (WeaponInteraction.Interact does the same to the weapon you let go).</summary>
        GameObject ThrowOut(MonoBehaviour host, Weapon w)
        {
            GameObject o;
            if (w.Id == heldId && w.Live != null)
            {
                o = w.Live;
                foreach (Component c in o.GetComponents<Component>())
                    if (c != null && IsA(c.GetType(), "AttributeBase")) Call(c, "Unequipped");
                o.transform.parent = null;
                heldId = 0;
                w.Live = null;
                MakeWorldWeapon(o);
                // Flying before the hand is emptied: EmptyHand runs at once, and it removes whatever the
                // spore holds unless it's flying (it removed the thrown mace in the first test).
                flying[w.Id] = o;
                host.StartCoroutine(EmptyHand()); // the spore needs something in its hand
            }
            else
            {
                GameObject slot = GameObject.FindWithTag("WeaponSlot");
                UnityEngine.Object prefab = Resources.Load(w.Path);
                if (slot == null || prefab == null)
                {
                    McLog.Info($"weapons: can't throw '{w.Name}': weapon slot {slot != null}, Resources/{w.Path} {prefab != null}");
                    return null;
                }
                // Made in the hand slot for the hand's scale (as Mycelium's dropped weapons keep), then let go.
                o = UnityEngine.Object.Instantiate(prefab, slot.transform) as GameObject;
                Set(Comp(o, "WeaponStats"), "acceptingAttribute", false);
                if (w.Name != "Stick") o.transform.localScale = o.transform.localScale / 0.03f / 100f / 1.2563f;
                o.transform.parent = null;
                MakeWorldWeapon(o);
                host.StartCoroutine(AddAttributes(o, w));
            }
            bag.Remove(w);
            version++;
            McLog.Info($"weapons: '{w.Name}' (#{w.Id}) thrown in Minecraft: out of the bag, the real weapon follows the item");
            return o;
        }

        static void MakeWorldWeapon(GameObject o)
        {
            o.tag = "Weapon";
            o.layer = LayerMask.NameToLayer("Weapon");
            Collider col = o.GetComponent<Collider>();
            if (col != null) col.enabled = true;
        }

        /// <summary>A weapon built for the world gets its attributes back a frame later (not Equipped: not held).</summary>
        IEnumerator AddAttributes(GameObject o, Weapon w)
        {
            yield return null;
            if (o == null) yield break;
            var builtIn = new List<Component>();
            foreach (Component c in o.GetComponents<Component>())
                if (c != null && IsA(c.GetType(), "AttributeBase")) builtIn.Add(c);
            object assigner = GetStatic("AttributeAssigner", "Instance");
            foreach (var (type, value, rating) in w.Atts)
            {
                Component att = builtIn.Find(c => c.GetType().Name == type);
                if (att != null) builtIn.Remove(att);
                else att = assigner != null ? Call(assigner, "PickAttFromString", o, type) as Component : null;
                if (att == null) continue;
                Set(att, "specialAttNum", value);
                if (rating != null) Set(att, "rating", rating);
            }
        }

        /// <summary>The empty hand's stand-in is never drawn (Renderer.enabled, which the camera's hiding doesn't touch).</summary>
        static void HideStandIn(GameObject o)
        {
            if (o == null) return;
            foreach (Renderer r in o.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        }

        /// <summary>Each weapon's swing with the spore as it is now. True if any number changed.</summary>
        bool Compute(Weapon held)
        {
            Component character = FindTagged("currentPlayer", "CharacterStats");
            Component ranges = FindComp("PlayerParent", "SporeAttributeRanges");
            Component attack = FindComp("PlayerParent", "PlayerAttack");
            if (character == null || ranges == null) return false;
            int primalNow = I(Get(character, "primalLevel")), speedNow = I(Get(character, "speedLevel"));
            float dmgBase = F(Get(ranges, "attackDamageBase"), 10f), dmgInc = F(Get(ranges, "attackDamageIncrement"), 0f);
            float spdBase = F(Get(ranges, "attackSpeedBase"), 1f), spdInc = F(Get(ranges, "attackSpeedIncrement"), 0f);
            float fungal = attack != null ? F(Get(attack, "fungalMightBonus"), 1f) : 1f;
            int primalBase = primalNow - (held != null && held.Live != null ? held.PrimalAmount : 0);
            int speedBase = speedNow - (held != null && held.Live != null ? held.SpeedAmount : 0);
            bool changed = false;
            foreach (Weapon w in bag)
            {
                int primal = Mathf.Clamp(primalBase + w.PrimalAmount, 1, 99);
                int speed = Mathf.Clamp(speedBase + w.SpeedAmount, 1, 99);
                float damage = Mathf.RoundToInt(dmgBase + (primal - 1) * dmgInc) * fungal * w.DamageMult;
                float animatorSpeed = spdBase + (speed - 1) * spdInc;
                float perSecond = animatorSpeed * w.SpeedMod / (0.9f * ClipLength(w.Type));
                if (Mathf.Abs(damage - w.Damage) > 0.01f || Mathf.Abs(perSecond - w.PerSecond) > 0.005f) changed = true;
                w.Damage = damage;
                w.PerSecond = perSecond;
            }
            return changed;
        }

        /// <summary>
        /// How long a weapon type's attack state runs (s, at animator speed 1), from the spore's own
        /// animator. Mycelium's states play faster than their clips (Slash 1.71 s clip runs 1.14 s,
        /// Smash 4.17 runs 2.08, Stab 1.08 runs 0.87: measured with -mcSwingTest), so each state is
        /// put on for no time at all (Play, Update(0)), its length read, and the animator put back
        /// where it was, all within one frame. Not while the spore is attacking. Falls back to the
        /// clip lengths, then 1 s.
        /// </summary>
        float ClipLength(int type)
        {
            string name = type == 1 ? "Smash" : type == 2 ? "Stab" : "Slash";
            GameObject player = GameObject.FindWithTag("currentPlayer");
            Animator a = player != null ? player.GetComponent<Animator>() : null;
            if (a != clipsFrom && a != null && a.isActiveAndEnabled && a.runtimeAnimatorController != null)
            {
                Component controller = FindComp("PlayerParent", "PlayerController");
                bool attacking = controller != null && Get(controller, "canUseAttack") is bool can && !can;
                if (!attacking)
                {
                    clipsFrom = a;
                    clipLength.Clear();
                    AnimatorStateInfo before = a.GetCurrentAnimatorStateInfo(0);
                    float speed = a.speed;
                    a.speed = 1f;
                    var found = new List<string>();
                    foreach (string t in new[] { "Slash", "Smash", "Stab" })
                    {
                        a.Play(t, 0, 0f);
                        a.Update(0f);
                        AnimatorStateInfo st = a.GetCurrentAnimatorStateInfo(0);
                        if (st.IsName(t) && st.length > 0.05f) { clipLength[t] = st.length; found.Add($"{t} {st.length:F2}"); }
                    }
                    a.Play(before.fullPathHash, 0, before.normalizedTime);
                    a.Update(0f);
                    a.speed = speed;
                    if (clipLength.Count < 3)
                    {
                        // Fallback: the clips themselves (named "SlashAnim" etc.).
                        foreach (AnimationClip clip in a.runtimeAnimatorController.animationClips)
                            foreach (string t in new[] { "Slash", "Smash", "Stab" })
                                if (!clipLength.ContainsKey(t) && clip != null && clip.name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                                    clipLength[t] = clip.length;
                    }
                    McLog.Info($"weapons: attack state lengths: Slash {Len("Slash")}, Smash {Len("Smash")}, Stab {Len("Stab")} s ({(found.Count == 3 ? "measured on the animator" : "measured: " + string.Join(", ", found) + "; the rest from clips")})");
                }
            }
            return clipLength.TryGetValue(name, out float len) && len > 0.05f ? len : 1f;
        }

        /// <summary>
        /// How far the held weapon reaches in its attack, for Better Combat's attack range in
        /// Minecraft (blocks). Mycelium hits with the weapon's own collider as the spore's attack
        /// animation swings it, so the spore's attack state is stepped through (Play at a time,
        /// Update(0), events off; put back within the frame, as ClipLength does) and the collider's
        /// farthest corner from the spore's middle, flat on the ground, is the reach. Measured on the
        /// real held object: Mycelium's hand, hold offsets and scale are all in it. Not while the
        /// spore is attacking (tried again next frame). Also logs the swing's sideways spread and
        /// when it reaches farthest, for the Better Combat presets.
        /// </summary>
        bool MeasureReach(Weapon w, WorldMapping map)
        {
            Component controller = FindComp("PlayerParent", "PlayerController");
            if (controller != null && Get(controller, "canUseAttack") is bool can && !can) return false;
            switch (MeasureOn(w.Live, w.Type, w.Name, map, out float reach))
            {
                case null: return false; // not now
                case false: w.ReachFrom = w.Live; return false;
            }
            w.ReachFrom = w.Live;
            w.Reach = reach;
            return true;
        }

        /// <summary>
        /// The measuring itself, on any weapon object in the spore's hand (also -mcReachTest).
        /// Null: can't now (no animator); false: can't on this weapon.
        /// </summary>
        public static bool? MeasureOn(GameObject live, int type, string name, WorldMapping map, out float reachBlocks)
        {
            reachBlocks = 0f;
            GameObject player = GameObject.FindWithTag("currentPlayer");
            Animator a = player != null ? player.GetComponent<Animator>() : null;
            if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null || map == null || live == null) return null;
            Collider col = live.GetComponent<Collider>();
            if (col == null) { McLog.Info($"weapons: '{name}' has no collider; reach not measured"); return false; }
            Vector3[] corners = LocalCorners(col);
            string state = type == 1 ? "Smash" : type == 2 ? "Stab" : "Slash";
            Transform root = player.transform;
            AnimatorStateInfo before = a.GetCurrentAnimatorStateInfo(0);
            float speed = a.speed;
            bool events = a.fireEvents;
            a.speed = 1f;
            a.fireEvents = false;
            const int Steps = 40;
            var reach = new float[Steps + 1];
            var yaw = new float[Steps + 1];
            bool found = true;
            try
            {
                for (int i = 0; i <= Steps; i++)
                {
                    a.Play(state, 0, i / (float)Steps);
                    a.Update(0f);
                    if (i == 0 && !a.GetCurrentAnimatorStateInfo(0).IsName(state)) { found = false; break; }
                    foreach (Vector3 c in corners)
                    {
                        Vector3 rel = col.transform.TransformPoint(c) - root.position;
                        rel.y = 0f;
                        if (rel.magnitude > reach[i])
                        {
                            reach[i] = rel.magnitude;
                            yaw[i] = Vector3.SignedAngle(Vector3.ProjectOnPlane(root.forward, Vector3.up), rel, Vector3.up);
                        }
                    }
                }
            }
            finally
            {
                a.Play(before.fullPathHash, 0, before.normalizedTime);
                a.Update(0f);
                a.speed = speed;
                a.fireEvents = events;
            }
            if (!found) { McLog.Info($"weapons: no '{state}' state on the spore; '{name}' reach not measured"); return false; }
            int far = 0;
            for (int i = 1; i <= Steps; i++) if (reach[i] > reach[far]) far = i;
            float minYaw = 180f, maxYaw = -180f;
            int front = far;
            for (int i = 0; i <= Steps; i++)
            {
                if (reach[i] < 0.75f * reach[far]) continue;
                minYaw = Mathf.Min(minYaw, yaw[i]);
                maxYaw = Mathf.Max(maxYaw, yaw[i]);
                if (Mathf.Abs(yaw[i]) < Mathf.Abs(yaw[front])) front = i;
            }
            reachBlocks = (float)(reach[far] / map.Scale);
            McLog.Info($"weapons: '{name}' ({state}) reaches {reach[far]:F2} units = {reachBlocks:F2} blocks from the spore's middle, farthest at {far / (float)Steps:F2} of the attack" +
                       $" (facing {yaw[far]:F0} deg); within 75% of that it spans {minYaw:F0}..{maxYaw:F0} deg, straight ahead at {front / (float)Steps:F2}; collider {col.GetType().Name}");
            return true;
        }

        /// <summary>The 8 corners of a collider's shape, in its object's own space.</summary>
        static Vector3[] LocalCorners(Collider col)
        {
            Vector3 center, half;
            switch (col)
            {
                case BoxCollider b: center = b.center; half = b.size * 0.5f; break;
                case SphereCollider s: center = s.center; half = Vector3.one * s.radius; break;
                case CapsuleCollider c:
                    center = c.center;
                    half = Vector3.one * c.radius;
                    half[c.direction] = Mathf.Max(c.radius, c.height * 0.5f);
                    break;
                case MeshCollider m when m.sharedMesh != null: center = m.sharedMesh.bounds.center; half = m.sharedMesh.bounds.extents; break;
                default: center = Vector3.zero; half = Vector3.zero; break;
            }
            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++)
                corners[i] = center + new Vector3((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z);
            return corners;
        }

        string Len(string t) => clipLength.TryGetValue(t, out float l) ? l.ToString("F2") : "?";

        MyceliumLink.WeaponEntry[] Table()
        {
            var t = new MyceliumLink.WeaponEntry[bag.Count];
            for (int i = 0; i < bag.Count; i++)
            {
                Weapon w = bag[i];
                t[i] = new MyceliumLink.WeaponEntry
                {
                    Id = w.Id, Type = (uint)w.Type, Damage = w.Damage, PerSecond = w.PerSecond, Knockback = w.Knockback,
                    DamageMult = w.DamageMult, SpeedMod = w.SpeedMod, Reach = w.Reach, Name = w.Name ?? "?", Attributes = w.AttText ?? "",
                    Key = w.Path != null ? w.Path.Substring(w.Path.LastIndexOf('/') + 1) : "" // its prefab: two weapons share "Rose Mace"
                };
            }
            return t;
        }

        Weapon Find(uint id)
        {
            if (id == 0) return null;
            foreach (Weapon w in bag) if (w.Id == id) return w;
            return null;
        }

        // --- reflection helpers ---
        static Component FindSwap() => FindComp("PlayerParent", "SwapWeapon");

        static Component FindComp(string objectName, string type)
        {
            GameObject o = GameObject.Find(objectName);
            return o != null ? Comp(o, type) : null;
        }

        static Component FindTagged(string tag, string type)
        {
            GameObject o = GameObject.FindWithTag(tag);
            return o != null ? Comp(o, type) : null;
        }

        static Component Comp(GameObject o, string type)
        {
            Type t = Type.GetType(type + ", Assembly-CSharp");
            return t != null && o != null ? o.GetComponent(t) : null;
        }

        static bool IsA(Type t, string baseName)
        {
            for (; t != null; t = t.BaseType) if (t.Name == baseName) return true;
            return false;
        }

        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        static object Get(object o, string name)
        {
            if (o == null) return null;
            for (Type t = o.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, All | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(o);
                PropertyInfo p = t.GetProperty(name, All | BindingFlags.DeclaredOnly);
                if (p != null) return p.GetValue(o);
            }
            return null;
        }

        static void Set(object o, string name, object value)
        {
            if (o == null) return;
            for (Type t = o.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, All | BindingFlags.DeclaredOnly);
                if (f != null) { f.SetValue(o, value); return; }
                PropertyInfo p = t.GetProperty(name, All | BindingFlags.DeclaredOnly);
                if (p != null && p.CanWrite) { p.SetValue(o, value); return; }
            }
        }

        static object GetStatic(string type, string name)
        {
            Type t = Type.GetType(type + ", Assembly-CSharp");
            if (t == null) return null;
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (f != null) return f.GetValue(null);
            return t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        }

        static object Call(object o, string method, params object[] args)
        {
            if (o == null) return null;
            foreach (MethodInfo m in o.GetType().GetMethods(All))
            {
                if (m.Name != method) continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length < args.Length) continue;
                var full = new object[ps.Length];
                bool ok = true;
                for (int i = 0; i < ps.Length; i++)
                {
                    if (i < args.Length) full[i] = args[i];
                    else if (ps[i].HasDefaultValue) full[i] = ps[i].DefaultValue;
                    else { ok = false; break; }
                }
                if (!ok) continue;
                try { return m.Invoke(o, full); }
                catch (Exception e)
                {
                    McLog.Info($"weapons: {o.GetType().Name}.{method} failed: {(e.InnerException ?? e).Message}");
                    return null;
                }
            }
            return null;
        }

        static float F(object v, float fallback) => v is float f ? f : v is double d ? (float)d : v is int i ? i : fallback;
        static int I(object v) => v is int i ? i : v is float f ? (int)f : 0;
    }
}
