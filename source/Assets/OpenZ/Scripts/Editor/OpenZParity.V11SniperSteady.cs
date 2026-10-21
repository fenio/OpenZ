#if !OPENZ_HARNESS
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>The Deviations row DV120 "the sniper stands and fires" (the rule: Sim/ZObject.SniperSteady.cs) and the K248b counted check
    /// (menu OpenZ/V1.1/Sniper: K248b Steady Check). Measured headless as the K248 stage stages it: the level 2 map without its start
    /// units, open dry sand (tile 41,57), three red snipers, two blue grunt squads gx px to the east (one 40 px north, one 15 px further
    /// and 40 px south), the snipers ordered at once to attack the first squad's leader (the player's order); no tank, no grenade, no crate; the
    /// switches of a normal game (headshots on). The drawn stance is the game's own decision code (View/SniperStanceGate.cs,
    /// WeaponsV2Logic.PickStance) driven by a model of the frames around it (UnitView's drawn body and legs, BS01Stance.DriveSniper,
    /// RobotWeaponPose.EngagementStance), stepped with the sim at 100 frames a second.</summary>
    public static partial class OpenZParity
    {
        static void AddSniperSteadyDeviation(List<Deviation> list)
        {
            try { list.Add(DevSniperSteady()); }
            catch (Exception ex) { list.Add(new Deviation { id = "DV120", change = "Snipers stand and fire", zod = "-", openz = "-", measured = "probe threw " + ex.GetType().Name + ": " + ex.Message, toggle = "-", status = DevCheck }); }
        }

        // ------------------------------------------------------------------ switches
        /// <summary>Every public static bool / int / double / float switch of the rules and of the stance gate, put back afterwards.</summary>
        sealed class SsSaved : IDisposable
        {
            readonly List<KeyValuePair<FieldInfo, object>> saved = new List<KeyValuePair<FieldInfo, object>>();
            public SsSaved()
            {
                foreach (var t in new[] { typeof(ZWorld), typeof(ZPathing), typeof(ZObject), typeof(SniperStanceGate) })
                    foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                        if (!f.IsLiteral && !f.IsInitOnly && (f.FieldType == typeof(bool) || f.FieldType == typeof(int) || f.FieldType == typeof(double) || f.FieldType == typeof(float)))
                            saved.Add(new KeyValuePair<FieldInfo, object>(f, f.GetValue(null)));
            }
            public void Dispose() { foreach (var kv in saved) kv.Key.SetValue(null, kv.Value); }
        }

        static void SsSet(Type t, string name, bool v) { var f = t.GetField(name, BindingFlags.Public | BindingFlags.Static); if (f != null && !f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(bool)) f.SetValue(null, v); }

        /// <summary>The rule switches of a normal game (View/ZGame.cs at a level start, default settings, no 'Zod exact'), set by name so
        /// the fight is the same whichever switches the caller had (the parity report, the menu, the headless runner).</summary>
        static void SsGameSwitches()
        {
            foreach (var n in new[] { "SnipeDeviation", "FortGunDeviation", "ApcCrewDeviation", "DuelDeviation", "VeteranDeviation", "ToughPrepareDeviation", "FormationDeviation",
                                      "ClearanceDeviation", "LosDeviation", "FortMortarDeviation", "BuildingReachDeviation", "ExposedDriverDeviation", "ApproachDeviation", "NoTankDuelVsJeep",
                                      "HeavyPairDeviation", "CraneGunDeviation", "HarbourWrecksDeviation", "DetailPropsDeviation", "FortRushDeviation", "BoardNearestDeviation", "ParkedPathDeviation",
                                      "CarParkBotDeviation", "GrabPolicyDeviation", "GrenadeRuleDeviation", "FortDefenderDeviation", "PathV3Deviation", "ElectrobotDeviation", "ElectrobotBotDeviation",
                                      "ElectrobotEscortDeviation", "KnockHoldDeviation", "RunOverDeviation", "RocketSpeedDeviation", "HelpDeviation", "CaptureRunDeviation", "CloseGrabDeviation",
                                      "BotCommitDeviation", "BotLaserDeviation", "BotFinishDeviation", "RepairInPlaceDeviation", "CraneFixDeviation", "CraneAnyRuinDeviation", "RepairBayFireDeviation",
                                      "UnmannedStopDeviation", "RiflePairDeviation", "FireOnMoveDeviation", "EjectLockDeviation", "EnterFallbackDeviation", "ExitLanesDeviation",
                                      "SnipeSafeSpotDeviation", "ElectroSteadyDeviation", "GrabCertainDeviation", "GrenadeBoostDeviation", "DriverFireDeviation", "L16PyramidsDeviation",
                                      "L16ShuttleDeviation", "CalmMoveDeviation", "FortRunDeviation", "RockRoundDeviation", "CalmFollowDeviation", "SquadSplitDeviation",
                                      "VeteranHealDeviation", "FortSniperDeviation", "CtrlAttackEmptyDeviation", "SniperHeadshotDeviation", "GroundFireDeviation", "PyroForceBurnDeviation" })
                SsSet(typeof(ZWorld), n, true);
            foreach (var n in new[] { "ArmourChipDeviation", "ElectrobotAllLevels", "L17CrewsDeviation", "FortDoorGunDeviation", "OvertakeDeviation", "FriendsOverlap", "StrikeGroupDeviation",
                                      "BotHeavyDeviation", "HeroSquadDeviation", "HeroBoostDeviation", "ShuttlePlayableDeviation" })
                SsSet(typeof(ZWorld), n, false);
            SsSet(typeof(ZPathing), "VehicleFirst", true); SsSet(typeof(ZPathing), "ZoneSideCommit", true);
        }

        static string SsNoUnits(string mapJson)
        {
            var m = ZMapData.FromJson(mapJson);
            m.objects.RemoveAll(o => { string c = o.kind.Split(':')[0]; return c == "robot" || c == "vehicle" || c == "cannon"; });
            return UnityEngine.JsonUtility.ToJson(m);
        }

        // ------------------------------------------------------------------ the counts
        internal sealed class SsCounts
        {
            public int fights, snipers, unfinished, grunts, shots, heads, headDeaths, pops, legs, reversals, reversalsInRange, walksAfterShot, walkBacks, agroLegs, attackLegs, hops;
            public int lieDowns, lieDownsInRange, firstDownsInRange, getUpsWalk, getUpsLinger, getUpsInRange, picks, nudgesHeld, autoCaptureLegs, cycles;
            public double fightSecs, px, pxAfterFirst, pxInRangeAfterFirst, inRangeSecs, inRangeMovingSecs;
            public ulong trace = 1469598103934665603UL;
            /// <summary>Stance changes (get up / lie down) while an enemy was in range, the first lie-down of each sniper in each fight
            /// (from one stance pick to the next) not counted: it takes its position once.</summary>
            public int Flips => getUpsInRange + lieDownsInRange - firstDownsInRange;
            /// <summary>Stance picks beyond the first of each sniper in each fight.</summary>
            public int ReRolls => Math.Max(0, picks - snipers);
            public void Add(SsCounts o)
            {
                fights += o.fights; snipers += o.snipers; unfinished += o.unfinished; grunts += o.grunts; shots += o.shots; heads += o.heads; headDeaths += o.headDeaths; pops += o.pops;
                legs += o.legs; hops += o.hops; reversals += o.reversals; reversalsInRange += o.reversalsInRange; walksAfterShot += o.walksAfterShot; walkBacks += o.walkBacks; agroLegs += o.agroLegs; attackLegs += o.attackLegs;
                lieDowns += o.lieDowns; lieDownsInRange += o.lieDownsInRange; firstDownsInRange += o.firstDownsInRange; getUpsWalk += o.getUpsWalk; getUpsLinger += o.getUpsLinger;
                getUpsInRange += o.getUpsInRange; cycles += o.cycles; picks += o.picks; nudgesHeld += o.nudgesHeld; autoCaptureLegs += o.autoCaptureLegs;
                fightSecs += o.fightSecs; px += o.px; pxAfterFirst += o.pxAfterFirst; pxInRangeAfterFirst += o.pxInRangeAfterFirst; inRangeSecs += o.inRangeSecs; inRangeMovingSecs += o.inRangeMovingSecs;
                trace = SnsFnv(trace, (long)o.trace);
            }
            public string Text()
            {
                double sf = Math.Max(1, snipers);
                return $"{fights} fights, {snipers} sniper-fights, {fightSecs:0.0} s of fighting{(unfinished > 0 ? $", {unfinished} NOT finished" : "")}, {grunts} grunts killed, {shots} sniper shots, {heads} headshots" +
                       $" | movement: {legs} walking legs ({legs / sf:0.00} per sniper and fight; agro {agroLegs}, attack approach {attackLegs}), {reversals} reversals ({reversalsInRange} with an enemy in range), " +
                       $"{walksAfterShot} walks within 1.5 s of shooting, {hops} short steps (0.5-2 m) after the first shot, {walkBacks} agro walk-backs, {px:0} px walked, {pxAfterFirst:0} px after the first shot ({pxAfterFirst / sf / 8:0.0} m per sniper and fight), " +
                       $"{pxInRangeAfterFirst:0} px of it with an enemy in range; {100 * inRangeMovingSecs / Math.Max(0.01, inRangeSecs):0.0} % of the in-range time walking" +
                       $" | drawn stance: {lieDowns} lie-downs ({lieDowns / sf:0.00} per sniper and fight), get-ups {getUpsWalk} for a walk + {getUpsLinger} after the fight, " +
                       $"{getUpsInRange} get-ups with an enemy in range, {Flips} stance flips with an enemy in range, {cycles} down-up-down cycles within 3 s, {ReRolls} stance re-rolls, {nudgesHeld} drawn walks that did not get it up" +
                       $" | after the fights: {autoCaptureLegs} auto-capture walks | head pops {pops} for {headDeaths} headshot deaths";
            }
        }

        static ulong SnsFnv(ulong h, long v) { h ^= (ulong)v; h *= 1099511628211UL; h ^= h >> 31; return h; }

        static readonly FieldInfo SsCurF = typeof(ZObject).GetField("cur", BindingFlags.NonPublic | BindingFlags.Instance);
        static string SsStage(ZObject o)
        {
            try { var c = SsCurF.GetValue(o); return c.GetType().GetField("stage", BindingFlags.Public | BindingFlags.Instance).GetValue(c).ToString(); } catch { return "?"; }
        }

        // ------------------------------------------------------------------ the drawn stance, frame by frame
        /// <summary>The frames around the stance decision, as the view runs them for one sniper (UnitView.Sync: the drawn body and the
        /// shown gait; BS01Stance.Drive / DriveSniper / OnFire with its clip lengths; RobotWeaponPose.EngagementStance with BS01Stance
        /// on the robot). The decisions themselves are the game's code: SniperStanceGate (the K248b rules, or the old ones with
        /// SniperStanceGate.On = false) and WeaponsV2Logic.PickStance.</summary>
        /// <summary>Debug only (the headless runner's "trace" of the drawn stance): every stance change and pick, null otherwise.</summary>
        internal static Action<string> SsDebug;

        sealed class SsView
        {
            enum St { Up, Down, Aim, Fire, GetUp }
            const float StandHold = 0.12f, WalkOn = 0.3f, WalkOff = 0.15f, MinStand = 0.25f, MinWalk = 0.45f, StrideSlew = 6f, SmoothT = 0.16f, Linger = 2.5f, HurryUp = 1.7f, Cadence = 1.05f;
            static float Len(string clip) => clip switch
            {
                "sniper_kneel_down" => 0.417f, "sniper_kneel_fire" => 0.417f, "sniper_kneel_up" => 0.333f,
                "sniper_prone_down" => 0.833f, "sniper_prone_fire" => 0.417f, "sniper_prone_up" => 0.583f, _ => 0.5f,
            };
            readonly SniperStanceGate gate = new SniperStanceGate();
            bool init, walkShown; double dx, dz, vx, vz, lx, lz; float stride, gaitT = 10f;
            St st = St.Up; string pose = ""; float t, len, speed = 1f, lastEngaged = -100f, lastDrawnShot = -100f;
            int engTarget = -1, engagements; float lastEngagedAt = -100f; WeaponsV2Logic.Stance want = WeaponsV2Logic.Stance.Stand; float poseWeight;
            public int lieDowns, lieDownsInRange, firstDownsInRange, getUpsWalk, getUpsLinger, getUpsInRange, picks, nudgesHeld, cycles; bool held, downThisFight; float lastGetUp = -100f;

            public void Frame(ZObject o, float now, float dt, bool shot, bool water, bool enemyInRange)
            {
                // UnitView: the drawn body eases to the sim (critically damped, never faster than the sim), the gait from its real step speed
                double px = o.CenterX / 8.0, pz = -o.CenterY / 8.0;
                float simSpeed = (float)Math.Sqrt(o.Dx * o.Dx + o.Dy * o.Dy) / 8f;
                if (!init) { dx = px; dz = pz; lx = dx; lz = dz; init = true; }
                else if (!walkShown && (px - dx) * (px - dx) + (pz - dz) * (pz - dz) < StandHold * StandHold) { vx = vz = 0; }
                else
                {
                    double cap = Math.Max(o.MoveSpeed / 8.0, simSpeed), ox = dx, oz = dz;
                    SmoothDamp(ref dx, ref dz, px, pz, ref vx, ref vz, SmoothT, cap * 1.15, dt);
                    double sx = dx - ox, sz = dz - oz, m = Math.Sqrt(sx * sx + sz * sz), lim = cap * dt;
                    if (m > lim && m > 0) { dx = ox + sx / m * lim; dz = oz + sz / m * lim; }
                }
                double stx = dx - lx, stz = dz - lz; lx = dx; lz = dz;
                float stepSpeed = dt > 0 ? (float)(Math.Sqrt(stx * stx + stz * stz) / dt) : 0f;
                stride = stride + Math.Max(-StrideSlew * dt, Math.Min(StrideSlew * dt, stepSpeed - stride));
                bool simWalk = o.IsMoving && o.MoveSpeed > 0, engaged = o.AttackObject != null && !o.AttackObject.Destroyed;
                bool aiming = !simWalk && engaged;
                bool wantWalk = walkShown ? stride > WalkOff : stride > WalkOn;
                gaitT += dt;
                if (wantWalk != walkShown && (gaitT >= (walkShown ? MinWalk : MinStand) || (!wantWalk && aiming))) { walkShown = wantWalk; gaitT = 0f; }
                // RobotWeaponPose.EngagementStance (MirrorExternal / SniperGoesDown): the weight is last frame's (BS01Stance standing = 0)
                if (engaged)
                {
                    bool fresh = o.AttackObject.Id != engTarget;
                    if (fresh && SniperStanceGate.NewEngagement(true, poseWeight, now - lastEngagedAt))
                    {
                        engagements++; picks++; downThisFight = false;
                        float r01 = o.AttackRadius > 0 ? (float)Math.Sqrt((double)(o.AttackObject.CenterX - o.CenterX) * (o.AttackObject.CenterX - o.CenterX) + (double)(o.AttackObject.CenterY - o.CenterY) * (o.AttackObject.CenterY - o.CenterY)) / o.AttackRadius : 0f;
                        want = WeaponsV2Logic.PickStance(o.Id, engagements, r01);
                        if (want == WeaponsV2Logic.Stance.Prone && o.IsFortDefender) want = WeaponsV2Logic.Stance.Kneel;
                        SsDebug?.Invoke($"{now:0.00} #{o.Id} pick {want} (range {r01:0.00})");
                    }
                    engTarget = o.AttackObject.Id; lastEngagedAt = now;
                }
                // BS01Stance.Drive -> SteadySniper -> DriveSniper
                if (engaged) lastEngaged = now;
                t += dt * Math.Max(0.1f, speed);
                bool walking = walkShown && o.MoveSpeed > 0;
                gate.Step(dt, engaged, walking, o.IsMoving, SniperStanceGate.OrderWalk(o), (float)dx, (float)dz);
                gate.Look(o);                                                               // K248d, as BS01Stance.SteadySniper
                bool gated = st == St.Up ? walking || !gate.MayGoDown : gate.MustGetUp(walking);
                bool h = st != St.Up && st != St.GetUp && walking && !gated;
                if (h && !held) nudgesHeld++;
                held = h;
                var before = st;
                switch (st)
                {
                    case St.Up:
                        if (!engaged || gated || water) break;
                        if (want == WeaponsV2Logic.Stance.Stand) break;                                 // SniperGoesDown: stay standing this engagement
                        pose = want == WeaponsV2Logic.Stance.Prone ? "prone" : "kneel";
                        Play("sniper_" + pose + "_down", 1f); st = St.Down; break;
                    case St.Down:
                        if (gated) { GetUp(gated); break; }
                        if (t >= len * 0.97f) { Play("aim", 1f, 99f); st = St.Aim; }
                        break;
                    case St.Aim:
                        if (gated) { GetUp(gated); break; }
                        if (!engaged && now - lastEngaged > SniperStanceGate.Linger(Linger)) GetUp(false);
                        break;
                    case St.Fire:
                        if (gated) { GetUp(gated); break; }
                        if (t >= len * 0.95f) { Play("aim", 1f, 99f); st = St.Aim; }
                        break;
                    case St.GetUp:
                        if (t >= len * 0.9f) { st = St.Up; speed = 1f; }
                        break;
                }
                if (before != st) SsDebug?.Invoke($"{now:0.00} #{o.Id} {before} -> {st} {pose} (engaged {engaged}, walking {walking}, sim moving {o.IsMoving}, enemy in range {enemyInRange})");
                if (before == St.Up && st == St.Down)
                {
                    gate.WentDown(); lieDowns++; if (now - lastGetUp < 3f) cycles++;
                    if (enemyInRange) { lieDownsInRange++; if (!downThisFight) firstDownsInRange++; }
                    downThisFight = true;
                }
                else if (before != St.GetUp && st == St.GetUp) { lastGetUp = now; if (gated) { gate.GotUpForWalk(); getUpsWalk++; } else getUpsLinger++; if (enemyInRange) getUpsInRange++; }
                // UnitView.OnFire -> BS01Stance.OnFire: a drawn aimed shot (weapons_v2: at most one every 1.05 s)
                if (shot && now - lastDrawnShot >= Cadence)
                {
                    lastDrawnShot = now;
                    if (st == St.Aim || st == St.Fire || (st == St.Down && t >= len * 0.6f)) { Play("sniper_" + pose + "_fire", 1f); st = St.Fire; }
                }
                poseWeight = st != St.Up && (pose == "prone" || pose == "kneel") ? 1f : 0f;                 // MirrorExternal
            }

            void Play(string clip, float spd, float l = -1f) { t = 0f; len = l > 0 ? l : Len(clip); speed = spd; }
            void GetUp(bool hurry) { Play("sniper_" + pose + "_up", hurry ? HurryUp : 1f); st = St.GetUp; }

            /// <summary>Vector SmoothDamp (the engine's formula), 2D.</summary>
            static void SmoothDamp(ref double x, ref double z, double tx, double tz, ref double vx, ref double vz, double smooth, double maxSpeed, double dt)
            {
                smooth = Math.Max(0.0001, smooth);
                double omega = 2.0 / smooth, k = omega * dt, e = 1.0 / (1.0 + k + 0.48 * k * k + 0.235 * k * k * k);
                double cx = x - tx, cz = z - tz, ox = tx, oz = tz, maxC = maxSpeed * smooth, cm = cx * cx + cz * cz;
                if (cm > maxC * maxC) { double m = Math.Sqrt(cm); cx = cx / m * maxC; cz = cz / m * maxC; }
                tx = x - cx; tz = z - cz;
                double ttx = (vx + omega * cx) * dt, ttz = (vz + omega * cz) * dt;
                vx = (vx - omega * ttx) * e; vz = (vz - omega * ttz) * e;
                double outX = tx + (cx + ttx) * e, outZ = tz + (cz + ttz) * e;
                if ((ox - x) * (outX - ox) + (oz - z) * (outZ - oz) > 0) { outX = ox; outZ = oz; vx = (outX - ox) / dt; vz = (outZ - oz) / dt; }
                x = outX; z = outZ;
            }
        }

        // ------------------------------------------------------------------ one staged fight
        /// <summary>One staged fight (see the class summary): <paramref name="rule"/> = DV120 on / off, <paramref name="stance"/> = the K248b
        /// stance rules on / off; <paramref name="gx"/> = how far east the grunts stand (px), <paramref name="seed"/> = the world's seed.
        /// The fight lasts until the six staged grunts are dead (at most 40 s), then 4 s more are watched (the stance's get-up, walks off).</summary>
        internal static SsCounts SsFight(bool rule, bool stance, int gx, int seed)
        {
            var r = new SsCounts { fights = 1 };
            using (new SsSaved())
            {
                SsGameSwitches();
                ZWorld.SniperSteadyDeviation = rule; SniperStanceGate.On = stance;
                var w = ZWorld.Create(SettingsText, SsNoUnits(MapText("L02")), seed);
                w.Bots.Clear();
                foreach (var (tm, x) in new[] { (Team.Red, 16), (Team.Blue, (w.Map.width - 2) * 16) })
                { var d = w.CreateObject("robot", "grunt", x, 16, tm); d.MaxHealth = d.Health = 1000000000; d.Damage = 0; }   // keep-alive grunts far away (the match never ends)
                for (int k = 0, n = (seed * 5 + gx / 10) % 9; k < n; k++)
                { var d = w.CreateObject("robot", "grunt", 48 + 16 * k, 16, Team.Red); d.MaxHealth = d.Health = 1000000000; d.Damage = 0; }   // more of them, so the staged units' ids (the view's stance picks hash them) differ from fight to fight
                int bx = 41 * 16, by = 57 * 16;
                foreach (var o in w.Objects) if (o.Kind == ZKind.MapItem && o.Type == "grenades" && Math.Abs(o.CenterX - bx) < 400 && Math.Abs(o.CenterY - by) < 400) o.KillAt = 0.01;
                while (w.Time < 30.0) { w.Tick(ZConst.TICK); w.Events.Clear(); }
                var sq = w.CreateRobotGroup("sniper", bx, by, Team.Red);
                var g1 = w.CreateRobotGroup("grunt", bx + gx, by - 40, Team.Blue);
                var g2 = w.CreateRobotGroup("grunt", bx + gx + 15, by + 40, Team.Blue);
                var sn = new List<ZObject> { sq }; sn.AddRange(sq.Minions);
                var gr = new List<ZObject> { g1 }; gr.AddRange(g1.Minions); gr.Add(g2); gr.AddRange(g2.Minions);
                int heads0 = w.Headshots;
                var view = new Dictionary<int, SsView>(); var tr = new Dictionary<int, SsTrack>();
                foreach (var s in sn) { view[s.Id] = new SsView(); tr[s.Id] = new SsTrack(); }
                double t0 = w.Time, end = -1, stop = t0 + 40.0; bool ordered = false;
                while (w.Time < stop)
                {
                    if (!ordered)
                    {
                        ordered = true;
                        if (!sq.Destroyed) w.SendWaypoints(Team.Red, sq, new[] { new ZWaypoint { mode = WpMode.Attack, refId = g1.Id, x = g1.CenterX, y = g1.CenterY, playerGiven = true } });
                    }
                    w.Tick(ZConst.TICK);
                    var shotBy = new HashSet<int>();
                    foreach (var e in w.Events)
                    {
                        if (e.type == ZEventType.Hitscan && tr.ContainsKey(e.objId)) { shotBy.Add(e.objId); r.shots++; }
                        if (e.type == ZEventType.Death)
                        {
                            var v = w.GetAny(e.objId);
                            if (v != null && gr.Contains(v)) { r.grunts++; if (v.HeadshotBy >= 0) { r.headDeaths++; r.pops++; } }   // the view's Die -> HeadshotDeath -> HeadPop, once per such death
                        }
                    }
                    bool fight = end < 0;
                    foreach (var s in sn)
                    {
                        if (s.Destroyed || s.KillAt >= 0) continue;
                        bool inr = false;
                        foreach (var o in w.Objects)
                            if (o.IsUnit && !o.Destroyed && o.KillAt < 0 && o.Owner != Team.Neutral && o.Owner != s.Owner && s.CanAttackObject(o) && s.WithinAttackRadius(o)) { inr = true; break; }
                        var wc = w.Map.ClassAt(s.CenterX >> 4, s.CenterY >> 4); bool water = wc == ZClass.Water || wc == ZClass.DeepWater;
                        view[s.Id].Frame(s, (float)w.Time, (float)ZConst.TICK, shotBy.Contains(s.Id), water, inr && fight);
                        tr[s.Id].Tick(s, w.Time, inr, shotBy.Contains(s.Id), fight);
                        r.trace = SnsFnv(SnsFnv(SnsFnv(r.trace, s.LocX), s.LocY), s.AttackObject != null ? s.AttackObject.Id : -1);
                    }
                    w.Events.Clear();
                    if (end < 0 && gr.TrueForAll(o => o.Destroyed)) { end = w.Time; stop = Math.Min(stop, end + 4.0); }
                }
                if (end < 0) { r.unfinished = 1; end = w.Time; }
                r.fightSecs = end - t0;
                r.heads = w.Headshots - heads0;
                foreach (var s in sn)
                {
                    r.snipers++;
                    var k = tr[s.Id]; k.Close();
                    r.legs += k.legs; r.hops += k.hops; r.reversals += k.rev; r.reversalsInRange += k.revInRange; r.walksAfterShot += k.afterShot; r.walkBacks += k.walkBacks; r.agroLegs += k.agro; r.attackLegs += k.attack;
                    r.px += k.px; r.pxAfterFirst += k.pxAfterFirst; r.pxInRangeAfterFirst += k.pxInRangeAfterFirst; r.inRangeSecs += k.inRange; r.inRangeMovingSecs += k.inRangeMoving; r.autoCaptureLegs += k.autoCapture;
                    var v = view[s.Id];
                    r.lieDowns += v.lieDowns; r.lieDownsInRange += v.lieDownsInRange; r.firstDownsInRange += v.firstDownsInRange; r.getUpsWalk += v.getUpsWalk; r.getUpsLinger += v.getUpsLinger;
                    r.getUpsInRange += v.getUpsInRange; r.picks += v.picks; r.nudgesHeld += v.nudgesHeld; r.cycles += v.cycles;
                }
            }
            return r;
        }

        /// <summary>The sim side of one sniper in a fight: walking legs (a pause of 0.25 s or more ends one), what it walked on,
        /// reversals (a leg of 3 px or more heading more than 120 degrees away from the one before), pixels.</summary>
        sealed class SsTrack
        {
            public int legs, rev, revInRange, afterShot, walkBacks, agro, attack, autoCapture, hops;
            public double px, pxAfterFirst, pxInRangeAfterFirst, inRange, inRangeMoving;
            int lx = int.MinValue, ly, x0, y0, x1, y1; double lastT = -1, legEnd = -99, lastTarget = -99, firstShot = -1; bool inLeg, legInRange, legBack, legAgro, legAttack, legGrab, legFight, legAfterFirst;
            int pvx, pvy; bool pvInRange, havePrev;

            public void Tick(ZObject s, double t, bool inr, bool shot, bool fight)
            {
                if (shot && firstShot < 0) firstShot = t;
                double dt = lastT < 0 ? 0 : t - lastT; lastT = t;
                if (fight && inr) inRange += dt;
                if (lx == int.MinValue) { lx = s.CenterX; ly = s.CenterY; }
                bool moved = s.CenterX != lx || s.CenterY != ly;
                if (moved)
                {
                    double d = Math.Sqrt((double)(s.CenterX - lx) * (s.CenterX - lx) + (double)(s.CenterY - ly) * (s.CenterY - ly));
                    if (fight)
                    {
                        px += d; if (inr) inRangeMoving += dt;
                        if (firstShot >= 0) { pxAfterFirst += d; if (inr) pxInRangeAfterFirst += d; }
                    }
                    if (!inLeg || t - legEnd > 0.25)
                    {
                        if (inLeg) Close();
                        inLeg = true; x0 = lx; y0 = ly; legInRange = false; legBack = legAgro = legAttack = legGrab = false; legFight = fight; legAfterFirst = firstShot >= 0;
                        if (fight && t - lastTarget < 1.5) afterShot++;
                    }
                    x1 = s.CenterX; y1 = s.CenterY; legEnd = t; legInRange |= inr;
                    if (s.Waypoints.Count > 0)
                    {
                        var wp = s.Waypoints[0];
                        if (wp.mode == WpMode.Agro) { legAgro = true; if (SsStage(s) == "Return") legBack = true; }
                        else if (wp.mode == WpMode.Attack) legAttack = true;
                        else if (wp.mode == WpMode.Move && wp.attackTo && !wp.playerGiven && wp.refId >= 0) legGrab = true;
                    }
                }
                if (s.AttackObject != null) lastTarget = t;
                lx = s.CenterX; ly = s.CenterY;
            }

            public void Close()
            {
                if (!inLeg) return;
                inLeg = false;
                if (legFight)
                {
                    legs++; if (legBack) walkBacks++; if (legAgro) agro++; if (legAttack) attack++;
                    int vx = x1 - x0, vy = y1 - y0; double m = Math.Sqrt((double)vx * vx + (double)vy * vy);
                    if (legAfterFirst && m >= 4 && m < 16) hops++;
                    if (m >= 3)
                    {
                        if (havePrev)
                        {
                            double c = (vx * (double)pvx + vy * (double)pvy) / (m * Math.Sqrt((double)pvx * pvx + (double)pvy * pvy));
                            if (c < -0.5) { rev++; if (legInRange || pvInRange) revInRange++; }
                        }
                        pvx = vx; pvy = vy; pvInRange = legInRange; havePrev = true;
                    }
                }
                else if (legGrab) autoCapture++;
            }
        }

        // ------------------------------------------------------------------ the counted check
        internal static readonly int[] SsGx = { 150, 160, 180 };

        internal static SsCounts SsMatrix(bool rule, bool stance, int seeds)
        {
            var all = new SsCounts { fights = 0 };
            foreach (var gx in SsGx) for (int s = 1; s <= seeds; s++) all.Add(SsFight(rule, stance, gx, s));
            return all;
        }

        /// <summary>The K248b counted check: 3 distances x <paramref name="seeds"/> seeds of the staged fight, before (both rules off: the
        /// project) and after (both on), and each rule alone.</summary>
        internal static string SsReport(int seeds, out bool all)
        {
            var sb = new StringBuilder(); bool ok_ = true;
            void V(bool ok, string what) { sb.AppendLine((ok ? "PASS  " : "FAIL  ") + what); ok_ &= ok; }
            bool r0 = ZWorld.SniperSteadyDeviation, g0 = SniperStanceGate.On, h0 = ZWorld.SniperHeadshotDeviation;
            var before = SsMatrix(false, false, seeds);
            var after = SsMatrix(true, true, seeds);
            var again = SsMatrix(true, true, seeds);
            var simOnly = SsMatrix(true, false, seeds);
            var viewOnly = SsMatrix(false, true, seeds);
            sb.AppendLine($"before (the project: DV120 off, the old stance rules): {before.Text()}");
            sb.AppendLine($"after  (K248b: DV120 on, the new stance rules)       : {after.Text()}");
            sb.AppendLine($"only DV120 (the old stance rules)                    : {simOnly.Text()}");
            sb.AppendLine($"only the new stance rules (DV120 off)                : {viewOnly.Text()}");
            V(after.fights == SsGx.Length * seeds && after.unfinished == 0 && after.grunts == 6 * after.fights, $"S1 every staged fight ends with all six grunts dead ({after.grunts} of {6 * after.fights}; before {before.grunts})");
            V(after.fightSecs <= before.fightSecs * 1.2, $"S2 the snipers are not slower killers: {after.fightSecs:0.0} s of fighting, before {before.fightSecs:0.0} s (at most +20 %)");
            V(after.reversals == 0 && after.reversalsInRange == 0, $"S3 no back and forth: {after.reversals} direction reversals ({after.reversalsInRange} with an enemy in range); before {before.reversals} ({before.reversalsInRange})");
            V(after.walkBacks == 0, $"S4 no agro walk-back: {after.walkBacks} (before {before.walkBacks})");
            V(after.Flips == 0 && after.cycles == 0, $"S5 no lying down / standing up while an enemy is in range: {after.Flips} stance flips ({after.getUpsInRange} get-ups with an enemy in range), {after.cycles} down-up-down cycles; before {before.Flips} flips ({before.getUpsInRange} get-ups), {before.cycles} cycles");
            V(after.picks <= after.snipers * 1.1, $"S6 one stance per fight: {after.picks} stance picks for {after.snipers} sniper-fights (a new pick only after {SniperStanceGate.FightGap:0} s without a target); before {before.picks}");
            V(after.lieDowns <= after.picks, $"S7 down at most once a fight: {after.lieDowns} lie-downs in {after.picks} fights (stance picks) of {after.snipers} sniper-fights; before {before.lieDowns} in {before.picks}");
            V(after.hops * 2 <= before.hops && after.legs < before.legs, $"S8 fewer, longer walks: {after.legs} walking legs (before {before.legs}), {after.hops} short steps of 0.5-2 m after the first shot (before {before.hops}; at most half)");
            V(after.heads > 0 && after.headDeaths == after.heads && after.pops == after.headDeaths, $"S9 one head pop per headshot: {after.pops} pops, {after.headDeaths} headshot deaths, {after.heads} headshots of the rule");
            V(again.trace == after.trace && again.Text() == after.Text(), "S10 the same fights twice: the same positions and targets every tick (deterministic)");
            V(ZWorld.SniperSteadyDeviation == r0 && SniperStanceGate.On == g0 && ZWorld.SniperHeadshotDeviation == h0, "S11 the switches are put back after the cases");
            sb.AppendLine($"(for the record: px walked after the first shot {after.pxAfterFirst:0}, before {before.pxAfterFirst:0}: fewer walks, each one deeper in; of it with an enemy in range {after.pxInRangeAfterFirst:0}, before {before.pxInRangeAfterFirst:0}; walks within 1.5 s of shooting {after.walksAfterShot}, before {before.walksAfterShot})");
            all = ok_;
            sb.AppendLine(all ? "ALL PASS" : "NOT ALL PASS");
            return sb.ToString();
        }

        static Deviation DevSniperSteady()
        {
            string measured; bool pass;
            try
            {
                var off = SsMatrix(false, true, 2); var on = SsMatrix(true, true, 2);
                pass = on.unfinished == 0 && on.reversals == 0 && on.walkBacks == 0 && on.legs < off.legs && on.hops < off.hops && on.Flips == 0 && on.fightSecs <= off.fightSecs * 1.25;
                measured = $"{on.fights} staged fights of 3 snipers against 6 grunts (the new stance rules on in both); rule off: {off.legs} walking legs, {off.hops} short steps, {off.reversals} reversals, {off.walkBacks} walk-backs, " +
                           $"{off.Flips} stance flips, {off.fightSecs:0.0} s of fighting; rule on: {on.legs} legs, {on.hops} short steps, {on.reversals} reversals, {on.walkBacks} walk-backs, {on.Flips} stance flips, {on.fightSecs:0.0} s";
            }
            catch (Exception ex) { measured = "the probe threw " + ex.GetType().Name + ": " + ex.Message; pass = false; }
            return new Deviation
            {
                id = "DV120", change = "Snipers stand and fire: no walk past an enemy in range, no walk back",
                zod = "a walking robot neither fires nor takes a new target (zobject.cpp:1615); an agro walk returns to where it began when its target leaves the leash (zobject.cpp:2233-2328) and stops at the edge of the range",
                openz = "Sim/ZObject.SniperSteady.cs: a sniper on foot on an agro / help walk or on the approach of an attack stops for an enemy it can kill inside its range (a robot, a vehicle or gun with a crew it can snipe): " +
                        "16 px inside its range (at once when it comes nearer): the agro walk ends, the attack waits and goes on afterwards (its own target first); an agro walk never turns back; " +
                        "while its squad fired in the last second and an enemy stands within 16 px outside its range it keeps its spot (no creep after each kill). " +
                        "The player's move, enter, crate, fort and repair orders and every step aside are never held. View (K248b): one stance per fight, no get-up for a nudge (View/SniperStanceGate.cs); down only where the fight needs no more walking (K248d: Sim/ZObject.SniperSettle.cs)",
                measured = measured,
                toggle = "ZWorld.SniperSteadyDeviation: View/ZGame.cs sets it at every start (off in 'Zod exact'); off in headless worlds and in the version 1.1 counted checks unless they set it",
                status = pass ? DevActive : DevCheck,
            };
        }
    }

#if UNITY_EDITOR
    /// <summary>K248b menus: the counted check (outside Play) and, in Play, the counters of the stance and the head pop.</summary>
    public static class OzV11SniperSteadyCheck
    {
        [UnityEditor.MenuItem("OpenZ/V1.1/Sniper: K248b Steady Check (18 fights)")]
        static void Menu() => OzV11Kit.RunMenu("Sniper: K248b Steady Check (18 fights)", () => OpenZParity.SsReport(6, out _));

        /// <summary>A/B look in Play: the snipers' rules of before (DV120 off, the old stance rules) or K248b's; the next stage shows them.</summary>
        [UnityEditor.MenuItem("OpenZ/V1.1/Sniper: K248b Old or new sniper rules (Play, A-B)")]
        static void Toggle()
        {
            bool now = !(ZWorld.SniperSteadyDeviation && SniperStanceGate.On);
            ZWorld.SniperSteadyDeviation = now; SniperStanceGate.On = now;
            UnityEngine.Debug.Log($"[OpenZ V1.1] K248b: snipers now play by the {(now ? "NEW rules (DV120 + one stance per fight)" : "OLD rules (as before K248b; for comparison only)")}. " +
                                  "The next Play session starts with the new rules again.");
        }

        [UnityEditor.MenuItem("OpenZ/V1.1/Sniper: K248b Report stance and head pop (Play)")]
        static void Counters()
        {
            var sb = new StringBuilder();
            var w = ZGame.I ? ZGame.I.World : null;
            sb.AppendLine($"[OpenZ V1.1] K248b: DV120 {(ZWorld.SniperSteadyDeviation ? "ON" : "off")}, stance rules {(SniperStanceGate.On ? "ON" : "off")}, headshots {(ZWorld.SniperHeadshotDeviation ? "ON" : "off")} (since the last stage)");
            sb.AppendLine($"  sim: agro walks ended by an enemy in range {ZWorld.SniperAgroEnded}, attacks held {ZWorld.SniperAttackHeld}, walk-backs not walked {ZWorld.SniperNoReturn}, close-in ticks {ZWorld.SniperCloseIns}, stand ticks {(w != null ? w.SniperStands : 0)}");
            sb.AppendLine($"  drawn stance: lie-downs {SniperStanceGate.LieDowns}, get-ups for a walk {SniperStanceGate.GetUpsWalk} (with an enemy in range {SniperStanceGate.GetUpsWalkInRange}), after the fight {SniperStanceGate.GetUpsLinger}, " +
                          $"for a grenade {SniperStanceGate.GetUpsThrow}; drawn walks that did not get it up {SniperStanceGate.NudgesHeld}; stance picks {SniperStanceGate.Picks}");
            foreach (var kv in BS01Stance.SteadyCounts)
                sb.AppendLine($"    sniper #{kv.Key}: lie-downs {kv.Value.x}, get-ups for a walk {kv.Value.y}, get-ups after the fight {kv.Value.z}");
            sb.AppendLine($"  head: headshot deaths drawn {UnitView.HeadshotDeaths - OzV11SniperSteadyStage.Deaths0}, head pops {UnitView.HeadPops - OzV11SniperSteadyStage.Pops0}, heads thrown {UnitView.HeadsThrown - OzV11SniperSteadyStage.Thrown0}, " +
                          $"can / head clatters {AnticCan.Clatters - OzV11SniperSteadyStage.Clatters0}, calls {UnitView.HeadCalls - OzV11SniperSteadyStage.Calls0}");
            UnityEngine.Debug.Log(sb.ToString());
        }
    }

    /// <summary>K248b: the K248 stage, made a clean take; K248c: its place by the rules' own distances (OzSniperStagePlace,
    /// OpenZParity.V11SniperStagePlace.cs): dry open ground where the units stand and walk with a clear line of fire between the squads,
    /// no flag, crate or empty vehicle in auto-grab reach of a squad leader that can stand idle (the snipers after the fight), no other
    /// unit or fort in reach of the fight; near the middle of the screen, else the level's known places (the camera flies there).
    /// Both grunt squads stand 150 px east, inside a grunt's agro reach. The stage's own units of a run before are taken away first
    /// (never two trios); the snipers are ordered to attack the first squad's leader (the player's order, as before). Counters start
    /// again at each stage.</summary>
    public static class OzV11SniperSteadyStage
    {
        static readonly List<int> staged = new List<int>();
        internal static int Deaths0, Pops0, Thrown0, Clatters0, Calls0;

        /// <summary>The menu OpenZ/V1.1/Sniper: K248 Stage headshots (Play) runs this. Always true (staged, or why not in the console).</summary>
        public static bool Run()
        {
            var g = ZGame.I;
            if (!UnityEngine.Application.isPlaying || g == null || g.World == null) { UnityEngine.Debug.LogWarning("[OpenZ V1.1] K248 stage: start a level in Play first."); return true; }
            var w = g.World; var cam = UnityEngine.Camera.main;
            if (!cam) { UnityEngine.Debug.LogWarning("[OpenZ V1.1] K248 stage: no main camera."); return true; }
            int removed = 0;
            foreach (var id in staged) { var o = w.GetAny(id); if (o != null && !o.Destroyed && o.KillAt < 0) { o.KillAt = w.Time; removed++; } }
            staged.Clear();
            var ray = cam.ViewportPointToRay(new UnityEngine.Vector3(0.5f, 0.5f, 0f));
            var c = cam.transform.position; var plane = new UnityEngine.Plane(UnityEngine.Vector3.up, new UnityEngine.Vector3(0f, g.GroundY(c.x, c.z), 0f));
            if (!plane.Raycast(ray, out float dist)) { UnityEngine.Debug.LogWarning("[OpenZ V1.1] K248 stage: the camera does not look at the ground."); return true; }
            var px = ZGame.WorldToPx(ray.GetPoint(dist));
            // K248c: the place by the rules' own distances (OzSniperStagePlace.Find): near the middle of the screen, else the level's known places;
            // K322: first the clean clip stage (OzClipStage.Start: the computer players out, every factory stopped, the units that are not the stage's
            // taken away, held so for the take), then K248c's tests plus the clear view (OzClipStagePlace.Find: nothing tall in front of a staged unit)
            if (!OzClipStage.Start(g, px.x, px.y, "K248 sniper stage")) return true;
            if (!OzClipStagePlace.Find(OzClipStagePlace.Kind.Sniper, w, (px.x >> 4) - 5, px.y >> 4, 14, g.player, out int stx, out int sty, out _, out _, out string placeNote))
            { UnityEngine.Debug.LogWarning($"[OpenZ V1.1] K248 stage: no place for a clean fight near the middle of the screen or at this level's known places: {placeNote}.{(removed > 0 ? $" ({removed} units of the stage before were taken away.)" : "")}"); return true; }
            int bx = stx * 16, by = sty * 16;
            if (Math.Abs(bx + 83 - px.x) > 64 || Math.Abs(by + 8 - px.y) > 64) { var rts = UnityEngine.Object.FindFirstObjectByType<RtsCamera>(); if (rts) rts.FlyTo(ZGame.PxToWorld(bx + 83, by + 8)); }   // K248c: the place is off the middle of the screen: the camera flies there (same zoom)
            var enemy = g.player == Team.Red ? Team.Blue : Team.Red;
            ZWorld.SniperHeadshotDeviation = true;
            var snipers = w.CreateRobotGroup("sniper", bx, by, g.player);
            var g1 = w.CreateRobotGroup("grunt", bx + OzSniperStagePlace.G1X, by + OzSniperStagePlace.G1Y, enemy);
            var g2 = w.CreateRobotGroup("grunt", bx + OzSniperStagePlace.G2X, by + OzSniperStagePlace.G2Y, enemy);   // K248c: 150 px east like the first (was 165: idle out of its agro reach, it walked off)
            foreach (var o in new[] { snipers, g1, g2 }) if (o != null) { staged.Add(o.Id); foreach (var m in o.Minions) staged.Add(m.Id); }
            OzClipStage.Stage(g, "K248 sniper stage", snipers, g1, g2);   // K322: the staged units' views (ZGame.SpawnUnitView, as for the game's own units made in Play), checked; the hold keeps them
            SniperStanceGate.ResetCounters(); BS01Stance.SteadyCounts.Clear();
            ZWorld.SniperAgroEnded = ZWorld.SniperAttackHeld = ZWorld.SniperNoReturn = ZWorld.SniperCloseIns = 0;
            Deaths0 = UnitView.HeadshotDeaths; Pops0 = UnitView.HeadPops; Thrown0 = UnitView.HeadsThrown; Clatters0 = AnticCan.Clatters; Calls0 = UnitView.HeadCalls;
            if (snipers != null && g1 != null) w.SendWaypoints(g.player, snipers, new[] { new ZWaypoint { mode = WpMode.Attack, refId = g1.Id, x = g1.CenterX, y = g1.CenterY, playerGiven = true } });
            UnityEngine.Debug.Log($"[OpenZ V1.1] K248 stage (K248b): {(snipers != null ? 1 + snipers.Minions.Count : 0)} snipers of {g.player} at tile {bx >> 4},{by >> 4} on dry open ground, two {enemy} grunt squads 9 tiles east (K248c place: {placeNote}); " +
                                  $"ordered to attack the first squad; headshots ON, DV120 {(ZWorld.SniperSteadyDeviation ? "ON" : "off")}, stance rules {(SniperStanceGate.On ? "ON" : "off")}{(removed > 0 ? $"; {removed} units of the stage before taken away" : "")}. " +
                                  "After the fight: menu OpenZ/V1.1/Sniper: K248b Report stance and head pop (Play).");
            return true;
        }

        // K248c: the place test is OzSniperStagePlace.Check (OpenZParity.V11SniperStagePlace.cs); K248b's own (Fits: 260 px round two points,
        // the whole box dry) found no place on Level 2 or Level 16 (the kit's census, out/census.txt)
    }
#endif
}
#endif
