using System.Collections.Generic;
using System.Linq;
using OpenZ.Intro;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// shuttle, lights, volume, the OzIntroPlayer wired to the timeline + master mix) and puts it first in the build (it hands over to
    /// MainMenu). Menu: OpenZ/Intro/Build Intro scene, OpenZ/Intro/Play live.</summary>
    public static class OzIntroBuilder
    {
        const string Root = "Assets/OpenZ/Intro/";
        const string ScenePath = "Assets/OpenZ/Scenes/Intro.unity";
        const string MatDir = Root + "Materials/";

        public const string VideoAsset = "Assets/OpenZ/Resources/OpenZ/Intro/openz_intro.mp4";

        /// <summary>The real-time intro (tweak and check; ignores the shipped video).</summary>
        [MenuItem("OpenZ/Intro/Play live")]
        public static void PlayLive() => Enter("live");

        /// <summary>As shipped: the pre-rendered video (Resources/OpenZ/Intro/openz_intro) with the live skip prompt.</summary>
        [MenuItem("OpenZ/Intro/Play video (as shipped)")]
        public static void PlayVideo() => Enter("");

        /// <summary>Re-record the shipped video: the live intro rendered offline, frame-exact 60 fps (full look), piped to ffmpeg
        /// (2560x1440, x264 CRF 14) + the master mix; on Play exit it is copied to <see cref="VideoAsset"/> and imported.</summary>
        [MenuItem("OpenZ/Intro/Re-record video")]
        public static void Rerecord() => Enter("record");

        static void Enter(string mode)
        {
            if (EditorApplication.isPlaying) return;
            EditorSceneManager.SaveOpenScenes();
            if (!System.IO.File.Exists(ScenePath)) Build();
            if (string.IsNullOrEmpty(mode)) EditorPrefs.DeleteKey(OzIntroPlayer.ModePref); else EditorPrefs.SetString(OzIntroPlayer.ModePref, mode);
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.isPlaying = true;
        }

        /// <summary>Copies a finished pre-render (Recordings/intro/openz_intro.mp4 + its .ready marker) into the Resources video.</summary>
        [MenuItem("OpenZ/Intro/Import recorded video")]
        public static void ImportRecordedVideo()
        {
            var src = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Recordings", "intro", "openz_intro.mp4"));
            if (!System.IO.File.Exists(src + ".ready")) return;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(VideoAsset));
            System.IO.File.Copy(src, VideoAsset, true);
            System.IO.File.Delete(src + ".ready");
            AssetDatabase.ImportAsset(VideoAsset, ImportAssetOptions.ForceUpdate);
            Debug.Log($"[OpenZ Intro] pre-rendered intro imported: {VideoAsset} ({new System.IO.FileInfo(VideoAsset).Length / 1048576f:0.0} MB)");
        }

        /// <summary>Re-applies Data/intro_look.txt to the saved Intro scene without rebuilding it: the sky panorama, the suns, the volume
        /// and the player's look data (per-panel overrides, flames, sand burst are read by OzIntroPlayer at run time).</summary>
        [MenuItem("OpenZ/Intro/Apply look data")]
        public static void ApplyLook()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[OpenZ Intro] stop Play first"); return; }
            EditorSceneManager.SaveOpenScenes();
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var look = Look.Load(Root + "Data/intro_look.txt");
            var sky = SkyPano(look, Root + "Textures/IC_SkyPano.asset", MatDir + "M_Intro_SkyPano.mat"); if (sky) RenderSettings.skybox = sky;
            var pl = Object.FindFirstObjectByType<OzIntroPlayer>();
            if (pl)
            {
                pl.lookTxt = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "Data/intro_look.txt");
                pl.rakeYaw = look.F("rakeYaw", 50f); pl.rakeElev = look.F("rakeElev", 32f); EditorUtility.SetDirty(pl);
            }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Debug.Log($"[OpenZ Intro] look data applied: sky panorama rebuilt, player lookTxt {(pl && pl.lookTxt ? "set" : "MISSING")}");
        }

        [MenuItem("OpenZ/Intro/Build Intro scene")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[OpenZ Intro] stop Play first"); return; }
            EditorSceneManager.SaveOpenScenes();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var setJson = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "Data/intro_set.json");
            var set = JsonUtility.FromJson<IntroSet>(setJson.text);
            var shader = Shader.Find("Universal Render Pipeline/Lit");

            // ---- materials
            Material Lit(string name, string tex, float smooth = 0.32f, string normal = null, bool instancing = false)
            {
                var m = Mat(name, shader);
                if (tex != null) m.SetTexture("_BaseMap", Tex(tex));
                m.SetColor("_BaseColor", Color.white); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
                if (normal != null) { m.SetTexture("_BumpMap", Tex(normal)); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", 1f); }
                m.enableInstancing = instancing; EditorUtility.SetDirty(m); return m;
            }
            var mAlan = Lit("M_Intro_Alan", "IC_Alan_BaseColor", 0.38f);
            var mBrad = Lit("M_Intro_Brad", "IC_Brad_BaseColor", 0.38f);
            var mShuttle = Lit("M_Intro_Shuttle", "IC_Shuttle_BaseColor", 0.3f);
            var mCan = Lit("M_Intro_Can", "IC_FuelCan_BaseColor", 0.45f, null, true);
            var mHeap = Lit("M_Intro_Heap", "IC_CanHeap_BaseColor", 0.25f, "IC_CanHeap_Normal");
            var mSand = Lit("M_Intro_Sand", "IC_Sand_BaseColor", 0.08f);
            var mPlanet = Lit("M_Intro_Planet", "IC_Planet_BaseColor", 0.1f);
            var mRing = Lit("M_Intro_Ring", "IC_CanRing_BaseColor", 0.3f); Transparent(mRing, 1f, true); mRing.SetTextureScale("_BaseMap", new Vector2(4, 1));
            var mGlass = Mat("M_Intro_Glass", shader); mGlass.SetColor("_BaseColor", new Color(0.75f, 0.85f, 0.9f, 0.38f)); mGlass.SetFloat("_Smoothness", 0.92f); Transparent(mGlass, 0.38f, false);
            var mGlow = Mat("M_Intro_Glow", Shader.Find("OpenZ/Intro/Glow"));
            var mStars = Mat("M_Intro_Stars", Shader.Find("OpenZ/Intro/Star")); mStars.SetFloat("_Gain", 2.6f);
            var mCloud = Mat("M_Intro_Cloud", shader); mCloud.SetColor("_BaseColor", new Color(1f, 0.92f, 0.8f)); mCloud.EnableKeyword("_EMISSION");
            mCloud.SetColor("_EmissionColor", new Color(1f, 0.55f, 0.25f) * 0.9f); mCloud.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            var mPanel = Mat("M_Intro_ComicPanel", Shader.Find("OpenZ/UI/ComicPanel"));
            var sky = Mat("M_Intro_Sky", Shader.Find("Skybox/Procedural"));
            sky.SetFloat("_SunSize", 0.045f); sky.SetFloat("_SunSizeConvergence", 6f); sky.SetFloat("_AtmosphereThickness", 0.62f);
            sky.SetColor("_SkyTint", IntroMath.C255(set.skyZenith)); sky.SetColor("_GroundColor", IntroMath.C255(set.skyHaze)); sky.SetFloat("_Exposure", 1.25f);
            foreach (var m in new[] { mGlass, mGlow, mStars, mCloud, mPanel, sky }) EditorUtility.SetDirty(m);

            var remap = new Dictionary<string, Material>
            {
                { "M_IC_Alan", mAlan }, { "M_IC_Brad", mBrad }, { "M_IC_Shuttle", mShuttle }, { "M_IC_FuelCan", mCan }, { "M_IC_CanHeap", mHeap },
                { "M_IC_Sand", mSand }, { "M_IC_Planet", mPlanet }, { "M_IC_CanRing", mRing }, { "M_IC_ShuttleGlass", mGlass },
            };
            // mouths v2 (staging models README section 9): flat URP Lit, the two dark mouths matte (a highlight reads as a stray line)
            Material Flat(string name, Color32 c, float smooth, bool matte = false)
            {
                var m = Mat(name, shader);
                m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
                if (matte)
                {
                    m.SetFloat("_SpecularHighlights", 0f); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                    m.SetFloat("_EnvironmentReflections", 0f); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                }
                EditorUtility.SetDirty(m); return m;
            }
            remap["M_IC_Alan_MouthFrame"] = Flat("M_Intro_Alan_MouthFrame", new Color32(66, 63, 59, 255), 0.5f);
            remap["M_IC_Alan_MouthGrille"] = Flat("M_Intro_Alan_MouthGrille", new Color32(216, 201, 174, 255), 0.5f);
            remap["M_IC_Alan_MouthDark"] = Flat("M_Intro_Alan_MouthDark", new Color32(30, 24, 23, 255), 0f, true);
            remap["M_IC_Brad_Jaw"] = Flat("M_Intro_Brad_Jaw", new Color32(186, 46, 34, 255), 0.5f);
            remap["M_IC_Brad_MouthTeeth"] = Flat("M_Intro_Brad_MouthTeeth", new Color32(226, 216, 196, 255), 0.5f);
            remap["M_IC_Brad_MouthFrame"] = Flat("M_Intro_Brad_MouthFrame", new Color32(92, 88, 82, 255), 0.5f);
            remap["M_IC_Brad_MouthDark"] = Flat("M_Intro_Brad_MouthDark", new Color32(30, 24, 23, 255), 0f, true);
            var look = Look.Load(Root + "Data/intro_look.txt");
            // item 9: the RockZ pillars kept the FBX's default grey Lit (the game assigns M_RockZ_Desert at run time; its TeamLit needs
            // game globals): an intro URP Lit with the same striated sandstone atlas
            var mRock = Mat("M_Intro_Rock", shader);
            var gameRock = AssetDatabase.LoadAssetAtPath<Material>(look.Str("rockAtlasFrom", "Assets/OpenZ/Models/RocksZ/M_RockZ_Desert.mat"));
            if (gameRock) mRock.SetTexture("_BaseMap", gameRock.GetTexture("_BaseMap"));
            mRock.SetColor("_BaseColor", Color.white); mRock.SetFloat("_Smoothness", look.F("rockSmoothness", 0.12f)); mRock.SetFloat("_Metallic", 0f); EditorUtility.SetDirty(mRock);
            var mFlame = Mat("M_Intro_Flame", Shader.Find("OpenZ/Intro/Flame")); EditorUtility.SetDirty(mFlame);
            var emissive = new Dictionary<string, Material>();

            // ---- the set
            var setRoot = new GameObject("IntroSet").transform;
            var ground = Place("IC_Set_Ground", setRoot, Vector3.zero, 0, 1, remap, emissive);
            ground.isStatic = true;
            var moundRoot = new GameObject("CanMounds").transform; moundRoot.SetParent(setRoot, false);
            foreach (var m in set.mounds) Place("IC_CanMound_" + m.type, moundRoot, IntroMath.V3(m.pos), m.yaw, m.scale, remap, emissive);
            var rockRoot = new GameObject("Rocks").transform; rockRoot.SetParent(setRoot, false);
            var rockAssets = new Dictionary<string, GameObject>();
            foreach (var r in set.rocks)
            {
                if (!rockAssets.TryGetValue(r.prefab, out var ra))
                {
                    var g = AssetDatabase.FindAssets(r.prefab + " t:GameObject").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => System.IO.Path.GetFileNameWithoutExtension(p) == r.prefab);
                    ra = g != null ? AssetDatabase.LoadAssetAtPath<GameObject>(g) : null; rockAssets[r.prefab] = ra;
                    if (!ra) Debug.LogWarning("[OpenZ Intro] rock asset missing: " + r.prefab);
                }
                if (!ra) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(ra, scene); go.transform.SetParent(rockRoot, false);
                go.transform.SetPositionAndRotation(IntroMath.V3(r.pos), Quaternion.Euler(0, r.yaw, 0)); go.transform.localScale *= r.scale;
                foreach (var rr in go.GetComponentsInChildren<Renderer>(true)) { var ms = rr.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = mRock; rr.sharedMaterials = ms; }
            }

            // item 12: seat every can mound on the dunes: sample the ground under its footprint rim and sink the base 8 cm below the
            var groundMf = ground.GetComponentInChildren<MeshFilter>(); var gcol = groundMf ? groundMf.gameObject.AddComponent<MeshCollider>() : null;
            if (gcol) { gcol.sharedMesh = groundMf.sharedMesh; Physics.SyncTransforms(); }
            int sunk = 0; float maxSink = 0f;
            if (gcol)
                foreach (Transform mt in moundRoot)
                {
                    var rend = mt.GetComponentInChildren<Renderer>(); if (!rend) continue;
                    var b = rend.bounds; float low = float.MaxValue;
                    for (int i = 0; i < 24; i++)
                    {
                        float a = i * Mathf.PI * 2f / 24f;
                        foreach (float k in new[] { 0.9f, 0.7f })
                        {
                            var p = new Vector3(b.center.x + Mathf.Cos(a) * b.extents.x * k, 60f, b.center.z + Mathf.Sin(a) * b.extents.z * k);
                            if (gcol.Raycast(new Ray(p, Vector3.down), out var h, 200f)) low = Mathf.Min(low, h.point.y);
                        }
                    }
                    if (low == float.MaxValue) continue;
                    float drop = (low - 0.08f) - b.min.y;
                    if (drop < 0f) { mt.position += Vector3.up * drop; sunk++; maxSink = Mathf.Max(maxSink, -drop); }
                }
            if (gcol) Object.DestroyImmediate(gcol);
            Debug.Log($"[OpenZ Intro] mounds seated on the dunes: {sunk} lowered (max {maxSink * 100f:0} cm)");

            // ---- actors
            var shuttle = Place("IC_Shuttle", null, IntroMath.V3(set.shuttlePos), set.shuttleYaw, 1, remap, emissive);
            var alan = Place("IC_Alan", null, IntroMath.V3(set.alanPile), set.alanYaw, 1, remap, emissive);
            var brad = Place("IC_Brad", null, IntroMath.V3(set.bradPile), set.bradYaw, 1, remap, emissive);
            foreach (var a in new[] { alan, brad }) { var an = a.GetComponent<Animator>(); if (an) an.enabled = false; }
            var planet = Place("IC_Planet", null, new Vector3(0, -5000, 0), 0, 120, remap, emissive); planet.transform.rotation = Quaternion.Euler(12, -30, 0); planet.SetActive(false);
            var ring = Place("IC_CanRing", null, new Vector3(0, -5000, 0), 0, 120, remap, emissive); ring.transform.rotation = Quaternion.Euler(-6, 0, 0); ring.SetActive(false);
            foreach (var r in ring.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;

            // ---- light, sky, volume
            Light Sun(string n, IntroSun s, float k, bool shadows)
            {
                var go = new GameObject(n); var l = go.AddComponent<Light>(); l.type = LightType.Directional; l.color = IntroMath.C255(s.col);
                l.intensity = k * s.intensity; l.shadows = shadows ? LightShadows.Soft : LightShadows.None; l.shadowStrength = 0.82f;
                go.transform.rotation = Quaternion.LookRotation(-IntroMath.V3(s.dir));
                go.AddComponent<UniversalAdditionalLightData>(); return l;
            }
            var sunBig = Sun("SunBig", set.suns[0], 2.4f, true);
            var sunSmall = Sun("SunSmall", set.suns[1], 2.4f, false);
            RenderSettings.sun = sunBig;
            // blacks), no Neutral tonemapper (it capped the whites near 215), a neutral trilight ambient (the khaki cast), whiter suns, the
            // animatic's warm peach sky with P01A's two sun discs, a cabin fill light for the cockpit shots
            RenderSettings.ambientMode = AmbientMode.Trilight; float ak = look.F("ambientScale", 0.75f);
            RenderSettings.ambientSkyColor = look.C("ambientSky", new Color(0.45f, 0.47f, 0.52f)) * ak;
            RenderSettings.ambientEquatorColor = look.C("ambientEquator", new Color(0.52f, 0.47f, 0.42f)) * ak;
            RenderSettings.ambientGroundColor = look.C("ambientGround", new Color(0.36f, 0.3f, 0.25f)) * ak; RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = look.F("fog", 0f) > 0.5f; RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = look.F("fogDensity", 0.0015f); RenderSettings.fogColor = look.C("fogColor", new Color(0.86f, 0.66f, 0.47f));
            sunBig.color = look.C("sunBigColor", new Color(1f, 0.95f, 0.88f)); sunBig.intensity = look.F("sunBigIntensity", 2.4f);
            sunSmall.color = look.C("sunSmallColor", new Color(1f, 0.85f, 0.72f)); sunSmall.intensity = look.F("sunSmallIntensity", 1f);
            RenderSettings.skybox = SkyPano(look, Root + "Textures/IC_SkyPano.asset", MatDir + "M_Intro_SkyPano.mat") ?? sky;
            var vol = new GameObject("IntroVolume").AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 5;
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Root + "Intro_Volume.asset");
            if (!prof) { prof = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(prof, Root + "Intro_Volume.asset"); }
            T Comp<T>() where T : VolumeComponent { if (!prof.TryGet<T>(out var c)) { c = prof.Add<T>(true); AssetDatabase.AddObjectToAsset(c, prof); } c.active = true; return c; }
            var tmc = Comp<Tonemapping>(); string tmn = look.Str("tonemap", "None");
            tmc.mode.Override(tmn == "Neutral" ? TonemappingMode.Neutral : tmn == "ACES" ? TonemappingMode.ACES : TonemappingMode.None);
            var blc = Comp<Bloom>(); blc.intensity.Override(look.F("bloomIntensity", 0.55f)); blc.threshold.Override(look.F("bloomThreshold", 1.05f)); blc.scatter.Override(0.6f);
            var cac = Comp<ColorAdjustments>(); cac.postExposure.Override(look.F("postExposure", -0.1f)); cac.contrast.Override(look.F("contrast", 10f)); cac.saturation.Override(look.F("saturation", 6f));
            EditorUtility.SetDirty(prof);
            vol.sharedProfile = prof;

            // ---- the player
            var pgo = new GameObject("IntroPlayer"); var pl = pgo.AddComponent<OzIntroPlayer>();
            pl.timelineJson = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "Data/intro_timeline.json"); pl.setJson = setJson;
            pl.mix = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "Audio/intro_mix.wav");
            pl.alan = alan; pl.bradGo = brad; pl.shuttle = shuttle;
            pl.alanClips = Clips(Root + "Models/IC_Alan.fbx"); pl.bradClips = Clips(Root + "Models/IC_Brad.fbx");
            pl.setRoot = setRoot; pl.planet = planet; pl.ring = ring; pl.sunBig = sunBig; pl.sunSmall = sunSmall;
            pl.canMeshes = new[] { "IC_FuelCan", "IC_FuelCan_Dented", "IC_FuelCan_Crushed" }.Select(n => AssetDatabase.LoadAllAssetsAtPath(Root + "Models/" + n + ".fbx").OfType<Mesh>().First()).ToArray();
            pl.canMat = mCan; pl.starMat = mStars; pl.glowMat = mGlow; pl.cloudMat = mCloud; pl.panelMat = mPanel;
            pl.flameMat = mFlame; pl.rakeYaw = look.F("rakeYaw", 50f); pl.rakeElev = look.F("rakeElev", 32f);
            pl.lookTxt = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "Data/intro_look.txt");
            BuildEndTitle(pl, shader);
            // the cockpit is lit by its own cabin lights (P05A / P05C / P06 were near-black inside: p50 65 vs the animatic's 119)
            Transform seatL = null, seatR = null; foreach (var t in shuttle.GetComponentsInChildren<Transform>(true)) { if (t.name == "Seat_L") seatL = t; if (t.name == "Seat_R") seatR = t; }
            if (seatL && seatR)
            {
                var cf = new GameObject("IntroCabinFill"); cf.transform.SetParent(shuttle.transform, false); cf.transform.position = (seatL.position + seatR.position) * 0.5f + Vector3.up * 1.1f;
                var cl = cf.AddComponent<Light>(); cl.type = LightType.Point; cl.color = look.C("cabinLightColor", new Color(1f, 0.9f, 0.8f));
                cl.intensity = look.F("cabinLightIntensity", 2.5f); cl.range = look.F("cabinLightRange", 3.5f); cl.shadows = LightShadows.None; cf.AddComponent<UniversalAdditionalLightData>();
            }
            // mouths v2: the jaw + head-bob players, driven from the mix clock by OzIntroPlayer.PlaceActor
            var mouthCurves = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "Data/ic_mouth_curves.json");
            foreach (var (robotGo, whoName) in new[] { (alan, "alan"), (brad, "brad") })
            {
                var j = robotGo.GetComponent<IC_RobotJaw>(); if (!j) j = robotGo.AddComponent<IC_RobotJaw>();
                j.who = whoName; j.curves = mouthCurves;
            }
            pl.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/OpenZ/Resources/OpenZ/Kit/Fonts/OZ_InterTight_SDF.asset");
            pl.logoTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/OpenZ/Resources/OpenZ/Kit/Wordmarks/wm_openz.png");
            EditorUtility.SetDirty(pl);

            EditorSceneManager.SaveScene(scene, ScenePath);
            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log($"[OpenZ Intro] scene built: {ScenePath} (first in the build), {set.mounds.Length} mounds, {rockRoot.childCount} rocks, clips A {pl.alanClips.Length} / B {pl.bradClips.Length}, emissive mats {emissive.Count}");
        }

        /// <summary>Fix-list item 18: the modelled OPENZ sign (staging intro_end_title_v1) on its own stage 2 km under the set, layer
        /// OzIntroPlayer.EndLayer: URP Lit materials per its README, a floor that takes the shadow and fades to the black page.</summary>
        static void BuildEndTitle(OzIntroPlayer pl, Shader lit)
        {
            const string ET = Root + "Models/EndTitle/", TX = Root + "Textures/EndTitle/";
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(ET + "OPENZ_Logo3D.fbx");
            if (!fbx) { Debug.LogWarning("[OpenZ Intro] end title FBX missing: 2D title"); return; }
            Texture2D T(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>(TX + n + ".png");
            Material M(string name, string baseMap, string ms, string nrm, Color tint, float bump)
            {
                var m = Mat(name, lit);
                m.SetTexture("_BaseMap", T(baseMap)); m.SetColor("_BaseColor", tint);
                m.SetTexture("_MetallicGlossMap", T(ms)); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_SmoothnessTextureChannel", 0f); m.SetFloat("_Smoothness", 1f); m.SetFloat("_Metallic", 1f);
                m.SetTexture("_BumpMap", T(nrm)); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", bump);
                EditorUtility.SetDirty(m); return m;
            }
            var face = M("M_Intro_Logo3D_Face", "OPENZ_Logo3D_Face_BaseColor", "OPENZ_Logo3D_Face_MetalSmooth", "OPENZ_Logo3D_Face_Normal", Color.white, 1f);
            var side = M("M_Intro_Logo3D_Side", "OPENZ_Logo3D_DarkMetal_BaseColor", "OPENZ_Logo3D_DarkMetal_MetalSmooth", "OPENZ_Logo3D_DarkMetal_Normal", new Color32(0xCF, 0xB3, 0x94, 255), 0.6f);
            var plate = M("M_Intro_Logo3D_Plate", "OPENZ_Logo3D_DarkMetal_BaseColor", "OPENZ_Logo3D_DarkMetal_MetalSmooth", "OPENZ_Logo3D_DarkMetal_Normal", new Color32(0xBB, 0xB6, 0xB1, 255), 0.6f);
            var hole = M("M_Intro_Logo3D_BulletHole", "OPENZ_BulletHole_BaseColor", "OPENZ_BulletHole_MetalSmooth", "OPENZ_BulletHole_Normal", Color.white, 1f);
            var map = new Dictionary<string, Material> { { "OPENZ_Logo3D_Face", face }, { "OPENZ_Logo3D_Side", side }, { "OPENZ_Logo3D_Plate", plate }, { "OPENZ_BulletHole", hole } };
            var stage = new GameObject("EndTitleStage"); stage.transform.position = new Vector3(0f, -2000f, 0f);
            var sign = (GameObject)PrefabUtility.InstantiatePrefab(fbx); sign.transform.SetParent(stage.transform, false);
            sign.transform.localPosition = Vector3.zero; sign.transform.localRotation = Quaternion.identity; sign.transform.localScale = Vector3.one;
            int remapped = 0;
            foreach (var r in sign.GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++) if (ms[i] && map.TryGetValue(ms[i].name, out var mm)) { ms[i] = mm; remapped++; }
                r.sharedMaterials = ms; r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true;
            }
            // the floor: dim desert sand (#2E2419) in a soft ellipse round the sign, fading to the black page by 13 m; takes the shadow
            const int N = 512; var ft = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "IC_EndFloor", wrapMode = TextureWrapMode.Clamp };
            var sand = new Color32(0x2E, 0x24, 0x19, 255);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float wx = ((x + 0.5f) / N - 0.5f) * 60f, wz = ((y + 0.5f) / N - 0.5f) * 60f;      // plane centred at (0, 0, 1)
                    float d = Mathf.Sqrt((wx / 1.5f) * (wx / 1.5f) + wz * wz);
                    float k = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2f, 13f, d));
                    ft.SetPixel(x, y, Color.Lerp(Color.black, sand, k));
                }
            ft.Apply(true);
            const string ftPath = Root + "Textures/EndTitle/IC_EndFloor.asset";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(ftPath)) AssetDatabase.DeleteAsset(ftPath);
            AssetDatabase.CreateAsset(ft, ftPath);
            var fm = Mat("M_Intro_EndFloor", lit); fm.SetTexture("_BaseMap", ft); float fk = Look.Load(Root + "Data/intro_look.txt").F("endFloorGain", 1.9f); fm.SetColor("_BaseColor", new Color(fk, fk, fk, 1f));
            fm.SetFloat("_Smoothness", 0f); fm.SetFloat("_Metallic", 0f);                                                  // matte: no reflection draws the plane's far edge on the black page
            fm.SetFloat("_SpecularHighlights", 0f); fm.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); fm.SetFloat("_EnvironmentReflections", 0f); fm.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); EditorUtility.SetDirty(fm);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.name = "EndFloor"; Object.DestroyImmediate(floor.GetComponent<Collider>());
            floor.transform.SetParent(stage.transform, false); floor.transform.localPosition = new Vector3(0f, 0f, 1f); floor.transform.localScale = new Vector3(6f, 1f, 6f);
            var fr = floor.GetComponent<MeshRenderer>(); fr.sharedMaterial = fm; fr.receiveShadows = true; fr.shadowCastingMode = ShadowCastingMode.Off;
            foreach (var t in stage.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = OzIntroPlayer.EndLayer;
            pl.endTitle = stage;
            pl.chipMeshes = AssetDatabase.LoadAllAssetsAtPath(ET + "OPENZ_MetalChips.fbx").OfType<Mesh>().ToArray();
            pl.chipMat = hole;
            Debug.Log($"[OpenZ Intro] end title 3D staged: {remapped} materials, chips {pl.chipMeshes.Length}, floor fade texture");
        }

        /// <summary>Data/intro_look.txt: "key v1 [v2 v3]" lines (colours as sRGB 0-255), # comments; edited without a recompile.</summary>
        sealed class Look
        {
            readonly Dictionary<string, string[]> v = new Dictionary<string, string[]>();
            public static Look Load(string path)
            {
                var l = new Look(); var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (ta) foreach (var line in ta.text.Split('\n'))
                    {
                        var s = line.Trim(); if (s.Length == 0 || s[0] == '#') continue;
                        var p = s.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries); if (p.Length > 1) l.v[p[0]] = p.Skip(1).ToArray();
                    }
                else Debug.LogWarning("[OpenZ Intro] " + path + " missing: built-in look values");
                return l;
            }
            static float P(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
            public float F(string k, float d) => v.TryGetValue(k, out var a) ? P(a[0]) : d;
            public string Str(string k, string d) => v.TryGetValue(k, out var a) ? a[0] : d;
            public Color C(string k, Color d) => v.TryGetValue(k, out var a) && a.Length >= 3 ? new Color(P(a[0]) / 255f, P(a[1]) / 255f, P(a[2]) / 255f) : d;
        }

        /// <summary>The intro sky: an HDR lat-long panorama (Skybox/Panoramic) graded by elevation like the animatic (warm peach to the
        /// horizon) with P01A's two sun discs (storyboard set_state sun_discs, Blender az / elev / size) as HDR discs + a soft halo.
        /// The values are written as sRGB numbers: this shader path displays them as given (edit-mode probe v4b; linear values came out
        /// orange-brown).</summary>
        static Material SkyPano(Look look, string texPath, string matPath)
        {
            var tlA = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "Data/intro_timeline.json"); if (!tlA) return null;
            var tl = JsonUtility.FromJson<IntroTimeline>(tlA.text); float[] discs = null;
            foreach (var pg in tl.pages) foreach (var pp in pg.panels) if (pp.sunDiscs != null && pp.sunDiscs.Length > 0) discs = pp.sunDiscs;
            const int W = 2048, H = 1024;
            var tex = new Texture2D(W, H, TextureFormat.RGBAHalf, false, true) { name = "IC_SkyPano", wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[W * H];
            Color below = look.C("skyBelow", new Color(0.93f, 0.75f, 0.55f)), hor = look.C("skyHorizon", new Color(0.98f, 0.8f, 0.58f)), upc = look.C("skyUpper", new Color(0.9f, 0.76f, 0.64f)), top = look.C("skyTop", new Color(0.77f, 0.75f, 0.77f));
            float ue = look.F("skyUpperElev", 11f), te = look.F("skyTopElev", 60f), dg = look.F("discGain", 3f);
            var dd = new List<Vector4>();
            if (discs != null) for (int i = 0; i + 2 < discs.Length; i += 3)
                {
                    float az = discs[i] * Mathf.Deg2Rad, el = discs[i + 1] * Mathf.Deg2Rad;
                    dd.Add(new Vector4(-Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), -Mathf.Cos(az) * Mathf.Cos(el), discs[i + 2] * 0.5f));
                }
            for (int y = 0; y < H; y++)
            {
                float lat = (1f - (y + 0.5f) / H) * Mathf.PI, e = 90f - lat * Mathf.Rad2Deg;
                Color c = e < 0 ? Color.Lerp(hor, below, Mathf.Clamp01(-e / 4f)) : e < ue ? Color.Lerp(hor, upc, Mathf.SmoothStep(0, 1, e / ue)) : Color.Lerp(upc, top, Mathf.SmoothStep(0, 1, (e - ue) / (te - ue)));
                for (int x = 0; x < W; x++)
                {
                    var col = c;
                    // "core" came out 1 - q.w * 1.04 away from the discs (-0.35 / +0.22), so the whole band -2..20 deg was 0.26 darker than
                    // the sky above it and the discs never got their HDR core; the hard step at 20 deg showed as a white ellipse in P07A.
                    // Now a real smoothstep on the angle (core 1 in the disc, 0 outside); intro_look.txt sky colours carry the band's look.
                    if (dd.Count > 0 && e > -2 && e < 40)
                    {
                        float lon = (0.5f - (x + 0.5f) / W) * 2f * Mathf.PI; var dir = new Vector3(Mathf.Sin(lat) * Mathf.Cos(lon), Mathf.Cos(lat), Mathf.Sin(lat) * Mathf.Sin(lon));
                        foreach (var q in dd)
                        {
                            float ang = Mathf.Acos(Mathf.Clamp(Vector3.Dot(dir, new Vector3(q.x, q.y, q.z)), -1, 1)) * Mathf.Rad2Deg;
                            float core = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(q.w * 0.92f, q.w * 1.04f, ang)), halo = Mathf.Exp(-Mathf.Max(0, ang - q.w) / (q.w * 1.6f)) * 0.25f;
                            col = Color.Lerp(col, Color.white, Mathf.Clamp01(halo)); col += Color.white * ((dg - 1f) * core);
                            if (core > 0.5f) col = new Color(Mathf.Max(col.r, 1f), Mathf.Max(col.g, 1f), Mathf.Max(col.b, 1f)) * (1f + (dg - 1f) * core);
                        }
                    }
                    col.a = 1; px[y * W + x] = col;
                }
            }
            tex.SetPixels(px); tex.Apply(false);
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(texPath)) AssetDatabase.DeleteAsset(texPath);
            AssetDatabase.CreateAsset(tex, texPath);
            var m = Mat(System.IO.Path.GetFileNameWithoutExtension(matPath), Shader.Find("Skybox/Panoramic"));
            m.SetTexture("_MainTex", tex); m.SetFloat("_Exposure", look.F("skyExposure", 1.3f)); m.SetFloat("_Mapping", 1f); m.SetFloat("_ImageType", 0f); m.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            EditorUtility.SetDirty(m); return m;
        }

        static AnimationClip[] Clips(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();

        static Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "Textures/" + name + ".png");

        static Material Mat(string name, Shader sh)
        {
            var path = MatDir + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(sh) { name = name }; AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != sh) m.shader = sh;
            return m;
        }

        static void Transparent(Material m, float alpha, bool twoSided)
        {
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetFloat("_AlphaClip", 0);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); m.SetFloat("_ZWrite", 0);
            m.SetFloat("_Cull", twoSided ? 0 : 2);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = (int)RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
            var c = m.GetColor("_BaseColor"); c.a = alpha; m.SetColor("_BaseColor", c);
            EditorUtility.SetDirty(m);
        }

        static GameObject Place(string model, Transform parent, Vector3 pos, float yaw, float scale, Dictionary<string, Material> remap, Dictionary<string, Material> emissive)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Models/" + model + ".fbx");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            if (parent) go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0) * asset.transform.localRotation); go.transform.localScale = go.transform.localScale * scale;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (!mats[i]) continue;
                    string n = mats[i].name;
                    if (remap.TryGetValue(n, out var m)) { mats[i] = m; continue; }
                    if (n.Contains("EyeGlow") || n.Contains("CabinLight") || n.Contains("Amber") || n.Contains("Beacon") || n.Contains("Lamp") || n.Contains("Screen"))
                    {
                        if (!emissive.TryGetValue(n, out var em))
                        {
                            em = Mat("M_Intro_" + n.Replace("M_IC_", ""), Shader.Find("Universal Render Pipeline/Lit"));
                            var src = mats[i]; var col = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : src.color;
                            if (n.Contains("EyeGlow")) col = new Color(1f, 0.72f, 0.25f);
                            if (n.Contains("Beacon")) col = new Color(1f, 0.6f, 0.2f);
                            em.SetColor("_BaseColor", col); em.EnableKeyword("_EMISSION");
                            float k = n.Contains("Screen") ? 0.2f : n.Contains("EyeGlow") ? 3.2f : 2.2f;
                            em.SetColor("_EmissionColor", col * k); em.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; em.SetFloat("_Smoothness", 0.6f);
                            EditorUtility.SetDirty(em); emissive[n] = em;
                        }
                        mats[i] = em;
                    }
                }
                r.sharedMaterials = mats;
            }
            return go;
        }
    }
}

namespace OpenZ.EditorTools
{
    /// <summary></summary>
    [InitializeOnLoad]
    static class OzIntroRecordHook
    {
        static OzIntroRecordHook()
        {
            EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.EnteredEditMode) OzIntroBuilder.ImportRecordedVideo(); };
        }
    }
}
