using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Editor side of the package:
    /// <list type="bullet">
    /// <item><b>Materials</b> (menu OpenZ/Look/Unit look v2: Apply materials + textures): the 57 unit materials in
    /// Materials/Units (vehicles, cannons, APC / crane, BS-01 robots and weapons, laser robot, bazooka) get the clean albedos
    /// (*_clean.png next to the originals: baked light, dirt, scratches, chrome streaks and camo flattened offline, paint baked
    /// to the one shared neutral light paint #B0A896, paint mask in alpha), _UnitLook 1 (TeamLit's clean unit shading),
    /// metallic 0, smoothness per band, flat DV3 parts snapped to the shared bands (olive -> paint, metal -> gunmetal #6B7079,
    /// recess / rubber -> dark #2C2F33), no de-grime / mask-area mips. Rocks, cliffs, buildings, props: untouched.</item>
    /// <item><b>Post / AA / shadows</b> (menu ...: Apply post, AA, shadows): motion blur at most 0.25 / clamp 0.02 (left off if the
    /// grade pass switched it off; UnitLookMotionGate turns it off while the camera moves), SSAO 0.55 / 0.45 m / direct 0.25 -> 0.35 / 0.30 m / 0.12,
    /// shadow cascades moved onto the 55-140 m the RTS camera sees, FSR sharpening 0.92 -> 0.5 (only used when the heat guard
    /// lowers the render scale).</item>
    /// <item>Every original value is saved once in UserSettings/OpenZ_UnitLookV2_backup.json; the Revert menus restore it.</item>
    /// <item>OpenZ/Look/Unit look v2: A-B shading (Play) switches the shading back to the old PBR + stylize path on the same
    /// materials for a live comparison (global _OZUnitLookOff).</item>
    /// </list>
    /// All menu methods return a one-line result (for scripted runs) and log it.
    /// </summary>
    public static class OpenZUnitLookV2
    {
        const string MatDir = "Assets/OpenZ/Materials/Units";
        const string ModelDir = "Assets/OpenZ/Models";
        const string PostFx = "Assets/OpenZ/Settings/OpenZ_PostFX.asset";
        const string Renderer = "Assets/Settings/PC_Renderer.asset";
        const string RpAsset = "Assets/Settings/PC_RPAsset.asset";
        const string BackupFile = "UserSettings/OpenZ_UnitLookV2_backup.json";   // project-relative, outside Assets

        // TeamLit still reads _NeutralPaint through SRGBToLinear on top of Unity's own conversion and multiplies by nshade
        // (1.08 on this bright paint): this material value renders exactly #A17B41, the colour baked into the clean albedos
        static readonly Color NeutralPaint = new Color(0.8035f, 0.7121f, 0.5323f, 1f);
        static readonly Color Gunmetal = new Color(0.4196f, 0.4392f, 0.4745f, 1f);     // #6B7079
        static readonly Color Dark = new Color(0.1725f, 0.1843f, 0.2000f, 1f);         // #2C2F33
        static readonly Color LaserArmour = new Color(0.3137f, 0.3333f, 0.3647f, 1f);  // #50555D, the chubby laser robot's armour
        public static Vector4 Ramp = new Vector4(-0.05f, 0.85f, 0.80f, 0.00f);
        public static Vector4 Shade = new Vector4(1.00f, 0.15f, 0.10f, 0f);           // ambient, warm bounce, highlight cap
        public static Vector4 Kick = new Vector4(0.74f, 0.93f, 0.80f, 0.14f);          // kicker from, to, day strength, night widen

        enum Kind { Clean, PaintFlat, Gunmetal, Dark, LaserArmour, Keep }

        // material (Materials/Units/<name>.mat) -> kind, clean albedo (Models/...), paint mask in alpha, smoothness (-1 = keep)
        static readonly (string mat, Kind kind, string tex, bool alpha, float smooth)[] Rules =
        {
            ("m06_apc_mat", Kind.Clean, "Gen/m06_apc_basecolor_clean.png", true, 0.32f),
            ("m07_crane_mat", Kind.Clean, "Gen/m07_crane_basecolor_clean.png", true, 0.32f),
            ("m08_medium_tank_mat", Kind.Clean, "Gen/m08_medium_tank_basecolor_clean.png", true, 0.32f),
            ("m09_gatling_mat", Kind.Clean, "Gen/m09_gatling_basecolor_clean.png", true, 0.32f),
            ("m10_howitzer_mat", Kind.Clean, "Gen/m10_howitzer_basecolor_clean.png", true, 0.32f),
            ("m11_missile_cannon_mat", Kind.Clean, "Gen/m11_missile_cannon_basecolor_clean.png", true, 0.32f),
            ("tank_DV3_VehiclePBR_Masked_tank", Kind.Clean, "Legacy/tank_basecolor_clean.png", true, 0.32f),
            ("mech_DV3_VehiclePBR_Masked_mech", Kind.Clean, "Legacy/mech_basecolor_clean.png", true, 0.32f),
            ("mlrs_DV3_VehiclePBR_Masked_mlrs_001", Kind.Clean, "Legacy/mlrs_basecolor_clean.png", true, 0.32f),
            ("jeep_DV3_VehiclePBR_Masked", Kind.Clean, "Legacy/jeep_basecolor_clean.png", true, 0.32f),
            ("heavy_tank_HeavyTank_body_PBR", Kind.Clean, "Legacy/heavy_tank_body_clean.png", true, 0.32f),
            ("heavy_tank_HeavyTank_head_PBR", Kind.Clean, "Legacy/heavy_tank_head_clean.png", true, 0.32f),
            ("other-bot_SkinPBR_other-bot", Kind.Clean, "Legacy/other-bot_skin_clean.png", false, 0.30f),
            ("M_AtomJeep_body", Kind.Clean, "Atom/atom_jeep_body_2048_clean.png", false, 0.32f),
            ("M_BS01_Body", Kind.Clean, "BS01/Textures/BS01_Body_BaseColor_clean.png", false, 0.30f),
            ("M_BS01_Weapon_Grunt", Kind.Clean, "BS01/Textures/BS01_Weapon_Grunt_BaseColor_clean.png", false, 0.30f),
            ("M_BS01_Weapon_Psycho", Kind.Clean, "BS01/Textures/BS01_Weapon_Psycho_BaseColor_clean.png", false, 0.30f),
            ("M_BS01_Weapon_Pyro", Kind.Clean, "BS01/Textures/BS01_Weapon_Pyro_BaseColor_clean.png", false, 0.30f),
            ("M_BS01_Weapon_Pyro_Hazard", Kind.Clean, "BS01/Types/Textures/BS01_Weapon_Pyro_BaseColor_Hazard_clean.png", false, 0.30f),
            ("M_BS01_Weapon_Sniper", Kind.Clean, "BS01/Textures/BS01_Weapon_Sniper_BaseColor_clean.png", false, 0.30f),
            ("M_BS01_Weapon_Tough", Kind.Clean, "BS01/Textures/BS01_Weapon_Tough_BaseColor_clean.png", false, 0.30f),
            // flat DV3 parts: the olive plates / hatches become the shared neutral paint (TeamLit replaces paint; white albedo
            // with alpha 1 = paint), metal -> gunmetal, recess / rubber -> dark
            ("bazooka_DV3_olive", Kind.PaintFlat, null, true, 0.32f),
            ("mech_DV3_olive", Kind.PaintFlat, null, true, 0.32f),
            ("mech_DV3_oliveLight", Kind.PaintFlat, null, true, 0.32f),
            ("mlrs_DV3_oliveLight", Kind.PaintFlat, null, true, 0.32f),
            ("tank_DV3_oliveLight", Kind.PaintFlat, null, true, 0.32f),
            ("heavy_tank_HeavyTank_detail", Kind.PaintFlat, null, true, 0.32f),
            ("bazooka_DV3_metal", Kind.Gunmetal, null, false, 0.35f),
            ("jeep_DV3_metal", Kind.Gunmetal, null, false, 0.35f),
            ("mech_DV3_metal", Kind.Gunmetal, null, false, 0.35f),
            ("mlrs_DV3_metal", Kind.Gunmetal, null, false, 0.35f),
            ("tank_DV3_metal", Kind.Gunmetal, null, false, 0.35f),
            ("jeep_DV3_gunMetal", Kind.Gunmetal, null, false, 0.35f),
            ("M_AtomGun_metal", Kind.Gunmetal, null, false, 0.35f),
            ("bazooka_DV3_recess", Kind.Dark, null, false, 0.18f),
            ("mech_DV3_recess", Kind.Dark, null, false, 0.18f),
            ("mlrs_DV3_recess", Kind.Dark, null, false, 0.18f),
            ("tank_DV3_recess", Kind.Dark, null, false, 0.18f),
            ("jeep_DV3_rubber", Kind.Dark, null, false, 0.15f),
            ("mlrs_DV3_rubber", Kind.Dark, null, false, 0.15f),
            ("M_AtomGun_dark", Kind.Dark, null, false, 0.25f),
            ("other-bot_SoldierArmor_Masked", Kind.LaserArmour, null, false, 0.30f),
            // cannon vertex palette): only the unit shading, metallic 0 and a sane smoothness
            ("bazooka_DV3_redGlow", Kind.Keep, null, false, -1f),
            ("cannon_DV3_redGlow", Kind.Keep, null, false, -1f),
            ("jeep_DV3_redGlow", Kind.Keep, null, false, -1f),
            ("mech_DV3_redGlow", Kind.Keep, null, false, -1f),
            ("mlrs_DV3_redGlow", Kind.Keep, null, false, -1f),
            ("tank_DV3_redGlow", Kind.Keep, null, false, -1f),
            ("jeep_DV3_lamp", Kind.Keep, null, false, -1f),
            ("tank_DV3_lamp", Kind.Keep, null, false, -1f),
            ("jeep_DV3_red", Kind.Keep, null, false, 0.30f),
            ("bazooka_DV3_wood", Kind.Keep, null, false, 0.15f),
            ("M_AtomJeep_owner", Kind.Keep, null, false, 0.35f),
            ("M_AtomJeep_seat", Kind.Keep, null, false, 0.20f),
            ("M_AtomJeep_tire", Kind.Keep, null, false, 0.12f),
            ("M_BS01_Kit", Kind.Keep, null, false, 0.30f),
            ("cannon_DV3_VehiclePalette_Masked_001", Kind.Keep, null, false, 0.32f),
        };

        static readonly string[] FloatProps = { "_UnitLook", "_PaintFromAlpha", "_Metallic", "_Smoothness", "_Degrime", "_MaskArea", "_MaskDilate" };
        static readonly string[] ColorProps = { "_BaseColor", "_NeutralPaint", "_UnitRamp", "_UnitShade", "_UnitKick" };

        // ------------------------------------------------------------------------------------------------ backup
        [Serializable] class MatState { public string path; public string[] fNames; public float[] fValues; public string[] cNames; public Color[] cValues; public string baseMap; }
        [Serializable] class PostState
        {
            public bool saved; public float mbIntensity, mbClamp; public bool mbIntensityOver, mbClampOver;
            public float ssaoIntensity, ssaoRadius, ssaoDirect;
            public Vector3 cascade4; public Vector2 cascade3; public bool fsrOverride; public float fsrSharpness;
        }
        [Serializable] class Backup { public string when; public List<MatState> mats = new List<MatState>(); public List<string> textures = new List<string>(); public PostState post = new PostState(); }

        static string BackupAbs => Path.Combine(Path.GetDirectoryName(Application.dataPath), BackupFile);
        static Backup LoadBackup() => File.Exists(BackupAbs) ? JsonUtility.FromJson<Backup>(File.ReadAllText(BackupAbs)) : null;
        static void SaveBackup(Backup b) { Directory.CreateDirectory(Path.GetDirectoryName(BackupAbs)); File.WriteAllText(BackupAbs, JsonUtility.ToJson(b, true)); }

        // ------------------------------------------------------------------------------------------------ materials
        [MenuItem("OpenZ/Look/Unit look v2: Apply materials + textures")]
        public static string ApplyMaterials()
        {
            var shader = Shader.Find("OpenZ/TeamLit");
            if (!shader) return Log("TeamLit shader not found");
            var probe = new Material(shader);
            bool hasProps = probe.HasProperty("_UnitLook") && probe.HasProperty("_UnitRamp") && probe.HasProperty("_PaintFromAlpha");
            UnityEngine.Object.DestroyImmediate(probe);
            if (!hasProps) return Log("TeamLit has no _UnitLook: run apply_unitlook_v2.py --apply first (and let Unity compile)");
            var bk = LoadBackup() ?? new Backup { when = DateTime.Now.ToString("s") };
            bool firstTime = bk.mats.Count == 0;
            int done = 0, tex = 0; var missing = new List<string>();
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var r in Rules)
                    if (r.tex != null && ImportClean($"{ModelDir}/{r.tex}")) tex++;
            }
            finally { AssetDatabase.StopAssetEditing(); }
            foreach (var r in Rules)
            {
                string path = $"{MatDir}/{r.mat}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!m) { missing.Add(r.mat); continue; }
                if (m.shader != shader) { missing.Add(r.mat + " (not TeamLit)"); continue; }
                if (firstTime) bk.mats.Add(Snapshot(m, path));
                Texture2D t = null;
                if (r.tex != null)
                {
                    t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ModelDir}/{r.tex}");
                    if (!t) { missing.Add(r.tex); continue; }
                }
                Configure(m, r.kind, t, r.alpha, r.smooth);
                EditorUtility.SetDirty(m); done++;
            }
            if (firstTime) SaveBackup(bk);
            AssetDatabase.SaveAssets();
            return Log($"unit look v2: {done}/{Rules.Length} unit materials set, {tex} clean albedos imported" +
                       (missing.Count > 0 ? $", MISSING: {string.Join(", ", missing)}" : "") + (firstTime ? $" (originals saved in {BackupFile})" : ""));
        }

        static void Configure(Material m, Kind kind, Texture2D t, bool alpha, float smooth)
        {
            m.SetFloat("_UnitLook", 1f);
            m.SetFloat("_PaintFromAlpha", alpha ? 1f : 0f);
            m.SetFloat("_Metallic", 0f);                          // dielectric only (metallic 0.6 made the paint khaki-dark, DIAGNOSIS #5)
            if (smooth >= 0f) m.SetFloat("_Smoothness", smooth);
            else m.SetFloat("_Smoothness", Mathf.Min(m.GetFloat("_Smoothness"), 0.45f));
            m.SetFloat("_Degrime", 0f); m.SetFloat("_MaskArea", 0f); m.SetFloat("_MaskDilate", 0f);
            m.SetColor("_NeutralPaint", NeutralPaint);
            m.SetVector("_UnitRamp", Ramp); m.SetVector("_UnitShade", Shade); m.SetVector("_UnitKick", Kick);
            switch (kind)
            {
                case Kind.Clean: m.SetTexture("_BaseMap", t); break;
                case Kind.PaintFlat: m.SetColor("_BaseColor", Color.white); break;
                case Kind.Gunmetal: m.SetColor("_BaseColor", Gunmetal); break;
                case Kind.Dark: m.SetColor("_BaseColor", Dark); break;
                case Kind.LaserArmour: m.SetColor("_BaseColor", LaserArmour); break;
            }
        }

        static MatState Snapshot(Material m, string path)
        {
            var fl = FloatProps.Where(m.HasProperty).ToArray();
            var co = ColorProps.Where(m.HasProperty).ToArray();
            var tx = m.GetTexture("_BaseMap");
            return new MatState
            {
                path = path, fNames = fl, fValues = fl.Select(m.GetFloat).ToArray(), cNames = co,
                cValues = co.Select(n => n.StartsWith("_Unit") ? (Color)m.GetVector(n) : m.GetColor(n)).ToArray(),
                baseMap = tx ? AssetDatabase.GetAssetPath(tx) + "|" + (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(tx, out string g, out long id) ? g + ":" + id : "") : ""
            };
        }

        static bool ImportClean(string path)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!ti) return false;
            bool ch = false;
            void S<T>(Func<T> get, Action<T> set, T v) { if (!EqualityComparer<T>.Default.Equals(get(), v)) { set(v); ch = true; } }
            S(() => ti.textureType, v => ti.textureType = v, TextureImporterType.Default);
            S(() => ti.sRGBTexture, v => ti.sRGBTexture = v, true);
            S(() => ti.alphaSource, v => ti.alphaSource = v, TextureImporterAlphaSource.FromInput);
            S(() => ti.alphaIsTransparency, v => ti.alphaIsTransparency = v, false);
            S(() => ti.mipmapEnabled, v => ti.mipmapEnabled = v, true);
            S(() => ti.mipmapFilter, v => ti.mipmapFilter = v, TextureImporterMipFilter.BoxFilter);   // Kaiser sharpens = sparkle
            S(() => ti.anisoLevel, v => ti.anisoLevel = v, 8);
            S(() => ti.filterMode, v => ti.filterMode = v, FilterMode.Trilinear);
            S(() => ti.wrapMode, v => ti.wrapMode = v, TextureWrapMode.Repeat);
            S(() => ti.maxTextureSize, v => ti.maxTextureSize = v, 2048);
            S(() => ti.textureCompression, v => ti.textureCompression = v, TextureImporterCompression.CompressedHQ);   // BC7: flat colours + mask, no DXT blocks
            if (ch) ti.SaveAndReimport();
            return true;
        }

        [MenuItem("OpenZ/Look/Unit look v2: Revert materials")]
        public static string RevertMaterials()
        {
            var bk = LoadBackup();
            if (bk == null || bk.mats.Count == 0) return Log("unit look v2: no material backup (" + BackupFile + ")");
            int n = 0;
            foreach (var s in bk.mats)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(s.path);
                if (!m) continue;
                for (int i = 0; i < s.fNames.Length; i++) m.SetFloat(s.fNames[i], s.fValues[i]);
                for (int i = 0; i < s.cNames.Length; i++)
                    if (s.cNames[i].StartsWith("_Unit")) m.SetVector(s.cNames[i], s.cValues[i]); else m.SetColor(s.cNames[i], s.cValues[i]);
                if (m.HasProperty("_UnitLook") && !s.fNames.Contains("_UnitLook")) m.SetFloat("_UnitLook", 0f);
                if (m.HasProperty("_PaintFromAlpha") && !s.fNames.Contains("_PaintFromAlpha")) m.SetFloat("_PaintFromAlpha", 0f);
                m.SetTexture("_BaseMap", ResolveTex(s.baseMap));
                EditorUtility.SetDirty(m); n++;
            }
            AssetDatabase.SaveAssets();
            return Log($"unit look v2: {n} unit materials restored from {BackupFile}");
        }

        static Texture ResolveTex(string rec)
        {
            if (string.IsNullOrEmpty(rec)) return null;
            var parts = rec.Split('|'); string path = parts[0];
            string gid = parts.Length > 1 ? parts[1] : "";
            if (gid.Contains(":"))
            {
                long id = long.Parse(gid.Split(':')[1]);
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (o is Texture tx && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string _, out long lid) && lid == id) return tx;
            }
            return AssetDatabase.LoadAssetAtPath<Texture>(path);
        }

        // ------------------------------------------------------------------------------------------------ post / AA / shadows
        [MenuItem("OpenZ/Look/Unit look v2: Apply post, AA, shadows")]
        public static string ApplyPost()
        {
            var bk = LoadBackup() ?? new Backup { when = DateTime.Now.ToString("s") };
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostFx);
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RpAsset);
            var rd = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(Renderer);
            if (!prof || !urp || !rd) return Log("unit look v2 post: OpenZ_PostFX / PC_RPAsset / PC_Renderer not found");
            var ssao = Ssao(rd);
            if (!bk.post.saved)
            {
                var p = bk.post; p.saved = true;
                if (prof.TryGet<MotionBlur>(out var mb0)) { p.mbIntensity = mb0.intensity.value; p.mbClamp = mb0.clamp.value; p.mbIntensityOver = mb0.intensity.overrideState; p.mbClampOver = mb0.clamp.overrideState; }
                if (ssao != null) { p.ssaoIntensity = F(ssao, "Intensity"); p.ssaoRadius = F(ssao, "Radius"); p.ssaoDirect = F(ssao, "DirectLightingStrength"); }
                p.cascade4 = urp.cascade4Split; p.cascade3 = urp.cascade3Split; p.fsrOverride = urp.fsrOverrideSharpness; p.fsrSharpness = urp.fsrSharpness;
                SaveBackup(bk);
            }
            // 1) motion blur: light (0.25 / clamp 0.02) and only while the camera stands still (UnitLookMotionGate). If the grade
            //    pass already switched it off (intensity 0), it stays off: only the clamp is lowered for a later switch-on
            string mbState = "motion blur not in OpenZ_PostFX";
            if (prof.TryGet<MotionBlur>(out var mb))
            {
                bool on = mb.intensity.overrideState && mb.intensity.value > 0.001f;
                if (on) mb.intensity.Override(Mathf.Min(mb.intensity.value, 0.25f));
                mb.clamp.Override(0.02f); EditorUtility.SetDirty(mb);
                mbState = on ? $"motion blur {mb.intensity.value:0.00} / clamp 0.02, gated by camera motion at run time" : "motion blur stays OFF (clamp 0.02 for a later switch-on)";
            }
            EditorUtility.SetDirty(prof);
            // 2) SSAO: gentle contact shadow for 2-5 m units (was crevice grime on dense meshes)
            if (ssao != null) { SetF(ssao, "Intensity", 0.35f); SetF(ssao, "Radius", 0.30f); SetF(ssao, "DirectLightingStrength", 0.12f); }
            // 3) cascades on what the camera sees (depth ~55-140 m at default zoom, ~40-65 m close, ~200 m overview): 4 cascades
            //    over 260 m end at 60 / 100 / 160 m (were 32 / 76 / 139: the first two covered ground the camera never shows);
            //    the take preset's 3 cascades over 160 m end at 67 / 109 m
            urp.cascade4Split = new Vector3(0.23f, 0.385f, 0.615f);
            urp.cascade3Split = new Vector2(0.42f, 0.68f);
            // 4) FSR sharpening (render scale < 1 under the heat guard / take light): 0.92 haloed unit edges
            urp.fsrOverrideSharpness = true; urp.fsrSharpness = 0.5f;
            EditorUtility.SetDirty(urp);
            AssetDatabase.SaveAssets();
            return Log($"unit look v2 post: {mbState}, SSAO 0.35 / 0.30 m / direct 0.12" +
                       (ssao == null ? " (SSAO feature NOT found)" : "") + ", cascades 4: 0.23 / 0.385 / 0.615, 3: 0.42 / 0.68, FSR sharpness 0.5");
        }

        [MenuItem("OpenZ/Look/Unit look v2: Revert post, AA, shadows")]
        public static string RevertPost()
        {
            var bk = LoadBackup();
            if (bk == null || !bk.post.saved) return Log("unit look v2 post: no backup");
            var p = bk.post;
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostFx);
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RpAsset);
            var rd = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(Renderer);
            if (prof && prof.TryGet<MotionBlur>(out var mb))
            {
                mb.intensity.value = p.mbIntensity; mb.intensity.overrideState = p.mbIntensityOver;
                mb.clamp.value = p.mbClamp; mb.clamp.overrideState = p.mbClampOver; EditorUtility.SetDirty(mb); EditorUtility.SetDirty(prof);
            }
            var ssao = rd ? Ssao(rd) : null;
            if (ssao != null) { SetF(ssao, "Intensity", p.ssaoIntensity); SetF(ssao, "Radius", p.ssaoRadius); SetF(ssao, "DirectLightingStrength", p.ssaoDirect); }
            if (urp) { urp.cascade4Split = p.cascade4; urp.cascade3Split = p.cascade3; urp.fsrOverrideSharpness = p.fsrOverride; urp.fsrSharpness = p.fsrSharpness; EditorUtility.SetDirty(urp); }
            AssetDatabase.SaveAssets();
            return Log("unit look v2 post: restored from " + BackupFile);
        }

        static SerializedObject Ssao(ScriptableRendererData rd)
        {
            foreach (var f in rd.rendererFeatures)
                if (f && f.GetType().Name.Contains("AmbientOcclusion")) return new SerializedObject(f);
            return null;
        }
        static float F(SerializedObject so, string n) { var p = so.FindProperty("m_Settings." + n); return p != null ? p.floatValue : 0f; }
        static void SetF(SerializedObject so, string n, float v)
        {
            var p = so.FindProperty("m_Settings." + n); if (p == null) return;
            p.floatValue = v; so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(so.targetObject);
        }

        // ------------------------------------------------------------------------------------------------ A/B + status
        [MenuItem("OpenZ/Look/Unit look v2: A-B shading (Play)")]
        public static string ToggleAB()
        {
            float off = Shader.GetGlobalFloat("_OZUnitLookOff") > 0.5f ? 0f : 1f;
            Shader.SetGlobalFloat("_OZUnitLookOff", off);
            return Log(off > 0.5f ? "unit look v2: OFF (old PBR + stylize shading on the same materials)" : "unit look v2: ON");
        }

        [MenuItem("OpenZ/Look/Unit look v2: Status")]
        public static string Status()
        {
            var sb = new StringBuilder("unit look v2 status: ");
            int on = 0, total = 0; var off = new List<string>();
            foreach (var r in Rules)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{r.mat}.mat");
                if (!m) continue; total++;
                if (m.HasProperty("_UnitLook") && m.GetFloat("_UnitLook") > 0.5f) on++; else off.Add(r.mat);
            }
            sb.Append($"{on}/{total} materials on");
            if (off.Count > 0) sb.Append(" (off: " + string.Join(", ", off.Take(8)) + (off.Count > 8 ? " ..." : "") + ")");
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostFx);
            if (prof && prof.TryGet<MotionBlur>(out var mb)) sb.Append($"; motion blur {mb.intensity.value:0.00} / {mb.clamp.value:0.000}");
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(RpAsset);
            if (urp) sb.Append($"; cascades {urp.cascade4Split}; FSR {(urp.fsrOverrideSharpness ? urp.fsrSharpness.ToString("0.00") : "default")}");
            var rd = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(Renderer);
            var ssao = rd ? Ssao(rd) : null;
            if (ssao != null) sb.Append($"; SSAO {F(ssao, "Intensity"):0.00} / {F(ssao, "Radius"):0.00} m / {F(ssao, "DirectLightingStrength"):0.00}");
            if (Application.isPlaying)
            {
                sb.Append($"; A-B {(Shader.GetGlobalFloat("_OZUnitLookOff") > 0.5f ? "OFF" : "on")}");
                if (UnitLookMotionGate.I) sb.Append($"; motion gate {UnitLookMotionGate.I.Current:0.00}");
            }
            return Log(sb.ToString());
        }

        static string Log(string s) { Debug.Log("[OpenZ] " + s); return s; }
    }
}
