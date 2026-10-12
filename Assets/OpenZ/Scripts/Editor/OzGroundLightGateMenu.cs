using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>ground_lights_fix: OpenZ/QC/Ground Light Gate (Play mode, a level scene; jumps to night when needed).</summary>
    public static class OzGroundLightGateMenu
    {
        [MenuItem("OpenZ/QC/Ground Light Gate")]
        static void Run()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] Ground light gate: enter Play mode in L02_Desert or L17_CarPark first"); return; }
            OzGroundLightGate.Run();
        }
    }
}
