using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MyceliumCraft
{
    /// <summary>
    /// Mycelium's Curio key and waving, while Minecraft drives (your requests, 2026-11-08).
    ///
    /// - No wave from the Curio key with no decoration near. Mycelium's PlayerController.Update makes
    ///   your spore wave then (InteractWithCurio with no curio); you have Emotecraft's emote wheel for
    ///   that now. Before PlayerController reads the key (ForgeCatch's early step), the same check it
    ///   makes is made here: no weapon held, a curio in range (FindClosestCurioInRange,
    ///   playerInteractRange, a DanceCurio only with canDance). If there's none, the press is cleared
    ///   from its Curio action (InputAction.Reset, Unity's "hard reset": switching the action off
    ///   wouldn't do, as Disable keeps "pressed this frame"), so it does nothing. Other actions on the
    ///   same button (Salvage) aren't touched.
    /// - Spores wave back at Emotecraft's wave: when Minecraft says you started it (McState's
    ///   EMOTE_WAVE), Mycelium's own PlayerController.GoadWaveBack is called, as after its own wave:
    ///   each wandering spore near you and in front of you (within 60 degrees of where you face) may
    ///   wave back, more likely the happier it is.
    /// </summary>
    sealed class CurioKey
    {
        Type controllerType, curioType, danceType;
        FieldInfo curioAction, canAct, canDance, swapField, rangeField;
        MethodInfo findCurio, goadWaveBack;
        bool looked;
        bool lastEmoteWave;

        bool Look()
        {
            if (looked) return controllerType != null;
            looked = true;
            controllerType = Type.GetType("PlayerController, Assembly-CSharp");
            curioType = Type.GetType("Curio, Assembly-CSharp");
            danceType = Type.GetType("DanceCurio, Assembly-CSharp");
            const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            curioAction = controllerType?.GetField("curioInteract", any);
            canAct = controllerType?.GetField("canAct", any);
            canDance = controllerType?.GetField("canDance", any);
            swapField = controllerType?.GetField("swapWeapon", any);
            findCurio = controllerType?.GetMethod("FindClosestCurioInRange", any);
            goadWaveBack = controllerType?.GetMethod("GoadWaveBack", any);
            rangeField = curioType?.GetField("playerInteractRange");
            McLog.Info($"curio key: PlayerController {controllerType != null}, its Curio action {curioAction != null}, FindClosestCurioInRange {findCurio != null}, GoadWaveBack {goadWaveBack != null}");
            return controllerType != null;
        }

        static Component Controller(Type t)
        {
            GameObject parent = GameObject.Find("PlayerParent");
            return parent != null ? parent.GetComponent(t) : null;
        }

        /// <summary>Before every other script this frame (ForgeCatch).</summary>
        public void Early(bool minecraftDriving)
        {
            if (!minecraftDriving || !Look() || curioAction == null || findCurio == null) return;
            Component controller = Controller(controllerType);
            if (controller == null || !(curioAction.GetValue(controller) is InputAction action) || !action.triggered) return;
            if (canAct?.GetValue(controller) is bool can && !can) return;
            object swap = swapField?.GetValue(controller);
            if (swap != null && swap.GetType().GetProperty("O_curWeapon")?.GetValue(swap) is GameObject held && held != null) return; // Mycelium does nothing then anyway
            GameObject spore = GameObject.FindWithTag("currentPlayer");
            Component curio = spore != null ? findCurio.Invoke(controller, null) as Component : null;
            if (curio != null)
            {
                float range = rangeField?.GetValue(curio) is float r ? r : 2f;
                bool dance = danceType != null && danceType.IsInstanceOfType(curio);
                bool danceOk = !dance || (canDance?.GetValue(controller) is bool d && d);
                if (Vector3.Distance(spore.transform.position, curio.transform.position) < range && danceOk)
                {
                    McLog.Info($"curio key: '{curio.gameObject.name}' ({curio.GetType().Name}) in range: Mycelium uses it");
                    return;
                }
            }
            action.Reset();
            McLog.Info($"curio key: no decoration in range{(curio != null ? $" (nearest '{curio.gameObject.name}' is {Vector3.Distance(spore.transform.position, curio.transform.position):F1} units away)" : "")}: no wave (you have Emotecraft's emote wheel)");
        }

        /// <summary>Every frame: Minecraft says Emotecraft's wave is playing on you.</summary>
        public void EmoteWave(bool waving)
        {
            if (waving && !lastEmoteWave && Look() && goadWaveBack != null)
            {
                Component controller = Controller(controllerType);
                if (controller != null)
                {
                    try
                    {
                        goadWaveBack.Invoke(controller, null);
                        McLog.Info("curio key: you wave (Emotecraft): the spores near you and in front of you may wave back, as after Mycelium's own wave");
                    }
                    catch (Exception e)
                    {
                        McLog.Info($"curio key: the wave back failed: {(e.InnerException ?? e).Message}");
                    }
                }
            }
            lastEmoteWave = waving;
        }
    }
}
