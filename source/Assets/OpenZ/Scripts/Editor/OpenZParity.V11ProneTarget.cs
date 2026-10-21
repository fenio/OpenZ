#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV122 "a kneeling or lying sniper is a smaller target" (the rule: Sim/ZObject.ProneTarget.cs) and the
    /// K305 counted checks (menu OpenZ/V1.1/Sniper: K305 Cover Check), all headless on the level 2 map without its start units:
    /// <list type="bullet">
    /// <item>the range: one shooter of each weapon 6 tiles from one sniper that cannot die and never moves, held standing, kneeling or
    /// lying (already down), N shots each, the rule off and on, the same seed: hits and HP (the factories build nothing, no fort gun);</item>
    /// <item>the K248b stage (3 snipers, two grunt squads, the player's attack order; the switches of a normal game, K248b's rules on,
    /// the factories build nothing):
    /// the rules' stance and a model of BS01Stance drawing it, frame by frame;</item>
    /// <item>the duel: the same stage, 20 seeds, rule off and on: how long the snipers live and how the fight ends (also with the
    /// snipers' rifles made harmless, so the grunts fire until every sniper is dead).</item>
    /// </list></summary>
    public static partial class OpenZParity
    {
        static void AddProneTargetDeviation(List<Deviation> list)
        {
            try { list.Add(DevProneTarget()); }
            catch (Exception ex) { list.Add(new Deviation { id = "DV122", change = "A kneeling or lying sniper is a smaller target", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
        }

        /// <summary>A normal game's switches (K248b's list) with K248b's rules on (DV120, the view's stance rules) and DV122 as given.</summary>
        static void PtSwitches(bool on)
        {
            SsGameSwitches();
            ZWorld.SniperSteadyDeviation = true; SniperStanceGate.On = true;
            ZWorld.ProneTargetDeviation = on;
        }

        static readonly string[] PtStanceName = { "standing", "kneeling", "lying" };

        /// <summary>The staged worlds: the factories build nothing (a long range or fight would fill the map with new units that join in);
        /// <paramref name="noForts"/>: no fort gun and no roof defender either (the range measures one shooter; the fort gun's blast is
        /// measured on its own).</summary>
        static void PtQuiet(ZWorld w, bool noForts)
        {
            foreach (var o in w.Objects) if (o.IsProducer) o.StopBuildingProduction();
            if (noForts) { ZWorld.FortGunDeviation = false; ZWorld.FortDefenderDeviation = false; }
        }

        // ------------------------------------------------------------------ the range
        internal sealed class PtShots
        {
            public bool placed; public int shots, hits, onBody, reached, overs, atCover, shortOf, zapMisses, zapMissGround, tx, ty, sx, sy, half; public long hp; public ulong trace = 1469598103934665603UL;
            public readonly List<(int x, int y)> fired = new List<(int x, int y)>();                  // the shells' and rockets' aim points, in firing order
            public double HitPct => shots > 0 ? 100.0 * hits / shots : 0;
            public double HpPerShot => shots > 0 ? (double)hp / shots : 0;
        }

        /// <summary>The weapons of the range: label, category, type ("+nades": a grunt with grenades), kind (gun = a hit roll, zap,
        /// shell = a flat shell, blast).</summary>
        internal static readonly (string label, string cat, string type, string kind)[] PtWeapons =
        {
            ("grunt rifle", "robot", "grunt", "gun"), ("psycho machine gun", "robot", "psycho", "gun"), ("sniper rifle", "robot", "sniper", "gun"),
            ("laser", "robot", "laser", "gun"), ("jeep machine gun", "vehicle", "jeep", "gun"), ("APC crew rifle", "vehicle", "apc", "gun"),
            ("gatling", "cannon", "gatling", "gun"), ("pyro flame (unchanged)", "robot", "pyro", "gun"),
            ("Electrobot zap", "robot", "electrobot", "zap"),
            ("light tank shell", "vehicle", "light", "shell"), ("medium tank shell", "vehicle", "medium", "shell"), ("heavy tank shells", "vehicle", "heavy", "shell"),
            ("gun shell", "cannon", "gun", "shell"),
            ("Tough rocket", "robot", "tough", "blast"), ("howitzer shell", "cannon", "howitzer", "blast"), ("missile cannon rocket", "cannon", "missile_cannon", "blast"),
            ("missile launcher rocket", "vehicle", "missile_launcher", "blast"), ("thrown grenade", "robot", "grunt+nades", "blast"),
        };

        /// <summary>One range case: weapon <paramref name="w"/> fires <paramref name="shots"/> times at a sniper held in
        /// <paramref name="stance"/>, DV122 <paramref name="on"/>. The sniper cannot die and never moves; the shooter cannot die.</summary>
        internal static PtShots PtRangeCase(bool on, (string label, string cat, string type, string kind) wp, ZStance stance, int shots, int seed)
        {
            var r = new PtShots();
            using (new SsSaved())
            {
                PtSwitches(on);
                ZWorld.SniperHeadshotDeviation = false;                                               // the range counts hits; a headshot would take the target off the range
                var w = ZWorld.Create(SettingsText, SsNoUnits(MapText("L02")), seed);
                w.Bots.Clear(); PtQuiet(w, true);
                foreach (var (tm, x) in new[] { (Team.Red, 16), (Team.Blue, (w.Map.width - 2) * 16) })
                { var d = w.CreateObject("robot", "grunt", x, 16, tm); d.MaxHealth = d.Health = 1000000000; d.Damage = 0; }   // keep-alive grunts far away (the match never ends)
                int bx = 41 * 16, by = 57 * 16;                                                       // K248b's stage: open dry sand
                foreach (var o in w.Objects) if (o.Kind == ZKind.MapItem && o.Type == "grenades" && Math.Abs(o.CenterX - bx) < 400 && Math.Abs(o.CenterY - by) < 400) o.KillAt = 0.01;
                w.Tick(ZConst.TICK); w.Events.Clear();
                var t = w.CreateObject("robot", "sniper", bx, by, Team.Red);
                t.MaxHealth = t.Health = 1000000000; t.Damage = 0; t.MoveSpeed = 0;
                t.HoldStanceForCheck(stance);
                bool nades = wp.type.EndsWith("+nades");
                var a = w.CreateObject(wp.cat, nades ? wp.type.Substring(0, wp.type.Length - 6) : wp.type, bx + 96, by, Team.Blue);
                a.MaxHealth = a.Health = 1000000000;
                if (nades) a.Grenades = 1 << 20;
                r.placed = true;
                w.SendWaypoints(Team.Blue, a, new[] { new ZWaypoint { mode = WpMode.Attack, refId = t.Id, x = t.CenterX, y = t.CenterY } });
                int over0 = ZWorld.ShellsOver, at0 = ZWorld.ShellsAtCover, short0 = ZWorld.ShellsShort;
                r.tx = t.CenterX; r.ty = t.CenterY; r.sx = a.CenterX; r.sy = a.CenterY; r.half = t.WidthPix >> 1;
                double limit = w.Time + shots * Math.Max(Math.Max(a.AttackSpeed, nades ? 2.3 : 0.1), wp.kind == "zap" ? 4.1 : 0.1) * 1.6 + 30.0, stopAt = -1;
                while (w.Time < limit && (stopAt < 0 || w.Time < stopAt))
                {
                    int hp0 = t.Health;
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                    {
                        if (e.objId != a.Id) continue;
                        if (e.type == ZEventType.Hitscan && r.shots < shots) { r.shots++; if (e.flag) r.hits++; r.trace = SnsFnv(r.trace, e.flag ? 1 : 2); }
                        else if (e.type == ZEventType.ElectroBolt && r.shots < shots)
                        {
                            r.shots++;
                            if (e.otherId == t.Id) r.hits++;
                            else { r.zapMisses++; if (e.otherId < 0 && Math.Abs(e.x - t.CenterX) + Math.Abs(e.y - t.CenterY) > 4) r.zapMissGround++; }
                            r.trace = SnsFnv(r.trace, e.otherId);
                        }
                        else if (e.type == ZEventType.MissileFired && r.shots < shots) { r.shots++; r.fired.Add((e.x, e.y)); r.trace = SnsFnv(SnsFnv(r.trace, e.x), e.y); }
                        else if (e.type == ZEventType.MissileExploded)
                        {
                            double dx = e.x - t.CenterX, dy = e.y - t.CenterY;
                            if (Math.Sqrt(dx * dx + dy * dy) < e.radius) r.reached++;
                            if (t.Contains(e.x, e.y)) r.onBody++;
                        }
                    }
                    r.hp += hp0 - t.Health;
                    w.Events.Clear();
                    if (r.shots >= shots && stopAt < 0) { a.Damage = 0; stopAt = w.Time + 5.0; }      // no more shots; the last shells and rockets land
                }
                if (wp.kind == "shell" || wp.kind == "blast") r.hits = r.reached;
                r.overs = ZWorld.ShellsOver - over0; r.atCover = ZWorld.ShellsAtCover - at0; r.shortOf = ZWorld.ShellsShort - short0;
            }
            return r;
        }

        /// <summary>Flat shells, the rule off against on (the same seed, so the same shells): how many burst points moved, how many of those
        /// were short of him before (on the shooter's side of his body), how many ended nearer to him than without the rule.</summary>
        static (int moved, int movedShort, int nearer, int unpaired) PtShellPairs(PtShots off, PtShots on, int halfBody)
        {
            int moved = 0, movedShort = 0, nearer = 0, n = Math.Min(off.fired.Count, on.fired.Count), unpaired = Math.Abs(off.fired.Count - on.fired.Count);
            double lx = off.tx - off.sx, ly = off.ty - off.sy, ll = Math.Sqrt(lx * lx + ly * ly), ux = ll > 0 ? lx / ll : 0, uy = ll > 0 ? ly / ll : 0;   // the line of fire, as the rule takes it
            for (int i = 0; i < n; i++)
            {
                var p = off.fired[i]; var q = on.fired[i];
                if (p == q) continue;
                moved++;
                if ((p.x - off.tx) * ux + (p.y - off.ty) * uy < -halfBody) movedShort++;
                double d0 = (double)(p.x - off.tx) * (p.x - off.tx) + (double)(p.y - off.ty) * (p.y - off.ty), d1 = (double)(q.x - off.tx) * (q.x - off.tx) + (double)(q.y - off.ty) * (q.y - off.ty);
                if (d1 < d0) nearer++;
            }
            return (moved, movedShort, nearer, unpaired);
        }

        /// <summary>A blast of <paramref name="damage"/> HP and <paramref name="radius"/> px <paramref name="dist"/> px from a held sniper
        /// (the fort gun's shell, a grenade crate): the HP it takes, DV122 as given.</summary>
        internal static int PtBlastAt(bool on, ZStance stance, int damage, int radius, int dist, bool neutral)
        {
            using (new SsSaved())
            {
                PtSwitches(on);
                var w = ZWorld.Create(SettingsText, SsNoUnits(MapText("L02")), 3);
                w.Bots.Clear(); PtQuiet(w, true);
                foreach (var (tm, x) in new[] { (Team.Red, 16), (Team.Blue, (w.Map.width - 2) * 16) })
                { var d = w.CreateObject("robot", "grunt", x, 16, tm); d.MaxHealth = d.Health = 1000000000; d.Damage = 0; }
                int bx = 41 * 16, by = 57 * 16;
                var t = w.CreateObject("robot", "sniper", bx, by, Team.Red);
                t.MaxHealth = t.Health = 1000000000; t.Damage = 0; t.MoveSpeed = 0;
                t.HoldStanceForCheck(stance);
                w.Tick(ZConst.TICK); w.Events.Clear();
                int hp0 = t.Health;
                w.AddMissileForTest(new ZMissile { team = neutral ? Team.Neutral : Team.Blue, attackerId = -1, targetId = t.Id, damage = damage, radius = radius, x = t.CenterX + dist, y = t.CenterY, explodeTime = w.Time + 0.05, debris = neutral });
                for (int i = 0; i < 20; i++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                return hp0 - t.Health;
            }
        }

        static string PtSd(double n, double p) => $"{Math.Sqrt(n * p * (1 - p)):0.0}";

        /// <summary>The range table and its verdicts.</summary>
        internal static string PtRangeReport(int shots, out bool all)
        {
            var sb = new StringBuilder(); bool ok_ = true;
            void V(bool ok, string what) { sb.AppendLine((ok ? "PASS  " : "FAIL  ") + what); ok_ &= ok; }
            bool d0 = ZWorld.ProneTargetDeviation, s0 = ZWorld.SniperSteadyDeviation;
            sb.AppendLine($"The range: {shots} shots of each weapon at one sniper 6 tiles away (cannot die, never moves), held standing / kneeling / lying (already down); seed 7; a normal game's switches with K248b's rules.");
            sb.AppendLine("weapon                     | rule OFF (before): standing / kneeling / lying              | rule ON (after): standing / kneeling / lying                | lying vs standing, kneeling vs standing (ON)");
            bool offSame = true, onStandSame = true, gunsOk = true, flameSame = true, zapOk = true, shellsOk = true, blastsOk = true, again = true;
            var lines = new List<string>();
            foreach (var wp in PtWeapons)
            {
                var off = new PtShots[3]; var on = new PtShots[3];
                for (int s = 0; s < 3; s++) { off[s] = PtRangeCase(false, wp, (ZStance)s, shots, 7); on[s] = PtRangeCase(true, wp, (ZStance)s, shots, 7); }
                string Cell(PtShots x) => wp.kind == "gun" || wp.kind == "zap" ? $"{x.hits,4}/{x.shots} hit {x.HitPct,5:0.0} %" : $"{x.HpPerShot,6:0} HP/shot ({x.onBody,3} on him)";
                double kp = on[0].hits > 0 ? (double)on[2].hits / on[0].hits : 0, kk = on[0].hits > 0 ? (double)on[1].hits / on[0].hits : 0;
                double hp2 = on[0].HpPerShot > 0 ? on[2].HpPerShot / on[0].HpPerShot : 0, hp1 = on[0].HpPerShot > 0 ? on[1].HpPerShot / on[0].HpPerShot : 0;
                string rel = wp.kind == "gun" || wp.kind == "zap" ? $"hits x{kp:0.00}, x{kk:0.00}" : $"HP x{hp2:0.00}, x{hp1:0.00}" + (wp.kind == "shell" ? $"; skimmed over {on[2].overs} / {on[1].overs} of {on[2].shots}" : "");
                sb.AppendLine($"{wp.label,-26} | {Cell(off[0])}, {Cell(off[1])}, {Cell(off[2])} | {Cell(on[0])}, {Cell(on[1])}, {Cell(on[2])} | {rel}");
                bool placed = Array.TrueForAll(off, x => x.placed && x.shots >= shots) && Array.TrueForAll(on, x => x.placed && x.shots >= shots);
                if (!placed) { V(false, $"{wp.label}: not every case fired {shots} shots ({string.Join(", ", Array.ConvertAll(on, x => x.shots.ToString()))})"); continue; }
                offSame &= off[1].trace == off[0].trace && off[2].trace == off[0].trace && off[1].hp == off[0].hp && off[2].hp == off[0].hp;
                onStandSame &= on[0].trace == off[0].trace && on[0].hp == off[0].hp && on[0].hits == off[0].hits;
                if (wp.kind == "gun" && wp.type == "pyro") flameSame &= on[1].trace == on[0].trace && on[2].trace == on[0].trace && on[2].hits == on[0].hits;
                else if (wp.kind == "gun")
                {   // the hit chance scaled: expected hits = standing hits x share, within 3 standard deviations of the binomial
                    double n = on[0].hits;
                    bool p = Math.Abs(on[2].hits - n * ZWorld.ProneHitPct / 100.0) <= 3 * Math.Sqrt(n * 0.6 * 0.4) + 1, k = Math.Abs(on[1].hits - n * ZWorld.KneelHitPct / 100.0) <= 3 * Math.Sqrt(n * 0.82 * 0.18) + 1;
                    gunsOk &= p && k;
                    lines.Add($"   {wp.label}: lying {on[2].hits} hits, expected {n * ZWorld.ProneHitPct / 100.0:0} (sd {PtSd(n, 0.6)}); kneeling {on[1].hits}, expected {n * ZWorld.KneelHitPct / 100.0:0} (sd {PtSd(n, 0.82)}){(p && k ? "" : "  <- OUT")}");
                }
                else if (wp.kind == "zap")
                {
                    double n = on[0].shots;
                    bool p = Math.Abs(on[2].hits - n * ZWorld.ProneHitPct / 100.0) <= 3 * Math.Sqrt(n * 0.6 * 0.4) + 1, k = Math.Abs(on[1].hits - n * ZWorld.KneelHitPct / 100.0) <= 3 * Math.Sqrt(n * 0.82 * 0.18) + 1;
                    zapOk &= on[0].hits == on[0].shots && p && k && on[2].zapMissGround == on[2].zapMisses && on[1].zapMissGround == on[1].zapMisses && off[2].hits == off[2].shots;
                    lines.Add($"   {wp.label}: standing {on[0].hits} of {on[0].shots} hit (Zod: always); lying {on[2].hits} (expected {n * 0.6:0}, sd {PtSd(n, 0.6)}), {on[2].zapMisses} misses into the ground beside him ({on[2].zapMissGround} drawn off his body); kneeling {on[1].hits} (expected {n * 0.82:0})");
                }
                else if (wp.kind == "shell")
                {   // only a shell that would reach him can skim over him (one that falls short lands where it lands); each shell paired with the rule off
                    double n2 = on[2].atCover, n1 = on[1].atCover;
                    bool p = n2 > 0 && Math.Abs(on[2].overs - n2 * (100 - ZWorld.ProneHitPct) / 100.0) <= 3 * Math.Sqrt(n2 * 0.6 * 0.4) + 2,
                         k = n1 > 0 && Math.Abs(on[1].overs - n1 * (100 - ZWorld.KneelHitPct) / 100.0) <= 3 * Math.Sqrt(n1 * 0.82 * 0.18) + 2;
                    var pl = PtShellPairs(off[2], on[2], on[2].half); var pk = PtShellPairs(off[1], on[1], on[1].half);
                    bool pairs = pl.unpaired == 0 && pk.unpaired == 0 && pl.moved == on[2].overs && pk.moved == on[1].overs && pl.movedShort == 0 && pk.movedShort == 0 && pl.nearer == 0 && pk.nearer == 0;
                    shellsOk &= p && k && pairs && hp2 < 0.75 && hp2 > 0.3 && hp1 < 0.9 && hp1 > 0.6 && off[2].overs == 0 && off[2].atCover == 0;
                    lines.Add($"   {wp.label}: lying: {on[2].shortOf} of {on[2].shots} fell short of him (not moved); {on[2].overs} of the {on[2].atCover} that would reach him skimmed over (expected {n2 * (100 - ZWorld.ProneHitPct) / 100.0:0}, sd {PtSd(n2, 0.6)}); " +
                              $"moved {pl.moved}, of them short before {pl.movedShort}, nearer to him {pl.nearer}; HP per shell x{hp2:0.00} of standing; kneeling: {on[1].overs} of {on[1].atCover} skimmed (expected {n1 * (100 - ZWorld.KneelHitPct) / 100.0:0}), " +
                              $"moved short {pk.movedShort}, nearer {pk.nearer}, HP x{hp1:0.00}{(p && k && pairs ? "" : "  <- OUT")}");
                }
                else
                {   // a blast: the same shots on the same spots (no draw differs), the HP scaled per blast and rounded
                    bool p = Math.Abs(hp2 - ZWorld.ProneBlastPct / 100.0) <= 0.01 && Math.Abs(hp1 - ZWorld.KneelBlastPct / 100.0) <= 0.01 && on[2].trace == on[0].trace && on[1].trace == on[0].trace;
                    blastsOk &= p;
                    lines.Add($"   {wp.label}: lying x{hp2:0.000} HP of standing (rule {ZWorld.ProneBlastPct} %), kneeling x{hp1:0.000} ({ZWorld.KneelBlastPct} %), the same {on[2].shots} bursts on the same spots: {(on[2].trace == on[0].trace ? "yes" : "NO")}");
                }
                if (wp.type == "grunt" || wp.type == "tough" || wp.type == "electrobot" || wp.type == "light")
                {
                    var re = PtRangeCase(true, wp, ZStance.Prone, shots, 7);
                    again &= re.trace == on[2].trace && re.hits == on[2].hits && re.hp == on[2].hp;
                }
            }
            foreach (var l in lines) sb.AppendLine(l);
            // blasts with no shooter: the fort gun's shell (the howitzer's 4166 HP, 52 px with the fort's splash bonus) and a grenade crate (1666, 40 px)
            var bl = new StringBuilder(); bool blOk = true;
            foreach (var (name, dmg, rad, neutral) in new[] { ("fort gun shell", 4166, 52, false), ("grenade crate blast", 1666, 40, true) })
                foreach (int dist in new[] { 0, 12, 24, 36 })
                {
                    int s0h = PtBlastAt(true, ZStance.Stand, dmg, rad, dist, neutral), k0 = PtBlastAt(true, ZStance.Kneel, dmg, rad, dist, neutral), p0 = PtBlastAt(true, ZStance.Prone, dmg, rad, dist, neutral);
                    int offP = PtBlastAt(false, ZStance.Prone, dmg, rad, dist, neutral);
                    bl.Append($"{name} {dist} px: standing {s0h}, kneeling {k0}, lying {p0} (rule off, lying: {offP}); ");
                    blOk &= offP == s0h && p0 == (int)(((long)s0h * ZWorld.ProneBlastPct + 50) / 100) && k0 == (int)(((long)s0h * ZWorld.KneelBlastPct + 50) / 100);
                }
            sb.AppendLine(bl.ToString().TrimEnd(' ', ';'));
            V(offSame, "R1 rule OFF (before): a sniper drawn kneeling or lying is hit exactly as a standing one, every weapon (the same hits, the same HP, the same rolls): the reported problem");
            V(onStandSame, "R2 rule ON: a STANDING sniper is hit exactly as before by every weapon (same rolls, same hits, same HP)");
            V(gunsOk, $"R3 direct fire (rifles, sniper rifle, machine guns, laser, jeep, APC, gatling): lying {ZWorld.ProneHitPct} % and kneeling {ZWorld.KneelHitPct} % of the standing hits, each within 3 standard deviations");
            V(flameSame, "R4 the pyro's flame is unchanged (it rolls along the ground: the same hits at every stance)");
            V(zapOk, $"R5 the Electrobot's zap: always hits a standing sniper (Zod); a lying / kneeling one {ZWorld.ProneHitPct} % / {ZWorld.KneelHitPct} % (3 sd); every miss strikes the ground beside him; rule off: always");
            V(shellsOk, $"R6 flat shells (tanks, the gun): a shell that falls short of him lands where it lands; of those that would reach him {100 - ZWorld.ProneHitPct} % skim over a lying sniper, {100 - ZWorld.KneelHitPct} % over a kneeling one (3 sd), each burst then farther from him, never nearer (every shell paired with the rule off); HP per shell lying under x0.75, kneeling under x0.90 of standing; rule off: none");
            V(blastsOk && blOk, $"R7 blasts (rocket, howitzer, missile cannon, missile launcher, thrown grenade, fort gun, crate): lying {ZWorld.ProneBlastPct} %, kneeling {ZWorld.KneelBlastPct} % of a standing sniper's HP, on the same spots; rule off: the same as standing");
            V(again, "R8 the same cases twice give the same rolls, hits and HP (deterministic)");
            V(ZWorld.ProneTargetDeviation == d0 && ZWorld.SniperSteadyDeviation == s0, "R9 the switches are put back after the cases");
            all = ok_;
            sb.AppendLine(all ? "ALL PASS" : "NOT ALL PASS");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ the stage: the rules' stance, drawn
        /// <summary>BS01Stance under DV122 (View/BS01Stance.Rules.cs: RuleSniper, GetUp, OnFire), frame by frame: it draws the rules'
        /// stance with the clip lengths of the BS-01 model; counts lie-downs, get-ups and times the drawn stance (once down or up) differs
        /// from the rules' cover stance for more than a second.</summary>
        sealed class PtDraw
        {
            enum St { Up, Down, Aim, Fire, GetUp }
            static float Len(string clip) => clip switch
            {
                "sniper_kneel_down" => 0.417f, "sniper_kneel_up" => 0.333f, "sniper_prone_down" => 0.833f, "sniper_prone_up" => 0.583f, "fire" => 0.417f, _ => 0.5f,
            };
            St st = St.Up; string pose = ""; float t, len, speed = 1f, off, lastShot = -100f;
            public int downs, ups, mismatches; public float mismatchSecs;

            public void Frame(ZObject o, float now, float dt, bool shot)
            {
                t += dt * Math.Max(0.1f, speed);
                string want = o.Stance == ZStance.Prone ? "prone" : o.Stance == ZStance.Kneel ? "kneel" : "";
                string drawn = st == St.Aim || st == St.Fire ? pose : st == St.Up ? "" : null;
                var c = o.CoverStance; string rule = c == ZStance.Prone ? "prone" : c == ZStance.Kneel ? "kneel" : "";
                if (drawn == null || drawn == rule) off = 0f;
                else { float b = off; off += dt; if (off >= 1f) { mismatchSecs += b >= 1f ? dt : off; if (b < 1f) mismatches++; } }
                switch (st)
                {
                    case St.Up: if (want.Length > 0) { pose = want; Play("sniper_" + pose + "_down", 1f); st = St.Down; downs++; } break;
                    case St.Down: if (want != pose) GetUp(o); else if (t >= len * 0.97f) { Play("aim", 1f, 99f); st = St.Aim; } break;
                    case St.Aim: if (want != pose) GetUp(o); break;
                    case St.Fire: if (want != pose) GetUp(o); else if (t >= len * 0.95f) { Play("aim", 1f, 99f); st = St.Aim; } break;
                    case St.GetUp: if (t >= len * 0.9f) { st = St.Up; speed = 1f; } break;
                }
                if (shot && now - lastShot >= 1.05f) { lastShot = now; if (st == St.Aim || st == St.Fire || (st == St.Down && t >= len * 0.6f)) { Play("fire", 1f); st = St.Fire; } }
            }

            void Play(string clip, float spd, float l = -1f) { t = 0f; len = l > 0 ? l : Len(clip); speed = spd; }
            void GetUp(ZObject o) { Play("sniper_" + pose + "_up", o.IsMoving ? 1.7f : 1f); st = St.GetUp; ups++; }
        }

        internal sealed class PtStage
        {
            public int fights, snipers, unfinished, grunts, sniperDeaths, shotsAtSnipers, hitsOnSnipers, picks, lieDowns, upsWalk, upsLinger, upsOther, flips, cycles, drawnDowns, drawnUps, mismatches, nonSniperStances;
            public int[] shotsAt = new int[3], hitsAt = new int[3], lifeN = new int[3];
            public double[] lifeSecs = new double[3];
            public double fightSecs, mismatchSecs, sniperHpLost, sniperDamage, aliveSecs, downSecs, proneSecs, kneelSecs;
            public ulong trace = 1469598103934665603UL;
            public void Add(PtStage o)
            {
                fights += o.fights; snipers += o.snipers; unfinished += o.unfinished; grunts += o.grunts; sniperDeaths += o.sniperDeaths; picks += o.picks; lieDowns += o.lieDowns;
                shotsAtSnipers += o.shotsAtSnipers; hitsOnSnipers += o.hitsOnSnipers; sniperDamage += o.sniperDamage;
                upsWalk += o.upsWalk; upsLinger += o.upsLinger; upsOther += o.upsOther; flips += o.flips; cycles += o.cycles; drawnDowns += o.drawnDowns; drawnUps += o.drawnUps;
                mismatches += o.mismatches; nonSniperStances += o.nonSniperStances; fightSecs += o.fightSecs; mismatchSecs += o.mismatchSecs; sniperHpLost += o.sniperHpLost;
                aliveSecs += o.aliveSecs; downSecs += o.downSecs; proneSecs += o.proneSecs; kneelSecs += o.kneelSecs;
                for (int i = 0; i < 3; i++) { shotsAt[i] += o.shotsAt[i]; hitsAt[i] += o.hitsAt[i]; lifeN[i] += o.lifeN[i]; lifeSecs[i] += o.lifeSecs[i]; }
                trace = SnsFnv(trace, (long)o.trace);
            }
            string Hit(int i) => shotsAt[i] > 0 ? $"{hitsAt[i]}/{shotsAt[i]} ({100.0 * hitsAt[i] / shotsAt[i]:0.0} %)" : "-";
            public string Text() =>
                $"{fights} fights, {snipers} sniper-fights, {fightSecs:0.0} s of fighting{(unfinished > 0 ? $", {unfinished} NOT finished" : "")}, {grunts} grunts killed, {sniperDeaths} snipers killed, " +
                $"sniper damage taken {sniperDamage:0} HP ({hitsOnSnipers} hits in {shotsAtSnipers} gun shots at them, {100.0 * hitsOnSnipers / Math.Max(1, shotsAtSnipers):0.0} %), HP down at the end {sniperHpLost:0} (veterans repair) | rules' stance: {picks} picks, {lieDowns} lie-downs, get-ups {upsWalk} for a walk + {upsLinger} after the fight + {upsOther} water / knockdown / throw, " +
                $"{flips} stance flips with an enemy in range, {cycles} down-up-down within 3 s; down {100 * downSecs / Math.Max(0.01, aliveSecs):0} % of the snipers' time (lying {100 * proneSecs / Math.Max(0.01, aliveSecs):0} %, kneeling {100 * kneelSecs / Math.Max(0.01, aliveSecs):0} %) " +
                $"| drawn: {drawnDowns} lie-downs, {drawnUps} get-ups, {mismatches} times the drawn stance differed from the rules' for over 1 s ({mismatchSecs:0.0} s) " +
                $"| gun shots at the snipers by their cover: standing {Hit(0)}, kneeling {Hit(1)}, lying {Hit(2)} | robots other than snipers with a stance: {nonSniperStances}";
        }

        /// <summary>One K248b staged fight (its set-up: 3 red snipers, two blue grunt squads gx px east, the player's attack order on the
        /// first squad's leader), DV122 <paramref name="on"/>; <paramref name="harmless"/>: the snipers' rifles do 1 HP and headshots are
        /// off, so the grunts fire until every sniper is dead (at most <paramref name="maxSecs"/> s).</summary>
        internal static PtStage PtFight(bool on, int gx, int seed, bool harmless = false, double maxSecs = 40.0)
        {
            var r = new PtStage { fights = 1 };
            using (new SsSaved())
            {
                PtSwitches(on);
                if (harmless) ZWorld.SniperHeadshotDeviation = false;
                var w = ZWorld.Create(SettingsText, SsNoUnits(MapText("L02")), seed);
                w.Bots.Clear(); PtQuiet(w, false);
                foreach (var (tm, x) in new[] { (Team.Red, 16), (Team.Blue, (w.Map.width - 2) * 16) })
                { var d = w.CreateObject("robot", "grunt", x, 16, tm); d.MaxHealth = d.Health = 1000000000; d.Damage = 0; }
                for (int k = 0, n = (seed * 5 + gx / 10) % 9; k < n; k++)
                { var d = w.CreateObject("robot", "grunt", 48 + 16 * k, 16, Team.Red); d.MaxHealth = d.Health = 1000000000; d.Damage = 0; }   // as K248b's stage (the ids differ from fight to fight)
                int bx = 41 * 16, by = 57 * 16;
                foreach (var o in w.Objects) if (o.Kind == ZKind.MapItem && o.Type == "grenades" && Math.Abs(o.CenterX - bx) < 400 && Math.Abs(o.CenterY - by) < 400) o.KillAt = 0.01;
                while (w.Time < 30.0) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                var sq = w.CreateRobotGroup("sniper", bx, by, Team.Red);
                var g1 = w.CreateRobotGroup("grunt", bx + gx, by - 40, Team.Blue);
                var g2 = w.CreateRobotGroup("grunt", bx + gx + 15, by + 40, Team.Blue);
                var sn = new List<ZObject> { sq }; sn.AddRange(sq.Minions);
                var gr = new List<ZObject> { g1 }; gr.AddRange(g1.Minions); gr.Add(g2); gr.AddRange(g2.Minions);
                if (harmless) foreach (var s in sn) s.Damage = 1;
                int picks0 = ZWorld.StancePicks, ld0 = ZWorld.StanceLieDowns, uw0 = ZWorld.StanceGetUpsWalk, ul0 = ZWorld.StanceGetUpsLinger, uo0 = ZWorld.StanceGetUpsOther;
                var sh0 = (int[])ZWorld.CoverShots.Clone(); var hi0 = (int[])ZWorld.CoverHits.Clone();
                var draw = new Dictionary<int, PtDraw>(); var last = new Dictionary<int, ZStance>(); var lastUp = new Dictionary<int, double>(); var downFight = new Dictionary<int, int>();
                var hp0 = new Dictionary<int, int>(); var hpPrev = new Dictionary<int, int>(); var died = new Dictionary<int, double>();
                foreach (var s in sn) { draw[s.Id] = new PtDraw(); last[s.Id] = ZStance.Stand; lastUp[s.Id] = -100; downFight[s.Id] = -1; hp0[s.Id] = hpPrev[s.Id] = s.Health; }
                double t0 = w.Time, end = -1, stop = t0 + maxSecs; bool ordered = false;
                while (w.Time < stop)
                {
                    if (!ordered) { ordered = true; if (!sq.Destroyed) w.SendWaypoints(Team.Red, sq, new[] { new ZWaypoint { mode = WpMode.Attack, refId = g1.Id, x = g1.CenterX, y = g1.CenterY, playerGiven = true } }); }
                    w.Tick(ZConst.TICK);
                    var shotBy = new HashSet<int>();
                    foreach (var e in w.Events)
                    {
                        if (e.type == ZEventType.Hitscan && draw.ContainsKey(e.objId)) shotBy.Add(e.objId);
                        if (e.type == ZEventType.Hitscan && draw.ContainsKey(e.otherId)) { r.shotsAtSnipers++; if (e.flag) r.hitsOnSnipers++; }
                        if (e.type == ZEventType.Death)
                        {
                            var v = w.GetAny(e.objId);
                            if (v != null && gr.Contains(v)) r.grunts++;
                            if (v != null && sn.Contains(v) && !died.ContainsKey(v.Id)) { died[v.Id] = w.Time; r.sniperDeaths++; }
                        }
                    }
                    bool fight = end < 0;
                    foreach (var o in w.Objects) if (o.Stance != ZStance.Stand && !(o.Kind == ZKind.Robot && o.Type == "sniper")) r.nonSniperStances++;
                    foreach (var s in sn)
                    {
                        int hpNow = Math.Max(0, s.Health);
                        if (hpPrev[s.Id] > hpNow) r.sniperDamage += hpPrev[s.Id] - hpNow;               // the damage it takes (veterans repair: the HP at the end tell less)
                        hpPrev[s.Id] = hpNow;
                        if (s.Destroyed || s.KillAt >= 0) continue;
                        bool inr = false;
                        foreach (var o in w.Objects)
                            if (o.IsUnit && !o.Destroyed && o.KillAt < 0 && o.Owner != Team.Neutral && o.Owner != s.Owner && s.CanAttackObject(o) && s.WithinAttackRadius(o)) { inr = true; break; }
                        var st = s.Stance; var was = last[s.Id];
                        if (st != was)
                        {
                            PtDebug?.Invoke($"{w.Time - t0:0.00} #{s.Id} {was} -> {st} fight {s.StanceFights} pick {s.StancePick} inRange {inr} moving {s.IsMoving} wp {(s.Waypoints.Count > 0 ? s.Waypoints[0].mode.ToString() + (s.Waypoints[0].playerGiven ? "/player" : "") + "/" + s.Waypoints[0].refId : "-")} target {(s.AttackObject != null ? s.AttackObject.Id : -1)} knocked {s.KnockedDown(w.Time)} hp {s.Health}");
                            if (was == ZStance.Stand)
                            {
                                if (w.Time - lastUp[s.Id] < ZWorld.StanceMinUpTime - 1e-6) r.cycles++;                  // down again within 3 s of getting up (the rule's own bound)
                                if (inr && fight && downFight[s.Id] == s.StanceFights) r.flips++;      // a second lie-down in the same fight with an enemy in range
                                downFight[s.Id] = s.StanceFights;
                            }
                            else if (st == ZStance.Stand) { lastUp[s.Id] = w.Time; if (inr && fight) r.flips++; }
                            else r.flips++;                                                               // kneel <-> prone directly (never expected)
                            last[s.Id] = st;
                        }
                        if (fight)
                        {
                            r.aliveSecs += ZConst.TICK;
                            if (s.CoverStance == ZStance.Prone) { r.proneSecs += ZConst.TICK; r.downSecs += ZConst.TICK; }
                            else if (s.CoverStance == ZStance.Kneel) { r.kneelSecs += ZConst.TICK; r.downSecs += ZConst.TICK; }
                        }
                        draw[s.Id].Frame(s, (float)w.Time, (float)ZConst.TICK, shotBy.Contains(s.Id));
                        r.trace = SnsFnv(SnsFnv(SnsFnv(SnsFnv(r.trace, s.LocX), s.LocY), (long)s.Stance), s.Health);
                    }
                    w.Events.Clear();
                    bool gruntsDead = gr.TrueForAll(o => o.Destroyed), snipersDead = sn.TrueForAll(o => o.Destroyed);
                    if (end < 0 && (harmless ? snipersDead : gruntsDead || snipersDead)) { end = w.Time; stop = Math.Min(stop, end + (harmless ? 0.0 : 7.0)); }
                }
                if (end < 0) { r.unfinished = 1; end = w.Time; }
                r.fightSecs = end - t0;
                r.picks = ZWorld.StancePicks - picks0; r.lieDowns = ZWorld.StanceLieDowns - ld0; r.upsWalk = ZWorld.StanceGetUpsWalk - uw0; r.upsLinger = ZWorld.StanceGetUpsLinger - ul0; r.upsOther = ZWorld.StanceGetUpsOther - uo0;
                for (int i = 0; i < 3; i++) { r.shotsAt[i] = ZWorld.CoverShots[i] - sh0[i]; r.hitsAt[i] = ZWorld.CoverHits[i] - hi0[i]; }
                foreach (var s in sn)
                {
                    r.snipers++;
                    r.sniperHpLost += hp0[s.Id] - Math.Max(0, s.Health);
                    int pi = (int)s.StancePick; r.lifeN[pi]++; r.lifeSecs[pi] += (died.TryGetValue(s.Id, out var dt0) ? dt0 : end) - t0;   // its life in this fight, by the stance of its (last) fight
                    var d = draw[s.Id]; r.drawnDowns += d.downs; r.drawnUps += d.ups; r.mismatches += d.mismatches; r.mismatchSecs += d.mismatchSecs;
                }
            }
            return r;
        }

        /// <summary>Debug only (the headless runner's "fight" command): every stance change of the rules, null otherwise.</summary>
        internal static Action<string> PtDebug;

        internal static PtStage PtMatrix(bool on, int seeds)
        {
            var all = new PtStage { fights = 0 };
            foreach (var gx in SsGx) for (int s = 1; s <= seeds; s++) all.Add(PtFight(on, gx, s));
            return all;
        }

        /// <summary>The stage check: K248b's 18 fights with the rules' stance (DV122 on) against the rule off, the drawn model, the picks.</summary>
        internal static string PtStageReport(int seeds, out bool all)
        {
            var sb = new StringBuilder(); bool ok_ = true;
            void V(bool ok, string what) { sb.AppendLine((ok ? "PASS  " : "FAIL  ") + what); ok_ &= ok; }
            bool d0 = ZWorld.ProneTargetDeviation;
            var off = PtMatrix(false, seeds); var on = PtMatrix(true, seeds); var again = PtMatrix(true, seeds);
            sb.AppendLine($"K248b's stage ({SsGx.Length} distances x {seeds} seeds; K248b's rules on, headshots on)");
            sb.AppendLine($"rule OFF (before: the stance only in the view): {off.Text()}");
            sb.AppendLine($"rule ON  (after: the rules' stance, drawn)    : {on.Text()}");
            int same = 0, n = 0;
            for (int id = 1; id <= 400; id++) for (int f = 1; f <= 25; f++) foreach (double r01 in new[] { 0.3, 0.7 })
                    {
                        n++;
                        var v = WeaponsV2Logic.PickStance(id, f, (float)r01);
                        if ((int)v == (int)ZObject.PickStance(id, f, r01)) same++;
                    }
            V(on.fights == SsGx.Length * seeds && on.unfinished == 0 && on.grunts == 6 * on.fights, $"P1 every staged fight ends with all six grunts dead ({on.grunts} of {6 * on.fights}; rule off {off.grunts})");
            V(on.picks <= on.snipers * 1.1 && on.picks > 0, $"P2 one stance per fight, picked by the rules: {on.picks} picks for {on.snipers} sniper-fights");
            V(on.lieDowns <= on.picks && on.lieDowns > 0, $"P3 down at most once a fight: {on.lieDowns} lie-downs in {on.picks} fights");
            V(on.flips == 0 && on.cycles == 0, $"P4 no lying down / standing up while an enemy is in range: {on.flips} stance flips, {on.cycles} down-up-down within 3 s");
            V(on.mismatches == 0 && on.drawnDowns == on.lieDowns, $"P5 the view draws the rules' stance: {on.drawnDowns} drawn lie-downs for {on.lieDowns} of the rules, {on.mismatches} times the drawn stance differed from the rules' for over 1 s");
            V(same == n, $"P6 the rules pick with the view's old odds and generator: {same} of {n} picks the same as WeaponsV2Logic.PickStance");
            var p7 = PtHeldP7(seeds);                                                             // P7 counted on a held-stance stage (OpenZParity.V11ProneTargetHeld.cs): the cover rule itself; the natural fights above may put no lying sniper under fire (K248d), their counts by cover stay in the line "rule ON" above
            V(p7.ok, p7.verdict); sb.Append(p7.detail);
            sb.AppendLine($"(for the record, P8: the grunts' gun shots at the snipers hit {100.0 * on.hitsOnSnipers / Math.Max(1, on.shotsAtSnipers):0.0} % with the rule ({on.hitsOnSnipers} of {on.shotsAtSnipers}), {100.0 * off.hitsOnSnipers / Math.Max(1, off.shotsAtSnipers):0.0} % without ({off.hitsOnSnipers} of {off.shotsAtSnipers}); damage taken {on.sniperDamage:0} HP against {off.sniperDamage:0}; few shots: the grunts die fast here, the range and the duel measure it)");
            V(on.nonSniperStances == 0 && off.nonSniperStances == 0, $"P9 only snipers ever take a stance ({on.nonSniperStances} ticks of another robot with one)");
            V(again.trace == on.trace && again.Text() == on.Text(), "P10 the same fights twice: the same positions, stances and health every tick (deterministic)");
            V(ZWorld.ProneTargetDeviation == d0, "P11 the switch is put back after the cases");
            all = ok_;
            sb.AppendLine(all ? "ALL PASS" : "NOT ALL PASS");
            return sb.ToString();
        }

        /// <summary>The duel: 3 snipers against 2 grunt squads (K248b's stage), <paramref name="seeds"/> seeds (distance 150 / 160 / 180 px in
        /// turn), rule off and on: (a) the normal fight; (b) the snipers' rifles harmless (1 HP, no headshots): the grunts fire until every
        /// sniper is dead.</summary>
        internal static string PtDuelReport(int seeds, out bool all)
        {
            var sb = new StringBuilder(); bool ok_ = true;
            void V(bool ok, string what) { sb.AppendLine((ok ? "PASS  " : "FAIL  ") + what); ok_ &= ok; }
            string Line(string what, List<PtStage> l)
            {
                int dead = 0, lost = 0, won = 0, unf = 0; double hp = 0, secs = 0;
                int sh = 0, hi = 0;
                foreach (var x in l) { dead += x.sniperDeaths; hp += x.sniperDamage; secs += x.fightSecs; sh += x.shotsAtSnipers; hi += x.hitsOnSnipers; if (x.sniperDeaths == x.snipers) lost++; else if (x.grunts == 6) won++; else unf++; }
                return $"{what}: {l.Count} fights: snipers won {won}, lost {lost}, open {unf}; {dead} of {3 * l.Count} snipers killed, {hp / l.Count:0} HP of damage taken a fight ({hi} hits in {sh} gun shots at them, {100.0 * hi / Math.Max(1, sh):0.0} %), {secs / l.Count:0.0} s a fight";
            }
            var a0 = new List<PtStage>(); var a1 = new List<PtStage>(); var b0 = new List<PtStage>(); var b1 = new List<PtStage>();
            for (int s = 1; s <= seeds; s++)
            {
                int gx = SsGx[s % SsGx.Length];
                a0.Add(PtFight(false, gx, 100 + s)); a1.Add(PtFight(true, gx, 100 + s));
                b0.Add(PtFight(false, gx, 100 + s, true, 600)); b1.Add(PtFight(true, gx, 100 + s, true, 600));
            }
            sb.AppendLine(Line("(a) normal fight, rule OFF", a0));
            sb.AppendLine(Line("(a) normal fight, rule ON ", a1));
            double l0 = 0, l1 = 0, d0s = 0, d1s = 0; int k0 = 0, k1 = 0;
            foreach (var x in b0) { l0 += x.fightSecs; k0 += x.sniperDeaths; d0s += x.downSecs / Math.Max(0.01, x.aliveSecs); }
            foreach (var x in b1) { l1 += x.fightSecs; k1 += x.sniperDeaths; d1s += x.downSecs / Math.Max(0.01, x.aliveSecs); }
            sb.AppendLine($"(b) harmless snipers, until all are dead: rule OFF {l0 / seeds:0.0} s until the last sniper dies ({k0} of {3 * seeds} died); rule ON {l1 / seeds:0.0} s ({k1} died), x{(l0 > 0 ? l1 / l0 : 0):0.00}; " +
                          $"the snipers were down (kneeling / lying, covered) {100 * d1s / seeds:0} % of their time with the rule (drawn down without cover before)");
            var hb = new int[3]; var sb3 = new int[3];
            foreach (var x in b1) for (int i = 0; i < 3; i++) { hb[i] += x.hitsAt[i]; sb3[i] += x.shotsAt[i]; }
            sb.AppendLine($"    (b) rule ON: grunt shots at the snipers by their cover: standing {hb[0]}/{sb3[0]}, kneeling {hb[1]}/{sb3[1]}, lying {hb[2]}/{sb3[2]}");
            var ln0 = new int[3]; var ls0 = new double[3]; var ln1 = new int[3]; var ls1 = new double[3];
            foreach (var x in b0) for (int i = 0; i < 3; i++) { ln0[i] += x.lifeN[i]; ls0[i] += x.lifeSecs[i]; }
            foreach (var x in b1) for (int i = 0; i < 3; i++) { ln1[i] += x.lifeN[i]; ls1[i] += x.lifeSecs[i]; }
            int nAll0 = ln0[0] + ln0[1] + ln0[2], nAll1 = ln1[0] + ln1[1] + ln1[2];
            double life0 = (ls0[0] + ls0[1] + ls0[2]) / Math.Max(1, nAll0), life1 = (ls1[0] + ls1[1] + ls1[2]) / Math.Max(1, nAll1);
            string L(int i) => ln1[i] > 0 ? $"{ls1[i] / ln1[i]:0.0} s ({ln1[i]} snipers)" : "-";
            sb.AppendLine($"    (b) a sniper's life under that fire: rule OFF {life0:0.0} s on average ({nAll0} snipers; the view drew some kneeling / lying, hit as standing); rule ON {life1:0.0} s (x{(life0 > 0 ? life1 / life0 : 0):0.00}): " +
                          $"standing {L(0)}, kneeling {L(1)}, lying {L(2)}");
            int won0 = a0.FindAll(x => x.grunts == 6).Count, won1 = a1.FindAll(x => x.grunts == 6).Count;
            double hp0 = 0, hp1 = 0; foreach (var x in a0) hp0 += x.sniperDamage; foreach (var x in a1) hp1 += x.sniperDamage;
            V(won1 >= won0, $"D1 the snipers win at least as often with the rule ({won1} of {seeds}; off {won0})");
            V(hp1 < hp0, $"D2 they take less damage in the normal fight ({hp1 / seeds:0} HP a fight; off {hp0 / seeds:0})");
            V(b1.TrueForAll(x => x.unfinished == 0) && b0.TrueForAll(x => x.unfinished == 0) && l1 > l0 * 1.05, $"D3 under the same fire they live longer: {l1 / seeds:0.0} s against {l0 / seeds:0.0} s until the last one dies (every fight ended)");
            V(l1 < l0 * 1.8, $"D4 but not untouchable: at most 1.8 times as long (x{(l0 > 0 ? l1 / l0 : 0):0.00})");
            all = ok_;
            sb.AppendLine(all ? "ALL PASS" : "NOT ALL PASS");
            return sb.ToString();
        }

        static Deviation DevProneTarget()
        {
            string measured; bool pass;
            try
            {
                var grunt = PtWeapons[0]; var tough = PtWeapons[13];
                var gs = PtRangeCase(true, grunt, ZStance.Stand, 300, 7); var gp = PtRangeCase(true, grunt, ZStance.Prone, 300, 7); var go = PtRangeCase(false, grunt, ZStance.Prone, 300, 7);
                var ts = PtRangeCase(true, tough, ZStance.Stand, 40, 7); var tp = PtRangeCase(true, tough, ZStance.Prone, 40, 7);
                double kp = gs.hits > 0 ? (double)gp.hits / gs.hits : 0, kb = ts.HpPerShot > 0 ? tp.HpPerShot / ts.HpPerShot : 0;
                pass = go.hits == gs.hits && kp > 0.45 && kp < 0.75 && Math.Abs(kb - ZWorld.ProneBlastPct / 100.0) < 0.02;
                measured = $"300 grunt shots at a sniper 6 tiles away: standing {gs.hits} hits, lying {gp.hits} (x{kp:0.00}; rule off, lying: {go.hits}); 40 Tough rockets: lying {tp.HpPerShot:0} HP a rocket against {ts.HpPerShot:0} standing (x{kb:0.00})";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            return new Deviation
            {
                id = "DV122", change = "A kneeling or lying sniper is a smaller target: the stance is a rule, the view draws it",
                zod = "no stance: one hit roll against damage_chance for every robot (zobject.cpp:2187), the same blast for every body (zserver.cpp:1043-1112); OpenZ's kneel / prone was drawn only",
                openz = "Sim/ZObject.ProneTarget.cs: a sniper on foot takes stand / kneel / prone by rule (once a fight, down after 0.3 s still with a target, up only for a real walk, down until 6 s without a target; never in water); " +
                        $"guns hit a kneeling / lying sniper with {ZWorld.KneelHitPct} % / {ZWorld.ProneHitPct} % of their hit chance (not the pyro's flame), the Electrobot's zap lands on him as often (else into the ground beside him), so does a flat tank / gun shell that would reach him (else it skims {ZWorld.ShellOverPx} px further on, behind him; one that falls short is not moved), " +
                        $"blasts take {ZWorld.KneelBlastPct} % / {ZWorld.ProneBlastPct} % of their HP; headshots stay 15 % of the hits. The view draws the rules' stance (View/BS01Stance.Rules.cs, View/SniperCoverView.cs)",
                measured = measured,
                toggle = "ZWorld.ProneTargetDeviation: View/ZGame.cs sets it at every start (off in 'Zod exact'); off in headless worlds, in the Zod rows and in the version 1.1 counted checks unless they set it",
                status = pass ? DevActive : DevCheck,
            };
        }
    }

#if UNITY_EDITOR
    /// <summary>K305 menus: the counted checks (outside Play) and, in Play, a stage of three snipers under fire (one lying, one kneeling,
    /// one standing) with the hit counters.</summary>
    public static class OzV11ProneTargetMenus
    {
        [UnityEditor.MenuItem("OpenZ/V1.1/Sniper: K305 Cover Check (quick: range 200 shots, stage, duel)")]
        static void Menu() => OzV11Kit.RunMenu("Sniper: K305 Cover Check (quick)", () =>
            OpenZParity.PtRangeReport(200, out _) + "\n" + OpenZParity.PtStageReport(6, out _) + "\n" + OpenZParity.PtDuelReport(20, out _));

        [UnityEditor.MenuItem("OpenZ/V1.1/Sniper: K305 Cover Check (full: range 1000 shots, long)")]
        static void MenuFull() => OzV11Kit.RunMenu("Sniper: K305 Cover Check (full)", () =>
            OpenZParity.PtRangeReport(1000, out _) + "\n" + OpenZParity.PtStageReport(6, out _) + "\n" + OpenZParity.PtDuelReport(20, out _));

        static readonly List<int> staged = new List<int>();
        static bool headshotsBefore = true, stageOn;

        /// <summary>Play: three of the player's snipers on dry open ground near the middle of the screen, held lying, kneeling and standing
        /// (they cannot die), and two enemy grunt squads 13 m east that cannot die either, so the fire goes on; headshots are off for the
        /// stage (back on with the end menu). Counters start again.</summary>
        [UnityEditor.MenuItem("OpenZ/V1.1/Sniper: K305 Stage snipers under fire (Play)")]
        static void Stage()
        {
            var g = ZGame.I;
            if (!UnityEngine.Application.isPlaying || g == null || g.World == null) { UnityEngine.Debug.LogWarning("[OpenZ V1.1] K305 stage: start a level in Play first."); return; }
            var w = g.World; var cam = UnityEngine.Camera.main;
            if (!cam) { UnityEngine.Debug.LogWarning("[OpenZ V1.1] K305 stage: no main camera."); return; }
            EndStage(false);
            var ray = cam.ViewportPointToRay(new UnityEngine.Vector3(0.5f, 0.5f, 0f));
            var c = cam.transform.position; var plane = new UnityEngine.Plane(UnityEngine.Vector3.up, new UnityEngine.Vector3(0f, g.GroundY(c.x, c.z), 0f));
            if (!plane.Raycast(ray, out float dist)) { UnityEngine.Debug.LogWarning("[OpenZ V1.1] K305 stage: the camera does not look at the ground."); return; }
            var px = ZGame.WorldToPx(ray.GetPoint(dist));
            int bx = -1, by = -1;
            for (int r = 0; r <= 14 && bx < 0; r++)
                for (int dy = -r; dy <= r && bx < 0; dy++)
                    for (int dx = -r; dx <= r && bx < 0; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                        int tx = (px.x >> 4) - 4 + dx, ty = (px.y >> 4) + dy;
                        if (Fits(w, tx, ty)) { bx = tx * 16; by = ty * 16; }
                    }
            if (bx < 0) { UnityEngine.Debug.LogWarning("[OpenZ V1.1] K305 stage: no dry open ground without units near the middle of the screen; move the camera to open sand."); return; }
            var enemy = g.player == Team.Red ? Team.Blue : Team.Red;
            headshotsBefore = ZWorld.SniperHeadshotDeviation; ZWorld.SniperHeadshotDeviation = false; stageOn = true;
            var sq = w.CreateRobotGroup("sniper", bx, by, g.player);
            var g1 = w.CreateRobotGroup("grunt", bx + 104, by - 24, enemy);
            var g2 = w.CreateRobotGroup("grunt", bx + 112, by + 24, enemy);
            var snipers = new List<ZObject> { sq }; snipers.AddRange(sq.Minions);
            var held = new[] { ZStance.Prone, ZStance.Kneel, ZStance.Stand };
            for (int i = 0; i < snipers.Count; i++) { snipers[i].MaxHealth = snipers[i].Health = 1000000000; snipers[i].HoldStanceForCheck(held[i % 3]); }
            foreach (var o in new[] { g1, g2 }) { o.MaxHealth = o.Health = 1000000000; foreach (var m in o.Minions) m.MaxHealth = m.Health = 1000000000; }
            foreach (var o in new[] { sq, g1, g2 }) { g.SpawnUnitView(o); staged.Add(o.Id); foreach (var m in o.Minions) { g.SpawnUnitView(m); staged.Add(m.Id); } }
            ZWorld.ResetCoverCounters(); BS01Stance.RuleCounts.Clear(); BS01Stance.RuleDownsDrawn = BS01Stance.RuleUpsDrawn = BS01Stance.RuleMismatches = 0; BS01Stance.RuleMismatchSecs = 0f; SniperCoverView.ZapMissesDrawn = 0;
            UnityEngine.Debug.Log($"[OpenZ V1.1] K305 stage: 3 snipers of {g.player} at tile {bx >> 4},{by >> 4} (#{snipers[0].Id} held LYING, #{(snipers.Count > 1 ? snipers[1].Id : -1)} KNEELING, #{(snipers.Count > 2 ? snipers[2].Id : -1)} STANDING; they cannot die), " +
                                  $"two {enemy} grunt squads 13 m east (cannot die); headshots off for the stage; DV122 {(ZWorld.ProneTargetDeviation ? "ON" : "OFF (the rule is off: 'Zod exact'?)")}. " +
                                  "Watch 30 s, then menu OpenZ/V1.1/Sniper: K305 Report stance and hits (Play); OpenZ/V1.1/Sniper: K305 End stage (Play) takes the units away.");
        }

        [UnityEditor.MenuItem("OpenZ/V1.1/Sniper: K305 Report stance and hits (Play)")]
        static void Report()
        {
            var w = ZGame.I ? ZGame.I.World : null;
            var sb = new StringBuilder();
            string Hit(int i) => ZWorld.CoverShots[i] > 0 ? $"{ZWorld.CoverHits[i]}/{ZWorld.CoverShots[i]} hits ({100.0 * ZWorld.CoverHits[i] / ZWorld.CoverShots[i]:0.0} %)" : "no shots";
            sb.AppendLine($"[OpenZ V1.1] K305: DV122 {(ZWorld.ProneTargetDeviation ? "ON" : "off")} (since the last stage)");
            sb.AppendLine($"  gun shots at snipers by their cover: standing {Hit(0)}, kneeling {Hit(1)}, lying {Hit(2)} (the rule: kneeling {ZWorld.KneelHitPct} %, lying {ZWorld.ProneHitPct} % of the standing rate)");
            sb.AppendLine($"  zaps at covered snipers {ZWorld.ZapsAtCover}, missed {ZWorld.ZapsMissed} (drawn into the ground {SniperCoverView.ZapMissesDrawn}); flat shells at covered snipers that would reach them {ZWorld.ShellsAtCover}, skimmed over {ZWorld.ShellsOver} (short of them, not moved: {ZWorld.ShellsShort}); blasts on covered snipers {ZWorld.BlastsAtCover}, HP kept {ZWorld.BlastHpSaved}");
            sb.AppendLine($"  the rules' stance: picks {ZWorld.StancePicks}, lie-downs {ZWorld.StanceLieDowns}, get-ups for a walk {ZWorld.StanceGetUpsWalk}, after the fight {ZWorld.StanceGetUpsLinger}, water / knockdown / throw {ZWorld.StanceGetUpsOther}");
            sb.AppendLine($"  drawn: lie-downs {BS01Stance.RuleDownsDrawn}, get-ups {BS01Stance.RuleUpsDrawn}, the drawn stance differed from the rules' for over 1 s: {BS01Stance.RuleMismatches} times ({BS01Stance.RuleMismatchSecs:0.0} s; expected 0)");
            if (w != null)
                foreach (var o in w.Objects)
                    if (o.Kind == ZKind.Robot && o.Type == "sniper" && !o.Destroyed)
                    {
                        BS01Stance.RuleCounts.TryGetValue(o.Id, out var rc);
                        var v = ZGame.I.Views != null && ZGame.I.Views.TryGetValue(o.Id, out var uv) ? uv : null;
                        string drawn = v && v.Stance ? (v.Stance.Busy ? v.Stance.Pose : "standing") : "?";
                        sb.AppendLine($"    sniper #{o.Id} ({o.Owner}): rules {o.Stance} (cover {o.CoverStance}{(o.StanceHeldForCheck ? ", held by the stage" : "")}), drawn {drawn}; drawn lie-downs {rc.x}, get-ups {rc.y}, mismatches {rc.z}");
                    }
            UnityEngine.Debug.Log(sb.ToString());
        }

        [UnityEditor.MenuItem("OpenZ/V1.1/Sniper: K305 End stage (Play)")]
        static void End() => EndStage(true);

        static void EndStage(bool log)
        {
            var w = ZGame.I ? ZGame.I.World : null;
            int removed = 0;
            if (w != null) foreach (var id in staged) { var o = w.GetAny(id); if (o != null && !o.Destroyed && o.KillAt < 0) { o.ReleaseStanceForCheck(); o.KillAt = w.Time; removed++; } }
            staged.Clear();
            if (stageOn) { ZWorld.SniperHeadshotDeviation = headshotsBefore; stageOn = false; }
            if (log) UnityEngine.Debug.Log($"[OpenZ V1.1] K305 stage ended: {removed} units taken away, headshots {(ZWorld.SniperHeadshotDeviation ? "ON" : "off")} again.");
        }

        /// <summary>The stage's block (tiles tx-1..tx+9, ty-3..ty+3) is walkable dry ground and no other unit is within 200 px.</summary>
        static bool Fits(ZWorld w, int tx, int ty)
        {
            for (int k = -1; k <= 9; k++)
                for (int j = -3; j <= 3; j++)
                {
                    if (!w.Path.TilePassable(tx + k, ty + j, true)) return false;
                    var cl = w.Map.ClassAt(tx + k, ty + j);
                    if (cl == ZClass.Water || cl == ZClass.DeepWater) return false;
                }
            int sx = tx * 16 + 8, sy = ty * 16 + 8;
            foreach (var o in w.Objects)
                if (!o.Destroyed && o.KillAt < 0 && o.IsUnit && (double)(o.CenterX - sx - 56) * (o.CenterX - sx - 56) + (double)(o.CenterY - sy) * (o.CenterY - sy) <= 200.0 * 200.0) return false;
            return true;
        }
    }
#endif
}
#endif
