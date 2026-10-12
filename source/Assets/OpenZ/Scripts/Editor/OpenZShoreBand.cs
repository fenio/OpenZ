using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Besides the painted
    /// wet bank (part 02) and the wet-band proxy (part 01), the land within ~1.4 m of the L02 waterline slopes 8-26 deg toward
    /// the water (the level builder's 0.25 m bank dip plus the 1 m grid step down to the bed) and its smooth vertex normals
    /// follow that slope: the bank side facing away from the sun got 0.65 (afternoon) to 0.79 (noon) of the light of flat
    /// ground, measured on the live normals. The bank keeps its shape; only its shading normals turn up:
    /// n' = normalize(lerp(n0, up, k)), n0 = the area-weighted normal of every ground face at that (x, z) position (the Water
    /// corners seam rule, so the meshes stay crease-free), k = 1 within FlatFull m of the waterline on land fading to 0 at
    /// FlatEnd m, and 1 at the waterline fading to 0 at WetEnd m under the water (bed-map shore distance). Only low bank
    /// vertices (at most 0.8 m above the water). Idempotent: n0 is recomputed from the triangles every run. Writes the
    /// writable ground meshes (TER_Ground / TER_RiverBed carved assets, the GND3_FloorPatch seam asset; backed up once to
    /// &lt;repo&gt;/backups/shore_band_fix). Water corners 1 Fix and Rollback call it after they rewrite the normals (their seam
    /// </summary>
    public static class OpenZShoreBand
    {
        const string SceneName = "L02_Desert";
        const string BedMapTex = "Assets/OpenZ/Textures/WaterV4/T_WaterBedMap_L02.png";
        const string PaintTex = "Assets/OpenZ/Textures/L02_ground_paint.png";
        const string WetMatPath = "Assets/OpenZ/Materials/Env/WaterV4/M_Water_L02_v4_Wet.mat";
        static readonly Vector4 MapRect = new Vector4(-24f, -204f, 192f, 228f);   // = _BedRect = the ground paint's planar UV
        const float SdLo = -2f, SdRange = 6f;                                      // _BedEncode.y, .z (+ in the water)
        public const float FlatFull = 1.4f, FlatEnd = 2.0f, WetEnd = 1.2f, MaxAboveWater = 0.8f;
        static string Backup => Path.Combine(OpenZPaths.RepoRoot, "backups", "shore_band_fix");

        [MenuItem("OpenZ/Water v4/4 Shore band: flat bank shading (open L02)")]
        public static void ApplyMenu()
        {
            var log = new StringBuilder("[OpenZ] Shore band fix\n");
            log.Append(FlattenBankNormals());
            AssetDatabase.SaveAssets();
            log.Append(CheckText());
            Debug.Log(log.ToString());
        }

        [MenuItem("OpenZ/Water v4/4 Shore band: check")]
        public static void CheckMenu() => Debug.Log(CheckText());

        /// <summary>Puts the ground mesh assets back from backups/shore_band_fix (the state before the first flattening).
        /// Water corners 1 Fix calls FlattenBankNormals again: after a rollback, do not re-run it (or remove its hook).</summary>
        [MenuItem("OpenZ/Water v4/4 Shore band: rollback bank normals")]
        public static void RollbackMenu()
        {
            var sb = new StringBuilder("[OpenZ] Shore band rollback\n");
            string prj = Path.Combine(OpenZPaths.RepoRoot, "projects", "unity", "OpenZ");
            foreach (var p in new[] { "Assets/OpenZ/Models/Generated/L02_TER_Ground_carved.asset", "Assets/OpenZ/Models/Generated/L02_TER_RiverBed_carved.asset",
                                      "Assets/OpenZ/Models/Generated/L02_GND3_FloorPatch_seam.asset" })
            {
                string b = Path.Combine(Backup, Path.GetFileName(p));
                if (!File.Exists(b)) { sb.Append($"  no backup for {p}\n"); continue; }
                File.Copy(b, Path.Combine(prj, p), true);
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
                sb.Append($"  {p} restored from backups/shore_band_fix\n");
            }
            Debug.Log(sb.ToString());
        }

        /// <summary>The flattening weight at a bed-map shore distance (m, + in the water).</summary>
        public static float FlatK(float sd)
        {
            return sd <= 0f ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FlatFull, FlatEnd, -sd))
                            : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, WetEnd, sd));
        }

        /// <summary>Turns the shading normals of the L02 river banks up (see the class summary). Returns a log.</summary>
        public static string FlattenBankNormals()
        {
            var sb = new StringBuilder();
            if (!L02Open(out var env)) { sb.Append("  shore band: open L02_Desert (edit mode) first\n"); return sb.ToString(); }
            var bed = LoadPng(BedMapTex);
            if (bed == null) { sb.Append($"  shore band: {BedMapTex} missing\n"); return sb.ToString(); }
            var bedPx = bed.GetPixels32(); int bw = bed.width, bh = bed.height;
            Object.DestroyImmediate(bed);
            float waterY = WaterLevel(env);
            var all = Ground(env);
            var mfs = all.Where(f => AssetDatabase.GetAssetPath(f.sharedMesh).EndsWith(".asset") && f.sharedMesh.isReadable).ToList();
            foreach (var f in all.Except(mfs))
                sb.Append($"  shore band: {f.name} uses {AssetDatabase.GetAssetPath(f.sharedMesh)} (not a writable mesh asset): skipped, run OpenZ > Water corners > 1 Fix first\n");
            if (mfs.Count == 0) return sb.ToString();

            Directory.CreateDirectory(Backup);                                   // backups once (the first state this tool saw)
            string prj = Path.Combine(OpenZPaths.RepoRoot, "projects", "unity", "OpenZ");
            foreach (var f in mfs)
            {
                string p = AssetDatabase.GetAssetPath(f.sharedMesh), dst = Path.Combine(Backup, Path.GetFileName(p));
                if (!File.Exists(dst)) File.Copy(Path.Combine(prj, p), dst);
            }

            // n0: the area-weighted normal of every ground face at each (x, z) position, over all meshes (Water corners seam rule)
            var acc = new Dictionary<long, Vector3>();
            var world = new List<Vector3[]>();
            foreach (var f in mfs)
            {
                var m = f.sharedMesh; var tr = f.transform; var v = m.vertices; var ix = m.triangles;
                var w = new Vector3[v.Length];
                for (int i = 0; i < v.Length; i++) w[i] = tr.TransformPoint(v[i]);
                world.Add(w);
                for (int t = 0; t < ix.Length; t += 3)
                {
                    var n = Vector3.Cross(w[ix[t + 1]] - w[ix[t]], w[ix[t + 2]] - w[ix[t]]);
                    for (int q = 0; q < 3; q++) { long key = Key(w[ix[t + q]]); acc[key] = acc.TryGetValue(key, out var s) ? s + n : n; }
                }
            }
            int changed = 0, touched = 0;
            for (int k = 0; k < mfs.Count; k++)
            {
                var m = mfs[k].sharedMesh; var tr = mfs[k].transform; var w = world[k];
                var nrm = m.normals;
                if (nrm == null || nrm.Length != w.Length) { m.RecalculateNormals(); nrm = m.normals; }
                for (int i = 0; i < w.Length; i++)
                {
                    if (w[i].y > waterY + MaxAboveWater) continue;               // low river banks only (never a raised rock foot)
                    float kf = FlatK(Sd(bedPx, bw, bh, w[i].x, w[i].z));
                    if (kf <= 0f) continue;
                    if (!acc.TryGetValue(Key(w[i]), out var n0) || n0.sqrMagnitude < 1e-12f) continue;
                    if (n0.y < 0f) n0 = -n0;
                    var nl = tr.InverseTransformDirection(Vector3.Lerp(n0.normalized, Vector3.up, kf).normalized).normalized;
                    touched++;
                    if (Vector3.Angle(nl, nrm[i]) > 0.01f) changed++;
                    nrm[i] = nl;
                }
                m.normals = nrm;
                EditorUtility.SetDirty(m);
            }
            sb.Append($"  shore band: flat bank shading on {string.Join(" + ", mfs.Select(f => f.name))}: {touched} bank vertices, {changed} normals changed " +
                      $"(k = 1 within {FlatFull} m of the waterline, 0 by {FlatEnd} m on land and by {WetEnd} m under the water; backups in backups/shore_band_fix)\n");
            return sb.ToString();
        }

        /// <summary>1 the wet band is a faint tint, 2 the ground paint has no dark bank, 3 the bank shading normals point up.</summary>
        public static string CheckText()
        {
            var sb = new StringBuilder("[OpenZ] Shore band check\n"); int bad = 0;
            void Line(bool ok, string s) { sb.Append(ok ? "  OK   " : "  FAIL ").Append(s).Append('\n'); if (!ok) bad++; }

            var wm = AssetDatabase.LoadAssetAtPath<Material>(WetMatPath);
            if (wm && wm.HasProperty("_WetColor") && wm.HasProperty("_WetWidth"))
            {
                var c = wm.GetColor("_WetColor"); float lum = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b, wd = wm.GetFloat("_WetWidth");
                Line(lum >= 0.94f && wd <= 0.6f, $"wet band {Path.GetFileName(WetMatPath)}: ({c.r:0.###}, {c.g:0.###}, {c.b:0.###}) = luminance x{lum:0.000}, width {wd:0.00} m (expect >= x0.94 and <= 0.6 m)");
            }
            else Line(false, $"{WetMatPath} with _WetColor / _WetWidth");

            var bed = LoadPng(BedMapTex); var paint = LoadPng(PaintTex);
            if (bed != null && paint != null)
            {
                var bp = bed.GetPixels32(); var pp = paint.GetPixels32();
                int bw = bed.width, bh = bed.height, pw = paint.width, ph = paint.height;
                var near = new List<float>(); var far = new List<float>();
                for (int j = 0; j < bh; j++)
                    for (int i = 0; i < bw; i++)
                    {
                        float sd = SdLo + bp[j * bw + i].g / 255f * SdRange;
                        bool isNear = sd > -0.3f && sd < -0.03f, isFar = sd <= -1.99f && ((i + j) & 7) == 0;
                        if (!isNear && !isFar) continue;
                        var c = pp[Mathf.Min(ph - 1, (int)((j + 0.5f) / bh * ph)) * pw + Mathf.Min(pw - 1, (int)((i + 0.5f) / bw * pw))];
                        float mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b)) / 255f, mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b)) / 255f;
                        if (mx >= 0.66f || (mx - mn) / Mathf.Max(mx, 1e-3f) <= 0.28f) continue;   // brown dirt only (no road, no dune sand)
                        float y = 0.2126f * Mathf.GammaToLinearSpace(c.r / 255f) + 0.7152f * Mathf.GammaToLinearSpace(c.g / 255f) + 0.0722f * Mathf.GammaToLinearSpace(c.b / 255f);
                        (isNear ? near : far).Add(y);
                    }
                Object.DestroyImmediate(paint);
                if (near.Count > 100 && far.Count > 100)
                {
                    near.Sort(); far.Sort();
                    float r = near[near.Count / 2] / Mathf.Max(far[far.Count / 2], 1e-4f);
                    Line(r >= 0.92f, $"ground paint: dirt 0.03-0.3 m from the waterline = x{r:0.00} of the dry dirt (>= 2 m) in linear luminance (expect >= 0.92; the live painted wet bank: x0.46)");
                }
                else Line(false, "ground paint: not enough bank / dry dirt texels to test");
                if (L02Open(out var env))
                {
                    float waterY = WaterLevel(env), maxTilt = 0f; int n = 0;
                    foreach (var f in Ground(env).Where(f => f.sharedMesh.isReadable))
                    {
                        var v = f.sharedMesh.vertices; var nr = f.sharedMesh.normals; var tr = f.transform;
                        if (nr == null || nr.Length != v.Length) continue;
                        for (int i = 0; i < v.Length; i++)
                        {
                            var w = tr.TransformPoint(v[i]);
                            if (w.y > waterY + MaxAboveWater) continue;
                            float sd = Sd(bp, bw, bh, w.x, w.z);
                            if (sd > 0f || sd < -FlatFull) continue;
                            maxTilt = Mathf.Max(maxTilt, Vector3.Angle(tr.TransformDirection(nr[i]), Vector3.up)); n++;
                        }
                    }
                    Line(n > 100 && maxTilt < 2f, $"bank shading normals within {FlatFull} m of the waterline: {n} vertices, max tilt {maxTilt:0.0} deg (expect < 2; live before the fix: ~26)");
                }
                else sb.Append("  --   bank normals: open L02_Desert to test them\n");
            }
            else Line(false, "bed map / ground paint PNG missing");
            if (bed != null) Object.DestroyImmediate(bed);
            sb.Append(bad == 0 ? "  ALL OK" : $"  {bad} problem(s)");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ helpers
        static bool L02Open(out GameObject env)
        {
            env = null;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorSceneManager.GetActiveScene().name != SceneName) return false;
            env = GameObject.Find("Environment");
            return env != null;
        }

        static float WaterLevel(GameObject env)
        {
            var w = env.transform.Find("WAT_Water");
            var r = w ? w.GetComponent<Renderer>() : null;
            return r ? r.bounds.min.y : -0.35f;
        }

        /// <summary>The rendered ground around the river: TER_Ground, TER_RiverBed (carved) and the Cliffs v3 floor patch.</summary>
        static List<MeshFilter> Ground(GameObject env)
        {
            var l = new List<MeshFilter>();
            foreach (var n in new[] { "TER_Ground", "TER_RiverBed" })
            {
                var t = env.transform.Find(n); var f = t ? t.GetComponent<MeshFilter>() : null;
                if (f && f.sharedMesh && f.gameObject.activeInHierarchy) l.Add(f);
            }
            var fp = env.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.name == "GND3_FloorPatch" && f.gameObject.activeInHierarchy && f.sharedMesh);
            if (fp) l.Add(fp);
            return l;
        }

        static Texture2D LoadPng(string path)
        {
            if (!File.Exists(path)) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            t.LoadImage(File.ReadAllBytes(path));
            return t;
        }

        /// <summary>Bed-map shore distance (m, + in the water) at world (x, z), bilinear, like the shaders.</summary>
        static float Sd(Color32[] px, int w, int h, float x, float z)
        {
            float u = (x - MapRect.x) / MapRect.z * w - 0.5f, v = (z - MapRect.y) / MapRect.w * h - 0.5f;
            int i0 = Mathf.Clamp(Mathf.FloorToInt(u), 0, w - 2), j0 = Mathf.Clamp(Mathf.FloorToInt(v), 0, h - 2);
            float fu = Mathf.Clamp01(u - i0), fv = Mathf.Clamp01(v - j0);
            float g00 = px[j0 * w + i0].g, g10 = px[j0 * w + i0 + 1].g, g01 = px[(j0 + 1) * w + i0].g, g11 = px[(j0 + 1) * w + i0 + 1].g;
            float g = Mathf.Lerp(Mathf.Lerp(g00, g10, fu), Mathf.Lerp(g01, g11, fu), fv) / 255f;
            return SdLo + g * SdRange;
        }

        static long Key(Vector3 w) => ((long)Mathf.RoundToInt(w.x * 100f) + 1000000L) * 10000000L + (Mathf.RoundToInt(w.z * 100f) + 1000000L);
    }
}
