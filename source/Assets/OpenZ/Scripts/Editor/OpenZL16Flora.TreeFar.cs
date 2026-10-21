using System.Collections.Generic;
using System.IO;
using OpenZ.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// DV88 (the trees that close a tile can be felled): where each breakable tree lies inside the joined far mesh of its 32 m
    /// cell. Menu 8 joined the far shapes of a cell in the order of the plant list, so a plant's points are one run in that mesh:
    /// start = the sum of the far shapes' point counts of the plants before it in its cell. This writes the runs of every
    /// tree-family plant that stands on a tree tile of the rules (ZTreeTilesData) to Resources/OpenZ/Maps/L16_tree_far.json; the
    /// game folds a felled tree's run onto one point (View/OzTreeFall.cs), so the far version of the cell stops showing it and
    /// nothing more is drawn than before. Each run is proved against the open scene before it is written: the cell's far mesh
    /// has exactly as many points as the list gives, and the first and last point of the run are the far shape's own, placed
    /// like the plant. A run that fails the proof is left out (the game then keeps that cell on its near objects once its tree
    /// fell). The scene and the far meshes are not changed; nothing has to be saved.
    /// </summary>
    public static partial class OpenZL16Flora
    {
        const string TreeFarPath = "Assets/OpenZ/Resources/OpenZ/Maps/L16_tree_far.json";
        [System.Serializable] class TreeFarDoc { public string[] names; public int[] start, count; }

        [MenuItem("OpenZ/L16 Jungle/8b Breakable trees: their runs in the far meshes (open level 16 scene, after menu 8)")]
        public static void TreeFarMenu() => Debug.Log("[OpenZ L16] " + TreeFar());

        public static string TreeFar()
        {
            if (Application.isPlaying) return "leave Play mode first";
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.name.StartsWith("L16")) return "open the level 16 scene first";
            var env = GameObject.Find("Environment"); var root = env ? env.transform.Find("L16_Flora") : null;
            if (!root) return "L16_Flora missing: menu 8 first";
            if (!File.Exists(PlacesPath)) return "list missing: " + PlacesPath;
            var doc = JsonUtility.FromJson<Doc>(File.ReadAllText(PlacesPath));
            var kit = AssetDatabase.LoadAssetAtPath<GameObject>(Kit); if (!kit) return "missing " + Kit;
            var shape = new Dictionary<string, Transform>(); foreach (Transform t in kit.transform) shape[t.name] = t;
            var set = ZTreeTilesData.L16; var tiles = new HashSet<int>();
            for (int i = 0; i + 1 < set.Tiles.Length; i += 2) tiles.Add(set.Tiles[i + 1] * 1000 + set.Tiles[i]);

            // every plant in the order menu 8 joined the far shapes of its cell in
            var next = new Dictionary<string, int>(); var rows = new List<(Row o, string cell, int start, Transform far)>();
            foreach (var o in doc.objects)
            {
                if (!shape.ContainsKey(o.data) || !shape.TryGetValue(o.data + "_far", out var fp)) continue;
                string key = $"{o.family}_{o.cell[0]}_{o.cell[1]}"; next.TryGetValue(key, out int at);
                if (o.family == "T" && tiles.Contains(Mathf.FloorToInt(-o.loc[1] * 0.5f) * 1000 + Mathf.FloorToInt(o.loc[0] * 0.5f))) rows.Add((o, key, at, fp));
                next[key] = at + fp.GetComponent<MeshFilter>().sharedMesh.vertexCount;
            }

            var names = new List<string>(); var start = new List<int>(); var count = new List<int>(); var cells = new HashSet<string>(); var bad = new List<string>();
            foreach (var r in rows)
            {
                var cell = root.Find(r.cell); var ft = cell ? cell.Find("FAR") : null; var fmf = ft ? ft.GetComponent<MeshFilter>() : null; var fm = fmf ? fmf.sharedMesh : null;
                var shapeMesh = r.far.GetComponent<MeshFilter>().sharedMesh; int n = shapeMesh.vertexCount;
                string why = null;
                if (!fm) why = "no far mesh in cell " + r.cell;
                else if (n <= 0) why = "its far shape is empty";
                else if (fm.vertexCount != next[r.cell]) why = $"cell {r.cell}: its far mesh has {fm.vertexCount} points, the list gives {next[r.cell]}";
                else if (!fm.isReadable || !shapeMesh.isReadable) why = "mesh not readable";
                else
                {
                    var m = PlaceMatrix(r.o, out _, out _, out _) * Matrix4x4.TRS(r.far.localPosition, r.far.localRotation, r.far.localScale);
                    var fv = fm.vertices; var sv = shapeMesh.vertices; float worst = 0f;
                    foreach (int j in new[] { 0, n / 2, n - 1 }) worst = Mathf.Max(worst, (ft.TransformPoint(fv[r.start + j]) - m.MultiplyPoint3x4(sv[j])).magnitude);
                    if (worst > 0.02f) why = $"its run does not lie on the plant ({worst:0.00} m off)";
                }
                if (why != null) { bad.Add(r.o.name + ": " + why); continue; }
                names.Add(r.o.name); start.Add(r.start); count.Add(n); cells.Add(r.cell);
            }
            File.WriteAllText(TreeFarPath, JsonUtility.ToJson(new TreeFarDoc { names = names.ToArray(), start = start.ToArray(), count = count.ToArray() }));
            AssetDatabase.ImportAsset(TreeFarPath);
            int main = 0; var have = new HashSet<string>(names); foreach (var u in set.Uids) if (have.Contains(u)) main++;
            return $"breakable trees: the far runs of {names.Count} of {rows.Count} tree-family plants on the {set.Uids.Length} tree tiles written to {TreeFarPath} ({main} of the {set.Uids.Length} trees of the rules among them, {cells.Count} cells)" +
                   (bad.Count > 0 ? $"; LEFT OUT {bad.Count}: " + string.Join("; ", bad.GetRange(0, Mathf.Min(6, bad.Count)).ToArray()) : "");
        }
    }
}
