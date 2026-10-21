#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV98 "a squad member left far behind becomes a squad of his own" (the rule: Sim/ZWorld.SquadSplit.cs).
    /// Measured headless on the level 16 map: a squad of four with one member set past the distance (he leaves, the robot count stays),
    /// one set just inside it (nothing happens), a leader set apart from his three (he leaves, the first of the three leads them), and the
    /// same first case with the rule off (the squad stays together).</summary>
    public static partial class OpenZParity
    {
        static void AddSquadSplitDeviation(List<Deviation> list)
        {
            string measured; bool pass;
            bool was = ZWorld.SquadSplitDeviation;
            try
            {
                string Case(bool on, int movePx, bool moveLeader, out int splits, out bool apartAlone, out int robotsBefore, out int robotsAfter, out int squadsAfter)
                {
                    ZWorld.SquadSplitDeviation = on;
                    var w = ZWorld.Create(SettingsText, MapText("L16"), 1);
                    w.Bots.Clear();
                    // an open stretch of ground wide enough for the test (robots pass every tile of the row)
                    int ty = -1, tx = -1;
                    for (int y = 10; y < w.Map.height - 10 && ty < 0; y++)
                        for (int x = 4; x + 50 < w.Map.width && ty < 0; x++)
                        {
                            bool ok = true;
                            for (int k = 0; k <= 50 && ok; k++) ok = w.Path.TilePassable(x + k, y, true) && w.Path.TilePassable(x + k, y + 1, true);
                            if (ok) { tx = x; ty = y; }
                        }
                    if (ty < 0) throw new Exception("no open row of 50 tiles on the map");
                    var lead = w.CreateRobotGroup("pyro", tx * 16, ty * 16, Team.Red);
                    var all = new List<ZObject> { lead }; all.AddRange(lead.Minions);
                    for (int i = 0; i < all.Count; i++) all[i].SetCords(tx * 16 + i * 16, ty * 16);
                    var apart = moveLeader ? lead : lead.Minions[lead.Minions.Count - 1];
                    if (moveLeader) for (int i = 1; i < all.Count; i++) all[i].SetCords(tx * 16 + movePx + i * 16, ty * 16);
                    else apart.SetCords(tx * 16 + movePx, ty * 16);
                    robotsBefore = w.UnitCount(Team.Red);
                    for (int i = 0; i < 10; i++) w.Tick(ZConst.TICK);
                    robotsAfter = w.UnitCount(Team.Red);
                    splits = w.SquadSplits;
                    apartAlone = apart.Leader == null && apart.Minions.Count == 0;
                    squadsAfter = all.Select(o => o.Leader ?? o).Distinct().Count();
                    var others = all.Where(o => o != apart).ToList();
                    var newLead = others.Select(o => o.Leader ?? o).Distinct().ToList();
                    return $"{splits} split, the member set {movePx / 8} m away alone {(apartAlone ? "yes" : "no")}, the other three in {newLead.Count} squad (led by #{newLead[0].Id}{(moveLeader ? ", a former member" : "")}), red robots {robotsBefore} -> {robotsAfter}";
                }
                string a = Case(true, 700, false, out int sA, out bool aloneA, out int rbA, out int raA, out int sqA);
                string b = Case(true, 560, false, out int sB, out bool aloneB, out int rbB, out int raB, out int sqB);
                string c = Case(true, 700, true, out int sC, out bool aloneC, out int rbC, out int raC, out int sqC);
                string d = Case(false, 700, false, out int sD, out bool aloneD, out int rbD, out int raD, out int sqD);
                pass = sA == 1 && aloneA && rbA == raA && sqA == 2 && sB == 0 && !aloneB && sqB == 1 && sC == 1 && aloneC && rbC == raC && sqC == 2 && sD == 0 && !aloneD && sqD == 1;
                measured = $"a member 87 m from his squad: {a}; one 70 m away: {b}; the leader 87 m from his three: {c}; rule off, 87 m: {d}";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            finally { ZWorld.SquadSplitDeviation = was; }
            list.Add(new Deviation
            {
                id = "DV98", change = "A squad member left far behind becomes a squad of his own",
                zod = "a squad stays one squad until its members die, whatever the distance between them (zobject.cpp minion lists); its frame then spans every member",
                openz = $"ZWorld.SquadSplit.cs: once a second of game time, a robot whose nearest squad mate is farther than {ZWorld.SquadSplitPx} px ({ZWorld.SquadSplitPx / 8} m, about one screen at the standard camera) leaves his squad " +
                        "and is a single robot from then on with his own orders; when the one left alone is the leader, the first of the others leads them (the hand-over the rules make when a leader dies). " +
                        "Nothing is created or removed: the robot counts stay the same, the number of squads grows by one. View: UI/OpenZUnitOverlay.cs draws a squad's frame only round the members near its main group (a member farther out gets a small frame of his own)",
                measured = measured,
                toggle = "ZWorld.SquadSplitDeviation: on in every game and in the headless runs (nothing turns it off but this probe)",
                status = pass ? DevActive : DevCheck,
            });
        }
    }
}
#endif
