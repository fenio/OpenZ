using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// Zod starts every owned map vehicle with ONE grunt driver inside (zvehicle.cpp:155-168 SetInitialDrivers); DV32 parks L17's start
    /// vehicles empty and neutral, which removes those drivers. With DV63 the player's fort sends out one grunt per parked start vehicle (still empty)
    /// of its side, one every <see cref="ZL17Balance.CrewGapSeconds"/> s through its own door, as if it had just built it (Created +
    /// UnitBuilt like BuildingCreateUnit, so the view opens and closes the door, plays the roll-out sound, Rachel's "Robot manufactured"
    /// and the HUD toast), and each walks straight into its own vehicle (an Enter order: boarding animation, capture sound, the vehicle
    /// turns red). Deterministic (fixed times, vehicles nearest the fort first, ties by id, no random draw). Needs ParkedStart (DV32);
    /// the fort's own production timer is untouched; a fort whose production was stopped sends none.
    /// Switch: ZGame sets it at every start: on only on EASY in human vs CPU games, for the player's side (PlayerPrefs oz_l17_crews,
    /// default on, off in 'Zod exact'); Medium, Hard, bot matches and the headless tests keep the live behaviour (static default off).
    /// </summary>
    public static class ZL17Balance
    {
        /// <summary>Robot type of the start crew (Zod's start driver type).</summary>
        public static string CrewType = "grunt";
        /// <summary>0 = <see cref="CrewSquads"/> full squads; 1 = one crew per parked start vehicle of the side (the drivers DV32
        /// removed), each <see cref="CrewSquadSize"/> robots (0 = the type's Zod group size).</summary>
        public static int CrewMode = 1;
        /// <summary>Mode 0: full squads of <see cref="CrewType"/>.</summary>
        public static int CrewSquads = 1;
        /// <summary>Mode 1: robots per crew (1 = a lone driver per vehicle, as Zod's start drivers).</summary>
        public static int CrewSquadSize = 1;
        /// <summary>Mode 2: the fort tops its robots up to this many, in squads of the type's Zod group size.</summary>
        public static int CrewTopUp = 9;
        /// <summary>Game time of the first crew out of the door (s), and the gap between two crews (s).</summary>
        public static double CrewFirstSeconds = 0.5, CrewGapSeconds = 2.5;
        /// <summary>Each crew gets an Enter order on its own parked vehicle (nearest the fort first).</summary>
        public static bool CrewBoards = true;
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV63 switch (off = DV32 alone: no start crews). ZGame: Easy + human vs CPU only.</summary>
        public static bool L17CrewsDeviation = false;
        /// <summary>DV63: the only side that gets start crews (ZGame: the human player, red).</summary>
        public static Team L17CrewsTeam = Team.Red;
        /// <summary>DV63 statistics: start-crew robots each team's fort sent out so far (index = (int)Team).</summary>
        public readonly int[] L17CrewsSent = new int[3];
        /// <summary>DV63 statistics: start crews that got an Enter order on a parked vehicle (index = (int)Team).</summary>
        public readonly int[] L17CrewsBoardOrders = new int[3];

        readonly List<(ZObject fort, List<int> squads, List<ZObject> vehicles)> l17Crews = new List<(ZObject, List<int>, List<ZObject>)>();
        double l17CrewNext = -1;

        /// <summary>ZWorld.Create hook, after the map objects: the crew side's fort with a door, its parked vehicles and crew sizes.</summary>
        void InitL17Crews()
        {
            l17Crews.Clear(); l17CrewNext = -1;
            var u = S.Unit(ZL17Balance.CrewType);
            if (!ParkedStart || !L17CrewsDeviation || u == null || L17CrewsTeam == Team.Neutral) return;
            int group = System.Math.Max(1, ZL17Balance.CrewMode == 1 && ZL17Balance.CrewSquadSize > 0 ? ZL17Balance.CrewSquadSize : u.group_amount);
            var team = L17CrewsTeam;
            ZObject fort = null; int robots = 0;
            var parked = new List<ZObject>();
            foreach (var o in Objects)
            {
                if (o.Kind == ZKind.Building && o.IsFort && o.Owner == team && !o.Destroyed && o.GetCreationPoint(out _, out _) && (fort == null || o.Type == "fort_front")) fort = o;
                if (o.Kind == ZKind.Vehicle && o.Owner == Team.Neutral && !o.Destroyed && o.Source != null && ParseOwner(o.Source.owner) == team) parked.Add(o);
                if (o.Kind == ZKind.Robot && o.Owner == team && !o.Destroyed) robots++;
            }
            if (fort == null) return;
            long D(ZObject v) { long dx = v.CenterX - fort.CenterX, dy = v.CenterY - fort.CenterY; return dx * dx + dy * dy; }
            parked.Sort((a, b) => { int c = D(a).CompareTo(D(b)); return c != 0 ? c : a.Id.CompareTo(b.Id); });
            var squads = new List<int>();
            if (ZL17Balance.CrewMode == 1) for (int left = parked.Count * group; left > 0; left -= group) squads.Add(System.Math.Min(group, left));
            else if (ZL17Balance.CrewMode == 2) for (int left = ZL17Balance.CrewTopUp - robots; left > 0; left -= u.group_amount) squads.Add(System.Math.Min(u.group_amount, left));
            else for (int i = 0; i < ZL17Balance.CrewSquads; i++) squads.Add(u.group_amount);
            if (squads.Count > 0) { l17Crews.Add((fort, squads, parked)); l17CrewNext = ZL17Balance.CrewFirstSeconds; }
        }

        /// <summary>ZWorld.Tick hook: at the scheduled times the fort sends its next crew out of its door, into its own vehicle.</summary>
        void L17CrewsTick()
        {
            if (l17CrewNext < 0 || Time + 1e-9 < l17CrewNext) return;
            bool more = false;
            foreach (var (f, squads, vehicles) in l17Crews)
            {
                if (squads.Count == 0) continue;
                int n = squads[0]; squads.RemoveAt(0);
                if (f.Destroyed || f.Owner == Team.Neutral || !f.Building) { squads.Clear(); continue; }   // production stopped (parity test fields): none
                ZObject v = null;
                while (ZL17Balance.CrewBoards && vehicles.Count > 0 && v == null)
                {
                    var c = vehicles[0]; vehicles.RemoveAt(0);
                    if (!c.Destroyed && c.Owner == Team.Neutral && c.Drivers.Count == 0) v = c;         // already taken / lost: the next one
                }
                if (ZL17Balance.CrewBoards && v == null) { squads.Clear(); continue; }                   // every start vehicle already has a crew: no more
                var crew = CrewOut(f, ZL17Balance.CrewType, n);
                if (crew != null)
                {
                    L17CrewsSent[(int)f.Owner] += n;
                    if (v != null && f.GetCreationMovePoint(out int ex, out int ey) &&
                        SendWaypoints(f.Owner, crew, new[] { new ZWaypoint { mode = WpMode.ForceMove, refId = -1, x = ex, y = ey },
                                                             new ZWaypoint { mode = WpMode.Enter, refId = v.Id, x = v.CenterX, y = v.CenterY } }))
                        L17CrewsBoardOrders[(int)f.Owner]++;
                }
                more |= squads.Count > 0;
            }
            l17CrewNext = more ? l17CrewNext + ZL17Balance.CrewGapSeconds : -1;
        }

        /// <summary>BuildingCreateUnit's robot branch (ZWorld.Rules.cs) with a squad size: out of the door, to the exit point, events.</summary>
        ZObject CrewOut(ZObject b, string type, int amount)
        {
            if (!b.GetCreationPoint(out int cx, out int cy)) return null;
            var u = CreateRobotGroup(type, cx, cy, b.Owner, amount);
            int w = u.WidthPix, h = u.HeightPix;
            u.SetCords(cx - (w >> 1), cy - (h >> 1)); foreach (var m in u.Minions) m.SetCords(cx - (w >> 1), cy - (h >> 1));
            if (b.GetCreationMovePoint(out int ex, out int ey))
                u.Waypoints.Add(new ZWaypoint { mode = WpMode.ForceMove, refId = -1, x = ex, y = ey });
            u.CloneMinionWayPoints();
            Emit(ZEventType.Created, u, b); foreach (var m in u.Minions) Emit(ZEventType.Created, m, b);
            Emit(ZEventType.UnitBuilt, u, b, 0, 0, (float)(Time - b.BInit));
            return u;
        }
    }
}
