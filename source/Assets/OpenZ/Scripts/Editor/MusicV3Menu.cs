using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>music_v3: OpenZ > Music v3. Check tracks = every added clip loads, is tagged "zopen" in its events, has a meta row and
    /// the right length (MUSIC V3 CHECK OK / FAIL, with a loud FAIL when music_v2's apply.py wiped the added clips); Run scenarios = the
    /// MusicCore timelines on this project's real playlists (EditMode is fine); in Play mode: Status, forced attacks (view-side reading
    /// only, the sim is untouched), a Zod line for the duck check, the overlay and Skip. In a level these replace OpenZ > Music v2 >
    /// Status / Skip (music_v2's A / B voices only play the main menu while music_v3 is on).</summary>
    public static class MusicV3Menu
    {
        static readonly string[] AddedEvents = { "mus_l02_calm", "mus_l17_calm", "mus_l02_attacking", "mus_l17_attacking", "mus_fort_danger" };
        const int WantAdded = 14 * 2 + 6 * 2 + 3;

        [Serializable] class Meta { public string n, src; public float len, tail, dramaIn, intensity; public float[] entries; }
        [Serializable] class MetaTable { public Meta[] tracks; }

        static (SoundTable table, Dictionary<string, Meta> meta) Load()
        {
            var ta = Resources.Load<TextAsset>(AudioDirector.TablePath);
            var mt = Resources.Load<TextAsset>(AudioDirector.MusicMetaPath);
            var meta = new Dictionary<string, Meta>();
            if (mt) { var m = JsonUtility.FromJson<MetaTable>(mt.text); if (m?.tracks != null) foreach (var t in m.tracks) meta[t.n] = t; }
            return (ta ? JsonUtility.FromJson<SoundTable>(ta.text) : null, meta);
        }

        [MenuItem("OpenZ/Music v3/Check tracks")]
        public static void Check()
        {
            var (table, meta) = Load();
            var sb = new StringBuilder("[music_v3] track check\n"); int fails = 0;
            if (table == null) { Debug.LogError("[music_v3] sound table missing"); return; }
            var byId = new Dictionary<string, SoundEventDef>(); foreach (var e in table.events) byId[e.id] = e;
            int added = 0;
            foreach (var id in AddedEvents)
            {
                if (!byId.TryGetValue(id, out var e)) { sb.AppendLine($"  FAIL {id}: event missing"); fails++; continue; }
                int own = 0, guest = 0, z = 0, bad0 = fails;
                foreach (var c in e.clips ?? new SoundClipRef[0])
                {
                    var clip = Resources.Load<AudioClip>(c.res);
                    string n = c.res.Substring(c.res.LastIndexOf('/') + 1);
                    if (!clip) { sb.AppendLine($"  FAIL {id}: {c.res} does not load"); fails++; continue; }
                    if (!meta.TryGetValue(clip.name, out var m)) { sb.AppendLine($"  FAIL {id}: {clip.name} has no meta row"); fails++; }
                    else if (Mathf.Abs(m.len - clip.length) > 0.6f) { sb.AppendLine($"  FAIL {id}: {clip.name} meta length {m.len:0.0} s vs clip {clip.length:0.0} s"); fails++; }
                    bool isZ = n.StartsWith("zopen", StringComparison.Ordinal);
                    if (isZ != (c.line == "zopen")) { sb.AppendLine($"  FAIL {id}: {n} line '{c.line}'"); fails++; }
                    if (c.line == "zopen") { z++; added++; if (clip.loadType != AudioClipLoadType.Streaming) { sb.AppendLine($"  FAIL {n}: not streaming"); fails++; } }
                    else if (c.line == "guest") guest++; else own++;
                }
                sb.AppendLine($"  {(fails == bad0 ? "OK  " : "FAIL")} {id}: {own} own + {guest} guest + {z} added");
            }
            int zRows = 0; foreach (var kv in meta) if (kv.Value.src == "zopen") zRows++;
            foreach (var kv in meta) if (kv.Key.StartsWith("zopendramatic", StringComparison.Ordinal)) sb.AppendLine($"       {kv.Key}: drama entry {kv.Value.dramaIn:0.0} s, intensity {kv.Value.intensity:+0.00;-0.00}");
            sb.AppendLine($"  added clip slots {added} (want {WantAdded} = 14 calm x 2 + 6 battle x 2 + 3 drama), zopen meta rows {zRows} (want 23)");
            if (added == 0 || zRows == 0)
            {
                sb.AppendLine("  FAIL THE ADDED MUSIC IS GONE: the sound table / openz_music_meta.json have no zopen entries although music_v3's scripts are installed.");
                sb.AppendLine("       the import script was probably run again after music_v3 (it rebuilds every mus_* list from its own manifest).");
                sb.AppendLine("       Fix: run the import script (idempotent), refresh Unity, run this check again.");
                fails++;
            }
            else if (added != WantAdded || zRows != 23) { sb.AppendLine($"  FAIL {added} added clip slots / {zRows} rows"); fails++; }
            sb.AppendLine(fails == 0 ? "MUSIC V3 CHECK OK" : $"MUSIC V3 CHECK FAIL ({fails})");
            if (fails == 0) Debug.Log(sb.ToString()); else Debug.LogError(sb.ToString());
        }

        /// <summary>The playlists MusicCore gets in a level (AudioDirector.BuildMusicV3 rules), from the table + meta.</summary>
        public static List<MusicTrack> Catalog(string lv)
        {
            var (table, meta) = Load();
            var list = new List<MusicTrack>();
            if (table == null) return list;
            var byId = new Dictionary<string, SoundEventDef>(); foreach (var e in table.events) byId[e.id] = e;
            void Add(string id, MusicPool pool)
            {
                if (!byId.TryGetValue(id, out var e) || e.clips == null) return;
                foreach (var c in e.clips)
                {
                    string n = c.res.Substring(c.res.LastIndexOf('/') + 1);
                    if (!meta.TryGetValue(n, out var m)) continue;
                    float tail = Mathf.Clamp(Mathf.Min(m.tail, m.len * 0.25f), Mathf.Min(2.5f, m.len * 0.25f), 8f);
                    list.Add(new MusicTrack { id = list.Count, name = n, pool = pool, length = m.len, tail = tail, gainDb = e.volDb + c.gainDb, entries = m.entries,
                                              dramaIn = pool == MusicPool.Drama ? m.dramaIn : 0f, intensity = m.intensity, eventId = id, zopen = c.line == "zopen", guest = c.line == "guest" });
                }
            }
            Add("mus_" + lv + "_calm", MusicPool.Calm); Add("mus_" + lv + "_attacking", MusicPool.Battle); Add("mus_fort_danger", MusicPool.Drama);
            Add("mus_victory_lap", MusicPool.Victory); Add("mus_menu", MusicPool.Menu);
            return list;
        }

        [MenuItem("OpenZ/Music v3/Run state machine scenarios")]
        public static void RunScenarios()
        {
            var sb = new StringBuilder();
            bool ok = MusicCoreScenarios.RunAll(Catalog("l02"), Catalog("l17"), s => sb.AppendLine(s), 20, 40);
            string head = sb.ToString(); int cut = head.IndexOf("DETAIL", StringComparison.Ordinal);
            string summary = cut > 0 ? head.Substring(0, cut) : head;
            if (ok) Debug.Log("[music_v3] " + summary); else Debug.LogError("[music_v3] " + summary);
            try { var p = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "..", "..", "work", "tmp", "music_v3", "editor_scenarios.txt")); System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)); System.IO.File.WriteAllText(p, head); Debug.Log("[music_v3] timelines: " + p); }
            catch (Exception ex) { Debug.LogWarning("[music_v3] could not write the timelines: " + ex.Message); }
        }

        static AudioDirector Live() { var ad = AudioDirector.I; if (!Application.isPlaying || !ad) { Debug.Log("[music_v3] enter Play mode in a level first"); return null; } return ad; }

        [MenuItem("OpenZ/Music v3/Status (Play mode)")]
        public static void Status() { var ad = Live(); if (ad) Debug.Log(ad.MusicV3Status() + "\n" + string.Join("\n", ad.MusicV3Core != null ? ad.MusicV3Core.History : new List<string>())); }

        [MenuItem("OpenZ/Music v3/Force big attack on OUR fort 20 s (Play mode)")]
        public static void ForceOurs() { var ad = Live(); if (ad) ad.MusicV3Force(1, 20f); }

        [MenuItem("OpenZ/Music v3/Force big attack on ENEMY fort 20 s (Play mode)")]
        public static void ForceTheirs() { var ad = Live(); if (ad) ad.MusicV3Force(2, 20f); }

        [MenuItem("OpenZ/Music v3/Force HEAVY attack on OUR fort 20 s (Play mode)")]
        public static void ForceHeavy() { var ad = Live(); if (ad) ad.MusicV3Force(4, 20f); }

        [MenuItem("OpenZ/Music v3/Force ONE unit shooting our fort 30 s - must NOT start the drama (Play mode)")]
        public static void ForceOne() { var ad = Live(); if (ad) ad.MusicV3Force(3, 30f); }

        [MenuItem("OpenZ/Music v3/Force idle units parked at our fort 60 s - must NOT start the drama (Play mode)")]
        public static void ForceIdle() { var ad = Live(); if (ad) ad.MusicV3Force(5, 60f); }

        [MenuItem("OpenZ/Music v3/Clear forced reading (Play mode)")]
        public static void ForceClear() { var ad = Live(); if (ad) ad.MusicV3Force(0, 0f); }

        [MenuItem("OpenZ/Music v3/Zod line now - duck check (Play mode)")]
        public static void ZodLine() { if (Live()) Debug.Log("[music_v3] " + ZodBriefing.DebugIdle()); }

        [MenuItem("OpenZ/Music v3/Toggle overlay")]
        public static void Overlay() { AudioDirector.MusicV3Overlay = !AudioDirector.MusicV3Overlay; Debug.Log("[music_v3] overlay " + (AudioDirector.MusicV3Overlay ? "on" : "off")); }

        [MenuItem("OpenZ/Music v3/Skip regular song (Play mode)")]
        public static void Skip() { var ad = Live(); if (ad) ad.MusicV3Skip(); }
    }
}
