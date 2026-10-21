#if !OPENZ_HARNESS
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// K440 (item 441: the camera never sees into a shuttle, View/OzShuttleCamFade.cs + OpenZ/TeamLit's _OZCamFade): the counted check out of
    /// Play and the menu of the Play proof (View/OzShuttleCamFadeProof.cs, level 16). The check builds the unit prefab of the shuttle in a
    /// preview scene (nothing in the open scene, nothing saved), poses it through the whole kickflip (the game's own curve, both roll
    /// directions, three headings) and asks the game's own fade code:
    ///  C1 TeamLit compiles without an error and has _OZCamFade; C2 its default is 0 and every TeamLit material keeps 0 (every other object
    ///  draws as before); C3 each drawn part of the ship takes the fade; C4 each drawn vertex lies inside its part's box in every pose (so the
    ///  measure is never farther than the hull); C5 an eye within the near plane's reach (0.58 m) of the hull always gets the full cut;
    ///  C6 an eye 6 m or more from every box gets none, the cut eases between; C7 the cut is written into the blocks the parts already have
    ///  (their team paint kept) and taken back to 0, and a break-up piece built with a copy of the cut hull's block gets it back to 0; C8 the numbers the off-engine sweep used are still the game's; C9 the kickflip's top
    ///  (why the closest zoom needs it).
    /// </summary>
    public static class OzShuttleCamFadeCheck
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K440 Camera No-Clip Check (no Play)")]
        static void CheckMenu()
        {
            if (Application.isPlaying) { Debug.LogWarning("[OpenZ V1.1] K440 camera no-clip check: leave Play mode first"); return; }
            Debug.Log("[OpenZ V1.1] K440 camera no-clip check\n" + Check());
        }

        [MenuItem("OpenZ/V1.1/Shuttle: K440 Camera No-Clip Proof (Level 16, Play)")]
        static void ProofMenu()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K440 camera no-clip proof: start level 16 in Play first"); return; }
            OzShuttleCamFadeProof.Run();
            Debug.Log("[OpenZ] K440 camera no-clip proof started; its report: OzShuttleCamFadeProof.Report, the log and pictures in " + OzShuttleCamFadeProof.Folder());
        }

        const float NearReach = 0.58f;                                                              // the near plane (0.5 m) at the frame's corners (FOV 32, 16:9)

        public static string Check()
        {
            var sb = new StringBuilder(); int pass = 0, fail = 0;
            void C(bool ok, string what) { if (ok) pass++; else fail++; sb.AppendLine((ok ? "PASS " : "FAIL ") + what); }
            string prop = OzShuttleCamFade.FadeProperty;

            // C1 / C2 the shader and the materials
            var sh = Shader.Find("OpenZ/TeamLit");
            int pi = sh ? sh.FindPropertyIndex(prop) : -1;
            bool err = !sh || ShaderUtil.ShaderHasError(sh);
            C(sh && !err && pi >= 0, $"C1 OpenZ/TeamLit compiles without an error and has {prop} (shader {(sh ? "found" : "MISSING")}, errors {(err ? "YES" : "none")}, property index {pi})");
            float def = pi >= 0 ? sh.GetPropertyDefaultFloatValue(pi) : -1f;
            int mats = 0, nonZero = 0; string firstBad = "";
            foreach (var guid in AssetDatabase.FindAssets("t:Material"))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (!m || m.shader != sh) continue;
                mats++;
                if (m.HasProperty(prop) && m.GetFloat(prop) != 0f) { nonZero++; if (firstBad == "") firstBad = m.name; }
            }
            C(def == 0f && mats > 0 && nonZero == 0, $"C2 its default is 0 (= {def:0.###}) and every TeamLit material keeps 0: {mats} materials, {nonZero} not 0{(firstBad != "" ? " (first: " + firstBad + ")" : "")}: every other object draws as before");

            // the ship, built in a preview scene
            var prefab = Resources.Load<GameObject>(OzPlayableShuttle.UnitPrefabPath);
            if (!prefab) { C(false, "C3 the shuttle's unit prefab Resources/" + OzPlayableShuttle.UnitPrefabPath + " loads"); return Summary(sb, pass, fail); }
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject go = null;
            try
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var dec = go.GetComponent<L16Shuttle>();
                if (dec)
                {   // as the game draws a unit shuttle (OzPlayableShuttle.SpawnView): the wreck kit off
                    foreach (var p in dec.pieces) if (p) foreach (var r in p.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                    if (dec.wreck) dec.wreck.gameObject.SetActive(false);
                }
                var root = go.transform;
                var all = new List<Renderer>(); root.GetComponentsInChildren(true, all);
                var fade = new List<Renderer>(); var drawnMesh = new List<(Renderer r, Mesh m)>();
                foreach (var r in all)
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                    if (OzShuttleCamFade.TakesFade(r)) fade.Add(r);
                    var mf = r.GetComponent<MeshFilter>(); if (mf && mf.sharedMesh) drawnMesh.Add((r, mf.sharedMesh));
                }
                int drawn = 0, drawnFade = 0; foreach (var (r, m) in drawnMesh) { drawn++; if (fade.Contains(r)) drawnFade++; }
                C(drawn > 0 && drawnFade == drawn, $"C3 each drawn part of the ship takes the fade: {drawnFade} of {drawn} drawn mesh parts ({all.Count} renderers in all, the wreck kit off)");

                // C4 / C5 / C6 through the kickflip (the game's own curve and pose formula), both directions, three headings
                var verts = new List<Vector3[]>(); foreach (var (r, m) in drawnMesh) verts.Add(m.vertices);
                int poses = 0, outside = 0, nearSamples = 0, nearNotCut = 0, farSamples = 0, farCut = 0; float worstOut = 0f, topMax = 0f, worstNearCut = 1f;
                var rng = new System.Random(440);
                foreach (float head in new[] { 0f, 45f, 90f })
                    foreach (int dir in new[] { 1, -1 })
                        for (int i = 0; i <= 50; i++)
                        {
                            double t = OzKickflipCurve.TotalSec * i / 50.0;
                            Pose(root, head, t, dir);
                            poses++;
                            float bestTop = float.MinValue;
                            for (int k = 0; k < drawnMesh.Count; k++)
                            {
                                var r = drawnMesh[k].r; var b = r.bounds; b.Expand(0.002f); var M = r.transform.localToWorldMatrix; var vs = verts[k];
                                for (int j = 0; j < vs.Length; j++)
                                {
                                    var w = M.MultiplyPoint3x4(vs[j]);
                                    if (!b.Contains(w)) { outside++; worstOut = Mathf.Max(worstOut, Mathf.Sqrt(b.SqrDistance(w))); }
                                    if (w.y > bestTop) bestTop = w.y;
                                }
                            }
                            topMax = Mathf.Max(topMax, bestTop);                                                       // the level root stood at y 0
                            // eyes within the near reach of the hull: a random drawn vertex, a random offset under 0.58 m
                            for (int n = 0; n < 12; n++)
                            {
                                int k = rng.Next(drawnMesh.Count); var vs = verts[k]; if (vs.Length == 0) continue;
                                var w = drawnMesh[k].r.transform.localToWorldMatrix.MultiplyPoint3x4(vs[rng.Next(vs.Length)]);
                                var off = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f);
                                var eye = w + (off.sqrMagnitude > 1e-6f ? off.normalized : Vector3.up) * (float)(rng.NextDouble() * NearReach);
                                float cut = OzShuttleCamFade.CutAt(OzShuttleCamFade.BoxDistance(fade, eye));
                                nearSamples++; if (cut < 0.999f) { nearNotCut++; worstNearCut = Mathf.Min(worstNearCut, cut); }
                            }
                            // eyes far from every box: 6 m and more over the highest box top / beside the farthest box side
                            float boxTop = float.MinValue; foreach (var r in fade) boxTop = Mathf.Max(boxTop, r.bounds.max.y);
                            for (int n = 0; n < 4; n++)
                            {
                                var eye = new Vector3(root.position.x + (float)(rng.NextDouble() * 4 - 2), boxTop + OzShuttleCamFade.ClearM + 0.01f + (float)rng.NextDouble() * 6f, root.position.z + (float)(rng.NextDouble() * 4 - 2));
                                farSamples++; if (OzShuttleCamFade.CutAt(OzShuttleCamFade.BoxDistance(fade, eye)) > 0f) farCut++;
                            }
                        }
                Pose(root, 0f, -1.0, 1);
                C(outside == 0, $"C4 each drawn vertex lies inside its part's box in every pose: {poses} poses (51 kickflip moments x 2 directions x 3 headings), vertices outside {outside} (worst {worstOut:0.000} m): the measure is never farther than the hull");
                C(nearNotCut == 0, $"C5 an eye within the near plane's reach ({NearReach} m) of the hull always gets the full cut: {nearSamples} eyes, not fully cut {nearNotCut}{(nearNotCut > 0 ? " (least cut " + worstNearCut.ToString("0.00") + ")" : "")}");
                float c2 = OzShuttleCamFade.CutAt(OzShuttleCamFade.GoneM), c4 = OzShuttleCamFade.CutAt((OzShuttleCamFade.GoneM + OzShuttleCamFade.ClearM) * 0.5f), c6 = OzShuttleCamFade.CutAt(OzShuttleCamFade.ClearM);
                C(farCut == 0 && c2 == 1f && Mathf.Abs(c4 - 0.5f) < 0.01f && c6 == 0f, $"C6 an eye {OzShuttleCamFade.ClearM} m or more from every box gets no cut ({farSamples} eyes, cut {farCut}); the cut at {OzShuttleCamFade.GoneM} / {(OzShuttleCamFade.GoneM + OzShuttleCamFade.ClearM) * 0.5f} / {OzShuttleCamFade.ClearM} m: {c2:0.00} / {c4:0.00} / {c6:0.00} (eased)");

                // C7 the blocks: a part with its own team paint block per material and a part with none
                Renderer withBlock = null, without = null; var mpb = new MaterialPropertyBlock();
                foreach (var r in fade)
                {
                    r.GetPropertyBlock(mpb, 0); bool has = !mpb.isEmpty || r.HasPropertyBlock();
                    if (has && !withBlock) withBlock = r; else if (!has && !without) without = r;
                }
                int tcId = Shader.PropertyToID("_TeamColor"); var probe = new Color(0.31f, 0.52f, 0.73f, 1f);
                if (!withBlock && fade.Count > 0) { withBlock = fade[0]; mpb.Clear(); mpb.SetColor(tcId, probe); withBlock.SetPropertyBlock(mpb, 0); }
                if (withBlock) { withBlock.GetPropertyBlock(mpb, 0); if (!mpb.HasColor(tcId)) { mpb.SetColor(tcId, probe); withBlock.SetPropertyBlock(mpb, 0); } }
                Color before = default; if (withBlock) { withBlock.GetPropertyBlock(mpb, 0); before = mpb.GetColor(tcId); }
                int mA = withBlock ? withBlock.sharedMaterials.Length : 1, mB = without ? without.sharedMaterials.Length : 1;
                if (withBlock) OzShuttleCamFade.WriteFade(withBlock, 0.5f, mA);
                if (without) OzShuttleCamFade.WriteFade(without, 0.5f, mB);
                float rA = withBlock ? OzShuttleCamFade.ReadFade(withBlock) : -1f, rB = without ? OzShuttleCamFade.ReadFade(without) : 0.5f;
                Color after = default; if (withBlock) { withBlock.GetPropertyBlock(mpb, 0); after = mpb.GetColor(tcId); }
                bool kept = withBlock && after == before;
                if (withBlock) OzShuttleCamFade.WriteFade(withBlock, 0f, mA);
                if (without) OzShuttleCamFade.WriteFade(without, 0f, mB);
                float zA = withBlock ? OzShuttleCamFade.ReadFade(withBlock) : -1f, zB = without ? OzShuttleCamFade.ReadFade(without) : 0f;
                // a break-up while the ship is cut: the pieces copy the hull's paint block (OzShuttleBreakup.Break -> ShellPaint); the copied cut
                // must be put back to 0 on the piece, its paint kept
                float pc0 = -1f, pc1 = -1f; bool pieceKept = false; int clearedN = 0;
                if (withBlock)
                {
                    var piece = new GameObject("K440_PieceProbe"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(piece, scene);
                    try
                    {
                        var pr = piece.AddComponent<MeshRenderer>(); pr.sharedMaterials = withBlock.sharedMaterials;
                        OzShuttleCamFade.WriteFade(withBlock, 0.7f, mA);
                        var paint = new MaterialPropertyBlock(); withBlock.GetPropertyBlock(paint, 0); pr.SetPropertyBlock(paint, 0);   // as Break copies it
                        pc0 = OzShuttleCamFade.ReadFade(pr);
                        clearedN = OzShuttleCamFade.ClearCopiedCut(piece.transform);
                        pc1 = OzShuttleCamFade.ReadFade(pr);
                        pr.GetPropertyBlock(mpb, 0); pieceKept = mpb.GetColor(tcId) == before;
                        OzShuttleCamFade.WriteFade(withBlock, 0f, mA);
                    }
                    finally { Object.DestroyImmediate(piece); }
                }
                C(withBlock && Mathf.Abs(rA - 0.5f) < 1e-4f && Mathf.Abs(rB - 0.5f) < 1e-4f && kept && zA == 0f && zB == 0f && Mathf.Abs(pc0 - 0.7f) < 1e-4f && pc1 == 0f && clearedN >= 1 && pieceKept,
                  $"C7 the cut goes into the blocks the parts have (a painted part {(withBlock ? withBlock.name : "-")}: {rA:0.00}, its team paint {(kept ? "kept" : "LOST")}; a part with none {(without ? without.name : "(none: every part painted)")}: {rB:0.00}) and back to 0 ({zA:0.00} / {zB:0.00}); " +
                  $"a break-up piece built with the hull's block while cut: {pc0:0.00} copied, {pc1:0.00} after the clean-up ({clearedN} block(s)), its paint {(pieceKept ? "kept" : "LOST")}");

                // C8 the numbers the off-engine sweep (out/sweep.txt) used
                bool same = RtsCamera.ZoomClosest == 25f && RtsCamera.FreeFloor == 14f && OzPlayableShuttle.KickPivot == new Vector3(0f, 2.8f, 0f)
                            && OzKickflipCurve.ArcRiseM == 4.8 && OzKickflipCurve.HopPeakM == 0.8 && ZWorld_ShuttleCruiseM() == 12.0;
                C(same, $"C8 the numbers the sweep used are the game's: closest zoom {RtsCamera.ZoomClosest} m, free camera floor {RtsCamera.FreeFloor} m, pivot {OzPlayableShuttle.KickPivot}, arc rise {OzKickflipCurve.ArcRiseM} m, hop {OzKickflipCurve.HopPeakM} m, cruise {ZWorld_ShuttleCruiseM()} m");
                // C9 why: the kickflip's top over the root (measured on the prefab), at plain cruise against the closest camera
                float reach = 12.0f + 0.7f + topMax;
                C(topMax > 11f && reach > RtsCamera.ZoomClosest - NearReach, $"C9 the kickflip lifts the hull's top {topMax:0.00} m over its root (level: 5.15): at plain cruise (12 m + the 0.7 m bob) it reaches {reach:0.00} m, the closest camera stands at {RtsCamera.ZoomClosest} m: without the fade its near plane cuts the hull");
            }
            finally
            {
                if (go) Object.DestroyImmediate(go);
                EditorSceneManager.ClosePreviewScene(scene);
            }
            return Summary(sb, pass, fail);
        }

        static double ZWorld_ShuttleCruiseM() => OpenZ.Sim.ZWorld.ShuttleCruiseM;

        /// <summary>The ship's drawn pose t s into a kickflip at heading head (OzPlayableShuttle.KickPose: the turn round KickPivot, the hop,
        /// the arc up and aside), the root at (0, 0, 0) before it; t below 0 = the level pose.</summary>
        static void Pose(Transform root, float head, double t, int dir)
        {
            var level = Quaternion.Euler(0f, head, 0f);
            if (t < 0) { root.SetPositionAndRotation(Vector3.zero, level); return; }
            OzKickflipCurve.At(t, out double roll, out _, out _, out double pitch, out _, out _, out double hop, out _, out _, out _);
            OzKickflipCurve.Arc(t, out double side, out _, out _, out double rise, out _, out _);
            var piv = OzPlayableShuttle.KickPivot;
            var q = level * (Quaternion.Euler(-(float)pitch, 0f, 0f) * Quaternion.Euler(0f, 0f, (float)roll * dir));
            var p = level * piv - q * piv + Vector3.up * (float)(hop + rise) + level * new Vector3((float)side * dir, 0f, 0f);
            root.SetPositionAndRotation(p, q);
        }

        static string Summary(StringBuilder sb, int pass, int fail)
        {
            sb.AppendLine($"K440 CHECK: {pass} PASS, {fail} FAIL{(fail == 0 ? " (all good)" : "")}");
            return sb.ToString();
        }
    }
}
#endif
