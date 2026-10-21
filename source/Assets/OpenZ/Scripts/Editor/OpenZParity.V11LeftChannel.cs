#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV119 "level 16: the swamp channel west of the gatling island is three tiles wide" (level data only:
    /// in Resources/OpenZ/Maps/L16_grid.json eighteen ground tiles west of the island, x 23-25 rows 60-62, x 23-24 rows 63-66 and
    /// x 23 row 67, are shallow water, so the island reads as an island; no rule code). Measured headless on the level data with the
    /// game's own move rule: the eighteen tiles are shallow water, no 2 x 2 vehicle reaches the island from the meadow west of it (as
    /// before: the one-tile channel already stopped them), a robot still wades to the gatling, and the island keeps its ground.</summary>
    public static partial class OpenZParity
    {
        internal static readonly int[] L16LeftChannelTiles =
            { 23, 60, 24, 60, 25, 60, 23, 61, 24, 61, 25, 61, 23, 62, 24, 62, 25, 62, 23, 63, 24, 63, 23, 64, 24, 64, 23, 65, 24, 65, 23, 66, 24, 66, 23, 67 };
        const int L16IslandX0 = 26, L16IslandX1 = 31, L16IslandY0 = 62, L16IslandY1 = 69, L16IslandGround = 40;

        static void AddL16LeftChannelDeviation(List<Deviation> list)
        {
            string measured; bool pass;
            try
            {
                var w = ZWorld.Create(SettingsText, MapText("L16"), 1);
                var map = w.Map; var path = w.Path;
                int wet = 0;
                for (int i = 0; i < L16LeftChannelTiles.Length; i += 2) if (map.ClassAt(L16LeftChannelTiles[i], L16LeftChannelTiles[i + 1]) == ZClass.Water) wet++;
                var island = new HashSet<(int, int)>();
                for (int y = L16IslandY0; y <= L16IslandY1; y++) for (int x = L16IslandX0; x <= L16IslandX1; x++) if (map.ClassAt(x, y) == ZClass.Ground) island.Add((x, y));
                HashSet<(int, int)> Reach(int sx, int sy, bool robot)
                {
                    var seen = new HashSet<(int, int)> { (sx, sy) }; var q = new Queue<(int, int)>(); q.Enqueue((sx, sy));
                    while (q.Count > 0)
                    {
                        var (cx, cy) = q.Dequeue();
                        for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                int nx = cx + dx, ny = cy + dy;
                                if (!seen.Contains((nx, ny)) && path.StepOk(cx, cy, nx, ny, robot, false)) { seen.Add((nx, ny)); q.Enqueue((nx, ny)); }
                            }
                    }
                    return seen;
                }
                var veh = Reach(20, 60, false);
                int vehOnIsland = veh.Count(p => island.Contains(p) || island.Contains((p.Item1 + 1, p.Item2)) || island.Contains((p.Item1, p.Item2 + 1)) || island.Contains((p.Item1 + 1, p.Item2 + 1)));
                var rob = Reach(20, 60, true);
                int robOnIsland = rob.Count(p => island.Contains(p));
                pass = wet == L16LeftChannelTiles.Length / 2 && vehOnIsland == 0 && robOnIsland > 0 && island.Count == L16IslandGround;
                measured = $"{wet} of {L16LeftChannelTiles.Length / 2} channel tiles are shallow water; from the meadow (tile 20,60) a 2 x 2 vehicle reaches {veh.Count} places, " +
                           $"{vehOnIsland} of them on the island (must be 0); a robot reaches {rob.Count} tiles, {robOnIsland} of the island's {island.Count} ground tiles (the gatling at 29,63 stands there; {L16IslandGround} expected)";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            list.Add(new Deviation
            {
                id = "DV119", change = "Level 16: the swamp channel west of the gatling island is three tiles wide (it reads as water all round the island)",
                zod = "the map file's own tile classes: one tile of water west of the island (x 25, rows 63-67) and grass up to it",
                openz = "the level data of level 16 makes eighteen ground tiles west of the island shallow water (x 23-25 rows 60-62, x 23-24 rows 63-66, x 23 row 67), " +
                        "joined to the channels north and south of the island; vehicles could not reach the island before and cannot now, robots wade to the gatling as before; " +
                        "the pathing elsewhere is unchanged (no rule code: the level data and the level's ground, water and pictures)",
                measured = measured,
                toggle = "none (level data)",
                status = pass ? DevActive : DevCheck,
            });
        }
    }
}
#endif
