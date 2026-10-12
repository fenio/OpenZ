using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// The decoded original Zod map (Resources/OpenZ/Maps/Lxx_grid.json, written from p02_bb_origNN.map): tile classes,
    /// palette tiles, zones and the object list with Zod's own tile coordinates (object loc = tile * 16, zserver.cpp:831-833).
    /// </summary>
    [Serializable]
    public sealed class ZMapData
    {
        [Serializable] public sealed class Zone { public int x, y, w, h; }
        [Serializable]
        public sealed class Obj
        {
            public int x, y; public string owner, kind; public int ot, oid, blevel, extra_links, health_percent;
            public string Category => kind.Split(':')[0];
            public string Type => kind.Split(':')[1];
        }

        public string file, planet;
        public int width, height;
        public int[] classes, tiles;
        public List<Zone> zones;
        public List<Obj> objects;

        public int WidthPix => width * ZConst.TILE;
        public int HeightPix => height * ZConst.TILE;

        public static ZMapData FromJson(string json) => UnityEngine.JsonUtility.FromJson<ZMapData>(json);

        public ZClass ClassAt(int tx, int ty) =>
            tx < 0 || ty < 0 || tx >= width || ty >= height ? ZClass.Impassable : (ZClass)classes[ty * width + tx];

        /// <summary>zmap.cpp:598-615 GetTileWalkSpeed at a pixel: impassable 0, road 1.689, water 0.7, else 1.</summary>
        public double TileWalkSpeed(int px, int py)
        {
            if (px < 0 || py < 0) return 0;
            int tx = px >> 4, ty = py >> 4;
            if (tx >= width || ty >= height) return 0;
            switch (ClassAt(tx, ty))
            {
                case ZClass.Impassable: case ZClass.DeepWater: return 0;
                case ZClass.Road: return ZConst.ROAD_SPEED;
                case ZClass.Water: return ZConst.WATER_SPEED;
                default: return 1.0;
            }
        }
    }
}
