using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// (humanoid_source/LEGACY_Robot_Clips.fbx, T-pose) are imported as Humanoid on their own avatar, and RobotBS01.controller
    /// (a copy of Robot.controller with those clips and a humanoid upper-body mask) plays them on BS-01. Weapons per type sit
    /// in the RightHandWeapon socket; WeaponHold and RobotRagdoll find the bones through Animator.GetBoneTransform.
    /// </summary>
    public static class OpenZBS01
    {
        const string Dir = "Assets/OpenZ/Models/BS01", Anim = "Assets/OpenZ/Animation", Pref = "Assets/OpenZ/Prefabs", Mat = "Assets/OpenZ/Materials/Units";
        const string Body = Dir + "/BS01_Robot.fbx", Clips = Dir + "/LEGACY_Robot_Clips.fbx", Ctl = Anim + "/RobotBS01.controller";
        public static readonly string[] Types = { "grunt", "psycho", "tough", "sniper", "pyro" };
        static readonly string[] LoopClips = { "idle", "walking", "run_forward", "run_backward", "strafe_left", "strafe_right", "shooting",
                                               "aim_down", "aim_up", "bazooka_idle", "seated_driver", "seated_gunner", "climb_ladder", "jump_air" };

        [MenuItem("OpenZ/BS-01: 1 Import (Humanoid)")]
        public static string Import()
        {
            var log = new List<string>();
            // textures: alpha = team mask, not transparency
            foreach (var p in AssetDatabase.FindAssets("t:Texture2D", new[] { Dir + "/Textures" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                ti.sRGBTexture = true; ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = false; ti.maxTextureSize = 2048;
                ti.SaveAndReimport();
            }
            // weapons: plain meshes, pivot = grip, sockets kept
            foreach (var t in Types)
            {
                var wi = (ModelImporter)AssetImporter.GetAtPath($"{Dir}/BS01_Weapon_{Cap(t)}.fbx");
                wi.animationType = ModelImporterAnimationType.None; wi.importAnimation = false; wi.preserveHierarchy = true;
                wi.materialImportMode = ModelImporterMaterialImportMode.None; wi.importCameras = false; wi.importLights = false;
                wi.SaveAndReimport();
            }
            var ai = (ModelImporter)AssetImporter.GetAtPath(Dir + "/BS01_Robot_Anims.fbx");
            if (ai != null) { ai.materialImportMode = ModelImporterMaterialImportMode.None; ai.animationType = ModelImporterAnimationType.None; ai.importAnimation = false; ai.SaveAndReimport(); }

            // BS-01 body: Humanoid, own avatar in a T-pose
            var bi = (ModelImporter)AssetImporter.GetAtPath(Body);
            bi.preserveHierarchy = true; bi.importAnimation = false; bi.materialImportMode = ModelImporterMaterialImportMode.None;
            bi.importCameras = false; bi.importLights = false;
            bi.animationType = ModelImporterAnimationType.Human; bi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            bi.humanDescription = Describe(AssetDatabase.LoadAssetAtPath<GameObject>(Body), true);
            bi.SaveAndReimport();
            log.Add("BS01 avatar: " + AvatarInfo(Body));

            var ci = (ModelImporter)AssetImporter.GetAtPath(Clips);
            ci.preserveHierarchy = true; ci.materialImportMode = ModelImporterMaterialImportMode.None; ci.importCameras = false; ci.importLights = false;
            ci.animationType = ModelImporterAnimationType.Human; ci.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            ci.humanDescription = Describe(AssetDatabase.LoadAssetAtPath<GameObject>(Clips), true);   // its scene rest is an arms-down frame, not the T-pose
            ci.importAnimation = true;
            ci.SaveAndReimport();
            var list = new List<ModelImporterClipAnimation>();
            foreach (var c in ci.defaultClipAnimations)
            {
                if (!c.takeName.StartsWith("Armature|")) continue;                                     // Key|... = empty shape-key takes
                c.name = c.takeName.Substring("Armature|".Length);
                c.loopTime = LoopClips.Contains(c.name);
                c.lockRootRotation = true; c.keepOriginalOrientation = true;
                c.lockRootHeightY = true; c.keepOriginalPositionY = true; c.heightFromFeet = false;
                c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
                list.Add(c);
            }
            ci.clipAnimations = list.ToArray(); ci.SaveAndReimport();
            log.Add("The legacy clips avatar: " + AvatarInfo(Clips) + $", {list.Count} humanoid clips");
            AssetDatabase.SaveAssets();
            return string.Join("\n", log);
        }

        [MenuItem("OpenZ/BS-01: 2 Controller + prefabs")]
        public static string Build()
        {
            var log = new List<string>();
            var clips = AssetDatabase.LoadAllAssetsAtPath(Clips).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToDictionary(c => c.name);
            if (!clips.ContainsKey("shooting")) return "no humanoid clips, run step 1";

            // aim_hold: the shooting clip's first frame, held (same trick as OpenZRobotAnim)
            string holdPath = Anim + "/Robot/aim_hold_humanoid.anim";
            var hold = new AnimationClip { name = "aim_hold" };
            foreach (var b in AnimationUtility.GetCurveBindings(clips["shooting"]))
                AnimationUtility.SetEditorCurve(hold, b, AnimationCurve.Constant(0f, 1f, AnimationUtility.GetEditorCurve(clips["shooting"], b).Evaluate(0f)));
            var st = AnimationUtility.GetAnimationClipSettings(hold); st.loopTime = true; AnimationUtility.SetAnimationClipSettings(hold, st);
            AssetDatabase.DeleteAsset(holdPath); AssetDatabase.CreateAsset(hold, holdPath);
            clips["aim_hold"] = hold;

            // humanoid upper-body mask: arms, hands, head; legs, hips and root stay on the locomotion
            string maskPath = Anim + "/RobotUpperBodyHumanoid.mask";
            var mask = new AvatarMask { name = "RobotUpperBodyHumanoid" };
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                var part = (AvatarMaskBodyPart)i;
                mask.SetHumanoidBodyPartActive(part, part == AvatarMaskBodyPart.Head || part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm
                    || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers || part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK);
            }
            AssetDatabase.DeleteAsset(maskPath); AssetDatabase.CreateAsset(mask, maskPath);

            // controller: Robot.controller with the humanoid clips (same states, parameters and transitions)
            AssetDatabase.DeleteAsset(Ctl); AssetDatabase.CopyAsset(Anim + "/Robot.controller", Ctl);
            var ctl = AssetDatabase.LoadAssetAtPath<AnimatorController>(Ctl);
            int swapped = 0; var missing = new List<string>();
            var layers = ctl.layers;
            foreach (var l in layers)
            {
                foreach (var s in l.stateMachine.states)
                {
                    if (!(s.state.motion is AnimationClip c)) continue;
                    if (clips.TryGetValue(c.name, out var h)) { s.state.motion = h; swapped++; } else missing.Add(c.name);
                }
                if (l.name == "UpperBody") l.avatarMask = mask;
                if (l.name == "Base Layer") l.iKPass = true;                                            // legs_rootcause: RobotLegIK pins the feet in this layer's IK pass
            }
            ctl.layers = layers;
            EditorUtility.SetDirty(ctl);
            log.Add($"RobotBS01.controller: {swapped} states retargeted" + (missing.Count > 0 ? ", missing " + string.Join(",", missing) : ""));
            log.Add(OpenZBS01Idle.Apply(ctl, clips["idle"]));                                      // take2 legs: calm idle, feet planted

            // materials (README: body TeamLit mode 3, texture alpha = team mask; weapons TeamMode 0)
            var robotPf = AssetDatabase.LoadAssetAtPath<GameObject>(Pref + "/Z_robot.prefab");
            var robotMat = robotPf.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial;
            var bodyMat = MakeMat("M_BS01_Body", robotMat, Dir + "/Textures/BS01_Body_BaseColor.png", m =>
            {
                Set(m, "_TeamMode", 3); Set(m, "_TeamRef", 0.18f); Set(m, "_ShadeMin", 0.6f); Set(m, "_ShadeMax", 1.25f);
                Set(m, "_Degrime", 0.35f); Set(m, "_DegrimeLod", 1.5f);
                Set(m, "_Accent", 0); Set(m, "_CreamToTeam", 0); Set(m, "_Degreen", 0); Set(m, "_TeamLift", 0); Set(m, "_UseVertexColor", 0);
            });

            // height like the current robots (units read at the same size on the map)
            var bodyGo = AssetDatabase.LoadAssetAtPath<GameObject>(Body);
            float oldH = BakedHeight(robotPf), newH = BakedHeight(bodyGo);
            float scale = oldH > 0.5f && newH > 0.5f ? oldH / newH : 1.35f;
            log.Add($"height: old robot {oldH:F2} m, BS-01 {newH:F2} m -> scale {scale:F3}");

            var oldAnim = robotPf.GetComponentInChildren<Animator>();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(Body).OfType<Avatar>().FirstOrDefault();
            var prefabs = new List<GameObject>();
            foreach (var t in Types)
            {
                var root = new GameObject("Z_bs01_" + t);
                try
                {
                    UnityEditorInternal.ComponentUtility.CopyComponent(robotPf.GetComponent<TeamPaint>());
                    UnityEditorInternal.ComponentUtility.PasteComponentAsNew(root);
                    var model = (GameObject)PrefabUtility.InstantiatePrefab(bodyGo);
                    PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    model.name = "Model"; model.transform.SetParent(root.transform, false);
                    model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity; model.transform.localScale = Vector3.one * scale;
                    var an = model.GetComponent<Animator>(); if (!an) an = model.AddComponent<Animator>();
                    an.avatar = avatar; an.runtimeAnimatorController = ctl; an.applyRootMotion = false;
                    an.cullingMode = oldAnim ? oldAnim.cullingMode : AnimatorCullingMode.CullUpdateTransforms;
                    foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    { smr.sharedMaterial = bodyMat; smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; smr.receiveShadows = true; }

                    var socket = model.GetComponentsInChildren<Transform>(true).First(x => x.name == "RightHandWeapon");
                    var wgo = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/BS01_Weapon_{Cap(t)}.fbx"));
                    PrefabUtility.UnpackPrefabInstance(wgo, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    wgo.name = "BS01_Weapon_" + Cap(t); wgo.transform.SetParent(socket, false);
                    wgo.transform.localPosition = Vector3.zero; wgo.transform.localRotation = Quaternion.identity; wgo.transform.localScale = Vector3.one;
                    var wMat = MakeMat("M_BS01_Weapon_" + Cap(t), robotMat, $"{Dir}/Textures/BS01_Weapon_{Cap(t)}_BaseColor.png", m =>
                    {
                        Set(m, "_TeamMode", 0); Set(m, "_Degreen", 0); Set(m, "_SatScale", 1); Set(m, "_Accent", 0); Set(m, "_CreamToTeam", 0); Set(m, "_UseVertexColor", 0);
                        Set(m, "_Degrime", 0.35f); Set(m, "_DegrimeLod", 1.5f);
                    });
                    foreach (var r in wgo.GetComponentsInChildren<Renderer>(true)) { r.sharedMaterial = wMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; }
                    string path = $"{Pref}/Z_bs01_{t}.prefab";
                    prefabs.Add(PrefabUtility.SaveAsPrefabAsset(root, path));
                }
                finally { Object.DestroyImmediate(root); }
            }
            AssetDatabase.SaveAssets();
            log.Add("prefabs: " + string.Join(", ", prefabs.Select(p => p.name)));
            return string.Join("\n", log);
        }

        /// <summary>Scene robots of the five BS-01 types become Z_bs01_* instances (same name, place, team); lasers stay.
        /// ZGame's run-time prefab list gets the new prefabs.</summary>
        [MenuItem("OpenZ/BS-01: 3 Swap scene robots (open level)")]
        public static string SwapScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var root = GameObject.Find("ZodObjects"); if (!root) return "no ZodObjects";
            var pfs = Types.ToDictionary(t => t, t => AssetDatabase.LoadAssetAtPath<GameObject>($"{Pref}/Z_bs01_{t}.prefab"));
            int n = 0;
            foreach (Transform t in root.transform.Cast<Transform>().ToList())
            {
                string type = t.name.Split('_')[0];
                if (!pfs.TryGetValue(type, out var pf) || !pf) continue;
                if (t.GetComponentsInChildren<Transform>(true).Any(x => x.name.StartsWith("BS01_Weapon_"))) continue;   // already swapped
                var go = (GameObject)PrefabUtility.InstantiatePrefab(pf, root.transform);
                go.name = t.name; go.transform.SetPositionAndRotation(t.position, t.rotation); go.transform.localScale = t.localScale;
                go.transform.SetSiblingIndex(t.GetSiblingIndex());
                var oldTp = t.GetComponent<TeamPaint>(); var tp = go.GetComponent<TeamPaint>();
                if (oldTp && tp) { tp.Team = oldTp.Team; EditorUtility.SetDirty(tp); }
                Object.DestroyImmediate(t.gameObject); n++;
            }
            var game = Object.FindFirstObjectByType<ZGame>();
            if (game)
            {
                var list = (game.unitPrefabs ?? new GameObject[0]).Where(g => g && !g.name.StartsWith("Z_bs01_")).ToList();
                list.AddRange(pfs.Values.Where(p => p)); game.unitPrefabs = list.ToArray(); EditorUtility.SetDirty(game);
            }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveOpenScenes(); AssetDatabase.SaveAssets();
            return $"{scene.name}: {n} robots swapped to BS-01";
        }

        // ---------------------------------------------------------------------------------------------------------------

        /// <summary>Bind-pose height of the skinned mesh (prefab-asset renderer bounds are not reliable).</summary>
        static float BakedHeight(GameObject pf)
        {
            var go = Object.Instantiate(pf); float lo = float.MaxValue, hi = float.MinValue;
            try
            {
                foreach (var s in go.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var m = new Mesh(); s.BakeMesh(m, true);
                    foreach (var v in m.vertices) { float y = s.transform.TransformPoint(v).y; lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y); }
                    Object.DestroyImmediate(m);
                }
            }
            finally { Object.DestroyImmediate(go); }
            return hi - lo;
        }

        static string Cap(string t) => char.ToUpper(t[0]) + t.Substring(1);
        static void Set(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }

        static Material MakeMat(string name, Material from, string texPath, System.Action<Material> tune)
        {
            string p = $"{Mat}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(from); AssetDatabase.CreateAsset(m, p); } else m.CopyPropertiesFromMaterial(from);
            m.shader = from.shader; m.name = name;
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath)); m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureOffset("_BaseMap", Vector2.zero);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            tune(m); m.enableInstancing = true; EditorUtility.SetDirty(m); return m;
        }

        static string AvatarInfo(string path)
        {
            var a = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            return a ? $"{a.name} valid={a.isValid} human={a.isHuman}" : "no avatar";
        }

        // Mixamo name -> Unity human bone name
        static readonly (string mix, string human)[] Map =
        {
            ("Hips","Hips"),("Spine","Spine"),("Spine1","Chest"),("Spine2","UpperChest"),("Neck","Neck"),("Head","Head"),
            ("LeftShoulder","LeftShoulder"),("LeftArm","LeftUpperArm"),("LeftForeArm","LeftLowerArm"),("LeftHand","LeftHand"),
            ("RightShoulder","RightShoulder"),("RightArm","RightUpperArm"),("RightForeArm","RightLowerArm"),("RightHand","RightHand"),
            ("LeftUpLeg","LeftUpperLeg"),("LeftLeg","LeftLowerLeg"),("LeftFoot","LeftFoot"),("LeftToeBase","LeftToes"),
            ("RightUpLeg","RightUpperLeg"),("RightLeg","RightLowerLeg"),("RightFoot","RightFoot"),("RightToeBase","RightToes"),
        };
        static readonly string[] Fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };

        /// <summary>Humanoid description from the mixamorig names; tpose: arms swung out horizontally and legs straight in the avatar skeleton
        /// (Unity's "Enforce T-Pose"), the mesh and bind pose are not changed.</summary>
        static HumanDescription Describe(GameObject src, bool tpose)
        {
            var inst = Object.Instantiate(src);
            try
            {
                var all = inst.GetComponentsInChildren<Transform>(true);
                Transform F(string n) => all.FirstOrDefault(t => t.name == "mixamorig" + n);
                if (tpose)
                {
                    var hips = F("Hips");
                    foreach (var side in new[] { "Left", "Right" })
                    {
                        var arm = F(side + "Arm"); if (!arm) continue;
                        var dir = new Vector3(Mathf.Sign(arm.position.x - hips.position.x), 0, 0);
                        foreach (var (b, c) in new[] { ("Arm", "ForeArm"), ("ForeArm", "Hand") })
                        {
                            var bt = F(side + b); var ct = F(side + c); if (!bt || !ct) continue;
                            bt.rotation = Quaternion.FromToRotation(ct.position - bt.position, dir) * bt.rotation;
                        }
                        // legs straight down
                        foreach (var (b, c) in new[] { ("UpLeg", "Leg"), ("Leg", "Foot") })
                        {
                            var bt = F(side + b); var ct = F(side + c); if (!bt || !ct) continue;
                            bt.rotation = Quaternion.FromToRotation(ct.position - bt.position, Vector3.down) * bt.rotation;
                        }
                    }
                }
                var human = new List<HumanBone>();
                foreach (var (mix, h) in Map) if (F(mix)) human.Add(HB(h, "mixamorig" + mix));
                foreach (var side in new[] { "Left", "Right" })
                    for (int f = 0; f < 5; f++)
                    {
                        string mixName = Fingers[f] == "Little" ? "Pinky" : Fingers[f];
                        string[] seg = { "Proximal", "Intermediate", "Distal" };
                        for (int k = 0; k < 3; k++)
                            if (F($"{side}Hand{mixName}{k + 1}")) human.Add(HB($"{side} {Fingers[f]} {seg[k]}", $"mixamorig{side}Hand{mixName}{k + 1}"));
                    }
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

        static HumanBone HB(string human, string bone) { var b = new HumanBone { humanName = human, boneName = bone }; b.limit.useDefaultValues = true; return b; }
    }
}
