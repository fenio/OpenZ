using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// OpenZ deviation DV73 "Hard: a missile cannon on each fort's door tower". Zod puts only the map's own cannons on a fort's 4 turret
    /// spots (fort-local tiles (1,0) (7,0) (1,3) (7,3), bfort.cpp:534-548): L02's forts start with one gun or none there, L17's with two
    /// or four howitzers. With DV73 every owned fort starts with one MISSILE CANNON (Zod's cannon, its settings unchanged) on a tower of
    /// its door row, the front of the fort where its units come out: fort_front leaves to the south (row 3), fort_back to the north
    /// (row 0). The door is in the middle of that row, so its two towers are equally close: the east one is used (tile 7, the right
    /// one in the game camera).
    /// A map cannon standing on the chosen spot is created as the missile cannon instead (same tile, owner, crew and health: one creation
    /// path, nothing is removed afterwards); a free spot gets a new cannon with Zod's start driver (zcannon.cpp:57-70). Like every cannon
    /// on a turret spot it is not ejectable (ZWorld.MarkFortTurretCannons).
    /// Switch: ZGame sets it at every start: on only on HARD in human vs CPU games, for both forts; Easy, Medium, bot matches, 'Zod
    /// exact' and the headless tests keep the map's cannons (static default off). Deterministic: map data only, no random draw.
    /// </summary>
    public sealed partial class ZWorld
    {
        /// <summary>DV73 switch (off = the map's own tower cannons).</summary>
        public static bool FortDoorGunDeviation = false;
        /// <summary>DV73: the cannon type on the door tower.</summary>
        public const string FortDoorGunType = "missile_cannon";
        /// <summary>DV73: the door-tower cannons of this world (ZGame gives each its view).</summary>
        public readonly List<ZObject> FortDoorGuns = new List<ZObject>();

        readonly List<(int tx, int ty, Team owner, bool taken)> fortDoorSpots = new List<(int tx, int ty, Team owner, bool taken)>();

        /// <summary>ZWorld.Create hook, before the map objects: the door-tower spot of every owned fort, from the map data.</summary>
        void PlanFortDoorGuns()
        {
            fortDoorSpots.Clear(); FortDoorGuns.Clear();
            if (!FortDoorGunDeviation || S.Unit(FortDoorGunType) == null) return;
            foreach (var f in Map.objects)
            {
                if (f.Category != "building" || (f.Type != "fort_front" && f.Type != "fort_back")) continue;
                var owner = ParseOwner(f.owner);
                if (owner == Team.Neutral) continue;
                int ty = f.y + (f.Type == "fort_front" ? 3 : 0);                              // the door row
                fortDoorSpots.Add((f.x + 7, ty, owner, MapCannonAt(f.x + 7, ty)));           // the east tower of the door row
            }
        }

        bool MapCannonAt(int tx, int ty)
        {
            foreach (var o in Map.objects) if (o.Category == "cannon" && o.x == tx && o.y == ty) return true;
            return false;
        }

        /// <summary>DV73: the type a map object is created as: the missile cannon for a map cannon on a planned door-tower spot.</summary>
        string FortDoorGunTypeAt(ZMapData.Obj o)
        {
            if (fortDoorSpots.Count == 0 || o.Category != "cannon") return o.Type;
            foreach (var s in fortDoorSpots) if (s.tx == o.x && s.ty == o.y) return FortDoorGunType;
            return o.Type;
        }

        /// <summary>ZWorld.Create hook, right after the map objects: a new missile cannon on every planned spot that was free, and the
        /// list of all door-tower cannons.</summary>
        void AddFortDoorGuns()
        {
            foreach (var s in fortDoorSpots)
            {
                ZObject c = null;
                if (s.taken) { foreach (var o in Objects) if (o.Kind == ZKind.Cannon && o.LocX == s.tx * 16 && o.LocY == s.ty * 16) c = o; }
                else c = CreateObject("cannon", FortDoorGunType, s.tx * 16, s.ty * 16, s.owner);
                if (c != null) FortDoorGuns.Add(c);
            }
        }
    }
}
