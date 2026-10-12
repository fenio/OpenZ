using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// Thrown grenades: a real little grenade, the grenade.glb of our earlier projects (olive body, lighter ribs, gunmetal fuse,
    /// tan spoon and lever) when the fx import has copied it to Resources/OpenZ/FX/grenade, else a procedural twin of it (egg body,
    /// band, cap, fuse, spoon lever: olive-grey body, dark band, gunmetal cap and lever). In flight it
    /// spins rot.x += 7, rot.z += 3 rad/s, casts a shadow. Drawn with OpenZ/TeamLit (stylized, flat facets, no team colour),
    /// a tiny flickering fuse spark (additive HDR, blooms) and a soft smoky trail (OpenZ/SoftTrail, alpha blended, never
    /// white). The robot holds it through the wind-up and releases it at .56 s of the 1.2 s grenade_throw clip, then it flies the rest of the flight on the M12 ballistic arc
    /// (g 9.8) and lands exactly when the sim explodes (flight = sim delay / gameSpeed).
    /// </summary>
    [DefaultExecutionOrder(101)]
    public class GrenadeFx : MonoBehaviour
    {
        public const float Size = 0.5f;
        public const float Release = 0.56f;           // s into grenade_throw
        const int Cap = 32;

        sealed class G
        {
            public bool active, held; public UnitView owner; public Transform hand; public Vector3 to, p0, v, pos; public float age, release, flight, g, lightT;
            public Quaternion rot; public TrailRenderer trail; public bool clink;
        }

        public static GrenadeFx I { get; private set; }
        readonly List<G> pool = new List<G>();
        readonly Dictionary<UnitView, Transform> hands = new Dictionary<UnitView, Transform>();
        Mesh body, spark; Material bodyMat, sparkMat, trailMat;
        readonly Matrix4x4[] bodyM = new Matrix4x4[Cap], sparkM = new Matrix4x4[Cap * 2];
        MaterialPropertyBlock sparkMpb; readonly float[] ones = new float[Cap * 2];
        static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        /// <summary></summary>
        public static bool LegacyModel { get; private set; }
        /// <summary></summary>
        public static bool UseLegacyModel = true;
        public int Active { get { int n = 0; foreach (var g in pool) if (g.active) n++; return n; } }
        public static int Thrown { get; private set; }
        /// <summary>vehicle_lights: every drawn grenade fuse once per frame (fuse tip, velocity; zero while still held).</summary>
        public static event System.Action<Vector3, Vector3> FuseDrawn;

        static GrenadeFx Instance
        {
            get
            {
                if (I) return I;
                I = FindFirstObjectByType<GrenadeFx>();
                if (!I) I = new GameObject("GrenadeFx").AddComponent<GrenadeFx>();
                return I;
            }
        }

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
        static Color Lin(string h, float gain) { var c = (Hex(h).linear * gain).gamma; c.a = 1; return c; }   // FxManager.Lin

        void Awake()
        {
            if (I && I != this) { Destroy(this); return; }
            I = this;
            var lit = Shader.Find("OpenZ/TeamLit"); var fx = Shader.Find("OpenZ/FxLit"); var tr = Shader.Find("OpenZ/SoftTrail");
            if (!lit || !fx) { Debug.LogError("GrenadeFx: OpenZ/TeamLit or OpenZ/FxLit missing"); enabled = false; return; }
            body = UseLegacyModel ? BakeLegacyGrenade() : null; LegacyModel = body; if (!body) body = BuildGrenade(); spark = BuildBall(8, 6);
            bodyMat = new Material(lit) { name = "Grenade", enableInstancing = true };
            bodyMat.SetFloat("_TeamMode", 0); bodyMat.SetFloat("_UseVertexColor", 1); bodyMat.SetFloat("_VertexColorSRGB", 1);
            bodyMat.SetFloat("_Degreen", 0);                                                        // olive is the grenade's paint, not a team
            bodyMat.SetFloat("_Stylize", 1); bodyMat.SetFloat("_FlatShade", 1); bodyMat.SetFloat("_Degrime", 0);
            bodyMat.SetFloat("_Smoothness", 0.4f); bodyMat.SetFloat("_Metallic", 0.15f); bodyMat.SetColor("_BaseColor", Color.white);
            sparkMat = new Material(fx) { name = "GrenadeFuse", enableInstancing = true, renderQueue = 3060 };
            sparkMat.SetColor("_BaseColor", new Color(0, 0, 0, 1)); sparkMat.SetColor("_EmissionColor", Lin("#ffa63d", FuseGain));
            sparkMat.SetFloat("_Unlit", 1); sparkMat.SetFloat("_SoftEdge", 1); sparkMat.SetFloat("_Cull", 0);
            sparkMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); sparkMat.SetFloat("_DstBlend", (float)BlendMode.One); sparkMat.SetFloat("_ZWrite", 0);
            sparkMpb = new MaterialPropertyBlock(); for (int i = 0; i < ones.Length; i++) ones[i] = 1f;
            if (tr) { trailMat = new Material(tr) { name = "GrenadeTrail" }; trailMat.SetColor("_Color", new Color(0.64f, 0.6f, 0.52f, 0.5f)); }
        }

        void OnDestroy() { if (I == this) I = null; }

        /// <summary>CombatView.MissileFired (grenade): from = the fallback release point, to = the sim's landing point,
        /// flight = the sim's time to detonation (ZEvent.value, SIM seconds; ZGame runs the sim at gameSpeed, ZGame.cs:107, so
        /// the grenade flies flight / gameSpeed real seconds and lands on MissileExploded also in the 4x bot match, like
        /// OpenZFxPlayer.Launch). False when GrenadeFx cannot draw (the caller falls back to the FX projectile).</summary>
        public static bool Throw(UnitView thrower, Vector3 from, Vector3 to, float flight)
        {
            var fx = Instance;
            float speed = ZGame.I ? Mathf.Max(0.25f, ZGame.I.gameSpeed) : 1f;
            return fx && fx.enabled && fx.bodyMat && fx.Launch(thrower, from, to, flight / speed);
        }

        /// <summary>
        /// ~1.5 m more): the gravity that puts the apex FortLobRise m over the higher end within the sim's flight time (it still lands
        /// on the sim's blast at its time). Solves apex A = vy^2 / 2g with vy = dy / T + g T / 2 (the larger root).</summary>
        public static float FortLobRise = 5f;
        /// <summary></summary>
        public static float FuseGain = 3.2f, FuseHalo = 0.13f;
        static float FortLobGravity(Vector3 p0, Vector3 to, float T)
        {
            T = Mathf.Max(0.2f, T);
            float dy = to.y - p0.y, A = Mathf.Max(0f, dy) + FortLobRise, k = 2f * A - dy;
            float disc = Mathf.Max(0f, k * k - dy * dy);
            return Mathf.Clamp((k + Mathf.Sqrt(disc)) * 2f / (T * T), 9.8f, 160f);
        }

        Transform HandOf(UnitView v)
        {
            if (!v) return null;
            if (hands.TryGetValue(v, out var h) && h) return h;
            h = null;
            foreach (var t in v.GetComponentsInChildren<Transform>(true)) if (t.name == "mixamorigRightHand") { h = t; break; }
            hands[v] = h;
            return h;
        }

        bool Launch(UnitView thrower, Vector3 from, Vector3 to, float flight)
        {
            G g = null;
            foreach (var x in pool) if (!x.active) { g = x; break; }
            if (g == null)
            {
                if (pool.Count >= Cap) { g = pool[0]; foreach (var x in pool) if (x.age > g.age) g = x; }   // recycle the oldest
                else { g = new G(); pool.Add(g); }
            }
            float T = Mathf.Max(0.12f, flight);
            var hand = HandOf(thrower); Thrown++;
            g.active = true; g.owner = thrower; g.hand = hand; g.to = to; g.age = 0; g.g = 9.8f; g.clink = false; g.lightT = 0;
            g.release = hand ? Mathf.Min(Release, T * 0.45f) : 0f;                                    // short throws release early
            g.flight = T - g.release;
            g.held = g.release > 0f;
            g.pos = g.held ? hand.position : from;
            g.rot = Random.rotation;
            if (!g.held) Fly(g);
            if (g.held) OzSoundV3.HeldThrowFrame = Time.frameCount;                                    // sound_v3: the whoosh at the release
            var aim = thrower ? thrower.GetComponent<UnitAim>() : null;
            if (aim) aim.BeginThrow(to, g.release + 0.35f);
            return true;
        }

        void Fly(G g)
        {
            if (g.release > 0f) OzSoundV3.GrenadeReleased(g.pos, g.owner && g.owner.Obj != null ? g.owner.Obj.Id : -1);   // sound_v3: out of the hand
            OzSoundV3.GrenadeFuse(g.pos);
            g.held = false; g.p0 = g.pos;
            if (g.owner && g.owner.IsFortDefender) g.g = FortLobGravity(g.p0, g.to, g.flight);
            g.v = (g.to - g.p0) / g.flight; g.v.y += g.g * g.flight * 0.5f;                           // FxManager.Projectile arc
            if (!trailMat) return;
            if (!g.trail)
            {
                var go = new GameObject("GrenadeTrail"); go.transform.SetParent(transform, false);
                var t = go.AddComponent<TrailRenderer>();
                t.sharedMaterial = trailMat; t.time = 0.38f; t.minVertexDistance = 0.08f; t.widthMultiplier = 0.2f;
                t.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
                var grad = new Gradient();
                grad.SetKeys(new[] { new GradientColorKey(new Color(0.85f, 0.8f, 0.7f), 0f), new GradientColorKey(new Color(0.55f, 0.53f, 0.5f), 1f) },
                             new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(0f, 1f) });
                t.colorGradient = grad; t.alignment = LineAlignment.View; t.textureMode = LineTextureMode.Stretch; t.numCapVertices = 2;
                t.shadowCastingMode = ShadowCastingMode.Off; t.receiveShadows = false;
                g.trail = t;
            }
            g.trail.transform.position = g.pos; g.trail.Clear(); g.trail.emitting = true;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime, now = Time.time;
            int nb = 0, ns = 0;
            foreach (var g in pool)
            {
                if (!g.active) continue;
                g.age += dt;
                if (g.held)
                {
                    if (g.hand && g.hand.gameObject.activeInHierarchy && g.owner && !g.owner.Dying && g.age < g.release) g.pos = g.hand.position;
                    else Fly(g);                                                                       // released (or the thrower died)
                }
                if (!g.held)
                {
                    float t = Mathf.Min(g.age - g.release, g.flight);
                    g.pos = g.p0 + g.v * t + Vector3.down * (0.5f * g.g * t * t);
                    g.rot = g.rot * Quaternion.Euler(7f * Mathf.Rad2Deg * dt, 0f, 3f * Mathf.Rad2Deg * dt);
                    if (g.trail) g.trail.transform.position = g.pos;
                    if (!g.clink && t >= g.flight * 0.88f) { g.clink = true; CombatAudio.I?.Play("grenade_land", g.pos, 0.4f, 0.1f, -1, OzSoundV3.BankGrenadeClink); OzSoundV3.GrenadeLanded(g.pos, g.flight - t); }   // sound_v3: 4 takes + 2nd bounce
                    if ((g.lightT -= dt) <= 0f && FxManager.I) { g.lightT = 0.12f; FxManager.I.AddLight(g.pos + Vector3.up * 0.2f, new Color(1f, 0.66f, 0.3f), 1.1f, 2.6f, 0.15f, 0.6f); }
                    if (t >= g.flight) { g.active = false; if (g.trail) g.trail.emitting = false; continue; }
                }
                if (nb < Cap) bodyM[nb++] = Matrix4x4.TRS(g.pos, g.rot, Vector3.one * Size);
                if (ns + 1 < sparkM.Length)
                {   // fuse spark: a hot core and a soft halo at the fuse tip, flickering
                    var tip = g.pos + g.rot * (Vector3.up * 0.54f * Size);
                    FuseDrawn?.Invoke(tip, g.held ? Vector3.zero : g.v + Vector3.down * (g.g * Mathf.Min(g.age - g.release, g.flight)));   // vehicle_lights
                    float f = 0.75f + 0.5f * Mathf.PerlinNoise(g.age * 23f, g.GetHashCode() * 0.001f);
                    sparkM[ns++] = Matrix4x4.TRS(tip, Quaternion.identity, Vector3.one * 0.055f * f);
                    sparkM[ns++] = Matrix4x4.TRS(tip, Quaternion.identity, Vector3.one * FuseHalo * f);
                }
            }
            if (nb > 0) Graphics.DrawMeshInstanced(body, 0, bodyMat, bodyM, nb, null, ShadowCastingMode.On, true);
            if (ns > 0)
            {
                sparkMpb.SetFloatArray(AlphaId, ones);
                Graphics.DrawMeshInstanced(spark, 0, sparkMat, sparkM, ns, sparkMpb, ShadowCastingMode.Off, false);
            }
        }

        // ------------------------------------------------------------------ meshes (vertex colours, sRGB; unit height 1, fuse up)
        static void Lathe(List<Vector3> v, List<Color32> c, List<int> t, (float y, float r)[] prof, int seg, Color32 col, bool capTop, bool capBottom)
        {
            for (int k = 0; k + 1 < prof.Length; k++)
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                    var p00 = new Vector3(Mathf.Cos(a0) * prof[k].r, prof[k].y, Mathf.Sin(a0) * prof[k].r);
                    var p01 = new Vector3(Mathf.Cos(a1) * prof[k].r, prof[k].y, Mathf.Sin(a1) * prof[k].r);
                    var p10 = new Vector3(Mathf.Cos(a0) * prof[k + 1].r, prof[k + 1].y, Mathf.Sin(a0) * prof[k + 1].r);
                    var p11 = new Vector3(Mathf.Cos(a1) * prof[k + 1].r, prof[k + 1].y, Mathf.Sin(a1) * prof[k + 1].r);
                    Quad(v, c, t, p00, p10, p11, p01, col);
                }
            if (capTop) Disc(v, c, t, prof[prof.Length - 1].y, prof[prof.Length - 1].r, seg, col, true);
            if (capBottom) Disc(v, c, t, prof[0].y, prof[0].r, seg, col, false);
        }

        static void Disc(List<Vector3> v, List<Color32> c, List<int> t, float y, float r, int seg, Color32 col, bool up)
        {
            if (r <= 1e-4f) return;
            int centre = v.Count; v.Add(new Vector3(0, y, 0)); c.Add(col);
            for (int i = 0; i <= seg; i++) { float a = i * Mathf.PI * 2f / seg; v.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r)); c.Add(col); }
            for (int i = 0; i < seg; i++)
            {
                if (up) t.AddRange(new[] { centre, centre + 2 + i, centre + 1 + i });
                else t.AddRange(new[] { centre, centre + 1 + i, centre + 2 + i });
            }
        }

        /// <summary>Quad a-b-c-d; the winding is chosen so the face points away from the grenade's axis (outward).</summary>
        static void Quad(List<Vector3> v, List<Color32> c, List<int> t, Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color32 col)
        {
            int i = v.Count; v.Add(a); v.Add(b); v.Add(cc); v.Add(d); for (int k = 0; k < 4; k++) c.Add(col);
            var n = Vector3.Cross(b - a, cc - a); var mid = (a + b + cc + d) * 0.25f; var outward = new Vector3(mid.x, 0, mid.z);
            if (outward.sqrMagnitude < 1e-6f) outward = mid;
            // triangle (a, b, c) faces Cross(b - a, c - a) (OpenZRifle.Box / OpenZFracture.Chunk rule)
            if (Vector3.Dot(n, outward) > 0) t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            else t.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
        }

        static void Box(List<Vector3> v, List<Color32> c, List<int> t, Vector3 centre, Vector3 half, Quaternion rot, Color32 col)
        {
            var dirs = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var d in dirs)
            {
                var a = d.x != 0 ? Vector3.up : Vector3.right; var b = Vector3.Cross(d, a);
                var fc = Vector3.Scale(d, half); var ea = Vector3.Scale(a, half); var eb = Vector3.Scale(b, half);
                Vector3 P(Vector3 q) => centre + rot * q;
                int i = v.Count;
                v.Add(P(fc - ea - eb)); v.Add(P(fc + ea - eb)); v.Add(P(fc + ea + eb)); v.Add(P(fc - ea + eb));
                for (int k = 0; k < 4; k++) c.Add(col);
                var n = Vector3.Cross(v[i + 1] - v[i], v[i + 2] - v[i]);
                if (Vector3.Dot(n, rot * d) > 0) t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                else t.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }
        }

        /// <summary>
        /// import copies it to Resources/OpenZ/FX/grenade, the same asset OpenZFxPlayer flies) baked into one flat-shaded mesh:
        /// every part in the glb root's space, each triangle coloured with its material's base colour (glTFast stores
        /// baseColorFactor as sRGB: ShaderGraphMaterialGenerator.cs:435 SetVector(BaseColor, baseColorLinear.gamma),
        /// MaterialProperty.cs:20), centred and scaled to unit height, fuse up (+y, as in the glb). Drawn with the same stylized
        /// TeamLit as the units. Null when the asset is missing or not readable: the procedural twin (BuildGrenade) is used.</summary>
        static Mesh BakeLegacyGrenade()
        {
            var src = Resources.Load<GameObject>("OpenZ/FX/grenade");
            if (!src) return null;
            var v = new List<Vector3>(); var c = new List<Color32>(); var t = new List<int>(); var pv = new List<Vector3>();
            var toRoot = src.transform.worldToLocalMatrix;
            foreach (var mf in src.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh; if (!m || !m.isReadable) continue;
                var mr = mf.GetComponent<MeshRenderer>(); var mats = mr ? mr.sharedMaterials : null;
                var M = toRoot * mf.transform.localToWorldMatrix;
                m.GetVertices(pv);
                for (int s = 0; s < m.subMeshCount; s++)
                {
                    var col = BaseColour(mats != null && mats.Length > 0 ? mats[Mathf.Min(s, mats.Length - 1)] : null);
                    var idx = m.GetTriangles(s);
                    for (int i = 0; i < idx.Length; i++) { t.Add(v.Count); v.Add(M.MultiplyPoint3x4(pv[idx[i]])); c.Add(col); }   // unshared: flat facets
                }
            }
            if (v.Count < 12) return null;
            var b = new Bounds(v[0], Vector3.zero); foreach (var p in v) b.Encapsulate(p);
            if (b.size.y < 1e-4f) return null;
            float k = 1f / b.size.y;
            for (int i = 0; i < v.Count; i++) v[i] = (v[i] - b.center) * k;
            var mesh = new Mesh { name = "OZ_Grenade_Legacy" };
            mesh.SetVertices(v); mesh.SetColors(c); mesh.SetTriangles(t, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>sRGB base colour of a glTFast (baseColorFactor) or URP / legacy (_BaseColor / _Color) material.</summary>
        static Color32 BaseColour(Material m)
        {
            Color col = !m ? Hex("#6e7253")
                      : m.HasProperty("baseColorFactor") ? (Color)m.GetVector("baseColorFactor")
                      : m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor")
                      : m.HasProperty("_Color") ? m.GetColor("_Color") : Hex("#6e7253");
            col.a = 1f;
            return col;
        }

        static Mesh BuildGrenade()
        {
            var v = new List<Vector3>(); var c = new List<Color32>(); var t = new List<int>();
            Color32 olive = Hex("#6e7253"), band = Hex("#2c302a"), metal = Hex("#545a52"), lever = Hex("#8f948b");
            const int seg = 10;
            // egg body (grenade.glb Grenade_body: r .084 -> .058, h .15 at 1:1; here unit height), centred on its middle
            Lathe(v, c, t, new[] { (-0.50f, 0.0f), (-0.47f, 0.20f), (-0.40f, 0.33f), (-0.28f, 0.41f), (-0.12f, 0.44f), (0.05f, 0.43f), (0.20f, 0.38f), (0.30f, 0.29f), (0.36f, 0.21f) }, seg, olive, true, false);
            Lathe(v, c, t, new[] { (-0.13f, 0.455f), (0.03f, 0.452f) }, seg, band, false, false);
            Disc(v, c, t, 0.03f, 0.452f, seg, band, true); Disc(v, c, t, -0.13f, 0.455f, seg, band, false);
            // cap and fuse (grenade.glb Grenade_fuse)
            Lathe(v, c, t, new[] { (0.34f, 0.21f), (0.44f, 0.21f) }, seg, metal, true, false);
            Lathe(v, c, t, new[] { (0.44f, 0.12f), (0.50f, 0.12f) }, 8, metal, true, false);
            // safety lever (spoon) down the side
            Box(v, c, t, new Vector3(0.37f, 0.22f, 0f), new Vector3(0.035f, 0.21f, 0.075f), Quaternion.Euler(0, 0, -14f), lever);
            var m = new Mesh { name = "OZ_Grenade" };
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static Mesh BuildBall(int seg, int rings)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int j = 0; j <= rings; j++)
            {
                float th = j * Mathf.PI / rings;
                for (int i = 0; i <= seg; i++) { float ph = i * Mathf.PI * 2f / seg; v.Add(new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph))); }
            }
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < seg; i++)
                {
                    int a = j * (seg + 1) + i, b = a + seg + 1;
                    t.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                }
            var m = new Mesh { name = "OZ_FuseSpark" };
            m.SetVertices(v); m.SetNormals(v); m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }
    }
}
