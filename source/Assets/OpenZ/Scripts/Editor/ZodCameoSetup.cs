// OpenZ (GPL-3.0) - Commander Zod cameo: one-click import setup (menu OpenZ > Zod Cameo > Setup, or an editor script
// "return OpenZ.ZodCameoSetup.Run();"). Idempotent; everything under Assets/OpenZ/Resources/OpenZ/Zod.
//  1. Layer "ZodCameo" (slot 29 when free; ZGame's walk layer takes 30, BuildingBreaker's debris layer 31).
//  2. Textures: body albedo 4096 BC7 high quality + mipmaps + aniso 8; cigar maps 256; smoke puff with alpha.
//  3. Materials: Zod_Body / Zod_Cigar (URP Lit; the cigar with its ember emission) remapped onto the FBX, and the stage
//     templates ZodStage_Lit / _Glow / _Cutout / _Smoke so a build keeps the shader variants the runtime set uses.
//  4. Zod_Cameo.fbx: Rig Humanoid (avatar from this model, arms put in a T-pose in the description like OpenZBS01, the Jaw
//     left OUT of the humanoid map so the keyed jaw curves of laugh / cigar_puff stay as extra-bone curves), Optimize Game
//     Objects off, all 24 takes as clips named like the takes, loop flags per the model README, root motion baked into the
//     pose (original orientation / height / XZ), an AnimationEvent "ZodPuff" at 0.5 s of cigar_puff. If Unity cannot build a
//     valid human avatar it falls back to Generic by itself (logged). Run(false) = Generic on purpose.
//  5. ZodCameo.controller: one state per clip (state name = clip name, default idle, write defaults), no transitions (ZodStage
//     cross-fades by name).
//  6. ZodCameo.prefab: the FBX + Animator (controller, avatar, unscaled time, always animate, no root motion) + ZodTalkRig
//     (mouth curves), every child on the ZodCameo layer and rendering layer 7, shadows on, update when off screen.
//  7. Voice clips: Force To Mono, Normalize off, Decompress On Load, Vorbis 0.7, preload (the project's voice settings).
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    public static class ZodCameoSetup
    {
        public const string Root = "Assets/OpenZ/Resources/OpenZ/Zod";
        static string Fbx = Root + "/Zod_Cameo.fbx", Ctl = Root + "/ZodCameo.controller", Prefab = Root + "/ZodCameo.prefab";
        static string Tex = Root + "/textures", Mats = Root + "/Materials";
        const string Voice = Root + "/Voice", Curves = Root + "/zod_mouth_curves.json";
        static bool V2, V3;

        static readonly HashSet<string> Loop = new HashSet<string>
        { "idle", "idle_hand_on_hip", "idle_look_around", "talk_passionate", "talk_hands_open", "talk_right_hand", "talk_angry", "cigar_puff",
          "hat_tip", "finger_wag", "scheming", "crouch_gun_gesture", "jaw_chatter", "walk", "run" };

        static readonly (string mix, string human)[] Map =
        {
            ("Hips","Hips"),("Spine","Spine"),("Spine1","Chest"),("Spine2","UpperChest"),("Neck","Neck"),("Head","Head"),
            ("LeftShoulder","LeftShoulder"),("LeftArm","LeftUpperArm"),("LeftForeArm","LeftLowerArm"),("LeftHand","LeftHand"),
            ("RightShoulder","RightShoulder"),("RightArm","RightUpperArm"),("RightForeArm","RightLowerArm"),("RightHand","RightHand"),
            ("LeftUpLeg","LeftUpperLeg"),("LeftLeg","LeftLowerLeg"),("LeftFoot","LeftFoot"),("LeftToeBase","LeftToes"),
            ("RightUpLeg","RightUpperLeg"),("RightLeg","RightLowerLeg"),("RightFoot","RightFoot"),("RightToeBase","RightToes"),
        };

        [MenuItem("OpenZ/Zod Cameo/Setup (Humanoid)")] static void MenuHumanoid() => Debug.Log(Run(true));
        [MenuItem("OpenZ/Zod Cameo/Setup (Generic rig)")] static void MenuGeneric() => Debug.Log(Run(false));
        [MenuItem("OpenZ/Zod Cameo/Setup v2 (Humanoid)")] static void MenuV2() => Debug.Log(RunV2(true));
        [MenuItem("OpenZ/Zod Cameo/Setup v3 (Humanoid)")] static void MenuV3() => Debug.Log(RunV3(true));
        [MenuItem("OpenZ/Zod/Use v1")] static void UseV1() => UseModel(1);
        [MenuItem("OpenZ/Zod/Use v2")] static void UseV2() => UseModel(2);
        [MenuItem("OpenZ/Zod/Use v3")] static void UseV3() => UseModel(3);

        /// <summary>
        /// next line builds the chosen model.</summary>
        public static void UseModel(int v)
        {
            v = v == 1 || v == 2 ? v : 3;
            PlayerPrefs.SetInt(ZodStage.PrefModel, v); PlayerPrefs.SetInt(ZodStage.PrefModelMigrated, 1); PlayerPrefs.Save();   // a choice made here is never migrated away
            int dropped = 0;
            if (Application.isPlaying) foreach (var st in Object.FindObjectsByType<ZodStage>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { Object.Destroy(st.gameObject); dropped++; }
            Debug.Log("[ZodCameo] model = v" + v + (dropped > 0 ? " (live stage dropped: the next line rebuilds it)" : ""));
        }

        /// <summary>
        /// shell bandolier) as model 3 next to v1 and v2: Resources/OpenZ/Zod/v3/Zod_v3.fbx + v3/textures, materials in v3/Materials
        /// (Zod3_Body with v1's mouth AO, Zod3_Cigar, Zod3_Brass, Zod3_Gear), controller v3/ZodCameo_v3.controller, prefab
        /// ZodCameo_v3.prefab. The same Humanoid path as v1 (v1's avatar description, clips, loop flags and prefab).</summary>
        public static string RunV3(bool humanoid = true)
        {
            string f = Fbx, c = Ctl, p = Prefab, t = Tex, m = Mats;
            V3 = true; Fbx = Root + "/v3/Zod_v3.fbx"; Ctl = Root + "/v3/ZodCameo_v3.controller"; Prefab = Root + "/ZodCameo_v3.prefab";
            Tex = Root + "/v3/textures"; Mats = Root + "/v3/Materials";
            try { return Run(humanoid).Replace("[ZodCameoSetup]", "[ZodCameoSetup v3]"); }
            finally { V3 = false; Fbx = f; Ctl = c; Prefab = p; Tex = t; Mats = m; }
        }

        /// <summary>
        /// re-import or a new controller: the same MaterialsV3 + BuildPrefab steps RunV3 runs, on the imported FBX and its controller.</summary>
        [MenuItem("OpenZ/Zod Cameo/Setup v3: materials + prefab (no FBX reimport)")] static void MenuV3Prefab() => Debug.Log(RunV3Prefab());
        public static string RunV3Prefab()
        {
            string f = Fbx, c = Ctl, p = Prefab, t = Tex, m = Mats;
            V3 = true; Fbx = Root + "/v3/Zod_v3.fbx"; Ctl = Root + "/v3/ZodCameo_v3.controller"; Prefab = Root + "/ZodCameo_v3.prefab";
            Tex = Root + "/v3/textures"; Mats = Root + "/v3/Materials";
            try
            {
                var log = new List<string> { "[ZodCameoSetup v3] materials + prefab (no FBX reimport)" };
                log.Add(Materials(out var body, out var cigar));
                var ctl = AssetDatabase.LoadAssetAtPath<AnimatorController>(Ctl);
                if (!ctl) return "[ZodCameoSetup v3] " + Ctl + " missing: run OpenZ > Zod Cameo > Setup v3 (Humanoid)";
                log.Add(BuildPrefab(ctl));
                AssetDatabase.SaveAssets();
                return string.Join("\n", log);
            }
            finally { V3 = false; Fbx = f; Ctl = c; Prefab = p; Tex = t; Mats = m; }
        }

        /// <summary>
        /// submeshes (Zod3_Brass + Zod3_Gear: shells, belt, buckle, sheriff star) move to their own SkinnedMeshRenderer
        /// (ZodStage.GearSkinName) on the same bones / bind poses, so it can sit on a rendering layer the ember light leaves out
        /// (ZodStage.EmberLightBit). Both meshes keep only their own vertices (no doubled skinning). Mesh assets v3/Zod_v3_BodySkin.asset +
        /// v3/Zod_v3_GearSkin.asset (overwritten in place, GUIDs kept).</summary>
        static string SplitGearV3(GameObject inst)
        {
            SkinnedMeshRenderer smr = null;
            foreach (var r in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.sharedMaterials.Any(x => x && (x.name.Contains("Brass") || x.name.Contains("Gear")))) { smr = r; break; }
            if (!smr || !smr.sharedMesh) return "gear split: NO gear submesh found (ember exclusion off for v3)";
            var src = smr.sharedMesh; var mats = smr.sharedMaterials;
            var gearIdx = new List<int>(); var bodyIdx = new List<int>();
            for (int i = 0; i < src.subMeshCount; i++)
            {
                string n = i < mats.Length && mats[i] ? mats[i].name : "";
                (n.Contains("Brass") || n.Contains("Gear") ? gearIdx : bodyIdx).Add(i);
            }
            var bodyMesh = SaveMesh(SubMeshes(src, bodyIdx, "Zod_v3_BodySkin"), Root + "/v3/Zod_v3_BodySkin.asset");
            var gearMesh = SaveMesh(SubMeshes(src, gearIdx, "Zod_v3_GearSkin"), Root + "/v3/Zod_v3_GearSkin.asset");
            var go = new GameObject(ZodStage.GearSkinName);
            go.transform.SetParent(smr.transform.parent, false);
            go.transform.localPosition = smr.transform.localPosition; go.transform.localRotation = smr.transform.localRotation; go.transform.localScale = smr.transform.localScale;
            var g = go.AddComponent<SkinnedMeshRenderer>();
            g.sharedMesh = gearMesh; g.bones = smr.bones; g.rootBone = smr.rootBone; g.quality = smr.quality;
            g.sharedMaterials = gearIdx.Select(i => mats[i]).ToArray();
            g.localBounds = smr.localBounds; g.updateWhenOffscreen = true;
            smr.sharedMesh = bodyMesh; smr.sharedMaterials = bodyIdx.Select(i => mats[i]).ToArray();
            return "gear split: " + smr.name + " keeps " + string.Join("+", bodyIdx.Select(i => mats[i].name).ToArray()) + " (" + bodyMesh.vertexCount + " v), "
                 + go.name + " gets " + string.Join("+", gearIdx.Select(i => mats[i].name).ToArray()) + " (" + gearMesh.vertexCount + " v, " + (gearMesh.triangles.Length / 3) + " tris), "
                 + g.bones.Length + " shared bones";
        }

        static Mesh SaveMesh(Mesh m, string path)
        {
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!old) { AssetDatabase.CreateAsset(m, path); return m; }
            EditorUtility.CopySerialized(m, old); old.name = m.name; EditorUtility.SetDirty(old); Object.DestroyImmediate(m);
            return old;
        }

        /// <summary>A mesh with only the given submeshes of src and only the vertices they use (positions, normals, tangents, colours, every
        /// UV channel, all bone weights, the same bind poses).</summary>
        static Mesh SubMeshes(Mesh src, List<int> subs, string name)
        {
            var map = new Dictionary<int, int>(); var order = new List<int>(); var tris = new List<int[]>();
            foreach (int s in subs)
            {
                var t = src.GetTriangles(s);
                for (int k = 0; k < t.Length; k++) { int o = t[k]; int ni; if (!map.TryGetValue(o, out ni)) { ni = order.Count; map[o] = ni; order.Add(o); } t[k] = ni; }
                tris.Add(t);
            }
            var m = new Mesh { name = name, indexFormat = order.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            var v = src.vertices; var nrm = src.normals; var tg = src.tangents; var col = src.colors32;
            m.SetVertices(order.Select(i => v[i]).ToList());
            if (nrm.Length == v.Length) m.SetNormals(order.Select(i => nrm[i]).ToList());
            if (tg.Length == v.Length) m.SetTangents(order.Select(i => tg[i]).ToList());
            if (col.Length == v.Length) m.SetColors(order.Select(i => col[i]).ToList());
            for (int ch = 0; ch < 8; ch++)
            {
                if (!src.HasVertexAttribute(VertexAttribute.TexCoord0 + ch)) continue;
                var uv = new List<Vector4>(); src.GetUVs(ch, uv);
                if (uv.Count == v.Length) m.SetUVs(ch, order.Select(i => uv[i]).ToList());
            }
            var bpv = src.GetBonesPerVertex(); var bw = src.GetAllBoneWeights();
            var start = new int[v.Length]; int acc = 0;
            for (int i = 0; i < v.Length; i++) { start[i] = acc; acc += bpv[i]; }
            var nb = new byte[order.Count]; var nw = new List<BoneWeight1>();
            for (int j = 0; j < order.Count; j++) { int i = order[j]; nb[j] = bpv[i]; for (int k = 0; k < bpv[i]; k++) nw.Add(bw[start[i] + k]); }
            m.bindposes = src.bindposes;
            using (var nbA = new Unity.Collections.NativeArray<byte>(nb, Unity.Collections.Allocator.Temp))
            using (var nwA = new Unity.Collections.NativeArray<BoneWeight1>(nw.ToArray(), Unity.Collections.Allocator.Temp))
                m.SetBoneWeights(nbA, nwA);
            m.subMeshCount = tris.Count;
            for (int s = 0; s < tris.Count; s++) m.SetTriangles(tris[s], s, false);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// v2/textures, materials in v2/Materials (Zod2_Body, Zod2_Cigar, Zod2_Glasses_Frame, Zod2_Glasses_Lens, + Zod2_Joint from round 5),
        /// controller v2/ZodCameo_v2.controller, prefab ZodCameo_v2.prefab. The same importer, clips, loop flags and prefab as v1.</summary>
        public static string RunV2(bool humanoid = true)
        {
            string f = Fbx, c = Ctl, p = Prefab, t = Tex, m = Mats;
            V2 = true; Fbx = Root + "/v2/Zod_v2.fbx"; Ctl = Root + "/v2/ZodCameo_v2.controller"; Prefab = Root + "/ZodCameo_v2.prefab";
            Tex = Root + "/v2/textures"; Mats = Root + "/v2/Materials";
            try { return Run(humanoid).Replace("[ZodCameoSetup]", "[ZodCameoSetup v2]"); }
            finally { V2 = false; Fbx = f; Ctl = c; Prefab = p; Tex = t; Mats = m; }
        }

        public static string Run(bool humanoid = true)
        {
            var log = new List<string> { "[ZodCameoSetup] " + (humanoid ? "Humanoid" : "Generic") };
            if (!File.Exists(Fbx)) return "[ZodCameoSetup] " + Fbx + " missing: run work/staging/zod_cameo/apply_zodbriefing.py --apply first";
            AssetDatabase.StartAssetEditing();
            try
            {
                log.Add(EnsureLayer());
                log.Add(Textures());
                if (!V2 && !V3) log.Add(VoiceClips());
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh();
            log.Add(Materials(out var body, out var cigar));
            log.Add(Model(humanoid, body, cigar));
            log.Add(Controller(out var ctl));
            log.Add(BuildPrefab(ctl));
            AssetDatabase.SaveAssets();
            return string.Join("\n", log);
        }

        // ------------------------------------------------------------------------------------------------ 1 layer
        static string EnsureLayer()
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tm.FindProperty("layers");
            for (int i = 0; i < layers.arraySize; i++) if (layers.GetArrayElementAtIndex(i).stringValue == ZodStage.LayerName) return $"layer {ZodStage.LayerName} = {i} (kept)";
            for (int i = 29; i >= 8; i--)
            {
                var p = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(p.stringValue)) continue;
                p.stringValue = ZodStage.LayerName; tm.ApplyModifiedPropertiesWithoutUndo();
                return $"layer {ZodStage.LayerName} = {i} (new)";
            }
            return "WARNING: no free layer for ZodCameo (runtime picks an unnamed one)";
        }

        // ------------------------------------------------------------------------------------------------ 2 textures
        static string Textures()
        {
            if (V3) return TexturesV3();
            int n = 0;
            var paths = V2 ? new[] { Tex + "/Zod2_Body_BaseColor.png", Tex + "/Zod2_Joint_BaseColor.png", Tex + "/Zod2_Cigar_BaseColor.png", Tex + "/Zod2_Cigar_Emission.png" }
                           : new[] { Tex + "/Zod_Body_BaseColor.png", Tex + "/Zod_Cigar_BaseColor.png", Tex + "/Zod_Cigar_Emission.png", Tex + "/zod_smoke.png" };
            foreach (var path in paths)
            {
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) continue;
                bool body = path.Contains("Body"), smoke = path.Contains("smoke"), joint = path.Contains("Joint");
                ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.mipmapEnabled = true;
                ti.alphaSource = smoke ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None; ti.alphaIsTransparency = smoke;
                ti.maxTextureSize = body ? 4096 : smoke ? 128 : 256; ti.anisoLevel = body ? 8 : 2; ti.filterMode = FilterMode.Trilinear;
                ti.wrapMode = smoke ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                if (joint) { ti.maxTextureSize = 256; ti.anisoLevel = 2; ti.filterMode = FilterMode.Bilinear; }
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                { name = "Standalone", overridden = true, maxTextureSize = ti.maxTextureSize, format = TextureImporterFormat.BC7, compressionQuality = 100 });
                ti.SaveAndReimport(); n++;
            }
            return $"textures: {n} set (body 4096 BC7 HQ, aniso 8)";
        }

        /// <summary>
        /// wrap (the strap UVs run past 0..1) and aniso 8, never the 256 cap of the small maps; cigar maps 256; the mouth AO = v1's
        /// Zod_Body_MouthAO import (linear, no mipmaps, uncompressed, clamp).</summary>
        static string TexturesV3()
        {
            int n = 0;
            foreach (var path in new[] { Tex + "/Zod3_Body_BaseColor.png", Tex + "/Zod3_Gear_BaseColor.png", Tex + "/Zod3_Cigar_BaseColor.png", Tex + "/Zod3_Cigar_Emission.png", Tex + "/Zod3_Body_MouthAO.png" })
            {
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) { Debug.LogWarning("[ZodCameoSetup v3] texture missing: " + path); continue; }
                bool body = path.EndsWith("Body_BaseColor.png"), gear = path.Contains("Gear"), ao = path.Contains("MouthAO");
                ti.textureType = TextureImporterType.Default;
                ti.alphaSource = TextureImporterAlphaSource.None; ti.alphaIsTransparency = false;
                if (ao)
                {
                    ti.sRGBTexture = false; ti.mipmapEnabled = false; ti.maxTextureSize = 1024; ti.anisoLevel = 1; ti.filterMode = FilterMode.Bilinear;
                    ti.wrapMode = TextureWrapMode.Clamp; ti.textureCompression = TextureImporterCompression.Uncompressed;
                    ti.ClearPlatformTextureSettings("Standalone");
                    ti.SaveAndReimport(); n++; continue;
                }
                ti.sRGBTexture = true; ti.mipmapEnabled = true;
                ti.maxTextureSize = body ? 4096 : gear ? 1024 : 256; ti.anisoLevel = body || gear ? 8 : 2; ti.filterMode = FilterMode.Trilinear;
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                { name = "Standalone", overridden = true, maxTextureSize = ti.maxTextureSize, format = TextureImporterFormat.BC7, compressionQuality = 100 });
                ti.SaveAndReimport(); n++;
            }
            return $"textures v3: {n} set (body 4096 BC7 HQ aniso 8, gear atlas 1024 BC7 HQ Repeat aniso 8, cigar 256, mouth AO linear)";
        }

        // ------------------------------------------------------------------------------------------------ 7 voice
        static string VoiceClips()
        {
            int n = 0;
            foreach (var path in Directory.GetFiles(Voice, "*.wav").Select(p => p.Replace('\\', '/')))
            {
                if (!(AssetImporter.GetAtPath(path) is AudioImporter ai)) continue;
                ai.forceToMono = true; ai.loadInBackground = false;
                var so = new SerializedObject(ai); var norm = so.FindProperty("normalize") ?? so.FindProperty("m_Normalize");
                if (norm != null) { norm.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); }
                var s = ai.defaultSampleSettings;
                s.loadType = AudioClipLoadType.DecompressOnLoad; s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = 0.7f; s.preloadAudioData = true;
                ai.defaultSampleSettings = s;
                ai.SaveAndReimport(); n++;
            }
            return $"voice: {n} clips (mono, normalize off, decompress on load, Vorbis 0.7, preload)";
        }

        // ------------------------------------------------------------------------------------------------ 3 materials
        static Material Mat(string name, string shader)
        {
            Directory.CreateDirectory(Mats);
            string path = Mats + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find(shader);
            if (!m) { m = new Material(sh) { name = name }; AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != sh) m.shader = sh;
            return m;
        }

        static void LitKeywords(Material m) { try { BaseShaderGUI.SetMaterialKeywords(m, LitGUI.SetMaterialKeywords); } catch (System.Exception e) { Debug.LogWarning("[ZodCameoSetup] keywords " + m.name + ": " + e.Message); } }

        static Material frameM, lensM, jointM, gearM, brassM;

        static string Materials(out Material body, out Material cigar)
        {
            const string lit = "Universal Render Pipeline/Lit";
            if (V2) return MaterialsV2(out body, out cigar);
            if (V3) return MaterialsV3(out body, out cigar);
            body = Mat("Zod_Body", lit);
            body.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "/Zod_Body_BaseColor.png")); body.SetColor("_BaseColor", Color.white);
            body.SetFloat("_WorkflowMode", 1f); body.SetFloat("_Metallic", 0.3f); body.SetFloat("_Smoothness", 0.45f);
            LitKeywords(body); EditorUtility.SetDirty(body);

            cigar = Mat("Zod_Cigar", lit);
            cigar.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "/Zod_Cigar_BaseColor.png")); cigar.SetColor("_BaseColor", Color.white);
            cigar.SetFloat("_Metallic", 0f); cigar.SetFloat("_Smoothness", 0.3f);
            cigar.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "/Zod_Cigar_Emission.png"));
            cigar.SetColor("_EmissionColor", new Color(1f, 0.38f, 0.06f) * 2.5f); cigar.EnableKeyword("_EMISSION");
            cigar.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            LitKeywords(cigar); cigar.EnableKeyword("_EMISSION"); EditorUtility.SetDirty(cigar);

            // stage templates (ZodStage instantiates them; the assets keep the variants in a build)
            var st = Mat("ZodStage_Lit", lit); st.SetColor("_BaseColor", new Color32(40, 45, 42, 255)); st.SetFloat("_Smoothness", 0.3f); LitKeywords(st); EditorUtility.SetDirty(st);
            var gl = Mat("ZodStage_Glow", lit); gl.SetColor("_BaseColor", Color.black); gl.SetColor("_EmissionColor", Color.white); gl.EnableKeyword("_EMISSION");
            LitKeywords(gl); gl.EnableKeyword("_EMISSION"); gl.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; EditorUtility.SetDirty(gl);
            var co = Mat("ZodStage_Cutout", lit); co.SetFloat("_AlphaClip", 1f); co.SetFloat("_Cutoff", 0.4f); LitKeywords(co); co.EnableKeyword("_ALPHATEST_ON"); EditorUtility.SetDirty(co);
            var sm = Mat("ZodStage_Smoke", "Universal Render Pipeline/Particles/Unlit");
            sm.SetFloat("_Surface", 1f); sm.SetFloat("_Blend", 0f); sm.SetFloat("_ZWrite", 0f); sm.SetOverrideTag("RenderType", "Transparent");
            sm.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "/zod_smoke.png")); sm.SetColor("_BaseColor", Color.white);
            try { BaseShaderGUI.SetMaterialKeywords(sm, null, ParticleGUI.SetMaterialKeywords); } catch (System.Exception e) { Debug.LogWarning("[ZodCameoSetup] smoke keywords: " + e.Message); }
            sm.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); sm.renderQueue = 3000; EditorUtility.SetDirty(sm);
            AssetDatabase.SaveAssets();
            return "materials: Zod_Body, Zod_Cigar (+ember), ZodStage_Lit / _Glow / _Cutout / _Smoke";
        }

        static string MaterialsV2(out Material body, out Material cigar)
        {
            const string lit = "Universal Render Pipeline/Lit";
            body = Mat("Zod2_Body", lit);
            body.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "/Zod2_Body_BaseColor.png")); body.SetColor("_BaseColor", Color.white);
            body.SetFloat("_WorkflowMode", 1f); body.SetFloat("_Metallic", 0.3f); body.SetFloat("_Smoothness", 0.45f);
            LitKeywords(body); EditorUtility.SetDirty(body);
            cigar = Mat("Zod2_Cigar", lit);
            cigar.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "/Zod2_Cigar_BaseColor.png")); cigar.SetColor("_BaseColor", Color.white);
            cigar.SetFloat("_Metallic", 0f); cigar.SetFloat("_Smoothness", 0.3f);
            cigar.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "/Zod2_Cigar_Emission.png"));
            cigar.SetColor("_EmissionColor", new Color(1f, 0.38f, 0.06f) * 2.5f); cigar.EnableKeyword("_EMISSION");
            cigar.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            LitKeywords(cigar); cigar.EnableKeyword("_EMISSION"); EditorUtility.SetDirty(cigar);
            frameM = Mat("Zod2_Glasses_Frame", lit);
            frameM.SetColor("_BaseColor", new Color(0.98f, 0.85f, 0.57f)); frameM.SetFloat("_Metallic", 1f); frameM.SetFloat("_Smoothness", 0.72f); LitKeywords(frameM); EditorUtility.SetDirty(frameM);
            lensM = Mat("Zod2_Glasses_Lens", lit);
            lensM.SetColor("_BaseColor", new Color(0.15f, 0.14f, 0.13f)); lensM.SetFloat("_Metallic", 0f); lensM.SetFloat("_Smoothness", 0.95f); LitKeywords(lensM); EditorUtility.SetDirty(lensM);
            jointM = null;
            var jt = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "/Zod2_Joint_BaseColor.png");
            if (jt)
            {   // round 5 on: the joint suit
                jointM = Mat("Zod2_Joint", lit); jointM.SetTexture("_BaseMap", jt); jointM.SetColor("_BaseColor", Color.white);
                jointM.SetFloat("_Metallic", 0f); jointM.SetFloat("_Smoothness", 0.25f); LitKeywords(jointM); EditorUtility.SetDirty(jointM);
            }
            AssetDatabase.SaveAssets();
            return "materials v2: Zod2_Body, Zod2_Cigar (+ember), Zod2_Glasses_Frame, Zod2_Glasses_Lens" + (jointM ? ", Zod2_Joint" : "");
        }

        /// <summary>
        /// open mouth), v1's cigar with its ember, the gear leather (metallic 0, smoothness 0.38) and the brass of the shells, buckle and
        /// sheriff star (metallic 0.85, smoothness 0.64), both on the 1024 gear atlas.</summary>
        public const float BrassSmoothness = 0.5f;
        public static readonly Color BrassTint = new Color(0.92f, 0.88f, 0.80f, 1f);
        static string MaterialsV3(out Material body, out Material cigar)
        {
            const string lit = "Universal Render Pipeline/Lit";
            var gearTex = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "/Zod3_Gear_BaseColor.png");
            var ao = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "/Zod3_Body_MouthAO.png");
            body = Mat("Zod3_Body", lit);
            body.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "/Zod3_Body_BaseColor.png")); body.SetColor("_BaseColor", Color.white);
            body.SetFloat("_WorkflowMode", 1f); body.SetFloat("_Metallic", 0.3f); body.SetFloat("_Smoothness", 0.45f);
            body.SetTexture("_OcclusionMap", ao); body.SetFloat("_OcclusionStrength", 1f);
            LitKeywords(body); if (ao) body.EnableKeyword("_OCCLUSIONMAP"); EditorUtility.SetDirty(body);
            cigar = Mat("Zod3_Cigar", lit);
            cigar.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "/Zod3_Cigar_BaseColor.png")); cigar.SetColor("_BaseColor", Color.white);
            cigar.SetFloat("_Metallic", 0f); cigar.SetFloat("_Smoothness", 0.3f);
            cigar.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + "/Zod3_Cigar_Emission.png"));
            cigar.SetColor("_EmissionColor", new Color(1f, 0.38f, 0.06f) * 2.5f); cigar.EnableKeyword("_EMISSION");
            cigar.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            LitKeywords(cigar); cigar.EnableKeyword("_EMISSION"); EditorUtility.SetDirty(cigar);
            gearM = Mat("Zod3_Gear", lit);
            gearM.SetTexture("_BaseMap", gearTex); gearM.SetColor("_BaseColor", Color.white);
            gearM.SetFloat("_WorkflowMode", 1f); gearM.SetFloat("_Metallic", 0f); gearM.SetFloat("_Smoothness", 0.38f); LitKeywords(gearM); EditorUtility.SetDirty(gearM);
            // f_v3_win04_1.0s 322 px at ~0.93 x the frame's tonemap ceiling): the flat cap discs mirrored the HQ key. Smoothness 0.64 -> 0.5
            // (~3.7x lower GGX peak, a broader softer highlight) and a slightly darker, warmer brass tint so the caps keep a brass hue.
            brassM = Mat("Zod3_Brass", lit);
            brassM.SetTexture("_BaseMap", gearTex); brassM.SetColor("_BaseColor", BrassTint);
            brassM.SetFloat("_WorkflowMode", 1f); brassM.SetFloat("_Metallic", 0.85f); brassM.SetFloat("_Smoothness", BrassSmoothness); LitKeywords(brassM); EditorUtility.SetDirty(brassM);
            AssetDatabase.SaveAssets();
            return "materials v3: Zod3_Body (+mouth AO " + (ao ? "OK" : "MISSING") + "), Zod3_Cigar (+ember), Zod3_Gear, Zod3_Brass (gear atlas " + (gearTex ? gearTex.width + " px" : "MISSING") + ")";
        }

        static Material PickMat(string n, Material body, Material cigar)
        {
            if (n.Contains("Cigar")) return cigar;
            if (V3 && n.Contains("Brass") && brassM) return brassM;
            if (V3 && n.Contains("Gear") && gearM) return gearM;
            if (V2 && n.Contains("Glasses_Frame") && frameM) return frameM;
            if (V2 && n.Contains("Glasses_Lens") && lensM) return lensM;
            if (V2 && n.Contains("Joint") && jointM) return jointM;
            return body;
        }

        // ------------------------------------------------------------------------------------------------ 4 model
        static string Model(bool humanoid, Material body, Material cigar)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(Fbx);
            mi.globalScale = 1f; mi.useFileScale = true; mi.importCameras = false; mi.importLights = false; mi.importVisibility = false;
            mi.preserveHierarchy = true; mi.importBlendShapes = false; mi.optimizeGameObjects = false; mi.importAnimation = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.animationType = humanoid ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            if (humanoid) mi.humanDescription = Describe(AssetDatabase.LoadAssetAtPath<GameObject>(Fbx));
            else mi.motionNodeName = "";
            // the FBX's materials -> ours (Blender export names Zod_Body / Zod_Cigar, plus whatever the importer found)
            var names = V2 ? new HashSet<string> { "Zod2_Body", "Zod2_Cigar", "Zod2_Glasses_Frame", "Zod2_Glasses_Lens" }
                      : V3 ? new HashSet<string> { "Zod3_Body", "Zod3_Cigar", "Zod3_Brass", "Zod3_Gear" } : new HashSet<string> { "Zod_Body", "Zod_Cigar" };
            foreach (var m in AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<Material>()) names.Add(m.name);
            foreach (var n in names)
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), n);
                mi.RemoveRemap(id);
                mi.AddRemap(id, PickMat(n, body, cigar));
            }
            mi.SaveAndReimport();
            var clips = new List<ModelImporterClipAnimation>();
            foreach (var c in mi.defaultClipAnimations)
            {
                string name = c.takeName.Contains("|") ? c.takeName.Substring(c.takeName.LastIndexOf('|') + 1) : c.takeName;
                c.name = name;
                c.loopTime = Loop.Contains(name); c.loopPose = false;
                c.lockRootRotation = true; c.keepOriginalOrientation = true;
                c.lockRootHeightY = true; c.keepOriginalPositionY = true; c.heightFromFeet = false;
                c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
                c.events = name == "cigar_puff" ? new[] { new AnimationEvent { functionName = "ZodPuff", time = 0.5f / 4.0f } } : new AnimationEvent[0];
                clips.Add(c);
            }
            mi.clipAnimations = clips.ToArray();
            mi.SaveAndReimport();
            var av = AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<Avatar>().FirstOrDefault();
            string info = av ? $"avatar valid={av.isValid} human={av.isHuman}" : "no avatar";
            if (humanoid && (av == null || !av.isValid || !av.isHuman))
            {
                Debug.LogWarning("[ZodCameoSetup] Humanoid avatar not valid (" + info + "): falling back to Generic");
                return Model(false, body, cigar) + " (Humanoid refused: " + info + ")";
            }
            return $"model: {(humanoid ? "Humanoid" : "Generic")}, {clips.Count} clips ({clips.Count(c => c.loopTime)} looping), {info}";
        }

        /// <summary>Humanoid description of Zod's own skeleton: mixamorig bones mapped (no fingers: mitts), the Jaw deliberately not
        /// mapped (its keyed slide stays a generic curve), arms set to a T-pose and legs straight in the reference skeleton (the
        /// imported bind pose is an A-pose), like OpenZBS01.Describe.</summary>
        static HumanDescription Describe(GameObject src)
        {
            var inst = Object.Instantiate(src);
            try
            {
                var all = inst.GetComponentsInChildren<Transform>(true);
                Transform F(string n) => all.FirstOrDefault(t => t.name == "mixamorig" + n);
                var hips = F("Hips");
                foreach (var side in new[] { "Left", "Right" })
                {
                    var arm = F(side + "Arm"); if (!arm || !hips) continue;
                    var dir = new Vector3(Mathf.Sign(arm.position.x - hips.position.x), 0, 0);
                    foreach (var (b, c) in new[] { ("Arm", "ForeArm"), ("ForeArm", "Hand") })
                    {
                        var bt = F(side + b); var ct = F(side + c); if (!bt || !ct) continue;
                        bt.rotation = Quaternion.FromToRotation(ct.position - bt.position, dir) * bt.rotation;
                    }
                    foreach (var (b, c) in new[] { ("UpLeg", "Leg"), ("Leg", "Foot") })
                    {
                        var bt = F(side + b); var ct = F(side + c); if (!bt || !ct) continue;
                        bt.rotation = Quaternion.FromToRotation(ct.position - bt.position, Vector3.down) * bt.rotation;
                    }
                }
                var human = new List<HumanBone>();
                foreach (var (mix, h) in Map) if (F(mix)) { var hb = new HumanBone { humanName = h, boneName = "mixamorig" + mix }; hb.limit.useDefaultValues = true; human.Add(hb); }
                var skel = all.Select(t => new SkeletonBone { name = t == inst.transform ? src.name : t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();
                return new HumanDescription
                {
                    human = human.ToArray(), skeleton = skel,
                    upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                    armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false,
                };
            }
            finally { Object.DestroyImmediate(inst); }
        }

        // ------------------------------------------------------------------------------------------------ 5 controller
        static string Controller(out AnimatorController ctl)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).OrderBy(c => c.name == "idle" ? 0 : 1).ThenBy(c => c.name).ToList();
            AssetDatabase.DeleteAsset(Ctl);
            ctl = AnimatorController.CreateAnimatorControllerAtPath(Ctl);
            var sm = ctl.layers[0].stateMachine;
            int i = 0;
            foreach (var c in clips)
            {
                var s = sm.AddState(c.name, new Vector3(300 + (i % 4) * 230, 40 + (i / 4) * 60, 0));
                s.motion = c; s.writeDefaultValues = true;
                if (c.name == "idle") sm.defaultState = s;
                i++;
            }
            EditorUtility.SetDirty(ctl);
            return $"controller: {clips.Count} states ({string.Join(", ", clips.Select(c => c.name))})";
        }

        // ------------------------------------------------------------------------------------------------ 6 prefab
        static string BuildPrefab(AnimatorController ctl)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            try
            {
                inst.name = V2 ? "ZodCameo_v2" : V3 ? "ZodCameo_v3" : "ZodCameo";
                var an = inst.GetComponent<Animator>(); if (!an) an = inst.AddComponent<Animator>();
                an.avatar = AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<Avatar>().FirstOrDefault();
                an.runtimeAnimatorController = ctl; an.applyRootMotion = false;
                an.updateMode = AnimatorUpdateMode.UnscaledTime; an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var rig = inst.GetComponent<ZodTalkRig>(); if (!rig) rig = inst.AddComponent<ZodTalkRig>();
                rig.mouthCurves = AssetDatabase.LoadAssetAtPath<TextAsset>(Curves);
                int layer = LayerMask.NameToLayer(ZodStage.LayerName);
                if (layer < 0) layer = ZodStage.ResolveLayer();
                foreach (var t in inst.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    r.renderingLayerMask = 1u << ZodStage.LightLayerBit; r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true;
                    r.lightProbeUsage = LightProbeUsage.Off;
                    if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                }
                string split = V3 ? ", " + SplitGearV3(inst) : "";
                PrefabUtility.SaveAsPrefabAsset(inst, Prefab, out bool ok);
                return ok ? $"prefab{split}: {Prefab} (avatar {(an.avatar ? an.avatar.name : "none")}, rig curves {(rig.mouthCurves ? "OK" : "MISSING")}, layer {layer})" : "PREFAB SAVE FAILED";
            }
            finally { Object.DestroyImmediate(inst); }
        }

        // ------------------------------------------------------------------------------------------------ Play-mode checks
        [MenuItem("OpenZ/Zod Cameo/Play check: briefing now")] static void PBriefing() => Debug.Log(ZodBriefing.DebugBriefing());
        [MenuItem("OpenZ/Zod Cameo/Play check: victory line")] static void PWin() => Debug.Log(ZodBriefing.DebugPunchline(true));
        [MenuItem("OpenZ/Zod Cameo/Play check: defeat line")] static void PLose() => Debug.Log(ZodBriefing.DebugPunchline(false));
        [MenuItem("OpenZ/Zod Cameo/Play check: idle barb")] static void PIdle() => Debug.Log(ZodBriefing.DebugIdle());
        [MenuItem("OpenZ/Zod Cameo/Play check: status")] static void PStatus() => Debug.Log(ZodBriefing.Status());
        [MenuItem("OpenZ/Zod Cameo/Briefing on every level start (toggle)")]
        static void ToggleAlways()
        {
            int v = PlayerPrefs.GetInt(ZodBriefing.PrefBriefing, 1) == 2 ? 1 : 2;
            PlayerPrefs.SetInt(ZodBriefing.PrefBriefing, v); PlayerPrefs.Save();
            Debug.Log("[ZodCameo] oz_zod_briefing = " + v + (v == 2 ? " (every start, also a level opened directly)" : " (first start of each level per session, from the menu)"));
        }
    }
}
