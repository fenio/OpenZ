using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// K327 menus (the repair key as the build menu's SET RALLY shortcut):
    ///  - R Key Matrix (no Play): the counted rule matrix (Editor/OzRallyKeyMatrix.cs), ends "[K327 R key] MATRIX: N checks, 0 violations".
    ///  - R Key Play Proof (Level 16 or 17, Play): the real game through the real input path (View/OzRallyKeyProof.cs), ends
    ///    "[K327 R key] PLAY PROOF ...: N checks, 0 FAIL".
    ///  - CONTROLS Layout, screens on screen now (Play): the tables shown now, "[K327 controls] ... overlaps 0, overflow 0, cut 0, shrunk 0".
    ///  - CONTROLS Layout, open every screen + pictures (Play): the main menu's CONTROLS (menu scene), or the pause menu's CONTROLS and
    ///    the F1 list at UI 100 % and 160 % (a match), each counted and pictured.
    /// </summary>
    public static class OzRallyKeyMenu
    {
        [MenuItem("OpenZ/V1.1/Controls: K327 R Key Matrix (no Play)")]
        public static void Matrix()
        {
            string report = OzRallyKeyMatrix.Run(out int n, out int bad);
            if (bad == 0) Debug.Log(report); else Debug.LogError(report);
        }

        [MenuItem("OpenZ/V1.1/Controls: K327 R Key Play Proof (Level 16 or 17, Play)")]
        public static void PlayProof()
        {
            if (!Application.isPlaying) { Debug.LogWarning(OzRallyKeyProof.Tag + "PLAY PROOF: enter Play mode on Level 16 or 17 and wait until the match runs"); return; }
            OzRallyKeyProof.RunProof();
        }

        [MenuItem("OpenZ/V1.1/Controls: K327 CONTROLS Layout, screens on screen now (Play)")]
        public static void LayoutNow()
        {
            if (!Application.isPlaying) { Debug.LogWarning(OzRallyKeyProof.LayoutTag + "enter Play mode and open a CONTROLS screen first"); return; }
            int bad = OzRallyKeyProof.Layout("on screen", new System.Text.StringBuilder());
            if (bad < 0) Debug.LogWarning(OzRallyKeyProof.LayoutTag + "no controls table on screen: CONTROLS in the main menu, Esc then CONTROLS, or F1 in a match");
        }

        [MenuItem("OpenZ/V1.1/Controls: K327 CONTROLS Layout, open every screen + pictures (Play)")]
        public static void LayoutAll()
        {
            if (!Application.isPlaying) { Debug.LogWarning(OzRallyKeyProof.LayoutTag + "enter Play mode (the main menu scene, or a running match)"); return; }
            OzRallyKeyProof.RunLayoutAll();
        }
    }
}
