using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// Level 16 crocodiles, second round (view only: the rule of Sim/ZWorld.Crocodiles.cs and its timing are untouched).
    ///   THE JAW CLACK: every closing of the jaws (the bite, the snap at the air after a miss, the flyover's snap) plays the event
    ///   critter_croc_jaw_snap on that frame: a short wet bony clack, one of <see cref="JawClackFiles"/> at random, never the same
    ///   twice in a row. Others (the mission-start window) call <see cref="JawClack"/>, the crocodile's own jaw event, or play the
    ///   same list under its other name <see cref="JawSnapFiles"/>.
    ///   THE PASSIVE SINK: a crocodile that is not hunting (lurking, or swimming its stretch) sinks smoothly out of sight when a
    ///   robot or vehicle comes near (its distance less than SinkTouchM + SinkLeadM + its speed x (SinkSeconds + 0.25 s)), stays
    ///   under while anything is near, and comes up again RiseHoldSeconds after the last one has gone (a soft ring and a quiet
    ///   sound when it goes and when it comes, where the camera looks). No robot walks through a drawn crocodile. A hunt brings it
    ///   up at once (0.4 s). While it sinks, is under or comes up it draws no V wake (the V fades from the sink's first frame), and the
    ///   wake card stays on the water while it fades.
    ///   TWO CATCHES: at each bite one of two is drawn, at random, never the same three times running: the slow death roll (as
    ///   before, now with a splash sound on the roll's three water frames per turn and a splash, not the old low drag-under sound, when
    ///   it pulls the robot under) or the FAST catch: 0.25 s after the bite the
    ///   animal dives straight down with the robot (the drag-under take at 1.5 x and 1.6 m straight down in 0.9 s, a dive splash on
    ///   the plunge frame), bubbles, the helmet; it stays under for the rest of the rule's roll and drag.
    ///   THE ROBOT IN THE JAWS: seated as the takes were made: the robot turned as the prey socket turns (upright and facing the
    ///   animal in the lunge's first frame) and its own grip point (its hips bone, measured on every robot type in every frame)
    ///   on the jaws' grip point (the takes' pelvis point, 1.2 m up the robot's axis from the socket).
    /// Counted checks: the statics of <see cref="OzCrocJ15Stats"/> (Editor/OzCrocJ15Check.cs reads them).
    /// </summary>
    public sealed partial class CrocodileView
    {
        // ------------------------------------------------------------------ numbers (statics: tune in Play)
        public static bool PassiveSink = true;
        /// <summary>The animal's own room (m from its middle: its outline 1.72 m ahead plus a robot's half width and a margin) in which
        /// no robot may meet a drawn crocodile; the lead before it; the sink and the rise (s), the depth it goes to, the quiet time
        /// before it comes up and the extra distance everything must be away for that.</summary>
        public static float SinkTouchM = 2.6f, SinkLeadM = 1.5f, SinkSeconds = 0.8f, RiseSeconds = 1.2f, HuntRiseSeconds = 0.4f, SinkDepthM = 1.5f, RiseHoldSeconds = 1.2f, RiseClearM = 2.0f;
        /// <summary>The catch drawn at a bite: 0 at random, 1 always the slow roll, 2 always the fast dive (the checks).</summary>
        public static int ForceCatch;
        public static float FastShare = 0.5f; public static int SameCatchMax = 2;
        public static float FastDiveAfterBite = 0.25f, FastDiveSeconds = 0.9f, FastDiveSpeed = 1.5f, FastDiveDropM = 1.6f, FastDiveSplashAt = 0.2f;
        /// <summary>The takes' pelvis point: 1.2 m up the robot's axis from the prey socket (the robot's feet).</summary>
        public static float GripDesignM = 1.2f;
        /// <summary>The lunge's roar (was 0.95): the loudest sound of the attack (5.6 LU under full scale, half its energy under
        /// 300 Hz, a 13 ms attack: the most explosion-like); lowered so that the clack is the sound of the bite.</summary>
        public static float LungeRoarVol = 0.35f;
        public static float JawClackVol = 0.9f, RollSplashVol = 0.85f, RollSplashSoftVol = 0.5f, DiveSplashVol = 0.9f, DragSplashVol = 0.9f, SinkSoundVol = 0.22f, RiseSoundVol = 0.25f;
        /// <summary>The roll's water frames in its take (thrash_roll, 1.2 s a turn): f8 and f28 the body slaps down, f17 it rolls over
        /// with the robot (the deepest point of the animal's top).</summary>
        public static readonly float[] RollSplashAt = { 8f / 30f, 17f / 30f, 28f / 30f };
        public static readonly bool[] RollSplashBig = { false, true, false };

        public static readonly string[] JawClackFiles = { "sd_croc_jaw_snap_a", "sd_croc_jaw_snap_b", "sd_croc_jaw_snap_c", "sd_croc_jaw_snap_d", "sd_croc_jaw_snap_e" };
        /// <summary>The same list under the name the mission-start window looks up (by reflection, as a static FIELD: keep it a field).</summary>
        public static readonly string[] JawSnapFiles = JawClackFiles;
        public static readonly string[] RollSplashFiles = { "sd_croc_roll_splash_a", "sd_croc_roll_splash_b", "sd_croc_roll_splash_c", "sd_croc_roll_splash_d" };
        public static readonly string[] DiveSplashFiles = { "sd_croc_dive_splash_a", "sd_croc_dive_splash_b", "sd_croc_dive_splash_c" };

        sealed partial class View
        {
            public float sink, drop, nearT; public bool sinkGone;
            public bool fast, fastDone, diveSplashed; public int catchFor = -1; public double diveAt = -1;
            public float rollPrev = -1f;
            public Transform grip, jawBone; public Quaternion robotRot0; public float gripH; public string robotType;
        }

        // ------------------------------------------------------------------ random pick, never the same twice running
        static readonly Dictionary<string, int> lastPick = new Dictionary<string, int>();
        static readonly List<string> pickable = new List<string>();
        static string PickFile(string key, string[] files)
        {
            pickable.Clear();
            foreach (var f in files) if (L16AnimalKit.Clip(f)) pickable.Add(f);
            if (pickable.Count == 0) return files.Length > 0 ? files[0] : null;                         // none there: the event logs its "DROP clip missing"
            lastPick.TryGetValue(key, out int last);
            int n = pickable.Count, i = Random.Range(0, n);
            if (n > 1 && i == last) i = (i + 1 + Random.Range(0, n - 1)) % n;
            lastPick[key] = i;
            return pickable[i];
        }

        /// <summary>THE CROCODILE'S OWN JAW EVENT: a wet bony clack at `at` (call it on the frame the jaws close). Silent with the
        /// view's sounds off.</summary>
        public static void JawClack(Vector3 at, float vol = -1f)
        {
            if (I && !I.sounds) return;
            OzCrocJ15Stats.Clacks++;
            L16AnimalKit.Play("critter_croc_jaw_snap", at, vol < 0f ? JawClackVol : vol, 8f, 80f, 1f, PickFile("jaw", JawClackFiles));
        }

        /// <summary>OnNote, the slow roll's Drag (the animal pulls the robot under): a splash of the dive takes on that frame. It was the
        /// low drag-under sound (two thirds of it under 150 Hz: it read as a boom).</summary>
        void DragSplash(Vector3 p)
        {
            OzCrocJ15Stats.DragSplashes++;
            Snd("critter_croc_drag_splash", p, DragSplashVol, 8f, 80f, PickFile("dive", DiveSplashFiles));
        }

        // ------------------------------------------------------------------ the design's seat of the robot (measured once)
        Quaternion preyToRobot = Quaternion.identity; bool seatReady; float crocScale = 1f;

        /// <summary>CrocodileView.Start: the prey socket's turn in the lunge's first frame, where the takes have the robot upright and
        /// facing the animal: from it, the robot's turn on the socket in every frame.</summary>
        void J15Start(View v0)
        {
            seatReady = false;
            if (v0 == null || v0.rig == null || !v0.rig.ok || !v0.prey || tLunge < 0) { Debug.LogWarning("[OpenZ] J15: no prey socket or lunge take: the robot keeps its own turn in the jaws"); return; }
            v0.rig.Pose(tLunge, 0f, -1, 0f, 0f, true);
            var rel = Quaternion.Inverse(v0.rig.root.rotation) * v0.prey.rotation;                         // the socket in the animal's own space
            preyToRobot = Quaternion.Inverse(rel) * Quaternion.Euler(0f, 180f, 0f);                         // upright, facing the animal (its nose is +Z)
            crocScale = Mathf.Abs(v0.rig.root.lossyScale.y);
            seatReady = true;
            foreach (var v in views) if (v.rig != null && v.rig.ok) v.jawBone = L16AnimalKit.Find(v.rig.model, "jaw");
            if (!subscribed) { subscribed = true; RenderPipelineManager.beginContextRendering += SampleGrip; }
            Debug.Log($"[OpenZ] J15 crocodiles: robot seat from the prey socket (lunge frame 0), jaw grip {GripDesignM * crocScale:0.00} m up the robot's axis; passive sink {(PassiveSink ? "on" : "off")}");
        }

        /// <summary>CrocodileView.Take: a robot is in the jaws: its grip point (hips bone) and its turn before the bite.</summary>
        void J15Take(View v, UnitView robot)
        {
            v.robotRot0 = robot.transform.rotation;
            v.robotType = robot.Obj != null ? robot.Obj.Type : "?";
            v.grip = null;
            var anim = robot.GetComponentInChildren<Animator>();
            if (anim && anim.isHuman) v.grip = anim.GetBoneTransform(HumanBodyBones.Hips);
            if (!v.grip) foreach (var t in robot.GetComponentsInChildren<Transform>(true)) if (t.name.EndsWith("Hips")) { v.grip = t; break; }
            v.gripH = v.grip ? Vector3.Dot(v.grip.position - robot.transform.position, robot.transform.up) : 1.0f;
            OzCrocJ15Stats.GripHeight[v.robotType] = v.gripH;
            PickCatch(v, robot.Obj != null ? robot.Obj.Id : -2);
            Debug.Log($"[OpenZ] J15 grip: {v.robotType} #{(robot.Obj != null ? robot.Obj.Id : -1)} hips {(v.grip ? v.grip.name : "NOT FOUND (its middle 1.0 m up)")} {v.gripH:0.00} m above its feet; catch {(v.fast ? "FAST dive" : "slow roll")}");
        }

        static int lastCatch, catchRun;
        /// <summary>The catch of this bite, once per robot taken.</summary>
        void PickCatch(View v, int robotId)
        {
            if (v.catchFor == robotId) return;
            v.catchFor = robotId; v.fastDone = false; v.diveSplashed = false; v.rollPrev = -1f;
            bool fast = ForceCatch == 2 || (ForceCatch == 0 && Random.value < FastShare);
            int kind = fast ? 2 : 1;
            if (ForceCatch == 0 && kind == lastCatch && catchRun >= SameCatchMax) { fast = !fast; kind = fast ? 2 : 1; }
            catchRun = kind == lastCatch ? catchRun + 1 : 1; lastCatch = kind;
            v.fast = fast;
            var c = v.c;
            double biteAt = c.State == ZCrocState.Lunge ? c.Since + ZWorld.CrocBiteSeconds : W.Time;
            v.diveAt = fast ? biteAt + FastDiveAfterBite : -1;
            if (fast) OzCrocJ15Stats.FastCatches++; else OzCrocJ15Stats.RollCatches++;
            string key = (v.robotType ?? "?") + (fast ? " fast" : " roll");
            OzCrocJ15Stats.CatchesBy.TryGetValue(key, out int n); OzCrocJ15Stats.CatchesBy[key] = n + 1;
        }

        /// <summary>OnNote, the bite: the robot's type from the rule, then the catch (Take may come before or after this note).</summary>
        void J15Bite(View v, ZCrocNote n)
        {
            var o = W.Get(n.robot); if (o != null) v.robotType = o.Type;
            PickCatch(v, n.robot);
        }

        void J15Reset(View v) { v.fast = false; v.fastDone = false; v.diveSplashed = false; v.catchFor = -1; v.diveAt = -1; v.rollPrev = -1f; }

        // ------------------------------------------------------------------ every frame
        struct Mover { public int id; public Vector3 at; public float speed; }
        readonly List<Mover> movers = new List<Mover>();
        readonly Dictionary<int, Vector3> moverWas = new Dictionary<int, Vector3>();
        readonly HashSet<int> moverSeen = new HashSet<int>();
        static bool subscribed;

        /// <summary>LateUpdate, before the animals: the robots and vehicles of the match (where, how fast).</summary>
        void J15Before(float dt)
        {
            movers.Clear(); moverSeen.Clear();
            if (!PassiveSink || W == null) return;
            var objs = W.Objects;
            for (int i = 0; i < objs.Count; i++)
            {
                var o = objs[i];
                if (o == null || (o.Kind != ZKind.Robot && o.Kind != ZKind.Vehicle) || o.Destroyed || o.Eaten) continue;
                var p = new Vector3(o.CenterX / 8f, 0f, -o.CenterY / 8f);
                float spd = 0f;
                if (moverWas.TryGetValue(o.Id, out var was) && dt > 1e-5f) spd = Mathf.Min(30f, Vector3.Distance(was, p) / dt);
                moverWas[o.Id] = p; moverSeen.Add(o.Id);
                movers.Add(new Mover { id = o.Id, at = p, speed = spd });
            }
            if (moverWas.Count > movers.Count + 64) { var gone = new List<int>(); foreach (var k in moverWas.Keys) if (!moverSeen.Contains(k)) gone.Add(k); foreach (var k in gone) moverWas.Remove(k); }
        }

        /// <summary>Step, before the animal is placed: the fast catch's dive, the roll's splashes, the passive sink. May hide the animal
        /// (hidden) and sets how far its drawn root goes under the water (v.drop).</summary>
        void J15Draw(View v, float dt, ref int take, ref float tt, ref bool loop, ref bool hidden)
        {
            var c = v.c; v.drop = 0f;
            if (v.show != 0) { v.sink = 0f; v.nearT = 0f; return; }
            // ---- the fast catch: straight down with the robot
            if (v.fast && c.Busy)
            {
                if (v.fastDone) { hidden = true; return; }
                if (v.diveAt >= 0 && W.Time >= v.diveAt && tDrag >= 0)
                {
                    float d = (float)(W.Time - v.diveAt);
                    take = tDrag; tt = d * FastDiveSpeed; loop = false;
                    v.drop = FastDiveDropM * Mathf.SmoothStep(0f, 1f, d / FastDiveSeconds);
                    if (!v.diveSplashed && d >= FastDiveSplashAt)
                    {
                        v.diveSplashed = true; OzCrocJ15Stats.DiveSplashes++;
                        var at = v.pos + Quaternion.Euler(0, v.yaw, 0) * Vector3.forward * 1.2f;
                        Snd("critter_croc_dive_splash", at, DiveSplashVol, 8f, 80f, PickFile("dive", DiveSplashFiles));
                        var fx = OpenZFxPlayer.I; if (fx && OnScreen(at)) { fx.Splash(at, 1.1f); fx.Ripple(at + Vector3.up * 0.03f, 0.8f); }
                        Bubbles(at, 1.2f);
                    }
                    if (d >= FastDiveSeconds) { FinishFastDive(v); hidden = true; }
                }
                return;
            }
            // ---- the slow roll: a splash on each of its water frames
            if (c.State == ZCrocState.Thrash && !v.fast && tThrash >= 0)
            {
                float len = Mathf.Max(0.1f, v.rig.Length(tThrash)), ph = Mathf.Repeat((float)(W.Time - v.since), len);
                if (v.rollPrev >= 0f)
                    for (int i = 0; i < RollSplashAt.Length; i++)
                    {
                        float m = RollSplashAt[i] * len / 1.2f;
                        bool crossed = v.rollPrev <= ph ? (m > v.rollPrev && m <= ph) : (m > v.rollPrev || m <= ph);
                        if (!crossed) continue;
                        OzCrocJ15Stats.RollSplashes++;
                        var at = v.prey ? new Vector3(v.prey.position.x, v.pos.y, v.prey.position.z) : v.pos;
                        Snd("critter_croc_roll_splash", at, RollSplashBig[i] ? RollSplashVol : RollSplashSoftVol, 8f, 80f, PickFile("roll", RollSplashFiles));
                        var fx = OpenZFxPlayer.I; if (fx && OnScreen(at)) { fx.Splash(at, RollSplashBig[i] ? 1.0f : 0.6f); if (RollSplashBig[i]) fx.Droplets(at + Vector3.up * 0.3f, 0.8f, 8); }
                    }
                v.rollPrev = ph;
            }
            // ---- the passive sink
            if (!PassiveSink) { v.sink = 0f; return; }
            if (c.Hidden) { v.sink = 1f; v.nearT = RiseHoldSeconds; v.sinkGone = true; return; }        // under by the rule: it comes back sunk and rises when clear
            if (c.Busy) { v.sink = Mathf.MoveTowards(v.sink, 0f, dt / HuntRiseSeconds); v.drop = SinkDepthM * Mathf.SmoothStep(0f, 1f, v.sink); v.sinkGone = false; return; }
            var me = World(c.X, c.Y);
            bool near = false;
            for (int i = 0; i < movers.Count && !near; i++)
            {
                var m = movers[i]; float dx = m.at.x - me.x, dz = m.at.z - me.z;
                float lead = SinkTouchM + SinkLeadM + m.speed * (SinkSeconds + 0.25f) + (v.sink > 0f ? RiseClearM : 0f);
                near = dx * dx + dz * dz < lead * lead;
            }
            if (near) v.nearT = RiseHoldSeconds; else v.nearT -= dt;
            float want = v.nearT > 0f ? 1f : 0f, before = v.sink;
            v.sink = Mathf.MoveTowards(v.sink, want, dt / Mathf.Max(0.05f, want > v.sink ? SinkSeconds : RiseSeconds));
            var fxp = OpenZFxPlayer.I; var head = me + Quaternion.Euler(0f, Yaw(c.Heading), 0f) * Vector3.forward * 0.4f;
            if (before <= 0f && v.sink > 0f)
            {   // it goes: a soft ring and a quiet gulp where the camera looks
                OzCrocJ15Stats.Sinks++;
                if (OnScreen(me)) { Ring(fxp, head + Vector3.up * 0.03f, 0.35f); Snd("critter_croc_sink", me, SinkSoundVol, 4f, 40f, "sd_croc_submerge_b", "sd_croc_submerge_a"); }
            }
            if (before >= 1f && v.sink < 1f)
            {   // it comes: a soft ring and a blup
                OzCrocJ15Stats.Rises++;
                if (OnScreen(me)) { Ring(fxp, head + Vector3.up * 0.03f, 0.35f); Snd("critter_croc_resurface", me, RiseSoundVol, 4f, 40f, "sd_croc_lurk_blup_a"); }
            }
            v.sinkGone = v.sink >= 1f;
            v.drop = SinkDepthM * Mathf.SmoothStep(0f, 1f, v.sink);
            if (v.sinkGone) hidden = true;
        }

        /// <summary>Step, both calls of the V wake: a crocodile that sinks, is under or comes up again draws no V (it fades from the sink's
        /// first frame); while it fades the card (a child of the drawn root, which goes down by v.drop) is kept on the water.</summary>
        void J15Wake(View v, float speed, float dt)
        {
            Wake(v, v.sink > 0f ? 0f : speed, dt);
            if (v.drop > 0f && v.wakeR && v.wakeR.enabled && v.wakeTr && v.wakeTr.parent)
            {
                var lp = v.wakeTr.localPosition; lp.y += v.drop / Mathf.Max(1e-3f, Mathf.Abs(v.wakeTr.parent.lossyScale.y));
                v.wakeTr.localPosition = lp;
            }
        }

        /// <summary>The fast catch is under: bubbles, a ring, the helmet as after the roll; the robot goes with it.</summary>
        void FinishFastDive(View v)
        {
            v.fastDone = true; OzCrocJ15Stats.FastDivesDone++;
            var p = v.pos; var fx = OpenZFxPlayer.I;
            Snd("critter_croc_bubbles", p, 0.7f, 8f, 60f, "sd_croc_bubbles_a", "sd_croc_bubbles_b");
            Bubbles(p, 2.5f); if (fx && OnScreen(p)) fx.Ripple(p + Vector3.up * 0.03f, 0.75f, 1.5f);
            FloatPart(v, p);
            if (v.robot && !v.robotHidden) { v.robot.Hide(); v.robotHidden = true; }
            v.robot = null;
            L16AnimalKit.StopLoop("critter_croc_thrash_loop", ref v.thrashLoop);
        }

        // ------------------------------------------------------------------ the robot in the jaws
        Vector3 JawGrip(View v, Quaternion robotTurn) => v.prey.position + robotTurn * Vector3.up * (GripDesignM * crocScale);

        /// <summary>Step: the robot in the jaws, k = 0..1 the snatch from where it stood (0.12 s).</summary>
        void SeatRobot(View v, Transform tr, float k)
        {
            if (!seatReady)
            {   // as before: its feet on the socket, its own turn
                tr.SetPositionAndRotation(Vector3.Lerp(v.robotFrom, v.prey.position, k), Quaternion.Slerp(tr.rotation, v.prey.rotation * v.robotRel, k));
                return;
            }
            var want = v.prey.rotation * preyToRobot;
            tr.rotation = Quaternion.Slerp(v.robotRot0, want, k);
            var off = v.grip ? v.grip.position - tr.position : tr.rotation * Vector3.up * v.gripH;           // its grip point now, from its feet
            tr.position = Vector3.Lerp(v.robotFrom, JawGrip(v, want) - off, k);
        }

        /// <summary>Just before the frame is drawn (after every script moved what it moves): the held robot's grip point against the
        /// jaws' grip point, per robot type and catch.</summary>
        static void SampleGrip(ScriptableRenderContext ctx, List<Camera> cams)
        {
            var me = I; if (!me || me.W == null || !me.seatReady) return;
            if (Time.frameCount == sampledFrame) return; sampledFrame = Time.frameCount;
            foreach (var v in me.views)
            {
                if (!v.robot || v.robotHidden || !v.prey || v.robotT < 0.12f) continue;
                var want = v.prey.rotation * me.preyToRobot; var jaw = me.JawGrip(v, want);
                var at = v.grip ? v.grip.position : v.robot.transform.position + v.robot.transform.up * v.gripH;
                float d = Vector3.Distance(at, jaw);
                OzCrocJ15Stats.AddGrip((v.robotType ?? "?") + (v.fast ? " fast" : " roll"), d, v.jawBone ? Vector3.Distance(jaw, v.jawBone.position) : -1f);
            }
            foreach (var v in me.views)
            {   // no robot or vehicle inside a drawn passive animal's own room
                if (v.show != 0 || v.rig == null || !v.rig.ok || v.c.Busy || v.c.Hidden) continue;
                bool drawn = v.rig.Shown && !v.sinkGone;
                foreach (var m in me.movers)
                {
                    float dx = m.at.x - v.pos.x, dz = m.at.z - v.pos.z;
                    long key = ((long)v.c.Index << 32) | (uint)m.id;
                    if (dx * dx + dz * dz > SinkTouchM * SinkTouchM) { OzCrocJ15Stats.Inside.Remove(key); continue; }
                    OzCrocJ15Stats.InsideSamples++;
                    if (OzCrocJ15Stats.Inside.Add(key)) OzCrocJ15Stats.Crossings++;
                    if (drawn) OzCrocJ15Stats.SeenInside++;
                }
            }
        }
        static int sampledFrame = -1;
    }

    /// <summary>The counted checks of kit J15 (Editor/OzCrocJ15Check.cs reads and resets them).</summary>
    public static class OzCrocJ15Stats
    {
        public static int Clacks, RollSplashes, DiveSplashes, DragSplashes, Sinks, Rises, FastCatches, RollCatches, FastDivesDone;
        /// <summary>A robot or vehicle within the animal's own room of a passive crocodile: samples, entries, and samples in which
        /// the crocodile was drawn (wanted 0).</summary>
        public static int InsideSamples, Crossings, SeenInside;
        public static readonly HashSet<long> Inside = new HashSet<long>();
        public static readonly Dictionary<string, int> CatchesBy = new Dictionary<string, int>();
        public static readonly Dictionary<string, float> GripHeight = new Dictionary<string, float>();
        public sealed class Grip { public int frames; public float max, sum, jawMin = 99f, jawMax = -1f; }
        public static readonly Dictionary<string, Grip> GripBy = new Dictionary<string, Grip>();

        public static void AddGrip(string key, float d, float jawHinge)
        {
            if (!GripBy.TryGetValue(key, out var g)) GripBy[key] = g = new Grip();
            g.frames++; g.sum += d; if (d > g.max) g.max = d;
            if (jawHinge >= 0f) { if (jawHinge < g.jawMin) g.jawMin = jawHinge; if (jawHinge > g.jawMax) g.jawMax = jawHinge; }
        }

        public static void Reset()
        {
            Clacks = RollSplashes = DiveSplashes = DragSplashes = Sinks = Rises = FastCatches = RollCatches = FastDivesDone = 0;
            InsideSamples = Crossings = SeenInside = 0; Inside.Clear(); CatchesBy.Clear(); GripHeight.Clear(); GripBy.Clear();
        }
    }
}
