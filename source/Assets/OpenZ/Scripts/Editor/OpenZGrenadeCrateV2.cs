using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    ///    re-export with CRATE_stacks, one crate material for the pickup, the pieces and the stacks);
    ///    grab_scoop states in RobotBS01.controller and the fitted per-type clips in the five RobotV3 override controllers.
    /// Everything is copied (the staging folders stay read-only); the old prefab is backed up under backups/unity/prefabs.
    /// </summary>
    public static class OpenZGrenadeCrateV2
    {
        const string Prefab = "Assets/OpenZ/Prefabs/Z_grenades.prefab", ModelDir = "Assets/OpenZ/Models/Props/GrenadeCrate",
            TexPath = "Assets/OpenZ/Textures/Props/crate_atlas.png", MatPath = "Assets/OpenZ/Materials/Props/M_GrenadeCrate.mat",
            SfxDir = "Assets/OpenZ/Resources/OpenZ/Audio/Sfx";
        static string Staging => Path.Combine(OpenZPaths.RepoRoot, "work", "staging", "grenade_crate_v1");
        static string ClipsStaging => Path.Combine(OpenZPaths.RepoRoot, "work", "staging", "robot_clips_v1", "export");

        static bool CopyIfChanged(string src, string dst)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            if (File.Exists(dst) && File.ReadAllBytes(dst).SequenceEqual(File.ReadAllBytes(src))) return false;
            File.Copy(src, dst, true); return true;
        }

        // ------------------------------------------------------------------------------------------ 1 crate
        [MenuItem("OpenZ/Grenade Crate/Steps (v1 earlier-project crate + grab clips)/1 Import crate + sounds + Z_grenades prefab")]
        public static string ImportCrate()
        {
            var sb = new StringBuilder("crate v1 (legacy): ");
            OpenZGrenadeCrateSwitch.SetActive("v1");
            var exp = Path.Combine(Staging, "export");
            int c = 0;
            foreach (var f in new[] { "GrenadeCrate.fbx", "GrenadeCrate_Pieces.fbx", "GrenadeCrate_Stack.fbx" })
                if (CopyIfChanged(Path.Combine(exp, f), $"{ModelDir}/{f}")) c++;
            if (CopyIfChanged(Path.Combine(exp, "crate_atlas.png"), TexPath)) c++;
            foreach (var f in new[] { "sfx_ammo_pickup.mp3", "sfx_crate_break_1.mp3", "sfx_crate_break_2.mp3" })
                if (CopyIfChanged(Path.Combine(Staging, "sfx", f), $"{SfxDir}/{f}")) c++;
            AssetDatabase.Refresh();
            sb.Append($"{c} files copied; ");
            // texture: sRGB, full 2048, mips, aniso 8, BC7 high quality
            var ti = (TextureImporter)AssetImporter.GetAtPath(TexPath);
            ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 8; ti.maxTextureSize = 2048; ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
            foreach (var f in new[] { "GrenadeCrate.fbx", "GrenadeCrate_Pieces.fbx", "GrenadeCrate_Stack.fbx" })
            {
                var mi = (ModelImporter)AssetImporter.GetAtPath($"{ModelDir}/{f}");
                mi.globalScale = 1; mi.useFileUnits = true; mi.bakeAxisConversion = false; mi.importCameras = false; mi.importLights = false;
                mi.meshCompression = ModelImporterMeshCompression.Off; mi.importNormals = ModelImporterNormals.Import; mi.addCollider = false;
                mi.isReadable = false; mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
                mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importBlendShapes = false; mi.importVisibility = false;
                mi.SaveAndReimport();
            }
            foreach (var f in new[] { "sfx_ammo_pickup.mp3", "sfx_crate_break_1.mp3", "sfx_crate_break_2.mp3" })
            {
                var ai = (AudioImporter)AssetImporter.GetAtPath($"{SfxDir}/{f}");
                var s = ai.defaultSampleSettings; s.loadType = AudioClipLoadType.DecompressOnLoad; s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = 0.9f;
                ai.defaultSampleSettings = s; ai.forceToMono = false; ai.SaveAndReimport();
            }
            var mat = CrateMaterialFor(MatPath, TexPath);
            // prefab: back up, then Model = the modelled crate, Pieces = its 9 break pieces (inactive), GrenadeCrateV2
            var bak = Path.Combine(OpenZPaths.RepoRoot, "backups", "unity", "prefabs");
            Directory.CreateDirectory(bak);
            var bakFile = Path.Combine(bak, "Z_grenades_before_crate_v2.prefab");
            if (!File.Exists(bakFile)) File.Copy(Path.GetFullPath(Prefab), bakFile);
            sb.Append(BuildPrefab(ModelDir, mat) + "; backup " + bakFile);
            Debug.Log("[OpenZ] " + sb);
            return sb.ToString();
        }

        /// <summary>Z_grenades = Model (the crate FBX in modelDir) + Pieces (its break pieces, inactive) + PickupGlow + GrenadeCrateV2, every
        /// </summary>
        public static string BuildPrefab(string modelDir, Material mat)
        {
            var root = PrefabUtility.LoadPrefabContents(Prefab);
            for (int i = root.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            var crateSrc = AssetDatabase.LoadAssetAtPath<GameObject>($"{modelDir}/GrenadeCrate.fbx");
            var piecesSrc = AssetDatabase.LoadAssetAtPath<GameObject>($"{modelDir}/GrenadeCrate_Pieces.fbx");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(crateSrc, root.transform);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Model"; model.transform.localPosition = Vector3.zero; model.transform.localScale = Vector3.one;
            var pieces = (GameObject)PrefabUtility.InstantiatePrefab(piecesSrc, root.transform);
            PrefabUtility.UnpackPrefabInstance(pieces, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            pieces.name = "Pieces"; pieces.transform.localPosition = Vector3.zero; pieces.transform.localScale = Vector3.one;
            pieces.transform.localRotation = model.transform.localRotation;
            int rends = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterial = mat; rends++;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
            }
            pieces.SetActive(false);
            if (!root.GetComponent<PickupGlow>()) root.AddComponent<PickupGlow>();
            root.GetComponent<PickupGlow>().radius = 1.25f;
            if (!root.GetComponent<GrenadeCrateV2>()) root.AddComponent<GrenadeCrateV2>();
            var b = new Bounds(); bool any = false;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            int sockets = model.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("GRENADE_"));
            int handles = model.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("HANDLE_"));
            bool lid = model.GetComponentsInChildren<Transform>(true).Any(t => t.name == "CRATE_lid");
            int nPieces = pieces.transform.childCount;
            PrefabUtility.SaveAsPrefabAsset(root, Prefab); PrefabUtility.UnloadPrefabContents(root);
            AssetDatabase.SaveAssets();
            return $"Z_grenades from {modelDir}: model bounds centre {b.center} size {b.size}, {rends} renderers, {sockets} grenade sockets, {handles} handles, {nPieces} pieces, lid {(lid ? "CRATE_lid (v2)" : "none (v1)")}, material {mat.name}";
        }

        /// <summary>
        /// v2 M_GrenadeCrate_v2 + crate_atlas_v2); OpenZBaseProps gives it to the Supply stacks' CRATE_ nodes.</summary>
        public static Material CrateMaterial()
            => OpenZGrenadeCrateSwitch.Active == "v2" ? CrateMaterialFor(OpenZGrenadeCrateSwitch.V2Mat, OpenZGrenadeCrateSwitch.V2Tex) : CrateMaterialFor(MatPath, TexPath);

        /// <summary>The BP2 paint material's look (OpenZ/TeamLit, atlas x baked vertex AO, no team paint, stylized), with the crate atlas.</summary>
        public static Material CrateMaterialFor(string matPath, string texPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(matPath));
            var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!m) { m = new Material(Shader.Find("OpenZ/TeamLit")); AssetDatabase.CreateAsset(m, matPath); }
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath)); m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureOffset("_BaseMap", Vector2.zero);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_UseVertexColor", 1); m.SetFloat("_VertexColorSRGB", 1); m.SetFloat("_TeamMode", 0); m.SetFloat("_Accent", 0);
            m.SetFloat("_Degreen", 0); m.SetFloat("_Triplanar", 0); m.SetFloat("_TopBlend", 0);
            m.SetFloat("_Stylize", 1); m.SetFloat("_Degrime", 0); m.SetFloat("_FlatShade", 0);
            m.SetFloat("_Smoothness", 0.18f); m.SetFloat("_Metallic", 0f);
            m.SetColor("_RimColor", new Color(0.10f, 0.09f, 0.08f)); m.SetFloat("_RimPower", 3.2f);
            if (m.HasProperty("_SoftFill")) m.SetFloat("_SoftFill", 0.62f);
            if (m.HasProperty("_AmbientBoost")) m.SetFloat("_AmbientBoost", 1.25f);
            if (m.HasProperty("_KeyWarm")) m.SetColor("_KeyWarm", new Color(1.06f, 1f, 0.92f));
            m.SetColor("_DarkColor", Color.black); m.SetFloat("_DarkThreshold", 0.12f);
            m.SetFloat("_SatScale", 1f); m.SetFloat("_ValScale", 1f); m.SetFloat("_HueShift", 0f);
            if (m.HasProperty("_NightClass")) m.SetFloat("_NightClass", 3f);
            if (m.HasProperty("_SeeThru")) m.SetFloat("_SeeThru", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------------------------------ 2 supply stacks
        [MenuItem("OpenZ/Grenade Crate/Steps (v1 earlier-project crate + grab clips)/2 Supply stacks (base props v2 re-export + rebuild)")]
        public static string Supply()
        {
            OpenZGrenadeCrateSwitch.SetActive("v1");
            var src = Path.Combine(Staging, "export", "bp2");
            var dst = Path.Combine(OpenZPaths.RepoRoot, "projects", "blender", "export", "base_props_v2");
            var bak = Path.Combine(OpenZPaths.RepoRoot, "backups", "base_props_v2_before_crate_v2");
            Directory.CreateDirectory(bak);
            int n = 0;
            foreach (var f in new[] { "BP2_L02_Supply.fbx", "BP2_L17_Supply.fbx", "base_props_v2_manifest.json", "base_props_v2_sets.json" })
            {
                var d = Path.Combine(dst, f);
                if (File.Exists(d) && !File.Exists(Path.Combine(bak, f))) File.Copy(d, Path.Combine(bak, f));
                if (CopyIfChanged(Path.Combine(src, f), d)) n++;
            }
            CrateMaterial();
            var r = OpenZBaseProps.ImportAndBuild();
            var s = $"supply: {n} files replaced in base_props_v2 (backup {bak}); {r}";
            Debug.Log("[OpenZ] " + s);
            return s;
        }

        // ------------------------------------------------------------------------------------------ 3 grab clips
        static readonly string[] Types = { "Psycho", "Tough", "Sniper", "Pyro", "Electrobot" };
        static readonly string[] Grabs = { "grab_stand_L", "grab_stand_R", "grab_scoop_L", "grab_scoop_R" };
        static readonly HashSet<string> Loops = new HashSet<string> { "prone_L", "prone_R", "prone_fwd", "fish_hold", "fish_reel", "sunbed_lie" };

        [MenuItem("OpenZ/Grenade Crate/Steps (v1 earlier-project crate + grab clips)/3 Import robot clips (grab) + controller states")]
        public static string ImportClips()
        {
            var sb = new StringBuilder("robot clips v1: ");
            string gDst = "Assets/OpenZ/Models/BS01/Clips_v1/BS01_RobotClips_v1.fbx";
            CopyIfChanged(Path.Combine(ClipsStaging, "BS01_RobotClips_v1.fbx"), gDst);
            foreach (var t in Types) CopyIfChanged(Path.Combine(ClipsStaging, "v3", $"BS01V3_{t}_RobotClips_v1.fbx"), $"Assets/OpenZ/Models/RobotsV3/Clips_v1/BS01V3_{t}_RobotClips_v1.fbx");
            AssetDatabase.Refresh();
            sb.Append(SetupClips(gDst, "Assets/OpenZ/Models/BS01/BS01_Robot.fbx") + "; ");
            foreach (var t in Types) sb.Append(SetupClips($"Assets/OpenZ/Models/RobotsV3/Clips_v1/BS01V3_{t}_RobotClips_v1.fbx", $"Assets/OpenZ/Models/RobotsV3/BS01V3_{t}.fbx") + "; ");
            // states in the BS-01 controller (code cross-fades into them; no transitions)
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/OpenZ/Animation/RobotBS01.controller");
            var sm = ac.layers[0].stateMachine;
            var any = sm.states.First(s => s.state.name == "grenade_throw").state;
            var gClips = AssetDatabase.LoadAllAssetsAtPath(gDst).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToDictionary(c => c.name);
            int y = 0;
            foreach (var g in Grabs)
            {
                if (!gClips.TryGetValue(g, out var clip)) { sb.Append($"MISSING clip {g}; "); continue; }
                var st = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == g) ?? sm.AddState(g, new Vector3(900, 40 + 60 * y, 0));
                st.motion = clip; st.writeDefaultValues = any.writeDefaultValues; st.speed = 1f; st.iKOnFeet = any.iKOnFeet; y++;
            }
            EditorUtility.SetDirty(ac);
            // the five V3 override controllers: their own fitted grab clips
            foreach (var t in Types)
            {
                var ov = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>($"Assets/OpenZ/Animation/RobotV3_{t}.overrideController");
                if (!ov) { sb.Append($"no override for {t}; "); continue; }
                var tClips = AssetDatabase.LoadAllAssetsAtPath($"Assets/OpenZ/Models/RobotsV3/Clips_v1/BS01V3_{t}_RobotClips_v1.fbx").OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToDictionary(c => c.name);
                var list = new List<KeyValuePair<AnimationClip, AnimationClip>>(); ov.GetOverrides(list);
                int set = 0;
                for (int i = 0; i < list.Count; i++)
                    if (list[i].Key && Grabs.Contains(list[i].Key.name) && tClips.TryGetValue(list[i].Key.name, out var tc)) { list[i] = new KeyValuePair<AnimationClip, AnimationClip>(list[i].Key, tc); set++; }
                ov.ApplyOverrides(list); EditorUtility.SetDirty(ov);
                sb.Append($"{t} {set} grabs; ");
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[OpenZ] " + sb);
            return sb.ToString();
        }

        static string SetupClips(string path, string avatarModel)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(path);
            if (!mi) return "no importer " + path;
            var avatar = AssetDatabase.LoadAllAssetsAtPath(avatarModel).OfType<Avatar>().FirstOrDefault();
            if (!avatar) return "no avatar in " + avatarModel;
            mi.animationType = ModelImporterAnimationType.Human; mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther; mi.sourceAvatar = avatar;
            mi.importAnimation = true; mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importCameras = false; mi.importLights = false;
            mi.globalScale = 1; mi.useFileUnits = true; mi.resampleCurves = true; mi.animationCompression = ModelImporterAnimationCompression.Off;
            mi.SaveAndReimport();
            var takes = mi.importedTakeInfos;
            var clips = new List<ModelImporterClipAnimation>();
            foreach (var t in takes)
            {
                string n = t.name.Contains("|") ? t.name.Substring(t.name.LastIndexOf('|') + 1) : t.name;
                float fps = t.sampleRate > 0 ? t.sampleRate : 24f;
                bool scoop = n.StartsWith("grab_scoop");
                clips.Add(new ModelImporterClipAnimation
                {
                    name = n, takeName = t.name, firstFrame = Mathf.Round(t.startTime * fps), lastFrame = Mathf.Round(t.stopTime * fps),
                    loopTime = Loops.Contains(n), lockRootRotation = true, lockRootHeightY = true, lockRootPositionXZ = !scoop,
                    keepOriginalOrientation = true, keepOriginalPositionY = true, keepOriginalPositionXZ = true, heightFromFeet = false,
                });
            }
            mi.clipAnimations = clips.ToArray();
            mi.SaveAndReimport();
            return $"{Path.GetFileName(path)}: {clips.Count} clips (avatar {avatar.name})";
        }

        [MenuItem("OpenZ/Grenade Crate/Steps (v1 earlier-project crate + grab clips)/All (1 + 2 + 3)")]
        public static string All() => ImportCrate() + " | " + Supply() + " | " + ImportClips();
    }
}
