using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// View-only hull yaw for vehicles; the Zod sim, its positions and its Dx/Dy stay untouched.
    /// <list type="bullet">
    /// <item>The hull follows the PATH, never the raw per-tick velocity: the heading is the direction to a carrot
    /// <see cref="LookaheadPx"/> ahead along the sim's own remaining path (current target + A* points), low-pass filtered.
    /// Zod's 8-direction staircase legs, attack re-aims, the pathing layer's sidesteps / re-centres / pushes and one-tick
    /// velocity flips average out.</item>
    /// <item>Dead zone <see cref="DeadZone"/>: a heading change below it never turns the hull.</item>
    /// <item>Hysteresis: a new heading must persist <see cref="HoldMin"/> s before the hull commits to it.</item>
    /// <item>Commit window <see cref="Commit"/> s: once committed the hull keeps that manoeuvre, unless a new order, a
    /// release from a block (re-route) or a destroyed target opens it at once, or the smoothed path really turned
    /// (&gt;= <see cref="CornerBreak"/> deg for HoldMin s: a real corner; the tank never drives sideways round it).</item>
    /// <item>Rate-limited, eased turn (SmoothDampAngle: ease in and out, capped at the unit's hull rate).</item>
    /// <item>Short moves (Zod missile dodges, pathing step-asides / back-offs, &lt; <see cref="ShortMovePx"/> of path):
    /// a move more than <see cref="ReverseOn"/> deg behind the hull backs up instead of turning round, and the hull turns
    /// at most <see cref="ShortTurnMax"/> deg toward a short move (no left/right flicking under fire).</item>
    /// <item>Standing: the hull never moves while the turret tracks; only a target outside the turret arc
    /// (<see cref="TurretArcFor"/>; every current OpenZ turret turns 360) turns the hull toward it.</item>
    /// </list>
    /// this exact file.
    /// </summary>
    public sealed class HullSteer
    {
        // ------------------------------------------------------------------ tuning (static: one set for every vehicle)
        /// <summary>Heading changes smaller than this (deg) never turn the hull.</summary>
        public static float DeadZone = 9f;
        /// <summary>A new heading must hold this long (s) before the hull commits to it (hysteresis).</summary>
        public static float HoldMin = 0.35f;
        /// <summary>Manoeuvre commit (s): no re-targeting of the hull inside it except new order / unblocked / target gone.</summary>
        public static float Commit = 1.4f;
        /// <summary>A persistent path turn at least this large (deg) is a real corner and may end the commit early.</summary>
        public static float CornerBreak = 50f;
        /// <summary>Carrot distance along the sim path (px; 8 px = 1 m): 28 px = 3.5 m.</summary>
        public static float LookaheadPx = 28f;
        /// <summary>Low-pass time constant of the path direction (s).</summary>
        public static float HeadingTau = 0.15f;
        /// <summary>Remaining path below this (px) is a short move (dodge, step-aside, back-off, last nudge): 28 px = 3.5 m.</summary>
        public static float ShortMovePx = 28f;
        /// <summary>A short move more than this far (deg) behind the hull reverses; it keeps reversing until below ReverseOff.</summary>
        public static float ReverseOn = 110f, ReverseOff = 70f;
        /// <summary>Largest hull turn (deg) toward a short move.</summary>
        public static float ShortTurnMax = 35f;
        /// <summary>Seconds after a new order / an unblock / a destroyed target during which commits are immediate.</summary>
        public static float ForceWindow = 0.6f;
        /// <summary>Ease of the hull turn (SmoothDampAngle time, s): tanks, jeep.</summary>
        public static float TurnSmooth = 0.28f, TurnSmoothJeep = 0.14f;
        /// <summary>Turn-rate deceleration (deg/s^2) when the vehicle stops: the hull settles instead of finishing a turn in place.</summary>
        public static float StopDecel = 900f;

        /// <summary>Turret arc (full width, deg) inside which a standing vehicle keeps its hull still while it fires.</summary>
        public static float TurretArcFor(string type) => 360f;   // tanks, mech waist, jeep gun and MLRS rack all traverse fully

        // ------------------------------------------------------------------ state
        float hull, goal, cand, candT, commitT = 99f, rate, forceT = 99f;
        float hx, hy; bool hInit;                                                   // filtered path direction (px space, y down)
        bool reverse, engagedTurn;
        int wpSig; bool wpInit, wasHeld; ZObject lastTarget;
        int duelSeq; bool duelInit;                                                 // vehicle_combat_v1: the sim's duel manoeuvre (DV9)

        public float Yaw => hull;
        public float Goal => goal;
        /// <summary>True while the vehicle backs up along its hull axis (short move behind it).</summary>
        public bool Reversing => reverse;
        /// <summary>Stats for the harness / Play check: hull commits, commits forced by events.</summary>
        public int Commits { get; private set; }
        public int ForcedCommits { get; private set; }

        public HullSteer(float yaw) { hull = goal = cand = yaw; }

        /// <summary>vehicles_nb_v1 NB21/22: the drawn hull (VehicleSlip) handed back while the vehicle stands, so the committed
        /// heading is where the hull really ended (a standing pivot, an eased stop): no turn back to an older heading.</summary>
        public void Adopt(float yaw) { hull = goal = cand = Mathf.Repeat(yaw + 180f, 360f) - 180f; rate = 0f; candT = 0f; }

        /// <summary>One frame. moving = the sim really moves the unit (UnitView: IsMoving and not PathHeld); aimYaw = world yaw
        /// toward the attack target (NaN = none); maxRate = hull turn cap (deg/s). Returns the hull yaw (deg, UnitView space).</summary>
        public float Step(ZObject o, bool moving, float aimYaw, float maxRate, bool jeep, float dt)
        {
            if (dt <= 0f) return hull;
            commitT += dt; forceT += dt;
            if (Observe(o)) { forceT = 0f; }
            bool forced = forceT < ForceWindow;

            if (moving && PathHeading(o, LookaheadPx, out float px, out float py, out float remain))
            {
                engagedTurn = false;
                float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, HeadingTau));
                if (!hInit || forced) { hx = px; hy = py; hInit = true; }
                else { hx += (px - hx) * k; hy += (py - hy) * k; }
                float want = Mathf.Atan2(hx, -hy) * Mathf.Rad2Deg;                          // px (x right, y down) -> world yaw
                // vehicle_combat_v1 (DV9): a duel manoeuvre (arc / flank / close, committed 1-2 s by the sim) is a real move the hull
                // follows fully although its steering point is only 3 m ahead; a manoeuvre behind the hull (the reverse, or an arc
                // right after it) backs up instead of turning round; the reverse keeps the hull on the target (short-move clamp)
                var duelKind = o.DuelKind;
                bool shortMove = duelKind == ZDuelKind.None ? remain < ShortMovePx || IsMinorMove(o) : duelKind == ZDuelKind.Reverse;
                float off = Mathf.Abs(Mathf.DeltaAngle(hull, want));
                reverse = (shortMove || duelKind != ZDuelKind.None) && (reverse ? off > ReverseOff : off > ReverseOn);
                if (reverse) want = Mathf.Repeat(want + 180f, 360f);
                if (shortMove)
                {   // a short move turns the hull only a little toward it (forward or reverse axis, whichever is nearer)
                    float d0 = Mathf.DeltaAngle(goal, want);
                    want = goal + Mathf.Clamp(d0, -ShortTurnMax, ShortTurnMax);
                }
                Consider(want, dt, forced, shortMove);
            }
            else if (!moving && !float.IsNaN(aimYaw))
            {   // standing and firing: the turret tracks, the hull stays unless the target leaves the turret arc
                reverse = false; hInit = false;
                float half = TurretArcFor(o.Type) * 0.5f;
                float off = Mathf.Abs(Mathf.DeltaAngle(goal, aimYaw));
                if (half < 180f && (engagedTurn ? off > 5f : off > half + 10f))
                {
                    engagedTurn = off > 5f;
                    if (Mathf.Abs(Mathf.DeltaAngle(goal, aimYaw)) > DeadZone) { goal = aimYaw; commitT = 0f; Commits++; }
                }
                candT = 0f;
            }
            else { reverse = false; hInit = false; candT = 0f; engagedTurn = false; }

            if (!moving && !engagedTurn)
            {   // standing (stopped, held in traffic, firing inside the turret arc): the hull stops turning, with a short ease-out
                rate = Mathf.MoveTowards(rate, 0f, StopDecel * dt);
                hull += rate * dt; goal = cand = hull;
                hull = Mathf.Repeat(hull + 180f, 360f) - 180f;
                return hull;
            }
            hull = Mathf.SmoothDampAngle(hull, goal, ref rate, jeep ? TurnSmoothJeep : TurnSmooth, maxRate, dt);
            if (Mathf.Abs(Mathf.DeltaAngle(hull, goal)) < 0.05f && Mathf.Abs(rate) < 0.5f) { hull = goal; rate = 0f; }
            hull = Mathf.Repeat(hull + 180f, 360f) - 180f;
            return hull;
        }

        void Consider(float want, float dt, bool forced, bool shortMove)
        {
            float d = Mathf.DeltaAngle(goal, want);
            if (Mathf.Abs(d) <= DeadZone) { cand = goal; candT = 0f; return; }           // dead zone: keep the committed heading
            if (Mathf.Abs(Mathf.DeltaAngle(cand, want)) > DeadZone) { cand = want; candT = 0f; }   // a different candidate: start over
            else { cand = want; candT += dt; }                                           // same candidate (tracks slow drift)
            bool corner = !shortMove && Mathf.Abs(d) >= CornerBreak && candT >= HoldMin;
            bool ready = forced || (candT >= HoldMin && (commitT >= Commit || corner));
            if (!ready) return;
            goal = cand; candT = 0f; commitT = 0f; Commits++; if (forced) ForcedCommits++;
        }

        /// <summary>True on a frame where a committed manoeuvre may be dropped at once: a new (non-minor) first waypoint = a new
        /// order, a release from a pathing hold (blocked -> re-route), or the attack target destroyed.</summary>
        bool Observe(ZObject o)
        {
            bool force = false;
            int sig = 0;
            if (o.Waypoints.Count > 0)
            {
                var w = o.Waypoints[0];
                sig = (((int)w.mode + 3) * 73856093) ^ (w.refId * 19349663) ^ (w.x * 83492791) ^ (w.y * 2654435) | 1;
                if (wpInit && sig != wpSig && !IsMinorMove(o)) force = true;
            }
            wpSig = sig; wpInit = true;
            bool held = o.PathHeld;
            if (wasHeld && !held && o.IsMoving) force = true;                              // blocked, now re-routed / free again
            wasHeld = held;
            if (lastTarget != null && (lastTarget.Destroyed || lastTarget.KillAt >= 0)) force = true;
            lastTarget = o.AttackObject;
            int ds = o.DuelSeq;                                                        // vehicle_combat_v1: a new duel manoeuvre (the sim already
            if (duelInit && ds != duelSeq && o.DuelKind != ZDuelKind.None) force = true;   // holds each one 1-2 s) is taken at once, no extra lag
            duelSeq = ds; duelInit = true;
            if (lastTarget != null && (lastTarget.Destroyed || lastTarget.KillAt >= 0)) lastTarget = null;
            return force;
        }

        /// <summary>Zod missile dodge and the pathing layer's helper moves (step aside, back off, regroup).</summary>
        static bool IsMinorMove(ZObject o)
        {
            if (o.Waypoints.Count == 0) return false;
            var w = o.Waypoints[0];
            return w.mode == WpMode.Dodge || (w.mode == WpMode.Move && w.refId <= ZPathing.RefStepAside && w.refId >= ZPathing.RefRegroup);
        }

        /// <summary>Unit direction (px space) from the unit's centre to a carrot lookPx ahead along its remaining sim path
        /// (current target, then the A* points); remain = remaining path length (px). False = no path to follow.</summary>
        public static bool PathHeading(ZObject o, float lookPx, out float dx, out float dy, out float remain)
        {
            dx = dy = remain = 0f;
            double x0 = o.PosX + o.WidthPix * 0.5, y0 = o.PosY + o.HeightPix * 0.5;
            double cx = x0, cy = y0, left = lookPx, carX = x0, carY = y0, total = 0; bool carDone = false;
            var t = o.CurrentTarget;
            Walk(t.x, t.y, ref cx, ref cy, ref left, ref carX, ref carY, ref total, ref carDone);
            var pts = o.PathPoints;
            if (pts != null) for (int i = 0; i < pts.Count; i++) Walk(pts[i].x, pts[i].y, ref cx, ref cy, ref left, ref carX, ref carY, ref total, ref carDone);
            remain = (float)total;
            double vx = carX - x0, vy = carY - y0, l = System.Math.Sqrt(vx * vx + vy * vy);
            if (l < 1.0)
            {   // on (or within a pixel of) the last point: fall back to the sim velocity
                double vl = System.Math.Sqrt((double)o.Dx * o.Dx + (double)o.Dy * o.Dy);
                if (vl < 1e-4) return false;
                dx = (float)(o.Dx / vl); dy = (float)(o.Dy / vl); return true;
            }
            dx = (float)(vx / l); dy = (float)(vy / l); return true;
        }

        static void Walk(int px, int py, ref double cx, ref double cy, ref double left, ref double carX, ref double carY, ref double total, ref bool carDone)
        {
            double sx = px - cx, sy = py - cy, sl = System.Math.Sqrt(sx * sx + sy * sy);
            total += sl;
            if (!carDone)
            {
                if (sl >= left && sl > 1e-9) { carX = cx + sx / sl * left; carY = cy + sy / sl * left; carDone = true; }
                else { left -= sl; carX = px; carY = py; }
            }
            cx = px; cy = py;
        }
    }
}
