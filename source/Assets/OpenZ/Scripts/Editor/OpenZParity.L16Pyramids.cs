#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV89 "level 16: the two pyramids are destroyable map items" (the rule: Sim/ZWorld.L16Pyramids.cs).
    /// Measured headless: level 16 with the rule on and off (pyramids, hit points, explosives only, every other object identical),
    /// the squares closed before and after a pyramid is destroyed, the sight grid (a standing pyramid blocks direct fire across it
    /// and can be fired at, its mound does not block, the tiles under the low pieces do not block), a replay of 600 ticks with both
    /// computer players, and levels 2 and 17 with the rule on and off (identical: the rule tests the map file). The switch is put
    /// back afterwards.</summary>
    public static partial class OpenZParity
    {
        static void AddL16PyramidsDeviation(List<Deviation> list, Func<string, int, int> getInt)
        {
            string measured; bool pass; bool was = ZWorld.L16PyramidsDeviation;
            bool shuttleWas = ZWorld.L16ShuttleDeviation;
            try
            {
                string m16 = MapText("L16");
                ZWorld.L16ShuttleDeviation = false;      // this row measures the pyramids alone (the shuttle's item is made after them: its id moves with them)
                ZWorld.L16PyramidsDeviation = true; var w = ZWorld.Create(SettingsText, m16, 1);
                ZWorld.L16PyramidsDeviation = false; var z = ZWorld.Create(SettingsText, m16, 1);
                int hp = ZWorld.L16PyramidHitPoints[0], hpSmall = ZWorld.L16PyramidHitPoints[1];
                bool made = w.L16Pyramids.Count == 2 && w.L16Pyramids[0].MaxHealth == hp && w.L16Pyramids[1].MaxHealth == hpSmall && w.L16Pyramids.All(o => o.AttackedByExplosives && o.Owner == Team.Neutral && !o.IsDestroyableImpass);
                bool same = z.L16Pyramids.Count == 0 && z.Objects.Count == w.Objects.Count - w.L16Pyramids.Count
                            && z.Objects.Zip(w.Objects.Where(o => !w.L16Pyramids.Contains(o)), (a, b) => a.Id == b.Id && a.LocX == b.LocX && a.LocY == b.LocY && a.Type == b.Type).All(x => x);
                int open = w.L16PyramidOpenTiles(0) + w.L16PyramidOpenTiles(1);
                // direct fire: the standing squares block, a shot at a pyramid has its line, the low pieces' tiles do not block
                int SquareBlocks(ZWorld ww, int k) { var t = ZWorld.L16PyramidTiles[k]; int n = 0; for (int x = t[0]; x < t[0] + t[2]; x++) for (int y = t[1]; y < t[1] + t[3]; y++) if (ww.Sight.BlockedTile(x, y)) n++; return n; }
                int LowBlocks(ZWorld ww) { int n = 0; for (int i = 0; i + 1 < ZWorld.L16LowTiles.Length; i += 2) if (ww.Sight.BlockedTile(ZWorld.L16LowTiles[i], ZWorld.L16LowTiles[i + 1])) n++; return n; }
                int standing = made ? SquareBlocks(w, 0) + SquareBlocks(w, 1) : -1, low = LowBlocks(w), lowOff = LowBlocks(z), lowAll = ZWorld.L16LowTiles.Length / 2;
                int lines = 0;
                for (int k = 0; made && k < 2; k++)
                {   // from two tiles outside the square on a middle row or column: the shot AT the pyramid has its line (its own tiles
                    // never block the shot at it); the same line to the same point for anything else is blocked by the square
                    var p = w.L16Pyramids[k]; bool got = false;
                    foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                    {
                        int ax = p.CenterX + dx * (p.WidthPix / 2 + 24), ay = p.CenterY + dy * (p.HeightPix / 2 + 24);
                        if (w.Sight.ClearUncached(ax, ay, 0, p.CenterX, p.CenterY, w.Sight.ExemptOf(p)) && !w.Sight.ClearUncached(ax, ay, 0, p.CenterX, p.CenterY, 0)) got = true;
                    }
                    if (got) lines++;
                }
                // a destroyed pyramid: the object goes, its square stays closed, its mound no longer blocks direct fire
                ZWorld.L16PyramidsDeviation = true;
                var k2 = ZWorld.Create(SettingsText, m16, 1); int openAfter = -1, left = -1, mound = -1;
                if (k2.L16Pyramids.Count == 2)
                {
                    var p0 = k2.L16Pyramids[0]; p0.DamageHealth(p0.Health); k2.UpdateObjectHealth(p0);
                    for (int i = 0; i < 100; i++) k2.Tick(ZConst.TICK);
                    openAfter = k2.L16PyramidOpenTiles(0); left = k2.Objects.Count(o => k2.L16Pyramids.Contains(o) && !o.Destroyed); mound = SquareBlocks(k2, 0);
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
                    ZWorld.L16PyramidsDeviation = true; var a = ZWorld.Create(SettingsText, MapText(lv), 1, lv == "L17");
                    ZWorld.L16PyramidsDeviation = false; var b = ZWorld.Create(SettingsText, MapText(lv), 1, lv == "L17");
                    bool eq = a.L16Pyramids.Count == 0 && a.Objects.Count == b.Objects.Count && a.Objects.Zip(b.Objects, (p, q) => p.Id == q.Id && p.Type == q.Type && p.LocX == q.LocX && p.LocY == q.LocY && p.MaxHealth == q.MaxHealth).All(x => x);
                    others &= eq; otherText += $"; level {lv.Substring(1)} on / off: {a.Objects.Count} / {b.Objects.Count} objects, {(eq ? "identical" : "DIFFERENT")}";
                }
                pass = made && same && open == 0 && openAfter == 0 && left == 1 && standing == 100 && lines == 2 && mound == 0 && low == 0 && det && others;
                measured = $"level 16: {w.L16Pyramids.Count} pyramids, {hp} and {hpSmall} hit points (a map object has {(int)(w.S.map_item_health * ZConst.MAX_UNIT_HEALTH)}), explosives only {(made ? "yes" : "NO")}; tiles of the two squares a unit can enter: {open}; " +
                           $"direct fire: tiles of the two standing squares that block {standing} of 100, the shot at a pyramid has its line where the same line to anything else is blocked {lines} of 2; tiles under low pieces that block {low} of {lowAll} (rule off: {lowOff}); " +
                           $"off: {z.L16Pyramids.Count} pyramids, every other object identical {(same ? "yes" : "NO")}; one pyramid destroyed: {left} left standing, its square still closed ({openAfter} open tiles), tiles of its mound that block direct fire {mound}; " +
                           $"replay (600 ticks, both computer players) {(det ? "identical" : "DIFFERENT")}{otherText}";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            finally { ZWorld.L16PyramidsDeviation = was; ZWorld.L16ShuttleDeviation = shuttleWas; }
            bool on = getInt("oz_zod_exact", 0) == 0;
            list.Add(new Deviation
            {
                id = "DV89", change = "Level 16: the two overgrown pyramids are destroyable map items (explosives only, three stages); their squares stay closed; low ruin pieces and fires do not block direct fire",
                zod = "no such objects: the jungle map has no pyramid; rock tiles, huts and the map's own objects are the only destroyable map items (omapobject.cpp:77), each with a map object's hit points; no line of sight at all (DV23 is OpenZ's)",
                openz = "ZWorld.L16Pyramids.cs: on the level 16 map two neutral map items of the detail kind (DV67's path: attacked by explosives only, hurt by missile splash, never a target of the computer player or of automatic fire, " +
                        $"no death debris) with {ZWorld.L16PyramidHitPoints[0]} hit points for the big and {ZWorld.L16PyramidHitPoints[1]} for the small one (one heavy tank needs about 38 s and about 21 s); the 8 x 8 and 6 x 6 tile squares under them are closed in the level data (with the tiles under the other ruin pieces and the fires: DV86's kind of change) " +
                        "and stay closed when a pyramid is destroyed, so a pyramid is never a destroyable block and no path changes in any stage. Line of sight (DV23, one line in ZSight's constructor): a pyramid's square is in the sight grid under the pyramid's own id, " +
                        "as a building's tiles are, so the standing pyramid blocks direct fire across it and can itself be fired at by tanks and Toughs; its rubble mound no longer blocks, as a destroyed building does not. With the same switch the " +
                        $"{ZWorld.L16LowTiles.Length / 2} tiles the level closes under its low pieces (campfires, stairs platforms, fallen columns, fire bowls and pole torches on the ground: ZWorld.L16LowTiles) do not block direct fire or sight: " +
                        "no unit enters them, but units shoot across them; the taller ruin pieces block like cliffs. The view shows the damaged stage at half the hit points and the rubble mound on the death event. " +
                        "The ruin pieces, fires, huts' look and lamp posts of the level are view only",
                measured = measured,
                toggle = $"ZWorld.L16PyramidsDeviation: the game sets it at every start, off in 'Zod exact' (oz_zod_exact = {(on ? "0" : "1")}); headless default on (it acts on the level 16 map file only)",
                status = !pass ? DevCheck : on ? DevActive : DevActive + " (off by pref)",
            });
        }
    }
}
#endif
