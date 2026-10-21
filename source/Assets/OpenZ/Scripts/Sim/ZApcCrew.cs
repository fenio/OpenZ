using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenZ.Sim
{
    /// <summary>
    ///
    /// Zod (kept when <see cref="ZWorld.ApcCrewDeviation"/> is off, and in 'Zod exact'): the APC's crew is ONE list of drivers of
    /// one driver_type (zobject.h:209-213): an owned APC carries its one grunt (zvehicle.cpp:155-168); only a NEUTRAL APC can be
    /// entered, and then the leader and every minion become drivers (zserver.cpp:1375-1421); it fires the crew robot's weapon with
    /// damage x crew size (vapc.cpp:266-342); EJECT pops the whole crew out as one squad and the APC turns neutral
    /// (zserver_events.cpp:631-731); robots never auto-enter an APC (zobject.cpp:1679); the APC is never snipable (no
    /// can_be_sniped flag in vapc.cpp; SnipeParityTests "crewed apc never snipable").
    ///
    /// DV15 (on):
    ///  - Crew = ONE driver (Drivers[0], DriverType) + ONE passenger squad (Passengers = per-robot HP in squad order, PassengerType,
    ///    PassengerGrenades = the squad's grenade pool). The driver never leaves on an order.
    ///  - An owned, crewed APC with no passengers takes a same-team squad that is ordered to enter it (the same ENTER waypoint and
    ///    the same SendWaypoints command path the player and the bot use); the leader and all its minions board, like Zod's APC.
    ///  - A second squad is refused while passengers are aboard (Zod keeps one driver_type per vehicle, so a mixed crew has no weapon
    ///    to fire; merging two squads would give back one oversized squad on exit, which Zod's squads never are (group_amount);
    ///    and it caps the firepower at one squad). The refused order is dropped at validation (CheckWaypoint), and a squad already
    ///    walking there when another one got in first stops next to the APC (its ENTER waypoint ends).
    ///  - A neutral APC (never crewed, or emptied in 'Zod exact' games) is boarded as in Zod; then the leader becomes the driver and
    ///    its minions the passengers (a lone robot only drives).
    ///  - Firing (ResetDamageInfo, Zod's VAPC rules): the weapon is the passengers' robot type when passengers are aboard, else the
    ///    driver's; damage x every robot aboard carrying that weapon (the passengers, plus the driver if he is of the same type).
    ///    A single squad that boards a neutral APC therefore fires exactly as in Zod (same type, x squad size); the default grunt
    ///    driver alone fires as Zod's one-grunt crew.
    ///  - EXIT (ZWorld.Eject, the same command): the passenger squad pops out next to the APC with its stored health and grenades,
    ///    the driver stays, the APC stays owned and keeps its orders. With no passengers Eject is refused (CanEjectCrew() = false:
    ///    the HUD greys its EXIT button, the cursor shows no EXIT).
    ///  - The APC turns neutral only when its driver dies (a driver snipe; Zod's APC is never snipable, so this only happens if a
    ///    later change makes it snipable): the driver is lost (Zod: a sniped crew is cleared) and the passengers pop out alive for
    ///    the team that owned the APC. A destroyed APC kills everyone aboard (Zod: the crew dies with the vehicle).
    ///  - A repair station keeps the passengers with the drivers while the APC is inside.
    /// Deterministic: no random draw, no frame time, object creation order fixed; every change goes through the existing
    /// commands (SEND_WAYPOINTS ENTER, EJECT_VEHICLE).
    /// </summary>
    public partial class ZObject
    {
        // ---- DV15 passengers (APC only)
        public string PassengerType = "grunt";
        public readonly List<int> Passengers = new List<int>();
        public int PassengerGrenades;

        public bool IsApc => Kind == ZKind.Vehicle && Type == "apc";
        bool ApcCrewRules => (IsApc && ZWorld.ApcCrewDeviation) || IsShuttle;   // DV118: the playable shuttle carries one squad by these rules (Sim/ZWorld.Shuttle.cs)

        /// <summary>DV15: this owned, crewed APC takes `team`'s squad as passengers now (no passengers aboard yet).</summary>
        public bool CanTakePassengers(Team team) =>
            (ApcCrewRules && ShuttleDoorOk && !Destroyed && KillAt < 0 && Owner != Team.Neutral && Owner == team && Drivers.Count > 0 && Passengers.Count == 0)   // DV118: the shuttle only on the ground
            || ShuttleEmptyTakes(team);                                                                   // DV118 (K256f): an EMPTY shuttle takes a human team's ordered squad, its leader the pilot (Sim/ZWorld.ShuttleEmpty.cs)

        /// <summary>DV15: robot `r` (a squad leader or one of its minions) may board this APC as a passenger.</summary>
        public bool CanTakePassengers(ZObject r) => r != null && r.IsRobot && !r.Destroyed && CanTakePassengers(r.Owner);

        /// <summary>The EXIT command would do something now (HUD button, cursor, right-click): DV15 APC = passengers aboard; Zod rule
        /// otherwise (APC / ejectable cannon with a crew, vapc.cpp:261, zcannon.cpp:51).</summary>
        public bool CanEjectCrew()
        {
            if (Destroyed || Owner == Team.Neutral) return false;
            if (ApcCrewRules) return (Passengers.Count > 0 || ShuttleHeroPilot) && (ShuttleDoorOk || ShuttleAirExitOk()) && ShuttleCrewSettled;   // K256f2: a hero pilot gets out too (Sim/ZWorld.ShuttleHeroes.cs)   // DV118: the shuttle lets them out on the ground, once the squad walking in has sat down; K256f: in the air only where it may land and let them out (Sim/ZWorld.ShuttleEmpty.cs)
            return CanEjectDrivers() && Drivers.Count > 0;
        }

        /// <summary>The robot type whose weapon the APC fires (view: sounds, tracers, projectiles). Anything else: DriverType.</summary>
        public string CrewWeaponType => ApcCrewRules && Passengers.Count > 0 ? PassengerType : DriverType;

        /// <summary>Robots aboard firing the APC's weapon (Zod: driver_info.size(), vapc.cpp:341).</summary>
        public int CrewFirepower
        {
            get
            {
                if (!ApcCrewRules) return Drivers.Count;
                if (Passengers.Count == 0) return Drivers.Count * ZWorld.CrewRifleMul;
                return Passengers.Count + (Drivers.Count > 0 && DriverType == PassengerType ? Drivers.Count : 0);
            }
        }

        /// <summary>Robots aboard (HUD "CREW").</summary>
        public int CrewAboard => Drivers.Count + Passengers.Count;

        /// <summary>DV15: a boarding squad leader follows a moving APC (its ENTER waypoint aims at where the APC was when the order was
        /// given; Zod only boards parked, neutral vehicles). Moves the waypoint to the APC's centre when it is more than FollowSlack px
        /// off, so the goal always stays inside the APC's footprint (a robot reaching it is under the APC and boards); the changed
        /// waypoint re-plans the path next tick (isNew) and is cloned to the minions. No random draw.</summary>
        public const int FollowSlack = 10;
        internal bool FollowBoardTarget(ZObject t)
        {
            if (IsMinion || t == null || t.IsShuttle || !t.CanTakePassengers(this) || Waypoints.Count == 0) return false;   // DV118: a landed shuttle never moves (its squad walks to its back slots)
            var wp = Waypoints[0];
            if (Math.Abs(t.CenterX - wp.x) <= FollowSlack && Math.Abs(t.CenterY - wp.y) <= FollowSlack) return false;
            wp.x = t.CenterX; wp.y = t.CenterY; Waypoints[0] = wp;
            UpdatedWaypoints = true;
            return true;
        }

        // ---- DV15 repair station (brepair.cpp:487-512 stores the drivers; the passengers ride along)
        public string RepairPassengerType = "grunt";
        public readonly List<int> RepairPassengers = new List<int>();
        public int RepairPassengerGrenades;

        /// <summary>SetRepairUnit (on the station): keep the APC's passengers with its drivers while it is inside.</summary>
        internal void StoreRepairPassengers(ZObject unit)
        {
            RepairPassengers.Clear(); RepairPassengerGrenades = 0;
            if (unit == null || unit.Passengers.Count == 0) return;
            RepairPassengerType = unit.PassengerType; RepairPassengers.AddRange(unit.Passengers); RepairPassengerGrenades = unit.PassengerGrenades;
        }

        /// <summary>BuildingRepairUnit (on the station): the fresh APC gets its passengers back.</summary>
        internal void RestoreRepairPassengers(ZObject u)
        {
            if (u == null || RepairPassengers.Count == 0) return;
            u.PassengerType = RepairPassengerType; u.Passengers.Clear(); u.Passengers.AddRange(RepairPassengers); u.PassengerGrenades = RepairPassengerGrenades;
            RepairPassengers.Clear(); RepairPassengerGrenades = 0;
            u.ResetDamageInfo();
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>OpenZ deviation DV15 (apc_v1): APC = one driver who never leaves + one passenger squad that boards, fires from it
        /// and exits on order (ZApcCrew.cs). OFF = Zod's APC exactly. Static like the other deviations; ZGame sets it at every start
        /// (PlayerPrefs oz_apc_crew, default on; off in 'Zod exact' oz_zod_exact = 1).</summary>
        public static bool ApcCrewDeviation = true;

        /// <summary>DV15 boarding of an owned APC (RobotEnterObject, before Zod's neutral-only path): the leader and all its minions
        /// become the passenger squad. Entered event with flag = true (passengers: no capture, no owner change).</summary>
        bool BoardAsPassengers(ZObject r, ZObject v)
        {
            if (!(ApcCrewDeviation || (v != null && v.IsShuttle)) || r == null || v == null || r.KillAt >= 0 || r.IsMinion || !v.CanTakePassengers(r)) return false;
            if (v.IsShuttle) ShuttleBoardSchedule(r, v);                                    // DV118: when each figure reaches the ramp foot, the ramp's hold (Sim/ZWorld.ShuttleRamp.cs)
            v.PassengerType = r.Type;
            v.Passengers.Clear();
            v.Passengers.Add(r.Health);
            r.KillAt = 0;
            foreach (var m in r.Minions) { if (m == null || m.KillAt >= 0) continue; v.Passengers.Add(m.Health); m.KillAt = 0; }
            v.PassengerGrenades = r.Grenades;
            v.ResetDamageInfo();
            Emit(ZEventType.Entered, r, v, 0, 0, v.Passengers.Count, 0, true);   // flag = passengers boarded, value = how many
            return true;
        }

        /// <summary>DV15: a squad that entered a NEUTRAL APC (Zod put the leader and every minion into Drivers): the leader drives,
        /// the minions become the passengers. Called before ResetDamageInfo, so the firepower is Zod's (same type, x squad size).</summary>
        void SplitApcCrew(ZObject v)
        {
            if (!ApcCrewDeviation || v == null || !v.IsApc || v.Drivers.Count <= 1) return;
            v.PassengerType = v.DriverType;
            v.Passengers.Clear();
            for (int i = 1; i < v.Drivers.Count; i++) v.Passengers.Add(v.Drivers[i]);
            v.Drivers.RemoveRange(1, v.Drivers.Count - 1);
            v.PassengerGrenades = 0;
        }

        /// <summary>DV15 EXIT on an APC: only the passengers leave; refused (false) with none aboard. The APC stays owned and driven.</summary>
        bool EjectPassengers(Team team, ZObject v)
        {
            if (v == null || team == Team.Neutral || v.Owner != team || v.Destroyed || !v.CanEjectCrew()) return false;
            if (v.IsShuttle) return ShuttleExitOrder(v, team);                               // DV118: the ramp comes down first; the squad gets out when it is fully down (Sim/ZWorld.ShuttleRamp.cs)
            return PassengersOut(v, team) != null;
        }

        /// <summary>The passenger squad leaves `v` as a squad of `team` next to it (same spot and order as Zod's EJECT_VEHICLE: leader
        /// + minions at the APC's top-left, stored health per robot, the grenade pool back to the leader). Emits Created (robot,
        /// vehicle) per robot, which BoardingView plays as the exit, then Ejected (vehicle, leader). Returns the leader.</summary>
        ZObject PassengersOut(ZObject v, Team team)
        {
            if (v == null || v.Passengers.Count == 0) return null;
            if (team == Team.Neutral) { v.Passengers.Clear(); v.PassengerGrenades = 0; v.ResetDamageInfo(); return null; }
            var hp = v.Passengers.ToList();
            var leader = CreateRobotGroup(v.PassengerType, v.LocX, v.LocY, team, hp.Count);
            var squad = new List<ZObject> { leader }; squad.AddRange(leader.Minions);
            HeroCrewOut(v, squad, hp, true);                                                                // DV108: hero members come out as themselves, Brad first, with their own hit points (Sim/ZHeroSquad.cs)
            if (v.IsShuttle) ShuttlePlaceCrew(v, squad); else ExitWalkApc(v, squad);   /* DV132: the APC's getting-out times (Sim/ZObject.ExitWalk.cs) */                                                   // DV118: each one standing on its own back slot behind the ship (Sim/ZWorld.ShuttleRamp.cs)
            for (int i = 0; i < squad.Count; i++)
            {
                squad[i].Health = Math.Min(squad[i].MaxHealth, Math.Max(1, hp[i]));
                Emit(ZEventType.Created, squad[i], v);
            }
            leader.Grenades = v.PassengerGrenades;
            v.Passengers.Clear(); v.PassengerGrenades = 0;
            v.ResetDamageInfo();
            if (v.AttackObject != null && !v.WithinAttackRadius(v.AttackObject)) v.Disengage();         // the driver's rifle may not reach it
            Emit(ZEventType.Ejected, v, leader, 0, 0, hp.Count, 0, true);                                   // flag = passengers only (the APC stays owned)
            return leader;
        }

        /// <summary>DV15: the APC went neutral (its driver died): the passengers get out alive for the team that owned it.</summary>
        void ApcWentNeutral(ZObject v)
        {
            if (!ApcCrewDeviation || v == null || !v.IsApc || v.Passengers.Count == 0) return;
            PassengersOut(v, v.SnipedFrom);
        }
    }
}
