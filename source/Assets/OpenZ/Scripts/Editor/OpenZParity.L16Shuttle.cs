#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV95 "level 16: the robots' shuttle stands in the place of one hut beside the red fort" (the rule:
    /// Sim/ZWorld.L16Shuttle.cs). Measured headless: level 16 with the rule on and off (the shuttle, its hit points, its sixteen
    /// tiles closed and blocking sight, the hut of the map gone and back, every other object identical), the tiles open again and
    /// no longer blocking when it is destroyed, a replay
    /// of 600 ticks with both computer players, and levels 2 and 17 with the rule on and off (identical: the rule tests the map
    /// file). The switch is put back afterwards.</summary>
    public static partial class OpenZParity
    {
        static void AddL16ShuttleDeviation(List<Deviation> list, Func<string, int, int> getInt)
        {
            string measured; bool pass; bool was = ZWorld.L16ShuttleDeviation;
            int hx = ZWorld.L16ShuttleHutTile[0], hy = ZWorld.L16ShuttleHutTile[1], hpWant = 0;
            var sq = ZWorld.L16ShuttleSquare; int n = sq[2] * sq[3];
            try
            {
                string m16 = MapText("L16");
                ZWorld.L16ShuttleDeviation = true; var w = ZWorld.Create(SettingsText, m16, 1);
                ZWorld.L16ShuttleDeviation = false; var z = ZWorld.Create(SettingsText, m16, 1);
                var s = w.L16Shuttle; hpWant = w.L16ShuttleHitPoints;
                bool HutAt(ZWorld ww) => ww.Objects.Any(o => o.Type == "hut" && !o.Destroyed && o.LocX == hx * 16 && o.LocY == hy * 16);
                bool made = s != null && s.MaxHealth == hpWant && s.Health == hpWant && s.AttackedByExplosives && s.Owner == Team.Neutral && s.IsDestroyableImpass && s.Type == ZWorld.DetailPropType
                            && w.DetailProps.Contains(s) && w.DetailPropUid.TryGetValue(s.Id, out var uid) && uid == ZWorld.L16ShuttleUid;
                int closed = w.L16ShuttleTilesClosed, open = w.L16ShuttleOpenTiles(), openOff = z.L16ShuttleOpenTiles(), sight = w.L16ShuttleSightBlocked(), sightAfter = -1;
                bool hutOn = HutAt(w), hutOff = HutAt(z);
                var wo = w.Objects.Where(o => o != s).ToList(); var zo = z.Objects.Where(o => !(o.Type == "hut" && ((o.LocX == hx * 16 && o.LocY == hy * 16) || (o.LocX == ZWorld.L16CampHutTile[0] * 16 && o.LocY == ZWorld.L16CampHutTile[1] * 16)))).ToList();
                bool same = z.L16Shuttle == null && wo.Count == zo.Count && wo.Zip(zo, (a, b) => a.Id == b.Id && a.LocX == b.LocX && a.LocY == b.LocY && a.Type == b.Type && a.MaxHealth == b.MaxHealth).All(x => x);
                // destroyed by the rules' own death path: the object goes, its tiles open
                ZWorld.L16ShuttleDeviation = true;
                var k = ZWorld.Create(SettingsText, m16, 1); int openAfter = -1; bool gone = false;
                if (k.L16Shuttle != null)
                {
                    var p0 = k.L16Shuttle; p0.DamageHealth(p0.Health); k.UpdateObjectHealth(p0);
                    for (int i = 0; i < 100; i++) k.Tick(ZConst.TICK);
                    openAfter = k.L16ShuttleOpenTiles(); gone = k.Get(p0.Id) == null; sightAfter = k.L16ShuttleSightBlocked();
                }
                string Run()
                {
                    var r = ZWorld.Create(SettingsText, m16, 7);
                    r.Bots.Add(new ZBot(r, Team.Red, 7)); r.Bots.Add(new ZBot(r, Team.Blue, 108));
                    for (int i = 0; i < 600; i++) r.Tick(ZConst.TICK);
                    return string.Join(";", r.Objects.Select(o => $"{o.Id}:{o.LocX},{o.LocY},{o.Health},{(o.Destroyed ? 1 : 0)}"));
                }
                bool det = Run() == Run();
                bool others = true; string otherText = "";
                foreach (var lv in new[] { "L02", "L17" })
                {
                    ZWorld.L16ShuttleDeviation = true; var a = ZWorld.Create(SettingsText, MapText(lv), 1, lv == "L17");
                    ZWorld.L16ShuttleDeviation = false; var b = ZWorld.Create(SettingsText, MapText(lv), 1, lv == "L17");
                    bool eq = a.L16Shuttle == null && a.Objects.Count == b.Objects.Count && a.Objects.Zip(b.Objects, (p, q) => p.Id == q.Id && p.Type == q.Type && p.LocX == q.LocX && p.LocY == q.LocY && p.MaxHealth == q.MaxHealth).All(x => x);
                    others &= eq; otherText += $"; level {lv.Substring(1)} on / off: {a.Objects.Count} / {b.Objects.Count} objects, {(eq ? "identical" : "DIFFERENT")}";
                }
                pass = made && closed == n && open == 0 && openOff == n - 1 && !hutOn && hutOff && w.L16ShuttleHutRemoved && w.L16CampHutRemoved && same && openAfter == n && gone && det && others && sight == n && sightAfter == 0;
                measured = $"level 16: the shuttle {(s != null ? "is" : "is NOT")} in the rules with {(s != null ? s.MaxHealth : 0)} hit points ({ZWorld.L16ShuttleHutsOfHealth} x a hut's {hpWant / ZWorld.L16ShuttleHutsOfHealth}), explosives only, neutral, a destroyable barrier {(made ? "yes" : "NO")}; " +
                           $"tiles of its {sq[2]} x {sq[3]} square it closed {closed} of {n}, tiles a unit can enter {open}, tiles that block direct fire and sight while it stands {sight} of {n}; the hut of the map on tile {hx},{hy}: {(hutOn ? "STILL THERE" : "gone")}, the hut beside the start camp on tile {ZWorld.L16CampHutTile[0]},{ZWorld.L16CampHutTile[1]}: {(w.L16CampHutRemoved ? "gone" : "STILL THERE")}; " +
                           $"off: no shuttle, the hut {(hutOff ? "stands" : "IS MISSING")}, tiles of the square a unit can enter {openOff} ({n - 1} = all but the hut's), every other object identical {(same ? "yes" : "NO")}; " +
                           $"destroyed: the object {(gone ? "is gone" : "IS STILL THERE")}, tiles open again {openAfter} of {n}, tiles that still block sight {sightAfter}; replay (600 ticks, both computer players) {(det ? "identical" : "DIFFERENT")}{otherText}";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            finally { ZWorld.L16ShuttleDeviation = was; }
            bool on = getInt("oz_zod_exact", 0) == 0;
            list.Add(new Deviation
            {
                id = "DV95", change = "Level 16: the two robots' landing shuttle stands right of the red fort in the place of one hut of the map: a destroyable map item on a 4 x 4 tile square (the ship is a third of the fort's side long); it does nothing else",
                zod = $"a hut stands on tile {hx},{hy} of the jungle map (one tile, a hut's hit points, a destroyable barrier that also blocks direct fire, ohut.cpp:247); the fifteen tiles round it are open ground; no such object as a shuttle",
                openz = "ZWorld.L16Shuttle.cs: on the level 16 map that hut leaves the rules at map load (the level file is not edited) and one neutral map item of the detail kind (DV67's path: attacked by explosives only, hurt by missile splash, " +
                        "no death debris, never a target of the computer player or of automatic fire) takes its place on the 4 x 4 tiles " +
                        $"{sq[0]} to {sq[0] + sq[2] - 1}, {sq[1]} to {sq[1] + sq[3] - 1} with {ZWorld.L16ShuttleHutsOfHealth} huts' hit points ({hpWant}: a hut's for each tile). " +
                        "Its tiles are a destroyable barrier as a hut's tile is (units path round it; a unit with explosives whose way is planned through it blasts it; no unit engages another across it) and open again when it is destroyed. " +
                        "Unlike the other details, and as the hut did on its tile, the standing ship blocks direct fire and sight across its square (its sixteen tiles are in the sight grid under its own id, like the level's pyramids; it can itself be fired at); destroyed, it no longer blocks. " +
                        "It produces nothing and cannot be captured, entered or repaired. The view draws it in the team colour of the fort beside it, with blinking lamps and their glow on the grass, blinking neon strips, a thin exhaust wisp, a damaged stage at half the hit points and a burnt wreck on the death event, " +
                        "(view only). The hut beside the robots' start camp (south-east of its campfire) leaves the rules at map load the same way (16 huts are left)",
                measured = measured,
                toggle = $"ZWorld.L16ShuttleDeviation: the game sets it at every start, off in 'Zod exact' (oz_zod_exact = {(on ? "0" : "1")}): the hut stands there again and the ship is not drawn; headless default on (it acts on the level 16 map file only)",
                status = !pass ? DevCheck : on ? DevActive : DevActive + " (off by pref)",
            });
        }
    }
}
#endif
