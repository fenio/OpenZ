using System.IO;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Sound wiring round (27 Sep 2026): the fire-every-event coverage test. In Play mode (a level, so CombatAudio and the sim are
    /// there) it fires EVERY event of Resources/OpenZ/Audio/openz_sound_events.json once through its real playback path
    /// events it owns, LegacyEngineAudio's loop clip), checks that a source really started with a non-zero volume and writes
    /// </summary>
    public static class OpenZAudioCoverageTest
    {
        const string Menu = "OpenZ/Audio/Coverage Test: Fire Every Event";

        [MenuItem(Menu)]
        static void Run()
        {
            var ad = AudioDirector.I;
            if (!EditorApplication.isPlaying || !ad) { Debug.LogWarning("[OpenZ] sound coverage test: enter Play mode in a level first"); return; }
            var path = Path.Combine(OpenZPaths.Records, "sound_fire_every_event.md");
            ad.StartCoroutine(ad.FireEveryEvent(path));
            Debug.Log("[OpenZ] sound coverage test running (every event once, ~0.15 s each) -> " + path);
        }

        [MenuItem(Menu, true)]
        static bool CanRun() => EditorApplication.isPlaying && AudioDirector.I != null;
    }
}
