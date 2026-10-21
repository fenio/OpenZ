using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// DV108 hero squads (Sim/ZHeroSquad.cs, View/OzHeroSquadView.cs): the ALAN &amp; BRAD unit models, built from the REAL Alan and Brad
    /// of the briefing and ending scenes (Assets/OpenZ/Models/ZodBoys). Menu OpenZ/Hero Squad (editor, outside Play):
    ///  1 Build hero prefabs: copies Alan.fbx / Brad.fbx to Models/HeroSquad/Hero_*.fbx (the briefing's own models and their Generic
    ///    clips stay untouched) and imports the copies as Humanoid (the bone map below; the avatar T-pose: arms out level, legs straight
    ///    down with the slight knee bend of the v3 robots, OpenZRobotsV3.AvatarKneeBend), so the grunt's own unit animations
    ///    (RobotBS01.controller: walk, run, aim, fire, hit, fall, the antics) play on them through the humanoid retarget, as on the
    ///    five v3 robots. Then Resources/OpenZ/HeroSquad/Z_hero_brad and Z_hero_alan: a copy of Z_bs01_grunt's root (TeamPaint and
    ///    the rest), its Model replaced by the hero model at its height (Alan = a grunt's height x AlanToGrunt: a soldier, never
    ///    smaller; Brad = Alan / AlanToBrad, about 5 % taller), turned to face -Z like every unit model, the grunt's rifle moved to a
    ///    RightHandWeapon socket on the hero's right hand, LeftHandGrip and HeadFront likewise (each socket keeps its pose relative to
    ///    its bone through the two avatars' T-poses, scaled by the body size); OzHeroLook with the red set (the robots' own paint) and
    ///    the blue set (Models/HeroSquad/Hero_*_BaseColor_Blue.png: the red paint turned to the blue team's).
    ///  2 Render portraits (KC4b: three pictures, each red and blue: the pair, Alan alone, Brad alone; copies for the owner in
    ///    (a long-lens bust framing, warm key front-left, cool rim behind right, soft fill, the deep blue backdrop): Brad on the right
    ///    and a little in front, Alan on the left, BOTH holding the grunt's rifle; the pose and the two turns are picked by measurement
    ///    (PoseTries x TurnTries rendered small: each rifle seen along its length, not end-on toward the camera, no rifle over a face,
    ///    nothing cut by the border; menu 2 prints the table and the pick), the rifle in the game's khaki (its own texture with the
    ///    orange paint turned khaki, the picture only; the rifle's mean colour is printed and judged: nothing orange in front of
    ///    them). Written to Resources/OpenZ/UI/PortraitsHero/portrait_{alanbrad,alan,brad}_{red,blue}.png.
    ///  3 Check: PASS / FAIL lines (avatars valid and human, the briefing's models still Generic, prefabs, heights and their ratio,
    ///    sockets, material sets, the six portraits, the gun sounds, the level 16 entry).
    ///  4 Icons for the owner: checks for_owner/hero_icons (the six pictures + the three unit panel pictures from Play) and writes
    ///    icons_DONE.txt.
    ///  Play: report (the hero views, the loungers, the shots) and select Brad (the loungers go away).
    /// </summary>
    public static partial class OzHeroSquadSetup                                                  // KH: + Editor/OzHeroSquadSetup.Kh.cs
    {
        const string SrcDir = "Assets/OpenZ/Models/ZodBoys", Dir = "Assets/OpenZ/Models/HeroSquad", Res = "Assets/OpenZ/Resources/OpenZ/HeroSquad",
                     PortraitDir = "Assets/OpenZ/Resources/OpenZ/UI/PortraitsHero", GruntPrefab = "Assets/OpenZ/Prefabs/Z_bs01_grunt.prefab",
                     GruntFbx = "Assets/OpenZ/Models/BS01/BS01_Robot.fbx", Ctl = "Assets/OpenZ/Animation/RobotBS01.controller",
                     PortraitRef = "Assets/OpenZ/Resources/OpenZ/UI/PortraitsBS01/portrait_grunt_red.png";
        /// <summary>Alan's height as a multiple of the grunt's (KC4b: heroes, clearly bigger than a grunt; OzHeroSquadView.AlanToGrunt,
        /// the number the map's pick box, frame and health bar use too).</summary>
        public static float AlanToGrunt => OzHeroSquadView.AlanToGrunt;
        /// <summary>Alan's height as a share of Brad's (Alan about 5 % under Brad).</summary>
        public static float AlanToBrad => OzHeroSquadView.AlanToBrad;
        /// <summary>The accepted range of Alan's height as a multiple of a grunt's (menu 3).</summary>
        public const float AlanToGruntMin = 1.25f, AlanToGruntMax = 1.35f;
        static readonly (string member, string model)[] Members = { ("brad", "Brad"), ("alan", "Alan") };
        /// <summary>Alan / Brad bone -> humanoid bone (both rigs use the same names; along their real parent chain, a child never above its parent).</summary>
        static readonly (string bone, string human)[] Map =
        {
            ("Hips", "Hips"), ("Spine02", "Spine"), ("Spine01", "Chest"), ("Spine", "UpperChest"), ("neck", "Neck"), ("Head", "Head"),   // the rigs' real chain: Hips > Spine02 > Spine01 > Spine
            ("LeftShoulder", "LeftShoulder"), ("LeftArm", "LeftUpperArm"), ("LeftForeArm", "LeftLowerArm"), ("LeftHand", "LeftHand"),
            ("RightShoulder", "RightShoulder"), ("RightArm", "RightUpperArm"), ("RightForeArm", "RightLowerArm"), ("RightHand", "RightHand"),
            ("LeftUpLeg", "LeftUpperLeg"), ("LeftLeg", "LeftLowerLeg"), ("LeftFoot", "LeftFoot"), ("LeftToeBase", "LeftToes"),
            ("RightUpLeg", "RightUpperLeg"), ("RightLeg", "RightLowerLeg"), ("RightFoot", "RightFoot"), ("RightToeBase", "RightToes"),
        };
        static readonly string[] Sockets = { "RightHandWeapon", "LeftHandGrip", "HeadFront" };

        static string Fbx(string model) => $"{Dir}/Hero_{model}.fbx";
        static string PrefabPath(string member) => $"{Res}/Z_hero_{member}.prefab";

        // ------------------------------------------------------------------------------------------------ 1 build
        [MenuItem("OpenZ/Hero Squad/1 Build hero prefabs (Alan, Brad)", false, 10)]
        static void MenuBuild() => Build();

        public static string Build()
        {
            if (Application.isPlaying) return Say("leave Play mode first");
            var log = new List<string>();
            Folder(Res); Folder(PortraitDir);
            var ctl = AssetDatabase.LoadAssetAtPath<AnimatorController>(Ctl);
            if (!ctl) return Say(Ctl + " missing");
            float gruntH = GruntHeight(log);
            if (gruntH <= 0) return Say("Z_bs01_grunt: no height measured (" + string.Join("; ", log) + ")");
            float alanH = gruntH * AlanToGrunt, bradH = alanH / AlanToBrad;
            log.Add($"heights: grunt {gruntH:F3} m -> Alan {alanH:F3} m, Brad {bradH:F3} m (Alan = {AlanToBrad * 100:0} % of Brad)");
            foreach (var (member, model) in Members)
            {
                string fbx = Fbx(model);
                if (!AssetDatabase.LoadAssetAtPath<GameObject>(fbx))
                {
                    if (!AssetDatabase.CopyAsset($"{SrcDir}/{model}.fbx", fbx)) { log.Add($"{model}: could not copy {SrcDir}/{model}.fbx"); continue; }
                    log.Add($"{model}: {fbx} copied from the briefing's model (the original keeps its import)");
                }
                if (!(AssetImporter.GetAtPath(fbx) is ModelImporter mi)) { log.Add($"{model}: no model importer"); continue; }
                mi.animationType = ModelImporterAnimationType.Human; mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.importAnimation = false; mi.materialImportMode = ModelImporterMaterialImportMode.None;
                mi.SaveAndReimport();
                mi.humanDescription = Describe(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
                mi.SaveAndReimport();
                var av = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Avatar>().FirstOrDefault();
                log.Add($"{model}: avatar {(av ? av.name : "-")} valid={(av && av.isValid)} human={(av && av.isHuman)}");
                if (!av || !av.isValid || !av.isHuman) continue;
                var mats = Materials(model, log);
                log.Add(BuildPrefab(member, model, av, ctl, member == "brad" ? bradH : alanH, gruntH, mats));
            }
            AssetDatabase.SaveAssets();
            return Say(string.Join("\n", log));
        }

        /// <summary>The humanoid description of a hero body: the bone map above; the avatar skeleton at the bind pose with the arms
        /// swung out level (T-pose) and the legs straight down with a slight knee bend (the v3 robots' rule); mesh and bind pose unchanged.</summary>
        public static HumanDescription Describe(GameObject src)
        {
            var inst = Object.Instantiate(src);
            try
            {
                inst.transform.position = Vector3.zero; inst.transform.rotation = Quaternion.identity;
                OpenZRobotsV3.ToBindPose(inst);
                var all = inst.GetComponentsInChildren<Transform>(true);
                Transform F(string n) => all.FirstOrDefault(x => x.name == n);
                var hips = F("Hips"); var head = F("Head"); var front = F("headfront");
                var fwd = front && head ? Vector3.ProjectOnPlane(front.position - head.position, Vector3.up) : Vector3.back;
                fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.back;
                var lateral = Vector3.Cross(Vector3.up, fwd).normalized;
                var outward = Vector3.Cross(fwd, Vector3.up).normalized;                                  // the body's left (+) / right (-) direction
                var kh = new KhLegs();                                                                    // KH (items 184, 185): the leg record
                foreach (var side in new[] { "Left", "Right" })
                {
                    var arm = F(side + "Arm");
                    if (arm && hips)
                    {
                        float sgn = Mathf.Sign(Vector3.Dot(arm.position - hips.position, outward)); if (sgn == 0f) sgn = side == "Left" ? 1f : -1f;
                        var dir = outward * sgn;
                        foreach (var (b, c) in new[] { ("Arm", "ForeArm"), ("ForeArm", "Hand") })
                        {
                            var bt = F(side + b); var ct = F(side + c); if (!bt || !ct) continue;
                            bt.rotation = Quaternion.FromToRotation(ct.position - bt.position, dir) * bt.rotation;
                        }
                    }
                    var up = F(side + "UpLeg"); var kn = F(side + "Leg"); var ft = F(side + "Foot");
                    if (up && kn && ft && TrueTPose) StraightLeg(side, up, kn, ft, fwd, lateral, kh);           // KH: a true T-pose leg (knee hinge across the body, foot flat)
                    else if (up && kn && ft)
                    {
                        up.rotation = Quaternion.FromToRotation(kn.position - up.position, Vector3.down) * up.rotation;
                        kn.rotation = Quaternion.FromToRotation(ft.position - kn.position, Vector3.down) * kn.rotation;
                        var toFront = Quaternion.AngleAxis(OpenZRobotsV3.AvatarKneeBend * 0.5f, lateral);
                        if (Vector3.Dot(toFront * Vector3.down, fwd) < 0f) toFront = Quaternion.Inverse(toFront);
                        up.rotation = toFront * up.rotation;
                        kn.rotation = Quaternion.Inverse(toFront) * Quaternion.Inverse(toFront) * kn.rotation;
                    }
                }
                if (TrueTPose) { KhGround(hips, kh); LegLog[src.name.Replace("Hero_", "")] = kh; }       // KH: the soles back on the ground
                var human = new List<HumanBone>();
                foreach (var (bone, h) in Map)
                    if (F(bone)) { var hb = new HumanBone { humanName = h, boneName = bone }; hb.limit.useDefaultValues = true; human.Add(hb); }
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

        /// <summary>The grunt prefab's body height (m, skinned renderers at the bind pose, the rifle left out).</summary>
        static float GruntHeight(List<string> log)
        {
            var g = AssetDatabase.LoadAssetAtPath<GameObject>(GruntPrefab); if (!g) { log.Add(GruntPrefab + " missing"); return 0f; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(g);
            try
            {
                inst.transform.position = Vector3.zero; inst.transform.rotation = Quaternion.identity;
                var model = inst.transform.Find("Model"); if (model) OpenZRobotsV3.ToBindPose(model.gameObject);
                return BodyHeight(model ? model.gameObject : inst);
            }
            finally { Object.DestroyImmediate(inst); }
        }

        /// <summary>Top of the skinned body above its lowest point (m); weapons, kit parts and the briefing's cans left out.</summary>
        static float BodyHeight(GameObject go)
        {
            var rs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                       .Where(r => !r.name.StartsWith("BS01_Weapon") && !r.name.Contains("FUEL_CAN") && !r.name.StartsWith("BS01Kit") && !r.name.StartsWith("BS01_Kit")).ToList();
            if (rs.Count == 0) return 0f;
            foreach (var r in rs) r.updateWhenOffscreen = true;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            return b.size.y;
        }

        const string GruntBodyMat = "Assets/OpenZ/Materials/Units/M_BS01_Body.mat";
        /// <summary>KH (item 183): ON. On the map each hero body wears the grunts' unit material (M_BS01_Body: TeamLit, team mode 3) with
        /// his own albedo Models/HeroSquad/Hero_*_Unit_BaseColor.png (the red panels made grey, their mask in alpha), one material for
        /// both teams (KhUnitMaterial, Editor/OzHeroSquadSetup.Kh.cs). The first try (run 15: the red texture with no mask) came out
        /// pink; without the unit albedo the cinematic set stays on the map. false = the cinematic set.</summary>
        public static bool MapUsesUnitMaterial = true;
        static (Material[] red, Material[] blue) Materials(string model, List<string> log)
        {
            var body = AssetDatabase.LoadAssetAtPath<Material>($"{SrcDir}/Materials/M_{model}_Body.mat");
            var eyes = AssetDatabase.LoadAssetAtPath<Material>($"{SrcDir}/Materials/M_{model}_Eyes.mat");
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/Hero_{model}_BaseColor_Blue.png");
            if (!body || !eyes) { log.Add($"{model}: M_{model}_Body / M_{model}_Eyes missing"); return (null, null); }
            Material Blue(Material src, string name)
            {
                string p = $"{Dir}/{name}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (!m) { m = new Material(src) { name = name }; AssetDatabase.CreateAsset(m, p); } else { m.shader = src.shader; m.CopyPropertiesFromMaterial(src); }
                if (tex) { if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex); if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex); }
                EditorUtility.SetDirty(m);
                return m;
            }
            if (!tex) log.Add($"{model}: {Dir}/Hero_{model}_BaseColor_Blue.png missing: the blue set shows the red paint");
            // (OpenZ/TeamLit with M_BS01_Body's lighting, rim and night look), the hero's own paint texture per team (no team mask:
            // the red and blue textures carry the paint). The briefing's cinematic body material stays the briefing's.
            var grunt = AssetDatabase.LoadAssetAtPath<Material>(GruntBodyMat);
            if (MapUsesUnitMaterial && grunt && grunt.HasProperty("_TeamMode"))
            {   // KH (item 183): the grunt's unit material with the hero's masked albedo, one material for both teams (TeamPaint colours it)
                var unit = KhUnitMaterial(model, grunt, log);
                if (unit) return (new[] { unit, eyes }, new[] { unit, Blue(eyes, $"M_Hero_{model}_Eyes_Blue") });
                return (new[] { body, eyes }, new[] { Blue(body, $"M_Hero_{model}_Body_Blue"), Blue(eyes, $"M_Hero_{model}_Eyes_Blue") });   // no unit albedo: the cinematic set (never the unmasked try)
            }
            Material Map(Texture t, string name)
            {
                string p = $"{Dir}/{name}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (!m) { m = new Material(grunt) { name = name }; AssetDatabase.CreateAsset(m, p); } else { m.shader = grunt.shader; m.CopyPropertiesFromMaterial(grunt); }
                m.SetTexture("_BaseMap", t); m.SetColor("_BaseColor", Color.white); m.SetFloat("_TeamMode", 0f);
                EditorUtility.SetDirty(m);
                return m;
            }
            if (MapUsesUnitMaterial && grunt && grunt.HasProperty("_TeamMode"))
            {
                var redTex = body.HasProperty("_BaseMap") ? body.GetTexture("_BaseMap") : body.mainTexture;
                log.Add($"{model}: map body material = the grunts' TeamLit look with the hero's own paint (red {(redTex ? redTex.name : "-")}, blue {(tex ? tex.name : "-")})");
                return (new[] { Map(redTex, $"M_Hero_{model}_Body_Map_Red"), eyes }, new[] { Map(tex ? (Texture)tex : redTex, $"M_Hero_{model}_Body_Map_Blue"), Blue(eyes, $"M_Hero_{model}_Eyes_Blue") });
            }
            log.Add($"{model}: {GruntBodyMat} missing: the cinematic body material on the map");
            return (new[] { body, eyes }, new[] { Blue(body, $"M_Hero_{model}_Body_Blue"), Blue(eyes, $"M_Hero_{model}_Eyes_Blue") });
        }

        static string BuildPrefab(string member, string model, Avatar av, AnimatorController ctl, float height, float gruntH, (Material[] red, Material[] blue) mats)
        {
            string path = PrefabPath(member);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) && !AssetDatabase.DeleteAsset(path)) return $"{member}: could not replace {path}";   // KC4b: always a fresh grunt copy (re-run safe)
            if (!AssetDatabase.CopyAsset(GruntPrefab, path)) return $"{member}: could not create {path}";
            var root = PrefabUtility.LoadPrefabContents(path);
            var log = new List<string>();
            var tmp = new List<GameObject>();
            try
            {
                root.name = "Z_hero_" + member;
                var kit = root.GetComponent<BS01TypeKit>();
                if (kit) { kit.Remove(); Object.DestroyImmediate(kit); log.Add("grunt kit off"); }
                foreach (var c in root.GetComponentsInChildren<BS01KitCable>(true).ToList()) Object.DestroyImmediate(c.gameObject);
                var old = root.transform.Find("Model");
                if (!old) return $"{member}: the grunt copy has no Model (run the menu on a fresh copy: delete {path} first)";
                int layer = old.gameObject.layer;
                var oldAnim = old.GetComponent<Animator>();
                var cull = oldAnim ? oldAnim.cullingMode : AnimatorCullingMode.CullUpdateTransforms;
                var gruntAv = oldAnim ? oldAnim.avatar : null;
                RuntimeAnimatorController gruntCtl = oldAnim && oldAnim.runtimeAnimatorController ? oldAnim.runtimeAnimatorController : ctl;
                // the grunt's sockets: their humanoid parent bone and their pose relative to it
                OpenZRobotsV3.ToBindPose(old.gameObject);
                var gruntHuman = HumanMap(gruntAv);
                var gruntT = TPoseWorld(GruntFbx, gruntHuman.Values, tmp);
                var socketInfo = new List<(string name, string human, Vector3 lp, Quaternion lr, Transform t)>();
                foreach (var sn in Sockets)
                {
                    var s = old.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == sn);
                    if (!s) { log.Add("no " + sn + " on the grunt"); continue; }
                    var p = s.parent; string h = null;
                    while (p && p != old) { var nm = p.name; h = gruntHuman.FirstOrDefault(kv => kv.Value == nm).Key; if (h != null) break; p = p.parent; }
                    if (h == null || !p) { log.Add(sn + ": no humanoid parent bone"); continue; }
                    var lp = Quaternion.Inverse(p.rotation) * (s.position - p.position);                // world metres in the bone's frame
                    var lr = Quaternion.Inverse(p.rotation) * s.rotation;
                    socketInfo.Add((sn, h, lp, lr, s));
                }
                // the hero model in place of the grunt's
                var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx(model));
                var hm = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
                PrefabUtility.UnpackPrefabInstance(hm, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                hm.name = "Model"; hm.transform.SetParent(root.transform, false);
                hm.transform.localPosition = Vector3.zero; hm.transform.localRotation = Quaternion.identity; hm.transform.localScale = Vector3.one;
                OpenZRobotsV3.ToBindPose(hm);
                var hAll = hm.GetComponentsInChildren<Transform>(true);
                var head = hAll.FirstOrDefault(x => x.name == "Head"); var hf = hAll.FirstOrDefault(x => x.name == "headfront");
                if (head && hf)
                {   // face -Z like every unit model (from the head to its 'headfront' node)
                    var f = Vector3.ProjectOnPlane(hf.position - head.position, Vector3.up);
                    if (f.sqrMagnitude > 1e-6f) hm.transform.localRotation = Quaternion.FromToRotation(f.normalized, Vector3.back);
                }
                float h0 = KhBaseHeight(hm, model, av, gruntCtl, height, gruntH, log);              // KH (item 183): sized in the idle the player sees, old -> new scale in the log
                if (h0 <= 0.01f) return $"{member}: the hero model has no skinned body";
                float scale = height / h0;
                hm.transform.localScale = Vector3.one * scale;
                log.Add($"height {height:F3} m (model {h0:F3} m x {scale:F4})");
                foreach (var tr in hm.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = layer;
                foreach (var c in hAll.Where(x => x && (x.name == "FUEL_CAN" || x.name == "FUEL_CAN_R"))) c.gameObject.SetActive(false);   // the briefing's cans stay off (the antics bring their own)
                var an = hm.GetComponent<Animator>(); if (!an) an = hm.AddComponent<Animator>();
                an.avatar = av; an.runtimeAnimatorController = KhController(gruntCtl, log); an.applyRootMotion = false; an.cullingMode = cull;   // KH: the grunt's own (HeroFootIk, as shipped) or the copy without foot IK (A/B)
                var renderers = new List<Renderer>();
                foreach (var r in hm.GetComponentsInChildren<Renderer>(true))
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true;
                    if (mats.red == null) continue;
                    var ms = r.sharedMaterials;
                    bool eye = r.name.ToUpperInvariant().Contains("EYE");
                    for (int i = 0; i < ms.Length; i++) ms[i] = eye ? mats.red[1] : mats.red[0];
                    r.sharedMaterials = ms; renderers.Add(r);
                }
                // the sockets on the hero's bones: the same pose relative to the bone, carried through the two avatars' T-poses
                // (in one humanoid pose a bone's world rotation = the shared body rotation x its own T-pose rotation), scaled by the body size
                var heroHuman = HumanMap(av);
                var heroT = TPoseWorld(Fbx(model), heroHuman.Values, tmp);
                float size = height / gruntH;
                foreach (var (sn, h, lp, lr, s) in socketInfo)
                {
                    if (!heroHuman.TryGetValue(h, out var boneName)) { log.Add(sn + ": the hero has no " + h); continue; }
                    var bone = hAll.FirstOrDefault(x => x && x.name == boneName);
                    if (!bone || !gruntT.TryGetValue(gruntHuman[h], out var tg) || !heroT.TryGetValue(boneName, out var th)) { log.Add(sn + ": no T-pose for " + h); continue; }
                    var conv = Quaternion.Inverse(th) * tg;
                    var go = new GameObject(sn) { layer = layer };
                    go.transform.SetParent(bone, false);
                    go.transform.position = bone.position + bone.rotation * (conv * lp * size);
                    go.transform.rotation = bone.rotation * (conv * lr);
                    float want = Mathf.Abs(s.lossyScale.x) * size, have = Mathf.Abs(go.transform.lossyScale.x);
                    if (have > 1e-6f) go.transform.localScale *= want / have;
                    foreach (Transform child in s.Cast<Transform>().ToList())
                    {   // the rifle (and its Grip / Foregrip / Muzzle) moves over with its local pose
                        var lpos = child.localPosition; var lrot = child.localRotation; var lsc = child.localScale;
                        child.SetParent(go.transform, false); child.localPosition = lpos; child.localRotation = lrot; child.localScale = lsc;
                        foreach (var tr in child.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = layer;
                    }
                    log.Add($"{sn} on {boneName} (world scale {want:F3})");
                }
                Object.DestroyImmediate(old.gameObject);
                var look = root.GetComponent<OzHeroLook>(); if (!look) look = root.AddComponent<OzHeroLook>();
                look.renderers = renderers.ToArray();
                if (mats.red != null)
                {
                    var red = new List<Material>(); var blue = new List<Material>();
                    foreach (var r in renderers) foreach (var m in r.sharedMaterials) { bool eye = m == mats.red[1]; red.Add(eye ? mats.red[1] : mats.red[0]); blue.Add(eye ? mats.blue[1] : mats.blue[0]); }
                    look.red = red.ToArray(); look.blue = blue.ToArray();
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return $"{path}: {string.Join("; ", log)}";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
                foreach (var g in tmp) if (g) Object.DestroyImmediate(g);
            }
        }

        /// <summary>humanoid bone name -> the rig's bone name (the avatar's description).</summary>
        static Dictionary<string, string> HumanMap(Avatar av)
        {
            var d = new Dictionary<string, string>();
            if (!av) return d;
            foreach (var hb in av.humanDescription.human) if (!string.IsNullOrEmpty(hb.humanName)) d[hb.humanName] = hb.boneName;
            return d;
        }

        /// <summary>World rotations of the named bones at the avatar's T-pose (the importer's skeleton), the model root at identity.</summary>
        static Dictionary<string, Quaternion> TPoseWorld(string fbx, IEnumerable<string> bones, List<GameObject> tmp)
        {
            var d = new Dictionary<string, Quaternion>();
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (!asset || !(AssetImporter.GetAtPath(fbx) is ModelImporter mi)) return d;
            var inst = Object.Instantiate(asset); tmp.Add(inst);
            inst.transform.position = Vector3.zero; inst.transform.rotation = Quaternion.identity; inst.transform.localScale = Vector3.one;
            var byName = new Dictionary<string, Transform>();
            foreach (var t in inst.GetComponentsInChildren<Transform>(true)) if (!byName.ContainsKey(t.name)) byName[t.name] = t;
            foreach (var sb in mi.humanDescription.skeleton)
                if (byName.TryGetValue(sb.name, out var t) && t != inst.transform) { t.localPosition = sb.position; t.localRotation = sb.rotation; }
            foreach (var b in bones) if (byName.TryGetValue(b, out var t)) d[b] = t.rotation;
            return d;
        }

        // ------------------------------------------------------------------------------------------------ 2 portraits
        [MenuItem("OpenZ/Hero Squad/2 Render portraits (pair, Alan, Brad; red, blue)", false, 11)]
        static void MenuPortraits() => OzHeroIconsKI.Render();                                   // kit KI: the planned poses (no automatic pick)

        /// <summary>KC4b: the three pictures (file key, the members drawn): the pair, Alan alone, Brad alone (View/OzHeroPair.cs picks
        /// which one the unit panel shows).</summary>
        static readonly (string key, string[] who)[] Pictures = { ("alanbrad", new[] { "brad", "alan" }), ("alan", new[] { "alan" }), ("brad", new[] { "brad" }) };
        /// <summary></summary>
        public static string OwnerDir => Path.Combine(OpenZPaths.Records, "milestones", "v1_1_20261012", "for_owner", "hero_icons");
        /// <summary></summary>
        static readonly string[] PanelShots = { "panel_alanbrad.png", "panel_alan.png", "panel_brad.png" };

        public static string RenderPortraits()
        {
            if (Application.isPlaying) return Say("leave Play mode first");
            Folder(PortraitDir);
            var log = new List<string>();
            string own = OwnerDir; Directory.CreateDirectory(own);
            foreach (var (key, who) in Pictures)
                foreach (var team in new[] { Team.Red, Team.Blue })
                {
                    string name = $"portrait_{key}_{(team == Team.Red ? "red" : "blue")}.png";
                    var png = Render(team, who, out string why);
                    if (png == null) { log.Add($"{name}: {why}"); continue; }
                    string p = $"{PortraitDir}/{name}";
                    File.WriteAllBytes(p, png);
                    AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
                    CopyTextureSettings(PortraitRef, p);
                    File.WriteAllBytes(Path.Combine(own, name), png);
                    log.Add($"{p}: written ({png.Length / 1024} KB), the same file for the owner in for_owner/hero_icons");
                    foreach (var l in PickLog) log.Add("   " + l);
                }
            File.WriteAllText(Path.Combine(own, "icons_pick_log.txt"), string.Join("\n", log) + "\n");
            log.Add("next: in Play, the unit panel with each picture (" + string.Join(", ", PanelShots) + ") into for_owner/hero_icons, then menu 4");
            return Say(string.Join("\n", log));
        }

        /// <summary>KC4b: the poses tried for the picture (a clip of the grunt's controller and the time in it, s) and the turns of the two
        /// (deg away from facing the camera: Brad, Alan; a picture of one uses his column). The menu renders every pose and turn small,
        /// measures it and keeps the best (log).</summary>
        public static (string clip, float t)[] PoseTries = { ("idle_calm_humanoid", 0.5f), ("aim_hold_humanoid", 0.3f), ("idle_calm_humanoid", 1.5f) };
        public static (float brad, float alan)[] TurnTries = { (20f, -20f), (32f, -30f), (10f, -10f) };
        /// <summary>run 15: every candidate of menu 2 written small into this folder (null = none), and a forced pick per picture key ("alanbrad", "alan", "brad": clip, second, turns).</summary>
        public static string DumpDir = null;
        public static readonly Dictionary<string, (string clip, float t, float[] yaw)> ForcePick = new Dictionary<string, (string clip, float t, float[] yaw)>
        {   // run 15: chosen by eye from the candidates (the map's own idle with the rifle held in both hands, the faces to the lens)
            ["alanbrad"] = ("idle_calm_humanoid", 0.5f, new[] { 20f, -20f }),
            ["alan"] = ("idle_calm_humanoid", 0.5f, new[] { -20f }),
            ["brad"] = ("idle_calm_humanoid", 0.5f, new[] { 20f }),
        };
        /// <summary>run 15: how far the head (and the neck, a share of it) turns from the pose toward the lens, at most (degrees).</summary>
        public static float HeadToLensMax = 55f, NeckShare = 0.35f;
        static readonly List<string> IkLog = new List<string>();
        /// <summary>run 15 (item 188): where the two stand in the pair picture (x a grunt's size; the lens looks along -z): Brad in front on the
        /// right of the picture, Alan half a step behind on the left, apart enough that his rifle does not cross Brad.</summary>
        public static Vector3 PairBrad = new Vector3(-0.42f, 0f, 0.22f), PairAlan = new Vector3(0.80f, 0f, -0.10f);
        /// <summary>The rifle's khaki in the picture only (PaintKhaki preset B #B89A6A, the game's khaki): the rifle texture's orange
        /// paint texels (hue KhakiHueLo..KhakiHueHi deg, saturated) are turned to it keeping each texel's luminance, as TeamLit's
        /// PaintKhaki remap does for the toy albedos (the grunt rifle's material is outside that path: in the game it stays orange).</summary>
        public static Color32 RifleKhaki = new Color32(184, 154, 106, 255);
        public static float KhakiHueLo = 18f, KhakiHueHi = 52f;
        /// <summary>The old flat tint, used only when the rifle texture cannot be read.</summary>
        public static Color RifleTint = new Color(0.86f, 0.80f, 0.66f, 1f);
        /// <summary>Wanted of the picked pose: each rifle seen along at least this share of its length (not end-on), at least this share
        /// of the picture showing each rifle (Brad / Alan; one robot alone: MinSoloRifle), at most this share of each face covered by a
        /// rifle. The rifle pixels' mean colour reads "orange" from OrangeSat saturation in the hue band 12-45 deg.</summary>
        public static float MinAlong = 0.55f, MinBradRifle = 0.012f, MinAlanRifle = 0.006f, MinSoloRifle = 0.012f, MaxFaceCover = 0.04f, OrangeSat = 0.6f;
        /// <summary>The last picks (menu 2's log).</summary>
        public static readonly List<string> PickLog = new List<string>();

        sealed class Member { public string name; public GameObject go; public Animator an; public Renderer[] rifle; public Transform grip, muzzle; }

        static byte[] Render(Team team, string[] who, out string why)
        {
            why = null;
            var scene = EditorSceneManager.NewPreviewScene();
            var made = new List<GameObject>(); var temp = new List<Object>();
            RenderTexture rt = null; Texture2D tex = null;
            var khaki = new Dictionary<Texture, Texture2D>();
            PickLog.Clear();
            try
            {
                var ctl = AssetDatabase.LoadAssetAtPath<AnimatorController>(Ctl);
                float size = AlanToGrunt;                                                             // v1 was drawn at a grunt's size (Alan x 1.00)
                bool pair = who.Length > 1;
                var ms = new List<Member>();
                foreach (var member in who)
                {
                    var pos = !pair ? Vector3.zero : member == "brad" ? PairBrad * size : PairAlan * size;
                    var pf = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(member));
                    if (!pf) { why = PrefabPath(member) + " missing (menu 1 first)"; return null; }
                    var go = Object.Instantiate(pf);
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene); made.Add(go);
                    go.transform.position = pos;
                    var look = go.GetComponent<OzHeroLook>(); if (look) look.Apply(team);
                    var m = new Member { name = member, go = go, an = go.GetComponentInChildren<Animator>() };
                    var rw = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "RightHandWeapon");
                    m.rifle = rw ? rw.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
                    m.grip = rw ? rw.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "Grip") : null;
                    m.muzzle = rw ? rw.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "Muzzle") : null;
                    var mpb = new MaterialPropertyBlock();
                    foreach (var r in m.rifle)
                    {   // KC4b round 4: the rifle's own texture with its orange paint turned khaki (the picture only)
                        var src = r.sharedMaterial && r.sharedMaterial.HasProperty("_BaseMap") ? r.sharedMaterial.GetTexture("_BaseMap") : r.sharedMaterial ? r.sharedMaterial.mainTexture : null;
                        if (src && !khaki.ContainsKey(src)) { var k = KhakiCopy(src); khaki[src] = k; if (k) temp.Add(k); }
                        r.GetPropertyBlock(mpb);
                        if (src && khaki[src])
                        {   // run 15: a material copy on this preview instance (the property block alone did not reach the picture: the rifle stayed orange)
                            var mc = new Material(r.sharedMaterial) { name = r.sharedMaterial.name + "_khaki" }; temp.Add(mc);
                            if (mc.HasProperty("_BaseMap")) mc.SetTexture("_BaseMap", khaki[src]); if (mc.HasProperty("_MainTex")) mc.SetTexture("_MainTex", khaki[src]);
                            if (mc.HasProperty("_BaseColor")) mc.SetColor("_BaseColor", Color.white);
                            r.sharedMaterial = mc;
                        }
                        else { mpb.SetColor("_BaseColor", RifleTint); mpb.SetColor("_Color", RifleTint); PickLog.Add($"{member}: the rifle texture of {r.name} could not be read: the flat tint instead"); }
                        r.SetPropertyBlock(mpb);
                    }
                    ms.Add(m);
                }
                var camGo = new GameObject("PortraitCam"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene); made.Add(camGo);
                var cam = camGo.AddComponent<Camera>(); cam.scene = scene; cam.fieldOfView = 24f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 80f;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.03f, 0.10f, 0.24f, 1f);
                void Lamp(string n, Color c, float intensity, Vector3 euler)
                {
                    var l = new GameObject(n); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(l, scene); made.Add(l);
                    var li = l.AddComponent<Light>(); li.type = LightType.Directional; li.color = c; li.intensity = intensity; l.transform.rotation = Quaternion.Euler(euler);
                }
                Lamp("Key", new Color(1f, 0.92f, 0.82f), 1.5f, new Vector3(28f, 150f, 0f));     // warm, front-left, from above (less orange than v1)
                Lamp("Rim", new Color(0.55f, 0.72f, 1f), 1.4f, new Vector3(18f, -30f, 0f));     // cool, behind right
                Lamp("Fill", new Color(0.75f, 0.80f, 0.95f), 0.35f, new Vector3(5f, 200f, 0f)); // soft

                if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
                AnimationClip Clip(string n) => ctl ? ctl.animationClips.FirstOrDefault(c => c.name == n) : null;
                void Pose(string clip, float ct, float[] yaw)
                {
                    for (int i = 0; i < ms.Count; i++) ms[i].go.transform.rotation = Quaternion.Euler(0f, 180f + yaw[i], 0f);   // the model faces -Z: turned to the camera on +Z
                    var c = Clip(clip);
                    AnimationMode.BeginSampling();                                                  // ONE sampling pass for all (v1 sampled Brad, then Alan in a
                    foreach (var m in ms) if (m.an && c) AnimationMode.SampleAnimationClip(m.an.gameObject, c, ct);   // second pass, which put Brad back in his bind pose)
                    AnimationMode.EndSampling();
                }
                Transform Bone(Member m, HumanBodyBones b) => m.an && m.an.isHuman ? m.an.GetBoneTransform(b) : null;
                void Frame()
                {
                    if (pair)
                    {
                        float headY = 0f; var mid = Vector3.zero; int nh = 0;
                        foreach (var m in ms)
                        {
                            var hb = Bone(m, HumanBodyBones.Head);
                            var hp = hb ? hb.position : m.go.transform.position + Vector3.up * 2f * size;
                            headY = Mathf.Max(headY, hp.y); mid += hp; nh++;
                        }
                        mid /= Mathf.Max(1, nh);
                        var aim = new Vector3(mid.x * 0.5f, headY - 0.35f * size, 0f);
                        cam.transform.position = aim + Quaternion.Euler(4f, -18f, 0f) * new Vector3(0f, 0f, 5.2f * size);
                        cam.transform.LookAt(aim);
                        return;
                    }
                    // one robot: a bust from the top of his head down to his hips or his rifle, whichever is lower (the rifle in the picture)
                    var one = ms[0]; var head = Bone(one, HumanBodyBones.Head); var hips = Bone(one, HumanBodyBones.Hips);
                    var basePos = one.go.transform.position;
                    float top = (head ? head.position.y : basePos.y + 2.4f * size) + 0.34f * size;
                    float low = hips ? hips.position.y : top - 1.5f * size;
                    // run 15: a bust like the other unit portraits (head to hips); the rifle may run out of the picture as theirs do
                    low -= 0.12f * size;
                    float hgt = Mathf.Max(0.9f * size, top - low);
                    float cx = head ? head.position.x : basePos.x;
                    var at = new Vector3(cx, (top + low) * 0.5f, head ? head.position.z : basePos.z);
                    float dist = hgt * 1.15f / (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
                    cam.transform.position = at + Quaternion.Euler(4f, -18f, 0f) * new Vector3(0f, 0f, dist);
                    cam.transform.LookAt(at);
                }
                void HandsOnRifle()
                {   // run 15 (items 181, 182): the left hand on the rifle's fore-grip as the game's view holds it (a two-bone reach of the arm; the
                    // preview has no runtime hand placement), the elbow kept on its own side
                    foreach (var m in ms)
                    {
                        var up = Bone(m, HumanBodyBones.LeftUpperArm); var lo = Bone(m, HumanBodyBones.LeftLowerArm); var hand = Bone(m, HumanBodyBones.LeftHand);
                        var fg = m.go.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "Foregrip");
                        if (!up || !lo || !hand || !fg) continue;
                        Vector3 A = up.position, B = lo.position, C = hand.position, T = fg.position;
                        float a = Vector3.Distance(A, B), b = Vector3.Distance(B, C), d = Mathf.Clamp(Vector3.Distance(A, T), Mathf.Abs(a - b) + 1e-3f, a + b - 1e-3f);
                        Vector3 at = (T - A).normalized, ab = (B - A).normalized;
                        Vector3 n = Vector3.Cross(at, ab); if (n.sqrMagnitude < 1e-8f) n = Vector3.Cross(at, Vector3.up); n.Normalize();
                        float angA = Mathf.Acos(Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f)) * Mathf.Rad2Deg;
                        Vector3 abWant = Quaternion.AngleAxis(-angA, n) * at;                   // toward the elbow's own side of the line to the target
                        if (Vector3.Dot(Vector3.Cross(at, abWant), n) < 0f) abWant = Quaternion.AngleAxis(angA, n) * at;
                        up.rotation = Quaternion.FromToRotation(ab, abWant) * up.rotation;
                        Vector3 B2 = lo.position, C2 = hand.position;
                        lo.rotation = Quaternion.FromToRotation((C2 - B2).normalized, (T - B2).normalized) * lo.rotation;
                        IkLog.Add($"{m.name}: left hand {Vector3.Distance(hand.position, T) * 100f:0.0} cm from the fore-grip (was {Vector3.Distance(C, T) * 100f:0.0} cm)");
                    }
                }
                void HeadsToLens()
                {   // run 15 (item 182): the face looks into the lens like every unit portrait: the neck takes a share, the head the rest
                    foreach (var m in ms)
                    {
                        var head = Bone(m, HumanBodyBones.Head); var neck = Bone(m, HumanBodyBones.Neck);
                        var front = m.go.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "HeadFront");
                        if (!head || !front) continue;
                        for (int pass = 0; pass < 2; pass++)
                        {
                            var bone = pass == 0 ? neck : head; if (!bone) continue;
                            Vector3 face = (front.position - head.position); if (face.sqrMagnitude < 1e-6f) break; face.Normalize();
                            Vector3 to = (cam.transform.position - front.position).normalized;
                            var full = Quaternion.FromToRotation(face, to); full.ToAngleAxis(out float ang, out Vector3 ax);
                            if (ang > 180f) ang -= 360f;
                            float want = Mathf.Clamp(ang, -HeadToLensMax, HeadToLensMax) * (pass == 0 ? NeckShare : 1f);
                            bone.rotation = Quaternion.AngleAxis(want, ax) * bone.rotation;
                        }
                    }
                }
                Color32[] Shot(int res, Renderer[] off)
                {
                    if (off != null) foreach (var r in off) r.enabled = false;
                    var t2 = RenderTexture.GetTemporary(res, res, 24, RenderTextureFormat.ARGB32); t2.antiAliasing = 1;
                    cam.targetTexture = t2; cam.Render();
                    var prev = RenderTexture.active; RenderTexture.active = t2;
                    var tx = new Texture2D(res, res, TextureFormat.RGB24, false); tx.ReadPixels(new Rect(0, 0, res, res), 0, 0); tx.Apply();
                    RenderTexture.active = prev; cam.targetTexture = null; RenderTexture.ReleaseTemporary(t2);
                    var px = tx.GetPixels32(); Object.DestroyImmediate(tx);
                    if (off != null) foreach (var r in off) r.enabled = true;
                    return px;
                }
                const int R = 256;
                var turns = pair ? TurnTries.Select(t => new[] { t.brad, t.alan }).ToArray() : TurnTries.Select(t => new[] { who[0] == "brad" ? t.brad : t.alan }).ToArray();
                float best = float.NegativeInfinity; string pickClip = "idle"; float pickT = 0.5f; float[] pickYaw = turns[0]; string pickLine = ""; Color pickRifle = Color.clear;
                string Names() => string.Join(" / ", ms.Select(m => m.name == "brad" ? "Brad" : "Alan"));
                foreach (var (clip, ct) in PoseTries)
                {
                    if (!Clip(clip)) { PickLog.Add($"{clip}: not in the controller"); continue; }
                    foreach (var yaw in turns)
                    {
                        Pose(clip, ct, yaw); Frame(); HandsOnRifle(); HeadsToLens();
                        var full = Shot(R, null);
                        if (DumpDir != null && team == Team.Red) { var dt = new Texture2D(R, R, TextureFormat.RGB24, false); dt.SetPixels32(full); dt.Apply(); Directory.CreateDirectory(DumpDir); File.WriteAllBytes(Path.Combine(DumpDir, $"{string.Join("_", who)}_{clip}_{ct:0.0}_{string.Join("_", yaw.Select(y => y.ToString("0")))}.png"), dt.EncodeToPNG()); Object.DestroyImmediate(dt); }
                        var no = ms.Select(m => Shot(R, m.rifle)).ToArray();
                        var vis = new float[ms.Count]; var along = new float[ms.Count]; int edge = 0;
                        for (int i = 0; i < ms.Count; i++) { vis[i] = Diff(full, no[i], R, out int e) / (float)(R * R); edge += e; along[i] = Along(cam, ms[i]); }
                        bool across = true;                                                          // run 15: each rifle lies across the picture (held, not stood up like a pole)
                        foreach (var m in ms) if (m.grip && m.muzzle) { var g2 = cam.WorldToScreenPoint(m.grip.position); var u2 = cam.WorldToScreenPoint(m.muzzle.position); if (Mathf.Abs(u2.x - g2.x) < Mathf.Abs(u2.y - g2.y)) across = false; }
                        float cover = 0f;
                        foreach (var m in ms) cover = Mathf.Max(cover, FaceCover(cam, m, full, no, R, size));
                        var rifleCol = RifleColour(full, no);
                        bool ok; float score;
                        if (pair)
                        {
                            ok = along[0] >= MinAlong && along[1] >= MinAlong * 0.8f && vis[0] >= MinBradRifle && vis[1] >= MinAlanRifle && cover <= MaxFaceCover && edge < 6 && across;
                            score = (ok ? 10f : 0f) + Mathf.Min(along[0], along[1]) + 20f * Mathf.Min(vis[0], 2f * vis[1]) - 10f * cover - 0.05f * edge;
                        }
                        else
                        {
                            ok = along[0] >= MinAlong && vis[0] >= MinSoloRifle && cover <= MaxFaceCover && across;       // run 15: a bust, the rifle may leave the picture
                            score = (ok ? 10f : 0f) + along[0] + 20f * vis[0] - 10f * cover;
                        }
                        string line = $"{clip} @{ct:0.0} s, turns {Names()} {string.Join(" / ", yaw.Select(y => y.ToString("0")))} deg: rifle seen along {string.Join(" / ", along.Select(x => x.ToString("0.00")))} of its length, " +
                                      $"rifle on {string.Join(" / ", vis.Select(x => (x * 100).ToString("0.0")))} % of the picture, faces covered {cover * 100:0} %, rifle pixels on the border {edge}, rifle colour {Hex(rifleCol)}{(across ? "" : ", a rifle stands upright")}{(ok ? " (meets every want)" : "")}";
                        PickLog.Add(line);
                        if (score > best) { best = score; pickClip = clip; pickT = ct; pickYaw = yaw; pickLine = line; pickRifle = rifleCol; }
                    }
                }
                PickLog.Add($"PICKED ({string.Join(" + ", who)}, {team}): " + pickLine);
                Color.RGBToHSV(pickRifle, out float ph, out float psat, out _);
                bool orange = pickRifle.a > 0f && ph * 360f >= 12f && ph * 360f <= 45f && psat >= OrangeSat;
                PickLog.Add((orange ? "FAIL  " : "PASS  ") + $"nothing orange in front of them: the rifle's mean colour in the picture {Hex(pickRifle)} (hue {ph * 360f:0} deg, saturation {psat:0.00}; orange = hue 12-45 deg and saturation from {OrangeSat:0.00}; the game's khaki #B89A6A is 0.42)");
                string pkey = pair ? "alanbrad" : who[0];
                if (ForcePick.TryGetValue(pkey, out var fp)) { pickClip = fp.clip; pickT = fp.t; pickYaw = fp.yaw; PickLog.Add($"FORCED ({pkey}): {fp.clip} @{fp.t:0.0} s, turns {string.Join(" / ", fp.yaw.Select(y => y.ToString("0")))} deg"); }
                Pose(pickClip, pickT, pickYaw); Frame(); IkLog.Clear(); HandsOnRifle(); HeadsToLens(); PickLog.AddRange(IkLog);
                rt = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
                cam.targetTexture = rt; cam.Render();
                var prevA = RenderTexture.active; RenderTexture.active = rt;
                tex = new Texture2D(1024, 1024, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0); tex.Apply();
                RenderTexture.active = prevA;
                cam.targetTexture = null;
                Backdrop(tex, cam.backgroundColor);
                return tex.EncodeToPNG();
            }
            finally
            {
                if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
                foreach (var g in made) if (g) Object.DestroyImmediate(g);
                foreach (var o in temp) if (o) Object.DestroyImmediate(o);
                if (rt) { rt.Release(); Object.DestroyImmediate(rt); }
                if (tex) Object.DestroyImmediate(tex);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>KC4b round 4: a readable copy of the rifle texture (from its file) with its orange paint turned to RifleKhaki, each
        /// texel keeping its luminance (TeamLit's PaintKhaki formula: target x luminance / PaintKhaki.RefLum); null = not readable.</summary>
        static Texture2D KhakiCopy(Texture src)
        {
            string p = src ? AssetDatabase.GetAssetPath(src) : null;
            if (string.IsNullOrEmpty(p) || !File.Exists(p)) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, true, false);
            if (!t.LoadImage(File.ReadAllBytes(p), false)) { Object.DestroyImmediate(t); return null; }
            var px = t.GetPixels32();
            var target = ((Color)RifleKhaki).linear;
            for (int i = 0; i < px.Length; i++)
            {
                Color c = px[i];
                Color.RGBToHSV(c, out float h, out float s, out float v);
                float w = HueBand(h * 360f, KhakiHueLo, KhakiHueHi, 6f) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.22f, 0.38f, s)) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.06f, 0.14f, v));
                if (w <= 0f) continue;
                var l = c.linear; float y = 0.2126f * l.r + 0.7152f * l.g + 0.0722f * l.b;
                float k = Mathf.Min(1f, y / PaintKhaki.RefLum);                                    // run 15: never brighter than the khaki itself (k over 1 turned it a light yellow-orange under the key light)
                var kc = new Color(Mathf.Clamp01(target.r * k), Mathf.Clamp01(target.g * k), Mathf.Clamp01(target.b * k), 1f);
                var o = Color.Lerp(l, kc, w).gamma; o.a = c.a;
                px[i] = o;
            }
            t.SetPixels32(px); t.Apply(true);
            t.wrapMode = src.wrapMode; t.filterMode = src.filterMode; t.anisoLevel = src.anisoLevel;
            return t;
        }

        static float HueBand(float h, float lo, float hi, float soft) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(lo - soft, lo, h)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(hi, hi + soft, h)));

        /// <summary>The mean colour of the rifle pixels (those that change when a rifle is hidden); a = 0: no rifle seen.</summary>
        static Color RifleColour(Color32[] full, Color32[][] no)
        {
            double r = 0, g = 0, b = 0; int n = 0;
            for (int i = 0; i < full.Length; i++)
            {
                bool rifle = false;
                foreach (var x in no) { int d = Mathf.Abs(full[i].r - x[i].r) + Mathf.Abs(full[i].g - x[i].g) + Mathf.Abs(full[i].b - x[i].b); if (d > 24) { rifle = true; break; } }
                if (!rifle) continue;
                r += full[i].r; g += full[i].g; b += full[i].b; n++;
            }
            return n == 0 ? Color.clear : new Color((float)(r / n / 255.0), (float)(g / n / 255.0), (float)(b / n / 255.0), 1f);
        }

        static string Hex(Color c) => c.a <= 0f ? "(no rifle seen)" : "#" + ColorUtility.ToHtmlStringRGB(c);

        /// <summary>Pixels that change when a rifle is hidden (= the rifle seen), and how many of them lie on the picture's border.</summary>
        static int Diff(Color32[] a, Color32[] b, int res, out int edge)
        {
            int n = 0; edge = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int d = Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b);
                if (d <= 24) continue;
                n++; int x = i % res, y = i / res;
                if (x == 0 || y == 0 || x == res - 1 || y == res - 1) edge++;
            }
            return n;
        }

        /// <summary>How much of the rifle's length the camera sees (1 = side on, 0 = end-on toward the camera).</summary>
        static float Along(Camera cam, Member m)
        {
            if (!m.grip || !m.muzzle) return 0f;
            var d = m.muzzle.position - m.grip.position; float len = d.magnitude; if (len < 1e-4f) return 0f;
            return Vector3.ProjectOnPlane(d, cam.transform.forward).magnitude / len;
        }

        /// <summary>The share of the member's face (a disc round the head bone) a rifle covers.</summary>
        static float FaceCover(Camera cam, Member m, Color32[] full, Color32[][] no, int res, float size)
        {
            var hb = m.an && m.an.isHuman ? m.an.GetBoneTransform(HumanBodyBones.Head) : null; if (!hb) return 0f;
            var c = hb.position + Vector3.up * 0.18f * size;
            var sc = cam.WorldToViewportPoint(c); var se = cam.WorldToViewportPoint(c + cam.transform.right * 0.28f * size);
            float r = Mathf.Abs(se.x - sc.x) * res; if (r < 1f || sc.z < 0f) return 0f;
            int cx = Mathf.RoundToInt(sc.x * res), cy = Mathf.RoundToInt(sc.y * res), all = 0, hit = 0;
            for (int y = cy - (int)r; y <= cy + (int)r; y++)
                for (int x = cx - (int)r; x <= cx + (int)r; x++)
                {
                    if (x < 0 || y < 0 || x >= res || y >= res || (x - cx) * (x - cx) + (y - cy) * (y - cy) > r * r) continue;
                    all++; int i = y * res + x;
                    foreach (var q in no)
                        if (Mathf.Abs(full[i].r - q[i].r) + Mathf.Abs(full[i].g - q[i].g) + Mathf.Abs(full[i].b - q[i].b) > 24) { hit++; break; }
                }
            return all > 0 ? hit / (float)all : 0f;
        }

        /// <summary>The BS-01 portraits' backdrop: deep blue, lighter behind the heads, darker to the corners.</summary>
        static void Backdrop(Texture2D t, Color bg)
        {
            var px = t.GetPixels(); int w = t.width, h = t.height;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = px[y * w + x];
                    float dx = (x - w * 0.5f) / w, dy = (y - h * 0.62f) / h, r = Mathf.Sqrt(dx * dx + dy * dy);
                    float k = Mathf.Clamp01(1.25f - r * 1.6f);
                    bool isBg = Mathf.Abs(c.r - bg.r) < 0.01f && Mathf.Abs(c.g - bg.g) < 0.01f && Mathf.Abs(c.b - bg.b) < 0.01f;
                    c = isBg ? Color.Lerp(new Color(0.01f, 0.05f, 0.14f), new Color(0.10f, 0.30f, 0.62f), k) : c * Mathf.Lerp(0.82f, 1f, k);
                    c.a = 1f; px[y * w + x] = c;
                }
            t.SetPixels(px); t.Apply();
        }

        static void CopyTextureSettings(string from, string to)
        {
            if (!(AssetImporter.GetAtPath(from) is TextureImporter a) || !(AssetImporter.GetAtPath(to) is TextureImporter b)) return;
            var s = new TextureImporterSettings(); a.ReadTextureSettings(s); b.SetTextureSettings(s);
            b.maxTextureSize = a.maxTextureSize; b.textureCompression = a.textureCompression; b.crunchedCompression = a.crunchedCompression;
            b.SaveAndReimport();
        }

        // ------------------------------------------------------------------------------------------------ 3 check
        [MenuItem("OpenZ/Hero Squad/3 Check", false, 12)]
        static void MenuCheck() => Check();

        public static string Check()
        {
            var sb = new StringBuilder();
            float gH = KhCheckHeight(null);                                                           // KH: the idle pose (the bind pose if it cannot be sampled)
            var hs = new Dictionary<string, float>();
            foreach (var (member, model) in Members)
            {
                var av = AssetDatabase.LoadAllAssetsAtPath(Fbx(model)).OfType<Avatar>().FirstOrDefault();
                sb.AppendLine(V(av && av.isValid && av.isHuman, $"{Fbx(model)}: humanoid avatar valid ({(av ? av.humanDescription.human.Length : 0)} bones mapped)"));
                var orig = AssetImporter.GetAtPath($"{SrcDir}/{model}.fbx") as ModelImporter;
                sb.AppendLine(V(orig && orig.animationType == ModelImporterAnimationType.Generic, $"{SrcDir}/{model}.fbx (the briefing's) still Generic"));
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(member));
                if (!pf) { sb.AppendLine(V(false, PrefabPath(member) + " missing")); continue; }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf);
                try
                {
                    var an = inst.GetComponentInChildren<Animator>();
                    bool states = an && an.isHuman && an.runtimeAnimatorController && an.runtimeAnimatorController.animationClips.Length > 10;
                    sb.AppendLine(V(states, $"{member}: humanoid Animator with the grunt's controller ({(an && an.runtimeAnimatorController ? an.runtimeAnimatorController.name + ", " + an.runtimeAnimatorController.animationClips.Length + " clips" : "-")})"));
                    var model2 = inst.transform.Find("Model"); if (model2) OpenZRobotsV3.ToBindPose(model2.gameObject);
                    hs[member] = KhCheckHeight(inst, model2 ? model2.gameObject : inst);                  // KH: the idle pose
                    var names = inst.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToList();
                    bool socks = Sockets.All(names.Contains) && names.Any(n => n.StartsWith("BS01_Weapon_")) && names.Contains("Grip") && names.Contains("Muzzle");
                    sb.AppendLine(V(socks, $"{member}: sockets {string.Join(", ", Sockets)} and the grunt's rifle with Grip / Foregrip / Muzzle"));
                    var look = inst.GetComponent<OzHeroLook>();
                    sb.AppendLine(V(look && look.red != null && look.blue != null && look.red.Length == look.blue.Length && look.blue.All(m => m), $"{member}: red and blue material sets"));
                }
                finally { Object.DestroyImmediate(inst); }
            }
            if (hs.TryGetValue("brad", out var b) && hs.TryGetValue("alan", out var a) && b > 0)
                sb.AppendLine(V(Mathf.Abs(a / b - AlanToBrad) < 0.01f && a >= gH * AlanToGruntMin * 0.99f && a <= gH * AlanToGruntMax * 1.01f,
                    $"heights: grunt {gH:F2} m, Alan {a:F2} m, Brad {b:F2} m (Alan {a / b * 100:0.0} % of Brad, {a / gH:0.00} x a grunt, wanted {AlanToGruntMin:0.00}-{AlanToGruntMax:0.00}; Brad {b / gH:0.00} x)"));
            foreach (var (key, _) in Pictures)
                foreach (var c in new[] { "red", "blue" })
                {
                    var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{PortraitDir}/portrait_{key}_{c}.png");
                    sb.AppendLine(V(t && t.width == 1024 && t.height == 1024, $"portrait_{key}_{c}.png 1024 x 1024 (KC4b: the pair, Alan alone, Brad alone)"));
                }
            sb.Append(KhCheck());                                                                     // KH: unit material, controller, avatar legs
            for (int i = 1; i <= 3; i++)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/OpenZ/Resources/OpenZ/Audio/Sfx/HeroSquad/hero_gun_{i}.wav");
                sb.AppendLine(V(clip && clip.length > 0.5f, $"hero_gun_{i}.wav loaded ({(clip ? clip.length : 0f):F2} s)"));
            }
            var map = Resources.Load<TextAsset>("OpenZ/Maps/L16_grid");
            sb.AppendLine(V(map && map.text.Contains("\"heroes\"") && map.text.Contains("\"squad\": \"alan_brad\""), "level 16 data: the list heroes places the extra squad alan_brad (with its campfire tile)"));
            return Say(sb.ToString());
        }

        static string V(bool ok, string s) => (ok ? "PASS  " : "FAIL  ") + s;

        // ------------------------------------------------------------------------------------------------ 4 the owner's marker
        [MenuItem("OpenZ/Hero Squad/4 Icons for the owner: check and mark ready", false, 13)]
        static void MenuOwnerIcons() => OwnerIcons();

        /// <summary>KC4b (item 166): checks for_owner/hero_icons (the six pictures of menu 2 at 1024 x 1024, the three unit panel
        /// missing.</summary>
        public static string OwnerIcons()
        {
            string own = OwnerDir; Directory.CreateDirectory(own);
            var have = new List<string>(); var miss = new List<string>();
            foreach (var (key, _) in Pictures)
                foreach (var c in new[] { "red", "blue" })
                {
                    string f = Path.Combine(own, $"portrait_{key}_{c}.png");
                    var tx = new Texture2D(2, 2);
                    bool ok = File.Exists(f) && tx.LoadImage(File.ReadAllBytes(f)) && tx.width == 1024 && tx.height == 1024;
                    Object.DestroyImmediate(tx);
                    (ok ? have : miss).Add($"portrait_{key}_{c}.png{(ok ? " (1024 x 1024)" : File.Exists(f) ? " (not 1024 x 1024)" : "")}");
                }
            foreach (var s in PanelShots) (File.Exists(Path.Combine(own, s)) ? have : miss).Add(s);
            string marker = Path.Combine(own, "icons_DONE.txt");
            if (miss.Count > 0)
            {
                if (File.Exists(marker)) File.Delete(marker);
                return Say("hero icons NOT ready, missing: " + string.Join(", ", miss) + " (menu 2 renders the pictures; the panel pictures come from Play)");
            }
            var sb = new StringBuilder();
            sb.AppendLine("ALAN & BRAD unit panel icons for approval: the pair, Alan alone, Brad alone (red and blue), rendered in the engine from the game's own models (menu OpenZ/Hero Squad/2), and the unit panel with each.");
            foreach (var h in have) sb.AppendLine("  " + h);
            sb.AppendLine("The pick table of each picture: icons_pick_log.txt. They ship only after the yes.");
            File.WriteAllText(marker, sb.ToString());
            return Say("hero icons ready: " + marker + "\n" + sb);
        }

        // ------------------------------------------------------------------------------------------------ Play helpers
        [MenuItem("OpenZ/Hero Squad/Play: report", false, 30)]
        static void PlayReport() => Say(Application.isPlaying ? OzHeroSquadView.Report() : "enter Play first");

        [MenuItem("OpenZ/Hero Squad/Play: select Brad (the loungers go away)", false, 31)]
        static void PlaySelect()
        {
            if (!Application.isPlaying) { Say("enter Play first"); return; }
            var g = ZGame.I; var input = Object.FindFirstObjectByType<PlayerInput>();
            if (!g || g.World == null || !input) { Say("no game / input"); return; }
            var brad = g.World.Objects.FirstOrDefault(o => o.IsHero && !o.IsMinion && !o.Destroyed);
            if (brad == null) { Say("no hero squad in this level"); return; }
            input.Selection.Clear(); input.Selection.Add(brad);
            Say($"selected {brad.Hero.Name} (#{brad.Id})");
        }

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace("\\", "/"); Folder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static string Say(string s) { Debug.Log("[OpenZ HeroSquad] " + s); return s; }
    }
}
