#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>K419a (the BATCH 3 Play faults, view only): a counted check out of Play of the kit's numbers, its hooks, the pieces' paint
    /// band and the mark's night tint. The Play re-checks are the kits' own proofs (K387 route, K256h death altitude, Break-up Proof K,
    /// K401 mark visible, K393 damage smoke, K397 fuel cans): see the kit's SPEC.md part 0.</summary>
    public static class OzK419aCheck
    {
        [MenuItem("OpenZ/V1.1/K419a: Batch 3 Faults Check (out of Play)")]
        static void Menu() { var r = Run(); Debug.Log("[OpenZ] K419a check: " + r); }

        static int pass, fail;
        static void Check(string what, bool ok) { if (ok) pass++; else fail++; Debug.Log("[OpenZ] K419a check: " + (ok ? "PASS " : "FAIL ") + what); }
        static string Src(string rel) { var p = System.IO.Path.Combine(Application.dataPath, "OpenZ/Scripts/" + rel); return System.IO.File.Exists(p) ? System.IO.File.ReadAllText(p) : ""; }
        static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) < 1e-4f && Mathf.Abs(a.g - b.g) < 1e-4f && Mathf.Abs(a.b - b.b) < 1e-4f;

        public static string Run()
        {
            pass = 0; fail = 0;
            // 1. the route (K387): the dots evenly spaced on screen, the start critically damped
            string route = Src("View/SelectionOverlay.ShuttleRoute.cs");
            Check("C1 the route's hooks: DrawLeg lays its dots by the screen chord (DrawLegEven), the start under the ship critically damped (BellyEase)",
                route.Contains("if (DrawLegEven(endDot, step, first, size, mat)) return;") && route.Contains("off = BellyEase(o.Id, off);") && SelectionOverlay.EvenDots);
            // 2. the pieces' paint band (the light grey roof panel)
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/OpenZ/Models/L16/Shuttle/M_L16_Shuttle.mat");
            bool bandOk = false; string bandWhy = "the shuttle material not found";
            if (mat)
            {
                var go = new GameObject("K419a_band_probe") { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    var m = new Mesh { subMeshCount = 2 }; m.SetVertices(new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward });
                    m.SetTriangles(new[] { 0, 1, 2 }, 0); m.SetTriangles(new[] { 0, 2, 3 }, 1);
                    go.AddComponent<MeshFilter>().sharedMesh = m; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterials = new[] { mat, mat };
                    var fn = typeof(OzShuttleBreakup).GetMethod("PieceBand", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    if (fn == null) bandWhy = "OzShuttleBreakup.PieceBand not found";
                    else
                    {
                        fn.Invoke(null, new object[] { r, 2, true });
                        var b0 = new MaterialPropertyBlock(); r.GetPropertyBlock(b0, 0); var b1 = new MaterialPropertyBlock(); r.GetPropertyBlock(b1, 1);
                        float lo = b0.GetFloat("_AccentLo"), hi = b0.GetFloat("_AccentHi");
                        bandOk = lo <= -1e5f && hi >= 1e5f && !b1.HasFloat("_AccentLo") && mat.GetFloat("_Accent") > 0.5f;
                        bandWhy = $"paint sub-mesh band {lo:0} .. {hi:0}, the inside sub-mesh untouched {!b1.HasFloat("_AccentLo")}, the material in accent mode {mat.GetFloat("_Accent") > 0.5f}";
                    }
                }
                finally { Object.DestroyImmediate(go); }
            }
            Check($"C2 a piece's paint keeps the team colour over its whole height, its inside is not touched ({bandWhy})", bandOk);
            var unit = Resources.Load<GameObject>("OpenZ/Vehicles/L16_ShuttleUnit");
            var hullT = unit ? FindDeep(unit.transform, "SH_Hull") : null; var hmf = hullT ? hullT.GetComponent<MeshFilter>() : null;
            if (!hmf)
            {   // the model itself (the unit may build its Model at run time)
                unit = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/OpenZ/Models/L16/Shuttle/L16_Shuttle.fbx");
                hullT = unit ? FindDeep(unit.transform, "SH_Hull") : null; hmf = hullT ? hullT.GetComponent<MeshFilter>() : null;
            }
            string why = "the shuttle unit or its SH_Hull not found"; bool cause = false;
            if (hmf && hmf.sharedMesh)
            {
                var hb = hmf.sharedMesh.bounds; float bandHi = hb.min.y + 1.1f * hb.size.y;
                var toRoot = unit.transform.worldToLocalMatrix * hullT.localToWorldMatrix; float top = float.MinValue, bot = float.MaxValue;
                foreach (var v in hmf.sharedMesh.vertices) { float y = toRoot.MultiplyPoint3x4(v).y; top = Mathf.Max(top, y); bot = Mathf.Min(bot, y); }
                cause = top > bandHi;
                why = $"the hull's band from its own mesh ends at {bandHi:F2}; in the root space the pieces' meshes use, the hull reaches {bot:F2} .. {top:F2} m: {Mathf.Clamp01((top - bandHi) / Mathf.Max(0.01f, top - bot)) * 100f:F0} % of its height over the band";
            }
            Check($"C3 the cause, measured on the model: {why}", cause);
            string brk = Src("View/OzShuttleBreakup.cs");
            Check("C4 Build widens the band on every piece's paint (PieceBand after the paint)", brk.Contains("PieceBand(r, nSub, capped);") && OzShuttleBreakup.PieceBandOn);
            // 3. the pieces' paint and inside lighter (Break-up Proof K's lumas)
            var sc = OzShuttleBreakup.ShellColor;
            Check($"C5 the pieces' paint less burnt (PaintCharred {OzShuttleBreakup.PaintCharred:F2}: x{1f - 0.78f * OzShuttleBreakup.PaintCharred:F2} of the paint, was x0.65), the inside's steel a little lighter (ShellColor {sc.r:F2}, {sc.g:F2}, {sc.b:F2}, was 0.85, 0.82, 0.80)",
                Mathf.Abs(OzShuttleBreakup.PaintCharred - 0.18f) < 1e-4f && Mathf.Abs(sc.r - 0.95f) < 1e-4f && Mathf.Abs(sc.g - 0.92f) < 1e-4f && Mathf.Abs(sc.b - 0.89f) < 1e-4f);
            // 4. K396: the pockets counted per blast, the split's out-throw
            string bb = Src("View/OzShuttleBreakup.BigBlast.cs"), k396 = Src("View/OzShuttleDeathAltitudeProof.K396.cs");
            Check("C6 the burning pockets and the column puffs counted from zero at each air blast, the proof reads those counts",
                bb.Contains("LastSecondariesFired = 0; LastColumnPuffs = 0;") && bb.Contains("LastSecondariesFired++;") && bb.Contains("LastColumnPuffs++;") && k396.Contains("OzShuttleBreakup.LastSecondariesFired >= OzShuttleBreakup.LastSecondaries"));
            Check($"C7 the split's out-throw x{OzShuttleBreakup.SplitOutK:F2} (was 0.90; the twin: median 6.5 m/s or more, the farthest piece well inside 50 m)", Mathf.Abs(OzShuttleBreakup.SplitOutK - 1.15f) < 1e-4f);
            // 5. K401: the dark mark black at night, the red as before, day unchanged
            var d0 = OzShuttleShadow.NightTint(0f, 0f); var r0 = OzShuttleShadow.NightTint(1f, 0f); var d1 = OzShuttleShadow.NightTint(0f, 1f); var r1 = OzShuttleShadow.NightTint(1f, 1f);
            float kr = 1f - OzShuttleShadow.NightDim;
            Check($"C8 the mark's tint: by day as before (dark {d0.r:F3}, red {r0.r:F3}); at night the dark one black ({d1.r:F3}, {d1.g:F3}, {d1.b:F3}), the red one as before (x{kr:F2}: {r1.r:F3}, still red)",
                Near(d0, OzShuttleShadow.Dark) && Near(r0, OzShuttleShadow.Red) && d1.r < 1e-5f && d1.g < 1e-5f && d1.b < 1e-5f && Near(r1, new Color(OzShuttleShadow.Red.r * kr, OzShuttleShadow.Red.g * kr, OzShuttleShadow.Red.b * kr)) && r1.r > r1.g * 3f
                && Src("View/OzShuttleShadow.cs").Contains("NightTint(rk, night)"));
            // 6. K393: the ship's smoke readable at 70 m (the same effect, sized and laid for the ship)
            string dss = Src("View/FxV3/OpenZDamageStates.Shuttle.cs");
            Check($"C9 the shuttle's damage smoke: puffs at the full size for the ship (ShipPointShare {OpenZDamageStates.ShipPointShare:F2}, smoke x{OpenZDamageStates.ShipSmokeScale:F2}), the beat x{OpenZDamageStates.ShipBeatK:F2}, a trail puff each {OpenZDamageStates.TrailSpacingM:F2} m",
                Mathf.Abs(OpenZDamageStates.ShipPointShare - 1f) < 1e-4f && Mathf.Abs(OpenZDamageStates.ShipBeatK - 0.8f) < 1e-4f && Mathf.Abs(OpenZDamageStates.TrailSpacingM - 0.34f) < 1e-4f
                && dss.Contains("Random.Range(0.3f, 0.45f) * ShipBeatK") && dss.Contains("u.nextFx = now + 0.3f * ShipBeatK;"));
            return fail == 0 ? $"{pass} PASS" : $"{fail} FAIL of {pass + fail}";
        }

        static Transform FindDeep(Transform t, string n)
        {
            if (t.name == n) return t;
            foreach (Transform c in t) { var f = FindDeep(c, n); if (f) return f; }
            return null;
        }
    }
}
#endif
