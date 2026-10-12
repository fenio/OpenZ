// OpenZ (GPL-3.0)
//  - the red cap is a rounded lathe cap with a bold white NUKE stencil on its top (four block letters, readable in the insert),
//    sunk in three layered black rubber bezel rings over a yellow / black hazard plate (like the reference);
//  - the console is a control desk: a bevelled gunmetal housing with an inset top plate, panel seams and screws, a hazard strip
//    along the front, three small round push buttons (white / yellow), three toggle switches (one hinged red safety cover flipped
//    open = armed, one closed), a tilted instrument riser with a backlit gauge (the needle swings over on the hit) and four
//    small indicator lamps (one blinks red; CALM SCREEN = steady), cables out of the back down to the floor.
// The cap keeps its size, rest height (CapTop) and travel, so the hammer fist lands exactly as before. DeskV2 = false = the old box.
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    public sealed partial class ZodStage
    {
        /// <summary></summary>
        public static bool DeskV2 = true;
        Transform deskNeedle; Material deskBlink; float deskBlinkBase = 3f;

        static Material Lit2(Color32 c, float smooth, float metal) => BronzeLit(c, smooth, metal);    // URP Lit, both faces

        static readonly Dictionary<string, Mesh> lathes = new Dictionary<string, Mesh>();

        /// <summary>A revolved mesh from an (r, y) profile (r = radius, outer -> inner); smooth normals where neighbouring bands
        /// meet at less than 60 deg, hard edges elsewhere.</summary>
        static Mesh Lathe(string key, Vector2[] prof, int seg = 56)
        {
            if (lathes.TryGetValue(key, out var cached) && cached) return cached;
            int nb = prof.Length - 1;
            var bn = new Vector2[nb];
            for (int i = 0; i < nb; i++) { var d = prof[i + 1] - prof[i]; bn[i] = d.sqrMagnitude < 1e-12f ? Vector2.up : new Vector2(d.y, -d.x).normalized; }
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i < nb; i++)
            {
                Vector2 a = prof[i], b = prof[i + 1];
                if ((b - a).sqrMagnitude < 1e-12f) continue;
                Vector2 na = i > 0 && Vector2.Dot(bn[i - 1], bn[i]) > 0.5f ? (bn[i - 1] + bn[i]).normalized : bn[i];
                Vector2 nbn = i < nb - 1 && Vector2.Dot(bn[i + 1], bn[i]) > 0.5f ? (bn[i + 1] + bn[i]).normalized : bn[i];
                int b0 = v.Count;
                for (int j = 0; j <= seg; j++)
                {
                    float ph = j / (float)seg * Mathf.PI * 2f;
                    var dir = new Vector3(Mathf.Cos(ph), 0f, Mathf.Sin(ph));
                    v.Add(dir * a.x + Vector3.up * a.y); n.Add((dir * na.x + Vector3.up * na.y).normalized);
                    v.Add(dir * b.x + Vector3.up * b.y); n.Add((dir * nbn.x + Vector3.up * nbn.y).normalized);
                }
                for (int j = 0; j < seg; j++)
                {
                    int p00 = b0 + j * 2, p10 = p00 + 1, p01 = p00 + 2, p11 = p00 + 3;
                    t.Add(p10); t.Add(p11); t.Add(p01); t.Add(p10); t.Add(p01); t.Add(p00);
                }
            }
            var m = new Mesh { name = "OZ_Lathe_" + key };
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds();
            lathes[key] = m;
            return m;
        }

        Renderer MeshPart(string name, Transform parent, Vector3 pos, Mesh mesh, Material m, bool cast = true)
        {
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetParent(parent, false); go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
            return Dress(go, m, cast);
        }

        Renderer RBox(string name, Transform parent, Vector3 pos, Vector3 size, float r, Material m, bool cast = true) => MeshPart(name, parent, pos, RoundedBox(size, r), m, cast);

        static Texture2D hazardTex;
        static Texture2D HazardTex()
        {
            if (hazardTex) return hazardTex;
            const int S = 64;
            hazardTex = new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8, hideFlags = HideFlags.DontSave, name = "OZ_Hazard" };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float p = ((x + y) % 16) / 16f;                                                   // 4 stripe pairs per tile, 45 deg
                    float k = Mathf.Clamp01((Mathf.Abs(p - 0.5f) - 0.23f) * 12f + 0.5f);              // soft edge (no stair steps)
                    px[y * S + x] = Color32.Lerp(new Color32(226, 176, 34, 255), new Color32(24, 24, 24, 255), k);
                }
            hazardTex.SetPixels32(px); hazardTex.Apply(true, true);
            return hazardTex;
        }

        static Material Hazard(Vector2 tiling)
        {
            var m = Lit(new Color32(255, 255, 255, 255), 0.35f, 0.05f);
            if (!m) return null;
            m.SetTexture("_BaseMap", HazardTex()); m.SetTextureScale("_BaseMap", tiling);
            if (m.HasProperty("_MainTex")) { m.SetTexture("_MainTex", HazardTex()); m.SetTextureScale("_MainTex", tiling); }
            return m;
        }

        /// <summary>Builds the desk, the bezel and the NUKE cap under buttonRoot (buttonRoot frame: +Z toward the camera, +X his right
        /// = screen left, y 0 = the old console top + 3 cm); sets buttonCap to the cap group.</summary>
        readonly System.Collections.Generic.List<Renderer> bezelRs = new System.Collections.Generic.List<Renderer>();
        public int LidCount { get; private set; }
        void BuildDesk(Material gm, Material dark)
        {
            var panel = Lit(new Color32(66, 72, 68, 255), 0.45f, 0.5f);
            var seamM = Lit(new Color32(14, 15, 15, 255), 0.2f, 0.1f);
            var steel = Lit(new Color32(128, 132, 130, 255), 0.6f, 0.85f);
            var steelDk = Lit(new Color32(62, 66, 66, 255), 0.5f, 0.75f);
            var rubA = Lit2(new Color32(30, 30, 32, 255), 0.32f, 0f);
            var rubB = Lit2(new Color32(46, 46, 49, 255), 0.36f, 0f);
            var rubC = Lit2(new Color32(17, 17, 19, 255), 0.30f, 0f);
            var cable = Lit(new Color32(19, 19, 20, 255), 0.55f, 0f);
            var white = Lit2(new Color32(226, 224, 216, 255), 0.55f, 0f);
            var yellowB = Lit2(new Color32(232, 186, 48, 255), 0.55f, 0f);
            var coverM = Lit(new Color32(196, 42, 28, 255), 0.62f, 0.05f);
            // ---- housing, top plate, seams, screws
            RBox("DeskHousing", buttonRoot, new Vector3(0f, -0.080f, -0.020f), new Vector3(0.52f, 0.10f, 0.32f), 0.016f, gm);
            RBox("DeskTopPlate", buttonRoot, new Vector3(0f, -0.027f, -0.020f), new Vector3(0.48f, 0.006f, 0.28f), 0.0025f, panel);
            Part(PrimitiveType.Cube, "SeamL", buttonRoot, new Vector3(0.125f, -0.0236f, -0.020f), new Vector3(0.0025f, 0.0012f, 0.28f), seamM);
            Part(PrimitiveType.Cube, "SeamR", buttonRoot, new Vector3(-0.125f, -0.0236f, -0.020f), new Vector3(0.0025f, 0.0012f, 0.28f), seamM);
            Part(PrimitiveType.Cube, "SeamLx", buttonRoot, new Vector3(0.1825f, -0.0236f, -0.002f), new Vector3(0.115f, 0.0012f, 0.0025f), seamM);
            Part(PrimitiveType.Cube, "SeamRx", buttonRoot, new Vector3(-0.1825f, -0.0236f, -0.040f), new Vector3(0.115f, 0.0012f, 0.0025f), seamM);
            foreach (var sx in new[] { -0.228f, 0.228f })
                foreach (var sz in new[] { -0.148f, 0.108f })
                    Part(PrimitiveType.Cylinder, "Screw", buttonRoot, new Vector3(sx, -0.0236f, sz), new Vector3(0.008f, 0.001f, 0.008f), steel);
            // ---- hazard plate under the bezel, hazard strip along the front
            Part(PrimitiveType.Cube, "HazardPlate", buttonRoot, new Vector3(0f, -0.0228f, 0f), new Vector3(0.23f, 0.0024f, 0.23f), Hazard(new Vector2(2.9f, 2.9f)));
            Part(PrimitiveType.Cube, "HazardStrip", buttonRoot, new Vector3(0f, -0.056f, 0.1408f), new Vector3(0.46f, 0.014f, 0.002f), Hazard(new Vector2(5.75f, 0.175f)));
            // ---- three layered black rubber bezel rings + the dark well under the cap
            bezelRs.Add(MeshPart("BezelOuter", buttonRoot, Vector3.zero, Lathe("bezel_a", new[] { new Vector2(0.099f, -0.0216f), new Vector2(0.099f, -0.0150f), new Vector2(0.0975f, -0.0125f), new Vector2(0.0950f, -0.0115f), new Vector2(0.0870f, -0.0115f) }), rubA));
            bezelRs.Add(MeshPart("BezelMid", buttonRoot, Vector3.zero, Lathe("bezel_b", new[] { new Vector2(0.0868f, -0.0216f), new Vector2(0.0868f, -0.0070f), new Vector2(0.0855f, -0.0050f), new Vector2(0.0830f, -0.0042f), new Vector2(0.0765f, -0.0042f) }), rubB));
            bezelRs.Add(MeshPart("BezelInner", buttonRoot, Vector3.zero, Lathe("bezel_c", new[] { new Vector2(0.0763f, -0.0216f), new Vector2(0.0763f, 0.0010f), new Vector2(0.0748f, 0.0032f), new Vector2(0.0725f, 0.0040f), new Vector2(0.0665f, 0.0040f), new Vector2(0.0650f, 0.0025f), new Vector2(0.0645f, -0.0460f) }), rubC));
            bezelRs.Add(Part(PrimitiveType.Cylinder, "BezelWell", buttonRoot, new Vector3(0f, -0.0213f, 0f), new Vector3(0.129f, 0.0004f, 0.129f), rubC));
            // ---- the cap (same size, rest height and travel as v1) with the NUKE stencil
            var capG = new GameObject("Cap") { layer = Layer };
            capG.transform.SetParent(buttonRoot, false); capG.transform.localPosition = new Vector3(0f, 0.005f, 0f);
            buttonCap = capG.transform;
            MeshPart("CapBody", buttonCap, Vector3.zero, Lathe("cap", new[] { new Vector2(0.060f, -0.044f), new Vector2(0.060f, 0.019f), new Vector2(0.0596f, 0.0228f), new Vector2(0.0583f, 0.0256f), new Vector2(0.0560f, 0.0273f), new Vector2(0.0525f, 0.0280f), new Vector2(0f, 0.0280f) }), capMat);
            BuildNukeStencil(buttonCap, 0.0284f);
            // ---- small round push buttons (screen right)
            var bez = Lathe("pb_bezel", new[] { new Vector2(0.021f, -0.024f), new Vector2(0.021f, -0.018f), new Vector2(0.0195f, -0.016f), new Vector2(0.0165f, -0.016f), new Vector2(0.0165f, -0.030f) });
            var pcap = Lathe("pb_cap", new[] { new Vector2(0.0158f, -0.030f), new Vector2(0.0158f, -0.012f), new Vector2(0.0150f, -0.0095f), new Vector2(0.0130f, -0.0082f), new Vector2(0.0080f, -0.0076f), new Vector2(0f, -0.0074f) });
            foreach (var (pos, col) in new[] { (new Vector3(-0.150f, 0f, 0.075f), white), (new Vector3(-0.196f, 0f, 0.075f), yellowB), (new Vector3(-0.173f, 0f, 0.020f), white) })
            {
                bezelRs.Add(MeshPart("PushBezel", buttonRoot, pos, bez, rubC));
                MeshPart("PushCap", buttonRoot, pos, pcap, col);
            }
            // ---- toggle switches (screen left): the first with its red safety cover flipped open (armed), the last covered
            for (int i = 0; i < 3; i++)
            {
                var p = new Vector3(0.140f + i * 0.032f, 0f, 0.035f);
                RBox("ToggleBase", buttonRoot, p + new Vector3(0f, -0.0225f, 0f), new Vector3(0.024f, 0.003f, 0.034f), 0.001f, steelDk);
                Part(PrimitiveType.Cylinder, "ToggleNut", buttonRoot, p + new Vector3(0f, -0.0195f, 0f), new Vector3(0.011f, 0.0025f, 0.011f), steel);
                var lever = new GameObject("ToggleLever") { layer = Layer };
                lever.transform.SetParent(buttonRoot, false); lever.transform.localPosition = p + new Vector3(0f, -0.018f, 0f);
                lever.transform.localRotation = Quaternion.Euler(i == 0 ? 22f : -24f, 0f, 0f);                // the armed one is thrown
                RBox("Bat", lever.transform, new Vector3(0f, 0.012f, 0f), new Vector3(0.0042f, 0.024f, 0.0042f), 0.0018f, steel);
                var tip = Part(PrimitiveType.Sphere, "BatTip", lever.transform, new Vector3(0f, 0.0245f, 0f), new Vector3(0.0066f, 0.0066f, 0.0066f), steel);
                if (i == 1) continue;
                var hinge = new GameObject("CoverHinge") { layer = Layer };
                hinge.transform.SetParent(buttonRoot, false); hinge.transform.localPosition = p + new Vector3(0f, -0.021f, -0.019f);
                hinge.transform.localRotation = Quaternion.Euler(i == 0 ? -100f : 0f, 0f, 0f);
                // back and the bottom) with a finger tab, swinging on the hinge pin: open over the armed toggle, closed over the last one
                var ht = hinge.transform; const float lw = 0.0016f;
                RBox("LidTop", ht, new Vector3(0f, 0.034f - lw * 0.5f, 0.020f), new Vector3(0.028f, lw, 0.040f), 0.0006f, coverM);
                RBox("LidSideL", ht, new Vector3(-0.014f + lw * 0.5f, 0.017f, 0.020f), new Vector3(lw, 0.034f, 0.040f), 0.0006f, coverM);
                RBox("LidSideR", ht, new Vector3(0.014f - lw * 0.5f, 0.017f, 0.020f), new Vector3(lw, 0.034f, 0.040f), 0.0006f, coverM);
                RBox("LidFront", ht, new Vector3(0f, 0.017f, 0.040f - lw * 0.5f), new Vector3(0.028f, 0.034f, lw), 0.0006f, coverM);
                RBox("LidTab", ht, new Vector3(0f, 0.031f, 0.0418f), new Vector3(0.012f, 0.0035f, 0.0036f), 0.001f, coverM);
                LidCount++;
                var pin = Part(PrimitiveType.Cylinder, "HingePin", buttonRoot, p + new Vector3(0f, -0.021f, -0.019f), new Vector3(0.004f, 0.016f, 0.004f), steel);
                pin.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
            // ---- the instrument riser at the back, tilted toward the camera: gauge + indicator lamps
            var riser = new GameObject("Riser") { layer = Layer };
            riser.transform.SetParent(buttonRoot, false); riser.transform.localPosition = new Vector3(0f, -0.031f, -0.150f);
            riser.transform.localRotation = Quaternion.Euler(-16f, 0f, 0f);
            var rt = riser.transform; const float fz = 0.023f;
            RBox("RiserBody", rt, new Vector3(0f, 0.036f, 0f), new Vector3(0.46f, 0.072f, 0.046f), 0.008f, gm);
            Part(PrimitiveType.Cube, "RiserSeam", rt, new Vector3(0f, 0.0725f, 0f), new Vector3(0.43f, 0.0012f, 0.030f), seamM);
            var g0 = new Vector3(0.115f, 0.036f, fz);
            Part(PrimitiveType.Cylinder, "GaugeBezel", rt, g0 + new Vector3(0f, 0f, 0.003f), new Vector3(0.050f, 0.004f, 0.050f), steelDk).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Part(PrimitiveType.Cylinder, "GaugeFace", rt, g0 + new Vector3(0f, 0f, 0.0072f), new Vector3(0.042f, 0.0006f, 0.042f), Glow(new Color32(222, 214, 190, 255), new Color(1f, 0.9f, 0.7f), 0.35f)).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var tickM = Lit(new Color32(20, 20, 20, 255), 0.2f, 0f); var redTick = Lit(new Color32(200, 30, 22, 255), 0.3f, 0f);
            for (int k = 0; k < 9; k++)
            {
                float a = Mathf.Lerp(-120f, 120f, k / 8f);
                var q = Quaternion.Euler(0f, 0f, a);
                var tk = Part(PrimitiveType.Cube, "GaugeTick", rt, g0 + new Vector3(0f, 0f, 0.0082f) + q * new Vector3(0f, 0.0165f, 0f), new Vector3(0.0013f, 0.0048f, 0.0004f), k >= 7 ? redTick : tickM);
                tk.transform.localRotation = q;
            }
            var np = new GameObject("GaugeNeedle") { layer = Layer };
            np.transform.SetParent(rt, false); np.transform.localPosition = g0 + new Vector3(0f, 0f, 0.0086f);
            deskNeedle = np.transform;
            Part(PrimitiveType.Cube, "Needle", deskNeedle, new Vector3(0f, 0.0075f, 0f), new Vector3(0.0016f, 0.017f, 0.0006f), Glow(new Color32(210, 30, 20, 255), new Color(1f, 0.1f, 0.05f), 0.6f));
            Part(PrimitiveType.Cylinder, "NeedleHub", deskNeedle, new Vector3(0f, 0f, 0.0004f), new Vector3(0.0045f, 0.0008f, 0.0045f), tickM).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            deskNeedle.localRotation = Quaternion.Euler(0f, 0f, -105f);
            var lampCols = new[] { (new Color32(40, 200, 80, 255), new Color(0.15f, 1f, 0.3f)), (new Color32(250, 170, 30, 255), new Color(1f, 0.6f, 0.1f)), (new Color32(240, 40, 30, 255), new Color(1f, 0.12f, 0.06f)), (new Color32(240, 40, 30, 255), new Color(1f, 0.12f, 0.06f)) };
            for (int k = 0; k < 4; k++)
            {
                var lp = new Vector3(-0.060f - k * 0.026f, 0.044f, fz);
                Part(PrimitiveType.Cylinder, "LampRing", rt, lp + new Vector3(0f, 0f, 0.001f), new Vector3(0.013f, 0.0012f, 0.013f), steelDk).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var lm = Glow(lampCols[k].Item1, lampCols[k].Item2, 2.2f);
                Part(PrimitiveType.Sphere, "IndicatorLamp", rt, lp + new Vector3(0f, 0f, 0.0015f), new Vector3(0.009f, 0.009f, 0.007f), lm);
                if (k == 3) { deskBlink = lm; deskBlinkBase = 3f; }
            }
            Part(PrimitiveType.Cube, "LampLabel", rt, new Vector3(-0.099f, 0.024f, fz + 0.0004f), new Vector3(0.100f, 0.007f, 0.0008f), Glow(new Color32(30, 26, 12, 255), new Color(1f, 0.6f, 0.2f), 0.25f));
            // ---- cables out of the back, down to the floor
            Cable("CableA", new[] { new Vector3(0.170f, -0.060f, -0.176f), new Vector3(0.182f, -0.070f, -0.230f), new Vector3(0.215f, -0.140f, -0.270f), new Vector3(0.240f, -0.300f, -0.280f), new Vector3(0.250f, -0.640f, -0.270f) }, 0.013f, cable);
            Cable("CableB", new[] { new Vector3(-0.120f, -0.065f, -0.176f), new Vector3(-0.130f, -0.080f, -0.240f), new Vector3(-0.170f, -0.160f, -0.250f), new Vector3(-0.200f, -0.350f, -0.220f), new Vector3(-0.210f, -0.660f, -0.200f) }, 0.013f, cable);
            Cable("CableC", new[] { new Vector3(0.050f, -0.050f, -0.176f), new Vector3(0.042f, -0.060f, -0.220f), new Vector3(0.020f, -0.180f, -0.250f), new Vector3(0.000f, -0.520f, -0.240f) }, 0.009f, cable);
            Debug.Log("[ZodCameo] nuke desk v2 built: " + buttonRoot.GetComponentsInChildren<Renderer>(true).Length + " parts (NUKE cap, 3 bezel rings, 3 push buttons, 3 toggles / 2 thin hinged safety lids, gauge, 4 lamps, 3 cables)");
        }

        /// <summary>Bold white block letters N U K E lying on the cap top (y = top + a hair), reading left to right from the camera.</summary>
        void BuildNukeStencil(Transform cap, float y)
        {
            var ink = Glow(new Color32(240, 238, 230, 255), new Color(1f, 0.97f, 0.92f), 0.28f);
            var root = new GameObject("NukeStencil") { layer = Layer };
            root.transform.SetParent(cap, false); root.transform.localPosition = new Vector3(0f, y, 0f);
            const float h = 0.026f, w = 0.0165f, sw = 0.0052f, th = 0.0012f;
            float W = w * 0.5f, H = h * 0.5f;
            var letters = new (float cx, Vector2[] s)[]
            {
                (0.0345f, new[] { new Vector2(-W, -H), new Vector2(-W, H), new Vector2(W, -H), new Vector2(W, H), new Vector2(-W, H), new Vector2(W, -H) }),                                   // N
                (0.0115f, new[] { new Vector2(-W, H), new Vector2(-W, -H), new Vector2(W, H), new Vector2(W, -H), new Vector2(-W, -H), new Vector2(W, -H) }),                                   // U
                (-0.0115f, new[] { new Vector2(-W, -H), new Vector2(-W, H), new Vector2(-W + 0.002f, -0.001f), new Vector2(W, H), new Vector2(-W + 0.004f, 0.002f), new Vector2(W, -H) }),     // K
                (-0.0345f, new[] { new Vector2(-W, -H), new Vector2(-W, H), new Vector2(-W, H), new Vector2(W, H), new Vector2(-W, 0f), new Vector2(W - 0.003f, 0f), new Vector2(-W, -H), new Vector2(W, -H) }),   // E
            };
            int n = 0;
            foreach (var (cx, s) in letters)
                for (int i = 0; i + 1 < s.Length; i += 2)
                {
                    // letter (u right, v up on screen) -> cap frame: screen right = -X, screen up = away from the camera = -Z
                    var a = new Vector3(cx - s[i].x, 0f, -s[i].y); var b = new Vector3(cx - s[i + 1].x, 0f, -s[i + 1].y);
                    var d = b - a; float len = d.magnitude;
                    var r = MeshPart("Stroke", root.transform, (a + b) * 0.5f, RoundedBox(new Vector3(len + sw, th, sw), 0.0005f), ink, false);
                    r.transform.localRotation = Quaternion.Euler(0f, Mathf.Atan2(-d.z, d.x) * Mathf.Rad2Deg, 0f);                      // flat on the cap (yaw only)
                    n++;
                }
        }

        void Cable(string name, Vector3[] pts, float d, Material m)
        {
            var root = new GameObject(name) { layer = Layer };
            root.transform.SetParent(buttonRoot, false);
            RBox("Boot", root.transform, pts[0] + new Vector3(0f, 0f, -0.006f), new Vector3(d * 1.7f, d * 1.7f, 0.016f), d * 0.35f, m);
            Vector3 prev = pts[0]; const int per = 5;
            for (int s = 0; s < pts.Length - 1; s++)
                for (int k = 1; k <= per; k++)
                {
                    float u = k / (float)per;
                    Vector3 p0 = pts[Mathf.Max(0, s - 1)], p1 = pts[s], p2 = pts[s + 1], p3 = pts[Mathf.Min(pts.Length - 1, s + 2)];
                    Vector3 p = 0.5f * (2f * p1 + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u * u + (-p0 + 3f * p1 - 3f * p2 + p3) * u * u * u);
                    var dd = p - prev; float len = dd.magnitude;
                    if (len > 1e-4f)
                    {
                        var r = RBox("Seg", root.transform, (p + prev) * 0.5f, new Vector3(len + d * 0.9f, d, d), d * 0.45f, m);
                        r.transform.localRotation = Quaternion.FromToRotation(Vector3.right, dd / len);
                    }
                    prev = p;
                }
        }

        /// <summary>ApplySlam: the gauge needle swings over on the hit (ease-out-back), one red lamp blinks (CALM SCREEN: steady).</summary>
        void TickDesk(float u)
        {
            if (deskNeedle)
            {
                float a;
                if (u < SlamHit) a = -105f + 4f * Mathf.Sin(clock * 9f) + 2f * Mathf.Sin(clock * 23f);
                else
                {
                    float t = Mathf.Clamp01((u - SlamHit) / 0.35f) - 1f; const float c1 = 1.70158f, c3 = c1 + 1f;
                    a = Mathf.LerpUnclamped(-105f, 98f, 1f + c3 * t * t * t + c1 * t * t) + (OzMotion.Reduced ? 0f : 3f * Mathf.Sin(clock * 31f));
                }
                deskNeedle.localRotation = Quaternion.Euler(0f, 0f, a);
            }
            if (deskBlink)
            {
                float k = OzMotion.Reduced ? 0.7f : ((clock * 3.2f) % 1f < 0.5f ? 1f : 0.08f);
                deskBlink.SetVector("_EmissionColor", (Vector4)(new Color(1f, 0.12f, 0.06f) * deskBlinkBase * k));
            }
        }
    }
}
