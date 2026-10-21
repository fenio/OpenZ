using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Level 16 "Jungle": where its parrots circle, where its parrot groups sit, where its monkeys sit and run to, where its snakes
    /// live (world metres: x east, z negative to the south). Written by a tool from the level's own placement list, the plants the
    /// game stands and the level data, and checked there (ground spots 8 m clear, monkey runs on walkable ground only, snake places
    /// off roads and away from buildings). If the level data or the plants change, run the tool again. Used by JungleFauna.cs.
    /// </summary>
    public static class JungleFaunaData
    {
        /// <summary>Parrots in the air (they take the eagle's place: its count, its circles): per bird its circles (x, z, radius) and height (m).</summary>
        public static readonly Vector3[][] ParrotRoutes =
        {
            new[] { new Vector3(84.0f, -30.0f, 14.0f), new Vector3(96.0f, -86.0f, 16.0f), new Vector3(172.0f, -48.0f, 18.0f), new Vector3(160.5f, -148.5f, 14.0f) },
            new[] { new Vector3(160.5f, -148.5f, 16.0f), new Vector3(186.0f, -186.0f, 18.0f), new Vector3(121.0f, -224.0f, 14.0f) },
            new[] { new Vector3(121.0f, -224.0f, 18.0f), new Vector3(89.0f, -184.0f, 14.0f), new Vector3(84.0f, -30.0f, 16.0f) },
        };
        public static readonly float[] ParrotAlt = { 17.2f, 15.2f, 15.5f };

        /// <summary>Sitting spots on open ground (x, z): 8 m or more from roads, water, buildings and flags. The first five carry a group at the start.</summary>
        public static readonly Vector2[] GroundSpots = { new Vector2(118.98f, -203.17f), new Vector2(193.08f, -198.94f), new Vector2(153.00f, -160.75f), new Vector2(120.90f, -219.21f), new Vector2(90.89f, -53.16f), new Vector2(11.00f, -157.00f), new Vector2(191.00f, -75.00f), new Vector2(29.00f, -245.00f), new Vector2(13.00f, -17.00f), new Vector2(33.00f, -87.00f) };

        /// <summary>Sitting spots in trees: the trunk's foot (x, z), the tile the tree closes, and its seats (the highest points of the crown). The first five carry a group at the start.</summary>
        public static readonly Vector2[] TreeSpots = { new Vector2(149.35f, -6.98f), new Vector2(89.69f, -133.67f), new Vector2(138.16f, -97.55f), new Vector2(172.92f, -249.29f), new Vector2(200.52f, -21.08f), new Vector2(200.84f, -155.38f), new Vector2(56.07f, -169.76f), new Vector2(34.28f, -205.07f), new Vector2(130.62f, -54.90f), new Vector2(61.00f, -27.00f) };
        public static readonly Vector2Int[] TreeSpotTiles = { new Vector2Int(74, 3), new Vector2Int(44, 66), new Vector2Int(69, 48), new Vector2Int(86, 124), new Vector2Int(100, 10), new Vector2Int(100, 77), new Vector2Int(28, 84), new Vector2Int(17, 102), new Vector2Int(65, 27), new Vector2Int(30, 13) };
        public static readonly Vector3[][] TreeSeats =
        {
            new[] { new Vector3(148.74f, 7.15f, -6.88f), new Vector3(149.63f, 7.15f, -6.23f), new Vector3(149.67f, 7.11f, -7.47f), new Vector3(148.75f, 6.82f, -7.93f), new Vector3(150.53f, 6.70f, -6.84f), new Vector3(150.67f, 6.60f, -7.88f) },   // TreeCanopyA
            new[] { new Vector3(89.65f, 0.10f, -134.66f), new Vector3(90.42f, -0.13f, -134.06f), new Vector3(88.46f, -0.17f, -134.06f), new Vector3(90.67f, -0.39f, -134.98f), new Vector3(89.22f, -0.42f, -133.18f), new Vector3(90.78f, -0.46f, -133.25f) },   // PalmShortBushyA
            new[] { new Vector3(138.32f, 5.14f, -97.37f), new Vector3(137.76f, 5.01f, -98.02f), new Vector3(138.78f, 4.87f, -98.20f), new Vector3(137.81f, 4.84f, -96.59f), new Vector3(137.22f, 4.67f, -97.32f), new Vector3(139.16f, 4.64f, -97.17f) },   // TreeCanopyB
            new[] { new Vector3(172.54f, 6.32f, -249.38f), new Vector3(172.90f, 6.32f, -248.79f), new Vector3(173.22f, 6.29f, -249.50f), new Vector3(172.79f, 6.08f, -249.99f), new Vector3(173.57f, 6.00f, -248.93f), new Vector3(173.89f, 5.93f, -249.50f) },   // TreeCanopyA
            new[] { new Vector3(200.27f, 9.46f, -21.25f), new Vector3(201.15f, 9.31f, -20.65f), new Vector3(199.99f, 9.15f, -20.12f), new Vector3(200.62f, 9.12f, -22.34f), new Vector3(201.57f, 8.93f, -21.66f), new Vector3(199.20f, 8.90f, -21.22f) },   // TreeCanopyB
            new[] { new Vector3(200.32f, 11.49f, -155.60f), new Vector3(200.71f, 11.49f, -154.66f), new Vector3(201.33f, 11.45f, -155.61f), new Vector3(200.83f, 11.14f, -156.42f), new Vector3(201.70f, 11.02f, -154.71f), new Vector3(202.30f, 10.91f, -155.46f) },   // TreeCanopyA
            new[] { new Vector3(55.88f, 5.30f, -169.86f), new Vector3(56.12f, 5.17f, -169.03f), new Vector3(56.91f, 5.04f, -170.91f), new Vector3(56.71f, 5.00f, -169.93f), new Vector3(55.49f, 4.88f, -170.68f), new Vector3(56.18f, 4.40f, -171.45f) },   // TreeSwampMossB
            new[] { new Vector3(34.70f, 4.45f, -204.98f), new Vector3(34.28f, 4.45f, -205.62f), new Vector3(33.95f, 4.42f, -204.83f), new Vector3(34.43f, 4.22f, -204.30f), new Vector3(33.55f, 4.14f, -205.45f), new Vector3(33.21f, 4.06f, -204.81f) },   // TreeCanopyA
            new[] { new Vector3(130.84f, 4.94f, -55.07f), new Vector3(130.08f, 4.81f, -54.50f), new Vector3(129.96f, 4.68f, -55.63f), new Vector3(131.66f, 4.65f, -54.46f), new Vector3(130.81f, 4.49f, -53.86f), new Vector3(131.11f, 4.46f, -55.98f) },   // TreeCanopyB
            new[] { new Vector3(61.13f, 2.84f, -26.91f), new Vector3(61.07f, 2.54f, -26.13f), new Vector3(61.91f, 1.98f, -26.84f), new Vector3(61.04f, 0.98f, -27.98f) },   // TreeDeadB
        };
        /// <summary>Birds per group at the start (ground groups, then tree groups).</summary>
        public static readonly int[] GroupSize = { 5, 4, 6, 3, 5, 4, 3, 5, 4, 3 };

        /// <summary>Monkey troops: the trunk's foot (x, z), the tile the tree closes, three seats, and up to three ways to run:
        /// the landing place first, the last point inside dense trees or beyond the map's edge.</summary>
        public static readonly Vector2[] TroopTrees = { new Vector2(109.05f, -202.00f), new Vector2(125.00f, -193.00f), new Vector2(193.00f, -167.00f), new Vector2(155.00f, -185.00f), new Vector2(79.43f, -85.26f), new Vector2(11.83f, -187.35f) };
        public static readonly Vector2Int[] TroopTiles = { new Vector2Int(54, 101), new Vector2Int(62, 96), new Vector2Int(96, 83), new Vector2Int(77, 92), new Vector2Int(39, 42), new Vector2Int(5, 93) };
        public static readonly Vector3[][] TroopSeats =
        {
            new[] { new Vector3(108.94f, 4.75f, -202.28f), new Vector3(109.61f, 4.65f, -201.78f), new Vector3(112.31f, 5.89f, -202.44f) },   // TreeCanopyC, 1 of the list
            new[] { new Vector3(124.95f, 3.70f, -193.22f), new Vector3(124.22f, 3.75f, -192.73f), new Vector3(125.69f, 3.72f, -193.65f) },   // TreeCanopyB, 1 of the list
            new[] { new Vector3(193.18f, 2.95f, -167.11f), new Vector3(192.40f, 2.49f, -166.06f), new Vector3(191.35f, 3.68f, -163.52f) },   // TreeCanopyB, 3 of the list
            new[] { new Vector3(154.74f, 4.84f, -185.13f), new Vector3(152.55f, 4.46f, -182.94f), new Vector3(153.19f, 4.46f, -182.45f) },   // TreeButtressA, by rule
            new[] { new Vector3(79.27f, 4.84f, -85.42f), new Vector3(79.17f, 4.35f, -84.11f), new Vector3(78.90f, 5.08f, -88.74f) },   // TreeCanopyB, 3 of the list
            new[] { new Vector3(11.89f, 5.20f, -187.70f), new Vector3(12.26f, 4.73f, -186.78f), new Vector3(11.34f, 4.71f, -187.07f) },   // TreeCanopyC, by rule
        };
        public static readonly Vector2[][][] TroopWays =
        {
            new[]
            {
                new[] { new Vector2(111.53f, -199.53f), new Vector2(131.00f, -197.00f), new Vector2(133.00f, -195.00f) },   // 21 m, into the trees
                new[] { new Vector2(105.55f, -202.00f), new Vector2(77.00f, -225.00f), new Vector2(75.00f, -227.00f) },   // 41 m, into the trees
            },
            new[]
            {
                new[] { new Vector2(128.50f, -193.00f), new Vector2(151.00f, -189.00f), new Vector2(153.00f, -187.00f) },   // 26 m, into the trees
                new[] { new Vector2(125.00f, -196.50f), new Vector2(151.00f, -231.00f), new Vector2(153.00f, -233.00f) },   // 47 m, into the trees
                new[] { new Vector2(121.50f, -193.00f), new Vector2(91.00f, -197.00f), new Vector2(87.00f, -199.00f), new Vector2(77.00f, -199.00f), new Vector2(69.00f, -191.00f), new Vector2(67.00f, -189.00f) },   // 60 m, into the trees
            },
            new[]
            {
                new[] { new Vector2(190.53f, -164.53f), new Vector2(189.00f, -165.00f), new Vector2(191.00f, -145.00f), new Vector2(193.00f, -143.00f) },   // 23 m, into the trees
                new[] { new Vector2(189.50f, -167.00f), new Vector2(177.00f, -185.00f), new Vector2(169.00f, -187.00f), new Vector2(157.00f, -187.00f), new Vector2(155.00f, -185.00f) },   // 47 m, into the trees
            },
            new[]
            {
                new[] { new Vector2(151.50f, -185.00f), new Vector2(137.00f, -193.00f), new Vector2(135.00f, -195.00f) },   // 20 m, into the trees
                new[] { new Vector2(157.47f, -187.47f), new Vector2(153.00f, -201.00f), new Vector2(153.00f, -231.00f), new Vector2(153.00f, -233.00f) },   // 48 m, into the trees
                new[] { new Vector2(158.50f, -185.00f), new Vector2(177.00f, -197.00f), new Vector2(201.00f, -199.00f), new Vector2(203.00f, -197.00f) },   // 51 m, into the trees
            },
            new[]
            {
                new[] { new Vector2(76.96f, -82.79f), new Vector2(61.00f, -65.00f), new Vector2(59.00f, -63.00f) },   // 27 m, into the trees
            },
            new[]
            {
                new[] { new Vector2(15.33f, -187.35f), new Vector2(23.00f, -193.00f), new Vector2(33.00f, -203.00f), new Vector2(35.00f, -205.00f) },   // 27 m, into the trees
                new[] { new Vector2(14.31f, -184.88f), new Vector2(43.00f, -173.00f), new Vector2(55.00f, -171.00f), new Vector2(57.00f, -169.00f) },   // 48 m, into the trees
            },
        };

        /// <summary>Snake dens on swamp banks (x, z), each with its walk along the bank and, where the water across is 4 to 16 m
        /// wide, the bank it swims to (x = NaN: this one does not swim).</summary>
        public static readonly Vector2[] Dens = { new Vector2(201.00f, -189.00f), new Vector2(65.00f, -23.00f), new Vector2(79.00f, -127.00f), new Vector2(7.00f, -111.00f), new Vector2(203.00f, -31.00f), new Vector2(177.00f, -109.00f) };
        public static readonly Vector2[][] DenWalks =
        {
            new[] { new Vector2(201.00f, -189.00f), new Vector2(203.00f, -189.00f), new Vector2(205.00f, -191.00f), new Vector2(207.00f, -193.00f), new Vector2(207.00f, -191.00f) },
            new[] { new Vector2(65.00f, -23.00f), new Vector2(65.00f, -21.00f), new Vector2(67.00f, -19.00f), new Vector2(69.00f, -17.00f), new Vector2(71.00f, -15.00f) },
            new[] { new Vector2(79.00f, -127.00f), new Vector2(81.00f, -129.00f), new Vector2(81.00f, -131.00f), new Vector2(81.00f, -133.00f), new Vector2(79.00f, -133.00f) },
            new[] { new Vector2(7.00f, -111.00f), new Vector2(5.00f, -109.00f), new Vector2(5.00f, -107.00f), new Vector2(5.00f, -105.00f), new Vector2(3.00f, -103.00f) },
            new[] { new Vector2(203.00f, -31.00f), new Vector2(201.00f, -31.00f) },
            new[] { new Vector2(177.00f, -109.00f), new Vector2(177.00f, -107.00f), new Vector2(177.00f, -105.00f), new Vector2(177.00f, -103.00f) },
        };
        public static readonly Vector2[] DenSwims = { new Vector2(float.NaN, 0f), new Vector2(float.NaN, 0f), new Vector2(71.00f, -119.00f), new Vector2(17.00f, -101.00f), new Vector2(203.00f, -39.00f), new Vector2(185.00f, -101.00f) };
    }
}
