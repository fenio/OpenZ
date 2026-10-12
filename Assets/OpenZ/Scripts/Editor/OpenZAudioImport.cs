using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Import settings for Assets/OpenZ/Resources/OpenZ/Audio (sound design bible section 6), applied automatically on import:
    ///  - Sfx / Ambience one-shots: Decompress On Load, Vorbis q70, preload; Force To Mono unless every event using the clip is 2D
    ///  - loops (any event with loop = true): Compressed In Memory, Vorbis q60; beds longer than 10 s: Streaming
    ///  - Voice: Force To Mono, Decompress On Load, Vorbis q70
    ///  - Music: Streaming, Vorbis q60, stereo
    /// Reads Resources/OpenZ/Audio/openz_sound_events.json to know which clips loop and which are 2D only.
    /// Menu OpenZ/Audio/Reimport Audio re-applies the settings to every clip.
    /// </summary>
    public class OpenZAudioImport : AssetPostprocessor
    {
        const string Root = "Assets/OpenZ/Resources/OpenZ/Audio/";
        static HashSet<string> loopClips, only2D; static long tableStamp = -1;   // long: a float cannot hold DateTime ticks (~1 h steps)

        [System.Serializable] class ClipRef { public string res; }
        [System.Serializable] class Ev { public string id; public bool is3D, loop; public ClipRef[] clips; }
        [System.Serializable] class Table { public Ev[] events; }

        static void LoadTable()
        {
            var path = Root + "openz_sound_events.json";
            if (!File.Exists(path)) { loopClips = new HashSet<string>(); only2D = new HashSet<string>(); return; }
            long stamp = File.GetLastWriteTimeUtc(path).Ticks;
            if (loopClips != null && stamp == tableStamp) return;
            tableStamp = stamp;
            var t = JsonUtility.FromJson<Table>(File.ReadAllText(path));
            loopClips = new HashSet<string>(); var used3D = new HashSet<string>(); var used2D = new HashSet<string>();
            foreach (var e in t.events)
            {
                if (e.clips == null) continue;
                foreach (var c in e.clips)
                {
                    var name = Path.GetFileName(c.res);
                    if (e.loop) loopClips.Add(name);
                    (e.is3D ? used3D : used2D).Add(name);
                }
            }
            only2D = new HashSet<string>(used2D); only2D.ExceptWith(used3D);
        }

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Root)) return;
            LoadTable();
            var imp = (AudioImporter)assetImporter;
            var name = Path.GetFileNameWithoutExtension(assetPath);
            var folder = assetPath.Substring(Root.Length).Split('/')[0];
            var s = imp.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            bool loop = loopClips.Contains(name);
            long bytes = new FileInfo(assetPath).Length;
            switch (folder)
            {
                case "Music":
                    s.loadType = AudioClipLoadType.Streaming; s.quality = 0.6f; imp.forceToMono = false; imp.loadInBackground = true;
                    break;
                case "Voice":
                    s.loadType = AudioClipLoadType.DecompressOnLoad; s.quality = 0.7f; imp.forceToMono = true;
                    break;
                default:   // Sfx, Ambience
                    imp.forceToMono = !only2D.Contains(name);
                    if (loop) { s.loadType = bytes > 1_700_000 ? AudioClipLoadType.Streaming : AudioClipLoadType.CompressedInMemory; s.quality = 0.6f; }
                    else { s.loadType = AudioClipLoadType.DecompressOnLoad; s.quality = 0.7f; }
                    break;
            }
            s.preloadAudioData = s.loadType != AudioClipLoadType.Streaming;
            imp.defaultSampleSettings = s;
        }

        [MenuItem("OpenZ/Audio/Reimport Audio")]
        static void ReimportAll()
        {
            tableStamp = -1;
            foreach (var g in AssetDatabase.FindAssets("t:AudioClip", new[] { Root.TrimEnd('/') }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(g), ImportAssetOptions.ForceUpdate);
        }

        // DC16: adaptive music on in normal play, off for the recorded takes (PlayerPrefs oz_music; works in Play mode too)
        const string MusicMenu = "OpenZ/Audio/Music In Game (oz_music)";
        [MenuItem(MusicMenu)]
        static void ToggleMusic() { AudioDirector.MusicEnabled = !AudioDirector.MusicEnabled; Debug.Log("[OpenZ] in-game music " + (AudioDirector.MusicEnabled ? "ON" : "OFF (takes)")); }
        [MenuItem(MusicMenu, true)]
        static bool ToggleMusicCheck() { Menu.SetChecked(MusicMenu, AudioDirector.MusicEnabled); return true; }

        /// <summary></summary>
        [MenuItem("OpenZ/Audio/Write Sound Coverage Report")]
        static void WriteCoverage()
        {
            var ad = AudioDirector.I;
            if (!Application.isPlaying || !ad) { Debug.LogWarning("[OpenZ] sound coverage: enter Play mode in a level first"); return; }
            var path = Path.Combine(OpenZPaths.Records, "sound_coverage.md");
            ad.WriteCoverageReport(path);
            Debug.Log("[OpenZ] sound coverage written: " + path);
        }
    }
}
