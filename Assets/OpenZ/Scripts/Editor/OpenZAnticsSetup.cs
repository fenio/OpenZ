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
    /// Robot antics setup (the runtime is View/Antics + UnitView.Antics.cs):
    ///  1 Import: the antic props (Models/Antics: phone, rod, bobber, toy fish, two sunbed builds and a parasol per team, their atlas and
    ///    emission map) get the details-kit import rule; one TeamLit prop material (atlas x baked vertex AO, the phone screen glowing) and
    ///    a TeamLit can material on the intro rocket-fuel can label; the AnticKit asset in Resources (props, cans, materials, the
    ///    PropSocket_LeftHand pose read from the clips model); the 14 antic states in RobotBS01.controller (the five V3 override
    ///    controllers inherit them and play the grunt clips through the humanoid retarget; the laser's Generic Robot.controller is
    ///    excluded on purpose).
    ///  2 Check: PASS / FAIL per item.
    /// </summary>
    public static class OpenZAnticsSetup
    {
        const string ModelDir = "Assets/OpenZ/Models/Antics", MatDir = "Assets/OpenZ/Materials/Antics", KitPath = "Assets/OpenZ/Resources/OpenZ/Antics/AnticKit.asset";
        const string Clips = "Assets/OpenZ/Models/BS01/Clips_v1/BS01_RobotClips_v1.fbx", Controller = "Assets/OpenZ/Animation/RobotBS01.controller";
        const string CanDir = "Assets/OpenZ/Intro/Models", CanTex = "Assets/OpenZ/Intro/Textures/IC_FuelCan_BaseColor.png";
        static readonly string[] Types = { "Psycho", "Tough", "Sniper", "Pyro", "Electrobot" };
        public static readonly string[] States = { "antic_turn", "antic_scan360", "antic_neck_stretch", "antic_phone", "fish_cast", "fish_hold", "fish_bite", "fish_reel",
                                                   "sunbed_down", "sunbed_lie", "sunbed_up", "drink", "crush", "toss" };
        static readonly Dictionary<string, float> Lengths = new Dictionary<string, float> {
            { "antic_turn", 64 / 24f }, { "antic_scan360", 96 / 24f }, { "antic_neck_stretch", 72 / 24f }, { "antic_phone", 120 / 24f }, { "fish_cast", 36 / 24f },
            { "fish_hold", 48 / 24f }, { "fish_bite", 18 / 24f }, { "fish_reel", 48 / 24f }, { "sunbed_down", 36 / 24f }, { "sunbed_lie", 48 / 24f }, { "sunbed_up", 36 / 24f },
            { "drink", 60 / 24f }, { "crush", 14 / 24f }, { "toss", 16 / 24f } };
        static readonly HashSet<string> Loops = new HashSet<string> { "fish_hold", "fish_reel", "sunbed_lie" };
        static readonly string[] Props = { "ANT_Phone", "ANT_FishingRod", "ANT_Bobber", "ANT_Fish", "ANT_SunbedA_Red", "ANT_SunbedB_Red", "ANT_SunbedA_Blue", "ANT_SunbedB_Blue", "ANT_Parasol_Red", "ANT_Parasol_Blue" };
        static readonly string[] Events = { "rob_antic_can_glug", "rob_antic_can_crush", "rob_antic_can_clatter", "rob_antic_phone", "rob_antic_fish_cast", "rob_antic_fish_reel",
                                            "rob_antic_fish_plop", "rob_antic_fish_splash", "rob_antic_sunbed_creak", "rob_antic_neck_servo", "rob_antic_scan_whirr", "rob_servo", "dst_debris_splash" };

        [MenuItem("OpenZ/Antics/1 Import props + kit + controller states")]
        public static string Import()
        {
            var sb = new StringBuilder("antics import: ");
            // ---- props: the details-kit import rule (metres at scale 1, flat normals as authored, socket empties kept)
            int n = 0;
            foreach (var p in Props)
            {
                var mi = AssetImporter.GetAtPath($"{ModelDir}/{p}.fbx") as ModelImporter;
                if (!mi) { sb.Append($"MISSING {p}.fbx; "); continue; }
                mi.globalScale = 1; mi.useFileUnits = true; mi.bakeAxisConversion = false; mi.importCameras = false; mi.importLights = false;
                mi.meshCompression = ModelImporterMeshCompression.Off; mi.importNormals = ModelImporterNormals.Import; mi.addCollider = false;
                mi.isReadable = false; mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None; mi.preserveHierarchy = true;
                mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importBlendShapes = false; mi.importVisibility = false;
                mi.SaveAndReimport(); n++;
            }
            sb.Append($"{n}/{Props.Length} props; ");
            var atlas = Tex($"{ModelDir}/antics_atlas.png"); var emis = Tex($"{ModelDir}/antics_emission.png");
            var canTex = AssetDatabase.LoadAssetAtPath<Texture2D>(CanTex);
            Directory.CreateDirectory(MatDir); Directory.CreateDirectory(Path.GetDirectoryName(KitPath)); AssetDatabase.Refresh();
            var propMat = Mat("M_Antics_Props", atlas, 0.22f, true);
            if (propMat.HasProperty("_EmissionMap")) { propMat.SetTexture("_EmissionMap", emis); propMat.SetColor("_EmissionMapColor", Color.white * 1.5f); F(propMat, "_EmissionNight", 0f); }
            var canMat = Mat("M_Antics_Can", canTex, 0.45f, false);
            // ---- the kit
            var kit = AssetDatabase.LoadAssetAtPath<AnticKit>(KitPath);
            if (!kit) { kit = ScriptableObject.CreateInstance<AnticKit>(); AssetDatabase.CreateAsset(kit, KitPath); }
            GameObject M(string p) => AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/{p}.fbx");
            kit.phone = M("ANT_Phone"); kit.rod = M("ANT_FishingRod"); kit.bobber = M("ANT_Bobber"); kit.fish = M("ANT_Fish");
            kit.sunbedsRed = new[] { M("ANT_SunbedA_Red"), M("ANT_SunbedB_Red") }.Where(g => g).ToArray();
            kit.sunbedsBlue = new[] { M("ANT_SunbedA_Blue"), M("ANT_SunbedB_Blue") }.Where(g => g).ToArray();
            kit.parasolRed = M("ANT_Parasol_Red"); kit.parasolBlue = M("ANT_Parasol_Blue");
            kit.can = AssetDatabase.LoadAssetAtPath<GameObject>($"{CanDir}/IC_FuelCan.fbx"); kit.canCrushed = AssetDatabase.LoadAssetAtPath<GameObject>($"{CanDir}/IC_FuelCan_Crushed.fbx");
            kit.propMat = propMat; kit.canMat = canTex ? canMat : null;
            var clipModel = AssetDatabase.LoadAssetAtPath<GameObject>(Clips);
            var ps = clipModel ? clipModel.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "PropSocket_LeftHand") : null;
            kit.socketOk = ps && ps.parent && ps.parent.name == "LeftHandGrip";
            if (kit.socketOk) { kit.socketPos = ps.localPosition; kit.socketRot = ps.localRotation; kit.socketScale = ps.localScale; }
            EditorUtility.SetDirty(kit);
            sb.Append($"kit (socket {(kit.socketOk ? "OK" : "MISSING")}); ");
            // ---- the antic states (code cross-fades into them; no transitions), like the grab states
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
            var clips = AssetDatabase.LoadAllAssetsAtPath(Clips).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToDictionary(c => c.name);
            if (ac)
            {
                var sm = ac.layers[0].stateMachine;
                var any = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "grenade_throw") ?? sm.states[0].state;
                int y = 0, made = 0;
                foreach (var s in States)
                {
                    if (!clips.TryGetValue(s, out var clip)) { sb.Append($"MISSING clip {s}; "); continue; }
                    var st = sm.states.Select(x => x.state).FirstOrDefault(x => x.name == s);
                    if (!st) { st = sm.AddState(s, new Vector3(1150, 40 + 60 * y, 0)); made++; }
                    st.motion = clip; st.writeDefaultValues = any.writeDefaultValues; st.speed = 1f; st.iKOnFeet = any.iKOnFeet; y++;
                }
                EditorUtility.SetDirty(ac);
                sb.Append($"{made} new antic states ({States.Length} wired); ");
            }
            else sb.Append("MISSING RobotBS01.controller; ");
            AssetDatabase.SaveAssets();
            Debug.Log("[OpenZ Antics] " + sb);
            return sb.ToString();
        }

        [MenuItem("OpenZ/Antics/2 Check")]
        public static string Check()
        {
            var sb = new StringBuilder(); bool ok = true;
            void R(bool pass, string what) { sb.AppendLine((pass ? "PASS  " : "FAIL  ") + what); ok &= pass; }
            var kit = AssetDatabase.LoadAssetAtPath<AnticKit>(KitPath);
            R(kit, "AnticKit in Resources/OpenZ/Antics");
            if (kit)
            {
                R(kit.HasHandProps, "phone + rocket-fuel can (full + crushed)");
                R(kit.HasFishing, "rod + bobber + toy fish");
                R(kit.HasSunbed, "sunbeds (red " + kit.sunbedsRed.Length + ", blue " + kit.sunbedsBlue.Length + ") + parasols");
                R(kit.propMat && kit.propMat.shader && kit.propMat.shader.name == "OpenZ/TeamLit" && kit.propMat.GetTexture("_BaseMap"), "prop material (TeamLit + atlas)");
                R(kit.canMat && kit.canMat.GetTexture("_BaseMap"), "can material (the rocket-fuel label)");
                R(kit.socketOk, "PropSocket_LeftHand pose from the clips model");
                if (kit.rod) R(kit.rod.GetComponentsInChildren<Transform>(true).Any(t => t.name == "ROD_TIP") && kit.rod.GetComponentsInChildren<Transform>(true).Any(t => t.name == "REEL_HANDLE"), "rod sockets ROD_TIP / REEL_HANDLE");
                if (kit.bobber) R(kit.bobber.GetComponentsInChildren<Transform>(true).Any(t => t.name == "LINE_ATTACH"), "bobber LINE_ATTACH");
                if (kit.phone) R(kit.phone.GetComponentsInChildren<Transform>(true).Any(t => t.name == "GRIP"), "phone GRIP");
            }
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
            R(ac, "RobotBS01.controller");
            var clips = AssetDatabase.LoadAllAssetsAtPath(Clips).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToDictionary(c => c.name);
            if (ac)
            {
                var st = ac.layers[0].stateMachine.states.Select(s => s.state).ToDictionary(s => s.name);
                foreach (var s in States)
                {
                    bool has = st.TryGetValue(s, out var state) && state.motion is AnimationClip c;
                    var clip = has ? (AnimationClip)state.motion : null;
                    R(has && Mathf.Abs(clip.length - Lengths[s]) < 0.05f && clip.isLooping == Loops.Contains(s),
                      $"state {s}: {(has ? clip.length.ToString("F3") + " s, loop " + clip.isLooping : "missing")} (authored {Lengths[s]:F3} s, loop {Loops.Contains(s)})");
                }
            }
            foreach (var t in Types)
            {
                var ov = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>($"Assets/OpenZ/Animation/RobotV3_{t}.overrideController");
                if (!ov) { R(false, $"RobotV3_{t} override"); continue; }
                var list = new List<KeyValuePair<AnimationClip, AnimationClip>>(); ov.GetOverrides(list);
                int seen = list.Count(kv => kv.Key && States.Contains(kv.Key.name));
                R(ov.runtimeAnimatorController == ac && seen == States.Length, $"RobotV3_{t}: base RobotBS01, {seen}/{States.Length} antic clips inherited (humanoid retarget of the grunt clips)");
            }
            var ev = Resources.Load<TextAsset>("OpenZ/Audio/openz_sound_events");
            foreach (var e in Events) R(ev && ev.text.Contains("\"" + e + "\""), "sound event " + e);
            sb.AppendLine("NOTE  the laser (Generic Robot.controller) is excluded on purpose: no humanoid antic clips reach it");
            sb.Insert(0, ok ? "ANTICS CHECK PASS\n" : "ANTICS CHECK FAIL\n");
            Debug.Log("[OpenZ Antics] " + sb);
            return sb.ToString();
        }

        // ---- Play helpers (Play mode): force an antic on the idle robot nearest the screen centre
        [MenuItem("OpenZ/Antics/Play: drink rocket fuel (nearest centre)")] static void PDrink() => Play("Drink");
        [MenuItem("OpenZ/Antics/Play: phone (nearest centre)")] static void PPhone() => Play("Phone");
        [MenuItem("OpenZ/Antics/Play: fishing (nearest centre)")] static void PFish() => Play("Fish");
        [MenuItem("OpenZ/Antics/Play: sunbed (nearest centre)")] static void PSun() => Play("Sunbed");
        [MenuItem("OpenZ/Antics/Play: turn (nearest centre)")] static void PTurn() => Play("Turn");
        [MenuItem("OpenZ/Antics/Play: scan 360 (nearest centre)")] static void PScan() => Play("Scan");
        [MenuItem("OpenZ/Antics/Play: neck stretch (nearest centre)")] static void PNeck() => Play("Neck");
        [MenuItem("OpenZ/Antics/Play: report + log")] static void PReport() => Debug.Log("[OpenZ Antics] " + AnticDirector.Report() + "\n" + AnticDirector.LastLog(40));
        static void Play(string k) { if (!Application.isPlaying) { Debug.LogWarning("[OpenZ Antics] Play mode only"); return; } Debug.Log("[OpenZ Antics] " + AnticDirector.ForceNearCentre(k)); }

        static Texture2D Tex(string path)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti)
            {
                ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Bilinear; ti.anisoLevel = 4; ti.maxTextureSize = 1024; ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>The details-kit TeamLit setup (OpenZDetailsV2.Mat): no team mode, stylised ramp, soft fill.</summary>
        static Material Mat(string name, Texture2D tex, float smooth, bool vcol)
        {
            string p = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(Shader.Find("OpenZ/TeamLit")); AssetDatabase.CreateAsset(m, p); }
            m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Color.white);
            F(m, "_UseVertexColor", vcol ? 1 : 0); F(m, "_VertexColorSRGB", 1); F(m, "_TeamMode", 0); F(m, "_Accent", 0);
            F(m, "_Degreen", 0); F(m, "_Triplanar", 0); F(m, "_TopBlend", 0); F(m, "_Stylize", 1); F(m, "_Degrime", 0); F(m, "_FlatShade", 0);
            F(m, "_Smoothness", smooth); F(m, "_Metallic", 0);
            if (m.HasProperty("_RimColor")) m.SetColor("_RimColor", new Color(0.10f, 0.09f, 0.08f));
            F(m, "_RimPower", 3.2f); F(m, "_SoftFill", 0.62f); F(m, "_AmbientBoost", 1.25f);
            if (m.HasProperty("_KeyWarm")) m.SetColor("_KeyWarm", new Color(1.06f, 1f, 0.92f));
            if (m.HasProperty("_DarkColor")) m.SetColor("_DarkColor", Color.black);
            F(m, "_DarkThreshold", 0.12f); F(m, "_SatScale", 1f); F(m, "_ValScale", 1f); F(m, "_HueShift", 0f); F(m, "_NightClass", 3f); F(m, "_SeeThru", 0f);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            m.enableInstancing = true; EditorUtility.SetDirty(m);
            return m;
        }
        static void F(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }
    }
}
