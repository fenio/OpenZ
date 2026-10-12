using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// soak_hitch_fix: the wreck_teeth cut baked in the editor. WreckPieces.ClipSet re-cut a whole fragment set (every triangle of the
    /// set clipped by every bin it crosses, then squashed toward its core) on the main thread at the FIRST break of each wreck type;
    /// after the first L02 visit: 42 -> 61 entries, the mech thigh sets). This menu runs the very same code
    /// (WreckPieces.ClipSetForBake -> ClipSetCore) once per OZ_WreckFragments holder of every unit prefab OpenZWreckFragments bakes,
    /// saves each cut piece as a mesh asset under Models/Generated/WreckFragments/Clipped and stores the mapping (source fragment ->
    /// cut piece + core box + the SquashOuter used) in a WreckClipBaked component on the holder. The frag_ children keep their
    /// original meshes, so everything before the break (TeamPaint's accent bands, the tear-off) is untouched; at the break Chunkify
    /// takes the stored piece: the same mesh data and the same core box the runtime cut cached, without the cut.
    /// Wheel-strip types (WheelSplit registers them at runtime) swap their fragments for tyre-less copies before the cut: those copies
    /// are never in the stored list, so such sets keep the runtime cut exactly as before.
    /// Idempotent: a rebake overwrites the clipped assets in place (same GUIDs) and replaces the component.
    /// </summary>
    public static class OpenZWreckClipBake
    {
        const string FragDir = "Assets/OpenZ/Models/Generated/WreckFragments";
        const string OutDir = FragDir + "/Clipped";
        static readonly string[] Types = { "apc", "crane", "jeep", "light", "medium", "heavy", "missile_launcher", "gatling", "gun", "howitzer", "missile_cannon" };

        [MenuItem("OpenZ/Wreck/Rebake clipped fragments")]
        public static void RebakeAll()
        {
            var log = new List<string>();
            foreach (var t in Types)
            {
                string path = $"Assets/OpenZ/Prefabs/Z_{t}.prefab";
                if (!File.Exists(path)) { log.Add(t + ": no prefab"); continue; }
                log.Add(t + ": " + ClipPrefab(path, t));
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[OpenZ Wreck] rebake clipped fragments (SquashOuter " + WreckPieces.SquashOuter.ToString("0.###") + "):\n" + string.Join("\n", log));
        }

        [MenuItem("OpenZ/Wreck/Check clipped fragments")]
        public static void CheckAll()
        {
            var log = new List<string>();
            foreach (var t in Types)
            {
                string path = $"Assets/OpenZ/Prefabs/Z_{t}.prefab";
                var root = File.Exists(path) ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
                if (!root) { log.Add(t + ": no prefab"); continue; }
                int holders = 0, baked = 0;
                foreach (var h in root.GetComponentsInChildren<Transform>(true).Where(x => x.name == "OZ_WreckFragments"))
                {
                    holders++;
                    if (WreckPieces.IsBaked(h)) baked++;
                }
                log.Add($"{t}: {baked} of {holders} holder(s) baked");
            }
            Debug.Log("[OpenZ Wreck] clipped fragments check:\n" + string.Join("\n", log));
        }

        /// <summary>Cuts every OZ_WreckFragments holder of the prefab at `path` and stores the result. Returns a one-line summary.</summary>
        public static string ClipPrefab(string path, string type)
        {
            if (WheelSplit.Strips(type)) return "skipped (wheel strip type: its fragments are swapped for tyre-less copies before the cut, the runtime cut stays)";
            if (!AssetDatabase.IsValidFolder(OutDir)) { Directory.CreateDirectory(OutDir); AssetDatabase.Refresh(); }
            var root = PrefabUtility.LoadPrefabContents(path);
            int sets = 0, pieces = 0, empty = 0, skipped = 0, tris = 0;
            try
            {
                var src = new List<Mesh>(); var cut = new List<Mesh>(); var boxes = new List<Bounds>();
                foreach (var holder in root.GetComponentsInChildren<Transform>(true).Where(x => x.name == "OZ_WreckFragments").ToList())
                {
                    foreach (var old in holder.GetComponents<WreckClipBaked>()) Object.DestroyImmediate(old, true);
                    if (!WreckPieces.ClipSetForBake(holder, src, cut, boxes)) { skipped++; continue; }   // the runtime leaves such a set to the per-piece cut too
                    var saved = new Mesh[cut.Count];
                    bool ok = true;
                    for (int i = 0; i < cut.Count && ok; i++)
                    {
                        if (!cut[i]) { empty++; continue; }
                        string sp = AssetDatabase.GetAssetPath(src[i]);
                        if (string.IsNullOrEmpty(sp) || !sp.EndsWith(".asset")) { ok = false; break; }   // not a baked fragment asset: leave the set to the runtime
                        string ap = $"{OutDir}/{Path.GetFileNameWithoutExtension(sp)}_clip.asset";
                        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(ap);
                        if (existing) { EditorUtility.CopySerialized(cut[i], existing); existing.name = cut[i].name; EditorUtility.SetDirty(existing); saved[i] = existing; Object.DestroyImmediate(cut[i]); }
                        else { AssetDatabase.CreateAsset(cut[i], ap); saved[i] = cut[i]; }
                        pieces++; for (int s = 0; s < saved[i].subMeshCount; s++) tris += (int)saved[i].GetIndexCount(s) / 3;
                    }
                    if (!ok) { skipped++; continue; }
                    var bk = holder.gameObject.AddComponent<WreckClipBaked>();
                    bk.squashOuter = WreckPieces.SquashOuter;
                    bk.src = src.ToArray(); bk.cut = saved; bk.box = boxes.ToArray();
                    var pmf = holder.parent ? holder.parent.GetComponent<MeshFilter>() : null; bk.model = pmf ? pmf.sharedMesh : null;   // the bins' source bounds
                    bk.note = $"soak_hitch_fix bake: {src.Count} fragments of {(holder.parent ? holder.parent.name : "?")} cut at the bin walls ({WreckPieces.ClipLast})";
                    sets++;
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return $"{sets} set(s) baked, {pieces} piece(s), {tris} tris, {empty} empty piece(s), {skipped} holder(s) left to the runtime";
            }
            catch (System.Exception e) { return "FAILED: " + e.GetType().Name + ": " + e.Message; }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
