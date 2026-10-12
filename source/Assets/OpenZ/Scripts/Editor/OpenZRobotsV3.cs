using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// (refs/robots_v3/{tough,psycho,sniper,pyro,laser}_ref.png) as five new models of the BS-01 family: the SAME skeleton names and
    /// hierarchy as BS01_Robot (SkinRoot / Armature / mixamorig*), rigid mech parts on the bones, the BS-01 face, a flat-colour vinyl
    /// atlas whose alpha is the team mask (TeamLit mode 3, like M_BS01_Kit), separate weapons with named sockets.
    /// Menu "OpenZ/Robots v3: 1 Import + prefabs":
    ///  1. import settings: textures, weapons, bodies as Humanoid (own avatar per type; its T-pose keeps a SLIGHT knee bend, never
    ///  2. materials M_BS01_V3 (copy of M_BS01_Kit on the v3 atlas) and M_BS01_V3_Glow (laser glow, HDR green)
    ///  3. RobotBS01.controller: base-layer states tough_prepare, tough_aim, psycho_aim, psycho_fire and the 8 sniper stance states
    ///     (BS01Stance plays them); override controllers RobotV3_Tough (bazooka_idle -> tough_carry, bazooka_fire -> tough_fire,
    ///     walking -> tough_walk: the waddle), RobotV3_Psycho (shooting -> psycho_walkfire, aim_hold / idle -> psycho_hold) and RobotV3_Pyro
    ///  4. prefabs Z_bs01_{tough,psycho,sniper,pyro,laser}: the Model child replaced by the v3 model (grunt's Model scale), the v1 type kit
    ///     taken off, the v3 weapon under RightHandWeapon, BS01Stance (tough / psycho / sniper), WeaponHold tunables (tough: the tube on
    ///     the RIGHT pad, left arm hanging; laser / pyro: left arm hanging), fuel / power hoses (pyro / laser) as BS01KitCable, a RobotV3 marker.
    ///     Z_bs01_laser is new (a copy of Z_bs01_grunt's root); ZGame / BoardingView / CombatView pick it for lasers when it exists.
    /// Menu "OpenZ/Robots v3: 2 Lasers in this level -> v3 laser" swaps the open level's laser robots (Z_robot instances) to Z_bs01_laser
    /// and adds it to ZGame.unitPrefabs; run it once per level scene. The grunt is untouched (BS-01 + its v1 kit).
    /// </summary>
    public static class OpenZRobotsV3
    {
        const string Dir = "Assets/OpenZ/Models/RobotsV3", Tex = Dir + "/Textures", Pref = "Assets/OpenZ/Prefabs", Mat = "Assets/OpenZ/Materials/Units",
                     Anim = "Assets/OpenZ/Animation", Ctl = Anim + "/RobotBS01.controller", Atlas = Tex + "/BS01V3_Atlas.png",
                     HeadTex = Tex + "/BS01V3_Head_toy.png";
        public static readonly string[] Types = { "tough", "psycho", "sniper", "pyro", "laser" };
        static readonly Dictionary<string, string[]> ClipsOf = new Dictionary<string, string[]>
        {
            { "tough", new[] { "tough_carry", "tough_prepare", "tough_aim", "tough_fire", "tough_walk" } },
            { "psycho", new[] { "psycho_hold", "psycho_aim", "psycho_fire", "psycho_walkfire" } },
            { "sniper", new[] { "sniper_hold", "sniper_walk", "sniper_run", "sniper_strafe_l", "sniper_strafe_r", "sniper_back", "sniper_kneel_down", "sniper_kneel_aim", "sniper_kneel_fire", "sniper_kneel_up", "sniper_prone_down", "sniper_prone_aim", "sniper_prone_fire", "sniper_prone_up" } },
            { "pyro", new[] { "pyro_hold" } }, { "laser", new string[0] },
        };
        static readonly string[] Loops = { "tough_carry", "tough_aim", "tough_walk", "psycho_hold", "psycho_aim", "psycho_fire", "psycho_walkfire", "sniper_kneel_aim", "sniper_prone_aim", "pyro_hold", "sniper_hold", "sniper_walk", "sniper_run", "sniper_strafe_l", "sniper_strafe_r", "sniper_back" };
        static readonly string[] States = { "tough_prepare", "tough_aim", "psycho_aim", "psycho_fire", "sniper_kneel_down", "sniper_kneel_aim", "sniper_kneel_fire", "sniper_kneel_up",
            "sniper_prone_down", "sniper_prone_aim", "sniper_prone_fire", "sniper_prone_up" };
        /// <summary></summary>
        public static bool ToughWaddle = true;
        /// <summary>Knee bend of the avatar T-pose (deg): slight, never exactly straight (legs_rootcause DIAGNOSIS R1).</summary>
        public const float AvatarKneeBend = 6f;

        // OUTER slope of the right pad (r 0.28), 0.20 outside the shoulder joint and 0.38 above it (on its cushion), the pad point 0.30
        // behind the pistol grip, the tube axis 0.27 above the grip; the head stays clear by 0.74; multiplied by the prefab's Model scale
        // for WeaponHold (its tunables are unit-root metres). Carry pitch 8 deg (BS01Stance): nearly level like the reference, the face
        // stays visible from the front-right.
        const float ToughRestUp = 0.291f, ToughRestOut = -0.115f, ToughRestAlong = 0.56f, ToughTubeAxisY = 0.121f, ToughHeadClear = 0.45f, ToughCarryPitch = 8f, ToughCarryLift = 0f, ToughElbowOut = 0.8f;

        [MenuItem("OpenZ/Robots v3: 1 Import + prefabs (tough, psycho, sniper, pyro, laser)")]
        public static string Run()
        {
            var log = new List<string>();
            if (!AssetDatabase.LoadAssetAtPath<AnimatorController>(Ctl)) return "Robots v3: RobotBS01.controller missing (run BS-01 steps 1 + 2 first)";
            var atlas = AtlasMaterial(log); var glow = GlowMaterial(log);
            var head = HeadMaterial(log);
            if (!atlas || !head) return "Robots v3: M_BS01_Kit / M_BS01_Body missing (BS-01 steps 2 + 4 first)";   // head falls back to M_BS01_Body
            ImportSettings(atlas, head, glow, log);
            var clips = LoadClips(log);
            AddStates(clips, log);
            var tough = Override("RobotV3_Tough", clips, ToughWaddle
                ? new Dictionary<string, string> { { "bazooka_idle", "tough_carry" }, { "bazooka_fire", "tough_fire" }, { "walking", "tough_walk" } }
                : new Dictionary<string, string> { { "bazooka_idle", "tough_carry" }, { "bazooka_fire", "tough_fire" } }, log);
            var psycho = Override("RobotV3_Psycho", clips, new Dictionary<string, string> { { "shooting", "psycho_walkfire" }, { "aim_hold", "psycho_hold" }, { "idle", "psycho_hold" } }, log);
            var pyro = Override("RobotV3_Pyro", clips, new Dictionary<string, string> { { "shooting", "pyro_hold" }, { "aim_hold", "pyro_hold" }, { "idle", "pyro_hold" } }, log);
            // robot_polish_v2: the standing sniper (idle, aim hold, standing shots, the upper-body layer over the walk) holds its long
            // own locomotion (the shared legs + sniper_hold's bladed spine) keeps the chest those arms were made for while it walks
            var sniper = Override("RobotV3_Sniper", clips, new Dictionary<string, string> { { "shooting", "sniper_hold" }, { "aim_hold", "sniper_hold" }, { "idle", "sniper_hold" },
                { "walking", "sniper_walk" }, { "run_forward", "sniper_run" }, { "strafe_left", "sniper_strafe_l" }, { "strafe_right", "sniper_strafe_r" }, { "run_backward", "sniper_back" } }, log);
            var ctl = AssetDatabase.LoadAssetAtPath<AnimatorController>(Ctl);
            float scale = GruntModelScale();
            log.Add($"Model scale (Z_bs01_grunt): {scale:F4}");
            foreach (var t in Types)
                log.Add(BuildPrefab(t, t == "tough" ? tough : t == "psycho" ? psycho : t == "pyro" ? (RuntimeAnimatorController)pyro : t == "sniper" ? sniper : ctl, atlas, head, glow, scale));
            AddLaserToOpenScene(log);
            AssetDatabase.SaveAssets();
            var s = string.Join("\n", log); Debug.Log("[OpenZRobotsV3]\n" + s); return s;
        }

        // ------------------------------------------------------------------------------------------------ materials
        static Material AtlasMaterial(List<string> log)
        {
            var src = AssetDatabase.LoadAssetAtPath<Material>($"{Mat}/M_BS01_Kit.mat");
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Atlas);
            if (!src || !tex) { log.Add("M_BS01_V3: " + (!src ? "M_BS01_Kit missing" : Atlas + " missing")); return null; }
            if (AssetImporter.GetAtPath(Atlas) is TextureImporter ti)
            {   // A = team mask (TeamLit mode 3), flat colour cells: bilinear, mips, no crunch
                ti.sRGBTexture = true; ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = false;
                ti.mipmapEnabled = true; ti.filterMode = FilterMode.Bilinear; ti.wrapMode = TextureWrapMode.Clamp; ti.anisoLevel = 8;   // REVIEW: standing rule aniso 8-16
                ti.maxTextureSize = 1024; ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.crunchedCompression = false;   // full source size (1024)
                ti.SaveAndReimport();
            }
            string p = $"{Mat}/M_BS01_V3.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(src); AssetDatabase.CreateAsset(m, p); } else m.CopyPropertiesFromMaterial(src);
            m.shader = src.shader; m.name = "M_BS01_V3";
            m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureOffset("_BaseMap", Vector2.zero);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            m.enableInstancing = true; EditorUtility.SetDirty(m);
            log.Add("M_BS01_V3: M_BS01_Kit's TeamLit settings (mode 3, vertex AO) on BS01V3_Atlas");
            return m;
        }

        /// <summary>M_BS01_V3_Head: the grunt's M_BS01_Body settings on a copy of its toy texture whose HEAD islands had the imported model's orange
        /// rust blotches cleaned to stone grey (tools/head_texture.py); the grunt keeps its own material untouched.</summary>
        static Material HeadMaterial(List<string> log)
        {
            var src = AssetDatabase.LoadAssetAtPath<Material>($"{Mat}/M_BS01_Body.mat");
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(HeadTex);
            if (!src) { log.Add("M_BS01_V3_Head: M_BS01_Body missing"); return null; }
            if (!tex) { log.Add("M_BS01_V3_Head: " + HeadTex + " missing, the heads use M_BS01_Body"); return src; }
            if (AssetImporter.GetAtPath(HeadTex) is TextureImporter ti)
            {
                ti.sRGBTexture = true; ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = false;
                ti.mipmapEnabled = true; ti.maxTextureSize = 2048; ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.anisoLevel = 8;
                ti.SaveAndReimport();
            }
            string p = $"{Mat}/M_BS01_V3_Head.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(src); AssetDatabase.CreateAsset(m, p); } else m.CopyPropertiesFromMaterial(src);
            m.shader = src.shader; m.name = "M_BS01_V3_Head";
            m.SetTexture("_BaseMap", tex);
            m.enableInstancing = true; EditorUtility.SetDirty(m);
            log.Add("M_BS01_V3_Head: M_BS01_Body's settings on BS01V3_Head_toy (clean stone face)");
            return m;
        }

        static Material GlowMaterial(List<string> log)
        {
            string p = $"{Mat}/M_BS01_V3_Glow.mat";
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (!sh) { log.Add("M_BS01_V3_Glow: URP Unlit shader not found"); return null; }
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(sh) { name = "M_BS01_V3_Glow" }; AssetDatabase.CreateAsset(m, p); }
            m.shader = sh;
            m.SetColor("_BaseColor", new Color(0.30f, 3.2f, 0.75f, 1f));           // HDR green over the bloom threshold by day (our laser is green)
            m.enableInstancing = true; EditorUtility.SetDirty(m);
            log.Add("M_BS01_V3_Glow: URP Unlit, HDR green (0.30, 3.2, 0.75)");
            return m;
        }

        // ------------------------------------------------------------------------------------------------ import
        static string Cap(string t) => char.ToUpper(t[0]) + t.Substring(1);

        static void Remap(ModelImporter mi, Material atlas, Material head, Material glow)
        {
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            if (atlas) mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_BS01_V3"), atlas);
            if (head) mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_BS01_Body"), head);
            if (glow) mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_BS01_V3_Glow"), glow);
        }

        static void ImportSettings(Material atlas, Material head, Material glow, List<string> log)
        {
            var bs01 = AssetImporter.GetAtPath("Assets/OpenZ/Models/BS01/BS01_Robot.fbx") as ModelImporter;
            foreach (var t in Types)
            {
                string wp = $"{Dir}/BS01V3_Weapon_{Cap(t)}.fbx", bp = $"{Dir}/BS01V3_{Cap(t)}.fbx";
                if (AssetImporter.GetAtPath(wp) is ModelImporter wi)
                {
                    wi.animationType = ModelImporterAnimationType.None; wi.importAnimation = false; wi.preserveHierarchy = true;
                    wi.importCameras = false; wi.importLights = false; wi.importBlendShapes = false; wi.addCollider = false;
                    wi.importNormals = ModelImporterNormals.Import; Remap(wi, atlas, head, glow);
                    wi.SaveAndReimport();
                }
                else log.Add(t + ": " + wp + " missing");
                if (!(AssetImporter.GetAtPath(bp) is ModelImporter bi)) { log.Add(t + ": " + bp + " missing"); continue; }
                if (bs01)
                {   // same units / axes / normals as BS01_Robot.fbx (same exporter settings)
                    bi.globalScale = bs01.globalScale; bi.useFileScale = bs01.useFileScale; bi.useFileUnits = bs01.useFileUnits; bi.bakeAxisConversion = bs01.bakeAxisConversion;
                    bi.maxBonesPerVertex = bs01.maxBonesPerVertex; bi.skinWeights = bs01.skinWeights;
                }
                bi.importNormals = ModelImporterNormals.Import; bi.weldVertices = true; bi.meshCompression = ModelImporterMeshCompression.Off;
                bi.preserveHierarchy = true; bi.importCameras = false; bi.importLights = false; bi.importBlendShapes = false; bi.importVisibility = false;
                Remap(bi, atlas, head, glow);
                bi.animationType = ModelImporterAnimationType.Human; bi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                bi.SaveAndReimport();                                                          // bring the model in once, then describe it
                bi.humanDescription = DescribeV3(AssetDatabase.LoadAssetAtPath<GameObject>(bp));
                bi.importAnimation = ClipsOf[t].Length > 0;
                bi.SaveAndReimport();
                if (ClipsOf[t].Length > 0)
                {
                    var list = new List<ModelImporterClipAnimation>();
                    foreach (var c in bi.defaultClipAnimations)
                    {
                        string n = c.takeName.Contains("|") ? c.takeName.Substring(c.takeName.LastIndexOf('|') + 1) : c.takeName;
                        if (!ClipsOf[t].Contains(n)) continue;
                        c.name = n; c.loopTime = Loops.Contains(n); c.loopPose = false;
                        c.lockRootRotation = true; c.keepOriginalOrientation = true;          // body pitch (prone) / yaw stay in the pose
                        c.lockRootHeightY = true; c.keepOriginalPositionY = true; c.heightFromFeet = false;   // kneel / prone drop stays in the pose
                        c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;         // recoil step / waddle sway stay in the pose
                        list.Add(c);
                    }
                    bi.clipAnimations = list.ToArray(); bi.SaveAndReimport();
                    log.Add($"{t}: {list.Count}/{ClipsOf[t].Length} clips");
                }
                var av = AssetDatabase.LoadAllAssetsAtPath(bp).OfType<Avatar>().FirstOrDefault();
                log.Add($"{t}: avatar {(av ? av.name : "-")} valid={(av && av.isValid)} human={(av && av.isHuman)}");
            }
        }

        /// <summary>Humanoid description of a v3 body: arms swung out horizontally (T-pose) and legs set straight DOWN (hip over ankle)
        /// with a slight knee bend (AvatarKneeBend, knee to the front): the avatar skeleton only, mesh and bind pose unchanged.</summary>
        public static HumanDescription DescribeV3(GameObject src)
        {
            var inst = Object.Instantiate(src);
            try
            {
                inst.transform.position = Vector3.zero; inst.transform.rotation = Quaternion.identity;
                ToBindPose(inst);                                                        // REVIEW: never the pose the FBX happened to be saved in
                var all = inst.GetComponentsInChildren<Transform>(true);
                Transform F(string n) => all.FirstOrDefault(x => x.name == "mixamorig" + n);
                var hips = F("Hips");
                var head = F("Head"); var front = all.FirstOrDefault(x => x.name == "HeadFront");
                // the model's front (Unity -Z for OpenZ robots): from the head to HeadFront, flattened
                var fwd = front && head ? Vector3.ProjectOnPlane(front.position - head.position, Vector3.up) : Vector3.back;
                fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.back;
                var lateral = Vector3.Cross(Vector3.up, fwd).normalized;              // the knee axis
                foreach (var side in new[] { "Left", "Right" })
                {
                    var arm = F(side + "Arm");
                    if (arm)
                    {
                        var dir = new Vector3(Mathf.Sign(arm.position.x - hips.position.x), 0, 0);
                        foreach (var (b, c) in new[] { ("Arm", "ForeArm"), ("ForeArm", "Hand") })
                        {
                            var bt = F(side + b); var ct = F(side + c); if (!bt || !ct) continue;
                            bt.rotation = Quaternion.FromToRotation(ct.position - bt.position, dir) * bt.rotation;
                        }
                    }
                    var up = F(side + "UpLeg"); var kn = F(side + "Leg"); var ft = F(side + "Foot");
                    if (up && kn && ft)
                    {
                        up.rotation = Quaternion.FromToRotation(kn.position - up.position, Vector3.down) * up.rotation;    // thigh down
                        kn.rotation = Quaternion.FromToRotation(ft.position - kn.position, Vector3.down) * kn.rotation;    // shin down
                        // slight bend: thigh forward by half, shin back by the full angle (knee in front, ankle under the hip)
                        var toFront = Quaternion.AngleAxis(AvatarKneeBend * 0.5f, lateral);
                        if (Vector3.Dot(toFront * Vector3.down, fwd) < 0f) toFront = Quaternion.Inverse(toFront);
                        up.rotation = toFront * up.rotation;
                        kn.rotation = Quaternion.Inverse(toFront) * Quaternion.Inverse(toFront) * kn.rotation;
                    }
                }
                var map = new (string mix, string human)[]
                {
                    ("Hips","Hips"),("Spine","Spine"),("Spine1","Chest"),("Spine2","UpperChest"),("Neck","Neck"),("Head","Head"),
                    ("LeftShoulder","LeftShoulder"),("LeftArm","LeftUpperArm"),("LeftForeArm","LeftLowerArm"),("LeftHand","LeftHand"),
                    ("RightShoulder","RightShoulder"),("RightArm","RightUpperArm"),("RightForeArm","RightLowerArm"),("RightHand","RightHand"),
                    ("LeftUpLeg","LeftUpperLeg"),("LeftLeg","LeftLowerLeg"),("LeftFoot","LeftFoot"),("LeftToeBase","LeftToes"),
                    ("RightUpLeg","RightUpperLeg"),("RightLeg","RightLowerLeg"),("RightFoot","RightFoot"),("RightToeBase","RightToes"),
                };
                var human = new List<HumanBone>();
                foreach (var (mix, h) in map)
                    if (F(mix)) { var hb = new HumanBone { humanName = h, boneName = "mixamorig" + mix }; hb.limit.useDefaultValues = true; human.Add(hb); }
                var skel = all.Select(x => new SkeletonBone { name = x == inst.transform ? src.name : x.name, position = x.localPosition, rotation = x.localRotation, scale = x.localScale }).ToArray();
                return new HumanDescription
                {
                    human = human.ToArray(), skeleton = skel,
                    upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                    armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false,
                };
            }
            finally { Object.DestroyImmediate(inst); }
        }

        /// <summary>
        /// first). The first robots_v3 export wrote the tough / psycho / sniper / pyro FBX default pose from a clip frame (the sniper came
        /// in kneeling: hips 0.64 m, spine 33 deg bent); an avatar T-pose or a prefab pose built from that is bent. The shipped FBXs are
        /// fixed (tools/fbx_rest_default.py); this keeps the menu right for any re-export. Returns the largest correction (m).</summary>
        public static float ToBindPose(GameObject go)
        {
            float moved = 0f;
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh; if (!mesh) continue;
                var bones = smr.bones; var bp = mesh.bindposes; var r2w = smr.transform.localToWorldMatrix;
                var order = Enumerable.Range(0, Mathf.Min(bones.Length, bp.Length)).Where(i => bones[i])
                                      .OrderBy(i => { int d = 0; for (var t = bones[i]; t; t = t.parent) d++; return d; }).ToList();
                var target = order.ToDictionary(i => i, i => r2w * bp[i].inverse);   // world matrices, taken before anything moves
                foreach (var i in order)
                {
                    var m = target[i]; var p = (Vector3)m.GetColumn(3);
                    var q = Quaternion.LookRotation(m.GetColumn(2), m.GetColumn(1));
                    moved = Mathf.Max(moved, (bones[i].position - p).magnitude);
                    bones[i].SetPositionAndRotation(p, q);
                }
            }
            return moved;
        }

        static Dictionary<string, AnimationClip> LoadClips(List<string> log)
        {
            var d = new Dictionary<string, AnimationClip>();
            foreach (var t in Types)
                foreach (var c in AssetDatabase.LoadAllAssetsAtPath($"{Dir}/BS01V3_{Cap(t)}.fbx").OfType<AnimationClip>())
                    if (!c.name.StartsWith("__preview") && ClipsOf[t].Contains(c.name)) d[c.name] = c;
            log.Add($"clip assets: {d.Count} ({string.Join(", ", d.Values.Select(c => $"{c.name} {c.length:F2}s{(c.isLooping ? " loop" : "")}"))})");
            return d;
        }

        // ------------------------------------------------------------------------------------------------ controller
        static void AddStates(Dictionary<string, AnimationClip> clips, List<string> log)
        {
            var ctl = AssetDatabase.LoadAssetAtPath<AnimatorController>(Ctl);
            var layers = ctl.layers; var sm = layers[0].stateMachine;
            var idle = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "idle");
            bool wd = idle ? idle.writeDefaultValues : true;
            int added = 0, updated = 0, i = 0;
            foreach (var n in States)
            {
                if (!clips.TryGetValue(n, out var clip)) continue;
                var st = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == n);
                if (!st) { st = sm.AddState(n, new Vector3(900 + (i % 2) * 260, -200 + (i / 2) * 60, 0)); added++; } else updated++;
                st.motion = clip; st.writeDefaultValues = wd; st.iKOnFeet = idle ? idle.iKOnFeet : false; st.speed = 1f;
                i++;
            }
            ctl.layers = layers; EditorUtility.SetDirty(ctl);
            log.Add($"RobotBS01.controller: {added} states added, {updated} updated (base layer, foot IK like idle)");
        }

        static AnimatorOverrideController Override(string name, Dictionary<string, AnimationClip> clips, Dictionary<string, string> map, List<string> log)
        {
            var ctl = AssetDatabase.LoadAssetAtPath<AnimatorController>(Ctl);
            string p = $"{Anim}/{name}.overrideController";
            var aoc = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(p);
            if (!aoc) { aoc = new AnimatorOverrideController(ctl) { name = name }; AssetDatabase.CreateAsset(aoc, p); }
            aoc.runtimeAnimatorController = ctl;
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            aoc.GetOverrides(pairs);
            var done = new List<string>();
            for (int k = 0; k < pairs.Count; k++)
            {
                var orig = pairs[k].Key; if (!orig) continue;
                string key = map.Keys.FirstOrDefault(m => orig.name == m || orig.name.StartsWith(m + "_"));
                if (key != null && clips.TryGetValue(map[key], out var repl)) { pairs[k] = new KeyValuePair<AnimationClip, AnimationClip>(orig, repl); done.Add($"{orig.name}->{repl.name}"); }
                else pairs[k] = new KeyValuePair<AnimationClip, AnimationClip>(orig, null);
            }
            aoc.ApplyOverrides(pairs); EditorUtility.SetDirty(aoc);
            log.Add($"{name}: {string.Join(", ", done)}");
            return aoc;
        }

        // ------------------------------------------------------------------------------------------------ prefabs
        static float GruntModelScale()
        {
            var g = AssetDatabase.LoadAssetAtPath<GameObject>($"{Pref}/Z_bs01_grunt.prefab");
            var m = g ? g.transform.Find("Model") : null;
            return m ? m.localScale.y : 1.0297053f;
        }

        static string BuildPrefab(string type, RuntimeAnimatorController ctl, Material atlas, Material head, Material glow, float scale)
        {
            string path = $"{Pref}/Z_bs01_{type}.prefab", T = Cap(type);
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/BS01V3_{T}.fbx");
            var wAsset = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/BS01V3_Weapon_{T}.fbx");
            if (!fbx || !wAsset) return $"{type}: v3 FBX missing";
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(path))
            {   // the laser is new: a copy of the grunt's prefab root (TeamPaint etc.), its Model is replaced below
                if (!AssetDatabase.CopyAsset($"{Pref}/Z_bs01_grunt.prefab", path)) return $"{type}: could not create {path}";
            }
            var root = PrefabUtility.LoadPrefabContents(path);
            var log = new List<string>();
            try
            {
                root.name = "Z_bs01_" + type;
                var kit = root.GetComponent<BS01TypeKit>();
                if (kit) { kit.Remove(); Object.DestroyImmediate(kit); log.Add("v1 kit off"); }
                foreach (var c in root.GetComponentsInChildren<BS01KitCable>(true).ToList()) Object.DestroyImmediate(c.gameObject);
                var old = root.transform.Find("Model");
                int layer = old ? old.gameObject.layer : root.layer;
                var oldAnim = old ? old.GetComponent<Animator>() : null;
                var cull = oldAnim ? oldAnim.cullingMode : AnimatorCullingMode.CullUpdateTransforms;
                if (old) Object.DestroyImmediate(old.gameObject);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                model.name = "Model"; model.transform.SetParent(root.transform, false);
                model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity; model.transform.localScale = Vector3.one * scale;
                float bindFix = ToBindPose(model);                                                   // REVIEW: the prefab's resting pose = the bind pose
                if (bindFix > 0.001f) log.Add($"bind pose restored ({bindFix:F3} m)");
                foreach (var tr in model.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = layer;
                var an = model.GetComponent<Animator>(); if (!an) an = model.AddComponent<Animator>();
                an.avatar = AssetDatabase.LoadAllAssetsAtPath($"{Dir}/BS01V3_{T}.fbx").OfType<Avatar>().FirstOrDefault();
                an.runtimeAnimatorController = ctl; an.applyRootMotion = false; an.cullingMode = cull;
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true; FixMats(r, atlas, head, glow); }
                // weapon under the palm socket (WeaponHold / UnitView find BS01_Weapon_* and its sockets)
                var socket = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "RightHandWeapon");
                if (!socket) return $"{type}: no RightHandWeapon";
                var w = (GameObject)PrefabUtility.InstantiatePrefab(wAsset);
                PrefabUtility.UnpackPrefabInstance(w, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                w.name = "BS01_Weapon_" + T; w.transform.SetParent(socket, false);
                w.transform.localPosition = Vector3.zero; w.transform.localRotation = Quaternion.identity; w.transform.localScale = Vector3.one;
                foreach (var tr in w.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = layer;
                foreach (var r in w.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; FixMats(r, atlas, head, glow); }
                log.Add("weapon sockets " + string.Join(",", w.GetComponentsInChildren<Transform>(true).Where(x => x != w.transform && !x.GetComponent<Renderer>()).Select(x => x.name)));
                // marker (UnitView yOffset for the v3 laser, reports)
                var mk = root.GetComponent<RobotV3>(); if (!mk) mk = root.AddComponent<RobotV3>(); mk.type = type;
                // stance (clip picking) and the weapon hold tunables
                var st = root.GetComponent<BS01Stance>();
                if (type == "tough" || type == "psycho" || type == "sniper")
                {
                    if (!st) st = root.AddComponent<BS01Stance>();
                    st.kind = type == "tough" ? BS01Stance.Kind.Tough : type == "psycho" ? BS01Stance.Kind.Psycho : BS01Stance.Kind.Sniper;
                    st.psychoOneHandWalk = false;                                           // v3 psycho: both fists stay on the gatling's rear handles
                }
                else if (st) Object.DestroyImmediate(st);
                var h = root.GetComponent<WeaponHold>();
                if (type == "tough" || type == "laser" || type == "pyro" || type == "psycho" || type == "sniper")
                {
                    if (!h) h = root.AddComponent<WeaponHold>();
                    h.leftHang = type == "tough" || type == "laser";                         // the reference (tough / laser): the left fist hangs free
                    // robot_polish_v1: the pyro holds its flamer TWO-HANDED (the reference; pyro_hold + flamer rebuilt) and the psycho its
                    // gatling with the arms clear of its chest: the left-arm IK keeps the clip's elbow plane (elbows out)
                    h.leftElbowFromClip = type == "pyro" || type == "psycho" || type == "sniper";   // robot_polish_v2: + the sniper
                    if (type == "tough")
                    {
                        h.bazookaRestUp = ToughRestUp * scale; h.bazookaRestOut = ToughRestOut * scale; h.bazookaRestAlong = ToughRestAlong * scale;
                        h.bazookaTubeAxisY = ToughTubeAxisY * scale; h.bazookaHeadClear = ToughHeadClear * scale;
                        h.bazookaChestLevel = 0.85f; h.bazookaChestYaw = 12f; h.maxCarryPitch = 20f; h.carryLift = ToughCarryLift * scale; h.bazookaElbowOut = ToughElbowOut;
                        if (st) st.carryPitch = ToughCarryPitch;
                    }
                    log.Add($"WeaponHold {(h.leftHang ? "leftHang" : "two-handed")}{(h.leftElbowFromClip ? ", left elbow from the clip (robot_polish_v1)" : "")}{(type == "tough" ? $", tube restUp {h.bazookaRestUp:F3} out {h.bazookaRestOut:F3} along {h.bazookaRestAlong:F3} axis {h.bazookaTubeAxisY:F3}" : "")}");
                }
                else if (h) Object.DestroyImmediate(h);
                // hoses (pyro fuel hose, laser power hose): a runtime cable from the back pack to the weapon, like the v1 pyro kit
                if (type == "pyro" || type == "laser")
                {
                    var hs = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "HoseStart");
                    var he = w.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "HoseEnd");
                    var kitMat = AssetDatabase.LoadAssetAtPath<Material>($"{Mat}/M_BS01_Kit.mat");
                    if (hs && he)
                    {
                        var go = new GameObject("BS01KitCable_V3Hose", typeof(MeshFilter), typeof(MeshRenderer)); go.layer = layer;
                        go.transform.SetParent(root.transform, false);
                        // robot_polish_v1: the pyro's fuel hose runs through HoseVia0 (behind the tank), HoseVia1 (back of the right upper arm)
                        // and HoseVia2 (under the right forearm) when the model has them: it rides with the arm, clear of the belly and legs
                        var vias = new[] { "HoseVia0", "HoseVia1", "HoseVia2" }.Select(n => model.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == n)).Where(x => x).ToList();
                        var cb = go.AddComponent<BS01KitCable>(); cb.style = BS01KitCable.Style.Hose; cb.points = new[] { hs }.Concat(vias).Concat(new[] { he }).ToArray();
                        cb.sag = type == "pyro" ? (vias.Count > 0 ? 0.06f : 0.16f) : 0.12f; cb.radius = type == "pyro" ? 0.035f : 0.038f; cb.maxSpan = 2.4f;
                        var mr = go.GetComponent<MeshRenderer>(); if (kitMat) mr.sharedMaterial = kitMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                        log.Add("hose cable");
                    }
                    else log.Add("hose: sockets missing");
                }
                var tp = root.GetComponent<TeamPaint>(); if (tp) tp.Apply();
                PrefabUtility.SaveAsPrefabAsset(root, path);
                int tris = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Sum(s => s.sharedMesh ? (int)(s.sharedMesh.triangles.Length / 3) : 0)
                         + w.GetComponentsInChildren<MeshFilter>(true).Sum(m => m.sharedMesh ? (int)(m.sharedMesh.triangles.Length / 3) : 0);
                log.Add($"{tris} tris, Model scale {scale:F4}, controller {(ctl ? ctl.name : "-")}");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return $"{type}: " + string.Join("; ", log);
        }

        /// <summary>Material slots by the FBX material names (the importer remaps them; this also covers a remap Unity skipped).</summary>
        static void FixMats(Renderer r, Material atlas, Material head, Material glow)
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                string n = mats[i] ? mats[i].name : "";
                if (n.StartsWith("M_BS01_V3_Glow")) mats[i] = glow ? glow : mats[i];
                else if (n.StartsWith("M_BS01_V3_Head") || n.StartsWith("M_BS01_Body")) mats[i] = head;
                else if (n.StartsWith("M_BS01_V3")) mats[i] = atlas;
                else if (!mats[i]) mats[i] = atlas;
            }
            r.sharedMaterials = mats;
        }

        // ------------------------------------------------------------------------------------------------ lasers in the level
        static void AddLaserToOpenScene(List<string> log)
        {
            var game = Object.FindFirstObjectByType<ZGame>();
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{Pref}/Z_bs01_laser.prefab");
            if (!game || !pf) return;
            var list = (game.unitPrefabs ?? new GameObject[0]).Where(g => g && g.name != "Z_bs01_laser").ToList();
            list.Add(pf); game.unitPrefabs = list.ToArray(); EditorUtility.SetDirty(game);
            EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
            log.Add($"{game.gameObject.scene.name}: Z_bs01_laser added to ZGame.unitPrefabs (run menu 2 in each level to swap its placed lasers)");
        }

        [MenuItem("OpenZ/Robots v3: 2 Lasers in this level -> v3 laser (open level scene)")]
        public static string SwapLasers()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var root = GameObject.Find("ZodObjects"); if (!root) { Debug.LogWarning("[OpenZRobotsV3] menu 2: no ZodObjects in the open scene (open L02 or L17)"); return "no ZodObjects"; }
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{Pref}/Z_bs01_laser.prefab"); if (!pf) { Debug.LogWarning("[OpenZRobotsV3] menu 2: Z_bs01_laser missing (run menu 1)"); return "Z_bs01_laser missing (run menu 1)"; }
            int n = 0;
            foreach (Transform t in root.transform.Cast<Transform>().ToList())
            {
                if (t.name.Split('_')[0] != "laser" || t.GetComponent<RobotV3>()) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(pf, root.transform);
                go.name = t.name; go.transform.SetPositionAndRotation(t.position, t.rotation); go.transform.localScale = t.localScale;
                go.transform.SetSiblingIndex(t.GetSiblingIndex());
                var oldTp = t.GetComponent<TeamPaint>(); var tp = go.GetComponent<TeamPaint>();
                if (oldTp && tp) { tp.Team = oldTp.Team; EditorUtility.SetDirty(tp); }
                Object.DestroyImmediate(t.gameObject); n++;
            }
            var log = new List<string>(); AddLaserToOpenScene(log);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveOpenScenes(); AssetDatabase.SaveAssets();
            var msg = $"{scene.name}: {n} lasers swapped to the v3 laser. " + string.Join(" ", log);
            Debug.Log("[OpenZRobotsV3] " + msg);
            return msg;
        }

        // ------------------------------------------------------------------------------------------------ report
        [MenuItem("OpenZ/Robots v3: 3 Report")]
        public static string Report()
        {
            var log = new List<string>();
            foreach (var t in new[] { "grunt" }.Concat(Types))
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{Pref}/Z_bs01_{t}.prefab"); if (!pf) { log.Add(t + ": missing"); continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(pf);
                try
                {
                    var an = go.GetComponentInChildren<Animator>(true); var root = go.transform;
                    string S(string n) { var x = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(y => y.name == n); return x ? $"{n} {root.InverseTransformPoint(x.position).ToString("F3")}" : null; }
                    var socks = new[] { "BazookaMuzzle", "BazookaBackBlast", "RifleMuzzle", "Muzzle", "MG_FeedPort", "EjectPort", "PilotLight", "HoseStart", "HoseEnd", "Foregrip" }.Select(S).Where(x => x != null);
                    var h = go.GetComponent<WeaponHold>(); var st = go.GetComponent<BS01Stance>();
                    log.Add($"{t}: v3={(go.GetComponent<RobotV3>() ? "yes" : "no")}, avatar {(an && an.avatar ? an.avatar.name + (an.avatar.isHuman ? " human" : "") : "-")}, " +
                            $"controller {(an && an.runtimeAnimatorController ? an.runtimeAnimatorController.name : "-")}, stance {(st ? st.kind.ToString() : "-")}, " +
                            $"hold {(h ? (h.leftHang ? "leftHang" : h.leftElbowFromClip ? "two-handed, elbow from clip" : "prefab") : "runtime")}; rest sockets (unit space): {string.Join(" | ", socks)}");
                }
                finally { Object.DestroyImmediate(go); }
            }
            var ctl = AssetDatabase.LoadAssetAtPath<AnimatorController>(Ctl);
            if (ctl) log.Add("RobotBS01 base states present: " + string.Join(",", States.Where(n => ctl.layers[0].stateMachine.states.Any(s => s.state.name == n))));
            var s_ = string.Join("\n", log); Debug.Log("[OpenZRobotsV3 report]\n" + s_); return s_;
        }

        /// <summary>Play mode: grunt, psycho, tough, sniper, pyro, laser in turn, 15 s each, near the camera, each attacking a harmless
        /// </summary>
        [MenuItem("OpenZ/Robots v3: 4 Robot Types Show (Play mode, day)")]
        static void ShowDay() => StartShow(false);

        [MenuItem("OpenZ/Robots v3: 5 Robot Types Show (Play mode, night)")]
        static void ShowNight() => StartShow(true);

        static void StartShow(bool night)
        {
            if (!Application.isPlaying) { Debug.LogWarning("[RobotTypesShow] enter Play mode on L02 or L17 first (and close the level-start briefing)"); return; }
            var s = RobotTypesShow.Begin(night);
            if (s) Debug.Log($"[RobotTypesShow] started ({(night ? "night" : "day")}): {RobotTypesShow.Order.Length} types x {RobotTypesShow.SegmentSeconds:F0} s near the camera focus; " +
                             "report in work/records/qc/robot_types_show_*.md");
        }

        // ------------------------------------------------------------------------------------------------ revert
        [MenuItem("OpenZ/Robots v3: 9 Revert to the BS-01 v1 robots")]
        public static string Revert()
        {
            // BS-01 steps 2 + 4 rebuild every Z_bs01_* prefab of the five v1 types from BS01_Robot + the v1 kits (same asset paths);
            // the v3 laser prefab is deleted (ZGame / BoardingView / CombatView fall back to Z_robot); placed v3 lasers go back via
            // the scene snapshot / menu "BS-01: 3 Swap scene robots" does not touch lasers, so re-open the level from before menu 2
            var a = OpenZBS01.Build(); var b = OpenZBS01Types.BakeAll();
            bool del = AssetDatabase.DeleteAsset($"{Pref}/Z_bs01_laser.prefab");
            var ctl = AssetDatabase.LoadAssetAtPath<AnimatorController>(Ctl);
            int n = 0;
            if (ctl)
            {
                var layers = ctl.layers; var sm = layers[0].stateMachine;
                foreach (var cs in sm.states.ToArray()) if (States.Contains(cs.state.name)) { sm.RemoveState(cs.state); n++; }
                ctl.layers = layers; EditorUtility.SetDirty(ctl);
            }
            foreach (var o in new[] { "RobotV3_Tough", "RobotV3_Psycho", "RobotV3_Pyro", "RobotV3_Sniper" }) AssetDatabase.DeleteAsset($"{Anim}/{o}.overrideController");
            AssetDatabase.SaveAssets();
            return $"reverted: {a}\n{b}\nZ_bs01_laser deleted: {del}; {n} v3 states removed";
        }
    }
}
