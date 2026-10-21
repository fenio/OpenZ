using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// OpenZ deviation DV112 "hero squads stronger" (on top of DV108 hero squads, Sim/ZHeroSquad.cs, and KH2's range, Sim/ZHeroSquad.Kh2.cs).
    /// ALAN &amp; BRAD, both members, from the numbers they had before:
    /// <list type="bullet">
    /// <item><b>Hit points x 1.20</b>: <see cref="ZHeroKind.HealthMul"/> x a grunt's, then x <see cref="ZHeroBoostRow.HealthPct"/> / 100
    /// (rounded half up): 3 x 1081 = 3243 -> 3892.</item>
    /// <item><b>Range x 1.15</b>: the member's own attack radius (KH2: Brad a grunt's 120 px, Alan 1.10 x = 132 px) x
    /// <see cref="ZHeroBoostRow.RangePct"/> / 100 (rounded half up): Brad 138 px (17.3 m), Alan 152 px (19 m). It is the robot's one
    /// range, as in KH2 (rifle, grenade throws, engage and agro tests, the approach planned with the leader's range).</item>
    /// <item><b>The strong shot x 1.20</b>: a bullet of theirs that hits removes (<see cref="ZHeroKind.DamageMul"/> x a grunt's hit) x
    /// <see cref="ZHeroBoostRow.DamagePct"/> / 100 (rounded half up): on a grunt 375 -> 450, on a hull 33 -> 40.</item>
    /// <item><b>Their thrown grenades x 1.20</b> (Alan's own five and any a hero throws from the squad's pool): the HP a grenade of theirs
    /// takes x <see cref="ZHeroBoostRow.GrenadePct"/> / 100 (rounded half up), set where the grenade leaves the hand (ZObject.cs
    /// ProcessAttackDamage): with the game's grenade rule DV104 a direct hit 1916 -> 2299 and an area hit 1499 -> 1799 at the centre of the
    /// blast (<see cref="ZMissile.areaDamage"/>, read by ZWorld.BlastHit), the same fall-off to the edge; with DV104 off 1666 -> 1999.
    /// The radius, the scatter, the throw range, the flight and the pause between throws stay every robot's.</item>
    /// <item><b>Self-repair</b>, the heroes' own ability: while a member is on foot (a robot on the map), alive and below his full health,
    /// he gets back <see cref="ZHeroBoostRow.RegenPctPerMin"/> % of his full health per minute of the game clock, a little every tick
    /// (integer maths: full health x per cent x milliseconds, the rest below one hit point carried to the next tick), never above
    /// his full health. 20 %: 778.4 HP a minute, from half health to full in 150 s. Inside a vehicle, gun or troop carrier the crew list
    /// keeps his hit points as they were (no repair there); out again he repairs on. A destroyed hero never comes back.</item>
    /// </list>
    /// Set where the kind's numbers are set (ZWorld.MakeHero: at the start and every time a member gets out of a crew list), in
    /// ZObject.HeroHit and in ZObject.HeroGrenade; the self-repair runs once a tick (ZWorld.Tick, before the flag captures). Switch
    /// <see cref="ZWorld.HeroBoostDeviation"/>: set at every game start (off in 'Zod exact', where no hero squad exists); off by default so
    /// every headless world and the older counted checks keep the numbers before DV112. No random draw; the game clock only.
    /// </summary>
    public sealed class ZHeroBoostRow
    {
        /// <summary>Per cent of the kind's own hit points, range, strong shot and thrown grenades (100 = unchanged).</summary>
        public int HealthPct = 100, RangePct = 100, DamagePct = 100, GrenadePct = 100;
        /// <summary>Self-repair: per cent of his full health per minute of the game clock (0 = none).</summary>
        public int RegenPctPerMin;
    }

    public static class ZHeroBoost
    {
        /// <summary>The boost of each hero kind (kind id -> row). Both members of a kind get the same row.</summary>
        public static readonly Dictionary<string, ZHeroBoostRow> Kinds = new Dictionary<string, ZHeroBoostRow>
        {
            [ZHeroSquads.AlanBrad] = new ZHeroBoostRow { HealthPct = 120, RangePct = 115, DamagePct = 120, GrenadePct = 120, RegenPctPerMin = 20 },
        };

        /// <summary>The kind's row while the switch is on (null = no boost: a normal robot, another kind, the switch off).</summary>
        public static ZHeroBoostRow Of(ZHeroKind k) => ZWorld.HeroBoostDeviation && k != null && Kinds.TryGetValue(k.Id, out var r) ? r : null;

        static int Pct(int v, int pct) => v <= 0 || pct == 100 ? v : (int)(((long)v * Math.Max(0, pct) + 50) / 100);   // rounded half up

        /// <summary>Full health from the kind's own (HealthMul x the base type's).</summary>
        public static int Health(int hp, ZHeroKind k) { var r = Of(k); return r == null ? hp : ZHeroPlus.Health(Pct(hp, r.HealthPct), k); }   // DV133 (item 444): x 1.15 on top (Sim/ZHeroSquad.K442.cs)
        /// <summary>The attack radius (px) from the member's own (KH2).</summary>
        public static int Range(int px, ZHeroKind k) { var r = Of(k); return r == null ? px : Pct(px, r.RangePct); }
        /// <summary>The HP a hit removes, from the strong shot (DamageMul x the base type's hit).</summary>
        public static int Damage(int hit, ZHeroKind k) { var r = Of(k); return r == null ? hit : ZHeroPlus.Damage(Pct(hit, r.DamagePct), k); }   // DV133 (item 444): x 1.15 on top (Sim/ZHeroSquad.K442.cs)
        /// <summary>The HP a thrown grenade of theirs takes, from the game's grenade (a direct hit or an area hit at the centre of the blast).</summary>
        public static int Grenade(int hp, ZHeroKind k) { var r = Of(k); return r == null ? hp : ZHeroPlus.Grenade(Pct(hp, r.GrenadePct), k); }   // DV133 (item 444): x 1.15 on top (Sim/ZHeroSquad.K442.cs)
        /// <summary>Self-repair per cent of full health per minute (0 = none).</summary>
        public static int RegenPct(ZHeroKind k) { var r = Of(k); return r == null ? 0 : Math.Max(0, r.RegenPctPerMin); }
    }

    public partial class ZObject
    {
        /// <summary>DV112: the self-repair's rest below one hit point (full health x per cent x milliseconds).</summary>
        internal long HeroRegenCarry;
        /// <summary>DV112: this robot repairs himself now (a hero of a kind with self-repair, alive, below full health). View: the unit panel.</summary>
        public bool HeroRepairing => Kind == ZKind.Robot && ZHeroBoost.RegenPct(Hero) > 0 && MaxHealth > 0 && !Destroyed && KillAt < 0 && Health < MaxHealth;

        /// <summary>ProcessAttackDamage hook, right after a thrown grenade got the game's numbers: a hero's grenade takes x GrenadePct / 100,
        /// on a direct hit (its damage) and on an area hit (its own <see cref="ZMissile.areaDamage"/>, only read while DV104 is on). Every
        /// other robot's grenade, and theirs with the switch off: as it was.</summary>
        void HeroGrenade(ZMissile m)
        {
            if (m == null || ZHeroBoost.Of(Hero) == null) return;
            m.damage = ZHeroBoost.Grenade(m.damage, Hero);
            m.areaDamage = ZHeroBoost.Grenade(World.ThrownGrenadeAreaDamage, Hero);
            World.HeroGrenadesThrown++;
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV112 switch: hero squads stronger (hit points, range, strong shot) and their self-repair. The game sets it at every start
        /// (off in 'Zod exact'); off by default so every headless world keeps the numbers before DV112.</summary>
        public static bool HeroBoostDeviation = false;
        /// <summary>DV112 statistics (checks): hit points given back by the self-repair in this world.</summary>
        public long HeroRegenHp { get; private set; }
        /// <summary>DV112 statistics (checks): thrown grenades of a hero that carried the boost in this world.</summary>
        public int HeroGrenadesThrown { get; internal set; }

        /// <summary>Per cent x seconds of a minute x milliseconds: one hit point of the carry.</summary>
        const long HeroRegenUnit = 100L * 60 * 1000;

        /// <summary>ZWorld.Tick hook (once a tick, after the objects ran and the blasts, run-overs and crocodiles hit): every hero on foot
        /// below full health gets his share of the minute's self-repair.</summary>
        void HeroRegenTick(double dt)
        {
            if (!HeroBoostDeviation || dt <= 0) return;
            long ms = (long)Math.Round(dt * 1000);
            for (int i = 0; i < Objects.Count; i++)
            {
                var o = Objects[i];
                if (o.Hero == null) continue;
                int pct = ZHeroBoost.RegenPct(o.Hero);
                if (pct <= 0 || o.Kind != ZKind.Robot || o.MaxHealth <= 0 || o.Destroyed || o.KillAt >= 0 || o.Health >= o.MaxHealth) { o.HeroRegenCarry = 0; continue; }
                o.HeroRegenCarry += ms * o.MaxHealth * pct;
                long add = o.HeroRegenCarry / HeroRegenUnit;
                if (add <= 0) continue;
                o.HeroRegenCarry -= add * HeroRegenUnit;
                int h0 = o.Health;
                o.SetHealth((int)Math.Min(o.MaxHealth, o.Health + add));
                HeroRegenHp += o.Health - h0;
            }
        }

        /// <summary>A hold of the switch for checks and reports (off while held, put back on Dispose).</summary>
        public sealed class HeroBoostHold : IDisposable
        {
            readonly bool was;
            internal HeroBoostHold(bool on) { was = HeroBoostDeviation; HeroBoostDeviation = on; }
            public void Dispose() { HeroBoostDeviation = was; }
        }
        public static HeroBoostHold HeroBoostOff() => new HeroBoostHold(false);
        public static HeroBoostHold HeroBoostOn() => new HeroBoostHold(true);
    }
}
