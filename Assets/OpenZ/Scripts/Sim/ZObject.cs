using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// A Zod object (zobject.cpp). Position = top-left pixel `loc` plus the sub-pixel carry `xover/yover`; every range test
    /// uses the centre. Ported: movement (ProcessMoveWP/ProcessMove/SetVelocity/ReachedTarget :2696-3650), squads,
    /// running, and combat: CheckPassiveEngage (:1603-1853), ProcessAttackDamage (:2102-2213, hitscan + snipe, missiles
    /// with EstimateMissileTarget lead :1855-1983 and scatter, grenades from the leader's pool), Agro/Attack/Enter/Pickup/
    /// Dodge waypoints (:2233-2694), CheckAttackTo attack-move (:3285-3339), DodgeMissile (:3657-3714), tank lids
    /// (zvehicle.cpp:178-240), drivers (:334-359), explosive-only targets and destroyable barriers.
    /// </summary>
    public partial class ZObject
    {
        public int Id;
        public Team Owner;
        public ZKind Kind;
        public string Type;                 // "grunt", "jeep", "fort_front", "rock", ...
        public ZUnitSettings U;             // unit settings (robots, vehicles, cannons), else null
        public ZMapData.Obj Source;         // map entry this object was created from (view binding)
        public int MemberIndex;             // 0 = leader / single object, 1.. = minions in creation order

        // location (object_location + xover/yover)
        public int LocX, LocY, WidthPix, HeightPix, CenterX, CenterY;
        public float Dx, Dy;
        public double XOver, YOver;
        public int MoveSpeed;
        public double RealMoveSpeed;

        public int Health, MaxHealth;
        public bool Destroyed => MaxHealth > 0 && Health <= 0;                     // zobject.cpp:283-286

        // ---- weapon (zobject.cpp:179-190; APC from its crew, vapc.cpp:266-342)
        public int Damage, AttackRadius, DamageRadius, MissileSpeed;
        public double DamageChance, SnipeChance, AttackSpeed;
        public bool CanSnipe, CanBeSnipedFlag, HasLid, DamageIsMissile, HasExplosivesFlag;
        public bool AttackedByExplosives, IsDestroyableImpass, CanBeDestroyed = true;
        /// <summary>DV67 detail props: the tiles (x, y pairs) this map item blocks (ZWorld.DetailProps.cs); null for every Zod object.</summary>
        public int[] BlockTiles;
        public ZObject AttackObject;
        double nextDamageTime, nextPassiveCheck;
        public bool LidOpen; bool doCloseLid; double nextCloseLidTime;
        public int Grenades;
        public double LastFireHit = -100, LastMissileHit = -100;

        // ---- drivers (zobject.h:209-213)
        public string DriverType = "grunt";
        public readonly List<int> Drivers = new List<int>();
        public bool Ejectable = true, JustLeftCannon;
        /// <summary>View/event only (no gameplay effect): the team that lost its driver in the last snipe. The sim sets the
        /// owner to neutral inside DamageDriverHealth (zobject.cpp:346-350) before the Sniped event, while Zod's client still
        /// had the old owner when it spawned the flying body (zplayer_events.cpp:1133).</summary>
        public Team SnipedFrom = Team.Neutral;

        // ---- life cycle
        public double KillAt = -1;                    // DoKillMe: deleted at the start of the next ProcessObjects
        public bool HasProcessedDeath;
        public bool DoAutoRepair; public double NextAutoRepairTime;

        // waypoints
        public readonly List<ZWaypoint> Waypoints = new List<ZWaypoint>();
        ZWaypoint lastWp = ZWaypoint.Cleared;

        enum Stage { None, Attack, Return, GotoEntrance, EnterBuilding, ExitBuilding, Wait, Repairing, RepairLeave }   // + DV62 repair pad stages (ZObject.RepairBay.cs)   // agro + crane/repair/fort stages (zobject.h)
        struct CurWp
        {
            public int x, y, sx, sy, adx, ady, pathFindingId; public bool gotPfResponse;
            public List<(int x, int y)> pfPoints;
            public int agroX, agroY, initAttackX, initAttackY, exitX, exitY; public Stage stage;
        }
        CurWp cur;

        // running
        public bool IsRunning;
        public double Stamina, MaxStamina;

        // squads
        public ZObject Leader;
        public readonly List<ZObject> Minions = new List<ZObject>();
        public bool IsMinion => Leader != null;

        // server flags (sflags)
        public bool UpdatedWaypoints, UpdatedVelocity, UpdatedAttackObjectHealth, UpdatedAttackObjectDriverHealth;
        public int EnteredTargetId = -1, DeleteGrenadeBoxId = -1;
        double lastProcessTime;
        internal ZWorld World;

        public bool IsRobot => Kind == ZKind.Robot;
        public bool IsUnit => Kind == ZKind.Robot || Kind == ZKind.Vehicle || Kind == ZKind.Cannon;
        public bool IsMobile => Kind == ZKind.Robot || Kind == ZKind.Vehicle;
        public bool CanMove => (Kind == ZKind.Robot || Kind == ZKind.Vehicle) && MoveSpeed > 0;
        public bool Selectable => ((Kind == ZKind.Robot && !IsMinion) || Kind == ZKind.Vehicle || Kind == ZKind.Cannon) && !IsFortDefender;   // zobject.cpp:888-894; fort_defender_v1 DV41: never the roof defender
        public bool IsMoving => !IsZ(Dx) || !IsZ(Dy);
        public double PosX => LocX + XOver;    // continuous top-left, for rendering
        public double PosY => LocY + YOver;
        public (int x, int y) CurrentTarget => (cur.x, cur.y);
        public IReadOnlyList<(int x, int y)> PathPoints => cur.pfPoints;
        public bool LastAttackPlayerGiven { get; private set; }

        static bool IsZ(double n) => n < 0.00001 && n > -0.00001;

        public void SetCords(int x, int y)
        {
            LocX = x; LocY = y;
            CenterX = x + (WidthPix >> 1); CenterY = y + (HeightPix >> 1);
        }

        public void InitRealMoveSpeed(ZMapData map) => RealMoveSpeed = MoveSpeed * map.TileWalkSpeed(CenterX, CenterY);   // :234-241

        public bool Contains(int px, int py) => px >= LocX && py >= LocY && px < LocX + WidthPix && py < LocY + HeightPix;
        /// <summary>UnderCursor (zobject.cpp:896-906): inclusive edges.</summary>
        public bool UnderCursor(int px, int py) => px >= LocX && py >= LocY && px <= LocX + WidthPix && py <= LocY + HeightPix;
        public bool IntersectsObject(ZObject o) =>
            LocX < o.LocX + o.WidthPix && o.LocX < LocX + WidthPix && LocY < o.LocY + o.HeightPix && o.LocY < LocY + HeightPix;

        // ------------------------------------------------------------------ capabilities
        public bool CanAttack() => Damage != 0 && !Destroyed;                                            // :4506-4509
        public bool HasExplosives() => HasExplosivesFlag || Grenades > 0 || (Leader != null && Leader.Grenades > 0);   // :4511-4519
        public bool CanAttackObject(ZObject o)                                                          // :4767-4777
        {
            if (o == null || !CanAttack() || o.Destroyed || Owner == o.Owner) return false;
            if (!HasExplosives() && o.AttackedByExplosives) return false;
            return true;
        }
        public bool CanBeEntered() => Owner == Team.Neutral && !Destroyed && (Kind == ZKind.Vehicle || Kind == ZKind.Cannon);   // :4723-4730
        public bool CanPickupGrenades() => Kind == ZKind.Robot && Type != "tough" && Type != ZElectro.Type && Grenades <= 0;   // zrobot.h:16, rtough.h:20; electrobot_v1 DV45: his zap, never grenades
        public bool CanEjectDrivers() => (Kind == ZKind.Vehicle && Type == "apc") || (Kind == ZKind.Cannon && Ejectable);   // vapc.cpp:261, zcannon.cpp:51
        public bool CanBeSniped()
        {
            if (Kind == ZKind.Cannon) return CanBeSnipedFlag && Drivers.Count > 0 && Ejectable;           // zcannon.cpp:46-49
            if (Kind == ZKind.Vehicle) return HasLid ? CanBeSnipedFlag && (LidOpen || Resting) && Drivers.Count > 0 : CanBeSnipedFlag && Drivers.Count > 0;   // DV75 idle rest (DC2): + the crew resting in the open hatch (Zod: the lid only, zvehicle.cpp:178-186)
            return false;                                                                                  // robots never
        }
        public bool CausesImpassAtCoord(int x, int y)
        {
            int tx = LocX / 16, ty = LocY / 16;
            if (Type == "rock") ty += 2;                                                                   // orock.cpp:675
            if (Type == ZWorld.WreckType) return ZWorld.WreckCausesImpassAt(this, x, y);                // l17_details_v1 DV39: any tile of a wreck's footprint
            if (Type == ZWorld.DetailPropType) return ZWorld.DetailPropCausesImpassAt(this, x, y);
            return (x >> 4) == tx && (y >> 4) == ty;
        }

        /// <summary>APC weapon from its crew (vapc.cpp:266-342): crew robot's stats, damage x crew size, Tough = missiles.</summary>
        public void ResetDamageInfo()
        {
            if (!(Kind == ZKind.Vehicle && Type == "apc")) return;
            AttackRadius = 0; Damage = 0; DamageChance = 0; AttackSpeed = 0; DamageIsMissile = false; DamageRadius = 0; MissileSpeed = 0;
            HasExplosivesFlag = false;
            if (Drivers.Count == 0) return;
            string crew = CrewWeaponType;                                                  // apc_v1 DV15: the passengers' weapon when aboard (ZApcCrew.cs), else the driver's
            var u = World.S.Unit(crew);
            AttackRadius = u.attack_radius; Damage = u.Damage; DamageChance = u.attack_damage_chance; AttackSpeed = u.attack_speed;
            DamageRadius = u.attack_damage_radius; MissileSpeed = ZWorld.RocketSpeed(crew, u.attack_missile_speed);   // DV52: a Tough crew's rockets like the Tough's
            if (crew == "tough") { HasExplosivesFlag = true; DamageIsMissile = true; }
            Damage *= CrewFirepower;                                                       // Zod: x driver_info.size() (vapc.cpp:341); DV15: x robots aboard with that weapon
        }

        // ------------------------------------------------------------------ server tick
        public void ProcessServer(double time)
        {
            double dt = time - lastProcessTime;
            lastProcessTime = time;
            UpdatedWaypoints = UpdatedVelocity = UpdatedAttackObjectHealth = UpdatedAttackObjectDriverHealth = false;
            EnteredTargetId = DeleteGrenadeBoxId = DestroyFortId = EnteredRepairId = -1;
            if (DoAutoRepair && time >= NextAutoRepairTime) { DoAutoRepair = false; World.AutoRepair(this); }   // :4235-4257
            ProcessRunStamina(dt);
            if (Destroyed) return;
            if (Kind == ZKind.Building) { ProcessBuilding(time); return; }                                    // BuildUnit / RepairUnit (:1456-1462)
            if (IsFortDefender) { FortDefenderManStep(time); return; }                                        // fort_defender_v1 DV41: no orders, no pathing, fires from his ring
            RestStep(time);                                                                                    // DV75 idle rest (DC2): a long-idle crew rests in the open hatch (ZObject.IdleRest.cs)
            ElectroTick();                                                                                     // electrobot_v1 DV45: tesla recharge end / dropped windup
            if (StunHold(time)) return;                                                                        // electrobot_v1 DV45: stunned: no move, still fires (ZObject.Electro.cs)
            ProcessServerLid(time);
            World.Pathing?.PreProcess(this);                                                                   // OpenZ pathing: deferred re-path / give-up / arrival

            LastAttackPlayerGiven = false;
            fortRush = false;                                                                                  // boarding_nb_v1 DV34: ProcessEnterFortWP sets it again
            if (UnmannedHold(time)) return;                                                                    // DV79 unmanned stop: nobody inside: no order, no step, no shot (ZObject.Unmanned.cs)
            PatrolStep();                                                                                      // DV68 (CTL1): a patrol turns round at the end of a leg
            if (Waypoints.Count > 0)
            {
                var wp = Waypoints[0];
                bool isNew;
                if (!wp.Equals(lastWp))
                {
                    isNew = true; lastWp = wp; if (World.Pathing == null || !World.Pathing.KeepSubPixelFor(this)) XOver = YOver = 0;   // DV44: no snap of the sub-pixel carry (ZCommit.Settle.cs)
                    cur = default;                         // clear_and_kill
                    SetTarget(CenterX, CenterY);           // initial "stop"
                    IsRunning = false;
                }
                else isNew = false;

                switch (wp.mode)
                {
                    case WpMode.Move: ProcessMoveWP(dt, isNew, true); break;
                    case WpMode.ForceMove: ProcessMoveWP(dt, isNew, false); break;
                    case WpMode.Dodge: ProcessDodgeWP(dt, isNew); break;
                    case WpMode.Enter: ProcessEnterWP(dt, isNew); break;
                    case WpMode.Attack: LastAttackPlayerGiven = wp.playerGiven; ProcessAttackWP(dt, isNew); break;
                    case WpMode.Agro: ProcessAgroWP(dt, isNew); break;
                    case WpMode.PickupGrenades: ProcessPickupWP(dt, isNew); break;
                    case WpMode.CraneRepair: ProcessCraneRepairWP(dt, isNew); break;
                    case WpMode.UnitRepair: ProcessUnitRepairWP(dt, isNew); break;
                    case WpMode.EnterFort: ProcessEnterFortWP(dt, isNew); break;
                    default: KillWP(); break;
                }
            }
            CheckWalkOverCrate();
            CheckPassiveEngage(time);
            ProcessAttackDamage(time, LastAttackPlayerGiven);
            CraneGunStep(time);                                                                       // vehicles_nb_v1 DV31: the crane's driver shoots back (ZObject.VehiclesNb.cs)
        }

        public void SetLastProcessTime(double t) => lastProcessTime = t;

        // ------------------------------------------------------------------ waypoints: move
        void ProcessMoveWP(double dt, bool isNew, bool stoppable)
        {
            var wp = Waypoints[0];
            if (isNew)
            {
                SetTarget(wp.x, wp.y);
                if (stoppable) StartPath(); else SetVelocity();
                foreach (var f in World.Flags)                                             // run for flags (:2729-2747)
                    if (Dist(f.LocX, f.LocY, wp.x, wp.y) <= 32) { CaptureRun(wp.x, wp.y); break; }   // DV54: a capture runs (ZObject.AiCombat.cs)   // parity P9: Zod DistanceFromCoords measures from the flag's loc (top-left), zobject.cpp:908-916
            }
            if (CheckAttackTo(wp)) return;
            if (stoppable && StunWaitForLeader()) return;                               // electrobot_v1 DV45: the squad waits for its stunned leader (ZObject.Electro.cs)
            if (stoppable && FormationStep(dt)) return;                                 // DV24: a minion walks in its squad's V (ZFormation.cs)
            if (!cur.gotPfResponse && stoppable) return;
            if (!ProcessMoveOrKillWP(World.Pathing != null ? dt * World.Pathing.RegroupPace(this) : dt, stoppable)) return;   // DV44: DV24's catch-up on a regroup (ZCommit.Follow.cs)
            if (!ReachedTarget()) return;
            if (!NextPathPoint() && !(World.Pathing != null && World.Pathing.SoftGoalHold(this))) KillWP();   // pathing v3 DV47: at debris short of its goal it waits, then over it (ZPathV3.cs)
        }

        void StartPath()
        {
            if (World.Pathing != null && World.Pathing.DeferPath(this)) return;                                 // DV44: minions follow their leader, requests budgeted per tick (ZCommit.cs)
            if (World.Pathing != null && !World.Pathing.PrepareStartPath(this)) return;                       // OpenZ pathing: unreachable goal -> nearest reachable
            cur.pathFindingId = World.Path.FindPath(LocX + 8, LocY + 8, cur.x, cur.y, IsRobot, HasExplosives(), out var pts);
            if (cur.pathFindingId != 0 && World.Pathing != null) pts = World.Pathing.CheckPath(this, pts);     // OpenZ pathing: no leg through a wall
            if (cur.pathFindingId != 0) { StopMove(); World.QueuePathResult(this, cur.pathFindingId, pts); }
            else { cur.gotPfResponse = true; SetVelocity(); }
        }

        bool NextPathPoint()
        {
            if (cur.pfPoints == null || cur.pfPoints.Count == 0) return false;
            SetTarget(cur.pfPoints[0].x, cur.pfPoints[0].y);
            SetVelocity();
            cur.pfPoints.RemoveAt(0);
            return true;
        }

        /// <summary>zobject.cpp:4779-4799 (called by the world one tick after the request, like the path thread)</summary>
        internal void PostPathFindingResult(int id, List<(int x, int y)> pts)
        {
            if (id != cur.pathFindingId || id == 0) return;
            cur.gotPfResponse = true;
            cur.pfPoints = pts ?? new List<(int, int)>();
            cur.pathFindingId = 0;
            NextPathPoint();
        }

        bool ProcessMoveOrKillWP(double dt, bool stoppable = true)
        {
            if (!ProcessMove(dt, stoppable, out int sx, out int sy))
            {
                if (DoAttackImpassableAtCoords(sx, sy)) return false;                   // blast the rock in the way
                if (World.Pathing != null && World.Pathing.OnBlocked(this, sx, sy)) return false;   // OpenZ pathing: re-path before giving up
                KillWP();
                return false;
            }
            return true;
        }

        /// <summary>zobject.cpp:3716-3747</summary>
        bool DoAttackImpassableAtCoords(int x, int y)
        {
            if (!HasExplosives()) return false;
            foreach (var o in World.Objects)
            {
                if (!o.IsDestroyableImpass || !o.CausesImpassAtCoord(x, y) || !CanAttackObject(o)) continue;
                Waypoints.Insert(0, new ZWaypoint { mode = WpMode.Attack, refId = o.Id, x = o.CenterX, y = o.CenterY });
                CloneMinionWayPoints();
                return true;
            }
            return false;
        }

        bool ProcessMove(double dt, bool stoppable, out int stopX, out int stopY)
        {
            stopX = stopY = 0;
            if (IsZ(Dx) && IsZ(Dy)) return true;
            double nx = LocX + (Dx * dt) + XOver, ny = LocY + (Dy * dt) + YOver;
            var pathing = World.Pathing != null && World.Pathing.Enabled ? World.Pathing : null;                // OpenZ pathing layer
            bool guardMinion = pathing != null && IsMinion && stoppable;
            if (pathing != null && !pathing.Gate(this, ref nx, ref ny)) { if (IsRobot) WalkDisengage(); return true; }   // held by traffic / a lane this tick (still walking: no firing at an old target)
            int inx = (int)Math.Floor(nx), iny = (int)Math.Floor(ny);
            if (IsMinion) stoppable = false;                                           // minions never stoppable
            if (guardMinion && ((pathing.MinionWouldClip(this, inx, iny, out stopX, out stopY) && !pathing.SoftStep(this, inx, iny)) || pathing.SoftHardStep(this, inx, iny, ref stopX, ref stopY))) return ReachedTarget();   // (pathing v3 DV47: on debris never into a hard tile)   // research 11.9
            if (stoppable && World.Path.WithinImpassable(inx + 1, iny + 1, WidthPix - 2, HeightPix - 2, out stopX, out stopY, IsRobot)
                && !(pathing != null && (pathing.EmbeddedEscape(this, inx, iny) || pathing.SoftStep(this, inx, iny))))   // pathing v3 DV47: over soft blockers (ZPathV3.cs)
                return ReachedTarget();
            if (IsRobot) WalkDisengage();                                              // robots don't attack while moving (DV57: grunt / psycho / laser keep firing, ZObject.FireOnMove.cs)
            XOver = nx - inx; YOver = ny - iny;
            LocX = inx; LocY = iny;
            CenterX = LocX + (WidthPix >> 1); CenterY = LocY + (HeightPix >> 1);
            NoteFacing();                                                              // combat_nb_v1 DV27: last heading (hits from behind)
            if (!IsZ(Dx) || !IsZ(Dy))
            {
                double prev = RealMoveSpeed;
                double tile = World.Map.TileWalkSpeed(CenterX, CenterY);
                if (pathing != null) tile = pathing.SafeTileSpeed(this, tile);                                  // never speed 0 (research 11.9)
                RealMoveSpeed = MoveSpeed * tile * DamagedSpeed() * RunSpeed();
                if (RealMoveSpeed != prev)
                {
                    if (prev > 0) { Dx = (float)(Dx / prev); Dy = (float)(Dy / prev); }
                    Dx = (float)(Dx * RealMoveSpeed); Dy = (float)(Dy * RealMoveSpeed);
                    UpdatedVelocity = true;
                }
            }
            return true;
        }

        public void SetTarget(int x, int y)
        {
            cur.x = x; cur.y = y; cur.sx = CenterX; cur.sy = CenterY;
            cur.adx = Math.Abs(cur.x - cur.sx); cur.ady = Math.Abs(cur.y - cur.sy);
        }

        bool ReachedTarget()
        {
            if (CenterX == cur.x && CenterY == cur.y) { XOver = YOver = 0; return true; }
            if (Math.Abs(CenterX - cur.sx) >= cur.adx && Math.Abs(CenterY - cur.sy) >= cur.ady)
            {
                SetCords(cur.x - (WidthPix >> 1), cur.y - (HeightPix >> 1));
                XOver = YOver = 0;
                return true;
            }
            return false;
        }

        public void SetVelocity()
        {
            float oldDx = Dx, oldDy = Dy;
            if (Waypoints.Count > 0)
            {
                Dx = cur.x - CenterX; Dy = cur.y - CenterY;
                if (!IsZ(Dx) || !IsZ(Dy))
                {
                    float mag = (float)Math.Sqrt(Dx * Dx + Dy * Dy);
                    Dx /= mag; Dy /= mag;
                    Dx = (float)(Dx * RealMoveSpeed); Dy = (float)(Dy * RealMoveSpeed);
                }
            }
            else StopMove();
            if (Math.Abs(Dx - oldDx) < 0.1) Dx = oldDx;
            if (Math.Abs(Dy - oldDy) < 0.1) Dy = oldDy;
            if (Dx != oldDx || Dy != oldDy) UpdatedVelocity = true;
        }

        public bool StopMove()
        {
            if (!IsMoving) return false;
            Dx = 0; Dy = 0; UpdatedVelocity = true;
            return true;
        }

        void KillWP()
        {
            UpdatedWaypoints = true;
            if (Waypoints.Count > 0) Waypoints.RemoveAt(0);
            StopMove();
            lastWp = ZWaypoint.Cleared;
        }

        /// <summary>zobject.cpp:3354-3384: a force move, or a crane / repair / fort entry past its approach stage, can't be overwritten.</summary>
        public bool CanOverwriteWP()
        {
            if (Waypoints.Count == 0) return true;
            switch (Waypoints[0].mode)
            {
                case WpMode.ForceMove: return false;
                case WpMode.CraneRepair: return cur.stage == Stage.GotoEntrance;
                case WpMode.UnitRepair: return cur.stage == Stage.GotoEntrance || cur.stage == Stage.Wait;
                case WpMode.EnterFort: return cur.stage == Stage.GotoEntrance;
            }
            return true;
        }

        // ------------------------------------------------------------------ waypoints: combat
        /// <summary>zobject.cpp:3285-3339 attack-move: a random enemy unit within agro range gets an ATTACK waypoint in front.</summary>
        bool CheckAttackTo(ZWaypoint wp)
        {
            if (!wp.attackTo || !CanOverwriteWP()) return false;
            List<ZObject> choices = null;
            foreach (var o in World.Objects)
            {
                if (!o.IsUnit || o.Owner == Team.Neutral || o.Owner == Owner || o.Destroyed) continue;
                if (WithinAgroRadius(o) && !PathUnreachable(o)) (choices ??= new List<ZObject>()).Add(o);
            }
            if (choices == null) return false;
            var c = choices[World.Rng.Next(choices.Count)];                           // quirk 11.11: random, not closest
            Waypoints.Insert(0, new ZWaypoint { mode = WpMode.Attack, refId = c.Id, x = c.CenterX, y = c.CenterY });
            return true;
        }

        /// <summary>zobject.cpp:2233-2328</summary>
        void ProcessAgroWP(double dt, bool isNew)
        {
            var wp = Waypoints[0];
            if (isNew) { cur.agroX = CenterX; cur.agroY = CenterY; cur.stage = Stage.Attack; }
            var t = World.Get(wp.refId);
            if (!CanAttackObject(t)) { KillWP(); return; }
            if (!Within(t.CenterX, t.CenterY, cur.agroX, cur.agroY, AttackRadius + World.S.agro_distance + wp.leash))   // DV53: a help call's leash reaches the attacker
            {
                if (cur.stage != Stage.Return) { DuelLeftRange(false); cur.stage = Stage.Return; SetTarget(cur.agroX, cur.agroY); SetVelocity(); }
            }
            else if (cur.stage != Stage.Attack) { cur.stage = Stage.Attack; SetTarget(t.CenterX, t.CenterY); SetVelocity(); }
            if (cur.stage == Stage.Attack)
            {
                if (!CanSee(t)) { KillWP(); return; }
                int dm = WithinAttackRadius(t) ? DuelStep(t, dt, true) : -1;          // DV9 (ZObject.Duel.cs): manoeuvre / approach engaged / Zod
                if (dm == ZDuel.Stand) { StopMove(); Engage(t); }
                else if (dm != ZDuel.Manoeuvre) { if (dm < 0) DuelLeftRange(false); SetTarget(t.CenterX, t.CenterY); SetVelocity(); if (!ProcessMoveOrKillWP(dt)) return; }
            }
            else
            {
                if (!ProcessMoveOrKillWP(dt)) return;
                if (!ReachedTarget()) return;
                KillWP();
            }
        }

        /// <summary>zobject.cpp:2330-2479</summary>
        void ProcessAttackWP(double dt, bool isNew)
        {
            var wp = Waypoints[0];
            var t = World.Get(wp.refId);
            if (!CanAttackObject(t)) { KillWP(); return; }
            if (WithinAttackRadius(t))
            {                                                                           // DV9 (ZObject.Duel.cs): a vehicle duel manoeuvres and fires; at the
                int dm = DuelStep(t, dt, false);                                        // very edge of range it keeps driving its approach, engaged
                if (dm == ZDuel.Manoeuvre) return;
                if (dm == ZDuel.Stand) { StopMove(); Engage(t); return; }               // Zod
            }
            else DuelLeftRange(true);                                                   // DV9: a manoeuvre that lost range re-plans the chase from here
            if (cur.pathFindingId != 0) return;
            if (!cur.gotPfResponse)
            {
                bool zodGoal = t.NearestAttackLoc(LocX, LocY, out int ex, out int ey, AttackRadius, IsRobot);
                if (!zodGoal) { ex = t.LocX + 8; ey = t.LocY + 8; }
                int gx = ex, gy = ey;
                if (!ApproachPlan(t, ref ex, ref ey) &&                                 // combat_nb_v1 DV28: the nearest reachable spot in range, not the target itself
                    !LosPlanAttack(t, zodGoal && ex == t.CenterX && ey == t.CenterY, ref ex, ref ey)) { KillWP(); return; }   // DV23: no spot with a clear shot anywhere
                SetTarget(ex, ey);
                bool losSpot = ex != gx || ey != gy;                                    // DV23: re-plan when the TARGET moves (not the shot spot)
                cur.initAttackX = losSpot ? t.CenterX : cur.x; cur.initAttackY = losSpot ? t.CenterY : cur.y;
                if (ApproachWalk()) return;                                             // combat_nb_v1 DV28: the breadth-first path round parked units
                StartPath();
                return;
            }
            if (Math.Abs(t.CenterX - cur.initAttackX) > AttackRadius || Math.Abs(t.CenterY - cur.initAttackY) > AttackRadius)
            { StopMove(); ClearCur(); return; }                                         // target moved away: re-plan
            SetVelocity();
            if (!ProcessMoveOrKillWP(dt)) return;
            if (!ReachedTarget()) return;
            if (!NextPathPoint()) { StopMove(); ClearCur(); }
        }

        void ClearCur() { var k = cur; cur = default; cur.agroX = k.agroX; cur.agroY = k.agroY; }

        /// <summary>zobject.cpp:1985-2040 (called on the target): where the attacker should walk to.</summary>
        public bool NearestAttackLoc(int sx, int sy, out int ex, out int ey, int aaRadius, bool isRobot)
        {
            var pf = World.Path;
            ex = CenterX; ey = CenterY;
            if (pf.ShouldBeAbleToMoveTo(sx, sy, ex, ey, isRobot)) return true;
            ex = LocX + 8; ey = LocY + 8;
            if (ex != CenterX && ey != CenterY && pf.ShouldBeAbleToMoveTo(sx, sy, ex, ey, isRobot)) return true;
            foreach (var (ox, oy) in new[] { (LocX + 8, LocY + 8 - 32), (LocX + 8 + 48, LocY + 8), (LocX + 8 - 48, LocY + 8) })
            {
                ex = ox; ey = oy;
                if (ex != CenterX && ey != CenterY && pf.ShouldBeAbleToMoveTo(sx, sy, ex, ey, isRobot)) return true;
            }
            foreach (var (lx, ly) in ZPathFinder.Bresenham(CenterX >> 4, CenterY >> 4, sx >> 4, sy >> 4, pf.W, pf.H))
            {
                ex = (lx << 4) + 8; ey = (ly << 4) + 8;
                if (!Within(CenterX, CenterY, ex, ey, aaRadius)) break;
                if (pf.ShouldBeAbleToMoveTo(sx, sy, ex, ey, isRobot)) return true;
            }
            return false;
        }

        /// <summary>zobject.cpp:2481-2557</summary>
        void ProcessPickupWP(double dt, bool isNew)
        {
            var wp = Waypoints[0];
            var t = World.Get(wp.refId);
            if (!CanPickupGrenades() || t == null || t.Type != "grenades") { KillWP(); return; }
            if (t.UnderCursor(CenterX, CenterY))
            {
                KillWP();
                if (!IsMinion) { Grenades += t.Grenades; t.Grenades = 0; DeleteGrenadeBoxId = t.Id; World.Emit(ZEventType.GrenadesPicked, this, t); }
                return;
            }
            if (isNew) { directRetries = 0; SetTarget(wp.x, wp.y); StartPath(); AttemptStartRun(wp.x, wp.y); }
            if (CheckAttackTo(wp)) return;
            if (!cur.gotPfResponse) return;
            if (!ProcessMoveOrKillWP(dt)) return;
            if (!ReachedTarget()) return;
            if (!NextPathPoint() && !RetryDirect(t)) KillWP();
        }

        /// <summary>DV16: a human's direct enter / pickup order whose path ended next to the target (a squad mate or a parked unit
        /// in the last tile) walks on to the object's centre instead of silently ending (Zod needs the unit's centre inside the box).</summary>
        int directRetries;
        bool RetryDirect(ZObject t)
        {
            if (t == null || !HumanDirectOrder || directRetries >= 8) return false;
            directRetries++;
            SetTarget(t.CenterX, t.CenterY); StartPath();
            return true;
        }

        /// <summary>zobject.cpp:2584-2667</summary>
        void ProcessEnterWP(double dt, bool isNew)
        {
            var wp = Waypoints[0];
            if (isNew) { directRetries = 0; SetTarget(wp.x, wp.y); StartPath(); CaptureRun(wp.x, wp.y); }   // DV54: running to the empty vehicle
            var t = World.Get(wp.refId);
            if (t == null || !(t.CanBeEntered() || t.CanTakePassengers(this))) { if (!EnterFallback(t, wp)) KillWP(); return; }   // apc_v1 DV15: or an own APC taking passengers; DV59: a player's order walks on to the spot (ZObject.BoardingRules.cs)
            if (!wp.playerGiven && t.AutoEnterLocked) { KillWP(); return; }                    // DV58: a cannon the player just ejected: no auto entry
            if (t.UnderCursor(CenterX, CenterY)) { bool mb = MinionBoards(t); KillWP(); if (!IsMinion || mb) EnteredTargetId = t.Id; return; }   // boarding_nb_v1 DV35: the first on it gets in
            if (FollowBoardTarget(t)) return;                                              // apc_v1 DV15: the APC drove on: aim at where it is now
            if (CheckAttackTo(wp)) return;
            if (!cur.gotPfResponse) return;
            if (!ProcessMoveOrKillWP(dt)) return;
            if (!ReachedTarget()) return;
            if (!NextPathPoint() && !RetryDirect(t)) KillWP();
        }

        /// <summary>zobject.cpp:2669-2694: straight line, no path-finding, tries to run.</summary>
        void ProcessDodgeWP(double dt, bool isNew)
        {
            var wp = Waypoints[0];
            if (isNew) { SetTarget(wp.x, wp.y); SetVelocity(); AttemptStartRun(); }
            if (!ProcessMoveOrKillWP(dt)) return;
            if (!ReachedTarget()) return;
            KillWP();
        }

        /// <summary>
        /// True while this unit runs a human player's direct order (enter, attack, grenade pickup, repair, fort entry). Deviation DV16.</summary>
        /// <summary>The minion that took over this dead leader's squad (RemoveObjectFromGroup), for the player's selection.</summary>
        public ZObject Successor;

        public bool HumanDirectOrder
        {
            get
            {
                if (!ZWorld.OrderPriorityDeviation || Waypoints.Count == 0 || !Waypoints[0].playerGiven || !World.IsHumanTeam(Owner)) return false;
                var m = Waypoints[0].mode;
                return m == WpMode.Enter || m == WpMode.Attack || m == WpMode.PickupGrenades || m == WpMode.UnitRepair || m == WpMode.CraneRepair || m == WpMode.EnterFort;
            }
        }

        /// <summary>A teammate's leader walks to enter `v` / pick up crate `v` on a direct player order: nobody else auto-grabs it (DV16).</summary>
        /// <summary>DV38: the player's last command for this unit was a player order (set by SendWaypoints on a human team): it holds
        /// where it was sent and never auto-grabs flags, vehicles or crates (it still engages enemies).</summary>
        public bool PlayerHold;

        /// <summary>DV38: another unit of this team (a leader) already has a waypoint on t (walking to capture / enter / pick it up).</summary>
        bool GrabClaimed(ZObject t)
        {
            foreach (var o in World.Objects)
            {
                if (o == this || o.Owner != Owner || o.IsMinion || o.Destroyed || o.Waypoints.Count == 0) continue;
                foreach (var w in o.Waypoints) if (w.refId == t.Id && (w.mode == WpMode.Move || w.mode == WpMode.Enter || w.mode == WpMode.PickupGrenades)) return true;
            }
            return false;
        }

        bool TeammateEntering(ZObject v)
        {
            foreach (var o in World.Objects)
                if (o != this && o.Owner == Owner && !o.IsMinion && o.IsRobot && !o.Destroyed && o.Waypoints.Count > 0 && (o.Waypoints[0].mode == WpMode.Enter || o.Waypoints[0].mode == WpMode.PickupGrenades) && o.Waypoints[0].refId == v.Id && o.HumanDirectOrder) return true;
            return false;
        }

        /// <summary>zobject.cpp:3657-3714 (called on the missile's target)</summary>
        public bool DodgeMissile(int tx, int ty, double timeTillExplode)
        {
            if (Stunned) return false;                                                 // electrobot_v1 DV45: stunned, before any rand() draw
            if (!CanOverwriteWP() || MoveSpeed <= 0 || RealMoveSpeed <= 0) return false;
            if (HumanDirectOrder) return false;                                        // DV16: a direct order is not interrupted by a sidestep
            if (fortRush) return false;                                                // boarding_nb_v1 DV34: through the fort door, no sidestep
            if (IsRobot && AttackObject != null) return false;
            if (Owner == Team.Neutral) return false;
            double dist = timeTillExplode > Stamina ? timeTillExplode * RealMoveSpeed : timeTillExplode * RealMoveSpeed * World.S.run_unit_speed;
            int mDist = (int)(dist * 2.0 / 4.0), fDist = (int)(dist * 4.0 / 4.0);
            if (mDist <= 0) mDist = 1;
            int moveDist = fDist + World.Rng.Next(mDist);
            int k = World.Rng.Next(1000); if (k == 0) k = 1000;                          // quirk 11.3 kept, minus the divide by zero
            double theta = (2 * 3.14159) * (1000.0 / k);                                // Zod's PI is 3.14159 (constants.h:46); vehicle_combat_v1 parity
            int nx = (int)(CenterX + (moveDist * Math.Cos(theta))), ny = (int)(CenterY + (moveDist * Math.Sin(theta)));   // int nx = center_x + (move_dist * cos(theta)): Zod truncates the SUM (zobject.cpp:3680-3681)
            if (Waypoints.Count > 0 && Waypoints[0].mode == WpMode.Dodge) { var w = Waypoints[0]; w.x = nx; w.y = ny; Waypoints[0] = w; }
            else Waypoints.Insert(0, new ZWaypoint { mode = WpMode.Dodge, refId = -1, x = nx, y = ny });
            return true;
        }

        // ------------------------------------------------------------------ engagement
        public bool WithinAttackRadius(ZObject o) =>
            o != null && InReach(o, CenterX, CenterY, AttackRadius) &&                // combat_nb_v1 DV26: a building's walls (else Zod's centre)
            (o.IsDestroyableImpass || !World.EngageBarrierBetween(CenterX, CenterY, o.CenterX, o.CenterY)) && HasLineOfSight(o);   // DV23: direct fire needs a line
        public bool WithinAgroRadius(ZObject o) =>
            o != null && Within(CenterX, CenterY, o.CenterX, o.CenterY, AttackRadius + World.S.agro_distance) &&
            (o.IsDestroyableImpass || !World.EngageBarrierBetween(CenterX, CenterY, o.CenterX, o.CenterY)) && CanSee(o);

        public void Engage(ZObject t)
        {
            if (t == null) { Disengage(); return; }
            if (AttackObject != t) { AttackObject = t; if (t.CanSnipe) SignalLidShouldOpen(); }
        }

        public bool Disengage()
        {
            if (AttackObject == null) return false;
            AttackObject = null; SignalLidShouldClose();
            return true;
        }

        void SignalLidShouldOpen() { if (!HasLid) return; if (World.Rng.Next(5) == 0) return; LidOpen = true; }   // quirk 11.10: 80 %; a pending close is NOT cancelled (zvehicle.cpp:198-207)
        void SignalLidShouldClose()
        {
            if (!HasLid) return;
            if (LidOpen && !doCloseLid) { doCloseLid = true; nextCloseLidTime = World.Time + 0.1 * World.Rng.Next(15); }
        }
        void ProcessServerLid(double time) { if (doCloseLid && time >= nextCloseLidTime) { doCloseLid = false; LidOpen = false; } }

        /// <summary>zobject.cpp:1603-1853, every 0.5 s</summary>
        void CheckPassiveEngage(double time, bool engageOnly = false)                      // electrobot_v1 DV45: engageOnly = stunned (no new waypoint)
        {
            if (time < nextPassiveCheck) return;
            nextPassiveCheck = time + 0.5;
            if (World.GameOver && ZWorld.EndFreezeDeviation) return;                           // endgame_ui_v1 (DV10): no enemy left; idle robots no longer walk off to vehicles, flags, crates
            if (!CanAttack() || Owner == Team.Neutral || !IsUnit) return;
            if (IsRobot && IsMoving && !LayerShuffle) { if (!FiresOnMove) return; engageOnly = true; }
            if (AttackObject != null) { if (!WithinAttackRadius(AttackObject)) Disengage(); else if (PrefersExposed) SwitchToExposed(); return; }   // combat_nb_v1 DV27
            List<ZObject> agro = null, enter = null, flags = null, crates = null;
            ZObject inRange = null;                                                     // combat_nb_v1 DV27: first enemy in range (an exposed driver goes first)
            bool idle = Waypoints.Count == 0 && Kind != ZKind.Cannon && !engageOnly && !HoldPosition;   // electrobot_v1 DV45: a stunned unit adds no agro / enter waypoint; DV68: hold position
            // On a human team a flag / vehicle / crate another unit
            // is already heading for is not auto-grabbed, and a unit the player ordered last stays where it was sent (it still fights)
            bool human = ZWorld.GrabPolicyDeviation && World.IsHumanTeam(Owner);
            bool grabOne = human, grabHold = human && PlayerHold;
            bool startHold = World.CalmStart(Owner);                                           // DV69 calm start: nothing self-started in the first 20 s (human teams)
            foreach (var o in World.Objects)
            {
                if (!o.IsUnit || o.Destroyed || o.KillAt >= 0) continue;
                if (o.Owner != Team.Neutral && o.Owner != Owner && CanAttackObject(o))
                {
                    if (WithinAttackRadius(o)) { if (!PrefersExposed || o.CanBeSniped()) { Engage(o); return; } inRange ??= o; }   // combat_nb_v1 DV27
                    if (idle && !startHold && WithinAgroRadius(o) && !PathUnreachable(o)) (agro ??= new List<ZObject>()).Add(o);   // DV69: no chase in the calm start
                }
                if (idle && !startHold && !IsMinion && agro == null && IsRobot && !NoAutoEnter && (!grabHold || CloseGrab(o)) && o.CanBeEntered() && Within(CenterX, CenterY, o.CenterX, o.CenterY, World.S.auto_grab_vehicle_distance) && !(grabOne && GrabClaimed(o))
                    && !(o.Kind == ZKind.Vehicle && o.Type == "apc") && !(JustLeftCannon && o.Kind == ZKind.Cannon) && !o.AutoEnterLocked && !PathUnreachable(o) && !TeammateEntering(o) && CanSee(o)
                    && !(grabOne && CloserGrabber(o, true)))                                   // DV55: held units take VERY close vehicles; the closest unit goes
                    (enter ??= new List<ZObject>()).Add(o);
            }
            if (inRange != null) { Engage(inRange); return; }                          // combat_nb_v1 DV27: no exposed driver in range: Zod's first pick
            if (engageOnly) return;                                                    // electrobot_v1 DV45: stunned: engage only
            if (!IsMinion && agro == null && Waypoints.Count == 0 && !HoldPosition)       // DV68: a unit holding position takes no flag / crate by itself
            {
                if (CanMove && !startHold) foreach (var f in World.Flags)
                        if (f.Owner != Owner && (!grabHold || CloseGrabFlag(f)) && Within(CenterX, CenterY, f.CenterX, f.CenterY, World.S.auto_grab_flag_distance) && !(grabOne && GrabClaimed(f)) && !PathUnreachable(f) && CanSee(f)
                            && !(grabOne && CloserGrabber(f, false))) (flags ??= new List<ZObject>()).Add(f);   // DV55: player-ordered units take a flag within ZWorld.CloseGrabFlagPx; the closest unit goes
                if (!grabHold && !startHold && CanPickupGrenades()) foreach (var g in World.Objects)
                        if (g.Type == "grenades" && !g.Destroyed && g.KillAt < 0 && Within(CenterX, CenterY, g.CenterX, g.CenterY, World.S.auto_grab_vehicle_distance) && !(grabOne && GrabClaimed(g)) && !PathUnreachable(g) && !TeammateEntering(g) && CanSee(g)) (crates ??= new List<ZObject>()).Add(g);
            }
            if (agro != null)
            {
                var c = Closest(agro);
                Waypoints.Add(new ZWaypoint { mode = WpMode.Agro, refId = c.Id, x = c.CenterX, y = c.CenterY });
                return;
            }
            bool e = enter != null, f2 = flags != null, g2 = crates != null;
            bool doE = false, doF = false, doG = false;
            if (e && f2 && g2) { int r = World.Rng.Next(3); doE = r == 0; doF = r == 1; doG = r == 2; }
            else if (e && f2) { if (World.Rng.Next(2) != 0) doE = true; else doF = true; }
            else if (e && g2) { if (World.Rng.Next(2) != 0) doE = true; else doG = true; }
            else if (f2 && g2) { if (World.Rng.Next(2) != 0) doF = true; else doG = true; }
            else if (e) doE = true; else if (f2) doF = true; else if (g2) doG = true;
            var list = doE ? enter : doF ? flags : doG ? crates : null;
            if (list == null || list.Count == 0) return;
            var ch = Closest(list);
            Waypoints.Add(new ZWaypoint { mode = doE ? WpMode.Enter : doG ? WpMode.PickupGrenades : WpMode.Move, refId = ch.Id, x = ch.CenterX, y = ch.CenterY, attackTo = true });
            CloneMinionWayPoints();
        }

        /// <summary>
        /// Zod only picks a crate up in PICKUP_GRENADES_WP (zobject.cpp:2481-2557, ordered or idle auto-grab);
        /// here a moving squad leader without grenades also takes a crate it walks across. Same pickup as the waypoint.</summary>
        void CheckWalkOverCrate()
        {
            if (!World.WalkOverGrenadePickup || !IsRobot || IsMinion || !IsMoving || !CanPickupGrenades() || DeleteGrenadeBoxId >= 0) return;
            if (Owner == Team.Neutral) return;
            foreach (var t in World.Objects)
            {
                if (t.Type != "grenades" || t.Destroyed || t.KillAt >= 0 || t.Grenades <= 0) continue;
                if (!t.UnderCursor(CenterX, CenterY)) continue;
                Grenades += t.Grenades; t.Grenades = 0; DeleteGrenadeBoxId = t.Id; World.Emit(ZEventType.GrenadesPicked, this, t);
                return;
            }
        }

        ZObject Closest(List<ZObject> l)
        {
            ZObject best = l[0]; double bd = DistanceFrom(best);
            for (int i = 1; i < l.Count; i++) { double d = DistanceFrom(l[i]); if (d < bd) { bd = d; best = l[i]; } }
            return best;
        }
        double DistanceFrom(ZObject o) => Dist(CenterX, CenterY, o.CenterX, o.CenterY);

        // ------------------------------------------------------------------ firing
        /// <summary>zobject.cpp:2102-2213</summary>
        void ProcessAttackDamage(double time, bool playerGiven)
        {
            HeavyPairStep(time);                                                                      // vehicles_nb_v1 DV30: the heavy's second barrel (ZObject.VehiclesNb.cs)
            var t = AttackObject;
            if (t == null || Damage == 0) return;
            if (KnockHold(time)) return;
            if (ToughPrepareHold(time)) return;                                                      // weapons_v2 DV11: Tough prepare (0.1 + 0.7 s, ZObject.ToughPrepare.cs)
            if (IsElectroWeapon) { ElectroFire(time, playerGiven); return; }                          // electrobot_v1 DV45: windup, bolt, stun, recharge (ZObject.Electro.cs)
            if (time < nextDamageTime) return;
            if (WalkShotOutOfRange(t)) { Disengage(); return; }                        // DV57: a walker's target left its range (no cooldown used)
            nextDamageTime = time + AttackSpeed;
            if (!CanAttackObject(t)) { Disengage(); return; }
            if (LosHoldShot(t)) return;                                                 // DV23: never a direct shot into a wall
            bool nades = Grenades > 0 || (Leader != null && Leader.Grenades > 0);
            if (nades && ZWorld.GrenadeRuleDeviation && Type != "grunt" && t.Kind == ZKind.Robot) nades = false;   // DV40: only grunts throw grenades at robots
            bool walking = WalkingShot;                                                 // DV57: fired while walking: the gun only, lower accuracy
            if (walking) nades = false;
            if (DamageIsMissile || nades)
            {
                if (!EstimateMissileTarget(t, MissileSpeed, out int tx, out int ty)) { tx = t.CenterX; ty = t.CenterY; }
                if (t.ReachToWalls) WallAim(t, out tx, out ty);                          // combat_nb_v1 DV26: the nearest wall section
                var m = new ZMissile { team = Owner, attackerId = Id, targetId = t.Id, playerGiven = playerGiven };
                if (nades)
                {
                    tx += World.Rng.Next(48) - 24; ty += World.Rng.Next(48) - 24;
                    m.damage = (int)(World.S.grenade_damage * ZConst.MAX_UNIT_HEALTH); m.radius = World.S.grenade_damage_radius;
                    m.x = tx; m.y = ty; m.CalcExplodeTime(CenterX, CenterY, World.S.grenade_missile_speed, time);
                    if (Grenades > 0) Grenades--; else Leader.Grenades--;
                    nextDamageTime = time + World.S.grenade_attack_speed;
                    m.grenade = true;
                }
                else
                {
                    tx += Scatter(World.Rng.Next(32) - 16); ty += Scatter(World.Rng.Next(32) - 16);   // veterancy_v1 DV12: veterans 3/4 of Zod's scatter
                    m.damage = HeavyPairFirst(time, t, playerGiven, tx, ty); m.radius = DamageRadius;   // vehicles_nb_v1 DV30: Zod = Damage; the heavy's first barrel = half
                    m.x = tx; m.y = ty; m.CalcExplodeTime(CenterX, CenterY, MissileSpeed, time);
                }
                World.AddMissile(m);
                World.Emit(ZEventType.MissileFired, this, t, tx, ty, (float)(m.explodeTime - time), m.radius, m.grenade);
                t.DodgeMissile(tx, ty, m.explodeTime - time);
            }
            else
            {
                bool miss = World.Rng.Roll() > HitChance() * (walking ? ZWorld.WalkHitFactor : 1.0);   // DV57: x0.6 while walking
                if (walking) NoteWalkShot(!miss);
                if (miss) { World.Emit(ZEventType.Hitscan, this, t, t.CenterX, t.CenterY, 0, 0, false); return; }   // miss (veterancy_v1 DV12: HitChance = Zod's damage_chance, veterans x1.10)
                bool snipe = CanSnipe && t.CanBeSniped() && World.Rng.Roll() <= SnipeChanceAt(t);   // combat_nb_v1 DV27: x2 from behind (Zod: SnipeChance)
                if (snipe) { t.DamageDriverHealth(World.DriverHitDamage(this, t)); UpdatedAttackObjectDriverHealth = true; }   // DC2: Zod = Damage
                else { t.DamageHealth(DirectHitDamage(t)); t.LastEnemyHit = time; UpdatedAttackObjectHealth = true; }   // DV78 rifle pair (ZObject.RiflePair.cs), else combat_nb_v1: DV29 chip (off: Zod's Damage), NB31
                if (IsRobot && Type == "pyro") t.LastFireHit = time;
                World.Emit(ZEventType.Hitscan, this, t, t.CenterX, t.CenterY, 0, 0, true, snipe);
            }
        }

        /// <summary>zobject.cpp:1855-1983 intercept of a moving target (straight missile at missile_speed from the centre).</summary>
        bool EstimateMissileTarget(ZObject target, int missileSpeed, out int tx, out int ty)
        {
            tx = ty = 0;
            if (missileSpeed <= 0) return false;
            if (IsZ(target.Dx) && IsZ(target.Dy)) return false;
            double dx = target.Dx, dy = target.Dy, xo = target.CenterX, yo = target.CenterY, x2o = CenterX, y2o = CenterY;
            double Cu = yo - y2o, Cd = xo - x2o, ms = missileSpeed;
            double a = Cu * Cu + Cd * Cd;
            double b = (2 * Cu * Cd * dy) - (2 * Cu * Cu * dx);
            double c = (Cd * Cd * dy * dy) - (2 * Cu * Cd * dx) + (Cu * Cu * dx * dx) - (Cd * Cd * ms * ms);
            double d = b * b - 4 * a * c;
            if (d <= 0.00001 || IsZ(a)) return false;
            double dx2 = (-b - Math.Sqrt(d)) / (2 * a);
            double g = ms * ms - dx2 * dx2;
            if (g <= 0.00001) { dx2 = (-b + Math.Sqrt(d)) / (2 * a); g = ms * ms - dx2 * dx2; if (g <= 0.00001) return false; }
            double dy2 = Math.Sqrt(g), t, dd;
            if (!IsZ(dd = dx - dx2)) t = -Cd / dd; else if (!IsZ(dd = dy - dy2)) t = -Cu / dd; else return false;
            if (t < 0)
            {
                dx2 = (-b + Math.Sqrt(d)) / (2 * a); g = ms * ms - dx2 * dx2; if (g <= 0.00001) return false;
                dy2 = Math.Sqrt(g);
                if (!IsZ(dd = dx - dx2)) t = -Cd / dd; else if (!IsZ(dd = dy - dy2)) t = -Cu / dd; else return false;
                if (t < 0) return false;
            }
            tx = (int)(dx * t + xo); ty = (int)(dy * t + yo);
            return true;
        }

        // ------------------------------------------------------------------ damage
        /// <summary>zobject.cpp:309-381</summary>
        public void SetHealth(int h)
        {
            bool was = Destroyed;
            Health = Math.Max(0, Math.Min(MaxHealth, h));
            if (was && !Destroyed) World.OnRevive(this);
            else if (!was && Destroyed) World.OnKilled(this);
        }
        public int DamageHealth(int amount)
        {
            if (Health <= 0) return Health;
            SetHealth(Health - amount);
            if (Kind == ZKind.Building) RecalcBuildTime();                                                 // every hit (:361-381)
            return Health;
        }
        public int DamageDriverHealth(int amount)
        {
            if (Drivers.Count == 0 || Drivers[0] <= 0) return 0;
            Drivers[0] -= amount;
            if (Drivers[0] <= 0) { SnipedFrom = Owner; Drivers.Clear(); ResetDamageInfo(); Owner = Team.Neutral; UnmannedStop(false, true); }   // DV79 unmanned stop: it stops in this very step; its target stays for this tick's server pass, which books what it hit itself and then drops it (ZObject.Unmanned.cs; Zod: at the server level, zobject.cpp:354-355)
            return 1;
        }

        // ------------------------------------------------------------------ speed factors, running
        public bool ShowDamaged => Kind == ZKind.Vehicle && MaxHealth > 0 && (double)Health / MaxHealth < 0.4;               // zvehicle.cpp:71-83
        public bool ShowPartiallyDamaged { get { if (Kind != ZKind.Vehicle || MaxHealth <= 0) return false; double p = (double)Health / MaxHealth; return p > 0.4 && p < 0.7; } }
        double DamagedSpeed() => ShowPartiallyDamaged ? World.S.partially_damaged_unit_speed : ShowDamaged ? World.S.damaged_unit_speed : 1.0;

        double RunSpeed()
        {
            if (fortRush) return World.S.run_unit_speed;                              // boarding_nb_v1 DV34: running speed through the fort door
            if (ZPathing.Yielding(this)) return World.S.run_unit_speed;               // vehicles first: stepping aside for a vehicle of its team at Zod's running speed, stamina untouched (ZYield.cs)
            if (Leader != null) return Leader.IsRunning && !ShowDamaged ? World.S.run_unit_speed : 1.0;
            return IsRunning && !ShowDamaged ? World.S.run_unit_speed : 1.0;
        }

        void ProcessRunStamina(double dt)
        {
            if (IsRunning) { Stamina -= dt; if (Stamina < 0) { Stamina = 0; IsRunning = false; } }
            else { Stamina += dt * World.S.run_recharge_rate; if (Stamina > MaxStamina) Stamina = MaxStamina; }
        }

        public void AttemptStartRun(int x, int y)
        {
            if (!Within(CenterX, CenterY, x, y, (int)(MoveSpeed * Stamina))) return;   // CanReachTargetRunning; parity P9: points_within_distance takes an int (common.cpp:156, zobject.cpp:2058-2065)
            AttemptStartRun();
        }
        public void AttemptStartRun()
        {
            if (IsRunning) return;
            if (World.Rng.Next(5) == 0) return;                                   // one in five we don't run
            if (Stamina < 0.3) return;
            IsRunning = true;
        }

        static double Dist(int ax, int ay, int bx, int by) => Math.Sqrt((double)(ax - bx) * (ax - bx) + (double)(ay - by) * (ay - by));
        internal static bool Within(int ax, int ay, int bx, int by, double d) => Dist(ax, ay, bx, by) <= d;   // points_within_distance

        // ------------------------------------------------------------------ squads
        public void CloneMinionWayPoints()
        {
            foreach (var m in Minions)
            {
                if (m == null) continue;
                m.Waypoints.Clear(); m.Waypoints.AddRange(Waypoints);
                m.SetVelocity();
                m.JustLeftCannon = JustLeftCannon;
            }
        }

        /// <summary>ZObject::RemoveObject (zobject.cpp:4051-4068): forget a deleted object.</summary>
        internal void ForgetObject(ZObject o)
        {
            if (AttackObject == o) Disengage();
        }

        public override string ToString() => $"{Type}#{Id} {Owner} @({CenterX},{CenterY})";
    }

    /// <summary>damage_missile (zdamagemissile.h): only a timer and a point; it never collides on the way.</summary>
    public sealed class ZMissile
    {
        public int x, y, damage, radius, attackerId = -1, targetId = -1;
        public Team team;
        public bool playerGiven, grenade, debris;
        public double explodeTime;
        public void CalcExplodeTime(int fx, int fy, int speed, double now) =>
            explodeTime = now + Math.Sqrt((double)(x - fx) * (x - fx) + (double)(y - fy) * (y - fy)) / Math.Max(1, speed);
    }

    public enum ZEventType
    {
        Hitscan, MissileFired, MissileExploded, Death, Sniped, DriverHit, Entered, Ejected, GrenadesPicked, Created, OwnerChanged, Revived,
        // rules: flag = zone captured (objId flag, value = new team, radius = old team); UnitBuilt (objId unit, otherId building);
        // CannonStored / CannonPlaced (building, cannon); RepairIn / RepairOut (vehicle, station); CraneAnim (crane, building, flag = on);
        // FortEntered (unit, fort); TeamEliminated (objId fort, value = team); GameOver (value = winner team, -1 = none)
        FlagCaptured, UnitBuilt, CannonStored, CannonPlaced, RepairIn, RepairOut, CraneAnim, FortEntered, TeamEliminated, GameOver, ProductionChanged,
        // electrobot_v1 DV45 (ZObject.Electro.cs): ElectroCharge (attacker, target; x, y target centre; value windup s; radius windup ticks),
        // ElectroBolt (attacker, target; value HP removed; radius recharge ticks; flag stunned; flag2 killed), ElectroFizzle (attacker, target
        // or -1), ElectroReady (attacker), StunStart / StunRefresh (unit, attacker; value stun s; radius stun ticks; StunStart flag = fresh),
        // StunPulse (unit; value whole seconds left), StunEnd (unit; flag = ended early)
        ElectroCharge, ElectroBolt, ElectroFizzle, ElectroReady, StunStart, StunRefresh, StunPulse, StunEnd,
        Promoted,                                                               // veterancy_v1 DV12: objId = the promoted crew (squad leader / vehicle / cannon), value = kills
        RunOverHit                                                              // DV65 (ZWorld.RunOver.cs): objId = the robot, otherId = the vehicle; value = HP removed; flag = glancing
    }

    /// <summary>Sim -> view events (sounds, FX, animation), consumed by the view once per frame.</summary>
    public struct ZEvent
    {
        public ZEventType type;
        public int objId, otherId, x, y, radius;
        public float value;
        public bool flag, flag2;
        public double end;
    }
}
