using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Shared helpers of the version 1.1 counted checks (menu OpenZ/V1.1/...). Every check runs the headless simulation
    /// (ZWorld.Create + Tick): no scene, no Play mode, nothing saved. The helpers only use the public members of the simulation.
    /// The same file is part of each of the four bug kits (b1 to b4); the copies are identical.
    /// </summary>
    public static class OzV11Kit
    {
        // ------------------------------------------------------------------ switches
        /// <summary>Every public static bool / int / double switch of the simulation as it was, put back by Dispose.</summary>
        public sealed class Switches : IDisposable
        {
            readonly List<KeyValuePair<FieldInfo, object>> saved = new List<KeyValuePair<FieldInfo, object>>();
            internal Switches()
            {
                foreach (var t in new[] { typeof(ZWorld), typeof(ZPathing), typeof(ZObject), typeof(ZElectro) })
                    foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                        if (!f.IsLiteral && !f.IsInitOnly && (f.FieldType == typeof(bool) || f.FieldType == typeof(int) || f.FieldType == typeof(double)))
                            saved.Add(new KeyValuePair<FieldInfo, object>(f, f.GetValue(null)));
            }
            public void Dispose() { foreach (var kv in saved) kv.Key.SetValue(null, kv.Value); }
        }

        /// <summary>Sets a static switch by name when the simulation has it (a kit that is not installed is simply not there).</summary>
        public static bool TrySet(Type type, string field, object value)
        {
            var f = type.GetField(field, BindingFlags.Public | BindingFlags.Static);
            if (f == null || f.IsLiteral || f.IsInitOnly) return false;
            f.SetValue(null, value);
            return true;
        }

        /// <summary>A static counter by name, 0 when the simulation does not have it.</summary>
        public static int Counter(Type type, string field)
        {
            var f = type.GetField(field, BindingFlags.Public | BindingFlags.Static);
            return f == null ? 0 : Convert.ToInt32(f.GetValue(null));
        }

        /// <summary>The switches of a normal game (View/ZGame.cs Awake with the default settings: no 'Zod exact', Medium, one CPU),
        /// plus the version 1.1 rules that are installed. Dispose the result to put everything back.</summary>
        public static Switches GameSwitches()
        {
            var s = new Switches();
            ZWorld.SnipeDeviation = true; ZWorld.FortGunDeviation = true; ZWorld.ApcCrewDeviation = true; ZWorld.DuelDeviation = true; ZWorld.VeteranDeviation = true;
            ZWorld.ToughPrepareDeviation = true; ZWorld.FormationDeviation = true; ZWorld.ClearanceDeviation = true; ZWorld.LosDeviation = true; ZWorld.FortMortarDeviation = true;
            ZWorld.BuildingReachDeviation = true; ZWorld.ExposedDriverDeviation = true; ZWorld.ApproachDeviation = true; ZWorld.ArmourChipDeviation = false; ZWorld.NoTankDuelVsJeep = true;
            ZWorld.HeavyPairDeviation = true; ZWorld.CraneGunDeviation = true; ZWorld.HarbourWrecksDeviation = true; ZWorld.DetailPropsDeviation = true; ZWorld.FortRushDeviation = true;
            ZWorld.BoardNearestDeviation = true; ZWorld.ParkedPathDeviation = true; ZWorld.CarParkBotDeviation = true; ZWorld.GrabPolicyDeviation = true; ZWorld.GrenadeRuleDeviation = true;
            ZWorld.FortDefenderDeviation = true; ZWorld.PathV3Deviation = true; ZWorld.ElectrobotDeviation = true; ZWorld.ElectrobotBotDeviation = true; ZWorld.ElectrobotEscortDeviation = true;
            ZWorld.ElectrobotAllLevels = false; ZWorld.KnockHoldDeviation = true; ZWorld.RunOverDeviation = true; ZWorld.RocketSpeedDeviation = true; ZWorld.HelpDeviation = true;
            ZWorld.CaptureRunDeviation = true; ZWorld.CloseGrabDeviation = true; ZWorld.BotCommitDeviation = true; ZWorld.BotLaserDeviation = true; ZWorld.BotFinishDeviation = true;
            ZWorld.RepairInPlaceDeviation = true; ZWorld.CraneFixDeviation = true; ZPathing.VehicleFirst = true; ZWorld.CraneAnyRuinDeviation = true; ZWorld.RepairBayFireDeviation = true;
            ZWorld.UnmannedStopDeviation = true; ZWorld.RiflePairDeviation = true; ZWorld.FireOnMoveDeviation = true; ZWorld.EjectLockDeviation = true; ZWorld.EnterFallbackDeviation = true;
            ZWorld.L17CrewsDeviation = false; ZWorld.FortDoorGunDeviation = false; ZWorld.ExitLanesDeviation = true;
            foreach (var name in V11Switches) TrySet(typeof(ZWorld), name, true);
            TrySet(typeof(ZWorld), "StrikeGroupDeviation", false);                          // DV107 enemy strike groups: off in every counted check unless the check sets it itself (a Play session leaves it on)
            TrySet(typeof(ZWorld), "BotHeavyDeviation", false);                             // DV115 the CPU's unit mix: off in every counted check unless the check sets it itself (a Play session leaves it on)
            TrySet(typeof(ZWorld), "OvertakeDeviation", false);                             // DV105: off in every counted check unless the check sets it itself (a Play session may leave it on)
            TrySet(typeof(ZWorld), "GrenadeBoostDeviation", false);                         // DV104: the same (every Play session sets it on); the grenade check sets it itself
            TrySet(typeof(ZWorld), "CalmMoveDeviation", false);                             // b49: off in every counted check unless the check sets it itself (a Play session leaves it on)
            TrySet(typeof(ZWorld), "FortRunDeviation", false); TrySet(typeof(ZWorld), "RockRoundDeviation", false); TrySet(typeof(ZWorld), "FriendsOverlap", false);   // KC1: the same for DV102, DV103 and the overlap option
            TrySet(typeof(ZWorld), "CalmFollowDeviation", OzKC1bFollowCount.InChecks);      // DV109 calm follow: the same (a Play session leaves it on); its own check sets it (and keeps it on for KC1's crowd runs)
            TrySet(typeof(ZWorld), "VeteranHealDeviation", false); TrySet(typeof(ZWorld), "FortSniperDeviation", false);   // DV110 / DV111: off in every counted check unless the check sets them itself (the game has them on)
            TrySet(typeof(ZWorld), "SniperHeadshotDeviation", false);                       // DV116 sniper headshots: off in every counted check unless the check sets it itself (a Play session leaves it on)
            TrySet(typeof(ZWorld), "SniperSteadyDeviation", false);                         // DV120 snipers stand and fire: off in every counted check unless the check sets it itself (a Play session leaves it on)
            TrySet(typeof(ZWorld), "ToughSteadyDeviation", false);                          // DV123 Toughs stand and fire: off in every counted check unless the check sets it itself (a Play session leaves it on)
            TrySet(typeof(ZWorld), "NoAutoFlagUnderFireDeviation", false);                  // DV130 no flag run of its own under fire: off in every counted check unless the check sets it itself (a Play session leaves it on)
            TrySet(typeof(ZWorld), "HeavyFallDeviation", false);                            // DV124 big fallers fell trees: off in every counted check unless the check sets it itself (a Play session leaves it on)
            TrySet(typeof(ZWorld), "GroundFireDeviation", false);                           // DV117: off in every counted check unless the check sets it itself (a Play session leaves it on)
            TrySet(typeof(ZWorld), "ProneTargetDeviation", false);                          // DV122 a lying sniper is a smaller target: off in every counted check unless the check sets it itself (a Play session leaves it on)
            TrySet(typeof(ZWorld), "HeroSquadDeviation", false);                            // DV108: no extra hero squads from the level data in a counted check unless it sets the switch itself (a Play session leaves it on)
            TrySet(typeof(ZWorld), "ShuttlePlayableDeviation", false); TrySet(typeof(ZWorld), "ShuttleOn", true);   // DV118 the playable shuttle: off in every counted check unless the check sets it itself (a Play session leaves it on)
            TrySet(typeof(ZWorld), "L17ShuttlesDeviation", false); TrySet(typeof(ZWorld), "L17StartShuttle", false);   // DV134 level 17's own shuttles: off in every counted check unless the check sets it itself (a Play session leaves it on)
            TrySet(typeof(ZWorld), "HeroBoostDeviation", false);                            // DV112: hero squads stronger + self-repair: off in every counted check unless it sets the switch itself (a Play session leaves it on)
            TrySet(typeof(ZWorld), "HeroPlusDeviation", false);                             // DV133: ALAN & BRAD +15 % on top of DV112: off in every counted check unless it sets the switch itself (on in the game, where it acts while DV112 is on)
            TrySet(typeof(ZWorld), "JeepChargeDeviation", false);                           // DV121 the jeep runs robots over: off in every counted check unless the check sets it itself (a Play session leaves it on)
            TrySet(typeof(ZPathing), "ZoneSideCommit", true);
            return s;
        }

        /// <summary>The rule switches of the version 1.1 bug kits on ZWorld (each exists only when its kit is installed).</summary>
        public static readonly string[] V11Switches = { "SnipeSafeSpotDeviation", "ElectroSteadyDeviation", "GrabCertainDeviation" };

        // ------------------------------------------------------------------ maps
        /// <summary>An empty map of plain ground with the given objects (kind as in the map files, tile coordinates).</summary>
        public static string Plain(int w, int h, params (string kind, int x, int y, string owner)[] objects)
        {
            var sb = new StringBuilder("{\"file\":\"v11_plain\",\"planet\":\"desert\",\"width\":" + w + ",\"height\":" + h + ",\"classes\":[");
            sb.Append(string.Join(",", Enumerable.Repeat("0", w * h))).Append("],\"tiles\":[").Append(string.Join(",", Enumerable.Repeat("0", w * h)));
            sb.Append("],\"zones\":[{\"x\":0,\"y\":0,\"w\":" + w + ",\"h\":" + h + "}],\"objects\":[");
            for (int i = 0; i < objects.Length; i++)
                sb.Append(i > 0 ? "," : "").Append("{\"x\":" + objects[i].x + ",\"y\":" + objects[i].y + ",\"owner\":\"" + objects[i].owner + "\",\"kind\":\"" + objects[i].kind + "\",\"ot\":0,\"oid\":0,\"blevel\":0,\"extra_links\":0,\"health_percent\":100}");
            return sb.Append("]}").ToString();
        }

        /// <summary>A level map without its start robots, vehicles and guns, and with nobody owning the blue buildings (no enemy fort
        /// gun, no fort defender): the terrain, bridges, buildings, flags and map items stay. For checks that measure movement only.</summary>
        public static string NoUnits(string mapJson)
        {
            var m = ZMapData.FromJson(mapJson);
            m.objects.RemoveAll(o => { string c = o.kind.Split(':')[0]; return c == "robot" || c == "vehicle" || c == "cannon"; });
            foreach (var o in m.objects) if (o.owner == "blue") o.owner = "null";
            return UnityEngine.JsonUtility.ToJson(m);
        }

        // ------------------------------------------------------------------ worlds, units, orders
        /// <summary>A world in which blue is a CPU team that gives no orders of its own (so red counts as the player's team).</summary>
        public static ZWorld World(string settings, string mapJson, int seed)
        {
            var w = ZWorld.Create(settings, mapJson, seed);
            w.Bots.Add(new ZBot(w, Team.Blue, seed + 101) { Ignored = true });
            w.Events.Clear();
            return w;
        }

        /// <summary>A blue grunt that cannot move, shoot or die, far from the action: with it the game is never over (a won game
        /// stops every unit, endgame rule DV10).</summary>
        public static ZObject Dummy(ZWorld w, int cx, int cy)
        {
            var d = w.CreateRobotGroup("grunt", cx - 8, cy - 8, Team.Blue, 1);
            d.MoveSpeed = 0; d.Damage = 0; d.MaxHealth = d.Health = 1000000000;
            return d;
        }

        /// <summary>Robots: a squad of the usual size (or of <paramref name="amount"/>); vehicles and guns: one, crewed unless neutral.</summary>
        public static ZObject Place(ZWorld w, string type, int cx, int cy, Team team, int amount = -1) =>
            ZBuildLists.IsRobot(type) ? w.CreateRobotGroup(type, cx - 8, cy - 8, team, amount)
                                      : w.CreateObject(ZBuildLists.IsCannon(type) ? "cannon" : "vehicle", type, cx - 16, cy - 16, team);

        public static IEnumerable<ZObject> Squad(ZObject o) { yield return o; foreach (var m in o.Minions) yield return m; }

        /// <summary>The CPU's attack order (ZBot.Command: attack-move flag set, aimed at the target's top-left tile centre).</summary>
        public static void CpuAttack(ZWorld w, ZObject u, ZObject t) =>
            w.SendWaypoints(u.Owner, u, new[] { new ZWaypoint { mode = WpMode.Attack, refId = t.Id, attackTo = true, playerGiven = true, x = t.LocX + 8, y = t.LocY + 8 } });

        /// <summary>The player's right-click orders (View/PlayerInput.cs BuildWaypoint).</summary>
        public static void PlayerAttack(ZWorld w, ZObject u, ZObject t) =>
            w.SendWaypoints(u.Owner, u, new[] { new ZWaypoint { mode = WpMode.Attack, refId = t.Id, x = t.CenterX, y = t.CenterY } });
        public static void PlayerMove(ZWorld w, ZObject u, int x, int y, bool attackMove = true) =>
            w.SendWaypoints(u.Owner, u, new[] { ZWaypoint.Move(x, y, attackMove) });
        public static void PlayerEnter(ZWorld w, ZObject u, ZObject t) =>
            w.SendWaypoints(u.Owner, u, new[] { new ZWaypoint { mode = WpMode.Enter, refId = t.Id, x = t.CenterX, y = t.CenterY } });

        public static double Dist(ZObject a, ZObject b) => Math.Sqrt((double)(a.CenterX - b.CenterX) * (a.CenterX - b.CenterX) + (double)(a.CenterY - b.CenterY) * (a.CenterY - b.CenterY));
        public static bool Alive(ZObject o) => o != null && !o.Destroyed && o.KillAt < 0;

        // ------------------------------------------------------------------ movement meter
        /// <summary>What one unit's body does, tick by tick: distance covered, reversals (a step that points more than 90 degrees away
        /// from the step before it), the most reversals inside any one second (shaking shows here), stops, and the last time it moved.</summary>
        public sealed class Motion
        {
            double px, py, hx, hy; bool was;
            readonly Queue<double> recent = new Queue<double>();
            public double Path, LastMove = -1;
            public int Reversals, WorstSecond, Stops, MovingTicks;
            public Motion(ZObject u) { px = u.PosX; py = u.PosY; }
            /// <summary>Call once per tick after ZWorld.Tick; true = the unit moved in this tick.</summary>
            public bool Step(ZObject u, double t)
            {
                double dx = u.PosX - px, dy = u.PosY - py; px = u.PosX; py = u.PosY;
                double l = Math.Sqrt(dx * dx + dy * dy); bool mv = l > 1e-9;
                if (was && !mv) Stops++;
                was = mv;
                if (!mv) return false;
                MovingTicks++; Path += l; LastMove = t; dx /= l; dy /= l;
                if ((hx != 0 || hy != 0) && dx * hx + dy * hy < 0)
                {
                    Reversals++; recent.Enqueue(t);
                    while (recent.Count > 0 && t - recent.Peek() > 1.0) recent.Dequeue();
                    if (recent.Count > WorstSecond) WorstSecond = recent.Count;
                }
                hx = dx; hy = dy;
                return true;
            }
        }

        public static string Verdict(bool pass, string what) => (pass ? "PASS  " : "FAIL  ") + what;

        /// <summary>The PASS / FAIL lines of a check's report as one table cell (the "Measured" column of a row of the parity report's
        /// Deviations block, Editor/OpenZParity.V11*.cs). pass = at least one verdict line and none of them FAIL.</summary>
        public static string VerdictCell(string report, out bool pass)
        {
            var lines = (report ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("PASS  ") || l.StartsWith("FAIL  ")).ToList();
            pass = lines.Count > 0 && lines.All(l => l.StartsWith("PASS"));
            return lines.Count == 0 ? "no verdict line in the check's report" : string.Join("; ", lines.Select(l => l.Substring(0, 4) + ": " + l.Substring(6)));
        }

#if UNITY_EDITOR
        // ------------------------------------------------------------------ editor side
        public static string SettingsText => UnityEngine.Resources.Load<UnityEngine.TextAsset>("OpenZ/default_settings").text;
        public static string MapText(string level) => UnityEngine.Resources.Load<UnityEngine.TextAsset>("OpenZ/Maps/" + level + "_grid").text;

        /// <summary>Runs a check with the game's switches, logs its report, puts the switches back.</summary>
        public static void RunMenu(string title, Func<string> check)
        {
            if (UnityEngine.Application.isPlaying) { UnityEngine.Debug.LogWarning("[OpenZ V1.1] " + title + ": leave Play mode first (the check sets the simulation's static switches)."); return; }
            string report;
            using (GameSwitches()) report = check();
            UnityEngine.Debug.Log("[OpenZ V1.1] " + title + "\n" + report);
        }
#endif
    }
}
