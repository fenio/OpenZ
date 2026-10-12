// OpenZ (GPL-3.0) - Commander Zod cameo: the voice lines (Resources/OpenZ/Zod/zod_lines.json = Materials/zod/voice/lines.json,
// shots and effects anchored on words of each line). Plain data; ZodBriefing plays it.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OpenZ
{
    [Serializable] public class ZodWord { public string word; public float start, end; }
    [Serializable] public class ZodCue { public string text; public float start, end; }
    [Serializable] public class ZodLine { public string id, group, trigger, text; public float duration; public ZodCue[] cues; public ZodWord[] words; }
    [Serializable] public class ZodLineGroups { public string[] menu, map_select, briefing, victory, defeat, idle, nuke; }
    [Serializable] public class ZodLineFile { public string project; public ZodLineGroups groups; public ZodLine[] lines; }

    /// <summary>A moment in a line: the start of the n-th occurrence of `word` (case and punctuation ignored) + offset, or the time
    /// `t` (seconds from the voice start, negative = before the voice) when no word is given.</summary>
    [Serializable] public class ZodBeatAt { public string word; public int n = 1; public float t = -99f; public float offset; }
    /// <summary>Animator clip at a moment. bed = it becomes the looping clip under the line; otherwise a one-shot that returns to the
    /// bed after its length (or `hold` seconds when hold > 0).</summary>
    [Serializable] public class ZodGesture : ZodBeatAt { public string clip; public bool bed; public float fade = 0.25f; public float hold = -1f; }
    /// <summary>Briefing camera shot (ZodFlyover.Shots) from a moment on; smooth = SmoothDamp time.</summary>
    [Serializable] public class ZodShot : ZodBeatAt { public string shot; public float smooth = 1.2f; }
    /// <summary>Effect at a moment: "badge" (30th-anniversary stamp), "puff" (cigar glow + smoke), "exhale" (smoke cloud),
    /// "chatter" (laugh jaw for `value` seconds), "framing:tight|medium|wide". v2 transmission fx: "punch" (zoom punch-in on his
    /// face, value = strength, hold = seconds), "pull" (pull-out for `value` seconds), "thrust" (the fist jabs at the camera, hold
    /// `value` s after the jab), "glitch" (signal burst: static, tearing, colour split + a static crackle; value = strength,
    /// hold = seconds), "kick" (a jolt of the hand-held camera).</summary>
    [Serializable] public class ZodFx : ZodBeatAt { public string fx; public float value = 1f; public float hold = -1f; }
    [Serializable]
    public class ZodBeatLine
    {
        public string id;
        public string preroll = "idle";                 // clip while the comms window opens
        public float tail = 0.9f;                         // seconds the window stays after the voice ends
        public string framing = "medium";
        public ZodGesture[] gestures; public ZodShot[] shots; public ZodFx[] fx;
    }
    [Serializable] public class ZodBeatFile { public int version; public ZodBeatLine[] lines; }

    /// <summary>Lines + beats loaded once per session from Resources; voice clips loaded on demand (Resources/OpenZ/Zod/Voice/&lt;id&gt;).</summary>
    public sealed class ZodLibrary
    {
        public const string LinesPath = "OpenZ/Zod/zod_lines", BeatsPath = "OpenZ/Zod/zod_beats", VoicePath = "OpenZ/Zod/Voice/";
        static ZodLibrary shared;
        public static ZodLibrary Shared => shared ??= new ZodLibrary();

        readonly Dictionary<string, ZodLine> lines = new Dictionary<string, ZodLine>();
        readonly Dictionary<string, ZodBeatLine> beats = new Dictionary<string, ZodBeatLine>();
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        public ZodLineGroups Groups { get; private set; }
        public bool Ok => lines.Count > 0;

        ZodLibrary()
        {
            try
            {
                var ta = Resources.Load<TextAsset>(LinesPath);
                if (ta)
                {
                    var f = JsonUtility.FromJson<ZodLineFile>(ta.text);
                    if (f?.lines != null) foreach (var l in f.lines) if (l != null && !string.IsNullOrEmpty(l.id)) lines[l.id] = l;
                    Groups = f?.groups;
                }
                var tb = Resources.Load<TextAsset>(BeatsPath);
                if (tb)
                {
                    var b = JsonUtility.FromJson<ZodBeatFile>(tb.text);
                    if (b?.lines != null) foreach (var l in b.lines) if (l != null && !string.IsNullOrEmpty(l.id)) beats[l.id] = l;
                }
            }
            catch (Exception e) { Debug.LogWarning("[ZodCameo] lines / beats not readable: " + e.Message); }
            if (lines.Count == 0) Debug.LogWarning("[ZodCameo] no voice lines in Resources/" + LinesPath + ".json (README_IMPORT step 1)");
            Groups ??= new ZodLineGroups();
        }

        public ZodLine Line(string id) => id != null && lines.TryGetValue(id, out var l) ? l : null;
        public ZodBeatLine Beats(string id) => id != null && beats.TryGetValue(id, out var b) ? b : new ZodBeatLine { id = id };

        public AudioClip Clip(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (clips.TryGetValue(id, out var c) && c) return c;
            c = Resources.Load<AudioClip>(VoicePath + id);
            if (c && c.loadState == AudioDataLoadState.Unloaded) c.LoadAudioData();
            clips[id] = c;
            return c;
        }

        public string[] Group(string name)
        {
            var g = Groups;
            switch (name)
            {
                case "menu": return g.menu ?? new string[0];
                case "map_select": return g.map_select ?? new string[0];
                case "briefing": return g.briefing ?? new string[0];
                case "victory": return g.victory ?? new string[0];
                case "defeat": return g.defeat ?? new string[0];
                case "idle": return g.idle ?? new string[0];
                case "nuke": return g.nuke ?? new string[0];                                                      // voice_lines_v3
            }
            return new string[0];
        }

        /// <summary>voice_lines_v3: the group has at least one line with its clip (no PlayerPrefs write, unlike Pick).</summary>
        public bool HasGroup(string group) { foreach (var id in Group(group)) if (Line(id) != null && Clip(id)) return true; return false; }

        /// <summary>Random line of a group without repeats: every line of the group plays once, in random order, before any line
        /// comes back (a shuffle bag kept in PlayerPrefs, so across matches too), and a new round never opens with the line
        /// that was played last. Only lines whose clip exists.</summary>
        public string Pick(string group)
        {
            var all = new List<string>();
            foreach (var id in Group(group)) if (Line(id) != null && Clip(id)) all.Add(id);
            if (TeamColors.Swap && all.Exists(id => !TeamColors.NamesColour(Line(id).text))) all.RemoveAll(id => TeamColors.NamesColour(Line(id).text));
            if (all.Count == 0) return null;
            string key = "oz_zod_last_" + group, bagKey = "oz_zod_bag_" + group, last = PlayerPrefs.GetString(key, "");
            var bag = new List<string>();
            foreach (var id in PlayerPrefs.GetString(bagKey, "").Split(',')) if (id.Length > 0 && all.Contains(id) && !bag.Contains(id)) bag.Add(id);
            if (bag.Count == 0) bag.AddRange(all);                                    // a new round: every line again
            // The most recent half of the group is never picked while another line is free, so a line cannot come back after
            // only one or two others when a new round starts (it stays in the bag for later in the round).
            string recentKey = "oz_zod_recent_" + group;
            var recent = new List<string>();
            foreach (var id in PlayerPrefs.GetString(recentKey, "").Split(',')) if (id.Length > 0) recent.Add(id);
            var from = bag.FindAll(id => !recent.Contains(id));
            if (from.Count == 0) { from = new List<string>(bag); if (from.Count > 1) from.Remove(last); }
            var pick = from[UnityEngine.Random.Range(0, from.Count)];
            bag.Remove(pick);
            recent.Remove(pick); recent.Add(pick);
            int keep = Mathf.Max(1, all.Count / 2);
            while (recent.Count > keep) recent.RemoveAt(0);
            PlayerPrefs.SetString(recentKey, string.Join(",", recent.ToArray()));
            PlayerPrefs.SetString(bagKey, string.Join(",", bag.ToArray()));
            PlayerPrefs.SetString(key, pick);
            return pick;
        }

        // ------------------------------------------------------------------------------------------------ word anchors
        static readonly StringBuilder sb = new StringBuilder(32);
        public static string Norm(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            sb.Clear();
            foreach (var ch in s) if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            return sb.ToString();
        }

        /// <summary>Seconds from the voice start of a beat moment; NaN when its word is not in the line.</summary>
        public static float Resolve(ZodLine line, ZodBeatAt at)
        {
            if (at == null) return float.NaN;
            if (string.IsNullOrEmpty(at.word)) return (at.t > -98f ? at.t : 0f) + at.offset;
            if (line?.words == null) return float.NaN;
            string w = Norm(at.word); int seen = 0, want = Mathf.Max(1, at.n);
            foreach (var x in line.words)
                if (Norm(x.word) == w && ++seen == want) return x.start + at.offset;
            return float.NaN;
        }

        /// <summary>The subtitle cue at time t (cues already hold until the next one when the gap is short), null in the gaps.</summary>
        public static string CueAt(ZodLine line, float t, float linger = 0.35f)
        {
            if (line?.cues == null) return null;
            for (int i = 0; i < line.cues.Length; i++)
            {
                var c = line.cues[i];
                bool last = i == line.cues.Length - 1;
                if (t >= c.start - 0.05f && t < c.end + (last ? linger : 0f)) return c.text;
            }
            return null;
        }
    }
}
