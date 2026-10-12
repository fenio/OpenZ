using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// small brown adobe shack with a dark arched doorway (L02 and L17 alike), so Z_hut is rebuilt as a chunky, readable
    /// shack: adobe body, straw half-barrel roof with overhang, dark arched door and window, wooden poles and beam ends.
    /// </summary>
    public static class OpenZHut
    {
        const string Prefab = "Assets/OpenZ/Prefabs/Z_hut.prefab", Mat = "Assets/OpenZ/Materials/Props";

        [MenuItem("OpenZ/Rebuild Hut")]
        public static string Build()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>("Assets/OpenZ/Resources/OpenZ/BaseProps2/BP2_L02_Fuel.prefab")
                || AssetDatabase.LoadAssetAtPath<GameObject>("Assets/OpenZ/Resources/OpenZ/BaseProps/BP_L02_FuelDump.prefab"))
                return OpenZBaseProps.RebuildHutPrefab();
            System.IO.Directory.CreateDirectory(Mat);
            var adobe = M("M_HutAdobe", new Color(0.62f, 0.46f, 0.33f)); var straw = M("M_HutStraw", new Color(0.78f, 0.63f, 0.36f));
            var dark = M("M_HutDoor", new Color(0.12f, 0.09f, 0.07f)); var wood = M("M_HutWood", new Color(0.42f, 0.29f, 0.18f));
            var root = PrefabUtility.LoadPrefabContents(Prefab);
            for (int i = root.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            var m = new GameObject("Model").transform; m.SetParent(root.transform, false);
            float W = 1.9f, H = 1.15f, D = 1.5f;
            P(m, PrimitiveType.Cube, "Body", new Vector3(0, H / 2, 0), new Vector3(W, H, D), Vector3.zero, adobe);
            P(m, PrimitiveType.Cube, "Plinth", new Vector3(0, 0.08f, 0), new Vector3(W + 0.12f, 0.16f, D + 0.12f), Vector3.zero, M("M_HutPlinth", new Color(0.5f, 0.37f, 0.27f)));
            // straw half-barrel roof along X, with overhang and a darker ridge
            P(m, PrimitiveType.Cylinder, "Roof", new Vector3(0, H, 0), new Vector3(D + 0.45f, (W + 0.4f) / 2f, 0.95f), new Vector3(0, 0, 90), straw);
            P(m, PrimitiveType.Cylinder, "Ridge", new Vector3(0, H + 0.46f, 0), new Vector3(0.14f, (W + 0.44f) / 2f, 0.14f), new Vector3(0, 0, 90), wood);
            // dark arched doorway on the front (-z, the model front) + a small window
            P(m, PrimitiveType.Cube, "Door", new Vector3(0.25f, 0.42f, -D / 2 - 0.01f), new Vector3(0.5f, 0.66f, 0.04f), Vector3.zero, dark);
            P(m, PrimitiveType.Cylinder, "DoorArch", new Vector3(0.25f, 0.75f, -D / 2 - 0.01f), new Vector3(0.5f, 0.02f, 0.5f), new Vector3(90, 0, 0), dark);
            P(m, PrimitiveType.Cube, "Window", new Vector3(-0.5f, 0.66f, -D / 2 - 0.01f), new Vector3(0.28f, 0.22f, 0.04f), Vector3.zero, dark);
            // wooden poles at the corners and beam ends poking out under the roof
            foreach (var sx in new[] { -1f, 1f }) foreach (var sz in new[] { -1f, 1f })
                P(m, PrimitiveType.Cylinder, "Pole", new Vector3(sx * (W / 2 + 0.02f), H / 2 + 0.05f, sz * (D / 2 + 0.02f)), new Vector3(0.1f, H / 2 + 0.08f, 0.1f), Vector3.zero, wood);
            for (int k = 0; k < 3; k++)
                P(m, PrimitiveType.Cylinder, "Beam", new Vector3(-0.6f + k * 0.6f, H - 0.05f, -D / 2 - 0.12f), new Vector3(0.08f, 0.14f, 0.08f), new Vector3(90, 0, 0), wood);
            m.localScale = Vector3.one * 1.1f;                                                   // one Zod tile, a bit toy-oversized
            foreach (var r in root.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true; }
            PrefabUtility.SaveAsPrefabAsset(root, Prefab); PrefabUtility.UnloadPrefabContents(root);
            AssetDatabase.SaveAssets();
            return "hut rebuilt";
        }

        static void P(Transform parent, PrimitiveType t, string name, Vector3 pos, Vector3 scale, Vector3 euler, Material mat)
        {
            var g = GameObject.CreatePrimitive(t); Object.DestroyImmediate(g.GetComponent<Collider>());
            g.name = name; g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localRotation = Quaternion.Euler(euler); g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = mat;
        }

        static Material M(string name, Color c)
        {
            string p = $"{Mat}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(Shader.Find("OpenZ/TeamLit")); AssetDatabase.CreateAsset(m, p); }
            m.SetTexture("_BaseMap", null); m.SetColor("_BaseColor", c); m.SetFloat("_TeamMode", 0); m.SetFloat("_Stylize", 1);
            m.SetFloat("_Degrime", 0); m.SetFloat("_Degreen", 0); m.SetFloat("_FlatShade", 1); m.SetFloat("_Smoothness", 0.06f); m.SetFloat("_Metallic", 0);
            m.enableInstancing = true; EditorUtility.SetDirty(m); return m;
        }
    }
}
