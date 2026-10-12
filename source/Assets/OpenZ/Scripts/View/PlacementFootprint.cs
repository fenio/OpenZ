using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// ghost: GREEN where Zod allows the spot (ZWorld.CannonPlacable), RED where not; a soft fill with a bright border and corner ticks,
    /// conforming to the ground (5 x 5 grid on GroundY), gently pulsing. On a fort tower pad the footprint snaps flat onto the pad
    /// (the ghost already sits exactly on the tower seat, combat_nb_v1 NB28). A change of validity ticks (ui_hover, rate-limited) so
    /// the colour flip is heard; arming / refusing keep ui_cannon_place_arm / ui_cannon_place_invalid. View only.
    /// </summary>
    public static class PlacementFootprint
    {
        static GameObject go; static Mesh mesh; static Material mat; static bool lastValid, shown; static float lastTick = -10f;
        public static int Shows, Flips;
        public static bool Visible => go && go.activeSelf;
        public static bool LastValid => lastValid;
        static readonly Color Ok = new Color(0.35f, 1f, 0.45f, 1f), Bad = new Color(1f, 0.28f, 0.22f, 1f);
        const int N = 5; const float Border = 0.22f;

        static void Ensure()
        {
            if (go) return;
            go = new GameObject("CannonFootprint");
            mesh = new Mesh { name = "CannonFootprint" }; mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var so = Object.FindFirstObjectByType<SelectionOverlay>();
            var shader = so && so.overlayMaterial ? so.overlayMaterial.shader : Shader.Find("OpenZ/Overlay");   // the scene's overlay material keeps the shader in builds
            if (!shader) shader = Shader.Find("Sprites/Default");
            mat = new Material(shader) { name = "CannonFootprint" };
            if (mat.HasProperty("_ZTest")) mat.SetFloat("_ZTest", 4);                                    // LEqual + the shader's offset: on the ground, behind the ghost's base
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        }

        /// <summary>centre = the footprint centre on the ground, size = edge in m; tower = flat at y (the pad), rotated by yaw.</summary>
        public static void Show(ZGame game, Vector3 centre, float size, bool valid, bool tower, float yaw)
        {
            Ensure();
            if (!shown) { shown = true; Shows++; lastValid = !valid; }
            if (valid != lastValid)
            {
                lastValid = valid; Flips++;
                if (Time.unscaledTime - lastTick > 0.15f) { lastTick = Time.unscaledTime; AudioDirector.UI("ui_hover", 0.7f, valid ? 1.15f : 0.85f); }
                OzSoundLog.Add("ui", "place_footprint", "ui_hover", valid ? "GREEN (allowed)" : "RED (blocked)");
            }
            go.SetActive(true);
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 6f);
            var c = valid ? Ok : Bad;
            // vertices: an outer N x N grid (fill) + an inner ring for the border, in world space
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var verts = new Vector3[N * N * 2]; var cols = new Color[verts.Length];
            float h = size * 0.5f;
            for (int k = 0; k < 2; k++)
            {
                float s = k == 0 ? h : h - Border;
                for (int j = 0; j < N; j++)
                    for (int i = 0; i < N; i++)
                    {
                        var off = rot * new Vector3(Mathf.Lerp(-s, s, i / (N - 1f)), 0f, Mathf.Lerp(-s, s, j / (N - 1f)));
                        var p = centre + off;
                        p.y = tower ? centre.y + 0.05f : game.GroundY(p.x, p.z) + 0.06f;
                        int idx = k * N * N + j * N + i;
                        verts[idx] = p;
                        cols[idx] = k == 0 ? new Color(c.r, c.g, c.b, 0.85f * pulse) : new Color(c.r, c.g, c.b, 0.85f * pulse);
                    }
            }
            // fill: the inner grid at low alpha; border: the band between the outer and inner edge rings at high alpha
            var fillCols = new Color(c.r, c.g, c.b, 0.22f * pulse);
            for (int j = 1; j < N - 1; j++) for (int i = 1; i < N - 1; i++) cols[N * N + j * N + i] = fillCols;
            var tris = new System.Collections.Generic.List<int>(6 * N * N * 2);
            int I0 = N * N;
            for (int j = 0; j < N - 1; j++)
                for (int i = 0; i < N - 1; i++)
                {   // fill quads on the inner grid
                    int a = I0 + j * N + i, b = a + 1, d = a + N, e = d + 1;
                    tris.Add(a); tris.Add(d); tris.Add(b); tris.Add(b); tris.Add(d); tris.Add(e);
                }
            // border band: outer edge vertex -> inner edge vertex, around the four sides
            void Band(int o0, int o1, int i0, int i1) { tris.Add(o0); tris.Add(i0); tris.Add(o1); tris.Add(o1); tris.Add(i0); tris.Add(i1); }
            for (int i = 0; i < N - 1; i++)
            {
                Band(i, i + 1, I0 + i, I0 + i + 1);                                                       // south
                Band((N - 1) * N + i + 1, (N - 1) * N + i, I0 + (N - 1) * N + i + 1, I0 + (N - 1) * N + i);   // north
                Band((i + 1) * N, i * N, I0 + (i + 1) * N, I0 + i * N);                                   // west
                Band(i * N + N - 1, (i + 1) * N + N - 1, I0 + i * N + N - 1, I0 + (i + 1) * N + N - 1);   // east
            }
            // the inner edge ring stays bright (a crisp line), the fill fades in from it
            for (int i = 0; i < N; i++)
            {
                cols[I0 + i] = cols[I0 + (N - 1) * N + i] = cols[I0 + i * N] = cols[I0 + i * N + N - 1] = new Color(c.r, c.g, c.b, 0.55f * pulse);
            }
            mesh.Clear(); mesh.vertices = verts; mesh.colors = cols; mesh.SetTriangles(tris, 0); mesh.RecalculateBounds();
        }

        public static void Hide() { shown = false; if (go) go.SetActive(false); }
    }
}
