using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// emit / step rules (golden-angle bursts, gravity, size envelopes, smoke alpha fades, velocity-aligned sparks and
    /// projectiles) and the same explosion recipe (plan/04 4.1). HDR emissive kinds feed the Bloom. Projectiles fly over the
    /// sim's missile delay and do not explode by themselves: the sim's detonation event triggers Explosion() at that moment.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class FxManager : MonoBehaviour
    {
        public static FxManager I { get; private set; }

        class Slot
        {
            public bool active; public Vector3 pos, vel; public float age, life, scale, gravity, hitDistance, speed, dustOpacity = -1, contactAge = -1, trail;
            public int serial; public bool grounded, rocketTrail;
            public float speed2 => vel.sqrMagnitude;
        }

        class Pool
        {
            public string kind; public Mesh mesh; public Material mat; public Slot[] slots; public bool fading, aligned, flat, rubble, streak;
            public Vector3 axis = Vector3.up; public Matrix4x4[] mats = new Matrix4x4[1023]; public float[] alpha = new float[1023];
            public MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        }

        readonly Dictionary<string, Pool> pools = new Dictionary<string, Pool>();
        int serial;
        Camera cam;
        RtsCamera rts;
        // A pool of point lights (Forward+, no shadows) with a white-hot flash, a warm decay and Perlin flicker.
        class FxLight { public Light l; public float age = 99, life, power, range, flicker, seed; public Color color; public bool flash; }
        readonly List<FxLight> lights = new List<FxLight>();
        const int MaxLights = 28;
        Mesh scorchMesh; Material scorchMat; readonly List<(Vector3 p, float age, float size, float rot)> scorches = new List<(Vector3, float, float, float)>();
        public int Emitted { get; private set; }

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
        /// <summary>HDR colour with the gain applied in LINEAR space. SetColor linearises per channel, so Hex * 1.9 became
        /// (4.1, 1.8, 0.08) in the shader and ACES turned every fireball white; (linear * gain).gamma round-trips exactly.</summary>
        static Color Lin(string h, float gain) { var c = (Hex(h).linear * gain).gamma; c.a = 1; return c; }

        void Awake()
        {
            I = this;
            cam = Camera.main; rts = FindFirstObjectByType<RtsCamera>();
            var lib = Resources.Load<GameObject>("OpenZ/FX/combat_fx");
            var shader = Shader.Find("OpenZ/FxLit");
            if (!lib || !shader) { Debug.LogError("FxManager: combat_fx library or OpenZ/FxLit shader missing"); enabled = false; return; }
            // kind -> (mesh, capacity), M12 definitions (capacities raised for Zod's 10 shots/s guns)
            var defs = new (string kind, string mesh, int cap)[]
            {
                ("fire", "FX_Fireball", 192), ("flamepuff", "FX_Smoke", 320), ("core", "FX_Core", 64), ("smoke", "FX_Smoke", 480), ("dark", "FX_DarkSmoke", 240),
                ("spark", "FX_Spark", 448), ("ring", "FX_ShockRing", 40), ("rocket", "FX_Rocket", 48), ("flame", "FX_Flame", 160),
                ("shell", "FX_Shell", 64), ("bomb", "FX_Shell", 48), ("bullet", "FX_Spark", 256), ("bolt", "FX_Bolt", 64),
                ("stone", "FX_RubbleStone", 320), ("metal", "FX_RubbleMetal", 160), ("wood", "FX_RubbleWood", 48),
                ("dust", "FX_Smoke", 160), ("pop", "FX_Spark", 96), ("glow", "FX_Core", 48)
            };
            var meshes = new Dictionary<string, (Mesh, Color)>();
            foreach (var mf in lib.GetComponentsInChildren<MeshFilter>(true))
            {
                if (meshes.ContainsKey(mf.name)) continue;
                var m = Instantiate(mf.sharedMesh); var M = lib.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;   // geometry.applyMatrix4(matrixWorld)
                var v = m.vertices; var n = m.normals;
                for (int i = 0; i < v.Length; i++) { v[i] = M.MultiplyPoint3x4(v[i]); if (i < n.Length) n[i] = M.MultiplyVector(n[i]).normalized; }
                m.vertices = v; if (n.Length == v.Length) m.normals = n; else m.RecalculateNormals(); m.RecalculateBounds();
                var src = mf.GetComponent<MeshRenderer>() ? mf.GetComponent<MeshRenderer>().sharedMaterial : null;
                Color col = src ? (src.HasProperty("baseColorFactor") ? src.GetColor("baseColorFactor") : src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : src.color) : Color.white;
                meshes[mf.name] = (m, col);
            }
            foreach (var (kind, meshName, cap) in defs)
            {
                if (!meshes.TryGetValue(meshName, out var mc)) { Debug.LogWarning("FX mesh missing " + meshName); continue; }
                var p = new Pool { kind = kind, mesh = mc.Item1, slots = new Slot[cap] };
                for (int i = 0; i < cap; i++) p.slots[i] = new Slot();
                var b = p.mesh.bounds.size; p.axis = b.x >= b.y && b.x >= b.z ? Vector3.right : b.z >= b.y ? Vector3.forward : Vector3.up;
                p.fading = kind == "smoke" || kind == "dark" || kind == "dust" || kind == "pop" || kind == "ring" || kind == "glow" || kind == "fire" || kind == "flamepuff";
                p.aligned = kind == "spark" || kind == "rocket" || kind == "shell" || kind == "flame" || kind == "bullet" || kind == "bolt" || kind == "bomb";
                p.flat = kind == "ring"; p.rubble = kind == "stone" || kind == "metal" || kind == "wood";
                p.streak = kind == "fire" || kind == "spark" || kind == "bullet" || kind == "flame";
                var mat = new Material(shader) { enableInstancing = true, name = "FX_" + kind };
                Color baseC = mc.Item2, emis = Color.black, rim = Color.black; float unlit = 0, rimPow = 0, soft = 0;
                // those went over 1 and the fireballs and rings read white. Here: saturated colours at low gains (ACES keeps the hue,
                // Unity ACES also desaturates anything above ~1.0 to pale yellow, so fire keeps an almost pure red/orange hue (tiny green, no blue: ACES keeps it orange even at 1.7, bright enough to beat the sunlit sand) and only the small core really blooms), hot yellow centres with red edges, dark smoke.
                switch (kind)
                {
                    case "core": emis = Lin("#ffb020", 2.6f); rim = Lin("#ff5008", 1.4f); rimPow = 1.2f; baseC = Color.black; break;
                    case "fire": emis = Lin("#ff8a10", 1.8f); rim = Lin("#c81004", 1.7f); rimPow = 1.1f; baseC = Color.black; break;   // yellow-orange heart, red edges
                    case "flame": emis = Lin("#ffb02a", 1.1f); rim = Lin("#d0300a", 0.9f); rimPow = 1.4f; baseC = Color.black; break;
                    case "flamepuff": emis = Lin("#ff7418", 1.25f); rim = Lin("#b81404", 0.8f); rimPow = 1.2f; baseC = Color.black; soft = 0.95f; break;   // soft additive flame (pyro)
                    case "spark": emis = Lin("#ff8a1c", 1.6f); baseC = Color.black; break;
                    case "bullet": emis = Lin("#ffe08a", 2.4f); baseC = Color.black; break;                // hot tracer core, blooms
                    case "bolt": emis = Lin("#3dff64", 1.8f); baseC = Color.black; break;
                    case "pop": emis = Lin("#83c4ff", 1.4f); baseC = Hex("#abd9ff"); break;
                    case "smoke": baseC = Hex("#5c524a"); soft = 0.8f; break;
                    case "dark": baseC = Hex("#231d1a"); soft = 0.8f; break;
                    case "dust": baseC = Hex("#c89a66"); soft = 1f; break;
                    case "ring": emis = Lin("#ff7026", 0.75f); baseC = Color.black; unlit = 1; break;
                    case "glow": emis = Lin("#ff8a30", 3.2f); baseC = Color.black; unlit = 1; soft = 1f; break;   // ground flash (additive)
                    case "shell": case "bomb": emis = Lin("#ffb13b", 1.2f); break;
                    case "stone": baseC = Hex("#9a7650"); break;                                     // glb cream read as white confetti on sand
                }
                baseC.a = 1;
                mat.SetColor("_BaseColor", baseC); mat.SetColor("_EmissionColor", emis); mat.SetFloat("_Unlit", unlit);
                mat.SetColor("_RimColor", rim); mat.SetFloat("_RimPower", rimPow); mat.SetFloat("_SoftEdge", soft);
                if (kind == "ring") p.mesh = ThinRing(p.mesh.bounds.extents.x, 0.9f, 48);
                if (kind == "glow") { p.mesh = GlowDisc(40, 6); p.flat = true; }
                if (p.fading)
                {
                    mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    mat.SetFloat("_ZWrite", 0); mat.renderQueue = 3000; mat.SetFloat("_Cull", kind == "ring" ? 0 : 2);
                }
                if (kind == "glow") { mat.SetFloat("_DstBlend", (float)BlendMode.One); mat.renderQueue = 3010; }   // adds light to the sand
                if (kind == "flamepuff") { mat.SetFloat("_DstBlend", (float)BlendMode.One); mat.renderQueue = 3050; }   // overlapping puffs add up to a flame
                if (kind == "fire" || kind == "core" || kind == "spark" || kind == "flame") mat.renderQueue = 3050;   // after the tan dust + smoke puffs (they tinted the fire peach)
                p.mat = mat;
                pools[kind] = p;
            }
            for (int i = 0; i < MaxLights; i++)
            {
                var go = new GameObject("FxLight" + i); go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>(); l.type = LightType.Point; l.shadows = LightShadows.None; l.enabled = false;
                l.renderMode = LightRenderMode.ForcePixel;
                lights.Add(new FxLight { l = l, seed = i * 13.37f });
            }
            scorchMesh = Disc(24); scorchMat = new Material(Shader.Find("OpenZ/Overlay")); scorchMat.SetColor("_Color", new Color(0.08f, 0.06f, 0.05f, 0.62f));
            ScorchDecals.Ensure();                                                                  // combatview: bake the scorch atlas before the first blast
        }

        /// <summary>Flat annulus (XZ) with the shock ring's outer radius and a thin band (inner = outer * innerFrac).</summary>
        /// <summary>Unit flat disc whose normals tilt from up (centre) to horizontal (rim): with FxLit's N.V soft edge it
        /// reads as a soft radial light pool on the ground from any RTS view angle.</summary>
        static Mesh GlowDisc(int seg, int rings)
        {
            var v = new List<Vector3> { Vector3.zero }; var n = new List<Vector3> { Vector3.up }; var t = new List<int>();
            for (int r = 1; r <= rings; r++)
            {
                float k = r / (float)rings, th = Mathf.Lerp(0f, 88f, Mathf.Pow(k, 0.8f)) * Mathf.Deg2Rad;
                for (int i = 0; i < seg; i++)
                {
                    float a = i * Mathf.PI * 2 / seg; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    v.Add(d * k); n.Add((d * Mathf.Sin(th) + Vector3.up * Mathf.Cos(th)).normalized);
                }
            }
            for (int i = 0; i < seg; i++) t.AddRange(new[] { 0, 1 + (i + 1) % seg, 1 + i });
            for (int r = 1; r < rings; r++)
                for (int i = 0; i < seg; i++)
                {
                    int a0 = 1 + (r - 1) * seg + i, a1 = 1 + (r - 1) * seg + (i + 1) % seg, b0 = a0 + seg, b1 = a1 + seg;
                    t.AddRange(new[] { a0, a1, b1, a0, b1, b0 });
                }
            var m = new Mesh { name = "FX_GlowDisc" }; m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }

        static Mesh ThinRing(float outer, float innerFrac, int seg)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2 / seg; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d * outer); v.Add(d * outer * innerFrac); n.Add(Vector3.up); n.Add(Vector3.up);
                if (i < seg) { int k = i * 2; t.AddRange(new[] { k, k + 1, k + 2, k + 1, k + 3, k + 2 }); }
            }
            var m = new Mesh { name = "FX_ThinRing" }; m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }

        static Mesh Disc(int seg)
        {
            var v = new List<Vector3> { Vector3.zero }; var c = new List<Color> { new Color(1, 1, 1, 1) }; var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2 / seg, r = 1f + 0.18f * Mathf.Sin(a * 5f) * Mathf.Cos(a * 3f);
                v.Add(new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r)); c.Add(new Color(1, 1, 1, 0));
                if (i < seg) t.AddRange(new[] { 0, i + 2, i + 1 });
            }
            var m = new Mesh(); m.SetVertices(v); m.SetColors(c); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
        }

        float Ground(float x, float z) => ZGame.I ? ZGame.I.GroundY(x, z) : 0f;

        Slot Emit(string kind, Vector3 pos, Vector3 vel, float life, float scale = 1)
        {
            if (!pools.TryGetValue(kind, out var p)) return null;
            Slot s = null;
            foreach (var c in p.slots) if (!c.active) { s = c; break; }
            if (s == null) { s = p.slots[0]; foreach (var c in p.slots) if (c.serial < s.serial) s = c; }   // recycle the oldest
            s.active = true; s.pos = pos; s.vel = vel; s.age = 0; s.life = life; s.scale = scale; s.serial = ++serial;
            s.gravity = 0; s.hitDistance = 0; s.speed = 0; s.dustOpacity = -1; s.contactAge = -1; s.grounded = false; s.rocketTrail = false; s.trail = 0;
            Emitted++;
            return s;
        }

        // ------------------------------------------------------------------ effects
        /// <summary>Capture aid (editor only): &gt;= 0 pauses the editor this many seconds after the next big explosion.</summary>
        public static float BreakAfterExplosion = -1;
        /// <summary>Capture aid (editor only): pause the editor when Time.time reaches this (set to Time.time + delay).</summary>
        public static float BreakAtTime = -1;
        public static Vector3 LastBigExplosion;

        System.Collections.IEnumerator BreakLater(float s) { yield return new WaitForSeconds(s); Debug.Break(); }

        /// <summary>Every explosion (position, strength): decor props break (PropBreakables).</summary>
        public static event System.Action<Vector3, float> Exploded;
        /// <summary>Lets OpenZFxPlayer callers fire the legacy event (PropBreakables) without drawing the old explosion.</summary>
        public static void RaiseExploded(Vector3 at, float strength) => Exploded?.Invoke(at, strength);

        public void Explosion(Vector3 at, float strength)
        {
            Exploded?.Invoke(at, strength);
            if (BreakAfterExplosion >= 0 && strength >= 1f) { LastBigExplosion = at; StartCoroutine(BreakLater(BreakAfterExplosion)); BreakAfterExplosion = -1; }
            if (OpenZFxPlayer.I && OpenZFxPlayer.I.isActiveAndEnabled) { OpenZFxPlayer.I.BlastLegacy(at, strength); return; }
            float floor = Ground(at.x, at.z) + 0.20f; at.y = Mathf.Max(at.y, floor);
            // shock ring and more shake than the literal recipe
            Dust(at, strength * 1.3f);
            float rs = Mathf.Sqrt(strength);
            for (int j = 0; j < 14; j++) { float a = j * 2.399f; var s = Emit(j % 3 != 0 ? "stone" : "metal", at, new Vector3(Mathf.Cos(a) * (2.5f + j % 4) * rs, 6.5f + j % 4 * 1.6f, Mathf.Sin(a) * (2.5f + j % 4) * rs), 3.2f + j % 3 * 0.3f, (0.26f + j % 3 * 0.1f) * rs); if (s != null) s.gravity = 9.81f; }
            for (int j = 0; j < 8; j++) Emit("fire", at + new Vector3(Mathf.Cos(j * 2.4f) * 0.55f * rs, 0.3f + j * 0.12f, Mathf.Sin(j * 2.4f) * 0.55f * rs), new Vector3(Mathf.Cos(j * 2.4f) * 1.9f, 2.2f + j % 3 * 0.4f, Mathf.Sin(j * 2.4f) * 1.9f) * rs, 0.5f + j * 0.06f, (0.95f + j % 4 * 0.14f) * strength);
            Emit("core", at, new Vector3(0, 0.3f, 0), 0.26f, 1.35f * strength);
            Emit("core", at + Vector3.up * 0.4f, Vector3.zero, 0.09f, 2.1f * strength);                     // the first-frames flash that blooms
            Emit("glow", new Vector3(at.x, Ground(at.x, at.z) + 0.12f, at.z), Vector3.zero, 0.38f, 5.5f * rs);
            for (int j = 0; j < 18; j++) { float a = j * 2.399f; Emit(j % 2 == 0 ? "dark" : "smoke", at + Vector3.up * (j % 3) * 0.3f, new Vector3(Mathf.Cos(a) * (1 + j % 3) * 0.8f, 1.5f + j % 5 * 0.45f, Mathf.Sin(a) * (1 + j % 3) * 0.8f), 1.9f + j % 4 * 0.3f, (0.65f + j % 3 * 0.18f) * strength); }
            for (int j = 0; j < 28; j++) { float a = j * 2.399f; var s = Emit("spark", at, new Vector3(Mathf.Cos(a) * (3.5f + j % 4), 2.5f + j % 5, Mathf.Sin(a) * (3.5f + j % 4)) * rs, 0.5f + j % 5 * 0.12f, 0.6f * strength); if (s != null) s.gravity = 9.81f; }
            Emit("ring", new Vector3(at.x, Ground(at.x, at.z) + 0.07f, at.z), Vector3.zero, 0.6f, 1.15f * strength);
            Scorch(at, 1.1f + 0.8f * strength);
            LightPulse(at, strength);
            OpenZFxShake.I?.Impact(at, strength * OpenZFxCatalog.LegacyToM12);
        }

        /// <summary>Fort death core blast under the mushroom cloud: big fireball, the wide ground shockwave, large scorch,
        /// a long bright light pulse and heavy camera shake (plan/04 4.1b).</summary>
        public void NukeBlast(Vector3 at)
        {
            float floor = Ground(at.x, at.z);
            OpenZFxShake.I?.TimedRumble(new Vector3(at.x, floor + 1.5f, at.z), OpenZFxCatalog.ShakeFortRumble, OpenZFxCatalog.ShakeFortRumbleSeconds, "fort_death");
            OzEndingVignette.Begin(-1);
            Explosion(new Vector3(at.x, floor + 1.5f, at.z), 3f);
            Emit("ring", new Vector3(at.x, floor + 0.08f, at.z), Vector3.zero, 1.8f, 6.5f);
            Emit("ring", new Vector3(at.x, floor + 0.09f, at.z), Vector3.zero, 1.1f, 3.5f);
            Scorch(at, 14f);
            AddLight(at + Vector3.up * 12f, new Color(1f, 0.6f, 0.28f), 38f, 90f, 2.6f, 0.25f, true);
        }

        /// <summary>demolitionDust: a broad lingering cloud over a collapsed building (8 big puffs, ~3 s).</summary>
        public void DemolitionDust(Vector3 at, float width, float depth, float strength = 1f, float opacity = 0.6f, float sizeMul = 1f)
        {
            for (int j = 0; j < 8; j++)
            {
                float a = j * 2.399f + serial * 0.17f;
                var p = at + new Vector3(Mathf.Cos(a) * width * 0.42f, 1.0f + j % 3 * 0.3f, Mathf.Sin(a) * depth * 0.42f);
                var s = Emit("dust", p, new Vector3(Mathf.Cos(a) * 1.3f, 0.65f + j % 3 * 0.12f, Mathf.Sin(a) * 1.3f), 2.8f + j * 0.1f, (5.2f + j % 3 * 1.1f) * strength * 0.5f * sizeMul);
                if (s != null) s.dustOpacity = opacity;
            }
        }

        /// <summary>A small fire with dark smoke rising (burning rubble).</summary>
        public void Burn(Vector3 at, float strength = 1f)
        {
            // ground_lights_fix: was 2.8 at 0.6 m, range 5.5 (the pool ended ~1 m out, under the flame sprite): 12 at 1.3 m, range 9
            // lights the ground ~3 m around the fire at night (1x the moonlit sand at 3 m, 11x under it)
            AddLight(at + Vector3.up * 1.3f, new Color(1f, 0.46f, 0.16f), 12f * strength, 9f, 0.5f, 0.5f);
            Emit("fire", at, new Vector3(Random.Range(-0.3f, 0.3f), 1.4f, Random.Range(-0.3f, 0.3f)), 0.55f, 0.35f * strength);
            if (Random.value < 0.6f) Emit("dark", at + Vector3.up * 0.6f, new Vector3(0.3f, 1.6f, 0.2f), 2.2f, 0.45f * strength);
        }

        public void Dust(Vector3 at, float strength = 0.5f)
        {
            for (int j = 0; j < 5; j++) { float a = j * 2.399f; var s = Emit("dust", at, new Vector3(Mathf.Cos(a) * 1.7f, 0.4f + j * 0.08f, Mathf.Sin(a) * 1.7f), 1.1f + j * 0.11f, (0.4f + j * 0.09f) * strength); if (s != null) s.dustOpacity = 0.32f; }
        }

        public void Pop(Vector3 at, float strength = 0.6f)
        {
            for (int j = 0; j < 9; j++) { float a = j * 2.399f; Emit("pop", at, new Vector3(Mathf.Cos(a) * 1.4f, 1.1f + j % 3 * 0.4f, Mathf.Sin(a) * 1.4f), 0.40f, 0.16f * strength); }
            Dust(at, 0.26f * strength);
        }

        /// <summary>breakup(): rubble + smoke burst (rocks, huts, crates).</summary>
        /// <summary>A rebuilt bridge must not wear the rubble of its own destruction (critic 09:2x): chunks within the radius
        /// retire at once (the pop-out scale-down plays over the next half second).</summary>
        public void ClearRubble(Vector3 at, float radius)
        {
            foreach (var kv in pools)
            {
                var p = kv.Value; if (!p.rubble) continue;
                foreach (var sl in p.slots)
                {
                    if (!sl.active) continue;
                    var d = sl.pos - at; d.y = 0; if (d.sqrMagnitude > radius * radius) continue;
                    sl.life = Mathf.Min(sl.life, sl.age + 0.5f);
                }
            }
        }

        public void Breakup(Vector3 at, string material, float width, int count = 12, float chunk = 1f)
        {
            OzSoundV3.Debris(at, material, width, count);                                               // sound_v3: the chunks land
            for (int j = 0; j < count; j++)
            {
                float a = j * 2.399f;
                var p = at + new Vector3(Mathf.Cos(a) * width * 0.12f, j % 3 * 0.14f, Mathf.Sin(a) * width * 0.12f);
                var s = Emit(material, p, new Vector3(Mathf.Cos(a) * (1 + j % 3), 2 + j % 4, Mathf.Sin(a) * (1 + j % 3)), 2.8f, chunk * (0.8f + j % 4 * 0.15f)); if (s != null) s.gravity = 9.81f;
                Emit("smoke", p, new Vector3(Mathf.Cos(a) * 0.45f, 1 + j % 3 * 0.2f, Mathf.Sin(a) * 0.45f), 1.6f, 0.45f + width * 0.09f);
            }
            Dust(at, 0.8f);
        }

        public void MuzzleFlash(Vector3 at, bool heavy = false) => MuzzleFlash(at, Vector3.zero, heavy);

        /// <summary>
        /// (not a ball), plus the flicker light.</summary>
        public void MuzzleFlash(Vector3 at, Vector3 dir, bool heavy)
        {
            Emit("core", at, Vector3.zero, 0.05f, heavy ? 0.22f : 0.13f);
            if (dir.sqrMagnitude > 1e-4f)
            {
                dir.Normalize(); var side = Vector3.Cross(Vector3.up, dir).normalized;
                Emit("spark", at, dir * 14f, 0.05f, heavy ? 0.5f : 0.34f);
                Emit("spark", at, (dir + side * 0.7f).normalized * 9f, 0.04f, 0.2f);
                Emit("spark", at, (dir - side * 0.7f).normalized * 9f, 0.04f, 0.2f);
            }
            AddLight(at, new Color(1f, 0.72f, 0.38f), heavy ? 8f : 5f, heavy ? 8f : 6f, heavy ? 0.12f : 0.09f, 0.2f, true);   // ground_lights_fix: was 4.5 / 2.6, range 6 / 4.5, 0.1 / 0.07 s
        }

        public void Bullet(Vector3 from, Vector3 to)
        {
            if (OpenZFxPlayer.I && OpenZFxPlayer.I.isActiveAndEnabled) { OpenZFxPlayer.I.Shot(from, to); return; }
            var d = to - from; float dist = d.magnitude;
            MuzzleFlash(from, d, false);
            if (dist < 0.01f) return;
            const float v = 110f;                                                                        // fast thin tracer
            var s = Emit("bullet", from + d / dist * 0.4f, d / dist * v, Mathf.Max(0.001f, (dist - 0.4f) / v), 0.5f); if (s != null) { s.hitDistance = dist; s.speed = v; }
        }

        /// <summary>
        /// an instant HDR green core line, a wider glow that lingers a little longer, a green muzzle and impact flash.</summary>
        public void Laser(Vector3 from, Vector3 to)
        {
            var d = to - from; float dist = d.magnitude; if (dist < 0.01f) return;
            Beam(from, to, 0.09f, 0.16f, new Color(0.35f, 5f, 0.7f));                                // core
            Beam(from, to, 0.42f, 0.38f, new Color(0.06f, 0.9f, 0.16f));                             // glow trail
            Emit("pop", from, Vector3.zero, 0.1f, 0.25f);
            Impact(to, true);
            GreenPulse(to);
        }

        /// <summary>
        /// burning ground where it lands.</summary>
        public void Flame(Vector3 from, Vector3 to)
        {
            var d = to - from; float dist = Mathf.Max(0.1f, d.magnitude); var dir = d / dist;
            // critic L02 #7 ("pyro flame = yellow pills"): the fireballs flew at stream speed and the streak stretched them into
            // capsules. Now: puffs spread along the whole stream, drifting slowly forward and up, growing with distance, plus
            for (int j = 0; j < 12; j++)
            {
                float u = (j + Random.value) / 12f;
                var p0 = from + dir * (0.35f + u * (dist - 0.35f)) + Random.insideUnitSphere * (0.08f + 0.35f * u);
                Emit("flamepuff", p0, dir * Random.Range(1.2f, 2.4f) + Vector3.up * Random.Range(0.4f, 1.2f) + Random.insideUnitSphere * 0.5f, Random.Range(0.26f, 0.42f), 0.3f + u * 0.8f);
                if (j < 3) Emit("fire", from + dir * (0.3f + j * 0.18f), dir * 1.5f, 0.16f, 0.12f + j * 0.04f);                                   // hot core at the nozzle
            }
            for (int j = 0; j < 2; j++)
                Emit("dark", to + Vector3.up * 0.4f + Random.insideUnitSphere * 0.4f, new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(0.8f, 1.6f), Random.Range(-0.3f, 0.3f)), Random.Range(0.9f, 1.4f), Random.Range(0.45f, 0.7f));
            for (int j = 0; j < 2; j++)
                Emit("flame", from + dir * (0.2f + j * 0.2f), dir * 9f + Random.insideUnitSphere * 0.6f, 0.12f, 0.35f + j * 0.1f);
            for (int j = 0; j < 4; j++)
            {
                var s = Emit("spark", from + dir * Random.Range(0.5f, dist), new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(1.2f, 2.6f), Random.Range(-0.6f, 0.6f)), Random.Range(0.5f, 0.9f), 0.22f);
                if (s != null) s.gravity = -1.5f;                                                        // embers drift up
            }
            if (Random.value < 0.35f) Burn(new Vector3(to.x, Ground(to.x, to.z) + 0.15f, to.z), 0.8f);
            if (Random.value < 0.15f) Scorch(to, 0.9f);
            AddLight(from + dir * 1.4f + Vector3.up * 0.3f, new Color(1f, 0.52f, 0.18f), 5f, 8f, 0.32f, 0.6f);          // the jet lights its surroundings
            AddLight(new Vector3(to.x, Ground(to.x, to.z) + 0.6f, to.z), new Color(1f, 0.45f, 0.15f), 3.5f, 6f, 0.45f, 0.7f);   // burning ground
        }

        void GreenPulse(Vector3 at) => AddLight(at + Vector3.up, new Color(0.35f, 1f, 0.45f), 4f, 6f, 0.3f, 0.3f, true);

        // ------------------------------------------------------------------ beams (pooled LineRenderers, additive HDR)
        readonly List<(LineRenderer lr, Material m, float life, float age, Color c)> beams = new List<(LineRenderer, Material, float, float, Color)>();
        Shader beamShader;
        void Beam(Vector3 a, Vector3 b, float width, float life, Color hdr)
        {
            beamShader ??= Shader.Find("OpenZ/Beam"); if (!beamShader) return;
            int k = beams.FindIndex(x => x.age >= x.life);
            if (k < 0)
            {
                if (beams.Count >= 24) k = 0;
                else
                {
                    var go = new GameObject("Beam"); go.transform.SetParent(transform, false);
                    var lr = go.AddComponent<LineRenderer>(); lr.useWorldSpace = true; lr.positionCount = 2; lr.textureMode = LineTextureMode.Stretch;
                    lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false; lr.alignment = LineAlignment.View;
                    var m = new Material(beamShader); lr.sharedMaterial = m;
                    beams.Add((lr, m, 0, 1, Color.black)); k = beams.Count - 1;
                }
            }
            var e = beams[k];
            e.lr.SetPosition(0, a); e.lr.SetPosition(1, b); e.lr.widthMultiplier = width; e.lr.enabled = true;
            e.m.SetColor("_Color", hdr);
            beams[k] = (e.lr, e.m, life, 0, hdr);
        }
        void StepBeams(float dt)
        {
            for (int i = 0; i < beams.Count; i++)
            {
                var e = beams[i]; if (e.age >= e.life) { if (e.lr.enabled) e.lr.enabled = false; continue; }
                e.age += dt; float a = 1f - Mathf.Clamp01(e.age / e.life);
                e.lr.startColor = e.lr.endColor = new Color(1, 1, 1, a * a);
                beams[i] = e;
            }
        }

        public void LaunchFlash(Vector3 at, Vector3 heading, float strength = 1)
        {
            heading = heading.sqrMagnitude > 0.001f ? heading.normalized : Vector3.forward;
            Emit("core", at, Vector3.zero, 0.11f, 0.42f * strength);
            AddLight(at, new Color(1f, 0.66f, 0.32f), 6f * strength, 8f, 0.14f, 0.25f, true);
            for (int j = 0; j < 3; j++)
            {
                var p = at - heading * (0.3f + j * 0.18f);
                Emit("flame", p, -heading * 3.2f, 0.16f, 0.75f * strength);
                Emit("smoke", p, new Vector3(0.2f, 0.75f, 0.1f), 0.7f, 0.35f * strength);
            }
        }

        /// <summary>projectile(): lands on `target` after exactly `duration` s (ballistic arc, gravity g).</summary>
        public void Projectile(Vector3 from, Vector3 target, string kind, float duration, float g)
        {
            duration = Mathf.Max(0.05f, duration);
            var v = (target - from) / duration; v.y += g * duration / 2f;
            var s = Emit(kind, from, v, duration, kind == "bomb" ? 0.8f : 1f);
            if (s != null) { s.gravity = g; s.rocketTrail = kind == "rocket"; }
        }

        void Scorch(Vector3 at, float size)
        {
            // combatview: ONE scorch system, ScorchDecals (terrain-conforming grid, random size / rotation / variant, soft
            ScorchDecals.Add(at, size, size < 1.2f ? ScorchKind.Contact : size >= 6f ? ScorchKind.Wreck : ScorchKind.Explosion);
        }

        /// <summary>Explosion light: a white-hot flash, then the fireball's orange, flickering while it burns out.</summary>
        void LightPulse(Vector3 at, float strength) =>
            AddLight(at + Vector3.up * 1.2f, new Color(1f, 0.58f, 0.26f), 16f * strength, 12f + 10f * strength, 0.8f + 0.2f * strength, 0.35f, true);

        /// <summary>Takes the free / weakest light of the pool.</summary>
        public void AddLight(Vector3 at, Color color, float power, float range, float life, float flicker = 0.3f, bool flash = false)
        {
            FxLight best = null; float bestScore = float.MaxValue;
            foreach (var x in lights)
            {
                float remaining = x.age >= x.life ? -1f : x.power * (1f - x.age / x.life);
                if (remaining < bestScore) { bestScore = remaining; best = x; }
            }
            if (best == null || (bestScore > power * 1.2f)) return;                                 // pool full of brighter lights
            best.age = 0; best.life = life; best.power = power; best.range = range; best.flicker = flicker; best.color = color; best.flash = flash;
            best.l.transform.position = at; best.l.range = range; best.l.enabled = true;
        }

        void StepLights(float dt)
        {
            float t = Time.time;
            foreach (var x in lights)
            {
                if (!x.l.enabled) continue;
                x.age += dt;
                if (x.age >= x.life) { x.l.enabled = false; continue; }
                float k = x.age / x.life;
                float env = Mathf.Exp(-3.2f * k) * (1f - k);
                float fl = 1f + x.flicker * (Mathf.PerlinNoise(x.seed, t * 17f) - 0.5f) * 2f;
                bool hot = x.flash && x.age < 0.06f;
                x.l.intensity = x.power * env * fl * (hot ? 1.8f : 1f);
                x.l.color = hot ? Color.Lerp(new Color(1f, 0.95f, 0.85f), x.color, x.age / 0.06f) : x.color;
            }
        }

        static readonly int ScorchColorId = Shader.PropertyToID("_Color");
        MaterialPropertyBlock scorchMpb;

        // ------------------------------------------------------------------ step + draw
        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (BreakAtTime > 0 && Time.time >= BreakAtTime) { BreakAtTime = -1; Debug.Break(); }
            StepLights(dt);
            foreach (var p in pools.Values) StepPool(p, dt);
            StepBeams(dt);
            for (int i = scorches.Count - 1; i >= 0; i--)
            {
                var s = scorches[i]; s.age += dt; scorches[i] = s;
                if (s.age > 18f) { scorches.RemoveAt(i); continue; }
                var mpb = scorchMpb ??= new MaterialPropertyBlock(); mpb.SetColor(ScorchColorId, new Color(0.08f, 0.06f, 0.05f, 0.62f * (1 - Mathf.SmoothStep(0, 1, (s.age - 8f) / 10f))));
                Graphics.DrawMesh(scorchMesh, Matrix4x4.TRS(s.p, Quaternion.Euler(0, s.rot, 0), Vector3.one * s.size), scorchMat, 0, null, 0, mpb, ShadowCastingMode.Off, false);
            }
        }

        void StepPool(Pool p, float dt)
        {
            int count = 0;
            foreach (var s in p.slots)
            {
                if (!s.active) continue;
                s.age += dt;
                if (s.age >= s.life) { s.active = false; if (p.kind == "bolt" || p.kind == "bullet") Impact(s.pos, p.kind == "bolt"); continue; }
                if (s.gravity > 0 && !(p.rubble && s.grounded)) s.vel.y -= s.gravity * dt;
                s.pos += s.vel * dt;
                if (s.hitDistance > 0 && s.age * s.speed >= s.hitDistance) { s.active = false; Impact(s.pos, p.kind == "bolt"); continue; }
                if (p.rubble)
                {
                    float floor = Ground(s.pos.x, s.pos.z) + 0.15f;
                    if (s.pos.y < floor && (OpenZFxPlayer.IsWaterTile(s.pos) || OzWaterLanding.OpenWaterAt(s.pos, floor - 0.15f, out _)))   // b27: also the drawn water over a land tile (the widened river, the water under a destroyed bridge, a ravine stream)
                    {   // critic L02 #12 ("bridge debris floats for 10 s"): over water a chunk splashes once, sinks and is gone
                        if (!s.grounded) { s.grounded = true; s.life = Mathf.Min(s.life, s.age + 1.3f); if (s.contactAge < 0) s.contactAge = s.age; OzWaterLanding.Splash(new Vector3(s.pos.x, floor - 0.1f, s.pos.z), OzWaterSize.Small, "rubble chip", Mathf.Abs(s.vel.y)); }   // b27: the shared water landing rule (a few small splashes and plops for a whole burst)
                        s.vel = new Vector3(s.vel.x * 0.2f, -0.8f, s.vel.z * 0.2f);
                    }
                    else if (s.pos.y < floor)
                    {
                        s.pos.y = floor;
                        if (s.vel.y < -2.2f) { s.vel = new Vector3(s.vel.x * 0.62f, -s.vel.y * 0.38f, s.vel.z * 0.62f); if (s.contactAge < 0) s.contactAge = s.age; }
                        else { s.vel = Vector3.zero; s.grounded = true; if (s.contactAge < 0) s.contactAge = s.age; }
                    }
                }
                if (s.rocketTrail) { s.trail += dt; while (s.trail > 0.035f) { s.trail -= 0.035f; Emit("smoke", s.pos - s.vel.normalized * 0.4f, new Vector3(0, 0.4f, 0), 0.9f, 0.22f); } }
                float t = s.age / s.life, scale = s.scale, a = 1f;
                var rot = Quaternion.identity;
                switch (p.kind)
                {
                    case "smoke": case "dark": case "dust":
                        scale *= 0.5f + t * 1.8f; a = (s.dustOpacity >= 0 ? s.dustOpacity : 0.54f) * (1 - Mathf.SmoothStep(0, 1, (t - 0.22f) / 0.78f));
                        rot = Quaternion.Euler((s.serial * 0.6f + t * 0.4f) * Mathf.Rad2Deg, s.serial * 0.3f * Mathf.Rad2Deg, 0); break;
                    case "pop": a = 1 - Mathf.SmoothStep(0, 1, (t - 0.15f) / 0.85f); break;
                    case "core": scale *= Mathf.Sin(Mathf.PI * Mathf.Min(0.999f, t)) * (1 - t * 0.4f); break;
                    case "fire": scale *= Mathf.Sin(Mathf.PI * Mathf.Min(0.999f, t)) * (1 - t * 0.4f); a = 1 - Mathf.SmoothStep(0, 1, (t - 0.4f) / 0.6f); break;   // burns out, not solid balls
                    case "flamepuff": scale *= 0.55f + 1.1f * t; a = 0.85f * Mathf.Pow(1f - t, 1.6f); break;                                             // licks out, grows, fades
                    case "ring": scale *= 0.2f + 6f * t; a = 0.8f * (1 - t) * (1 - t); break;
                    case "glow": scale *= 0.7f + 0.5f * t; a = (1 - t) * (1 - t); break;
                }
                if (p.rubble)
                {
                    float k = Mathf.Min(s.contactAge >= 0 ? s.contactAge : s.age, 1f);
                    rot = Quaternion.Euler(s.serial * 0.23f * k * Mathf.Rad2Deg, s.serial * 0.17f * k * Mathf.Rad2Deg, s.serial * 0.09f * k * Mathf.Rad2Deg);
                    scale *= PopOut.DebrisScale(s.age, s.life);
                }
                if (p.aligned && s.vel.sqrMagnitude > 0.001f) rot = Quaternion.FromToRotation(p.axis, s.vel.normalized);
                var sc = p.flat ? new Vector3(scale, 1, scale) : Vector3.one * Mathf.Max(0.001f, scale);
                if (p.kind == "glow") sc = new Vector3(scale, 1f, scale);
                if (p.kind == "bullet") sc = Vector3.one * scale * 0.4f + p.axis * scale * 1.9f;             // thin and long (~0.8 m streak)
                // motion blur for fast fire and sparks: stretch along the velocity (the GPU-instanced FX have no motion vectors)
                if (p.streak && s.speed2 > 9f)
                {
                    float sp = Mathf.Sqrt(s.speed2), stretch = 1f + Mathf.Min(1.4f, sp * 0.045f);
                    var axis = p.aligned ? p.axis : Vector3.up;
                    if (!p.aligned) rot = Quaternion.FromToRotation(Vector3.up, s.vel / sp);
                    sc = Vector3.Scale(sc, Vector3.one + axis * (stretch - 1f));
                }
                p.mats[count] = Matrix4x4.TRS(s.pos, rot, sc); p.alpha[count] = a;
                if (++count == 1023) { Flush(p, count); count = 0; }
            }
            if (count > 0) Flush(p, count);
        }

        void Flush(Pool p, int count)
        {
            if (p.fading) p.mpb.SetFloatArray("_Alpha", p.alpha); else p.mpb.Clear();
            Graphics.DrawMeshInstanced(p.mesh, 0, p.mat, p.mats, count, p.mpb, ShadowCastingMode.Off, false);
        }

        /// <summary>
        /// orange sparks that fall and bounce off (gravity), and a blue-white arc flicker light on the hull and the ground.</summary>
        public void WeldSparks(Vector3 at, Vector3 outward, float strength = 1f)
        {
            Emit("core", at, Vector3.zero, 0.07f, 0.3f * strength);
            int n = 5 + (int)(4 * strength);
            for (int j = 0; j < n; j++)
            {
                var d = (outward * 1.2f + Random.insideUnitSphere * 0.9f + Vector3.up * 0.5f).normalized;
                var zs = OzFxSmooth.On ? OpenZFxPlayer.I : null;                                        // MOTION BLUR option: the sparks go to the effect player's spark channel (a streak that fades to nothing)
                if (zs && zs.isActiveAndEnabled) { zs.EmitChannel("wv2_spark", at, d * Random.Range(2.5f, 5.5f), Random.Range(0.25f, 0.55f), 0.045f, OzFxSmooth.WeldSpark, OzFxSmooth.WeldSparkAlpha); continue; }
                var s = Emit("spark", at, d * Random.Range(2.5f, 5.5f), Random.Range(0.25f, 0.55f), Random.Range(0.25f, 0.4f));   // 0.10-0.18 -> 0.25-0.4
                if (s != null) s.gravity = 9.81f;
            }
            AddLight(at + outward * 0.25f, new Color(0.62f, 0.78f, 1f), 2.4f * strength, 4.5f, 0.09f, 0.6f, true);
        }

        void Impact(Vector3 at, bool bolt)
        {
            Emit("core", at, Vector3.zero, bolt ? 0.13f : 0.05f, bolt ? 0.40f : 0.12f);
            int n = bolt ? 5 : 7;
            for (int j = 0; j < n; j++) { float a = j * 2.399f + Random.value; var s = Emit("spark", at, new Vector3(Mathf.Sin(a) * Random.Range(2f, 5f), Random.Range(1.5f, 4f), Mathf.Cos(a) * Random.Range(2f, 5f)), Random.Range(0.18f, 0.3f), 0.2f); if (s != null) s.gravity = 9.81f; }
            if (!bolt) { AddLight(at + Vector3.up * 0.3f, new Color(1f, 0.7f, 0.35f), 1.6f, 3f, 0.06f, 0.1f, true); Dust(at, 0.18f); }
        }
    }
}
