using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Toy paint look, editor side (the toy paint path itself is in TeamLit.shader):
    /// <list type="bullet">
    /// <item><b>OpenZ/Look/Toy paint: Apply</b>: the repainted 2048 albedos (Models/*/Toy/*_toy.png: military khaki in a few
    /// close tones, dark warm gunmetal tracks / barrels / wheels, soft off-white accents only, a very gentle top-to-bottom
    /// gradient and soft baked AO, crisp team panels on the model's own panels in alpha) go on the unit materials, plus the
    /// matching shading preset of TeamLit's unit path: soft wrapped diffuse (shade side about 0.75 of the lit side), one broad
    /// soft low highlight (smoothness 0.36-0.40, capped at 5 % of the key), no rim / outline by day, dielectric only.</item>
    /// <item>Every value it changes is saved once in UserSettings/OpenZ_ToyPaint_backup.json (the state before the first
    /// apply, i.e. unitlook_v2's); <b>OpenZ/Look/Toy paint: Revert</b> restores it exactly.</item>
    /// <item><b>OpenZ/Look/Toy paint: Status</b> counts the materials on the toy path.</item>
    /// </list>
    /// Run after unitlook_v2's "Unit look v2: Apply materials + textures": running that menu again (or an OpenZSetup / Atom jeep
    /// setup menu that rebuilds unit materials) puts the unitlook albedos back; then run Toy paint: Apply again.
    /// All menu methods return a one-line result (scripted runs) and log it.
    /// </summary>
    public static class OpenZToyPaint
    {
        const string MatDir = "Assets/OpenZ/Materials/Units";
        const string ModelDir = "Assets/OpenZ/Models";
        const string ShaderPath = "Assets/OpenZ/Shaders/TeamLit.shader";
        const string BackupFile = "UserSettings/OpenZ_ToyPaint_backup.json";   // project-relative, outside Assets

        // ---- the shading preset of TeamLit's unit path (generated from tools/tp_preset.py; tools/parity_check.py verifies)
        public static Vector4 Ramp = new Vector4(-0.2f, 0.85f, 0.6f, 0f);    // ramp from N.L, to N.L, Lambert share, key side
        public static Vector4 Shade = new Vector4(0.7f, 0.12f, 0.05f, 0f);   // ambient, warm bounce in shade, highlight cap
        public static Vector4 Kick = new Vector4(0.74f, 0.93f, 0f, 0.1f);    // kicker from, to, DAY strength 0 (no rim), night widen
        const float ToyTeamRef = 0.5149f;    // linear value of the team-panel base colour (190, 124, 50)
        const float RobotTeamRef = 0.1812f;  // linear value of the BS-01 plate base (118, 118, 118)
        const float VcValScale = 0.26f;
        const float VcNShade = 0.8f;

        enum Kind { Toy, Swatch, Robot, Robot2, Weapon, Flat, VcPaint, Keep }

        // material (Materials/Units/<name>.mat) -> kind, toy texture (under Models/), colour (sRGB 0-255, flat / swatch / vcpaint),
        // smoothness (-1 = keep, capped 0.45)
        static readonly (string mat, Kind kind, string tex, Color32 col, float smooth)[] Rules =
        {
            ("tank_DV3_VehiclePBR_Masked_tank", Kind.Toy, "Legacy/Toy/tank_toy.png", new Color32(0, 0, 0, 255), 0.4f),
            ("tank_DV3_oliveLight", Kind.Swatch, "Legacy/Toy/swatch_khaki_light.png", new Color32(218, 152, 68, 255), 0.4f),
            ("tank_DV3_metal", Kind.Flat, null, new Color32(82, 77, 71, 255), 0.36f),
            ("tank_DV3_recess", Kind.Flat, null, new Color32(146, 95, 40, 255), 0.28f),
            ("tank_DV3_redGlow", Kind.Keep, null, new Color32(0, 0, 0, 255), -1f),
            ("tank_DV3_lamp", Kind.Keep, null, new Color32(0, 0, 0, 255), -1f),
            ("mech_DV3_VehiclePBR_Masked_mech", Kind.Toy, "Legacy/Toy/mech_toy.png", new Color32(0, 0, 0, 255), 0.4f),
            ("mech_DV3_oliveLight", Kind.Swatch, "Legacy/Toy/swatch_khaki_light.png", new Color32(218, 152, 68, 255), 0.4f),
            ("mech_DV3_olive", Kind.Swatch, "Legacy/Toy/swatch_team.png", new Color32(190, 124, 50, 255), 0.4f),
            ("mech_DV3_metal", Kind.Flat, null, new Color32(82, 77, 71, 255), 0.36f),
            ("mech_DV3_recess", Kind.Flat, null, new Color32(146, 95, 40, 255), 0.28f),
            ("mech_DV3_redGlow", Kind.Keep, null, new Color32(0, 0, 0, 255), -1f),
            ("heavy_tank_HeavyTank_body_PBR", Kind.Toy, "Legacy/Toy/heavy_tank_body_legacy.png", new Color32(0, 0, 0, 255), 0.4f),
            ("heavy_tank_HeavyTank_head_PBR", Kind.Toy, "Legacy/Toy/heavy_tank_head_legacy.png", new Color32(0, 0, 0, 255), 0.4f),
            ("heavy_tank_HeavyTank_detail", Kind.Swatch, "Legacy/Toy/swatch_heavy_gunmetal.png", new Color32(56, 61, 66, 255), 0.4f),
            ("mlrs_DV3_VehiclePBR_Masked_mlrs_001", Kind.Toy, "Legacy/Toy/mlrs_toy.png", new Color32(0, 0, 0, 255), 0.4f),
            ("mlrs_DV3_oliveLight", Kind.Swatch, "Legacy/Toy/swatch_khaki_light.png", new Color32(218, 152, 68, 255), 0.4f),
            ("mlrs_DV3_metal", Kind.Flat, null, new Color32(82, 77, 71, 255), 0.36f),
            ("mlrs_DV3_recess", Kind.Flat, null, new Color32(146, 95, 40, 255), 0.28f),
            ("mlrs_DV3_rubber", Kind.Flat, null, new Color32(54, 51, 48, 255), 0.22f),
            ("mlrs_DV3_redGlow", Kind.Keep, null, new Color32(0, 0, 0, 255), -1f),
            ("cannon_DV3_VehiclePalette_Masked_001", Kind.VcPaint, null, new Color32(206, 138, 56, 255), 0.4f),
            ("cannon_DV3_redGlow", Kind.Keep, null, new Color32(0, 0, 0, 255), -1f),
            ("M_AtomJeep_body", Kind.Toy, "Atom/Toy/atom_jeep_body_toy.png", new Color32(0, 0, 0, 255), 0.4f),
            ("M_AtomJeep_tire", Kind.Weapon, "Atom/Toy/atom_jeep_tire_toy.png", new Color32(0, 0, 0, 255), 0.22f),
            ("M_AtomGun_metal", Kind.Flat, null, new Color32(82, 77, 71, 255), 0.36f),
            ("M_AtomGun_dark", Kind.Flat, null, new Color32(54, 51, 48, 255), 0.28f),
            ("M_AtomJeep_seat", Kind.Flat, null, new Color32(146, 95, 40, 255), 0.22f),
            ("M_AtomJeep_owner", Kind.Keep, null, new Color32(0, 0, 0, 255), -1f),
            ("m06_apc_mat", Kind.Toy, "Gen/Toy/m06_apc_toy.png", new Color32(0, 0, 0, 255), 0.4f),
            ("m07_crane_mat", Kind.Toy, "Gen/Toy/m07_crane_toy.png", new Color32(0, 0, 0, 255), 0.4f),
            ("m09_gatling_mat", Kind.Toy, "Gen/Toy/m09_gatling_toy.png", new Color32(0, 0, 0, 255), 0.4f),
            ("m10_howitzer_mat", Kind.Toy, "Gen/Toy/m10_howitzer_toy.png", new Color32(0, 0, 0, 255), 0.4f),
            ("m11_missile_cannon_mat", Kind.Toy, "Gen/Toy/m11_missile_cannon_toy.png", new Color32(0, 0, 0, 255), 0.4f),
            ("M_BS01_Body", Kind.Robot, "BS01/Toy/BS01_Body_toy.png", new Color32(0, 0, 0, 255), 0.4f),
            ("M_BS01_Kit", Kind.Keep, null, new Color32(0, 0, 0, 255), -1f),
            ("M_BS01_Weapon_Grunt", Kind.Weapon, "BS01/Toy/BS01_Weapon_Grunt_toy.png", new Color32(0, 0, 0, 255), 0.36f),
            ("M_BS01_Weapon_Psycho", Kind.Weapon, "BS01/Toy/BS01_Weapon_Psycho_toy.png", new Color32(0, 0, 0, 255), 0.36f),
            ("M_BS01_Weapon_Pyro", Kind.Weapon, "BS01/Toy/BS01_Weapon_Pyro_toy.png", new Color32(0, 0, 0, 255), 0.36f),
            ("M_BS01_Weapon_Pyro_Hazard", Kind.Weapon, "BS01/Toy/BS01_Weapon_Pyro_Hazard_toy.png", new Color32(0, 0, 0, 255), 0.36f),
            ("M_BS01_Weapon_Sniper", Kind.Weapon, "BS01/Toy/BS01_Weapon_Sniper_toy.png", new Color32(0, 0, 0, 255), 0.36f),
            ("M_BS01_Weapon_Tough", Kind.Weapon, "BS01/Toy/BS01_Weapon_Tough_toy.png", new Color32(0, 0, 0, 255), 0.36f),
            ("other-bot_SkinPBR_other-bot", Kind.Robot2, "Legacy/Toy/other-bot_skin_toy.png", new Color32(0, 0, 0, 255), 0.4f),
            ("other-bot_SoldierArmor_Masked", Kind.Flat, null, new Color32(82, 77, 71, 255), 0.36f),
            ("bazooka_DV3_olive", Kind.Swatch, "Legacy/Toy/swatch_khaki.png", new Color32(206, 138, 56, 255), 0.4f),
            ("bazooka_DV3_metal", Kind.Flat, null, new Color32(82, 77, 71, 255), 0.36f),
            ("bazooka_DV3_recess", Kind.Flat, null, new Color32(54, 51, 48, 255), 0.28f),
            ("bazooka_DV3_redGlow", Kind.Keep, null, new Color32(0, 0, 0, 255), -1f),
            ("bazooka_DV3_wood", Kind.Keep, null, new Color32(0, 0, 0, 255), -1f),
        };

        static readonly string[] FloatProps = { "_UnitLook", "_PaintFromAlpha", "_Metallic", "_Smoothness", "_TeamMode", "_Accent", "_TeamRef",
                                                "_ShadeMin", "_ShadeMax", "_Degrime", "_MaskArea", "_MaskDilate", "_Degreen", "_AccentFrom", "_AccentTo",
                                                "_ValScale" };
        static readonly string[] ColorProps = { "_BaseColor", "_NeutralPaint", "_UnitRamp", "_UnitShade", "_UnitKick" };

        // ------------------------------------------------------------------------------------------------ backup
        [Serializable] class MatState { public string path; public string[] fNames; public float[] fValues; public string[] cNames; public Color[] cValues; public string baseMap; }
        [Serializable] class Backup { public string when; public List<MatState> mats = new List<MatState>(); }


        static string BackupAbs => Path.Combine(Path.GetDirectoryName(Application.dataPath), BackupFile);
        static Backup LoadBackup() => File.Exists(BackupAbs) ? JsonUtility.FromJson<Backup>(File.ReadAllText(BackupAbs)) : null;
        static void SaveBackup(Backup b) { Directory.CreateDirectory(Path.GetDirectoryName(BackupAbs)); File.WriteAllText(BackupAbs, JsonUtility.ToJson(b, true)); }

        static MatState Snapshot(Material m, string path)
        {
            var s = new MatState { path = path };
            var fn = FloatProps.Where(m.HasProperty).ToArray(); s.fNames = fn; s.fValues = fn.Select(m.GetFloat).ToArray();
            var cn = ColorProps.Where(m.HasProperty).ToArray(); s.cNames = cn; s.cValues = cn.Select(n => (Color)m.GetVector(n)).ToArray();
            var t = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
            s.baseMap = t ? AssetDatabase.GetAssetPath(t) : "";
            return s;
        }

        // ------------------------------------------------------------------------------------------------ apply
        /// <summary>true when TeamLit carries the toypaint_v1 blocks (apply_toypaint_v1.py --apply ran).</summary>
        public static bool ShaderReady()
        {
            var abs = Path.Combine(Path.GetDirectoryName(Application.dataPath), ShaderPath);
            return File.Exists(abs) && File.ReadAllText(abs).Contains("toypaint_v1: the team panel of a toy albedo");
        }

        [MenuItem("OpenZ/Look/Toy paint: Apply")]
        public static string Apply()
        {
            var shader = Shader.Find("OpenZ/TeamLit");
            if (!shader) return Log("TeamLit shader not found");
            var probe = new Material(shader);
            bool hasProps = probe.HasProperty("_UnitLook") && probe.HasProperty("_PaintFromAlpha") && probe.HasProperty("_UnitRamp");
            UnityEngine.Object.DestroyImmediate(probe);
            if (!hasProps) return Log("TeamLit has no _UnitLook: unitlook_v2 must be imported first");
            if (!ShaderReady()) return Log("TeamLit has no toypaint_v1 blocks: run apply_toypaint_v1.py --apply first (and let Unity compile)");
            var bk = LoadBackup() ?? new Backup { when = DateTime.Now.ToString("s") };
            bool firstTime = bk.mats.Count == 0;
            int done = 0, tex = 0, added = 0; var missing = new List<string>();
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var t in Rules.Where(r => r.tex != null).Select(r => r.tex).Distinct())
                    if (ImportToy($"{ModelDir}/{t}")) tex++;
            }
            finally { AssetDatabase.StopAssetEditing(); }
            foreach (var r in Rules)
            {
                string path = $"{MatDir}/{r.mat}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!m) { missing.Add(r.mat); continue; }
                if (m.shader != shader) { missing.Add(r.mat + " (not TeamLit)"); continue; }
                Texture2D t = null;
                if (r.tex != null)
                {
                    t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ModelDir}/{r.tex}");
                    if (!t) { missing.Add(r.tex); continue; }
                }
                if (!bk.mats.Any(x => x.path == path)) { bk.mats.Add(Snapshot(m, path)); added++; }   // saved once, before the first change
                Configure(m, r.kind, t, r.col, r.smooth);
                EditorUtility.SetDirty(m); done++;
            }
            SaveBackup(bk);
            AssetDatabase.SaveAssets();
            return Log($"toy paint: {done}/{Rules.Length} unit materials set, {tex} toy albedos imported" +
                       (missing.Count > 0 ? $", MISSING: {string.Join(", ", missing)}" : "") + (firstTime ? $" (previous values saved in {BackupFile})" : ""));
        }

        static Color Srgb(Color32 c) => new Color(c.r / 255f, c.g / 255f, c.b / 255f, 1f);

        static void Configure(Material m, Kind kind, Texture2D t, Color32 col, float smooth)
        {
            m.SetFloat("_UnitLook", 1f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", smooth >= 0f ? smooth : Mathf.Min(m.GetFloat("_Smoothness"), 0.45f));
            m.SetFloat("_Degrime", 0f); m.SetFloat("_MaskArea", 0f); m.SetFloat("_MaskDilate", 0f);
            m.SetVector("_UnitRamp", Ramp); m.SetVector("_UnitShade", Shade); m.SetVector("_UnitKick", Kick);
            switch (kind)
            {
                case Kind.Toy:
                case Kind.Swatch:
                    // toy paint path: texture paint kept, alpha 0.5 = khaki paint (neutral -> grey, owned -> half livery),
                    // alpha 1 = the team panel (vivid team colour shaded by the texture's AO / gradient: value / _TeamRef)
                    m.SetTexture("_BaseMap", t); m.SetColor("_BaseColor", Color.white);
                    m.SetFloat("_PaintFromAlpha", 2f); m.SetFloat("_TeamMode", 1f); m.SetFloat("_Accent", 1f);
                    m.SetFloat("_TeamRef", ToyTeamRef); m.SetFloat("_Degreen", 0f);
                    m.SetFloat("_AccentFrom", 2f); m.SetFloat("_AccentTo", 2f);            // no height band: the texture has the panels
                    break;
                case Kind.Robot:                                                           // BS-01 body: TeamMode 3, plates in alpha
                    m.SetTexture("_BaseMap", t); m.SetColor("_BaseColor", Color.white);
                    m.SetFloat("_PaintFromAlpha", 0f);
                    m.SetFloat("_TeamRef", RobotTeamRef); m.SetFloat("_ShadeMin", 0.5f); m.SetFloat("_ShadeMax", 1.2f);
                    break;
                case Kind.Robot2:                                                          // laser robot: plates stay in vertex alpha
                case Kind.Weapon:
                    m.SetTexture("_BaseMap", t); m.SetColor("_BaseColor", Color.white);
                    m.SetFloat("_PaintFromAlpha", 0f);
                    break;
                case Kind.Flat:
                    m.SetColor("_BaseColor", Srgb(col)); m.SetFloat("_PaintFromAlpha", 0f);
                    break;
                case Kind.VcPaint:
                    m.SetColor("_NeutralPaint", NeutralPaintFor(col)); m.SetFloat("_PaintFromAlpha", 0f);
                    m.SetFloat("_AccentFrom", 2f); m.SetFloat("_AccentTo", 2f);
                    m.SetFloat("_ValScale", VcValScale);                                   // its white palette ring -> light grey
                    break;
                case Kind.Keep:
                    break;
            }
        }

        /// <summary>_NeutralPaint (stored sRGB) that renders the sRGB colour c through TeamLit's SRGBToLinear(_NeutralPaint) (Unity
        /// already linearised the property once) x nshade (the cannon's olive vertex colour: clamp(V / 0.3, 0.8, 1.08) = 0.8).</summary>
        static Color NeutralPaintFor(Color32 c)
        {
            float F(float v) => Mathf.LinearToGammaSpace(Mathf.LinearToGammaSpace(Mathf.Min(1f, Mathf.GammaToLinearSpace(v / 255f) / VcNShade)));
            return new Color(F(c.r), F(c.g), F(c.b), 1f);
        }

        static bool ImportToy(string path)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!ti) return false;
            bool swatch = path.Contains("swatch_");
            bool ch = false;
            void S<T>(Func<T> get, Action<T> set, T v) { if (!EqualityComparer<T>.Default.Equals(get(), v)) { set(v); ch = true; } }
            S(() => ti.textureType, v => ti.textureType = v, TextureImporterType.Default);
            S(() => ti.sRGBTexture, v => ti.sRGBTexture = v, true);
            S(() => ti.alphaSource, v => ti.alphaSource = v, TextureImporterAlphaSource.FromInput);
            S(() => ti.alphaIsTransparency, v => ti.alphaIsTransparency = v, false);          // alpha = paint / team mask (data)
            S(() => ti.mipmapEnabled, v => ti.mipmapEnabled = v, true);
            S(() => ti.mipmapFilter, v => ti.mipmapFilter = v, TextureImporterMipFilter.BoxFilter);
            S(() => ti.anisoLevel, v => ti.anisoLevel = v, swatch ? 0 : 8);
            S(() => ti.filterMode, v => ti.filterMode = v, swatch ? FilterMode.Point : FilterMode.Trilinear);
            S(() => ti.wrapMode, v => ti.wrapMode = v, TextureWrapMode.Repeat);                // as the models' own samplers
            S(() => ti.maxTextureSize, v => ti.maxTextureSize = v, 2048);                       // full source resolution
            S(() => ti.textureCompression, v => ti.textureCompression = v, TextureImporterCompression.CompressedHQ);   // BC7
            if (ch) ti.SaveAndReimport();
            return true;
        }

        // ------------------------------------------------------------------------------------------------ revert / status
        [MenuItem("OpenZ/Look/Toy paint: Revert")]
        public static string Revert()
        {
            var bk = LoadBackup();
            if (bk == null || bk.mats.Count == 0) return Log($"toy paint: nothing to revert (no {BackupFile})");
            int n = 0;
            foreach (var s in bk.mats)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(s.path);
                if (!m) continue;
                for (int i = 0; i < s.fNames.Length; i++) if (m.HasProperty(s.fNames[i])) m.SetFloat(s.fNames[i], s.fValues[i]);
                for (int i = 0; i < s.cNames.Length; i++) if (m.HasProperty(s.cNames[i])) m.SetVector(s.cNames[i], s.cValues[i]);
                if (m.HasProperty("_BaseMap"))
                    m.SetTexture("_BaseMap", string.IsNullOrEmpty(s.baseMap) ? null : AssetDatabase.LoadAssetAtPath<Texture>(s.baseMap));
                EditorUtility.SetDirty(m); n++;
            }
            AssetDatabase.SaveAssets();
            File.Move(BackupAbs, BackupAbs + "." + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".reverted");
            return Log($"toy paint: {n} materials restored from {BackupFile} (kept as *.reverted)");
        }

        [MenuItem("OpenZ/Look/Toy paint: Status")]
        public static string Status()
        {
            int on = 0, total = 0; var off = new List<string>();
            foreach (var r in Rules)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{r.mat}.mat");
                if (!m) { off.Add(r.mat + " (missing)"); continue; }
                total++;
                bool ok = m.HasProperty("_UnitLook") && m.GetFloat("_UnitLook") > 0.5f && m.GetVector("_UnitRamp") == Ramp;
                if (r.kind == Kind.Toy || r.kind == Kind.Swatch) ok &= m.GetFloat("_PaintFromAlpha") > 1.5f;
                if (r.tex != null) { var t = m.GetTexture("_BaseMap"); ok &= t && AssetDatabase.GetAssetPath(t).EndsWith(Path.GetFileName(r.tex)); }
                if (ok) on++; else off.Add(r.mat);
            }
            return Log($"toy paint: {on}/{total} materials on, shader {(ShaderReady() ? "ready" : "NOT PATCHED")}, backup {(File.Exists(BackupAbs) ? "saved" : "none")}" +
                       (off.Count > 0 ? $"; off: {string.Join(", ", off.Take(12))}{(off.Count > 12 ? " ..." : "")}" : ""));
        }

        static string Log(string s) { Debug.Log("[OpenZ] " + s); return s; }
    }
}
