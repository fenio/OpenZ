using System;

namespace OpenZ
{
    /// <summary>
    /// K344, the shuttle's kickflip: its lamps while it rolls (view only; plain C#, also counted outside the editor). Through the flip the
    /// lamp heads keep their aim (gimballed: View/OzPlayableShuttle.Kickflip.cs), so the beams and their pools stay on the ground while the
    /// hull turns round them. A beam must never be seen passing through the ship: around 180 degrees the belly lamps sit on top of the hull
    /// and their beams would go down through it; a landing light aimed back meets a leg already after about 28 degrees of roll; a beam
    /// turned about 85 degrees grazes its own housing. So each lamp is dimmed by where its beam can still shine.
    /// <list type="bullet">
    /// <item>THE FIELD (View/OzShuttleKickflip.LampField.cs, made by the kit's tool from the ship's own model files): for the two belly lamps
    /// and the nose searchlight, every direction in the ship's root space with its CLEAR MARGIN: how many degrees the beam's axis may still
    /// turn before any part of its drawn cone meets the ship (the hull and its doorway, the pods, the legs, the lift nozzles, the lamp
    /// housings, the closed ramp, the nose searchlight's head for the belly lamps).</item>
    /// <item>THE FADE of a lamp (<see cref="Fade"/>): its beam's way in the flipped ship (its level aim turned back by the flip's extra turn)
    /// read in the field, eased by a smootherstep from 0 (margin <see cref="SafetyDeg"/> or less: nothing of it shows) to 1 (margin
    /// <see cref="SafetyDeg"/> + <see cref="FadeDeg"/> or more). At level the lamp is exactly as without the flip: the fade comes in with
    /// the hull's turn against the beam, over the first degrees of it (never more than the margin the beam had at level, less
    /// <see cref="LevelSlackDeg"/>, so it is complete before the beam can reach the ship). So the fade is a smooth function of the drawn
    /// pose: 1 at both ends of the flip, no step, the same at any frame rate, still on a paused frame.</item>
    /// <item>The ramp light (dark in flight: it burns only while the ramp is down) is faded by the hull's turn alone (out at
    /// <see cref="RampTurnOutDeg"/>).</item>
    /// <item>What it dims (OzPlayableShuttle.Kickflip.cs): each lamp's lens, halo, beam cone, real light and the pool of light on the
    /// ground, in the drawn picture only; the level pose's numbers come back the next frame (the lamps' own code is untouched).</item>
    /// </list>
    /// </summary>
    public static partial class OzKickflipLamps
    {
        public const int BellyL = 0, BellyR = 1, Nose = 2, Ramp = 3, Count = 4;
        /// <summary>The clear margin (deg) under which a lamp is out, the margin over which it fades back to full; the slack between the
        /// margin at level and the turn by which the fade has come in (deg), its least and most turn (deg); the ramp light's turn out (deg).</summary>
        public static double SafetyDeg = 5.0, FadeDeg = 24.0, LevelSlackDeg = 5.0, TurnMinDeg = 0.5, TurnMaxDeg = 12.0, RampTurnOutDeg = 20.0;
        static byte[][] cells;

        static void Decode()
        {
            const string B64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
            var c = new byte[FieldRows.Length][];
            for (int k = 0; k < FieldRows.Length; k++)
            {
                var rows = FieldRows[k]; var b = new byte[FieldRowsN * FieldCols];
                for (int j = 0; j < FieldRowsN; j++)
                    for (int i = 0; i < FieldCols; i++) { int v = B64.IndexOf(rows[j][i]); b[j * FieldCols + i] = (byte)(v < 0 ? 0 : v); }
                c[k] = b;
            }
            cells = c;
        }

        static double C01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;
        /// <summary>6x^5 - 15x^4 + 10x^3 on 0..1 (rate and its change 0 at both ends).</summary>
        public static double Smoother(double x) { x = C01(x); return x * x * x * (x * (6.0 * x - 15.0) + 10.0); }
        /// <summary>The steepest slope of <see cref="Smoother"/> (per unit of x).</summary>
        public const double SmootherSlope = 1.875;

        /// <summary>Lamp k's clear margin (deg) for a beam along (x, y, z) in the ship's root space (any length); 0 = it meets the ship. The
        /// field read bilinearly (phi = atan2(x, -y) round the long axis, wrapped; lat = asin z).</summary>
        public static double Margin(int k, double x, double y, double z)
        {
            if (k < 0 || k >= FieldRows.Length) return 0;
            if (cells == null) Decode();
            double n = Math.Sqrt(x * x + y * y + z * z); if (n < 1e-12) return 0;
            x /= n; y /= n; z /= n;
            double phi = Math.Atan2(x, -y) * (180.0 / Math.PI), lat = Math.Asin(z < -1 ? -1 : z > 1 ? 1 : z) * (180.0 / Math.PI);
            double fi = (phi + 180.0) / FieldStepDeg, fj = (lat + 90.0) / FieldStepDeg;
            int i0 = (int)Math.Floor(fi); double ti = fi - i0; i0 = ((i0 % FieldCols) + FieldCols) % FieldCols; int i1 = (i0 + 1) % FieldCols;
            int j0 = (int)Math.Floor(fj); if (j0 < 0) j0 = 0; if (j0 > FieldRowsN - 2) j0 = FieldRowsN - 2; double tj = C01(fj - j0);
            var c = cells[k]; int r0 = j0 * FieldCols, r1 = r0 + FieldCols;
            double a = c[r0 + i0] + (c[r0 + i1] - c[r0 + i0]) * ti, b = c[r1 + i0] + (c[r1 + i1] - c[r1 + i0]) * ti;
            return (a + (b - a) * tj) * FieldUnitDeg;
        }

        /// <summary>The fade of a clear margin: 0 at <see cref="SafetyDeg"/> or less .. 1 from <see cref="SafetyDeg"/> + <see cref="FadeDeg"/>.</summary>
        public static double FadeOfMargin(double m) => Smoother((m - SafetyDeg) / FadeDeg);

        /// <summary>A direction of the level ship's root space as the flipped ship's root space sees it: the flip's extra turn
        /// (Euler(-pitchUp, 0, 0) * Euler(0, 0, roll), as OzPlayableShuttle.KickPose draws it) taken back.</summary>
        public static void Unflip(double rollDeg, double pitchUpDeg, double x, double y, double z, out double ux, out double uy, out double uz)
        {
            double p = pitchUpDeg * (Math.PI / 180.0), r = rollDeg * (Math.PI / 180.0), cp = Math.Cos(p), sp = Math.Sin(p), cr = Math.Cos(r), sr = Math.Sin(r);
            double wy = cp * y - sp * z, wz = sp * y + cp * z;                                            // Euler(+pitchUp, 0, 0)
            ux = cr * x + sr * wy; uy = -sr * x + cr * wy; uz = wz;                                       // then Euler(0, 0, -roll)
        }

        /// <summary>The angle (deg) between two directions.</summary>
        public static double Angle(double ax, double ay, double az, double bx, double by, double bz)
        {
            double na = Math.Sqrt(ax * ax + ay * ay + az * az), nb = Math.Sqrt(bx * bx + by * by + bz * bz);
            if (na < 1e-12 || nb < 1e-12) return 0;
            double c = (ax * bx + ay * by + az * bz) / (na * nb);
            return Math.Acos(c < -1 ? -1 : c > 1 ? 1 : c) * (180.0 / Math.PI);
        }

        /// <summary>Lamp k's fade (0..1) in the flip's pose (roll, pitchUp, deg), its beam aimed along (ax, ay, az) in the LEVEL ship's root
        /// space (the gimballed head's aim). turnDeg = how far the hull has turned against the beam, marginDeg = the beam's clear margin now.</summary>
        public static double Fade(int k, double ax, double ay, double az, double rollDeg, double pitchUpDeg, out double turnDeg, out double marginDeg)
        {
            Unflip(rollDeg, pitchUpDeg, ax, ay, az, out double dx, out double dy, out double dz);
            turnDeg = Angle(ax, ay, az, dx, dy, dz);
            if (k == Ramp) { marginDeg = 0; return 1.0 - Smoother(turnDeg / RampTurnOutDeg); }
            marginDeg = Margin(k, dx, dy, dz);
            double lev = Margin(k, ax, ay, az), turnIn = lev - LevelSlackDeg;
            turnIn = turnIn < TurnMinDeg ? TurnMinDeg : turnIn > TurnMaxDeg ? TurnMaxDeg : turnIn;
            return 1.0 - (1.0 - FadeOfMargin(marginDeg)) * Smoother(turnDeg / turnIn);
        }

        public static double Fade(int k, double ax, double ay, double az, double rollDeg, double pitchUpDeg) => Fade(k, ax, ay, az, rollDeg, pitchUpDeg, out _, out _);
    }
}
