using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// NightV2 status, and an edit-mode audit of every OpenZ/TeamLit material's night class.</summary>
    public static class NightV2Menu
    {
        [MenuItem("OpenZ/Night/Night v2 Luminance Check (Play)")]
        static void Check()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] Night v2 luminance check: enter Play mode first (night: F9 to 0.62)"); return; }
            NightLumaCheck.Run();
        }

        [MenuItem("OpenZ/Night/Night v2 Luminance Check, night only (Play)")]
        static void CheckNightOnly()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] Night v2 luminance check: enter Play mode first"); return; }
            NightLumaCheck.Run(false);
        }

        [MenuItem("OpenZ/Night/Night v2 Status")]
        static void Status() => Debug.Log(NightV2.StatusText());

        [MenuItem("OpenZ/Night/Night v2 Material Classes")]
        static void Classes()
        {
            var sb = new System.Text.StringBuilder("[OpenZ] Night v2 classes of the OpenZ/TeamLit materials (-1 in the file = auto)\n");
            int[] count = new int[4];
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/OpenZ" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                int c = NightV2.ClassOf(m); if (c < 0) continue;
                count[c]++;
                float raw = m.HasProperty("_NightClass") ? m.GetFloat("_NightClass") : float.NaN;
                sb.Append($"  {NightV2.ClassNames[c],-12} {(raw > -0.5f ? "explicit" : "auto    ")}  {path}\n");
            }
            sb.Append($"  total: {count[0]} environment, {count[1]} unit, {count[2]} building, {count[3]} map item");
            Debug.Log(sb.ToString());
        }
    }
}
