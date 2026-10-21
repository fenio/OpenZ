using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>A one-lane bridge crossing of the pathing layer's bridge queue: one deck, or several decks on one line with a
    /// short island between them (L02: (26,44) + (41,44)), which share one reservation (else a unit holding the first deck
    /// waits for the second while the other way holds the second and waits for the first).</summary>
    public sealed class ZBridgeLane
    {
        internal readonly List<ZLane> Decks = new List<ZLane>();       // deck corridors, approach zones, keep-clear areas (ZLanes.cs), along the axis
        internal readonly List<ZObject> Objects = new List<ZObject>(); // the bridge building of each deck
        public bool Vertical;
        public ZObject Bridge => Objects[0];
        public IReadOnlyList<ZObject> BridgeObjects => Objects;
        /// <summary>Direction the crossing is reserved for: -1 free, 0 = travel along +axis (entered at the low end), 1 = -axis.</summary>
        public int Dir = -1;
        internal readonly List<ZObject> holders = new List<ZObject>();                          // admitted, not yet off the far approach
        internal readonly List<ZBridgeUnit>[] queue = { new List<ZBridgeUnit>(), new List<ZBridgeUnit>() };   // [direction] waiting, oldest first
        internal int turnCount; internal double lastServed = -100, exitBlockedSince = -1; internal ZObject lastGranted;
        // statistics (stress test / debug overlay)
        public int Admitted, Queued, Switches, Replans, GiveUps, Revokes, Evacuations, RobotWades, RobotBridge, MaxQueue;
        public double MaxWait;
        public IReadOnlyList<ZObject> Holders => holders;
        /// <summary>Last time a unit holding the crossing moved (the queue is being served).</summary>
        public double LastServed => lastServed;
        public int Waiting => queue[0].Count + queue[1].Count;
        // extent along the axis (px): approach zones and keep-clear areas of the outer decks
        internal double Z0 => Vertical ? Decks[0].ZY0 : Decks[0].ZX0;
        internal double Z1 => Vertical ? Decks[Decks.Count - 1].ZY1 : Decks[Decks.Count - 1].ZX1;
        public override string ToString()
        {
            var s = "bridge ";
            for (int i = 0; i < Objects.Count; i++) s += (i > 0 ? "+" : "") + $"({Objects[i].LocX / 16},{Objects[i].LocY / 16})";
            return s + (Vertical ? " N-S" : " E-W");
        }
    }

    /// <summary>Per-unit bridge queue state (ZPathState.bridge).</summary>
    public sealed class ZBridgeUnit
    {
        internal ZObject o;
        internal ZBridgeLane queued;            // waiting for this crossing (not admitted yet)
        internal int end; internal ZLane deck; internal double since; internal ZWaypoint order;   // direction, first deck on its way, order
        internal double nextBayTry; internal int bayX, bayY; internal bool hasBay;
        internal bool replanTried;
        internal double grantedAt = -100;
        internal ZBridgeLane avoid; internal ZWaypoint avoidWp;   // this order routes round that crossing (robots wade, vehicles re-planned)
        internal ZBridgeLane lastChoice; internal ZWaypoint lastChoiceWp; internal bool lastChoiceWade;   // robot decision per order (minions copy the leader)
        internal double idleOnDeckSince = -1;
        internal int wadeTries; internal double noCrossSince = -1;
        internal ZWaypoint replannedWp = ZWaypoint.Cleared;                                    // the order already re-planned round a queue once
        internal double nextReverseTry = -1;
        internal double stillX, stillY, stillSince;                                             // queued unit: where / since when it has not moved
        internal double nextRealign = -1, nextYield = -1;
        internal double jamX, jamY, jamSince;                                                   // dead-jam last resort (UnstickDeadJams)
    }

    /// <summary>
    /// A Zod bridge deck is 2 tiles between impassable rails (bbridge.cpp:168-190): one vehicle wide.
    /// Zod lets units drive through each other (research 7.4), so it never needed a rule; with sim-side spacing two columns
    /// meeting on a deck jam. Deliberate OpenZ change (parity report DV8); speeds and Zod's paths are untouched:
    ///  - One direction at a time. A vehicle whose path runs over a deck within RequestLook px and that would not be let on
    ///    queues right there; every unit asks again where it enters the bridge's keep-clear area (5.5 tiles before the deck).
    ///    It is let on when the crossing is free or already reserved for its direction, nobody at its end waited longer, the
    ///    other end has not waited long (ConvoyGrace, at most MaxConvoy per turn) and the way out at the far end is clear (a
    ///    box junction rule). Admitted units hold the reservation until their box has left the far approach.
    ///  - Waiting vehicles go to a bay BESIDE the road near where they are: on their own bank, outside every keep-clear area
    ///    and every approach band (3.5 tiles either side of the axis, 12 tiles out), off the road when possible. When the deck
    ///    is free, the end whose first unit has waited longest gets it (longest wait first).
    ///  - Robots do not need a bridge: Zod lets them wade (water 0.7x speed). Robots never queue for a deck unless it is
    ///    clearly faster: a squad leader (its minions copy the choice) whose path runs over a busy deck (reserved the other
    ///    way, or with a queue) wades instead unless the deck is clearly faster (RobotBridgeAdvantage, time-weighted A*: road
    ///    1/1.689, water 1/0.7); over a free deck Zod's path stays unless wading is faster anyway; a robot refused at a deck
    ///    re-plans to wade at once. Wading robots keep right and off the keep-clear area. On a deck robots walk its centre
    ///    line: a squad crosses single file, then re-forms (regroup / follow-through).
    ///  - Waiting too long re-plans: a vehicle queued VehicleReplanAfter s takes another way when it is not slower than the
    ///    time already waited; after BridgeGiveUpAfter s its order resolves to the nearest reachable spot on its side.
    ///  - Safety nets: a granted unit that never gets onto the deck is re-queued (GrantTimeout); a unit idle on a deck is
    ///    sent off it; idle units in the way out are sent aside; a destroyed bridge drops its queue (units re-path; ZReach
    ///    resolves the far side). Helper moves never cross a deck and never pick a spot across water.
    /// BridgeQueueEnabled = false restores the older lane rule for bridges (ZLanes.LaneConflict).
    /// </summary>
    public sealed partial class ZPathing
    {
        public static bool BridgeQueueEnabled = true;
        public const int RefBay = -5;                           // MOVE to a waiting bay (never player-given)
        public const int MaxConvoy = 4;                         // units admitted per turn while the other end waits
        public const double ConvoyGrace = 8;                    // s the other end may wait before this end stops admitting
        public const double GrantTimeout = 25;                  // s a granted unit may take to get onto the deck
        public const double VehicleReplanAfter = 30;            // s queued before looking for another way
        public const double BridgeGiveUpAfter = 180;            // s queued before the order resolves to this side
        public const double RobotBridgeAdvantage = 1.35;        // robots keep a bridge path only if wading costs > 1.35x + 3 tiles
        public const int RobotBridgeMargin = 300;               // (cost units: 100 = one ground tile)
        public const double RequestLook = 176;                  // px of path ahead: a vehicle that would be refused queues this early
        public const double ExitBlockedGrace = 6;               // s a free crossing waits for its way out before the next unit goes anyway
        const int BayLateral = 56, BayLateralMin = 40, BayBand = 12 * 16, BayRadius = 9, ChainGap = 16 * 16, FloodRadius = 20;

        readonly List<ZBridgeLane> bridges = new List<ZBridgeLane>();
        int bridgesVersion = int.MinValue;
        public IReadOnlyList<ZBridgeLane> BridgeLanes => bridges;

        internal static bool BridgeHelperRef(int r) => r == RefBay || (r <= RefStepAside && r >= RefRegroup);
        static bool OnHelper(ZObject o) => o.Waypoints.Count > 0 && BridgeHelperRef(o.Waypoints[0].refId);
        static bool OnBay(ZObject o) => o.Waypoints.Count > 0 && o.Waypoints[0].refId == RefBay;

        /// <summary>The bridge (the first deck on its way) this unit waits for; null = not in a bridge queue. Tests / overlay.</summary>
        public ZObject BridgeQueuedAt(ZObject o)
        {
            if (!Enabled || !BridgeQueueEnabled) return null;
            var q = o.PS.bridge; if (q.queued == null) return null;
            int i = q.queued.Decks.IndexOf(q.deck);
            return q.queued.Objects[i >= 0 ? i : 0];
        }
        /// <summary>Waiting in a bridge queue (not admitted): an orderly wait, never "stuck".</summary>
        internal bool BridgeWaitOk(ZObject o) => BridgeQueueEnabled && o.PS.bridge.queued != null;
        /// <summary>Stands still waiting for a bridge turn (bay or keep edge): for detours it is a parked unit.</summary>
        internal bool BridgeParked(ZObject o) => BridgeQueueEnabled && o.PS.bridge.queued != null && !OnHelper(o);

        public string BridgeQueueStats()
        {
            var parts = new List<string>();
            foreach (var b in bridges)
                parts.Add($"{b}: admitted {b.Admitted}, queued {b.Queued}, direction turns {b.Switches}, max queue {b.MaxQueue}, max queue wait {b.MaxWait:F0} s, re-plans {b.Replans}, give-ups {b.GiveUps}, revoked {b.Revokes}, deck evacuations {b.Evacuations}, robot orders wade/bridge {b.RobotWades}/{b.RobotBridge}");
            return parts.Count == 0 ? "no bridge lanes" : string.Join("; ", parts);
        }

        /// <summary>Debug: reservation, holders, queue heads and what blocks the way out for each head.</summary>
        public string BridgeState(ZBridgeLane b)
        {
            var sb = new System.Text.StringBuilder($"{b}: dir {b.Dir}, turn {b.turnCount}, served {W.Time - b.lastServed:F1} s ago; holders:");
            foreach (var h in b.holders) sb.Append($" {h.Type}#{h.Id}[{h.PS.wait} mv={Moving(h)} wps={h.Waypoints.Count} ({h.CenterX >> 4},{h.CenterY >> 4})]");
            for (int d = 0; d < 2; d++)
            {
                sb.Append($"; queue {d}:");
                foreach (var q in b.queue[d]) sb.Append($" {q.o.Type}#{q.o.Id}({W.Time - q.since:F0}s)");
                if (b.queue[d].Count > 0)
                {
                    var hq = b.queue[d][0]; var blockers = new List<string>();
                    double at = AlongOf(b, CX(hq.o), CY(hq.o));
                    foreach (var u in units)
                    {
                        if (u == hq.o || !Separable(u) || !InKeepAny(b, CX(u), CY(u)) || InCorridorAny(b, u.LocX + u.XOver, u.LocY + u.YOver, u.WidthPix, u.HeightPix)) continue;
                        double along = AlongOf(b, CX(u), CY(u));
                        if (d == 0 ? along <= at : along >= at) continue;
                        if (b.holders.Contains(u) && Moving(u) && u.PS.wait != ZPathWait.Held && u.PS.wait != ZPathWait.Bridge) continue;
                        if (Moving(u) && !u.PathHeld && !PathUsesAny(b, u, NearLook)) continue;
                        blockers.Add($"{u.Type}#{u.Id}[{u.PS.wait} wps={u.Waypoints.Count}{(u.Waypoints.Count > 0 ? " ref" + u.Waypoints[0].refId : "")} ({u.CenterX >> 4},{u.CenterY >> 4})]");
                    }
                    sb.Append($" | head exit blocked by: {(blockers.Count == 0 ? "-" : string.Join(" ", blockers))}; convoy ok {ConvoyOk(b, hq.since, b.queue[1 - d].Count > 0 ? b.queue[1 - d][0] : null)}, previous entered {PreviousEntered(b)}");
                }
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ geometry over all decks of a crossing
        static bool InZoneAny(ZBridgeLane b, double lx, double ly, int w, int h) { foreach (var L in b.Decks) if (BoxInZone(L, lx, ly, w, h)) return true; return false; }
        static bool InCorridorAny(ZBridgeLane b, double lx, double ly, int w, int h) { foreach (var L in b.Decks) if (BoxInCorridor(L, lx, ly, w, h)) return true; return false; }
        static bool InKeepAny(ZBridgeLane b, double x, double y) { foreach (var L in b.Decks) if (InKeep(L, x, y)) return true; return false; }
        static bool PathUsesAny(ZBridgeLane b, ZObject a, double len) { foreach (var L in b.Decks) if (PathUsesCorridor(L, a, len)) return true; return false; }
        static double AlongOf(ZBridgeLane b, double x, double y) => b.Vertical ? y : x;

        /// <summary>Direction the unit will travel over the crossing: taken at the first deck its path meets.</summary>
        static int CrossDir(ZBridgeLane b, ZObject a)
        {
            if (b.Decks.Count == 1) return EntryDir(b.Decks[0], a);
            double x0 = CX(a), y0 = CY(a), px = a.PathTargetX, py = a.PathTargetY;
            foreach (var L in b.Decks) if (SegmentHits(L, a, x0, y0, px, py) || BoxInZone(L, a.LocX + a.XOver, a.LocY + a.YOver, a.WidthPix, a.HeightPix)) return FirstDeckDir(b, a, x0, y0, px, py);
            if (a.PathPoints != null)
                foreach (var p in a.PathPoints)
                {
                    foreach (var L in b.Decks) if (SegmentHits(L, a, px, py, p.x, p.y)) return FirstDeckDir(b, a, px, py, p.x, p.y);
                    px = p.x; py = p.y;
                }
            double mid = (b.Z0 + b.Z1) * 0.5;
            return AlongOf(b, CX(a), CY(a)) < mid ? 0 : 1;
        }

        static int FirstDeckDir(ZBridgeLane b, ZObject a, double sx, double sy, double ex, double ey)
        {
            double s = b.Vertical ? sy : sx, e = b.Vertical ? ey : ex;
            if (Math.Abs(e - s) > 1) return e > s ? 0 : 1;
            double mid = (b.Z0 + b.Z1) * 0.5;
            return AlongOf(b, CX(a), CY(a)) < mid ? 0 : 1;
        }

        /// <summary>The first deck (in travel direction d) that lies ahead of the unit.</summary>
        static ZLane DeckAhead(ZBridgeLane b, ZObject a, int d)
        {
            double at = AlongOf(b, CX(a), CY(a));
            if (d == 0) { foreach (var L in b.Decks) if ((b.Vertical ? L.Y1 : L.X1) > at) return L; return b.Decks[b.Decks.Count - 1]; }
            for (int i = b.Decks.Count - 1; i >= 0; i--) { var L = b.Decks[i]; if ((b.Vertical ? L.Y0 : L.X0) < at) return L; }
            return b.Decks[0];
        }

        // ------------------------------------------------------------------ lanes -> crossings (BeginTick, after EnsureLanes)
        void EnsureBridges()
        {
            if (bridgesVersion == W.Path.Version) return;
            bridgesVersion = W.Path.Version;
            var old = new List<ZBridgeLane>(bridges);
            bridges.Clear();
            var decks = new List<(ZLane L, ZObject obj)>();
            foreach (var L in Lanes)
            {
                if (!L.Bridge || !L.VehicleLane) continue;
                foreach (var o in W.Objects)
                    if (o.IsBridge && !o.Destroyed && L.X0 < o.LocX + o.WidthPix && o.LocX < L.X1 && L.Y0 < o.LocY + o.HeightPix && o.LocY < L.Y1) { decks.Add((L, o)); break; }
            }
            decks.Sort((p, q) => p.L.Vertical != q.L.Vertical ? (p.L.Vertical ? 1 : -1) : p.L.Vertical ? (p.L.X0 != q.L.X0 ? p.L.X0.CompareTo(q.L.X0) : p.L.Y0.CompareTo(q.L.Y0)) : (p.L.Y0 != q.L.Y0 ? p.L.Y0.CompareTo(q.L.Y0) : p.L.X0.CompareTo(q.L.X0)));
            ZBridgeLane cur = null;
            foreach (var (L, obj) in decks)
            {
                if (cur != null)
                {
                    var last = cur.Decks[cur.Decks.Count - 1];
                    bool line = cur.Vertical == L.Vertical && (L.Vertical ? L.X0 == last.X0 && L.X1 == last.X1 : L.Y0 == last.Y0 && L.Y1 == last.Y1);
                    double gap = L.Vertical ? L.Y0 - last.Y1 : L.X0 - last.X1;
                    if (line && gap >= 0 && gap <= ChainGap) { cur.Decks.Add(L); cur.Objects.Add(obj); continue; }
                }
                cur = new ZBridgeLane { Vertical = L.Vertical }; cur.Decks.Add(L); cur.Objects.Add(obj);
                bridges.Add(cur);
            }
            // keep the state of crossings that did not change (same decks); the others lose their queue
            for (int i = 0; i < bridges.Count; i++)
            {
                var nb = bridges[i];
                foreach (var ob in old)
                {
                    if (ob.Decks.Count != nb.Decks.Count || ob.Vertical != nb.Vertical) continue;
                    bool same = true;
                    for (int k = 0; k < ob.Decks.Count && same; k++)
                    {
                        ZLane a = ob.Decks[k], c = nb.Decks[k];
                        same = ob.Objects[k] == nb.Objects[k] && a.X0 == c.X0 && a.Y0 == c.Y0 && a.X1 == c.X1 && a.Y1 == c.Y1;
                    }
                    if (!same) continue;
                    ob.Decks.Clear(); ob.Decks.AddRange(nb.Decks);
                    foreach (var q in ob.queue[0]) q.deck = Remap(q.deck, ob);
                    foreach (var q in ob.queue[1]) q.deck = Remap(q.deck, ob);
                    bridges[i] = ob; old.Remove(ob);
                    break;
                }
            }
            foreach (var b in old) DropBridge(b);                                               // destroyed (or rebuilt differently): queue dropped
        }

        static ZLane Remap(ZLane oldDeck, ZBridgeLane b)
        {
            foreach (var L in b.Decks) if (oldDeck != null && L.X0 == oldDeck.X0 && L.Y0 == oldDeck.Y0) return L;
            return b.Decks[0];
        }

        void DropBridge(ZBridgeLane b)
        {
            for (int e = 0; e < 2; e++)
                foreach (var q in b.queue[e])
                {
                    q.queued = null;
                    var o = q.o;
                    if (!Separable(o)) continue;
                    if (OnBay(o)) o.Waypoints.RemoveAt(0);
                    o.PS.reissue = true;                                                        // re-path: ZReach resolves the far side / another bridge
                    Log(o, $"{b} gone: queue dropped, re-path");
                }
            b.queue[0].Clear(); b.queue[1].Clear(); b.holders.Clear(); b.Dir = -1;
        }

        // ------------------------------------------------------------------ per tick (BeginTick)
        void UpdateBridges()
        {
            if (!BridgeQueueEnabled) return;
            EnsureBridges();
            if (bridges.Count == 0) return;
            EarlyRequests();
            foreach (var b in bridges) ServeBridge(b);
            UnstickDeadJams();
        }

        void ServeBridge(ZBridgeLane b)
        {
            double now = W.Time;
            // holders: release when off the far approach, out of the zones with no path over a deck, dead, or idle off a deck
            for (int i = b.holders.Count - 1; i >= 0; i--)
            {
                var h = b.holders[i];
                if (!Separable(h) || h.UnmannedHeld) { b.holders.RemoveAt(i); continue; }   // DV79 unmanned stop: nobody inside: no crossing to finish, never driven off the deck; it stands like any parked hull
                double lx = h.LocX + h.XOver, ly = h.LocY + h.YOver;
                bool inZone = InZoneAny(b, lx, ly, h.WidthPix, h.HeightPix), onDeck = InCorridorAny(b, lx, ly, h.WidthPix, h.HeightPix);
                if (h.IsMoving && h.PS.wait != ZPathWait.Held && h.PS.wait != ZPathWait.Bridge) b.lastServed = now;
                var st = h.PS.bridge;
                if (h.Waypoints.Count == 0)
                {
                    if (!onDeck) { b.holders.RemoveAt(i); continue; }
                    if (st.idleOnDeckSince < 0) st.idleOnDeckSince = now;
                    else if (now - st.idleOnDeckSince > 1.0 && EvacuateDeck(b, h)) st.idleOnDeckSince = -1;
                    continue;
                }
                st.idleOnDeckSince = -1;
                if (onDeck && b.Dir >= 0 && !OnHelper(h) && !h.PathPending && now - st.grantedAt > 0.5)
                {                                                                               // a new order turned it round on the deck (a squad sent back while its
                    ZLane on = null;                                                            // last robots were still crossing): alone it takes the deck its way,
                    foreach (var L in b.Decks) if (BoxInCorridor(L, lx, ly, h.WidthPix, h.HeightPix)) { on = L; break; }   // else it drives off the far end first
                    int td = on != null ? TravelDir(on, h) : -1;
                    if (td >= 0 && td != b.Dir)
                    {
                        if (b.holders.Count == 1) { b.Dir = td; b.turnCount = 1; b.Switches++; Log(h, $"{b}: turned round alone on it: the deck is its way now"); }
                        else if (now >= st.nextReverseTry) { st.nextReverseTry = now + 1; FinishCrossing(b, h); }
                    }
                }
                if (inZone) continue;
                if (PastFarSide(b, h, lx, ly)) { b.holders.RemoveAt(i); continue; }
                if (now - st.grantedAt < 3 || h.PathPending) continue;                         // just granted: its path is being re-planned
                if (!PathUsesAny(b, h, 400) && !OnHelper(h)) { b.holders.RemoveAt(i); continue; }
                if ((now - st.grantedAt > GrantTimeout || now - st.grantedAt > 8 && h.PS.wait == ZPathWait.Held) && now - h.PS.lastProgress > 5)
                {                                                                               // never got onto the deck: back in the queue (front)
                    b.holders.RemoveAt(i); b.Revokes++;
                    int e = CrossDir(b, h);
                    var q = st; q.o = h; q.queued = b; q.end = e; q.deck = DeckAhead(b, h, e); q.hasBay = false; q.replanTried = false; q.noCrossSince = -1; q.nextBayTry = now;
                    if (h.Waypoints.Count > 0 && !OnHelper(h)) q.order = h.Waypoints[0];
                    b.queue[e].Insert(Math.Min(1, b.queue[e].Count), q);                        // behind the next one (it may be blocked where it stands)
                    Log(h, $"{b}: granted {now - st.grantedAt:F0} s ago and not on the deck: back in the queue");
                }
            }
            SingleVehicleApproach(b);
            // queues: drop units that died, got a new order or no longer need the deck
            for (int e = 0; e < 2; e++)
                for (int i = b.queue[e].Count - 1; i >= 0; i--)
                {
                    var q = b.queue[e][i]; var o = q.o;
                    bool keep = Separable(o) && q.queued == b && HasOrder(o, q.order);
                    string why = !Separable(o) ? "gone" : q.queued != b ? "moved to another queue" : !keep ? "order changed" : "";
                    if (keep && o.Waypoints[0].Equals(q.order) && !o.PathPending && !PathUsesAny(b, o, 600) && !InKeepAny(b, CX(o), CY(o)))
                    {                                                                           // (for 1.5 s: a restarted order re-plans first)
                        if (q.noCrossSince < 0) q.noCrossSince = now;
                        else if (now - q.noCrossSince > 1.5) { keep = false; why = "its path no longer crosses"; }
                    }
                    else q.noCrossSince = -1;
                    if (!keep)
                    {
                        if (Separable(o)) Log(o, $"{b}: leaves the queue ({why})");
                        b.queue[e].RemoveAt(i);
                        if (q.queued == b) q.queued = null;
                        if (Separable(o) && OnBay(o)) o.Waypoints.RemoveAt(0);
                        continue;
                    }
                    double waited = now - q.since;
                    if (waited > b.MaxWait) b.MaxWait = waited;
                }
            if (b.Waiting > b.MaxQueue) b.MaxQueue = b.Waiting;
            if (b.holders.Count == 0) b.Dir = -1;
            // a queued unit already standing in the mouth on its own side is first in line physically: it goes first (it would
            // block the way out for everybody else)
            for (int e = 0; e < 2; e++)
                for (int i = 1; i < b.queue[e].Count; i++)
                {
                    var q = b.queue[e][i];
                    if (OnHelper(q.o) || !InKeepAny(b, CX(q.o), CY(q.o))) continue;
                    b.queue[e].RemoveAt(i); b.queue[e].Insert(0, q);
                    Log(q.o, $"{b}: stands in the mouth: first in line");
                    break;
                }
            // grants: longest wait first; a running turn goes on while the other end has not waited long
            ZBridgeUnit h0 = b.queue[0].Count > 0 ? b.queue[0][0] : null, h1 = b.queue[1].Count > 0 ? b.queue[1][0] : null;
            if (h0 != null || h1 != null)
            {
                if (b.Dir < 0)
                {                                                                               // the longest waiting head first; if its way out is blocked, the other end's
                    var g = h0 == null ? h1 : h1 == null ? h0 : (h0.since < h1.since || (h0.since == h1.since && h0.o.Id < h1.o.Id)) ? h0 : h1;
                    var g2 = g == h0 ? h1 : h0;
                    if (ExitClear(b, g.end, g.o)) { Grant(b, g); b.exitBlockedSince = -1; }
                    else if (g2 != null && ExitClear(b, g2.end, g2.o)) { Grant(b, g2); b.exitBlockedSince = -1; }
                    else if (b.exitBlockedSince < 0) b.exitBlockedSince = now;
                    else if (now - b.exitBlockedSince > ExitBlockedGrace)
                    {                                                                           // blocked by units that do not clear: go anyway (holders have right of way)
                        b.exitBlockedSince = -1; Grant(b, g);
                        Log(g.o, $"{b}: way out blocked {ExitBlockedGrace:0} s: goes anyway");
                    }
                }
                else
                {
                    int d = b.Dir; ZBridgeUnit hd = b.queue[d].Count > 0 ? b.queue[d][0] : null, ho = b.queue[1 - d].Count > 0 ? b.queue[1 - d][0] : null;
                    if (hd != null && PreviousEntered(b) && ConvoyOk(b, hd.since, ho) && ExitClear(b, d, hd.o)) Grant(b, hd);
                }
            }
            // queued units: bays, re-plans, give-ups
            for (int e = 0; e < 2; e++)
                for (int i = 0; i < b.queue[e].Count; i++)
                {
                    var q = b.queue[e][i]; var o = q.o;
                    if (Math.Abs(CX(o) - q.stillX) + Math.Abs(CY(o) - q.stillY) >= 2) { q.stillX = CX(o); q.stillY = CY(o); q.stillSince = now; }
                    if (OnBay(o) && now - q.stillSince > 3 && now >= q.nextBayTry)
                    {                                                                           // cannot get to its bay (a crowd in the way): pick again from here
                        o.Waypoints.RemoveAt(0); q.hasBay = false;
                        Log(o, $"{b}: cannot reach its bay: picks another spot");
                    }
                    bool inTheWay = !OnHelper(o) && (KeepClearAt(o.CenterX, o.CenterY) != null || InAnyApproachBand(CX(o), CY(o), BayLateralMin));
                    bool ownMouth = inTheWay && InKeepAny(b, CX(o), CY(o)) && i == 0;           // first in line in its own mouth: stays, goes next
                    bool follow = BridgeOneWayFollow && o.Kind == ZKind.Vehicle && !OnBay(o) && OneWay(b, e);
                    if (follow) { }
                    else if (o.Kind == ZKind.Vehicle && (!q.hasBay || inTheWay && !ownMouth) && now >= q.nextBayTry)
                    {
                        q.hasBay = false; AssignBay(b, q);
                        if (!q.hasBay && inTheWay && !ownMouth) StepOutOfKeep(b, q);                // no bay free: at least out of the mouth / approach
                    }
                    else if (inTheWay && !ownMouth && now >= q.nextBayTry) StepOutOfKeep(b, q);     // never wait in a mouth or on an approach
                    double waited = now - q.since;
                    if (!q.replanTried && waited > VehicleReplanAfter) { q.replanTried = true; if (TryReplanAround(b, q, false)) { i--; continue; } }
                    if (waited > BridgeGiveUpAfter) { if (TryReplanAround(b, q, true)) i--; }
                }
        }

        /// <summary>A vehicle whose path runs over a deck within RequestLook px and that would be refused there queues now,
        /// where it is, and waits in a bay near it: waiting vehicles never pile up in the mouth. (One that would be admitted
        /// drives on and asks again at the keep-clear edge.)</summary>
        void EarlyRequests()
        {
            foreach (var o in units)
            {
                if (o.Kind != ZKind.Vehicle || Static(o) || !o.CanMove || o.Waypoints.Count == 0 || o.PathPending || OnHelper(o)) continue;
                var st = o.PS.bridge;
                if (st.queued != null || o.PathInBuildingStage || o.Waypoints[0].mode == WpMode.ForceMove) continue;
                foreach (var b in bridges)
                {
                    if (b.holders.Contains(o) || InKeepAny(b, CX(o), CY(o))) continue;
                    if (st.avoid == b && HasOrder(o, st.avoidWp)) continue;
                    bool hit = false;
                    foreach (var L in b.Decks) if (PathCrossesWithin(L, o, RequestLook)) { hit = true; break; }
                    if (!hit) continue;
                    int d = CrossDir(b, o);
                    if (WouldAdmit(b, o, d)) continue;
                    if (BridgeOneWayFollow && OneWay(b, d)) continue;
                    Enqueue(b, o, d);
                    break;
                }
            }
        }

        /// <summary>
        /// The first plan already takes the deck; the detour was the queue's
        /// waiting bay (beside / behind the road) for a vehicle that only waited behind traffic going ITS way (a group sent over
        /// together, 0.1-4 s turns). With nobody queued at the other end and the deck free or reserved this way, a vehicle queues
        /// in line at the bridge instead (no early queue, no bay, no step-out); the moment the other end has a waiter the bay rules
        /// apply again (the road must be clear for the traffic coming off the deck). Deterministic; false = the old rule.</summary>
        public static bool BridgeOneWayFollow = true;
        bool OneWay(ZBridgeLane b, int d) => b.queue[1 - d].Count == 0 && (b.Dir < 0 || b.Dir == d);

        bool WouldAdmit(ZBridgeLane b, ZObject a, int d)
        {
            var other = b.queue[1 - d].Count > 0 ? b.queue[1 - d][0] : null;
            if (b.Dir < 0) return b.Waiting == 0 && ExitClear(b, d, a);
            if (b.Dir != d || b.queue[d].Count > 0) return false;
            return ConvoyOk(b, W.Time, other) && ExitClear(b, d, a);
        }

        /// <summary>Nobody stands in the way ahead (approaches and keep-clear strips of the crossing beyond the unit in its
        /// direction): a unit only goes onto a deck when it can drive off it again, like a car at a box junction. Idle units
        /// standing there are sent aside.</summary>
        bool ExitClear(ZBridgeLane b, int d, ZObject a)
        {
            bool clear = true;
            double at = AlongOf(b, CX(a), CY(a));
            foreach (var u in units)
            {
                if (u == a || !Separable(u)) continue;
                if (ZWorld.ParkedPathDeviation && Static(u) && !u.Stunned) continue;            // boarding_nb_v1 DV36: a parked unit never clears: no waiting for it (electrobot_v1: a stunned one does)
                double cx = CX(u), cy = CY(u);
                if (!InKeepAny(b, cx, cy) || InCorridorAny(b, u.LocX + u.XOver, u.LocY + u.YOver, u.WidthPix, u.HeightPix)) continue;
                double along = AlongOf(b, cx, cy);
                if (d == 0 ? along <= at : along >= at) continue;                                // behind the unit
                if (b.holders.Contains(u) && Moving(u) && u.PS.wait != ZPathWait.Held && u.PS.wait != ZPathWait.Bridge) continue;   // on its way
                if (Moving(u) && !u.PathHeld && !PathUsesAny(b, u, NearLook)) continue;                                            // passing across
                clear = false;
                if (u.Waypoints.Count == 0 && u.CanMove && !Static(u) && W.Time >= u.PS.nextShove) SendOutOfKeep(b, u);
            }
            return clear;
        }

        /// <summary>An idle unit standing in a bridge's keep-clear area walks to the nearest free spot outside it and outside
        /// every approach band (a Zod ScuffleUnits-style MOVE, never player-given).</summary>
        void SendOutOfKeep(ZBridgeLane b, ZObject u)
        {
            u.PS.nextShove = W.Time + ShoveInterval;
            bool nr = NoRocks(u); int c = Reach.UnitComp(u, nr);
            if (c < 0) return;
            var (tx0, ty0) = ZReach.FootTile(u.LocX, u.LocY);
            for (int ring = 1; ring <= 6; ring++)
                for (int ty = ty0 - ring; ty <= ty0 + ring; ty++)
                    for (int tx = tx0 - ring; tx <= tx0 + ring; tx++)
                    {
                        if (Math.Max(Math.Abs(tx - tx0), Math.Abs(ty - ty0)) != ring) continue;
                        var (x, y) = ZReach.FootCentre(tx, ty, u.IsRobot);
                        if (KeepClearAt(x, y) != null || InAnyApproachBand(x, y, BayLateralMin) || !Reach.PointOk(x, y, u.IsRobot, nr, c) || Occupied(x, y, u, null) || !HelperLegOk(u, x, y)) continue;
                        u.Waypoints.Add(ZWaypoint.Move(x, y, false, RefStepAside));
                        u.PS.Shoves++;
                        Log(u, $"idle in the way out of {b}: steps aside to ({x},{y})");
                        return;
                    }
        }

        /// <summary>A queued unit standing in the keep-clear area (a robot with no wading route, a vehicle with no free bay)
        /// steps out of it (a MOVE in front of its order): it would block the way out it is waiting for.</summary>
        void StepOutOfKeep(ZBridgeLane b, ZBridgeUnit q)
        {
            var u = q.o;
            q.nextBayTry = W.Time + 2;
            if (!u.CanOverwriteWP() || u.Waypoints.Count == 0) return;
            bool nr = NoRocks(u); int c = Reach.UnitComp(u, nr);
            if (c < 0) return;
            var (tx0, ty0) = ZReach.FootTile(u.LocX, u.LocY);
            for (int ring = 1; ring <= 8; ring++)
                for (int ty = ty0 - ring; ty <= ty0 + ring; ty++)
                    for (int tx = tx0 - ring; tx <= tx0 + ring; tx++)
                    {
                        if (Math.Max(Math.Abs(tx - tx0), Math.Abs(ty - ty0)) != ring) continue;
                        var (x, y) = ZReach.FootCentre(tx, ty, u.IsRobot);
                        if (KeepClearAt(x, y) != null || InAnyApproachBand(x, y, BayLateralMin) || !Reach.PointOk(x, y, u.IsRobot, nr, c) || Occupied(x, y, u, null) || !HelperLegOk(u, x, y)) continue;
                        u.Waypoints.Insert(0, ZWaypoint.Move(x, y, false, RefStepAside));
                        u.PS.Shoves++;
                        Log(u, $"{b}: waits outside the mouth at ({x},{y})");
                        return;
                    }
        }

        static bool HasOrder(ZObject o, ZWaypoint order)
        {
            for (int i = 0; i < o.Waypoints.Count && i < 4; i++) if (o.Waypoints[i].Equals(order)) return true;
            return false;
        }

        /// <summary>The unit's box has left the crossing's zones on the far side of its travel direction.</summary>
        static bool PastFarSide(ZBridgeLane b, ZObject h, double lx, double ly)
        {
            double a0 = b.Vertical ? ly : lx, a1 = a0 + (b.Vertical ? h.HeightPix : h.WidthPix);
            return b.Dir == 0 ? a0 >= b.Z1 - 1 : b.Dir == 1 ? a1 <= b.Z0 + 1 : (a0 >= b.Z1 - 1 || a1 <= b.Z0 + 1);
        }

        /// <summary>The unit granted last in this turn is within 6 tiles of the approach it drives onto (or gone).</summary>
        bool PreviousEntered(ZBridgeLane b)
        {
            var g = b.lastGranted;
            if (g == null || !Separable(g) || !b.holders.Contains(g)) return true;
            double along = AlongOf(b, CX(g), CY(g));
            double dist = along < b.Z0 ? b.Z0 - along : along > b.Z1 ? along - b.Z1 : 0;
            return dist <= 96;
        }

        /// <summary>A unit of the reserved direction may still join the turn: the other end is empty, has not waited longer
        /// than this unit, or has waited less than ConvoyGrace while the turn is short.</summary>
        bool ConvoyOk(ZBridgeLane b, double since, ZBridgeUnit other)
        {
            if (other == null) return true;
            if (since <= other.since) return true;
            return W.Time - other.since < ConvoyGrace && b.turnCount < MaxConvoy;
        }

        void Grant(ZBridgeLane b, ZBridgeUnit q)
        {
            var o = q.o;
            b.queue[q.end].Remove(q);
            q.queued = null;
            AddHolder(b, o, q.end);
            if (OnBay(o)) o.Waypoints.RemoveAt(0);                                              // the order resumes from the bay
            Log(o, $"{b}: turn after {W.Time - q.since:F1} s, direction {q.end}");
        }

        void AddHolder(ZBridgeLane b, ZObject o, int d)
        {
            if (b.holders.Count == 0 || b.Dir != d) { b.turnCount = 0; if (b.Dir != d) b.Switches++; }
            b.Dir = d; b.holders.Add(o); b.turnCount++; b.Admitted++; b.lastGranted = o;
            o.PS.bridge.grantedAt = W.Time; o.PS.bridge.idleOnDeckSince = -1;
        }

        /// <summary>Gate: may this unit go onto the deck now (fresh arrival, not queued)?</summary>
        bool TryAdmit(ZBridgeLane b, ZObject a, int d)
        {
            if (b.holders.Contains(a)) return true;
            bool ok = WouldAdmit(b, a, d) ||                                                    // free / this direction, nobody waited longer
                      (a.IsRobot && b.Dir == d && b.queue[d].Count == 0 && SquadMateHolds(b, a));   // a squad crosses as one file
            if (ok) AddHolder(b, a, d);
            return ok;
        }

        static bool SquadMateHolds(ZBridgeLane b, ZObject a)
        {
            foreach (var h in b.holders) if (h != a && SameSquad(a, h)) return true;
            return false;
        }

        void Enqueue(ZBridgeLane b, ZObject a, int d)
        {
            var q = a.PS.bridge; q.o = a;
            if (q.queued == b) return;
            if (q.queued != null) q.queued.queue[q.end].Remove(q);
            q.queued = b; q.end = d; q.deck = DeckAhead(b, a, d); q.since = W.Time; q.order = a.Waypoints[0]; q.noCrossSince = -1;
            q.hasBay = false; q.replanTried = false; q.nextBayTry = W.Time;
            b.queue[d].Add(q); b.Queued++;
            if (b.Waiting > b.MaxQueue) b.MaxQueue = b.Waiting;
            Log(a, $"{b}: reserved {(b.Dir < 0 ? "for the other end's waiters" : b.Dir == d ? "this way but others go first" : "the other way")}: queues for direction {d} ({b.queue[d].Count} waiting)");
        }

        // ------------------------------------------------------------------ gate (ZSeparation.GateCore, before the lane rule)
        /// <summary>False = hold this tick (wait = Bridge).</summary>
        bool BridgeGate(ZObject a, double ox, double oy, double nx, double ny)
        {
            if (!BridgeQueueEnabled || bridges.Count == 0) return true;
            var st = a.PS.bridge; st.o = a;
            if (st.queued != null)
            {
                var qb = st.queued;
                bool onOrder = a.Waypoints.Count > 0 && a.Waypoints[0].Equals(st.order);
                if (!onOrder && !OntoForeignDeck(a, ox, oy, nx, ny)) return true;              // to its bay, stepping aside, a Zod dodge / attack in front (never onto a deck)
                Hold(a, ZPathWait.Bridge, qb.holders.Count > 0 ? qb.holders[0] : null);
                return false;
            }
            double hw = a.WidthPix * 0.5, hh = a.HeightPix * 0.5;
            foreach (var b in bridges)
            {
                bool holder = b.holders.Contains(a);
                foreach (var L in b.Decks)
                {
                    bool zoneEntry = BoxInZone(L, nx, ny, a.WidthPix, a.HeightPix) && !BoxInZone(L, ox, oy, a.WidthPix, a.HeightPix);
                    bool corridorEntry = BoxInCorridor(L, nx, ny, a.WidthPix, a.HeightPix) && !BoxInCorridor(L, ox, oy, a.WidthPix, a.HeightPix);
                    bool keepEntry = !zoneEntry && !corridorEntry && InKeep(L, nx + hw, ny + hh) && !InKeep(L, ox + hw, oy + hh);
                    if (!zoneEntry && !corridorEntry && !keepEntry) continue;
                    if (holder)
                    {                                                                           // one vehicle at a time in the approach: holders never meet abreast at the mouth
                        if (zoneEntry && !corridorEntry)
                        {
                            var ahead = ApproachTaken(b, L, a);
                            if (ahead != null) { Hold(a, ZPathWait.Held, ahead); return false; }
                        }
                        break;
                    }
                    if (OnHelper(a)) { if (corridorEntry) { Hold(a, ZPathWait.Bridge, null); return false; } continue; }   // helper moves stay off decks
                    if (!PathUsesCorridor(L, a, keepEntry ? 160 : NearLook)) continue;           // crossing the approach, not the deck
                    var bs = a.PS.bridge;
                    bool avoiding = bs.avoid == b && a.Waypoints.Count > 0 && bs.avoidWp.Equals(a.Waypoints[0]);
                    if (avoiding && a.IsRobot && !corridorEntry && (a.PS.reissue || a.PathPending)) { Hold(a, ZPathWait.Bridge, null); return false; }   // wade re-plan on its way
                    int d = corridorEntry && !zoneEntry ? TravelDir(L, a) : EntryDir(L, a);
                    if (d < 0) d = EntryDir(L, a);
                    if (TryAdmit(b, a, d)) { holder = true; break; }
                    if (a.IsRobot && a.Waypoints[0].mode != WpMode.ForceMove)
                    {                                                                           // robots do not need the bridge: wade instead
                        if (!avoiding) { SetAvoid(a, b); bs.wadeTries = 0; b.RobotWades++; }
                        if (bs.wadeTries < 3)
                        {                                                                       // (a detour or a clean search may have put it back on the deck)
                            bs.wadeTries++; a.PS.reissue = true;
                            Hold(a, ZPathWait.Bridge, b.holders.Count > 0 ? b.holders[0] : null);
                            Log(a, $"{b} reserved the other way: re-plan to wade ({bs.wadeTries})");
                            return false;
                        }
                    }
                    Enqueue(b, a, d);
                    Hold(a, ZPathWait.Bridge, b.holders.Count > 0 ? b.holders[0] : null);
                    return false;
                }
            }
            return true;
        }

        /// <summary>Two vehicles with a turn side by side in one approach (a sidestep or a push put the second one there): they
        /// would each wait for the other at the one-lane mouth. The one nearer the deck (then the one granted first) goes on,
        /// the others back out of the approach (a MOVE in front of their order) and come in again behind it.</summary>
        void SingleVehicleApproach(ZBridgeLane b)
        {
            foreach (var L in b.Decks)
                for (int e = 0; e < 2; e++)
                {
                    ZObject first = null; int n = 0;
                    foreach (var h in b.holders)
                    {
                        if (h.Kind != ZKind.Vehicle || OnHelper(h)) continue;
                        double lx = h.LocX + h.XOver, ly = h.LocY + h.YOver;
                        if (!BoxInZone(L, lx, ly, h.WidthPix, h.HeightPix) || BoxInCorridor(L, lx, ly, h.WidthPix, h.HeightPix) || EntryDir(L, h) != e) continue;
                        n++;
                        if (first == null || Ahead(L, e, h, first)) first = h;
                    }
                    if (n < 2) continue;
                    foreach (var h in b.holders)
                    {
                        if (h == first || h.Kind != ZKind.Vehicle || OnHelper(h) || W.Time < h.PS.bridge.nextYield) continue;
                        double lx = h.LocX + h.XOver, ly = h.LocY + h.YOver;
                        if (!BoxInZone(L, lx, ly, h.WidthPix, h.HeightPix) || BoxInCorridor(L, lx, ly, h.WidthPix, h.HeightPix) || EntryDir(L, h) != e) continue;
                        h.PS.bridge.nextYield = W.Time + 2;
                        BackOutOfApproach(b, L, e, h, first);
                    }
                }
        }

        static bool Ahead(ZLane L, int e, ZObject a, ZObject c)
        {
            double pa = L.Vertical ? CY(a) : CX(a), pc = L.Vertical ? CY(c) : CX(c);
            if (Math.Abs(pa - pc) > 2) return e == 0 ? pa > pc : pa < pc;                      // nearer the deck
            if (a.PS.bridge.grantedAt != c.PS.bridge.grantedAt) return a.PS.bridge.grantedAt < c.PS.bridge.grantedAt;
            return a.Id < c.Id;
        }

        void BackOutOfApproach(ZBridgeLane b, ZLane L, int e, ZObject h, ZObject first)
        {
            if (!h.CanOverwriteWP() || h.Waypoints.Count == 0) return;
            bool nr = NoRocks(h); int c = Reach.UnitComp(h, nr);
            if (c < 0) return;
            double edge = e == 0 ? (L.Vertical ? L.ZY0 : L.ZX0) : (L.Vertical ? L.ZY1 : L.ZX1);
            double lat = L.Vertical ? CX(h) : CY(h);
            for (int step = 0; step < 4; step++)
                foreach (int off in new[] { 0, 16, -16, 32, -32 })
                {
                    double dd = edge + (e == 0 ? -1 : 1) * (Radius(h) + 8 + step * 16);
                    int x = (int)Math.Round(L.Vertical ? lat + off : dd), y = (int)Math.Round(L.Vertical ? dd : lat + off);
                    if (!Reach.PointOk(x, y, false, nr, c) || Occupied(x, y, h, first) || !HelperLegOk(h, x, y)) continue;
                    h.Waypoints.Insert(0, ZWaypoint.Move(x, y, false, RefBackOff));
                    h.PS.BackOffs++;
                    Log(h, $"{b}: side by side with {first.Type}#{first.Id} at the mouth: backs out to ({x},{y}), then follows");
                    return;
                }
        }

        /// <summary>Another unit with a turn on this crossing stands or drives in this deck's approach (not yet on the deck,
        /// not leaving): a vehicle waits for it outside the approach, a robot only for a vehicle.</summary>
        ZObject ApproachTaken(ZBridgeLane b, ZLane L, ZObject a)
        {
            foreach (var h in b.holders)
            {
                if (h == a || (a.IsRobot && h.IsRobot)) continue;
                double lx = h.LocX + h.XOver, ly = h.LocY + h.YOver;
                if (!BoxInZone(L, lx, ly, h.WidthPix, h.HeightPix) || BoxInCorridor(L, lx, ly, h.WidthPix, h.HeightPix)) continue;
                if (EntryDir(L, h) != EntryDir(L, a)) continue;                                  // at the other end (leaving)
                return h;
            }
            return null;
        }

        /// <summary>The step puts the unit onto a deck of a crossing it holds no turn for.</summary>
        bool OntoForeignDeck(ZObject a, double ox, double oy, double nx, double ny)
        {
            foreach (var b in bridges)
            {
                if (b.holders.Contains(a)) continue;
                foreach (var L in b.Decks) if (EntersCorridor(L, a, ox, oy, nx, ny)) return true;
            }
            return false;
        }

        static bool EntersCorridor(ZLane L, ZObject a, double ox, double oy, double nx, double ny) =>
            BoxInCorridor(L, nx, ny, a.WidthPix, a.HeightPix) && !BoxInCorridor(L, ox, oy, a.WidthPix, a.HeightPix);

        static bool EntersZone(ZLane L, ZObject a, double ox, double oy, double nx, double ny) =>
            BoxInZone(L, nx, ny, a.WidthPix, a.HeightPix) && !BoxInZone(L, ox, oy, a.WidthPix, a.HeightPix);

        /// <summary>Sidestep / push candidates: a step into a deck zone by a unit that holds no turn there is refused.</summary>
        internal bool BridgeBlocks(ZObject a, double ox, double oy, double nx, double ny)
        {
            if (!BridgeQueueEnabled) return false;
            foreach (var b in bridges)
            {
                if (b.holders.Contains(a)) continue;
                foreach (var L in b.Decks) if (EntersZone(L, a, ox, oy, nx, ny)) return true;
            }
            return false;
        }

        void SetAvoid(ZObject o, ZBridgeLane b) => SetAvoidFor(o, b, o.Waypoints.Count > 0 ? o.Waypoints[0] : ZWaypoint.Cleared);
        void SetAvoidFor(ZObject o, ZBridgeLane b, ZWaypoint wp) { var st = o.PS.bridge; st.o = o; st.avoid = b; st.avoidWp = wp; }

        /// <summary>A unit that holds a bridge turn (on its way in, over or out): right of way in waiting loops.</summary>
        internal bool BridgeHolderNear(ZObject o)
        {
            if (!BridgeQueueEnabled) return false;
            foreach (var b in bridges) if (b.holders.Contains(o)) return true;
            return false;
        }

        // ------------------------------------------------------------------ waiting bays (beside the road)
        void AssignBay(ZBridgeLane b, ZBridgeUnit q)
        {
            var o = q.o; var L = q.deck ?? b.Decks[0];
            q.nextBayTry = W.Time + 2;
            if (!o.CanOverwriteWP() || o.Waypoints.Count == 0 || !o.Waypoints[0].Equals(q.order)) return;
            bool nr = NoRocks(o);
            var (ftx, fty) = ZReach.FootTile(o.LocX, o.LocY);
            if (BayOk(L, q.end, CX(o), CY(o), BayLateral) && OpenAround(ftx, fty, nr)) { q.hasBay = true; q.bayX = o.CenterX; q.bayY = o.CenterY; return; }   // already beside the road
            int c = Reach.UnitComp(o, nr);
            if (c < 0) return;
            FloodLocal(o, nr);                                                                  // bays on this bank only (no deck on the way)
            double edge = q.end == 0 ? (L.Vertical ? L.KY0 : L.KX0) : (L.Vertical ? L.KY1 : L.KX1);   // keep-clear boundary on this side
            long bestScore = long.MaxValue; int bx = 0, by = 0;
            // candidate spots: vehicle footprints round the unit (it waits near where it is, off the road, not further back)
            var (utx, uty) = ZReach.FootTile(o.LocX, o.LocY);
            int nLocal = 0, nBay = 0, nPoint = 0, nFree = 0;
            for (int pass = 0; pass < 2 && bestScore == long.MaxValue; pass++)                  // then closer to the road (never on it) when all is taken
                for (int ty = uty - BayRadius; ty <= uty + BayRadius; ty++)
                    for (int tx = utx - BayRadius; tx <= utx + BayRadius; tx++)
                    {
                        if (!LocalReach(tx, ty)) continue;
                        nLocal++;
                        var (px, py) = ZReach.FootCentre(tx, ty, false);
                        if (!BayOk(L, q.end, px, py, pass == 0 ? BayLateral : BayLateralMin) || (pass == 0 && !OpenAround(tx, ty, nr))) continue;
                        nBay++;
                        if (!Reach.PointOk(px, py, false, nr, c)) continue;
                        nPoint++;
                        if (Occupied(px, py, o, null) || BayTaken(o, px, py) || (pass == 0 && WayBlocked(o, px, py, null))) continue;
                        nFree++;
                        int road = 0;
                        for (int j = 0; j < 2; j++) for (int i = 0; i < 2; i++) if (W.Map.ClassAt(tx + i, ty + j) == ZClass.Road) road++;
                        double along = Math.Abs((L.Vertical ? py : px) - edge);
                        double du = Math.Sqrt((double)(px - o.CenterX) * (px - o.CenterX) + (double)(py - o.CenterY) * (py - o.CenterY));
                        double back = BridgeOneWayFollow ? Math.Max(0, DeckDist(L, q.end, px, py) - DeckDist(L, q.end, o.CenterX, o.CenterY)) : 0;
                        long score = (long)(du + along * 0.5 + road * 24 + back * 2) * 4096 + ty * 256 + tx;
                        if (score < bestScore) { bestScore = score; bx = px; by = py; }
                    }
            if (bestScore == long.MaxValue) { Log(o, $"{b}: no waiting bay free: waits here (reach {nLocal}, bay {nBay}, fits {nPoint}, free {nFree})"); return; }
            q.hasBay = true; q.bayX = bx; q.bayY = by;
            o.Waypoints.Insert(0, ZWaypoint.Move(bx, by, false, RefBay));
            Log(o, $"{b}: waits beside the road at ({bx},{by})");
        }

        /// <summary>Distance (px, along the axis) from a point on the end-e bank to that end of the deck.</summary>
        static double DeckDist(ZLane L, int e, double x, double y) =>
            e == 0 ? (L.Vertical ? L.Y0 - y : L.X0 - x) : (L.Vertical ? y - L.Y1 : x - L.X1);

        /// <summary>The 2x2 footprint and a 1-tile ring round it are open ground (no rock pocket: the unit can drive out again).</summary>
        bool OpenAround(int tx, int ty, bool nr)
        {
            for (int j = ty - 1; j <= ty + 2; j++) for (int i = tx - 1; i <= tx + 2; i++) if (!W.Path.TilePassableGrid(i, j, false, nr)) return false;
            return true;
        }

        // local flood fill (footprints, Zod's TileOk moves, every bridge zone closed) round the unit: its own bank
        int[] floodMark; int floodStamp; readonly List<int> floodQ = new List<int>();

        void FloodLocal(ZObject o, bool nr)
        {
            var pf = W.Path; int w = pf.W, h = pf.H;
            if (floodMark == null || floodMark.Length != w * h) { floodMark = new int[w * h]; floodStamp = 0; }
            floodStamp++;
            bool robot = o.IsRobot;
            var (sx, sy) = ZReach.FootTile(o.LocX, o.LocY);
            floodQ.Clear();
            if (sx < 0 || sy < 0 || sx >= w || sy >= h) return;
            floodMark[sy * w + sx] = floodStamp; floodQ.Add(sy * w + sx);
            for (int i = 0; i < floodQ.Count; i++)
            {
                int cur = floodQ[i], cx = cur % w, cy = cur / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h || Math.Max(Math.Abs(nx - sx), Math.Abs(ny - sy)) > FloodRadius) continue;
                        int ni = ny * w + nx;
                        if (floodMark[ni] == floodStamp || !pf.StepOk(cx, cy, nx, ny, robot, nr) || InAnyBridgeZone(nx, ny, robot)) continue;
                        floodMark[ni] = floodStamp; floodQ.Add(ni);
                    }
            }
        }

        bool LocalReach(int tx, int ty) => floodMark != null && tx >= 0 && ty >= 0 && tx < W.Path.W && ty < W.Path.H && floodMark[ty * W.Path.W + tx] == floodStamp;

        bool InAnyBridgeZone(int tx, int ty, bool robot)
        {
            int fw = robot ? 1 : 2;
            foreach (var b in bridges)
                foreach (var L in b.Decks)
                    if (tx * 16 < L.ZX1 && L.ZX0 < (tx + fw) * 16 && ty * 16 < L.ZY1 && L.ZY0 < (ty + fw) * 16) return true;
            return false;
        }

        /// <summary>A waiting spot: outside every lane's keep-clear area, on the unit's side of the deck it will enter (not
        /// level with that deck's approach: exits turn there) and outside every bridge's approach band.</summary>
        bool BayOk(ZLane L, int end, double x, double y, int lateral)
        {
            if (KeepClearAt((int)x, (int)y) != null) return false;
            double along = L.Vertical ? y : x;
            double k0 = L.Vertical ? L.KY0 : L.KX0, k1 = L.Vertical ? L.KY1 : L.KX1;
            if (end == 0 ? along > k0 : along < k1) return false;
            return !InAnyApproachBand(x, y, lateral);
        }

        bool InAnyApproachBand(double x, double y, int lateral)
        {
            foreach (var b in bridges) foreach (var L in b.Decks) if (InApproachBand(L, x, y, lateral)) return true;
            return false;
        }

        /// <summary>The road in front of a deck: its axis +- lateral px, BayBand px out from the deck ends (kept open).</summary>
        static bool InApproachBand(ZLane L, double x, double y, int lateral)
        {
            double along = L.Vertical ? y : x, lat = L.Vertical ? x - (L.X0 + L.X1) * 0.5 : y - (L.Y0 + L.Y1) * 0.5;
            double c0 = L.Vertical ? L.Y0 : L.X0, c1 = L.Vertical ? L.Y1 : L.X1;
            return Math.Abs(lat) < lateral && along > c0 - BayBand && along < c1 + BayBand;
        }

        bool BayTaken(ZObject o, int x, int y)
        {
            foreach (var b in bridges)
                for (int e = 0; e < 2; e++)
                    foreach (var q in b.queue[e])
                    {
                        if (q.o == o || !q.hasBay) continue;
                        double dx = q.bayX - x, dy = q.bayY - y, m = MinSpacing(o, q.o) + 4;
                        if (dx * dx + dy * dy < m * m) return true;
                    }
            return false;
        }

        /// <summary>A unit turned round on a deck while others cross with it: it first drives off the far end in the
        /// reserved direction (a MOVE in front of its new order), then comes back on a turn of its own.</summary>
        void FinishCrossing(ZBridgeLane b, ZObject h)
        {
            if (!h.CanOverwriteWP() || h.Waypoints.Count == 0) return;
            var L = b.Decks[b.Dir == 0 ? b.Decks.Count - 1 : 0];
            bool nr = NoRocks(h); int c = Reach.UnitComp(h, nr);
            if (c < 0) return;
            double mid = L.Vertical ? (L.X0 + L.X1) * 0.5 : (L.Y0 + L.Y1) * 0.5;
            bool plus = b.Dir == 0;
            double edge = plus ? b.Z1 : b.Z0;
            for (int step = 0; step < 6; step++)
                foreach (int off in new[] { 0, 16, -16, 32, -32, 48, -48 })
                {
                    double dd = edge + (plus ? 1 : -1) * (Radius(h) + 8 + step * 16);
                    int x = (int)Math.Round(L.Vertical ? mid + off : dd), y = (int)Math.Round(L.Vertical ? dd : mid + off);
                    if (!Reach.PointOk(x, y, h.IsRobot, nr, c) || Occupied(x, y, h, null)) continue;
                    h.Waypoints.Insert(0, ZWaypoint.Move(x, y, false, RefStepAside));
                    h.PS.Shoves++;
                    Log(h, $"turned round on {b}: finishes the crossing to ({x},{y}) first");
                    return;
                }
        }

        // ------------------------------------------------------------------ lining up at a deck mouth
        /// <summary>ZPathing.OnBlocked: a unit with a bridge turn, in a deck's approach and pushed off its centre line, stops at
        /// a rail or the cliff beside the mouth (Zod's KillWP would drop its order). It first slides back onto the centre line
        /// (a path point in front of the rest of its path), then drives on.</summary>
        internal bool BridgeRealign(ZObject o, int stopX, int stopY)
        {
            if (!BridgeQueueEnabled || W.Time < o.PS.bridge.nextRealign) return false;
            double lx = o.LocX + o.XOver, ly = o.LocY + o.YOver;
            foreach (var b in bridges)
            {
                if (!b.holders.Contains(o)) continue;
                foreach (var L in b.Decks)
                {
                    if (!BoxInZone(L, lx, ly, o.WidthPix, o.HeightPix)) continue;
                    double mid = L.Vertical ? (L.X0 + L.X1) * 0.5 : (L.Y0 + L.Y1) * 0.5, lat = (L.Vertical ? CX(o) : CY(o)) - mid;
                    if (Math.Abs(lat) <= 1.5) return false;
                    // the spot on the centre line level with the unit, backed off a little from the mouth if needed
                    int half = o.WidthPix >> 1; bool nr = NoRocks(o);
                    double along = L.Vertical ? CY(o) : CX(o), away = along < (L.Vertical ? (L.Y0 + L.Y1) * 0.5 : (L.X0 + L.X1) * 0.5) ? -1 : 1;
                    for (int k = 0; k <= 3; k++)
                    {
                        double a = along + away * 8 * k;
                        int tx = (int)Math.Round(L.Vertical ? mid : a), ty = (int)Math.Round(L.Vertical ? a : mid);
                        if (!Reach.BoxFree(tx - half, ty - half, o.WidthPix, o.HeightPix, o.IsRobot, nr)) continue;
                        o.PS.bridge.nextRealign = W.Time + 0.5;
                        o.PathRecenter(tx, ty);
                        Log(o, $"{b}: {Math.Abs(lat):F0} px off the centre line at ({stopX},{stopY}): lines up at ({tx},{ty})");
                        return true;
                    }
                    return false;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ last resort in a dead jam
        /// <summary>ResolveDeadlocks found a waiting loop in which nobody has room to step aside or back up: its lowest-ranked
        /// unit that is not on or next to a bridge deck passes through the others for GhostTime s (Zod has no unit collision
        /// at all, research 7.4; the overlap push separates them again). Never onto a deck, never twice within 4 s.</summary>
        internal bool GhostThroughLoop(List<ZObject> loop)
        {
            if (!BridgeQueueEnabled || !GhostEnabled) return false;
            var order = new List<ZObject>(loop);
            order.Sort((x, y) => x == y ? 0 : Beats(x, y) ? 1 : -1);                          // lowest priority first
            foreach (var u in order)
            {
                if (Static(u) || !u.CanMove || u.PathInBuildingStage || W.Time < u.PS.ghostUntil + 4) continue;
                bool nearDeck = false;
                foreach (var b in bridges)
                    foreach (var L in b.Decks)
                        if (BoxInZone(L, u.LocX + u.XOver, u.LocY + u.YOver, u.WidthPix, u.HeightPix)) nearDeck = true;
                if (nearDeck) continue;
                u.PS.ghostUntil = W.Time + GhostTime; u.PS.Yields++; Ghosts++;
                Log(u, $"dead jam of {loop.Count} with no room: passes through for {GhostTime} s");
                return true;
            }
            return false;
        }
        public const double GhostTime = 1.5, GhostAfter = 6;
        /// <summary>Dead-jam last resort on / off (A/B for the stress tests).</summary>
        public static bool GhostEnabled = true;

        /// <summary>Last resort under every other rule: a unit with an order that has not moved 2 px for GhostAfter s, is not
        /// waiting for a bridge turn, not fighting, not in Zod's scripted building moves and not on or at a deck passes through
        /// the units round it for GhostTime s (never onto a deck out of turn: the gate still applies). Zod itself has no unit
        /// collision (research 7.4); the overlap push separates the units again at once.</summary>
        void UnstickDeadJams()
        {
            if (!GhostEnabled) return;
            double now = W.Time;
            foreach (var u in units)
            {
                var st = u.PS.bridge;
                if (Math.Abs(CX(u) - st.jamX) + Math.Abs(CY(u) - st.jamY) >= 2 || u.Waypoints.Count == 0) { st.jamX = CX(u); st.jamY = CY(u); st.jamSince = now; continue; }
                if (now - st.jamSince < GhostAfter || now < u.PS.ghostUntil + 4) continue;
                if (Static(u) || !u.CanMove || BridgeParked(u) || u.AttackObject != null || u.PathInBuildingStage || u.PathWaitingForRepair || u.PathPending) continue;
                bool nearDeck = false;
                foreach (var b in bridges)
                    foreach (var L in b.Decks)
                        if (BoxInZone(L, u.LocX + u.XOver, u.LocY + u.YOver, u.WidthPix, u.HeightPix)) nearDeck = true;
                if (nearDeck) continue;
                u.PS.ghostUntil = now + GhostTime; u.PS.Yields++; st.jamSince = now; Ghosts++;
                Log(u, $"no movement for {GhostAfter:0} s in a jam: passes through for {GhostTime} s");
            }
        }
        public int Ghosts { get; private set; }

        // ------------------------------------------------------------------ idle on a deck: drive off it
        bool EvacuateDeck(ZBridgeLane b, ZObject h)
        {
            if (!h.CanMove || h.Waypoints.Count > 0) return false;
            ZLane L = null;
            foreach (var x in b.Decks) if (BoxInCorridor(x, h.LocX + h.XOver, h.LocY + h.YOver, h.WidthPix, h.HeightPix)) { L = x; break; }
            if (L == null) return false;
            bool nr = NoRocks(h); int c = Reach.UnitComp(h, nr);
            if (c < 0) return false;
            double mid = L.Vertical ? (L.X0 + L.X1) * 0.5 : (L.Y0 + L.Y1) * 0.5;
            int first = b.Dir == 1 ? 1 : 0;
            for (int pass = 0; pass < 2; pass++)
            {
                bool plus = (pass == 0) == (first == 0);
                double edge = L.Vertical ? (plus ? L.ZY1 : L.ZY0) : (plus ? L.ZX1 : L.ZX0);
                for (int step = 0; step < 6; step++)
                    foreach (int off in new[] { 0, 16, -16, 32, -32, 48, -48 })
                    {
                        double dd = edge + (plus ? 1 : -1) * (Radius(h) + 8 + step * 16);
                        int x = (int)Math.Round(L.Vertical ? mid + off : dd), y = (int)Math.Round(L.Vertical ? dd : mid + off);
                        if (!Reach.PointOk(x, y, h.IsRobot, nr, c) || Occupied(x, y, h, null)) continue;
                        h.Waypoints.Add(ZWaypoint.Move(x, y, false, RefStepAside));
                        b.Evacuations++; h.PS.Shoves++;
                        Log(h, $"idle on {b}: drives off to ({x},{y})");
                        return true;
                    }
            }
            return false;
        }

        // ------------------------------------------------------------------ re-plan round a crossing
        /// <summary>A unit queued too long: another way round (another bridge) when it is not slower than the time already
        /// waited; with giveUp the order resolves to the nearest reachable spot on this side (the blocked search's closest point).</summary>
        bool TryReplanAround(ZBridgeLane b, ZBridgeUnit q, bool giveUp)
        {
            var o = q.o;
            if (!o.CanOverwriteWP()) return false;
            var (gx, gy) = (q.order.x, q.order.y);
            var alt = SearchW(o, gx, gy, b, out bool found, out int altCost);
            if (alt == null) return false;
            if (!giveUp)
            {
                if (!found || o.PS.bridge.replannedWp.Equals(q.order)) return false;             // one re-plan per order (no ping-pong between bridges)
                foreach (var x in bridges)
                {                                                                               // the other way must not queue at another busy crossing
                    if (x == b || !PathCrossesPts(o, alt, x)) continue;
                    if (x.Waiting > 0 || x.Dir >= 0) return false;
                }
                SearchW(o, gx, gy, null, out bool vf, out int viaCost);
                double waited = W.Time - q.since, speed = Math.Max(1, o.MoveSpeed);
                int allowance = (int)(waited * speed / 16.0 * 100);                             // the time already waited, in ground tiles
                if (vf && altCost > viaCost + allowance) return false;
                o.PS.bridge.replannedWp = q.order;
            }
            b.queue[q.end].Remove(q); q.queued = null;
            if (OnBay(o)) o.Waypoints.RemoveAt(0);
            SetAvoidFor(o, b, q.order);
            o.PS.reissue = true;
            if (giveUp) { b.GiveUps++; Log(o, $"{b}: queued {W.Time - q.since:F0} s: order resolves to the nearest reachable spot on this side"); }
            else { b.Replans++; Log(o, $"{b}: queued {W.Time - q.since:F0} s: re-plans round it"); }
            return true;
        }

        /// <summary>A helper move (step aside, make way, back up) goes in a straight line: its spot must be reachable that way
        /// (no wall or river in between: a spot on the other bank is not "aside"), and it never crosses a bridge deck the unit
        /// is not on already.</summary>
        internal bool HelperLegOk(ZObject u, int x, int y)
        {
            if (!Enabled || !BridgeQueueEnabled) return true;
            if (!LegClear(u.CenterX, u.CenterY, x, y, u.IsRobot, NoRocks(u), u, true)) return false;
            double lx = u.LocX + u.XOver, ly = u.LocY + u.YOver;
            foreach (var b in bridges)
                foreach (var L in b.Decks)
                {
                    bool on = BoxInCorridor(L, lx, ly, u.WidthPix, u.HeightPix);
                    if (!on && SegmentHits(L, u, CX(u), CY(u), x, y)) return false;
                    if (on && b.Dir >= 0)
                    {                                                                           // on a deck: only on in the reserved direction, never back
                        double now = b.Vertical ? CY(u) : CX(u), to = b.Vertical ? y : x;
                        if (b.Dir == 0 ? to < now - 0.5 : to > now + 0.5) return false;
                    }
                }
            return true;
        }

        /// <summary>The unit's path runs through the deck within maxLen px of path (the last segment cut at the budget).</summary>
        static bool PathCrossesWithin(ZLane L, ZObject a, double maxLen)
        {
            double left = maxLen, px = a.PathTargetX, py = a.PathTargetY;
            bool Seg(double sx, double sy, double ex, double ey)
            {
                double dx = ex - sx, dy = ey - sy, l = Math.Sqrt(dx * dx + dy * dy);
                if (l > left) { double f = left / Math.Max(l, 1e-9); ex = sx + dx * f; ey = sy + dy * f; l = left; }
                left -= l;
                return SegmentHits(L, a, sx, sy, ex, ey);
            }
            if (Seg(CX(a), CY(a), px, py)) return true;
            if (left <= 0 || a.PathPoints == null) return false;
            foreach (var p in a.PathPoints)
            {
                if (Seg(px, py, p.x, p.y)) return true;
                if (left <= 0) return false;
                px = p.x; py = p.y;
            }
            return false;
        }

        /// <summary>A detour for a unit with a bridge turn that would drive back against the reserved direction inside the
        /// crossing's zones (round units standing at the far end: back over the deck and round) is refused: head-on on a deck.</summary>
        internal bool BridgeRejectsPath(ZObject o, List<(int x, int y)> pts)
        {
            if (!BridgeQueueEnabled) return false;
            foreach (var b in bridges)
            {
                if (b.Dir < 0 || !b.holders.Contains(o)) continue;
                double prev = AlongOf(b, CX(o), CY(o)); double px = CX(o), py = CY(o);
                foreach (var p in pts)
                {
                    bool inZone = false;
                    foreach (var L in b.Decks) if (p.x >= L.ZX0 && p.x < L.ZX1 && p.y >= L.ZY0 && p.y < L.ZY1) inZone = true;
                    foreach (var L in b.Decks) if (SegmentHits(L, o, px, py, p.x, p.y)) inZone = true;
                    double at = AlongOf(b, p.x, p.y);
                    if (inZone && (b.Dir == 0 ? at < prev - 2 : at > prev + 2)) return true;
                    prev = at; px = p.x; py = p.y;
                }
            }
            return false;
        }

        /// <summary>The layer's own searches (detours, clean paths) keep an order that avoids a crossing off its decks.</summary>
        internal bool BridgeAvoidsTile(ZObject o, int tx, int ty)
        {
            var st = o.PS.bridge;
            if (!BridgeQueueEnabled || st.avoid == null || !bridges.Contains(st.avoid) || !HasOrder(o, st.avoidWp)) return false;
            int fw = o.IsRobot ? 1 : 2;
            foreach (var L in st.avoid.Decks)
                if (tx * 16 < L.ZX1 && L.ZX0 < (tx + fw) * 16 && ty * 16 < L.ZY1 && L.ZY0 < (ty + fw) * 16) return true;
            return false;
        }

        // ------------------------------------------------------------------ path hooks (StartPath)
        /// <summary>PrepareStartPath: a straight line (Zod's DirectPathPossible) over a deck skips CheckPath; a robot or a unit
        /// avoiding that bridge then runs the A* so the bridge rules below see its path.</summary>
        void BridgeForceSearch(ZObject o)
        {
            if (!BridgeQueueEnabled || bridges.Count == 0 || o.Waypoints.Count == 0) return;
            var st = o.PS.bridge;
            bool avoiding = st.avoid != null && st.avoidWp.Equals(o.Waypoints[0]);
            if (!o.IsRobot && !avoiding) return;
            double x0 = o.CenterX, y0 = o.CenterY, x1 = o.PathTargetX, y1 = o.PathTargetY;
            foreach (var b in bridges)
                foreach (var L in b.Decks)
                    if (SegmentHits(L, o, x0, y0, x1, y1)) { W.Path.ForceSearch = true; return; }
        }

        /// <summary>CheckPath: robots wade unless a deck is clearly faster (leaders decide, minions copy), robots on a deck
        /// walk its centre line (single file), and an order told to avoid a crossing routes round it.</summary>
        List<(int x, int y)> BridgePath(ZObject o, List<(int x, int y)> pts)
        {
            if (!BridgeQueueEnabled || bridges.Count == 0 || pts == null || pts.Count == 0 || o.Waypoints.Count == 0) return pts;
            var wp = o.Waypoints[0];
            if (BridgeHelperRef(wp.refId)) return pts;
            var st = o.PS.bridge; st.o = o;
            var (gx, gy) = pts[pts.Count - 1];
            if (st.avoid != null && st.avoidWp.Equals(wp) && bridges.Contains(st.avoid) && PathCrosses(o, pts, st.avoid))
            {
                bool found;
                var alt = o.IsRobot ? SearchWade(o, gx, gy, st.avoid, out found, out _) : SearchW(o, gx, gy, st.avoid, out found, out _);
                if (alt != null) { Log(o, $"routes round {st.avoid}: {alt.Count} points{(found ? "" : " (nearest reachable)")}"); return alt; }
                return pts;
            }
            if (!o.IsRobot) return pts;
            ZBridgeLane b = null;
            foreach (var x in bridges) if (PathCrosses(o, pts, x)) { b = x; break; }
            if (b == null) return pts;
            bool wade;
            var ls = o.Leader?.PS.bridge;
            if (o.IsMinion && ls != null && ls.lastChoice == b && ls.lastChoiceWp.Equals(wp)) wade = ls.lastChoiceWade;   // the squad does what its leader does
            else
            {
                SearchW(o, gx, gy, null, out bool vf, out int viaCost);
                SearchWade(o, gx, gy, b, out bool af, out int wadeCost);
                int d = CrossDir(b, o);
                bool busy = (b.Dir >= 0 && b.Dir != d) || b.Waiting > 0;
                // a free deck: Zod's path stays unless wading is faster anyway; a busy deck (reserved the other way or with a
                // queue): robots do not queue for it, they wade, unless the deck is clearly faster
                wade = af && (!vf || (!W.CrocKeepsToBridge && (busy ? wadeCost <= viaCost * RobotBridgeAdvantage + RobotBridgeMargin : wadeCost < viaCost)));   // level 16: no wading round a bridge that can be crossed (ZWorld.Crocodiles.cs)
                if (wade) b.RobotWades++; else b.RobotBridge++;
            }
            st.lastChoice = b; st.lastChoiceWp = wp; st.lastChoiceWade = wade;
            if (wade)
            {
                var alt = SearchWade(o, gx, gy, b, out bool af2, out _);
                if (alt != null && af2) { SetAvoid(o, b); Log(o, $"wades instead of {b}"); return alt; }
                return pts;
            }
            foreach (var L in b.Decks) pts = SingleFile(o, pts, L);
            return pts;
        }

        bool PathCrossesPts(ZObject o, List<(int x, int y)> pts, ZBridgeLane b) => PathCrosses(o, pts, b);

        bool PathCrosses(ZObject o, List<(int x, int y)> pts, ZBridgeLane b)
        {
            foreach (var L in b.Decks)
            {
                double px = o.CenterX, py = o.CenterY;
                foreach (var p in pts) { if (SegmentHits(L, o, px, py, p.x, p.y)) return true; px = p.x; py = p.y; }
            }
            return false;
        }

        /// <summary>Robots over a deck walk its centre line from one approach end to the other (the squad crosses single file).</summary>
        List<(int x, int y)> SingleFile(ZObject o, List<(int x, int y)> pts, ZLane L)
        {
            bool InZone(double x, double y) => x >= L.ZX0 && x < L.ZX1 && y >= L.ZY0 && y < L.ZY1;
            var (gx, gy) = pts[pts.Count - 1];
            if (InZone(gx, gy) || InZone(o.CenterX, o.CenterY)) return pts;                   // goal on the approach / already on it: keep Zod's points
            int i0 = -1, i1 = -1, hitSeg = -1;
            double px = o.CenterX, py = o.CenterY;
            for (int i = 0; i < pts.Count; i++)
            {
                if (hitSeg < 0 && SegmentHits(L, o, px, py, pts[i].x, pts[i].y)) hitSeg = i;
                if (InZone(pts[i].x, pts[i].y)) { if (i0 < 0) i0 = i; i1 = i; }
                else if (i0 >= 0) break;
                px = pts[i].x; py = pts[i].y;
            }
            if (hitSeg < 0) return pts;
            int from = i0 >= 0 ? Math.Min(i0, hitSeg) : hitSeg, to = i0 >= 0 ? i1 : hitSeg - 1;   // replace [from..to] (empty when no point lies in the zone)
            double bx = from > 0 ? pts[from - 1].x : o.CenterX, by = from > 0 ? pts[from - 1].y : o.CenterY;
            bool plus = (L.Vertical ? by : bx) < (L.Vertical ? (L.Y0 + L.Y1) * 0.5 : (L.X0 + L.X1) * 0.5);
            double mid = L.Vertical ? (L.X0 + L.X1) * 0.5 : (L.Y0 + L.Y1) * 0.5;
            const int m = 24;
            double a0 = L.Vertical ? (plus ? L.Y0 - m : L.Y1 + m) : (plus ? L.X0 - m : L.X1 + m);
            double a1 = L.Vertical ? (plus ? L.Y1 + m : L.Y0 - m) : (plus ? L.X1 + m : L.X0 - m);
            var entry = L.Vertical ? ((int)mid, (int)a0) : ((int)a0, (int)mid);
            var exit = L.Vertical ? ((int)mid, (int)a1) : ((int)a1, (int)mid);
            var res = new List<(int x, int y)>();
            for (int i = 0; i < from; i++) res.Add(pts[i]);
            res.Add(entry); res.Add(exit);
            for (int i = to + 1; i < pts.Count; i++) res.Add(pts[i]);
            // every leg must stay clear of walls for the robot's box, else keep Zod's points
            bool nr = NoRocks(o); int qx = o.CenterX, qy = o.CenterY; bool first = true;
            foreach (var p in res) { if (!LegClear(qx, qy, p.x, p.y, true, nr, o, first)) return pts; qx = p.x; qy = p.y; first = false; }
            return res;
        }

        // ------------------------------------------------------------------ time-weighted A* (road 1/1.689, water 1/0.7)
        int[] wg, wCame, wHeapNode, wHeapKey; bool[] wDone; int wHeapN; readonly List<int> wTouched = new List<int>();

        /// <summary>A* on the Zod graph (footprint tiles, TileOk moves, the unit's grid) with travel-time weights: 100 per ground
        /// tile, x 1/1.689 on road, x 1/0.7 in water (ZMapData.TileWalkSpeed). With <paramref name="block"/> its decks are
        /// closed: approach zones for vehicles, the whole keep-clear areas for robots (wading robots cross the approach road
        /// well away from the mouth). Returns Zod-style points (goal last when found) or, when the goal cannot be reached, the
        /// path to the closest reached tile; cost = the path's weighted length. Deterministic (heap with node tie-break).</summary>
        /// <summary>A wading route round a crossing for a robot travelling in direction d: keep right (east-bound robots wade
        /// south of an E-W bridge, west-bound north; south-bound west of an N-S bridge, north-bound east), so squads wading
        /// the two ways never meet head-on in the water beside the rails; without such a route, any wading route.</summary>
        List<(int x, int y)> SearchWade(ZObject o, int gxp, int gyp, ZBridgeLane b, out bool found, out int cost)
        {
            int d = CrossDir(b, o);
            var p = SearchW(o, gxp, gyp, b, out found, out cost, d, true);                      // keep right, no 1-tile robot gaps
            if (p != null && found) return p;
            p = SearchW(o, gxp, gyp, b, out found, out cost, d);
            if (p != null && found) return p;
            return SearchW(o, gxp, gyp, b, out found, out cost);
        }

        internal List<(int x, int y)> SearchW(ZObject o, int gxp, int gyp, ZBridgeLane block, out bool found, out int cost, int keepRight = -1, bool noGaps = false)
        {
            found = false; cost = int.MaxValue; layerSearches++;                            // DV44 path budget (ZCommit.cs)
            var pf = W.Path; int w = pf.W, h = pf.H, n = w * h;
            bool robot = o.IsRobot, nr = NoRocks(o);
            if (wg == null || wg.Length != n)
            {
                wg = new int[n]; wCame = new int[n]; wDone = new bool[n]; wHeapNode = new int[n * 8 + 16]; wHeapKey = new int[wHeapNode.Length];
                for (int i = 0; i < n; i++) wg[i] = int.MaxValue;
            }
            var (sx, sy) = ZReach.FootTile(o.LocX, o.LocY);
            if (!pf.StepOk(sx, sy, sx, sy, robot, nr))
            {                                                                                  // off the graph (pushed into a corner): nearest valid footprint
                bool got = false;
                for (int r = 1; r <= 2 && !got; r++)
                    for (int y = sy - r; y <= sy + r && !got; y++)
                        for (int x = sx - r; x <= sx + r && !got; x++)
                            if (pf.StepOk(x, y, x, y, robot, nr)) { sx = x; sy = y; got = true; }
                if (!got) return null;
            }
            var (gx, gy) = ZReach.FootTileAt(gxp, gyp, robot);
            int fw = robot ? 1 : 2;
            bool keepOut = robot && block != null && !InKeepAny(block, CX(o), CY(o));          // (a robot already in the mouth only keeps off the approach)
            // keep right: the wrong side of the crossing (8 tiles out from the axis, along the decks and approaches + 1 tile)
            double sa0 = 0, sa1 = 0, sl0 = 0, sl1 = 0; bool side = false;
            if (block != null && keepRight >= 0)
            {
                var F = block.Decks[0]; var La = block.Decks[block.Decks.Count - 1];
                double axis = block.Vertical ? (F.X0 + F.X1) * 0.5 : (F.Y0 + F.Y1) * 0.5;
                sa0 = (block.Vertical ? F.ZY0 : F.ZX0) - 16; sa1 = (block.Vertical ? La.ZY1 : La.ZX1) + 16;   // the water beside the rails
                // right of +axis travel: E-W bridge -> +y (south); N-S bridge -> -x (west)
                bool wrongIsLow = block.Vertical ? keepRight == 1 : keepRight == 0;              // the side with lower lateral coordinates is wrong
                sl0 = wrongIsLow ? axis - 8 * 16 : axis + 16; sl1 = wrongIsLow ? axis - 16 : axis + 8 * 16;
                bool In(double x, double y) { double al = block.Vertical ? y : x, la = block.Vertical ? x : y; return al >= sa0 && al < sa1 && la >= sl0 && la < sl1; }
                side = !In(CX(o), CY(o)) && !In(gxp, gyp);
            }
            double gox = CX(o), goy = CY(o);                                                    // 1-tile robot gaps (ZLanes: one robot at a time) not at the start / goal
            bool Gap(ZLane L) => !L.VehicleLane && !(gox >= L.ZX0 && gox < L.ZX1 && goy >= L.ZY0 && goy < L.ZY1) && !(gxp >= L.ZX0 && gxp < L.ZX1 && gyp >= L.ZY0 && gyp < L.ZY1);
            bool Closed(int tx, int ty)
            {
                if (block == null) return false;
                if (noGaps) foreach (var L in LanesNear(tx * 16 + fw * 8, ty * 16 + fw * 8)) if (tx * 16 < L.X1 && L.X0 < (tx + fw) * 16 && ty * 16 < L.Y1 && L.Y0 < (ty + fw) * 16 && Gap(L)) return true;   // (a gap its footprint touches is one of the lanes round the tile, not any lane of the map)
                if (side)
                {
                    double cx = tx * 16 + fw * 8, cy = ty * 16 + fw * 8, al = block.Vertical ? cy : cx, la = block.Vertical ? cx : cy;
                    if (al >= sa0 && al < sa1 && la >= sl0 && la < sl1) return true;
                }
                foreach (var L in block.Decks)
                {
                    int x0 = keepOut ? L.KX0 : L.ZX0, y0 = keepOut ? L.KY0 : L.ZY0, x1 = keepOut ? L.KX1 : L.ZX1, y1 = keepOut ? L.KY1 : L.ZY1;
                    if (tx * 16 < x1 && x0 < (tx + fw) * 16 && ty * 16 < y1 && y0 < (ty + fw) * 16) return true;
                }
                return false;
            }
            int start = sy * w + sx, goal = gx >= 0 && gy >= 0 && gx < w && gy < h ? gy * w + gx : -1;
            int best = start, bestH = WHeur(sx, sy, gx, gy);
            wHeapN = 0; wTouched.Clear();
            wg[start] = 0; wCame[start] = -1; wTouched.Add(start); WPush(start, bestH);
            int expansions = 0, limit = n * 2;
            while (wHeapN > 0 && expansions < limit)
            {
                int cur = WPop();
                if (wDone[cur]) continue;                                                      // stale heap entry
                wDone[cur] = true; expansions++;
                if (cur == goal) { found = true; best = cur; break; }
                int cx = cur % w, cy = cur / w, cg = wg[cur];
                int hc = WHeur(cx, cy, gx, gy); if (hc < bestH || (hc == bestH && cg < wg[best])) { bestH = hc; best = cur; }
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = cx + dx, ny = cy + dy;
                        if (!pf.StepOk(cx, cy, nx, ny, robot, nr) || Closed(nx, ny)) continue;
                        int ni = ny * w + nx;
                        int ng = cg + StepCost(nx, ny, robot, dx != 0 && dy != 0);
                        if (ng >= wg[ni]) continue;
                        if (wg[ni] == int.MaxValue) wTouched.Add(ni);
                        wg[ni] = ng; wCame[ni] = cur;
                        WPush(ni, ng + WHeur(nx, ny, gx, gy));
                    }
            }
            cost = wg[best];
            var tiles = new List<int>();
            for (int c = best; c != -1 && c != start; c = wCame[c]) tiles.Add(c);
            foreach (var i in wTouched) { wg[i] = int.MaxValue; wDone[i] = false; }
            if (tiles.Count == 0 && !found) return null;
            tiles.Reverse();
            var pts = new List<(int x, int y)>();
            for (int k = 0; k < tiles.Count; k++)
            {
                int t = tiles[k];
                if (k > 0 && k + 1 < tiles.Count)
                {
                    int a = tiles[k - 1], bb = tiles[k + 1];
                    if (t % w - a % w == bb % w - t % w && t / w - a / w == bb / w - t / w) continue;   // middle of a straight run
                }
                pts.Add(ZReach.FootCentre(t % w, t / w, robot));
            }
            if (found) pts.Add((gxp, gyp));
            if (pts.Count == 0) return null;
            return pts;
        }

        /// <summary>Weighted step: 100 (141 diagonal) x the inverse walk speed of the footprint (robots: its tile; vehicles: the
        /// mean of their 2x2), like ZMapData.TileWalkSpeed: road 1.689, water 0.7.</summary>
        int StepCost(int tx, int ty, bool robot, bool diag)
        {
            int baseCost = diag ? 141 : 100;
            if (robot) return (int)(baseCost * TileFactor(tx, ty));
            double f = (TileFactor(tx, ty) + TileFactor(tx + 1, ty) + TileFactor(tx, ty + 1) + TileFactor(tx + 1, ty + 1)) * 0.25;
            return (int)(baseCost * f);
        }

        double TileFactor(int tx, int ty)
        {
            switch (W.Map.ClassAt(tx, ty))
            {
                case ZClass.Road: return 1.0 / ZConst.ROAD_SPEED;
                case ZClass.Water: return 1.0 / ZConst.WATER_SPEED * W.Path.WaterCost;   // level 16: in step with the path grid (ZWorld.Crocodiles.cs); 1 everywhere else
                default: return 1.0;
            }
        }

        static int WHeur(int x, int y, int gx, int gy)
        {
            int dx = Math.Abs(x - gx), dy = Math.Abs(y - gy);
            return (int)((100 * Math.Max(dx, dy) + 41 * Math.Min(dx, dy)) / ZConst.ROAD_SPEED);   // admissible: road is the cheapest
        }

        void WPush(int node, int key)
        {
            if (wHeapN >= wHeapNode.Length) return;
            int i = wHeapN++;
            wHeapNode[i] = node; wHeapKey[i] = key;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (wHeapKey[p] < wHeapKey[i] || (wHeapKey[p] == wHeapKey[i] && wHeapNode[p] <= wHeapNode[i])) break;
                (wHeapNode[p], wHeapNode[i]) = (wHeapNode[i], wHeapNode[p]); (wHeapKey[p], wHeapKey[i]) = (wHeapKey[i], wHeapKey[p]);
                i = p;
            }
        }

        int WPop()
        {
            int top = wHeapNode[0]; layerWork += PopWork;                                 // DV44 path budget (ZCommit.cs): counter only
            wHeapN--;
            wHeapNode[0] = wHeapNode[wHeapN]; wHeapKey[0] = wHeapKey[wHeapN];
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, m = i;
                if (l < wHeapN && (wHeapKey[l] < wHeapKey[m] || (wHeapKey[l] == wHeapKey[m] && wHeapNode[l] < wHeapNode[m]))) m = l;
                if (r < wHeapN && (wHeapKey[r] < wHeapKey[m] || (wHeapKey[r] == wHeapKey[m] && wHeapNode[r] < wHeapNode[m]))) m = r;
                if (m == i) break;
                (wHeapNode[m], wHeapNode[i]) = (wHeapNode[i], wHeapNode[m]); (wHeapKey[m], wHeapKey[i]) = (wHeapKey[i], wHeapKey[m]);
                i = m;
            }
            return top;
        }
    }
}
