using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Heavy shell bodies (howitzer round, fort gun round). Convention of a body: unit length, nose at +Z, centred on its
    /// length (base at z = -0.5, tip at z = +0.5), three sub-meshes: <see cref="Steel"/> the body, <see cref="Band"/> the
    /// copper driving band(s) and the fuze, <see cref="Hot"/> the recessed cup in the base (drawn with a glowing material).
    /// Draw it with a uniform scale = the wanted length in metres.
    /// <list type="bullet">
    /// <item>First choice: the imported models <see cref="HowitzerModel"/> and <see cref="FortModel"/> (Resources). They are
    /// read, turned and scaled into the convention by <see cref="FromModel"/>, whatever axis or scale the import gave them:
    /// the long axis comes from the bounds, the nose is its thinner end, the sub-meshes are matched by material name.</item>
    /// <item>Fallback: the same two profiles turned around the axis in code (<see cref="Lathe"/>), used when a model is
    /// missing, not readable or not a three part shell. Same shape, same triangle counts.</item>
    /// <item>Profile of the fallback: a list of strips (z along the axis, r the radius, both as fractions of the length).
    /// Every strip has its own vertices, so the joint between two strips is a crisp edge, while the points inside one strip
    /// (the ogive) are shaded smooth. Order: base centre, out along the base, then up the side to the tip. With that order
    /// the surface faces (-dr, dz) in the (z, r) plane, which is outwards everywhere, and the triangles are front faces.</item>
    /// </list>
    /// </summary>
    public static class OpenZShellMesh
    {
        public const int Steel = 0, Band = 1, Hot = 2;
        public const int Sides = 12;
        /// <summary>Imported models: Resources paths without extension.</summary>
        public const string HowitzerModel = "OpenZ/FX/shell_howitzer", FortModel = "OpenZ/FX/shell_fort";
        /// <summary>False = always the code-built bodies (comparison and test only).</summary>
        public static bool UseModels = true;
        /// <summary>Where each body came from ("model" or "code") and its triangle counts. Filled when the body is made.</summary>
        public static string HowitzerInfo = "", FortInfo = "";

        struct Strip { public int mat; public float[] zr; }                                    // z0, r0, z1, r1, ...
        static Strip S(int mat, params float[] zr) => new Strip { mat = mat, zr = zr };

        /// <summary>Howitzer round: a high-explosive shell, 3.7 calibres long. Base cup, boat tail, one wide driving band, a
        /// small step before the long pointed ogive, a fuze.</summary>
        public static Mesh Howitzer() { return Get(HowitzerModel, "HeavyShellHowitzer", true, out HowitzerInfo); }

        /// <summary>Fort gun round: a heavy capped shell, 3.2 calibres long. Base cup, tapered base, two driving bands, a cap
        /// joint and a pointed cap.</summary>
        public static Mesh Fort() { return Get(FortModel, "HeavyShellFort", false, out FortInfo); }

        static Mesh Get(string resource, string name, bool howitzer, out string info)
        {
            string why = "models switched off";
            Mesh m = UseModels ? FromModel(resource, name, out why) : null;
            bool model = m != null;
            if (!model)
            {
                Debug.LogWarning("[OpenZ] heavy shell model " + resource + " not used (" + why + "): the code-built body is drawn");
                m = howitzer ? HowitzerCode() : FortCode();
            }
            info = name + ": " + (model ? "model" : "code") + ", triangles steel / band / hot " + (m.GetIndexCount(Steel) / 3) + " / "
                + (m.GetIndexCount(Band) / 3) + " / " + (m.GetIndexCount(Hot) / 3) + ", " + m.vertexCount + " verts";
            Debug.Log("[OpenZ] heavy shell body " + info);
            return m;
        }

        /// <summary>An imported shell model as a new mesh in this class's convention; null (and the reason) when it cannot be
        /// used. The model needs Read/Write enabled in its import settings.</summary>
        static Mesh FromModel(string resource, string name, out string why)
        {
            why = "not found in Resources";
            var go = Resources.Load<GameObject>(resource);
            if (!go) return null;
            var mf = go.GetComponentInChildren<MeshFilter>(true);
            var src = mf ? mf.sharedMesh : null;
            why = "no mesh"; if (!src) return null;
            why = "Read/Write is off in its import settings"; if (!src.isReadable) return null;
            why = "fewer than 3 sub-meshes"; if (src.subMeshCount < 3) return null;
            var v = src.vertices; var n = src.normals; var uv = src.uv;
            why = "no vertices or no normals"; if (v.Length == 0 || n.Length != v.Length) return null;
            var M = go.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            for (int i = 0; i < v.Length; i++) { v[i] = M.MultiplyPoint3x4(v[i]); n[i] = M.MultiplyVector(n[i]).normalized; }
            // the long axis of the bounds is the flight axis, its thinner end is the nose
            Vector3 min = v[0], max = v[0];
            for (int i = 1; i < v.Length; i++) { min = Vector3.Min(min, v[i]); max = Vector3.Max(max, v[i]); }
            Vector3 c = (min + max) * 0.5f, e = (max - min) * 0.5f;
            int ax = e.x >= e.y && e.x >= e.z ? 0 : e.y >= e.z ? 1 : 2;
            var axis = ax == 0 ? Vector3.right : ax == 1 ? Vector3.up : Vector3.forward;
            float half = e[ax];
            why = "empty bounds"; if (half < 1e-6f) return null;
            float wPlus = 0f, wMinus = 0f, rMax = 0f; int cPlus = 0, cMinus = 0;
            for (int i = 0; i < v.Length; i++)
            {
                var q = v[i] - c; float along = Vector3.Dot(q, axis), rad = (q - axis * along).magnitude;
                if (rad > rMax) rMax = rad;
                if (along > half * 0.6f) { wPlus += rad; cPlus++; } else if (along < -half * 0.6f) { wMinus += rad; cMinus++; }
            }
            why = "not a shell shape"; if (cPlus == 0 || cMinus == 0 || rMax > half * 0.6f || rMax < half * 0.1f) return null;
            var nose = wPlus / cPlus <= wMinus / cMinus ? axis : -axis;
            var rot = Quaternion.FromToRotation(nose, Vector3.forward); float k = 0.5f / half;
            for (int i = 0; i < v.Length; i++) { v[i] = rot * (v[i] - c) * k; n[i] = rot * n[i]; }
            // sub-meshes by material name, else in the order of the file (steel, copper, hot)
            int steel = 0, band = 1, hot = 2;
            var mr = mf.GetComponent<MeshRenderer>(); var mats = mr ? mr.sharedMaterials : null;
            if (mats != null)
            {
                int s = -1, b = -1, h = -1;
                for (int i = 0; i < mats.Length && i < src.subMeshCount; i++)
                {
                    string mn = mats[i] ? mats[i].name : "";
                    if (mn.Contains("Steel")) s = i; else if (mn.Contains("Copper")) b = i; else if (mn.Contains("Hot")) h = i;
                }
                if (s >= 0 && b >= 0 && h >= 0) { steel = s; band = b; hot = h; }
            }
            var hotTris = src.GetTriangles(hot);
            float hotZ = 0f; foreach (int t in hotTris) hotZ += v[t].z;
            why = "sub-mesh order: the hot part is not the base cup"; if (hotTris.Length == 0 || hotZ / hotTris.Length > -0.44f) return null;
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetNormals(n); if (uv.Length == v.Length) m.SetUVs(0, uv);
            m.subMeshCount = 3;
            m.SetTriangles(src.GetTriangles(steel), Steel); m.SetTriangles(src.GetTriangles(band), Band); m.SetTriangles(hotTris, Hot);
            m.RecalculateBounds();
            why = "";
            return m;
        }

        // ---------------------------------------------------------------------------------------- fallback: built in code
        static Mesh HowitzerCode()
        {
            var o = new List<float>(); Ogive(o, 0.478f, 0.129f, 0.50f, 0f, 0.12f, 0.23f, 0.33f, 0.4195f);
            float zo = o[o.Count - 2], ro = o[o.Count - 1];                                     // the fuze seat, about (.8975, .040)
            return Lathe("HeavyShellHowitzer", new[]
            {
                S(Hot, 0.030f, 0f, 0.030f, 0.078f),                                             // base cup: floor
                S(Hot, 0.030f, 0.078f, 0f, 0.098f),                                             // base cup: wall
                S(Steel, 0f, 0.098f, 0f, 0.112f),                                               // base rim
                S(Steel, 0f, 0.112f, 0.105f, 0.135f),                                           // boat tail
                S(Band, 0.105f, 0.135f, 0.113f, 0.147f),                                        // driving band: rear chamfer
                S(Band, 0.113f, 0.147f, 0.172f, 0.147f),                                        // driving band: land
                S(Band, 0.172f, 0.147f, 0.180f, 0.135f),                                        // driving band: front chamfer
                S(Steel, 0.180f, 0.135f, 0.470f, 0.135f),                                       // body
                S(Steel, 0.470f, 0.135f, 0.478f, 0.129f),                                       // step before the ogive
                new Strip { mat = Steel, zr = o.ToArray() },                                    // ogive
                S(Band, zo, ro, 0.955f, 0.0165f, 1f, 0f),                                       // fuze, pointed
            });
        }

        static Mesh FortCode()
        {
            var o = new List<float>(); Ogive(o, 0.520f, 0.149f, 0.48f, 0f, 0.1632f, 0.2976f, 0.408f);
            o.Add(1f); o.Add(0f);                                                               // the ogive closes at the tip
            return Lathe("HeavyShellFort", new[]
            {
                S(Hot, 0.032f, 0f, 0.032f, 0.094f),                                             // base cup: floor
                S(Hot, 0.032f, 0.094f, 0f, 0.118f),                                             // base cup: wall
                S(Steel, 0f, 0.118f, 0f, 0.138f),                                               // base rim
                S(Steel, 0f, 0.138f, 0.045f, 0.156f),                                           // base taper
                S(Band, 0.045f, 0.156f, 0.053f, 0.170f),                                        // rear driving band
                S(Band, 0.053f, 0.170f, 0.103f, 0.170f),
                S(Band, 0.103f, 0.170f, 0.111f, 0.156f),
                S(Steel, 0.111f, 0.156f, 0.146f, 0.156f),                                       // between the bands
                S(Band, 0.146f, 0.156f, 0.154f, 0.170f),                                        // front driving band
                S(Band, 0.154f, 0.170f, 0.204f, 0.170f),
                S(Band, 0.204f, 0.170f, 0.212f, 0.156f),
                S(Steel, 0.212f, 0.156f, 0.512f, 0.156f),                                       // body
                S(Steel, 0.512f, 0.156f, 0.520f, 0.149f),                                       // cap joint: a small step down
                new Strip { mat = Steel, zr = o.ToArray() },                                    // cap, pointed
            });
        }

        /// <summary>Tangent ogive: leaves (z0, rb) parallel to the axis and would close to a point after fullLen. Adds one
        /// point for every distance in xs.</summary>
        static void Ogive(List<float> zr, float z0, float rb, float fullLen, params float[] xs)
        {
            float rho = (rb * rb + fullLen * fullLen) / (2f * rb);
            foreach (float x in xs)
            {
                zr.Add(z0 + x); zr.Add(Mathf.Max(0f, Mathf.Sqrt(Mathf.Max(0f, rho * rho - x * x)) + rb - rho));
            }
        }

        static void Accumulate(float dz, float dr, ref float nz, ref float nr)
        {
            float l = Mathf.Sqrt(dz * dz + dr * dr); if (l < 1e-7f) return;
            nz += -dr / l; nr += dz / l;
        }

        static Mesh Lathe(string name, Strip[] strips)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>();
            var tris = new[] { new List<int>(), new List<int>(), new List<int>() };
            int ring = Sides + 1;
            foreach (var st in strips)
            {
                var zr = st.zr; int pts = zr.Length / 2; if (pts < 2) continue;
                int first = v.Count;
                for (int k = 0; k < pts; k++)
                {
                    float z = zr[k * 2], r = zr[k * 2 + 1], nz = 0f, nr = 0f;
                    if (k > 0) Accumulate(z - zr[k * 2 - 2], r - zr[k * 2 - 1], ref nz, ref nr);
                    if (k < pts - 1) Accumulate(zr[k * 2 + 2] - z, zr[k * 2 + 3] - r, ref nz, ref nr);
                    float nl = Mathf.Sqrt(nz * nz + nr * nr);
                    if (nl > 1e-6f) { nz /= nl; nr /= nl; } else { nz = 1f; nr = 0f; }
                    for (int i = 0; i <= Sides; i++)
                    {
                        float ang = i * Mathf.PI * 2f / Sides, cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                        v.Add(new Vector3(r * cs, r * sn, z - 0.5f));
                        n.Add(new Vector3(nr * cs, nr * sn, nz));
                    }
                }
                var list = tris[st.mat];
                for (int k = 0; k < pts - 1; k++)
                {
                    bool onAxisA = zr[k * 2 + 1] < 1e-6f, onAxisB = zr[k * 2 + 3] < 1e-6f;   // a ring on the axis: one triangle of each quad has no area
                    for (int i = 0; i < Sides; i++)
                    {
                        int a = first + k * ring + i, b = a + 1, c = a + ring, d = c + 1;
                        if (!onAxisA) { list.Add(a); list.Add(b); list.Add(c); }
                        if (!onAxisB) { list.Add(b); list.Add(d); list.Add(c); }
                    }
                }
            }
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetNormals(n);
            m.subMeshCount = 3;
            for (int i = 0; i < 3; i++) m.SetTriangles(tris[i], i);
            m.RecalculateBounds();
            return m;
        }
    }
}
