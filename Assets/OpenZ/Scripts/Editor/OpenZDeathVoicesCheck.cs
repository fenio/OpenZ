using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// - Check: every death event of the sound table (death_cry_*, death_robot*, hit_grunt) loads all its clips, every death pool
    ///   holds the rules (<= 2 cries in any 1 s, >= 0.35 s apart, no clip again within 8) at 0.5 / 2 / 6 deaths per second, 3 seeds
    /// - Play Next Death: Play mode, one death of the next type (bullet, blast, fire, laser, crush) at the camera focus through the
    ///   real path (limiter included, so a quick second click may be voiceless or silent: that is the rule working).
    /// </summary>
    public static class OpenZDeathVoicesCheck
    {
        static readonly string[] Events =
        {
            "death_cry_bullet", "death_cry_blast", "death_cry_fire", "death_cry_laser", "death_cry_crush",
            "death_robot", "death_robot_blast", "death_robot_melt", "death_robot_zap", "death_robot_crush", "hit_grunt",
        };

        [MenuItem("OpenZ/Audio/death_voices Check (clips, pools, limiter)")]
        static void Check()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# death_voices check (" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ")").AppendLine();
            bool ok = true;
            var ta = Resources.Load<TextAsset>(AudioDirector.TablePath);
            var table = ta ? JsonUtility.FromJson<SoundTable>(ta.text) : null;
            var pools = new Dictionary<string, List<string>>();
            int clips = 0, missing = 0;
            foreach (var id in Events)
            {
                var ev = table?.events?.FirstOrDefault(e => e.id == id);
                if (ev == null || ev.clips == null || ev.clips.Length == 0) { sb.AppendLine($"- FAIL: {id} missing or empty in {AudioDirector.TablePath}.json"); ok = false; continue; }
                var names = new List<string>();
                foreach (var c in ev.clips)
                {
                    clips++;
                    var clip = Resources.Load<AudioClip>(c.res);
                    if (!clip) { missing++; sb.AppendLine($"- FAIL: {id}: clip does not load: {c.res}"); ok = false; continue; }
                    names.Add(clip.name);
                    if (clip.name == "death" || clip.name == "death-2") { sb.AppendLine($"- FAIL: {id} still holds the legacy '{clip.name}'"); ok = false; }
                    if (clip.length > 2.05f) { sb.AppendLine($"- WARN: {id}: {clip.name} is {clip.length:0.00} s (> 2 s)"); }
                }
                pools[id] = names;
                bool big = names.Count >= 9;
                if (!big) ok = false;
                sb.AppendLine($"- {id}: {names.Count} clips, {(ev.is3D ? "3D" : "2D !")}, bus {ev.bus}, {(big ? "ok" : "FAIL (< 9 clips)")}");
                if (!ev.is3D) ok = false;
            }
            sb.AppendLine($"- {clips} clip references, {missing} missing").AppendLine();
            foreach (var rate in new[] { 0.5, 2.0, 6.0 })
                for (int seed = 1; seed <= 3; seed++)
                {
                    var r = DeathVoices.Simulate(pools, seed, 600, rate);
                    ok &= r.pass;
                    sb.AppendLine($"- simulation {(r.pass ? "PASS" : "FAIL")}: " + r.report.Replace("\n", "\n  ").TrimEnd());
                }
            if (Application.isPlaying && AudioDirector.I)
                sb.AppendLine().AppendLine($"- live ({(AudioDirector.DeathVoicesLive ? "death voices live" : "NOT live: legacy fallback")}): {DeathVoices.Shared.Summary()}");
            sb.Insert(0, $"RESULT: {(ok ? "PASS" : "FAIL")}\n\n");
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../..", "work", "records"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "death_voices_check.md");
            File.WriteAllText(path, sb.ToString());
            if (ok) Debug.Log("[death_voices] RESULT: PASS -> " + path + "\n" + sb);
            else Debug.LogError("[death_voices] RESULT: FAIL -> " + path + "\n" + sb);
        }

        static int next;
        [MenuItem("OpenZ/Audio/death_voices Play Next Death (Play mode)")]
        static void PlayNext()
        {
            if (!Application.isPlaying || !AudioDirector.I) { Debug.LogWarning("[death_voices] Play mode only"); return; }
            if (!AudioDirector.DeathVoicesLive) { Debug.LogWarning("[death_voices] not live: death tables missing or AudioDirector.deathVoices off"); return; }
            var c = (DeathVoices.Cause)(next++ % 5);
            AudioDirector.PreviewDeath(c);
            Debug.Log($"[death_voices] {c}: {DeathVoices.Shared.Summary()}");
        }
    }
}
