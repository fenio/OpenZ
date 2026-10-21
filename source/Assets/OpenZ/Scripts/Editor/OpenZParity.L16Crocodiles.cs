#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations rows DV91 to DV94 of level 16's crocodiles (the rule: Sim/ZWorld.Crocodiles.cs). Written from the live
    /// constants and measured by the rule's own counted check (OzL16CrocCount, the menu OpenZ/L16 Jungle/Checks/Count Crocodile Rule,
    /// here without the whole 30-minute match), run once for the four rows with the game's switches; the switches are put back
    /// afterwards, so every other row runs without the rule.</summary>
    public static partial class OpenZParity
    {
        static void AddCrocodileDeviations(List<Deviation> list, Func<string, int, int> getInt)
        {
            string[] lines; string err = null;
            try
            {
                string report;
                using (OzV11Kit.GameSwitches()) report = OzL16CrocCount.Run(SettingsText, MapText, OzL16CrocCount.SquadCrossings, false);
                lines = report.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("PASS  ") || l.StartsWith("FAIL  ")).ToArray();
            }
            catch (Exception ex) { lines = new string[0]; err = "the check threw " + ex.GetType().Name + ": " + ex.Message; }
            // the check's verdict lines, by what they are about
            string Cell(out bool pass, params string[] about)
            {
                var mine = lines.Where(l => about.Any(a => l.Contains(a))).ToList();
                pass = err == null && mine.Count > 0 && mine.All(l => l.StartsWith("PASS"));
                return err ?? (mine.Count == 0 ? "no verdict line in the check's report" : string.Join("; ", mine.Select(l => l.Substring(0, 4) + ": " + l.Substring(6))));
            }
            bool game = getInt("oz_zod_exact", 0) == 0;
            string Toggle(string field, string pref) => $"ZWorld.{field}: the game turns it on at every start (PlayerPrefs {pref} = {getInt(pref, 1)}, default 1), off in 'Zod exact' (oz_zod_exact = {(game ? "0" : "1")}); headless default off, so every other row runs without it";
            string Status(bool pass, string pref) => !pass ? DevCheck : game && getInt(pref, 1) == 1 ? DevActive : DevActive + " (off by pref)";

            string m1 = Cell(out bool p1, "crocodiles of the table", "squad crossings lose one", "crossings lose two", "with the switch off level 16", "computer against computer");
            list.Add(new Deviation
            {
                id = "DV91", change = "Level 16: crocodiles in the swamp take wading robots",
                zod = "no crocodile in the engine (refs/zod_engine/src: the only hits are the missile cannon's 'crockets'; the 1996 game's bite sound CROCBITE.wav ships unused); robots cross water at 0.7 speed and nothing else happens to them (zmap.cpp GetTileWalkSpeed)",
                openz = $"{ZCrocData.Places.Length} crocodiles at fixed places of the level table, only ever on water that is theirs: their middle keeps {ZWorld.CrocTurnM} m of free water (the distance to the nearest tile that is not swamp, less {ZWorld.CrocWobbleM:0.#} m for the rounded pool outlines, and never more than the distance to the nearest place the level draws as dry). " +
                        $"A robot (never a vehicle, never on a bridge) that has waded {ZWorld.CrocWetSeconds:0.#} s without a break and stands in attack water (where the whole attack fits: {ZWorld.CrocBoxAheadM} m ahead, {ZWorld.CrocBoxBehindM} m behind and {ZWorld.CrocBoxSideM} m either side of the animal, wholly on water) can be picked: once every {ZWorld.CrocCheckEvery:0.#} s per pool, {ZWorld.CrocStartChance} chances in 100 (a hash of tick, pool and robot: no random call), one hunt per pool, {ZWorld.CrocMaxHunts} on the map. " +
                        $"The hunt: a warning of {ZWorld.CrocWarnSeconds:0.#} s or more from an animal {ZWorld.CrocStartMinM:0} to {ZWorld.CrocStartMaxM:0} m away (else one comes up at the pool's farthest place within {ZWorld.CrocStartMaxM:0} m; first sign to bite never under {ZWorld.CrocFirstSignSeconds} s for a robot that stands), the nose-only stalk at {ZWorld.CrocStalkMps} m/s for at most {ZWorld.CrocStalkLimit:0} s (over when no robot wades in attack water within {ZWorld.CrocSwitchM:0} m of it), " +
                        $"the rise ({ZWorld.CrocRiseSeconds} s) within {ZWorld.CrocRiseM} m and {ZWorld.CrocRiseDeg:0} degrees, the lunge: {ZWorld.CrocBiteSeconds} s in, the robot within reach of the jaws is taken (health 0, nobody gets the kill): one robot per attack. Then {ZWorld.CrocCoolKillMin:0} to {ZWorld.CrocCoolKillMax:0} s of peace in that pool ({ZWorld.CrocCoolMiss:0} s after a miss, {ZWorld.CrocCoolScared:0} s when a blast within {ZWorld.CrocScareM:0} m scared it off). It cannot be shot. Both teams and the computer player alike (Sim/ZWorld.Crocodiles.cs)",
                measured = "OpenZ/L16 Jungle/Checks/Count Crocodile Rule (short): " + m1,
                toggle = Toggle("CrocodileDeviation", "oz_crocodiles"),
                status = Status(p1, "oz_crocodiles"),
            });
            string m2 = Cell(out bool p2, "wades");
            list.Add(new Deviation
            {
                id = "DV92", change = "Level 16: a robot wading near a hunting crocodile hurries",
                zod = "a robot on a water tile moves at 0.7 of its speed, whatever happens round it (zmap.cpp GetTileWalkSpeed); running costs stamina (zobject.cpp ProcessRunStamina)",
                openz = $"a robot on a water tile within {ZWorld.CrocHurryM:0} m of a crocodile that warns, stalks, rises, lunges or rolls wades {ZWorld.CrocHurryFactor} times as fast while that lasts and {ZWorld.CrocHurryAfter:0} s longer; stamina untouched; a robot that runs anyway keeps the running speed (the larger of the two, never both); it ends the moment the robot stands on dry ground (Sim/ZObject.cs RunSpeed, Sim/ZWorld.Crocodiles.cs)",
                measured = "OpenZ/L16 Jungle/Checks/Count Crocodile Rule (short): " + m2,
                toggle = Toggle("CrocHurryDeviation", "oz_croc_hurry") + "; needs DV91",
                status = Status(p2, "oz_croc_hurry"),
            });
            string m3 = Cell(out bool p3, "idle robots two tiles", "hold position");
            list.Add(new Deviation
            {
                id = "DV93", change = "Level 16: a robot in the water with no order walks to the bank when a crocodile hunts near",
                zod = "an idle unit starts nothing because of a danger it cannot shoot; it stands where it was left (zobject.cpp:1603-1853 CheckPassiveEngage)",
                openz = $"a single robot or squad leader with no order, not on hold position, that wades (or whose squad still wades while it stands on the bank) gets a move of its own to the nearest dry tile within {ZWorld.CrocFleeTiles} tiles when a crocodile hunts within {ZWorld.CrocFleeM:0} m or has picked one of its squad: never a tile within {ZWorld.CrocFleeKeepM:0} m of the animal or nearer to the animal than to itself (else the plain nearest); looked for every 0.5 s. " +
                        $"Until the hunt in that pool is over and {ZWorld.CrocWaryAfter:0} s more ({ZWorld.CrocWaryMax:0} s at most) it starts nothing by itself (no chase, no flag, no vehicle, no crate), like a unit on hold position; it still shoots what is in range; any order ends it. A robot on an order keeps going; a robot on hold position stays (Sim/ZWorld.Crocodiles.cs CrocFleeStep, Sim/ZObject.cs CheckPassiveEngage)",
                measured = "OpenZ/L16 Jungle/Checks/Count Crocodile Rule (short): " + m3,
                toggle = Toggle("CrocFleeDeviation", "oz_croc_flee") + "; needs DV91",
                status = Status(p3, "oz_croc_flee"),
            });
            string m4 = Cell(out bool p4, "fixed routes", "swamp tiles on the straight line", "no spot found");
            list.Add(new Deviation
            {
                id = "DV94", change = "Level 16: the swamp is no short cut (water weighs double in the robots' path search; no wading round a bridge; no idle walk across the swamp)",
                zod = "a water tile weighs 1 / 0.7 of a ground tile in the path search on every map (zpath_finding.cpp:417-471); an idle robot walks to any flag, empty vehicle or crate within its grab distance (zobject.cpp:1603-1853)",
                openz = $"on a map with a crocodile table a swamp tile weighs {ZWorld.CrocWaterCost:0.#} times as much in the ROBOT grids of the path search (142 becomes 285; vehicle grids untouched, vehicles never enter water), and as much in the bridge layer's own search; robots do not wade round a bridge while a way over it exists; " +
                        $"an idle robot starts no walk of its own to a flag, an empty vehicle or a crate whose straight line from it crosses more than {ZWorld.CrocSwampTiles} swamp tiles. A straight walk with nothing in its way is still taken as it is (the engine's own rule, zpath_finding.cpp:903-1019), and an order into the water is obeyed (Sim/ZPathFinder.cs SetTileInfo, Sim/ZBridges.cs, Sim/ZObject.cs CheckPassiveEngage)",
                measured = "OpenZ/L16 Jungle/Checks/Count Crocodile Rule (short): " + m4,
                toggle = Toggle("CrocWaterCostDeviation", "oz_croc_water_cost"),
                status = Status(p4, "oz_croc_water_cost"),
            });
        }
    }
}
#endif
