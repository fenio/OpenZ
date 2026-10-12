using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// While playing (and the option is on), the Game view is re-maximised whenever something un-maximises it.
    /// </summary>
    [InitializeOnLoad]
    public static class KeepGameViewMaximized
    {
        public static bool On { get => EditorPrefs.GetBool("OpenZ.KeepGameViewMax", true); set => EditorPrefs.SetBool("OpenZ.KeepGameViewMax", value); }
        static double next;
        static readonly System.Type GameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");

        static KeepGameViewMaximized() { EditorApplication.update += Tick; }

        static void Tick()
        {
            if (!On || !EditorApplication.isPlaying || EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + 0.5;
            var views = Resources.FindObjectsOfTypeAll(GameViewType);
            if (views.Length == 0) return;
            var gv = (EditorWindow)views[0];
            if (!gv.maximized) { gv.maximized = true; Debug.Log("[KeepGameViewMaximized] Game view re-maximised"); }
        }
    }
}
