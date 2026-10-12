#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Import settings for Assets/OpenZ/Models/DetailsV2/PalmsV3 (as the DetailsV2 kit: no materials, preserve hierarchy, no axis
    /// bake, unit scale; the atlases sRGB 1024 with mips), and the menus:
    ///   OpenZ > Palms v3 > 1 Swap palm meshes : DV2_SD1_PalmA / PalmB keep their names and GUIDs (every placement keeps working);
    ///       the old kit node (BODY_Trunk, LOOSE_Crown, RUIN_Stump, FRONT_) is replaced by the new FBX node (BODY_ intact palm,
    ///       RUIN_Stump, FRONT_, inactive BREAK_ parts), with M_DV2_Palm{A,B}_v3 (TeamLit, a copy of M_DV2_Paint_L02 with the new
    ///       atlas), and PalmFall on the root. Re-runnable: it works on whatever is in the prefabs at the time.
    ///       OpenZ > Details v2 > Build and Place rebuilds the palm prefabs from the old kit, so it calls Swap again (L02) before placing.
    ///   OpenZ > Palms v3 > 2 Check : PASS / FAIL per prefab (node, parts, material, texture, PalmFall, tris).
    ///   OpenZ > Palms v3 > Play: break the palm nearest the view centre : in Play, breaks it from the camera side (view only).
    /// </summary>
    public class OpenZPalmsV3 : AssetPostprocessor
    {
        public const string Folder = "Assets/OpenZ/Models/DetailsV2/PalmsV3/";
        const string Prefabs = "Assets/OpenZ/Resources/OpenZ/DetailsV2/";
        const string MatFolder = "Assets/OpenZ/Materials/DetailsV2/";
        const string PaintMat = "Assets/OpenZ/Materials/DetailsV2/M_DV2_Paint_L02.mat";
        static readonly string[] Variants = { "A", "B" };

        void OnPreprocessModel()
        {
            if (!assetPath.Replace('\\', '/').StartsWith(Folder)) return;
            var mi = (ModelImporter)assetImporter;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.isReadable = false;
            mi.globalScale = 1f; mi.useFileScale = true; mi.bakeAxisConversion = false; mi.preserveHierarchy = true;
            mi.importBlendShapes = false; mi.importCameras = false; mi.importLights = false; mi.importVisibility = false;
            mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
            mi.addCollider = false; mi.generateSecondaryUV = false; mi.weldVertices = true;
            mi.importNormals = ModelImporterNormals.Import; mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.sortHierarchyByName = true;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').StartsWith(Folder)) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.alphaSource = TextureImporterAlphaSource.None;
            ti.maxTextureSize = 1024; ti.anisoLevel = 4; ti.wrapMode = TextureWrapMode.Clamp; ti.textureCompression = TextureImporterCompression.CompressedHQ;
        }

        /// <summary>True when the palms_v3 FBX files are in the project (OpenZDetailsV2.BuildAndPlace swaps again after its kit rebuild).</summary>
        public static bool Ready() { return AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "PalmA_v3.fbx") != null; }

        [MenuItem("OpenZ/Palms v3/1 Swap palm meshes (DV2_SD1_PalmA, PalmB)")]
        public static void SwapMenu() { Swap(true); }

        public static void Swap(bool check)
        {
            AssetDatabase.ImportAsset(Folder.TrimEnd('/'), ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            var log = new StringBuilder("[OpenZ Palms v3] swap\n");
            foreach (var v in Variants)
            {
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "Palm" + v + "_v3.fbx");
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "Palm" + v + "_v3_Atlas.png");
                var path = Prefabs + "DV2_SD1_Palm" + v + ".prefab";
                if (!fbx || !tex || !AssetDatabase.LoadAssetAtPath<GameObject>(path)) { log.AppendLine("  " + v + ": MISSING fbx / atlas / prefab (" + path + ")"); continue; }
                var mat = MakeMaterial(v, tex);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    // the old kit node: the child that holds BODY_ / LOOSE_ / RUIN_ parts
                    var old = new List<Transform>();
                    foreach (Transform c in root.transform)
                        if (c.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("BODY_") || t.name.StartsWith("LOOSE_") || t.name.StartsWith("RUIN_"))) old.Add(c);
                    string nodeName = old.Count > 0 ? old[0].name : "DV2_SD1_Palm" + v + "_Palm" + v;
                    // the old node's pose = the FBX axis node turned so FRONT_ faces -Z (Build and Place); the new node gets the same
                    bool hasOld = old.Count > 0;
                    Vector3 oPos = hasOld ? old[0].localPosition : Vector3.zero, oScale = hasOld ? old[0].localScale : Vector3.one;
                    Quaternion oRot = hasOld ? old[0].localRotation : Quaternion.identity;
                    foreach (var c in old) Object.DestroyImmediate(c.gameObject);
                    var inst = (GameObject)Object.Instantiate(fbx, root.transform, false);   // in the prefab's own preview scene
                    var node = inst.transform.Find("Palm" + v + "_v3");
                    if (!node && inst.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("BODY_Palm"))) { node = new GameObject("node").transform; node.SetParent(root.transform, false); foreach (var c in inst.transform.Cast<Transform>().ToList()) c.SetParent(node, true); }
                    if (!node) { Object.DestroyImmediate(inst); log.AppendLine("  " + v + ": FBX has no node Palm" + v + "_v3"); continue; }
                    node.SetParent(root.transform, false);                                    // keeps its local pose (the FBX axis node)
                    Object.DestroyImmediate(inst);
                    node.name = nodeName;
                    if (hasOld) { node.localPosition = oPos; node.localRotation = oRot; node.localScale = oScale; }
                    else
                    {   // no old node: turn about Y so FRONT_ faces -Z, as Build and Place does
                        var fr = node.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("FRONT_"));
                        if (fr)
                        {
                            var d = root.transform.InverseTransformPoint(fr.position); d.y = 0f;
                            if (d.sqrMagnitude > 1e-4f) node.localRotation = Quaternion.AngleAxis(Vector3.SignedAngle(d, Vector3.back, Vector3.up), Vector3.up) * node.localRotation;
                        }
                    }
                    int parts = 0, tris = 0;
                    foreach (var r in node.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        r.sharedMaterial = mat;
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true;
                        var mf = r.GetComponent<MeshFilter>();
                        if (r.name.StartsWith("BREAK_")) { parts++; r.gameObject.SetActive(false); }
                        else if (r.name.StartsWith("BODY_") && mf && mf.sharedMesh) tris = (int)(mf.sharedMesh.GetIndexCount(0) / 3);
                    }
                    if (!root.GetComponent<PalmFall>()) root.AddComponent<PalmFall>();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    log.AppendLine("  " + v + ": " + path + " node " + nodeName + ", body " + tris + " tris, " + parts + " break parts, " + mat.name);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log(log.ToString());
            if (check) Check();
        }

        static Material MakeMaterial(string v, Texture2D tex)
        {
            var path = MatFolder + "M_DV2_Palm" + v + "_v3.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                var src = AssetDatabase.LoadAssetAtPath<Material>(PaintMat);
                m = src ? new Material(src) : new Material(Shader.Find("OpenZ/TeamLit") ?? Shader.Find("Universal Render Pipeline/Lit"));
                m.name = "M_DV2_Palm" + v + "_v3";
                AssetDatabase.CreateAsset(m, path);
            }
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (m.HasProperty("_UseVertexColor")) m.SetFloat("_UseVertexColor", 1f);           // the "Col" AO, as the kits
            EditorUtility.SetDirty(m);
            return m;
        }

        [MenuItem("OpenZ/Palms v3/2 Check")]
        public static void Check()
        {
            var log = new StringBuilder("[OpenZ Palms v3] check\n"); bool ok = true;
            foreach (var v in Variants)
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "DV2_SD1_Palm" + v + ".prefab");
                if (!pf) { log.AppendLine("  " + v + ": FAIL no prefab"); ok = false; continue; }
                var all = pf.GetComponentsInChildren<Transform>(true);
                var body = all.FirstOrDefault(t => t.name.StartsWith("BODY_Palm"));
                var ruin = all.FirstOrDefault(t => t.name.StartsWith("RUIN_Stump"));
                var parts = all.Where(t => t.name.StartsWith("BREAK_")).ToList();
                int segs = parts.Count(t => t.name.StartsWith("BREAK_Seg")), cocos = parts.Count(t => t.name.StartsWith("BREAK_Coco")), crown = parts.Count(t => t.name.StartsWith("BREAK_Crown"));
                bool oldLeft = all.Any(t => t.name.StartsWith("LOOSE_Crown") || t.name.StartsWith("BODY_Trunk"));
                var r = body ? body.GetComponent<MeshRenderer>() : null;
                var mat = r ? r.sharedMaterial : null;
                bool texOk = mat && mat.HasProperty("_BaseMap") && mat.GetTexture("_BaseMap") && mat.GetTexture("_BaseMap").name == "Palm" + v + "_v3_Atlas";
                var mf = body ? body.GetComponent<MeshFilter>() : null;
                int tris = mf && mf.sharedMesh ? (int)(mf.sharedMesh.GetIndexCount(0) / 3) : 0;
                bool hasCol = mf && mf.sharedMesh && mf.sharedMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color);
                bool pass = body && ruin && segs >= 3 && crown == 1 && !oldLeft && texOk && tris > 800 && tris < 2200 && pf.GetComponent<PalmFall>() && pf.GetComponent<DetailProp>() && hasCol;
                ok &= pass;
                log.AppendLine("  " + v + ": " + (pass ? "PASS" : "FAIL") + "  body " + tris + " tris" + (hasCol ? " + Col" : " NO Col") + ", ruin " + (ruin ? "yes" : "NO") + ", " + segs + " segments, " + crown + " crown, " + cocos + " coconuts"
                               + (oldLeft ? ", OLD PARTS LEFT" : "") + ", material " + (mat ? mat.name + " / " + mat.shader.name : "NONE") + (texOk ? "" : " (atlas NOT set)")
                               + ", PalmFall " + (pf.GetComponent<PalmFall>() ? "yes" : "NO"));
            }
            Debug.Log(log.Append(ok ? "PALMS V3 CHECK PASS" : "PALMS V3 CHECK FAIL").ToString());
        }

        [MenuItem("OpenZ/Palms v3/Play: break the palm nearest the view centre")]
        public static void PlayBreak()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ Palms v3] enter Play first"); return; }
            var cam = Camera.main; if (!cam) return;
            var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            DetailProp best = null; float bd = float.MaxValue;
            foreach (var p in DetailProp.All)
            {
                if (!p || p.Broken || p.kind != "palm") continue;
                float d = Vector3.Cross(ray.direction, p.transform.position - ray.origin).magnitude;
                if (d < bd) { bd = d; best = p; }
            }
            if (!best) { Debug.LogWarning("[OpenZ Palms v3] no standing palm"); return; }
            var from = best.transform.position - Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized * 4f;   // a blast on the camera side: it falls away from the camera
            Debug.Log("[OpenZ Palms v3] breaking " + best.id + " (" + best.name + ") at " + best.transform.position.ToString("F1") + ", view only (the sim palm stays until the sim kills it)");
            best.Break(from);
        }
    }
}
#endif
