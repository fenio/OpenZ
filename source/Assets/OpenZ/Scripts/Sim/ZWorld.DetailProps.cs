using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// details_v2 prop (L02 wind-pump, signs, palms, cacti, stones, shrubs; L17 traffic lights, road-works signal, bus stop, neon,
    /// hydrants, street signs) is a sim map item, on the DV39 harbour-wreck pattern (ZWorld.Wrecks.cs) and Zod's own rules for
    /// destroyable map objects (omapobject.cpp:77):
    ///  * a neutral map item of type <see cref="DetailPropType"/> with Zod's map-object hit points (map_item.map_item.health x
    ///    MAX_UNIT_HEALTH), attacked by explosives only (AttackedByExplosives from SetClassFlags: Toughs, tanks, cannons, the MLRS,
    ///    grenades; rifles cannot hurt it, exactly like a cactus or a rock) and hurt by missile splash;
    ///  * footprint = the prop's Zod tiles from the placement (ZDetailPropsData, generated from placement_L0x.json, the file the
    ///    drawn props are placed from, so sim and view cannot drift). Only tiles that were PASSABLE when the map loaded become
    ///    destroyable impassables (today only the wind-pump's tile (38, 56)); a prop on a cliff /
    ///    object tile changes no path (its tile was never walkable). Destroyed: those tiles get back their map class (the ruins
    ///    are flat, below 0.3 m);
    ///  * no sight block, no death debris missile (Zod throws one for map_object types only), never a bot or auto target (ZBot
    ///    lists only flags / crates / buildings / units; auto-targeting skips neutrals).
    /// Created in ZWorld.Create right after the harbour wrecks, so every Zod object keeps its id. Static switch like the other
    /// deviations: ZGame sets it at every start (off in 'Zod exact'); headless matches and the parity suite use the default (on).
    /// </summary>
    public sealed partial class ZWorld
    {
        /// <summary>DV67 switch (off = Zod: the details are decor only, nothing to target, the yellow-circle tile stays open).</summary>
        public static bool DetailPropsDeviation = true;
        public const string DetailPropType = "detail_prop";
        /// <summary>The detail props created at map load (read only for the view: DetailPropHub binds the drawn props to them).</summary>
        public readonly List<ZObject> DetailProps = new List<ZObject>();
        /// <summary>sim id -> placement uid (the drawn prop's GameObject name).</summary>
        public readonly Dictionary<int, string> DetailPropUid = new Dictionary<int, string>();
        /// <summary>Tiles (x, y pairs) each prop really blocks (were passable at load).</summary>
        public int DetailPropBlockedTiles { get; private set; }

        void AddDetailProps()
        {
            if (!DetailPropsDeviation || Map == null) return;
            var set = ZDetailPropsData.For(Map.file);
            if (set == null) return;
            for (int k = 0; k < set.Uids.Length; k++)
            {
                var t = set.Tiles[k];
                if (t == null || t.Length < 2) continue;
                int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
                for (int i = 0; i + 1 < t.Length; i += 2) { x0 = Math.Min(x0, t[i]); y0 = Math.Min(y0, t[i + 1]); x1 = Math.Max(x1, t[i]); y1 = Math.Max(y1, t[i + 1]); }
                var o = CreateObject("map_item", DetailPropType, x0 * 16, y0 * 16, Team.Neutral);
                o.WidthPix = (x1 - x0 + 1) * 16; o.HeightPix = (y1 - y0 + 1) * 16; o.SetCords(x0 * 16, y0 * 16);
                o.MaxHealth = o.Health = (int)(S.map_item_health * ZConst.MAX_UNIT_HEALTH);   // Zod map object HP
                var block = new List<int>();
                for (int i = 0; i + 1 < t.Length; i += 2)
                    if (Path.TilePassable(t[i], t[i + 1], true) && !TileTakenByProp(t[i], t[i + 1]) && !TileHasObject(t[i], t[i + 1], o)) { block.Add(t[i]); block.Add(t[i + 1]); }
                o.BlockTiles = block.ToArray();
                o.IsDestroyableImpass = o.BlockTiles.Length > 0;
                if (o.IsDestroyableImpass) SetMapImpassables(o);
                DetailPropBlockedTiles += o.BlockTiles.Length / 2;
                DetailProps.Add(o); DetailPropUid[o.Id] = set.Uids[k];
            }
        }

        bool TileTakenByProp(int x, int y)
        {
            foreach (var p in DetailProps)
                if (p.BlockTiles != null) for (int i = 0; i + 1 < p.BlockTiles.Length; i += 2) if (p.BlockTiles[i] == x && p.BlockTiles[i + 1] == y) return true;
            return false;
        }

        bool TileHasObject(int x, int y, ZObject self)
        {
            var probe = new ZObject { WidthPix = 16, HeightPix = 16 }; probe.SetCords(x * 16, y * 16);
            foreach (var o in Objects) if (o != self && !o.Destroyed && o.Kind != ZKind.MapItem && probe.IntersectsObject(o)) return true;
            return false;
        }

        /// <summary>SetMapImpassables for a detail prop: only the tiles it really blocks; cleared, each gets back its map class
        /// (InitPathfinding), as the harbour wrecks do.</summary>
        void DetailPropImpassables(ZObject o, bool set)
        {
            if (treeTileIds.Contains(o.Id)) { TreeTileImpassables(o, set); return; }                 // DV88: a tree tile opens as a rock's tile does (ZWorld.TreeTiles.cs); every other prop as before
            var b = o.BlockTiles; if (b == null) return;
            for (int i = 0; i + 1 < b.Length; i += 2)
            {
                int x = b[i], y = b[i + 1];
                if (set) { Path.SetImpassable(x, y, true, true); continue; }
                var c = Map.ClassAt(x, y);
                Path.SetTileInfo(x, y, c == ZClass.Water ? PfTile.Water : c == ZClass.Road ? PfTile.Road : c == ZClass.Ground ? PfTile.Normal : PfTile.Impassable);
            }
        }

        /// <summary>ZObject.CausesImpassAtCoord for a detail prop: a tile it blocks.</summary>
        internal static bool DetailPropCausesImpassAt(ZObject o, int x, int y)
        {
            var b = o.BlockTiles; if (b == null) return false;
            int px = x >> 4, py = y >> 4;
            for (int i = 0; i + 1 < b.Length; i += 2) if (b[i] == px && b[i + 1] == py) return true;
            return false;
        }
    }
}
