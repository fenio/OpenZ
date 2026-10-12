using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    ///  - 1 Reimport models: the import settings below on both FBX + their textures.
    ///  - 2 Build materials + prefabs: URP Lit materials (Models/ZodBoys/Materials), one Animator controller per robot (one state per
    ///    clip, state name = clip name, default = the reference pose), and Resources/OpenZ/Zod/Boys/ZodAlan + ZodBrad: the rig, the
    ///    MOUTH / EYES nodes under the Head bone, the rocket-fuel can FUEL_CAN in the left hand (HAND_L, off by default) and a second
    ///    can FUEL_CAN_R in the right hand socket (HAND_R, off by default). ZodStage loads these prefabs (the grunts are the fallback).
    ///  - 3 Check: prefabs, states, nodes, materials, heights, the jaw direction and Alan's mouth curves; PASS / FAIL per item.
    ///  - Play: briefing line / victory outro / hold the front shot / save its frames / log status (Play mode).
    /// Import settings (ZodBoysImport): scale 1 + convert units (the robots come in at 2.34 m and 2.92 m), Generic rig with its own
    /// avatar, no materials from the FBX (the prefab assigns them), all transforms kept (MOUTH / EYES / HAND_* / FUEL_CAN), the takes as
    /// clips (Loop Time on Idle / Talk / HandsClasped / ArmsCrossed / Nervous, Drink once), root motion baked into the pose.
    /// Textures: BaseColor sRGB, Normal as a normal map, MetallicSmoothness linear (metallic RGB, smoothness A), 2048.
    /// Brad only: the model is scaled to his 2.92 m (BradScale) and his base colour gets a gain at import (BradBaseColorGain).
    /// </summary>
    public static class OpenZAlanBradSetup
    {
        public const string Dir = "Assets/OpenZ/Models/ZodBoys", TexDir = Dir + "/Textures", MatDir = Dir + "/Materials";
        public const string ResDir = "Assets/OpenZ/Resources/OpenZ/Zod/Boys";
        public const string CanMat = "Assets/OpenZ/Intro/Materials/M_Intro_Can.mat";
        static readonly (string name, string[] clips, string pose, float height)[] Robots =
        {
            ("Alan", new[] { "Idle", "Talk", "Drink", "HandsClasped", "Nervous" }, "HandsClasped", 2.34f),
            ("Brad", new[] { "Idle", "Talk", "Drink", "ArmsCrossed" }, "ArmsCrossed", 2.92f),
        };
        public static readonly HashSet<string> Looping = new HashSet<string> { "Idle", "Talk", "HandsClasped", "ArmsCrossed", "Nervous" };
        static readonly string[] Nodes = { "Head", "MOUTH", "EYES", "HAND_L", "HAND_R", "FUEL_CAN", "FUEL_CAN_R" };

        // ------------------------------------------------------------------------------------------------ 1 import
        [MenuItem("OpenZ/Zod Boys/1 Reimport models (import settings)", false, 10)]
        public static void Reimport()
        {
            foreach (var r in Robots) AssetDatabase.ImportAsset($"{Dir}/{r.name}.fbx", ImportAssetOptions.ForceUpdate);
            foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { TexDir })) AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(g), ImportAssetOptions.ForceUpdate);
            Debug.Log("[OpenZ ZodBoys] reimported Alan.fbx, Brad.fbx and their textures; next: OpenZ > Zod Boys > 2 Build materials + prefabs");
        }

        // ------------------------------------------------------------------------------------------------ 2 build
        [MenuItem("OpenZ/Zod Boys/2 Build materials + prefabs", false, 11)]
        public static void Build()
        {
            Folder(MatDir); Folder(ResDir);
            var can = AssetDatabase.LoadAssetAtPath<Material>(CanMat);
            if (!can) Debug.LogWarning("[OpenZ ZodBoys] " + CanMat + " missing: the cans get the body material");
            foreach (var r in Robots)
            {
                string fbx = $"{Dir}/{r.name}.fbx";
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                if (!src) { Debug.LogError("[OpenZ ZodBoys] " + fbx + " missing (apply.py copies it)"); continue; }
                var body = MakeMat($"M_{r.name}_Body", r.name, false);
                var eyes = MakeMat($"M_{r.name}_Eyes", r.name, true);
                var ctrl = MakeController(r.name, fbx, r.clips, r.pose);
                var go = Object.Instantiate(src);
                go.name = "Zod" + r.name;
                foreach (var rd in go.GetComponentsInChildren<Renderer>(true))
                {
                    var m = rd.name.StartsWith("FUEL_CAN") ? (can ? can : body) : rd.name == "EYES" ? eyes : body;
                    rd.sharedMaterials = Enumerable.Repeat(m, Mathf.Max(1, rd.sharedMaterials.Length)).ToArray();
                    rd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                var an = go.GetComponent<Animator>();
                if (!an) an = go.AddComponent<Animator>();
                an.runtimeAnimatorController = ctrl; an.applyRootMotion = false; an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var canL = Deep(go.transform, "FUEL_CAN"); var handR = Deep(go.transform, "HAND_R");
                if (canL && handR && !Deep(go.transform, "FUEL_CAN_R"))
                {   // the same can, seated the same way in the right-hand socket (HAND_R has HAND_L's conventions)
                    var cr = Object.Instantiate(canL.gameObject, handR, false);
                    cr.name = "FUEL_CAN_R"; cr.transform.localPosition = canL.localPosition; cr.transform.localRotation = canL.localRotation; cr.transform.localScale = canL.localScale;
                    cr.SetActive(false);
                }
                if (canL) canL.gameObject.SetActive(false);                                                // shown by the celebration shot only
                string path = $"{ResDir}/Zod{r.name}.prefab";
                PrefabUtility.SaveAsPrefabAsset(go, path);
                Object.DestroyImmediate(go);
                Debug.Log($"[OpenZ ZodBoys] {path}: controller {AssetDatabase.GetAssetPath(ctrl)}, materials {body.name} / {eyes.name} / {(can ? can.name : body.name)}");
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[OpenZ ZodBoys] built; run OpenZ > Zod Boys > 3 Check");
        }

        static Material MakeMat(string matName, string robot, bool eyes)
        {
            string path = $"{MatDir}/{matName}.mat";
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(sh) { name = matName }; AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != sh) m.shader = sh;
            Texture2D T(string s) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{robot}_{s}.png");
            var bc = T("BaseColor"); var nm = T("Normal"); var ms = T("MetallicSmoothness");
            m.SetTexture("_BaseMap", bc); m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_WorkflowMode", 1f); m.SetFloat("_Surface", 0f);
            m.SetTexture("_MetallicGlossMap", ms); m.SetFloat("_Metallic", 1f); m.SetFloat("_Smoothness", 1f); m.SetFloat("_SmoothnessTextureChannel", 0f);   // Metallic Alpha
            if (ms) m.EnableKeyword("_METALLICSPECGLOSSMAP"); else m.DisableKeyword("_METALLICSPECGLOSSMAP");
            m.SetTexture("_BumpMap", nm); m.SetFloat("_BumpScale", 1f);
            if (nm) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
            if (eyes)
            {   // only the bright pupils / slits of the colour map glow; linear HDR through SetVector (SetColor would gamma-convert it)
                m.SetTexture("_EmissionMap", bc); m.SetVector("_EmissionColor", new Vector4(2.0f, 1.84f, 1.6f, 1f));
                m.EnableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            else { m.SetVector("_EmissionColor", Vector4.zero); m.DisableKeyword("_EMISSION"); }
            EditorUtility.SetDirty(m);
            return m;
        }

        static AnimatorController MakeController(string robot, string fbx, string[] clips, string pose)
        {
            string path = $"{Dir}/{robot}.controller";
            AssetDatabase.DeleteAsset(path);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm = ac.layers[0].stateMachine;
            var all = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
            foreach (var n in clips)
            {
                var clip = all.FirstOrDefault(c => c.name == n || c.name.EndsWith("|" + n));
                if (!clip) { Debug.LogWarning($"[OpenZ ZodBoys] {fbx}: no clip '{n}' (takes: {string.Join(", ", all.Select(c => c.name))})"); continue; }
                var st = sm.AddState(n); st.motion = clip; st.writeDefaultValues = true;
                if (n == pose) sm.defaultState = st;
            }
            EditorUtility.SetDirty(ac);
            return ac;
        }

        // ------------------------------------------------------------------------------------------------ 3 check
        [MenuItem("OpenZ/Zod Boys/3 Check", false, 12)]
        public static void Check()
        {
            var sb = new System.Text.StringBuilder(); bool ok = true;
            void Line(bool pass, string what) { ok &= pass; sb.AppendLine((pass ? "  PASS  " : "  FAIL  ") + what); }
            foreach (var r in Robots)
            {
                string res = $"OpenZ/Zod/Boys/Zod{r.name}";
                var p = Resources.Load<GameObject>(res);
                Line(p, $"{r.name}: Resources/{res}.prefab");
                if (!p) continue;
                var an = p.GetComponent<Animator>();
                var ctrl = an ? an.runtimeAnimatorController : null;
                var names = ctrl ? ctrl.animationClips.Select(c => c.name).ToList() : new List<string>();
                Line(ctrl && r.clips.All(names.Contains), $"{r.name}: controller states {string.Join(", ", names)}");
                foreach (var n in Nodes) Line(Deep(p.transform, n), $"{r.name}: node {n}");
                var can = Deep(p.transform, "FUEL_CAN");
                Line(can && !can.gameObject.activeSelf, $"{r.name}: FUEL_CAN off by default");
                foreach (var rd in p.GetComponentsInChildren<Renderer>(true))
                    Line(rd.sharedMaterials.All(m => m && m.shader && m.shader.name == "Universal Render Pipeline/Lit" && (rd.name.StartsWith("FUEL_CAN") || m.GetTexture("_BaseMap"))),
                         $"{r.name}: {rd.name} materials {string.Join(", ", rd.sharedMaterials.Select(m => m ? m.name : "NULL"))}");
                foreach (var c in ctrl ? ctrl.animationClips : new AnimationClip[0])
                    Line(c.isLooping == Looping.Contains(c.name), $"{r.name}: clip {c.name} {c.length:F2} s loop {c.isLooping}");
                // height on the reference pose + the jaw direction (a point in front of the hinge must drop for +X)
                var go = Object.Instantiate(p);
                try
                {
                    var pose = ctrl ? ctrl.animationClips.FirstOrDefault(c => c.name == "Idle") : null;
                    if (pose) pose.SampleAnimation(go, 0f);
                    var b = new Bounds(); bool any = false;
                    foreach (var rd in go.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        if (rd.name.StartsWith("FUEL_CAN")) continue;
                        var m = new Mesh(); rd.BakeMesh(m, true);
                        foreach (var v in m.vertices) { var w = rd.transform.TransformPoint(v); if (!any) { b = new Bounds(w, Vector3.zero); any = true; } else b.Encapsulate(w); }
                        Object.DestroyImmediate(m);
                    }
                    Line(any && Mathf.Abs(b.size.y - r.height) < 0.12f, $"{r.name}: height {b.size.y:F2} m on Idle (want {r.height:F2}), feet {b.min.y:+0.00;-0.00} m");
                    var mouth = Deep(go.transform, "MOUTH");
                    if (mouth)
                    {
                        Vector3 local = Quaternion.Inverse(mouth.rotation) * (-go.transform.forward) * 0.1f;
                        float y0 = mouth.TransformPoint(local).y; var rest = mouth.localRotation;
                        mouth.localRotation = rest * Quaternion.Euler(16f, 0f, 0f);
                        float y1 = mouth.TransformPoint(local).y; mouth.localRotation = rest;
                        sb.AppendLine($"  INFO  {r.name}: +16 deg on MOUTH local X moves the jaw front {(y1 - y0) * 100f:+0.0;-0.0} cm ({(y1 < y0 ? "opens, sign +1" : "lifts: ZodStage flips the sign itself")})");
                    }
                }
                finally { Object.DestroyImmediate(go); }
            }
            var curves = Resources.Load<TextAsset>(ZodStage.AlanCurvesPath);
            Line(curves && new[] { "zod_brief_L02", "zod_brief_L17", "zod_win_02" }.All(id => curves.text.Contains("\"" + id + "\"")), "Alan's mouth curves (Resources/" + ZodStage.AlanCurvesPath + ".json): briefings + zod_win_02");
            Debug.Log("[OpenZ ZodBoys] check " + (ok ? "PASS" : "FAIL") + "\n" + sb);
        }

        // ------------------------------------------------------------------------------------------------ Play
        static string Level => ZGame.I ? ZGame.I.level : "L02";
        [MenuItem("OpenZ/Zod Boys/Play: briefing line (backs + plan of attack)", false, 30)]
        static void PlayBriefing() => Log(EditorApplication.isPlaying ? ZodBriefing.DebugLineAs("zod_brief_" + Level, ZodBriefing.Kind.Briefing) : "enter Play first (a level, or OpenZ > Briefing Camera > Play Briefing From Start for the full briefing)");
        [MenuItem("OpenZ/Zod Boys/Play: victory outro (celebration with the cans)", false, 31)]
        static void PlayOutro() => Log(EditorApplication.isPlaying ? ZodBriefing.DebugLineAs(ZodBriefing.AlanOutroId, ZodBriefing.Kind.Victory) : "enter Play first");
        [MenuItem("OpenZ/Zod Boys/Play: hold front shot at the cut", false, 40)] static void H0() => Log(ZodBriefing.DebugFrontHold(0f));
        [MenuItem("OpenZ/Zod Boys/Play: hold front shot in the middle", false, 41)] static void H1() => Log(ZodBriefing.DebugFrontHold(0.5f));
        [MenuItem("OpenZ/Zod Boys/Play: hold front shot at the end", false, 42)] static void H2() => Log(ZodBriefing.DebugFrontHold(1f));
        [MenuItem("OpenZ/Zod Boys/Play: release the hold", false, 43)] static void HR() => Log(ZodBriefing.DebugFrontHold(-1f));
        [MenuItem("OpenZ/Zod Boys/Play: save front shot frames (Captures/ZodBoys)", false, 50)]
        static void Frames() => Log(ZodBriefing.DebugFrontFrames(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures", "ZodBoys"))));
        [MenuItem("OpenZ/Zod Boys/Log status", false, 60)] static void Status() => Log(ZodBriefing.DebugFront() + "\n" + ZodBriefing.DebugBoys());
        static void Log(string s) => Debug.Log("[OpenZ ZodBoys] " + s);

        // ------------------------------------------------------------------------------------------------ helpers
        static Transform Deep(Transform t, string n)
        {
            if (!t) return null;
            if (t.name == n) return t;
            for (int i = 0; i < t.childCount; i++) { var r = Deep(t.GetChild(i), n); if (r) return r; }
            return null;
        }

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Folder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }

    /// <summary>Import settings of Assets/OpenZ/Models/ZodBoys (see OpenZAlanBradSetup).</summary>
    public sealed class ZodBoysImport : AssetPostprocessor
    {
        static bool Ours(string p, string dir) => p.Replace('\\', '/').StartsWith(dir + "/");

        /// <summary>Brad's file is 2.879 m tall (the second head is squatter); he is imported at his 2.92 m so he stands as tall
        /// on screen as before and the eye line of the front shot still fits.</summary>
        const float BradScale = 2.92f / 2.879f;
        /// <summary>Gain on Brad's base colour in linear light: his red plates measured darker than his reference under the stage
        /// lights (and darker than Alan's texture); 1.2 brings their mean red to the reference. The file itself stays as delivered.</summary>
        const float BradBaseColorGain = 1.2f;
        public override uint GetVersion() => 2;                                                             // raise when a number above changes: the cached imports are redone

        void OnPreprocessModel()
        {
            if (!Ours(assetPath, OpenZAlanBradSetup.Dir) || !assetPath.ToLowerInvariant().EndsWith(".fbx")) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = Path.GetFileNameWithoutExtension(assetPath) == "Brad" ? BradScale : 1f; mi.useFileScale = true;
            mi.importBlendShapes = false; mi.importCameras = false; mi.importLights = false; mi.importVisibility = true;
            mi.isReadable = false; mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;                                 // the prefab assigns the materials
            mi.animationType = ModelImporterAnimationType.Generic; mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.optimizeGameObjects = false;                                                                 // MOUTH / EYES / HAND_* / FUEL_CAN stay transforms
            mi.importAnimation = true; mi.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
        }

        void OnPreprocessAnimation()
        {
            if (!Ours(assetPath, OpenZAlanBradSetup.Dir)) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;
            foreach (var c in clips)
            {
                int bar = c.name.LastIndexOf('|'); if (bar >= 0) c.name = c.name.Substring(bar + 1);
                c.loopTime = OpenZAlanBradSetup.Looping.Contains(c.name); c.loopPose = false;
                c.lockRootRotation = true; c.lockRootHeightY = true; c.lockRootPositionXZ = true;            // root motion baked into the pose
                c.keepOriginalOrientation = true; c.keepOriginalPositionY = true; c.keepOriginalPositionXZ = true;
            }
            mi.clipAnimations = clips;
        }

        void OnPreprocessTexture()
        {
            if (!Ours(assetPath, OpenZAlanBradSetup.TexDir)) return;
            var ti = (TextureImporter)assetImporter;
            string n = Path.GetFileNameWithoutExtension(assetPath);
            ti.maxTextureSize = 2048; ti.mipmapEnabled = true; ti.textureCompression = TextureImporterCompression.CompressedHQ;
            if (n.EndsWith("_Normal")) { ti.textureType = TextureImporterType.NormalMap; ti.sRGBTexture = false; }
            else { ti.textureType = TextureImporterType.Default; ti.sRGBTexture = !n.EndsWith("_MetallicSmoothness"); ti.alphaIsTransparency = false; }
        }

        void OnPostprocessTexture(Texture2D texture)
        {
            if (!Ours(assetPath, OpenZAlanBradSetup.TexDir) || Path.GetFileNameWithoutExtension(assetPath) != "Brad_BaseColor") return;
            var lut = new byte[256];                                                                        // the gain in linear light, on the sRGB values of every mip
            for (int i = 0; i < 256; i++) lut[i] = (byte)Mathf.RoundToInt(Mathf.Clamp01(Mathf.LinearToGammaSpace(Mathf.GammaToLinearSpace(i / 255f) * BradBaseColorGain)) * 255f);
            for (int m = 0; m < texture.mipmapCount; m++)
            {
                var px = texture.GetPixels32(m);
                for (int i = 0; i < px.Length; i++) { px[i].r = lut[px[i].r]; px[i].g = lut[px[i].g]; px[i].b = lut[px[i].b]; }
                texture.SetPixels32(px, m);
            }
            texture.Apply(false);
        }
    }
}
