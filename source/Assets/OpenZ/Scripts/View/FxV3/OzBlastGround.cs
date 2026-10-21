// OpenZ (GPL-3.0) - version 1.1 items 214, 215: the ground under the fort blast's wave and under its thrown trees, out to the end of the
// level's ground.
// Inside the map: the effects' cached 0.5 m ground with the water's surface (OpenZFxPlayer.SurfaceY), as the ring used before.
// Outside the map: that cache stops 2 m past the map's edge and answers its edge height further out, so the ring lay INSIDE the map
// sides there (Level 16: the rock walls and the jungle rims). Where the level's ground collider (COL_Terrain) reaches past the map
// (Level 16: its 24 m apron, which carries the rims' shape), the ground out there is read from that collider (one ray a point); past
// its end there is no ground and the answer says so. A level whose collider ends at the map keeps the old answer out there.
// Drawn only: no rule reads it.
using UnityEngine;

namespace OpenZ
{
    public static class OzBlastGround
    {
        static int scene = -1; static Collider col; static bool apron; static Rect map, world;
        /// <summary>For the checks: rays cast past the map.</summary>
        public static int Rays;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { scene = -1; col = null; apron = false; Rays = 0; }

        static void Bind()
        {
            int sc = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            if (scene == sc && (!apron || col)) return;
            scene = sc;
            var go = GameObject.Find("COL_Terrain"); col = go ? go.GetComponent<Collider>() : null;
            float w = 160f, d = 200f; var g = ZGame.I;
            if (g != null && g.World != null && g.World.Map != null) { w = g.World.Map.WidthPix / OpenZFxCatalog.PxPerMetre; d = g.World.Map.HeightPix / OpenZFxCatalog.PxPerMetre; }
            map = Rect.MinMaxRect(0f, -d, w, 0f); world = map; apron = false;
            if (col)
            {
                var b = col.bounds;
                apron = b.min.x < -4f || b.max.x > w + 4f || b.min.z < -d - 4f || b.max.z > 4f;
                if (apron) world = Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
            }
        }

        /// <summary>The ground (the water's surface included) at x, z; false = past the end of the level's ground.</summary>
        public static bool Y(float x, float z, out float y)
        {
            Bind();
            if (!apron || map.Contains(new Vector2(x, z)))
            {
                var fx = OpenZFxPlayer.I;
                y = fx ? fx.SurfaceY(new Vector3(x, 0f, z)) : ZGame.I ? ZGame.I.GroundY(x, z) : 0f;
                return true;
            }
            Rays++;
            if (col && col.Raycast(new Ray(new Vector3(x, 200f, z), Vector3.down), out var h, 400f)) { y = h.point.y; return true; }
            y = 0f; return false;
        }

        /// <summary>The level's ground as a box seen from above (x, z): the collider's where it reaches past the map, else the map.</summary>
        public static Rect World { get { Bind(); return world; } }

        /// <summary>Metres from p along the flat direction dir to the end of the level's ground.</summary>
        public static float Room(Vector3 p, Vector3 dir)
        {
            var r = World; float t = 1e6f;
            if (dir.x > 1e-4f) t = Mathf.Min(t, (r.xMax - p.x) / dir.x); else if (dir.x < -1e-4f) t = Mathf.Min(t, (r.xMin - p.x) / dir.x);
            if (dir.z > 1e-4f) t = Mathf.Min(t, (r.yMax - p.z) / dir.z); else if (dir.z < -1e-4f) t = Mathf.Min(t, (r.yMin - p.z) / dir.z);
            return Mathf.Max(0f, t);
        }
    }
}
