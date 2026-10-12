using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The imported gatling's chair turns with the turret over a fixed square sandbag frame
    /// (0.6-0.73 m high, a 1.21 m post at 1.17 m from the pivot), and a BS-01 crew in the seated_gunner pose (feet 0.76 m under
    /// on that chair without his legs in the sandbags at every turret angle (the old chair: 36 cm deep at the worst angle).
    /// A search over stations (tools/gatling_seat_fine.py: real leg capsules, 72 turret angles, surface samples of the static
    /// base) found the nearest clear one: hips at (-2.25, 1.72, 1.301) in prefab-root space, 0.4 m higher and 0.95 m behind /
    /// beside the old chair, clear of the base by >= 8 cm at every angle, nothing of the turret in his body.
    /// This builds the seat there, a gunmetal outrigger saddle on the turret: seat pan under the hips (top 0.12 m under them,
    /// like the old chair), a backrest and a strut to the turret's rear (clear of his legs by >= 3 cm). It is a child of the
    /// turret yaw node, so it turns with the gun like its gunner. View only.
    /// </summary>
    public static class GunnerSaddle
    {
        /// <summary>Gatling saddle parts relative to the hips (prefab-root space, turret at rest, gun front = +x).</summary>
        static readonly Vector3 PanCentre = new Vector3(0f, -0.16f, 0f), PanSize = new Vector3(0.44f, 0.08f, 0.40f);
        static readonly Vector3 BackCentre = new Vector3(-0.27f, 0.06f, 0f), BackSize = new Vector3(0.06f, 0.36f, 0.38f);
        static readonly Vector3 StrutFrom = new Vector3(0.10f, -0.20f, 0f), StrutTo = new Vector3(0.437f, -0.398f, -0.217f);
        const float StrutHalf = 0.035f;
        public const string NodeName = "OZ_GunnerSaddle";
        static Material mat;

        /// <summary>Builds the saddle under `turret` (once). hipsRoot = the gunner's hips in prefab-root space at the turret's rest
        /// pose; rootToTurret = inverse of the turret's rest matrix in root space (BoardingView.CarrySeat).</summary>
        public static void Build(Transform turret, Matrix4x4 rootToTurret, Vector3 hipsRoot)
        {
            if (!turret || turret.Find(NodeName)) return;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            void Box(Vector3 c, Vector3 ax, Vector3 ay, Vector3 az, Vector3 half)
            {
                // 6 faces, flat normals, in root space -> turret space
                Vector3[] dirs = { ax, -ax, ay, -ay, az, -az };
                foreach (var f in dirs)
                {
                    Vector3 u, w; float hu, hw, hf;
                    if (f == ax || f == -ax) { u = ay; w = az; hu = half.y; hw = half.z; hf = half.x; }
                    else if (f == ay || f == -ay) { u = az; w = ax; hu = half.z; hw = half.x; hf = half.y; }
                    else { u = ax; w = ay; hu = half.x; hw = half.y; hf = half.z; }
                    var fc = c + f * hf; int s = v.Count;
                    var corners = new[] { fc - u * hu - w * hw, fc + u * hu - w * hw, fc + u * hu + w * hw, fc - u * hu + w * hw };
                    foreach (var p in corners) { v.Add(rootToTurret.MultiplyPoint3x4(p)); n.Add(rootToTurret.MultiplyVector(f).normalized); }
                    // winding toward the face normal
                    var nn = Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]);
                    if (Vector3.Dot(nn, f) > 0f) t.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
                    else t.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
                }
            }
            Box(hipsRoot + PanCentre, Vector3.right, Vector3.up, Vector3.forward, PanSize * 0.5f);
            Box(hipsRoot + BackCentre, Vector3.right, Vector3.up, Vector3.forward, BackSize * 0.5f);
            var a = hipsRoot + StrutFrom; var b = hipsRoot + StrutTo; var ax2 = (b - a).normalized;
            var side = Vector3.Cross(ax2, Vector3.up).normalized; var up2 = Vector3.Cross(side, ax2).normalized;
            Box((a + b) * 0.5f, ax2, up2, side, new Vector3((b - a).magnitude * 0.5f + StrutHalf, StrutHalf, StrutHalf));
            var m = new Mesh { name = "OZ_GunnerSaddle" };
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds();
            var go = new GameObject(NodeName);
            go.transform.SetParent(turret, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = Material();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; mr.receiveShadows = true;
        }

        static Material Material()
        {
            if (mat) return mat;
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (!sh) sh = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (!sh) sh = Shader.Find("Standard");
            mat = new Material(sh) { name = "OZ_GunnerSaddle (runtime)" };
            var col = new Color(0.40f, 0.41f, 0.43f);                                                      // gunmetal, like the tower mounts
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.45f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.4f);
            return mat;
        }
    }
}
