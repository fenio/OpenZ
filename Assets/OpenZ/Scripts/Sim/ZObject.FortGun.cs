using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// In Zod a fort has no weapon of its own (bfort.cpp); its
    /// guns are the ordinary cannons on its 4 turret spots. Here the big gun on the fort roof is a real weapon of the fort
    /// building itself, with the Zod HOWITZER's numbers (default_settings unit.howitzer: attack radius 200 px from the fort
    /// centre, damage, 40 px splash, shell speed 95 px/s, one shot every 4.86 s). It has no crew (no sniping, no capture), it
    /// cannot be targeted on its own, and it stops only when the fort is destroyed. Targets: the nearest enemy unit in range
    /// (sticky while it stays valid); a new target costs a 1.4 s lay (the turret slews onto it), so the view's barrel is on
    /// target when the real shell leaves. fortgun_v2 envelope: no target closer than FortGunMinEdgePx (64 px = 4 tiles) to the
    /// fort's walls (the 16 px scatter + the 40 px splash can never reach the fort's own walls and gate apron, and every arc
    /// clears the towers and the tower cannons), and none inside the rear arc +-FortGunRearDeg (34 deg) behind the fort, where
    /// the fort flag pole and its cloth stand in the barrel's way (fort_front: north, fort_back: south).
    /// Toggle: ZWorld.FortGunDeviation (PlayerPrefs oz_fortgun_sim, default on);
    /// listed in the parity report's Deviations.
    /// </summary>
    public partial class ZObject
    {
        /// <summary>The fort gun's current target (view: FortGunView turns the turret onto it).</summary>
        public ZObject FortGunTarget { get; private set; }
        /// <summary>Game time of the fort gun's next possible shot (view: a new target's lay).</summary>
        public double FortGunReadyAt => Math.Max(fortGunNext, fortGunLayUntil);
        double fortGunNext, fortGunLayUntil, fortGunScan;
        public const double FortGunLay = 1.4;
        /// <summary>
        /// under DV7): the fort gun reaches this many px past the Zod howitzer's attack radius (+2 tiles).</summary>
        public const int FortGunRangeBonusPx = 32;
        /// <summary></summary>
        public const int FortGunSplashBonusPx = 12;
        /// <summary>
        /// From this many sim seconds before its shot (the view starts its
        /// visible lay: barrel climb 5 s, head tilt 1.1 s) the gun is COMMITTED: it never drops or swaps that target and fires at it, or,
        /// when it died / left the envelope meanwhile, at the last spot where it stood inside the envelope.</summary>
        public const double FortGunCommitLead = 5.5;
        /// <summary>The gun has committed to its next shot (view: lays on FortGunAimX / FortGunAimY, never aborts).</summary>
        public bool FortGunCommitted { get; private set; }
        /// <summary>The committed shot's aim point (px): the target's centre while it stays valid, else its last valid spot.</summary>
        public int FortGunAimX { get; private set; }
        public int FortGunAimY { get; private set; }
        /// <summary>The fort gun's attack radius (px): the Zod howitzer's + FortGunRangeBonusPx.</summary>
        public static int FortGunRange(ZUnitSettings u) => u.attack_radius + FortGunRangeBonusPx;
        /// <summary>fortgun_v2: minimum distance (px) between a target's centre and the fort's footprint (walls, gate apron).</summary>
        public const int FortGunMinEdgePx = 64;
        /// <summary>fortgun_v2: half-width (deg) of the rear arc the gun cannot fire into (the fort flag stands behind the turret).</summary>
        public const int FortGunRearDeg = 34;

        /// <summary>fortgun_v2: inside the DV7 envelope? Integer maths only (deterministic): the target point is at least
        /// FortGunMinEdgePx from the fort rectangle and outside the rear arc (cos^2 34 deg = 11/16).</summary>
        public bool FortGunCanReach(int x, int y)
        {
            long ex = Math.Max(0, Math.Max(LocX - x, x - (LocX + WidthPix)));
            long ey = Math.Max(0, Math.Max(LocY - y, y - (LocY + HeightPix)));
            if (ex * ex + ey * ey < (long)FortGunMinEdgePx * FortGunMinEdgePx) return false;
            long dx = x - CenterX, dy = y - CenterY, back = Type == "fort_front" ? -dy : dy;             // rear: fort_front north, fort_back south
            return !(back > 0 && back * back * 16 >= (dx * dx + dy * dy) * 11);
        }

        internal void ProcessFortGun(double time)
        {
            if (!ZWorld.FortGunDeviation || !IsFort || Destroyed || Owner == Team.Neutral || FortGunDestroyed || (World.GameOver && ZWorld.EndFreezeDeviation)) { FortGunTarget = null; FortGunCommitted = false; return; }   // combat_nb_v1 DV25: a destroyed gun is silent; DV10: none after the game over
            var u = World.S.Unit("howitzer");
            if (u == null || u.Damage == 0) return;
            int range = FortGunRange(u);
            if (time >= fortGunScan)
            {
                fortGunScan = time + 0.5;
                if (!FortGunCommitted && !FortGunValid(FortGunTarget, range))                           // a committed target is never dropped or swapped
                {
                    ZObject best = null; double bd = double.MaxValue;
                    foreach (var o in World.Objects)
                    {
                        if (!FortGunValid(o, range)) continue;
                        double d = Dist(CenterX, CenterY, o.CenterX, o.CenterY);
                        if (d < bd) { bd = d; best = o; }
                    }
                    if (best != FortGunTarget) { FortGunTarget = best; if (best != null) fortGunLayUntil = time + FortGunLay; }
                }
            }
            var t = FortGunTarget;
            bool valid = FortGunValid(t, range);
            // target while it stays valid and keeps its last valid spot (always inside the envelope) when it dies or leaves
            if (!FortGunCommitted && valid && time >= FortGunReadyAt - FortGunCommitLead) FortGunCommitted = true;
            if (FortGunCommitted && valid) { FortGunAimX = t.CenterX; FortGunAimY = t.CenterY; }
            if (!FortGunCommitted)
            {
                if (t != null && !valid) { FortGunTarget = null; fortGunScan = time; }               // fortgun_v2: it left the envelope since the last scan
                return;
            }
            if (time < fortGunLayUntil || time < fortGunNext) return;
            bool mortar = ZWorld.FortMortarDeviation;                                            // combat_nb_v1 DV25: 30 s reload, a slow lob at where the target stands
            fortGunNext = time + (mortar ? FortMortarReload : u.attack_speed); FortGunLastShot = time;
            int tx = FortGunAimX, ty = FortGunAimY;
            if (!mortar && valid && EstimateMissileTarget(t, u.attack_missile_speed, out int lx, out int ly) && FortGunCanReach(lx, ly)) { tx = lx; ty = ly; }   // DV25: no lead (it can be dodged)
            tx += World.Rng.Next(32) - 16; ty += World.Rng.Next(32) - 16;                      // the howitzer's scatter (zobject.cpp:2155)
            var m = new ZMissile { team = Owner, attackerId = Id, targetId = t != null ? t.Id : -1, damage = u.Damage, radius = u.attack_damage_radius + FortGunSplashBonusPx, x = tx, y = ty };
            if (mortar) m.explodeTime = time + FortMortarFlight(Dist(CenterX, CenterY, tx, ty));   // DV25: the lob's flight (2.9-4.2 s)
            else m.CalcExplodeTime(CenterX, CenterY, u.attack_missile_speed, time);
            World.AddMissile(m);
            World.Emit(ZEventType.MissileFired, this, t, tx, ty, (float)(m.explodeTime - time), m.radius, false);
            if (valid) t.DodgeMissile(tx, ty, m.explodeTime - time);
            FortGunCommitted = false;
            if (!valid) { FortGunTarget = null; fortGunScan = time; }
        }

        bool FortGunValid(ZObject o, int radius) =>
            o != null && o.IsUnit && !o.Destroyed && o.KillAt < 0 && o.Owner != Team.Neutral && o.Owner != Owner
            && Within(CenterX, CenterY, o.CenterX, o.CenterY, radius) && FortGunCanReach(o.CenterX, o.CenterY);   // fortgun_v2 envelope
    }
}
