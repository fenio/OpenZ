using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// DV118, the shuttle's move orders and the places it can never land (K256g; the option SHUTTLE ORDERS of the view,
    /// UI/OzShuttleOrdersOption.cs). Deterministic: positions and fixed lists only, no random draw, no frame time.
    ///  * <see cref="ShuttleNeverLands"/>: the ship can never land with its middle at a clicked point: the point lies in the map's edge
    ///    band (<see cref="ShuttleEdgeTiles"/> tiles, where its middle may never stand, or off the map), or the landing spot rule
    ///    (<see cref="ShuttleLandSpot"/>: its own outline + 0.5 m and its ramp lane) finds no heading that fits on a 5-degree lattice of
    ///    headings (<see cref="ShuttleNeverLattices"/>: the landing tries one 15-degree lattice from the heading it arrives on, any heading)
    ///    once the units are left out (units walk away; water, rocks, closed tiles, buildings, huts, crates, flags,
    ///    bridges, props and plant crowns stay). An own repair station is never such a place (its deck has its own rule).
    ///  * <see cref="ShuttleOrdersFilter"/>: the view's PlayerInput hands it every order of one click (the map, the minimap, touch and the
    ///    gamepad pointer) before the orders are sent. With SHUTTLE ORDERS = LANDABLE SPOTS ONLY a human team's shuttle whose order is a move
    ///    to such a place is taken out of the click: it gets no order at all and flies on with what it had. The other units of the click
    ///    keep their orders, as for every order some units of a group cannot take (CheckWaypoint drops it for those only; the refusal blip
    ///    plays only when no unit took the order). ANYWHERE: nothing is taken out (it flies there and hovers where it cannot land).
    ///  * The rules themselves never read the option: SendWaypoints and ShuttleOrder are unchanged, so scripts, proofs and checks give
    ///    orders as before, and the hover over a blocked spot stays for safety (a unit standing on the spot when it arrives, STOP over
    ///    water, a spot that became blocked on the way).
    /// </summary>
    public sealed partial class ZWorld
    {
        /// <summary>Check counters (every ship): shuttle orders taken out of a click, and the reason of the last one.</summary>
        public static int ShuttleOrdersNoLand;
        public static string ShuttleOrdersNoLandWhy = "";
        /// <summary>The landing spot rule with the units left out (ShuttleNeverLands only; ZWorld.ShuttleGround.cs reads it).</summary>
        bool shuttleSpotNoUnits;

        /// <summary>Review fix (K256g): the base headings (deg) of the landing spot rule's 15-degree lattices ShuttleNeverLands tries: together a
        /// 5-degree lattice, fixed in the world (not the ship's own heading: the answer for a place does not change while the ship turns). The
        /// landing itself tries one 15-degree lattice from whatever heading it arrives on, so "never" has to mean no heading fits: on the
        /// ship's own lattice alone 15 of level 16's 3008 landable tile middles were refused, and flights there landed on 3 of 6; with these
        /// three, 6 are left, each landable only within a window of headings under 5 degrees wide (a 1-degree sweep finds 0, but costs 40 ms
        /// a place near an obstacle: too slow for the cursor).</summary>
        public static readonly int[] ShuttleNeverLattices = { 0, 5, 10 };

        /// <summary>The shuttle s can never land with its middle at the clicked point (x, y) (px). why = "map edge" (its edge band, or off the
        /// map), else the landing spot rule's word for the first thing in the way ("water", "rocks", "blocked", "building", "flag", "bridge",
        /// "item", "trees"), the units left out. False: it can land there (now, or once the units there have walked away).</summary>
        public bool ShuttleNeverLands(int x, int y, ZObject s, out string why)
        {
            for (int i = 0; ; i++)
            {
                int r = ShuttleNeverLandsPart(x, y, s, i, out why);
                if (r != 1) return r == 2;
            }
        }

        /// <summary>ShuttleNeverLands in parts, so the view's cursor can spread it over frames (one lattice a frame; the click asks it whole).
        /// Part 0: the map's edge band (never), an own repair station (it can land), the middle's own tile closed (water, rocks, a closed tile)
        /// or a building / map item over the middle (never at every heading: the outline holds the whole middle tile at every heading), then
        /// the landing spot rule on the lattice ShuttleNeverLattices[0]; part i (1, 2): the lattice ShuttleNeverLattices[i]. Returns 0 = it can
        /// land there, 2 = never, 1 = no heading of the lattices up to part i fits: ask part i + 1.</summary>
        public int ShuttleNeverLandsPart(int x, int y, ZObject s, int part, out string why)
        {
            why = "";
            if (Map == null || s == null || !s.IsShuttle) return 0;
            if (x < 0 || y < 0 || (x >> 4) >= Map.width || (y >> 4) >= Map.height) { why = "map edge"; return 2; }
            var (cx, cy) = ShuttleClamp(x, y);
            if (cx >> 4 != x >> 4 || cy >> 4 != y >> 4) { why = "map edge"; return 2; }                 // the edge band: its middle may never stand there
            if (ShuttleStationAt(x, y, s) != null) return 0;                                              // an own repair station: its deck's own rule (item 271)
            if (part == 0 && ShuttleRepairStation(s) == null && ShuttleMiddleClosed(cx, cy, s, out why)) return 2;   // (on a repair trip the deck rule may apply: the full rule)
            int k = part < 0 ? 0 : part >= ShuttleNeverLattices.Length ? ShuttleNeverLattices.Length - 1 : part;
            bool was = shuttleSpotNoUnits; shuttleSpotNoUnits = true;
            try { if (ShuttleLandSpot(cx, cy, s, ShuttleNeverLattices[k], out _, out why)) { why = ""; return 0; } }
            finally { shuttleSpotNoUnits = was; }
            return k + 1 < ShuttleNeverLattices.Length ? 1 : 2;
        }

        /// <summary>Review fix: what closes the spot at every heading, as the landing spot rule itself reads it (ShuttleSpotPart: every 4 px of
        /// the middle tile lies inside the outline + 0.5 m at every heading, its inscribed radius at the middle being 2.8 m; a building or map
        /// item that holds the middle point is hit at once). why = the rule's own word.</summary>
        bool ShuttleMiddleClosed(int cx, int cy, ZObject s, out string why)
        {
            why = "";
            int tx = cx >> 4, ty = cy >> 4;
            var c = Map.ClassAt(tx, ty);
            if (c == ZClass.Water || c == ZClass.DeepWater) { why = "water"; return true; }
            if (c == ZClass.Impassable) { why = "rocks"; return true; }
            if (!Path.TilePassable(tx, ty, false)) { why = "blocked"; return true; }
            foreach (var o in Objects)
            {
                if (o == s || o.Destroyed || o.KillAt >= 0 || (o.Kind != ZKind.Building && o.Kind != ZKind.MapItem) || !o.Contains(cx, cy)) continue;
                why = o.Kind == ZKind.Building ? "building" : o.Type == "flag" ? "flag" : o.IsBridge ? "bridge" : "item";
                return true;
            }
            return false;
        }

        /// <summary>One click's orders (unit -> its waypoint list, as PlayerInput collects them), before they are sent: with landableOnly every
        /// shuttle of the human team `team` whose order is a move to a place where it can never land (<see cref="ShuttleNeverLands"/>) is
        /// taken out of `pending` (no order for it) and added to `refused`. The shuttle's order is the one ShuttleOrder reads: the first
        /// UNIT_REPAIR (kept) or MOVE / FORCE MOVE of its list; STOP (an empty list) and everything else are kept. Returns how many were taken
        /// out (0 with landableOnly false: ANYWHERE, the rules as they were).</summary>
        public int ShuttleOrdersFilter(Team team, IDictionary<ZObject, List<ZWaypoint>> pending, bool landableOnly, List<ZObject> refused = null)
        {
            if (!landableOnly || pending == null || pending.Count == 0 || !IsHumanTeam(team)) return 0;
            List<ZObject> drop = null;
            foreach (var kv in pending)
            {
                var o = kv.Key;
                if (o == null || !o.IsShuttle || o.Destroyed || o.Owner != team || kv.Value == null) continue;
                var land = ShuttleLandingMove(kv.Value);                                                       // DV125: a route's LAST move (its landing); one point: the first move, as before (Sim/ZWorld.ShuttleQueue.cs)
                if (land.HasValue && ShuttleNeverLands(land.Value.x, land.Value.y, o, out string why))
                {
                    (drop ??= new List<ZObject>()).Add(o);
                    o.ShuttleNoLandOrders++; ShuttleOrdersNoLand++; ShuttleOrdersNoLandWhy = why;
                }
            }
            if (drop == null) return 0;
            foreach (var o in drop) { pending.Remove(o); refused?.Add(o); }
            return drop.Count;
        }
    }

    public partial class ZObject
    {
        /// <summary>K256g: this shuttle's move orders taken out of a click because it could never land there (the view's red mark reads it).</summary>
        public int ShuttleNoLandOrders;
    }
}
