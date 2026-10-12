using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// supply crates and debris planks), so Z_grenades is rebuilt from chunky boxes: plank sides with gaps, corner battens,
    /// a darker lid band, metal end handles, a stencilled label board (2 cm proud, never co-planar), three grenades on the
    /// lid, and the PickupGlow ring. Same prefab asset, so every scene instance updates.
    /// </summary>
    public static class OpenZCrate
    {
        const string Prefab = "Assets/OpenZ/Prefabs/Z_grenades.prefab", Tex = "Assets/OpenZ/Textures", Mat = "Assets/OpenZ/Materials/Props";

        /// <summary>
        /// (OpenZ/Grenade Crate v2/1). This builder deleted every child of the prefab and rebuilt ~30 code boxes, so it refuses to run.</summary>
        public static bool Retired = true;
        [MenuItem("OpenZ/Rebuild Grenade Crate (RETIRED: use Grenade Crate v2)")]
        public static string Build()
        {
            if (Retired) { Debug.LogWarning("[OpenZ] OpenZCrate is retired: the grenade crate is the modelled v2 crate (menu OpenZ/Grenade Crate v2). Nothing changed."); return "retired"; }
            System.IO.Directory.CreateDirectory(Mat);
            var wood = M("M_CrateWood", AssetDatabase.LoadAssetAtPath<Texture>($"{Tex}/crate_wood.png"), Color.white);
            var woodDark = M("M_CrateWoodDark", AssetDatabase.LoadAssetAtPath<Texture>($"{Tex}/crate_wood.png"), new Color(0.58f, 0.52f, 0.46f));
            var metal = M("M_CrateMetal", null, new Color(0.22f, 0.23f, 0.25f));
            var label = M("M_CrateLabel", LabelTexture(), Color.white);
            var grenade = M("M_Grenade", null, new Color(0.36f, 0.38f, 0.28f));
            var root = PrefabUtility.LoadPrefabContents(Prefab);
            for (int i = root.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            var model = new GameObject("Model").transform; model.SetParent(root.transform, false);
            float L = 1.2f, Hh = 0.52f, D = 0.74f;
            // plank sides: 3 horizontal boards per face with small gaps
            for (int k = 0; k < 3; k++)
            {
                float y = 0.02f + k * (Hh / 3f) + Hh / 6f, h = Hh / 3f - 0.025f;
                Box(model, "PlankF" + k, new Vector3(0, y, -D / 2), new Vector3(L, h, 0.04f), wood);
                Box(model, "PlankB" + k, new Vector3(0, y, D / 2), new Vector3(L, h, 0.04f), wood);
                Box(model, "PlankL" + k, new Vector3(-L / 2, y, 0), new Vector3(0.04f, h, D - 0.04f), wood);
                Box(model, "PlankR" + k, new Vector3(L / 2, y, 0), new Vector3(0.04f, h, D - 0.04f), wood);
            }
            Box(model, "Core", new Vector3(0, Hh / 2 + 0.01f, 0), new Vector3(L - 0.05f, Hh - 0.02f, D - 0.05f), woodDark);   // dark gaps between boards
            // lid: boards + darker band all round
            for (int k = 0; k < 4; k++) Box(model, "Lid" + k, new Vector3(0, Hh + 0.04f, -D / 2 + (k + 0.5f) * D / 4f), new Vector3(L + 0.02f, 0.05f, D / 4f - 0.02f), wood);
            Box(model, "LidBandF", new Vector3(0, Hh - 0.01f, -D / 2 - 0.012f), new Vector3(L + 0.04f, 0.09f, 0.03f), woodDark);
            Box(model, "LidBandB", new Vector3(0, Hh - 0.01f, D / 2 + 0.012f), new Vector3(L + 0.04f, 0.09f, 0.03f), woodDark);
            Box(model, "LidBandL", new Vector3(-L / 2 - 0.012f, Hh - 0.01f, 0), new Vector3(0.03f, 0.09f, D + 0.04f), woodDark);
            Box(model, "LidBandR", new Vector3(L / 2 + 0.012f, Hh - 0.01f, 0), new Vector3(0.03f, 0.09f, D + 0.04f), woodDark);
            // corner battens
            foreach (var sx in new[] { -1f, 1f }) foreach (var sz in new[] { -1f, 1f })
                Box(model, "Batten", new Vector3(sx * (L / 2 + 0.01f), Hh / 2, sz * (D / 2 + 0.01f)), new Vector3(0.07f, Hh + 0.02f, 0.07f), woodDark);
            // metal handles on the short ends (bracket + bar)
            foreach (var sx in new[] { -1f, 1f })
            {
                Box(model, "HandleBar", new Vector3(sx * (L / 2 + 0.07f), Hh * 0.62f, 0), new Vector3(0.03f, 0.03f, 0.28f), metal);
                Box(model, "HandleL", new Vector3(sx * (L / 2 + 0.045f), Hh * 0.62f, -0.13f), new Vector3(0.06f, 0.05f, 0.03f), metal);
                Box(model, "HandleR", new Vector3(sx * (L / 2 + 0.045f), Hh * 0.62f, 0.13f), new Vector3(0.06f, 0.05f, 0.03f), metal);
            }
            // stencilled label boards on both long faces, 2 cm proud of the planks
            Box(model, "LabelF", new Vector3(0, Hh * 0.48f, -D / 2 - 0.035f), new Vector3(0.86f, 0.26f, 0.02f), label, true, 0f, 180f);   // roll 180: cube face UVs are upside down
            Box(model, "LabelB", new Vector3(0, Hh * 0.48f, D / 2 + 0.035f), new Vector3(0.86f, 0.26f, 0.02f), label, true, 180f, 180f);
            // grenades on the lid (readable at game zoom)
            for (int k = 0; k < 3; k++)
            {
                var gp = new Vector3(-0.3f + k * 0.3f, Hh + 0.16f, (k - 1) * 0.08f);
                var g = GameObject.CreatePrimitive(PrimitiveType.Sphere); Object.DestroyImmediate(g.GetComponent<Collider>());
                g.name = "Grenade"; g.transform.SetParent(model, false); g.transform.localPosition = gp; g.transform.localScale = new Vector3(0.17f, 0.21f, 0.17f);
                g.GetComponent<Renderer>().sharedMaterial = grenade;
                Box(model, "Fuse", gp + new Vector3(0, 0.12f, 0), new Vector3(0.06f, 0.06f, 0.06f), metal);
                Box(model, "Lever", gp + new Vector3(0.05f, 0.07f, 0), new Vector3(0.025f, 0.1f, 0.05f), metal);
            }
            // scale the crate to Z's 1-tile pickup (a bit oversized, toy-like) and add the glow
            model.localScale = Vector3.one * 1.5f;                                                     // reads at the default RTS zoom
            if (!root.GetComponent<OpenZ.PickupGlow>()) root.AddComponent<OpenZ.PickupGlow>();
            foreach (var r in root.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true; }
            PrefabUtility.SaveAsPrefabAsset(root, Prefab); PrefabUtility.UnloadPrefabContents(root);
            AssetDatabase.SaveAssets();
            return "grenade crate rebuilt";
        }

        static void Box(Transform parent, string name, Vector3 pos, Vector3 size, Material m, bool label = false, float yaw = 0, float roll = 0)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(b.GetComponent<Collider>());
            b.name = name; b.transform.SetParent(parent, false); b.transform.localPosition = pos; b.transform.localScale = size;
            b.transform.localRotation = Quaternion.Euler(0, yaw, roll);
            b.GetComponent<Renderer>().sharedMaterial = m;
        }

        static Material M(string name, Texture tex, Color c)
        {
            string p = $"{Mat}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(Shader.Find("OpenZ/TeamLit")); AssetDatabase.CreateAsset(m, p); }
            m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", c); m.SetFloat("_TeamMode", 0); m.SetFloat("_Stylize", 1);
            m.SetFloat("_Degrime", 0); m.SetFloat("_Degreen", 0); m.SetFloat("_FlatShade", 1); m.SetFloat("_Smoothness", name.Contains("Metal") ? 0.35f : 0.08f);
            m.SetFloat("_Metallic", name.Contains("Metal") ? 0.4f : 0f); m.enableInstancing = true;
            EditorUtility.SetDirty(m); return m;
        }

        /// <summary>Wood + stencil composited (opaque, so the label never needs alpha sorting).</summary>
        static Texture LabelTexture()
        {
            string p = $"{Tex}/crate_label.png";
            var wood = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Tex}/crate_wood.png"); var st = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Tex}/crate_stencil.png");
            if (!wood || !st) return wood;
            MakeReadable(wood); MakeReadable(st);
            var outT = new Texture2D(st.width, st.height, TextureFormat.RGB24, false);
            for (int y = 0; y < st.height; y++)
                for (int x = 0; x < st.width; x++)
                {
                    var w = wood.GetPixel(x % wood.width, y % wood.height) * 1.05f; var s = st.GetPixel(x, y);
                    outT.SetPixel(x, y, Color.Lerp(w, new Color(s.r, s.g, s.b), s.a));
                }
            outT.Apply(); System.IO.File.WriteAllBytes(p, outT.EncodeToPNG()); AssetDatabase.ImportAsset(p);
            return AssetDatabase.LoadAssetAtPath<Texture>(p);
        }

        static void MakeReadable(Texture2D t)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t));
            if (!ti.isReadable) { ti.isReadable = true; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.SaveAndReimport(); }
        }
    }
}
