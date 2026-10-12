// OpenZ frame: x east, z north (tile centre x = tx*2+1, z = -(ty*2+1)); heights come from the terrain at run time.
using UnityEngine;

namespace OpenZ
{
    public static class HarbourDetailsData
    {
        /// <summary>Red floor lights: x, z, outward normal x, z (towards the water). 67 lights on 20 quay runs,
        /// 4 m apart, 0.38 m in from the embankment top; flush (<= 3 cm), every spot checked free and flat (L17_details.json).</summary>
        public static readonly Vector4[] Lights =
        {
            new Vector4(10f, -160.52f, 0f, -1f), new Vector4(14f, -160.52f, 0f, -1f), new Vector4(22f, -160.52f, 0f, -1f),
            new Vector4(26f, -160.52f, 0f, -1f), new Vector4(30f, -160.52f, 0f, -1f), new Vector4(45f, -160.52f, 0f, -1f),
            new Vector4(57f, -160.52f, 0f, -1f), new Vector4(61f, -160.52f, 0f, -1f), new Vector4(73f, -160.52f, 0f, -1f),
            new Vector4(77f, -160.52f, 0f, -1f), new Vector4(147f, -160.52f, 0f, -1f), new Vector4(151f, -160.52f, 0f, -1f),
            new Vector4(155f, -160.52f, 0f, -1f), new Vector4(163f, -160.52f, 0f, -1f), new Vector4(167f, -160.52f, 0f, -1f),
            new Vector4(171f, -160.52f, 0f, -1f), new Vector4(175f, -160.52f, 0f, -1f), new Vector4(179f, -160.52f, 0f, -1f),
            new Vector4(183f, -160.52f, 0f, -1f), new Vector4(187f, -160.52f, 0f, -1f), new Vector4(191f, -160.52f, 0f, -1f),
            new Vector4(195f, -160.52f, 0f, -1f), new Vector4(199f, -160.52f, 0f, -1f), new Vector4(203f, -160.52f, 0f, -1f),
            new Vector4(207f, -160.52f, 0f, -1f), new Vector4(54f, -238.52f, 0f, -1f), new Vector4(58f, -238.52f, 0f, -1f),
            new Vector4(62f, -238.52f, 0f, -1f), new Vector4(66f, -238.52f, 0f, -1f), new Vector4(76f, -238.52f, 0f, -1f),
            new Vector4(80f, -238.52f, 0f, -1f), new Vector4(96f, -238.52f, 0f, -1f), new Vector4(120f, -238.52f, 0f, -1f),
            new Vector4(124f, -238.52f, 0f, -1f), new Vector4(141f, -238.52f, 0f, -1f), new Vector4(145f, -238.52f, 0f, -1f),
            new Vector4(157f, -238.52f, 0f, -1f), new Vector4(161f, -238.52f, 0f, -1f), new Vector4(165f, -238.52f, 0f, -1f),
            new Vector4(58f, -173.38f, 0f, 1f), new Vector4(62f, -173.38f, 0f, 1f), new Vector4(70f, -173.38f, 0f, 1f),
            new Vector4(74f, -173.38f, 0f, 1f), new Vector4(78f, -173.38f, 0f, 1f), new Vector4(103f, -173.38f, 0f, 1f),
            new Vector4(117f, -173.38f, 0f, 1f), new Vector4(154f, -173.38f, 0f, 1f), new Vector4(158f, -173.38f, 0f, 1f),
            new Vector4(162f, -173.38f, 0f, 1f), new Vector4(104.52f, -174f, 1f, 0f), new Vector4(104.52f, -178f, 1f, 0f),
            new Vector4(104.52f, -182f, 1f, 0f), new Vector4(104.52f, -202f, 1f, 0f), new Vector4(104.52f, -206f, 1f, 0f),
            new Vector4(104.52f, -210f, 1f, 0f), new Vector4(104.52f, -214f, 1f, 0f), new Vector4(104.52f, -218f, 1f, 0f),
            new Vector4(104.52f, -222f, 1f, 0f), new Vector4(115.38f, -174f, -1f, 0f), new Vector4(115.38f, -178f, -1f, 0f),
            new Vector4(115.38f, -182f, -1f, 0f), new Vector4(115.38f, -202f, -1f, 0f), new Vector4(115.38f, -206f, -1f, 0f),
            new Vector4(115.38f, -210f, -1f, 0f), new Vector4(115.38f, -214f, -1f, 0f), new Vector4(115.38f, -218f, -1f, 0f),
            new Vector4(115.38f, -222f, -1f, 0f),
        };
        /// <summary>Blink phase index of each light along its run (x HarbourDetails.blinkPhaseStep = a slow travelling wave).</summary>
        public static readonly float[] LightPhase = { 0f, 1f, 3f, 4f, 5f, 1f, 0f, 1f, 0f, 1f, 1f, 2f, 3f, 5f, 6f, 7f, 8f, 9f, 10f, 11f, 12f, 13f, 14f, 15f, 16f, 0f, 1f, 2f, 3f, 0f, 1f, 0f, 1f, 2f, 0f, 1f, 0f, 1f, 2f, 1f, 2f, 0f, 1f, 2f, 0f, 0f, 1f, 2f, 3f, 0f, 1f, 2f, 1f, 2f, 3f, 4f, 5f, 6f, 0f, 1f, 2f, 1f, 2f, 3f, 4f, 5f, 6f };

        /// <summary>Sewage outfalls: point on the embankment face (x, z) + outward normal (x, z).</summary>
        public static readonly Vector4[] Outfalls =
        {
            new Vector4(21f, -161.4f, 0f, -1f), new Vector4(191f, -161.4f, 0f, -1f),
            new Vector4(105.4f, -211f, 1f, 0f), new Vector4(118.6f, -114f, -1f, 0f),
        };
        public static readonly string[] OutfallIds = { "O1", "O2", "O3", "O4" };

        /// <summary>Lit floor grates: x, z, yaw (deg; local X = 1.0 m side along the quay).</summary>
        public static readonly Vector4[] Grates =
        {
            new Vector4(21f, -158.6f, 180f, 0f), new Vector4(191f, -158.6f, 180f, 0f),
            new Vector4(102.6f, -211f, 90f, 0f), new Vector4(121.3f, -114f, -90f, 0f),
            new Vector4(81f, -207f, 90f, 0f), new Vector4(162f, -176.8f, 0f, 0f),
        };

        /// <summary>Maintenance hatches: x, z, yaw.</summary>
        public static readonly Vector3[] Hatches = { new Vector3(120.5f, -158f, 0f), new Vector3(133.5f, -226.5f, 0f) };

        /// <summary>Car wrecks: x, z, half width, half length. W* = view only, in the harbour channels to the sea (off the tile map);
        /// HW* = in the open harbour water, bound to their sim object (ZWorld.Wrecks, DV39) whose tiles robots path round.</summary>
        public static readonly Vector4[] Wrecks =
        {
            new Vector4(-3f, -222f, 0.94f, 1.7f), new Vector4(223.25f, -199.5f, 1.15f, 2.13f),
            new Vector4(30f, -187f, 1f, 1.93f), new Vector4(187f, -182f, 1.15f, 2.13f),
            new Vector4(26f, -233f, 0.94f, 1.7f),
        };
        /// <summary>yaw (deg, model front +Z), pitch (nose down +), roll.</summary>
        public static readonly Vector3[] WreckAngles = { new Vector3(280f, -6f, 11f), new Vector3(96f, 12f, 6f), new Vector3(94f, 8f, -6f), new Vector3(182f, -7f, 5f), new Vector3(268f, 9f, 7f) };
        public static readonly string[] WreckModels = { "B", "C", "A", "C", "B" };
        public static readonly string[] WreckIds = { "W2", "W3", "HW1", "HW2", "HW3" };
        /// <summary>Top-left Zod tile of the sim wreck (ZWrecksData.Tiles; its LocX / LocY = tile x 16), (-1, -1) = view only.</summary>
        public static readonly Vector2Int[] WreckSimTile = { new Vector2Int(-1, -1), new Vector2Int(-1, -1), new Vector2Int(14, 93), new Vector2Int(93, 90), new Vector2Int(12, 116) };
    }
}
