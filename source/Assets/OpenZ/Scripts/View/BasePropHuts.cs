using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// base_props_v2: which military installation stands on which Zod hut tile, its gate side and its ground decor
    /// and generators next to the forts / factories, fuel where vehicles park, comms relays on the edges, supply compounds on
    /// the approach roads; neighbours never share a set), plus a safety net: at scene load every ZodObjects/hut_x_y without its
    /// v2 installation gets it from Resources (e.g. a scene rebuilt by OpenZ Setup after the bake), and the ground decor
    /// (BasePropDecor: jeep tracks / foot trails toward the nearest road or building, power cables) is built.
    /// Only the view changes: tile, blocking and health stay Zod's hut (ohut.cpp, ZWorld.SetMapImpassables).
    /// </summary>
    public static class BasePropHuts
    {
        public const string ResourceDir = "OpenZ/BaseProps2/";
        public const string LayoutResource = "OpenZ/BaseProps2/base_props_v2_layout";
        public static readonly string[] Sets = { "Solar", "Fuel", "Generator", "Comms", "Supply" };

        [System.Serializable] public class Line { public float[] xz; }
        [System.Serializable]
        public class Hut
        {
            public int x, y; public string set, gate, target, track_kind, why;
            public float[] track; public Line[] cables;
            public float[] nudge;                                  // installation centre offset from the tile centre (x, z m); null = none
        }
        [System.Serializable] public class Level { public string level; public float[] cable_uv; public Hut[] huts; }
        [System.Serializable] public class LayoutFile { public int version; public Level[] levels; }

        // fallback when the layout file is missing (same table as layout_v2.py, gates from its path planner)
        static readonly Dictionary<(int, int), (string, string)> L02 = new Dictionary<(int, int), (string, string)>
        {
            [(5, 10)] = ("Generator", "S"), [(9, 7)] = ("Solar", "E"), [(57, 7)] = ("Fuel", "S"),
            [(60, 55)] = ("Supply", "W"), [(64, 57)] = ("Comms", "W"), [(65, 80)] = ("Generator", "W"), [(61, 82)] = ("Solar", "W"),
        };
        static readonly Dictionary<(int, int), (string, string)> L17 = new Dictionary<(int, int), (string, string)>
        {
            [(99, 29)] = ("Generator", "W"), [(104, 33)] = ("Solar", "S"), [(92, 6)] = ("Supply", "S"), [(79, 8)] = ("Comms", "S"),
            [(73, 30)] = ("Fuel", "W"), [(44, 7)] = ("Supply", "S"), [(20, 6)] = ("Comms", "W"), [(23, 9)] = ("Fuel", "S"),
            [(5, 26)] = ("Generator", "E"), [(7, 30)] = ("Solar", "E"), [(32, 39)] = ("Supply", "W"), [(6, 58)] = ("Generator", "S"),
            [(3, 64)] = ("Comms", "E"),
        };

        // geometry cuts into its frame. View only: the hut root, the blocked tile and the sim stay on the tile.
        // L17 (104,33): the cliffs v3 retaining wall fills the frame's north-east corner -> 0.35 m south (tools/layout_v2.py NUDGE).
        static readonly Dictionary<(string, int, int), Vector2> NudgeFallback = new Dictionary<(string, int, int), Vector2>
        {
            [("L17", 104, 33)] = new Vector2(0f, -0.35f),
        };

        static LayoutFile layout; static bool layoutTried;

        public static LayoutFile Layout
        {
            get
            {
                if (layout == null && !layoutTried)
                {
                    layoutTried = true;
                    var ta = Resources.Load<TextAsset>(LayoutResource);
                    if (ta) { try { layout = JsonUtility.FromJson<LayoutFile>(ta.text); } catch (System.Exception e) { Debug.LogWarning("[OpenZ] base props v2 layout: " + e.Message); } }
                }
                return layout;
            }
        }

        /// <summary>Re-read the layout (editor bake after an import).</summary>
        public static void ReloadLayout() { layout = null; layoutTried = false; }

        public static Level LevelLayout(string level)
        {
            var l = Layout;
            if (l?.levels != null) foreach (var lv in l.levels) if (lv != null && lv.level == level) return lv;
            return null;
        }

        public static Hut HutFor(string level, int x, int y)
        {
            var lv = LevelLayout(level);
            if (lv?.huts != null) foreach (var h in lv.huts) if (h != null && h.x == x && h.y == y) return h;
            return null;
        }

        public static string LevelOf(Scene s)
        {
            if (ZGame.I && ZGame.I.gameObject.scene == s && !string.IsNullOrEmpty(ZGame.I.level)) return ZGame.I.level;
            return s.name.StartsWith("L17") ? "L17" : s.name.StartsWith("L16") ? "L16" : "L02";
        }

        /// <summary>Installation + gate for a hut tile; unknown tiles (another map) get a stable pick.</summary>
        public static (string set, string gate) SetFor(string level, int x, int y)
        {
            var h = HutFor(level, x, y);
            if (h != null && !string.IsNullOrEmpty(h.set)) return (h.set, string.IsNullOrEmpty(h.gate) ? "S" : h.gate);
            var map = level == "L17" ? L17 : L02;
            if (map.TryGetValue((x, y), out var k)) return k;
            return (Sets[Mathf.Abs(x * 7 + y * 13) % Sets.Length], "S");
        }

        /// <summary>Local offset of the installation (the BP2_ child) from its hut root on the tile centre (world x, z metres).</summary>
        public static Vector3 NudgeFor(string level, int x, int y)
        {
            var h = HutFor(level, x, y);
            if (h?.nudge != null && h.nudge.Length >= 2) return new Vector3(h.nudge[0], 0f, h.nudge[1]);
            return NudgeFallback.TryGetValue((level, x, y), out var n) ? new Vector3(n.x, 0f, n.y) : Vector3.zero;
        }

        public static bool TryParseHut(string name, out int x, out int y)
        {
            x = y = 0;
            if (!name.StartsWith("hut_")) return false;
            var p = name.Split('_');
            return p.Length >= 3 && int.TryParse(p[1], out x) && int.TryParse(p[2], out y);
        }

        public static string PrefabName(string level, string set) => $"BP2_{level}_{set}";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            SceneManager.sceneLoaded += OnLoaded;
            for (int i = 0; i < SceneManager.sceneCount; i++) OnLoaded(SceneManager.GetSceneAt(i), LoadSceneMode.Additive);
        }

        static void OnLoaded(Scene s, LoadSceneMode m)
        {
            int huts = Swap(s, out int swapped);
            if (huts > 0) BasePropDecor.Ensure(s, LevelOf(s));
        }

        /// <summary>Runtime safety net: huts without their v2 installation get it (the old model is hidden and destroyed);
        /// baked ones only get their gate re-applied. Returns the number of huts found.</summary>
        public static int Swap(Scene s, out int swapped)
        {
            swapped = 0;
            if (!s.IsValid() || !s.isLoaded) return 0;
            Transform root = null;
            foreach (var g in s.GetRootGameObjects()) if (g.name == "ZodObjects") { root = g.transform; break; }
            if (!root) return 0;
            string level = LevelOf(s);
            if (level == "L16") return 0;                                                 // level 16: its huts are the jungle huts placed in the scene (L16Hut): no installation, no base decor
            int n = 0;
            foreach (Transform hut in root)
            {
                if (!TryParseHut(hut.name, out int x, out int y)) continue;
                n++;
                var (want, gate) = SetFor(level, x, y);
                var nudge = NudgeFor(level, x, y);
                var have = hut.GetComponentInChildren<BaseProp>(true);
                if (have && have.setKey == $"{level}_{want}" && have.gameObject.name.StartsWith("BP2_"))
                {
                    if (have.gate != gate) have.SetGate(gate);
                    if ((hut.localScale - Vector3.one).sqrMagnitude > 1e-6f || hut.rotation != Quaternion.identity
                        || (have.transform.localPosition - nudge).sqrMagnitude > 1e-6f)
                    {   // OpenZ Setup fits Z_hut to 1.95 m by scale (and may nudge it): the installation stands at full size on its tile
                        hut.localScale = Vector3.one; hut.rotation = Quaternion.identity;
                        hut.position = new Vector3(x * 2 + 1, hut.position.y, -(y * 2 + 1));
                        have.transform.localPosition = nudge; have.transform.localRotation = Quaternion.identity;
                        HideDecorOn(s, hut.position + nudge);
                        swapped++;
                    }
                    continue;
                }
                var pf = Resources.Load<GameObject>(ResourceDir + PrefabName(level, want));
                if (!pf) { Debug.LogWarning($"[OpenZ] base props v2: Resources/{ResourceDir}{PrefabName(level, want)} missing (base props v2 not imported)"); return n; }
                for (int i = hut.childCount - 1; i >= 0; i--) { var c = hut.GetChild(i).gameObject; c.SetActive(false); Object.Destroy(c); }
                hut.localScale = Vector3.one;
                hut.rotation = Quaternion.identity;
                var tile = new Vector3(x * 2 + 1, hut.position.y, -(y * 2 + 1));
                var go = Object.Instantiate(pf, tile + nudge, Quaternion.identity, hut);
                go.name = pf.name;
                var bp = go.GetComponent<BaseProp>();
                if (bp) { bp.gate = gate; bp.SetGate(gate); }
                HideDecorOn(s, tile + nudge);
                swapped++;
            }
            if (swapped > 0) Debug.Log($"[OpenZ] base props v2: {swapped} huts on {level} swapped to their installations at load (bake the scene to keep them: OpenZ/Base Props/2)");
            return n;
        }

        /// <summary>Hides level decor (DEC_*: cacti, boulders, crates) standing inside an installation's frame (the frame overhangs
        /// the Zod tile; decor never overlaps it). The editor bake does the same and saves it.</summary>
        public static int HideDecorOn(Scene s, Vector3 tile, float hx = 1.8f, float hz = 1.6f)
        {
            int hidden = 0;
            foreach (var g in s.GetRootGameObjects())
            {
                if (g.name == "ZodObjects") continue;
                foreach (var r in g.GetComponentsInChildren<Renderer>(false))
                {
                    var t = r.transform;
                    Transform dec = null;
                    for (var q = t; q; q = q.parent) if (q.name.StartsWith("DEC_")) { dec = q; break; }
                    if (!dec || !dec.gameObject.activeSelf) continue;
                    var b = r.bounds;
                    if (b.max.x < tile.x - hx || b.min.x > tile.x + hx || b.max.z < tile.z - hz || b.min.z > tile.z + hz) continue;
                    if (b.size.x > 12f || b.size.z > 12f) continue;                                           // never a terrain-sized mesh
                    dec.gameObject.SetActive(false); hidden++;
                }
            }
            return hidden;
        }
    }
}
