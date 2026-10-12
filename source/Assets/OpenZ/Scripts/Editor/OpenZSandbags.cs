using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// VX64: builds the sandbag emplacements of the open level (L02_Desert / L17_CarPark) from
    /// non-walkable Zod tiles, clear of every building model, DEC prop, cliff, rock, unit and door lane, flat ground).
    /// Idempotent: Environment/Sandbags is deleted and rebuilt, the meshes are regenerated into
    /// Assets/OpenZ/Models/Sandbags/Sandbags_Lxx.asset. Each wall segment = DEC_Sandbag_Lxx_NN with
    ///   Intact (merged bags, 2-3 rows, every bottom bag seated on the ground by a raycast) and Rubble (inactive: slumped
    ///   bags + spilled sand), broken at run time by SandbagBreakables (on the Sandbags group).
    /// low-poly bag, #a99969 canvas), rebuilt as a chunkier pillow bag in vertex colours on DV3_BatchedPalette (the DEC
    /// props' clean flat-shaded TeamLit material). The canvas stays below the olive hue band, so TeamLit never recolours it.
    /// </summary>
    public static class OpenZSandbags
    {
        const string Dir = "Assets/OpenZ/Models/Sandbags";
        const string MatPath = "Assets/OpenZ/Materials/Env/DV3_BatchedPalette.mat";
        const float BagL = 0.80f, BagW = 0.50f, BagH = 0.25f, Pitch = 0.74f, RowRise = 0.20f;

        [System.Serializable] class Seg { public string name, anchor, team; public float x, y, z, yaw, length, ground_range; public int bags, rows; }
        [System.Serializable] class Doc { public string level; public List<Seg> segments; }

        [MenuItem("OpenZ/Sandbags/Old v1 generator (rejected look)/Place (open level)")]
        public static void Place()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[OpenZ] Sandbags: stop Play first"); return; }
            var scene = EditorSceneManager.GetActiveScene();
            string level = scene.name.Contains("L02") ? "L02" : scene.name.Contains("L17") ? "L17" : null;
            if (level == null) { Debug.LogError("[OpenZ] Sandbags: open L02_Desert or L17_CarPark"); return; }
            var ta = AssetDatabase.LoadAssetAtPath<TextAsset>($"{Dir}/{level}_sandbags.json");
            if (!ta) { Debug.LogError($"[OpenZ] Sandbags: {Dir}/{level}_sandbags.json missing (README_IMPORT step 1)"); return; }
            var doc = JsonUtility.FromJson<Doc>(ta.text);
            var env = GameObject.Find("Environment");
            if (!env) { Debug.LogError("[OpenZ] Sandbags: no Environment in the scene"); return; }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (!mat) { Debug.LogError("[OpenZ] Sandbags: " + MatPath + " missing"); return; }

            // idempotent: remove the old group and the old mesh asset
            var old = env.transform.Find("Sandbags");
            if (old) Object.DestroyImmediate(old.gameObject);
            string assetPath = $"{Dir}/Sandbags_{level}.asset";
            if (AssetDatabase.LoadMainAssetAtPath(assetPath)) AssetDatabase.DeleteAsset(assetPath);

            bool city = level == "L17";
            Color canvas = city ? new Color32(162, 152, 136, 255) : new Color32(178, 158, 124, 255);
            Color sand = city ? new Color32(146, 134, 116, 255) : new Color32(201, 170, 122, 255);
            var bag = BagMesh(canvas); bag.name = "SandbagBag_" + level;
            AssetDatabase.CreateAsset(bag, assetPath);

            var group = new GameObject("Sandbags");
            group.transform.SetParent(env.transform, false);
            group.transform.position = Vector3.zero; group.transform.rotation = Quaternion.identity; group.transform.localScale = Vector3.one;
            Physics.SyncTransforms();

            var decBoxes = new List<(string, Bounds)>();
            foreach (Transform t in env.transform)
            {
                if (!t.name.StartsWith("DEC_") || !t.gameObject.activeInHierarchy) continue;
                var rs = t.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                if (b.size.x > 7f || b.size.z > 7f) continue;          // big islands / ridges: the offline raster already checked their real shape
                decBoxes.Add((t.name, b));
            }

            int made = 0, bagsTotal = 0, warn = 0, hitGround = 0, probes = 0;
            for (int si = 0; si < doc.segments.Count; si++)
            {
                var s = doc.segments[si];
                var rng = new System.Random((city ? 17 : 2) * 100003 + si * 7919);   // deterministic: same bags every rebuild
                var go = new GameObject(s.name);
                go.transform.SetParent(group.transform, false);
                go.transform.position = new Vector3(s.x, s.y, s.z);
                go.transform.rotation = Quaternion.Euler(0, s.yaw, 0);
                var inv = go.transform.worldToLocalMatrix;

                // bottom row seated bag by bag on the ground (raycast on the terrain colliders), upper rows on the bags below
                int n = s.bags, rows = Mathf.Clamp(s.rows, 1, 3);
                var baseY = new float[n];
                for (int k = 0; k < n; k++)
                {
                    float lx = (k - (n - 1) * 0.5f) * Pitch;
                    var wp = go.transform.TransformPoint(new Vector3(lx, 0, 0));
                    probes++;
                    if (GroundAt(wp, s.y, out float gy)) { hitGround++; baseY[k] = gy - s.y; } else baseY[k] = 0;
                }
                var V = new List<Vector3>(); var C = new List<Color>(); var T = new List<int>();
                var RV = new List<Vector3>(); var RC = new List<Color>(); var RT = new List<int>();
                var rowY = new List<float[]>();
                for (int r = 0; r < rows; r++)
                {
                    int cnt = n - r; if (cnt < 1) break;
                    var ys = new float[cnt];
                    for (int k = 0; k < cnt; k++)
                    {
                        float lx = (k - (cnt - 1) * 0.5f) * Pitch;
                        // bag centres: the bottom row sinks 1.3 cm into the ground (bag underside is 0.088 below its centre),
                        // every upper bag rests on the higher of the two below it and squashes into them by ~3 cm
                        float y = r == 0 ? baseY[k] + 0.075f : Mathf.Max(rowY[r - 1][k], rowY[r - 1][k + 1]) + RowRise;
                        ys[k] = y;
                        var pos = new Vector3(lx + R(rng, 0.03f), y, R(rng, 0.03f));
                        var rot = Quaternion.Euler(R(rng, 3f), R(rng, 5f), R(rng, 3f));
                        var scl = new Vector3(1f + R(rng, 0.05f), 1f + R(rng, 0.08f), 1f + R(rng, 0.05f));
                        float tone = 0.93f + (float)rng.NextDouble() * 0.12f;
                        var tint = new Color(tone, tone * (0.99f + R(rng, 0.015f)), tone * (0.98f + R(rng, 0.03f)));
                        Append(bag, Matrix4x4.TRS(pos, rot, scl), tint, V, C, T);
                        bagsTotal++;
                        if (r == 0)
                        {   // rubble: the bottom bags slumped flat + turned, darker (wet sand spilled out)
                            var rp = new Vector3(lx + R(rng, 0.15f), baseY[k] + BagH * 0.16f, R(rng, 0.12f));
                            var rr = Quaternion.Euler(R(rng, 4f), R(rng, 28f), R(rng, 4f));
                            Append(bag, Matrix4x4.TRS(rp, rr, new Vector3(1.1f, 0.42f, 1.2f)), tint * 0.86f, RV, RC, RT);
                        }
                    }
                    rowY.Add(ys);
                }
                // rubble sand spill: a low mound along the wall, pushed a little outward
                float avg = 0; foreach (var b in baseY) avg += b; avg /= n;
                AppendMound(new Vector3(0, avg + 0.02f, 0), new Vector3(s.length * 0.62f + 0.35f, 0.11f, 0.62f), sand, RV, RC, RT);

                var intact = new GameObject("Intact"); intact.transform.SetParent(go.transform, false);
                var im = Build(s.name + "_intact", V, C, T); AssetDatabase.AddObjectToAsset(im, assetPath);
                intact.AddComponent<MeshFilter>().sharedMesh = im;
                var imr = intact.AddComponent<MeshRenderer>(); imr.sharedMaterial = mat;
                var rubble = new GameObject("Rubble"); rubble.transform.SetParent(go.transform, false);
                var rm = Build(s.name + "_rubble", RV, RC, RT); AssetDatabase.AddObjectToAsset(rm, assetPath);
                rubble.AddComponent<MeshFilter>().sharedMesh = rm;
                rubble.AddComponent<MeshRenderer>().sharedMaterial = mat;
                rubble.SetActive(false);
                GameObjectUtility.SetStaticEditorFlags(intact, 0);           // it disappears when broken: never static-batched
                made++;

                // editor-side clearance report against the live DEC props (warn only: the plan checked real mesh shapes)
                var sb = imr.bounds; sb.Expand(new Vector3(-0.08f, -0.02f, -0.08f));
                foreach (var (dn, db) in decBoxes)
                    if (db.Intersects(sb)) { warn++; Debug.LogWarning($"[OpenZ] Sandbags: {s.name} box touches {dn} (check in the Scene view)", go); }
            }

            var br = group.AddComponent<SandbagBreakables>();
            br.bagMesh = bag; br.material = mat; br.canvas = canvas;

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();
            Debug.Log($"[OpenZ] Sandbags {level}: {made} segments, {bagsTotal} bags, ground hits {hitGround}/{probes}, DEC box warnings {warn}. Saved.");
        }

        [MenuItem("OpenZ/Sandbags/Remove (open level)")]
        public static void Remove()
        {
            var env = GameObject.Find("Environment"); if (!env) return;
            var old = env.transform.Find("Sandbags"); if (!old) return;
            Object.DestroyImmediate(old.gameObject);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("[OpenZ] Sandbags removed (the mesh asset stays until the next Place)");
        }

        static float R(System.Random r, float a) => ((float)r.NextDouble() * 2f - 1f) * a;

        /// <summary>Highest terrain hit under p (COL_Terrain or any Environment collider, never units / buildings).</summary>
        static bool GroundAt(Vector3 p, float planY, out float y)
        {
            y = planY;
            var hits = Physics.RaycastAll(new Vector3(p.x, planY + 4f, p.z), Vector3.down, 9f, ~0, QueryTriggerInteraction.Ignore);
            bool found = false; float best = float.NegativeInfinity;
            foreach (var h in hits)
            {
                var t = h.collider.transform; bool terrain = false;
                for (var q = t; q; q = q.parent) { if (q.name == "COL_Terrain" || q.name == "Environment") { terrain = true; break; } if (q.name == "ZodObjects" || q.name == "Sandbags") break; }
                if (!terrain || h.point.y <= best) continue;
                best = h.point.y; found = true;
            }
            if (found && Mathf.Abs(best - planY) < 0.6f) { y = best; return true; }
            return false;
        }

        /// <summary>Pillow bag: a superellipsoid (boxy in plan, rounded in section), flat underside, pinched tied ends;
        /// darker underside baked into the vertex colours (soft contact shade). ~100 triangles.</summary>
        static Mesh BagMesh(Color canvas)
        {
            const int Rings = 6, Segs = 10;
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            float hx = BagL * 0.5f, hy = BagH * 0.5f, hz = BagW * 0.5f;
            for (int j = 0; j <= Rings; j++)
            {
                float th = -Mathf.PI / 2 + Mathf.PI * j / Rings;
                for (int i = 0; i < Segs; i++)
                {
                    float ph = Mathf.PI * 2 * i / Segs + Mathf.PI / Segs;
                    float cx = Mathf.Cos(th) * Mathf.Cos(ph), cy = Mathf.Sin(th), cz = Mathf.Cos(th) * Mathf.Sin(ph);
                    float x = SP(cx, 0.45f) * hx, y = SP(cy, 0.6f) * hy, z = SP(cz, 0.45f) * hz;
                    float endK = Mathf.Pow(Mathf.Abs(x / hx), 4f);
                    y *= 1f - 0.35f * endK; z *= 1f - 0.2f * endK;             // tied ends pinch
                    if (y < 0) y *= 0.7f;                                     // it sits flat
                    else y *= 1f + 0.12f * (1f - Mathf.Abs(x / hx));          // bulging top
                    v.Add(new Vector3(x, y, z));
                    float shade = Mathf.Lerp(0.72f, 1.04f, Mathf.Clamp01((y / hy + 0.7f) / 1.6f));
                    var col = canvas * shade; col.a = 1; c.Add(col);
                }
            }
            for (int j = 0; j < Rings; j++)
                for (int i = 0; i < Segs; i++)
                {
                    int a = j * Segs + i, b = j * Segs + (i + 1) % Segs, d = a + Segs, e = b + Segs;
                    t.Add(a); t.Add(d); t.Add(b); t.Add(b); t.Add(d); t.Add(e);
                }
            return Build("SandbagBag", v, c, t);
        }

        static float SP(float c, float e) => Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), e);

        static void Append(Mesh src, Matrix4x4 m, Color tint, List<Vector3> V, List<Color> C, List<int> T)
        {
            int b = V.Count; var sv = src.vertices; var sc = src.colors; var st = src.triangles;
            for (int i = 0; i < sv.Length; i++)
            {
                V.Add(m.MultiplyPoint3x4(sv[i]));
                var col = sc.Length == sv.Length ? sc[i] : Color.white;
                C.Add(new Color(col.r * tint.r, col.g * tint.g, col.b * tint.b, 1));
            }
            foreach (var i in st) T.Add(b + i);
        }

        /// <summary>Low spilled-sand mound (flattened ellipsoid cap, 12 x 3), for the rubble.</summary>
        static void AppendMound(Vector3 c, Vector3 size, Color sand, List<Vector3> V, List<Color> C, List<int> T)
        {
            const int S = 12, Rr = 3; int b = V.Count;
            V.Add(c + Vector3.up * size.y); C.Add(sand);
            for (int j = 1; j <= Rr; j++)
            {
                float r = j / (float)Rr, h = size.y * (1 - r * r);
                for (int i = 0; i < S; i++)
                {
                    float a = Mathf.PI * 2 * i / S;
                    V.Add(c + new Vector3(Mathf.Cos(a) * r * size.x * 0.5f, h - 0.03f * (j == Rr ? 1 : 0), Mathf.Sin(a) * r * size.z * 0.5f));
                    C.Add(sand * (0.9f + 0.1f * (1 - r)));
                }
            }
            for (int i = 0; i < S; i++) { T.Add(b); T.Add(b + 1 + (i + 1) % S); T.Add(b + 1 + i); }          // outward (up) faces
            for (int j = 1; j < Rr; j++)
                for (int i = 0; i < S; i++)
                {
                    int a0 = b + 1 + (j - 1) * S + i, a1 = b + 1 + (j - 1) * S + (i + 1) % S, b0 = a0 + S, b1 = a1 + S;
                    T.Add(a0); T.Add(a1); T.Add(b0); T.Add(a1); T.Add(b1); T.Add(b0);
                }
        }

        static Mesh Build(string name, List<Vector3> v, List<Color> c, List<int> t)
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
