using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// K347, the SHUTTLE box of the battlefield screen (rule DV118; UI/OzShuttleStart.cs), counted headless (ZWorld.Create, no scene;
    /// nothing is saved: the box's state is handed to OzShuttleStart.Rule, the player's own box is never read or written). Menu
    /// OpenZ/V1.1/Shuttle: K347 Start Box Check (no Play).
    ///  A. the box is offered on EASY and MEDIUM only, on levels 16 and 17 (never on HARD, never on level 2, never in 'Zod exact'), and
    ///     the rule switch it gives at the start (ZWorld.ShuttleOn): level 16 = the box on EASY / MEDIUM, off on HARD; levels 2 and 17: on;
    ///  B. 18 level starts as the game makes them (levels 16, 2, 17 x EASY / MEDIUM / HARD x the box on / off; HARD with the fort door
    ///     guns of DV73, level 17 EASY with its start crews of DV63, as ZGame sets them): level 16 with the box on EASY / MEDIUM: the
    ///     playable shuttle, red, in the decoration's place, and every other object the same as with the box off; level 16 with the
    ///     box off or on HARD: no shuttle at all (not the decoration either), its 16 tiles open; level 2: no shuttle, the box changes
    ///     nothing (every object the same); level 17: its own ships (K444, below);
    ///  C. the testing panel (ZWorld.SpawnShuttle, as P drops it) in each of the 18 starts beside the player's fort: a red one, a blue
    ///     one (both crewed by their pilot) and an empty one (the null team, no pilot): all 54 made, on every level and difficulty and
    ///     with the box on or off;
    ///  D. HEAD START with the shuttle on level 16 (EASY, the box on and off): its 3 vehicles are made and none of them stands on the
    ///     shuttle's footprint.
    ///  K444 (DV134): level 17 has the box too (A), and its starts (B) have the empty car-park shuttle always and the box's ship by the red
    ///     fort on EASY / MEDIUM with the box on (Editor/OzL17ShuttleCount.cs counts the rest).
    /// </summary>
    public static class OzShuttleStartCount
    {
        public static readonly string[] Levels = { "L16", "L02", "L17" };
        static readonly GameDifficulty[] Diffs = { GameDifficulty.Easy, GameDifficulty.Medium, GameDifficulty.Hard };
        /// <summary>The level 16 head start (ZGame.HeadStartL16Types; the Play proof compares the two).</summary>
        public static readonly string[] HeadStartL16 = { "jeep", "jeep", "light" };

        static string Sig(ZObject o) => o.Type + "#" + o.Id + "@" + o.LocX + "," + o.LocY + ":" + o.Owner;
        static string D(GameDifficulty d) => d == GameDifficulty.Easy ? "EASY" : d == GameDifficulty.Medium ? "MEDIUM" : "HARD";

        /// <summary>One level start as ZGame makes it (the switches the difficulty and the box decide), the player red.</summary>
        static ZWorld Start(string settings, string map, string level, GameDifficulty d, bool box)
        {
            ZWorld.ShuttlePlayableDeviation = true; ZWorld.L16ShuttleDeviation = true; ZWorld.ShuttleTeam = Team.Red;
            ZWorld.FortDoorGunDeviation = d == GameDifficulty.Hard;                                                  // DV73 (ZGame: Hard, not bot vs bot)
            ZWorld.L17CrewsDeviation = level == "L17" && d == GameDifficulty.Easy;                                    // DV63 (ZGame: L17 EASY)
            ZWorld.ShuttleOn = OzShuttleStart.Rule(level, d, box);                                                    // ZGame: OzShuttleStart.RuleAtStart
            ZWorld.L17ShuttlesDeviation = true; ZWorld.L17StartShuttle = OzShuttleStart.L17Start(level, d, box);                    // K444 (DV134): as ZGame sets them
            return ZWorld.Create(settings, map, 1, level == "L17");
        }

        static ZObject Fort(ZWorld w, Team t) => w.Objects.FirstOrDefault(o => o.Kind == ZKind.Building && o.Type == "fort_front" && o.Owner == t && !o.Destroyed)
                                                 ?? w.Objects.FirstOrDefault(o => o.Kind == ZKind.Building && o.Owner == t && !o.Destroyed);

        /// <summary>The testing panel's drop (ZWorld.SpawnShuttle) at the first place ring by ring from (cx, cy) where one is made.</summary>
        static ZObject Drop(ZWorld w, int cx, int cy, Team team, out string why)
        {
            why = "";
            for (int r = 64; r <= 900; r += 32)
                for (int a = 0; a < 16; a++)
                {
                    double g = a * Math.PI / 8;
                    var s = w.SpawnShuttle(cx + (int)(Math.Cos(g) * r), cy + (int)(Math.Sin(g) * r), team, -90f, out why);
                    if (s != null) return s;
                    if (why != "no room") return null;                                                                  // refused for another reason: no place helps
                }
            return null;
        }

        public static string Check(string settings, string m16, string m02, string m17)
        {
            var sb = new StringBuilder();
            bool was = ZWorld.ShuttlePlayableDeviation, wasOn = ZWorld.ShuttleOn, wasL16 = ZWorld.L16ShuttleDeviation, wasDoor = ZWorld.FortDoorGunDeviation, wasCrews = ZWorld.L17CrewsDeviation;
            bool wasL17 = ZWorld.L17ShuttlesDeviation, wasL17s = ZWorld.L17StartShuttle;                                                    // K444
            var team0 = ZWorld.ShuttleTeam;
            var maps = new Dictionary<string, string> { ["L16"] = m16, ["L02"] = m02, ["L17"] = m17 };
            try
            {
                // ---------------------------------------------------------------- A. offered, and the rule switch
                var table = new StringBuilder(); bool offerOk = true, ruleOk = true, zodOk = true;
                foreach (var lv in Levels)
                    foreach (var d in Diffs)
                    {
                        bool off = OzShuttleStart.Offered(lv, d, false), wantOff = (lv == "L16" || lv == "L17") && d != GameDifficulty.Hard;   // K444: level 17 too
                        bool r1 = OzShuttleStart.Rule(lv, d, true), r0 = OzShuttleStart.Rule(lv, d, false);
                        bool w1 = lv != "L16" || d != GameDifficulty.Hard, w0 = lv != "L16";
                        offerOk &= off == wantOff; ruleOk &= r1 == w1 && r0 == w0; zodOk &= !OzShuttleStart.Offered(lv, d, true);
                        table.Append($" {lv} {D(d)}: {(off ? "offered" : "-")}, switch {(r1 ? "on" : "off")} / {(r0 ? "on" : "off")};");
                    }
                sb.AppendLine("A. box on / off ->" + table);
                sb.AppendLine(OzV11Kit.Verdict(offerOk && zodOk, "the box is offered for levels 16 and 17 on EASY and MEDIUM only (not on HARD, not on level 2, never in 'Zod exact')"));
                sb.AppendLine(OzV11Kit.Verdict(ruleOk, "the rule switch at the start: level 16 = the box on EASY / MEDIUM and off on HARD; levels 2 and 17: on whatever the box"));

                // ---------------------------------------------------------------- B. the 18 starts, C. the testing panel in each
                int starts = 0, made = 0, want = 0, l17Made = 0; var bad = new StringBuilder(); var drops = new StringBuilder(); int dropped = 0, dropWant = 0;
                foreach (var lv in Levels)
                    foreach (var d in Diffs)
                    {
                        var on = Start(settings, maps[lv], lv, d, true); var offW = Start(settings, maps[lv], lv, d, false);
                        foreach (var (w, box) in new[] { (on, true), (offW, false) })
                        {
                            starts++;
                            bool expect = lv == "L16" && d != GameDifficulty.Hard && box;
                            var sh = w.Objects.Where(o => o.IsShuttle && !o.Destroyed).ToList();
                            bool ok;
                            if (expect)
                            {
                                want++;
                                var s = w.PlayableShuttle;
                                ok = s != null && sh.Count == 1 && sh[0] == s && s.Owner == Team.Red && s.Drivers.Count == 1 && w.L16Shuttle == null && !w.L16ShuttleOptionOff && w.L16ShuttleOpenTiles() == 16;
                                if (ok) made++;
                            }
                            else if (lv == "L16")
                                ok = sh.Count == 0 && w.PlayableShuttle == null && w.L16Shuttle == null && w.L16ShuttleOptionOff && w.L16ShuttleOpenTiles() == 16;
                            else if (lv == "L17")
                            {   // K444 (DV134): the empty car-park ship always; the box's ship by the red fort on EASY / MEDIUM with the box on
                                bool fort = OzShuttleStart.L17Start(lv, d, box);
                                ok = w.L17CarParkShuttle != null && w.L17CarParkShuttle.ShuttleIsEmpty && sh.Count == (fort ? 2 : 1) && w.L16Shuttle == null && !w.L16ShuttleOptionOff
                                     && (fort ? w.L17FortShuttle != null && w.L17FortShuttle.Owner == Team.Red && w.PlayableShuttle == w.L17FortShuttle : w.L17FortShuttle == null && w.PlayableShuttle == null);
                                if (ok && fort) l17Made++;
                            }
                            else
                                ok = sh.Count == 0 && w.PlayableShuttle == null && w.L16Shuttle == null && !w.L16ShuttleOptionOff;
                            if (!ok) bad.Append($" {lv} {D(d)} box {(box ? "on" : "off")}: shuttles {sh.Count}, playable {(w.PlayableShuttle != null ? "#" + w.PlayableShuttle.Id : "none")}, decoration {(w.L16Shuttle != null ? "THERE" : "none")}, option-off flag {w.L16ShuttleOptionOff};");
                        }
                        // every other object the same with the box on and off (level 16 EASY / MEDIUM: all but the shuttle)
                        var a = on.Objects.Where(o => !o.IsShuttle).Select(Sig).ToList(); var b = offW.Objects.Where(o => !o.IsShuttle).Select(Sig).ToList();
                        if (!a.SequenceEqual(b)) bad.Append($" {lv} {D(d)}: the other objects differ with the box on / off ({a.Count} / {b.Count});");
                        if (!OzShuttleStart.Offered(lv, d, false))                                                                    // K444: level 17 EASY / MEDIUM: the box adds its ship (the others compared above)
                            if (!on.Objects.Select(Sig).SequenceEqual(offW.Objects.Select(Sig))) bad.Append($" {lv} {D(d)}: the box changed the start;");
                        // C. the testing panel beside the player's fort, in both starts
                        foreach (var (w, box) in new[] { (on, true), (offW, false) })
                        {
                            var fort = Fort(w, Team.Red);
                            if (fort == null) { drops.Append($" {lv} {D(d)}: NO red fort;"); dropWant += 3; continue; }
                            int fx = fort.LocX + fort.WidthPix / 2, fy = fort.LocY + fort.HeightPix / 2;
                            foreach (var t in new[] { Team.Red, Team.Blue, Team.Neutral })
                            {
                                dropWant++;
                                var s = Drop(w, fx, fy, t, out string why);
                                bool ok = s != null && s.IsShuttle && s.Owner == t && (t == Team.Neutral ? s.ShuttleIsEmpty : s.Drivers.Count == 1);
                                if (ok) dropped++;
                                else drops.Append($" {lv} {D(d)} box {(box ? "on" : "off")} {t}: {(s == null ? "refused (" + why + ")" : "made but wrong (owner " + s.Owner + ", pilots " + s.Drivers.Count + ")")};");
                            }
                        }
                    }
                sb.AppendLine($"B. {starts} level starts: the playable shuttle in {made} of the {want} where the box is offered and on; {(bad.Length == 0 ? "every other start without a shuttle, the other objects the same" : "WRONG:" + bad)}");
                sb.AppendLine(OzV11Kit.Verdict(starts == 18 && made == want && want == 2 && l17Made == 2 && bad.Length == 0,
                    "level 16 EASY / MEDIUM with the box on: the playable shuttle (red, its pilot, the decoration's place, its 16 tiles open); the box off or HARD: no shuttle at all; level 17 (K444): its empty car-park shuttle always, the box's ship by the red fort on EASY / MEDIUM with the box on; level 2: no shuttle and the box changes nothing; every other object the same"));
                sb.AppendLine($"C. the testing panel beside the player's fort: {dropped} of {dropWant} shuttles made (red, blue crewed; the null team empty){(drops.Length == 0 ? "" : "; MISSING:" + drops)}");
                sb.AppendLine(OzV11Kit.Verdict(dropWant == 54 && dropped == dropWant, "the testing panel drops shuttles (crewed and empty) on every level and difficulty, with the box on and off"));

                // ---------------------------------------------------------------- D. HEAD START with the shuttle (level 16 EASY)
                var hs = new StringBuilder(); bool hsOk = true;
                foreach (bool box in new[] { true, false })
                {
                    var w = Start(settings, m16, "L16", GameDifficulty.Easy, box);
                    var extra = w.AddHeadStart(Team.Red, HeadStartL16);
                    var s = w.PlayableShuttle; int onPad = 0;
                    if (s != null) onPad = extra.Count(v => v.LocX < s.LocX + s.WidthPix && s.LocX < v.LocX + v.WidthPix && v.LocY < s.LocY + s.HeightPix && s.LocY < v.LocY + v.HeightPix);
                    bool ok = extra.Count == HeadStartL16.Length && onPad == 0 && (box ? s != null : s == null);
                    hsOk &= ok;
                    hs.Append($" box {(box ? "on" : "off")}: {extra.Count} vehicles ({string.Join(", ", extra.Select(v => v.Type + "@" + (v.LocX >> 4) + "," + (v.LocY >> 4)))}), shuttle {(s != null ? "#" + s.Id : "none")}, on its footprint {onPad};");
                }
                sb.AppendLine("D. level 16 EASY with HEAD START:" + hs);
                sb.AppendLine(OzV11Kit.Verdict(hsOk, "HEAD START with the shuttle: its 3 vehicles are made, none on the shuttle's footprint (the box on and off)"));
            }
            finally
            {
                ZWorld.ShuttlePlayableDeviation = was; ZWorld.ShuttleOn = wasOn; ZWorld.L16ShuttleDeviation = wasL16; ZWorld.FortDoorGunDeviation = wasDoor;
                ZWorld.L17CrewsDeviation = wasCrews; ZWorld.ShuttleTeam = team0;
                ZWorld.L17ShuttlesDeviation = wasL17; ZWorld.L17StartShuttle = wasL17s;                                    // K444
            }
            return sb.ToString();
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Shuttle: K347 Start Box Check (no Play)")]
        static void Menu() => OzV11Kit.RunMenu("K347 the battlefield screen's SHUTTLE box at the level start", () => Check(OzV11Kit.SettingsText, OzV11Kit.MapText("L16"), OzV11Kit.MapText("L02"), OzV11Kit.MapText("L17")));
#endif
    }
}
