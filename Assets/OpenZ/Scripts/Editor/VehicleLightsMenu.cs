using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>vehicle_lights package: play-mode helpers (menu OpenZ/Vehicle Lights). The clock jumps live in OpenZ/Night.</summary>
    public static class VehicleLightsMenu
    {
        [MenuItem("OpenZ/Vehicle Lights/Log Vehicle Lights Status", false, 60)]
        static void LogStatus()
        {
            if (!Application.isPlaying || !VehicleLights.I) { Debug.Log("[OpenZ] VehicleLights: enter Play in a level scene first"); return; }
            Debug.Log(VehicleLights.I.Status());
        }

        [MenuItem("OpenZ/Vehicle Lights/Fire FX Stage Test At Camera Focus (projectile glow)", false, 62)]
        static void StageTest()
        {
            var fx = OpenZFxPlayer.I; var rts = Object.FindFirstObjectByType<RtsCamera>();
            if (!Application.isPlaying || !fx || !rts) { Debug.Log("[OpenZ] VehicleLights: enter Play in a level scene first"); return; }
            fx.RunStageTest(rts.focus);                                        // shells, rockets, MLRS salvo, grenade, rifle / MG shots
        }

        [MenuItem("OpenZ/Vehicle Lights/Toggle Projectile Glow By Day (test only)", false, 61)]
        static void DayGlow()
        {
            var g = NightProjectileGlow.I;
            if (!Application.isPlaying || !g) { Debug.Log("[OpenZ] VehicleLights: enter Play in a level scene first"); return; }
            g.dayShare = g.dayShare > 0.5f ? 0f : 1f;
            Debug.Log($"[OpenZ] VehicleLights: projectile glow by day {(g.dayShare > 0.5f ? "ON (test only, switch off before a take)" : "off")}");
        }
    }
}
