using System;
using System.Collections.Generic;
using System.Text;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Level 16 wind, the counted check of the sway and the rain lean on the five trees of the design's sway sheet (import plan,
    /// step J9. Pure C#: it measures each tree's shape with the game's
    /// own code (OzFloraWindMath.Measure) and moves its points with the game's own sway (OzFloraWindMath.Displace, the plain
    /// number twin of Shaders/OpenZFloraWind.hlsl), in calm wind (strength 0.75) and in the rain wind (strength 1.5, lean on),
    /// over the design's own 19.2 s at 5 samples a second, and compares with the design's measured table
    /// (leaf tips travel, one sway, crown top pushed downwind).
    /// Menu: OpenZ/V1.1/Count Sway And Lean Of Five Trees (J9). The first line of the text is the verdict "PASS n/n".
    /// </summary>
    public static class OzV11SwayCount
    {
        public sealed class Ref
        {
            public string Part, Label; public int Kind; public float SheetX;
            public float Height, PivotY, Radius;                                                         // the design's shape numbers
            public float CalmTips, CalmSway, CalmTopMean, RainTips, RainSway, RainTopMean, RainTopMax;   // the design's measured movement
        }

        /// <summary>The design's own table (five trees: three shown on its sheet, two as numbers).</summary>
        public static readonly Ref[] Five =
        {
            new Ref { Part = "PalmTallLean", Label = "tall leaning palm", Kind = OzFloraWindMath.Palm, SheetX = -10f, Height = 7.79f, PivotY = 5.85f, Radius = 2.10f,
                      CalmTips = 0.364f, CalmSway = 2.69f, CalmTopMean = 0.072f, RainTips = 0.909f, RainSway = 2.00f, RainTopMean = 1.077f, RainTopMax = 1.320f },
            new Ref { Part = "TreeKapokA", Label = "tall kapok tree", Kind = OzFloraWindMath.Tree, SheetX = 0f, Height = 13.0f, PivotY = 8.25f, Radius = 2.40f,
                      CalmTips = 0.306f, CalmSway = 2.97f, CalmTopMean = 0.076f, RainTips = 0.770f, RainSway = 2.20f, RainTopMean = 0.851f, RainTopMax = 1.194f },
            new Ref { Part = "TreeSwampMossA", Label = "swamp tree with hanging moss", Kind = OzFloraWindMath.Tree, SheetX = 10f, Height = 8.0f, PivotY = 4.80f, Radius = 2.15f,
                      CalmTips = 0.232f, CalmSway = 2.65f, CalmTopMean = 0.053f, RainTips = 0.585f, RainSway = 1.96f, RainTopMean = 0.730f, RainTopMax = 0.945f },
            new Ref { Part = "PalmShortBushy", Label = "short bushy palm", Kind = OzFloraWindMath.Palm, SheetX = 40f, Height = 4.31f, PivotY = 2.69f, Radius = 2.29f,
                      CalmTips = 0.458f, CalmSway = 1.74f, CalmTopMean = 0.081f, RainTips = 1.114f, RainSway = 1.29f, RainTopMean = 0.965f, RainTopMax = 1.308f },
            new Ref { Part = "PalmYoung", Label = "young palm (robot height)", Kind = OzFloraWindMath.Small, SheetX = 52f, Height = 2.12f, PivotY = 1.27f, Radius = 1.60f,
                      CalmTips = 0.162f, CalmSway = 1.61f, CalmTopMean = 0.034f, RainTips = 0.399f, RainSway = 1.19f, RainTopMean = 0.444f, RainTopMax = 0.561f },
        };

        public const float WindX = 0.9700247f, WindZ = -0.2430062f;      // the wind of the design's sheet
        public const float TolMove = 0.15f, TolShape = 0.15f, TolSway = 0.03f;

        public sealed class Result
        {
            public OzFloraWindMath.Shape Shape; public int Points;
            public float Tips, TopTravel, TopMean, TopMax, Largest, MovingShare, FootMove, SwaySeconds, LeanDeg, TrunkLeanDeg;
        }

        /// <summary>One tree, one weather: the design's measurement (96 samples, 0.2 s apart, from second 20).</summary>
        public static Result Measure(float[] xyz, int kind, float sheetX, bool rain, OzFloraWindMath.Shape? given = null)
        {
            int n = xyz.Length / 3;
            var s = given ?? OzFloraWindMath.Measure(xyz, n, kind);
            var r = new Result { Shape = s, Points = n };
            float footX = sheetX + 3.7f, footY = 0f, footZ = 11.3f;
            float topX = footX + s.Px, topY = footY + s.Py, topZ = footZ + s.Pz;
            float storm = rain ? 1f : 0f, S = rain ? OzFloraWindMath.RainStrength : OzFloraWindMath.CalmStrength;
            var lo = new float[n * 3]; var hi = new float[n * 3];
            for (int i = 0; i < n * 3; i++) { lo[i] = float.MaxValue; hi[i] = float.MinValue; }
            int top = 0; float topH = float.MinValue, lowBand = 0.05f * s.H;
            for (int i = 0; i < n; i++) if (xyz[3 * i + 1] > topH) { topH = xyz[3 * i + 1]; top = i; }
            float sumTop = 0f, maxTop = float.MinValue, largest = 0f, foot = 0f, sumPivot = 0f; const int Samples = 96;
            for (int k = 0; k < Samples; k++)
            {
                float t = 20f + 0.2f * k;
                var w = new OzFloraWindMath.Wind { Wx = WindX, Wz = WindZ, S = S, Storm = storm, T = t * (1f + OzFloraWindMath.RainTempo * storm), Gust = rain ? OzFloraWindMath.RainGust(t) : OzFloraWindMath.CalmGust(t) };
                for (int i = 0; i < n; i++)
                {
                    float x = footX + xyz[3 * i], y = footY + xyz[3 * i + 1], z = footZ + xyz[3 * i + 2];
                    OzFloraWindMath.Displace(kind, w, footX, footY, footZ, topX, topY, topZ, 1f, s.R, s.H, 1f, x, y, z, out float dx, out float dy, out float dz);
                    if (dx < lo[3 * i]) lo[3 * i] = dx; if (dx > hi[3 * i]) hi[3 * i] = dx;
                    if (dy < lo[3 * i + 1]) lo[3 * i + 1] = dy; if (dy > hi[3 * i + 1]) hi[3 * i + 1] = dy;
                    if (dz < lo[3 * i + 2]) lo[3 * i + 2] = dz; if (dz > hi[3 * i + 2]) hi[3 * i + 2] = dz;
                    float m = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz); if (m > largest) largest = m;
                    if (xyz[3 * i + 1] <= lowBand && m > foot) foot = m;
                    if (i == top) { float dw = dx * WindX + dz * WindZ; sumTop += dw; if (dw > maxTop) maxTop = dw; }
                }
                // the pivot itself rides on the trunk: its downwind move is the trunk's lean
                OzFloraWindMath.Displace(kind, w, footX, footY, footZ, topX, topY, topZ, 1f, s.R, s.H, 1f, topX, topY, topZ, out float px, out _, out float pz);
                sumPivot += px * WindX + pz * WindZ;
            }
            var pp = new List<float>(n); int moving = 0;
            for (int i = 0; i < n; i++)
            {
                float ax = hi[3 * i] - lo[3 * i], ay = hi[3 * i + 1] - lo[3 * i + 1], az = hi[3 * i + 2] - lo[3 * i + 2];
                float v = (float)Math.Sqrt(ax * ax + ay * ay + az * az); pp.Add(v); if (v > 0.02f) moving++;
                if (i == top) r.TopTravel = v;
            }
            pp.Sort();
            float pos = 0.99f * (n - 1); int i0 = (int)Math.Floor(pos), i1 = Math.Min(i0 + 1, n - 1);
            r.Tips = pp[i0] + (pp[i1] - pp[i0]) * (pos - i0);
            r.TopMean = sumTop / Samples; r.TopMax = maxTop; r.Largest = largest; r.MovingShare = n > 0 ? (float)moving / n : 0f; r.FootMove = foot;
            float h2 = OzFloraWindMath.Hash(footZ + 17.3f, footX + 17.3f);
            OzFloraWindMath.SizeK(s.H, out float tempo, out _);
            r.SwaySeconds = 6.2831853f / (2.64f * (0.88f + 0.24f * h2) * tempo * (1f + OzFloraWindMath.RainTempo * storm));
            r.LeanDeg = (float)(Math.Atan2(r.TopMean, Math.Max(0.1f, topH)) * 180.0 / Math.PI);
            r.TrunkLeanDeg = (float)(Math.Atan2(sumPivot / Samples, Math.Max(0.1f, s.Py)) * 180.0 / Math.PI);
            return r;
        }

        static bool Near(float got, float want, float tol) => Math.Abs(got - want) <= tol * Math.Abs(want);

        /// <summary>pointsOf(part of the shape's name) = its points as x, up, z in metres with the foot at 0 (null = not found).</summary>
        public static string Run(Func<string, float[]> pointsOf)
        {
            var sb = new StringBuilder(); int pass = 0, total = 0;
            void Line(bool ok, string text) { total++; if (ok) pass++; sb.Append(ok ? "PASS  " : "FAIL  ").Append(text).Append('\n'); }
            float calmLeanMax = 0f, rainLeanMin = float.MaxValue;
            foreach (var f in Five)
            {
                var pts = pointsOf(f.Part);
                if (pts == null || pts.Length < 30) { Line(false, f.Label + ": shape " + f.Part + " not found"); continue; }
                var calm = Measure(pts, f.Kind, f.SheetX, false); var rain = Measure(pts, f.Kind, f.SheetX, true, calm.Shape);
                var s = calm.Shape;
                Line(Near(s.H, f.Height, 0.02f) && Math.Abs(s.Py - f.PivotY) <= 0.30f && Near(s.R, f.Radius, TolShape),
                    $"{f.Label} ({calm.Points} points): height {s.H:0.00} m (design {f.Height:0.00}), crown pivot {s.Py:0.00} m up (design {f.PivotY:0.00}, within 0.30), crown radius {s.R:0.00} m (design {f.Radius:0.00}, within 15 %), trunk radius {s.TrunkR:0.00} m");
                Line(Near(calm.Tips, f.CalmTips, TolMove) && Near(calm.SwaySeconds, f.CalmSway, TolSway),
                    $"{f.Label}, calm: leaf tips travel {calm.Tips:0.000} m (design {f.CalmTips:0.000}, within 15 %), one sway {calm.SwaySeconds:0.00} s (design {f.CalmSway:0.00}), {calm.MovingShare * 100f:0} % of the points move more than 2 cm");
                Line(Near(rain.Tips, f.RainTips, TolMove) && Near(rain.TopMax, f.RainTopMax, TolMove) && Near(rain.SwaySeconds, f.RainSway, TolSway),
                    $"{f.Label}, rain wind: leaf tips travel {rain.Tips:0.000} m (design {f.RainTips:0.000}), crown top pushed downwind {rain.TopMax:0.000} m at most (design {f.RainTopMax:0.000}), one sway {rain.SwaySeconds:0.00} s (design {f.RainSway:0.00})");
                Line(Near(rain.TopMean, f.RainTopMean, TolMove) && Math.Abs(calm.TopMean - f.CalmTopMean) <= 0.03f && rain.LeanDeg >= 3f * Math.Max(calm.LeanDeg, 0.1f),
                    $"{f.Label}, lean: calm {calm.LeanDeg:0.00} deg (crown top {calm.TopMean:0.000} m downwind on average, design {f.CalmTopMean:0.000}), in rain {rain.LeanDeg:0.00} deg ({rain.TopMean:0.000} m, design {f.RainTopMean:0.000}, within 15 %); the trunk alone {calm.TrunkLeanDeg:0.00} -> {rain.TrunkLeanDeg:0.00} deg");
                Line(calm.FootMove <= 0.02f && rain.FootMove <= 0.03f, $"{f.Label}: the foot stays: largest move in the lowest 5 % of the height {calm.FootMove * 100f:0.0} cm calm, {rain.FootMove * 100f:0.0} cm in rain (at most 2 and 3 cm)");
                calmLeanMax = Math.Max(calmLeanMax, calm.LeanDeg); rainLeanMin = Math.Min(rainLeanMin, rain.LeanDeg);
            }
            Line(rainLeanMin > calmLeanMax && rainLeanMin >= 2.5f && calmLeanMax <= 1.5f, $"all five: the largest calm lean {calmLeanMax:0.00} deg is under the smallest rain lean {rainLeanMin:0.00} deg (calm at most 1.5, rain at least 2.5)");
            return (pass == total ? "PASS " : "FAIL ") + pass + "/" + total + " lines: sway and rain lean of the five trees of the design's sheet (calm strength 0.75, rain wind 1.5), the game's own shape and sway code\n" + sb;
        }

#if UNITY_EDITOR
        const string Kit = "Assets/OpenZ/Models/L16/Flora/L16_flora_kit.fbx";

        /// <summary>The points of a kit shape as x, up, z in metres with the foot at 0, read from the imported model (the mesh's
        /// own points through the shape's place in the kit, without its position).</summary>
        public static float[] KitPoints(string part)
        {
            var kit = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(Kit); if (!kit) return null;
            foreach (UnityEngine.Transform t in kit.transform)
            {
                if (t.name.EndsWith("_far") || t.name.IndexOf(part, StringComparison.Ordinal) < 0 || t.name.Contains("_lite")) continue;
                var mf = t.GetComponent<UnityEngine.MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
                var v = mf.sharedMesh.vertices; var o = new float[v.Length * 3];
                var m = UnityEngine.Matrix4x4.TRS(UnityEngine.Vector3.zero, t.localRotation, t.localScale);
                for (int i = 0; i < v.Length; i++) { var p = m.MultiplyPoint3x4(v[i]); o[3 * i] = p.x; o[3 * i + 1] = p.y; o[3 * i + 2] = p.z; }
                return o;
            }
            return null;
        }

        [UnityEditor.MenuItem("OpenZ/V1.1/Count Sway And Lean Of Five Trees (J9)")]
        static void Menu()
        {
            string text = Run(KitPoints);
            System.IO.File.WriteAllText(System.IO.Path.Combine(OzL16Weather.OutDir(), "OzV11SwayCount_editor.txt"), text);
            if (text.StartsWith("PASS")) UnityEngine.Debug.Log("[OpenZ J9] " + text); else UnityEngine.Debug.LogError("[OpenZ J9] " + text);
        }
#endif
    }
}
