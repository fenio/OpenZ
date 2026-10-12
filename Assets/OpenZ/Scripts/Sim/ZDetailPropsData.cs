namespace OpenZ.Sim
{
    /// <summary>
    /// its placement uid (= the drawn prop's GameObject name) and its Zod footprint tiles (x, y pairs).</summary>
    public static class ZDetailPropsData
    {
        public sealed class Set { public string MapFile; public string[] Uids; public int[][] Tiles; }
        public static readonly Set L02 = new Set
        {
            MapFile = "p02_bb_orig02.map",
            Uids = new[] { "D03", "D04", "D05", "D06", "D08", "SD1_Wbridge_1_PalmA", "SD1_Wbridge_2_Shrub", "SD1_Wbridge_4_StoneB", "SD1_Mid_w_b_1_CactusB", "SD1_Mid_e_b_1_StoneB", "SD1_Mid_e_b_2_CactusA", "SD1_Ebridge_2_Shrub", "SD1_Ebridge_3_StoneB", "SD1_S_mouth_b_2_StoneB", "SD1_S_mouth_b_3_CactusB", "SD1_N_spring_1_PalmA", "SD1_RedX_1_PalmA" },
            Tiles = new[] { new[] { 38, 56 }, new[] { 55, 63 }, new[] { 36, 42 }, new[] { 49, 26 }, new[] { 22, 65 }, new[] { 21, 31 }, new[] { 2, 39 }, new[] { 1, 40, 1, 41 }, new[] { 21, 27 }, new[] { 68, 62 }, new[] { 67, 49 }, new[] { 69, 73 }, new[] { 68, 42 }, new[] { 31, 87 }, new[] { 12, 88 }, new[] { 35, 2 }, new[] { 39, 49 } },
        };
        public static readonly Set L17 = new Set
        {
            MapFile = "p02_bb_orig17.map",
            Uids = new[] { "C01", "C02", "C03", "C04", "C05", "C06", "C07", "C08", "C09", "C10" },
            Tiles = new[] { new[] { 41, 98 }, new[] { 68, 98 }, new[] { 98, 10 }, new[] { 105, 54, 105, 55 }, new[] { 99, 42, 100, 42 }, new[] { 34, 26 }, new[] { 76, 26 }, new[] { 11, 10 }, new[] { 37, 26 }, new[] { 73, 26 } },
        };
        public static Set For(string mapFile)
        {
            if (string.Equals(mapFile, L02.MapFile, System.StringComparison.OrdinalIgnoreCase)) return L02;
            if (string.Equals(mapFile, L17.MapFile, System.StringComparison.OrdinalIgnoreCase)) return L17;
            return null;
        }
    }
}
