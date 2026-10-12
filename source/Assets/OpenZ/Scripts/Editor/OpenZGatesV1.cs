using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// as the live m01 / m02 / m03 models, so every node lands in the prefab Model's space: checked against the kit report's pivots in
    /// prefab-root metres) and rigs Z_fort / Z_robot_factory / Z_vehicle_factory:
    ///  * the Model's body mesh -> the cut body (fort: FortBase_Gate from the LIVE fort_v2 FortBase, 4096 textures unchanged; the gun
    ///    nodes stay the live ones), materials kept;
    ///  * the gate_* nodes (hall, light strips, lamp lenses, door leaves / shutter slats, markers) as children of Model, materials:
    ///    GateKit_* = OpenZ/TeamLit with the kit atlas x the baked vertex AO, GateKit_Owner = the owner band (TeamLit accent, full team
    ///    colour), GateLight = warm emissive (HDR, blooms);
    ///  * Resources/OpenZ/BuildingAnims/vehicle_factory_Split.glb -> the kit's cut split body (BuildingAnimView swaps it in at run time).
    /// Backups of the prefabs and the split body go to backups/unity/prefabs.
    /// </summary>
    public static class OpenZGatesV1
    {
        const string Dst = "Assets/OpenZ/Models/Gates", MatDir = "Assets/OpenZ/Materials/Gates";
        static string Src => Path.Combine(OpenZPaths.RepoRoot, "work", "staging", "gates_v1_resume", "exports");
        static readonly (string prefab, string glb, string body, string kit, Vector3 pivotL)[] Sets =
        {
            ("Assets/OpenZ/Prefabs/Z_fort.prefab", "Fort_Gate.glb", "FortBase", "Fort", new Vector3(-0.802f, 2.9065f, 6.353f)),
            ("Assets/OpenZ/Prefabs/Z_robot_factory.prefab", "robot_factory_Gate.glb", "robot_factory_Body", "Factory", new Vector3(-0.872f, 1.1665f, 1.329f)),
            ("Assets/OpenZ/Prefabs/Z_vehicle_factory.prefab", "vehicle_factory_Gate.glb", "vehicle_factory_Body", "Factory", new Vector3(0f, 0.25125f, 2.594f)),
        };

        static bool Copy(string src, string dst)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            if (File.Exists(dst) && File.ReadAllBytes(dst).SequenceEqual(File.ReadAllBytes(src))) return false;
            File.Copy(src, dst, true); return true;
        }

        static Material Mat(string name, System.Action<Material> set)
        {
            Directory.CreateDirectory(MatDir);
            string p = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(Shader.Find("OpenZ/TeamLit")); AssetDatabase.CreateAsset(m, p); }
            set(m); m.enableInstancing = true; EditorUtility.SetDirty(m); return m;
        }

        static void KitLook(Material m, Texture tex)
        {
            m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_UseVertexColor", 1); m.SetFloat("_VertexColorSRGB", 1); m.SetFloat("_TeamMode", 0); m.SetFloat("_Accent", 0);
            m.SetFloat("_Degreen", 0); m.SetFloat("_Triplanar", 0); m.SetFloat("_TopBlend", 0);
            m.SetFloat("_Stylize", 1); m.SetFloat("_Degrime", 0); m.SetFloat("_FlatShade", 0);
            m.SetFloat("_Smoothness", 0.22f); m.SetFloat("_Metallic", 0f);
            m.SetColor("_RimColor", new Color(0.10f, 0.09f, 0.08f)); m.SetFloat("_RimPower", 3.2f);
            if (m.HasProperty("_SoftFill")) m.SetFloat("_SoftFill", 0.62f);
            if (m.HasProperty("_AmbientBoost")) m.SetFloat("_AmbientBoost", 1.25f);
            if (m.HasProperty("_NightClass")) m.SetFloat("_NightClass", 3f);
            m.SetColor("_EmissionColor", Color.black);
        }

        [MenuItem("OpenZ/Gates v1/Import kit + rig the fort and factories")]
        public static string Import()
        {
            var sb = new StringBuilder("gates v1: ");
            int c = 0;
            foreach (var f in new[] { "Fort_Gate.glb", "robot_factory_Gate.glb", "vehicle_factory_Gate.glb", "gate_kit_fort_base_color.png", "gate_kit_factory_base_color.png" })
                if (Copy(Path.Combine(Src, f), $"{Dst}/{f}")) c++;
            var bak = Path.Combine(OpenZPaths.RepoRoot, "backups", "unity", "prefabs");
            Directory.CreateDirectory(bak);
            const string split = "Assets/OpenZ/Resources/OpenZ/BuildingAnims/vehicle_factory_Split.glb";
            if (!File.Exists(Path.Combine(bak, "vehicle_factory_Split_before_gates.glb"))) File.Copy(Path.GetFullPath(split), Path.Combine(bak, "vehicle_factory_Split_before_gates.glb"));
            if (Copy(Path.Combine(Src, "building_anims_split", "vehicle_factory_Split.glb"), split)) c++;
            AssetDatabase.Refresh();
            sb.Append($"{c} files copied; ");
            foreach (var t in new[] { "gate_kit_fort_base_color.png", "gate_kit_factory_base_color.png" })
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath($"{Dst}/{t}");
                ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.anisoLevel = 8; ti.maxTextureSize = 2048; ti.filterMode = FilterMode.Trilinear;
                ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.wrapMode = TextureWrapMode.Clamp; ti.SaveAndReimport();
            }
            var atlasFort = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dst}/gate_kit_fort_base_color.png");
            var atlasFac = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dst}/gate_kit_factory_base_color.png");
            var kitFort = Mat("M_GateKit_Fort", m => KitLook(m, atlasFort));
            var kitFac = Mat("M_GateKit_Factory", m => KitLook(m, atlasFac));
            // owner band: TeamPaint names "OwnerGlow" materials full team colour (accent mode, _OwnerPart 1)
            var ownerFort = Mat("M_GateKit_OwnerGlow_Fort", m => { KitLook(m, atlasFort); m.SetFloat("_Accent", 1); m.SetFloat("_TeamMode", 1); });
            var ownerFac = Mat("M_GateKit_OwnerGlow_Factory", m => { KitLook(m, atlasFac); m.SetFloat("_Accent", 1); m.SetFloat("_TeamMode", 1); });
            var light = Mat("M_GateLight", m => { KitLook(m, null); m.SetFloat("_UseVertexColor", 0); m.SetColor("_BaseColor", new Color(1f, 0.86f, 0.66f)); m.SetColor("_EmissionColor", new Color(1f, 0.8f, 0.55f) * 2.6f); m.SetFloat("_Stylize", 0); });
            foreach (var s in Sets)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dst}/{s.glb}");
                if (!asset) { sb.Append($"{s.glb} not imported; "); continue; }
                var bakFile = Path.Combine(bak, Path.GetFileNameWithoutExtension(s.prefab) + "_before_gates.prefab");
                if (!File.Exists(bakFile)) File.Copy(Path.GetFullPath(s.prefab), bakFile);
                var root = PrefabUtility.LoadPrefabContents(s.prefab);
                var model = root.transform.Find("Model");
                // drop an earlier rig of the kit (re-runnable)
                foreach (var old in model.GetComponentsInChildren<Transform>(true).Where(t => t != model && t.name.StartsWith("gate_") && t.parent == model).ToList()) Object.DestroyImmediate(old.gameObject);
                // 1) the cut body
                var bodySrc = asset.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == s.body);
                var bodyMf = bodySrc ? bodySrc.GetComponent<MeshFilter>() : null;
                var liveMf = s.body == "FortBase" ? model.Find("FortBase")?.GetComponent<MeshFilter>() : model.GetComponent<MeshFilter>();
                string bodyNote;
                if (bodyMf && liveMf)
                {
                    var ob = liveMf.sharedMesh.bounds; var nb = bodyMf.sharedMesh.bounds;
                    bodyNote = $"body {liveMf.sharedMesh.name} -> {bodyMf.sharedMesh.name}, bounds delta {Vector3.Distance(ob.min, nb.min) + Vector3.Distance(ob.max, nb.max):F4}";
                    liveMf.sharedMesh = bodyMf.sharedMesh;
                }
                else bodyNote = "BODY NOT SWAPPED";
                // 2) the gate nodes, in Model space (the asset root = the Model's space: glTFast, like the live model)
                var bodyMat = liveMf ? liveMf.GetComponent<MeshRenderer>().sharedMaterial : null;
                int nodes = 0;
                foreach (Transform t in asset.transform)
                {
                    if (!t.name.StartsWith("gate_")) continue;
                    var g = Object.Instantiate(t.gameObject, model, false); g.name = t.name;
                    g.transform.localPosition = t.localPosition; g.transform.localRotation = t.localRotation; g.transform.localScale = t.localScale;
                    foreach (var r in g.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        var ms = r.sharedMaterials;
                        for (int i = 0; i < ms.Length; i++)
                        {
                            string n = ms[i] ? ms[i].name : "";
                            ms[i] = n.Contains("GateLight") ? light : n.Contains("Owner") ? (s.kit == "Fort" ? ownerFort : ownerFac) : n.Contains("GateKit") ? (s.kit == "Fort" ? kitFort : kitFac) : (bodyMat ? bodyMat : (s.kit == "Fort" ? kitFort : kitFac));
                        }
                        r.sharedMaterials = ms;
                        r.shadowCastingMode = r.name.StartsWith("gate_light") || r.name.StartsWith("gate_lamp") ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                        r.receiveShadows = true;
                    }
                    nodes++;
                }
                // 3) check: gate_door_L (or the bottom slat) in prefab-root metres vs the kit report
                var probe = model.Find("gate_door_L") ?? model.Find("gate_slat_00");
                var at = probe ? root.transform.InverseTransformPoint(probe.position) : Vector3.zero;
                PrefabUtility.SaveAsPrefabAsset(root, s.prefab); PrefabUtility.UnloadPrefabContents(root);
                sb.Append($"{Path.GetFileName(s.prefab)}: {bodyNote}, {nodes} gate nodes, {(probe ? probe.name : "?")} at {at.ToString("F3")} (kit {s.pivotL.ToString("F3")}, off {Vector3.Distance(at, s.pivotL):F3} m); ");
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[OpenZ] " + sb);
            return sb.ToString();
        }
    }
}
