using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenZ.Sim
{
    /// <summary>
    /// The Zod server (zserver.cpp) for one map: objects in creation order, the ~100 Hz loop (ScuffleUnits, delete killed
    /// objects, ProcessServer, path results, side effects, ProcessMissiles), death processing with debris missiles,
    /// drivers/sniping, entering and ejecting, squads, and the command layer shared by the player and the bot.
    /// Pure C#: runs headless for the parity tests and inside ZGame for the 3D view.
    /// </summary>
    public sealed partial class ZWorld
    {
        public ZSettings S;
        public ZMapData Map;
        public readonly ZPathFinder Path = new ZPathFinder();
        public ZRng Rng;
        public double Time;
        public readonly List<ZObject> Objects = new List<ZObject>();
        public readonly List<ZObject> Flags = new List<ZObject>();
        public readonly List<ZEvent> Events = new List<ZEvent>();
        public readonly List<ZBot> Bots = new List<ZBot>();
        /// <summary>OpenZ difficulty (deviation, parity report): extra build-list levels per team (Neutral, Red, Blue). 0 = Zod
        /// (Hard, bots, parity); Medium gives the human +2, Easy +5 (every unit in every factory). Never changes build times.</summary>
        public readonly int[] TechBonus = new int[3];
        /// <summary>OpenZ pathing layer (Sim/ZPathing.cs: stuck guard, unit spacing, lanes, reachable goals). Pathing.Enabled =
        /// false restores pure Zod movement.</summary>
        public ZPathing Pathing { get; private set; }
        readonly Dictionary<int, ZObject> byId = new Dictionary<int, ZObject>();
        readonly List<ZMissile> missiles = new List<ZMissile>(), newMissiles = new List<ZMissile>();
        int nextRefId = 1;
        double nextScuffleTime;
        readonly List<(ZObject o, int id, List<(int x, int y)> pts)> pathResults = new List<(ZObject, int, List<(int, int)>)>();

        public IReadOnlyList<ZMissile> PendingMissiles => missiles;
        /// <summary>deaths_mech_v1 (read only, for the view): missiles queued this tick (death debris of a unit that died after
        /// ProcessMissiles), merged into PendingMissiles at the next ProcessMissiles.</summary>
        public IReadOnlyList<ZMissile> QueuedMissiles => newMissiles;

        public static ZWorld Create(string settingsText, string mapJson, int seed = 1, bool parkedStart = false)
        {
            var w = new ZWorld { S = ZSettings.Parse(settingsText), Map = ZMapData.FromJson(mapJson), Rng = new ZRng(seed), ParkedStart = parkedStart };
            w.DuelRng = new ZRng(unchecked(seed * 7919 + 104729));                   // DV9 duel manoeuvres: own stream, Zod's Rng untouched
            w.Pathing = new ZPathing(w);
            w.InitClearance();                                                      // los_clearance_v1: building mesh table (DV22 / DV23)
            w.InitPathfinding();
            w.Path.RebuildRegions();                                                // zmap.cpp:700-705: regions from the tile classes at map load, never rebuilt
            w.BuildZones();
            w.PlanFortDoorGuns();                                                   // DV73 (Hard): each fort's door-tower spot (ZWorld.FortDoorGun.cs)
            foreach (var o in w.Map.objects) w.CreateFromMap(o);
            w.AddFortDoorGuns();                                                    // DV73: a new missile cannon on a planned spot that was free
            w.AddHarbourWrecks();                                                   // l17_details_v1 DV39: L17 harbour wrecks (ZWorld.Wrecks.cs), right after Zod's objects
            w.AddDetailProps();
            w.AddTreeTiles();                                                       // DV88: level 16, the trees that close a tile are map items of the same kind (ZWorld.TreeTiles.cs), right after the details
            w.AddL16Pyramids();                                                     // level 16 DV89: the two pyramids as destroyable map items on closed tiles (ZWorld.L16Pyramids.cs)
            w.AddL16Shuttle();                                                      // level 16 DV95: the robots' shuttle in the place of one hut beside the red fort (ZWorld.L16Shuttle.cs)
            w.AddHeroSquads();                                                      // DV108: the level data's extra hero squads (ZHeroSquad.cs), after every object of the map; switch off = nothing
            w.ClearExitLanes();
            w.AddL17Shuttles();                                                     // K444 DV134: level 17's own shuttles: the EMPTY car-park one (every difficulty), the SHUTTLE box's one by the red fort (ZWorld.L17Shuttles.cs)
            w.MarkFortTurretCannons();
            w.InitRules();
            w.InitL17Crews();
            w.InitCrocodiles();                                                     // level 16: the crocodiles' water and places (ZWorld.Crocodiles.cs)
            w.Events.Clear();
            return w;
        }

        public ZObject Get(int id) => byId.TryGetValue(id, out var o) ? o : null;

        // Objects deleted in an earlier tick of the same frame are still needed by the view to play their death (the sim runs
        // ~2-3 ticks per rendered frame): keep the last 512 deleted objects findable for event handling only.
        readonly Dictionary<int, ZObject> graveyard = new Dictionary<int, ZObject>(); readonly Queue<int> graveOrder = new Queue<int>();
        public ZObject GetAny(int id) => byId.TryGetValue(id, out var o) ? o : graveyard.TryGetValue(id, out var d) ? d : null;

        public void Emit(ZEventType t, ZObject a, ZObject b = null, int x = 0, int y = 0, float value = 0, int radius = 0, bool flag = false, bool flag2 = false) =>
            Events.Add(new ZEvent { type = t, objId = a?.Id ?? -1, otherId = b?.Id ?? -1, x = x, y = y, value = value, radius = radius, flag = flag, flag2 = flag2, end = Time + value });

        /// <summary>zmap.cpp:2004-2034 InitPathfinding from the tile classes, then vehicle wide weights.</summary>
        void InitPathfinding()
        {
            Path.Reset(Map.width, Map.height);
            Path.WaterCost = CrocPathWaterCost();                                   // level 16: a swamp tile weighs double for robots (ZWorld.Crocodiles.cs); 1 everywhere else
            for (int j = 0; j < Map.height; j++)
                for (int i = 0; i < Map.width; i++)
                {
                    var c = Map.ClassAt(i, j);
                    Path.SetTileInfo(i, j, c == ZClass.Water ? PfTile.Water : c == ZClass.Road ? PfTile.Road :
                                           c == ZClass.Ground ? PfTile.Normal : PfTile.Impassable);
                }
            Path.SetTileWideWeights();
        }

        // ------------------------------------------------------------------ object creation (zserver.cpp:623-833)
        static Team ParseOwner(string o) => o == "red" ? Team.Red : o == "blue" ? Team.Blue : Team.Neutral;

        /// <summary>
        /// driver) on both sides, so they no longer race to the fort flags at 0 s (the west one sits against a rock wall: blocked
        /// vehicles blasted the rocks); robots collect them. Cannons keep their crews. Off = Zod (crewed, zvehicle.cpp:155-168).</summary>
        public bool ParkedStart;

        void CreateFromMap(ZMapData.Obj o)
        {
            var owner = ParseOwner(o.owner);
            if (ParkedStart && o.Category == "vehicle") owner = Team.Neutral;
            if (ShuttleFromMap(o, owner)) return;                                   // DV118 (K256f): a map's "vehicle:shuttle" entry is a playable shuttle, "null" = an empty one (Sim/ZWorld.ShuttleEmpty.cs)
            if (o.Category == "robot") CreateRobotGroup(o.Type, o.x * 16, o.y * 16, owner, -1, o.health_percent, o);
            else CreateObject(o.Category, FortDoorGunTypeAt(o), o.x * 16, o.y * 16, owner, o.extra_links, o.health_percent, o);   // DV73: the door tower's cannon
        }

        /// <summary>zserver.cpp:623-657: leader + (amount - 1) minions at exactly the same coordinates.</summary>
        public ZObject CreateRobotGroup(string type, int x, int y, Team owner, int amount = -1, int healthPercent = 100, ZMapData.Obj src = null)
        {
            if (amount < 0) amount = S.Unit(type)?.group_amount ?? 1;
            var leader = CreateObject("robot", type, x, y, owner, 0, healthPercent, src);
            for (int i = 1; i < amount; i++)
            {
                var m = CreateObject("robot", type, x, y, owner, 0, healthPercent, src);
                m.MemberIndex = i; m.Leader = leader; leader.Minions.Add(m);
            }
            return leader;
        }

        /// <summary>
        /// team on top of the map's start units, parked on free vehicle tiles around its fort (deterministic spiral from the
        /// fort's front). Returns the created vehicles.
        /// </summary>
        public List<ZObject> AddHeadStart(Team team, params string[] types) => AddHeadStart(team, false, types);

        /// <summary><paramref name="empty"/>: parked empty and neutral (DV32 on L17) instead of crewed.</summary>
        public List<ZObject> AddHeadStart(Team team, bool empty, params string[] types)
        {
            var made = new List<ZObject>();
            ZObject fort = null;
            foreach (var o in Objects) if (o.Kind == ZKind.Building && o.IsFort && o.Owner == team && (fort == null || o.Type == "fort_front")) fort = o;
            if (fort == null) return made;
            int fx = fort.CenterX / 16, fy = fort.CenterY / 16;
            int ftx0 = fort.LocX / 16 - 1, fty0 = fort.LocY / 16 - 1, ftx1 = (fort.LocX + fort.WidthPix) / 16 + 1, fty1 = (fort.LocY + fort.HeightPix) / 16 + 1;
            bool Free(int tx, int ty, string type)
            {
                if (tx < 1 || ty < 1 || tx + 2 >= Map.width || ty + 2 >= Map.height) return false;
                for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 2; dy++)
                    if (!Path.TilePassable(tx + dx, ty + dy, false) || Map.ClassAt(tx + dx, ty + dy) == ZClass.Water || Map.ClassAt(tx + dx, ty + dy) == ZClass.DeepWater) return false;
                if (tx + 2 > ftx0 && tx < ftx1 && ty + 2 > fty0 && ty < fty1) return false;              // keep the fort apron clear
                var probe = new ZObject { WidthPix = 48, HeightPix = 48 }; probe.SetCords(tx * 16 - 8, ty * 16 - 8);   // 1/2 tile gap to everything
                foreach (var o in Objects) if (!o.Destroyed && o.Kind != ZKind.MapItem && probe.IntersectsObject(o)) return false;
                foreach (var o in Objects) if (o.Kind == ZKind.MapItem && o.Type != "flag" && !o.Destroyed && probe.IntersectsObject(o)) return false;
                if (ExitLanesDeviation && !ParkingFree(tx, ty, null, type)) return false;      // DV66: out of every exit lane, 2 tiles of room all round
                return true;
            }
            // candidates around the fort, nearest first, on the side facing the map centre (a spot behind the fort at the map
            // edge hides the vehicles under the cliff skirt); deterministic order
            var cand = new List<(double score, int tx, int ty)>();
            double cx = Map.width * 0.5, cy = Map.height * 0.5;
            for (int dy = -17; dy <= 17; dy++)
                for (int dx = -17; dx <= 17; dx++)
                {
                    int r = System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)); if (r < 3) continue;
                    int tx = fx + dx, ty = fy + dy;
                    double toCentre = System.Math.Sqrt((tx - cx) * (tx - cx) + (ty - cy) * (ty - cy));
                    double score = r + 0.6 * toCentre;
                    if (ExitLanesDeviation && fort.GetCreationMovePoint(out int gx, out int gy))
                    {   // DV66: a tidy group beside the gate (nearest the exit point, outside its lane: Free), beside the roads, not on them
                        double ddx = tx * 16 + 16 - gx, ddy = ty * 16 + 16 - gy; score = Math.Sqrt(ddx * ddx + ddy * ddy) / 16.0;
                        for (int j = 0; j < 2; j++) for (int i = 0; i < 2; i++) if (Map.ClassAt(tx + i, ty + j) == ZClass.Road) score += 3;
                    }
                    cand.Add((score + (ty * Map.width + tx) * 1e-7, tx, ty));
                }
            cand.Sort((a, b) => a.score.CompareTo(b.score));
            int k = 0;
            foreach (var c in cand)
            {
                if (k >= types.Length) break;
                if (!Free(c.tx, c.ty, types[k])) continue;
                made.Add(CreateObject("vehicle", types[k++], c.tx * 16, c.ty * 16, empty ? Team.Neutral : team));
            }
            return made;
        }

        public ZObject CreateObject(string cat, string type, int x, int y, Team owner, int extraLinks = 0, int healthPercent = 100, ZMapData.Obj src = null)
        {
            var o = new ZObject { Id = nextRefId++, Owner = owner, Type = type, Source = src, World = this };
            int w = 1, h = 1;
            switch (cat)
            {
                case "robot": o.Kind = ZKind.Robot; break;
                case "vehicle": o.Kind = ZKind.Vehicle; w = h = 2; break;
                case "cannon": o.Kind = ZKind.Cannon; w = h = 2; break;
                case "building": o.Kind = ZKind.Building; (w, h) = BuildingSize(type, extraLinks); break;
                default: o.Kind = ZKind.MapItem; if (type == "rock") { w = 1; h = 3; } break;   // orock.cpp:48-49
            }
            if (o.Kind == ZKind.MapItem && type != "flag") o.Owner = Team.Neutral;           // items force NULL_TEAM
            o.WidthPix = w * 16; o.HeightPix = h * 16;
            o.U = S.Unit(type);
            if (o.U != null)
            {
                o.MoveSpeed = o.U.move_speed;
                o.MaxHealth = o.U.MaxHealth;
                o.MaxStamina = o.Stamina = o.U.max_run_time;
                o.Damage = o.U.Damage; o.AttackRadius = o.U.attack_radius; o.DamageChance = o.U.attack_damage_chance;
                o.DamageRadius = o.U.attack_damage_radius; o.MissileSpeed = RocketSpeed(type, o.U.attack_missile_speed); o.AttackSpeed = o.U.attack_speed;   // DV52: Tough / missile cannon rockets slower
                o.SnipeChance = o.U.attack_snipe_chance;
            }
            else o.MaxHealth = (int)(ItemOrBuildingHealth(type) * ZConst.MAX_UNIT_HEALTH);
            SetClassFlags(o);
            o.SetCords(x, y);
            o.ZoneIndex = GetZoneIndex(x, y);                                        // connected_zone from the top-left (zserver.cpp:802)
            if (o.Kind == ZKind.Building && src != null && src.blevel >= 0 && src.blevel < 6) o.Level = src.blevel;   // zserver.cpp:915-919
            o.InitRealMoveSpeed(Map);
            o.SetLastProcessTime(Time);
            Objects.Add(o); byId[o.Id] = o;
            SetMapImpassables(o);
            OnCreatedLosClearance(o);                                                // los_clearance_v1: DV22 mesh tiles, DV23 sight blockers
            // initial drivers (zvehicle.cpp:155-168, zcannon.cpp:57-70): an owned vehicle/cannon carries one full-health grunt
            if ((o.Kind == ZKind.Vehicle || o.Kind == ZKind.Cannon) && owner != Team.Neutral)
            { o.DriverType = "grunt"; o.Drivers.Add(S.Unit("grunt").MaxHealth); }
            o.ResetDamageInfo();
            if (type == "grenades") o.Grenades = S.grenades_per_box;
            // SetHealthPercent: a map object can start destroyed (L17 bridges) -> destroyed impassables + auto-repair timer
            o.Health = o.MaxHealth * Math.Max(0, Math.Min(100, healthPercent)) / 100;
            if (o.Destroyed) { OnKilled(o); o.HasProcessedDeath = true; }
            if (type == "flag") Flags.Add(o);
            return o;
        }

        /// <summary>Constructor flags per class (rgrunt.cpp ... cmissilecannon.cpp, zbuilding.cpp:17-19, orock.cpp:52 ...).</summary>
        static void SetClassFlags(ZObject o)
        {
            switch (o.Type)
            {
                case "grunt": case "psycho": case "sniper": case "pyro": case "laser": o.CanSnipe = true; break;
                case "tough": o.HasExplosivesFlag = true; o.DamageIsMissile = true; break;
                case "jeep": o.CanSnipe = true; o.CanBeSnipedFlag = true; break;
                case "light": case "medium": case "heavy": o.HasExplosivesFlag = true; o.DamageIsMissile = true; o.HasLid = true; o.CanBeSnipedFlag = true; break;
                case "missile_launcher": o.HasExplosivesFlag = true; o.DamageIsMissile = true; break;
                case "gatling": o.CanSnipe = true; o.CanBeSnipedFlag = true; break;
                case "gun": case "howitzer": case "missile_cannon": o.HasExplosivesFlag = true; o.DamageIsMissile = true; o.CanBeSnipedFlag = true; break;
            }
            if (o.Kind == ZKind.Building) { o.AttackedByExplosives = true; o.CanBeDestroyed = false; }
            if (o.Kind == ZKind.MapItem)
            {
                if (o.Type == "flag") o.CanBeDestroyed = false;
                else o.AttackedByExplosives = true;
                if (o.Type == "rock" || o.Type == "hut" || o.Type.StartsWith("map_object")) o.IsDestroyableImpass = true;
            }
        }

        /// <summary>Cannons on a fort's 4 turret spots are not ejectable, hence never snipable (bfort.cpp:534-548).</summary>
        void MarkFortTurretCannons()
        {
            foreach (var f in Objects.Where(o => o.Type == "fort_front" || o.Type == "fort_back").ToList())
                foreach (var c in Objects.Where(o => o.Kind == ZKind.Cannon))
                    foreach (var (ox, oy) in new[] { (16, 0), (16, 48), (112, 0), (112, 48) })
                        if (c.LocX == f.LocX + ox && c.LocY == f.LocY + oy) c.Ejectable = false;
        }

        static (int, int) BuildingSize(string type, int extraLinks)
        {
            int links = extraLinks < 100 ? extraLinks : 0;          // L17 carries junk extra_links on some objects
            switch (type)
            {
                case "fort_front": return (10, 12);                  // bfort.cpp:144-145
                case "fort_back": return (10, 11);                   // bfort.cpp:163-164
                case "robot_factory": case "vehicle_factory": return (4, 5);
                case "repair": return (5, 4);
                case "radar": return (4, 3);
                case "bridge_vert": return (4, 5 + links);           // bbridge.cpp:431-438
                case "bridge_horz": return (5 + links, 4);
            }
            return (1, 1);
        }

        double ItemOrBuildingHealth(string type)
        {
            switch (type)
            {
                case "fort_front": case "fort_back": return S.fort_building_health;
                case "robot_factory": return S.robot_building_health;
                case "vehicle_factory": return S.vehicle_building_health;
                case "repair": return S.repair_building_health;
                case "radar": return S.radar_building_health;
                case "bridge_vert": case "bridge_horz": return S.bridge_building_health;
                case "rock": return S.rock_item_health;
                case "grenades": return S.grenades_item_health;
                case "rockets": return S.rockets_item_health;
                case "hut": return S.hut_item_health;
                case "flag": return 0;                                // quirk 11.6 not replicated: flags are immune
            }
            return type.StartsWith("map_object") ? S.map_item_health : 0;
        }

        /// <summary>Per-type SetMapImpassables: bfort.cpp:550-620, brobot/bvehicle/brepair/bradar, bbridge.cpp:168-190,
        /// orock.cpp:678-686 (bottom tile of the 1x3 sprite), ohut.cpp:247, omapobject.cpp:77 (destroyable barriers).</summary>
        void SetMapImpassables(ZObject o, bool set = true)
        {
            int tx = o.LocX / 16, ty = o.LocY / 16, w = o.WidthPix / 16, h = o.HeightPix / 16;
            switch (o.Type)
            {
                case "fort_front": case "fort_back":
                {
                    for (int i = tx; i < tx + 10; i++) for (int j = ty; j < ty + 9; j++) Path.SetImpassable(i, j);
                    foreach (var (a, b) in new[] { (0, 0), (0, 1), (0, 2), (0, 7), (0, 8), (0, 9), (9, 0), (9, 1), (9, 2), (9, 7), (9, 8), (9, 9),
                                                   (3, 0), (4, 0), (5, 0), (6, 0), (1, 8), (3, 8), (4, 8), (5, 8), (6, 8), (8, 8) })
                        Path.SetImpassable(tx + a, ty + b, false);
                    if (o.Type == "fort_front")
                    {
                        Path.SetImpassable(tx + 3, ty + 8); Path.SetImpassable(tx + 3, ty + 9); Path.SetImpassable(tx + 6, ty + 8); Path.SetImpassable(tx + 6, ty + 9);
                        Path.SetImpassable(tx + 4, ty + 7, false); Path.SetImpassable(tx + 5, ty + 7, false);
                        Path.SetImpassable(tx + 4, ty + 6, false); Path.SetImpassable(tx + 5, ty + 6, false);
                    }
                    else
                    {
                        Path.SetImpassable(tx + 4, ty + 1, false); Path.SetImpassable(tx + 5, ty + 1, false);
                        Path.SetImpassable(tx + 4, ty + 2, false); Path.SetImpassable(tx + 5, ty + 2, false);
                    }
                    break;
                }
                case "robot_factory": case "vehicle_factory": case "repair":
                    for (int i = tx; i < tx + w; i++) for (int j = ty; j < ty + h; j++) Path.SetImpassable(i, j);
                    break;
                case "radar":
                    for (int i = tx; i < tx + w; i++) for (int j = ty; j < ty + h; j++) Path.SetImpassable(i, j);
                    Path.SetImpassable(tx + 3, ty + 2, false);
                    break;
                case "bridge_vert": for (int j = ty; j < ty + h; j++) { Path.SetImpassable(tx, j); Path.SetImpassable(tx + 3, j); } break;
                case "bridge_horz": for (int i = tx; i < tx + w; i++) { Path.SetImpassable(i, ty); Path.SetImpassable(i, ty + 3); } break;
                case WreckType: WreckImpassables(o, set); break;                   // l17_details_v1 DV39: the whole footprint, destroyable like a rock
                case DetailPropType: DetailPropImpassables(o, set); break;
                case "rock": Path.SetImpassable(tx, ty + 2, set, true); break;
                case "hut": Path.SetImpassable(tx, ty, set, true); break;
                default:
                    if (o.Type.StartsWith("map_object")) Path.SetImpassable(tx, ty, set, true);
                    break;
            }
        }

        /// <summary>bbridge.cpp:196-232: a destroyed bridge also blocks its centre.</summary>
        void SetBridgeCentre(ZObject o, bool impassable)
        {
            int tx = o.LocX / 16, ty = o.LocY / 16, w = o.WidthPix / 16, h = o.HeightPix / 16;
            if (o.Type == "bridge_vert") for (int j = ty; j < ty + h; j++) { Path.SetImpassable(tx + 1, j, impassable); Path.SetImpassable(tx + 2, j, impassable); }
            if (o.Type == "bridge_horz") for (int i = tx; i < tx + w; i++) { Path.SetImpassable(i, ty + 1, impassable); Path.SetImpassable(i, ty + 2, impassable); }
        }

        // ------------------------------------------------------------------ server loop (zserver.cpp:924-1041, 1132-1330)
        public void Tick(double dt)
        {
            ZRuleProf.TickBegin(); TickIndex++;                                                            // electrobot_v1 DV45: the stun / tesla clock (ZObject.Electro.cs)
            Time += dt;
            L17CrewsTick();
            ScuffleUnits();
            for (int i = 0; i < Objects.Count;)                                     // delete objects marked kill-me
            {
                var o = Objects[i];
                if (o.KillAt >= 0 && Time >= o.KillAt) DeleteObject(o); else i++;
            }
            int rp = ZRuleProf.Enter(ZRuleProf.Path); Pathing?.BeginTick(); ZRuleProf.Exit(rp);                                                   // OpenZ pathing: spatial hash, lanes
            int n = Objects.Count;
            for (int i = 0; i < n && i < Objects.Count; i++) Objects[i].ProcessServer(Time);
            if (pathResults.Count > 0)                                              // the path thread answers after the objects ran
            {
                var batch = pathResults.ToArray(); pathResults.Clear();
                foreach (var (o, id, pts) in batch) o.PostPathFindingResult(id, pts);
            }
            n = Objects.Count;
            for (int i = 0; i < n && i < Objects.Count; i++)                        // side effects
            {
                var o = Objects[i];
                if (o.EnteredTargetId >= 0) RobotEnterObject(o, Get(o.EnteredTargetId));
                if (o.UpdatedWaypoints) o.CloneMinionWayPoints();
                if (o.UpdatedAttackObjectHealth && o.AttackObject != null) { UpdateObjectHealth(o.AttackObject, o.Id); HelpCall(o.AttackObject, o); }   // DV53: squad help
                if (o.UpdatedAttackObjectDriverHealth && o.AttackObject != null) UpdateObjectDriverHealth(o.AttackObject, o);
                if (o.DeleteGrenadeBoxId >= 0) { var box = Get(o.DeleteGrenadeBoxId); if (box != null) { box.SetHealth(0); UpdateObjectHealth(box); } }
                RulesSideEffects(o);
            }
            rp = ZRuleProf.Enter(ZRuleProf.Move); Pathing?.EndTick(dt); ZRuleProf.Exit(rp);                                                   // OpenZ pathing: spacing push, stuck guard, deadlocks
            RunOverTick();                                                          // DV65: moving enemy vehicles knock down / squash robots (ZWorld.RunOver.cs)
            GroundFireTick();                                                       // DV117: burning ground bites an enemy who stays in it (ZWorld.GroundFire.cs)
            CrocodileTick(dt);                                                      // level 16: the crocodiles' own clock (ZWorld.Crocodiles.cs)
            HeroRegenTick(dt);                                                      // DV112: the heroes' self-repair (Sim/ZHeroSquad.Kh4.cs)
            ProneTargetTick(dt);                                                    // DV122: every sniper's stance, a rule (Sim/ZObject.ProneTarget.cs)
            CheckFlagCaptures();
            rp = ZRuleProf.Enter(ZRuleProf.Combat); ProcessMissiles(); ZRuleProf.Exit(rp);   // b109: rule tick timer per part, log only (Sim/ZRuleProf.cs)
            HeavyFallTick();                                                        // DV124: a fort cannon shot to pieces gets its toss; the shuttle / cannon that land now fell their trees (Sim/ZWorld.HeavyFall.cs)
            CheckEndGame();
            SquadSplitTick();                                                       // DV98: a member left far behind becomes a squad of his own (ZWorld.SquadSplit.cs)
            if (!GameOver || !EndFreezeDeviation)                                   // endgame_ui_v1 (DV10): the CPU stops at the game over
            {
                rp = ZRuleProf.Enter(ZRuleProf.Bot); IdleSweepTick();                                                    // DV70: forgotten CPU squads by its factories (every 7 min)
                foreach (var bot in Bots) bot.Tick();                               // bot clients act on the state after the server loop
                ZRuleProf.Exit(rp);
            }
            ZRuleProf.TickEnd();   // b109: rule tick timer per part, log only (Sim/ZRuleProf.cs)
        }

        internal void QueuePathResult(ZObject o, int id, List<(int x, int y)> pts) => pathResults.Add((o, id, pts));
        internal void AddMissile(ZMissile m) => newMissiles.Add(m);
        public void AddMissileForTest(ZMissile m) => newMissiles.Add(m);

        /// <summary>zserver.cpp:974-1041, every 1 s: idle same-team units whose centres are within 6 px get a random nudge.</summary>
        void ScuffleUnits()
        {
            if (Time < nextScuffleTime) return;
            nextScuffleTime = Time + ZConst.SCUFFLE_INTERVAL;
            foreach (var i in Objects)
            {
                if (!i.IsMobile || i.Owner == Team.Neutral || i.Waypoints.Count > 0 || i.Destroyed || i.IsFortDefender || i.Stunned) continue;   // fort_defender_v1 DV41: bolted to his ring; electrobot_v1 DV45: stunned
                foreach (var j in Objects)
                {
                    if (j == i || j.Owner != i.Owner || !j.IsMobile || j.Waypoints.Count > 0 || j.Destroyed || j.IsFortDefender || j.Stunned) continue;
                    if (Math.Abs(i.CenterX - j.CenterX) >= 6 || Math.Abs(i.CenterY - j.CenterY) >= 6) continue;
                    var wp = new ZWaypoint { mode = WpMode.Move, refId = -1 };
                    int shift = Rng.Next((j.WidthPix >> 1) + (i.WidthPix >> 1));
                    wp.x = Rng.Next(2) != 0 ? i.CenterX - shift : i.CenterX + shift;
                    shift = Rng.Next((j.HeightPix >> 1) + (i.HeightPix >> 1));
                    wp.y = Rng.Next(2) != 0 ? i.CenterY - shift : i.CenterY + shift;
                    i.Waypoints.Add(wp);
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ missiles (zserver.cpp:1043-1112)
        void ProcessMissiles()
        {
            for (int i = 0; i < missiles.Count;)
            {
                if (Time >= missiles[i].explodeTime) { var m = missiles[i]; missiles.RemoveAt(i); ProcessMissileDamage(m); }
                else i++;
            }
            missiles.AddRange(newMissiles); newMissiles.Clear();
        }

        void ProcessMissileDamage(ZMissile m)
        {
            CrocBlast(m.x, m.y);                                                    // level 16: a blast within 4 m sends a stalking crocodile under (ZWorld.Crocodiles.cs)
            HeavyFallTurret(m);                                                     // DV124: a blown-off turret lands here now and fells the trees it lies on (Sim/ZWorld.HeavyFall.cs)
            Events.Add(new ZEvent { type = ZEventType.MissileExploded, objId = m.attackerId, otherId = m.targetId, x = m.x, y = m.y, radius = m.radius, flag = m.grenade, flag2 = m.debris, value = m.damage, driver = m.driver });   // driver: DV87, a hatch driver's rocket
            int n = Objects.Count;
            for (int i = 0; i < n && i < Objects.Count; i++)
            {
                var o = Objects[i];
                if (o.Destroyed || (m.team != Team.Neutral && o.Owner == m.team)) continue;   // no friendly fire
                if (ShuttleAirBlast(m, o)) continue;                               // DV118: up in the air only a blast aimed at the shuttle reaches it, and nobody below (Sim/ZWorld.Shuttle.cs)
                int dx = m.x - o.CenterX, dy = m.y - o.CenterY;
                bool walls = o.Id == m.targetId && o.ReachToWalls;                  // combat_nb_v1 DV26: the aimed building takes it from its walls
                if (!walls && (Math.Abs(dx) > m.radius || Math.Abs(dy) > m.radius)) continue;
                float mag = walls ? (float)o.WallBlastDistance(m.x, m.y) : (float)Math.Sqrt((float)(dx * dx + dy * dy));
                if (mag >= m.radius) continue;
                if (o.MaxHealth <= 0) continue;                                     // flags: immune
                // double. The explicit cast rounds the ratio to float on every JIT / AOT (Mono kept it in double: 1000 HP r50 at 30 px gave
                // 400 instead of Zod's 399, 18 of 50 distances off by 1 HP, and the editor could disagree with a build).
                float ratio = (float)(mag / m.radius);
                int dmg = CoverBlast(o, BlastHit(m, o, ratio)), hp0 = o.Health;     // DV104: a thrown grenade on the target's body = its own damage, only in its area = Zod's less one tenth; every other blast and the rule off: Zod's line (ZWorld.GrenadeBoost.cs); DV122: a kneeling / lying sniper takes less of it (Sim/ZObject.ProneTarget.cs)
                if (o.IsFortDefender && o.FortDefenderInNest && mag > ZWorld.FortDefenderNestPx) dmg /= ZWorld.FortDefenderNestCover;
                o.DamageHealth(dmg);
                if (m.team != Team.Neutral && o.Health < hp0) { o.LastEnemyHit = Time; if (o.IsFort) { o.FortGunTakeHit(dmg); o.NoteFortAttack(m.attackerId, m.x, m.y); } }   // combat_nb_v1: NB31 hit time, DV25 the gun shares the fort's hits; fort_defender_v1 DV41: the attack's side
                o.LastMissileHit = Time;
                UpdateObjectHealth(o, m.attackerId);
                if (m.team != Team.Neutral && o.Health < hp0) HelpCall(o, Get(m.attackerId));   // DV53: squad help
                KnockOnBlast(o, m, ratio);
            }
        }

        public bool EngageBarrierBetween(int x1, int y1, int x2, int y2)          // zmap.cpp:2266-2295
        {
            foreach (var (x, y) in ZPathFinder.Bresenham(x1 >> 4, y1 >> 4, x2 >> 4, y2 >> 4, Map.width, Map.height)) if (Path.HasDestroyableBarrier(x, y)) return true;
            foreach (var (x, y) in ZPathFinder.Bresenham(x2 >> 4, y2 >> 4, x1 >> 4, y1 >> 4, Map.width, Map.height)) if (Path.HasDestroyableBarrier(x, y)) return true;
            return false;
        }

        // ------------------------------------------------------------------ health, death (zserver.cpp:1731-1907)
        /// <summary>SetHealth crossed to 0: destroyed impassables + ProcessKillObject (zobject.cpp:2215-2231).</summary>
        internal void OnKilled(ZObject o)
        {
            if (o.Type == "bridge_vert" || o.Type == "bridge_horz") SetBridgeCentre(o, true);
            if (o.IsBridge && Time > 0) Pathing?.BridgeGone(o);                     // boarding_nb_v1 DV36: vehicles bound over it re-plan now
            if (o.IsProducer) o.StopBuildingProduction();                          // production stops, queue cleared (zbuilding.cpp:275-287)
            if (o.Kind == ZKind.Building && o.Type != "fort_front" && o.Type != "fort_back")
            {
                o.DoAutoRepair = true;
                o.NextAutoRepairTime = Time + S.building_auto_repair_time;
                if (S.building_auto_repair_random_additional_time > 0) o.NextAutoRepairTime += Rng.Next(S.building_auto_repair_random_additional_time + 1);
                // impassable for exactly BridgeRepairSeconds of game time, then rebuilds itself (a crane can still repair it
                // sooner). The Zod random roll above is still drawn, so every later roll stays identical to Zod. Bridges that
                // start the level destroyed (L17) keep Zod's 600-660 s: the crane is part of that level's design.
                if (o.IsBridge && Time > 0 && BridgeRepairSeconds > 0) o.NextAutoRepairTime = Time + BridgeRepairSeconds;
            }
        }

        internal void OnRevive(ZObject o)
        {
            if (o.Type == "bridge_vert" || o.Type == "bridge_horz") SetBridgeCentre(o, false);
            if (o.IsBridge) Pathing?.BridgeBack(o);                                 // boarding_nb_v1 DV36: the deck may be the faster way now
            Emit(ZEventType.Revived, o);
        }

        /// <summary>zobject.cpp:4235-4257: instant full health, idle, same owner; cancelled for good in a zone with a destroyed fort.</summary>
        /// <summary>OpenZ: seconds a bridge destroyed in play stays down before it rebuilds itself (0 = Zod's 600-660 s).</summary>
        public double BridgeRepairSeconds = 120;

        /// <summary>OpenZ deviation DC2 (listed in the parity report): driver sniping happens in real fights. Zod's rules are
        /// kept: who can snipe and who can be sniped, the tank lid (zvehicle.cpp:178-240), the hit and snipe rolls
        /// (zobject.cpp:2187-2199) and the 1081 HP driver pool. Changed: the HP a driver hit removes, at least
        /// DriverHitFloor[target type] (a 3-grunt squad empties a jeep in ~15 s, an open-lid light / medium / heavy tank in
        /// ~30 / 45 / 60 s by the floor alone), times SniperDriverFactor for snipers (the fastest driver killers); and for the
        /// attacker / target pairs of OpenSeatPair (open seats: a jeep, a crewed gatling, a light tank with its lid open) the
        /// pair's own driver-hit HP and snipe chance (DriverHitDamage, SnipeChanceOn). OFF = Zod exact: the parity
        /// report, the bot checks, SnipeParityTests' Zod cases and the 'Zod exact' mode (PlayerPrefs oz_zod_exact).
        /// Static on purpose: one switch for every world; ZGame sets it at every start.</summary>
        public static bool SnipeDeviation = true;
        /// <summary>OpenZ deviation DV7: the fort's big gun is a real howitzer of the fort (ZObject.FortGun.cs). OFF = Zod exact
        /// (forts have no weapon). Static like SnipeDeviation; ZGame sets it at every start (PlayerPrefs oz_fortgun_sim).</summary>
        public static bool FortGunDeviation = true;
        public static readonly Dictionary<string, int> DriverHitFloor = new Dictionary<string, int> { ["jeep"] = 57, ["light"] = 29, ["medium"] = 19, ["heavy"] = 15 };
        public const int SniperDriverFactor = 3;

        /// <summary>HP one driver hit removes: Zod = the attacker's normal damage (zobject.cpp:2192); with the deviation at least
        /// the pair's own value when the attacker and target types are listed in OpenSeatPair, else at least the target's
        /// floor; x3 for snipers. Everything in neither table (gun, howitzer, missile cannon ...) keeps Zod's damage.</summary>
        public int DriverHitDamage(ZObject attacker, ZObject target)
        {
            int d = attacker.Damage;
            if (!SnipeDeviation) return d;
            if (OpenSeatPair.TryGetValue((attacker.Type, target.Type), out var pair) && pair.hit > 0) { if (d < pair.hit) d = pair.hit; }
            else if (DriverHitFloor.TryGetValue(target.Type, out int floor) && d < floor) d = floor;
            if (attacker.Type == "sniper") d *= SniperDriverFactor;
            d = attacker.ChargeDriverHit(target, d);                    // DV121: a Laser / hero's driver hit on a vehicle that drives at him from close (Sim/ZObject.JeepCharge.cs)
            return d;
        }

        /// <summary>DC2 open seats, per attacker type and target type (the driver sits in the open: a jeep, a crewed gatling, a
        /// light tank while its lid is open). hit = the HP one driver hit removes at least; it takes the place of the target's
        /// DriverHitFloor for that pair and may be lower than it (0 = keep the floor). chance = the snipe chance of that attacker
        /// on that target when it is higher than Zod's attack_snipe_chance (0 = Zod's: grunt / psycho 0.3, jeep / gatling 0.4,
        /// zsettings.cpp:28, :42, :113, :212). The driver pool is 1081 HP, so hit decides how many driver hits empty the seat.
        /// The values are set on counted fights of one attacker (a full squad for robots) against one healthy crewed target
        /// that fires back, 40 fights per pair (Editor menu OpenZ/Deviations/Count Driver Snipes). Pairs not listed keep the
        /// floor and Zod's chance. A sniper pair would also be multiplied by SniperDriverFactor.</summary>
        public static readonly Dictionary<(string attacker, string target), (int hit, double chance)> OpenSeatPair =
            new Dictionary<(string attacker, string target), (int hit, double chance)>
            {
                [("grunt", "gatling")] = (68, 0.0),    // 16 driver hits empty the seat
                [("grunt", "light")] = (145, 0.0),     // 8 driver hits empty the seat: about 6 s of undisturbed squad fire
                [("psycho", "gatling")] = (34, 0.0),   // 32 driver hits empty the seat
                [("psycho", "light")] = (55, 0.5),     // 20 driver hits empty the seat, snipe chance 0.5
                [("jeep", "gatling")] = (46, 0.0),     // 24 driver hits empty the seat
                [("jeep", "light")] = (155, 0.55),     // 7 driver hits empty the seat, snipe chance 0.55
                [("gatling", "light")] = (155, 0.62),  // 7 driver hits empty the seat, snipe chance 0.62
            };

        /// <summary>The attacker's snipe threshold against target: Zod = its attack_snipe_chance (zobject.cpp:2190); with the
        /// deviation the chance of the pair in OpenSeatPair when that is higher.</summary>
        public static double SnipeChanceOn(ZObject attacker, ZObject target)
        {
            double sc = attacker.SnipeChance;
            if (!SnipeDeviation || target == null) return sc;
            if (OpenSeatPair.TryGetValue((attacker.Type, target.Type), out var pair) && pair.chance > sc) sc = pair.chance;
            return sc;
        }

        internal void AutoRepair(ZObject o)
        {
            if (HasDestroyedFortInZone(o)) return;
            o.SetHealth(o.MaxHealth); o.HasProcessedDeath = false;
        }

        public void UpdateObjectHealth(ZObject o, int attackerId = -1)
        {
            if (o == null || !o.Destroyed || o.HasProcessedDeath) return;
            o.HasProcessedDeath = true;
            RelayObjectDeath(o, attackerId);
            ShuttleDown(o);                                                         // DV118: the shuttle shot down in the air crashes with its passengers; on the ground they get out (Sim/ZWorld.Shuttle.cs)
            CheckDestroyedFort(o);
            CheckDestroyedBridge(o);
            CheckNoUnitsDestroyFort(o.Owner);
        }

        void RelayObjectDeath(ZObject o, int attackerId)
        {
            Events.Add(new ZEvent { type = ZEventType.Death, objId = o.Id, otherId = attackerId, x = o.CenterX, y = o.CenterY,
                                    flag = Time - o.LastFireHit < 1.5, flag2 = Time - o.LastMissileHit < 1.5 });
            CreditKill(o, attackerId);                                              // veterancy_v1 DV12 (ZVeterancy.cs): no random draw, Zod's order kept
            foreach (var d in DeathDebris(o)) newMissiles.Add(d);
            if (o.CanBeDestroyed) o.KillAt = Time;                                  // LIFE_AFTER_DEATH_TIME = 0
        }

        /// <summary>ServerFireTurrentMissile per class (vlight.cpp:261-278, cgatling.cpp:279-296, omapobject.cpp:107-125, ogrenades.cpp:56-77).</summary>
        IEnumerable<ZMissile> DeathDebris(ZObject o)
        {
            int mh = S.max_turrent_horizontal_distance, mv = S.max_turrent_vertical_distance;
            ZMissile One(int hx, int vy, double delay, int dmg) => new ZMissile
            {
                x = (o.LocX + 16) + (hx - Rng.Next(hx + hx)), y = (o.LocY + 16) + (vy - Rng.Next(vy + vy)),
                damage = dmg, radius = 40, team = Team.Neutral, debris = true, attackerId = o.Id, explodeTime = Time + delay
            };
            if (o.Kind == ZKind.Vehicle && (o.Type == "light" || o.Type == "medium" || o.Type == "heavy"))
            { var m = One(mh, mv, 0, 40); m.explodeTime = Time + 3 + 0.01 * Rng.Next(100); yield return m; }
            else if (o.Kind == ZKind.Cannon)
            { var m = One(mh, mv, 0, 40); m.explodeTime = Time + 7 + 0.01 * Rng.Next(300); yield return m; }
            else if (o.Type.StartsWith("map_object"))
            { double t = 3 + 0.01 * Rng.Next(100); yield return One(mh, mv, t, (int)(S.map_item_turrent_damage * ZConst.MAX_UNIT_HEALTH)); }
            else if (o.Type == "grenades")
                for (int i = 0; i < o.Grenades; i++)
                {
                    var m = new ZMissile { x = (o.LocX + 16) + (130 - Rng.Next(260)), y = (o.LocY + 16) + (130 - Rng.Next(260)), damage = (int)(S.grenade_damage * ZConst.MAX_UNIT_HEALTH),
                                           radius = 40, team = Team.Neutral, debris = true, attackerId = o.Id };
                    m.explodeTime = Time + 3 + 0.01 * Rng.Next(100);
                    yield return m;
                }
        }

        /// <summary>zserver.cpp:1639-1673: every robot and vehicle on a destroyed bridge dies.</summary>
        void CheckDestroyedBridge(ZObject b)
        {
            if (b.Type != "bridge_vert" && b.Type != "bridge_horz") return;
            foreach (var o in Objects.ToList())
                if (!o.Destroyed && !o.ShuttleAirborne && (o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle) && o.IntersectsObject(b)) { o.SetHealth(0); UpdateObjectHealth(o); }
        }

        /// <summary>zserver.cpp:1675-1729: a destroyed fort eliminates its team (units die, buildings become rubble, its flags go neutral).</summary>
        void CheckDestroyedFort(ZObject f)
        {
            if (!f.IsFort) return;
            var team = f.Owner; if (team == Team.Neutral) return;
            EliminateTeam(f, team);
        }

        /// <summary>zserver.cpp:1925-1965: a team with no robot, vehicle or cannon left loses its forts.</summary>
        void CheckNoUnitsDestroyFort(Team team)
        {
            if (team == Team.Neutral) return;
            if (Objects.Any(o => o.Owner == team && o.IsUnit && !o.Destroyed && o.KillAt < 0 && !o.IsFortDefender)) return;   // fort_defender_v1 DV41: the roof defender is the fort's, not a unit
            foreach (var f in Objects.Where(o => o.Owner == team && (o.Type == "fort_front" || o.Type == "fort_back") && !o.Destroyed).ToList())
            { f.SetHealth(0); UpdateObjectHealth(f); }
        }

        /// <summary>zserver.cpp:1808-1874 after a driver hit.</summary>
        void UpdateObjectDriverHealth(ZObject v, ZObject by = null)
        {
            if (v.Drivers.Count > 0 && v.Drivers[0] > 0) { Emit(ZEventType.DriverHit, v, by, v.CenterX, v.CenterY, v.Drivers[0]); return; }   // value = driver HP left
            Emit(ZEventType.Sniped, v, by, v.CenterX, v.CenterY, (float)v.SnipedFrom);                 // otherId = sniper, value = team that lost the driver
            CreditSnipe(v, by);                                                                          // veterancy_v1 DV12
            v.Disengage();
            ResetObjectTeam(v, Team.Neutral);
            ApcWentNeutral(v);                                                                           // apc_v1 DV15: the passengers get out alive
            if (v.Waypoints.Count > 0) { v.Waypoints.Clear(); v.StopMove(); }
            DropQueuedAttacksOnEmptied(v);                                                               // DV81: human teams also drop an attack order on v that WAITS in a list (Zod, below: the first waypoint only), so it is not carried out on the empty vehicle later (ZWorld.EmptyTargets.cs)
            foreach (var o in Objects)
            {
                if (o.AttackObject == v) o.Disengage();
                if (o.Waypoints.Count > 0 && (o.Waypoints[0].mode == WpMode.Attack || o.Waypoints[0].mode == WpMode.Agro) && o.Waypoints[0].refId == v.Id)
                { o.Waypoints.RemoveAt(0); o.StopMove(); }
            }
        }

        void ResetObjectTeam(ZObject o, Team t)
        {
            o.Owner = t;
            o.ClearStun(true);                                                      // electrobot_v1 DV45: sniped driver, eject, capture end a stun (StunEnd, early)
            Emit(ZEventType.OwnerChanged, o, null, 0, 0, (float)t);
        }

        /// <summary>zserver.cpp:1375-1421</summary>
        void RobotEnterObject(ZObject r, ZObject v)
        {
            var heroIn = HeroBoarding(r, v);                                        // DV108: who of a hero squad gets in (Sim/ZHeroSquad.cs); null for every plain squad
            if (ShuttleTakeEmpty(r, v, heroIn)) return;                             // DV118 (K256f): a squad takes an EMPTY shuttle: its leader the pilot, the rest its passengers (Sim/ZWorld.ShuttleEmpty.cs)
            if (BoardAsPassengers(r, v)) { HeroSeatsIn(v, heroIn, true); return; }  // apc_v1 DV15: a squad boards an own APC as passengers (ZApcCrew.cs); DV108: their seats ride beside the list
            if (r == null || v == null || !v.CanBeEntered() || r.KillAt >= 0) return;
            v.DriverType = r.Type;
            v.Drivers.Add(r.Health);
            v.TakeCrewFrom(r);                                                      // veterancy_v1 DV12: the crew's rank travels with it
            r.KillAt = 0;
            if (v.Kind == ZKind.Vehicle && v.Type == "apc")
                foreach (var m in r.Minions) { v.Drivers.Add(m.Health); m.KillAt = 0; }
            SplitApcCrew(v);                                                        // apc_v1 DV15: a neutral APC: the leader drives, the minions ride
            HeroSeatsIn(v, heroIn, false);                                          // DV108: who of a hero squad sits where (Sim/ZHeroSquad.cs)
            v.ResetDamageInfo();
            Emit(ZEventType.Entered, r, v);
            ResetObjectTeam(v, r.Owner);
        }

        /// <summary>zserver.cpp:2065-2190: group hand-over, barriers released, everybody forgets the object.</summary>
        void DeleteObject(ZObject o)
        {
            RemoveObjectFromGroup(o);
            if (o.IsDestroyableImpass) { SetMapImpassables(o, false); }
            foreach (var x in Objects) if (x != o) x.ForgetObject(o);
            Objects.Remove(o); byId.Remove(o.Id);
            graveyard[o.Id] = o; graveOrder.Enqueue(o.Id);
            while (graveOrder.Count > 512) graveyard.Remove(graveOrder.Dequeue());
        }

        void RemoveObjectFromGroup(ZObject o)
        {
            if (o.Leader != null) { o.Leader.Minions.Remove(o); o.Leader = null; return; }
            if (o.Minions.Count == 0) return;
            var nl = o.Minions[0];
            nl.Leader = null; o.Successor = nl;                                          // the selection follows the squad (view reads it)
            foreach (var m in o.Minions) if (m != nl) { m.Leader = nl; nl.Minions.Add(m); }
            if (o.Grenades > 0) nl.Grenades = ZHeroGrenades.Merge(nl, o.Grenades);   // the pool passes to the new leader (KH: on top of a hero's own, Sim/ZHeroSquad.Kh.cs)
            o.Minions.Clear();
        }

        // ------------------------------------------------------------------ commands (zcore.cpp:196-306, 498-568; zserver_events.cpp)
        bool CheckWaypoint(ZObject o, ref ZWaypoint wp)
        {
            if (!o.CanAttack()) wp.attackTo = false;
            switch (wp.mode)
            {
                case WpMode.Move: return o.CanMove;
                case WpMode.ForceMove: if (!o.CanMove) return false; wp.mode = WpMode.Move; return true;
                case WpMode.Dodge: if (!o.CanMove) return false; wp.mode = WpMode.Attack; return o.CanAttackObject(Get(wp.refId));   // Zod falls through into AGRO
                case WpMode.Agro: wp.mode = WpMode.Attack; return o.CanAttackObject(Get(wp.refId));
                case WpMode.Attack: return o.CanAttackObject(Get(wp.refId)) || o.PyroBurnOrderOk(Get(wp.refId), wp);   // DV113: the player's Ctrl order sends a Pyro at a building or bridge (ZObject.PyroForceBurn.cs)
                case WpMode.Enter: { var t = Get(wp.refId); return o.CanMove && o.IsRobot && t != null && (t.CanBeEntered() || t.CanTakePassengers(o)); }   // apc_v1 DV15: + passengers
                case WpMode.PickupGrenades: { var t = Get(wp.refId); return o.CanPickupGrenades() && t != null && t.Type == "grenades"; }
                case WpMode.CraneRepair: { var t = Get(wp.refId); return o.CanMove && o.Kind == ZKind.Vehicle && o.Type == "crane" && t != null && t.CanBeRepairedByCrane(o.Owner); }
                case WpMode.UnitRepair: { var t = Get(wp.refId); return o.CanMove && o.CanBeRepaired() && t != null && t.CanRepairUnit(o.Owner); }
                case WpMode.EnterFort: { var t = Get(wp.refId); return o.CanMove && t != null && t.CanEnterFort(o.Owner); }
            }
            return false;
        }

        /// <summary>
        /// setup data (the bot list), so it is the same on every lockstep peer.</summary>
        /// <summary>DV16 switch (off = Zod: attack-move diversions, dodges, stuck give-ups and minion drop-outs apply to player orders too).</summary>
        public static bool OrderPriorityDeviation = true;
        /// <summary>
        /// ordered hold instead of auto-grabbing. PlayerPrefs oz_grab_policy.</summary>
        public static bool GrabPolicyDeviation = true;
        /// <summary>
        /// for the first CalmStartSec s of sim time (the sim starts when the player gets the camera, after the briefing) a human team's
        /// units start nothing by themselves: no idle auto-capture of flags, no auto-boarding of empty vehicles / cannons, no crate grabs,
        /// no walking off to chase enemies (idle agro, squad help calls); they still turn and shoot at enemies in range from where they
        /// stand. Explicit orders work at once; CPU teams are not held. The one shared gate every auto-behaviour asks: CalmStart(team).</summary>
        public static bool CalmStartDeviation = true;
        public static double CalmStartSec = 20.0;
        /// <summary>DV69: this team is still in its calm start (no self-started walking).</summary>
        public bool CalmStart(Team t) => CalmStartDeviation && Time < CalmStartSec && IsHumanTeam(t);
        /// <summary>
        /// vehicles, cannons, buildings and rocks (Zod: at anything). PlayerPrefs oz_grenade_rule.</summary>
        public static bool GrenadeRuleDeviation = true;

        public bool IsHumanTeam(Team t)
        {
            if (t == Team.Neutral) return false;
            foreach (var b in Bots) if (b.Team == t) return false;
            return true;
        }

        static bool CanSetWaypoints(ZObject o) => (o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle || o.Kind == ZKind.Cannon) && !o.IsFortDefender;   // fort_defender_v1 DV41: never controllable

        /// <summary>SEND_WAYPOINTS: replaces the unit's whole list (keeps a non-overwritable first one), validates each
        /// waypoint, clones it to the squad, empty list = stop. Used by the player input and the bot alike.</summary>
        public bool SendWaypoints(Team team, ZObject o, IList<ZWaypoint> list)
        {
            if (o == null || o.Owner == Team.Neutral || o.Owner != team) return false;
            if (!CanSetWaypoints(o) || o.IsMinion || o.Destroyed) return false;
            if (o.IsShuttle) return ShuttleOrder(team, o, list);                                   // DV118: a move order is the shuttle's flight target; a CPU team's order is refused (Sim/ZWorld.Shuttle.cs)
            if (RepairInPlaceDeviation) o.RepairBayOnOrder(IsHumanTeam(team));                    // DV62: a player's order calls a vehicle off the repair pad
            o.ClearOrderModes(); foreach (var m in o.Minions) m?.ClearOrderModes();                // DV68: any new order ends a patrol / hold
            o.ChargeOrdered();                                                                     // DV121: a new order ends a jeep's charge or its drive back at once (Sim/ZObject.JeepCharge.cs)
            if (!o.CanOverwriteWP()) { var first = o.Waypoints[0]; o.Waypoints.Clear(); o.Waypoints.Add(first); }
            else o.Waypoints.Clear();
            foreach (var w0 in list) { var w = w0; w.playerGiven = true; if (CheckWaypoint(o, ref w)) o.Waypoints.Add(w); }
            o.JustLeftCannon = false;
            if (GrabPolicyDeviation && IsHumanTeam(team)) o.PlayerHold = true;                   // DV38: sent by the player: holds, no auto-grab
            // a fresh order starts clean: pathing-layer actions queued for the OLD order (give-up, crowd arrival, retarget, back-off,
            o.PS.ClearPending(); foreach (var m in o.Minions) m?.PS.ClearPending();
            // ordered onto a (neutral) building kept shelling it while driving away. Passive engagement re-acquires real enemies in range.
            if (IsHumanTeam(team) && o.AttackObject != null && (o.Waypoints.Count == 0 || o.AttackObject.Id != o.Waypoints[0].refId) && ZWorld.OrderPriorityDeviation)
            { o.Disengage(); foreach (var m in o.Minions) m?.Disengage(); }
            Pathing?.OnOrder(o);                                                    // OpenZ pathing: group moves get their own spots
            o.CloneMinionWayPoints();
            if (o.Waypoints.Count == 0) { o.StopMove(); foreach (var m in o.Minions) m.StopMove(); }
            return true;
        }

        /// <summary>EJECT_VEHICLE (zserver_events.cpp:631-731): the crew pops out as a squad, the vehicle turns neutral.
        /// Each robot keeps its stored health (quirk 11.4 fixed to the intent).</summary>
        public bool Eject(Team team, ZObject v)
        {
            if (v != null && v.IsShuttle) return EjectPassengers(team, v);                       // DV118: the shuttle's passengers leave by the APC's rules, only on the ground (Sim/ZWorld.Shuttle.cs)
            if (ApcCrewDeviation && v != null && v.IsApc) return EjectPassengers(team, v);   // apc_v1 DV15: only the passengers leave, the driver stays
            if (v == null || v.Owner != team || team == Team.Neutral || !v.CanEjectDrivers() || v.Drivers.Count == 0) return false;
            var hp = v.Drivers.ToList();
            var leader = CreateRobotGroup(v.DriverType, v.LocX, v.LocY, team, hp.Count);
            var squad = new List<ZObject> { leader }; squad.AddRange(leader.Minions);
            CrewOut(v, squad);                                                      // veterancy_v1 DV12
            HeroCrewOut(v, squad, hp, false);                                       // DV108: hero members come out as themselves, Brad first, with their own hit points (Sim/ZHeroSquad.cs)
            for (int i = 0; i < squad.Count; i++)
            {
                squad[i].Health = Math.Min(squad[i].MaxHealth, hp[i]);
                if (v.Kind == ZKind.Cannon) squad[i].JustLeftCannon = true;
                Emit(ZEventType.Created, squad[i], v);
            }
            v.Drivers.Clear(); v.ResetDamageInfo(); v.Waypoints.Clear(); v.StopMove(); v.Disengage();
            Emit(ZEventType.Ejected, v, leader);
            ResetObjectTeam(v, Team.Neutral);
            LockAutoEnter(team, v);
            return true;
        }

        public IEnumerable<ZObject> OwnedSelectable(Team t) => Objects.Where(o => o.Owner == t && o.Selectable && !o.Destroyed && o.KillAt < 0);
    }
}
