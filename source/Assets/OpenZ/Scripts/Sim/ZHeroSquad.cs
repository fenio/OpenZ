using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// OpenZ deviation DV108 "hero squads": a custom squad kind that a level's data can place (Zod has none). The first kind is
    /// ALAN &amp; BRAD: exactly two robots of the grunt type, the bigger one (Brad) the squad leader, so he walks in front and the
    /// smaller one (Alan) in the V slot behind him (DV24 gives the only minion of a two-robot squad the slot one row behind).
    /// Every rule of a normal grunt squad holds for them (they ARE grunts: every "grunt" rule of the code applies), except:
    ///  * hit points: <see cref="ZHeroKind.HealthMul"/> x a grunt's (3 x);
    ///  * the strong shot: a bullet that hits removes <see cref="ZHeroKind.DamageMul"/> x what a grunt's bullet removes from that
    ///    target (the game's strong-grunt rifle of the crane and hatch drivers, ZWorld.CrewRifleMul = 3, 33 HP on a hull; on a
    ///    robot 3 x the rifle-pair value of DV78), the hit roll, range, snipe rule and grenades stay a grunt's;
    ///  * a lower fire rate: the pause between two shots is <see cref="ZHeroKind.CycleMul"/> x a grunt's, chosen by counted fights
    ///    (Editor/OzV11HeroSquadCount.cs) so that the squad of two is worth about two grunt squads and never more than three;
    ///  * their name and pictures (view only: View/OzHeroSquadView.cs).
    /// Vehicles, guns and troop carriers: they board exactly as grunts do (by order and by themselves, the same rules, the same crew
    /// lists: inside they ARE a grunt crew, the vehicle fires as with grunts aboard). Who of the squad sits in which place of a crew list
    /// (Drivers, Passengers, a repair station's stored crew) is carried beside the list (<see cref="ZHeroSeat"/>), so whoever gets out
    /// (EXIT of a troop carrier or a gun, a sniped troop carrier's passengers, Zod's whole-crew EXIT) comes out as Alan or Brad again,
    /// with the hit points the list kept, 3 x a grunt's maximum, the strong shot and the slower trigger; when both get out together
    /// Brad leads. A vehicle destroyed or a driver sniped with them inside: they are lost, as grunts are.
    /// Losing them is a normal loss of a squad: no rule of the match looks at them (no defeat, no event of its own).
    /// Placement: an EXTRA squad. The level data's own list "heroes" (beside "objects"; Zod's maps have none) holds entries
    /// {"kind": "robot:grunt", "owner", "x", "y", "squad": "&lt;kind id&gt;", "at": [x0, y0, x1, y1, ...] start tiles per member,
    /// leader first, "start": "lounger", "camp": [x, y]}; while <see cref="ZWorld.HeroSquadDeviation"/> is on each entry is created
    /// as that hero squad ON TOP of the map's own units (nothing of the map is replaced or moved), right after the level's own
    /// objects (every object of the map keeps its id). Switch off ('Zod exact', every headless world by default): the list is not
    /// read at all. "start" (the two begin the level lying in the sun-lounger antic) and "camp" (a burning campfire beside them)
    /// are read by the view only.
    /// No random draw, no clock.
    /// </summary>
    public sealed class ZHeroKind
    {
        /// <summary>The id the level data names ("squad": id).</summary>
        public string Id;
        /// <summary>The name the unit panel shows.</summary>
        public string Name;
        /// <summary>The robot type whose rules they follow (its row of the rule table, its weapon, its grenades).</summary>
        public string BaseType = "grunt";
        /// <summary>The members, leader first (the leader walks in front).</summary>
        public string[] Members;
        public int HealthMul = 3, DamageMul = 3;
        /// <summary>The pause between two shots as a multiple of the base type's attack_speed (1 = a grunt's).</summary>
        public double CycleMul = 1.0;
    }

    public static class ZHeroSquads
    {
        /// <summary>The ALAN &amp; BRAD squad (level data: "squad": "alan_brad").</summary>
        public const string AlanBrad = "alan_brad";

        /// <summary>The kinds a level's data can place (id -> kind).</summary>
        public static readonly Dictionary<string, ZHeroKind> Kinds = new Dictionary<string, ZHeroKind>
        {
            [AlanBrad] = new ZHeroKind
            {
                Id = AlanBrad, Name = "ALAN & BRAD", BaseType = "grunt", Members = new[] { "brad", "alan" },
                HealthMul = 3, DamageMul = ZWorld.CrewRifleMul,
                CycleMul = 1.75,                                                                     // counted fights (Editor/OzV11HeroSquadCount.cs, 100 a row): 0.875 s between shots; x1 beat two squads 96 times of 100 and three 46, x1.75 beats two 40 times (two squads beat two 41 times)
            },
        };

        public static ZHeroKind Get(string id) => string.IsNullOrEmpty(id) ? null : Kinds.TryGetValue(id, out var k) ? k : null;
    }

    public partial class ZObject
    {
        /// <summary>DV108: the hero squad kind this robot belongs to (null = a normal robot).</summary>
        public ZHeroKind Hero { get; internal set; }
        /// <summary>DV108: index of this robot in <see cref="ZHeroKind.Members"/> (0 = the leader of the kind), -1 = none.</summary>
        public int HeroMember { get; internal set; } = -1;
        /// <summary>DV108: the level data entry the squad was placed from (null when made by code).</summary>
        public ZMapData.Obj HeroFrom { get; internal set; }
        public bool IsHero => Hero != null;
        /// <summary>DV108: "brad", "alan", ... (null for a normal robot).</summary>
        public string HeroMemberName => Hero != null && HeroMember >= 0 && HeroMember < Hero.Members.Length ? Hero.Members[HeroMember] : null;

        /// <summary>DV108: the HP a hit of this robot removes, from what a robot of its base type would remove (the strong shot).</summary>
        int HeroHit(int hit) => Hero == null || hit <= 0 ? hit : ZHeroBoost.Damage(hit * Math.Max(1, Hero.DamageMul), Hero);   // DV112: x 1.20 on top (Sim/ZHeroSquad.Kh4.cs)
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV108 switch: the level data's "heroes" entries are placed as extra hero squads. The game sets it at every start
        /// (off in 'Zod exact'); off by default so every headless world is the map alone.</summary>
        public static bool HeroSquadDeviation = false;
        /// <summary>DV108 statistics (checks): hero squads created in this world.</summary>
        public int HeroSquadsMade { get; private set; }

        /// <summary>ZWorld.Create hook, right after the level's own objects (the shuttle is the last of them): every "heroes" entry of
        /// the level data becomes an extra hero squad of its owner. Nothing of the map is replaced. Switch off: nothing happens.</summary>
        void AddHeroSquads()
        {
            if (!HeroSquadDeviation || Map == null || Map.heroes == null) return;
            foreach (var o in Map.heroes)
            {
                if (o == null || ZHeroSquads.Get(o.squad) == null) continue;
                CreateHeroSquad(o.squad, ParseOwner(o.owner), o.at != null && o.at.Length >= 2 ? o.at : new[] { o.x, o.y }, o,
                                o.health_percent > 0 ? o.health_percent : 100);
            }
        }

        /// <summary>Creates a hero squad of kind <paramref name="kindId"/> for <paramref name="owner"/>: member i at tile
        /// (tiles[2i], tiles[2i+1]) (the last pair given for the rest), leader first. Returns the leader (null: unknown kind).</summary>
        public ZObject CreateHeroSquad(string kindId, Team owner, int[] tiles, ZMapData.Obj from = null, int healthPercent = 100)
        {
            var k = ZHeroSquads.Get(kindId);
            if (k == null || S.Unit(k.BaseType) == null || tiles == null || tiles.Length < 2) return null;
            ZObject leader = null;
            for (int i = 0; i < k.Members.Length; i++)
            {
                int j = Math.Min(i, tiles.Length / 2 - 1);
                var m = CreateObject("robot", k.BaseType, tiles[2 * j] * 16, tiles[2 * j + 1] * 16, owner, 0, 100, null);
                MakeHero(m, k, i, from, healthPercent); HeroStartKit(m);                                   // KH (item 187): a member's own grenades at the start (Sim/ZHeroSquad.Kh.cs)
                if (leader == null) leader = m;
                else { m.MemberIndex = i; m.Leader = leader; leader.Minions.Add(m); }
            }
            HeroSquadsMade++;
            return leader;
        }

        /// <summary>The kind's own numbers on a freshly created robot of its base type.</summary>
        static void MakeHero(ZObject o, ZHeroKind k, int member, ZMapData.Obj from, int healthPercent)
        {
            o.Hero = k; o.HeroMember = member; o.HeroFrom = from;
            if (o.U != null)
            {
                o.MaxHealth = ZHeroBoost.Health(o.U.MaxHealth * Math.Max(1, k.HealthMul), k);             // DV112: x 1.20 on top (Sim/ZHeroSquad.Kh4.cs)
                o.AttackSpeed = o.U.attack_speed * Math.Max(0.1, k.CycleMul);
                o.AttackRadius = ZHeroBoost.Range(ZHeroRange.Radius(o.U.attack_radius, k, member), k);    // KH2 (item 199): a member's own range (Alan x 1.10, Sim/ZHeroSquad.Kh2.cs); DV112: x 1.15 on top (Sim/ZHeroSquad.Kh4.cs)
            }
            o.Health = o.MaxHealth * Math.Max(0, Math.Min(100, healthPercent)) / 100;
        }

        // ------------------------------------------------------------------ crews (vehicles, guns, troop carriers, the repair station)
        /// <summary>DV108 statistics (checks): hero robots that got into a crew list / came out of one as themselves.</summary>
        public int HeroesBoarded { get; private set; }
        public int HeroesOut { get; private set; }

        /// <summary>The robots that may get in with the boarding robot (RobotEnterObject's first line, before anything changes).</summary>
        internal sealed class HeroBoard { public ZObject[] all; public bool[] alive; public int d0; }

        /// <summary>RobotEnterObject hook (first line): who of a hero squad may get in. Null (nothing to carry) when no hero is in the squad
        /// and the vehicle carries no seats: plain crews never allocate anything.</summary>
        internal HeroBoard HeroBoarding(ZObject r, ZObject v)
        {
            if (r == null || v == null) return null;
            bool any = r.IsHero || v.HeroDriverSeats != null || v.HeroPassengerSeats != null;
            if (!any) foreach (var m in r.Minions) if (m != null && m.IsHero) { any = true; break; }
            if (!any) return null;
            var b = new HeroBoard { all = new ZObject[r.Minions.Count + 1], alive = new bool[r.Minions.Count + 1], d0 = v.Drivers.Count };
            b.all[0] = r; b.alive[0] = r.KillAt < 0;
            for (int i = 0; i < r.Minions.Count; i++) { b.all[i + 1] = r.Minions[i]; b.alive[i + 1] = r.Minions[i] != null && r.Minions[i].KillAt < 0; }
            return b;
        }

        /// <summary>RobotEnterObject hook (after the robots got in): the seats beside the lists, in the lists' own order.
        /// passengers = DV15's boarding of an own troop carrier (Passengers = the robot + its live minions, ZApcCrew.cs); else Zod's
        /// path: the robot (+ every minion for a troop carrier) was added to Drivers after the d0 robots already in (a neutral vehicle:
        /// none), and DV15's split then moved Drivers[1..] to Passengers.</summary>
        internal void HeroSeatsIn(ZObject v, HeroBoard b, bool passengers)
        {
            if (b == null || v == null) return;
            if (passengers)
            {
                var ps = new List<ZHeroSeat> { ZHeroSeat.Of(b.all[0]) };
                for (int i = 1; i < b.all.Length; i++) if (b.alive[i]) ps.Add(ZHeroSeat.Of(b.all[i]));
                v.HeroPassengerSeats = ZHeroSeat.Fit(ps, v.Passengers.Count);
                CountIn(ps);
                return;
            }
            var all = ZHeroSeat.Fit(v.HeroDriverSeats, b.d0);
            var added = new List<ZHeroSeat> { ZHeroSeat.Of(b.all[0]) };
            if (v.IsApc) for (int i = 1; i < b.all.Length; i++) if (b.all[i] != null) added.Add(ZHeroSeat.Of(b.all[i]));
            all.AddRange(added);
            int nd = v.Drivers.Count;
            v.HeroDriverSeats = ZHeroSeat.Fit(all.GetRange(0, Math.Min(nd, all.Count)), nd);
            if (all.Count > nd) v.HeroPassengerSeats = ZHeroSeat.Fit(all.GetRange(nd, all.Count - nd), v.Passengers.Count);
            CountIn(added);
        }

        void CountIn(List<ZHeroSeat> seats) { foreach (var s in seats) if (s != null) HeroesBoarded++; }

        /// <summary>Eject / PassengersOut hook (the new squad is made, its hit points not yet set): the hero members come out as
        /// themselves. hp is the copy of the list those two methods read the hit points from; it and the seats are put in the kind's
        /// member order (Brad first: he leads), every other robot after them, so the health loop that follows gives each one its own
        /// stored hit points (now up to 3 x a grunt's).</summary>
        internal void HeroCrewOut(ZObject v, List<ZObject> squad, List<int> hp, bool passengers)
        {
            if (v == null) return;
            var held = passengers ? v.HeroPassengerSeats : v.HeroDriverSeats;
            if (passengers) v.HeroPassengerSeats = null; else v.HeroDriverSeats = null;                // the list is emptied right after
            if (held == null || squad == null || hp == null) return;
            var seats = ZHeroSeat.Fit(held, hp.Count);
            if (!seats.Exists(s => s != null)) return;
            for (int i = 1; i < seats.Count; i++)                                                         // stable insertion sort: heroes by member, then the rest
                for (int j = i; j > 0 && ZHeroSeat.Rank(seats[j]) < ZHeroSeat.Rank(seats[j - 1]); j--)
                {
                    var s = seats[j]; seats[j] = seats[j - 1]; seats[j - 1] = s;
                    int h = hp[j]; hp[j] = hp[j - 1]; hp[j - 1] = h;
                }
            for (int i = 0; i < seats.Count && i < squad.Count; i++)
                if (seats[i] != null && squad[i] != null) { MakeHero(squad[i], seats[i].Kind, seats[i].Member, seats[i].From, 100); HeroGrenadesBack(squad[i], seats[i]); HeroesOut++; }   // KH: his own grenades out with him
        }

        /// <summary>A hold of the switch for checks and reports (off while held, put back on Dispose).</summary>
        public sealed class HeroSquadHold : IDisposable
        {
            readonly bool was;
            internal HeroSquadHold() { was = HeroSquadDeviation; HeroSquadDeviation = false; }
            public void Dispose() { HeroSquadDeviation = was; }
        }
        public static HeroSquadHold HeroSquadsOff() => new HeroSquadHold();
    }

    /// <summary>DV108: who of a hero squad sits in one place of a crew list (null in the list = a plain robot).</summary>
    public sealed class ZHeroSeat
    {
        public ZHeroKind Kind; public int Member; public ZMapData.Obj From;
        /// <summary>KH: the grenades he carries as his own (Alan's), given back to him when he gets out.</summary>
        public int Grenades;

        public static ZHeroSeat Of(ZObject o) => o == null || o.Hero == null ? null : new ZHeroSeat { Kind = o.Hero, Member = o.HeroMember, From = o.HeroFrom, Grenades = ZHeroGrenades.Carried(o) };
        internal static int Rank(ZHeroSeat s) => s == null ? int.MaxValue : s.Member;

        /// <summary>A copy of the seats (null = all plain) cut or padded with plain places to n: the seats always line up with the list
        /// from its first place (a list that was cleared, a sniped driver, leaves no stale seat).</summary>
        public static List<ZHeroSeat> Fit(List<ZHeroSeat> seats, int n)
        {
            var r = new List<ZHeroSeat>(Math.Max(0, n));
            for (int i = 0; i < n; i++) r.Add(seats != null && i < seats.Count ? seats[i] : null);
            return r;
        }
    }

    public partial class ZObject
    {
        /// <summary>DV108: the hero seats beside Drivers / Passengers (null = every place plain; plain crews never allocate them).</summary>
        internal List<ZHeroSeat> HeroDriverSeats, HeroPassengerSeats;
        /// <summary>DV108: the same for the crew a repair station holds while it swaps a vehicle (Zod's 5 s swap).</summary>
        internal List<ZHeroSeat> HeroRepairDriverSeats, HeroRepairPassengerSeats;

        /// <summary>DV108: the hero kind of the drivers (false) or the passengers (true) aboard now, null = plain robots. View: the
        /// unit panel's crew line.</summary>
        public ZHeroKind HeroCrewKind(bool passengers)
        {
            var s = passengers ? HeroPassengerSeats : HeroDriverSeats; int n = passengers ? Passengers.Count : Drivers.Count;
            if (s == null) return null;
            for (int i = 0; i < n && i < s.Count; i++) if (s[i] != null) return s[i].Kind;
            return null;
        }

        /// <summary>DV108: hero robots aboard now (drivers + passengers).</summary>
        public int HeroesAboard
        {
            get
            {
                int c = 0;
                if (HeroDriverSeats != null) for (int i = 0; i < Drivers.Count && i < HeroDriverSeats.Count; i++) if (HeroDriverSeats[i] != null) c++;
                if (HeroPassengerSeats != null) for (int i = 0; i < Passengers.Count && i < HeroPassengerSeats.Count; i++) if (HeroPassengerSeats[i] != null) c++;
                return c;
            }
        }

        /// <summary>SetRepairUnit hook (on the station, after the crew lists were stored): the seats ride along.</summary>
        internal void HeroSeatsStore(ZObject unit)
        {
            HeroRepairDriverSeats = unit == null || unit.HeroDriverSeats == null ? null : ZHeroSeat.Fit(unit.HeroDriverSeats, RepairDrivers.Count);
            HeroRepairPassengerSeats = unit == null || unit.HeroPassengerSeats == null ? null : ZHeroSeat.Fit(unit.HeroPassengerSeats, unit.Passengers.Count);
        }

        /// <summary>BuildingRepairUnit hook (on the station, after the fresh vehicle got its crew back): and their seats.</summary>
        internal void HeroSeatsRestore(ZObject u)
        {
            if (u != null && HeroRepairDriverSeats != null && RepairDrivers.Count > 0) u.HeroDriverSeats = ZHeroSeat.Fit(HeroRepairDriverSeats, u.Drivers.Count);
            if (u != null && HeroRepairPassengerSeats != null) u.HeroPassengerSeats = ZHeroSeat.Fit(HeroRepairPassengerSeats, u.Passengers.Count);
            HeroRepairDriverSeats = HeroRepairPassengerSeats = null;
        }
    }
}
