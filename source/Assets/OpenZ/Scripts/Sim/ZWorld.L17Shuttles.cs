using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// OpenZ deviation DV134 (level 17 "Car Park", on top of DV118's playable shuttle and K256f's EMPTY shuttle): two shuttles of the
    /// level's own, made at map load, deterministic (fixed places from this file's data, no random draw, no clock):
    ///  * THE CAR-PARK SHUTTLE, every difficulty: one EMPTY shuttle (no pilot, nobody aboard, the null team: the rules of
    ///    Sim/ZWorld.ShuttleEmpty.cs) in the free bay of the east car park's first row, the red side, beside the row's light tank, nose
    ///    south like the row's vehicles (<see cref="L17CarParkShuttleSpot"/>). Only the player takes it: a human team's squad ORDERED into it
    ///    (its leader becomes the pilot, the ship turns to the squad's team). The CPU never does: every shuttle is left out of "an empty
    ///    vehicle anyone can enter" (ZObject.CanBeEntered), so the CPU's target lists (ZBot.CollectOurTargets_3 / MatchTargets_3), its car
    ///    park drive (DV37, ZBot.CarParkRound), the Electrobot's pick and every automatic grab (DV38 / DV55 / DV84) skip it; an ENTER that
    ///    nobody ordered is dropped (ZObject.ShuttleEmptyAutoDrop); a CPU team's order on it is refused (ZObject.ShuttleEmptyTakes: a human
    ///    team only). Neutral, it is no target of anyone's automatic fire either (ZObject.CheckPassiveEngageCore skips the null team, and
    ///    the CPU lists only enemy-owned vehicles); a blast meant for something else can still hurt it like any vehicle. Taken by the
    ///    player it is the player's shuttle (a target like any of the player's units).
    ///  * THE FORT SHUTTLE, the battlefield screen's SHUTTLE box (UI/OzShuttleStart.cs; EASY and MEDIUM, the box on, as on level 16): the
    ///    player's crewed shuttle (its pilot, <see cref="ZWorld.ShuttleTeam"/>, red) parked in the quiet pocket east of the red fort, between
    ///    the fort's north-east tower, the red robot factory's back and the east cliffs, its ramp to the north-west (toward the fort's gate
    ///    side) (<see cref="L17FortShuttleSpot"/>). It is <see cref="ZWorld.PlayableShuttle"/>. HARD or the box off: no fort shuttle.
    ///  * Both are made right after DV66 has re-parked the start vehicles (ZWorld.ClearExitLanes), so every object of the map, every DV66
    ///    move and the HEAD START places stay as they were; they take the next free ids. Each spot is checked with the landing spot rule on
    ///    its exact heading (ZWorld.ShuttlePadFreeAt); if the map ever changed under it, the nearest tile within
    ///    <see cref="L17SpotSearchTiles"/> tiles that takes the ship is used (counted, <see cref="L17ShuttlesMoved"/>), else none is made.
    ///  * The places were chosen by numbers (the kit's headless measure: every place the landing rule takes, against every vehicle, gun,
    ///    building, flag, item, drawn prop, exit lane, gate face, the paths the CPU's units use, the walk from the fort's gate): the car-park
    ///    bay keeps 7.8 m from the nearest vehicle, 7.7 m from the robot factory south of it, 3.0 m from the nearest drawn prop (a mooring
    ///    bollard) and its ramp lane 1.4 m from the nearest bollard; the fort pocket keeps 7.2 m from the nearest vehicle, 10.1 m from the
    ///    nearest building, no unit's path crosses it, a 25 m walk from the gate.
    /// Switches: <see cref="L17ShuttlesDeviation"/> (the game: on, off in 'Zod exact'; headless default off, so every older check keeps its
    /// numbers) and <see cref="L17StartShuttle"/> (the box's answer at the level start). Needs DV118 on (ShuttlePlayableDeviation) and
    /// the shuttle rule on (ShuttleOn: always on level 17). Levels 2 and 16 never see any of it (the map file is tested).
    /// </summary>
    public sealed partial class ZWorld
    {
        /// <summary>DV134 switch (off = level 17 as before: no shuttle of its own).</summary>
        public static bool L17ShuttlesDeviation = false;
        /// <summary>DV134: the SHUTTLE box's answer for this level start (level 17, EASY / MEDIUM, the box on): the fort shuttle is made.</summary>
        public static bool L17StartShuttle = false;
        /// <summary>The fort shuttle's place: tile x, tile y (its middle on that tile's middle), heading (deg; 0 = east, 90 = south).</summary>
        public static readonly int[] L17FortShuttleSpot = { 100, 35, 45 };
        /// <summary>The car-park shuttle's place: tile x, tile y, heading (nose south, the ramp toward the car park's north entry).</summary>
        public static readonly int[] L17CarParkShuttleSpot = { 82, 92, 90 };
        /// <summary>How far (tiles) a spot is looked for when its own place does not take the ship (the map changed).</summary>
        public const int L17SpotSearchTiles = 3;

        /// <summary>The fort shuttle made at map load (null: another level, HARD, the box off, the switch off).</summary>
        public ZObject L17FortShuttle { get; private set; }
        /// <summary>The empty car-park shuttle made at map load (null: another level or the switch off).</summary>
        public ZObject L17CarParkShuttle { get; private set; }
        /// <summary>Shuttles this rule made at map load, and those that could not stand on their own place (0 on the map as it is).</summary>
        public int L17ShuttlesMade { get; private set; }
        public int L17ShuttlesMoved { get; private set; }
        /// <summary>Why a spot made nothing ("" = both as wanted).</summary>
        public string L17ShuttleWhy { get; private set; } = "";

        /// <summary>The view (OzPlayableShuttle.Spawn): this level's shuttles are drawn from the unit prefab (there is no scene ship).</summary>
        public static bool L17DrawnFromPrefab(ZWorld w) => w != null && w.L17ShuttlesMade > 0;

        /// <summary>ZWorld.Create hook, right after ClearExitLanes (DV66): the car-park shuttle, then the fort shuttle when the box asks.</summary>
        void AddL17Shuttles()
        {
            L17FortShuttle = L17CarParkShuttle = null; L17ShuttlesMade = L17ShuttlesMoved = 0; L17ShuttleWhy = "";
            if (!L17ShuttlesDeviation || !ShuttlePlayableDeviation || !ShuttleOn || !CarParkMap) return;
            var keep = PlayableShuttle;
            L17CarParkShuttle = L17Place(L17CarParkShuttleSpot, Team.Neutral, "car park");
            if (keep == null && L17CarParkShuttle != null && PlayableShuttle == L17CarParkShuttle) PlayableShuttle = null;   // the empty one is never the level's own playable ship
            if (!L17StartShuttle) return;
            if (ShuttleTeam != Team.Red) { L17ShuttleWhy += "fort: the player is not red (the spot is the red fort's); "; return; }
            L17FortShuttle = L17Place(L17FortShuttleSpot, ShuttleTeam, "fort");
            if (L17FortShuttle != null) PlayableShuttle = L17FortShuttle;
        }

        /// <summary>One shuttle on its spot (tile x, y, heading), the landing spot rule on that exact heading; else the nearest tile ring by
        /// ring within L17SpotSearchTiles (the heading turned to a free side, ZWorld.ShuttleLandSpot), else none (why kept).</summary>
        ZObject L17Place(int[] spot, Team team, string what)
        {
            int tx0 = spot[0], ty0 = spot[1]; double hd = spot[2];
            int cx = tx0 * 16 + 8, cy = ty0 * 16 + 8;
            if (!ShuttlePadFreeAt(cx, cy, null, hd, out string why))
            {
                bool found = false;
                for (int r = 1; r <= L17SpotSearchTiles && !found; r++)
                    for (int dy = -r; dy <= r && !found; dy++)
                        for (int dx = -r; dx <= r && !found; dx++)
                        {
                            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                            int x = (tx0 + dx) * 16 + 8, y = (ty0 + dy) * 16 + 8;
                            if (Map == null || tx0 + dx < ShuttleEdgeTiles || ty0 + dy < ShuttleEdgeTiles || tx0 + dx >= Map.width - ShuttleEdgeTiles || ty0 + dy >= Map.height - ShuttleEdgeTiles) continue;
                            if (!ShuttleLandSpot(x, y, null, spot[2], out double h2, out _)) continue;
                            cx = x; cy = y; hd = h2; found = true;
                        }
                if (!found) { L17ShuttleWhy += what + ": " + why + "; "; return null; }
                L17ShuttlesMoved++;
                L17ShuttleWhy += what + ": its spot took no ship (" + why + "), the nearest free tile is used; ";
            }
            var s = CreatePlayableShuttle(cx, cy, team, (float)ZObject.ShuttleNormDeg(hd));
            L17ShuttlesMade++;
            return s;
        }
    }
}
