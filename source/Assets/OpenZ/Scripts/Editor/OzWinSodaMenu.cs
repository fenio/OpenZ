#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K439 menus (the win line's rocket fuel can and the boys' giggles, View/Zod/ZodBriefing.WinSoda.cs): the counted check out of
    /// Play (Editor/OzWinSodaCount.cs, the real clips and the line's sheet) and the Play proof View/OzWinSodaProof.cs on any level (its output
    /// folder: OzWinSodaProof.Folder(), built at run time from the project's place, nothing named here).</summary>
    public static class OzWinSodaMenu
    {
        [MenuItem("OpenZ/V1.1/Zod: K439 Win Can + Giggles Check (out of Play)")]
        static void CheckMenu()
        {
            if (Application.isPlaying) { Debug.LogWarning("[OpenZ V1.1] K439 win can + giggles check: leave Play mode first"); return; }
            Debug.Log("[OpenZ V1.1] K439 win can + giggles check\n" + OzWinSodaCount.Check());
        }

        [MenuItem("OpenZ/V1.1/Zod: K439 Win Can + Giggles Proof (any level, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K439 win can + giggles proof: start a level in Play first"); return; }
            OzWinSodaProof.Run();
            Debug.Log("[OpenZ] K439 win can + giggles proof started; its report: OzWinSodaProof.Report, the log and the recording in " + OzWinSodaProof.Folder());
        }
    }
}
#endif
