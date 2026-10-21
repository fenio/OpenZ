using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// OpenZ deviation DV130 "under fire, no flag run of its own" (K438, item 439: a player's robot under fire never leaves a fight by
    /// itself to capture a flag; a flag the player clicked is always obeyed).
    /// <para>What sent a player's robot to a flag by itself, and why a fight did not stop it:
    /// (1) the idle grab (ZObject.cs CheckPassiveEngageCore, Zod's zobject.cpp:1603-1853): an idle squad leader with no enemy in its agro
    /// range (attack range + 40 px) that it sees and has not found unreachable walks, running (DV54), to a flag that is not its team's
    /// within 220 px, and its squad follows it. A shooter that out-ranges it (a howitzer, a fort gun), one it cannot see, one it found it
    /// cannot reach, or a fight of the squad's other robot did not stop it;
    /// (2) the flag beside the way of the player's plain move (DV84, ZObject.GrabCertain.cs GrabOnTheWay), put in front of his order; on
    /// it the robot fights only enemies inside its own range (calm movement, ZObject.Calm.cs), so a laser, a sniper or a tank firing from
    /// just beyond that range shot it while it walked to the flag;
    /// (3) a flag run of its own already under way: Zod's attack-move turns it only towards an enemy within its range + 40 px that it sees.</para>
    /// <para>DV130, a robot of a player's team (a team no computer player drives), squad leader or squad: while its squad is IN A FIGHT IT CAN
    /// TAKE
    ///  - a robot of the squad holds an enemy unit as its target, or walks to attack or chase one, or did either in the last
    ///    <see cref="ZWorld.NoAutoFlagCalmSec"/> s and that enemy is still within its range + 40 px (a step aside or a moment without a
    ///    clear line does not end the fight), or
    ///  - an enemy unit stands where a robot of the squad can hit it from where it stands (in its range, a clear line), or
    ///  - the squad is under fire (a robot of it took damage from an enemy in the last <see cref="ZWorld.NoAutoFlagCalmSec"/> s, or an enemy
    ///    unit holds one of them as its target) and an enemy unit that a robot of the squad would go and fight is near: the game's own agro
    ///    test (within its range + 40 px, seen, not found unreachable),
    /// it starts no flag run of its own ((1) and (2)), a flag run of its own under way ends at the next half-second look ((3)): the squad
    /// stays, and the game's own rules pick its fight (an enemy in range, the chase of an enemy in its agro range, the squad's help call
    /// on the shooter, DV53), or it goes on with the player's order. It does not count as the closest unit for a flag (DV55), so a calm
    /// robot of the team may still take it. Fire it cannot answer from where it is (a howitzer or a fort gun beyond its reach, an enemy it
    /// cannot see or cannot get to) does not hold it: standing still there only gets it shot (counted: two Toughs under a howitzer 190 px
    /// away both destroyed standing, both alive when they ran to the flag as before). When the fight is over (no target, no enemy in range,
    /// nobody aiming at it, no hit for that long) its flag runs of its own work as before.</para>
    /// <para>Not changed: a flag, a move or any order the player gave (his waypoints are never touched); empty vehicles, guns and crates
    /// taken by themselves; vehicles; the computer's teams (computer against computer: every tick as before).</para>
    /// Deterministic: sim time and object order only, no random draw of its own. Switch <see cref="ZWorld.NoAutoFlagUnderFireDeviation"/>:
    /// View/ZGame.cs sets it at every start (off in 'Zod exact'); off in headless worlds and in the counted checks unless they set it
    /// (Editor/OzV11Kit.cs GameSwitches). Off = the game as before, tick for tick.
    /// </summary>
    public sealed partial class ZWorld
    {
        /// <summary>DV130 switch (see ZObject.NoAutoFlag.cs). View/ZGame.cs sets it at every start (off in 'Zod exact'); headless default off.</summary>
        public static bool NoAutoFlagUnderFireDeviation = false;
        /// <summary>DV130: a hit keeps a squad "under fire" this many seconds (the span of the "our fort is taking fire" warning): while an enemy
        /// it would go and fight is near, its flag runs of its own wait until this long after the last hit (and nobody aims at it).</summary>
        public static double NoAutoFlagCalmSec = 4.0;
        /// <summary>DV130 statistics (checks, the Play watch): half-second looks in which an idle robot's flag run was held; flag runs of its
        /// own ended under fire; flags beside the way of the player's move left alone under fire.</summary>
        public static int NoAutoFlagHeld, NoAutoFlagDropped, NoAutoFlagWayHeld;

        /// <summary>Turns the rule off until it is disposed, then puts back what was there. The parity report holds one while it measures
        /// (a Play session leaves the switch on); its own row turns the rule on by itself.</summary>
        public sealed class NoAutoFlagHold : IDisposable
        {
            readonly bool was;
            internal NoAutoFlagHold() { was = NoAutoFlagUnderFireDeviation; NoAutoFlagUnderFireDeviation = false; }
            public void Dispose() { NoAutoFlagUnderFireDeviation = was; }
        }
        public static NoAutoFlagHold NoAutoFlagOff() => new NoAutoFlagHold();
    }

    public partial class ZObject
    {
        int k438FoughtId = -1; double k438FoughtAt = -100;                                   // DV130: the enemy this robot last held as its target or walked to attack / chase, and when (its half-second looks)

        /// <summary>DV130 applies to this unit: a robot of a player's team, the switch on.</summary>
        public bool NoAutoFlagApplies => ZWorld.NoAutoFlagUnderFireDeviation && World != null && Kind == ZKind.Robot && Owner != Team.Neutral && !IsFortDefender && World.IsHumanTeam(Owner);

        /// <summary>DV130: this robot's squad (its leader and the leader's robots) is in a fight it can take now (see the class summary).
        /// Public: the counted check and the Play watch read it.</summary>
        public bool SquadUnderFire
        {
            get
            {
                if (World == null) return false;
                var lead = Leader ?? this;
                double now = World.Time;
                bool hit = false;
                if (Fights(lead, now, ref hit)) return true;
                foreach (var m in lead.Minions) if (m != null && Fights(m, now, ref hit)) return true;
                foreach (var e in World.Objects)
                {
                    if (!e.IsUnit || e.Destroyed || e.KillAt >= 0 || e.Owner == Team.Neutral || e.Owner == lead.Owner) continue;
                    var a = e.AttackObject;
                    bool fire = hit || (a != null && (a == lead || lead.Minions.Contains(a)));
                    if (Pressed(lead, e, fire)) return true;
                    foreach (var m in lead.Minions) if (m != null && Pressed(m, e, fire)) return true;
                }
                return false;
            }
        }

        /// <summary>DV130: the rule holds this robot's flag runs of its own at this moment (applies, and its squad is in a fight it can take).</summary>
        public bool FlagRunHeld => NoAutoFlagApplies && SquadUnderFire;

        /// <summary>DV130: robot m fights an enemy unit (its target, an attack / chase waypoint in front, or the enemy it held or went for in the
        /// calm span while that enemy is still within its range + 40 px); hit = it was hit in the calm span.</summary>
        static bool Fights(ZObject m, double now, ref bool hit)
        {
            if (m.Destroyed || m.KillAt >= 0) return false;
            if (now - m.LastEnemyHit < ZWorld.NoAutoFlagCalmSec) hit = true;
            if (Foe(m, m.AttackObject)) return true;
            if (m.Waypoints.Count > 0)
            {
                var w = m.Waypoints[0];
                if ((w.mode == WpMode.Attack || w.mode == WpMode.Agro) && Foe(m, m.World.Get(w.refId))) return true;
            }
            if (m.k438FoughtId >= 0 && now - m.k438FoughtAt < ZWorld.NoAutoFlagCalmSec)
            {
                var f = m.World.Get(m.k438FoughtId);
                if (Foe(m, f) && Within(m.CenterX, m.CenterY, f.CenterX, f.CenterY, m.AttackRadius + m.World.S.agro_distance)) return true;
            }
            return false;
        }

        static bool Foe(ZObject m, ZObject t) => t != null && t.IsUnit && !t.Destroyed && t.KillAt < 0 && t.Owner != Team.Neutral && t.Owner != m.Owner;

        /// <summary>DV130: robot m can hit enemy unit e from where it stands, or (fire = its squad is under fire) e is near enough that the
        /// game's own agro would send m to fight it (within its range + 40 px, seen, not found unreachable).</summary>
        static bool Pressed(ZObject m, ZObject e, bool fire)
        {
            if (m.Destroyed || m.KillAt >= 0) return false;
            if (!Within(m.CenterX, m.CenterY, e.CenterX, e.CenterY, m.AttackRadius + m.World.S.agro_distance) || !m.CanAttackObject(e)) return false;
            if (m.WithinAttackRadius(e)) return true;
            return fire && m.WithinAgroRadius(e) && !m.PathUnreachable(e);
        }

        /// <summary>DV130, CheckPassiveEngageCore, before it picks between an empty vehicle, a flag and a crate: an idle robot of a player's
        /// team whose squad is in a fight it can take takes no flag by itself (true = the flags it found are left; an empty vehicle or a crate as before).</summary>
        bool AutoFlagHeld()
        {
            if (!FlagRunHeld) return false;
            ZWorld.NoAutoFlagHeld++;
            return true;
        }

        /// <summary>DV130, GrabOnTheWay (DV84), for a flag beside the way of the player's plain move: left alone while the squad is in a fight it can
        /// take (the order goes on; an empty vehicle beside the way as before).</summary>
        bool AutoFlagWayHeld()
        {
            if (!FlagRunHeld) return false;
            ZWorld.NoAutoFlagWayHeld++;
            return true;
        }

        /// <summary>DV130, CheckPassiveEngageCore on its half-second look, right after DV84's watch (before a walking robot leaves the look):
        /// first the note of the enemy this robot holds as its target or walks to attack or chase (each robot of a player's team); then, for
        /// a squad leader, a flag run of its own under way (the first waypoint, a move onto a flag that is not its team's, not the player's)
        /// ends while the squad is in a fight it can take. The squad stays (the run's speed ends with it); the rest of the look then picks
        /// its fight, or the player's order behind it goes on. Not a failed try for DV84 (no 20 s memory): when the fight is over it may take
        /// that flag. The note of an attack or chase waypoint matters here: the run's own attack-move gives a squad robot such a waypoint, and
        /// the squad's copy of the leader's list that follows the run's end clears it; without the note the next look found no fight, the
        /// run began again and the robot was called off again, every second (the robot now goes for that enemy by itself instead).</summary>
        void AutoFlagDrop(double time)
        {
            if (!NoAutoFlagApplies) return;
            var fought = Foe(this, AttackObject) ? AttackObject : null;                          // (every robot of the squad keeps its own note)
            if (fought == null && Waypoints.Count > 0 && (Waypoints[0].mode == WpMode.Attack || Waypoints[0].mode == WpMode.Agro))
            {
                var t = World.Get(Waypoints[0].refId);
                if (Foe(this, t)) fought = t;
            }
            if (fought != null) { k438FoughtId = fought.Id; k438FoughtAt = time; }
            if (IsMinion || Waypoints.Count == 0) return;
            var wp = Waypoints[0];
            if (wp.mode != WpMode.Move || wp.playerGiven || wp.refId < 0) return;
            var f = World.Get(wp.refId);
            if (f == null || f.Kind != ZKind.MapItem || f.Type != "flag" || f.Owner == Owner || !SquadUnderFire) return;
            if (grabId == f.Id) grabId = -1;
            KillWP();
            IsRunning = false;
            ZWorld.NoAutoFlagDropped++;
        }
    }
}
