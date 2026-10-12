using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// The Zod CPU player (zbot.cpp, zbot_events.cpp), 1:1, as a client of the Zod server (ZWorld).
    /// Loop: ProcessAI (zbot.cpp:100-125) once per game second = Stage1AI_3 (orders) then ChooseBuildOrders (production);
    /// "ignored" bots do nothing (:111-112). Orders: aggression tier from the flag share with neutral counted as an owner
    /// (GoAllOut_3 :635-691), order delay 4/5/8/12 s from last_order_time = 0 (:10-11, :140), idle units
    /// (CollectOurUnits_3 :578-633), targets (CollectOurTargets_3 :513-576: foreign flags, every grenade box, enemy forts,
    /// our repair stations, crane-repairable buildings, empty vehicles and cannons, enemy units only when all-out), a random
    /// 15-35 % (+1) wave (ReduceUnitsToPercent :176-201), spread over untargeted targets unless that leaves 25 % or less
    /// (RemoveTargetedFromTargets :203-220), same-region eligibility (MatchTargets_3 :416-511) and mutual-nearest matching by
    /// top-left distance (GiveOutOrders_3 :222-414, NearestObjectFromList zobject.cpp:3917-3938) with the full-list second
    /// pass (:161-169). Production: the first of Medium, Light, Jeep, Pyro, Tough, Sniper, Tough, Psycho that the building
    /// can make at its level, via STOP_BUILDING + START_BUILDING (:20-27, :1372-1432).
    ///
    /// It acts ONLY through the player's command layer (ZWorld.SendWaypoints / StopBuilding / StartBuilding, the same calls
    /// PlayerInput makes) and it decides from what a Zod bot CLIENT knows (<see cref="ClientView"/>, default on):
    ///  - its own map regions, built from the tile classes at map read before any object exists and never rebuilt
    ///    (zmap.cpp:700-705 -> zpath_finding.cpp:224-285). ZWorld.Path's regions also contain building/rock footprints,
    ///    which hides every fort_back, repair station and factory centre (region -2) from the bot.
    ///  - no rocks, huts or map objects: the bot deletes every map item except flags and grenade boxes on arrival
    ///    (zbot_events.cpp:96-118), so a unit blasting a rock looks idle.
    ///  - unit waypoints as last RELAYED to its team (zserver.cpp:1193-1198 KillWP, :1565/:1634 new units, :1841-1868 snipes,
    ///    zserver_events.cpp:407 SEND_WAYPOINTS; relays go to the owner's team only, zserver.cpp:2367-2385). Waypoints the
    ///    server inserts on its own (agro, attack-move diversions, dodges, rock blasting, scuffle nudges, idle auto-grabs,
    ///    rally points) are invisible until the next KillWP, so a unit fighting on its own looks idle and can be re-ordered,
    ///    exactly like Zod.
    /// ClientView = false restores the first OpenZ port (server lists, all objects, ZWorld.Path regions) for A/B checks.
    /// No other knowledge beyond Zod's bot: it sees the whole map (no fog), like zbot.
    /// </summary>
    /// <summary>CPU strength. Normal = the Zod bot exactly (zbot.cpp), the default and the only level used for bot vs bot,
    /// the parity checks and the balance runs. Easy = OpenZ-only handicap for a human vs the CPU (not in Zod): the CPU waits
    /// <see cref="ZBot.EASY_START_DELAY"/> s before its first order or production change, thinks half as often, re-plans
    /// 2.5x slower, orders at most <see cref="ZBot.EASY_MAX_WAVE"/> units per round and keeps at most
    /// <see cref="ZBot.EASY_MAX_ATTACKERS"/> units on hostile orders (attack, enter the enemy fort) at a time.
    /// PlayerPrefs "oz_difficulty": 0 = Normal, 1 = Easy (read by ZGame).</summary>
    public enum ZBotDifficulty { Normal = 0, Easy = 1 }

    public sealed partial class ZBot
    {
        // ------------------------------------------------------------------ Zod knobs (all hard-coded in zbot.cpp)
        /// <summary>zbot.cpp:109 next_ai_time = the_time + 1 (game time).</summary>
        public const double AI_INTERVAL = 1.0;
        /// <summary>zbot.cpp:216-219: "if we removed more than 75 % of targets then nevermind".</summary>
        public const double TARGETED_UNDO_SHARE = 0.25;
        /// <summary>The sim adds 0.01 s per tick in floating point; without this the 1 s AI tick drifts one tick later each
        /// second (Zod checks a real clock every 10 ms loop, zbot.cpp:39-57).</summary>
        const double TIME_EPS = 1e-6;
        /// <summary>zbot.cpp:20-27 preferred_build_list, first entry the building can make wins.</summary>
        public static readonly string[] Preferred = { "medium", "light", "jeep", "pyro", "tough", "sniper", "tough", "psycho" };

        public readonly Team Team;
        readonly ZWorld W;
        readonly ZRng rng;                                    // the bot runs its own client: its own rand()
        double nextAiTime = 0, lastOrderTime = 0;             // ZBot::ZBot (zbot.cpp:10-11)

        /// <summary>Zod "Manage Bots: Off" / bots_start_ignored (zbot.cpp:111-112): still receives the game state, gives no
        /// orders and leaves production alone.</summary>
        public bool Ignored;
        /// <summary>true = decide from the bot client's knowledge like Zod (see class notes); false = first-port behaviour.</summary>
        public bool ClientView = true;
        /// <summary>Also log every order (production changes are always logged).</summary>
        public bool Verbose;
        /// <summary>Normal (default) = Zod 1:1, every Easy branch below is skipped. Easy = OpenZ handicap, see ZBotDifficulty.</summary>
        public ZBotDifficulty Difficulty = ZBotDifficulty.Normal;

        // ---- Easy knobs (OpenZ deviation, listed in the parity report; never read on Normal)
        /// <summary>Game seconds before the Easy CPU gives its first order or changes production (Zod: 4.01 s / 0.01 s).</summary>
        public const double EASY_START_DELAY = 45.0;
        /// <summary>AI tick every 2 s instead of every 1 s (zbot.cpp:109).</summary>
        public const double EASY_AI_INTERVAL_SCALE = 2.0;
        /// <summary>Order delay of the aggression tier x 2.5: 4 / 5 / 8 / 12 s -> 10 / 12.5 / 20 / 30 s (zbot.cpp:140).</summary>
        public const double EASY_ORDER_DELAY_SCALE = 2.5;
        /// <summary>At most this many idle units (squad leaders or vehicles) get an order per round (Zod: 15-35 % + 1).</summary>
        public const int EASY_MAX_WAVE = 2;
        /// <summary>At most this many units on hostile orders (Attack, EnterFort) at once; extra ones stay idle this round.</summary>
        public const int EASY_MAX_ATTACKERS = 2;
        int easyAttackers;
        /// <summary>Hostile orders the Easy cap held back (debug / checks).</summary>
        public int EasyHeldBack { get; private set; }

        // ---- counters for the bot match / parity checks
        public int Orders { get; private set; }               // waypoint lists sent (SEND_WAYPOINTS)
        public int BuildChanges { get; private set; }         // STOP + START pairs sent
        public int Rounds { get; private set; }               // order rounds that passed the delay
        public int RejectedOrders { get; private set; }       // sent waypoint dropped by the server's CheckWaypoint
        public readonly Dictionary<WpMode, int> OrdersByMode = new Dictionary<WpMode, int>();
        public readonly List<string> Log = new List<string>();
        /// <summary>Orders of the latest round (for a debug overlay: who was sent where).</summary>
        public readonly List<BotOrder> LastRoundOrders = new List<BotOrder>();
        public struct BotOrder { public int unitId, targetId; public WpMode mode; public double time; }

        // ---- last tier (GoAllOut_3), for HUD/debug
        public bool LastAllOut { get; private set; }
        public double LastPercent { get; private set; } = 0.35;
        public double LastDelay { get; private set; } = 4;

        // ai_list / ai_orig_list (zobject.h:556-558), per bot (each Zod bot has its own client objects)
        readonly Dictionary<ZObject, List<ZObject>> aiList = new Dictionary<ZObject, List<ZObject>>();
        readonly Dictionary<ZObject, List<ZObject>> aiOrig = new Dictionary<ZObject, List<ZObject>>();

        public ZBot(ZWorld world, Team team, int seed)
        {
            W = world; Team = team; rng = new ZRng(seed * 7919 + (int)team);
            BuildClientRegions();
            // connect: the objects arrive without any waypoint list (waypoints only travel in relays)
            foreach (var o in W.Objects) if (o.IsMobile) known[o.Id] = new Known { live = o.Owner == Team };
        }

        // ------------------------------------------------------------------ demo switch
        /// <summary>Zod seeds per team as used by ZGame / OpenZBotMatch (red = seed, blue = seed + 101).</summary>
        public static int SeedFor(Team team, int seed) => team == Team.Blue ? seed + 101 : seed;

        /// <summary>
        /// Adds CPU players to the world: every team except <paramref name="human"/> (Z: the player is red, the CPU blue);
        /// with <paramref name="demo"/> both teams are bot-driven (bot vs bot). Blue is added first, like ZGame.
        /// </summary>
        public static List<ZBot> Attach(ZWorld w, Team human, bool demo, int seed)
        {
            var added = new List<ZBot>();
            foreach (var t in new[] { Team.Blue, Team.Red })
            {
                if (!demo && t == human) continue;
                bool exists = false;
                foreach (var b in w.Bots) if (b.Team == t) exists = true;
                if (exists) continue;
                var bot = new ZBot(w, t, SeedFor(t, seed));
                w.Bots.Add(bot); added.Add(bot);
            }
            return added;
        }

        /// <summary>Hand a team back to the human (bot stays attached, ignored) or to the CPU again.</summary>
        public static void SetDemo(ZWorld w, Team human, bool demo)
        {
            foreach (var b in w.Bots) if (b.Team == human) b.Ignored = !demo;
        }

        // ------------------------------------------------------------------ loop (called by ZWorld.Tick every server tick)
        public void Tick()
        {
            if (ClientView) ObserveRelays();                  // the event handler runs every 10 ms loop (zbot.cpp:39-57)
            if (W.Time + TIME_EPS < nextAiTime) return;
            bool easy = Difficulty == ZBotDifficulty.Easy;
            nextAiTime = W.Time + (easy ? AI_INTERVAL * EASY_AI_INTERVAL_SCALE : AI_INTERVAL);
            Purge();
            if (Ignored) return;
            if (easy && W.Time + TIME_EPS < EASY_START_DELAY) return;                   // Easy: the CPU sits still at the start
            Stage1AI_3();
            CommitIdleGroups();
            FinishGame();
            ChooseBuildOrders();
        }

        // ------------------------------------------------------------------ orders
        void Stage1AI_3()
        {
            bool allOut = GoAllOut_3(out double percent, out double delay);
            LastAllOut = allOut; LastPercent = percent; LastDelay = delay;
            bool easy = Difficulty == ZBotDifficulty.Easy;
            if (W.Time + TIME_EPS < lastOrderTime + (easy ? delay * EASY_ORDER_DELAY_SCALE : delay)) return;   // ordering too fast? (:140)
            Rounds++;
            if (easy) easyAttackers = CountHostileOrders();
            LastRoundOrders.Clear();
            var units = new List<ZObject>(); var targeted = new List<ZObject>(); var targets = new List<ZObject>();
            CollectOurUnits_3(units, targeted);
            CollectOurTargets_3(targets, allOut);
            CarParkRound(units, targeted, targets);                                        // boarding_nb_v1 DV37 (L17, Normal CPU): squads to empty vehicles first
            var escorts = ElectrobotHoldBack(units);                                       // DV72: the Electrobot leaves with a unit ordered out near him (ZBot.Electrobot.cs)
            var idleAll = escorts != null ? new List<ZObject>(units) : null;
            ReduceUnitsToPercent(units, percent);
            if (easy && units.Count > EASY_MAX_WAVE) units.RemoveRange(EASY_MAX_WAVE, units.Count - EASY_MAX_WAVE);   // random order already
            var targetsOrig = new List<ZObject>(targets);
            RemoveTargetedFromTargets(targets, targeted);
            MatchTargets_3(units, targets);
            GiveOutOrders_3(units, targets);
            if (units.Count > 0 && targets.Count != targetsOrig.Count)                      // :161-169
            {
                MatchTargets_3(units, targetsOrig);
                GiveOutOrders_3(units, targetsOrig);
            }
            if (escorts != null) ElectrobotEscort(escorts, idleAll, targetsOrig);          // DV72
            lastOrderTime = W.Time;
        }

        /// <summary>zbot.cpp:635-691. Returns all_out; neutral counts as a flag owner.</summary>
        public bool GoAllOut_3(out double percent, out double delay)
        {
            percent = 0.35; delay = 4;
            var owners = new HashSet<Team>(); int ours = 0;
            foreach (var f in W.Flags) { owners.Add(f.Owner); if (f.Owner == Team) ours++; }
            if (owners.Count == 0) return true;
            if (W.Flags.Count == 0) return true;
            double owned = 1.0 * ours / W.Flags.Count, fair = 1.0 / owners.Count;
            if (owned >= fair) { percent = 0.15; delay = 12; return true; }
            if (owned >= fair * 0.5) { percent = 0.15; delay = 8; }
            else if (owned >= fair * 0.25) { percent = 0.25; delay = 5; }
            return false;
        }

        /// <summary>zbot.cpp:578-633 over mobile_olist (robots + vehicles, zolists.cpp:70): minions never get orders; a unit
        /// whose first (known) waypoint points at an existing object is busy and that object "targeted", except a flag we own
        /// (idle) and a repair station (busy, not recorded); a first waypoint without a target is idle unless it is a dodge.</summary>
        public void CollectOurUnits_3(List<ZObject> units, List<ZObject> targeted)
        {
            foreach (var o in W.Objects)
            {
                if (!o.IsMobile || o.Owner != Team || Gone(o) || o.IsFortDefender) continue;   // fort_defender_v1 DV41: the fort's, not the CPU's
                if (o.Kind == ZKind.Robot && o.IsMinion) continue;
                var wps = KnownWaypoints(o);
                if (wps.Count > 0)
                {
                    var t = BotObject(wps[0].refId);
                    if (t != null)
                    {
                        if (t.Kind == ZKind.MapItem && t.Type == "flag")
                        {
                            if (t.Owner != Team) { targeted.Add(t); continue; }
                        }
                        else
                        {
                            if (!(t.Kind == ZKind.Building && t.Type == "repair")) targeted.Add(t);
                            continue;
                        }
                    }
                    if (wps[0].mode == WpMode.Dodge) continue;
                }
                units.Add(o);
            }
        }

        /// <summary>zbot.cpp:513-576 over the bot's object list (creation order).</summary>
        public void CollectOurTargets_3(List<ZObject> targets, bool allOut)
        {
            foreach (var t in W.Objects)
            {
                if (Gone(t) || !VisibleToBot(t)) continue;
                switch (t.Kind)
                {
                    case ZKind.MapItem:
                        if (t.Type == "flag" && t.Owner != Team) targets.Add(t);
                        if (t.Type == "grenades") targets.Add(t);
                        break;
                    case ZKind.Building:
                        if (t.IsFort) { if (t.Owner != Team && t.Owner != Team.Neutral && !t.Destroyed) targets.Add(t); }
                        else if (t.Type == "repair") { if (t.Owner == Team && !t.Destroyed) targets.Add(t); }
                        if (CraneJob(t)) targets.Add(t);
                        break;
                    case ZKind.Cannon: case ZKind.Vehicle:
                        if (t.CanBeEntered()) targets.Add(t);
                        else if (allOut && t.Owner != Team && t.Owner != Team.Neutral && !t.Destroyed) targets.Add(t);
                        break;
                    case ZKind.Robot:
                        if (allOut && t.Owner != Team && t.Owner != Team.Neutral && !t.Destroyed) targets.Add(t);
                        break;
                }
            }
        }

        /// <summary>zbot.cpp:176-201: keep (int)(percent x n) + 1 random idle units unless percent >= 0.95 or n &lt;= 1.</summary>
        public void ReduceUnitsToPercent(List<ZObject> units, double maxPercent)
        {
            if (maxPercent >= 0.95) return;
            if (units.Count <= 1) return;
            int max = (int)(maxPercent * units.Count);
            max++;
            if (max > units.Count) max = units.Count;
            var keep = new List<ZObject>();
            while (keep.Count < max && units.Count > 0)
            {
                int c = rng.Next(units.Count);
                keep.Add(units[c]);
                units.RemoveAt(c);
            }
            units.Clear(); units.AddRange(keep);
        }

        /// <summary>zbot.cpp:203-220</summary>
        public static void RemoveTargetedFromTargets(List<ZObject> targets, List<ZObject> targeted)
        {
            if (targets.Count == 0) return;
            var orig = new List<ZObject>(targets);
            foreach (var t in targeted) { var tt = t; targets.RemoveAll(x => x == tt); }
            if (1.0 * targets.Count / orig.Count <= TARGETED_UNDO_SHARE) { targets.Clear(); targets.AddRange(orig); }
        }

        /// <summary>The crane jobs the CPU picks (zbot.cpp: every building its crane may repair). DV74 lets a crane rebuild a ruin in
        /// any zone; the CPU takes bridges and the ruins of its own or a neutral zone only, as before: a building rebuilt in the other
        /// side's zone would belong to that side.</summary>
        bool CraneJob(ZObject t) => t.CanBeRepairedByCrane(Team) && (t.IsBridge || t.Owner == Team.Neutral || t.Owner == Team);

        /// <summary>Bot target point: building centre, else top-left + 8 (zbot.cpp:437-447).</summary>
        static void TargetPoint(ZObject t, out int x, out int y)
        {
            if (t.Kind == ZKind.Building) { x = t.CenterX; y = t.CenterY; }
            else { x = t.LocX + 8; y = t.LocY + 8; }
        }

        /// <summary>zbot.cpp:416-511: pair a unit and a target when both points are in the same region and the unit may take
        /// that target; the link is stored on both sides (AddToAIList, zobject.h:558).</summary>
        public void MatchTargets_3(List<ZObject> units, List<ZObject> targets)
        {
            foreach (var u in units) AI(u).Clear();
            foreach (var t in targets) AI(t).Clear();
            foreach (var u in units)
            {
                int ux = u.LocX + 8, uy = u.LocY + 8;
                bool robot = u.Kind == ZKind.Robot;
                foreach (var t in targets)
                {
                    TargetPoint(t, out int tx, out int ty);
                    if (!InSameRegion(ux, uy, tx, ty, robot)) continue;                 // :449-451
                    bool add = false;
                    switch (t.Kind)
                    {
                        case ZKind.MapItem:
                            if (t.Type == "flag" && t.Owner != Team) add = true;
                            if (t.Type == "grenades" && u.CanPickupGrenades()) add = true;
                            break;
                        case ZKind.Building:
                            if (t.IsFort) { if (t.Owner != Team && t.Owner != Team.Neutral && !t.Destroyed) add = true; }
                            else if (t.Type == "repair") { if (t.Owner == Team && !t.Destroyed && u.CanBeRepaired()) add = true; }
                            if (CraneJob(t) && u.Kind == ZKind.Vehicle && u.Type == "crane") add = true;
                            break;
                        case ZKind.Cannon: case ZKind.Vehicle:
                            if (t.CanBeEntered()) { if (robot && !u.NoAutoEnter) add = true; }   // electrobot_v1 D7: never sends its Electrobot in
                            else if (u.CanAttackObject(t)) add = true;
                            break;
                        case ZKind.Robot:
                            if (u.CanAttackObject(t)) add = true;
                            break;
                    }
                    if (add) { AI(u).Add(t); AI(t).Add(u); }
                }
            }
        }

        /// <summary>zbot.cpp:222-414: pass after pass, a unit gets its nearest candidate target only if it is that target's
        /// nearest candidate unit (distance between top-left corners); an emptied list falls back to the unit's original list.</summary>
        public void GiveOutOrders_3(List<ZObject> units, List<ZObject> targets)
        {
            foreach (var u in units) aiOrig[u] = new List<ZObject>(AI(u));
            while (units.Count > 0)
            {
                bool orderGiven = false;
                for (int i = 0; i < units.Count;)
                {
                    var u = units[i];
                    if (AI(u).Count == 0)
                    {
                        if (aiOrig.TryGetValue(u, out var orig) && orig.Count > 0) { AI(u).Clear(); AI(u).AddRange(orig); }
                        else { units.RemoveAt(i); continue; }
                    }
                    var t = Nearest(u, AI(u));
                    if (t == null) { units.RemoveAt(i); continue; }
                    var back = Nearest(t, AI(t));
                    if (back != null && back != u) { i++; continue; }             // not mutually nearest (yet)
                    Command(u, t);
                    foreach (var t2 in targets) AI(t2).RemoveAll(x => x == u);
                    orderGiven = true;
                    units.RemoveAt(i);
                    foreach (var u2 in units) AI(u2).RemoveAll(x => x == t);
                }
                if (!orderGiven) break;
            }
        }

        /// <summary>zbot.cpp:279-392: one waypoint replacing the unit's orders; attack aims at the top-left + 8, everything
        /// else at the centre; attack_to and player_given set; sent as SEND_WAYPOINTS (SendBotDevWaypointList :1434-1451).</summary>
        void Command(ZObject u, ZObject t, bool finisher = false)
        {
            WpMode mode = WpMode.None;
            switch (t.Kind)
            {
                case ZKind.MapItem:
                    if (t.Type == "flag" && t.Owner != Team) mode = WpMode.Move;
                    if (t.Type == "grenades") mode = WpMode.PickupGrenades;
                    break;
                case ZKind.Building:
                    if (t.IsFort) { if (t.Owner != Team && t.Owner != Team.Neutral && !t.Destroyed) mode = WpMode.EnterFort; }
                    else if (t.Type == "repair") { if (t.Owner == Team && !t.Destroyed) mode = WpMode.UnitRepair; }
                    if (CraneJob(t)) mode = WpMode.CraneRepair;
                    break;
                case ZKind.Cannon: case ZKind.Vehicle:
                    if (t.Owner == Team.Neutral && !t.Destroyed) mode = WpMode.Enter;
                    else if (u.CanAttackObject(t)) mode = WpMode.Attack;
                    break;
                case ZKind.Robot:
                    if (u.CanAttackObject(t)) mode = WpMode.Attack;
                    break;
            }
            if (mode == WpMode.None) return;
            bool hostile = mode == WpMode.Attack || mode == WpMode.EnterFort;
            if (Difficulty == ZBotDifficulty.Easy && hostile && !finisher && easyAttackers >= EASY_MAX_ATTACKERS) { EasyHeldBack++; return; }   // DV60: finishing a won game is not an attack wave
            var wp = new ZWaypoint { mode = mode, refId = t.Id, attackTo = true, playerGiven = true };
            if (mode != WpMode.Attack) { wp.x = t.CenterX; wp.y = t.CenterY; }
            else { wp.x = t.LocX + 8; wp.y = t.LocY + 8; }
            if (!W.SendWaypoints(Team, u, new[] { wp })) { RejectedOrders++; return; }
            if (ClientView) Relay(u);                                                 // the server relays the list it kept, also when CheckWaypoint
                                                                                      // dropped ours (zcore.cpp:539-566, zserver_events.cpp:392-407)
            if (!u.Waypoints.Exists(x => x.mode == wp.mode && x.refId == wp.refId && x.x == wp.x && x.y == wp.y))   // CheckWaypoint (zcore.cpp:196-306)
            { RejectedOrders++; return; }                                             // dropped (attack_to alone is cleared for unarmed units)
            Orders++;
            elbWaitSince.Remove(u.Id);                                                // DV72: any CPU order ends the wait of an Electrobot for company
            if (Difficulty == ZBotDifficulty.Easy && hostile) easyAttackers++;
            OrdersByMode[mode] = OrdersByMode.TryGetValue(mode, out int n) ? n + 1 : 1;
            LastRoundOrders.Add(new BotOrder { unitId = u.Id, targetId = t.Id, mode = mode, time = W.Time });
            if (Verbose) Log.Add($"{W.Time:F0}s {Team} {u.Type}#{u.Id} -> {mode} {t.Type}#{t.Id}");
        }

        // ------------------------------------------------------------------ production
        /// <summary>zbot.cpp:1372-1432: every own building except radar, repair and bridges (= ZObject.IsProducer); first
        /// preferred unit in its build list at its level; if that differs from the current production: STOP then START.</summary>
        public void ChooseBuildOrders()
        {
            ElectrobotRound();                                                                      // electrobot_v1 DV46 (ZBot.Electrobot.cs)
            foreach (var b in W.Objects)
            {
                if (b.Owner != Team || b.Kind != ZKind.Building || !b.IsProducer) continue;
                string pick = null;
                foreach (var p in Preferred) if (ZBuildLists.InList(b.Type, b.BuildLevel, p)) { pick = p; break; }
                pick = LaserPick(b, pick);                                                          // DV71: a Laser squad now and then
                pick = ElectrobotPick(b, pick);                                                     // electrobot_v1 DV46: Zod's pick unless claimed
                if (pick == null || b.CurUnit == pick) continue;
                W.StopBuilding(Team, b);
                W.StartBuilding(Team, b, pick);
                BuildChanges++; if (pick == "laser") LaserPicks++;
                Log.Add($"{W.Time:F0}s {Team} {b.Type}({b.LocX / 16},{b.LocY / 16}) L{b.Level} -> build {pick}");
            }
        }

        /// <summary>OpenZ deviation DV71 "the CPU builds Lasers sometimes". Zod's preferred list (zbot.cpp:20-27) has no Laser, so a
        /// producer that can make Lasers (robot factory and fort at build levels 4-5, zbuildlist.cpp) builds only Zod's pick (Pyros,
        /// Medium tanks). With DV71 such a producer makes a Laser squad in place of Zod's pick on a fixed rhythm of its own built
        /// units (the Electrobot is not counted): a robot factory its 2nd unit and then every <see cref="LASER_EVERY"/>-th; a fort the
        /// first <see cref="LASER_RUN_FORT"/> of every <see cref="LASER_EVERY_FORT"/> units (it shares its time with the Electrobot and
        /// most of the CPU's factories cannot make Lasers at all, so the fort carries more of the mix than a factory). Every other unit
        /// stays Zod's pick; producers that cannot make Lasers are untouched. A fort claimed for the Electrobot (DV46) first finishes
        /// the unit it is building (ZBot.Electrobot.cs), so a started Laser squad is never thrown away. Deterministic: the building's
        /// count of built units, no random draw, the same STOP_BUILDING + START_BUILDING commands. Normal and Easy.
        /// Switch: ZWorld.BotLaserDeviation.</summary>
        public const int LASER_EVERY = 3, LASER_EVERY_FORT = 3, LASER_RUN_FORT = 2;
        /// <summary>DV71 statistics (checks): production changes to a Laser squad.</summary>
        public int LaserPicks { get; private set; }

        /// <summary>DV71: Zod's pick of this producer, or the Laser when its rhythm says so (public for the checks).</summary>
        public string LaserPick(ZObject b, string zodPick)
        {
            if (!ZWorld.BotLaserDeviation || zodPick == null || !ZBuildLists.InList(b.Type, b.BuildLevel, "laser")) return zodPick;
            if (b.IsFort) return b.BuiltCount % LASER_EVERY_FORT < LASER_RUN_FORT ? "laser" : zodPick;
            return b.BuiltCount % LASER_EVERY == 1 ? "laser" : zodPick;
        }

        /// <summary>Easy only: our leaders / vehicles whose known first waypoint is an attack or a fort entry on a live object.</summary>
        int CountHostileOrders()
        {
            int n = 0;
            foreach (var o in W.Objects)
            {
                if (!o.IsMobile || o.Owner != Team || Gone(o) || (o.Kind == ZKind.Robot && o.IsMinion)) continue;
                var wps = KnownWaypoints(o);
                if (wps.Count > 0 && (wps[0].mode == WpMode.Attack || wps[0].mode == WpMode.EnterFort) && BotObject(wps[0].refId) != null) n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ helpers
        List<ZObject> AI(ZObject o) { if (!aiList.TryGetValue(o, out var l)) aiList[o] = l = new List<ZObject>(); return l; }

        /// <summary>The candidate list MatchTargets_3 built for a unit or target (read-only use, for checks).</summary>
        public IReadOnlyList<ZObject> Candidates(ZObject o) => AI(o);

        static double Dist(ZObject a, ZObject b)                                          // DistanceFromObject (zobject.cpp:918-926)
        {
            int dx = a.LocX - b.LocX, dy = a.LocY - b.LocY;
            return Math.Sqrt((double)(dx * dx + dy * dy));
        }

        static ZObject Nearest(ZObject from, List<ZObject> l)                             // zobject.cpp:3917-3938, first wins ties
        {
            if (l.Count == 0) return null;
            var best = l[0]; double bd = Dist(from, best);
            for (int i = 0; i < l.Count; i++) { double d = Dist(from, l[i]); if (d < bd) { bd = d; best = l[i]; } }
            return best;
        }

        /// <summary>About to be deleted this tick (the client already got DELETE/DESTROY_OBJECT): killed units, robots that
        /// entered a vehicle or a repair station, picked-up grenade boxes.</summary>
        static bool Gone(ZObject o) => o.KillAt >= 0 || (o.Destroyed && o.CanBeDestroyed);

        /// <summary>zbot_events.cpp:96-118: of the map items the bot keeps only flags and grenade boxes.</summary>
        public bool VisibleToBot(ZObject o) => !ClientView || o.Kind != ZKind.MapItem || o.Type == "flag" || o.Type == "grenades";

        /// <summary>GetObjectFromID on the bot's object list.</summary>
        ZObject BotObject(int id)
        {
            if (id < 0) return null;
            var o = W.Get(id);
            if (o == null || Gone(o) || !VisibleToBot(o)) return null;
            return o;
        }

        // ------------------------------------------------------------------ the bot client's map regions
        int rw, rh; int[] robotRegion, vehicleRegion;

        /// <summary>zmap.cpp:2004-2034 tile passability (same class mapping as ZWorld.InitPathfinding: water = robots only,
        /// impassable and deep water = nobody), flood-filled 4-connected like ZPath_Finding_RegionInfo::BuildRegions.</summary>
        void BuildClientRegions()
        {
            var m = W.Map; rw = m.width; rh = m.height;
            robotRegion = new int[rw * rh]; vehicleRegion = new int[rw * rh];
            for (int x = 0; x < rw; x++)
                for (int y = 0; y < rh; y++)
                {
                    var c = m.ClassAt(x, y);
                    bool r = c == ZClass.Ground || c == ZClass.Road || c == ZClass.Water;
                    bool v = c == ZClass.Ground || c == ZClass.Road;
                    robotRegion[y * rw + x] = r ? -1 : -2;
                    vehicleRegion[y * rw + x] = v ? -1 : -2;
                }
            Fill(robotRegion); Fill(vehicleRegion);
        }

        void Fill(int[] reg)
        {
            int cur = 0; var q = new List<int>(rw * rh);
            for (int x = 0; x < rw; x++)
                for (int y = 0; y < rh; y++)
                {
                    if (reg[y * rw + x] != -1) continue;
                    q.Clear(); reg[y * rw + x] = cur; q.Add(y * rw + x);
                    for (int i = 0; i < q.Count; i++)
                    {
                        int px = q[i] % rw, py = q[i] / rw;
                        Add(reg, q, px - 1, py, cur); Add(reg, q, px + 1, py, cur); Add(reg, q, px, py - 1, cur); Add(reg, q, px, py + 1, cur);
                    }
                    cur++;
                }
        }

        void Add(int[] reg, List<int> q, int x, int y, int cur)
        {
            if (x < 0 || y < 0 || x >= rw || y >= rh || reg[y * rw + x] != -1) return;
            reg[y * rw + x] = cur; q.Add(y * rw + x);
        }

        /// <summary>zpath_finding.cpp:255-285 on the bot's own regions (or ZWorld.Path when ClientView is off).</summary>
        public bool InSameRegion(int sx, int sy, int ex, int ey, bool isRobot)
        {
            if (!ClientView) return W.Path.InSameRegion(sx, sy, ex, ey, isRobot);
            sx >>= 4; sy >>= 4; ex >>= 4; ey >>= 4;
            if (sx < 0 || sy < 0 || ex < 0 || ey < 0 || sx >= rw || sy >= rh || ex >= rw || ey >= rh) return true;
            var reg = isRobot ? robotRegion : vehicleRegion;
            return reg[sy * rw + sx] == reg[ey * rw + ex];
        }

        // ------------------------------------------------------------------ what the bot client knows about waypoints
        sealed class Known
        {
            public readonly List<ZWaypoint> list = new List<ZWaypoint>();   // last list relayed to our team
            public bool live;                                                  // ours without a break since that relay
        }
        readonly Dictionary<int, Known> known = new Dictionary<int, Known>();
        static readonly List<ZWaypoint> none = new List<ZWaypoint>();

        /// <summary>The unit's waypoint list as the bot client knows it (server list when ClientView is off).</summary>
        public IReadOnlyList<ZWaypoint> KnownWaypoints(ZObject o)
        {
            if (!ClientView) return o.Waypoints;
            return known.TryGetValue(o.Id, out var k) ? k.list : none;
        }

        void Relay(ZObject o)
        {
            if (!known.TryGetValue(o.Id, out var k)) known[o.Id] = k = new Known();
            k.list.Clear(); k.list.AddRange(o.Waypoints); k.live = true;
        }

        /// <summary>
        /// Runs after every server tick and mirrors RelayObjectWayPoints: a unit created for our team arrives with its list
        /// (factory exit, repair-station exit); KillWP relays the whole remaining list; a leader or vehicle whose player-given
        /// waypoints changed got a SEND_WAYPOINTS from some client (relayed); a known first waypoint that vanished without
        /// KillWP was cut by a snipe (relayed). Server-side insertions are not relayed. Units of other teams get no relays, so
        /// a recaptured vehicle keeps the stale list the bot last saw until its next relay (Zod quirk, kept).
        /// </summary>
        void ObserveRelays()
        {
            foreach (var o in W.Objects)
            {
                if (!o.IsMobile) continue;
                bool ours = o.Owner == Team;
                if (!known.TryGetValue(o.Id, out var k))
                {
                    k = new Known(); known[o.Id] = k;
                    if (ours) Relay(o);
                    continue;
                }
                if (!ours) { k.live = false; continue; }
                if (o.UpdatedWaypoints) { Relay(o); continue; }
                if (!k.live || o.IsMinion) continue;
                if ((k.list.Count > 0 && !o.Waypoints.Contains(k.list[0])) || !SamePlayerGiven(k.list, o.Waypoints)) Relay(o);
            }
        }

        static bool SamePlayerGiven(List<ZWaypoint> a, List<ZWaypoint> b)
        {
            int i = 0, j = 0;
            while (true)
            {
                while (i < a.Count && !a[i].playerGiven) i++;
                while (j < b.Count && !b[j].playerGiven) j++;
                if (i >= a.Count || j >= b.Count) return i >= a.Count && j >= b.Count;
                if (!a[i].Equals(b[j])) return false;
                i++; j++;
            }
        }

        /// <summary>Forget deleted objects (delete_object_event, zbot_events.cpp:171-185).</summary>
        void Purge()
        {
            List<int> dead = null;
            foreach (var kv in known) if (W.Get(kv.Key) == null) (dead ??= new List<int>()).Add(kv.Key);
            if (dead != null) foreach (var id in dead) known.Remove(id);
            // stale ai lists of deleted objects
            List<ZObject> gone = null;
            foreach (var kv in aiList) if (W.Get(kv.Key.Id) != kv.Key) (gone ??= new List<ZObject>()).Add(kv.Key);
            if (gone != null) foreach (var o in gone) { aiList.Remove(o); aiOrig.Remove(o); }
        }
    }
}
