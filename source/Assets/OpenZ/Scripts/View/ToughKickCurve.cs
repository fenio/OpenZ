using System;

namespace OpenZ
{
    /// <summary>
    /// off-engine check runs exactly the code the game runs. View only: nothing here reads or writes the simulation.
    /// <para>Every channel has the same shape, a damped back-and-forth: 0 before the shot; out to 1 in <see cref="Shape.Attack"/> s (an
    /// ease-out: fast at the release, still at the peak); then back like a released spring, exp(-u / tau) (cos(w u) + sin(w u) / (w tau)),
    /// through rest <see cref="Shape.ZeroAt"/> s after the peak and on to <see cref="Shape.Overshoot"/> of the peak the other way (the
    /// forward rock after the backward one); from that turning point it settles without another swing (critically damped,
    /// <see cref="Shape.Settle"/> s). A channel may start <see cref="Shape.Delay"/> s after the shot (the head and the free arm lag the body).</para>
    /// <para>A new rocket while a kick still runs starts the new kick from where the old one is (<see cref="Channel.Trigger"/>: the old
    /// value fades out in <see cref="CarryTau"/> s under the new kick), so a shot every reload interval never jumps and never drifts:
    /// every term returns to 0 by itself, and a channel is never more than one rocket's peak.</para>
    /// </summary>
    public static class ToughKickCurve
    {
        /// <summary>One channel's shape: attack (s), zero crossing after the peak (s), overshoot (share of the peak), delay (s), settle (s).</summary>
        public struct Shape
        {
            public float Attack, ZeroAt, Overshoot, Delay, Settle;
            public float W, Tau, Turn, EndAt;                                                     // derived (Make)

            public static Shape Make(float attack, float zeroAt, float overshoot, float delay = 0f, float settle = 0.12f)
            {
                var s = new Shape { Attack = Math.Max(0.005f, attack), ZeroAt = Math.Max(0.02f, zeroAt), Overshoot = Clamp(overshoot, 0.001f, 0.9f),
                                    Delay = Math.Max(0f, delay), Settle = Math.Max(0.02f, settle) };
                // overshoot = exp(-pi / (w tau)) at the turning point u = pi / w; zero crossing: w u0 = pi - atan(w tau)
                double wt = Math.PI / Math.Log(1.0 / s.Overshoot);
                s.W = (float)((Math.PI - Math.Atan(wt)) / s.ZeroAt);
                s.Tau = (float)(wt / s.W);
                s.Turn = (float)(Math.PI / s.W);
                // settled: the critically damped tail os (1 + x) e^-x under 0.2 % of the peak
                double x = 0.0; while (s.Overshoot * (1.0 + x) * Math.Exp(-x) > 0.002 && x < 40.0) x += 0.05;
                s.EndAt = s.Delay + s.Attack + s.Turn + (float)(x * s.Settle);
                return s;
            }
        }

        /// <summary>Seconds after which a shape is taken as settled (under 0.2 % of the peak): the evaluation stops there.</summary>
        public static float End(Shape s) => s.EndAt;

        /// <summary>The unit envelope at `age` s after the shot (peak 1 at Delay + Attack).</summary>
        public static float Env(Shape s, float age)
        {
            float a = age - s.Delay;
            if (a <= 0f || age > s.EndAt) return 0f;
            if (a < s.Attack) { float x = 1f - a / s.Attack; return 1f - x * x * x; }                // ease-out: fast at the release, still at the peak
            float u = a - s.Attack;
            if (u <= s.Turn)
            {
                double e = Math.Exp(-u / s.Tau);
                return (float)(e * (Math.Cos(s.W * u) + Math.Sin(s.W * u) / (s.W * s.Tau)));       // back through rest to the turning point (no speed there)
            }
            double q = (u - s.Turn) / s.Settle;
            return (float)(-s.Overshoot * (1.0 + q) * Math.Exp(-q));                                // from the turning point, settles from rest without a second swing
        }

        /// <summary>How fast the rest of a kick still running fades when the next rocket starts a new one (s).</summary>
        public const float CarryTau = 0.06f;

        /// <summary>One channel of one robot: amplitude x the envelope since the last shot + the carried rest of the kick before it.</summary>
        public struct Channel
        {
            public Shape Shape; public float Amp; float carry, firedAt; bool any;

            public Channel(Shape shape, float amp) { Shape = shape; Amp = amp; carry = 0f; firedAt = -100f; any = false; }

            /// <summary>A rocket left at `at` (s): the new kick starts from the value the channel has at that moment.</summary>
            public void Trigger(float at) { carry = any ? Value(at) : 0f; firedAt = at; any = true; }

            public float Value(float now)
            {
                if (!any) return 0f;
                float age = now - firedAt;
                if (age < 0f) return 0f;
                float v = Amp * Env(Shape, age);
                if (carry != 0f) v += carry * (float)Math.Exp(-age / CarryTau);
                return Clamp(v, -Amp, Amp);                                                            // rockets on top of each other never add up past one rocket's peak
            }

            /// <summary>True while the channel is not settled (worth applying).</summary>
            public bool Live(float now) => any && now - firedAt < Shape.EndAt + 6f * CarryTau;
        }

        static float Clamp(float x, float a, float b) => x < a ? a : x > b ? b : x;

        // ------------------------------------------------------------------------------------------------ the kick's shapes
        /// <summary>The torso rock (top back): out in 0.05 s, through rest 0.30 s after the peak, a third forward past it, settles.</summary>
        public static Shape Rock = Shape.Make(0.05f, 0.30f, 0.33f, 0f, 0.13f);
        /// <summary>The head's extra nod back: a little after the body.</summary>
        public static Shape Head = Shape.Make(0.06f, 0.28f, 0.25f, 0.03f, 0.12f);
        /// <summary>The right shoulder thrown back (the upper chest turned into the gun): out in 0.045 s, through rest 0.32 s later, a fifth past.</summary>
        public static Shape Twist = Shape.Make(0.045f, 0.32f, 0.20f, 0f, 0.12f);
        /// <summary>The free (left) arm: the upper arm swings the fist forward as the body snaps back, then back past rest (30 %), settles.</summary>
        public static Shape Arm = Shape.Make(0.10f, 0.26f, 0.30f, 0.02f, 0.15f);
        /// <summary>... and the elbow flexes forward a moment after it.</summary>
        public static Shape Elbow = Shape.Make(0.12f, 0.28f, 0.25f, 0.04f, 0.15f);
    }
}
