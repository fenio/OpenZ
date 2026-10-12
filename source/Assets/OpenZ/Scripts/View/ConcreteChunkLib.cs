using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Resources/OpenZ/ConcreteChunks): 63 fractured chunks XS / S / M / L (bent rebar out of the broken faces of M / L, the team
    /// band on 7 band chunks) + 3 rubble heaps + 126 convex colliders (plain + mirrored). Loaded once, pools per class; the look
    /// (per-level concrete, fracture aggregate, rust / steel rebar, team trim) is set per submesh with a property block.
    /// </summary>
    public static class ConcreteChunkLib
    {
        [System.Serializable]
        public class Entry
        {
            public string name, cls, kind, collider, collider_mirrored;
            public float[] size_xyz, concrete_abc;
            public float volume_m3, rest_bottom_y, suggested_mass, longest_any, max_scale;
            public bool small_xs, band;
            public string[] slots;
            [System.NonSerialized] public Mesh mesh, col, colM;
            [System.NonSerialized] public Material[] mats;
            [System.NonSerialized] public int[] slotKind;                                            // per submesh: 0 outer, 1 fracture, 2 rebar, 3 trim
            public bool Heavy => cls == "L" || cls == "M";
            public float Thick => concrete_abc != null && concrete_abc.Length > 2 ? concrete_abc[2] : 0.3f;
            public float Longest => longest_any > 0f ? longest_any : (size_xyz != null && size_xyz.Length > 2 ? Mathf.Max(size_xyz[0], Mathf.Max(size_xyz[1], size_xyz[2])) : 1f);
        }

        [System.Serializable] class Manifest { public Entry[] chunks; public Entry[] heaps; }

        public const string Root = "OpenZ/ConcreteChunks/";
        public static bool Loaded { get; private set; }
        static bool tried;
        public static Material Outer, Fracture, Rebar, Trim;
        public static Texture2D RebarL02, RebarL17;
        public static readonly List<Entry> All = new List<Entry>(), Heaps = new List<Entry>();
        static readonly Dictionary<string, List<Entry>> pools = new Dictionary<string, List<Entry>>();
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), BaseMapId = Shader.PropertyToID("_BaseMap");
        static MaterialPropertyBlock mpb;
        public static IEnumerable<Material> Materials { get { if (Outer) yield return Outer; if (Fracture) yield return Fracture; if (Rebar) yield return Rebar; if (Trim) yield return Trim; } }

        public static bool Load()
        {
            if (tried) return Loaded;
            tried = true;
            float t0 = Time.realtimeSinceStartup;
            var vis = Resources.Load<GameObject>(Root + "OZ_ConcreteChunks");
            var cols = Resources.Load<GameObject>(Root + "OZ_ConcreteChunks_Colliders");
            var ta = Resources.Load<TextAsset>(Root + "concrete_chunks_manifest");
            Outer = Resources.Load<Material>(Root + "Materials/OZC_Outer"); Fracture = Resources.Load<Material>(Root + "Materials/OZC_Fracture");
            Rebar = Resources.Load<Material>(Root + "Materials/OZC_Rebar"); Trim = Resources.Load<Material>(Root + "Materials/OZC_Trim");
            RebarL02 = Resources.Load<Texture2D>(Root + "Textures/OZC_rebar_tile"); RebarL17 = Resources.Load<Texture2D>(Root + "Textures/OZC_rebar_steel_tile");
            if (!vis || !cols || !ta || !Outer || !Fracture || !Rebar || !Trim) { Debug.LogError("[OpenZ] chunk library: missing assets under Resources/" + Root + " (fallback: procedural blocks)"); return false; }
            Manifest man;
            try { man = JsonUtility.FromJson<Manifest>(ta.text); } catch (System.Exception e) { Debug.LogError("[OpenZ] chunk library manifest: " + e.Message); return false; }
            var meshes = new Dictionary<string, MeshRenderer>(); var colMeshes = new Dictionary<string, Mesh>();
            foreach (Transform c in vis.transform) { var mr = c.GetComponent<MeshRenderer>(); if (mr) meshes[c.name] = mr; }
            foreach (Transform c in cols.transform) { var mf = c.GetComponent<MeshFilter>(); if (mf && mf.sharedMesh) colMeshes[c.name] = mf.sharedMesh; }
            int bad = 0;
            System.Action<Entry> bind = e =>
            {
                if (!meshes.TryGetValue(e.name, out var mr)) { Debug.LogError("[OpenZ] chunk library: no mesh " + e.name); bad++; return; }
                e.mesh = mr.GetComponent<MeshFilter>().sharedMesh;
                var src = mr.sharedMaterials; e.mats = new Material[src.Length]; e.slotKind = new int[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    string n = src[i] ? src[i].name : "";
                    int k = n.StartsWith("OZC_Outer") ? 0 : n.StartsWith("OZC_Fracture") ? 1 : n.StartsWith("OZC_Rebar") ? 2 : n.StartsWith("OZC_Trim") ? 3 : -1;
                    if (k < 0) { Debug.LogError("[OpenZ] chunk library: " + e.name + " slot " + i + " material '" + n + "' matches no OZC_* slot"); k = 1; bad++; }
                    e.slotKind[i] = k; e.mats[i] = k == 0 ? Outer : k == 1 ? Fracture : k == 2 ? Rebar : Trim;
                }
                if (e.slots != null) foreach (var s in e.slots) { bool found = false; foreach (var m in src) if (m && m.name.StartsWith(s)) found = true; if (!found) { Debug.LogError("[OpenZ] chunk library: " + e.name + " lists slot " + s + " but the mesh has none"); bad++; } }
                if (!string.IsNullOrEmpty(e.collider) && colMeshes.TryGetValue(e.collider, out var cm)) e.col = cm;
                if (!string.IsNullOrEmpty(e.collider_mirrored) && colMeshes.TryGetValue(e.collider_mirrored, out var cmm)) e.colM = cmm;
                if (e.max_scale <= 0f) e.max_scale = 1.15f;
            };
            if (man?.chunks != null) foreach (var e in man.chunks) { bind(e); if (e.mesh) All.Add(e); }
            if (man?.heaps != null) foreach (var e in man.heaps) { bind(e); if (e.mesh) Heaps.Add(e); }
            foreach (var e in All)
            {
                string key = e.band ? "b" + e.cls : e.cls;
                Add(key, e);
                if (!e.band && e.cls == "XS" && e.small_xs) Add("XSs", e);
                if (e.col && e.col.isReadable) Physics.BakeMesh(e.col.GetInstanceID(), true);
                if (e.colM && e.colM.isReadable) Physics.BakeMesh(e.colM.GetInstanceID(), true);
            }
            Loaded = All.Count > 0;
            Debug.Log($"[OpenZ] chunk library: {All.Count} chunks (L {Count("L")} M {Count("M")} S {Count("S")} XS {Count("XS")}, band {Count("bXS") + Count("bS") + Count("bM")}), {Heaps.Count} heaps, {colMeshes.Count} colliders baked, {bad} slot problems, {(Time.realtimeSinceStartup - t0) * 1000f:F0} ms");
            return Loaded;
        }

        static void Add(string key, Entry e) { if (!pools.TryGetValue(key, out var l)) pools[key] = l = new List<Entry>(); l.Add(e); }
        public static int Count(string key) => pools.TryGetValue(key, out var l) ? l.Count : 0;
        public static List<Entry> Pool(string key) => pools.TryGetValue(key, out var l) ? l : null;

        public static Entry Pick(System.Random rnd, string cls, bool halfSmallXs = false)
        {
            if (cls == "XS" && halfSmallXs && rnd.NextDouble() < 0.5 && Count("XSs") > 0) cls = "XSs";
            var l = Pool(cls); if (l == null || l.Count == 0) l = Pool("S");
            return l[rnd.Next(l.Count)];
        }

        /// <summary>A band chunk of that class (team paint); v1.4 has no band L: an L request returns a plain L.</summary>
        public static Entry PickBand(System.Random rnd, string cls)
        {
            var l = Pool("b" + cls);
            if (l == null || l.Count == 0) return Pick(rnd, cls);
            return l[rnd.Next(l.Count)];
        }

        public static Entry Heap(int i) => Heaps.Count > 0 ? Heaps[i % Heaps.Count] : null;

        public static Vector3 RandomScale(System.Random rnd, float maxScale)
        {
            float m = maxScale > 0f ? maxScale : 1.15f;
            return new Vector3(Mathf.Min(m, 0.85f + 0.3f * (float)rnd.NextDouble()), Mathf.Min(m, 0.85f + 0.3f * (float)rnd.NextDouble()), Mathf.Min(m, 0.85f + 0.3f * (float)rnd.NextDouble()));
        }

        public static Quaternion RandomRotation(System.Random rnd)
        {   // uniform (Shoemake)
            double u1 = rnd.NextDouble(), u2 = rnd.NextDouble() * 2.0 * System.Math.PI, u3 = rnd.NextDouble() * 2.0 * System.Math.PI;
            float a = (float)System.Math.Sqrt(1 - u1), b = (float)System.Math.Sqrt(u1);
            return new Quaternion(a * Mathf.Sin((float)u2), a * Mathf.Cos((float)u2), b * Mathf.Sin((float)u3), b * Mathf.Cos((float)u3));
        }

        static bool L17 => ZGame.I && ZGame.I.level == "L17";
        static readonly Color L02Outer = new Color(0.84f, 0.78f, 0.67f), L02Frac = new Color(0.89f, 0.78f, 0.61f);
        static readonly Color L17Outer = new Color(0.56f, 0.41f, 0.28f), L17Frac = new Color(0.50f, 0.42f, 0.35f);
        static readonly Color Scorch = new Color(0.22f, 0.19f, 0.17f);
        /// <summary>The level's concrete tones (the materials themselves are white: every user tints them): outer face, broken face, soot.</summary>
        public static Color LevelOuter => L17 ? L17Outer : L02Outer;
        public static Color LevelFracture => L17 ? L17Frac : L02Frac;
        public static Color ScorchTone => Scorch;

        /// <summary>README 6: per submesh, the level's concrete (x 0.92-1.04), the fracture aggregate a little scorched (L / M less),
        /// the level's rebar map, the trim neutral until the death sets the owner (SetTrim).</summary>
        public static void ApplyLook(MeshRenderer mr, Entry e, System.Random rnd)
        {
            if (!mr || e == null || e.slotKind == null) return;
            mpb ??= new MaterialPropertyBlock();
            bool l17 = L17;
            float v = 0.92f + 0.12f * (float)rnd.NextDouble();
            float sc = e.Heavy ? 0.03f + 0.05f * (float)rnd.NextDouble() : 0.03f + 0.12f * (float)rnd.NextDouble();
            var outer = (l17 ? L17Outer : L02Outer) * v; outer.a = 1f;
            var frac = Color.Lerp((l17 ? L17Frac : L02Frac) * v, Scorch, sc); frac.a = 1f;
            for (int i = 0; i < e.slotKind.Length; i++)
            {
                mpb.Clear();
                switch (e.slotKind[i])
                {
                    case 0: mpb.SetColor(BaseColorId, outer); break;
                    case 1: mpb.SetColor(BaseColorId, frac); break;
                    case 2: var rt = l17 ? RebarL17 : RebarL02; if (rt) mpb.SetTexture(BaseMapId, rt); mpb.SetColor(BaseColorId, Color.white); break;
                    default: mpb.SetColor(BaseColorId, Color.Lerp(TeamColors.Neutral, Color.black, 0.2f)); break;
                }
                mr.SetPropertyBlock(mpb, i);
            }
        }

        /// <summary>The team band (Trim submesh) in the owner's colour, 15-25 % toward black; set at the death (the owner is final then).</summary>
        public static void SetTrim(MeshRenderer mr, Entry e, Color team, System.Random rnd)
        {
            if (!mr || e == null || e.slotKind == null || !e.band) return;
            mpb ??= new MaterialPropertyBlock();
            var c = Color.Lerp(team, Color.black, 0.15f + 0.1f * (float)rnd.NextDouble()); c.a = 1f;
            for (int i = 0; i < e.slotKind.Length; i++)
            {
                if (e.slotKind[i] != 3) continue;
                mr.GetPropertyBlock(mpb, i); mpb.SetColor(BaseColorId, c); mr.SetPropertyBlock(mpb, i);
            }
        }
    }
}
