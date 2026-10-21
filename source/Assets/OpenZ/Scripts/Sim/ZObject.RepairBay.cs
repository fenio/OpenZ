using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// Zod (brepair.cpp:487-563, zserver.cpp:1350-1373 / 1496-1571): the vehicle drives to the station centre, is deleted, and 5 s later a
    /// fresh full-health copy is created there and force-moves out. With DV62 the same trip (entrance, wait while the bay is taken, drive in
    /// to the centre) ends ON the pad: the vehicle stays in the world (it can be shot and its passive engage still fires), the station heals
    /// it in ticks of 0.25 s, from its health at arrival to full in Zod's 5 s (hits taken on the pad add time), then it drives back out to
    /// the entrance. A human player's order while it stands on the pad calls it off at once (it drives out first, then follows the order);
    /// after a full repair a human team's vehicle drives back to where it was when the repair trip began, unless more orders are queued.
    /// The CPU's vehicles keep Zod's flow otherwise (its orders queue behind the repair, no automatic return). Deterministic: sim ticks
    /// only, no RNG. OFF (PlayerPrefs oz_repair_pad 0 or 'Zod exact') = Zod's swap.
    /// </summary>
    public partial class ZObject
    {
        // ---- station side
        /// <summary>DV62: the vehicle on (or driving off) the pad, -1 = the bay is free.</summary>
        public int RepairOccupantId = -1;
        /// <summary>DV62: the vehicle's health when it reached the pad (the heal step = (max - this) / 5 s x 0.25 s).</summary>
        public int RepairFromHealth;
        double repairNextTick, repairDoorstepUntil = -1;
        // ---- vehicle side
        int repairRetX, repairRetY; bool repairReturn, repairNoReturn;
        /// <summary>DV62 proof counters (all stations / vehicles).</summary>
        public static int PadArrivals, PadTicks, PadHealed, PadReleased, PadReturns, PadCalledOff;

        /// <summary>DV62: this vehicle stands on the pad of station `stationId` being repaired.</summary>
        public bool OnRepairPad(int stationId) => (Waypoints.Count > 0 && Waypoints[0].mode == WpMode.UnitRepair && Waypoints[0].refId == stationId && cur.stage == Stage.Repairing)
                                                  || (IsShuttle && ShuttleOnRepairPad && ShuttleRepairTarget == stationId);   // item 271 (Sim/ZWorld.ShuttleRepair.cs)
        /// <summary>DV62: this vehicle is driving off the pad of station `stationId` (healed or called off).</summary>
        public bool LeavingRepairPad(int stationId) => Waypoints.Count > 0 && Waypoints[0].mode == WpMode.UnitRepair && Waypoints[0].refId == stationId && cur.stage == Stage.RepairLeave;
        /// <summary>DV62: this vehicle holds the bay of station `stationId`: driving in, on the pad, driving off, or backing out.</summary>
        public bool InRepairBay(int stationId) => Waypoints.Count > 0 && Waypoints[0].mode == WpMode.UnitRepair && Waypoints[0].refId == stationId
            && (cur.stage == Stage.EnterBuilding || cur.stage == Stage.Repairing || cur.stage == Stage.RepairLeave || cur.stage == Stage.ExitBuilding)
            || (IsShuttle && ShuttleOnRepairPad && ShuttleRepairTarget == stationId);                     // item 271
        /// <summary>DV62: on any repair pad (view: no idle sway, no re-order by R).</summary>
        public bool OnAnyRepairPad => Waypoints.Count > 0 && Waypoints[0].mode == WpMode.UnitRepair && (cur.stage == Stage.Repairing || cur.stage == Stage.RepairLeave);
        /// <summary>DV62: the bay holds another vehicle (on the pad or still driving off it).</summary>
        public bool BayTakenFor(ZObject u) => ZWorld.RepairInPlaceDeviation && RepairOccupantId >= 0 && (u == null || RepairOccupantId != u.Id);
        /// <summary>DV62 view: the share of the repair done (0..1) for the vehicle on the pad, -1 = none.</summary>
        public float RepairPadProgress
        {
            get
            {
                if (!RepairingUnit || RepairOccupantId < 0 || World == null) return -1f;
                var u = World.Get(RepairOccupantId); if (u == null || u.MaxHealth <= 0) return -1f;
                int need = u.MaxHealth - RepairFromHealth; if (need <= 0) return 1f;
                return Math.Max(0f, Math.Min(1f, (u.Health - RepairFromHealth) / (float)need));
            }
        }

        /// <summary>DV62: where a vehicle waits for the bay: 40 px out from the entrance (the one driving off the pad stops at the entrance
        /// itself), or the entrance when that spot is blocked.</summary>
        static void RepairQueueSpot(ZObject t, ZObject u, out int x, out int y)
        {
            t.GetRepairEntrance(out x, out y);
            int qy = y + 40;
            if (!u.World.Path.WithinImpassable(x - (u.WidthPix >> 1) + 1, qy - (u.HeightPix >> 1) + 1, u.WidthPix - 2, u.HeightPix - 2, out _, out _, false)) y = qy;
        }

        void RepairTripStart()
        {
            repairRetX = CenterX; repairRetY = CenterY; repairNoReturn = false;
            repairReturn = ZWorld.RepairInPlaceDeviation && World != null && World.IsHumanTeam(Owner);
        }

        /// <summary>The vehicle reached the centre (Zod's EnterBuilding end): accepted = it stays on the pad; else it backs out like Zod.</summary>
        void RepairBayArrive(ZObject t)
        {
            int ex, ey;
            if (!t.CanRepairUnit(Owner) || !CanBeRepaired() || t.RepairingUnit || t.BayTakenFor(this))
            {
                if (!t.GetRepairEntrance(out ex, out ey)) { KillWP(); return; }
                SetTarget(ex, ey); cur.stage = Stage.ExitBuilding; SetVelocity();
                return;
            }
            StopMove();
            cur.stage = Stage.Repairing;
            t.RepairOccupantId = Id; t.RepairingUnit = true; t.RepairFromHealth = Health; t.RepairType = Type;
            t.repairNextTick = World.Time + ZWorld.RepairPadTick;
            t.RepairTime = World.Time + ZWorld.RepairPadSeconds;                                      // the countdown ring (BridgeRepairBadges)
            PadArrivals++;
            World.Emit(ZEventType.RepairIn, this, t);
        }

        /// <summary>The pad and drive-off stages of the UNIT_REPAIR waypoint; true = handled this tick.</summary>
        bool RepairBayStep(double dt, ZObject t)
        {
            if (cur.stage != Stage.Repairing && cur.stage != Stage.RepairLeave) return false;
            if (Owner == Team.Neutral) { KillWP(); return true; }                                        // driver sniped on the pad
            if (cur.stage == Stage.Repairing)
            {
                if (t.RepairOccupantId != Id || !t.CanRepairUnit(Owner) || !t.RepairingUnit) RepairBayLeave(t);   // healed, or the station lost / captured
                else if (IsMoving) StopMove();
                return true;
            }
            if (!ProcessMoveOrKillWP(dt, false)) return true;
            if (!ReachedTarget()) return true;
            bool back = repairReturn && !repairNoReturn && Waypoints.Count == 1 && (repairRetX != CenterX || repairRetY != CenterY);
            KillWP();
            if (back) { Waypoints.Add(ZWaypoint.Move(repairRetX, repairRetY, false)); UpdatedWaypoints = true; PadReturns++; }
            return true;
        }

        void RepairBayLeave(ZObject t)
        {
            if (!t.GetRepairEntrance(out int ex, out int ey)) { KillWP(); return; }
            cur.stage = Stage.RepairLeave; SetTarget(ex, ey); SetVelocity();
        }

        /// <summary>ZWorld.SendWaypoints, before the waypoints are replaced: a human player's order calls the vehicle off the pad (it
        /// drives out first: the kept UNIT_REPAIR waypoint, now leaving, then the new orders; no automatic return). The CPU's orders
        /// queue behind the repair (Zod: the vehicle is gone while it is repaired).</summary>
        internal void RepairBayOnOrder(bool human)
        {
            if (Waypoints.Count == 0 || Waypoints[0].mode != WpMode.UnitRepair) return;
            if (cur.stage == Stage.RepairLeave) { repairNoReturn = true; return; }
            if (cur.stage != Stage.Repairing || !human) return;
            var t = World.Get(Waypoints[0].refId);
            if (t == null) return;
            repairNoReturn = true; PadCalledOff++;
            RepairBayLeave(t);
        }

        /// <summary>Station tick (ProcessBuilding): heal the vehicle on the pad; free the bay when it is gone, dead, captured or off the pad.</summary>
        void RepairBayTick(double time)
        {
            if (RepairOccupantId < 0) { RepairingUnit = false; return; }
            var u = World.Get(RepairOccupantId);
            bool onPad = u != null && !u.Destroyed && u.KillAt < 0 && u.Owner == Owner && CanRepairUnit(u.Owner) && u.OnRepairPad(Id);
            bool inBay = u != null && !u.Destroyed && u.KillAt < 0 && u.InRepairBay(Id);                 // claimed at the entrance, held until it is out again
            if (!onPad)
            {
                if (RepairingUnit) { RepairingUnit = false; PadReleased++; }
                if (inBay) { repairDoorstepUntil = -1; return; }
                // the vehicle just came out: the bay stays taken while it is still on the doorstep (max 3 s), so the next one
                // never drives in through it
                bool alive = u != null && !u.Destroyed && u.KillAt < 0;
                if (alive && repairDoorstepUntil < 0) repairDoorstepUntil = time + 3.0;
                bool doorstep = alive && time < repairDoorstepUntil && GetRepairEntrance(out int ex, out int ey) && Within(u.CenterX, u.CenterY, ex, ey, 36);
                if (!doorstep && time >= ShuttleWreckUntil) { RepairOccupantId = -1; repairDoorstepUntil = -1; }   // K256e: a shot down shuttle's wreck on the deck holds the bay for ZWorld.ShuttleDeckWreckSec
                return;
            }
            if (!RepairingUnit || time + 1e-9 < repairNextTick) return;
            repairNextTick = time + ZWorld.RepairPadTick;
            int step = Math.Max(1, (int)Math.Ceiling((u.MaxHealth - RepairFromHealth) * ZWorld.RepairPadTick / ZWorld.RepairPadSeconds));
            u.SetHealth(u.Health + step); PadTicks++;
            RepairTime = time + Math.Max(0, Math.Ceiling((u.MaxHealth - u.Health) / (double)step) * ZWorld.RepairPadTick);
            if (u.Health < u.MaxHealth) return;
            RepairingUnit = false; PadHealed++;
            u.ResetDamageInfo();                                                                          // Zod's fresh copy: APC weapon from its crew
            World.Emit(ZEventType.RepairOut, u, this);
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>
        /// OFF = Zod's 5 s swap. PlayerPrefs oz_repair_pad, oz_zod_exact.</summary>
        public static bool RepairInPlaceDeviation = true;
        /// <summary>DV62: game seconds from the arrival health to full (Zod's flat repair time) and the heal tick.</summary>
        public const double RepairPadSeconds = 5.0, RepairPadTick = 0.25;
        /// <summary>DV77: a vehicle fires out of its repair bay on its whole way in and out, not only while it stands on the pad or
        /// drives off it: the sight line leaves the station's own walls out from the moment the vehicle drives in. The repair and its
        /// time are untouched. OFF = DV62 alone. Needs DV62 and DV23; off in 'Zod exact'.</summary>
        public static bool RepairBayFireDeviation = false;
    }
}
