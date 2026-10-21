using System.IO;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// The blind sectors of the guns (View/AttackRangeRings.BlindSectors.cs, View/OzBlindSectorsCore.cs): the audit (every gun of the
    /// running level, every cell inside its range: the rules against the drawn sectors), the proof pictures (View/OzBlindSectorsProof.cs)
    /// and the on / off switch for an A / B look. Play mode only.
    /// </summary>
    public static class OzBlindSectorsMenu
    {
        [MenuItem("OpenZ/V1.1/Blind Sectors: Audit (K333, Play mode)")]
        static void Audit()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] blind sectors audit: Play mode only (start a level from the main menu)"); return; }
            string s = AttackRangeRings.AuditBlind(out int v);
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/milestones/k333_blind_sectors"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "audit.txt"), s);
            if (v == 0) Debug.Log("[OpenZ] blind sectors audit: PASS\n" + s); else Debug.LogError("[OpenZ] blind sectors audit: FAIL (" + v + " cells differ)\n" + s);
        }

        [MenuItem("OpenZ/V1.1/Blind Sectors: Proof Pictures (K333, Play mode, Level 16)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] blind sectors proof: Play mode only (start Level 16 from the main menu)"); return; }
            OzBlindSectorsProof.Run();
        }

        [MenuItem("OpenZ/V1.1/Blind Sectors: On Or Off (K333, Play mode)")]
        static void Toggle()
        {
            var rr = AttackRangeRings.I;
            if (!rr) { Debug.LogWarning("[OpenZ] blind sectors: no AttackRangeRings in the scene (Play mode)"); return; }
            rr.blindSectors = !rr.blindSectors;
            Debug.Log("[OpenZ] blind sectors: " + (rr.blindSectors ? "ON" : "off"));
        }
    }
}
