using System.Collections;
using System.IO;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Play-mode check and picture helper of the neutral flag waves (View/OzNeutralFlagWaves.cs, kit KC2 item C10).
    ///   menu OpenZ/V1.1/Neutral Flag Waves: Check (KC2, Play mode)        about 45 s of play, the camera as it is; one log line
    ///        subfolder kc2_flag_waves (check_LEVEL.txt)
    ///   menu OpenZ/V1.1/Neutral Flag Waves: Show Next Neutral Flag (KC2, Play mode)   the camera to the next neutral flag at the
    ///        standard zoom (70 m), a wave started on it 0.4 s later (for the pictures; the flag's ground class is logged)
    /// View only: it never changes a rule, an owner or a setting. The camera is put back at the end of the check.
    /// </summary>
    public static class OzKC2FlagWaveCheck
    {
        const string Tag = "[KC2 flag waves]";
        public const float StandardHeight = 70f;
        public static string Report = "not run";
        static int next = -1;

        sealed class Runner : MonoBehaviour { }

        [MenuItem("OpenZ/V1.1/Neutral Flag Waves: Check (KC2, Play mode)")]
        public static void Check()
        {
            var g = ZGame.I;
            if (!EditorApplication.isPlaying || !g || g.World == null) { Debug.LogWarning(Tag + " enter Play mode first (a running level)"); return; }
            if (SessionState.GetBool("kc2_flag_wave_check_running", false)) { Debug.LogWarning(Tag + " a run is still going"); return; }
            SessionState.SetBool("kc2_flag_wave_check_running", true);
            var go = new GameObject("KC2 flag wave check (temporary)");
            go.AddComponent<Runner>().StartCoroutine(Run(g, go));
        }

        static IEnumerator Run(ZGame g, GameObject self)
        {
            var sb = new StringBuilder(); int pass = 0, all = 0;
            void V(bool ok, string what) { all++; if (ok) pass++; sb.AppendLine((ok ? "PASS  " : "FAIL  ") + what); }
            var fw = OzNeutralFlagWaves.I;
            int neutral = 0, owned = 0; foreach (var f in g.World.Flags) if (f != null && !f.Destroyed) { if (f.Owner == Team.Neutral) neutral++; else owned++; }
            bool beacon = fw && fw.BeaconHere;                                    // the wave layer's own test: a live, enabled FlagBeacons in this level
            sb.AppendLine($"{Tag} level {g.level}, night {NightGrade.NightK:0.00}; zone flags: {neutral} neutral, {owned} owned; the older capture beacon here: {(beacon ? "ON, drawing in this level (no wave on top of it; started straight in this scene?)" : "not drawing in this level (the wave is the flags' cue)")}");
            V(fw && fw.enabled, "W1 the wave layer is attached to the game and enabled");
            if (!fw || !fw.enabled) { Finish(g, sb, pass, all, self); yield break; }
            // W2: watch 40 s with the camera as it is
            int s0 = OzNeutralFlagWaves.Started, w0 = OzNeutralFlagWaves.StartedWrong; float t0 = Time.realtimeSinceStartup;
            OzNeutralFlagWaves.LastPeak = 0f;
            var cam = Camera.main; int onCam = 0;
            if (cam) foreach (var f in g.World.Flags) if (f != null && f.Owner == Team.Neutral) { var v = cam.WorldToViewportPoint(g.CenterWorld(f)); if (v.z > 0 && v.x > 0 && v.x < 1 && v.y > 0 && v.y < 1) onCam++; }
            while (Time.realtimeSinceStartup - t0 < 40f) yield return null;
            int started = OzNeutralFlagWaves.Started - s0, wrong = OzNeutralFlagWaves.StartedWrong - w0;
            V(wrong == 0, $"W2 in 40 s: {started} wave(s) started, {wrong} of them on a flag that was not neutral or not on camera (wanted 0); neutral flags on camera at the start: {onCam}");
            if (beacon) sb.AppendLine("NOTE  W3 not judged: the older capture beacon draws in this level (start the level from the main menu, as the game does, to judge the wave)");
            else V(onCam == 0 || started > 0, $"W3 a neutral flag on camera gets a wave now and then: {started} in 40 s with {onCam} on camera (wanted at least 1 when one is on camera; at most {Mathf.CeilToInt(onCam * 40f / OzNeutralFlagWaves.GapMin) + onCam})");
            V(OzNeutralFlagWaves.LastPeak <= OzNeutralFlagWaves.PeakDay + 1e-4f, $"W4 very subtle: the strongest drawn opacity {OzNeutralFlagWaves.LastPeak:0.000} (at most {OzNeutralFlagWaves.PeakDay:0.00} by day, {OzNeutralFlagWaves.PeakNight:0.00} at night); a wave lasts {OzNeutralFlagWaves.WaveSeconds} s, then {OzNeutralFlagWaves.GapMin} to {OzNeutralFlagWaves.GapMax} s of nothing");
            // W5: every built ring lies on the ground (sampled against the ground now)
            float worstUnder = float.NegativeInfinity, worstOver = 0f; int rings = 0, samples = 0;
            ZObject nearest = null; float best = float.MaxValue;
            foreach (var f in g.World.Flags)
                if (f != null && f.Owner == Team.Neutral && cam)
                {
                    var v = cam.WorldToViewportPoint(g.CenterWorld(f)); float d = (new Vector2(v.x, v.y) - new Vector2(0.5f, 0.5f)).sqrMagnitude;
                    if (v.z > 0 && d < best) { best = d; nearest = f; }
                }
            if (nearest != null && !beacon) { fw.Force(nearest.Id); yield return null; }
            foreach (var r in fw.Rings())
                if (fw.Clearance(r.id, out float u, out float o, out int n)) { rings++; samples += n; worstUnder = Mathf.Max(worstUnder, u); worstOver = Mathf.Max(worstOver, o); }
            if (rings > 0) V(worstUnder < 0f && worstOver < 0.15f, $"W5 on the ground's surface, no clipping: {rings} ring(s), {samples} points (cell centres, edge middles, corners) sampled against the ground now: never under it (closest {(-worstUnder) * 100f:0.0} cm above), at most {worstOver * 100f:0.0} cm above (lift {OzNeutralFlagWaves.Lift * 100f:0} cm)");
            else sb.AppendLine($"NOTE  W5 not judged: no ring built on this level{(beacon ? " (the capture beacon draws here: start the level from the main menu, as the game does, to judge the wave)" : " (no neutral flag came on camera: move the camera over one and run again)")}");
            Finish(g, sb, pass, all, self);
        }

        static void Finish(ZGame g, StringBuilder sb, int pass, int all, GameObject self)
        {
            string head = $"{Tag} {(pass == all ? "PASS" : "FAIL")} {pass}/{all}";
            Report = head + "\n" + sb;
            Debug.Log(Report);
            try
            {
                string dir = OutDir(); Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, $"check_{g.level}.txt"), Report);
            }
            catch (System.Exception ex) { Debug.LogWarning(Tag + " could not write the text: " + ex.Message); }
            SessionState.SetBool("kc2_flag_wave_check_running", false);
            if (self) Object.Destroy(self);
        }

        [MenuItem("OpenZ/V1.1/Neutral Flag Waves: Show Next Neutral Flag (KC2, Play mode)")]
        public static void ShowNext()
        {
            var g = ZGame.I; var rts = Object.FindFirstObjectByType<RtsCamera>();
            if (!EditorApplication.isPlaying || !g || g.World == null || !rts) { Debug.LogWarning(Tag + " enter Play mode first (a running level)"); return; }
            var fs = g.World.Flags.FindAll(f => f != null && !f.Destroyed && f.Owner == Team.Neutral);
            if (fs.Count == 0) { Debug.LogWarning(Tag + " no neutral flag left on this level"); return; }
            next = (next + 1) % fs.Count; var flag = fs[next];
            var p = g.CenterWorld(flag);
            rts.focus = new Vector3(p.x, rts.focus.y, p.z); rts.SetHeightImmediate(StandardHeight); rts.Place();
            var c = g.World.Map.ClassAt(flag.CenterX >> 4, flag.CenterY >> 4);
            Debug.Log($"{Tag} neutral flag {next + 1} of {fs.Count}: id {flag.Id}, tile ({flag.CenterX >> 4},{flag.CenterY >> 4}), map class {c}, night {NightGrade.NightK:0.00}; a wave starts in 0.4 s (take the picture about 1 s after it starts)");
            var go = new GameObject("KC2 flag wave show (temporary)");
            go.AddComponent<Runner>().StartCoroutine(Later(flag.Id, go));
        }

        static IEnumerator Later(int id, GameObject self)
        {
            yield return new WaitForSecondsRealtime(0.4f);
            bool ok = OzNeutralFlagWaves.I && OzNeutralFlagWaves.I.Force(id);
            Debug.Log($"{Tag} wave on flag {id}: {(ok ? "started" : "NOT started (not neutral, or the layer is off)")}");
            if (self) Object.Destroy(self, 3f);
        }

        static string OutDir()
        {
            string all = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/milestones")), stage = "v1_1";
            if (Directory.Exists(all))
            {
                var ds = Directory.GetDirectories(all, "v1_1*");
                System.Array.Sort(ds, System.StringComparer.Ordinal);
                if (ds.Length > 0) stage = Path.GetFileName(ds[ds.Length - 1]);
            }
            return Path.Combine(all, stage, "kc2_flag_waves");
        }
    }
}
