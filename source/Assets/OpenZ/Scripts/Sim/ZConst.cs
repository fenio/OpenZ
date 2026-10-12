namespace OpenZ.Sim
{
    /// <summary>Zod Engine constants (constants.h). Distances in Zod pixels: 1 tile = 16 px = 2 m in OpenZ.</summary>
    public static class ZConst
    {
        public const int TILE = 16;                   // zmap.cpp:2004-2034, zobject.cpp:2026 (>>4)
        public const int MAX_UNIT_HEALTH = 10000;     // constants.h:42
        public const double ROAD_SPEED = 1.689;       // constants.h:23
        public const double WATER_SPEED = 0.7;        // constants.h:24
        public const double TICK = 0.01;              // zserver.cpp:970 uni_pause(10): ~100 Hz server loop
        public const double SCUFFLE_INTERVAL = 1.0;   // zserver.cpp:983
        public const float PX_TO_M = 2f / 16f;        // OpenZ world: x = px / 8, z = -py / 8
    }

    /// <summary>Zod map classes as written by the OpenZ map decoder (legend in Maps/Lxx_grid.json).</summary>
    public enum ZClass { Ground = 0, Road = 1, Water = 2, Impassable = 3, DeepWater = 4 }

    public enum ZKind { Robot, Vehicle, Cannon, Building, MapItem }

    /// <summary>Waypoint modes, same order as zobject.h:77-82. None = Zod's cleared waypoint (mode -1).</summary>
    public enum WpMode { None = -1, Move = 0, Enter, Attack, ForceMove, CraneRepair, UnitRepair, Agro, EnterFort, Dodge, PickupGrenades }

    /// <summary>Zod waypoint record (zobject.h:157-195) with its field-by-field equality.</summary>
    public struct ZWaypoint : System.IEquatable<ZWaypoint>
    {
        public WpMode mode;
        public int refId;
        public int x, y;
        public bool attackTo, playerGiven;
        /// <summary></summary>
        public int leash;

        public static readonly ZWaypoint Cleared = new ZWaypoint { mode = WpMode.None, refId = -1 };

        public static ZWaypoint Move(int x, int y, bool attackTo = false, int refId = -1) =>
            new ZWaypoint { mode = WpMode.Move, refId = refId, x = x, y = y, attackTo = attackTo };

        public bool Equals(ZWaypoint o) =>
            o.mode == mode && o.refId == refId && o.x == x && o.y == y && o.attackTo == attackTo && o.playerGiven == playerGiven && o.leash == leash;
        public override bool Equals(object obj) => obj is ZWaypoint w && Equals(w);
        public override int GetHashCode() => (int)mode ^ (refId << 4) ^ (x << 8) ^ (y << 20);
    }

    /// <summary>C rand() stand-in: seeded, and Roll() = (rand() % 10000) / 10000.0 like zobject.cpp:2187.</summary>
    public sealed class ZRng
    {
        readonly System.Random r;
        public ZRng(int seed) { r = new System.Random(seed); }
        public int Next() => r.Next(0, int.MaxValue);          // rand()
        public int Next(int mod) => mod <= 0 ? 0 : Next() % mod; // rand() % mod
        public double Roll() => (Next() % 10000) / 10000.0;
    }
}
