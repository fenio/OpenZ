#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV118 "the level 16 shuttle is the player's transport" (the rule: Sim/ZWorld.Shuttle.cs). Measured
    /// headless: the counted check of Editor/OzV11ShuttleCount.cs (map load with the switch off / on and the option off, boarding,
    /// flight and landing, hovering, flags, the map edge, shot down in the air and on the ground, every weapon, the CPU).</summary>
    public static partial class OpenZParity
    {
        static void AddPlayableShuttleDeviation(List<Deviation> list)
        {
            string measured; bool pass;
            bool was = ZWorld.ShuttlePlayableDeviation, wasOn = ZWorld.ShuttleOn;
            try
            {
                string check;
                using (OzV11Kit.GameSwitches()) check = OzV11ShuttleCount.Check(SettingsText, MapText("L16"), MapText("L02"));
                using (OzV11Kit.GameSwitches()) check += OzShuttleEmptyCount.Check(SettingsText, MapText("L16"), MapText("L02"), MapText("L17"), OzShuttleEmptyCount.RowSoakSec);   // K256f: the empty shuttle and EXIT in the air (Editor/OzShuttleEmptyCount.cs)
                using (OzV11Kit.GameSwitches()) check += OzShuttleHeroesCount.Check(SettingsText, MapText("L16"), MapText("L02"));   // K256f2: the heroes get out, no vehicle boards a shuttle (Editor/OzShuttleHeroesCount.cs)
                using (OzV11Kit.GameSwitches()) check += OzShuttleTouchesCount.Check(SettingsText, MapText("L16"), MapText("L02"));   // K256g: the shuttle's orders only to places where it can land (Editor/OzShuttleTouchesCount.cs)
                using (OzV11Kit.GameSwitches()) check += OzShuttleTrafficCount.Check(SettingsText, MapText("L16"), OzShuttleTrafficCount.RowSeeds);
                using (OzV11Kit.GameSwitches()) check += OzShuttleQuickBoardCount.Check(SettingsText, MapText("L16"));   // K422: the boarding / unloading phase by phase, quicker than run 28 (Editor/OzShuttleQuickBoardCount.cs)   // K338: several shuttles in the air: apart, giving way smoothly, never on one landing spot (Editor/OzShuttleTrafficCount.cs)
                measured = OzV11Kit.VerdictCell(check, out pass) + " (the whole report: menu OpenZ/V1.1/Shuttle: DV118 Check)";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            finally { ZWorld.ShuttlePlayableDeviation = was; ZWorld.ShuttleOn = wasOn; }
            list.Add(new Deviation
            {
                id = "DV118", change = "Level 16: the shuttle beside the red fort is the player's transport for one squad (a new unit), with the option SHUTTLE on / off",
                zod = "no such unit: Zod's level 16 has a hut there (DV95 put a parked ship in its place, a destructible decoration)",
                openz = $"a red vehicle in the ship's place, {ShuttleHpWords}, one pilot who never gets out, no weapon, never snipable; one squad boards it by the APC's passenger rules (DV15), walking round to its back and up its rear ramp (a rule state: the ramp swings down and up in {ZWorld.ShuttleRampOpenSec:0.0#} s, a take-off waits for it to shut; K422: it starts up once the last robot is through the doorway, and an EXIT lets the squad get up while it comes down), " +
                        $"only while it stands on the ground; a move order: {ZWorld.ShuttleClimbSec:0} s climb to {ZWorld.ShuttleCruiseM:0} m, a flight that turns (at most {ZWorld.ShuttleTurnMaxDeg:0} deg/s) and speeds up and slows down smoothly ({ZWorld.ShuttleAccelSec:0.0} s) at the jeep's speed x {ZWorld.ShuttleJeepFactor:0.0}, " +
                        $"{ZWorld.ShuttleDescendSec:0} s landing on a free {ZWorld.ShuttlePadTiles * 2} x {ZWorld.ShuttlePadTiles * 2} m pad (ground or road, no building, rock, hut, tree, water, bridge, flag, crate or unit), " +
                        "else it hovers there (EXIT refused) until the next move; every target clamped to the map; never takes a flag; every weapon can hit it up there, sight on the ground never hides it, " +
                        "a blast aimed at it up there hurts nobody below and a blast on the ground never reaches it; destroyed, in the air or on the ground, its passengers are lost; the CPU never uses it; " +
                        "the option SHUTTLE off: no shuttle on level 16 at all; an EMPTY shuttle (no pilot, the null team: the testing panel's empty row, a map's 'vehicle:shuttle' entry) " +
                        "never flies and nobody walks into it by himself; a human squad ordered in boards up the ramp: its leader becomes the pilot (never gets out), the rest its passengers; " +
                        "a HERO pilot (the ALAN & BRAD squad took it) gets out on EXIT with everyone aboard and the ship is empty again; no vehicle, gun or shuttle ever boards a shuttle; " +
                        "EXIT and boarding offered only while it stands on the ground (from the touchdown, a crash landing's too): no EXIT cursor, button or order up in the air (K430b, the player's side; the rules' EXIT in the air where it may land is kept for the checks); " +
                        $"several ships in the air (own and enemy alike) keep their middles at least {ZWorld.ShuttleAirSepM:0} m apart, each giving way by a smooth step aside or a slower cruise (never a hard push, never a back-and-forth), " +
                        $"and never share a landing spot (a second ship takes the nearest free spot at least {ZWorld.ShuttleClaimM:0} m from the others, or holds over a spot until the ship leaving it has gone); a take-off waits while another ship is up within {ZWorld.ShuttleLiftAirM:0} m",
                measured = measured,
                toggle = "ZWorld.ShuttlePlayableDeviation (code switch; ZGame: on, off in Zod exact; headless default off, this probe turns it on) and the option SHUTTLE (oz_shuttle, ZWorld.ShuttleOn, default on); " +
                         "K256g: the option SHUTTLE ORDERS (oz_shuttle_orders, the player's input only: LANDABLE SPOTS ONLY = no order to a place where it can never land, the default; ANYWHERE = as before); " +
                         "K338: the air traffic of several ships, ZWorld.ShuttleTrafficOn (code switch, on; with one shuttle in the match nothing of it runs); " +
                         "K422: the quicker boarding / unloading waits, ZWorld.ShuttleQuickRamp (code switch, on; off = the ramp up only after the last one sat, the squad out only with the ramp fully down, with the new times)",
                status = pass ? DevActive : DevCheck,
            });
        }
    }
}
#endif
