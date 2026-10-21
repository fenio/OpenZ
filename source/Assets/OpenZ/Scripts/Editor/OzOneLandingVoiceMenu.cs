#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K407 menu (one landing voice per shuttle landing, View/OzPlayableShuttle.LandingVoice.cs): the Play proof
    /// View/OzOneLandingVoiceProof.cs on level 16 (six landings, the fourth a crash: exactly one landing voice each, both voices used, no run
    /// longer than 2). Its output folder: OzOneLandingVoiceProof.Folder() (built at run time from the project's place, nothing named here).</summary>
    public static class OzOneLandingVoiceMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K407 One Landing Voice Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K407 one landing voice proof: start level 16 in Play first"); return; }
            OzOneLandingVoiceProof.Run();
            Debug.Log("[OpenZ] K407 one landing voice proof started; its report: OzOneLandingVoiceProof.Report, the log and the sound rows in " + OzOneLandingVoiceProof.Folder());
        }
    }
}
#endif
