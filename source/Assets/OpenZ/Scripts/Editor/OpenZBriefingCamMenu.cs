using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Cinematic briefing camera: OpenZ/Briefing Camera menu.
    ///  - Play Briefing From Start: the real level-start briefing (Zod's transmission, the GENERAL joke, the letterbox, the shot list,
    ///    the hand-back) on the open level. Outside Play it enters Play first and starts it once the level is up (a level opened directly
    ///    gets no briefing of its own). With no Zod cameo in the scene it falls back to the camera-only preview.
    ///  - Camera Only Preview: the shot list alone with the real briefing timing (ZodCinePreview).
    ///  - Hold Shot 1-5 / Release Hold: freeze one shot at the middle of its move for a frame check.
    ///  - Log Status: the shot list state and the last fitted plan.
    /// </summary>
    [InitializeOnLoad]
    public static class OpenZBriefingCamMenu
    {
        const string PendingKey = "oz_briefcam_pending";
        static double levelSeen = -1;

        static OpenZBriefingCamMenu() { EditorApplication.update -= Poll; EditorApplication.update += Poll; }

        [MenuItem("OpenZ/Briefing Camera/Play Briefing From Start", false, 10)]
        static void PlayBriefing()
        {
            ZodFlyover.HoldShot = -1;
            if (!EditorApplication.isPlaying)
            {
                SessionState.SetBool(PendingKey, true); levelSeen = -1;
                Debug.Log("[OpenZ BriefingCam] entering Play: the briefing starts once the level is up");
                EditorApplication.EnterPlaymode();
                return;
            }
            Debug.Log("[OpenZ BriefingCam] " + StartBriefing());
        }

        static string StartBriefing()
        {
            if (ZodBriefing.BriefingActive) return "a briefing is already running: " + ZodFlyover.Status();
            ZodCinePreview.Stop();
            string r = ZodBriefing.DebugBriefing();
            if (r.StartsWith("briefing started")) return r + " (shot list: see the [ZodFlyover] lines)";
            return r + " -> camera-only preview: " + ZodCinePreview.Play();
        }

        static void Poll()
        {
            if (!SessionState.GetBool(PendingKey, false)) return;
            if (!EditorApplication.isPlaying)
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode) SessionState.SetBool(PendingKey, false);   // Play was cancelled
                return;
            }
            var game = ZGame.I;
            if (!game || game.World == null) return;
            if (levelSeen < 0) { levelSeen = EditorApplication.timeSinceStartup; return; }
            if (EditorApplication.timeSinceStartup - levelSeen < 1.6) return;                                      // the Zod stage prebuilds 1.5 s in
            SessionState.SetBool(PendingKey, false); levelSeen = -1;
            Debug.Log("[OpenZ BriefingCam] " + StartBriefing());
        }

        [MenuItem("OpenZ/Briefing Camera/Camera Only Preview", false, 11)]
        static void CameraOnly() => Debug.Log("[OpenZ BriefingCam] " + ZodCinePreview.Play());

        [MenuItem("OpenZ/Briefing Camera/Hold Shot 1", false, 30)] static void Hold1() => Hold(1);
        [MenuItem("OpenZ/Briefing Camera/Hold Shot 2", false, 31)] static void Hold2() => Hold(2);
        [MenuItem("OpenZ/Briefing Camera/Hold Shot 3", false, 32)] static void Hold3() => Hold(3);
        [MenuItem("OpenZ/Briefing Camera/Hold Shot 4", false, 33)] static void Hold4() => Hold(4);
        [MenuItem("OpenZ/Briefing Camera/Hold Shot 5", false, 34)] static void Hold5() => Hold(5);
        static void Hold(int k) => Debug.Log("[OpenZ BriefingCam] " + ZodCinePreview.Hold(k));

        [MenuItem("OpenZ/Briefing Camera/Release Hold", false, 40)]
        static void Release() => Debug.Log("[OpenZ BriefingCam] " + ZodCinePreview.Release());

        [MenuItem("OpenZ/Briefing Camera/Stop Preview", false, 41)]
        static void StopPreview() => Debug.Log("[OpenZ BriefingCam] " + ZodCinePreview.Stop());

        [MenuItem("OpenZ/Briefing Camera/Log Status", false, 50)]
        static void LogStatus() => Debug.Log("[OpenZ BriefingCam] " + ZodFlyover.Status() + " | " + FlybySky.Status());

        [MenuItem("OpenZ/Briefing Camera/Camera Only Preview", true)]
        [MenuItem("OpenZ/Briefing Camera/Hold Shot 1", true)]
        [MenuItem("OpenZ/Briefing Camera/Hold Shot 2", true)]
        [MenuItem("OpenZ/Briefing Camera/Hold Shot 3", true)]
        [MenuItem("OpenZ/Briefing Camera/Hold Shot 4", true)]
        [MenuItem("OpenZ/Briefing Camera/Hold Shot 5", true)]
        [MenuItem("OpenZ/Briefing Camera/Release Hold", true)]
        [MenuItem("OpenZ/Briefing Camera/Stop Preview", true)]
        static bool InPlay() => EditorApplication.isPlaying;
    }
}
