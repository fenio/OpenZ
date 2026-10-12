// OpenZ (GPL-3.0) - Commander Zod cameo: the little studio Zod talks from. A 3D set far above and away from the map
// (Origin, 1.8 km up: the atmosphere's height fog is zero there and no game camera ever looks at it) rendered by its own
// camera into a RenderTexture that ZodComms shows in the kit comms window.
//  - Zod: Resources/OpenZ/Zod/ZodCameo.prefab (built by OpenZ > Zod Cameo > Setup: FBX + Animator controller + ZodTalkRig), or
//    the bare FBX with a Playables mixer when the setup was not run yet.
//  - Isolation: everything sits on the "ZodCameo" layer (the stage camera renders only that layer, every other camera drops it)
//    and on rendering layer 7 (URP rendering layers are on in PC_RPAsset): the level's sun, lamps and explosion lights use
//    rendering layer 0 only, so they never light Zod, and the stage lights (rendering layer 7) never light the level.
//  - Look: warm key spot with soft shadows (hat brim shadow over the shades), cool fill, warm rim, a wall wash, a practical
//    lamp, a glowing tactical board with the level's map, a red-force banner, an amber console strip; its own global Volume
//    on the ZodCameo layer (ACES, bloom for the cigar ember, vignette, gentle depth of field on the back wall).
//  - Cigar smoke rises from the CigarTip socket (world space, unscaled time); cigar_puff glows the ember and exhales a cloud.
//  - Transmission camera: a hand-held field camera. Continuous organic shake and sway (layered noise on position and
//    rotation, a footstep bob, the operator re-framing every few seconds), quick zoom PUNCHES on stressed words (an under-damped
//    field-of-view spring: snap in, a small overshoot, hold, ease back; the aim slides up to his face), a PULL-OUT on the final
//    order, and KICKS (a short high-frequency jolt) on punches, the fist and static bursts. Reduced motion keeps 15 % of it.
//    It runs on the stage clock, so it freezes with the game's pause like the rest of the performance.
//  - The final order: Thrust() drives the right arm on top of the animation (LateUpdate, transforms only, so it works with the
//    Humanoid or the Generic rig): the fist jabs straight at the lens, elbow out and down, wrist straight, chest leaning in.
//    A two-bone IK that bends the elbow only around its own hinge (cached from the animated arm), then aims and swivels the
//    whole arm; tools/thrust_check.py runs the same maths on the FBX in Blender (qc/thrust/*.png).
//  - Humanoid guard: salute, laugh and cigar_puff were keyed in Blender with elbow / wrist rotations a Humanoid avatar cannot
//    express (qc/humanoid_clip_check.json: the salute holds the forearm 35 deg off the elbow hinge with a 92 deg wrist bend;
//    Unity throws that away = the "broken arm" of the v1 salute). With a Humanoid avatar SafeClip() swaps them for imported clips;
//    the Generic rig (OpenZ > Zod Cameo > Setup (Generic rig)) plays them as keyed.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenZ
{
    [DefaultExecutionOrder(160)]
    public sealed partial class ZodStage : MonoBehaviour
    {
        public const string CurvesPath = "OpenZ/Zod/zod_mouth_curves";
        /// <summary>
        /// ZodCameo_v3 + v3/Zod_v3). Menu OpenZ/Zod/Use v1 / Use v2 / Use v3; a missing v2 / v3 prefab falls back to v1 with one warning.</summary>
        public const string PrefModel = "oz_zod_model";
        public const string PrefabPathV1 = "OpenZ/Zod/ZodCameo", ModelPathV1 = "OpenZ/Zod/Zod_Cameo", PrefabPathV2 = "OpenZ/Zod/ZodCameo_v2", ModelPathV2 = "OpenZ/Zod/v2/Zod_v2";
        /// <summary>
        /// gear (sheriff star, v1's chevron, the shell bandolier). Every v1 table (fist hull, palm axis, head clamps, bronze prop fist,
        /// face rig) applies to it: only ModelVersion == 2 takes v2's.</summary>
        public const string PrefabPathV3 = "OpenZ/Zod/ZodCameo_v3", ModelPathV3 = "OpenZ/Zod/v3/Zod_v3";
        /// <summary>One-time migration flag: a stored oz_zod_model (1 or 2, from the v2 import, a take or Use v1) is cleared once so v3 becomes
        /// the default in the editor and in every build (they read the same key); Use v1 / v2 / v3 after that sticks.</summary>
        public const string PrefModelMigrated = "oz_zod_model_v3default";
        /// <summary>Proof / capture only, session-only (never saved): 1 / 2 / 3 builds that model whatever the setting is; 0 = the setting.</summary>
        public static int ForceModel;
        static bool modelPrefMigrated;
        public static int ModelWanted
        {
            get
            {
                if (ForceModel >= 1 && ForceModel <= 3) return ForceModel;
                MigrateModelPref();
                int v = PlayerPrefs.GetInt(PrefModel, 3);
                return v == 1 || v == 2 ? v : 3;
            }
        }
        static void MigrateModelPref()
        {
            if (modelPrefMigrated) return;
            modelPrefMigrated = true;
            if (PlayerPrefs.GetInt(PrefModelMigrated, 0) == 1) return;
            bool had = PlayerPrefs.HasKey(PrefModel); int old = had ? PlayerPrefs.GetInt(PrefModel, 0) : 0;
            if (had) PlayerPrefs.DeleteKey(PrefModel);
            PlayerPrefs.SetInt(PrefModelMigrated, 1); PlayerPrefs.Save();
            Debug.Log("[ZodCameo] model setting migrated once (Zod v3): " + PrefModel + " was " + (had ? old.ToString() : "unset") + " -> cleared, default v3");
        }
        /// <summary>The setting as read (for the stage-built log).</summary>
        public static string ModelPrefState => (ForceModel >= 1 && ForceModel <= 3 ? "proof force v" + ForceModel + ", " : "")
                                                + (PlayerPrefs.HasKey(PrefModel) ? PrefModel + " = " + PlayerPrefs.GetInt(PrefModel, 0) : PrefModel + " unset (default v3)");
        public static string PrefabPath => PrefabPathFor(ModelWanted);
        public static string ModelPath => ModelPathFor(ModelWanted);
        static string PrefabPathFor(int v) => v == 2 ? PrefabPathV2 : v == 3 ? PrefabPathV3 : PrefabPathV1;
        static string ModelPathFor(int v) => v == 2 ? ModelPathV2 : v == 3 ? ModelPathV3 : ModelPathV1;
        /// <summary>The model this stage runs (1 / 2 / 3; 2 or 3 only when that prefab or FBX loaded).</summary>
        public int ModelVersion { get; private set; } = 1;
        public bool ModelV2 => ModelVersion == 2;
        static bool v2MissingWarned, v3MissingWarned;
        public const string MatPath = "OpenZ/Zod/Materials/", SmokeTexPath = "OpenZ/Zod/textures/zod_smoke";
        public const string LayerName = "ZodCameo";
        /// <summary>URP rendering layer of the stage ("Light Layer 7"): lights and renderers of the set only.</summary>
        public const int LightLayerBit = 7;
        /// <summary>
        /// render on this bit ONLY; every stage light reaches it (StageLightMask) except the red ones (alarm wash, beacon spots, the
        /// button lamp: NoBezel), so the rings stay black rubber under the red wash.</summary>
        public const int BezelLightBit = 4;
        public const uint StageLightMask = (1u << LightLayerBit) | (1u << BezelLightBit);
        /// <summary>
        /// The cigar ember point light reaches this rendering layer ONLY. Zod's body, face, cigar and mouth renderers carry it next
        /// to the stage bit; v3's gear renderer (GearSkinName: shells, belt, buckle, sheriff star, split off in ZodCameoSetup.RunV3) does
        /// not, so the ember can never light the bandolier, whatever the pose. Light Layer 5: defined, otherwise unused (0 Default,
        /// 1 Character, 2 Boss, 4 bezel, 6 console button, 7 stage).</summary>
        public const int EmberLightBit = 5;
        /// <summary>v3's gear SkinnedMeshRenderer (ZodCameoSetup.SplitGearV3): stage lights yes, the ember light no.</summary>
        public const string GearSkinName = "Zod3_GearSkin";
        /// <summary>The ember light sits this far past the ember disc along the cigar (was: the tip + 3 cm toward the lens, which put it
        /// in the shell row when he bowed).</summary>
        public const float EmberOut = 0.004f;
        /// <summary>A red light: the stage (bit 7) only, never the bezel bit.</summary>
        static void NoBezel(Light l)
        {
            if (!l) return;
            l.renderingLayerMask = 1 << LightLayerBit;
            var ld = l.GetUniversalAdditionalLightData();
            if (ld) { ld.renderingLayers = 1u << LightLayerBit; ld.shadowRenderingLayers = 1u << LightLayerBit; }
        }
        public static readonly Vector3 Origin = new Vector3(-5200f, 1800f, -5200f);

        [Header("Lights (URP intensities for the ~2.7 m framing; sun of the level = 1.9)")]
        public float keyIntensity = 12f;
        public float fillIntensity = 3.2f, rimIntensity = 20f, wallIntensity = 5f, lampIntensity = 2.6f, bounceIntensity = 1.6f, emberIntensity = 0.5f;
        public Color keyColor = new Color(1f, 0.80f, 0.60f), fillColor = new Color(0.62f, 0.72f, 1f), rimColor = new Color(1f, 0.66f, 0.40f),
                     wallColor = new Color(1f, 0.60f, 0.28f);
        [Header("Framing")]
        [Tooltip("Camera yaw around Zod (deg, + = his left side)")] public float yawDeg = 14f;
        [Tooltip("Camera elevation (dir.y): negative = a slightly low, heroic angle (a low shot)")] public float elevation = -0.12f;
        public float fov = 28f;
        [Tooltip("Hand-held field camera (shake, sway, re-framing); reduced motion keeps 15 %")] public bool handheld = true;
        [Header("Transmission camera (v2)")]
        [Tooltip("Hand-held amount (1 = the tuned look)")] [Range(0f, 2f)] public float handheldAmount = 1f;
        [Tooltip("Zoom punch: field of view multiplier at strength 1 (0.62 = 28 -> 17 deg, the face fills the feed)")] public float punchFov = 0.62f;
        [Tooltip("Pull-out on the final order: field of view multiplier (1.32 = 28 -> 37 deg)")] public float pullFov = 1.32f;
        [Tooltip("Aim height during a punch (m above the stage floor: his face under the hat brim)")] public float punchLook = 2.03f;
        [Tooltip("Aim height during the pull-out (room for the fist)")] public float pullLook = 1.80f;
        [Header("Fist at the camera (final order, v2)")]
        [Tooltip("Arm reach toward the lens while the thrust holds (share of the arm length)")] public float thrustReach = 0.86f;
        [Tooltip("Extra reach of each jab (one per GO!)")] public float thrustJab = 0.11f;
        [Tooltip("Upper chest leaning toward the camera during the thrust (deg)")] public float thrustLean = 7f;
        [Tooltip("The fist aims this far to his right of the lens (m): it lands beside his face, never over his mouth")] public float thrustAside = 0.4f;

        public RenderTexture Feed { get; private set; }
        public bool HasModel => model != null;
        public ZodTalkRig Rig => rig;
        public float Mouth01 => rig ? rig.Mouth01 : 0f;
        public int Layer { get; private set; }
        public string BoardLevel { get; private set; }
        public string CurrentClip { get; private set; }
        public string Bed { get; private set; } = "idle";
        public bool Live => cam && cam.enabled;

        Transform model, cigarTip;
        Animator anim; ZodTalkRig rig; Puppet puppet;
        Camera cam; Volume vol; DepthOfField dof;
        Light key, fill, rim, wall, lamp, bounce, ember;
        ParticleSystem smoke;
        Renderer boardScreen; Material boardMat;
        float clock, oneShotUntil = -1f, clipStart, puffAt = -1f, exhaleAt = -1f, puffT, chatterUntil = -1f, nextCamScan;
        float dist = 2.2f, lookY = 1.86f, distVel, lookVel, wantDist = 2.2f, wantLook = 1.86f, push;
        bool frozen;
        // v2 transmission camera (stage clock)
        float fovMul = 1f, fovVel, punchUntil = -1f, punchK = 1f, pullUntil = -1f, lookBias, lookBiasVel, kick, reframeAt, reframeT0;
        Vector2 reframeFrom, reframeTo, reframeCur;
        // v2 fist at the camera
        Transform uArm, fArm, hand, chest; Quaternion handRest = Quaternion.identity; Vector3 hingeLocal; bool hingeOk;
        float thrustUntil = -1f, jabAt = -10f, thrustW;
        readonly HashSet<string> swapped = new HashSet<string>();
        /// <summary>v2: clips keyed in Blender that a Humanoid avatar breaks (elbow off its hinge, wrist past its limit), and the
        /// imported clip that stands in for each while the avatar is Humanoid (qc/humanoid_clip_check.json).</summary>
        public static readonly Dictionary<string, string> HumanoidUnsafe = new Dictionary<string, string>
        {
            ["salute"] = "fist_pump", ["laugh"] = "chest_pound", ["cigar_puff"] = "idle_hand_on_hip",
        };
        static readonly Dictionary<string, Texture2D> boardTex = new Dictionary<string, Texture2D>();

        // ------------------------------------------------------------------------------------------------ clips
        /// <summary>Length (s) and loop flag of every clip of Zod_Cameo.fbx (model README), used when the Animator cannot tell.</summary>
        public static readonly Dictionary<string, (float len, bool loop)> ClipInfo = new Dictionary<string, (float, bool)>
        {
            ["idle"] = (4.0f, true), ["idle_hand_on_hip"] = (5.0f, true), ["idle_look_around"] = (4.0f, true), ["talk_passionate"] = (10.3f, true),
            ["talk_hands_open"] = (4.0f, true), ["talk_right_hand"] = (3.8f, true), ["talk_left_hand"] = (4.7f, false), ["talk_angry"] = (20.8f, true),
            ["point_map"] = (2.1f, false), ["salute"] = (1.8f, false), ["cigar_puff"] = (4.0f, true), ["hat_tip"] = (8.9f, true), ["laugh"] = (3.0f, false),
            ["fist_pump"] = (1.6f, false), ["victory_cheer"] = (9.4f, false), ["chest_pound"] = (4.9f, false), ["facepalm"] = (4.9f, false),
            ["angry_stomp"] = (8.0f, false), ["finger_wag"] = (5.0f, true), ["scheming"] = (3.3f, true), ["crouch_gun_gesture"] = (6.2f, true),
            ["jaw_chatter"] = (2.0f, true), ["walk"] = (1.1f, true), ["run"] = (0.7f, true),
        };
        /// <summary>Framing a clip asks for (arms up / leaning back need room); the director may override with fx "framing:x".</summary>
        static readonly Dictionary<string, string> ClipFraming = new Dictionary<string, string>
        {
            ["fist_pump"] = "wide", ["victory_cheer"] = "wide", ["laugh"] = "wide", ["angry_stomp"] = "wide", ["chest_pound"] = "wide",
            ["point_map"] = "medium", ["salute"] = "medium", ["hat_tip"] = "medium", ["cigar_puff"] = "tight", ["scheming"] = "medium",
        };
        static readonly Dictionary<string, (float dist, float look)> Framings = new Dictionary<string, (float, float)>
        {
            ["tight"] = (1.75f, 2.00f), ["medium"] = (2.20f, 1.86f), ["wide"] = (3.00f, 1.74f),     // head+shoulders / to the chest plate / arms up
        };
        string lineFraming = "medium";

        // ================================================================================================ build
        public static ZodStage Create(Transform owner, string level)
        {
            var go = new GameObject("ZodCameoStage");
            if (owner) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, owner.gameObject.scene);
            go.transform.position = Origin;
            var s = go.AddComponent<ZodStage>();
            s.Build(level);
            return s;
        }

        /// <summary>The ZodCameo layer (OpenZ > Zod Cameo > Setup names it); before the setup: the highest unnamed layer that neither
        /// ZGame's walk layer (30) nor BuildingBreaker's debris layer (31) takes.</summary>
        public static int ResolveLayer()
        {
            int l = LayerMask.NameToLayer(LayerName);
            if (l >= 0) return l;
            for (int i = 29; i >= 8; i--)
                if (i != ZGame.WalkLayer && string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
            return 29;
        }

        void Build(string level)
        {
            Layer = ResolveLayer();
            gameObject.layer = Layer;
            BuildModel();
            BuildSet();
            SetBoardLevel(level);
            BuildLights();
            BuildSmoke();
            BuildCamera();
            BuildBoys();                                                                                  // zod_briefings_v3: the two red soldiers
            Debug.Log($"[ZodCameo] stage built: model v{ModelVersion} {(model ? (puppet is ControllerPuppet ? "prefab + Animator controller" : "FBX + Playables fallback (run OpenZ > Zod Cameo > Setup)") : "MISSING (audio + subtitles only)")}, "
                      + $"layer {Layer} ({LayerMask.LayerToName(Layer)}), rendering layer {LightLayerBit}, board {level}");
        }

        void BuildModel()
        {
            ModelVersion = ModelWanted; string prefabPath = PrefabPathFor(ModelVersion), modelPath = ModelPathFor(ModelVersion);
            var src = Resources.Load<GameObject>(prefabPath);
            if (!src && ModelVersion != 1)
            {
                bool v3 = ModelVersion == 3;
                if (v3 ? !v3MissingWarned : !v2MissingWarned)
                {
                    if (v3) v3MissingWarned = true; else v2MissingWarned = true;
                    Debug.LogWarning("[ZodCameo] Resources/" + prefabPath + ".prefab missing: falling back to v1 (run OpenZ > Zod Cameo > Setup " + (v3 ? "v3" : "v2") + ")");
                }
                ModelVersion = 1; prefabPath = PrefabPathV1; modelPath = ModelPathV1;
                src = Resources.Load<GameObject>(prefabPath);
            }
            Debug.Log("[ZodCameo] model setting: " + ModelPrefState + " -> ModelVersion " + ModelVersion + " (" + prefabPath + ")");
            bool isPrefab = src != null;
            if (!src)
            {
                src = Resources.Load<GameObject>(modelPath);
                if (src) Debug.LogWarning("[ZodCameo] Resources/" + prefabPath + ".prefab missing: using the bare FBX (run OpenZ > Zod Cameo > Setup)");
            }
            hullNow = ModelVersion == 2 ? FistHullV2 : FistHull; handBindRead = false;
            if (!src) { Debug.LogWarning("[ZodCameo] no Zod model in Resources/OpenZ/Zod (README_IMPORT step 1): comms window shows NO SIGNAL"); return; }
            ApplyFaceRig(src.name);
            var go = Instantiate(src, transform);
            go.name = "Zod";
            modelKey = src.name;
            model = go.transform; model.localPosition = Vector3.zero; model.localRotation = Quaternion.identity; model.localScale = Vector3.one;
            anim = go.GetComponentInChildren<Animator>();
            if (!anim) anim = go.AddComponent<Animator>();
            anim.updateMode = AnimatorUpdateMode.UnscaledTime; anim.cullingMode = AnimatorCullingMode.AlwaysAnimate; anim.applyRootMotion = false;
            if (anim.runtimeAnimatorController) puppet = new ControllerPuppet(anim);
            else
            {
                var clips = new List<AnimationClip>();
                foreach (var c in Resources.LoadAll<AnimationClip>(modelPath)) if (c && !c.name.StartsWith("__preview__")) clips.Add(c);
                puppet = new PlayablesPuppet(anim, clips);
            }
            rig = anim.GetComponent<ZodTalkRig>();
            if (!rig) rig = anim.gameObject.AddComponent<ZodTalkRig>();
            if (!rig.mouthCurves) rig.SetCurves(Resources.Load<TextAsset>(CurvesPath));
            ZodTalkRig.MaxChinDownDeg = ModelVersion == 2 ? 4f : 16f; ZodTalkRig.MaxRollDeg = ModelVersion == 2 ? 8f : 14f;
            baseChinDown = ZodTalkRig.MaxChinDownDeg; ZodTalkRig.ChinSoftKeep = ZodTalkRig.SoftKeep; clipHeadW = 0f; clipHeadNow = null;
            cigarTip = Find(model, "CigarTip");
            uArm = Find(model, "mixamorigRightArm"); fArm = Find(model, "mixamorigRightForeArm"); hand = Find(model, "mixamorigRightHand");
            chest = Find(model, "mixamorigSpine2");
            if (hand) handRest = hand.localRotation;                                                    // the bind pose: a straight wrist
            if (!uArm || !fArm || !hand) Debug.LogWarning("[ZodCameo] right arm bones not found: no fist at the camera (the rest works)");
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.renderingLayerMask = r.name == GearSkinName ? 1u << LightLayerBit : (1u << LightLayerBit) | (1u << EmberLightBit);
                r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true;
                r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            Play("idle", 0f);
            if (!isPrefab) Debug.Log("[ZodCameo] fallback puppet clips: " + (puppet as PlayablesPuppet)?.Count);
        }

        // ------------------------------------------------------------------------------------------------ materials
        // template copies are DontSave (= DontUnloadUnusedAsset), so UnloadUnusedAssets never freed them after a briefing / ending; every
        // copy is listed here and destroyed when the last stage goes (OnDestroy). Same look, nothing shared outside the stage.
        static readonly List<Material> madeMats = new List<Material>();
        static Material Template(string name, string shader)
        {
            var t = Resources.Load<Material>(MatPath + name);
            Material m = null;
            if (t) m = new Material(t) { hideFlags = HideFlags.DontSave };
            else { var sh = Shader.Find(shader); if (sh) m = new Material(sh) { hideFlags = HideFlags.DontSave }; }
            if (m) madeMats.Add(m);
            return m;
        }

        static Material Lit(Color32 c, float smooth = 0.3f, float metal = 0.15f)
        {
            var m = Template("ZodStage_Lit", "Universal Render Pipeline/Lit");
            if (!m) return null;
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            return m;
        }

        /// <summary>Emissive Lit: linear HDR emission through SetVector (SetColor would gamma-convert the HDR value).</summary>
        static Material Glow(Color32 baseC, Color linear, float k)
        {
            var m = Template("ZodStage_Glow", "Universal Render Pipeline/Lit");
            if (!m) return null;
            m.SetColor("_BaseColor", baseC); m.SetFloat("_Smoothness", 0.4f); m.SetFloat("_Metallic", 0f);
            m.EnableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.SetVector("_EmissionColor", (Vector4)(linear * k));
            return m;
        }

        Renderer Box(string name, Vector3 pos, Vector3 size, Material m, bool cast = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false); go.transform.localPosition = pos; go.transform.localScale = size;
            return Dress(go, m, cast);
        }

        Renderer Quad(string name, Vector3 pos, Vector2 size, Material m, float yawDeg = 180f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name; Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false); go.transform.localPosition = pos; go.transform.localRotation = Quaternion.Euler(0, yawDeg, 0);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            return Dress(go, m, false);
        }

        Renderer Dress(GameObject go, Material m, bool cast)
        {
            go.layer = Layer;
            var r = go.GetComponent<Renderer>();
            if (m) r.sharedMaterial = m;
            r.renderingLayerMask = 1u << LightLayerBit;
            r.shadowCastingMode = cast ? ShadowCastingMode.On : ShadowCastingMode.Off; r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.Off;
            return r;
        }

        /// <summary>The set: gunmetal wall with steel ribs, olive console wainscot with a glowing amber strip, the tactical board
        /// (level map) on Zod's left, the red-force banner on his right, an off-screen work lamp. Zod faces +Z; the camera sits at +Z
        /// slightly to his left, so the wall behind him shows mostly at -X (screen right) and a strip at +X (screen left).</summary>
        void BuildSet()
        {
            var gm = Lit(new Color32(40, 45, 42, 255), 0.32f, 0.35f);
            var rib = Lit(new Color32(58, 64, 60, 255), 0.45f, 0.6f);
            var olive = Lit(new Color32(66, 71, 52, 255), 0.25f, 0.2f);
            var dark = Lit(new Color32(20, 23, 21, 255), 0.4f, 0.4f);
            Box("Wall", new Vector3(0f, 2.1f, -2.8f), new Vector3(8f, 4.8f, 0.2f), gm);
            for (int i = -3; i <= 3; i++) Box("Rib" + i, new Vector3(i * 1.3f + 0.35f, 2.1f, -2.66f), new Vector3(0.1f, 4.8f, 0.08f), rib);
            Box("Wainscot", new Vector3(0f, 0.62f, -2.6f), new Vector3(8f, 1.24f, 0.22f), olive);
            Box("ConsoleLip", new Vector3(0f, 1.25f, -2.52f), new Vector3(8f, 0.05f, 0.12f), dark);
            Box("AmberStrip", new Vector3(0f, 1.215f, -2.47f), new Vector3(8f, 0.022f, 0.02f), Glow(new Color32(60, 40, 10, 255), new Color(1f, 0.55f, 0.16f), 2.4f));
            // tactical board (screen right)
            Box("BoardFrame", new Vector3(-1.95f, 2.0f, -2.62f), new Vector3(1.28f, 1.52f, 0.07f), dark);
            boardMat = Glow(new Color32(6, 8, 7, 255), new Color(0.85f, 0.95f, 0.85f), 0.9f);
            boardScreen = Quad("BoardScreen", new Vector3(-1.95f, 2.0f, -2.583f), new Vector2(1.16f, 1.40f), boardMat);
            Box("BoardLampBar", new Vector3(-1.95f, 2.8f, -2.52f), new Vector3(1.0f, 0.04f, 0.1f), Glow(new Color32(40, 30, 10, 255), new Color(1f, 0.7f, 0.35f), 1.6f));
            // red-force banner (screen left)
            Box("BannerRod", new Vector3(1.55f, 2.98f, -2.62f), new Vector3(1.05f, 0.035f, 0.035f), rib);
            Quad("Banner", new Vector3(1.55f, 2.18f, -2.64f), new Vector2(0.86f, 1.6f), Lit(TeamColors.Swap ? new Color32(30, 52, 150, 255) : new Color32(150, 34, 26, 255), 0.18f, 0f));
            var emblemTex = OzKit.GetTexture("Bits/emblem_" + TeamColors.Key(Team.Red) + "_lg");
            if (emblemTex)
            {
                var em = Template("ZodStage_Cutout", "Universal Render Pipeline/Lit");
                if (em)
                {
                    em.SetTexture("_BaseMap", emblemTex); em.SetColor("_BaseColor", Color.white); em.SetFloat("_Smoothness", 0.3f);
                    em.SetFloat("_AlphaClip", 1f); em.SetFloat("_Cutoff", 0.4f); em.EnableKeyword("_ALPHATEST_ON"); em.renderQueue = 2450;
                    Quad("BannerEmblem", new Vector3(1.55f, 2.42f, -2.628f), new Vector2(0.56f, 0.56f), em);
                }
            }
            // (the work lamp is a light only: its bulb read as a white dot in the corner of every framing)
        }

        /// <summary>The tactical board shows the level's map (OzLevelInfo's baked minimap, the same art as the level-select inset).</summary>
        public void SetBoardLevel(string level)
        {
            if (string.IsNullOrEmpty(level)) level = "L02";
            if (level == BoardLevel || !boardMat) return;
            BoardLevel = level;
            Texture2D tex = null;
            var g = ZGame.I;
            if (g && g.World != null && g.level == level) tex = BakeLive(g.World, level);                 // in a level: the live map (no second ZWorld)
            if (!tex && (!boardTex.TryGetValue(level, out tex) || !tex))
            {   // front end: OzLevelInfo builds the map once (cached for the session)
                try { tex = OzLevelInfo.Load(level)?.Mini; } catch (Exception e) { Debug.LogWarning("[ZodCameo] board map " + level + ": " + e.Message); tex = null; }
                boardTex[level] = tex;
            }
            boardMat.SetTexture("_EmissionMap", tex); boardMat.SetTexture("_BaseMap", tex);
            if (tex && boardScreen)
            {
                float h = 1.40f, w = Mathf.Clamp(h * tex.width / Mathf.Max(1f, tex.height), 0.8f, 1.9f);
                boardScreen.transform.localScale = new Vector3(w, h, 1f);
            }
        }

        static System.Reflection.MethodInfo bakeMethod; static bool bakeLooked;
        /// <summary>OzLevelInfo's minimap bake (the level-select inset art) on the running world: cheap, current zone owners.</summary>
        static Texture2D BakeLive(OpenZ.Sim.ZWorld w, string level)
        {
            if (!bakeLooked)
            {
                bakeLooked = true;
                bakeMethod = typeof(OzLevelInfo).GetMethod("Bake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            }
            try { return bakeMethod?.Invoke(null, new object[] { w, level }) as Texture2D; }
            catch (Exception e) { Debug.LogWarning("[ZodCameo] live board bake: " + (e.InnerException ?? e).Message); return null; }
        }

        Light AddLight(string name, LightType type, Vector3 pos, Vector3 aim, Color c, float intensity, float range, float angle = 50f, bool shadows = false)
        {
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetParent(transform, false); go.transform.localPosition = pos;
            if (aim != pos) go.transform.rotation = Quaternion.LookRotation(transform.TransformPoint(aim) - transform.TransformPoint(pos));
            var l = go.AddComponent<Light>();
            l.type = type; l.color = c; l.intensity = intensity; l.range = range;
            if (type == LightType.Spot) { l.spotAngle = angle; l.innerSpotAngle = angle * 0.55f; }
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            if (shadows) { l.shadowStrength = 0.85f; l.shadowBias = 0.02f; l.shadowNormalBias = 0.25f; l.shadowNearPlane = 0.2f; }
            l.renderingLayerMask = (int)StageLightMask;
            var ld = l.GetUniversalAdditionalLightData();
            if (ld) { ld.renderingLayers = StageLightMask; ld.shadowRenderingLayers = StageLightMask; }
            return l;
        }

        void BuildLights()
        {
            // face (-1.3, 2.35, 2.3), 12, (1, 0.80, 0.60) blew the face out to flat yellow
            BuildFaceLights();
            wall = AddLight("WallWash", LightType.Point, new Vector3(-0.6f, 1.2f, -1.9f), new Vector3(-0.6f, 1.2f, -1.9f), wallColor, wallIntensity, 3.4f);
            lamp = AddLight("Lamp", LightType.Point, new Vector3(0.95f, 2.5f, -1.2f), new Vector3(0.95f, 2.5f, -1.2f), new Color(1f, 0.76f, 0.48f), lampIntensity, 2.8f);
            ember = AddLight("Ember", LightType.Point, new Vector3(0f, 2f, 0.3f), new Vector3(0f, 2f, 0.3f), new Color(1f, 0.42f, 0.1f), emberIntensity, FaceLights.emberRange);
            ember.renderingLayerMask = 1 << EmberLightBit;
            var emd = ember.GetUniversalAdditionalLightData();
            if (emd) { emd.renderingLayers = 1u << EmberLightBit; emd.shadowRenderingLayers = 1u << EmberLightBit; }
        }

        // ------------------------------------------------------------------------------------------------ smoke
        void BuildSmoke()
        {
            var go = new GameObject("CigarSmoke") { layer = Layer };
            go.transform.SetParent(cigarTip ? cigarTip : transform, false);
            if (!cigarTip) go.transform.localPosition = new Vector3(-0.08f, 2.0f, 0.24f);
            smoke = go.AddComponent<ParticleSystem>();
            smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = smoke.main;
            main.duration = 5f; main.loop = true; main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.80f, 0.76f, 0.71f, 0.42f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true; main.maxParticles = 160; main.gravityModifier = -0.015f; main.scalingMode = ParticleSystemScalingMode.Shape;   // the socket's FBX scale never shrinks the smoke
            var em = smoke.emission; em.rateOverTime = 7f;
            var sh = smoke.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 8f; sh.radius = 0.004f;
            sh.rotation = new Vector3(-90f, 0f, 0f);                                                          // up in the socket's frame (smoke still rises in world up)
            var vel = smoke.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.015f, 0.015f); vel.y = new ParticleSystem.MinMaxCurve(0.10f, 0.16f); vel.z = new ParticleSystem.MinMaxCurve(-0.015f, 0.02f);
            var noise = smoke.noise; noise.enabled = true; noise.strength = 0.07f; noise.frequency = 0.9f; noise.scrollSpeed = 0.3f; noise.damping = true; noise.octaveCount = 2;
            var size = smoke.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.35f, 1.6f), new Keyframe(1f, 3.4f)));
            var col = smoke.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(0.85f, 0.8f, 0.74f), 0f), new GradientColorKey(new Color(0.62f, 0.62f, 0.64f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.1f), new GradientAlphaKey(0.28f, 0.55f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var rot = smoke.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.7f, 0.7f);
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = ParticleSystemRenderMode.Billboard; pr.sortMode = ParticleSystemSortMode.Distance;
            pr.shadowCastingMode = ShadowCastingMode.Off; pr.receiveShadows = false; pr.renderingLayerMask = 1u << LightLayerBit;
            pr.sharedMaterial = SmokeMaterial();
            smoke.Play();
        }

        static Material smokeMat;
        static Material SmokeMaterial()
        {
            if (smokeMat) return smokeMat;
            var t = Resources.Load<Material>(MatPath + "ZodStage_Smoke");
            if (t) smokeMat = new Material(t) { hideFlags = HideFlags.DontSave };
            else
            {
                var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (!sh) return null;
                smokeMat = new Material(sh) { hideFlags = HideFlags.DontSave, name = "ZodStage_Smoke" };
                smokeMat.SetFloat("_Surface", 1); smokeMat.SetFloat("_Blend", 0);
                smokeMat.SetOverrideTag("RenderType", "Transparent"); smokeMat.renderQueue = 3000;
                smokeMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); smokeMat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                smokeMat.SetFloat("_ZWrite", 0); smokeMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            var tex = Resources.Load<Texture2D>(SmokeTexPath);
            if (!tex)
            {   // soft round puff (the Atmosphere / BS01 kit recipe) when the package's noisy puff texture is missing
                tex = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                var px = new Color32[64 * 64];
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        float dx = (x + 0.5f) / 32f - 1, dy = (y + 0.5f) / 32f - 1, a = Mathf.Clamp01(1 - Mathf.Sqrt(dx * dx + dy * dy));
                        px[y * 64 + x] = new Color32(255, 255, 255, (byte)(255 * a * a * (3 - 2 * a)));
                    }
                tex.SetPixels32(px); tex.Apply();
            }
            smokeMat.SetTexture("_BaseMap", tex);
            return smokeMat;
        }

        /// <summary>A cloud of smoke from the mouth (cigar_puff exhale, a laugh at the end of a line).</summary>
        public void Exhale(float strength = 1f)
        {
            if (!smoke) return;
            Vector3 at = cigarTip ? cigarTip.position : transform.TransformPoint(new Vector3(-0.05f, 1.98f, 0.25f));
            Vector3 fwd = model ? model.forward : Vector3.forward;
            var ep = new ParticleSystem.EmitParams();
            int n = Mathf.RoundToInt(10 * strength);
            for (int i = 0; i < n; i++)
            {
                ep.position = at + fwd * UnityEngine.Random.Range(-0.02f, 0.05f) + UnityEngine.Random.insideUnitSphere * 0.03f;
                ep.velocity = fwd * UnityEngine.Random.Range(0.18f, 0.36f) + Vector3.up * UnityEngine.Random.Range(0.06f, 0.16f) + UnityEngine.Random.insideUnitSphere * 0.05f;
                ep.startSize = UnityEngine.Random.Range(0.10f, 0.19f); ep.startLifetime = UnityEngine.Random.Range(2.2f, 3.2f);
                ep.startColor = new Color(0.86f, 0.82f, 0.77f, 0.5f);
                smoke.Emit(ep, 1);
            }
        }

        // ------------------------------------------------------------------------------------------------ camera + look
        void BuildCamera()
        {
            var go = new GameObject("ZodStageCamera") { layer = Layer };
            go.transform.SetParent(transform, false);
            cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.cullingMask = 1 << Layer; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.03f, 0.035f, 0.034f, 1f);
            cam.fieldOfView = fov; cam.nearClipPlane = 0.05f; cam.farClipPlane = 30f; cam.allowHDR = true; cam.allowMSAA = true; cam.depth = -20f;
            cam.useOcclusionCulling = false;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true; data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; data.antialiasingQuality = AntialiasingQuality.High;
            data.volumeLayerMask = 1 << Layer; data.renderShadows = true; data.stopNaN = true; data.dithering = true;
            data.requiresColorOption = CameraOverrideOption.Off;                                             // no opaque texture needed on the set
            // the set's own grade (only cameras whose volume mask holds the ZodCameo layer see it: the game cameras use Default)
            var vgo = new GameObject("ZodStageVolume") { layer = Layer };
            vgo.transform.SetParent(transform, false);
            vol = vgo.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 50f;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>(); prof.name = "ZodStage"; prof.hideFlags = HideFlags.DontSave;
            prof.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
            var bl = prof.Add<Bloom>(true); bl.threshold.Override(0.95f); bl.intensity.Override(0.85f); bl.scatter.Override(0.62f); bl.tint.Override(new Color(1f, 0.93f, 0.84f));
            var vg = prof.Add<Vignette>(true); vg.intensity.Override(0.3f); vg.smoothness.Override(0.45f); vg.color.Override(Color.black);
            var ca = prof.Add<ColorAdjustments>(true); ca.postExposure.Override(0.05f); ca.contrast.Override(10f); ca.saturation.Override(4f);
            dof = prof.Add<DepthOfField>(true); dof.mode.Override(DepthOfFieldMode.Gaussian); dof.gaussianStart.Override(3.5f); dof.gaussianEnd.Override(7.5f);
            dof.gaussianMaxRadius.Override(1.0f); dof.highQualitySampling.Override(true);
            vol.sharedProfile = prof;
        }

        /// <summary>RenderTexture of the comms well (4x MSAA, sRGB), recreated only when the size changes.</summary>
        public RenderTexture EnsureFeed(int w, int h)
        {
            w = Mathf.Clamp(w, 160, 1280); h = Mathf.Clamp(h, 120, 960);
            if (Feed && Feed.width == w && Feed.height == h) return Feed;
            if (Feed) { if (cam) cam.targetTexture = null; Feed.Release(); Destroy(Feed); }
            Feed = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "ZodFeed", antiAliasing = 4, useMipMap = false };
            Feed.Create();
            if (cam) { cam.targetTexture = Feed; cam.aspect = w / (float)h; }
            return Feed;
        }

        /// <summary>Starts / stops rendering the feed (the stage camera runs only while a comms window shows it).</summary>
        public void SetLive(bool on)
        {
            if (!cam) return;
            if (on && !Feed) EnsureFeed(640, 480);
            if (model && model.gameObject.activeSelf != on) model.gameObject.SetActive(on);             // no Animator / rig / smoke cost between lines
            if (smoke && !(model && smoke.transform.IsChildOf(model))) smoke.gameObject.SetActive(on);   // no CigarTip socket: smoke under the stage root
            cam.enabled = on;
            if (!on) { ResetSlam(); ResetBoys(); }                                                                         // voice_lines_v3
            if (vol) vol.enabled = on;                                                                  // the set's grade exists only while it renders
            if (on) { ExcludeFromOtherCameras(); nextCamScan = Time.unscaledTime + 2f; if (smoke && !smoke.isPlaying) smoke.Play(); }
        }

        void ExcludeFromOtherCameras()
        {
            int bit = 1 << Layer;
            foreach (var c in Camera.allCameras) if (c && c != cam && (c.cullingMask & bit) != 0) c.cullingMask &= ~bit;
        }

        /// <summary>Line framing: "tight" / "medium" / "wide" (clip framings override it while they play).</summary>
        public void SetFraming(string f) { if (!string.IsNullOrEmpty(f) && Framings.ContainsKey(f)) lineFraming = f; UpdateWantFraming(); }
        public void SnapFraming()
        {
            UpdateWantFraming(); dist = wantDist; lookY = wantLook; distVel = lookVel = 0f; push = 0f;
            // REVIEW: a new line starts clean (a skipped line could leave a punch / pull-out / fist hold running into the next one)
            fovMul = 1f; fovVel = 0f; punchUntil = pullUntil = thrustUntil = -1f; lookBias = lookBiasVel = 0f; kick = 0f; thrustW = 0f; ResetSlam(); ResetBoys();
        }
        /// <summary>Slow dolly-in over a line (0..1).</summary>
        public float Push { set => push = Mathf.Clamp01(value); }

        void UpdateWantFraming()
        {
            string f = !boysOn && CurrentClip != null && ClipFraming.TryGetValue(CurrentClip, out var cf) && Rank(cf) > Rank(lineFraming) ? cf : lineFraming;   // zod_briefings_v3
            var fr = Framings.TryGetValue(f, out var x) ? x : Framings["medium"];
            wantDist = fr.dist; wantLook = fr.look;
        }
        static int Rank(string f) => f == "wide" ? 2 : f == "medium" ? 1 : 0;

        // ================================================================================================ performance
        public bool HasClip(string c) => puppet != null && !string.IsNullOrEmpty(c) && puppet.Has(c);
        public float ClipLength(string c) => puppet != null && puppet.Length(c) > 0f ? puppet.Length(c) : ClipInfo.TryGetValue(c ?? "", out var i) ? i.len : 2f;

        /// <summary>The looping clip under the line (talk_*, idle ...). Plays now unless a one-shot is running.</summary>
        public void SetBed(string clip, float fade = 0.3f)
        {
            clip = SafeClip(clip);
            if (!HasClip(clip)) return;
            Bed = clip;
            if (oneShotUntil < 0f) Play(clip, fade);
        }

        /// <summary>A one-shot gesture (point_map, salute, fist_pump ...): back to the bed after its length, or after `hold` s.</summary>
        public void OneShot(string clip, float fade = 0.25f, float hold = -1f)
        {
            clip = SafeClip(clip);
            if (!HasClip(clip)) return;
            Play(clip, fade);
            float len = ClipLength(clip);
            oneShotUntil = clock + Mathf.Max(0.3f, (hold > 0f ? Mathf.Min(hold, len) : len) - 0.3f);
        }

        /// <summary>
        /// hips +0.30 m, both feet 0.21 m off the floor); fist_pump = a firm "yes" pump with the feet down (feet lift 0.01 m).</summary>
        public static readonly Dictionary<string, string> CalmSwaps = new Dictionary<string, string>
        {
            ["victory_cheer"] = "fist_pump",
        };

        /// <summary>v2: the clip to play for `clip` on this avatar (Humanoid: the Blender-keyed salute / laugh / cigar_puff are swapped
        /// </summary>
        public string SafeClip(string clip)
        {
            if (clip != null && CalmSwaps.TryGetValue(clip, out var calm))
            {
                if (swapped.Add(clip)) Debug.Log($"[ZodCameo] '{clip}' (a jump, not his reserved character) plays as '{calm}'");
                clip = calm;
            }
            if (clip == null || !anim || !anim.isHuman || !HumanoidUnsafe.TryGetValue(clip, out var alt)) return clip;
            if (swapped.Add(clip)) Debug.Log($"[ZodCameo] Humanoid avatar: '{clip}' (keyed, breaks the arm under Humanoid) plays as '{alt}'");
            return alt;
        }

        void Play(string clip, float fade)
        {
            clip = SafeClip(clip);
            if (puppet == null || !puppet.Has(clip)) return;
            puppet.Play(clip, fade);
            CurrentClip = clip; clipStart = clock;
            puffAt = exhaleAt = -1f;
            if (clip == "cigar_puff") { puffAt = clock + 0.5f; exhaleAt = clock + 2.05f; }
            if (clip == "laugh") chatterUntil = clock + 2.5f;
            UpdateWantFraming();
        }

        /// <summary>Glow the cigar ember (cigar_puff draw, the "puff" fx).</summary>
        public void Puff(float seconds = 1.2f) { puffT = Mathf.Max(puffT, seconds); if (rig) rig.Puff(seconds); }
        /// <summary>The laugh's jaw (Humanoid imports drop the keyed "HAR HAR"; ZodTalkRig opens the mouth on top of the voice).</summary>
        public void Chatter(float seconds) { chatterUntil = Mathf.Max(chatterUntil, clock + seconds); }

        // ------------------------------------------------------------------------------------------------ v2 transmission camera API
        /// <summary>Zoom punch on a stressed word: the field of view snaps in (strength 1 = punchFov) and the aim slides up to his face,
        /// held `hold` seconds, then eases back.</summary>
        public void PunchIn(float strength = 1f, float hold = 0.55f)
        {
            punchK = Mathf.Clamp(strength, 0.3f, 1.5f); punchUntil = clock + Mathf.Clamp(hold, 0.2f, 2f); pullUntil = -1f;
            Kick(0.35f * punchK);
        }
        /// <summary>Pull-out on the final order: wider field of view for `seconds` (the fist needs the room), aim lowered a little.</summary>
        public void PullOut(float seconds = 1.6f) { pullUntil = clock + Mathf.Clamp(seconds, 0.3f, 4f); punchUntil = -1f; Kick(0.45f); }
        /// <summary>A short high-frequency jolt of the hand-held camera (0..1), decays in about 0.4 s.</summary>
        public void Kick(float k) { if (!OzMotion.Reduced) kick = Mathf.Clamp01(Mathf.Max(kick, k)); }
        /// <summary>The fist jabs at the lens (one call per GO!); the arm holds the thrust for `hold` s after the last jab.</summary>
        public void Thrust(float hold = 0.6f)
        {
            if (!uArm || !fArm || !hand) return;
            jabAt = clock; thrustUntil = Mathf.Max(thrustUntil, clock + Mathf.Clamp(hold, 0.2f, 3f));
            Kick(0.6f);
        }
        /// <summary>True while the thrust owns the right arm (Play check).</summary>
        public bool Thrusting => thrustW > 0.001f;
        public float FovNow => cam ? cam.fieldOfView : fov;

        public bool Frozen
        {
            get => frozen;
            set
            {
                if (frozen == value) return;
                frozen = value;
                puppet?.Freeze(value);
                if (rig) rig.frozen = value;
                FreezeBoys(value);                                                                          // zod_briefings_v3
                if (smoke) { var m = smoke.main; m.simulationSpeed = value ? 0f : 1f; }
            }
        }

        void LateUpdate()
        {
            float dt = frozen ? 0f : Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            clock += dt;
            puppet?.Tick(dt);
            if (oneShotUntil >= 0f && clock >= oneShotUntil) { oneShotUntil = -1f; Play(Bed, 0.35f); }
            if (puffAt >= 0f && clock >= puffAt) { puffAt = -1f; Puff(1.3f); }
            if (exhaleAt >= 0f && clock >= exhaleAt) { exhaleAt = -1f; Exhale(1f); }
            if (rig)
            {
                float laughK = 0f;
                if (clock < chatterUntil && CurrentClip == "laugh")
                {   // "HAR HAR": 4.2 Hz flaps, strongest in the middle of the laugh
                    float u = clock - clipStart;
                    laughK = Mathf.Max(0f, Mathf.Sin(u * Mathf.PI * 2f * 4.2f)) * 0.75f * Mathf.Clamp01(u / 0.25f) * Mathf.Clamp01((chatterUntil - clock) / 0.3f);
                }
                rig.extraMouth = laughK;
            }
            puffT = Mathf.Max(0f, puffT - dt);
            if (ember)
            {
                if (cigarTip) ember.transform.position = cigarTip.position + cigarTip.forward * EmberOut;
                float flick = 1f + 0.25f * (Mathf.PerlinNoise(Time.unscaledTime * 3.1f, 0.37f) - 0.5f) * 2f;
                ember.intensity = emberIntensity * flick * (1f + 3f * Mathf.Clamp01(puffT / 0.3f));
            }
            if (lamp && !frozen) lamp.intensity = lampIntensity * (0.96f + 0.04f * Mathf.PerlinNoise(Time.unscaledTime * 1.7f, 4.2f));
            BoysPoseIn();
            TickBoys(dt);                                                                                 // zod_briefings_v3: the opening curve + the boys' idle life
            TickFront(dt);
            AlanHunch();
            BoysPoseOut();
            if (cam && cam.enabled) PlaceCamera(dt);
            if (model && model.gameObject.activeInHierarchy) { ApplyThrust(dt); ApplySlam(dt); ApplyGaze(dt); }
            TickHqFire(dt);
            TickAlarm(dt);
            TickClipHead(dt);
                  // voice_lines_v3: + the red button                          // v2: after the Animator and the camera
            if (cam && cam.enabled && Time.unscaledTime > nextCamScan) { nextCamScan = Time.unscaledTime + 2f; ExcludeFromOtherCameras(); }
        }

        /// <summary>v2: the hand-held field camera. Base framing (dist / aim height, SmoothDamp as before) + zoom spring + hand-held
        /// noise + operator re-frames + kicks. Everything runs on the stage clock (frozen with the game's pause).</summary>
        void PlaceCamera(float dt)
        {
            float t = clock;
            float hh = handheld ? handheldAmount * (OzMotion.Reduced ? 0.15f : 1f) : 0f;
            bool punching = clock < punchUntil, pulling = clock < pullUntil;
            if (dt > 0f)
            {
                dist = Mathf.SmoothDamp(dist, wantDist * (1f - 0.05f * push), ref distVel, 0.55f, 10f, dt);
                lookY = Mathf.SmoothDamp(lookY, wantLook, ref lookVel, 0.55f, 10f, dt);
                float biasGoal = punching ? punchLook - wantLook : pulling ? pullLook - wantLook : 0f;
                lookBias = Mathf.SmoothDamp(lookBias, biasGoal, ref lookBiasVel, punching ? 0.07f : 0.22f, 20f, dt);
                // zoom rocker: under-damped spring on the fov multiplier (snap in with a small overshoot, softer back out)
                float goal = punching ? Mathf.LerpUnclamped(1f, punchFov, punchK) : pulling ? pullFov : 1f;
                if (OzMotion.Reduced) goal = Mathf.Lerp(1f, goal, 0.35f);
                float w = goal < fovMul ? 24f : 15f, zeta = 0.62f;
                int steps = Mathf.CeilToInt(dt / 0.008f); float h = dt / steps;
                for (int i = 0; i < steps; i++) { fovVel += (-2f * zeta * w * fovVel - w * w * (fovMul - goal)) * h; fovMul += fovVel * h; }
                fovMul = Mathf.Clamp(fovMul, 0.4f, 1.6f);
                kick = Mathf.MoveTowards(kick, 0f, dt * 2.5f);
                // the operator re-frames every 2-4.5 s: a quick small correction (0.35 s, eased)
                if (t >= reframeAt)
                {
                    reframeFrom = reframeCur; reframeTo = new Vector2(UnityEngine.Random.Range(-1f, 1f) * 0.9f, UnityEngine.Random.Range(-1f, 1f) * 0.55f);
                    reframeT0 = t; reframeAt = t + UnityEngine.Random.Range(2.0f, 4.5f);
                }
                reframeCur = Vector2.Lerp(reframeFrom, reframeTo, Mathf.SmoothStep(0f, 1f, (t - reframeT0) / 0.35f));
            }
            bool react = ReactActive(out var rc);
            Vector3 look = react ? transform.TransformPoint(new Vector3(0f, wantLook + rc.look, 0.02f))
                                 : transform.TransformPoint(new Vector3(0f, lookY + lookBias, 0.02f) + BoysLookOffset());   // zod_briefings_v3: the opening on the boys' backs
            Vector3 dir = Quaternion.Euler(0f, yawDeg + (react ? rc.yaw : 0f), 0f) * Vector3.forward; dir.y = elevation + (react ? rc.elev : 0f); dir.Normalize();
            Vector3 pos = react ? look + transform.TransformDirection(dir) * rc.dist
                                : look + transform.TransformDirection(dir) * (dist + BoysExtraDist()) + transform.up * BoysRise();   // zod_briefings_v3: back in the HQ room, then the push-in
            var baseRot = Quaternion.LookRotation(look - pos, transform.up);
            // hand-held noise (-1..1 layers): slow sway + faster jitter, a footstep bob, the re-frame; zoomed in = a little calmer
            float zoomCalm = Mathf.Lerp(0.7f, 1f, Mathf.Clamp01((fovMul - 0.6f) / 0.4f));
            // (Perlin layers rarely pass +-0.6, so the real sway is about +-0.45 deg pitch, +-0.55 deg yaw, +-0.65 deg roll + the
            // re-frames: about 5-10 px of drift in a 300 px feed, clearly hand-held, never losing his face)
            // REVIEW: the 1.6-2.3 Hz "hand" layers 0.25 / 0.25 / 0.3 -> 0.45 each: the hand tremor was ~1.4 px RMS in the 300 px feed
            float pitch = (0.75f * N(t, 0.40f, 3.3f) + 0.45f * N(t, 2.3f, 4.4f) + reframeCur.y) * hh * zoomCalm;
            float yaw = (0.9f * N(t, 0.33f, 6.6f) + 0.45f * N(t, 2.1f, 7.7f) + reframeCur.x) * hh * zoomCalm;
            float roll = (1.1f * N(t, 0.25f, 11.1f) + 0.45f * N(t, 1.6f, 12.2f)) * hh;
            Vector3 off = new Vector3(0.020f * N(t, 0.35f, 1.3f) + 0.007f * N(t, 1.7f, 5.1f),
                                      0.013f * N(t, 0.30f, 2.7f) + 0.005f * N(t, 1.9f, 8.3f) + 0.005f * Mathf.Sin(t * 2.2f * Mathf.PI),
                                      0.018f * N(t, 0.22f, 9.1f)) * hh;
            float k = kick * kick;                                                                      // kicks: a jolt that dies fast
            if (k > 0f)
            {
                pitch += k * 1.7f * Mathf.Sin(t * 47f); yaw += k * 1.4f * Mathf.Sin(t * 53f + 1.1f); roll += k * 1.3f * Mathf.Sin(t * 41f + 2.3f);
                off += new Vector3(Mathf.Sin(t * 43f), Mathf.Sin(t * 57f + 0.7f), 0f) * 0.012f * k;
            }
            if (PlaceFrontCamera(pitch, yaw, roll, off)) return;
            if (PlaceSlamCamera(pitch, yaw, roll, off)) return;                                        // voice_lines_v3: the red-button insert
            cam.transform.position = pos + baseRot * off;
            cam.transform.rotation = baseRot * Quaternion.Euler(pitch, yaw, roll);
            cam.fieldOfView = react ? fov * fovMul * rc.fov : fov * fovMul * BoysFovMul();                  // zod_briefings_v3
            if (dof != null) { float dd = react ? rc.dist : dist + BoysExtraDist(); dof.gaussianStart.value = dd + 0.9f; dof.gaussianEnd.value = dd + 5f; }   // zod_briefings_v3
        }

        static float N(float t, float freq, float seed) => (Mathf.PerlinNoise(t * freq, seed) - 0.5f) * 2f;

        // ------------------------------------------------------------------------------------------------ v2 fist at the camera
        /// <summary>The elbow's hinge axis in the forearm's own frame, taken from the animated arm whenever it is clearly bent (the
        /// imported clips bend the elbow within 7 deg of one axis): the IK then bends it exactly like the animation does.</summary>
        void CacheHinge()
        {
            Vector3 A = uArm.position, B = fArm.position, C = hand.position, u = A - B, v = C - B;
            if (u.sqrMagnitude < 1e-6f || v.sqrMagnitude < 1e-6f || Vector3.Angle(u, v) > 160f) return;   // straight: no plane
            hingeLocal = Quaternion.Inverse(fArm.rotation) * Vector3.Cross(u, v).normalized;
            hingeOk = true;
        }

        static float JabPulse(float x) => x < 0f ? 0f : x < 0.07f ? Mathf.Sin(x / 0.07f * Mathf.PI * 0.5f) : Mathf.Exp(-(x - 0.07f) / 0.16f);

        /// <summary>Right arm on top of the animation: weight in 0.12 s, out in 0.32 s. 0. chest leans toward the lens; 1. the elbow
        /// bends around its hinge to the law-of-cosines angle for the reach; 2. the arm aims at the target; 3. it swivels around
        /// the shoulder-target axis so the elbow points out and down; 4. the wrist straightens (bind pose). From-to rotations only,
        /// so neither the bone axes nor the engine's handedness matter (tools/thrust_check.py runs the same steps in Blender).</summary>
        void ApplyThrust(float dt)
        {
            thrustW = clock < thrustUntil ? Mathf.MoveTowards(thrustW, 1f, dt / 0.12f) : Mathf.MoveTowards(thrustW, 0f, dt / 0.32f);
            if (!uArm || !fArm || !hand || !cam) return;
            CacheHinge();                                                                               // from this frame's animation
            if (thrustW <= 0f) return;
            float w = Mathf.SmoothStep(0f, 1f, thrustW);
            Vector3 up = transform.up, right = model ? model.right : transform.right, camPos = cam.transform.position;
            if (chest && thrustLean > 0f)
            {
                Vector3 hz = Vector3.ProjectOnPlane(camPos - chest.position, up);
                if (hz.sqrMagnitude > 1e-4f)
                {
                    var lean = Quaternion.FromToRotation(up, Vector3.Slerp(up, hz.normalized, thrustLean / 90f));
                    chest.rotation = Quaternion.Slerp(Quaternion.identity, lean, w) * chest.rotation;
                }
            }
            Vector3 A = uArm.position, B = fArm.position, C = hand.position;
            float la = (B - A).magnitude, lb = (C - B).magnitude;
            if (la < 1e-4f || lb < 1e-4f) return;
            Quaternion u0 = uArm.rotation, f0 = fArm.rotation, h0 = hand.localRotation;
            float reach = Mathf.Clamp(thrustReach + thrustJab * JabPulse(clock - jabAt), 0.5f, 0.985f);
            Vector3 T = A + (camPos + right * thrustAside - up * 0.05f - A).normalized * (la + lb) * reach;
            float lat = Mathf.Clamp((T - A).magnitude, Mathf.Abs(la - lb) + 1e-3f, la + lb - 1e-3f);
            // 1. elbow (no bent frame seen yet: his right axis, which bends a hanging forearm forward, toward the lens)
            Vector3 n = hingeOk ? (fArm.rotation * hingeLocal).normalized : right, u = A - B, v = C - B;
            Vector3 e1 = Vector3.ProjectOnPlane(u, n).normalized, e2 = Vector3.Cross(n, e1).normalized;
            float phi = Mathf.Acos(Mathf.Clamp((la * la + lb * lb - lat * lat) / (2f * la * lb), -1f, 1f));
            var r1 = Quaternion.FromToRotation(v, (e1 * Mathf.Cos(phi) + e2 * Mathf.Sin(phi)) * lb);
            // 2. aim
            var r2 = Quaternion.FromToRotation(B + r1 * v - A, T - A);
            // 3. swivel: elbow out (his right) and down
            Vector3 ax = (T - A).normalized, b2 = r2 * (B - A);
            Vector3 e = Vector3.ProjectOnPlane(b2, ax), p = Vector3.ProjectOnPlane(right * 0.35f - up, ax);
            var r3 = e.sqrMagnitude > 1e-8f && p.sqrMagnitude > 1e-8f ? Quaternion.FromToRotation(e, p) : Quaternion.identity;
            var rUp = r3 * r2;
            uArm.rotation = Quaternion.Slerp(u0, rUp * u0, w);
            fArm.rotation = Quaternion.Slerp(f0, rUp * r1 * f0, w);
            // 4. straight wrist: the fist continues the forearm
            hand.localRotation = Quaternion.Slerp(h0, handRest, w);
        }

        static Transform Find(Transform t, string name)
        {
            if (!t) return null;
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var r = Find(t.GetChild(i), name); if (r) return r; }
            return null;
        }

        // shells while the visible cigar tip was 7-13 cm away): ZodStage's LateUpdate (execution order 160) places the ember light BEFORE
        // ZodTalkRig's LateUpdate (order 200) applies the chin clamp / nods / gaze, so whenever a clip bowed his head the light stayed where
        // the UNclamped cigar tip had been: in the shell row (v3) or on the chest (v1), up to 50 deg of head tilt below the cigar we see.
        // The light is placed again right before the stage camera renders, at the final cigar tip (every frame and every CaptureFeed).
        void OnEnable() { RenderPipelineManager.beginCameraRendering += PlaceEmberForRender; }
        void OnDisable() { RenderPipelineManager.beginCameraRendering -= PlaceEmberForRender; }
        void PlaceEmberForRender(ScriptableRenderContext ctx, Camera c)
        {
            if (c != cam || !ember || !cigarTip) return;
            ember.transform.position = cigarTip.position + cigarTip.forward * EmberOut;
        }

        void OnDestroy()
        {
            puppet?.Dispose();
            if (cam) cam.targetTexture = null;
            if (Feed) { Feed.Release(); Destroy(Feed); }
            if (vol && vol.sharedProfile) Destroy(vol.sharedProfile);
            bool last = true;
            foreach (var o in FindObjectsByType<ZodStage>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (o && o != this) { last = false; break; }
            if (last) { foreach (var m in madeMats) if (m) Destroy(m); madeMats.Clear(); }
        }

        // ================================================================================================ puppets
        abstract class Puppet
        {
            public abstract bool Has(string clip);
            public abstract float Length(string clip);
            public abstract void Play(string clip, float fade);
            public virtual void Tick(float dt) { }
            public virtual void Freeze(bool f) { }
            public virtual void Dispose() { }
        }

        /// <summary>The setup's Animator controller: one state per clip (state name = clip name), cross-faded in fixed time.</summary>
        sealed class ControllerPuppet : Puppet
        {
            readonly Animator a; readonly Dictionary<string, float> len = new Dictionary<string, float>();
            public ControllerPuppet(Animator a)
            {   // the setup names every state after its clip, so the controller's clip list is the state list (HasState needs an
                // initialised Animator, which a fresh instance is not yet)
                this.a = a;
                foreach (var c in a.runtimeAnimatorController.animationClips) if (c) len[c.name] = c.length;
            }
            public override bool Has(string clip) => clip != null && len.ContainsKey(clip);
            public override float Length(string clip) => clip != null && len.TryGetValue(clip, out var l) ? l : 0f;
            public override void Play(string clip, float fade) { if (a && a.isActiveAndEnabled) a.CrossFadeInFixedTime(clip, Mathf.Max(0f, fade), 0); }
            public override void Freeze(bool f) { if (a) a.speed = f ? 0f : 1f; }
        }

        /// <summary>Fallback without the setup: the FBX's clips in a 2-input Playables mixer (cross-fade, manual loop).</summary>
        sealed class PlayablesPuppet : Puppet
        {
            PlayableGraph g; AnimationMixerPlayable mix; AnimationClipPlayable cur, prev;
            readonly Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
            float fadeT, fadeDur = 0.01f; string curName; bool frozen;
            public int Count => clips.Count;
            public PlayablesPuppet(Animator a, List<AnimationClip> list)
            {
                foreach (var c in list) clips[c.name] = c;
                g = PlayableGraph.Create("ZodPuppet");
                g.SetTimeUpdateMode(DirectorUpdateMode.UnscaledGameTime);
                var o = AnimationPlayableOutput.Create(g, "Zod", a);
                mix = AnimationMixerPlayable.Create(g, 2);
                o.SetSourcePlayable(mix);
                g.Play();
            }
            public override bool Has(string clip) => clip != null && clips.ContainsKey(clip);
            public override float Length(string clip) => clip != null && clips.TryGetValue(clip, out var c) ? c.length : 0f;
            public override void Play(string clip, float fade)
            {
                if (!g.IsValid() || !clips.TryGetValue(clip, out var c)) return;
                if (prev.IsValid()) { g.Disconnect(mix, 0); prev.Destroy(); }
                if (cur.IsValid()) { g.Disconnect(mix, 1); g.Connect(cur, 0, mix, 0); prev = cur; }
                cur = AnimationClipPlayable.Create(g, c); cur.SetApplyFootIK(false);
                g.Connect(cur, 0, mix, 1);
                curName = clip; fadeT = 0f; fadeDur = Mathf.Max(0.01f, fade);
                Weights();
            }
            public override void Tick(float dt)
            {
                if (!g.IsValid() || frozen) return;
                fadeT += dt; Weights();
                if (cur.IsValid() && curName != null && ClipInfo.TryGetValue(curName, out var i) && i.loop)
                {   // the bare FBX has no loop flags: wrap by hand
                    double len = cur.GetAnimationClip().length;
                    if (len > 0.01 && cur.GetTime() > len) cur.SetTime(cur.GetTime() % len);
                }
            }
            void Weights() { float w = Mathf.Clamp01(fadeT / fadeDur); mix.SetInputWeight(1, w); mix.SetInputWeight(0, prev.IsValid() ? 1f - w : 0f); }
            public override void Freeze(bool f) { frozen = f; if (mix.IsValid()) mix.SetSpeed(f ? 0 : 1); }
            public override void Dispose() { if (g.IsValid()) g.Destroy(); }
        }
    }
}
