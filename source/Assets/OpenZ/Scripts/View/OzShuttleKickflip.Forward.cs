using System;

namespace OpenZ
{
    /// <summary>K423: a camera's picture for the kickflip's lead (screen px of a world point, y up; false = behind the camera).</summary>
    public interface IOzKickScreen
    {
        bool S(double x, double y, double z, out double sx, out double sy);
    }

    /// <summary>
    /// K423, the kickflip keeps travelling forward on screen through the whole roll (an easter egg, view only; the flip: View/OzShuttleKickflip.Core.cs).
    /// The plain part, shared by the game (View/OzPlayableShuttle.KickForward.cs), the counted check (Editor/OzShuttleKickflipCount.K423.cs) and the
    /// kit's twins. No engine type, no random draw, no clock of its own, nothing allocated per frame. The rules never know about it.
    /// <list type="bullet">
    /// <item>WHY IT READ AS "IN PLACE": the rules fly the ship on at its cruise speed through the flip (13.1 m in 2.5 s), but the drawn hull rises
    /// (the hop 0.8 m + the barrel roll's arc 4.8 m), comes down again and swings 3.2 m out to each side. From the game's slanted camera a rise
    /// moves the hull up the screen, which is the same on-screen move as a step AWAY from the camera; on a diagonal flight the swing to the side
    /// also moves it partly along its screen line. Flying toward or away from the camera the hull stalls and even runs BACKWARDS on screen for
    /// about half a second (the counted twin: down to -96 % of its cruise pace at the 70 m camera, -210 % at 35 m), so the ship seems to hang in
    /// place while it rolls.</item>
    /// <item>THE LEAD (<see cref="LeadSolve"/>): each drawn frame the hull is shifted along the flight's own direction just so far that its
    /// picture stays level with the level ship's picture along the flight's screen line: the rise and the swing move it only ACROSS that line
    /// (the spiral: out, over and across round its way), never back along it. So on screen the hull goes forward exactly at its cruise pace
    /// through the whole roll, from any camera height and heading (across the screen the lead is about 0 and the arc shows as before). It is
    /// solved from the camera's own picture (a few Newton steps), 0 when the hull is on its level pose (both ends of the flip, every way out's
    /// end), smooth as the hop, the arc and the camera are, and at most <see cref="LeadMaxM"/> m.</item>
    /// <item>THE WAY LEFT (<see cref="WayLeftM"/>): with a route (a Shift queue the ship flies through, DV125) the ship slows before each middle
    /// point and turns there; the flip's start test now measures the way left to the NEXT point of the route, not to its end, so a flip never
    /// runs into that slow-down and turn (the counted twin: down to 54 % of the top speed under the flip before).</item>
    /// </list>
    /// </summary>
    public static class OzKickflipForward
    {
        /// <summary>The switch (tests): false = no lead (the round-2 pose exactly).</summary>
        public static bool LeadOn = true;
        /// <summary>The largest lead (m; the game's camera at 70 m needs up to about 5.5 m toward or away from it).</summary>
        public static double LeadMaxM = 9.0;

        /// <summary>The lead (m along the flight's direction (dx, dz), a unit vector across the ground) that keeps the drawn hull's picture level
        /// with the level hull's along the flight's screen line: b = the level hull's middle (world), o = the flip's drawn offset (the hop + the
        /// arc's rise up, the swing to the side; world). Solves (S(b + o + d L) - S(b)) . u = 0, u = the flight's screen direction at b, by
        /// Newton steps (the slope from a 0.5 m step along d). 0 with the switch off, no offset, a picture that cannot be read (behind the
        /// camera) or a flight that does not move on screen.</summary>
        public static double LeadSolve<TS>(ref TS cam, double bx, double by, double bz, double dx, double dz, double ox, double oy, double oz) where TS : struct, IOzKickScreen
        {
            if (!LeadOn || (ox == 0.0 && oy == 0.0 && oz == 0.0)) return 0.0;
            if (!cam.S(bx, by, bz, out double b0x, out double b0y) || !cam.S(bx + dx, by, bz + dz, out double b1x, out double b1y)) return 0.0;
            double ux = b1x - b0x, uy = b1y - b0y, ul = Math.Sqrt(ux * ux + uy * uy);
            if (!(ul > 1e-4)) return 0.0;
            ux /= ul; uy /= ul;
            double L = 0.0;
            for (int it = 0; it < 6; it++)
            {
                double px = bx + ox + dx * L, py = by + oy, pz = bz + oz + dz * L;
                if (!cam.S(px, py, pz, out double sx, out double sy) || !cam.S(px + 0.5 * dx, py, pz + 0.5 * dz, out double tx, out double ty)) break;
                double f = (sx - b0x) * ux + (sy - b0y) * uy, g = ((tx - sx) * ux + (ty - sy) * uy) / 0.5;
                if (!(g > 1e-4)) break;
                double step = f / g; L -= step;
                if (Math.Abs(step) < 1e-6) break;
            }
            return double.IsNaN(L) ? 0.0 : Math.Max(-LeadMaxM, Math.Min(LeadMaxM, L));
        }

        /// <summary>The way left for the start test (m): to the goal, or with a route to the next middle point (which the ship slows for and
        /// turns at), whichever is nearer.</summary>
        public static double WayLeftM(double toGoalM, bool hasNextPoint, double toNextPointM) => hasNextPoint ? Math.Min(toGoalM, Math.Max(0.0, toNextPointM)) : toGoalM;
    }
}
