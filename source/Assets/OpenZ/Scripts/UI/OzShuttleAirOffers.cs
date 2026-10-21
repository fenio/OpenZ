using OpenZ.Sim;

namespace OpenZ
{
    /// <summary>
    /// K430b: the shuttle cannot unload or load up in the sky, so the player's side offers its EXIT and its boarding only while it stands on
    /// the ground. "On the ground" is the rules' own flag (ZObject.ShuttleAirborne false: landed, lift 0), the same one the view draws the air
    /// and the ground by: from the touchdown on, a crash landing's too (while it crashes, rests tilted and rights itself EXIT is taken, K426,
    /// and the rules hold the ramp until it stands level); never while it takes off, climbs, flies, hovers, turns onto its landing heading or
    /// comes down.
    ///  * EXIT (<see cref="Exit"/>): the EXIT cursor on the selected ship (UI/OpenZCursors.cs), the EXIT CREW button with its key, its touch key
    ///    and the gamepad button (UI/OpenZHud.cs CanExit), the right-click on the ship (View/PlayerInput.cs TryEject: up in the air that click
    ///    is a plain move order to the spot under the pointer, as the move cursor shown there says). The order itself
    ///    (<see cref="ExitOrder"/>, View/PlayerInput.cs EjectUnit) is refused up in the air with the game's refusal blip.
    ///  * Boarding (<see cref="Board"/>): the rules already take a squad only while the ship stands on the ground (ZObject.CanTakePassengers
    ///    needs ShuttleDoorOk or ShuttleEmptyTakes: landed, lift 0), so the ENTER cursor, the ENTER order of a click and a tap never offer an
    ///    airborne ship; <see cref="Board"/> is that same question for the checks.
    /// The rules are untouched: ZObject.CanEjectCrew still takes an EXIT given up in the air where the ship may land (it lands there and lets
    /// them out); no player control gives it any more, so that path is left to the checks. The computer never flies a shuttle. Every other
    /// unit (the APC, the guns) is offered exactly as before. No sound is added or taken away: the cursors keep their sounds, a refused order
    /// its blip.
    /// </summary>
    public static class OzShuttleAirOffers
    {
        /// <summary>Test switch (the game: on). Off = the old offers (EXIT also up in the air where the rules would take it). Checks only.</summary>
        public static bool GroundOnly = true;

        /// <summary>Check counters: EXIT offers held back because the ship was up in the air (asked by the cursor, the HUD and the right-click
        /// every frame), the player's EXIT orders refused up in the air.</summary>
        public static int AirExitHeld, AirExitOrdersRefused;

        /// <summary>A shuttle up in the air (the rules' ShuttleAirborne). False for every other unit and with the test switch off.</summary>
        public static bool InAir(ZObject u) => GroundOnly && u != null && u.IsShuttle && u.ShuttleAirborne;

        /// <summary>The player's side offers EXIT on u now: what the rules take (ZObject.CanEjectCrew), never a shuttle up in the air.</summary>
        public static bool Exit(ZObject u)
        {
            if (u == null || !u.CanEjectCrew()) return false;
            if (InAir(u)) { AirExitHeld++; return false; }
            return true;
        }

        /// <summary>View/PlayerInput.cs EjectUnit: the player's EXIT order on u may go to the rules. False = a shuttle up in the air (refused).</summary>
        public static bool ExitOrder(ZObject u)
        {
            if (!InAir(u)) return true;
            AirExitOrdersRefused++;
            return false;
        }

        /// <summary>The player's side offers boarding h to a robot of `team` (the ENTER cursor, a click's ENTER order, a tap): what the rules take
        /// (ZObject.CanTakePassengers, already only on the ground for a shuttle), never a shuttle up in the air.</summary>
        public static bool Board(ZObject h, Team team) => h != null && h.CanTakePassengers(team) && !InAir(h);
    }
}
