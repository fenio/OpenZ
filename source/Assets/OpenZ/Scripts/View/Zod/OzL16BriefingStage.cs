// OpenZ (GPL-3.0)
// The flight itself is the level's shot list (ZodFlyover.L16.cs); this small class only watches
// which shot is on screen and how far into it (ZodFlyover.CineShown) and puts the animals there:
//   dawn_swamp, rim_fall_parrots   a parrot flock that passes the lens at a set second of the shot (flutter + squawks)
//   pool_glide                     a crocodile that swims a set path through the frame for the whole shot (V wake, wake sound)
//   crocs                          the near crocodile, facing the lens: head up, jaws wide, SNAP at a set second (swell, snap, splash)
// Everything it shows is a pure function of the shot and the seconds into it, so a held frame (ZodFlyover.HoldShot) shows the same
// picture as the running flight, a paused briefing stands still, and the Play check can count it.
// NO BIRD APPEARS OR VANISHES INSIDE THE PICTURE. A crossing flock's lines run on at both ends until they begin and end outside the
// view (one wing span, at the real picture shape) or before the cut into the shot; a flock that comes straight at the lens is
// already in the air when its shot begins (Build).
// THE ANIMALS ARE THE LEVEL'S OWN. The level's animal code registers itself in OzL16BriefingStage.Animals (IOzL16BriefingAnimals; the
// animals step brings the adapter OzL16AnimalsForBriefing.cs): it lends the crocodile nearest to a place, shows the take the stage
// asks for, and takes the animal back. At the hand-back, on a skip, or when the scene goes, every lent animal is returned at once:
// it is back at its own place under the level's normal rules. While the level draws crocodiles of its own the stage NEVER builds
// one: two animals in one place would be the result.
// FALLBACK while the level has no animal code at all: the stage builds its own actors from the SAME files in the SAME folder the
// animals step uses (Resources/OpenZ/L16Animals: Croc, Parrot_perched, Croc_SurfaceFx and their pictures) and gives them their
// materials in code, by the recipe of the level's animals (the game's lit surface with the animal's own picture): no imported
// material is relied on. When a model or its picture is missing the cue is left out with ONE warning: the stage never shows a
// stand-in, and never a grey animal.
// NOTHING IS BUILT IN THE FRAME IT FIRST SHOWS: birds, the stage's own crocodiles, its wake and its nine sound clips are made ready
// (hidden) in the first frame of the flight, which in the game is the level's first frame, under the load tail and the letterbox.
// SOUNDS: 2D on their own sources at the world level, ducked by ZodFlyover.DipDuck under a voice (the briefing's own voice AND the
// announcer's start line of the quiet opening), every start written to the sound log (source "L16B"). The stage alone plays the
// swell, the snap and the splash of the staged animal. The clips are the prepared ones (SfxRes + file name).
// It exists on Level 16 only (it attaches itself to that scene) and does nothing while no shot list flies.
// Play checks: return OpenZ.OzL16BriefingStage.Status();   the menu OpenZ/Checks/L16 briefing flyover (Play mode).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>What the level's animal code does for the briefing stage (the animals step joins it to its own views with the small
    /// file OzL16AnimalsForBriefing.cs). The stage moves a lent animal's root itself (position and heading on the water); the level
    /// only stops steering it, shows the take it is asked for, draws its own wake for it, and takes it back.</summary>
    public interface IOzL16BriefingAnimals
    {
        /// <summary>Lend the crocodile nearest to `near` that is not lent yet: from now on the level neither moves nor animates it.
        /// Returns its root (origin at the body's middle, on the water surface, +Z = its nose). Null = none to lend.</summary>
        Transform CrocBorrow(Vector3 near);
        /// <summary>Show `take` ("swim_past", "lunge_bite", "lurk", ...) at `seconds` into it on a lent animal (a looping take wraps, a
        /// one-shot holds its last frame). `speed` = how fast the stage moves it (m/s): above 0.8 the level draws its V wake.</summary>
        void CrocPose(Transform croc, string take, float seconds, float speed);
        /// <summary>The animal goes back to its own place and state under the level's rules, at once (no travel).</summary>
        void CrocReturn(Transform croc);
        /// <summary>CrocPose with a second take mixed in (shareB = its share, 0..1): the chase blends the swim into the lunge and back.</summary>
        void CrocPoseMix(Transform croc, string takeA, float secondsA, string takeB, float secondsB, float shareB, float speed);
        /// <summary>Lend one flying parrot of the level's model that the level does not steer (no shrinking near the lens, shadows as the
        /// level's birds). Root: +Z = beak, +Y = up, body centre at the origin. Null = the stage builds its own.</summary>
        Transform ParrotBorrow();
        /// <summary>Show the wing-beat loop at `flapSeconds` (wraps).</summary>
        void ParrotPose(Transform parrot, float flapSeconds);
        void ParrotReturn(Transform parrot);
    }

    [DefaultExecutionOrder(200)]                                                                      // LateUpdate after the briefing has flown the camera
    public sealed partial class OzL16BriefingStage : MonoBehaviour
    {
        public const string Level = "L16";
        public static OzL16BriefingStage I { get; private set; }
        /// <summary>The level's animal code (null = no animal code in the game yet: the stage's own actors from the prepared models).</summary>
        public static IOzL16BriefingAnimals Animals;

        /// <summary>The stage's own actors (only while the level has no animal code): models and pictures in the ONE folder the level's
        /// animals live in, the wake's mesh file, its material (made by the animals step's menu; without it the stage builds the same
        /// material from the wake picture), and the folder of the sound clips. Statics: changeable in Play, no rebuild.</summary>
        public static string CrocRes = "OpenZ/L16Animals/Croc", CrocBodyTex = "OpenZ/L16Animals/croc_basecolor", CrocEyeTex = "OpenZ/L16Animals/croc_eye",
            ParrotRes = "OpenZ/L16Animals/Parrot_perched", ParrotTex = "OpenZ/L16Animals/parrot_perched_tex",
            WakeFxRes = "OpenZ/L16Animals/Croc_SurfaceFx", WakeMatRes = "OpenZ/L16Animals/CrocFx_Wake", WakeTexRes = "OpenZ/L16Animals/crocfx_wake_strip",
            SfxRes = "OpenZ/Audio/Sfx/L16/";
        /// <summary>Parrots: size (the prepared model is 2.36 m across at 1), wing beats per second of the 14-frame loop, the little rise and
        /// fall of each bird (m) and its roll with it (deg).</summary>
        public static float ParrotScale = 0.6f, FlapRate = 2.6f, BirdBob = 0.12f, BirdBank = 9f;
        /// <summary>Crocodile: heading offset of the model (deg; 0 = its nose is +Z), height of its origin over the water (m), the speed the
        /// swim take was made for (m/s), the second of the lunge take at which the jaws shut (frame 14 of 30; LungeSpeed in the cue
        /// numbers below slows the take so that the jaws begin to open as the word is said).</summary>
        public static float CrocYawOffset = 0f, CrocLift = 0f, SwimTakeSpeed = 1.5f, LungeSnapAt = 0.467f;
        /// <summary>The take of the near animal while it waits for its cut (the pool shot before it).</summary>
        public static string WaitTake = "lurk";
        /// <summary>The stage's own wake, drawn by the level's own wake rule: the V mesh's point this far ahead of the animal's middle (m),
        /// its height over the water (m), its length 1.2 m + 1.1 m per m/s up to 9 m, from 0.8 m/s on, in and out over 0.6 s.</summary>
        public static float WakeApexAhead = 1.35f, WakeLift = 0.03f, WakeBase = 1.2f, WakePerMps = 1.1f, WakeMax = 9f, WakeFrom = 0.8f, WakeFade = 0.6f;
        public static Color WakeTint = new Color(0.86f, 0.9f, 0.8f, 0.5f);
        /// <summary>Sound levels (linear, before the world bus and the duck under the voices). The calls are set so that the announcer's
        /// start line stays 9 dB or more over them (loudest 400 ms of each clip times its gain, ducked).</summary>
        public static float GainFrog = 0.225f, GainFlutter = 0.85f, GainSquawk = 0.275f, GainWake = 0.70f, GainSurface = 0.40f, GainSnap = 0.95f, GainSplash = 0.55f;
        /// <summary>The jungle's own sound through the whole flight (the level's day and swamp loops on the stage's own sources, the swamp
        /// loop full at the water shots), its calls (birds, insects, far monkeys, frogs), and the chase (the robot's strokes, its alarm,
        /// the churned water, the crocodile's growl and roar). All ducked under a voice like the rest, never gone.</summary>
        public static float GainBedDay = 0.24f, GainBedSwamp = 0.18f, GainBird = 0.275f, GainInsect = 0.20f, GainMonkey = 0.19f,
            GainStroke = 0.55f, GainAlarm = 0.40f, GainChurn = 0.40f, GainRoar = 0.22f;                       // the roar at the crocodile's own ratio (0.35 of 0.95)
        public static string BedDayFile = "OpenZ/Audio/Ambience/sd_amb_jungle_day_loop", BedSwampFile = "OpenZ/Audio/Ambience/sd_amb_jungle_swamp_loop";
        /// <summary>False = the briefing's own voice clip already carries the snap and the splash: the stage leaves them out.</summary>
        public static bool SnapSound = true;
        /// <summary>False = silent stage (a check that wants pictures only).</summary>
        public static bool Sounds = true;

        // ------------------------------------------------------------------------------------------------ cue tables
        public sealed class BirdDef
        {
            public Vector3 off; public float dt, phase;
            public BirdDef(float x, float y, float z, float dt, float phase) { off = new Vector3(x, y, z); this.dt = dt; this.phase = phase; }
        }

        /// <summary>A flock: every bird flies one straight line in the world. The line is laid out in the camera's frame of the second
        /// `crossAt` of its shot (x right, y up, z ahead, m): `p0` at crossAt - lead, `p1` at crossAt + tail. `ahead` above 0 = a flock
        /// that comes straight at the lens: every bird is already in the air when its shot begins (crossAt at most lead - ahead).</summary>
        public sealed class Flock
        {
            public string shot; public float crossAt, lead, tail, ahead, flutterAt; public Vector3 p0, p1; public float[] squawks; public BirdDef[] birds;
            // built when its shot comes on screen
            internal bool built; internal float cross; internal Vector3[] a, b; internal float[] t0, t1;
        }

        // <numbers: written from the level's shot sheet, do not edit by hand>
        /// <summary>No bird appears or vanishes inside the picture: a line runs on until it starts and ends this far outside the view (m, one
        /// wing span), or this long before the cut into its shot / after its end (s); the step in which it runs on (s).</summary>
        public static float EdgeMargin = 1.42f, EdgeEarly = 0.1f, EdgeStep = 0.05f;
        static readonly Flock[] Flocks =
        {
            // the sunrise shot: a long pass from right to left that crosses the sun disc (the disc stands 7.5 deg right of the middle and
            // 10 deg over it: the line passes through that sight line 18 m from the lens at the crossing second)
            new Flock { shot = "dawn_swamp", crossAt = 3.4f, lead = 2.4f, tail = 2.2f, ahead = 0f, p0 = new Vector3(15f, 2.6f, 26f), p1 = new Vector3(-11f, 3.4f, 7f), flutterAt = -1.6f,
                squawks = new[] { -1.8f, -0.6f, 0.5f, 1.6f },
                birds = new[]
                {   // offset right, up, ahead (m), time shift (s), wing phase
                    new BirdDef(0f, 0f, 0f, 0f, 0f),
                    new BirdDef(0.8547f, -0.1277f, -0.0583f, -0.2436f, 0.37f),
                    new BirdDef(-0.048f, 0.7069f, 1.4032f, 0.115f, 0.74f),
                    new BirdDef(-1.4868f, 0.6302f, 1.4286f, -0.1675f, 0.11f),
                    new BirdDef(0.5029f, -0.7672f, -1.5331f, -0.0918f, 0.48f),
                    new BirdDef(-1.2999f, 0.4144f, 1.2731f, -0.0777f, 0.85f),
                    new BirdDef(-0.6886f, -0.6213f, -1.6642f, 0.1064f, 0.22f),
                } },
            new Flock { shot = "rim_fall_parrots", crossAt = 3.1f, lead = 3.4f, tail = 0.8f, ahead = 0.3f, p0 = new Vector3(-2f, 5f, 39.5f), p1 = new Vector3(5.2f, 0.1f, -6f), flutterAt = -1.6f,
                squawks = new[] { -2.2f, -1f, -0.2f, 0.3f },
                birds = new[]
                {   // offset right, up, ahead (m), time shift (s), wing phase
                    new BirdDef(0f, 0f, 0f, 0f, 0f),
                    new BirdDef(-0.8704f, -0.5021f, 0.7449f, -0.1664f, 0.37f),
                    new BirdDef(-0.4302f, 0.2125f, -0.3523f, -0.2488f, 0.74f),
                    new BirdDef(1.5361f, 0.6929f, -0.7984f, 0.0448f, 0.11f),
                    new BirdDef(1.9137f, 0.6212f, -1.7397f, -0.1026f, 0.48f),
                    new BirdDef(-0.8483f, 0.5804f, 0.5047f, -0.1948f, 0.85f),
                } },
            // the last shot: a flock crosses over our fort, under the rising camera, from right to left
            new Flock { shot = "our_base_home", crossAt = 1.5f, lead = 1.2f, tail = 1.2f, ahead = 0f, p0 = new Vector3(9f, -2f, 17f), p1 = new Vector3(-9f, -1f, 10f), flutterAt = -0.9f,
                squawks = new[] { -0.8f, 0f, 0.7f },
                birds = new[]
                {
                    new BirdDef(0f, 0f, 0f, 0f, 0f),
                    new BirdDef(1.1f, 0.4f, -0.8f, -0.2f, 0.37f),
                    new BirdDef(-0.9f, -0.5f, 1.2f, 0.12f, 0.74f),
                    new BirdDef(1.9f, -0.3f, 1.5f, -0.1f, 0.11f),
                    new BirdDef(-1.7f, 0.6f, -1.3f, 0.08f, 0.48f),
                    new BirdDef(0.4f, 0.8f, 1.9f, -0.16f, 0.85f),
                } },
        };
        public static string SwimShot = "pool_chase_high", SwimTake = "swim_past", SnapShot = "";
        /// <summary>The swimmer: path (x, z), seconds it has already swum at the cut, seconds it still has to go at the next cut; the level's own
        /// animal nearest to SwimBorrow is the one that swims.</summary>
        public static Vector2 SwimFrom = new Vector2(58.3f, -44.8f), SwimTo = new Vector2(56.6f, -33.4f), SwimBorrow = new Vector2(57.25f, -37.75f);
        public static float SwimStartAhead = 0.5f, SwimEndAfter = 0.35f;
        /// <summary>The near animal of the cut: place (x, z), heading (deg), the second of its shot at which the jaws shut; the level's own animal
        /// nearest to SnapBorrow is the one.</summary>
        public static Vector2 SnapAt = new Vector2(56.3f, -49.8f), SnapBorrow = new Vector2(56.25f, -46.75f);
        public static float SnapHeading = 187f, SnapSecond = 1.32f, LungeSpeed = 0.6f, PoolWaterY = -1.25f;
        /// <summary>THE CHASE of the two crocodile shots (one clock: seconds since the cut into the pool shot; the cut shot goes on from
        /// the pool shot's end). A robot of the OTHER team flees through the pool along a straight line at RobotSpeed, waist-deep
        /// (the game's own wading depth), leaning forward, arms pumping (its own run take at RobotBeat times its speed); every
        /// StrokeEvery seconds an arm comes down: a splash burst at that hand, a ring round the robot, the churned water behind it.
        /// The crocodile swims the same line behind it, its middle GapStart closing to GapEnd metres behind the robot (the jaws 1.7 m
        /// nearer), with its V wake and rings from its head; it lunges with its own lunge take, mixed in over LungeBlend seconds, so
        /// that the jaws are wide before and clack shut ON: SnapInPool seconds into the pool shot, and SnapsInCut seconds into the cut.</summary>
        public static Vector2 ChaseFrom = new Vector2(57f, -33.5f), ChaseTo = new Vector2(57.4f, -52.5f);
        public static float RobotSpeed = 2.15f, GapStart = 5.6f, GapEnd = 4.3f, RobotSink = 1.0f, RobotLean = 20f, RobotBeat = 1.5f, StrokeEvery = 0.27f,
            SnapInPool = 3.7f, LungeAfter = 0.33f, LungeBlend = 0.12f;
        public static float[] SnapsInCut = { 0.45f, 1.30f };
        /// <summary>Seconds between two rings of the swimming crocodile (one from its head, one behind its tail).</summary>
        public static float CrocRingEvery = 0.9f;
        /// <summary>The fleeing robot: the model's own facing against its heading (deg: it must face the way it flees), how far a stroke
        /// dips it (m), how far it rolls to the stroke's side and swings forward and back (deg), and its one look back over the shoulder:
        /// the second of the chase, how long, how far the head turns (deg).</summary>
        public static float RobotYawOffset = 180f, RobotBob = 0.17f, RobotRoll = 13f, RobotPitchSwing = 7f, RobotLookBackAt = 1.7f, RobotLookBackFor = 1.1f, RobotLookBackDeg = 70f;
        /// <summary>For the flight's camera: the chase clock now (s, under 0 = no chase) and where the jaws are.</summary>
        public static float ChaseTau = -1f; public static Vector3 JawsAt;
        /// <summary>The second of the chase clock at which the jaws clack shut for the j-th time, for a pool shot of that length.</summary>
        public static float ChaseSnapSecond(int j, float pool) => j == 0 ? Mathf.Min(SnapInPool, pool - 0.45f) : pool + SnapsInCut[Mathf.Min(j - 1, SnapsInCut.Length - 1)];
        public static int SnapCount => 1 + SnapsInCut.Length;
        /// <summary>Where the pool's second crocodile lies lurking during the chase (x, z) and its heading (deg): 4 m west of the line.</summary>
        public static Vector2 LurkAt = new Vector2(53.2f, -43.5f); public static float LurkHeading = 100f;
        public static string RobotPrefab = "Z_bs01_grunt", RobotTake = "run_forward_legfix";
        // </numbers>

        /// <summary>One frog call early in these shots (shot name, second).</summary>
        static readonly (string shot, float at, int kind, string file)[] Calls =
        {   // kind: 0 a frog (the water shots), 1 a bird call, 2 an insect swell, 3 far monkeys
            ("dawn_swamp", 0.5f, 0, "sd_frog_croak_a"), ("dawn_swamp", 1.3f, 1, "sd_jungle_bird_a"), ("dawn_swamp", 3.0f, 2, "sd_insect_swell_a"),
            ("dawn_swamp", 4.7f, 3, "sd_monkey_far_a"), ("dawn_swamp", 6.1f, 1, "sd_jungle_bird_c"),
            ("rim_fall_parrots", 0.6f, 0, "sd_frog_croak_a"), ("rim_fall_parrots", 2.0f, 1, "sd_jungle_bird_b"), ("rim_fall_parrots", 3.4f, 0, "sd_frog_croak_a"),
            ("rim_fall_parrots", 4.4f, 3, "sd_monkey_far_b"),
            ("pool_chase_high", 0.4f, 0, "sd_frog_croak_a"), ("pool_chase_high", 2.2f, 2, "sd_insect_swell_b"),
            ("swamp_filler_high", 0.25f, 0, "sd_frog_croak_a"), ("swamp_filler_high", 1.1f, 2, "sd_insect_swell_a"),
            ("enemy_fort", 0.3f, 1, "sd_jungle_bird_d"), ("enemy_fort", 1.5f, 3, "sd_monkey_far_c"), ("enemy_fort", 2.5f, 2, "sd_insect_swell_a"),
            ("our_base_home", 0.4f, 1, "sd_jungle_bird_e"), ("our_base_home", 1.7f, 3, "sd_monkey_far_a"),
        };
        static readonly string[] CallIds = { "brief_l16_frog", "brief_l16_bird_call", "brief_l16_insect_swell", "brief_l16_monkey_far" };
        static float CallGain(int kind) => kind == 0 ? GainFrog : kind == 1 ? GainBird : kind == 2 ? GainInsect : GainMonkey;
        static readonly string[] SnapFiles = { "sd_croc_jaw_snap_b", "sd_croc_jaw_snap_a", "sd_croc_jaw_snap_c" };
        static readonly string[] SquawkFiles = { "sd_parrot_squawk_a", "sd_parrot_squawk_b", "sd_parrot_squawk_d" };
        /// <summary>Every clip the stage can play (made ready in the first frame of the flight).</summary>
        static readonly string[] AllFiles = { "sd_frog_croak_a", "sd_parrot_flutter_a", "sd_parrot_squawk_a", "sd_parrot_squawk_b", "sd_parrot_squawk_d",
            "sd_croc_wake_loop_b", "sd_croc_surface_swell_a", "sd_croc_jaw_snap_b", "sd_croc_lunge_splash_c",
            "sd_croc_jaw_snap_a", "sd_croc_jaw_snap_c", "sd_croc_lunge_roar_a", "sd_croc_warning_a", "sd_croc_thrash_loop_a", "sd_robot_legs_kick_a", "sd_robot_croc_alarm_a",
            "sd_jungle_bird_a", "sd_jungle_bird_b", "sd_jungle_bird_c", "sd_jungle_bird_d", "sd_jungle_bird_e", "sd_insect_swell_a", "sd_insect_swell_b",
            "sd_monkey_far_a", "sd_monkey_far_b", "sd_monkey_far_c" };

        // ------------------------------------------------------------------------------------------------ what the Play check reads
        /// <summary>The parrots that are drawn in this frame (one object per bird of the flock for the whole shot).</summary>
        public static readonly List<Transform> BirdsNow = new List<Transform>();
        /// <summary>The swimmer and the near animal of the cut while the stage holds them (null otherwise), the swimmer's speed (m/s), whether
        /// its wake is drawn (the stage's own, or asked of the level through CrocPose), whether the jaws have shut in this run.</summary>
        public static Transform Swimmer, Snapper; public static float SwimmerSpeed; public static bool WakeOn, Snapped;
        /// <summary>The fleeing robot while it is drawn (null otherwise), its arm strokes and the crocodile's jaw snaps in this flight.</summary>
        public static Transform RobotNow; public static int RobotStrokes, Snaps;
        /// <summary>Counters of a session: flights staged, animals lent by the level / built by the stage / given back, sounds started.</summary>
        public static int Flights, Lent, Built, GivenBack, SoundsStarted;
        /// <summary>The last thing that was left out and why ("" = nothing).</summary>
        public static string LastWarning = "";

        // ------------------------------------------------------------------------------------------------ boot
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            I = null; Animals = null; BirdsNow.Clear(); Swimmer = Snapper = null; SwimmerSpeed = 0f; WakeOn = Snapped = false; RobotNow = null; RobotStrokes = Snaps = 0;
            Flights = Lent = Built = GivenBack = SoundsStarted = 0; LastWarning = ""; levelCrocType = null; levelCrocAsked = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnScene; SceneManager.sceneLoaded += OnScene; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AfterFirstScene() { TryAttach(SceneManager.GetActiveScene()); }

        static void OnScene(Scene s, LoadSceneMode m) { if (m == LoadSceneMode.Single) TryAttach(s); }

        static void TryAttach(Scene s)
        {
            if (!Application.isPlaying || I) return;
            var game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            if (!game || game.level != Level) return;
            var go = new GameObject("OzL16BriefingStage");
            if (s.IsValid() && go.scene != s) SceneManager.MoveGameObjectToScene(go, s);
            go.AddComponent<OzL16BriefingStage>();
        }

        static Type levelCrocType; static bool levelCrocAsked;
        /// <summary>The level's own crocodile view when the animals step is in the game, null otherwise. Found by its name, so this file
        /// compiles with and without that step. While it exists the stage never builds a crocodile of its own.</summary>
        public static Component LevelCrocodiles()
        {
            if (!levelCrocAsked) { levelCrocAsked = true; levelCrocType = typeof(OzL16BriefingStage).Assembly.GetType("OpenZ.CrocodileView"); }
            return levelCrocType != null ? FindFirstObjectByType(levelCrocType) as Component : null;
        }

        // ------------------------------------------------------------------------------------------------ state
        sealed class Poser
        {   // shows one clip of a model at a time, at a time the stage sets (no clock of its own)
            readonly List<AnimationClip> clips = new List<AnimationClip>();
            PlayableGraph graph; AnimationMixerPlayable mix; readonly List<AnimationClipPlayable> plays = new List<AnimationClipPlayable>();
            Animation legacy; readonly List<AnimationState> states = new List<AnimationState>();
            bool graphOk; int on = -1;
            public int Count => clips.Count;

            public Poser(GameObject model, AnimationClip[] all, string name)
            {
                if (all != null) foreach (var c in all) if (c && !c.name.StartsWith("__preview__")) clips.Add(c);
                if (clips.Count == 0) return;
                if (clips[0].legacy)
                {
                    legacy = model.GetComponent<Animation>(); if (!legacy) legacy = model.AddComponent<Animation>();
                    legacy.playAutomatically = false; legacy.cullingType = AnimationCullingType.AlwaysAnimate;
                    for (int i = 0; i < clips.Count; i++)
                    {
                        string key = "S" + i; legacy.AddClip(clips[i], key);
                        var st = legacy[key]; st.enabled = true; st.speed = 0f; st.weight = 0f; states.Add(st);
                    }
                    return;
                }
                var an = model.GetComponent<Animator>(); if (!an) an = model.AddComponent<Animator>();
                an.applyRootMotion = false; an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                graph = PlayableGraph.Create("OpenZ." + name);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "Stage", an);
                mix = AnimationMixerPlayable.Create(graph, clips.Count);
                for (int i = 0; i < clips.Count; i++)
                {
                    var p = AnimationClipPlayable.Create(graph, clips[i]);
                    p.SetApplyFootIK(false); p.SetSpeed(0.0);
                    graph.Connect(p, 0, mix, i); mix.SetInputWeight(i, 0f); plays.Add(p);
                }
                output.SetSourcePlayable(mix);
                graph.Play();
                graphOk = true;
            }

            /// <summary>The clip whose name is `take` or ends with it ("Armature|swim_past"); -1 = none.</summary>
            public int Find(string take)
            {
                for (int i = 0; i < clips.Count; i++) if (string.Equals(clips[i].name, take, StringComparison.OrdinalIgnoreCase)) return i;
                for (int i = 0; i < clips.Count; i++) if (clips[i].name.EndsWith(take, StringComparison.OrdinalIgnoreCase)) return i;
                return -1;
            }

            public int Longest() { int b = -1; for (int i = 0; i < clips.Count; i++) if (b < 0 || clips[i].length > clips[b].length) b = i; return b; }
            public float Length(int i) => i >= 0 && i < clips.Count ? clips[i].length : 0f;

            public void Pose(int i, float seconds)
            {
                if (i < 0 || i >= clips.Count) return;
                if (graphOk)
                {
                    if (on != i) { for (int k = 0; k < plays.Count; k++) mix.SetInputWeight(k, k == i ? 1f : 0f); on = i; }
                    plays[i].SetTime(seconds); graph.Evaluate(0f);
                }
                else if (legacy)
                {
                    if (on != i) { for (int k = 0; k < states.Count; k++) states[k].weight = k == i ? 1f : 0f; on = i; }
                    states[i].time = seconds; legacy.Sample();
                }
            }

            public void Dispose() { if (graphOk && graph.IsValid()) graph.Destroy(); graphOk = false; }
        }

        sealed class Croc { public Transform t; public bool own, shown = true; public Poser poser; public Renderer[] rends; }
        sealed class Bird { public Transform t; public bool own, shown = true; public Poser poser; public int clip = -1; public float loopA, loopLen; public Renderer[] rends; }

        ZGame game; Camera cam;
        bool staged, crocsOn; string shotNow; float prevInto = -1f;
        Croc swimmer, snapper; bool swimMissing, snapMissing, parrotMissing;
        readonly List<Bird> birds = new List<Bird>();
        Material bodyMat, eyeMat, parrotMat;                                                          // the stage's own actors only
        GameObject wakeGo; Renderer wakeRend; Material wakeMat; bool wakeTried; float wakeK;
        readonly List<AudioSource> voices = new List<AudioSource>(); readonly List<float> voiceGain = new List<float>();
        AudioSource wakeLoop; float wakeLoopK; AudioSource flutter, splash; float splashAt;
        // the chase and the jungle's own sound
        Transform robotT, robotHandL, robotHandR, robotHead; Poser robotPoser; Renderer[] robotRends; bool robotShown, robotTried;
        float prevChase = -1f, poolDur = 4.6f, lastCrocHeading, bedSwampK; Vector2 lastCrocAt;
        AudioSource churn, bedDay, bedSwamp; bool churnTried, bedTried;
        readonly Dictionary<string, AudioClip> sfx = new Dictionary<string, AudioClip>();
        readonly HashSet<string> warned = new HashSet<string>();
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color");

        void Awake() { I = this; game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>(); PreloadClips(); }

        /// <summary>Every clip the stage can play. They are loaded when the stage wakes (the level's own loading frame): the first play of a
        /// clip that is not loaded yet costs a frame of 0.1 to 0.4 s in the middle of a shot.</summary>
        static readonly string[] PreloadFiles = { "sd_frog_croak_a", "sd_parrot_flutter_a", "sd_parrot_squawk_a", "sd_parrot_squawk_b", "sd_parrot_squawk_d", "sd_jungle_bird_a", "sd_jungle_bird_b", "sd_jungle_bird_c", "sd_jungle_bird_d", "sd_jungle_bird_e", "sd_insect_swell_a", "sd_insect_swell_b", "sd_monkey_far_a", "sd_monkey_far_b", "sd_monkey_far_c", "sd_croc_surface_swell_a", "sd_croc_warning_a", "sd_robot_croc_alarm_a", "sd_croc_wake_loop_b", "sd_croc_thrash_loop_a", "sd_robot_legs_kick_a", "sd_croc_lunge_roar_a", "sd_croc_jaw_snap_a", "sd_croc_jaw_snap_b", "sd_croc_jaw_snap_c", "sd_croc_lunge_splash_c" };
        public static int Preloaded;

        void PreloadClips()
        {
            int n = 0;
            foreach (var f in PreloadFiles) if (Warm(f)) n++;
            if (Warm(BedDayFile)) n++;
            if (Warm(BedSwampFile)) n++;
            Preloaded = n;
        }

        bool Warm(string file)
        {
            var c = Clip(file); if (!c) return false;
            if (c.loadState == AudioDataLoadState.Unloaded) c.LoadAudioData();
            return true;
        }
        void OnDisable() { Release("the stage was switched off"); }
        void OnDestroy()
        {
            Release("the scene went");
            foreach (var m in new[] { bodyMat, eyeMat, parrotMat }) if (m) Destroy(m);
            if (I == this) I = null;
        }

        void Warn(string key, string text)
        {
            LastWarning = text;
            if (!warned.Add(key)) return;
            Debug.LogWarning("[OzL16BriefingStage] " + text);
        }

        // ------------------------------------------------------------------------------------------------ frame
        void LateUpdate()
        {
            var fly = ZodFlyover.Live;
            string shot = null; float into = 0f, dur = 0f;
            bool on = fly != null && fly.Active && fly.Cine && fly.CineShown(out shot, out into, out dur) && game && game.level == Level;
            if (!on) { if (staged) Release("the flight handed the camera back"); return; }
            if (!cam) cam = Camera.main;
            if (!staged) { staged = true; Flights++; Snapped = false; Snaps = 0; RobotStrokes = 0; prevChase = -1f; shotNow = null; foreach (var f in Flocks) f.built = false; Prepare(); }
            if (shot != shotNow) { shotNow = shot; prevInto = -1f; flutter = null; }
            // a voice is speaking: the briefing's own voice, or the announcer's start line of the quiet opening (it plays on another source)
            bool voice = (fly.VoiceOn != null && fly.VoiceOn()) || ZodBriefing.StartLinePlaying;
            float duck = Sounds && voice ? ZodFlyover.DipDuck : 1f;

            TickFlocks(fly, shot, into, dur, duck);
            TickSunBirds(shot, into, dur, duck);
            TickCrocs(shot, into, dur, duck);
            foreach (var c in Calls) if (c.shot == shot && Crossed(c.at, into)) Play(CallIds[c.kind], c.file, CallGain(c.kind), duck, shot);
            TickBed(shot, duck);
            if (splash)
            {   // the splash is full for 0.2 s, then down to 15 % within 0.15 s: the next line starts 0.45 s after the snap
                int si = voices.IndexOf(splash); float age = Time.unscaledTime - splashAt;
                if (si >= 0 && splash.isPlaying) voiceGain[si] = GainSplash * Mathf.Lerp(1f, 0.15f, Mathf.Clamp01((age - 0.2f) / 0.15f));
                if (!splash.isPlaying && age > 0.1f) splash = null;
            }
            for (int i = 0; i < voices.Count; i++) if (voices[i] && voices[i].isPlaying) voices[i].volume = voiceGain[i] * Bus() * duck;
            prevInto = into;
        }

        /// <summary>A cue second was passed in this frame. On the first frame of a shot (prevInto below 0) a cue that lies up to a quarter
        /// second behind still starts, so a cue at the very start of a shot is never lost on a slow frame; inside a shot a jump of half a
        /// second or more (a held frame, a corrected clock) starts nothing.</summary>
        bool Crossed(float at, float into) => into >= at && prevInto < at && (prevInto < 0f ? into - at < 0.25f : into - prevInto < 0.5f);

        /// <summary>Everything a flight will show or play is made ready in its first frame, hidden: the parrots, the stage's own
        /// crocodiles and wake (only while the level has no animal code), the sound clips. In the game that frame is the level's first
        /// frame; nothing is built in the frame it first shows.</summary>
        void Prepare()
        {
            int need = 0; foreach (var f in Flocks) need = Mathf.Max(need, f.birds.Length);
            while (birds.Count < need && !parrotMissing)
            {
                var b = NewBird(birds.Count);
                if (b == null) { parrotMissing = true; break; }
                birds.Add(b); Show(b.rends, ref b.shown, false);
            }
            var level = LevelCrocodiles();
            if (Animals == null && !level)
            {   // no animal code in the game: the stage's own two crocodiles and its wake
                if (swimmer == null && !swimMissing) swimmer = BuildCroc("BriefingCrocSwimmer", ref swimMissing);
                if (snapper == null && !snapMissing) snapper = BuildCroc("BriefingCrocNear", ref snapMissing);
                if (swimmer != null) Show(swimmer.rends, ref swimmer.shown, false);
                if (snapper != null) Show(snapper.rends, ref snapper.shown, false);
                if (swimmer != null && !wakeTried) BuildWake();
            }
            else if (Animals == null)
                Warn("croc", "no staged crocodile: the level draws its own crocodiles but nothing lends them to the stage (OzL16BriefingStage.Animals is not set: "
                           + "the animals step's OzL16AnimalsForBriefing.cs is not in the project; run that step's apply script again). The stage builds none of its own beside them.");
            if (Sounds) { foreach (var file in AllFiles) Clip(file); Clip(BedDayFile); Clip(BedSwampFile); }
            BuildRobot();
        }

        /// <summary>The jungle's own sound through the whole flight: the day loop, and the swamp loop full at the water shots.</summary>
        void TickBed(string shot, float duck)
        {
            if (!Sounds) return;
            if (!bedTried)
            {
                bedTried = true;
                bedDay = Play("brief_l16_bed_day", BedDayFile, GainBedDay, duck, shot, true);
                bedSwamp = Play("brief_l16_bed_swamp", BedSwampFile, GainBedSwamp * 0.4f, duck, shot, true);
                bedSwampK = 0.4f;
            }
            bool water = shot == "dawn_swamp" || shot == "rim_fall_parrots" || shot == SwimShot || shot == SnapShot;
            bedSwampK = Mathf.MoveTowards(bedSwampK, water ? 1f : 0.4f, Time.unscaledDeltaTime / 0.6f);
            int i = bedSwamp ? voices.IndexOf(bedSwamp) : -1; if (i >= 0) voiceGain[i] = GainBedSwamp * bedSwampK;
            i = bedDay ? voices.IndexOf(bedDay) : -1; if (i >= 0) voiceGain[i] = GainBedDay;
        }

        /// <summary>The fleeing robot: the game's own grunt in the other team's colours, drawn only (no unit of the match: the rules never
        /// know it), with its own run take; made ready, hidden, in the first frame of the flight. It carries no weapon: it swims for
        /// its life. Left out with one warning when the model or the take is missing.</summary>
        void BuildRobot()
        {
            if (robotTried) return;
            robotTried = true;
            var prefab = game ? game.Prefab(RobotPrefab) : null;
            if (!prefab) { Warn("robot", $"no fleeing robot in the crocodile shots: the game has no unit model {RobotPrefab}"); return; }
            var root = new GameObject("BriefingFleeingRobot").transform; root.SetParent(transform, false);
            var go = Instantiate(prefab, root, false); go.name = "Model";
            var tp = go.GetComponent<TeamPaint>(); if (tp) tp.Team = game.player == Team.Red ? Team.Blue : Team.Red;
            var an = go.GetComponentInChildren<Animator>(true);
            AnimationClip take = null;
            if (an && an.runtimeAnimatorController) foreach (var c in an.runtimeAnimatorController.animationClips) if (c && c.name == RobotTake) take = c;
            if (!an || !take)
            {
                Warn("robot", $"no fleeing robot in the crocodile shots: the model {RobotPrefab} has no take {RobotTake}");
                Destroy(root.gameObject); return;
            }
            an.runtimeAnimatorController = null;                                                       // the stage sets its pose itself
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            var keep = new List<Renderer>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.Contains("Weapon")) { r.enabled = false; continue; }
                r.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;                       // as every living unit: no smear from the motion blur
                keep.Add(r);
            }
            robotRends = keep.ToArray();
            robotPoser = new Poser(an.gameObject, new[] { take }, "BriefingFleeingRobot");
            robotHandL = an.GetBoneTransform(HumanBodyBones.LeftHand); robotHandR = an.GetBoneTransform(HumanBodyBones.RightHand); robotHead = an.GetBoneTransform(HumanBodyBones.Head);
            robotT = root; robotShown = true; Show(robotRends, ref robotShown, false);
            Built++;
        }

        // ------------------------------------------------------------------------------------------------ parrots
        void TickFlocks(ZodFlyover fly, string shot, float into, float dur, float duck)
        {
            BirdsNow.Clear();
            Flock fl = null;
            foreach (var f in Flocks) if (f.shot == shot) fl = f;
            int n = 0;
            if (fl != null)
            {
                if (!fl.built) Build(fly, fl, dur);
                if (fl.built)
                {
                    n = Mathf.Min(fl.birds.Length, birds.Count);                                       // bird i of the flock is object i for the whole shot
                    Vector3 centre = Vector3.zero; int shownCount = 0;
                    for (int i = 0; i < n; i++)
                    {
                        var bird = birds[i];
                        float k = (into - fl.t0[i]) / Mathf.Max(0.05f, fl.t1[i] - fl.t0[i]);
                        if (k < 0f || k > 1f) { Show(bird.rends, ref bird.shown, false); continue; }
                        var d = fl.birds[i];
                        float w = (into * 1.9f + d.phase) * Mathf.PI * 2f;
                        Vector3 pos = Vector3.Lerp(fl.a[i], fl.b[i], k) + Vector3.up * (Mathf.Sin(w) * BirdBob);
                        Vector3 dir = fl.b[i] - fl.a[i];
                        var rot = Quaternion.LookRotation(dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward, Vector3.up) * Quaternion.Euler(0f, 0f, Mathf.Cos(w) * BirdBank);
                        bird.t.SetPositionAndRotation(pos, rot);
                        Show(bird.rends, ref bird.shown, true);
                        float flap = into * FlapRate + d.phase;
                        if (bird.own) { if (bird.poser != null && bird.clip >= 0) bird.poser.Pose(bird.clip, bird.loopA + Mathf.Repeat(flap, 1f) * bird.loopLen); }
                        else Animals?.ParrotPose(bird.t, flap * (14f / 24f));
                        BirdsNow.Add(bird.t); centre += pos; shownCount++;
                    }
                    float pan = 0f;
                    if (shownCount > 0 && cam) { var v = cam.WorldToViewportPoint(centre / shownCount); pan = Mathf.Clamp((v.x - 0.5f) * 1.6f, -0.8f, 0.8f); }
                    if (Crossed(fl.cross + fl.flutterAt, into)) flutter = Play("brief_l16_parrot_flutter", "sd_parrot_flutter_a", GainFlutter, duck, shot);
                    if (flutter && flutter.isPlaying) flutter.panStereo = pan;
                    for (int q = 0; q < fl.squawks.Length; q++)
                        if (Crossed(fl.cross + fl.squawks[q], into))
                        {
                            var s = Play("brief_l16_parrot_squawk", SquawkFiles[q % SquawkFiles.Length], GainSquawk, duck, shot);
                            if (s) s.panStereo = pan;
                        }
                }
            }
            for (int i = n; i < birds.Count; i++) Show(birds[i].rends, ref birds[i].shown, false);
        }

        /// <summary>Lays the flock's lines out in the camera's frame of its crossing second (clamped into the fitted shot), then lets
        /// every line run on, at the bird's own speed, until it starts and ends outside the picture: no bird appears or vanishes inside
        /// the frame. The speed and the crossing second stay as laid out.</summary>
        void Build(ZodFlyover fly, Flock fl, float dur)
        {
            // a shot shorter than the pass squeezes its times (the birds fly that much faster): the pass is always whole
            float k = Mathf.Min(1f, dur / (fl.lead + fl.tail + 0.3f)), lead = fl.lead * k, tail = fl.tail * k;
            float cross = Mathf.Min(fl.crossAt, dur - tail - 0.1f);
            // head-on: it cannot start outside the picture, so every bird is in the air before the cut (the time shifts are 0.25 s at most);
            // crossing: the pass is laid out inside the shot and its lines run on to the edge of the picture below
            fl.cross = fl.ahead > 0f ? Mathf.Min(cross, lead - fl.ahead) : Mathf.Max(cross, lead + 0.05f);
            if (!fly.CinePoseAt(fl.shot, fl.cross, out var pos, out var rot, out _)) return;
            float aspect = cam ? cam.aspect : 16f / 9f;
            int n = fl.birds.Length;
            fl.a = new Vector3[n]; fl.b = new Vector3[n]; fl.t0 = new float[n]; fl.t1 = new float[n];
            for (int i = 0; i < n; i++)
            {
                var d = fl.birds[i];
                Vector3 a = pos + rot * (fl.p0 + d.off), b = pos + rot * (fl.p1 + Vector3.Scale(d.off, new Vector3(1f, 1f, 0.6f)));
                float t0 = fl.cross - lead + d.dt, t1 = fl.cross + tail + d.dt;
                Vector3 vel = (b - a) / Mathf.Max(0.05f, t1 - t0);
                // backwards: until the start lies outside the picture of the camera of that second, or before the cut into the shot
                // (the bird is then already flying on the shot's first frame)
                for (int s = 0; s < 400 && t0 > -EdgeEarly && !Outside(fly, fl.shot, a, t0, aspect, EdgeMargin); s++) { a -= vel * EdgeStep; t0 -= EdgeStep; }
                // forwards: until the end lies outside the picture (or behind the lens), or past the end of the shot
                for (int s = 0; s < 400 && t1 < dur + EdgeEarly && !Outside(fly, fl.shot, b, t1, aspect, EdgeMargin); s++) { b += vel * EdgeStep; t1 += EdgeStep; }
                fl.a[i] = a; fl.b[i] = b; fl.t0[i] = t0; fl.t1[i] = t1;
            }
            fl.built = true;
        }

        /// <summary>True when the point lies `margin` metres or more outside the picture of the camera of second `t` of the shot (measured
        /// from the nearest side of the view), or behind the lens.</summary>
        static bool Outside(ZodFlyover fly, string shot, Vector3 p, float t, float aspect, float margin)
        {
            if (!fly.CinePoseAt(shot, t, out var cp, out var cr, out float fov)) return true;
            Vector3 l = Quaternion.Inverse(cr) * (p - cp);
            if (l.z < -margin * 0.5f) return true;
            float tv = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad), th = tv * aspect;
            float dx = (Mathf.Abs(l.x) - l.z * th) / Mathf.Sqrt(1f + th * th), dy = (Mathf.Abs(l.y) - l.z * tv) / Mathf.Sqrt(1f + tv * tv);
            return Mathf.Max(dx, dy) >= margin;
        }

        Bird NewBird(int i)
        {
            var lent = Animals != null ? Animals.ParrotBorrow() : null;
            if (lent) { Lent++; return new Bird { t = lent, own = false, rends = lent.GetComponentsInChildren<Renderer>(true) }; }
            var prefab = Resources.Load<GameObject>(ParrotRes);
            if (!prefab)
            {
                Warn("parrot", $"no parrots at the lens: the level lends none and Resources/{ParrotRes} is missing (the level's parrot model: see the head of OzL16BriefingStage.cs)");
                return null;
            }
            if (!parrotMat) parrotMat = AnimalMat("BriefingParrot (runtime)", ParrotTex, 0.2f);
            if (!parrotMat)
            {
                Warn("parrot", $"no parrots at the lens: the picture Resources/{ParrotTex} is missing (the stage shows no grey bird)");
                return null;
            }
            var root = new GameObject("BriefingParrot" + i).transform; root.SetParent(transform, false);
            var pivot = new GameObject("Pivot").transform; pivot.SetParent(root, false);
            var orient = new GameObject("Orient").transform; orient.SetParent(pivot, false);          // the axis fix lives here: the take keys the model root
            var go = Instantiate(prefab, orient, false); go.name = "Model";
            var b = new Bird { t = root, own = true, rends = Dress(go, parrotMat, null) };
            OrientBird(pivot, orient, go.transform);
            pivot.localScale = Vector3.one * ParrotScale;
            b.poser = new Poser(go, Resources.LoadAll<AnimationClip>(ParrotRes), "BriefingParrot" + i);
            b.clip = b.poser.Longest();
            if (b.clip >= 0)
            {   // the wing-beat loop is frames 1..15 of the take at 24 fps; some imports start the take one frame early
                int frames = Mathf.RoundToInt(b.poser.Length(b.clip) * 24f);
                int shift = frames == 20 || frames == 280 ? 1 : 0;
                b.loopA = shift / 24f; b.loopLen = 14f / 24f;
            }
            else Warn("parrot clip", $"the parrots at the lens do not beat their wings: no animation clip in Resources/{ParrotRes}");
            Built++;
            return b;
        }

        /// <summary>The model turned so that its beak is +Z and its back +Y, body centre on the pivot, from its bones (whatever axes the
        /// import chose): the same rule as the level's flying birds (AmbientLife.Orient).</summary>
        static void OrientBird(Transform pivot, Transform orient, Transform model)
        {
            Transform head = FindDeep(model, "head"), tail = FindDeep(model, "tail_2"), wl = FindDeep(model, "wing_tip_L"), wr = FindDeep(model, "wing_tip_R"), hips = FindDeep(model, "hips");
            if (!head || !tail || !wl || !wr) return;
            Vector3 f = pivot.InverseTransformPoint(head.position) - pivot.InverseTransformPoint(tail.position);
            Vector3 r = pivot.InverseTransformPoint(wr.position) - pivot.InverseTransformPoint(wl.position);
            if (f.sqrMagnitude < 1e-6f || r.sqrMagnitude < 1e-6f) return;
            var fs = Snap(f); var u = Vector3.Cross(f.normalized, r.normalized); u -= fs * Vector3.Dot(u, fs); var us = Snap(u);
            if (Mathf.Abs(Vector3.Dot(fs, us)) > 0.5f) return;
            orient.localRotation = Quaternion.Inverse(Quaternion.LookRotation(fs, us));
            if (hips) orient.localPosition -= pivot.InverseTransformPoint(hips.position);
        }

        static Vector3 Snap(Vector3 v)
        {
            float ax = Mathf.Abs(v.x), ay = Mathf.Abs(v.y), az = Mathf.Abs(v.z);
            if (ax >= ay && ax >= az) return new Vector3(Mathf.Sign(v.x), 0f, 0f);
            if (ay >= az) return new Vector3(0f, Mathf.Sign(v.y), 0f);
            return new Vector3(0f, 0f, Mathf.Sign(v.z));
        }

        static Transform FindDeep(Transform t, string n)
        {
            if (t.name == n) return t;
            for (int i = 0; i < t.childCount; i++) { var r = FindDeep(t.GetChild(i), n); if (r) return r; }
            return null;
        }

        static void Show(Renderer[] rends, ref bool shown, bool on)
        {
            if (shown == on) return;
            shown = on;
            if (rends != null) foreach (var r in rends) if (r) r.enabled = on;
        }

        // ------------------------------------------------------------------------------------------------ the stage's own materials
        /// <summary>The surface of one of the stage's own animals, by the recipe of the level's animals: the game's lit surface with the
        /// animal's own picture, no team paint, no grime, dark at night like the scenery. Null = the picture or the surface is missing
        /// (the animal is then left out: the stage shows no grey stand-in).</summary>
        static Material AnimalMat(string name, string pictureRes, float smooth)
        {
            var sh = DetailKit.Lit; var tex = Resources.Load<Texture2D>(pictureRes);
            if (!sh || !tex) return null;
            var m = new Material(sh) { name = name, hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex); else m.mainTexture = tex;
            DetailKit.F(m, "_UseVertexColor", 0f); DetailKit.F(m, "_TeamMode", 0f); DetailKit.F(m, "_Degreen", 0f); DetailKit.F(m, "_Stylize", 1f); DetailKit.F(m, "_Degrime", 0f); DetailKit.F(m, "_FlatShade", 0f);
            DetailKit.F(m, "_SoftFill", 0.2f); DetailKit.F(m, "_AmbientBoost", 1f); DetailKit.F(m, "_Smoothness", smooth); DetailKit.F(m, "_Metallic", 0f); DetailKit.F(m, "_RimPower", 3.4f); DetailKit.F(m, "_NightClass", 0f);
            DetailKit.C(m, "_KeyWarm", new Color(1.06f, 1f, 0.92f, 1f)); DetailKit.C(m, "_RimColor", new Color(0.5f, 0.46f, 0.38f, 1f));
            DetailKit.C(m, "_BaseColor", Color.white); DetailKit.C(m, "_EmissionColor", Color.black);
            return m;
        }

        /// <summary>Gives every renderer of one of the stage's own models its materials (a second material of the file, the crocodile's
        /// eyes, by its name, else by its place), takes its colliders off and returns the renderers. No cast shadow: the crocodile lies
        /// in the water, the level's flying birds cast none either.</summary>
        static Renderer[] Dress(GameObject model, Material body, Material eye)
        {
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);
            var rends = model.GetComponentsInChildren<Renderer>(true);
            foreach (var rd in rends)
            {
                var old = rd.sharedMaterials; var arr = new Material[Mathf.Max(1, old.Length)];
                for (int i = 0; i < arr.Length; i++)
                {
                    bool named = i < old.Length && old[i] && old[i].name.ToLowerInvariant().Contains("eye");
                    bool second = i == 1 && !(old.Length > 0 && old[0] && old[0].name.ToLowerInvariant().Contains("eye"));
                    arr[i] = eye && (named || second) ? eye : body;
                }
                rd.sharedMaterials = arr; rd.receiveShadows = true; rd.shadowCastingMode = ShadowCastingMode.Off;
                if (rd is SkinnedMeshRenderer sk) sk.updateWhenOffscreen = true;
            }
            return rends;
        }

        // ------------------------------------------------------------------------------------------------ crocodiles
        float WaterY(float x, float z) => OzWaterLevel.At(Level, x, z, PoolWaterY);

        /// <summary>One of the stage's own crocodiles (only while the level has no animal code), with its materials built in code.</summary>
        Croc BuildCroc(string name, ref bool missing)
        {
            var prefab = Resources.Load<GameObject>(CrocRes);
            if (!prefab)
            {
                missing = true;
                Warn("croc", $"no staged crocodile: the level has no animal code yet and Resources/{CrocRes} is missing (see the head of OzL16BriefingStage.cs)");
                return null;
            }
            if (!bodyMat) bodyMat = AnimalMat("BriefingCroc (runtime)", CrocBodyTex, 0.32f);
            if (!eyeMat) eyeMat = AnimalMat("BriefingCrocEye (runtime)", CrocEyeTex, 0.6f);
            if (!bodyMat || !eyeMat)
            {
                missing = true;
                Warn("croc", $"no staged crocodile: its pictures Resources/{CrocBodyTex} and Resources/{CrocEyeTex} are not both there (the stage shows no grey animal)");
                return null;
            }
            var root = new GameObject(name).transform; root.SetParent(transform, false);
            var go = Instantiate(prefab, root, false); go.name = "Model";
            var croc = new Croc { t = root, own = true, rends = Dress(go, bodyMat, eyeMat), poser = new Poser(go, Resources.LoadAll<AnimationClip>(CrocRes), name) };
            if (croc.poser.Count == 0) Warn("croc clips", $"the staged crocodile does not move its body: no animation clips in Resources/{CrocRes}");
            Built++;
            return croc;
        }

        /// <summary>The level's own crocodile nearest to a place, lent for the flight (null = it lends none there).</summary>
        Croc BorrowCroc(Vector2 borrow, string what, ref bool missing)
        {
            if (missing) return null;
            var lent = Animals.CrocBorrow(new Vector3(borrow.x, WaterY(borrow.x, borrow.y), borrow.y));
            if (lent) { Lent++; return new Croc { t = lent, own = false }; }
            missing = true;
            Warn("croc " + what, $"no {what}: the level lent no crocodile near ({borrow.x:F1}, {borrow.y:F1}) (none drawn there, or it is hunting); the stage builds none of its own beside the level's");
            return null;
        }

        /// <summary>Both animals of the pool come on the stage together, on the first frame of the pool shot (a cut): the swimmer on its
        /// path, the near animal waiting at its place for the cut. Lent ones are borrowed here (nothing is built); the stage's own were
        /// made ready, hidden, in the first frame of the flight.</summary>
        void EnterCrocs()
        {
            crocsOn = true;
            if (Animals != null && swimmer == null) swimmer = BorrowCroc(SwimBorrow, "crocodile of the chase", ref swimMissing);
            if (Animals != null && snapper == null) snapper = BorrowCroc(SnapBorrow, "second crocodile of the pool", ref snapMissing);
            if (swimmer != null) Show(swimmer.rends, ref swimmer.shown, true);
            if (snapper != null) Show(snapper.rends, ref snapper.shown, true);
            Swimmer = swimmer?.t; Snapper = swimmer?.t;                                                // ONE animal: it chases through both shots
        }

        void PoseCroc(Croc c, Vector2 at, float heading, string take, float seconds, bool loop, float speed)
        {
            c.t.SetPositionAndRotation(new Vector3(at.x, WaterY(at.x, at.y) + CrocLift, at.y), Quaternion.Euler(0f, heading + CrocYawOffset, 0f));
            if (!c.own) { Animals?.CrocPose(c.t, take, seconds, speed); return; }
            if (c.poser == null) return;
            int i = c.poser.Find(take);
            if (i < 0) { Warn("take " + take, $"the staged crocodile has no take '{take}' in Resources/{CrocRes}"); return; }
            float len = c.poser.Length(i);
            c.poser.Pose(i, loop ? Mathf.Repeat(seconds, Mathf.Max(0.05f, len)) : Mathf.Clamp(seconds, 0f, Mathf.Max(0f, len - 0.001f)));
        }

        /// <summary>The second of the chase clock at which the jaws clack shut for the j-th time.</summary>
        float SnapTime(int j) => j == 0 ? Mathf.Min(SnapInPool, poolDur - 0.45f) : poolDur + SnapsInCut[Mathf.Min(j - 1, SnapsInCut.Length - 1)];
        bool ChaseCrossed(float at, float tau) => tau >= at && prevChase < at && (prevChase < 0f ? tau - at < 0.9f : tau - prevChase < 0.5f);   // the first frame after the cut may be a slow one: the chase's opening cues still start

        void TickCrocs(string shot, float into, float dur, float duck)
        {
            bool swim = shot == SwimShot, snap = shot == SnapShot || ChaseGoesOn(shot);
            ChaseClock(shot, into, dur, swim);                                                            // the chase goes on into the window (OzL16BriefingStage.WindowChase.cs)
            if ((swim || snap) && !crocsOn) EnterCrocs();
            if (!crocsOn) { WakeTick(null, default, 0f, 0f, duck); return; }
            // the pool's second animal has its own place ON the chase's line: it lies lurking beside the line while the chase passes
            if (snapper != null) PoseCroc(snapper, LurkAt, LurkHeading, WaitTake, Time.unscaledTime, true, 0f);
            if (!swim && !snap)
            {   // the chase is over (the cut to the mission): the animal lies where it got to, the robot went with the cut
                if (churn && churn.isPlaying) churn.Stop();
                churn = null;
                if (robotT) Show(robotRends, ref robotShown, false);
                RobotNow = null; prevChase = -1f; ChaseTau = -1f;
                if (swimmer != null) { PoseCroc(swimmer, lastCrocAt, lastCrocHeading, SwimTake, Time.unscaledTime * 0.35f, true, 0f); SwimmerSpeed = 0f; }
                WakeTick(swimmer, lastCrocAt, lastCrocHeading, 0f, duck);
                return;
            }
            if (swim) poolDur = Mathf.Max(1f, dur);
            float tau = swim ? into : poolDur + into;                                                 // the chase clock
            if (!swim && chaseBegun && !float.IsNaN(chaseVoice0)) { float vc = VoiceClock(); if (!float.IsNaN(vc)) tau = Mathf.Max(poolDur, vc - chaseVoice0); }   // run 15: on the voice clock after the pool shot (the swamp filler and our fort follow it: a shot's own clock starts again at each cut)
            ChaseTau = tau;
            bool run = prevChase >= 0f && tau > prevChase && tau - prevChase < 0.5f;                     // it runs: not a held frame, not a jump
            Vector2 line = ChaseTo - ChaseFrom; float len = line.magnitude; Vector2 dir = len > 0.01f ? line / len : Vector2.up;
            float heading = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            float robotD = Mathf.Min(len, tau * RobotSpeed);
            float gap = Mathf.Lerp(GapStart, GapEnd, Mathf.Clamp01(tau / Mathf.Max(0.5f, poolDur + 1.7f)));
            Vector2 robotAt = ChaseFrom + dir * robotD, crocAt = ChaseFrom + dir * (robotD - gap);
            Vector3 fwd = new Vector3(dir.x, 0f, dir.y);
            var fx = OpenZFxPlayer.I;
            int snaps = 1 + SnapsInCut.Length;
            // ---- the robot: waist-deep, leaning into its flight, arms pumping; a splash burst at the hand that comes down
            if (robotT)
            {
                float wy = WaterY(robotAt.x, robotAt.y);
                // it faces the way it flees, its back to the crocodile; every stroke dips the body and rolls the shoulders to that
                // side on top of a slower swell (never a body sliding at one height); once it looks back over its shoulder
                float sp = tau / Mathf.Max(0.05f, StrokeEvery) * Mathf.PI, calmK = OzMotion.Reduced ? 0.4f : 1f, face = Mathf.Cos(RobotYawOffset * Mathf.Deg2Rad);
                float bob = ((0.35f - Mathf.Abs(Mathf.Sin(sp))) * RobotBob + Mathf.Sin(tau * 2.3f + 0.6f) * RobotBob * 0.5f + Mathf.Sin(tau * 5.9f) * RobotBob * 0.2f) * calmK;
                float roll = (Mathf.Sin(sp) * RobotRoll + Mathf.Sin(tau * 1.7f) * 4f) * calmK;
                robotT.SetPositionAndRotation(new Vector3(robotAt.x, wy - RobotSink + bob, robotAt.y),
                    Quaternion.Euler((RobotLean + Mathf.Sin(tau * 4.1f) * RobotPitchSwing * calmK) * face, heading + RobotYawOffset + Mathf.Sin(sp * 0.5f + 0.4f) * 6f * calmK, roll));
                if (robotPoser != null) robotPoser.Pose(0, Mathf.Repeat(tau * RobotBeat, Mathf.Max(0.05f, robotPoser.Length(0))));
                if (robotHead)
                {
                    float lk = Mathf.Clamp01((tau - RobotLookBackAt) / 0.25f) * Mathf.Clamp01((RobotLookBackAt + RobotLookBackFor - tau) / 0.3f);
                    if (lk > 0.001f) robotHead.rotation = Quaternion.AngleAxis(RobotLookBackDeg * lk * lk * (3f - 2f * lk), robotT.up) * robotHead.rotation;
                }
                Show(robotRends, ref robotShown, true); RobotNow = robotT;
                int stroke = Mathf.FloorToInt(tau / StrokeEvery);
                if (run && stroke != Mathf.FloorToInt(prevChase / StrokeEvery))
                {
                    var surf = new Vector3(robotAt.x, wy + 0.03f, robotAt.y);
                    var hand = (stroke & 1) == 0 ? robotHandL : robotHandR;
                    Vector3 hp = hand ? hand.position : surf; hp.y = wy + 0.05f;
                    if (fx)
                    {
                        fx.Splash(hp, 0.45f); fx.Droplets(hp + Vector3.up * 0.25f, 0.6f, 7);
                        fx.Ripple(surf, 0.7f, 1.6f);                                                  // a ring from every stroke
                        fx.Ripple(surf - fwd * 1.2f, 0.5f, 2.2f);                                     // its own wake: the churned water behind it
                    }
                    RobotStrokes++;
                    if ((stroke & 1) == 0) Play("brief_l16_robot_stroke", "sd_robot_legs_kick_a", GainStroke, duck, shot);
                }
            }
            // ---- the crocodile: the swim take, the lunge take mixed in round every snap (jaws wide before it, shut ON it)
            if (swimmer != null)
            {
                float beat = tau * Mathf.Clamp(RobotSpeed / Mathf.Max(0.1f, SwimTakeSpeed), 0.7f, 2.2f);
                float lt = 0f, lw = 0f;
                for (int j = 0; j < snaps; j++)
                {
                    float s = SnapTime(j), a0 = s - LungeSnapAt, a1 = s + LungeAfter;
                    if (j + 1 < snaps) a1 = Mathf.Min(a1, SnapTime(j + 1) - LungeSnapAt);
                    if (tau < a0 || tau > a1) continue;
                    lt = tau - a0; lw = Mathf.Clamp01(Mathf.Min((tau - a0) / Mathf.Max(0.02f, LungeBlend), (a1 - tau) / Mathf.Max(0.02f, LungeBlend)));
                }
                float cwy = WaterY(crocAt.x, crocAt.y);
                swimmer.t.SetPositionAndRotation(new Vector3(crocAt.x, cwy + CrocLift, crocAt.y), Quaternion.Euler(0f, heading + CrocYawOffset, 0f));
                if (!swimmer.own)
                {
                    if (lw > 0.001f) Animals?.CrocPoseMix(swimmer.t, SwimTake, beat, "lunge_bite", lt, lw, RobotSpeed);
                    else Animals?.CrocPose(swimmer.t, SwimTake, beat, RobotSpeed);
                }
                else PoseCroc(swimmer, crocAt, heading, lw > 0.5f ? "lunge_bite" : SwimTake, lw > 0.5f ? lt : beat, lw <= 0.5f, RobotSpeed);
                SwimmerSpeed = RobotSpeed; lastCrocAt = crocAt; lastCrocHeading = heading;
                WakeTick(swimmer, crocAt, heading, RobotSpeed, duck);
                Vector3 jaws = new Vector3(crocAt.x, cwy + 0.05f, crocAt.y) + fwd * 1.7f;
                JawsAt = jaws + Vector3.up * 0.35f;
                if (run && fx && Mathf.FloorToInt(tau / Mathf.Max(0.1f, CrocRingEvery)) != Mathf.FloorToInt(prevChase / Mathf.Max(0.1f, CrocRingEvery)))
                {   // rings spread from its head, the water closes behind its tail
                    fx.Ripple(jaws, 0.9f, 1.8f);
                    fx.Ripple(new Vector3(crocAt.x, cwy + 0.03f, crocAt.y) - fwd * 1.5f, 1.1f, 2.4f);
                }
                for (int j = 0; j < snaps; j++)
                {
                    float s = SnapTime(j);
                    if (j == 0 && ChaseCrossed(s - LungeSnapAt, tau)) PlayRoar(j, "brief_l16_croc_roar", "sd_croc_lunge_roar_a", duck, shot);   // never on the far clack, never under a word (WindowChase)
                    if (!ChaseCrossed(s, tau)) continue;
                    Snapped = true; Snaps++;
                    if (SnapSound)
                    {
                        PlayJaw(GainSnap, duck, shot);                                                         // the crocodile's own jaw event (WindowChase)
                        splash = Play("brief_l16_croc_splash", "sd_croc_lunge_splash_c", GainSplash, duck, shot); splashAt = Time.unscaledTime;
                    }
                    if (fx) { fx.Splash(jaws, 1.0f); fx.Droplets(jaws + Vector3.up * 0.4f, 0.9f, 10); }
                }
                if (tau >= SnapTime(0)) Snapped = true;                                                // a held frame after a snap
            }
            else WakeTick(null, default, 0f, 0f, duck);
            if (ChaseCrossed(0.05f, tau))
            {
                PlayWordFree("brief_l16_robot_alarm", "sd_robot_croc_alarm_a", GainAlarm, duck, shot);             // only between words (WindowChase)
                PlayRoar(-1, "brief_l16_croc_growl", "sd_croc_warning_a", duck, shot);
                Play("brief_l16_croc_surface", "sd_croc_surface_swell_a", GainSurface, duck, shot);
            }
            if (Sounds && !churnTried) { churnTried = true; churn = Play("brief_l16_water_churn", "sd_croc_thrash_loop_a", GainChurn, duck, shot, true); }
            prevChase = tau;
        }

        // ------------------------------------------------------------------------------------------------ the wake
        void WakeTick(Croc c, Vector2 at, float heading, float speed, float duck)
        {
            bool moving = c != null && speed > WakeFrom;
            wakeK = Mathf.MoveTowards(wakeK, moving ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(0.05f, WakeFade));
            // sound: the wake loop while it moves, out over 0.6 s
            wakeLoopK = Mathf.MoveTowards(wakeLoopK, moving ? 1f : 0f, Time.unscaledDeltaTime / (moving ? 0.3f : 0.6f));
            if (moving && Sounds && (!wakeLoop || !wakeLoop.isPlaying)) wakeLoop = Play("brief_l16_croc_wake", "sd_croc_wake_loop_b", GainWake, duck, SwimShot, true);
            if (wakeLoop)
            {
                int vi = voices.IndexOf(wakeLoop);
                if (vi >= 0) voiceGain[vi] = GainWake * wakeLoopK;
                if (wakeLoopK <= 0.001f && wakeLoop.isPlaying) { wakeLoop.Stop(); OzSoundLog.Add("L16B", "brief_l16_croc_wake", wakeLoop.clip ? wakeLoop.clip.name : null, "STOP loop (the crocodile lies still)"); }
                if (wakeLoopK <= 0.001f && !wakeLoop.isPlaying) wakeLoop = null;                      // the source is free for the next sound
            }
            if (c == null || !c.own) { WakeOn = c != null && moving; if (wakeGo) wakeGo.SetActive(false); return; }   // a lent animal: the level draws its wake (CrocPose speed)
            if (!wakeGo) { WakeOn = false; return; }
            bool show = wakeK > 0.01f;
            if (wakeGo.activeSelf != show) wakeGo.SetActive(show);
            WakeOn = show && moving;
            if (!show) return;
            // the level's own wake rule: the V mesh is one metre long, scaled to the length for this speed, its point ahead of the middle
            float length = Mathf.Min(WakeMax, WakeBase + WakePerMps * Mathf.Max(speed, WakeFrom));
            var turn = Quaternion.Euler(0f, heading + CrocYawOffset, 0f);
            wakeGo.transform.SetPositionAndRotation(new Vector3(at.x, WaterY(at.x, at.y) + WakeLift, at.y) + turn * Vector3.forward * WakeApexAhead, turn);
            wakeGo.transform.localScale = new Vector3(length, 1f, length);
            var col = WakeTint; col.a *= wakeK * Mathf.Lerp(1f, 0.6f, DetailKit.Night);
            if (wakeMat.HasProperty(BaseColorId)) wakeMat.SetColor(BaseColorId, col);
            if (wakeMat.HasProperty(ColorId)) wakeMat.SetColor(ColorId, col);
        }

        /// <summary>The stage's own wake (only for its own crocodile): the level's V mesh (CrocFx_WakeV of the surface-effects file, its
        /// fade in its vertex colours) with the level's wake material, or the same material built here from the wake picture.</summary>
        void BuildWake()
        {
            wakeTried = true;
            var fx = Resources.Load<GameObject>(WakeFxRes);
            if (!fx) { Warn("wake", $"the staged crocodile has no V wake: Resources/{WakeFxRes} is missing (the level's surface-effects file: see the head of OzL16BriefingStage.cs)"); return; }
            var shared = Resources.Load<Material>(WakeMatRes);
            if (shared) wakeMat = new Material(shared) { name = "BriefingWake (runtime)" };
            else
            {
                var tex = Resources.Load<Texture2D>(WakeTexRes);
                var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (!tex || !sh)
                {
                    Warn("wake", $"the staged crocodile has no V wake: neither the material Resources/{WakeMatRes} nor the picture Resources/{WakeTexRes} with a see-through unlit surface was found");
                    return;
                }
                wakeMat = new Material(sh) { name = "BriefingWake (runtime)" };
                if (wakeMat.HasProperty("_BaseMap")) wakeMat.SetTexture("_BaseMap", tex); else wakeMat.mainTexture = tex;
                wakeMat.SetFloat("_Surface", 1f); wakeMat.SetFloat("_Blend", 0f); wakeMat.SetFloat("_ZWrite", 0f); wakeMat.SetFloat("_Cull", (float)CullMode.Off);
                wakeMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); wakeMat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                wakeMat.SetOverrideTag("RenderType", "Transparent");
                wakeMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); wakeMat.DisableKeyword("_ALPHATEST_ON");
                wakeMat.renderQueue = (int)RenderQueue.Transparent + 4;
            }
            wakeMat.hideFlags = HideFlags.DontSave;
            wakeGo = new GameObject("BriefingWake"); wakeGo.transform.SetParent(transform, false);
            var inst = Instantiate(fx, wakeGo.transform, false); inst.name = "Fx";                    // the whole file, so its own axes hold with any import
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                bool v = !wakeRend && r.name.StartsWith("CrocFx_WakeV");
                if (v) { wakeRend = r; r.sharedMaterial = wakeMat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
                else r.gameObject.SetActive(false);                                                    // its rings and bubbles are not used here
            }
            if (!wakeRend)
            {
                Warn("wake", $"the staged crocodile has no V wake: no mesh CrocFx_WakeV in Resources/{WakeFxRes}");
                Destroy(wakeGo); Destroy(wakeMat); wakeGo = null; wakeMat = null; return;
            }
            wakeGo.SetActive(false);
        }

        // ------------------------------------------------------------------------------------------------ sounds
        static float Bus() => CombatAudio.I ? CombatAudio.I.volume * CombatAudio.BusGain(CombatAudio.Bus.World) : 0.55f;

        AudioClip Clip(string file)
        {
            if (!sfx.TryGetValue(file, out var clip)) sfx[file] = clip = Resources.Load<AudioClip>(file.IndexOf('/') >= 0 ? file : SfxRes + file);   // a full path: the level's own bed loops
            return clip;
        }

        AudioSource Play(string id, string file, float gain, float duck, string shot, bool loop = false)
        {
            if (!Sounds) return null;
            var clip = Clip(file);
            if (!clip)
            {
                OzSoundLog.Add("L16B", id, null, "DROP clip missing (" + file + ")");
                Warn("sfx " + file, $"silent cue {id}: Resources/{SfxRes}{file} is missing (nine clips belong there: frog, flutter, three squawks, wake loop, swell, snap, splash)");
                return null;
            }
            AudioSource src = null; int at = -1;
            for (int i = 0; i < voices.Count; i++) if (voices[i] && !voices[i].isPlaying) { src = voices[i]; at = i; break; }
            if (!src)
            {
                src = gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false; src.spatialBlend = 0f; src.dopplerLevel = 0f; src.bypassReverbZones = true; src.priority = 60;
                voices.Add(src); voiceGain.Add(gain); at = voices.Count - 1;
            }
            voiceGain[at] = gain;
            src.clip = clip; src.loop = loop; src.pitch = loop ? 1f : UnityEngine.Random.Range(0.97f, 1.03f); src.panStereo = 0f;
            src.volume = gain * Bus() * duck; src.Play();
            SoundsStarted++;
            OzSoundLog.Add("L16B", id, clip.name, (loop ? "LOOP" : "PLAY") + " 2D briefing " + shot + (duck < 0.999f ? " (under the voice)" : ""), src.volume);
            return src;
        }

        // ------------------------------------------------------------------------------------------------ hand-back
        /// <summary>Everything staged ends at once: lent animals go back under the level's rules, the stage's own actors and its wake
        /// are destroyed, its sounds stop. Called when the flight hands the camera back (also on a skip) and when the scene goes.</summary>
        void Release(string why)
        {
            bool had = staged || swimmer != null || snapper != null || birds.Count > 0;
            staged = false; crocsOn = false; shotNow = null; prevInto = -1f; ChaseTau = -1f; ChaseRelease();
            ReleaseSunBirds();
            foreach (var c in new[] { swimmer, snapper })
            {
                if (c == null) continue;
                if (c.own) { c.poser?.Dispose(); if (c.t) Destroy(c.t.gameObject); }
                else { try { Animals?.CrocReturn(c.t); } catch (Exception e) { Debug.LogException(e); } }
                GivenBack++;
            }
            swimmer = snapper = null; Swimmer = Snapper = null; SwimmerSpeed = 0f; WakeOn = false;
            if (robotT) { robotPoser?.Dispose(); Destroy(robotT.gameObject); GivenBack++; }
            robotT = robotHandL = robotHandR = robotHead = null; robotPoser = null; robotRends = null; robotTried = false; RobotNow = null;
            prevChase = -1f; churn = bedDay = bedSwamp = null; churnTried = bedTried = false; bedSwampK = 0f;
            foreach (var b in birds)
            {
                if (b == null) continue;
                if (b.own) { b.poser?.Dispose(); if (b.t) Destroy(b.t.gameObject); }
                else { Show(b.rends, ref b.shown, true); try { Animals?.ParrotReturn(b.t); } catch (Exception e) { Debug.LogException(e); } }
                GivenBack++;
            }
            birds.Clear(); BirdsNow.Clear();
            swimMissing = snapMissing = parrotMissing = false;                                        // the next flight asks again (the level may lend by then)
            if (wakeGo) Destroy(wakeGo);
            if (wakeMat) Destroy(wakeMat);
            wakeGo = null; wakeRend = null; wakeMat = null; wakeTried = false; wakeK = 0f; wakeLoopK = 0f;
            foreach (var v in voices) if (v && v.isPlaying) v.Stop();
            wakeLoop = null; flutter = null; splash = null;
            if (had) Debug.Log($"[OzL16BriefingStage] released ({why}): {GivenBack} animals given back or removed so far, {SoundsStarted} sounds started so far");
        }

        /// <summary>One-line state for the Play check.</summary>
        public static string Status()
        {
            if (!I) return "OzL16BriefingStage: not in this scene (it attaches itself on Level 16 only)";
            return $"OzL16BriefingStage: {(I.staged ? "staging shot " + I.shotNow : "idle")}, animals {(Animals != null ? "lent by the level" : "the stage's own")}, "
                 + $"birds shown {BirdsNow.Count}, swimmer {(Swimmer ? Swimmer.position.ToString("F1") + " at " + SwimmerSpeed.ToString("F1") + " m/s, wake " + (WakeOn ? "on" : "off") : "none")}, "
                 + $"near animal {(Snapper ? (Snapped ? "jaws shut" : "waiting") : "none")}, flights {Flights}, lent {Lent}, built {Built}, given back {GivenBack}, sounds {SoundsStarted}"
                 + (LastWarning.Length > 0 ? " | left out: " + LastWarning : "");
        }
    }
}
