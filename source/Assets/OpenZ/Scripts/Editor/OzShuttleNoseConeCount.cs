#if UNITY_EDITOR || OZ_NOSECONE_HEADLESS
using System;
using System.Collections.Generic;
using System.Text;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// K256d2, the counted check of the nose searchlight's clear cone (no Play; the same file also runs outside the editor). The plan
    /// (OzShuttleNightPlan, with its clear cone) runs through made-up flights; every frame the drawn beam (the drum's turn and drop, the
    /// cone as wide as it is drawn) is tested against the ship's own model files (OzNoseConeShipModel: the hull with its doorway, the pods,
    /// the legs, the lift nozzles, the lamp housings, the closed ramp, the bay).
    /// <list type="bullet">
    /// <item>A the edge itself, B every aim the rules can give (before: the defect's extent), C the limit's own shape (unchanged inside,
    /// eased, never reaching the edge, no step, no kink), D the lock (behind the side not taken; the nearest inside taken; inside as before;
    /// held at the edge; let go past it; the reported case), E the frame rates (30, 60, 144: no beam through the ship, no step against a
    /// 1 ms clock), F the night lights' own counts (K256d / K256e) unchanged.</item>
    /// </list>
    /// </summary>
    public static class OzShuttleNoseConeCount
    {
        const double D2R = Math.PI / 180.0;
        static StringBuilder sb; static int fails, passes;
        static OzNoseConeShipModel M; static OzNoseConeShipModel.Probe Pr;

        static void Check(string what, bool ok) { if (ok) passes++; else fails++; sb.AppendLine((ok ? "PASS  " : "FAIL  ") + what); }
        static void Note(string what) => sb.AppendLine("      " + what);

        /// <summary>The angle (deg) between two aims (turn, drop).</summary>
        public static double AimAngle(double y1, double p1, double y2, double p2)
        {
            double a = y1 * D2R, b = p1 * D2R, c = y2 * D2R, d = p2 * D2R;
            return OzNoseConeShipModel.Angle(Math.Sin(a) * Math.Cos(b), -Math.Sin(b), Math.Cos(a) * Math.Cos(b), Math.Sin(c) * Math.Cos(d), -Math.Sin(d), Math.Cos(c) * Math.Cos(d));
        }

        /// <summary>A made-up flight at cruise height by night (the nose searchlight on), the enemies as the view would measure them.</summary>
        sealed class Run
        {
            public readonly OzShuttleNightPlan P = new OzShuttleNightPlan();
            public OzShuttleNightPlan.Frame F = new OzShuttleNightPlan.Frame { Alive = true, Airborne = true, State = OzShuttleNightPlan.Cruise, FeetM = 12f, GoalDistM = -1f, NightK = 1f };
            public readonly List<OzShuttleNightPlan.Seen> Enemies = new List<OzShuttleNightPlan.Seen>();
            public float Dt = 1f / 60f, T; public bool Model = true;
            public int Frames, Lit, Through, Beeps; public double MaxStep, MaxRate, PastEdge = -999, MaxAbsYaw, YawLo = 999, YawHi = -999;
            public readonly List<float> Yaws = new List<float>();
            float ly = float.NaN, lp;
            public Run(float dt) { Dt = dt; }
            public void Step()
            {
                F.Dt = Dt; F.RealDt = Dt;
                if (P.LockId >= 0)
                {
                    int k = Enemies.FindIndex(e => e.Id == P.LockId);
                    P.LockSeen = k >= 0;
                    if (k >= 0) { P.LockDistM = Enemies[k].DistM; P.LockYawDeg = Enemies[k].YawDeg; P.LockPitchDeg = Enemies[k].PitchDeg; }
                }
                if (P.ScanDue) { P.Candidates.Clear(); P.Candidates.AddRange(Enemies); }
                P.Step(F); T += Dt; Frames++;
                if (P.BeepNow) Beeps++;
                float y = P.YawNow, p = P.PitchNow;
                Yaws.Add(y);
                if (!float.IsNaN(ly)) { double st = AimAngle(ly, lp, y, p); MaxStep = Math.Max(MaxStep, st); MaxRate = Math.Max(MaxRate, st / Dt); }
                MaxAbsYaw = Math.Max(MaxAbsYaw, Math.Abs(y)); YawLo = Math.Min(YawLo, y); YawHi = Math.Max(YawHi, y);
                PastEdge = Math.Max(PastEdge, Math.Abs(y) - OzShuttleNightPlan.ConeEdgeDeg(y, p));
                if (P.NoseStrength > 0.001f) { Lit++; if (Model && M != null && OzNoseConeBeam.Through(M, Pr, y, p, P.AngleDeg)) Through++; }
                ly = y; lp = p;
            }
            public void For(float sec, Action each = null) { int n = (int)Math.Round(sec / Dt); for (int i = 0; i < n; i++) { each?.Invoke(); Step(); } }
            public void ResetRec() { MaxStep = MaxRate = 0; PastEdge = -999; MaxAbsYaw = 0; YawLo = 999; YawHi = -999; Yaws.Clear(); }
            public int Find(int id) => Enemies.FindIndex(e => e.Id == id);
            public void Set(int id, float dist, float yaw, float pitch) { int k = Find(id); var e = E(id, dist, yaw, pitch); if (k >= 0) Enemies[k] = e; else Enemies.Add(e); }
        }

        static OzShuttleNightPlan.Seen E(int id, float dist, float yaw, float pitch) => new OzShuttleNightPlan.Seen { Id = id, DistM = dist, YawDeg = yaw, PitchDeg = pitch };

        /// <summary>The reported case and the hold: an enemy taken at `from` deg, walking round at `rate` deg/s to `to` deg (and staying), at
        /// this drop. Returns the run (its counts) and the turn at which it was let go (999: never).</summary>
        static Run Walk(float dt, float drop, float from, float to, float rate, float stay, bool model, out float letGoAt, out float heldOff, out bool lockedPast)
        {
            var r = new Run(dt) { Model = model };
            r.For(2f);                                                                                     // the light comes on
            r.Set(7, 15f, from, drop); r.For(1.5f);
            float yaw = from, lg = 999f, ho = 0f; bool lp = true; int rel0 = r.P.Releases, hn = 0;
            float edge = OzShuttleNightPlan.ConeEdgeDeg(to, drop);
            r.For(Math.Abs(to - from) / rate + stay, () =>
            {
                yaw = to > from ? Math.Min(to, yaw + rate * r.Dt) : Math.Max(to, yaw - rate * r.Dt);
                if (r.Find(7) >= 0) r.Set(7, 15f, yaw, drop);
                if (r.P.Releases > rel0 && lg > 900f) lg = yaw;
                if (OzShuttleNightPlan.ConeLimitOn && r.P.LockId == 7 && r.P.Releases == rel0 && Math.Abs(yaw) >= edge + 2f && Math.Abs(yaw) <= edge + OzShuttleNightPlan.ConeHoldDeg - 1f)
                {   // past the edge, still followed: the beam held at the edge (after its catch-up)
                    ho = Math.Max(ho, edge - Math.Abs(r.P.YawNow)); hn++;
                    lp &= r.P.Locked && r.P.Alarm >= 0.9f;
                }
            });
            letGoAt = lg; heldOff = ho; lockedPast = lp && (hn > 0 || !OzShuttleNightPlan.ConeLimitOn);
            return r;
        }

        public static string Check(string projectRoot)
        {
            sb = new StringBuilder(); fails = passes = 0; OzNoseConeBeam.Tests = 0;
            var t0 = DateTime.Now;
            sb.AppendLine("K256d2 the nose searchlight's clear cone: the counted check (OzShuttleNightPlan + its cone; the ship's model files of " + projectRoot + ")");
            bool on0 = OzShuttleNightPlan.ConeLimitOn;
            try
            {
                OzShuttleNightPlan.ConeLimitOn = true;
                M = OzNoseConeShipModel.Load(projectRoot);
                Check($"M the ship's model read: {M.Tris} triangles ({string.Join(", ", PartList(M))}){(M.Error != null ? "; ERROR " + M.Error : "")}", M.Error == null && M.Tris > 10000);
                if (M.Error != null) { M = null; return Done(t0, on0); }
                Pr = OzNoseConeBeam.ProbeOf(M);
                Note($"the nose searchlight's beam: from ({OzNoseConeBeam.Pivot[0]:F3}, {OzNoseConeBeam.Pivot[1]:F3}, {OzNoseConeBeam.Pivot[2]:F3}) + {OzNoseConeBeam.Reach:F3} m along its aim, {OzNoseConeBeam.BeamLen:F1} m long; the calm cone {OzShuttleNightPlan.NoseAngleDeg:F0} deg (drawn half angle {OzNoseConeBeam.DrawnHalfDeg(OzShuttleNightPlan.NoseAngleDeg):F2}), the alarm's {OzShuttleNightPlan.AlarmAngleDeg:F0} ({OzNoseConeBeam.DrawnHalfDeg(OzShuttleNightPlan.AlarmAngleDeg):F2}); each test: the axis and 200 rays in five rings");
                PartA(); PartB(); PartC(); PartD(); PartE(); PartF();
            }
            finally { OzShuttleNightPlan.ConeLimitOn = on0; }
            return Done(t0, on0);
        }

        static IEnumerable<string> PartList(OzNoseConeShipModel m) { foreach (var kv in m.PartTris) yield return kv.Key + " " + kv.Value; }

        static string Done(DateTime t0, bool on0)
        {
            OzShuttleNightPlan.ConeLimitOn = on0;
            sb.AppendLine($"RESULT {(fails == 0 ? "PASS" : "FAIL " + fails)} ({passes} PASS, {OzNoseConeBeam.Tests} beam tests against the model, {(DateTime.Now - t0).TotalSeconds:F0} s)");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ A the edge itself
        static void PartA()
        {
            int n = 0, bad = 0, wide1 = 0, wide3 = 0; string first = "";
            for (int i = 0; i <= 740; i++)
            {
                float p = 6f + i * 0.1f;
                foreach (int sg in new[] { 1, -1 })
                {
                    float e = sg * OzShuttleNightPlan.ConeEdgeDeg(sg, p); n++;
                    if (OzNoseConeBeam.Through(M, Pr, e, p, OzShuttleNightPlan.NoseAngleDeg)) { bad++; if (first == "") first = $" (first at {e:F1} / {p:F1})"; }
                    if (OzNoseConeBeam.Through(M, Pr, e, p, OzShuttleNightPlan.NoseAngleDeg, 1.0)) wide1++;
                    if (OzNoseConeBeam.Through(M, Pr, e, p, OzShuttleNightPlan.NoseAngleDeg, OzShuttleNightPlan.ConeTableClearDeg)) wide3++;
                }
            }
            Note($"the edge (deg of turn, right / left) at a drop of 6: {OzShuttleNightPlan.ConeEdgeDeg(1, 6):F1} / {OzShuttleNightPlan.ConeEdgeDeg(-1, 6):F1}, 15: {OzShuttleNightPlan.ConeEdgeDeg(1, 15):F1}, 20: {OzShuttleNightPlan.ConeEdgeDeg(1, 20):F1}, 30: {OzShuttleNightPlan.ConeEdgeDeg(1, 30):F1}, 36: {OzShuttleNightPlan.ConeEdgeDeg(1, 36):F1}, 40: {OzShuttleNightPlan.ConeEdgeDeg(1, 40):F1}, 45: {OzShuttleNightPlan.ConeEdgeDeg(1, 45):F1}, 50: {OzShuttleNightPlan.ConeEdgeDeg(1, 50):F1}, 80: {OzShuttleNightPlan.ConeEdgeDeg(1, 80):F1}");
            Note($"the cone widened by 1 deg on the edge: {wide1} of {n} through; widened by the table's clear margin {OzShuttleNightPlan.ConeTableClearDeg:F0} deg: {wide3} of {n} (the table's margin is measured from rays of the beam's own way: a little less from its apex)");
            Check($"A the edge itself (the turn the drawn beam comes nearest but never reaches), both sides, every 0.1 deg of drop 6 .. 80 ({n} aims), the calm cone (the widest): beams through the ship {bad}{first}", bad == 0);
        }

        // ------------------------------------------------------------------ B every aim the rules can give
        static void PartB()
        {
            int n = 0, bad = 0, nOld = 0, badOld = 0, badOldKeep = 0; string first = "", oldEx = "";
            double ymax = 0;
            for (int yi = -180; yi <= 180; yi++)
                for (int pi = 0; pi <= 148; pi++)
                {
                    float y = yi, p = 6f + pi * 0.5f, d = OzShuttleNightPlan.ConeTurn(y, p); n++;
                    ymax = Math.Max(ymax, Math.Abs(d) - OzShuttleNightPlan.ConeEdgeDeg(d, p));
                    if (OzNoseConeBeam.Through(M, Pr, d, p, OzShuttleNightPlan.NoseAngleDeg)) { bad++; if (first == "") first = $" (first at {y:F0} / {p:F1} drawn {d:F1})"; }
                    if (Math.Abs(y) <= OzShuttleNightPlan.LockYawMax + OzShuttleNightPlan.KeepYawExtra)
                    {   // before: the drawn aim was the aim itself
                        nOld++;
                        if (OzNoseConeBeam.Through(M, Pr, y, p, OzShuttleNightPlan.NoseAngleDeg))
                        {
                            badOld++; if (Math.Abs(y) > OzShuttleNightPlan.LockYawMax) badOldKeep++;
                            if (oldEx == "" && Math.Abs(y) == 125 && p == 20f) oldEx = " (e.g. 125 / 20: through)";
                        }
                    }
                }
            Note($"before this fix: of the {nOld} aims the rules could give (turn up to {OzShuttleNightPlan.LockYawMax + OzShuttleNightPlan.KeepYawExtra:F0} deg, drop 6 .. 80) {badOld} shone through the ship ({badOldKeep} of them past {OzShuttleNightPlan.LockYawMax:F0} deg, where a followed enemy was kept){oldEx}");
            Check($"B0 the test sees the reported defect: before this fix, aims through the ship ({badOld}) > 0", badOld > 0);
            Check($"B every aim the rules can give, now: the drum's aim at every whole degree of turn -180 .. 180 x every 0.5 deg of drop 6 .. 80 ({n} aims), drawn through the clear cone, the calm cone: beams through the ship {bad}{first}; the drawn turn never past the edge (at most {ymax:F4} deg past it: 0 = on the edge itself, A)", bad == 0 && ymax <= 0);
        }

        // ------------------------------------------------------------------ C the limit's own shape
        static void PartC()
        {
            double worstIn = 0, worstSlopeLo = 1, worstSlopeHi = 0, worstKink = 0, worstDrop = 0, worstEdge = -999; int knee = 0;
            for (int pi = 6; pi <= 80; pi++)
            {
                float p = pi;
                foreach (int sg in new[] { 1, -1 })
                {
                    float e = OzShuttleNightPlan.ConeEdgeDeg(sg, p);
                    double prevSlope = double.NaN;
                    for (int i = 0; i < 18000; i++)
                    {
                        float y0 = sg * i * 0.01f, y1 = sg * (i + 1) * 0.01f;
                        double f0 = OzShuttleNightPlan.ConeTurn(y0, p), f1 = OzShuttleNightPlan.ConeTurn(y1, p), sl = (f1 - f0) / (y1 - y0);
                        if (Math.Abs(y0) <= e - OzShuttleNightPlan.ConeKneeDeg) { worstIn = Math.Max(worstIn, Math.Abs(f0 - y0)); knee++; }
                        worstSlopeLo = Math.Min(worstSlopeLo, sl); worstSlopeHi = Math.Max(worstSlopeHi, sl);
                        if (!double.IsNaN(prevSlope)) worstKink = Math.Max(worstKink, Math.Abs(sl - prevSlope));
                        prevSlope = sl;
                        worstEdge = Math.Max(worstEdge, Math.Abs(f0) - e);
                    }
                    worstEdge = Math.Max(worstEdge, Math.Abs(OzShuttleNightPlan.ConeTurn(sg * 1e6f, p)) - e);
                }
            }
            for (int yi = -180; yi <= 180; yi += 2)
                for (int i = 0; i < 7400; i++)
                {
                    float p0 = 6f + i * 0.01f, p1 = p0 + 0.01f;
                    worstDrop = Math.Max(worstDrop, Math.Abs(OzShuttleNightPlan.ConeTurn(yi, p1) - OzShuttleNightPlan.ConeTurn(yi, p0)) / 0.01);
                }
            Check($"C1 inside the cone up to {OzShuttleNightPlan.ConeKneeDeg:F0} deg before the edge the aim is unchanged ({knee} aims: largest change {worstIn:F6} deg)", worstIn <= 1e-4);
            Check($"C2 eased into the edge: the drawn turn rises with the aim, never faster (its slope {worstSlopeLo:F4} .. {worstSlopeHi:F4} per deg), never past the edge (at most {worstEdge:F4} deg past it, also for an aim a million degrees round; it comes onto the edge only in the last float digit)", worstSlopeLo >= -1e-3 && worstSlopeHi <= 1.002 && worstEdge <= 0);
            Check($"C3 no kink: the slope changes by at most {worstKink:F5} from one 0.01 deg to the next (a snap into the edge would change it by up to 1)", worstKink <= 0.01);
            Check($"C4 no step in the drop: the drawn turn changes by at most {worstDrop:F2} deg per deg of drop (the edge's own steepest: {OzShuttleNightPlan.ConeTableSlope:F1})", worstDrop <= OzShuttleNightPlan.ConeTableSlope + 0.05);
        }

        // ------------------------------------------------------------------ D the lock
        static void PartD()
        {
            const float Dt = 1f / 60f;
            // D1 an enemy behind the side, alone (105 deg, 20 deg down: the old rules took it, its beam went through the front leg)
            Run D1(bool on)
            {
                OzShuttleNightPlan.ConeLimitOn = on;
                var r = new Run(Dt); r.For(2f); r.Set(1, 15f, 105f, 20f); r.ResetRec(); r.For(9f);
                OzShuttleNightPlan.ConeLimitOn = true; return r;
            }
            var a = D1(true); var b = D1(false);
            Note($"D1 before: taken {b.P.Acquires}, beeps {b.Beeps}, beam frames through the ship {b.Through} of {b.Lit}; now: taken {a.P.Acquires}, beeps {a.Beeps}, through {a.Through} of {a.Lit}, the sweep {a.YawLo:F1} .. {a.YawHi:F1} deg");
            Check("D1 an enemy behind the side (105 deg, 20 deg down; the edge there 93): not taken, no beep, the light sweeps on ahead (both sides, >= 24 deg each), beams through the ship 0 (before: taken, through)",
                a.P.Acquires == 0 && a.Beeps == 0 && a.YawLo <= -24 && a.YawHi >= 24 && a.Through == 0 && b.P.Acquires == 1 && b.Through > 0);
            // D2 the nearest enemy behind the side, a farther one inside: the one inside is taken
            Run D2(bool on)
            {
                OzShuttleNightPlan.ConeLimitOn = on;
                var r = new Run(Dt); r.For(2f); r.Set(2, 10f, 105f, 20f); r.Set(3, 20f, 40f, 30f); r.For(3f);
                OzShuttleNightPlan.ConeLimitOn = true; return r;
            }
            a = D2(true); b = D2(false);
            Check($"D2 the nearest enemy behind the side (10 m) and a farther one inside (20 m, 40 deg): the one inside is taken, one beep, the beam on it (within 2 deg: {Math.Abs(a.P.YawNow - 40f):F2}), through 0 (before: the nearest behind the side, through {b.Through})",
                a.P.LockId == 3 && a.Beeps == 1 && a.P.Locked && Math.Abs(a.P.YawNow - 40f) <= 2f && Math.Abs(a.P.PitchNow - 30f) <= 2f && a.Through == 0 && b.P.LockId == 2);
            // D3 inside, near the edge: taken, the beep, the alarm, the beam on it as before
            Run D3(bool on, out float lockAt)
            {
                OzShuttleNightPlan.ConeLimitOn = on; float la = -1f;
                var r = new Run(Dt); r.For(2f); r.Set(4, 15f, 88f, 20f); float t0 = r.T; r.For(2f, () => { if (la < 0f && r.P.Locked) la = r.T - t0; });
                lockAt = la; OzShuttleNightPlan.ConeLimitOn = true; return r;
            }
            a = D3(true, out float la1); b = D3(false, out float la0);
            Check($"D3 an enemy inside near the edge (88 deg, 20 deg down; the edge 93): taken, one beep, alarming, the beam on it ({a.P.YawNow:F2} / {a.P.PitchNow:F2}; within 1 deg), the beam on it at {la1:F3} s (before {la0:F3} s), through 0",
                a.P.LockId == 4 && a.Beeps == 1 && a.P.Alarm >= 0.99f && Math.Abs(a.P.YawNow - 88f) <= 1f && Math.Abs(a.P.PitchNow - 20f) <= 0.5f && Math.Abs(la1 - la0) <= 2.5f * Dt && a.Through == 0 && b.Beeps == 1);
            // D4 well inside: frame by frame as before (the sweep, a lock at 100 deg 45 down, a let-go)
            {
                var r1 = new Run(Dt) { Model = false }; var r0 = new Run(Dt) { Model = false };
                double worst = 0; int beepDiff = 0;
                for (int i = 0; i < 60 * 30; i++)
                {
                    float t = i * Dt;
                    foreach (var r in new[] { r1, r0 })
                    {
                        if (i == 120) r.Set(5, 9f, 100f, 45f);
                        if (i == 600) r.Set(6, 14f, -60f, 30f);
                        if (i == 900) r.Enemies.RemoveAt(r.Find(5));
                        if (i == 1300) r.Enemies.Clear();
                        OzShuttleNightPlan.ConeLimitOn = r == r1; r.Step();
                    }
                    OzShuttleNightPlan.ConeLimitOn = true;
                    worst = Math.Max(worst, Math.Max(Math.Abs(r1.P.YawNow - r0.P.YawNow), Math.Abs(r1.P.PitchNow - r0.P.PitchNow)));
                    beepDiff += r1.P.BeepNow != r0.P.BeepNow ? 1 : 0;
                }
                Check($"D4 well inside the cone everything is as before, frame by frame for 30 s (the sweep, a lock at 100 deg 45 down, another at -60 / 30, let go, the sweep again): largest difference {worst:F6} deg, beeps on other frames {beepDiff}, locks {r1.P.LockOns} = {r0.P.LockOns}, let go {r1.P.Releases} = {r0.P.Releases}",
                    worst <= 1e-3 && beepDiff == 0 && r1.P.LockOns == r0.P.LockOns && r1.P.Releases == r0.P.Releases && r1.P.LockOns == 2);
            }
            // D5 held at the edge: a followed enemy walks round from 60 to 140 deg at 20 deg down
            a = Walk(Dt, 20f, 60f, 140f, 10f, 4f, true, out float letGo, out float heldOff, out bool lockedPast);
            OzShuttleNightPlan.ConeLimitOn = false; b = Walk(Dt, 20f, 60f, 140f, 10f, 4f, true, out float letGo0, out _, out _); OzShuttleNightPlan.ConeLimitOn = true;
            float e20 = OzShuttleNightPlan.ConeEdgeDeg(1f, 20f);
            Check($"D5 a followed enemy walks round from 60 to 140 deg (20 deg down; the edge {e20:F1}): the beam eases into the edge and holds there (at most {heldOff:F2} deg short of it, still alarming: {lockedPast}) while the enemy is up to {OzShuttleNightPlan.ConeHoldDeg:F0} deg past it, never past it ({a.PastEdge:F3}), lets go once at {letGo:F1} deg ({a.P.Releases} let go), back to the sweep ({a.P.YawNow:F1} deg at the end); beams through the ship {a.Through} of {a.Lit} (before: kept to {letGo0:F0} deg, through {b.Through})",
                heldOff <= 3f && lockedPast && a.PastEdge <= 0 && letGo >= e20 + OzShuttleNightPlan.ConeHoldDeg - 1f && letGo <= e20 + OzShuttleNightPlan.ConeHoldDeg + 1f && a.P.Releases == 1 && Math.Abs(a.P.YawNow) <= 35f && a.Through == 0 && b.Through > 0);
            // D6 the reported case: an enemy far behind the side (125 deg) at a shallow drop, 6 .. 30 deg down
            var sbx = new StringBuilder(); bool ok6 = true; int before6 = 0, after6 = 0;
            foreach (float drop in new[] { 6f, 10f, 15f, 20f, 25f, 30f })
            {
                var x = Walk(Dt, drop, 85f, 125f, 20f, 3f, true, out float lg, out _, out _);
                OzShuttleNightPlan.ConeLimitOn = false; var y = Walk(Dt, drop, 85f, 125f, 20f, 3f, true, out _, out _, out _); OzShuttleNightPlan.ConeLimitOn = true;
                sbx.Append($" {drop:F0}: {y.Through} -> {x.Through} (let go at {lg:F0});");
                before6 += y.Through; after6 += x.Through; ok6 &= x.Through == 0 && x.PastEdge <= 0;
            }
            Check($"D6 the reported case: an enemy taken at 85 deg walks to 125 deg behind the side, 6 .. 30 deg down: beam frames through the ship before -> now, per drop:{sbx} in all {before6} -> {after6}", ok6 && after6 == 0 && before6 > 0);
            // D7 the other side: a followed enemy destroyed, the next one on the other side is taken: the short way, by the front
            Run D7(bool on)
            {
                OzShuttleNightPlan.ConeLimitOn = on;
                var r = new Run(Dt); r.For(2f); r.Set(8, 10f, 90f, 40f); r.For(2f); r.Set(9, 14f, -100f, 40f); r.For(0.5f); r.ResetRec();
                r.Enemies.RemoveAt(r.Find(8)); r.For(3f);
                OzShuttleNightPlan.ConeLimitOn = true; return r;
            }
            a = D7(true); b = D7(false);
            Check($"D7 a followed enemy (90 deg, 40 down) destroyed, the next one on the other side (-100 deg) taken: the beam turns the short way by the front (turn {a.YawLo:F1} .. {a.YawHi:F1}, never past either), on it ({a.P.YawNow:F1}), through 0 (before {b.YawLo:F1} .. {b.YawHi:F1})",
                a.P.LockId == 9 && a.YawLo >= -100.5 && a.YawHi <= 90.5 && Math.Abs(a.P.YawNow + 100f) <= 2f && a.Through == 0);
            // D8 an enemy circling the ship (12 m, 25 and 40 deg down, 30 deg/s, two turns each)
            int th = 0, lit = 0, acq = 0; double past = -999;
            foreach (float drop in new[] { 25f, 40f })
            {
                var r = new Run(Dt); r.For(2f); float yaw = 0f;
                r.For(24f, () => { yaw += 30f * r.Dt; float w = (yaw + 180f) % 360f - 180f; r.Set(10, 12f, w, drop); });
                th += r.Through; lit += r.Lit; acq += r.P.Acquires; past = Math.Max(past, r.PastEdge);
            }
            Check($"D8 an enemy circling the ship twice (12 m; 25 and 40 deg down; 30 deg/s): taken {acq} times as it comes round into the cone, the beam never past the edge ({past:F3}), beams through the ship {th} of {lit}", th == 0 && past <= 0 && acq >= 4);
            // D9 by day the drum rests (0 / 62) as before
            {
                var r = new Run(Dt) { Model = false }; r.F.NightK = 0f; r.For(3f);
                Check($"D9 by day (the light out) the drum rests at the nose, 62 deg down, as before ({r.P.YawNow:F3} / {r.P.PitchNow:F3})", Math.Abs(r.P.YawNow) < 1e-3 && Math.Abs(r.P.PitchNow - OzShuttleNightPlan.RestPitchDeg) < 1e-2);
            }
        }

        // ------------------------------------------------------------------ E the frame rates
        static void PartE()
        {
            var sbx = new StringBuilder(); bool ok = true;
            foreach (var (name, make) in new (string, Func<float, bool, Run>)[]
            {
                ("the hold (D5)", (dt, m) => Walk(dt, 20f, 60f, 140f, 10f, 4f, m, out _, out _, out _)),
                ("the reported case at 20 deg down (D6)", (dt, m) => Walk(dt, 20f, 85f, 125f, 20f, 3f, m, out _, out _, out _)),
                ("the reported case at 30 deg down (D6)", (dt, m) => Walk(dt, 30f, 85f, 125f, 20f, 3f, m, out _, out _, out _)),
                ("the circling enemy (D8, 25 down)", (dt, m) =>
                {
                    var r = new Run(dt) { Model = m }; r.For(2f); float yaw = 0f;
                    r.For(24f, () => { yaw += 30f * r.Dt; float w = (yaw + 180f) % 360f - 180f; r.Set(10, 12f, w, 25f); }); return r;
                }),
            })
            {
                var fine = make(0.001f, false);
                sbx.Append($"\n      {name}: 1 ms: largest step {fine.MaxStep:F3} deg, fastest {fine.MaxRate:F0} deg/s");
                bool okS = fine.MaxStep <= 0.5;
                foreach (float fps in new[] { 30f, 60f, 144f })
                {
                    var r = make(1f / fps, true);
                    bool okR = r.MaxRate <= fine.MaxRate * 1.05 + 1.0 && r.Through == 0 && r.PastEdge <= 0;
                    sbx.Append($"; {fps:F0} fps: through {r.Through} of {r.Lit}, largest step {r.MaxStep:F2} deg, fastest {r.MaxRate:F0} deg/s{(okR ? "" : " <- BAD")}");
                    ok &= okR;
                }
                ok &= okS;
            }
            Check("E 30 / 60 / 144 fps: beams through the ship 0, the beam never past the edge, no step (never faster than the same motion on a 1 ms clock, x 1.05; its largest change in 1 ms at most 0.5 deg):" + sbx, ok);
        }

        // ------------------------------------------------------------------ F the night lights' own counts
        static void PartF()
        {
            var text = OzShuttleNightCounts.Run(out int f, out int p);
            foreach (var l in text.Split('\n')) if (l.StartsWith("FAIL")) Note("K256d: " + l.TrimEnd());
            Check($"F the night lights' own counts (K256d + K256e: the lights' states, the dusk and dawn, the lock, the beep, the alarm, the pulse, CALM MODE, the sweep, LOW) with the clear cone: {p} PASS, {f} FAIL", f == 0 && p > 0);
        }
    }
}
#endif
