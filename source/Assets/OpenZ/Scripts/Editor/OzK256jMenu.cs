#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// The shuttle round-3 faults' Play proofs, one menu per fault (level 16 in Play; each runs the proof stage that found the fault,
    /// with its own PASS limit):
    /// <list type="bullet">
    /// <item>M6 the boarding file: Rules Proof R1 (fresh level 16 start): the closest pair of figures at the ramp foot >= 0.8 m.</item>
    /// <item>M11 the climb: Flight Look Proof M11: every crossing climbs at most 4.05 m/s (fort roof, cliff, kapok), gap >= 1.0 m, the
    /// hover bob 0.55-0.75 m and its 3 s strip >= 15 px up and down.</item>
    /// <item>M14 the landing lamps: Night Lights Proof N7: both lamps within 10 degrees of their points beside the landing point while it
    /// comes down (pads over 2.5 m), their spots >= 3 m apart.</item>
    /// <item>M15 the hull hit: Env Proof E (whole-take sound table): shuttle_hull_hit_energy played, its quietest play >= 0.12.</item>
    /// <item>M16 the fort's gun: Break-up Proof G (G19: >= 3 shells, >= 1 on the hull, a shell in flight) and K (K3: the fort's gun
    /// is the killing hit).</item>
    /// <item>S6 the camp: Flight Look Proof S6 (right after the level start): the heroes stay on their loungers (got up 0).</item>
    /// </list>
    /// </summary>
    public static class OzK256jMenu
    {
        const string P = "OpenZ/V1.1/Shuttle: K256j ";

        [MenuItem(P + "M6 boarding file (Rules Proof, fresh Level 16 start, Play)")]
        static void M6() { if (Go("M6")) { OzShuttleRulesProof.Run(); Tell("M6", "R1 closest pair at the ramp foot >= 0.8 m", OzShuttleRulesProof.Folder()); } }

        [MenuItem(P + "M11 cliff climb (Flight Look Proof M11, Level 16, Play)")]
        static void M11() { if (Go("M11")) { OzShuttleFlightProof.OutDir = ""; OzShuttleFlightProof.Run("M11"); Tell("M11", "every crossing's fastest climb <= 4.05 m/s, gap >= 1.0 m; hover bob 0.55-0.75 m", OzShuttleFlightProof.Folder()); } }

        [MenuItem(P + "M14 landing lamps aim (Night Lights Proof, Level 16, Play)")]
        static void M14() { if (Go("M14")) { OzShuttleNightProof.Run(); Tell("M14", "N7 the lamps within 10 deg of the landing points (pads over 2.5 m), spots >= 3 m apart", OzShuttleNightProof.Folder()); } }

        [MenuItem(P + "M15 hull hit sound (Env Proof E, Level 16, Play)")]
        static void M15() { if (Go("M15")) { OzShuttleEnvProof.Run("E"); Tell("M15", "shuttle_hull_hit_energy played, quietest play >= 0.12 (sound_take_E_table.txt)", "the Env proof's folder"); } }

        [MenuItem(P + "M16 fort gun at the hovering ship (Break-up Proof G, Level 16, Play)")]
        static void M16G() { if (Go("M16")) { OzShuttleBreakupGallery.Run("G"); Tell("M16 G19", ">= 3 shells, >= 1 on the hull, a shell seen in flight; 'the match goes on' line when the enemy had no unit left", OzShuttleBreakupGallery.Folder()); } }

        [MenuItem(P + "M16 fort gun kill (Break-up Proof K, fresh Level 16 starts, Play)")]
        static void M16K() { if (Go("M16")) { OzShuttleBreakupGallery.Run("K"); Tell("M16 K3", "K3 a real fort-gun kill (the killing hit is the fort's gun), shells > 0", OzShuttleBreakupGallery.Folder()); } }

        [MenuItem(P + "S6 heroes stay on their loungers (Flight Look Proof S6, right after the level start, Play)")]
        static void S6() { if (Go("S6")) { OzShuttleFlightProof.OutDir = ""; OzShuttleFlightProof.Run("S6"); Tell("S6", "got up 0 while a squad boards, the ship flies over and hovers over the camp", OzShuttleFlightProof.Folder()); } }

        static bool Go(string tag)
        {
            if (Application.isPlaying) return true;
            Debug.LogWarning("[OpenZ] K256j " + tag + ": enter Play on level 16 first");
            return false;
        }

        static void Tell(string tag, string pass, string folder) => Debug.Log("[OpenZ] K256j " + tag + " proof started; PASS: " + pass + "; the logs in " + folder);
    }
}
#endif
