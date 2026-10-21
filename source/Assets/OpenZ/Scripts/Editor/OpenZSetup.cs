using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// One-click project setup for OpenZ (menu "OpenZ/..."): import settings, TeamLit materials, unit/building/prop
    /// (MRK_tile check), imported GLBs are normalised to ~1.9 units and centred, imported vehicles face along X, M12 props
    /// use vertex colours, olive paint is not a team (TeamLit), robot skin team mask = vertex alpha.
    /// </summary>
    public static class OpenZSetup
    {
        const string Root = "Assets/OpenZ";
        const string Env = Root + "/Models/Environment";
        const string Tex = Root + "/Textures";
        const string MatEnv = Root + "/Materials/Env";
        const string MatUnits = Root + "/Materials/Units";
        const string Pref = Root + "/Prefabs";

        // ------------------------------------------------------------------------------------------------ 1. imports
        [MenuItem("OpenZ/1 Configure Imports")]
        public static void ConfigureImports()
        {
            foreach (var lv in new[] { "L02", "L17" })
                SetTexture($"{Tex}/{lv}_ground_paint.png", 8192, 16, TextureWrapMode.Clamp, false);
            foreach (var t in new[] { "cliff_strata.png", "cliff_strata_city.png", "sandstone_shared.png" })
                SetTexture($"{Tex}/{t}", 2048, 8, TextureWrapMode.Repeat, false);
            SetTexture($"{Tex}/lake_ripple_normal.png", 1024, 4, TextureWrapMode.Repeat, true);
            AssetDatabase.SaveAssets();
        }

        static void SetTexture(string path, int max, int aniso, TextureWrapMode wrap, bool normal)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            if (ti == null) { Debug.LogWarning("missing texture " + path); return; }
            ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.maxTextureSize = max; ti.anisoLevel = aniso; ti.filterMode = FilterMode.Trilinear; ti.wrapMode = wrap;
            ti.mipmapEnabled = true; ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
        }

        // ------------------------------------------------------------------------------------------------ materials
        static Shader TeamLit => Shader.Find("OpenZ/TeamLit");
        static Shader UrpLit => Shader.Find("Universal Render Pipeline/Lit");

        static Material SaveMat(string folder, string name, Material m)
        {
            Directory.CreateDirectory(folder);
            string p = $"{folder}/{San(name)}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (existing != null) { existing.shader = m.shader; existing.CopyPropertiesFromMaterial(m); EditorUtility.SetDirty(existing); return existing; }
            AssetDatabase.CreateAsset(m, p); return m;
        }

        static string San(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_'));

        static Material TeamMat(Color baseCol, Texture tex = null, bool vcol = false, int teamMode = 0, float smooth = 0.3f, Color? emission = null)
        {
            var m = new Material(TeamLit);
            m.SetColor("_BaseColor", baseCol); if (tex) m.SetTexture("_BaseMap", tex);
            m.SetFloat("_UseVertexColor", vcol ? 1 : 0); m.SetFloat("_TeamMode", teamMode); m.SetFloat("_Smoothness", smooth);
            if (emission.HasValue) m.SetColor("_EmissionColor", emission.Value);
            m.enableInstancing = true;
            return m;
        }

        static Material Lit(Texture tex, float smooth)
        {
            var m = new Material(UrpLit); m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", smooth); m.SetFloat("_SpecularHighlights", 0f); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.SetFloat("_EnvironmentReflections", 0f); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            m.enableInstancing = true; return m;
        }

        static Material Cliff(Texture strata)
        {
            var m = TeamMat(Color.white, strata, false, 0, 0.05f);
            m.SetFloat("_Triplanar", 1); m.SetFloat("_TriScale", 0.045f); m.SetFloat("_TriHeight", 18f);
            return m;
        }

        /// <summary>OpenZ/WaterStylized presets (Z 1996 palette): L02 blue-grey murky river, L17 olive murky harbour/canals.</summary>
        static Material WaterStylized(bool harbour)
        {
            var m = new Material(Shader.Find("OpenZ/WaterStylized"));
            if (m.shader != null && m.shader.FindPropertyIndex("_MidColor") >= 0) { OpenZWaterV3.ApplyPreset(m, harbour); return m; }   // water_v3
            void C(string p, float r, float g, float b, float a = 1f) => m.SetColor(p, new Color(r, g, b, a));
            void F(string p, float v) => m.SetFloat(p, v);
            if (!harbour)
            {   // L02 river: Z samples 119,119,143 / 127,115,115 -> clean blue-grey slate
                C("_ColorShallow", 0.41f, 0.40f, 0.49f, 0.60f); C("_ColorDeep", 0.33f, 0.33f, 0.46f, 0.95f);
                F("_DepthDistance", 0.35f); F("_EdgeFade", 0.04f);
                C("_ColorSky", 0.62f, 0.70f, 0.80f); F("_FresnelStrength", 0.5f); F("_ShadowStrength", 0.65f);
                F("_RippleTiling1", 0.45f); F("_RippleTiling2", 1.1f); F("_RippleSlope", 0.35f); F("_RippleSpeed", 0.2f);
                F("_RippleDirection", 90f); F("_RippleShading", 0.4f); F("_LineStrength", 0.12f); F("_LineWidth", 0.05f);
                C("_ColorFoam", 0.84f, 0.86f, 0.88f, 0.85f); F("_FoamWidth", 0.45f); F("_FoamTiling", 1.6f); F("_FoamSpeed", 0.12f);
                F("_FoamBreakup", 0.8f); F("_FoamSoftness", 0.04f); F("_FoamPulse", 0.15f); F("_SurfaceFoam", 0.1f);
                F("_SunGlintIntensity", 2.0f); F("_AddLightStrength", 1.0f); F("_FallbackDepth", 0.6f);
            }
            else
            {   // L17 harbour + canals: Z samples 59,55,27 -> olive murky, a touch lifted for readability
                C("_ColorShallow", 0.298f, 0.290f, 0.188f, 0.75f); C("_ColorDeep", 0.243f, 0.235f, 0.118f, 1.0f);
                F("_DepthDistance", 0.3f); F("_EdgeFade", 0.03f);
                C("_ColorSky", 0.55f, 0.58f, 0.55f); F("_FresnelStrength", 0.35f); F("_ShadowStrength", 0.6f);
                F("_RippleTiling1", 0.3f); F("_RippleTiling2", 0.8f); F("_RippleSlope", 0.3f); F("_RippleSpeed", 0.08f);
                F("_RippleDirection", 0f); F("_RippleShading", 0.35f); F("_LineStrength", 0.08f); F("_LineWidth", 0.04f);
                C("_ColorFoam", 0.66f, 0.66f, 0.56f, 0.6f); F("_FoamWidth", 0.5f); F("_FoamTiling", 1.4f); F("_FoamSpeed", 0.05f);
                F("_FoamBreakup", 0.85f); F("_FoamSoftness", 0.05f); F("_FoamPulse", 0.1f); F("_SurfaceFoam", 0.05f);
                F("_SunGlintIntensity", 1.4f); F("_AddLightStrength", 1.2f); F("_FallbackDepth", 0.8f);
            }
            F("_FresnelPower", 3f); F("_AmbientStrength", 1f);
            F("_SunGlintPower", 300f); F("_SunGlintThreshold", 0.6f); F("_SunGlintSoftness", 0.06f); F("_SunGlintViewBias", 0.45f);
            F("_AdditionalLights", 1f); m.DisableKeyword("_WATER_NO_ADDITIONAL_LIGHTS");
            F("_VertexWaves", 0f); m.DisableKeyword("_WATER_VERTEX_WAVES");
            F("_WaveHeight", 0.03f); F("_WaveLength", 6f); F("_WaveSpeed", 0.8f);
            F("_NoDepth", 0f); m.DisableKeyword("_WATER_NO_DEPTH");
            return m;
        }

        [MenuItem("OpenZ/Apply Stylized Water")]
        public static void ApplyStylizedWater()
        {   // updates the two existing material assets in place (GUIDs kept): no FBX re-import needed
            var ws = Shader.Find("OpenZ/WaterStylized"); if (ws != null && ws.FindPropertyIndex("_MidColor") >= 0) { OpenZWaterV3.MaterialsOnly(); return; }   // water_v3
            foreach (var (name, harbour) in new[] { ("M_Water", false), ("M_Water_Harbour", true) })
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>($"{MatEnv}/{name}.mat");
                if (mat == null) { Debug.LogWarning("missing " + name); continue; }
                var w = WaterStylized(harbour);
                mat.shader = w.shader; mat.CopyPropertiesFromMaterial(w); mat.shaderKeywords = w.shaderKeywords;
                mat.renderQueue = -1;   // shader queue: Transparent-10
                EditorUtility.SetDirty(mat);
            }
            AssetDatabase.SaveAssets();
        }

        static Material Water(Color col, Color sky, float glint)
        {
            var m = new Material(Shader.Find("OpenZ/Water"));
            m.SetColor("_ShallowColor", col); m.SetColor("_SkyColor", sky); m.SetFloat("_GlintIntensity", glint);
            m.SetTexture("_RippleMap", AssetDatabase.LoadAssetAtPath<Texture>($"{Tex}/lake_ripple_normal.png"));
            // critic: 4 m ripple tiling read as a dotted grid and the glints blew the river out to white
            m.SetFloat("_RippleScale", 0.125f); m.SetFloat("_RippleScale2", 0.0435f); m.SetFloat("_RippleStrength", 0.3f); m.SetFloat("_GlintPower", 250f);
            return m;
        }

        /// <summary>Material for an FBX material name (environment, bridges, props). Unknown names keep their colour on TeamLit.</summary>
        static Material EnvMaterial(string name, Material src, bool hasVertexColours)
        {
            Texture T(string f) => AssetDatabase.LoadAssetAtPath<Texture>($"{Tex}/{f}");
            string n = name;
            if (n == "M_L02_Ground")
            {
                var sand = new Material(Shader.Find("OpenZ/Sand")); sand.SetTexture("_BaseMap", T("L02_ground_paint.png"));
                var gm02 = SaveMat(MatEnv, n, sand); OpenZTexturesV2.ApplyGround(gm02, "L02"); return gm02;   // textures_v2: crisp roads + detail
            }
            if (n == "M_L17_Ground") { var g = Lit(T("L17_ground_paint.png"), 0.05f); g.SetColor("_BaseColor", new Color(0.86f, 0.87f, 0.9f)); var gm17 = SaveMat(MatEnv, n, g); OpenZTexturesV2.ApplyGround(gm17, "L17"); return gm17; }   // textures_v2
            if (n == "M_L16_Ground") { var g16 = SaveMat(MatEnv, n, Lit(T("L16_ground_paint.png"), 0.05f)); OpenZL16Look.ApplyGround(g16, false); return g16; }   // level 16: the layered ground (the baked far picture stays as its far view)
            if (n == "M_Cliff_JungleRock") { var c16 = SaveMat(MatEnv, n, Cliff(T("L16_cliff_rock.png"))); OpenZL16Look.ApplyGround(c16, true); return c16; }   // level 16 rock walls: the same layered recipe (cliff rock from the side), casting shadows
            if (n == "M_Water_Swamp") return SaveMat(MatEnv, n, WaterStylized(true));                      // level 16, first water: the murky preset (the swamp preset replaces it)
            if (n == "M_L16_Ground") return SaveMat(MatEnv, n, Lit(T("L16_ground_paint.png"), 0.05f));   // level 16, first ground: the baked far picture (the layered ground replaces it)
            if (n == "M_Cliff_JungleRock") return SaveMat(MatEnv, n, Cliff(T("L16_cliff_rock.png")));
            if (n == "M_Water_Swamp") return SaveMat(MatEnv, n, WaterStylized(true));                      // level 16, first water: the murky preset (the swamp preset replaces it)
            if (n == "M_Cliff_Sandstone") return SaveMat(MatEnv, n, Cliff(T("cliff_strata.png")));
            if (n == "M_Cliff_CityRock") return SaveMat(MatEnv, n, Cliff(T("cliff_strata_city.png")));
            if (n == "M_QuayWall") { var q = TeamMat(new Color(0.58f, 0.58f, 0.56f), null, false, 0, 0.1f); return SaveMat(MatEnv, n, q); }   // #9 concrete quay walls
            if (n == "M_Water") return SaveMat(MatEnv, n, WaterStylized(false));   // helper package: Z blue-grey murky river
            // harbour: softer sparkle (critic r7: loose diagonal rows)
            if (n == "M_Water_Harbour") return SaveMat(MatEnv, n, WaterStylized(true));   // Z olive murky harbour
            if (n.StartsWith("DV3_Sandstone")) return SaveMat(MatEnv, "DV3_Sandstone_Textured", TeamMat(Color.white, T("sandstone_shared.png"), false, 0, 0.05f));
            if (n.StartsWith("DV3_GroundPaint")) return SaveMat(MatEnv, "DV3_GroundPaint", TeamMat(new Color(0.91f, 0.70f, 0.38f), null, false, 0, 0.05f));
            if (n.StartsWith("DV3_BatchedGlow") || n.Contains("amber_glass"))
            {
                var gm = TeamMat(new Color(1f, 0.85f, 0.6f), null, hasVertexColours, 0, 0.6f, new Color(0.9f, 0.62f, 0.32f)); gm.SetFloat("_VertexColorSRGB", 1);
                return SaveMat(MatEnv, n.Split('.')[0], gm);
            }
            Color c = src == null ? Color.white : src.shader.name == "OpenZ/TeamLit" ? src.GetColor("_BaseColor") : src.HasProperty("_Color") ? src.color : Color.white;
            if (n.StartsWith("DV3_BatchedPalette")) c = Color.white;
            var em = TeamMat(c, null, hasVertexColours, 0, n.Contains("metal") || n.Contains("Steel") ? 0.45f : 0.2f);
            em.SetFloat("_VertexColorSRGB", 1);   // Blender writes FBX vertex colours as sRGB (critic: bleached planks and crates)
            return SaveMat(MatEnv, n.Split('.')[0], em);
        }

        [MenuItem("OpenZ/2 Remap Environment Materials")]
        public static void RemapEnvironment() => RemapEnvironmentOf("L02_environment", "L17_environment", "BRG_desert", "BRG_city", "props_map12", "props_vicecity");

        static void RemapEnvironmentOf(params string[] files)
        {
            foreach (var f in files)
            {
                string p = $"{Env}/{f}.fbx";
                var imp = (ModelImporter)AssetImporter.GetAtPath(p);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (imp == null || go == null) { Debug.LogWarning("missing " + p); continue; }
                imp.importCameras = false; imp.importLights = false; imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                imp.bakeAxisConversion = true;   // Blender exports without "Apply Transform" (it broke rotated children): Unity bakes the axes
                // which materials sit on vertex-coloured meshes
                // names compared without the ".005" suffix: after the first remap the renderers carry OUR material names
                // (the first version compared raw names, so re-runs saved the palette without vertex colours -> bleached props)
                string Key(string nm) => San(nm.Split('.')[0]);
                var vc = new HashSet<string>();
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf && mf.sharedMesh && mf.sharedMesh.colors32.Length > 0) foreach (var m in r.sharedMaterials) if (m) vc.Add(Key(m.name));
                }
                // embedded materials exist only before the first remap: also walk the existing remap table (the first
                // version walked embedded ones only, so later runs updated nothing: the water kept its old settings)
                var names = new Dictionary<string, Material>();
                foreach (var src in AssetDatabase.LoadAllAssetsAtPath(p).OfType<Material>()) names[src.name] = src;
                foreach (var kv in imp.GetExternalObjectMap()) if (kv.Key.type == typeof(Material) && !names.ContainsKey(kv.Key.name)) names[kv.Key.name] = kv.Value as Material;
                foreach (var kv in names)
                    imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), EnvMaterial(kv.Key, kv.Value, vc.Contains(Key(kv.Key))));
                imp.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------------------------------------ 3. prefabs
        class ModelDef { public string key, path, child; public Vector2 footprint; public float length, height, yaw; public bool gen, sideways; }

        static readonly ModelDef[] Models =
        {
            // buildings (Zod footprints, tiles * 2 m): fort 10x12, factories 4x5, repair 5x4, radar 4x3
            new ModelDef { key = "fort", path = "Models/Gen/m01_fort.glb", footprint = new Vector2(20, 24), gen = true },
            new ModelDef { key = "robot_factory", path = "Models/Gen/m02_robot_factory.glb", footprint = new Vector2(8, 10), gen = true },
            new ModelDef { key = "vehicle_factory", path = "Models/Gen/m03_vehicle_factory.glb", footprint = new Vector2(8, 10), gen = true },
            new ModelDef { key = "repair", path = "Models/Gen/m04_repair_station.glb", footprint = new Vector2(10, 8), gen = true },
            new ModelDef { key = "radar", path = "Models/Gen/m05_radar.glb", footprint = new Vector2(8, 6), gen = true },
            // cannons (2x2 tiles)
            new ModelDef { key = "gatling", path = "Models/Gen/m09_gatling.glb", footprint = new Vector2(4, 4), gen = true },
            new ModelDef { key = "howitzer", path = "Models/Gen/m10_howitzer.glb", footprint = new Vector2(4, 4), gen = true },
            new ModelDef { key = "missile_cannon", path = "Models/Gen/m11_missile_cannon.glb", footprint = new Vector2(4, 4), gen = true },
            new ModelDef { key = "gun", path = "Models/Legacy/cannon.glb", length = 4.0f },
            // vehicles (chunky, ~2-2.5 tiles long); imported vehicles face along X -> turned 90 deg
            new ModelDef { key = "jeep", path = "Models/Legacy/jeep.glb", length = 4.2f },
            new ModelDef { key = "light", path = "Models/Legacy/tank.glb", length = 4.0f },
            new ModelDef { key = "medium", path = "Models/Gen/m08_medium_tank.glb", length = 4.8f, gen = true, sideways = true },
            new ModelDef { key = "heavy", path = "Models/Legacy/heavy_tank.glb", length = 5.2f },
            new ModelDef { key = "apc", path = "Models/Gen/m06_apc.glb", length = 4.8f, gen = true, sideways = true },
            new ModelDef { key = "missile_launcher", path = "Models/Legacy/mlrs.glb", length = 4.9f },
            new ModelDef { key = "crane", path = "Models/Gen/m07_crane.glb", length = 4.8f, gen = true, sideways = true },
            // robots
            new ModelDef { key = "robot", path = "Models/Legacy/other-bot.glb", height = -1 },
            new ModelDef { key = "tough", path = "Models/Legacy/mech.glb", height = 2.8f },
        };

        static readonly (string key, string fbx, string child, float scale)[] Props =
        {
            ("grenades", "props_map12", "PRP_Supply_crate_02", 1f), ("hut", "props_map12", "PRP_Saloon_1", 0.25f),
            ("barrel", "props_map12", "PRP_Barrel_00", 1f), ("crate", "props_map12", "PRP_Supply_crate_05", 1f),
            ("rock_desert_0", "props_map12", "PRP_Interior_rock_cluster_00", 0.7f), ("rock_desert_1", "props_map12", "PRP_Interior_rock_cluster_02", 0.7f),
            ("rock_desert_2", "props_map12", "PRP_Interior_rock_cluster_05", 0.7f), ("rock_desert_3", "props_map12", "PRP_Interior_rock_cluster_08", 0.7f),
            ("cactus_0", "props_map12", "PRP_Cactus_group_00", 0.8f), ("cactus_1", "props_map12", "PRP_Cactus_group_01", 0.8f),
            ("cactus_2", "props_map12", "PRP_Cactus_group_02", 0.8f), ("city_block", "props_vicecity", "PRP_City_block", 0.95f),
            ("stop_sign", "props_vicecity", "PRP_round13_stop_sign", 1f),
            ("bridge_desert_intact", "BRG_desert", "BRG_desert_intact", 1f), ("bridge_desert_destroyed", "BRG_desert", "BRG_desert_destroyed", 1f),
            ("bridge_city_intact", "BRG_city", "BRG_city_intact", 1f), ("bridge_city_destroyed", "BRG_city", "BRG_city_destroyed", 1f),
            ("bridge_desert_damaged", "BRG_desert", "BRG_desert_damaged", 1f), ("bridge_city_damaged", "BRG_city", "BRG_city_damaged", 1f),
        };

        internal static Material UnitMaterial(string model, Material src, bool vcol, bool gen)
        {
            var m = new Material(TeamLit);
            Texture tex = src.HasProperty("baseColorTexture") ? src.GetTexture("baseColorTexture") : src.mainTexture;
            Color col = src.HasProperty("baseColorFactor") ? src.GetColor("baseColorFactor") : Color.white;
            if (tex) m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", col);
            float rough = src.HasProperty("roughnessFactor") ? src.GetFloat("roughnessFactor") : 0.7f;
            float metal = src.HasProperty("metallicFactor") ? src.GetFloat("metallicFactor") : 0f;
            m.SetFloat("_Smoothness", Mathf.Clamp01(1 - rough) * 0.8f); m.SetFloat("_Metallic", Mathf.Clamp01(metal) * 0.6f);
            if (src.HasProperty("emissiveFactor")) { var e = src.GetColor("emissiveFactor"); if (e.maxColorComponent > 0.01f) m.SetColor("_EmissionColor", e * 0.9f); }
            m.SetFloat("_UseVertexColor", vcol ? 1 : 0);
            string n = src.name;
            if (n.StartsWith("SkinPBR")) { m.SetFloat("_TeamMode", 2); m.SetFloat("_ShadeMin", 1.0f); m.SetFloat("_ShadeMax", 1.0f); m.SetFloat("_TeamLift", 0f); }   // robot armour: vertex alpha mask, exact fort red/blue (white armour x1.6 + lift read pink)
            // (texture alpha is NOT the owner mask on the legacy vehicles: tried, tanks turned green) -> hue band
            else if (n.StartsWith("DV3_VehiclePBR_Masked") && model == "jeep")
            { m.SetFloat("_TeamMode", 1); m.SetFloat("_HueMin", 0.02f); m.SetFloat("_SatMin", 0.12f); m.SetFloat("_ValMax", 0.85f); m.SetFloat("_ShadeMin", 0.95f); }   // jeep: tan-brown camo + olive
            else if (n.StartsWith("HeavyTank_body") || n.StartsWith("HeavyTank_head"))
            { m.SetFloat("_TeamMode", 1); m.SetFloat("_HueMin", 0.42f); m.SetFloat("_HueMax", 0.64f); m.SetFloat("_SatMin", 0.18f);
              m.SetFloat("_HueMin2", 0.02f); m.SetFloat("_HueMax2", 0.12f); m.SetFloat("_SatMin2", 0.35f); m.SetFloat("_ValMax", 0.9f); }   // #12 orange panels too
            else if (n.StartsWith("SoldierArmor")) m.SetFloat("_TeamMode", 0);                 // robot rifle: plain gunmetal (critic N4 team speckle)
            else m.SetFloat("_TeamMode", 1);                                                      // olive paint band 0.12-0.34
            if (gen) { m.SetFloat("_SatScale", 0.78f); m.SetFloat("_ValScale", 0.88f); }       // imported lime -> legacy olive match
            if (gen && tex) m.SetFloat("_MaskDilate", 3f);   // cream pinstripes along panel edges (critic #15)
            // imported buildings: cream stucco walls carry faint greenish stains -> stricter paint test (no team flecks on the walls)
            if (model == "m01_fort" || model == "m02_robot_factory" || model == "m03_vehicle_factory" || model == "m04_repair_station" || model == "m05_radar")
            { m.SetFloat("_ValMax", 0.42f); m.SetFloat("_SatMin", 0.42f); }
            m.enableInstancing = true;
            bool building = model == "m01_fort" || model == "m02_robot_factory" || model == "m03_vehicle_factory" || model == "m04_repair_station" || model == "m05_radar";
            if (!building) OpenZStyle.ApplyUnitStyle(m, model == "other-bot" || n.StartsWith("SkinPBR"));
            return SaveMat(MatUnits, $"{model}_{(string.IsNullOrEmpty(n) ? "mat" : n)}", m);
        }

        [MenuItem("OpenZ/3 Build Prefabs")]
        public static void BuildPrefabs()
        {
            Directory.CreateDirectory(Pref);
            var controller = BuildRobotController();
            foreach (var d in Models)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/{d.path}");
                if (!asset) { Debug.LogWarning("missing " + d.path); continue; }
                var root = new GameObject("Z_" + d.key);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                model.name = "Model"; model.transform.SetParent(root.transform, false);
                // TeamLit materials
                string mk = Path.GetFileNameWithoutExtension(d.path);
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                {
                    Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                    bool vcol = mesh && mesh.colors32.Length > 0;
                    r.sharedMaterials = r.sharedMaterials.Select(m => m ? UnitMaterial(mk, m, vcol, d.gen) : m).ToArray();
                    r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true;
                }
                if (d.sideways) model.transform.localRotation = Quaternion.Euler(0, 90, 0);
                // scale to footprint / length / height, then pivot = ground centre
                Bounds b = WorldBounds(model);
                float s = 1f;
                if (d.footprint != Vector2.zero) s = Mathf.Min(d.footprint.x / b.size.x, d.footprint.y / b.size.z);
                else if (d.length > 0) s = d.length / Mathf.Max(b.size.x, b.size.z);
                else if (d.height > 0) s = d.height / b.size.y;
                model.transform.localScale *= s;
                b = WorldBounds(model);
                model.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
                if (d.key == "robot")
                {
                    var an = model.GetComponentInChildren<Animator>(); if (!an) an = model.AddComponent<Animator>();
                    an.runtimeAnimatorController = controller; an.applyRootMotion = false;
                    an.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                    // store the idle pose so robots never stand in the bind (T) pose in the editor / before the Animator runs
                    var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Root}/Animation/Robot/idle.anim");
                    if (idle) idle.SampleAnimation(an.gameObject, 0.1f);
                }
                root.AddComponent<TeamPaint>();
                PrefabUtility.SaveAsPrefabAsset(root, $"{Pref}/Z_{d.key}.prefab");
                UnityEngine.Object.DestroyImmediate(root);
            }
            foreach (var p in Props)
            {
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{Env}/{p.fbx}.fbx");
                var src = fbx ? fbx.transform.Cast<Transform>().FirstOrDefault(t => t.name == p.child) : null;
                if (!src) { Debug.LogWarning($"missing prop {p.child} in {p.fbx}"); continue; }
                var root = new GameObject("Z_" + p.key);
                var inst = UnityEngine.Object.Instantiate(src.gameObject, root.transform, false);
                inst.name = "Model"; inst.transform.localPosition = Vector3.zero; inst.transform.localScale = src.localScale * p.scale;
                Bounds b = WorldBounds(inst);
                inst.transform.localPosition = -new Vector3(b.center.x, b.min.y, b.center.z);
                if (p.key.StartsWith("bridge")) inst.transform.localPosition = new Vector3(inst.transform.localPosition.x, 0, inst.transform.localPosition.z);
                root.AddComponent<TeamPaint>();
                if (p.key == "barrel") HazardBarrel(root);
                PrefabUtility.SaveAsPrefabAsset(root, $"{Pref}/Z_{p.key}.prefab");
                UnityEngine.Object.DestroyImmediate(root);
            }
            BuildFlag();
            if (!Pf("city_block")) BuildCityBlock();
            AssetDatabase.SaveAssets();
        }

        /// <summary>Critic N1: the plank gaps of the intact bridge decks showed the river as cyan hairlines. A thin dark wood
        /// plate under the planks (deck height from raycasts on the bridge mesh, inside the rails) closes them.</summary>
        static string DeckUnderlays(Transform zroot)
        {
            var mat = SaveMat(MatEnv, "M_DeckUnderlay", TeamMat(new Color(0.20f, 0.12f, 0.06f), null, false, 0, 0.05f));
            int n = 0;
            foreach (var t in zroot.Cast<Transform>().ToList())
            {
                if (!t.name.StartsWith("bridge_")) continue;
                var src = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                if (!src || !src.name.Contains("intact")) continue;
                var tmp = new List<MeshCollider>();
                foreach (var mf in t.GetComponentsInChildren<MeshFilter>()) { var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; tmp.Add(mc); }
                Physics.SyncTransforms();
                var b = WorldBounds(t.gameObject); var tops = new List<float>();
                for (int i = 0; i < 5; i++)
                    for (int j = 0; j < 5; j++)
                    {
                        var o = new Vector3(Mathf.Lerp(b.min.x, b.max.x, 0.3f + 0.1f * i), 300, Mathf.Lerp(b.min.z, b.max.z, 0.3f + 0.1f * j));
                        float hy = float.MinValue;
                        foreach (var h in Physics.RaycastAll(o, Vector3.down, 600)) if (tmp.Contains(h.collider as MeshCollider)) hy = Mathf.Max(hy, h.point.y);
                        if (hy > float.MinValue) tops.Add(hy);
                    }
                foreach (var mc in tmp) UnityEngine.Object.DestroyImmediate(mc);
                Physics.SyncTransforms();
                if (tops.Count == 0) continue;
                tops.Sort(); float top = tops[tops.Count / 2];
                var u = GameObject.CreatePrimitive(PrimitiveType.Cube);
                UnityEngine.Object.DestroyImmediate(u.GetComponent<Collider>());
                u.name = "underlay_" + t.name; u.transform.SetParent(zroot, true);
                u.transform.position = new Vector3(b.center.x, top - 0.12f, b.center.z);
                u.transform.localScale = new Vector3(b.size.x * 0.8f, 0.04f, b.size.z * 0.8f);
                var r = u.GetComponent<Renderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off;
                n++;
            }
            return $"deck underlays: {n}";
        }

        /// <summary>Bright red M12 barrels read as RED-team objects in neutral city blocks (critic N5): own material,
        /// palette hue shifted to hazard yellow-orange (TeamLit _HueShift), everything else unchanged.</summary>
        public static void HazardBarrel(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterials = r.sharedMaterials.Select(m =>
                {
                    if (!m) return m;
                    string path = $"{MatEnv}/{m.name}_HazardBarrel.mat";
                    var hm = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (!hm) { hm = new Material(m); AssetDatabase.CreateAsset(hm, path); }
                    hm.SetFloat("_HueShift", 0.11f); hm.SetFloat("_TeamMode", 0); EditorUtility.SetDirty(hm);
                    return hm;
                }).ToArray();
        }

        /// <summary>Zod city "rock" = dark concrete block (not a PRP_ root in props_vicecity, so rebuilt here like in Blender).</summary>
        static void BuildCityBlock()
        {
            var root = new GameObject("Z_city_block");
            var mat = SaveMat(MatUnits, "city_block_concrete", TeamMat(new Color(0.30f, 0.29f, 0.28f), null, false, 0, 0.15f));
            foreach (var (pos, size) in new[] { (new Vector3(0, 0.55f, 0), new Vector3(1.7f, 1.1f, 1.7f)), (new Vector3(0, 1.15f, 0), new Vector3(1.3f, 0.12f, 1.3f)) })
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube); UnityEngine.Object.DestroyImmediate(c.GetComponent<Collider>());
                c.transform.SetParent(root.transform, false); c.transform.localPosition = pos * 0.95f; c.transform.localScale = size * 0.95f;
                c.GetComponent<Renderer>().sharedMaterial = mat;
            }
            root.AddComponent<TeamPaint>();
            PrefabUtility.SaveAsPrefabAsset(root, $"{Pref}/Z_city_block.prefab");
            UnityEngine.Object.DestroyImmediate(root);
        }

        static void BuildFlag()
        {
            var root = new GameObject("Z_flag");
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder); pole.name = "Pole";
            UnityEngine.Object.DestroyImmediate(pole.GetComponent<Collider>());
            pole.transform.SetParent(root.transform, false); pole.transform.localScale = new Vector3(0.12f, 1.6f, 0.12f); pole.transform.localPosition = new Vector3(-0.5f, 1.6f, 0);
            pole.GetComponent<Renderer>().sharedMaterial = SaveMat(MatUnits, "flag_pole", TeamMat(new Color(0.35f, 0.36f, 0.38f), null, false, 0, 0.5f));
            var ban = GameObject.CreatePrimitive(PrimitiveType.Cube); ban.name = "Banner";
            UnityEngine.Object.DestroyImmediate(ban.GetComponent<Collider>());
            ban.transform.SetParent(root.transform, false); ban.transform.localScale = new Vector3(1.2f, 0.8f, 0.05f); ban.transform.localPosition = new Vector3(0.1f, 2.75f, 0);
            var bm = TeamMat(Color.white, null, false, 1, 0.2f); bm.SetFloat("_HueMin", 0); bm.SetFloat("_HueMax", 1); bm.SetFloat("_SatMin", 0); bm.SetFloat("_TeamRef", 0.9f);
            ban.GetComponent<Renderer>().sharedMaterial = SaveMat(MatUnits, "flag_banner", bm);      // the whole banner is the owner colour
            root.AddComponent<TeamPaint>();
            PrefabUtility.SaveAsPrefabAsset(root, $"{Pref}/Z_flag.prefab");
            UnityEngine.Object.DestroyImmediate(root);
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        static readonly string[] LoopClips = { "idle", "walking", "run_forward", "run_backward", "strafe_left", "strafe_right", "shooting",
                                               "seated_driver", "seated_gunner", "bazooka_idle", "aim_up", "aim_down", "climb_ladder", "blast_airborne" };

        static AnimatorController BuildRobotController()
        {
            string glb = $"{Root}/Models/Legacy/other-bot.glb", dir = $"{Root}/Animation/Robot";
            Directory.CreateDirectory(dir);
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var c in AssetDatabase.LoadAllAssetsAtPath(glb).OfType<AnimationClip>())
            {
                string p = $"{dir}/{c.name}.anim";
                var copy = UnityEngine.Object.Instantiate(c); copy.name = c.name;
                var st = AnimationUtility.GetAnimationClipSettings(copy); st.loopTime = LoopClips.Contains(c.name); AnimationUtility.SetAnimationClipSettings(copy, st);
                if (AssetDatabase.LoadAssetAtPath<AnimationClip>(p)) AssetDatabase.DeleteAsset(p);
                AssetDatabase.CreateAsset(copy, p); clips[c.name] = copy;
            }
            string cp = $"{Root}/Animation/Robot.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(cp)) AssetDatabase.DeleteAsset(cp);
            var ctl = AnimatorController.CreateAnimatorControllerAtPath(cp);
            var sm = ctl.layers[0].stateMachine;
            foreach (var kv in clips.OrderBy(k => k.Key == "idle" ? 0 : 1))
            {
                var s = sm.AddState(kv.Key); s.motion = kv.Value;
                if (kv.Key == "idle") sm.defaultState = s;
            }
            Debug.Log($"Robot controller: {clips.Count} clips");
            return ctl;
        }

        // ------------------------------------------------------------------------------------------------ 4. look
        [MenuItem("OpenZ/4 Setup Map 12 Look")]
        public static void SetupLook()
        {
            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline = rp;
            for (int i = 0; i < QualitySettings.names.Length; i++) { QualitySettings.SetQualityLevel(i, false); QualitySettings.renderPipeline = rp; }
            QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, true);
            var so = new SerializedObject(rp);
            void Set(string prop, Action<SerializedProperty> f) { var p = so.FindProperty(prop); if (p != null) f(p); else Debug.LogWarning("URP asset has no " + prop); }
            Set("m_SupportsHDR", p => p.boolValue = true);
            Set("m_MainLightShadowmapResolution", p => p.intValue = 4096);
            Set("m_ShadowDistance", p => p.floatValue = 260f);
            Set("m_ShadowCascadeCount", p => p.intValue = 4);
            Set("m_SoftShadowsSupported", p => p.boolValue = true);
            Set("m_SoftShadowQuality", p => p.intValue = 3);
            Set("m_AdditionalLightsRenderingMode", p => p.intValue = 1);
            Set("m_AdditionalLightsPerObjectLimit", p => p.intValue = 8);
            Set("m_MSAA", p => p.intValue = 4);
            Set("m_ColorGradingMode", p => p.intValue = 1);   // HDR grading
            Set("m_ShadowDepthBias", p => p.floatValue = 0.5f);   // critic #7: thin robot shadows detached ("hovering")
            Set("m_ShadowNormalBias", p => p.floatValue = 0.3f);
            so.ApplyModifiedPropertiesWithoutUndo();
            Directory.CreateDirectory($"{Root}/Settings");
            string vp = $"{Root}/Settings/OpenZ_PostFX.asset";
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(vp);
            if (!prof) { prof = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(prof, vp); }
            prof.components.RemoveAll(x => x == null);
            // components must be saved INTO the profile asset (the first version only added them in memory: every domain
            // reload dropped bloom / grading and only URP's default profile applied)
            T Comp<T>() where T : VolumeComponent
            {
                if (!prof.TryGet<T>(out var c)) { c = prof.Add<T>(true); c.name = typeof(T).Name; AssetDatabase.AddObjectToAsset(c, prof); }
                c.active = true; return c;
            }
            var mblur = Comp<MotionBlur>();
            mblur.mode.Override(MotionBlurMode.CameraAndObjects); mblur.intensity.Override(0.3f); mblur.clamp.Override(0.04f); mblur.quality.Override(MotionBlurQuality.Medium);
            var bloom = Comp<Bloom>();          // only HDR effects glow: fireballs, muzzle flashes, lasers, lamps at night, water glints
            bloom.threshold.Override(1.1f); bloom.intensity.Override(0.8f); bloom.scatter.Override(0.7f); bloom.highQualityFiltering.Override(true);
            var tm = Comp<Tonemapping>(); tm.mode.Override(TonemappingMode.ACES);   // critic #5: Neutral capped whites at ~208; None clipped the sand red channel (flat yellow). ACES +0.2 EV = Blender sand, whites 249
            var wb = Comp<WhiteBalance>(); wb.temperature.Override(0f); wb.tint.Override(4f);   // warmer, less olive
            var ca = Comp<ColorAdjustments>(); ca.saturation.Override(0f); ca.contrast.Override(5f); ca.postExposure.Override(0.2f);   // calibrated on the live Game view against blender_L02_red_fort
            var vg = Comp<Vignette>(); vg.intensity.Override(0.18f); vg.smoothness.Override(0.45f);
            EditorUtility.SetDirty(prof); AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------------------------------------ 4b. atmosphere
        [MenuItem("OpenZ/4b Setup Atmosphere (volumetric fog)")]
        public static string SetupAtmosphere()
        {
            var mat = SaveMat($"{Root}/Materials", "M_Atmosphere", new Material(Shader.Find("OpenZ/AtmosphereFullscreen")));
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            if (!data) return "PC_Renderer not found";
            var feat = data.rendererFeatures.OfType<FullScreenPassRendererFeature>().FirstOrDefault(f => f.name == "OpenZ Atmosphere");
            if (!feat)
            {
                feat = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>(); feat.name = "OpenZ Atmosphere";
                AssetDatabase.AddObjectToAsset(feat, data);
                data.rendererFeatures.Add(feat);
                var so = new SerializedObject(data); var map = so.FindProperty("m_RendererFeatureMap");
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feat, out string _, out long localId);
                map.arraySize++; map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            feat.passMaterial = mat; feat.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing;
            feat.requirements = ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Color; feat.fetchColorBuffer = true; feat.SetActive(true);
            EditorUtility.SetDirty(feat); EditorUtility.SetDirty(data); AssetDatabase.SaveAssets();
            AddSsao(data);
            return "atmosphere feature on " + data.name + " (features: " + string.Join(", ", data.rendererFeatures.Select(f => f ? f.name : "null")) + ")";
        }

        static void AddSsao(UniversalRendererData data)
        {
            var t = typeof(UniversalRendererData).Assembly.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion");
            if (t == null || data.rendererFeatures.Any(f => f && f.GetType() == t)) return;
            var feat = (ScriptableRendererFeature)ScriptableObject.CreateInstance(t); feat.name = "SSAO";
            AssetDatabase.AddObjectToAsset(feat, data); data.rendererFeatures.Add(feat);
            var so = new SerializedObject(data); var map = so.FindProperty("m_RendererFeatureMap");
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feat, out string _, out long localId);
            map.arraySize++; map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId; so.ApplyModifiedPropertiesWithoutUndo();
            var fs = new SerializedObject(feat);
            void F(string p, float v) { var sp = fs.FindProperty("m_Settings." + p); if (sp != null) sp.floatValue = v; }
            F("Intensity", 1.2f); F("Radius", 0.6f); F("DirectLightingStrength", 0.3f); fs.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(feat); EditorUtility.SetDirty(data); AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------------------------------------ 5. level scene
        [Serializable] class ZObj { public int x, y; public string owner, kind; public int blevel, extra_links, health_percent; }
        [Serializable] class ZMapJson { public string file, planet; public int width, height; public int[] classes; public ZObj[] objects; }

        static readonly Dictionary<string, int> GroupSize = new Dictionary<string, int>
            { { "grunt", 3 }, { "psycho", 3 }, { "sniper", 3 }, { "tough", 2 }, { "pyro", 4 }, { "laser", 4 } };

        /// <summary>Level 16 only: import settings of its ground picture and rock texture, then the materials of its environment
        /// model. Never touches a material, prefab or model of the two other levels.</summary>
        [MenuItem("OpenZ/L16 Jungle/1 Imports and environment materials")]
        public static void L16ImportsAndMaterials()
        {
            SetTexture($"{Tex}/L16_ground_paint.png", 8192, 16, TextureWrapMode.Clamp, false);
            SetTexture($"{Tex}/L16_cliff_rock.png", 2048, 8, TextureWrapMode.Repeat, false);
            AssetDatabase.SaveAssets();
            // The remap writes a material asset for every name the model carries: a foreign name would overwrite a tuned material
            // of the other levels. Anything but the three level 16 names stops here, nothing is remapped.
            string model = $"{Env}/L16_environment.fbx";
            var modelImp = AssetImporter.GetAtPath(model) as ModelImporter;
            if (modelImp == null) { Debug.LogError("[OpenZ] L16: missing " + model); return; }
            var own = new HashSet<string> { "M_L16_Ground", "M_Cliff_JungleRock", "M_Water_Swamp" };
            var foreign = AssetDatabase.LoadAllAssetsAtPath(model).OfType<Material>().Select(m => m.name)
                .Concat(modelImp.GetExternalObjectMap().Keys.Where(k => k.type == typeof(Material)).Select(k => k.name))
                .Distinct().Where(nm => !own.Contains(nm) && nm != "No Name").ToList();   // "No Name" = the importer's name for a mesh without a material (the walk collider)
            if (foreign.Count > 0)
            {
                Debug.LogError("[OpenZ] L16: the model carries other materials (" + string.Join(", ", foreign) + "): nothing remapped. Its materials must be M_L16_Ground, M_Cliff_JungleRock, M_Water_Swamp.");
                return;
            }
            RemapEnvironmentOf("L16_environment");
        }
        [MenuItem("OpenZ/L16 Jungle/2 Build L16 Jungle Scene")] public static void BuildL16() => BuildLevel("L16");
        /// <summary>The level being built: Spawn reads it for the choices that differ per level.</summary>
        static string SpawnLv = "L02";

        [MenuItem("OpenZ/5 Build L02 Desert Scene")] public static void BuildL02() => BuildLevel("L02");
        [MenuItem("OpenZ/5 Build L17 Car Park Scene")] public static void BuildL17() => BuildLevel("L17");

        public static string BuildLevel(string lv)
        {
            bool city = lv == "L17", jungle = lv == "L16";
            SpawnLv = lv;
            var log = new System.Text.StringBuilder();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // environment: Blender FBX arrives rotated 180 deg around Y -> undo it and verify the tile markers
            var envAsset = AssetDatabase.LoadAssetAtPath<GameObject>($"{Env}/{lv}_environment.fbx");
            var env = (GameObject)PrefabUtility.InstantiatePrefab(envAsset);
            env.name = "Environment";
            // pick the root yaw that puts the tile markers where the Zod grid says (0 with bakeAxisConversion, 180 was needed before)
            float bestYaw = 0; float bestErr = float.MaxValue;
            foreach (float yawTry in new[] { 0f, 180f, 90f, 270f })
            {
                env.transform.rotation = Quaternion.Euler(0, yawTry, 0); float err = 0;
                foreach (Transform t in env.GetComponentsInChildren<Transform>())
                {
                    if (!t.name.StartsWith("MRK_tile_")) continue;
                    var pp = t.name.Split('_'); int ax = int.Parse(pp[2]), ay = int.Parse(pp[3].Split('.')[0]);
                    err += (t.position - new Vector3(ax * 2 + 1, 0, -(ay * 2 + 1))).magnitude;
                }
                if (err < bestErr) { bestErr = err; bestYaw = yawTry; }
            }
            env.transform.rotation = Quaternion.Euler(0, bestYaw, 0);
            log.AppendLine($"environment root yaw {bestYaw}");
            bool markersOk = true;
            foreach (Transform t in env.GetComponentsInChildren<Transform>())
            {
                if (!t.name.StartsWith("MRK_tile_")) continue;
                var parts = t.name.Split('_'); int tx = int.Parse(parts[2]), ty = int.Parse(parts[3].Split('.')[0]);
                var expect = new Vector3(tx * 2 + 1, 0, -(ty * 2 + 1));
                bool ok = (t.position - expect).magnitude < 0.01f; markersOk &= ok;
                log.AppendLine($"{t.name}: {t.position:F2} expected {expect:F2} {(ok ? "OK" : "WRONG")}");
            }
            foreach (Transform t in env.transform)
            {
                if (t.name.StartsWith("COL_"))
                {
                    var mr = t.GetComponent<MeshRenderer>(); if (mr) mr.enabled = false;   // never render the collision copy (z-fighting)
                    var mc = t.gameObject.GetComponent<MeshCollider>(); if (!mc) mc = t.gameObject.AddComponent<MeshCollider>();   // no ?? on Unity objects
                    mc.sharedMesh = t.GetComponent<MeshFilter>().sharedMesh;
                }
                if (t.name.StartsWith("WAT_")) foreach (var r in t.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
                // the M12 dune backdrop loses its node rotation in the FBX and stands up as a 790 m wall: not used in Unity
                if (t.name.StartsWith("DEC_Distant")) t.gameObject.SetActive(false);
            }
            // skirt: sea (city) or sand (desert) far beyond the terrain edge so no void shows through border gaps (critic #2)
            var skirt = GameObject.CreatePrimitive(PrimitiveType.Plane); skirt.name = "SKIRT";
            UnityEngine.Object.DestroyImmediate(skirt.GetComponent<Collider>());
            skirt.transform.position = new Vector3(0, city ? -1.7f : jungle ? -7f : -1.2f, 0);   // below the water surface / harbour + river beds inside the map
            skirt.transform.localScale = new Vector3(240, 1, 240);                 // (was swallowed by the comment above: SKIRT stayed 10 x 10 m)
            skirt.GetComponent<Renderer>().sharedMaterial = city ? AssetDatabase.LoadAssetAtPath<Material>($"{MatEnv}/M_Water_Harbour.mat")
                                                          : jungle ? SaveMat(MatEnv, "M_Skirt_Jungle", TeamMat(new Color(0.16f, 0.22f, 0.10f), null, false, 0, 0.05f))
                                                                 : SaveMat(MatEnv, "M_Skirt_Sand", TeamMat(new Color(0.84f, 0.64f, 0.36f), null, false, 0, 0.05f));
            skirt.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            // sun, sky, fog
            var sunGo = new GameObject("Sun"); var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional; sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.82f; sun.color = new Color(1f, 0.93f, 0.8f); sun.intensity = 2.2f;
            sunGo.AddComponent<UniversalAdditionalLightData>();
            RenderSettings.sun = sun;
            RenderSettings.skybox = SkyMat();                                  // gradient sky (critic #2: flat grey through the L17 notch)
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear; RenderSettings.fogStartDistance = 220; RenderSettings.fogEndDistance = 760;
            var dn = new GameObject("DayNight").AddComponent<DayNight>(); dn.sun = sun; dn.lampRoot = env.transform; dn.timeOfDay = 0.08f;
            dn.warmth = city ? 0.35f : jungle ? 0.7f : 1f;   // the city reads cool grey, the desert warm (critic: city looked sandy)
            var atmo = new GameObject("Atmosphere").AddComponent<Atmosphere>();
            atmo.preset = city || jungle ? Atmosphere.Preset.City : Atmosphere.Preset.Desert; atmo.dayNight = dn; atmo.lampRoot = env.transform; atmo.level = lv;
            atmo.groundMaterial = AssetDatabase.LoadAssetAtPath<Material>($"{MatEnv}/M_{lv}_Ground.mat");
            var vol = new GameObject("PostFX").AddComponent<Volume>(); vol.isGlobal = true;
            vol.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>($"{Root}/Settings/OpenZ_PostFX.asset");
            // camera over the player's (RED) fort
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>(); cam.fieldOfView = 32; cam.nearClipPlane = 0.5f; cam.farClipPlane = 1500; cam.allowHDR = true;
            cam.clearFlags = CameraClearFlags.Skybox;
            var cd = camGo.AddComponent<UniversalAdditionalCameraData>(); cd.renderPostProcessing = true; cd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.AddComponent<AudioListener>();
            var rts = camGo.AddComponent<RtsCamera>();
            var map = JsonUtility.FromJson<ZMapJson>(File.ReadAllText($"{Root}/Resources/OpenZ/Maps/{lv}_grid.json"));
            rts.mapSizeMeters = new Vector2(map.width * 2, map.height * 2);
            var redFort = map.objects.FirstOrDefault(o => o.owner == "red" && o.kind.StartsWith("building:fort"));
            if (redFort != null) rts.focus = new Vector3(redFort.x * 2 + 10, 0, -(redFort.y * 2 + 16));
            rts.height = 70f;   // parity P3: start over the red base at Zod's 800x600 framing (43.6 of 43.75 tiles across); RtsCamera keeps the frame inside the map edge
            // Zod simulation + Z controls (plan/04: sim in pixel space, views bound by name, player RED)
            var zgo = new GameObject("ZGame");
            var zgame = zgo.AddComponent<ZGame>(); zgame.level = lv; zgame.player = Team.Red;
            zgame.unitPrefabs = new[] { "robot", "tough", "jeep", "light", "medium", "heavy", "apc", "missile_launcher", "crane", "gatling", "gun", "howitzer", "missile_cannon", "grenades",
                                     "bridge_desert_intact", "bridge_desert_destroyed", "bridge_city_intact", "bridge_city_destroyed",
                                     "bridge_desert_damaged", "bridge_city_damaged", "bridge_desert_chunks", "bridge_city_chunks",
                                     "bridge_jungle_intact", "bridge_jungle_damaged", "bridge_jungle_destroyed", "bridge_jungle_chunks",
                                     "bs01_grunt", "bs01_psycho", "bs01_tough", "bs01_sniper", "bs01_pyro", "bs01_laser", "bs01_electrobot" }
                                .Select(Pf).Where(g => g).ToArray();
            var zin = zgo.AddComponent<PlayerInput>(); zin.game = zgame; zin.cam = rts;
            var zov = zgo.AddComponent<SelectionOverlay>(); zov.input = zin; zov.overlayMaterial = OverlayMat();
            // Zod objects from the map, owner colours (player RED, CPU BLUE, neutral GREY)
            Physics.SyncTransforms();
            var zroot = new GameObject("ZodObjects").transform;
            Forts = map.objects.Where(f => f.kind.StartsWith("building:fort")).ToList();
            var counts = new Dictionary<string, int>();
            var rng = new System.Random(lv.GetHashCode());
            foreach (var o in map.objects)
            {
                var placed = Spawn(o, city, zroot, rng);
                if (placed > 0) { string k = o.kind.Split(':')[0]; counts[k] = counts.TryGetValue(k, out var c) ? c + placed : placed; }
            }
            log.AppendLine("spawned " + string.Join(", ", counts.Select(kv => kv.Key + " " + kv.Value)));
            log.AppendLine(TowerGunSeat.SeatAll(zroot));                                            // tower_gun_fix: tower guns seated as in Play + edit-mode plinths
            log.AppendLine(DeckUnderlays(zroot));
            log.AppendLine(AntiClip(env.transform, zroot));
            log.AppendLine("markers " + (markersOk ? "OK" : "WRONG"));
            Directory.CreateDirectory($"{Root}/Scenes");
            string sp = $"{Root}/Scenes/{lv}_{(city ? "CarPark" : jungle ? "Jungle" : "Desert")}.unity";
            EditorSceneManager.SaveScene(scene, sp);
            var list = EditorBuildSettings.scenes.ToList();
            if (!list.Any(s => s.path == sp)) { list.Add(new EditorBuildSettingsScene(sp, true)); EditorBuildSettings.scenes = list.ToArray(); }
            Debug.Log(log.ToString());
            return log.ToString();
        }

        static Material SkyMat()
        {
            string p = $"{Root}/Materials/Env/M_SkyGradient.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m) return m;
            m = new Material(Shader.Find("OpenZ/SkyGradient"));
            AssetDatabase.CreateAsset(m, p);
            return m;
        }

        static Material OverlayMat()
        {
            string p = $"{Root}/Materials/UI/M_Overlay.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m) return m;
            Directory.CreateDirectory($"{Root}/Materials/UI");
            m = new Material(Shader.Find("OpenZ/Overlay"));
            AssetDatabase.CreateAsset(m, p);
            return m;
        }

        static GameObject Pf(string key) => AssetDatabase.LoadAssetAtPath<GameObject>($"{Pref}/Z_{key}.prefab");

        static int Spawn(ZObj o, bool city, Transform parent, System.Random rng)
        {
            string[] k = o.kind.Split(':'); string cat = k[0], type = k[1];
            Team team = TeamColors.Parse(o.owner);
            int w = 1, h = 1; string key = null; float yaw = 0; Vector3 scale = Vector3.one;
            float teamYaw = team == Team.Red ? 0 : 180;          // units look toward the enemy (blue fort is north)
            switch (o.kind)
            {
                case "building:fort_front": key = "fort"; w = 10; h = 12; yaw = 180; break;     // gate opens south
                case "building:fort_back": key = "fort"; w = 10; h = 11; yaw = 0; break;        // gate opens north
                case "building:robot_factory": key = "robot_factory"; w = 4; h = 5; yaw = 180; break;
                case "building:vehicle_factory": key = "vehicle_factory"; w = 4; h = 5; yaw = 180; break;
                case "building:repair": key = "repair"; w = 5; h = 4; yaw = 180; break;
                case "building:radar": key = "radar"; w = 4; h = 3; yaw = 180; break;
                case "building:bridge_vert":
                case "building:bridge_horz":
                {
                    bool vert = type == "bridge_vert"; int len = 5 + (o.extra_links < 100 ? o.extra_links : 0);
                    w = vert ? 4 : len; h = vert ? len : 4;
                    bool plank = SpawnLv == "L16";
                    key = $"bridge_{(city ? "city" : plank ? "jungle" : "desert")}_{(o.health_percent <= 0 ? "destroyed" : "intact")}";
                    float mw = city || plank ? 8f : 7.8f, ml = city ? 12f : plank ? 22f : 20.6f;
                    yaw = vert ? 0 : 90; scale = new Vector3((vert ? w : h) * 2 / mw, 1, (vert ? h : w) * 2 / ml);
                    team = Team.Neutral; break;
                }
            }
            if (key == null)
            {
                if (cat == "cannon") { key = type == "gun" ? "gun" : type; w = h = 2; yaw = teamYaw; }
                else if (cat == "vehicle") { key = type; w = h = 2; yaw = team == Team.Neutral ? rng.Next(2) * 180 : teamYaw; }   // parked along the bays
                else if (cat == "robot") { key = SpawnLv == "L16" && Pf("bs01_" + type) != null ? "bs01_" + type : type == "tough" ? "tough" : "robot"; }
                else if (type == "flag") key = "flag";
                else if (type == "grenades") key = "grenades";
                else if (type == "hut") key = "hut";
                // rocks sit square-on in their tile (+-8 deg jitter): a freely rotated 2.2 m rock overlaps its neighbours in Zod's rock rows
                else if (type == "rock") { key = city ? "city_block" : "rock_desert_" + rng.Next(4); yaw = rng.Next(4) * 90; }
                else if (type.StartsWith("map_object"))
                {
                    int idx = int.Parse(new string(type.Where(char.IsDigit).ToArray()));
                    key = city ? (idx == 11 ? "barrel" : idx == 12 ? "crate" : idx == 13 ? "stop_sign" : "city_block")
                               : (idx >= 9 ? "rock_desert_2" : "cactus_" + (idx % 3));
                    yaw = rng.Next(360);
                }
            }
            var prefab = key != null ? Pf(key) : null;
            if (!prefab) { if (key != null) Debug.LogWarning("no prefab " + key); return 0; }
            // Zod anchor = top-left tile; rocks block only their bottom tile (1x3 sprite)
            int ty = type == "rock" ? o.y + 2 : o.y;
            var center = new Vector3(o.x * 2 + w, 0, -(ty * 2 + h));
            bool onFort = cat == "cannon" && IsOnFort(o);
            int n = cat == "robot" ? (GroupSize.TryGetValue(type, out var g) ? g : 3) : 1;
            bool isBridge = o.kind.StartsWith("building:bridge");
            bool smallItem = cat == "map_item";                                    // rocks, huts, crates, decorations: must fit their tile
            for (int i = 0; i < n; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                go.name = $"{type}_{o.x}_{o.y}" + (n > 1 ? $"_{i}" : "");
                Vector3 off = n > 1 ? new Vector3(Mathf.Cos(i * Mathf.PI * 2 / n + 0.4f), 0, Mathf.Sin(i * Mathf.PI * 2 / n + 0.4f)) * 0.8f : Vector3.zero;
                go.transform.SetPositionAndRotation(center + off, Quaternion.Euler(0, yaw, 0));
                go.transform.localScale = scale;
                if (smallItem && type != "flag")
                {
                    var b0 = WorldBounds(go);
                    if (type == "rock")
                    {
                        // Zod rocks fill their tile: stretch both axes to 1.95 m (world-aligned, yaw is a multiple of 90) and stand taller
                        var ls = go.transform.localScale; bool swap = Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)) > 0.5f;
                        float target = 1.75f * (0.9f + 0.1f * (float)rng.NextDouble());   // gaps between neighbours (critic #4)
                        float kx = target / b0.size.x, kz = target / b0.size.z;
                        float sx = swap ? kz : kx, sz = swap ? kx : kz;
                        go.transform.localScale = new Vector3(ls.x * sx, ls.y * Mathf.Max(kx, kz) * (1.0f + 0.2f * (float)rng.NextDouble()), ls.z * sz);
                    }
                    else
                    {
                        float fit = (type == "hut" ? 1.95f : 1.9f) / Mathf.Max(b0.size.x, b0.size.z);
                        if (fit < 1f) go.transform.localScale = scale * fit;
                    }
                }
                Vector3 p = go.transform.position;
                if (isBridge) p.y = city ? 0.05f : 0.06f;                           // deck just above the quay / abutment ground (never co-planar)
                else if (onFort)
                {   // on its tower top, scaled to the dome cap and seated into it (critic #13: plates overhung the domes)
                    go.transform.localScale = scale * 0.72f;
                    p.y = FortRoofY(go, parent, o) - 0.35f; p.x = go.transform.position.x; p.z = go.transform.position.z;
                }
                else
                {
                    // sit on the LOWEST ground under the footprint: nothing floats; small items sink 3 cm
                    var (lo, hi) = FootprintGround(go);
                    if (smallItem && hi - lo > 0.6f) { go.transform.localScale *= 0.8f; (lo, hi) = FootprintGround(go); }
                    p.y = type == "rock" ? lo + (hi - lo) * 0.35f - 0.03f : cat == "robot" ? lo - 0.08f : lo - (smallItem ? 0.03f : 0.01f);   // robots: feet on the ground (critic r7)
                }
                go.transform.position = p;
                var tp = go.GetComponent<TeamPaint>(); if (tp) tp.Team = team;
            }
            return n;
        }

        /// <summary>
        /// Zod's 4 fort turret spots (local tiles (1,0) (7,0) (1,3) (7,3)) -> the 4 tower tops of the imported fort, one each:
        /// column 1 = west, 7 = east; row 0 = north towers, row 3 = south towers (visual placement; gameplay keeps the Zod spot).
        /// Tower tops are found by raycasting the fort mesh (temporary colliders), per quadrant, away from the central turret.
        /// </summary>
        static float FortRoofY(GameObject cannon, Transform parent, ZObj o)
        {
            var c = cannon.transform.position;
            Transform fort = parent.Cast<Transform>().Where(t => t.name.StartsWith("fort_")).OrderBy(t => (t.position - c).sqrMagnitude).FirstOrDefault();
            var fz = Forts.FirstOrDefault(f => (o.x - f.x == 1 || o.x - f.x == 7) && (o.y - f.y == 0 || o.y - f.y == 3));
            if (!fort || fz == null) return 7.4f;
            bool east = o.x - fz.x == 7, north = o.y - fz.y == 0;
            var tmp = new List<MeshCollider>();
            foreach (var mf in fort.GetComponentsInChildren<MeshFilter>()) { var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; tmp.Add(mc); }
            Physics.SyncTransforms();
            var fb = WorldBounds(fort.gameObject); var ctr = fb.center;
            var pts = new List<Vector3>();
            for (float x = fb.min.x; x <= fb.max.x; x += 0.5f)
                for (float z = fb.min.z; z <= fb.max.z; z += 0.5f)
                {
                    if ((x > ctr.x) != east || (z > ctr.z) != north) continue;                 // this quadrant only
                    if (Mathf.Abs(x - ctr.x) < 4.5f || Mathf.Abs(z - ctr.z) < 4.5f) continue;   // corners only: not the central turret, its barrel or the flag
                    foreach (var hit in Physics.RaycastAll(new Vector3(x, 300, z), Vector3.down, 600))
                        if (tmp.Contains(hit.collider as MeshCollider)) { pts.Add(hit.point); break; }
                }
            float y = 7.4f;
            if (pts.Count > 0)
            {
                float top = pts.Max(q => q.y);
                var cap = pts.Where(q => q.y > top - 0.4f).ToList();
                var cen = new Vector3(cap.Average(q => q.x), top, cap.Average(q => q.z));
                cannon.transform.position = new Vector3(cen.x, c.y, cen.z); y = top;
                Vector3 outward = cen - ctr; outward.y = 0;
                if (outward.sqrMagnitude > 0.01f) cannon.transform.rotation = Quaternion.LookRotation(outward.normalized);
            }
            foreach (var mc in tmp) UnityEngine.Object.DestroyImmediate(mc);
            Physics.SyncTransforms();
            return y;
        }

        static (float lo, float hi) BaseGround(Vector3 c, float r)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var o in new[] { Vector3.zero, new Vector3(r, 0, 0), new Vector3(-r, 0, 0), new Vector3(0, 0, r), new Vector3(0, 0, -r) })
                if (Physics.Raycast(new Vector3(c.x, 300, c.z) + o, Vector3.down, out var hit, 600)) { lo = Mathf.Min(lo, hit.point.y); hi = Mathf.Max(hi, hit.point.y); }
            return lo == float.MaxValue ? (0, 0) : (lo, hi);
        }

        /// <summary>Lowest and highest ground (COL_Terrain) under the object's footprint (3x3 samples inside its bounds).</summary>
        static (float lo, float hi) FootprintGround(GameObject go)
        {
            var b = WorldBounds(go); float lo = float.MaxValue, hi = float.MinValue;
            for (int ix = -1; ix <= 1; ix++)
                for (int iz = -1; iz <= 1; iz++)
                {
                    var q = new Vector3(b.center.x + ix * b.extents.x * 0.85f, 300, b.center.z + iz * b.extents.z * 0.85f);
                    if (Physics.Raycast(q, Vector3.down, out var hit, 600)) { lo = Mathf.Min(lo, hit.point.y); hi = Mathf.Max(hi, hit.point.y); }
                }
            return lo == float.MaxValue ? (0, 0) : (lo, hi);
        }

        /// <summary>
        /// No clipping (the project rules): Blender decoration that intersects a gameplay object is removed, and rock/rock and
        /// object/object overlaps are counted for the report.
        /// </summary>
        static string AntiClip(Transform env, Transform zroot)
        {
            Bounds Shrink(Bounds b, float k) { b.extents *= k; return b; }
            string[] smallDec = { "Cactus", "lamp", "Barrel", "Supply_crate", "Solar", "water_tower", "bollard", "stop_sign" };
            // 1) small decorations: never on a steep slope (cut into the cliff), always seated on the lowest ground
            int decSlope = 0, decSeated = 0, decMoved = 0;
            foreach (Transform d in env)
            {
                if (!d.name.StartsWith("DEC_") || !d.gameObject.activeSelf) continue;
                if (!smallDec.Any(k => d.name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                // test the BASE only (post / pole / trunk): arms and lamp heads may overhang slopes, quay walls or water
                string nl = d.name.ToLowerInvariant();
                float r = nl.Contains("bollard") || nl.Contains("lamp") || nl.Contains("stop_sign") ? 0.1f : nl.Contains("cactus") ? 0.2f : 0.3f;
                var (lo, hi) = BaseGround(d.position, r);
                if (hi - lo > 0.3f)
                {
                    // move down-slope to the nearest flat spot within 2.5 m, else remove (never cut into the cliff)
                    Vector3? best = null; float bestLo = 0;
                    for (float rad = 0.5f; rad <= 2.5f && best == null; rad += 0.5f)
                        for (int a = 0; a < 16; a++)
                        {
                            var q = d.position + new Vector3(Mathf.Cos(a * Mathf.PI / 8), 0, Mathf.Sin(a * Mathf.PI / 8)) * rad;
                            var (l2, h2) = BaseGround(q, r);
                            if (h2 - l2 <= 0.3f && l2 <= lo + 0.3f) { best = q; bestLo = l2; break; }
                        }
                    if (best == null) { d.gameObject.SetActive(false); decSlope++; continue; }
                    d.position = new Vector3(best.Value.x, d.position.y, best.Value.z); lo = bestLo; decMoved++;
                }
                var db = WorldBounds(d.gameObject); float dy = (lo - 0.02f) - db.min.y;
                if (Mathf.Abs(dy) > 0.01f) { d.position += Vector3.up * dy; decSeated++; }
            }
            // 1b) cacti always upright (critic #8), boulders rock-coloured with a natural tilt (critic #10), lamps face away from cliffs (#14)
            var terCols = new List<MeshCollider>();
            foreach (var mf in env.GetComponentsInChildren<MeshFilter>())
                if (mf.name.StartsWith("TER_")) { var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; terCols.Add(mc); }
            Physics.SyncTransforms();
            var rngd = new System.Random(7); int decCliff = 0, lampMoved = 0, lampRemoved = 0, lampTurned = 0;
            foreach (Transform d in env)
            {
                if (!d.name.StartsWith("DEC_") || !d.gameObject.activeSelf) continue;
                string dn = d.name.ToLowerInvariant();
                if (dn.Contains("cactus"))
                {
                    foreach (var mr in d.GetComponentsInChildren<Renderer>())
                        mr.transform.rotation = Quaternion.Euler(0, mr.transform.eulerAngles.y, 0);
                    // whole footprint (arms too) must stand free of the cliff: terrain above the lower 20% anywhere -> removed
                    var cb = WorldBounds(d.gameObject); float limit = cb.min.y + 0.2f * cb.size.y;
                    bool inRock = false;
                    for (int gx = 0; gx <= 2 && !inRock; gx++)
                        for (int gz = 0; gz <= 2 && !inRock; gz++)
                        {
                            var q = new Vector3(Mathf.Lerp(cb.min.x, cb.max.x, gx / 2f), 300, Mathf.Lerp(cb.min.z, cb.max.z, gz / 2f));
                            if (Physics.Raycast(q, Vector3.down, out var ch, 600) && ch.point.y > limit) inRock = true;
                        }
                    if (inRock) { d.gameObject.SetActive(false); decCliff++; }
                }
                if (dn.Contains("sea_worn_boulder"))
                {
                    // grey boulders on the brown city cliffs read as foreign objects (critic #10): same rock colour as the cliff
                    foreach (var br in d.GetComponentsInChildren<Renderer>())
                        br.sharedMaterials = br.sharedMaterials.Select(m =>
                        {
                            if (!m || m.name.EndsWith("_CliffBrown")) return m;
                            var bm = AssetDatabase.LoadAssetAtPath<Material>($"{MatEnv}/{m.name}_CliffBrown.mat");
                            if (!bm) { bm = new Material(m); bm.SetColor("_BaseColor", new Color(1.0f, 0.74f, 0.55f)); AssetDatabase.CreateAsset(bm, $"{MatEnv}/{m.name}_CliffBrown.mat"); }
                            return bm;
                        }).ToArray();
                    d.rotation = Quaternion.Euler((float)rngd.NextDouble() * 24 - 12, (float)rngd.NextDouble() * 360, (float)rngd.NextDouble() * 24 - 12);
                    d.localScale = Vector3.Scale(d.localScale, new Vector3(0.9f + 0.4f * (float)rngd.NextDouble(), 0.7f + 0.4f * (float)rngd.NextDouble(), 0.9f + 0.4f * (float)rngd.NextDouble()));
                }
                if (dn.Contains("lamp") && !dn.Contains("glow"))
                {
                    // overhangs the heightfield collider, so the head box is tested against the real TER_ meshes: 8 headings,
                    // downhill first; all blocked -> step 0.5 m away from the cliff (max 3 m), else remove
                    System.Func<bool> headHits = () =>
                    {
                        Physics.SyncTransforms();
                        var lb = WorldBounds(d.gameObject);
                        var c = new Vector3(lb.center.x, lb.max.y - 0.6f, lb.center.z);
                        var ext = new Vector3(lb.extents.x * 0.9f, 0.5f, lb.extents.z * 0.9f);
                        return Physics.OverlapBox(c, ext).Any(col => terCols.Contains(col as MeshCollider));
                    };
                    bool ok = false;
                    for (int step = 0; step <= 6 && !ok; step++)
                    {
                        if (step > 0)
                        {   // walk down-slope, re-seat on the ground
                            var pp = d.position; float hx = 0, hz = 0;
                            if (Physics.Raycast(pp + new Vector3(1, 300, 0), Vector3.down, out var a1, 600) && Physics.Raycast(pp + new Vector3(-1, 300, 0), Vector3.down, out var a2, 600)) hx = a1.point.y - a2.point.y;
                            if (Physics.Raycast(pp + new Vector3(0, 300, 1), Vector3.down, out var b1, 600) && Physics.Raycast(pp + new Vector3(0, 300, -1), Vector3.down, out var b2, 600)) hz = b1.point.y - b2.point.y;
                            var away = new Vector3(-hx, 0, -hz); if (away.sqrMagnitude < 1e-4f) break;
                            d.position = pp + away.normalized * 0.5f;
                            var (glo, _) = BaseGround(d.position, 0.1f); Physics.SyncTransforms(); var db0 = WorldBounds(d.gameObject); d.position += Vector3.up * ((glo - 0.02f) - db0.min.y);
                            lampMoved++;
                        }
                        // the pivot is at the bounds centre: head = highest child mesh, pole = mirror of the head through the centre;
                        // swing the whole lamp around its pole base (world up, quaternion: the root has a 90 deg X)
                        Physics.SyncTransforms();
                        var q0 = d.rotation; var p0 = d.position; var lb0 = WorldBounds(d.gameObject);
                        var head = d.GetComponentsInChildren<Renderer>().OrderByDescending(rr => rr.bounds.center.y).First().bounds.center;
                        var armv = head - lb0.center; armv.y = 0;
                        var pole = lb0.center - armv; pole.y = 0;
                        System.Action<float> swing = ang =>
                        {
                            var qq = Quaternion.AngleAxis(ang, Vector3.up);
                            d.rotation = qq * q0; var off = qq * (p0 - new Vector3(pole.x, p0.y, pole.z));
                            d.position = new Vector3(pole.x, p0.y, pole.z) + off;
                        };
                        var order = new List<(float g, float yaw)>();
                        for (int k = 0; k < 8; k++)
                        {
                            var dir = Quaternion.AngleAxis(45 * k, Vector3.up) * (armv.sqrMagnitude > 1e-4f ? armv.normalized : Vector3.forward);
                            var tip = pole + dir * 3f;
                            order.Add((Physics.Raycast(new Vector3(tip.x, 300, tip.z), Vector3.down, out var th, 600) ? th.point.y : 0f, 45 * k));
                        }
                        foreach (var o2 in order.OrderBy(q => q.g))
                        {
                            swing(o2.yaw);
                            if (!headHits()) { ok = true; if (o2.yaw != 0) lampTurned++; break; }
                        }
                        if (!ok) swing(0);
                    }
                    if (!ok) { d.gameObject.SetActive(false); lampRemoved++; }
                }
            }
            foreach (var mc in terCols) UnityEngine.Object.DestroyImmediate(mc);
            Physics.SyncTransforms();
            // 2) decorations intersecting gameplay objects are removed (gameplay wins)
            var objs = zroot.Cast<Transform>().Select(t => (t, b: Shrink(WorldBounds(t.gameObject), 0.9f))).ToList();
            int decRemoved = 0;
            foreach (Transform d in env)
            {
                if (!d.name.StartsWith("DEC_") || !d.gameObject.activeSelf) continue;
                var db = Shrink(WorldBounds(d.gameObject), 0.9f);
                // bridges: strict (critic #1: rock clusters on the bank tiles jammed into the rails and abutments)
                bool nearBridge = zroot.Cast<Transform>().Where(t => t.name.StartsWith("bridge_"))
                                       .Any(t => { var bb = WorldBounds(t.gameObject); bb.Expand(5.0f); return bb.Intersects(db); });   // Expand adds to the SIZE: 2.5 m per side (abutment cells)
                if (nearBridge || objs.Any(o => o.b.Intersects(db))) { d.gameObject.SetActive(false); decRemoved++; }
            }
            // 3) small Zod items (rocks, huts, crates, decorations, flags) and cannons vs big objects: nudge + shrink
            bool IsItem(Transform t) => t.name.StartsWith("rock_") || t.name.StartsWith("map_object") || t.name.StartsWith("hut_") || t.name.StartsWith("grenades_") || t.name.StartsWith("flag_");
            bool IsBig(Transform t) => t.name.StartsWith("bridge_") || t.name.StartsWith("fort_") || t.name.Contains("factory") || t.name.StartsWith("radar_") || t.name.StartsWith("repair_");
            int nudged = 0, stillOverlap = 0;
            foreach (var it in zroot.Cast<Transform>().Where(t => IsItem(t) || t.name.StartsWith("gatling_") || t.name.StartsWith("gun_") || t.name.StartsWith("howitzer_") || t.name.StartsWith("missile_cannon_")))
            {
                bool item = IsItem(it);
                if (!item && it.position.y > 3f) continue;                          // fort turret cannons sit on the fort by design
                for (int iter = 0; iter < 4; iter++)
                {
                    var ib = Shrink(WorldBounds(it.gameObject), 0.92f);
                    var other = zroot.Cast<Transform>().FirstOrDefault(o => o != it && (IsBig(o) || (!item && IsItem(o) == false && o.name.StartsWith("bridge_"))) && Shrink(WorldBounds(o.gameObject), 0.95f).Intersects(ib));
                    if (!other) break;
                    Vector3 away = it.position - WorldBounds(other.gameObject).center; away.y = 0;
                    if (away.sqrMagnitude < 1e-4f) away = Vector3.right;
                    it.position += away.normalized * (item ? 0.35f : 0.6f);
                    if (item && it.localScale.x > 0.75f * it.localScale.y / 1.15f) it.localScale = new Vector3(it.localScale.x * 0.93f, it.localScale.y, it.localScale.z * 0.93f);
                    nudged++;
                    if (iter == 3) stillOverlap++;
                }
            }
            // 4) report item/item overlaps
            int rockOverlaps = 0, itemOverlaps = 0;
            var items = zroot.Cast<Transform>().Where(IsItem).Select(t => (t, b: Shrink(WorldBounds(t.gameObject), 0.9f))).ToList();
            for (int i = 0; i < items.Count; i++)
                for (int j = i + 1; j < items.Count; j++)
                {
                    if (!items[i].b.Intersects(items[j].b)) continue;
                    if (items[i].t.name.StartsWith("rock_") && items[j].t.name.StartsWith("rock_")) rockOverlaps++; else itemOverlaps++;
                }
            return $"anti-clip: {decMoved} small decorations moved off slopes, {decSlope} removed (no flat spot), {decCliff} cacti touching cliffs removed, lamps: {lampTurned} turned away from rock, {lampMoved} steps away from cliffs, {lampRemoved} removed, {decSeated} seated, {decRemoved} intersecting gameplay objects removed, " +
                   $"{nudged} item/cannon nudges ({stillOverlap} still touching), rock/rock overlaps {rockOverlaps}, other item overlaps {itemOverlaps}";
        }

        static List<ZObj> Forts = new List<ZObj>();
        // Zod fort turret spots (2x2 cannon top-left, local tiles): (1,0) (1,3) (7,0) (7,3)  (bfort.cpp:534-548)
        static bool IsOnFort(ZObj o) => Forts.Any(f =>
            (o.x - f.x == 1 || o.x - f.x == 7) && (o.y - f.y == 0 || o.y - f.y == 3));

        static float GroundY(Vector3 p, bool onFort)
        {
            if (onFort) return 7.4f;                                           // fort turret spots are on the roof
            if (Physics.Raycast(new Vector3(p.x, 200, p.z), Vector3.down, out var hit, 400)) return hit.point.y;
            return 0;
        }

        // ------------------------------------------------------------------------------------------------ editor
        [MenuItem("OpenZ/Test Mode (30 fps, no atmosphere)")]
        public static void ToggleTestMode()
        {
            bool on = PlayerPrefs.GetInt("oz_testmode", 0) != 1;
            PlayerPrefs.SetInt("oz_testmode", on ? 1 : 0); PlayerPrefs.Save();
            Debug.Log("OpenZ test mode " + (on ? "ON: 30 fps, atmosphere off" : "OFF: full look, 60 fps"));
        }
        [MenuItem("OpenZ/Test Mode (30 fps, no atmosphere)", true)]
        static bool ToggleTestModeCheck() { Menu.SetChecked("OpenZ/Test Mode (30 fps, no atmosphere)", PlayerPrefs.GetInt("oz_testmode", 0) == 1); return true; }

        [MenuItem("OpenZ/Maximize On Play")]
        public static string MaximizeOnPlay()
        {
            var gvType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
            var views = Resources.FindObjectsOfTypeAll(gvType);
            if (views.Length == 0) { EditorWindow.GetWindow(gvType); views = Resources.FindObjectsOfTypeAll(gvType); }
            int set = 0;
            foreach (var v in views)
            {
                var p = gvType.GetProperty("enterPlayModeBehavior", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null) { p.SetValue(v, Enum.Parse(p.PropertyType, "PlayMaximized")); set++; continue; }
                var q = gvType.GetProperty("maximizeOnPlay", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (q != null) { q.SetValue(v, true); set++; }
            }
            return $"Maximize On Play set on {set} Game view(s)";
        }
    }
}
