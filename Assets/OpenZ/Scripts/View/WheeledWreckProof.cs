using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// <c></c>
    /// free lane near the camera a neutral jeep, APC, crane, MLRS (and in the stills run a light tank) are placed, the CPU bot paused,
    /// and each is killed in turn with the camera on it.
    /// <para><see cref="ProofCapture.SelfCheck"/>
    /// through <see cref="ProofCapture"/> (full resolution, no Blit = no double sRGB encode). Stills run (clip false): living APCs
    /// driving (open cut vs closed tyre) as wheel crops at 60 fps, per death the alive frame + wheel crop, a 60 fps crop strip of the
    /// flying tyre (Time.captureFramerate 60 for the strip only) with per-frame pixel counts inside the tyre's own projected
    /// outline (sand-hued / light-grey), timed frames, the wreck close-up (+ the old core colour for the before / after pair).
    /// Clip run (clip true): NO stills (Grab hitches a 60 fps recording), real speed, every death: ~1.2 s wide on the intact vehicle,
    /// the kill, the camera follows the newest wheel at height 13 (frame-rate independent ease) until it is at rest + 1 s.</para>
    /// </summary>
    public sealed class WheeledWreckProof : MonoBehaviour
    {
        public static string Root = global::OpenZ.OzRelease.Milestones("task_wheeled_wreck_breakup");
        public static string Sub = "";
        public static string Status = "idle";
        static readonly StringBuilder log = new StringBuilder();
        string dir, cdir; int shots;
        RtsCamera rts; float minH0 = -1f;

        static void Log(string s) { log.AppendLine(System.DateTime.Now.ToString("HH:mm:ss.fff") + " " + s); Debug.Log("[WheeledWreckProof] " + s); Status = s; }

        /// <summary>plain = WreckFx.Force 2 for the run (no DV17 flip / DV18 break on top: the tear-off alone is seen; restored after).</summary>
        public static string Run(bool clip, bool plain)
        {
            var g = ZGame.I; if (!g || g.World == null) return "no game";
            log.Length = 0; Status = "running";
            var h = new GameObject("WheeledWreckProof").AddComponent<WheeledWreckProof>();
            h.StartCoroutine(h.Routine(clip, plain));
            return "wheeled wreck proof started on " + g.level + (clip ? " (clip: no stills)" : " (stills)");
        }

        IEnumerator Grab(string name)
        {
            yield return new WaitForEndOfFrame();
            ProofCapture.SaveJpg(Path.Combine(dir, name + ".jpg"));
            shots++;
        }

        static void Kill(ZWorld W, ZObject o) { o.SetHealth(0); W.UpdateObjectHealth(o, -1); }

        void Cam(Vector3 p, float h) { if (!rts) return; rts.focus = new Vector3(p.x, 0f, p.z); rts.SetHeightImmediate(h); }

        /// <summary>A 15 x 7 tile strip of plain ground / road with no object, nearest to the camera (L02 has no 22 x 15 FindLane lane).</summary>
        static string SmallLane(ZWorld W)
        {
            var m = W.Map; var rts = FindFirstObjectByType<RtsCamera>();
            Vector3 f = rts ? rts.focus : Vector3.zero; int ftx = (int)(f.x * 0.5f), fty = (int)(-f.z * 0.5f);
            int best = int.MaxValue, bx = -1, by = -1;
            for (int ty = 6; ty < m.height - 6; ty++)
                for (int tx = 8; tx < m.width - 8; tx++)
                {
                    int d = (tx - ftx) * (tx - ftx) + (ty - fty) * (ty - fty); if (d >= best) continue;
                    bool ok = true;
                    for (int y = ty - 3; y <= ty + 3 && ok; y++) for (int x = tx - 7; x <= tx + 7 && ok; x++) { var c = m.ClassAt(x, y); if (c != ZClass.Ground && c != ZClass.Road) ok = false; }
                    if (!ok) continue;
                    int x0 = (tx - 7) * 16, x1 = (tx + 7) * 16, y0 = (ty - 3) * 16, y1 = (ty + 3) * 16;
                    foreach (var o in W.Objects) if (o != null && !o.Destroyed && o.CenterX + o.WidthPix / 2 > x0 && o.CenterX - o.WidthPix / 2 < x1 && o.CenterY + o.HeightPix / 2 > y0 && o.CenterY - o.HeightPix / 2 < y1) { ok = false; break; }
                    if (ok) { best = d; bx = tx; by = ty; }
                }
            return bx < 0 ? "none" : bx + "," + by;
        }

        static Transform FirstWheel(Transform root)
        {
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>()) if (mr.enabled && WheeledBreakup.IsWheelName(mr.name)) return mr.transform;
            return null;
        }

        /// <summary>A square crop (PNG) around a wheel from the current frame + the pixel counts inside the wheel's own projected
        /// outline (its mesh triangles rasterised, eroded 1 px): sand-hued (L &gt; 150, R - B &gt;= 32) and light grey (L &gt; 150, R - B &lt; 32).</summary>
        string Crop(Texture2D tex, Transform wheel, string name)
        {
            var cam = Camera.main; if (!cam || !wheel || !wheel.TryGetComponent<MeshFilter>(out var mf) || !mf.sharedMesh) return name + ": no wheel";
            var b = wheel.GetComponent<Renderer>().bounds; var c = b.center;
            var sc = cam.WorldToScreenPoint(c); if (sc.z <= 0f) return name + ": behind camera";
            float rpx = (cam.WorldToScreenPoint(c + cam.transform.up * b.extents.magnitude) - sc).magnitude;
            int S = Mathf.Clamp(Mathf.RoundToInt(rpx * 2.6f), 48, 420);
            int x0 = Mathf.Clamp(Mathf.RoundToInt(sc.x) - S / 2, 0, tex.width - S), y0 = Mathf.Clamp(Mathf.RoundToInt(sc.y) - S / 2, 0, tex.height - S);
            var px = tex.GetPixels(x0, y0, S, S);
            var o = new Texture2D(S, S, TextureFormat.RGB24, false); o.SetPixels(px); o.Apply();
            File.WriteAllBytes(Path.Combine(cdir, name + ".png"), o.EncodeToPNG()); Destroy(o);
            // the outline: every triangle of the wheel mesh (and of its core) projected into the crop
            var mask = new bool[S * S];
            foreach (var f in wheel.GetComponentsInChildren<MeshFilter>())
            {
                var m = f.sharedMesh; if (!m || !m.isReadable) continue;
                var v = m.vertices; var t = m.triangles; var l2w = f.transform.localToWorldMatrix; var sp = new Vector2[v.Length];
                for (int i = 0; i < v.Length; i++) { var q = cam.WorldToScreenPoint(l2w.MultiplyPoint3x4(v[i])); sp[i] = new Vector2(q.x - x0, q.y - y0); }
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    Vector2 A = sp[t[i]], B = sp[t[i + 1]], C = sp[t[i + 2]];
                    int minx = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.x, Mathf.Min(B.x, C.x)))), maxx = Mathf.Min(S - 1, Mathf.CeilToInt(Mathf.Max(A.x, Mathf.Max(B.x, C.x))));
                    int miny = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A.y, Mathf.Min(B.y, C.y)))), maxy = Mathf.Min(S - 1, Mathf.CeilToInt(Mathf.Max(A.y, Mathf.Max(B.y, C.y))));
                    float den = (B.y - C.y) * (A.x - C.x) + (C.x - B.x) * (A.y - C.y); if (Mathf.Abs(den) < 1e-6f) continue;
                    for (int y = miny; y <= maxy; y++) for (int x = minx; x <= maxx; x++)
                        {
                            float X = x + 0.5f, Y = y + 0.5f;
                            float l1 = ((B.y - C.y) * (X - C.x) + (C.x - B.x) * (Y - C.y)) / den, l2 = ((C.y - A.y) * (X - C.x) + (A.x - C.x) * (Y - C.y)) / den;
                            if (l1 >= 0f && l2 >= 0f && l1 + l2 <= 1f) mask[y * S + x] = true;
                        }
                }
            }
            int inside = 0, sand = 0, grey = 0;
            for (int y = 1; y < S - 1; y++) for (int x = 1; x < S - 1; x++)
                {
                    int k = y * S + x; if (!mask[k] || !mask[k - 1] || !mask[k + 1] || !mask[k - S] || !mask[k + S]) continue;
                    inside++; var p = px[k]; float L = (0.2126f * p.r + 0.7152f * p.g + 0.0722f * p.b) * 255f, rb = (p.r - p.b) * 255f;
                    if (L > 150f) { if (rb >= 32f) sand++; else grey++; }
                }
            return $"{name}: {S}px crop, tyre outline {inside} px, sand-hued {sand}, light-grey {grey}";
        }

        IEnumerator CropNow(Transform wheel, string name, List<string> notes)
        {
            yield return new WaitForEndOfFrame();
            var tex = ProofCapture.Frame();
            notes.Add(Crop(tex, wheel, name));
            Destroy(tex);
        }

        IEnumerator Routine(bool clip, bool plain)
        {
            var g = ZGame.I; var W = g.World;
            dir = Path.Combine(Root, Sub, g.level); Directory.CreateDirectory(dir);
            cdir = Path.Combine(dir, "crops"); Directory.CreateDirectory(cdir);
            rts = FindFirstObjectByType<RtsCamera>();
            if (rts) { minH0 = rts.minHeight; rts.minHeight = 10f; }                                      // session-only: the close follow at 12-14 m
            yield return ProofCapture.SelfCheck(dir, g.level);
            Log(ProofCapture.LastCheck);
            if (!ProofCapture.LastOk) { Log("FAIL: capture gamma self-check failed, proof stopped"); Finish(); yield break; }
            int force0 = WreckFx.Force; if (plain) WreckFx.Force = 2;
            var lane = SmallLane(W);
            Log($"{g.level}: lane {lane}; screen {Screen.width}x{Screen.height}; testmode {PlayerPrefs.GetInt("oz_testmode", 0)}; clip {clip}; targetFrameRate {Application.targetFrameRate}, vSync {QualitySettings.vSyncCount}");
            var sp = lane.Split(','); if (sp.Length != 2) { Log("no lane"); WreckFx.Force = force0; Finish(); yield break; }
            int tx = int.Parse(sp[0]), ty = int.Parse(sp[1]);
            var bots = new List<ZBot>(W.Bots); W.Bots.Clear();
            float snd0 = Time.unscaledTime;
            var notes = new List<string>();

            if (!clip)
            {   // living wheels while driving: the old open cut vs the closed tyre (APC + crane)
                var live = new List<(ZObject o, string label)>();
                bool cl0 = WheelSplit.ClosedLiving;
                foreach (var (type, closed, row) in new[] { ("apc", false, -2), ("apc", true, 0), ("crane", true, 2) })
                {
                    WheelSplit.ClosedLiving = closed;
                    var o = W.CreateObject("vehicle", type, (tx - 6) * 16, (ty + row) * 16, Team.Red);
                    if (o == null) { Log("living spawn failed " + type); continue; }
                    g.SpawnUnitView(o); o.Damage = 0; o.AttackRadius = 0;
                    live.Add((o, type + (closed ? "_closed" : "_open")));
                    yield return null;
                }
                WheelSplit.ClosedLiving = cl0;
                Log("flight mesh: " + WheelSplit.FlightLog);
                yield return new WaitForSeconds(1f);
                foreach (var (o, _) in live) W.SendWaypoints(Team.Red, o, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Move, refId = -1, x = (tx + 6) * 16 + 8, y = o.CenterY } });
                yield return new WaitForSeconds(1.6f);
                if (live.Count > 0 && g.Views.TryGetValue(live[1 % live.Count].o.Id, out var lv) && lv) Cam(lv.transform.position, 13f);
                yield return new WaitForSeconds(0.3f);
                Time.captureFramerate = 60;
                for (int f = 0; f < 16; f++)
                {
                    if (live.Count > 0 && g.Views.TryGetValue(live[1 % live.Count].o.Id, out var cv) && cv && rts) { var p = cv.transform.position; rts.focus = new Vector3(p.x, 0f, p.z); }
                    yield return new WaitForEndOfFrame();
                    var tex = ProofCapture.Frame();
                    if (f == 0) File.WriteAllBytes(Path.Combine(dir, "living_driving.jpg"), tex.EncodeToJPG(92));
                    foreach (var (o, label) in live)
                        if (g.Views.TryGetValue(o.Id, out var v) && v) { var wt = FirstWheel(v.transform); if (f % 3 == 0) notes.Add(Crop(tex, wt, $"living_{label}_f{f:00}")); else Crop(tex, wt, $"living_{label}_f{f:00}"); }
                    Destroy(tex);
                }
                Time.captureFramerate = 0;
                yield return Oblique(live, notes);
                foreach (var (o, _) in live) if (!o.Destroyed) Kill(W, o);
                yield return new WaitForSeconds(9f);                                                       // their wrecks burn out / sink before the lane is used
            }

            var types = clip ? new[] { "jeep", "apc", "crane", "missile_launcher" } : new[] { "jeep", "apc", "crane", "missile_launcher", "light" };
            for (int i = 0; i < types.Length; i++)
            {   // spawned just in time (a neighbour's blast never kills the next one early), 3 tiles apart
                var o = W.CreateObject("vehicle", types[i], (tx - 6 + i * 3) * 16, ty * 16, Team.Neutral);
                if (o == null) { Log("spawn failed " + types[i]); continue; }
                g.SpawnUnitView(o);
                yield return new WaitForSeconds(clip ? 0.4f : 1.2f);
                if (!g.Views.TryGetValue(o.Id, out var v) || !v) { Log("no view " + o.Type); continue; }
                Cam(v.transform.position, clip ? 16f : 24f);
                int made0 = RollingWheel.Made;
                float t0;
                if (clip)
                {   // the take: wide on the intact vehicle, the kill, follow the newest wheel until it rests + 1 s
                    yield return new WaitForSeconds(1.2f);
                    Kill(W, o); t0 = Time.time; float restAt = -1f;
                    while (Time.time - t0 < 14f)
                    {
                        var w = RollingWheel.Made > made0 ? RollingWheel.Newest : null;
                        float k = 1f - Mathf.Pow(0.75f, Time.deltaTime * 10f);
                        if (rts && w) { var p = w.transform.position; rts.focus = Vector3.Lerp(rts.focus, new Vector3(p.x, 0f, p.z), k); rts.SetHeightImmediate(Mathf.Lerp(rts.height, 13f, k)); }
                        bool rest = Time.time - t0 > 1.5f && (RollingWheel.Made == made0 || !w || (w.TryGetComponent<Rigidbody>(out var rb) && rb.isKinematic));
                        if (rest && restAt < 0f) restAt = Time.time;
                        if (restAt > 0f && Time.time - restAt > 1f) break;
                        yield return null;
                    }
                    Log($"{o.Type}: {(WheeledBreakup.Applies(o) ? WheeledBreakup.Last : "tank")}; take {Time.time - t0:F1} s after the kill");
                    continue;
                }
                yield return new WaitForSeconds(0.5f);
                yield return Grab($"{o.Type}_0_alive");
                var aliveWheel = FirstWheel(v.transform);
                if (aliveWheel) yield return CropNow(aliveWheel, $"{o.Type}_alive_wheel", notes);
                Kill(W, o); t0 = Time.time;
                // the flying tyre over a full turn: 60 fps game time for the strip only
                while (RollingWheel.Made == made0 && Time.time - t0 < 0.3f) yield return null;
                var fw = RollingWheel.Made > made0 ? RollingWheel.Newest : null;
                if (fw)
                {
                    var tyre = FirstWheel(fw.transform) ?? fw.transform.GetChild(0);
                    var mfm = tyre ? tyre.GetComponent<MeshFilter>() : null;
                    Log($"{o.Type}: flying tyre {tyre?.name}: mesh {(mfm ? mfm.sharedMesh.name : "-")}, open edges {(mfm ? WheelSplit.OpenEdges(mfm.sharedMesh) : -1)}, mats {(tyre ? string.Join("/", System.Array.ConvertAll(tyre.GetComponent<MeshRenderer>().sharedMaterials, m => m ? m.name : "-")) : "-")}");
                    Time.captureFramerate = 60;
                    for (int f = 0; f < 30; f++)
                    {
                        if (rts && fw) { var p = fw.transform.position; rts.focus = new Vector3(p.x, 0f, p.z); rts.SetHeightImmediate(13f); }
                        yield return new WaitForEndOfFrame();
                        var tex = ProofCapture.Frame();
                        if (f == 0 || f == 15) File.WriteAllBytes(Path.Combine(dir, $"{o.Type}_tyre_frame_{f:00}.jpg"), tex.EncodeToJPG(92));
                        notes.Add(Crop(tex, tyre, $"{o.Type}_tyre_f{f:00}"));
                        Destroy(tex);
                    }
                    Time.captureFramerate = 0;
                }
                else Log($"{o.Type}: no flying tyre (tracked or plain debris)");
                Cam(v ? v.transform.position : Vector3.zero, 24f);
                foreach (var at in new[] { 1.2f, 2.6f })
                {
                    while (Time.time - t0 < at) yield return null;
                    yield return Grab($"{o.Type}_{at:0.00}s");
                }
                if (v) Cam(v.transform.position, 12f);
                while (Time.time - t0 < 6.5f) yield return null;
                yield return Grab($"{o.Type}_wreck_close_6.5s");                                                 // the charred wreck with its holes
                if (o.Type == "apc" || o.Type == "crane")
                {
                    float ts = Time.timeScale; Time.timeScale = 0f;
                    if (v) { WheeledBreakup.SetWreckSheen(v.transform, true); yield return null; yield return Grab($"{o.Type}_wreck_close_sheen_on"); WheeledBreakup.SetWreckSheen(v.transform, false); yield return null; yield return Grab($"{o.Type}_wreck_close_matte"); }
                    foreach (var hh in new[] { 30f, 60f }) { if (v) Cam(v.transform.position, hh); yield return null; yield return null; yield return Grab($"{o.Type}_wreck_h{hh:0}"); }
                    Time.timeScale = ts;
                    Log($"{o.Type}: hole fix {WheeledBreakup.HoleFix}: rim fragments {WheeledBreakup.RimFrags}, rim tris {WheeledBreakup.RimTris}, matte fragments {WheeledBreakup.MatteFrags}, cavities under a measured low rim {WheeledBreakup.CavityLowRim}");
                }
                Log($"{o.Type}: {(WheeledBreakup.Applies(o) ? WheeledBreakup.Last : "tank (not wheeled): turret toss + DV18 as before")}; rolling wheels live {RollingWheel.Live}");
                yield return new WaitForSeconds(1.5f);
            }
            if (!clip)
            {   // water: a wheel dropped over the nearest water tile
                var m = W.Map; int best = int.MaxValue, wx = -1, wy = -1;
                for (int y = 2; y < m.height - 2; y++) for (int x = 2; x < m.width - 2; x++)
                    {
                        var c = m.ClassAt(x, y); if (c != ZClass.Water && c != ZClass.DeepWater) continue;
                        int d = (x - tx) * (x - tx) + (y - ty) * (y - ty); if (d < best) { best = d; wx = x; wy = y; }
                    }
                if (wx >= 0)
                {
                    var jeep = W.CreateObject("vehicle", "jeep", tx * 16, (ty - 4) * 16, Team.Neutral);
                    if (jeep != null)
                    {
                        g.SpawnUnitView(jeep); yield return new WaitForSeconds(1f);
                        int made0 = RollingWheel.Made; Kill(W, jeep); yield return new WaitForSeconds(0.3f);
                        var w = RollingWheel.Made > made0 ? RollingWheel.Newest : null;
                        if (w)
                        {
                            var wp = ZGame.PxToWorld(wx * 16 + 8, wy * 16 + 8); var fxp = OpenZFxPlayer.I; float sy = fxp ? fxp.SurfaceY(wp) : 0f;
                            var rb = w.GetComponent<Rigidbody>();
                            rb.position = new Vector3(wp.x, sy + 3f, wp.z); rb.linearVelocity = Vector3.down * 2f;
                            Cam(wp, 24f);
                            float s0 = RollingWheel.Splashes;
                            yield return new WaitForSeconds(0.6f); yield return Grab("water_splash");
                            yield return new WaitForSeconds(1.5f); yield return Grab("water_sinking");
                            Log($"water: wheel dropped over tile {wx},{wy} (surface y {sy:F2}): splashes +{RollingWheel.Splashes - s0}");
                        }
                        else Log("water: the jeep threw no rolling wheel");
                    }
                }
                else Log("water: no water tile");
            }
            float tw = Time.time;
            while (RollingWheel.Live > 0 && Time.time - tw < 16f) yield return null;
            Log($"RollingWheel: made {RollingWheel.Made}, burnt tyres {RollingWheel.Burnt}, peak live {RollingWheel.PeakLive} (cap {RollingWheel.MaxRolling}), bounces {RollingWheel.Bounces}, flats {RollingWheel.Flats}, splashes {RollingWheel.Splashes}, sinks {RollingWheel.Sinks}, pops {RollingWheel.Pops}, roll loops {RollingWheel.LoopStarts}, max speed {RollingWheel.MaxSpeed:F1} m/s, max roll {RollingWheel.MaxDist:F1} m");
            foreach (var l in RollingWheel.Log) Log("  " + l);
            Log($"task_wheel_rest2: tyre cores made {WheelSplit.TyreCoreMade} (half width {WheelSplit.TyreCoreHalf:F2}), skipped on closed flight meshes {WheelSplit.TyreCoreSkipped}; rim meshes freed so far {OzOwnedMesh.Freed}");
            Log($"WheeledBreakup: deaths {WheeledBreakup.Deaths}, parts {WheeledBreakup.Parts} (hull bins {WheeledBreakup.HullBins}, hole cores {WheeledBreakup.Cores}), wheels {WheeledBreakup.Wheels}, axle stubs {WheeledBreakup.Stubs}, slumps {WheeledBreakup.Slumps}");
            foreach (var n in notes) Log("  crop " + n);
            var keys = new SortedDictionary<string, int>();
            foreach (var e in OzSoundLog.Since(snd0))
            {
                if (e.key == null) continue;
                bool wheelish = e.src == "WHEEL" || e.key == "debris" || e.key == RobotPopDeath.SoundKey || e.key == "dst_debris_metal" || e.key == "dst_turret_toss" || e.key == "dst_debris_splash" || e.key == "turret";
                if (!wheelish) continue;
                string k = e.src + ":" + e.key + (string.IsNullOrEmpty(e.clip) ? "" : " [" + e.clip + "]") + " " + (e.outcome ?? "");
                if (k.Length > 90) k = k.Substring(0, 90);
                keys.TryGetValue(k, out int nk); keys[k] = nk + 1;
            }
            foreach (var kv in keys) Log($"  sound {kv.Key} x{kv.Value}");
            W.Bots.AddRange(bots); WreckFx.Force = force0;
            Log($"frames {shots} in {dir}");
            Finish();
        }

        /// <summary>
        /// the frames: the closed tyre vs the old open cut where the wheel sits under its arch (the top-down crops only saw the hull).</summary>
        IEnumerator Oblique(List<(ZObject o, string label)> live, List<string> notes)
        {
            var cam = Camera.main; if (!cam) yield break;
            var g = ZGame.I; var camT = cam.transform; var p0 = camT.position; var r0 = camT.rotation; bool on = rts && rts.enabled;
            if (rts) rts.enabled = false;
            foreach (var (o, label) in live)
            {
                if (!g.Views.TryGetValue(o.Id, out var v) || !v) continue;
                var wt = FirstWheel(v.transform); if (!wt) continue;
                var wc = wt.GetComponent<Renderer>().bounds.center;
                var side = v.transform.right; side.y = 0f; side.Normalize(); if (Vector3.Dot(wc - v.transform.position, side) < 0f) side = -side;
                camT.position = wc + side * 4.4f + Vector3.up * 2.6f; camT.LookAt(wc);
                yield return new WaitForEndOfFrame();
                var tex = ProofCapture.Frame();
                File.WriteAllBytes(Path.Combine(dir, $"living_oblique_{label}.jpg"), tex.EncodeToJPG(92));
                notes.Add(Crop(tex, wt, $"living_oblique_{label}"));
                Destroy(tex);
            }
            camT.SetPositionAndRotation(p0, r0);
            if (rts) rts.enabled = on;
        }

        public static string FpsStatus = "idle";
        /// <summary>Real frame pacing for `seconds` (unscaled): frames / s, frame-time p50 / p95 / p99, missed vsyncs / s (each one = a
        /// </summary>
        public static string Fps(float seconds, string label)
        {
            FpsStatus = "running";
            var h = new GameObject("OZ_FpsProbe").AddComponent<WheeledWreckProof>();
            h.StartCoroutine(h.FpsRun(seconds, label));
            return "fps probe started: " + label;
        }

        IEnumerator FpsRun(float seconds, string label)
        {
            var dts = new List<float>(); float t0 = Time.realtimeSinceStartup, last = t0;
            while (Time.realtimeSinceStartup - t0 < seconds) { yield return null; float now = Time.realtimeSinceStartup; dts.Add(now - last); last = now; }
            float span = last - t0; dts.Sort();
            int missed = 0; foreach (var d in dts) missed += Mathf.Max(0, Mathf.RoundToInt(d * 60f) - 1);
            float P(float q) => dts.Count == 0 ? 0f : dts[Mathf.Clamp(Mathf.RoundToInt(q * (dts.Count - 1)), 0, dts.Count - 1)] * 1000f;
            string res = $"{System.DateTime.Now:HH:mm:ss} {label}: {(ZGame.I ? ZGame.I.level : "-")}, screen {Screen.width}x{Screen.height}, testmode {PlayerPrefs.GetInt("oz_testmode", 0)}, " +
                         $"{dts.Count} frames in {span:F1} s = {dts.Count / Mathf.Max(0.01f, span):F1} fps, frame ms p50 {P(0.5f):F1} / p95 {P(0.95f):F1} / p99 {P(0.99f):F1} / max {P(1f):F1}, " +
                         $"missed vsyncs {missed / Mathf.Max(0.01f, span):F1} / s, targetFrameRate {Application.targetFrameRate}, vSync {QualitySettings.vSyncCount}, gpu {SystemInfo.graphicsDeviceName}";
            File.AppendAllText(Path.Combine(Root, "fps_probe.txt"), res + System.Environment.NewLine);
            Debug.Log("[FpsProbe] " + res);
            FpsStatus = "DONE " + res;
            Destroy(gameObject);
        }

        static void Recore()
        {
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if ((r.name == "OZ_HoleCore" || r.name == "OZ_ChunkCore" || r.name == "OZ_AxleStub") && r.sharedMaterial) r.sharedMaterial = r.name == "OZ_HoleCore" && WheeledBreakup.HoleFix ? WreckPieces.HoleMat(r.sharedMaterial) : WreckPieces.CoreMat(r.sharedMaterial);
        }

        void Finish()
        {
            Time.captureFramerate = 0;
            if (rts && minH0 > 0f) { rts.minHeight = minH0; if (rts.height < minH0) rts.SetHeightImmediate(minH0); }
            File.AppendAllText(Path.Combine(dir, "proof_log.txt"), log.ToString());
            Status = "DONE";
            Destroy(gameObject);
        }

        void OnDestroy() { Time.captureFramerate = 0; if (rts && minH0 > 0f) rts.minHeight = minH0; }
    }
}
