using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// OpenZ deviation DV88 "the trees that close a tile can be felled" (level 16). Every tall tree, mangrove or vine of the dressed
    /// level that stands on a tile units could enter is a map object of the rules, built on the pattern of the destructible details
    /// (DV67, ZWorld.DetailProps.cs: the Desert palms are the same kind of object) and treated like a rock tile:
    ///  * a neutral map item of type <see cref="DetailPropType"/> on its one tile, with <see cref="TreeHealthOfRock"/> of a rock's hit
    ///    points (map_item.rock.health x MAX_UNIT_HEALTH = 1250, so 625): attacked by explosives only (AttackedByExplosives from
    ///    SetClassFlags: Toughs, tanks, guns, the mobile missile launcher, grenades; rifles cannot hurt it, exactly like a rock) and
    ///    hurt by every missile splash. One aimed explosive hit fells it (Zod's aim scatter of 16 px leaves at least 723 of a Tough's
    ///    1666 points); a rock takes one or two;
    ///  * its tile is a destroyable barrier like a rock's (ZPathFinder.SetImpassable(x, y, true, destroyable: true)): robots and
    ///    vehicles path round it on their normal grids; units with explosives plan through it and blast it when they reach it
    ///    (DoAttackImpassableAtCoords, zobject.cpp:3716-3747), which is also all the CPU ever does to a rock; it blocks
    ///    EngageBarrierBetween like a rock, and the view keeps drawn units off it (ZClearance.ViewTile);
    ///  * destroyed: the Death event, KillAt, DeleteObject -> SetMapImpassables(o, false) opens the tile as a rock's opens: on ground
    ///    only the passable flags change (the path weights of the map load stay, orock.cpp:678-686); a swamp tile gets its water rule
    ///    back (robots only), as the harbour wrecks do.
    /// The level data no longer closes these tiles: they carry the class of the map file again (ground or swamp), so the regions of
    /// the map load contain them as they contain a rock's tile, and an opened tile is ordinary ground for every other rule. With no
    /// tree destroyed the same tiles are closed as before. The list (ZTreeTilesData) is generated from the scenery list that closed
    /// them, with the scene object name of each tree, so the rules and the drawn trees cannot drift apart.
    /// Created in ZWorld.Create right after the destructible details, so every other object keeps its id. Like a detail prop: no
    /// sight block, no death debris missile, never a CPU or automatic target. Static switch like the other deviations (off = the
    /// tiles are open ground and nothing stands in the rules; the game never turns it off).
    /// </summary>
    public sealed partial class ZWorld
    {
        /// <summary>DV88 switch (off = no tree objects: the tiles under the trees are open, as the map file has them).</summary>
        public static bool TreeTilesDeviation = true;
        /// <summary>A tree's hit points as a share of a rock's: clearly weaker than a rock, the same class of health (map items).</summary>
        public const double TreeHealthOfRock = 0.5;
        /// <summary>The tree objects created at map load, in the list's order (read only for the view and the checks).</summary>
        public readonly List<ZObject> TreeTiles = new List<ZObject>();
        /// <summary>How many of them closed their tile at map load (the tile was open in the level data and nothing stood on it).</summary>
        public int TreeTilesClosed { get; private set; }
        readonly HashSet<int> treeTileIds = new HashSet<int>();

        /// <summary>Hit points of a tree (625 with Zod's settings).</summary>
        public int TreeHealth => (int)(S.rock_item_health * TreeHealthOfRock * ZConst.MAX_UNIT_HEALTH);
        /// <summary>Is this object one of the trees (standing or destroyed)?</summary>
        public bool IsTreeTile(ZObject o) => o != null && treeTileIds.Contains(o.Id);

        /// <summary>ZWorld.Create hook, right after the destructible details.</summary>
        void AddTreeTiles()
        {
            if (!TreeTilesDeviation || Map == null) return;
            var set = ZTreeTilesData.For(Map.file);
            if (set == null) return;
            for (int k = 0; k < set.Uids.Length && 2 * k + 1 < set.Tiles.Length; k++)
            {
                int x = set.Tiles[2 * k], y = set.Tiles[2 * k + 1];
                var o = CreateObject("map_item", DetailPropType, x * 16, y * 16, Team.Neutral);
                o.MaxHealth = o.Health = TreeHealth;
                treeTileIds.Add(o.Id);
                bool closes = Path.TilePassable(x, y, true) && !TileTakenByProp(x, y) && !TileHasObject(x, y, o);   // as a detail prop: only a tile that was open at load
                o.BlockTiles = closes ? new[] { x, y } : new int[0];
                o.IsDestroyableImpass = closes;
                if (closes) { SetMapImpassables(o); TreeTilesClosed++; DetailPropBlockedTiles++; }
                TreeTiles.Add(o); DetailProps.Add(o); DetailPropUid[o.Id] = set.Uids[k];                          // the view binds the drawn tree by its name (DetailPropHub)
            }
        }

        /// <summary>SetMapImpassables for a tree tile (called from DetailPropImpassables). Set: a destroyable barrier like a rock's
        /// tile. Cleared: ground and road open as a rock's tile does; shallow water gets its own path rule back (vehicles never
        /// enter water, so Zod's SetImpassable(false) would be wrong there: ZWorld.Wrecks.cs).</summary>
        void TreeTileImpassables(ZObject o, bool set)
        {
            var b = o.BlockTiles; if (b == null || b.Length < 2) return;
            int x = b[0], y = b[1];
            if (set) { Path.SetImpassable(x, y, true, true); return; }
            var c = Map.ClassAt(x, y);
            if (c == ZClass.Ground || c == ZClass.Road) Path.SetImpassable(x, y, false);
            else Path.SetTileInfo(x, y, c == ZClass.Water ? PfTile.Water : PfTile.Impassable);
        }
    }
}
