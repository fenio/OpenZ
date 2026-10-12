using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// vehicles, cannons) switch on TeamLit's stylize mode: de-grime (blurred-mip blend), black parts lifted to a dark
    /// gunmetal, soft wrap shading, a warm key, more ambient, a bright rim, clean team paint (narrow shade range), a softer
    /// matte-satin finish. Buildings and terrain keep their look.
    /// </summary>
    public static class OpenZStyle
    {
        static readonly string[] UnitPrefabs = { "robot", "tough", "jeep", "light", "medium", "heavy", "apc", "missile_launcher", "crane", "gatling", "gun", "howitzer", "missile_cannon" };

        public static void ApplyUnitStyle(Material m, bool robot)
        {
            if (!m || !m.HasProperty("_Stylize")) return;
            m.SetFloat("_Stylize", 1);
            m.SetFloat("_Degrime", robot ? 0.6f : 0.5f);
            m.SetFloat("_DegrimeLod", robot ? 2.8f : 2.4f);
            m.SetColor("_DarkColor", new Color(0.055f, 0.062f, 0.078f, 1));        // lifts black to a cool dark gunmetal
            m.SetFloat("_DarkThreshold", 0.13f);
            m.SetFloat("_SoftFill", 0.75f);
            m.SetFloat("_AmbientBoost", 1.4f);
            m.SetColor("_KeyWarm", new Color(1.08f, 1.0f, 0.9f, 1));
            m.SetColor("_RimColor", new Color(0.62f, 0.56f, 0.46f, 1));
            m.SetFloat("_RimPower", robot ? 2.8f : 3.4f);
            m.SetFloat("_ShadeMin", 0.95f); m.SetFloat("_ShadeMax", robot ? 1.0f : 1.15f); // clean, even team paint
            m.SetFloat("_Smoothness", Mathf.Min(m.GetFloat("_Smoothness"), 0.42f));
            if (robot)
            {   // robots read as black silhouettes from the game camera: a real gunmetal, clean bone-white joints, stronger rim
                m.SetColor("_DarkColor", new Color(0.095f, 0.105f, 0.125f, 1)); m.SetFloat("_DarkThreshold", 0.2f);
                m.SetFloat("_SatScale", 0.55f); m.SetFloat("_ValScale", 1.18f);
                m.SetColor("_RimColor", new Color(0.58f, 0.53f, 0.45f, 1)); m.SetFloat("_RimPower", 3.4f);   // 0.8 / 2.4 speckled the small facets
                m.SetFloat("_SoftFill", 0.9f); m.SetFloat("_AmbientBoost", 1.55f);
            }
            EditorUtility.SetDirty(m);
        }

        /// <summary>
        /// rocks and cliffs (de-grimed, flat facets, soft warm fill, no rim), soft palette props; warmer post.</summary>
        [MenuItem("OpenZ/Apply Desert Style (reference look)")]
        public static string ApplyEnvStyle()
        {
            var log = new List<string>();
            var ground = AssetDatabase.LoadAssetAtPath<Material>("Assets/OpenZ/Materials/Env/M_L02_Ground.mat");
            if (ground && ground.shader.name != "OpenZ/Sand")
            {
                var tex = ground.GetTexture("_BaseMap"); ground.shader = Shader.Find("OpenZ/Sand"); ground.SetTexture("_BaseMap", tex);
                EditorUtility.SetDirty(ground); log.Add("ground -> OpenZ/Sand");
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/OpenZ/Materials" }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (!m || m.shader.name != "OpenZ/TeamLit") continue;
                bool rock = m.name.StartsWith("DV3_Sandstone"), cliff = m.name.StartsWith("M_Cliff"), palette = m.name.StartsWith("DV3_BatchedPalette");
                if (!rock && !cliff && !palette) continue;
                m.SetFloat("_Stylize", 1); m.SetFloat("_FlatShade", 1);
                m.SetFloat("_Degrime", palette ? 0f : 0.85f); m.SetFloat("_DegrimeLod", 4f);
                m.SetColor("_DarkColor", new Color(0.02f, 0.012f, 0.008f, 1)); m.SetFloat("_DarkThreshold", 0.08f);
                m.SetFloat("_SoftFill", 0.6f); m.SetFloat("_AmbientBoost", 1.25f);
                m.SetColor("_KeyWarm", new Color(1.06f, 1.0f, 0.92f, 1)); m.SetColor("_RimColor", Color.black);
                if (!palette) { m.SetFloat("_SatScale", 1.22f); m.SetFloat("_ValScale", 1.04f); }
                m.SetFloat("_Smoothness", Mathf.Min(m.GetFloat("_Smoothness"), 0.15f));
                EditorUtility.SetDirty(m); log.Add(m.name);
            }
            AssetDatabase.SaveAssets();
            return string.Join(", ", log);
        }

        [MenuItem("OpenZ/Apply Unit Style (stylized)")]
        public static string ApplyAll()
        {
            var mats = new HashSet<Material>();
            foreach (var n in UnitPrefabs)
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/OpenZ/Prefabs/Z_{n}.prefab");
                if (!pf) continue;
                foreach (var r in pf.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) if (m && m.shader && m.shader.name == "OpenZ/TeamLit") mats.Add(m);
            }
            int robot = 0;
            foreach (var m in mats)
            {
                bool isRobot = m.name.Contains("SkinPBR") || m.name.StartsWith("other-bot") || m.name.StartsWith("robot");
                if (isRobot) robot++;
                ApplyUnitStyle(m, isRobot);
            }
            AssetDatabase.SaveAssets();
            return $"stylized {mats.Count} unit materials ({robot} robot): {string.Join(", ", mats.Select(m => m.name).OrderBy(x => x).Take(40))}";
        }
    }
}
