using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// The dashed side lines only on real blind sectors (View/OzBlindSectorsCore.RealSectors.cs): the proof pictures with their counted lines
    /// (View/OzNoLinesProof.cs; Level 16: six, Level 2: two). The audit of the cells stays the blind sectors' own menu. Play mode only.
    /// </summary>
    public static class OzNoLinesMenu
    {
        [MenuItem("OpenZ/V1.1/Blind Sectors: Lines Only On Real Sectors (K427, Play mode, Level 16 or 2)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] lines only on real sectors proof: Play mode only (start Level 16 or Level 2 from the main menu)"); return; }
            OzNoLinesProof.Run();
        }
    }
}
