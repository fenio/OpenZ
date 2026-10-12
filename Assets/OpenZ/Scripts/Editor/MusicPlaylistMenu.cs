using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>music_v2: OpenZ > Music v2 menu. Check = every music event's playlist resolves (clips load, meta present, guests
    /// tagged) and prints MUSIC CHECK OK / FAIL; in Play mode: Status (track now, history, back-to-back count) and Skip.</summary>
    public static class MusicPlaylistMenu
    {
        static readonly string[] Events = { "mus_menu", "mus_l02_calm", "mus_l02_attacking", "mus_l17_calm", "mus_l17_attacking", "mus_fort_danger", "mus_victory_lap" };

        [MenuItem("OpenZ/Music v2/Check playlists")]
        public static void Check()
        {
            var ta = Resources.Load<TextAsset>(AudioDirector.TablePath);
            var mt = Resources.Load<TextAsset>(AudioDirector.MusicMetaPath);
            var sb = new StringBuilder("[music_v2] playlist check\n");
            int fails = 0;
            if (!ta) { Debug.LogError("[music_v2] sound table missing"); return; }
            var table = JsonUtility.FromJson<SoundTable>(ta.text);
            var meta = new HashSet<string>();
            if (mt) { var m = JsonUtility.FromJson<AudioDirector.MusicMetaTable>(mt.text); if (m?.tracks != null) foreach (var t in m.tracks) meta.Add(t.n); }
            else { sb.AppendLine("  FAIL openz_music_meta.json missing"); fails++; }
            var byId = new Dictionary<string, SoundEventDef>();
            foreach (var e in table.events) byId[e.id] = e;
            foreach (var id in Events)
            {
                if (!byId.TryGetValue(id, out var e)) { sb.AppendLine($"  FAIL {id}: event missing"); fails++; continue; }
                int own = 0, guest = 0, bad = 0; float len = 0f;
                var names = new List<string>();
                foreach (var c in e.clips ?? new SoundClipRef[0])
                {
                    var clip = Resources.Load<AudioClip>(c.res);
                    if (!clip) { sb.AppendLine($"  FAIL {id}: clip {c.res} does not load"); bad++; continue; }
                    if (!meta.Contains(clip.name)) { sb.AppendLine($"  FAIL {id}: {clip.name} has no entry in openz_music_meta.json"); bad++; }
                    if (c.line == "guest") guest++; else { own++; len += clip.length; }
                    names.Add((c.line == "guest" ? "(guest) " : "") + clip.name + $" {clip.length:0}s {c.gainDb:+0.0;-0.0} dB");
                }
                fails += bad;
                if (own == 0) { sb.AppendLine($"  FAIL {id}: no own clip"); fails++; }
                sb.AppendLine($"  {(bad == 0 && own > 0 ? "OK  " : "FAIL")} {id}: {own} own + {guest} guest, {len / 60f:0.0} min of own music{(own + guest < 2 ? " (single clip: loops as before)" : "")}");
                foreach (var n in names) sb.AppendLine("       " + n);
            }
            sb.AppendLine(fails == 0 ? "MUSIC CHECK OK" : $"MUSIC CHECK FAIL ({fails})");
            if (fails == 0) Debug.Log(sb.ToString()); else Debug.LogError(sb.ToString());
        }

        [MenuItem("OpenZ/Music v2/Status (Play mode)")]
        public static void Status()
        {
            var ad = AudioDirector.I;
            if (!Application.isPlaying || !ad) { Debug.Log("[music_v2] enter Play mode first"); return; }
            Debug.Log($"[music_v2] now: {ad.MusicNowPlaying}\n  changes {ad.MusicTrackChanges}, back-to-back repeats {ad.MusicBackToBack} (must be 0)\n  history: {string.Join(" > ", ad.MusicHistory)}");
        }

        [MenuItem("OpenZ/Music v2/Skip to next track (Play mode)")]
        public static void Skip()
        {
            var ad = AudioDirector.I;
            if (!Application.isPlaying || !ad) { Debug.Log("[music_v2] enter Play mode first"); return; }
            ad.MusicSkip();
        }
    }
}
