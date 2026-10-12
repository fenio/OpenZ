using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// boost (VX53). Everything here is view only: the test crane job never tells the sim anything.
    /// </summary>
    public static class OpenZBatchCMenu
    {
        const string Root = "OpenZ/Batch C/";

        [MenuItem(Root + "Report (crane arm, zaps, recoil)", false, 1)]
        static void Report()
        {
            Debug.Log("[OpenZ] Batch C report\n" + CraneArm.Report() + ElectricZaps.Report() + RecoilFeel.Report());
        }

        static ZObject testCrane, testBuilding;

        [MenuItem(Root + "Crane: test job on the nearest building (view only)", false, 20)]
        static void CraneTest()
        {
            var g = ZGame.I; if (!Application.isPlaying || g == null || g.World == null) { Debug.LogWarning("[OpenZ] Batch C: enter Play first"); return; }
            ZObject crane = null;
            foreach (var kv in g.Views) { var o = kv.Value ? kv.Value.Obj : null; if (o != null && o.Type == "crane" && !o.Destroyed && o.Kind == ZKind.Vehicle) { crane = o; if (o.Owner == g.player) break; } }
            if (crane == null) { Debug.LogWarning("[OpenZ] Batch C: no live crane (build one at the vehicle factory or spawn one with the cheat spawner)"); return; }
            var cp = g.CenterWorld(crane); ZObject best = null; float bd = float.MaxValue;
            foreach (var o in g.World.Objects)
            {
                if (o == null || o.Kind != ZKind.Building || o.IsBridge || o.MaxHealth <= 0) continue;
                float d = (g.CenterWorld(o) - cp).sqrMagnitude; if (d < bd) { bd = d; best = o; }
            }
            if (best == null) { Debug.LogWarning("[OpenZ] Batch C: no building"); return; }
            if (testBuilding != null) CraneArm.Job(g, testCrane, testBuilding, false);
            testCrane = crane; testBuilding = best;
            CraneArm.Job(g, crane, best, true);
            Debug.Log($"[OpenZ] Batch C: crane#{crane.Id} works on {best.Type}#{best.Id} ({Mathf.Sqrt(bd):F1} m away); 'Crane: end the test job' ends it\n" + CraneArm.Report());
        }

        [MenuItem(Root + "Crane: end the test job", false, 21)]
        static void CraneEnd()
        {
            var g = ZGame.I; if (g == null || testBuilding == null) return;
            CraneArm.Job(g, testCrane, testBuilding, false); testBuilding = null; testCrane = null;
            Debug.Log("[OpenZ] Batch C: test job ended\n" + CraneArm.Report());
        }

        [MenuItem(Root + "Zaps: show one now (nearest to the view centre)", false, 40)]
        static void ZapNow()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] Batch C: enter Play first"); return; }
            Debug.Log("[OpenZ] Batch C: " + ElectricZaps.ShowOne());
        }

        [MenuItem(Root + "Recoil: kick every unit on screen (view only)", false, 60)]
        static void KickAll()
        {
            var g = ZGame.I; var cam = Camera.main; if (!Application.isPlaying || g == null || !cam) { Debug.LogWarning("[OpenZ] Batch C: enter Play first"); return; }
            int n = 0;
            foreach (var kv in g.Views)
            {
                var v = kv.Value; if (!v || v.Obj == null || !v.gameObject.activeInHierarchy) continue;
                var p = cam.WorldToViewportPoint(v.transform.position);
                if (p.z <= 0f || p.x < 0f || p.x > 1f || p.y < 0f || p.y > 1f) continue;
                v.DebugKick(); n++;
            }
            Debug.Log($"[OpenZ] Batch C: kicked {n} units\n" + RecoilFeel.Report());
        }

        // ---- Options switches (never a silent off: the menu shows the state, the report prints it)
        const string OptArm = Root + "Options/Crane arm animation", OptZap = Root + "Options/Electric zaps", OptRecoil = Root + "Options/Recoil boost";
        [MenuItem(OptArm, false, 80)] static void ToggleArm() { CraneArm.Enabled = !CraneArm.Enabled; Debug.Log("[OpenZ] crane arm " + (CraneArm.Enabled ? "ON" : "OFF (RulesView sparks)")); }
        [MenuItem(OptArm, true)] static bool ToggleArmV() { Menu.SetChecked(OptArm, CraneArm.Enabled); return true; }
        [MenuItem(OptZap, false, 81)] static void ToggleZap() { ElectricZaps.Enabled = !ElectricZaps.Enabled; Debug.Log("[OpenZ] electric zaps " + (ElectricZaps.Enabled ? "ON" : "OFF")); }
        [MenuItem(OptZap, true)] static bool ToggleZapV() { Menu.SetChecked(OptZap, ElectricZaps.Enabled); return true; }
        [MenuItem(OptRecoil, false, 82)] static void ToggleRecoil() { RecoilFeel.Enabled = !RecoilFeel.Enabled; Debug.Log("[OpenZ] recoil boost " + (RecoilFeel.Enabled ? "ON" : "OFF (authored kicks)")); }
        [MenuItem(OptRecoil, true)] static bool ToggleRecoilV() { Menu.SetChecked(OptRecoil, RecoilFeel.Enabled); return true; }
    }
}
