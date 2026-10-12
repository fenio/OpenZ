using System;
using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ
{
    /// <summary>
    /// music_v3: the VIEW-SIDE reading of one fort for MusicCore's drama detector. Reads ZWorld.Objects, writes nothing (the sim stays
    /// deterministic and multiplayer-safe). Plain C# over the Sim types only (no UnityEngine): AudioDirector.MusicV3.cs calls it every
    ///  - counted: alive mobile units (robots, vehicles) of the other side; never neutral units, cannons, cranes or the fort defender
    ///  - weight: MusicCoreConfig (robot 1, jeep / APC 2, light 3, medium / missile launcher 4, heavy 5)
    ///  - ENGAGED (score + units): within rIn (16 tiles) of the nearest alive fort building of that side AND
    ///      attacking an object of that side that lies in the FORT AREA (within rCore, 11 tiles, of the fort: the fort itself, its
    ///      guns, its defender, units at its gate), or one of its next 3 waypoints attacks / enters such an object or ends in the
    ///      fort area, or it stands in the fort area while the fort takes hits (hitWindow).
    ///    Idle or passing units never count, and a fight at a flag outside the fort area (L17's Z6 flag is ~14 tiles from the red
    ///    fort) is not a fort attack.
    ///  - outerScore: every hostile mobile unit within rOut (20 tiles), for the overlay only (no rule reads it).
    ///  - sinceHit: game seconds since that side's fort last took an enemy hit (ZObject.LastEnemyHit, NB31)
    /// </summary>
    public static class MusicFortReader
    {
        [ThreadStatic] static List<ZObject> forts;

        public static FortReading Read(ZWorld w, Team side, MusicCoreConfig c)
        {
            var r = new FortReading { sinceHit = 999f };
            if (w == null) return r;
            var fs = forts ??= new List<ZObject>();
            fs.Clear();
            foreach (var o in w.Objects) if (o.IsFort && o.Owner == side && !o.Destroyed) fs.Add(o);
            if (fs.Count == 0) return r;
            r.alive = true;
            foreach (var f in fs) if (f.LastEnemyHit >= 0) r.sinceHit = Math.Min(r.sinceHit, (float)(w.Time - f.LastEnemyHit));
            bool underFire = r.sinceHit <= c.hitWindow;
            double rIn2 = (double)c.rIn * c.rIn, rOut2 = (double)c.rOut * c.rOut, rCore2 = (double)c.rCore * c.rCore;
            foreach (var o in w.Objects)
            {
                if (!o.IsMobile || o.Destroyed || o.Owner == Team.Neutral || o.Owner == side || o.IsFortDefender) continue;
                float wgt = Weight(o, c); if (wgt <= 0f) continue;
                double d2 = Dist2(o.CenterX, o.CenterY, fs);
                if (d2 > rOut2) continue;
                r.outerScore += wgt;
                if (d2 > rIn2) continue;
                bool engaged = InFortArea(o.AttackObject, side, fs, rCore2) || HeadingAt(w, o, side, fs, rCore2) || (underFire && d2 <= rCore2);
                if (engaged) { r.score += wgt; r.units++; }
            }
            return r;
        }

        public static float Weight(ZObject o, MusicCoreConfig c)
        {
            if (o.IsRobot) return c.wRobot;
            switch (o.Type)
            {
                case "crane": return 0f;
                case "jeep": return c.wJeep;
                case "apc": return c.wApc;
                case "light": return c.wLight;
                case "medium": return c.wMedium;
                case "missile_launcher": return c.wMissile;
                case "heavy": return c.wHeavy;
                default: return c.wOther;
            }
        }

        static double Dist2(double x, double y, List<ZObject> fs)
        {
            double d2 = double.MaxValue;
            foreach (var f in fs) { double dx = x - f.CenterX, dy = y - f.CenterY; d2 = Math.Min(d2, dx * dx + dy * dy); }
            return d2;
        }

        /// <summary>A live object of that side inside the fort area (the fort, its guns and defender, units at its gate).</summary>
        static bool InFortArea(ZObject t, Team side, List<ZObject> fs, double rCore2) =>
            t != null && !t.Destroyed && t.Owner == side && Dist2(t.CenterX, t.CenterY, fs) <= rCore2;

        /// <summary>"Moving on it": one of its next 3 waypoints attacks / enters an object of that side in the fort area, or ends there.</summary>
        static bool HeadingAt(ZWorld w, ZObject o, Team side, List<ZObject> fs, double rCore2)
        {
            int n = Math.Min(3, o.Waypoints.Count);
            for (int k = 0; k < n; k++)
            {
                var wp = o.Waypoints[k];
                if (wp.mode == WpMode.None) continue;
                if (wp.mode == WpMode.Attack || wp.mode == WpMode.EnterFort)
                {
                    var t = wp.refId >= 0 ? w.Get(wp.refId) : null;
                    if (InFortArea(t, side, fs, rCore2)) return true;
                    if (t != null) continue;                                                     // attacking something elsewhere
                }
                if (Dist2(wp.x, wp.y, fs) <= rCore2) return true;
            }
            return false;
        }
    }
}
