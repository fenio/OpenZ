namespace OpenZ.Sim
{
    /// <summary>DV39 harbour wrecks (ZWorld.Wrecks.cs): Zod tile footprints in the open L17 harbour water, checked on the live grid
    /// by tools/plan_wrecks.py (2 open water tiles all round, 5 tiles from bridges / flags / buildings / vehicles, robot regions
    /// unchanged, paths between 92 key points +1 tile at most).</summary>
    public static class ZWrecksData
    {
        public const string MapFile = "p02_bb_orig17.map";
        /// <summary>tile x, tile y, width, height (tiles) per wreck.</summary>
        public static readonly int[] Tiles = { 14, 93, 2, 1, 93, 90, 1, 2, 12, 116, 2, 1 };
        public static readonly string[] Ids = { "HW1", "HW2", "HW3" };
    }
}
