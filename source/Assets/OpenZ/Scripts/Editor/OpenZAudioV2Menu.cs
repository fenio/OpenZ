using UnityEditor;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// - Level Probe: in Play mode, in a level: freezes the sim, default 70 m view, one sound per category at the screen centre, measured
    /// - Loudness Meter: shows / hides the read-out (same as Ctrl+Shift+F9 in play).
    /// - Import Check: reads the imported peak of a few voice and SFX clips (Normalize off = the file peaks, on = about 0 dBFS).
    /// </summary>
    public static class OpenZAudioV2Menu
    {
        [MenuItem("OpenZ/Audio/audio_v2 Level Probe (Play mode, level scene)")]
        static void Probe()
        {
            if (!Application.isPlaying) { EditorUtility.DisplayDialog("audio_v2 Level Probe", "Enter Play mode in L02_Desert or L17_CarPark first (full look, 60 fps cap), wait for the level to start, then run the probe again.", "OK"); return; }
            var m = OzLoudnessMeter.I;
            if (!m) { Debug.LogWarning("[audio_v2] no OzLoudnessMeter on the listener rig yet (CombatAudio adds it when the RTS camera exists)"); return; }
            m.showOverlay = true;
            m.StartCoroutine(m.Probe(null));
            Debug.Log("[audio_v2] level probe started (about 60 s, the sim is frozen meanwhile)");
        }

        [MenuItem("OpenZ/Audio/audio_v2 Loudness Meter (toggle)")]
        static void Meter()
        {
            var m = OzLoudnessMeter.I;
            if (!m) { Debug.LogWarning("[audio_v2] the meter lives on the listener rig in Play mode (level scenes)"); return; }
            m.showOverlay = !m.showOverlay; if (m.showOverlay) m.ResetStats();
        }

        [MenuItem("OpenZ/Audio/audio_v2 Import Check (Normalize off?)")]
        static void ImportCheck()
        {
            string[] clips = { "OpenZ/Audio/Voice/alan_moving_out_1", "OpenZ/Audio/Voice/alan_yes_sir_1", "OpenZ/Audio/Voice/rachel_robot_manufactured_1", "OpenZ/Audio/Sfx/sd_ui_click_a", "OpenZ/Audio/Sfx/sd_door_blast_open_a" };
            float[] filePeak = { -6.3f, -1.9f, -2.2f, -1.2f, -1.0f };                                    // mono-downmix peaks of the files (tools/clip_metrics.py)
            int normalized = 0, read = 0;
            for (int i = 0; i < clips.Length; i++)
            {
                var c = Resources.Load<AudioClip>(clips[i]);
                if (!c) { Debug.LogWarning("[audio_v2] import check: missing " + clips[i]); continue; }
                if (c.loadType != AudioClipLoadType.DecompressOnLoad) c.LoadAudioData();
                var d = new float[c.samples * c.channels];
                if (!c.GetData(d, 0)) { Debug.Log($"[audio_v2] import check: {c.name} cannot be read ({c.loadType}); skip"); continue; }
                float pk = 0f; foreach (var s in d) { float a = Mathf.Abs(s); if (a > pk) pk = a; }
                float db = 20f * Mathf.Log10(Mathf.Max(pk, 1e-6f)); read++;
                bool norm = db > filePeak[i] + 1.5f && db > -0.8f;
                if (norm) normalized++;
                Debug.Log($"[audio_v2] import check: {c.name} imported peak {db:0.0} dBFS (file {filePeak[i]:0.0}) -> {(norm ? "NORMALIZED (Normalize still on?)" : "file level (ok)")}");
            }
            Debug.Log($"[audio_v2] import check: {read} clips read, {normalized} normalized. Normalized voices play up to 9 dB louder than designed: run apply_audio_v2.py --apply (step 4) or untick Normalize.");
        }
    }
}
