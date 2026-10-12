using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// (lift .33, sway .20, phase = travel x 1.6) inside the leg's own plane: the feet never touched a real spot, so they slid and
    /// floated over slopes, decks and craters. Now:
    /// <list type="bullet">
    /// <item>each foot is LOCKED on a ground spot (height from ZGame.GroundY = terrain + bridge decks, the crater row extends it;
    /// never units or debris) while it stands; its yaw is locked too;</item>
    /// <item>a step starts when a foot is too far from its predicted spot (home under the hip + velocity x half a swing), when the
    /// body has turned more than YawStep from the foot (turn in place), or to square up after stopping; one foot swings at a time;</item>
    /// <item>the swing is an eased arc to the spot the foot must land on (re-aimed every frame from the walk speed and heading);</item>
    /// <item>the pelvis height follows the planted feet (and drops if a leg would over-stretch), with a small step bob;</item>
    /// <item>two-bone IK per leg in its own plane (the knee always bends to its rest side = the pole hint, a soft reach limit so
    /// it never snaps straight), a small hip roll toward a foot that sits off the plane, the ankle plate turned to the ground
    /// normal (at most MaxTilt deg) and kept on the foot's locked yaw;</item>
    /// <item>ONE plant event per landing drives LegacyEngineAudio (mech_step + MechFoley's servo / stomp), TrackMarks (footprint +
    /// a surface-aware dust puff) and the Planted callback; nothing else times mech steps from Travel any more.</item>
    /// </list>
    /// A mech that dies mid-step keeps its last leg pose (no snap back to rest). View only.
    /// <para>
    /// MECH FOOT IK (On = oz_mech_foot_ik, default ON, saved, live; editor menus OpenZ/Mech/Foot IK On / Off). OFF = the pre-IK MechWalk
    /// </para>
    /// <para>The slide leftover (stepping mean 4.6-5.9 cm, max 21.6-86.3 cm): the probe read the ankle BEFORE this frame's solve, i.e.
    /// last frame's leg pose carried along by this frame's body move (v x dt: 5 cm at 1.5 m/s and 30 fps; a hitch frame gives the
    /// 20-90 cm maxima). It now reads the DRAWN ankle after the solve, and the real slide sources are closed: the solve is closed loop
    /// (IkPasses re-aims against the real bone chain: the roll approximation, out-of-plane bone offsets, scales), a locked foot never
    /// leaves the soft-reach zone (a hard, unsmoothed floor on the pelvis), and the other foot's swing is hurried before the
    /// over-stretch guard has to drag a locked foot. Every stance over SlideWarnCm is logged with its cause. Heights stay ZGame.GroundY.</para>
    /// </summary>
    [DefaultExecutionOrder(79)]
                                  // TrackMarks (80) reads the ankles for the OFF prints, every frame, as the pre-IK MechWalk (order 0) always was
    public class MechWalk : MonoBehaviour
    {
        /// <summary>
        /// LateUpdate switches). OFF = the previous mech walk without the foot lock (Legacy), statement for statement.</summary>
        public const string Pref = "oz_mech_foot_ik";
        public const string Label = "MECH FOOT IK · PLANTED FEET";
        public const string Tip = "MECH FOOT IK|On (default): the walking mech plants each foot on the ground (sand, slopes, bridge decks, craters), keeps it still while it carries the weight and steps when it turns. Off: the classic mech walk cycle. Visual only: the rules never change.|";
        static int on = -1;
        public static bool On
        {
            get { if (on < 0) on = PlayerPrefs.GetInt(Pref, 1) == 1 ? 1 : 0; return on == 1; }
            set
            {
                on = value ? 1 : 0; PlayerPrefs.SetInt(Pref, on); PlayerPrefs.Save();
                Debug.Log($"[OpenZ Options] mech foot IK: {(value ? "ON" : "OFF (the classic mech walk)")} ({Pref} {on}, saved, live)");
            }
        }
        /// <summary>Step tuning (world metres at model scale 1 are multiplied by the model scale).</summary>
        public static float StepHeight = 0.30f, SwingMin = 0.28f, SwingMax = 0.55f, YawStep = 24f, MaxTilt = 20f, MaxRoll = 18f,
                            SettleAfter = 0.35f, SettleDist = 0.12f, MaxStepShare = 0.8f, BobAmp = 0.06f, PlantDip = 0.05f, PelvisSmooth = 0.08f;

        class Leg
        {
            public Transform hip, knee, ankle; public Vector2 a, b, target; public float restA, restB; public Quaternion hipRest, kneeRest, ankleRest; public bool left;
            // world stepping
            public Vector3 homeLocal; public float ankleH, len, restLat, A, B;
            public bool planted = true; public Vector3 plant, from, aim; public float plantYaw, fromYaw, plantedFor, swingT, swingDur, stepH, stepLen;
            public Vector3 normal = Vector3.up, foot; public float footYaw, groundNow;
            // slide probe: ankle drift over one stance
            public Vector3 stanceStart; public float stanceMax; public bool stanceOk;
            public float stanceT, stanceYaw, dragCm, residCm, rollNeed; public int softFrames; public bool soft;
            // legacy probe
            public bool legacyLow;
        }
        Leg[] legs;
        Transform pelvis; Vector3 pelvisRest;
        Vector3 lastPos, vel; float travel, age, moving, pelvisOff, pelvisVel, dip, stillFor; bool init;
        UnitView view; Transform body;
        /// <summary>vehicles_nb_v1 NB26: metres walked (kept for the off-screen / no-MechWalk fallbacks).</summary>
        public float Travel => travel;
        public float sign = 1f;                                        // glTF handedness: flip if the knees bend backwards
        /// <summary>Landing callback: (walk, leg 0 = left / 1 = right, the planted ankle's ground point, the ground normal).</summary>
        public System.Action<MechWalk, int, Vector3, Vector3> Planted;
        /// <summary>Plants so far (proof counter) and true while the world-space stepping drives this mech's footfalls.</summary>
        public int Plants { get; private set; }
        public bool Drives => On && legs != null && legs.Length == 2 && init && isActiveAndEnabled;
        /// <summary>Slide probe (cm per stance, the planted ankle's ground drift from landing to lift-off), all mechs, both modes.
        /// </summary>
        public static int StanceN, SlideOverN, Hurries, Drags; public static double SlideSumCm; public static float SlideMaxCm;
        /// <summary>
        /// solve passes; ReachKeep = the share of a leg's in-plane reach a LOCKED foot may use (the solve's soft zone starts at 0.96);
        /// HurryLeft = the most swing time left when a locked foot nears the end of its reach; MaxCrouch = the deepest pelvis floor.</summary>
        public static float SlideWarnCm = 2f, ReachKeep = 0.94f, HurryLeft = 0.10f, MaxCrouch = 0.5f; public static int IkPasses = 3; public static bool SlideLog = true;
        public static void ResetSlide() { StanceN = 0; SlideSumCm = 0; SlideMaxCm = 0f; SlideOverN = 0; Hurries = 0; Drags = 0; }
        public static string SlideStats => $"[MechWalk] {(On ? "stepping (foot IK ON)" : "legacy sine (foot IK OFF)")}: {StanceN} stances, slide mean {(StanceN > 0 ? SlideSumCm / StanceN : 0):0.00} cm, max {SlideMaxCm:0.00} cm" +
                                           (On ? $", over {SlideWarnCm:0.#} cm {SlideOverN}, hurried swings {Hurries}, over-stretch drag frames {Drags} (drawn ankle, after the solve)" : "");

        void Start()
        {
            var l = Make("L"); var r = Make("R");
            legs = l != null && r != null ? new[] { l, r } : new Leg[0];
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == "MechPelvis") { pelvis = t; pelvisRest = t.localPosition; }
            view = GetComponentInParent<UnitView>();
            body = view ? view.transform : transform;
            foreach (var g in legs)
            {   // rest pose (Start runs before the first LateUpdate): home spot under the hip and the ankle's height over the soles' plane
                g.homeLocal = body.InverseTransformPoint(g.ankle.position); g.homeLocal.y = 0f;
                g.ankleH = Mathf.Max(0.02f, g.ankle.position.y - body.position.y);
                g.len = Vector3.Distance(g.hip.position, g.knee.position) + Vector3.Distance(g.knee.position, g.ankle.position);
                var par = g.hip.parent;
                if (par)
                {
                    var k = par.InverseTransformPoint(g.knee.position) - g.hip.localPosition; var an = par.InverseTransformPoint(g.ankle.position) - g.hip.localPosition;
                    g.A = k.magnitude; g.B = (an - k).magnitude; g.restLat = an.x;
                }
            }
            lastPos = transform.position;
        }

        Leg Make(string side)
        {
            Transform hip = null, knee = null, ankle = null;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "MechHip_" + side) hip = t; else if (t.name == "MechKnee_" + side) knee = t; else if (t.name == "MechAnkle_" + side) ankle = t;
            }
            if (!hip || !knee || !ankle) return null;
            var g = new Leg { hip = hip, knee = knee, ankle = ankle, left = side == "L", hipRest = hip.localRotation, kneeRest = knee.localRotation, ankleRest = ankle.localRotation };
            g.a = new Vector2(knee.localPosition.z, knee.localPosition.y); g.b = new Vector2(ankle.localPosition.z, ankle.localPosition.y);
            g.restA = Mathf.Atan2(g.a.y, g.a.x); g.restB = Mathf.Atan2(g.b.y, g.b.x); g.target = g.a + g.b;
            return g;
        }

        void LateUpdate()
        {
            if (legs == null) return;
            float dt = Time.deltaTime; var p = transform.position; var d = p - lastPos; d.y = 0; lastPos = p;
            float step = d.magnitude; bool tele = step > 3f; if (tele) step = 0;                 // teleports don't stride
            travel += step; age += dt;
            var game = ZGame.I;
            // (as before, a dying mech's legs ease back to rest) and Travel counted even with no legs found (LegacyEngineAudio's steps)
            if (!On || !game) { Legacy(dt, step); init = false; return; }
            if (legs.Length == 0) return;
            if (view && view.Dying) return;                                                      // killed mid-step: the legs keep their last pose
            if (dt <= 0f) return;
            Stepping(game, dt, tele ? Vector3.zero : d / dt, tele);
        }

        // ================================================================================================ world-space stepping
        static float Smooth(float s) => s * s * (3f - 2f * s);

        Vector3 Ground(ZGame g, Vector3 at) { at.y = g.GroundY(at.x, at.z); return at; }

        /// <summary>Ground normal from GroundY (the same height source as the plants, so craters / decks agree), tilt-clamped.</summary>
        Vector3 Normal(ZGame g, Vector3 at, float e)
        {
            float hx = g.GroundY(at.x + e, at.z) - g.GroundY(at.x - e, at.z), hz = g.GroundY(at.x, at.z + e) - g.GroundY(at.x, at.z - e);
            var n = new Vector3(-hx / (2f * e), 1f, -hz / (2f * e)).normalized;
            float ang = Vector3.Angle(Vector3.up, n);
            if (ang > MaxTilt) n = Vector3.Slerp(Vector3.up, n, MaxTilt / ang).normalized;
            return n;
        }

        void Stepping(ZGame g, float dt, Vector3 rawVel, bool tele)
        {
            float sc = Mathf.Max(0.05f, body.lossyScale.y);
            vel = tele ? Vector3.zero : Vector3.Lerp(vel, rawVel, 1f - Mathf.Exp(-dt / 0.12f));
            float speed = vel.magnitude;
            float bodyYaw = body.eulerAngles.y;
            moving = Mathf.MoveTowards(moving, speed > 0.05f * sc ? 1f : 0f, dt * 4f);
            stillFor = speed > 0.05f * sc ? 0f : stillFor + dt;
            if (!init || tele)
            {   // first frame / teleport: both feet stand on their home spots
                foreach (var l in legs)
                {
                    l.plant = Ground(g, body.TransformPoint(l.homeLocal)); l.normal = Normal(g, l.plant, 0.35f * sc);
                    l.planted = true; l.plantYaw = bodyYaw; l.plantedFor = 1f; l.stanceOk = false;
                }
                pelvisOff = 0f; pelvisVel = 0f; dip = 0f; init = true;
            }
            float reachH = 0.75f * legs[0].len, maxStep = MaxStepShare * reachH;
            float swing = Mathf.Clamp(maxStep / Mathf.Max(0.01f, 2f * speed), SwingMin, SwingMax);
            // ---- which leg steps (one at a time; the one farthest off its spot first)
            int pick = -1; float best = 0f;
            for (int i = 0; i < 2; i++)
            {
                var l = legs[i]; var o = legs[1 - i];
                if (!l.planted) continue;
                l.plantedFor += dt;
                var home = body.TransformPoint(l.homeLocal);
                var pred = home + vel * (swing * 0.5f);
                float err = new Vector2(l.plant.x - pred.x, l.plant.z - pred.z).magnitude;
                float yawErr = Mathf.Abs(Mathf.DeltaAngle(l.plantYaw, bodyYaw));
                float hipDist = new Vector2(l.hip.position.x - l.plant.x, l.hip.position.z - l.plant.z).magnitude;
                bool rollOut = Mathf.Abs(l.rollNeed) > 0.9f * MaxRoll && err > 0.1f * sc;
                bool need = (moving > 0.5f && err > Mathf.Max(0.1f * sc, speed * swing * 0.95f))
                            || yawErr > YawStep || hipDist > reachH || rollOut
                            || (stillFor > SettleAfter && (err > SettleDist * sc || yawErr > 6f));
                bool otherOk = o.planted && o.plantedFor > 0.06f;
                float score = err / Mathf.Max(0.01f, maxStep) + yawErr / 90f + (hipDist > reachH ? 2f : 0f) + (rollOut ? 1f : 0f);
                if (need && otherOk && score > best) { best = score; pick = i; }
            }
            if (pick >= 0)
            {
                var l = legs[pick];
                LiftOff(l);
                l.planted = false; l.from = l.plant; l.fromYaw = l.plantYaw; l.swingT = 0f; l.swingDur = swing; l.stepH = -1f;
            }
            // HurryLeft s left, same arc, only faster) instead of letting the over-stretch guard below drag the locked foot (that drag IS slide)
            for (int i = 0; i < 2; i++)
            {
                var l = legs[i]; var o = legs[1 - i];
                if (!l.planted || o.planted) continue;
                if (new Vector2(l.hip.position.x - l.plant.x, l.hip.position.z - l.plant.z).magnitude <= 0.95f * reachH) continue;
                float rest = 1f - Mathf.Clamp01(o.swingT), left = rest * o.swingDur;
                if (left > HurryLeft) { o.swingDur = HurryLeft / Mathf.Max(0.05f, rest); Hurries++; }
            }
            // ---- feet
            float bob = 0f;
            for (int i = 0; i < 2; i++)
            {
                var l = legs[i];
                if (l.planted)
                {
                    l.plant.y = g.GroundY(l.plant.x, l.plant.z);                                   // a crater forming under a planted foot
                    l.foot = l.plant + Vector3.up * l.ankleH; l.footYaw = l.plantYaw; l.groundNow = l.plant.y;
                    // over-stretch guard while the other foot is in the air: the foot is dragged (never the leg snapping); counted as slide
                    var hp = l.hip.position; var fl = new Vector2(l.plant.x - hp.x, l.plant.z - hp.z);
                    float lim = 1.05f * reachH;
                    if (fl.magnitude > lim)
                    {
                        var was = l.plant; fl = fl.normalized * lim; l.plant.x = hp.x + fl.x; l.plant.z = hp.z + fl.y; l.plant = Ground(g, l.plant); l.foot = l.plant + Vector3.up * l.ankleH;
                        l.dragCm += new Vector2(l.plant.x - was.x, l.plant.z - was.z).magnitude * 100f; Drags++;
                    }
                    continue;
                }
                l.swingT += dt / Mathf.Max(0.05f, l.swingDur);
                float s = Mathf.Clamp01(l.swingT), e = Smooth(s);
                var home = body.TransformPoint(l.homeLocal);
                l.aim = Ground(g, home + vel * ((1f - s) * l.swingDur + swing * 0.5f));           // re-aimed every frame: speed / heading / turn
                float len = new Vector2(l.aim.x - l.from.x, l.aim.z - l.from.z).magnitude;
                if (l.stepH < 0f) l.stepH = Mathf.Clamp(StepHeight * sc * (0.55f + 0.45f * len / Mathf.Max(0.01f, maxStep)), 0.12f * sc, StepHeight * sc * 1.2f);
                var xz = Vector3.Lerp(l.from, l.aim, e);
                float baseY = Mathf.Lerp(l.from.y, l.aim.y, e);
                baseY = Mathf.Max(baseY, g.GroundY(xz.x, xz.z));                                     // never through a bump on the way
                float u = 4f * s * (1f - s);
                l.foot = new Vector3(xz.x, baseY + l.ankleH + l.stepH * u * u, xz.z);
                l.footYaw = Mathf.LerpAngle(l.fromYaw, bodyYaw, e);
                l.groundNow = baseY;
                bob += Mathf.Sin(Mathf.PI * s) * 0.5f;
                if (s >= 1f)
                {   // touch-down: lock the foot, one plant event drives sound, footprints and dust
                    l.plant = l.aim; l.normal = Normal(g, l.plant, 0.35f * sc); l.planted = true; l.plantedFor = 0f; l.plantYaw = bodyYaw;
                    l.foot = l.plant + Vector3.up * l.ankleH; l.footYaw = bodyYaw;
                    l.stanceStart = l.plant; l.stanceMax = 0f; l.stanceOk = true;
                    l.stanceT = 0f; l.stanceYaw = bodyYaw; l.dragCm = 0f; l.residCm = 0f; l.softFrames = 0;
                    dip = 1f;
                    Plant(i, l.plant, l.normal);
                }
            }
            // ---- pelvis: follows the planted feet (their ground vs the ground under their home spots), drops if a leg would over-stretch
            float follow = 0f;
            for (int i = 0; i < 2; i++) { var l = legs[i]; follow += l.groundNow - g.GroundY(body.TransformPoint(l.homeLocal).x, body.TransformPoint(l.homeLocal).z); }
            follow *= 0.5f;
            float drop = 0f, top = float.PositiveInfinity;
            for (int i = 0; i < 2; i++)
            {
                var l = legs[i]; var hp = l.hip.position + Vector3.down * pelvisOff;              // the hip without this frame's offset
                float dh = new Vector2(hp.x - l.foot.x, hp.z - l.foot.z).magnitude, rk = 0.96f * l.len;
                if (dh < rk) drop = Mathf.Max(drop, (hp.y + follow - l.foot.y) - Mathf.Sqrt(rk * rk - dh * dh));
                if (!l.planted) continue;
                // shortens the leg (its soft zone starts at 0.96) and never pulls the drawn ankle off its spot. A hard bound: no smoothing lag.
                float kr = ReachKeep * Mathf.Min(l.len, l.a.magnitude * l.hip.lossyScale.x + l.b.magnitude * l.knee.lossyScale.x);
                if (dh < kr) top = Mathf.Min(top, l.foot.y + Mathf.Sqrt(kr * kr - dh * dh) - hp.y);
            }
            dip = Mathf.MoveTowards(dip, 0f, dt / 0.18f);
            float want = follow - Mathf.Clamp(drop, 0f, 0.4f * sc) + (BobAmp * bob * moving - PlantDip * Smooth(dip)) * sc;
            pelvisOff = Mathf.SmoothDamp(pelvisOff, want, ref pelvisVel, PelvisSmooth, Mathf.Infinity, dt);
            if (pelvisOff > top)
            {   // never deeper than MaxCrouch under the feet-follow height (past that the hurried swing / step takes over)
                pelvisOff = Mathf.Max(top, Mathf.Min(pelvisOff, follow - MaxCrouch * sc));
                if (pelvisVel > 0f) pelvisVel = 0f;
            }
            if (pelvis)
            {
                var up = pelvis.parent ? pelvis.parent.InverseTransformVector(Vector3.up * pelvisOff) : Vector3.up * pelvisOff;
                pelvis.localPosition = pelvisRest + up;
            }
            foreach (var l in legs) Solve(l, bodyYaw);
            foreach (var l in legs) if (l.planted) TrackStance(l, dt);
        }

        /// <summary>
        /// formula and uniform scales (the legs README's "look at hard" list); up to IkPasses passes re-aim it by the drawn ankle's miss,
        /// kept only while the miss shrinks (out of reach: the best pass stays), so a reachable locked foot is drawn on its spot.</summary>
        void Solve(Leg l, float bodyYaw)
        {
            var par = l.hip.parent; if (!par) return;
            var goal = l.foot; var aim = goal; var R = Pose(l, par, aim);
            float rollNeed = l.rollNeed;                                                            // the goal's own roll (step trigger)
            var miss = goal - l.ankle.position; float best = miss.sqrMagnitude; var bestAim = aim;
            for (int k = 1; k < IkPasses && best > 1e-6f; k++)                                      // 1 mm
            {
                aim += Vector3.ClampMagnitude(miss, 0.25f * l.len);
                R = Pose(l, par, aim);
                miss = goal - l.ankle.position; float e2 = miss.sqrMagnitude;
                if (e2 < best) { best = e2; bestAim = aim; continue; }
                R = Pose(l, par, bestAim); break;                                                   // worse: keep the best pass
            }
            l.rollNeed = rollNeed;
            // world: undo the hip roll on the plate, turn it to the ground normal (swing: eased from / to it), keep its locked yaw
            var rollW = par.rotation * R * Quaternion.Inverse(par.rotation);
            var n = l.planted ? l.normal : Vector3.Slerp(l.normal, Vector3.up, Mathf.Sin(Mathf.PI * Mathf.Clamp01(l.swingT)));
            var tilt = Quaternion.FromToRotation(par.up, n);
            var yaw = Quaternion.AngleAxis(Mathf.DeltaAngle(bodyYaw, l.footYaw), n);
            l.ankle.rotation = yaw * tilt * Quaternion.Inverse(rollW) * l.ankle.rotation;
        }

        /// <summary>The analytic two-bone pose to a world target (hip roll, hip / knee / ankle local rotations; the plate level with the
        /// body). Returns the hip roll; sets rollNeed (unclamped, deg) and soft (the target is in the soft-reach zone).</summary>
        Quaternion Pose(Leg l, Transform par, Vector3 target)
        {
            var t = par.InverseTransformPoint(target) - l.hip.localPosition;                       // parent space, from the hip pivot
            l.rollNeed = Mathf.Atan2(t.x - l.restLat, Mathf.Max(0.05f, -t.y)) * Mathf.Rad2Deg;
            float roll = Mathf.Clamp(l.rollNeed, -MaxRoll, MaxRoll);
            var R = Quaternion.AngleAxis(roll, Vector3.forward);
            var th = Quaternion.Inverse(l.hipRest) * (Quaternion.Inverse(R) * t);                   // hip rest space (the leg plane = its y / z)
            var hs = l.hip.localScale; if (hs.x > 1e-4f) th /= hs.x;
            var tg = new Vector2(th.z, th.y);
            float A = l.a.magnitude, B = l.b.magnitude, Lmax = A + B - 0.001f, dsoft = 0.96f * Lmax, dist = tg.magnitude;
            l.soft = dist > dsoft;
            if (dist > dsoft) dist = dsoft + (Lmax - dsoft) * (1f - Mathf.Exp(-(dist - dsoft) / Mathf.Max(1e-4f, Lmax - dsoft)));   // soft reach: no snap
            dist = Mathf.Max(dist, Mathf.Abs(A - B) + 0.001f);
            float elbow = Mathf.Acos(Mathf.Clamp((dist * dist - A * A - B * B) / (2 * A * B), -1f, 1f));
            float restBend = l.restB - l.restA, bend = restBend < 0 ? -elbow : elbow;              // the knee pole: always its rest side
            float angle = Mathf.Atan2(tg.y, tg.x) - Mathf.Atan2(B * Mathf.Sin(bend), A + B * Mathf.Cos(bend));
            float hipX = -(angle - l.restA) * sign * Mathf.Rad2Deg, kneeX = -(bend - restBend) * sign * Mathf.Rad2Deg;
            l.hip.localRotation = R * l.hipRest * Quaternion.AngleAxis(hipX, Vector3.right);
            l.knee.localRotation = l.kneeRest * Quaternion.AngleAxis(kneeX, Vector3.right);
            l.ankle.localRotation = l.ankleRest * Quaternion.AngleAxis(-hipX - kneeX, Vector3.right);   // plate level with the body
            return R;
        }

        void TrackStance(Leg l)
        {
            if (!l.stanceOk) return;
            var a = l.ankle.position; float dcm = new Vector2(a.x - l.stanceStart.x, a.z - l.stanceStart.z).magnitude * 100f;
            if (dcm > l.stanceMax) l.stanceMax = dcm;
        }

        /// <summary>
        /// spot, plus the parts for the log (the IK miss against the locked spot, soft-reach frames, the stance time).</summary>
        void TrackStance(Leg l, float dt)
        {
            if (!l.stanceOk) return;
            TrackStance(l);
            l.stanceT += dt;
            var a = l.ankle.position; l.residCm = Mathf.Max(l.residCm, new Vector2(a.x - l.plant.x, a.z - l.plant.z).magnitude * 100f);
            if (l.soft) l.softFrames++;
        }

        static float lastSlideLog = -9f;
        void LiftOff(Leg l)
        {
            if (!l.stanceOk) return;
            l.stanceOk = false; StanceN++; SlideSumCm += l.stanceMax; SlideMaxCm = Mathf.Max(SlideMaxCm, l.stanceMax);
            if (!On || l.stanceMax <= SlideWarnCm) return;
            SlideOverN++;
            if (!SlideLog || Time.unscaledTime - lastSlideLog < 0.25f) return;
            lastSlideLog = Time.unscaledTime;
            string why = l.dragCm >= 0.5f * l.stanceMax ? "over-stretch drag (the body outran the legs, or a push)"
                       : l.softFrames > 0 ? "soft reach (leg at full stretch)"
                       : Mathf.Abs(l.rollNeed) > MaxRoll ? "hip roll limit (turning)" : "IK miss";
            float turned = body ? Mathf.Abs(Mathf.DeltaAngle(l.stanceYaw, body.eulerAngles.y)) : 0f;
            Debug.Log($"[MechWalk] slide {l.stanceMax:0.0} cm > {SlideWarnCm:0.#} cm: unit {(view && view.Obj != null ? view.Obj.Id : -1)} {(l.left ? "left" : "right")} foot, " +
                      $"stance {l.stanceT:0.00} s, turned {turned:0} deg, speed {vel.magnitude:0.00} m/s, drag {l.dragCm:0.0} cm, IK miss max {l.residCm:0.0} cm, " +
                      $"soft-reach frames {l.softFrames}, roll {l.rollNeed:0} deg -> {why}");
        }

        void Plant(int i, Vector3 at, Vector3 n)
        {
            Plants++;
            if (view && view.Obj != null)
            {
                if (LegacyEngineAudio.I) LegacyEngineAudio.I.MechPlant(view, i, at);
                if (TrackMarks.I) TrackMarks.I.MechPlant(view, i == 0, at);
            }
            Planted?.Invoke(this, i, at, n);
        }

        // ================================================================================================ legacy (A/B only)
        /// <summary>
        /// statement for statement (the slide probe only reads), and the before numbers (slide per stance).</summary>
        void Legacy(float dt, float step)
        {
            moving = Mathf.MoveTowards(moving, step > 0.0005f ? 1f : 0f, dt * 4f);
            float time = travel * 1.6f;
            foreach (var l in legs)
            {
                float phase = time * 2f + (l.left ? 0f : Mathf.PI), lift = Mathf.Max(0f, Mathf.Sin(phase)) * 0.33f * moving;
                var target = l.target; target.y += lift; target.x += Mathf.Sin(phase) * 0.20f * moving;
                float A = l.a.magnitude, B = l.b.magnitude, dist = Mathf.Clamp(target.magnitude, Mathf.Abs(A - B) + 0.001f, A + B - 0.001f);
                float elbow = Mathf.Acos(Mathf.Clamp((dist * dist - A * A - B * B) / (2 * A * B), -1f, 1f));
                float restBend = l.restB - l.restA, bend = restBend < 0 ? -elbow : elbow;
                float angle = Mathf.Atan2(target.y, target.x) - Mathf.Atan2(B * Mathf.Sin(bend), A + B * Mathf.Cos(bend));
                float hipX = -(angle - l.restA) * sign * Mathf.Rad2Deg, kneeX = -(bend - restBend) * sign * Mathf.Rad2Deg;
                l.hip.localRotation = l.hipRest * Quaternion.AngleAxis(hipX, Vector3.right);
                l.knee.localRotation = l.kneeRest * Quaternion.AngleAxis(kneeX, Vector3.right);
                l.ankle.localRotation = l.ankleRest * Quaternion.AngleAxis(-hipX - kneeX, Vector3.right);   // foot stays level
                // slide probe: a stance = the foot's lift is 0 (sin(phase) <= 0) while walking
                bool low = Mathf.Sin(phase) <= 0f && moving > 0.5f;
                if (low && !l.legacyLow) { l.stanceStart = l.ankle.position; l.stanceMax = 0f; l.stanceOk = true; }
                if (low) TrackStance(l);
                if (!low && l.legacyLow) LiftOff(l);
                l.legacyLow = low;
            }
            if (pelvis) pelvis.localPosition = pelvisRest + Vector3.up * Mathf.Abs(Mathf.Sin(age * 5.2f)) * 0.12f * moving / Mathf.Max(0.001f, pelvis.lossyScale.y);
        }
    }
}
