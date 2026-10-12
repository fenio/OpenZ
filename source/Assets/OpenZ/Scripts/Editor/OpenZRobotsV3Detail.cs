using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// The v3
    /// robots keep their models, rigs, clips, sockets and palette colours; their painted faces get a real UV layout and three 2048
    /// maps each (Textures/Detail/BS01V3D_&lt;Type&gt;_Albedo / _Mask / _Normal) on one material per type, M_BS01D_&lt;Type&gt; (TeamLit
    /// mode 3 like M_BS01_V3, + mask map and normal map). The Tough's bazooka is the oversized one.
    /// Menus (edit mode):
    ///  1 Apply: texture import settings (BC7 albedo / mask, BC5 normal map, mipmaps, aniso 8, 2048), the FBX material remap
    ///    M_BS01D_&lt;Type&gt; -> the material, the prefab renderers' material slots (only the slots whose FBX material is M_BS01D_*;
    ///    the BS-01 head and every glow slot are left as they are), then the check below.
    ///  2 Electrobot: the same for the Electrobot, after electrobot_v1 and its importer are in.
    ///  3 Check: materials and textures, the team mask read back from the albedo through the imported meshes' UVs (vs the
    ///    package's numbers), the emissive slots (laser green, Electrobot blue), the bazooka sockets, the Tough's WeaponHold hold.
    /// head (saddle cushion + WeaponHold values + bazookaElbowOut, apply.py), the sniper's boonie hat (brown cloth, not team colour).
    ///  4 / 5 Lineup (Play mode, day / night): RobotsV3DetailLineup.
    /// </summary>
    public static class OpenZRobotsV3Detail
    {
        const string Dir = "Assets/OpenZ/Models/RobotsV3", Tex = Dir + "/Textures/Detail", Mat = "Assets/OpenZ/Materials/Units", Pref = "Assets/OpenZ/Prefabs",
                     Expect = Tex + "/robots_v3_detail_expect.json";
        public static readonly string[] Types = { "tough", "psycho", "sniper", "pyro", "laser" };
        static string Cap(string t) => char.ToUpper(t[0]) + t.Substring(1);

        [MenuItem("OpenZ/Robots v3 detail: 1 Apply (tough, psycho, sniper, pyro, laser)")]
        public static string Apply() => Run(Types, true);

        [MenuItem("OpenZ/Robots v3 detail: 2 Electrobot (after electrobot_v1)")]
        public static string ApplyElectrobot() => Run(new[] { "electrobot" }, true);

        [MenuItem("OpenZ/Robots v3 detail: 3 Check (team masks, emissives, sockets)")]
        public static string Check()
        {
            var t = Types.ToList();
            if (File.Exists($"{Dir}/BS01V3_Electrobot.fbx") && File.Exists($"{Mat}/M_BS01D_Electrobot.mat")) t.Add("electrobot");
            return Run(t.ToArray(), false);
        }

        [MenuItem("OpenZ/Robots v3 detail: 4 Lineup (Play mode, day)")]
        public static void LineupDay() { if (!EditorApplication.isPlaying) Debug.LogWarning("[OpenZRobotsV3Detail] enter Play mode first"); else RobotsV3DetailLineup.Begin(false); }

        [MenuItem("OpenZ/Robots v3 detail: 5 Lineup (Play mode, night)")]
        public static void LineupNight() { if (!EditorApplication.isPlaying) Debug.LogWarning("[OpenZRobotsV3Detail] enter Play mode first"); else RobotsV3DetailLineup.Begin(true); }

        // ------------------------------------------------------------------------------------------------ expectations (package json)
        [System.Serializable] public class Exp { public string type; public float team_share; public float team_tol; public string[] glow_body; public string[] glow_weapon;
                                                 public float muzzle_dist; public float backblast_dist; public HoldExp hold; }
        /// <summary></summary>
        [System.Serializable] public class HoldExp { public float bazookaRestUp = float.NaN, bazookaRestOut, bazookaHeadClear, bazookaRestAlong, bazookaTubeAxisY, carryLift, bazookaElbowOut; }
        [System.Serializable] class ExpList { public Exp[] types; }
        static Dictionary<string, Exp> LoadExp()
        {
            var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(Expect);
            if (!ta) return new Dictionary<string, Exp>();
            var l = JsonUtility.FromJson<ExpList>(ta.text);
            return (l?.types ?? new Exp[0]).ToDictionary(e => e.type, e => e);
        }

        static string Run(string[] types, bool apply)
        {
            var rep = new StringBuilder(); int fails = 0;
            var exp = LoadExp();
            rep.AppendLine($"# robots_v3_detail {(apply ? "apply + check" : "check")}, {System.DateTime.Now:yyyy-MM-dd HH:mm}");
            rep.AppendLine();
            foreach (var t in types)
            {
                var lines = new List<string>(); bool ok = true;
                string T = Cap(t), body = $"{Dir}/BS01V3_{T}.fbx", weap = $"{Dir}/BS01V3_Weapon_{T}.fbx", matP = $"{Mat}/M_BS01D_{T}.mat", matN = $"M_BS01D_{T}";
                string alb = $"{Tex}/BS01V3D_{T}_Albedo.png", msk = $"{Tex}/BS01V3D_{T}_Mask.png", nrm = $"{Tex}/BS01V3D_{T}_Normal.png";
                var m = AssetDatabase.LoadAssetAtPath<Material>(matP);
                if (!m) { rep.AppendLine($"## {t}: FAIL {matP} missing"); fails++; continue; }
                ok &= TexSettings(alb, true, false, apply, lines) & TexSettings(msk, false, false, apply, lines) & TexSettings(nrm, false, true, apply, lines);
                ok &= MatCheck(m, alb, msk, nrm, apply, lines);
                ok &= Remap(body, matN, m, apply, lines) & Remap(weap, matN, m, apply, lines);
                if (apply) FixPrefab(t, body, weap, lines);
                ok &= PrefabCheck(t, body, weap, m, exp.TryGetValue(t, out var e0) ? e0 : null, lines);
                ok &= MaskCheck(t, body, weap, matN, alb, exp.TryGetValue(t, out var e1) ? e1 : null, lines);
                if (t == "tough") ok &= SocketCheck(weap, exp.TryGetValue(t, out var e2) ? e2 : null, lines);
                if (t == "tough") ok &= HoldCheck(exp.TryGetValue(t, out var e3) ? e3 : null, lines);
                rep.AppendLine($"## {t}: {(ok ? "OK" : "FAIL")}");
                foreach (var l in lines) rep.AppendLine("- " + l);
                rep.AppendLine();
                if (!ok) fails++;
            }
            AssetDatabase.SaveAssets();
            string verdict = fails == 0 ? "CHECK OK" : $"CHECK FAILED ({fails})";
            rep.AppendLine(verdict);
            try
            {
                var root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", ".."));
                var dir = Path.Combine(root, "work", "records", "qc"); Directory.CreateDirectory(dir);
                var p = Path.Combine(dir, $"robots_v3_detail_check_{System.DateTime.Now:HHmm}.md");
                File.WriteAllText(p, rep.ToString()); rep.AppendLine("report: " + p);
            }
            catch (System.Exception ex) { rep.AppendLine("report not written: " + ex.Message); }
            var s = rep.ToString();
            if (fails == 0) Debug.Log("[OpenZRobotsV3Detail]\n" + s); else Debug.LogError("[OpenZRobotsV3Detail]\n" + s);
            return s;
        }

        // ------------------------------------------------------------------------------------------------ textures
        static bool TexSettings(string path, bool srgb, bool normal, bool apply, List<string> lines)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) { lines.Add($"{Path.GetFileName(path)}: MISSING"); return false; }
            var ps = ti.GetPlatformTextureSettings("Standalone");
            bool fmt = normal ? (!ps.overridden || ps.format == TextureImporterFormat.BC5 || ps.format == TextureImporterFormat.Automatic)
                              : (ps.overridden && ps.format == TextureImporterFormat.BC7);
            bool ok = ti.sRGBTexture == srgb && ti.mipmapEnabled && ti.maxTextureSize >= 2048 && ti.anisoLevel >= 8 && fmt
                      && ti.textureType == (normal ? TextureImporterType.NormalMap : TextureImporterType.Default)
                      && ti.textureCompression == TextureImporterCompression.CompressedHQ && !ti.crunchedCompression;
            if (!ok && apply)
            {
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = srgb; ti.mipmapEnabled = true; ti.maxTextureSize = 2048; ti.anisoLevel = 8; ti.filterMode = FilterMode.Trilinear;
                ti.alphaSource = normal ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = false;
                ti.wrapMode = TextureWrapMode.Clamp; ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.crunchedCompression = false;
                var s = new TextureImporterPlatformSettings { name = "Standalone", overridden = !normal, maxTextureSize = 2048,
                    format = normal ? TextureImporterFormat.Automatic : TextureImporterFormat.BC7, textureCompression = TextureImporterCompression.CompressedHQ, compressionQuality = 100 };
                ti.SetPlatformTextureSettings(s);
                ti.SaveAndReimport(); ok = true;
                lines.Add($"{Path.GetFileName(path)}: import settings set");
            }
            var tx = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            lines.Add($"{Path.GetFileName(path)}: {(tx ? $"{tx.width}x{tx.height} {tx.format} mips {tx.mipmapCount}" : "-")} sRGB {ti.sRGBTexture} {(ok ? "ok" : "WRONG SETTINGS")}");
            return ok && tx;
        }

        static bool MatCheck(Material m, string alb, string msk, string nrm, bool apply, List<string> lines)
        {
            var a = AssetDatabase.LoadAssetAtPath<Texture2D>(alb); var k = AssetDatabase.LoadAssetAtPath<Texture2D>(msk); var n = AssetDatabase.LoadAssetAtPath<Texture2D>(nrm);
            bool ok = m.shader && m.shader.name == "OpenZ/TeamLit" && m.GetTexture("_BaseMap") == a && m.GetTexture("_MaskMap") == k && m.GetTexture("_BumpMap") == n
                      && Mathf.Approximately(m.GetFloat("_TeamMode"), 3f) && m.GetFloat("_UseMaskMap") > 0.5f && m.GetFloat("_UseNormalMap") > 0.5f && m.GetFloat("_UnitLook") > 0.5f;
            if (!ok && apply && m.shader && m.shader.name == "OpenZ/TeamLit")
            {
                m.SetTexture("_BaseMap", a); m.SetTexture("_MaskMap", k); m.SetTexture("_BumpMap", n);
                m.SetFloat("_TeamMode", 3f); m.SetFloat("_UseMaskMap", 1f); m.SetFloat("_UseNormalMap", 1f); m.SetFloat("_UnitLook", 1f);
                EditorUtility.SetDirty(m); ok = true; lines.Add($"{m.name}: textures / flags set");
            }
            lines.Add($"{m.name}: shader {(m.shader ? m.shader.name : "-")}, team mode {m.GetFloat("_TeamMode")}, mask map {m.GetFloat("_UseMaskMap")}, normal map {m.GetFloat("_UseNormalMap")}, vertex AO {m.GetFloat("_UseVertexColor")} {(ok ? "ok" : "WRONG")}");
            return ok;
        }

        static bool Remap(string fbx, string matName, Material m, bool apply, List<string> lines)
        {
            if (!(AssetImporter.GetAtPath(fbx) is ModelImporter mi)) { lines.Add($"{Path.GetFileName(fbx)}: MISSING"); return false; }
            var map = mi.GetExternalObjectMap();
            var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), matName);
            bool ok = map.TryGetValue(id, out var o) && o == m;
            if (!ok && apply) { mi.AddRemap(id, m); mi.SaveAndReimport(); ok = true; lines.Add($"{Path.GetFileName(fbx)}: remap {matName} -> {m.name} added"); }
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var used = go ? go.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(x => x).Select(x => x.name).Distinct().ToArray() : new string[0];
            bool inFbx = used.Contains(m.name);
            lines.Add($"{Path.GetFileName(fbx)}: materials [{string.Join(", ", used)}] {(ok && inFbx ? "ok" : "REMAP MISSING")}");
            return ok && inFbx;
        }

        // ------------------------------------------------------------------------------------------------ prefab
        static IEnumerable<(Renderer r, Mesh mesh)> MeshRenderers(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                Mesh me = r is SkinnedMeshRenderer s ? s.sharedMesh : r.TryGetComponent<MeshFilter>(out var f) ? f.sharedMesh : null;
                if (me) yield return (r, me);
            }
        }

        static Material[] FbxMaterials(string fbx, Mesh mesh)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (!go) return null;
            foreach (var (r, me) in MeshRenderers(go)) if (me == mesh) return r.sharedMaterials;
            return null;
        }

        static void FixPrefab(string t, string body, string weap, List<string> lines)
        {
            string p = $"{Pref}/Z_bs01_{t}.prefab";
            if (!File.Exists(p)) { lines.Add($"{Path.GetFileName(p)}: not found (run the type's importer first)"); return; }
            var root = PrefabUtility.LoadPrefabContents(p); int n = 0;
            try
            {
                foreach (var (r, me) in MeshRenderers(root))
                {
                    string mp = AssetDatabase.GetAssetPath(me);
                    if (mp != body && mp != weap) continue;
                    var src = FbxMaterials(mp, me); if (src == null) continue;
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length && i < src.Length; i++)
                        if (src[i] && src[i].name.StartsWith("M_BS01D_") && mats[i] != src[i]) { mats[i] = src[i]; n++; }
                    r.sharedMaterials = mats;
                }
                if (n > 0) PrefabUtility.SaveAsPrefabAsset(root, p);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            lines.Add($"{Path.GetFileName(p)}: {n} material slot(s) set to the detail material");
        }

        static bool PrefabCheck(string t, string body, string weap, Material m, Exp e, List<string> lines)
        {
            string p = $"{Pref}/Z_bs01_{t}.prefab";
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (!pf) { lines.Add($"{Path.GetFileName(p)}: MISSING"); return false; }
            bool ok = true; int found = 0;
            var glow = new List<string>();
            foreach (var (r, me) in MeshRenderers(pf))
            {
                string mp = AssetDatabase.GetAssetPath(me);
                if (mp != body && mp != weap) continue;
                found++;
                var mats = r.sharedMaterials;
                var names = mats.Select(x => x ? x.name : "null").ToArray();
                bool hasDetail = mats.Contains(m), stale = names.Any(x => x == "M_BS01_V3");
                if (!hasDetail || stale) ok = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var x = mats[i]; if (!x) continue;
                    if (x.name.Contains("Glow"))
                    {
                        int tris = i < me.subMeshCount ? (int)(me.GetIndexCount(i) / 3) : -1;
                        bool hdr = x.HasProperty("_BaseColor") && x.GetColor("_BaseColor").maxColorComponent > 1f;
                        glow.Add($"{x.name} ({x.shader.name}, {tris} tris{(hdr ? ", HDR" : "")})");
                    }
                }
                lines.Add($"prefab {r.name}: [{string.Join(", ", names)}]{(hasDetail && !stale ? "" : " WRONG (detail material missing / old atlas left)")}");
            }
            if (found < 2) { ok = false; lines.Add("prefab: body or weapon renderer from the detail FBX not found"); }
            if (e != null && (e.glow_body?.Length ?? 0) + (e.glow_weapon?.Length ?? 0) > 0)
            {
                var want = (e.glow_body ?? new string[0]).Concat(e.glow_weapon ?? new string[0]).ToArray();
                bool all = want.All(w => glow.Any(g => g.StartsWith(w)));
                lines.Add($"emissive slots: {(glow.Count > 0 ? string.Join("; ", glow) : "none")} (expected {string.Join(", ", want)}) {(all ? "ok" : "MISSING")}");
                ok &= all;
            }
            return ok;
        }

        // ------------------------------------------------------------------------------------------------ team mask read back
        static bool MaskCheck(string t, string body, string weap, string matName, string alb, Exp e, List<string> lines)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            try { tex.LoadImage(File.ReadAllBytes(alb)); } catch { lines.Add("mask: albedo not readable"); return false; }
            var rng = new System.Random(7);
            double area = 0, team = 0, part = 0; long samples = 0;
            var list = new List<(Vector2 a, Vector2 b, Vector2 c, double ar)>();
            foreach (var fbx in new[] { body, weap })
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(fbx); if (!go) continue;
                foreach (var (r, me) in MeshRenderers(go))
                {
                    var mats = r.sharedMaterials; Vector2[] uv;
                    try { uv = me.uv; } catch { lines.Add("mask: mesh not readable here (run in edit mode)"); return false; }
                    for (int s = 0; s < me.subMeshCount && s < mats.Length; s++)
                    {
                        if (!mats[s] || mats[s].name != matName) continue;
                        var tri = me.GetTriangles(s);
                        for (int i = 0; i < tri.Length; i += 3)
                        {
                            Vector2 a = uv[tri[i]], b = uv[tri[i + 1]], c = uv[tri[i + 2]];
                            double ar = System.Math.Abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y)) * 0.5;
                            if (ar > 1e-9) { list.Add((a, b, c, ar)); area += ar; }
                        }
                    }
                }
            }
            if (area <= 0) { lines.Add("mask: no detail faces found"); return false; }
            foreach (var (a, b, c, ar) in list)
            {
                int n = Mathf.Max(1, (int)System.Math.Round(ar / area * 200000));
                for (int k = 0; k < n; k++)
                {
                    double r1 = rng.NextDouble(), r2 = rng.NextDouble(); if (r1 + r2 > 1) { r1 = 1 - r1; r2 = 1 - r2; }
                    var q = a + (b - a) * (float)r1 + (c - a) * (float)r2;
                    float al = tex.GetPixelBilinear(q.x, q.y).a;
                    double w = ar / n; samples++;
                    if (al > 0.5f) team += w;
                    if (al > 0.05f && al < 0.95f) part += w;
                }
            }
            Object.DestroyImmediate(tex);
            double share = team / area, partial = part / area;
            bool ok = e == null || System.Math.Abs(share - e.team_share) <= Mathf.Max(0.015f, e.team_tol);
            ok &= partial < 0.06;
            lines.Add($"team mask through the imported UVs: {share:P1} of the painted surface is team paint (package: {(e != null ? e.team_share.ToString("P1") : "-")}), partial {partial:P1}, {samples} samples {(ok ? "ok" : "MISMATCH")}");
            return ok;
        }

        /// <summary>tweaks: the Tough prefab's WeaponHold carries the package's hold (read through SerializedObject, so a WeaponHold.cs
        /// without the bazookaElbowOut field reports MISSING instead of breaking the compile).</summary>
        static bool HoldCheck(Exp e, List<string> lines)
        {
            if (e == null || e.hold == null || float.IsNaN(e.hold.bazookaRestUp)) { lines.Add("WeaponHold hold: no expectation in the package json"); return false; }
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{Pref}/Z_bs01_tough.prefab");
            var h = pf ? pf.GetComponentInChildren<OpenZ.WeaponHold>(true) : null;
            if (!h) { lines.Add("WeaponHold hold: Z_bs01_tough WeaponHold MISSING"); return false; }
            var so = new SerializedObject(h); bool ok = true; var got = new List<string>();
            foreach (var (n, v) in new[] { ("bazookaRestUp", e.hold.bazookaRestUp), ("bazookaRestOut", e.hold.bazookaRestOut), ("bazookaHeadClear", e.hold.bazookaHeadClear),
                                           ("bazookaRestAlong", e.hold.bazookaRestAlong), ("bazookaTubeAxisY", e.hold.bazookaTubeAxisY), ("carryLift", e.hold.carryLift),
                                           ("bazookaElbowOut", e.hold.bazookaElbowOut) })
            {
                var sp = so.FindProperty(n);
                if (sp == null) { ok = false; got.Add($"{n} MISSING (WeaponHold.cs not patched?)"); continue; }
                bool same = Mathf.Abs(sp.floatValue - v) < 1e-4f; ok &= same;
                got.Add($"{n} {sp.floatValue:F4}{(same ? "" : $" (package {v:F4}) WRONG")}");
            }
            lines.Add($"WeaponHold hold (the bazooka on the pad next to the head): {string.Join(", ", got)} {(ok ? "ok" : "WRONG")}");
            return ok;
        }

        static bool SocketCheck(string weap, Exp e, List<string> lines)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(weap);
            if (!go) return false;
            var all = go.GetComponentsInChildren<Transform>(true);
            float D(string n) { var x = all.FirstOrDefault(a => a.name == n); return x ? Vector3.Distance(go.transform.InverseTransformPoint(x.position), Vector3.zero) : -1f; }
            float mz = D("Muzzle"), bm = D("BazookaMuzzle"), bb = D("BackBlast"), bbb = D("BazookaBackBlast");
            bool ok = mz > 0 && bm > 0 && bb > 0 && bbb > 0 && Mathf.Abs(mz - bm) < 1e-3f && Mathf.Abs(bb - bbb) < 1e-3f;
            if (e != null && e.muzzle_dist > 0 && e.backblast_dist > 0) ok &= Mathf.Abs(mz / e.muzzle_dist - 1f) < 0.02f && Mathf.Abs(bb / e.backblast_dist - 1f) < 0.02f;   // the FBX unit scale cancels out
            lines.Add($"bazooka sockets from the grip: Muzzle {mz:F3} m, BazookaMuzzle {bm:F3}, BackBlast {bb:F3}, BazookaBackBlast {bbb:F3} (package {(e != null ? $"{e.muzzle_dist:F3} / {e.backblast_dist:F3}" : "-")}) {(ok ? "ok" : "WRONG")}");
            return ok;
        }
    }
}
