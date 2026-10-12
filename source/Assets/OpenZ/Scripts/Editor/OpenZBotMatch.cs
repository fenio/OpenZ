using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Bot vs bot on a real map, headless (the same sim the game runs, 100 Hz ticks): logs every zone capture, every unit
    /// produced (elapsed time checked against Zod's build-time formula at completion), eliminations and the winner, plus
    /// </summary>
    public static class OpenZBotMatch
    {
        public class Result { public string file; public Team winner; public double endTime; public int captures, built, badBuild; public string summary; }

        [MenuItem("OpenZ/Bot vs Bot (L02, headless)")]
        public static string RunMenu() => Run("L02", 1, 3600).summary;

        public static Result Run(string lv, int seed, double maxSeconds)
        {
            var w = ZWorld.Create(Resources.Load<TextAsset>("OpenZ/default_settings").text, Resources.Load<TextAsset>($"OpenZ/Maps/{lv}_grid").text, seed, ZGame.ParkedStartOn(lv));   // DV32 as in the game
            var red = new ZBot(w, Team.Red, seed); var blue = new ZBot(w, Team.Blue, seed + 101);
            w.Bots.Add(red); w.Bots.Add(blue);
            var caps = new List<string>(); var built = new List<string>(); var elim = new List<string>(); var mins = new List<string>();
            var perTeamBuilt = new Dictionary<Team, Dictionary<string, int>> { { Team.Red, new Dictionary<string, int>() }, { Team.Blue, new Dictionary<string, int>() } };
            int badBuild = 0, nCaps = 0, nBuilt = 0; double nextMin = 60; Team winner = Team.Neutral; double end = -1;
            string exception = null;
            // per building: build start and first scheduled end; a later change (flags, owner, damage) re-scales from the start
            var track = new Dictionary<int, (double binit, double bfinal, bool rescaled)>();
            int grenades = 0, entered = 0, fortEntries = 0;
            try
            {
                while (w.Time < maxSeconds)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                    {
                        switch (e.type)
                        {
                            case ZEventType.FlagCaptured:
                            {
                                var f = w.GetAny(e.objId); var z = w.ZoneOf(f); nCaps++;
                                caps.Add($"| {Clock(w.Time)} | Z{z?.Id} flag ({f.LocX / 16},{f.LocY / 16}) | {(Team)e.radius} -> **{(Team)(int)e.value}** | {Fr(w, Team.Red)} / {Fr(w, Team.Blue)} |");
                                break;
                            }
                            case ZEventType.UnitBuilt: case ZEventType.CannonStored:
                            {
                                var b = e.type == ZEventType.UnitBuilt ? w.GetAny(e.otherId) : w.GetAny(e.objId);
                                var u = e.type == ZEventType.UnitBuilt ? w.GetAny(e.objId) : null;
                                string type = u != null ? u.Type : "cannon (stored)";
                                // formula at completion: base x (1 - 0.5 x flag share) x (1 + 1.25 x (1 - hp/max)); a mid-build change re-scales from the start
                                string unitType = u != null ? u.Type : (b.CurUnit ?? "?");
                                double expected = b.BuildTimeModified(w.S.Unit(u != null ? u.Type : BuiltCannonType(b))?.build_time ?? 0);
                                bool rescaled = track.TryGetValue(b.Id, out var trk) && trk.rescaled;
                                // exact when nothing changed; after a mid-build re-scale Zod completes on the first tick past the new end
                                bool ok = u == null || Math.Abs(e.value - expected) <= 0.011 || (rescaled && e.value >= expected - 0.011);
                                if (!ok) badBuild++;
                                nBuilt++;
                                var d = perTeamBuilt[b.Owner]; d[type] = d.TryGetValue(type, out var c) ? c + 1 : 1;
                                built.Add($"| {Clock(w.Time)} | {b.Owner} | {b.Type} ({b.LocX / 16},{b.LocY / 16}) L{b.Level} | {type}{(u != null && u.Minions.Count > 0 ? " x" + (u.Minions.Count + 1) : "")} | {e.value:F2} s | {(u != null ? expected.ToString("F2") + " s" : "-")} | {(ok ? (rescaled && Math.Abs(e.value - expected) > 0.011 ? "ok (re-scaled mid-build)" : "ok") : "**MISMATCH**")} |");
                                break;
                            }
                            case ZEventType.GrenadesPicked: grenades++; break;
                            case ZEventType.Entered: entered++; break;
                            case ZEventType.TeamEliminated:
                                elim.Add($"- {Clock(w.Time)}: **{(Team)(int)e.value} eliminated** (fort {w.GetAny(e.objId)?.Type})");
                                break;
                            case ZEventType.FortEntered:
                                fortEntries++;
                                elim.Add($"- {Clock(w.Time)}: {w.GetAny(e.objId)?.Type} ({w.GetAny(e.objId)?.Owner}) walked into the {w.GetAny(e.otherId)?.Owner} fort");
                                break;
                            case ZEventType.GameOver:
                                winner = e.value < 0 ? Team.Neutral : (Team)(int)e.value; end = w.Time;
                                break;
                        }
                    }
                    w.Events.Clear();
                    foreach (var b in w.Objects)
                    {
                        if (!b.IsProducer || !b.Building) continue;
                        if (!track.TryGetValue(b.Id, out var tr) || tr.binit != b.BInit) track[b.Id] = (b.BInit, b.BFinal, false);
                        else if (Math.Abs(tr.bfinal - b.BFinal) > 1e-6) track[b.Id] = (tr.binit, b.BFinal, true);
                    }
                    if (w.Time >= nextMin)
                    {
                        nextMin += 60;
                        mins.Add($"| {Clock(w.Time)} | {w.UnitCount(Team.Red)} | {w.UnitCount(Team.Blue)} | {w.Flags.Count(f => f.Owner == Team.Red)} | {w.Flags.Count(f => f.Owner == Team.Blue)} | {red.Orders} / {blue.Orders} |");
                    }
                    if (w.GameOver && w.Time > end + 2) break;
                }
            }
            catch (Exception ex) { exception = ex.ToString(); }

            var sb = new StringBuilder();
            sb.AppendLine($"# OpenZ bot vs bot, {lv}, seed {seed}");
            sb.AppendLine();
            sb.AppendLine($"Headless Zod sim (100 Hz), both teams driven by the zbot.cpp port. Generated {DateTime.Now:yyyy-MM-dd HH:mm}.");
            sb.AppendLine();
            sb.AppendLine(exception != null ? $"**EXCEPTION at {Clock(w.Time)}:** `{exception.Split('\n')[0]}`" :
                          end >= 0 ? $"**Winner: {winner}** at {Clock(end)} ({end:F0} s game time; {end / 4 / 60:F1} min at 4x)." : $"**No winner** within {maxSeconds:F0} s.");
            sb.AppendLine($"Captures {nCaps}, units produced {nBuilt}, production time mismatches {badBuild}, grenade crates picked up {grenades}, vehicles/cannons entered {entered}, fort entries {fortEntries}, bot orders red {red.Orders} / blue {blue.Orders}, bot build changes red {red.BuildChanges} / blue {blue.BuildChanges}.");
            sb.AppendLine();
            sb.AppendLine("## Eliminations"); sb.AppendLine(elim.Count > 0 ? string.Join("\n", elim) : "- none"); sb.AppendLine();
            sb.AppendLine("## Zone captures"); sb.AppendLine("| Time | Zone | Owner change | Flag share red / blue |"); sb.AppendLine("|---|---|---|---|"); foreach (var c in caps) sb.AppendLine(c); sb.AppendLine();
            sb.AppendLine("## Production (elapsed vs Zod formula at completion)"); sb.AppendLine("| Time | Team | Building | Unit | Elapsed | Formula | Check |"); sb.AppendLine("|---|---|---|---|---|---|---|"); foreach (var b in built) sb.AppendLine(b); sb.AppendLine();
            sb.AppendLine("## Units built per team"); foreach (var kv in perTeamBuilt) sb.AppendLine($"- {kv.Key}: {string.Join(", ", kv.Value.Select(x => x.Key + " " + x.Value))}"); sb.AppendLine();
            sb.AppendLine("## Per minute"); sb.AppendLine("| Time | Red units | Blue units | Red flags | Blue flags | Bot orders red / blue |"); sb.AppendLine("|---|---|---|---|---|---|"); foreach (var m in mins) sb.AppendLine(m); sb.AppendLine();
            sb.AppendLine("## Bot production decisions"); foreach (var l in red.Log.Concat(blue.Log).Take(60)) sb.AppendLine("- " + l);
            string file = Path.Combine(OpenZPaths.Records, $"bot_match_{lv}_seed{seed}.md");
            File.WriteAllText(file, sb.ToString());
            var summary = (exception != null ? "EXCEPTION " + exception.Split('\n')[0] + " " : "") + $"{lv} seed {seed}: winner {winner} at {Clock(end)}, captures {nCaps}, built {nBuilt}, build mismatches {badBuild}, grenades {grenades}, entered {entered}, fort entries {fortEntries}, orders {red.Orders}/{blue.Orders}";
            return new Result { file = file, winner = winner, endTime = end, captures = nCaps, built = nBuilt, badBuild = badBuild, summary = summary };
        }

        static string BuiltCannonType(ZObject b) => b.StoredCannons.Count > 0 ? b.StoredCannons[b.StoredCannons.Count - 1] : "gatling";
        static string Fr(ZWorld w, Team t) => $"{w.Flags.Count(f => f.Owner == t)}/{w.Flags.Count}";
        static string Clock(double t) => t < 0 ? "-" : $"{(int)(t / 60)}:{(int)(t % 60):D2}";
    }
}
