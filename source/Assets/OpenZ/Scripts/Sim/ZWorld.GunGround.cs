namespace OpenZ.Sim
{
    /// <summary>
    /// Gun ground rule (deviation DV80). The Zod placement rule (ZCore::CannonPlacable, zcore.cpp:637-676) tests the zone and the
    /// objects in the way and ends on an empty "is it all over good terrain?" step, so a stored gun could be put into a river or
    /// on top of impassable ground. OpenZ closes that step: the four tiles of the 2x2 spot must be plain ground or road. A fort
    /// tower spot is exempt (the gun stands on the fort). The check is one line at the end of ZWorld.CannonPlacable, so every
    /// caller (the player's click, the placement overlay, tests) gets the same answer.
    /// Also here: the reason why a spot is refused, for the player's notice. The yes or no always comes from CannonPlacable itself.
    /// </summary>
    public sealed partial class ZWorld
    {
        /// <summary>true (default): no gun on water or on impassable ground. false: the Zod rule (no terrain check).</summary>
        public static bool GunGroundRule = true;

        /// <summary>Why a spot takes no gun. Ok = CannonPlacable says yes.</summary>
        public enum GunSpot { Ok, NoZone, OutsideZone, Water, RoughGround, Blocked }

        /// <summary>The 2x2 spot stands on plain ground or road only (always true with the rule off, and on a fort tower spot).</summary>
        public bool GunGroundOk(int tx, int ty)
        {
            if (!GunGroundRule) return true;
            bool ground = true;
            for (int j = 0; j < 2; j++)
                for (int i = 0; i < 2; i++)
                {
                    var c = Map.ClassAt(tx + i, ty + j);
                    if (c != ZClass.Ground && c != ZClass.Road) ground = false;
                }
            return ground || AreaIsFortTurret(tx, ty);                      // a fort tower spot: the gun stands on the fort, not on the ground
        }

        /// <summary>The spot is one of a fort's four tower spots (bfort.cpp:534-548), as the placement rule sees it.</summary>
        public bool GunSpotOnFortPad(int tx, int ty) => AreaIsFortTurret(tx, ty);

        /// <summary>CannonPlacable's answer with the reason for a refusal, in the rule's own order: the zone first, then things in
        /// the way, then the ground. (A bridge stands on water and impassable tiles: pointing at its deck must say "blocked", not
        /// "water" or "rough ground"; the same for a rock or a building at the water's edge.)</summary>
        public GunSpot GunSpotState(ZObject b, int tx, int ty)
        {
            if (CannonPlacable(b, tx, ty)) return GunSpot.Ok;
            var z = ZoneOf(b);
            if (z == null) return GunSpot.NoZone;
            int x = tx * 16, y = ty * 16;
            if (tx < 0 || ty < 0 || tx > Map.width || ty > Map.height) return GunSpot.OutsideZone;
            if (x < z.X + 16 || y < z.Y + 16 || x + 32 > z.X + z.W - 16 || y + 32 > z.Y + z.H - 16) return GunSpot.OutsideZone;
            foreach (var o in Objects)
            {
                if (o.Kind == ZKind.Vehicle || o.Kind == ZKind.Robot) continue;
                if (CannonNotPlacable(o, x, x + 32, y, y + 32)) return GunSpot.Blocked;
            }
            if (!GunGroundOk(tx, ty))
            {
                for (int j = 0; j < 2; j++)
                    for (int i = 0; i < 2; i++)
                        if (Map.ClassAt(tx + i, ty + j) == ZClass.Impassable) return GunSpot.RoughGround;
                return GunSpot.Water;
            }
            return GunSpot.Blocked;
        }
    }
}
