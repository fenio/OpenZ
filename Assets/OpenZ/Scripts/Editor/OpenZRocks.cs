using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenZ.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// open level as a tall striated pillar: one per rock on its bottom (blocking) tile, variant A-D (never the same as the
    /// left / upper neighbour), yaw 90 x k +- 8 deg, scale 0.95-1.1; a side facing a road, water or a building is squashed
    /// so the pillar never clips into it. Children: Pillar (active), Damaged, Rubble (inactive) + a RockPillar component.
    /// </summary>
    public static class OpenZRocks
    {
        const string Dst = "Assets/OpenZ/Models/RocksZ";

        // rocks_v2 (VX18): the v2 kit (jointed mesa blocks + strata/sand atlas); same mesh names, pivots and stages as v1
        static string Src => Path.Combine(OpenZPaths.RepoRoot, "projects", "blender", "export", "rocks_z_v2");

        [MenuItem("OpenZ/Rocks: Import RockZ kit")]
        public static string Import()
        {
            Directory.CreateDirectory(Dst);
            int n = 0;
            foreach (var f in Directory.GetFiles(Src, "RockZ_*_*.fbx"))
            {
                var dst = $"{Dst}/{Path.GetFileName(f)}";
                File.Copy(f, dst, true); n++;
            }
            foreach (var f in Directory.GetFiles(Src, "rockz2_atlas_*.png"))
            {   // rocks_v2 copy: one 1024 atlas per biome (bottom half rock strata 16 x 8 m, top half sand / gravel)
                File.Copy(f, $"{Dst}/{Path.GetFileName(f)}", true); n++;
            }
            AssetDatabase.Refresh();
            foreach (var path in Directory.GetFiles(Dst, "*.fbx").Select(p => p.Replace('\\', '/')))
            {
                var mi = (ModelImporter)AssetImporter.GetAtPath(path);
                mi.globalScale = 1; mi.useFileUnits = true; mi.bakeAxisConversion = false; mi.importCameras = false; mi.importLights = false;
                mi.meshCompression = ModelImporterMeshCompression.Off; mi.importNormals = ModelImporterNormals.Import; mi.addCollider = false;
                mi.isReadable = path.Contains("_Chunk_"); mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                mi.SaveAndReimport();
            }
            foreach (var path in Directory.GetFiles(Dst, "rockz2_atlas_*.png").Select(p => p.Replace('\\', '/')))
            {   // rocks_v2: sRGB colour atlas, clamped (UVs stay inside their half), trilinear + aniso like the cliffs v3 textures
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 8; ti.maxTextureSize = 2048; ti.textureCompression = TextureImporterCompression.CompressedHQ;   // textures_v2: 2048 atlas, 128 px/m
                ti.SaveAndReimport();
            }
            Mat("Desert"); Mat("City");
            AssetDatabase.SaveAssets();
            return $"imported {n} FBX";
        }

        /// <summary>One shared vertex-colour TeamLit material per biome (README settings; desert graded to the L02 sandstone).</summary>
        static Material Mat(string biome)
        {
            string p = $"{Dst}/M_RockZ_{biome}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(Shader.Find("OpenZ/TeamLit")); AssetDatabase.CreateAsset(m, p); }
            // rocks_v2: atlas x vertex colour with the M_CliffV3_Rock values (clean stylized, no grading), see-through on
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dst}/rockz2_atlas_{biome}.png"));
            m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureOffset("_BaseMap", Vector2.zero); m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Triplanar", 0); m.SetFloat("_TriVertical", 0); m.SetFloat("_TopBlend", 0); m.SetFloat("_Accent", 0); m.SetFloat("_Degreen", 0);
            if (m.HasProperty("_SeeThru")) m.SetFloat("_SeeThru", 1);
            m.SetFloat("_UseVertexColor", 1); m.SetFloat("_VertexColorSRGB", 1); m.SetFloat("_TeamMode", 0);
            m.SetFloat("_FlatShade", 1); m.SetFloat("_Stylize", 1); m.SetFloat("_Degrime", 0f /* textures_v2: no de-grime blur */); m.SetFloat("_DegrimeLod", 2); m.SetFloat("_Smoothness", 0.05f); m.SetFloat("_Metallic", 0);
            m.SetColor("_RimColor", new Color(0.12f, 0.10f, 0.08f)); m.SetFloat("_RimPower", 3.2f);
            if (m.HasProperty("_SoftFill")) m.SetFloat("_SoftFill", 0.6f);
            if (m.HasProperty("_AmbientBoost")) m.SetFloat("_AmbientBoost", 1.25f);
            if (m.HasProperty("_KeyWarm")) m.SetColor("_KeyWarm", new Color(1.06f, 1f, 0.92f));
            m.SetColor("_DarkColor", Color.black); m.SetFloat("_DarkThreshold", 0.12f);
            // rocks_v2: the atlas is the approved cliffs v3 strata / sand, so no extra grade (matches M_CliffV3_Rock_L02 / L17)
            m.SetFloat("_SatScale", 1f); m.SetFloat("_ValScale", 1f); m.SetFloat("_HueShift", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Mesh MeshOf(string name) => AssetDatabase.LoadAllAssetsAtPath($"{Dst}/{name}.fbx").OfType<Mesh>().FirstOrDefault();

        [MenuItem("OpenZ/Rocks: Rebuild rocks in open level")]
        public static string RebuildOpenLevel()
        {
            var scene = EditorSceneManager.GetActiveScene();
            string lv = scene.name.StartsWith("L17") ? "L17" : "L02";
            string biome = lv == "L17" ? "City" : "Desert";
            var mat = Mat(biome);
            var pillars = "ABCD".Select(c => MeshOf($"RockZ_{biome}_Pillar_{c}")).ToArray();
            var dmg = MeshOf($"RockZ_{biome}_Damaged"); var rub = MeshOf($"RockZ_{biome}_Rubble");
            var chunks = Enumerable.Range(0, 8).Select(i => MeshOf($"RockZ_{biome}_Chunk_{i}")).Where(x => x).ToArray();
            if (pillars.Any(x => !x) || !dmg || !rub) return "kit meshes missing: run Import first";

            var w = ZWorld.Create(Resources.Load<TextAsset>("OpenZ/default_settings").text, Resources.Load<TextAsset>($"OpenZ/Maps/{lv}_grid").text, 1);
            var root = GameObject.Find("ZodObjects");
            var byName = new Dictionary<string, Transform>(); foreach (Transform t in root.transform) byName[t.name] = t;
            var ground = GameObject.Find("COL_Terrain")?.GetComponent<Collider>();
            // tiles a pillar must not spill into: roads, water and building footprints
            var blocked = new HashSet<(int, int)>();
            for (int x = 0; x < w.Map.WidthPix / 16; x++) for (int y = 0; y < w.Map.HeightPix / 16; y++) { var c = w.Map.ClassAt(x, y); if (c == ZClass.Road || c == ZClass.Water || c == ZClass.DeepWater) blocked.Add((x, y)); }
            foreach (var o in w.Objects) if (o.Kind == ZKind.Building || o.Kind == ZKind.Cannon)
                for (int x = o.LocX / 16; x < (o.LocX + o.WidthPix) / 16; x++) for (int y = o.LocY / 16; y < (o.LocY + o.HeightPix) / 16; y++) blocked.Add((x, y));
            var placed = new Dictionary<(int, int), int>();
            int count = 0, squashed = 0;
            foreach (var o in w.Objects.Where(x => x.Type == "rock").OrderBy(x => x.LocY).ThenBy(x => x.LocX))
            {
                if (o.Source == null || !byName.TryGetValue($"rock_{o.Source.x}_{o.Source.y}", out var t)) continue;
                int tx = o.LocX / 16, ty = o.LocY / 16 + 2;                               // the bottom (blocking) tile
                var rng = new System.Random(tx * 7919 + ty * 104729 + (lv == "L17" ? 17 : 2));
                var avoid = new HashSet<int>();
                if (placed.TryGetValue((tx - 1, ty), out var l)) avoid.Add(l);
                if (placed.TryGetValue((tx, ty - 1), out var u)) avoid.Add(u);
                int variant; do variant = rng.Next(4); while (avoid.Contains(variant) && avoid.Count < 4);
                placed[(tx, ty)] = variant;

                var pos = new Vector3(tx * 2 + 1, 0, -(ty * 2 + 1));
                if (ground && ground.Raycast(new Ray(pos + Vector3.up * 200, Vector3.down), out var hit, 400)) pos.y = hit.point.y;
                float yaw = 90f * rng.Next(4) + (float)(rng.NextDouble() * 16 - 8), s = 0.95f + (float)rng.NextDouble() * 0.15f;
                // squash a side that faces a road / water / building so the pillar (reach ~1.3 m) stays on its tile
                float sx = s, sz = s;
                if (blocked.Contains((tx - 1, ty)) || blocked.Contains((tx + 1, ty))) { sx = Mathf.Min(s, 0.78f); squashed++; }
                if (blocked.Contains((tx, ty - 1)) || blocked.Contains((tx, ty + 1))) { sz = Mathf.Min(s, 0.78f); squashed++; }

                for (int i = t.childCount - 1; i >= 0; i--) Object.DestroyImmediate(t.GetChild(i).gameObject);
                foreach (var old in t.GetComponents<Component>()) if (!(old is Transform)) Object.DestroyImmediate(old);
                t.SetPositionAndRotation(pos, Quaternion.identity); t.localScale = Vector3.one;
                var rp = t.gameObject.AddComponent<RockPillar>();
                rp.pillar = Part(t, "Pillar", pillars[variant], mat, yaw, sx, s, sz, true);
                rp.damaged = Part(t, "Damaged", dmg, mat, yaw, sx, s, sz, false);
                rp.rubble = Part(t, "Rubble", rub, mat, yaw, sx, s, sz, false);
                rp.chunks = chunks; rp.chunkMaterial = mat;
                count++;
            }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveOpenScenes(); AssetDatabase.SaveAssets();
            return $"{lv}: {count} rocks rebuilt as {biome} pillars ({squashed} sides squashed next to roads/water/buildings)";
        }

        static GameObject Part(Transform parent, string name, Mesh mesh, Material mat, float yaw, float sx, float sy, float sz, bool active)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            // world-axis squash: yaw is a multiple of 90 deg (+-8), so swap the axes on 90 / 270
            bool swap = Mathf.RoundToInt(yaw / 90f) % 2 != 0;
            go.transform.localScale = swap ? new Vector3(sz, sy, sx) : new Vector3(sx, sy, sz);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; mr.receiveShadows = true;
            go.SetActive(active);
            return go;
        }
    }
}
