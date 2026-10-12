using System;
using System.Collections.Generic;
using System.Globalization;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// What the build menu's unit card says about each thing a fort or a factory can build (OzUnitPopup shows it).
    /// The three lines of words per unit live in the table below and nowhere else: what it is, one strength, one weakness.
    /// They hold no numbers on purpose. Every number on the card is read from the rule tables when the card is first asked
    /// for (ZSettings, the same ZUnitSettings objects the Sim builds its units from), so the card can never drift from the rules:
    ///   HP      = ZUnitSettings.MaxHealth, the health one unit starts with (one robot of a squad).
    ///   DAMAGE  = ZUnitSettings.Damage, the health ONE hit removes (for a shell, rocket or missile: at the centre of its blast),
    ///             followed by "every N s" = the weapon's cycle (attack_speed; the Electrobot's own bolt-to-bolt cycle).
    ///             The Heavy Tank's two shells of one cycle count as its one hit (they always add up to Damage).
    ///             A unit with no weapon shows "none"; the APC shows "as its crew" (it fires the weapon of the robots aboard).
    ///   RANGE   = attack_radius in metres of the game world (ZConst.PX_TO_M: 8 rule pixels = 1 m, one map tile = 2 m).
    /// Bars compare the unit with every unit of the three build lists: HP and RANGE as a plain share of the highest value, DAMAGE
    /// as the square root of the share of the highest damage per second (Damage / cycle), so a rifle stays visible next to a
    /// missile battery. A longer bar always means more.
    /// </summary>
    public static class OzUnitFacts
    {
        public sealed class Words
        {
            public readonly string role, plus, minus;
            public Words(string role, string plus, string minus) { this.role = role; this.plus = plus; this.minus = minus; }
        }

        // @facts begin   unit type, what it is, strength, weakness (plain words, no numbers)
        static readonly Dictionary<string, Words> words = new Dictionary<string, Words>
        {
            { "grunt", new Words("Rifle squad. The basic robot.", "Quickest unit to build", "Lowest health, weakest weapon") },
            { "psycho", new Words("Machine-gun squad.", "Fast fire, tougher than Grunts", "Weak against armoured vehicles") },
            { "sniper", new Words("Marksman squad.", "Long range. Picks off drivers", "Weak against armoured vehicles") },
            { "tough", new Words("Rocket squad.", "Rockets damage buildings too", "Slow to reload, slow on foot") },
            { "pyro", new Words("Flamethrower squad.", "Heavy damage up close", "Short range, slow on foot") },
            { "laser", new Words("Laser squad.", "Strong beam that also hits drivers", "Slowest robot squad to build") },
            { "electrobot", new Words("Lone robot with a lightning gun.", "Never misses. Stuns robots and vehicles", "Slow to recharge between shots") },
            { "jeep", new Words("Fast scout car with a machine gun.", "Fastest unit in the game", "The driver can be shot out of it") },
            { "light", new Words("Light tank with a cannon.", "Fastest tank, quick to reload", "Weakest armour of the tanks") },
            { "medium", new Words("Armoured mech with a cannon.", "Much tougher than the Light Tank", "Slower than the Light Tank") },
            { "heavy", new Words("Heavy tank with twin cannons.", "Best armour and range of the tanks", "Slowest tank, slow to reload") },
            { "apc", new Words("Armoured carrier for one robot squad.", "The squad rides and fires from inside", "Fires only the weapons of its crew") },
            { "missile_launcher", new Words("Mobile missile launcher.", "Biggest blast of any unit", "Slowest vehicle, longest to build") },
            { "crane", new Words("Repair vehicle for destroyed structures.", "Rebuilds destroyed bridges and factories", "Weak attack: only the driver's rifle") },
            { "gatling", new Words("Fixed machine-gun post.", "Rapid fire, quickest fixed gun to build", "Its crew can be shot out") },
            { "gun", new Words("Fixed cannon.", "Explosive shells with splash damage", "Its crew can be shot out") },
            { "howitzer", new Words("Fixed long-range artillery.", "Longest range of any unit", "Slowest reload of any unit") },
            { "missile_cannon", new Words("Fixed missile battery.", "Huge damage with a quick reload", "Slowest fixed gun to build") },
        };
        // @facts end

        /// <summary>One unit's finished card: the strings as shown and the numbers behind them (the proof compares these with the
        /// rule tables through its own arithmetic).</summary>
        public sealed class Card
        {
            public string type, title, basis, role, plus, minus, hp, damage, range;
            public float hpBar, damageBar, rangeBar;
            public int hpValue, damageValue, rangeMetres, squad;
            public double cycle;
            public bool armed, crewWeapon;
        }

        public const string BasisPerRobot = "STATS PER ROBOT";
        public const string NoWeapon = "none", CrewWeapon = "as its crew";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static readonly Dictionary<string, Card> cards = new Dictionary<string, Card>();
        static ZSettings cardsFor; static bool cardsTmp;
        static int maxHp, maxRange; static double maxDps;
        static string[] types;

        /// <summary>Every unit of the three build lists (the rule tables' own name lists: robots, vehicles, fixed guns).</summary>
        public static string[] Types
        {
            get
            {
                if (types != null) return types;
                var l = new List<string>();
                l.AddRange(ZSettings.Robots); l.AddRange(ZSettings.Vehicles); l.AddRange(ZSettings.Cannons);
                return types = l.ToArray();
            }
        }

        public static Words WordsOf(string type) => type != null && words.TryGetValue(type, out var w) ? w : null;

        /// <summary>Seconds from one hit to the next: attack_speed; the Electrobot fires on its own tick cycle (ZElectro).</summary>
        public static double Cycle(ZUnitSettings u, string type) =>
            u == null ? 0 : type == ZElectro.Type ? ZElectro.Seconds(ZElectro.CycleTicks) : u.attack_speed;

        /// <summary>Damage per second of fire, for the damage bar only.</summary>
        public static double PerSecond(ZUnitSettings u, string type)
        {
            double c = Cycle(u, type);
            return u == null || u.Damage <= 0 || c <= 0 ? 0 : u.Damage / c;
        }

        public static int RangeMetres(ZUnitSettings u) => u == null ? 0 : Mathf.RoundToInt(u.attack_radius * ZConst.PX_TO_M);

        /// <summary>The card of a unit type under these rule tables; built once per table set and kept (no work per frame).</summary>
        public static Card For(ZSettings s, string type)
        {
            if (s == null || type == null) return null;
            if (!ReferenceEquals(s, cardsFor) || cardsTmp != OzText.UsesTmp)
            {
                cards.Clear(); cardsFor = s; cardsTmp = OzText.UsesTmp;
                maxHp = 0; maxRange = 0; maxDps = 0;
                foreach (var n in Types)
                {
                    var x = s.Unit(n); if (x == null) continue;
                    if (x.MaxHealth > maxHp) maxHp = x.MaxHealth;
                    if (x.attack_radius > maxRange) maxRange = x.attack_radius;
                    double d = PerSecond(x, n); if (d > maxDps) maxDps = d;
                }
            }
            if (cards.TryGetValue(type, out var c)) return c;
            var u = s.Unit(type); if (u == null) return null;
            var w = WordsOf(type);
            string muted = "<color=" + OzUi.Hex(OzUi.Muted) + ">";
            c = new Card { type = type };
            c.squad = ZBuildLists.IsRobot(type) ? Math.Max(1, u.group_amount) : 1;
            c.title = UnitInfo.Name(type) + (c.squad > 1 ? " ×" + c.squad.ToString(Inv) : "");
            c.basis = c.squad > 1 ? BasisPerRobot : "";
            c.role = w != null ? w.role : ""; c.plus = w != null ? w.plus : ""; c.minus = w != null ? w.minus : "";
            c.hpValue = u.MaxHealth; c.damageValue = u.Damage; c.rangeMetres = RangeMetres(u); c.cycle = Cycle(u, type);
            int radius = u.attack_radius;
            if (type == "crane" && ZWorld.CraneGunDeviation)
            {   // the Sim's own rule (ZObject.CraneGunStep): the crane's driver shoots back with the grunt's rifle, a crew's worth of it
                var r = s.Unit("grunt");
                if (r != null) { c.damageValue = r.Damage * ZWorld.CrewRifleMul; c.cycle = r.attack_speed; radius = r.attack_radius; c.rangeMetres = RangeMetres(r); }
            }
            c.crewWeapon = type == "apc";                                      // the Sim's own test (ZObject.ResetDamageInfo): it fires its crew's weapon
            c.armed = !c.crewWeapon && c.damageValue > 0 && c.cycle > 0;
            c.hp = OzText.Tab(c.hpValue.ToString(Inv));
            string none = muted + (c.crewWeapon ? CrewWeapon : NoWeapon) + "</color>";
            c.damage = c.armed ? OzText.Tab(c.damageValue.ToString(Inv) + " " + muted + "every " + c.cycle.ToString("0.0", Inv) + " s</color>") : none;
            c.range = c.armed && radius > 0 ? OzText.Tab(c.rangeMetres.ToString(Inv) + " " + muted + "m</color>") : none;
            c.hpBar = maxHp > 0 ? Mathf.Clamp01(c.hpValue / (float)maxHp) : 0f;
            c.damageBar = c.armed && maxDps > 0 ? Mathf.Clamp01(Mathf.Sqrt((float)(c.damageValue / c.cycle / maxDps))) : 0f;
            c.rangeBar = c.armed && maxRange > 0 ? Mathf.Clamp01(radius / (float)maxRange) : 0f;
            cards[type] = c;
            return c;
        }
    }
}
