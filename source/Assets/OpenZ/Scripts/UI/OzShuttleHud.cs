using OpenZ.Sim;

namespace OpenZ
{
    /// <summary>
    /// The unit panel's second line for the playable shuttle (DV118, Sim/ZWorld.Shuttle.cs), called by OpenZHud where the driver /
    /// passengers line of a vehicle is made: the pilot (he never gets out), who is aboard (type, or the heroes' names, and how many),
    /// and what it is doing (on the ground, up, hovering because it cannot land, and why). The world badge over the ship shows
    /// <see cref="Badge"/> (the count aboard).
    /// </summary>
    public static class OzShuttleHud
    {
        public static string Line(ZObject u)
        {
            if (u == null) return "";
            if (u.ShuttleIsEmpty) return "EMPTY · NO PILOT · ORDER A SQUAD IN: ONE OF THEM FLIES IT";          // K256f (Sim/ZWorld.ShuttleEmpty.cs)
            string who = u.Passengers.Count > 0 ? $"PASSENGERS {OzHeroSquadView.CrewNameOr(u, true, UnitInfo.Name(u.PassengerType))} ×{u.Passengers.Count}" : "ROOM FOR ONE SQUAD";
            return $"{OzShuttleHeroesView.PilotWord(u)} · {who} · {(u.ShuttleExitOnLanding ? "LANDING TO LET THEM OUT" : State(u))}";   // K256f: EXIT given in the air; K256f2: a hero pilot by name, he gets out on EXIT (View/OzShuttleHeroesView.cs)
        }

        public static string State(ZObject u)
        {
            switch (u.ShuttleState)
            {
                case ZShuttleState.Landed: return u.ShuttleOnRepairPad ? "REPAIRING" : u.ShuttleExitPending ? "UNLOADING" : u.ShuttleRampPhase != ZShuttleRamp.Shut ? "RAMP OPEN" : u.ShuttleHasLeavers ? "LANDED · EXIT READY" : "LANDED";   // item 271
                case ZShuttleState.Climb: return "TAKING OFF";
                case ZShuttleState.Cruise: return "FLYING";
                case ZShuttleState.Descend: return "LANDING";
                case ZShuttleState.Hover: return "HOVERING · CANNOT LAND HERE" + (string.IsNullOrEmpty(u.ShuttleNoLand) ? "" : " (" + Why(u.ShuttleNoLand) + ")");
            }
            return "";
        }

        /// <summary>The count aboard for the world badge over the ship ("" when empty).</summary>
        public static string Badge(ZObject u) => u == null || u.Passengers.Count == 0 ? "" : u.Passengers.Count.ToString();

        public static string Why(string why)
        {
            switch (why)
            {
                case "water": return "WATER";
                case "rocks": return "ROCKS";
                case "blocked": return "NO ROOM";
                case "units": return "UNITS IN THE WAY";
                case "building": return "A BUILDING";
                case "flag": return "A FLAG";
                case "bridge": return "A BRIDGE";
                case "item": return "SOMETHING IN THE WAY";
                case "map edge": return "MAP EDGE";
                case "repair bay busy": return "REPAIR BAY BUSY";
            }
            return why.ToUpperInvariant();
        }
    }
}
