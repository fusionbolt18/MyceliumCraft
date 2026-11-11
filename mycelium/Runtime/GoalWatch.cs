using System;
using System.Reflection;
using MyceliumCraft.Link;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MyceliumCraft
{
    /// <summary>
    /// The level's goal on Minecraft's locator bar (your request, 2026-11-10; MycGoal.java).
    ///
    /// The goal is what Mycelium's own Navigate Camera turns the camera to (VCamRotator.levelGoal,
    /// found by the scene's name when the level starts): the light in New Tutorial, ==StartRun== in
    /// The Carcass, BackToHub in Daybreak Arboretum and Delta Crag, the Rival Sporemother in Impact
    /// Barrens, nothing anywhere else. Read from VCamRotator; if it found nothing (a generated level's
    /// goal can appear after it looked), the same rule is applied again here, once a second.
    /// Its position goes to Minecraft every frame (it can move: the boss walks).
    /// </summary>
    sealed class GoalWatch
    {
        static readonly Type RotatorType = Type.GetType("VCamRotator, Assembly-CSharp");
        static readonly FieldInfo GoalField = RotatorType?.GetField("levelGoal", BindingFlags.NonPublic | BindingFlags.Instance);

        GameObject goal;
        string scene = "";
        string logged = "";
        float nextFind;

        /// <summary>Every frame. on: Minecraft drives the spore in a level. Writes the goal (or none) to the link.</summary>
        public void Update(MyceliumLink link, WorldMapping map, bool on)
        {
            if (link == null) return;
            string now = SceneManager.GetActiveScene().name;
            if (now != scene)
            {
                scene = now;
                goal = null;
                nextFind = 0f;
            }
            if (on && (goal == null || !goal.activeInHierarchy) && Time.unscaledTime >= nextFind)
            {
                nextFind = Time.unscaledTime + 1f;
                goal = Find(out string how);
                string line = goal != null ? $"'{goal.name}' in '{scene}' ({how})" : $"none in '{scene}' ({how})";
                if (line != logged)
                {
                    logged = line;
                    McLog.Info($"goal: {line}" + (goal != null ? ", shown on Minecraft's locator bar" : ""));
                }
            }
            if (!on || goal == null || !goal.activeInHierarchy || map == null)
            {
                link.WriteGoal(false, 0f, 0f, 0f);
                return;
            }
            Vector3 p = goal.transform.position;
            map.ToMc(p.x, p.y, p.z, out double x, out double y, out double z);
            link.WriteGoal(true, (float)x, (float)y, (float)z);
        }

        GameObject Find(out string how)
        {
            var rotator = RotatorType != null ? UnityEngine.Object.FindObjectOfType(RotatorType) : null;
            if (rotator != null && GoalField != null && GoalField.GetValue(rotator) is GameObject g && g != null)
            {
                how = "Navigate Camera's goal";
                return g;
            }
            // VCamRotator's own rule, by the scene's name.
            string name;
            switch (scene)
            {
                case "New Tutorial": name = "ParSys_GodRays"; break;
                case "The Carcass": name = "==StartRun=="; break;
                case "Daybreak Arboretum":
                case "Delta Crag": name = "BackToHub"; break;
                case "Impact Barrens": name = "Rival Sporemother"; break;
                default: how = "this level has no goal"; return null;
            }
            how = rotator == null ? "no VCamRotator; found by name" : "found by name";
            return GameObject.Find(name);
        }
    }
}
