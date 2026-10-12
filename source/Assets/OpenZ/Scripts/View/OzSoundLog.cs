using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// play request of CombatAudio and AudioDirector with what happened to it (played at which volume and distance, or dropped
    /// and why). Ring buffer of the last 2000 entries; OzCombatSoundCheck and the menu OpenZ/Audio/Sound Log dump it.
    /// </summary>
    public static class OzSoundLog
    {
        public struct Entry { public float t; public string src, key, clip, outcome; public float vol, dist; }
        public const int Max = 2000;
        static readonly List<Entry> ring = new List<Entry>(Max);
        static int head;
        public static bool On = true;
        static AudioListener listener;
        public static int Count => ring.Count;

        public static void Add(string src, string key, string clip, string outcome, float vol = 0f, Vector3? pos = null)
        {
            if (!On || !Application.isPlaying) return;
            float d = -1f;
            if (pos.HasValue)
            {
                if (!listener || !listener.isActiveAndEnabled) listener = Object.FindFirstObjectByType<AudioListener>();
                if (listener) d = Vector3.Distance(listener.transform.position, pos.Value);
            }
            var e = new Entry { t = Time.unscaledTime, src = src, key = key, clip = clip, outcome = outcome, vol = vol, dist = d };
            if (ring.Count < Max) ring.Add(e); else { ring[head] = e; head = (head + 1) % Max; }
        }

        /// <summary>Entries since unscaled time t0, oldest first.</summary>
        public static List<Entry> Since(float t0)
        {
            var o = new List<Entry>();
            for (int i = 0; i < ring.Count; i++) { var e = ring[(head + i) % ring.Count]; if (e.t >= t0) o.Add(e); }
            return o;
        }

        public static string Format(List<Entry> es)
        {
            var sb = new StringBuilder();
            foreach (var e in es) sb.AppendLine($"{e.t,8:F2}  {e.src,-4} {e.key,-24} {e.clip ?? "-",-32} {e.outcome,-22} vol {e.vol:F3}  dist {e.dist:F1} m");
            return sb.ToString();
        }
    }
}
