using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    // ============================================================================================================================
    // Every music event is a shuffled playlist instead of one looping clip:
    //  - a track plays once to its natural ending and crossfades into the next track of the same mood (crossfade = the track's
    //    measured decay tail, 2.5-8 s); never the same track twice in a row (also not across a reshuffle or a mood change);
    //  - each cycle is a fresh shuffle of the level's own tracks plus ONE guest track of the other level's same mood
    //    (clips tagged line "guest" in openz_sound_events.json), so a 30 min match hears every calm / battle track;
    //  - the Zod danger switching (CALM / ATTACKING / FORT, debounce + hold) is unchanged: a mood switch still crossfades at once
    //    (2 s, fort 1 s) and enters the new track on a section start (process_music.py cue points) instead of a random second;
    //  - coming back to a mood within musicResumeWindow seconds resumes the track where it was left (a short skirmish no longer
    //    restarts the calm music from a new song every time);
    //  - after the game: the victory lap playlist on a win (then the menu playlist), the menu playlist on a defeat;
    //  - an event with ONE clip (or musicPlaylist off) loops that clip exactly as before (whole-file loop).
    // Track data: Resources/OpenZ/Audio/openz_music_meta.json (length, crossfade tail, entry cues, tempo, mood, energy).
    // ============================================================================================================================
    public partial class AudioDirector
    {
        [Serializable] public class MusicMeta { public string n, mood, flavour; public float len, tail, bpm, lufs; public int energy; public float[] entries; }
        [Serializable] public class MusicMetaTable { public int version; public float target_lufs; public MusicMeta[] tracks; }
        public const string MusicMetaPath = "OpenZ/Audio/openz_music_meta";

        [Header("Music playlist (music_v2)")]
        [Tooltip("music_v2: each mood plays a shuffled playlist (crossfade at each track's natural ending) instead of looping one clip")]
        public bool musicPlaylist = true;
        [Tooltip("music_v2: returning to a mood within this many seconds resumes the track where it was left")]
        public float musicResumeWindow = 90f;
        [Tooltip("music_v2: crossfade limits between two tracks of the same mood (the measured tail is clamped to this)")]
        public float musicMinXfade = 2.5f, musicMaxXfade = 8f;
        [Tooltip("music_v2: extra level of the victory lap after a win (the menu music after the game plays at -6 dB)")]
        public float victoryLapDb = -3f;

        /// <summary>music_v2: after this playlist's track ends, continue with that event's playlist.</summary>
        public static readonly Dictionary<string, string> MusicThen = new Dictionary<string, string> { ["mus_victory_lap"] = "mus_menu" };

        sealed class MusicBag
        {
            public readonly List<int> order = new List<int>();
            public int last = -1, lastGuest = -1, resumeIdx = -1;
            public float resumeTime, resumeAt = -1000f;
        }
        readonly Dictionary<string, MusicBag> musicBags = new Dictionary<string, MusicBag>();
        Dictionary<string, MusicMeta> musicMeta;
        int musicIdxNow = -1; float musicExtraDbNow, musicStartAt = -1f; string lastMusicClip;

        /// <summary>music_v2 checks: track changes, same clip twice in a row (must stay 0), the last 32 tracks played.</summary>
        public int MusicTrackChanges { get; private set; }
        public int MusicBackToBack { get; private set; }
        public readonly List<string> MusicHistory = new List<string>();
        /// <summary>music_v2: "event: clip @ time / length" of the track playing now (debug HUD, tests).</summary>
        public string MusicNowPlaying
        {
            get
            {
                if (musicA == null || musicDef == null) return "(none)";
                var c = musicOnA ? musicA : musicB;
                return c.src && c.src.clip ? $"{musicDef.id}: {c.src.clip.name} @ {c.src.time:0.0} / {c.src.clip.length:0.0} s{(c.src.loop ? " (loop)" : "")}" : "(none)";
            }
        }

        bool MusicPlaylistOn(SoundEventDef d) => musicPlaylist && d != null && d.loaded != null && d.loaded.Count >= 2;

        MusicMeta MetaOf(AudioClip c)
        {
            if (musicMeta == null)
            {
                musicMeta = new Dictionary<string, MusicMeta>();
                var ta = Resources.Load<TextAsset>(MusicMetaPath);
                if (ta)
                {
                    var t = JsonUtility.FromJson<MusicMetaTable>(ta.text);
                    if (t?.tracks != null) foreach (var m in t.tracks) if (!string.IsNullOrEmpty(m.n)) musicMeta[m.n] = m;
                }
            }
            return c && musicMeta.TryGetValue(c.name, out var mm) ? mm : null;
        }

        float MusicXfade(AudioClip c)
        {
            var m = MetaOf(c);
            float tail = m != null && m.tail > 0f ? m.tail : 4f;
            float len = c ? c.length : 60f;
            return Mathf.Clamp(Mathf.Min(tail, len * 0.25f), Mathf.Min(musicMinXfade, len * 0.25f), musicMaxXfade);
        }

        MusicBag Bag(string id) { if (!musicBags.TryGetValue(id, out var b)) musicBags[id] = b = new MusicBag(); return b; }

        static bool IsGuest(SoundEventDef d, int i) => d.lines != null && i < d.lines.Count && d.lines[i] == "guest";

        /// <summary>PlayMusic hook: idx >= 0 plays that clip (menu rotation, single-clip events, playlist off); idx &lt; 0 lets the
        /// playlist choose (resume, else the next of the shuffle). Also remembers where the outgoing track was.</summary>
        int MusicPick(SoundEventDef d, int idx)
        {
            musicStartAt = -1f;
            if (d == null || d.loaded == null || d.loaded.Count == 0) return 0;
            if (musicA != null && musicDef != null && musicIdxNow >= 0)
            {   // the outgoing track: resume it later if this mood comes back soon and it had a good part left
                var cur = musicOnA ? musicA : musicB;
                var b0 = Bag(musicDef.id);
                if (cur.busy && cur.fadeTarget > 0f && cur.src.clip && cur.src.isPlaying && cur.src.clip.length - cur.src.time > MusicXfade(cur.src.clip) + 20f)
                { b0.resumeIdx = musicIdxNow; b0.resumeTime = cur.src.time; b0.resumeAt = Time.unscaledTime; }
                else b0.resumeIdx = -1;
            }
            if (idx >= 0 || !MusicPlaylistOn(d)) { idx = Mathf.Clamp(Mathf.Max(0, idx), 0, d.loaded.Count - 1); NoteMusic(d, idx); return idx; }
            var b = Bag(d.id);
            if (b.resumeIdx >= 0 && b.resumeIdx < d.loaded.Count && Time.unscaledTime - b.resumeAt <= musicResumeWindow && d != musicDef)
            {
                idx = b.resumeIdx; b.resumeIdx = -1;
                musicStartAt = Mathf.Max(0f, b.resumeTime - 1f);
                NoteMusic(d, idx); return idx;
            }
            b.resumeIdx = -1;
            idx = NextFromBag(d, b);
            NoteMusic(d, idx);
            return idx;
        }

        int NextFromBag(SoundEventDef d, MusicBag b)
        {
            if (b.order.Count == 0)
            {   // a new cycle: the level's own tracks shuffled + one guest (not the last guest), never starting with the last track
                var own = new List<int>(); var guests = new List<int>();
                for (int i = 0; i < d.loaded.Count; i++) (IsGuest(d, i) ? guests : own).Add(i);
                if (own.Count == 0) { own.AddRange(guests); guests.Clear(); }
                for (int i = own.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); (own[i], own[j]) = (own[j], own[i]); }
                if (guests.Count > 0)
                {
                    int g = guests[UnityEngine.Random.Range(0, guests.Count)];
                    if (g == b.lastGuest && guests.Count > 1) g = guests[(guests.IndexOf(g) + 1 + UnityEngine.Random.Range(0, guests.Count - 1)) % guests.Count];
                    b.lastGuest = g;
                    own.Insert(UnityEngine.Random.Range(1, own.Count + 1), g);                 // never first: the level's own sound opens a cycle
                }
                b.order.AddRange(own);
            }
            // never the same clip twice in a row (this event's last, or the last clip heard anywhere)
            for (int k = 0; k < b.order.Count; k++)
            {
                int c = b.order[k];
                if (b.order.Count > 1 && (c == b.last || d.loaded[c].name == lastMusicClip)) continue;
                b.order.RemoveAt(k); return c;
            }
            int f = b.order[0]; b.order.RemoveAt(0); return f;
        }

        void NoteMusic(SoundEventDef d, int idx)
        {
            var name = d.loaded[idx] ? d.loaded[idx].name : "?";
            if (name == lastMusicClip && MusicPlaylistOn(d)) MusicBackToBack++;
            lastMusicClip = name; Bag(d.id).last = idx; MusicTrackChanges++;
            MusicHistory.Add(d.id + ":" + name); if (MusicHistory.Count > 32) MusicHistory.RemoveAt(0);
            Debug.Log($"[AudioDirector] music_v2: {d.id} -> {name} ({(MusicPlaylistOn(d) ? d.loaded.Count + "-track playlist" : "loop")})");
        }

        /// <summary>PlayMusic hook for AudioSource.time: a resume position, a section cue on a mood switch, else 0.</summary>
        float MusicEntry(SoundEventDef d, int idx, float len, bool randomEntry, float extraDb)
        {
            musicIdxNow = idx; musicExtraDbNow = extraDb;
            if (musicStartAt >= 0f) { float s = musicStartAt; musicStartAt = -1f; return Mathf.Clamp(s, 0f, Mathf.Max(0f, len - 25f)); }
            if (!randomEntry) return 0f;
            var m = MetaOf(d.loaded[idx]);
            if (m != null && m.entries != null && m.entries.Length > 0)
            {   // a section start that leaves most of the track (playlist) / at least 30 s (single loop)
                float room = MusicPlaylistOn(d) ? Mathf.Max(45f, len * 0.45f) : 30f;
                var ok = new List<float>();
                foreach (var e in m.entries) if (e >= 0f && e <= len - room) ok.Add(e);
                if (ok.Count > 0) return ok[UnityEngine.Random.Range(0, ok.Count)];
                return 0f;
            }
            return len > 40f ? Mathf.Floor(UnityEngine.Random.Range(0f, len - 30f) / 2f) * 2f : 0f;   // no meta: the old bar-aligned-ish entry
        }

        /// <summary>Update hook (after StepMusicVoices): crossfade into the next track at the playing track's tail.</summary>
        void MusicPlaylistTick()
        {
            if (!musicPlaylist || musicA == null || musicDef == null || !MusicOn) return;
            var cur = musicOnA ? musicA : musicB;
            if (!cur.busy || cur.fadeTarget <= 0f || !cur.src.clip || cur.src.loop) return;
            if (Time.unscaledTime - cur.start < 1f) return;                                 // just started (time may still read 0)
            float len = cur.src.clip.length, x = MusicXfade(cur.src.clip);
            bool ended = !cur.src.isPlaying;
            if (!ended && len - cur.src.time > x) return;
            string next = MusicThen.TryGetValue(musicDef.id, out var nx) && HasClips(nx) ? nx : musicDef.id;
            float extra = next == musicDef.id ? musicExtraDbNow : (next == "mus_menu" ? -6f : 0f);
            PlayMusic(Def(next), -1, ended ? 1f : x, extra, false);
        }

        /// <summary>music_v2 listening check (menu OpenZ > Music v2 > Skip): crossfade to the next track of the current playlist now.</summary>
        public void MusicSkip()
        {
            if (musicDef == null || !MusicOn) { Debug.Log("[AudioDirector] music_v2: no music playing"); return; }
            PlayMusic(musicDef, MusicPlaylistOn(musicDef) ? -1 : 0, 1.5f, musicExtraDbNow, false);
        }

        /// <summary>After the game: the victory lap on a win (when it has clips), else the menu playlist.</summary>
        string EndMusicId(bool won) => won && HasClips("mus_victory_lap") ? "mus_victory_lap" : "mus_menu";
        float EndMusicDb(bool won) => won && HasClips("mus_victory_lap") ? victoryLapDb : -6f;
    }
}
