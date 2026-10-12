using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// endings_v1: bakes every building's ruin + big parts (BuildingRuins.Build, the same code the game runs) into
    /// Resources/OpenZ/Fractures/R_&lt;key&gt;_meshes.asset. The game prefers these baked meshes, so the player build never needs
    /// readable models (the imported GLB meshes are not Read/Write; the fort FBX is). Run once after a building model changes.
    /// </summary>
    public static class OpenZRuinBake
    {
        const string Prefabs = "Assets/OpenZ/Prefabs", OutDir = "Assets/OpenZ/Resources/OpenZ/Fractures";
        static readonly string[] Keys = { "fort", "robot_factory", "vehicle_factory", "repair", "radar" };

        [MenuItem("OpenZ/Bake Building Ruins")]
        public static string BakeAll()
        {
            System.IO.Directory.CreateDirectory(OutDir);
            var log = new List<string>();
            foreach (var key in Keys) log.Add(Bake(key));
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            var msg = string.Join("\n", log);
            Debug.Log("[OpenZ] Bake Building Ruins\n" + msg);
            return msg;
        }

        static string Bake(string key)
        {
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/Z_{key}.prefab");
            if (!pf) return key + ": no prefab";
            var mr = BuildingRuins.MainRenderer(pf.transform);
            var mf = mr ? mr.GetComponent<MeshFilter>() : null;
            if (!mf || !mf.sharedMesh) return key + ": no main mesh";
            var set = BuildingRuins.Build(mf.sharedMesh, key);
            if (set == null || !set.ruin) return key + ": cut failed";
            string path = $"{OutDir}/R_{key}_meshes.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(set.ruin, path);
            foreach (var p in set.parts) AssetDatabase.AddObjectToAsset(p, path);
            return $"{key}: ruin {set.ruin.triangles.Length / 3} tris (double-sided), {set.parts.Count} big parts from {mf.sharedMesh.name} ({mr.name})";
        }
    }
}
