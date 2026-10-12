using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>Procedural meshes and shared materials of the night lights (no imported art: open-source clean).
    /// Housings use the project's own OpenZ/TeamLit (gunmetal, lit like the buildings, night rim via _Accent); the light
    /// effects use the night package shaders through Resources/OpenZ/Night/M_Night*.mat (kept in player builds), with a
    /// Shader.Find fallback.</summary>
    public static class NightMeshes
    {
        static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        static Material beamMat, glowHaloMat, lensMat, bulbMat, poolMat, housingMat, lampPostMat, darkGlassMat;
        public static readonly int ColorId = Shader.PropertyToID("_Color"), EmissionId = Shader.PropertyToID("_EmissionColor"),
            FadeId = Shader.PropertyToID("_Fade");

        // ------------------------------------------------------------------ materials
        static Material Load(string res, string shader, System.Action<Material> setup)
        {
            var m = Resources.Load<Material>("OpenZ/Night/" + res);
            if (m) m = new Material(m);
            else
            {
                var s = Shader.Find(shader);
                if (!s) { Debug.LogWarning($"[OpenZ] NightLights: shader {shader} missing (README_IMPORT.md step 1)"); s = Shader.Find("Hidden/InternalErrorShader"); }
                m = new Material(s);
            }
            m.name = res + " (night)"; m.hideFlags = HideFlags.DontSave;
            setup?.Invoke(m);
            return m;
        }

        public static Material Beam() => beamMat ? beamMat : beamMat = Load("M_NightBeam", "OpenZ/NightBeam", null);
        public static Material Halo() => glowHaloMat ? glowHaloMat : glowHaloMat = Load("M_NightGlow", "OpenZ/NightGlow", m => m.SetFloat("_Mode", 0f));
        public static Material Lens() => lensMat ? lensMat : lensMat = Load("M_NightGlow", "OpenZ/NightGlow", m => { m.SetFloat("_Mode", 1f); m.SetFloat("_Falloff", 2.5f); });
        public static Material Bulb() => bulbMat ? bulbMat : bulbMat = Load("M_NightGlow", "OpenZ/NightGlow", m => m.SetFloat("_Mode", 2f));
        public static Material Pool() => poolMat ? poolMat : poolMat = Load("M_NightPool", "OpenZ/NightPool", null);

        static Material Lit(string name, Color baseCol, float smooth, float metal)
        {
            var s = Shader.Find("OpenZ/TeamLit");
            if (!s) s = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(s) { name = name, hideFlags = HideFlags.DontSave, enableInstancing = true };
            m.SetColor("_BaseColor", baseCol);
            if (m.HasProperty("_TeamMode")) m.SetFloat("_TeamMode", 0f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            if (m.HasProperty("_Accent")) m.SetFloat("_Accent", 1f);          // TeamLit: gets the night rim like the buildings
            if (m.HasProperty("_NightClass")) m.SetFloat("_NightClass", 2f);  // night_v2: fixtures are building parts (faint rim, no fill)
            if (m.HasProperty("_Degreen")) m.SetFloat("_Degreen", 0f);
            return m;
        }
        /// <summary>Gunmetal housings (searchlights, floods, beacon bases), the buildings' neutral metal.</summary>
        public static Material Housing() => housingMat ? housingMat : housingMat = Lit("NightHousing", new Color(0.33f, 0.34f, 0.36f), 0.45f, 0.35f);
        /// <summary>Fallback street-lamp post (no lamp prop to clone): the L17 lamps' deep teal paint.</summary>
        public static Material LampPostMaterial() => lampPostMat ? lampPostMat : lampPostMat = Lit("NightLampPost", new Color(0.24f, 0.36f, 0.38f), 0.3f, 0.2f);
        /// <summary>Dark glass in front of a lens (what is left when the light is off).</summary>
        public static Material DarkGlass() => darkGlassMat ? darkGlassMat : darkGlassMat = Lit("NightDarkGlass", new Color(0.09f, 0.1f, 0.12f), 0.85f, 0f);

        // ------------------------------------------------------------------ meshes
        static Mesh Cached(string key, System.Func<Mesh> make)
        {
            if (meshes.TryGetValue(key, out var m) && m) return m;
            m = make(); m.name = "Night_" + key; m.hideFlags = HideFlags.DontSave; meshes[key] = m; return m;
        }

        /// <summary>Open cone, apex at the origin, axis +z, length 1, end radius 1; uv.y 0 apex .. 1 end (NightBeam).</summary>
        public static Mesh Cone() => Cached("cone", () =>
        {
            const int S = 24, R = 6;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int r = 0; r <= R; r++)
            {
                float k = r / (float)R;
                for (int i = 0; i <= S; i++)
                {
                    float a = i * Mathf.PI * 2f / S; var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    v.Add(new Vector3(d.x * k, d.y * k, k));
                    n.Add(new Vector3(d.x, d.y, -1f).normalized);   // cone surface normal (45 deg cone in mesh space, rescaled by the transform)
                    uv.Add(new Vector2(i / (float)S, k));
                }
            }
            for (int r = 0; r < R; r++)
                for (int i = 0; i < S; i++)
                {
                    int a = r * (S + 1) + i, b = a + S + 1;
                    t.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            var m = new Mesh(); m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.bounds = new Bounds(new Vector3(0, 0, 0.5f), new Vector3(2f, 2f, 1f)); return m;
        });

        /// <summary>Unit cube -0.5..0.5 (NightPool volumes; the shader only needs positions).</summary>
        public static Mesh Cube() => Cached("cube", () =>
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i < 8; i++) v.Add(new Vector3((i & 1) == 0 ? -0.5f : 0.5f, (i & 2) == 0 ? -0.5f : 0.5f, (i & 4) == 0 ? -0.5f : 0.5f));
            int[] f = { 0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4, 2, 6, 7, 2, 7, 3, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5 };
            t.AddRange(f);
            var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
        });

        /// <summary>Quad in the XY plane (-1..1, normal +z), uv 0..1 (halo billboards and lenses; NightGlow draws both sides).</summary>
        public static Mesh Quad() => Cached("quad", () =>
        {
            var m = new Mesh();
            m.SetVertices(new List<Vector3> { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) });
            m.SetNormals(new List<Vector3> { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            m.bounds = new Bounds(Vector3.zero, new Vector3(2.5f, 2.5f, 2.5f));   // billboards turn: generous bounds
            return m;
        });

        /// <summary>Flat disc in the XY plane, radius 1, normal +z, uv = (x, y) * 0.5 + 0.5.</summary>
        public static Mesh Disc() => Cached("disc", () =>
        {
            const int S = 20;
            var v = new List<Vector3> { Vector3.zero }; var n = new List<Vector3> { Vector3.forward }; var uv = new List<Vector2> { new Vector2(0.5f, 0.5f) }; var t = new List<int>();
            for (int i = 0; i <= S; i++)
            {
                float a = i * Mathf.PI * 2f / S; var p = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                v.Add(p); n.Add(Vector3.forward); uv.Add(new Vector2(p.x * 0.5f + 0.5f, p.y * 0.5f + 0.5f));
                if (i < S) t.AddRange(new[] { 0, i + 1, i + 2 });
            }
            var m = new Mesh(); m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        });

        /// <summary>Closed cylinder along +z from z0 to z1 (radius r), optional front cap.</summary>
        static void Cylinder(List<Vector3> v, List<Vector3> n, List<int> t, float r, float z0, float z1, int seg, bool capFront, bool capBack, Vector3 off)
        {
            int b = v.Count;
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg; var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                v.Add(off + d * r + new Vector3(0, 0, z0)); n.Add(d);
                v.Add(off + d * r + new Vector3(0, 0, z1)); n.Add(d);
            }
            for (int i = 0; i < seg; i++) { int k = b + i * 2; t.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 }); }
            void Cap(float z, Vector3 nn, bool flip)
            {
                int c = v.Count; v.Add(off + new Vector3(0, 0, z)); n.Add(nn);
                for (int i = 0; i <= seg; i++) { float a = i * Mathf.PI * 2f / seg; v.Add(off + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z)); n.Add(nn); }
                for (int i = 0; i < seg; i++) { if (flip) t.AddRange(new[] { c, c + 1 + i, c + 2 + i }); else t.AddRange(new[] { c, c + 2 + i, c + 1 + i }); }
            }
            if (capFront) Cap(z1, Vector3.forward, true);
            if (capBack) Cap(z0, Vector3.back, false);
        }

        static void Box(List<Vector3> v, List<Vector3> n, List<int> t, Vector3 c, Vector3 s)
        {
            Vector3 h = s * 0.5f;
            Vector3[] N = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var nn in N)
            {
                Vector3 u = Mathf.Abs(nn.y) > 0.5f ? Vector3.right : Vector3.up, w = Vector3.Cross(nn, u);
                int b = v.Count;
                Vector3 fc = c + Vector3.Scale(nn, h);
                Vector3 uu = Vector3.Scale(u, h), ww = Vector3.Scale(w, h);
                v.Add(fc - uu - ww); v.Add(fc + uu - ww); v.Add(fc + uu + ww); v.Add(fc - uu + ww);
                for (int i = 0; i < 4; i++) n.Add(nn);
                // wind so the front face points along nn
                if (Vector3.Dot(Vector3.Cross(v[b + 1] - v[b], v[b + 2] - v[b]), nn) >= 0) t.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                else t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }
        }

        static Mesh Build(List<Vector3> v, List<Vector3> n, List<int> t)
        {
            var m = new Mesh(); m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }

        /// <summary>Searchlight drum (lamp axis +z, 0.32 m radius, 0.62 m long, the lens at z = +0.34) around its pitch pivot.</summary>
        public static Mesh SearchDrum() => Cached("drum", () =>
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            Cylinder(v, n, t, 0.32f, -0.28f, 0.34f, 20, false, true, Vector3.zero);      // body (front open: the lens sits there)
            Cylinder(v, n, t, 0.35f, 0.26f, 0.36f, 20, false, false, Vector3.zero);      // front bezel ring
            Cylinder(v, n, t, 0.2f, -0.42f, -0.28f, 12, false, true, Vector3.zero);     // rear housing
            Box(v, n, t, new Vector3(0f, 0.36f, 0.02f), new Vector3(0.08f, 0.06f, 0.44f));   // top handle bar
            return Build(v, n, t);
        });

        /// <summary>Searchlight yoke (turns in yaw): two side arms up to the drum trunnions + a turntable.</summary>
        public static Mesh SearchYoke() => Cached("yoke", () =>
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            Box(v, n, t, new Vector3(0.39f, 0.02f, 0f), new Vector3(0.06f, 0.5f, 0.16f));
            Box(v, n, t, new Vector3(-0.39f, 0.02f, 0f), new Vector3(0.06f, 0.5f, 0.16f));
            Box(v, n, t, new Vector3(0f, -0.25f, 0f), new Vector3(0.84f, 0.06f, 0.2f));
            Box(v, n, t, new Vector3(0f, -0.3f, 0f), new Vector3(0.4f, 0.06f, 0.4f));            // turntable
            return Build(v, n, t);
        });

        /// <summary>Wall bracket / pedestal under a searchlight (fixed): arm from the wall (+z) plus the turntable post.</summary>
        public static Mesh SearchMount(float armLength) => Cached("mount" + armLength.ToString("F2"), () =>
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            Box(v, n, t, new Vector3(0f, -0.33f, 0f), new Vector3(0.36f, 0.12f, 0.36f));                 // turntable base
            if (armLength > 0.01f)
            {
                Box(v, n, t, new Vector3(0f, -0.43f, -armLength * 0.5f), new Vector3(0.14f, 0.12f, armLength));   // arm to the wall
                Box(v, n, t, new Vector3(0f, -0.65f, -armLength + 0.04f), new Vector3(0.44f, 0.6f, 0.08f));      // wall plate
                Box(v, n, t, new Vector3(0f, -0.62f, -armLength * 0.5f), new Vector3(0.08f, 0.08f, armLength * 1.05f)); // strut
            }
            else Box(v, n, t, new Vector3(0f, -0.52f, 0f), new Vector3(0.16f, 0.3f, 0.16f));             // roof post
            return Build(v, n, t);
        });

        /// <summary>Floodlight head: flat box, lens on +z (0.46 x 0.3 m) with a small wall bracket behind.</summary>
        public static Mesh FloodHead() => Cached("flood", () =>
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            Box(v, n, t, new Vector3(0f, 0f, -0.06f), new Vector3(0.5f, 0.34f, 0.16f));
            Box(v, n, t, new Vector3(0f, 0f, -0.2f), new Vector3(0.08f, 0.08f, 0.14f));
            Box(v, n, t, new Vector3(0f, 0.19f, 0.03f), new Vector3(0.52f, 0.03f, 0.12f));    // visor
            return Build(v, n, t);
        });

        /// <summary>Beacon base (a short cylinder standing on its mount, dome glow above it).</summary>
        public static Mesh BeaconBase() => Cached("beacon", () =>
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            Box(v, n, t, new Vector3(0f, 0.04f, 0f), new Vector3(0.26f, 0.08f, 0.26f));
            Box(v, n, t, new Vector3(0f, 0.12f, 0f), new Vector3(0.18f, 0.08f, 0.18f));
            return Build(v, n, t);
        });

        /// <summary>Low-poly sphere (bulb / beacon dome glow), radius 1.</summary>
        public static Mesh Sphere() => Cached("sphere", () =>
        {
            const int LAT = 8, LON = 12;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
            for (int a = 0; a <= LAT; a++)
                for (int b = 0; b <= LON; b++)
                {
                    float th = a * Mathf.PI / LAT, ph = b * Mathf.PI * 2f / LON;
                    var p = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    v.Add(p); n.Add(p); uv.Add(new Vector2(b / (float)LON, a / (float)LAT));
                }
            for (int a = 0; a < LAT; a++)
                for (int b = 0; b < LON; b++)
                {
                    int i0 = a * (LON + 1) + b, i1 = i0 + LON + 1;
                    t.AddRange(new[] { i0, i0 + 1, i1, i0 + 1, i1 + 1, i1 });
                }
            var m = new Mesh(); m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        });

        /// <summary>Fallback street lamp post (no prop to clone): 3.2 m tapered pole, arm along +z, head at (0, 3.1, 0.7).</summary>
        public static Mesh LampPost() => Cached("post", () =>
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            Box(v, n, t, new Vector3(0f, 0.1f, 0f), new Vector3(0.34f, 0.2f, 0.34f));
            Box(v, n, t, new Vector3(0f, 1.65f, 0f), new Vector3(0.12f, 3.1f, 0.12f));
            Box(v, n, t, new Vector3(0f, 3.18f, 0.36f), new Vector3(0.08f, 0.08f, 0.8f));
            Box(v, n, t, new Vector3(0f, 3.12f, 0.72f), new Vector3(0.42f, 0.12f, 0.3f));
            return Build(v, n, t);
        });

        // ------------------------------------------------------------------ helpers
        public static GameObject Part(string name, Transform parent, Mesh mesh, Material mat, bool shadows, Vector3 localPos, Quaternion localRot, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos; go.transform.localRotation = localRot; go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = shadows;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            if (!shadows) r.allowOcclusionWhenDynamic = false;
            return go;
        }
    }
}
