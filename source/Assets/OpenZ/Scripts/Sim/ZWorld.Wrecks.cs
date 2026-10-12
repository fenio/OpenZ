using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// The old car wrecks half sunk in the open L17 harbour water are sim objects. Zod has no such object (a
    /// destroyed vehicle leaves no sim remains; OpenZ's burning hulls are view only), so each wreck uses Zod's own rules for
    /// destroyable map barriers (rocks, huts, map objects: orock.cpp:678-686, ohut.cpp:247, omapobject.cpp:77):
    ///  * a neutral map item of type <see cref="WreckType"/> with Zod's map-object hit points (map_item.map_item.health = 0.166667
    ///    x MAX_UNIT_HEALTH = 1667 HP), attacked by explosives only (Toughs, tanks, cannons, the MLRS, grenades) and hurt by any
    ///    missile splash, like a hut or a city block;
    ///  * its whole footprint (2 x 1 or 1 x 2 shallow-water tiles) is a destroyable impassable (ZPathFinder.SetImpassable(x, y,
    ///    true, destroyable: true)): robots path round it on their normal grid; units with explosives plan through it on the
    ///    no-rocks grid and blast it when they reach it (DoAttackImpassableAtCoords, zobject.cpp:3716-3747); it blocks
    ///    EngageBarrierBetween like a rock; the view keeps drawn units off it (ZClearance.ViewTile: HasDestroyableBarrier);
    ///  * destroyed: the Death event, KillAt, DeleteObject -> SetMapImpassables(o, false) opens every footprint tile again. No
    ///    debris missile (Zod throws one for map_object types only; the view throws the panels).
    /// Vehicles never enter water tiles, so only robots are affected. The spots (ZWrecksData, generated from
    /// tools/plan_wrecks.py) keep 2 open water tiles all round and 5 tiles from every bridge, flag, building, vehicle and cannon,
    /// never split a robot region and lengthen no path between flags, doors and 60 sample tiles by more than 1 tile.
    /// Static switch like the other deviations: ZGame sets it at every start (PlayerPrefs oz_l17_wrecks, off in 'Zod exact');
    /// headless bot matches and the parity suite use the default (on). Needs the 3 one-line hooks apply.py adds (ZWorld.Create,
    /// ZWorld.SetMapImpassables, ZObject.CausesImpassAtCoord).
    /// </summary>
    public sealed partial class ZWorld
    {
        /// <summary>DV39 switch (off = Zod: no wreck objects, the harbour water is open everywhere).</summary>
        public static bool HarbourWrecksDeviation = true;
        public const string WreckType = "wreck";
        /// <summary>The harbour wrecks created at map load (read only for the view: HarbourDetails binds its models to them).</summary>
        public readonly List<ZObject> Wrecks = new List<ZObject>();

        /// <summary>ZWorld.Create hook, right after the map objects (so every Zod object keeps its id; the wrecks come next).</summary>
        void AddHarbourWrecks()
        {
            if (!HarbourWrecksDeviation || Map == null || !string.Equals(Map.file, ZWrecksData.MapFile, StringComparison.OrdinalIgnoreCase)) return;
            var t = ZWrecksData.Tiles;
            for (int k = 0; k + 3 < t.Length; k += 4)
            {
                int tx = t[k], ty = t[k + 1], w = t[k + 2], h = t[k + 3];
                if (!WreckSpotFree(tx, ty, w, h)) continue;                                   // defensive: never on land, a unit or an object
                var o = CreateObject("map_item", WreckType, tx * 16, ty * 16, Team.Neutral);
                o.WidthPix = w * 16; o.HeightPix = h * 16; o.SetCords(tx * 16, ty * 16);
                o.IsDestroyableImpass = true;                                                 // SetClassFlags gave AttackedByExplosives
                o.MaxHealth = o.Health = (int)(S.map_item_health * ZConst.MAX_UNIT_HEALTH);   // Zod map object HP (1667)
                SetMapImpassables(o);                                                         // the whole footprint, destroyable
                Wrecks.Add(o);
            }
        }

        /// <summary>SetMapImpassables for a wreck (case <see cref="WreckType"/>): every footprint tile, destroyable like a rock.
        /// Cleared (the wreck destroyed), each tile gets back exactly its map class's path info (InitPathfinding): Zod's
        /// SetImpassable(false) would open the vehicle grids too, which is right for a rock on ground but would make a water tile
        /// driveable (vehicles never enter water).</summary>
        void WreckImpassables(ZObject o, bool set)
        {
            int tx = o.LocX / 16, ty = o.LocY / 16, w = Math.Max(1, o.WidthPix / 16), h = Math.Max(1, o.HeightPix / 16);
            for (int i = tx; i < tx + w; i++)
                for (int j = ty; j < ty + h; j++)
                {
                    if (set) { Path.SetImpassable(i, j, true, true); continue; }
                    var c = Map.ClassAt(i, j);
                    Path.SetTileInfo(i, j, c == ZClass.Water ? PfTile.Water : c == ZClass.Road ? PfTile.Road : c == ZClass.Ground ? PfTile.Normal : PfTile.Impassable);
                }
        }

        bool WreckSpotFree(int tx, int ty, int w, int h)
        {
            for (int i = tx; i < tx + w; i++)
                for (int j = ty; j < ty + h; j++)
                    if (Map.ClassAt(i, j) != ZClass.Water || !Path.TilePassable(i, j, true)) return false;
            var probe = new ZObject { WidthPix = w * 16, HeightPix = h * 16 }; probe.SetCords(tx * 16, ty * 16);
            foreach (var o in Objects) if (!o.Destroyed && probe.IntersectsObject(o)) return false;
            return true;
        }

        /// <summary>ZObject.CausesImpassAtCoord for a wreck: any tile of its footprint (a rock blocks only its bottom tile).</summary>
        internal static bool WreckCausesImpassAt(ZObject o, int x, int y)
        {
            int tx = o.LocX >> 4, ty = o.LocY >> 4, px = x >> 4, py = y >> 4;
            return px >= tx && px < tx + Math.Max(1, o.WidthPix >> 4) && py >= ty && py < ty + Math.Max(1, o.HeightPix >> 4);
        }
    }
}
