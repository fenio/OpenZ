using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// Restores Z 1996
    /// behaviour missing from the Zod port (refs/zod_engine/src has no crush code), so it is a deliberate OpenZ deviation:
    /// <para>A MOVING crewed vehicle (never a parked / held one, never a cannon) whose hull touches an ENEMY robot knocks him down (DV51's
    /// KnockAt / KnockUntil: he holds his fire, the view plays the fall AWAY from the impact along the vehicle's travel, a glancing side hit
    /// throws him sideways) and hurts him (RunOverHurtFrac = 25 % of his max health head-on, RunOverGlanceFrac = 10 % on a glancing hit, never below 1 HP). Only if the vehicle then drives on over
    /// him (his centre under the inner hull, at least RunOverSquashDelay s after the hit) is he squashed: instant death, the vehicle's crew
    /// gets the kill (veterancy), the view flattens his own mesh into a pancake (UnitView.Squash). A glancing hit leaves him beside the
    /// path: he gets up hurt. Own-team robots are never hit (they step aside / are pushed as before: no friendly fire, Z's rule); the
    /// CPU obeys the same rule (it is the sim). The moving vehicle and an enemy robot are exempt from the spacing gate (ZPathing.PairExempt),
    /// so the vehicle drives into him instead of queueing behind him. Deterministic: sim positions, sim time, object order, no rand().</para>
    /// OFF = Zod exact (oz_zod_exact 1, PlayerPrefs oz_run_over 0, every Zod parity row).
    /// </summary>
    public partial class ZObject
    {
        /// <summary>DV65: died under an enemy vehicle (the view's pancake, the crush death sound).</summary>
        public bool Squashed;
        /// <summary>DV65: the vehicle that hit this robot last (-1 = none), when, the push direction (px, unit) and a glancing (side) hit.</summary>
        public int RunOverBy = -1;
        public double RunOverAt = -1;
        public float RunOverDirX, RunOverDirY;
        public bool RunOverGlancing;
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV65 (see ZWorld.RunOver.cs). PlayerPrefs oz_run_over; ZGame sets it at every start.</summary>
        public static bool RunOverDeviation = true;
        public const double RunOverHurtFrac = 0.25, RunOverGlanceFrac = 0.10, RunOverSquashDelay = 0.25, RunOverRehit = 3.0;
        /// <summary>DV65 hull box per chassis (fractions of ZPathing.VehicleRadiusOf): half width; the squash zone (lateral, along).</summary>
        public const double RunOverHalfWidth = 0.8, RunOverSquashLat = 0.7, RunOverSquashAlong = 0.8;
        /// <summary>DV65 statistics (checks / proof): hulls that hit an enemy robot, glancing ones, squashes.</summary>
        public int RunOverHits { get; private set; }
        public int RunOverGlances { get; private set; }
        public int RunOverSquashes { get; private set; }

        /// <summary>DV65: v may drive into r (and r walk into v) without the spacing gate: a moving crewed vehicle and an enemy robot.</summary>
        public static bool RunOverPair(ZObject v, ZObject r) =>
            RunOverDeviation && v != null && r != null && v.Kind == ZKind.Vehicle && r.Kind == ZKind.Robot && v.Owner != Team.Neutral && r.Owner != Team.Neutral
            && v.Owner != r.Owner && v.Drivers.Count > 0 && ZPathing.Moving(v) && !r.IsFortDefender;

        /// <summary>ZWorld.Tick after the pathing layer's end of tick: hull contacts of moving crewed vehicles with enemy robots.</summary>
        void RunOverTick()
        {
            if (!RunOverDeviation) return;
            int n = Objects.Count;
            for (int i = 0; i < n && i < Objects.Count; i++)
            {
                var v = Objects[i];
                if (v.Kind != ZKind.Vehicle || v.Destroyed || v.KillAt >= 0 || v.Owner == Team.Neutral || v.Drivers.Count == 0) continue;
                if (!ZPathing.Moving(v) || v.PathHeld || v.RealMoveSpeed <= 0) continue;
                double dl = Math.Sqrt((double)v.Dx * v.Dx + (double)v.Dy * v.Dy);
                if (dl < 1e-6) continue;
                double hx = v.Dx / dl, hy = v.Dy / dl, px = -hy, py = hx;
                double halfLen = ZPathing.VehicleRadiusOf(v.Type), halfW = halfLen * RunOverHalfWidth;
                for (int j = 0; j < n && j < Objects.Count; j++)
                {
                    var r = Objects[j];
                    if (r.Kind != ZKind.Robot || r.Destroyed || r.KillAt >= 0 || r.Health <= 0 || r.Owner == Team.Neutral || r.Owner == v.Owner) continue;   // never the own team
                    if (r.IsFortDefender || r.PathInBuildingStage || r.FortRushOn) continue;
                    double rx = r.CenterX - v.CenterX, ry = r.CenterY - v.CenterY;
                    if (Math.Abs(rx) > 40 || Math.Abs(ry) > 40) continue;
                    double along = rx * hx + ry * hy, lat = rx * px + ry * py, rr = ZPathing.Radius(r);
                    if (along < -halfLen * 0.3 || along > halfLen + rr || Math.Abs(lat) > halfW + rr) continue;   // not touching the hull (behind it: driving away)
                    bool fresh = r.RunOverBy != v.Id || Time - r.RunOverAt > RunOverRehit;
                    if (fresh)
                    {   // the hit: knocked down away from the impact, hurt
                        bool glancing = Math.Abs(lat) > halfW * 0.55;
                        double dx = hx, dy = hy;
                        if (glancing) { double s = lat >= 0 ? 1 : -1; dx = hx * 0.6 + px * s; dy = hy * 0.6 + py * s; double l = Math.Sqrt(dx * dx + dy * dy); dx /= l; dy /= l; }
                        r.RunOverBy = v.Id; r.RunOverAt = Time; r.RunOverDirX = (float)dx; r.RunOverDirY = (float)dy; r.RunOverGlancing = glancing;
                        if (r.KnockUntil <= Time + 1e-9)
                        {
                            uint h = unchecked((uint)r.Id * 2654435761u ^ (uint)v.Id * 40503u ^ (uint)(long)Math.Round(Time * 1000.0) * 374761393u);
                            h ^= h >> 15; h = unchecked(h * 2246822519u); h ^= h >> 13;
                            r.KnockAt = Time; r.KnockUntil = Time + KnockBaseSec + KnockSpreadSec * (h % 1000u) / 1000.0;
                        }
                        int hurt = Math.Max(1, (int)(r.MaxHealth * (glancing ? RunOverGlanceFrac : RunOverHurtFrac)));   // head-on 25 %, glancing 10 % (never below 1 HP)
                        r.SetHealth(Math.Max(1, r.Health - hurt));
                        r.LastEnemyHit = Time;
                        RunOverHits++; if (glancing) RunOverGlances++;
                        Events.Add(new ZEvent { type = ZEventType.RunOverHit, objId = r.Id, otherId = v.Id, x = r.CenterX, y = r.CenterY, value = hurt, flag = glancing });
                        continue;
                    }
                    if (Time - r.RunOverAt < RunOverSquashDelay) continue;                                // he falls first
                    if (Math.Abs(lat) > halfW * RunOverSquashLat || along > halfLen * RunOverSquashAlong || along < -halfLen * 0.6) continue;
                    r.Squashed = true;                                                                     // driven over: flattened
                    r.SetHealth(0);
                    UpdateObjectHealth(r, v.Id);                                                           // Death event, veterancy credit to the crew
                    RunOverSquashes++;
                }
            }
        }
    }
}
