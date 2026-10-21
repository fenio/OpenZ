using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// Level 16: the crocodiles on screen. The rule is the simulation's (Sim/ZWorld.Crocodiles.cs: where each animal is, what it does,
    /// which robot it took); this draws it: the 3.3 m model with its takes (lurking with the eye hoods out, the slow pass, nose-only
    /// travel with the V wake, the rise, the lunge, the roll, the drag under, the quiet dive), the signs on the water (the V wake as
    /// the prepared picture cut to its length, slow rings round a lurking head and behind a swimmer, bubbles, splashes: on the water
    /// only), the robot in the jaws (hung on the prey bone from the bite on, gone under with the animal), the helmet that bobs up
    /// afterwards, and every sound. Away from an attack: critter_croc_pass_ripple (a quiet loop) while it swims its stretch,
    /// critter_croc_lurk_lap (no file yet) with the slow rings of a lurking head, critter_croc_lurk_blup. In the order of the attack:
    ///   critter_croc_warning (growl) + critter_croc_bubbles_rise, critter_croc_wake_loop while it stalks, critter_croc_surface at the
    ///   rise, critter_croc_lunge_roar + critter_croc_lunge_splash, critter_croc_jaw_snap + critter_croc_bite_metal + robot_eaten_cry at
    ///   the bite, critter_croc_thrash_loop + robot_legs_kick, critter_croc_tail_slap + critter_croc_drag_under, critter_croc_bubbles,
    ///   critter_croc_part_float, critter_croc_burp; a miss: critter_croc_miss_snort, critter_croc_submerge.
    /// The robots' side is heard too: robot_croc_alarm when a squad starts to hurry, and the three spoken moments (vo_croc_hurry,
    /// vo_croc_flee, vo_croc_bank) as named events that stay silent until their lines are recorded.
    /// Two hooks for the mission-start flyover (they change what is DRAWN, never the rule): <see cref="ShowSwim"/> and <see cref="ShowSnap"/>.
    /// View only; boots itself on level 16.
    /// </summary>
    [DefaultExecutionOrder(76)]   // after UnitView (60) and OpenZWading (70): the robot in the jaws is placed last
    public sealed partial class CrocodileView : MonoBehaviour                         // J15: CrocodileView.J15.cs
    {
        public static CrocodileView I { get; private set; }
        public static bool AutoBoot = true;

        [Tooltip("crossfade between two takes (s)")] public float fade = 0.22f;
        [Tooltip("the V wake, a flat picture on the water that is cut to its length and never stretched: behind the part of the animal that is out of the water 1.2 m + 1.1 m per m/s, 8 m at most from its point; from 0.8 m/s on; in over 0.3 s, out over 0.6 s")]
        public float wakeBase = 1.2f, wakePerMps = 1.1f, wakeMax = 8f, wakeFrom = 0.8f, wakeFadeIn = 0.3f, wakeFade = 0.6f;
        [Tooltip("the wake's strength: 0.44 + 0.055 per m/s, 0.72 at most, times wakeGain (1 = as prepared; the one number to raise if it reads too faint on the game's water)")]
        public float wakeStrength = 0.44f, wakeStrengthPerMps = 0.055f, wakeStrengthMax = 0.72f, wakeGain = 1.5f;
        [Tooltip("the wake's size on the water: 1 = the picture as prepared (8 m long, 6.4 m wide); more = the same V wider and longer (its arms keep their angle), so that it is seen from the default camera height")]
        public float wakeSize = 2.4f;
        [Tooltip("the helmet that bobs up afterwards: its size against the robot's own head (it must read from camera height 40), how far it bobs (m), the strength of its ring")]
        public float helmetScale = 2.0f, helmetBob = 0.08f, helmetRing = 0.4f;
        [Tooltip("rings (the game's own ring effect), only where the camera looks: round a lurking head one every 1.3 s that lives 4 s and ends 1.5 m wide in radius (three alive); behind a slow swimmer one every 0.6 s that lives 2.4 s and ends 1.3 m")]
        public float lurkRingEvery = 1.3f, lurkRingLife = 4f, lurkRingM = 1.5f, swimRingEvery = 0.6f, swimRingLife = 2.4f, swimRingM = 1.3f;
        /// <summary>The wake's picture and its material under Resources/OpenZ/L16Animals (the material is made by the menu OpenZ/L16 Jungle/10 Animals).</summary>
        public const string WakePicture = "fx_wake_v_1024", WakeMaterial = "CrocFx_WakeCard";
        // the picture: 8 m long (its top is the point of the V), 6.4 m wide, the arms 19.5 degrees to each side; the game's ring ends 4.65 times its strength in radius (OpenZFxPlayer.Ripple)
        const float WakeLong = 8f, WakeWide = 6.4f, WakeArm = 0.354f, RingGrows = 4.65f; const int WakeRows = 16;
        public bool sounds = true;
        /// <summary>Which baked part of the robot bobs up afterwards (the end of its bone's name): "Head" = its helmet. "LeftForeArm" gives
        /// an arm instead, should the helmet ever read as anything but a toy's.</summary>
        public static string FloatingPart = "Head";

        // ------------------------------------------------------------------ proof counters (Editor/OzL16AnimalsCheck.cs)
        public static int Shown, BitesSeen, PartsFloated;
        /// <summary>The least drawn free water (m) round any animal's outline since the level started (ZGame.GroundY against the pool's
        /// water height, 8 outline points and a ring 0.6 m round them); 99 = not sampled yet. Filled by <see cref="SampleBanks"/>.</summary>
        public static float LeastDrawnBank = 99f; public static int BankSamples, BankNear;

        sealed partial class View
        {
            public ZCroc c; public L16AnimalKit.Rig rig; public Vector3 pos; public float yaw, yawRate; public bool placed;
            public int take = -1, prev = -1; public float takeT0, prevT0, mixT; public bool prevLoop, takeLoop;
            public ZCrocState state = ZCrocState.Lurk; public double since;
            public Transform prey, wakeTr; public Renderer wakeR; public Mesh wakeMesh; public float wakeLen, wakeAlpha, wakeNow, wakeApex = 1.35f, wakeOut = 1.45f, wakeCut = -1f, wakeCutOut = -1f;
            public AudioSource wakeLoop, thrashLoop, passLoop, lapLoop; public bool passAsked, lapAsked; public float lapOffT, lurkRingT, swimRingT;
            public UnitView robot; public Quaternion robotRel; public Vector3 robotFrom; public float robotT; public bool robotHidden; public Transform helmet;
            public float ringT, blupT, splashT, wakeRingT; public int ringsLeft; public Vector3 ringAt;
            // the flyover's show: the drawn animal follows a line or snaps where it was put; the rule's animal is untouched
            public int show; public Vector3 showA, showB; public float showT, showLen, showYaw, showSnapAt, showSpeed; public string showTake; public bool showSnapped;
        }
        sealed class Floater { public Transform t; public float t0, life; public Vector3 at; public float phase; public bool plopped, burped; }

        ZGame game; ZWorld W; Camera cam; float flatWater; int seen;
        readonly List<View> views = new List<View>();
        readonly List<Floater> floaters = new List<Floater>();
        Material bodyMat, eyeMat, wakeMat; MaterialPropertyBlock mpb;
        int tLurk, tPast, tNose, tNoseL, tNoseR, tRise, tLunge, tThrash, tDrag, tDive;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color");

        // ------------------------------------------------------------------ boot
        static bool hooked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!hooked) { hooked = true; SceneManager.sceneLoaded += (s, m) => TryAttach(); }
            TryAttach();
        }

        public static void TryAttach()
        {
            if (!AutoBoot || !ZGame.I || ZGame.I.level != "L16" || FindFirstObjectByType<CrocodileView>()) return;
            new GameObject("CrocodileView").AddComponent<CrocodileView>();
        }

        void Awake() { I = this; }

        void Start()
        {
            game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            if (!game || game.World == null || game.level != "L16") { enabled = false; return; }
            W = game.World; cam = Camera.main; mpb = new MaterialPropertyBlock();
            L16AnimalKit.NewLevel(); Shown = BitesSeen = PartsFloated = 0; LeastDrawnBank = 99f; BankSamples = BankNear = 0;
            flatWater = DetailKit.WaterY(game.level);
            seen = W.CrocNoteCount;
            if (W.Crocs.Count == 0) { Debug.Log("[OpenZ] CrocodileView L16: the rule has no crocodile (switch off): nothing to draw"); enabled = false; return; }
            bodyMat = L16AnimalKit.Mat("L16_Croc_Body", "croc_basecolor", 0.32f);
            eyeMat = L16AnimalKit.Mat("L16_Croc_Eye", "croc_eye", 0.6f);
            wakeMat = Resources.Load<Material>(L16AnimalKit.ModelRes + WakeMaterial);
            foreach (var c in W.Crocs)
            {
                var v = new View { c = c, state = c.State, since = c.Since };
                v.rig = L16AnimalKit.Rig.Make("Croc", "Crocodile" + c.Index, transform, new[] { bodyMat, eyeMat }, 1f, false);   // no cast shadow: it is under water
                if (v.rig == null) { views.Add(v); continue; }
                v.rig.FaceAhead("head", "tail5");
                v.prey = L16AnimalKit.Find(v.rig.model, "prey");
                if (wakeMat)
                {
                    var wg = new GameObject("Wake"); wg.transform.SetParent(v.rig.root, false);
                    v.wakeMesh = NewWakeMesh(c.Index);
                    wg.AddComponent<MeshFilter>().sharedMesh = v.wakeMesh;
                    v.wakeR = wg.AddComponent<MeshRenderer>(); v.wakeR.sharedMaterial = wakeMat; v.wakeR.shadowCastingMode = ShadowCastingMode.Off; v.wakeR.receiveShadows = false; v.wakeR.enabled = false;
                    v.wakeTr = wg.transform;
                }
                v.blupT = 3f + (c.Index * 1.7f) % 5f; v.lurkRingT = (c.Index * 0.37f) % 1.3f;
                views.Add(v);
            }
            var r0 = views.Count > 0 ? views[0].rig : null;
            if (r0 != null)
            {
                tLurk = r0.Take("lurk"); tPast = r0.Take("swim_past"); tNose = r0.Take("nose_travel"); tNoseL = r0.Take("nose_turn_L"); tNoseR = r0.Take("nose_turn_R");
                tRise = r0.Take("nose_rise"); tLunge = r0.Take("lunge_bite"); tThrash = r0.Take("thrash_roll"); tDrag = r0.Take("drag_under"); tDive = r0.Take("submerge");
            }
            J15Start(views.Count > 0 ? views[0] : null);                                           // J15: the robot's seat in the jaws (the prey socket's turn)
            Debug.Log($"[OpenZ] CrocodileView L16: {views.Count} crocodiles, model {(r0 != null && r0.ok ? r0.Takes + " takes" : "MISSING")}, wake {(wakeMat ? "the picture " + WakePicture : "rings (no wake material: run the menu OpenZ/L16 Jungle/10 Animals)")}, attack water {AttackWater():0} m2");
        }

        double AttackWater() { double a = 0; for (int p = 0; p < W.CrocPoolCount; p++) a += W.CrocPoolAttackM2(p); return a; }

        void OnDestroy()
        {
            foreach (var v in views)
            {
                v.rig?.Dispose(); if (v.wakeMesh) Destroy(v.wakeMesh); if (v.helmet) Destroy(v.helmet.gameObject);
                if (v.wakeLoop) Destroy(v.wakeLoop.gameObject); if (v.thrashLoop) Destroy(v.thrashLoop.gameObject); if (v.passLoop) Destroy(v.passLoop.gameObject); if (v.lapLoop) Destroy(v.lapLoop.gameObject);
            }
            foreach (var f in floaters) if (f.t) Destroy(f.t.gameObject);
            if (I == this) I = null;
        }

        // ------------------------------------------------------------------ world
        float WaterAt(float x, float z) => OzWaterLevel.At(game.level, x, z, flatWater);
        Vector3 World(double px, double py) { float x = (float)(px / 8.0), z = (float)(-py / 8.0); return new Vector3(x, WaterAt(x, z), z); }
        static float Yaw(double heading) => Mathf.Atan2((float)System.Math.Cos(heading), (float)-System.Math.Sin(heading)) * Mathf.Rad2Deg;   // map plane (0 = east, +y south) to a turn about Y
        bool OnScreen(Vector3 p) => L16AnimalKit.InView(cam, p, 6f);

        void Snd(string evt, Vector3 at, float vol, float min, float max, params string[] files) { if (sounds) L16AnimalKit.Play(evt, at, vol, min, max, 1f, files); }

        // ------------------------------------------------------------------ the eaten robot
        /// <summary>UnitView.EatenByCrocodile: the animal that took this robot hangs it in its jaws. False = no crocodile is drawn.</summary>
        public static bool Take(UnitView robot)
        {
            if (!I || robot == null || robot.Obj == null) return false;
            int k = robot.Obj.EatenBy;
            if (k < 0 || k >= I.views.Count || I.views[k].rig == null || !I.views[k].rig.ok || !I.views[k].prey) return false;
            var v = I.views[k];
            v.robot = robot; v.robotT = 0f; v.robotFrom = robot.transform.position; v.robotHidden = false;
            v.robotRel = Quaternion.Inverse(v.prey.rotation) * robot.transform.rotation;       // it keeps the way it stood: no pop at the bite
            I.PrepareHelmet(v, robot);
            I.J15Take(v, robot);                                                                // J15: its grip point (hips), the catch
            return true;
        }

        // ------------------------------------------------------------------ the flyover's hooks
        /// <summary>
        /// FLYOVER HOOK 1. The crocodile nearest to `borrowNear` (a world point; its pool's own animal) is DRAWN swimming the straight
        /// line from `from` to `to` (world x, z; the height is the pool's water) for `seconds` in the take `take` ("swim_past": head
        /// and back showing with a 2.9 m wake at 1.5 m/s; "nose_travel": nose and eyes only), then it dives where it is and comes up
        /// again where the rule has it: back under its normal rule. The rule's own animal never moves for this: a match plays the
        /// same with or without a flyover. The line is refused (false, nothing changes) when any point of it has less than 2.4 m of
        /// the animal's own free water (it would come too near a bank), when no such animal exists or when it is hunting. If the rule
        /// starts a hunt with this animal meanwhile, the show ends at once.
        /// </summary>
        public static bool ShowSwim(Vector3 borrowNear, Vector3 from, Vector3 to, float seconds, string take = "swim_past")
        {
            if (!I) return false;
            var v = I.Nearest(borrowNear); if (v == null || v.c.Busy || seconds <= 0f) return false;
            float len = Vector3.Distance(new Vector3(from.x, 0, from.z), new Vector3(to.x, 0, to.z));
            int n = Mathf.Max(1, Mathf.CeilToInt(len / 0.25f));
            for (int i = 0; i <= n; i++) { var p = Vector3.Lerp(from, to, i / (float)n); if (I.W.CrocFreeAt(p.x * 8.0, -p.z * 8.0) < ZWorld.CrocTurnM) return false; }
            v.show = 1; v.showA = from; v.showB = to; v.showT = 0f; v.showLen = seconds; v.showTake = take; v.showSpeed = len / seconds;
            v.showYaw = Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;
            I.BeginShow(v, from);
            return true;
        }

        /// <summary>
        /// FLYOVER HOOK 1b (for the cut to the crocodiles). The crocodile nearest to `borrowNear` is DRAWN at `at` (world x, z) facing
        /// `yawDeg` (a turn about Y: 0 = north, 90 = east), head up, and snaps its jaws at the air `snapAtSeconds` from now (the lunge
        /// take without a robot, with its splash and the jaw snap sound on that frame), then dives and comes up where the rule has it.
        /// Refused (false) when the lunge does not fit there with 0.6 m of water to spare (2.85 m of its own free water).
        /// </summary>
        public static bool ShowSnap(Vector3 borrowNear, Vector3 at, float yawDeg, float snapAtSeconds)
        {
            if (!I) return false;
            var v = I.Nearest(borrowNear); if (v == null || v.c.Busy) return false;
            if (I.W.CrocFreeAt(at.x * 8.0, -at.z * 8.0) < 2.85) return false;
            v.show = 2; v.showA = v.showB = at; v.showT = 0f; v.showYaw = yawDeg; v.showSnapAt = Mathf.Max(0.47f, snapAtSeconds); v.showLen = v.showSnapAt + 0.6f; v.showSnapped = false;
            I.BeginShow(v, at);
            return true;
        }

        /// <summary>A show begins: the drawn animal is not slid from its own place across the pool (and perhaps over a bank) to the show's
        /// first place: it is gone where it was and comes up there, in the show's first frame, with no blend from the take it was in
        /// (bubbles and one ring there where the camera looks, as when the rule moves an animal to another place).</summary>
        void BeginShow(View v, Vector3 at)
        {
            v.placed = false; v.take = -1; v.prev = -1; v.yawRate = 0f;
            v.wakeAlpha = 0f; v.wakeLen = 0f; if (v.wakeR) v.wakeR.enabled = false;
            at.y = WaterAt(at.x, at.z);
            if (OpenZFxPlayer.I && OnScreen(at)) { Bubbles(at, 1.0f); OpenZFxPlayer.I.Ripple(at + Vector3.up * 0.03f, 0.5f); }
        }

        /// <summary>
        /// FLYOVER HOOK 1c (the lending the flyover stage asks for: its own interface is served by OzL16AnimalsForBriefing.cs).
        /// Lend: the crocodile nearest to `near` (a world point) that is drawn, not lent and not hunting; from now on this view neither
        /// moves nor animates it: whoever borrowed it sets the position and heading of the returned root (the body's middle on the
        /// water surface, +Z = the nose) and calls LendPose every frame. LendPose: that take at that second (a looping take wraps, a
        /// one-shot holds its end); a speed over 0.8 m/s draws the V wake by the level's own rule (cut before a bank). LendReturn: at
        /// once back at its own place and state under the rule. Null / nothing when there is no such animal. The rule's animal is
        /// never touched; if the rule needs the animal (a hunt), the loan ends by itself and LendPose does nothing more.
        /// </summary>
        public static Transform Lend(Vector3 near)
        {
            if (!I) return null;
            var v = I.Nearest(near); if (v == null || v.c.Busy) return null;
            v.show = 3; v.rig.Show(true); if (v.wakeLoop) L16AnimalKit.StopLoop("critter_croc_wake_loop", ref v.wakeLoop);
            I.PassLoop(v, false, 0f); L16AnimalKit.StopLoop("critter_croc_lurk_lap", ref v.lapLoop); v.lapAsked = false;   // the flyover plays its own sounds for a lent animal
            return v.rig.root;
        }

        static readonly HashSet<string> LoopTakes = new HashSet<string> { "lurk", "swim", "swim_past", "turn_L", "turn_R", "nose_travel", "nose_turn_L", "nose_turn_R", "thrash_roll" };

        public static void LendPose(Transform croc, string take, float seconds, float speed)
        {
            if (!I || !croc) return;
            View v = null; foreach (var x in I.views) if (x.rig != null && x.rig.root == croc) { v = x; break; }
            if (v == null || v.show != 3) return;
            int t = v.rig.Take(take); if (t < 0) return;
            float len = Mathf.Max(0.05f, v.rig.Length(t));
            v.rig.Pose(t, LoopTakes.Contains(take) ? Mathf.Repeat(seconds, len) : Mathf.Min(seconds, len), -1, 0f, 0f, true);
            v.pos = croc.position; v.yaw = croc.eulerAngles.y; v.take = t;
            I.Wake(v, speed, Time.deltaTime);
        }

        /// <summary>LendPose with a second take mixed in (shareB = the second take's share, 0..1): the flyover's chase blends the swim
        /// into the lunge and back. Only the two named takes carry weight: the rig takes every other take's weight off.</summary>
        public static void LendPoseMix(Transform croc, string takeA, float secondsA, string takeB, float secondsB, float shareB, float speed)
        {
            if (!I || !croc) return;
            View v = null; foreach (var x in I.views) if (x.rig != null && x.rig.root == croc) { v = x; break; }
            if (v == null || v.show != 3) return;
            int a = v.rig.Take(takeA), b = v.rig.Take(takeB); if (a < 0) return;
            float la = Mathf.Max(0.05f, v.rig.Length(a)), lb = b >= 0 ? Mathf.Max(0.05f, v.rig.Length(b)) : 0f;
            shareB = b >= 0 ? Mathf.Clamp01(shareB) : 0f;
            v.rig.Pose(a, LoopTakes.Contains(takeA) ? Mathf.Repeat(secondsA, la) : Mathf.Min(secondsA, la), shareB > 0.001f ? b : -1,
                       b >= 0 ? (LoopTakes.Contains(takeB) ? Mathf.Repeat(secondsB, lb) : Mathf.Min(secondsB, lb)) : 0f, shareB, true);
            v.pos = croc.position; v.yaw = croc.eulerAngles.y; v.take = a;
            I.Wake(v, speed, Time.deltaTime);
        }

        public static void LendReturn(Transform croc)
        {
            if (!I || !croc) return;
            foreach (var v in I.views) if (v.rig != null && v.rig.root == croc && v.show == 3) { v.show = 0; v.placed = false; v.take = -1; v.prev = -1; if (v.wakeR) v.wakeR.enabled = false; v.wakeAlpha = 0f; v.wakeLen = 0f; }
        }

        /// <summary>Is a show of the flyover still drawn?</summary>
        public static bool Showing { get { if (I) foreach (var v in I.views) if (v.show != 0) return true; return false; } }

        View Nearest(Vector3 p)
        {
            View best = null; float bd = float.MaxValue;
            foreach (var v in views)
            {
                if (v.rig == null || !v.rig.ok || v.show != 0) continue;
                float dx = (float)(v.c.X / 8.0) - p.x, dz = (float)(-v.c.Y / 8.0) - p.z, d = dx * dx + dz * dz;
                if (d < bd) { bd = d; best = v; }
            }
            return best;
        }

        // ------------------------------------------------------------------ frame
        void LateUpdate()
        {
            if (W == null) return;
            if (!cam) cam = Camera.main;
            float dt = Time.deltaTime;
            for (; seen < W.CrocNoteCount; seen++)
            {
                int back = W.CrocNoteCount - seen; if (back > W.CrocNotes.Count) continue;
                OnNote(W.CrocNotes[W.CrocNotes.Count - back]);
            }
            int shown = 0;
            J15Before(dt);                                                                          // J15: the robots and vehicles (the passive sink)
            foreach (var v in views) { Step(v, dt); if (v.rig != null && v.rig.Shown) shown++; }
            Shown = shown;
            for (int i = floaters.Count - 1; i >= 0; i--) if (!StepFloater(floaters[i])) floaters.RemoveAt(i);
        }

        /// <summary>Seconds in which one squad says no second crocodile line: the later one is dropped.</summary>
        public const double CrocLineGap = 2.0;
        /// <summary>Checks: spoken lines dropped because their squad had just spoken.</summary>
        public static int CrocLinesDropped;
        /// <summary>Checks: hurry lines not said because the squad had no order (its flee line follows).</summary>
        public static int CrocHurryLeftToFlee;
        readonly Dictionary<int, double> crocLineAt = new Dictionary<int, double>();

        /// <summary>A robot's spoken line about a crocodile (hurry, flee, bank), at most one per squad in CrocLineGap seconds.</summary>
        void CrocLine(ZCrocNote n, string evt, Vector3 at, params string[] files)
        {
            var o = W.Get(n.robot); int squad = o != null && o.Leader != null ? o.Leader.Id : n.robot;
            double last;
            if (crocLineAt.TryGetValue(squad, out last) && W.Time - last < CrocLineGap)
            {
                CrocLinesDropped++;
                if (sounds) OzSoundLog.Add("L16", evt, null, $"DROP the squad spoke {W.Time - last:0.0} s ago", 0.8f, at);
                return;
            }
            crocLineAt[squad] = W.Time;
            Snd(evt, at, 0.8f, 8f, 70f, files);
        }

        void OnNote(ZCrocNote n)
        {
            var fx = OpenZFxPlayer.I;
            if (n.what == ZCrocNote.Hurry || n.what == ZCrocNote.Flee || n.what == ZCrocNote.Bank)
            {   // the robots' side
                var at = World(n.x, n.y) + Vector3.up;
                if (n.what == ZCrocNote.Hurry)
                {   // the alarm beeps for every squad; the spoken hurry line only from a squad on an order or on hold: one with neither sets
                    // off for the bank at its next look (half a second) and says its flee line then
                    Snd("robot_croc_alarm", at, 0.55f, 8f, 70f, "sd_robot_croc_alarm_a");
                    var ro = W.Get(n.robot); var lead = ro != null && ro.Leader != null ? ro.Leader : ro;
                    if (lead == null || lead.Waypoints.Count > 0 || lead.HoldPosition) CrocLine(n, "vo_croc_hurry", at);
                    else { CrocHurryLeftToFlee++; if (sounds) OzSoundLog.Add("L16", "vo_croc_hurry", null, "not said: the squad has no order, its flee line follows", 0.8f, at); }
                }
                else if (n.what == ZCrocNote.Flee) CrocLine(n, "vo_croc_flee", at, "sd_vo_croc_flee_a", "sd_vo_croc_flee_b", "sd_vo_croc_flee_c");   // the takes named: they take even turns
                else if (W.CrocBanks % 3 == 1) CrocLine(n, "vo_croc_bank", at, "sd_vo_croc_bank_a", "sd_vo_croc_bank_b");
                return;
            }
            if (n.croc < 0 || n.croc >= views.Count) return;
            var v = views[n.croc]; var p = World(n.x, n.y);
            if (v.show != 0 && n.what == ZCrocNote.State && n.state != ZCrocState.Lurk && n.state != ZCrocState.Swim) EndShow(v, false);   // the rule needs it
            switch (n.what)
            {
                case ZCrocNote.Moved:
                    if (fx && OnScreen(p)) { Bubbles(p, 1.0f); fx.Ripple(p + Vector3.up * 0.03f, 0.5f); }
                    v.placed = false;                                                                // it comes up at its new place, no slide across the pool
                    return;
                case ZCrocNote.Bite:
                    BitesSeen++;
                    J15Bite(v, n);                                                                  // J15: which catch (the slow roll or the fast dive)
                    JawClack(p);                                                                    // J15: the wet bony clack, on the bite's frame
                    Snd("critter_croc_bite_metal", p, 0.9f, 8f, 80f, "sd_croc_bite_metal_b", "sd_croc_bite_metal_a");
                    Snd("robot_eaten_cry", p + Vector3.up, 0.85f, 8f, 80f, "sd_robot_cry_cut_a");
                    if (fx) { fx.Splash(p, 1.1f); fx.Droplets(p + Vector3.up * 0.4f, 0.9f, 10); }
                    return;
                case ZCrocNote.Scared: return;
            }
            // a state began
            v.state = n.state; v.since = n.time;
            switch (n.state)
            {
                case ZCrocState.Warn:
                    Snd("critter_croc_warning", p, 1f, 10f, 110f, "sd_croc_warning_b", "sd_croc_warning_a");
                    Snd("critter_croc_bubbles_rise", p, 0.5f, 8f, 60f, "sd_croc_bubbles_b", "sd_croc_bubbles_a");
                    Bubbles(p, 1.0f); v.ringsLeft = 3; v.ringT = 0f; v.ringAt = p;
                    break;
                case ZCrocState.Stalk:
                    if (!v.wakeLoop && sounds) v.wakeLoop = L16AnimalKit.Loop("critter_croc_wake_loop", transform, p, 0.55f, 6f, 60f, "sd_croc_wake_loop_b", "sd_croc_wake_loop_a");
                    break;
                case ZCrocState.Rise:
                    L16AnimalKit.StopLoop("critter_croc_wake_loop", ref v.wakeLoop);
                    Snd("critter_croc_surface", p, 0.7f, 8f, 90f, "sd_croc_surface_swell_a", "sd_croc_surface_swell_b");
                    if (fx) fx.Splash(p, 0.7f);
                    v.ringsLeft = 3; v.ringT = 0f; v.ringAt = p;
                    break;
                case ZCrocState.Lunge:
                    Snd("critter_croc_lunge_roar", p + Vector3.up * 0.8f, LungeRoarVol, 10f, 110f, "sd_croc_lunge_roar_a");   // J15: was 0.95 (it read as a blast)
                    v.splashT = 0.23f;                                                               // the burst out of the water, 0.23 s into the take
                    break;
                case ZCrocState.Thrash:
                    if (v.fast) break;                                                              // J15: the fast catch dives instead of rolling
                    if (!v.thrashLoop && sounds) v.thrashLoop = L16AnimalKit.Loop("critter_croc_thrash_loop", transform, p, 0.75f, 5f, 60f, "sd_croc_thrash_loop_a", "sd_croc_thrash_loop_b");
                    Snd("robot_legs_kick", p + Vector3.up * 0.6f, 0.6f, 5f, 50f, "sd_robot_legs_kick_a");
                    v.splashT = 0.05f;
                    break;
                case ZCrocState.Drag:
                    L16AnimalKit.StopLoop("critter_croc_thrash_loop", ref v.thrashLoop);
                    if (v.fast) break;                                                              // J15: the fast catch is under already
                    Snd("critter_croc_tail_slap", p, 0.8f, 8f, 80f, "sd_croc_tail_slap_a");
                    DragSplash(p);                                                                  // J15: a splash as it pulls the robot under (was a low boom)
                    if (fx) fx.Splash(p, 1.0f);
                    break;
                case ZCrocState.Under:
                    L16AnimalKit.StopLoop("critter_croc_wake_loop", ref v.wakeLoop); L16AnimalKit.StopLoop("critter_croc_thrash_loop", ref v.thrashLoop);
                    if ((v.c.Took || v.robot) && !v.fastDone)                                       // J15: the fast catch had its bubbles and helmet at its dive
                    {   // after a kill: the bubble boil, the helmet, the burp
                        Snd("critter_croc_bubbles", p, 0.7f, 8f, 60f, "sd_croc_bubbles_a", "sd_croc_bubbles_b");
                        Bubbles(p, 2.5f); v.ringsLeft = 2; v.ringT = 0.4f; v.ringAt = p;
                        FloatPart(v, p);
                        if (v.robot && !v.robotHidden) { v.robot.Hide(); v.robotHidden = true; }
                        v.robot = null;
                    }
                    break;
                case ZCrocState.Watch:
                    L16AnimalKit.StopLoop("critter_croc_wake_loop", ref v.wakeLoop);
                    Snd("critter_croc_miss_snort", p, 0.6f, 6f, 60f);                                  // no file yet: a named silent event (MISSING_SOUNDS.md)
                    v.ringsLeft = 3; v.ringT = 0f; v.ringAt = p;
                    break;
                case ZCrocState.Snap:
                    JawClack(p);                                                                    // J15: the clack, on the snap's frame
                    Snd("critter_croc_miss_snort", p, 0.6f, 6f, 60f);
                    if (fx) fx.Splash(p, 0.8f);
                    break;
                case ZCrocState.Dive:
                    L16AnimalKit.StopLoop("critter_croc_wake_loop", ref v.wakeLoop);
                    Snd("critter_croc_submerge", p, 0.55f, 4f, 45f, "sd_croc_submerge_b", "sd_croc_submerge_a");
                    Bubbles(p, 1.3f); if (fx && OnScreen(p)) fx.Ripple(p + Vector3.up * 0.03f, 0.6f);
                    break;
                case ZCrocState.Lurk:
                    J15Reset(v);
                    if (OnScreen(p)) Ring(fx, p + Vector3.up * 0.03f, 0.35f);
                    break;
            }
        }

        readonly List<(Vector3 at, float until)> boils = new List<(Vector3, float)>();
        void Bubbles(Vector3 at, float seconds) { boils.Add((at, Time.time + seconds)); }

        void StepBoils(float dt)
        {
            var fx = OpenZFxPlayer.I;
            for (int i = boils.Count - 1; i >= 0; i--)
            {
                if (Time.time >= boils[i].until) { boils.RemoveAt(i); continue; }
                if (!fx || !OnScreen(boils[i].at)) continue;
                if (Random.value < dt * 16f)
                {
                    var p = boils[i].at + new Vector3(Random.Range(-0.5f, 0.5f), 0.04f, Random.Range(-0.5f, 0.5f));
                    fx.EmitChannel("m12_smoke", p, Vector3.up * Random.Range(0.15f, 0.4f), Random.Range(0.35f, 0.6f), Random.Range(0.1f, 0.22f), new Color(0.9f, 0.93f, 0.85f), 0.7f);
                    if (Random.value < 0.35f) fx.Droplets(p, 0.35f, 2);
                }
            }
        }

        void Step(View v, float dt)
        {
            if (v == views[0]) StepBoils(dt);
            var c = v.c; var fx = OpenZFxPlayer.I;
            if (v.rig == null || !v.rig.ok) { if (v.robot && c.State == ZCrocState.Drag) { v.robot.Hide(); v.robot = null; } return; }
            if (v.show == 3) return;                                                                 // lent to the flyover: it is placed and posed from there
            float tState = (float)(W.Time - v.since);
            // ---- where and which way: the rule's place, or the flyover's show
            Vector3 target = World(c.X, c.Y); float yawT = Yaw(c.Heading); bool hidden = c.Hidden; float speed = (float)(c.Speed / 8.0);
            int take = tLurk; float tt = Mathf.Repeat(tState, Mathf.Max(0.1f, v.rig.Length(tLurk))); bool loop = true;
            switch (c.State)
            {
                case ZCrocState.Swim: take = c.Kind == 2 ? tNose : tPast; tt = Mathf.Repeat(Time.time, Mathf.Max(0.1f, v.rig.Length(take))); break;
                case ZCrocState.Warn: case ZCrocState.Watch: take = tNose; tt = Mathf.Repeat(tState, Mathf.Max(0.1f, v.rig.Length(tNose))); break;
                case ZCrocState.Stalk:
                    take = Mathf.Abs(v.yawRate) > 30f && tNoseL >= 0 && tNoseR >= 0 ? (v.yawRate < 0f ? tNoseL : tNoseR) : tNose;
                    tt = Mathf.Repeat(Time.time, Mathf.Max(0.1f, v.rig.Length(take))); break;
                case ZCrocState.Rise: take = tRise; tt = tState; loop = false; break;
                case ZCrocState.Lunge: case ZCrocState.Snap: take = tLunge; tt = (float)(W.Time - (c.State == ZCrocState.Snap ? c.Until - ZWorld.CrocLungeSeconds : v.since)); loop = false; break;
                case ZCrocState.Thrash: take = tThrash; tt = Mathf.Repeat(tState, Mathf.Max(0.1f, v.rig.Length(tThrash))); break;
                case ZCrocState.Drag: take = tDrag; tt = tState; loop = false; break;
                case ZCrocState.Dive: take = tDive; tt = tState; loop = false; break;
            }
            if (v.show != 0)
            {
                v.showT += dt;
                if (v.showT >= v.showLen + 1.2f) EndShow(v, true);
                else
                {
                    hidden = false; yawT = v.showYaw;
                    float k = Mathf.Clamp01(v.showT / Mathf.Max(0.01f, v.showLen));
                    target = Vector3.Lerp(v.showA, v.showB, v.show == 1 ? k : 0f); target.y = WaterAt(target.x, target.z);
                    if (v.showT > v.showLen) { take = tDive; tt = v.showT - v.showLen; loop = false; speed = 0f; }      // the dive that ends the show
                    else if (v.show == 1) { take = v.rig.Take(v.showTake); if (take < 0) take = tPast; tt = Mathf.Repeat(Time.time * 1.4f, Mathf.Max(0.1f, v.rig.Length(take))); speed = v.showSpeed; }
                    else
                    {
                        float lungeAt = v.showSnapAt - (float)ZWorld.CrocBiteSeconds; speed = 0f;
                        if (v.showT < lungeAt) { take = tNose; tt = Mathf.Repeat(v.showT, 2f); }
                        else { take = tLunge; tt = v.showT - lungeAt; loop = false; }
                        if (!v.showSnapped && v.showT >= v.showSnapAt)
                        {
                            v.showSnapped = true;
                            JawClack(target);                                                                       // J15: the clack
                            if (fx) fx.Splash(target + Quaternion.Euler(0, yawT, 0) * Vector3.forward * 1.6f, 1.2f);
                        }
                    }
                }
            }
            J15Draw(v, dt, ref take, ref tt, ref loop, ref hidden);                                   // J15: the fast dive, the roll's splashes, the passive sink
            if (!v.placed || hidden) { v.pos = target; v.yaw = yawT; v.placed = !hidden; v.yawRate = 0f; }
            else
            {
                v.pos = Vector3.Lerp(v.pos, target, 1f - Mathf.Exp(-dt * 18f)); v.pos.y = target.y;
                float ny = Mathf.LerpAngle(v.yaw, yawT, 1f - Mathf.Exp(-dt * 14f));
                v.yawRate = Mathf.Lerp(v.yawRate, dt > 1e-5f ? Mathf.DeltaAngle(v.yaw, ny) / dt : 0f, 0.2f); v.yaw = ny;
            }
            v.rig.Show(!hidden);
            v.rig.root.SetPositionAndRotation(v.pos + Vector3.down * v.drop, Quaternion.Euler(0f, v.yaw, 0f));   // J15: v.drop = under the water (sink, dive)
            if (hidden) { J15Wake(v, 0f, dt); PassLoop(v, false, 0f); LapLoop(v, false, dt); return; }   // J15: the wake card on the water
            // ---- the take, crossfaded from the one before
            if (take != v.take) { v.prev = v.take; v.prevT0 = v.takeT0; v.prevLoop = v.takeLoop; v.take = take; v.mixT = v.prev < 0 ? fade : 0f; v.takeLoop = loop; }
            v.mixT += dt;
            float wb = v.prev >= 0 ? Mathf.Clamp01(v.mixT / fade) : 1f;
            bool need = v.robot;                                                                     // the prey bone is read: pose it off screen too
            if (wb >= 1f || v.prev < 0) v.rig.Pose(take, tt, -1, 0f, 0f, need);
            else v.rig.Pose(v.prev, v.prevLoop ? Mathf.Repeat(Time.time, Mathf.Max(0.1f, v.rig.Length(v.prev))) : v.rig.Length(v.prev), take, tt, wb, need);
            v.takeT0 = tt;
            // ---- signs on the water
            bool vis = OnScreen(v.pos);
            bool onLeg = v.show == 0 ? c.State == ZCrocState.Swim : v.show == 1 && v.showT <= v.showLen;   // the slow swim of its stretch (it turns on the spot first), or of a show line
            bool swims = onLeg && speed >= wakeFrom, lurks = v.show == 0 && c.State == ZCrocState.Lurk;
            J15Wake(v, c.State == ZCrocState.Stalk || c.State == ZCrocState.Swim || v.show == 1 ? speed : 0f, dt);   // J15: no V while it sinks, is under or rises
            if (v.wakeLoop) { v.wakeLoop.transform.position = v.pos; L16AnimalKit.LoopVolume(v.wakeLoop, 0.3f + 0.25f * Mathf.Clamp01(speed / 4.5f), 0.85f + 0.3f * Mathf.Clamp01(speed / 4.5f)); }
            PassLoop(v, onLeg, speed);
            LapLoop(v, lurks && vis, dt);
            // the slow rings: behind a swimmer one where its head is every 0.6 s (they stay where they were made); round a lurking head
            // one every 1.3 s, three alive. Only where the camera looks.
            if (swims && (v.swimRingT -= dt) <= 0f) { v.swimRingT = swimRingEvery; if (vis) Ring(fx, Ahead(v, 0.7f), swimRingM / RingGrows, swimRingLife); }
            if (lurks && vis && (v.lurkRingT -= dt) <= 0f) { v.lurkRingT = lurkRingEvery; Ring(fx, Ahead(v, 0.24f), lurkRingM / RingGrows, lurkRingLife); }
            if (v.ringsLeft > 0 && (v.ringT -= dt) <= 0f) { v.ringsLeft--; v.ringT = 0.35f; if (fx && vis) fx.Ripple(v.ringAt + Vector3.up * 0.03f, 0.75f, 1.5f); }
            if (v.splashT > 0f && (v.splashT -= dt) <= 0f)
            {
                if (c.State == ZCrocState.Lunge)
                {
                    var at = v.pos + Quaternion.Euler(0, v.yaw, 0) * Vector3.forward * 1.4f;
                    Snd("critter_croc_lunge_splash", at, 0.9f, 8f, 90f, "sd_croc_lunge_splash_b", "sd_croc_lunge_splash_c", "sd_croc_lunge_splash_a");
                    if (fx) { fx.Splash(at, 1.6f); fx.Ripple(at + Vector3.up * 0.03f, 0.9f); }
                }
                else if (c.State == ZCrocState.Thrash)
                {
                    v.splashT = 0.3f;                                                                // a splash every 0.3 s of the roll
                    if (fx && vis) { fx.Splash(v.pos + Random.insideUnitSphere * 0.8f, 0.7f); fx.Ripple(v.pos + Vector3.up * 0.03f, 0.8f); }
                }
            }
            if (c.State == ZCrocState.Lurk && v.show == 0 && (v.blupT -= dt) <= 0f)
            {   // the quiet bubble of a lurker, every 6 to 10 s, with a small ring of its own: only where the camera looks
                v.blupT = 6f + ((c.Index * 37 + Time.frameCount) % 400) / 100f;
                if (vis) { Snd("critter_croc_lurk_blup", v.pos, 0.22f, 4f, 40f, "sd_croc_lurk_blup_a"); Ring(fx, v.pos + Vector3.up * 0.03f, 0.3f); }
            }
            // ---- the robot in the jaws
            if (v.robot)
            {
                v.robotT += dt;
                float k = Mathf.Clamp01(v.robotT / 0.12f);
                var tr = v.robot.transform;
                SeatRobot(v, tr, k);                                                                // J15: seated in the mouth (the takes' turn, its hips on the jaws' grip)
                if (c.State == ZCrocState.Drag && tState >= 25f / 30f && !v.robotHidden) { v.robot.Hide(); v.robotHidden = true; v.robot = null; }   // under with the animal (frame 25 of the take)
            }
        }

        void EndShow(View v, bool done)
        {
            if (v.show == 0) return;
            v.show = 0; v.placed = false;
            if (!done && OpenZFxPlayer.I) { Bubbles(v.pos, 1.0f); OpenZFxPlayer.I.Ripple(v.pos + Vector3.up * 0.03f, 0.5f); }
        }

        /// <summary>A quiet ring of the animal (round a lurking head, behind a slow swimmer, the lurker's bubble, round the floating
        /// helmet): the game's ring on the channel whose glow follows the night (OpenZFxPlayer.RippleCalm): after dark it has the
        /// water's own tone, a little lighter, not the day's light blue. The rings of the attack stay the game's splash rings.</summary>
        static void Ring(OpenZFxPlayer fx, Vector3 at, float s, float life = OpenZFxCatalog.RippleLife) { if (fx) fx.RippleCalm(at, s, life); }

        /// <summary>A place on the water ahead of the animal's middle (m): where its head is out of the water (the takes as measured:
        /// the eye hoods of the lurk 0.05 to 0.43 m ahead, the part out of the water in the two slow swims centred 0.65 to 0.72 m ahead).</summary>
        Vector3 Ahead(View v, float m) => v.pos + Quaternion.Euler(0f, v.yaw, 0f) * Vector3.forward * m + Vector3.up * 0.03f;

        /// <summary>The quiet ripple of a slow swimmer (the design's croc_pass_ripple: the prepared wake loop, quieter than the stalk's):
        /// one loop of its own while the animal swims its stretch or a show line, moved with it; one line of the sound log when it
        /// starts and one when it stops.</summary>
        void PassLoop(View v, bool on, float speed)
        {
            if (!on) { L16AnimalKit.StopLoop("critter_croc_pass_ripple", ref v.passLoop); v.passAsked = false; return; }
            if (!v.passLoop && !v.passAsked && sounds) { v.passAsked = true; v.passLoop = L16AnimalKit.Loop("critter_croc_pass_ripple", transform, v.pos, 0.3f, 6f, 60f, "sd_croc_wake_loop_a", "sd_croc_wake_loop_b"); }
            if (v.passLoop) { v.passLoop.transform.position = v.pos; L16AnimalKit.LoopVolume(v.passLoop, 0.22f + 0.25f * Mathf.Clamp01(speed / 4.5f), 0.85f + 0.3f * Mathf.Clamp01(speed / 4.5f)); }
        }

        /// <summary>The water lapping at a lurking head while its slow rings show (the water design asks for one quiet loop near the
        /// animal; it was never made: a named event that stays silent until sd_critter_croc_lurk_lap_a.wav exists, see the kit's
        /// MISSING_SOUNDS.md). Asked for once each time a lurking animal comes into the camera's view; let go when it has been out of
        /// view for 3 s or does something else.</summary>
        void LapLoop(View v, bool on, float dt)
        {
            if (on)
            {
                v.lapOffT = 0f;
                if (!v.lapLoop && !v.lapAsked && sounds) { v.lapAsked = true; v.lapLoop = L16AnimalKit.Loop("critter_croc_lurk_lap", transform, v.pos, 0.2f, 4f, 40f, "sd_critter_croc_lurk_lap_a", "sd_critter_croc_lurk_lap_b"); }
                if (v.lapLoop) v.lapLoop.transform.position = v.pos;
                return;
            }
            if (!v.lapAsked) return;
            if (v.show == 0 && v.c.State == ZCrocState.Lurk && (v.lapOffT += dt) < 3f) return;       // only just out of view: it goes on
            L16AnimalKit.StopLoop("critter_croc_lurk_lap", ref v.lapLoop); v.lapAsked = false;
        }

        static readonly List<Vector3> wakeV = new List<Vector3>(); static readonly List<Vector2> wakeUv = new List<Vector2>(); static readonly List<Color32> wakeC = new List<Color32>();

        /// <summary>One wake card per animal: a strip of WakeRows rows across the whole width of the picture; <see cref="CutWake"/> lays
        /// its rows from the point back to the length it is cut to.</summary>
        static Mesh NewWakeMesh(int index)
        {
            var m = new Mesh { name = "CrocWake" + index }; m.MarkDynamic();
            wakeV.Clear(); wakeUv.Clear(); wakeC.Clear();
            for (int i = 0; i < (WakeRows + 1) * 2; i++) { wakeV.Add(Vector3.zero); wakeUv.Add(Vector2.zero); wakeC.Add(new Color32(255, 255, 255, 0)); }
            var tris = new int[WakeRows * 6];
            for (int r = 0; r < WakeRows; r++) { int a = r * 2, t = r * 6; tris[t] = a; tris[t + 1] = a + 1; tris[t + 2] = a + 2; tris[t + 3] = a + 1; tris[t + 4] = a + 3; tris[t + 5] = a + 2; }
            m.SetVertices(wakeV); m.SetUVs(0, wakeUv); m.SetColors(wakeC); m.SetTriangles(tris, 0);
            m.bounds = new Bounds(new Vector3(0f, 0f, -WakeLong * 0.5f), new Vector3(WakeWide, 0.2f, WakeLong));
            return m;
        }

        /// <summary>The card from its point (the picture's top) back to `len` metres: the picture keeps its size on the water (a row
        /// `p` metres behind the point shows the picture's row p / 8), so a shorter wake is the same V cut off, not a smaller one. In
        /// the vertex colours: it begins behind the part of the animal that is out of the water (`outLen` metres from the snout) and
        /// its last half fades out.</summary>
        void CutWake(View v, float len, float outLen)
        {
            if (Mathf.Abs(len - v.wakeCut) < 0.01f && Mathf.Abs(outLen - v.wakeCutOut) < 0.01f) return;
            v.wakeCut = len; v.wakeCutOut = outLen;
            wakeV.Clear(); wakeUv.Clear(); wakeC.Clear();
            for (int r = 0; r <= WakeRows; r++)
            {
                float p = len * r / WakeRows;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(len, len * 0.5f, p)) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f * outLen, outLen + 0.3f, p));
                var col = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * a));
                for (int side = 0; side < 2; side++)
                {
                    wakeV.Add(new Vector3((side - 0.5f) * WakeWide, 0f, -p));
                    wakeUv.Add(new Vector2(side, 1f - p / WakeLong));
                    wakeC.Add(col);
                }
            }
            v.wakeMesh.SetVertices(wakeV); v.wakeMesh.SetUVs(0, wakeUv); v.wakeMesh.SetColors(wakeC);
        }

        bool WakeWater(Vector3 p) => W.CrocFreeAt(p.x * 8.0, -p.z * 8.0) >= -(ZWorld.CrocWobbleM - 0.2);   // the animal's own water, to 0.2 m before its shore

        /// <summary>The V behind the animal: the prepared wake picture lying flat on the water, its point 0.09 m behind the snout tip,
        /// CUT to the length for this speed (see the fields) and never stretched, so its soft bands keep their width at every speed.
        /// Never drawn past the water: the length is also cut 0.5 m before the first place where its middle line or one of its two
        /// arms would leave the animal's own water. Strength from the speed. Without the wake material: rings left and right.</summary>
        void Wake(View v, float speed, float dt)
        {
            bool on = speed >= wakeFrom;
            float size = Mathf.Max(0.25f, wakeSize);                                                     // the picture's size on the water (wider and longer together)
            if (on)
            {   // measured on the takes: the snout tip is 1.72 m ahead of the middle with head and back out (2.0 m of the animal out of
                // the water), 1.44 m in the nose-only takes (1.45 m from the nostrils to the eye bumps)
                bool headOut = v.take == tPast && tPast >= 0;
                v.wakeApex = headOut ? 1.63f : 1.35f; v.wakeOut = headOut ? 2.0f : 1.45f;
                float want = Mathf.Min(wakeMax * size, v.wakeOut + (wakeBase + wakePerMps * speed) * size);
                v.wakeLen = Mathf.Lerp(v.wakeLen <= 0f ? want : v.wakeLen, want, 1f - Mathf.Exp(-dt * 4f));
                v.wakeNow = Mathf.Min(wakeStrengthMax, wakeStrength + wakeStrengthPerMps * speed);
            }
            v.wakeAlpha = Mathf.MoveTowards(v.wakeAlpha, on ? 1f : 0f, dt / Mathf.Max(0.01f, on ? wakeFadeIn : wakeFade));
            if (v.wakeAlpha <= 0.001f) { if (v.wakeR) v.wakeR.enabled = false; v.wakeLen = 0f; return; }
            var turn = Quaternion.Euler(0f, v.yaw, 0f); Vector3 back = turn * Vector3.back, right = turn * Vector3.right, tip = v.pos + turn * Vector3.forward * v.wakeApex;
            float len = v.wakeLen;
            for (float s = 0.5f; s < len; s += 0.5f)
            {
                var mid = tip + back * s; var arm = right * (s * WakeArm);
                if (!WakeWater(mid) || !WakeWater(mid + arm) || !WakeWater(mid - arm)) { len = Mathf.Max(1f, s - 0.5f); break; }
            }
            if (v.wakeR)
            {
                v.wakeR.enabled = true;
                v.wakeTr.localPosition = new Vector3(0f, OpenZFxCatalog.RippleLift * 0.5f, v.wakeApex);
                v.wakeTr.localRotation = Quaternion.identity; v.wakeTr.localScale = new Vector3(size, 1f, size);   // the arms keep their angle
                CutWake(v, len / size, v.wakeOut / size);
                float dark = Mathf.Lerp(1f, 0.45f, DetailKit.Night);                                    // the picture is unlit: it goes dark with the scenery
                var col = new Color(dark, dark, dark, Mathf.Clamp01(v.wakeNow * wakeGain * v.wakeAlpha));
                v.wakeR.GetPropertyBlock(mpb); mpb.SetColor(BaseColorId, col); mpb.SetColor(ColorId, col); v.wakeR.SetPropertyBlock(mpb);
            }
            else if (OpenZFxPlayer.I && OnScreen(v.pos) && (v.wakeRingT -= dt) <= 0f)
            {
                v.wakeRingT = 0.22f;
                var side = right * ((Time.frameCount & 1) == 0 ? 0.5f : -0.5f);
                OpenZFxPlayer.I.Ripple(v.pos + back * 0.3f + side + Vector3.up * 0.03f, 0.3f, 1.1f);
            }
        }

        // ------------------------------------------------------------------ what floats up afterwards
        /// <summary>The robot's helmet (its own baked head part with its own team colour), made ready at the bite while the robot is
        /// still there to copy the colour from; it stays out of sight until the animal is under.</summary>
        void PrepareHelmet(View v, UnitView robot)
        {
            if (v.helmet) Destroy(v.helmet.gameObject);
            v.helmet = null;
            var ps = RobotPopDeath.PartsOf(robot.Obj.Type);
            SkinnedMeshRenderer body = null;
            foreach (var sk in robot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (sk.sharedMesh && (!body || sk.sharedMesh.vertexCount > body.sharedMesh.vertexCount)) body = sk;
            int head = -1;
            if (ps != null) for (int i = 0; i < ps.bone.Length; i++) if (ps.bone[i] != null && ps.bone[i].EndsWith(FloatingPart)) { head = i; break; }
            if (!body || head < 0) return;
            Transform bone = null; foreach (var b in body.bones) if (b && b.name == ps.bone[head]) { bone = b; break; }
            var go = new GameObject("CrocFloat_Helmet"); go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = ps.mesh[head];
            // the whole head piece in the squad's bright team colour (the robot's own head is mostly grey metal: from the default
            // camera heights that read as a lump, not as a helmet)
            var mr = go.AddComponent<MeshRenderer>(); var mats = new Material[body.sharedMaterials.Length];
            var paint = HelmetPaint(TeamColors.Shown(robot.Obj.Owner) == Team.Red);
            for (int m = 0; m < mats.Length; m++) mats[m] = paint;
            mr.sharedMaterials = mats; mr.shadowCastingMode = ShadowCastingMode.Off;
            go.transform.localScale = (bone ? bone.lossyScale : Vector3.one) * helmetScale;
            go.SetActive(false);
            v.helmet = go.transform;
        }

        static Material helmetRed, helmetBlue;
        /// <summary>Bright toy paint for the floating helmet: red or blue, a little glow of its own so it reads in shade and at night.</summary>
        static Material HelmetPaint(bool red)
        {
            if (red ? helmetRed : helmetBlue) return red ? helmetRed : helmetBlue;
            var c = red ? new Color(0.92f, 0.10f, 0.07f) : new Color(0.10f, 0.36f, 0.95f);
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = red ? "OzCrocHelmetRed" : "OzCrocHelmetBlue" };
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", 0.55f); m.SetFloat("_Metallic", 0f);
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * 0.22f); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            if (red) helmetRed = m; else helmetBlue = m;
            return m;
        }

        /// <summary>The one thing left: the helmet pops up where the animal went under, bobs for 12 s and sinks. Slapstick, with a plop
        /// and, a second later, a burp.</summary>
        void FloatPart(View v, Vector3 at)
        {
            var f = new Floater { t0 = Time.time + 2.0f, life = 12f, at = at + Quaternion.Euler(0, v.yaw, 0) * new Vector3(0.4f, 0f, 1.0f), phase = v.c.Index * 1.3f, t = v.helmet };
            v.helmet = null;
            floaters.Add(f);
        }

        bool StepFloater(Floater f)
        {
            float t = Time.time - f.t0;
            if (t < 0f) return true;
            var fx = OpenZFxPlayer.I;
            float y = WaterAt(f.at.x, f.at.z);
            if (!f.plopped)
            {
                f.plopped = true; PartsFloated++;
                if (f.t) f.t.gameObject.SetActive(true);
                Snd("critter_croc_part_float", f.at, 0.8f, 6f, 60f, "sd_croc_part_float_a", "sd_croc_part_float_b");
                if (fx) { fx.Droplets(new Vector3(f.at.x, y, f.at.z), 0.6f, 6); fx.Ripple(new Vector3(f.at.x, y + 0.03f, f.at.z), 0.5f); }
            }
            if (!f.burped && t >= 1.0f)
            {
                f.burped = true;
                Snd("critter_croc_burp", f.at, 0.7f, 6f, 60f);                                          // no file yet: a named silent event (MISSING_SOUNDS.md)
                Bubbles(f.at + new Vector3(0.6f, 0f, 0.3f), 0.5f);
            }
            if (t >= f.life) { if (f.t) Destroy(f.t.gameObject); return false; }
            if (f.t)
            {
                float up = Mathf.Clamp01(t / 0.35f), down = Mathf.Clamp01((f.life - t) / 1.5f);          // pops up, bobs, sinks at the end
                float bob = Mathf.Sin(t * 2.1f + f.phase) * helmetBob + (up < 1f ? Mathf.Sin(up * Mathf.PI) * 0.25f : 0f);
                f.t.position = new Vector3(f.at.x + Mathf.Sin(t * 0.3f + f.phase) * 0.15f, y - 0.12f * helmetScale + bob - ((1f - down) * 0.9f + (1f - up) * 0.8f) * helmetScale, f.at.z + Mathf.Cos(t * 0.27f + f.phase) * 0.15f);
                f.t.rotation = Quaternion.Euler(Mathf.Sin(t * 1.3f + f.phase) * 12f - 90f, t * 14f + f.phase * 40f, Mathf.Cos(t * 1.1f) * 10f);
                if (t > 0.5f && t < f.life - 1.5f && Mathf.FloorToInt(t / 1.4f) != Mathf.FloorToInt((t - Time.deltaTime) / 1.4f) && OnScreen(f.at)) Ring(fx, new Vector3(f.t.position.x, y + 0.03f, f.t.position.z), helmetRing, 1.8f);   // its ring, as long as it floats
            }
            return true;
        }

        // ------------------------------------------------------------------ proof (Editor/OzL16AnimalsCheck.cs samples this every 0.5 s)
        /// <summary>One sample of every drawn animal against the DRAWN bank: 8 points of its outline and, round each, a ring of 0.6 m:
        /// every one must lie over drawn water (the ground under it lower than the pool's surface). Counts samples and the ones that
        /// do not; LeastDrawnBank = the smallest ring that was still all water, in steps of 0.1 m up to 1.5 m.</summary>
        public static void SampleBanks()
        {
            if (!I || I.W == null) return;
            foreach (var v in I.views)
            {
                if (v.rig == null || !v.rig.Shown || v.c.Hidden) continue;
                BankSamples++;
                var s = OzCrocSpace.Of(v.c);
                var fwd = Quaternion.Euler(0, v.yaw, 0) * Vector3.forward; var right = Quaternion.Euler(0, v.yaw, 0) * Vector3.right;
                float least = 1.5f;
                for (int k = 0; k < 8; k++)
                {
                    float a = k == 0 ? s.ahead : k == 1 ? -s.behind : k < 4 ? 0f : (k < 6 ? s.ahead : -s.behind) * 0.7f;
                    float sd = k < 2 ? 0f : k == 2 ? s.half : k == 3 ? -s.half : ((k & 1) == 0 ? s.half : -s.half) * 0.7f;
                    var p = v.pos + fwd * a + right * sd;
                    for (float r = 0f; r <= 1.5f; r += 0.1f)
                    {
                        bool water = true;
                        for (int q = 0; q < 8 && water; q++)
                        {
                            float ang = q * Mathf.PI / 4f; float x = p.x + Mathf.Cos(ang) * r, z = p.z + Mathf.Sin(ang) * r;
                            float wy = OzWaterLevel.At(I.game.level, x, z, float.NegativeInfinity);
                            water = !float.IsNegativeInfinity(wy) && I.game.GroundY(x, z) < wy - 0.02f;
                        }
                        if (!water) { least = Mathf.Min(least, r - 0.1f); break; }
                    }
                }
                if (least < LeastDrawnBank) LeastDrawnBank = least;
                if (least < 0.6f - 1e-4f) BankNear++;
            }
        }

        /// <summary>One line per animal: what it does and where (for the checks and for whoever looks).</summary>
        public static string Status()
        {
            if (!I) return "CrocodileView not running (not level 16, or the rule is off)";
            var sb = new System.Text.StringBuilder();
            foreach (var v in I.views) sb.Append($"croc {v.c.Index} pool {v.c.Pool} {v.c.State} at {v.pos.x:0.0}, {v.pos.z:0.0} yaw {v.yaw:0} {(v.show != 0 ? "SHOW " : "")}{(v.rig != null && v.rig.Shown ? "drawn" : "hidden")}\n");
            return sb.ToString();
        }
    }

    /// <summary>The space each take needs round the animal's middle (m), measured on the 3.3 m model; with the robot in the jaws for
    /// the bite, the roll and the drag under (the same table as Editor/OzL16CrocCount.cs).</summary>
    public struct OzCrocSpace
    {
        public float ahead, behind, half;
        public static OzCrocSpace Of(ZCroc c)
        {
            switch (c.State)
            {
                case ZCrocState.Lunge: return c.Took ? new OzCrocSpace { ahead = 3.57f, behind = 1.80f, half = 1.65f } : new OzCrocSpace { ahead = 2.21f, behind = 1.80f, half = 1.45f };
                case ZCrocState.Snap: return new OzCrocSpace { ahead = 2.21f, behind = 1.80f, half = 1.45f };
                case ZCrocState.Thrash: return new OzCrocSpace { ahead = 3.24f, behind = 1.34f, half = 1.82f };
                case ZCrocState.Drag: return new OzCrocSpace { ahead = 3.22f, behind = 1.36f, half = 1.45f };
                case ZCrocState.Rise: return new OzCrocSpace { ahead = 1.58f, behind = 1.66f, half = 1.34f };
                case ZCrocState.Dive: return new OzCrocSpace { ahead = 1.76f, behind = 1.66f, half = 1.33f };
                case ZCrocState.Swim: return new OzCrocSpace { ahead = 1.72f, behind = 1.53f, half = 1.36f };
                default: return new OzCrocSpace { ahead = 1.65f, behind = 1.66f, half = 1.35f };
            }
        }
    }
}
