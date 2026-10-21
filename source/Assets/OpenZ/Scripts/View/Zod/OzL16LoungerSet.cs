// OpenZ (GPL-3.0) - Level 16: the two robots on their sun loungers at the swamp.
// The set (two loungers, parasol, side table) stands on the south bank of the north-west pool for the whole level; the two robots lie
// on it with a can of rocket fuel each. It is seen three times:
//   1. the mission-start briefing: the transmission window shows them through this set's own camera (six shots of the storyboard, cut on
//      the clock of the voice clip), while the whole screen flies over the level; the beats are the cue sheet's own names
//      (lie_can_loop, head_to_brad, forearm_up_look, forearm_down, head_to_swamp, sit_up_point, sit_square_well: round 2, the smaller
//      robot is a soldier: no knees-up pose);
//   2. during play: from the game camera, both lying;
//   3. the two endings (OzL16LoungerSet.Endings.cs, only when the endings' own code is in the game).
// View only: no collider, no rule, nothing saved. Levels 2 and 17 never build it (it attaches itself on level L16 only).
//
// What is where:
//   models    Resources/OpenZ/L16Loungers/L16_LoungerSet (JG_SunLoungerV6 with the cushion's dents as shapes, JG_BeachParasolV6,
//             JG_SideTableV6), <Robot>_LoungerBody (the robot's own body mesh with the shapes GripL / GripR: fingers laid on the can)
//   robots    the game's own prefabs (ZodStage.AlanPrefabPath / BradPrefabPath), the game's own scale rule, on the level's layers and
//             light; only the body mesh is swapped (same skeleton, bound by bone name) and the can is seated where the grip was fitted
//   clips     Resources/OpenZ/L16Loungers/Anim/<Robot>_<clip>: bone rotations + the hips' place, played by a two-input mixer on each
//             robot's Animator (no state machine); the MOUTH node is in no clip: the jaw is driven here from the voice clip's curve
//             inside each speaker's own subtitle cues, with the game's limits (ZodStage.alanJawDeg / bradJawDeg) and its 25 / 45 ms ease
//   data      Resources/OpenZ/L16Loungers/l16_lounger_scene.json (place, pieces, clips, beats, sounds, the six window cameras)
// Each piece is laid on the level's own ground (ZGame.GroundY under its footprint: height and tilt), so no foot hangs in the air.
// Play checks: OzL16LoungerSet.Status(); the menu OpenZ/Checks/L16 lounger scene (Play mode).
//
// WRITTEN BY READING ONLY: compiled off-line with the project's code, never run in the engine. Remove this line once it runs.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    [Serializable] public class OzLoungerPlace { public float x, z, yaw; }
    [Serializable] public class OzLoungerPiece { public string name, mesh, kind, baseMap, normalMap; public float x, z, footX0, footZ0, footX1, footZ1, sizeX, sizeY, sizeZ; }
    [Serializable] public class OzLoungerCan { public string hand, node, grip; public float px, py, pz, qx, qy, qz, qw; }
    [Serializable] public class OzLoungerRobot { public string who, piece, body, bodyNode; public OzLoungerCan[] cans; }
    /// <summary>Round 3: hold = the loop of small life this clip's held end goes over into when the move has played out (cross-faded over holdFade s);
    /// tilted = the can is at his mouth in this clip (the Play check does not ask for an upright can then).</summary>
    [Serializable] public class OzLoungerClip { public string who, name, res, dent, dentFrom, can; public float seconds, dentSeconds, lead, spacing; public bool loop; public string hold; public float holdFade; public bool tilted; }
    [Serializable] public class OzLoungerBeat { public string scene, who, beat, clip; public float t, fade; }
    [Serializable] public class OzLoungerSound { public float t; public string ev, at; public float vol = 1f; }
    [Serializable] public class OzLoungerShot { public int n; public string name, subjects; public float t0, t1, px, py, pz, tx, ty, tz, fov; public bool window; public float[] alanBox, bradBox, alanHead, bradHead; public string speaker;
        /// <summary>A slow steady push-in (a zoom of the lens): the lens at the shot's end (0 = none) and the second on the briefing's voice clock
        /// at which it starts; it runs on to the shot's end (soft start, then steady).</summary>
        public float zoomFov, zoomFrom; }
    [Serializable] public class OzLoungerDrop { public string who, hand; public float radius, yaw; public float[] t, x, y, z, lying; }
    [Serializable] public class OzLoungerCushion { public float length, width, top, backDeg; }
    [Serializable] public class OzLoungerSize { public float length, width, height; }
    /// <summary>Round 3 (a small lounger fitted to each robot): width = his cushion's width to his shoulder width (at most widthMost); wholeLength = his
    /// lounger from head end to foot end along its line, to his lyingLength (lying STRAIGHT; at most lengthMost); cushionFlat / footEnd = where the
    /// cushion's flat part and the frame end, from the fold: his knees and boots are past it.</summary>
    [Serializable] public class OzLoungerRatio { public string who; public float length, width, lyingLength, shoulderWidth, cushionLength, cushionWidth, cushionTop, cushionFlat, wholeLength, wholeRatio, footEnd, lengthMost, widthMost; }
    /// <summary>Round 2: the staging of one ending (set space). fortLeftDeg = ZodBriefing.EndingFortLeftDeg (90 = the fallen fort straight out past the
    /// smaller robot's side); cam* = the endings' scene camera; push = its push-in (m); mushroomScale = the far cloud's size in the scene's shots.</summary>
    [Serializable] public class OzLoungerEndStage { public float fortLeftDeg, camX, camY, camZ, camLeftDeg, camUpDeg, camFov, push, mushroomScale, fireScale, mushroomLife; public string[] hide; }
    /// <summary>The campfire of the endings: its place in the set's own space, the level's campfire it is a copy of, its burning hours, its light.</summary>
    [Serializable] public class OzLoungerCampfire { public float x, y, z, radius; public string shapeOut, shapeLit, copyOf, kind; public float onAt, offAt, lightX, lightY, lightZ; public string lightColor; public float lightIntensity, lightRange, flicker, flickerSpeed, wander; public string sound; }
    /// <summary>Round 2: a robot's own straight stance (the model's rest pose) as rotations in his root's space, and his front: the reference of the two turns.</summary>
    [Serializable] public class OzLoungerRest { public string who; public float hipsX, hipsY, hipsZ, hipsW, chestX, chestY, chestZ, chestW, headX, headY, headZ, headW, frontX, frontZ; }
    /// <summary>Play check: where that robot's joints are in the storyboard at second t of that window shot (metres in his lounger's own space).</summary>
    [Serializable] public class OzLoungerJoints { public int shot; public string who, pose; public float t; public string[] bone; public float[] x, y, z; }
    [Serializable]
    public class OzLoungerFile
    {
        public int version; public string level, setRes; public OzLoungerPlace place; public float spacingBrief, spacingEnd;
        public OzLoungerPiece[] pieces; public OzLoungerRobot[] robots; public OzLoungerClip[] clips; public OzLoungerBeat[] beats; public OzLoungerSound[] sounds;
        public OzLoungerShot[] shots; public OzLoungerDrop[] drops; public OzLoungerCushion cushion; public OzLoungerSize loungerSize; public OzLoungerRatio[] ratios;
        public OzLoungerJoints[] joints;
        public float jointTol;                                                                           // round 3: metres; 0 = the first kit's 3 cm
        public OzLoungerEndStage endWin, endLose; public OzLoungerRest[] restFacing; public float headMostDeg, chestMostDeg;   // round 2
        public OzLoungerCampfire campfire;
    }

    [DefaultExecutionOrder(150)]                                                                      // after the briefing's own Update (it moves the line clock), before the stage
    public sealed partial class OzL16LoungerSet : MonoBehaviour
    {
        public const string Level = "L16", DataPath = "OpenZ/L16Loungers/l16_lounger_scene";
        public static OzL16LoungerSet I { get; private set; }
        /// <summary>Tunable in Play: x = world x, y = world z, z = heading (deg) of the set; (0,0,0) = the data file's. Call Arrange after it.</summary>
        public static Vector3 PlaceOverride = Vector3.zero;
        /// <summary>The window camera creeps this share of its distance toward what it looks at over each shot (0 = it stands).</summary>
        public static float ShotPush = 0.06f;
        /// <summary>The window's camera is never a locked frame: metres it slides across a shot, metres and degrees of its hand-held drift.</summary>
        public static float WindowSlide = 0.07f, WindowDrift = 0.012f, WindowSway = 0.35f;
        /// <summary>A piece is tilted with the ground at most this much (deg).</summary>
        public static float MaxTilt = 8f;
        /// <summary>The servo of a gesture, dB under the voice (the two robots' own servo clips, as in the commander's HQ).</summary>
        public static float ServoDb = -22f;
        public static string LastWarning = "";
        readonly HashSet<string> holdLost = new HashSet<string>();                                         // round 3

        public sealed class Robot
        {
            public string who; public OzLoungerRobot def; public Transform root, head, mouth, piece; public Animator anim; public SkinnedMeshRenderer body, cushion;
            public Transform[] can = new Transform[2]; public OzLoungerCan[] seat = new OzLoungerCan[2]; public int[] grip = { -1, -1 }; public float[] gripW = new float[2], gripTo = new float[2];
            public Quaternion mouthRest = Quaternion.identity; public float jawSign = 1f, jawDeg = 10f, jaw, jawExternal = -1f;
            public PlayableGraph graph; public AnimationMixerPlayable mixer; public AnimationClipPlayable[] slot = new AnimationClipPlayable[2]; public int cur = -1; public float fadeT, fadeDur = 0.01f;
            public OzLoungerClip clip; public float clipT; public string hand = "L", dentFrom = "", dentTo = ""; public float dentT, dentDur;
            public bool gripOk; public readonly List<Vector2> cues = new List<Vector2>();
            public Renderer[] rends;
        }

        OzLoungerFile data; bool built, buildTried; Transform root, group; readonly Dictionary<string, Transform> pieces = new Dictionary<string, Transform>();
        readonly Dictionary<string, OzLoungerPiece> pieceDefs = new Dictionary<string, OzLoungerPiece>();
        readonly List<Robot> robots = new List<Robot>(); readonly List<Material> mats = new List<Material>();
        Camera cam; AudioSource sfx; ZGame game; float spacing, shotLift; bool commonPlane; string scene = "play";
        Transform droppingCan;                                                                           // a can that has left its hand (the endings): no clip switches it on or off
        // the briefing
        ZodStage stage; AudioSource voice; Func<float> clock; bool briefOn; int beatI, soundI, shotI = -1; float shotAt, voiceGain = 1f, lastT = -99f;
        readonly List<OzLoungerBeat> briefBeats = new List<OzLoungerBeat>(); readonly float[] jawBuf = new float[256];
        /// <summary>Play check: every beat that started a clip, with the second of the clock it started on.</summary>
        public readonly List<(float t, string scene, string who, string beat, string clip)> BeatLog = new List<(float, string, string, string, string)>();

        public OzLoungerFile Data => data;
        public IReadOnlyList<Robot> Robots => robots;
        public Transform Root => root;
        public Camera WindowCamera => cam;
        public int WindowShot => shotI >= 0 && data != null ? data.shots[shotI].n : 0;
        public bool BriefingOn => briefOn;
        public string Scene => scene;
        public float BriefClock => lastT;
        /// <summary>Play check: the voice the two robots speak with while they hold the briefing.</summary>
        public AudioSource BriefVoice => voice;
        /// <summary>The larger of the two jaws, 0..1 (the window's signal meter follows it).</summary>
        public float Mouth01 { get { float m = 0f; foreach (var r in robots) m = Mathf.Max(m, r.jaw); return m; } }
        /// <summary>Models, clips and the place are there.</summary>
        public bool Ready => EnsureBuilt();
        public Transform Piece(string name) => pieces.TryGetValue(name, out var t) ? t : null;
        public Robot Find(string who) { foreach (var r in robots) if (r.who == who) return r; return null; }

        // ------------------------------------------------------------------------------------------------ attach (level L16 only)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { I = null; LastWarning = ""; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnScene; SceneManager.sceneLoaded += OnScene; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AfterFirstScene() { TryAttach(SceneManager.GetActiveScene()); }
        static void OnScene(Scene s, LoadSceneMode m) { if (m == LoadSceneMode.Single) TryAttach(s); }

        static void TryAttach(Scene s)
        {
            if (!Application.isPlaying || I) return;
            var g = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            if (!g || g.level != Level) return;
            var go = new GameObject("OzL16LoungerSet");
            if (s.IsValid() && go.scene != s) SceneManager.MoveGameObjectToScene(go, s);
            go.AddComponent<OzL16LoungerSet>();
        }

        /// <summary>The set of this level, or null (any level but L16).</summary>
        public static OzL16LoungerSet For(string level) => level == Level ? I : null;

        void Awake() { I = this; game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>(); }
        void Start() { EnsureBuilt(); if (built && !briefOn && scene == "play") { Arrange(data.spacingBrief, false); StartLoops("brief"); } }

        void OnDestroy()
        {
            if (I == this) I = null;
            foreach (var r in robots) if (r.graph.IsValid()) r.graph.Destroy();
            foreach (var m in mats) if (m) Destroy(m);
            if (stage && cam) stage.LoungerFeed(null);
            UnbindEnding(); SceneExtrasEnd();
        }

        partial void BindEnding();
        partial void UnbindEnding();
        partial void TickEnding(float dt);
        partial void LateEnding();

        static void Warn(string s) { LastWarning = s; Debug.LogWarning("[OpenZ L16 loungers] " + s); }

        // ------------------------------------------------------------------------------------------------ build
        bool EnsureBuilt()
        {
            if (built) return true;
            if (buildTried) return false;
            buildTried = true;
            try { built = Build(); }
            catch (Exception e) { Debug.LogException(e); built = false; }
            if (built) BindEnding();
            return built;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var f = FindDeep(t.GetChild(i), name); if (f) return f; }
            return null;
        }

        static int BlendIndex(Mesh m, string key)
        {
            if (!m) return -1;
            for (int i = 0; i < m.blendShapeCount; i++) { string n = m.GetBlendShapeName(i); if (n == key || n.EndsWith("." + key)) return i; }
            return -1;
        }

        Material Mat(string name, string baseRes, string normalRes)
        {   // the game's own lit surface with the model's picture (the recipe of the level's other models)
            var m = new Material(DetailKit.Lit) { name = name, hideFlags = HideFlags.DontSave };
            var tex = Resources.Load<Texture2D>(baseRes);
            if (tex) { if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex); else m.mainTexture = tex; }
            else Warn("picture " + baseRes + " missing");
            var nrm = Resources.Load<Texture2D>(normalRes);
            if (nrm && m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", nrm); DetailKit.F(m, "_UseNormalMap", 1f); m.EnableKeyword("_USENORMALMAP_ON"); }
            DetailKit.F(m, "_UseVertexColor", 0f); DetailKit.F(m, "_TeamMode", 0f); DetailKit.F(m, "_Degreen", 0f); DetailKit.F(m, "_Stylize", 1f); DetailKit.F(m, "_Degrime", 0f); DetailKit.F(m, "_FlatShade", 0f);
            DetailKit.F(m, "_SoftFill", 0.2f); DetailKit.F(m, "_AmbientBoost", 1f); DetailKit.F(m, "_Smoothness", 0.22f); DetailKit.F(m, "_Metallic", 0f); DetailKit.F(m, "_NightClass", 0f);
            DetailKit.C(m, "_BaseColor", Color.white); DetailKit.C(m, "_EmissionColor", Color.black);
            mats.Add(m);
            return m;
        }

        bool Build()
        {
            var ta = Resources.Load<TextAsset>(DataPath);
            if (!ta) { Warn("data file Resources/" + DataPath + " missing: no lounger set"); return false; }
            data = JsonUtility.FromJson<OzLoungerFile>(ta.text);
            if (data == null || data.pieces == null || data.robots == null || data.clips == null || data.shots == null) { Warn("data file not readable"); return false; }
            var model = Resources.Load<GameObject>(data.setRes);
            if (!model) { Warn("model Resources/" + data.setRes + " missing: no lounger set"); return false; }
            var pa = Resources.Load<GameObject>(ZodStage.AlanPrefabPath); var pb = Resources.Load<GameObject>(ZodStage.BradPrefabPath);
            if (!pa || !pb) { Warn("the two robots' prefabs are missing (Resources/" + ZodStage.AlanPrefabPath + ", " + ZodStage.BradPrefabPath + ")"); return false; }

            root = new GameObject("L16LoungerSet").transform; root.SetParent(transform, false);
            group = new GameObject("Group").transform; group.SetParent(root, false);
            var matByTex = new Dictionary<string, Material>();
            foreach (var p in data.pieces)
            {
                var src = FindDeep(model.transform, p.mesh);
                if (!src) { Warn("mesh " + p.mesh + " not in " + data.setRes); return false; }
                // a holder at the piece's place; the model under it keeps the turn and the size its import gave it
                var go = new GameObject(p.name); go.transform.SetParent(group, false); go.transform.localPosition = new Vector3(p.x, 0f, p.z);
                var inst = Instantiate(src.gameObject, go.transform);
                inst.name = p.mesh; inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = src.localRotation; inst.transform.localScale = src.localScale;
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
                if (!matByTex.TryGetValue(p.baseMap, out var m)) matByTex[p.baseMap] = m = Mat("L16Lounger_" + p.kind, p.baseMap, p.normalMap);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; r.gameObject.layer = 0;
                    if (r is SkinnedMeshRenderer s) s.updateWhenOffscreen = true;
                }
                pieces[p.name] = go.transform; pieceDefs[p.name] = p;
            }
            var st = FindFirstObjectByType<ZodStage>();
            float bradScale = st ? st.boysModelScale : 0.84f, alanMul = st ? st.alanScale : 1.19f;
            foreach (var rd in data.robots)
            {
                if (!pieces.TryGetValue(rd.piece, out var piece)) { Warn("piece " + rd.piece + " missing"); return false; }
                bool alan = rd.who == "alan";
                var go = Instantiate(alan ? pa : pb);
                go.name = alan ? "L16Lounger_Alan" : "L16Lounger_Brad"; go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var r = new Robot { who = rd.who, def = rd, root = go.transform, piece = piece, jawDeg = alan ? (st ? st.alanJawDeg : 16f) : (st ? st.bradJawDeg : 10f) };
                r.head = FindDeep(go.transform, "Head"); r.mouth = FindDeep(go.transform, "MOUTH");
                if (r.mouth)
                {   // which way opens the jaw: a point in front of the hinge must DROP (measured on the standing model, as the game does)
                    r.mouthRest = r.mouth.localRotation;
                    Vector3 front = -go.transform.forward, local = Quaternion.Inverse(r.mouth.rotation) * front * 0.1f;
                    float y0 = r.mouth.TransformPoint(local).y;
                    r.mouth.localRotation = r.mouthRest * Quaternion.Euler(10f, 0f, 0f);
                    r.jawSign = r.mouth.TransformPoint(local).y < y0 ? 1f : -1f;
                    r.mouth.localRotation = r.mouthRest;
                }
                foreach (var tr in go.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = 0;
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
                foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Destroy(rb);
                r.rends = go.GetComponentsInChildren<Renderer>(true);
                foreach (var rr in r.rends)
                {
                    rr.renderingLayerMask = 1u; rr.shadowCastingMode = ShadowCastingMode.On; rr.receiveShadows = true;
                    if (rr is SkinnedMeshRenderer s) s.updateWhenOffscreen = true;
                    rr.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;                    // as every living unit (UnitLookMotionGate): a pose step at a window cut never smears the body
                }
                // the body with its grips: the same skeleton, bound to this robot's own bones by name
                var bodyT = FindDeep(go.transform, rd.bodyNode); r.body = bodyT ? bodyT.GetComponent<SkinnedMeshRenderer>() : null;
                var bm = Resources.Load<GameObject>(rd.body); var srcSmr = bm ? bm.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
                if (r.body && srcSmr && srcSmr.sharedMesh)
                {
                    var bones = new Transform[srcSmr.bones.Length]; bool ok = true;
                    for (int i = 0; i < bones.Length; i++) { bones[i] = srcSmr.bones[i] ? FindDeep(go.transform, srcSmr.bones[i].name) : null; if (!bones[i]) ok = false; }
                    if (ok) { r.body.sharedMesh = srcSmr.sharedMesh; r.body.bones = bones; r.gripOk = true; }
                }
                if (!r.gripOk) Warn(rd.who + ": the body with the grip shapes (Resources/" + rd.body + ") is missing or does not fit: the fingers stay as modelled, they will cut the can");
                foreach (var cd in rd.cans)
                {
                    int k = cd.hand == "R" ? 1 : 0;
                    r.can[k] = FindDeep(go.transform, cd.node); r.seat[k] = cd;
                    r.grip[k] = r.gripOk ? BlendIndex(r.body.sharedMesh, cd.grip) : -1;
                    if (!r.can[k]) Warn(rd.who + ": can node " + cd.node + " missing in the prefab");
                    else
                    {   // seated where the grip was fitted; ONE standard can for both robots (the game's rule: the smaller robot's own scale does not grow it)
                        r.can[k].localPosition = new Vector3(cd.px, cd.py, cd.pz); r.can[k].localRotation = new Quaternion(cd.qx, cd.qy, cd.qz, cd.qw);
                        r.can[k].localScale = Vector3.one * (alan ? 1f / Mathf.Max(0.01f, alanMul) : 1f);
                        r.can[k].gameObject.SetActive(false);
                    }
                }
                go.transform.SetParent(piece, false);
                go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one * (alan ? bradScale * alanMul : bradScale);
                r.cushion = piece.GetComponentInChildren<SkinnedMeshRenderer>(true);
                r.anim = go.GetComponentInChildren<Animator>(true);
                if (!r.anim) { Warn(rd.who + ": no Animator on the prefab"); return false; }
                r.anim.runtimeAnimatorController = null; r.anim.applyRootMotion = false; r.anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                r.graph = PlayableGraph.Create("L16Lounger_" + rd.who); r.graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                r.mixer = AnimationMixerPlayable.Create(r.graph, 2);
                AnimationPlayableOutput.Create(r.graph, "pose", r.anim).SetSourcePlayable(r.mixer);
                r.graph.Play();
                robots.Add(r);
            }
            // the window's camera: a second view of the level, rendered into the transmission window's own picture
            var cgo = new GameObject("L16LoungerWindowCamera"); cgo.transform.SetParent(root, false);
            cam = cgo.AddComponent<Camera>(); var main = Camera.main;
            if (main) cam.CopyFrom(main);
            cam.enabled = false; cam.targetTexture = null; cam.depth = -30f; cam.nearClipPlane = 0.1f; cam.farClipPlane = Mathf.Min(main ? main.farClipPlane : 600f, 600f); cam.rect = new Rect(0f, 0f, 1f, 1f);
            cam.cullingMask = (main ? main.cullingMask : ~0) & ~(1 << 5) & ~(1 << ZodStage.ResolveLayer());
            var cd2 = cam.GetUniversalAdditionalCameraData(); cd2.renderPostProcessing = true; cd2.renderShadows = true; cd2.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            sfx = gameObject.AddComponent<AudioSource>(); sfx.playOnAwake = false; sfx.spatialBlend = 0f; sfx.priority = 40;
            int lost = 0;                                                                                // every clip is loaded now: no hitch on a beat
            foreach (var c in data.clips) if (!Resources.Load<AnimationClip>(c.res)) lost++;
            if (lost > 0) Warn(lost + " of " + data.clips.Length + " clips are missing under Resources/OpenZ/L16Loungers/Anim");
            foreach (var b in data.beats) if (b.scene == "brief" && b.t > -900f) briefBeats.Add(b);
            briefBeats.Sort((a, b) => a.t.CompareTo(b.t));
            Array.Sort(data.sounds ?? (data.sounds = new OzLoungerSound[0]), (a, b) => a.t.CompareTo(b.t));
            Arrange(data.spacingBrief, false);
            Debug.Log($"[OpenZ L16 loungers] built at ({root.position.x:F1}, {root.position.y:F2}, {root.position.z:F1}) heading {root.eulerAngles.y:F0}: {pieces.Count} pieces, {robots.Count} robots, {data.clips.Length} clips, grips {(robots.TrueForAll(r => r.gripOk) ? "in" : "MISSING")}");
            return true;
        }

        // ------------------------------------------------------------------------------------------------ on the level's ground
        float Ground(Vector3 w) => game ? game.GroundY(w.x, w.z) : 0f;

        /// <summary>Position and tilt of a footprint (corners in `frame` space) on the ground: the plane through its four corner heights.</summary>
        void Lay(Transform t, Transform frame, Vector3 centre, float x0, float z0, float x1, float z1, Quaternion heading)
        {
            Vector3 a = frame.TransformPoint(centre + new Vector3(x0, 0f, z0)), b = frame.TransformPoint(centre + new Vector3(x1, 0f, z0)),
                    c = frame.TransformPoint(centre + new Vector3(x1, 0f, z1)), d = frame.TransformPoint(centre + new Vector3(x0, 0f, z1));
            a.y = Ground(a); b.y = Ground(b); c.y = Ground(c); d.y = Ground(d);
            Vector3 n = Vector3.Cross(c - a, d - b).normalized; if (n.y < 0f) n = -n;
            float tilt = Vector3.Angle(Vector3.up, n);
            if (tilt > MaxTilt) n = Vector3.Slerp(Vector3.up, n, MaxTilt / tilt);
            Vector3 mid = (a + b + c + d) * 0.25f; Vector3 at = frame.TransformPoint(centre);
            // the plane through the middle of the four corners, read at the piece's own origin
            at.y = mid.y - (n.x * (at.x - mid.x) + n.z * (at.z - mid.z)) / Mathf.Max(0.2f, n.y);
            t.SetPositionAndRotation(at, Quaternion.FromToRotation(Vector3.up, n) * heading);
        }

        /// <summary>The set on its place. spacing = metres between the two loungers' middles. common = the two loungers and the table as
        /// ONE stiff group on their common ground plane (the endings: the cans must meet); otherwise every piece on its own ground.</summary>
        public void Arrange(float spacingM, bool common) { Arrange(spacingM, common, PlaceOverride.sqrMagnitude > 0.01f ? new Vector3(PlaceOverride.x, 0f, PlaceOverride.y) : new Vector3(data.place.x, 0f, data.place.z), Quaternion.Euler(0f, PlaceOverride.sqrMagnitude > 0.01f ? PlaceOverride.z : data.place.yaw, 0f)); }

        public void Arrange(float spacingM, bool common, Vector3 place, Quaternion heading)
        {
            spacing = spacingM; commonPlane = common;
            place.y = Ground(place);
            root.SetPositionAndRotation(place, heading);
            group.localPosition = Vector3.zero; group.localRotation = Quaternion.identity;
            foreach (var kv in pieces)
            {
                var p = pieceDefs[kv.Key];
                float x = p.kind == "lounger" ? Mathf.Sign(p.x) * spacingM * 0.5f : p.x;
                kv.Value.localPosition = new Vector3(x, 0f, p.z); kv.Value.localRotation = Quaternion.identity;
            }
            if (common)
            {   // one plane under both loungers
                float hx = 0f, fz0 = 0f, fz1 = 0f;                                                        // round 3: the loungers' own footprints, not the first kit's numbers
                foreach (var kv in pieces)
                {
                    var q = pieceDefs[kv.Key]; if (q.kind != "lounger") continue;
                    hx = Mathf.Max(hx, spacingM * 0.5f + Mathf.Max(-q.footX0, q.footX1)); fz0 = Mathf.Min(fz0, q.footZ0); fz1 = Mathf.Max(fz1, q.footZ1);
                }
                if (hx <= 0f) { hx = spacingM * 0.5f + 0.86f; fz0 = -1.85f; fz1 = 1.09f; }
                Lay(group, root, Vector3.zero, -hx, fz0, hx, fz1, heading);
                foreach (var kv in pieces) if (pieceDefs[kv.Key].kind == "parasol") { var p = pieceDefs[kv.Key]; Lay(kv.Value, root, new Vector3(p.x, 0f, p.z), p.footX0, p.footZ0, p.footX1, p.footZ1, heading); kv.Value.rotation = heading; }
            }
            else
                foreach (var kv in pieces)
                {
                    var p = pieceDefs[kv.Key]; Vector3 c = kv.Value.localPosition;
                    Lay(kv.Value, root, c, p.footX0, p.footZ0, p.footX1, p.footZ1, heading);
                    if (p.kind == "parasol") kv.Value.rotation = heading;                               // a parasol stands upright
                }
            // the window's shots are measured from the ground the two loungers stand on: their mean height over the set's own origin
            float sum = 0f; int n = 0;
            foreach (var kv in pieces) if (pieceDefs[kv.Key].kind == "lounger") { sum += kv.Value.position.y; n++; }
            shotLift = n > 0 ? sum / n - root.position.y : 0f;
        }

        // ------------------------------------------------------------------------------------------------ clips
        OzLoungerClip Clip(string who, string name) { foreach (var c in data.clips) if (c.who == who && c.name == name) return c; return null; }

        OzLoungerBeat BeatDef(string sc, string who, string beat)
        {
            OzLoungerBeat any = null;
            foreach (var b in data.beats)
            {
                if (b.who != who || b.beat != beat) continue;
                if (b.scene == sc) return b;
                if (b.scene == "end" && (sc == "win" || sc == "lose")) any = b;
            }
            return any;
        }

        /// <summary>Play check: this scene ("brief", "win", "lose") has a clip for that beat of that robot.</summary>
        public bool HasBeat(string sc, string who, string beat)
        {
            if (who == "both") return HasBeat(sc, "alan", beat) && HasBeat(sc, "brad", beat);
            var b = BeatDef(sc, who, beat);
            return b != null && !string.IsNullOrEmpty(b.clip) && Clip(who, b.clip) != null && Resources.Load<AnimationClip>(Clip(who, b.clip).res);
        }

        public bool Play(Robot r, string clipName, float fade, float speed = 1f)
        {
            var cd = Clip(r.who, clipName);
            var ac = cd != null ? Resources.Load<AnimationClip>(cd.res) : null;
            if (!ac) { Warn(r.who + ": clip " + clipName + " missing (Resources/" + (cd != null ? cd.res : "?") + ")"); return false; }
            int next = r.cur == 0 ? 1 : 0;
            if (r.slot[next].IsValid()) { r.mixer.DisconnectInput(next); r.slot[next].Destroy(); }
            var p = AnimationClipPlayable.Create(r.graph, ac); p.SetApplyFootIK(false); p.SetSpeed(Mathf.Clamp(speed, 0.4f, 2.5f)); p.SetTime(0);
            r.graph.Connect(p, 0, r.mixer, next); r.slot[next] = p;
            r.fadeDur = r.cur < 0 ? 0f : Mathf.Max(0f, fade); r.fadeT = 0f; r.cur = next;
            if (r.fadeDur <= 0f) { r.mixer.SetInputWeight(next, 1f); if (r.slot[1 - next].IsValid()) r.mixer.SetInputWeight(1 - next, 0f); }
            else { r.mixer.SetInputWeight(next, 0f); if (r.slot[1 - next].IsValid()) r.mixer.SetInputWeight(1 - next, 1f); }   // the fade starts from the old pose: a new port comes connected at weight 1
            r.clip = cd; r.clipT = 0f;
            r.dentFrom = string.IsNullOrEmpty(r.dentTo) ? cd.dent : r.dentTo; r.dentTo = cd.dent; r.dentT = 0f; r.dentDur = r.dentFrom == r.dentTo ? 0f : Mathf.Max(0.05f, cd.dentSeconds > 0f ? cd.dentSeconds : 0.25f);
            SetHand(r, cd.can);
            return true;
        }

        void SetHand(Robot r, string hand)
        {
            r.hand = hand;
            for (int k = 0; k < 2; k++)
            {
                bool on = (k == 1) == (hand == "R");
                if (r.can[k] && r.can[k].gameObject.activeSelf != on && !(droppingCan && droppingCan == r.can[k])) r.can[k].gameObject.SetActive(on);
                r.gripTo[k] = on && !(droppingCan && droppingCan == r.can[k]) ? 100f : 0f;            // round 3d: a hand whose can has left it stays open
                if (r.gripW[k] <= 0f && on || !on) r.gripW[k] = r.gripTo[k];                         // a hand closes with its can at once; it opens at once when the can is in the other hand
            }
        }

        void StartLoops(string sc, float fade = 0f)
        {
            foreach (var r in robots) { var b = BeatDef(sc, r.who, "lie_can_loop"); Play(r, b != null && !string.IsNullOrEmpty(b.clip) ? b.clip : "lie_can_loop", fade); r.jawExternal = -1f; }
        }

        /// <summary>A beat of a cue sheet: who = alan / brad / both, beat = its name, sc = brief / win / lose.</summary>
        public bool BeatIn(string sc, string who, string beat, float speed = 1f, float clockT = float.NaN)
        {
            if (!EnsureBuilt()) return false;
            if (who == "both") { bool a = BeatIn(sc, "alan", beat, speed, clockT), b2 = BeatIn(sc, "brad", beat, speed, clockT); return a || b2; }
            var r = Find(who); if (r == null) return false;
            var b = BeatDef(sc, who, beat);
            if (b == null || string.IsNullOrEmpty(b.clip)) return false;                               // a beat without a move ("no_move")
            bool ok = Play(r, b.clip, b.fade, speed);
            if (ok) BeatLog.Add((clockT, sc, who, beat, b.clip));
            return ok;
        }

        // ------------------------------------------------------------------------------------------------ the briefing
        /// <summary>ZodBriefing: the two robots hold this line. cuesA / cuesB = each speaker's cues on the voice clip (start, end);
        /// clock = seconds on the voice clip (negative before it), +infinity = the line is over and the window is closing, NaN = gone.</summary>
        public bool BriefBegin(ZodStage st, AudioSource voiceSource, List<Vector2> cuesA, List<Vector2> cuesB, Func<float> lineClock, float gain)
        {
            if (!EnsureBuilt() || !st) return false;
            stage = st; voice = voiceSource; clock = lineClock; voiceGain = gain; scene = "brief";
            Arrange(data.spacingBrief, false); StartLoopsKeep("brief"); PrepareWindow();                 // no pose jump, no occlusion (OzL16LoungerSet.Window.cs)
            var a = Find("alan"); var b = Find("brad");
            a.cues.Clear(); b.cues.Clear(); if (cuesA != null) a.cues.AddRange(cuesA); if (cuesB != null) b.cues.AddRange(cuesB);
            beatI = soundI = 0; shotI = -1; lastT = -99f; BeatLog.Clear();
            briefOn = true;
            stage.LoungerFeed(cam);
            Debug.Log($"[OpenZ L16 loungers] briefing on the loungers: {a.cues.Count} + {b.cues.Count} cues, {briefBeats.Count} beats, {data.sounds.Length} sounds, window camera into the transmission feed");
            return true;
        }

        void BriefEnd()
        {
            if (!briefOn) return;
            briefOn = false; scene = "play"; clock = null; voice = null;
            if (stage) stage.LoungerFeed(null);
            foreach (var r in robots) { r.cues.Clear(); r.jaw = 0f; }
            Arrange(data.spacingBrief, false);                                                            // on the ground as it is now
            StartLoops("brief", 0.9f);                                                                    // both lie down again for the game camera (eased, no jump)
            ZodBriefing.BoysHostEnd();
            Debug.Log("[OpenZ L16 loungers] briefing over: both lying, the window is the commander's again");
        }

        void TickBrief()
        {
            float t = clock != null ? clock() : float.NaN;
            if (float.IsNaN(t)) { BriefEnd(); return; }
            bool over = float.IsPositiveInfinity(t);
            if (!over)
            {
                lastT = t;
                while (beatI < briefBeats.Count && briefBeats[beatI].t <= t) { var b = briefBeats[beatI++]; BeatIn("brief", b.who, b.beat, 1f, t); }
                while (soundI < data.sounds.Length && data.sounds[soundI].t <= t) Sound(data.sounds[soundI++], "brief");
            }
            // the window's shot: the last window shot that has begun (the whole-screen cut to the crocodiles is not the window's: it stays where it is)
            float ts = over ? lastT : t; int want = -1;
            for (int i = 0; i < data.shots.Length; i++) if (data.shots[i].window && (want < 0 || data.shots[i].t0 <= ts)) want = i;
            if (want != shotI) { shotI = want; shotAt = Time.unscaledTime; OnWindowCut(); }
        }

        void PlaceWindowCamera()
        {
            if (!cam || shotI < 0) return;
            var s = data.shots[shotI];
            if (ChaseWindow(s)) return;                                                                  // the chase shot (OzL16LoungerSet.Window.cs)
            Vector3 p = root.TransformPoint(new Vector3(s.px, s.py, s.pz)), at = root.TransformPoint(new Vector3(s.tx, s.ty, s.tz));
            float len = Mathf.Max(0.5f, s.t1 - s.t0), u = Mathf.Clamp01((Time.unscaledTime - shotAt) / len), e = u * u * (3f - 2f * u);
            p.y += shotLift; at.y += shotLift;
            p = Vector3.Lerp(p, at, ShotPush * e);
            // a slow slide across the shot and a gentle hand-held drift (calm screen: the slide only)
            var look = Quaternion.LookRotation(at - p, Vector3.up); float now = Time.unscaledTime;
            p += look * new Vector3((e - 0.5f) * WindowSlide * ((shotI & 1) == 0 ? 1f : -1f), 0f, 0f);
            look = Quaternion.LookRotation(at - p, Vector3.up);
            if (!OzMotion.Reduced)
            {
                float n1 = Mathf.PerlinNoise(now * 0.55f, 3.7f + shotI) - 0.5f, n2 = Mathf.PerlinNoise(8.3f + shotI, now * 0.5f) - 0.5f, n3 = Mathf.PerlinNoise(now * 0.45f, 12.9f) - 0.5f;
                p += look * new Vector3(n2 * 2f * WindowDrift, n1 * 2f * WindowDrift, 0f);
                look = Quaternion.LookRotation(at - p, Vector3.up) * Quaternion.Euler(n1 * 2f * WindowSway, n2 * 2f * WindowSway, n3 * 1.6f * WindowSway);
            }
            cam.transform.SetPositionAndRotation(p, look);
            cam.fieldOfView = ShotLens(s);
        }

        /// <summary>The lens of a window shot now: its own, or its push-in (zoomFov from zoomFrom on the voice clock to the shot's end).</summary>
        float ShotLens(OzLoungerShot s)
        {
            if (s.zoomFov <= 0f || Mathf.Abs(s.zoomFov - s.fov) < 0.01f || lastT < -50f) return s.fov;
            float z = Mathf.Clamp01((lastT - s.zoomFrom) / Mathf.Max(0.3f, s.t1 - s.zoomFrom)), k = ZoomSoftStart;
            z = z < k ? z * z / (2f * k) : z - k * 0.5f;                                                    // soft start, then a steady speed to the cut
            return Mathf.Lerp(s.fov, s.zoomFov, z / (1f - k * 0.5f));
        }

        /// <summary>The share of a push-in over which it eases in (the rest runs at one steady speed).</summary>
        public static float ZoomSoftStart = 0.25f;

        float JawFromVoice(Robot r)
        {
            var v = voice;
            if (!v || !v.clip || !v.isPlaying || r.cues.Count == 0) return 0f;
            float t = v.timeSamples / (float)Mathf.Max(1, v.clip.frequency); bool mine = false;
            foreach (var c in r.cues) if (t >= c.x - 0.12f && t <= c.y + 0.2f) { mine = true; break; }       // only his own words
            if (!mine) return 0f;
            float j = ZodStage.LoungerJaw(v.clip, t);
            if (j >= 0f) return j;
            v.GetOutputData(jawBuf, 0);                                                                    // no curve for this clip: the live level, as the game does
            double sum = 0; for (int i = 0; i < jawBuf.Length; i++) sum += jawBuf[i] * jawBuf[i];
            float rms = Mathf.Sqrt((float)(sum / jawBuf.Length)) / Mathf.Max(0.05f, v.volume);
            float k = Mathf.Clamp01((20f * Mathf.Log10(rms + 1e-6f) + 40f) / 24f);
            return k * k;
        }

        /// <summary>Every frame from outside (the endings): how far that robot's jaw is open, 0..1.</summary>
        public void Jaw(string who, float open01) { var r = Find(who); if (r != null) r.jawExternal = Mathf.Clamp01(open01); }

        /// <summary>World place of "alan", "brad" or "between" (where the two cans meet) for a sound.</summary>
        public Vector3 Point(string at)
        {
            if (!built) return transform.position;
            var r = Find(at);
            if (r != null) return r.head ? r.head.position : r.root.position;
            return group.TransformPoint(new Vector3(0f, 1.1f, 0f));
        }

        void Sound(OzLoungerSound s, string src)
        {
            float vol = s.vol > 0f ? s.vol : 1f;
            if (s.ev == "zod_boys_servo")
            {   // the two robots' own servo clips, played by code under the voice (not a sound-table event), and written into the sound log
                string res = "OpenZ/Audio/Sfx/sd_robot_servo_" + "abc"[UnityEngine.Random.Range(0, 3)];
                var c = Resources.Load<AudioClip>(res);
                float g = Mathf.Clamp01(voiceGain * Mathf.Pow(10f, ServoDb / 20f) * vol);
                if (c) { sfx.pitch = UnityEngine.Random.Range(0.95f, 1.05f); sfx.PlayOneShot(c, g); OzSoundLog.Add("L16Loungers", src + "_" + s.at + "_servo", c.name, "played", g); }
                else OzSoundLog.Add("L16Loungers", src + "_" + s.at + "_servo", res, "missing clip");
                return;
            }
            if (!AudioDirector.Has(s.ev)) { OzSoundLog.Add("L16Loungers", src + "_" + s.ev, s.ev, "missing event"); Warn("sound event " + s.ev + " missing"); return; }
            // "2d" = part of the transmission: played at the listener. The briefing's own sounds are all "2d": while the window shows the
            // set, the main camera flies over another part of the level, and the sound table drops a 3D event of this kind whose place
            // is not in the MAIN camera's picture ("DROP off screen"): a creak at the robot's head would never be heard.
            float now = Time.unscaledTime; int n0 = TableLines(s.ev, now, out _);
            if (s.at == "2d") AudioDirector.UI(s.ev, vol);
            else AudioDirector.Fire(s.ev, Point(s.at), s.at == "alan" ? -1611 : s.at == "brad" ? -1612 : -1613, vol);
            // what became of it, from the sound table's own line of this frame, written as this set's line (the Play check reads these)
            int n1 = TableLines(s.ev, now, out string last);
            OzSoundLog.Add("L16Loungers", src + "_" + s.ev, s.ev, n1 > n0 ? (last.StartsWith("PLAY") ? "played" : last) : "sent, no line of the sound table", vol);
        }

        static int TableLines(string ev, float since, out string last)
        {
            last = ""; int n = 0;
            foreach (var e in OzSoundLog.Since(since)) if (e.src == "AD" && e.key == ev) { n++; last = e.outcome ?? ""; }
            return n;
        }

        // ------------------------------------------------------------------------------------------------ frame
        void Update()
        {
            if (!built) return;
            bool paused = AudioListener.pause || Time.timeScale <= 0f && !briefOn;
            float dt = paused ? 0f : Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (briefOn) TickBrief();
            TickEnding(dt);
            foreach (var r in robots)
            {
                if (r.cur >= 0 && r.fadeDur > 0f && r.fadeT < r.fadeDur)
                {
                    r.fadeT = Mathf.Min(r.fadeDur, r.fadeT + dt); float k = r.fadeT / r.fadeDur; k = k * k * (3f - 2f * k);
                    r.mixer.SetInputWeight(r.cur, k); if (r.slot[1 - r.cur].IsValid()) r.mixer.SetInputWeight(1 - r.cur, 1f - k);
                }
                r.clipT += dt;
                // ROUND 3: a held pose is not a statue. When a move has played out, the pose
                // goes over into its own loop of small life (breath, head, hands, can, a boot: the data file's clip row "hold"), cross-faded.
                if (r.clip != null && !r.clip.loop && !string.IsNullOrEmpty(r.clip.hold) && r.cur >= 0 && r.slot[r.cur].IsValid() && r.slot[r.cur].GetTime() >= r.clip.seconds
                    && !holdLost.Contains(r.who + "/" + r.clip.hold))
                {
                    string hold = r.clip.hold; float hf = r.clip.holdFade > 0f ? r.clip.holdFade : 0.35f;
                    if (Clip(r.who, hold) == null || !Play(r, hold, hf)) holdLost.Add(r.who + "/" + hold);
                }
                r.graph.Evaluate(dt);
                // the cushion's dent follows the body
                if (r.cushion && r.cushion.sharedMesh)
                {
                    r.dentT = Mathf.Min(r.dentDur, r.dentT + dt); float w = r.dentDur <= 0f ? 1f : r.dentT / r.dentDur; var m = r.cushion.sharedMesh;
                    for (int i = 0; i < m.blendShapeCount; i++)
                    {
                        string n = m.GetBlendShapeName(i); bool to = n == r.dentTo || n.EndsWith("." + r.dentTo), from = n == r.dentFrom || n.EndsWith("." + r.dentFrom);
                        r.cushion.SetBlendShapeWeight(i, to ? (from ? 100f : 100f * w) : from ? 100f * (1f - w) : 0f);
                    }
                }
                for (int k = 0; k < 2; k++)
                {
                    if (r.grip[k] < 0) continue;
                    r.gripW[k] = Mathf.MoveTowards(r.gripW[k], r.gripTo[k], dt * 100f / 0.12f);
                    r.body.SetBlendShapeWeight(r.grip[k], r.gripW[k]);
                }
            }
        }

        /// <summary>Pictures and checks only: the set is drawn in normal play too.</summary>
        public static bool DrawInPlay;
        /// <summary>The set and the two robots are drawn right now.</summary>
        public bool Drawn => drawn;
        bool drawn = true; readonly List<Renderer> hiddenParts = new List<Renderer>();
        /// <summary>The set and the two lying robots are drawn only while they hold the briefing or an ending. In normal play they
        /// are hidden: units walk through that place. What was drawn is remembered and comes back as it was.</summary>
        void SetDrawn(bool on)
        {
            drawn = on;
            if (!on) { hiddenParts.Clear(); foreach (var r in root.GetComponentsInChildren<Renderer>(true)) if (r.enabled) { r.enabled = false; hiddenParts.Add(r); } }
            else { foreach (var r in hiddenParts) if (r) r.enabled = true; hiddenParts.Clear(); }
        }

        void LateUpdate()
        {
            if (!built) return;
            bool wantDrawn = briefOn || scene != "play" || DrawInPlay;
            if (wantDrawn != drawn) SetDrawn(wantDrawn);
            // Each can sits where its grip was fitted. The prefab's own controller used to move the can's place: when it is taken
            // off (Build), the engine puts that place back to the prefab's, AFTER the seat was set: the can then sat in the palm with
            // the fingers through it (found in the engine: 578 of the smaller robot's 1450 hand points inside the can). So the seat
            // is held here, every frame, for every can that is still in its hand. In an ending the ending's own code holds the cans
            // (it shakes them on the far boom, from the place it finds: the seat).
            if (scene == "play" || scene == "brief")
            foreach (var r in robots)
                for (int k = 0; k < 2; k++)
                    if (r.can[k] && r.seat[k] != null && r.can[k].parent && !(droppingCan && droppingCan == r.can[k]) && r.can[k].parent.name.StartsWith("HAND"))
                        r.can[k].localPosition = new Vector3(r.seat[k].px, r.seat[k].py, r.seat[k].pz);
            float dt = AudioListener.pause ? 0f : Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            foreach (var r in robots)
            {
                float target = r.jawExternal >= 0f ? r.jawExternal : briefOn ? JawFromVoice(r) : 0f;
                if (r.jawExternal >= 0f) r.jaw = target;                                                  // the endings ease it themselves
                else if (dt > 0f) r.jaw += (target - r.jaw) * (1f - Mathf.Exp(-dt / (target > r.jaw ? 0.025f : 0.045f)));   // open 25 ms, close 45 ms (the game's own)
                r.jaw = Mathf.Clamp01(r.jaw);
                if (r.mouth) r.mouth.localRotation = r.mouthRest * Quaternion.Euler(r.jaw * r.jawDeg * r.jawSign, 0f, 0f);
                // the smaller robot's chin lifts a little with each open syllable (the game's own talking overlay; the bigger one moves his jaw only)
                ChinLift(r, dt);                                                                          // eased, no nod on every syllable (OzL16LoungerSet.Window.cs)
            }
            if (briefOn) PlaceWindowCamera();
            LateEnding();
            SceneExtrasLate();
        }

        /// <summary>One-line state for the Play check.</summary>
        public static string Status()
        {
            if (!I) return "lounger set: none in this scene (level L16 only)";
            if (!I.built) return "lounger set: NOT built (" + LastWarning + ")";
            var a = I.Find("alan"); var b = I.Find("brad");
            return $"lounger set: {I.scene}, at ({I.root.position.x:F1}, {I.root.position.y:F2}, {I.root.position.z:F1}) heading {I.root.eulerAngles.y:F0}, spacing {I.spacing:F2} m{(I.commonPlane ? " (one plane)" : "")}, "
                 + $"alan {(a.clip != null ? a.clip.name : "-")} jaw {a.jaw:F2}, brad {(b.clip != null ? b.clip.name : "-")} jaw {b.jaw:F2}, window shot {I.WindowShot}, clock {I.lastT:F2} s, grips {(a.gripOk && b.gripOk ? "in" : "MISSING")}"
                 + (LastWarning.Length > 0 ? " | last warning: " + LastWarning : "");
        }
    }
}
