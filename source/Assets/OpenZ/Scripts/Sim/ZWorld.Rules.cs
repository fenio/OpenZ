using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenZ.Sim
{
    /// <summary>Zone rectangle in pixels (zmap.h:97-103, built in zmap.cpp:1676-1771; owner starts NULL).</summary>
    public sealed class ZZone
    {
        public int Id, X, Y, W, H;
        public Team Owner = Team.Neutral;
        public ZObject Flag;                                   // null = fort zone (no flag)
        public bool Contains(int px, int py) => X <= px && px <= X + W && Y <= py && py <= Y + H;   // inclusive (zmap.cpp:1656-1669)
    }

    /// <summary>
    /// The rules of Z (zserver.cpp, zcore.cpp, zbuilding.cpp, oflag.cpp): zones and flags (touch = capture, checked every
    /// 0.2 s), production with the flag-fraction build time, unit cap 70, cannon storage (4 per zone) and placement, the
    /// repair station, crane repairs, fort entry, team elimination, and the 1 s game-end check.
    /// </summary>
    public sealed partial class ZWorld
    {
        public const int MAX_UNITS_PER_TEAM = 70;              // constants.h:21
        /// <summary>OpenZ override: robots also pick up grenade crates they walk over (Zod: pickup order / idle auto-grab only).</summary>
        public bool WalkOverGrenadePickup = true;
        public readonly List<ZZone> Zones = new List<ZZone>();
        public bool GameOver { get; private set; }
        public Team Winner { get; private set; } = Team.Neutral;
        /// <summary>
        /// at the game over every factory and fort stops producing (queues cleared), the CPU bots stop, every unit still on the map
        /// stops and disengages (so the winners stand and cheer), and PlayerInput takes no more orders. Zod keeps processing objects
        /// after ProcessEndGame (zserver.cpp:277-300: game_on only gates the end check) until the map resets 10 s later. Post-game
        /// only: nothing before the game over changes (parity rows and bot matches end at the game over).</summary>
        public static bool EndFreezeDeviation = true;
        /// <summary>
        /// the last unit is a per-building REPEAT toggle (Zod: always). Off = Zod's LIFO queue with its repeat copy.</summary>
        public static bool FifoQueueDeviation = true;
        double nextFlagCheck, nextEndCheck;

        public ZZone ZoneOf(ZObject o) => o != null && o.ZoneIndex >= 0 && o.ZoneIndex < Zones.Count ? Zones[o.ZoneIndex] : null;

        /// <summary>zmap.cpp:1656-1669 GetZone: the FIRST zone in list order containing the point (inclusive edges).</summary>
        public int GetZoneIndex(int px, int py)
        {
            for (int i = 0; i < Zones.Count; i++) if (Zones[i].Contains(px, py)) return i;
            return -1;
        }

        void BuildZones()
        {
            Zones.Clear();
            if (Map.zones == null) return;
            for (int i = 0; i < Map.zones.Count; i++)
            {
                var z = Map.zones[i];
                Zones.Add(new ZZone { Id = i, X = z.x * 16, Y = z.y * 16, W = z.w * 16, H = z.h * 16 });
            }
        }

        /// <summary>zserver.cpp:551-606: in object order, forts claim their zone and its buildings, flags link their zone's
        /// buildings and give them the flag's owner; then default productions and the flag fractions.</summary>
        void InitRules()
        {
            foreach (var o in Objects)
            {
                var z = ZoneOf(o); if (z == null) continue;
                if (o.IsFort)
                {
                    z.Owner = o.Owner;
                    foreach (var b in Objects) if (b.Kind == ZKind.Building && b.ZoneIndex == o.ZoneIndex) SetOwnerZod(b, o.Owner, false);
                }
                else if (o.Type == "flag")
                {
                    z.Flag = o; z.Owner = o.Owner;
                    o.LinkedObjects.Clear();
                    foreach (var b in Objects) if (b.Kind == ZKind.Building && b.ZoneIndex == o.ZoneIndex) { o.LinkedObjects.Add(b); SetOwnerZod(b, o.Owner, false); }
                }
            }
            foreach (var b in Objects) if (b.IsProducer) b.SetBuildingDefaultProduction();
            ResetZoneOwnagePercentages();
            nextFlagCheck = Time; nextEndCheck = Time;
        }

        /// <summary>ResetObjectTeam for rules: bridges ignore owners (bbridge.cpp:449-452).</summary>
        void SetOwnerZod(ZObject o, Team t, bool emit = true)
        {
            if (o.IsBridge) return;
            if (o.Owner != t && o.IsProducer) o.RepeatProduction = true;                        // DV21: a captured factory starts with REPEAT on
            o.Owner = t;
            if (emit) Emit(ZEventType.OwnerChanged, o, null, 0, 0, (float)t);
        }

        // ------------------------------------------------------------------ flags + zones (zserver.cpp:1967-2063, 2600-2632)
        public double TeamFlagFraction(Team t)
        {
            if (Flags.Count == 0) return 0;
            int n = 0; foreach (var f in Flags) if (f.Owner == t) n++;
            return (double)n / Flags.Count;
        }

        void ResetZoneOwnagePercentages()
        {
            foreach (var b in Objects) if (b.IsProducer) b.ResetBuildTime(TeamFlagFraction(b.Owner));
        }

        /// <summary>Every 0.2 s: the first enemy robot / vehicle whose box touches a flag takes its zone.</summary>
        void CheckFlagCaptures()
        {
            if (Time <= nextFlagCheck) return;
            nextFlagCheck = Time + 0.2;
            foreach (var f in Flags)
                foreach (var o in Objects)
                {
                    if (o.Owner == Team.Neutral || f.Owner == o.Owner) continue;
                    if (!(o.Kind == ZKind.Vehicle || o.Kind == ZKind.Robot) || o.IsFortDefender) continue;   // fort_defender_v1 DV41
                    if (!f.IntersectsObject(o)) continue;
                    AwardZone(f, o.Owner, o);
                    break;
                }
        }

        /// <summary>zserver.cpp:2009-2063: flag, linked buildings (default production if idle) and zone switch team;
        /// units inside are untouched; build times re-scale to the new flag fractions.</summary>
        public void AwardZone(ZObject flag, Team team, ZObject conqueror = null)
        {
            var z = ZoneOf(flag);
            var old = z?.Owner ?? flag.Owner;
            SetOwnerZod(flag, team);
            bool radar = false;
            foreach (var b in flag.LinkedObjects)
            {
                SetOwnerZod(b, team);
                if (b.SetBuildingDefaultProduction()) Emit(ZEventType.ProductionChanged, b);
                if (b.Type == "radar") radar = true;
            }
            if (z != null) z.Owner = team;
            Events.Add(new ZEvent { type = ZEventType.FlagCaptured, objId = flag.Id, otherId = conqueror?.Id ?? -1, x = flag.CenterX, y = flag.CenterY,
                                    value = (float)team, radius = (int)old, flag = radar });
            ResetZoneOwnagePercentages();
        }

        // ------------------------------------------------------------------ production output (zserver.cpp:1573-1637)
        public int UnitCount(Team t)
        {
            int n = 0;
            foreach (var o in Objects) if (o.Owner == t && o.IsUnit && !o.IsFortDefender) n++;            // zcore.cpp:712-747; fort_defender_v1 DV41: not a unit of the cap
            return n;
        }
        public bool UnitLimitReached(Team t) => t != Team.Neutral && UnitCount(t) >= MAX_UNITS_PER_TEAM;

        /// <summary>zbuilding.cpp:429-452: stored cannons of every building in the zone + every cannon object in the zone (any team).</summary>
        public int CannonsInZone(ZObject b)
        {
            int n = b.StoredCannons.Count;
            if (b.ZoneIndex < 0) return n;
            foreach (var o in Objects)
            {
                if (o.ZoneIndex != b.ZoneIndex) continue;
                if (o != b && o.Kind == ZKind.Building) n += o.StoredCannons.Count;
                if (o.Kind == ZKind.Cannon) n++;
            }
            return n;
        }

        ZObject BuildingCreateUnit(ZObject b, string type)
        {
            if (ZBuildLists.IsCannon(type))
            {
                if (CannonsInZone(b) < ZObject.MAX_STORED_CANNONS && b.StoredCannons.Count < ZObject.MAX_STORED_CANNONS)
                { b.StoredCannons.Add(type); Emit(ZEventType.CannonStored, b, null, 0, 0, (float)(Time - b.BInit)); }
                return null;                                                                                // else silently discarded
            }
            if (!b.GetCreationPoint(out int cx, out int cy)) return null;
            ZObject u;
            if (ZBuildLists.IsRobot(type))
            {
                u = CreateRobotGroup(type, cx, cy, b.Owner);
                int w = u.WidthPix, h = u.HeightPix;
                u.SetCords(cx - (w >> 1), cy - (h >> 1)); foreach (var m in u.Minions) m.SetCords(cx - (w >> 1), cy - (h >> 1));
            }
            else
            {
                u = CreateObject("vehicle", type, cx, cy, b.Owner);
                u.SetCords(cx - (u.WidthPix >> 1), cy - (u.HeightPix >> 1));
            }
            if (b.GetCreationMovePoint(out int ex, out int ey))
                AddExitWaypoints(b, u, cx, cy, ex, ey);                                   // Zod: FORCE_MOVE to the exit; DV66: a taken exit -> the nearest free exit cell (ZWorld.ExitLanes.cs)
            u.CloneMinionWayPoints();
            Emit(ZEventType.Created, u, b); foreach (var m in u.Minions) Emit(ZEventType.Created, m, b);
            if (type != ZElectro.Type) b.BuiltCount++;                                   // DV71: the CPU's Laser mix counts a producer's built units (ZBot.LaserPick); the Electrobot is outside that rhythm
            Emit(ZEventType.UnitBuilt, u, b, 0, 0, (float)(Time - b.BInit));              // value = elapsed build time
            return u;
        }

        /// <summary>zserver.cpp:1496-1571: the fresh vehicle leaves the station and resumes its orders.</summary>
        void BuildingRepairUnit(ZObject st)
        {
            if (!st.GetRepairCenter(out int x, out int y)) return;
            var u = CreateObject("vehicle", st.RepairType, x, y, st.Owner);
            if (st.RepairDrivers.Count > 0) { u.DriverType = st.RepairDriverType; u.Drivers.Clear(); u.Drivers.AddRange(st.RepairDrivers); u.ResetDamageInfo(); }
            if (st.RepairDrivers.Count > 0) u.SetCrew(st.RepairCrew);                                    // veterancy_v1 DV12
            st.RestoreRepairPassengers(u);                                                               // apc_v1 DV15
            u.SetCords(x - (u.WidthPix >> 1), y - (u.HeightPix >> 1));
            st.GetRepairEntrance(out int ex, out int ey);
            u.Waypoints.Add(new ZWaypoint { mode = WpMode.ForceMove, refId = -1, x = ex, y = ey });
            for (int i = 1; i < st.RepairWaypoints.Count; i++) u.Waypoints.Add(st.RepairWaypoints[i]);
            u.CloneMinionWayPoints();
            Emit(ZEventType.Created, u, st);
            Emit(ZEventType.RepairOut, u, st);
        }

        /// <summary>zserver.cpp:1350-1373</summary>
        void UnitEnterRepairBuilding(ZObject u, ZObject st)
        {
            if (u == null || st == null) return;
            if (!st.SetRepairUnit(u)) return;
            u.KillAt = Time;                                                                                // DoKillMe(0): gone, not dead
            Emit(ZEventType.RepairIn, u, st);
        }

        /// <summary>Side effects of the rules for one object (zserver.cpp:1217-1280, same order).</summary>
        void RulesSideEffects(ZObject o)
        {
            if (o.DestroyFortId >= 0)
            {
                var f = Get(o.DestroyFortId);
                if (f != null && !f.Destroyed) { Emit(ZEventType.FortEntered, o, f); f.SetHealth(0); UpdateObjectHealth(f, o.Id); }
            }
            if (o.BuildUnitType != null)
            {
                var type = o.BuildUnitType; o.BuildUnitType = null;
                var u = BuildingCreateUnit(o, type);
                o.ResetProduction();
                Emit(ZEventType.ProductionChanged, o);
                if (u != null) { foreach (var r in o.RallyPoints) u.Waypoints.Add(r); u.CloneMinionWayPoints(); }
            }
            if (o.RepairUnitReady) { o.RepairUnitReady = false; BuildingRepairUnit(o); }
            if (o.EnteredRepairId >= 0) UnitEnterRepairBuilding(o, Get(o.EnteredRepairId));
        }

        // ------------------------------------------------------------------ elimination + game end (zserver.cpp:277-367, 1675-1729)
        void EliminateTeam(ZObject fort, Team team)
        {
            Events.Add(new ZEvent { type = ZEventType.TeamEliminated, objId = fort.Id, x = fort.CenterX, y = fort.CenterY, value = (float)team });
            foreach (var o in Objects.ToList())
                if (o != fort && o.Owner == team && o.Kind != ZKind.MapItem && !o.Destroyed && o.MaxHealth > 0) { o.SetHealth(0); UpdateObjectHealth(o); }
            foreach (var f in Flags.ToList()) if (f.Owner == team) AwardZone(f, Team.Neutral);
        }

        /// <summary>Every 1 s: the game ends when at most one team still owns a robot, vehicle or cannon.</summary>
        void CheckEndGame()
        {
            if (GameOver) { if (EndFreezeDeviation) HoldAfterGameOver(); return; }
            if (Time < nextEndCheck) return;
            nextEndCheck = Time + 1.0;
            var teams = new HashSet<Team>();
            foreach (var o in Objects) if (o.IsUnit && o.Owner != Team.Neutral && !o.Destroyed && !o.IsFortDefender) teams.Add(o.Owner);   // fort_defender_v1 DV41
            if (teams.Count > 1) return;
            GameOver = true; Winner = teams.Count == 1 ? teams.First() : Team.Neutral;
            Events.Add(new ZEvent { type = ZEventType.GameOver, value = teams.Count == 1 ? (float)Winner : -1 });
            if (EndFreezeDeviation) FreezeAfterGameOver();                                     // endgame_ui_v1 (DV10)
        }

        /// <summary>endgame_ui_v1 (DV10): production off, every unit on the map halts (queued orders dropped, targets released). Units in
        /// a step that Zod never interrupts (a force move, the inside of a repair / crane / fort entry) finish it. No events.</summary>
        void FreezeAfterGameOver()
        {
            foreach (var o in Objects)
            {
                if (o.Destroyed) continue;
                if (o.IsProducer && (o.Building || o.CurUnit != null || o.Queue.Count > 0)) o.StopBuildingProduction();
                if (o.IsUnit) o.Disengage();
                if (!o.IsMobile || o.IsMinion || !o.CanOverwriteWP()) continue;
                o.Waypoints.Clear(); o.CloneMinionWayPoints();
                o.StopMove(); foreach (var m in o.Minions) { m.StopMove(); m.Disengage(); }
            }
        }

        /// <summary>
        /// waypoint a unit picks up later is automatic: the pathing layer's step-aside off a bridge deck (ZBridges.SendOutOfKeep), back-off,
        /// squad regroup and waiting-bay MOVEs, Zod's ScuffleUnits nudge, a dodge, or the rally points left behind a factory exit's force
        /// move. The winners stand: each tick after the game over those are dropped (a step Zod never interrupts, CanOverwriteWP false,
        /// still finishes first). Runs in CheckEndGame, after Pathing.EndTick, so a waypoint added in the tick is gone before it moves.</summary>
        void HoldAfterGameOver()
        {
            foreach (var o in Objects)
            {
                if (o.Destroyed || !o.IsMobile || o.Waypoints.Count == 0 || !o.CanOverwriteWP()) continue;
                o.Waypoints.Clear();
                if (!o.IsMinion) o.CloneMinionWayPoints();
                o.StopMove();
            }
        }

        /// <summary>zobject.cpp:4215-4232 HasDestroyedFortInZone. Zod tests `oid == FORT_FRONT || oid == FORT_FRONT`, so only a
        /// destroyed FORT_FRONT blocks the auto-repair of its zone's buildings (kept 1:1; after an elimination the game is over anyway).</summary>
        bool HasDestroyedFortInZone(ZObject b)
        {
            if (b.ZoneIndex < 0) return false;
            foreach (var o in Objects) if (o.Kind == ZKind.Building && o.ZoneIndex == b.ZoneIndex && o.Destroyed && o.Type == "fort_front") return true;
            return false;
        }

        // ------------------------------------------------------------------ commands (zserver_events.cpp: START/STOP/QUEUE/CANCEL/PLACE)
        static bool Owns(Team team, ZObject b) => b != null && team != Team.Neutral && b.Owner == team && b.Kind == ZKind.Building;

        public bool StartBuilding(Team team, ZObject b, string unit)
        {
            if (!Owns(team, b)) return false;
            bool ok = b.SetBuildingProduction(unit); if (ok) Emit(ZEventType.ProductionChanged, b); return ok;
        }
        public bool StopBuilding(Team team, ZObject b)
        {
            if (!Owns(team, b)) return false;
            b.StopBuildingProduction(); Emit(ZEventType.ProductionChanged, b); return true;
        }
        public bool AddBuildingQueue(Team team, ZObject b, string unit)
        {
            if (!Owns(team, b)) return false;
            bool ok = !b.Building ? b.SetBuildingProduction(unit) : b.AddBuildingQueue(unit);
            if (ok) Emit(ZEventType.ProductionChanged, b); return ok;
        }
        /// <summary>DV21 command: the REPEAT toggle of a factory / fort the team owns.</summary>
        public bool SetBuildingRepeat(Team team, ZObject b, bool repeat)
        {
            if (!Owns(team, b) || !b.IsProducer) return false;
            b.RepeatProduction = repeat; Emit(ZEventType.ProductionChanged, b); return true;
        }
        public bool CancelBuildingQueue(Team team, ZObject b, int i, string unit)
        {
            if (!Owns(team, b)) return false;
            bool ok = b.CancelBuildingQueue(i, unit); if (ok) Emit(ZEventType.ProductionChanged, b); return ok;
        }
        public bool SetRallyPoints(Team team, ZObject b, IList<ZWaypoint> pts)
        {
            if (!Owns(team, b) || !b.IsProducer) return false;
            b.RallyPoints.Clear();
            foreach (var p in pts) b.RallyPoints.Add(new ZWaypoint { mode = WpMode.Move, refId = -1, x = p.x, y = p.y, attackTo = p.attackTo });   // MOVE only (zcore.cpp:189-194)
            return true;
        }

        /// <summary>zcore.cpp:637-676: 2x2 tiles inside the zone shrunk by 16 px, overlapping nothing but robots/vehicles
        /// (fort turret spots may overlap their fort); no terrain check, no distance limit.</summary>
        public bool CannonPlacable(ZObject b, int tx, int ty)
        {
            int x = tx * 16, y = ty * 16, right = x + 32, bottom = y + 32;
            if (tx < 0 || ty < 0 || tx > Map.width || ty > Map.height) return false;
            var z = ZoneOf(b); if (z == null) return false;
            if (x < z.X + 16 || y < z.Y + 16 || x + 32 > z.X + z.W - 16 || y + 32 > z.Y + z.H - 16) return false;
            foreach (var o in Objects)
            {
                if (o.Kind == ZKind.Vehicle || o.Kind == ZKind.Robot) continue;
                if (CannonNotPlacable(o, x, right, y, bottom)) return false;
            }
            if (!GunGroundOk(tx, ty)) return false;                                                    // DV80 gun ground: not on water, not on impassable ground (ZWorld.GunGround.cs)
            return true;
        }
        static bool WithinSelection(ZObject o, int l, int r, int t, int btm) =>
            !(l >= o.LocX + o.WidthPix || r <= o.LocX || t >= o.LocY + o.HeightPix || btm <= o.LocY);   // zobject.cpp:940-951
        static bool CannonNotPlacable(ZObject o, int l, int r, int t, int btm)
        {
            if (o.IsFort)                                                                                   // bfort.cpp:534-548
            {
                int lx = l - o.LocX, ly = t - o.LocY;
                if ((lx == 16 || lx == 112) && (ly == 0 || ly == 48)) return false;
            }
            return WithinSelection(o, l, r, t, btm);
        }
        bool AreaIsFortTurret(int tx, int ty)                                                               // zcore.cpp:678-699
        {
            int x = tx * 16, y = ty * 16;
            foreach (var o in Objects) if (o.IsFort && WithinSelection(o, x, x + 32, y, y + 32) && !CannonNotPlacable(o, x, x + 32, y, y + 32)) return true;
            return false;
        }

        /// <summary>place_cannon_event (zserver_events.cpp:557-598): the first stored cannon, with a Grunt driver; the unit
        /// cap does not apply; on a fort turret spot it becomes non-ejectable.</summary>
        public ZObject PlaceCannon(Team team, ZObject b, int tx, int ty, int index = 0)
        {
            if (!Owns(team, b) || index < 0 || index >= b.StoredCannons.Count) return null;
            if (!CannonPlacable(b, tx, ty)) return null;
            var type = b.StoredCannons[index]; b.StoredCannons.RemoveAt(index);
            var c = CreateObject("cannon", type, tx * 16, ty * 16, b.Owner);
            if (AreaIsFortTurret(tx, ty)) c.Ejectable = false;
            Emit(ZEventType.Created, c, b);
            Emit(ZEventType.CannonPlaced, c, b);
            return c;
        }
    }
}
