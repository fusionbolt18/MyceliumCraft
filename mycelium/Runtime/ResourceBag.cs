using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using MyceliumCraft.Link;

namespace MyceliumCraft
{
    /// <summary>
    /// Mycelium's resources as Minecraft items, several at once (your requests, 2026-11-02): every
    /// resource you pick up goes into this bag and becomes its own item in Minecraft's inventory
    /// (MycState's resource list, MycResources.java), one slot each.
    ///
    /// Mycelium itself carries one at a time (NutrientTracker.heldLog / heldExoskeleton / heldCalcite
    /// / heldFlesh and heldItem, the picked-up object itself): a pickup puts the one you had back on
    /// the ground where the new one lay (GetMaterial.AddMaterial). While Minecraft drives, that one is
    /// hidden again at once and stays in the bag. Mycelium's own "held" is kept as the newest in the
    /// bag, so its notifications ("... stored at the Carcass", "... dropped!") still name one.
    ///  - Level end (LevelEnd.SceneChangeSetup: KeepMaterials stores the held one, LoseMaterials):
    ///    the rest of the bag is stored at the Carcass too, and Mycelium's save is written again
    ///    (ProfileManager.SaveOverride), since the next scene reads the save.
    ///  - Dying, quitting, any other scene change: the bag is lost, as Mycelium's resource is.
    ///  - Throwing one in Minecraft: its item flies through Minecraft's world and Minecraft sends where
    ///    it is (Thrown block, flag ThrResource). The real resource object leaves the bag, follows it,
    ///    and stays on the ground as a pickup once Minecraft removes the item after it lands.
    /// Mycelium's resource panel never shows while Minecraft is linked (KeepPanelOut).
    /// Everything Mycelium is reached by name (reflection), like the rest of the mod.
    /// </summary>
    sealed class ResourceBag
    {
        sealed class Res
        {
            public uint Id;
            public int Type;      // 1 Rotten Log, 2 Fresh Exoskeleton, 3 Calcite Deposit, 4 Flesh
            public GameObject Obj; // the real resource (GetMaterial), hidden while in the bag
            public float Lift;     // how high it lay above the ground (Mycelium units)
        }

        static readonly string[] Held = { "heldLog", "heldExoskeleton", "heldCalcite", "heldFlesh" };
        static readonly string[] Stored = { "storedLog", "storedExoskeleton", "storedCalcite", "storedFlesh" };
        static readonly string[] Names = { "none", "Rotten Log", "Fresh Exoskeleton", "Calcite Deposit", "Flesh" };
        const BindingFlags Inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        readonly List<Res> bag = new List<Res>();
        readonly Dictionary<uint, Res> flying = new Dictionary<uint, Res>();
        readonly HashSet<uint> seen = new HashSet<uint>();
        readonly uint[] thrIds = new uint[Proto.ThrMax], thrFlags = new uint[Proto.ThrMax];
        readonly double[] thrXyz = new double[Proto.ThrMax * 3];
        readonly uint[] entries = new uint[Proto.ResMax];
        uint nextId = 1;
        Component tracker;
        int lastHeld, lastStored;
        GameObject lastHeldObj;

        /// <summary>Test aid: don't write Mycelium's save at a level end.</summary>
        public bool NoSave;

        /// <summary>For MycState: each (id &lt;&lt; 4) | type; Update returns how many.</summary>
        public uint[] Entries => entries;

        public int Count => bag.Count;

        public void SceneLoaded(Scene scene)
        {
            tracker = null;
            if (bag.Count == 0 && flying.Count == 0) return;
            McLog.Info($"resources: scene '{scene.name}' loaded with {bag.Count} resource(s) still carried: lost (a death or quitting the level)");
            bag.Clear();
            flying.Clear();
        }

        /// <summary>Every frame: watch Mycelium's pickups and level end, follow thrown ones; returns how many are carried.</summary>
        public int Update(Component t, bool driving, MyceliumLink link, WorldMapping map)
        {
            if (t != null)
            {
                if (!ReferenceEquals(t, tracker))
                {
                    tracker = t;
                    Read(t, out lastHeld, out lastHeldObj, out lastStored);
                }
                Watch(t, driving);
                if (driving) FollowThrown(t, link, map);
                Read(t, out lastHeld, out lastHeldObj, out lastStored);
            }
            int n = 0;
            foreach (Res r in bag)
                if (n < entries.Length) entries[n++] = (r.Id << 4) | (uint)r.Type;
            return n;
        }

        static void Read(Component t, out int held, out GameObject obj, out int stored)
        {
            Type ty = t.GetType();
            held = 0;
            for (int i = 0; i < Held.Length; i++)
                if (ty.GetField(Held[i], Inst)?.GetValue(t) is int n && n != 0) { held = i + 1; break; }
            obj = ty.GetField("heldItem", Inst)?.GetValue(t) as GameObject;
            if (obj == null) obj = null; // destroyed objects compare equal to null
            stored = 0;
            for (int i = 0; i < Stored.Length; i++)
                if (ty.GetField(Stored[i], Inst)?.GetValue(t) is int n) stored += n;
        }

        bool Has(GameObject o)
        {
            foreach (Res r in bag) if (ReferenceEquals(r.Obj, o)) return true;
            return false;
        }

        void Watch(Component t, bool driving)
        {
            Read(t, out int held, out GameObject obj, out int stored);
            if (held != 0 && obj != null && !Has(obj))
            {
                PickedUp(t, held, obj, driving);
            }
            else if (held == 0 && lastHeld != 0)
            {
                if (stored > lastStored) StoreRest(t, stored - lastStored);
                else if (bag.Count > 0)
                {
                    McLog.Info($"resources: Mycelium lost its resource (a death or quitting): the bag's {bag.Count} are gone");
                    bag.Clear();
                }
            }
        }

        /// <summary>Mycelium picked one up (GetMaterial.AddMaterial): into the bag; the one it put back down stays in the bag.</summary>
        void PickedUp(Component t, int type, GameObject obj, bool driving)
        {
            GameObject before = lastHeldObj;
            string old = "";
            if (before != null && !ReferenceEquals(before, obj) && Has(before) && before.activeSelf)
            {
                if (driving && bag.Count < Proto.ResMax)
                {
                    before.SetActive(false);
                    old = $"; '{before.name}', which Mycelium put back down, stays in the bag";
                }
                else
                {
                    bag.RemoveAll(r => ReferenceEquals(r.Obj, before));
                    old = driving ? $"; the bag is full ({Proto.ResMax}): '{before.name}' stays on the ground" : "; Minecraft isn't driving: Mycelium's own swap";
                }
            }
            foreach (uint id in new List<uint>(flying.Keys))
                if (ReferenceEquals(flying[id].Obj, obj)) flying.Remove(id); // caught in flight
            var res = new Res { Id = nextId++, Type = type, Obj = obj, Lift = LiftOf(obj) };
            if (nextId >= (1u << 28)) nextId = 1;
            bag.Add(res);
            McLog.Info($"resources: picked up {Names[type]} '{obj.name}' (#{res.Id}, {res.Lift:F2} above the ground){old}; carrying {Describe()}");
        }

        /// <summary>How high a resource lay above the ground, so a thrown one lands the same way.</summary>
        static float LiftOf(GameObject o)
        {
            Vector3 from = o.transform.position + Vector3.up * 0.05f;
            float best = float.PositiveInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.attachedRigidbody != null || hit.collider.transform.IsChildOf(o.transform)) continue; // the spore, enemies, itself
                best = Mathf.Min(best, hit.distance);
            }
            return float.IsInfinity(best) ? 0.5f : Mathf.Clamp(best - 0.05f, 0f, 5f);
        }

        /// <summary>Level end: Mycelium stored the one it held (KeepMaterials); store the rest and save.</summary>
        void StoreRest(Component t, int mycStored)
        {
            Type ty = t.GetType();
            bool skipped = false;
            int added = 0;
            var parts = new List<string>();
            foreach (Res r in bag)
            {
                if (!skipped && r.Type == lastHeld) { skipped = true; continue; } // Mycelium stored this one
                FieldInfo f = ty.GetField(Stored[r.Type - 1], Inst);
                if (f == null) continue;
                f.SetValue(t, (int)f.GetValue(t) + 1);
                added++;
                parts.Add(Names[r.Type]);
            }
            string lostFlying = flying.Count > 0 ? $"; {flying.Count} thrown one(s) stay behind in the level" : "";
            bag.Clear();
            flying.Clear();
            string saved = "";
            if (added > 0)
            {
                if (NoSave) saved = "; not saved (test)";
                else saved = Save() ? "; Mycelium's save written" : "; WRITING MYCELIUM'S SAVE FAILED";
            }
            McLog.Info($"resources: level end: Mycelium stored {mycStored} ({Names[lastHeld]}), MyceliumCraft {added} more ({(parts.Count > 0 ? string.Join(", ", parts) : "none")}){saved}{lostFlying}");
        }

        static bool Save()
        {
            Type pt = Type.GetType("ProfileManager, Assembly-CSharp");
            object pm = pt?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            MethodInfo m = pt?.GetMethod("SaveOverride", Inst);
            if (pm == null || m == null) return false;
            try { m.Invoke(pm, null); return true; }
            catch (Exception e) { McLog.Info($"resources: SaveOverride failed: {(e.InnerException ?? e).Message}"); return false; }
        }

        /// <summary>Mycelium's own "held" = the newest in the bag (or none).</summary>
        void SetHeld(Component t)
        {
            Type ty = t.GetType();
            Res top = bag.Count > 0 ? bag[bag.Count - 1] : null;
            for (int i = 0; i < Held.Length; i++) ty.GetField(Held[i], Inst)?.SetValue(t, top != null && top.Type == i + 1 ? 1 : 0);
            ty.GetField("heldItem", Inst)?.SetValue(t, top?.Obj);
        }

        /// <summary>Every frame: your resources flying in Minecraft's world; the real ones follow them.</summary>
        void FollowThrown(Component t, MyceliumLink link, WorldMapping map)
        {
            int n = link.TryReadThrown(thrIds, thrFlags, thrXyz);
            if (n < 0) return;
            seen.Clear();
            for (int i = 0; i < n; i++)
            {
                if ((thrFlags[i] & Proto.ThrResource) == 0) continue; // a weapon: WeaponBag's
                uint id = thrIds[i];
                seen.Add(id);
                if (!flying.TryGetValue(id, out Res r))
                {
                    r = bag.Find(x => x.Id == id);
                    if (r == null) continue; // already out in the world
                    bag.Remove(r);
                    flying[id] = r;
                    if (r.Obj != null) r.Obj.SetActive(true);
                    SetHeld(t);
                    McLog.Info($"resources: {Names[r.Type]} (#{id}) thrown in Minecraft: out of the bag, the real one follows the item; carrying {Describe()}");
                }
                if (r.Obj == null || !r.Obj.activeSelf) continue;
                map.ToMyc(thrXyz[i * 3], thrXyz[i * 3 + 1], thrXyz[i * 3 + 2], out double x, out double y, out double z);
                Vector3 from = r.Obj.transform.position;
                r.Obj.transform.position = new Vector3((float)x, (float)y + r.Lift, (float)z);
                // Its pop-up goes with it, as a thrown weapon's (WeaponBag.MovePopUp).
                if (WeaponBag.MovePopUp(r.Obj, r.Obj.transform.position - from, out Vector3 at)) { popUpMoves++; popUpAt = at; }
            }
            foreach (uint id in new List<uint>(flying.Keys))
            {
                if (seen.Contains(id)) continue;
                Res r = flying[id];
                flying.Remove(id);
                if (r.Obj == null) continue;
                McLog.Info($"resources: thrown {Names[r.Type]} (#{id}) lies at {r.Obj.transform.position:F1}, ready to pick up" +
                           (popUpMoves > 0 ? $"; its pop-up moved with it for {popUpMoves} frames, now at {popUpAt:F1}" : "; it had no pop-up in flight"));
                popUpMoves = 0;
            }
        }

        int popUpMoves;
        Vector3 popUpAt;

        string Describe()
        {
            if (bag.Count == 0) return "nothing";
            var parts = new List<string>();
            foreach (Res r in bag) parts.Add($"{Names[r.Type]} #{r.Id}");
            return string.Join(", ", parts);
        }

        // Mycelium's resource panel (HUDItem.itemPanel): kept at its out-of-sight spot.
        Component[] huds = new Component[0];
        float nextHudLook;
        bool wasPushing;
        int pushes;

        /// <summary>
        /// Every frame, after Mycelium's slide coroutines (LateUpdate): while Minecraft is linked, every
        /// HUDItem's resource panel is put at its out-of-sight spot (materialOutsideTarget), whoever
        /// slid it in. Before (sliding it away once after a pickup), it still showed in later levels.
        /// </summary>
        public void KeepPanelOut(bool linked)
        {
            if (!linked) return;
            if (Time.unscaledTime >= nextHudLook)
            {
                nextHudLook = Time.unscaledTime + 1f;
                Type ht = Type.GetType("HUDItem, Assembly-CSharp");
                var found = ht != null ? UnityEngine.Object.FindObjectsOfType(ht) : new UnityEngine.Object[0];
                huds = new Component[found.Length];
                for (int i = 0; i < found.Length; i++) huds[i] = found[i] as Component;
            }
            bool pushing = false;
            foreach (Component h in huds)
            {
                if (h == null) continue;
                Type ht = h.GetType();
                var panel = ht.GetField("itemPanel", Inst)?.GetValue(h) as RectTransform;
                var outside = ht.GetField("materialOutsideTarget", Inst)?.GetValue(h) as RectTransform;
                if (panel == null || outside == null) continue;
                if ((panel.localPosition - outside.localPosition).sqrMagnitude < 1f) continue;
                if (!wasPushing && !pushing)
                    McLog.Info($"resources: Mycelium's resource panel on '{h.gameObject.name}' (scene '{h.gameObject.scene.name}', {huds.Length} HUD(s)) was at {panel.localPosition:F0}: kept out of sight at {outside.localPosition:F0}");
                panel.localPosition = outside.localPosition;
                pushing = true;
                pushes++;
            }
            wasPushing = pushing;
        }

        // The level end's notification for several resources (your request, 2026-11-02).
        const string StoredSuffix = " stored at the Carcass";
        readonly List<GameObject> noticeIcons = new List<GameObject>();
        UnityEngine.UI.Image noticeIcon;
        bool noticeDone;

        /// <summary>
        /// Every frame (LateUpdate): LevelEnd shows "AREA COMPLETE / &lt;held&gt; stored at the Carcass"
        /// with that resource's picture (NotificationManager.Notification). With more than one in the
        /// bag the line becomes "Resources stored at The Carcass", and the picture is replaced by one
        /// per type stored, at its size, in a row, each with how many at its bottom right. Put
        /// back when the notification is over.
        /// </summary>
        public void LevelEndNotice()
        {
            Type nt = Type.GetType("NotificationManager, Assembly-CSharp");
            var manager = nt?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as Component;
            if (manager == null) return;
            bool showing = nt.GetField("isDisplaying", Inst)?.GetValue(manager) is bool b && b;
            if (!showing)
            {
                if (noticeDone) RestoreNotice();
                return;
            }
            if (noticeDone || bag.Count < 2) return;
            var helper = nt.GetField("helperText", Inst)?.GetValue(manager) as Component;
            var icon = nt.GetField("notificationIcon", Inst)?.GetValue(manager) as UnityEngine.UI.Image;
            var sprites = nt.GetField("notificationSprites", Inst)?.GetValue(manager) as List<Sprite>;
            PropertyInfo textProp = helper?.GetType().GetProperty("text");
            if (helper == null || icon == null || sprites == null || textProp == null) return;
            string text = textProp.GetValue(helper) as string;
            if (text == null || !text.EndsWith(StoredSuffix)) return;
            noticeDone = true;

            textProp.SetValue(helper, "Resources stored at The Carcass");
            int[] counts = new int[Names.Length];
            foreach (Res r in bag) counts[r.Type]++;
            var types = new List<int>();
            for (int i = 1; i < Names.Length; i++) if (counts[i] > 0) types.Add(i);

            // In a row at Mycelium's own size (squeezing them into its square made them too small and
            // squished, your report), centred on where Mycelium's picture is (your request).
            RectTransform rt = icon.rectTransform;
            float side = Mathf.Min(rt.rect.width, rt.rect.height);
            float step = rt.rect.width * 1.1f;
            var parts = new List<string>();
            for (int k = 0; k < types.Count; k++)
            {
                int type = types[k];
                Sprite sprite = sprites.Find(s => s != null && s.name == Names[type]);
                GameObject o = UnityEngine.Object.Instantiate(icon.gameObject, icon.transform.parent);
                o.name = "MyceliumCraft stored " + Names[type];
                var img = o.GetComponent<UnityEngine.UI.Image>();
                img.enabled = true;
                if (sprite != null) img.sprite = sprite;
                var ort = (RectTransform)o.transform;
                ort.localPosition = rt.localPosition + new Vector3((k - (types.Count - 1) / 2f) * step, 0f, 0f);
                float cell = side;
                for (int c = ort.childCount - 1; c >= 0; c--) UnityEngine.Object.Destroy(ort.GetChild(c).gameObject);
                // The count: a copy of the notification's own text (its font), small, at the bottom right.
                GameObject num = UnityEngine.Object.Instantiate(helper.gameObject, o.transform);
                num.name = "count";
                var nrt = (RectTransform)num.transform;
                nrt.anchorMin = nrt.anchorMax = nrt.pivot = new Vector2(1f, 0f);
                nrt.anchoredPosition = new Vector2(cell * 0.12f, -cell * 0.08f);
                nrt.sizeDelta = new Vector2(cell, cell * 0.5f);
                Component tmp = num.GetComponent(helper.GetType());
                SetProp(tmp, "enableAutoSizing", false);
                SetProp(tmp, "fontSize", Mathf.Max(8f, cell * 0.35f));
                SetEnum(tmp, "alignment", "BottomRight");
                SetProp(tmp, "text", counts[type].ToString());
                noticeIcons.Add(o);
                parts.Add($"{Names[type]} x{counts[type]} (picture {(sprite != null ? sprite.name : "NOT FOUND")}) at {ort.localPosition:F0} size {cell:F0}");
            }
            icon.enabled = false;
            noticeIcon = icon;
            McLog.Info($"resources: level end notice: 'Resources stored at The Carcass' (was '{text}'); Mycelium's picture {rt.rect.width:F0}x{rt.rect.height:F0} at {rt.localPosition:F0}, the row centred on it: {string.Join(", ", parts)}");
        }

        void RestoreNotice()
        {
            foreach (GameObject o in noticeIcons) if (o != null) UnityEngine.Object.Destroy(o);
            noticeIcons.Clear();
            if (noticeIcon != null) noticeIcon.enabled = true;
            noticeIcon = null;
            noticeDone = false;
        }

        static void SetProp(Component c, string name, object value)
        {
            PropertyInfo p = c?.GetType().GetProperty(name);
            if (p == null || !p.CanWrite) return;
            try { p.SetValue(c, Convert.ChangeType(value, p.PropertyType)); } catch (Exception) { }
        }

        static void SetEnum(Component c, string name, string value)
        {
            PropertyInfo p = c?.GetType().GetProperty(name);
            if (p == null || !p.PropertyType.IsEnum) return;
            try { p.SetValue(c, Enum.Parse(p.PropertyType, value)); } catch (Exception) { }
        }

        /// <summary>For the test log: the panel's position on every HUD.</summary>
        public string PanelReport()
        {
            var parts = new List<string>();
            foreach (Component h in huds)
            {
                if (h == null) continue;
                var panel = h.GetType().GetField("itemPanel", Inst)?.GetValue(h) as RectTransform;
                var outside = h.GetType().GetField("materialOutsideTarget", Inst)?.GetValue(h) as RectTransform;
                if (panel != null && outside != null) parts.Add($"{panel.localPosition:F0} (out {outside.localPosition:F0})");
            }
            return $"{parts.Count} panel(s): {string.Join(", ", parts)}; pushed out {pushes} frame(s)";
        }
    }
}
