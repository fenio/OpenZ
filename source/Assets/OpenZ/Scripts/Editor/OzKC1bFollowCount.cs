using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// The counted check of kit KC1b (DV109 calm follow behind a slower unit).
    /// 1. The table: on a straight road of the Desert, the Jungle and the Car Park, a slower unit ahead and a faster one behind it, every
    ///    pair of kinds that can occur by speed (jeep 17; light tank, APC, crane, grunt 14; mech 12; heavy tank 9; missile launcher 6),
    ///    ordered to the same far point: 3 tiles behind and ordered at once (a group order), or 8 tiles behind and ordered 1 s later (it
    ///    catches up). Each case with the rule OFF and ON, calm movement on in both, passing off (the game's defaults).
    /// 2. The red fort case: Level 16, the red heavy tank at (51,104) and the mech at (56,104) behind it, one order WEST along the road
    ///    north of the red fort, in line and one tile to the side; the rule off / ON, the calm movement off / on.
    /// 3. A new order after a follow (the next order starts at the unit's own speed), and KC1's crowd runs with the rule off / ON.
    /// Counted for the unit behind while the one ahead moves and is within 6 tiles: stops (5 ticks or more without moving: the view's
    /// "standing"), speed changes (its speed in 0.1 s windows; a change of more than 2 px/s from one window to the next), speed
    /// reversals (the change of its speed turns from up to down or back, each change at least 0.5 px/s per 0.1 s: a surge and brake),
    /// heading reversals (3 px chords, a turn of 15 degrees one way and then the other within 2 s), the centre gap (mean, sd, least;
    /// against the gate's spacing = the two radii + 1 px), the speed and gap sd once it has closed up (its speed first within 1.5 px/s of
    /// the slower unit's), the times it began to ease, and when each unit's order ended (the pair has arrived when both have).
    /// Menu OpenZ/V1.1/Count Calm Follow (KC1b), outside Play. The same code runs headless (the kit's runner).
    /// Uses public members of the rules only; a switch the project does not have is left alone (the line then says n/a).
    /// </summary>
    public static class OzKC1bFollowCount
    {
        public static string Settings, MapsDir;                                          // headless: the settings text and the folder of the *_grid.json maps
#if UNITY_EDITOR
        static string SettingsText => Settings ?? OzV11Kit.SettingsText;
        static string MapText(string level) => MapsDir != null ? System.IO.File.ReadAllText(System.IO.Path.Combine(MapsDir, level + "_grid.json")) : OzV11Kit.MapText(level);
#else
        static string SettingsText => Settings;
        static string MapText(string level) => System.IO.File.ReadAllText(System.IO.Path.Combine(MapsDir, level + "_grid.json"));
#endif
        /// <summary>While true, OzV11Kit.GameSwitches leaves calm follow ON (only this check sets it, for KC1's crowd runs, and puts it back).</summary>
        public static bool InChecks;
        static string F(double v, string f = "0.0") => double.IsNaN(v) || double.IsInfinity(v) ? "never" : v.ToString(f, CultureInfo.InvariantCulture);
        const double Tick = ZConst.TICK, Near = 96, ArriveNear = 96;

        /// <summary>The roads (tiles): the slow unit's start and the far point both are sent to.</summary>
        public static readonly (string level, string name, int lx, int ly, int gx, int gy)[] Roads =
        {
            ("L02", "Desert road south", 52, 18, 52, 44),
            ("L16", "Jungle road east", 34, 104, 92, 104),
            ("L17", "Car Park road east", 74, 74, 100, 74),
        };
        /// <summary>Every follower kind behind every slower kind of its team that can occur by speed (medium = the mech).</summary>
        public static readonly (string lead, string fol)[] Pairs =
        {
            ("heavy", "medium"), ("heavy", "light"), ("heavy", "jeep"), ("heavy", "apc"), ("heavy", "crane"), ("heavy", "grunt"),
            ("missile_launcher", "medium"), ("missile_launcher", "heavy"), ("missile_launcher", "light"), ("missile_launcher", "apc"), ("missile_launcher", "crane"), ("missile_launcher", "jeep"), ("missile_launcher", "grunt"),
            ("medium", "light"), ("medium", "apc"), ("medium", "crane"), ("medium", "jeep"), ("medium", "grunt"),
            ("crane", "jeep"), ("apc", "jeep"),
        };
        public static readonly (double behind, double delay)[] Starts = { (3, 0), (8, 1) };

        // ------------------------------------------------------------------ the meter
        public sealed class Meter
        {
            readonly ZObject L, U; readonly double steadyFar, gpx, gpy;
            /// <summary>px: not counted while the unit ahead is this near the point (where a group arrives and the gate's arrival rules act; 0 = always counted).</summary>
            public double EndZone;
            /// <summary>s after the start: a heading reversal before this is not counted (the path's first jog onto the road line; 0 = all).</summary>
            public double JogTime;
            public int Stops, SpeedChanges, Reversals, TurnsBack, SpeedReversals, Starts; public double FollowTime, StoodTime, GapSum, Gap2Sum, GapMin = double.PositiveInfinity; public int GapN;
            public double CloseT = double.NaN, CvSum, Cv2Sum, CgSum, Cg2Sum; public int CN;          // once closed up: the unit's speed (0.1 s windows) and the gap
            double lpx, lpy, upx, upy, wx, wy, lwx, lwy, wPrev = double.NaN, ax, ay, hx, hy, lastTurnT = -100; int still, wTicks, lastSign, dvSign; bool counted, hasH, onW, wasEasing, closed;
            public readonly List<string> Trace;
            /// <summary>gx, gy: the point both are sent to (px); the steady stretch ends where the slower unit is within the clear road's closing ramp of it.</summary>
            public Meter(ZObject l, ZObject u, bool trace, double gx = double.NaN, double gy = double.NaN) { L = l; U = u; gpx = gx; gpy = gy; steadyFar = SteadyFar(u); lpx = lwx = l.PosX; lpy = lwy = l.PosY; upx = ax = wx = u.PosX; upy = ay = wy = u.PosY; Trace = trace ? new List<string>() : null; }
            public void Step(double t, bool ordered, double t0)
            {
                double ldx = L.PosX - lpx, ldy = L.PosY - lpy, ld = Math.Sqrt(ldx * ldx + ldy * ldy); lpx = L.PosX; lpy = L.PosY;
                double ud = Math.Sqrt((U.PosX - upx) * (U.PosX - upx) + (U.PosY - upy) * (U.PosY - upy)); upx = U.PosX; upy = U.PosY;
                double gx = L.PosX + L.WidthPix * 0.5 - U.PosX - U.WidthPix * 0.5, gy = L.PosY + L.HeightPix * 0.5 - U.PosY - U.HeightPix * 0.5, gap = Math.Sqrt(gx * gx + gy * gy);
                bool road = EndZone <= 0 || double.IsNaN(gpx) || (L.CenterX - gpx) * (L.CenterX - gpx) + (L.CenterY - gpy) * (L.CenterY - gpy) > EndZone * EndZone;   // (outside the arrival zone, when one is given)
                bool on = ordered && U.Waypoints.Count > 0 && L.Waypoints.Count > 0 && ld > 1e-6 && ldx * gx + ldy * gy > 0 && gap <= Near && road;   // the slower unit drives on, away from the unit behind
                if (on) { FollowTime += Tick; GapSum += gap; Gap2Sum += gap * gap; GapN++; if (gap < GapMin) GapMin = gap; }
                if (ud > 1e-6) { still = 0; counted = false; }
                else if (U.Waypoints.Count > 0) { still++; if (on) StoodTime += Tick; if (still >= 5 && !counted && on) { Stops++; counted = true; } }
                if (ordered && U.CalmFollowing && !wasEasing) Starts++;
                wasEasing = U.CalmFollowing;
                if (on) onW = true;
                if (++wTicks == 10)
                {
                    double v = Math.Sqrt((U.PosX - wx) * (U.PosX - wx) + (U.PosY - wy) * (U.PosY - wy)) / (10 * Tick);
                    double vl = Math.Sqrt((L.PosX - lwx) * (L.PosX - lwx) + (L.PosY - lwy) * (L.PosY - lwy)) / (10 * Tick);
                    if (onW && !double.IsNaN(wPrev))
                    {
                        if (Math.Abs(v - wPrev) > 2.0) SpeedChanges++;
                        double dv = v - wPrev;
                        if (Math.Abs(dv) >= 0.5) { int sg = dv > 0 ? 1 : -1; if (dvSign != 0 && sg != dvSign) SpeedReversals++; dvSign = sg; }
                        if (!closed && Math.Abs(v - vl) <= 1.5) { closed = true; CloseT = t - t0; }
                        bool steady = double.IsNaN(gpx) || Math.Sqrt((L.CenterX - gpx) * (L.CenterX - gpx) + (L.CenterY - gpy) * (L.CenterY - gpy)) > steadyFar;
                        if (closed && steady) { CvSum += v; Cv2Sum += v * v; CgSum += gap; Cg2Sum += gap * gap; CN++; }
                    }
                    Trace?.Add("  t " + F(t - t0, "0.00") + " ahead (" + F((L.PosX + L.WidthPix * 0.5) / 16, "0.00") + "," + F((L.PosY + L.HeightPix * 0.5) / 16, "0.00") + ") " + F(vl) + " px/s | behind (" + F((U.PosX + U.WidthPix * 0.5) / 16, "0.00") + "," + F((U.PosY + U.HeightPix * 0.5) / 16, "0.00") + ") " + F(v) + " of " + F(U.RealMoveSpeed) + " px/s, gap " + F(gap) + " px, " + U.PathWait + (U.PathHeld ? " HELD" : "") + (U.CalmFollowing ? " easing" : "") + (onW ? "" : " (not counted)"));
                    wx = U.PosX; wy = U.PosY; lwx = L.PosX; lwy = L.PosY; wPrev = v; wTicks = 0; onW = false;
                }
                // heading: 3 px chords, a turn of 15 degrees, a reversal = a turn the other way within 2 s
                double cx = U.PosX - ax, cy = U.PosY - ay, cl = Math.Sqrt(cx * cx + cy * cy);
                if (cl >= 3 && U.Waypoints.Count > 0 && road)
                {
                    cx /= cl; cy /= cl; ax = U.PosX; ay = U.PosY;
                    if (hasH)
                    {
                        double d = Math.Atan2(hx * cy - hy * cx, hx * cx + hy * cy) * 180 / Math.PI;
                        if (Math.Abs(d) >= 15)
                        {
                            int sg = d > 0 ? 1 : -1;
                            if (Math.Abs(d) > 120) TurnsBack++;
                            if (lastSign != 0 && sg != lastSign && t - lastTurnT <= 2.0 && t - t0 >= JogTime) Reversals++;
                            lastSign = sg; lastTurnT = t;
                        }
                    }
                    hx = cx; hy = cy; hasH = true;
                }
            }
            /// <summary>The slower unit's distance to its end at which the unit behind starts to close its clear road (the rule's ramp + 1 tile; 6 tiles if the rule is not there).</summary>
            static double SteadyFar(ZObject u)
            {
                var f = typeof(ZPathing).GetField("FollowLeadRamp", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                double ramp = f != null ? Convert.ToDouble(f.GetValue(null)) : 3;
                return ramp * 2 * ZPathing.Radius(u) + 16;
            }
            public double GapMean => GapN > 0 ? GapSum / GapN : double.NaN;
            public double GapSd => GapN > 1 ? Math.Sqrt(Math.Max(0, Gap2Sum / GapN - GapMean * GapMean)) : double.NaN;
            public double SpeedSdClosed => CN > 1 ? Math.Sqrt(Math.Max(0, Cv2Sum / CN - (CvSum / CN) * (CvSum / CN))) : double.NaN;
            public double GapSdClosed => CN > 1 ? Math.Sqrt(Math.Max(0, Cg2Sum / CN - (CgSum / CN) * (CgSum / CN))) : double.NaN;
        }

        public sealed class Result
        {
            public string Name, Fol, Lead, Line; public int Stops, SpeedChanges, Reversals, TurnsBack, SpeedReversals, Starts, Easing;
            public double FollowTime, StoodTime, TLead, TBehind, TNear, GapMean, GapSd, GapMin, Spacing, SpeedSdClosed, GapSdClosed, CloseT, EndOff, TAlone = double.NaN, Allow; public bool Arrived;
            public List<string> Trace;
            /// <summary>Both orders ended (the pair has arrived): the later of the two.</summary>
            public double TPair => Math.Max(TLead, TBehind);
            /// <summary>Never inside the gate's spacing while it followed (no overlap).</summary>
            public bool GapOk => double.IsInfinity(GapMin) || GapMin >= Spacing - 1e-6;
        }

        static string NoItems(string mapJson)
        {
            var m = ZMapData.FromJson(mapJson);
            m.objects.RemoveAll(o => o.kind == "map_item:flag" || o.kind == "map_item:grenades");
            return UnityEngine.JsonUtility.ToJson(m);
        }

        /// <summary>One case. side = tiles to the right of the slow unit's line; behindFirst = the unit behind is made first (it moves before
        /// the slow one in every tick).</summary>
        public static Result Case(string level, string leadType, string folType, int lx, int ly, int gx, int gy, double behind, double delay, bool trace = false, double side = 0, bool behindFirst = false, double maxS = 300, int seed = 1)
        {
            var w = OzV11Kit.World(SettingsText, NoItems(OzV11Kit.NoUnits(MapText(level))), seed);
            OzV11Kit.Dummy(w, 5 * 16 + 8, 5 * 16 + 8);
            int lcx = lx * 16 + 16, lcy = ly * 16 + 16, gcx = gx * 16 + 8, gcy = gy * 16 + 8;
            double dx = gcx - lcx, dy = gcy - lcy, dl = Math.Sqrt(dx * dx + dy * dy); dx /= dl; dy /= dl;
            int fcx = (int)Math.Round(lcx - dx * behind * 16 - dy * side * 16), fcy = (int)Math.Round(lcy - dy * behind * 16 + dx * side * 16);
            ZObject lead, fol;
            if (behindFirst) { fol = OzV11Kit.Place(w, folType, fcx, fcy, Team.Red); lead = OzV11Kit.Place(w, leadType, lcx, lcy, Team.Red); }
            else { lead = OzV11Kit.Place(w, leadType, lcx, lcy, Team.Red); fol = OzV11Kit.Place(w, folType, fcx, fcy, Team.Red); }
            for (int i = 0; i < 300; i++) { w.Tick(Tick); w.Events.Clear(); }
            double t0 = w.Time, leadSpeed = lead.MoveSpeed * w.Map.TileWalkSpeed(lead.CenterX, lead.CenterY);
            OzV11Kit.PlayerMove(w, lead, gcx, gcy);
            bool ordered = false; var m = new Meter(lead, fol, trace, gcx, gcy);
            double tl = double.PositiveInfinity, tf = double.PositiveInfinity, tn = double.PositiveInfinity; int easing = 0;
            while (w.Time - t0 < maxS)
            {
                if (!ordered && w.Time - t0 >= delay - 1e-9) { OzV11Kit.PlayerMove(w, fol, gcx, gcy); ordered = true; }
                w.Tick(Tick); w.Events.Clear();
                m.Step(w.Time, ordered && double.IsInfinity(tf), t0);
                if (fol.CalmFollowing) easing++;
                if (double.IsInfinity(tl) && lead.Waypoints.Count == 0) tl = w.Time - t0;
                if (ordered && double.IsInfinity(tf) && fol.Waypoints.Count == 0) tf = w.Time - t0;
                if (double.IsInfinity(tn) && (double)(fol.CenterX - gcx) * (fol.CenterX - gcx) + (double)(fol.CenterY - gcy) * (fol.CenterY - gcy) <= ArriveNear * ArriveNear) tn = w.Time - t0;
                if (!double.IsInfinity(tl) && !double.IsInfinity(tf)) break;
            }
            double endOff = Math.Sqrt((double)(fol.CenterX - gcx) * (fol.CenterX - gcx) + (double)(fol.CenterY - gcy) * (fol.CenterY - gcy)) / 16;
            var r = new Result
            {
                Lead = leadType, Fol = folType,
                Stops = m.Stops, SpeedChanges = m.SpeedChanges, Reversals = m.Reversals, TurnsBack = m.TurnsBack, SpeedReversals = m.SpeedReversals, Starts = m.Starts, FollowTime = m.FollowTime, StoodTime = m.StoodTime,
                TLead = tl, TBehind = tf, TNear = tn, GapMean = m.GapMean, GapSd = m.GapSd, GapMin = m.GapMin, Spacing = ZPathing.MinSpacing(lead, fol) + 1, SpeedSdClosed = m.SpeedSdClosed, GapSdClosed = m.GapSdClosed, CloseT = m.CloseT,
                EndOff = endOff, Arrived = !double.IsInfinity(tf) && endOff <= 6, Easing = easing, Trace = m.Trace,
                Allow = 2.0 * ZPathing.Radius(fol) / Math.Max(1.0, leadSpeed),
            };
            r.Line = leadType + " ahead, " + folType + " " + F(behind, "0") + " tiles behind" + (side != 0 ? " and " + F(side, "0") + " to the side" : "") + ", ordered +" + F(delay) + " s | behind it " + F(r.FollowTime) + " s: stops " + r.Stops + " (stood " + F(r.StoodTime) + " s), speed changes " + r.SpeedChanges
                + " (" + F(r.FollowTime > 0.5 ? r.SpeedChanges * 10 / r.FollowTime : 0) + " per 10 s), speed reversals " + r.SpeedReversals + ", heading reversals " + r.Reversals + ", turns back " + r.TurnsBack
                + " | gap " + F(r.GapMean / 16, "0.00") + " tiles (sd " + F(r.GapSd / 16, "0.00") + "), least " + F(r.GapMin, "0.0") + " px (spacing " + F(r.Spacing, "0") + ")"
                + " | closed up at " + F(r.CloseT) + " s, on the steady stretch: speed sd " + F(r.SpeedSdClosed, "0.00") + " px/s, gap sd " + F(r.GapSdClosed, "0.00") + " px | began to ease " + r.Starts + "x"
                + " | behind it: within " + F(ArriveNear / 16, "0") + " tiles of the point at " + F(tn) + " s | order ended: ahead " + F(tl) + " s, behind " + F(tf) + " s, " + F(endOff) + " tiles from the point | easing " + F(easing * Tick) + " s"
                + (w.Pathing != null && w.Pathing.FailSafeError != null ? " FAILSAFE " + w.Pathing.FailSafeError.Split('\n')[0] : "");
            return r;
        }

        static bool SetFollow(bool on) => OzV11Kit.TrySet(typeof(ZWorld), "CalmFollowDeviation", on);

        static readonly Dictionary<string, double> alone = new Dictionary<string, double>();
        /// <summary>The slower unit driving the same road alone (the same switches): when its order ends. With passing off the pair cannot be
        /// done before it.</summary>
        public static double LeadAlone(string level, string leadType, int lx, int ly, int gx, int gy)
        {
            string key = level + " " + leadType + " " + lx + "," + ly + ">" + gx + "," + gy + " " + ZWorld.CalmMoveDeviation;
            if (alone.TryGetValue(key, out double t)) return t;
            var w = OzV11Kit.World(SettingsText, NoItems(OzV11Kit.NoUnits(MapText(level))), 1);
            OzV11Kit.Dummy(w, 5 * 16 + 8, 5 * 16 + 8);
            var lead = OzV11Kit.Place(w, leadType, lx * 16 + 16, ly * 16 + 16, Team.Red);
            for (int i = 0; i < 300; i++) { w.Tick(Tick); w.Events.Clear(); }
            double t0 = w.Time; t = double.PositiveInfinity;
            OzV11Kit.PlayerMove(w, lead, gx * 16 + 8, gy * 16 + 8);
            while (w.Time - t0 < 300) { w.Tick(Tick); w.Events.Clear(); if (lead.Waypoints.Count == 0) { t = w.Time - t0; break; } }
            alone[key] = t;
            return t;
        }

        /// <summary>Every case of the table with the rule as given (calm movement on, passing off). line(...) gets one line per case.</summary>
        public static List<Result> Table(bool on, Action<string> line, string only = null)
        {
            var all = new List<Result>();
            OzV11Kit.TrySet(typeof(ZWorld), "CalmMoveDeviation", true); OzV11Kit.TrySet(typeof(ZWorld), "OvertakeDeviation", false);
            bool had = SetFollow(on);
            line?.Invoke("calm follow (DV109) " + (had ? (on ? "ON" : "off") : "n/a") + ", calm movement ON, passing off");
            foreach (var road in Roads)
                foreach (var p in Pairs)
                    foreach (var st in Starts)
                    {
                        string name = road.level + " " + p.lead + ">" + p.fol + " " + F(st.behind, "0") + "t";
                        if (only != null && !name.Contains(only)) continue;
                        var r = Case(road.level, p.lead, p.fol, road.lx, road.ly, road.gx, road.gy, st.behind, st.delay);
                        r.Name = name; r.TAlone = LeadAlone(road.level, p.lead, road.lx, road.ly, road.gx, road.gy); all.Add(r);
                        line?.Invoke("  " + name.PadRight(36) + " " + r.Line);
                    }
            SetFollow(false);
            return all;
        }

        public static string Totals(IEnumerable<Result> rs, string label)
        {
            var l = rs.ToList(); double ft = l.Sum(r => r.FollowTime);
            return label + ": " + l.Count + " cases, behind the slower unit " + F(ft) + " s | stops " + l.Sum(r => r.Stops) + " | speed changes " + l.Sum(r => r.SpeedChanges) + " (" + F(ft > 0 ? l.Sum(r => r.SpeedChanges) * 10 / ft : 0, "0.00") + " per 10 s)"
                + " | speed reversals " + l.Sum(r => r.SpeedReversals) + " | heading reversals " + l.Sum(r => r.Reversals) + ", turns back " + l.Sum(r => r.TurnsBack) + " | inside the spacing " + l.Count(r => !r.GapOk) + " cases"
                + " | began to ease " + l.Sum(r => r.Starts) + "x | the unit behind within 6 tiles of the point " + F(l.Where(r => !double.IsInfinity(r.TNear)).Sum(r => r.TNear)) + " s (never " + l.Count(r => double.IsInfinity(r.TNear)) + ") | orders ended (behind) " + F(l.Where(r => !double.IsInfinity(r.TBehind)).Sum(r => r.TBehind)) + " s, (ahead) " + F(l.Where(r => !double.IsInfinity(r.TLead)).Sum(r => r.TLead)) + " s, not arrived " + l.Count(r => !r.Arrived);
        }

        /// <summary>One verdict over a group of cases, the same cases off (a) and ON (b): 0 stops ON, at most half the speed changes, no more
        /// speed or heading reversals, never inside the spacing ON, the pair arrived (both orders ended) at most 3 % later over the cases the
        /// unit behind arrived in both, not fewer arrivals.</summary>
        static bool Judge(List<Result> a, List<Result> b, string label, StringBuilder sb)
        {
            double fa = a.Sum(r => r.FollowTime), fb = b.Sum(r => r.FollowTime);
            double ca = fa > 0 ? a.Sum(r => r.SpeedChanges) * 10 / fa : 0, cb = fb > 0 ? b.Sum(r => r.SpeedChanges) * 10 / fb : 0;
            var both = Enumerable.Range(0, Math.Min(a.Count, b.Count)).Where(i => a[i].Arrived && b[i].Arrived && !double.IsInfinity(a[i].TPair) && !double.IsInfinity(b[i].TPair)).ToList();
            double ta = both.Sum(i => a[i].TPair), tb = both.Sum(i => b[i].TPair);
            double tr = both.Sum(i => double.IsNaN(b[i].TAlone) || double.IsInfinity(b[i].TAlone) ? a[i].TPair : Math.Max(a[i].TPair, b[i].TAlone)), allow = both.Sum(i => b[i].Allow);
            int sa = a.Sum(r => r.SpeedReversals), sb2 = b.Sum(r => r.SpeedReversals), ra = a.Sum(r => r.Reversals), rb = b.Sum(r => r.Reversals);
            int inA = a.Count(r => !r.GapOk), inB = b.Count(r => !r.GapOk), na = a.Count(r => !r.Arrived), nb = b.Count(r => !r.Arrived);
            int starts = b.Sum(r => r.Starts), maxStarts = b.Count > 0 ? b.Max(r => r.Starts) : 0;
            bool ok = b.Sum(r => r.Stops) == 0 && (cb <= 0.5 * ca || cb <= 0.2) && sb2 <= sa && rb <= ra && inB == 0 && tb <= tr * 1.03 + allow && nb <= na;
            sb.AppendLine(OzV11Kit.Verdict(ok, label + ": stops while the slower unit moves " + a.Sum(r => r.Stops) + " -> " + b.Sum(r => r.Stops) + " (must be 0), speed changes " + F(ca, "0.00") + " -> " + F(cb, "0.00") + " per 10 s (at most half)"
                + ", speed reversals " + sa + " -> " + sb2 + ", heading reversals " + ra + " -> " + rb + " (no more), cases inside the spacing " + inA + " -> " + inB + " (must be 0 ON)"
                + ", the pair arrived (both orders ended) " + F(ta) + " -> " + F(tb) + " s (" + (ta > 0 ? (tb >= ta ? "+" : "") + F((tb / ta - 1) * 100, "0.0") : "-") + " %) over the " + both.Count + " cases the unit behind arrived in both; reference " + F(tr) + " s (the rule-off run, never before the slower unit alone), at most +3 % + one body length of clear road at the slower unit's pace (" + F(allow) + " s) = " + F(tr * 1.03 + allow) + " s; not arrived " + na + " -> " + nb
                + " | ON: began to ease " + starts + "x in " + b.Count + " cases (at most " + maxStarts + " in one), least gap over the spacing " + F(b.Count > 0 ? b.Min(r => r.GapMin - r.Spacing) : double.NaN, "0.0") + " px"));
            return ok;
        }

        /// <summary>The red fort case: Level 16, the red start heavy tank (51,104) and the mech (56,104) behind it, one order WEST along the
        /// road north of the red fort to (34,104); in line, and one tile to the side (south of the tank's line, as in the reported frames);
        /// calm follow off / ON, calm movement off / on. Verdict for every ON run: 0 stops, 0 heading reversals, at most 2 speed
        /// reversals, never inside the spacing, the gap steady once closed up (sd at most 2 px), one start of the easing.</summary>
        public static bool FortRoadCase(Action<string> line)
        {
            bool all = true;
            line?.Invoke("THE RED FORT CASE (Level 16, the road north of the red fort): heavy tank (51,104) ahead, mech (56,104) behind it, one order west along y=104 to (34,104), passing off");
            foreach (double side in new[] { 0.0, -1.0 })
                foreach (bool calm in new[] { false, true })
                {
                    var rs = new Result[2];
                    foreach (bool on in new[] { false, true })
                    {
                        OzV11Kit.TrySet(typeof(ZWorld), "CalmMoveDeviation", calm); OzV11Kit.TrySet(typeof(ZWorld), "OvertakeDeviation", false); SetFollow(on);
                        var r = Case("L16", "heavy", "medium", 51, 104, 34, 104, 5, 0, side: side);
                        rs[on ? 1 : 0] = r;
                        line?.Invoke("  " + ("calm movement " + (calm ? "ON" : "off") + ", calm follow " + (on ? "ON" : "off")).PadRight(36) + " " + r.Line);
                    }
                    SetFollow(false);
                    var b = rs[1]; var a = rs[0];
                    bool ok = b.Stops == 0 && b.Reversals == 0 && b.SpeedReversals <= 2 && b.GapOk && !double.IsNaN(b.GapSdClosed) && b.GapSdClosed <= 2.0 && b.Starts == 1 && b.Arrived;
                    all &= ok;
                    line?.Invoke(OzV11Kit.Verdict(ok, "red fort case, " + (side == 0 ? "in line" : "one tile to the side") + ", calm movement " + (calm ? "ON" : "off") + ": stops " + a.Stops + " -> " + b.Stops + " (must be 0), heading reversals " + a.Reversals + " -> " + b.Reversals + " (must be 0)"
                        + ", speed reversals " + a.SpeedReversals + " -> " + b.SpeedReversals + " (at most 2), least centre gap " + F(a.GapMin, "0.0") + " -> " + F(b.GapMin, "0.0") + " px (spacing " + F(b.Spacing, "0") + "; never inside ON), gap " + F(a.GapMean, "0.0") + " -> " + F(b.GapMean, "0.0") + " px"
                        + ", gap sd once closed up " + F(a.GapSdClosed, "0.00") + " -> " + F(b.GapSdClosed, "0.00") + " px (at most 2), began to ease " + b.Starts + "x (must be 1), the pair arrived " + F(a.TPair) + " -> " + F(b.TPair) + " s"));
                }
            return all;
        }

        /// <summary>A column of four kinds, each faster than the one ahead (heavy tank, mech, light tank, jeep), 4 tiles apart on each road,
        /// one order for all at once; each unit is counted behind the one ahead of it (the mech behind the heavy tank, the light tank behind
        /// the mech that eases itself, the jeep behind the light tank), rule off / ON, calm movement on, passing off; counted until the unit
        /// ahead is within 6 tiles of the point (there the group arrives: the gate's arrival rules), heading reversals from 3 s on (the paths'
        /// first jog onto the road line is the same in both runs). Verdict per road and unit: 0 stops ON,
        /// no more heading reversals, no more speed changes, never inside the spacing ON.</summary>
        public static bool Columns(Action<string> line, string only = null, Action<string> trace = null)
        {
            string[] kinds = { "heavy", "medium", "light", "jeep" };
            bool all = true;
            line?.Invoke("COLUMNS: heavy tank, mech, light tank, jeep in a column 4 tiles apart (each faster than the one ahead), one order for all at once, calm movement on, passing off; each unit counted behind the one ahead of it until that one is within 6 tiles of the point");
            foreach (var road in Roads)
            {
                if (only != null && road.level != only) continue;
                var res = new Meter[2][]; var ends = new double[2]; var arrived = new int[2];
                foreach (bool on in new[] { false, true })
                {
                    OzV11Kit.TrySet(typeof(ZWorld), "CalmMoveDeviation", true); OzV11Kit.TrySet(typeof(ZWorld), "OvertakeDeviation", false); SetFollow(on);
                    var w = OzV11Kit.World(SettingsText, NoItems(OzV11Kit.NoUnits(MapText(road.level))), 1);
                    OzV11Kit.Dummy(w, 5 * 16 + 8, 5 * 16 + 8);
                    int lcx = road.lx * 16 + 16, lcy = road.ly * 16 + 16, gcx = road.gx * 16 + 8, gcy = road.gy * 16 + 8;
                    double dx = gcx - lcx, dy = gcy - lcy, dl = Math.Sqrt(dx * dx + dy * dy); dx /= dl; dy /= dl;
                    var u = new ZObject[kinds.Length];
                    for (int i = 0; i < kinds.Length; i++) u[i] = OzV11Kit.Place(w, kinds[i], (int)Math.Round(lcx - dx * i * 64), (int)Math.Round(lcy - dy * i * 64), Team.Red);
                    for (int i = 0; i < 300; i++) { w.Tick(Tick); w.Events.Clear(); }
                    double t0 = w.Time; foreach (var o in u) OzV11Kit.PlayerMove(w, o, gcx, gcy);
                    var ms = new Meter[kinds.Length]; var done = new bool[kinds.Length];
                    for (int i = 1; i < kinds.Length; i++) ms[i] = new Meter(u[i - 1], u[i], trace != null, gcx, gcy) { EndZone = 96, JogTime = 3 };
                    double end = double.PositiveInfinity;
                    while (w.Time - t0 < 300)
                    {
                        w.Tick(Tick); w.Events.Clear();
                        for (int i = 0; i < kinds.Length; i++) if (!done[i] && u[i].Waypoints.Count == 0) done[i] = true;
                        for (int i = 1; i < kinds.Length; i++) ms[i].Step(w.Time, !done[i], t0);
                        if (done.All(x => x)) { end = w.Time - t0; break; }
                    }
                    int k = on ? 1 : 0; res[k] = ms; ends[k] = end;
                    if (trace != null) { trace("== " + road.level + " calm follow " + (on ? "ON" : "off")); int n = ms[1].Trace.Count; for (int j = 0; j < n; j++) for (int i = 1; i < kinds.Length; i++) if (j < ms[i].Trace.Count) trace(kinds[i] + " " + ms[i].Trace[j]); }
                    arrived[k] = u.Count(o => Math.Sqrt((double)(o.CenterX - gcx) * (o.CenterX - gcx) + (double)(o.CenterY - gcy) * (o.CenterY - gcy)) <= 6 * 16 + 2 * ZPathing.Radius(o) * 3);
                }
                SetFollow(false);
                for (int i = 1; i < kinds.Length; i++)
                {
                    var a = res[0][i]; var b = res[1][i];
                    double sp = ZPathing.VehicleRadiusOf(kinds[i]) + ZPathing.VehicleRadiusOf(kinds[i - 1]) + 1;
                    bool gapOk = double.IsInfinity(b.GapMin) || b.GapMin >= sp - 1e-6;
                    bool ok = b.Stops == 0 && b.Reversals <= a.Reversals && b.SpeedChanges <= a.SpeedChanges && gapOk;
                    all &= ok;
                    line?.Invoke(OzV11Kit.Verdict(ok, "column " + road.level + ": " + kinds[i] + " behind the " + kinds[i - 1] + ": stops " + a.Stops + " -> " + b.Stops + " (must be 0), speed changes " + a.SpeedChanges + " -> " + b.SpeedChanges + " (no more), speed reversals " + a.SpeedReversals + " -> " + b.SpeedReversals
                        + ", heading reversals " + a.Reversals + " -> " + b.Reversals + " (no more), least centre gap " + F(a.GapMin, "0.0") + " -> " + F(b.GapMin, "0.0") + " px (spacing " + F(sp, "0") + "; never inside ON), gap sd once closed up " + F(a.GapSdClosed, "0.00") + " -> " + F(b.GapSdClosed, "0.00") + " px, began to ease " + b.Starts + "x"));
                }
                line?.Invoke("  column " + road.level + ": the last order ended " + F(ends[0]) + " -> " + F(ends[1]) + " s (the heavy tank alone: " + F(LeadAlone(road.level, "heavy", road.lx, road.ly, road.gx, road.gy)) + " s; without the rule the faster units squeeze past and the heavy tank's order can end short behind them), at the point (within 6 tiles + 3 body lengths) " + arrived[0] + " -> " + arrived[1] + " of " + kinds.Length);
            }
            return all;
        }

        /// <summary>A new order after a follow: Desert road, a mech 3 tiles behind a missile launcher; the launcher to the far point, the mech to a
        /// point 8 tiles short of it, so its order ends while it still eases behind the launcher; 5 s after both orders ended the mech is sent
        /// back north (a free road). The time from the order to its own full speed (a 0.1 s window at 95 %
        /// of it) and the way it made in the first second, the rule off and ON: must be the same.</summary>
        public static (double tFull, double way1, bool wasEasing) Restart(bool on)
        {
            OzV11Kit.TrySet(typeof(ZWorld), "CalmMoveDeviation", true); OzV11Kit.TrySet(typeof(ZWorld), "OvertakeDeviation", false); SetFollow(on);
            try
            {
                var w = OzV11Kit.World(SettingsText, NoItems(OzV11Kit.NoUnits(MapText("L02"))), 1);
                OzV11Kit.Dummy(w, 5 * 16 + 8, 5 * 16 + 8);
                var lead = OzV11Kit.Place(w, "missile_launcher", 52 * 16 + 16, 18 * 16 + 16, Team.Red);
                var fol = OzV11Kit.Place(w, "medium", 52 * 16 + 16, 15 * 16 + 16, Team.Red);
                for (int i = 0; i < 300; i++) { w.Tick(Tick); w.Events.Clear(); }
                OzV11Kit.PlayerMove(w, lead, 52 * 16 + 8, 44 * 16 + 8); OzV11Kit.PlayerMove(w, fol, 52 * 16 + 8, 36 * 16 + 8);
                double t0 = w.Time; bool was = false, folDone = false;
                while (w.Time - t0 < 120 && (lead.Waypoints.Count > 0 || fol.Waypoints.Count > 0))
                {
                    w.Tick(Tick); w.Events.Clear();
                    if (!folDone && fol.Waypoints.Count == 0) { folDone = true; was = fol.CalmFollowing; }
                }
                int second = (int)Math.Round(1.0 / Tick), window = (int)Math.Round(0.1 / Tick);
                for (int i = 0; i < 5 * second; i++) { w.Tick(Tick); w.Events.Clear(); }                     // 5 s
                OzV11Kit.PlayerMove(w, fol, fol.CenterX, 12 * 16 + 8);
                double sx = fol.PosX, sy = fol.PosY, wx = sx, wy = sy, tFull = double.PositiveInfinity, way1 = 0; t0 = w.Time;
                for (int k = 1; k <= 2 * second; k++)
                {
                    w.Tick(Tick); w.Events.Clear();
                    if (k == second) way1 = Math.Sqrt((fol.PosX - sx) * (fol.PosX - sx) + (fol.PosY - sy) * (fol.PosY - sy));
                    if (k % window == 0)
                    {
                        double v = Math.Sqrt((fol.PosX - wx) * (fol.PosX - wx) + (fol.PosY - wy) * (fol.PosY - wy)) / (window * Tick); wx = fol.PosX; wy = fol.PosY;
                        if (double.IsInfinity(tFull) && fol.RealMoveSpeed > 0 && v >= 0.95 * fol.RealMoveSpeed) tFull = w.Time - t0;
                    }
                }
                return (tFull, way1, was);
            }
            finally { SetFollow(false); }
        }

        /// <summary>KC1's crowd runs (OzKC1Count.RunCrowd: six robot squads, two light tanks, two jeeps of one team, one group order, through
        /// the level's bridge or road and across the front of its own fort gate; calm movement on, FRIENDLY UNITS OVERLAP = NO, the game's
        /// default) with calm follow off and ON. Verdict per run: as many bodies arrive, the last order ends at most 3 % later.</summary>
        public static bool Crowds(Action<string> line)
        {
            bool all = true;
            line?.Invoke("KC1's crowd runs (23 bodies of one team, one group order, calm movement on, overlap NO): calm follow off -> ON");
            foreach (bool gate in new[] { false, true })
                foreach (var lv in OzKC1Count.Levels)
                {
                    OzKC1Count.CrowdRun a, b;
                    try { InChecks = false; a = OzKC1Count.RunCrowd(lv, false, gate); InChecks = true; b = OzKC1Count.RunCrowd(lv, false, gate); }
                    finally { InChecks = false; }
                    double ra = a.Bodies > 0 ? (double)a.Reversals / a.Bodies : 0, rb = b.Bodies > 0 ? (double)b.Reversals / b.Bodies : 0;
                    bool lastOk = double.IsInfinity(b.Last) ? double.IsInfinity(a.Last) : double.IsInfinity(a.Last) || b.Last <= a.Last * 1.03;
                    bool ok = b.Arrived >= a.Arrived && lastOk;
                    all &= ok;
                    line?.Invoke(OzV11Kit.Verdict(ok, "crowd " + lv + (gate ? " fort gate" : " bridge / road") + " (" + a.Bodies + " bodies): arrived " + a.Arrived + " -> " + b.Arrived + " (not fewer), last order ended " + F(a.Last) + " -> " + F(b.Last) + " s (at most +3 %), reversals per body " + F(ra, "0.00") + " -> " + F(rb, "0.00")));
                }
            return all;
        }

        /// <summary>The check: the table with the rule off and on, verdicts per map and per follower kind; the red fort case; a new order after
        /// a follow; KC1's crowd runs.</summary>
        public static string Run()
        {
            var sb = new StringBuilder(); alone.Clear();
            var off = Table(false, s => sb.AppendLine(s));
            var on = Table(true, s => sb.AppendLine(s));
            bool all = true;
            foreach (var lv in Roads.Select(r => r.level).Distinct())
            {
                var a = off.Where(r => r.Name.StartsWith(lv)).ToList(); var b = on.Where(r => r.Name.StartsWith(lv)).ToList();
                sb.AppendLine(Totals(a, lv + " rule off")); sb.AppendLine(Totals(b, lv + " rule ON "));
                all &= Judge(a, b, lv, sb);
            }
            sb.AppendLine("Per follower kind (every slower kind ahead, the three roads, both starts):");
            foreach (var kind in Pairs.Select(p => p.fol).Distinct())
            {
                var a = off.Where(r => r.Fol == kind).ToList(); var b = on.Where(r => r.Fol == kind).ToList();
                all &= Judge(a, b, "behind a slower unit: " + kind + " (" + string.Join(", ", Pairs.Where(p => p.fol == kind).Select(p => p.lead)) + ")", sb);
            }
            all &= FortRoadCase(s => sb.AppendLine(s));
            all &= Columns(s => sb.AppendLine(s));
            var r0 = Restart(false); var r1 = Restart(true);
            bool rok = Math.Abs(r1.tFull - r0.tFull) <= 1e-6 + Tick && Math.Abs(r1.way1 - r0.way1) <= 0.5;
            all &= rok;
            sb.AppendLine(OzV11Kit.Verdict(rok, "a new order 5 s after a follow ended (the mech easing at its end: " + (r1.wasEasing ? "yes" : "no") + "): to its own full speed " + F(r0.tFull, "0.00") + " -> " + F(r1.tFull, "0.00") + " s, way in the first second " + F(r0.way1, "0.0") + " -> " + F(r1.way1, "0.0") + " px (the same)"));
            all &= Crowds(s => sb.AppendLine(s));
            sb.AppendLine(all ? "ALL PASS" : "FAIL");
            return sb.ToString();
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Count Calm Follow (KC1b)")]
        static void Menu() => OzV11Kit.RunMenu("Count Calm Follow (KC1b)", Run);

        // In Play, for the before / after takes: the rule off (or on again) in the running level only; nothing is saved, the next level
        // start sets it from the game's settings again (View/ZGame.cs).
        [UnityEditor.MenuItem("OpenZ/V1.1/Calm Follow (Play)/Calm follow OFF until the next level start")]
        static void FollowOff() => Say(ZWorld.CalmFollowDeviation = false);
        [UnityEditor.MenuItem("OpenZ/V1.1/Calm Follow (Play)/Calm follow ON again")]
        static void FollowOn() => Say(ZWorld.CalmFollowDeviation = true);
        [UnityEditor.MenuItem("OpenZ/V1.1/Calm Follow (Play)/Reset the counters")]
        static void Reset()
        {
            var g = UnityEngine.Object.FindFirstObjectByType<ZGame>();
            g?.World?.Pathing?.ResetFollowCounters();
            UnityEngine.Debug.Log("[OpenZ V1.1] calm follow (DV109): counters cleared");
        }
        [UnityEditor.MenuItem("OpenZ/V1.1/Calm Follow (Play)/Report the counters")]
        static void Counters()
        {
            var g = UnityEngine.Object.FindFirstObjectByType<ZGame>();
            var w = g != null ? g.World : null; var p = w?.Pathing;
            int easing = 0; if (w != null) foreach (var o in w.Objects) if (o.CalmFollowing) easing++;
            int pairs = p?.FollowPairs ?? 0, starts = p?.FollowStarts ?? 0;
            double gm = p != null ? p.FollowGapMin : double.PositiveInfinity;
            UnityEngine.Debug.Log($"[OpenZ V1.1] calm follow (DV109): {(ZWorld.CalmFollowDeviation ? "ON" : "off")}; units easing behind a slower one now {easing}; since the level start (or the reset): eased steps {p?.FollowTicks ?? 0} (ticks), times a unit began to ease {starts}, followed pairs {pairs}, starts per pair {(pairs > 0 ? ((double)starts / pairs).ToString("0.00", CultureInfo.InvariantCulture) : "-")} (1 on a straight road), least gap over the spacing {(double.IsInfinity(gm) ? "-" : gm.ToString("0.0", CultureInfo.InvariantCulture))} px (at least 0: never inside the spacing)");
        }
        static void Say(bool on) => UnityEngine.Debug.Log($"[OpenZ V1.1] calm follow (DV109): {(on ? "ON" : "OFF until the next level start")} (Play only, nothing saved)");
#endif
    }
}
