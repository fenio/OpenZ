using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// the next). View-only static collections that outlive a level are reset in one place:
    ///  * NewWorld() (ZGame, right before a new ZWorld is built): the per-match ones keyed by sim id, and the debug / proof logs,
    ///    are cleared, so match 2 behaves exactly like match 1 (e.g. RobotPopDeath.knocked no longer blocks part knock-offs);
    ///  * on every scene load and in NewWorld: entries keyed by a destroyed Unity object (a UnitView, Transform, Light, Renderer,
    ///    Mesh of the old level) are pruned. Those kept the dead C# wrappers and everything they reference (a UnitView holds its
    ///    ZObject, which holds the whole old ZWorld) alive across levels.
    /// Lossless: nothing live is dropped (sim ids of the new world do not exist yet when NewWorld runs; pruning only removes keys
    /// whose Unity object is destroyed). The sim is never touched. Logged once per call: [OpenZ] level statics: ...
    /// </summary>
    public static class OzLevelStatics
    {
        /// <summary>Per-match state keyed by sim id, and debug / proof logs: cleared when a new world starts.</summary>
        static readonly string[] ClearOnNewWorld =
        {
            "MechFoley.lastServo", "MechRockets.shotIndex", "UnitAim.StatById", "RobotPopDeath.knocked",
            "OzSoundV3.slew", "OzSoundV3.spins", "OzSoundV3.veh", "OzSoundV3.accentWindow", "OzSoundV3.ventNext", "OzSoundV3.bridgeTick",
            "MissileCannonSalvo.salvo", "NightLights.relit", "GatlingBrass.drums", "EngineRev.last", "CaptureHold.lastShown", "FlagView.cueLast",
            "GrenadeCrateV2.Trace", "OpenZFxPlayer.Exp2Log", "OzShakeGate.Log", "BlastWave.BulletKills", "BlastWave.DrawnKills", "RollingWheel.Log",
            // kept the old ZWorld's vehicle alive and blocked IsHeld for a new vehicle with the same id, the Exp2 volley windows)
            "OzSoundV3.forts", "CaptureHold.holds", "OpenZFxPlayer.e2Volleys",
        };
        /// <summary>Keyed by Unity objects of a level: entries whose object is destroyed are removed (scene load + new world).</summary>
        static readonly string[] PruneDead =
        {
            "MechFoley.walks", "MechRockets.podRest", "NightShadows.tierSet", "ApcCraneKit.kitHulls", "TowerMounts.fortTops",
            "HitSurface.cache", "PlayerInput.meshRs",
        };

        /// <summary>
        /// that only hold references. Cleared when the main menu loads, so the menu checkpoint's UnloadUnusedAssets can free the in-match
        /// sprites; every lookup loads the same asset again on demand (Resources.Load returns the loaded object while it is in memory).</summary>
        static readonly string[] ClearOnMenu = { "OzKit.spriteCache" };

        public static string Last = "not run";
        static Dictionary<string, Type> types;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            Run(false, "scene " + s.name);
            if (s.name != "MainMenu") return;
            int missing = 0, released = 0;                                                                       // soak_hitch_fix
            try { foreach (var n in ClearOnMenu) released += Do(n, true, ref missing); }
            catch (Exception e) { Debug.LogWarning("[OpenZ] level statics (menu): " + e.GetType().Name + ": " + e.Message); }
            Debug.Log($"[OpenZ] level statics: menu: released {released} cached Resources references{(missing > 0 ? $", {missing} field(s) not found" : "")}");
        }

        /// <summary>Called by ZGame right before it builds a new ZWorld.</summary>
        public static void NewWorld() { Run(true, "new world"); }

        static void Run(bool clear, string why)
        {
            int cleared = 0, pruned = 0, missing = 0;
            try
            {
                if (clear) foreach (var n in ClearOnNewWorld) cleared += Do(n, true, ref missing);
                foreach (var n in PruneDead) pruned += Do(n, false, ref missing);
            }
            catch (Exception e) { Debug.LogWarning("[OpenZ] level statics: " + e.GetType().Name + ": " + e.Message); }
            Last = $"{why}: cleared {cleared} entries, pruned {pruned} dead-object entries{(missing > 0 ? $", {missing} field(s) not found" : "")}";
            Debug.Log("[OpenZ] level statics: " + Last);
        }

        static int Do(string name, bool clear, ref int missing)
        {
            int dot = name.LastIndexOf('.');
            var t = FindType(name.Substring(0, dot));
            var f = t?.GetField(name.Substring(dot + 1), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (f == null) { missing++; return 0; }
            var v = f.GetValue(null);
            if (v == null) return 0;
            if (clear)
            {
                int before = v is ICollection c ? c.Count : CountOf(v);
                var cm = v.GetType().GetMethod("Clear", Type.EmptyTypes);
                if (cm == null) { missing++; return 0; }
                cm.Invoke(v, null);
                return before;
            }
            var dead = new List<object>();
            if (v is IDictionary d)
            {
                foreach (DictionaryEntry e in d) if (e.Key is UnityEngine.Object o && o == null) dead.Add(e.Key);
                foreach (var k in dead) d.Remove(k);
                return dead.Count;
            }
            if (v is IList l)
            {
                int n = 0;
                for (int i = l.Count - 1; i >= 0; i--) if (l[i] is UnityEngine.Object o && o == null) { l.RemoveAt(i); n++; }
                return n;
            }
            if (v is IEnumerable en)
            {
                foreach (var x in en) if (x is UnityEngine.Object o && o == null) dead.Add(x);
                var rm = v.GetType().GetMethod("Remove");
                if (rm == null) return 0;
                foreach (var x in dead) rm.Invoke(v, new[] { x });
                return dead.Count;
            }
            return 0;
        }

        static int CountOf(object v) { int n = 0; if (v is IEnumerable en) foreach (var _ in en) n++; return n; }

        static Type FindType(string shortName)
        {
            if (types == null)
            {
                types = new Dictionary<string, Type>();
                foreach (var t in typeof(OzLevelStatics).Assembly.GetTypes())
                    if (t.Namespace == "OpenZ" && !t.IsNested && !types.ContainsKey(t.Name)) types[t.Name] = t;
            }
            return types.TryGetValue(shortName, out var r) ? r : null;
        }
    }
}
