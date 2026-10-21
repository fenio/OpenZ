using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// DV118, the playable shuttle at a repair station (item 271: repaired at a repair station like the other vehicles, there and back
    /// with R or sent there by hand; item 317: it lands ON the station, turned to fit, the crane moved out of its way). The shuttle has
    /// no driving path, so it is not sent through the vehicles' UNIT_REPAIR waypoint (ZObject.Buildings.cs / ZObject.RepairBay.cs);
    /// its own flight takes it there and the station's own pad repair (DV62, RepairBayTick) heals it, at the same rate and in the same
    /// ticks as a vehicle on the pad:
    ///  * a repair order (the R key, or a right click on the own station: the UNIT_REPAIR waypoint) = it flies to the station's deck
    ///    point (ZObject.ShuttleDeckPoint: the place where the whole ship fits on the deck), hovers there and turns slowly onto
    ///    ZWorld.ShuttleDeckHeadingDeg while the station's crane moves out of its way (ZWorld.ShuttleCraneMoveSec), comes down onto the
    ///    deck, is repaired, and then flies back to where it stood when the order was given (as the vehicles come back after DV62's trip);
    ///  * a plain move order onto an own repair station (by hand) = it lands on the deck the same way and is repaired there; it stays;
    ///  * the bay taken by another vehicle: it hovers over the station, turns onto the deck heading while it waits
    ///    and tries again every 0.5 s, coming down by itself once the bay is free; any new order calls it off at once; the passengers
    ///    stay inside the whole time; the station lost or destroyed ends the repair. The landing note says ZWorld.ShuttleWhyRepairPad.
    /// No random draw, no frame time; the CPU never uses the shuttle (DV118), so this is the human player's only.
    /// </summary>
    public partial class ZObject
    {
        /// <summary>DV118 + item 271: the repair station the shuttle is flying to or standing on (-1 = none).</summary>
        public int ShuttleRepairTarget = -1;
        /// <summary>DV118 + item 271: it stands on that station's pad and the station repairs it.</summary>
        public bool ShuttleOnRepairPad;
        bool shuttleRepairBack; int shuttleRetX, shuttleRetY;
        /// <summary>Check counters: repairs begun on a pad, return flights after a repair.</summary>
        public static int ShuttleRepairs, ShuttleRepairReturns;

        internal void ShuttleRepairPlan(ZObject station, bool back)
        {
            ShuttleRepairTarget = station.Id; ShuttleOnRepairPad = false; shuttleRepairBack = back; shuttleRetX = CenterX; shuttleRetY = CenterY;
        }

        /// <summary>Called off (a new order) or done: the station frees its bay at its next tick (RepairBayTick: no longer on the pad).</summary>
        internal void ShuttleRepairLeave() { ShuttleRepairTarget = -1; ShuttleOnRepairPad = false; }

        /// <summary>The point on its line (a strafe aside) stands over the station's deck point (ZObject.ShuttleDeckPoint, within a tile).</summary>
        internal bool ShuttleOverStation(ZObject t) => ShuttleDeckPoint(t, out int cx, out int cy) && Math.Abs(ShuttleLineX - cx) <= 16 && Math.Abs(ShuttleLineY - cy) <= 16;

        /// <summary>It is over (or coming down onto, or standing on) the deck of its own repair station, the station still standing.</summary>
        internal bool ShuttleOnDeckApproach()
        {
            if (ShuttleRepairTarget < 0 || World == null) return false;
            var t = World.Get(ShuttleRepairTarget);
            return t != null && !t.Destroyed && t.CanRepairUnit(Owner) && ShuttleOverStation(t);
        }

        double shuttleRetryAt;
        /// <summary>Hovering over its station on a repair trip: true every 0.5 s (sim time), when the bay is tested again.</summary>
        internal bool ShuttleRepairRetry()
        {
            if (!ShuttleOnDeckApproach() || World.Time < shuttleRetryAt) return false;
            shuttleRetryAt = World.Time + 0.5;
            return true;
        }

        /// <summary>Just landed (or ordered while standing on it): the station takes it on its pad, as RepairBayArrive takes a vehicle.</summary>
        internal void ShuttleRepairArrive()
        {
            if (ShuttleRepairTarget < 0 || World == null) return;
            var t = World.Get(ShuttleRepairTarget);
            if (t == null || !t.CanRepairUnit(Owner) || !CanBeRepaired() || !ShuttleOverStation(t) || (t.RepairingUnit && t.RepairOccupantId != Id) || t.BayTakenFor(this))
            { ShuttleRepairLeave(); return; }
            ShuttleOnRepairPad = true;
            t.RepairOccupantId = Id; t.RepairingUnit = true; t.RepairFromHealth = Health; t.RepairType = Type;
            t.repairNextTick = World.Time + ZWorld.RepairPadTick;
            t.RepairTime = World.Time + ZWorld.RepairPadSeconds;
            PadArrivals++; ShuttleRepairs++;
            World.Emit(ZEventType.RepairIn, this, t);
        }

        /// <summary>Every step while it stands on the ground: the repair over (healed: the station let it go) = it flies back when the
        /// order was a repair order; the station lost or gone = the repair just ends.</summary>
        internal void ShuttleRepairStep()
        {
            if (!ShuttleOnRepairPad || World == null) return;
            var t = World.Get(ShuttleRepairTarget);
            if (t != null && !t.Destroyed && t.CanRepairUnit(Owner) && t.RepairingUnit && t.RepairOccupantId == Id) return;   // still on the pad
            bool back = shuttleRepairBack && Health >= MaxHealth;
            ShuttleRepairLeave();
            if (!back) return;
            int dx = shuttleRetX - CenterX, dy = shuttleRetY - CenterY;
            if (dx * dx + dy * dy <= ZWorld.ShuttleSameSpotPx * ZWorld.ShuttleSameSpotPx) return;
            var (gx, gy) = World.ShuttleClamp(shuttleRetX, shuttleRetY);
            ShuttleSetGoal(gx, gy); ShuttleRepairReturns++; PadReturns++;
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>Item 271: a repair order for the shuttle to station `stationId` (back = fly back afterwards). false = refused (not
        /// its team's station, nothing to repair, not a repair station).</summary>
        bool ShuttleRepairOrder(ZObject o, int stationId, bool back)
        {
            var t = Get(stationId);
            if (t == null || !t.CanRepairUnit(o.Owner) || !o.CanBeRepaired() || !o.ShuttleDeckPoint(t, out int cx, out int cy)) return false;   // item 317: onto the station's deck point
            if (o.ShuttleOnRepairPad && o.ShuttleRepairTarget == t.Id) return true;                    // already on that pad
            o.ShuttleRepairPlan(t, back);
            if (o.ShuttleState == ZShuttleState.Landed && o.ShuttleLift <= 0 && o.ShuttleOverStation(t)) o.ShuttleRepairArrive();
            else o.ShuttleSetGoal(cx, cy);
            return true;
        }

        /// <summary>Item 271: the own repair station whose ground (its footprint) lies under a move target (px), or null.</summary>
        ZObject ShuttleStationAt(int x, int y, ZObject o)
        {
            foreach (var b in Objects)
                if (b.Kind == ZKind.Building && b.CanRepairUnit(o.Owner) && x >= b.LocX && x < b.LocX + b.WidthPix && y >= b.LocY && y < b.LocY + b.HeightPix) return b;
            return null;
        }

        /// <summary>Item 271, for ShuttlePadFree: the station the shuttle is to land on for its repair (its pad is a landing place).</summary>
        ZObject ShuttleRepairStation(ZObject self) => self != null && self.ShuttleRepairTarget >= 0 ? Get(self.ShuttleRepairTarget) : null;
    }
}
