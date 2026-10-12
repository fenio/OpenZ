using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// (OpenZRobotsV3, left as it is): the BS-01 skeleton names + hierarchy (SkinRoot / Armature / mixamorig*) plus three non-human bones
    /// (CoilMast_L / CoilMast_R / Gauge_Needle under mixamorigSpine2), rigid parts on the bones, the shared v3 vinyl atlas (a superset:
    /// his cells in rows 8-9) whose alpha is the team mask (TeamLit mode 3), four HDR glow slots, the tesla gatling with its sockets and
    /// the rotating BarrelSpin.
    /// Menus (OpenZ/Electrobot):
    ///  1 Import + prefab: import settings (bodies Humanoid via OpenZRobotsV3.DescribeV3, the 10 electro_* clips), the glow materials
    ///    M_Electro_Glow_Coils / _Pack / _Cells / _Emitter (URP Unlit, _BaseMap = the atlas: the reference's electric blue and white-hot
    ///    cores, _BaseColor = the idle intensity; ElectrobotView animates it per slot, keeping the hue), the base-layer states
    ///    electro_windup / electro_fire / electro_cackle / electro_hatch in RobotBS01.controller, the override controller
    ///    RobotV3_Electrobot (idle / aim_hold / shooting -> electro_hold, the locomotion -> electro_*), the prefab Z_bs01_electrobot (a copy
    ///    of Z_bs01_grunt's root: the v3 Model at the grunt's Model scale, the gatling under RightHandWeapon, WeaponHold two-handed with
    ///    the left elbow from the clip, the two battery hoses as BS01KitCable, RobotV3 type "electrobot", ElectrobotCoils, ElectrobotView
    ///    when the fx package is in), and adds it to the open level's ZGame.unitPrefabs.
    ///  2 Add to this level: Z_bs01_electrobot into the open scene's ZGame.unitPrefabs (run once in L02_Desert and once in L17_CarPark).
    ///  3 Report.  4 Show (Play).  5 Crew Fit Check (Play).  9 Revert.
    /// </summary>
    public static class OpenZElectrobot
    {
        public const string Type = "electrobot";
        const string Dir = "Assets/OpenZ/Models/RobotsV3", Tex = Dir + "/Textures", Pref = "Assets/OpenZ/Prefabs", Mat = "Assets/OpenZ/Materials/Units",
                     Anim = "Assets/OpenZ/Animation", Ctl = Anim + "/RobotBS01.controller", Atlas = Tex + "/BS01V3_Atlas.png",
                     Body = Dir + "/BS01V3_Electrobot.fbx", Weapon = Dir + "/BS01V3_Weapon_Electrobot.fbx",
                     PrefabPath = Pref + "/Z_bs01_electrobot.prefab", OverridePath = Anim + "/RobotV3_Electrobot.overrideController";
        static readonly string[] Clips = { "electro_hold", "electro_windup", "electro_fire", "electro_cackle", "electro_hatch", "electro_walk", "electro_run",
                                           "electro_strafe_l", "electro_strafe_r", "electro_back" };
        static readonly string[] Loops = { "electro_hold", "electro_hatch", "electro_walk", "electro_run", "electro_strafe_l", "electro_strafe_r", "electro_back" };
        static readonly string[] States = { "electro_windup", "electro_fire", "electro_cackle", "electro_hatch" };
        static readonly Dictionary<string, string> OverrideMap = new Dictionary<string, string>
        {
            { "idle", "electro_hold" }, { "aim_hold", "electro_hold" }, { "shooting", "electro_hold" }, { "walking", "electro_walk" },
            { "run_forward", "electro_run" }, { "strafe_left", "electro_strafe_l" }, { "strafe_right", "electro_strafe_r" }, { "run_backward", "electro_back" },
        };
        /// <summary>The four glow slots and their idle intensity (linear HDR; ElectrobotView's charged idle: coils 2.4 x pulse, pack 1.8,
        /// cells 3, emitter 1.6).</summary>
        static readonly (string slot, float k)[] Glows = { ("Coils", 2.4f), ("Pack", 1.8f), ("Cells", 3f), ("Emitter", 1.6f) };
        /// <summary>
        /// the atlas glow cells (304BFF coils + pack, 6F83FF cells) are so saturated that the tonemapper turns their HDR into lilac by day.
        /// The coils, the pack and the gun cells therefore carry NO texture: ElectrobotView then uses OpenZElectroFx.Glow, the bolt's own
        /// electric blue, which stays blue at every HDR level. The emitter keeps the atlas (its white-hot core).</summary>
        static readonly HashSet<string> GlowUntextured = new HashSet<string> { "Coils", "Pack", "Cells" };
        /// <summary>The coil windings (the copper cell, 1104 tris within 0.35 m of CoilCore_L / R; the pack's copper terminals keep it) glow
        /// on their own slot M_Electro_Glow_Coils_Wind (the coil glow material sampling one texel of the coil glow cell): the reference has
        /// blue glowing windings, the game showed copper ones. A derived mesh asset; the FBX is not touched.</summary>
        const string CoilWindMesh = "Assets/OpenZ/Models/RobotsV3/BS01V3_Electrobot_BodyCoilGlow.asset";
        static readonly Rect CopperCell = new Rect(0.5f, 0.4375f, 0.0625f, 0.0625f);
        const float CoilWindRadius = 0.35f;
        /// <summary>The battery hoses: BS01KitCable Hose between the pack's outlets and the gun's inlets (the model's previews and its
        /// every-frame clip QC draw exactly this cable: radius 0.03, sag 0.10).</summary>
        const float HoseRadius = 0.03f, HoseSag = 0.10f, HoseMaxSpan = 1.6f;

        // ------------------------------------------------------------------------------------------------ 1 import + prefab
        [MenuItem("OpenZ/Electrobot: 1 Import + prefab Z_bs01_electrobot")]
        public static string Run()
        {
            var log = new List<string>();
            if (!AssetDatabase.LoadAssetAtPath<AnimatorController>(Ctl)) return Say("RobotBS01.controller missing (BS-01 steps 1 + 2 first)");
            var atlas = AssetDatabase.LoadAssetAtPath<Material>($"{Mat}/M_BS01_V3.mat");
            if (!atlas) return Say("M_BS01_V3 missing: run OpenZ/Robots v3: 1 first");
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Atlas);
            if (!tex) return Say(Atlas + " missing");
            if (tex.width < 1024 || tex.height < 1024) log.Add($"WARNING atlas {tex.width}x{tex.height}");
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(Body) || !AssetDatabase.LoadAssetAtPath<GameObject>(Weapon)) return Say("the Electrobot FBX are missing (model/apply.py --apply first)");
            var glows = GlowMaterials(tex, log);
            ImportSettings(atlas, glows, log);
            var clips = LoadClips(log);
            AddStates(clips, log);
            var aoc = Override(clips, log);
            float scale = GruntModelScale();
            log.Add($"Model scale (Z_bs01_grunt): {scale:F4}");
            log.Add(BuildPrefab(aoc, atlas, glows, scale));
            AddToOpenScene(log);
            AssetDatabase.SaveAssets();
            return Say(string.Join("\n", log));
        }

        static string Say(string s) { Debug.Log("[OpenZElectrobot]\n" + s); return s; }

        static Dictionary<string, Material> GlowMaterials(Texture2D atlas, List<string> log)
        {
            var d = new Dictionary<string, Material>();
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (!sh) { log.Add("URP Unlit shader not found: no glow materials"); return d; }
            foreach (var (slot, k) in Glows)
            {
                string name = "M_Electro_Glow_" + slot, p = $"{Mat}/{name}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (!m) { m = new Material(sh) { name = name }; AssetDatabase.CreateAsset(m, p); }
                m.shader = sh;
                if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", GlowUntextured.Contains(slot) ? null : atlas); m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureOffset("_BaseMap", Vector2.zero); }
                // the colour is the atlas cell's (the reference's electric blue / white-hot cores); _BaseColor carries only the idle
                // intensity, stored so the shader receives linear k (SetColor converts from gamma in a linear project)
                var c = new Color(k, k, k, 1f).gamma;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                m.enableInstancing = true; m.doubleSidedGI = false;
                EditorUtility.SetDirty(m); d[name] = m;
            }
            log.Add($"glow materials: {string.Join(", ", d.Keys)} (URP Unlit on BS01V3_Atlas, idle HDR {string.Join("/", Glows.Select(g => g.k.ToString("F1")))})");
            return d;
        }

        static void Remap(ModelImporter mi, Material atlas, Dictionary<string, Material> glows)
        {
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_BS01_V3"), atlas);
            foreach (var kv in glows) mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
        }

        static void ImportSettings(Material atlas, Dictionary<string, Material> glows, List<string> log)
        {
            if (AssetImporter.GetAtPath(Weapon) is ModelImporter wi)
            {
                wi.animationType = ModelImporterAnimationType.None; wi.importAnimation = false; wi.preserveHierarchy = true;
                wi.importCameras = false; wi.importLights = false; wi.importBlendShapes = false; wi.addCollider = false;
                wi.importNormals = ModelImporterNormals.Import; Remap(wi, atlas, glows);
                wi.SaveAndReimport();
            }
            var bs01 = AssetImporter.GetAtPath("Assets/OpenZ/Models/BS01/BS01_Robot.fbx") as ModelImporter;
            if (!(AssetImporter.GetAtPath(Body) is ModelImporter bi)) { log.Add(Body + " has no ModelImporter"); return; }
            if (bs01)
            {   // same units / axes / normals as BS01_Robot.fbx and the v3 siblings (same exporter settings)
                bi.globalScale = bs01.globalScale; bi.useFileScale = bs01.useFileScale; bi.useFileUnits = bs01.useFileUnits; bi.bakeAxisConversion = bs01.bakeAxisConversion;
                bi.maxBonesPerVertex = bs01.maxBonesPerVertex; bi.skinWeights = bs01.skinWeights;
            }
            bi.importNormals = ModelImporterNormals.Import; bi.weldVertices = true; bi.meshCompression = ModelImporterMeshCompression.Off;
            bi.preserveHierarchy = true; bi.importCameras = false; bi.importLights = false; bi.importBlendShapes = false; bi.importVisibility = false;
            Remap(bi, atlas, glows);
            bi.animationType = ModelImporterAnimationType.Human; bi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            bi.SaveAndReimport();
            bi.humanDescription = OpenZRobotsV3.DescribeV3(AssetDatabase.LoadAssetAtPath<GameObject>(Body));   // the siblings' avatar (slight knee bend)
            bi.importAnimation = true;
            bi.SaveAndReimport();
            var list = new List<ModelImporterClipAnimation>();
            foreach (var c in bi.defaultClipAnimations)
            {
                string n = c.takeName.Contains("|") ? c.takeName.Substring(c.takeName.LastIndexOf('|') + 1) : c.takeName;
                if (!Clips.Contains(n)) continue;
                c.name = n; c.loopTime = Loops.Contains(n); c.loopPose = false;
                c.lockRootRotation = true; c.keepOriginalOrientation = true;
                c.lockRootHeightY = true; c.keepOriginalPositionY = true; c.heightFromFeet = false;
                c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
                list.Add(c);
            }
            bi.clipAnimations = list.ToArray(); bi.SaveAndReimport();
            var av = AssetDatabase.LoadAllAssetsAtPath(Body).OfType<Avatar>().FirstOrDefault();
            log.Add($"import: {list.Count}/{Clips.Length} clips, avatar {(av ? av.name : "-")} valid={(av && av.isValid)} human={(av && av.isHuman)}");
        }

        static Dictionary<string, AnimationClip> LoadClips(List<string> log)
        {
            var d = new Dictionary<string, AnimationClip>();
            foreach (var c in AssetDatabase.LoadAllAssetsAtPath(Body).OfType<AnimationClip>())
                if (!c.name.StartsWith("__preview") && Clips.Contains(c.name)) d[c.name] = c;
            log.Add($"clip assets: {d.Count} ({string.Join(", ", d.Values.Select(c => $"{c.name} {c.length:F2}s{(c.isLooping ? " loop" : "")}"))})");
            return d;
        }

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
                if (!st) { st = sm.AddState(n, new Vector3(1420 + (i % 2) * 260, -200 + (i / 2) * 60, 0)); added++; } else updated++;
                st.motion = clip; st.writeDefaultValues = wd; st.iKOnFeet = idle ? idle.iKOnFeet : false; st.speed = 1f;
                i++;
            }
            ctl.layers = layers; EditorUtility.SetDirty(ctl);
            log.Add($"RobotBS01.controller: {added} states added, {updated} updated ({string.Join(", ", States)})");
        }

        static AnimatorOverrideController Override(Dictionary<string, AnimationClip> clips, List<string> log)
        {
            var ctl = AssetDatabase.LoadAssetAtPath<AnimatorController>(Ctl);
            var aoc = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(OverridePath);
            if (!aoc) { aoc = new AnimatorOverrideController(ctl) { name = "RobotV3_Electrobot" }; AssetDatabase.CreateAsset(aoc, OverridePath); }
            aoc.runtimeAnimatorController = ctl;
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            aoc.GetOverrides(pairs);
            var done = new List<string>();
            for (int k = 0; k < pairs.Count; k++)
            {
                var orig = pairs[k].Key; if (!orig) continue;
                string key = OverrideMap.Keys.FirstOrDefault(m => orig.name == m || orig.name.StartsWith(m + "_"));
                if (key != null && clips.TryGetValue(OverrideMap[key], out var repl)) { pairs[k] = new KeyValuePair<AnimationClip, AnimationClip>(orig, repl); done.Add($"{orig.name}->{repl.name}"); }
                else pairs[k] = new KeyValuePair<AnimationClip, AnimationClip>(orig, null);
            }
            aoc.ApplyOverrides(pairs); EditorUtility.SetDirty(aoc);
            log.Add("RobotV3_Electrobot: " + string.Join(", ", done));
            return aoc;
        }

        static float GruntModelScale()
        {
            var g = AssetDatabase.LoadAssetAtPath<GameObject>($"{Pref}/Z_bs01_grunt.prefab");
            var m = g ? g.transform.Find("Model") : null;
            return m ? m.localScale.y : 1.0297053f;
        }

        static string CoilWindGlow(GameObject model, Dictionary<string, Material> glows)
        {
            var smr = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(x => x.name == "BS01_Body");
            if (!smr || !smr.sharedMesh) return "coil windings: no BS01_Body";
            var src = AssetDatabase.LoadAllAssetsAtPath(Body).OfType<Mesh>().FirstOrDefault(m => m.name == smr.sharedMesh.name) ?? smr.sharedMesh;
            var mats = smr.sharedMaterials.ToList();
            int glowSub = mats.FindIndex(m => m && m.name == "M_Electro_Glow_Coils");
            if (glowSub < 0 || glowSub >= src.subMeshCount || !glows.TryGetValue("M_Electro_Glow_Coils", out var coilMat)) return "coil windings: no coil glow slot";
            var cl = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "CoilCore_L");
            var cr = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "CoilCore_R");
            if (!cl || !cr) return "coil windings: CoilCore sockets missing";
            var uv = src.uv; var baked = new Mesh(); smr.sharedMesh = src; smr.BakeMesh(baked, true);
            var v = baked.vertices; var M = smr.transform.localToWorldMatrix;
            var t0 = src.GetTriangles(0); var keep = new List<int>(t0.Length); var wind = new List<int>();
            for (int i = 0; i < t0.Length; i += 3)
            {
                var u = (uv[t0[i]] + uv[t0[i + 1]] + uv[t0[i + 2]]) / 3f;
                bool copper = CopperCell.Contains(u);
                if (copper)
                {
                    var c = (M.MultiplyPoint3x4(v[t0[i]]) + M.MultiplyPoint3x4(v[t0[i + 1]]) + M.MultiplyPoint3x4(v[t0[i + 2]])) / 3f;
                    copper = Mathf.Min(Vector3.Distance(c, cl.position), Vector3.Distance(c, cr.position)) < CoilWindRadius;
                }
                (copper ? wind : keep).Add(t0[i]); (copper ? wind : keep).Add(t0[i + 1]); (copper ? wind : keep).Add(t0[i + 2]);
            }
            Object.DestroyImmediate(baked);
            if (wind.Count == 0) return "coil windings: none found";
            // the coil glow cell: the mean UV of the coil glow slot's own triangles
            var tg = src.GetTriangles(glowSub); var mu = Vector2.zero; foreach (var ix in tg) mu += uv[ix]; mu /= Mathf.Max(1, tg.Length);
            string mp = $"{Mat}/M_Electro_Glow_Coils_Wind.mat";
            var wm = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (!wm) { wm = new Material(coilMat) { name = "M_Electro_Glow_Coils_Wind" }; AssetDatabase.CreateAsset(wm, mp); }
            else wm.CopyPropertiesFromMaterial(coilMat);
            wm.shader = coilMat.shader;
            if (wm.HasProperty("_BaseMap") && wm.GetTexture("_BaseMap")) { wm.SetTextureScale("_BaseMap", Vector2.zero); wm.SetTextureOffset("_BaseMap", mu); }   // textured coil slot: one texel of its glow cell
            EditorUtility.SetDirty(wm);
            var mesh = Object.Instantiate(src); mesh.name = src.name + "_CoilGlow";
            int n = src.subMeshCount; mesh.subMeshCount = n + 1;
            for (int sIx = 0; sIx < n; sIx++) mesh.SetTriangles(sIx == 0 ? keep.ToArray() : src.GetTriangles(sIx), sIx, false);
            mesh.SetTriangles(wind.ToArray(), n, false);
            mesh.bounds = src.bounds;
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(CoilWindMesh);
            if (existing) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); mesh = existing; EditorUtility.SetDirty(existing); }
            else AssetDatabase.CreateAsset(mesh, CoilWindMesh);
            smr.sharedMesh = mesh;
            while (mats.Count < n) mats.Add(mats.Count > 0 ? mats[0] : null);
            if (mats.Count > n) mats.RemoveRange(n, mats.Count - n);
            mats.Add(wm); smr.sharedMaterials = mats.ToArray();
            return $"coil windings: {wind.Count / 3} copper tris within {CoilWindRadius} m of the coil cores -> glow slot M_Electro_Glow_Coils_Wind (submesh {n}, texel {mu.x:F3},{mu.y:F3}), mesh {CoilWindMesh}";
        }

        static void FixMats(Renderer r, Material atlas, Dictionary<string, Material> glows)
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                string n = mats[i] ? mats[i].name : "";
                var g = glows.FirstOrDefault(kv => n.StartsWith(kv.Key)).Value;
                if (g) mats[i] = g;
                else if (n.StartsWith("M_BS01_V3") || !mats[i]) mats[i] = atlas;
            }
            r.sharedMaterials = mats;
        }

        static string BuildPrefab(RuntimeAnimatorController ctl, Material atlas, Dictionary<string, Material> glows, float scale)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(Body);
            var wAsset = AssetDatabase.LoadAssetAtPath<GameObject>(Weapon);
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) && !AssetDatabase.CopyAsset($"{Pref}/Z_bs01_grunt.prefab", PrefabPath))
                return "prefab: could not create " + PrefabPath;
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            var log = new List<string>();
            try
            {
                root.name = "Z_bs01_electrobot";
                var kit = root.GetComponent<BS01TypeKit>(); if (kit) { kit.Remove(); Object.DestroyImmediate(kit); log.Add("v1 kit off"); }
                foreach (var c in root.GetComponentsInChildren<BS01KitCable>(true).ToList()) Object.DestroyImmediate(c.gameObject);
                var st = root.GetComponent<BS01Stance>(); if (st) Object.DestroyImmediate(st);
                var old = root.transform.Find("Model");
                int layer = old ? old.gameObject.layer : root.layer;
                var oldAnim = old ? old.GetComponent<Animator>() : null;
                var cull = oldAnim ? oldAnim.cullingMode : AnimatorCullingMode.CullUpdateTransforms;
                if (old) Object.DestroyImmediate(old.gameObject);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                model.name = "Model"; model.transform.SetParent(root.transform, false);
                model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity; model.transform.localScale = Vector3.one * scale;
                float bindFix = OpenZRobotsV3.ToBindPose(model); if (bindFix > 0.001f) log.Add($"bind pose restored ({bindFix:F3} m)");
                foreach (var tr in model.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = layer;
                var an = model.GetComponent<Animator>(); if (!an) an = model.AddComponent<Animator>();
                an.avatar = AssetDatabase.LoadAllAssetsAtPath(Body).OfType<Avatar>().FirstOrDefault();
                an.runtimeAnimatorController = ctl; an.applyRootMotion = false; an.cullingMode = cull;
                foreach (var r in model.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true; FixMats(r, atlas, glows); }
                log.Add(CoilWindGlow(model, glows));
                var socket = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "RightHandWeapon");
                if (!socket) return "prefab: no RightHandWeapon socket";
                var w = (GameObject)PrefabUtility.InstantiatePrefab(wAsset);
                PrefabUtility.UnpackPrefabInstance(w, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                w.name = "BS01_Weapon_Electrobot"; w.transform.SetParent(socket, false);
                w.transform.localPosition = Vector3.zero; w.transform.localRotation = Quaternion.identity; w.transform.localScale = Vector3.one;
                foreach (var tr in w.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = layer;
                foreach (var r in w.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; FixMats(r, atlas, glows); }
                log.Add("weapon sockets " + string.Join(",", w.GetComponentsInChildren<Transform>(true).Where(x => x != w.transform && !x.GetComponent<Renderer>()).Select(x => x.name)));
                var mk = root.GetComponent<RobotV3>(); if (!mk) mk = root.AddComponent<RobotV3>(); mk.type = Type;
                var h = root.GetComponent<WeaponHold>(); if (!h) h = root.AddComponent<WeaponHold>();
                h.leftHang = false; h.leftElbowFromClip = true;                         // two-handed: right fist on the rear loop, left fist on the band's handle
                // the two battery hoses (pack outlets -> gun inlets), runtime cables like the pyro / laser hoses
                var kitMat = AssetDatabase.LoadAssetAtPath<Material>($"{Mat}/M_BS01_Kit.mat");
                int hoses = 0;
                foreach (var k in new[] { "A", "B" })
                {
                    var a = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "Battery_HoseOut_" + k);
                    var b = w.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "Gun_HoseIn_" + k);
                    if (!a || !b) { log.Add("hose " + k + ": sockets missing"); continue; }
                    var go = new GameObject("BS01KitCable_BatteryHose" + k, typeof(MeshFilter), typeof(MeshRenderer)); go.layer = layer;
                    go.transform.SetParent(root.transform, false);
                    var cb = go.AddComponent<BS01KitCable>(); cb.style = BS01KitCable.Style.Hose; cb.points = new[] { a, b };
                    cb.sag = HoseSag; cb.radius = HoseRadius; cb.maxSpan = HoseMaxSpan;
                    var mr = go.GetComponent<MeshRenderer>(); if (kitMat) mr.sharedMaterial = kitMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    hoses++;
                }
                log.Add($"{hoses} battery hoses (BS01KitCable Hose, r {HoseRadius}, sag {HoseSag})");
                if (!root.GetComponent<ElectrobotCoils>()) root.AddComponent<ElectrobotCoils>();
                // the fx package's ElectrobotView (idle electricity, glow slots, barrels, gauge): by name, so this script compiles alone
                var ev = System.Type.GetType("OpenZ.ElectrobotView, Assembly-CSharp");
                if (ev != null) { if (!root.GetComponent(ev)) root.AddComponent(ev); log.Add("ElectrobotView on the prefab"); }
                else log.Add("ElectrobotView: the fx package is not applied (OpenZElectroFx adds it at run time once it is)");
                var tp = root.GetComponent<TeamPaint>(); if (tp) tp.Apply();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                int tris = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Sum(s => s.sharedMesh ? s.sharedMesh.triangles.Length / 3 : 0)
                         + w.GetComponentsInChildren<MeshFilter>(true).Sum(m => m.sharedMesh ? m.sharedMesh.triangles.Length / 3 : 0);
                log.Add($"{tris} tris, Model scale {scale:F4}, controller {(ctl ? ctl.name : "-")}");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return "Z_bs01_electrobot: " + string.Join("; ", log);
        }

        // ------------------------------------------------------------------------------------------------ 2 add to this level
        [MenuItem("OpenZ/Electrobot: 2 Add to this level's ZGame.unitPrefabs (L02, then L17)")]
        public static string AddToLevel()
        {
            var log = new List<string>(); AddToOpenScene(log);
            var game = Object.FindFirstObjectByType<ZGame>();
            if (game) { EditorSceneManager.MarkSceneDirty(game.gameObject.scene); EditorSceneManager.SaveOpenScenes(); }
            return Say(log.Count > 0 ? string.Join("\n", log) : "no ZGame in the open scene (open L02_Desert or L17_CarPark)");
        }

        static void AddToOpenScene(List<string> log)
        {
            var game = Object.FindFirstObjectByType<ZGame>();
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!game || !pf) return;
            var list = (game.unitPrefabs ?? new GameObject[0]).Where(g => g && g.name != "Z_bs01_electrobot").ToList();
            list.Add(pf); game.unitPrefabs = list.ToArray(); EditorUtility.SetDirty(game);
            EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
            log.Add($"{game.gameObject.scene.name}: Z_bs01_electrobot in ZGame.unitPrefabs (run menu 2 in the other level too)");
        }

        // ------------------------------------------------------------------------------------------------ 3 report
        [MenuItem("OpenZ/Electrobot: 3 Report")]
        public static string Report()
        {
            var log = new List<string>();
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!pf) return Say("Z_bs01_electrobot missing (menu 1)");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(pf);
            try
            {
                var an = go.GetComponentInChildren<Animator>(true); var root = go.transform;
                var names = go.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToHashSet();
                string[] want = { "RightHandWeapon", "LeftHandGrip", "CoilMast_L", "CoilMast_R", "Gauge_Needle", "GaugeCentre", "CoilTop_L", "CoilTop_R", "CoilBall_L0", "CoilBall_L1",
                                  "CoilBall_R0", "CoilBall_R1", "CoilCore_L", "CoilCore_R", "BatteryCore", "Battery_HoseOut_A", "Battery_HoseOut_B", "AntennaTip", "Eye_L", "Eye_R",
                                  "BS01_Weapon_Electrobot", "BarrelSpin", "Grip", "Foregrip", "Muzzle", "ElectroMuzzle", "ChargeBall", "EmitterTip", "RingCentre", "CellsCentre",
                                  "GunIntake", "Gun_HoseIn_A", "Gun_HoseIn_B", "Ring_0", "Ring_1", "Ring_2", "Ring_3", "Ring_4", "Ring_5", "Ring_6", "Ring_7" };
                var missing = want.Where(n => !names.Contains(n)).ToList();
                log.Add($"sockets / bones: {want.Length - missing.Count}/{want.Length}" + (missing.Count > 0 ? " MISSING " + string.Join(",", missing) : ""));
                var slots = go.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m).Select(m => m.name).Distinct().ToList();
                log.Add("materials: " + string.Join(", ", slots));
                log.Add($"avatar {(an && an.avatar ? an.avatar.name + (an.avatar.isHuman ? " human" : "") : "-")}, controller {(an && an.runtimeAnimatorController ? an.runtimeAnimatorController.name : "-")}");
                var h = go.GetComponent<WeaponHold>();
                log.Add($"WeaponHold {(h ? (h.leftHang ? "leftHang" : "two-handed") + (h.leftElbowFromClip ? ", left elbow from the clip" : "") : "none")}; hoses {go.GetComponentsInChildren<BS01KitCable>(true).Length}; " +
                        $"ElectrobotCoils {(go.GetComponent<ElectrobotCoils>() ? "yes" : "no")}; ElectrobotView {(go.GetComponents<Component>().Any(c => c && c.GetType().Name == "ElectrobotView") ? "yes" : "no")}");
                // culling box (2.93 m here, the grunt's 2.76 m), not a height, and the old line divided that box by the grunt's GEOMETRY
                // top (2.41) -> a false 1.216 x. The rest height is the bind-pose body mesh (the package's rest_top_world_m method).
                var gpf = AssetDatabase.LoadAssetAtPath<GameObject>($"{Pref}/Z_bs01_grunt.prefab");
                var gg = gpf ? (GameObject)PrefabUtility.InstantiatePrefab(gpf) : null;
                try
                {
                    float eb = GeomTop(go, false), ev = GeomTop(go, true), ec = CullTop(go);
                    float gb = gg ? GeomTop(gg, false) : 2.407f, gv = gg ? GeomTop(gg, true) : 2.470f, gc = gg ? CullTop(gg) : 0f;
                    float rb = eb / gb;
                    log.Add($"rest height (bind-pose body mesh) {eb:F3} m, the grunt's {gb:F3} m: {rb:F3} x (band 1.10-1.15: {(rb >= 1.10f && rb <= 1.15f ? "OK" : "OUT")}; package rest_top_world_m 2.681)");
                    log.Add($"visible top incl. kit pieces {ev:F3} m, the grunt's {gv:F3} m (his kit head): {ev / gv:F3} x");
                    log.Add($"culling box top (SkinnedMeshRenderer.bounds, padded: not a height) {ec:F3} m, the grunt's {gc:F3} m");
                    log.Add("game-hold height: measured live in Play (ElectrobotProof, PlayerPrefs oz_ebproof_auto = size*); package scale test 1.139 x (2.629 m vs 2.309 m)");
                }
                finally { if (gg) Object.DestroyImmediate(gg); }
                log.Add($"BoardingView crew fit: {string.Join(", ", BoardingView.ElectrobotFit.Select(kv => kv.Key + " " + kv.Value))}, default {BoardingView.ElectrobotFitDefault}");
                log.Add($"ragdoll: hips {RobotV3.RagdollRadius(go, "Hips", -1f)}, chest {RobotV3.RagdollRadius(go, "Spine1", -1f)}, head {RobotV3.RagdollRadius(go, "Head", -1f)}");
            }
            finally { Object.DestroyImmediate(go); }
            var ctl = AssetDatabase.LoadAssetAtPath<AnimatorController>(Ctl);
            if (ctl) log.Add("RobotBS01 base states present: " + string.Join(",", States.Where(n => ctl.layers[0].stateMachine.states.Any(s => s.state.name == n))));
            return Say(string.Join("\n", log));
        }

        /// <summary>Geometry top above the root, world m. body: the unskinned (bind-pose) BS01_Body mesh only (the package's Blender
        /// method); visible: every mesh renderer, kit pieces at the prefab's bone pose included.</summary>
        static float GeomTop(GameObject go, bool visible)
        {
            float top = float.NegativeInfinity;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var smr = r as SkinnedMeshRenderer; var mf = r.GetComponent<MeshFilter>();
                Mesh m = smr ? smr.sharedMesh : r is MeshRenderer && mf ? mf.sharedMesh : null;
                if (!m || (!visible && !(smr && r.name == "BS01_Body"))) continue;
                var M = r.transform.localToWorldMatrix;
                foreach (var v in m.vertices) top = Mathf.Max(top, M.MultiplyPoint3x4(v).y);
            }
            return top - go.transform.position.y;
        }
        static float CullTop(GameObject go)
        {
            float top = float.NegativeInfinity;
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) top = Mathf.Max(top, r.bounds.max.y);
            return top - go.transform.position.y;
        }

        // ------------------------------------------------------------------------------------------------ 4 show / 5 crew fit (Play)
        /// <summary>Play: a red Electrobot at the camera focus (view + sim object, like the Play checks), then the fx package's show (3
        /// bolts onto the nearest enemy) when it is in.</summary>
        [MenuItem("OpenZ/Electrobot: 4 Show (Play)")]
        static void Show()
        {
            if (!Application.isPlaying || !ZGame.I) { Debug.LogWarning("[OpenZElectrobot] Show: enter Play on L02_Desert or L17_CarPark first"); return; }
            var g = ZGame.I; var W = g.World;
            var rc = Object.FindFirstObjectByType<RtsCamera>();
            var px = ZGame.WorldToPx(rc ? rc.focus : Vector3.zero);
            int tx = px.x >> 4, ty = px.y >> 4;
            for (int rr = 0; rr < 12; rr++)                                                   // the nearest passable tile round the focus
            {
                bool found = false;
                for (int j = -rr; j <= rr && !found; j++) for (int i = -rr; i <= rr && !found; i++)
                    if (W.Path.TilePassable((px.x >> 4) + i, (px.y >> 4) + j, false)) { tx = (px.x >> 4) + i; ty = (px.y >> 4) + j; found = true; }
                if (found) break;
            }
            var o = W.CreateRobotGroup(Type, tx * 16, ty * 16, Team.Red, 1);
            if (o != null) { var v = g.SpawnUnitView(o); if (v) Selection.activeGameObject = v.gameObject; }
            if (!EditorApplication.ExecuteMenuItem("OpenZ/FX/Electro FX/Show (Play): 3 bolts onto the nearest enemy"))
                Debug.Log("[OpenZElectrobot] Show: Electrobot placed; the fx package's show menu is not there (apply fx/apply.py)");
        }

        [MenuItem("OpenZ/Electrobot: 5 Crew Fit Check (Play)")]
        static void CrewFit()
        {
            if (!Application.isPlaying) { Debug.LogError("[OpenZElectrobot] Crew fit check: enter Play on L02_Desert or L17_CarPark first"); return; }
            CrewSeatCheck.CrewType = Type;
            CrewSeatCheck.Types = new[] { "gun", "gatling", "howitzer", "missile_cannon", "jeep", "light", "medium", "heavy" };
            CrewSeatCheck.Run();
            Debug.Log("[OpenZElectrobot] CrewSeatCheck with the Electrobot: work/records/crew_seat_check.md + work/records/milestones/crew_seats/electrobot_*.png");
        }

        // ------------------------------------------------------------------------------------------------ 9 revert
        [MenuItem("OpenZ/Electrobot: 9 Revert (delete the prefab, states, override, glow materials)")]
        public static string Revert()
        {
            bool p = AssetDatabase.DeleteAsset(PrefabPath), o = AssetDatabase.DeleteAsset(OverridePath);
            int n = 0;
            var ctl = AssetDatabase.LoadAssetAtPath<AnimatorController>(Ctl);
            if (ctl)
            {
                var layers = ctl.layers; var sm = layers[0].stateMachine;
                foreach (var cs in sm.states.ToArray()) if (States.Contains(cs.state.name)) { sm.RemoveState(cs.state); n++; }
                ctl.layers = layers; EditorUtility.SetDirty(ctl);
            }
            foreach (var (slot, _) in Glows) AssetDatabase.DeleteAsset($"{Mat}/M_Electro_Glow_{slot}.mat");
            var game = Object.FindFirstObjectByType<ZGame>();
            if (game && game.unitPrefabs != null) { game.unitPrefabs = game.unitPrefabs.Where(g => g && g.name != "Z_bs01_electrobot").ToArray(); EditorUtility.SetDirty(game); EditorSceneManager.MarkSceneDirty(game.gameObject.scene); }
            AssetDatabase.SaveAssets();
            return Say($"reverted: prefab {p}, override {o}, {n} states removed, glow materials deleted (the FBX / atlas / scripts: model/apply.py --revert)");
        }
    }
}
