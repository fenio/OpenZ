using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Level 16 rock, placed into the OPEN level 16 scene (the scene is never rebuilt):
    /// - "5 Rock masses": the twelve natural rock masses of the level. Each of the 214 rock tiles gets ITS OWN SLICE of its
    ///   mass (the faces whose centre lies over the tile), standing on the tile the rules block (two tiles south of the map
    ///   entry, like every rock of the game), draped on the ground. Children Pillar (the slice, with its plants) and Damaged
    ///   (the same slice broken off lower, fresh rock on the break) + the RockPillar component; toward a neighbouring rock tile
    ///   the slice is closed with a fresh rock face that shows only once that neighbour is gone. One object per tile: no rule changes.
    /// - "6 Rim rocks": the generated rock pieces that stand where nothing breaks (on the map sides), from the object list of
    ///   the last blockout, each with its far version.
    /// </summary>
    public static class OpenZL16Rocks
    {
        const string Dir = "Assets/OpenZ/Models/L16/Rocks";
        const string TilesAsset = Dir + "/L16_rock_tiles.asset";
        internal static string ListPath => Path.Combine(OpenZPaths.RepoRoot, "work", "staging", "v1_1_20261012", "jungle_import", "J4_rims_rocks", "out", "blockout_v5_objects.json");

        [System.Serializable] internal class BObj { public string name, type, data, parent; public string[] colls, mats; public float[] loc, quat_wxyz, scale, min, max; public bool hidden; }
        [System.Serializable] class BDoc { public BObj[] objects; }
        internal static BObj[] LoadList()
        {
            if (!File.Exists(ListPath)) { Debug.LogError("[OpenZ L16] object list missing: " + ListPath); return new BObj[0]; }
            return JsonUtility.FromJson<BDoc>(File.ReadAllText(ListPath)).objects;
        }

        /// <summary>A place of the blockout (x east, y north, z up, turn as w x y z, size) as a Unity place. A mirrored object keeps its mirror (negative x size).</summary>
        internal static void Place(BObj o, out Vector3 pos, out Quaternion rot, out Vector3 scl)
        {
            pos = new Vector3(o.loc[0], o.loc[2], o.loc[1]);
            var q = new Quaternion(o.quat_wxyz[1], o.quat_wxyz[2], o.quat_wxyz[3], o.quat_wxyz[0]);
            var m = Matrix4x4.TRS(Vector3.zero, q, new Vector3(o.scale[0], o.scale[1], o.scale[2]));       // the numbers as they are (not yet a Unity matrix)
            // swap y and z on both sides: columns are the images of the Unity axes
            Vector3 Col(int unityAxis) { int b = unityAxis == 1 ? 2 : unityAxis == 2 ? 1 : 0; var c = m.GetColumn(b); return new Vector3(c.x, c.z, c.y); }
            Vector3 cx = Col(0), cy = Col(1), cz = Col(2);
            float sx = cx.magnitude, sy = cy.magnitude, sz = cz.magnitude;
            if (Vector3.Dot(Vector3.Cross(cx, cy), cz) < 0f) { sx = -sx; }
            Vector3 ax = cx / sx, ay = cy / sy, az = cz / sz;
            rot = Quaternion.LookRotation(az, ay); scl = new Vector3(sx, sy, sz);
            if (Vector3.Dot(rot * Vector3.right, ax) < 0.99f) rot = Quaternion.LookRotation(az, ay);        // (orthonormal by construction)
        }

        static void ImportModel(string path, bool readable)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(path);
            if (mi == null) return;
            mi.importCameras = false; mi.importLights = false; mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
            mi.bakeAxisConversion = true; mi.isReadable = readable; mi.meshCompression = ModelImporterMeshCompression.Off; mi.addCollider = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            mi.SaveAndReimport();
        }

        static void ImportTex(string path, int aniso = 4)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            if (ti == null) return;
            ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Repeat;
            ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = aniso; ti.maxTextureSize = 2048; ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
        }

        /// <summary>A rock material in the game's own rock settings (the released rock pillars' material is the pattern), with one picture.</summary>
        internal static Material RockMat(string name, string tex, bool vertexColours, bool seeThru)
        {
            string p = $"{Dir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            var pattern = AssetDatabase.LoadAssetAtPath<Material>("Assets/OpenZ/Models/RocksZ/M_RockZ_Desert.mat");
            if (!m) { m = pattern ? new Material(pattern) : new Material(Shader.Find("OpenZ/TeamLit")); AssetDatabase.CreateAsset(m, p); }
            else if (pattern) { m.shader = pattern.shader; m.CopyPropertiesFromMaterial(pattern); }
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tex));
            m.SetFloat("_UseVertexColor", vertexColours ? 1 : 0); m.SetFloat("_FlatShade", 0);
            if (m.HasProperty("_SeeThru")) m.SetFloat("_SeeThru", seeThru ? 1 : 0);
            m.enableInstancing = true; EditorUtility.SetDirty(m);
            return m;
        }

        static float Hash(float x, float z) { float s = Mathf.Sin(x * 12.9898f + z * 78.233f) * 43758.5453f; return s - Mathf.Floor(s); }

        // ------------------------------------------------------------------------------------------------ rock masses
        [MenuItem("OpenZ/L16 Jungle/5 Rock masses (rock tiles of the open level 16 scene)")]
        public static void RockMassesMenu() => Debug.Log("[OpenZ L16] " + RockMasses());

        public static string RockMasses()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.name.StartsWith("L16")) return "open the level 16 scene first";
            string fbx = Dir + "/JG2_RockGroups.fbx";
            if (!File.Exists(fbx)) return "missing " + fbx;
            ImportModel(fbx, true);
            foreach (var t in new[] { "jg2_rock_strata_1024", "jg2_top_jungle_1024", "jungle_verge_moss_1024", "jg2_rock_fresh_1024", "jg2_palette" }) ImportTex($"{Dir}/{t}.png");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var mats = new[]
            {
                RockMat("M_L16_Rock", $"{Dir}/jg2_rock_strata_1024.png", false, true), RockMat("M_L16_RockTop", $"{Dir}/jg2_top_jungle_1024.png", false, true),
                RockMat("M_L16_RockMoss", $"{Dir}/jungle_verge_moss_1024.png", false, true), RockMat("M_L16_RockFresh", $"{Dir}/jg2_rock_fresh_1024.png", false, true),
            };
            var vegMat = RockMat("M_L16_RockPlants", $"{Dir}/jg2_palette.png", true, true); vegMat.SetFloat("_VertexColorSRGB", 1);
            vegMat.SetTexture("_BaseMap", null);   // the plants carry their colour in the vertices (the palette picture holds the same colour: both together went near black)
            string[] matKeys = { "M_JG2_Rock", "M_JG2_TopJungle", "M_JG2_Moss", "M_JG2_RockFresh" };
            var chunks = Enumerable.Range(0, 8).Select(i => AssetDatabase.LoadAllAssetsAtPath($"Assets/OpenZ/Models/RocksZ/RockZ_Desert_Chunk_{i}.fbx").OfType<Mesh>().FirstOrDefault()).Where(x => x).ToArray();

            var root = GameObject.Find("ZodObjects"); var col = GameObject.Find("COL_Terrain")?.GetComponent<Collider>();
            if (!root || !col) return "ZodObjects or COL_Terrain missing";
            Physics.SyncTransforms();
            float Ground(float x, float z) => col.Raycast(new Ray(new Vector3(x, 300f, z), Vector3.down), out var h, 600f) ? h.point.y : 0f;
            // the tile a rock stands on (the rules block the tile two rows south of the map entry)
            var tiles = new Dictionary<Vector2Int, Transform>();
            foreach (Transform t in root.transform)
            {
                if (!t.name.StartsWith("rock_")) continue;
                var p = t.name.Split('_'); tiles[new Vector2Int(int.Parse(p[1]), int.Parse(p[2]) + 2)] = t;
            }
            if (File.Exists(TilesAsset)) AssetDatabase.DeleteAsset(TilesAsset);
            Mesh first = null; int made = 0, strayTris = 0, droppedTris = 0, curtains = 0; long trisAll = 0;
            void Store(Mesh m) { if (first == null) { first = m; AssetDatabase.CreateAsset(m, TilesAsset); } else AssetDatabase.AddObjectToAsset(m, TilesAsset); }

            var perTile = new Dictionary<Vector2Int, TileBuf>();
            var colliders = new List<GameObject>();
            for (int g = 0; g < 12; g++)
            {
                var tr = model.transform.Find($"JG2_RockGroup_{g:00}"); var trv = model.transform.Find($"JG2_RockGroup_{g:00}_veg");
                if (!tr) return $"JG2_RockGroup_{g:00} not in the model";
                var touched = new HashSet<Vector2Int>();
                Mesh world = Slice(tr, false, matKeys, tiles, perTile, Ground, touched, ref strayTris, ref droppedTris);
                if (trv) Slice(trv, true, matKeys, tiles, perTile, Ground, touched, ref strayTris, ref droppedTris);
                var cg = new GameObject("tmp_rockgroup_col") { hideFlags = HideFlags.HideAndDontSave }; cg.AddComponent<MeshCollider>().sharedMesh = world; colliders.Add(cg);
                Physics.SyncTransforms();
                var mc = cg.GetComponent<MeshCollider>();
                // close every slice toward its neighbouring rock tiles with a fresh rock face (seen only when the neighbour is gone)
                foreach (var tk in touched)
                {
                    var buf = perTile[tk];
                    for (int side = 0; side < 4; side++)
                    {
                        var nb = tk + (side == 0 ? new Vector2Int(0, -1) : side == 1 ? new Vector2Int(0, 1) : side == 2 ? new Vector2Int(-1, 0) : new Vector2Int(1, 0));
                        if (!tiles.ContainsKey(nb)) continue;
                        float x0 = tk.x * 2f, z0 = -tk.y * 2f;                                         // north-west corner of the tile
                        Vector3 a, b, outward;
                        if (side == 0) { a = new Vector3(x0, 0, z0 - 0.03f); b = new Vector3(x0 + 2, 0, z0 - 0.03f); outward = Vector3.forward; }
                        else if (side == 1) { a = new Vector3(x0 + 2, 0, z0 - 1.97f); b = new Vector3(x0, 0, z0 - 1.97f); outward = Vector3.back; }
                        else if (side == 2) { a = new Vector3(x0 + 0.03f, 0, z0 - 2); b = new Vector3(x0 + 0.03f, 0, z0); outward = Vector3.left; }
                        else { a = new Vector3(x0 + 1.97f, 0, z0); b = new Vector3(x0 + 1.97f, 0, z0 - 2); outward = Vector3.right; }
                        const int K = 9; int baseIdx = buf.pos.Count; bool any = false;
                        for (int i = 0; i < K; i++)
                        {
                            var p = Vector3.Lerp(a, b, i / (K - 1f)); float gy = Ground(p.x, p.z) - 0.3f, top = gy;
                            if (mc.Raycast(new Ray(new Vector3(p.x, 300f, p.z), Vector3.down), out var hit, 600f)) { top = Mathf.Max(gy, hit.point.y - 0.02f); any = true; }
                            float u = (side < 2 ? p.x : p.z) / 12f;
                            buf.pos.Add(new Vector3(p.x, gy, p.z)); buf.nrm.Add(outward); buf.uv.Add(new Vector2(u, gy / 12f)); buf.col.Add(Color.white);
                            buf.pos.Add(new Vector3(p.x, top, p.z)); buf.nrm.Add(outward); buf.uv.Add(new Vector2(u, top / 12f)); buf.col.Add(Color.white);
                        }
                        if (!any) { buf.pos.RemoveRange(baseIdx, K * 2); buf.nrm.RemoveRange(baseIdx, K * 2); buf.uv.RemoveRange(baseIdx, K * 2); buf.col.RemoveRange(baseIdx, K * 2); continue; }
                        for (int i = 0; i < K - 1; i++)
                        {   // seen from outside (from the neighbour's side), a -> b runs left to right
                            int q = baseIdx + i * 2;
                            buf.tri[3].AddRange(new[] { q, q + 3, q + 1, q, q + 2, q + 3 });
                        }
                        buf.curtainFrom = Mathf.Min(buf.curtainFrom, baseIdx); curtains++;
                    }
                }
            }
            foreach (var cg in colliders) Object.DestroyImmediate(cg);

            foreach (var kv in perTile)
            {
                var t = tiles[kv.Key]; var buf = kv.Value;
                var centre = new Vector3(kv.Key.x * 2 + 1, 0, -(kv.Key.y * 2 + 1)); centre.y = Ground(centre.x, centre.z);
                if (PrefabUtility.IsPartOfPrefabInstance(t.gameObject)) PrefabUtility.UnpackPrefabInstance(PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject), PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                for (int i = t.childCount - 1; i >= 0; i--) Object.DestroyImmediate(t.GetChild(i).gameObject);
                foreach (var old in t.GetComponents<Component>()) if (!(old is Transform)) Object.DestroyImmediate(old);
                t.SetPositionAndRotation(centre, Quaternion.identity); t.localScale = Vector3.one;

                var pillar = buf.Build($"rock_{kv.Key.x}_{kv.Key.y}_pillar", centre, false, 0f);
                float H = pillar.bounds.max.y;
                var damaged = buf.Build($"rock_{kv.Key.x}_{kv.Key.y}_damaged", centre, true, Mathf.Max(0.45f, H * 0.5f));
                Store(pillar); Store(damaged); trisAll += pillar.triangles.Length / 3;
                var rp = t.gameObject.AddComponent<RockPillar>();
                rp.pillar = Part(t, "Pillar", pillar, mats, true);
                if (buf.vpos.Count > 0)
                {
                    var veg = buf.BuildVeg($"rock_{kv.Key.x}_{kv.Key.y}_plants", centre); Store(veg);
                    var vg = new GameObject("Plants"); vg.transform.SetParent(rp.pillar.transform, false);
                    vg.AddComponent<MeshFilter>().sharedMesh = veg; var vr = vg.AddComponent<MeshRenderer>(); vr.sharedMaterial = vegMat;
                    vr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
                rp.damaged = Part(t, "Damaged", damaged, mats, false);
                rp.rubble = null; rp.chunks = chunks; rp.chunkMaterial = mats[3];
                made++;
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            int without = tiles.Count - perTile.Count;
            return $"rock masses: {made} of {tiles.Count} rock tiles carry their slice ({without} without any rock face), {trisAll} triangles standing, {curtains} closing faces toward neighbours, " +
                   $"{strayTris} triangles given to the nearest rock tile, {droppedTris} dropped (further than 3 tiles from any rock tile); chunk meshes {chunks.Length}";
        }

        sealed class TileBuf
        {
            public List<Vector3> pos = new List<Vector3>(), nrm = new List<Vector3>(); public List<Vector2> uv = new List<Vector2>(); public List<Color> col = new List<Color>();
            public List<int>[] tri = { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
            public List<Vector3> vpos = new List<Vector3>(), vnrm = new List<Vector3>(); public List<Vector2> vuv = new List<Vector2>(); public List<Color> vcol = new List<Color>(); public List<int> vtri = new List<int>();
            public int curtainFrom = int.MaxValue;

            public Mesh Build(string name, Vector3 origin, bool damaged, float cut)
            {
                var p = new Vector3[pos.Count]; var clamped = new bool[pos.Count];
                for (int i = 0; i < p.Length; i++)
                {
                    p[i] = pos[i] - origin;
                    if (damaged)
                    {
                        float c = cut * (0.72f + 0.56f * Hash(Mathf.Round(pos[i].x * 2f), Mathf.Round(pos[i].z * 2f)));
                        if (p[i].y > c) { p[i].y = c; clamped[i] = true; }
                    }
                }
                var m = new Mesh { name = name, indexFormat = p.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                m.SetVertices(p); m.SetNormals(nrm); m.SetUVs(0, uv);
                m.subMeshCount = 4;
                if (!damaged) for (int s = 0; s < 4; s++) m.SetTriangles(tri[s], s);
                else
                {   // what broke off shows fresh rock: faces whose three corners all came down move to the fresh rock material
                    var lists = new[] { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
                    for (int s = 0; s < 4; s++)
                        for (int i = 0; i < tri[s].Count; i += 3)
                        {
                            int a = tri[s][i], b = tri[s][i + 1], c = tri[s][i + 2];
                            if (p[a] == p[b] || p[b] == p[c] || p[a] == p[c]) continue;
                            bool all = clamped[a] && clamped[b] && clamped[c];
                            lists[all ? 3 : s].AddRange(new[] { a, b, c });
                        }
                    for (int s = 0; s < 4; s++) m.SetTriangles(lists[s], s);
                    m.RecalculateNormals();
                }
                m.RecalculateBounds(); m.RecalculateTangents();
                return m;
            }

            public Mesh BuildVeg(string name, Vector3 origin)
            {
                var m = new Mesh { name = name };
                m.SetVertices(vpos.Select(v => v - origin).ToList()); m.SetNormals(vnrm); m.SetUVs(0, vuv); m.SetColors(vcol); m.SetTriangles(vtri, 0); m.RecalculateBounds();
                return m;
            }
        }

        /// <summary>One mass (or its plants): shifted onto the blocked tiles, draped on the ground, its faces handed to the tiles. Returns the whole draped mass (for the closing faces).</summary>
        static Mesh Slice(Transform tr, bool veg, string[] matKeys, Dictionary<Vector2Int, Transform> tiles, Dictionary<Vector2Int, TileBuf> perTile,
                          System.Func<float, float, float> ground, HashSet<Vector2Int> touched, ref int stray, ref int dropped)
        {
            var mesh = tr.GetComponent<MeshFilter>().sharedMesh; var l2w = tr.localToWorldMatrix; var rmats = tr.GetComponent<MeshRenderer>().sharedMaterials;
            var v = mesh.vertices; var n = mesh.normals; var uv = mesh.uv; var c = mesh.colors;
            var wp = new Vector3[v.Length]; var wn = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var p = l2w.MultiplyPoint3x4(v[i]); p.z -= 4f;                                            // onto the tile the rules block (two tiles south)
                p.y += ground(p.x, p.z) - 0.12f;
                wp[i] = p; wn[i] = i < n.Length ? l2w.MultiplyVector(n[i]).normalized : Vector3.up;
            }
            var maps = new Dictionary<Vector2Int, Dictionary<int, int>>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                string mn = s < rmats.Length && rmats[s] ? rmats[s].name.Split('.')[0] : "";
                int slot = System.Array.IndexOf(matKeys, mn); if (slot < 0) slot = 0;
                var tris = mesh.GetTriangles(s);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    var cen = (wp[tris[i]] + wp[tris[i + 1]] + wp[tris[i + 2]]) / 3f;
                    var tk = new Vector2Int(Mathf.FloorToInt(cen.x / 2f), Mathf.FloorToInt(-cen.z / 2f));
                    if (!tiles.ContainsKey(tk))
                    {
                        Vector2Int best = tk; int bd = int.MaxValue;
                        for (int dy = -3; dy <= 3; dy++) for (int dx = -3; dx <= 3; dx++) { var q = new Vector2Int(tk.x + dx, tk.y + dy); int d = dx * dx + dy * dy; if (d < bd && tiles.ContainsKey(q)) { bd = d; best = q; } }
                        if (bd == int.MaxValue) { dropped++; continue; }
                        tk = best; stray++;
                    }
                    if (!perTile.TryGetValue(tk, out var buf)) perTile[tk] = buf = new TileBuf();
                    if (!maps.TryGetValue(tk, out var map)) maps[tk] = map = new Dictionary<int, int>();
                    touched.Add(tk);
                    for (int k = 0; k < 3; k++)
                    {
                        int src = tris[i + k];
                        if (!map.TryGetValue(src, out int dst))
                        {
                            if (veg) { dst = buf.vpos.Count; buf.vpos.Add(wp[src]); buf.vnrm.Add(wn[src]); buf.vuv.Add(src < uv.Length ? uv[src] : Vector2.zero); buf.vcol.Add(src < c.Length ? c[src] : Color.white); }
                            else { dst = buf.pos.Count; buf.pos.Add(wp[src]); buf.nrm.Add(wn[src]); buf.uv.Add(src < uv.Length ? uv[src] : Vector2.zero); buf.col.Add(Color.white); }
                            map[src] = dst;
                        }
                        if (veg) buf.vtri.Add(dst); else buf.tri[slot].Add(dst);
                    }
                }
            }
            var world = new Mesh { name = "tmp", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            world.SetVertices(wp); world.SetTriangles(mesh.triangles, 0); world.RecalculateBounds();
            return world;
        }

        static GameObject Part(Transform parent, string name, Mesh mesh, Material[] mats, bool active)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = mats;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; mr.receiveShadows = true;
            go.SetActive(active);
            return go;
        }

        // ------------------------------------------------------------------------------------------------ generated pieces on the map sides
        [MenuItem("OpenZ/L16 Jungle/6 Rim rocks (generated pieces on the map sides, open level 16 scene)")]
        public static void RimRocksMenu() => Debug.Log("[OpenZ L16] " + RimRocks());

        public static string RimRocks()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.name.StartsWith("L16")) return "open the level 16 scene first";
            var env = GameObject.Find("Environment"); if (!env) return "Environment missing";
            var list = LoadList().Where(o => o.type == "MESH" && !o.hidden && o.data != null && o.data.StartsWith("HERO_JGH_") &&
                                             (o.name.StartsWith("RIM2_rock_") || o.name.StartsWith("RCK_Hero_10") || o.name.StartsWith("RCK_Hero_11") || o.name.StartsWith("RCK_Hero_12"))).ToArray();
            if (list.Length == 0) return "no rim rock in the object list";
            var old = env.transform.Find("L16_RimRocks"); if (old) Object.DestroyImmediate(old.gameObject);
            var parent = new GameObject("L16_RimRocks").transform; parent.SetParent(env.transform, false);
            var kit = new Dictionary<string, (GameObject near, GameObject far, Material m, Material mf, Quaternion fix)>();
            var sb = new System.Text.StringBuilder(); int placed = 0; float worstAll = 0f; long tris = 0, trisFar = 0;
            foreach (var o in list)
            {
                string piece = o.data.Substring(5).Split('.')[0];                                        // JGH_<piece>
                if (!kit.TryGetValue(piece, out var k))
                {
                    string fbx = $"{Dir}/{piece}.fbx";
                    if (!File.Exists(fbx)) { sb.Append($" [{piece}: model missing]"); kit[piece] = default; continue; }
                    ImportModel(fbx, false); ImportTex($"{Dir}/{piece}_orm.png"); ImportTex($"{Dir}/{piece}_far_basecolor_512.png", 1);
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                    var near = model.transform.Find(piece); var far = model.transform.Find(piece + "_far");
                    if (!near) { sb.Append($" [{piece}: object not in its model]"); kit[piece] = default; continue; }
                    k = (near.gameObject, far ? far.gameObject : null, RockMat("M_L16_" + piece, $"{Dir}/{piece}_orm.png", false, false),
                         far ? RockMat("M_L16_" + piece + "_far", $"{Dir}/{piece}_far_basecolor_512.png", false, false) : null, Quaternion.identity);
                    // which way round the model came in: the turn about the up axis that fits this object's box in the blockout best
                    float bestErr = float.MaxValue;
                    foreach (float yaw in new[] { 0f, 180f, 90f, 270f })
                    {
                        float e = BoxError(o, near, Quaternion.Euler(0, yaw, 0));
                        if (e < bestErr - 0.01f) { bestErr = e; k.fix = Quaternion.Euler(0, yaw, 0); }
                    }
                    kit[piece] = k;
                }
                if (!k.near) continue;
                Place(o, out var pos, out var rot, out var scl);
                var go = new GameObject(o.name); go.transform.SetParent(parent, false); go.transform.SetPositionAndRotation(pos, rot); go.transform.localScale = scl;
                var fixT = new GameObject("Model").transform; fixT.SetParent(go.transform, false); fixT.localRotation = k.fix;
                var a = Object.Instantiate(k.near, fixT, false); a.name = "LOD0"; a.GetComponent<MeshRenderer>().sharedMaterial = k.m;
                var lods = new List<LOD> { new LOD(0.3f, a.GetComponentsInChildren<Renderer>()) };
                tris += a.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0) / 3;
                if (k.far)
                {
                    var b = Object.Instantiate(k.far, fixT, false); b.name = "LOD1"; b.GetComponent<MeshRenderer>().sharedMaterial = k.mf;
                    lods.Add(new LOD(0.01f, b.GetComponentsInChildren<Renderer>())); trisFar += b.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0) / 3;
                }
                else lods[0] = new LOD(0.01f, lods[0].renderers);
                var lg = go.AddComponent<LODGroup>(); lg.SetLODs(lods.ToArray()); lg.RecalculateBounds();
                float err = BoxError(o, a.transform, Quaternion.identity, true); worstAll = Mathf.Max(worstAll, err);
                placed++;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            return $"rim rocks: {placed} of {list.Length} placed ({kit.Count(x => x.Value.near)} models), {tris} triangles near / {trisFar} far; largest difference between a placed rock's box and its box in the blockout {worstAll:0.00} m{sb}";
        }

        /// <summary>Largest difference (m) between the world box of a model placed for a blockout object and that object's own box.</summary>
        internal static float BoxError(BObj o, Transform model, Quaternion fix, bool placedAlready = false)
        {
            var mesh = model.GetComponent<MeshFilter>().sharedMesh; var mb = mesh.bounds;
            Matrix4x4 m;
            if (placedAlready) m = model.localToWorldMatrix;
            else { Place(o, out var pos, out var rot, out var scl); m = Matrix4x4.TRS(pos, rot, scl) * Matrix4x4.Rotate(fix) * Matrix4x4.TRS(model.localPosition, model.localRotation, model.localScale); }
            Vector3 lo = Vector3.one * float.MaxValue, hi = Vector3.one * float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var w = m.MultiplyPoint3x4(c); lo = Vector3.Min(lo, w); hi = Vector3.Max(hi, w);
            }
            var wantLo = new Vector3(o.min[0], o.min[2], o.min[1]); var wantHi = new Vector3(o.max[0], o.max[2], o.max[1]);
            var d1 = lo - wantLo; var d2 = hi - wantHi;
            return Mathf.Max(Mathf.Max(Mathf.Abs(d1.x), Mathf.Abs(d1.y), Mathf.Abs(d1.z)), Mathf.Max(Mathf.Abs(d2.x), Mathf.Abs(d2.y), Mathf.Abs(d2.z)));
        }
    }
}
