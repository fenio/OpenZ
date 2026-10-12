using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// the old FxManager.Flame, and a status line (jets, sprites, burning things, ground patches, lights, loops).</summary>
    public static class OpenZPyroFireMenu
    {
        const string Root = "OpenZ/FX/Pyro Fire v2/";

        [MenuItem(Root + "Stage Fire Show (Play mode)")]
        static void Stage()
        {
            if (!Application.isPlaying) { Debug.LogWarning("pyro_fire_v2: enter Play mode on L02 or L17 first"); return; }
            var s = OpenZ.OpenZPyroFireShow.Begin();
            Debug.Log(s ? $"pyro_fire_v2: staged; report in ~{OpenZ.OpenZPyroFireShow.Duration:F0} s -> work/records/qc/pyro_fire_v2_show_*.md" : "pyro_fire_v2: no running ZGame");
        }

        [MenuItem(Root + "Toggle v2 fire (A-B, Play mode)")]
        static void Toggle()
        {
            OpenZ.OpenZPyroFire.Enabled = !OpenZ.OpenZPyroFire.Enabled;
            if (!OpenZ.OpenZPyroFire.Enabled && OpenZ.OpenZPyroFire.I) OpenZ.OpenZPyroFire.I.ClearAll();
            Debug.Log($"pyro_fire_v2 {(OpenZ.OpenZPyroFire.Enabled ? "ON (the legacy M12 flamethrower)" : "OFF (the old FxManager.Flame)")}");
        }

        [MenuItem(Root + "Toggle v2 fire (A-B, Play mode)", true)]
        static bool ToggleCheck() { Menu.SetChecked(Root + "Toggle v2 fire (A-B, Play mode)", OpenZ.OpenZPyroFire.Enabled); return true; }

        [MenuItem(Root + "Status (Play mode)")]
        static void Status()
        {
            var p = OpenZ.OpenZPyroFire.I;
            Debug.Log(p ? "[OpenZPyroFire] " + p.Status + " | " + p.Diagnostics() : "[OpenZPyroFire] not running (it starts when a game level loads; OpenZPyroFire.AutoCreate off = never)");
        }
    }
}
