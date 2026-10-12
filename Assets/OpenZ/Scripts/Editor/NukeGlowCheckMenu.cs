using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>nuke_fx_v3: menus for the Play-mode nuke glow gate (NukeGlowCheck), the fort shockwave and the heavy shells.</summary>
    public static class NukeGlowCheckMenu
    {
        [MenuItem("OpenZ/Checks/Nuke Glow (Play, day + night)")]
        static void Run()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[OpenZ] Nuke Glow check: enter Play mode in L02 or L17 first"); return; }
            Debug.Log("[OpenZ] " + NukeGlowCheck.Run());
        }

        [MenuItem("OpenZ/Checks/Nuke Glow (Play, real strike - ends the match)")]
        static void RunReal()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[OpenZ] Nuke Glow check: enter Play mode in L02 or L17 first"); return; }
            Debug.Log("[OpenZ] " + NukeGlowCheck.Run(true, true, true));
        }

        [MenuItem("OpenZ/Checks/Heavy Shell Test - Fort Gun (Play)")]
        static void ShellFort()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[OpenZ] Heavy shell test: enter Play mode first"); return; }
            Debug.Log("[OpenZ] " + OpenZHeavyShells.Test(true));
            OpenZHeavyShells.Status();
        }

        [MenuItem("OpenZ/Checks/Heavy Shell Old Body On-Off (test only)")]
        static void ShellOldBody()
        {
            OpenZHeavyShells.Modelled = !OpenZHeavyShells.Modelled;
            Debug.Log("[OpenZ] heavy shells: " + (OpenZHeavyShells.Modelled ? "modelled bodies" : "old baked body (comparison only)"));
        }

        [MenuItem("OpenZ/Checks/Heavy Shell Test (Play)")]
        static void Shell()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[OpenZ] Heavy shell test: enter Play mode first"); return; }
            Debug.Log("[OpenZ] " + OpenZHeavyShells.Test(false));
        }

        /// <summary>A light tank shell and a heavy tank shell side by side, 28 m in 2.4 real seconds across the camera focus,
        /// with their drawn sizes in the Console (FX_Shell is 0.45 m long, 0.19 m wide at scale 1).</summary>
        [MenuItem("OpenZ/Checks/Tank Shell Size Pair (Play)")]
        static void TankShellPair()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[OpenZ] Tank shell pair: enter Play mode first"); return; }
            var r = Object.FindFirstObjectByType<RtsCamera>(); var g = ZGame.I; var z = OpenZFxPlayer.I;
            if (!r || g == null || !z) { Debug.LogWarning("[OpenZ] Tank shell pair: no camera / game / fx player"); return; }
            for (int i = 0; i < 2; i++)
            {
                var w = i == 0 ? FxWeapon.TankShell : FxWeapon.HeavyShell;
                var from = r.focus + new Vector3(-14f, 0f, i * 3f - 1.5f); from.y = g.GroundY(from.x, from.z) + 1.6f;
                var to = r.focus + new Vector3(14f, 0f, i * 3f - 1.5f); to.y = g.GroundY(to.x, to.z) + 0.3f;
                z.Launch(from, to, 2.4f * g.gameSpeed, w);
                var spec = OpenZFxCatalog.ProjectileOf(w);
                float thick = spec.bodyThick > 1.001f ? spec.bodyThick : 1f;
                Debug.Log($"[OpenZ] {w}: body {0.45f * spec.bodyScale:F3} m long x {0.19f * spec.bodyScale * thick:F3} m wide (scale {spec.bodyScale:F3}, thick {thick:F2}), halo {spec.haloPx:F2} px");
            }
        }

        [MenuItem("OpenZ/Checks/Fort Shockwave Status (Play)")]
        static void Wave()
        {
            if (!EditorApplication.isPlaying) return;
            OpenZFortShockwave.Status();
        }
    }
}
