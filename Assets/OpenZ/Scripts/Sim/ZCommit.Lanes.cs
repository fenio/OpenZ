using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// DV44 (PT1, head-on at chokepoints): a squad passes a chokepoint as one, and a one-tile-long gap is a chokepoint too.
    ///  - The layer's lanes (ZLanes.cs) admit one direction at a time; they were found only where a 1-3 tile wide passage runs two
    ///    tiles or more. With DV44 a one-tile-long gap (a 2-tile opening in a rock wall, between a cliff and rocks) is a lane as well,
    ///    so two squads meeting there queue on either side instead of side-stepping round each other inside it.
    ///  - The lane's fairness switch (the other end has waited LaneFairness s: this end stops admitting) no longer cuts a squad in
    ///    two: a squad member whose leader or a mate is in the lane going the same way, or whose leader has already passed it on this
    ///    order, keeps the turn (oncoming traffic already in the lane still holds it: one direction at a time for everybody).
    /// </summary>
    public sealed partial class ZPathing
    {
        bool lanesShort;
        /// <summary>DV44: one-row chokepoints are lanes.</summary>
        internal bool ShortLanes => CommitOn && CommitFollow && (!ZonesOn || ShortLanesOutside);   // (v3: the live lanes are zones; DV44's one-tile-long gaps stay DV44's)
        /// <summary>Approach of a short chokepoint's zone (px each side; lanes: LaneApproach 40); its keep-clear area is the zone itself.</summary>
        public const int ShortApproach = 24;

        /// <summary>Lane rebuilds so far (EnsureLanes; pathing v3 keeps its master list of the lanes per build, ZPathV3.cs SyncLanes).</summary>
        internal int lanesBuilds;

        /// <summary>The squad of unit a is already passing lane L in direction d (its leader or a mate in the zone going d, or its
        /// leader past the zone that way on the same order): a keeps the squad's turn.</summary>
        bool SquadHasTurn(ZObject a, ZLane L, int d)
        {
            if (!FollowOn(a) || a.Kind != ZKind.Robot || LaneLive(L)) return false;           // (pathing v3: a lane in a zone / an active passage keeps the live rules for everybody)
            var l = a.IsMinion ? a.Leader : a.Minions.Count > 0 ? a : null;
            if (l == null || l.Destroyed || a.Waypoints.Count == 0) return false;
            if (a.IsMinion && (l.Waypoints.Count == 0 || !l.Waypoints[0].Equals(a.Waypoints[0]))) return false;
            foreach (var x in a.IsMinion ? (System.Collections.Generic.IEnumerable<ZObject>)l.Minions : Array.Empty<ZObject>())
                if (x != null && x != a && InZoneGoing(L, x, d)) return true;
            if (a.IsMinion && InZoneGoing(L, l, d)) return true;
            if (!a.IsMinion) return false;
            double c = L.Vertical ? CY(l) : CX(l);                                              // the leader is past the lane that way
            double lo = L.Vertical ? L.ZY0 : L.ZX0, hi = L.Vertical ? L.ZY1 : L.ZX1;
            double ca = L.Vertical ? CY(a) : CX(a);
            return d == 0 ? c >= hi && ca < hi : c < lo && ca >= lo;
        }

        static bool InZoneGoing(ZLane L, ZObject x, int d) => !x.Destroyed && x.Waypoints.Count > 0 && BoxInZone(L, x.LocX + x.XOver, x.LocY + x.YOver, x.WidthPix, x.HeightPix) && TravelDir(L, x) == d;

        // ------------------------------------------------------------------ a squad passes a chokepoint as one (review 2: head-on)
        /// <summary>Lanes a squad is passing: the direction and the squad's leader, since when. Admitting one member of a squad into a
        /// lane's zone reserves the lane for the whole squad in that direction until every member with the order has passed its far end
        /// (or SquadResMax s): traffic the other way waits outside the keep-clear area instead of meeting the squad inside it.</summary>
        readonly System.Collections.Generic.Dictionary<ZLane, (int dir, ZObject leader, double since)> squadRes = new System.Collections.Generic.Dictionary<ZLane, (int, ZObject, double)>();
        /// <summary>A squad's hold on a lane lasts at most this long (s): a squad stuck in it never locks the lane for good.</summary>
        public const double SquadResMax = 30;

        static ZObject SquadLeaderOf(ZObject a) => a.Kind != ZKind.Robot ? null : a.IsMinion ? a.Leader : a.Minions.Count > 0 ? a : null;

        /// <summary>LaneConflict (DV44): unit a, about to enter lane L going d, waits - the lane is reserved by another squad going the
        /// other way (the lane's own rules decide everything else).</summary>
        internal bool SquadLaneHold(ZObject a, ZLane L, int d, bool keepEntry)
        {
            if (!FollowOn(a) || d < 0 || LaneLive(L)) return false;
            if (PassesOwnSquad(a, L)) return false;                                              // vehicles first: a squad of its own team does not close the lane against a vehicle (ZYield.cs)
            if (squadRes.TryGetValue(L, out var r))
            {
                if (!SquadStillPassing(L, r)) squadRes.Remove(L);
                else if (r.dir != d && SquadLeaderOf(a) != r.leader) return true;                 // another squad is passing the other way
            }
            return false;
        }

        bool PassesOwnSquad(ZObject a, ZLane L) =>
            VehicleFirst && a.Kind == ZKind.Vehicle && a.Owner != Team.Neutral && squadRes.TryGetValue(L, out var r) && r.leader != null && r.leader.Owner == a.Owner;

        /// <summary>LaneEnter (DV44): a squad member admitted into a lane's zone reserves it for its squad.</summary>
        internal void SquadLaneEnter(ZObject a, ZLane L, int d)
        {
            if (!FollowOn(a) || d < 0 || L.Bridge && BridgeQueueEnabled || LaneLive(L)) return;
            var l = SquadLeaderOf(a); if (l == null || l.Destroyed) return;
            if (squadRes.TryGetValue(L, out var r) && SquadStillPassing(L, r)) return;
            squadRes[L] = (d, l, W.Time);
        }

        bool SquadStillPassing(ZLane L, (int dir, ZObject leader, double since) r)
        {
            var l = r.leader;
            if (l == null || l.Destroyed || W.Time - r.since > SquadResMax) return false;
            bool Pending(ZObject u)
            {
                if (u == null || u.Destroyed || u.Waypoints.Count == 0) return false;
                double c = L.Vertical ? CY(u) : CX(u);
                double far = r.dir == 0 ? (L.Vertical ? L.ZY1 : L.ZX1) : (L.Vertical ? L.ZY0 : L.ZX0);
                bool past = r.dir == 0 ? c >= far : c < far;
                if (past) return false;
                if (BoxInZone(L, u.LocX + u.XOver, u.LocY + u.YOver, u.WidthPix, u.HeightPix)) return TravelDir(L, u) != 1 - r.dir;   // (in it, not turned back)
                return TravelDir(L, u) == r.dir && PathUsesCorridor(L, u, SquadResLook);            // still coming in the same way
            }
            if (Pending(l)) return true;
            foreach (var m in l.Minions) if (Pending(m)) return true;
            return false;
        }
        /// <summary>A squad member whose way passes the lane within this many px has still to pass it.</summary>
        public const double SquadResLook = 240;
    }
}
