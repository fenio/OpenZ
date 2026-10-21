using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// Prepared answer 23: both pyramids break in three stages, the
    /// columns, walls, head and gateway stay): the two stepped pyramids of level 16 are neutral map items on the path of the
    /// destructible details (DV67, ZWorld.DetailProps.cs: type <see cref="DetailPropType"/>, attacked by explosives only, hurt by
    /// missile splash, never a target of the computer player or of automatic fire, no death debris missile), with three
    /// differences that are theirs alone:
    ///  * hit points = <see cref="L16PyramidHitPoints"/> (35400 for the big one, 20800 for the small one: a rock tile or a hut has
    ///    1666), so one heavy tank needs about 38 s for the big pyramid and about 21 s for the small one;
    ///  * the tiles under a pyramid are closed in the LEVEL DATA (impassable, like the tiles under the other ruins) and stay closed
    ///    when it is destroyed: the rubble mound stands on the same square. So a pyramid is never a "destroyable block": no unit
    ///    plans a path through it or shoots it to get past, and nothing about paths changes in any of its three stages;
    ///  * line of sight (DV23, ZSight): the rules take every impassable tile of the level data for a cliff, which no direct shot
    ///    passes and which hides whatever stands on it. A pyramid's square is therefore entered in the sight grid under the
    ///    pyramid's own id, as a building's tiles are: the STANDING pyramid blocks direct fire across it and can itself be fired
    ///    at (the tiles of the target never block the shot at it), and its rubble mound no longer blocks, as a destroyed building
    ///    does not. Without this no tank and no Tough could ever hit a pyramid: only arcing fire reached it.
    /// With the same switch, and for the same reason, the tiles this level closes under its LOW pieces
    /// (<see cref="L16LowTiles"/>: the four campfires, the two stairs platforms, the eight fallen columns, the two fire bowls and
    /// the two pole torches on the ground: nothing there is taller than 1.7 m or wider than a post) do not block direct fire: units
    /// cannot enter them, but they shoot and see across a campfire as across a crate. The taller ruin pieces block like cliffs.
    /// The drawn pyramid (View/L16Pyramid.cs on a DetailProp) is bound by its uid, reads these hit points for the damaged stage at
    /// half and collapses on the death event. Created right after the detail props, so every object before it keeps its id.
    /// Level 16 only (the map file is tested): levels 2 and 17 never see it. No random draw, no clock: positions only.
    /// Switch like the other deviations: ZGame sets it at every start (off in 'Zod exact'); headless default on.
    /// </summary>
    public sealed partial class ZWorld
    {
        /// <summary>DV89 switch (off = the pyramids are scenery on closed tiles: nothing to target, nothing breaks; every closed
        /// tile of the level is a cliff for direct fire, the low pieces' tiles included).</summary>
        public static bool L16PyramidsDeviation = true;
        public const string L16MapFile = "p02_bb_orig16.map";
        /// <summary>Hit points of the big and of the small pyramid (a map object has 1666). Set so that ONE heavy tank with the attack
        /// order needs 35 to 45 s for the big one and 18 to 25 s for the small one: a heavy tank's two shells of a 4.09 s cycle take
        /// about 1870 points each off a pyramid (counted: 1839 and 1891 a hit) (2500 at the middle of its square, less with the shot's scatter of 16 px).</summary>
        public static readonly int[] L16PyramidHitPoints = { 35400, 20800 };
        /// <summary>uid (= the drawn pyramid's object name) and footprint (first tile x, y, tiles wide, tiles deep).</summary>
        public static readonly string[] L16PyramidUids = { "L16_PyramidBig", "L16_PyramidSmall" };
        public static readonly int[][] L16PyramidTiles = { new[] { 93, 17, 8, 8 }, new[] { 96, 88, 6, 6 } };
        /// <summary>Tiles (x, y pairs) the level data closes under low pieces: closed for walking and driving, open for direct
        /// fire and for sight (<see cref="L16SightTiles"/>). The list of the placement data, 55 tiles.</summary>
        public static readonly int[] L16LowTiles = { 4, 4, 5, 42, 5, 43, 5, 44, 6, 42, 6, 43, 6, 44, 6, 48, 6, 102, 6, 103, 6, 104, 7, 42, 7, 43, 7, 44, 7, 46, 7, 47, 7, 48, 7, 102, 7, 103, 7, 104, 8, 46, 8, 47, 8, 48, 8, 104, 39, 0, 41, 0, 63, 90, 64, 90, 65, 90, 68, 86, 68, 88, 68, 89, 69, 88, 69, 89, 95, 41, 95, 43, 95, 125, 95, 126, 96, 43, 96, 125, 96, 126, 97, 43, 97, 81, 97, 86, 97, 87, 97, 126, 98, 40, 98, 79, 98, 81, 98, 86, 98, 87, 99, 81, 102, 40, 102, 41, 102, 42 };
        /// <summary>The pyramids created at map load (read only for the view, the sight grid and the checks).</summary>
        public readonly List<ZObject> L16Pyramids = new List<ZObject>();

        void AddL16Pyramids()
        {
            if (!L16PyramidsDeviation || Map == null || !string.Equals(Map.file, L16MapFile, StringComparison.OrdinalIgnoreCase)) return;
            for (int k = 0; k < L16PyramidUids.Length; k++)
            {
                var t = L16PyramidTiles[k];
                var o = CreateObject("map_item", DetailPropType, t[0] * 16, t[1] * 16, Team.Neutral);
                o.WidthPix = t[2] * 16; o.HeightPix = t[3] * 16; o.SetCords(t[0] * 16, t[1] * 16);
                o.MaxHealth = o.Health = L16PyramidHitPoints[k];
                o.BlockTiles = new int[0];                                             // its tiles are closed by the level data and stay closed in every stage
                o.IsDestroyableImpass = false;
                DetailProps.Add(o); DetailPropUid[o.Id] = L16PyramidUids[k]; L16Pyramids.Add(o);
            }
            sight = null;                                                              // a sight grid built before this point (none today) is built again with the pyramids in it
        }

        /// <summary>ZSight hook (its constructor, after the map's objects are entered; one line there). Level 16 with the switch on:
        /// each pyramid's square is entered under the pyramid's id, standing or destroyed (a destroyed one no longer blocks, whenever
        /// the grid is built), and the tiles of <see cref="L16LowTiles"/> that are cliffs only because the level data closes them
        /// are cleared. Any other level, or the switch off: there is no pyramid and nothing happens.</summary>
        internal void L16SightTiles(int[] tileObj, List<ZObject> blockers)
        {
            if (L16Pyramids.Count == 0 || Map == null) return;
            int w = Map.width, h = Map.height;
            foreach (var p in L16Pyramids)
            {
                for (int ty = p.LocY >> 4; ty < (p.LocY + p.HeightPix) >> 4; ty++)
                    for (int tx = p.LocX >> 4; tx < (p.LocX + p.WidthPix) >> 4; tx++)
                        if (tx >= 0 && ty >= 0 && tx < w && ty < h) tileObj[ty * w + tx] = p.Id;
                blockers.Add(p);
            }
            for (int i = 0; i + 1 < L16LowTiles.Length; i += 2)
            {
                int lx = L16LowTiles[i], ly = L16LowTiles[i + 1];
                if (lx >= 0 && ly >= 0 && lx < w && ly < h && tileObj[ly * w + lx] == -1) tileObj[ly * w + lx] = 0;
            }
        }

        /// <summary>How many tiles of a pyramid's footprint a robot or a vehicle can still enter (0 when the level data closes them all).</summary>
        public int L16PyramidOpenTiles(int k)
        {
            if (k < 0 || k >= L16PyramidTiles.Length) return 0;
            var t = L16PyramidTiles[k]; int open = 0;
            for (int x = t[0]; x < t[0] + t[2]; x++)
                for (int y = t[1]; y < t[1] + t[3]; y++)
                    if (Path.TilePassable(x, y, true) || Path.TilePassable(x, y, false)) open++;
            return open;
        }
    }
}
