using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ
{
    /// <summary>
    /// DV108 hero squads (Sim/ZHeroSquad.cs), the pair rule of the view: are the members of a hero kind TOGETHER (all of them in one
    /// squad on the map) or apart (one died; one sits in a vehicle, a gun or a troop carrier while the other stands outside; the DV98
    /// split made one a squad of his own)?
    ///  * Together: the kind's name ("ALAN &amp; BRAD") and its picture of both; either of them answers a selection or an order.
    ///  * Apart: each robot shows his OWN name ("ALAN" / "BRAD") and his own picture, and only he speaks (his own lines).
    ///  * Together again: when they are one squad again (both out of one troop carrier: Brad leads, Alan behind him). The game has no
    ///    rule that joins two squads into one, so two members who left the squad one by one stay apart.
    ///  * A crew list (a vehicle's or gun's drivers, a troop carrier's passengers): every member of the kind in that list = the kind's
    ///    name, one member = his name.
    /// Plain code (no engine calls, no random draw): the view uses it and the headless checks count it (Editor/OzV11HeroSquadCount.cs).
    /// </summary>
    public static class OzHeroPair
    {
        static bool Live(ZObject o) => o != null && !o.Destroyed && o.KillAt < 0;

        /// <summary>The live robots of u's squad that are members of u's hero kind, u first (empty when u is not a live hero).</summary>
        public static List<ZObject> Mates(ZObject u)
        {
            var l = new List<ZObject>();
            if (u == null || !u.IsHero || !Live(u)) return l;
            l.Add(u);
            var lead = u.Leader ?? u;
            if (lead != u && lead.IsHero && lead.Hero == u.Hero && Live(lead)) l.Add(lead);
            foreach (var m in lead.Minions)
                if (m != null && m != u && m.IsHero && m.Hero == u.Hero && Live(m) && !l.Contains(m)) l.Add(m);
            return l;
        }

        /// <summary>How many different members of u's kind are in u's squad (u included; 0 = not a live hero).</summary>
        public static int MembersHere(ZObject u)
        {
            var seen = new HashSet<int>();
            foreach (var m in Mates(u)) seen.Add(m.HeroMember);
            return seen.Count;
        }

        /// <summary>Every member of u's kind is in u's squad on the map.</summary>
        public static bool Together(ZObject u) => u != null && u.IsHero && MembersHere(u) >= u.Hero.Members.Length;

        /// <summary>The name a member shows on his own ("ALAN", "BRAD").</summary>
        public static string MemberName(ZHeroKind k, int member) =>
            k != null && k.Members != null && member >= 0 && member < k.Members.Length ? k.Members[member].ToUpperInvariant() : null;

        /// <summary>u's name: the kind's while together, else his own; null for a robot that is not a hero.</summary>
        public static string NameOf(ZObject u)
        {
            if (u == null || !u.IsHero) return null;
            return Together(u) ? u.Hero.Name : Joined(u.Hero, Members(Mates(u)), u.HeroMember);
        }

        /// <summary>u's picture key: the kind's ("alanbrad") while together, else the member's ("alan", "brad"); null = not a hero.</summary>
        public static string PictureKey(ZObject u)
        {
            if (u == null || !u.IsHero) return null;
            return Together(u) || u.HeroMemberName == null ? KindKey(u.Hero) : u.HeroMemberName;
        }

        /// <summary>The picture key of the kind's picture of all its members ("alanbrad").</summary>
        public static string KindKey(ZHeroKind k) => k == null ? null : k.Id.Replace("_", "");

        /// <summary>The crew line's name for a vehicle's drivers (false) or passengers (true): the kind's name when every member of the
        /// kind sits in that list, else the members there ("BRAD"); null = no hero in that list.</summary>
        public static string CrewName(ZObject v, bool passengers)
        {
            if (v == null) return null;
            var seats = passengers ? v.HeroPassengerSeats : v.HeroDriverSeats; int n = passengers ? v.Passengers.Count : v.Drivers.Count;
            if (seats == null) return null;
            ZHeroKind k = null; var here = new List<int>();
            for (int i = 0; i < n && i < seats.Count; i++)
            {
                var s = seats[i]; if (s == null || s.Kind == null) continue;
                if (k == null) k = s.Kind;
                if (s.Kind == k && !here.Contains(s.Member)) here.Add(s.Member);
            }
            if (k == null) return null;
            return here.Count >= k.Members.Length ? k.Name : Joined(k, here, -1);
        }

        static List<int> Members(List<ZObject> l) { var r = new List<int>(); foreach (var o in l) if (!r.Contains(o.HeroMember)) r.Add(o.HeroMember); return r; }

        /// <summary>The members' own names in the kind's order, joined with " &amp; " (one member: his name).</summary>
        static string Joined(ZHeroKind k, List<int> members, int fallback)
        {
            var names = new List<string>();
            for (int i = 0; i < k.Members.Length; i++) if (members.Contains(i)) names.Add(MemberName(k, i));
            if (names.Count == 0) { var f = MemberName(k, fallback); return f ?? k.Name; }
            return string.Join(" & ", names);
        }
    }
}
