#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// DV118's landing spot rule). Measured headless: the counted check of Editor/OzShuttleFairSpotCount.cs in its row size (every sixth tile each
    /// way, 3 flights): level 16's gatling island (tile 29,68 taken, the gun, its flag and the water refused, a flight with a squad that lands,
    /// gets out and takes the flag), the rule's promises on levels 16, 2 and 17, before and after, the click agreeing with the flight, determinism.</summary>
    public static partial class OpenZParity
    {
        static void AddShuttleFairSpotDeviation(List<Deviation> list)
        {
            string measured; bool pass;
            bool was = ZWorld.ShuttlePlayableDeviation, wasOn = ZWorld.ShuttleOn, wasF = ZWorld.ShuttleFairSpotOn;
            try
            {
                string check;
                using (OzV11Kit.GameSwitches()) check = OzShuttleFairSpotCount.Check(SettingsText, MapText("L16"), MapText("L02"), MapText("L17"), null, true);
                measured = OzV11Kit.VerdictCell(check, out pass) + " (the whole report: menu OpenZ/V1.1/Shuttle: K437 Fair Landing Spot Check)";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            finally { ZWorld.ShuttlePlayableDeviation = was; ZWorld.ShuttleOn = wasOn; ZWorld.ShuttleFairSpotOn = wasF; }
            list.Add(new Deviation
            {
                id = "DV128", change = "The shuttle lands where it really fits: dry ground under its landing gear, the pods may reach over water, a flag counts by its pole and cloth, a gun by its ring, always",
                zod = "no shuttle in Zod (DV118 is an OpenZ unit); DV118's landing spot wanted its whole outline + 0.5 m on dry ground, the whole flag tile clear and a dry lane 10.6 m behind its middle, and its click test left the guns out",
                openz = $"the landing gear (the four pads and everything between them, measured from the model; every part lower than {ZWorld.ShuttleLowM:0.0} m lies inside it) + {ZWorld.ShuttleGearClearM:0.00} m on dry ground (exact per tile); " +
                        $"the whole outline + {ZWorld.ShuttleSpotClearM:0.0} m off rocks, closed tiles, the map's edge, buildings, huts, crates, bridges, props, plant crowns of {ZWorld.ShuttleLowM:0.0} m and taller (lower ones only under the gear) and units, as before; " +
                        $"a flag by its pole (+{ZWorld.ShuttleSpotClearM:0.0} m) and its {ZWorld.ShuttleFlagClothM:0.00} m cloth clear of the ship's upper body; a gun by its {ZPathing.CannonRadius / 8.0:0.0} m ring (+{ZWorld.ShuttleSpotClearM:0.0} m), always (also in the click's test), never under the ramp; " +
                        $"the ramp ({ZWorld.ShuttleRampHalfW * 2:0.0} m wide, to the first robot's spot at its foot) on dry ground with no flag pole or gun, the squad's line behind it on ground a robot can stand on (shallow water counts); " +
                        $"the landing tries its 15-degree lattice from its arrival heading, then every multiple of {ZWorld.ShuttleFineStepDeg} degrees (the click's lattice), so a place the click took is a place it lands on. Level 16's gatling island: tile 29,68 (before: no island tile). Deterministic: the map, the objects and the ship's sim state only",
                measured = measured,
                toggle = "ZWorld.ShuttleFairSpotOn (code switch, on; off = DV118's landing spot as it was); matters only while DV118 makes shuttles (off in 'Zod exact')",
                status = pass ? DevActive : DevCheck,
            });
        }
    }
}
#endif
