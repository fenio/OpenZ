using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K342 Play proof (menu OpenZ/V1.1/Station: K342 Crane Rams Proof (Level 16, Play), Editor/OzCraneRamsMenu.cs): the repair station's
    /// crane carried by its two hydraulic rams while the playable shuttle lands on the station. Counted PASS / FAIL lines and full-screen
    /// pictures from the normal RTS camera, the camera held on the station:
    ///  * k342_rest_h70 / k342_rest_close: the rams at rest (pin to pin 4.84 m);
    ///  * cycle 1 at the standard 70 m: the shuttle sent to repair, k342_rams_away_f00..05_h70 (6 frames every 0.4 s over the crane's
    ///    2 s move, + a sheet), the ship on the deck (k342_parked_h70 / k342_parked_close), k342_rams_back_f00..05_h70 after it left;
    ///  * cycle 2 at 32 m: the same strips close (k342_rams_away_f00..05_close, k342_rams_back_f00..05_close);
    ///  * counted: the rams built on the station, their length at rest and parked, the smallest stage overlap (>= 0.35 m), the top
    ///    mounts in step with the gantry (<= 1 mm), the station's / gantry's / hoist's / ship's vertices inside a ram part (0) at every
    ///    strip frame and on the deck, the hiss at each start and the release at each stop (the sound log).
    /// K342b adds: the clean cut used at each split with nothing left open (OzStationCrane.CleanLog), the rams as 7 merged meshes (7 casting
    /// shadows), and with the ship on the deck at 32 m the close pictures of what stays and what left: k342b_tower_top_close (the west
    /// tower's whole cream top with the beam's seat), k342b_post_socket_close (the deck closed in its own colour where the east post
    /// stood), k342b_beam_end_close (the parked beam's end and its west ram mount), k342b_parked_night_close (the same at night 0.55), and
    /// the frame time (mean and worst over 1.5 s, no pictures taken meanwhile) at rest and with the crane parked.
    /// Test only (CPU removed, morning held).
    /// </summary>
    public sealed class OzCraneRamsProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "crane_rams_proof_k342");
            return System.IO.Path.Combine(Application.persistentDataPath, "crane_rams_proof_k342");
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzCraneRamsProof").AddComponent<OzCraneRamsProof>();
        }

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; DayNight dn; ZObject s; OzPlayableShuttle ship; Team me = Team.Red;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes; string dir; Vector3? look; int hitFrames, hitsAll; string hitWhere = "-";

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>(); dn = FindFirstObjectByType<DayNight>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Update() { if (look.HasValue && rts) rts.focus = new Vector3(look.Value.x, 0f, look.Value.z); }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzCraneRamsProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        string P(string name) => System.IO.Path.Combine(dir, name + ".png");
        void Shot(string name) { ScreenCapture.CaptureScreenshot(P(name)); Log($"frame {name}.png (camera {(rts ? rts.height : -1f):F1} m)"); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        IEnumerator Height(float h) { if (rts) rts.SetHeightImmediate(h); yield return null; yield return null; }

        /// <summary>A fixed-camera strip: n frames every `step` s on `at`, the rams' hits counted at each frame, then one sheet.</summary>
        IEnumerator Strip(string name, string tag, ZObject station, Vector3 at, int n, float step)
        {
            look = at; yield return null; yield return null;
            var fr = new List<string>();
            for (int i = 0; i < n; i++)
            {
                string f = $"{name}_f{i:00}_{tag}"; Shot(f); fr.Add(P(f));
                Count(station, $"{f}");
                yield return OzProofPictures.AfterGrab();
                if (i < n - 1) yield return Wait(Mathf.Max(0f, step - Time.deltaTime));
            }
            foreach (var f in fr) yield return OzProofPictures.Written(f);
            bool ok = OzProofPictures.Sheet(fr, 3, 640, System.IO.Path.Combine(dir, $"{name}_{tag}_sheet.jpg"));
            Log($"strip {name}_{tag}: {n} frames every {step:F2} s from a fixed camera, sheet {(ok ? name + "_" + tag + "_sheet.jpg" : "NOT MADE")}");
        }

        /// <summary>K342b: the frame time over 1.5 s with no picture taken (mean and worst, ms), logged; returns the mean.</summary>
        float ftMean;
        IEnumerator FrameTime(string when)
        {
            yield return null; yield return null;
            float sum = 0f, worst = 0f; int n = 0; float t = 0f;
            while (t < 1.5f) { yield return null; float dt = Time.unscaledDeltaTime; t += dt; sum += dt; worst = Mathf.Max(worst, dt); n++; }
            ftMean = sum / Mathf.Max(1, n) * 1000f;
            Log($"K342b frame time {when}: mean {ftMean:F2} ms, worst {worst * 1000f:F1} ms over {n} frames (rams: {OzStationCrane.RamRenderers} renderers per station, {OzStationCrane.RamShadowCasters} casting shadows; the crane {(OzStationCrane.Split(s != null ? s.ShuttleCraneStation : -1) ? "split" : "whole")})");
        }

        void Count(ZObject station, string when)
        {
            int h = OzStationCrane.RamHits(station.Id, ship ? ship.transform : null, out string w);
            OzStationCrane.RamLengths(station.Id, out float lw, out float le);
            Log($"  {when}: crane slide {OzStationCrane.SlideK(station.Id):F2}, rams {lw:F2} / {le:F2} m pin to pin, vertices inside a ram part {h}{(h > 0 ? " (" + w + ")" : "")}");
            if (h > 0) { hitFrames++; hitsAll += h; if (hitWhere == "-") hitWhere = w; }
        }

        /// <summary>K342b: the camera's ground focus that puts a raised world point in the middle of the picture.</summary>
        Vector3 FocusFor(Vector3 p)
        {
            var f = rts ? rts.transform.forward : Vector3.forward; var h = new Vector3(f.x, 0f, f.z); float hm = h.magnitude;
            if (hm < 1e-4f) return p;
            return p + h / hm * (p.y * hm / Mathf.Max(0.05f, -f.y));
        }
        /// <summary>K342b: a point of the station frame (View/OzStationCrane.cs: x east, z north, y up from the base) in the world.</summary>
        Vector3 StationPoint(ZObject station, Vector3 fr)
        {
            return g.StaticViews.TryGetValue(station.Id, out var go) && go ? go.transform.TransformPoint(new Vector3(-fr.x, fr.y, -fr.z)) : g.CenterWorld(station);
        }
        IEnumerator CloseLook(ZObject station, Vector3 fr, string shot)
        {
            look = FocusFor(StationPoint(station, fr)); yield return null; yield return null; yield return null;
            Shot(shot); yield return OzProofPictures.AfterGrab();
        }

        int Played(float t0, string key)
        {
            int n = 0;
            foreach (var e in OzSoundLog.Since(t0)) if (e.key == key && e.outcome != null && e.outcome.StartsWith("PLAY")) n++;
            return n;
        }

        IEnumerator Cycle(ZObject station, Vector3 sp, string tag)
        {
            float t0 = Time.unscaledTime; int hiss0 = OzStationCrane.RamHisses, rel0 = OzStationCrane.RamReleases;
            int clean0 = OzStationCrane.CleanSplits, fall0 = OzStationCrane.CleanFallbacks;
            s.SetHealth(s.MaxHealth / 2); input.Selection.Clear(); input.Selection.Add(s);
            Check($"{tag}: the shuttle is sent to repair (R)", input.SendToRepair());
            yield return WaitFor(() => s.ShuttleCraneStation == station.Id && s.ShuttleCraneCallAt >= 0, 60f);
            yield return Strip("k342_rams_away", tag, station, sp, 6, 0.4f);
            yield return WaitFor(() => s.ShuttleOnRepairPad, 60f);
            yield return Wait(1.0f);
            look = sp; yield return null; yield return null;
            OzStationCrane.RamLengths(station.Id, out float pw, out float pe);
            Log($"{tag}: the ship on the deck; {OzStationCrane.ParkLog}; the rams {pw:F2} / {pe:F2} m pin to pin");
            Check($"{tag}: the rams carry the parked gantry (both longer than at rest by at least 3 m)", pw > 7.8f && pe > 7.8f);
            Shot("k342_parked_" + tag); Count(station, "on the deck"); yield return OzProofPictures.AfterGrab();
            yield return FrameTime($"{tag}, the ship on the deck, the crane parked on its rams");
            // K342b: the clean split (what stays whole, what left with closed ends)
            Log($"{tag}: K342b cut: {OzStationCrane.CleanLog}; clean splits +{OzStationCrane.CleanSplits - clean0}, fallbacks +{OzStationCrane.CleanFallbacks - fall0}");
            Check($"{tag}: K342b the crane was split by the clean cut (no fallback) and the cut left nothing open ({OzStationCrane.CleanOpenAfter} open edges = the model's own {OzStationCrane.CleanOpenBefore})",
                  OzStationCrane.CleanSplits > clean0 && OzStationCrane.CleanFallbacks == fall0 && OzStationCrane.CleanOpenBefore >= 0 && OzStationCrane.CleanOpenAfter == OzStationCrane.CleanOpenBefore);
            if (tag == "close")
            {
                yield return CloseLook(station, new Vector3(-3.6f, 5.05f, 1.8f), "k342b_tower_top_close");
                yield return CloseLook(station, new Vector3(3.6f, 0.94f, 1.7f), "k342b_post_socket_close");
                yield return CloseLook(station, new Vector3(-4.2f, 4.1f, 6.8f), "k342b_beam_end_close");
                if (dn) { dn.timeOfDay = 0.55f; dn.Apply(0.55f); yield return null; yield return null; }
                yield return CloseLook(station, new Vector3(0f, 2.5f, 3.0f), "k342b_parked_night_close");
                if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); yield return null; }
                look = sp; yield return null; yield return null;
            }
            yield return WaitFor(() => !s.ShuttleOnRepairPad, 25f);
            yield return WaitFor(() => s.ShuttleCraneStation < 0, 30f);
            yield return Strip("k342_rams_back", tag, station, sp, 6, 0.4f);
            yield return WaitFor(() => OzStationCrane.SlideK(station.Id) <= 0f, 6f);
            yield return Wait(0.5f);
            OzStationCrane.RamLengths(station.Id, out float bw, out float be);
            Check($"{tag}: back at rest, the rams retracted ({bw:F2} / {be:F2} m, at rest 4.84)", Mathf.Abs(bw - 4.84f) < 0.03f && Mathf.Abs(be - 4.84f) < 0.03f);
            int hiss = OzStationCrane.RamHisses - hiss0, rel = OzStationCrane.RamReleases - rel0;
            int hissP = Played(t0, "station_crane_ram_hiss"), relP = Played(t0, "station_crane_ram_release");
            Log($"{tag}: hisses fired {hiss} (played {hissP}), releases fired {rel} (played {relP})");
            Check($"{tag}: a hiss at each start and a release at each stop (away and back: at least 2 of each, played)", hiss >= 2 && rel >= 2 && hissP >= 2 && relP >= 2);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleOnRepairPad, 80f);
        }

        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || W.PlayableShuttle == null || !OzPlayableShuttle.I) { Log("no playable shuttle (level 16 in Play, the option SHUTTLE on)"); fails++; Finish(); yield break; }
            s = W.PlayableShuttle; ship = OzPlayableShuttle.I; me = s.Owner;
            W.Bots.Clear();
            if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            if (rts) { rts.FreeLookOff(); rts.SetHeightImmediate(70f); }
            Log($"K342 + K342b crane rams proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}; CPU removed, day 0.10 held");
            ZObject station = null;
            foreach (var f in W.Flags) foreach (var b in f.LinkedObjects) if (b.Type == "repair" && station == null) { station = b; if (b.Owner != me) W.AwardZone(f, me); }
            if (station == null) { Check("a repair station", false); Finish(); yield break; }
            yield return Wait(1f);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne, 30f);
            var sp = g.CenterWorld(station);
            Log("rams: " + OzStationCrane.RamLog + $"; rigs built {OzStationCrane.RamRigs}");
            Check($"K342b: the rams are {OzStationCrane.RamRenderers} merged meshes per station (7), {OzStationCrane.RamShadowCasters} casting shadows (7: every part keeps its shadow)",
                  OzStationCrane.RamRenderers == 7 && OzStationCrane.RamShadowCasters == 7);
            Log("K342b: the clean cut, worked out when the rams were built: " + OzStationCrane.CleanLog);
            bool have = OzStationCrane.RamLengths(station.Id, out float rw, out float re);
            Check($"the station has its two rams, at rest {rw:F2} / {re:F2} m pin to pin (4.84)", have && Mathf.Abs(rw - 4.84f) < 0.03f && Mathf.Abs(re - 4.84f) < 0.03f);
            look = sp; yield return null; yield return null;
            Shot("k342_rest_h70"); Count(station, "at rest"); yield return OzProofPictures.AfterGrab();
            yield return FrameTime("h70, at rest (the crane whole, the rams retracted)");
            yield return Height(32f); Shot("k342_rest_close"); yield return OzProofPictures.AfterGrab(); yield return Height(70f);
            yield return Cycle(station, sp, "h70");
            yield return Height(32f);
            yield return Cycle(station, sp, "close");
            yield return Height(70f); look = null;
            Log($"the rams over the take: longest {OzStationCrane.RamMaxLength:F2} m, smallest stage overlap {OzStationCrane.RamMinOverlap:F2} m, top mounts off the gantry at most {OzStationCrane.RamLagMax * 1000f:F1} mm");
            Check("no stage pulled out (overlap at least 0.35 m) and nothing stretched (at most 9.4 m pin to pin)", OzStationCrane.RamMinOverlap >= OzStationCrane.RamOvMin - 1e-3f && OzStationCrane.RamMaxLength <= 9.4f);
            Check("the rams move in step with the gantry (the top mounts at most 1 mm off it)", OzStationCrane.RamLagMax <= 0.001f);
            Check($"no station, gantry, hoist or ship vertex inside a ram part at any counted frame ({hitsAll} in {hitFrames} frames{(hitFrames > 0 ? ", " + hitWhere : "")})", hitFrames == 0);
            Finish();
        }

        void Finish()
        {
            string res = fails == 0 ? "RESULT PASS" : $"RESULT FAIL ({fails})";
            Log($"{res} ({passes} PASS)");
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "proof_log_K342.txt"), sb.ToString()); } catch (System.Exception ex) { Debug.LogWarning("[OzCraneRamsProof] " + ex.Message); }
            Report = res; Destroy(gameObject);
        }
    }
}
