using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// "OpenZ/Gate Battery": runs GateBattery (runtime) on L02, then L17, then the main menu, each in its own Play session in
    /// the FULL look (PlayerPrefs oz_testmode = 0, the battery itself caps 20 fps). Frames + gate_log.json land in
    /// "Gate Battery (this level)" runs only the open scene. "Gate Battery: Stop" clears the queue.
    /// </summary>
    [InitializeOnLoad]
    public static class OpenZGateBattery
    {
        const string QueueKey = "oz_gate_queue";
        static readonly string[] Scenes = { "Assets/OpenZ/Scenes/L02_Desert.unity", "Assets/OpenZ/Scenes/L17_CarPark.unity", "Assets/OpenZ/Scenes/MainMenu.unity" };

        static OpenZGateBattery()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.EnteredEditMode && SessionState.GetString(QueueKey, "") != "") pendingNext = true;   // delayCall does not fire while the editor is unfocused; Poll does
                if (s == PlayModeStateChange.EnteredEditMode && SessionState.GetString(QueueKey, "") == "") { PlayerPrefs.SetInt(GateBattery.Pref, 0); PlayerPrefs.SetString("oz_gate_only", ""); PlayerPrefs.Save(); }
            };
        }

        [MenuItem("OpenZ/Gate Battery", false, 1)]
        public static void RunAll() { PlayerPrefs.SetString("oz_gate_only", ""); Start(string.Join(";", Scenes)); }

        [MenuItem("OpenZ/Gate Battery (this level)", false, 2)]
        public static void RunThis() { PlayerPrefs.SetString("oz_gate_only", ""); Start(EditorSceneManager.GetActiveScene().path); }

        /// <summary>Only some steps (comma list of step names), e.g. after fixes; frames go to gate/&lt;level&gt;/recheck/.</summary>
        public static void RunSubset(string scenes, string steps)
        {
            PlayerPrefs.SetString("oz_gate_only", steps); PlayerPrefs.Save();
            Start(scenes);
        }

        [MenuItem("OpenZ/Gate Battery: Stop", false, 3)]
        public static void Stop() { SessionState.EraseString(QueueKey); PlayerPrefs.SetInt(GateBattery.Pref, 0); PlayerPrefs.Save(); if (EditorApplication.isPlaying) EditorApplication.isPlaying = false; Debug.Log("[Gate] stopped"); }

        static void Start(string queue)
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Gate] stop Play first"); return; }
            EditorSceneManager.SaveOpenScenes(); AssetDatabase.SaveAssets();
            SessionState.SetString(QueueKey, queue);
            Next();
        }

        static void Next()
        {
            if (EditorApplication.isPlaying) return;
            var q = SessionState.GetString(QueueKey, "");
            if (q == "") return;
            var parts = q.Split(';');
            var scene = parts[0];
            SessionState.SetString(QueueKey, string.Join(";", parts, 1, parts.Length - 1));
            EditorSceneManager.OpenScene(scene);
            PlayerPrefs.SetInt("oz_testmode", 0); PlayerPrefs.SetInt("oz_botmatch", 0); PlayerPrefs.SetInt(GateBattery.Pref, 1); PlayerPrefs.Save();
            Debug.Log($"[Gate] {scene} (left: {SessionState.GetString(QueueKey, "")})");
            EditorApplication.isPlaying = true;
        }

        static bool pendingNext;

        static void Poll()
        {
            if (pendingNext && !EditorApplication.isPlaying && !EditorApplication.isCompiling && !EditorApplication.isUpdating) { pendingNext = false; Next(); return; }
            if (EditorApplication.isPlaying && GateBattery.Done && !EditorApplication.isPaused)
            {
                EditorApplication.isPlaying = false;                                   // EnteredEditMode then starts the next level
                if (SessionState.GetString(QueueKey, "") == "") Debug.Log("[Gate] battery finished: work/records/gate/");
            }
        }
    }
}
