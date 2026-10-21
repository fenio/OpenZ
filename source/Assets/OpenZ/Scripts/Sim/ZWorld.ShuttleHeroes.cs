using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// DV118, the heroes and the shuttle they took EMPTY (deterministic: no random draw, no clock; a match without a hero pilot plays
    /// exactly as before). The pilot of a ship that was empty is the leader of the squad that took it (Sim/ZWorld.ShuttleEmpty.cs), and that
    /// pilot never gets out. A HERO is the exception: heroes never stay stuck in the cockpit.
    ///  * The rule: when the pilot is a hero (his seat beside Drivers is a hero's, DV108: the ALAN &amp; BRAD squad took the empty ship),
    ///    EXIT lets EVERYONE out: the passengers as always, and the hero pilot with them. The ship is then EMPTY again exactly as before it
    ///    was boarded: the null team, no pilot, nobody aboard, no crew rank, no order kept (no take-off waiting, no repair trip, no EXIT
    ///    pending), dark; it never flies, nobody walks into it by himself, and only an order puts a squad in again.
    ///  * Who comes out with whom: passengers who boarded WITH the hero pilot (his own squad mates: his squad took the empty ship, every
    ///    passenger seat a hero of his kind, <see cref="ZObject.ShuttlePilotSquadIn"/>) come out with him as ONE squad, the kind's leader
    ///    first (Brad leads, as they went in); passengers of another squad (a squad that boarded a lone hero pilot's ship later, his own
    ///    mate too when DV98 had split them apart) come out as their own squad, and the hero pilot as his own: everyone as he went in, every
    ///    grenade once. They stand in one line on the back slots, the hero squad first (on the farthest slots).
    ///  * EXIT is offered with a hero pilot alone (no passengers) too: on the ground, and in the air only where it may land (the rule of
    ///    EXIT in the air).
    ///  * Everything else as before: any other pilot never gets out (a lone grunt pilot keeps the ship; heroes who boarded HIS ship are its
    ///    passengers and get out on EXIT as always); level 16's parked ship keeps its own pilot. Shot down or destroyed: everyone aboard is
    ///    lost, the hero pilot too (listed for the view's bodies, <see cref="ZWorld.ShuttleLostCrew"/>).
    ///  * Hit points, rank and grenades: each hero comes out with the hit points his place kept, the squad's rank (DV12) goes back to the
    ///    squad, Alan's own grenades come back with him (KH) and the squad's pool goes back to its leader; a lone hero pilot's pool is kept
    ///    for him (<see cref="ZObject.ShuttlePilotPool"/>).
    /// </summary>
    public partial class ZObject
    {
        /// <summary>DV118: this shuttle's pilot is a hero (he came from the hero squad that took the empty ship): he gets out on EXIT.</summary>
        public bool ShuttleHeroPilot => IsShuttle && Drivers.Count > 0 && HeroDriverSeats != null && HeroDriverSeats.Count > 0 && HeroDriverSeats[0] != null;

        /// <summary>DV118: someone aboard gets out on EXIT: the passengers, or a hero pilot.</summary>
        public bool ShuttleHasLeavers => Passengers.Count > 0 || ShuttleHeroPilot;

        /// <summary>DV118: the squad's grenade pool a lone hero pilot took aboard (nobody else aboard carries it); back to him when he gets out.</summary>
        public int ShuttlePilotPool { get; internal set; }

        /// <summary>DV118: the passengers aboard boarded WITH the hero pilot (his own squad mates, when his squad took the empty ship), so
        /// they come out with him as one squad. False for a squad that boarded later (it comes out as its own, with its own pool: a lone
        /// Alan's grenades are then counted once, from his seat and the passengers' pool alike).</summary>
        public bool ShuttlePilotSquadIn { get; internal set; }

        /// <summary>Check counters (all shuttles): EXITs that let a hero pilot out, ships left empty by them, hero pilots lost with their ship.</summary>
        public static int ShuttleHeroExits, ShuttleHeroesEmptied, ShuttleHeroPilotsLost;

        /// <summary>The ship is empty again: every order it held is forgotten (a take-off waiting for the ramp, a repair trip and its flight
        /// back, an EXIT given in the air).</summary>
        internal void ShuttleForgetOrders()
        {
            shuttleNewGoal = false; ShuttleTakeOffHeld = false; ShuttleExitOnLanding = false; ShuttleExitPending = false;
            ShuttleRepairLeave(); shuttleRepairBack = false; ShuttleViasClear();                       // DV125: its route too (Sim/ZWorld.ShuttleQueue.cs)
            if (Waypoints.Count > 0) Waypoints.Clear();
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>ZWorld.ShuttleTakeEmpty hook (the seats are set): a lone hero pilot keeps the squad's grenade pool (the part of his
        /// grenades that is not his own) for when he gets out; every other boarding keeps none here. A hero pilot's minions who came in
        /// with him are marked as his own squad aboard.</summary>
        internal void ShuttleHeroPilotIn(ZObject v, ZObject r)
        {
            if (v == null) return;
            v.ShuttlePilotPool = r != null && v.ShuttleHeroPilot && v.Passengers.Count == 0 ? Math.Max(0, r.Grenades - ZHeroGrenades.Carried(r)) : 0;
            v.ShuttlePilotSquadIn = v.ShuttleHeroPilot && v.Passengers.Count > 0;
        }

        /// <summary>The passengers aboard are the hero pilot's own squad mates: they boarded with him (his squad took the empty ship) and
        /// every passenger seat is a hero of his kind. A squad that boarded his ship later (DV98 can split ALAN &amp; BRAD apart) is not:
        /// it carries its own pool, which for a lone Alan holds his own grenades as his seat does.</summary>
        static bool ShuttlePilotMates(ZObject v)
        {
            int n = v.Passengers.Count;
            if (n == 0 || !v.ShuttlePilotSquadIn || v.HeroPassengerSeats == null) return false;
            var kind = v.HeroDriverSeats[0].Kind;
            for (int i = 0; i < n; i++)
                if (i >= v.HeroPassengerSeats.Count || v.HeroPassengerSeats[i] == null || v.HeroPassengerSeats[i].Kind != kind) return false;
            return true;
        }

        /// <summary>ZWorld.ShuttleExitNow hook (the ramp is fully down with an EXIT pending): with a hero pilot everyone gets out and the
        /// ship is empty again. False = not a hero pilot (the passengers get out as before, the pilot stays).</summary>
        internal bool ShuttleHeroesOut(ZObject v)
        {
            if (v == null || v.Destroyed || !v.ShuttleHeroPilot || v.Owner == Team.Neutral) return false;
            var team = v.Owner;
            bool mates = ShuttlePilotMates(v);
            // the pilot's squad: the hero pilot, and his own squad mates when they are the passengers
            var hp = new List<int> { v.Drivers[0] };
            var seats = new List<ZHeroSeat> { v.HeroDriverSeats[0] };
            int pool = v.ShuttlePilotPool;
            if (mates) { hp.AddRange(v.Passengers); seats.AddRange(ZHeroSeat.Fit(v.HeroPassengerSeats, v.Passengers.Count)); pool += v.PassengerGrenades; }
            var lead = CreateRobotGroup(v.DriverType, v.LocX, v.LocY, team, hp.Count);
            var squad = new List<ZObject> { lead }; squad.AddRange(lead.Minions);
            v.HeroDriverSeats = seats;
            HeroCrewOut(v, squad, hp, false);                                                       // DV108: as themselves, the kind's leader first, with the hit points their places kept
            CrewOut(v, squad);                                                                      // DV12: the squad's rank back to it, the ship's cleared
            // the other squad: passengers who boarded a lone hero pilot's ship
            List<ZObject> other = null; List<int> hpO = null;
            if (!mates && v.Passengers.Count > 0)
            {
                hpO = new List<int>(v.Passengers);
                var lo = CreateRobotGroup(v.PassengerType, v.LocX, v.LocY, team, hpO.Count);
                other = new List<ZObject> { lo }; other.AddRange(lo.Minions);
                HeroCrewOut(v, other, hpO, true);
            }
            var all = new List<ZObject>(squad); if (other != null) all.AddRange(other);
            ShuttlePlaceCrew(v, all);                                                               // one line on the back slots, the hero squad first (the farthest)
            for (int i = 0; i < squad.Count; i++)
            {
                squad[i].Health = Math.Min(squad[i].MaxHealth, Math.Max(1, hp[i]));
                Emit(ZEventType.Created, squad[i], v);
            }
            lead.Grenades = ZHeroGrenades.Carried(lead) + pool;                                     // KH: his own (Alan's) + the squad's pool
            if (other != null)
            {
                for (int i = 0; i < other.Count; i++)
                {
                    other[i].Health = Math.Min(other[i].MaxHealth, Math.Max(1, hpO[i]));
                    Emit(ZEventType.Created, other[i], v);
                }
                other[0].Grenades = v.PassengerGrenades;
            }
            ZObject.ShuttleHeroExits++;
            ShuttleEmptyAgain(v);
            Emit(ZEventType.Ejected, v, lead, 0, 0, all.Count, 0, false);                            // flag false: the whole crew left (the ship turns neutral)
            ResetObjectTeam(v, Team.Neutral);
            v.ShuttleLastRampUse = ZShuttleRampUse.Unloaded;
            ZObject.ShuttleHeroesEmptied++;
            return true;
        }

        /// <summary>The ship as an empty one is made (ZWorld.CreatePlayableShuttle with the null team): no pilot, nobody aboard, no seats,
        /// no grenades, no rank, the usual crew types, no order kept. Its team is set by the caller.</summary>
        void ShuttleEmptyAgain(ZObject v)
        {
            v.Drivers.Clear(); v.Passengers.Clear(); v.PassengerGrenades = 0; v.ShuttlePilotPool = 0; v.ShuttlePilotSquadIn = false;
            v.HeroDriverSeats = null; v.HeroPassengerSeats = null;
            v.DriverType = "grunt"; v.PassengerType = "grunt";
            v.ClearCrew();
            v.ShuttleForgetOrders();
            v.ResetDamageInfo();
        }

        /// <summary>ZWorld.ShuttleDown hook (after the passengers' rows): a hero pilot lost with the ship gets his row too, after them
        /// (the view throws his body out among the pieces, as it does the passengers').</summary>
        internal void ShuttleHeroPilotLost(ZObject o)
        {
            if (o == null || !o.ShuttleHeroPilot) return;
            var seat = o.HeroDriverSeats[0]; var kind = seat.Kind;
            string member = kind != null && kind.Members != null && seat.Member >= 0 && seat.Member < kind.Members.Length ? kind.Members[seat.Member] : "";
            int k = o.Passengers.Count;
            ShuttleLostCrew.Add(new ZShuttleLost
            {
                seq = ++ShuttleLostSeq, shipId = o.Id, team = o.Owner, type = string.IsNullOrEmpty(o.DriverType) ? "grunt" : o.DriverType,
                hero = kind?.Id ?? "", member = member, index = k, count = k + 1, hp = o.Drivers[0], time = Time,
                airborne = o.ShuttleAirborne, heightM = o.ShuttleHeightM, x = o.CenterX, y = o.CenterY, vx = o.Dx, vy = o.Dy, headingDeg = o.ShuttleHeadingDeg,
            });
            if (ShuttleLostCrew.Count > 32) ShuttleLostCrew.RemoveAt(0);
            ZObject.ShuttleHeroPilotsLost++;
        }
    }
}
