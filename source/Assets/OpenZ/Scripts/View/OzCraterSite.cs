using OpenZ.Sim;

namespace OpenZ
{
    /// <summary>
    /// Deformable ground (<see cref="CraterField"/>): the parts of a crater's site test that read only the rules' own data, the same
    /// on every level (no level names, no map kinds). Free of the engine, so the headless check runs this very file.
    /// <list type="bullet">
    /// <item>ROADS: a crater on a road is a paved crater (broken paving, chips), on sand, dirt or grass an earth crater, and one
    /// crater never lies across both (<see cref="Paved"/>, <see cref="Mixed"/>). Before, only the city map had craters on its roads;
    /// every other map refused any big blast within a crater's width of a road.</item>
    /// <item>THINGS ON THE GROUND (<see cref="Blocker"/>): no crater under or beside a building, a gun, a wreck or a big map object:
    /// its bowl stays <see cref="BigClearM"/> from their footprint, as before. A ONE-TILE map item (a rock, a tree that closes its
    /// tile, a flag, a crate, a small prop) keeps the bowl only off its own tile with <see cref="ItemClearM"/> to spare. Before, the
    /// big clearance held for every one of them too, so a map dotted with rocks and trees (the Jungle: 214 rock tiles and 243 tree
    /// tiles among its fighting places) had almost no open place left for a crater.</item>
    /// <item>A DESTROYED ROCK is gone (its chunks fly and fade, no heap is left: RockPillar), so it no longer keeps a crater away:
    /// the blast that breaks a rock may leave a crater where it stood. A felled tree keeps its stump, a ruin its rubble, a wreck
    /// its hull: they still do.</item>
    /// </list>
    /// </summary>
    public static class OzCraterSite
    {
        /// <summary>Clearance (metres) between a crater's edge and the footprint of a building, gun, wreck or big map object.</summary>
        public const float BigClearM = 1.5f;
        /// <summary>Clearance (metres) between a crater's edge and the tile of a one-tile map item.</summary>
        public const float ItemClearM = 0.5f;
        /// <summary>Road weight (0..1, bilinear between tile centres) from which the ground counts as paving.</summary>
        public const float PavedFrom = 0.5f;

        /// <summary>Road weight at world (x, z), 0..1, bilinear between tile centres from the map's own tile classes (the same
        /// weights as TrackSurface.Classes: tile tx covers x 2tx..2tx+2, ty covers z -2ty-2..-2ty).</summary>
        public static float RoadWeight(ZMapData map, float x, float z)
        {
            if (map == null) return 0f;
            float fx = x * 0.5f - 0.5f, fy = -z * 0.5f - 0.5f;
            int tx = (int)System.Math.Floor(fx), ty = (int)System.Math.Floor(fy);
            float ax = fx - tx, ay = fy - ty, road = 0f;
            for (int k = 0; k < 4; k++)
            {
                int dx = k & 1, dy = k >> 1;
                if (map.ClassAt(tx + dx, ty + dy) == ZClass.Road) road += (dx == 0 ? 1f - ax : ax) * (dy == 0 ? 1f - ay : ay);
            }
            return road;
        }

        /// <summary>The crater at a blast is a paved one (road weight at its centre).</summary>
        public static bool Paved(float roadW) => roadW > PavedFrom;

        /// <summary>A point of the bowl whose ground kind differs from the crater's own (paving under an earth crater or the reverse).</summary>
        public static bool Mixed(float roadW, bool paved) => (roadW > PavedFrom) != paved;

        /// <summary>True for a map item that stands on one tile only (its clearance is <see cref="ItemClearM"/>).</summary>
        public static bool OneTileItem(ZObject o) =>
            o != null && o.Kind == ZKind.MapItem && o.WidthPix <= 16 && o.HeightPix <= 16 && o.Type != ZWorld.WreckType;

        /// <summary>Nothing of it is left on the ground: a destroyed rock.</summary>
        public static bool Gone(ZObject o) => o != null && o.Destroyed && o.Kind == ZKind.MapItem && o.Type == "rock";

        /// <summary>The first object a crater of radius <paramref name="radiusM"/> at world (x, z) would lie under or beside;
        /// null = clear. Buildings, guns, map items and wrecks only (a unit drives on, a crater is no obstacle).</summary>
        public static ZObject Blocker(ZWorld w, float x, float z, float radiusM)
        {
            if (w == null) return null;
            float px = x * 8f, py = -z * 8f;                                                    // ZGame.WorldToPx: 8 px per metre, y = -z
            foreach (var o in w.Objects)
            {
                bool solid = o.Kind == ZKind.Building || o.Kind == ZKind.MapItem || o.Kind == ZKind.Cannon || (o.Kind == ZKind.Vehicle && o.Destroyed);
                if (!solid || Gone(o)) continue;
                float m = (radiusM + (OneTileItem(o) ? ItemClearM : BigClearM)) * 8f;
                if (px > o.LocX - m && px < o.LocX + o.WidthPix + m && py > o.LocY - m && py < o.LocY + o.HeightPix + m) return o;
            }
            return null;
        }
    }
}
