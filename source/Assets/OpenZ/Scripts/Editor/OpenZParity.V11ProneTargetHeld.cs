#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>K305's check P7 (the stage report's P7, Editor/OpenZParity.V11ProneTarget.cs PtStageReport) on a held-stance stage: the
    /// cover rule itself, whatever the stance rule picks. A sniper squad (3) on the open sand of K248b's stage, held lying, kneeling and
    /// standing (already down: they cover at once; the stances go round the three snipers from seed to seed, so no place in the squad
    /// favours one), two enemy grunt squads 13 m east, as the Play stage (menu K305 Stage snipers under fire); nobody can die, headshots
    /// off, the factories build nothing, no fort gun. Counted: every grunt gun shot at a held sniper and its hit, by the held stance; and
    /// the same stage with the rule off, where a held lying sniper is hit as a standing one (so the stage itself favours no stance and the
    /// gap is the rule's). The natural fights of the stage report may put no lying sniper under fire at all (a sniper that lies down only
    /// where it will stay often does so after the last grunt fell), so P7 is counted here.</summary>
    public static partial class OpenZParity
    {
        /// <summary>The held-stance stage: seconds of fire per seed.</summary>
        internal const double PtHeldSecs = 30.0;

        internal sealed class PtHeld
        {
            public int stages, snipers, notHeld, otherShots;
            public readonly int[] shotsAt = new int[3], hitsAt = new int[3], ruleShots = new int[3], ruleHits = new int[3];
            public ulong trace = 1469598103934665603UL;
            public void Add(PtHeld o)
            {
                stages += o.stages; snipers += o.snipers; notHeld += o.notHeld; otherShots += o.otherShots;
                for (int i = 0; i < 3; i++) { shotsAt[i] += o.shotsAt[i]; hitsAt[i] += o.hitsAt[i]; ruleShots[i] += o.ruleShots[i]; ruleHits[i] += o.ruleHits[i]; }
                trace = SnsFnv(trace, (long)o.trace);
            }
            public double Rate(int i) => shotsAt[i] > 0 ? (double)hitsAt[i] / shotsAt[i] : 0;
            public string Hit(int i) => shotsAt[i] > 0 ? $"{hitsAt[i]}/{shotsAt[i]} ({100.0 * hitsAt[i] / shotsAt[i]:0.0} %)" : "0/0";
            public string Text() =>
                $"{stages} stages x {PtHeldSecs:0} s, {snipers} held snipers; grunt gun shots at them by the held stance: standing {Hit(0)}, kneeling {Hit(1)}, lying {Hit(2)}; " +
                $"the rules' cover counters: standing {ruleHits[0]}/{ruleShots[0]}, kneeling {ruleHits[1]}/{ruleShots[1]}, lying {ruleHits[2]}/{ruleShots[2]}; " +
                $"ticks a sniper was out of its held stance {notHeld}; shots at them by others {otherShots}";
        }

        /// <summary>One held-stance stage, DV122 <paramref name="on"/>: <see cref="PtHeldSecs"/> s of fire after the squads are placed.</summary>
        internal static PtHeld PtHeldStage(bool on, int seed)
        {
            var r = new PtHeld { stages = 1 };
            using (new SsSaved())
            {
                PtSwitches(on);
                ZWorld.SniperHeadshotDeviation = false;                                               // as the Play stage: a headshot would take a sniper off the stage
                var w = ZWorld.Create(SettingsText, SsNoUnits(MapText("L02")), seed);
                w.Bots.Clear(); PtQuiet(w, true);
                foreach (var (tm, x) in new[] { (Team.Red, 16), (Team.Blue, (w.Map.width - 2) * 16) })
                { var d = w.CreateObject("robot", "grunt", x, 16, tm); d.MaxHealth = d.Health = 1000000000; d.Damage = 0; }   // keep-alive grunts far away (the match never ends)
                int bx = 41 * 16, by = 57 * 16;                                                       // K248b's stage: open dry sand
                foreach (var o in w.Objects) if (o.Kind == ZKind.MapItem && o.Type == "grenades" && Math.Abs(o.CenterX - bx) < 400 && Math.Abs(o.CenterY - by) < 400) o.KillAt = 0.01;
                while (w.Time < 30.0) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                var sq = w.CreateRobotGroup("sniper", bx, by, Team.Red);
                var g1 = w.CreateRobotGroup("grunt", bx + 104, by - 24, Team.Blue);                   // the Play stage's two squads, 13 m east
                var g2 = w.CreateRobotGroup("grunt", bx + 112, by + 24, Team.Blue);
                var sn = new List<ZObject> { sq }; sn.AddRange(sq.Minions);
                var gr = new HashSet<int>();
                foreach (var g in new[] { g1, g2 })
                {
                    g.MaxHealth = g.Health = 1000000000; gr.Add(g.Id);
                    foreach (var m in g.Minions) { m.MaxHealth = m.Health = 1000000000; gr.Add(m.Id); }
                }
                var held = new Dictionary<int, ZStance>();
                for (int i = 0; i < sn.Count; i++)
                {
                    var st = (ZStance)((i + seed) % 3);                                               // the stances go round the squad from seed to seed
                    sn[i].MaxHealth = sn[i].Health = 1000000000; sn[i].HoldStanceForCheck(st); held[sn[i].Id] = st;
                }
                r.snipers = sn.Count;
                var rs0 = (int[])ZWorld.CoverShots.Clone(); var rh0 = (int[])ZWorld.CoverHits.Clone();
                double stop = w.Time + PtHeldSecs;
                while (w.Time < stop)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                    {
                        if (e.type != ZEventType.Hitscan || !held.TryGetValue(e.otherId, out var hs)) continue;
                        if (!gr.Contains(e.objId)) { r.otherShots++; continue; }
                        r.shotsAt[(int)hs]++; if (e.flag) r.hitsAt[(int)hs]++;
                        r.trace = SnsFnv(SnsFnv(r.trace, e.otherId), e.flag ? 1 : 2);
                    }
                    w.Events.Clear();
                    foreach (var s in sn)
                    {
                        var hs = held[s.Id];
                        if (s.Destroyed || s.Stance != hs || s.CoverStance != (on ? hs : ZStance.Stand)) r.notHeld++;   // rule off: drawn down, hit as standing
                        r.trace = SnsFnv(SnsFnv(SnsFnv(r.trace, s.LocX), s.LocY), s.Health);
                    }
                }
                for (int i = 0; i < 3; i++) { r.ruleShots[i] = ZWorld.CoverShots[i] - rs0[i]; r.ruleHits[i] = ZWorld.CoverHits[i] - rh0[i]; }
            }
            return r;
        }

        /// <summary>How far the hits at stance <paramref name="i"/> lie from the standing rate times <paramref name="share"/>, in standard
        /// deviations (the binomial spread of both counts).</summary>
        static double PtHeldZ(PtHeld x, int i, double share)
        {
            double n0 = x.shotsAt[0], ni = x.shotsAt[i];
            if (n0 <= 0 || ni <= 0) return double.PositiveInfinity;
            double p0 = x.hitsAt[0] / n0, pi = p0 * share, k = ni * share / n0;
            double v = ni * pi * (1 - pi) + k * k * n0 * p0 * (1 - p0);
            return (x.hitsAt[i] - ni * pi) / Math.Sqrt(Math.Max(1e-9, v));
        }

        /// <summary>P7 on the held-stance stage: whole rounds of the three stances (at least 3 seeds), the rule off, on, and on again.</summary>
        internal static (bool ok, string verdict, string detail) PtHeldP7(int seeds)
        {
            int n = Math.Max(3, (seeds + 2) / 3 * 3);
            bool d0 = ZWorld.ProneTargetDeviation;
            PtHeld off = new PtHeld(), on = new PtHeld(), again = new PtHeld();
            for (int s = 1; s <= n; s++) { off.Add(PtHeldStage(false, s)); on.Add(PtHeldStage(true, s)); again.Add(PtHeldStage(true, s)); }
            double kp = ZWorld.ProneHitPct / 100.0, kk = ZWorld.KneelHitPct / 100.0;
            double zp = PtHeldZ(on, 2, kp), zk = PtHeldZ(on, 1, kk), fp = PtHeldZ(off, 2, 1.0), fk = PtHeldZ(off, 1, 1.0);
            bool enough = true, rules = on.otherShots == 0 && off.otherShots == 0;
            for (int i = 0; i < 3; i++)
            {
                enough &= on.shotsAt[i] >= 100 && off.shotsAt[i] >= 100;
                rules &= on.ruleShots[i] == on.shotsAt[i] && on.ruleHits[i] == on.hitsAt[i] && off.ruleShots[i] == 0;
            }
            bool order = on.Rate(2) < on.Rate(1) && on.Rate(1) < on.Rate(0);
            bool share = Math.Abs(zp) <= 3 && Math.Abs(zk) <= 3, fair = Math.Abs(fp) <= 3 && Math.Abs(fk) <= 3;
            bool held = on.notHeld == 0 && off.notHeld == 0, same = again.trace == on.trace && again.Text() == on.Text(), back = ZWorld.ProneTargetDeviation == d0;
            bool ok = enough && order && share && fair && held && rules && same && back;
            var why = new List<string>();
            if (!enough) why.Add("fewer than 100 shots at a stance");
            if (!order) why.Add("not lying < kneeling < standing");
            if (!share) why.Add("the rule's shares not within 3 sd");
            if (!fair) why.Add("rule off: the stances not hit alike");
            if (!held) why.Add("a sniper left its held stance");
            if (!rules) why.Add("the rules' counters differ from the counted shots");
            if (!same) why.Add("not the same twice");
            if (!back) why.Add("the switch was not put back");
            string X(PtHeld x, int i) => x.Rate(0) > 0 ? $"x{x.Rate(i) / x.Rate(0):0.00}" : "x-";
            string Z(double z) => double.IsInfinity(z) ? "-" : z.ToString("+0.0;-0.0;0.0");
            string verdict = $"P7 the grunts hit a lying sniper less often than a standing one (held-stance stage, {n} x {PtHeldSecs:0} s; the rule ON: standing {on.Hit(0)}, kneeling {on.Hit(1)}, lying {on.Hit(2)}, " +
                             $"lying {X(on, 2)} and kneeling {X(on, 1)} of standing, the rule {ZWorld.ProneHitPct} % / {ZWorld.KneelHitPct} % (z {Z(zp)} / {Z(zk)}, within 3); " +
                             $"the rule OFF, the same stage: standing {off.Hit(0)}, kneeling {off.Hit(1)}, lying {off.Hit(2)}, hit alike (z {Z(fp)} / {Z(fk)}); " +
                             $"in the held stance every tick, the rules' counters = the counted shots, the same twice){(ok ? "" : "  <- " + string.Join("; ", why))}";
            var sb = new StringBuilder();
            sb.AppendLine($"      held-stance stage, rule OFF: {off.Text()}");
            sb.AppendLine($"      held-stance stage, rule ON : {on.Text()}");
            return (ok, verdict, sb.ToString());
        }

        /// <summary>P7 alone (its own menu and the headless runner).</summary>
        internal static string PtHeldReport(int seeds, out bool all)
        {
            var sb = new StringBuilder();
            var p = PtHeldP7(seeds);
            sb.AppendLine("K305 P7 on the held-stance stage (3 snipers held lying / kneeling / standing, the stances going round the squad from seed to seed; two grunt squads 13 m east; nobody can die, headshots off; a normal game's switches with K248b's rules)");
            sb.AppendLine((p.ok ? "PASS  " : "FAIL  ") + p.verdict);
            sb.Append(p.detail);
            all = p.ok;
            sb.AppendLine(all ? "ALL PASS" : "NOT ALL PASS");
            return sb.ToString();
        }
    }

#if UNITY_EDITOR
    /// <summary>K305's P7 alone, outside Play (the quick and the full Cover Check menus count it too, in their stage part).</summary>
    public static class OzV11ProneTargetHeldMenu
    {
        [UnityEditor.MenuItem("OpenZ/V1.1/Sniper: K305 Cover Check P7 (held-stance stage)")]
        static void Menu() => OzV11Kit.RunMenu("Sniper: K305 Cover Check P7 (held-stance stage)", () => OpenZParity.PtHeldReport(6, out _));
    }
#endif
}
#endif
