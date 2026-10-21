using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// Version 1.1, calm movement, parts 4 and 5: calm in a fight, the player's order first; who steps aside at all (the switch: ZWorld.CalmMoveDeviation, see ZCalm.cs).
    /// <para>What gives a unit a move of its own while it fights or is under fire, and where:
    /// the step aside from a shell or a grenade (ZObject.cs DodgeMissile: one for every shell fired at it, in a random direction, put in
    /// front of whatever it does; a second shell re-aims a step that is under way); the attack on the way of an attack-move (ZObject.cs
    /// CheckAttackTo: an ATTACK waypoint on a random enemy in range, put in front of the move; the unit follows that enemy until it is dead
    /// or cannot be attacked); the chase of an idle unit (ZObject.cs CheckPassiveEngage, every 0.5 s: an AGRO waypoint, with the walk
    /// back); the help call of a squad (ZObject.AiCombat.cs: an AGRO waypoint, once a second per victim, idle units only); a vehicle's
    /// duel manoeuvres (ZObject.Duel.cs, inside an attack, each kept for its own time); a flag, an empty vehicle or a crate taken by an
    /// idle unit or beside the way of the player's move (ZObject.GrabCertain.cs); a rock shot out of the way (ZObject.cs
    /// DoAttackImpassableAtCoords); the spacing layer's helper moves (step aside, back off, regroup, a waiting bay); the pad return after a
    /// repair (ZObject.RepairBay.cs); the computer's order rounds (ZBot.cs).</para>
    /// <para>THE PLAYER'S ORDER FIRST. While a unit of a player's team holds an order the player gave (a move, a capture, an enter, an
    /// attack on a named target, a repair, a fort entry: any waypoint he sent that is still in its list):
    ///  - the small things stay as before: the step aside from a shell or a grenade (a vehicle: exactly as before; a robot: by the rule
    ///    Below), the flag or the empty vehicle taken beside its way, a rock shot out of its way. Each is put
    ///    in front of the order and the order goes on after it;
    ///  - it still fights what it meets, but it does not leave its way for it: of the enemies near its way it attacks only one it can
    ///    hit from where it stands (before: any enemy within its range + 40 px, which it then walked towards and followed until it
    ///    was dead); it stops on its way and fires, and walks on the moment that enemy is dead or out of its range. A vehicle's duel
    ///    manoeuvres during such a stop end CalmOrderFightFar px from its way. The click with Ctrl held (ZWaypoint.fightOnWay) gives
    ///    the attack-move as it always was, approach and chase included;
    ///  - his move is never ended early far from where he sent it. Before, the stuck guard's way round standing units could lead to
    ///    a point short of his goal (the goal "walled in" by something that stands in a gap) and arriving at that point ended his
    ///    order; and the guard's last resort gave a move up after three steps without a way through or 12 s without movement. Now,
    ///    farther than KeepGoalFar px from the ordered point, that way round is not taken and the guard plans the way again (ZCalm.cs
    ///    CalmKeepsGoal, CalmKeepsMove). Nearer than that nothing changes: there it is what lets a unit stop beside its team.</para>
    /// <para>WHO STEPS ASIDE FROM A SHELL OR A GRENADE AT ALL (robots; with or without an order):
    ///  - TOUGHS and SNIPERS never do. They keep their place and fire, the way the Electrobot does (ZObject.ElectroSteady.cs): no step
    ///    aside, no random draw for one. They still walk on an order, turn to their target, keep the squad's spacing and make room for a
    ///    vehicle of their own team;
    ///  - GRUNTS, PSYCHOS, PYROS and LASERS still step aside, but not for every shell and not for every grenade. A fixed rule, no random
    ///    draw: after a step aside by any body of a squad, no body of that squad steps aside again for CalmRobotDodgeRest s; and while a
    ///    body has an enemy it can hit inside its range, it does not step aside before the squad has fired once since its last step
    ///    aside (it shoots first; counted, this part almost never acts: a robot that holds a target at that moment never stepped
    ///    aside anyway). A step aside that was begun is walked to its point, as before;
    ///  - VEHICLES step aside exactly as before.</para>
    /// <para>ON THE PLAYER'S ORDER a row of steps aside is short: each new shell re-aims the step under way, as before, but
    /// CalmOrderAsideTime s after the first began, or CalmOrderAsideFar px from where the unit left its way, the step under way is
    /// finished and the unit goes back to his order (robots and vehicles).</para>
    /// <para>NOT SHIPPED, KEPT AS SWITCHES (both off). ZWorld.CalmDodgeEnds: a robot's step aside ends CalmDodgeEndWait s after the
    /// shell or grenade it was for has gone off (as shipped it walks on to its point, up to half as far again). Counted, light
    /// robots then spend clearly less time stepping aside and lose clearly more fights against grenades (stepping aside is what
    /// saves them there), so it is left off (SPEC.md part 5.4). ZWorld.CalmVehicleArbiter: for
    /// vehicles without the player's order (a step aside under way not re-aimed for CalmDodgeHold s, CalmDodgeRest s between two,
    /// a duel manoeuvre not broken off in its first CalmManoeuvreHold s). Counted, it halves their turning in a fight and costs
    /// them 8 to 20 fights in 100, so vehicles step aside exactly as before.</para>
    /// Sim only, fixed tick, object order. With the switch on fewer random numbers are drawn than before (a step aside that is not
    /// taken draws none), so a match differs from the same match with the switch off from the first held step on.
    /// </summary>
    public sealed partial class ZWorld
    {
        // ---- numbers (s). Chosen with the counts of the kit (SPEC.md part 3).
        public static double CalmRobotDodgeRest = 8.0;   // grunts, psychos, pyros, lasers: from the start of one step aside of a squad to the next
        public static int CalmShootFirst = 1;            // ... and not before the squad has fired once since, while an enemy is in range (0 = this part off)
        public static bool CalmStandFirm = true;         // Toughs and Snipers never step aside
        public static bool CalmDodgeEnds = false;        // NOT shipped (true = the other robots: a step aside ends when what it was for has gone off; counted, it costs them fights against grenades) ...
        public static double CalmDodgeEndWait = 0.1;     // ... this long after it
        public static bool CalmVehicleArbiter = false;   // vehicles step aside exactly as before (true = those without the player's order: one move of their own at a time; counted, it costs them fights: SPEC.md part 3)
        public static double CalmDodgeHold = 1.0;        // ... a step aside under way is not re-aimed for this long
        public static double CalmDodgeRest = 2.5;        // ... from the start of one step aside to the start of the next
        public static double CalmManoeuvreHold = 1.0;    // ... a duel manoeuvre this young is not broken off by a step aside
        /// <summary>false = a unit on the player's plain move still walks towards and after an enemy it meets (the attack-move as before).</summary>
        public static bool CalmOrderNoChase = true;
        public static double CalmOrderFightFar = 64, CalmOrderFightRest = 3.0;   // an attack on the way of his order: never this far from its way; then it walks on this long
        /// <summary>A unit on the player's order: steps aside that follow one another (each shell re-aims the step under way, as before) end
        /// this long after the first began, or this far from where it left its way: the step under way is finished, then it is back on
        /// his order.</summary>
        public static double CalmOrderAsideTime = 4.0, CalmOrderAsideFar = 64;
        /// <summary>Statistics (checks): steps aside not taken (vehicles; robots by the rest; robots that shoot first; Toughs and Snipers
        /// that stand), attacks on the way ended because the target left the range or the unit its way.</summary>
        public static int CalmOrderAsidesHeld, CalmDodgesEnded, CalmDodgesHeld, CalmRobotDodgesHeld, CalmShootsFirst, CalmStoodFirm, CalmChasesEnded;
    }

    public partial class ZObject
    {
        /// <summary>A/B of the headless runner only (the game never sets it): the teams the fight rules apply to, one bit per team; -1 = all.</summary>
        internal static int CalmTeams = -1;
        bool CalmFightOn => ZWorld.CalmMoveDeviation && (CalmTeams < 0 || ((CalmTeams >> (int)Owner) & 1) != 0);

        /// <summary>A unit of a player's team that still holds an order the player gave (any waypoint of its list).</summary>
        public bool PlayerOrderHeld
        {
            get
            {
                if (World == null || Owner == Team.Neutral || Waypoints.Count == 0 || !World.IsHumanTeam(Owner)) return false;
                for (int i = 0; i < Waypoints.Count; i++) if (Waypoints[i].playerGiven) return true;
                return false;
            }
        }

        /// <summary>A Tough or a Sniper on foot, the switch on: it never steps aside, it stands and fires.</summary>
        public bool CalmStandsFirm => ZWorld.CalmMoveDeviation && ZWorld.CalmStandFirm && IsRobot && (Type == "tough" || Type == "sniper");

        /// <summary>DodgeMissile, after every reason the rules already have not to step aside and before its random draws: not now.</summary>
        bool CalmHoldsDodge(double timeTillExplode)
        {
            if (!CalmFightOn) return false;
            double now = World.Time; var c = Calm;
            bool dodging = Waypoints.Count > 0 && Waypoints[0].mode == WpMode.Dodge;
            if (IsRobot)
            {
                if (ZWorld.CalmStandFirm && (Type == "tough" || Type == "sniper")) { ZWorld.CalmStoodFirm++; return true; }
                if (ZWorld.CalmRobotDodgeRest > 0)
                {
                    var q = (IsMinion && Leader != null ? Leader : this).Calm;             // the squad's state is kept by its leader
                    if (now < q.dodgeNext) { ZWorld.CalmRobotDodgesHeld++; return true; }
                    if (ZWorld.CalmShootFirst > 0 && (ZWorld.CalmShootFirst > 1 || q.dodgeStart > -50) && q.lastShot <= q.dodgeStart && ElectroNearestInRange() != null) { ZWorld.CalmShootsFirst++; return true; }
                    q.dodgeStart = now; q.dodgeNext = now + ZWorld.CalmRobotDodgeRest; c.dodgeOver = now + timeTillExplode;
                }
            }
            else if (ZWorld.CalmVehicleArbiter && !PlayerOrderHeld)
            {
                if ((dodging && now - c.dodgeStart < ZWorld.CalmDodgeHold) || (!dodging && now < c.dodgeNext) || (duel != null && duel.active && now - duel.start < ZWorld.CalmManoeuvreHold))
                { ZWorld.CalmDodgesHeld++; return true; }
                c.dodgeStart = now; c.dodgeNext = now + ZWorld.CalmDodgeRest;
            }
            // a unit on the player's order: steps aside one after the other do not carry it away from its way
            if (dodging && PlayerOrderHeld)
            {
                double dx = CenterX - c.leftX, dy = CenterY - c.leftY;
                if (now - c.leftAt >= ZWorld.CalmOrderAsideTime || dx * dx + dy * dy >= ZWorld.CalmOrderAsideFar * ZWorld.CalmOrderAsideFar) { ZWorld.CalmOrderAsidesHeld++; return true; }
            }
            if (!dodging) { c.leftAt = now; c.leftX = CenterX; c.leftY = CenterY; }
            return false;
        }

        /// <summary>ProcessDodgeWP, only with ZWorld.CalmDodgeEnds (not shipped: off): the shell or grenade this robot stepped aside from
        /// has gone off: the step aside ends here (as shipped, and before: it walks on to its point, up to half as far again).</summary>
        bool CalmDodgeOver()
        {
            if (!CalmFightOn || !ZWorld.CalmDodgeEnds || !IsRobot || World.Time < Calm.dodgeOver + ZWorld.CalmDodgeEndWait) return false;
            ZWorld.CalmDodgesEnded++;
            return true;
        }

        /// <summary>ProcessAttackDamage, the moment a shot, a rocket or a grenade leaves: the squad has fired (the rule "it shoots first").</summary>
        void CalmShot(double time)
        {
            if (!ZWorld.CalmMoveDeviation || !IsRobot) return;
            (IsMinion && Leader != null ? Leader : this).Calm.lastShot = time;
        }

        /// <summary>A unit of a player's team that holds an order the player gave without the Ctrl mark (ZWaypoint.fightOnWay): the rule
        /// Applies to it.</summary>
        bool CalmKeepsOrder()
        {
            if (!ZWorld.CalmMoveDeviation || !ZWorld.CalmOrderNoChase || World == null || Owner == Team.Neutral || !World.IsHumanTeam(Owner)) return false;
            bool held = false;
            for (int i = 0; i < Waypoints.Count; i++) if (Waypoints[i].playerGiven) { if (Waypoints[i].fightOnWay) return false; held = true; }
            return held;
        }

        /// <summary>CheckAttackTo (the attack on the way of an attack-move), for each enemy it would choose from: on the player's order
        /// only an enemy the unit can hit from where it stands on its way (inside its attack range, a clear line). So it stops on its
        /// way and fires; it does not walk towards an enemy that is only near (before: attack range + 40 px).</summary>
        bool CalmMayFight(ZObject o)
        {
            if (!CalmKeepsOrder()) return true;
            return World.Time >= Calm.noFightUntil && WithinAttackRadius(o);
        }

        /// <summary>ProcessAttackWP, an attack on the way in front of the player's order (not an attack he ordered, not a rock in the way):
        /// it ends, and the order goes on, the moment the target is out of the unit's range (no chase), or the unit is more than
        /// CalmOrderFightFar px from where it left its way (a vehicle's duel manoeuvres); after the second it walks on for
        /// CalmOrderFightRest s before it may stop to fight again.</summary>
        bool CalmNoChase(ZObject t)
        {
            if (t == null || !t.IsUnit || Waypoints[0].playerGiven || !CalmKeepsOrder()) return false;
            var c = Calm; double now = World.Time;
            if (c.fightRef != t.Id || now - c.fightSeen > 0.05) { c.fightRef = t.Id; c.fightX = CenterX; c.fightY = CenterY; }
            c.fightSeen = now;
            double dx = CenterX - c.fightX, dy = CenterY - c.fightY;
            bool far = dx * dx + dy * dy > ZWorld.CalmOrderFightFar * ZWorld.CalmOrderFightFar;
            if (!far && WithinAttackRadius(t)) return false;
            if (far) c.noFightUntil = now + ZWorld.CalmOrderFightRest;
            c.fightRef = -1; ZWorld.CalmChasesEnded++;
            return true;
        }
    }
}
