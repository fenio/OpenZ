using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted check of the enemy strike groups (rule DV107, Sim/ZBot.StrikeGroup.cs). Headless simulation, whole matches with the
    /// game's switches on the three levels of the game and on two made-up maps (the rule knows no level: no pass line below asks
    /// which map a match ran on); blue is the computer.
    ///   "player holds": red is a stand-in for a player who never attacks: he owns the four territories nearest his fort, a tank and
    ///     a squad hold each flag, four tanks and four squads hold his gate, and he keeps ONE LARGE GATHERING (eight units on hold
    ///     at a place of his own, the meeting point of everything his buildings turn out). He keeps the gate guard and the gathering
    ///     at strength: every 30 s a new unit stands on every post whose unit is gone (without that the computer walks into his fort
    ///     before 8:00 on the Jungle and the Car Park, and nothing could be counted).
    ///   "computer match": red is the game's own second computer.
    /// Everything is read from the computer's own record of what it did (ZBot.StrikeEvents) and from the objects themselves:
    /// when a window opened, who joined, where the members stood when the group left, how long each stood at the fort, when
    /// each reached the target area, what every former member did after a release.
    /// "The general rule": the same matches on a small made-up map (one factory beside the computer's fort, a second one farther
    /// off) and on a large one (five factories of the computer), and on every level with all but one of the computer's factories
    /// taken from it until its first window opens: which buildings feed a group there, by the check's own count.
    /// Menu: OpenZ/V1.1/Count Enemy Strike Groups (f39) = the short run (2 matches per level and row, 16 minutes each); the long
    /// run (10 matches, 30 minutes) is the kit's headless runner.
    /// </summary>
    public static class OzV11StrikeCount
    {
        public enum Sides { PlayerHolds, ComputerMatch }
        /// <summary>Something done to the match from outside, once, to see a rule that the stand-in never triggers.</summary>
        /// <summary>TakeTerritory: the player takes a territory that holds a FEEDING building (the window ends); TakeOtherTerritory: one
        /// that holds none (the window goes on); CrocTakesMember: a crocodile takes a robot of the forming group (the window goes on).</summary>
        public enum Disturb { None, TakeTerritory, DestroyMember, DestroyLeader, ArmyMoves, ArmyGone, SwitchOff, CannotMove, OneFactoryLeft, TakeOtherTerritory, CrocTakesMember }

        public static readonly string[] Levels = { "L02", "L16", "L17" };
        /// <summary>The first seed of a run (the matches of a row are the seeds FirstSeed, FirstSeed + 1, ...).</summary>
        public static int FirstSeed = 1;
        /// <summary>A traced match also writes every member's state every 5 s of a march.</summary>
        public static bool TraceMarch;
        /// <summary>What-if rows: this many feeding factories in the next match (0 = the rule's own number for the map).</summary>
        public static int FactoriesForCheck;
        /// <summary>What-if row: the next match gives a window up only for a lost territory of a feeding factory.</summary>
        /// <summary>The before rows of round 2: the first round's rules (any territory ends a window, a crocodile's robot ends it, no
        /// common route on the march: ZWorld.StrikeRoundOneForCheck).</summary>
        public static bool RoundOne;
        /// <summary>The common route: a marching member (not in a fight) more than this from the group's route counts as off it.</summary>
        public const int CorridorPx = 160;
        public const double OffRouteLimitSec = 10.0;
        public static string LevelName(string lv) => lv == "L02" ? "Desert" : lv == "L16" ? "Jungle" : lv == "L17" ? "Car Park" : lv;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string F(double v, string f = "0") => v.ToString(f, Inv);
        static string Clock(double t) => t < 0 ? "-" : ((int)(t / 60)).ToString(Inv) + ":" + ((int)(t % 60)).ToString("00", Inv);
        static double D(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

        // ------------------------------------------------------------------ what one match gave
        public sealed class Member
        {
            public int id; public string type; public double joined, left = -1, freedAt = -1, arrived = -1, busyAfter = -1; public bool settled, departed, lost, standing, onPlace, foughtOnWay;
            public int placeX, placeY; public double offPlace = -1; public bool freed, freedIdleNoTarget;
            /// <summary>The id it joined with (a squad goes on under its next robot: id follows it); times it was sent to its place
            /// again while the group formed; its place is the spot it stopped on / another place; why it was released alone.</summary>
            public int firstId, resends; public bool adopted, moved, leaderFell; public string leftWhy;
            /// <summary>While its group formed: the longest time it stayed (within 8 px) on a spot nobody may stand on, counted
            /// from the moment it stood there with no order: the corridor along the gate line, an exit lane, nearer the gate wall
            /// than the places. (A member that walks through, or waits in its building's door with an order, is not standing.)</summary>
            public double worstBad, badSince = -1; public int badX, badY;
        }
        /// <summary>No member may stay on such a spot longer than this. The rule looks once a second and sends a member back twice
        /// before it gives it another place or releases it: three looks, then it walks off (the check counts until it is 8 px away).</summary>
        public const double BadStandLimit = 6.0;
        public sealed class Group
        {
            public int n; public double opened, ended = -1, formed = -1; public int feeders; public string cycle, how, end;
            public bool gaveUp; public readonly List<Member> members = new List<Member>();
            public int tx, ty, atTarget, biggest; public double minApart = -1, worstOffPlace; public int inLane, standing, onPlace, offPlace; public bool atCycleEnd;
            /// <summary>How the march went: 0 = three members in the target area within 10 s; 1 = fewer than three could arrive (the others were
            /// destroyed on the way); 2 = three arrived, but not within 10 s, and one of them had a fight on the way; 3 = neither (a straggler).</summary>
            public int marchKind = -1;
            public double firstArrival = -1, thirdAfterFirst = -1, lastAfterFirst = -1; public int arrivedIn10, arrivedAll, lostOnMarch, marchers; public double marchSeconds = -1;
            public bool retargeted;
            public double worstOffRoute, farthestOffRoute; public bool routeSeen;                     // round 2: the longest time a marching member (not fighting) was off the common route after it had joined it, and the farthest
            /// <summary>Members that were not on the route when the march began (still on their way from their building, or a place to
            /// the side of the route): how many, the farthest off then, the longest time until they were on it, how many reached the
            /// target area without ever joining it (a road of their own).</summary>
            public int joiners, ownRoad; public double farthestJoiner, worstJoin;
            /// <summary>Every STANDING member when the group left: in the gate's corridor, nearer the gate wall than the places, two
            /// with their bodies in each other; the least distance between any two; standing, but not on its place at that moment.
            /// The most times one member was sent to its place again. Members on a spot they stopped on / on another place.</summary>
            public int inCorridor, tooNear, overlap, standingAll, standingOff, maxResends, adopted, movedPlace; public double minApartAll = -1;
            /// <summary>The feeding buildings by the check's own count: the factories the computer owned when the window opened, how
            /// many buildings should feed, how many factories fed, and whether they were the fort and its nearest factories.</summary>
            public int factoriesOwned, feedersWanted, feederFactories; public bool feedersRight; public string feedersNote;
        }
        public sealed class Result
        {
            public string level; public int seed; public Sides sides; public bool on; public ZBotDifficulty difficulty; public Disturb disturb;
            public double end; public bool over; public Team winner; public readonly List<Group> groups = new List<Group>();
            public int blueLost, redLost, redGroups; public string error; public ulong fingerprint; public int placesFound; public double placesMinApart; public int placesInLane;
            public double disturbAt = -1; public int membersHeldAtEnd, windowsAfterDisturb; public Member disturbMember; public int tiles;
            public int eaten; public string disturbNote;
            public readonly List<string> trace = new List<string>();
        }

        // ------------------------------------------------------------------ the stand-in player
        /// <summary>A free spot for a unit of `size` px near (cx, cy): ground or road under its whole box, 8 px clear of every object.</summary>
        static bool Spot(ZWorld w, int cx, int cy, int size, out int x, out int y)
        {
            for (int r = 0; r <= 10; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                        x = cx + dx * 16; y = cy + dy * 16; int x0 = x - size / 2, y0 = y - size / 2; bool ok = true;
                        for (int ty = (y0 - 8) >> 4; ty <= (y0 + size + 7) >> 4 && ok; ty++)
                            for (int tx = (x0 - 8) >> 4; tx <= (x0 + size + 7) >> 4 && ok; tx++)
                            {
                                if (tx < 1 || ty < 1 || tx >= w.Map.width - 1 || ty >= w.Map.height - 1) { ok = false; break; }
                                var c = w.Map.ClassAt(tx, ty); if ((c != ZClass.Ground && c != ZClass.Road) || !w.Path.TilePassable(tx, ty, false)) ok = false;
                            }
                        if (!ok) continue;
                        foreach (var o in w.Objects)
                        {
                            if (o.Destroyed || (o.Kind == ZKind.MapItem && o.Type == "flag")) continue;
                            if (x0 - 12 < o.LocX + o.WidthPix && x0 + size + 12 > o.LocX && y0 - 12 < o.LocY + o.HeightPix && y0 + size + 12 > o.LocY) { ok = false; break; }
                        }
                        if (ok) return true;
                    }
            x = y = 0; return false;
        }

        /// <summary>One post of the stand-in: a unit of a type on hold at a spot; the unit there now.</summary>
        public sealed class Post { public string type; public int x, y; public ZObject unit; }
        public sealed class StandInState
        {
            public int gx, gy; public double next = 30; public bool keepGathering = true; public int replaced;
            public readonly List<Post> guard = new List<Post>(), gathering = new List<Post>();
        }

        static ZObject Hold(ZWorld w, string type, int cx, int cy, List<Post> posts = null)
        {
            if (!Spot(w, cx, cy, ZBuildLists.IsRobot(type) ? 16 : 32, out int x, out int y)) return null;
            var u = OzV11Kit.Place(w, type, x, y, Team.Red);
            w.SendHold(Team.Red, u);
            posts?.Add(new Post { type = type, x = x, y = y, unit = u });
            return u;
        }

        /// <summary>The stand-in (see the class notes): his territories, his gate guard, his large gathering.</summary>
        public static StandInState StandIn(ZWorld w)
        {
            var S = new StandInState();
            ZObject fort = w.Objects.First(o => o.IsFort && o.Owner == Team.Red), other = w.Objects.First(o => o.IsFort && o.Owner == Team.Blue);
            ZWorld.StrikeGate(fort, out int ex, out int ey, out int dx, out int dy, out _);
            // his four territories nearest his fort, each held by a tank and a squad
            foreach (var f in w.Flags.OrderBy(f => D(f.CenterX, f.CenterY, fort.CenterX, fort.CenterY)).ThenBy(f => f.Id).Take(4).ToList())
            {
                w.AwardZone(f, Team.Red);
                Hold(w, "psycho", f.CenterX - 24, f.CenterY + 24);
                Hold(w, "light", f.CenterX + 48, f.CenterY);
            }
            // his gate: four tanks and four squads
            Hold(w, "medium", ex - 64, ey + dy * 64, S.guard); Hold(w, "medium", ex + 64, ey + dy * 64, S.guard);
            Hold(w, "psycho", ex - 40, ey + dy * 112, S.guard); Hold(w, "psycho", ex + 40, ey + dy * 112, S.guard);
            Hold(w, "psycho", ex - 96, ey + dy * 16, S.guard); Hold(w, "psycho", ex + 96, ey + dy * 16, S.guard);
            Hold(w, "light", ex - 128, ey + dy * 96, S.guard); Hold(w, "light", ex + 128, ey + dy * 96, S.guard);
            // his large gathering: a third of the way from his fort to the other one, on open ground both robots and vehicles reach from his gate
            int gx = ex + (other.CenterX - ex) * 30 / 100, gy = ey + (other.CenterY - ey) * 30 / 100;
            for (int r = 0; r <= 16; r++)
            {
                bool found = false;
                for (int oy = -r; oy <= r && !found; oy++)
                    for (int ox = -r; ox <= r && !found; ox++)
                    {
                        if (Math.Max(Math.Abs(ox), Math.Abs(oy)) != r) continue;
                        int x = gx + ox * 32, y = gy + oy * 32;
                        if (x < 160 || y < 160 || x > w.Map.WidthPix - 160 || y > w.Map.HeightPix - 160) continue;
                        if (!w.Path.InSameRegion(ex, ey + dy * 32, x, y, true) || !w.Path.InSameRegion(ex, ey + dy * 32, x, y, false)) continue;
                        int free = 0;
                        for (int j = -4; j <= 4; j++) for (int i = -5; i <= 5; i++) { var c = w.Map.ClassAt((x >> 4) + i, (y >> 4) + j); if ((c == ZClass.Ground || c == ZClass.Road) && w.Path.TilePassable((x >> 4) + i, (y >> 4) + j, false)) free++; }
                        if (free < 90) continue;                                                     // 9 x 11 tiles, nearly all open
                        gx = x; gy = y; found = true;
                    }
                if (found) break;
            }
            S.gx = gx; S.gy = gy;
            string[] types = { "medium", "light", "psycho", "medium", "light", "psycho", "light", "psycho" };
            for (int i = 0; i < types.Length; i++) Hold(w, types[i], gx + (i % 4 - 2) * 48 + 24, gy + (i / 4) * 56 - 28, S.gathering);
            // everything his buildings turn out walks to the gathering
            foreach (var b in w.Objects) if (b.Owner == Team.Red && b.IsProducer) w.SetRallyPoints(Team.Red, b, new[] { ZWaypoint.Move(gx, gy + 72, true) });
            return S;
        }

        /// <summary>Every 30 s the stand-in puts a new unit on every post of his gate guard and of his gathering whose unit is gone
        /// (a player who keeps both at strength), when the post is free.</summary>
        public static void StandInKeep(ZWorld w, StandInState S)
        {
            if (w.Time < S.next) return;
            S.next += 30;
            foreach (var list in new[] { S.guard, S.gathering })
            {
                if (list == S.gathering && !S.keepGathering) continue;
                foreach (var p in list)
                {
                    if (p.unit != null && w.Get(p.unit.Id) == p.unit && !p.unit.Destroyed && p.unit.KillAt < 0 && p.unit.Owner == Team.Red) continue;
                    int size = ZBuildLists.IsRobot(p.type) ? 16 : 32; bool free = true;
                    foreach (var o in w.Objects)
                    {
                        if (o.Destroyed || o.KillAt >= 0 || (o.Kind == ZKind.MapItem && o.Type == "flag")) continue;
                        if (p.x - size / 2 - 8 < o.LocX + o.WidthPix && p.x + size / 2 + 8 > o.LocX && p.y - size / 2 - 8 < o.LocY + o.HeightPix && p.y + size / 2 + 8 > o.LocY) { free = false; break; }
                    }
                    if (!free || w.UnitLimitReached(Team.Red)) continue;
                    p.unit = OzV11Kit.Place(w, p.type, p.x, p.y, Team.Red);
                    w.SendHold(Team.Red, p.unit); S.replaced++;
                }
            }
        }

        // ------------------------------------------------------------------ made-up maps (the rule must work on a map nobody has seen)
        /// <summary>A map of plain ground with several zones: zones in tiles (a fort's zone first), objects with the kind of the map
        /// files, in tiles. Like OzV11Kit.Plain, which has one zone.</summary>
        public static string MadeUp(int w, int h, (int x, int y, int w, int h)[] zones, (string kind, int x, int y, string owner)[] objects)
        {
            var sb = new StringBuilder("{\"file\":\"v11_made_up\",\"planet\":\"desert\",\"width\":" + w + ",\"height\":" + h + ",\"classes\":[");
            sb.Append(string.Join(",", Enumerable.Repeat("0", w * h))).Append("],\"tiles\":[").Append(string.Join(",", Enumerable.Repeat("0", w * h))).Append("],\"zones\":[");
            for (int i = 0; i < zones.Length; i++) sb.Append(i > 0 ? "," : "").Append("{\"x\":" + zones[i].x + ",\"y\":" + zones[i].y + ",\"w\":" + zones[i].w + ",\"h\":" + zones[i].h + "}");
            sb.Append("],\"objects\":[");
            for (int i = 0; i < objects.Length; i++)
                sb.Append(i > 0 ? "," : "").Append("{\"x\":" + objects[i].x + ",\"y\":" + objects[i].y + ",\"owner\":\"" + objects[i].owner + "\",\"kind\":\"" + objects[i].kind + "\",\"ot\":0,\"oid\":0,\"blevel\":0,\"extra_links\":0,\"health_percent\":100}");
            return sb.Append("]}").ToString();
        }

        /// <summary>A small made-up map, 48 x 100 tiles (4,800): the computer's fort at the top with ONE factory beside it (in the
        /// fort's own territory, 256 px away) and a second factory in a territory of its own farther off (447 px); eight territories
        /// with a flag; the other fort at the bottom. The computer has two guns at its gate (a side without any unit is out).</summary>
        public static string SmallMap() => MadeUp(48, 100,
            new[] { (0, 0, 48, 24), (0, 76, 48, 24), (0, 24, 24, 13), (24, 24, 24, 13), (0, 37, 24, 13), (24, 37, 24, 13), (0, 50, 24, 13), (24, 50, 24, 13), (0, 63, 24, 13), (24, 63, 24, 13) },
            new[]
            {
                ("building:fort_front", 19, 3, "blue"), ("building:robot_factory", 6, 6, "blue"), ("cannon:gun", 14, 16, "blue"), ("cannon:gun", 32, 16, "blue"),
                ("building:fort_back", 19, 86, "red"), ("building:robot_factory", 38, 88, "red"),
                ("map_item:flag", 6, 30, "null"),
                ("map_item:flag", 44, 34, "blue"), ("building:vehicle_factory", 42, 26, "blue"),
                ("map_item:flag", 10, 43, "null"), ("map_item:flag", 38, 43, "null"),
                ("map_item:flag", 10, 56, "null"), ("building:robot_factory", 4, 52, "null"), ("map_item:flag", 38, 56, "null"),
                ("map_item:flag", 10, 69, "null"), ("map_item:flag", 38, 69, "null"), ("building:vehicle_factory", 40, 65, "null"),
            });

        /// <summary>A large made-up map, 120 x 140 tiles (16,800): the computer's fort at the top and FIVE factories of the
        /// computer, 225 and 291 px from the fort (in the fort's own territory), 575, 620 and 890 px (each in a territory of its
        /// own); eighteen territories with a flag; the other fort at the bottom with two factories.</summary>
        public static string LargeMap() => MadeUp(120, 140,
            new[]
            {
                (0, 0, 120, 22), (0, 118, 120, 22),
                (0, 22, 40, 20), (40, 22, 40, 20), (80, 22, 40, 20), (0, 42, 40, 20), (40, 42, 40, 20), (80, 42, 40, 20), (0, 62, 40, 20), (40, 62, 40, 20), (80, 62, 40, 20),
                (0, 82, 40, 20), (40, 82, 40, 20), (80, 82, 40, 20), (0, 102, 40, 16), (40, 102, 40, 16), (80, 102, 40, 16),
            },
            new[]
            {
                ("building:fort_front", 55, 4, "blue"), ("building:robot_factory", 44, 6, "blue"), ("building:vehicle_factory", 76, 10, "blue"), ("cannon:gun", 48, 17, "blue"), ("cannon:gun", 70, 17, "blue"),
                ("building:fort_back", 55, 125, "red"), ("building:robot_factory", 40, 128, "red"), ("building:vehicle_factory", 76, 128, "red"),
                ("map_item:flag", 20, 38, "blue"), ("building:robot_factory", 30, 30, "blue"),
                ("map_item:flag", 60, 39, "null"),
                ("map_item:flag", 110, 38, "blue"), ("building:vehicle_factory", 88, 32, "blue"),
                ("map_item:flag", 12, 52, "null"), ("map_item:flag", 60, 52, "null"),
                ("map_item:flag", 112, 56, "blue"), ("building:robot_factory", 100, 44, "blue"),
                ("map_item:flag", 12, 72, "null"), ("map_item:flag", 60, 72, "null"), ("map_item:flag", 108, 72, "null"),
                ("map_item:flag", 12, 92, "null"), ("building:robot_factory", 20, 86, "null"), ("map_item:flag", 60, 92, "null"), ("map_item:flag", 108, 92, "null"), ("building:vehicle_factory", 96, 86, "null"),
                ("map_item:flag", 16, 110, "null"), ("map_item:flag", 60, 106, "null"), ("map_item:flag", 104, 110, "null"),
            });

        /// <summary>The check's own count of the buildings that should feed a group of this computer now: its fort and its
        /// factories nearest to the fort (centre to centre, the lower id on a tie), as many as the map's size in tiles gives.</summary>
        static void Feeders(ZWorld w, ZBot bot, Group g)
        {
            var fort = w.Objects.FirstOrDefault(o => o.IsFort && o.Owner == bot.Team && !o.Destroyed);
            var have = bot.StrikeFeeders.ToList();
            if (fort == null) { g.feedersNote = "no fort"; return; }
            long D2(ZObject o) => (long)(o.CenterX - fort.CenterX) * (o.CenterX - fort.CenterX) + (long)(o.CenterY - fort.CenterY) * (o.CenterY - fort.CenterY);
            var fac = w.Objects.Where(o => o.Kind == ZKind.Building && o.IsProducer && !o.IsFort && o.Owner == bot.Team && !o.Destroyed).OrderBy(D2).ThenBy(o => o.Id).ToList();
            int tiles = w.Map.width * w.Map.height;
            int n = ZWorld.StrikeFactoriesForCheck > 0 ? ZWorld.StrikeFactoriesForCheck : tiles < ZWorld.StrikeSmallMapTiles ? 1 : tiles < ZWorld.StrikeLargeMapTiles ? 2 : 3;
            var want = new List<int> { fort.Id }; want.AddRange(fac.Take(n).Select(o => o.Id));
            g.factoriesOwned = fac.Count; g.feedersWanted = want.Count; g.feederFactories = Math.Max(0, have.Count - 1); g.feedersRight = have.SequenceEqual(want);
            g.feedersNote = $"the computer owns {fac.Count} factories ({string.Join(", ", fac.Select(o => F(Math.Sqrt(D2(o))) + " px"))} from its fort); the fort and {g.feederFactories} of them feed";
        }

        /// <summary>true (the default) = the newer kits' movement rules as a Play session sets them with the Options at their defaults
        /// (OzV11Kit.GameSwitches turns them off for every counted check): the calm movement (b49), the fort run and the rock rounding,
        /// the calm follow, the hero squads, the grenade boost ON; FAST UNITS PASS SLOW ONES and FRIENDLY UNITS OVERLAP OFF, as their
        /// Options rows are by default. Each is set only when its kit is installed. false = all of them off (the kit's first counts).</summary>
        public static bool NewerRulesAsInPlay = true;
        /// <summary>What-if rows: FAST UNITS PASS SLOW ONES on (its Options row is OFF by default).</summary>
        public static bool OvertakeOn;
        public static readonly string[] PlayOnRules = { "CalmMoveDeviation", "FortRunDeviation", "RockRoundDeviation", "CalmFollowDeviation", "HeroSquadDeviation", "GrenadeBoostDeviation" };

        /// <summary>The switches of a normal game with every rule the game turns on at a start; the strike groups as asked.</summary>
        public static OzV11Kit.Switches Game(bool strikeGroups)
        {
            var s = OzV11Kit.GameSwitches();
            OzV11Kit.TrySet(typeof(ZWorld), "DriverFireDeviation", true);
            foreach (var n in new[] { "CrocodileDeviation", "CrocHurryDeviation", "CrocFleeDeviation", "CrocWaterCostDeviation" }) OzV11Kit.TrySet(typeof(ZWorld), n, true);
            if (NewerRulesAsInPlay) foreach (var n in PlayOnRules) OzV11Kit.TrySet(typeof(ZWorld), n, true);
            OzV11Kit.TrySet(typeof(ZWorld), "OvertakeDeviation", OvertakeOn);
            OzV11Kit.TrySet(typeof(ZWorld), "FriendsOverlap", false);
            ZWorld.StrikeGroupDeviation = strikeGroups;
            return s;
        }

        /// <summary>Which of the newer kits' rules a run had on (for the report's first line).</summary>
        public static string RulesNote()
        {
            var on = new List<string>();
            foreach (var n in PlayOnRules.Concat(new[] { "OvertakeDeviation", "FriendsOverlap" }))
            {
                var f = typeof(ZWorld).GetField(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (f == null) continue;
                bool v = n == "OvertakeDeviation" ? OvertakeOn : n == "FriendsOverlap" ? false : NewerRulesAsInPlay;
                on.Add(n.Replace("Deviation", "") + (v ? " on" : " off"));
            }
            return on.Count == 0 ? "" : " (newer rules: " + string.Join(", ", on) + ")";
        }

        static ulong Mix(ulong h, long v) { h ^= (ulong)v; h *= 1099511628211UL; h ^= h >> 31; return h; }
        /// <summary>Folds every object's place, health, owner and orders into h.</summary>
        public static ulong Fold(ulong h, ZWorld w)
        {
            h = Mix(h, w.Objects.Count);
            foreach (var o in w.Objects)
            {
                h = Mix(h, o.Id); h = Mix(h, (long)o.Owner); h = Mix(h, o.LocX); h = Mix(h, o.LocY); h = Mix(h, BitConverter.DoubleToInt64Bits(o.XOver)); h = Mix(h, BitConverter.DoubleToInt64Bits(o.YOver));
                h = Mix(h, o.Health); h = Mix(h, o.Waypoints.Count);
                foreach (var p in o.Waypoints) { h = Mix(h, (long)p.mode); h = Mix(h, p.refId); h = Mix(h, p.x); h = Mix(h, p.y); }
            }
            return h;
        }

        // ------------------------------------------------------------------ one match
        public static Result Match(string settings, string map, string level, int seed, Sides sides, bool on, ZBotDifficulty difficulty, double maxSeconds,
                                   Disturb disturb = Disturb.None, bool blueOnly = false, bool trace = false)
        {
            var R = new Result { level = level, seed = seed, sides = sides, on = on, difficulty = difficulty, disturb = disturb };
            using (Game(on))
            {
                ZWorld.StrikeFactoriesForCheck = FactoriesForCheck;                                   // (put back with the other switches)
                ZWorld.StrikeRoundOneForCheck = RoundOne;
                var w = ZWorld.Create(settings, map, seed, level == "L17");
                var blue = new ZBot(w, Team.Blue, seed + 101) { Difficulty = sides == Sides.ComputerMatch ? ZBotDifficulty.Normal : difficulty };
                w.Bots.Add(blue);
                ZBot red = null;
                if (sides == Sides.ComputerMatch) { red = new ZBot(w, Team.Red, seed) { StrikeGroupsOn = !blueOnly }; w.Bots.Add(red); }
                var blueFort = w.Objects.First(o => o.IsFort && o.Owner == Team.Blue);
                R.tiles = w.Map.width * w.Map.height;
                if (disturb == Disturb.OneFactoryLeft)
                {   // the computer has lost all but one factory, for the whole match: it keeps the one nearest its fort of those it owns
                    // at the start; every other factory of the map is out of the match (nobody's, and tied to no flag any more, so
                    // nobody can win it)
                    var keep = w.Objects.Where(o => o.Kind == ZKind.Building && o.IsProducer && !o.IsFort && o.Owner != Team.Red)
                                .OrderBy(o => o.Owner == Team.Blue ? 0 : 1)                                  // (when it owns none at the start: the nearest one it can win)
                                .ThenBy(o => D(o.CenterX, o.CenterY, blueFort.CenterX, blueFort.CenterY)).ThenBy(o => o.Id).FirstOrDefault();
                    int out0 = 0;
                    if (keep != null)
                        foreach (var b in w.Objects.Where(o => o.Kind == ZKind.Building && o.IsProducer && !o.IsFort && o != keep && o.Owner != Team.Red).ToList())
                        {
                            foreach (var f in w.Flags) f.LinkedObjects.Remove(b);
                            b.Owner = Team.Neutral; out0++;
                        }
                    R.trace.Add($"0:00 ONE FACTORY LEFT: the computer keeps {(keep != null ? keep.Type + " " + F(D(keep.CenterX, keep.CenterY, blueFort.CenterX, blueFort.CenterY)) + " px from its fort" + (keep.Owner == Team.Blue ? "" : " (nobody's at the start: it has to win it)") : "none (the map has none for it)")}; {out0} other factories are out of the match");
                }
                StandInState standIn = sides == Sides.PlayerHolds ? StandIn(w) : null;
                w.Events.Clear();
                ZWorld.StrikeGate(blueFort, out int gex, out int gey, out int gdx, out int gdy, out int wall);
                var places = w.StrikePlacesFor(blueFort, ZWorld.StrikeMaxMembers);
                R.placesFound = places.Count; R.placesMinApart = double.MaxValue;
                for (int i = 0; i < places.Count; i++)
                {
                    if (w.InExitLane(places[i].x, places[i].y, 20)) R.placesInLane++;
                    for (int j = i + 1; j < places.Count; j++) R.placesMinApart = Math.Min(R.placesMinApart, D(places[i].x, places[i].y, places[j].x, places[j].y));
                }
                int seen = 0; Group cur = null; var byId = new Dictionary<int, Member>(); bool disturbed = false; double disturbAt = -1;
                var snap = new Dictionary<int, (int x, int y, bool standing, int r)>();                 // where every member stood at the end of the last tick in which the group was still forming
                Dictionary<int, (int x, int y, bool standing, int r)> snapLeft = null;                  // ... and as the rule saw them when it let the group leave: after the units' movement of that tick (standing = not moving in it; the march orders it gave in that tick are not looked at)
                var watch = new List<(Group g, Member m, double until, int tx, int ty)>();           // former members: do they get busy, do they still arrive
                ulong fp = 1469598103934665603UL; double nextFold = 1.0, nextMarchLine = 0, nextFightLook = 0, nextRouteLook = 0;
                var offSince = new Dictionary<int, double>();
                var onRoute = new HashSet<int>(); var ownRoad = new HashSet<int>(); var joinFrom = new Dictionary<int, (double t, double d)>();   // on the route once in this march; reached the target area without joining it; when and how far off a member was first seen off the route before it joined it
                if (!trace) R.trace.Clear();
                void T(string s) { if (trace) R.trace.Add(Clock(w.Time) + " " + s); }
                // a squad goes on under its next robot: the member's record follows it (the rule's own member does the same)
                void Follow(Member m, bool known)
                {
                    var o = w.GetAny(m.id);
                    while (o != null && (o.Destroyed || w.Get(o.Id) != o) && o.Successor != null) o = o.Successor;
                    if (o == null || o.Id == m.id) return;
                    T($"  the squad of {m.type}#{m.id} goes on under #{o.Id}");
                    m.id = o.Id; if (!m.freed) m.leaderFell = true; if (known) byId[o.Id] = m;      // leaderFell: only while it was a member
                }
                try
                {
                    while (w.Time < maxSeconds && !w.GameOver)
                    {
                        if (cur != null && cur.formed >= 0 && w.Time >= nextFightLook)
                        {   // who of the group (also after its release) is in a fight before it has reached the target area
                            nextFightLook = w.Time + 0.5;
                            foreach (var m in cur.members) { if (m.lost || m.arrived >= 0) continue; var o = w.Get(m.id); if (o != null && Fights(o)) m.foughtOnWay = true; }
                        }
                        if (blue.StrikePhaseNow == ZBot.StrikePhase.Forming)
                        {
                            snap.Clear();
                            foreach (int id in blue.StrikeMemberIds())
                            {
                                var o = w.Get(id); if (o == null) continue;
                                bool standing = o.Waypoints.Count == 0 && !o.IsMoving; int r = ZPathing.Radius(o);
                                snap[id] = (o.CenterX, o.CenterY, standing, r);
                                if (!byId.TryGetValue(id, out var bm)) continue;
                                // how long does it stay on a spot nobody may stand on (every tick of every window, not only when the group leaves)
                                int front = gdy != 0 ? (o.CenterY - wall) * gdy : (o.CenterX - wall) * gdx, side = gdy != 0 ? Math.Abs(o.CenterX - gex) : Math.Abs(o.CenterY - gey);
                                bool bad = w.InExitLane(o.CenterX, o.CenterY, r) || (front > 0 && side < ZWorld.StrikeLaneHalfPx - ZWorld.StrikeSettlePx) || front < ZWorld.StrikeFrontMinPx - ZWorld.StrikeSettlePx;
                                if (!bad || (bm.badSince >= 0 && D(o.CenterX, o.CenterY, bm.badX, bm.badY) > 8)) bm.badSince = -1;
                                if (bad && bm.badSince < 0 && standing) { bm.badSince = w.Time; bm.badX = o.CenterX; bm.badY = o.CenterY; }
                                if (bm.badSince >= 0 && w.Time - bm.badSince > bm.worstBad) bm.worstBad = w.Time - bm.badSince;
                            }
                        }
                        if (cur != null && blue.StrikePhaseNow == ZBot.StrikePhase.Marching && w.Time >= nextRouteLook)
                        {   // round 2: does every marching member keep to the group's one route (a member in a fight is not judged)
                            nextRouteLook = w.Time + 1.0;
                            var line = blue.StrikeRouteLine();
                            if (line.Count >= 2)
                            {
                                cur.routeSeen = true;
                                foreach (int uid in blue.StrikeMemberIds())
                                {
                                    var o = w.Get(uid); if (o == null) continue;
                                    int id = byId.TryGetValue(uid, out var mk) ? mk.firstId : uid;      // a squad that goes on under its next robot keeps its record
                                    bool inArea = D(o.CenterX, o.CenterY, line[line.Count - 1].x, line[line.Count - 1].y) <= ZWorld.StrikeArrivePx;
                                    if (inArea && !onRoute.Contains(id) && joinFrom.ContainsKey(id) && ownRoad.Add(id)) { cur.ownRoad++; T($"    OWN ROAD {o.Type}#{o.Id} reached the target area without joining the route"); }
                                    if (Fights(o) || inArea) { offSince.Remove(id); continue; }
                                    double d = double.MaxValue;
                                    for (int i = 0; i + 1 < line.Count; i++) d = Math.Min(d, SegD(o.CenterX, o.CenterY, line[i].x, line[i].y, line[i + 1].x, line[i + 1].y));
                                    if (d <= CorridorPx)
                                    {
                                        if (onRoute.Add(id) && joinFrom.TryGetValue(id, out var jf)) { cur.worstJoin = Math.Max(cur.worstJoin, w.Time - jf.t); T($"    {o.Type}#{o.Id} joins the route {F(w.Time - jf.t)} s after the march began ({F(jf.d)} px off it then)"); }
                                        offSince.Remove(id); continue;
                                    }
                                    if (!onRoute.Contains(id))
                                    {   // not yet on the route (still on its way from its building, or its place lies to the side of the route): it joins it
                                        if (!joinFrom.ContainsKey(id)) { joinFrom[id] = (w.Time, d); cur.joiners++; cur.farthestJoiner = Math.Max(cur.farthestJoiner, d); }
                                        continue;
                                    }
                                    if (d > cur.farthestOffRoute) cur.farthestOffRoute = d;
                                    if (!offSince.TryGetValue(id, out double since)) offSince[id] = since = w.Time;
                                    if (TraceMarch && (w.Time - since) % 5.0 < 1.0) T($"    OFF THE ROUTE {o.Type}#{o.Id} at ({o.CenterX},{o.CenterY}) {F(d)} px from the route for {F(w.Time - since)} s; the route {string.Join(" ", line.Select(p => "(" + p.x + "," + p.y + ")"))}");
                                    if (w.Time - since > cur.worstOffRoute) cur.worstOffRoute = w.Time - since;
                                }
                            }
                        }
                        else if (blue.StrikePhaseNow != ZBot.StrikePhase.Marching) { offSince.Clear(); onRoute.Clear(); joinFrom.Clear(); ownRoad.Clear(); }
                        if (standIn != null) StandInKeep(w, standIn);
                        if (TraceMarch && trace && blue.StrikePhaseNow != ZBot.StrikePhase.Waiting && w.Time >= nextMarchLine) { nextMarchLine = w.Time + (blue.StrikePhaseNow == ZBot.StrikePhase.Marching ? 5 : 1); T("    " + blue.StrikeMemberLines()); }
                        bool wasForming = blue.StrikePhaseNow == ZBot.StrikePhase.Forming;
                        w.Tick(ZConst.TICK);
                        if (wasForming && blue.StrikePhaseNow == ZBot.StrikePhase.Marching)
                        {
                            snapLeft = new Dictionary<int, (int x, int y, bool standing, int r)>();
                            foreach (var kv in snap) { var o = w.Get(kv.Key); snapLeft[kv.Key] = o != null && !o.Destroyed ? (o.CenterX, o.CenterY, !o.IsMoving, kv.Value.r) : kv.Value; }
                        }
                        bool gone = false; int bl = 0, rl = 0;
                        foreach (var e in w.Events)
                        {
                            if (e.type == ZEventType.Death)
                            {
                                var o = w.GetAny(e.objId); if (o != null && o.IsMobile) { if (o.Owner == Team.Blue) bl++; else if (o.Owner == Team.Red) rl++; }
                                if (trace && o != null && cur != null && cur.ended < 0 && (byId.ContainsKey(o.Id) || (o.Leader != null && byId.ContainsKey(o.Leader.Id))))
                                { var k = w.GetAny(e.otherId); T($"  a body of the group falls: {o.Type}#{o.Id} at ({o.CenterX},{o.CenterY}) to {(k != null ? k.Owner + " " + k.Type + "#" + k.Id + " at (" + k.CenterX + "," + k.CenterY + ")" : "nobody's shot (id " + e.otherId + ")")}{(o.Eaten ? " (a crocodile)" : "")}"); }
                            }
                            else if (e.type == ZEventType.TeamEliminated || e.type == ZEventType.GameOver) gone = true;
                        }
                        if (!gone && !w.GameOver) { R.blueLost += bl; R.redLost += rl; }
                        w.Events.Clear();
                        if (w.Time >= nextFold) { nextFold += 1.0; fp = Fold(fp, w); }
                        if (cur != null) foreach (var m in cur.members) if (!m.lost) Follow(m, true);
                        foreach (var x in watch) Follow(x.m, false);
                        // ---- the computer's own record
                        for (; seen < blue.StrikeEvents.Count; seen++)
                        {
                            var e = blue.StrikeEvents[seen];
                            switch (e.kind)
                            {
                                case ZBot.StrikeEventKind.WindowOpened:
                                    cur = new Group { n = e.group, opened = e.time, feeders = e.n, cycle = e.note }; R.groups.Add(cur); byId.Clear();
                                    Feeders(w, blue, cur);
                                    T($"window {e.group} opens: {e.n} buildings, {e.note}; by the check's own count: {cur.feedersNote}{(cur.feedersRight ? "" : " (NOT the fort and its nearest factories)")}");
                                    break;
                                case ZBot.StrikeEventKind.Joined:
                                {
                                    var o = w.Get(e.unitId); var m = new Member { id = e.unitId, firstId = e.unitId, type = o?.Type ?? "?", joined = e.time, placeX = e.x, placeY = e.y };
                                    cur.members.Add(m); byId[e.unitId] = m; T($"  + {m.type}#{m.id} from building {e.n} -> place ({e.x},{e.y})");
                                    break;
                                }
                                case ZBot.StrikeEventKind.Settled: if (byId.TryGetValue(e.unitId, out var ms)) { ms.settled = true; T($"  {ms.type}#{ms.id} stands on its place after {F(e.time - ms.joined)} s, {F(D(e.x, e.y, ms.placeX, ms.placeY))} px from its middle"); } break;
                                case ZBot.StrikeEventKind.Formed:
                                    cur.formed = e.time; cur.how = e.note; cur.tx = e.x; cur.ty = e.y; cur.marchers = e.n;
                                    Formed(w, blueFort, cur, snapLeft ?? snap); snapLeft = null;
                                    T($"FORMED and leaves: {e.n} members ({e.note}); standing {cur.standingAll} (on their places {cur.onPlace}, of them on the spot they stopped on {cur.adopted}; off their place at that moment {cur.standingOff}), still walking {cur.offPlace - cur.standingOff}; least distance between two standing members {F(cur.minApartAll)} px; standing in the gate corridor {cur.inCorridor}, in an exit lane {cur.inLane}, too near the gate wall {cur.tooNear}; the most times one member was sent again {cur.maxResends}; target ({e.x},{e.y}) with {cur.atTarget} units (the most anywhere: {cur.biggest})");
                                    break;
                                case ZBot.StrikeEventKind.Departed: if (byId.TryGetValue(e.unitId, out var md) && !md.departed) { md.departed = true; md.left = e.time; T($"  {md.type}#{md.id} leaves (stood at the fort {F(e.time - md.joined)} s)"); } break;
                                case ZBot.StrikeEventKind.Retargeted: cur.retargeted = true; cur.tx = e.x; cur.ty = e.y; T($"  the target moved: now ({e.x},{e.y}), {e.n} units"); break;
                                case ZBot.StrikeEventKind.Route: if (cur != null) T($"  the common route: {e.n} points on {e.note}, the first ({e.x},{e.y}){(TraceMarch ? "; " + string.Join(" ", blue.StrikeRouteLine().Select(p => "(" + p.x + "," + p.y + ")")) : "")}"); break;
                                case ZBot.StrikeEventKind.Arrived: if (byId.TryGetValue(e.unitId, out var ma) && ma.arrived < 0) { ma.arrived = e.time; T($"  {ma.type}#{ma.id} reaches the target area {F(e.time - cur.formed)} s after the group formed"); } break;
                                case ZBot.StrikeEventKind.MemberLost: if (byId.TryGetValue(e.unitId, out var ml)) { ml.lost = true; if (ml.left < 0) ml.left = e.time; T($"  {ml.type}#{ml.id} is destroyed"); } break;
                                case ZBot.StrikeEventKind.MemberLeft:
                                    if (byId.TryGetValue(e.unitId, out var mf)) { if (mf.left < 0) mf.left = e.time; mf.freed = true; mf.freedAt = e.time; mf.leftWhy = e.note; watch.Add((cur, mf, e.time + 12.0, cur.tx, cur.ty)); T($"  {mf.type}#{mf.id} is released alone ({e.note})"); }
                                    break;
                                case ZBot.StrikeEventKind.PlaceChanged:
                                    if (byId.TryGetValue(e.unitId, out var mp))
                                    {
                                        mp.placeX = e.x; mp.placeY = e.y; if (e.n == 1) mp.adopted = true; else mp.moved = true;
                                        T($"  {mp.type}#{mp.id}: its place is now ({e.x},{e.y}), {(e.n == 1 ? "the spot it stands on" : "the next free place")} ({e.note})");
                                    }
                                    break;
                                case ZBot.StrikeEventKind.NoOrder: if (byId.TryGetValue(e.unitId, out var mn)) mn.freedIdleNoTarget = true; break;
                                case ZBot.StrikeEventKind.SingleOrder: if (byId.TryGetValue(e.unitId, out var mo)) T($"  {mo.type}#{mo.id} gets its own order (target {e.n})"); break;
                                case ZBot.StrikeEventKind.Reissued:
                                    if (byId.TryGetValue(e.unitId, out var mr))
                                    {
                                        if (cur.formed < 0) { mr.resends++; if (mr.resends > cur.maxResends) cur.maxResends = mr.resends; }
                                        T($"  {mr.type}#{mr.id} stood without an order: its order again");
                                    }
                                    break;
                                case ZBot.StrikeEventKind.GivenUp: case ZBot.StrikeEventKind.Released:
                                    cur.ended = e.time; cur.end = e.note; cur.gaveUp = e.kind == ZBot.StrikeEventKind.GivenUp;
                                    if (cur.formed >= 0) cur.marchSeconds = e.time - cur.formed;
                                    foreach (var m in cur.members)
                                    {
                                        if (m.lost || m.freed) continue;
                                        m.freed = true; m.freedAt = e.time; if (m.left < 0) m.left = e.time;
                                        watch.Add((cur, m, e.time + 12.0, cur.tx, cur.ty));
                                    }
                                    T($"{(cur.gaveUp ? "GIVEN UP" : "RELEASED")}: {e.note}; {e.n} members free");
                                    break;
                            }
                        }
                        // ---- former members: busy within 12 s? (an order or a target)
                        for (int i = watch.Count - 1; i >= 0; i--)
                        {
                            var (g, m, until, tx, ty) = watch[i];
                            var o = w.Get(m.id);
                            if (o != null && o.Destroyed && o.Owner == Team.Blue && o.Minions.Count > 0) continue;   // its leader fell in this tick: the squad's next robot is known in the next one (Follow)
                            if (o == null || o.Destroyed || o.KillAt >= 0 || o.Owner != Team.Blue) { if (m.busyAfter < 0) m.busyAfter = -2; watch.RemoveAt(i); continue; }   // gone before it could matter
                            if (m.busyAfter < 0 && (o.Waypoints.Count > 0 || o.AttackObject != null)) m.busyAfter = Math.Max(0, w.Time - m.freedAt);
                            if (m.arrived < 0 && g.formed >= 0 && (D(o.CenterX, o.CenterY, tx, ty) <= ZWorld.StrikeArrivePx || (o.AttackObject != null && o.AttackObject.IsUnit && D(o.AttackObject.CenterX, o.AttackObject.CenterY, tx, ty) <= ZWorld.StrikeArrivePx))) m.arrived = w.Time;
                            if (w.Time >= until + (g.formed >= 0 ? 60 : 0)) { if (m.busyAfter < 0) m.busyAfter = 999; watch.RemoveAt(i); }
                        }
                        // ---- the disturbance, once
                        if (disturb != Disturb.None && !disturbed && cur != null && cur.ended < 0)
                        {
                            bool forming = blue.StrikePhaseNow == ZBot.StrikePhase.Forming, marching = blue.StrikePhaseNow == ZBot.StrikePhase.Marching;
                            if ((disturb == Disturb.TakeTerritory || disturb == Disturb.TakeOtherTerritory) && forming && cur.members.Count >= 2 && w.Time - cur.opened >= 10)
                            {   // a territory that holds a feeding building (the window must end), or one that holds none (it must go on)
                                var watched = blue.StrikeWatchedFlags;
                                var f = w.Flags.Where(x => x.Owner == Team.Blue && watched.Contains(x.Id) == (disturb == Disturb.TakeTerritory))
                                         .OrderByDescending(x => D(x.CenterX, x.CenterY, blueFort.CenterX, blueFort.CenterY)).ThenBy(x => x.Id).FirstOrDefault();
                                if (f != null)
                                {
                                    disturbed = true; disturbAt = w.Time; w.AwardZone(f, Team.Red);
                                    R.disturbNote = disturb == Disturb.TakeTerritory ? "a feeding building's territory" : "a territory with no feeding building";
                                    T("DISTURBANCE: the player takes " + R.disturbNote + " of the computer");
                                }
                            }
                            else if (disturb == Disturb.CrocTakesMember && forming && cur.members.Count >= 2 && w.Time - cur.opened >= 10)
                            {   // a crocodile takes a robot of the group: a squad's robot first (its squad goes on), else a robot member
                                ZObject prey = null; Member who = null;
                                foreach (var m in cur.members)
                                {
                                    var o = w.Get(m.id);
                                    if (m.lost || m.freed || o == null || o.Destroyed || o.Kind != ZKind.Robot) continue;
                                    var mn = o.Minions.FirstOrDefault(x => x != null && !x.Destroyed);
                                    if (mn != null) { prey = mn; who = m; break; }
                                    if (prey == null) { prey = o; who = m; }
                                }
                                if (prey != null)
                                {
                                    disturbed = true; disturbAt = w.Time; R.disturbMember = who;
                                    R.disturbNote = prey == w.Get(who.id) ? "a robot member (no squad)" : "a robot of a squad";
                                    w.StrikeCrocTakeForCheck(prey);
                                    T($"DISTURBANCE: a crocodile takes {prey.Type}#{prey.Id} ({R.disturbNote}) of the forming group");
                                }
                            }
                            else if (disturb == Disturb.DestroyMember && forming && cur.members.Count >= 2 && w.Time - cur.opened >= 10)
                            {
                                disturbed = true; disturbAt = w.Time;
                                {
                                    var o = w.Get(cur.members[0].id);
                                    if (o != null) { R.disturbMember = cur.members[0]; o.SetHealth(0); w.UpdateObjectHealth(o); T($"DISTURBANCE: {o.Type}#{o.Id} of the group is destroyed"); }
                                }
                            }
                            else if (disturb == Disturb.DestroyLeader && forming && cur.members.Count >= 2 && w.Time - cur.opened >= 10)
                            {   // the LEADER of a squad of the group is destroyed by a shot, inside a tick, as the game does it; its robots live on.
                                // A squad that already stands on its place (on hold) first; from 25 s after the opening any squad.
                                Member pick = null;
                                foreach (var m in cur.members)
                                {
                                    var o = w.Get(m.id);
                                    if (m.lost || m.freed || o == null || o.Kind != ZKind.Robot || o.Minions.Count == 0 || o.Destroyed) continue;
                                    if (m.settled) { pick = m; break; }
                                    if (pick == null && w.Time - cur.opened >= 25) pick = m;
                                }
                                if (pick != null)
                                {
                                    var o = w.Get(pick.id);
                                    disturbed = true; disturbAt = w.Time; R.disturbMember = pick;
                                    o.SetHealth(1);
                                    w.AddMissileForTest(new ZMissile { x = o.CenterX, y = o.CenterY, damage = 60, radius = 5, team = Team.Red, explodeTime = w.Time });
                                    T($"DISTURBANCE: a shot at the leader {o.Type}#{o.Id} of a squad of the group ({o.Minions.Count} more robots; {(pick.settled ? "standing on its place" : "walking to its place")})");
                                }
                            }
                            else if (disturb == Disturb.CannotMove && marching && w.Time - cur.formed >= 5)
                            {   // a member keeps its order but cannot move any more
                                foreach (var m in cur.members)
                                {
                                    var o = w.Get(m.id);
                                    if (m.lost || m.freed || m.arrived >= 0 || o == null || o.Destroyed || o.AttackObject != null) continue;
                                    disturbed = true; disturbAt = w.Time; R.disturbMember = m;
                                    o.MoveSpeed = 0; foreach (var mn in o.Minions) mn.MoveSpeed = 0;
                                    T($"DISTURBANCE: {o.Type}#{o.Id} of the group cannot move any more (it keeps its order)");
                                    break;
                                }
                            }
                            else if ((disturb == Disturb.ArmyMoves || disturb == Disturb.ArmyGone) && marching && w.Time - cur.formed >= 15)
                            {
                                disturbed = true; disturbAt = w.Time;
                                var ids = new List<int>(); blue.StrikeGathering(blueFort.CenterX, blueFort.CenterY, out int ax, out int ay, ids);
                                if (standIn != null) standIn.keepGathering = false;
                                if (disturb == Disturb.ArmyGone) { foreach (int id in ids) { var o = w.Get(id); if (o != null) { foreach (var mn in o.Minions.ToList()) { mn.SetHealth(0); w.UpdateObjectHealth(mn); } o.SetHealth(0); w.UpdateObjectHealth(o); } } T($"DISTURBANCE: the {ids.Count} units of the gathering are gone"); }
                                else
                                {
                                    // away to the farther one of his two own places: his gate, his gathering place
                                    var redFort = w.Objects.First(x => x.IsFort && x.Owner == Team.Red);
                                    ZWorld.StrikeGate(redFort, out int rex, out int rey, out _, out int rdy, out _);
                                    int ox = rex, oy = rey + rdy * 96;
                                    if (standIn != null && D(ax, ay, standIn.gx, standIn.gy) > D(ax, ay, ox, oy)) { ox = standIn.gx; oy = standIn.gy; }
                                    foreach (int id in ids) { var o = w.Get(id); if (o != null) w.SendWaypoints(Team.Red, o, new[] { ZWaypoint.Move(ox + (o.CenterX - ax) / 2, oy + (o.CenterY - ay) / 2, false) }); }
                                    T($"DISTURBANCE: the {ids.Count} units of the gathering are ordered away to ({ox},{oy}), {F(D(ax, ay, ox, oy))} px off");
                                }
                            }
                            else if (disturb == Disturb.SwitchOff && (forming || marching) && cur.members.Count >= 2 && w.Time - cur.opened >= 20)
                            { disturbed = true; disturbAt = w.Time; ZWorld.StrikeGroupDeviation = false; T("DISTURBANCE: the switch is turned off"); }
                        }
                    }
                }
                catch (Exception ex) { R.error = ex.GetType().Name + ": " + ex.Message + " at " + Clock(w.Time) + "\n" + ex.StackTrace; }
                foreach (var (g, m, until, tx, ty) in watch) if (m.busyAfter < 0) m.busyAfter = -2;                           // the match ended first
                R.end = w.Time; R.over = w.GameOver; R.winner = w.Winner; R.fingerprint = Fold(fp, w);
                R.disturbAt = disturbAt; R.membersHeldAtEnd = w.Objects.Count(o => o.StrikeHeld); R.eaten = blue.StrikeEatenCount;
                if (disturbAt >= 0) R.windowsAfterDisturb = R.groups.Count(g => g.opened > disturbAt);
                if (red != null) R.redGroups = red.StrikeGroupsFormed;
                foreach (var g in R.groups) Marched(g);
                T(w.GameOver ? "the match is over: winner " + w.Winner : "the match is not over at the limit");
            }
            return R;
        }

        /// <summary>The moment a group has formed: where its members stand, and what it marches at.</summary>
        static void Formed(ZWorld w, ZObject fort, Group g, Dictionary<int, (int x, int y, bool standing, int r)> snap)
        {
            // pts: the members on the places the rule searched; all: every member that stands, wherever it stands (also on a spot it took as its place)
            var pts = new List<(double x, double y)>(); var all = new List<(double x, double y, int r)>();
            ZWorld.StrikeGate(fort, out int ex, out int ey, out int gdx, out int gdy, out int wall);
            int slack = ZWorld.StrikeSettlePx;                                                   // "on its place" = within this of its middle: the same slack for the corridor and the gate wall
            foreach (var m in g.members)
            {
                if (m.lost || m.freed || !snap.TryGetValue(m.id, out var p)) continue;
                m.offPlace = D(p.x, p.y, m.placeX, m.placeY);
                m.standing = p.standing; m.onPlace = p.standing && m.offPlace <= ZWorld.StrikeSettlePx;
                if (p.standing)
                {   // EVERY standing member, on its place or off it (a member that walks out of its building's door is not counted: that is the lane's use)
                    int front = gdy != 0 ? (p.y - wall) * gdy : (p.x - wall) * gdx, side = gdy != 0 ? Math.Abs(p.x - ex) : Math.Abs(p.y - ey);
                    g.standingAll++; all.Add((p.x, p.y, p.r));
                    if (w.InExitLane(p.x, p.y, p.r)) g.inLane++;
                    if (front > 0 && side < ZWorld.StrikeLaneHalfPx - slack) g.inCorridor++;
                    if (front < ZWorld.StrikeFrontMinPx - slack) g.tooNear++;
                    if (!m.onPlace) g.standingOff++;
                }
                if (!m.onPlace) { g.offPlace++; continue; }                                      // still walking to its place, or just made to step aside, when the cycle ended: it leaves from where it is
                g.standing++; g.onPlace++;
                if (m.adopted) g.adopted++; else pts.Add((p.x, p.y));
                if (m.moved) g.movedPlace++;
                if (m.offPlace > g.worstOffPlace) g.worstOffPlace = m.offPlace;
            }
            g.atCycleEnd = g.how != null && g.how.StartsWith("the cycle is over");
            for (int i = 0; i < pts.Count; i++) for (int j = i + 1; j < pts.Count; j++) { double d = D(pts[i].x, pts[i].y, pts[j].x, pts[j].y); if (g.minApart < 0 || d < g.minApart) g.minApart = d; }
            for (int i = 0; i < all.Count; i++)
                for (int j = i + 1; j < all.Count; j++)
                {
                    double d = D(all[i].x, all[i].y, all[j].x, all[j].y);
                    if (g.minApartAll < 0 || d < g.minApartAll) g.minApartAll = d;
                    if (d < all[i].r + all[j].r) g.overlap++;                                    // two bodies in each other
                }
            // the check's own count of the other side's units round the target, and the most round any of them
            var army = w.Objects.Where(o => o.IsMobile && !o.IsMinion && o.Owner == Team.Red && !o.Destroyed && o.KillAt < 0 && !o.IsFortDefender).ToList();
            foreach (var u in army)
            {
                var near = army.Where(o => D(o.CenterX, o.CenterY, u.CenterX, u.CenterY) <= ZWorld.StrikeGatherPx).ToList();
                if (near.Count > g.biggest) g.biggest = near.Count;
                int mx = (int)(near.Sum(o => (long)o.CenterX) / near.Count), my = (int)(near.Sum(o => (long)o.CenterY) / near.Count);
                if (mx == g.tx && my == g.ty && near.Count > g.atTarget) g.atTarget = near.Count;      // the rule's target point is the middle of this unit's gathering
            }
        }

        /// <summary>In a fight: the rule's own test (ZBot.StrikeGroup.cs SgFighting: a target, or an attack / agro order first; the rule
        /// lets such a member move freely). Round 2: the first round's check looked at the target only and missed a squad whose
        /// leader had the agro order while its robots fired.</summary>
        static bool Fights(ZObject o) => o.AttackObject != null || (o.Waypoints.Count > 0 && (o.Waypoints[0].mode == WpMode.Attack || o.Waypoints[0].mode == WpMode.Agro));

        /// <summary>The distance of a point from a segment.</summary>
        static double SegD(double px, double py, double ax, double ay, double bx, double by)
        {
            double vx = bx - ax, vy = by - ay, l2 = vx * vx + vy * vy;
            double u = l2 <= 0 ? 0 : Math.Max(0, Math.Min(1, ((px - ax) * vx + (py - ay) * vy) / l2));
            return D(px, py, ax + u * vx, ay + u * vy);
        }

        /// <summary>After the match: how the march of a group went.</summary>
        static void Marched(Group g)
        {
            if (g.formed < 0) return;
            var arr = g.members.Where(m => m.arrived >= 0).Select(m => m.arrived).OrderBy(t => t).ToList();
            g.arrivedAll = arr.Count; g.lostOnMarch = g.members.Count(m => m.lost);
            if (arr.Count > 0) { g.firstArrival = arr[0] - g.formed; g.lastAfterFirst = arr[arr.Count - 1] - arr[0]; }
            for (int i = 0; i + 2 < arr.Count; i++) if (g.thirdAfterFirst < 0 || arr[i + 2] - arr[i] < g.thirdAfterFirst) g.thirdAfterFirst = arr[i + 2] - arr[i];   // the tightest three
            for (int i = 0; i < arr.Count; i++) { int k = arr.Count(t => t >= arr[i] && t <= arr[i] + 10.0); if (k > g.arrivedIn10) g.arrivedIn10 = k; }
            if (g.ended < 0) return;
            g.marchKind = g.arrivedIn10 >= 3 ? 0 : g.marchers - g.lostOnMarch < 3 || (g.arrivedAll < 3 && g.members.Count(m => !m.lost && m.arrived < 0 && !m.foughtOnWay) == 0 && g.lostOnMarch > 0) ? 1
                        : g.members.Any(m => m.foughtOnWay && !m.lost) ? 2 : 3;
        }

        // ------------------------------------------------------------------ the whole check
        public sealed class Tally
        {
            public int matches, reached8, withGroup, windows, formed, gaveUp, errors;
            public readonly Dictionary<string, int> why = new Dictionary<string, int>(), ends = new Dictionary<string, int>();
            public int blueWins, redWins, open; public double lengthSum, winLengthSum; public int blueLost, redLost;
            public double worstWait; public int members, standingGroups; public double leastApart = double.MaxValue, worstOff; public int inLane, tooClose, onPlace, offPlaceEarly, offPlaceEnd, leftEarly, leftAtEnd;
            public int marches, marches3, broken, heldUp, straggle; public int freed, freedBusy, freedLate, freedNoTarget; public double worstBusy;
            public int routeMarches, offRoute, eaten; public double worstOffRoute, farthestOffRoute;
            public int joiners, ownRoad, ownRoadMarches; public double farthestJoiner, worstJoin;
            public double firstWindow = double.MaxValue, leastGap = double.MaxValue; public int targetBiggest, targetChecked;
            public readonly List<int> sizes = new List<int>(); public readonly List<double> windowSeconds = new List<double>(), marchSeconds = new List<double>(), spreads = new List<double>(), firstArrivals = new List<double>(), cycles = new List<double>();
            public readonly List<string> lines = new List<string>();
            /// <summary>Every standing member of every group that left: how many, in the gate corridor, too near the gate wall, bodies
            /// in each other, standing off their place at that moment, on a spot they stopped on, on another place; the least
            /// distance between any two; the most times one member was sent again in one window, and the windows with 10 or more.</summary>
            public int standingAll, inCorridor, tooNear, overlap, standingOff, adopted, movedPlace, maxResends, resendWindows; public double leastApartAll = double.MaxValue;
            /// <summary>Windows whose feeding buildings were / were not the fort and its nearest factories (the check's own count).</summary>
            public int feedersRight, feedersWrong; public readonly Dictionary<string, int> feederKinds = new Dictionary<string, int>(), leftWhy = new Dictionary<string, int>();
            /// <summary>Squads whose leader fell while they were members, and how many of them were not busy within 12 s of their release.</summary>
            public int leaderFell, leaderFellLate;
            /// <summary>The longest time a member stayed on a spot nobody may stand on while its group formed; members over the limit.</summary>
            public double worstBad; public int badLasting;
        }

        public static void Add(Tally t, Result r)
        {
            t.matches++; if (r.error != null) t.errors++;
            if (r.end >= ZWorld.StrikeStartSec) t.reached8++;
            if (r.groups.Any(g => g.formed >= 0)) t.withGroup++;
            if (!r.over) t.open++; else if (r.winner == Team.Blue) { t.blueWins++; t.winLengthSum += r.end; } else if (r.winner == Team.Red) t.redWins++;
            t.lengthSum += r.end; t.blueLost += r.blueLost; t.redLost += r.redLost; t.eaten += r.eaten;
            double lastFormed = -1;
            foreach (var g in r.groups)
            {
                t.windows++; t.firstWindow = Math.Min(t.firstWindow, g.opened);
                if (g.feedersRight) t.feedersRight++; else t.feedersWrong++;
                { string k = $"the fort and {g.feederFactories} of {g.factoriesOwned} factories"; t.feederKinds[k] = t.feederKinds.TryGetValue(k, out int c0) ? c0 + 1 : 1; }
                if (g.maxResends > t.maxResends) t.maxResends = g.maxResends;
                if (g.maxResends >= 10) t.resendWindows++;
                if (g.cycle != null && g.cycle.StartsWith("cycle ") && double.TryParse(g.cycle.Substring(6, g.cycle.IndexOf(' ', 6) - 6), System.Globalization.NumberStyles.Float, Inv, out double cyc)) t.cycles.Add(cyc);
                if (g.formed < 0) { t.gaveUp++; string k = g.end ?? "the match ended first"; t.why[k] = t.why.TryGetValue(k, out int c) ? c + 1 : 1; }
                else
                {
                    t.formed++; t.sizes.Add(g.marchers); t.windowSeconds.Add(g.formed - g.opened);
                    if (lastFormed >= 0) t.leastGap = Math.Min(t.leastGap, g.formed - lastFormed);
                    lastFormed = g.formed;
                    if (g.onPlace - g.adopted >= 2) { t.standingGroups++; t.leastApart = Math.Min(t.leastApart, g.minApart); if (g.minApart < ZWorld.StrikePlacePitchPx - 2 * ZWorld.StrikeSettlePx) t.tooClose++; }
                    t.onPlace += g.onPlace; if (g.atCycleEnd) { t.leftAtEnd++; t.offPlaceEnd += g.offPlace; } else { t.leftEarly++; t.offPlaceEarly += g.offPlace; }
                    t.worstOff = Math.Max(t.worstOff, g.worstOffPlace); t.inLane += g.inLane;
                    t.standingAll += g.standingAll; t.inCorridor += g.inCorridor; t.tooNear += g.tooNear; t.overlap += g.overlap; t.standingOff += g.standingOff; t.adopted += g.adopted; t.movedPlace += g.movedPlace;
                    if (g.minApartAll >= 0) t.leastApartAll = Math.Min(t.leastApartAll, g.minApartAll);
                    t.targetChecked++; if (g.atTarget >= g.biggest) t.targetBiggest++;
                    if (g.ended >= 0)
                    {
                        string k = g.end ?? "?"; t.ends[k] = t.ends.TryGetValue(k, out int c) ? c + 1 : 1;
                        t.marches++; t.marchSeconds.Add(g.marchSeconds);
                        if (g.thirdAfterFirst >= 0) t.spreads.Add(g.thirdAfterFirst);
                        if (g.firstArrival >= 0) t.firstArrivals.Add(g.firstArrival);
                        if (g.marchKind == 0) t.marches3++; else if (g.marchKind == 1) t.broken++; else if (g.marchKind == 2) t.heldUp++; else t.straggle++;
                        if (g.routeSeen) { t.routeMarches++; if (g.worstOffRoute > OffRouteLimitSec) t.offRoute++; t.worstOffRoute = Math.Max(t.worstOffRoute, g.worstOffRoute); t.farthestOffRoute = Math.Max(t.farthestOffRoute, g.farthestOffRoute);
                                         t.joiners += g.joiners; t.ownRoad += g.ownRoad; if (g.ownRoad > 0) t.ownRoadMarches++; t.farthestJoiner = Math.Max(t.farthestJoiner, g.farthestJoiner); t.worstJoin = Math.Max(t.worstJoin, g.worstJoin); }
                    }
                }
                foreach (var m in g.members)
                {
                    t.members++;
                    if (m.worstBad > t.worstBad) t.worstBad = m.worstBad;
                    if (m.worstBad > BadStandLimit) t.badLasting++;
                    if (m.leftWhy != null) t.leftWhy[m.leftWhy] = t.leftWhy.TryGetValue(m.leftWhy, out int c1) ? c1 + 1 : 1;
                    if (m.leaderFell && m.freed && m.busyAfter != -2) { t.leaderFell++; if (!(m.busyAfter >= 0 && m.busyAfter <= 12.0) && !m.freedIdleNoTarget) t.leaderFellLate++; }
                    if (m.left >= 0) { double wait = (m.left - m.joined) / (r.difficulty == ZBotDifficulty.Easy && ZWorld.EasyCpuSlowerOn ? ZWorld.EasyDecisionSlow : 1.0); if (wait > t.worstWait) t.worstWait = wait; }   // K434: an Easy group stands 1.3 x longer: its wait read on that clock
                    if (m.freed && m.busyAfter != -2)
                    {
                        t.freed++;
                        if (m.busyAfter >= 0 && m.busyAfter <= 12.0) { t.freedBusy++; t.worstBusy = Math.Max(t.worstBusy, m.busyAfter); }
                        else if (m.freedIdleNoTarget) t.freedNoTarget++; else t.freedLate++;
                    }
                }
            }
        }

        static string Stat(List<double> v) { if (v.Count == 0) return "-"; var s = v.OrderBy(x => x).ToList(); return $"{F(s[0])} to {F(s[s.Count - 1])} s (median {F(s[s.Count / 2])})"; }
        static string StatI(List<int> v) { if (v.Count == 0) return "-"; var s = v.OrderBy(x => x).ToList(); return $"{s[0]} to {s[s.Count - 1]} (median {s[s.Count / 2]}, mean {F(s.Average(), "0.0")})"; }

        public static string Line(Result r)
        {
            string g = string.Join("; ", r.groups.Select(x => (x.formed >= 0
                ? $"group at {Clock(x.formed)} ({x.marchers} members, window {F(x.formed - x.opened)} s, {x.arrivedAll} arrived, most in 10 s {x.arrivedIn10}, lost {x.lostOnMarch}, {x.end ?? "running"}{(x.worstOffRoute > OffRouteLimitSec ? ", OFF THE ROUTE " + F(x.worstOffRoute) + " s, " + F(x.farthestOffRoute) + " px" : "")}{(x.ownRoad > 0 ? ", OWN ROAD " + x.ownRoad : "")}{(!x.atCycleEnd && x.offPlace > 0 ? ", LEFT ALL STANDING WITH " + x.offPlace + " OFF THEIR PLACE" : "")}{(x.joiners > 0 ? ", joined after " + F(x.worstJoin) + " s" : "")}{(x.marchKind == 3 ? ", NEITHER" : "")})"
                : $"window {Clock(x.opened)} given up ({x.end ?? "match ended"}, {x.members.Count} members)") + (x.members.Any(mm => mm.worstBad > BadStandLimit) ? $" [STOOD IN THE GATE'S WAY {F(x.members.Max(mm => mm.worstBad), "0.0")} s]" : "")));
            return $"    seed {r.seed}: {(r.over ? "ends " + Clock(r.end) + ", winner " + r.winner : "not over at " + Clock(r.end))}; blue lost {r.blueLost}, red lost {r.redLost}; {(g.Length > 0 ? g : "no window")}{(r.error != null ? "; ERROR " + r.error.Split('\n')[0] : "")}";
        }

        /// <summary>The check. seeds = matches per level and row; minutes = the cap of a match. Returns the report, PASS / FAIL lines at its end.</summary>
        public static string Run(string settings, Func<string, string> map, int seeds, double minutes, Action<string> progress = null)
        {
            var sb = new StringBuilder(); double cap = minutes * 60;
            sb.AppendLine($"ENEMY STRIKE GROUPS, counted: {seeds} matches per level and row (seeds {FirstSeed} to {FirstSeed + seeds - 1}), each at most {F(minutes)} min of game time; blue = the computer; the game's switches{RulesNote()}.");
            sb.AppendLine($"The rule's numbers: no window before {Clock(ZWorld.StrikeStartSec)}; {Clock(ZWorld.StrikeGapSec)} between groups; {F(ZWorld.StrikeRetrySec)} s after a window given up; window at most {F(ZWorld.StrikeCycleLimitSec)} s; {ZWorld.StrikeMinMembers} to {ZWorld.StrikeMaxMembers} members; places {ZWorld.StrikePlacePitchPx} px apart, {ZWorld.StrikeFrontMinPx} to {ZWorld.StrikeFrontMaxPx} px in front of the gate wall; gathering {ZWorld.StrikeGatherPx} px; target area {ZWorld.StrikeArrivePx} px; march limit {F(ZWorld.StrikeMarchLimitSec)} s.");
            sb.AppendLine();
            var all = new Tally(); var perRow = new List<(string name, Tally t)>();
            var rows = new (string name, Sides sides, ZBotDifficulty diff, bool on, bool blueOnly)[]
            {
                ("player holds, MEDIUM / HARD computer, strike groups ON", Sides.PlayerHolds, ZBotDifficulty.Normal, true, false),
                ("player holds, EASY computer, strike groups ON", Sides.PlayerHolds, ZBotDifficulty.Easy, true, false),
                ("computer match, strike groups ON for both", Sides.ComputerMatch, ZBotDifficulty.Normal, true, false),
            };
            foreach (var lv in Levels)
            {
                string m = map(lv);
                foreach (var row in rows)
                {
                    var t = new Tally(); var lines = new StringBuilder();
                    for (int seed = FirstSeed; seed < FirstSeed + seeds; seed++)
                    {
                        var r = Match(settings, m, lv, seed, row.sides, row.on, row.diff, cap, Disturb.None, row.blueOnly);
                        Add(t, r); Add(all, r); lines.AppendLine(Line(r));
                        progress?.Invoke($"{lv} {row.name} seed {seed}: {Line(r).Trim()}");
                    }
                    perRow.Add((LevelName(lv) + ", " + row.name, t));
                    sb.AppendLine($"{LevelName(lv)} ({lv}), {row.name}");
                    sb.AppendLine("  " + Summary(t));
                    sb.Append(lines);
                    sb.AppendLine();
                }
            }
            var extra = new List<string>();
            sb.Append(General(settings, map, Math.Min(seeds, 3), cap, all, extra, progress));
            sb.Append(Verdicts(all, perRow, extra));
            return sb.ToString();
        }

        /// <summary>
        /// THE GENERAL RULE: nothing in the rule knows a level. The same matches (player holds, MEDIUM / HARD computer) on a small
        /// made-up map with one factory beside the computer's fort and on a large one with five, and on every level with all but
        /// one of the computer's factories taken from it until its first window opens. Judged: which buildings fed each window,
        /// against the check's own count (the fort and its nearest factories, as many as the map's size gives: 1, 2 or 3).
        /// </summary>
        public static string General(string settings, Func<string, string> map, int seeds, double cap, Tally all, List<string> verdicts, Action<string> progress = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"THE GENERAL RULE: {seeds} matches per row (seeds {FirstSeed} to {FirstSeed + seeds - 1}), player holds, MEDIUM / HARD computer, strike groups ON.");
            var rows = new (string name, string label, Func<string> mapText, Disturb d, bool parked)[]
            {
                ("a small made-up map (48 x 100 = 4,800 tiles; the computer owns one factory beside its fort and one farther off)", "made-up small", SmallMap, Disturb.None, false),
                ("a large made-up map (120 x 140 = 16,800 tiles; the computer owns five factories)", "made-up large", LargeMap, Disturb.None, false),
            };
            foreach (var row in rows)
            {
                var t = new Tally(); var lines = new StringBuilder(); string m = row.mapText(); int tiles = 0, wrong = 0, windows = 0; var kinds = new Dictionary<string, int>();
                for (int seed = FirstSeed; seed < FirstSeed + seeds; seed++)
                {
                    var r = Match(settings, m, row.label, seed, Sides.PlayerHolds, true, ZBotDifficulty.Normal, cap, row.d);
                    Add(t, r); Add(all, r); lines.AppendLine(Line(r)); tiles = r.tiles;
                    foreach (var g in r.groups) { windows++; if (!g.feedersRight) wrong++; string k = g.feedersNote ?? "?"; kinds[k] = kinds.TryGetValue(k, out int c) ? c + 1 : 1; }
                    progress?.Invoke($"{row.label} seed {seed}: {Line(r).Trim()}");
                }
                sb.AppendLine(row.name);
                sb.AppendLine("  " + Summary(t));
                foreach (var kv in kinds.OrderByDescending(k => k.Value)) sb.AppendLine($"  {kv.Value} windows: {kv.Key}");
                sb.Append(lines); sb.AppendLine();
                int n = tiles < ZWorld.StrikeSmallMapTiles ? 1 : tiles < ZWorld.StrikeLargeMapTiles ? 2 : 3;
                verdicts.Add(OzV11Kit.Verdict(t.errors == 0 && windows > 0 && wrong == 0 && t.formed > 0,
                    $"{row.name}: the fort and its {n} nearest factories feed (all it owns when it owns fewer): {windows - wrong}/{windows} windows by the check's own count ({string.Join("; ", t.feederKinds.OrderByDescending(k => k.Value).Select(k => k.Value + " x " + k.Key))}); groups formed {t.formed} in {t.withGroup}/{t.matches} matches, size {StatI(t.sizes)}; exceptions {t.errors}"));
            }
            {   // all but one factory lost
                var t = new Tally(); var lines = new StringBuilder(); int windows = 0, one = 0, right = 0, withGroup = 0, withWindow = 0; var notes = new List<string>();
                foreach (var lv in Levels)
                    for (int seed = FirstSeed; seed < FirstSeed + Math.Min(seeds, 2); seed++)
                    {
                        var r = Match(settings, map(lv), lv, seed, Sides.PlayerHolds, true, ZBotDifficulty.Normal, cap, Disturb.OneFactoryLeft);
                        Add(t, r); Add(all, r); lines.AppendLine("  " + LevelName(lv) + Line(r));
                        progress?.Invoke($"one factory left, {lv} seed {seed}: {Line(r).Trim()}");
                        if (r.groups.Count > 0) withWindow++;
                        if (r.groups.Any(x => x.formed >= 0)) withGroup++;
                        foreach (var g in r.groups)
                        {
                            windows++; if (g.factoriesOwned == 1) one++;
                            if (g.feedersRight && g.feederFactories == 1 && g.factoriesOwned == 1) right++;
                        }
                        var g0 = r.groups.FirstOrDefault();
                        notes.Add(g0 == null ? $"{LevelName(lv)} seed {seed}: no window opened" : $"{LevelName(lv)} seed {seed}: first window at {Clock(g0.opened)}: {g0.feedersNote}{(g0.formed >= 0 ? "; it formed a group of " + g0.marchers : "; given up (" + (g0.end ?? "the match ended") + ")")}");
                    }
                sb.AppendLine("every level with all but one of the computer's factories lost for the whole match (it keeps the one nearest its fort; every other factory of the map is nobody's and tied to no flag)");
                sb.AppendLine("  " + Summary(t));
                foreach (var x in notes) sb.AppendLine("  " + x);
                sb.Append(lines); sb.AppendLine();
                verdicts.Add(OzV11Kit.Verdict(t.errors == 0 && windows > 0 && right == windows && withGroup > 0,
                    $"all but one factory lost (every level; the other factories are out of the match from the start): every window is fed by the fort and that one factory: {right}/{windows} windows (the computer owned exactly one factory in {one}); a window opened in {withWindow}/{t.matches} matches, a group formed in {withGroup}, size {StatI(t.sizes)}; exceptions {t.errors}"));
            }
            return sb.ToString();
        }

        public static string Summary(Tally t)
        {
            string why = t.why.Count == 0 ? "" : " (" + string.Join(", ", t.why.OrderByDescending(k => k.Value).Select(k => k.Key + ": " + k.Value)) + ")";
            string ends = t.ends.Count == 0 ? "" : "; marches ended: " + string.Join(", ", t.ends.OrderByDescending(k => k.Value).Select(k => k.Key + ": " + k.Value));
            return $"matches {t.matches}: lasted to 8:00 {t.reached8}, with at least one group {t.withGroup}; windows {t.windows}, groups formed {t.formed}, given up {t.gaveUp}{why}; group size {StatI(t.sizes)}; one production cycle of the feeding buildings when the window opened {Stat(t.cycles)}; from the window's opening to the group's leaving {Stat(t.windowSeconds)}; first member in the target area after {Stat(t.firstArrivals)}; the tightest three arrivals of a march within {Stat(t.spreads)}; three within 10 s in {t.marches3}/{t.marches} marches, fewer than three left by fire {t.broken}, held up by a fight on the way {t.heldUp}, neither {t.straggle}; march until the release {Stat(t.marchSeconds)}{ends}; " +
                   $"standing members when a group left {t.standingAll}: on a spot they stopped on beside their place {t.adopted}, on another place than their first {t.movedPlace}, off their place at that moment {t.standingOff}; the longest stay of a member on a spot nobody may stand on {F(t.worstBad, "0.0")} s; the most times one member was sent to its place again in a window {t.maxResends}; released alone: {(t.leftWhy.Count == 0 ? "0" : string.Join(", ", t.leftWhy.OrderByDescending(k => k.Value).Select(k => k.Key + ": " + k.Value)))}; " +
                   $"blue wins {t.blueWins}, red wins {t.redWins}, not over {t.open}; mean length {F(t.lengthSum / Math.Max(1, t.matches) / 60, "0.0")} min; blue lost {t.blueLost}, red lost {t.redLost}";
        }

        /// <summary>The PASS / FAIL lines over a set of matches. extra = lines of the special rows (cancel cases, moving target), already judged.</summary>
        public static string Verdicts(Tally t, List<(string name, Tally t)> perRow, List<string> extra)
        {
            var sb = new StringBuilder();
            sb.AppendLine(OzV11Kit.Verdict(t.errors == 0, $"no exception in {t.matches} matches ({t.errors} with one)"));
            sb.AppendLine(OzV11Kit.Verdict(t.reached8 > 0 && t.withGroup * 2 > t.reached8, $"a group forms in most matches that last to 8:00: {t.withGroup}/{t.reached8} (matches in all {t.matches}; the others were over before 8:00)"));
            sb.AppendLine(OzV11Kit.Verdict(t.windows == 0 || t.firstWindow >= ZWorld.StrikeStartSec, $"no window before 8:00: the earliest of {t.windows} opened at {(t.windows == 0 ? "-" : Clock(t.firstWindow))}"));
            sb.AppendLine(OzV11Kit.Verdict(t.leastGap >= ZWorld.StrikeGapSec, $"never two formations within 7:00: the least time between two groups of one match {(t.leastGap == double.MaxValue ? "- (no match had two)" : Clock(t.leastGap))}"));
            sb.AppendLine(OzV11Kit.Verdict(t.tooClose == 0 && t.badLasting == 0 && t.overlap == 0 && t.offPlaceEarly == 0,
                $"members stand on separate places, apart, out of the gate's way and of the exit lanes, not too near the gate. (a) Every member, every tick of every window ({t.members} members, {t.windows} windows): the longest time one stayed on a spot nobody may stand on (the corridor along the gate line, an exit lane, nearer than {ZWorld.StrikeFrontMinPx - ZWorld.StrikeSettlePx} px to the gate wall; counted from the moment it stood there with no order) {F(t.worstBad, "0.0")} s; members over {F(BadStandLimit)} s: {t.badLasting}. " +
                $"(b) EVERY standing member of every group at the moment it left ({t.standingAll} members, {t.formed} groups): in the corridor {t.inCorridor}, in an exit lane {t.inLane}, too near the gate wall {t.tooNear} (each of these is inside the time of (a)); pairs with their bodies in each other {t.overlap}; the least distance between two standing members {(t.leastApartAll == double.MaxValue ? "-" : F(t.leastApartAll))} px. " +
                $"(c) Members on their place: {t.onPlace}: {t.onPlace - t.adopted} on a place the rule searched (in {t.standingGroups - t.tooClose}/{t.standingGroups} groups each at least {ZWorld.StrikePlacePitchPx - 2 * ZWorld.StrikeSettlePx} px from the next, least seen {(t.leastApart == double.MaxValue ? "-" : F(t.leastApart))} px; the places are {ZWorld.StrikePlacePitchPx} px apart, 'on its place' = standing within {ZWorld.StrikeSettlePx} px of its middle; {t.movedPlace} of them on another one than their first), {t.adopted} on the spot they stopped on beside their place (they are in the least distance of (b)); standing off their place at that moment (about to be sent again): {t.standingOff}. " +
                $"Not standing: {t.offPlaceEarly} in the {t.leftEarly} groups that left because all stood, {t.offPlaceEnd - t.standingOff} still walking in the {t.leftAtEnd} groups that left at the end of their cycle (they leave from where they are)"));
            sb.AppendLine(OzV11Kit.Verdict(t.resendWindows == 0, $"nobody is sent to its place again and again: windows in which one member was sent again 10 times or more {t.resendWindows}/{t.windows}; the most in one window {t.maxResends} (the rule: {ZWorld.StrikeResendTries} times without coming nearer; then the spot it stands on, another place, or released alone)"));
            sb.AppendLine(OzV11Kit.Verdict(t.windows == 0 || t.feedersWrong == 0, $"the feeding buildings are the fort and its nearest factories, as many as the map's size gives: {t.feedersRight}/{t.windows} windows by the check's own count ({string.Join("; ", t.feederKinds.OrderByDescending(k => k.Value).Select(k => k.Value + " x " + k.Key))})"));
            sb.AppendLine(OzV11Kit.Verdict(t.worstWait <= ZWorld.StrikeCycleLimitSec + 1.01, $"no member waits at the fort longer than the cycle limit: the longest time between joining and leaving (or being released) {F(t.worstWait)} s of {t.members} members (limit {F(ZWorld.StrikeCycleLimitSec)} s)"));
            sb.AppendLine(OzV11Kit.Verdict(t.marches > 0 && t.straggle == 0, $"a group arrives together (three members in the target area within the same 10 s, or broken by fire on the way): {t.marches - t.straggle}/{t.marches} marches = {t.marches3} with three within 10 s + {t.broken} with fewer than three members left to arrive + {t.heldUp} with three arrived more than 10 s apart after a fight on the way; NEITHER (three arrived more than 10 s apart with no fight: the ways parted) {t.straggle}, each of them counted as a failure of this line"));
            sb.AppendLine(OzV11Kit.Verdict(t.routeMarches > 0 && t.offRoute == 0, $"one common route for the group: marches in which a member (not in a fight, not yet in the target area) that was on the route was more than {CorridorPx} px from it for more than {OffRouteLimitSec:0} s: {t.offRoute}/{t.routeMarches} (the longest such time {F(t.worstOffRoute)} s, the farthest {F(t.farthestOffRoute)} px){(RoundOne ? " [the first round's rules: the route is laid but nobody follows it]" : "")}"));
            sb.AppendLine(OzV11Kit.Verdict(t.routeMarches > 0 && t.ownRoad == 0, $"members that were not on the route when the march began join it before the target: {t.joiners - t.ownRoad}/{t.joiners} (still on their way from their building when the cycle ended, or a place to the side of the route; the farthest {F(t.farthestJoiner)} px off it then, the longest time until on it {F(t.worstJoin)} s); reached the target area by a road of their own {t.ownRoad} in {t.ownRoadMarches}/{t.routeMarches} marches{(RoundOne ? " [the first round's rules]" : "")}"));
            sb.AppendLine($"INFO  robots of a group taken by crocodiles (they no longer end a window): {t.eaten}; windows given up: {string.Join("; ", t.why.OrderByDescending(k => k.Value).Select(k => k.Value + " x " + k.Key))}");
            sb.AppendLine(OzV11Kit.Verdict(t.freedLate == 0, $"after a release or a giving up every former member has an order or a target within 12 s: {t.freedBusy}/{t.freed - t.freedNoTarget} (slowest {F(t.worstBusy, "0.0")} s); left with nothing because the computer had no target for it {t.freedNoTarget}; of them squads whose leader fell while they were members (the squad is followed under its next robot): {t.leaderFell - t.leaderFellLate}/{t.leaderFell}"));
            sb.AppendLine(OzV11Kit.Verdict(t.targetChecked > 0 && t.targetBiggest == t.targetChecked, $"the group marches at the biggest gathering: {t.targetBiggest}/{t.targetChecked} (the check's own count of the other side's units within {ZWorld.StrikeGatherPx} px)"));
            if (extra != null) foreach (var l in extra) sb.AppendLine(l);
            bool pass = !sb.ToString().Split('\n').Any(l => l.StartsWith("FAIL"));
            sb.AppendLine((pass ? "PASS " : "FAIL ") + sb.ToString().Split('\n').Count(l => l.StartsWith("PASS  ")) + "/" + sb.ToString().Split('\n').Count(l => l.StartsWith("PASS  ") || l.StartsWith("FAIL  ")) + " checks");
            return sb.ToString();
        }

        /// <summary>
        /// rule on, and the same match with the switch off. Counted numbers only.</summary>
        public static string Probe(string settings, Func<string, string> map)
        {
            var sb = new StringBuilder();
            var on = Match(settings, map(Levels[0]), Levels[0], 1, Sides.PlayerHolds, true, ZBotDifficulty.Normal, 720);
            var off = Match(settings, map(Levels[0]), Levels[0], 1, Sides.PlayerHolds, false, ZBotDifficulty.Normal, 720);
            var first = on.groups.FirstOrDefault(); var g = on.groups.FirstOrDefault(x => x.formed >= 0);
            sb.AppendLine(OzV11Kit.Verdict(on.error == null && first != null && first.opened >= ZWorld.StrikeStartSec, first == null ? "rule on: no window opened" + (on.error != null ? " (" + on.error.Split('\n')[0] + ")" : "")
                : $"rule on: the first window opens at {Clock(first.opened)} (never before {Clock(ZWorld.StrikeStartSec)}), fed by {first.feeders} buildings"));
            sb.AppendLine(OzV11Kit.Verdict(g != null && g.formed - g.opened <= ZWorld.StrikeCycleLimitSec + 1.01 && g.standingAll >= 2 && g.overlap == 0 && g.members.All(x => x.worstBad <= BadStandLimit) && first.feedersRight, g == null ? "no group formed"
                : $"the group forms {F(g.formed - g.opened)} s after its window opened (limit {F(ZWorld.StrikeCycleLimitSec)} s) and leaves with {g.marchers} members; the {g.standingAll} standing ones at least {F(g.minApartAll)} px apart, in the gate corridor {g.inCorridor}, in an exit lane {g.inLane}, too near the gate wall {g.tooNear} (the longest stay of a member on such a spot while it formed {F(g.members.Max(x => x.worstBad), "0.0")} s); fed by the fort and its nearest factories: {(first.feedersRight ? "yes" : "NO")}"));
            sb.AppendLine(OzV11Kit.Verdict(g != null && g.atTarget >= g.biggest && (g.arrivedIn10 >= 3 || g.marchers - g.lostOnMarch < 3), g == null ? "no march"
                : $"it marches at the biggest gathering ({g.atTarget} units of the other side within {ZWorld.StrikeGatherPx} px; the most anywhere {g.biggest}); {g.arrivedAll} of {g.marchers} members reach the target area, the tightest three within {(g.thirdAfterFirst >= 0 ? F(g.thirdAfterFirst) + " s" : "-")}; destroyed on the way {g.lostOnMarch}; released: {g.end ?? "not before 12:00"}"));
            sb.AppendLine(OzV11Kit.Verdict(off.groups.Count == 0 && off.fingerprint != on.fingerprint && off.error == null, $"switch off, the same match: windows {off.groups.Count}; its fingerprint {(off.fingerprint != on.fingerprint ? "differs from" : "EQUALS")} the match with the rule on"));
            return sb.ToString();
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Count Enemy Strike Groups (f39)")]
        static void Menu() => OzV11Kit.RunMenu("f39 enemy strike groups (short run: 2 matches per level and row and the rows of the general rule, 28 matches of 16 min each; several minutes)", () => Run(OzV11Kit.SettingsText, OzV11Kit.MapText, 2, 16));
#endif
    }
}
