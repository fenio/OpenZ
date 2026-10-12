using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Replaces the v2 baked strip (WATER_Upper_Sheet_D01 + WATER_Stream_Ring_D01), the small hexagon top pool (WATER_Pond_D01) and the
    /// D01 lip rocks with the modelled WF3 set: a fall sheet fitted to the cliffs v3 face (overflow lip, two cascade breaks, width and
    /// thickness variation), three side trickles, a stone-rimmed source basin (RocksZ v2 stones), an organic plunge-pond bank that covers
    /// the old straight right / bottom pond edges, a broken shore lace and plunge rings. The D01 WaterfallFx (scrolling, foam, mist,
    /// spray, light, the 3D loop sound) now drives the WF3 children (the old kit pieces are switched off and ignored). Idempotent.
    /// Waterfall v3: revert to v2; Details v2/Build and Place bakes v2 again); these files stay so v3 can be installed from this menu.
    /// View only: no sim change, no Zod tile touched, no colliders.
    /// </summary>
    public static class OpenZWaterfallV3
    {
        const string Dir = "Assets/OpenZ/Models/DetailsV2/WF3";
        const string Fbx = Dir + "/WF3_L02_Waterfall.fbx";
        const string Tex = Dir + "/wf3_flow_foam.png";
        const string WaterMat = "Assets/OpenZ/Materials/DetailsV2/M_WF3_Water.mat";
        const string SheetMat = "Assets/OpenZ/Materials/DetailsV2/M_DV2_WaterfallSheet_L02.mat";
        const string CliffTopMat = "Assets/OpenZ/Materials/Env/M_CliffV3_Top_L02.mat";
        const string RockMat = "Assets/OpenZ/Models/RocksZ/M_RockZ_Desert.mat";
        const string GroundMat = "Assets/OpenZ/Materials/Env/M_L02_Ground.mat";
        const string BankFit = Dir + "/BANK_WF3_Plunge_fit.asset";
        public const string RootName = "WF3_L02";
        static readonly Vector3 RootPos = new Vector3(72.4f, 0f, -11.6f);
        static readonly Vector3 LipExpected = new Vector3(72.576f, 17.785f, -8.578f), LandExpected = new Vector3(72.05f, -0.35f, -13.0f);
        /// <summary>The D01 kit / v2 pieces WF3 replaces (switched off; WaterfallFx ignores inactive children).</summary>
        public static readonly string[] OldKit = { "WATER_Pond_D01", "BODY_LipRocks_D01", "WATER_Upper_D01", "WATER_Lower_D01", "FX_Foam_D01", "FX_Mist_D01",
                                                   "FX_Spray_D01", "FX_Spray_Top_D01", "LIGHT_Waterfall_D01", "SND_Waterfall_D01" };

        [MenuItem("OpenZ/Details v2/Waterfall v3 (install L02 WF3)")]
        public static string Menu()
        {
            var r = Apply(null);
            var s = EditorSceneManager.GetActiveScene(); EditorSceneManager.MarkSceneDirty(s); EditorSceneManager.SaveScene(s);
            AssetDatabase.SaveAssets();
            Debug.Log(r);
            return r;
        }

        [MenuItem("OpenZ/Details v2/Waterfall v3: revert to v2")]
        public static void Revert()
        {
            var wf = Object.FindObjectsByType<OpenZ.WaterfallFx>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
            if (!wf) { Debug.LogWarning("[OpenZ] waterfall v3 revert: no WaterfallFx in the open scene"); return; }
            var prev = wf.transform.Find(RootName); if (prev) Object.DestroyImmediate(prev.gameObject);
            foreach (var t in wf.GetComponentsInChildren<Transform>(true)) if (OldKit.Contains(t.name)) t.gameObject.SetActive(true);
            Debug.Log(OpenZWaterfallV2.Apply(null) + " (waterfall v3 reverted to v2)");
            var s = EditorSceneManager.GetActiveScene(); EditorSceneManager.MarkSceneDirty(s); EditorSceneManager.SaveScene(s);
        }

        static void EnsureImport()
        {
            var mi = AssetImporter.GetAtPath(Fbx) as ModelImporter;
            if (mi && (mi.materialImportMode != ModelImporterMaterialImportMode.None || mi.importAnimation || mi.addCollider || mi.isReadable
                       || mi.meshCompression != ModelImporterMeshCompression.Off || mi.importNormals != ModelImporterNormals.Import || mi.bakeAxisConversion
                       || mi.importCameras || mi.importLights || !mi.useFileScale || Mathf.Abs(mi.globalScale - 1f) > 1e-4f))
            {   // as DV2_L02_Kit.fbx: scale 1, file units, no baked axis conversion, normals imported, no compression, no colliders / materials
                mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importAnimation = false; mi.addCollider = false; mi.isReadable = false;
                mi.meshCompression = ModelImporterMeshCompression.Off; mi.importNormals = ModelImporterNormals.Import; mi.bakeAxisConversion = false;
                mi.importCameras = false; mi.importLights = false; mi.useFileScale = true; mi.globalScale = 1f; mi.importBlendShapes = false;
                mi.SaveAndReimport();
            }
            var ti = AssetImporter.GetAtPath(Tex) as TextureImporter;
            if (ti && (!ti.sRGBTexture || ti.wrapMode != TextureWrapMode.Repeat || !ti.mipmapEnabled || ti.anisoLevel != 4 || ti.textureCompression != TextureImporterCompression.CompressedHQ))
            {
                ti.sRGBTexture = true; ti.wrapMode = TextureWrapMode.Repeat; ti.mipmapEnabled = true; ti.anisoLevel = 4;
                ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.SaveAndReimport();
            }
        }

        static Material Water()
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(WaterMat);
            if (!m)
            {
                var src = AssetDatabase.LoadAssetAtPath<Material>(SheetMat);
                m = src ? new Material(src) : new Material(Shader.Find("OpenZ/WaterfallSheet"));
                m.name = "M_WF3_Water";
                AssetDatabase.CreateAsset(m, WaterMat);
            }
            m.shader = Shader.Find("OpenZ/WaterfallSheet");
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Tex));
            m.SetFloat("_FadeY", -0.35f);
            EditorUtility.SetDirty(m);
            return m;
        }

        public static string Apply(Transform detailsRoot)
        {
            var sb = new StringBuilder("[OpenZ] waterfall v3: ");
            EnsureImport();
            var wf = detailsRoot ? detailsRoot.GetComponentInChildren<OpenZ.WaterfallFx>(true)
                                 : Object.FindObjectsByType<OpenZ.WaterfallFx>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
            if (!wf) return sb.Append("no WaterfallFx in the open scene").ToString();
            var root = wf.transform;
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            if (!src) return sb.Append("FAILED: " + Fbx + " not imported").ToString();

            // 1. the v2 bake and the old kit pieces off
            int removed = 0, off = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t && (t.name.StartsWith("WATER_Upper_Sheet") || t.name.StartsWith("WATER_Stream_Ring"))).ToList())
            { Object.DestroyImmediate(t.gameObject); removed++; }
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (OldKit.Contains(t.name) && t.gameObject.activeSelf) { t.gameObject.SetActive(false); off++; }
            sb.Append("v2 pieces removed " + removed + ", kit pieces off " + off + "; ");

            // 2. the WF3 set (unpacked, so the markers can go)
            var prev = root.Find(RootName); if (prev) Object.DestroyImmediate(prev.gameObject);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src, root.gameObject.scene);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = RootName;
            go.transform.SetParent(root, true);
            go.transform.position = RootPos; go.transform.localScale = Vector3.one;
            sb.Append("root rot " + go.transform.rotation.eulerAngles.ToString("F1") + "; ");

            // 3. marker check, then the markers go
            var all = go.GetComponentsInChildren<Transform>(true);
            var lip = all.FirstOrDefault(t => t.name.StartsWith("MRK_WF3_Lip")); var land = all.FirstOrDefault(t => t.name.StartsWith("MRK_WF3_Land"));
            if (lip && land && Vector3.Distance(lip.position, LipExpected) > 0.05f)
            {   // the FBX root imports as (270, 0, 0): the set lands mirrored in x / z; half a turn about the vertical axis puts it on its spots
                go.transform.rotation = Quaternion.Euler(0f, 180f, 0f) * go.transform.rotation; go.transform.position = RootPos;
                sb.Append("turned 180 deg about y -> root rot " + go.transform.rotation.eulerAngles.ToString("F1") + "; ");
            }
            float dl = lip ? Vector3.Distance(lip.position, LipExpected) : -1f, dd = land ? Vector3.Distance(land.position, LandExpected) : -1f;
            sb.Append("markers: lip " + (lip ? lip.position.ToString("F3") + " off " + dl.ToString("0.000") + " m" : "MISSING") +
                      ", land " + (land ? land.position.ToString("F3") + " off " + dd.ToString("0.000") + " m" : "MISSING") +
                      ((dl >= 0f && dl < 0.05f && dd >= 0f && dd < 0.05f) ? " (OK)" : " (CHECK: not on the expected spots)") + "; ");
            foreach (var t in all.Where(t => t && t.name.StartsWith("MRK_")).ToList()) Object.DestroyImmediate(t.gameObject);

            // 4. materials + shadows
            var water = Water();
            var cliffTop = AssetDatabase.LoadAssetAtPath<Material>(CliffTopMat);
            var rock = AssetDatabase.LoadAssetAtPath<Material>(RockMat);
            var ground = AssetDatabase.LoadAssetAtPath<Material>(GroundMat);
            int tris = 0, nR = 0;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                string n = r.name; Material m = null;
                if (n.StartsWith("WATER_")) { m = water; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = true; }
                else if (n.StartsWith("BED_")) { m = cliffTop; r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; }
                else if (n.StartsWith("ROCKZ_")) { m = rock; r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; }
                else if (n.StartsWith("BANK_"))
                {
                    m = ground; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = true;
                    var fit = AssetDatabase.LoadAssetAtPath<Mesh>(BankFit);
                    if (fit) { r.GetComponent<MeshFilter>().sharedMesh = fit; sb.Append("bank: fitted mesh; "); }
                }
                if (m) r.sharedMaterials = Enumerable.Repeat(m, Mathf.Max(1, r.sharedMaterials.Length)).ToArray();
                else sb.Append("NO MATERIAL RULE for " + n + "; ");
                r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                var mf = r.GetComponent<MeshFilter>(); if (mf && mf.sharedMesh) tris += mf.sharedMesh.triangles.Length / 3;
                nR++;
            }
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            sb.Append(nR + " meshes, " + tris + " tris; materials M_WF3_Water / M_CliffV3_Top_L02 / M_RockZ_Desert / M_L02_Ground; ");
            sb.Append("FX points " + string.Join(", ", go.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("FX_") || t.name.StartsWith("SND_") || t.name.StartsWith("LIGHT_")).Select(t => t.name)) + ".");
            return sb.ToString();
        }
    }
}
