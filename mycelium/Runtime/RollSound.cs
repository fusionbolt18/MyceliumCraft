using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MyceliumCraft
{
    /// <summary>
    /// The dodge roll's sound plays as you press the key (your request, 2026-11-02).
    ///
    /// Mycelium plays it at the roll's end: PlayerController.Dodging moves the spore, waits out the
    /// roll's animation, and only then plays SoundEffectManager's "Stab" on the spore (with the
    /// landing dust). So at the roll's start (the spore's Animator "Roll" goes on in the same frame
    /// as the key press: Dodging sets it before its first wait) the same sound is played here, with
    /// Mycelium's own SoundEffectManager.PlaySound. The late one is stopped when it starts: a new
    /// "One shot audio" on the spore playing one of "Stab"'s clips, while that roll is on. Only
    /// while Minecraft drives; Mycelium's code is unchanged.
    /// </summary>
    sealed class RollSound
    {
        const string Sfx = "Stab";
        const string OursName = "One shot audio (roll, early)";

        bool wasRolling;
        bool lateStopped;
        float rollStart;
        readonly HashSet<AudioClip> clips = new HashSet<AudioClip>();
        bool clipsLooked;

        /// <summary>LateUpdate. rolling: the spore's Animator has "Roll" on.</summary>
        public void Update(bool driving, bool rolling)
        {
            if (!driving)
            {
                wasRolling = rolling;
                return;
            }
            GameObject spore = rolling || wasRolling ? GameObject.FindWithTag("currentPlayer") : null;
            if (rolling && !wasRolling && spore != null)
            {
                rollStart = Time.time;
                lateStopped = false;
                PlayNow(spore.transform);
            }
            if (rolling && !lateStopped && spore != null) StopLate(spore.transform);
            if (!rolling && wasRolling && !lateStopped)
                McLog.Info($"roll sound: roll over after {Time.time - rollStart:F2} s without Mycelium's own sound being seen (nothing to stop)");
            wasRolling = rolling;
        }

        void PlayNow(Transform spore)
        {
            object manager = Manager();
            if (manager == null) { McLog.Info("roll sound: no SoundEffectManager; Mycelium's roll sound stays at the roll's end"); lateStopped = true; return; }
            var before = new HashSet<Transform>();
            foreach (Transform c in spore) before.Add(c);
            try
            {
                manager.GetType().GetMethod("PlaySound", new[] { typeof(string), typeof(Transform), typeof(float), typeof(float), typeof(float) })
                    ?.Invoke(manager, new object[] { Sfx, spore, 0f, 1f, 50f });
            }
            catch (Exception e) { McLog.Info($"roll sound: playing failed: {(e.InnerException ?? e).Message}"); lateStopped = true; return; }
            int n = 0;
            foreach (Transform c in spore)
                if (!before.Contains(c) && c.name == "One shot audio") { c.name = OursName; n++; }
            McLog.Info($"roll sound: '{Sfx}' played as the roll starts ({n} sound{(n == 1 ? "" : "s")}); Mycelium's own at the roll's end will be stopped");
        }

        void StopLate(Transform spore)
        {
            LookUpClips();
            foreach (Transform c in spore)
            {
                if (c.name != "One shot audio") continue;
                var source = c.GetComponent<AudioSource>();
                if (source == null || source.clip == null || !clips.Contains(source.clip)) continue;
                source.Stop();
                source.volume = 0f;
                c.name = "One shot audio (roll, stopped)";
                lateStopped = true;
                McLog.Info($"roll sound: Mycelium's own roll sound stopped as it started, {Time.time - rollStart:F2} s into the roll");
            }
        }

        void LookUpClips()
        {
            if (clipsLooked) return;
            clipsLooked = true;
            object manager = Manager();
            if (!(manager?.GetType().GetField("soundEffects", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(manager) is IEnumerable list)) return;
            foreach (object sfx in list)
            {
                if (sfx == null || sfx.GetType().GetField("sfxName")?.GetValue(sfx) as string != Sfx) continue;
                if (sfx.GetType().GetField("sfxSounds")?.GetValue(sfx) is IEnumerable sounds)
                    foreach (object o in sounds) if (o is AudioClip a && a != null) clips.Add(a);
            }
            McLog.Info($"roll sound: '{Sfx}' has {clips.Count} clips");
        }

        static object Manager() =>
            Type.GetType("SoundEffectManager, Assembly-CSharp")?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is UnityEngine.Object o && o != null ? o : null;
    }
}
