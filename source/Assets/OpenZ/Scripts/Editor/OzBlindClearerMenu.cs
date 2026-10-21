using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// The clearer blind sectors (View/AttackRangeRings.BlindClearer.cs: the grey over the blind ground, grey faint dots over the blind arcs):
    /// the proof pictures with their measured lines (View/OzBlindClearerProof.cs; Level 16: three, Level 2: four). The audit of the cells stays
    /// the blind sectors' own menu (OpenZ/V1.1/Blind Sectors: Audit). Play mode only.
    /// </summary>
    public static class OzBlindClearerMenu
    {
        [MenuItem("OpenZ/V1.1/Blind Sectors: Clearer Grey Pictures (K333c, Play mode, Level 16 or 2)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] clearer blind sectors proof: Play mode only (start Level 16 or Level 2 from the main menu)"); return; }
            OzBlindClearerProof.Run();
        }
    }
}
