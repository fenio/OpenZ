using System;
using System.Linq;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Three old car wrecks half sunk in the open L17 harbour are sim objects (ZWorld.Wrecks.cs): neutral map items
    /// with Zod's map-object hit points whose 2 water tiles each are destroyable impassables, exactly like a rock, until the wreck
    /// is destroyed. Measured headless on L17 once on and once off (Zod): wrecks, blocked tiles, the rest of the world unchanged.
    /// SESSION: save as Assets/OpenZ/Scripts/Editor/OpenZParity.HarbourWrecks.cs and add to CollectDeviations, after DevCraneGun:
    ///     try { list.Add(DevHarbourWrecks(getInt)); }                                      // l17_details_v1 (DV39)
    ///     catch (Exception ex) { list.Add(new Deviation { id = "DV39", change = "DevHarbourWrecks", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
    /// (renumber DV39 if another package took it first)
    /// </summary>
    public static partial class OpenZParity
    {
        static Deviation DevHarbourWrecks(Func<string, int, int> getInt)
        {
            bool pref = getInt("oz_l17_wrecks", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            bool was = ZWorld.HarbourWrecksDeviation;
            try
            {
                ZWorld.HarbourWrecksDeviation = true;
                var w = ZWorld.Create(SettingsText, MapText("L17"), 1, true);
                int tiles = 0, blocked = 0, hp = w.Wrecks.Count > 0 ? w.Wrecks[0].MaxHealth : 0;
                foreach (var o in w.Wrecks)
                    for (int i = o.LocX >> 4; i < (o.LocX + o.WidthPix) >> 4; i++)
                        for (int j = o.LocY >> 4; j < (o.LocY + o.HeightPix) >> 4; j++)
                        { tiles++; if (!w.Path.TilePassable(i, j, true) && w.Path.HasDestroyableBarrier(i, j)) blocked++; }
                ZWorld.HarbourWrecksDeviation = false;
                var z = ZWorld.Create(SettingsText, MapText("L17"), 1, true);
                bool same = z.Objects.Count == w.Objects.Count - w.Wrecks.Count
                            && z.Objects.Zip(w.Objects.Where(o => o.Type != ZWorld.WreckType), (a, b) => a.Id == b.Id && a.LocX == b.LocX && a.LocY == b.LocY).All(x => x);
                bool ok = w.Wrecks.Count == ZWrecksData.Ids.Length && blocked == tiles && tiles > 0 && z.Wrecks.Count == 0 && same;
                return new Deviation
                {
                    id = "DV39", change = "L17: 3 car wrecks in the open harbour water block their tiles until destroyed (like rocks)",
                    zod = "no such object; every L17 harbour / canal tile is open shallow water (robots wade at 0.7x); a destroyed vehicle leaves no sim remains",
                    openz = "ZWorld.Wrecks.cs: neutral map items \"wreck\" (map_item.map_item.health = 1666 HP, explosives only, missile splash hurts them), " +
                            "2 x 1 water tiles each set destroyable-impassable (ZPathFinder.SetImpassable(.., destroyable)): robots path round, units with explosives " +
                            "blast them on their way (zobject.cpp:3716-3747), they block EngageBarrierBetween like a rock; destroyed -> the tiles are water again " +
                            "(robots only, never driveable). Spots (ZWrecksData, tools/plan_wrecks.py): 2 open water tiles round each, 5 tiles from bridges / flags / " +
                            "buildings / vehicles; robot regions unchanged; flag / door / sample paths +1 tile at most",
                    measured = $"L17: {w.Wrecks.Count} wrecks, {blocked}/{tiles} footprint tiles blocked (destroyable), {hp} HP each; off: {z.Wrecks.Count} wrecks, " +
                               $"every Zod object identical ({(same ? "yes" : "NO")}); headless harness (l17_details_v1/tools/harness): grunts go round (+2 s), " +
                               "Toughs blast through, 30 bot matches on / off: Red 1 / Blue 29 vs Red 2 / Blue 28 (L17's known Blue lean, unchanged)",
                    toggle = $"PlayerPrefs oz_l17_wrecks = {(getInt("oz_l17_wrecks", 1) == 1 ? "1 (on, default)" : "0 (off)")}; menu OpenZ/Deviations/L17 Harbour Wrecks Block Tiles; off in 'Zod exact' (oz_zod_exact = 1); bot matches follow the same switch",
                    status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
                };
            }
            finally { ZWorld.HarbourWrecksDeviation = was; }
        }
    }
}
