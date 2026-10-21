using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// Zod parity first: the Zod Engine has NO veterancy, experience or rank. zobject.h / zobject.cpp keep no kill count, the
    /// server's death relay (zserver.cpp:1731-1907) only credits the attacker for the bot's wave logic, ZUnitRating (zbot.cpp)
    /// is a static per-type table the CPU uses to pick targets, and the only "level" is a building's production level
    /// (zbuilding.cpp). The 1996 Z (Bitmap Brothers) had none either as far as known. So everything here is a listed deviation,
    /// OFF in 'Zod exact' (PlayerPrefs oz_zod_exact = 1), by its own pref (oz_veterans = 0) and in every parity row.
    ///
    /// Rule (deterministic, sim-side, no random draw of its own, no frame time; the view only reads Veteran / Kills and the
    /// Promoted event, so a lockstep peer computes the same promotions from the same ticks):
    ///  * A kill = an enemy robot, vehicle or cannon (owned, not neutral) destroyed by a live, owned unit of the other team
    ///    (the attacker id Zod already relays with every death: hitscan attacker, missile / grenade owner), or a vehicle /
    ///    cannon driver sniped (the Sniped relay's sniper). Buildings, map items, debris missiles, bridge collapses, team
    ///    elimination and kills by a unit that is itself already dead or gone do not count.
    ///  * Credit goes to the CREW: a squad counts as one crew (the kill of any member is the squad's kill; every member carries
    ///    the squad count, so a new leader inherits it when the old one dies or climbs into a vehicle, exactly like Zod passes
    ///    the grenade pool on, zserver.cpp:2065-2190); a vehicle or cannon counts for the robots driving it. Why per squad: in
    ///    Zod the squad is the unit (one selection, one order list cloned to the minions, one target), per-robot counters would
    ///    split the same fights over 2-4 robots and almost never promote anyone, and the star would flicker per robot.
    /// <see cref="VeteranKills"/>
    ///  * The crew's veterancy travels with it: a robot entering a vehicle / cannon hands the squad's kills and rank to it
    ///    (zserver.cpp:1375-1421 RobotEnterObject), an ejected crew (APC / cannon EXIT) takes them back to the new squad, a
    ///    vehicle through the repair station keeps its crew's rank (brepair.cpp SetRepairUnit -> the fresh vehicle), a sniped
    ///    driver takes his rank to the grave (the empty vehicle is a fresh capture).
    ///  * Perks (veteran crews only, all small and deterministic):
    ///      - buttoned up: a vehicle / cannon with a veteran crew is sniped with <see cref="VeteranSnipeFactor"/> of the sniper's
    ///        snipe chance (zobject.cpp:2187-2199 roll kept, same number of draws; the tank lid still opens and closes on Zod's
    ///        rolls, the view simply never shows a veteran commander in the hatch: BoardingView guard);
    ///      - a little more accurate: Zod's hitscan hit roll (zobject.cpp:2187 "rand()%10000/10000 > damage_chance" = miss)
    ///        uses damage_chance x <see cref="VeteranHitFactor"/> (capped at 1), and a missile's scatter (zobject.cpp:2155,
    ///        +-16 px) is 3/4 as wide (same draw, scaled); grenades keep Zod's throw scatter;
    ///      - quicker aim: view only (UnitAim slews turrets / torsos faster for veterans; the sim has no aim time in Zod, and
    ///        the drawn shot is only held for the view, so sim timing never changes).
    /// </summary>
    public partial class ZObject
    {
        /// <summary>DV12: kills credited to this unit's crew (squad members all carry the squad's count).</summary>
        public int Kills;
        /// <summary>DV12: veteran crew (squad, or the robots in this vehicle / cannon). Read by the view (star, buttoned-up hatch).</summary>
        public bool Veteran;
        /// <summary>DV12: sim time of the promotion (-1 = never); travels with the crew. View: the star's pop-in.</summary>
        public double PromotedAt = -1;
        /// <summary>DV12: the crew of the vehicle in this repair station (brepair.cpp stores the unit, the fresh one keeps it).</summary>
        internal Crew RepairCrew;

        /// <summary>The veteran perks apply (the rank is kept even while the deviation is off, it only stops mattering).</summary>
        public bool VeteranActive => Veteran && ZWorld.VeteranDeviation;

        /// <summary>Zod's hit threshold (miss when the roll is above it); DV12 veterans x VeteranHitFactor, capped at 1.</summary>
        internal double HitChance() => VeteranActive ? Math.Min(1.0, DamageChance * ZWorld.VeteranHitFactor) : DamageChance;
        /// <summary>This attacker's snipe threshold against target: DC2 open seats (ZWorld.SnipeChanceOn), then DV12 veteran crews
        /// stay buttoned up (x VeteranSnipeFactor).</summary>
        internal double SnipeChanceAgainst(ZObject target) { double sc = ZWorld.SnipeChanceOn(this, target); return target != null && target.VeteranActive ? sc * ZWorld.VeteranSnipeFactor : sc; }
        /// <summary>A missile's scatter offset (one Zod draw, rand()%32 - 16): DV12 veterans ScatterNum / ScatterDen of it.</summary>
        internal int Scatter(int offset) => VeteranActive ? offset * ZWorld.VeteranScatterNum / ZWorld.VeteranScatterDen : offset;

        internal struct Crew { public int kills; public bool veteran; public double at; }
        internal Crew CrewOf() => new Crew { kills = Kills, veteran = Veteran, at = PromotedAt };
        internal void SetCrew(Crew c) { Kills = c.kills; Veteran = c.veteran; PromotedAt = c.at; }
        internal void TakeCrewFrom(ZObject src) { if (src != null) SetCrew(src.CrewOf()); }
        internal void ClearCrew() { Kills = 0; Veteran = false; PromotedAt = -1; }
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV12 switch. Static like SnipeDeviation / DuelDeviation: ZGame sets it at every start from PlayerPrefs
        /// (oz_veterans 1 = on, default; off in 'Zod exact' oz_zod_exact = 1); the parity rows and bot checks run it off.
        /// Lockstep note: like every deviation switch it is part of the match settings both peers must agree on.</summary>
        public static bool VeteranDeviation = true;
        /// <summary>Kills to promote a crew (DV12). 4: the headless bot-vs-bot runs (reports/kills_*.md) promote a handful of
        /// crews per side per match (after a few minutes of fighting), never a whole army.</summary>
        public static int VeteranKills = 4;
        /// <summary>DV12 buttoned up: the snipe chance against a veteran crew (x0.25 = four times harder to snipe).</summary>
        public const double VeteranSnipeFactor = 0.25;
        /// <summary>DV12 accuracy: hitscan damage_chance x1.10 (grunt 0.70 -> 0.77, sniper 0.80 -> 0.88), capped at 1.</summary>
        public const double VeteranHitFactor = 1.10;
        /// <summary>DV12 accuracy: missile scatter 3/4 of Zod's +-16 px.</summary>
        public const int VeteranScatterNum = 3, VeteranScatterDen = 4;

        /// <summary>DV12 statistics for tools and the parity row (promotions per team: Neutral, Red, Blue).</summary>
        public readonly int[] Promotions = new int[3];

        /// <summary>RelayObjectDeath hook: credit the kill to the attacker's crew (rules in the class summary of ZVeterancy.cs).</summary>
        internal void CreditKill(ZObject victim, int attackerId)
        {
            if (!VeteranDeviation || victim == null || !victim.IsUnit || victim.Owner == Team.Neutral || attackerId < 0) return;
            var a = Get(attackerId);
            if (a == null || !a.IsUnit || a.Destroyed || a.KillAt >= 0 || a.Owner == Team.Neutral || a.Owner == victim.Owner) return;
            AddKill(a.Leader ?? a);
        }

        /// <summary>UpdateObjectDriverHealth hook (the Sniped branch): the driver died, his rank with him; the sniper's crew
        /// gets the kill. v.SnipedFrom = the team that lost the driver.</summary>
        internal void CreditSnipe(ZObject v, ZObject sniper)
        {
            if (v == null) return;
            v.ClearCrew();
            if (!VeteranDeviation || sniper == null || !sniper.IsUnit || sniper.Destroyed || sniper.KillAt >= 0) return;
            if (sniper.Owner == Team.Neutral || v.SnipedFrom == Team.Neutral || sniper.Owner == v.SnipedFrom) return;
            AddKill(sniper.Leader ?? sniper);
        }

        /// <summary>Eject hook: the crew leaves the APC / cannon as a new squad and takes its kills and rank along.</summary>
        internal void CrewOut(ZObject v, List<ZObject> squad)
        {
            if (v == null) return;
            var c = v.CrewOf();
            if (squad != null) foreach (var s in squad) s?.SetCrew(c);
            v.ClearCrew();
        }

        void AddKill(ZObject crew)
        {
            crew.Kills++;
            foreach (var m in crew.Minions) if (m != null) m.Kills = crew.Kills;
            if (crew.Veteran || crew.Kills < VeteranKills) return;
            crew.Veteran = true; crew.PromotedAt = Time;
            foreach (var m in crew.Minions) if (m != null) { m.Veteran = true; m.PromotedAt = Time; }
            VeteranHeal(crew);                                                      // DV110: the new veteran crew gets 15 % of its full health back, never above full (ZWorld.Kc5Rules.cs)
            if ((int)crew.Owner >= 0 && (int)crew.Owner < Promotions.Length) Promotions[(int)crew.Owner]++;
            Emit(ZEventType.Promoted, crew, null, crew.CenterX, crew.CenterY, crew.Kills);          // view: star, Alan's line
        }
    }
}
