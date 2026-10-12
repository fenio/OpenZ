using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// clean chunky low-poly rifle built from bevel-free boxes in the old rifle's local frame: barrel on the RifleMuzzle
    /// line (-z forward), receiver, magazine, grip, stock and a small scope. Flat normals, one gunmetal material.
    /// </summary>
    public static class OpenZRifle
    {
        const string MeshPath = "Assets/OpenZ/Models/Generated/OZ_Rifle.asset";

        [MenuItem("OpenZ/Rebuild Robot Rifle")]
        public static string Build()
        {
            var parts = new List<(Vector3 c, Vector3 s)>
            {
                (new Vector3(0, 0.10f, -0.25f), new Vector3(0.15f, 0.19f, 0.62f)),     // receiver
                (new Vector3(0, 0.12f, -0.82f), new Vector3(0.075f, 0.075f, 0.60f)),   // barrel
                (new Vector3(0, 0.12f, -1.14f), new Vector3(0.10f, 0.10f, 0.06f)),     // muzzle brake
                (new Vector3(0, -0.07f, -0.36f), new Vector3(0.09f, 0.20f, 0.13f)),    // magazine
                (new Vector3(0, -0.03f, 0.00f), new Vector3(0.08f, 0.16f, 0.08f)),     // pistol grip (in the right hand)
                (new Vector3(0, 0.07f, 0.26f), new Vector3(0.11f, 0.17f, 0.30f)),      // stock
                (new Vector3(0, 0.25f, -0.27f), new Vector3(0.07f, 0.07f, 0.24f)),     // scope
                (new Vector3(0, 0.05f, -0.62f), new Vector3(0.11f, 0.08f, 0.22f)),     // handguard (left hand)
            };
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            foreach (var (c, s) in parts) Box(c, s * 0.5f, v, n, t);
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            bool fresh = !mesh; if (fresh) mesh = new Mesh { name = "OZ_Rifle" };
            mesh.Clear(); mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetTriangles(t, 0);
            var uv = new Vector2[v.Count]; mesh.uv = uv; mesh.RecalculateBounds();
            System.IO.Directory.CreateDirectory("Assets/OpenZ/Models/Generated");
            if (fresh) AssetDatabase.CreateAsset(mesh, MeshPath); else EditorUtility.SetDirty(mesh);

            int swapped = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/OpenZ/Prefabs", "Assets/OpenZ/Resources" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path); bool dirty = false;
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) if (mf.name == "Rifle_Heavy") { mf.sharedMesh = mesh; Sockets(mf.transform); dirty = true; swapped++; }
                if (dirty) PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
            }
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (mf.name == "Rifle_Heavy") { mf.sharedMesh = mesh; Sockets(mf.transform); EditorUtility.SetDirty(mf); swapped++; }
            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes(); AssetDatabase.SaveAssets();
            return $"rifle: {t.Count / 3} tris, swapped {swapped} renderers";
        }

        /// <summary>Grip / forward grip / muzzle sockets in the rifle's frame (used by WeaponHold and the muzzle flash).</summary>
        static void Sockets(Transform rifle)
        {
            void Socket(string n, Vector3 p) { var t = rifle.Find(n); if (!t) { t = new GameObject(n).transform; t.SetParent(rifle, false); } t.localPosition = p; t.localRotation = Quaternion.identity; t.localScale = Vector3.one; }
            Socket("Rifle_grip", Vector3.zero); Socket("Rifle_forward_grip", new Vector3(0, 0.02f, -0.62f)); Socket("RifleMuzzle", new Vector3(0, 0.12f, -1.18f));
        }

        static void Box(Vector3 c, Vector3 h, List<Vector3> v, List<Vector3> n, List<int> t)
        {
            var dirs = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var d in dirs)
            {
                var a = d.x != 0 ? Vector3.up : Vector3.right; var b = Vector3.Cross(d, a);
                var fc = c + Vector3.Scale(d, h); var ea = Vector3.Scale(a, h); var eb = Vector3.Scale(b, h);
                int i = v.Count;
                v.Add(fc - ea - eb); v.Add(fc + ea - eb); v.Add(fc + ea + eb); v.Add(fc - ea + eb);
                for (int k = 0; k < 4; k++) n.Add(d);
                // winding so the face normal Cross(b-a, c-a) points along d
                if (Vector3.Dot(Vector3.Cross(v[i + 1] - v[i], v[i + 2] - v[i]), d) > 0) t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                else t.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }
        }
    }
}
