using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Hero legs (item 209: "Alan's and Brad's walk is not fixed ... redo the legs completely"). In the game the two heroes keep their
    /// humanoid avatars and the grunt's controller for everything above the pelvis; this component, on the hero's Model object (the
    /// Animator's), writes the Hips and the 8 leg bones every frame AFTER the Animator from poses baked on the hero's own skeleton
    /// (Resources/OpenZ/HeroSquad/Legs/&lt;member&gt;_legs.json, View/OzHeroLegs.Data.cs): no retarget, no IK, no leg code of the old kind
    /// on them, which is how the cutscenes drive their legs.
    /// <list type="bullet">
    /// <item>Gaits (walking, run_forward, run_backward, strafes): generated cycles with a fixed knee plane (knees and toes out), the
    /// planted foot still in the world by construction; the phase advances with the DISTANCE the drawn root travels along the gait
    /// (phase += |v along| x dt / stride), so a planted boot cannot slide at any speed; below <see cref="HoldSpeed"/> the phase runs at the
    /// Animator's own rate (a held body never freezes a foot in the air). Gait to gait keeps the phase; a gait entered from a standing
    /// state starts at the bake's entry phase (the feet nearest their standing spots).</item>
    /// <item>Every standing state (idle, shooting, grenade, drink, grabs, ...) is ONE static pose: the hero's own cutscene stance, so no foot
    /// ever moves between standing states.</item>
    /// <item>The camp lie is one static pose (hips, spine, legs), written from its first frame: nothing on it can move while he lies.</item>
    /// <item>Lie-down / get-up, knockdown, the animated deaths, jumps, climbing and seats follow the Animator's normalizedTime through the
    /// grunt's takes transferred bone by bone to the hero's proportions.</item>
    /// <item>Between two programs the pose follows the Animator's own transition weight; a change with no transition (a Play call) eases
    /// over <see cref="Ease"/> s, except the level-start lie (written at once).</item>
    /// </list>
    /// RobotLegIK releases its foot goals for a driven hero (<see cref="Drives"/>: one hook); every other robot's pass is unchanged. The
    /// switch <see cref="On"/> (PlayerPrefs <see cref="Pref"/>, menu OpenZ/Legs) OFF = the heroes' legs exactly as before, frame for frame.
    /// A boarding clone (the whole view instantiated, no UnitView) keeps the component and drives its climb / seat / jump legs from the
    /// Animator alone. The player ships with no solver, no ground sampling: what the bake's pictures show is, bone for bone, what it writes.
    /// Counters for the checks: <see cref="Attached"/>, <see cref="Frames"/>, <see cref="CloneFrames"/>, <see cref="HipsWrites"/>, <see cref="Log"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(61)]   // LateUpdate after UnitView (60: places the root, picks the state), before UnitAim (62), RobotLegIK (70), WeaponHold / TrackMarks (80), RobotGestures (95)
    public sealed partial class OzHeroLegs : MonoBehaviour
    {
        /// <summary>The switch (false = the heroes' legs as before this component).</summary>
        public static bool On = true;
        /// <summary>PlayerPrefs key read at start (1 = on, the default).</summary>
        public const string Pref = "oz_hero_legs";
        /// <summary>Below this speed along the gait (m/s) a gait state advances at the Animator's own rate.</summary>
        public static float HoldSpeed = 0.15f;
        /// <summary>The own ease (s) between two programs when the Animator runs no transition.</summary>
        public static float Ease = 0.2f;
        /// <summary>Smoothing (s) of the drawn root's velocity.</summary>
        public static float VelTau = 0.08f;
        /// <summary>Checks: views driven, frames written on views, frames written on boarding clones, Hips writes.</summary>
        public static int Attached, Frames, CloneFrames, HipsWrites;
        public static readonly List<string> Log = new List<string>();

        [SerializeField] string member;                                                   // kept on an instantiated clone
        Animator anim; UnitView view; bool viewLooked; LegData data; Transform[] bones; Transform hips; bool ready, inited; string why = "not set up";
        readonly Quaternion[] qa = new Quaternion[16], qb = new Quaternion[16], qo = new Quaternion[16], lastQ = new Quaternion[16], easeQ = new Quaternion[16];
        Vector3 pa, pb, po, lastP, easeP; bool wroteOnce; float easeT = 99f;
        Program cur, nxt, prevCur, prevNxt; float phase; bool gaitLive, entryPending;
        Vector3 rootPrev, vel; bool rootPrevOk; readonly Vector3[] footPrev = new Vector3[2]; bool footPrevOk;

        /// <summary>Checks: set up and driving (data loaded, every bone found).</summary>
        public bool Ready => ready;
        public string Why => why;
        public string Member => member;
        public LegData Data => data;
        /// <summary>Checks: the program of the current state, the next state's, the Animator's transition weight, the gait phase.</summary>
        public string ProgramName => cur != null ? cur.Name : "-";
        public string NextName => nxt != null ? nxt.Name : "-";
        public float BlendW { get; private set; }
        public float Phase => phase;
        /// <summary>Checks: the drawn root's speed along the gait (m/s) this frame.</summary>
        public float SpeedAlong { get; private set; }
        /// <summary>Checks: the pose was written this frame; how many bones carry a program (9 or 12).</summary>
        public bool Wrote { get; private set; }
        public int WrittenBones { get; private set; }
        /// <summary>Checks: the largest foot step (world xz, m) at the last stance to gait entry (-1 before one happened).</summary>
        public float EntryStep { get; private set; } = -1f;
        /// <summary>Checks: a bone by its index in the data's bone list (0 Hips, 3 LeftFoot, 4 LeftToeBase, 7 RightFoot, 8 RightToeBase).</summary>
        public Transform Bone(int i) => bones != null && i >= 0 && i < bones.Length ? bones[i] : null;
        /// <summary>Checks: the foot (0 left, 1 right) is planted by the program that weighs most this frame.</summary>
        public bool Planted(int foot) { var p = BlendW >= 0.5f && nxt != null ? nxt : cur; return p != null && p.IsGait && p.Planted(foot, phase); }

        static readonly Dictionary<int, OzHeroLegs> byView = new Dictionary<int, OzHeroLegs>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { byView.Clear(); cache.Clear(); Attached = Frames = CloneFrames = HipsWrites = 0; Log.Clear(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { On = PlayerPrefs.GetInt(Pref, 1) == 1; }

        static void Say(string s) { if (Log.Count > 100) Log.RemoveAt(0); Log.Add(s); Debug.Log("[OpenZ HeroLegs] " + s); }

        /// <summary>OzHeroSquadView.MakeView, after the view is bound: puts the component on the view's Animator object and starts it.</summary>
        public static OzHeroLegs Attach(UnitView v, string member)
        {
            if (!v || string.IsNullOrEmpty(member)) return null;
            var an = v.GetComponentInChildren<Animator>();
            if (!an) { Say(member + ": no Animator under the view: legs not driven"); return null; }
            var c = an.GetComponent<OzHeroLegs>();
            if (!c) c = an.gameObject.AddComponent<OzHeroLegs>();
            c.member = member; c.view = v; c.viewLooked = true;
            c.Init();
            byView[v.GetInstanceID()] = c;
            if (c.ready) Attached++;
            Say($"{member} #{(v.Obj != null ? v.Obj.Id : -1)}: {(c.ready ? "legs driven (" + c.data.Programs.Length + " programs, " + c.data.ByState.Count + " states)" : "NOT driven: " + c.why)}");
            return c;
        }

        /// <summary>RobotLegIK's hook: this view's legs are written by the component (its own foot goals are released).</summary>
        public static bool Drives(UnitView v) => On && v && v.Obj != null && v.Obj.IsHero && byView.TryGetValue(v.GetInstanceID(), out var c) && c && c.ready && c.isActiveAndEnabled;

        /// <summary>Checks: the component driving this view (null: none).</summary>
        public static OzHeroLegs Of(UnitView v) => v && byView.TryGetValue(v.GetInstanceID(), out var c) && c ? c : null;

        void Awake() { if (!inited && !string.IsNullOrEmpty(member)) Init(); }           // a clone: the member is serialized, the view is gone

        void OnDestroy()
        {
            int key = -1;
            foreach (var kv in byView) if (kv.Value == this) { key = kv.Key; break; }
            if (key != -1) byView.Remove(key);
        }

        void Init()
        {
            inited = true; ready = false;
            anim = GetComponent<Animator>();
            if (!anim) { why = "no Animator on " + name; return; }
            data = Load(member);
            if (data == null || data.Programs == null || data.Programs.Length == 0) { why = "legs data: " + (data != null && data.Error != null ? data.Error : "missing"); return; }
            if (data.Error != null) Say(member + ": legs data loaded with a fault: " + data.Error);
            var names = data.Bones;
            if (names == null || names.Length < 9 || names.Length > qa.Length) { why = "legs data: bone list wrong"; return; }
            bones = new Transform[names.Length];
            var all = anim.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < names.Length; i++)
            {
                Transform found = null; int hits = 0;
                foreach (var t in all) if (t.name == names[i]) { if (!found) found = t; hits++; }
                if (!found) { why = "bone " + names[i] + " not found under " + anim.name; bones = null; return; }
                if (hits > 1) Say(member + ": bone name " + names[i] + " found " + hits + " times (the first is driven)");
                bones[i] = found;
            }
            hips = bones[0];
            ready = true; why = "ready";
        }

        void Drop() { Wrote = false; wroteOnce = false; gaitLive = false; entryPending = false; cur = nxt = prevCur = prevNxt = null; easeT = 99f; footPrevOk = false; SpeedAlong = 0f; BlendW = 0f; }

        void LateUpdate()
        {
            if (!On || !ready || !anim || !anim.enabled || !anim.isInitialized) { Drop(); return; }
            if (!viewLooked) { viewLooked = true; view = GetComponentInParent<UnitView>(); }
            bool hasView = view;
            float dt = Time.deltaTime;
            // 1. the drawn root's velocity (UnitView.Sync placed it in Update, before the Animator)
            var rootT = hasView ? view.transform : transform;
            var rp = rootT.position;
            if (rootPrevOk && dt > 0f)
            {
                var v = (rp - rootPrev) / dt; v.y = 0f;
                if (v.sqrMagnitude > 64f) v = vel;                                              // a teleport (> 8 m/s): the last velocity kept
                vel = Vector3.Lerp(vel, v, 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, VelTau)));
            }
            rootPrev = rp; rootPrevOk = true;
            // 2. the programs of this frame: the current state's and, in a transition, the next state's with the Animator's own weight
            var st = anim.GetCurrentAnimatorStateInfo(0);
            cur = data.ForState(st.shortNameHash); nxt = null; float w = 0f; var ns = default(AnimatorStateInfo);
            bool inTr = anim.IsInTransition(0);
            if (inTr) { ns = anim.GetNextAnimatorStateInfo(0); nxt = data.ForState(ns.shortNameHash); w = Mathf.Clamp01(anim.GetAnimatorTransitionInfo(0).normalizedTime); }
            if (cur == null && nxt == null) { Drop(); return; }                                 // a state without a program: nothing written this frame
            BlendW = w;
            // 3. the gait phase (distance along the gait; the Animator's rate while the body is held; the entry phase from a stance)
            var gait = nxt != null && nxt.IsGait ? nxt : cur != null && cur.IsGait ? cur : null;
            if (gait != null)
            {
                if (!gaitLive) { phase = gait.EntryPhase; gaitLive = true; entryPending = true; }
                float sc = Mathf.Max(0.1f, anim.transform.lossyScale.y);
                float along = hasView ? Mathf.Abs(Vector3.Dot(vel, GaitDir(gait, rootT))) : 0f;
                SpeedAlong = along;
                float k = Mathf.Clamp01(along / Mathf.Max(0.01f, HoldSpeed));
                float rate = k * along / Mathf.Max(0.05f, gait.Stride * sc) + (1f - k) * Mathf.Abs(anim.speed) / Mathf.Max(0.05f, gait.Period);
                if (dt > 0f) phase = Mathf.Repeat(phase + rate * dt, 1f);
            }
            else { gaitLive = false; entryPending = false; SpeedAlong = 0f; }
            // 4. sample and blend (a missing program on one side = the Animator's own pose, so a fade to / from it is smooth)
            int n = bones.Length;
            int na = Fill(cur, st, qa, out pa), nb = Fill(nxt, ns, qb, out pb);
            if (nxt == null || w <= 0f) { for (int b = 0; b < n; b++) qo[b] = qa[b]; po = pa; }
            else if (cur == null || w >= 1f) { for (int b = 0; b < n; b++) qo[b] = qb[b]; po = pb; }
            else { for (int b = 0; b < n; b++) qo[b] = Quaternion.Slerp(qa[b], qb[b], w); po = Vector3.Lerp(pa, pb, w); }
            // 5. the own ease where the Animator ran no transition (a Play call); the level-start lie is written at once
            bool changed = cur != prevCur, followed = cur != null && cur == prevNxt;
            bool lieStart = cur != null && cur.Name == "lie" && (prevCur == null || prevCur.Name == "stand");
            if (wroteOnce && changed && !followed && !inTr && !lieStart) { for (int b = 0; b < n; b++) easeQ[b] = lastQ[b]; easeP = lastP; easeT = 0f; }
            if (easeT < Ease)
            {
                float e = Mathf.SmoothStep(0f, 1f, easeT / Mathf.Max(0.01f, Ease)); easeT += dt;
                for (int b = 0; b < n; b++) qo[b] = Quaternion.Slerp(easeQ[b], qo[b], e);
                po = Vector3.Lerp(easeP, po, e);
            }
            // 6. write: the Hips' local position (+ the root's sole offset, world up) and the local rotations; the Animator rewrites the
            //    same transforms next frame, so nothing accumulates
            float scW = Mathf.Max(0.1f, anim.transform.lossyScale.y);
            float liftWorld = data.J.soleLift - data.J.soleY * scW;
            var lift = hips.parent ? hips.parent.InverseTransformVector(Vector3.up * liftWorld) : Vector3.zero;
            hips.localPosition = po + lift; HipsWrites++;
            for (int b = 0; b < n; b++) { bones[b].localRotation = qo[b]; lastQ[b] = qo[b]; }
            lastP = po; wroteOnce = true; Wrote = true; WrittenBones = Mathf.Max(na, nb); prevCur = cur; prevNxt = nxt;
            Frames++; if (!hasView) CloneFrames++;
            // 7. the feet after the write (the checks: the largest foot step at a stance to gait entry)
            var fL = bones[3].position; var fR = bones[7].position;
            if (entryPending)
            {
                if (footPrevOk) { var dL = fL - footPrev[0]; dL.y = 0f; var dR = fR - footPrev[1]; dR.y = 0f; EntryStep = Mathf.Max(dL.magnitude, dR.magnitude); }
                entryPending = false;
            }
            footPrev[0] = fL; footPrev[1] = fR; footPrevOk = true;
        }

        /// <summary>The program's pose at its phase into q / hp (the bones it does not carry, and a null program: the Animator's own pose).</summary>
        int Fill(Program p, AnimatorStateInfo s, Quaternion[] q, out Vector3 hp)
        {
            int n = bones.Length;
            for (int b = 0; b < n; b++) q[b] = bones[b].localRotation;
            hp = hips.localPosition;
            if (p == null) return 0;
            float u = p.IsGait ? phase : p.IsStatic ? 0f : p.Loop ? Mathf.Repeat(s.normalizedTime, 1f) : Mathf.Clamp01(s.normalizedTime);
            p.Sample(u, q, out hp);
            return p.NBones;
        }

        /// <summary>The gait's travel direction in the world: the body's front for walk / run (robots face their -z: UnitView yaw + modelYaw
        /// 180, RobotLegIK reads the front the same way), its back for the back-pedal, its side for a strafe.</summary>
        static Vector3 GaitDir(Program g, Transform root)
        {
            var f = -root.forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
            f.Normalize();
            switch (g.Dir)
            {
                case "back": return -f;
                case "left": return -Vector3.Cross(Vector3.up, f);                                 // Cross(up, front) is the body's right
                case "right": return Vector3.Cross(Vector3.up, f);
                default: return f;
            }
        }
    }
}
