using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// cuts a building's own model into a RUIN (every triangle below a jagged, blocky cut height: broken walls, corner towers left
    /// standing taller) and a few BIG PARTS (the triangles above the cut, grouped by sector around the centre and by height band:
    /// cells, rebuilt from the model). Every piece is double-sided (back faces with flipped normals), so a broken wall shows its
    /// inside instead of turning invisible. Meshes stay in the source mesh space (a part's pivot is found from its bounds).
    /// Sources, in order: baked meshes in Resources/OpenZ/Fractures/R_&lt;key&gt;_meshes.asset (OpenZ/Bake Building Ruins, build-safe),
    /// else built at run time from the model when it is Read/Write (the fort FBX), else null (BuildingBreaker sinks the model).
    /// Cached per building key; built during OpenZEnding's level-load prewarm, never at the moment of the death.
    /// </summary>
    public static class BuildingRuins
    {
        public sealed class Set
        {
            public Mesh ruin;
            public readonly List<Mesh> parts = new List<Mesh>();
            public string source;                                                              // "baked" / "runtime"
        }

        /// <summary>(cut base, jag amplitude, taller corners, sectors) as fractions of the model height.</summary>
        public static (float cut, float amp, float corner, int sectors) Params(string key)
        {
            switch (key)
            {
                case "fort": return (0.26f, 0.20f, 0.22f, 6);
                case "robot_factory": case "vehicle_factory": return (0.22f, 0.20f, 0.14f, 5);
                case "repair": return (0.20f, 0.18f, 0.12f, 4);
                case "radar": return (0.18f, 0.16f, 0.10f, 4);
                default: return (0.22f, 0.18f, 0.12f, 4);
            }
        }

        static readonly Dictionary<string, Set> cache = new Dictionary<string, Set>();

        /// <summary>The ruin + big parts for this building key; null when neither baked nor buildable (cached either way).</summary>
        public static Set Get(string key, Mesh source)
        {
            if (cache.TryGetValue(key, out var s)) return s;
            s = LoadBaked(key);
            if (s == null && source)
            {
                // run time: only a Read/Write model (the fort FBX); the imported GLB buildings come baked (OpenZ/Bake Building Ruins),
                // reading a non-readable mesh in Play would log an error and return nothing
                if (source.isReadable)
                {
                    try { s = Build(source, key); if (s != null) s.source = "runtime"; }
                    catch (System.Exception e) { Debug.LogWarning($"[OpenZ] ruin {key}: cannot read the model ({e.Message}); the building sinks instead"); s = null; }
                }
                else Debug.LogWarning($"[OpenZ] ruin {key}: model not Read/Write and no baked R_{key}_meshes (run OpenZ/Bake Building Ruins); the building sinks instead");
            }
            cache[key] = s;
            if (s != null) Debug.Log($"[OpenZ] ruin {key}: {s.source}, ruin {(s.ruin ? s.ruin.triangles.Length / 3 : 0)} tris, {s.parts.Count} big parts");
            return s;
        }

        static Set LoadBaked(string key)
        {
            var all = Resources.LoadAll<Mesh>("OpenZ/Fractures/R_" + key + "_meshes");
            if (all == null || all.Length == 0) return null;
            var s = new Set { source = "baked" };
            var parts = new List<Mesh>();
            foreach (var m in all)
            {
                if (!m) continue;
                if (m.name.EndsWith("_ruin")) s.ruin = m;
                else if (m.name.Contains("_part_")) parts.Add(m);
            }
            parts.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            s.parts.AddRange(parts);
            return s.ruin ? s : null;
        }

        // ------------------------------------------------------------------------------------------------ cutting
        static float Hash(int x, int z, int k)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663) ^ (uint)(k * 83492791);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        /// <summary>Jagged cut height (mesh space) at (x, z): blocky broken-wall steps + a slow wave, corners stand taller.</summary>
        public static float CutY(Bounds b, string key, float x, float z)
        {
            var p = Params(key);
            float size = Mathf.Max(b.size.x, b.size.z), cell = Mathf.Max(0.9f, size / 11f);
            int cx = Mathf.FloorToInt(x / cell), cz = Mathf.FloorToInt(z / cell);
            float q = Hash(cx, cz, key.Length);
            float wave = 0.5f + 0.5f * Mathf.Sin(x * 0.83f + 1.3f) * Mathf.Cos(z * 0.67f + 0.4f);
            float n = 0.62f * q + 0.38f * wave;
            float ux = Mathf.Abs(x - b.center.x) / Mathf.Max(0.01f, b.extents.x), uz = Mathf.Abs(z - b.center.z) / Mathf.Max(0.01f, b.extents.z);
            float corner = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.8f, Mathf.Min(ux, uz)));   // both far from the centre = a corner
            return b.min.y + b.size.y * (p.cut + p.amp * n + p.corner * corner * (0.6f + 0.4f * q));
        }

        /// <summary>Cuts the model (any submesh count). Returns null for an empty or unreadable mesh.</summary>
        public static Set Build(Mesh src, string key)
        {
            var v = src.vertices; if (v == null || v.Length == 0) return null;
            var nrm = src.normals; var uv = src.uv; var col = src.colors; var tan = src.tangents;
            var b = src.bounds; var p = Params(key);
            int subs = src.subMeshCount, sectors = Mathf.Max(2, p.sectors);
            float topBand = b.min.y + b.size.y * 0.74f;
            var ruinTris = new List<int>[subs];
            var partTris = new Dictionary<int, List<int>[]>();
            for (int s = 0; s < subs; s++)
            {
                ruinTris[s] = new List<int>();
                var idx = src.GetTriangles(s);
                for (int i = 0; i + 2 < idx.Length; i += 3)
                {
                    int a = idx[i], c1 = idx[i + 1], c2 = idx[i + 2];
                    var cen = (v[a] + v[c1] + v[c2]) / 3f;
                    if (cen.y <= CutY(b, key, cen.x, cen.z)) { ruinTris[s].Add(a); ruinTris[s].Add(c1); ruinTris[s].Add(c2); continue; }
                    float ang = Mathf.Atan2(cen.z - b.center.z, cen.x - b.center.x) + Mathf.PI;
                    int sector = Mathf.Clamp((int)(ang / (2f * Mathf.PI) * sectors), 0, sectors - 1);
                    int id = sector * 2 + (cen.y > topBand ? 1 : 0);
                    if (!partTris.TryGetValue(id, out var lists)) { lists = new List<int>[subs]; for (int k = 0; k < subs; k++) lists[k] = new List<int>(); partTris[id] = lists; }
                    lists[s].Add(a); lists[s].Add(c1); lists[s].Add(c2);
                }
            }
            // crumbs (< 40 triangles) join the other band of their sector, or are dropped (the chunks and the dust cover them)
            int Count(List<int>[] l) { int n = 0; foreach (var x in l) n += x.Count / 3; return n; }
            var ids = new List<int>(partTris.Keys); ids.Sort();
            foreach (var id in ids)
            {
                if (!partTris.TryGetValue(id, out var l) || Count(l) >= 40) continue;
                int other = id ^ 1;
                if (partTris.TryGetValue(other, out var o)) for (int s = 0; s < subs; s++) o[s].AddRange(l[s]);
                partTris.Remove(id);
            }
            var set = new Set();
            set.ruin = Make($"R_{key}_ruin", ruinTris, v, nrm, uv, col, tan, subs);
            ids = new List<int>(partTris.Keys); ids.Sort();
            int pi = 0;
            foreach (var id in ids)
            {
                var l = partTris[id]; if (Count(l) < 12) continue;
                set.parts.Add(Make($"R_{key}_part_{pi++:D2}", l, v, nrm, uv, col, tan, subs));
            }
            return set.ruin ? set : null;
        }

        /// <summary>A double-sided mesh from the chosen triangles (same submesh layout, so the building's materials fit).</summary>
        static Mesh Make(string name, List<int>[] tris, Vector3[] v, Vector3[] n, Vector2[] uv, Color[] col, Vector4[] tan, int subs)
        {
            var map = new Dictionary<int, int>();
            var pv = new List<Vector3>(); var pn = new List<Vector3>(); var pu = new List<Vector2>(); var pc = new List<Color>(); var pt = new List<Vector4>();
            bool hasN = n != null && n.Length == v.Length, hasU = uv != null && uv.Length == v.Length, hasC = col != null && col.Length == v.Length, hasT = tan != null && tan.Length == v.Length;
            var front = new List<int>[subs]; var back = new List<int>[subs];
            for (int s = 0; s < subs; s++)
            {
                front[s] = new List<int>(tris[s].Count); back[s] = new List<int>(tris[s].Count);
                foreach (int o in tris[s])
                {
                    if (!map.TryGetValue(o, out int j))
                    {
                        j = pv.Count; map[o] = j; pv.Add(v[o]);
                        if (hasN) pn.Add(n[o]); if (hasU) pu.Add(uv[o]); if (hasC) pc.Add(col[o]); if (hasT) pt.Add(tan[o]);
                    }
                    front[s].Add(j);
                }
            }
            // back faces: own vertices (flipped normal / tangent), reversed winding
            int frontCount = pv.Count;
            for (int i = 0; i < frontCount; i++)
            {
                pv.Add(pv[i]);
                if (hasN) pn.Add(-pn[i]); if (hasU) pu.Add(pu[i]); if (hasC) pc.Add(pc[i]);
                if (hasT) { var t = pt[i]; pt.Add(new Vector4(-t.x, -t.y, -t.z, t.w)); }
            }
            for (int s = 0; s < subs; s++)
                for (int i = 0; i + 2 < front[s].Count; i += 3)
                { back[s].Add(front[s][i] + frontCount); back[s].Add(front[s][i + 2] + frontCount); back[s].Add(front[s][i + 1] + frontCount); }
            var m = new Mesh { name = name };
            if (pv.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(pv);
            if (hasN) m.SetNormals(pn); if (hasU) m.SetUVs(0, pu); if (hasC) m.SetColors(pc); if (hasT) m.SetTangents(pt);
            m.subMeshCount = subs;
            for (int s = 0; s < subs; s++) { var all = new List<int>(front[s]); all.AddRange(back[s]); m.SetTriangles(all, s, false); }
            m.RecalculateBounds();
            if (!hasN) m.RecalculateNormals();
            return m;
        }

        /// <summary>The building's main model renderer: the largest mesh below root, the fort gun excluded (FortGunView tosses it).</summary>
        public static MeshRenderer MainRenderer(Transform root)
        {
            MeshRenderer best = null; float bestV = 0f;
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (IsGunPart(mr.transform, root)) continue;
                var mf = mr.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
                var s = Vector3.Scale(mf.sharedMesh.bounds.size, mr.transform.lossyScale);
                float vol = Mathf.Abs(s.x * s.y * s.z);
                if (vol > bestV) { bestV = vol; best = mr; }
            }
            return best;
        }

        public static bool IsGunPart(Transform t, Transform root)
        {
            for (var x = t; x && x != root; x = x.parent) if (x.name.StartsWith("FortGun")) return true;
            return false;
        }
    }
}
