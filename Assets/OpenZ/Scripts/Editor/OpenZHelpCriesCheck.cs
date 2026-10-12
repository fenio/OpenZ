using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// - Check: the sound table's vo_under_attack_repeat holds the 24 new alan_cry_* lines, every clip loads, and the radio rules
    ///   hold over 30 simulated minutes of non-stop fighting (5 seeds): at most 2 radio lines per 60 s, 30-60 s between cries, no
    ///   line again before every other line had its turn, never the same line twice in a row
    /// - Play Next Cry: Play mode only, plays the next cry of a preview bag right now (listening check; bypasses the limiter,
    ///   still waits for Alan's channel like every alert).
    /// </summary>
    public static class OpenZHelpCriesCheck
    {
        const string Event = "vo_under_attack_repeat";

        [MenuItem("OpenZ/Audio/help_cries Check (clips, line bag, radio limiter)")]
        static void Check()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# help_cries check (" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ")").AppendLine();
            bool ok = true;
            var ta = Resources.Load<TextAsset>(AudioDirector.TablePath);
            var table = ta ? JsonUtility.FromJson<SoundTable>(ta.text) : null;
            var ev = table?.events?.FirstOrDefault(e => e.id == Event);
            if (ev == null || ev.clips == null) { sb.AppendLine($"- FAIL: {Event} missing from {AudioDirector.TablePath}.json"); ok = false; }
            else
            {
                var cries = ev.clips.Where(c => c.res != null && c.res.Contains("/alan_cry_")).ToList();
                int missing = 0;
                foreach (var c in ev.clips)
                {
                    var clip = Resources.Load<AudioClip>(c.res);
                    if (!clip) { missing++; sb.AppendLine($"- FAIL: clip does not load: {c.res}"); }
                }
                var lines = ev.clips.Select(c => c.line).Where(l => !string.IsNullOrEmpty(l)).Distinct().ToList();
                sb.AppendLine($"- {Event}: {ev.clips.Length} clips, {lines.Count} distinct lines, {cries.Count} new alan_cry_* clips (want 24), {missing} missing");
                if (cries.Count < 24 || missing > 0) ok = false;
                for (int seed = 1; seed <= 5; seed++)
                {
                    var probe = new RadioDiscipline();
                    var r = RadioDiscipline.Simulate(lines, 30f, seed, probe.GapMin, probe.GapMax, probe.PerMinute);
                    bool pass = r.Ok(probe);
                    ok &= pass;
                    sb.AppendLine($"- simulation seed {seed}: {(pass ? "PASS" : "FAIL")}: {r}");
                    if (seed == 1) sb.AppendLine($"  first lines: {r.firstLines}");
                }
            }
            if (Application.isPlaying && AudioDirector.I)
            {
                var ad = AudioDirector.I; float now = Time.unscaledTime; var rd = RadioDiscipline.Shared;
                sb.AppendLine($"- live: distress calls {ad.DistressPlays}, cries for help {ad.HelpCries}, radio lines {rd.Lines}, " +
                              $"in the last 60 s {rd.CallsInLastMinute(now)}, radio shut for {Mathf.Max(0f, rd.ShutUntil - now):0} s more " +
                              $"(gap {ad.radioGap.x:0}-{ad.radioGap.y:0} s, max {ad.radioPerMinute} per minute)");
            }
            sb.AppendLine().AppendLine(ok ? "RESULT: PASS" : "RESULT: FAIL");
            var path = Path.Combine(OpenZPaths.Records, "help_cries_check.md");
            File.WriteAllText(path, sb.ToString());
            if (ok) Debug.Log("[help_cries] check PASS -> " + path + "\n" + sb);
            else Debug.LogWarning("[help_cries] check FAIL -> " + path + "\n" + sb);
        }

        static readonly RadioDiscipline preview = new RadioDiscipline();

        [MenuItem("OpenZ/Audio/help_cries Play Next Cry (Play mode)")]
        static void PlayNext()
        {
            var ad = AudioDirector.I;
            if (!Application.isPlaying || !ad) { Debug.LogWarning("[help_cries] enter Play mode in a level first"); return; }
            var d = ad.Def(Event);
            var line = d != null ? preview.Peek(Event, d.lines, true) : null;
            if (line == null) { Debug.LogWarning("[help_cries] no lines loaded for " + Event); return; }
            preview.Played(Event, line);
            AudioDirector.Say(Event, "=" + line);
            Debug.Log("[help_cries] cry: " + line);
        }
    }
}
