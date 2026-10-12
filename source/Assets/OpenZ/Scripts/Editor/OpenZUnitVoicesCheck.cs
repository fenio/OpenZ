using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// - Check: every per-type event of the sound table (vo_select_x_*, vo_order_x_*_move / _attack / _refuse, vo_idle_x_*) exists for
    ///   the 17 types, loads all its clips, sits on Alan's main channel (2D, Voice/Robots, so the runtime robot filter and the
    ///   never-cut rule apply), every type keeps >= 12 lines, the classic pools are still there with all their clips (added, not
    ///   replaced), and UnitVoices.Simulate holds the picker AND channel rules on these pools with the real clip lengths (no repeat
    ///   of the type's last 6 lines, never the same line twice, jokes only after 3 quick clicks and in gag order, no quick re-click
    ///   queued behind a line, no order line queued behind an order line, every order line within 0.8 s of its click, nothing
    ///   cut), 3 seeds
    /// - Say Next Line: Play mode, the next type / category line through the game's own path (AudioDirector.DebugUnitLine).
    /// </summary>
    public static class OpenZUnitVoicesCheck
    {
        static readonly string[] Classic = { "vo_select_generic", "vo_select_type", "vo_order_ack", "vo_order_attack", "vo_order_no_way" };

        [MenuItem("OpenZ/Audio/unit_voices Check (pools, clips, picker)")]
        static void Check()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# unit_voices check (" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ")").AppendLine();
            bool ok = true;
            var ta = Resources.Load<TextAsset>(AudioDirector.TablePath);
            var table = ta ? JsonUtility.FromJson<SoundTable>(ta.text) : null;
            if (table?.events == null) { Debug.LogError("[unit_voices] RESULT: FAIL: no sound table at Resources/" + AudioDirector.TablePath); return; }
            var ev = new Dictionary<string, SoundEventDef>();
            foreach (var e in table.events) if (e != null && e.id != null) ev[e.id] = e;
            var pools = new Dictionary<string, List<string>>();
            var lengths = new Dictionary<string, List<double>>();
            int clips = 0, missing = 0, lines = 0;
            foreach (var t in UnitVoices.Types)
            {
                var parts = new List<string>(); int tl = 0;
                for (int c = 0; c < UnitVoices.CatNames.Length; c++)
                {
                    string id = UnitVoices.EventFor(t, (UnitVoices.Cat)c);
                    if (!ev.TryGetValue(id, out var e) || e.clips == null || e.clips.Length == 0) continue;
                    var names = new List<string>(); var lens = new List<double>();
                    foreach (var cr in e.clips)
                    {
                        clips++;
                        var clip = Resources.Load<AudioClip>(cr.res);
                        if (!clip) { missing++; ok = false; sb.AppendLine($"- FAIL: {id}: clip does not load: {cr.res}"); continue; }
                        lens.Add(clip.length);
                        if (clip.length > 2.6f) sb.AppendLine($"- WARN: {id}: {clip.name} is {clip.length:0.00} s");
                        if (!string.IsNullOrEmpty(cr.line) && !names.Contains(cr.line)) names.Add(cr.line);
                    }
                    if (e.channel != "alan_main" || e.is3D || e.bus != "Voice/Robots") { ok = false; sb.AppendLine($"- FAIL: {id}: channel {e.channel}, bus {e.bus}, {(e.is3D ? "3D" : "2D")} (must be alan_main, Voice/Robots, 2D)"); }
                    pools[id] = names; lengths[id] = lens; tl += names.Count; parts.Add($"{UnitVoices.CatNames[c]} {names.Count}");
                }
                lines += tl;
                bool enough = tl >= 12;
                if (!enough) ok = false;
                sb.AppendLine($"- {t}: {tl} lines ({string.Join(", ", parts)}) {(enough ? "ok" : "FAIL (< 12 lines)")}");
            }
            sb.AppendLine($"- {pools.Count} per-type events, {lines} lines, {clips} clip references, {missing} missing").AppendLine();
            foreach (var id in Classic)
            {   // added, not replaced: the classic pools keep all their clips
                if (!ev.TryGetValue(id, out var e) || e.clips == null || e.clips.Length == 0) { ok = false; sb.AppendLine($"- FAIL: classic {id} missing"); continue; }
                int bad = e.clips.Count(cr => !Resources.Load<AudioClip>(cr.res));
                if (bad > 0) ok = false;
                lengths[id] = e.clips.Select(cr => Resources.Load<AudioClip>(cr.res)).Where(a => a).Select(a => (double)a.length).ToList();
                sb.AppendLine($"- classic {id}: {e.clips.Length} clips{(bad > 0 ? $", {bad} MISSING" : ", all load")}");
            }
            sb.AppendLine();
            for (int seed = 1; seed <= 3; seed++)
            {
                var r = UnitVoices.Simulate(pools, lengths, seed, 20000);
                ok &= r.pass;
                sb.AppendLine($"- simulation {(r.pass ? "PASS" : "FAIL")}: " + r.report.Replace("\n", "\n  ").TrimEnd());
            }
            if (Application.isPlaying && AudioDirector.I) sb.AppendLine().AppendLine("- live pools (Play mode):").Append(AudioDirector.UnitVoicesLiveReport());
            sb.Insert(0, $"RESULT: {(ok ? "PASS" : "FAIL")}\n\n");
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../..", "work", "records"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "unit_voices_check.md");
            File.WriteAllText(path, sb.ToString());
            if (ok) Debug.Log("[unit_voices] RESULT: PASS -> " + path + "\n" + sb);
            else Debug.LogError("[unit_voices] RESULT: FAIL -> " + path + "\n" + sb);
        }

        static int next;
        [MenuItem("OpenZ/Audio/unit_voices Say Next Line (Play mode)")]
        static void SayNext()
        {
            if (!Application.isPlaying || !AudioDirector.I) { Debug.LogWarning("[unit_voices] Play mode only"); return; }
            int n = UnitVoices.Types.Length * UnitVoices.CatNames.Length;
            for (int k = 0; k < n; k++)
            {
                int i = next++ % n;
                string t = UnitVoices.Types[i / UnitVoices.CatNames.Length], c = UnitVoices.CatNames[i % UnitVoices.CatNames.Length];
                var r = AudioDirector.DebugUnitLine(t, c);
                if (r.StartsWith("no lines")) continue;                                         // robots have no refuse pool, cannons no move pool
                Debug.Log("[unit_voices] " + r);
                return;
            }
            Debug.LogWarning("[unit_voices] no per-type pools in the sound table (apply unit_voices_v1)");
        }
    }
}
