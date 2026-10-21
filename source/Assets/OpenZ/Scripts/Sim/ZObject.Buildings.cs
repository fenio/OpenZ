using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// Buildings (zbuilding.cpp, bfort/brobot/bvehicle/brepair/bradar/bbridge.cpp): zone link, production state machine with
    /// Zod's build-time formula, LIFO queue and endless repeat, stored cannons, rally points, the repair station's 5 s swap,
    /// anchor points; and the three unit waypoints that use them: CRANE_REPAIR (zobject.cpp:2915-3091), UNIT_REPAIR
    /// (:3093-3283) and ENTER_FORT (:2774-2913).
    /// </summary>
    public partial class ZObject
    {
        // ---- zone (connected_zone = first zone containing the top-left, zserver.cpp:802)
        public int ZoneIndex = -1;
        public readonly List<ZObject> LinkedObjects = new List<ZObject>();   // flags: the buildings of their zone (oflag.cpp:72-99)

        // ---- production (zbuilding.h:78-87)
        public int Level;                                    // blevel 0..5
        /// <summary>The level the build list is read at: blevel + the owner's difficulty bonus (ZWorld.TechBonus, 0 = Zod).</summary>
        public int BuildLevel => System.Math.Min(5, Level + (World != null && (int)Owner >= 0 && (int)Owner < 3 ? World.TechBonus[(int)Owner] : 0));
        public bool Building;                                // BUILDING_BUILDING (else BUILDING_SELECT)
        public string CurUnit;                               // (bot, boid); null = none
        public double BInit, BFinal;
        /// <summary>Robot squads and vehicles this building has built so far (the CPU's Laser mix reads it; stored cannons and
        /// the Electrobot are not counted).</summary>
        public int BuiltCount;
        public double ZoneOwnage;                            // flags owned by our team / all flags
        public readonly List<string> Queue = new List<string>();          // <= 5, index 0 = next
        /// <summary>
        /// being built. Off: the factory goes idle once the queue is done. Only read while ZWorld.FifoQueueDeviation is on.</summary>
        public bool RepeatProduction = true;
        public readonly List<string> StoredCannons = new List<string>();  // <= 4
        public readonly List<ZWaypoint> RallyPoints = new List<ZWaypoint>();
        public const int MAX_QUEUE_ITEMS = 5, MAX_STORED_CANNONS = 4;

        // ---- repair station (brepair.cpp)
        public bool RepairingUnit; public double RepairTime;
        public string RepairType, RepairDriverType; public readonly List<int> RepairDrivers = new List<int>();
        public readonly List<ZWaypoint> RepairWaypoints = new List<ZWaypoint>();

        // ---- server flags for the side-effect pass
        public string BuildUnitType;                          // sflags.build_unit + (bot, boid)
        public bool RepairUnitReady;                          // sflags.repair_unit
        public int DestroyFortId = -1, EnteredRepairId = -1;  // sflags.destroy_fort_building_ref_id / entered_repair_building_ref_id

        public bool IsFort => Type == "fort_front" || Type == "fort_back";
        public bool IsBridge => Type == "bridge_vert" || Type == "bridge_horz";
        public bool IsProducer => Kind == ZKind.Building && ZBuildLists.IsProducer(Type);

        // ------------------------------------------------------------------ anchors (research 1.3, px relative to the top-left)
        /// <summary>The robot factory exit line (px from its left edge). Zod: 43, which is 11 px to the right of the middle of the door the
        /// model has, so every squad walked out through the right door post. DV66: the middle of the door (the same tile column).</summary>
        static int RobotDoorX => ZWorld.ExitLanesDeviation ? 32 : 43;
        /// <summary>GetBuildingCreationPoint: where a new unit is created (centre).</summary>
        public bool GetCreationPoint(out int x, out int y)
        {
            x = y = 0;
            switch (Type)
            {
                case "fort_front": x = LocX + 80; y = LocY + 128; return true;
                case "fort_back": x = LocX + 80; y = LocY + 32; return true;
                case "robot_factory": x = LocX + RobotDoorX; y = LocY + 53; return true;
                case "vehicle_factory": x = LocX + 32; y = LocY + 48; return true;
            }
            return false;
        }
        /// <summary>GetBuildingCreationMovePoint: the exit the new unit force-moves to (also the fort gate for ENTER_FORT).</summary>
        public bool GetCreationMovePoint(out int x, out int y)
        {
            x = y = 0;
            switch (Type)
            {
                case "fort_front": x = LocX + 80; y = LocY + 208; return true;
                case "fort_back": x = LocX + 80; y = LocY - 16; return true;
                case "robot_factory": x = LocX + RobotDoorX; y = LocY + 96; return true;
                case "vehicle_factory": x = LocX + 32; y = LocY + 96; return true;
            }
            return false;
        }
        /// <summary>GetCraneEntrance (two for bridges) and GetCraneCenter (brobot.cpp:477-489 ... bbridge.cpp:454-481).</summary>
        public bool GetCraneEntrance(out int x, out int y, out int x2, out int y2)
        {
            x = y = x2 = y2 = 0; int h = HeightPix, w = WidthPix;
            switch (Type)
            {
                case "robot_factory": x = LocX + 35; y = LocY + h + 32; break;
                case "vehicle_factory": x = LocX + 31; y = LocY + h + 32; break;
                case "repair": x = LocX + 32; y = LocY + h + 32; break;
                case "radar": x = LocX + 28; y = LocY + h + 32; break;
                case "bridge_vert": x = LocX + 32; y = LocY - 32; x2 = LocX + 32; y2 = LocY + h + 32; return true;
                case "bridge_horz": x = LocX - 31; y = LocY + 31; x2 = LocX + w + 32; y2 = LocY + 32; return true;
                default: return false;
            }
            x2 = x; y2 = y; return true;
        }
        public bool GetCraneCenter(out int x, out int y)
        {
            x = y = 0;
            switch (Type)
            {
                case "robot_factory": x = LocX + 35; y = LocY + 32; return true;
                case "vehicle_factory": x = LocX + 31; y = LocY + 32; return true;
                case "repair": x = LocX + 32; y = LocY + 32; return true;
                case "radar": x = LocX + 28; y = LocY + 24; return true;
                case "bridge_vert": case "bridge_horz": x = LocX + (WidthPix >> 1); y = LocY + (HeightPix >> 1); return true;
            }
            return false;
        }
        public bool GetRepairEntrance(out int x, out int y) { x = LocX + 32; y = LocY + 96; return Type == "repair"; }   // brepair.cpp:459-471
        public bool GetRepairCenter(out int x, out int y) { x = LocX + 32; y = LocY + 32; return Type == "repair"; }

        // ------------------------------------------------------------------ rules
        public bool CanEnterFort(Team team) => IsFort && team != Owner && !Destroyed;                     // bfort.cpp:483-489
        public bool CanBeRepairedByCrane(Team team) =>                                                     // zobject.cpp:4494-4504
            Kind == ZKind.Building && !IsFort &&
            (Destroyed ? ZWorld.CraneAnyRuinDeviation || Owner == Team.Neutral || team == Owner            // DV74: a destroyed building / bridge in any zone (Zod: own or neutral)
                       : (Owner == Team.Neutral || team == Owner) && ZWorld.CraneFixDeviation && Health < MaxHealth && GetCraneCenter(out _, out _));   // DV61: a damaged standing one too (own or neutral)
        public bool CanBeRepaired() => Kind == ZKind.Vehicle && !Destroyed && Health < MaxHealth;        // zvehicle.cpp:170-176
        public bool CanRepairUnit(Team team) => Type == "repair" && Owner != Team.Neutral && team == Owner && !Destroyed;   // brepair.cpp:473-480

        // ------------------------------------------------------------------ production (zbuilding.cpp)
        /// <summary>zbuilding.cpp:633-667: T = base * (1 - 0.5 z) * (1 + 1.25 (1 - hp/max)), from the ORIGINAL start time.</summary>
        public double BuildTimeModified(int baseSeconds)
        {
            double z = Math.Max(0, Math.Min(1, ZoneOwnage));
            double t = baseSeconds - baseSeconds * 0.5 * z;
            double hp = MaxHealth > 0 ? (double)Health / MaxHealth : 1;
            t += t * 1.25 * (1.0 - hp);
            return t;
        }
        public void RecalcBuildTime()
        {
            if (!IsProducer || CurUnit == null) return;
            BFinal = BInit + BuildTimeModified(World.S.Unit(CurUnit)?.build_time ?? 0);
        }
        /// <summary>zbuilding.cpp:621-631: only when the fraction really changed.</summary>
        public void ResetBuildTime(double zoneOwnage)
        {
            if (zoneOwnage == ZoneOwnage) return;
            ZoneOwnage = zoneOwnage;
            RecalcBuildTime();
        }

        /// <summary>zbuilding.cpp:115-144 SetBuildingProduction (no destroyed check, like Zod).</summary>
        public bool SetBuildingProduction(string unit)
        {
            if (Owner == Team.Neutral || !IsProducer || unit == null) return false;
            if (World.GameOver && ZWorld.EndFreezeDeviation) return false;                                // endgame_ui_v1 (DV10): no production after the win
            if (unit == CurUnit) return false;
            if (!ZBuildLists.InList(Type, BuildLevel, unit)) return false;
            CurUnit = unit; Building = true; BInit = World.Time;
            RecalcBuildTime();
            if (Queue.Count == 0 && !ZWorld.FifoQueueDeviation) Queue.Add(unit);                           // the repeat copy (Zod; DV21 repeats by flag)
            return true;
        }
        /// <summary>zbuilding.cpp:146-168: new entries go to the FRONT (built next, LIFO).</summary>
        public bool AddBuildingQueue(string unit, bool pushToFront = true)
        {
            if (Owner == Team.Neutral || !IsProducer || Queue.Count >= MAX_QUEUE_ITEMS) return false;
            if (World.GameOver && ZWorld.EndFreezeDeviation) return false;                                // endgame_ui_v1 (DV10)
            if (!ZBuildLists.InList(Type, BuildLevel, unit)) return false;
            if (pushToFront && !ZWorld.FifoQueueDeviation) Queue.Insert(0, unit); else Queue.Add(unit);   // DV21: first in, first out
            return true;
        }
        public bool CancelBuildingQueue(int i, string unit)                                                // zbuilding.cpp:170-189
        {
            if (i < 0 || i >= Queue.Count || Queue[i] != unit) return false;
            Queue.RemoveAt(i); return true;
        }
        public void StopBuildingProduction(bool clearQueue = true)                                         // zbuilding.cpp:275-287
        {
            Building = false; CurUnit = null;
            if (clearQueue) Queue.Clear();
        }
        /// <summary>zbuilding.cpp:100-113: an idle building starts build-list entry 0.</summary>
        public bool SetBuildingDefaultProduction()
        {
            if (!IsProducer || Building || CurUnit != null) return false;
            var list = ZBuildLists.For(Type, BuildLevel);
            return list.Count > 0 && SetBuildingProduction(list[0]);
        }
        /// <summary>zbuilding.cpp:477-506: pop the front item and start it (it repeats when it was the last one).</summary>
        public void ResetProduction()
        {
            if (ZWorld.FifoQueueDeviation)
            {   // DV21: the next queued unit in order; an empty queue repeats the unit just built while REPEAT is on, else goes idle
                string next = Queue.Count > 0 ? Queue[0] : RepeatProduction ? CurUnit : null;
                if (Queue.Count > 0) Queue.RemoveAt(0);
                StopBuildingProduction(false);
                if (next != null) SetBuildingProduction(next);
                return;
            }
            if (Queue.Count > 0)
            {
                var u = Queue[0]; Queue.RemoveAt(0);
                StopBuildingProduction(false);
                SetBuildingProduction(u);
            }
            else StopBuildingProduction();
        }
        public double ProductionProgress => !Building || BFinal <= BInit ? 0 : Math.Max(0, Math.Min(1, (World.Time - BInit) / (BFinal - BInit)));
        public double ProductionTimeLeft => Building ? Math.Max(0, BFinal - World.Time) : 0;

        /// <summary>Building part of ProcessServer: BuildUnit (zbuilding.cpp:401-416) and RepairUnit (brepair.cpp:548-563).</summary>
        void ProcessBuilding(double time)
        {
            BuildUnitType = null; RepairUnitReady = false;
            if (IsProducer && Building && CurUnit != null && Owner != Team.Neutral && time >= BFinal && !World.UnitLimitReached(Owner)
                && !(World.GameOver && ZWorld.EndFreezeDeviation))                                   // endgame_ui_v1 (DV10): nothing is built after the win
                BuildUnitType = CurUnit;
            if (Type == "repair" && ZWorld.RepairInPlaceDeviation) RepairBayTick(time);
            else if (Type == "repair" && RepairingUnit && time >= RepairTime) { RepairingUnit = false; RepairUnitReady = true; }
            if (IsFort) { ProcessFortGun(time); FortDefenderStep(time); }                                  // DV7 fort gun (OpenZ); fort_defender_v1 DV41 roof defender
        }

        /// <summary>brepair.cpp:487-512 SetRepairUnit: store the vehicle and start the flat 5 s.</summary>
        public bool SetRepairUnit(ZObject unit)
        {
            if (unit == null || RepairingUnit || !CanRepairUnit(unit.Owner)) return false;
            RepairType = unit.Type; RepairDriverType = unit.DriverType;
            RepairDrivers.Clear(); RepairDrivers.AddRange(unit.Drivers);
            RepairCrew = unit.CrewOf();                                                                    // veterancy_v1 DV12
            RepairWaypoints.Clear(); RepairWaypoints.AddRange(unit.Waypoints);
            StoreRepairPassengers(unit);                                                                   // apc_v1 DV15: the passengers ride along
            HeroSeatsStore(unit);                                                                          // DV108: and who of a hero squad sits where (Sim/ZHeroSquad.cs)
            RepairingUnit = true; RepairTime = World.Time + 5;
            return true;
        }

        // ------------------------------------------------------------------ unit waypoints that use buildings
        /// <summary>zobject.cpp:2774-2913: path to the gate, walk inside (fort destroyed), walk back out.</summary>
        void ProcessEnterFortWP(double dt, bool isNew)
        {
            var wp = Waypoints[0];
            var t = World.Get(wp.refId);
            if (t == null || t.Kind != ZKind.Building) { KillWP(); return; }
            if (isNew)
            {
                cur.stage = Stage.GotoEntrance;
                if (!t.GetCreationMovePoint(out int ex, out int ey)) { KillWP(); return; }
                cur.exitX = ex; cur.exitY = ey;
                SetTarget(ex, ey);
                StartPath();
            }
            if (!t.CanEnterFort(Owner))
            {
                if (cur.stage == Stage.GotoEntrance) { KillWP(); return; }
                if (cur.stage == Stage.EnterBuilding) { SetTarget(cur.exitX, cur.exitY); cur.stage = Stage.ExitBuilding; SetVelocity(); }
            }
            FortRushStep(t);                                                                               // boarding_nb_v1 DV34 (ZObject.BoardingNB.cs)
            if (!fortRush && CheckAttackTo(wp)) return;                                                    // DV34: a robot rushing the door never turns off to fight
            if (!cur.gotPfResponse) return;
            if (!ProcessMoveOrKillWP(dt, cur.stage == Stage.GotoEntrance)) return;
            if (!ReachedTarget()) return;
            switch (cur.stage)
            {
                case Stage.GotoEntrance:
                    if (NextPathPoint()) break;
                    if (!t.GetCreationPoint(out int cx, out int cy)) { KillWP(); break; }
                    SetTarget(cx, cy); SetVelocity(); cur.stage = Stage.EnterBuilding;
                    break;
                case Stage.EnterBuilding:
                    SetTarget(cur.exitX, cur.exitY); cur.stage = Stage.ExitBuilding;
                    DestroyFortId = t.Id;                                                                  // the fort dies
                    SetVelocity();
                    break;
                default: KillWP(); break;
            }
        }

        /// <summary>DV61: sim time the crane's mending of a damaged standing building ends (-1 = none).</summary>
        double craneFixUntil = -1;
        /// <summary>DV61 proof counter: damaged-building mends started.</summary>
        public static int CraneFixes;

        /// <summary>zobject.cpp:2915-3091: path to the (nearest) crane entrance, drive to the centre, drive back out, repair.</summary>
        void ProcessCraneRepairWP(double dt, bool isNew)
        {
            var wp = Waypoints[0];
            var t = World.Get(wp.refId);
            if (t == null || t.Kind != ZKind.Building) { KillWP(); return; }
            if (isNew)
            {
                cur.stage = Stage.GotoEntrance; craneFixUntil = -1;
                if (!t.GetCraneEntrance(out int ex, out int ey, out int ex2, out int ey2)) { KillWP(); return; }
                if (ex == ex2 && ey == ey2) SetTarget(ex, ey);
                else
                {
                    double d1 = Math.Sqrt(Math.Pow(LocX - ex, 2) + Math.Pow(LocY - ey, 2)), d2 = Math.Sqrt(Math.Pow(LocX - ex2, 2) + Math.Pow(LocY - ey2, 2));
                    if (d1 < d2) SetTarget(ex, ey); else SetTarget(ex2, ey2);                               // Zod measures from loc (top-left)
                }
                cur.exitX = cur.x; cur.exitY = cur.y;
                StartPath();
            }
            if (!t.CanBeRepairedByCrane(Owner))
            {
                if (cur.stage == Stage.GotoEntrance) { KillWP(); return; }
                if (cur.stage == Stage.EnterBuilding) { SetTarget(cur.exitX, cur.exitY); cur.stage = Stage.ExitBuilding; SetVelocity(); }
            }
            if (!cur.gotPfResponse) return;
            if (!ProcessMoveOrKillWP(dt, cur.stage == Stage.GotoEntrance)) return;
            if (!ReachedTarget()) return;
            switch (cur.stage)
            {
                case Stage.GotoEntrance:
                    if (NextPathPoint()) break;
                    if (!t.Destroyed && ZWorld.CraneFixDeviation)
                    {   // DV61: a damaged building that still stands is mended from its entrance (the crane never drives into the walls)
                        cur.stage = Stage.ExitBuilding; SetTarget(CenterX, CenterY); SetVelocity();
                        craneFixUntil = World.Time + ZWorld.CraneFixSeconds; CraneFixes++;
                        World.Emit(ZEventType.CraneAnim, this, t, 0, 0, 0, 0, true);
                        break;
                    }
                    if (!t.GetCraneCenter(out int cx, out int cy)) { KillWP(); break; }
                    cur.stage = Stage.EnterBuilding; SetTarget(cx, cy); SetVelocity();
                    World.Emit(ZEventType.CraneAnim, this, t, 0, 0, 0, 0, true);
                    break;
                case Stage.EnterBuilding:
                    SetTarget(cur.exitX, cur.exitY); SetVelocity(); cur.stage = Stage.ExitBuilding;
                    break;
                case Stage.ExitBuilding:
                    if (craneFixUntil >= 0 && World.Time + 1e-9 < craneFixUntil) break;                     // DV61: still mending at the entrance
                    craneFixUntil = -1;
                    t.DoAutoRepair = true; t.NextAutoRepairTime = 0;                                       // repaired on the next tick
                    World.Emit(ZEventType.CraneAnim, this, t, 0, 0, 0, 0, false);
                    KillWP();
                    break;
                default: KillWP(); break;
            }
        }

        /// <summary>zobject.cpp:3093-3283: path to the entrance, wait while the station is busy, drive in (the server swaps
        /// the vehicle for a fresh copy after 5 s), or back out if the station can't take us any more.</summary>
        void ProcessUnitRepairWP(double dt, bool isNew)
        {
            var wp = Waypoints[0];
            var t = World.Get(wp.refId);
            if (t == null || t.Kind != ZKind.Building) { KillWP(); return; }
            int ex, ey;
            if (isNew)
            {
                cur.stage = Stage.GotoEntrance;
                if (!t.GetRepairEntrance(out ex, out ey)) { KillWP(); return; }
                if (ZWorld.RepairInPlaceDeviation) RepairQueueSpot(t, this, out ex, out ey);              // DV62: wait clear of the doorstep
                SetTarget(ex, ey);
                StartPath();
                RepairTripStart();                                                                        // DV62: the spot to come back to
            }
            if (ZWorld.RepairInPlaceDeviation && RepairBayStep(dt, t)) return;                           // DV62: on the pad / driving off it
            if (!t.CanRepairUnit(Owner) || !CanBeRepaired())
            {
                if (cur.stage == Stage.EnterBuilding)
                {
                    if (!t.GetRepairEntrance(out ex, out ey)) { KillWP(); return; }
                    SetTarget(ex, ey); cur.stage = Stage.ExitBuilding; SetVelocity();
                }
                else { KillWP(); return; }
            }
            if (CheckAttackTo(wp)) return;
            if (!cur.gotPfResponse) return;
            if (t.RepairingUnit || t.BayTakenFor(this))                                                 // DV62: also while the last one drives off the pad
            {
                if (cur.stage == Stage.EnterBuilding)
                {
                    if (!t.GetRepairEntrance(out ex, out ey)) { KillWP(); return; }
                    SetTarget(ex, ey); cur.stage = Stage.ExitBuilding; SetVelocity();
                }
                else if (cur.stage == Stage.Wait) return;
            }
            bool stoppable = !(cur.stage == Stage.EnterBuilding || cur.stage == Stage.ExitBuilding);
            if (!ProcessMoveOrKillWP(dt, stoppable)) return;
            if (!ReachedTarget()) return;
            switch (cur.stage)
            {
                case Stage.GotoEntrance:
                    if (NextPathPoint()) break;
                    cur.stage = Stage.Wait; SetVelocity();
                    break;
                case Stage.ExitBuilding:
                    cur.stage = Stage.Wait; SetVelocity();
                    break;
                case Stage.EnterBuilding:
                    if (ZWorld.RepairInPlaceDeviation) { RepairBayArrive(t); break; }                     // DV62: stays on the pad, visible
                    EnteredRepairId = t.Id;
                    if (!t.GetRepairEntrance(out ex, out ey)) { KillWP(); return; }                         // leave if not accepted
                    SetTarget(ex, ey); cur.stage = Stage.ExitBuilding; SetVelocity();
                    break;
                case Stage.Wait:
                    if (!t.GetRepairCenter(out int cx, out int cy)) { KillWP(); return; }
                    if (ZWorld.RepairInPlaceDeviation) { if (t.RepairingUnit || t.BayTakenFor(this)) break; t.RepairOccupantId = Id; }   // DV62: claim the bay before driving in (no two in the bay)
                    cur.stage = Stage.EnterBuilding; SetTarget(cx, cy); SetVelocity();
                    break;
                default: KillWP(); break;
            }
        }
    }
}
