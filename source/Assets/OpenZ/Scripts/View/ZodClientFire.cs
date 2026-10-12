namespace OpenZ
{
    /// <summary>
    /// The Zod client's fire-animation clock (rgrunt.cpp:151-176, rsniper.cpp:151-175, rpsycho.cpp:151-172, rpyro.cpp:151-217,
    /// rlaser.cpp:151-217, vjeep.cpp:229-264, cgatling.cpp:185-223). While a unit attacks, the client steps its fire sprite on
    /// its own random timer and draws a bullet (EBullet / EFlame / ELaser) and plays the fire sound only on the fire frame.
    /// It does not follow the server's damage rolls: a Z grunt shows one bullet a second for two damage rolls a second.
    /// Damage stays on the sim; this only decides when a shot is drawn and heard. Pure C#, measured by the parity suite.
    /// </summary>
    public sealed class ZodClientFire
    {
        public enum Kind { PerRoll, Rifle, Sniper, Toggle, Pyro, Laser }

        public static Kind KindOf(string type)
        {
            switch (type)
            {
                case "grunt": return Kind.Rifle;                                      // rgrunt.cpp:151-176
                case "sniper": return Kind.Sniper;                                    // rsniper.cpp:151-175
                case "psycho": case "jeep": case "gatling": return Kind.Toggle;      // rpsycho.cpp:151-172, vjeep.cpp:229-264, cgatling.cpp:185-223
                case "pyro": return Kind.Pyro;                                        // rpyro.cpp:151-217
                case "laser": return Kind.Laser;                                      // rlaser.cpp:151-217
                default: return Kind.PerRoll;                                         // APC crew: one bullet per roll (vapc.cpp:134-152)
            }
        }

        /// <summary>Mean seconds between visible shots in Zod (continuous clock), straight from the formulas in the files above.</summary>
        public static double MeanPeriod(Kind k)
        {
            const double r = 0.495;                                                   // mean of (rand() % 100) * 0.01
            switch (k)
            {
                case Kind.Rifle: return 2 * (0.40 + 0.2 * r);                         // 0.998 s: bullet on every 2nd 0.40-0.60 s step
                case Kind.Sniper: return 2 * (0.30 + 0.18 * r);                       // 0.778 s
                case Kind.Toggle: return 2 * (0.07 + 0.03 * r);                       // 0.170 s
                case Kind.Pyro: return (0.07 + 0.03 * r) + 2 * (0.05 + 0.03 * r);     // 0.215 s
                case Kind.Laser: return (0.30 + 0.2 * r) + 2 * (0.05 + 0.03 * r);     // 0.529 s
                default: return 0;
            }
        }

        /// <summary>Speed for the robots' looping `shooting` clip (0.2 s) so one loop lasts one Zod fire cycle (at most 1x).</summary>
        public static float ShootClipRate(string type) => ShootClipRate(type, 0.2f);

        /// <summary>Same for a `shooting` clip of `clipSeconds` (Robot/shooting.anim 0.2 s; the BS-01 Humanoid clip from
        /// LEGACY_Robot_Clips.fbx is 5 frames): one loop per Zod fire cycle, never faster than authored.</summary>
        public static float ShootClipRate(string type, float clipSeconds)
        {
            double p = MeanPeriod(KindOf(type));
            return p > 0 && clipSeconds > 0f ? (float)System.Math.Min(1.0, clipSeconds / p) : 1f;
        }

        public readonly Kind kind;
        readonly System.Random rng;
        int action;
        double next = double.NegativeInfinity;

        public ZodClientFire(Kind kind, int seed) { this.kind = kind; rng = new System.Random(seed); }

        /// <summary>A new attack: the fire sprite starts over (Zod resets action_i to 0, zrobot.cpp:96,161).</summary>
        public void Reset(double now) { action = 0; next = now; }

        /// <summary>Steps the clock up to sim time `now` and returns how many visible shots fell due (normally 0 or 1).
        /// Steps are scheduled from the previous step time, so the result does not depend on the frame rate.</summary>
        public int Advance(double now)
        {
            if (kind == Kind.PerRoll) return 0;
            if (double.IsNegativeInfinity(next)) next = now;
            int shots = 0, guard = 0;
            while (now >= next && guard++ < 64)
            {
                bool fire; double dt;
                switch (kind)
                {
                    case Kind.Rifle:
                    case Kind.Sniper:
                        if (++action >= 5) action = 3;
                        dt = action >= 3 ? (kind == Kind.Rifle ? 0.40 + R() * 0.2 * 0.01 : 0.30 + R() * 0.18 * 0.01) : 0.02;
                        fire = action == 4;
                        break;
                    case Kind.Toggle:
                        action = 1 - action;
                        dt = 0.07 + R() * 0.03 * 0.01;
                        fire = action == 1;
                        break;
                    default:                                                          // Pyro, Laser: 0 -> 1 -> 2 (fire) -> 0
                        action = (action + 1) % 3;
                        dt = action == 0 ? (kind == Kind.Pyro ? 0.07 + R() * 0.03 * 0.01 : 0.30 + R() * 0.2 * 0.01) : 0.05 + R() * 0.03 * 0.01;
                        fire = action == 2;
                        break;
                }
                next += dt;
                if (fire) shots++;
            }
            return shots;
        }

        int R() => rng.Next(100);
    }
}
