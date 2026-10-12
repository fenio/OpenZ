using System;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    public static partial class OpenZParity
    {
        // ------------------------------------------------------------------ DV80 gun ground: no stored gun on water or impassable ground
        /// <summary>DV80: the allowed 2x2 spots over all zones of both maps, counted with the rule and without it (headless,
        /// OzGunPlacementCheck.SpotCounts: the same GunCover the placement overlay draws).</summary>
        static Deviation DevGunGround(Func<string, int, int> getInt)
        {
            string measured = "not measured in this build"; bool ok = true;
#if !OPENZ_HARNESS
            var (l02On, l02Off, l17On, l17Off) = OzGunPlacementCheck.SpotCounts();
            measured = $"allowed 2x2 spots over all zones, with the rule / without it: L02 {l02On} / {l02Off}, L17 {l17On} / {l17Off}";
            ok = l02On < l02Off && l17On < l17Off;
#endif
            return new Deviation
            {
                id = "DV80", change = "Gun ground: a stored gun cannot be placed on water or on impassable ground",
                zod = "no terrain check: CannonPlacable tests the zone and the objects in the way and ends on an empty \"is it all over good terrain?\" step (zcore.cpp:637-676), so a gun can be placed in a river or on impassable ground",
                openz = "the four tiles of the 2x2 spot must be plain ground or road; a fort tower spot is exempt; one rule for the player's click, the marked area and the tests (ZWorld.GunGroundOk, the last step of ZWorld.CannonPlacable)",
                measured = measured,
                toggle = $"ZWorld.GunGroundRule = {(ZWorld.GunGroundRule ? "true (OpenZ, default)" : "false (Zod: no terrain check)")} (code)",
                status = !ZWorld.GunGroundRule ? DevNotInBuild + " (switched off)" : ok ? DevActive : DevCheck,
            };
        }
    }
}
