#if !OPENZ_HARNESS
using System;
using System.Reflection;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>K248d counted check (menu OpenZ/V1.1/Sniper: K248d Down-to-stay Check): a sniper kneels or lies down only where its
    /// fight needs no more walking (Sim/ZObject.SniperSettle.cs, View/SniperStanceGate.cs Look). The same staged fights as the K248b check
    /// (OpenZParity.V11SniperSteady.cs: 3 snipers against two grunt squads on dry sand, the drawn stance by the game's own decision code
    /// on a model of the frames around it), counted before (the project before K248b), with K248b's stance rules and with K248d's. When
    /// the rules' own stance is in the build (ZWorld.ProneTargetDeviation), the same fights once more with that rule on, its own
    /// get-ups counted (read by name, so this file compiles with or without it).</summary>
    public static partial class OpenZParity
    {
        static FieldInfo SyField(string n) => typeof(ZWorld).GetField(n, BindingFlags.Public | BindingFlags.Static);
        static int SyInt(string n) { var f = SyField(n); return f == null ? 0 : Convert.ToInt32(f.GetValue(null)); }

        /// <summary>The rules' own stance in the staged fights: the counts of all fights.</summary>
        sealed class SyRules { public int fights, snipers, unfinished, grunts, lieDowns, getUpsWalk, getUpsLinger; public double fightSecs; public ulong trace = 1469598103934665603UL; }

        /// <summary>One staged fight of the K248b check (SsFight's set-up line for line: the level 2 map without its start units, dry
        /// sand at tile 41,57, three red snipers, two blue grunt squads gx px east, the player's attack order on the first squad's leader,
        /// a normal game's switches) with the rules' own stance on: its counters read inside the fight (the switches and counters are put
        /// back when it ends).</summary>
        static void SyFight(int gx, int seed, SyRules r)
        {
            using (new SsSaved())
            {
                SsGameSwitches();
                ZWorld.SniperSteadyDeviation = true; SniperStanceGate.On = true;
                SyField("ProneTargetDeviation")?.SetValue(null, true);
                var w = ZWorld.Create(SettingsText, SsNoUnits(MapText("L02")), seed);
                w.Bots.Clear();
                foreach (var (tm, x) in new[] { (Team.Red, 16), (Team.Blue, (w.Map.width - 2) * 16) })
                { var d = w.CreateObject("robot", "grunt", x, 16, tm); d.MaxHealth = d.Health = 1000000000; d.Damage = 0; }
                for (int k = 0, n = (seed * 5 + gx / 10) % 9; k < n; k++)
                { var d = w.CreateObject("robot", "grunt", 48 + 16 * k, 16, Team.Red); d.MaxHealth = d.Health = 1000000000; d.Damage = 0; }
                int bx = 41 * 16, by = 57 * 16;
                foreach (var o in w.Objects) if (o.Kind == ZKind.MapItem && o.Type == "grenades" && Math.Abs(o.CenterX - bx) < 400 && Math.Abs(o.CenterY - by) < 400) o.KillAt = 0.01;
                while (w.Time < 30.0) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                var sq = w.CreateRobotGroup("sniper", bx, by, Team.Red);
                var g1 = w.CreateRobotGroup("grunt", bx + gx, by - 40, Team.Blue);
                var g2 = w.CreateRobotGroup("grunt", bx + gx + 15, by + 40, Team.Blue);
                var sn = new System.Collections.Generic.List<ZObject> { sq }; sn.AddRange(sq.Minions);
                var gr = new System.Collections.Generic.List<ZObject> { g1 }; gr.AddRange(g1.Minions); gr.Add(g2); gr.AddRange(g2.Minions);
                int l0 = SyInt("StanceLieDowns"), u0 = SyInt("StanceGetUpsWalk"), k0 = SyInt("StanceGetUpsLinger");
                double t0 = w.Time, end = -1, stop = t0 + 40.0; bool ordered = false;
                while (w.Time < stop)
                {
                    if (!ordered)
                    {
                        ordered = true;
                        if (!sq.Destroyed) w.SendWaypoints(Team.Red, sq, new[] { new ZWaypoint { mode = WpMode.Attack, refId = g1.Id, x = g1.CenterX, y = g1.CenterY, playerGiven = true } });
                    }
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events) if (e.type == ZEventType.Death) { var v = w.GetAny(e.objId); if (v != null && gr.Contains(v)) r.grunts++; }
                    w.Events.Clear();
                    foreach (var s in sn) if (!s.Destroyed) r.trace = SnsFnv(SnsFnv(SnsFnv(r.trace, s.LocX), s.LocY), s.AttackObject != null ? s.AttackObject.Id : -1);
                    if (end < 0 && gr.TrueForAll(o => o.Destroyed)) { end = w.Time; stop = Math.Min(stop, end + 4.0); }
                }
                if (end < 0) { r.unfinished++; end = w.Time; }
                r.fights++; r.snipers += sn.Count; r.fightSecs += end - t0;
                r.lieDowns += SyInt("StanceLieDowns") - l0; r.getUpsWalk += SyInt("StanceGetUpsWalk") - u0; r.getUpsLinger += SyInt("StanceGetUpsLinger") - k0;
            }
        }

        /// <summary>The staged fights of the check (3 distances x seeds) with the rules' own stance on, K248d on or off.</summary>
        static SyRules SyRulesStance(bool stay, int seeds)
        {
            bool d0 = ZWorld.SniperDownToStay; var r = new SyRules();
            try { ZWorld.SniperDownToStay = stay; foreach (var gx in SsGx) for (int s = 1; s <= seeds; s++) SyFight(gx, s, r); }
            finally { ZWorld.SniperDownToStay = d0; }
            return r;
        }

        internal static string SyReport(int seeds, out bool all)
        {
            var sb = new StringBuilder(); bool ok_ = true;
            void V(bool ok, string what) { sb.AppendLine((ok ? "PASS  " : "FAIL  ") + what); ok_ &= ok; }
            bool d0 = ZWorld.SniperDownToStay, g0 = SniperStanceGate.On, r0 = ZWorld.SniperSteadyDeviation;
            var dv = SyField("ProneTargetDeviation"); bool p0 = dv != null && (bool)dv.GetValue(null);
            dv?.SetValue(null, false);
            SsCounts before, k248b, after, again;
            try
            {
                before = SsMatrix(false, false, seeds);
                ZWorld.SniperDownToStay = false; k248b = SsMatrix(true, true, seeds);
                ZWorld.SniperDownToStay = true; after = SsMatrix(true, true, seeds); again = SsMatrix(true, true, seeds);
            }
            finally { ZWorld.SniperDownToStay = d0; dv?.SetValue(null, p0); }
            sb.AppendLine($"before (the project before K248b)                        : {before.Text()}");
            sb.AppendLine($"K248b (DV120 + one stance per fight, down as soon as still): {k248b.Text()}");
            sb.AppendLine($"K248d (down only where the fight needs no more walking)   : {after.Text()}");
            int sf = after.snipers;
            V(after.getUpsWalk <= 5, $"S12 lie down / kneel, then stand up for a walk: {after.getUpsWalk} get-ups for a walk in {sf} sniper-fights (at most 5; K248b {k248b.getUpsWalk}, before {before.getUpsWalk})");
            V(after.fightSecs <= before.fightSecs * 1.2, $"S13 the snipers are not slower killers: {after.fightSecs:0.0} s of fighting, before {before.fightSecs:0.0} s (at most +20 %; K248b {k248b.fightSecs:0.0} s)");
            V(after.trace == k248b.trace && after.grunts == k248b.grunts && after.shots == k248b.shots, "S14 the rules' fight is untouched (a view rule here): every position and target of every tick as with K248b's stance rules");
            V(after.Flips == 0 && after.cycles == 0 && after.lieDowns <= after.picks && after.picks <= after.snipers * 1.1,
              $"S15 K248b's stance promises kept: {after.Flips} stance flips with an enemy in range, {after.cycles} down-up-down cycles, {after.lieDowns} lie-downs in {after.picks} stance picks of {sf} sniper-fights");
            V(after.lieDowns > 0 && after.lieDowns - after.getUpsWalk > 0,
              $"S16 they still kneel or lie down where the fight lets them: {after.lieDowns} lie-downs ({after.lieDowns - after.getUpsWalk} that stayed down until the fight was over; K248b {k248b.lieDowns}, of them {k248b.lieDowns - k248b.getUpsWalk})");
            V(again.trace == after.trace && again.Text() == after.Text(), "S17 the same fights twice give the same counts (deterministic)");
            if (dv != null)
            {
                var off = SyRulesStance(false, seeds); var on = SyRulesStance(true, seeds); var on2 = SyRulesStance(true, seeds);
                sb.AppendLine($"the rules' stance ON, K248b's rule there : {off.fights} fights, {off.snipers} sniper-fights, {off.fightSecs:0.0} s of fighting, {off.grunts} grunts killed; the rules' stance: {off.lieDowns} lie-downs, get-ups {off.getUpsWalk} for a walk + {off.getUpsLinger} after the fight");
                sb.AppendLine($"the rules' stance ON, with K248d         : {on.fights} fights, {on.snipers} sniper-fights, {on.fightSecs:0.0} s of fighting, {on.grunts} grunts killed; the rules' stance: {on.lieDowns} lie-downs, get-ups {on.getUpsWalk} for a walk + {on.getUpsLinger} after the fight");
                V(on.getUpsWalk <= 5, $"S18 the rules' own stance: {on.getUpsWalk} get-ups for a walk in {on.snipers} sniper-fights (at most 5; K248b's rule there {off.getUpsWalk})");
                V(on.unfinished == 0 && on.grunts == 6 * on.fights && on.fightSecs <= before.fightSecs * 1.2, $"S19 with the rules' stance every fight is won and not slower: {on.fightSecs:0.0} s, before {before.fightSecs:0.0} s (at most +20 %)");
                V(on.lieDowns > 0 && on.lieDowns - on.getUpsWalk > 0 && on2.trace == on.trace && on2.lieDowns == on.lieDowns && on2.getUpsWalk == on.getUpsWalk,
                  $"S20 the rules' stance still goes down where the fight lets it ({on.lieDowns} lie-downs, {on.lieDowns - on.getUpsWalk} stayed down) and the same fights twice give the same game (deterministic)");
            }
            else sb.AppendLine("(the rules' own stance is not in this build: S18-S20 not run; the view's stance rules decide alone)");
            V(ZWorld.SniperDownToStay == d0 && SniperStanceGate.On == g0 && ZWorld.SniperSteadyDeviation == r0 && (dv == null || (bool)dv.GetValue(null) == p0), "S21 the switches are put back after the cases");
            all = ok_;
            sb.AppendLine(all ? "ALL PASS" : "NOT ALL PASS");
            return sb.ToString();
        }
    }

#if UNITY_EDITOR
    /// <summary>K248d menus: the counted check (outside Play) and, in Play, the stance counters since the last stage.</summary>
    public static class OzV11SniperStayCheck
    {
        [UnityEditor.MenuItem("OpenZ/V1.1/Sniper: K248d Down-to-stay Check (18 fights)")]
        static void Menu() => OzV11Kit.RunMenu("Sniper: K248d Down-to-stay Check (18 fights)", () => OpenZParity.SyReport(6, out _));

        [UnityEditor.MenuItem("OpenZ/V1.1/Sniper: K248d Report down-to-stay (Play)")]
        static void Counters()
        {
            UnityEngine.Debug.Log($"[OpenZ V1.1] K248d: down only where the fight needs no more walking {(ZWorld.SniperDownToStay ? "ON" : "off")} (stance rules {(SniperStanceGate.On ? "ON" : "off")}); since the last stage: " +
                                  $"lie-downs {SniperStanceGate.LieDowns}, get-ups for a walk {SniperStanceGate.GetUpsWalk}, after the fight {SniperStanceGate.GetUpsLinger}; " +
                                  $"kept standing because the fight leads on {SniperStanceGate.HeldStanding} times (the rules' stance: {ZWorld.SniperStoodToWalkOn} looks)");
        }
    }
#endif
}
#endif
