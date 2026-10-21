using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>One robot of a squad walking in or out of a shuttle when the ship was destroyed (DV126): saved (it was drawn outside,
    /// on the ramp or in the doorway: it stands on the ground behind the wreck as a normal unit) or lost (it was fully inside).</summary>
    public struct ZShuttleDoorwayRow
    {
        /// <summary>Counts up from 1 over the match (the view keeps the last one it handled).</summary>
        public int seq;
        /// <summary>The shuttle's id.</summary>
        public int shipId;
        /// <summary>The robot now: a saved boarder's NEW robot, the exiting robot itself (saved or lost).</summary>
        public int robotId;
        /// <summary>The robot the view's figure belongs to: a boarder's robot from before it boarded, else robotId.</summary>
        public int oldId;
        /// <summary>Its place in the ramp's file (ZObject.ShuttleFootIds) and how many walked in or out.</summary>
        public int index, count;
        /// <summary>Walking in (true) or out (false); saved (true) or lost with the ship (false).</summary>
        public bool boarding, saved;
        /// <summary>Saved: where it stands now (px, its middle); lost: the ship's middle.</summary>
        public int x, y;
        /// <summary>The moment (sim seconds) and how far it was then: seconds after it reached the ramp foot (boarding) or after it left
        /// its seat (exit).</summary>
        public double time, phase;
    }

    /// <summary>
    /// board a squad in ONE tick at the ramp foot and let a squad out in one tick onto its back slots (DV15's passenger rule); the view then
    /// walks each figure up or down the ramp at the moments the rules give (ZObject.ShuttleFootTimes). Before this rule a kill in those few
    /// seconds took the whole boarding squad (some still drawn walking to the ramp) and left every exiting robot alive (some still drawn on
    /// their seats). Now the rules agree with what is drawn:
    ///  * Walking IN: member k reaches the ramp foot at ShuttleFootTimes[k] and passes the doorway (into the bay) <see cref="ZWorld.ShuttleBoardDoorSec"/>
    ///    later (the view times its walk up the ramp to that). Destroyed before that moment, it SURVIVES: it is made again as a normal robot
    ///    (its type, its hit points, a hero as himself) standing on the ground on the back axis behind the wreck (back slots 1, 2 ..: clear of
    ///    the hull and the ramp, in the order they walked), with no order; together the saved make one squad (the first in the file leads;
    ///    a hero leads by his rank). Only the members past the doorway are lost with the ship. The squad's grenade pool goes with the saved
    ///    when the squad's leader is among them; a saved pilot-to-be (the leader of a squad that took an EMPTY ship) brings his rank back.
    ///  * Walking OUT: member k leaves its seat at ShuttleFootTimes[k] - ZWorld.ShuttleRampOutSec and reaches the doorway
    ///    <see cref="ZWorld.ShuttleExitDoorSec"/> later. Destroyed before that moment (still seated or walking down the aisle) it is lost with
    ///    the ship (its robot is taken off the map at once, one ZWorld.ShuttleLostCrew row for the view's body); in the doorway, on the ramp
    ///    or already on the ground it lives (it stands on its back slot: a normal unit).
    ///  * Everything else is unchanged: a kill in the air, before the squad got to the ramp foot or after it sat down, a kill with nobody on
    ///    the ramp: everyone aboard is lost, robots outside live. Deterministic: sim time and the rules' foot schedule only; one
    ///    <see cref="ZShuttleDoorwayRow"/> per robot of the file for the view (it walks a saved figure on to its robot, throws a lost one out
    ///    with the pieces).
    /// Switch: <see cref="ZWorld.ShuttleDoorwayDeviation"/> (on; off = DV118 as it was). Matters only while DV118 makes shuttles.
    /// </summary>
    public sealed partial class ZWorld
    {
        /// <summary>DV126 switch (on; off = everyone listed aboard is lost, every robot let out lives, as before).</summary>
        public static bool ShuttleDoorwayDeviation = true;
        /// <summary>From the ramp foot to the doorway (walking in) and from the seat to the doorway (walking out), seconds: the view times
        /// the walks to these moments (View/BoardingView.ShuttleDoorway.cs). Set to the walks' own pace on the kit's ramp (2.51 m from the
        /// 0.13 s + the ramp at 2.35 m/s 1.07 s + the doorway 0.3 s, 1.5 s (was 1.6); out = up from the bench 0.45 s + the aisle at 2.0 m/s,
        /// 0.65 - 1.35 s fitted to 0.9 s (was 1.0) within the 2.5 s down to the foot (was 3.2).</summary>
        public const double ShuttleBoardDoorSec = 1.5, ShuttleExitDoorSec = 0.9;
        /// <summary>The rows of the robots in a ramp's file when its ship was destroyed (at most 64; seq counts up).</summary>
        public readonly List<ZShuttleDoorwayRow> ShuttleDoorwayRows = new List<ZShuttleDoorwayRow>();
        public int ShuttleDoorwaySeq { get; private set; }
        /// <summary>Check counters (all shuttles): boarders saved, exiters saved, exiters lost (fully inside).</summary>
        public static int ShuttleDoorwayBoardSaved, ShuttleDoorwayExitSaved, ShuttleDoorwayExitLost;

        /// <summary>DV126: a boarder whose figure reached the ramp foot at footAt is past the doorway (inside the bay) at `now`.</summary>
        public static bool ShuttleBoarderInside(double now, double footAt) => now >= footAt + ShuttleBoardDoorSec - 1e-9;
        /// <summary>DV126: an exiter who steps off the ramp foot at footAt is still inside (seated or in the aisle) at `now`.</summary>
        public static bool ShuttleExiterInside(double now, double footAt) => now < footAt - ShuttleRampOutSec + ShuttleExitDoorSec - 1e-9;

        /// <summary>ZWorld.ShuttleDown, first (the ship was just destroyed, its lists untouched): the squad in the ramp's file now.</summary>
        internal void ShuttleDoorwayDown(ZObject o)
        {
            if (!ShuttleDoorwayDeviation || o == null || !o.IsShuttle || o.ShuttleAirborne) return;
            int n = o.ShuttleFootIds.Count;
            if (n == 0 || o.ShuttleFootTimes.Count < n) return;
            if (o.ShuttleLastRampUse == ZShuttleRampUse.Boarded) ShuttleDoorwayBoarders(o, n);
            else if (o.ShuttleLastRampUse == ZShuttleRampUse.Unloaded) ShuttleDoorwayExiters(o, n);
        }

        sealed class DoorwaySaved { public int k, oldId, hp; public string type; public ZHeroSeat seat; public bool pilot; }

        /// <summary>The squad walking in: every member not yet past the doorway is made again on the ground behind the wreck.</summary>
        void ShuttleDoorwayBoarders(ZObject o, int n)
        {
            // which list holds member k: a squad that took an EMPTY ship = its leader the pilot (Drivers[0]), the rest the passengers;
            // a squad boarding a crewed ship = all passengers (ZWorld.ShuttleTakeEmpty / BoardAsPassengers, both in squad order)
            bool pilotIn = o.Drivers.Count == 1 && n == o.Passengers.Count + 1;
            if (!pilotIn && n != o.Passengers.Count) return;                                               // the lists do not match the file: leave it as it was
            var saved = new List<DoorwaySaved>();
            for (int k = 0; k < n; k++)
            {
                if (ShuttleBoarderInside(Time, o.ShuttleFootTimes[k])) continue;
                bool pilot = pilotIn && k == 0; int idx = pilotIn ? k - 1 : k;
                saved.Add(new DoorwaySaved
                {
                    k = k, oldId = o.ShuttleFootIds[k], pilot = pilot,
                    hp = pilot ? o.Drivers[0] : o.Passengers[idx],
                    type = pilot ? o.DriverType : o.PassengerType,
                    seat = pilot ? (o.HeroDriverSeats != null && o.HeroDriverSeats.Count > 0 ? o.HeroDriverSeats[0] : null)
                                 : (o.HeroPassengerSeats != null && idx < o.HeroPassengerSeats.Count ? o.HeroPassengerSeats[idx] : null),
                });
            }
            if (saved.Count == 0) return;
            var team = o.Owner;
            if (team == Team.Neutral) return;
            // off the ship's lists (the passengers' places from the back, their hero seats with them)
            bool leaderSaved = saved[0].k == 0;
            int pool = leaderSaved ? o.PassengerGrenades + (saved[0].pilot ? o.ShuttlePilotPool : 0) : 0;
            for (int i = saved.Count - 1; i >= 0; i--)
            {
                var s = saved[i];
                if (s.pilot) { o.Drivers.Clear(); o.HeroDriverSeats = null; o.ShuttlePilotPool = 0; continue; }
                int idx = pilotIn ? s.k - 1 : s.k;
                o.Passengers.RemoveAt(idx);
                if (o.HeroPassengerSeats != null && idx < o.HeroPassengerSeats.Count) o.HeroPassengerSeats.RemoveAt(idx);
            }
            if (leaderSaved) o.PassengerGrenades = 0;
            // one squad: heroes first by their rank, then the rest in the file's order (the order DV108's HeroCrewOut gives a squad)
            var order = new List<DoorwaySaved>(saved);
            for (int i = 1; i < order.Count; i++)
                for (int j = i; j > 0 && ZHeroSeat.Rank(order[j].seat) < ZHeroSeat.Rank(order[j - 1].seat); j--) { var t = order[j]; order[j] = order[j - 1]; order[j - 1] = t; }
            var lead = CreateRobotGroup(string.IsNullOrEmpty(order[0].type) ? "grunt" : order[0].type, o.LocX, o.LocY, team, order.Count);
            var squad = new List<ZObject> { lead }; squad.AddRange(lead.Minions);
            var hp = new List<int>(); var seats = new List<ZHeroSeat>();
            foreach (var s in order) { hp.Add(s.hp); seats.Add(s.seat); }
            if (seats.Exists(x => x != null))
            {
                var keep = o.HeroPassengerSeats;
                o.HeroPassengerSeats = seats;
                HeroCrewOut(o, squad, hp, true);                                                           // DV108: as themselves (already in their order: nothing moves)
                o.HeroPassengerSeats = keep;
            }
            if (saved[0].pilot) CrewOut(o, squad);                                                         // DV12: the pilot-to-be's rank travels back with the squad
            // on the back axis behind the wreck, in the file's order (the one nearest the ramp on slot 1)
            var unplaced = new HashSet<ZObject>(squad);
            for (int i = 0; i < saved.Count; i++)
            {
                var s = saved[i]; int at = order.IndexOf(s); var m = squad[at];
                var (sx, sy) = o.ShuttleBackSpot(i + 1, true, unplaced, out _);
                m.SetCords(sx - (m.WidthPix >> 1), sy - (m.HeightPix >> 1)); m.XOver = m.YOver = 0;
                m.Waypoints.Clear(); m.StopMove();
                m.Health = Math.Min(m.MaxHealth, Math.Max(1, s.hp));
                unplaced.Remove(m);
                ShuttleDoorwayRow(o, m.Id, s.oldId, s.k, n, true, true, m.CenterX, m.CenterY, Time - o.ShuttleFootTimes[s.k]);
                ShuttleDoorwayBoardSaved++;
            }
            lead.Grenades = ZHeroGrenades.Carried(lead) + pool;                                            // KH: a hero's own + the squad's pool when its leader is saved
            for (int i = 0; i < squad.Count; i++) Emit(ZEventType.Created, squad[i]);                      // a plain new unit (no vehicle: the view walks the figure on to it)
        }

        /// <summary>The squad walking out: every robot still inside (seated or in the aisle) is lost with the ship.</summary>
        void ShuttleDoorwayExiters(ZObject o, int n)
        {
            var lost = new List<(int k, ZObject r)>();
            for (int k = 0; k < n; k++)
            {
                var r = Get(o.ShuttleFootIds[k]);
                if (r == null || r.Destroyed || r.KillAt >= 0 || !r.IsRobot) continue;
                double phase = Time - (o.ShuttleFootTimes[k] - ShuttleRampOutSec);
                if (!ShuttleExiterInside(Time, o.ShuttleFootTimes[k]))
                {
                    if (Time < o.ShuttleFootTimes[k] + 0.5) { ShuttleDoorwayRow(o, r.Id, r.Id, k, n, false, true, r.CenterX, r.CenterY, phase); ShuttleDoorwayExitSaved++; }   // still on the ramp (or just off it): its figure walks on to it
                    continue;
                }
                lost.Add((k, r));
            }
            for (int i = 0; i < lost.Count; i++)
            {
                var (k, r) = lost[i];
                var kind = r.Hero;
                string member = kind?.Members != null && r.HeroMember >= 0 && r.HeroMember < kind.Members.Length ? kind.Members[r.HeroMember] : "";
                ShuttleLostCrew.Add(new ZShuttleLost
                {
                    seq = ++ShuttleLostSeq, shipId = o.Id, team = r.Owner, type = string.IsNullOrEmpty(r.Type) ? "grunt" : r.Type,
                    hero = kind?.Id ?? "", member = member, index = i, count = lost.Count, hp = r.Health, time = Time,
                    airborne = false, heightM = 0, x = o.CenterX, y = o.CenterY, vx = o.Dx, vy = o.Dy, headingDeg = o.ShuttleHeadingDeg,
                });
                if (ShuttleLostCrew.Count > 32) ShuttleLostCrew.RemoveAt(0);
                ShuttleDoorwayRow(o, r.Id, r.Id, k, n, false, false, o.CenterX, o.CenterY, Time - (o.ShuttleFootTimes[k] - ShuttleRampOutSec));
                r.Waypoints.Clear(); r.StopMove();
                r.KillAt = Time;                                                                           // off the map with the ship (as a boarder is): no death of its own on the ground
                r.HasProcessedDeath = true;                                                                // still on the map until the next tick: a later blast or shot of this tick gives it no second death (no Death event, no kill credit, no second body)
                ShuttlePassengersLost++; ShuttleDoorwayExitLost++;
            }
        }

        void ShuttleDoorwayRow(ZObject o, int robotId, int oldId, int k, int n, bool boarding, bool saved, int x, int y, double phase)
        {
            ShuttleDoorwayRows.Add(new ZShuttleDoorwayRow
            {
                seq = ++ShuttleDoorwaySeq, shipId = o.Id, robotId = robotId, oldId = oldId, index = k, count = n, boarding = boarding, saved = saved,
                x = x, y = y, time = Time, phase = phase,
            });
            if (ShuttleDoorwayRows.Count > 64) ShuttleDoorwayRows.RemoveAt(0);
        }
    }
}
