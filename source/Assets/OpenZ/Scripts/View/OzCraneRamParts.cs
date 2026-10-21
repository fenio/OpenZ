using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K342 / K342b: the repair station crane's two hydraulic rams as a few merged meshes (View/OzStationCrane.Rams.cs draws and poses
    /// them). K342 drew every part (consoles, ears, nuts, pins, eyes, barrels, glands, stages, rods, hoses, ports: 50 per station) as its
    /// own renderer with its own property block, all casting shadows. The same parts, the same sizes and paints, are baked here into
    /// seven meshes by how they move: the fixed mounts (consoles and base eyes; the small ears, nuts and pins in a mesh of their own), the top
    /// mounts riding the gantry (arms and rod eyes; the small parts in a mesh of their own), and the three telescoping tube sets (both barrels with
    /// their caps, glands, necks, hoses and ports; both stages with their glands; both rods with their necks). Both rams always share one
    /// pose (the gantry moves only north and up: the two rams are mirror twins in x), so each tube set is one mesh for both sides.
    /// K342b also moves the west top mount: the tower's cream top no longer rides with the beam (the clean split), so the west arm is
    /// bolted to the beam's own west end block (above the tower top, 1 cm clear of it) and its ears reach down to the same pin.
    /// Plain arithmetic, no engine calls: the kit bakes these same meshes off the engine for its pictures and checks.
    /// Numbers: the station frame of View/OzStationCrane.cs (metres, x east, z north, y up), the kit's tools/ram_design.py.
    /// </summary>
    public static class OzCraneRamParts
    {
        // ---- the design (K342 numbers; K342b: the west top mount per side)
        public const float RamX = 5.05f, BaseY = 0.62f, BaseZ = -1.00f, TopY = 4.60f, TopZ = 1.75f;
        /// <summary>[0] west, [1] east: the top arm's inner end (bolted into the beam's west end block / the post's recessed face), how
        /// deep it is bolted in at most, its height (bottom, top).</summary>
        public static readonly float[] FaceTop = { -4.049f, 3.746f }, EmbedTop = { 0.09f, 0.11f }, ArmY0 = { 5.06f, 4.76f }, ArmY1 = { 5.30f, 5.00f };
        public static readonly float[] FaceBase = { -4.654f, 3.995f };
        public const float Embed = 0.02f, ConOut = 0.01f, CapDrop = 0.01f;
        public const float Body0 = 0.30f, LB = 3.70f, LS = 3.00f, LR = 3.00f, Tip = 0.06f, OvMin = 0.35f;
        public const float RBody = 0.23f, RCap = 0.245f, RGland = 0.255f, RStage = 0.165f, RSGland = 0.185f, RRod = 0.115f;
        public const float CapL = 0.10f, GlandL = 0.12f, SGlandL = 0.07f;
        public const float EyeR0 = 0.15f, EyeR1 = 0.14f, EyeHalf = 0.12f, NeckR0 = 0.09f, NeckR1 = 0.085f;
        public const float EarIn = 0.135f, EarT = 0.05f, PinR = 0.055f, PinHalf = 0.23f, NutR = 0.085f, NutT = 0.03f;
        public const float HoseR = 0.035f, HoseOff = 0.28f, HoseS0 = 0.50f, HoseS1 = 3.70f, PortLat = 0.255f;
        public static readonly Vector3 Port = new Vector3(0.10f, 0.12f, 0.10f);
        public const float ConY0 = 0.06f, ConY1 = 0.44f, ConZ0 = -1.30f, ConZ1 = -0.70f, BEarY0 = 0.40f, BEarY1 = 0.76f, BEarZH = 0.14f;
        public const float ArmZH = 0.30f, TEarY0 = 4.44f, TEarZH = 0.14f;
        public const int PaintTeam = 0, PaintCream = 1, PaintDark = 2, PaintSteel = 3;

        /// <summary>The seven meshes: how each moves (FixedBig / FixedSmall: with the station; TopBig / TopSmall: by the gantry's offset;
        /// Barrels: turned about the base pins' line; Stages / Rods: turned the same and slid along the rams by e / 2e).</summary>
        public const int FixedBig = 0, FixedSmall = 1, TopBig = 2, TopSmall = 3, Barrels = 4, Stages = 5, Rods = 6, Groups = 7;
        public static readonly string[] GroupNames = { "Mounts", "MountBolts", "TopMounts", "TopBolts", "Barrels", "Stages", "Rods" };
        /// <summary>Submesh 0 / 1 paint of each mesh (-1 = none): the barrels' team paint and their dark parts; the steel tubes and their
        /// dark glands / necks; the mounts are all station paint (cream, dark) in one submesh.</summary>
        public static readonly int[] Sub0 = { PaintDark, PaintDark, PaintDark, PaintDark, PaintTeam, PaintSteel, PaintSteel };
        public static readonly bool[] Shadows = { true, true, true, true, true, true, true };      // every part casts its shadow, as in K342 (the look the same)

        /// <summary>One part (for the proof's inside test): a box or a cylinder along its local y, in its mesh's own space.</summary>
        public struct Shape
        {
            public string name; public int group; public bool cyl; public Vector3 c, ax, ay, az, size; public int paint;
            /// <summary>A bolted-on end (top arm, console): the end face's x and how deep it may sit in what it is bolted to.</summary>
            public bool bolted; public float endX, embed;
        }

        public sealed class MeshData
        {
            public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
            public readonly List<Vector4> t = new List<Vector4>(); public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int>[] sub = { new List<int>(), new List<int>() };
        }

        /// <summary>The barrel set's frame: its origin on the base pins' line (0, BaseY, BaseZ), x along the pins, y along the rams.</summary>
        public static readonly Vector3 Pivot = new Vector3(0f, BaseY, BaseZ);

        /// <summary>Every part of both rams in its mesh's own space, the tubes with no stroke (e = 0). sideFilter -1 west only, +1 east
        /// only, 0 both.</summary>
        public static List<Shape> Shapes(int sideFilter = 0)
        {
            var L = new List<Shape>();
            Vector3 ex = Vector3.right, ey = Vector3.up, ez = Vector3.forward;
            Vector3 xa0 = -ey, xa1 = ex, xa2 = ez;                                                  // a cylinder along x: local x -> -y, y -> x, z -> z
            void Box(string nm, int g, int paint, Vector3 c, Vector3 size, bool bolted = false, float endX = 0f, float emb = 0f) =>
                L.Add(new Shape { name = nm, group = g, cyl = false, c = c, ax = ex, ay = ey, az = ez, size = size, paint = paint, bolted = bolted, endX = endX, embed = emb });
            void CylX(string nm, int g, int paint, Vector3 c, float r, float len) =>
                L.Add(new Shape { name = nm, group = g, cyl = true, c = c, ax = xa0, ay = xa1, az = xa2, size = new Vector3(2f * r, len, 2f * r), paint = paint });
            void Along(string nm, int g, int paint, float sx, float s0, float s1, float r, float lateral = 0f) =>
                L.Add(new Shape { name = nm, group = g, cyl = true, c = new Vector3(sx * RamX, (s0 + s1) * 0.5f, -lateral), ax = ex, ay = ey, az = ez, size = new Vector3(2f * r, s1 - s0, 2f * r), paint = paint });
            for (int i = 0; i < 2; i++)
            {
                int sx = i == 0 ? -1 : 1; if (sideFilter != 0 && sideFilter != sx) continue;
                string k = sx < 0 ? "W" : "E"; float X = sx * RamX;
                var P0 = new Vector3(X, BaseY, BaseZ); var top = new Vector3(X, TopY, TopZ);
                float xo = X + sx * (EarIn + EarT), xc = xo + sx * ConOut, fb = FaceBase[i], ft = FaceTop[i];
                Box($"Ram{k}_BaseConsole", FixedBig, PaintCream, new Vector3((fb + xc) * 0.5f, (ConY0 + ConY1) * 0.5f, (ConZ0 + ConZ1) * 0.5f), new Vector3(Mathf.Abs(xc - fb), ConY1 - ConY0, ConZ1 - ConZ0), true, fb, Embed);
                Box($"Ram{k}_TopArm", TopBig, PaintDark, new Vector3((ft + xo) * 0.5f, (ArmY0[i] + ArmY1[i]) * 0.5f, TopZ), new Vector3(Mathf.Abs(xo - ft), ArmY1[i] - ArmY0[i], 2f * ArmZH), true, ft, EmbedTop[i]);
                for (int e = -1; e <= 1; e += 2)
                {
                    string nm = e == -sx ? "In" : "Out"; float cx = X + e * (EarIn + EarT * 0.5f), nx = X + e * (EarIn + EarT + NutT * 0.5f);
                    Box($"Ram{k}_BaseEar{nm}", FixedSmall, PaintDark, new Vector3(cx, (BEarY0 + BEarY1) * 0.5f, BaseZ), new Vector3(EarT, BEarY1 - BEarY0, 2f * BEarZH));
                    CylX($"Ram{k}_BaseNut{nm}", FixedSmall, PaintDark, new Vector3(nx, BaseY, BaseZ), NutR, NutT);
                    Box($"Ram{k}_TopEar{nm}", TopSmall, PaintDark, new Vector3(cx, (TEarY0 + ArmY0[i]) * 0.5f, TopZ), new Vector3(EarT, ArmY0[i] - TEarY0, 2f * TEarZH));
                    CylX($"Ram{k}_TopNut{nm}", TopSmall, PaintDark, new Vector3(nx, TopY, TopZ), NutR, NutT);
                }
                CylX($"Ram{k}_BasePin", FixedSmall, PaintDark, P0, PinR, 2f * PinHalf);
                CylX($"Ram{k}_TopPin", TopSmall, PaintDark, top, PinR, 2f * PinHalf);
                CylX($"Ram{k}_BaseEye", FixedBig, PaintDark, P0, EyeR0, 2f * EyeHalf);
                CylX($"Ram{k}_RodEye", TopBig, PaintDark, top, EyeR1, 2f * EyeHalf);
                // the tubes, in the barrel set's frame (x = the station's, y along the ram from the base pin, z = -the ram's upper side)
                Along($"Ram{k}_BaseNeck", Barrels, PaintDark, sx, 0.05f, Body0 + 0.04f, NeckR0);
                Along($"Ram{k}_BarrelCap", Barrels, PaintDark, sx, Body0 - CapDrop, Body0 + CapL - CapDrop, RCap);
                Along($"Ram{k}_Barrel", Barrels, PaintTeam, sx, Body0, Body0 + LB, RBody);
                Along($"Ram{k}_BarrelGland", Barrels, PaintDark, sx, Body0 + LB - GlandL + 0.02f, Body0 + LB + 0.02f, RGland);
                Along($"Ram{k}_Hose", Barrels, PaintDark, sx, HoseS0, HoseS1, HoseR, HoseOff);
                Box($"Ram{k}_PortA", Barrels, PaintDark, new Vector3(X, HoseS0, -PortLat), Port);
                Box($"Ram{k}_PortB", Barrels, PaintDark, new Vector3(X, HoseS1, -PortLat), Port);
                float s1 = Body0 + LB;                                                               // the stage's top with no stroke
                Along($"Ram{k}_Stage", Stages, PaintSteel, sx, s1 - LS, s1, RStage);
                Along($"Ram{k}_StageGland", Stages, PaintDark, sx, s1 - SGlandL, s1 + 0.01f, RSGland);
                float r1 = Body0 + LB;                                                               // the rod's top with no stroke
                Along($"Ram{k}_Rod", Rods, PaintSteel, sx, r1 - LR, r1, RRod);
                Along($"Ram{k}_RodNeck", Rods, PaintDark, sx, r1 - 0.10f, r1 + 0.04f, NeckR1);
            }
            return L;
        }

        /// <summary>The seven meshes. paintUv: the station's texels (team paint, cream, dark); the steel's UVs are any (one texel).</summary>
        public static MeshData[] Bake(Vector2[] paintUv, int sideFilter = 0)
        {
            var M = new MeshData[Groups]; for (int g = 0; g < Groups; g++) M[g] = new MeshData();
            foreach (var s in Shapes(sideFilter))
            {
                var m = M[s.group]; int sub = s.group < Barrels || s.paint == Sub0[s.group] ? 0 : 1;      // the mounts: one submesh
                var uv = s.paint == PaintSteel ? Vector2.zero : paintUv[Mathf.Clamp(s.paint, 0, 2)];
                Unit(s.cyl, out var uvv, out var uvn, out var ut, out var tri);
                int b0 = m.v.Count;
                for (int i = 0; i < uvv.Count; i++)
                {
                    var q = uvv[i]; var lp = new Vector3(q.x * s.size.x, q.y * s.size.y, q.z * s.size.z);
                    m.v.Add(s.c + s.ax * lp.x + s.ay * lp.y + s.az * lp.z);
                    var nn = uvn[i]; var ln = new Vector3(nn.x / s.size.x, nn.y / s.size.y, nn.z / s.size.z).normalized;   // as the engine turns a scaled normal
                    m.n.Add((s.ax * ln.x + s.ay * ln.y + s.az * ln.z).normalized);
                    var tt = ut[i]; var lt = new Vector3(tt.x * s.size.x, tt.y * s.size.y, tt.z * s.size.z).normalized;
                    var wt = (s.ax * lt.x + s.ay * lt.y + s.az * lt.z).normalized; m.t.Add(new Vector4(wt.x, wt.y, wt.z, tt.w));
                    m.uv.Add(uv);
                }
                foreach (int x in tri) m.sub[sub].Add(b0 + x);
            }
            return M;
        }

        /// <summary>K342's unit primitives: a cylinder (radius 0.5, y -0.5 .. 0.5, 20 sides, capped) or a box (-0.5 .. 0.5), front faces
        /// outward (Cross(b - a, c - a)), a tangent across each normal.</summary>
        static void Unit(bool cyl, out List<Vector3> v, out List<Vector3> n, out List<Vector4> tg, out List<int> tris)
        {
            v = new List<Vector3>(); n = new List<Vector3>(); tris = new List<int>();
            var V = v; var T = tris;
            void Tri(int a, int b, int c, Vector3 outward)
            {
                if (Vector3.Dot(Vector3.Cross(V[b] - V[a], V[c] - V[a]), outward) < 0f) { int s = b; b = c; c = s; }
                T.Add(a); T.Add(b); T.Add(c);
            }
            if (cyl)
            {
                const int N = 20;
                for (int j = 0; j <= N; j++)
                {
                    float a = j * Mathf.PI * 2f / N; var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    v.Add(d * 0.5f + Vector3.down * 0.5f); n.Add(d); v.Add(d * 0.5f + Vector3.up * 0.5f); n.Add(d);
                }
                for (int j = 0; j < N; j++)
                {
                    int a = 2 * j, b = a + 1, c = a + 2, e = a + 3; var o = (n[a] + n[c]).normalized;
                    Tri(a, b, e, o); Tri(a, e, c, o);
                }
                for (int s = -1; s <= 1; s += 2)
                {
                    int c0 = v.Count; v.Add(Vector3.up * 0.5f * s); n.Add(Vector3.up * s);
                    for (int j = 0; j < N; j++) { float a = j * Mathf.PI * 2f / N; v.Add(new Vector3(Mathf.Cos(a) * 0.5f, 0.5f * s, Mathf.Sin(a) * 0.5f)); n.Add(Vector3.up * s); }
                    for (int j = 0; j < N; j++) Tri(c0, c0 + 1 + j, c0 + 1 + (j + 1) % N, Vector3.up * s);
                }
            }
            else
            {
                for (int f = 0; f < 6; f++)
                {
                    var nn = f == 0 ? Vector3.right : f == 1 ? Vector3.left : f == 2 ? Vector3.up : f == 3 ? Vector3.down : f == 4 ? Vector3.forward : Vector3.back;
                    var a1 = Mathf.Abs(nn.y) > 0.5f ? Vector3.right : Vector3.up; var a2 = Vector3.Cross(nn, a1);
                    int b0 = v.Count;
                    foreach (var q in new[] { -a1 - a2, a1 - a2, a1 + a2, -a1 + a2 }) { v.Add((nn + q) * 0.5f); n.Add(nn); }
                    Tri(b0, b0 + 1, b0 + 2, nn); Tri(b0, b0 + 2, b0 + 3, nn);
                }
            }
            tg = new List<Vector4>(n.Count);
            foreach (var nv in n) { var a = Vector3.Cross(nv, Mathf.Abs(nv.y) < 0.9f ? Vector3.up : Vector3.right).normalized; tg.Add(new Vector4(a.x, a.y, a.z, 1f)); }
        }

        /// <summary>The rams' pose for the gantry's offset (station frame): the ram's direction u (from the base pin to the top pin), its
        /// pin-to-pin length L and each stage's share e of the stroke (clamped to the reach; clamped = true when it had to be).</summary>
        public static void Pose(Vector3 off, out Vector3 u, out float L, out float e, out bool clamped)
        {
            var d = new Vector3(0f, TopY + off.y - BaseY, TopZ + off.z - BaseZ); L = d.magnitude; u = L > 1e-4f ? d / L : Vector3.up;
            e = (L - Tip - Body0 - LB) * 0.5f; clamped = e > LS - OvMin || e < 0f; e = Mathf.Clamp(e, 0f, LS - OvMin);
        }
    }
}
