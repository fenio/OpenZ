using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted check of kit b1_snipe_block. A plain map with one bridge, one fort, one robot factory, one vehicle factory and one
    /// repair station (all nobody's: no fort gun, no production; the repair station is a control, the rule does not name it). Part 1, spots: a crewed blue jeep that cannot move, shoot or be
    /// destroyed stands on a spot; a red sniper squad that cannot die, 92 px away with a clear line, has an attack order on it;
    /// 60 s per run, 20 runs per spot, with the rule off (the game before version 1.1) and on. Part 2, crossing: a blue jeep
    /// (hull cannot be destroyed, driver as in the game) drives over the bridge past the same snipers; counted where it stands when
    /// its driver is shot out.
    /// </summary>
    public static class OzV11SnipeCount
    {
        public const int Runs = 20;
        public const double Seconds = 60;
        const int Big = 1000000000;

        // tiles: bridge_vert 4 x 5 at (20,20); fort_front 10 x 12 at (40,10); robot_factory 4 x 5 at (60,40); vehicle_factory 4 x 5 at (20,60); repair 5 x 4 at (60,70)
        static string Map() => OzV11Kit.Plain(96, 96, ("building:bridge_vert", 20, 20, "null"), ("building:fort_front", 40, 10, "null"), ("building:robot_factory", 60, 40, "null"),
                                                      ("building:vehicle_factory", 20, 60, "null"), ("building:repair", 60, 70, "null"));

        /// <summary>(name, jeep centre, sniper squad centre, the rule protects the spot).</summary>
        static List<(string name, int x, int y, int sx, int sy, bool safe)> Spots(ZWorld w)
        {
            var l = new List<(string, int, int, int, int, bool)>();
            var br = w.Objects.First(o => o.IsBridge);
            int bx = br.LocX + br.WidthPix / 2, top = br.LocY, mid = br.LocY + br.HeightPix / 2;
            l.Add(("bridge: middle of the deck", bx, mid, bx + 92, mid, true));
            l.Add(("bridge: on the deck at its north end", bx, top + 16, bx + 92, top + 16, true));
            l.Add(("before the bridge: hull touching its north end", bx, top - 16, bx + 92, top - 16, true));
            l.Add(("before the bridge: hull 32 px off its north end", bx, top - 48, bx + 92, top - 48, true));
            l.Add(("before the bridge: hull 80 px off its north end", bx, top - 96, bx + 92, top - 96, true));
            l.Add(("road: hull 96 px off the north end (control)", bx, top - 112, bx + 92, top - 112, false));
            l.Add(("road: hull 160 px off the north end (control)", bx, top - 176, bx + 92, top - 176, false));
            l.Add(("beside the road: hull 64 px east of the line of the deck, 40 px off the north end (control)", bx + 96, top - 56, bx + 96, top - 148, false));
            foreach (var b in w.Objects.Where(o => o.Kind == ZKind.Building && !o.IsBridge).OrderBy(o => o.Id))
            {
                int ex, ey;
                if (!b.GetCreationMovePoint(out ex, out ey)) b.GetRepairEntrance(out ex, out ey);
                bool door = b.Type != "repair";                                                  // the rule names bridges, forts and factories: the repair station's entrance is a control
                string n = b.Type == "fort_front" ? "fort gate" : b.Type == "robot_factory" ? "robot factory door" : b.Type == "vehicle_factory" ? "vehicle factory door" : "repair station entrance (not a door the rule names)";
                l.Add(($"{n}: on the exit point{(door ? "" : " (control)")}", ex, ey, ex + 70, ey + 60, door));
                l.Add(($"{n}: 32 px out along the exit line{(door ? "" : " (control)")}", ex, ey + 32, ex + 70, ey + 92, door));
                l.Add(($"{n}: 100 px out along the exit line (control)", ex, ey + 100, ex + 70, ey + 160, false));
                l.Add(($"{n}: 96 px to the side of the exit point (control)", ex + 96, ey + 16, ex + 96, ey + 108, false));
            }
            return l;
        }

        /// <summary>The whole check. settings = default_settings. Returns the report.</summary>
        public static string Run(string settings, int runs = Runs)
        {
            var sb = new StringBuilder();
            bool had = OzV11Kit.TrySet(typeof(ZWorld), "SnipeSafeSpotDeviation", true);
            string map = Map();
            var spots = Spots(OzV11Kit.World(settings, map, 1));
            sb.AppendLine($"Part 1, spots: {runs} runs of {Seconds:0} s per spot. A driver hit = a hit the snipe roll turned into driver damage.");
            sb.AppendLine("spot | rule | driver shot out | mean s | driver hits | hull hits | snipes the rule turned into hull hits");
            int safeOutOn = 0, safeHitsOn = 0, ctrlOutOn = 0, ctrlRuns = 0, safeOutOff = 0, safeRuns = 0, ctrlOutOff = 0;
            foreach (var s in spots)
                foreach (bool rule in new[] { false, true })
                {
                    if (rule && !had) continue;
                    OzV11Kit.TrySet(typeof(ZWorld), "SnipeSafeSpotDeviation", rule);
                    int outN = 0; double sum = 0; long dh = 0, hh = 0; int held0 = OzV11Kit.Counter(typeof(ZWorld), "SnipesHeld");
                    for (int seed = 1; seed <= runs; seed++)
                    {
                        var w = OzV11Kit.World(settings, map, seed);
                        var t = OzV11Kit.Place(w, "jeep", s.x, s.y, Team.Blue);
                        t.Damage = 0; t.MoveSpeed = 0; t.RealMoveSpeed = 0; t.MaxHealth = t.Health = Big;
                        var sn = OzV11Kit.Place(w, "sniper", s.sx, s.sy, Team.Red);
                        foreach (var m in OzV11Kit.Squad(sn)) m.MaxHealth = m.Health = Big;
                        OzV11Kit.PlayerAttack(w, sn, t);
                        double end = -1;
                        while (w.Time < Seconds && end < 0)
                        {
                            w.Tick(ZConst.TICK);
                            foreach (var e in w.Events)
                            {
                                if (e.type == ZEventType.Hitscan && e.otherId == t.Id && e.flag) { if (e.flag2) dh++; else hh++; }
                                if (e.type == ZEventType.Sniped && e.objId == t.Id) end = w.Time;
                            }
                            w.Events.Clear();
                        }
                        if (end >= 0) { outN++; sum += end; }
                    }
                    int held = OzV11Kit.Counter(typeof(ZWorld), "SnipesHeld") - held0;
                    sb.AppendLine($"{s.name} | {(rule ? "on" : "off")} | {outN} of {runs} | {(outN > 0 ? sum / outN : 0):0.0} | {dh} | {hh} | {held}");
                    if (s.safe) { if (rule) { safeOutOn += outN; safeHitsOn += (int)dh; } else { safeOutOff += outN; safeRuns += runs; } }
                    else { if (rule) { ctrlOutOn += outN; ctrlRuns += runs; } else ctrlOutOff += outN; }
                }
            // part 2: a jeep crosses the bridge
            sb.AppendLine($"Part 2, crossing: a blue jeep drives over the bridge from 134 px north of it to 220 px south of it, the sniper squad stands 92 px east of the middle of the deck; {runs} runs, 60 s.");
            sb.AppendLine("rule | driver shot out | of those: hull left on the bridge or within 88 px of its ends | hull left clear of the bridge | reached the far side crewed");
            int onBridgeOn = 0, onBridgeOff = 0;
            foreach (bool rule in new[] { false, true })
            {
                if (rule && !had) continue;
                OzV11Kit.TrySet(typeof(ZWorld), "SnipeSafeSpotDeviation", rule);
                int outN = 0, onBr = 0, clear = 0, across = 0;
                for (int seed = 1; seed <= runs; seed++)
                {
                    var w = OzV11Kit.World(settings, map, seed);
                    var br = w.Objects.First(o => o.IsBridge);
                    int bx = br.LocX + br.WidthPix / 2, top = br.LocY, bot = br.LocY + br.HeightPix, mid = (top + bot) / 2;
                    var j = OzV11Kit.Place(w, "jeep", bx, top - 150, Team.Blue);
                    j.Damage = 0; j.MaxHealth = j.Health = Big;
                    var sn = OzV11Kit.Place(w, "sniper", bx + 92, mid, Team.Red);
                    foreach (var m in OzV11Kit.Squad(sn)) m.MaxHealth = m.Health = Big;
                    w.SendWaypoints(Team.Blue, j, new[] { new ZWaypoint { mode = WpMode.Move, refId = -1, x = bx, y = bot + 220, attackTo = false, playerGiven = true } });
                    OzV11Kit.PlayerAttack(w, sn, j);
                    bool sniped = false;
                    while (w.Time < Seconds && !sniped)
                    {
                        w.Tick(ZConst.TICK);
                        foreach (var e in w.Events) if (e.type == ZEventType.Sniped && e.objId == j.Id) sniped = true;
                        w.Events.Clear();
                    }
                    if (!sniped) { if (j.CenterY > bot + 48) across++; continue; }
                    outN++;
                    bool near = j.LocX < br.LocX + br.WidthPix && br.LocX < j.LocX + j.WidthPix && j.LocY < bot + 88 && top - 88 < j.LocY + j.HeightPix;
                    if (near) onBr++; else clear++;
                }
                sb.AppendLine($"{(rule ? "on" : "off")} | {outN} of {runs} | {onBr} | {clear} | {across}");
                if (rule) onBridgeOn = onBr; else onBridgeOff = onBr;
            }
            OzV11Kit.TrySet(typeof(ZWorld), "SnipeSafeSpotDeviation", true);
            if (!had) { sb.AppendLine("The rule is not installed (ZWorld.SnipeSafeSpotDeviation not found): only the 'off' rows were run."); return sb.ToString(); }
            sb.AppendLine(OzV11Kit.Verdict(safeOutOn == 0 && safeHitsOn == 0, $"rule on, protected spots: no driver shot out and no driver hit (shot out {safeOutOn}, driver hits {safeHitsOn})"));
            sb.AppendLine(OzV11Kit.Verdict(ctrlOutOn == ctrlRuns && ctrlOutOn == ctrlOutOff, $"rule on, control spots: sniping as before (shot out {ctrlOutOn} of {ctrlRuns}; rule off {ctrlOutOff})"));
            sb.AppendLine(OzV11Kit.Verdict(safeOutOff == safeRuns, $"rule off, protected spots: the driver is shot out (the fault): {safeOutOff} of {safeRuns}"));
            sb.AppendLine(OzV11Kit.Verdict(onBridgeOn == 0 && onBridgeOff > 0, $"crossing: no hull left on the bridge with the rule on (off {onBridgeOff}, on {onBridgeOn})"));
            return sb.ToString();
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Count Snipes On Bridges And In Doors (b1)")]
        static void Menu() => OzV11Kit.RunMenu("b1 snipe block", () => Run(OzV11Kit.SettingsText));
#endif
    }
}
