using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// Driven by TrackMarks, which knows the surface and every footfall:
    /// - vehicles on sand / dirt: puffs behind the rear contact points, alternating sides, spaced 1.4 -> 0.65 m (tracks) or
    ///   1.1 -> 0.5 m (jeep) as the speed rises, bigger, denser and thrown farther back with speed; none on roads, concrete
    ///   or water, faint on bridge decks (TrackSurface.DustStrength, TrackMarks.deckDust);
    ///   fx.dust(at, .5): 5 puffs radial 1.7 m/s, up .4-.72, life 1.1-1.55 s).
    /// a short fade-in, drag and a drift down the level's wind (Sand.shader _WindAngle). The colour of each puff is the
    /// rendered ground under it (TrackSurface.DustColor: L02 golden sand / tan dirt, L17 rust-brown dirt), so the dust always
    /// FxManager and FactoryViews use), one instanced draw call sorted far to near, no per-frame allocations. It does not
    /// use FxManager's 160-slot dust pool, so explosions never lose their dust to the traffic.
    /// </summary>
    [DefaultExecutionOrder(81)]
    public class DustTrails : MonoBehaviour
    {
        public static DustTrails I { get; private set; }
        public static bool Active => I && I.isActiveAndEnabled;
        public const int Capacity = 1023;                                           // one DrawMeshInstanced batch

        [Header("Vehicle wake")]
        public float wakeAlpha = 0.34f, wakeSize = 0.95f, wakeLife = 1.6f;
        [Header("Footfalls")]
        public float robotAlpha = 0.24f, robotSize = 0.36f, mechAlpha = 0.32f;
        [Header("Motion")]
        public float drag = 1.4f, windSpeed = 0.35f;
        [Header("Colour")]
        [Tooltip("Airborne dust reads paler than the ground it came from")] [Range(0f, 0.5f)] public float pale = 0.14f;
        [Range(0.5f, 1.5f)] public float brightness = 1.04f;

        public int Live { get; private set; }

        struct Puff { public bool on; public Vector3 p, v; public float age, life, size, alpha, spin; public Quaternion rot; public Vector4 tint; }

        readonly Puff[] puffs = new Puff[Capacity];
        readonly Matrix4x4[] mats = new Matrix4x4[Capacity];
        readonly float[] alphas = new float[Capacity];
        readonly Vector4[] tints = new Vector4[Capacity];
        readonly float[] keys = new float[Capacity];
        readonly int[] order = new int[Capacity];
        int cursor;
        Mesh mesh; float norm = 0.5f; bool ownMesh;
        Material mat; MaterialPropertyBlock mpb; bool tinted;
        bool linear;
        static readonly int AlphaId = Shader.PropertyToID("_Alpha"), TintId = Shader.PropertyToID("_Tint");

        void Awake()
        {
            if (I && I != this) { Destroy(this); return; }
            I = this;
            linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            mpb = new MaterialPropertyBlock();
            InitMesh();
            InitMaterial();
            if (!mesh || !mat) { Debug.LogError("DustTrails: no puff mesh or material"); enabled = false; }
        }

        void OnDestroy()
        {
            if (I != this) return;
            I = null;
            if (ownMesh && mesh) Destroy(mesh);
            if (mat) Destroy(mat);
        }

        void InitMesh()
        {
            var lib = Resources.Load<GameObject>("OpenZ/FX/combat_fx");
            if (lib)
                foreach (var mf in lib.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.name != "FX_Smoke" || !mf.sharedMesh) continue;
                    var m = Instantiate(mf.sharedMesh); var M = lib.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    var v = m.vertices; var n = m.normals;
                    for (int i = 0; i < v.Length; i++) { v[i] = M.MultiplyPoint3x4(v[i]); if (i < n.Length) n[i] = M.MultiplyVector(n[i]).normalized; }
                    m.vertices = v; if (n.Length == v.Length) m.normals = n; else m.RecalculateNormals(); m.RecalculateBounds();
                    mesh = m; ownMesh = true; break;
                }
            if (!mesh) { mesh = Sphere(10, 7); ownMesh = true; }
            var s = mesh.bounds.size;
            norm = 1f / Mathf.Max(0.01f, Mathf.Max(s.x, Mathf.Max(s.y, s.z)));                     // size 1 = 1 m across
        }

        void InitMaterial()
        {
            var src = Resources.Load<Material>("OpenZ/FX/M_TrackDust");                        // keeps the shader in builds
            if (src && src.shader && src.shader.name == "OpenZ/TrackDust") { mat = new Material(src); tinted = true; }
            else
            {
                var sh = Shader.Find("OpenZ/TrackDust");
                if (sh) { mat = new Material(sh); tinted = true; }
                else
                {   // fallback: FxLit like FactoryViews' smoke (one colour for the whole level)
                    sh = Shader.Find("OpenZ/FxLit");
                    if (!sh) return;
                    mat = new Material(sh);
                    mat.SetColor("_BaseColor", new Color(0.78f, 0.60f, 0.40f, 1f)); mat.SetColor("_EmissionColor", Color.black);
                    mat.SetFloat("_Unlit", 0.1f); mat.SetFloat("_SoftEdge", 1f);
                    mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    mat.SetFloat("_ZWrite", 0f); mat.SetFloat("_Cull", 2f);
                    tinted = false;
                }
            }
            mat.name = "DustTrails"; mat.enableInstancing = true; mat.renderQueue = 3000;             // with FxManager's dust and smoke
        }

        // ================================================================================================ emitters
        /// <summary>
        /// alpha = peak opacity, srgb = colour of the ground it came from.</summary>
        public void Emit(Vector3 at, Vector3 vel, float size, float life, float alpha, Color srgb)
        {
            if (!enabled || alpha < 0.004f || life <= 0.05f) return;
            ref var p = ref puffs[cursor];
            cursor = (cursor + 1) % Capacity;                                                        // ring: the oldest puff goes first
            var c = Color.Lerp(srgb, new Color(0.9f, 0.86f, 0.8f), pale) * brightness;
            if (linear) c = c.linear;
            p.on = true; p.p = at; p.v = vel; p.age = 0f; p.life = life; p.size = size; p.alpha = alpha;
            p.rot = Random.rotation; p.spin = Random.Range(-30f, 30f);
            p.tint = new Vector4(c.r, c.g, c.b, 1f);
        }

        /// <summary>Behind a moving vehicle: at = ground under the rear contact point, dir = travel direction, sideOut = outward
        /// from the hull, speed01 = speed / top road speed, strength = TrackSurface.DustStrength.</summary>
        public void VehicleWake(Vector3 at, Vector3 dir, Vector3 sideOut, float speed01, float strength, Color srgb, bool wheeled)
        {
            float s = Mathf.Clamp01(speed01);
            var vel = -dir * ((0.3f + 0.9f * s) * Random.Range(0.8f, 1.2f)) + sideOut * Random.Range(0.12f, 0.45f)
                      + Vector3.up * Random.Range(0.3f, 0.6f);
            float size = wakeSize * (wheeled ? 0.85f : 1.1f) * (0.7f + 0.6f * s) * Random.Range(0.85f, 1.15f);
            float life = wakeLife * (0.9f + 0.6f * s) * Random.Range(0.85f, 1.2f);
            Emit(at + Vector3.up * (0.12f + 0.1f * size), vel, size, life, wakeAlpha * strength * (0.3f + 0.7f * s), srgb);
            if (!wheeled && s > 0.6f)                                                                // heavy tracks throw a second, lower puff
                Emit(at + Vector3.up * 0.08f - dir * 0.3f, vel * 0.6f + sideOut * 0.2f, size * 0.7f, life * 0.8f, wakeAlpha * strength * 0.6f * s, srgb);
        }

        /// <summary></summary>
        public void Footfall(Vector3 at, float strength, Color srgb, bool big)
        {
            if (big)
            {   // dust(at, .5): 5 puffs, a = j x 2.399, radial 1.7, up .4 + .08 j, life 1.1 + .11 j, scale (.4 + .09 j) x .5
                float a0 = Random.value * 6.2832f;
                for (int j = 0; j < 5; j++)
                {
                    float a = a0 + j * 2.399f;
                    Emit(at + Vector3.up * 0.1f, new Vector3(Mathf.Cos(a) * 1.7f, 0.4f + j * 0.08f, Mathf.Sin(a) * 1.7f),
                         (0.4f + 0.09f * j), 1.1f + 0.11f * j, mechAlpha * strength, srgb);
                }
                return;
            }
            for (int j = 0; j < 2; j++)
            {
                float a = Random.value * 6.2832f;
                Emit(at + Vector3.up * 0.05f, new Vector3(Mathf.Cos(a) * 0.45f, 0.22f + 0.06f * j, Mathf.Sin(a) * 0.45f),
                     robotSize * Random.Range(0.85f, 1.15f), Random.Range(0.8f, 1.1f), robotAlpha * strength, srgb);
            }
        }

        /// <summary>Removes every puff.</summary>
        public void Clear() { for (int i = 0; i < Capacity; i++) puffs[i].on = false; Live = 0; }

        // ================================================================================================ update + draw
        void LateUpdate()
        {
            var g = ZGame.I;
            float dt = g ? (g.paused ? 0f : Time.deltaTime * g.gameSpeed) : Time.deltaTime;
            var tm = TrackMarks.I;
            var wind = (tm && tm.Surface != null ? tm.Surface.Wind : new Vector3(0.88f, 0f, 0.47f)) * windSpeed;
            var cam = Camera.main;
            var cp = cam ? cam.transform.position : Vector3.zero;
            float damp = Mathf.Max(0f, 1f - drag * dt);
            int n = 0;
            for (int i = 0; i < Capacity; i++)
            {
                ref var p = ref puffs[i];
                if (!p.on) continue;
                p.age += dt;
                if (p.age >= p.life) { p.on = false; continue; }
                float k = p.age / p.life;
                p.v *= damp;
                p.p += (p.v + wind * Mathf.Min(1f, k * 2f)) * dt;
                keys[n] = -(p.p - cp).sqrMagnitude;                                                  // far first
                order[n] = i;
                n++;
            }
            Live = n;
            if (n == 0) return;
            System.Array.Sort(keys, order, 0, n);
            int m = 0;
            for (int j = 0; j < n; j++)
            {
                ref var p = ref puffs[order[j]];
                float k = p.age / p.life;
                float a = p.alpha * Smooth(0f, 0.08f, k) * (1f - Smooth(0.22f, 1f, k));             // M12 smoke fade + fade-in
                if (a < 0.002f) continue;
                float size = p.size * (0.5f + 1.8f * k) * norm;
                mats[m] = Matrix4x4.TRS(p.p, p.rot * Quaternion.Euler(0f, p.spin * p.age, 0f), new Vector3(size, size, size));
                alphas[m] = a;
                tints[m] = p.tint;
                m++;
            }
            if (m == 0) return;
            mpb.SetFloatArray(AlphaId, alphas);
            if (tinted) mpb.SetVectorArray(TintId, tints);
            Graphics.DrawMeshInstanced(mesh, 0, mat, mats, m, mpb, ShadowCastingMode.Off, false);
        }

        static float Smooth(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3f - 2f * t); }

        static Mesh Sphere(int seg, int rings)
        {
            var v = new Vector3[(seg + 1) * (rings + 1)]; var tri = new int[seg * rings * 6];
            for (int r = 0; r <= rings; r++)
                for (int s = 0; s <= seg; s++)
                {
                    float th = Mathf.PI * r / rings, ph = 2f * Mathf.PI * s / seg;
                    v[r * (seg + 1) + s] = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                }
            int k = 0;
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = r * (seg + 1) + s, b = a + seg + 1;
                    tri[k++] = a; tri[k++] = a + 1; tri[k++] = b; tri[k++] = a + 1; tri[k++] = b + 1; tri[k++] = b;
                }
            var m = new Mesh { name = "DustPuff" };
            m.vertices = v; m.triangles = tri; m.normals = v; m.RecalculateBounds();
            return m;
        }
    }
}
