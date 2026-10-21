using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted check of K438 (rule DV130, Sim/ZObject.NoAutoFlag.cs: a robot of a player's team in a fight it can take runs to no flag by itself;
    /// a flag the player clicked is always obeyed). Headless: no scene, no Play mode, nothing saved. Every row with the rule off (the game
    /// before) and on; the switches of a game in Play (OzV11Kit.GameSwitches plus the rules View/ZGame.cs turns on at a start: Toughs and
    /// snipers stand and fire, calm movement, ...); the calm start (DV69) off; open ground 80 x 80 tiles with one free flag at tile (40, 40);
    /// red is the player's team (blue: a computer player that gives no orders; its units fight by themselves). "Own flag run" = a red squad
    /// leader holds a waypoint onto the flag that the player did not give.
    /// <para>A. The case reported: a squad of each robot type stands 150 px east of the flag through a calm start of 3 s (its robots take
    /// their places; they fire at what is in range); 0.25 s before it ends a blue grunt arrives and holds position where the squad's last
    /// robot sees it and has it in range,
    /// while one cliff tile hides it from the squad leader (found for each type and seed by a search on the same ground). Rule off: at its
    /// first look after the calm start the leader, seeing no enemy, runs to the flag and takes its squad out of the fight. Rule on: no own
    /// flag run while the fight lasts; the squad fights, wins, then takes the flag.</para>
    /// <para>B. A flag run of its own under way: at 3 s the squad's last robot is hit (its hit time written, as every enemy hit writes it)
    /// and a blue grunt that cannot move or shoot stands 150 px beyond it (170 px from the leader, out of the leader's own reach). Rule on:
    /// the run ends at the next half-second look, the squad stays and its robot goes and fights; the run goes on when the fight is over.</para>
    /// <para>C. Fire it cannot answer: a blue howitzer 190 px east of an idle Tough squad (beyond its range + 40 px), the flag 150 px west; and
    /// the same for a light tank (a vehicle). Both must run as before (the run takes them out of the howitzer's reach).</para>
    /// <para>D. The player clicks the flag under fire (C's howitzer; a laser squad the Tough is fighting): it runs and takes the flag.</para>
    /// <para>E. Calm: an idle Tough squad and an idle grunt squad, no enemy: they take the flag by themselves as before.</para>
    /// Deterministic: every row is run twice (the second time must give the same lines).
    /// </summary>
    public static class OzNoAutoFlagCount
    {
        public const int Runs = 5;
        const int FT = 40;                                                                   // the flag's tile (x and y)

        static string F(double v, string f = "0.00") => v.ToString(f, CultureInfo.InvariantCulture);

        /// <summary>The rules View/ZGame.cs turns on at every start that the counted checks' defaults leave off (OzV11Kit.GameSwitches): the
        /// combat and movement of a game in Play. Each only when the build has it.</summary>
        public static readonly string[] PlayRules = { "CalmMoveDeviation", "FortRunDeviation", "RockRoundDeviation", "CalmFollowDeviation", "GrenadeBoostDeviation",
            "VeteranHealDeviation", "FortSniperDeviation", "SniperHeadshotDeviation", "SniperSteadyDeviation", "ToughSteadyDeviation", "HeavyFallDeviation",
            "GroundFireDeviation", "ProneTargetDeviation", "HeroBoostDeviation", "JeepChargeDeviation", "DriverFireDeviation" };

        enum Kind { Drag, RunHit, Howitzer, ClickHowitzer, ClickLasers, Calm }

        sealed class Row { public string name, type; public Kind kind; public bool cpuTeam; public double seconds; }

        /// <summary>What one run measured.</summary>
        sealed class Run
        {
            public double claimAt = -1, capAt = -1, dropAt = -1, resumeAt = -1, hitAt = -1, moved = -1, enemyDownAt = -1;
            public int claims, startsInFight, drops, hpLost, bodiesAtRun = -1, bodies;
            public bool alive = true, fought;
            public string Line() => $"own run {F(claimAt)} s, flag taken {F(capAt)} s, runs ended {drops} (first {F(dropAt)} s, the leader moved {F(moved, "0.0")} px until the run went on at {F(resumeAt)} s), " +
                                    $"own runs started in a fight {startsInFight}, the squad fought {(fought ? "yes" : "no")}, enemy destroyed {F(enemyDownAt)} s, HP lost {hpLost}, robots {bodies} at the start, {bodiesAtRun} at its first own run, leader {(alive ? "alive" : "destroyed")}";
        }

        static readonly string[] DragTypes = { "tough", "grunt", "psycho", "sniper", "pyro", "laser" };
        static Row[] AllRows()
        {
            var l = new List<Row>();
            foreach (var t in DragTypes) l.Add(new Row { name = "A " + t + " squad, its last robot fights a grunt hidden from its leader", type = t, kind = Kind.Drag, seconds = 60 });
            l.Add(new Row { name = "A tough squad of a computer team (not changed)", type = "tough", kind = Kind.Drag, cpuTeam = true, seconds = 60 });
            l.Add(new Row { name = "B tough squad, its own run: its robot hit at 3 s, an enemy beyond it", type = "tough", kind = Kind.RunHit, seconds = 60 });
            l.Add(new Row { name = "B grunt squad, its own run: its robot hit at 3 s, an enemy beyond it", type = "grunt", kind = Kind.RunHit, seconds = 60 });
            l.Add(new Row { name = "C tough squad, a howitzer 190 px east it cannot reach (as before)", type = "tough", kind = Kind.Howitzer, seconds = 30 });
            l.Add(new Row { name = "C light tank, a howitzer 190 px east (a vehicle: not changed)", type = "light", kind = Kind.Howitzer, seconds = 30 });
            l.Add(new Row { name = "D the player clicks the flag: tough squad under the howitzer", type = "tough", kind = Kind.ClickHowitzer, seconds = 30 });
            l.Add(new Row { name = "D the player clicks the flag: tough squad fighting lasers", type = "tough", kind = Kind.ClickLasers, seconds = 30 });
            l.Add(new Row { name = "E tough squad, calm, no enemy", type = "tough", kind = Kind.Calm, seconds = 30 });
            l.Add(new Row { name = "E grunt squad, calm, no enemy", type = "grunt", kind = Kind.Calm, seconds = 30 });
            return l.ToArray();
        }

        /// <summary>The whole check: the report, ending in PASS / FAIL lines. maps is not used (open ground; kept for the menu's shape).</summary>
        public static string Check(string settings, Func<string, string> maps = null, int runs = Runs, Action<string> progress = null)
        {
            var sb = new StringBuilder();
            var rows = AllRows();
            layouts.Clear();
            sb.AppendLine($"K438 (DV130) a robot of the player's team in a fight it can take runs to no flag by itself; {runs} runs per row (positions moved by the seed), rule off / on; " +
                          $"hit span {F(ZWorld.NoAutoFlagCalmSec, "0.0")} s; the rules of a game in Play on ({string.Join(", ", PlayRules.Where(n => typeof(ZWorld).GetField(n) != null).Select(n => n.Replace("Deviation", "")))}).");
            var res = new Dictionary<string, (List<Run> off, List<Run> on)>();
            var lines = new List<string>();
            bool exception = false;
            foreach (var row in rows)
            {
                progress?.Invoke(row.name);
                var pair = (new List<Run>(), new List<Run>());
                foreach (bool on in new[] { false, true })
                    for (int seed = 1; seed <= runs; seed++)
                    {
                        string l;
                        try { var r = Play(settings, row, seed, on); (on ? pair.Item2 : pair.Item1).Add(r); l = $"{row.name} | rule {(on ? "on " : "off")} | seed {seed} | {r.Line()}"; }
                        catch (Exception ex) { exception = true; l = $"{row.name} | rule {(on ? "on " : "off")} | seed {seed} | EXCEPTION {ex.GetType().Name}: {ex.Message}"; }
                        sb.AppendLine(l); lines.Add(l);
                    }
                res[row.name] = pair;
            }
            foreach (var kv in layouts) sb.AppendLine($"   layout {kv.Value.note}");

            bool same = true; int k = 0;                                                      // determinism: every row once more
            foreach (var row in rows)
                foreach (bool on in new[] { false, true })
                    for (int seed = 1; seed <= runs; seed++)
                    {
                        string l;
                        try { l = $"{row.name} | rule {(on ? "on " : "off")} | seed {seed} | {Play(settings, row, seed, on).Line()}"; }
                        catch (Exception ex) { l = $"{row.name} | rule {(on ? "on " : "off")} | seed {seed} | EXCEPTION {ex.GetType().Name}: {ex.Message}"; }
                        if (k >= lines.Count || lines[k] != l) same = false;
                        k++;
                    }

            sb.AppendLine();
            List<Run> On(Row r) => res[r.name].on;
            List<Run> Off(Row r) => res[r.name].off;
            bool Same(Row r)
            {
                var p = res[r.name];
                if (p.off.Count != p.on.Count || p.on.Count == 0) return false;
                for (int i = 0; i < p.on.Count; i++) if (p.off[i].Line() != p.on[i].Line()) return false;
                return true;
            }
            // A
            var drag = rows.Where(r => r.kind == Kind.Drag && !r.cpuTeam).ToList();
            int aOk = 0, aAll = 0, aOffRan = 0, aOffWon = 0, aOnWon = 0, aEarly = 0; var aBad = new List<string>();
            foreach (var row in drag)
            {
                foreach (var r in On(row))
                {
                    aAll++;
                    bool after = r.claimAt >= r.enemyDownAt - 1e-6, lost = r.bodiesAtRun >= 0 && r.bodiesAtRun < r.bodies;   // its own run after the grunt fell, or after the robot that fought it was destroyed (nobody left sees the hidden grunt)
                    bool ok = r.fought && r.enemyDownAt > 0 && r.capAt > 0 && r.startsInFight == 0 && (after || lost);
                    if (!after && lost) aEarly++;
                    if (ok) aOk++; else aBad.Add($"{row.type}: own run {F(r.claimAt)} s, enemy destroyed {F(r.enemyDownAt)} s, taken {F(r.capAt)} s, started in a fight {r.startsInFight}");
                    if (r.enemyDownAt > 0) aOnWon++;
                }
                foreach (var r in Off(row)) { if (r.claimAt >= SettleSec - 1e-6 && r.claimAt < SettleSec + 0.6 && r.startsInFight > 0) aOffRan++; if (r.enemyDownAt > 0) aOffWon++; }
            }
            sb.AppendLine(OzV11Kit.Verdict(aOk == aAll && aAll > 0 && aOffRan == aAll,
                $"P1 the case reported, every robot type with a squad: no own flag run while its squad is in a fight it can take; it fights, the hidden grunt is destroyed, then it takes the flag: {aOk}/{aAll} (of them {aEarly} ran after the robot that fought was destroyed: nobody left saw the hidden grunt); rule off: the leader ran at once out of the fight in {aOffRan}/{aAll}; hidden grunt destroyed, rule off {aOffWon} / on {aOnWon}{(aBad.Count > 0 ? "; NOT: " + string.Join("; ", aBad.Take(4)) : "")}"));
            // B
            var runHit = rows.Where(r => r.kind == Kind.RunHit).ToList();
            int bOk = 0, bAll = 0, bOff = 0; var bBad = new List<string>();
            foreach (var row in runHit)
            {
                foreach (var r in On(row))
                {
                    bAll++;
                    bool ok = r.dropAt > r.hitAt && r.dropAt <= r.hitAt + 0.6 && r.moved >= 0 && r.moved < 4.0 && r.fought && r.enemyDownAt > 0 && r.resumeAt >= r.enemyDownAt - 1e-6 && r.capAt > 0;
                    if (ok) bOk++; else bBad.Add($"{row.type}: hit {F(r.hitAt)}, ended {F(r.dropAt)}, moved {F(r.moved, "0.0")} px, enemy destroyed {F(r.enemyDownAt)}, on again {F(r.resumeAt)}, taken {F(r.capAt)}");
                }
                bOff += Off(row).Count(r => r.drops == 0 && r.capAt > 0);
            }
            sb.AppendLine(OzV11Kit.Verdict(bOk == bAll && bAll > 0 && bOff == bAll,
                $"P2 a flag run of its own under way ends at the next look once its squad is in a fight (within 0.6 s), the leader stays (under 4 px) while its robot fights and wins, then the run goes on and takes the flag: {bOk}/{bAll} (rule off: the run went on, never ended, in {bOff}/{bAll}){(bBad.Count > 0 ? "; NOT: " + string.Join("; ", bBad.Take(4)) : "")}"));
            // C
            var how = rows.Where(r => r.kind == Kind.Howitzer).ToList();
            int cSame = how.Count(Same), cCap = how.Sum(r => On(r).Count(x => x.capAt > 0)), cAll = how.Sum(r => On(r).Count);
            sb.AppendLine(OzV11Kit.Verdict(cSame == how.Count && cCap == cAll && cAll > 0,
                $"P3 fire it cannot answer (a howitzer beyond its reach) does not hold it: the same runs with the rule off and on, the flag taken by its own run: rows identical {cSame}/{how.Count}, taken {cCap}/{cAll}"));
            // computer team, vehicle
            var cpu = rows.Where(r => r.cpuTeam).ToList();
            int uSame = cpu.Count(Same);
            bool tank = Same(how.First(r => r.type == "light"));
            sb.AppendLine(OzV11Kit.Verdict(uSame == cpu.Count && cpu.Count > 0 && tank, $"P4 a computer team's squad and a vehicle not changed (the same runs with the rule off and on): computer squad {uSame}/{cpu.Count}, light tank {(tank ? "1/1" : "0/1")}"));
            // D
            var click = rows.Where(r => r.kind == Kind.ClickHowitzer || r.kind == Kind.ClickLasers).ToList();
            int dSame = click.Count(Same), dEnded = click.Sum(r => On(r).Sum(x => x.drops)), dCap = click.Sum(r => On(r).Count(x => x.capAt > 0)), dDead = click.Sum(r => On(r).Count(x => !x.alive)), dAll = click.Sum(r => On(r).Count);
            sb.AppendLine(OzV11Kit.Verdict(dSame == click.Count && dEnded == 0 && dAll > 0, $"P5 the player's click on the flag is obeyed under fire (never ended; the same runs as with the rule off): rows identical {dSame}/{click.Count}, runs ended 0 wanted: {dEnded}; the flag taken {dCap}/{dAll} (the leader destroyed on the way in {dDead}, with the rule off the same)"));
            // E
            var calm = rows.Where(r => r.kind == Kind.Calm).ToList();
            int eSame = calm.Count(Same), eCap = calm.Sum(r => On(r).Count(x => x.capAt > 0 && x.claimAt >= 0 && x.drops == 0)), eAll = calm.Sum(r => On(r).Count);
            double eWorst = calm.Max(r => On(r).Max(x => x.claimAt));
            sb.AppendLine(OzV11Kit.Verdict(eSame == calm.Count && eCap == eAll && eAll > 0, $"P6 a calm squad still runs to the flag by itself and takes it (the same runs as with the rule off): {eCap}/{eAll}, rows identical {eSame}/{calm.Count}, latest own run {F(eWorst)} s"));
            var mine = rows.Where(r => !r.cpuTeam && r.type != "light").ToList();
            int onStarts = mine.Sum(r => On(r).Sum(x => x.startsInFight)), offStarts = mine.Sum(r => Off(r).Sum(x => x.startsInFight));
            sb.AppendLine(OzV11Kit.Verdict(onStarts == 0 && offStarts > 0, $"P7 own flag runs of the player's robots started while the squad was in a fight it can take, every row: rule on {onStarts}, rule off {offStarts}"));
            sb.AppendLine(OzV11Kit.Verdict(!exception, "P8 no exception"));
            sb.AppendLine(OzV11Kit.Verdict(same, "P9 deterministic: every row run twice, the same lines"));
            return sb.ToString();
        }

        // ------------------------------------------------------------------ the hidden-enemy layout (A)
        sealed class Layout { public int ex, ey, tx, ty; public string note; }
        static readonly Dictionary<string, Layout> layouts = new Dictionary<string, Layout>();

        static string Ground(int cliffX = -1, int cliffY = -1)
        {
            var m = ZMapData.FromJson(OzV11Kit.Plain(80, 80, ("map_item:flag", FT, FT, "null")));
            if (cliffX >= 0) m.classes[cliffY * m.width + cliffX] = (int)ZClass.Impassable;
            return UnityEngine.JsonUtility.ToJson(m);
        }

        /// <summary>A: the squad first stands this long in a calm start (DV69: nothing self-started, it still fires at what is in range) so its
        /// robots take their places; the leader's first look after it decides.</summary>
        public const double SettleSec = 3.0;
        /// <summary>A: the hidden blue grunt arrives this long before the calm start ends (so the fight begins as the leader looks).</summary>
        public const double EnemyLead = 0.25;

        /// <summary>A world of row A: the ground (with the cliff tile, if any), the squad at (sx, sy), the calm start of SettleSec s.</summary>
        static ZWorld DragWorld(string settings, int seed, int cliffX, int cliffY, string type, int sx, int sy, out ZObject unit)
        {
            ZWorld.CalmStartDeviation = true; ZWorld.CalmStartSec = SettleSec;
            var w = OzV11Kit.World(settings, Ground(cliffX, cliffY), seed);
            unit = OzV11Kit.Place(w, type, sx, sy, Team.Red);
            return w;
        }

        /// <summary>For a squad type standing at (sx, sy), its robots in their places after the calm start: a spot for one blue grunt that the
        /// squad's last robot sees and has in range (12 px inside), and one cliff tile on the leader's line to it (the game's own tile walk,
        /// both ways) and off the last robot's line: the leader can neither see it nor hit it. Searched in a fixed order on the same ground,
        /// each candidate checked in a world of its own.</summary>
        static Layout DragLayout(string settings, string type, int sx, int sy, int seed)
        {
            string key = type + "@" + sx + "," + sy + "#" + seed;
            if (layouts.TryGetValue(key, out var have)) return have;
            Layout found = null;
            using (OzV11Kit.GameSwitches())
            {
                foreach (var n in PlayRules) OzV11Kit.TrySet(typeof(ZWorld), n, true);
                var w0 = DragWorld(settings, seed, -1, -1, type, sx, sy, out var u0);
                while (w0.Time < SettleSec - EnemyLead - 0.005) { w0.Tick(ZConst.TICK); w0.Events.Clear(); }
                var sq0 = OzV11Kit.Squad(u0).ToList();
                var L0 = sq0[0]; var M0 = sq0[sq0.Count - 1];
                int lx = L0.CenterX, ly = L0.CenterY, mx = M0.CenterX, my = M0.CenterY, range = M0.AttackRadius;
                var cands = new List<(int x, int y, double d)>();
                for (int dx = -120; dx <= 120; dx += 6)
                    for (int dy = -120; dy <= 120; dy += 6)
                    {
                        int ex = mx + dx, ey = my + dy;
                        double dM = Math.Sqrt((double)dx * dx + (double)dy * dy), dL = Math.Sqrt((double)(ex - lx) * (ex - lx) + (double)(ey - ly) * (ey - ly));
                        if (dM > range - 12 || dM < 40 || dL < 40 || ex > 1180 || Math.Abs(ex - (FT * 16 + 8)) < 120) continue;   // (away from the flag and the far dummy)
                        cands.Add((ex, ey, dL - dM));
                    }
                int W = w0.Map.width, H = w0.Map.height;
                foreach (var (ex, ey, _) in cands.OrderByDescending(c => c.d).ThenBy(c => c.x).ThenBy(c => c.y))
                {
                    var lline = new HashSet<(int, int)>(ZPathFinder.Bresenham(lx >> 4, ly >> 4, ex >> 4, ey >> 4, W, H).Concat(ZPathFinder.Bresenham(ex >> 4, ey >> 4, lx >> 4, ly >> 4, W, H)));
                    var mline = new HashSet<(int, int)>(ZPathFinder.Bresenham(mx >> 4, my >> 4, ex >> 4, ey >> 4, W, H).Concat(ZPathFinder.Bresenham(ex >> 4, ey >> 4, mx >> 4, my >> 4, W, H)));
                    var tiles = lline.Where(c => !mline.Contains(c) && c != (lx >> 4, ly >> 4) && c != (ex >> 4, ey >> 4) && !sq0.Any(o => (o.CenterX >> 4, o.CenterY >> 4) == c))
                                     .OrderBy(c => Math.Abs(c.Item1 * 16 + 8 - lx) + Math.Abs(c.Item2 * 16 + 8 - ly)).ToList();
                    foreach (var (tx, ty) in tiles)
                    {
                        var w = DragWorld(settings, seed, tx, ty, type, sx, sy, out var u);
                        while (w.Time < SettleSec - EnemyLead - 0.005) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                        var E = OzV11Kit.Place(w, "grunt", ex, ey, Team.Blue, 1); w.SendHold(Team.Blue, E);
                        var sq = OzV11Kit.Squad(u).ToList();
                        var L = sq[0]; var M = sq[sq.Count - 1];
                        if (!OzV11Kit.Alive(E) || Math.Abs(L.CenterX - lx) + Math.Abs(L.CenterY - ly) > 3 || Math.Abs(M.CenterX - mx) + Math.Abs(M.CenterY - my) > 3) continue;
                        if (L.CanSee(E) || L.WithinAttackRadius(E) || !M.CanSee(E) || !M.WithinAttackRadius(E)) continue;
                        found = new Layout { ex = ex, ey = ey, tx = tx, ty = ty, note = $"{type} squad at ({sx},{sy}), seed {seed}: after the calm start leader ({lx},{ly}), last robot ({mx},{my}); blue grunt at ({ex},{ey}), {Math.Sqrt((double)(ex - mx) * (ex - mx) + (double)(ey - my) * (ey - my)):0} px from the last robot, {Math.Sqrt((double)(ex - lx) * (ex - lx) + (double)(ey - ly) * (ey - ly)):0} px from the leader; the cliff tile ({tx},{ty})" };
                        break;
                    }
                    if (found != null) break;
                }
            }
            layouts[key] = found ?? new Layout { ex = -1, note = $"{type} squad at ({sx},{sy}), seed {seed}: NO layout found" };
            return layouts[key];
        }

        // ------------------------------------------------------------------ one run
        static Run Play(string settings, Row row, int seed, bool on)
        {
            var rng = new Random(seed * 7919 + 17);
            int jx = rng.Next(13) - 6, jy = rng.Next(13) - 6;
            int fx = FT * 16 + 8, fy = FT * 16 + 8;
            int sx = fx + 150 + jx, sy = fy + jy;
            Layout lay = row.kind == Kind.Drag ? DragLayout(settings, row.type, sx, sy, seed) : null;
            if (lay != null && lay.ex < 0) throw new InvalidOperationException(lay.note);
            using (OzV11Kit.GameSwitches())
            {
                foreach (var n in PlayRules) OzV11Kit.TrySet(typeof(ZWorld), n, true);
                ZWorld.CalmStartDeviation = false;
                ZWorld.NoAutoFlagUnderFireDeviation = on;
                ZObject unit = null;
                var w = lay != null ? DragWorld(settings, seed, lay.tx, lay.ty, row.type, sx, sy, out unit) : OzV11Kit.World(settings, Ground(), seed);
                if (row.cpuTeam) w.Bots.Add(new ZBot(w, Team.Red, seed) { Ignored = true });
                var flag = w.Flags[0];
                OzV11Kit.Dummy(w, 1230, 1230);
                if (unit == null) unit = OzV11Kit.Place(w, row.type, sx, sy, Team.Red);
                var squad = OzV11Kit.Squad(unit).ToList();
                ZObject enemy = null;
                if (row.kind == Kind.Howitzer || row.kind == Kind.ClickHowitzer) enemy = OzV11Kit.Place(w, "howitzer", unit.CenterX + 190, unit.CenterY, Team.Blue);
                if (row.kind == Kind.ClickLasers) { enemy = OzV11Kit.Place(w, "laser", unit.CenterX + 110, unit.CenterY, Team.Blue); w.SendHold(Team.Blue, enemy); }
                w.Events.Clear();
                var R = new Run();
                int hp0 = squad.Sum(m => m.Health); R.bodies = squad.Count;
                bool had = false, clicked = false; double dropX = 0, dropY = 0;
                var enemies = new List<ZObject>();
                if (enemy != null) enemies.AddRange(OzV11Kit.Squad(enemy));
                while (w.Time < row.seconds && !w.GameOver)
                {
                    if (lay != null && enemies.Count == 0 && w.Time >= SettleSec - EnemyLead - 0.005)
                    {                                                                         // A: the hidden grunt arrives just before the calm start ends
                        var e = OzV11Kit.Place(w, "grunt", lay.ex, lay.ey, Team.Blue, 1); w.SendHold(Team.Blue, e);
                        enemies.Add(e);
                    }
                    if (row.kind == Kind.RunHit && R.hitAt < 0 && w.Time >= 3.0 - 1e-9)
                    {                                                                         // the staged hit on the squad's last robot, and an enemy 150 px beyond it (out of the leader's reach)
                        var M = squad.LastOrDefault(OzV11Kit.Alive) ?? unit;
                        double vx = M.CenterX - unit.CenterX, vy = M.CenterY - unit.CenterY, vl = Math.Sqrt(vx * vx + vy * vy);
                        if (vl < 1) { vx = 0; vy = 1; vl = 1; }
                        var e = OzV11Kit.Place(w, "grunt", M.CenterX + (int)Math.Round(vx / vl * 150), M.CenterY + (int)Math.Round(vy / vl * 150), Team.Blue, 1);
                        e.MoveSpeed = 0; e.Damage = 0;
                        enemies.Add(e);
                        M.LastEnemyHit = w.Time; R.hitAt = w.Time;
                    }
                    if ((row.kind == Kind.ClickHowitzer || row.kind == Kind.ClickLasers) && !clicked && w.Time >= (row.kind == Kind.ClickLasers ? 2.0 : 0.0) - 1e-9)
                    {
                        clicked = true;
                        w.SendWaypoints(Team.Red, unit, new[] { ZWaypoint.Move(fx, fy, !NearHostiles(w, unit), flag.Id) });   // the player's right-click on the flag (View/PlayerInput.cs BuildWaypoint)
                    }
                    w.Tick(ZConst.TICK); w.Events.Clear();
                    if (!OzV11Kit.Alive(unit)) { R.alive = false; break; }
                    bool own = unit.Waypoints.Any(q => q.refId == flag.Id && !q.playerGiven && q.mode == WpMode.Move);
                    if (own && !had)
                    {
                        R.claims++;
                        if (R.claimAt < 0) { R.claimAt = w.Time; R.bodiesAtRun = squad.Count(OzV11Kit.Alive); } else if (R.dropAt >= 0 && R.resumeAt < 0) R.resumeAt = w.Time;
                        if (unit.SquadUnderFire) R.startsInFight++;
                    }
                    if (!own && had && flag.Owner != Team.Red) { R.drops++; if (R.dropAt < 0) { R.dropAt = w.Time; dropX = unit.PosX; dropY = unit.PosY; R.moved = 0; } }
                    had = own;
                    if (R.dropAt >= 0 && R.resumeAt < 0) R.moved = Math.Max(R.moved, Math.Sqrt((unit.PosX - dropX) * (unit.PosX - dropX) + (unit.PosY - dropY) * (unit.PosY - dropY)));
                    if (!R.fought && enemies.Count > 0 && squad.Any(m => OzV11Kit.Alive(m) && enemies.Any(e => m.AttackObject == e || (m.Waypoints.Count > 0 && (m.Waypoints[0].mode == WpMode.Attack || m.Waypoints[0].mode == WpMode.Agro) && m.Waypoints[0].refId == e.Id)))) R.fought = true;
                    if (R.enemyDownAt < 0 && enemies.Count > 0 && enemies.All(e => !OzV11Kit.Alive(e))) R.enemyDownAt = w.Time;
                    if (R.capAt < 0 && flag.Owner == Team.Red) R.capAt = w.Time;
                    if (R.capAt >= 0 && w.Time > R.capAt + 1.0) break;
                }
                R.hpLost = hp0 - squad.Where(OzV11Kit.Alive).Sum(m => m.Health);
                return R;
            }
        }

        /// <summary>View/PlayerInput.cs NearHostiles: an enemy unit within the unit's attack range + agro distance (a click there is not an attack-move).</summary>
        static bool NearHostiles(ZWorld w, ZObject u)
        {
            double r = u.AttackRadius + w.S.agro_distance;
            foreach (var o in w.Objects)
                if ((o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle || o.Kind == ZKind.Cannon) && o.Owner != Team.Neutral && o.Owner != u.Owner && !o.Destroyed
                    && (o.CenterX - u.CenterX) * (double)(o.CenterX - u.CenterX) + (o.CenterY - u.CenterY) * (double)(o.CenterY - u.CenterY) <= r * r)
                    return true;
            return false;
        }
    }
}
