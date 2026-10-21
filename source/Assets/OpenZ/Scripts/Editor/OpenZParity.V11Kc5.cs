#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations rows DV110 "a new veteran heals" and DV111 "the fort's roof defender is a sniper" (the rules:
    /// Sim/ZWorld.Kc5Rules.cs). Measured headless on the level 2 map without its start units (forts, buildings and terrain kept).</summary>
    public static partial class OpenZParity
    {
        static void AddKc5Deviations(List<Deviation> list)
        {
            list.Add(DevVeteranHeal());
            list.Add(DevFortSniper());
        }

        /// <summary>A level map without its robots, vehicles and guns (forts, buildings, flags, map items and terrain stay).</summary>
        internal static string Kc5NoUnits(string mapJson)
        {
            var m = ZMapData.FromJson(mapJson);
            m.objects.RemoveAll(o => { string c = o.kind.Split(':')[0]; return c == "robot" || c == "vehicle" || c == "cannon"; });
            return UnityEngine.JsonUtility.ToJson(m);
        }

        /// <summary>One red and one blue grunt in far corners that cannot die and do no harm: with them neither side ever has no units
        /// left (Zod's no-units rule would end the game and freeze the forts).</summary>
        internal static void Kc5KeepAlive(ZWorld w)
        {
            foreach (var (t, x) in new[] { (Team.Red, 16), (Team.Blue, (w.Map.width - 2) * 16) })
            {
                var d = w.CreateObject("robot", "grunt", x, 16, t);
                d.MaxHealth = d.Health = 1000000000; d.Damage = 0;
            }
        }

        /// <summary>DV110 probe, one case: a red grunt squad (leader, two members) and a red light tank at the given health shares, four
        /// blue grunts killed and credited to the squad (and four to the tank): the health of each before and after the promotion.</summary>
        internal static string Kc5HealCase(bool on, out bool pass)
        {
            bool was = ZWorld.VeteranHealDeviation, vet = ZWorld.VeteranDeviation;
            ZWorld.VeteranHealDeviation = on; ZWorld.VeteranDeviation = true;
            try
            {
                var w = ZWorld.Create(SettingsText, Kc5NoUnits(MapText("L02")), 3);
                w.Bots.Clear(); Kc5KeepAlive(w);
                var fort = w.Objects.First(o => o.IsFort && o.Owner == Team.Red);
                int x0 = fort.CenterX + 160, y0 = fort.CenterY + 160;
                var lead = w.CreateRobotGroup("grunt", x0, y0, Team.Red);
                var squad = new List<ZObject> { lead }; squad.AddRange(lead.Minions);
                var tank = w.CreateObject("vehicle", "light", x0 + 96, y0, Team.Red);
                int[] share = { 50, 95, 100, 20 };                                                     // leader, member 1, member 2, the tank (per cent)
                for (int i = 0; i < squad.Count && i < 3; i++) squad[i].SetHealth(squad[i].MaxHealth * share[i] / 100);
                tank.SetHealth(tank.MaxHealth * share[3] / 100);
                ZObject dead = squad.Count > 3 ? squad[3] : null;
                var before = squad.Select(o => o.Health).ToList(); int tank0 = tank.Health;
                int heals0 = w.VeteranHeals;
                void Kills(ZObject a, int n)
                {
                    for (int k = 0; k < n; k++)
                    {
                        var v = w.CreateObject("robot", "grunt", x0 + 300 + 20 * k, y0 + 200, Team.Blue);
                        v.SetHealth(0); w.UpdateObjectHealth(v, a.Id);
                    }
                }
                Kills(lead, 3);
                bool notYet = squad.Select(o => o.Health).SequenceEqual(before) && !lead.Veteran;
                Kills(lead, 1);
                Kills(tank, 4);
                var after = squad.Select(o => o.Health).ToList();
                int Exp(int h, int max) => Math.Min(max, h >= max ? h : h + max * ZWorld.VeteranHealPct / 100);
                bool ok = notYet && lead.Veteran && tank.Veteran;
                var parts = new List<string>();
                for (int i = 0; i < 3 && i < squad.Count; i++)
                {
                    int want = on ? Exp(before[i], squad[i].MaxHealth) : before[i];
                    ok &= after[i] == want && after[i] <= squad[i].MaxHealth;
                    parts.Add($"{(i == 0 ? "leader" : "member " + i)} {share[i]} % ({before[i]}) -> {after[i]} HP ({100.0 * after[i] / squad[i].MaxHealth:0} %)");
                }
                int tWant = on ? Exp(tank0, tank.MaxHealth) : tank0;
                ok &= tank.Health == tWant;
                parts.Add($"light tank {share[3]} % ({tank0}) -> {tank.Health} HP ({100.0 * tank.Health / tank.MaxHealth:0} %)");
                parts.Add($"after 3 kills nothing changed {(notYet ? "yes" : "NO")}, units healed {w.VeteranHeals - heals0}");
                ok &= (w.VeteranHeals - heals0) == (on ? 3 : 0);                                       // leader, member 1, the tank (member 2 was full)
                pass = ok;
                return string.Join("; ", parts);
            }
            finally { ZWorld.VeteranHealDeviation = was; ZWorld.VeteranDeviation = vet; }
        }

        static Deviation DevVeteranHeal()
        {
            string measured; bool pass;
            try
            {
                string a = Kc5HealCase(true, out bool pa), b = Kc5HealCase(false, out bool pb);
                pass = pa && pb;
                measured = $"rule on: {a} | rule off: {b}";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            return new Deviation
            {
                id = "DV110", change = "A crew that becomes a veteran gets 15 % of its full health back (never above full)",
                zod = "no veterancy at all (DV12 is OpenZ's); health comes back only at a repair station or by a crane",
                openz = $"Sim/ZWorld.Kc5Rules.cs: on the tick of the kill that promotes a crew (DV12, ZVeterancy.cs AddKill), every living unit of it (the squad leader and his squad, or the vehicle / cannon the crew drives) " +
                        $"that is not at full health gets full health x {ZWorld.VeteranHealPct} / 100 (rounded down) back, capped at its full health. A rank handed on later (boarding, leaving, a repair) heals nothing",
                measured = measured,
                toggle = "ZWorld.VeteranHealDeviation: on in the game (with DV12; 'Zod exact' turns DV12 off, so nothing heals); off in the other rows and the version 1.1 counted checks unless they set it",
                status = pass ? DevActive : DevCheck,
            };
        }

        /// <summary>DV111 probe, one case: a blue heavy tank that cannot die shells the red fort of level 2 (fort gun rule off, like the DV41
        /// probe): the man who comes up, his type, health, range, damage, hit chance and the gaps between his shots over 20 s.</summary>
        internal static string Kc5SniperCase(bool on, out string type, out int range, out int damage, out double gap, out int shots)
        {
            bool was = ZWorld.FortSniperDeviation, fd = ZWorld.FortDefenderDeviation, fg = ZWorld.FortGunDeviation;
            ZWorld.FortSniperDeviation = on; ZWorld.FortDefenderDeviation = true; ZWorld.FortGunDeviation = false;
            type = "-"; range = 0; damage = 0; gap = -1; shots = 0;
            try
            {
                var w = ZWorld.Create(SettingsText, Kc5NoUnits(MapText("L02")), 5);
                w.Bots.Clear(); Kc5KeepAlive(w);
                var fort = w.Objects.First(o => o.IsFort && o.Owner == Team.Red);
                ZObject heavy = null;
                for (int dx = 9; dx <= 13 && heavy == null; dx++)
                {
                    int x = fort.CenterX + dx * 16, y = fort.CenterY;
                    if (!w.Path.TilePassable(x >> 4, y >> 4, false) || !w.Path.TilePassable((x >> 4) + 1, (y >> 4) + 1, false)) continue;
                    heavy = w.CreateObject("vehicle", "heavy", x, y, Team.Blue);
                }
                if (heavy == null) return "no free spot for the attacking tank";
                heavy.MaxHealth = heavy.Health = 1000000000;
                w.SendWaypoints(Team.Blue, heavy, new[] { new ZWaypoint { mode = WpMode.Attack, refId = fort.Id, x = fort.CenterX, y = fort.CenterY } });
                ZObject man = null; var times = new List<double>();
                while (w.Time < 40 && (man == null || times.Count < 12))
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                    {
                        if (e.type == ZEventType.Created && e.otherId == fort.Id) man = w.Get(e.objId);
                        if (man != null && e.type == ZEventType.Hitscan && e.objId == man.Id) times.Add(w.Time);
                    }
                    w.Events.Clear();
                }
                if (man == null) return "nobody came up";
                type = man.Type; range = man.AttackRadius; damage = man.Damage; shots = times.Count;
                var gaps = times.Zip(times.Skip(1), (a, b) => b - a).ToList();
                gap = gaps.Count > 0 ? gaps.Min() : -1;
                return $"{man.Type} #{man.Id}, {man.MaxHealth} HP, range {man.AttackRadius} px, {man.Damage} HP a hit at {man.DamageChance:0.00}, " +
                       $"shortest gap {(gap < 0 ? "-" : gap.ToString("0.00"))} s over {shots} shots, snipes drivers {(man.CanSnipe ? "yes" : "no")}";
            }
            finally { ZWorld.FortSniperDeviation = was; ZWorld.FortDefenderDeviation = fd; ZWorld.FortGunDeviation = fg; }
        }

        static Deviation DevFortSniper()
        {
            string measured; bool pass;
            try
            {
                var set = ZSettings.Parse(SettingsText);
                var sn = set.Unit("sniper"); var gr = set.Unit("grunt");
                string a = Kc5SniperCase(true, out string ta, out int ra, out int da, out double ga, out int sa);
                string b = Kc5SniperCase(false, out string tb, out int rb, out int db, out double gb, out int sb);
                pass = ta == "sniper" && ra == sn.attack_radius && da == sn.Damage && Math.Abs(ga - sn.attack_speed) < 0.011 && sa >= 10
                       && tb == "grunt" && rb == gr.attack_radius && db == gr.Damage * ZWorld.CrewRifleMul && (sb == 0 || Math.Abs(gb - gr.attack_speed) < 0.011);   // the grunt's 120 px often reach nothing
                measured = $"rule on: {a} | rule off: {b}{(sb == 0 ? " (the tank shelling the fort stands beyond the grunt's 120 px: no shot)" : "")}";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            return new Deviation
            {
                id = "DV111", change = "The fort's roof defender (DV41) is a sniper instead of a grunt with the crew rifle",
                zod = "forts have no crew of their own (bfort.cpp); DV41 sent up a grunt with the grunt's shot x 3 (the crane driver's rifle)",
                openz = "Sim/ZWorld.Kc5Rules.cs + ZObject.FortDefender.cs: the man is a sniper of the fort's team with Zod's sniper settings (health, range, shot, hit chance, cycle); every other DV41 rule is kept: " +
                        "when and where he comes up, the deck walk, a clear line for every shot, grenade lobs when no spot has a line (his range + the roof bonus), no driver sniping, the sandbag cover, the respawn, going down. " +
                        "View: the sniper's model, rifle, crack and aim; on the roof he kneels, never lies prone (RobotWeaponPose)",
                measured = measured,
                toggle = "ZWorld.FortSniperDeviation: on in the game (with DV41; 'Zod exact' turns DV41 off); off in the other rows and the version 1.1 counted checks unless they set it",
                status = pass ? DevActive : DevCheck,
            };
        }
    }
}
#endif
