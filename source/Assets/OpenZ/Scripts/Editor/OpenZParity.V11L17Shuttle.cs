#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// DV118 and K256f's empty shuttle; the box: UI/OzShuttleStart.cs). Measured headless: the counted check of Editor/OzL17ShuttleCount.cs
    /// with a shorter CPU soak (the starts on every difficulty, the room round both ships, the player boarding the empty one, CPU matches,
    /// levels 2 and 16 unchanged, the switches, determinism).</summary>
    public static partial class OpenZParity
    {
        static void AddL17ShuttleDeviation(List<Deviation> list)
        {
            string measured; bool pass;
            bool was = ZWorld.ShuttlePlayableDeviation, wasOn = ZWorld.ShuttleOn, wasR = ZWorld.L17ShuttlesDeviation, wasS = ZWorld.L17StartShuttle;
            try
            {
                string check;
                using (OzV11Kit.GameSwitches()) check = OzL17ShuttleCount.Check(SettingsText, MapText("L17"), MapText("L16"), MapText("L02"), OzL17ShuttleCount.EditorKeepOut(), OzL17ShuttleCount.RowSoakSec, OzL17ShuttleCount.RowSeeds);
                measured = OzV11Kit.VerdictCell(check, out pass) + " (the whole report: menu OpenZ/V1.1/Shuttle: K444 Level 17 Shuttles Check)";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            finally { ZWorld.ShuttlePlayableDeviation = was; ZWorld.ShuttleOn = wasOn; ZWorld.L17ShuttlesDeviation = wasR; ZWorld.L17StartShuttle = wasS; }
            var cp = ZWorld.L17CarParkShuttleSpot; var fs = ZWorld.L17FortShuttleSpot;
            list.Add(new Deviation
            {
                id = "DV134", change = "Level 17 has shuttles of its own: an EMPTY one in the car park's red side on every difficulty (only the player can take it), and the SHUTTLE box's ship parked by the red fort on EASY / MEDIUM",
                zod = "no shuttle in Zod (DV118 is an OpenZ unit); before: level 17 had no shuttle of its own (only the testing panel dropped one), and the SHUTTLE box was offered on level 16 only",
                openz = $"at map load, right after DV66 re-parks the start vehicles (every other object, DV66 move and HEAD START place unchanged; the ships take the next free ids): the empty car-park shuttle on tile {cp[0]},{cp[1]} facing {cp[2]} deg " +
                        "(the free bay of the east car park's first row, nose south like the row; the null team, no pilot: K256f's rules: a human team's squad ORDERED into it takes it, the CPU never does: no shuttle is \"an empty vehicle anyone can enter\", " +
                        $"a CPU's ENTER is refused, an unordered ENTER is dropped; neutral, no unit fires at it by itself); on EASY / MEDIUM with the box on, the player's crewed shuttle on tile {fs[0]},{fs[1]} facing {fs[2]} deg (the quiet pocket east of the red fort, " +
                        "its ramp toward the gate side; the level's playable shuttle). Each spot is checked with the landing spot rule on its exact heading; a changed map would use the nearest free tile within 3 (counted). Deterministic: fixed places, no random draw",
                measured = measured,
                toggle = "ZWorld.L17ShuttlesDeviation (code switch; ZGame: on, off in 'Zod exact'; headless default off) and ZWorld.L17StartShuttle (the battlefield screen's SHUTTLE box: level 17, EASY / MEDIUM, oz_start_shuttle, default on); needs DV118",
                status = pass ? DevActive : DevCheck,
            });
        }
    }
}
#endif
