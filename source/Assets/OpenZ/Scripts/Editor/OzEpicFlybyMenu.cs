// OpenZ (GPL-3.0) - epic flybys: the editor menu. Each item runs one saved scenario (Assets/OpenZ/Scripts/Editor/EpicFlybys/*.json):
// "preview" = 1920 x 1080 (the same fixed steps and nearly the same battle as the big pass, cheap), "4K" = 3840 x 2160 at 60 fps with
// the world sound. Use it out of Play mode: the item enters Play and the take starts by itself when Level 16 is up and its briefing is
// over (every take from a fresh level; in Play mode the item is refused: leave Play first).
// Frames, the sound file and the logs go to the folder of the editor setting oz_epic_flyby_out (else the project's Temp folder);
// an off-project recorder turns them into the clip and its checks. Nothing here runs in normal play.
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    public static class OzEpicFlybyMenu
    {
        const string Root = "OpenZ/Epic Flybys/";
        public const string A = "scenario_a_sunrise", B = "scenario_b_night", C = "scenario_c2_lake_raid", C1 = "scenario_c_island";   // C: the lake raid (kit KF5); the island stays as C1

        [MenuItem(Root + "A Sunrise - preview 1080p", false, 10)] static void PA() => Say(Run(A, "preview"));
        [MenuItem(Root + "A Sunrise - record 4K", false, 11)] static void RA() => Say(Run(A, "4k"));
        [MenuItem(Root + "B Night temples - preview 1080p", false, 30)] static void PB() => Say(Run(B, "preview"));
        [MenuItem(Root + "B Night temples - record 4K", false, 31)] static void RB() => Say(Run(B, "4k"));
        [MenuItem(Root + "C Lake raid - preview 1080p", false, 50)] static void PC() => Say(Run(C, "preview"));
        [MenuItem(Root + "C Lake raid - record 4K", false, 51)] static void RC() => Say(Run(C, "4k"));
        [MenuItem(Root + "C1 Island (old) - preview 1080p", false, 60)] static void PC1() => Say(Run(C1, "preview"));
        [MenuItem(Root + "C1 Island (old) - record 4K", false, 61)] static void RC1() => Say(Run(C1, "4k"));
        [MenuItem(Root + "Status", false, 70)] static void St() => Say(OzEpicFlyby.Status());
        [MenuItem(Root + "Stop the take", false, 71)] static void Ab() => Say(OzEpicFlyby.Abort());
        [MenuItem(Root + "Check the scenario files", false, 72)] static void Ck() => Say(CheckFiles());

        /// <summary>The recorder and the menu: run a scenario by name in "preview" or "4k" (outDir null = the editor setting).</summary>
        public static string Run(string name, string mode, string outDir = null) => OzEpicFlyby.Request(name, mode, outDir);

        /// <summary>Reads the scenario files and says what each holds (or what is wrong).</summary>
        public static string CheckFiles()
        {
            var s = "";
            foreach (var n in new[] { A, B, C, C1 })
            {
                var err = OzEpicFlyby.Load(n, out var sc);
                s += n + ": " + (err ?? (sc.seconds + " s, clock " + sc.time_of_day + ", " + sc.camera.Length + " camera keys, " + sc.units.Length + " squads, " + sc.cues.Length + " cues -> " + sc.output)) + "\n";
            }
            return s.TrimEnd();
        }

        static void Say(string s) => Debug.Log("[OpenZ EpicFlyby] " + s);
    }
}
