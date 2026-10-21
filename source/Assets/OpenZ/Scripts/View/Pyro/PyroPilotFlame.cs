using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Every living pyro (on foot, not inside a vehicle / cannon) draws a flickering
    /// additive flame at its flamer's PilotLight socket (Muzzle as fallback): a blue-white core in a warm orange halo, HDR so it
    /// blooms, kept at a minimum on-screen size when zoomed out. View only; one DrawMesh per pyro, no lights (the flamer's own fire
    /// light stays in OpenZPyroFire).
    /// </summary>
    public class PyroPilotFlame : MonoBehaviour
    {
        public static bool On = true;
        public static float CoreSize = 0.16f, MinScreenFrac = 0.0065f, Intensity = 3.2f;
        public static readonly Color Halo = new Color(1f, 0.46f, 0.12f), Core = new Color(0.55f, 0.75f, 1f);
        Material mat; Mesh quad; MaterialPropertyBlock mpb;
        readonly Dictionary<UnitView, Transform> sockets = new Dictionary<UnitView, Transform>();
        public static int Drawn { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { if (!FindFirstObjectByType<PyroPilotFlame>()) new GameObject("OZ_PyroPilotFlame").AddComponent<PyroPilotFlame>(); }

        void Start()
        {
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit"); if (!sh) { enabled = false; return; }
            mat = new Material(sh) { name = "PyroPilotFlame", renderQueue = 3100 };
            mat.SetFloat("_Surface", 1f); mat.SetFloat("_Blend", 2f); mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One); mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetTexture("_BaseMap", GlowTexture());
            quad = Quad(); mpb = new MaterialPropertyBlock();
        }

        static Texture2D GlowTexture()
        {
            const int N = 64; var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "PilotGlow" };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy * 0.55f);                                        // a little taller than wide: a flame
                    float halo = Mathf.Clamp01(1f - r); halo *= halo;
                    float core = Mathf.Clamp01(1f - r * 3.2f);
                    var c = Halo * halo + Core * core * 1.6f;
                    px[y * N + x] = new Color(c.r, c.g, c.b, 1f);
                }
            t.SetPixels(px); t.Apply(); return t;
        }

        static Mesh Quad()
        {
            var m = new Mesh { name = "PilotQuad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 }; m.RecalculateBounds(); return m;
        }

        void LateUpdate()
        {
            Drawn = 0;
            var g = ZGame.I; var cam = Camera.main;
            if (!On || !mat || g == null || !cam) return;
            float t = Time.time, fovK = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            foreach (var kv in g.Views)
            {
                var v = kv.Value; var o = v ? v.Obj : null;
                if (!v || o == null || o.Type != "pyro" || !v.isActiveAndEnabled || ZodBriefing.EndingHides(v)) continue;   // version 1.1: not in the level 16 ending's picture (ZodBriefing.EndingEnemies.cs)
                float out01 = 1f;                                                                           // a dead pyro's pilot flame dies down, it is not cut
                if (o.Destroyed || v.Dying) { out01 = Mathf.Clamp01(1f - v.DeadFor / OzGlowFade.Seconds); if (!v.Dying || out01 <= 0.02f) continue; }   // clamped: an eaten robot's death clock is minus infinity (UnitView.Eaten.cs), the flame keeps its normal size
                if (v.AnticActive && v.Antic.StowsWeapon) continue;                                      // the flamer is put away (drink, phone, fishing, lounger): no nozzle flame in the air
                if (!sockets.TryGetValue(v, out var s) || !s) { s = FindSocket(v.transform); sockets[v] = s; if (!s) continue; }
                if (!s.gameObject.activeInHierarchy) continue;
                var p = s.position;
                float dist = Vector3.Distance(cam.transform.position, p);
                float size = Mathf.Max(CoreSize, MinScreenFrac * dist * fovK);
                float flick = 0.85f + 0.1f * Mathf.Sin(t * 23f + o.Id) + 0.05f * Mathf.Sin(t * 41f + o.Id * 1.7f);
                var rot = Quaternion.LookRotation(p - cam.transform.position, cam.transform.up);
                var m = Matrix4x4.TRS(p, rot, new Vector3(size, size * 1.25f, size) * (flick * (0.35f + 0.65f * out01)));
                mpb.SetColor("_BaseColor", new Color(Intensity * flick * out01, Intensity * flick * out01, Intensity * flick * out01, 1f));
                Graphics.DrawMesh(quad, m, mat, 0, null, 0, mpb, UnityEngine.Rendering.ShadowCastingMode.Off, false);
                Drawn++;
            }
        }

        static Transform FindSocket(Transform root)
        {
            Transform muzzle = null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "PilotLight") return t;
                if (!muzzle && t.name == "Muzzle") muzzle = t;
            }
            return muzzle;
        }
    }
}
