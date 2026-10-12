using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using OpenZ.Sim;
#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;
#endif

namespace OpenZ.EditorTools
{
    /// <summary>
    /// The parity report's "Deviations" block (the project rules "Balance and deviations", MASTER_TODO DC2-DC5): every deliberate OpenZ
    /// change from the Zod Engine, read from the LIVE code and measured headless in the same sim the PASS/FAIL rows use, never
    /// typed in by hand, so the list cannot drift from the build. Not counted in the PASS total.
    ///   DV1 bridge auto-repair (ZWorld.BridgeRepairSeconds), DV2 driver snipe chance (the project rules targets, measured as time to
    ///   snipe), DV3 L02 head start (ZGame.HeadStartTypes, pref oz_headstart), DV4 Easy CPU (ZBot EASY_*), DV5 balance tweaks
    ///   (every unit stat against the Zod literals of OpenZParity.Zod.cs + any public static knob of an OpenZ.Sim type whose name
    ///   contains "Balance"), DV6 the sim-side pathing / spacing layer (OpenZ.Sim.ZPathing, when imported).
    /// A change that is planned but not in this build says NOT IN BUILD; a change whose measurement disagrees with its own code
    /// says CHECK. Menus: OpenZ/Deviations/Write Deviations Block (refreshes only this block of parity_report.md, a few seconds),
    /// OpenZ/Deviations/Head Start On L02 (oz_headstart) and OpenZ/Deviations/Zod Exact Driver Sniping (oz_zod_exact), the
    /// editor toggles until the main menu (UI1) has them.
    /// </summary>
    public static partial class OpenZParity
    {
        sealed class Deviation { public string id, change, zod, openz, measured, toggle, status; }

        const string DeviationsHeading = "## Deviations";
        const string DevActive = "ACTIVE", DevNotInBuild = "NOT IN BUILD", DevCheck = "CHECK";

        /// <summary>The Deviations block as markdown (also used by the headless harness, which passes its own prefs).</summary>
        internal static string DeviationsMarkdown(Func<string, int, int> getInt)
        {
            var sb = new StringBuilder();
            AppendDeviations(sb, CollectDeviations(getInt));
            return sb.ToString();
        }

        static List<Deviation> CollectDeviations(Func<string, int, int> getInt)
        {
            var list = new List<Deviation>();
            foreach (var make in new Func<Func<string, int, int>, Deviation>[] { DevBridge, DevSnipe, DevHeadStart, DevEasy, DevBalance, DevPathing, DevFortGun })
            {
                try { list.Add(make(getInt)); }
                catch (Exception ex) { list.Add(new Deviation { id = "DV?", change = make.Method.Name, zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            }
            try { list.Add(DevEndFreeze(getInt)); }                                          // endgame_ui_v1 (DV10)
            catch (Exception ex) { list.Add(new Deviation { id = "DV10", change = "DevEndFreeze", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevDuel(getInt)); }                                               // vehicle_combat_v1 (DV9)
            catch (Exception ex) { list.Add(new Deviation { id = "DV9", change = "DevDuel", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevToughPrepare(getInt)); }                                       // weapons_v2 (DV11)
            catch (Exception ex) { list.Add(new Deviation { id = "DV11", change = "DevToughPrepare", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevVeterans(getInt)); }                                           // veterancy_v1 (DV12)
            catch (Exception ex) { list.Add(new Deviation { id = "DV12", change = "DevVeterans", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevBridgeQueue(getInt)); }                                        // bridge_queue_v1 (DV8)
            catch (Exception ex) { list.Add(new Deviation { id = "DV8", change = "DevBridgeQueue", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevFormation(getInt)); }                                        // los_clearance_v1 (DV24)
            catch (Exception ex) { list.Add(new Deviation { id = "DV24", change = "DevFormation", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevClearance(getInt)); }                                         // los_clearance_v1 (DV22)
            catch (Exception ex) { list.Add(new Deviation { id = "DV22", change = "DevClearance", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevLineOfSight(getInt)); }                                      // los_clearance_v1 (DV23)
            catch (Exception ex) { list.Add(new Deviation { id = "DV23", change = "DevLineOfSight", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            list.Add(DevOrders());
            list.Add(DevQueue());
            try { list.Add(DevApcCrew(getInt)); }                                            // apc_v1 (DV15)
            catch (Exception ex) { list.Add(new Deviation { id = "DV15", change = "DevApcCrew", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            AddCombatNbDeviations(list, getInt);                                              // combat_nb_v1 (DV25-DV29, DV9 NB12 note; OpenZParity.CombatNB.cs)
            try { list.Add(DevHeavyPair(getInt)); }                                          // vehicles_nb_v1 (DV30)
            catch (Exception ex) { list.Add(new Deviation { id = "DV30", change = "DevHeavyPair", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevParkedStart(getInt)); }
            catch (Exception ex) { list.Add(new Deviation { id = "DV32", change = "DevParkedStart", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            list.Add(DevRadarDots(getInt));
            list.Add(DevGrabPolicy(getInt));
            try { list.Add(DevGrenadeRule(getInt)); }
            catch (Exception ex) { list.Add(new Deviation { id = "DV40", change = "DevGrenadeRule", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevHarbourWrecks(getInt)); }                                      // l17_details_v1 harbour wrecks (DV39; the package called it DV34, taken by boarding_nb_v1)
            catch (Exception ex) { list.Add(new Deviation { id = "DV39", change = "DevHarbourWrecks", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevCraneGun(getInt)); }                                           // vehicles_nb_v1 (DV31)
            catch (Exception ex) { list.Add(new Deviation { id = "DV31", change = "DevCraneGun", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            AddBoardingNbDeviations(list, getInt);                                           // boarding_nb_v1 (DV34-DV37; OpenZParity.BoardingNB.cs)
            try { list.Add(DevFortDefender(getInt)); }                                       // fort_defender_v1 (DV41; OpenZParity.FortDefender.cs)
            catch (Exception ex) { list.Add(new Deviation { id = "DV41", change = "DevFortDefender", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevPathV3(getInt)); }                                             // pathing_v3 (DV44 re-scoped; OpenZParity.PathV3.cs)
            catch (Exception ex) { list.Add(new Deviation { id = "DV44", change = "DevPathV3", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevSoftUnstick(getInt)); }                                        // pathing_v3 (DV47 soft unstick; OpenZParity.PathV3.cs)
            catch (Exception ex) { list.Add(new Deviation { id = "DV47", change = "DevSoftUnstick", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            AddElectrobotDeviations(list, getInt);                                           // electrobot_v1 (DV45 Electrobot, DV46 CPU; OpenZParity.Electrobot.cs)
            AddCombatFeelDeviations(list, getInt);
            AddAiCombatDeviations(list, getInt);
            try { list.Add(DevFireOnMove(getInt)); }
            catch (Exception ex) { list.Add(new Deviation { id = "DV57", change = "DevFireOnMove", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            AddBoardingRulesDeviations(list, getInt);
            try { list.Add(DevL17Crews(getInt)); }
            catch (Exception ex) { list.Add(new Deviation { id = "DV63", change = "DevL17Crews", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            list.Add(DevRunOver(getInt));
            try { list.Add(DevDetailProps(getInt)); }
            catch (Exception ex) { list.Add(new Deviation { id = "DV67", change = "DevDetailProps", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevBotLasers(getInt)); }                                          // DV71 the CPU builds Lasers sometimes
            catch (Exception ex) { list.Add(new Deviation { id = "DV71", change = "DevBotLasers", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevElectrobotEscort(getInt)); }                                   // DV72 the CPU's Electrobot leaves with another unit
            catch (Exception ex) { list.Add(new Deviation { id = "DV72", change = "DevElectrobotEscort", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevCraneAnyRuin(getInt)); }                                       // DV74 a crane rebuilds a destroyed building / bridge in any zone
            catch (Exception ex) { list.Add(new Deviation { id = "DV74", change = "DevCraneAnyRuin", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevRepairBayFire(getInt)); }                                      // DV77 a vehicle fires out of its repair bay on the way in and out too
            catch (Exception ex) { list.Add(new Deviation { id = "DV77", change = "DevRepairBayFire", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevRiflePair(getInt)); }                                          // DV78 rifle pairs: a grunt's rifle on a grunt
            catch (Exception ex) { list.Add(new Deviation { id = "DV78", change = "DevRiflePair", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevUnmannedStop(getInt)); }                                       // DV79 unmanned stop: a vehicle with nobody inside never moves
            catch (Exception ex) { list.Add(new Deviation { id = "DV79", change = "DevUnmannedStop", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevVehicleFirst(getInt)); }                                       // DV76 vehicles first: a vehicle is never held by robots of its own team
            catch (Exception ex) { list.Add(new Deviation { id = "DV76", change = "DevVehicleFirst", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevFortDoorGun(getInt)); }                                        // DV73 Hard: a missile cannon on each fort's door tower
            catch (Exception ex) { list.Add(new Deviation { id = "DV73", change = "DevFortDoorGun", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevIdleSweep(getInt)); }
            catch (Exception ex) { list.Add(new Deviation { id = "DV70", change = "DevIdleSweep", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevGunGround(getInt)); }                                          // DV80 gun ground: no stored gun on water or impassable ground
            catch (Exception ex) { list.Add(new Deviation { id = "DV80", change = "DevGunGround", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevIdleRest(getInt)); }                                           // DV75 idle rest: a long-idle crew rests in the open hatch (part of DC2)
            catch (Exception ex) { list.Add(new Deviation { id = "DV75", change = "DevIdleRest", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevStartHold(getInt)); }
            catch (Exception ex) { list.Add(new Deviation { id = "DV69", change = "DevStartHold", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevPatrol(getInt)); }
            catch (Exception ex) { list.Add(new Deviation { id = "DV68", change = "DevPatrol", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
            try { list.Add(DevExitLanes(getInt)); }
            catch (Exception ex) { list.Add(new Deviation { id = "DV66", change = "DevExitLanes", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
#if !OPENZ_HARNESS
            AddDeathDeviations(list);                                                         // deaths_mech_v1 (DV17-DV20, view only)
            AddMusicV3Deviation(list);                                                        // music_v3 (DV50, view only)
#endif
            return list;
        }

        static void AppendDeviations(StringBuilder sb, List<Deviation> devs)
        {
            sb.AppendLine(DeviationsHeading);
            sb.AppendLine();
            sb.AppendLine($"Deliberate OpenZ changes from the Zod Engine (the project rules \"Balance and deviations\"), read from the live code and measured " +
                          $"headless in the same sim as the rows above ({DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}). " +
                          $"Not counted in the PASS total. {DeviationSummary(devs)}.");
            sb.AppendLine();
            sb.AppendLine("| # | Change | Zod Engine | OpenZ (live code) | Measured (headless) | Toggle | Status |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var d in devs)
                sb.AppendLine($"| {d.id} | {Cell(d.change)} | {Cell(d.zod)} | {Cell(d.openz)} | {Cell(d.measured)} | {Cell(d.toggle)} | {(d.status.StartsWith(DevActive, StringComparison.Ordinal) ? d.status : "**" + d.status + "**")} |");
            sb.AppendLine();
            sb.AppendLine("weapons_v2 (presentation only, not a deviation: damage per roll, hit chance, range, cooldowns and the sim's rocket blast times stay Zod's): " +
                          "the psycho draws machine-gun bursts of about 11 tracer rounds/s over its 10 rolls/s (Zod's client: one bullet per 0.17 s); the sniper draws an " +
                          "aimed shot every 1.05-1.6 s (Zod's client: 0.78 s) with a crack tracer at Zod's 37.5 m/s, its killing roll at once, and kneels / lies prone " +
                          "on long shots; the laser draws a double tap of Zod-speed dashes per client shot. Every drawn round (bolt, tracer, dash, crack, jeep / gatling bullet) flies at Zod's 37.5 m/s x game speed, and a robot killed by one is thrown when it arrives (BlastWave.BulletDelay), never before. The Tough's prepare time is a SIM change: DV11 above (with DV11 " +
                          "off the view only holds the first rocket while the tube rises, at most 0.6 x its flight, and it lands on the sim's blast).");
            sb.AppendLine();
            sb.AppendLine("pyro_fire_v2 (presentation only, not a deviation: the Pyro's damage per roll, hit chance, 15 m range and cadence stay Zod's): " +
                          "the flame is drawn as the legacy M12's continuous flamethrower stream while a Pyro fires (Zod's client: one flame bullet per " +
                          "fire frame, ~4.6 a second, at 37.5 m/s; the stream uses the same speed and ends on the target), and the robot, vehicle or " +
                          "cannon it hits burns for 5 s with sticky fire, charred paint and a burning patch of ground: visual only, Zod's Pyro deals no " +
                          "burn damage (and, as in Zod, never attacks buildings or map items: explosive-only targets); decor and other sides' " +
                          "buildings the drawn stream only passes through burn visually for the legacy 12.5 s (no damage).");
            sb.AppendLine();
            sb.AppendLine("Not deviations (presentation only; the sim and its numbers are untouched): the 3D camera controls (WASD / arrows / edge / minimap, " +
                          "eased wheel zoom 22-230 m; the start height of 70 m is Zod's 800x600 framing), view-side unit spacing, legacy M12 visuals drawn on " +
                          "Zod's client fire clock at Zod's 37.5 m/s bullet speed, 3D positional audio. The pause menu's GAME SPEED (25-400 %) is Zod's own " +
                          "options menu (gmm_options.h:8-11); every level starts at 1x (row \"normal play game speed\").");
        }

        static string DeviationSummary(List<Deviation> devs)
        {
            int active = devs.Count(d => d.status.StartsWith(DevActive, StringComparison.Ordinal));
            var missing = devs.Where(d => d.status.StartsWith(DevNotInBuild, StringComparison.Ordinal)).Select(d => d.id).ToList();
            var check = devs.Where(d => d.status.StartsWith(DevCheck, StringComparison.Ordinal)).Select(d => d.id).ToList();
            return $"deviations: {active} active" + (missing.Count > 0 ? $", not in this build: {string.Join(" ", missing)}" : "") +
                   (check.Count > 0 ? $", CHECK: {string.Join(" ", check)}" : "");
        }

        static string Cell(string s) => string.IsNullOrEmpty(s) ? "-" : s.Replace("|", "/").Replace("\r", " ").Replace("\n", " ");
        static string Sec(double s) => double.IsInfinity(s) ? "never" : s.ToString(s < 10 ? "0.0" : "0", CultureInfo.InvariantCulture) + " s";

        // ------------------------------------------------------------------ DV1 bridge auto-repair
        static Deviation DevBridge(Func<string, int, int> getInt)
        {
            var w = RulesWorld("L02", 21);
            double secs = w.BridgeRepairSeconds;
            int zodMin = w.S.building_auto_repair_time, zodMax = zodMin + w.S.building_auto_repair_random_additional_time;
            var bridge = w.Objects.FirstOrDefault(o => o.IsBridge && !o.Destroyed);
            string measured; bool ok = false;
            if (bridge == null) measured = "no intact bridge on L02";
            else
            {
                Until(w, 1.0);                                                                      // Time > 0: destroyed in play, not at map load
                double t0 = w.Time;
                Kill(w, bridge);
                bool back = Until(w, zodMax + 30, pred: () => !bridge.Destroyed);
                double took = w.Time - t0;
                measured = back ? $"L02 bridge at ({bridge.LocX / 16},{bridge.LocY / 16}) destroyed at {t0:F2} s rebuilt itself after {took:F2} s"
                                : $"L02 bridge at ({bridge.LocX / 16},{bridge.LocY / 16}) not rebuilt within {zodMax + 30} s";
                ok = back && Math.Abs(took - (secs > 0 ? secs : zodMin)) <= (secs > 0 ? 0.015 : zodMax - zodMin + 0.015);
            }
            return new Deviation
            {
                id = "DV1", change = "Bridge auto-repair",
                zod = $"a destroyed bridge auto-repairs after building_auto_repair_time {zodMin} s + 0-{zodMax - zodMin} s (default_settings.txt, zobject.cpp:4235-4257); a crane repairs it sooner",
                openz = secs > 0 ? $"a bridge destroyed in play is impassable for ZWorld.BridgeRepairSeconds = {secs:0} s of game time, then rebuilds itself (countdown badge); the crane still repairs sooner; bridges that START destroyed (L17) keep Zod's {zodMin}-{zodMax} s; Zod's random roll is still drawn"
                                 : "BridgeRepairSeconds = 0: Zod's rule",
                measured = measured,
                toggle = "fixed OpenZ rule (ZWorld.BridgeRepairSeconds, 0 = Zod)",
                status = secs <= 0 ? DevNotInBuild : ok ? DevActive : DevCheck,
            };
        }

        // ------------------------------------------------------------------ DV2 driver snipe chance
        /// <summary>the project rules "Driver sniping is a deliberate OpenZ change": a grunt squad firing at the vehicle snipes its driver in
        /// about this many seconds (tanks: while the lid is open, as the snipe package defines it); 0 = reported only.</summary>
        static readonly (string attacker, string vehicle, bool openLid, double target)[] SnipeDuels =
        {
            ("grunt", "jeep", false, 15), ("grunt", "light", true, 0), ("grunt", "medium", true, 45), ("grunt", "heavy", true, 60),
            ("sniper", "jeep", false, 0),
        };
        const int SnipeTrials = 6;
        const double SnipeMaxT = 120;

        /// <summary>The snipe package's global switch (ZWorld.SnipeDeviation, DC2), read by reflection so this file also compiles
        /// against a build without it.</summary>
        static FieldInfo SnipeSwitch => typeof(ZWorld).GetField("SnipeDeviation", BindingFlags.Public | BindingFlags.Static);

        /// <summary>Seconds until a full squad of `attacker` (5 tiles away, auto-engaging) snipes the driver of a crewed enemy
        /// `vehicle`. Hull and squad are unkillable, so only the driver decides; the vehicle keeps its gun. openLid holds a
        /// tank's lid open every tick, otherwise the lid follows Zod's rule (it opens when the tank engages a snipe-capable unit).
        /// A trial with no snipe within SnipeMaxT is extrapolated from the driver damage dealt.</summary>
        static (double mean, int sniped, bool extrapolated) SnipeTime(string attacker, string vehicle, bool openLid)
        {
            double sum = 0; int sniped = 0; bool extra = false, never = false;
            for (int k = 0; k < SnipeTrials; k++)
            {
                var w = Legacy(40 + k);
                var lead = w.CreateRobotGroup(attacker, 56 * 16, 14 * 16, Team.Red);
                foreach (var r in new[] { lead }.Concat(lead.Minions)) r.MaxHealth = r.Health = 1000000000;
                var v = w.CreateObject("vehicle", vehicle, 61 * 16, 14 * 16, Team.Blue);        // owned = crewed by a full-health grunt (zvehicle.cpp:155-168)
                v.MaxHealth = v.Health = 1000000000; v.MoveSpeed = 0;
                int pool0 = v.Drivers.Count > 0 ? v.Drivers[0] : 0;
                double t0 = w.Time, when = -1;
                while (when < 0 && w.Time - t0 < SnipeMaxT)
                {
                    if (openLid && v.HasLid && v.Drivers.Count > 0) v.LidOpen = true;
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events) if (e.type == ZEventType.Sniped && e.objId == v.Id) when = w.Time - t0;
                    w.Events.Clear();
                }
                if (when >= 0) { sum += when; sniped++; continue; }
                int dealt = pool0 - (v.Drivers.Count > 0 ? v.Drivers[0] : pool0);
                if (dealt > 0) { sum += SnipeMaxT * pool0 / dealt; extra = true; } else never = true;
            }
            return (never ? double.PositiveInfinity : sum / SnipeTrials, sniped, extra);
        }

        static string Duel(string attacker, string vehicle, bool openLid, (double mean, int sniped, bool extrapolated) r) =>
            $"{attacker} squad on {vehicle}{(vehicle != "jeep" ? (openLid ? " (lid open)" : " (lid by Zod's rule)") : "")} " +
            $"{(r.extrapolated || double.IsInfinity(r.mean) ? "~" : "")}{Sec(r.mean)} ({r.sniped}/{SnipeTrials})";

        /// <summary>Zod's expected time for a full squad on a lid-less vehicle: driver pool (a full grunt) / driver damage per second.</summary>
        static double ZodSnipeEstimate(string attacker)
        {
            var z = Zod(attacker);
            int squad = ZodGroupSize.TryGetValue(attacker, out var n) ? n : 1;
            double perSecond = z.dmg * z.chance * z.snipe / z.interval * squad;
            return perSecond > 0 ? Zod("grunt").hp / perSecond : double.PositiveInfinity;
        }

        /// <summary>Zod group_amount (default_settings.txt; research table 1.1).</summary>
        static readonly Dictionary<string, int> ZodGroupSize = new Dictionary<string, int> { ["grunt"] = 3, ["psycho"] = 3, ["sniper"] = 3, ["tough"] = 2, ["pyro"] = 4, ["laser"] = 4 };

        /// <summary>The DC2 open-seat pairs as text for the DV2 row: attacker on target, the HP of one driver hit, the snipe chance.</summary>
        static string OpenSeatPairText()
        {
            var parts = new List<string>();
            foreach (var kv in ZWorld.OpenSeatPair)
            {
                string s = kv.Key.attacker + " on " + kv.Key.target + (kv.Value.hit > 0 ? " " + kv.Value.hit + " HP" : "");
                if (kv.Value.chance > 0) s += " at " + kv.Value.chance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                parts.Add(s);
            }
            return string.Join(", ", parts);
        }

        static Deviation DevSnipe(Func<string, int, int> getInt)
        {
            var sw = SnipeSwitch;
            object saved = sw?.GetValue(null);
            var openz = new List<string>(); var zod = new List<string>(); var ratios = new List<double>(); double onJeep = double.PositiveInfinity;
            try
            {
                if (sw != null) sw.SetValue(null, true);
                foreach (var (attacker, vehicle, openLid, target) in SnipeDuels)
                {
                    var r = SnipeTime(attacker, vehicle, openLid);
                    if (attacker == "grunt" && vehicle == "jeep") onJeep = r.mean;
                    if (target > 0) ratios.Add(r.mean / target);
                    openz.Add(Duel(attacker, vehicle, openLid, r) + (target > 0 ? $" target ~{target:0} s" : ""));
                }
                if (sw != null)
                {
                    sw.SetValue(null, false);
                    foreach (var (attacker, vehicle, openLid) in new[] { ("grunt", "jeep", false), ("grunt", "light", true), ("sniper", "jeep", false) })
                        zod.Add(Duel(attacker, vehicle, openLid, SnipeTime(attacker, vehicle, openLid)));
                }
            }
            finally { if (sw != null) sw.SetValue(null, saved); }
            double zodGrunt = ZodSnipeEstimate("grunt"), zodSniper = ZodSnipeEstimate("sniper");
            bool onTarget = ratios.All(x => x >= 0.5 && x <= 2.0), zodRate = onJeep >= 0.6 * zodGrunt;
            bool exactMode = getInt("oz_zod_exact", 0) == 1;
            string status = sw == null ? (zodRate ? DevNotInBuild + " (Zod rate)" : onTarget ? DevActive : DevActive + " (off target)")
                          : zodRate ? DevCheck + " (switch on, Zod rate)" : onTarget ? DevActive : DevActive + " (off target)";
            if (exactMode && !status.StartsWith(DevNotInBuild, StringComparison.Ordinal)) status += " (Zod exact mode selected)";
            return new Deviation
            {
                id = "DV2", change = "Driver snipe chance",
                zod = $"a hit that passes attack_snipe_chance (grunt / psycho 0.30, sniper 0.80, laser 0.60, jeep / gatling 0.40) goes into the driver pool of one full grunt ({Zod("grunt").hp} HP) (zobject.cpp:2187-2199, 334-359); tanks only while their lid is open. " +
                      $"Estimate on a jeep: grunt squad {Sec(zodGrunt)}, sniper squad {Sec(zodSniper)}; the jeep's hull dies first" +
                      (zod.Count > 0 ? ". Measured Zod exact: " + string.Join("; ", zod) : ""),
                openz = "driver hits raised so snipes happen in real fights (the project rules, DC2): a grunt squad empties a jeep in ~15 s, an open-lid medium / heavy tank in ~45 / 60 s, snipers fastest (driver-hit floors jeep 57, light 29, medium 19, heavy 15); open seats (a jeep, a crewed gatling, a light tank with its lid open), per attacker on target the HP of one driver hit and, where raised, the snipe chance: " + OpenSeatPairText() + "; every other pair keeps the floor and Zod's chance; Zod's who-can-snipe rules, lid rule (shut in 1 of 5 engagements), hit roll and the driver pool are kept" +
                        (sw == null ? " (switch not in this build)" : ""),
                measured = "OpenZ: " + string.Join("; ", openz) + $" (hull and squad unkillable, {SnipeTrials} seeds each, ~ = extrapolated from the driver damage dealt in {SnipeMaxT:0} s)",
                toggle = $"PlayerPrefs oz_zod_exact = {(exactMode ? "1 (Zod exact)" : "0 (OpenZ, default)")}; the parity rows run Zod exact; " + SimKnobs(new[] { "snipe", "driverhit" }, "no snipe knob in this build"),
                status = status,
            };
        }

        // ------------------------------------------------------------------ DV3 head start (extra starting vehicles on L02)
        static Deviation DevHeadStart(Func<string, int, int> getInt)
        {
            var w = ZWorld.Create(SettingsText, MapText("L02"), 1);
            int before = w.Objects.Count(o => o.Kind == ZKind.Vehicle && o.Owner == Team.Red);
            var made = w.AddHeadStart(Team.Red, ZGame.HeadStartTypes);
            bool crewed = made.All(v => v.Owner == Team.Red && v.Drivers.Count > 0 && v.Drivers[0] > 0);
            bool on = getInt(ZGame.HeadStartPref, 0) == 1;                                            // menu_exe_v1: default OFF
            bool ok = made.Count == ZGame.HeadStartTypes.Length && crewed;
            return new Deviation
            {
                id = "DV3", change = "Extra starting vehicles (head start, both maps)",
                zod = $"the L02 map gives neither side a vehicle ({before} red vehicles at start; 126/126 start objects = the Zod map files); L17 = the map's own start units",
                openz = $"a switch on both maps and every difficulty (level select HEAD START, off by default), human vs CPU only: L02 +{string.Join(", ", ZGame.HeadStartTypes)} , crewed, parked by the player's fort; L17 +{string.Join(", ", ZGame.EasyL17Types)}, parked EMPTY by the fort like the other L17 start vehicles (DV32: robots collect them; crewed only with DV32 off / Zod exact) (ZWorld.AddHeadStart); never in bot vs bot, bot matches or the parity suite",
                measured = $"L02 red vehicles {before} -> {before + made.Count} ({string.Join(", ", made.Select(v => v.Type))}), crewed {(crewed ? "yes" : "NO")}",
                toggle = $"PlayerPrefs {ZGame.HeadStartPref} = {(on ? "1 (on)" : "0 (off, default)")}; level select HEAD START (key H) on every difficulty; menu OpenZ/Deviations/Head Start On L02",
                status = !ok ? DevCheck : on ? DevActive : DevActive + " (off by pref)",
            };
        }

        static Deviation DevRunOver(Func<string, int, int> getInt)
        {
            bool on = getInt("oz_run_over", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            return new Deviation
            {
                id = "DV65", change = "Run-over: moving enemy vehicles knock down and squash robots (restores Z 1996)",
                zod = "the Zod port has no crush code (refs/zod_engine/src): vehicles and robots never touch (OpenZ spacing makes vehicles queue behind robots)",
                openz = $"a MOVING crewed vehicle whose hull touches an ENEMY robot knocks him down away from the impact (DV51 hold, {ZWorld.RunOverHurtFrac * 100:0} % of his max health, never below 1 HP; a glancing side hit throws him sideways and he gets up hurt, {ZWorld.RunOverGlanceFrac * 100:0} %); " +
                        $"if it drives on over him (centre under the inner hull, >= {ZWorld.RunOverSquashDelay:0.0} s after the hit) he is squashed: instant death, the crew gets the kill, the view flattens his own mesh (pancake, oil splat, springs, crush sound). Never the own team (no friendly fire); the CPU obeys the same rule (ZWorld.RunOverTick, ZPathing.PairExempt)",
                measured = "Play proof enemy squads driven through by a jeep and a tank are squashed, a friendly squad 0 hurt",
                toggle = $"ZWorld.RunOverDeviation (PlayerPrefs oz_run_over = {(getInt("oz_run_over", 1) == 1 ? "1 (on, default)" : "0 (off)")}); off in 'Zod exact' oz_zod_exact 1 and in every Zod row",
                status = on ? DevActive : DevActive + " (off by pref)",
            };
        }

        // ------------------------------------------------------------------ DV4 Easy CPU
        /// <summary>First order and first production change of a Blue CPU on the full L02 map (headless, no human orders).</summary>
        static (double order, double build) BotStart(ZBotDifficulty d, double secs)
        {
            var w = ZWorld.Create(SettingsText, MapText("L02"), 1);
            var bot = new ZBot(w, Team.Blue, ZBot.SeedFor(Team.Blue, 1)) { Difficulty = d };
            w.Bots.Add(bot);
            double order = -1, build = -1;
            while (w.Time < secs && (order < 0 || build < 0))
            {
                w.Tick(ZConst.TICK); w.Events.Clear();
                if (order < 0 && bot.Orders > 0) order = w.Time;
                if (build < 0 && bot.BuildChanges > 0) build = w.Time;
            }
            return (order, build);
        }

        static Deviation DevEasy(Func<string, int, int> getInt)
        {
            double horizon = ZBot.EASY_START_DELAY + 8;
            var (nOrder, nBuild) = BotStart(ZBotDifficulty.Normal, horizon);
            var (eOrder, eBuild) = BotStart(ZBotDifficulty.Easy, horizon);
            string F(double t) => t < 0 ? $"none by {horizon:0} s" : $"{t:F2} s";
            bool ok = nOrder >= 0 && nOrder < ZBot.EASY_START_DELAY && (eOrder < 0 || eOrder >= ZBot.EASY_START_DELAY - 1e-6) && (eBuild < 0 || eBuild >= ZBot.EASY_START_DELAY - 1e-6);
            int dv = getInt(OpenZDifficulty.Key, 0); bool easy = dv == 1;
            string dn = dv == 1 ? "1 (Easy)" : dv == 2 ? "2 (Medium)" : "0 (Hard: Zod CPU and build lists + DV73, default)";
            // bug #15: unlocks per difficulty, measured on the L02 red fort (blevel 0)
            var uw = ZWorld.Create(SettingsText, MapText("L02"), 1);
            var fort = uw.Objects.First(o => o.Kind == ZKind.Building && o.Owner == Team.Red && ZBuildLists.IsProducer(o.Type) && o.Type.StartsWith("fort"));
            string U(GameDifficulty g) { uw.TechBonus[(int)Team.Red] = OpenZDifficulty.TechBonus(g); return $"{OpenZDifficulty.Label(g)} L{fort.BuildLevel} ({ZBuildLists.For(fort.Type, fort.BuildLevel).Count} types)"; }
            string unlocks = $"{U(GameDifficulty.Hard)}, {U(GameDifficulty.Medium)}, {U(GameDifficulty.Easy)}";
            ok &= fort.Level == 0 && OpenZDifficulty.TechBonus(GameDifficulty.Hard) == 0;
            return new Deviation
            {
                id = "DV4", change = "Difficulty Easy / Medium / Hard (Hard = Zod's CPU, build lists and start units + DV73)",
                zod = "one CPU: the Zod bot (zbot.cpp); OpenZ's Normal is that bot 1:1 (same order hash)",
                openz = $"HARD = the Zod CPU, Zod build lists and the map's start units, plus one missile cannon on each fort (DV73); see DV71 / DV72 for the CPU's Lasers and its Electrobot. MEDIUM = Zod CPU, the human's factories/fort read their build list 2 levels up, L02 head start (toggle). EASY = every unit unlocked for the human (build list level 5), L02 head start always, and the Easy CPU: waits {ZBot.EASY_START_DELAY:0} s before its first order or production change, thinks every {ZBot.AI_INTERVAL * ZBot.EASY_AI_INTERVAL_SCALE:0.#} s, order delays x{ZBot.EASY_ORDER_DELAY_SCALE}, at most {ZBot.EASY_MAX_WAVE} units per wave and {ZBot.EASY_MAX_ATTACKERS} on hostile orders; human vs CPU only (bot vs bot is always Normal)",
                measured = $"L02 Blue CPU, first order / first production change: Zod {F(nOrder)} / {F(nBuild)}; Easy {F(eOrder)} / {F(eBuild)}. Red fort build list: {unlocks}; build times unchanged",
                toggle = $"PlayerPrefs {OpenZDifficulty.Key} = {dn}; menu OpenZ/Difficulty; main menu DIFFICULTY [EASY | MEDIUM | HARD] (options + level select, key C)",
                status = !ok ? DevCheck : DevActive + $" ({OpenZDifficulty.Label((GameDifficulty)(dv == 1 ? 1 : dv == 2 ? 2 : 0))} selected)",
            };
        }

        // ------------------------------------------------------------------ DV5 balance tweaks
        static Deviation DevBalance(Func<string, int, int> getInt)
        {
            var w = ZWorld.Create(SettingsText, MapText("L02"), 1);
            var diffs = new List<string>();
            foreach (var z in ZodUnits)
            {
                bool cfRocket = ZWorld.RocketSpeedDeviation; ZWorld.RocketSpeedDeviation = false;
                ZObject o; try { o = w.CreateObject(z.cat, z.type, 40 * 16, 40 * 16, Team.Red); } finally { ZWorld.RocketSpeedDeviation = cfRocket; }
                bool armed = z.type != "apc" && z.type != "crane";
                void Cmp(string field, double zod, double live) { if (Math.Abs(zod - live) > 1e-6) diffs.Add($"{z.type}.{field} {zod.ToString(CultureInfo.InvariantCulture)} -> {live.ToString(CultureInfo.InvariantCulture)}"); }
                Cmp("move", z.move, o.MoveSpeed); Cmp("hp", z.hp, o.MaxHealth);
                if (armed)
                {
                    Cmp("range", z.radius, o.AttackRadius); Cmp("interval", z.interval, o.AttackSpeed); Cmp("damage", z.dmg, o.Damage);
                    Cmp("hit", z.chance, o.DamageChance); Cmp("snipe", z.snipe, o.SnipeChance); Cmp("missile", z.missile, o.MissileSpeed); Cmp("splash", z.splash, o.DamageRadius);
                }
                if (ZodGroupSize.TryGetValue(z.type, out var g) && w.S.Unit(z.type).group_amount != g) diffs.Add($"{z.type}.group {g} -> {w.S.Unit(z.type).group_amount}");
            }
            string knobs = SimKnobs(new[] { "balance" }, null, typeNameFilter: "Balance");
            bool any = diffs.Count > 0 || knobs != null;
            return new Deviation
            {
                id = "DV5", change = "Balance tweaks (bot vs bot about 50/50 on both maps, DC3)",
                zod = "default_settings.txt + zbot.cpp as shipped (17 units: the literals in OpenZParity.Zod.cs)",
                openz = any ? "listed here as found in the live code: unit stats that differ from Zod, and the public knobs of OpenZ.Sim *Balance* types"
                            : "none yet: the fix is the smallest listed tweak after 8+8 seeded bot matches per map (bot tuning first)",
                measured = diffs.Count > 0 ? "unit stats that differ from Zod: " + string.Join(", ", diffs) : "all 17 units = Zod literals (move, range, interval, damage, hit, snipe, HP, missile, splash, squad size)",
                toggle = knobs ?? "-",
                status = any ? DevActive : DevNotInBuild,
            };
        }

        static Deviation DevFortGun(Func<string, int, int> getInt)
        {
            bool saved = ZWorld.FortGunDeviation; ZWorld.FortGunDeviation = true;
            var w = ZWorld.Create(SettingsText, MapText("L02"), 1);
            var fort = w.Objects.First(o => o.IsFort && o.Owner == Team.Blue);           // no turret cannon: only the fort gun can fire
            var u = w.S.Unit("howitzer");
            // a red grunt ~160 px west of the fort centre (inside the 200 px howitzer radius, 82 px from the west wall), standing still
            var g = w.CreateObject("robot", "grunt", fort.CenterX - 170, fort.CenterY - 8, Team.Red);
            int hp0 = g.Health, shots = 0; double first = -1;
            while (w.Time < 12 && !g.Destroyed)
            {
                w.Tick(ZConst.TICK);
                foreach (var e in w.Events) if (e.type == ZEventType.MissileFired && e.objId == fort.Id) { shots++; if (first < 0) first = w.Time; }
                w.Events.Clear();
            }
            // fortgun_v2 envelope: a grunt hugging the east wall (inside the 64 px min range) and one behind the fort (rear arc)
            // stand 12 s in the howitzer radius, the other blue units neutral so nothing else shoots; a violation = a fort shot
            // whose target stood outside the envelope at that moment (0 required), shots = all fort shots at the probe
            int probeShots = 0, violations = 0; string probeLog = "";
            foreach (var (px, py, label) in new[] { (fort.LocX + fort.WidthPix + 24, fort.CenterY - 8, "32 px off the east wall"), (fort.CenterX - 8, fort.LocY - 90, "behind the fort (rear arc)") })
            {
                var w2 = ZWorld.Create(SettingsText, MapText("L02"), 1);
                var f2 = w2.Objects.First(o => o.IsFort && o.Owner == Team.Blue);
                foreach (var o in w2.Objects.Where(o => o.IsUnit && o.Owner == Team.Blue).ToList()) { o.Owner = Team.Neutral; o.Drivers.Clear(); }
                var g2 = w2.CreateObject("robot", "grunt", px, py, Team.Red);
                int n2 = 0, v2 = f2.FortGunCanReach(g2.CenterX, g2.CenterY) ? 1000 : 0;                   // the probe itself must start outside
                while (w2.Time < 12 && !g2.Destroyed)
                {
                    w2.Tick(ZConst.TICK);
                    foreach (var e in w2.Events)
                        if (e.type == ZEventType.MissileFired && e.objId == f2.Id)
                        {
                            n2++; var t2 = w2.GetAny(e.otherId);
                            if (t2 != null && !f2.FortGunCanReach(t2.CenterX, t2.CenterY)) v2++;
                        }
                    w2.Events.Clear();
                }
                probeShots += n2; violations += v2;
                probeLog += $"{label}: {(v2 >= 1000 ? "PROBE STARTS INSIDE THE ENVELOPE" : $"{n2} shot(s), {v2} outside the envelope")}; ";
            }
            ZWorld.FortGunDeviation = saved;
            bool on = getInt("oz_fortgun_sim", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            bool ok = shots >= 1 && first > 0 && (g.Destroyed || g.Health < hp0) && violations == 0;
            return new Deviation
            {
                id = "DV7", change = "Fort gun (the big gun on the fort roof is a real weapon)",
                zod = "forts have no weapon of their own (bfort.cpp); their guns are the cannons on the 4 turret spots",
                openz = $"each owned fort fires the Zod HOWITZER's shell from its centre: radius {ZObject.FortGunRange(u)} px (howitzer {u.attack_radius} + {ZObject.FortGunRangeBonusPx}: our own addition), damage {u.Damage}, splash {u.attack_damage_radius + ZObject.FortGunSplashBonusPx} px (howitzer {u.attack_damage_radius} + {ZObject.FortGunSplashBonusPx}), shell {u.attack_missile_speed} px/s, every {u.attack_speed:0.##} s; nearest enemy unit, {ZObject.FortGunLay} s lay on a new target; committed {ZObject.FortGunCommitLead:0.#} s before each shot (no aim-then-abort): it then fires at that target, or at its last spot inside the envelope when it died or left; min range {ZObject.FortGunMinEdgePx} px from the fort's walls (never shells units hugging the fort or its gate: 16 px scatter + 40 px splash stay off the walls), no fire into the rear arc +-{ZObject.FortGunRearDeg} deg (the fort flag stands behind the turret); no crew (no snipe / capture), not targetable, stops only when the fort dies",
                measured = $"L02 blue fort (no turret cannon) vs a red grunt 162 px away: first shot at {(first < 0 ? "none" : first.ToString("F2") + " s")}, {shots} shot(s) in 12 s, grunt {(g.Destroyed ? "killed" : $"hp {g.Health}/{hp0}")}; " +
                           $"envelope probes, 12 s each, only the fort armed: {probeLog}(0 outside required)",
                toggle = $"PlayerPrefs oz_fortgun_sim = {(on ? "1 (on, default)" : "0 (off)")}; off in 'Zod exact' (oz_zod_exact = 1) and in this parity report's measurements",
                status = !ok ? DevCheck : on ? DevActive : DevActive + " (off by pref)",
            };
        }

        static Deviation DevOrders() => new Deviation
        {
            id = "DV16", change = "A human player's direct order (enter, attack, grenade pickup, repair, fort entry) always wins over automatic behaviour",
            zod = "attack-to diversions hijack enter / pickup orders (zobject.cpp:3285-3339 via :2561/:2645), missile dodges interrupt any order (:3657-3714), " +
                  "a path that ends next to the target silently drops the order (:2516/:2633)",
            openz = "enter / pickup / repair / fort orders from the player are never attack-moves (PlayerInput.BuildWaypoint) and aim at the object's centre; no dodge " +
                    "during them; a new order drops the old target at once and clears the pathing layer's pending actions; stuck / watchdog re-path instead of " +
                    "dropping; a minion that gave up rejoins the squad's order; the leader never queues behind its own minion; a path ending short walks on to the centre; " +
                    "no teammate auto-grabs a vehicle another squad was ordered into; an enemy squad member is a valid click target. CPU orders keep Zod's rules",
            measured = "Play: OpenZ.OrderCheck (work/records/order_check.md): enter / attack / pickup under fire + EXIT CREW by real mouse click",
            toggle = $"ZWorld.OrderPriorityDeviation = {(ZWorld.OrderPriorityDeviation ? "true (default)" : "false")} (code switch)",
            status = ZWorld.OrderPriorityDeviation ? DevActive : DevCheck,
        };

        static Deviation DevQueue() => new Deviation
        {
            id = "DV21", change = "Production queue is first in, first out (up to 5) with a REPEAT toggle per factory / fort",
            zod = "a queued unit goes to the FRONT (built next, LIFO) and the last entry repeats forever (zbuilding.cpp:146-168, 477-506)",
            openz = "click a unit tile while building = add it to the END of the queue; right-click a tile = remove its last queued copy; click a queue chip = remove it; " +
                    "REPEAT on (default, Zod's auto-production) keeps building the last unit when the queue is empty, REPEAT off = the factory goes idle; " +
                    "a captured factory starts with REPEAT on. The CPU only starts production (never queues), so its behaviour is unchanged",
            measured = "Rules suite row 'DV21 FIFO' (queue jeep,crane,gatling; output grunt,jeep,crane,gatling x3; idle after REPEAT off) + Play proof with 4 queued units",
            toggle = $"ZWorld.FifoQueueDeviation = {(ZWorld.FifoQueueDeviation ? "true (default)" : "false")} (code switch)",
            status = ZWorld.FifoQueueDeviation ? DevActive : DevCheck,
        };

        // ------------------------------------------------------------------ DV10 the game stops after the win (endgame_ui_v1)
        static Deviation DevEndFreeze(Func<string, int, int> getInt)
        {
            bool saved = ZWorld.EndFreezeDeviation;
            var on = EndRun(true); var zod = EndRun(false);
            ZWorld.EndFreezeDeviation = saved;
            bool ok = on.overAt >= 0 && on.built == 0 && on.producing == 0 && on.moving == 0 && zod.moving > 0;
            return new Deviation
            {
                id = "DV10", change = "The game stops after the win (production, CPU and player orders; the winners stand and cheer)",
                zod = "ProcessEndGame (zserver.cpp:277-300) only sets game_on = false (the end check); objects, factories and bots keep running until the map resets 10 s later",
                openz = "at the game over every factory / fort stops (queue cleared), the CPU bots stop, every unit halts and disengages (no idle auto-grab of vehicles, flags, crates), no production can start, PlayerInput takes no orders; routine and combat voice lines stay muted (OpenZVictory.MutesVoice); post-game only",
                measured = on.overAt < 0 ? "no game over reached (CHECK)"
                    : $"L02, red units marching on the blue fort, the blue fort killed at 3 s: game over at {on.overAt:F2} s; 2 s later {on.moving} red unit(s) moving (Zod rule: {zod.moving}); "
                      + $"in the 90 s after it {on.built} unit(s) built (Zod rule: {zod.built}), {on.producing} building(s) still producing (Zod rule: {zod.producing})",
                toggle = $"ZWorld.EndFreezeDeviation = {(saved ? "true (default)" : "false")} (code switch; nothing before the game over depends on it)",
                status = ok ? DevActive : DevCheck,
            };
        }

        /// <summary>L02 with a CPU bot on blue, red human: the first sim time a red / blue unit takes an automatic capture waypoint
        /// (a flag, an empty vehicle / cannon, a crate), with the hold and Zod exact (the same seed).</summary>
        static Deviation DevStartHold(Func<string, int, int> getInt)
        {
            bool saved = ZWorld.CalmStartDeviation;
            var on = StartHoldRun(true); var zod = StartHoldRun(false);
            ZWorld.CalmStartDeviation = saved;
            bool ok = on.red < 0 || on.red >= ZWorld.CalmStartSec;
            string T(double t) => t < 0 ? "none in 40 s" : $"{t:F1} s";
            return new Deviation
            {
                id = "DV69", change = "Calm start: the player's units start nothing by themselves for the first 20 s of play (orders work at once; the CPU is not held)",
                zod = "idle units walk to flags, empty vehicles and crates in range from the first passive check (zobject.cpp CheckPassiveEngage)",
                openz = $"on a human team, until {ZWorld.CalmStartSec:F0} s of sim time (the sim starts when the briefing hands over the camera), no idle unit takes a flag / vehicle / cannon / crate waypoint or walks off after an enemy (idle agro, squad help calls); units in range still shoot from where they stand; factory units made meanwhile wait too; explicit orders are not held; CPU teams unchanged",
                measured = $"L02, CPU blue: first red auto-capture {T(on.red)} (Zod rule: {T(zod.red)}), first blue {T(on.blue)} (Zod rule: {T(zod.blue)})",
                toggle = $"ZWorld.CalmStartDeviation = {(saved ? "true (default)" : "false")} (code switch)",
                status = ok ? DevActive : DevCheck,
            };
        }

        // ------------------------------------------------------------------ DV71 the CPU builds Lasers sometimes
        static Deviation DevBotLasers(Func<string, int, int> getInt)
        {
            bool was = ZWorld.BotLaserDeviation; string seq = "-"; bool ok = false;
            try
            {
                ZWorld.BotLaserDeviation = true;
                var w = ZWorld.Create(SettingsText, MapText("L17"), 1);
                var bot = new ZBot(w, Team.Blue, 108);
                var fac = w.Objects.First(o => o.Type == "robot_factory" && o.Level == 4);
                var fort = w.Objects.First(o => o.IsFort && o.Owner == Team.Blue);
                var a = new List<string>(); var f = new List<string>();
                for (int i = 0; i < 8; i++) { fac.BuiltCount = i; a.Add(bot.LaserPick(fac, "pyro")); fort.BuiltCount = i; f.Add(bot.LaserPick(fort, "medium")); }
                seq = $"L17 level {fac.Level + 1} robot factory, units 1-8: {string.Join(" ", a)}; level {fort.Level + 1} fort, units 1-8: {string.Join(" ", f)}";
                ok = a.Count(x => x == "laser") == 3 && f.Count(x => x == "laser") == 6 && a[0] == "pyro" && f[0] == "laser" && f[2] == "medium";
            }
            finally { ZWorld.BotLaserDeviation = was; }
            return new Deviation
            {
                id = "DV71", change = "The CPU builds Lasers sometimes",
                zod = "the bot builds the first of medium, light, jeep, pyro, tough, sniper, tough, psycho its producer can make (zbot.cpp:20-27, :1372-1432): never a Laser",
                openz = $"a CPU producer that can make Lasers builds a Laser squad in place of Zod's pick: a robot factory its 2nd unit and then every {ZBot.LASER_EVERY}rd, a fort the first {ZBot.LASER_RUN_FORT} of every {ZBot.LASER_EVERY_FORT} units (its Electrobots not counted); a fort claimed for the Electrobot (DV46) finishes the unit it is building first; a counter per building, no random draw",
                measured = seq,
                toggle = "ZWorld.BotLaserDeviation (code switch; ZGame: on, off in Zod exact; headless default off)",
                status = ok ? DevActive : DevCheck,
            };
        }

        // ------------------------------------------------------------------ DV72 the CPU's Electrobot leaves with another unit
        static Deviation DevElectrobotEscort(Func<string, int, int> getInt)
        {
            bool e0 = ZWorld.ElectrobotDeviation, b0 = ZWorld.ElectrobotBotDeviation, s0 = ZWorld.ElectrobotEscortDeviation;
            int escorted = 0, alone = 0; string line = "no Electrobot order within 60 s";
            try
            {
                ZWorld.ElectrobotDeviation = true; ZWorld.ElectrobotBotDeviation = true; ZWorld.ElectrobotEscortDeviation = true;
                var w = ZWorld.Create(SettingsText, MapText("L02"), 1);
                var bot = new ZBot(w, Team.Blue, 108); w.Bots.Add(bot);
                var fort = w.Objects.First(o => o.IsFort && o.Owner == Team.Blue);
                fort.GetCreationMovePoint(out int ex, out int ey);
                w.CreateRobotGroup(ZElectro.Type, ex - 8, ey - 8, Team.Blue, 1);
                while (w.Time < 60 && bot.ElectrobotEscorted + bot.ElectrobotAlone == 0) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                escorted = bot.ElectrobotEscorted; alone = bot.ElectrobotAlone;
                line = bot.Log.LastOrDefault(l => l.Contains("DV72")) ?? line;
            }
            finally { ZWorld.ElectrobotDeviation = e0; ZWorld.ElectrobotBotDeviation = b0; ZWorld.ElectrobotEscortDeviation = s0; }
            return new Deviation
            {
                id = "DV72", change = "The CPU's Electrobot leaves with another unit of his team",
                zod = "no Electrobot in Z; the bot pairs every idle unit with its own nearest target, one unit per target (zbot.cpp:222-414), so a squad of one walks alone",
                openz = $"Normal CPU: the idle Electrobot is left out of the wave and takes the target (flag, fort, attack) of the nearest unit ordered in the same round within {ZBot.ELB_JOIN_PX} px; else the nearest idle armed unit that near gets its order now and he goes with it; alone only after {ZBot.ELB_WAIT:F0} s without company",
                measured = $"L02, CPU blue, an Electrobot at the blue fort door: escorted {escorted}, alone {alone}; {line}",
                toggle = "ZWorld.ElectrobotEscortDeviation (code switch; ZGame: on with the CPU Electrobot, off in Zod exact; headless default off)",
                status = escorted == 1 && alone == 0 ? DevActive : DevCheck,
            };
        }

        // ------------------------------------------------------------------ DV73 Hard: a missile cannon on each fort's door tower
        static Deviation DevFortDoorGun(Func<string, int, int> getInt)
        {
            bool was = ZWorld.FortDoorGunDeviation; var got = new List<string>(); bool ok = true;
            try
            {
                foreach (var lv in new[] { "L02", "L17" })
                {
                    ZWorld.FortDoorGunDeviation = false;
                    int before = ZWorld.Create(SettingsText, MapText(lv), 1).Objects.Count(o => o.Kind == ZKind.Cannon);
                    ZWorld.FortDoorGunDeviation = true;
                    var w = ZWorld.Create(SettingsText, MapText(lv), 1);
                    int after = w.Objects.Count(o => o.Kind == ZKind.Cannon);
                    var guns = w.FortDoorGuns.Select(c => $"{c.Owner} {c.Type} at {c.LocX / 16},{c.LocY / 16}{(c.Ejectable ? " EJECTABLE" : "")} crew {c.Drivers.Count}");
                    got.Add($"{lv}: cannons {before} -> {after}; {string.Join(", ", guns)}");
                    ok &= w.FortDoorGuns.Count == 2 && w.FortDoorGuns.All(c => c.Type == ZWorld.FortDoorGunType && !c.Ejectable && c.Drivers.Count == 1 && c.Owner != Team.Neutral);
                }
            }
            finally { ZWorld.FortDoorGunDeviation = was; }
            return new Deviation
            {
                id = "DV73", change = "Hard: every fort starts with a missile cannon on the east tower of its door row",
                zod = "a fort's tower guns are the map's own cannons on its 4 turret spots (bfort.cpp:534-548); no difficulty levels",
                openz = "HARD, human vs CPU: one missile cannon (Zod's unit, not ejectable) on the east tower of the door row of each owned fort (the front, where its units come out; the right tower in the game camera); a map cannon on that spot is created as the missile cannon instead. Easy, Medium, bot matches: the map's cannons",
                measured = string.Join(" | ", got),
                toggle = "ZWorld.FortDoorGunDeviation (code switch; ZGame: on for Hard in human vs CPU, off in Zod exact; headless default off)",
                status = ok ? DevActive : DevCheck,
            };
        }

        // ------------------------------------------------------------------ DV74 a crane rebuilds a destroyed building or bridge in any zone
        /// <summary>L02 without the map's units (as the rules rows): a vehicle factory's zone is given to blue, the factory and a bridge are destroyed;
        /// the rule is read with the switch off and on for both teams, then a red crane gets the order on the blue ruin and rebuilds it
        /// (the owner stays blue).</summary>
        static Deviation DevCraneAnyRuin(Func<string, int, int> getInt)
        {
            bool was = ZWorld.CraneAnyRuinDeviation, ef = ZWorld.EndFreezeDeviation, fg = ZWorld.FortGunDeviation;
            string got = "-"; bool ok = false;
            try
            {
                ZWorld.EndFreezeDeviation = false; ZWorld.FortGunDeviation = false;              // a world without units: no end freeze, no fort gun on the crane
                var w = RulesWorld("L02", 9);
                var b = B(w, "vehicle_factory", 22, 61);                                         // a flag zone without a fort (a ruin beside a destroyed fort is never rebuilt)
                var home = w.Objects.First(o => o.IsFort && o.Owner == Team.Blue);
                home.GetCreationMovePoint(out int hx, out int hy);
                w.CreateRobotGroup("grunt", hx - 8, hy - 8, Team.Blue, 1);                         // blue keeps one unit at its fort: a team without units loses its fort and zones at once
                w.AwardZone(w.Flags.First(f => f.ZoneIndex == b.ZoneIndex), Team.Blue);
                var bridge = w.Objects.First(o => o.IsBridge);
                var fort = RulesWorld("L02", 9).Objects.First(o => o.IsFort && o.Owner == Team.Blue);
                ZWorld.CraneAnyRuinDeviation = true;
                b.SetHealth(b.MaxHealth / 2); bool damaged = b.CanBeRepairedByCrane(Team.Red);   // mending a standing building (DV61) stays own / neutral
                Kill(w, b); Kill(w, bridge); fort.SetHealth(0);
                bool ruin = b.CanBeRepairedByCrane(Team.Red), own = b.CanBeRepairedByCrane(Team.Blue), br = bridge.CanBeRepairedByCrane(Team.Red) && bridge.CanBeRepairedByCrane(Team.Blue), fortRuin = fort.CanBeRepairedByCrane(Team.Red) || fort.CanBeRepairedByCrane(Team.Blue);
                ZWorld.CraneAnyRuinDeviation = false;
                bool zodRuin = b.CanBeRepairedByCrane(Team.Red), zodOwn = b.CanBeRepairedByCrane(Team.Blue), zodBr = bridge.CanBeRepairedByCrane(Team.Red);
                b.GetCraneEntrance(out int ex, out int ey, out _, out _);
                var crane = w.CreateObject("vehicle", "crane", ex - 16, ey + 32, Team.Red);
                var order = new[] { new ZWaypoint { mode = WpMode.CraneRepair, refId = b.Id, x = b.CenterX, y = b.CenterY } };
                bool zodOrder = w.SendWaypoints(Team.Red, crane, order) && crane.Waypoints.Count == 1;
                ZWorld.CraneAnyRuinDeviation = true;
                bool sent = w.SendWaypoints(Team.Red, crane, order) && crane.Waypoints.Count == 1;
                double t0 = w.Time, tr = -1;
                Until(w, 90, e => { if (e.type == ZEventType.Revived && e.objId == b.Id) { tr = w.Time; return true; } return false; });
                got = $"L02, a {b.Type} in a zone blue holds, destroyed: red crane may rebuild it {ruin} (Zod rule {zodRuin}), blue crane {own} (Zod {zodOwn}); a destroyed bridge {br} (Zod {zodBr}); a destroyed fort {fortRuin}; the same building only damaged, red crane {damaged}; " +
                      $"red crane order accepted {sent} (Zod rule {zodOrder}), " + (tr < 0 ? "not rebuilt in 90 s" : $"rebuilt after {tr - t0:F1} s, {b.Health}/{b.MaxHealth}, owner {b.Owner}");
                ok = ruin && own && br && !fortRuin && !damaged && !zodRuin && zodOwn && !zodOrder && sent && tr > 0 && b.Health == b.MaxHealth && b.Owner == Team.Blue;
            }
            finally { ZWorld.CraneAnyRuinDeviation = was; ZWorld.EndFreezeDeviation = ef; ZWorld.FortGunDeviation = fg; }
            return new Deviation
            {
                id = "DV74", change = "A crane rebuilds any destroyed building or bridge, whoever owns the zone",
                zod = "a crane may rebuild a destroyed building only when its owner is neutral or the crane's team (zobject.cpp:4494-4504; cursor zplayer.cpp:2219); a fort never",
                openz = "every destroyed building that is not a fort and every destroyed bridge is a crane target for every team (repair cursor, order); the rebuilt building belongs to its zone's owner as before; forts are never rebuilt; mending a damaged standing building stays own / neutral. The CPU's cranes follow the same rule but only pick bridges and ruins of an own or neutral zone",
                measured = got,
                toggle = "ZWorld.CraneAnyRuinDeviation (code switch; ZGame: on, off in Zod exact; headless default off)",
                status = ok ? DevActive : DevCheck,
            };
        }

        // ------------------------------------------------------------------ DV77 a vehicle fires out of its repair bay on the way in and out too
        /// <summary>L17 without the map's units: blue's repair station, a blue medium tank at 30 % health sent to it, a red grunt squad on
        /// hold (health x300) north of the pad, behind the station's own wall. Returns the tank's shots while it drives in / stands on the
        /// pad / drives off, its health at the end (per mille) and the time on the pad (tenths of a second, -1 = never left it).</summary>
        static int[] RepairBayFireProbe()
        {
            var w = RulesWorld("L17", 9);
            var st = w.Objects.First(o => o.Type == "repair" && o.Owner == Team.Blue && !o.Destroyed);
            st.GetRepairEntrance(out int ex, out int ey); st.GetRepairCenter(out int cx, out int cy);
            var tank = w.CreateObject("vehicle", "medium", ex - 16, ey + 48, Team.Blue);
            tank.SetHealth(tank.MaxHealth * 30 / 100);
            var squad = w.CreateRobotGroup("grunt", cx - 8, cy - RepairBayProbePx - 8, Team.Red);
            w.SendHold(Team.Red, squad);
            foreach (var m in new[] { squad }.Concat(squad.Minions)) { m.MaxHealth *= 300; m.SetHealth(m.MaxHealth); }
            w.SendWaypoints(Team.Blue, tank, new[] { new ZWaypoint { mode = WpMode.UnitRepair, refId = st.Id, x = st.CenterX - 8, y = st.CenterY } });
            int driveIn = 0, onPad = 0, driveOff = 0; double padIn = -1, padOut = -1;
            Until(w, 60, e =>
            {
                if ((e.type != ZEventType.MissileFired && e.type != ZEventType.Hitscan) || e.objId != tank.Id) return false;
                if (tank.OnRepairPad(st.Id)) onPad++; else if (tank.LeavingRepairPad(st.Id)) driveOff++; else if (tank.InRepairBay(st.Id)) driveIn++;
                return false;
            }, () =>
            {
                if (padIn < 0 && tank.OnRepairPad(st.Id)) padIn = w.Time;
                if (padIn >= 0 && padOut < 0 && !tank.OnRepairPad(st.Id)) padOut = w.Time;
                return padOut >= 0 && !tank.InRepairBay(st.Id);
            });
            return new[] { driveIn, onPad, driveOff, tank.MaxHealth > 0 ? (int)(tank.Health * 1000L / tank.MaxHealth) : 0, padIn < 0 || padOut < 0 ? -1 : (int)Math.Round((padOut - padIn) * 10) };
        }
        const int RepairBayProbePx = 89;

        static Deviation DevRepairBayFire(Func<string, int, int> getInt)
        {
            bool was = ZWorld.RepairBayFireDeviation, pad = ZWorld.RepairInPlaceDeviation, los = ZWorld.LosDeviation, ef = ZWorld.EndFreezeDeviation, fg = ZWorld.FortGunDeviation;
            string got = "-"; bool ok = false;
            try
            {
                ZWorld.RepairInPlaceDeviation = true; ZWorld.LosDeviation = true; ZWorld.EndFreezeDeviation = false; ZWorld.FortGunDeviation = false;
                ZWorld.RepairBayFireDeviation = false; var off = RepairBayFireProbe();
                ZWorld.RepairBayFireDeviation = true; var on = RepairBayFireProbe();
                got = $"L17, blue's repair station, a blue medium tank at 30 % health sent to it, a red grunt squad on hold {RepairBayProbePx} px north of the pad behind the station's wall: " +
                      $"the tank's shots while it drives in {on[0]} (without the switch {off[0]}), on the pad {on[1]} ({off[1]}), driving off {on[2]} ({off[2]}); " +
                      $"time on the pad {(on[4] < 0 ? "never left it" : (on[4] / 10.0).ToString("F1") + " s")} ({(off[4] < 0 ? "never left it" : (off[4] / 10.0).ToString("F1") + " s")}; Zod's repair time {ZWorld.RepairPadSeconds:F0} s, hits taken on the pad add to it), health when it is out again {on[3] / 10.0:F1} % ({off[3] / 10.0:F1} %)";
                ok = on[0] > 0 && off[0] == 0 && on[1] > 0 && off[1] > 0 && on[4] > 0 && off[4] > 0 && on[3] > 900;
            }
            finally { ZWorld.RepairBayFireDeviation = was; ZWorld.RepairInPlaceDeviation = pad; ZWorld.LosDeviation = los; ZWorld.EndFreezeDeviation = ef; ZWorld.FortGunDeviation = fg; }
            return new Deviation
            {
                id = "DV77", change = "A vehicle fires out of its repair bay on its whole way in and out",
                zod = "no line of sight rule: a vehicle on its way into the repair station fires at anything in range; while it is repaired it is not in the world (deleted, a fresh copy 5 s later, brepair.cpp:487-563)",
                openz = "a direct shot needs a clear line (DV23) and the station's own walls stood in it; DV62 left the station out of that line only while the vehicle stood on the pad or drove off it. Now the station is left out from the moment the vehicle drives in until it is out again (drive in, on the pad, drive off): it picks targets in range and fires the whole time, exactly as it would standing outside. The repair itself, its 5 s and the heal ticks are untouched (ZSight.ExemptOf)",
                measured = got,
                toggle = "ZWorld.RepairBayFireDeviation (code switch; ZGame: on, off in Zod exact; headless default off; needs DV62 and DV23)",
                status = ok ? DevActive : DevCheck,
            };
        }

        // ------------------------------------------------------------------ DV78 rifle pairs: a grunt's rifle on a grunt
        /// <summary>L02 open ground, no other unit: a red and a blue grunt squad without grenades stand RiflePairProbePx apart and engage by
        /// themselves. Returns the seconds until one squad is dead (the mean of RiflePairFights seeds; a fight that is not over at
        /// RiflePairProbeMaxT counts as that long), the hits landed per fight and the fights that were not over.</summary>
        static double[] RiflePairProbe(string other)
        {
            double sum = 0; long hits = 0; int open = 0;
            for (int k = 0; k < RiflePairFights; k++)
            {
                var w = Legacy(60 + k);
                var red = w.CreateRobotGroup("grunt", 56 * 16, 14 * 16, Team.Red);
                var blue = w.CreateRobotGroup(other, 56 * 16 + RiflePairProbePx, 14 * 16, Team.Blue);
                var rs = new[] { red }.Concat(red.Minions).ToList(); var bs = new[] { blue }.Concat(blue.Minions).ToList();
                double t0 = w.Time;
                while (w.Time - t0 < RiflePairProbeMaxT && rs.Any(m => !m.Destroyed) && bs.Any(m => !m.Destroyed))
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events) if (e.type == ZEventType.Hitscan && e.flag) hits++;
                    w.Events.Clear();
                }
                if (rs.Any(m => !m.Destroyed) && bs.Any(m => !m.Destroyed)) open++;
                sum += w.Time - t0;
            }
            return new[] { sum / RiflePairFights, (double)hits / RiflePairFights, open };
        }
        const int RiflePairFights = 6, RiflePairProbePx = 80;
        const double RiflePairProbeMaxT = 300;

        static Deviation DevRiflePair(Func<string, int, int> getInt)
        {
            bool was = ZWorld.RiflePairDeviation; string got = "-"; bool ok = false;
            int pair = ZWorld.RiflePair.TryGetValue(("grunt", "grunt"), out int pv) ? pv : 0, zodDmg = Zod("grunt").dmg, hp = Zod("grunt").hp;
            try
            {
                ZWorld.RiflePairDeviation = false; var off = RiflePairProbe("grunt"); var offPsycho = RiflePairProbe("psycho");
                ZWorld.RiflePairDeviation = true; var on = RiflePairProbe("grunt"); var onPsycho = RiflePairProbe("psycho");
                got = $"L02 open ground, two fresh grunt squads without grenades standing {RiflePairProbePx} px apart, {RiflePairFights} seeds: one squad dead after {on[0]:F1} s with {on[1]:F0} hits landed (Zod rule {off[0]:F1} s, {off[1]:F0} hits" +
                      (off[2] > 0 ? $", {off[2]:0} not over at {RiflePairProbeMaxT:0} s" : "") + $"); control, a grunt squad against a psycho squad: {onPsycho[0]:F2} s, {onPsycho[1]:F0} hits (Zod rule {offPsycho[0]:F2} s, {offPsycho[1]:F0} hits)";
                ok = pair > zodDmg && on[2] == 0 && on[0] >= 6 && on[0] <= 14 && off[0] > 4 * on[0] && Math.Abs(onPsycho[0] - offPsycho[0]) < 1e-9 && Math.Abs(onPsycho[1] - offPsycho[1]) < 1e-9;
            }
            finally { ZWorld.RiflePairDeviation = was; }
            return new Deviation
            {
                id = "DV78", change = "Rifle pairs: a grunt's rifle hit on a grunt removes more, so a grunt mirror fight ends",
                zod = $"a bullet that hits removes the attacker's attack_damage whatever the target is (zobject.cpp:2187-2199): a grunt's rifle takes {zodDmg} of a grunt's {hp} HP ({Math.Ceiling((double)hp / zodDmg):0} hits), so two grunt squads without grenades fire at each other for about two minutes",
                openz = $"for the pairs of ZWorld.RiflePair a robot's bullet that hits a robot removes the pair's own value: grunt on grunt {pair} HP ({Math.Ceiling((double)hp / Math.Max(1, pair)):0} hits fell a grunt). Only that hit's damage changes: Zod's hit roll, cooldown, range and target choice are kept; grenades are thrown as before while a squad has any; a grunt's rifle on every other robot, vehicle, cannon and driver, every other weapon on a grunt, and the fort's roof defender (DV41, both ways) keep Zod's damage (Sim/ZObject.RiflePair.cs)",
                measured = got,
                toggle = "ZWorld.RiflePairDeviation (code switch; ZGame: on, off in Zod exact; headless default off); values ZWorld.RiflePair; counted fights: menu OpenZ/Deviations/Count Grunt Mirror Fights",
                status = ok ? DevActive : DevCheck,
            };
        }

        // ------------------------------------------------------------------ DV79 unmanned stop: a vehicle with nobody inside never moves
        /// <summary>L02 without the map's units, one red vehicle per world on open ground (tile 47,30), ordered 11 tiles down (patrol: on a
        /// patrol over the same stretch). At loseAt s its driver is removed in one step with no shooter (ZObject.DamageDriverHealth: the
        /// server pass of zserver.cpp:1808-1874 does not run for it). Returns: 1 = it was moving at that step; the largest change of its
        /// position in the 6 s after it (hundredths of a px, rounded up); 1 = an order, a speed, a target or a patrol was left at the end;
        /// and with reenter: 1 = a red grunt got in afterwards and the vehicle drove at least 16 px on a new order within 3 s.</summary>
        static int[] UnmannedProbe(string type, double loseAt, bool patrol, bool reenter)
        {
            var w = ZWorld.Create(SettingsText, MapText("L02"), 1);
            w.Objects.RemoveAll(o => o.IsUnit || o.Type == "grenades" || o.Type == "flag");
            w.Flags.Clear();
            foreach (var b in w.Objects) if (b.Kind == ZKind.Building) b.StopBuildingProduction();
            var v = w.CreateObject("vehicle", type, 47 * 16, 30 * 16, Team.Red);
            int sx = v.CenterX, sy = v.CenterY, gx = 47 * 16 + 8, gy = 41 * 16 + 8;
            if (patrol) w.SendPatrol(Team.Red, v, gx, gy); else w.SendWaypoints(Team.Red, v, new[] { ZWaypoint.Move(gx, gy) });
            while (w.Time < loseAt) { w.Tick(ZConst.TICK); w.Events.Clear(); }
            int moving = v.IsMoving ? 1 : 0;
            v.DamageDriverHealth(1 << 20);
            double x0 = v.PosX, y0 = v.PosY, far = 0;
            for (int i = 0; i < 600; i++)
            {
                w.Tick(ZConst.TICK); w.Events.Clear();
                far = Math.Max(far, Math.Sqrt((v.PosX - x0) * (v.PosX - x0) + (v.PosY - y0) * (v.PosY - y0)));
            }
            bool left = v.Waypoints.Count > 0 || v.IsMoving || v.AttackObject != null || v.Patrolling;
            int drove = 0;
            if (reenter && v.Owner == Team.Neutral)
            {
                var g = w.CreateRobotGroup("grunt", v.CenterX - 8, v.CenterY - 8, Team.Red, 1);
                w.SendWaypoints(Team.Red, g, new[] { new ZWaypoint { mode = WpMode.Enter, refId = v.Id, x = v.CenterX, y = v.CenterY } });
                for (int i = 0; i < 300 && v.Owner != Team.Red; i++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                if (v.Owner == Team.Red)
                {
                    double x1 = v.PosX, y1 = v.PosY;
                    bool toStart = Math.Abs(v.CenterY - sy) > Math.Abs(v.CenterY - gy);              // the farther end of the stretch
                    w.SendWaypoints(Team.Red, v, new[] { ZWaypoint.Move(toStart ? sx : gx, toStart ? sy : gy) });
                    for (int i = 0; i < 300; i++) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                    if (Math.Sqrt((v.PosX - x1) * (v.PosX - x1) + (v.PosY - y1) * (v.PosY - y1)) >= 16) drove = 1;
                }
            }
            return new[] { moving, (int)Math.Ceiling(far * 100), left ? 1 : 0, drove };
        }
        const int UnmannedProbeN = 20;

        static Deviation DevUnmannedStop(Func<string, int, int> getInt)
        {
            bool was = ZWorld.UnmannedStopDeviation, ef = ZWorld.EndFreezeDeviation, pat = ZWorld.PatrolDeviation;
            int stops = ZWorld.UnmannedStops, caught = ZWorld.UnmannedCaught;
            string got = "-"; bool ok = false;
            try
            {
                ZWorld.EndFreezeDeviation = false;                                                // one team only on the map: the match must not count as over
                ZWorld.PatrolDeviation = true;
                string[] types = { "light", "jeep", "medium", "heavy" };
                int n = UnmannedProbeN, patrols = 0, movingOn = 0, movedOn = 0, farOn = 0, leftOn = 0, droveOn = 0, movedOff = 0, farOff = 0;
                for (int i = 0; i < n; i++)
                {
                    string type = types[i % types.Length]; double at = 0.5 + 0.05 * i; bool patrol = i % 8 >= 4;
                    if (patrol) patrols++;
                    ZWorld.UnmannedStopDeviation = true; var on = UnmannedProbe(type, at, patrol, true);
                    ZWorld.UnmannedStopDeviation = false; var off = UnmannedProbe(type, at, patrol, false);
                    movingOn += on[0]; if (on[1] > 0) movedOn++; farOn = Math.Max(farOn, on[1]); leftOn += on[2]; droveOn += on[3];
                    if (off[1] > 0) movedOff++; farOff = Math.Max(farOff, off[1]);
                }
                got = $"L02 open ground, {n} red vehicles (light tank, jeep, medium, heavy in turn; {patrols} of them on a patrol) lose their driver in one step in mid-move, with no shooter: " +
                      $"{movedOn} of {n} changed position in the next 6 s (largest {farOn / 100.0:F2} px), {leftOn} kept an order, a speed, a target or a patrol; a grunt got in and {droveOn} of {n} drove off again within 3 s; " +
                      $"with the switch off {movedOff} of {n} drove on (largest {farOff / 100.0:F0} px)";
                ok = movingOn == n && movedOn == 0 && leftOn == 0 && droveOn == n && movedOff > 0;
            }
            finally { ZWorld.UnmannedStopDeviation = was; ZWorld.EndFreezeDeviation = ef; ZWorld.PatrolDeviation = pat; ZWorld.UnmannedStops = stops; ZWorld.UnmannedCaught = caught; }
            return new Deviation
            {
                id = "DV79", change = "Unmanned stop: a vehicle with nobody inside stops in the step it loses its driver and never moves, turns or fires",
                zod = "the hit that kills the driver clears the drivers and sets the null team; 'disengage, clear waypoints, and stop movement' are 'done at the server level' (zobject.cpp:334-359): ZServer::UpdateObjectDriverHealth after every object has run (zserver.cpp:1808-1874), and only while the shooter still has an attack object (zserver.cpp:1212); an ejected vehicle is cleared in the eject handler (zserver_events.cpp:631-731)",
                openz = "the step that removes the last driver also stops the vehicle (speed zero; waypoints, path, patrol / hold record, queued pathing actions and a heavy tank's loaded second barrel dropped; its target goes in the same tick, in Zod's server pass or its own tick). While nobody is inside, its tick ends before the patrol, waypoint and fire steps, " +
                        "so a move handed to it afterwards (the next patrol leg of DV68, the bridge queue's drive-off of DV8) is dropped and never driven, and the bridge queue lets go of it at once: it stands on a deck like any parked hull. It can be entered as before and is a normal vehicle from the tick a robot is inside. " +
                        "View: the drawn vehicle stands where it is drawn from that frame (no catch-up drive, no pivot onto its sim spot), wheels and tracks stop (Sim/ZObject.Unmanned.cs, View/VehicleSlip.cs Hold)",
                measured = got,
                toggle = "ZWorld.UnmannedStopDeviation (code switch; ZGame: on at every start, Zod exact included; headless default off)",
                status = ok ? DevActive : DevCheck,
            };
        }

        // ------------------------------------------------------------------ DV76 vehicles first: a vehicle is never held by robots of its own team
        /// <summary>L02 without the map's units: a red light tank ordered once along 11 tiles of open ground (tile 47,30 to 47,41). With
        /// crowd: 12 red grunt squads on hold stand across that path. Returns the tank's arrival (tenths of a second, -1 = not within
        /// VehicleFirstProbeMaxT), the step-aside moves asked of robots for a vehicle, the robots that lost health and the robots placed.</summary>
        static int[] VehicleFirstProbe(bool crowd)
        {
            var w = ZWorld.Create(SettingsText, MapText("L02"), 1);
            w.Objects.RemoveAll(o => o.IsUnit || o.Type == "grenades" || o.Type == "flag");
            w.Flags.Clear();
            foreach (var b in w.Objects) if (b.Kind == ZKind.Building) b.StopBuildingProduction();
            var tank = w.CreateObject("vehicle", "light", 47 * 16, 30 * 16, Team.Red);
            var robots = new List<ZObject>();
            if (crowd)
                for (int gx = 0; gx < 4; gx++)
                    for (int gy = 0; gy < 3; gy++)
                    {
                        var s = w.CreateRobotGroup("grunt", (45 + gx) * 16 + 8, (34 + gy) * 16 + 8, Team.Red);
                        w.SendHold(Team.Red, s);
                        robots.Add(s); robots.AddRange(s.Minions);
                    }
            Until(w, 3);                                                                       // the crowd settles on its spacing first
            int asked0 = ZPathing.YieldsAsked;
            w.SendWaypoints(Team.Red, tank, new[] { ZWaypoint.Move(47 * 16 + 8, 41 * 16 + 8) });
            double t0 = w.Time, arrived = -1;
            while (arrived < 0 && w.Time - t0 < VehicleFirstProbeMaxT)
            {
                w.Tick(ZConst.TICK); w.Events.Clear();
                if (tank.Waypoints.Count == 0 && tank.CenterY > 41 * 16 - 8) arrived = w.Time - t0;       // the order is done AND it stands at the goal
            }
            return new[] { arrived < 0 ? -1 : (int)Math.Round(arrived * 10), ZPathing.YieldsAsked - asked0, robots.Count(r => r.Health < r.MaxHealth), robots.Count };
        }
        const double VehicleFirstProbeMaxT = 60;

        static Deviation DevVehicleFirst(Func<string, int, int> getInt)
        {
            bool was = ZPathing.VehicleFirst, ef = ZWorld.EndFreezeDeviation; int asked = ZPathing.YieldsAsked;
            string got = "-"; bool ok = false;
            try
            {
                ZWorld.EndFreezeDeviation = false;                                                // one team only on the map: the match must not count as over
                ZPathing.VehicleFirst = true; var free = VehicleFirstProbe(false); var on = VehicleFirstProbe(true);
                ZPathing.VehicleFirst = false; var off = VehicleFirstProbe(true);
                string T(int tenths) => tenths < 0 ? $"not there in {VehicleFirstProbeMaxT:0} s" : (tenths / 10.0).ToString("F1", CultureInfo.InvariantCulture) + " s";
                got = $"L02, open ground, a red light tank ordered once along 11 tiles: free path {T(free[0])}; through {on[3]} red grunts on hold standing across the path {T(on[0])}, " +
                      $"{on[1]} step-aside moves asked of them, {on[2]} robots hurt; the same crowd with the switch off {T(off[0])}";
                ok = free[0] > 0 && on[0] > 0 && on[0] <= free[0] * 1.15 + 5 && on[2] == 0 && (off[0] < 0 || off[0] >= on[0] * 1.5);
            }
            finally { ZPathing.VehicleFirst = was; ZPathing.YieldsAsked = asked; ZWorld.EndFreezeDeviation = ef; }
            return new Deviation
            {
                id = "DV76", change = "Vehicles first: a vehicle on an order is never held by robots of its own team",
                zod = "no unit collision: units pass through each other, only idle units are nudged apart (ScuffleUnits, zserver.cpp:974-1041), so a vehicle is never held up by its own infantry",
                openz = $"under the spacing layer (DV6) a vehicle inside a crowd of its own robots side-stepped, queued and stood. Now the robots of its team in the strip ahead of it ({ZPathing.YieldAheadPx:0} px) step aside at Zod's running speed (x1.8), " +
                        "and the vehicle never waits for them: a robot that has no room, or is not out of the way in time, is driven through at full speed, pushed out sideways where there is room and never hurt (the run-over rule DV65 stays for enemies only). " +
                        "A standing vehicle is not moved by robots of its own team either, and in a one-lane gap they are no lane traffic for it. Enemy units, other vehicles, buildings and terrain block as before; bridge decks keep their queue (DV8) (Sim/ZYield.cs)",
                measured = got,
                toggle = "ZPathing.VehicleFirst (code switch; ZGame: on at every start, off in Zod exact)",
                status = ok ? DevActive : DevCheck,
            };
        }

        /// <summary>L02, CPU blue: three blue squads stand by the blue fort with no order (the bot does not use them: they are added after
        /// its plan); at the 7:00 check they must get one attack-move order to the red fort; a blue squad walking elsewhere is untouched.</summary>
        static Deviation DevIdleSweep(Func<string, int, int> getInt)
        {
            double every = ZWorld.IdleSweepEvery; ZWorld.IdleSweepEvery = 120.0;                      // probe only: the first check at 2:00 (headless an idle red loses before 7:00)
            var w = ZWorld.Create(SettingsText, MapText("L02"), 1);
            w.Bots.Add(new ZBot(w, Team.Blue, 108));
            var fort = w.Objects.First(o => o.IsFort && o.Owner == Team.Blue);
            var idle = new List<ZObject>();
            for (int i = 0; i < 3; i++) idle.Add(w.CreateRobotGroup("grunt", fort.LocX + fort.WidthPix + 16 + i * 24, fort.LocY + fort.HeightPix / 2, Team.Blue, 3));
            int idlePx = idle[0].CenterX, idlePy = idle[0].CenterY;
            double orderedAt = -1; int ordered = 0;
            try
            {
                while (w.Time < 140)
                {
                    foreach (var o in idle) if (o.Waypoints.Count > 0 && w.Time < 119) o.Waypoints.Clear();   // the bot never picks them before the check (forgotten)
                    w.Tick(ZConst.TICK); w.Events.Clear();
                    if (orderedAt < 0 && w.Time >= 120) { ordered = idle.Count(o => !o.Destroyed && o.Waypoints.Count > 0); if (ordered > 0) orderedAt = w.Time; }
                }
            }
            finally { ZWorld.IdleSweepEvery = every; }
            double moved = idle.Where(o => !o.Destroyed).Select(o => Math.Sqrt((o.CenterX - idlePx) * (double)(o.CenterX - idlePx) + (o.CenterY - idlePy) * (double)(o.CenterY - idlePy)) / 8.0).DefaultIfEmpty(0).Max();
            string log = w.IdleSweepLog.Count > 0 ? string.Join(" / ", w.IdleSweepLog) : "no check logged";
            bool ok = w.IdleSweepChecks > 0 && ordered >= 2;
            return new Deviation
            {
                id = "DV70", change = "CPU idle sweep: every 7 min the CPU sends squads it forgot by its factories / fort to attack the enemy fort",
                zod = "zbot.cpp only orders units its own plan picks; units it never picks stand by the factory until attacked",
                openz = $"at 7:00, 14:00, 21:00 ... each CPU team's squads / vehicles with no order or target, not fighting, guarding or defending, moved < 3 m in the last {ZWorld.IdleSweepStillSec:F0} s and within 20 m of its factory / fort are counted; with {ZWorld.IdleSweepMinCount}+ of them each gets one attack-move to the enemy fort; human teams never",
                measured = $"L02, CPU blue, 3 idle blue grunt squads by the blue fort: {log}; {ordered} of 3 had the order at the check, the farthest walked {moved:F0} m by 140 s (probe check moved to 2:00; the game checks at 7:00, 14:00, ...)",
                toggle = $"ZWorld.IdleSweepDeviation = {(ZWorld.IdleSweepDeviation ? "true (default)" : "false")} (code switch)",
                status = ok ? DevActive : DevCheck,
            };
        }

        /// <summary>DV75 idle rest (part of the driver-sniping deviation DC2): the same ambush, counted on a light tank whose crew
        /// rests and on one that stood as long with the rest held off (OpenZRestCount.Probe: headless, flat map).</summary>
        static Deviation DevIdleRest(Func<string, int, int> getInt)
        {
            bool exactMode = getInt("oz_zod_exact", 0) == 1;
            string measured = "not measured in this build"; bool ok = true;
#if !OPENZ_HARNESS
            var (rest, fresh, n) = OpenZRestCount.Probe("psycho", "light", 20);
            measured = $"flat map, a psycho squad appears 100 px from a crewed light tank that shoots back, {n} ambushes each: driver sniped {rest} of {n} while the crew rests, {fresh} of {n} on a tank that stood as long with no rest";
            ok = rest >= fresh;
#endif
            return new Deviation
            {
                id = "DV75", change = "Idle rest: the crew of a long-idle tank opens the hatch and rests in it; a resting driver is easier to snipe",
                zod = "no such rule: the lid opens only when the tank engages a target that can snipe (4 of 5 times, zvehicle.cpp:198-207) and closes 0-1.4 s after it lets go (zvehicle.cpp:209-240); a tank can be sniped only while the lid is open (zvehicle.cpp:178-186); one attack_snipe_chance per hit (zobject.cpp:2190)",
                openz = $"a crewed light / medium / heavy tank with no order, no move, no target and no hit for {ZWorld.IdleRestSeconds:0} s (each vehicle adds its own 0-{ZWorld.IdleRestSpreadSeconds:0} s) opens its hatch and the driver rests head and shoulders out; while he rests he can be sniped and the snipe chance of that hit is x{ZWorld.RestSnipeFactor:0.0} (at most {ZWorld.RestSnipeCap:0.0}); an order shuts the hatch at once, an enemy in sight {ZWorld.IdleRestWakeSeconds:0.0} s later, a hit {ZWorld.IdleRestHitWakeSeconds:0.0} s later; the crane shows its resting driver only (never sniped, as in Zod); veteran crews never rest; both teams, CPU vehicles included",
                measured = measured,
                toggle = $"the DC2 switch: PlayerPrefs oz_zod_exact = {(exactMode ? "1 (Zod exact: no rest)" : "0 (OpenZ, default)")}; values ZWorld.IdleRestSeconds / IdleRestWakeSeconds / IdleRestHitWakeSeconds / RestSnipeFactor (code)",
                status = (ok ? DevActive : DevCheck) + (exactMode ? " (Zod exact mode selected)" : ""),
            };
        }

        static (double red, double blue) StartHoldRun(bool hold)
        {
            ZWorld.CalmStartDeviation = hold;
            var w = ZWorld.Create(SettingsText, MapText("L02"), 1);
            w.Bots.Add(new ZBot(w, Team.Blue, 108));                                              // blue = the CPU (as in the game)
            double red = -1, blue = -1;
            bool Auto(ZObject o) => o.Waypoints.Count > 0 && !o.Waypoints[0].playerGiven && (o.Waypoints[0].mode == WpMode.Enter || o.Waypoints[0].mode == WpMode.PickupGrenades || o.Waypoints[0].mode == WpMode.Agro
                || (o.Waypoints[0].mode == WpMode.Move && o.Waypoints[0].refId >= 0 && w.Flags.Any(f => f.Id == o.Waypoints[0].refId)));
            while (w.Time < 40 && (red < 0 || blue < 0))
            {
                w.Tick(ZConst.TICK); w.Events.Clear();
                foreach (var o in w.Objects)
                {
                    if (o.Destroyed || !o.IsMobile || o.IsMinion || !Auto(o)) continue;
                    if (o.Owner == Team.Red && red < 0) red = w.Time;
                    if (o.Owner == Team.Blue && blue < 0) blue = w.Time;
                }
            }
            return (red, blue);
        }

        static (double overAt, int built, int producing, int moving) EndRun(bool freeze)
        {
            ZWorld.EndFreezeDeviation = freeze;
            var w = ZWorld.Create(SettingsText, MapText("L02"), 1);
            var fort = w.Objects.First(o => o.IsFort && o.Owner == Team.Blue);
            foreach (var o in w.Objects.ToList())
                if (o.IsMobile && o.Owner == Team.Red && !o.IsMinion && !o.Destroyed)
                    w.SendWaypoints(Team.Red, o, new List<ZWaypoint> { ZWaypoint.Move(fort.CenterX, fort.CenterY) });
            while (w.Time < 3) { w.Tick(ZConst.TICK); w.Events.Clear(); }
            foreach (var o in w.Objects.ToList())                                              // the blue fort falls (OpenZVictory.DebugFortFalls)
                if (o.IsFort && o.Owner == Team.Blue && !o.Destroyed) { o.DamageHealth(o.Health + 1); w.UpdateObjectHealth(o, -1); }
            double overAt = -1; int built = 0, moving = -1;
            while (w.Time < 110 && (overAt < 0 || w.Time < overAt + 90))
            {
                w.Tick(ZConst.TICK);
                foreach (var e in w.Events)
                {
                    if (e.type == ZEventType.GameOver && overAt < 0) overAt = w.Time;
                    else if (e.type == ZEventType.UnitBuilt && overAt >= 0) built++;
                }
                w.Events.Clear();
                if (overAt >= 0 && moving < 0 && w.Time >= overAt + 2) moving = w.Objects.Count(o => o.IsMobile && o.Owner == Team.Red && !o.Destroyed && o.IsMoving);
            }
            int producing = w.Objects.Count(o => o.IsProducer && !o.Destroyed && o.Building);
            return (overAt, built, producing, Math.Max(0, moving));
        }

        // ------------------------------------------------------------------ DV9 vehicle duel manoeuvres (vehicle_combat_v1)
        /// <summary>Heavy vs heavy on L02 open ground (tile 49,38), attack orders both ways, every other unit removed: once with DV9
        /// (share of shots fired on the move, manoeuvres and their commit times) and once Zod exact (the same seed).</summary>
        static (int shots, int moving, int mans, double minCommit, int shortEnds, int dodges, double end, string winner) DuelProbe(bool on)
        {
            bool saved = ZWorld.DuelDeviation; ZWorld.DuelDeviation = on;
            try
            {
                var w = ZWorld.Create(SettingsText, MapText("L02"), 1);
                w.Objects.RemoveAll(o => o.IsUnit);
                int cx = 49 * 16 + 8, cy = 38 * 16 + 8;
                bool Free(double x, double y)
                {
                    int tx = (int)x >> 4, ty = (int)y >> 4;
                    for (int i = tx - 1; i <= tx + 1; i++) for (int j = ty - 1; j <= ty + 1; j++) if (!w.Path.TilePassable(i, j, false)) return false;
                    return true;
                }
                double ang = 37 * Math.PI / 180;
                for (int k = 0; k < 36; k++)
                {
                    double a = (37 + 10 * k) * Math.PI / 180;
                    if (Free(cx - Math.Cos(a) * 112, cy - Math.Sin(a) * 112) && Free(cx + Math.Cos(a) * 112, cy + Math.Sin(a) * 112)) { ang = a; break; }
                }
                var r = w.CreateObject("vehicle", "heavy", (int)(cx - Math.Cos(ang) * 112) - 16, (int)(cy - Math.Sin(ang) * 112) - 16, Team.Red);
                var b = w.CreateObject("vehicle", "heavy", (int)(cx + Math.Cos(ang) * 112) - 16, (int)(cy + Math.Sin(ang) * 112) - 16, Team.Blue);
                w.SendWaypoints(Team.Red, r, new[] { new ZWaypoint { mode = WpMode.Attack, refId = b.Id, x = b.CenterX, y = b.CenterY } });
                w.SendWaypoints(Team.Blue, b, new[] { new ZWaypoint { mode = WpMode.Attack, refId = r.Id, x = r.CenterX, y = r.CenterY } });
                int shots = 0, moving = 0, dodges = 0; bool[] inDodge = new bool[2];
                while (w.Time < 60 && !r.Destroyed && !b.Destroyed && r.KillAt < 0 && b.KillAt < 0)
                {
                    w.Tick(ZConst.TICK);
                    foreach (var e in w.Events)
                        if ((e.type == ZEventType.MissileFired || e.type == ZEventType.Hitscan) && (e.objId == r.Id || e.objId == b.Id))
                        { shots++; var s = e.objId == r.Id ? r : b; if (s.IsMoving && !s.PathHeld) moving++; }
                    w.Events.Clear();
                    for (int i = 0; i < 2; i++)
                    {
                        var o = i == 0 ? r : b;
                        bool d = o.Waypoints.Count > 0 && o.Waypoints[0].mode == WpMode.Dodge;
                        if (d && !inDodge[i]) dodges++;
                        inDodge[i] = d;
                    }
                }
                var st = new[] { r.DuelState, b.DuelState }.Where(x => x != null).ToList();
                int mans = st.Sum(x => x.Manoeuvres), shortEnds = st.Sum(x => x.ShortAborts);
                double minC = st.Where(x => x.Expired > 0).Select(x => x.MinExpired).DefaultIfEmpty(0).Min();
                string win = r.Destroyed || r.KillAt >= 0 ? "blue" : b.Destroyed || b.KillAt >= 0 ? "red" : "none";
                return (shots, moving, mans, minC, shortEnds, dodges, w.Time, win);
            }
            finally { ZWorld.DuelDeviation = saved; }
        }

        static Deviation DevDuel(Func<string, int, int> getInt)
        {
            var on = DuelProbe(true); var off = DuelProbe(false);
            bool pref = getInt("oz_duel_sim", 1) == 1 && getInt("oz_zod_exact", 0) == 0;
            string P(int a, int b) => b == 0 ? "-" : $"{100.0 * a / b:0}%";
            bool ok = on.mans > 0 && on.shots > 0 && on.moving * 10 >= on.shots * 8 && on.minCommit >= ZDuel.CommitMin - 1e-6;
            return new Deviation
            {
                id = "DV9", change = "Vehicle duels: tanks, mech and jeep manoeuvre while they fire at a vehicle / cannon in range",
                zod = "vehicles already fire on the move and keep firing while they dodge (only robots disengage, zobject.cpp:3519-3520, :1615); the missile's target dodges (DodgeMissile :3657-3714, kept); but an ATTACK / AGRO waypoint whose target is in range stops dead and stands (StopMove + Engage, :2356-2362, :2296-2302)",
                openz = $"jeep / light / medium / heavy vs a vehicle or cannon, ATTACK or AGRO only (never a MOVE order, robots, APC, crane, Mobile Missile): committed manoeuvres of {ZDuel.CommitMin:0.#}-{ZDuel.CommitMax:0.#} s (arc at attack radius - {ZDuel.Margin("heavy")} px (heavy) / {ZDuel.Margin("jeep")} px (jeep, light), flank dash, short reverse, spiral in), steered every {ZDuel.AimInterval} s along swept, passable, lane-free, in-range headings; Zod's own movement, speeds, damage, cooldown, lead and scatter; own seeded stream (ZWorld.DuelRng)",
                measured = $"L02 heavy vs heavy, seed 1: DV9 {P(on.moving, on.shots)} of {on.shots} shots fired on the move, {on.mans} manoeuvres, shortest completed commit {on.minCommit:0.00} s, {on.shortEnds} cut short, {on.dodges} Zod dodges, {on.winner} won at {on.end:0.0} s; Zod exact {P(off.moving, off.shots)} of {off.shots} shots on the move (dodges only), {off.dodges} dodges, {off.winner} won at {off.end:0.0} s",
                toggle = $"PlayerPrefs oz_duel_sim = {(getInt("oz_duel_sim", 1) == 1 ? "1 (on, default)" : "0 (off)")}; menu OpenZ/Deviations/Vehicle Duel Manoeuvres; off in 'Zod exact' (oz_zod_exact = 1) and in this parity report's measurements",
                status = !ok ? DevCheck : pref ? DevActive : DevActive + " (off by pref)",
            };
        }

        // ------------------------------------------------------------------ DV8 bridge queue (one lane, robots wade)
        /// <summary>Headless check of the bridge queue (Sim/ZBridges.cs) on the L02 east bridge (51,58): a red jeep on the north
        /// bank and a red heavy tank on the south bank are sent across at the same moment, a grunt squad 6 tiles west of the
        /// bridge is sent to the south bank. Measured: both vehicles cross, never on the deck together, and the squad wades.</summary>
        static Deviation DevBridgeQueue(Func<string, int, int> getInt)
        {
            bool on = ZPathing.BridgeQueueEnabled;
            bool freeze = ZWorld.EndFreezeDeviation; ZWorld.EndFreezeDeviation = false;   // one team only = "game over" at 1 s (DV10 would halt everyone)
            try { return DevBridgeQueueRun(on); } finally { ZWorld.EndFreezeDeviation = freeze; }
        }

        static Deviation DevBridgeQueueRun(bool on)
        {
            var w = ZWorld.Create(SettingsText, MapText("L02"), 1);
            w.Objects.RemoveAll(o => o.IsUnit || o.Type == "grenades" || o.Type == "flag");
            w.Flags.Clear();
            foreach (var b in w.Objects) if (b.Kind == ZKind.Building) b.StopBuildingProduction();
            var jeep = w.CreateObject("vehicle", "jeep", 52 * 16, 48 * 16, Team.Red);
            var heavy = w.CreateObject("vehicle", "heavy", 52 * 16, 69 * 16, Team.Red);
            var squad = w.CreateRobotGroup("grunt", 46 * 16, 54 * 16, Team.Red);
            var robots = new List<ZObject> { squad }; robots.AddRange(squad.Minions);
            w.SendWaypoints(Team.Red, jeep, new[] { ZWaypoint.Move(53 * 16, 72 * 16) });
            w.SendWaypoints(Team.Red, heavy, new[] { ZWaypoint.Move(53 * 16, 48 * 16) });
            w.SendWaypoints(Team.Red, squad, new[] { ZWaypoint.Move(46 * 16 + 8, 66 * 16 + 8) });
            int deckX0 = 52 * 16, deckX1 = 54 * 16, deckY0 = 58 * 16, deckY1 = 64 * 16;
            bool OnDeck(ZObject o) => o.CenterX >= deckX0 && o.CenterX < deckX1 && o.CenterY >= deckY0 && o.CenterY < deckY1;
            int together = 0, waded = 0; var wet = new HashSet<int>();
            double jeepT = -1, heavyT = -1, squadT = -1;
            while (w.Time < 240 && (jeepT < 0 || heavyT < 0 || squadT < 0))
            {
                w.Tick(ZConst.TICK); w.Events.Clear();
                if (OnDeck(jeep) && OnDeck(heavy)) together++;
                foreach (var r in robots) if (w.Map.ClassAt(r.CenterX >> 4, r.CenterY >> 4) == ZClass.Water) wet.Add(r.Id);
                if (jeepT < 0 && jeep.Waypoints.Count == 0 && jeep.CenterY > 66 * 16) jeepT = w.Time;
                if (heavyT < 0 && heavy.Waypoints.Count == 0 && heavy.CenterY < 56 * 16) heavyT = w.Time;
                if (squadT < 0 && robots.All(r => r.Waypoints.Count == 0) && squad.CenterY > 63 * 16) squadT = w.Time;
            }
            waded = wet.Count;
            var lane = w.Pathing.BridgeLanes.FirstOrDefault(b => b.Bridge.LocX == 51 * 16 && b.Bridge.LocY == 58 * 16);
            string F(double t) => t < 0 ? "not across in 240 s" : $"{t:F1} s";
            bool ok = on && jeepT > 0 && heavyT > 0 && squadT > 0 && together == 0 && waded == robots.Count;
            return new Deviation
            {
                id = "DV8", change = "Bridge queue (one lane per deck, robots wade)",
                zod = "no rule: units drive through each other on a deck (research 7.4, bbridge.cpp:168-190: 2-tile deck between impassable rails); robots follow Zod's A* (road 1/(1.689+0.5), water 1/0.7 weights), which prefers bridge roads",
                openz = $"a deck is one lane: one direction at a time; units ask for it 5.5 tiles out; behind traffic going their own way (nobody waiting at the other end) vehicles wait in line at the bridge (no bay trip away and back), else they wait in bays BESIDE the road (3.5+ tiles off the approach, never further back than needed), longest wait first, up to {ZPathing.MaxConvoy} per turn while the other end has waited < {ZPathing.ConvoyGrace:0} s; " +
                        $"robots never queue: over a busy deck they wade unless it is clearly faster (x{ZPathing.RobotBridgeAdvantage} + 3 tiles, time-weighted A*), a robot refused at a deck wades at once, wading keeps right; squads cross the deck single file and re-form; queued {ZPathing.VehicleReplanAfter:0} s = another way if not slower, {ZPathing.BridgeGiveUpAfter:0} s = nearest reachable spot; speeds and Zod's paths otherwise unchanged; last resort: a unit in a dead jam (no 2 px in {ZPathing.GhostAfter:0} s, off decks) passes through for {ZPathing.GhostTime} s",
                measured = $"L02 bridge (51,58): jeep N->S {F(jeepT)}, heavy S->N {F(heavyT)}, both on the deck together {together} ticks; grunt squad (3) off-axis N->S {F(squadT)}, {waded}/{robots.Count} robots waded" +
                           (lane != null ? $"; queue: {lane.Queued} queued, {lane.Switches} direction turns, max wait {lane.MaxWait:F0} s" : "; no bridge lane found"),
                toggle = "ZPathing.BridgeQueueEnabled (static; false = the older lane rule for decks), ZPathing.GhostEnabled (dead-jam pass-through)",
                status = !on ? DevActive + " (off)" : ok ? DevActive : DevCheck,
            };
        }

        // ------------------------------------------------------------------ DV6 pathing / spacing layer
        static Deviation DevPathing(Func<string, int, int> getInt)
        {
            var asm = typeof(ZWorld).Assembly;
            var type = asm.GetType("OpenZ.Sim.ZPathing");
            string state = "-"; bool on = false;
            if (type != null)
            {
                var def = type.GetField("DefaultEnabled", BindingFlags.Public | BindingFlags.Static);
                var w = ZWorld.Create(SettingsText, MapText("L02"), 1);
                object inst = typeof(ZWorld).GetField("Pathing")?.GetValue(w) ?? typeof(ZWorld).GetProperty("Pathing")?.GetValue(w);
                object en = inst == null ? null : (object)type.GetField("Enabled")?.GetValue(inst) ?? type.GetProperty("Enabled")?.GetValue(inst);
                on = en is bool b && b;
                state = $"ZPathing.DefaultEnabled = {(def != null ? def.GetValue(null) : "?")}, a new L02 world has it {(inst == null ? "not attached" : on ? "on" : "off")}";
            }
            return new Deviation
            {
                id = "DV6", change = "Sim-side pathing layer (nobody stuck, no overlap, DC5)",
                zod = "a squad stands stacked on one pixel (zserver.cpp:623-657), units may overlap, no stuck recovery",
                openz = type == null ? "not imported: spacing is view-side only (UnitView separation)" : "OpenZ.Sim.ZPathing: sim-side spacing, stuck guard, chokepoint queueing, unreachable orders resolved to the nearest reachable tile; vehicles and robots of one team: DV76",
                measured = type == null ? "OpenZ.Sim.ZPathing not in this build" : state,
                toggle = type == null ? "-" : "ZPathing.Enabled (per world) / ZPathing.DefaultEnabled",
                status = type == null ? DevNotInBuild : on ? DevActive : DevActive + " (off)",
            };
        }

        /// <summary>Public static fields / properties / constants of OpenZ.Sim types whose member name (or, with typeNameFilter, whose
        /// type name) contains one of the filters, plus ZWorld instance fields of such a name on a fresh world: "Type.Member = value; ...".</summary>
        static string SimKnobs(string[] memberFilters, string none, string typeNameFilter = null)
        {
            var found = new List<string>();
            Type[] types;
            try { types = typeof(ZWorld).Assembly.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
            foreach (var t in types.Where(t => t.Namespace == "OpenZ.Sim" && !t.IsEnum).OrderBy(t => t.Name, StringComparer.Ordinal))
            {
                bool typeHit = typeNameFilter != null && t.Name.IndexOf(typeNameFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                if (typeNameFilter != null && !typeHit) continue;
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
                    if (typeHit || Hit(f.Name, memberFilters)) found.Add($"{t.Name}.{f.Name} = {Fmt(f.GetValue(null))}");
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Static))
                    if (p.GetIndexParameters().Length == 0 && (typeHit || Hit(p.Name, memberFilters))) found.Add($"{t.Name}.{p.Name} = {Fmt(p.GetValue(null))}");
            }
            if (typeNameFilter == null)
            {
                var w = ZWorld.Create(SettingsText, MapText("L02"), 1);
                foreach (var f in typeof(ZWorld).GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (Hit(f.Name, memberFilters)) found.Add($"ZWorld.{f.Name} = {Fmt(f.GetValue(w))}");
            }
            return found.Count > 0 ? string.Join("; ", found) : none;
        }

        static bool Hit(string name, string[] filters) => filters.Any(f => name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0);

        static string Fmt(object v)
        {
            if (v is IFormattable f) return f.ToString(null, CultureInfo.InvariantCulture);
            if (v is string str) return str;
            if (v is System.Collections.IDictionary d)
            {
                var kv = new List<string>();
                foreach (System.Collections.DictionaryEntry e in d) kv.Add($"{Fmt(e.Key)} {Fmt(e.Value)}");
                return "{" + string.Join(", ", kv) + "}";
            }
            if (v is System.Collections.IEnumerable list) { var items = new List<string>(); foreach (var x in list) items.Add(Fmt(x)); return "[" + string.Join(", ", items) + "]"; }
            return v?.ToString() ?? "null";
        }

#if !OPENZ_HARNESS
        /// <summary></summary>
        [MenuItem("OpenZ/Deviations/Write Deviations Block")]
        public static string WriteDeviationsBlock()
        {
            var devs = CollectDeviations(PlayerPrefs.GetInt);
            var block = new StringBuilder(); AppendDeviations(block, devs);
            string text = File.Exists(Report) ? File.ReadAllText(Report).Replace("\r\n", "\n") : "# OpenZ Z-parity report\n\n(rows not run yet: OpenZ/Run Parity Report)\n";
            int at = text.StartsWith(DeviationsHeading + "\n", StringComparison.Ordinal) ? 0 : text.IndexOf("\n" + DeviationsHeading + "\n", StringComparison.Ordinal);
            if (at >= 0)
            {
                int start = at == 0 ? 0 : at + 1, next = text.IndexOf("\n## ", start + DeviationsHeading.Length, StringComparison.Ordinal);
                text = text.Substring(0, start) + (next >= 0 ? text.Substring(next + 1) : "");
            }
            text = text.TrimEnd('\n') + "\n\n" + block.ToString().Replace("\r\n", "\n");
            File.WriteAllText(Report, text);
            string summary = DeviationSummary(devs);
            Debug.Log($"parity report {summary} -> {Report}");
            return summary;
        }

        const string DuelMenu = "OpenZ/Deviations/Vehicle Duel Manoeuvres (oz_duel_sim)";

        [MenuItem(DuelMenu)]
        static void ToggleDuel()
        {
            bool on = PlayerPrefs.GetInt("oz_duel_sim", 1) != 1;
            PlayerPrefs.SetInt("oz_duel_sim", on ? 1 : 0); PlayerPrefs.Save();
            Debug.Log($"OpenZ vehicle duels (deviation DV9) from the next Play: {(on ? "ON: tanks, mech and jeep manoeuvre and fire against vehicles / cannons in range" : "OFF: Zod's attacker stops and fires")}" +
                      (PlayerPrefs.GetInt("oz_zod_exact", 0) == 1 ? " (still off while 'Zod exact' oz_zod_exact = 1)" : ""));
        }

        [MenuItem(DuelMenu, true)]
        static bool ToggleDuelCheck() { Menu.SetChecked(DuelMenu, PlayerPrefs.GetInt("oz_duel_sim", 1) == 1); return true; }

        const string HeadStartMenu = "OpenZ/Deviations/Head Start On L02 (human vs CPU)";

        [MenuItem(HeadStartMenu)]
        static void ToggleHeadStart()
        {
            bool on = PlayerPrefs.GetInt(ZGame.HeadStartPref, 1) != 1;
            PlayerPrefs.SetInt(ZGame.HeadStartPref, on ? 1 : 0); PlayerPrefs.Save();
            Debug.Log($"OpenZ head start (deviation DV3): {(on ? "ON: the player gets " + string.Join(", ", ZGame.HeadStartTypes) + " on L02 from the next Play" : "OFF: L02 starts with the Zod map's units only")}");
        }

        [MenuItem(HeadStartMenu, true)]
        static bool ToggleHeadStartCheck() { Menu.SetChecked(HeadStartMenu, PlayerPrefs.GetInt(ZGame.HeadStartPref, 1) == 1); return true; }

        /// <summary>The snipe package's "Zod exact" switch (its README step 6): PlayerPrefs oz_zod_exact, read by ZGame at every start.</summary>
        const string ZodExactMenu = "OpenZ/Deviations/Zod Exact Driver Sniping (oz_zod_exact)";

        [MenuItem(ZodExactMenu)]
        static void ToggleZodExact()
        {
            bool exact = PlayerPrefs.GetInt("oz_zod_exact", 0) != 1;
            PlayerPrefs.SetInt("oz_zod_exact", exact ? 1 : 0); PlayerPrefs.Save();
            Debug.Log($"OpenZ driver sniping (deviation DV2) from the next Play: {(exact ? "Zod exact (driver hits = the attacker's damage)" : "OpenZ (driver-hit floor, snipers x3, open-seat snipe chance)")}");
        }

        [MenuItem(ZodExactMenu, true)]
        static bool ToggleZodExactCheck() { Menu.SetChecked(ZodExactMenu, PlayerPrefs.GetInt("oz_zod_exact", 0) == 1); return true; }
#endif
    }
}
