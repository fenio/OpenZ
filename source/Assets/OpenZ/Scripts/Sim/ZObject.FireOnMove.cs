namespace OpenZ.Sim
{
    /// <summary>
    /// OpenZ deviation DV57 "fire on the move".
    /// <para>Zod: a robot never attacks while it moves: ProcessMove calls Disengage() on every step (zobject.cpp:3519 "robots don't
    /// attack while moving") and CheckPassiveEngage skips a moving robot, so a walking squad ignores the enemy it passes.</para>
    /// <para>DV57: a walking GRUNT, PSYCHO or LASER robot (leaders and squad members; any walk but a fort rush or a repair trip) keeps
    /// its target while it steps and picks one up in range on the passive check (every 0.5 s, engage only: no agro / grab waypoint, the
    /// order goes on). A shot fired while it moves uses its own gun only (no grenade throw) and hits WalkHitFactor x its hit chance;
    /// damage, rate and range are Zod's; a target that left the range while walking is dropped before the shot (no cooldown used).
    /// Tough, pyro, sniper and the Electrobot keep Zod: they stand to fire (the sniper's kneel / prone and the Tough's brace are the
    /// view's stances). The view already aims the upper body over the walking legs (UnitAim.AdjustBodyYaw: torso twist up to 80 deg,
    /// beyond that the body faces the target and strafes / back-pedals; UnitView up_shooting layer) and plays the shot's FX + sound
    /// from the Hitscan event like any shot. Deterministic: sim time, object order, the one Zod roll per shot.</para>
    /// OFF = Zod exact (PlayerPrefs oz_fire_on_move 0, 'Zod exact' oz_zod_exact 1, every parity Zod row).
    /// </summary>
    public partial class ZObject
    {
        /// <summary>DV57: a light robot (grunt / psycho / laser) that fires while it walks, on this waypoint.</summary>
        public bool FiresOnMove
        {
            get
            {
                if (!ZWorld.FireOnMoveDeviation || Kind != ZKind.Robot || IsFortDefender) return false;
                if (Type != "grunt" && Type != "psycho" && Type != "laser") return false;
                if (Waypoints.Count == 0) return true;
                var m = Waypoints[0].mode;
                return m != WpMode.EnterFort && m != WpMode.CraneRepair && m != WpMode.UnitRepair;
            }
        }

        /// <summary>DV57 statistics (checks / proofs): shots fired while walking, hits among them.</summary>
        public int WalkShots { get; private set; }
        public int WalkHits { get; private set; }

        /// <summary>ProcessMove's step: Zod drops the target of every moving robot; a DV57 walker keeps it.</summary>
        void WalkDisengage()
        {
            if (FiresOnMove && AttackObject != null && !AttackObject.Destroyed) return;
            Disengage();
        }

        /// <summary>ProcessAttackDamage before the cooldown: a walking DV57 shooter whose target left its range drops it (true = no shot).</summary>
        bool WalkShotOutOfRange(ZObject t) => IsMoving && FiresOnMove && !WithinAttackRadius(t);

        /// <summary>DV57: a shot fired while walking (gun only, lower accuracy).</summary>
        bool WalkingShot => IsMoving && FiresOnMove;

        void NoteWalkShot(bool hit) { WalkShots++; if (hit) WalkHits++; ZWorld.WalkShotsTotal++; if (hit) ZWorld.WalkHitsTotal++; }
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV57 fire on the move (see ZObject.FireOnMove.cs). PlayerPrefs oz_fire_on_move; ZGame sets it at every start.</summary>
        public static bool FireOnMoveDeviation = true;
        /// <summary>DV57: hit chance while walking, x Zod's damage_chance (veterans x1.10 first).</summary>
        public const double WalkHitFactor = 0.6;
        /// <summary>DV57 statistics (Play proofs): walking shots / hits of every unit since the last reset.</summary>
        public static int WalkShotsTotal, WalkHitsTotal;
    }
}
