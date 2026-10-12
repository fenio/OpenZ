using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary></summary>
    public static class OpenZCombatSoundCheckMenu
    {
        [MenuItem("OpenZ/Audio/Combat Sound Check (Play)")]
        static void Run()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[OzCombatSoundCheck] enter Play mode in a level first"); return; }
            OzCombatSoundCheck.Run();
        }

        [MenuItem("OpenZ/Audio/Sound Log: dump last 60 s")]
        static void Dump()
        {
            var es = OzSoundLog.Since(Time.unscaledTime - 60f);
            var path = System.IO.Path.Combine(OpenZPaths.Records, "sound_log_last60s.txt");
            System.IO.File.WriteAllText(path, OzSoundLog.Format(es));
            Debug.Log($"[OzSoundLog] {es.Count} entries -> {path}");
        }
    }
}
