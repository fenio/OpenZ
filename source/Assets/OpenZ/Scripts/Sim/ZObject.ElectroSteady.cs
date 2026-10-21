using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// OpenZ deviation DV83 "the Electrobot stands and fires" (part of the Electrobot, DV45; he does not exist in Z).
    /// <para>He inherited three rules every Zod robot has, and with his weapon they made him walk about under fire instead of
    /// shooting. (1) A robot the shell is aimed at steps aside unless it holds a target at that moment (DodgeMissile,
    /// zobject.cpp:3657-3714): each shell gives a new random point and, while he walks to it, the next shell another one. He is
    /// without a target whenever the shooter stands outside or at the edge of his 144 px (heavy tank and missile cannon 144,
    /// Mobile Missile 160, howitzer 200, the fort's gun), while he walks, and for up to half a second after each kill. (2) A robot
    /// never fires while it moves (zobject.cpp:3519) and a moving robot does not look for targets (zobject.cpp:1615): on an agro or
    /// help walk towards one enemy he ignored every other enemy inside his range. (3) An agro walk returns to where it began when
    /// its target leaves the leash (zobject.cpp:2233-2328): back and forth with a target that hovers at the edge. With a 0.35 s
    /// windup that fizzles on the first step and one bolt every 4 s he got 0.2 to 0.7 bolts out before he died.</para>
    /// <para>DV83: (1) he never steps aside for a shell; (2) while an enemy unit he can hit stands inside his range and his gun is
    /// charged or winding up, he stands and fires before any walking step: an agro or help walk ends there (he stands and fights),
    /// an order (a move, an attack on another target, entering, a grenade crate, a step aside for a team-mate) waits for that one
    /// bolt, 0.35 s, and goes on while he recharges; the order's own target comes first when it is in range; (3) an agro walk never
    /// turns back: he stays where the chase ended; (4) chasing an agro target that moves away he closes to 12 px inside his range
    /// before he stands, so the target is still there when the bolt goes. Never changed: a fort rush, the walk out of a factory door, a stunned
    /// Electrobot, the weapon itself (ZObject.Electro.cs), an APC that carries him.</para>
    /// Deterministic: sim state and object order only, no random draw (the dodge he no longer makes drew two). Switch:
    /// ZWorld.ElectroSteadyDeviation (the game turns it on with the Electrobot at every start; headless default off, so every
    /// earlier proof of DV45 runs as before).
    /// </summary>
    public partial class ZObject
    {
        /// <summary>DV83 applies to this unit: an Electrobot on foot, the switch on.</summary>
        public bool ElectroSteady => ZWorld.ElectroSteadyDeviation && IsElectrobot;

        bool elStood;

        /// <summary>DV83, ProcessServer in front of the waypoint step: true = he stands this tick (the waypoint step is skipped;
        /// the passive check and the weapon run as on every tick).</summary>
        bool ElectroStand()
        {
            if (!ElectroSteady) return false;
            if (ElectroStandNow()) { elStood = true; return true; }
            if (elStood) { elStood = false; if (Waypoints.Count > 0) SetVelocity(); }       // the bolt is out: on with the order from where he stands
            return false;
        }

        bool ElectroStandNow()
        {
            var wp = Waypoints[0];
            if (wp.mode == WpMode.ForceMove || wp.mode == WpMode.EnterFort || PathInBuildingStage) return false;
            bool agro = wp.mode == WpMode.Agro;
            if (!agro && elPhase == ElectroPhase.Recharging) return false;                  // an order goes on while he recharges
            if (wp.mode == WpMode.Attack || agro)
            {
                var own = World.Get(wp.refId);
                if (own != null && CanAttackObject(own) && WithinAttackRadius(own)) return false;   // its own target is in range: the waypoint stands and fires at it (Zod)
            }
            var t = AttackObject != null && CanAttackObject(AttackObject) && WithinAttackRadius(AttackObject) ? AttackObject : ElectroNearestInRange();
            if (t == null) return false;
            if (agro) KillWP(); else StopMove();                                            // an agro / help walk ends here: he stands and fights
            Engage(t);
            PS.lastProgress = World.Time; PS.stillX = double.NaN; PS.heldTicks = 0; PS.wait = ZPathWait.None;   // standing to fire is not "stuck"
            World.ElectroStands++;
            return true;
        }

        /// <summary>DV83, ProcessAgroWP: his agro target is in range but moving away from him and still in the outer
        /// <see cref="ZWorld.ElectroStandBand"/> px of it: he walks on (closes in) instead of standing for a windup the target would
        /// leave before the bolt (0.35 s). A target that stands or comes nearer is fired at from full range. Never during a windup.</summary>
        bool ElectroCloseIn(ZObject t)
        {
            if (!ElectroSteady || elPhase == ElectroPhase.Windup || t == null || !t.IsMoving) return false;
            double rx = t.CenterX - CenterX, ry = t.CenterY - CenterY;
            if (t.Dx * rx + t.Dy * ry <= 0) return false;                                   // standing still or coming nearer
            return Math.Sqrt(rx * rx + ry * ry) > AttackRadius - ZWorld.ElectroStandBand;
        }

        /// <summary>DV83: the nearest enemy unit he can hit from where he stands (range, clear line), ties by object order.</summary>
        ZObject ElectroNearestInRange()
        {
            ZObject best = null; double bd = double.MaxValue;
            foreach (var o in World.Objects)
            {
                if (!o.IsUnit || o.Destroyed || o.KillAt >= 0 || o.Owner == Team.Neutral || o.Owner == Owner || !CanAttackObject(o)) continue;
                double d = Dist(CenterX, CenterY, o.CenterX, o.CenterY);
                if (d >= bd || d > AttackRadius || !WithinAttackRadius(o)) continue;
                bd = d; best = o;
            }
            return best;
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV83 switch (see ZObject.ElectroSteady.cs). The game turns it on at every start when the Electrobot is in
        /// the game; headless default off.</summary>
        public static bool ElectroSteadyDeviation = false;
        /// <summary>DV83: against an agro target that moves away he closes to this many px inside his range before he stands (a jeep
        /// covers 6 px in the 0.35 s windup, 10 px on a road).</summary>
        public const int ElectroStandBand = 12;
        /// <summary>DV83 statistics (checks): ticks an Electrobot of this world stood to fire in front of a waypoint.</summary>
        public int ElectroStands;
    }
}
