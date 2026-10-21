#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV88 "level 16: the trees that close a tile can be destroyed like rocks" (the rule:
    /// Sim/ZWorld.TreeTiles.cs). Written from the live constants and measured headless on the level's own data: the tree objects
    /// and the tiles they close, their hit points against a rock's, one tree on ground and one in the swamp destroyed by the rules'
    /// own death path (the tile opens: ground for robots and vehicles, swamp for robots only), the same world with the rule off
    /// (no tree objects, every other object the same, exactly the tree tiles open) and a replay (two worlds, same seed, the CPU on
    /// both sides, 600 ticks: the same object state).</summary>
    public static partial class OpenZParity
    {
        static void AddTreeTilesDeviation(List<Deviation> list)
        {
            string measured; bool pass; int hp = 0, rockHp = 0;
            bool was = ZWorld.TreeTilesDeviation;
            try
            {
                ZWorld.TreeTilesDeviation = true;
                var w = ZWorld.Create(SettingsText, MapText("L16"), 1);
                int listed = ZTreeTilesData.L16.Uids.Length, made = w.TreeTiles.Count, closed = 0, swamp = 0;
                foreach (var o in w.TreeTiles) { int x = o.LocX >> 4, y = o.LocY >> 4; if (w.Path.HasDestroyableBarrier(x, y)) closed++; if (w.Map.ClassAt(x, y) == ZClass.Water) swamp++; }
                hp = w.TreeHealth; rockHp = (int)(w.S.rock_item_health * ZConst.MAX_UNIT_HEALTH);
                bool like = w.TreeTiles.All(o => o.AttackedByExplosives && o.Owner == Team.Neutral && o.IsDestroyableImpass && o.MaxHealth == hp && o.Health == hp);
                // the same level without the rule: no tree objects, every other object the same, exactly the tree tiles open
                ZWorld.TreeTilesDeviation = false;
                var z = ZWorld.Create(SettingsText, MapText("L16"), 1);
                bool same = z.TreeTiles.Count == 0 && z.Objects.Count == w.Objects.Count - made
                            && z.Objects.Zip(w.Objects.Where(o => o.Type != ZWorld.DetailPropType), (a, b) => a.Id == b.Id && a.LocX == b.LocX && a.LocY == b.LocY && a.Type == b.Type).All(x => x);
                int diffR = 0, diffV = 0;
                for (int y = 0; y < w.Map.height; y++)
                    for (int x = 0; x < w.Map.width; x++)
                    {
                        if (w.Path.TilePassable(x, y, true) != z.Path.TilePassable(x, y, true)) diffR++;
                        if (w.Path.TilePassable(x, y, false) != z.Path.TilePassable(x, y, false)) diffV++;
                    }
                ZWorld.TreeTilesDeviation = true;
                // one tree on ground and one in the swamp, destroyed by the rules' own death path
                string Fell(bool wet, out bool ok)
                {
                    var t = w.TreeTiles.FirstOrDefault(o => (w.Map.ClassAt(o.LocX >> 4, o.LocY >> 4) == ZClass.Water) == wet);
                    if (t == null) { ok = false; return (wet ? "swamp" : "ground") + ": no such tree"; }
                    int x = t.LocX >> 4, y = t.LocY >> 4;
                    bool before = !w.Path.TilePassable(x, y, true) && !w.Path.TilePassable(x, y, false) && w.Path.HasDestroyableBarrier(x, y);
                    t.SetHealth(0); w.UpdateObjectHealth(t);
                    for (int i = 0; i < 3; i++) w.Tick(ZConst.TICK);
                    bool robots = w.Path.TilePassable(x, y, true), vehicles = w.Path.TilePassable(x, y, false);
                    ok = before && robots && vehicles == !wet && !w.Path.HasDestroyableBarrier(x, y) && w.Get(t.Id) == null;
                    return $"{(wet ? "swamp" : "ground")} tile ({x}, {y}): closed and destroyable before {(before ? "yes" : "NO")}, after the tree is destroyed robots {(robots ? "pass" : "do NOT pass")}, vehicles {(vehicles ? "pass" : "do not pass")}";
                }
                string g = Fell(false, out bool okG), s = Fell(true, out bool okS);
                string Run()
                {
                    var r = ZWorld.Create(SettingsText, MapText("L16"), 7);
                    r.Bots.Add(new ZBot(r, Team.Red, 7)); r.Bots.Add(new ZBot(r, Team.Blue, 108));
                    for (int i = 0; i < 600; i++) r.Tick(ZConst.TICK);
                    return string.Join(";", r.Objects.Select(o => $"{o.Id}:{o.LocX},{o.LocY},{o.Health},{(o.Destroyed ? 1 : 0)}"));
                }
                bool det = Run() == Run();
                pass = made == listed && closed == made && w.TreeTilesClosed == made && like && same && diffR == made && diffV == made - swamp && okG && okS && det && hp > 0 && hp < rockHp;
                measured = $"L16: {made}/{listed} tree objects, {closed} tiles closed and destroyable ({made - swamp} ground, {swamp} swamp), {hp} HP each (a rock {rockHp}), explosives only {(like ? "yes" : "NO")}; " +
                           $"{g}; {s}; rule off: {z.TreeTiles.Count} tree objects, every other object identical {(same ? "yes" : "NO")}, tiles that differ: {diffR} for robots, {diffV} for vehicles; replay (600 ticks, CPU on both sides) {(det ? "identical" : "DIFFERENT")}";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            finally { ZWorld.TreeTilesDeviation = was; }
            list.Add(new Deviation
            {
                id = "DV88", change = "Level 16: the trees that close a tile can be destroyed like rocks",
                zod = "no such objects: the trees of the tile art block nothing and nothing can destroy them; the only barriers that can be destroyed are rocks, huts and map objects (orock.cpp:678-686, omapobject.cpp:77)",
                openz = "ZWorld.TreeTiles.cs: every tall tree, mangrove or vine of the dressed level that stands on a tile units could enter is a neutral map item (the list ZTreeTilesData is generated from the scenery list " +
                        $"that closed these tiles in the level data before; the same kind of object as the Desert palms, DV67) with {ZWorld.TreeHealthOfRock:0.##} of a rock's hit points ({hp} against {rockHp}): attacked by explosives only and hurt by " +
                        "missile splash, like a rock; its tile is a barrier that can be destroyed, like a rock's (units with explosives plan through it and blast it when they reach it, which is all the CPU ever does to it; fire lines across it are blocked); " +
                        "destroyed, the tile opens as a rock's does (ground: robots and vehicles, the path weights of the map load; swamp: robots only). The level data leaves these tiles open (DV86). No sight block, no death debris, never a CPU or " +
                        "automatic target. View only: View/OzTreeFall.cs (the tree leans at half its points, falls with its sounds, its own lowest part stays as the stump, the far version of its 32 m cell stops showing it)",
                measured = measured,
                toggle = "ZWorld.TreeTilesDeviation: on in every game and in the headless runs (nothing turns it off but this probe: the tiles are open ground then)",
                status = pass ? DevActive : DevCheck,
            });
        }
    }
}
#endif
