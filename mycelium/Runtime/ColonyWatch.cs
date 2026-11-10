using System;
using System.Collections.Generic;
using System.Reflection;
using MyceliumCraft.Link;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MyceliumCraft
{
    /// <summary>
    /// Your colony in Minecraft's player list instead of Mycelium's colony meter (your request,
    /// 2026-11-03).
    ///
    /// Mycelium's meter is HUDHappiness on the "HUD" object: a "Colony x N" text and a slider with
    /// the colony's average happiness (HappinessManager.GetAverageColonyHappiness: the mean of
    /// every spore's CharacterStats.sporeHappiness, 0..1). While Minecraft drives, both are switched
    /// off, and switched back to how Mycelium had them afterwards (Mycelium shows or hides the
    /// meter itself: only at the Carcass with two spores or more, so its choice made meanwhile is
    /// kept). Every spore in the colony (SwapCharacter.characters on "PlayerParent"): name, body and
    /// cap colours (DesignTracker.bodyColor, capColor), happiness and walking speed go to Minecraft
    /// (Colony block), which lists them under the players with a text bar of the average
    /// (MycColony.java), and makes each one's skin and body (MycSkins.java, MycSporeBodies.java).
    /// A spore grown in the shop plays Mycelium's "Sprout" animation (SpawnCharacter): how far through
    /// goes too, every frame while it lasts, and its body digs out of the ground to match.
    /// During a run (any level but The Carcass) Mycelium's nutrient bonus goes too, worked out as its
    /// HUD does (HUDNutrients: 50% per loop after the first), and the list shows that in place of
    /// the happiness bar.
    /// </summary>
    sealed class ColonyWatch
    {
        const float LookEvery = 0.1f; // often enough for the bodies' legs to start and stop with the spore

        public readonly uint[] Colors = new uint[Proto.ColMax];
        public readonly uint[] Caps = new uint[Proto.ColMax];
        public readonly float[] Speeds = new float[Proto.ColMax];
        public readonly float[] Emerges = new float[Proto.ColMax];
        /// <summary>How far through Mycelium's "Drum Hit" each spore is (0..1), -1 when it isn't hitting the beetle drum.</summary>
        public readonly float[] Drums = new float[Proto.ColMax];
        /// <summary>Each spore at a decoration (as yours, MinecraftDriver.DecorationAnims): 0 none, 1 sitting, 2 lying on its back, 4 dancing, 5 lying head ahead.</summary>
        public readonly byte[] Poses = new byte[Proto.ColMax];
        /// <summary>Each spore's facing as a Minecraft yaw.</summary>
        public readonly float[] Yaws = new float[Proto.ColMax];
        readonly Dictionary<GameObject, int> lastPose = new Dictionary<GameObject, int>();
        readonly Dictionary<RuntimeAnimatorController, bool> hasActionState = new Dictionary<RuntimeAnimatorController, bool>();
        /// <summary>Bit i: spore i is waving (Mycelium's Wave trigger plays its Animator's "Hello" state: WaveCurio, waving back, the dance and drum curios).</summary>
        public uint Waving;
        uint wavingBefore;
        public readonly GameObject[] Spores = new GameObject[Proto.ColMax];
        public readonly float[] Happiness = new float[Proto.ColMax];
        public readonly string[] Names = new string[Proto.ColMax];
        public int Count;
        public int Current = -1;
        public float Average = -1f;
        public int NutrientBonus = -1; // percent; -1 = not in a run

        float nextLook;
        string lastLogged = "";
        readonly Dictionary<GameObject, (Vector3 pos, float time, float speed)> lastSeen = new Dictionary<GameObject, (Vector3, float, float)>();
        int sprouting, drumming;

        // Mycelium's meter while we hide it: the objects, and whether Mycelium had each on.
        readonly List<(GameObject obj, bool wasOn)> hidden = new List<(GameObject, bool)>();
        Component hud;
        bool hiding;

        /// <summary>Every frame. driving: Minecraft drives the spore. Returns true when the colony was looked at again (send it).</summary>
        public bool Update(bool driving, float scale)
        {
            HideMeter(driving);
            if (Time.realtimeSinceStartup < nextLook) return false;
            Look(scale);
            // Every frame while a spore sprouts or hits the drum: Minecraft poses its body from how far through it is.
            nextLook = sprouting > 0 || drumming > 0 ? 0f : Time.realtimeSinceStartup + LookEvery;
            return true;
        }

        /// <summary>How fast the spore has moved along the ground since the last look, in blocks per second.</summary>
        float SpeedOf(GameObject spore, float scale)
        {
            Vector3 pos = spore.transform.position;
            float now = Time.realtimeSinceStartup, speed = 0f;
            bool known = lastSeen.TryGetValue(spore, out var seen);
            if (known && now - seen.time < LookEvery * 0.9f) return seen.speed; // looking every frame for a sprout: too short to measure
            if (known && scale > 0f)
            {
                Vector3 moved = pos - seen.pos;
                moved.y = 0f;
                speed = moved.magnitude / (now - seen.time) / scale;
            }
            if (speed < 0.1f) speed = 0f; // the physics' tiny drift isn't walking
            lastSeen[spore] = (pos, now, speed);
            return speed;
        }

        /// <summary>How far through Mycelium's Sprout the spore is (0..1), or -1 when it isn't in it.</summary>
        static float SproutOf(GameObject spore)
        {
            Animator animator = spore.GetComponent<Animator>();
            if (animator == null || !animator.isActiveAndEnabled) return -1f;
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            return state.IsName("Sprout") && state.normalizedTime < 1f ? Mathf.Clamp01(state.normalizedTime) : -1f;
        }

        /// <summary>
        /// How far through a beetle drum hit the spore is (0..1), or -1. Mycelium's DrumCurio triggers
        /// "DrumHit" on the beat; its Animator plays "Drum Hit" (SmashAnim at double speed, about
        /// 1.5 s with its blends). Also while blending into it, so the Minecraft body starts with it.
        /// </summary>
        static float DrumOf(GameObject spore)
        {
            Animator animator = spore.GetComponent<Animator>();
            if (animator == null || !animator.isActiveAndEnabled) return -1f;
            AnimatorStateInfo now = animator.GetCurrentAnimatorStateInfo(0);
            if (now.IsName("Drum Hit")) return Mathf.Clamp01(now.normalizedTime);
            if (animator.IsInTransition(0))
            {
                AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
                if (next.IsName("Drum Hit")) return Mathf.Clamp01(next.normalizedTime);
            }
            return -1f;
        }

        /// <summary>
        /// A spore at a decoration (your request, 2026-11-09): the curios set the Animator's
        /// InActionState and play SitFloor / SitStump / Sleep / Lay, as for you (MinecraftDriver.
        /// DecorationPose). Between two of those states the last pose is kept while InActionState
        /// stays on. 4 is dancing at the beetle drum (Bustin Moves).
        /// </summary>
        int PoseOf(GameObject spore, string name)
        {
            Animator a = spore.GetComponent<Animator>();
            int last = lastPose.TryGetValue(spore, out int l) ? l : 0, pose = 0;
            if (a != null && a.isActiveAndEnabled && a.runtimeAnimatorController != null)
            {
                if (!hasActionState.TryGetValue(a.runtimeAnimatorController, out bool has))
                {
                    has = false;
                    foreach (AnimatorControllerParameter p in a.parameters) if (p.name == "InActionState" && p.type == AnimatorControllerParameterType.Bool) has = true;
                    hasActionState[a.runtimeAnimatorController] = has;
                }
                if (has && a.GetBool("InActionState"))
                {
                    AnimatorStateInfo s = a.GetCurrentAnimatorStateInfo(0);
                    AnimatorStateInfo next = a.IsInTransition(0) ? a.GetNextAnimatorStateInfo(0) : s;
                    pose = -1;
                    foreach (var (state, p) in MinecraftDriver.DecorationAnims)
                        if (s.IsName(state) || next.IsName(state)) { pose = p; break; }
                    if (pose == -1) pose = last;
                }
            }
            if (pose != last)
            {
                lastPose[spore] = pose;
                McLog.Info($"colony: {name} {(pose == 1 ? "sits at a decoration" : pose == 2 ? "lies down on its back (head behind)" : pose == 5 ? "lies down head ahead" : pose == 4 ? "dances at the beetle drum (Minecraft plays an Emotecraft dance)" : "gets up from a decoration")}; Minecraft poses its body so");
            }
            return pose;
        }

        /// <summary>Is the spore waving: its Animator in "Hello", where Mycelium's Wave trigger takes it.</summary>
        static bool WavingNow(GameObject spore)
        {
            Animator animator = spore.GetComponent<Animator>();
            return animator != null && animator.isActiveAndEnabled && animator.GetCurrentAnimatorStateInfo(0).IsName("Hello");
        }

        /// <summary>Mycelium's nutrient bonus in percent, or -1 outside a run.</summary>
        static int NutrientBonusNow()
        {
            if (SceneManager.GetActiveScene().name == "The Carcass") return -1;
            object loop = Type.GetType("GlobalData, Assembly-CSharp")?.GetField("currentLoop")?.GetValue(null);
            return loop is int n ? Mathf.Max(0, (n - 1) * 50) : -1;
        }

        void Look(float scale)
        {
            Count = 0;
            Current = -1;
            Waving = 0;
            Average = -1f;
            NutrientBonus = NutrientBonusNow();
            int sproutingBefore = sprouting, drummingBefore = drumming;
            sprouting = 0;
            drumming = 0;
            GameObject parent = GameObject.FindWithTag("PlayerParent");
            Type swapType = Type.GetType("SwapCharacter, Assembly-CSharp");
            Type statsType = Type.GetType("CharacterStats, Assembly-CSharp");
            Type designType = Type.GetType("DesignTracker, Assembly-CSharp");
            Component swap = parent != null && swapType != null ? parent.GetComponent(swapType) : null;
            var list = swap != null ? swapType.GetField("characters")?.GetValue(swap) as List<GameObject> : null;
            if (list == null || statsType == null) { LogOnce("colony: none (no SwapCharacter on PlayerParent)"); return; }
            GameObject current = GameObject.FindWithTag("currentPlayer");
            FieldInfo nameField = statsType.GetField("sporeName"), happyField = statsType.GetField("sporeHappiness");
            FieldInfo colorField = designType?.GetField("bodyColor"), capField = designType?.GetField("capColor");
            float total = 0f;
            int all = 0;
            foreach (GameObject spore in list)
            {
                if (spore == null) continue;
                Component stats = spore.GetComponent(statsType);
                if (stats == null) continue;
                float happy = happyField?.GetValue(stats) is float h ? h : 0.5f;
                total += happy;
                all++;
                if (Count >= Proto.ColMax) continue;
                Component design = designType != null ? spore.GetComponent(designType) : null;
                Color c = design != null && colorField?.GetValue(design) is Color col ? col : Color.white;
                Color cap = design != null && capField?.GetValue(design) is Color capCol ? capCol : Color.white;
                Names[Count] = ((nameField?.GetValue(stats) as string) ?? "").Trim();
                Happiness[Count] = happy;
                Colors[Count] = Rgb(c);
                Caps[Count] = Rgb(cap);
                Speeds[Count] = SpeedOf(spore, scale);
                Emerges[Count] = SproutOf(spore);
                Drums[Count] = spore != current ? DrumOf(spore) : -1f;
                Poses[Count] = spore != current ? (byte)PoseOf(spore, Names[Count]) : (byte)0;
                Yaws[Count] = WorldMapping.McYaw(spore.transform.eulerAngles.y);
                if (Drums[Count] >= 0f) drumming++;
                if (spore != current && WavingNow(spore)) Waving |= 1u << Count;
                if (Emerges[Count] >= 0f && spore != current) sprouting++;
                Spores[Count] = spore;
                if (spore == current) Current = Count;
                Count++;
            }
            if (all > 0) Average = total / all;
            if (Waving != wavingBefore)
            {
                for (int i = 0; i < Count; i++)
                    if (((Waving ^ wavingBefore) & (1u << i)) != 0) McLog.Info($"colony: {Names[i]} {((Waving & (1u << i)) != 0 ? "waves (Minecraft plays Emotecraft's wave)" : "stops waving")}");
                wavingBefore = Waving;
            }
            if (sprouting != sproutingBefore) McLog.Info($"colony: {sprouting} other spore(s) in Mycelium's Sprout");
            if (drumming != drummingBefore) McLog.Info($"colony: {drumming} other spore(s) hitting the beetle drum (Mycelium's Drum Hit)");
            var parts = new List<string>();
            for (int i = 0; i < Count; i++) parts.Add($"{Names[i]}{(i == Current ? " (you)" : "")} {Happiness[i]:F2} body #{Colors[i]:X6} cap #{Caps[i]:X6}");
            LogOnce($"colony: {all} spore(s), average happiness {Average:F2}, nutrient bonus {NutrientBonus}: {string.Join(", ", parts)}");
        }

        static uint Rgb(Color c) =>
            ((uint)Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f) << 16) | ((uint)Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f) << 8) | (uint)Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f);

        void LogOnce(string text)
        {
            if (text == lastLogged) return;
            lastLogged = text;
            McLog.Info(text);
        }

        void HideMeter(bool driving)
        {
            if (!driving)
            {
                if (!hiding) return;
                hiding = false;
                int back = 0;
                foreach (var (obj, wasOn) in hidden)
                    if (obj != null) { obj.SetActive(wasOn); if (wasOn) back++; }
                hidden.Clear();
                McLog.Info($"colony: Mycelium's colony meter back as it was ({back} part(s) on)");
                return;
            }
            if (hud == null)
            {
                Type t = Type.GetType("HUDHappiness, Assembly-CSharp");
                GameObject h = GameObject.Find("HUD");
                hud = h != null && t != null ? h.GetComponent(t) : null;
                if (hud == null) return;
                hidden.Clear();
                hiding = false;
            }
            if (!hiding)
            {
                hiding = true;
                foreach (string field in new[] { "happinessSlider", "colonyText" })
                {
                    var part = hud.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(hud) as Component;
                    if (part != null) hidden.Add((part.gameObject, part.gameObject.activeSelf));
                }
                McLog.Info($"colony: Mycelium's colony meter hidden while Minecraft drives ({hidden.Count} part(s); on before: {string.Join(", ", hidden.ConvertAll(p => $"{p.obj.name}={p.wasOn}"))})");
            }
            // Mycelium may show it meanwhile (a new spore grown): note that, and keep it hidden.
            for (int i = 0; i < hidden.Count; i++)
            {
                var (obj, _) = hidden[i];
                if (obj == null || !obj.activeSelf) continue;
                hidden[i] = (obj, true);
                obj.SetActive(false);
            }
        }

        /// <summary>A new scene: Mycelium's HUD is a new one.</summary>
        public void SceneLoaded()
        {
            hud = null;
            hidden.Clear();
            hiding = false;
            nextLook = 0f;
            lastSeen.Clear();
        }
    }
}
