using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// The two robots' landing shuttle stands right of the red fort in the place of
    /// one hut of the map. In the rules it is ONE neutral map item on the path of the destructible details (DV67,
    /// ZWorld.DetailProps.cs: type <see cref="DetailPropType"/>, attacked by explosives only, hurt by missile splash, no death
    /// debris missile, never a target of the computer player or of automatic fire), with what is its own:
    ///  * the hut of the map on tile <see cref="L16ShuttleHutTile"/> leaves the rules at map load (the rules' own delete: its tile
    ///    opens as a destroyed hut's does; the events of the load are cleared, so nothing is drawn or heard). The level data is
    ///    NOT edited: the hut stays in the level file and comes back when the switch is off;
    ///  * the shuttle closes the 4 x 4 tiles of <see cref="L16ShuttleSquare"/> (8 x 8 m; the ship is 6.67 m long: a third of the
    ///    fort's side, the about 1/3 of fort size; turned 30 degrees its box is 6.7 x 7.0 m) as a hut closes
    ///    its tile: a destroyable barrier (units path round it; a unit with explosives whose way is planned through it blasts it,
    ///    which is all the computer player ever does to a hut or a rock). Only tiles that are open at that moment are closed
    ///    (all sixteen on the map as it is);
    ///  * hit points = <see cref="L16ShuttleHutsOfHealth"/> huts' (a hut's hit points for each of its sixteen tiles);
    ///  * line of sight (DV23, ZSight): a detail never blocks a shot, but this one is a 6 m hull. As the hut it replaces did on its
    ///    tile, the STANDING ship blocks direct fire and sight across its square: its sixteen tiles are entered in the sight grid
    ///    under its own id, the way the level's pyramids are (ZWorld.L16Pyramids.cs). It can itself be fired at (the tiles of the
    ///    target never block the shot at it); destroyed, it no longer blocks (<see cref="L16ShuttleBlocksFire"/>). Zod's own
    ///    barrier test (no engagement across a destroyable barrier, as across a hut or a rock) holds for its tiles in any case;
    ///  * destroyed: the object goes and its tiles open again (their map class), as a hut's tile does.
    /// It produces nothing, holds no flag, cannot be captured, entered or repaired. The drawn ship (View/L16Shuttle.cs on a
    /// DetailProp named <see cref="L16ShuttleUid"/>) is bound by that name, shows the damaged stage at half the hit points and
    /// breaks on the death event. Created after the pyramids (the last of the level's own items), so every object of the map keeps
    /// its id; the removed hut's id is simply not used. Level 16 only (the map file is tested): levels 2 and 17 never see it.
    /// The army searchlight the view puts beside the ship (View/OzFieldSearchlight.cs) is NOT in the rules: it closes no tile and
    /// has no map item (a small prop of the view, as the level's lamp posts are); <see cref="L16ShuttleSearchlightTile"/> only
    /// names the tile it stands on, for the checks (open ground, no road, no exit lane).
    /// No random draw, no clock. Switch like the other deviations: the game sets it at every start (off in 'Zod exact': the hut
    /// stands there again and the view hides the ship); headless default on.
    /// </summary>
    public sealed partial class ZWorld
    {
        /// <summary>DV95 switch (off = the map as it is: the hut on its tile, no shuttle in the rules).</summary>
        public static bool L16ShuttleDeviation = true;
        /// <summary>uid = the drawn ship's object name (the detail hub binds by it).</summary>
        public const string L16ShuttleUid = "L16_Shuttle";
        /// <summary>The ship's square: first tile x, y, tiles wide, tiles deep.</summary>
        public static readonly int[] L16ShuttleSquare = { 59, 115, 4, 4 };
        /// <summary>The tile the view's searchlight stands on, south-east of the square (nothing in the rules: the tile stays open).</summary>
        public static readonly int[] L16ShuttleSearchlightTile = { 63, 119 };
        /// <summary>The number of tiles of the ship's square (16).</summary>
        public static int L16ShuttleTiles => L16ShuttleSquare[2] * L16ShuttleSquare[3];
        /// <summary>The hut of the map the ship stands in the place of (tile x, y).</summary>
        public static readonly int[] L16ShuttleHutTile = { 61, 117 };
        /// <summary>The hut of the map beside the robots' start camp (south-east of its campfire) that leaves the rules with the ship
        /// (the owner's item 174); the same delete as the ship's own hut, the level file is not edited.</summary>
        public static readonly int[] L16CampHutTile = { 67, 119 };
        /// <summary>True when that hut was found and taken out of the rules at map load.</summary>
        public bool L16CampHutRemoved { get; private set; }
        /// <summary>Hit points as a number of huts: one hut for each tile of its square.</summary>
        public const int L16ShuttleHutsOfHealth = 16;
        /// <summary>The standing ship blocks direct fire and sight across its square, as the hut it replaces did on its tile
        /// (false = a shot at a rock or hut behind it, and a unit's eyes, pass over it as over any other detail).</summary>
        public static bool L16ShuttleBlocksFire = true;
        /// <summary>The shuttle created at map load (null on any other level or with the switch off).</summary>
        public ZObject L16Shuttle { get; private set; }
        /// <summary>True when the hut of the map was found on its tile and taken out of the rules at map load.</summary>
        public bool L16ShuttleHutRemoved { get; private set; }
        /// <summary>How many tiles the shuttle closed at map load (16 on the map as it is).</summary>
        public int L16ShuttleTilesClosed { get; private set; }
        /// <summary>A hut's hit points (map_item.hut.health x MAX_UNIT_HEALTH) x <see cref="L16ShuttleHutsOfHealth"/>.</summary>
        public int L16ShuttleHitPoints => (int)(S.hut_item_health * ZConst.MAX_UNIT_HEALTH) * L16ShuttleHutsOfHealth;

        /// <summary>ZWorld.Create hook, right after the pyramids.</summary>
        void AddL16Shuttle()
        {
            if (!L16ShuttleDeviation || Map == null || !string.Equals(Map.file, L16MapFile, StringComparison.OrdinalIgnoreCase)) return;
            // the hut of the map leaves the rules; its tile opens as a destroyed hut's does
            ZObject hut = null;
            foreach (var o in Objects)
                if (o.Kind == ZKind.MapItem && o.Type == "hut" && !o.Destroyed && o.LocX == L16ShuttleHutTile[0] * 16 && o.LocY == L16ShuttleHutTile[1] * 16) { hut = o; break; }
            if (hut != null) { DeleteObject(hut); L16ShuttleHutRemoved = true; }
            ZObject camp = null;                                                       // item 174: the hut beside the start camp leaves the rules too
            foreach (var o in Objects)
                if (o.Kind == ZKind.MapItem && o.Type == "hut" && !o.Destroyed && o.LocX == L16CampHutTile[0] * 16 && o.LocY == L16CampHutTile[1] * 16) { camp = o; break; }
            if (camp != null) { DeleteObject(camp); L16CampHutRemoved = true; }
            if (L16ShuttleReplaced()) return;                                          // DV118: the playable shuttle instead of the decoration, or none at all with the option SHUTTLE off (ZWorld.Shuttle.cs)
            var q = L16ShuttleSquare;
            var s = CreateObject("map_item", DetailPropType, q[0] * 16, q[1] * 16, Team.Neutral);
            s.WidthPix = q[2] * 16; s.HeightPix = q[3] * 16; s.SetCords(q[0] * 16, q[1] * 16);
            s.MaxHealth = s.Health = L16ShuttleHitPoints;
            var block = new List<int>();
            for (int y = q[1]; y < q[1] + q[3]; y++)
                for (int x = q[0]; x < q[0] + q[2]; x++)
                    if (Path.TilePassable(x, y, true) && !TileTakenByProp(x, y) && !TileHasObject(x, y, s)) { block.Add(x); block.Add(y); }
            s.BlockTiles = block.ToArray();
            s.IsDestroyableImpass = s.BlockTiles.Length > 0;
            if (s.IsDestroyableImpass) SetMapImpassables(s);
            L16ShuttleTilesClosed = s.BlockTiles.Length / 2; DetailPropBlockedTiles += L16ShuttleTilesClosed;
            DetailProps.Add(s); DetailPropUid[s.Id] = L16ShuttleUid; L16Shuttle = s;
            sight = null;                                                              // a sight grid built before this point (none today) is built again: without the hut, with the ship
        }

        /// <summary>ZSight hook (its constructor, beside the pyramids' line). Level 16 with the switch on: the ship's square is
        /// entered under the ship's id, standing or destroyed (a destroyed one no longer blocks, whenever the grid is built).
        /// Any other level, or the switch off: there is no ship and nothing happens.</summary>
        internal void L16ShuttleSightTiles(int[] tileObj, List<ZObject> blockers)
        {
            var s = L16Shuttle; if (s == null || !L16ShuttleBlocksFire || Map == null) return;
            int w = Map.width, h = Map.height;
            for (int ty = s.LocY >> 4; ty < (s.LocY + s.HeightPix) >> 4; ty++)
                for (int tx = s.LocX >> 4; tx < (s.LocX + s.WidthPix) >> 4; tx++)
                    if (tx >= 0 && ty >= 0 && tx < w && ty < h) tileObj[ty * w + tx] = s.Id;
            blockers.Add(s);
        }

        /// <summary>How many tiles of the ship's square block direct fire and sight now (16 while it stands, 0 when it is destroyed).</summary>
        public int L16ShuttleSightBlocked()
        {
            var q = L16ShuttleSquare; int n = 0;
            for (int y = q[1]; y < q[1] + q[3]; y++)
                for (int x = q[0]; x < q[0] + q[2]; x++)
                    if (Sight.BlockedTile(x, y)) n++;
            return n;
        }

        /// <summary>How many tiles of the ship's square a robot or a vehicle can enter now (0 while it stands, 16 when it is destroyed).</summary>
        public int L16ShuttleOpenTiles()
        {
            var q = L16ShuttleSquare; int open = 0;
            for (int y = q[1]; y < q[1] + q[3]; y++)
                for (int x = q[0]; x < q[0] + q[2]; x++)
                    if (Path.TilePassable(x, y, true) || Path.TilePassable(x, y, false)) open++;
            return open;
        }
    }
}
