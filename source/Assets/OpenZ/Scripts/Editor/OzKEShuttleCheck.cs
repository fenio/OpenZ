// OpenZ (GPL-3.0) - version 1.1 items 229 / 230, the checks in Play (level 16).
//  * OpenZ/V1.1/KE Throw The Shuttle (Play): throws the shuttle now from the middle of the fort nearest to it, as the LOSE ending's
//    blast does (OzShuttleBlastThrow.ThrowNow), and 4 s after its launch prints the throw's numbers and PASS / FAIL lines;
//  * the same + Pauses For Pictures: the editor pauses 0.5 s and 1.5 s (game time) after the launch (press Play / pause again to go on);
//  * OpenZ/V1.1/KE Can Toss Sounds (Play): the can toss's contacts and the sounds started (after the LOSE ending's toss).
// WRITTEN BY READING ONLY: compiled off-line with the project's code, never run in the engine. Remove this line once it runs.
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    public static class OzKEShuttleCheck
    {
        static float launchSeen = -1f, reportAt = -1f; static int pauseStep; static bool pauses;

        [MenuItem("OpenZ/V1.1/KE Throw The Shuttle (Play)")]
        static void Throw() => Run(false);

        [MenuItem("OpenZ/V1.1/KE Throw The Shuttle + Pauses For Pictures (Play)")]
        static void ThrowPictures() => Run(true);

        [MenuItem("OpenZ/V1.1/KE Can Toss Sounds (Play)")]
        static void CanSounds() => Debug.Log("[KE] " + OzL16LoungerSet.TossSoundStatus());

        static void Run(bool withPauses)
        {
            if (!Application.isPlaying) { Debug.LogWarning("[KE] enter Play on level 16 first"); return; }
            Debug.Log("[KE] " + OzShuttleBlastThrow.ThrowNow());
            pauses = withPauses; pauseStep = 0; launchSeen = -1f; reportAt = -1f;
            EditorApplication.update -= Watch; EditorApplication.update += Watch;
        }

        static void Watch()
        {
            if (!Application.isPlaying) { EditorApplication.update -= Watch; return; }
            if (launchSeen < 0f)
            {
                if (OzShuttleBlastThrow.LastLaunchAt > 0f && Time.time >= OzShuttleBlastThrow.LastLaunchAt) { launchSeen = OzShuttleBlastThrow.LastLaunchAt; reportAt = launchSeen + 4f; Debug.Log("[KE] launched: " + OzShuttleBlastThrow.Status); }
                return;
            }
            if (pauses && pauseStep < 2)
            {
                float at = launchSeen + (pauseStep == 0 ? 0.5f : 1.5f);
                if (Time.time >= at) { pauseStep++; EditorApplication.isPaused = true; Debug.Log($"[KE] paused {Time.time - launchSeen:F2} s after the launch: take the picture, then go on"); }
            }
            if (Time.time < reportAt) return;
            EditorApplication.update -= Watch;
            Debug.Log("[KE] " + OzShuttleBlastThrow.Report());
            Line("thrown on the wave's front (0.0 to 0.6 s after the blast)", OzShuttleBlastThrow.Thrown > 0 && OzShuttleBlastThrow.LastReachAfter >= 0f && OzShuttleBlastThrow.LastReachAfter <= 0.6f, $"{OzShuttleBlastThrow.LastReachAfter:F2} s");
            Line("lifted and tumbling (roll 60 to 200 deg, apex >= 0.8 m, travel >= 5 m)", OzShuttleBlastThrow.LastRoll >= 60f && OzShuttleBlastThrow.LastApex >= 0.8f && OzShuttleBlastThrow.LastTravel >= 4.99f,
                 $"roll {OzShuttleBlastThrow.LastRoll:F0} deg, apex {OzShuttleBlastThrow.LastApex:F2} m, travel {OzShuttleBlastThrow.LastTravel:F1} m");
            Line("rests stably on its outline (room >= 0.3 m)", OzShuttleBlastThrow.LastRollMargin >= 0.3f, $"{OzShuttleBlastThrow.LastRollMargin:F2} m");
            Line("never in the ground in flight (lowest point >= -0.05 m)", OzShuttleBlastThrow.LastLowestInFlight >= -0.05f, $"{OzShuttleBlastThrow.LastLowestInFlight:F2} m");
            Line("landed hard (heavy landing), lies on the ground (gap -0.08 to +0.03 m)", OzShuttleBlastThrow.Landed > 0 && OzHeavyLanding.LastGap > -0.08f && OzHeavyLanding.LastGap < 0.03f, $"gap {OzHeavyLanding.LastGap:F3} m, fit {OzHeavyLanding.LastFit}");
            Line("clear of the hero camp and the endings' set / camera", OzShuttleBlastThrow.LastClearWay, $"camp room {OzShuttleBlastThrow.LastCampRoom:F1} m, set room {OzShuttleBlastThrow.LastSceneRoom:F1} m");
            Line("every action heard (sounds started >= 7)", OzShuttleBlastThrow.SoundsStarted >= 7, $"{OzShuttleBlastThrow.SoundsStarted} of {OzShuttleBlastThrow.SoundsSent}: {OzShuttleBlastThrow.LastSounds}");
        }

        static void Line(string what, bool ok, string detail) => Debug.Log($"[KE] {(ok ? "PASS" : "FAIL")} {what}: {detail}");
    }
}
