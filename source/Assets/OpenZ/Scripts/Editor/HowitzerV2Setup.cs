using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Edits the split imported howitzer prefab (OpenZTurretSplit) in three ways:
    ///  1. turret pivot: the split put TurretYaw at prefab-root x -0.42, but the turntable ring it turns on is centred at x -0.546
    ///     (trimmed circle fits: yaw collar -0.546, base ring -0.544 / -0.550, base plate centre -0.540; tools/hz_glb.py). UnitAim
    ///     turned the turret 0.126 model units (9 cm on a 0.72 tower gun) off its ring, so every idle 45 deg scan step and every aim
    ///     swung the whole turret sideways over its base (up to 18 cm at 180 deg). TurretYaw -> (-0.546, 0.52, 0), its mesh
    ///     shifted by +0.126 x (bounds too), BarrelPitch moved back by the same amount so the trunnion stays exactly where it
    ///     was (TowerMounts.FootprintCentreLocal, fixed in this package, then puts both plate and turn axis on the pad centre);
    ///  2. barrel (skipped by "Apply turret pivot only"; crews_v2's ToyBarrels leaves this mesh alone): BarrelRecoil gets the new chunky barrel (Models/Generated/Source/howitzer_barrel_v2.json, built by
    ///     tools/hz_barrel.py): khaki recoil sleeve r 0.40 out of the cradle collar, dark machined ring, gunmetal tube r 0.34 with a
    ///     fat bore evacuator, dark octagonal two-baffle muzzle brake (0.94 across) with a dark bore; 3.22 long from the trunnion
    ///     (was a 0.21 tube, 2.79 long). Same material (m10_howitzer_mat, UVs on flat colour squares of the clean atlas), bounds
    ///     set so TeamPaint's accent band lies above the barrel (no team smear across the slanted tube). The Muzzle socket moves
    ///     to the new muzzle face (same firing direction);
    ///  3. cradle: the old tube's guide collar (pitch-part triangles more than 0.60 along the tube from the trunnion, 20 tris) is
    ///     dropped: the new sleeve covers it at rest and it would z-fight the tube in recoil.
    /// Idempotent; "Restore original howitzer" puts the split meshes and positions back. After OpenZ/3 Build Prefabs + Split
    /// Cannon Turrets run this again (the split rule itself now uses the right pivot).
    /// </summary>
    public static class HowitzerV2Setup
    {
        const string PrefabPath = "Assets/OpenZ/Prefabs/Z_howitzer.prefab";
        const string SplitAsset = "Assets/OpenZ/Models/Generated/howitzer_turret_split.asset";
        const string PartsAsset = "Assets/OpenZ/Models/Generated/howitzer_v2_parts.asset";
        const string BarrelJson = "Assets/OpenZ/Models/Generated/Source/howitzer_barrel_v2.json";
        public static readonly Vector3 YawPivotOld = new Vector3(-0.42f, 0.52f, 0f), YawPivotV2 = new Vector3(-0.546f, 0.52f, 0f);
        public static readonly Vector3 PitchPivot = new Vector3(-0.18f, 2.08f, 0f);                     // trunnion, prefab root (unchanged)
        static readonly Vector3 PitchLocalOld = new Vector3(0.24f, 1.56f, 0f), MuzzleLocalOld = new Vector3(2.06f, 1.88f, 0f);
        static readonly Vector3 HD = new Vector3(0.7386f, 0.6741f, -0.0049f).normalized;                 // tube axis (OpenZTurretSplit)
        const float CollarU = 0.60f;
        const string YawName = "howitzer_yaw_v2", PitchName = "howitzer_pitch_v2", BarrelName = "howitzer_barrel_v2";

        [Serializable]
        class BarrelData
        {
            public string name;
            public float[] vertices, normals, uv, bounds_center, bounds_size, muzzle_local, axis;
            public int[] triangles;
        }

        static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        // ------------------------------------------------------------------ apply
        [MenuItem("OpenZ/Howitzer v2/1 Apply (turret pivot + chunky barrel)")]
        public static string Apply() => Apply(true);

        /// <summary>Only the turret pivot fix (keeps whatever barrel the prefab has; crews_v2's ToyBarrels then fattens the imported tube).</summary>
        [MenuItem("OpenZ/Howitzer v2/Apply turret pivot only (keep the toy barrel)")]
        public static string ApplyPivotOnly() => Apply(false);

        public static string Apply(bool withBarrel)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return Log("stop Play first", true);
            if (!File.Exists(BarrelJson)) return Log("missing " + BarrelJson + " (copy the package files first)", true);
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var yaw = root.transform.Find("TurretYaw"); var pitch = yaw ? yaw.Find("BarrelPitch") : null;
                var recoil = pitch ? pitch.Find("BarrelRecoil") : null; var muzzle = recoil ? recoil.Find("Muzzle") : null;
                if (!yaw || !pitch || !recoil || !muzzle) return Log("Z_howitzer is not split (TurretYaw > BarrelPitch > BarrelRecoil > Muzzle): run OpenZ/Split Cannon Turrets first", true);
                var yawMF = yaw.GetComponent<MeshFilter>(); var pitchMF = pitch.GetComponent<MeshFilter>(); var recMF = recoil.GetComponent<MeshFilter>();
                if (!yawMF || !yawMF.sharedMesh || !pitchMF || !pitchMF.sharedMesh || !recMF) return Log("split part meshes missing", true);
                if (Mathf.Abs(yaw.localPosition.y - YawPivotV2.y) > 0.01f) return Log($"unexpected TurretYaw height {yaw.localPosition.y:F3} (expected 0.52): model changed, not applied", true);
                bool pivotDone = yawMF.sharedMesh.name == YawName && (yaw.localPosition - YawPivotV2).sqrMagnitude < 1e-8f;
                bool barrelDone = pitchMF.sharedMesh.name == PitchName && recMF.sharedMesh && recMF.sharedMesh.name == BarrelName;
                if (pivotDone && (barrelDone || !withBarrel)) return Log($"already applied (TurretYaw at -0.546{(barrelDone ? ", barrel v2" : "")}): nothing to do");

                // 1. turret pivot (keep the trunnion where it is)
                var shift = yaw.localPosition - YawPivotV2; shift.y = 0f;                                // v_new = v_old + (old pivot - new pivot)
                var yawSrc = yawMF.sharedMesh;
                var yawM = Object.Instantiate(yawSrc); yawM.name = YawName;
                if (shift.sqrMagnitude > 1e-10f)
                {
                    var vs = yawM.vertices; for (int i = 0; i < vs.Length; i++) vs[i] += shift;
                    yawM.vertices = vs;
                }
                yawM.bounds = new Bounds(yawSrc.bounds.center + shift, yawSrc.bounds.size);             // TeamPaint band = whole-model bounds, kept
                // 2. cradle without the old tube's guide collar
                var pitchSrc = pitchMF.sharedMesh;
                var pitchM = Object.Instantiate(pitchSrc); pitchM.name = PitchName;
                int dropped = 0;
                if (withBarrel && pitchSrc.name != PitchName)
                {
                    var pv = pitchM.vertices;
                    for (int s = 0; s < pitchM.subMeshCount; s++)
                    {
                        var t = pitchM.GetTriangles(s); var keep = new List<int>(t.Length);
                        for (int i = 0; i + 2 < t.Length; i += 3)
                        {
                            var c = (pv[t[i]] + pv[t[i + 1]] + pv[t[i + 2]]) / 3f;                          // pitch-local = root - trunnion
                            if (Vector3.Dot(c, HD) > CollarU) { dropped++; continue; }
                            keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]);
                        }
                        pitchM.SetTriangles(keep, s, false);
                    }
                    pitchM.bounds = pitchSrc.bounds;
                }
                // 3. the chunky barrel
                BarrelData data = null; Mesh barrel = null;
                if (withBarrel)
                {
                    barrel = BuildBarrel(out data);
                    if (!barrel) return Log("could not read " + BarrelJson, true);
                }

                // store: one asset with the three meshes (re-created; the prefab is re-pointed right below)
                AssetDatabase.DeleteAsset(PartsAsset);
                AssetDatabase.CreateAsset(yawM, PartsAsset);
                AssetDatabase.AddObjectToAsset(pitchM, PartsAsset);
                if (barrel) AssetDatabase.AddObjectToAsset(barrel, PartsAsset);

                var pitchWorldBefore = root.transform.InverseTransformPoint(pitch.position);
                yaw.localPosition = YawPivotV2;
                pitch.localPosition += shift;
                yawMF.sharedMesh = yawM; pitchMF.sharedMesh = pitchM;
                var mz = muzzle.localPosition;
                if (barrel)
                {
                    recMF.sharedMesh = barrel;
                    mz = new Vector3(data.muzzle_local[0], data.muzzle_local[1], data.muzzle_local[2]);
                    muzzle.localPosition = mz;
                }
                var pitchWorldAfter = root.transform.InverseTransformPoint(pitch.position);
                float fireErr = Vector3.Angle(root.transform.InverseTransformDirection(-muzzle.forward), HD);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                return Log($"applied: TurretYaw {YawPivotOld.x:F3} -> {YawPivotV2.x:F3} (shift {shift.x:+0.000;-0.000}), trunnion moved {(pitchWorldAfter - pitchWorldBefore).magnitude * 1000f:F1} mm, " +
                           $"collar tris dropped {dropped}, " + (barrel ? $"barrel {barrel.vertexCount} verts / {barrel.triangles.Length / 3} tris" : "barrel kept (" + (recMF.sharedMesh ? recMF.sharedMesh.name : "none") + ")") + $", muzzle {mz}, fire direction vs tube axis {fireErr:F2} deg");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static Mesh BuildBarrel(out BarrelData d)
        {
            d = JsonUtility.FromJson<BarrelData>(File.ReadAllText(BarrelJson));
            if (d == null || d.vertices == null || d.triangles == null) return null;
            int nv = d.vertices.Length / 3;
            var V = new Vector3[nv]; var N = new Vector3[nv]; var U = new Vector2[nv];
            for (int i = 0; i < nv; i++)
            {
                V[i] = new Vector3(d.vertices[i * 3], d.vertices[i * 3 + 1], d.vertices[i * 3 + 2]);
                N[i] = new Vector3(d.normals[i * 3], d.normals[i * 3 + 1], d.normals[i * 3 + 2]);
                U[i] = new Vector2(d.uv[i * 2], d.uv[i * 2 + 1]);
            }
            var m = new Mesh { name = BarrelName };
            m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, U);
            m.SetTriangles(d.triangles, 0, false);
            // TeamPaint: accent band = [min.y + 0.5 size.y, min.y + 0.62 size.y] of these bounds = above the muzzle (no team colour here)
            m.bounds = new Bounds(new Vector3(d.bounds_center[0], d.bounds_center[1], d.bounds_center[2]), new Vector3(d.bounds_size[0], d.bounds_size[1], d.bounds_size[2]));
            m.RecalculateTangents();
            return m;
        }

        // ------------------------------------------------------------------ restore
        [MenuItem("OpenZ/Howitzer v2/Restore original howitzer")]
        public static string Restore()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return Log("stop Play first", true);
            var split = AssetDatabase.LoadAllAssetsAtPath(SplitAsset).OfType<Mesh>().ToDictionary(m => m.name);
            if (!split.TryGetValue("howitzer_yaw", out var y0) || !split.TryGetValue("howitzer_pitch", out var p0) || !split.TryGetValue("howitzer_recoil", out var r0))
                return Log("split meshes not found in " + SplitAsset, true);
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var yaw = root.transform.Find("TurretYaw"); var pitch = yaw ? yaw.Find("BarrelPitch") : null;
                var recoil = pitch ? pitch.Find("BarrelRecoil") : null; var muzzle = recoil ? recoil.Find("Muzzle") : null;
                if (!yaw || !pitch || !recoil || !muzzle) return Log("Z_howitzer is not split", true);
                yaw.localPosition = YawPivotOld; pitch.localPosition = PitchLocalOld; muzzle.localPosition = MuzzleLocalOld;
                yaw.GetComponent<MeshFilter>().sharedMesh = y0; pitch.GetComponent<MeshFilter>().sharedMesh = p0; recoil.GetComponent<MeshFilter>().sharedMesh = r0;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                return Log("restored the split howitzer (TurretYaw -0.42, old tube, old muzzle); " + PartsAsset + " kept (unused)");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // ------------------------------------------------------------------ check
        /// <summary>Prefab: pivot vs the turntable ring (circle fit of the yaw collar in TurretYaw space: centre must be at 0, 0), barrel,
        /// muzzle. Plinth mesh numbers (compare with the package preview). In Play also every tower cannon of the open level:
        /// turn axis vs pad centre (3 cm), root on the plinth top (3 cm), rest heading = tower diagonal (1 deg), base plate centre
        /// from its own vertices vs pad centre (3 cm), plinth present.
        /// </summary>
        [MenuItem("OpenZ/Howitzer v2/Check (prefab + tower seats)")]
        public static string Check()
        {
            var sb = new StringBuilder(); int fail = 0;
            void Row(bool ok, string what) { sb.AppendLine($"| {(ok ? "PASS" : "FAIL")} | {what} |"); if (!ok) fail++; }
            sb.AppendLine($"# howitzer_towers_v1 check ({DateTime.Now:dd MMM HH:mm}, {(Application.isPlaying ? "Play" : "edit")})\n");
            sb.AppendLine("| status | check |\n|---|---|");
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var yaw = pf ? pf.transform.Find("TurretYaw") : null;
            var pitch = yaw ? yaw.Find("BarrelPitch") : null; var recoil = pitch ? pitch.Find("BarrelRecoil") : null; var muzzle = recoil ? recoil.Find("Muzzle") : null;
            if (!yaw || !pitch || !recoil || !muzzle) Row(false, "Z_howitzer split nodes present");
            else
            {
                Row((yaw.localPosition - YawPivotV2).magnitude < 0.002f, $"TurretYaw pivot {yaw.localPosition.x:F3}, {yaw.localPosition.y:F3}, {yaw.localPosition.z:F3} (want -0.546, 0.52, 0)");
                var trunnion = yaw.localPosition + pitch.localPosition;
                Row((trunnion - PitchPivot).magnitude < 0.002f, $"trunnion (BarrelPitch) at root {trunnion.x:F3}, {trunnion.y:F3} (want -0.18, 2.08)");
                var ymf = yaw.GetComponent<MeshFilter>(); var ym = ymf ? ymf.sharedMesh : null;
                var bt = pf.transform.Find("Base"); var bmf = bt ? bt.GetComponent<MeshFilter>() : null; var basem = bmf ? bmf.sharedMesh : null;
                if (ym && ym.isReadable)
                {   // the collar the turret stands on: TurretYaw-space vertices at root y 0.52 .. 0.70, trimmed circle fit (keeps the best half)
                    var (cx, cz, r) = TrimmedFit(ym.vertices.Where(v => v.y >= 0.0f && v.y < 0.18f).Select(v => new Vector2(v.x, v.z)).ToList());
                    Row(Mathf.Abs(cx) < 0.02f && Mathf.Abs(cz) < 0.02f, $"turret collar centre in TurretYaw space ({cx:+0.000;-0.000}, {cz:+0.000;-0.000}) r {r:F3}: the turret turns about its own ring (tol 2 cm model)");
                }
                if (basem && basem.isReadable)
                {   // the ring on the base the turret sits on (root y 0.40 .. 0.54)
                    var (bx, bz, br) = TrimmedFit(basem.vertices.Where(v => v.y >= 0.40f && v.y < 0.54f).Select(v => new Vector2(v.x, v.z)).ToList());
                    Row(Mathf.Abs(bx - yaw.localPosition.x) < 0.02f && Mathf.Abs(bz - yaw.localPosition.z) < 0.02f, $"base ring centre ({bx:+0.000;-0.000}, {bz:+0.000;-0.000}) r {br:F3} vs TurretYaw pivot ({yaw.localPosition.x:+0.000;-0.000}, {yaw.localPosition.z:+0.000;-0.000}): turret and base concentric (tol 2 cm model)");
                }
                var rmf = recoil.GetComponent<MeshFilter>(); var bm = rmf ? rmf.sharedMesh : null;
                bool v2 = bm && bm.name == BarrelName;
                sb.AppendLine($"| INFO | BarrelRecoil mesh {(bm ? bm.name : "none")} ({(bm ? bm.vertexCount : 0)} verts): {(v2 ? "chunky barrel v2" : "imported tube (crews_v2 ToyBarrels fattens it at run time)")} |");
                var mz = muzzle.localPosition;
                if (v2) Row(Mathf.Abs(Vector3.Dot(mz, HD) - 3.22f) < 0.02f, $"Muzzle at {Vector3.Dot(mz, HD):F3} along the tube (want 3.22 = the new brake face)");
                float fe = Vector3.Angle(pf.transform.InverseTransformDirection(-muzzle.forward), HD);
                Row(fe < 0.5f, $"Muzzle fires along the tube axis ({fe:F2} deg)");
            }
            // plinth mesh (same numbers as tools/hz_after.py)
            var pm = TowerMounts.MountMesh();
            sb.AppendLine($"\nPlinth mesh: {pm.vertexCount} verts, steel {pm.GetTriangles(0).Length / 3} / dark {pm.GetTriangles(1).Length / 3} tris, bounds {pm.bounds.min:F3} .. {pm.bounds.max:F3} " +
                          $"(pad top {TowerMounts.PadTopY}, plinth top +{TowerMounts.MountHeight}, drum r {TowerMounts.MountRadius}, foot r {TowerMounts.PlinthFootRadius} to -{TowerMounts.PlinthFootDepth}, ledge -{TowerMounts.LedgeDrop}).");
            Row(pm.subMeshCount == 2 && Mathf.Abs(pm.bounds.max.y - TowerMounts.MountHeight) < 0.005f, $"plinth top at +{pm.bounds.max.y:F3} = MountHeight (the gun root sits {TowerMounts.SeatSink} m into it)");
            Row(pm.bounds.min.y < -TowerMounts.LedgeDrop - 0.05f, $"plinth foot {pm.bounds.min.y:F3} reaches >= 5 cm into the ledge (-{TowerMounts.LedgeDrop}): stands on the tower cap, never hovers");
            Row(TowerMounts.LedgeRadius - 0.25f > TowerMounts.PlinthFootRadius, $"gunner hatch r {TowerMounts.LedgeRadius} clears the flange r {TowerMounts.PlinthFootRadius} by more than a crew body (0.25 m)");
            Object.DestroyImmediate(pm);

            string level = SceneManager.GetActiveScene().name;
            if (Application.isPlaying && ZGame.I && ZGame.I.World != null)
            {
                var g = ZGame.I; int n = 0;
                sb.AppendLine("\n| status | tower cannon | pad | axis off (cm) | height off (cm) | heading off (deg) | base centre off (cm) | plinth |\n|---|---|---|---|---|---|---|---|");
                var mounts = GameObject.Find("OZ_TowerMounts");
                foreach (var kv in g.Views)
                {
                    var view = kv.Value; if (!view || view.Obj == null || view.Obj.Kind != ZKind.Cannon) continue;
                    if (TowerMounts.Resolve(g, view.Obj, out var fort, out int pad) != 1) continue;
                    n++;
                    var t = view.transform;
                    var turret = t.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name.EndsWith("TurretYaw"));
                    var seat = TowerMounts.SeatWorld(fort, pad);
                    var ax = turret ? turret.position : t.position;
                    float axisOff = new Vector2(ax.x - seat.x, ax.z - seat.z).magnitude * 100f;
                    float hOff = (t.position.y - seat.y) * 100f;
                    float want = TowerMounts.OutwardHeading(fort, pad) + TowerMounts.RestYawOffset(view.Obj.Type);
                    float hdg = Mathf.Abs(Mathf.DeltaAngle(t.eulerAngles.y, want));
                    var bt = t.Find("Base"); var bmf2 = bt ? bt.GetComponent<MeshFilter>() : null;
                    float baseOff = -1f;
                    if (bmf2 && bmf2.sharedMesh && bmf2.sharedMesh.isReadable)
                    {   // the plate's own geometry (vertices; mesh.bounds is the whole model on split parts)
                        var bvs = bmf2.sharedMesh.vertices; Vector3 lo = bvs[0], hi = bvs[0];
                        foreach (var bv in bvs) { lo = Vector3.Min(lo, bv); hi = Vector3.Max(hi, bv); }
                        var bc = bt.TransformPoint((lo + hi) * 0.5f);
                        baseOff = new Vector2(bc.x - seat.x, bc.z - seat.z).magnitude * 100f;
                    }
                    bool plinth = mounts && mounts.transform.Cast<Transform>().Any(m => m.gameObject.activeInHierarchy && new Vector2(m.position.x - seat.x, m.position.z - seat.z).magnitude < 0.05f);
                    bool ok = axisOff <= 3f && Mathf.Abs(hOff) <= 3f && hdg <= 1f && plinth && baseOff >= 0f && baseOff <= 3f;
                    sb.AppendLine($"| {(ok ? "PASS" : "FAIL")} | {t.name} ({view.Obj.Type}) | {fort.name} {pad} | {axisOff:F1} | {hOff:+0.0;-0.0} | {hdg:F2} | {(baseOff < 0 ? "-" : baseOff.ToString("F1"))} | {(plinth ? "yes" : "NO")} |");
                    if (!ok) fail++;
                }
                if (n == 0) { sb.AppendLine("| FAIL | no tower cannon found in this level |"); fail++; }
            }
            else sb.AppendLine("\n(Edit mode: the tower seats are checked in Play; run this menu again while the level plays.)");
            sb.AppendLine($"\nResult: {(fail == 0 ? "CHECK OK" : fail + " FAIL")}");
            try
            {
                string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "..", "work", "records", "qc"));
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"howitzer_towers_check{(Application.isPlaying ? "_play" : "")}_{(string.IsNullOrEmpty(level) ? "none" : level)}.md");
                File.WriteAllText(path, sb.ToString());
                sb.AppendLine("-> " + path);
            }
            catch (Exception e) { sb.AppendLine("(report not written: " + e.Message + ")"); }
            return Log(sb.ToString(), fail > 0);
        }

        /// <summary>Circle fit that keeps refitting on the best half of the points (drops cheeks, bolts and struts in the slice).</summary>
        static (float x, float z, float r) TrimmedFit(List<Vector2> p)
        {
            if (p.Count < 8) return (99f, 99f, 0f);
            var q = p;
            for (int it = 0; it < 6; it++)
            {
                var (cx, cz, r) = CircleFit(q);
                var res = p.Select(v => Mathf.Abs((v - new Vector2(cx, cz)).magnitude - r)).ToList();
                float th = Mathf.Max(0.01f, res.OrderBy(x => x).ElementAt(p.Count / 2));
                q = p.Where((v, i) => res[i] <= th).ToList();
            }
            return CircleFit(q);
        }

        static (float x, float z, float r) CircleFit(List<Vector2> p)
        {
            // least squares x^2 + z^2 = 2ax + 2bz + c
            double sxx = 0, sxz = 0, szz = 0, sx = 0, sz = 0, n = p.Count, sxw = 0, szw = 0, sw = 0;
            foreach (var q in p) { double x = q.x, z = q.y, w = x * x + z * z; sxx += x * x; sxz += x * z; szz += z * z; sx += x; sz += z; sxw += x * w; szw += z * w; sw += w; }
            var A = new double[3, 3] { { 2 * sxx, 2 * sxz, sx }, { 2 * sxz, 2 * szz, sz }, { 2 * sx, 2 * sz, n } };
            var b = new double[] { sxw, szw, sw };
            var s = Solve3(A, b);
            double r = Math.Sqrt(Math.Max(0, s[2] + s[0] * s[0] + s[1] * s[1]));
            return ((float)s[0], (float)s[1], (float)r);
        }

        static double[] Solve3(double[,] A, double[] b)
        {
            int n = 3; var M = new double[n, n + 1];
            for (int i = 0; i < n; i++) { for (int j = 0; j < n; j++) M[i, j] = A[i, j]; M[i, n] = b[i]; }
            for (int c = 0; c < n; c++)
            {
                int piv = c; for (int r = c + 1; r < n; r++) if (Math.Abs(M[r, c]) > Math.Abs(M[piv, c])) piv = r;
                for (int k = 0; k <= n; k++) { var tmp = M[c, k]; M[c, k] = M[piv, k]; M[piv, k] = tmp; }
                if (Math.Abs(M[c, c]) < 1e-12) return new double[] { 0, 0, 0 };
                for (int r = 0; r < n; r++)
                {
                    if (r == c) continue; double f = M[r, c] / M[c, c];
                    for (int k = c; k <= n; k++) M[r, k] -= f * M[c, k];
                }
            }
            return new[] { M[0, n] / M[0, 0], M[1, n] / M[1, 1], M[2, n] / M[2, 2] };
        }

        static string Log(string msg, bool error = false)
        {
            if (error) Debug.LogError("[OpenZ] Howitzer v2: " + msg); else Debug.Log("[OpenZ] Howitzer v2: " + msg);
            return msg;
        }
    }
}
