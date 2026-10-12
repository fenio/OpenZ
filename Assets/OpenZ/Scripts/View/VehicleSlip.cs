using System;

namespace OpenZ
{
    /// <summary>
    /// VIEW ONLY: the sim position is never written.
    /// <para>Cause: the drawn vehicle sat exactly on its sim point (+ view spacing) while its hull followed HullSteer's smoothed,
    /// committed path heading (dead zone 9 deg, 0.35 s hold, 1.4 s commit, short moves clamped to 35 deg, standing = hull still).
    /// Every sim move that is not along that heading was drawn as a sideways glide: Zod's 8-direction staircase legs, duel
    /// manoeuvres (DV9 flank / arc / reverse) during the hull's turn, missile dodges and pathing step-asides beyond the 35 deg clamp,
    /// sim spacing pushes and the view spacing (sepOffset, up to 2 m/s) while standing, and the jeep's view weave (+-0.22 m
    /// sideways, never steered). motion_v1's probe measured the hull 7.9-16 deg off the travel direction on average.</para>
    /// <para>Fix: the drawn vehicle is a non-holonomic follower of its sim point P. Each frame the offset P - V is split along the
    /// hull: the part along the hull is driven (capped at the sim speed x CatchK + CatchAdd, so a lag is caught up smoothly, never
    /// teleported); sideways it may only move tan(SlipMax) x the distance driven (a real vehicle's tyre / track slip: it drives
    /// diagonally only by steering). The sideways part that remains is a lag the hull steers into: while moving, pure pursuit
    /// (the hull turns atan2(lag, Look + ahead) toward the sim path, eased, at most MaxCorr); standing, a tracked vehicle pivots in
    /// place toward a push beyond Stand m and drives there (tanks may pivot), a wheeled one (jeep, APC, crane) never turns on
    /// the spot: its yaw rate is capped by speed / Rmin (a car steers, it does not rotate in place). Beyond Lmax sideways (and
    /// LagMax in total) the rest slides (counted: ForcedSlipM), and a teleport over SnapM snaps (spawns, exits).</para>
    /// </summary>
    public sealed class VehicleSlip
    {
        /// <summary>Off = the old behaviour (the drawn vehicle on its sim point, the hull on HullSteer's heading).</summary>
        public static bool Enabled = true;
        public static float SnapM = 6f, LagMax = 2.6f, CatchK = 1.35f, CatchAdd = 0.9f, MaxCorr = 38f, CorrTau = 0.12f, StandRelease = 0.08f;
        /// <summary>H13: heading offsets (deg) below this follow the sim's real motion (fading out over the next 4 deg).</summary>
        public static float SmallFollow = 10f;
        /// <summary>
        /// a wheeled vehicle whose sim point turns back behind it (more than UTurnDeg off the hull) drives a K-turn instead of
        /// sliding: it backs up on a steered arc (radius Rmin, yaw = distance / Rmin) until the new heading is UTurnSwitch off,
        /// then drives forward on the same tight arc onto the new heading. Pure rolling (no sideways step, no Lmax slide); it ends
        /// on the sim's path about 2 Rmin behind where it turned, the sim point ahead, and the normal follower catches up.
        /// UTurn = false: the old behaviour.</summary>
        public static bool UTurn = true;
        public static float UTurnDeg = 110f, UTurnSwitch = 92f, UTurnDone = 10f, UTurnMaxS = 6f, UTurnSpeedK = 1.15f;

        public sealed class Chassis
        {
            public string type; public bool pivot; public float rate, slipMax, look, stand, lmax, rmin, tau, accel;
        }

        /// <summary>Per chassis: pivot (tracks), hull turn cap (deg/s), SlipMax (deg), pursuit Look (m), Stand push that makes a tracked
        /// vehicle reposition (m), Lmax sideways lag before it slides (m), Rmin (m, wheeled: yaw rate &lt;= speed / Rmin), controller
        /// tau (s) and angular acceleration (deg/s^2): the smooth turn per chassis (heavy slowest).</summary>
        public static Chassis Of(string type)
        {
            type ??= "";
            if (!chassis.TryGetValue(type, out var c)) chassis[type] = c = Make(type);
            return c;
        }
        static readonly System.Collections.Generic.Dictionary<string, Chassis> chassis = new System.Collections.Generic.Dictionary<string, Chassis>();
        static Chassis Make(string type)
        {
            switch (type)
            {
                case "jeep": return new Chassis { type = type, pivot = false, rate = 380f, slipMax = 5f, look = 3.2f, stand = 0.5f, lmax = 1.6f, rmin = 0.8f, tau = 0.05f, accel = 2400f };
                case "light": return new Chassis { type = type, pivot = true, rate = 150f, slipMax = 7f, look = 3.2f, stand = 0.35f, lmax = 1.5f, rmin = 0f, tau = 0.06f, accel = 1400f };
                case "medium": return new Chassis { type = type, pivot = true, rate = 140f, slipMax = 9f, look = 3.2f, stand = 0.35f, lmax = 1.5f, rmin = 0f, tau = 0.07f, accel = 1200f };   // the mech walks: a little more side step
                case "heavy": return new Chassis { type = type, pivot = true, rate = 110f, slipMax = 6f, look = 3.4f, stand = 0.35f, lmax = 1.7f, rmin = 0f, tau = 0.08f, accel = 900f };
                case "missile_launcher": return new Chassis { type = type, pivot = true, rate = 120f, slipMax = 6f, look = 3.4f, stand = 0.35f, lmax = 1.7f, rmin = 0f, tau = 0.08f, accel = 1000f };
                case "apc": return new Chassis { type = type, pivot = false, rate = 220f, slipMax = 5f, look = 3.6f, stand = 0.5f, lmax = 1.5f, rmin = 1.6f, tau = 0.06f, accel = 1600f };
                case "crane": return new Chassis { type = type, pivot = false, rate = 180f, slipMax = 5f, look = 3.8f, stand = 0.5f, lmax = 1.5f, rmin = 1.8f, tau = 0.07f, accel = 1300f };
                default: return new Chassis { type = type, pivot = true, rate = 150f, slipMax = 7f, look = 3.2f, stand = 0.35f, lmax = 1.5f, rmin = 0f, tau = 0.06f, accel = 1400f };
            }
        }

        /// <summary>UnitView.HullRate for vehicles (deg/s): the chassis turn cap HullSteer uses too.</summary>
        public static float RateOf(string type) => Of(type).rate;

        public readonly Chassis C;
        public float X, Z, Yaw;                       // drawn position (world x / z, m) and hull yaw (deg, UnitView space: front = (sin, cos))
        float yawRate, corr, reposYaw; bool init, repos;
        // ------------------------------------------------------------------ stats (harness / Play proof)
        /// <summary>Last frame: angle (deg) between the drawn motion and the hull axis (forward or reverse, the nearer), and the drawn speed (m/s).</summary>
        public float Sideslip { get; private set; }
        public float DrawnSpeed { get; private set; }
        /// <summary>Last frame: drawn speed along the hull (m/s, + forward, - backing up): VehicleMotion rolls the wheels / tracks with it.</summary>
        public float AlongSpeed { get; private set; }
        /// <summary>Distance (m) between the drawn vehicle and its sim point, last frame.</summary>
        public float LagM { get; private set; }
        /// <summary>Metres slid sideways because the lag hit Lmax / LagMax (the only sideways glides left), and snaps.</summary>
        public float ForcedSlipM { get; private set; }
        public int Snaps { get; private set; }
        /// <summary>Tracked vehicles: standing repositions (pivot + drive) started.</summary>
        public int Repositions { get; private set; }
        public bool Repositioning => repos;
        /// <summary></summary>
        public int UTurns { get; private set; }
        public int UTurnPhase => uPhase;
        int uPhase, uDir; float uT, uV;

        public VehicleSlip(Chassis c) { C = c; }

        static float Rad(float d) => d * (MathF.PI / 180f);
        static float Deg(float r) => r * (180f / MathF.PI);
        static float Delta(float c, float t) { float d = (t - c) % 360f; if (d > 180f) d -= 360f; if (d < -180f) d += 360f; return d; }
        static float Wrap(float a) { a %= 360f; if (a > 180f) a -= 360f; if (a <= -180f) a += 360f; return a; }
        static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        static float Toward(float c, float t, float md) => MathF.Abs(t - c) <= md ? t : c + MathF.Sign(t - c) * md;

        public void Reset(float x, float z, float yaw) { X = x; Z = z; Yaw = Wrap(yaw); yawRate = 0f; corr = 0f; repos = false; init = true; uPhase = 0; }

        /// <summary>DV79 unmanned stop (nobody inside): the drawn vehicle stands exactly where it is drawn, from this frame. No
        /// catch-up drive, no pivot onto its sim point, no K-turn, no turn; wheels / tracks stop (AlongSpeed 0). The offset to the
        /// sim point (LagM) stays and is driven off by the next crew like any standing lag. The first frame and a teleport over
        /// SnapM still snap.</summary>
        public void Hold(float px, float pz, float intentYaw)
        {
            float dx = px - X, dz = pz - Z, dist = MathF.Sqrt(dx * dx + dz * dz);
            if (!init) { Reset(px, pz, intentYaw); dist = 0f; }
            else if (dist > SnapM) { Reset(px, pz, intentYaw); Snaps++; dist = 0f; }
            yawRate = 0f; corr = 0f; repos = false; uPhase = 0;
            Sideslip = 0f; DrawnSpeed = 0f; AlongSpeed = 0f; LagM = dist;
        }

        /// <summary>
        /// false when no K-turn runs (start / end tested here); true = the pose was moved (pure rolling on the Rmin arc).</summary>
        bool KTurn(float dx, float dz, float dist, bool moving, float simSpeed, float simYaw, float intentYaw, bool reversing, float dt)
        {
            if (!UTurn || C.pivot) { uPhase = 0; return false; }
            float hd = float.IsNaN(simYaw) ? intentYaw : simYaw;
            float err = Delta(Yaw, hd);
            if (uPhase == 0)
            {
                if (!moving || reversing || MathF.Abs(err) < UTurnDeg) return false;
                float h0 = Rad(Yaw), along0 = dx * MathF.Sin(h0) + dz * MathF.Cos(h0);
                if (along0 > 0.5f) return false;                                                   // the sim point is still ahead: normal steering
                float side0 = dx * MathF.Cos(h0) - dz * MathF.Sin(h0);
                // backing up with the yaw rising swings the vehicle to its left (and v.v.): swing away from nothing, toward the point
                uDir = MathF.Abs(err) < 170f ? (err > 0f ? 1 : -1) : (side0 > 0.05f ? -1 : side0 < -0.05f ? 1 : 1);
                uPhase = 1; uT = 0f; uV = MathF.Max(1.2f, simSpeed); UTurns++;
            }
            uT += dt;
            if (moving) uV = MathF.Max(1.2f, simSpeed);
            if (uT > UTurnMaxS || !moving) { uPhase = 0; return false; }                            // the point stopped (a dodge step): normal follow
            if (uPhase == 1 && MathF.Abs(err) <= UTurnSwitch) uPhase = 2;
            if (uPhase == 2 && MathF.Abs(err) <= UTurnDone) { uPhase = 0; corr = 0f; yawRate = 0f; return false; }
            float ds = uV * UTurnSpeedK * dt;                                                        // a touch quicker than the sim point
            float r = MathF.Max(0.3f, C.rmin);
            float turn = Deg(ds / r) * (uPhase == 1 ? uDir : MathF.Sign(err));
            if (uPhase == 2 && MathF.Abs(turn) > MathF.Abs(err)) turn = err;
            float y0 = Yaw; Yaw = Wrap(Yaw + turn * 0.5f);                                          // midpoint heading for the step
            float h = Rad(Yaw), sgn = uPhase == 1 ? -1f : 1f;
            float sx = MathF.Sin(h) * ds * sgn, sz = MathF.Cos(h) * ds * sgn;
            Yaw = Wrap(y0 + turn);
            X += sx; Z += sz;
            yawRate = turn / dt; corr = 0f; repos = false;
            DrawnSpeed = ds / dt; AlongSpeed = sgn * ds / dt; Sideslip = 0f;
            float lx = dx - sx, lz = dz - sz; LagM = MathF.Sqrt(lx * lx + lz * lz);
            return true;
        }

        /// <summary>One frame. px / pz = the vehicle's sim point (+ view spacing, + the jeep's weave); intentYaw = HullSteer's hull
        /// (deg); simSpeed = the sim's speed (m/s, 0 when standing / held); reversing = HullSteer backs up; dt = frame time (s).
        /// Afterwards X / Z / Yaw are the drawn pose. Returns true when the caller should hand Yaw back to HullSteer (standing).</summary>
        public bool Step(float px, float pz, float intentYaw, float simSpeed, bool reversing, float dt) => Step(px, pz, intentYaw, simSpeed, float.NaN, reversing, dt);

        /// <summary>simYaw = the direction the sim point really moves (deg, NaN = unknown): when the intended heading is evidently
        /// wrong for this move (the sideways lag keeps growing past half of Lmax), the pursuit blends over to it.</summary>
        public bool Step(float px, float pz, float intentYaw, float simSpeed, float simYaw, bool reversing, float dt)
        {
            if (!init) { Reset(px, pz, intentYaw); return true; }
            if (dt <= 0f) return false;
            float dx = px - X, dz = pz - Z, dist = MathF.Sqrt(dx * dx + dz * dz);
            if (dist > SnapM) { Reset(px, pz, intentYaw); Snaps++; Sideslip = 0f; DrawnSpeed = 0f; AlongSpeed = 0f; LagM = 0f; return true; }
            bool moving = simSpeed > 0.05f;
            if (KTurn(dx, dz, dist, moving, simSpeed, simYaw, intentYaw, reversing, dt)) return false;

            // ---- 1. where the hull should point
            float target;
            if (moving)
            {
                repos = false;
                float fi = Rad(intentYaw), fx = MathF.Sin(fi), fz = MathF.Cos(fi);
                float b = dx * fz - dz * fx;                                                        // sideways lag, right of the intended heading
                // a sideways lag that keeps growing (the intended heading is wrong for this move): the pursuit follows the sim's own
                // motion and may turn the hull up to 90 deg
                float grow = Clamp((MathF.Abs(b) - 0.5f * C.lmax) / (0.5f * C.lmax), 0f, 1f);
                float mh = reversing ? Wrap(intentYaw + 180f) : intentYaw;                          // the direction the vehicle drives
                // real motion (APC 82 vs 88.5 deg for seconds), so the drawn vehicle crabbed at SlipMax the whole leg. A small offset
                // (< SmallFollow) now follows the sim's own motion; big ones (Zod's 45 deg staircase legs) keep HullSteer's smoothing.
                float small = float.IsNaN(simYaw) ? 0f : Clamp((SmallFollow + 4f - MathF.Abs(Delta(mh, simYaw))) / 4f, 0f, 1f);
                if (!float.IsNaN(simYaw)) mh = Wrap(mh + Delta(mh, simYaw) * MathF.Max(grow, small));
                float cx = dx + MathF.Sin(Rad(mh)) * C.look, cz = dz + MathF.Cos(Rad(mh)) * C.look;     // pure pursuit: a carrot Look m ahead of P
                float drive = Deg(MathF.Atan2(cx, cz));
                float want = Delta(intentYaw, reversing ? Wrap(drive + 180f) : drive);
                float lim = MaxCorr + (90f - MaxCorr) * grow;
                want = Clamp(want, -lim, lim);
                corr += (want - corr) * (1f - MathF.Exp(-dt / MathF.Max(0.01f, CorrTau)));        // eased pursuit correction
                target = intentYaw + corr;
            }
            else
            {
                corr = 0f;
                float to = Deg(MathF.Atan2(dx, dz));
                if (C.pivot && !repos && dist > C.stand)
                {   // tracks pivot in place toward the push (forward or reverse axis, the nearer), then drive onto the spot
                    float back = Wrap(to + 180f);
                    reposYaw = MathF.Abs(Delta(Yaw, to)) <= MathF.Abs(Delta(Yaw, back)) ? to : back;
                    repos = true; Repositions++;
                }
                else if (repos)
                {   // the axis is kept (no re-aiming at every centimetre); a push that swung far off it re-aims; done on the spot
                    float h0 = Rad(reposYaw), along0 = dx * MathF.Sin(h0) + dz * MathF.Cos(h0);
                    float off = MathF.Abs(Delta(reposYaw, to)); off = MathF.Min(off, 180f - off);
                    if (dist < StandRelease || MathF.Abs(along0) < StandRelease) repos = false;
                    else if (off > 40f && dist > C.stand) { float back = Wrap(to + 180f); reposYaw = MathF.Abs(Delta(Yaw, to)) <= MathF.Abs(Delta(Yaw, back)) ? to : back; }
                }
                target = repos ? reposYaw : Yaw;                                                  // standing: the hull holds (eases out)
            }

            // ---- 2. turn: eased (rate follows the error / tau, accelerating at `accel`), capped per chassis; wheels: speed / Rmin
            float cap = C.rate;
            if (!C.pivot) cap = MathF.Min(cap, Deg(MathF.Max(0f, MathF.Abs(simSpeed)) / MathF.Max(0.1f, C.rmin)));
            float err = Delta(Yaw, target);
            float wantRate = Clamp(err / MathF.Max(0.01f, C.tau), -cap, cap);
            yawRate = Toward(yawRate, wantRate, C.accel * dt);
            yawRate = Clamp(yawRate, -cap, cap);
            float turn = yawRate * dt;
            if (MathF.Abs(turn) > MathF.Abs(err) && MathF.Sign(turn) == MathF.Sign(err)) { turn = err; yawRate = 0f; }   // no overshoot
            Yaw = Wrap(Yaw + turn);

            // ---- 3. move: along the hull (catch-up capped), sideways only tan(SlipMax) x the distance driven
            float h = Rad(Yaw), hx = MathF.Sin(h), hz = MathF.Cos(h);
            float along = dx * hx + dz * hz, side = dx * hz - dz * hx;
            float vcap = (MathF.Max(0f, simSpeed) * CatchK + CatchAdd) * dt;
            float stepA = Clamp(along, -vcap, vcap);
            if (!moving && !repos && C.pivot) stepA = 0f;                                         // a standing tank inside Stand: holds (no creep)
            if (repos && MathF.Abs(Delta(Yaw, reposYaw)) > 15f) stepA = 0f;                       // repositioning: pivot first, then drive
            float allowB = MathF.Tan(Rad(C.slipMax)) * MathF.Abs(stepA);
            float stepB = Clamp(side, -allowB, allowB);
            float remB = side - stepB, forced = 0f;
            if (MathF.Abs(remB) > C.lmax) { float ex = remB - MathF.Sign(remB) * C.lmax; stepB += ex; forced += MathF.Abs(ex); }
            float remA = along - stepA;
            if (MathF.Abs(remA) > LagMax) { float ex = remA - MathF.Sign(remA) * LagMax; stepA += ex; }
            float sx = hx * stepA + hz * stepB, sz = hz * stepA - hx * stepB;
            X += sx; Z += sz;
            ForcedSlipM += forced;

            // ---- stats
            float sm = MathF.Sqrt(sx * sx + sz * sz);
            DrawnSpeed = sm / dt; AlongSpeed = stepA / dt;
            if (sm > 1e-5f)
            {
                float ang = MathF.Abs(Delta(Yaw, Deg(MathF.Atan2(sx, sz))));
                Sideslip = MathF.Min(ang, 180f - ang);
            }
            else Sideslip = 0f;
            float lx = px - X, lz = pz - Z; LagM = MathF.Sqrt(lx * lx + lz * lz);
            return !moving;
        }
    }
}
