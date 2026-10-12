using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// Two deviations, one switch (ZWorld.PathV3Deviation, PlayerPrefs oz_path_v3, off in 'Zod exact'):
    ///
    /// DV44 re-scoped. DV44's squad trail follow (PT1), path budget (PT2)
    /// and quiet arrivals (PT3) (ZCommit*.cs) run everywhere EXCEPT inside ZONES, where the unit keeps the live pathing layer
    /// (DV6 / DV8 / DV24 / DV36 rules: its own Zod path, the lanes' and bridge queue's back-and-forth, the live pushes and arrivals).
    /// Zones are derived at level load from the Zod grid (again whenever passability changes), deterministically, PER GRID (a unit
    /// asks the mask of its own grid: robots the robot grid, vehicles their 2x2 footprint):
    ///  - every bridge (intact or destroyed): its rect + BridgeEnd tiles past each end along its axis + 1 tile either side, and the
    ///    keep-clear area of the live lane on its deck (where the bridge queue lines units up);
    ///  - bottlenecks: a free run across of at most NarrowRun positions (robots: tiles, vehicles: footprint positions = a 2-3 tile
    ///    corridor) that is a real passage - open on both ends and no way round it within DetourMax steps - grown by NarrowGrow;
    ///  - water crossings: wadeable water tiles whose water run across (a row or a column) is at most WaterCrossRun tiles with dry
    ///    land at both ends (rivers, the canal, narrow harbour arms; not the open harbour basins) + WaterShore tiles of shore (robots);
    ///  - fort gates: the gate route through each fort (ZClearance's kept-open tiles) grown by GateGrow tiles.
    /// SWITCHING: a unit is ZONED when its centre tile is in its grid's zone mask, or its own way ahead (path / trail) enters it within
    /// LookAhead px (the rule set changes before the passage, on open ground, not in its mouth); it stays zoned until it is ZoneExitGrow
    /// tiles clear of the mask and has been out ZoneExitHold s (hysteresis). In a static zone each member switches where it reaches it;
    /// an active passage / an encounter zones its squad's members in tight places. ONE RULE SET PER ENCOUNTER: in a tight place (a zone, its band, a narrow run, a lane's keep-clear area) a unit within
    /// EncStart px of a zoned unit of another squad / group is zoned too (and so its squad) until the two are EncEnd px apart for EncHold s.
    /// ONE ARRIVAL RULE SET PER GROUP ORDER: a group order whose clicked point lies in / next to a zone, or whose members' straight ways
    /// to it cross a zone, gets the live group spots AND the live arrival / push rules for every member until its next order; any other
    /// group order DV44's spots and settle.
    /// HAND-OVER: a minion that followed its leader's trail (no path of its own) walks on along its leader's way (the trail ahead of
    /// its place, then his path) when it becomes zoned - no stop, no 1-tick wait, never turned round (one level with / ahead of its
    /// leader keeps its own Zod path forward). Leaving, it keeps that path until the trail ahead of it is straight and runs its way.
    /// REVIEW ROUND 2: a new order decides the zone state anew once its own way is known (nothing carries over from an earlier move);
    /// every zone reason applies only in tight places (a zone or its band, a narrow run + TightGrow, a lane's keep-clear area, a
    /// passage) - ZoneExitHold s out of them a squad is DV44 again (a unit on a live group order keeps the live rules on the whole order);
    /// a zoned unit stays zoned ZonedHold s after its last reason and switches back within MinDwell s only across a real zone edge
    /// (DwellEdgeMove px on from its last switch; a new order does not lift it), never more than twice within MinDwell s (FlipCap);
    /// a passage whose free run is 1 tile or that a wreck narrows is a zone for squads even one way; within a zoned
    /// squad a minion blocked by a mate queues behind it (DV44's gating), and a zoned unit held by another unit (behind a mate: only
    /// with other traffic near) walks again once the gap has grown ResumeAnyGap px or after ResumeAnyMax s (no stop-go every other
    /// tick); the hand-back rules of the file (strict order of two standing mates, a stalled follower's own path after 1 s) apply to
    /// squads just out of a zone.
    /// REVIEW ROUND 3: bridges, water crossings and gates run the live rules literally (no head-on smoothing on their tiles or bands, no
    /// pace cap in any zone, Zod's path at the hand-over, a squad goes onto them as one); an encounter needs opposing goals; a <= 2-tile
    /// passage is live one way too for the units whose way goes through it (a squad as one, a group order through it gets the live spots,
    /// DV44's one-tile lanes there leave the list while only live traffic is at them); the head-on smoothing needs a head-on reason;
    /// DV47's clock with no debris near hands the unit and its squad to the live rules until past the passage (TestR4).
    ///
    /// DV47 "soft unstick" (below): a unit with a move / attack order (a squad leader for his squad too) in a tight place that made less
    /// than UnstickProgress px of net progress along its way to the order's goal over the last UnstickAfter s of active time (helper
    /// moves - step aside, back off, regroup, bay - and path waits pause the clock; a new order restarts it) may plan (every UnstickRetry
    /// s) a path through SOFT blockers only - wreck map items (DV39's harbour wrecks, the test wreck) - never buildings, rocks, city
    /// blocks, huts, props, walls, cliffs, water for vehicles, or any other impassable; the units in the way are gone round first, then
    /// ignored (the gate keeps the spacing). With that path it may step over those tiles (ZObject.ProcessMove: SoftStep), a lane lets it
    /// pass (the debris is its second lane), and the permission ends once it is past them (UnstickClear s off every soft tile) or after
    /// UnstickHold s (never while it still stands on one). A passage that debris closes completely: a unit whose way round is much longer
    /// than the way over the debris (or whose goal lies behind it) walks to the debris, waits UnstickAfter s and then goes over it. The
    /// view draws it over the debris (UnitView skips only the soft tiles in its prop push-out while PathSoftPass; building mesh tiles and
    /// props still push the model out).
    ///
    /// Everything is sim, fixed tick, object / id order, integer tiles; no RNG, no clock, no view state. ZWorld.PathV3Deviation = false
    /// = the live pathing layer bit for bit (every hook returns the live value first).
    /// </summary>
    public sealed partial class ZPathing
    {
        // ------------------------------------------------------------------ switches (per world; the game always runs both; tests A/B)
        /// <summary>Zones on (v3). False = DV44 everywhere (the old pathing_commit_v1 behaviour; A/B only).</summary>
        public bool ZonesOn = TestV3Defaults[0];
        /// <summary>DV47 soft unstick on.</summary>
        public bool UnstickOn = TestV3Defaults[1];
        /// <summary>Headless A/B harnesses only (never set by the game): zones, unstick for new worlds.</summary>
        internal static readonly bool[] TestV3Defaults = { true, true };
        /// <summary>Headless A/B only (the game runs the defaults): [0] a squad switches as one in a static zone too (default: every member by its
        /// own tile and way there - it switches where the trail reaches the zone; an encounter / active passage always zones the whole squad),
        /// [1] an order ending at a zone keeps the live rules for the whole order, [2] a bridge lane's keep-clear area is part of the bridge
        /// zone, [3] sticky: a unit zoned once on an order keeps the live rules until that order ends, [4] compact squads (the live regroup
        /// reach), [5] live group spots everywhere, [6] DV44's one-tile-long lanes outside the zones, [7] footprint re-slot.</summary>
        internal static readonly bool[] TestZoneRules = { false, false, true, false, false, false, true, false };
        internal bool ShortLanesOutside => TestZoneRules[6] && !TestNoShortLanes;
        /// <summary>(A/B, headless) no DV44 one-tile-long lanes at all.</summary>
        internal static bool TestNoShortLanes;
        /// <summary>[7] DV44's footprint arrival first looks for a free spot nearer the clicked point on the unit's own side (ReSlot).</summary>
        internal bool FootprintReslot => TestZoneRules[7];
        /// <summary>Headless check only: every unit counts as zoned (v3 must then be the live layer).</summary>
        internal static bool TestAllZoned;
        /// <summary>Zone kinds (bit mask: 1 bridges, 4 bottlenecks, 8 water crossings, 16 fort gates; 2 is unused). Default 29 = all.</summary>
        internal static int TestZoneKinds = 29;
        /// <summary>A/B (headless): every wadeable water tile is a crossing (else only runs of at most WaterCrossRun tiles between dry land).</summary>
        internal static bool TestWaterAll = false;
        /// <summary>A/B (headless): the way-ahead switch (else the centre tile only), the encounter rule, the walking hand-over (else stop +
        /// Zod re-path next tick, the round-1 hand-over), the straight re-join, the group-order rule set, the soft shortcut.</summary>
        internal static bool TestLookAhead = true, TestEncounter = true, TestHandoverWalk = true, TestStraightRejoin = true, TestGroupRules = true, TestSoftShortcut = true;
        /// <summary>A/B (headless): bottlenecks are static zones (round-1 style) instead of zones only while traffic opposes in them.</summary>
        internal static bool TestNarrowStatic = false;
        /// <summary>A group order whose members' straight ways (at least GroupCrossShare of them) cross a zone also gets the live spots and
        /// arrivals (A/B: off = only the clicked point near a zone decides).</summary>
        internal static bool TestGroupCross = true;
        /// <summary>A group order with the live spots / arrivals runs the live rules for the whole order (every member zoned while it has an
        /// order: one rule set per group order; A/B off = only its arrivals).</summary>
        internal static bool TestLiveOrderZoned = true;

        // ------------------------------------------------------------------ tuning (tiles / s / px; constants)
        public const int BridgeEnd = 3, NarrowRun = 2, NarrowGrow = 0, WaterShore = 0, WaterCrossRun = 10, GateGrow = 1, ZoneExitGrow = 2, DetourMax = 12;
        public const double ZoneExitHold = 0.5, LookAhead = 32;
        /// <summary>A minion that walked its own path in a zone keeps it at least RejoinHold s after its squad left the zone (and its band),
        /// then re-joins the trail at the next straight segment (RejoinOk); the arrival / push rules are DV44's again as soon as it is out.</summary>
        public static double RejoinHold = 2.0;
        /// <summary>DV47 tight places: every narrow run (any, robot grid) grown by TightGrow tiles.</summary>
        public const int TightGrow = 3;
        /// <summary>
        /// a passage is active when two units of different squads with orders, within NarrowSense tiles of it, head opposite ways
        /// (their directions to their goals at more than NarrowOppose deg); it stays active NarrowHold s after that and while a unit it
        /// zoned is still within NarrowReach tiles of it. While active, every unit within NarrowReach tiles of it (or whose way ahead
        /// enters that) is zoned. One-way traffic: review round 3 - a <= 2-tile passage is live for the units that go through it (TestR4 8).</summary>
        public static int NarrowSense = 6, NarrowReach = 3;
        public const double NarrowHold = 1.0, NarrowOppose = 120;
        /// <summary>Encounter rule: start within EncStart px of a zoned unit, end EncEnd px apart for EncHold s.</summary>
        public const double EncStart = 48, EncEnd = 64, EncHold = 1.0;
        /// <summary>A group order's spots and arrivals use DV44's rules only when no zone lies within GroupZoneReach tiles of the clicked
        /// point and no member's straight way to it crosses a zone (GroupRouteSide tiles either side). (A/B rule [1], off: an order whose
        /// goal lies within GoalZoneReach tiles of a zone runs the live rules for the whole order.)</summary>
        public const int GoalZoneReach = 2, GroupZoneReach = 2, GroupRouteSide = 1;
        /// <summary>Re-join after a zone: the minion is within RejoinOff px of the trail and the trail turns less than RejoinTurn deg over the
        /// 2 tiles ahead of its place (a straight segment); RejoinWindow s after the squad left the zone it re-joins anyway.</summary>
        internal static double RejoinTurn = 30, RejoinOff = 32, RejoinWindow = 12;
        public const double UnstickAfter = 5.0, UnstickProgress = 16, UnstickRetry = 0.5, UnstickHold = 10.0, UnstickClear = 0.5, UnstickSample = 0.25;
        /// <summary>Soft search window (tiles round the unit) and how far a soft blocker may be for a search to run at all.</summary>
        public const int SoftWindow = 32, SoftReach = 12, SoftMaxNodes = 20000;
        /// <summary>Soft search: extra cost per tile of a lane's zone for a unit the lane holds (it waits there against oncoming traffic).</summary>
        public const int LaneAvoidCost = 40;
        /// <summary>Soft shortcut: the way round (Zod's path) is longer than the way over the debris by more than ShortcutExtra px AND by
        /// ShortcutShare of it.</summary>
        public const double ShortcutExtra = 192, ShortcutShare = 0.5;
        /// <summary>Soft shortcut: the way round must take longer than the way over the debris by UnstickAfter + ShortcutMargin s (the wait).</summary>
        public const double ShortcutMargin = 3.0;

        // ------------------------------------------------------------------ per-unit predicates (every DV44 rule asks these)
        internal bool Zoned(ZObject o) => o != null && ZonesOn && o.PC.zoned;
        /// <summary>DV44 PT1 (trail follow, short lanes, squad lane turns) applies to this unit / pair.</summary>
        internal bool FollowOn(ZObject o) => CommitOn && CommitFollow && !Zoned(o);
        internal bool FollowOn(ZObject a, ZObject b) => CommitOn && CommitFollow && !Zoned(a) && !Zoned(b);
        /// <summary>DV44 PT3 (settle / arrival rules) applies to this unit / pair (not zoned, not on a group order with the live arrivals - such
        /// an order also gets the live regroup reach and follow-through for its squads, ZCommit.Follow.cs).</summary>
        internal bool SettleFor(ZObject o) => CommitOn && CommitSettle && !Zoned(o) && !LiveOrder(o);
        internal bool SettleFor(ZObject a, ZObject b) => CommitOn && CommitSettle && !Zoned(a) && !Zoned(b) && !LiveOrder(a) && !LiveOrder(b);
        bool LiveOrder(ZObject o) => ZonesOn && o.PC.liveOrder;
        /// <summary>DV44 PT2 (path budget) applies to this unit's request (not in a zone: there a request runs at once, like live).</summary>
        internal bool BudgetFor(ZObject o) => CommitOn && CommitBudget && !Zoned(o);
        internal bool KeepSubPixelFor(ZObject o) => SettleFor(o);
        internal bool StrictLaneParkingFor(ZObject o) => SettleFor(o);
        internal bool SearchBudgetLeftFor(ZObject o) => !BudgetFor(o) || NodesTotal - tickWorkBase < WorkBudget;

        // ------------------------------------------------------------------ zone map (per grid)
        byte[] zR, zV; int zw, zh, zVersion = int.MinValue, zLanes = -1;
        bool[] ztight;                                                                     // DV47's tight places: narrow runs (any), lanes' keep-clear areas (grown)
        const byte ZInner = 1, ZOuter = 2, ZBridgeBit = 4, ZNarrowBit = 8, ZWaterBit = 16, ZLaneBit = 32, ZGateBit = 64;
        readonly HashSet<int> softTiles = new HashSet<int>();
        readonly List<ZObject> softObjs = new List<ZObject>();
        /// <summary>Checks: walkable positions of each grid (robots: tiles, vehicles: footprint positions) and how many are in a zone; per
        /// kind for the robot grid.</summary>
        public int ZoneWalkable { get; private set; }
        public int ZoneTiles { get; private set; }
        public int ZoneWalkableV { get; private set; }
        public int ZoneTilesV { get; private set; }
        public int ZoneBridgeTiles { get; private set; }
        public int ZoneNarrowTiles { get; private set; }
        public int ZoneWaterTiles { get; private set; }
        public int ZoneLaneTiles { get; private set; }
        public int ZoneGateTiles { get; private set; }
        public int ZoneBuilds { get; private set; }
        /// <summary>Soft blocker tiles on the map now (DV47).</summary>
        public int SoftTileCount => softTiles.Count;
        /// <summary>Checks: DV47 unsticks started, soft searches run, unit-ticks on the soft pass, soft shortcuts taken; zone switches,
        /// trail -> own path hand-overs, group orders with the live rules.</summary>
        public int Unsticks { get; private set; }
        public int SoftSearches { get; private set; }
        public int SoftPassTicks { get; private set; }
        public int SoftShortcuts { get; private set; }
        public int ZoneSwitches { get; private set; }
        public int Handovers { get; private set; }
        public int LiveGroupOrders { get; private set; }

        readonly List<ZObject> encNear = new List<ZObject>();
        bool v3Dirty = true;

        /// <summary>BeginTick (after EnsureLanes): the zone map is current, every unit's zone state updated (hysteresis, way ahead,
        /// encounters, squads together). With the switch off: no unit keeps a v3 state (a soft pass ends at once).</summary>
        void UpdateZones()
        {
            if (!CommitOn) { ClearV3State(); return; }
            v3Dirty = true;
            SyncLanes(false);
            EnsureZoneMap();
            if (!ZonesOn) return;
            double now = W.Time;
            ActivatePassages(now);
            SyncLanes(true);
            foreach (var u in units)
            {
                var c = u.PC;
                if (!u.IsMobile) { c.zoneOwn = false; continue; }
                var zm = ZM(u);
                int t = ZIdx(u.CenterX >> 4, u.CenterY >> 4);
                byte m = t >= 0 ? zm[t] : (byte)0;
                bool inner = (m & ZInner) != 0, band = (m & ZOuter) != 0, pin = false, pband = false;
                bool squad = SqueezeFor(u);
                int nc = t >= 0 ? nNear[t] : -1;
                int ad = R3(8192) && t >= 0 ? ActiveDist(t, now) : int.MaxValue;
                if (R3(16384)) ad = Math.Min(ad, GhostDist(t, now));
                if (ad <= NarrowReach) pin = true; else if (ad <= NarrowReach + ZoneExitGrow) pband = true;   // review round 2: any active passage in reach
                c.passActive = ad <= NarrowReach + ZoneExitGrow;
                if (R3(8192))
                {
                    if (!pin && !pband && squad && nc >= 0 && nSqueeze[nc]) { if (nComp[t] == nc) pin = true; else if (nDist[t] <= NarrowReach + ZoneExitGrow) pband = true; }
                }
                else if (nc >= 0 && nActive[nc] >= now)
                {                                                                        // an active bottleneck (opposing traffic in it) near it
                    if (nDist[t] <= NarrowReach) pin = true; else if (nDist[t] <= NarrowReach + ZoneExitGrow) pband = true;
                }
                else if (squad && nc >= 0 && nSqueeze[nc])
                {                                                                        // review round 2: a squeeze (1 tile, or narrowed by debris) for a squad even one way:
                    if (nComp[t] == nc) pin = true; else if (nDist[t] <= NarrowReach + ZoneExitGrow) pband = true;   // in it, or (hold) near it
                }
                bool innerTile = inner, pinTile = pin, innerAhead = false, pinAhead = false;
                c.tightNow = TightFor(u, t) || (t >= 0 && nComp[t] >= 0) || (R3(16384) && GhostDist(t, now) <= NarrowReach + ZoneExitGrow);   // (a ghost passage is still tight)
                c.zCls = TileCls(u, u.CenterX, u.CenterY);                                // (its tile's kind: zone, band, tight place, passage)
                if (c.tightNow || (R3(262144) && TightNear(u, t, ConfineBand))) c.tightLastIn = now;   // (R3 262144: spatial hysteresis, ConfineBand tiles)
                bool oppAhead = false;
                if (TestLookAhead && u.Waypoints.Count > 0 && !HelperMove(u.Waypoints[0]))
                {                                                                        // its way ahead goes into a zone: switch now
                    if (!inner && AheadHits(u, LookAhead, k => (zm[k] & ZInner) != 0)) inner = innerAhead = true;
                    if (!pin && AheadHits(u, LookAhead, k => nComp[k] >= 0 && nActive[nComp[k]] >= now)) pin = pinAhead = oppAhead = true;
                    if (!pin && squad && AheadHits(u, SqueezeLook, k => nComp[k] >= 0 && nSqueeze[nComp[k]])) pin = pinAhead = true;   // (a squeeze: SqueezeLook ahead)
                }
                c.oppNow = oppAhead || (nc >= 0 && nActive[nc] >= now && nDist[t] <= NarrowReach + ZoneExitGrow);   // review round 3: opposing traffic in a passage near it / on its way
                c.oppSquad = false;
                if (R4(8)) PassLiveStep(u, c, t, nc, now);                                // review round 3: a <= 2-tile passage is live for the units that go through it one way
                if (R4(128)) StuckLiveStep(u, c, t, nc, now);                             // review round 3: DV47's clock fired with no soft blocker near: live until past the passage
                int oi = OrderIndex(u); var ow = oi >= 0 ? u.Waypoints[oi] : ZWaypoint.Cleared;
                if (R3(16777216) && ow.mode == WpMode.Enter && ow.playerGiven && !c.liveOrder && W.IsHumanTeam(u.Owner))
                {                                                                        // review round 2 follow-up: a player's ENTER order (and DV59's walk to the spot next
                    c.liveOrder = true; c.liveZOff = false;                              // to a vehicle someone else got first, which keeps the order) runs the live rules:
                    Log(u, "v3: a player's enter order: live rules and arrivals");      // boarding is the live layer's (DV59 ACTIVE again: the squad stopped 3.8 tiles out)
                }
                if (!ow.Equals(c.zoneWp) && R2(2) && (!R3(2) || OrderWayKnown(u, oi)))
                {                                                                        // review round 2: a new order re-decides the zone state - zoned for it only when its
                    c.zoneWp = ow;                                                       // own tile is in the inner mask or its new way enters it within the look-ahead
                    if (ow.mode == WpMode.Move || ow.mode == WpMode.Attack || ow.mode == WpMode.ForceMove)
                    {                                                                    // (standing in the band from an earlier move does not carry over)
                        if (!innerTile && !innerAhead && c.zoneOwn) { c.zoneOwn = false; Log(u, "v3: new order away from the zone: out of it at once"); }
                        if (!pinTile && !pinAhead && c.passOwn) c.passOwn = false;
                    }
                }
                if (inner) { c.zoneOwn = true; c.zoneLastIn = now; }
                else if (c.zoneOwn && !band && now - c.zoneLastIn >= ZoneExitHold) c.zoneOwn = false;
                if (pin) { c.passOwn = true; c.passLastIn = now; }
                else if (c.passOwn && !pband && now - c.passLastIn >= ZoneExitHold) c.passOwn = false;
                var wp = u.Waypoints.Count > 0 ? u.Waypoints[0] : ZWaypoint.Cleared;
                if (TestZoneRules[3])
                {                                                                        // (A/B) sticky per order
                    if (!wp.Equals(c.stickyWp)) { c.stickyWp = wp; c.sticky = false; }
                    if (c.zoneOwn && wp.mode != WpMode.None) c.sticky = true;
                }
                if (!wp.Equals(c.goalWp))
                {                                                                        // (A/B) a new waypoint: does it END in / next to a zone?
                    c.goalWp = wp;
                    c.goalZoned = TestZoneRules[1] && wp.mode == WpMode.Move && ZoneWithin(wp.x, wp.y, GoalZoneReach);
                }
            }
            if (TestEncounter) Encounters(now);
            foreach (var u in units)
            {
                var c = u.PC;
                if (c.liveOrder && u.Waypoints.Count > 0) c.liveLastIn = now;              // review round 2: the live group order's term holds ZoneExitHold s after its
                bool liveZ = TestLiveOrderZoned && c.liveOrder && (u.Waypoints.Count > 0 || (R2(1) && now - c.liveLastIn < (R3(8) ? LiveOrderHold : ZoneExitHold)));   // waypoints emptied (a re-issued order: no flip)
                if (R3(8))
                {                                                                        // review round 2: once its waypoints have been empty LiveOrderHold s the term is
                    if (c.liveOrder && !liveZ) c.liveZOff = true;                          // over for this order (a later regroup / step aside does not switch it back on)
                    if (c.liveZOff) liveZ = false;
                }
                bool liveAll = liveZ && R3(32768) && (!u.IsRobot || R3(131072)) && !(R4(33554432) && c.liveOrderPass);   // review round 2: a vehicle (A/B 131072: every unit) keeps its live group order's rules everywhere (review round 3: an order through a passage only in tight places)
                if (R3(4) && !c.tightNow && !liveAll) liveZ = false;                       // review round 2: the live order's rules where it is tight only (open ground: DV44)
                bool plive = (R4(8) && c.passLive) || (R4(128) && c.stuckLive);           // review round 3: live through a passage one way / after DV47's clock fired
                bool exitLive = R4(134217728) && u.Waypoints.Count > 0 && u.Waypoints[0].mode == WpMode.ForceMove;   // review round 3 (bot matches): Zod's factory / repair exit FORCE_MOVE runs the live rules
                plive |= exitLive;
                c.zonedNext = c.zoneOwn || c.passOwn || c.encOn || c.goalZoned || (TestZoneRules[3] && c.sticky) || TestAllZoned || liveZ || plive;
                c.zonedDyn = c.passOwn || c.encOn;
                if (R3(128) && !TestAllZoned && !c.tightNow && now - c.tightLastIn >= ZoneExitHold)
                { c.zonedNext = liveAll || (plive && !PassLiveConfined); c.zonedDyn = false; }   // review round 2: on open ground (out of every tight place ZoneExitHold s) DV44 always (squads; review round 3: the one-way live term too, PassLiveConfined)
                c.zwhy = (c.zoneOwn ? 1 : 0) | (c.passOwn ? 2 : 0) | (c.encOn ? 4 : 0) | (liveZ ? 8 : 0) | (c.goalZoned || c.sticky || TestAllZoned ? 32 : 0) | (plive ? (exitLive ? 256 : 128) : 0);
                if (c.encOn) c.oppNow = true;                                            // (review round 3: an encounter is opposing traffic: R4 1)
                if (R4(32) && !c.oppNow && c.tightNow && OpposedNear(u)) c.oppNow = true;   // (and a unit of another order heading the other way within OpposedReach px)
            }
            if (R4(8388608))
                foreach (var u in units)
                {                                                                        // review round 3 (A/B): a squad goes onto a bridge / water / gate zone as one - every
                    if (u.IsMinion || u.Minions.Count == 0 || !u.IsRobot) continue;      // member live from the moment its leader's way enters it within StaticSquadLook px
                    bool z = (u.PC.zwhy & 1) != 0 && u.PC.zonedNext;                     // or the leader is in it, until the squad is out
                    if (!z && u.Waypoints.Count > 0 && !HelperMove(u.Waypoints[0]) && AheadHits(u, StaticSquadLook, k => (zR[k] & ZInner) != 0)) z = true;
                    if (!z) foreach (var m in u.Minions) if (m != null && m.PC.zonedNext && (m.PC.zwhy & 1) != 0) z = true;
                    if (!z) continue;
                    if (!u.PC.zonedNext) { u.PC.zwhy |= 1 | 16; u.PC.zonedNext = true; }
                    foreach (var m in u.Minions) if (m != null && !m.PC.zonedNext) { m.PC.zwhy |= 1 | 16; m.PC.zonedNext = true; }
                }
            if (R4(16))
                foreach (var u in units)
                {                                                                        // review round 3: a squad goes through a passage one way as one (every member
                    if (u.IsMinion || u.Minions.Count == 0) continue;                    // live from the moment the first one's way enters it until the last one is past)
                    bool pl = u.PC.zonedNext && (u.PC.zwhy & 128) != 0;
                    foreach (var m in u.Minions) if (m != null && m.PC.zonedNext && (m.PC.zwhy & 128) != 0) pl = true;
                    if (!pl) continue;
                    bool tightOnly = R4(67108864);                                       // (R4 67108864: only the members in a tight place - the rest follows the trail until it gets there)
                    if (!tightOnly || u.PC.tightNow) { if (!u.PC.zonedNext) u.PC.zwhy |= 128 | 16; u.PC.zonedNext = true; }
                    foreach (var m in u.Minions) if (m != null && (!tightOnly || m.PC.tightNow)) { if (!m.PC.zonedNext) m.PC.zwhy |= 128 | 16; m.PC.zonedNext = true; }
                }
            foreach (var u in units)
            {                                                                            // review round 3: opposing traffic for one member is for its squad (the smoothing)
                if (u.IsMinion || u.Minions.Count == 0) continue;
                bool o = u.PC.oppNow;
                foreach (var m in u.Minions) if (m != null && m.PC.oppNow) o = true;
                if (!o) continue;
                u.PC.oppSquad = true; foreach (var m in u.Minions) if (m != null) m.PC.oppSquad = true;
            }
            foreach (var u in units)
            {                                                                            // an encounter / active passage zones the whole squad (one rule set per
                if (u.IsMinion || u.Minions.Count == 0) continue;                        // encounter); a static zone each member by its own tile and way (it
                bool z = TestZoneRules[0] ? u.PC.zonedNext : u.PC.zonedDyn;              // switches where the trail reaches the zone)
                foreach (var m in u.Minions) if (m != null && (TestZoneRules[0] ? m.PC.zonedNext : m.PC.zonedDyn)) z = true;
                if (!z) continue;
                if (TestProp == 1) continue;                                             // (A/B: each member by its own state only)
                if (TestProp == 2 && !TestZoneRules[0])
                {                                                                        // review round 2: only the members at the passage / in the encounter's
                    if (PropNear(u)) u.PC.zonedNext = true;                              // tight place - the rest of the squad follows the trail until it gets there
                    foreach (var m in u.Minions) if (m != null && PropNear(m)) m.PC.zonedNext = true;
                    continue;
                }
                if (R3(32))
                {                                                                        // review round 2: the encounter / active passage zones the squad members that
                    if (u.PC.tightNow && !u.PC.zonedNext) { u.PC.zonedNext = true; u.PC.zwhy |= 16; }   // are in a tight place; the rest follows the trail (DV44) until
                    foreach (var m in u.Minions) if (m != null && m.PC.tightNow && !m.PC.zonedNext) { m.PC.zonedNext = true; m.PC.zwhy |= 16; }   // it gets there
                    continue;
                }
                if (!u.PC.zonedNext) u.PC.zwhy |= 16;
                u.PC.zonedNext = true;
                foreach (var m in u.Minions) if (m != null) { if (!m.PC.zonedNext) m.PC.zwhy |= 16; m.PC.zonedNext = true; }
            }
            foreach (var u in units)
            {
                var c = u.PC;
                if (R3(1024))
                {                                                                        // review round 2: hysteresis on the whole zone state - zoned ZonedHold s
                    if (c.zonedNext) c.zWantLast = now;                                    // after its last reason (a passage's / squad's / encounter's reason that
                    else if (c.zoned && now - c.zWantLast < ZonedHold && (c.tightNow || now - c.tightLastIn < ZoneExitHold)) { c.zonedNext = true; c.zwhy |= 64; }   // flickers does not flip it); open ground ends it
                }
                bool edge = c.zCls != c.zSwitchCls && (DwellEdgeMove <= 0 || Math.Abs(u.CenterX - c.zSwitchX) + Math.Abs(u.CenterY - c.zSwitchY) >= DwellEdgeMove);   // (a real edge crossing: its tile is of another kind now)
                if (R3(2097152) && c.zonedNext != c.zoned && (DwellAnyOrder || OrderOf(u).Equals(c.zSwitchWp)) && (now - c.zSwitchAt < MinDwell && !edge || !c.zonedNext && now - c.zSwitchAt < DwellExit || FlipCap && now - c.zSwitchPrevAt < MinDwell))
                { c.zonedNext = c.zoned; DwellHolds++; }                                 // review round 2: no switch back within MinDwell s on the same kind of tile and order
                if (c.zonedNext != c.zoned) { c.zSwitchPrevAt = c.zSwitchAt; c.zSwitchAt = now; c.zSwitchCls = c.zCls; c.zSwitchWp = OrderOf(u); c.zSwitchX = u.CenterX; c.zSwitchY = u.CenterY; }
                if (c.zonedNext != c.zoned) { ZoneSwitches++; if (!c.zonedNext) c.zoneOffAt = now; Log(u, c.zonedNext ? "v3: zoned (live rules)" : "v3: out of the zones (DV44)"); }
                c.zoned = c.zonedNext;
            }
            foreach (var u in units)
            {                                                                            // review round 3 (R4 8192): a head-on reason (an active passage or a squeeze, an
                if (u.IsMinion) continue;                                                // encounter) of one squad member is its squad's (the smoothing)
                bool h = (u.PC.zwhy & (2 | 4)) != 0 && u.PC.zoned;
                foreach (var m in u.Minions) if (m != null && m.PC.zoned && (m.PC.zwhy & (2 | 4)) != 0) h = true;
                if (!h) continue;
                u.PC.headLast = now; foreach (var m in u.Minions) if (m != null) m.PC.headLast = now;
            }
        }

        // ------------------------------------------------------------------ review round 3: live in bridge / water / gate zones literally, live one way through passages
        /// <summary>Review round 3 fixes, one bit each (headless A/B only; the game runs the default 0xAEFBBDF; "not adopted" = measured, off):
        /// 1 an encounter only between units of different orders whose goals oppose (more than NarrowOppose deg, as for passages - never the
        /// squads of one group order going the same way); 2 no head-on smoothing (MateQueue, resume hysteresis, hand-over-ahead) for a unit on a
        /// bridge / water / gate zone tile or its band, whatever else zoned it; 4 no pace cap in a zone (the live DV24 step as is); 8 a
        /// <= 2-tile passage (robot grid, PassTileFor) is live for the units that go through it one way: within NarrowSense steps of a passage
        /// with its own way entering one within PassLook px (to the end of the order when its goal lies within PassGoalReach steps of a
        /// passage, else until PassExit steps past it); 16 a squad goes through as one (every member live while one is); 32 (not adopted) the
        /// smoothing only with opposing traffic within OpposedReach; 64 the hand-back rules (strict file order, a stalled follower's own path)
        /// for every squad in a tight place (HandbackTight); 128 DV47's clock with no soft blocker near hands the unit and its squad to the
        /// live rules until it is PassExit steps past the passage and 2 tiles on; 256 a group order whose members' shortest ways go through a
        /// passage gets the live spots and rules (GroupThroughPassage); 512 the hand-over on a bridge / water / gate zone tile or its band is
        /// Zod's path (live), not the leader's way; 1024 (not adopted) a one-way squeeze keeps the smoothing; 2048 no one-way live term with
        /// opposing traffic near (OpposedNear: the head-on rules decide); 4096 an order with DV44's group spots does not keep the live term to
        /// its end; 8192 the smoothing only with a head-on reason (an active passage / a squeeze / an encounter, its own or its squad's, held
        /// ZonedHold s: headLast); 16384 / 1048576 (not adopted) idle units / orders ending next to a passage live; 32768 a squeeze passage keeps
        /// its round-2 rule for squads (no one-way live term there); 65536 the hand-over at its order's start (leader's trail < HandoverTrailMin
        /// px, no head-on reason) is Zod's path; 131072 a group order with DV44's spots gets no one-way live term; 262144 HandbackTight without
        /// 1-tile squeezes and DV44's own short lanes; 524288 the DV47 live hand-off only at a passage, a zone or a lane (HandbackTight);
        /// 2097152 + 4194304 DV44's one-tile lanes at a <= 2-tile passage (not a squeeze) leave the layer's list while only zoned units
        /// without a head-on reason are at it (OneWayLiveAt, TestLaneAnyLive); 8388608 a squad goes onto a bridge / water / gate zone as one
        /// (its leader's way within StaticSquadLook px); 16777216 (not adopted) the one-way live term decided at the order (OrderLiveStart);
        /// 33554432 a group order through a passage keeps the live rules in tight places only (not on the whole order); 67108864 (not
        /// adopted) the one-way term spread only to the squad members in a tight place; 134217728 a unit on Zod's factory / repair exit
        /// FORCE_MOVE runs the live rules (its ghost pass; DV44's queue / side-steps jammed a squad leader at the exit for 30 s and its
        /// minion then took the cloned, unstoppable FORCE_MOVE back through the factory: bot match L02 seed 3).</summary>
        internal static int TestR4 = 0xAEFBBDF;
        /// <summary>Review round 3: the one-way live term applies only in tight places too (open ground: DV44 - the open-tile counts).</summary>
        internal static bool PassLiveConfined = false;
        internal static bool R4(int bit) => (TestR4 & bit) != 0;
        /// <summary>Review round 3: how far ahead on its own way (px) a passage makes a unit live; the steps past it after which that ends; the
        /// steps from a passage within which an order's goal keeps the live rules to the end of the order.</summary>
        public static double PassLook = 160, StaticSquadLook = 64; public static int PassExit = 2, PassGoalReach = 6; internal static int IdleLiveSteps = 6;
        /// <summary>Checks: DV47 live hand-offs (review round 3).</summary>
        public int StuckLives { get; private set; }

        /// <summary>Tile k is a bottleneck passage tile of walkable width <= 2 tiles (the robot grid's passages;
        /// a 3-tile vehicle corridor is no bottleneck for this rule). (A/B PassVehGrid: a vehicle asks its own grid.)</summary>
        bool PassTileFor(ZObject u, int k) => k >= 0 && nComp[k] >= 0 && ((PassVehGrid ? ZM(u) : zR)[k] & ZNarrowBit) != 0 && !(R4(32768) && nSqueeze[nComp[k]] && SqueezeFor(u));   // (a squeeze keeps its round-2 rule for squads: R4 32768)
        internal static bool PassVehGrid = false;
        /// <summary>The point (px) lies within `steps` walking steps of a passage (robot grid BFS, at most max(NarrowSense, NarrowReach + 2)).</summary>
        bool PassageNear(int px, int py, int steps) { int t = ZIdx(px >> 4, py >> 4); return t >= 0 && nNear[t] >= 0 && nDist[t] <= steps; }

        /// <summary>Review round 3 (R4 8): the unit is live through a passage while it stands on one of its grid's passage tiles or its own way
        /// enters one within PassLook px; afterwards to the end of that order when the order's goal lies within PassGoalReach steps of a
        /// passage (it arrives the live way), else until it is PassExit steps past it (+ ZoneExitHold s).</summary>
        void PassLiveStep(ZObject u, ZCommitState c, int t, int nc, double now)
        {
            bool on = PassTileFor(u, t), ahead = false;
            var ow = OrderOf(u);
            bool ordered = ow.mode == WpMode.Move || ow.mode == WpMode.Attack || ow.mode == WpMode.ForceMove;
            if (R4(131072) && ordered && ow.Equals(c.dvSpotWp) && !c.passLive) return;   // (a group order with DV44's spots stays DV44's: one rule set per group order)
            if (!on && ordered && nc >= 0 && u.Waypoints.Count > 0 && !HelperMove(u.Waypoints[0]) && !(u.IsMinion && c.noPath))   // (within NarrowSense steps of a passage only)
                ahead = AheadHits(u, PassLook, k => PassTileFor(u, k));
            if (!on && !ordered && R4(16384) && nc >= 0 && nDist[t] <= IdleLiveSteps && !c.passLive && ow.mode == WpMode.None) on = true;   // (A/B: an idle unit next to a passage stands the live way)
            bool goalNear = ordered && R4(1048576) && !ow.Equals(c.dvSpotWp) && PassageNear(ow.x, ow.y, IdleLiveSteps) && nc >= 0 && nDist[t] <= IdleLiveSteps;
            if (!on && goalNear && !c.passLive) on = true;                              // (A/B: an order from next to a passage to a point next to it: live)
            if ((on || ahead) && !c.passLive && R4(2048) && OpposedNear(u))
            { on = ahead = false; }                                                      // (opposing traffic near: the head-on rules decide, not the one-way live term)
            if (on || ahead)
            {
                if (!c.passLive) Log(u, on ? "v3: on a passage: live rules" : "v3: its way goes through a passage: live rules");
                c.passLive = true; c.passLiveIn = now; c.passLiveAtOrder = ZWaypoint.Cleared;
                c.passLiveWp = ordered && PassageNear(ow.x, ow.y, PassGoalReach) && !(R4(4096) && ow.Equals(c.dvSpotWp)) ? ow : ZWaypoint.Cleared;   // (not a group order with DV44's spots: R4 4096)
                return;
            }
            if (!c.passLive) return;
            if (ordered && ow.Equals(c.passLiveWp) && !c.passLiveWp.Equals(ZWaypoint.Cleared)) { c.passLiveIn = now; return; }   // (the order ends near the passage: live to its end)
            if (ow.mode == WpMode.Enter && (ow.Equals(c.passLiveWp) || c.passLiveWp.mode == WpMode.Move || c.passLiveWp.mode == WpMode.Attack))
            { c.passLiveWp = ow; c.passLiveIn = now; return; }                           // (it boards a vehicle right after such an order: that walk too)
            if (nc >= 0 && nDist[t] <= PassExit) { c.passLiveIn = now; return; }           // (not yet PassExit steps past it)
            if (now - c.passLiveIn < ZoneExitHold) return;
            c.passLive = false; c.passLiveWp = ZWaypoint.Cleared;
            Log(u, "v3: past the passage: DV44");
        }

        /// <summary>Review round 3 (R4 2048): a unit of another squad / group with an order whose goal opposes u's (GoalsOppose) is within
        /// OpposedReach px of u (its squad's leader for a minion): the passage is not one-way traffic - the one-way live term does not start.</summary>
        bool OpposedNear(ZObject u)
        {
            Near(u.CenterX, u.CenterY, OpposedReach, oppList);
            foreach (var v in oppList)
            {
                if (v == u || !v.IsMobile || Static(v) || SameSquad(u, v) || v.Waypoints.Count == 0) continue;
                double dx = v.CenterX - u.CenterX, dy = v.CenterY - u.CenterY;
                if (dx * dx + dy * dy > (double)OpposedReach * OpposedReach) continue;
                if (GoalsOppose(u, v)) return true;
            }
            return false;
        }
        internal static int OpposedReach = 256;
        readonly List<ZObject> oppList = new List<ZObject>();

        /// <summary>Review round 3 (R4 128): DV47's clock fired for u in a tight place with no soft blocker near - u's squad (its leader and every
        /// minion) runs the live rules until it is PassExit steps past the passage and 2 tiles on from where it stood, its order changes, or
        /// StuckLiveMax s.</summary>
        void StuckLiveStart(ZObject u)
        {
            var l = u.IsMinion && u.Leader != null && !u.Leader.Destroyed ? u.Leader : u;
            bool any = false;
            void Set(ZObject o)
            {
                var c = o.PC; if (c.stuckLive) return;
                c.stuckLive = true; c.stuckLiveAt = W.Time; c.stuckLiveX = o.CenterX; c.stuckLiveY = o.CenterY; c.stuckLiveWp = OrderOf(o); any = true;
                Log(o, "DV47 (v3): stuck in a tight place, no soft blocker near: the live rules until past it");
            }
            Set(l); foreach (var m in l.Minions) if (m != null && !m.Destroyed && m.KillAt < 0) Set(m);
            if (u != l) Set(u);
            if (any) StuckLives++;
        }
        void StuckLiveStep(ZObject u, ZCommitState c, int t, int nc, double now)
        {
            if (!c.stuckLive) return;
            double dx = u.CenterX - c.stuckLiveX, dy = u.CenterY - c.stuckLiveY;
            bool past = dx * dx + dy * dy >= 32 * 32 && (nc < 0 || nDist[t] > PassExit) && !PassTileFor(u, t);
            bool gone = !OrderOf(u).Equals(c.stuckLiveWp);
            if (!past && !gone && now - c.stuckLiveAt < StuckLiveMax) return;
            c.stuckLive = false; c.stuckLiveWp = ZWaypoint.Cleared;
            Log(u, $"DV47 (v3): live hand-off ends ({(gone ? "order" : past ? "past it" : "time")})");
        }
        public const double StuckLiveMax = 10;

        /// <summary>Review round 3 (R4 1): u and v have orders whose goals lie more than NarrowOppose deg apart (seen from each unit).</summary>
        bool GoalsOppose(ZObject u, ZObject v)
        {
            if (!GoalDir(u, out double ux, out double uy) || !GoalDir(v, out double vx, out double vy)) return false;
            return ux * vx + uy * vy < Math.Cos(NarrowOppose * Math.PI / 180);
        }
        static bool GoalDir(ZObject u, out double dx, out double dy)
        {
            dx = dy = 0;
            int oi = OrderIndex(u); if (oi < 0) return false;
            var wp = u.Waypoints[oi];
            if (wp.mode != WpMode.Move && wp.mode != WpMode.Attack && wp.mode != WpMode.ForceMove) return false;
            int gx = wp.x, gy = wp.y;
            if (oi == 0 && !(u.IsMinion && u.PC.noPath) && u.PathPlannedFor(wp)) { var (fx, fy) = u.PathFinalPoint; gx = fx; gy = fy; }   // (its path's end once planned, else the order's point)
            double ex = gx - u.CenterX, ey = gy - u.CenterY, l = Math.Sqrt(ex * ex + ey * ey);
            if (l < 8) return false;
            dx = ex / l; dy = ey / l; return true;
        }
        /// <summary>Review round 3 (R4 2): u stands on a bridge / water crossing / fort gate zone tile of its own grid or in its band.</summary>
        internal bool StaticBandAt(ZObject u) { if (zR == null) return false; int t = ZIdx(u.CenterX >> 4, u.CenterY >> 4); return t >= 0 && (ZM(u)[t] & (ZInner | ZOuter)) != 0; }
        /// <summary>(A/B R4 1024) the nearest passage to u is a squeeze within its band.</summary>
        bool SqueezeNear(ZObject u) { if (zR == null) return false; int t = ZIdx(u.CenterX >> 4, u.CenterY >> 4); return t >= 0 && nNear[t] >= 0 && nSqueeze[nNear[t]] && nDist[t] <= NarrowReach + ZoneExitGrow; }
        /// <summary>Review round 3 (R4 256): at least half of a group's members (squads by their leader) have a shortest way (8 directions, no
        /// corner cutting, on their own grid, a walk from the clicked point of at most GroupPassSteps steps) to the clicked point that goes
        /// through a passage of walkable width <= 2 tiles - the group goes through a bottleneck: the live spots and rules for the order.</summary>
        bool GroupThroughPassage(List<ZObject> group, int px, int py)
        {
            EnsureZoneMap();
            int n = 0, cross = 0;
            for (int g = 0; g < 2; g++)
            {
                bool robot = g == 0, any = false;
                foreach (var u in group) if (!u.IsMinion && u.IsRobot == robot) any = true;
                if (!any) continue;
                var (gx, gy) = ZReach.FootTileAt(px, py, robot);
                GpBfs(robot, gx, gy);
                foreach (var u in group)
                {
                    if (u.IsMinion || u.IsRobot != robot) continue;
                    var (sx, sy) = robot ? (u.CenterX >> 4, u.CenterY >> 4) : ZReach.FootTile(u.LocX, u.LocY);
                    if (!GpOk(sx, sy)) continue;
                    n++;
                    if (R4(2048) && OpposedNear(u)) return false;                       // (opposing traffic near: the head-on rules decide)
                    if (GpCrosses(robot, sx, sy)) cross++;
                }
            }
            return n > 0 && cross * 2 >= n;
        }
        public const int GroupPassSteps = 96;
        int[] gpDist, gpSeen; int gpStamp; readonly List<int> gpQ = new List<int>();
        bool GpOk(int x, int y) => x >= 0 && y >= 0 && x < zw && y < zh && gpSeen[y * zw + x] == gpStamp;
        void GpBfs(bool robot, int gx, int gy)
        {
            var pf = W.Path; int n = zw * zh;
            if (gpDist == null || gpDist.Length != n) { gpDist = new int[n]; gpSeen = new int[n]; }
            gpStamp++; gpQ.Clear();
            bool F(int x, int y) => x >= 0 && y >= 0 && x + (robot ? 0 : 1) < zw && y + (robot ? 0 : 1) < zh && pf.TilePassableGrid(x, y, robot, false)
                                    && (robot || (pf.TilePassableGrid(x + 1, y, false, false) && pf.TilePassableGrid(x, y + 1, false, false) && pf.TilePassableGrid(x + 1, y + 1, false, false)));
            if (!F(gx, gy)) return;
            gpSeen[gy * zw + gx] = gpStamp; gpDist[gy * zw + gx] = 0; gpQ.Add(gy * zw + gx);
            for (int qi = 0; qi < gpQ.Count; qi++)
            {
                int k = gpQ[qi], cx = k % zw, cy = k / zw;
                if (gpDist[k] >= GroupPassSteps) continue;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = cx + dx, ny = cy + dy;
                        if (!F(nx, ny) || (dx != 0 && dy != 0 && (!F(cx + dx, cy) || !F(cx, cy + dy)))) continue;
                        int nk = ny * zw + nx; if (gpSeen[nk] == gpStamp) continue;
                        gpSeen[nk] = gpStamp; gpDist[nk] = gpDist[k] + 1; gpQ.Add(nk);
                    }
            }
        }
        /// <summary>Down the BFS distance from (sx, sy) to the clicked point (the lowest neighbour, lowest index first): a passage tile on the
        /// way (a vehicle: any tile of its 2x2 footprint).</summary>
        bool GpCrosses(bool robot, int sx, int sy, int maxSteps = GroupPassSteps + 1)
        {
            int x = sx, y = sy;
            for (int guard = 0; guard <= maxSteps; guard++)
            {
                int k = y * zw + x;
                if (PassR(k) || (!robot && (PassR(k + 1) || PassR(k + zw) || PassR(k + zw + 1)))) return true;
                if (gpDist[k] == 0) return false;
                int bx = -1, by = -1, bd = gpDist[k];
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if ((dx == 0 && dy == 0) || !GpOk(nx, ny)) continue;
                        if (dx != 0 && dy != 0 && (!GpOk(x + dx, y) || !GpOk(x, y + dy))) continue;
                        int d = gpDist[ny * zw + nx]; if (d < bd) { bd = d; bx = nx; by = ny; }
                    }
                if (bx < 0) return false;
                x = bx; y = by;
            }
            return false;
        }
        bool PassR(int k) => k >= 0 && k < zR.Length && nComp[k] >= 0 && (zR[k] & ZNarrowBit) != 0;

        // ------------------------------------------------------------------ review round 2 (issue 3): DV44's short lanes step aside where the live rules run
        List<ZLane> laneMaster = new List<ZLane>(); bool[] laneHidden = new bool[0]; int laneMasterBuild = -1;
        bool[] laneAtPass = new bool[0]; int laneAtPassBuild = int.MinValue;
        /// <summary>Review round 3 (R4 2097152): a DV44 one-tile-long lane whose corridor or keep-clear area holds or touches (1 tile) a passage
        /// tile of walkable width <= 2 (robot grid): the live layer has no lane there, and the units going through run the live rules - the lane
        /// is not in the layer's list (for everybody, for good: static, no switching).</summary>
        /// <summary>Review round 3 (R4 4194304): a unit with the one-way live term (zwhy 128) is within 3 tiles of the lane's keep-clear area and
        /// no unit outside the zones (DV44's rules) stands in it + 1 tile: the passage carries one-way live traffic now.</summary>
        internal static bool TestLaneAnyLive = true;
        bool OneWayLiveAt(ZLane L)
        {
            bool live = false;
            foreach (var u in units)
            {
                if (!u.IsMobile) continue;
                int x = u.CenterX, y = u.CenterY;
                if (x >= L.KX0 - 48 && x < L.KX1 + 48 && y >= L.KY0 - 48 && y < L.KY1 + 48)
                {
                    bool inK = x >= L.KX0 - 16 && x < L.KX1 + 16 && y >= L.KY0 - 16 && y < L.KY1 + 16;
                    if (TestLaneAnyLive)
                    {                                                                    // (A/B: any zoned unit without a head-on reason; one with one keeps the lane)
                        if (u.PC.zoned && W.Time - u.PC.headLast > ZonedHold + 1e-6) live = true;
                        else if (u.PC.zoned) return false;
                        else if (inK) return false;
                    }
                    else if (u.PC.zoned && (u.PC.zwhy & 128) != 0) live = true;
                    else if (inK && !u.PC.zoned) return false;
                }
            }
            return live;
        }
        bool ShortLaneAtPassage(ZLane L)
        {
            int x0 = Math.Min(L.X0, L.KX0) >> 4, y0 = Math.Min(L.Y0, L.KY0) >> 4, x1 = (Math.Max(L.X1, L.KX1) - 1) >> 4, y1 = (Math.Max(L.Y1, L.KY1) - 1) >> 4;
            for (int j = Math.Max(0, y0 - 1); j <= Math.Min(zh - 1, y1 + 1); j++)
                for (int i = Math.Max(0, x0 - 1); i <= Math.Min(zw - 1, x1 + 1); i++) { int k = j * zw + i; if (PassR(k) && !nSqueeze[nComp[k]]) return true; }   // (not at a squeeze: its round-2 rule)
            return false;
        }
        /// <summary>The lanes as built (EnsureLanes), short ones included: the zone map is built from these.</summary>
        List<ZLane> LanesAll => laneMasterBuild == lanesBuilds ? laneMaster : Lanes;
        /// <summary>BeginTick: (hide = false) a lane rebuild refreshes the master list; (hide = true, after the passages' activation) a DV44 short
        /// lane (a one-tile-long chokepoint, ZCommit.Lanes.cs) whose corridor lies in a static zone or an ACTIVE bottleneck passage is taken out of
        /// the layer's lane list while that lasts - for everybody there: the live layer (its lanes, keep-clear areas, crowd arrival, make-way)
        /// runs as live, one rule set per encounter (measured: the extra lane's keep-clear area made the zoned units' live gate stutter, L17
        /// head-on 536 stop-go with every unit zoned against live 318). Back in the list (its waits reset) when the passage is quiet again.
        /// Deterministic: the master list keeps the build order.</summary>
        void SyncLanes(bool hide)
        {
            if (laneMasterBuild != lanesBuilds)
            {
                laneMaster = new List<ZLane>(Lanes); laneHidden = new bool[laneMaster.Count]; laneMasterBuild = lanesBuilds;
            }
            if (!hide || !(R2(256) || R4(2097152))) return;
            bool changed = false;
            if (R4(2097152) && laneAtPassBuild != laneMasterBuild + zVersion * 7919)
            {                                                                            // review round 3: which DV44 short lanes lie at a <= 2-tile passage (static per build)
                laneAtPassBuild = laneMasterBuild + zVersion * 7919; laneAtPass = new bool[laneMaster.Count];
                for (int i = 0; i < laneMaster.Count; i++) laneAtPass[i] = laneMaster[i].Short && ShortLaneAtPassage(laneMaster[i]);
            }
            for (int i = 0; i < laneMaster.Count; i++)
            {
                var L = laneMaster[i];
                bool h = L.Short && ((R2(256) && LaneLive(L)) || (R4(2097152) && laneAtPass[i] && (!R4(4194304) || OneWayLiveAt(L))));   // (review round 3: no DV44 short lane at a passage while one-way live traffic uses it - the live layer has none there)
                if (h == laneHidden[i]) continue;
                laneHidden[i] = h; changed = true;
                if (!h) { L.waitSince[0] = L.waitSince[1] = -1; L.waited[0] = L.waited[1] = false; }
            }
            if (!changed) return;
            Lanes.Clear();
            for (int i = 0; i < laneMaster.Count; i++) if (!laneHidden[i]) Lanes.Add(laneMaster[i]);
        }
        /// <summary>ZFormation.FormationStep (v3, review round 1 + 2): the live DV24 step of a zoned minion is capped at its own Zod speed in water
        /// and on any slow tile (DV24's catch-up never applies there, as for DV44's follow); on other ground it is the live step exactly
        /// (round 1 capped it everywhere: the zoned minions then walked unlike live in the head-on passages, more reversals).</summary>
        internal bool PaceCapped(ZObject m) => TestPaceCap && (!ZonesOn || (!(R4(4) && Zoned(m)) && (!R2(512) || SlowGround(m))));   // (DV44 everywhere, A/B: round 1's cap; review round 3: never in a zone, the live DV24 step as is)
        /// <summary>(A/B, headless only) the cap at all (default on; TestR2 bit 512: only on slow ground).</summary>
        internal static bool TestPaceCap = true;

        /// <summary>Squad propagation of an active passage / encounter (review round 2, A/B TestProp): 0 the whole squad (round 1), 1 nobody (each
        /// member by its own state), 2 the members within the passage's band (NarrowReach + ZoneExitGrow steps) or in a tight place.</summary>
        internal static int TestProp = 0;
        bool PropNear(ZObject m)
        {
            int t = ZIdx(m.CenterX >> 4, m.CenterY >> 4);
            if (t < 0) return false;
            if (nNear[t] >= 0 && nDist[t] <= NarrowReach + ZoneExitGrow) return true;
            return TightAt(m.CenterX, m.CenterY) && m.PC.encOn;
        }

        /// <summary>One rule set per encounter: in a tight place a unit within EncStart px of a zoned unit of another squad / group is zoned
        /// too, both until EncEnd px apart for EncHold s (decided from the state at the start of the tick: order-independent).</summary>
        void Encounters(double now)
        {
            foreach (var u in units) { u.PC.encWas = u.PC.encOn; u.PC.encHit = false; }
            foreach (var u in units)
            {
                if (!u.IsMobile || Static(u) || !TightAt(u.CenterX, u.CenterY)) continue;
                Near(u.CenterX, u.CenterY, (int)EncEnd, encNear);
                foreach (var v in encNear)
                {
                    if (v == u || !v.IsMobile || Static(v) || SameSquad(u, v)) continue;
                    double dx = v.CenterX - u.CenterX, dy = v.CenterY - u.CenterY, d2 = dx * dx + dy * dy;
                    bool start = d2 <= EncStart * EncStart && (u.PC.zoneOwn || v.PC.zoneOwn);
                    bool keep = d2 <= EncEnd * EncEnd && u.PC.encWas && v.PC.encWas;
                    if ((start || keep) && R4(1) && !GoalsOppose(u, v)) continue;           // review round 3: only units of different orders whose goals oppose (not the squads of one group order)
                    if ((start || keep) && R3(16) && !TightAt(v.CenterX, v.CenterY)) continue;   // review round 2: both in a tight place (open ground: DV44)
                    if (start || keep) { u.PC.encHit = true; v.PC.encHit = true; }
                }
            }
            foreach (var u in units)
            {
                var c = u.PC;
                if (c.encHit) { c.encOn = true; c.encSeen = now; }
                else if (c.encOn && now - c.encSeen >= EncHold) c.encOn = false;
            }
        }

        /// <summary>The unit's own way ahead (its current target, then its path points) enters its grid's zone mask within `look` px.</summary>
        bool RouteEntersZone(ZObject u, byte[] zm, double look, double now) => !HelperMove(u.Waypoints[0]) && RouteHits(u, look, t => (zm[t] & ZInner) != 0 || (nComp[t] >= 0 && nActive[nComp[t]] >= now));   // (a helper move - step aside, back off - never switches by its way ahead)

        /// <summary>Review round 2: the unit's way ahead for the look-ahead switch - its path once that is planned for its current waypoint; while
        /// it is not (a new order, its path request still on the way) nothing: its own tile decides (the old order's path is not its way).</summary>
        bool AheadHits(ZObject u, double look, Func<int, bool> hit)
        {
            var wp = u.Waypoints[0];
            if (wp.mode != WpMode.Move && wp.mode != WpMode.Attack && wp.mode != WpMode.ForceMove) return false;
            if (u.PathPlannedFor(wp) || !R2(4)) return RouteHits(u, look, hit);
            return false;                                                                // (no way ahead known yet: its own tile decides; the next tick its path)
        }

        /// <summary>Review round 2: a squad (a robot leader with minions, or a minion) treats a squeeze passage as a zone even with one-way
        /// traffic (SqueezeFor; a vehicle / single robot keeps DV44 there).</summary>
        static bool SqueezeFor(ZObject u) => R2(128) && u.IsRobot && (u.IsMinion || u.Minions.Count > 0);
        /// <summary>Review round 2 fixes, one bit each (headless A/B only; the game runs the default): 1 the live-order term's exit hold, 2 a new
        /// order re-decides the zone state, 4 no look-ahead on an old path while the new one is not planned yet, 8 the file's strict order,
        /// 16 a stalled follower takes its own path, 32 the re-join guard, 64 a minion left behind runs its DV47 clock, 128 squeeze passages,
        /// 256 DV44's short lanes hidden where the live rules run (measured, not adopted: off by default), 512 the zoned minions' pace cap only
        /// on slow ground, 1024 the resume hysteresis (its scope: TestResumeScope).</summary>
        internal static int TestR2 = 0x6FF;
        internal static bool R2(int bit) => (TestR2 & bit) != 0;
        /// <summary>Review round 2, second pass (headless A/B only): 2 the re-decide waits until the new order's own way is known, 4 the live
        /// group order's zone term only in a tight place, 8 that term held LiveOrderHold s after the waypoints emptied and then over for the
        /// order, 16 an encounter only between units both in a tight place, 32 squad propagation only to the members in a tight place,
        /// 64 zoned squad mates queue behind each other (DV44's MateQueue), 128 no zone term at all on open ground (out of every tight place
        /// and off every passage tile for ZoneExitHold s), 256 the hand-over path is the leader's way (trail + his path), 1024 the zone state's
        /// hysteresis (ZonedHold) and a passage kept active by the units it zoned, 2048 the hand-back rules (R2 8, 16) for squads just out of a
        /// zone only, 4096 a passage kept active by zoned units with orders + PassHold, 8192 every passage in reach per tile (not adopted),
        /// 16384 the passages' activity over a zone-map rebuild + the ghost (not adopted), 32768 a vehicle on a live group order keeps the live
        /// rules everywhere, 65536 the hand-over never turns a minion back to its leader (HandoverAhead), 131072 every member of a live group
        /// order keeps them, 262144 the confinement's spatial band (not adopted), 524288 a squad that crossed a zone arrives the live way (not
        /// adopted), 1048576 HandoverAhead only in traffic (not adopted), 2097152 MinDwell, 4194304 the head-on smoothing not for units zoned
        /// only by their live group order, 8388608 nor for units in a static zone, 16777216 a player's ENTER order runs the live rules (DV59).
        /// </summary>
        internal static int TestR3 = 0x1E395FE;
        /// <summary>Review round 2 (R3 2097152): after a zone switch a unit does not switch back within MinDwell s while its tile is of the same
        /// kind (zone / band / tight place / passage) - a real edge crossing still switches at once (DwellAnyOrder: also with a new order).</summary>
        public static double MinDwell = 2.0;
        /// <summary>Review round 2: an edge crossing lifts MinDwell only once the unit is DwellEdgeMove px (manhattan) from where it last
        /// switched - a unit stepping to and fro over a tile edge does not flip with each step (0 = any edge crossing).</summary>
        public static int DwellEdgeMove = 8;
        /// <summary>(A/B, not adopted: L17 gapX reversals 24-28 against live 23) a unit leaves the zone state no sooner than DwellExit s after
        /// it switched, edge or not (0 = off).</summary>
        public static double DwellExit = 0;
        /// <summary>Review round 2: at most 2 zone switches within MinDwell s - a third, edge or not, waits until the first is MinDwell s old
        /// (a grunt walking along the edge of the wreck's tight place flipped 4 times in 1.8 s).</summary>
        public static bool FlipCap = true;
        /// <summary>Review round 2: MinDwell / FlipCap hold across a new order too (a bot re-issuing its orders flipped a jeep twice in
        /// 1.8 s; false = a new order switches at once).</summary>
        public static bool DwellAnyOrder = true;
        /// <summary>Checks: ticks a switch was held back by MinDwell.</summary>
        public int DwellHolds { get; private set; }
        /// <summary>Review round 2 (MinDwell): the kind of the tile under (px, py) for unit u: 1 zone, 2 band, 4 tight place, 8 passage.</summary>
        int TileCls(ZObject u, int px, int py)
        {
            int t = ZIdx(px >> 4, py >> 4); if (t < 0) return 0;
            byte m = ZM(u)[t];
            return ((m & ZInner) != 0 ? 1 : 0) | ((m & ZOuter) != 0 ? 2 : 0) | (TightFor(u, t) ? 4 : 0) | (nComp[t] >= 0 ? 8 : 0);
        }
        static ZWaypoint OrderOf(ZObject u) { int oi = OrderIndex(u); return oi >= 0 ? u.Waypoints[oi] : ZWaypoint.Cleared; }
        /// <summary>Review round 2 (R3 4096): an active passage stays active this long after the opposing traffic was last seen.</summary>
        internal static double PassHold = 2.0;
        /// <summary>Review round 2 (R3 2048): the squad (its leader or a minion) left a zone within HandbackWindow s - the hand-back rules apply.</summary>
        internal bool HandbackSquad(ZObject l)
        {
            if (!R3(2048)) return true;
            double now = W.Time;
            if (now - l.PC.zoneOffAt < HandbackWindow || l.PC.zoned) return true;
            foreach (var m in l.Minions) if (m != null && (now - m.PC.zoneOffAt < HandbackWindow || m.PC.zoned)) return true;
            if (R4(64))
            {                                                                            // review round 3: in a tight place (a zone, its band, a passage + its reach, a lane's
                if (HandbackTight(l)) return true;                                       // keep-clear area) the hand-back rules hold for every squad
                foreach (var m in l.Minions) if (m != null && HandbackTight(m)) return true;
            }
            return false;
        }
        internal static double HandbackWindow = 15;
        /// <summary>Review round 3 (R4 64): u stands in a zone or its band, within NarrowReach + ZoneExitGrow steps of a passage, or in a lane's
        /// keep-clear area (the review's tight places for the hand-back rules; not the wider DV47 tight band round every narrow run).</summary>
        bool HandbackTight(ZObject u)
        {
            if (zR == null) return false;
            int t = ZIdx(u.CenterX >> 4, u.CenterY >> 4); if (t < 0) return false;
            return (ZM(u)[t] & (ZInner | ZOuter)) != 0 || (nNear[t] >= 0 && nDist[t] <= (R4(262144) ? NarrowReach : NarrowReach + ZoneExitGrow) && !(R4(262144) && nSqueeze[nNear[t]])) || (KeepClearAt(u.CenterX, u.CenterY) is ZLane L && !(R4(262144) && L.Short));   // (R4 262144: not a 1-tile squeeze, not DV44's own one-tile lanes)
        }
        /// <summary>Review round 2 (R3 524288): a member of the squad is zoned now or left a zone within HandbackWindow s (it crossed a zone on its way:
        /// the squad arrives the live way - every minion to a spot next to its leader - not spread in DV44's V behind the passage).</summary>
        internal bool SquadZonedRecently(ZObject l)
        {
            double now = W.Time;
            if (l.PC.zoned || now - l.PC.zoneOffAt < HandbackWindow) return true;
            foreach (var m in l.Minions) if (m != null && (m.PC.zoned || now - m.PC.zoneOffAt < HandbackWindow)) return true;
            return false;
        }
        /// <summary>Review round 2 (R3 1024): a zoned unit stays zoned this long after its last reason ended (in tight places only).</summary>
        public static double ZonedHold = 1.0;
        internal static bool R3(int bit) => (TestR3 & bit) != 0;
        public const double LiveOrderHold = 2.0;
        /// <summary>Review round 2 (R3 262144): a tight tile of the unit's grid or a passage tile lies within `band` tiles (Chebyshev) of tile t.</summary>
        bool TightNear(ZObject u, int t, int band)
        {
            if (t < 0) return false;
            int tx = t % zw, ty = t / zw;
            for (int j = Math.Max(0, ty - band); j <= Math.Min(zh - 1, ty + band); j++)
                for (int i = Math.Max(0, tx - band); i <= Math.Min(zw - 1, tx + band); i++) { int k = j * zw + i; if (TightFor(u, k) || nComp[k] >= 0) return true; }
            return false;
        }
        public static int ConfineBand = 1;
        /// <summary>The unit stands in a tight place of its own grid: a zone or its band, a narrow run (+ TightGrow), a lane's keep-clear area.</summary>
        bool TightFor(ZObject u, int t) => t >= 0 && ((ZM(u)[t] & (ZInner | ZOuter)) != 0 || ztight[t]);
        /// <summary>The new order's own way ahead is known: its first waypoint is the order and its path is planned (or it follows its leader's trail).</summary>
        static bool OrderWayKnown(ZObject u, int oi) => oi < 0 || (oi == 0 && (u.PC.noPath || u.PathPlannedFor(u.Waypoints[0]) || (u.Waypoints[0].mode != WpMode.Move && u.Waypoints[0].mode != WpMode.Attack && u.Waypoints[0].mode != WpMode.ForceMove)));
        /// <summary>(A/B) a natural 1-tile passage is a squeeze too (else only passages a soft blocker narrows).</summary>
        internal static bool TestSqueezeRun1 = true;
        /// <summary>How far ahead a squad leader's way switches the squad for a squeeze passage (px): the squad goes single file on its own
        /// Zod paths before the squeeze, not in its mouth.</summary>
        internal static double SqueezeLook = 96;

        /// <summary>The unit's way ahead (its current target, then its path points), sampled every 8 px up to `look` px, hits a tile.</summary>
        bool RouteHits(ZObject u, double look, Func<int, bool> hit)
        {
            var wm = u.Waypoints[0].mode;
            if (wm != WpMode.Move && wm != WpMode.Attack && wm != WpMode.ForceMove) return false;
            double x = u.CenterX, y = u.CenterY, left = look;
            var pts = u.PathPoints;
            for (int i = -1; left > 0; i++)
            {
                double qx, qy;
                if (i < 0) { qx = u.PathTargetX; qy = u.PathTargetY; }
                else { if (pts == null || i >= pts.Count) break; qx = pts[i].x; qy = pts[i].y; }
                double dx = qx - x, dy = qy - y, len = Math.Sqrt(dx * dx + dy * dy);
                for (double s = Math.Min(8, len); s <= len && left > 0 && len > 1e-9; s += 8, left -= 8)
                {
                    int t = ZIdx((int)Math.Floor(x + dx * s / len) >> 4, (int)Math.Floor(y + dy * s / len) >> 4);
                    if (t >= 0 && hit(t)) return true;
                }
                if (len < 8) left -= Math.Max(len, 1);
                x = qx; y = qy;
            }
            return false;
        }

        /// <summary>The switch is off (or was just switched off): no unit keeps a zone state, a live group order or a soft pass.</summary>
        void ClearV3State()
        {
            if (!v3Dirty) return;
            v3Dirty = false;
            foreach (var o in W.Objects)
            {
                var c = o.PC;
                c.zoned = c.zoneOwn = c.passOwn = c.encOn = c.liveOrder = false; c.holdBy = -1; c.liveZOff = false; c.zwhy = 0;
                c.passLive = c.oppNow = c.oppSquad = c.stuckLive = false; c.stuckLiveWp = c.passLiveWp = c.passLiveAtOrder = ZWaypoint.Cleared;
                c.softPass = false; c.softGoalWp = ZWaypoint.Cleared; c.softHoldSince = -1; c.stWp = ZWaypoint.Cleared; ClockReset(c);
            }
        }

        byte[] ZM(ZObject u) => u.IsRobot ? zR : zV;
        int ZIdx(int tx, int ty) => zR == null || tx < 0 || ty < 0 || tx >= zw || ty >= zh ? -1 : ty * zw + tx;
        /// <summary>The point (px) lies in a zone of either grid (inner mask).</summary>
        public bool ZoneAt(int px, int py) { EnsureZoneMap(); int t = ZIdx(px >> 4, py >> 4); return t >= 0 && ((zR[t] | zV[t]) & ZInner) != 0; }
        /// <summary>The point (px) lies in a zone of either grid or within ZoneExitGrow tiles of one.</summary>
        public bool ZoneNear(int px, int py) { EnsureZoneMap(); int t = ZIdx(px >> 4, py >> 4); return t >= 0 && ((zR[t] | zV[t]) & (ZInner | ZOuter)) != 0; }
        /// <summary>DV47: the point (px) lies in a tight place - a zone of either grid, its band, a narrow run, a lane's keep-clear area.</summary>
        public bool TightAt(int px, int py) { EnsureZoneMap(); int t = ZIdx(px >> 4, py >> 4); return t >= 0 && (((zR[t] | zV[t]) & (ZInner | ZOuter)) != 0 || ztight[t]); }
        /// <summary>Checks / map dumps: DV47's tight-place mask at a tile.</summary>
        public bool TightTile(int tx, int ty) { EnsureZoneMap(); int t = ZIdx(tx, ty); return t >= 0 && (((zR[t] | zV[t]) & (ZInner | ZOuter)) != 0 || ztight[t]); }
        /// <summary>A zone tile (inner mask, either grid) lies within `tiles` tiles (square) of the point (px).</summary>
        public bool ZoneWithin(int px, int py, int tiles)
        {
            EnsureZoneMap(); int tx = px >> 4, ty = py >> 4;
            for (int j = Math.Max(0, ty - tiles); j <= Math.Min(zh - 1, ty + tiles); j++)
                for (int i = Math.Max(0, tx - tiles); i <= Math.Min(zw - 1, tx + tiles); i++) if (((zR[j * zw + i] | zV[j * zw + i]) & ZInner) != 0) return true;
            return false;
        }
        /// <summary>Checks / map dumps: the zone bits of a tile, robot grid (1 inner, 2 outer, 4 bridge, 8 bottleneck, 16 water, 32 bridge lane, 64 gate).</summary>
        public int ZoneBits(int tx, int ty) { EnsureZoneMap(); int t = ZIdx(tx, ty); return t >= 0 ? zR[t] : 0; }
        /// <summary>Checks / map dumps: the zone bits of a tile, vehicle grid.</summary>
        public int ZoneBitsV(int tx, int ty) { EnsureZoneMap(); int t = ZIdx(tx, ty); return t >= 0 ? zV[t] : 0; }
        /// <summary>The tile holds a soft blocker (DV47).</summary>
        public bool SoftTileAt(int tx, int ty) { EnsureZoneMap(); return tx >= 0 && ty >= 0 && tx < zw && ty < zh && softTiles.Contains(ty * zw + tx); }

        void EnsureZoneMap()
        {
            var pf = W.Path;
            if (zR != null && zVersion == pf.Version && zLanes == lanesVersion) return;     // (rebuilt with the lanes: EnsureLanes runs first in BeginTick)
            zVersion = pf.Version; zLanes = lanesVersion; ZoneBuilds++;
            double[] oldAct = null;
            if (R3(16384) && nComp != null && nActive != null && nComp.Length == pf.W * pf.H)
            {                                                                            // review round 2: the passages' activity per tile, carried over the rebuild
                oldAct = new double[nComp.Length];
                for (int k = 0; k < nComp.Length; k++) oldAct[k] = nComp[k] >= 0 ? nActive[nComp[k]] : -100;
            }
            zw = pf.W; zh = pf.H; int n = zw * zh;
            if (zR == null || zR.Length != n) { zR = new byte[n]; zV = new byte[n]; ztight = new bool[n]; }
            else { Array.Clear(zR, 0, n); Array.Clear(zV, 0, n); Array.Clear(ztight, 0, n); }
            var inR = new bool[n]; var inV = new bool[n];
            bool TP(int x, int y, bool robot) => x >= 0 && y >= 0 && x < zw && y < zh && pf.TilePassableGrid(x, y, robot, false);
            void Mark(bool[] inner, byte[] zm, int x, int y, byte bit) { if (x < 0 || y < 0 || x >= zw || y >= zh) return; int k = y * zw + x; inner[k] = true; zm[k] |= bit; }
            void MarkBoth(int x, int y, byte bit) { Mark(inR, zR, x, y, bit); Mark(inV, zV, x, y, bit); }
            // DV47's tight places (as in round 1): every free run across of at most NarrowRun tiles (robot grid; a soft blocker counts as free:
            // a passage debris closes is still a passage) and one-tile diagonal squeezes, grown; every lane's keep-clear area + 1 (where units queue)
            BuildSoftTiles();
            bool TPt(int x, int y) => TP(x, y, true) || (x >= 0 && y >= 0 && x < zw && y < zh && softTiles.Contains(y * zw + x));
            {
                var narrow = new bool[n];
                for (int y = 0; y < zh; y++)
                    for (int x = 0; x < zw;)
                    {
                        if (!TPt(x, y)) { x++; continue; }
                        int s = x; while (x < zw && TPt(x, y)) x++;
                        if (x - s <= NarrowRun) for (int i = s; i < x; i++) narrow[y * zw + i] = true;
                    }
                for (int x = 0; x < zw; x++)
                    for (int y = 0; y < zh;)
                    {
                        if (!TPt(x, y)) { y++; continue; }
                        int s = y; while (y < zh && TPt(x, y)) y++;
                        if (y - s <= NarrowRun) for (int j = s; j < y; j++) narrow[j * zw + x] = true;
                    }
                for (int y = 0; y < zh; y++)
                    for (int x = 0; x < zw; x++)
                        if (TPt(x, y) && ((!TPt(x - 1, y - 1) && !TPt(x + 1, y + 1)) || (!TPt(x + 1, y - 1) && !TPt(x - 1, y + 1)))) narrow[y * zw + x] = true;
                int tg = TightGrow;
                for (int y = 0; y < zh; y++)
                    for (int x = 0; x < zw; x++)
                        if (narrow[y * zw + x])
                            for (int j = Math.Max(0, y - tg); j <= Math.Min(zh - 1, y + tg); j++) for (int i = Math.Max(0, x - tg); i <= Math.Min(zw - 1, x + tg); i++) ztight[j * zw + i] = true;
                foreach (var L in LanesAll)
                    for (int j = Math.Max(0, (L.KY0 >> 4) - 1); j <= Math.Min(zh - 1, ((L.KY1 - 1) >> 4) + 1); j++)
                        for (int i = Math.Max(0, (L.KX0 >> 4) - 1); i <= Math.Min(zw - 1, ((L.KX1 - 1) >> 4) + 1); i++) ztight[j * zw + i] = true;
            }
            // bottlenecks: real passages at most NarrowRun across on each grid (robots: tiles; vehicles: 2x2 footprint positions)
            var run1 = new bool[n];
            if ((TestZoneKinds & 4) != 0)
            {
                var pr = Passages(true, run1); var pv = Passages(false, null);
                var pass = new bool[n];
                for (int y = 0; y < zh; y++)
                    for (int x = 0; x < zw; x++)
                    {
                        if (pr[y * zw + x]) for (int j = y - NarrowGrow; j <= y + NarrowGrow; j++) for (int i = x - NarrowGrow; i <= x + NarrowGrow; i++)
                        { if (TestNarrowStatic) Mark(inR, zR, i, j, ZNarrowBit); else if (i >= 0 && j >= 0 && i < zw && j < zh) { zR[j * zw + i] |= ZNarrowBit; pass[j * zw + i] = true; } }
                        if (pv[y * zw + x]) for (int j = y - NarrowGrow; j <= y + 1 + NarrowGrow; j++) for (int i = x - NarrowGrow; i <= x + 1 + NarrowGrow; i++)   // (the footprint's 4 tiles: a vehicle's centre tile is its lower-right one)
                        { if (TestNarrowStatic) Mark(inV, zV, i, j, ZNarrowBit); else if (i >= 0 && j >= 0 && i < zw && j < zh) { zV[j * zw + i] |= ZNarrowBit; pass[j * zw + i] = true; } }
                    }
                BuildPassages(pass);
            }
            else BuildPassages(new bool[n]);
            if (oldAct != null)
            {                                                                            // (old active passage tiles that are no passage now: the ghost)
                ghostTiles.Clear();
                for (int k = 0; k < n; k++) if (oldAct[k] >= W.Time && nComp[k] < 0) ghostTiles.Add(k);
                ghostUntil = 0; for (int k = 0; k < n; k++) if (oldAct[k] >= W.Time && nComp[k] < 0 && oldAct[k] > ghostUntil) ghostUntil = oldAct[k];
                if (ghostTiles.Count == 0) ghostUntil = -100;
            }
            if (oldAct != null)
                for (int k = 0; k < n; k++)
                {                                                                        // (a new passage tile inherits the activity of the old passage tiles on or next to it)
                    if (nComp[k] < 0) continue;
                    int kx = k % zw, ky = k / zw;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = kx + dx, y = ky + dy; if (x < 0 || y < 0 || x >= zw || y >= zh) continue;
                        double a = oldAct[y * zw + x]; if (a > nActive[nComp[k]]) nActive[nComp[k]] = a;
                    }
                }
            // review round 2: squeeze passages - a robot passage whose free run is 1 tile, or one a soft blocker narrows (a soft tile next to
            // it): a zone for squads even with one-way traffic (each minion its own Zod path and the live single file through it)
            nSqueeze = new bool[nComps];
            for (int k = 0; k < n; k++)
            {
                if (nComp[k] < 0) continue;
                if (run1[k] && TestSqueezeRun1) { nSqueeze[nComp[k]] = true; continue; }
                int kx = k % zw, ky = k / zw;
                for (int dy = -1; dy <= 1 && !nSqueeze[nComp[k]]; dy++)
                    for (int dx = -1; dx <= 1; dx++) { int x = kx + dx, y = ky + dy; if (x >= 0 && y >= 0 && x < zw && y < zh && softTiles.Contains(y * zw + x)) { nSqueeze[nComp[k]] = true; break; } }
            }
            SqueezeCount = 0; foreach (bool b in nSqueeze) if (b) SqueezeCount++;
            // water crossings (robots wade): narrow water runs between dry land + shore
            var map = W.Map;
            if ((TestZoneKinds & 8) != 0)
            {
                bool Wat(int x, int y) => x >= 0 && y >= 0 && x < zw && y < zh && map.ClassAt(x, y) == ZClass.Water && pf.TilePassable(x, y, true);
                bool Dry(int x, int y) => x >= 0 && y >= 0 && x < zw && y < zh && map.ClassAt(x, y) != ZClass.Water && pf.TilePassable(x, y, true);
                var crow = new bool[n]; var ccol = new bool[n]; var cross = new bool[n];
                for (int y = 0; y < zh; y++)
                    for (int x = 0; x < zw;)
                    {
                        if (!Wat(x, y)) { x++; continue; }
                        int s = x; while (x < zw && Wat(x, y)) x++;
                        if (TestWaterAll || (x - s <= WaterCrossRun && Dry(s - 1, y) && Dry(x, y))) for (int i = s; i < x; i++) crow[y * zw + i] = true;
                    }
                for (int x = 0; x < zw; x++)
                    for (int y = 0; y < zh;)
                    {
                        if (!Wat(x, y)) { y++; continue; }
                        int s = y; while (y < zh && Wat(x, y)) y++;
                        if (TestWaterAll || (y - s <= WaterCrossRun && Dry(x, s - 1) && Dry(x, y))) for (int j = s; j < y; j++) ccol[j * zw + x] = true;
                    }
                bool R(bool[] a, int x, int y) => x >= 0 && y >= 0 && x < zw && y < zh && a[y * zw + x];
                for (int y = 0; y < zh; y++)                                                 // a channel: the short run goes on in the lines either side
                    for (int x = 0; x < zw; x++)                                             // (a 1-2 line bay in a basin's shore is no crossing)
                        cross[y * zw + x] = TestWaterAll ? crow[y * zw + x] : (R(crow, x, y) && R(crow, x, y - 1) && R(crow, x, y + 1)) || (R(ccol, x, y) && R(ccol, x - 1, y) && R(ccol, x + 1, y));
                for (int y = 0; y < zh; y++)
                    for (int x = 0; x < zw; x++)
                        if (cross[y * zw + x]) for (int j = y - WaterShore; j <= y + WaterShore; j++) for (int i = x - WaterShore; i <= x + WaterShore; i++) Mark(inR, zR, i, j, ZWaterBit);
            }
            // bridges (intact or destroyed): rect + BridgeEnd tiles past each end, 1 tile either side; the deck lane's keep-clear area
            if ((TestZoneKinds & 1) != 0)
            {
                foreach (var o in W.Objects)
                {
                    if (!o.IsBridge) continue;
                    int tx0 = o.LocX >> 4, ty0 = o.LocY >> 4, tx1 = (o.LocX + o.WidthPix - 1) >> 4, ty1 = (o.LocY + o.HeightPix - 1) >> 4;
                    bool vert = o.Type == "bridge_vert";
                    int ex = vert ? 1 : BridgeEnd, ey = vert ? BridgeEnd : 1;
                    for (int j = ty0 - ey; j <= ty1 + ey; j++) for (int i = tx0 - ex; i <= tx1 + ex; i++) MarkBoth(i, j, ZBridgeBit);
                }
                if (TestZoneRules[2])
                    foreach (var L in LanesAll)
                    {
                        if (!L.Bridge) continue;
                        for (int j = L.KY0 >> 4; j <= (L.KY1 - 1) >> 4; j++) for (int i = L.KX0 >> 4; i <= (L.KX1 - 1) >> 4; i++) MarkBoth(i, j, ZLaneBit);
                    }
            }
            // fort gates: the gate route through each fort (kept open by DV22's mesh table) + GateGrow tiles
            var cl = W.Clearance;
            if (cl != null && (TestZoneKinds & 16) != 0)
                for (int y = 0; y < zh; y++)
                    for (int x = 0; x < zw; x++)
                        if (cl.KeptOpenTile(x, y))
                            for (int j = y - GateGrow; j <= y + GateGrow; j++) for (int i = x - GateGrow; i <= x + GateGrow; i++) MarkBoth(i, j, ZGateBit);
            // outer = inner grown by ZoneExitGrow
            Band(inR, zR); Band(inV, zV);
            int walk = 0, inz = 0, br = 0, na = 0, wa = 0, la = 0, ga = 0, walkV = 0, inzV = 0;
            for (int y = 0; y < zh; y++)
                for (int x = 0; x < zw; x++)
                {
                    int k = y * zw + x;
                    if (TP(x, y, false) && TP(x + 1, y, false) && TP(x, y + 1, false) && TP(x + 1, y + 1, false))
                    { walkV++; if ((zV[(y + 1) * zw + x + 1] & ZInner) != 0) inzV++; }        // (a vehicle at this footprint: its centre tile)
                    if (!pf.TilePassable(x, y, true)) continue;
                    walk++; byte b = zR[k];
                    if ((b & ZInner) == 0) { if (nComp[k] >= 0) na++; continue; }
                    if (nComp[k] >= 0) na++;
                    inz++; if ((b & ZBridgeBit) != 0) br++; if ((b & ZWaterBit) != 0) wa++; if ((b & ZLaneBit) != 0) la++; if ((b & ZGateBit) != 0) ga++;
                }
            ZoneWalkable = walk; ZoneTiles = inz; ZoneBridgeTiles = br; ZoneNarrowTiles = na; ZoneWaterTiles = wa; ZoneLaneTiles = la; ZoneGateTiles = ga;
            ZoneWalkableV = walkV; ZoneTilesV = inzV;
        }

        // ------------------------------------------------------------------ bottlenecks: zones only while traffic opposes in them
        int[] nComp, nDist, nNear; int nComps; double[] nActive; bool[] nSqueeze;
        /// <summary>Review round 2: each tile's NearK nearest passages and their walking distances (sorted, -1 = none).</summary>
        int[] nNearK, nDistK; const int NearK = 3;
        readonly List<int> ghostTiles = new List<int>(); double ghostUntil = -100;
        /// <summary>Review round 2: Chebyshev distance (tiles) from tile t to the nearest ghost passage tile while the ghost is active.</summary>
        int GhostDist(int t, double now)
        {
            if (ghostTiles.Count == 0 || ghostUntil < now || t < 0) return int.MaxValue;
            int tx = t % zw, ty = t / zw, best = int.MaxValue;
            foreach (int k in ghostTiles) { int d = Math.Max(Math.Abs(k % zw - tx), Math.Abs(k / zw - ty)); if (d < best) best = d; }
            return best;
        }
        void InsertNear(int k, int comp, int d)
        {
            int b = k * NearK;
            for (int i = 0; i < NearK; i++)
            {
                if (nNearK[b + i] == comp) return;
                if (nNearK[b + i] < 0 || d < nDistK[b + i])
                {
                    for (int j = NearK - 1; j > i; j--) { nNearK[b + j] = nNearK[b + j - 1]; nDistK[b + j] = nDistK[b + j - 1]; }
                    nNearK[b + i] = comp; nDistK[b + i] = d; return;
                }
            }
        }
        /// <summary>Review round 2: walking distance from tile t to the nearest ACTIVE passage among its NearK (int.MaxValue = none).</summary>
        int ActiveDist(int t, double now)
        {
            int best = int.MaxValue;
            for (int q = 0; q < NearK; q++) { int pc = nNearK[t * NearK + q]; if (pc >= 0 && nActive[pc] >= now && nDistK[t * NearK + q] < best) best = nDistK[t * NearK + q]; }
            return best;
        }
        /// <summary>Checks: squeeze passages (review round 2) and whether a tile's passage is one.</summary>
        public int SqueezeCount { get; private set; }
        public bool SqueezeAt(int tx, int ty) { EnsureZoneMap(); int t = ZIdx(tx, ty); return t >= 0 && nComp[t] >= 0 && nSqueeze[nComp[t]]; }
        readonly List<(int c, ZObject u, double dx, double dy)> nRec = new List<(int c, ZObject u, double dx, double dy)>();
        bool[] nOpp, nKeep; bool ghostKeep;
        /// <summary>Checks: passages (bottleneck components), activations so far.</summary>
        public int PassageCount => nComps;
        public int PassageActivations { get; private set; }
        /// <summary>Checks: the last passage activation (time, passage, the two units).</summary>
        public string LastPassageNote { get; private set; }
        /// <summary>Checks / map dumps: the bottleneck passage id of a tile (-1 none) and whether a unit there is zoned by an active one now.</summary>
        public int PassageAt(int tx, int ty) { EnsureZoneMap(); int t = ZIdx(tx, ty); return t >= 0 ? nComp[t] : -1; }
        public bool PassageActive(int tx, int ty) { EnsureZoneMap(); int t = ZIdx(tx, ty); return t >= 0 && nNear[t] >= 0 && nDist[t] <= NarrowReach && nActive[nNear[t]] >= W.Time; }

        /// <summary>The passage tiles (either grid's bottleneck positions) as 8-connected components; every walkable tile within
        /// max(NarrowSense, NarrowReach + ZoneExitGrow) steps of one gets its distance and that passage (multi-source walk on the robot grid).</summary>
        void BuildPassages(bool[] pass)
        {
            int n = zw * zh; var pf = W.Path;
            nComp = new int[n]; nDist = new int[n]; nNear = new int[n];
            for (int k = 0; k < n; k++) { nComp[k] = -1; nDist[k] = int.MaxValue; nNear[k] = -1; }
            nComps = 0;
            var q = new List<int>();
            for (int k = 0; k < n; k++)
            {
                if (!pass[k] || nComp[k] >= 0) continue;
                int id = nComps++; q.Clear(); q.Add(k); nComp[k] = id;
                for (int qi = 0; qi < q.Count; qi++)
                {
                    int cx = q[qi] % zw, cy = q[qi] / zw;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = cx + dx, ny = cy + dy; if (nx < 0 || ny < 0 || nx >= zw || ny >= zh) continue;
                        int nk = ny * zw + nx; if (!pass[nk] || nComp[nk] >= 0) continue;
                        nComp[nk] = id; q.Add(nk);
                    }
                }
            }
            q.Clear();
            for (int k = 0; k < n; k++) if (pass[k]) { nDist[k] = 0; nNear[k] = nComp[k]; q.Add(k); }
            int maxD = Math.Max(NarrowSense, NarrowReach + ZoneExitGrow);
            for (int qi = 0; qi < q.Count; qi++)
            {
                int k = q[qi], cx = k % zw, cy = k / zw;
                if (nDist[k] >= maxD) continue;
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = cx + dx, ny = cy + dy; if (nx < 0 || ny < 0 || nx >= zw || ny >= zh) continue;
                    int nk = ny * zw + nx;
                    if (nDist[nk] <= nDist[k] + 1 || !pf.TilePassableGrid(nx, ny, true, false)) continue;   // (walkable tiles only: across a wall is not near)
                    nDist[nk] = nDist[k] + 1; nNear[nk] = nNear[k]; q.Add(nk);
                }
            }
            nActive = new double[nComps]; for (int i = 0; i < nComps; i++) nActive[i] = -100;
            // review round 2: every tile's NearK nearest passages (walking distance, robot grid, up to maxD) - one BFS per passage
            nNearK = new int[n * NearK]; nDistK = new int[n * NearK];
            for (int k = 0; k < n * NearK; k++) { nNearK[k] = -1; nDistK[k] = int.MaxValue; }
            var dist = new int[n]; var seenAt = new int[n]; int stamp = 0; var bq = new List<int>();
            var compTiles = new List<int>[nComps]; for (int i = 0; i < nComps; i++) compTiles[i] = new List<int>();
            for (int k = 0; k < n; k++) if (nComp[k] >= 0) compTiles[nComp[k]].Add(k);
            for (int cc = 0; cc < nComps; cc++)
            {
                stamp++; bq.Clear();
                foreach (int k in compTiles[cc]) { seenAt[k] = stamp; dist[k] = 0; bq.Add(k); }
                for (int qi = 0; qi < bq.Count; qi++)
                {
                    int k = bq[qi], cx = k % zw, cy = k / zw;
                    InsertNear(k, cc, dist[k]);
                    if (dist[k] >= maxD) continue;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = cx + dx, ny = cy + dy; if (nx < 0 || ny < 0 || nx >= zw || ny >= zh) continue;
                        int nk = ny * zw + nx;
                        if (seenAt[nk] == stamp || !pf.TilePassableGrid(nx, ny, true, false)) continue;
                        seenAt[nk] = stamp; dist[nk] = dist[k] + 1; bq.Add(nk);
                    }
                }
            }
            nOpp = new bool[nComps]; nKeep = new bool[nComps];
        }

        /// <summary>BeginTick: a passage becomes active when two units of different squads with orders, within NarrowSense tiles of it and
        /// whose ways ahead (10 tiles) go into it, head opposite ways; it stays active NarrowHold s after that and while a unit it zoned (with
        /// an order) is still within NarrowReach tiles of it.</summary>
        void ActivatePassages(double now)
        {
            if (nComps == 0) return;
            nRec.Clear();
            Array.Clear(nOpp, 0, nComps); Array.Clear(nKeep, 0, nComps); ghostKeep = false;
            foreach (var u in units)
            {
                if (!u.IsMobile || Static(u) || u.Waypoints.Count == 0) continue;
                var wm = u.Waypoints[0].mode;
                if (wm != WpMode.Move && wm != WpMode.Attack && wm != WpMode.ForceMove) continue;
                int t = ZIdx(u.CenterX >> 4, u.CenterY >> 4);
                if (R3(16384) && t >= 0 && u.PC.zoned && ghostUntil >= now - 0.0101 && GhostDist(t, now - 0.0101) <= NarrowReach + ZoneExitGrow) ghostKeep = true;   // review round 2: a ghost passage kept
                if (t < 0 || nNear[t] < 0) continue;
                int c = nNear[t];
                if ((u.PC.zoneOwn || (R3(1024) && u.PC.passOwn)) && nDist[t] <= NarrowReach && nActive[c] >= now - 0.0101) nKeep[c] = true;
                if (R3(4096) && u.PC.zoned && nDist[t] <= NarrowReach + ZoneExitGrow && nActive[c] >= now - 0.0101) nKeep[c] = true;   // review round 2: a zoned unit with an order in its band
                if (R3(8192) && u.PC.zoned)
                    for (int q = 0; q < NearK; q++) { int pc = nNearK[t * NearK + q]; if (pc >= 0 && nDistK[t * NearK + q] <= NarrowReach + ZoneExitGrow && nActive[pc] >= now - 0.0101) nKeep[pc] = true; }
                if (nDist[t] > NarrowSense) continue;
                if (nComp[t] != c && !RouteHits(u, 160, k => nComp[k] == c)) continue;    // (passing by its mouth: not traffic through it)
                var (gx, gy) = u.PathFinalPoint;
                double dx = gx - u.CenterX, dy = gy - u.CenterY, l = Math.Sqrt(dx * dx + dy * dy);
                if (l < 8) continue;
                nRec.Add((c, u, dx / l, dy / l));
            }
            double cosOpp = Math.Cos(NarrowOppose * Math.PI / 180);
            for (int i = 0; i < nRec.Count; i++)
                for (int j = i + 1; j < nRec.Count; j++)
                {
                    var a = nRec[i]; var b = nRec[j];
                    if (a.c != b.c || nOpp[a.c] || SameSquad(a.u, b.u)) continue;
                    if (a.dx * b.dx + a.dy * b.dy < cosOpp) { nOpp[a.c] = true; if (nActive[a.c] < now) LastPassageNote = $"{now:0.00} passage {a.c}: {a.u.Type}#{a.u.Id} ({a.u.CenterX >> 4},{a.u.CenterY >> 4}) vs {b.u.Type}#{b.u.Id} ({b.u.CenterX >> 4},{b.u.CenterY >> 4})"; }
                }
            if (ghostKeep) ghostUntil = Math.Max(ghostUntil, now);
            for (int c = 0; c < nComps; c++)
            {
                if (nOpp[c]) { if (nActive[c] < now) PassageActivations++; nActive[c] = Math.Max(nActive[c], now + (R3(4096) ? PassHold : NarrowHold)); }
                else if (nKeep[c]) nActive[c] = Math.Max(nActive[c], now);
            }
        }

        void Band(bool[] inner, byte[] zm)
        {
            for (int y = 0; y < zh; y++)
                for (int x = 0; x < zw; x++)
                {
                    int k = y * zw + x;
                    if (inner[k]) { zm[k] |= ZInner; continue; }
                    bool near = false;
                    for (int j = Math.Max(0, y - ZoneExitGrow); j <= Math.Min(zh - 1, y + ZoneExitGrow) && !near; j++)
                        for (int i = Math.Max(0, x - ZoneExitGrow); i <= Math.Min(zw - 1, x + ZoneExitGrow); i++) if (inner[j * zw + i]) { near = true; break; }
                    if (near) zm[k] |= ZOuter;
                }
        }

        int[] pSeen; int pStamp; readonly List<int> pQueue = new List<int>(); readonly List<int> pNext = new List<int>();

        /// <summary>Bottleneck positions of one grid (robots: free tiles, rocks solid; vehicles: positions where the 2x2 footprint fits): a
        /// maximal free run across (in a row or a column) of at most NarrowRun positions, open on both ends of the passage (a position right
        /// before and right after it in the passage's direction), with no way round it within DetourMax steps (8 directions, no corner
        /// cutting, the run itself closed). A gap between two loose rocks is no bottleneck (a few steps round); a gap between a cliff and a
        /// rock mass, an opening in a rock wall, a 2-3 tile vehicle corridor is.</summary>
        bool[] Passages(bool robot, bool[] run1)
        {
            var pf = W.Path; int n = zw * zh;
            var f = new bool[n];
            for (int y = 0; y < zh; y++)
                for (int x = 0; x < zw; x++)
                    f[y * zw + x] = robot ? pf.TilePassableGrid(x, y, true, false)
                                          : x + 1 < zw && y + 1 < zh && pf.TilePassableGrid(x, y, false, false) && pf.TilePassableGrid(x + 1, y, false, false) && pf.TilePassableGrid(x, y + 1, false, false) && pf.TilePassableGrid(x + 1, y + 1, false, false);
            bool F(int x, int y) => x >= 0 && y >= 0 && x < zw && y < zh && f[y * zw + x];
            var res = new bool[n];
            if (pSeen == null || pSeen.Length != n) pSeen = new int[n];
            for (int pass = 0; pass < 2; pass++)
            {
                bool rows = pass == 0;                                                       // rows: narrow across x, the passage runs along y
                int lines = rows ? zh : zw, len = rows ? zw : zh;
                for (int line = 0; line < lines; line++)
                    for (int a = 0; a < len;)
                    {
                        if (!(rows ? F(a, line) : F(line, a))) { a++; continue; }
                        int s = a; while (a < len && (rows ? F(a, line) : F(line, a))) a++;
                        int e = a - 1;
                        if (e - s + 1 > NarrowRun) continue;
                        bool before = false, after = false;
                        for (int i = s; i <= e; i++)
                        {
                            if (rows ? F(i, line - 1) : F(line - 1, i)) before = true;
                            if (rows ? F(i, line + 1) : F(line + 1, i)) after = true;
                        }
                        if (!before || !after || DetourWithin(f, rows, line, s, e)) continue;
                        for (int i = s; i <= e; i++) { res[rows ? line * zw + i : i * zw + line] = true; if (run1 != null && e == s) run1[rows ? line * zw + i : i * zw + line] = true; }
                    }
            }
            return res;
        }

        /// <summary>Passages: a way from a position right before the run [s, e] on `line` to one right after it, the run closed, within
        /// DetourMax steps (BFS, 8 directions, no corner cutting).</summary>
        bool DetourWithin(bool[] f, bool rows, int line, int s, int e)
        {
            bool Run(int x, int y) => rows ? y == line && x >= s && x <= e : x == line && y >= s && y <= e;
            bool Ok(int x, int y) => x >= 0 && y >= 0 && x < zw && y < zh && f[y * zw + x] && !Run(x, y);
            pStamp++; pQueue.Clear();
            for (int i = s; i <= e; i++)
            {
                int x = rows ? i : line - 1, y = rows ? line - 1 : i;
                if (Ok(x, y) && pSeen[y * zw + x] != pStamp) { pSeen[y * zw + x] = pStamp; pQueue.Add(y * zw + x); }
            }
            for (int step = 0; step < DetourMax && pQueue.Count > 0; step++)
            {
                pNext.Clear();
                foreach (int k in pQueue)
                {
                    int cx = k % zw, cy = k / zw;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = cx + dx, ny = cy + dy;
                            if (!Ok(nx, ny) || (dx != 0 && dy != 0 && (!Ok(cx + dx, cy) || !Ok(cx, cy + dy)))) continue;
                            int nk = ny * zw + nx;
                            if (pSeen[nk] == pStamp) continue;
                            pSeen[nk] = pStamp;
                            if (rows ? ny == line + 1 && nx >= s && nx <= e : nx == line + 1 && ny >= s && ny <= e) return true;   // right after the run: a short way round
                            pNext.Add(nk);
                        }
                }
                pQueue.Clear(); pQueue.AddRange(pNext);
            }
            return false;
        }

        // ------------------------------------------------------------------ group orders: one spot rule and one arrival rule set per order
        bool spreadLive;
        /// <summary>ZSlots.SpreadGroup hook (DV44 on): a group order whose clicked point lies in / next to a zone, or whose members' straight
        /// ways to it cross a zone, gets the live spots and (TestGroupRules) the live arrival / push rules for every member until its next
        /// order; any other group order DV44's spots and settle.</summary>
        internal bool SpreadGroupV3(List<ZObject> group, int px, int py)
        {
            bool liveZone = TestZoneRules[5] || (ZonesOn && (ZoneWithin(px, py, GroupZoneReach) || WaterWithin(px, py, 1) || GoalOutOfReach(group, px, py) || (TestGroupCross && GroupCrossesZone(group, px, py))));
            bool livePass = !liveZone && ZonesOn && R4(256) && GroupThroughPassage(group, px, py);   // review round 3: a group order through a <= 2-tile passage: live
            bool live = liveZone || livePass;
            if (!live)
            {
                SpreadGroupCommit(group, px, py);
                if (ZonesOn) foreach (var u in group)
                {                                                                        // (review round 3: an order with DV44's group spots; no one-way live term from its order)
                    u.PC.dvSpotWp = u.Waypoints.Count > 0 ? u.Waypoints[0] : ZWaypoint.Cleared; DropOrderLive(u);
                    foreach (var m in u.Minions) if (m != null) { m.PC.dvSpotWp = u.PC.dvSpotWp; DropOrderLive(m); }
                }
                return true;
            }
            spreadLive = true;
            try { SpreadGroup(group, px, py); } finally { spreadLive = false; }
            if (ZonesOn && TestGroupRules)
            {
                LiveGroupOrders++;
                foreach (var u in group) { u.PC.liveOrder = true; u.PC.liveZOff = false; u.PC.liveOrderPass = livePass; foreach (var m in u.Minions) if (m != null) { m.PC.liveOrder = true; m.PC.liveZOff = false; m.PC.liveOrderPass = livePass; } Log(u, livePass ? "v3: group order through a passage: live spots and arrivals" : "v3: group order through / at a zone: live spots and arrivals"); }
            }
            return true;
        }

        /// <summary>The clicked point is out of reach for most of the group (inside a cliff, a building): every member resolves to the nearest
        /// reachable point and the group crowds at the edge - the live spots and arrivals (DV44's shape spots left squads spread there).</summary>
        bool GoalOutOfReach(List<ZObject> group, int px, int py)
        {
            int n = 0, outOf = 0;
            foreach (var u in group)
            {
                if (u.IsMinion) continue;
                bool nr = NoRocks(u); int c = Reach.UnitComp(u, nr);
                if (c < 0) continue;
                n++; if (!Reach.PointOk(px, py, u.IsRobot, nr, c)) outOf++;
            }
            return n > 0 && outOf * 2 > n;
        }

        /// <summary>A wadeable water tile lies within `tiles` tiles of the point (px): a group order into the water (robots wade in, vehicles stop
        /// at the shore) arrives the live way too (measured: DV44's spots and settle there left the harbour group 16 tiles out).</summary>
        bool WaterWithin(int px, int py, int tiles)
        {
            int tx = px >> 4, ty = py >> 4; var map = W.Map; var pf = W.Path;
            for (int j = ty - tiles; j <= ty + tiles; j++)
                for (int i = tx - tiles; i <= tx + tiles; i++)
                    if (i >= 0 && j >= 0 && i < zw && j < zh && map.ClassAt(i, j) == ZClass.Water && pf.TilePassable(i, j, true)) return true;
            return false;
        }

        /// <summary>ZSlots.OnOrder (via GroupOrderReset): a new order ends the live arrival rules of the unit's (and its squad's) last group order.</summary>
        internal void LiveOrderReset(ZObject o)
        {
            o.PC.liveOrder = false; o.PC.liveZOff = false;
            foreach (var m in o.Minions) if (m != null) { m.PC.liveOrder = false; m.PC.liveZOff = false; }
            if (R4(16777216)) OrderLiveStart(o);
        }

        /// <summary>Review round 3 (R4 16777216): ZWorld.SendWaypoints (via ZSlots.OnOrder): a new MOVE / attack order whose shortest way from
        /// where the unit stands (8 directions on its own grid, a walk from the goal of at most GroupPassSteps steps) enters a passage of
        /// walkable width <= 2 within PassLook px, the unit within NarrowSense steps of a passage, no opposing traffic near: the one-way live
        /// term starts AT ONCE - before the order's first path request (its squad's minions plan their own Zod paths like live, no DV44 queue,
        /// no trail follow first). A group order that then gets DV44's spots drops it again (SpreadGroupV3: one rule set per group order).</summary>
        void DropOrderLive(ZObject u) { var c = u.PC; if (!c.passLive || c.passLiveAtOrder.mode == WpMode.None) return; c.passLive = false; c.passLiveAtOrder = ZWaypoint.Cleared; c.passLiveWp = ZWaypoint.Cleared; }
        void OrderLiveStart(ZObject o)
        {
            if (!CommitOn || !ZonesOn || !o.IsMobile || o.Waypoints.Count == 0) return;
            var wp = o.Waypoints[0];
            if ((wp.mode != WpMode.Move && wp.mode != WpMode.Attack && wp.mode != WpMode.ForceMove) || HelperMove(wp)) return;
            EnsureZoneMap();
            int t = ZIdx(o.CenterX >> 4, o.CenterY >> 4);
            if (t < 0 || nNear[t] < 0) return;
            bool robot = o.IsRobot;
            var (gx, gy) = ZReach.FootTileAt(wp.x, wp.y, robot);
            GpBfs(robot, gx, gy);
            var (sx, sy) = robot ? (o.CenterX >> 4, o.CenterY >> 4) : ZReach.FootTile(o.LocX, o.LocY);
            if (!GpOk(sx, sy) || !GpCrosses(robot, sx, sy, (int)(PassLook / 16))) return;
            if (R4(2048) && OpposedNear(o)) return;
            double now = W.Time;
            void Set(ZObject u)
            {
                var c = u.PC; if (!c.passLive) Log(u, "v3: its order's way goes through a passage: live rules from the order on");
                c.passLive = true; c.passLiveIn = now; c.passLiveAtOrder = wp; c.passLiveWp = PassageNear(wp.x, wp.y, PassGoalReach) ? wp : ZWaypoint.Cleared;
            }
            Set(o); foreach (var m in o.Minions) if (m != null && !m.Destroyed) Set(m);
        }

        /// <summary>At least GroupCrossShare of the group's members (squads by their leader) have a straight way from where they stand to the
        /// clicked point that crosses their grid's zone mask (a bridge, a water crossing, a fort gate; GroupRouteSide tiles either side): the
        /// group goes through a zone on its way (it arrives strung out - late units must be able to push in, the live arrival).</summary>
        bool GroupCrossesZone(List<ZObject> group, int px, int py)
        {
            EnsureZoneMap();
            int n = 0, cross = 0;
            foreach (var u in group)
            {
                if (u.IsMinion) continue;
                n++;
                var zm = ZM(u);
                double x0 = u.CenterX, y0 = u.CenterY, dx = px - x0, dy = py - y0, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1) continue;
                double ux = dx / len, uy = dy / len; bool hit = false;
                for (double s = 0; s <= len && !hit; s += 8)
                    for (int k = -GroupRouteSide; k <= GroupRouteSide && !hit; k++)
                    {
                        int t = ZIdx((int)Math.Floor(x0 + ux * s - uy * k * 16) >> 4, (int)Math.Floor(y0 + uy * s + ux * k * 16) >> 4);
                        if (t >= 0 && (zm[t] & ZInner) != 0) hit = true;
                    }
                if (hit) cross++;
            }
            return n > 0 && cross >= GroupCrossShare * n;
        }
        /// <summary>Share of a group's members whose way crosses a zone for the group order to take the live spots and arrivals.</summary>
        internal static double GroupCrossShare = 0.5;

        // ------------------------------------------------------------------ hand-over: trail follow <-> own Zod path
        /// <summary>FollowStep (v3): a minion on its own path after its squad left a zone re-joins the trail only RejoinHold s after the zone and
        /// where the trail is straight (no turn to re-join); RejoinWindow s after the zone it re-joins anyway.</summary>
        internal bool RejoinOk(ZObject m)
        {
            if (!ZonesOn) return true;
            var c = m.PC; var l = m.Leader;
            if (l == null || W.Time - c.zoneOffAt > RejoinWindow) return true;
            if (R2(32) && !m.IsMoving && !m.PathPending && m.PathOnLastLeg) return true;                // review round 2: no own path left to walk: back on the trail (never neither)
            if (W.Time - c.zoneOffAt < RejoinHold) return false;                               // (at least RejoinHold s on its own path after the zone)
            if (!TestStraightRejoin || !TrailValid(l)) return true;
            double s = TrailProject(l, CX(m), CY(m), out double off);
            if (off > RejoinOff) return false;
            TrailDir(l, s, out double ax, out double ay);                                  // the trail where it would join, and 2 tiles ahead of that
            TrailDir(l, Math.Max(0, s - 32), out double bx, out double by);
            return Deg(ax, ay, bx, by) <= RejoinTurn;
        }
        static double Deg(double ux, double uy, double vx, double vy) => Math.Acos(Math.Max(-1, Math.Min(1, ux * vx + uy * vy))) * 180 / Math.PI;

        /// <summary>Review round 2 (R3 256): the hand-over path along the leader's way - the trail ahead of the minion's place (every 16 px) up to
        /// the leader, his target and the rest of his path to his goal (FollowPath) - when the minion is near the trail (RejoinOff), a free line
        /// leads to its first point and that goal is the minion's own; first / last points behind it dropped (NoBackSteps). Null = Zod's path.</summary>
        internal List<(int x, int y)> TrailHandoverPath(ZObject m)
        {
            var l = m.Leader;
            if (l == null || !TrailValid(l) || l.Waypoints.Count == 0 || m.Waypoints.Count == 0 || !l.Waypoints[0].Equals(m.Waypoints[0])) return null;
            if (R3(65536) && AheadOfLeader(m, l) && (!R3(1048576) || OtherTrafficNear(m))) return null;   // (level with / ahead of him in traffic: its own Zod path forward)
            TrailProject(l, CX(m), CY(m), out double off);
            if (off > RejoinOff) return null;
            var pts = new List<(int x, int y)>(32);
            FollowPath(m, pts);
            if (pts.Count < 2) return null;
            var (gx, gy) = l.PathFinalPoint;
            if (Math.Abs(gx - m.PathTargetX) > 1 || Math.Abs(gy - m.PathTargetY) > 1) { var (mx, my) = (m.PathTargetX, m.PathTargetY); if (pts[pts.Count - 1].x != mx || pts[pts.Count - 1].y != my) pts.Add((mx, my)); }
            if (!LineFreeBetween(m, CX(m), CY(m), pts[0].x, pts[0].y, NoRocks(m))) return null;
            return NoBackStepsCore(m, pts);
        }

        /// <summary>Review round 2 (R3 65536): ZFormation.FormationStep - a zoned minion level with or ahead of its walking leader (along his
        /// heading) does not take its live V slot behind him (that walked it back into his face): it keeps its own Zod path forward.</summary>
        /// <summary>Review round 3 (R4 512): the hand-over is the live one (Zod's path from where the minion stands) on a bridge / water / gate zone
        /// tile or its band, or when its leader has walked less than HandoverTrailMin px on this order (zoned at the order's start: the trail
        /// is no way yet - the squad crowded onto the leader's line and held each other); in head-on traffic (SmoothFor) the leader's way.</summary>
        internal bool HandoverZod(ZObject m) => StaticBandAt(m) || (R4(65536) && (TestZodHandoverOneWay ? !SmoothFor(m) : (m.Leader != null && TrailLength(m.Leader) < HandoverTrailMin && W.Time - m.PC.headLast > ZonedHold + 1e-6)));   // (not with a head-on / squeeze reason in its squad)
        internal static double HandoverTrailMin = 32; internal static bool TestZodHandoverOneWay = false;
        internal bool HandoverAhead(ZObject m)
        {
            if (!ZonesOn || !R3(65536) || !Zoned(m) || m.Leader == null || !SmoothFor(m)) return false;
            if (R3(1048576) && !OtherTrafficNear(m)) return false;                          // (R3 1048576: only in traffic; a squad on its own keeps its V slots)
            return AheadOfLeader(m, m.Leader);
        }
        static bool AheadOfLeader(ZObject m, ZObject l)
        {
            double vl = Math.Sqrt((double)l.Dx * l.Dx + (double)l.Dy * l.Dy);
            if (vl < 1e-6) return false;
            return (CX(m) - CX(l)) * l.Dx / vl + (CY(m) - CY(l)) * l.Dy / vl > AheadMargin;
        }
        /// <summary>(A/B) how far along the leader's heading a minion must be ahead of him to count as ahead (px; -2 = level with him).</summary>
        public static double AheadMargin = 8;

        /// <summary>DV44 NoBackSteps for a hand-over path (the unit is zoned: the DV44 check itself does not run for it).</summary>
        internal List<(int x, int y)> HandoverNoBackSteps(ZObject o, List<(int x, int y)> pts) => NoBackStepsCore(o, pts);
        internal void CountHandover() => Handovers++;

        // ------------------------------------------------------------------ DV47 soft unstick
        /// <summary>A map item a unit may step over when stuck in a tight place: a wreck (DV39's harbour wrecks, the test wreck). Intact
        /// props (barrels, crates, stop signs, cacti), rocks, city blocks, huts and buildings never.</summary>
        static bool SoftType(ZObject o) => o.Type == ZWorld.WreckType;

        void BuildSoftTiles()
        {
            softTiles.Clear(); softObjs.Clear(); softLaneMemo.Clear();
            var pf = W.Path;
            foreach (var o in W.Objects)
            {
                if (!o.IsDestroyableImpass || o.Destroyed || o.KillAt >= 0 || !SoftType(o)) continue;
                int tx0 = o.LocX >> 4, ty0 = o.LocY >> 4, tx1 = Math.Max(tx0, (o.LocX + o.WidthPix - 1) >> 4), ty1 = Math.Max(ty0, (o.LocY + o.HeightPix - 1) >> 4) + 2;
                bool any = false;
                for (int j = ty0; j <= ty1; j++)
                    for (int i = tx0; i <= tx1; i++)
                    {
                        if (i < 0 || j < 0 || i >= zw || j >= zh || !o.CausesImpassAtCoord(i * 16 + 8, j * 16 + 8)) continue;
                        if (pf.TilePassable(i, j, true) || !pf.TilePassableGrid(i, j, true, true)) continue;   // blocked by it alone (a destroyable barrier)
                        softTiles.Add(j * zw + i); any = true;
                    }
                if (any) softObjs.Add(o);
            }
        }

        /// <summary>The unit may stand on this tile under DV47: a soft blocker's tile its own grid would allow without the blocker.</summary>
        bool SoftUsable(int tx, int ty, bool robot) => tx >= 0 && ty >= 0 && tx < zw && ty < zh && softTiles.Contains(ty * zw + tx) && W.Path.TilePassableGrid(tx, ty, robot, true);

        /// <summary>ZObject.ProcessMove / the minion clip check (DV47): a unit with the soft permission may take a step whose box touches only
        /// free and soft tiles (Zod's shrunk box, 1 px each side). False whenever pathing v3 is off.</summary>
        public bool SoftStep(ZObject o, int inx, int iny)
        {
            if (!Enabled || !CommitOn || softTiles.Count == 0 || (!o.PC.softPass && !BoxOnSoft(o))) return false;   // (on the pass, or still standing on debris: it may step off)
            var pf = W.Path; bool robot = o.IsRobot;
            int x = inx + 1, y = iny + 1, w = o.WidthPix - 2, h = o.HeightPix - 2;
            if (x < 0 || y < 0 || x + w >= pf.WidthPix || y + h >= pf.HeightPix) return false;
            for (int j = y >> 4; j <= (y + h - 1) >> 4; j++)
                for (int i = x >> 4; i <= (x + w - 1) >> 4; i++)
                    if (!pf.TilePassable(i, j, robot) && !SoftUsable(i, j, robot)) return false;
            return true;
        }

        /// <summary>ZPathing.ImpassCount (DV47): for a unit on the soft pass or standing on debris a soft tile is no blocked tile (so the
        /// escape rule and the pushes never let it slide from the debris into a hard one).</summary>
        internal bool SoftFree(ZObject o, int tx, int ty) => CommitOn && softTiles.Count > 0 && SoftUsable(tx, ty, o.IsRobot) && (o.PC.softPass || BoxOnSoft(o));

        /// <summary>ZObject.ProcessMove, the minion clip check (DV47): a minion standing on debris may not step into a hard blocked tile (Zod's
        /// minion clip guard only watches steps from a clear spot). True = blocked (stop point out).</summary>
        public bool SoftHardStep(ZObject o, int inx, int iny, ref int stopX, ref int stopY)
        {
            if (!Enabled || !CommitOn || softTiles.Count == 0 || !BoxOnSoft(o)) return false;
            var pf = W.Path; bool robot = o.IsRobot;
            int x = inx + 1, y = iny + 1, w = o.WidthPix - 2, h = o.HeightPix - 2;
            for (int j = y >> 4; j <= (y + h - 1) >> 4; j++)
                for (int i = x >> 4; i <= (x + w - 1) >> 4; i++)
                    if (!pf.TilePassable(i, j, robot) && !SoftUsable(i, j, robot)) { stopX = i * 16 + 8; stopY = j * 16 + 8; return true; }
            return false;
        }

        bool BoxOnSoft(ZObject o)
        {
            int x = o.LocX + 1, y = o.LocY + 1, w = o.WidthPix - 2, h = o.HeightPix - 2;
            for (int j = y >> 4; j <= (y + h - 1) >> 4; j++)
                for (int i = x >> 4; i <= (x + w - 1) >> 4; i++)
                    if (i >= 0 && j >= 0 && i < zw && j < zh && softTiles.Contains(j * zw + i)) return true;
            return false;
        }

        bool SoftNear(ZObject o, int tiles) => SoftNearPoint(o.CenterX, o.CenterY, tiles);
        bool SoftNearPoint(int px, int py, int tiles)
        {
            int tx = px >> 4, ty = py >> 4;
            foreach (int k in softTiles) { int x = k % zw, y = k / zw; if (Math.Abs(x - tx) <= tiles && Math.Abs(y - ty) <= tiles) return true; }
            return false;
        }

        static bool HelperMove(ZWaypoint wp) => wp.refId == RefStepAside || wp.refId == RefBackOff || wp.refId == RefRegroup || wp.refId == RefBay;

        /// <summary>The unit's ORDER (the waypoint the DV47 clock is keyed to): the first waypoint that is no helper move.</summary>
        static int OrderIndex(ZObject u)
        {
            for (int i = 0; i < u.Waypoints.Count; i++) if (!HelperMove(u.Waypoints[i])) return i;
            return -1;
        }

        /// <summary>EndTick (after the stuck guard): DV47. Permissions are kept / ended first; then the progress clock of every unit with an
        /// order runs (net progress along its way to the order's goal; active time only: in a tight place, on the order itself, not waiting
        /// for a path answer); a unit whose net progress over the last UnstickAfter s of active time is under UnstickProgress px tries a
        /// path through soft blockers (a squad leader for his squad too).</summary>
        void UnstickTick()
        {
            if (!CommitOn || !UnstickOn) return;
            EnsureZoneMap();
            double now = W.Time;
            foreach (var u in units)
            {
                var c = u.PC;
                if (!c.softPass) continue;
                SoftPassTicks++;
                bool on = BoxOnSoft(u);
                if (on) { c.softWasOn = true; c.softOffSince = -1; }
                else if (c.softOffSince < 0) c.softOffSince = now;
                bool orderGone = u.Waypoints.Count == 0 || !u.Waypoints[0].Equals(c.softWp);
                bool through = c.softWasOn && !on && now - c.softOffSince >= UnstickClear;
                if (on && u.Waypoints.Count == 0 && !Static(u)) StepOffSoft(u);                 // its order ended on the debris: it steps off (nobody parks on a wreck)
                if (!on && (through || orderGone || now >= c.softUntil))
                {
                    c.softPass = false; ClockReset(c);
                    Log(u, $"DV47: soft pass ends ({(through ? "through" : orderGone ? "order ended" : "time")})");
                }
            }
            if (softTiles.Count == 0 && !(ZonesOn && R4(128))) return;                   // (review round 3: the clock also runs for the live hand-off where no debris lies)
            foreach (var u in units)
            {
                var c = u.PC;
                if (c.softPass || !u.IsMobile || Static(u) || u.Waypoints.Count == 0) { if (!c.softPass) { ClockReset(c); c.stWp = ZWaypoint.Cleared; } continue; }
                int oi = OrderIndex(u);
                if (oi < 0) { ClockReset(c); continue; }
                var ow = u.Waypoints[oi];
                if ((ow.mode != WpMode.Move && ow.mode != WpMode.Attack) || u.AttackObject != null) { ClockReset(c); c.stWp = ZWaypoint.Cleared; continue; }
                if (!ow.Equals(c.stWp)) { c.stWp = ow; ClockReset(c); c.shortTodo = oi == 0 && !u.IsMinion; continue; }   // a new order: the clock starts over
                if (c.shortTodo && oi == 0 && !u.PathPending) { c.shortTodo = false; if (softTiles.Count > 0) SoftShortcut(u); }   // its Zod path is known: a way over debris much shorter?
                bool follows = FollowsSquad(u) && !LeftBehind(u, now);                          // (a minion walking with its leader: the leader's clock is the squad's -
                bool active = oi == 0 && !u.PathPending && !u.PathInBuildingStage && !follows && TightAt(u.CenterX, u.CenterY);   //  unless it stands while its leader is far ahead)
                if (!ClockStep(u, c, active)) continue;
                if (now < c.softNextTry) continue;
                c.softNextTry = now + UnstickRetry;
                if (softTiles.Count == 0 || !SoftNear(u, SoftReach))
                {
                    if (ZonesOn && R4(128) && (!R4(524288) || HandbackTight(u))) { StuckLiveStart(u); continue; }   // review round 3: no debris near - it (and its squad) runs the live rules until past the passage (R4 524288: at a passage, a zone or a lane)
                    Log(u, "DV47: stuck in a tight place, no soft blocker near"); continue;
                }
                TryUnstick(u);
            }
        }

        /// <summary>Review round 2: a following minion that has stood (moved under 2 px) for LeftBehindStand s while its leader is more than
        /// LeftBehindDist px away runs its own DV47 clock (the leader walking on is no progress of its own).</summary>
        bool LeftBehind(ZObject m, double now)
        {
            var c = m.PC; double x = CX(m), y = CY(m);
            if (double.IsNaN(c.mStandX) || Math.Abs(x - c.mStandX) + Math.Abs(y - c.mStandY) > 2) { c.mStandX = x; c.mStandY = y; c.mStandSince = now; }
            if (!ZonesOn || !R2(64) || m.Leader == null || now - c.mStandSince < LeftBehindStand) return false;
            double dx = CX(m.Leader) - x, dy = CY(m.Leader) - y;
            return dx * dx + dy * dy > LeftBehindDist * LeftBehindDist;
        }
        public const double LeftBehindStand = 0.5, LeftBehindDist = 32;

        static void ClockReset(ZCommitState c) { c.stOdo = 0; c.stRem = double.NaN; c.stActive = 0; c.stNextSample = 0; c.stHist.Clear(); }

        /// <summary>The DV47 progress clock: net progress along the unit's way (the drop of its remaining path from tick to tick; a re-plan,
        /// which changes the remaining path at once, counts as no progress either way), sampled every UnstickSample s of active time. True
        /// = stuck: less than UnstickProgress px of net progress over the last UnstickAfter s of active time (a unit shuttling back and
        /// forth makes none).</summary>
        bool ClockStep(ZObject u, ZCommitState c, bool active)
        {
            if (!active) { c.stRem = double.NaN; return false; }                         // paused: helper move, path answer, outside the tight places
            double rem = u.PathRemaining();
            if (!double.IsNaN(c.stRem))
            {
                double d = c.stRem - rem;
                if (Math.Abs(d) <= Math.Max(4.0, 3 * u.RealMoveSpeed * lastDt + 1)) c.stOdo += d;
            }
            c.stRem = rem;
            c.stActive += lastDt;
            if (c.stActive + 1e-9 < c.stNextSample) return false;
            c.stNextSample = c.stActive + UnstickSample;
            c.stHist.Add((c.stActive, c.stOdo));
            while (c.stHist.Count > 2 && c.stHist[1].t <= c.stActive - UnstickAfter + 1e-9) c.stHist.RemoveAt(0);
            if (c.stHist.Count < 2 || c.stActive - c.stHist[0].t < UnstickAfter - 1e-9) return false;
            return c.stOdo - c.stHist[0].odo < UnstickProgress;
        }

        /// <summary>A path for u (and its squad) through soft blockers to its path's end, or to the first point of its path at least 8 tiles
        /// ahead that lies inside the search window (the rest of its path follows). Units in the way are gone round first, then ignored.</summary>
        void TryUnstick(ZObject u)
        {
            if (!SoftGoal(u, out int gx, out int gy, out var rest)) return;
            var pts = SoftSearch(u, gx, gy, true, out bool soft);
            if (pts == null || !soft) pts = SoftSearch(u, gx, gy, false, out soft);           // (the traffic it is stuck in: the gate keeps the spacing)
            if (pts == null || !soft) { Log(u, "DV47: stuck in a tight place, no way over soft blockers"); return; }
            ApplySoft(u, pts, rest);
            Unsticks++;
            foreach (var m in u.Minions)
            {
                if (m == null || m.Destroyed || m.KillAt >= 0 || m.Waypoints.Count == 0 || !m.Waypoints[0].Equals(u.Waypoints[0])) continue;
                var mp = SoftSearch(m, gx, gy, true, out _) ?? SoftSearch(m, gx, gy, false, out _);
                if (mp != null) ApplySoft(m, mp, rest);
                else { var mc = m.PC; mc.softPass = true; mc.softWp = m.Waypoints[0]; mc.softUntil = W.Time + UnstickHold; mc.softWasOn = false; mc.softOffSince = -1; mc.softGoalWp = ZWaypoint.Cleared; mc.softHoldSince = -1; ClockReset(mc); }
            }
        }

        bool SoftGoal(ZObject u, out int gx, out int gy, out List<(int x, int y)> rest)
        {
            rest = null;
            var c = u.PC;
            if (u.Waypoints.Count > 0 && u.Waypoints[0].Equals(c.softGoalWp)) { gx = c.softGoalX; gy = c.softGoalY; return true; }   // the real goal behind the debris
            var (fx, fy) = u.PathFinalPoint; gx = fx; gy = fy;
            int ux = u.CenterX >> 4, uy = u.CenterY >> 4;
            bool InWin(int x, int y) => Math.Abs((x >> 4) - ux) < SoftWindow - 1 && Math.Abs((y >> 4) - uy) < SoftWindow - 1;
            if (InWin(fx, fy)) return true;
            var pts = new List<(int x, int y)> { (u.PathTargetX, u.PathTargetY) };
            var pp = u.PathPoints; if (pp != null) pts.AddRange(pp);
            double arc = Math.Sqrt((double)(pts[0].x - u.CenterX) * (pts[0].x - u.CenterX) + (double)(pts[0].y - u.CenterY) * (pts[0].y - u.CenterY));
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0) arc += Math.Sqrt((double)(pts[i].x - pts[i - 1].x) * (pts[i].x - pts[i - 1].x) + (double)(pts[i].y - pts[i - 1].y) * (pts[i].y - pts[i - 1].y));
                if (arc < 8 * 16 || !InWin(pts[i].x, pts[i].y)) { if (!InWin(pts[i].x, pts[i].y)) break; continue; }
                gx = pts[i].x; gy = pts[i].y;
                rest = pts.GetRange(i + 1, pts.Count - i - 1);
                return true;
            }
            return false;
        }

        void ApplySoft(ZObject u, List<(int x, int y)> pts, List<(int x, int y)> rest)
        {
            var list = new List<(int x, int y)>(pts);
            if (rest != null) list.AddRange(rest);
            if (list.Count == 0) return;
            if (list.Count == 1) list.Insert(0, (u.CenterX, u.CenterY));                     // (the path code reads two points)
            u.PathSetPoints(list);
            var c = u.PC; var s = u.PS;
            c.softPass = true; c.softWp = u.Waypoints[0]; c.softUntil = W.Time + UnstickHold; c.softWasOn = false; c.softOffSince = -1; ClockReset(c);
            c.softGoalWp = ZWaypoint.Cleared; c.softHoldSince = -1;                           // (a goal behind debris: on its way to it now)
            s.lastProgress = W.Time; s.rebase = true; s.blockRepaths = 0;
            Log(u, $"DV47: stuck {UnstickAfter:0} s in a tight place: a way over soft blockers, {list.Count} points");
        }

        readonly List<int> sOpen = new List<int>();
        int[] sCost, sFrom; bool[] sClosed, sOcc; int sStamp; int[] sSeen;
        List<(int x, int y)> lastSoftTiles;

        /// <summary>A* (8 directions, no corner cutting) in a window round the unit on its own grid (rocks solid) where soft blocker tiles are
        /// open; with closeUnits the tiles of the units outside its squad are closed, else only those of units that never move (cannons,
        /// empty vehicles). Returns footprint-centre points (collinear ones merged) ending at (gx, gy), and whether the way crosses a soft
        /// tile (lastSoftTiles = its tiles). Null = none.</summary>
        List<(int x, int y)> SoftSearch(ZObject u, int gx, int gy, bool closeUnits, out bool usesSoft)
        {
            usesSoft = false; SoftSearches++; lastSoftTiles = null;
            bool robot = u.IsRobot; var pf = W.Path;
            var (sx, sy) = ZReach.FootTile(u.LocX, u.LocY);
            var (ex, ey) = ZReach.FootTileAt(gx, gy, robot);
            int x0 = Math.Max(0, sx - SoftWindow), y0 = Math.Max(0, sy - SoftWindow), x1 = Math.Min(zw - (robot ? 1 : 2), sx + SoftWindow), y1 = Math.Min(zh - (robot ? 1 : 2), sy + SoftWindow);
            if (ex < x0 || ey < y0 || ex > x1 || ey > y1) return null;
            int ww = x1 - x0 + 1, hh = y1 - y0 + 1, n = ww * hh;
            if (sCost == null || sCost.Length < n) { sCost = new int[n]; sFrom = new int[n]; sClosed = new bool[n]; sOcc = new bool[n]; sSeen = new int[n]; }
            sStamp++;
            foreach (var o in units)
            {
                if (o == u || SameSquad(o, u) || (!closeUnits && !Static(o))) continue;
                int ax = (o.LocX + 2) >> 4, ay = (o.LocY + 2) >> 4, bx = (o.LocX + o.WidthPix - 3) >> 4, by = (o.LocY + o.HeightPix - 3) >> 4;
                for (int j = ay; j <= by; j++) for (int i = ax; i <= bx; i++) { if (i < x0 || j < y0 || i > x1 || j > y1) continue; int k = (j - y0) * ww + (i - x0); Touch(k); sOcc[k] = true; }
            }
            bool TileOk(int x, int y) => pf.TilePassable(x, y, robot) || SoftUsable(x, y, robot);
            bool laneHeld = u.PS.wait == ZPathWait.Lane || u.PS.laneWaitSince >= 0;           // held at a lane against oncoming traffic: the way round it over the debris
            bool InLaneZoneTile(int x, int y)
            {
                if (!laneHeld) return false;
                int cx = x * 16 + 8, cy = y * 16 + 8;
                foreach (var L in Lanes) if (cx >= L.ZX0 && cx < L.ZX1 && cy >= L.ZY0 && cy < L.ZY1) return true;
                return false;
            }
            bool Ok(int x, int y)
            {
                if (x < x0 || y < y0 || x > x1 || y > y1) return false;
                int k = (y - y0) * ww + (x - x0); Touch(k); if (sOcc[k] && !(x == sx && y == sy) && !(x == ex && y == ey)) return false;   // (its own tile and the goal's never count)
                if (robot) return TileOk(x, y);
                return TileOk(x, y) && TileOk(x + 1, y) && TileOk(x, y + 1) && TileOk(x + 1, y + 1);
            }
            bool Soft(int x, int y) => robot ? softTiles.Contains(y * zw + x) : softTiles.Contains(y * zw + x) || softTiles.Contains(y * zw + x + 1) || softTiles.Contains((y + 1) * zw + x) || softTiles.Contains((y + 1) * zw + x + 1);
            if (!Ok(ex, ey)) { Log(u, $"DV47: soft search: goal tile ({ex},{ey}) not free"); return null; }
            int H(int x, int y) { int dx = Math.Abs(x - ex), dy = Math.Abs(y - ey); return 10 * Math.Max(dx, dy) + 4 * Math.Min(dx, dy); }
            sOpen.Clear();
            int s0 = (sy - y0) * ww + (sx - x0); Touch(s0); sCost[s0] = 0; sFrom[s0] = -1; sOpen.Add(s0);
            int goal = (ey - y0) * ww + (ex - x0), nodes = 0;
            while (sOpen.Count > 0 && nodes++ < SoftMaxNodes)
            {
                int bi = 0, bf = int.MaxValue;
                for (int i = 0; i < sOpen.Count; i++) { int k = sOpen[i]; int f = sCost[k] + H(x0 + k % ww, y0 + k / ww); if (f < bf || (f == bf && k < sOpen[bi])) { bf = f; bi = i; } }
                int cur = sOpen[bi]; sOpen[bi] = sOpen[sOpen.Count - 1]; sOpen.RemoveAt(sOpen.Count - 1);
                if (sClosed[cur]) continue;
                sClosed[cur] = true;
                if (cur == goal) break;
                int cx = x0 + cur % ww, cy = y0 + cur / ww;
                for (int d = 0; d < 8; d++)
                {
                    int ddx = d < 3 ? -1 : d < 5 ? 0 : 1, ddy = d == 0 || d == 3 || d == 5 ? -1 : d == 1 || d == 6 ? 0 : 1;
                    if (ddx == 0 && ddy == 0) continue;
                    int nx = cx + ddx, ny = cy + ddy;
                    if (!Ok(nx, ny)) continue;
                    if (ddx != 0 && ddy != 0 && (!Ok(cx + ddx, cy) || !Ok(cx, cy + ddy))) continue;   // no corner cutting
                    int k = (ny - y0) * ww + (nx - x0); Touch(k);
                    if (sClosed[k]) continue;
                    int cost = sCost[cur] + (ddx != 0 && ddy != 0 ? 14 : 10) + (Soft(nx, ny) ? 2 : 0) + (InLaneZoneTile(nx, ny) ? LaneAvoidCost : 0);
                    if (cost >= sCost[k]) continue;
                    sCost[k] = cost; sFrom[k] = cur; sOpen.Add(k);
                }
            }
            Touch(goal);
            if (!sClosed[goal]) { Log(u, $"DV47: soft search ({sx},{sy}) -> ({ex},{ey}){(closeUnits ? "" : " (units ignored)")}: no way ({nodes} nodes)"); return null; }
            var tiles = new List<(int x, int y)>();
            for (int k = goal; k >= 0; k = sFrom[k]) { tiles.Add((x0 + k % ww, y0 + k / ww)); if (tiles.Count > n) return null; }
            tiles.Reverse();
            foreach (var (tx, ty) in tiles) if (Soft(tx, ty)) usesSoft = true;
            lastSoftTiles = tiles;
            var pts = new List<(int x, int y)>();
            for (int i = 1; i < tiles.Count; i++)
            {
                bool last = i == tiles.Count - 1;
                if (!last)
                {
                    int ax = tiles[i].x - tiles[i - 1].x, ay = tiles[i].y - tiles[i - 1].y, bx = tiles[i + 1].x - tiles[i].x, by = tiles[i + 1].y - tiles[i].y;
                    if (ax == bx && ay == by) continue;                                      // collinear: merged
                }
                pts.Add(ZReach.FootCentre(tiles[i].x, tiles[i].y, robot));
            }
            if (pts.Count == 0 || pts[pts.Count - 1].x != gx || pts[pts.Count - 1].y != gy) pts.Add((gx, gy));
            return pts;

            void Touch(int k) { if (sSeen[k] == sStamp) return; sSeen[k] = sStamp; sCost[k] = int.MaxValue; sFrom[k] = -1; sClosed[k] = false; sOcc[k] = false; }
        }

        static double PathLen(int x0, int y0, List<(int x, int y)> pts)
        {
            double d = 0; int px = x0, py = y0;
            foreach (var p in pts) { d += Math.Sqrt((double)(p.x - px) * (p.x - px) + (double)(p.y - py) * (p.y - py)); px = p.x; py = p.y; }
            return d;
        }

        /// <summary>DV47 soft shortcut (a new MOVE order, the unit's Zod path known): debris closes a tight passage on the way over it and the
        /// way round (its Zod path) is much longer than the way over the debris (ShortcutExtra px AND ShortcutShare more). The unit (and its
        /// squad) walks to the debris instead - the way over it up to the last free tile before it - and stands there with its order (the
        /// progress clock runs); after UnstickAfter s the unstick takes it over. Live (v3 off): Zod's way round.</summary>
        void SoftShortcut(ZObject u)
        {
            if (!TestSoftShortcut || !u.IsRobot || u.Waypoints.Count == 0 || u.Waypoints[0].mode != WpMode.Move || HelperMove(u.Waypoints[0]) || !SoftNear(u, SoftReach)) return;   // (robots: a vehicle needs the whole gap, head-on it waits anyway)
            var (gx, gy) = u.PathFinalPoint;
            double zod = u.PathRemaining();
            var pts = SoftSearch(u, gx, gy, false, out bool soft);
            if (pts == null || !soft || lastSoftTiles == null) return;
            var tilesAll = lastSoftTiles;
            double over = PathLen(u.CenterX, u.CenterY, pts), speed = Math.Max(u.RealMoveSpeed, 1);
            if (zod - over < ShortcutExtra || zod < over * (1 + ShortcutShare) || (zod - over) / speed < UnstickAfter + ShortcutMargin) return;   // (the way round must cost more than the wait)
            bool robot = u.IsRobot;
            bool SoftFoot(int x, int y) => robot ? softTiles.Contains(y * zw + x) : softTiles.Contains(y * zw + x) || softTiles.Contains(y * zw + x + 1) || softTiles.Contains((y + 1) * zw + x) || softTiles.Contains((y + 1) * zw + x + 1);
            int first = -1;
            for (int i = 0; i < tilesAll.Count; i++) if (SoftFoot(tilesAll[i].x, tilesAll[i].y)) { first = i; break; }
            if (first < 1) return;
            var (fx, fy) = tilesAll[first];
            if (!TightAt(fx * 16 + 8, fy * 16 + 8)) return;                                  // only where the debris closes a tight place
            var (hx, hy) = ZReach.FootCentre(tilesAll[first - 1].x, tilesAll[first - 1].y, robot);
            var prefix = new List<(int x, int y)>();
            for (int i = 1; i < first; i++) prefix.Add(ZReach.FootCentre(tilesAll[i].x, tilesAll[i].y, robot));
            if (prefix.Count == 0 || prefix[prefix.Count - 1].x != hx || prefix[prefix.Count - 1].y != hy) prefix.Add((hx, hy));
            ShortcutTo(u, prefix, gx, gy);
            SoftShortcuts++;
            Log(u, $"DV47: debris closes the passage, the way round is {zod / 16:0} tiles, over it {over / 16:0}: walks to the debris ({hx},{hy}), waits, then over it");
            foreach (var m in u.Minions)
            {
                if (m == null || m.Destroyed || m.KillAt >= 0 || m.Waypoints.Count == 0 || !m.Waypoints[0].Equals(u.Waypoints[0])) continue;
                var mp = SoftSearch(m, hx, hy, false, out _);
                if (mp != null) ShortcutTo(m, mp, gx, gy);
                m.PC.shortTodo = false;
            }
        }

        void ShortcutTo(ZObject o, List<(int x, int y)> prefix, int gx, int gy)
        {
            var list = new List<(int x, int y)>(prefix);
            if (list.Count == 1) list.Insert(0, (o.CenterX, o.CenterY));
            o.PathSetPoints(list);
            var c = o.PC; c.softGoalWp = o.Waypoints[0]; c.softGoalX = gx; c.softGoalY = gy; c.softHoldSince = -1;
            o.PS.lastProgress = W.Time; o.PS.rebase = true;
        }

        /// <summary>PrepareStartPathCore (DV47): the goal (tx, ty) is out of reach and resolved to (rx, ry). When that point lies in / at a
        /// tight place near soft debris, the unit notes the real goal: arrived at (rx, ry) it waits there with its order (SoftGoalHold) and
        /// the unstick rule plans the way over the debris FROM WHERE IT STANDS after UnstickAfter s.</summary>
        void SoftGoalNote(ZObject o, int tx, int ty, int rx, int ry)
        {
            var c = o.PC; c.softGoalWp = ZWaypoint.Cleared; c.softHoldSince = -1;
            if (!CommitOn || !UnstickOn || o.Waypoints.Count == 0 || !o.IsMobile) return;
            var wp = o.Waypoints[0];
            if (wp.mode != WpMode.Move || HelperMove(wp)) return;
            EnsureZoneMap();
            if (softTiles.Count == 0 || !TightAt(rx, ry) || !SoftNearPoint(rx, ry, SoftReach)) return;
            c.softGoalWp = wp; c.softGoalX = tx; c.softGoalY = ty;
            Log(o, $"DV47: its goal ({tx},{ty}) is out of reach, debris near ({rx},{ry}): it waits there, then tries the way over it");
        }

        /// <summary>ZObject.ProcessMoveWP (DV47): arrived at the point short of a goal behind debris (or at the debris of a soft shortcut), the
        /// unit keeps its order and stands (the progress clock runs: the unstick takes it over after UnstickAfter s); after UnstickAfter + 3 s
        /// without a way over the debris the order ends there like live.</summary>
        public bool SoftGoalHold(ZObject o)
        {
            if (!Enabled || !CommitOn || o.Waypoints.Count == 0) return false;
            var c = o.PC;
            if (!o.Waypoints[0].Equals(c.softGoalWp)) return false;
            if (c.softHoldSince < 0) { c.softHoldSince = W.Time; Log(o, "DV47: at the debris short of its goal: waits"); }
            if (W.Time - c.softHoldSince > UnstickAfter + 3.0) { c.softGoalWp = ZWaypoint.Cleared; c.softHoldSince = -1; Log(o, "DV47: no way over the debris: the order ends here"); return false; }
            o.StopMove();
            return true;
        }

        /// <summary>ZStuckGuard.Monitor (DV47): a unit waiting at debris short of its goal is not stuck (an orderly wait).</summary>
        internal bool SoftGoalHolding(ZObject o) => CommitOn && o.PC.softHoldSince >= 0 && o.Waypoints.Count > 0 && o.Waypoints[0].Equals(o.PC.softGoalWp);

        /// <summary>DV47: a unit left standing on a soft tile (its order ended there) walks to the nearest free tile next to it (a step-aside
        /// MOVE, never player-given).</summary>
        void StepOffSoft(ZObject u)
        {
            bool robot = u.IsRobot, nr = NoRocks(u); int comp = Reach.UnitComp(u, nr);
            var (tx0, ty0) = ZReach.FootTile(u.LocX, u.LocY);
            int bx = int.MinValue, by = 0; long best = long.MaxValue;
            for (int ty = ty0 - 2; ty <= ty0 + 2; ty++)
                for (int tx = tx0 - 2; tx <= tx0 + 2; tx++)
                {
                    var (x, y) = ZReach.FootCentre(tx, ty, robot);
                    long d = (long)(x - u.CenterX) * (x - u.CenterX) + (long)(y - u.CenterY) * (y - u.CenterY);
                    if (d >= best || (comp >= 0 && !Reach.PointOk(x, y, robot, false, comp)) || !Reach.BoxFree(x - (u.WidthPix >> 1), y - (u.HeightPix >> 1), u.WidthPix, u.HeightPix, robot, false) || Occupied(x, y, u, null)) continue;
                    best = d; bx = x; by = y;
                }
            if (bx == int.MinValue) return;
            u.Waypoints.Add(ZWaypoint.Move(bx, by, false, RefStepAside));
            u.PC.softWp = u.Waypoints[0]; u.PC.softUntil = W.Time + UnstickHold;              // (the permission carries it off the tile)
            Log(u, $"DV47: stopped on soft debris: steps off to ({bx},{by})");
        }

        readonly Dictionary<ZLane, bool> softLaneMemo = new Dictionary<ZLane, bool>();
        /// <summary>ZLanes.LaneConflict (DV47): a unit with the soft pass is not held at a lane whose keep-clear area holds soft debris (the
        /// debris is its second lane: it goes over it beside the traffic in the lane; the gate keeps the spacing).</summary>
        internal bool SoftLaneFree(ZObject a, ZLane L)
        {
            if (!CommitOn || !a.PC.softPass || softTiles.Count == 0) return false;
            if (!softLaneMemo.TryGetValue(L, out bool has))
            {
                has = false;
                for (int j = L.KY0 >> 4; j <= (L.KY1 - 1) >> 4 && !has; j++) for (int i = L.KX0 >> 4; i <= (L.KX1 - 1) >> 4; i++) if (softTiles.Contains(j * zw + i)) { has = true; break; }
                softLaneMemo[L] = has;
            }
            return has;
        }

        /// <summary>ZLanes.LaneConflict (v3): a zoned unit runs the live rules - DV44's one-tile-long chokepoint lanes (ZCommit.Lanes.cs) do not
        /// hold it (the live layer has no lane there; one rule set per encounter).</summary>
        internal bool ShortLaneOff(ZObject a, ZLane L) => L.Short && (Zoned(a) || LaneLive(L));

        /// <summary>Pathing v3 (one rule set per encounter): the lane's corridor lies in a static zone or in a bottleneck passage that is active
        /// now (opposing traffic) - its live queue / alternation rules hold for everybody there: DV44's squad-lane rules (a squad passes as
        /// one, ZCommit.Lanes.cs) and its one-tile "short lanes" do not apply.</summary>
        internal bool LaneLive(ZLane L)
        {
            if (!ZonesOn || zR == null) return false;
            double now = W.Time;
            for (int j = L.Y0 >> 4; j <= (L.Y1 - 1) >> 4; j++)
                for (int i = L.X0 >> 4; i <= (L.X1 - 1) >> 4; i++)
                {
                    int t = ZIdx(i, j); if (t < 0) continue;
                    if (((zR[t] | zV[t]) & ZInner) != 0) return true;
                    if (nNear != null && nNear[t] >= 0 && nDist[t] <= 1 && nActive[nNear[t]] >= now) return true;
                }
            return false;
        }

        /// <summary>GateCore (MateQueue, review round 2): a zoned minion blocked by a member of its own squad on the same order queues behind it
        /// (a shortened step or a wait) instead of the live side-step / shove / displace round its own squad: DV44's within-squad gating; the
        /// live rules stay for every other pair. A mate that stands MateHoldTicks in its way is gone round (no dead lock).</summary>
        internal bool ZonedMateQueue(ZObject a, ZObject b)
        {
            if (!CommitOn || !ZonesOn || !R3(64) || !Zoned(a) || !a.IsMinion || !SameSquad(a, b) || a.Waypoints.Count == 0 || b.Waypoints.Count == 0 || !SmoothFor(a)) return false;
            var aw = a.Waypoints[0];
            if (aw.mode != WpMode.Move || HelperMove(aw) || HelperMove(b.Waypoints[0])) return false;
            if (!b.Waypoints[0].Equals(aw) && !(b == a.Leader && a.Leader.Waypoints[0].Equals(aw))) return false;
            var c = a.PC;
            if (W.Time < c.mateFreeUntil) return false;
            if (a.PS.heldTicks >= MateHoldTicks && a.PS.heldBy == b && (!Moving(b) || b.PathHeld)) { c.mateFreeUntil = W.Time + 1.0; return false; }
            return true;
        }

        // ------------------------------------------------------------------ review round 2 (issue 3): the live gate's resume hysteresis for zoned units
        /// <summary>A zoned unit the live gate held behind unit b (HoldNote) stays held until the gap to b has opened by ResumeGap px over the
        /// gap when it was held, b is no longer ahead of it (along its heading), or ResumeHoldMax s have passed: no stop-go stutter behind
        /// the unit ahead in a passage (the live gate let it step on every tick the step was clear). Outside the zones / v3 off: nothing.</summary>
        internal bool ResumeHold(ZObject a)
        {
            var c = a.PC;
            if (c.holdBy < 0) return false;
            if (!CommitOn || !ZonesOn || !R2(1024) || !ResumeScope(a)) { c.holdBy = -1; return false; }
            var b = W.Get(c.holdBy);
            bool wide = TestResumeScope > 0;
            if (b == null || b.Destroyed || b.KillAt >= 0 || W.Time - c.holdSince > (wide ? ResumeAnyMax : ResumeHoldMax)) { c.holdBy = -1; return false; }
            double dx = CX(b) - CX(a), dy = CY(b) - CY(a), d = Math.Sqrt(dx * dx + dy * dy);
            bool ahead = dx * a.Dx + dy * a.Dy > 0;
            bool minStand = wide && W.Time - c.holdSince < ResumeAnyMin;                 // (A/B ResumeAnyMin: a held unit stands at least that long while the unit is ahead)
            if (!ahead || (!wide && !SameWay(a, b)) || (!minStand && d >= c.holdD + (wide ? ResumeAnyGap : ResumeGap(a)))) { c.holdBy = -1; return false; }
            Hold(a, ZPathWait.Held, b);
            ResumeHolds++;
            return true;
        }
        /// <summary>GateCore held zoned unit a behind b: the resume hysteresis starts (a hold by the same unit keeps its start).</summary>
        internal void HoldNote(ZObject a, ZObject b)
        {
            if (!CommitOn || !ZonesOn || !R2(1024) || b == null || !ResumeScope(a)) return;
            if (TestResumeScope == 0 && (!SameWay(a, b) || (TestResumeMatesOnly && !SameSquad(a, b)))) return;
            if (TestResumeScope == 7 && SameSquad(a, b) && !a.PC.passActive && !a.PC.encOn) return;   // (7: behind its own squad only in head-on traffic)
            if (TestResumeScope == 8 && SameSquad(a, b) && !OtherTrafficNear(a)) return;             // (8: behind its own squad only with other traffic within ResumeTrafficReach)
            var c = a.PC;
            if (c.holdBy == b.Id) return;
            c.holdBy = b.Id; c.holdSince = W.Time; c.holdD = Math.Sqrt((CX(b) - CX(a)) * (CX(b) - CX(a)) + (CY(b) - CY(a)) * (CY(b) - CY(a)));
        }
        /// <summary>b has an order and heads the same way as a (its intended heading within 60 deg of a's): a queue behind it, not head-on
        /// traffic or a unit standing idle (those keep the live gate: step aside, make way).</summary>
        static bool SameWay(ZObject a, ZObject b)
        {
            if (b.Waypoints.Count == 0) return false;
            double al = Math.Sqrt((double)a.Dx * a.Dx + (double)a.Dy * a.Dy), bl = Math.Sqrt((double)b.Dx * b.Dx + (double)b.Dy * b.Dy);
            return al > 1e-6 && bl > 1e-6 && (a.Dx * b.Dx + a.Dy * b.Dy) > 0.5 * al * bl;
        }
        static double ResumeGap(ZObject a) => Math.Min(2 * Radius(a), ResumeGapMax);
        /// <summary>Resume hysteresis: the gap must open by a body length (at most ResumeGapMax px); held at most ResumeHoldMax s.</summary>
        public const double ResumeGapMax = 16, ResumeHoldMax = 1.0;
        /// <summary>(A/B) the resume hysteresis (TestR2 bit 1024) only behind a mate of its own squad; for units outside the zones too.</summary>
        internal static bool TestResumeMatesOnly = true, TestResumeAll = false;
        /// <summary>Review round 2 (A/B): who gets the resume hysteresis - 0 zoned units behind a squad mate heading the same way, 1 zoned units
        /// behind any unit, 2 any unit in a tight place behind any unit (open ground: never).</summary>
        internal static int TestResumeScope = 8;
        internal static double ResumeAnyGap = 8, ResumeAnyMax = 0.6, ResumeAnyMin = 0;
        readonly List<ZObject> resumeNear = new List<ZObject>();
        /// <summary>Review round 2 (resume scope 8): a unit of another squad / group with an order within ResumeTrafficReach px of a (traffic, not
        /// one squad on its own through a squeeze).</summary>
        bool OtherTrafficNear(ZObject a)
        {
            Near(a.CenterX, a.CenterY, (int)ResumeTrafficReach, resumeNear);
            foreach (var v in resumeNear) if (v != a && v.IsMobile && !SameSquad(a, v) && v.Waypoints.Count > 0 && !Static(v)) return true;
            return false;
        }
        public static double ResumeTrafficReach = 160;
        /// <summary>Review round 2 (R3 4194304, 8388608): the head-on smoothing (MateQueue, the resume hysteresis, HandoverAhead) is for units
        /// zoned by a passage, a squeeze, an encounter or their squad - not for one zoned only by its live group order, nor in a static zone
        /// (a bridge, a water crossing, a gate): those keep the live rules exactly (round 2's rules; the stress suite's bridge rows).</summary>
        internal bool SmoothFor(ZObject a) => (!R3(4194304) || (a.PC.zwhy & 8) == 0 || (a.PC.zwhy & ~(8 | 64)) != 0)
                                              && (!R3(8388608) || !a.PC.zoneOwn)                                       // (R3 8388608: nor in a static zone: bridges, water crossings, gates keep the live rules exactly)
                                              && (!R4(2) || !StaticBandAt(a))                                          // review round 3: nor on a bridge / water / gate zone tile or its band, whatever else zoned it
                                              && (!R4(32) || a.PC.oppNow || a.PC.oppSquad || (R4(1024) && (a.PC.zwhy & 2) != 0 && SqueezeNear(a)))   // (A/B R4 32: only for opposing traffic near - not adopted, L02 grunts vs tanks 116 stop-go)
                                              && (!R4(8192) || W.Time - a.PC.headLast <= ZonedHold + 1e-6);   // review round 3: only with a head-on reason (an active passage / squeeze, an encounter - its own or its squad's, ZonedHold s on): one-way, live group order only: the live rules
        bool ResumeScope(ZObject a) => !SmoothFor(a) ? false : TestResumeScope == 7 || TestResumeScope == 8 ? Zoned(a)
                                     : TestResumeScope == 6 ? Zoned(a) && !(a.PC.passOwn && !a.PC.passActive && !a.PC.encOn && !a.PC.zoneOwn)   // (6: every zoned unit but one zoned by a one-way squeeze only)
                                     : TestResumeScope >= 3 ? (Zoned(a) && (a.PC.passActive || a.PC.encOn)) || (TestResumeScope == 4 && !a.IsRobot) || (TestResumeScope == 5 && !a.IsRobot && a.PC.tightNow)
                                     : TestResumeScope == 2 ? a.PC.tightNow : (Zoned(a) || TestResumeAll);
        /// <summary>Checks: ticks a zoned unit was kept standing by the resume hysteresis.</summary>
        public int ResumeHolds { get; private set; }

        /// <summary>Stress watch (DV47): the unit's box may touch this blocked tile - a soft blocker it has the soft pass for.</summary>
        public bool SoftPassTile(ZObject u, int tx, int ty) => u != null && CommitOn && u.PC.softPass && SoftUsable(tx, ty, u.IsRobot);

        /// <summary>Checks (stress watch, Play proof): the unit stands on a soft tile it is allowed on (DV47).</summary>
        public bool SoftPassOn(ZObject o) => o != null && CommitOn && o.PC.softPass && BoxOnSoft(o);
    }

    public partial class ZObject
    {
        /// <summary>DV47: this unit may step over soft blockers right now (the view skips the soft tiles in its prop push-out).</summary>
        public bool PathSoftPass => PC.softPass && World?.Pathing != null && World.Pathing.CommitOn;
        /// <summary>Pathing v3: this unit (its squad) runs the live pathing rules now (a bridge / bottleneck zone, an encounter there).</summary>
        public bool PathZoned => World?.Pathing != null && World.Pathing.Zoned(this);
        /// <summary>Checks / traces (review round 2): why the unit is zoned (1 tile / way, 2 active passage, 4 encounter, 8 live group order, 16 its squad, 32 A/B, 64 the hold, 128 one-way passage / DV47 live hand-off, 256 an exit FORCE_MOVE).</summary>
        public int PathZoneWhy => PathZoned ? PC.zwhy : 0;
        /// <summary>Checks / traces: the unit stood in a tight place at its last zone update (v3).</summary>
        public bool PathTightNow => PC.tightNow;
        /// <summary>Checks (review round 2): the kind of tile under the unit at its last zone update on its own grid (1 zone, 2 band, 4 tight place, 8 passage).</summary>
        public int PathZoneTileClass => PC.zCls;
        /// <summary>Checks (editor probes): the unit is in Zod's scripted building move (crane / repair / fort: it drives through walls by design).</summary>
        public bool PathScriptedMove => PathInBuildingStage;
        /// <summary>Pathing v3 (review round 2): the unit's path is planned for this waypoint (Zod's waypoint start ran for it, no answer pending).</summary>
        internal bool PathPlannedFor(ZWaypoint wp) => lastWp.Equals(wp) && cur.pathFindingId == 0;

        /// <summary>ZFormation.FormationStep (v3): a minion that followed its leader's trail without a path of its own, its squad now zoned:
        /// it gets its own Zod path from where it stands NOW and walks on (no stop; its first leg without a step back), then the live DV24
        /// step runs (its V slot, or single file on that path). True = handled this tick (the round-1 hand-over: stop, Zod re-path next tick).</summary>
        internal bool FollowHandover()
        {
            var c = PC;
            if (!c.noPath) { if (c.followMode != 3 && c.followMode != 6) c.followMode = 0; return false; }
            c.noPath = false; c.followMode = 0; c.standing = false; formOn = false;
            var P = World.Pathing;
            P.CountHandover();
            if (ZPathing.TestHandoverWalk && HandoverWalk()) { P.LogPublic(this, "v3: its squad is zoned: own Zod path from here, walks on (live rules)"); return false; }
            StopMove(); PathReissue();
            P.LogPublic(this, "v3: its squad is in a bridge / bottleneck zone: own Zod path (live rules)");
            return true;
        }

        /// <summary>The hand-over path: Zod's Find_Path from where the unit stands to its order's goal (the layer's goal resolution first), the
        /// layer's leg check, DV44's no-step-back on its ends; taken at once (Zod waits one tick for its path thread and stops the unit
        /// meanwhile - here it walks on). False = no move order (nothing done).</summary>
        bool HandoverWalk()
        {
            if (Waypoints.Count == 0 || Waypoints[0].mode != WpMode.Move) return false;
            var wp = Waypoints[0];
            var P = World.Pathing;
            SetTarget(wp.x, wp.y);
            if (!P.PrepareStartPath(this)) return true;                                     // unreachable: the layer's give-up runs next tick (it stopped)
            var tp = ZPathing.R3(256) && !(ZPathing.R4(512) && P.HandoverZod(this)) ? P.TrailHandoverPath(this) : null;   // (review round 3: on a bridge / water / gate zone or its band, or at its order's start: Zod's path, the live hand-over)
            if (tp != null)
            {                                                                            // review round 2: on along its leader's way (the trail, then his path)
                cur.pathFindingId = 0; cur.gotPfResponse = true;
                cur.pfPoints = tp; NextPathPoint();
                PS.lastProgress = World.Time; PS.rebase = true;
                return true;
            }
            int id = World.Path.FindPath(LocX + 8, LocY + 8, cur.x, cur.y, IsRobot, HasExplosives(), out var pts);
            cur.pathFindingId = 0; cur.gotPfResponse = true;
            if (id != 0 && pts != null && pts.Count > 0)
            {
                pts = P.CheckPath(this, pts);
                pts = P.HandoverNoBackSteps(this, pts);
            }
            if (id == 0 || pts == null || pts.Count == 0) { cur.pfPoints = new List<(int x, int y)>(); SetVelocity(); return true; }   // Zod's straight line
            cur.pfPoints = pts;
            NextPathPoint();
            PS.lastProgress = World.Time; PS.rebase = true;
            return true;
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>Checks / Play proof only (never in a match): a neutral wreck map item (DV39's type, destroyable, a soft blocker for DV47)
        /// on free ground tiles tx..tx+w-1, ty..ty+h-1. Null when a tile is not free ground / water or an object is there.</summary>
        public ZObject AddTestWreck(int tx, int ty, int w, int h)
        {
            for (int i = tx; i < tx + w; i++)
                for (int j = ty; j < ty + h; j++)
                {
                    var c = Map.ClassAt(i, j);
                    if ((c != ZClass.Ground && c != ZClass.Road && c != ZClass.Water) || !Path.TilePassable(i, j, true)) return null;
                }
            var probe = new ZObject { WidthPix = w * 16, HeightPix = h * 16 }; probe.SetCords(tx * 16, ty * 16);
            foreach (var o in Objects)
            {                                                                            // (a unit, a building or a blocking map item there: no room)
                if (o.Destroyed || !probe.IntersectsObject(o)) continue;
                if (o.IsUnit || o.Kind == ZKind.Building) return null;
                for (int i = tx; i < tx + w; i++) for (int j = ty; j < ty + h; j++) if (o.CausesImpassAtCoord(i * 16 + 8, j * 16 + 8)) return null;
            }
            var wk = CreateObject("map_item", WreckType, tx * 16, ty * 16, Team.Neutral);
            wk.WidthPix = w * 16; wk.HeightPix = h * 16; wk.SetCords(tx * 16, ty * 16);
            wk.IsDestroyableImpass = true;
            wk.MaxHealth = wk.Health = (int)(S.map_item_health * ZConst.MAX_UNIT_HEALTH);
            SetMapImpassables(wk);
            return wk;
        }
    }
}
