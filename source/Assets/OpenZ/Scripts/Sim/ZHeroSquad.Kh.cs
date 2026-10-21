using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// DV108 hero squads, two more rules of their own (Sim/ZHeroSquad.cs):
    /// <list type="bullet">
    /// <item><b>Boarding only on the player's order.</b> A hero (and a squad with a hero in it) never gets into a vehicle, a gun or a
    /// troop carrier by itself: the idle grab of an empty vehicle or gun (CheckPassiveEngage, DV38 / DV55), the grab on the way of a
    /// plain move (DV84 GrabOnTheWay), the closer-grabber test (a hero never counts as the closer unit, so a grunt nearby still goes)
    /// and the CPU's car park sends (DV37) all skip them, through the same <see cref="ZObject.NoAutoEnter"/> test the Electrobot uses
    /// (D7). An order to enter (the player's right-click on the vehicle, gun or carrier) boards them exactly as before. Flags and
    /// grenade crates: as before. Switch <see cref="ZWorld.HeroOrderedBoardingDeviation"/>.</item>
    /// <item><b>Alan's grenades.</b> Alan starts with <see cref="ZHeroGrenades.Start"/> grenades of his own (Brad none). He throws them by
    /// the sniper's grenade rule (DV40: every robot type except the grunt uses its gun against robots and throws grenades only at
    /// vehicles, guns and buildings; with that rule off, as in 'Zod exact', at anything, as a sniper does then): the throw itself is
    /// the game's own (ProcessAttackDamage: damage, radius, scatter, the grenade cooldown). His own grenades are spent first, then the
    /// squad's pool (a crate Brad picked up). They are his: a hero's own grenades ride with him through a crew list
    /// (<see cref="ZHeroSeat.Grenades"/>) and come out with him, like his hit points. The unit panel shows the squad's count
    /// (<see cref="ZObject.SquadGrenades"/>: the leader's pool + the hero members' own). When the leader dies or leaves the squad
    /// (ZWorld.RemoveObjectFromGroup) his pool passes to the new leader ON TOP of a hero's own (<see cref="ZHeroGrenades.Merge"/>):
    /// Brad dies after picking up a crate, Alan keeps his own and gets the pool.</item>
    /// </list>
    /// No random draw of its own, no clock.
    /// </summary>
    public static class ZHeroGrenades
    {
        /// <summary>Grenades each member of a kind starts with (kind id -> per member, leader first).</summary>
        public static readonly Dictionary<string, int[]> Start = new Dictionary<string, int[]>
        {
            [ZHeroSquads.AlanBrad] = new[] { 0, 5 },                                                   // Brad none, Alan 5
        };

        /// <summary>The robot type whose grenade rule (DV40) a member's throws follow (null = his own type's).</summary>
        public static readonly Dictionary<string, string[]> RuleAs = new Dictionary<string, string[]>
        {
            [ZHeroSquads.AlanBrad] = new[] { null, "sniper" },                                         // Alan: the sniper's rule
        };

        public static int StartOf(ZHeroKind k, int member) =>
            k != null && Start.TryGetValue(k.Id, out var a) && member >= 0 && member < a.Length ? Math.Max(0, a[member]) : 0;

        public static string RuleOf(ZHeroKind k, int member) =>
            k != null && RuleAs.TryGetValue(k.Id, out var a) && member >= 0 && member < a.Length ? a[member] : null;

        /// <summary>ZWorld.RemoveObjectFromGroup hook: the new leader's grenades when the old leader's pool passes to him. A hero keeps
        /// his own on top of the pool; any other robot gets the pool as before (the rules' hand-over).</summary>
        public static int Merge(ZObject newLeader, int pool) =>
            newLeader != null && newLeader.IsHero ? Math.Max(0, newLeader.Grenades) + Math.Max(0, pool) : pool;

        /// <summary>The grenades a hero carries as his own through a crew list (only a member that starts with his own).</summary>
        internal static int Carried(ZObject o) => o != null && StartOf(o.Hero, o.HeroMember) > 0 ? Math.Max(0, o.Grenades) : 0;
    }

    public partial class ZObject
    {
        /// <summary>KH: this robot (a hero, or the leader of a squad with a hero in it) boards only on the player's order.</summary>
        public bool HeroNoAutoBoard
        {
            get
            {
                if (!ZWorld.HeroOrderedBoardingDeviation || Kind != ZKind.Robot) return false;
                if (IsHero) return true;
                foreach (var m in Minions) if (m != null && m.IsHero) return true;
                return false;
            }
        }

        /// <summary>KH: the robot type whose grenade rule (DV40) this robot's throws follow: a hero member's own (Alan: the sniper's),
        /// else its type.</summary>
        public string GrenadeRuleType => (Hero != null ? ZHeroGrenades.RuleOf(Hero, HeroMember) : null) ?? Type;

        /// <summary>KH: the grenades the unit panel shows for this squad: the leader's pool + the hero members' own.</summary>
        public int SquadGrenades
        {
            get
            {
                int n = Math.Max(0, Grenades);
                foreach (var m in Minions) if (m != null && m.IsHero && !m.Destroyed && m.KillAt < 0) n += Math.Max(0, m.Grenades);
                return n;
            }
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>KH switch: heroes board vehicles, guns and troop carriers only on the player's order. On by default (it acts only on
        /// hero squads, which exist only while <see cref="HeroSquadDeviation"/> is on).</summary>
        public static bool HeroOrderedBoardingDeviation = true;
        /// <summary>KH statistics (checks): grenades handed out at a hero squad's start, and given back to a hero getting out.</summary>
        public int HeroStartGrenades { get; private set; }
        public int HeroGrenadesCarriedOut { get; private set; }

        /// <summary>CreateHeroSquad hook, right after MakeHero (the squad's start only, never on getting out): the member's own grenades.</summary>
        internal void HeroStartKit(ZObject m)
        {
            int n = m != null ? ZHeroGrenades.StartOf(m.Hero, m.HeroMember) : 0;
            if (n <= 0) return;
            m.Grenades = n; HeroStartGrenades += n;
        }

        /// <summary>HeroCrewOut hook, right after MakeHero: the hero's own grenades back from his seat.</summary>
        internal void HeroGrenadesBack(ZObject m, ZHeroSeat s)
        {
            if (m == null || s == null || s.Grenades <= 0) return;
            m.Grenades = s.Grenades; HeroGrenadesCarriedOut += s.Grenades;
        }
    }
}
