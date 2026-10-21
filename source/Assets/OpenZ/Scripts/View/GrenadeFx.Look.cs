using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// Which model the grenade is drawn with.
    /// One switch for every grenade GrenadeFx draws: held in the hand through the wind-up, thrown, and the ones a destroyed crate throws.
    ///   0 = the old grenade (grenade.glb baked into vertex colours, kept unchanged for safety)
    ///   1 = the pineapple grenade (segmented olive body, grey cap, ring and lever), Resources/OpenZ/FX/grenade_v1_pineapple
    ///   2 = the round grenade with the yellow-orange band (olive ball, grey cap, ring and lever), Resources/OpenZ/FX/grenade_v2_round_band
    /// View only: the throw, the release frame, the flight arc, the random start turn and the spin (rot.x 7, rot.z 3 rad/s), the timing, the
    /// trail, the fuse light and every sound stay exactly as they are. The new models are as big on screen as the old one while they tumble
    /// (the same mean silhouette: the old fat tube is 0.45 x 0.50 x 0.49 m, the slimmer pineapple 0.38 x 0.71 x 0.53 m, the round one
    /// 0.46 x 0.64 x 0.53 m), centred on their bounds like the old one, fuse head up (+Y), lever and spoon on +Z where the old lever was,
    /// the ring on +X; the fuse spark sits 0.02 m over the new fuse head as it did over the old one.
    /// Drawn with the old grenade's own material (OpenZ/TeamLit, no team colour, stylized, the same smoothness and metal), its colour from
    /// the model's texture instead of vertex colours, smooth shaded (<see cref="Smooth"/>; false = flat facets like the old one).
    /// Editor menu: OpenZ/V1.1/Grenade look (works in Play: the next frame draws the chosen look).
    /// </summary>
    public static class OzGrenadeLook
    {
        public const int Old = 0, Pineapple = 1, RoundBand = 2;
        /// <summary>The look the game draws: 0 the old grenade (default until the owner picks), 1 pineapple, 2 round band.</summary>
        public static int Variant = Old;
        /// <summary>Smooth shading on the new models (the owner's picture is a smooth cartoon). False = one normal per face, like the old one.</summary>
        public static bool Smooth = true;
        public static readonly string[] Names = { "old grenade", "pineapple", "round band" };
        public static readonly string[] Models = { null, "OpenZ/FX/grenade_v1_pineapple", "OpenZ/FX/grenade_v2_round_band" };
        public static readonly string[] Textures = { null, "OpenZ/FX/grenade_v1_pineapple_color", "OpenZ/FX/grenade_v2_round_band_color" };
        /// <summary>The drawn height (m) each look is made for (the old: GrenadeFx.Size); a model drawn otherwise is reported (its import changed).</summary>
        public static readonly float[] Heights = { 0.5f, 0.711f, 0.643f };
        /// <summary>The look GrenadeFx draws now (-1 before the grenade view is up; 0 also when a new look could not load).</summary>
        public static int Drawn { get; internal set; } = -1;
        /// <summary>One line about the drawn look: its mesh, triangles, drawn size, the fuse spark's place; or why a new look fell back.</summary>
        public static string Report { get; internal set; } = "";
        /// <summary>How many times the drawn look changed (the menu's proof count).</summary>
        public static int Switches { get; internal set; }
    }

    public partial class GrenadeFx
    {
        // K350: the looks (0 = the mesh and material Awake made, kept as they are); lookTip = the fuse spark's height over the centre in
        // units of Size (the old grenade: its unit-height top 0.5 + 0.04 = 0.54, the old constant)
        Mesh lookOldBody; Material lookOldMat; int lookShown = -1; bool lookSmoothShown = true; float lookTip = 0.54f;
        readonly Mesh[] lookMesh = new Mesh[3]; readonly Material[] lookMat = new Material[3]; readonly float[] lookTips = { 0.54f, 0.54f, 0.54f };
        readonly bool[] lookTried = new bool[3]; readonly string[] lookWhy = new string[3];

        /// <summary>LateUpdate, before the grenades are drawn: puts the chosen look's mesh and material in place when the choice changed.</summary>
        void LookTick()
        {
            if (!bodyMat) return;
            int want = Mathf.Clamp(OzGrenadeLook.Variant, 0, 2);
            bool smooth = OzGrenadeLook.Smooth;
            if (want == lookShown && smooth == lookSmoothShown) return;
            if (lookShown < 0) { lookOldBody = body; lookOldMat = bodyMat; }
            if (want > 0 && !lookTried[want]) { lookTried[want] = true; MakeLook(want); }
            for (int i = 1; i < 3; i++) if (lookMat[i]) lookMat[i].SetFloat("_FlatShade", smooth ? 0f : 1f);
            int drawn = want > 0 && lookMesh[want] && lookMat[want] ? want : 0;
            body = drawn > 0 ? lookMesh[drawn] : lookOldBody; bodyMat = drawn > 0 ? lookMat[drawn] : lookOldMat; lookTip = lookTips[drawn];
            if (drawn != OzGrenadeLook.Drawn) OzGrenadeLook.Switches++;
            OzGrenadeLook.Drawn = drawn;
            var sz = body ? body.bounds.size * Size : Vector3.zero;
            OzGrenadeLook.Report = $"grenade look {drawn} ({OzGrenadeLook.Names[drawn]}{(drawn > 0 ? (smooth ? ", smooth" : ", flat facets") : "")}): mesh {(body ? body.name : "-")}, " +
                                   $"{(body ? body.triangles.Length / 3 : 0)} triangles, drawn {sz.x:F3} x {sz.y:F3} x {sz.z:F3} m, fuse spark {lookTip * Size:F3} m over the centre" +
                                   (want != drawn ? $"; look {want} ({OzGrenadeLook.Names[want]}) not drawn: {lookWhy[want]}" : "");
            if (want != drawn) Debug.LogWarning("[OpenZ] " + OzGrenadeLook.Report);
            else Debug.Log("[OpenZ] " + OzGrenadeLook.Report);
            lookShown = want; lookSmoothShown = smooth;
        }

        void MakeLook(int i)
        {
            var mesh = BakeLook(OzGrenadeLook.Models[i], out float tip, out string why);
            var tex = mesh ? Resources.Load<Texture2D>(OzGrenadeLook.Textures[i]) : null;
            if (!mesh) { lookWhy[i] = why; return; }
            if (!tex) { lookWhy[i] = "texture Resources/" + OzGrenadeLook.Textures[i] + " missing"; return; }
            var sz = mesh.bounds.size * Size;                                                    // drawn metres: fuse up = the tallest side, at the made height
            if (Mathf.Abs(sz.y - OzGrenadeLook.Heights[i]) > 0.03f * OzGrenadeLook.Heights[i] || sz.y < sz.x || sz.y < sz.z)
                Debug.LogWarning($"[OpenZ] grenade look {i} ({OzGrenadeLook.Names[i]}) is drawn {sz.x:F3} x {sz.y:F3} x {sz.z:F3} m, made {OzGrenadeLook.Heights[i]:F3} m tall with the fuse up: check its model import (Resources/{OzGrenadeLook.Models[i]})");
            // the old grenade's material (OpenZ/TeamLit: no team colour, stylized, no de-grime, smoothness .4, metal .15, instanced), coloured by the texture
            var m = new Material(lookOldMat) { name = "Grenade " + OzGrenadeLook.Names[i], enableInstancing = true };
            m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureOffset("_BaseMap", Vector2.zero);
            m.SetFloat("_UseVertexColor", 0f); m.SetColor("_BaseColor", Color.white);
            lookMesh[i] = mesh; lookMat[i] = m; lookTips[i] = tip;
        }

        /// <summary>A look's model (authored in drawn metres: its FBX is the size it is drawn) baked into one mesh like the old grenade: every
        /// part as the model stands when placed unturned (its own root's turn and scale kept: an FBX root may carry the axis turn of its
        /// file), centred on the bounds, in units of Size (the draw scales by Size), with its normals and UVs.
        /// tip = the fuse spark's height over the centre (its top + 0.02 m, in units of Size). Null when the model is missing or not readable.</summary>
        static Mesh BakeLook(string res, out float tip, out string why)
        {
            tip = 0.54f; why = "";
            var src = string.IsNullOrEmpty(res) ? null : Resources.Load<GameObject>(res);
            if (!src) { why = "model Resources/" + res + " missing"; return null; }
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            var pv = new List<Vector3>(); var pn = new List<Vector3>(); var puv = new List<Vector2>();
            foreach (var mf in src.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh; if (!m) continue;
                if (!m.isReadable) { why = "mesh " + m.name + " is not readable (its import: Read/Write)"; continue; }
                var M = mf.transform.localToWorldMatrix; var N = M.inverse.transpose; bool flip = M.determinant < 0f;   // the asset's own space (root turn kept), re-centred below
                m.GetVertices(pv); m.GetNormals(pn); m.GetUVs(0, puv);
                int b = v.Count;
                for (int i = 0; i < pv.Count; i++)
                {
                    v.Add(M.MultiplyPoint3x4(pv[i]));
                    n.Add(i < pn.Count ? N.MultiplyVector(pn[i]).normalized : Vector3.up);
                    uv.Add(i < puv.Count ? puv[i] : Vector2.zero);
                }
                for (int s = 0; s < m.subMeshCount; s++)
                {
                    var idx = m.GetTriangles(s);
                    for (int i = 0; i + 2 < idx.Length; i += 3)
                    {
                        t.Add(b + idx[i]);
                        if (flip) { t.Add(b + idx[i + 2]); t.Add(b + idx[i + 1]); }
                        else { t.Add(b + idx[i + 1]); t.Add(b + idx[i + 2]); }
                    }
                }
            }
            if (v.Count < 12 || t.Count < 36) { if (why == "") why = "model Resources/" + res + " has no drawable mesh"; return null; }
            var bb = new Bounds(v[0], Vector3.zero); foreach (var p in v) bb.Encapsulate(p);
            float k = 1f / Size;
            for (int i = 0; i < v.Count; i++) v[i] = (v[i] - bb.center) * k;
            var mesh = new Mesh { name = "OZ_Grenade_" + res.Substring(res.LastIndexOf('/') + 1) };
            if (v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0); mesh.RecalculateBounds();
            tip = bb.extents.y * k + 0.04f;
            return mesh;
        }
    }
}
