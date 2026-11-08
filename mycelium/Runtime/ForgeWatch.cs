using System;
using System.Reflection;
using MyceliumCraft.Link;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// Blacksmith Ironspore opens Minecraft's smithing screen (your request, 2026-11-08). The first
    /// slot is the weapon base, the next two the attribute donors (MycForge.java).
    ///
    /// Mycelium's Blacksmith (ForgeShroom, in the first level) takes the weapon you hold each time you
    /// interact: the first is the base, the next two give their first attribute, and the third makes
    /// the forged weapon (base prefab, only those two attributes, rated Forged) at its cache. It takes
    /// no Stick, nothing forged and nothing without an attribute, and forges only once.
    ///
    /// While Minecraft drives:
    /// - Interacting with it (PlayerController.Update: the Interact action, canAct, the closest thing
    ///   of SporeInteractableFinder) asks Minecraft to open the screen instead. So that Mycelium's own
    ///   Interact does nothing, its weaponGoalReached is set for that one frame (Interact then only
    ///   takes its pop-up away). That's done by a component that runs before every other script
    ///   (Early) and put back after them (Late).
    /// - Minecraft gets the ids of the weapons it takes, so its screen takes only those.
    /// - Taking the result in Minecraft sends a request; the bag forges it as ForgeShroom would
    ///   (WeaponBag.Forge) and the answer goes back with the new weapon's id. After that the
    ///   Blacksmith is done, as in Mycelium (weaponGoalReached stays set).
    /// </summary>
    sealed class ForgeWatch
    {
        Type forgeType, controllerType, finderType;
        FieldInfo goalField, interactField, canActField, closestField;
        bool looked;
        uint openSeq, handled, result, lastRequest;
        uint lastPreview, previewHandled;
        uint pvBase, pvDonor1, pvDonor2;
        string reason = "";
        bool forging;
        Component forge;          // the Blacksmith you last opened
        Component suppressed;     // its weaponGoalReached is set for this frame only
        readonly uint[] ids = new uint[Proto.FgMax];
        float nextLook;
        bool available;
        int lastCount = -1;
        bool lastDriving;

        /// <summary>For the test aid: Blacksmith screens asked for.</summary>
        public uint Opened => openSeq;
        public uint LastResult => result;
        public uint Handled => handled;

        bool Look()
        {
            if (looked) return forgeType != null;
            looked = true;
            forgeType = Type.GetType("ForgeShroom, Assembly-CSharp");
            controllerType = Type.GetType("PlayerController, Assembly-CSharp");
            finderType = Type.GetType("SporeInteractableFinder, Assembly-CSharp");
            const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            goalField = forgeType?.GetField("weaponGoalReached", any);
            interactField = controllerType?.GetField("interact", any);
            canActField = controllerType?.GetField("canAct", any);
            closestField = finderType?.GetField("closestInteractableObject", any);
            finderListField = finderType?.GetField("interactableObjects", any);
            McLog.Info($"forge: ForgeShroom {forgeType != null}, weaponGoalReached {goalField != null}, PlayerController.interact {interactField != null}, canAct {canActField != null}, closest thing {closestField != null}");
            return forgeType != null;
        }

        /// <summary>Before every other script this frame: catch the Interact key on the Blacksmith.</summary>
        public void Early(bool minecraftDriving, bool minecraftScreenOpen)
        {
            if (!Look()) return;
            PruneFinder();
            if (!minecraftDriving || goalField == null || interactField == null || closestField == null) return;
            GameObject parent = GameObject.Find("PlayerParent");
            Component controller = parent != null ? parent.GetComponent(controllerType) : null;
            if (controller == null) return;
            if (!(interactField.GetValue(controller) is UnityEngine.InputSystem.InputAction action) || !action.triggered) return;
            if (canActField != null && canActField.GetValue(controller) is bool can && !can) return;
            Component spore = PlayerFinder();
            GameObject closest = spore != null ? closestField.GetValue(spore) as GameObject : null;
            Component f = closest != null ? closest.GetComponent(forgeType) : null;
            if (f == null) return;
            Interacted(f, minecraftScreenOpen);
        }

        /// <summary>Interact on the Blacksmith (from Early, or the test aid).</summary>
        public void Interacted(Component f, bool minecraftScreenOpen)
        {
            if (goalField.GetValue(f) is bool done && done)
            {
                McLog.Info($"forge: interact on '{f.gameObject.name}': it has forged already (Mycelium's rule: once)");
                return;
            }
            goalField.SetValue(f, true); // Mycelium's Interact does nothing this frame
            suppressed = f;
            forge = f;
            if (minecraftScreenOpen)
            {
                McLog.Info("forge: interact on the Blacksmith while a Minecraft screen is open: not opened again");
                return;
            }
            openSeq++;
            McLog.Info($"forge: interact on the Blacksmith '{f.gameObject.name}': Minecraft's smithing screen asked for (#{openSeq}); Mycelium's own Interact skipped");
        }

        /// <summary>After every other script this frame.</summary>
        public void Late()
        {
            if (suppressed == null) return;
            if (!forging || suppressed != forge) goalField.SetValue(suppressed, false);
            suppressed = null;
        }

        /// <summary>Every frame (the driver's Update): send Minecraft what it needs, handle its request.</summary>
        public void Update(MonoBehaviour host, MyceliumLink link, WeaponBag bag, bool minecraftDriving)
        {
            if (!Look()) return;
            if (Time.realtimeSinceStartup >= nextLook)
            {
                nextLook = Time.realtimeSinceStartup + 0.5f;
                available = false;
                foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(forgeType))
                    if (o is Component c && !(goalField?.GetValue(c) is bool d && d && c != suppressed)) { available = true; break; }
            }
            if (minecraftDriving != lastDriving)
            {
                lastDriving = minecraftDriving;
                McLog.Info($"forge: Minecraft drives the spore: {minecraftDriving} (the Blacksmith's screen works only then)");
            }
            int n = minecraftDriving ? bag.Forgeable(ids) : 0;
            if (n != lastCount)
            {
                lastCount = n;
                McLog.Info($"forge: the Blacksmith takes {n} of your weapons{Ids(n)} (a Blacksmith here: {available})");
            }
            // The result slot's preview: the forged weapon's numbers as it would be.
            uint pv = link.ReadForgePreview(out uint pb, out uint p1, out uint p2);
            if (pv != lastPreview && minecraftDriving)
            {
                lastPreview = pv;
                pvBase = pb; pvDonor1 = p1; pvDonor2 = p2;
                uint answering = pv;
                host.StartCoroutine(bag.BuildPreview(pb, p1, p2, (ok, why) =>
                {
                    if (answering != lastPreview) return; // the slots changed meanwhile
                    previewHandled = answering;
                    if (!ok) McLog.Info($"forge: no preview for #{pb}, #{p1}, #{p2}: {why}");
                }));
            }
            MyceliumLink.WeaponEntry entry = default;
            if (previewHandled != lastPreview || !bag.PreviewEntry(pvBase, pvDonor1, pvDonor2, out entry)) entry = default;
            link.WriteForge(openSeq, available, ids, n, handled, result, reason, previewHandled, entry);

            uint req = link.ReadForgeRequest(out uint baseId, out uint d1, out uint d2);
            if (req == lastRequest || forging) return;
            if (lastRequest == 0 && handled == 0 && req != 0 && openSeq == 0)
            {
                // A request left from before this Mycelium started: not ours to answer.
                lastRequest = req;
                handled = req;
                result = 0;
                reason = "Mycelium was restarted";
                McLog.Info($"forge: Minecraft's request #{req} is from before this Mycelium started; answered as refused");
                return;
            }
            lastRequest = req;
            if (!minecraftDriving) { Answer(req, 0, "Minecraft isn't driving the spore"); return; }
            McLog.Info($"forge: Minecraft asks to forge #{baseId} (base) with #{d1} and #{d2} (request #{req})");
            forging = true;
            host.StartCoroutine(bag.Forge(baseId, d1, d2, (id, why) =>
            {
                forging = false;
                Answer(req, id, why);
                if (id != 0 && forge != null) goalField.SetValue(forge, true); // done, as Mycelium's Blacksmith is after forging
            }));
        }

        string Ids(int n)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < n; i++) sb.Append(i == 0 ? ": #" : ", #").Append(ids[i]);
            return sb.ToString();
        }

        void Answer(uint req, uint id, string why)
        {
            handled = req;
            result = id;
            reason = why ?? "";
            McLog.Info(id != 0 ? $"forge: request #{req} answered: weapon #{id}; the Blacksmith is done" : $"forge: request #{req} refused: {reason}");
        }

        /// <summary>
        /// Mycelium's list of things near enough to interact with (SporeInteractableFinder.
        /// interactableObjects) loses a thing only when it leaves the trigger. The weapon you swap
        /// out on a pickup is removed by this mod (it goes into your bag, WeaponBag) while it's in
        /// that list, so the finder's Update threw on it every frame from then on, and nothing near
        /// you became the thing to interact with again (seen in the forge test, 2026-11-08). Removed
        /// things are taken off the list before the finder runs.
        /// </summary>
        void PruneFinder()
        {
            Component finder = PlayerFinder();
            if (finder == null || finderListField == null) return;
            if (!(finderListField.GetValue(finder) is System.Collections.Generic.List<GameObject> list)) return;
            int gone = list.RemoveAll(o => o == null);
            if (gone > 0)
            {
                pruned += gone;
                McLog.Info($"interact: {gone} removed thing(s) taken off Mycelium's list of things near enough to interact with ({pruned} so far; one is the weapon a pickup swaps out, which goes into your bag)");
            }
        }

        FieldInfo finderListField;
        int pruned;

        Component PlayerFinder()
        {
            GameObject spore = GameObject.FindWithTag("currentPlayer");
            return spore != null ? spore.GetComponent(finderType) : null;
        }

        /// <summary>For the test aid: the Blacksmith nearest the spore.</summary>
        public Component Nearest(Vector3 from)
        {
            if (!Look()) return null;
            Component best = null;
            float bestD = float.MaxValue;
            foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsOfType(forgeType))
            {
                if (!(o is Component c)) continue;
                float d = Vector3.Distance(from, c.transform.position);
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        public void SceneLoaded()
        {
            forge = null;
            suppressed = null;
            nextLook = 0f;
        }
    }

    /// <summary>Runs before every other script (Early) and after (Late), for ForgeWatch.</summary>
    [DefaultExecutionOrder(-10000)]
    sealed class ForgeCatch : MonoBehaviour
    {
        public Action EarlyUpdate, LateAfter;
        void Update() => EarlyUpdate?.Invoke();
        void LateUpdate() => LateAfter?.Invoke();
    }
}
