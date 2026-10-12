using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>legs_gate_v2: menus for LegGateTypes (the legs gate on every robot type, the RP1 grunt face close-ups, the 22 m eye frames) and the
    /// </summary>
    public static class OpenZLegGateTypes
    {
        [MenuItem("OpenZ/Checks/Legs gate: all robot types (Play, ~25 s)")]
        public static void Run()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[LegGateTypes] enter Play on L02 or L17 first (test mode, 60 fps cap, level-start briefing closed)"); return; }
            OpenZ.LegGateTypes.Begin();
        }

        [MenuItem("OpenZ/Checks/Legs gate: grunt face close-ups day + night (Play, ~8 s)")]
        public static void Faces()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[LegGateTypes] enter Play on L17 first (level-start briefing closed)"); return; }
            OpenZ.LegGateTypes.BeginFaces();
        }

        [MenuItem("OpenZ/Checks/Legs gate: 22 m eye frames walk + stand (Play, ~15 s)")]
        public static void Eyes()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[LegGateTypes] enter Play on L02 first (test mode, level-start briefing closed)"); return; }
            OpenZ.LegGateTypes.BeginEyes();
        }

        [MenuItem("OpenZ/Checks/Legs gate: phase windows per type (EditMode)")]
        public static void PhaseGate() => Debug.Log("[LegPhaseGate] " + OpenZLegPhaseGate.Run());
    }
}
