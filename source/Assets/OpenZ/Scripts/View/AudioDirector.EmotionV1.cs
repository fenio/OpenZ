using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace OpenZ
{
    // ============================================================================================================================
    // The Play
    // (vo_under_attack, vo_under_attack_repeat, ann_fort_under_attack, ann_base_under_attack, ann_youre_losing, vo_victory_cheer,
    // vo_win_lines, vo_lose_lines). Nothing here runs by itself; the game picks the new takes through its normal requests.
    //  - EmotionV1Proof(): every new take plays once on its own event through the real voice channel (Alan's alert / end channel
    //    with the runtime robot filter, Rachel's channel; the event's bus, gain and pitch), one after another, never over another
    //    voice. Quick mode: each take plays 0.4 s (the clip must be running); EmotionV1Proof(true): every take to its end (listen).
    //    Every play is in OzSoundLog ("VO", event, clip, "PLAY <bus> <channel>", the PlayVoice log line of this pack).
    //  - Pick check (no audio, state restored): the game's own requests (the radio's "=line", the defeat line "lost it", the
    //    victory fallback "Yeee", Rachel's plain bag) reach the new takes through the real Pick() with its no-repeat rules.
    //  - Crowd check: OpenZCelebrate's new whoop banks (alan_emo_*_1 / _2) load.
    //  Result: Console "[EmotionV1] ..." and EmotionV1Status(); "PROOF PASS" when every take played and every check passed.
    // ============================================================================================================================
    public partial class AudioDirector
    {
        static string emoStatus = "EmotionV1: not run";
        public static string EmotionV1Status() => emoStatus;
        static readonly string[] EmoCrowdKeys = { "alan_emo_yeehaa", "alan_emo_yeah", "alan_emo_woohoo_we_won", "alan_emo_yeehaa_smashed_em" };

        /// <summary>Play proof of the voice_emotion_v1 takes (in Play, in a level). listen = play every take to its end.</summary>
        public static string EmotionV1Proof(bool listen = false)
        {
            if (!I) return "EmotionV1: no AudioDirector (enter Play in a level first)";
            var takes = I.EmoTakes();
            I.StartCoroutine(I.EmoProof(takes, listen));
            return $"EmotionV1: proof started, {takes.Count} new takes ({(listen ? "full listen" : "quick, 0.4 s each")})";
        }

        static bool IsEmo(AudioClip c) => c && (c.name.StartsWith("alan_emo_") || c.name.StartsWith("rachel_emo_"));
        static bool IsEmo(string n) => n != null && (n.StartsWith("alan_emo_") || n.StartsWith("rachel_emo_"));

        List<(SoundEventDef d, int i)> EmoTakes()
        {
            var r = new List<(SoundEventDef, int)>();
            foreach (var d in defs.Values.OrderBy(x => x.id))
                if (d.loaded != null && (d.id.StartsWith("vo_") || d.id.StartsWith("ann_")))
                    for (int i = 0; i < d.loaded.Count; i++) if (IsEmo(d.loaded[i])) r.Add((d, i));
            return r;
        }

        bool EmoVoiceBusy() => (alanMain.busy && alanMain.src.isPlaying) || (alanAlert.busy && alanAlert.src.isPlaying)
                               || (rachel.busy && rachel.src.isPlaying) || rachelQueue.Count > 0;

        IEnumerator EmoProof(List<(SoundEventDef d, int i)> takes, bool listen)
        {
            var per = new SortedDictionary<string, int[]>();                                          // event -> {ran, takes}
            var fails = new List<string>(); int logged = 0;
            foreach (var (d, i) in takes)
            {
                if (!per.ContainsKey(d.id)) per[d.id] = new int[2];
                per[d.id][1]++;
                float w = Time.unscaledTime;
                while (EmoVoiceBusy() && Time.unscaledTime - w < 15f) yield return null;             // never over a game voice
                yield return new WaitForSecondsRealtime(0.2f);
                var ch = d.channel == "rachel" ? rachel : alanAlert;                                   // SayImpl: alan_alert + alan_end -> alanAlert
                if (ch.busy && ch.src.isPlaying) ch.src.Stop();
                float s0 = Time.unscaledTime - 0.01f;
                forcePick = i; bool ok = PlayVoice(d, ch, null); forcePick = -1;
                ok &= ch.src.clip == d.loaded[i];
                string cn = d.loaded[i].name, did = d.id;                                              // read the log at once (a busy fight can fill its ring)
                if (OzSoundLog.Since(s0).Any(e => e.src == "VO" && e.key == did && e.clip == cn && e.outcome != null && e.outcome.StartsWith("PLAY"))) logged++;
                float s = Time.unscaledTime, len = d.loaded[i].length / Mathf.Max(0.5f, Mathf.Abs(ch.src.pitch));
                while (ok && ch.src.isPlaying && Time.unscaledTime - s < (listen ? len + 0.2f : 0.4f)) yield return null;
                ok &= listen || ch.src.time > 0.05f;                                                   // the clip really ran
                if (!listen && ch.src.isPlaying) ch.src.Stop();
                if (listen) yield return new WaitForSecondsRealtime(0.35f);
                if (ok) per[d.id][0]++; else fails.Add($"{d.id}:{d.loaded[i].name}");
            }

            // the game's own requests (Pick() with its filters and no-repeat, no audio, state restored)
            var sb = new StringBuilder(); bool picksOk = true;
            foreach (var d in takes.Select(t => t.d).Distinct())
            {
                var filters = new List<string>();
                foreach (var l in Enumerable.Range(0, d.loaded.Count).Where(k => IsEmo(d.loaded[k])).Select(k => d.lines[k]).Distinct()) filters.Add("=" + l);
                if (d.id == "vo_lose_lines") filters.Add("lost it");                                   // OpenZVictory's defeat line
                if (d.id == "vo_win_lines") filters.Add("Yeee");                                       // OpenZVictory's fallback whoop
                if (d.id.StartsWith("ann_")) filters.Add(null);                                        // Rachel: the plain shuffle bag
                foreach (var f in filters)
                {
                    int last = d.lastIdx; var bag = d.bag == null ? null : new List<int>(d.bag);
                    int n = f == null ? 3 * d.loaded.Count : 30, emo = 0, rep = 0, prev = -1;
                    for (int k = 0; k < n; k++) { int p = Pick(d, f); if (IsEmo(d.loaded[p])) emo++; if (p == prev) rep++; prev = p; }
                    d.lastIdx = last; d.bag = bag;
                    int cands = f == null ? d.loaded.Count : Enumerable.Range(0, d.loaded.Count).Count(k => LineMatch(d.lines[k], f));
                    bool pass = emo > 0 && (!d.noRepeat || cands < 2 || rep == 0);
                    picksOk &= pass;
                    sb.Append($"\n  {(pass ? "ok  " : "FAIL")} {d.id} {(f ?? "(bag)")}: {emo}/{n} picks new, {rep} repeats, {cands} candidates");
                }
            }
            int crowd = 0;
            foreach (var k in EmoCrowdKeys) for (int n = 1; n <= 2; n++) if (Resources.Load<AudioClip>("OpenZ/Audio/Voice/" + k + "_" + n)) crowd++;

            int ran = per.Values.Sum(v => v[0]);
            bool pass2 = ran == takes.Count && takes.Count > 0 && logged == takes.Count && picksOk && crowd == 2 * EmoCrowdKeys.Length;
            emoStatus = $"EmotionV1: {(pass2 ? "PROOF PASS" : "PROOF FAIL")} {ran}/{takes.Count} new takes played, {logged} in OzSoundLog, " +
                        $"crowd whoops {crowd}/{2 * EmoCrowdKeys.Length}, pick checks {(picksOk ? "ok" : "FAIL")}; " +
                        string.Join(", ", per.Select(kv => $"{kv.Key} {kv.Value[0]}/{kv.Value[1]}")) +
                        (fails.Count > 0 ? "; failed: " + string.Join(", ", fails) : "");
            Debug.Log("[EmotionV1] " + emoStatus + sb);
        }
    }
}
