using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// DV118, the shuttle's strafe under fire (called from ZObject.ShuttleStep; deterministic: no random draw, no frame time):
    ///  * The watch: every step up in the air it looks at the projectiles in flight (ZWorld.PendingMissiles: rockets, tank shells,
    ///    missiles, the fort gun's and the guns' shells, grenades; never the debris of a death) that an enemy aimed at it. Each one is
    ///    counted once, when it is first seen; from it the ship is "under fire" for ZWorld.ShuttleHeavyFireHoldSec s.
    ///  * ONE smooth glide: on the first projectile it eases ZWorld.ShuttleStrafeM sideways to its facing, to the side away from the
    ///    shooters, over ZWorld.ShuttleStrafeSec (a quintic: speed and acceleration 0 at both ends). The side is chosen once for the threat
    ///    and kept until the ship is back on its line. While the fire goes on it holds there (no zig-zag); once the fire has stopped it
    ///    drifts back onto its line slowly, eased, over ZWorld.ShuttleStrafeBackSec. A glide is never reversed or cut short, and two changes
    ///    of the sideways direction are at least ZWorld.ShuttleStrafeMinFlipSec apart (counted from the moment the sideways motion really
    ///    turns); fired at again during the drift back it eases to a stop and holds there (it never turns back out: one change of direction
    ///    per threat), then drifts on back once the fire has stopped. Its flight plan is untouched: the point on the line (ShuttleBaseX /
    ///    ShuttleBaseY) goes on to the goal, so it never leaves its order and never goes far.
    ///  * Where: only up in the air (the lift at least ZWorld.ShuttleStrafeMinLift; climbing, cruising or hovering). It never leaves the map
    ///    (the side's room to the edge limits the offset; no room on either side = no glide). Coming down to land it does not strafe: the
    ///    drift back finishes first and the descent waits for it, so it always lands on the tested pad.
    ///  * The game's own missile rules decide the hits: a projectile flies to the point it was aimed at (with the game's lead from the
    ///    ship's real velocity, the strafe included) and its blast reaches the ship only within its radius. A ship that has moved off that
    ///    point takes less, or nothing.
    ///  * Outputs: ShuttleStrafePx / ShuttleStrafeVelPx / ShuttleStrafeAccelPx (+ = right of the facing), ShuttleStrafeStage,
    ///    ShuttleStrafeSide, ShuttleUnderHeavyFire, ShuttleThreatsSeen, ShuttleStrafes (glides out begun), ShuttleStrafeReturns. The offset is
    ///    part of the ship's place (CenterX / CenterY, Dx / Dy).
    /// </summary>
    public partial class ZObject
    {
        List<ZMissile> shuttleSeen;                               // projectiles aimed at it that are still in flight and already counted
        double shuttleFireUntil = -1e9;                           // under fire until (World.Time): the last sighting + ShuttleHeavyFireHoldSec
        double shuttleFromX, shuttleFromY;                        // where the recent shots came from (unit vectors, the older ones halved)
        int shuttleStrafeMode;                                    // 0 = on its line, 1 = gliding out, 2 = holding, 3 = drifting back, 4 = easing to a stop on the way back
        int shuttleStrafeKeep;                                    // the side of this threat (+1 right, -1 left; 0 = none): kept until back on the line
        double shuttleStrafeFrom, shuttleStrafeTo, shuttleStrafeV0, shuttleStrafeA0, shuttleStrafeAge, shuttleStrafeDur;
        double shuttleTurnAt = -1e9;                              // World.Time the sideways motion last turned (its direction changed)
        int shuttleLastDir;                                       // the last sideways direction it moved in (+1, -1; 0 = never yet)
        double shuttleOffX, shuttleOffY;                          // the offset applied to the ship's place (px)

        /// <summary>A strafe is on (gliding, holding off its line or drifting back).</summary>
        internal bool ShuttleStrafeBusy => shuttleStrafeMode != 0 || ShuttleAvoidBusy;   // K338: or off its line giving way to another ship (ZWorld.ShuttleTraffic.cs): the descent and the turns over its spot wait for it too

        /// <summary>The strafe and the watch forgotten (the ship placed somewhere by hand, a new level).</summary>
        void ShuttleStrafeReset()
        {
            shuttleStrafeMode = 0; shuttleStrafeKeep = 0; shuttleOffX = shuttleOffY = 0; shuttleTurnAt = -1e9; shuttleLastDir = 0;
            ShuttleStrafePx = ShuttleStrafeVelPx = ShuttleStrafeAccelPx = 0;
            shuttleSeen?.Clear(); shuttleFireUntil = -1e9; ShuttleUnderHeavyFire = false;
        }

        /// <summary>Every step: the projectiles aimed at it.</summary>
        void ShuttleThreatScan()
        {
            var w = World; double now = w.Time;
            if (!ShuttleAirborne) { shuttleSeen?.Clear(); shuttleFireUntil = -1e9; ShuttleUnderHeavyFire = false; return; }
            var pend = w.PendingMissiles;
            if (shuttleSeen == null) shuttleSeen = new List<ZMissile>();
            for (int i = shuttleSeen.Count - 1; i >= 0; i--)
            {
                bool still = false;
                for (int k = 0; k < pend.Count; k++) if (ReferenceEquals(pend[k], shuttleSeen[i])) { still = true; break; }
                if (!still) shuttleSeen.RemoveAt(i);
            }
            for (int k = 0; k < pend.Count; k++)
            {
                var m = pend[k];
                if (m.targetId != Id || m.debris || m.team == Team.Neutral || m.team == Owner || shuttleSeen.Contains(m)) continue;
                shuttleSeen.Add(m); ShuttleThreatsSeen++;
                shuttleFireUntil = now + ZWorld.ShuttleHeavyFireHoldSec;                     // one projectile is enough
                var a = w.Get(m.attackerId);
                double ax = a != null ? a.CenterX - CenterX : m.x - CenterX, ay = a != null ? a.CenterY - CenterY : m.y - CenterY, al = Math.Sqrt(ax * ax + ay * ay);
                shuttleFromX *= 0.5; shuttleFromY *= 0.5;
                if (al > 1e-6) { shuttleFromX += ax / al; shuttleFromY += ay / al; }
            }
            ShuttleUnderHeavyFire = now < shuttleFireUntil;
        }

        /// <summary>The ship's right (sim px, y down): the facing turned 90 degrees clockwise seen from above.</summary>
        (double x, double y) ShuttleRight() { double h = ShuttleHeadingDeg * Math.PI / 180.0; return (-Math.Sin(h), Math.Cos(h)); }

        /// <summary>How far (px) the ship may go to `side` (+1 right, -1 left) off its line point before the map edge rule (one tile
        /// plus half its box in from the edge) stops it; 2 px kept spare.</summary>
        double ShuttleStrafeRoom(int side)
        {
            var m = World?.Map;
            if (m == null || side == 0) return 0;
            var (rx, ry) = ShuttleRight(); rx *= side; ry *= side;
            double lo = 16 + WidthPix * 0.5, hx = m.WidthPix - 16 - WidthPix * 0.5, hy = m.HeightPix - 16 - HeightPix * 0.5, room = 1e9;
            if (rx > 1e-9) room = Math.Min(room, (hx - shuttleFx) / rx); else if (rx < -1e-9) room = Math.Min(room, (lo - shuttleFx) / rx);
            if (ry > 1e-9) room = Math.Min(room, (hy - shuttleFy) / ry); else if (ry < -1e-9) room = Math.Min(room, (lo - shuttleFy) / ry);
            return Math.Max(0.0, Math.Min(room, ShuttleTrafficStrafeRoom(side)) - 2.0);   // K338: and short of another ship beside it (ZWorld.ShuttleTraffic.cs; no limit with one shuttle)
        }

        /// <summary>The side of this threat's glide: away from where the recent shots came from; shots from straight ahead or behind: the
        /// side with more room (right when equal). A side with less than 1 m of room is not used; none = 0 (no glide).</summary>
        int ShuttleStrafePick()
        {
            var (rx, ry) = ShuttleRight();
            double dot = shuttleFromX * rx + shuttleFromY * ry, fl = Math.Sqrt(shuttleFromX * shuttleFromX + shuttleFromY * shuttleFromY);
            int side = fl > 1e-6 && dot > 0.25 * fl ? -1 : fl > 1e-6 && dot < -0.25 * fl ? 1 : (ShuttleStrafeRoom(-1) > ShuttleStrafeRoom(1) + 1e-6 ? -1 : 1);
            if (ShuttleStrafeRoom(side) < 8.0) side = -side;
            return ShuttleStrafeRoom(side) < 8.0 ? 0 : side;
        }

        /// <summary>A move of the offset to `to` (px) from where it is now, its speed and acceleration carried in (a quintic in time), over
        /// `fullSec` x sqrt(distance / ShuttleStrafeM) s (the same peak acceleration for a shorter move), at least 0.5 s.</summary>
        void ShuttleStrafeMove(double to, double fullSec, int mode)
        {
            double from = ShuttleStrafePx, dist = Math.Abs(to - from), s = ZWorld.ShuttleStrafeM * 8.0;
            shuttleStrafeFrom = from; shuttleStrafeTo = to; shuttleStrafeV0 = ShuttleStrafeVelPx; shuttleStrafeA0 = ShuttleStrafeAccelPx; shuttleStrafeAge = 0;
            shuttleStrafeDur = Math.Max(0.5, fullSec * Math.Sqrt(Math.Max(dist, 1.0) / s));
            shuttleStrafeMode = mode;
            if (mode == 1) ShuttleStrafes++;
        }

        /// <summary>Fired at again while it drifts back: it eases to rest over ZWorld.ShuttleStrafeStopSec (half its speed x that time
        /// further on, a quintic from its speed now to rest: the motion never turns back) and holds there.</summary>
        void ShuttleStrafeStop()
        {
            double v = ShuttleStrafeVelPx, t = ZWorld.ShuttleStrafeStopSec;
            shuttleStrafeFrom = ShuttleStrafePx; shuttleStrafeTo = ShuttleStrafePx + 0.5 * v * t; shuttleStrafeV0 = v; shuttleStrafeA0 = 0; shuttleStrafeAge = 0;
            shuttleStrafeDur = t; shuttleStrafeMode = 4;
        }

        // the quintic: p0 + (p1 - p0) S5(u) + v0 T B1(u) + a0 T^2 C1(u); S5 = 10u^3 - 15u^4 + 6u^5, B1 = u - 6u^3 + 8u^4 - 3u^5, C1 = u^2 (1 - u)^3 / 2
        static double ShuttleQuinP(double u, double p0, double p1, double v0, double a0, double t)
        {
            double u2 = u * u, u3 = u2 * u, u4 = u3 * u, u5 = u4 * u, w = 1 - u;
            return p0 + (p1 - p0) * (10 * u3 - 15 * u4 + 6 * u5) + v0 * t * (u - 6 * u3 + 8 * u4 - 3 * u5) + a0 * t * t * 0.5 * u2 * w * w * w;
        }

        /// <summary>Every step, after the flight: the strafe's state and the offset. allowed = up in the air and not coming down to land.</summary>
        void ShuttleStrafeStep(double dt, bool allowed)
        {
            double s0 = ShuttleStrafePx, v0 = ShuttleStrafeVelPx, now = World.Time;
            bool fire = allowed && ZWorld.ShuttleStrafeOn && ShuttleUnderHeavyFire;
            bool turnOk = now - shuttleTurnAt >= ZWorld.ShuttleStrafeMinFlipSec - 1e-9;     // the sideways direction may change again (counted from the last real turn)
            double full = ZWorld.ShuttleStrafeM * 8.0;
            switch (shuttleStrafeMode)
            {
                case 0:                                                                       // on its line: the first projectile starts the glide
                    if (fire && turnOk)
                    {
                        int side = ShuttleStrafePick();
                        if (side != 0) { shuttleStrafeKeep = side; ShuttleStrafeMove(side * Math.Min(full, ShuttleStrafeRoom(side)), ZWorld.ShuttleStrafeSec, 1); }
                    }
                    break;
                case 1:                                                                       // gliding out: never reversed, never cut short
                    break;
                case 2:                                                                       // holding while the fire goes on (or while the last turn is too recent)
                    if (!fire && turnOk) ShuttleStrafeMove(0, ZWorld.ShuttleStrafeBackSec, 3);
                    break;
                case 3:                                                                       // drifting back and fired at again: it eases to a stop and holds (never back out)
                    if (fire) ShuttleStrafeStop();
                    break;
                case 4:                                                                       // easing to a stop: never cut short
                    break;
            }
            double s = s0;
            if (shuttleStrafeMode == 1 || shuttleStrafeMode == 3 || shuttleStrafeMode == 4)
            {
                shuttleStrafeAge += dt;
                double u = Math.Min(1.0, shuttleStrafeAge / shuttleStrafeDur);
                s = ShuttleQuinP(u, shuttleStrafeFrom, shuttleStrafeTo, shuttleStrafeV0, shuttleStrafeA0, shuttleStrafeDur);
                if (u >= 1.0)
                {
                    s = shuttleStrafeTo;
                    if (shuttleStrafeMode == 3) { shuttleStrafeMode = 0; shuttleStrafeKeep = 0; ShuttleStrafeReturns++; }
                    else shuttleStrafeMode = 2;
                }
            }
            if (s != 0)
            {   // never past the map edge rule (the line point may have come nearer to an edge meanwhile)
                double room = ShuttleStrafeRoom(Math.Sign(s));
                if (Math.Abs(s) > room) s = Math.Sign(s) * room;
            }
            ShuttleStrafePx = s;
            ShuttleStrafeVelPx = (s - s0) / dt;
            ShuttleStrafeAccelPx = (ShuttleStrafeVelPx - v0) / dt;
            int dir = ShuttleStrafeVelPx > ZWorld.ShuttleStrafeStillPx ? 1 : ShuttleStrafeVelPx < -ZWorld.ShuttleStrafeStillPx ? -1 : 0;
            if (dir != 0 && dir != shuttleLastDir) { shuttleTurnAt = now; shuttleLastDir = dir; }   // the sideways motion turned (or began)
            var (rx, ry) = ShuttleRight();
            shuttleOffX = s * rx; shuttleOffY = s * ry;
        }
    }
}
