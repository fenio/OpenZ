using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>Version 1.1 item 360 (rule DV124): the Play proof of the big fallers that fell the trees they come down on
    /// (View/OzTreeCrushProof.cs). Level 16 in Play (the turret also on level 2); pictures and the report into
    /// </summary>
    public static class OzTreeCrushMenu
    {
        const string M = "OpenZ/V1.1/Trees: K360 ";

        static bool Ready()
        {
            if (Application.isPlaying && ZGame.I && ZGame.I.World != null) return true;
            Debug.LogWarning("[OpenZ K360] enter Play on level 16 first (the turret check also runs on level 2)");
            return false;
        }

        [MenuItem(M + "All Three (Play, level 16)")]
        static void All() { if (Ready()) OzTreeCrushProof.Run("TSG"); }

        [MenuItem(M + "Tank Turret Onto A Tree (Play)")]
        static void Turret() { if (Ready()) OzTreeCrushProof.Run("T"); }

        [MenuItem(M + "Shuttle Shot Down Over Trees (Play, level 16)")]
        static void Shuttle() { if (Ready()) OzTreeCrushProof.Run("S"); }

        [MenuItem(M + "Fort Cannon Toss Onto A Tree (Play, level 16)")]
        static void Gun() { if (Ready()) OzTreeCrushProof.Run("G"); }

        [MenuItem(M + "State")]
        static void State()
        {
            var w = ZGame.I ? ZGame.I.World : null;
            Debug.Log($"[OpenZ K360] rule DV124 {(OpenZ.Sim.ZWorld.HeavyFallDeviation ? "ON" : "off")}, view {(OzTreeCrush.Enabled ? "ON" : "off")}; " +
                      (w != null ? $"{w.HeavyTreeCount} trees known, {w.HeavyFalls.Count} big falls, {w.HeavyFallTrees} trees felled, {w.HeavyTurretsByRock} turrets by a rock, {w.HeavyFallGunLeftToView} cannon tosses left to the view; " : "") +
                      $"held {OzTreeCrush.Held}, by touch {OzTreeCrush.ReleasedByTouch}, by landing {OzTreeCrush.ReleasedByLanding}, by time-out {OzTreeCrush.ReleasedByTimeout}; last: {OzTreeCrush.LastLog}; proof: {OzTreeCrushProof.Report}");
        }
    }
}
