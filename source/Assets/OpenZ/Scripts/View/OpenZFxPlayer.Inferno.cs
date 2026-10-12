using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// One tick of the fort ruin blaze (OzFortInferno calls it every ~0.1 s): several large
    /// layered fire columns (three stacked leg_fire tongues each, 2.4-5 m, rising 3.5-7 m/s, the wreck flame ramp white-yellow ->
    /// orange -> red, additive glow = HDR bloom), embers and sparks pouring up with a swirl, a pulsing hot core glow at the heart and
    /// thick dark smoke rising from the fire (leg_smoke, the 8-variant smoke atlas with per-puff variant / rotation). k = strength
    /// (1 at the fall, settling to ~0.55, never dying to tiny flames).
    /// </summary>
    public partial class OpenZFxPlayer
    {
        public static float InfernoScale = 1.35f;
        public int InfernoFlames { get; private set; }
        /// <summary>
        /// leg_fire gain bloomed to white from the RTS camera).</summary>
        public static readonly Color InfernoCore = new Color(1f, 0.69f, 0.2f), InfernoEmber = new Color(1f, 0.42f, 0.1f);
        public int InfernoSmokePuffs { get; private set; }

        public void InfernoTick(Vector3 centre, float radius, float k)
        {
            var fire = Ch("leg_fire"); if (fire == null) return;
            var ember = Ch("leg_ember"); var sm = Ch("leg_smoke"); var core = Ch("blast_core") ?? Ch("m12_core");
            float gy = Ground(centre.x, centre.z);
            int columns = k > 0.8f ? 5 : 4;
            for (int i = 0; i < columns; i++)
            {
                float a = Rand() * Mathf.PI * 2f, r = radius * Mathf.Sqrt(Rand());
                var basePos = new Vector3(centre.x + Mathf.Cos(a) * r, gy + 0.5f + Rand() * 1.4f, centre.z + Mathf.Sin(a) * r);
                float lick = (2.4f + Rand() * 2.6f) * (0.7f + 0.45f * k) * InfernoScale;           // m: tall licking tongues
                for (int j = 0; j < 3; j++)
                {
                    var vel = new Vector3((Rand() - 0.5f) * 1.4f, (3.5f + Rand() * 3.5f) * (0.75f + 0.35f * k), (Rand() - 0.5f) * 1.4f);
                    var c = j == 0 ? InfernoCore : j == 1 ? OpenZFxCatalog.WreckFlameB : OpenZFxCatalog.WreckFlameC;
                    EmitP(fire, basePos + Vector3.up * (j * 0.8f * lick * 0.4f), vel, 0.55f + Rand() * 0.55f, lick * (1f - 0.18f * j), c, 1f / Mathf.Max(1f, fireGlowNow), j * 0.06f + Rand() * 0.04f);   // the blaze piles dozens of these tongues: it keeps its own brightness while the single puffs get the day gain
                }
                InfernoFlames++;
            }
            if (ember != null)
                for (int i = 0; i < 7; i++)
                {
                    float a = Rand() * Mathf.PI * 2f, r = radius * 0.8f * Mathf.Sqrt(Rand());
                    var pos = new Vector3(centre.x + Mathf.Cos(a) * r, gy + 1f + Rand() * 3f, centre.z + Mathf.Sin(a) * r);
                    var swirl = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * (0.8f + Rand() * 1.6f);
                    EmitP(ember, pos, swirl + Vector3.up * (4.5f + Rand() * 6f), 1.1f + Rand() * 1.3f, 0.12f + Rand() * 0.12f,
                          Rand() < 0.5f ? InfernoEmber : OpenZFxCatalog.WreckFlameB, 1f, Rand() * 0.08f);
                }
            if (core != null && Rand() < 0.4f)
                EmitP(core, new Vector3(centre.x, gy + 1.6f, centre.z), Vector3.zero, 0.5f + Rand() * 0.3f, radius * (1.1f + 0.5f * Rand()) * k, OpenZFxCatalog.WreckCore, 0.55f);
            // the flame tips, bigger, sooty (no pale burn-smoke tone, which the sun lit to beige), higher alpha
            if (sm != null)
            {
                int n = Rand() < 0.4f ? 2 : 1;
                for (int s2 = 0; s2 < n; s2++)
                {
                    float a = Rand() * Mathf.PI * 2f, r = radius * 0.55f * Rand();
                    var pos = new Vector3(centre.x + Mathf.Cos(a) * r, gy + 5.5f + Rand() * 3f, centre.z + Mathf.Sin(a) * r);
                    // first Play check: near-black puffs at 0.95 merged into a flat black blot from the RTS camera: charcoal greys with
                    // variation (the sun and the fire light still shape them), a little less dense, a touch smaller
                    var dark = Color.Lerp(OpenZFxCatalog.WreckSmokeDark, OpenZFxCatalog.WreckSmokeGrey, 0.2f + 0.55f * Rand());
                    EmitP(sm, pos, new Vector3((Rand() - 0.5f) * 1.6f, 2.6f + Rand() * 2.4f, (Rand() - 0.5f) * 1.6f), 3.0f + Rand() * 2.2f,
                          (3.6f + Rand() * 2.8f) * (0.75f + 0.4f * k), dark, 0.78f, Rand() * 0.1f);
                    InfernoSmokePuffs++;
                }
            }
        }
    }

    /// <summary>
    /// .InfernoTick every 0.1 s, a bright warm flickering point light on the ruin and its surroundings, a deep fire roar loop
    /// (sd_fire_crackle_loop + flame_loop1 an octave-ish down, 3D, broad). Full strength 20 s, then settling over 60 s to 0.55 and
    /// holding there (never tiny); gone with the scene (or after MaxLife s).</summary>
    public class OzFortInferno : MonoBehaviour
    {
        public static float MaxLife = 300f, LightIntensity = 14f, LightRange = 38f;
        public static int Spawned { get; private set; }
        public static OzFortInferno Last { get; private set; }
        Vector3 centre; float radius, born, next; Light lt; AudioSource crackle, roar, crackle2;
        static float duckFromDb, duckToDb, duckT0 = -10f, duckDur = 1f;
        public static float DuckDbNow => Mathf.Lerp(duckFromDb, duckToDb, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.unscaledTime - duckT0) / duckDur)));
        /// <summary>Fades the inferno roar + crackle down to db over seconds (only ever deeper; reset when a new inferno starts).</summary>
        public static void DuckAll(float db, float seconds, string why)
        {
            float now = DuckDbNow;
            if (db >= duckToDb - 0.01f) return;
            duckFromDb = now; duckToDb = db; duckT0 = Time.unscaledTime; duckDur = Mathf.Max(0.1f, seconds);
            Debug.Log($"[OpenZ] fort inferno: roar duck {now:F1} -> {db:F1} dB over {seconds:F1} s ({why}){(Last ? "" : " (no inferno burning)")}");
        }
        // to a random point every 1.8-3.6 s, with a slow random level and pitch drift
        float xw = 1f, xTarget = 1f, nextSwap;
        public float K { get; private set; }
        public float LightNow => lt ? lt.intensity : 0f;

        public static OzFortInferno Spawn(Vector3 centre, float radius)
        {
            var go = new GameObject("OZ_FortInferno");
            var f = go.AddComponent<OzFortInferno>();
            f.centre = centre; f.radius = Mathf.Clamp(radius, 3f, 12f);
            go.transform.position = centre;
            Spawned++; Last = f;
            duckFromDb = duckToDb = 0f; duckT0 = -10f;
            Debug.Log($"[OpenZ] fort inferno: the ruin centre {centre} burns (radius {f.radius:0.0} m, light {LightIntensity:0} / {LightRange:0} m, roar loop)");
            return f;
        }

        void Start()
        {
            born = Time.time;
            var gy = ZGame.I ? ZGame.I.GroundY(centre.x, centre.z) : centre.y;
            centre.y = gy;
            BuildColumns();
            var lg = new GameObject("InfernoLight"); lg.transform.SetParent(transform, false); lg.transform.position = centre + Vector3.up * 4.5f;
            lt = lg.AddComponent<Light>(); lt.type = LightType.Point; lt.color = new Color(1f, 0.55f, 0.22f); lt.range = LightRange;
            lt.intensity = 0f; lt.shadows = LightShadows.None; lt.renderMode = LightRenderMode.ForcePixel;
            crackle = Loop("OpenZ/Audio/Sfx/sd_fire_crackle_loop", 1f, 0.42f);
            crackle2 = Loop("OpenZ/Audio/Sfx/sd_fire_crackle_loop", 1.04f, 0.42f);
            roar = Loop("OpenZ/Audio/Sfx/flame_loop1", 0.62f, 0.5f);
            nextSwap = Time.time + Random.Range(1.8f, 3.6f);
        }

        AudioSource Loop(string res, float pitch, float vol)
        {
            var clip = Resources.Load<AudioClip>(res); if (!clip) { Debug.LogWarning("[OpenZ] fort inferno: " + res + " missing (no roar)"); return null; }
            var a = gameObject.AddComponent<AudioSource>();
            a.clip = clip; a.loop = true; a.playOnAwake = false; a.pitch = pitch; a.dopplerLevel = 0f; a.priority = 60;
            var ca = CombatAudio.I; if (ca) ca.Configure(a, null, CombatAudio.Profile.Broad, 160f); else { a.spatialBlend = 1f; a.maxDistance = 160f; }
            a.volume = 0f; a.time = Random.value * clip.length; a.Play();
            vols[a] = vol;
            return a;
        }
        readonly System.Collections.Generic.Dictionary<AudioSource, float> vols = new System.Collections.Generic.Dictionary<AudioSource, float>();

        void Update()
        {
            float age = Time.time - born;
            if (age > MaxLife) { Destroy(gameObject); return; }
            float rise = Mathf.SmoothStep(0f, 1f, age / 1.2f);
            K = rise * Mathf.Lerp(1f, 0.55f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(20f, 80f, age)));
            if (lt)
            {
                float fl = 0.78f + 0.14f * Mathf.PerlinNoise(Time.time * 7.3f, 0.2f) + 0.08f * Mathf.Sin(Time.time * 23f);
                lt.intensity = LightIntensity * K * fl;
            }
            if (crackle && crackle2 && Time.time >= nextSwap)
            {   // the silent copy jumps to a random point and takes over (0.45 s crossfade)
                nextSwap = Time.time + Random.Range(1.8f, 3.6f);
                var idle = xTarget > 0.5f ? crackle2 : crackle;
                if (idle.clip) idle.time = Random.value * Mathf.Max(0.1f, idle.clip.length - 0.1f);
                idle.pitch = Random.Range(0.94f, 1.07f);
                xTarget = 1f - xTarget;
            }
            xw = Mathf.MoveTowards(xw, xTarget, Time.deltaTime / 0.45f);
            float drift = 0.8f + 0.35f * Mathf.PerlinNoise(Time.time * 0.6f, 3.1f);
            float duck = Mathf.Pow(10f, DuckDbNow / 20f);
            foreach (var kv in vols)
            {
                if (!kv.Key) continue;
                float w = kv.Key == crackle ? xw * drift : kv.Key == crackle2 ? (1f - xw) * drift : 1f;
                kv.Key.volume = kv.Value * Mathf.Lerp(0.6f, 1f, K) * rise * w * duck;
            }
            DrawColumns();
            if (Time.time < next) return;
            next = Time.time + 0.1f;
            var fxp = OpenZFxPlayer.I; if (fxp && fxp.isActiveAndEnabled) fxp.InfernoTick(centre, radius, K);
        }

        void OnDestroy()
        {
            if (Last == this) Last = null;
            if (colMesh) Destroy(colMesh);
            if (colMat) Destroy(colMat);
            if (bedMesh) Destroy(bedMesh);
            if (bedMat) Destroy(bedMat);
            if (hazeMat) Destroy(hazeMat);
            if (hazeQuad) Destroy(hazeQuad);
        }

        // noise-licked tongue, HDR heat ramp) rooted in the ruin centre, plus a rising heat-shimmer plume over the blaze (OpenZ/HeatDistort
        // mode 2). One static mesh, one draw call; the shader animates every column.
        public static int Columns = 16;
        public static float ColumnHeightMin = 4f, ColumnHeightMax = 6.5f, HazeStrength = 0.013f;
        Mesh colMesh, hazeQuad, bedMesh; Material colMat, hazeMat, bedMat; MaterialPropertyBlock colBlock, hazeBlock;
        public static float BedRadius { get; private set; }
        public int ColumnCount { get; private set; }
        public static int ColumnsDrawn { get; private set; }

        void BuildColumns()
        {
            var tmpl = Resources.Load<Material>("OpenZ/FX/M_InfernoFlame");
            if (tmpl) colMat = new Material(tmpl) { name = "InfernoFlame (runtime)" };
            else { var sh = Shader.Find("OpenZ/InfernoFlame"); if (sh) colMat = new Material(sh) { name = "InfernoFlame (runtime)" }; }
            if (!colMat) { Debug.LogWarning("[OpenZ] fort inferno: OpenZ/InfernoFlame material missing (no flame columns)"); return; }
            if (!colMat.GetTexture("_MainTex")) colMat.SetTexture("_MainTex", Resources.Load<Texture2D>("OpenZ/FX/Pyro/legacy-flame"));
            if (!colMat.GetTexture("_NoiseTex")) colMat.SetTexture("_NoiseTex", Resources.Load<Texture2D>("OpenZ/FX/Pyro/legacy-noise"));
            var game = ZGame.I; var rnd = new System.Random(Mathf.RoundToInt(centre.x * 13f + centre.z * 7f));
            int n = Columns; var pos = new Vector3[n * 4]; var d0 = new Vector4[n * 4]; var d1 = new Vector4[n * 4]; var idx = new int[n * 6];
            for (int i = 0; i < n; i++)
            {
                bool big = i < n * 0.6f;                                                                     // the core columns, then smaller licks around them
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f, rr = radius * (big ? 0.75f : 1.15f) * Mathf.Sqrt((float)rnd.NextDouble());
                float x = centre.x + Mathf.Cos(a) * rr, z = centre.z + Mathf.Sin(a) * rr;
                float gy = (game ? game.GroundY(x, z) : centre.y) + 0.35f + (float)rnd.NextDouble() * 0.8f;
                float hgt = big ? Mathf.Lerp(ColumnHeightMin, ColumnHeightMax, (float)rnd.NextDouble()) : Mathf.Lerp(2.2f, 3.6f, (float)rnd.NextDouble());
                float wid = hgt * (0.36f + 0.12f * (float)rnd.NextDouble());
                float seed = (float)rnd.NextDouble() * 10f, speed = 0.55f + 0.6f * (float)rnd.NextDouble(), phase = (float)rnd.NextDouble();
                for (int c = 0; c < 4; c++)
                {
                    float u = c == 1 || c == 2 ? 1f : 0f, v = c >= 2 ? 1f : 0f;
                    pos[i * 4 + c] = new Vector3(x, gy, z);
                    d0[i * 4 + c] = new Vector4(u, v, seed, wid);
                    d1[i * 4 + c] = new Vector4(hgt, speed, phase, 0f);
                }
                int b = i * 4;
                idx[i * 6] = b; idx[i * 6 + 1] = b + 2; idx[i * 6 + 2] = b + 1; idx[i * 6 + 3] = b; idx[i * 6 + 4] = b + 3; idx[i * 6 + 5] = b + 2;
            }
            colMesh = new Mesh { name = "OZ_InfernoColumns" };
            colMesh.vertices = pos;
            colMesh.SetUVs(0, new System.Collections.Generic.List<Vector4>(d0));
            colMesh.SetUVs(1, new System.Collections.Generic.List<Vector4>(d1));
            colMesh.SetIndices(idx, MeshTopology.Triangles, 0, false);
            colMesh.bounds = new Bounds(centre + Vector3.up * 4f, new Vector3(radius * 2.6f + 8f, 12f, radius * 2.6f + 8f));
            ColumnCount = n;
            colBlock = new MaterialPropertyBlock(); hazeBlock = new MaterialPropertyBlock();
            var hd = Resources.Load<Material>("OpenZ/FX/M_HeatDistort");
            if (hd)
            {
                hazeMat = new Material(hd) { name = "InfernoHaze (runtime)" };
                hazeMat.SetFloat("_Mode", 2f); hazeMat.SetFloat("_Inner", 0.1f); hazeMat.SetFloat("_NoiseScale", 7f); hazeMat.SetFloat("_Speed", 2.6f); hazeMat.SetFloat("_Strength", HazeStrength);
                hazeQuad = new Mesh { name = "OZ_InfernoHazeQuad" };
                hazeQuad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
                hazeQuad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                hazeQuad.triangles = new[] { 0, 2, 1, 0, 3, 2 }; hazeQuad.RecalculateBounds();
            }
            bedMat = new Material(colMat) { name = "InfernoEmberBed (runtime)", renderQueue = colMat.renderQueue - 1 };
            bedMat.SetFloat("_Mode", 1f); bedMat.SetFloat("_SoftFade", 0.45f); bedMat.SetFloat("_Gain", 1f);
            float br = radius * 1.35f + 1.5f, by = (game ? game.GroundY(centre.x, centre.z) : centre.y) + 0.3f;
            bedMesh = new Mesh { name = "OZ_InfernoEmberBed" };
            bedMesh.vertices = new[] { new Vector3(centre.x - br, by, centre.z - br), new Vector3(centre.x + br, by, centre.z - br), new Vector3(centre.x + br, by, centre.z + br), new Vector3(centre.x - br, by, centre.z + br) };
            float bs = (float)rnd.NextDouble() * 10f;
            bedMesh.SetUVs(0, new System.Collections.Generic.List<Vector4> { new Vector4(0, 0, bs, 0), new Vector4(1, 0, bs, 0), new Vector4(1, 1, bs, 0), new Vector4(0, 1, bs, 0) });
            bedMesh.SetUVs(1, new System.Collections.Generic.List<Vector4> { Vector4.zero, Vector4.zero, Vector4.zero, Vector4.zero });
            bedMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            bedMesh.bounds = new Bounds(new Vector3(centre.x, by, centre.z), new Vector3(br * 2f, 2f, br * 2f));
            BedRadius = br;
            Debug.Log($"[OpenZ] fort inferno: {n} InfernoFlame columns ({ColumnHeightMin:0}-{ColumnHeightMax:0.#} m core, 2.2-3.6 m licks, depth fade {colMat.GetFloat("_SoftFade"):0.0} m), ember bed {br * 2f:0} m, heat plume {(hazeMat ? "on" : "missing")}");
        }

        void DrawColumns()
        {
            if (!colMesh || !colMat) return;
            colBlock.SetFloat("_K", K);
            var rp = new RenderParams(colMat) { layer = 0, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false, matProps = colBlock, worldBounds = colMesh.bounds };
            Graphics.RenderMesh(rp, colMesh, 0, Matrix4x4.identity);
            ColumnsDrawn = ColumnCount;
            if (bedMesh && bedMat)
            {
                var bp = new RenderParams(bedMat) { layer = 0, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false, matProps = colBlock, worldBounds = bedMesh.bounds };
                Graphics.RenderMesh(bp, bedMesh, 0, Matrix4x4.identity);
            }
            var cam = Camera.main;
            if (hazeMat && hazeQuad && cam)
            {
                var at = centre + Vector3.up * 2.2f; var cp = cam.transform.position;
                var rot = Quaternion.LookRotation(at - cp, Vector3.up);
                float w = radius * 2.4f + 5f, h = 14f;
                hazeBlock.SetFloat("_Alpha", Mathf.Clamp01(0.35f + 0.65f * K));
                var hp = new RenderParams(hazeMat) { layer = 0, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false, matProps = hazeBlock };
                Graphics.RenderMesh(hp, hazeQuad, 0, Matrix4x4.TRS(at + rot * Vector3.up * (h * 0.5f), rot, new Vector3(w, h, 1f)));
            }
        }
    }
}
