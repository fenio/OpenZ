using System;
using System.Collections.Generic;
using System.Text;
#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;
#endif

namespace OpenZ.EditorTools
{
#if !OPENZ_HARNESS
    /// <summary>
    /// The K256d menus (the shuttle's night lights, rule DV118, view only): the Play proof on level 16 (View/OzShuttleNightProof.cs:
    /// acceptance line 21, pictures and counted lines) and the edit-mode counts of the lights' rules (<see cref="OzShuttleNightCounts"/>,
    /// the same counts also run outside the editor). The proof's output folder: OzShuttleNightProof.Folder(), built at run time.
    /// </summary>
    public static class OzShuttleNightMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K256d Night Lights Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K256d proof: enter Play on level 16 first"); return; }
            OzShuttleNightProof.Run();
            Debug.Log("[OpenZ] K256d night lights proof started; its report: OzShuttleNightProof.Report, the logs in " + OzShuttleNightProof.Folder());
        }

        [MenuItem("OpenZ/V1.1/Shuttle: K256d Night Lights Counts (edit mode)")]
        static void Counts()
        {
            var text = OzShuttleNightCounts.Run(out int fails, out int passes);
            Debug.Log("[OpenZ] K256d night lights counts: " + (fails == 0 ? "PASS" : "FAIL " + fails) + " (" + passes + " PASS)\n" + text);
            try
            {
                var dir = OzShuttleNightProof.Folder(); System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "counts_edit_mode.txt"), text);
            }
            catch (Exception ex) { Debug.LogWarning("[OpenZ] K256d counts: " + ex.Message); }
        }
    }
#endif

    /// <summary>
    /// The counted rules of the shuttle's night lights (OzShuttleNightPlan: pure numbers, no scene), run at 60 frames a second through
    /// made-up flights: the lights' states per flight state, by day and by night; the easing with the night share (dusk, dawn, a sudden
    /// change); the lock taking the nearest enemy in reach and letting go; the alarm, its pulse and the beep; CALM MODE without a pulse;
    /// the sweep never stuck; LOW's light count and its shadows. Also run outside the editor (the same file).
    /// </summary>
    public static class OzShuttleNightCounts
    {
        const float Dt = 1f / 60f;
        static StringBuilder sb; static int fails, passes;

        static void Check(string what, bool ok) { if (ok) passes++; else fails++; sb.AppendLine((ok ? "PASS  " : "FAIL  ") + what); }
        static void Note(string what) => sb.AppendLine("      " + what);

        /// <summary>A made-up flight: a plan, its frame input, the enemies around it (as the view would measure them).</summary>
        sealed class Run1
        {
            public readonly OzShuttleNightPlan P = new OzShuttleNightPlan();
            public OzShuttleNightPlan.Frame F = new OzShuttleNightPlan.Frame { Alive = true, GoalDistM = -1f };
            public readonly List<OzShuttleNightPlan.Seen> Enemies = new List<OzShuttleNightPlan.Seen>();
            public float T; public int Beeps;
            /// <summary>The game speed (1 = normal): game seconds per real second.</summary>
            public float Speed = 1f;
            public void Step()
            {
                F.Dt = Dt * Speed; F.RealDt = Dt;
                if (P.LockId >= 0)
                {
                    int k = Enemies.FindIndex(e => e.Id == P.LockId);
                    P.LockSeen = k >= 0;
                    if (k >= 0) { P.LockDistM = Enemies[k].DistM; P.LockYawDeg = Enemies[k].YawDeg; P.LockPitchDeg = Enemies[k].PitchDeg; }
                }
                if (P.ScanDue) { P.Candidates.Clear(); P.Candidates.AddRange(Enemies); }
                P.Step(F); T += Dt;
                if (P.BeepNow) Beeps++;
            }
            public void For(float sec, Action each = null) { int n = (int)Math.Round(sec / Dt); for (int i = 0; i < n; i++) { Step(); each?.Invoke(); } }
            public void Landed() { F.State = OzShuttleNightPlan.Landed; F.Airborne = false; F.FeetM = 0f; }
            public void Fly(int state, float feet, float goal) { F.State = state; F.Airborne = true; F.FeetM = feet; F.GoalDistM = goal; }
        }

        static OzShuttleNightPlan.Seen E(int id, float dist, float yaw, float pitch) => new OzShuttleNightPlan.Seen { Id = id, DistM = dist, YawDeg = yaw, PitchDeg = pitch };

        /// <summary>A whole flight: 1 s standing, a 2 s climb to 12 m, 10 s flying in from 80 m, a 2 s descent, 1 s standing, the ramp down
        /// (1.5 s), 3 s open, up (1.5 s), 3 s standing. Records each light's level per phase.</summary>
        static Dictionary<string, float[]> Flight(Run1 r)
        {
            var rec = new Dictionary<string, float[]>();
            void Rec(string k) { var p = r.P; rec[k] = new[] { p.Landing, p.Ramp, p.Nose, p.LandingAtGoal ? 1f : 0f, p.Alarm }; }
            void Max(string k) { var p = r.P; if (!rec.TryGetValue(k, out var m)) rec[k] = m = new float[5]; m[0] = Math.Max(m[0], p.Landing); m[1] = Math.Max(m[1], p.Ramp); m[2] = Math.Max(m[2], p.Nose); m[3] = Math.Max(m[3], p.LandingAtGoal ? 1f : 0f); m[4] = Math.Max(m[4], p.Alarm); }
            r.Landed(); r.F.RampOpen01 = 0f; r.For(1f, () => Max("stand"));
            for (int i = 0; i < 120; i++) { float k = (i + 1) / 120f; r.Fly(OzShuttleNightPlan.Climb, 12f * k, 80f); r.Step(); Max("climb"); if (r.F.FeetM > 4f && r.F.FeetM < 7f) Max("climb_low"); }
            for (int i = 0; i < 600; i++) { float k = (i + 1) / 600f; float goal = 80f * (1f - k); r.Fly(OzShuttleNightPlan.Cruise, 12f, goal); r.Step(); if (goal > 40f && i > 60) Max("cruise_far"); if (goal < 20f) Max("approach"); }
            Rec("approach_end");
            for (int i = 0; i < 120; i++) { float k = (i + 1) / 120f; r.Fly(OzShuttleNightPlan.Descend, 12f * (1f - k), 0f); r.Step(); if (r.F.FeetM < 10f && r.F.FeetM > 4f) Max("descend"); if (r.F.FeetM < 1.5f) Max("descend_last"); }
            r.Landed(); r.F.GoalDistM = 0f;
            r.For(0.5f); Rec("touch_0_5");
            r.For(1.7f); Rec("touch_2_2");
            for (int i = 0; i < 90; i++) { r.F.RampOpen01 = (i + 1) / 90f; r.Step(); }
            r.For(0.6f); Rec("ramp_open_0_6"); r.For(2.4f, () => Max("ramp_open"));
            for (int i = 0; i < 90; i++) { r.F.RampOpen01 = 1f - (i + 1) / 90f; r.Step(); }
            r.F.RampOpen01 = 0f; r.For(1.0f); Rec("ramp_shut_1_0");
            r.For(2f, () => Max("after"));
            return rec;
        }

        public static string Run(out int failCount, out int passCount)
        {
            sb = new StringBuilder(); fails = passes = 0;
            sb.AppendLine("K256d + K256e the shuttle's night lights: the rules' counts (OzShuttleNightPlan; 60 frames a second)");

            // ---- 1. by day: a whole flight with an enemy beside its way: nothing burns, nothing is followed
            {
                var r = new Run1(); r.F.NightK = 0f; r.Enemies.Add(E(1, 15f, 10f, 35f));
                var rec = Flight(r); float worst = 0f; foreach (var v in rec.Values) for (int i = 0; i < 5; i++) if (i != 3) worst = Math.Max(worst, v[i]);
                Note($"by day (night share 0): the brightest any light came {worst:F3}; enemies followed {r.P.Acquires}, beeps {r.Beeps}");
                Check("1 by day no light burns in any state (standing, climb, flight, descent, touchdown, ramp down) and nothing is followed", worst == 0f && r.P.Acquires == 0 && r.Beeps == 0);
            }
            // ---- 2. at night: each light in its states
            {
                var r = new Run1(); r.F.NightK = 1f;
                var rec = Flight(r);
                string L(string k) { var v = rec[k]; return $"{k}: landing {v[0]:F2} ramp {v[1]:F2} nose {v[2]:F2} at-goal {v[3]:F0}"; }
                foreach (var k in new[] { "stand", "climb_low", "cruise_far", "approach", "descend", "descend_last", "touch_0_5", "touch_2_2", "ramp_open_0_6", "ramp_open", "ramp_shut_1_0", "after" }) Note(L(k));
                Check("2a standing at night before the flight: all dark", rec["stand"][0] == 0f && rec["stand"][1] == 0f && rec["stand"][2] == 0f);
                Check("2b the take-off, low (4-7 m): the landing lights on (>= 0.95)", rec["climb_low"][0] >= 0.95f);
                Check("2c flying at cruise height far from the landing point: the landing lights off (<= 0.01), the nose searchlight on (>= 0.99)", rec["cruise_far"][0] <= 0.01f && rec["cruise_far"][2] >= 0.99f);
                Check("2d flying in, the last 24 m: the landing lights on and on the landing point", rec["approach"][0] >= 0.95f && rec["approach"][3] == 1f && rec["approach_end"][0] >= 0.99f);
                Check("2e coming down: the landing lights on the landing point (>= 0.99); the nose searchlight goes out in the last metres", rec["descend"][0] >= 0.99f && rec["descend"][3] == 1f && rec["descend_last"][2] < rec["descend"][2]);
                Check("2f just landed (0.5 s): the landing lights still on, the nose searchlight out", rec["touch_0_5"][0] >= 0.99f && rec["touch_0_5"][2] <= 0.01f);
                Check("2g 2.2 s after the touchdown: the landing lights out (eased off)", rec["touch_2_2"][0] <= 0.001f);
                Check("2h the ramp down (the squad walking): the ramp light on (>= 0.95 0.6 s after it is fully down), only then", rec["ramp_open_0_6"][1] >= 0.95f && rec["climb_low"][1] == 0f && rec["cruise_far"][1] == 0f && rec["descend"][1] == 0f);
                Check("2i 1 s after the ramp has shut: the ramp light out; standing afterwards: all dark", rec["ramp_shut_1_0"][1] <= 0.001f && rec["after"][0] == 0f && rec["after"][1] == 0f && rec["after"][2] == 0f);
            }
            // ---- 3. easing with the night share
            {
                var r = new Run1(); r.Fly(OzShuttleNightPlan.Cruise, 12f, -1f);
                float worstStep = 0f, worstLead = 0f, worstLag = 0f; int downs = 0; float last = 0f;
                for (int i = 0; i <= 600; i++)
                {
                    r.F.NightK = i / 600f * 0.6f; r.Step();
                    float g = OzShuttleNightPlan.Gate(r.F.NightK), n = r.P.Nose;
                    worstStep = Math.Max(worstStep, Math.Abs(n - last)); worstLead = Math.Max(worstLead, n - g); worstLag = Math.Max(worstLag, g - n);
                    if (n < last - 1e-6f) downs++; last = n;
                }
                Note($"dusk (night share 0 -> 0.6 over 10 s): largest step a frame {worstStep:F4}, ahead of the night gate by at most {worstLead:F4}, behind by at most {worstLag:F4}, steps down {downs}, at the end {r.P.Nose:F3}");
                Check("3a dusk: the lights come up with the night share, smoothly (largest step a frame <= 0.05), never ahead of it, never down, fully on at night", worstStep <= 0.05f && worstLead <= 1e-4f && worstLag <= 0.05f && downs == 0 && r.P.Nose >= 0.999f);
                worstStep = 0f; int ups = 0; last = r.P.Nose;
                for (int i = 0; i <= 480; i++) { r.F.NightK = 0.6f * (1f - i / 480f); r.Step(); worstStep = Math.Max(worstStep, Math.Abs(r.P.Nose - last)); if (r.P.Nose > last + 1e-6f) ups++; last = r.P.Nose; }
                r.For(0.7f);
                Note($"dawn (0.6 -> 0 over 8 s): largest step a frame {worstStep:F4}, steps up {ups}, by day {r.P.Nose:F3}");
                Check("3b dawn: the lights go down with the night share, smoothly, never up, out by day", worstStep <= 0.05f && ups == 0 && r.P.Nose == 0f);
                r.F.NightK = 1f; r.For(0.1f); float at01 = r.P.Nose; r.For(1.0f); float at11 = r.P.Nose; r.For(1.3f); float at24 = r.P.Nose;
                r.F.NightK = 0f; r.For(0.3f); float off03 = r.P.Nose; r.For(0.35f); float off065 = r.P.Nose;
                Note($"a sudden night: {at01:F2} after 0.1 s, {at11:F2} after 1.1 s, {at24:F2} after 2.4 s; a sudden day: {off03:F2} after 0.3 s, {off065:F2} after 0.65 s");
                Check("3c a sudden change of the clock never pops: on over 2 s (K256e: <= 0.06 after 0.1 s, about half after 1.1 s, full by 2.4 s), off over 0.6 s", at01 <= 0.06f && at11 >= 0.4f && at11 <= 0.6f && at24 >= 0.999f && off03 >= 0.45f && off065 == 0f);
                // K256e (FIX_LIST M14 N2): a quick dusk (the night share 0 -> 1 in 1 s): the lights follow it over at least 2 s, at most 0.5 a second
                var q = new Run1(); q.Fly(OzShuttleNightPlan.Cruise, 12f, -1f); float qMax = 0f, qLast = 0f, qT = 0f, q05 = -1f, q95 = -1f;
                for (int i = 0; i <= 360; i++)
                {
                    q.F.NightK = Math.Min(1f, i / 60f); q.Step(); qT += Dt;
                    float n = q.P.Nose; qMax = Math.Max(qMax, (n - qLast) / Dt); qLast = n;
                    if (q05 < 0f && n >= 0.05f) q05 = qT; if (q95 < 0f && n >= 0.95f) q95 = qT;
                }
                Note($"a quick dusk (the night share 0 -> 1 in 1 s): fastest {qMax:F2} a second, 5 % -> 95 % in {(q95 - q05):F2} s");
                Check("3e a quick dusk: the lights come up over at least 2 s (at most 0.5 a second: 0 -> 1 in 2 s, 5 % -> 95 % in >= 1.75 s)", qMax <= 0.5f + 1e-3f && q95 - q05 >= 1.75f);
                Check("3d the gate: 0 by day, full from the night share " + OzShuttleNightPlan.FullAtNight.ToString("F2"), OzShuttleNightPlan.Gate(0f) == 0f && OzShuttleNightPlan.Gate(OzShuttleNightPlan.FullAtNight) >= 0.999f && OzShuttleNightPlan.Gate(1f) >= 0.999f && OzShuttleNightPlan.Gate(0.1f) > 0f);
            }
            // ---- 4. the lock: the nearest enemy in reach; letting go
            {
                var r = new Run1(); r.F.NightK = 1f; r.Fly(OzShuttleNightPlan.Cruise, 12f, -1f); r.For(2f);
                r.Enemies.Add(E(11, 20f, -40f, 30f));      // in reach
                r.Enemies.Add(E(12, 12f, 20f, 42f));       // in reach, the nearest
                r.Enemies.Add(E(13, 40f, 0f, 15f));        // out of reach
                r.Enemies.Add(E(14, 9f, 150f, 50f));       // behind the ship: out of the nose's turn
                float t0 = r.T; float lockAt = -1f, onAt = -1f; int beeps0 = r.Beeps;
                r.For(1.5f, () => { if (lockAt < 0f && r.P.LockId >= 0) lockAt = r.T - t0; if (onAt < 0f && r.P.Locked) onAt = r.T - t0; });
                Note($"enemies 11 (20 m), 12 (12 m), 13 (40 m), 14 (9 m behind): taken {r.P.LockId} after {lockAt:F2} s, the beam on it after {onAt:F2} s, beeps {r.Beeps - beeps0}");
                Check("4a it takes the nearest enemy in reach (12: not 14 behind it, not 13 out of reach) within a look round (<= 0.3 s)", r.P.LockId == 12 && lockAt >= 0f && lockAt <= 0.3f);
                Check("4b the beam comes onto it within 1 s, with one beep", onAt > 0f && onAt <= 1.0f + 1e-3f && r.Beeps - beeps0 == 1);
                r.For(0.4f);
                r.P.Colour(out float cr, out float cg, out float cb);
                Note($"alarming: alarm {r.P.Alarm:F2}, cone {r.P.AngleDeg:F1} deg (calm {OzShuttleNightPlan.NoseAngleDeg:F0}), edge {r.P.InnerShare:F2}, colour {cr:F2} {cg:F2} {cb:F2}, drum {r.P.YawNow:F1} / {r.P.PitchNow:F1} deg (the enemy 20 / 42)");
                Check("4c on it: ALARMING (alarm >= 0.99): narrower (11 deg), a harder edge, amber-red (1.0, 0.45, 0.05); the drum on the enemy (within 1 deg)", r.P.Alarm >= 0.99f && r.P.AngleDeg <= OzShuttleNightPlan.AlarmAngleDeg + 0.01f && r.P.InnerShare >= 0.79f && cr >= 0.99f && cg >= 0.44f && cg <= 0.46f && cb <= 0.06f && Math.Abs(r.P.YawNow - 20f) <= 1f && Math.Abs(r.P.PitchNow - 42f) <= 1f);
                // the enemy moves: the beam follows
                for (int i = 0; i < 120; i++) { r.Enemies[1] = E(12, 12f + i * 0.05f, 20f - i * 0.3f, 42f - i * 0.1f); r.Step(); }
                r.For(0.5f);
                Note($"it walks 6 m and 36 deg round: the drum at {r.P.YawNow:F1} deg (the enemy {r.Enemies[1].YawDeg:F1}), still on it {r.P.LockId == 12}");
                Check("4d the beam follows it as it moves (within 2 deg after 0.5 s)", r.P.LockId == 12 && Math.Abs(r.P.YawNow - r.Enemies[1].YawDeg) <= 2f);
                // destroyed: let go, the pause, then the next nearest
                int rel0 = r.P.Releases; r.Enemies.RemoveAt(1); r.Step();
                bool letGo = r.P.LockId == -1 && r.P.Releases == rel0 + 1; float t1 = r.T, nextAt = -1f;
                r.For(1.5f, () => { if (nextAt < 0f && r.P.LockId >= 0) nextAt = r.T - t1; });
                Note($"12 destroyed: let go at once {letGo}; then took {r.P.LockId} after {nextAt:F2} s (the pause {OzShuttleNightPlan.CooldownSec:F1} s)");
                Check("4e the enemy destroyed: it lets go at once, and after the short pause takes the next nearest in reach (11)", letGo && r.P.LockId == 11 && nextAt >= OzShuttleNightPlan.CooldownSec - 0.02f && nextAt <= OzShuttleNightPlan.CooldownSec + 0.3f);
                r.For(1f);
                // out of reach: kept a little beyond (no flicker at the edge), let go past it
                int k11 = r.Enemies.FindIndex(e => e.Id == 11);
                r.Enemies[k11] = E(11, 33f, -40f, 20f); r.For(0.5f); bool kept = r.P.LockId == 11;
                r.Enemies[k11] = E(11, 36f, -40f, 18f); r.Step(); bool gone = r.P.LockId == -1;
                Note($"11 at 33 m (reach {OzShuttleNightPlan.LockRangeM:F0} m x {OzShuttleNightPlan.KeepRangeK:F2}): kept {kept}; at 36 m: let go {gone}");
                Check("4f out of reach: kept to 1.15 x the reach (no flicker at the edge), let go past it", kept && gone);
                r.Enemies.Clear(); float a1 = 0f; r.For(0.85f); a1 = r.P.Alarm; r.P.Colour(out cr, out cg, out cb);
                float y0 = r.P.YawNow; float ymin = y0, ymax = y0; r.For(4.5f, () => { ymin = Math.Min(ymin, r.P.YawNow); ymax = Math.Max(ymax, r.P.YawNow); });
                Note($"nobody left: alarm {a1:F3} after 0.85 s, colour {cr:F2} {cg:F2} {cb:F2}, cone {r.P.AngleDeg:F1}; the sweep again: the drum {ymin:F1} .. {ymax:F1} deg in 4.5 s");
                Check("4g the enemy gone: back to the calm white sweep (alarm 0 in 0.85 s, the white colour, the wide cone, the drum sweeping again)", a1 <= 1e-4f && cr <= 0.87f && cg >= 0.91f && cb >= 0.99f && r.P.AngleDeg >= OzShuttleNightPlan.NoseAngleDeg - 0.01f && ymax - ymin >= 25f);
                Note($"counts: taken {r.P.Acquires}, beams on them {r.P.LockOns}, beeps {r.Beeps}, let go {r.P.Releases}");
                Check("4h counts: 2 enemies taken, 2 beams on them = 2 beeps, 2 let go (one destroyed, one out of reach)", r.P.Acquires == 2 && r.P.LockOns == 2 && r.Beeps == 2 && r.P.Releases == 2);
            }
            // ---- 5. the warning pulse, and CALM MODE
            foreach (bool calm in new[] { false, true })
            {
                var r = new Run1(); r.F.NightK = 1f; r.F.Calm = calm; r.Fly(OzShuttleNightPlan.Hover, 12f, 0f); r.For(1f);
                r.Enemies.Add(E(21, 14f, 10f, 40f)); r.For(2f);
                float lo = 9f, hi = -9f; int dips = 0; float p1 = 1f, p2 = 1f; float alarmLo = 9f; int frames = 0, notOne = 0;
                r.For(5f, () => { float p = r.P.Pulse; lo = Math.Min(lo, p); hi = Math.Max(hi, p); if (p2 > p1 && p1 < p && p1 < 0.9f) dips++; p2 = p1; p1 = p; alarmLo = Math.Min(alarmLo, r.P.Alarm); frames++; if (p != 1f) notOne++; });
                r.P.Colour(out float cr, out float cg, out float cb);
                Note($"{(calm ? "CALM MODE" : "normal")}: alarm at least {alarmLo:F2}, pulse {lo:F2} .. {hi:F2}, dips in 5 s {dips} (period {OzShuttleNightPlan.PulseSec:F2} s = {1f / OzShuttleNightPlan.PulseSec:F2} Hz), frames off full light {notOne} of {frames}, colour {cr:F2} {cg:F2} {cb:F2}, beeps {r.Beeps}");
                if (!calm) Check("5a ALARMING pulses slowly: 0.55 .. 1 of the light, 3-5 dips in 5 s (0.8 a second, far under the 3 a second of a flash), one beep", alarmLo >= 0.99f && lo >= 1f - OzShuttleNightPlan.PulseDepth - 1e-3f && lo <= 0.56f && hi >= 0.999f && dips >= 3 && dips <= 5 && 1f / OzShuttleNightPlan.PulseSec < 3f && r.Beeps == 1);
                else Check("5b CALM MODE: the colour still turns amber-red and the beam narrows, the beep still plays, but it never pulses (every frame full light)", alarmLo >= 0.99f && notOne == 0 && cr >= 0.99f && cg <= 0.46f && r.P.AngleDeg <= OzShuttleNightPlan.AlarmAngleDeg + 0.01f && r.Beeps == 1);
            }
            // ---- 5c. the fastest game speed (x4) never quickens the warning pulse: it runs on real time, as the fort's alarm lights
            {
                var r = new Run1 { Speed = 4f }; r.F.NightK = 1f; r.Fly(OzShuttleNightPlan.Hover, 12f, 0f); r.For(1f);
                r.Enemies.Add(E(51, 14f, 10f, 40f)); r.For(2f);
                float lo = 9f, alarmLo = 9f, p1 = 1f, p2 = 1f; int dips = 0;
                r.For(5f, () => { float p = r.P.Pulse; lo = Math.Min(lo, p); if (p2 > p1 && p1 < p && p1 < 0.9f) dips++; p2 = p1; p1 = p; alarmLo = Math.Min(alarmLo, r.P.Alarm); });
                Note($"game speed x4: alarm at least {alarmLo:F2}, pulse down to {lo:F2}, dips in 5 real seconds {dips} (at x1: 4)");
                Check("5c game speed x4: the warning pulse is not quicker (3-5 dips in 5 real seconds, never a quick blink)", alarmLo >= 0.99f && lo <= 0.56f && dips >= 3 && dips <= 5);
            }
            // ---- 6. the sweep: never stuck in one direction
            {
                var r = new Run1(); r.F.NightK = 1f; r.Fly(OzShuttleNightPlan.Cruise, 12f, -1f); r.For(3f);
                float ymin = 999f, ymax = -999f, pmin = 999f, pmax = -999f; float longest = 0f, still = 0f, last = r.P.YawNow;
                r.For(18f, () => { ymin = Math.Min(ymin, r.P.YawNow); ymax = Math.Max(ymax, r.P.YawNow); pmin = Math.Min(pmin, r.P.PitchNow); pmax = Math.Max(pmax, r.P.PitchNow); still = Math.Abs(r.P.YawNow - last) < 0.01f ? still + Dt : 0f; longest = Math.Max(longest, still); last = r.P.YawNow; });
                Note($"18 s of flight: the drum {ymin:F1} .. {ymax:F1} deg from the nose (sweep {OzShuttleNightPlan.SweepDeg:F0}), {pmin:F1} .. {pmax:F1} deg down; the longest it stood still {longest:F2} s");
                Check("6 the nose searchlight sweeps the ground ahead in a slow arc (both sides, >= 24 deg each), a little nod, never standing still more than 0.2 s", ymin <= -24f && ymax >= 24f && pmin >= OzShuttleNightPlan.SweepPitchDeg - 4f && pmax <= OzShuttleNightPlan.SweepPitchDeg + 4f && longest <= 0.2f);
            }
            // ---- 7. no lock while the nose searchlight is out (standing at night, by day)
            {
                var r = new Run1(); r.F.NightK = 1f; r.Landed(); r.Enemies.Add(E(31, 10f, 0f, 30f)); r.For(3f);
                var d = new Run1(); d.F.NightK = 0f; d.Fly(OzShuttleNightPlan.Cruise, 12f, -1f); d.Enemies.Add(E(32, 10f, 0f, 30f)); d.For(3f);
                Note($"standing at night with an enemy 10 m away: taken {r.P.Acquires}, nose {r.P.Nose:F2}; flying by day: taken {d.P.Acquires}");
                Check("7 the lock needs the nose searchlight burning: none standing (its light out), none by day", r.P.Acquires == 0 && d.P.Acquires == 0 && r.Beeps == 0 && d.Beeps == 0);
            }
            // ---- 8. GRAPHICS QUALITY: LOW = fewer real lights, no light shadows
            {
                int h = OzShuttleNightPlan.RealLights(0), m = OzShuttleNightPlan.RealLights(1), l = OzShuttleNightPlan.RealLights(2);
                int sh0 = 0, sh2 = 0; for (int i = 0; i < OzShuttleNightPlan.Lamps; i++) { if (OzShuttleNightPlan.ShadowCandidate(i, 0)) sh0++; if (OzShuttleNightPlan.ShadowCandidate(i, 2)) sh2++; }
                Note($"real lights offered: HIGH {h}, MEDIUM {m}, LOW {l} (LOW: landing light 1 {OzShuttleNightPlan.Offered(OzShuttleNightPlan.LandingL, 2)}, landing light 2 {OzShuttleNightPlan.Offered(OzShuttleNightPlan.LandingR, 2)}, ramp {OzShuttleNightPlan.Offered(OzShuttleNightPlan.RampLamp, 2)}, nose {OzShuttleNightPlan.Offered(OzShuttleNightPlan.NoseLamp, 2)}); shadow candidates HIGH {sh0}, LOW {sh2}");
                Check("8 LOW: 2 real lights instead of 4 (one landing light and the nose searchlight; the others keep lens and painted pool), no light shadows (HIGH: the nose searchlight only)", h == 4 && m == 4 && l == 2 && sh0 == 1 && sh2 == 0 && OzShuttleNightPlan.ShadowCandidate(OzShuttleNightPlan.NoseLamp, 0));
            }
            // ---- 9. shot down: everything out at once
            {
                var r = new Run1(); r.F.NightK = 1f; r.Fly(OzShuttleNightPlan.Hover, 5f, 0f); r.Enemies.Add(E(41, 10f, 0f, 30f)); r.For(2f);
                bool wasLit = r.P.Landing > 0.9f && r.P.Nose > 0.9f && r.P.LockId == 41; r.P.Out();
                Check("9 shot down: every light out at once, the lock let go", wasLit && r.P.Landing == 0f && r.P.Nose == 0f && r.P.Ramp == 0f && r.P.Alarm == 0f && r.P.LockId == -1);
            }
            // ---- 10. K256e: the take-off: the landing lights held at least 2 s after the lift-off, then out over 1 s
            {
                var r = new Run1(); r.F.NightK = 1f; r.Landed(); r.For(1f);
                float onFor = 0f, t = 0f, outAt = -1f, at25 = -1f;
                for (int i = 0; i < 300; i++)
                {   // a quick climb: 12 m in 0.8 s, then cruise far from any landing point
                    t += Dt; float k = Math.Min(1f, t / 0.8f);
                    r.Fly(k < 1f ? OzShuttleNightPlan.Climb : OzShuttleNightPlan.Cruise, 12f * k, 80f); r.Step();
                    if (r.P.Landing >= 0.95f) onFor = t;
                    if (at25 < 0f && t >= 2.5f) at25 = r.P.Landing;
                    if (outAt < 0f && t > 2f && r.P.Landing <= 0.001f) outAt = t;
                }
                Note($"a quick take-off (12 m in 0.8 s): the landing lights on (>= 0.95) until {onFor:F2} s after the lift-off, {at25:F2} at 2.5 s, out at {outAt:F2} s");
                Check("10 the take-off: the landing lights stay on at least 2 s after the lift-off (also above 8 m), then fade out over about 1 s", onFor >= 1.98f && at25 >= 0.4f && at25 <= 0.6f && outAt >= 2.9f && outAt <= 3.1f);
            }
            sb.AppendLine($"RESULT {(fails == 0 ? "PASS" : "FAIL " + fails)} ({passes} PASS)");
            failCount = fails; passCount = passes;
            return sb.ToString();
        }
    }
}
