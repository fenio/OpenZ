using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// The blind ground seen from the gun (View/AttackRangeRings.BlindFromGun.cs, View/OzBlindSectorsCore.FromGun.cs): the seven proof
    /// pictures (View/OzBlindFromGunProof.cs; Level 16: three, Level 2: four). The audit of the cells stays the blind sectors' own menu
    /// (OpenZ/V1.1/Blind Sectors: Audit). Play mode only.
    /// </summary>
    public static class OzBlindFromGunMenu
    {
        [MenuItem("OpenZ/V1.1/Blind Sectors: From The Gun Pictures (K333b, Play mode, Level 16 or 2)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] blind ground from the gun proof: Play mode only (start Level 16 or Level 2 from the main menu)"); return; }
            OzBlindFromGunProof.Run();
        }
    }
}
