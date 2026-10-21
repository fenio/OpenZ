// OpenZ (GPL-3.0)
// Assets/Settings/PC_RPAsset (HIGH, never written). Re-runnable: each run re-copies PC_RPAsset, so a later change to HIGH flows into
// both presets. Only the fields below differ; render scale stays 1.0 (the project rules "full quality, never downscaled").
// PC_Renderer (HIGH's renderer is never written): MEDIUM = SSAO at half resolution, LOW = SSAO off. Both preset assets are registered as
// extra quality levels after "PC" (the Standalone default level and the editor's current level stay PC = HIGH), so the URP build
// preprocessor gathers their features and keeps their shader variants (SSAO off, the low / medium soft-shadow filters) in the player;
// OzQuality still switches them live with QualitySettings.renderPipeline.
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ
{
    public static class OpenZQualityPresets
    {
        const string Src = "Assets/Settings/PC_RPAsset.asset", SrcRenderer = "Assets/Settings/PC_Renderer.asset", Dir = "Assets/OpenZ/Resources/OpenZ/Quality";

        struct Preset { public string name, renderer, level; public int msaa, shadowRes, addShadowRes, cascades, softQuality; public float distance; public bool ssaoOff, ssaoHalf; }

        static readonly Preset[] Presets =
        {
            new Preset { name = OzQuality.MediumAsset, renderer = "OZ_Renderer_Medium", level = "PC Medium (OpenZ preset)", msaa = 2, shadowRes = 2048, addShadowRes = 2048, cascades = 2, distance = 180f, softQuality = 2, ssaoHalf = true },
            new Preset { name = OzQuality.LowAsset, renderer = "OZ_Renderer_Low", level = "PC Low (OpenZ preset)", msaa = 1, shadowRes = 1024, addShadowRes = 1024, cascades = 1 /* version 1.1: one shadow step on LOW */, distance = 120f, softQuality = 1, ssaoOff = true },
        };

        [MenuItem("OpenZ/Perf/Make quality presets")]
        public static void Make()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[OpenZ Quality] stop Play first"); return; }
            if (!AssetDatabase.IsValidFolder("Assets/OpenZ/Resources/OpenZ/Quality")) AssetDatabase.CreateFolder("Assets/OpenZ/Resources/OpenZ", "Quality");
            var sb = new StringBuilder("[OpenZ Quality] presets made from " + Src + ":");
            foreach (var p in Presets)
            {
                // the preset's renderer: a copy of PC_Renderer (every feature kept: Atmosphere, Forward+, decals, depth / opaque textures)
                string rdst = Dir + "/" + p.renderer + ".asset";
                if (AssetDatabase.LoadAssetAtPath<Object>(rdst)) AssetDatabase.DeleteAsset(rdst);
                if (!AssetDatabase.CopyAsset(SrcRenderer, rdst)) { Debug.LogError("[OpenZ Quality] copy failed: " + rdst); return; }
                string ssao = "no SSAO feature found";
                foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(rdst))
                {
                    if (!sub || sub.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;
                    var fso = new SerializedObject(sub);
                    if (p.ssaoOff) { var act = fso.FindProperty("m_Active"); if (act != null) { act.boolValue = false; ssao = "SSAO off"; } }
                    if (p.ssaoHalf) { var ds = fso.FindProperty("m_Settings.Downsample"); if (ds != null) { ds.boolValue = true; ssao = "SSAO half resolution"; } }
                    fso.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(sub);
                }
                var rd = AssetDatabase.LoadAssetAtPath<ScriptableObject>(rdst);

                string dst = Dir + "/" + p.name + ".asset";
                if (AssetDatabase.LoadAssetAtPath<Object>(dst)) AssetDatabase.DeleteAsset(dst);
                if (!AssetDatabase.CopyAsset(Src, dst)) { Debug.LogError("[OpenZ Quality] copy failed: " + dst); return; }
                var a = AssetDatabase.LoadAssetAtPath<Object>(dst);
                var so = new SerializedObject(a);
                Set(so, "m_MSAA", p.msaa, sb); Set(so, "m_MainLightShadowmapResolution", p.shadowRes, sb);
                Set(so, "m_AdditionalLightsShadowmapResolution", p.addShadowRes, sb); Set(so, "m_ShadowCascadeCount", p.cascades, sb);
                Set(so, "m_SoftShadowQuality", p.softQuality, sb); Set(so, "m_RenderScale", 1f, sb);
                var d = so.FindProperty("m_ShadowDistance"); if (d != null) d.floatValue = p.distance; else sb.Append(" (m_ShadowDistance missing)");
                var list = so.FindProperty("m_RendererDataList");
                if (list != null && rd) { list.arraySize = 1; list.GetArrayElementAtIndex(0).objectReferenceValue = rd; var di = so.FindProperty("m_DefaultRendererIndex"); if (di != null) di.intValue = 0; }
                else sb.Append(" (m_RendererDataList missing: the preset keeps PC_Renderer)");
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(a);
                sb.Append($"\n  {p.name}: MSAA {p.msaa}x, main shadows {p.shadowRes} / {p.cascades} cascades / {p.distance:0} m, additional shadow atlas {p.addShadowRes}, soft quality {p.softQuality}, render scale 1, renderer {p.renderer} ({ssao})");
            }
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            sb.Append("\n  " + RegisterLevels());
            Debug.Log(sb.ToString());
        }

        /// <summary>Adds (or re-points) one quality level per preset after "PC", with the preset's URP asset; the current / default level
        /// stays where it was (PC = HIGH). Only the build's shader-variant gathering reads these levels; the game never selects them.</summary>
        [MenuItem("OpenZ/Perf/Register quality presets as quality levels")]
        public static string RegisterLevels()
        {
            var qsObj = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
            if (qsObj == null || qsObj.Length == 0) return "QualitySettings.asset not found";
            var so = new SerializedObject(qsObj[0]);
            var arr = so.FindProperty("m_QualitySettings"); var cur = so.FindProperty("m_CurrentQuality");
            if (arr == null) return "m_QualitySettings not found";
            int pc = -1;
            for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == "PC") pc = i;
            if (pc < 0) return "quality level PC not found: presets not registered";
            int curBefore = cur != null ? cur.intValue : -1;
            var sb = new StringBuilder("quality levels:");
            foreach (var p in Presets)
            {
                var asset = AssetDatabase.LoadAssetAtPath<Object>(Dir + "/" + p.name + ".asset");
                if (!asset) { sb.Append($" {p.name} missing;"); continue; }
                int at = -1;
                for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == p.level) at = i;
                if (at < 0)
                {
                    arr.arraySize = arr.arraySize + 1;                                                    // a new last level (Unity fills it from the previous last)
                    at = arr.arraySize - 1;
                    var src = arr.GetArrayElementAtIndex(pc); var e = arr.GetArrayElementAtIndex(at);
                    // copy PC's settings field by field, then the name and the URP asset
                    var it = src.Copy(); var end = src.GetEndProperty(); bool enter = true;
                    while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
                    {
                        enter = false;
                        var rel = it.propertyPath.Substring(src.propertyPath.Length + 1);
                        var dstP = e.FindPropertyRelative(rel); if (dstP == null) continue;
                        try { CopyValue(it, dstP); } catch { }
                    }
                    sb.Append($" added '{p.level}' at {at};");
                }
                else sb.Append($" '{p.level}' at {at} re-pointed;");
                var el = arr.GetArrayElementAtIndex(at);
                el.FindPropertyRelative("name").stringValue = p.level;
                var crp = el.FindPropertyRelative("customRenderPipeline"); if (crp != null) crp.objectReferenceValue = asset;
            }
            if (cur != null && curBefore >= 0) cur.intValue = curBefore;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            var r = sb.Append($" current level {(cur != null ? cur.intValue.ToString() : "?")} (was {curBefore}), names {string.Join(" / ", QualitySettings.names)}").ToString();
            Debug.Log("[OpenZ Quality] " + r);
            return r;
        }

        static void CopyValue(SerializedProperty a, SerializedProperty b)
        {
            switch (a.propertyType)
            {
                case SerializedPropertyType.Integer: b.intValue = a.intValue; break;
                case SerializedPropertyType.Boolean: b.boolValue = a.boolValue; break;
                case SerializedPropertyType.Float: b.floatValue = a.floatValue; break;
                case SerializedPropertyType.String: b.stringValue = a.stringValue; break;
                case SerializedPropertyType.Enum: b.enumValueIndex = a.enumValueIndex; break;
                case SerializedPropertyType.ObjectReference: b.objectReferenceValue = a.objectReferenceValue; break;
                case SerializedPropertyType.Vector2: b.vector2Value = a.vector2Value; break;
                case SerializedPropertyType.Vector3: b.vector3Value = a.vector3Value; break;
                case SerializedPropertyType.Color: b.colorValue = a.colorValue; break;
            }
        }

        static void Set(SerializedObject so, string field, int v, StringBuilder sb)
        {
            var pr = so.FindProperty(field);
            if (pr == null) { sb.Append($" ({field} missing)"); return; }
            pr.intValue = v;
        }

        static void Set(SerializedObject so, string field, float v, StringBuilder sb)
        {
            var pr = so.FindProperty(field);
            if (pr == null) { sb.Append($" ({field} missing)"); return; }
            pr.floatValue = v;
        }
    }
}
