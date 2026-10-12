using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Robot antics: the modelled smartphone (replaces the small white phone box). A thin rounded-rectangle slab (aspect 1 : 2.05,
    /// corner radius 12 % of the width, a small edge bevel), a dark graphite body and frame, a black glass front with a bright
    /// emissive screen (a gradient wallpaper with rounded app tiles; a green call screen for calls), a small dark pill at the top
    /// and a square camera bump with three lenses on the back; no logo, no text. Built at its in-game size (about 3x a real phone
    /// for the robot hand) in portrait: top on +Y, screen on +Z, GRIP on the back at 26 % of the height (the fist).
    /// Menu: OpenZ > Antics > 3 Build smartphone (writes Models/Antics/Phone_v2 and points the AnticKit at it).
    /// </summary>
    public static class OpenZPhoneModel
    {
        const string Dir = "Assets/OpenZ/Models/Antics/Phone_v2", KitPath = "Assets/OpenZ/Resources/OpenZ/Antics/AnticKit.asset";
        public const float W = 0.215f, H = 0.44f, T = 0.024f, Bevel = 0.0035f, Border = 0.0085f;
        static float R => W * 0.12f;
        const int Seg = 10;

        [MenuItem("OpenZ/Antics/3 Build smartphone")]
        public static string Build()
        {
            Directory.CreateDirectory(Dir); AssetDatabase.Refresh();
            // ---- textures
            var home = SaveTex(HomeScreen(256, 525), "T_Phone_Home");
            var call = SaveTex(CallScreen(256, 525), "T_Phone_Call");
            // ---- materials
            var body = Lit("M_Phone_Body", new Color(0.115f, 0.12f, 0.13f), 0.7f, 0.6f, null, null, 0f);
            var glass = Lit("M_Phone_Glass", new Color(0.015f, 0.016f, 0.02f), 0.0f, 0.93f, null, null, 0f);
            var bump = Lit("M_Phone_CamBump", new Color(0.16f, 0.165f, 0.175f), 0.75f, 0.66f, null, null, 0f);
            var lens = Lit("M_Phone_Lens", new Color(0.02f, 0.025f, 0.04f), 0.2f, 0.95f, null, null, 0f);
            var scrHome = Lit("M_Phone_ScreenHome", new Color(0.35f, 0.35f, 0.35f), 0f, 0.86f, home, home, 5f);
            var scrCall = Lit("M_Phone_ScreenCall", new Color(0.35f, 0.35f, 0.35f), 0f, 0.86f, call, call, 5f);
            // ---- meshes (one asset with sub-meshes)
            string meshPath = $"{Dir}/ANT_Phone_v2_meshes.asset";
            AssetDatabase.DeleteAsset(meshPath);
            var mBody = Slab("PhoneBody", 0f, 0f, W, H, R, T, Bevel, 0f, true);
            float sw = W - 2f * Border, sh = H - 2f * Border;
            var mScreen = Flat("PhoneScreen", 0f, 0f, sw, sh, R - Border, T * 0.5f + 0.0006f, true);
            var mPill = Flat("PhonePill", 0f, sh * 0.5f - 0.016f, 0.056f, 0.0145f, 0.00725f, T * 0.5f + 0.0012f, false);
            float bs = 0.078f, bx = W * 0.5f - 0.013f - bs * 0.5f, by = H * 0.5f - 0.013f - bs * 0.5f, bt = 0.0055f;
            var mBump = Slab("PhoneCamBump", bx, by, bs, bs, 0.02f, bt, 0.0018f, -(T * 0.5f + bt * 0.5f - 0.0004f), false);
            var lenses = new List<Mesh>();
            float lz = -(T * 0.5f + bt - 0.0004f);
            var lp = new[] { new Vector2(bx + 0.018f, by + 0.018f), new Vector2(bx + 0.018f, by - 0.018f), new Vector2(bx - 0.019f, by) };
            for (int i = 0; i < lp.Length; i++) lenses.Add(Lens("PhoneLens" + i, lp[i], i < 2 ? 0.0145f : 0.0105f, 0.0042f, lz));
            AssetDatabase.CreateAsset(mBody, meshPath);
            foreach (var m in new[] { mScreen, mPill, mBump }) AssetDatabase.AddObjectToAsset(m, meshPath);
            foreach (var m in lenses) AssetDatabase.AddObjectToAsset(m, meshPath);
            // ---- prefab
            var root = new GameObject("ANT_Phone_v2");
            Part(root, "Body", mBody, body, glass);
            Part(root, "Screen", mScreen, scrHome);
            Part(root, "Pill", mPill, glass);
            Part(root, "CamBump", mBump, bump, bump);
            for (int i = 0; i < lenses.Count; i++) Part(root, "Lens_" + i, lenses[i], lens, bump);
            var grip = new GameObject("GRIP").transform; grip.SetParent(root.transform, false); grip.localPosition = new Vector3(0f, (0.26f - 0.5f) * H, -T * 0.5f);
            string pfPath = $"{Dir}/ANT_Phone_v2.prefab";
            var pf = PrefabUtility.SaveAsPrefabAsset(root, pfPath);
            Object.DestroyImmediate(root);
            // ---- the kit
            var kit = AssetDatabase.LoadAssetAtPath<AnticKit>(KitPath);
            string res = "phone v2 built: " + pfPath;
            if (kit)
            {
                kit.phone = pf; kit.phoneOwnMats = true; kit.phoneScreenHome = scrHome; kit.phoneScreenCall = scrCall; kit.phoneSize = new Vector3(W, H, T);
                EditorUtility.SetDirty(kit); res += "; AnticKit.phone -> ANT_Phone_v2";
            }
            else res += "; NO AnticKit";
            AssetDatabase.SaveAssets();
            int tris = 0; foreach (var m in new[] { mBody, mScreen, mPill, mBump }) tris += m.triangles.Length / 3; foreach (var m in lenses) tris += m.triangles.Length / 3;
            res += $"; {tris} tris, {W:F3} x {H:F3} x {T:F3} m (aspect 1:{H / W:F2}, corner {R / W * 100f:F0} % of the width)";
            Debug.Log("[OpenZ Antics] " + res);
            return res;
        }

        [MenuItem("OpenZ/Antics/3b Revert to the old phone box")]
        public static string Revert()
        {
            var kit = AssetDatabase.LoadAssetAtPath<AnticKit>(KitPath); if (!kit) return "no kit";
            kit.phone = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/OpenZ/Models/Antics/ANT_Phone.fbx"); kit.phoneOwnMats = false;
            EditorUtility.SetDirty(kit); AssetDatabase.SaveAssets(); return "kit phone -> ANT_Phone.fbx";
        }

        static void Part(GameObject root, string name, Mesh m, params Material[] mats)
        {
            var go = new GameObject(name); go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            var use = new Material[m.subMeshCount]; for (int i = 0; i < use.Length; i++) use[i] = mats[Mathf.Min(i, mats.Length - 1)];
            mr.sharedMaterials = use;
        }

        // ------------------------------------------------------------------ geometry
        /// <summary>Rounded-rectangle outline (counter-clockwise seen from +Z): points and outward normals.</summary>
        static void Outline(float cx, float cy, float w, float h, float r, List<Vector2> pts, List<Vector2> nrm)
        {
            pts.Clear(); nrm.Clear();
            r = Mathf.Max(0.0005f, Mathf.Min(r, Mathf.Min(w, h) * 0.5f));
            var c = new[] { new Vector2(w * 0.5f - r, h * 0.5f - r), new Vector2(-w * 0.5f + r, h * 0.5f - r), new Vector2(-w * 0.5f + r, -h * 0.5f + r), new Vector2(w * 0.5f - r, -h * 0.5f + r) };
            for (int q = 0; q < 4; q++)
                for (int i = 0; i <= Seg; i++)
                {
                    float a = (q * 90f + i * 90f / Seg) * Mathf.Deg2Rad;
                    var n = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    pts.Add(new Vector2(cx, cy) + c[q] + n * r); nrm.Add(n);
                }
        }

        /// <summary>A bevelled rounded slab centred at (cx, cy, zc): submesh 0 = sides, bevels, back; submesh 1 = the front cap.</summary>
        static Mesh Slab(string name, float cx, float cy, float w, float h, float r, float t, float b, float zc, bool frontSub)
        {
            var po = new List<Vector2>(); var no = new List<Vector2>(); var pi = new List<Vector2>(); var ni = new List<Vector2>();
            Outline(cx, cy, w, h, r, po, no); Outline(cx, cy, w - 2f * b, h - 2f * b, r - b, pi, ni);
            int n = po.Count;
            var v = new List<Vector3>(); var nn = new List<Vector3>(); var uv = new List<Vector2>();
            var side = new List<int>(); var front = new List<int>();
            float zf = zc + t * 0.5f, zb = zc - t * 0.5f;
            // strips: (ring A, zA, normal z) -> (ring B, zB)
            void Strip(List<Vector2> a, float za, List<Vector2> bb, float zb2, float nz)
            {
                int s0 = v.Count;
                for (int i = 0; i < n; i++)
                {
                    var nm = new Vector3(no[i].x, no[i].y, nz).normalized;
                    v.Add(new Vector3(a[i].x, a[i].y, za)); nn.Add(nm); uv.Add(new Vector2(i / (float)n, 0f));
                    v.Add(new Vector3(bb[i].x, bb[i].y, zb2)); nn.Add(nm); uv.Add(new Vector2(i / (float)n, 1f));
                }
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n; int a0 = s0 + i * 2, a1 = s0 + j * 2;
                    side.AddRange(new[] { a0, a1, a0 + 1, a1, a1 + 1, a0 + 1 });
                }
            }
            Strip(pi, zf, po, zf - b, 0.75f);
            Strip(po, zf - b, po, zb + b, 0f);
            Strip(po, zb + b, pi, zb, -0.75f);
            // caps (convex: a fan from the centre)
            void Cap(List<Vector2> ring, float z, bool up, List<int> tris)
            {
                int c0 = v.Count; v.Add(new Vector3(cx, cy, z)); nn.Add(up ? Vector3.forward : Vector3.back); uv.Add(new Vector2(0.5f, 0.5f));
                for (int i = 0; i < n; i++) { v.Add(new Vector3(ring[i].x, ring[i].y, z)); nn.Add(up ? Vector3.forward : Vector3.back); uv.Add(new Vector2((ring[i].x - cx) / w + 0.5f, (ring[i].y - cy) / h + 0.5f)); }
                for (int i = 0; i < n; i++)
                {
                    int a = c0 + 1 + i, bq = c0 + 1 + (i + 1) % n;
                    if (up) tris.AddRange(new[] { c0, bq, a }); else tris.AddRange(new[] { c0, a, bq });
                }
            }
            Cap(pi, zf, true, frontSub ? front : side);
            Cap(pi, zb, false, side);
            // Unity is left-handed: flip the winding so the faces point outward
            Flip(side); Flip(front);
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetNormals(nn); m.SetUVs(0, uv);
            m.subMeshCount = 2; m.SetTriangles(side, 0); m.SetTriangles(front, 1);
            m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }

        /// <summary>A flat rounded rectangle facing +Z at height z, UV 0..1 across it.</summary>
        static Mesh Flat(string name, float cx, float cy, float w, float h, float r, float z, bool uvRect)
        {
            var p = new List<Vector2>(); var nr = new List<Vector2>(); Outline(cx, cy, w, h, r, p, nr);
            var v = new List<Vector3> { new Vector3(cx, cy, z) }; var uv = new List<Vector2> { new Vector2(0.5f, 0.5f) }; var tr = new List<int>();
            foreach (var q in p) { v.Add(new Vector3(q.x, q.y, z)); uv.Add(new Vector2((q.x - cx) / w + 0.5f, (q.y - cy) / h + 0.5f)); }
            for (int i = 0; i < p.Count; i++) tr.AddRange(new[] { 0, 1 + (i + 1) % p.Count, 1 + i });
            Flip(tr);
            var m = new Mesh { name = name }; m.SetVertices(v); m.SetUVs(0, uv);
            var nn = new List<Vector3>(); foreach (var _ in v) nn.Add(Vector3.forward); m.SetNormals(nn);
            m.SetTriangles(tr, 0); m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }

        /// <summary>A camera lens on the back (facing -Z): glass disc (submesh 0) and a short metal barrel (submesh 1).</summary>
        static Mesh Lens(string name, Vector2 c, float rad, float depth, float zBase)
        {
            int s = 24; var v = new List<Vector3>(); var nn = new List<Vector3>(); var glass = new List<int>(); var ring = new List<int>();
            float zt = zBase - depth;
            for (int i = 0; i < s; i++)
            {
                float a = i * Mathf.PI * 2f / s; var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                v.Add(new Vector3(c.x, c.y, zBase) + d * rad); nn.Add(d);
                v.Add(new Vector3(c.x, c.y, zt) + d * rad); nn.Add(d);
            }
            for (int i = 0; i < s; i++) { int j = (i + 1) % s; int a0 = i * 2, a1 = j * 2; ring.AddRange(new[] { a0, a0 + 1, a1, a1, a0 + 1, a1 + 1 }); }
            int c0 = v.Count; v.Add(new Vector3(c.x, c.y, zt)); nn.Add(Vector3.back);
            int r0 = v.Count;
            for (int i = 0; i < s; i++) { float a = i * Mathf.PI * 2f / s; v.Add(new Vector3(c.x + Mathf.Cos(a) * rad * 0.82f, c.y + Mathf.Sin(a) * rad * 0.82f, zt - 0.0003f)); nn.Add(Vector3.back); }
            for (int i = 0; i < s; i++) glass.AddRange(new[] { c0, r0 + (i + 1) % s, r0 + i });
            // the barrel's top rim (outside the glass)
            int t0 = v.Count;
            for (int i = 0; i < s; i++) { float a = i * Mathf.PI * 2f / s; v.Add(new Vector3(c.x + Mathf.Cos(a) * rad, c.y + Mathf.Sin(a) * rad, zt)); nn.Add(Vector3.back); v.Add(new Vector3(c.x + Mathf.Cos(a) * rad * 0.82f, c.y + Mathf.Sin(a) * rad * 0.82f, zt)); nn.Add(Vector3.back); }
            for (int i = 0; i < s; i++) { int j = (i + 1) % s; int a0 = t0 + i * 2, a1 = t0 + j * 2; ring.AddRange(new[] { a0, a0 + 1, a1, a1, a0 + 1, a1 + 1 }); }
            var m = new Mesh { name = name }; m.SetVertices(v); m.SetNormals(nn);
            m.subMeshCount = 2; m.SetTriangles(glass, 0); m.SetTriangles(ring, 1);
            m.RecalculateBounds();
            return m;
        }

        static void Flip(List<int> t) { for (int i = 0; i + 2 < t.Count; i += 3) { int a = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = a; } }

        // ------------------------------------------------------------------ materials + textures
        static Material Lit(string name, Color baseCol, float metal, float smooth, Texture2D baseMap, Texture2D emisMap, float emis)
        {
            string p = $"{Dir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); } else m.shader = sh;
            m.SetColor("_BaseColor", baseCol); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
            if (baseMap) m.SetTexture("_BaseMap", baseMap);
            if (emisMap && emis > 0f)
            {
                m.EnableKeyword("_EMISSION"); m.SetTexture("_EmissionMap", emisMap); m.SetColor("_EmissionColor", Color.white * emis);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            else { m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D SaveTex(Texture2D t, string name)
        {
            string p = $"{Dir}/{name}.png";
            File.WriteAllBytes(p, t.EncodeToPNG()); Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
            var ti = AssetImporter.GetAtPath(p) as TextureImporter;
            if (ti)
            {
                ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.anisoLevel = 8; ti.wrapMode = TextureWrapMode.Clamp;
                ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.alphaSource = TextureImporterAlphaSource.None;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }

        static float RoundRectSdf(Vector2 p, Vector2 c, Vector2 half, float r)
        {
            var q = new Vector2(Mathf.Abs(p.x - c.x) - half.x + r, Mathf.Abs(p.y - c.y) - half.y + r);
            var mq = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f));
            return mq.magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
        }

        static void Blend(Color[] px, int w, int x, int y, Color c, float a)
        {
            int i = y * w + x; px[i] = Color.Lerp(px[i], c, Mathf.Clamp01(a));
        }

        /// <summary>The home screen: a warm-to-cool gradient wallpaper, a 4 x 5 grid of rounded colourful app tiles and a dock of 4 on a
        /// light glass bar; no text.</summary>
        static Texture2D HomeScreen(int w, int h)
        {
            var px = new Color[w * h];
            Color top = new Color(0.36f, 0.22f, 0.86f), mid = new Color(0.12f, 0.55f, 0.95f), bot = new Color(0.18f, 0.92f, 0.82f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float v = y / (float)(h - 1); float d = (x / (float)w - 0.5f) * 0.12f;
                    var c = v > 0.5f ? Color.Lerp(mid, top, (v - 0.5f) * 2f + d) : Color.Lerp(bot, mid, v * 2f - d);
                    px[y * w + x] = c;
                }
            var cols = new[] { new Color(1f, 0.32f, 0.3f), new Color(1f, 0.62f, 0.15f), new Color(1f, 0.86f, 0.2f), new Color(0.3f, 0.88f, 0.38f),
                               new Color(0.25f, 0.62f, 1f), new Color(0.7f, 0.4f, 1f), new Color(1f, 0.42f, 0.7f), new Color(0.2f, 0.85f, 0.85f),
                               new Color(0.98f, 0.98f, 0.98f), new Color(1f, 0.5f, 0.35f) };
            float tile = w * 0.165f, gap = (w - 4f * tile) / 5f, rad = tile * 0.26f;
            int k = 0;
            for (int row = 0; row < 5; row++)
                for (int col = 0; col < 4; col++)
                {
                    var c = new Vector2(gap + tile * 0.5f + col * (tile + gap), h - h * 0.11f - tile * 0.5f - row * (tile + gap * 1.35f));
                    Tile(px, w, h, c, tile, rad, cols[(k++ * 7 + row) % cols.Length]);
                }
            // the dock: a light glass bar with four tiles
            var dockC = new Vector2(w * 0.5f, h * 0.075f);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    float sd = RoundRectSdf(new Vector2(x, y), dockC, new Vector2(w * 0.45f, tile * 0.68f), tile * 0.4f);
                    if (sd < 1f) Blend(px, w, x, y, Color.white, 0.28f * Mathf.Clamp01(1f - sd));
                }
            for (int col = 0; col < 4; col++) Tile(px, w, h, new Vector2(gap + tile * 0.5f + col * (tile + gap), dockC.y), tile, rad, cols[(col * 3 + 1) % cols.Length]);
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true); t.SetPixels(px); t.Apply(); return t;
        }

        static void Tile(Color[] px, int w, int h, Vector2 c, float size, float rad, Color col)
        {
            int x0 = Mathf.Max(0, (int)(c.x - size)), x1 = Mathf.Min(w - 1, (int)(c.x + size)), y0 = Mathf.Max(0, (int)(c.y - size)), y1 = Mathf.Min(h - 1, (int)(c.y + size));
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x, y);
                    float sd = RoundRectSdf(p, c, Vector2.one * size * 0.5f, rad);
                    if (sd < 1f)
                    {
                        float shade = 0.9f + 0.2f * ((y - c.y) / size);                                   // a soft top-lit gloss
                        Blend(px, w, x, y, col * shade, Mathf.Clamp01(1f - sd));
                        // a simple white glyph: a circle in the middle (no text)
                        float g = (p - c).magnitude - size * 0.17f;
                        if (g < 1f && sd < -2f) Blend(px, w, x, y, Color.white, 0.85f * Mathf.Clamp01(1f - g));
                    }
                }
        }

        /// <summary>The call screen: a green gradient, a round contact picture with a simple robot-head silhouette, a pulsing ring and the
        /// round call buttons (red end call); no text.</summary>
        static Texture2D CallScreen(int w, int h)
        {
            var px = new Color[w * h];
            Color top = new Color(0.05f, 0.42f, 0.2f), bot = new Color(0.25f, 0.9f, 0.45f);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = Color.Lerp(bot, top, y / (float)(h - 1));
            var c = new Vector2(w * 0.5f, h * 0.66f); float R0 = w * 0.25f;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    var p = new Vector2(x, y); float d = (p - c).magnitude;
                    float ring = Mathf.Abs(d - R0 * 1.3f) - 3f; if (ring < 1f) Blend(px, w, x, y, Color.white, 0.35f * Mathf.Clamp01(1f - ring));
                    float ring2 = Mathf.Abs(d - R0 * 1.6f) - 2f; if (ring2 < 1f) Blend(px, w, x, y, Color.white, 0.18f * Mathf.Clamp01(1f - ring2));
                    if (d < R0 + 1f) Blend(px, w, x, y, new Color(0.92f, 0.97f, 0.93f), Mathf.Clamp01(R0 + 1f - d));
                    // silhouette: a rounded head and shoulders
                    float head = RoundRectSdf(p, c + new Vector2(0f, R0 * 0.18f), new Vector2(R0 * 0.36f, R0 * 0.3f), R0 * 0.14f);
                    float sh = RoundRectSdf(p, c + new Vector2(0f, -R0 * 0.62f), new Vector2(R0 * 0.62f, R0 * 0.3f), R0 * 0.28f);
                    float sil = Mathf.Min(head, sh);
                    if (sil < 1f && d < R0) Blend(px, w, x, y, new Color(0.1f, 0.45f, 0.22f), Mathf.Clamp01(1f - sil));
                }
            void Btn(Vector2 bc, float br, Color col)
            {
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                    {
                        float d = (new Vector2(x, y) - bc).magnitude - br;
                        if (d < 1f) Blend(px, w, x, y, col, Mathf.Clamp01(1f - d));
                    }
            }
            Btn(new Vector2(w * 0.5f, h * 0.12f), w * 0.11f, new Color(1f, 0.2f, 0.18f));
            Btn(new Vector2(w * 0.22f, h * 0.3f), w * 0.075f, new Color(1f, 1f, 1f, 1f) * 0.85f);
            Btn(new Vector2(w * 0.5f, h * 0.3f), w * 0.075f, new Color(1f, 1f, 1f, 1f) * 0.85f);
            Btn(new Vector2(w * 0.78f, h * 0.3f), w * 0.075f, new Color(1f, 1f, 1f, 1f) * 0.85f);
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true); t.SetPixels(px); t.Apply(); return t;
        }
    }
}
