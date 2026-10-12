using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary></summary>
    public static class OpenZWeaponsV2Menu
    {
        [MenuItem("OpenZ/Weapons v2/Stage Weapon Show (Play mode)")]
        static void Stage()
        {
            if (!Application.isPlaying) { Debug.LogWarning("weapons_v2: enter Play mode on L02 or L17 first"); return; }
            var c = OpenZ.WeaponsV2Check.Begin();
            Debug.Log(c ? $"weapons_v2: staged; report in {OpenZ.WeaponsV2Check.Duration:F0} s -> work/records/weapons_v2_check.md" : "weapons_v2: no running ZGame");
        }

        [MenuItem("OpenZ/Weapons v2/Toggle weapons_v2 (A-B, Play mode)")]
        static void Toggle()
        {
            OpenZ.WeaponsV2.Enabled = !OpenZ.WeaponsV2.Enabled;
            Debug.Log($"weapons_v2 {(OpenZ.WeaponsV2.Enabled ? "ON" : "OFF (the pre-weapons_v2 look)")}");
        }

        [MenuItem("OpenZ/Weapons v2/Toggle weapons_v2 (A-B, Play mode)", true)]
        static bool ToggleCheck() { Menu.SetChecked("OpenZ/Weapons v2/Toggle weapons_v2 (A-B, Play mode)", OpenZ.WeaponsV2.Enabled); return true; }
    }
}
