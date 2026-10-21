using System;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// spark / glow sprites, fireballs, cores, lit smoke and dust, rubble, embers, splashes and ripples. Ground-conforming meshes
    /// for the shock rings (M12 terrain ring + LEG outer / echo rings), the M12 blast ground light, scorch and tread marks. A
    /// pooled point-light flash per explosion, muzzle flash and impact. Rockets with the continuous M12 exhaust flame, smoke
    /// trail, LEG glow halo and sparkle trail; shells with a dark body and hot rear tracer; spinning grenades. The M12 camera
    /// impact (OpenZFxShake): a mini shake only when a big blast is on screen, clamped for the RTS camera.
    /// Projectiles never explode by themselves: the sim's MissileExploded event calls Blast() (same rule as FxManager).
    /// </summary>
    [DefaultExecutionOrder(110)]
    public partial class OpenZFxPlayer : MonoBehaviour
    {
        public static OpenZFxPlayer I { get; private set; }
        /// <summary>Creates the player in every scene that has a ZGame (set false to add it by hand).</summary>
        public static bool AutoCreate = true;
        /// <summary>Every blast (position, M12 strength). Use for props, wildlife, debris physics (FxManager.Exploded keeps firing
        /// for callers that still go through FxManager.Explosion).</summary>
        public static event Action<Vector3, float> Exploded;
        /// <summary>mlrs_heavy_v1: true while MlrsSalvo draws a cosmetic ripple blast: Exploded is not raised (no prop / lamp / sandbag
        /// breaking, no bloom flare); only the sim's real missile blast reaches those listeners.</summary>
        public static bool QuietBlast;
        /// <summary>Debris hook: (position, M12 strength, kind). BuildingBreaker / RockPillar / physics debris can listen here.</summary>
        public static event Action<Vector3, float, FxBlastKind> DebrisRequested;
        /// <summary>vehicle_lights: every live projectile once per frame (weapon, position, velocity, age in s), for the night
        /// glow (NightProjectileGlow). Raised from StepProjectiles; no subscriber = no cost.</summary>
        public static event Action<FxWeapon, Vector3, Vector3, float> ProjectileDrawn;
        /// <summary>vehicle_lights: every hitscan shot (from, to, flight seconds of the bolt / tracer head, weapon).</summary>
        public static event Action<Vector3, Vector3, float, FxWeapon> ShotTraced;

        [Header("Layers")]
        public bool pointLights = true;
        public bool groundLights = true, shockRings = true, scorchMarks = true, trackMarks = true, legGlow = true, rubble = true;
        [Tooltip("Armour ricochet sound on metal hits")] public bool impactSounds = true;
        [Tooltip("A/B test without code edits: every FxManager.Explosion also gets the new layers (additive squares, rings, ground " +
                 "light). FxManager's own meshes, light and RtsCamera shake still play, so this is for comparison only.")]
        public bool mirrorFxManager;
        [Range(0f, 1f)] public float shakeScale = 1f;

        public OpenZFxShake Shake { get; private set; }
        public float WaterY { get; private set; } = float.NegativeInfinity;
        public int Emitted { get; private set; }
        public int ActiveLights { get; private set; }
        // plain literal: a MonoBehaviour field initializer runs in the constructor, where Unity forbids ColorUtility (and it
        // would run the OpenZFxCatalog static constructor there); Start() sets the level's tint
        Color waterTint = new Color(0.510f, 0.714f, 0.725f);

        // ------------------------------------------------------------------------------------------------ bootstrap
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded; }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!AutoCreate || I) return;
            if (!ZGame.I && !FindFirstObjectByType<ZGame>()) return;
            new GameObject("OpenZFxPlayer").AddComponent<OpenZFxPlayer>();
        }

        // ------------------------------------------------------------------------------------------------ state
        sealed class Channel
        {
            public FxChannelDef def; public ParticleSystem ps; public ParticleSystemRenderer r; public Material mat;
        }
        readonly Dictionary<string, Channel> channels = new Dictionary<string, Channel>();
        readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        Shader fxShader, litShader;
        Camera cam;
        int serial;
        uint rngState = 0x9E3779B9u;

        struct Pending { public float at; public Channel ch; public ParticleSystem.EmitParams ep; }
        readonly List<Pending> pending = new List<Pending>(512);
        struct PendingImpact { public float at; public Vector3 pos; public FxSurface surface; public bool bolt, anchored; public HitSurface.Anchor anc; }   // IM1: anchored on the body
        readonly List<PendingImpact> pendingImpacts = new List<PendingImpact>(256);

        // ------------------------------------------------------------------------------------------------ lifecycle
        void Awake()
        {
            if (I && I != this) { Destroy(gameObject); return; }
            I = this;
            cam = Camera.main;
            haloBlock = new MaterialPropertyBlock();                                             // not in a field initializer (constructor)
            fxShader = LoadFxShader();
            litShader = Shader.Find("OpenZ/FxLit");
            if (!fxShader) { Debug.LogError("OpenZFxPlayer: shader OpenZ/FxAdditive missing (see README_IMPORT.md)"); enabled = false; return; }
            BakeLibrary();
            SmokeAtlasChannels = 0; SmokeAtlasOn = true;
            foreach (var d in OpenZFxCatalog.Channels) { var c = CreateChannel(d); if (c != null) channels[d.name] = c; }
            ground = new GroundCache();
            BuildDecalPools();
            BuildLights();
            BuildProjectileMaterials();
            Shake = new OpenZFxShake { scale = shakeScale };
        }

        void Start()
        {
            MeasureWater();
            string level = ZGame.I ? ZGame.I.level : null;
            waterTint = OpenZFxCatalog.RippleColorFor(level);
            SoftWater = level == "L16";                                                                   // the swamp's own water look (soft swells, round drops, foam)
            var wEm = OpenZFxCatalog.RippleEmissionFor(level).linear * 0.6f;
            foreach (var n in new[] { "m12_ripple", "m12_splash", "m12_ripple_calm" }) { var ch = Ch(n); if (ch != null) SetLinear(ch.mat, "_EmissionColor", wEm); }
            calmGlow = wEm;
            FxManager.Exploded -= OnFxManagerExploded;
            FxManager.Exploded += OnFxManagerExploded;
        }

        void OnDestroy()
        {
            FxManager.Exploded -= OnFxManagerExploded;
            Shake?.Dispose();
            if (I == this) I = null;
        }

        static Shader LoadFxShader()
        {   // a material in Resources keeps the shader in player builds (Shader.Find alone only finds included shaders)
            var m = Resources.Load<Material>("OpenZ/FX/M_FxAdditive");
            return m && m.shader ? m.shader : Shader.Find("OpenZ/FxAdditive");
        }

        void MeasureWater()
        {   // same rule as ZoneMarkers.cs:24-27: the flat river / harbour surface = lowest WAT_* bounds + 2 cm
            float y = float.NegativeInfinity;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.name.StartsWith("WAT_") && r.gameObject.activeInHierarchy) y = Mathf.Max(y, r.bounds.min.y + 0.02f);
            WaterY = y;
        }

        /// <summary>Water surface height at a point: the tile's own height on a level with a water table, else the flat height.</summary>
        public float WaterYAt(Vector3 p) => OzWaterLevel.At(ZGame.I ? ZGame.I.level : "", p.x, p.z, WaterY);

        // ------------------------------------------------------------------------------------------------ glb meshes
        void BakeLibrary()
        {   // same bake as FxManager.cs:66-76 (node transform applied into the mesh, library-root space)
            var lib = Resources.Load<GameObject>("OpenZ/FX/combat_fx");
            if (lib) BakeInto(lib, false);
            else Debug.LogWarning("OpenZFxPlayer: Resources/OpenZ/FX/combat_fx missing: rockets, shells and rubble are not drawn");
            var gren = Resources.Load<GameObject>("OpenZ/FX/grenade");
            if (gren) BakeInto(gren, true);
            quad = BuildQuad();
        }

        /// <summary>grenade.glb is 7 meshes (Grenade_body, 3 ribs, fuse, spoon, lever; measured from the GLB), each with its own
        /// material: every part and sub-mesh is kept and drawn with the same spin matrix.</summary>
        struct GrenadePart { public Mesh mesh; public int sub; public Material mat; }
        readonly List<GrenadePart> grenadeParts = new List<GrenadePart>();

        void BakeInto(GameObject root, bool grenade)
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh) continue;
                if (!grenade && meshes.ContainsKey(mf.name)) continue;
                var m = Instantiate(mf.sharedMesh);
                var M = root.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var v = m.vertices; var n = m.normals;
                for (int i = 0; i < v.Length; i++) { v[i] = M.MultiplyPoint3x4(v[i]); if (i < n.Length) n[i] = M.MultiplyVector(n[i]).normalized; }
                m.vertices = v; if (n.Length == v.Length) m.normals = n; else m.RecalculateNormals(); m.RecalculateBounds();
                if (!grenade) { meshes[mf.name] = m; continue; }
                var mr = mf.GetComponent<MeshRenderer>();
                var mats = mr ? mr.sharedMaterials : null;
                for (int s = 0; s < m.subMeshCount; s++)
                {
                    var mat = mats != null && mats.Length > 0 ? mats[Mathf.Min(s, mats.Length - 1)] : null;
                    if (mat) grenadeParts.Add(new GrenadePart { mesh = m, sub = s, mat = mat });
                }
            }
        }

        /// <summary>Long axis of a projectile mesh, pointing at its pointy end (nose of a rocket / shell, tip of the flame cone),
        /// measured from the vertices so no axis convention is assumed. min / max are positions along that axis.</summary>
        static void NoseAxis(Mesh m, out Vector3 nose, out float min, out float max)
        {
            var b = m.bounds; var s = b.size;
            Vector3 axis = s.x >= s.y && s.x >= s.z ? Vector3.right : s.z >= s.y ? Vector3.forward : Vector3.up;
            var v = m.vertices; float lo = float.MaxValue, hi = float.MinValue;
            foreach (var p in v) { float t = Vector3.Dot(p, axis); lo = Mathf.Min(lo, t); hi = Mathf.Max(hi, t); }
            float span = Mathf.Max(1e-4f, hi - lo), rLo = 0, rHi = 0; int nLo = 0, nHi = 0;
            foreach (var p in v)
            {
                float t = Vector3.Dot(p, axis); var radial = p - b.center; radial -= axis * Vector3.Dot(radial, axis);
                if (t < lo + span * 0.15f) { rLo += radial.magnitude; nLo++; } else if (t > hi - span * 0.15f) { rHi += radial.magnitude; nHi++; }
            }
            rLo = nLo > 0 ? rLo / nLo : 0; rHi = nHi > 0 ? rHi / nHi : 0;
            if (rLo < rHi) { nose = -axis; min = -hi; max = -lo; } else { nose = axis; min = lo; max = hi; }
        }

        Mesh quad;
        static Mesh BuildQuad()
        {
            var m = new Mesh { name = "FX_HaloQuad" };
            m.SetVertices(new List<Vector3> { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            m.SetNormals(new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            m.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
            m.SetTriangles(new List<int> { 0, 2, 1, 0, 3, 2 }, 0); m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------------------------------------ channels
        Material FxMaterial(string name, FxShape shape, int queue, float opaqueness)
        {
            var m = new Material(fxShader) { name = "FX_" + name, renderQueue = queue };
            m.SetFloat("_Shape", (float)shape); m.SetFloat("_Opaqueness", opaqueness);
            return m;
        }

        static void SetLinear(Material m, string prop, Color linear) => m.SetVector(prop, new Vector4(linear.r, linear.g, linear.b, linear.a));
        static void SetLinear(MaterialPropertyBlock b, string prop, Color linear) => b.SetVector(prop, new Vector4(linear.r, linear.g, linear.b, linear.a));

        Channel CreateChannel(FxChannelDef d)
        {
            Material mat;
            Mesh particleMesh = null;
            if (d.mesh)
            {
                if (!meshes.TryGetValue(d.meshName, out particleMesh) || !litShader) return null;
                mat = new Material(litShader) { name = "FX_" + d.name, renderQueue = d.queue };
                mat.SetColor("_BaseColor", d.meshColor); mat.SetColor("_EmissionColor", Color.black);
                mat.SetFloat("_SrcBlend", (float)BlendMode.One); mat.SetFloat("_DstBlend", (float)BlendMode.Zero); mat.SetFloat("_ZWrite", 1f); mat.SetFloat("_Cull", 2f);
            }
            else
            {
                mat = FxMaterial(d.name, d.shape, d.queue, d.opaqueness);
                SetLinear(mat, "_Color", d.tint.linear);
                mat.SetFloat("_Intensity", d.intensity);
                SetLinear(mat, "_CoreColor", d.coreColor.linear);
                mat.SetFloat("_CoreMix", d.coreMix);
                mat.SetFloat("_Lit", d.lit); mat.SetFloat("_Wrap", d.wrap);
                SetLinear(mat, "_EmissionColor", d.emission.linear * d.emissionGain);
                mat.SetFloat("_RimGain", d.rimGain > 0 ? d.rimGain : 0.18f); mat.SetFloat("_RimPower", d.rimPower > 0 ? d.rimPower : 1.2f);
                mat.SetFloat("_Squareness", d.squareness); mat.SetFloat("_EdgeSoft", d.edgeSoft > 0 ? d.edgeSoft : 0.25f);
                mat.SetFloat("_SoftFade", d.softFade);
                mat.SetFloat("_BandInner", d.bandInner > 0 ? d.bandInner : 0.88f); mat.SetFloat("_BandSides", d.bandSides);
                // motion smoothing: only view-facing smoke / fire quads are drawn out along their velocity (never ground quads or streaks)
                mat.SetFloat("_Smear", d.flat || d.stretch ? 0f : d.shape == FxShape.Puff ? OzFxSmooth.SmearSmoke : d.shape == FxShape.Glow || d.shape == FxShape.Fireball ? OzFxSmooth.SmearFire : 0f);
                if (d.shape == FxShape.Puff && !d.flat && !d.stretch && System.Array.IndexOf(OzFxSmooth.PathBlurChannels, d.name) >= 0)
                {   // the big puffs of blasts: blurred along their own path by the shader instead of stretched
                    mat.SetFloat("_Smear", OzFxSmooth.SmearBlastPuff); mat.SetFloat("_SmearPath", 1f);
                }
                mat.SetFloat("_UseVertexColor", 1f);
                // fx_look: heat-ramp fireball + billowy lit smoke (OpenZFxAdditive shapes 3 / 2), all values linear
                mat.SetFloat("_FireRamp", d.fireRamp ? 1f : 0f);
                mat.SetVector("_FireOpacity", d.heatOpacity ? new Vector4(d.opacityRim, d.opacityCore, 0f, 0f) : new Vector4(-1f, 0f, 0f, 0f));   // fx_v2
                if (d.fireRamp)
                {
                    SetLinear(mat, "_FireCore", d.fireCore); SetLinear(mat, "_FireHot", d.fireHot); SetLinear(mat, "_FireMid", d.fireMid);
                    SetLinear(mat, "_FireRim", d.fireRim); SetLinear(mat, "_FireSoot", d.fireSoot);
                    mat.SetFloat("_Cooling", d.cooling); mat.SetFloat("_HeatBias", d.heatBias);
                }
                mat.SetFloat("_NoiseAmt", d.noiseAmt); mat.SetFloat("_NoiseScale", d.noiseScale > 0f ? d.noiseScale : 2.2f);
                mat.SetFloat("_Boil", d.boil); mat.SetFloat("_Spin", d.spin); mat.SetFloat("_Bump", d.bump); mat.SetFloat("_SelfShadow", d.selfShadow);
                SetLinear(mat, "_AgeColor", d.ageColor.a > 0f ? d.ageColor : Color.white);
                SetLinear(mat, "_HeatColor", d.heatColor); mat.SetFloat("_HeatTime", d.heatTime > 0f ? d.heatTime : 0.25f);
                if (d.shape == FxShape.Puff) ApplySmokeAtlas(mat, d);
            }

            var go = new GameObject("FxCh_" + d.name);
            go.SetActive(false);                                                   // configure before the system wakes up
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            var r = go.GetComponent<ParticleSystemRenderer>();

            var main = ps.main;
            main.playOnAwake = false; main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.maxParticles = d.capacity;
            main.startSpeed = 0f; main.startLifetime = 1f; main.startSize = 1f;
            main.startRotation3D = d.flat || d.mesh;
            float g = Mathf.Max(0.01f, Mathf.Abs(Physics.gravity.y));
            main.gravityModifier = d.gravity / g;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;

            if (d.sizeCurve != FxSizeCurve.Constant)
            {
                var sol = ps.sizeOverLifetime; sol.enabled = true;
                var curve = SizeCurve(d.sizeCurve, d.sizeEnd, out float mul);
                sol.size = new ParticleSystem.MinMaxCurve(mul, curve);
            }
            var col = ps.colorOverLifetime; col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(AlphaGradient(d.alphaCurve, d.colorFade));

            if (d.drag > 0f && d.drag < 1f)
            {   // LEG: v.xz *= drag^(60 dt)  ->  linear drag coefficient k = -60 ln(drag)
                var lv = ps.limitVelocityOverLifetime; lv.enabled = true;
                lv.limit = 1000f; lv.dampen = 0f;
                lv.drag = -60f * Mathf.Log(d.drag);
                lv.multiplyDragByParticleSize = false; lv.multiplyDragByParticleVelocity = false;
            }
            if (d.collide)
            {
                var c = ps.collision; c.enabled = true;
                c.type = ParticleSystemCollisionType.World; c.mode = ParticleSystemCollisionMode.Collision3D;
                c.quality = ParticleSystemCollisionQuality.Medium; c.enableDynamicColliders = false;
                c.collidesWith = ZGame.WalkLayer >= 0 ? (1 << ZGame.WalkLayer) : Physics.DefaultRaycastLayers;
                c.bounce = d.bounce; c.dampen = d.dampen; c.lifetimeLoss = 0f; c.radiusScale = 0.5f;
            }
            if (d.mesh)
            {   // M12 rubble spins while flying and stops at contact: rotation by speed
                var rbs = ps.rotationBySpeed; rbs.enabled = true; rbs.separateAxes = true; rbs.range = new Vector2(0f, 2f);
                var up = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));
                var dn = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, -1f));
                rbs.x = new ParticleSystem.MinMaxCurve(9f, dn, up); rbs.y = new ParticleSystem.MinMaxCurve(7f, dn, up); rbs.z = new ParticleSystem.MinMaxCurve(4f, dn, up);
            }

            r.renderMode = d.mesh ? ParticleSystemRenderMode.Mesh : d.stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            if (d.stretch) { r.lengthScale = d.lengthScale; r.velocityScale = 0f; r.cameraVelocityScale = 0f; }
            if (d.mesh) { r.mesh = particleMesh; r.enableGPUInstancing = false; }
            r.alignment = d.flat || d.mesh ? ParticleSystemRenderSpace.World : ParticleSystemRenderSpace.View;
            r.sortMode = d.sortByDistance ? ParticleSystemSortMode.Distance : ParticleSystemSortMode.None;
            r.shadowCastingMode = ShadowCastingMode.Off;                           // M12 pools never cast; FxLit has no shadow pass
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.minParticleSize = 0f; r.maxParticleSize = 4f;
            r.applyActiveColorSpace = true;                                        // sRGB particle colours -> linear in the shader
            r.sharedMaterial = mat;
            if (!d.mesh) r.SetActiveVertexStreams(FxStreams);                      // fx_look: uv.z = age 0-1, uv.w = stable random

            go.SetActive(true);
            ps.Play();
            return new Channel { def = d, ps = ps, r = r, mat = mat };
        }

        /// <summary>fx_look: POSITION, NORMAL, COLOR, TEXCOORD0.xy = UV, TEXCOORD0.z = AgePercent, TEXCOORD0.w = StableRandom.x.</summary>
        static readonly List<ParticleSystemVertexStream> FxStreams = new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV, ParticleSystemVertexStream.AgePercent, ParticleSystemVertexStream.StableRandomX,
            ParticleSystemVertexStream.Velocity, ParticleSystemVertexStream.SizeX, ParticleSystemVertexStream.Center,   // motion smoothing: TEXCOORD1 = velocity xyz + size, TEXCOORD2.xyz = centre
        };

        static AnimationCurve SizeCurve(FxSizeCurve k, float end, out float mul)
        {
            Func<float, float> f;
            switch (k)
            {
                case FxSizeCurve.SinShrink: f = t => Mathf.Sin(Mathf.PI * Mathf.Min(0.999f, t)) * (1f - 0.4f * t); break;
                case FxSizeCurve.LegCore: f = t => Mathf.Sin(Mathf.PI * t) * (1f - 0.35f * t); break;
                case FxSizeCurve.SmokeGrow: f = t => 0.5f + 1.8f * t; break;
                case FxSizeCurve.RingGrow: f = t => 0.2f + 6f * t; break;
                case FxSizeCurve.Lerp: f = t => Mathf.Lerp(1f, end, t); break;
                case FxSizeCurve.RubbleShrink: f = t => t > 0.8f ? Mathf.Max(0.001f, (1f - t) / 0.2f) : 1f; break;
                case FxSizeCurve.SinPulse: f = t => Mathf.Sin(Mathf.PI * t); break;
                case FxSizeCurve.BillowGrow: f = t => 0.45f + 1.85f * (1f - Mathf.Pow(1f - t, 2.2f)); break;   // fx_look
                default: f = t => 1f; break;
            }
            return Sample(f, 16, out mul);
        }

        /// <summary>Samples f on [0,1] into a normalised AnimationCurve (max 1) with finite-difference tangents; mul = max.</summary>
        static AnimationCurve Sample(Func<float, float> f, int n, out float mul)
        {
            var vals = new float[n + 1]; mul = 1e-4f;
            for (int i = 0; i <= n; i++) { vals[i] = f(i / (float)n); mul = Mathf.Max(mul, vals[i]); }
            var keys = new Keyframe[n + 1];
            for (int i = 0; i <= n; i++)
            {
                int a = Mathf.Max(0, i - 1), b = Mathf.Min(n, i + 1);
                float slope = (vals[b] - vals[a]) / ((b - a) / (float)n) / mul;
                keys[i] = new Keyframe(i / (float)n, vals[i] / mul, slope, slope);
            }
            return new AnimationCurve(keys);
        }

        static Gradient AlphaGradient(FxAlphaCurve a, float colorFade)
        {
            Func<float, float> f;
            float[] ts;
            switch (a)
            {
                case FxAlphaCurve.SmokeFade: f = t => 1f - Mathf.SmoothStep(0f, 1f, (t - 0.22f) / 0.78f); ts = new[] { 0f, 0.22f, 0.35f, 0.5f, 0.65f, 0.8f, 0.9f, 1f }; break;
                case FxAlphaCurve.PopFade: f = t => 1f - Mathf.SmoothStep(0f, 1f, (t - 0.15f) / 0.85f); ts = new[] { 0f, 0.15f, 0.3f, 0.45f, 0.6f, 0.75f, 0.9f, 1f }; break;
                case FxAlphaCurve.LegSin: f = t => Mathf.Sin(Mathf.PI * Mathf.Min(1f, t * 1.08f)); ts = new[] { 0f, 0.08f, 0.2f, 0.33f, 0.463f, 0.62f, 0.8f, 0.926f }; break;
                case FxAlphaCurve.Linear: f = t => 1f - t; ts = new[] { 0f, 1f }; break;
                case FxAlphaCurve.BillowFade: f = t => Mathf.SmoothStep(0f, 1f, t / 0.06f) * (1f - Mathf.SmoothStep(0f, 1f, (t - 0.3f) / 0.7f));   // fx_look
                    ts = new[] { 0f, 0.06f, 0.3f, 0.45f, 0.6f, 0.75f, 0.88f, 1f }; break;
                default: f = t => 1f; ts = new[] { 0f, 1f }; break;
            }
            var ak = new GradientAlphaKey[ts.Length];
            for (int i = 0; i < ts.Length; i++) ak[i] = new GradientAlphaKey(Mathf.Clamp01(f(ts[i])), ts[i]);
            var ck = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white * (1f - colorFade), 1f) };
            var g = new Gradient(); g.SetKeys(ck, ak);
            return g;
        }

        Channel Ch(string name) => name != null && channels.TryGetValue(name, out var c) ? c : null;

        /// <summary>
        /// draws from the 4096 smoke flipbook + the 1024 detail-erosion texture (OpenZFxAdditive shape 2, _AtlasOn). Resources keep
        /// both in player builds. SmokeAtlasOn = false falls back to the old procedural puff (proof before / after only).</summary>
        public static bool SmokeAtlasOn = true;
        public static int SmokeAtlasChannels { get; private set; }
        static Texture2D smokeAtlas, smokeDetail;
        void ApplySmokeAtlas(Material mat, FxChannelDef d)
        {
            if (!smokeAtlas) smokeAtlas = Resources.Load<Texture2D>("OpenZ/FX/Smoke/OpenZ_SmokeAtlas");
            if (!smokeDetail) smokeDetail = Resources.Load<Texture2D>("OpenZ/FX/Smoke/OpenZ_SmokeDetail");
            if (!smokeAtlas || !smokeDetail) { Debug.LogWarning("OpenZFxPlayer: Resources/OpenZ/FX/Smoke textures missing: procedural puffs"); return; }
            mat.SetTexture("_SmokeAtlas", smokeAtlas); mat.SetTexture("_SmokeDetail", smokeDetail);
            mat.SetFloat("_DetailAmt", Mathf.Clamp(0.24f + d.noiseAmt * 0.5f, 0.26f, 0.56f));     // coarse / boiling looks erode more
            mat.SetFloat("_DetailScale", Mathf.Max(1.6f, (d.noiseScale > 0f ? d.noiseScale : 2f) * 1.5f));
            mat.SetFloat("_Dissolve", 0.35f); mat.SetFloat("_FlipRate", 1f);
            mat.SetFloat("_AtlasOn", SmokeAtlasOn ? 1f : 0f);
            SmokeAtlasChannels++;
        }

        /// <summary>Switch every puff channel between the flipbook (true) and the old procedural puff (false); returns the count.</summary>
        public int SetSmokeAtlas(bool on)
        {
            SmokeAtlasOn = on; int n = 0;
            foreach (var c in channels.Values) if (!c.def.mesh && c.def.shape == FxShape.Puff && c.mat.HasProperty("_AtlasOn")) { c.mat.SetFloat("_AtlasOn", on ? 1f : 0f); n++; }
            return n;
        }

        /// <summary>Proof: the biggest live puff on screen (diameter in pixels) and its channel.</summary>
        public float BiggestPuffPx(Camera c, out string channel, out int live)
        {
            channel = "-"; live = 0; float best = 0f;
            if (!c) return 0f;
            float k = Screen.height / (2f * Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad));
            foreach (var ch in channels.Values)
            {
                if (ch.def.mesh || ch.def.shape != FxShape.Puff) continue;
                int n = ch.ps.particleCount; if (n == 0) continue;
                if (puffBuf == null || puffBuf.Length < ch.def.capacity) puffBuf = new ParticleSystem.Particle[Mathf.Max(ch.def.capacity, 1500)];
                n = ch.ps.GetParticles(puffBuf); live += n;
                for (int i = 0; i < n; i++)
                {
                    var p = puffBuf[i]; float dist = Vector3.Dot(p.position - c.transform.position, c.transform.forward);
                    if (dist < 0.5f) continue;
                    var vp = c.WorldToViewportPoint(p.position); if (vp.x < -0.1f || vp.x > 1.1f || vp.y < -0.1f || vp.y > 1.1f) continue;
                    float px = p.GetCurrentSize(ch.ps) / dist * k;
                    if (px > best) { best = px; channel = ch.def.name; }
                }
            }
            return best;
        }
        ParticleSystem.Particle[] puffBuf;

        /// <summary>One particle. size = particle width (billboards: diameter; stretched: width, length = width x lengthScale).</summary>
        void EmitP(Channel ch, Vector3 pos, Vector3 vel, float life, float size, Color color, float alpha, float delay = 0f)
        {
            if (ch == null) return;
            var ep = new ParticleSystem.EmitParams();
            ep.applyShapeToPosition = false;
            ep.position = pos; ep.velocity = vel;
            ep.startLifetime = Mathf.Max(0.01f, life);
            ep.startSize = Mathf.Max(ch.def.minWidth, size);
            color.a = Mathf.Clamp01(alpha); ep.startColor = color;
            if (ch.def.flat) ep.rotation3D = new Vector3(90f, 0f, 0f);
            else if (ch.def.mesh) ep.rotation3D = new Vector3(Rand() * 360f, Rand() * 360f, Rand() * 360f);
            if (delay > 0f) { pending.Add(new Pending { at = Time.time + delay, ch = ch, ep = ep }); return; }
            ch.ps.Emit(ep, 1);
            Emitted++;
        }

        // xorshift32: deterministic per effect when seeded (LEG randomFactory(effect.id))
        float Rand() { rngState ^= rngState << 13; rngState ^= rngState >> 17; rngState ^= rngState << 5; return (rngState & 0xFFFFFF) / 16777216f; }
        void Seed(int id) { rngState = (uint)(id * 747796405 + 2891336453u) | 1u; Rand(); }

        void EmitGolden(in FxGoldenLayer L, Vector3 at, float s, float sizeMul = 1f, Color? tint = null)
        {
            if (L.count <= 0) return;
            var main = Ch(L.channel); var alt = Ch(L.altChannel);
            // M12's golden-angle recipe gave every blast the same puff layout; smoke and dust layers now turn the whole
            // pattern by a random angle per blast and jitter each puff's direction, speed, rise, size and life (the billow shape
            // itself already varies per puff through the stable random in the shader)
            bool vary = L.channel != null && (L.channel.Contains("smoke") || L.channel.Contains("dust"));
            float a0 = vary ? Rand() * 6.2831853f : 0f;
            // On top of NB8 each blast draws its own character: a rise, life and size
            // bias, a density, a tone from sooty grey to dusty tan, a wind lean shared by its puffs, and the column's puffs come from
            // three billow looks (blast_smoke / _b coarse, boiling, counter-spinning / _c smooth, fast-spinning: different noise,
            // boil, spin, bump, buoyancy in the shader) in a per-blast mix; a few puffs split off a smaller satellite puff
            float riseK = 1f, lifeK = 1f, sizeK = 1f, dens = 1f, mixB = 0f, mixC = 0f; var tone = Color.white; var lean = Vector3.zero;
            Channel chB = null, chC = null;
            if (vary)
            {
                riseK = 0.8f + 0.45f * Rand(); lifeK = 0.85f + 0.35f * Rand(); sizeK = 0.85f + 0.32f * Rand(); dens = 0.78f + 0.32f * Rand();
                tone = Color.Lerp(new Color(0.8f, 0.79f, 0.78f, 1f), new Color(1.1f, 1.02f, 0.88f, 1f), Rand());
                float la = Rand() * 6.2831853f; lean = new Vector3(Mathf.Cos(la), 0f, Mathf.Sin(la)) * (0.12f + 0.6f * Rand());
                if (L.channel == "blast_smoke") { chB = Ch("blast_smoke_b"); chC = Ch("blast_smoke_c"); mixB = 0.15f + 0.5f * Rand(); mixC = 0.1f + 0.45f * Rand(); }
            }
            for (int j = 0; j < L.count; j++)
            {
                float a = a0 + j * L.angleStep + (vary ? (Rand() - 0.5f) * 0.9f : 0f), ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                bool isAlt = L.altEvery > 0 && ((j % L.altEvery == 0) == L.altWhenZero);
                var ch = isAlt && alt != null ? alt : main;
                if (chB != null || chC != null) { float pick = Rand(); if (pick < mixB && chB != null) ch = chB; else if (pick < mixB + mixC && chC != null) ch = chC; }
                float pr = L.posRadial.At(j);
                var pos = at + new Vector3(ca * pr, L.posUp.At(j), sa * pr);
                float rad = L.radial.At(j) * (L.radialTimesStrength > 0f ? s : 1f);
                float up = L.up.At(j), life = L.life.At(j);
                float scale = L.scale.At(j) * (L.scaleTimesStrength ? s : 1f);
                if (vary) { rad *= 0.7f + 0.6f * Rand(); up *= (0.75f + 0.5f * Rand()) * riseK; life *= (0.8f + 0.45f * Rand()) * lifeK; scale *= (0.75f + 0.55f * Rand()) * sizeK; }
                var vel = new Vector3(ca * rad, up, sa * rad);
                var color = tint ?? (isAlt && L.altColor.a > 0f ? L.altColor : L.color);
                float alpha = L.alpha;
                if (vary)
                {
                    vel += lean * (0.6f + 0.8f * Rand());
                    float lum = 0.88f + 0.24f * Rand();
                    color = new Color(color.r * tone.r * lum, color.g * tone.g * lum, color.b * tone.b * lum, color.a);
                    alpha = Mathf.Clamp01(alpha * dens * (0.85f + 0.3f * Rand()));
                    if (Rand() < 0.16f)
                    {   // a satellite puff split off this one
                        var off = new Vector3(Rand() - 0.5f, Rand() * 0.6f, Rand() - 0.5f) * (scale * L.sizePerScale * sizeMul * 0.9f);
                        EmitP(ch, pos + off, vel * (0.7f + 0.4f * Rand()) + off * 0.3f, life * (0.6f + 0.3f * Rand()), scale * L.sizePerScale * sizeMul * (0.45f + 0.25f * Rand()), color, alpha * 0.85f);
                    }
                }
                EmitP(ch, pos, vel, life, scale * L.sizePerScale * sizeMul, color, alpha);
            }
        }

        // ------------------------------------------------------------------------------------------------ ground / water
        GroundCache ground;
        float Ground(float x, float z) => ground != null ? ground.Y(x, z) : (ZGame.I ? ZGame.I.GroundY(x, z) : 0f);
        /// <summary>The walkable surface under p (terrain / river bed or a bridge deck; the cached 0.5 m grid).</summary>
        public float GroundAt(Vector3 p) => Ground(p.x, p.z);

        /// <summary>Zod tile class at a world point (ZGame.WorldToPx, ZMapData.ClassAt; tile = 16 px).</summary>
        public static ZClass ClassAt(Vector3 p)
        {
            var g = ZGame.I; if (g == null || g.World == null || g.World.Map == null) return ZClass.Ground;
            var px = ZGame.WorldToPx(p);
            return g.World.Map.ClassAt(px.x >> 4, px.y >> 4);
        }
        public static bool IsWaterTile(Vector3 p) { var c = ClassAt(p); return c == ZClass.Water || c == ZClass.DeepWater; }
        /// <summary>Open water at p: a water tile whose walkable surface (terrain / river bed, or a bridge deck) is not above the
        /// water. Bridge decks over the river are ground, so rings, scorches and tread marks follow them.</summary>
        public bool WetAt(Vector3 p, float groundY) => IsWaterTile(p) && (float.IsNegativeInfinity(WaterY) || groundY < WaterYAt(p) + DeckClearance);
        public bool WetAt(Vector3 p) => WetAt(p, Ground(p.x, p.z));
        const float DeckClearance = 0.1f;
        /// <summary>Water surface under p, or the ground when there is no water there.</summary>
        public float SurfaceY(Vector3 p) => IsWaterTile(p) && !float.IsNegativeInfinity(WaterY) ? Mathf.Max(WaterYAt(p), Ground(p.x, p.z)) : Ground(p.x, p.z);

        public FxSurface SurfaceAt(Vector3 p)
        {
            switch (ClassAt(p))
            {
                case ZClass.Water: case ZClass.DeepWater: return WetAt(p) ? FxSurface.Water : FxSurface.Road;   // deck: riveted plates
                case ZClass.Road: return FxSurface.Road;
                case ZClass.Impassable: return FxSurface.Rock;
                default: return FxSurface.Ground;
            }
        }

        /// <summary>Ground heights cached on a .5 m grid (lazy raycasts, bilinear), so ring / scorch meshes conform to the
        /// terrain and bridge decks without hundreds of raycasts per blast. Invalidate() after a bridge or building falls.</summary>
        sealed class GroundCache
        {
            const float Cell = 0.5f;
            readonly float x0, z0; readonly int nx, nz; readonly float[] h;
            public GroundCache()
            {
                var g = ZGame.I;
                float w = 160f, d = 200f;
                if (g != null && g.World != null && g.World.Map != null) { w = g.World.Map.WidthPix / OpenZFxCatalog.PxPerMetre; d = g.World.Map.HeightPix / OpenZFxCatalog.PxPerMetre; }
                x0 = -2f; z0 = -d - 2f;
                nx = Mathf.CeilToInt((w + 4f) / Cell) + 1; nz = Mathf.CeilToInt((d + 4f) / Cell) + 1;
                h = new float[nx * nz];
                for (int i = 0; i < h.Length; i++) h[i] = float.NaN;
            }
            float At(int ix, int iz)
            {
                ix = Mathf.Clamp(ix, 0, nx - 1); iz = Mathf.Clamp(iz, 0, nz - 1);
                int k = iz * nx + ix; float v = h[k];
                if (float.IsNaN(v)) { v = (ZGame.I ? ZGame.I.GroundY(x0 + ix * Cell, z0 + iz * Cell) : 0f) + CraterField.FinalMissing(x0 + ix * Cell, z0 + iz * Cell); h[k] = v; }
                return v;
            }
            public float Y(float x, float z)
            {
                float fx = (x - x0) / Cell, fz = (z - z0) / Cell;
                int ix = Mathf.FloorToInt(fx), iz = Mathf.FloorToInt(fz);
                float tx = fx - ix, tz = fz - iz;
                float a = At(ix, iz), b = At(ix + 1, iz), c = At(ix, iz + 1), e = At(ix + 1, iz + 1);
                // a bank / deck edge inside the cell: take the highest sample so a decal never cuts under a step
                float lo = Mathf.Min(Mathf.Min(a, b), Mathf.Min(c, e)), hi = Mathf.Max(Mathf.Max(a, b), Mathf.Max(c, e));
                if (hi - lo > 0.6f) return ZGame.I ? ZGame.I.GroundY(x, z) + CraterField.FinalMissing(x, z) : hi;
                return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, e, tx), tz);
            }
            public void Invalidate(Vector3 c, float r)
            {
                int ix0 = Mathf.Max(0, Mathf.FloorToInt((c.x - r - x0) / Cell)), ix1 = Mathf.Min(nx - 1, Mathf.CeilToInt((c.x + r - x0) / Cell));
                int iz0 = Mathf.Max(0, Mathf.FloorToInt((c.z - r - z0) / Cell)), iz1 = Mathf.Min(nz - 1, Mathf.CeilToInt((c.z + r - z0) / Cell));
                for (int iz = iz0; iz <= iz1; iz++) for (int ix = ix0; ix <= ix1; ix++) h[iz * nx + ix] = float.NaN;
            }
        }
        /// <summary>Call after a bridge / building / rock wall changes the walkable surface around c.</summary>
        public void InvalidateGround(Vector3 c, float radius) => ground?.Invalidate(c, radius);

        // ------------------------------------------------------------------------------------------------ explosions
        /// <summary>
        /// scorch, camera impact) + the LEG layers M12 lacks (additive glow clouds, glow sparks, fire clouds, outer / echo rings,
        /// point light). radiusM = the Zod splash radius in metres (r_px / 8); 0 = from the kind's M12 strength.</summary>
        public void Blast(Vector3 at, float radiusM, FxBlastKind kind = FxBlastKind.Missile, int id = -1, float legacyStrength = 1f)
        {
            float s = OpenZFxCatalog.StrengthOf(kind, radiusM, legacyStrength);
            BlastInternal(at, s, radiusM > 0f ? radiusM : Mathf.Min(4f * s, OpenZFxCatalog.LegFixedKindMaxRadius), kind, id, false);
        }

        /// <summary>Old FxManager.Explosion(at, strength) callers (BuildingBreaker, RockPillar, NukeFx, RulesView, UnitView).</summary>
        public void BlastLegacy(Vector3 at, float openZStrength) => Blast(at, 0f, FxBlastKind.Legacy, -1, openZStrength);

        /// <summary>legacy_match: an explicit M12 strength s and LEG radius per source (LegacyMatch size table), keeping the kind's
        /// extras (vehicle hero rings, building collapse camera, grenade hot colour).</summary>
        public void BlastSized(Vector3 at, float s, float legRadiusM, FxBlastKind kind, int id = -1) =>
            BlastInternal(at, Mathf.Max(0.2f, s), legRadiusM > 0f ? legRadiusM : Mathf.Min(4f * s, OpenZFxCatalog.LegFixedKindMaxRadius), kind, id, false);

        /// <summary>Large collapse (building, hangar, bridge, rock wall): explosion + demolition dust + camera 3.2.</summary>
        public void Collapse(Vector3 at, float sizeX, float sizeZ, FxBlastKind kind = FxBlastKind.Building)
        {
            Blast(at, 0f, kind);
            float big = Mathf.Max(sizeX, sizeZ);
            Dust(at, Mathf.Max(1.5f, big * 0.32f));
            Scorch(at, big * 0.43f / OpenZFxCatalog.ScorchRadiusPerS, 2);
            Shake?.Impact(at, OpenZFxCatalog.ShakeCollapse);
            InvalidateGround(at, big);
        }

        void OnFxManagerExploded(Vector3 at, float strength)
        {
            if (mirrorFxManager) BlastInternal(at, Mathf.Max(0.2f, strength * OpenZFxCatalog.LegacyToM12), Mathf.Min(4f * strength * OpenZFxCatalog.LegacyToM12, OpenZFxCatalog.LegFixedKindMaxRadius), FxBlastKind.Legacy, -1, true);
        }

        void BlastInternal(Vector3 at, float s, float legRadius, FxBlastKind kind, int id, bool mirror)
        {
            if (!enabled) return;
            if (id < 0) id = ++serial;
            Seed(id);
            if (!mirror && !QuietBlast) Exploded?.Invoke(at, s);                               // mlrs_heavy_v1: cosmetic ripple blasts stay quiet
            float gy = Ground(at.x, at.z);
            bool onWater = IsWaterTile(at) && !float.IsNegativeInfinity(WaterY) && WaterYAt(at) >= gy - 0.05f;
            float surf = onWater ? WaterYAt(at) : gy;
            at.y = Mathf.Max(at.y, surf + OpenZFxCatalog.M12FloorClamp);                     // floor clamp
            var flat = new Vector3(at.x, surf, at.z);
            bool airBurst = OzShuttleBreakup.IsAirBurst(at, gy, kind);                          // K256e: up in the air (at the flying shuttle): nothing of it on the ground under it
            if (!mirror) OzShuttleBreakup.NoteBlastFx(at, flat, at.y - gy, airBurst, !onWater && !airBurst && (groundLights || shockRings));   // the proof's count
            if (!mirror && !QuietBlast && !onWater && at.y - gy <= OpenZFxCatalog.ScorchMaxHeight) CraterField.OnBlast(flat, s, kind);

            // --- M12 impact handler: camera, ground light, scorch
            if (!mirror) Shake.Impact(at, s);
            if (groundLights && !onWater && !airBurst) GroundLight(flat, s, false, true);                // fx_look: blast flash (longer, stronger)
            if (scorchMarks && !onWater && at.y - gy <= OpenZFxCatalog.ScorchMaxHeight)
                Scorch(flat, s, kind == FxBlastKind.Vehicle || kind == FxBlastKind.Building || kind == FxBlastKind.Nuke ? 2 : 1);
            if (kind == FxBlastKind.Building) Shake.Impact(at, OpenZFxCatalog.ShakeCollapse);

            // --- M12 recipe
            if (!mirror)
            {
                // fireball sat on the river): on water the spout leads (bigger splash, a second droplet crown, a slower outer ring)
                // and the fire / core / smoke below are cut to a flash in the spray
                if (onWater)
                {
                    Splash(flat, s * OpenZFxCatalog.BlastWaterSplash * 1.4f);
                    Droplets(flat + Vector3.up * 0.2f, s * 1.8f, 10);
                    Ripple(flat, s * 0.6f, OpenZFxCatalog.RippleLife * 1.2f);
                    Ripple(flat, s * 1.1f, OpenZFxCatalog.RippleLife * 2.2f);
                }
                else if (!airBurst) EmitGolden(OpenZFxCatalog.M12Dust, at, s);                // K256e: no ground dust in mid-air
                if (rubble && !onWater)
                {   // fx_look: rubble sized for the RTS camera + chunks thrown high on big blasts
                    float rs = OpenZFxCatalog.RubbleSizeOf(s);
                    EmitGolden(OpenZFxCatalog.M12Rubble, at, s, rs);
                    if (s >= OpenZFxCatalog.RubbleHighMinS) EmitGolden(OpenZFxCatalog.M12RubbleHigh, at, s, rs);
                }
                EmitGolden(OpenZFxCatalog.M12Fire, at, onWater ? s * 0.35f : s);
                EmitGolden(OpenZFxCatalog.M12BlastCore, at, onWater ? s * 0.55f : s);        // fx_look: small hot core (was M12Core: a white disc)
                if (!onWater) EmitGolden(OpenZFxCatalog.M12BlastSmoke, at, s, 1f, airBurst ? OzShuttleBreakup.AirBurstSmoke : (Color?)null);   // K256e: an air burst's smoke is dark               // fx_look: billowing lit smoke that darkens
            }
            EmitGolden(OpenZFxCatalog.M12Sparks, at, s);                                     // the additive "squares"
            if (!mirror) BlastEmbers(at, gy, s);                                             // fx_look: burning bits thrown high
            if (shockRings && !onWater && !airBurst) RingM12(flat, s);
            DebrisRequested?.Invoke(at, s, kind);

            bool tnt = kind == FxBlastKind.Building || kind == FxBlastKind.Barrel || kind == FxBlastKind.Nuke;
            bool vehicle = kind == FxBlastKind.Vehicle || kind == FxBlastKind.TurretPop;
            bool large = tnt || vehicle || legRadius >= OpenZFxCatalog.LegLargeRadius;
            bool hero = tnt || vehicle || (large && id % 4 == 0);
            float vr = legRadius * (tnt ? 1.2f : vehicle ? OpenZFxCatalog.LegVehicleMul : large ? OpenZFxCatalog.LegLargeMul : 1f) * (hero ? OpenZFxCatalog.LegHeroMul : 1f);
            var hot = OpenZFxCatalog.LegHotColor(kind);
            if (shockRings && !onWater && large && !airBurst)
            {
                float life = hero ? OpenZFxCatalog.LegOuterRingLifeHero : tnt ? OpenZFxCatalog.LegOuterRingLifeTnt : OpenZFxCatalog.LegOuterRingLife;
                float mul = hero ? OpenZFxCatalog.LegOuterRingMulHero : tnt ? OpenZFxCatalog.LegOuterRingMulTnt : OpenZFxCatalog.LegOuterRingMul;
                RingLeg(flat, vr * mul, life, 0f, tnt ? OpenZFxCatalog.LegOuterRingColorTnt : OpenZFxCatalog.LegOuterRingColor);
            }
            if (shockRings && !onWater && hero && !airBurst && (tnt || !OpenZFxCatalog.LegEchoRingTntOnly))   // fx_look: echo ring on TNT kinds only
                RingLeg(flat, vr * OpenZFxCatalog.LegEchoRingMul, OpenZFxCatalog.LegEchoRingEnd - OpenZFxCatalog.LegEchoRingDelay, OpenZFxCatalog.LegEchoRingDelay, OpenZFxCatalog.LegEchoRingColor);
            if (pointLights)
            {
                float I0 = ((large ? OpenZFxCatalog.LegLightBaseLarge : OpenZFxCatalog.LegLightBase) + vr * OpenZFxCatalog.LegLightPerVr) * (hero ? OpenZFxCatalog.LegLightHeroMul : 1f);
                float range = vr * (large ? OpenZFxCatalog.LegLightRangeLarge : OpenZFxCatalog.LegLightRange) * (hero ? OpenZFxCatalog.LegLightRangeHeroMul : 1f);
                float life = hero ? OpenZFxCatalog.LegLightLifeHero : large ? OpenZFxCatalog.LegLightLifeLarge : OpenZFxCatalog.LegLightLife;
                AddLight(at + Vector3.up * OpenZFxCatalog.BlastLightLift, hot, I0 * OpenZFxCatalog.BlastLightGain, range * OpenZFxCatalog.BlastLightRangeMul,
                    life * OpenZFxCatalog.BlastLightLifeMul, OpenZFxCatalog.LegLightDecayPow, 0f, true);   // fx_v2: lifted, stronger, longer
            }
            if (legGlow) LegGlow(at, airBurst ? at.y - 0.6f : gy, vr, large, hero, hot);   // K256e: an air burst's sparks and fire clouds round it, not on the ground
        }

        /// <summary>fx_look: glowing embers / burning bits thrown high (7 per unit of s, max 24) on leg_spark: arcs, bounces, fades.</summary>
        void BlastEmbers(Vector3 at, float gy, float s)
        {
            var ch = Ch("leg_spark"); if (ch == null) return;
            int n = Mathf.Clamp(Mathf.RoundToInt(OpenZFxCatalog.BlastEmberPerS * s), 0, OpenZFxCatalog.BlastEmberMax);
            float px = OpenZFxCatalog.LegPxToM, root = Mathf.Sqrt(Mathf.Max(0.2f, s));
            var pos = new Vector3(at.x, Mathf.Max(at.y, gy + 0.3f), at.z);
            for (int i = 0; i < n; i++)
            {
                float a = Rand() * Mathf.PI * 2f, sp = OpenZFxCatalog.BlastEmberSpeed + Rand() * OpenZFxCatalog.BlastEmberSpeedRand * root;
                var vel = new Vector3(Mathf.Cos(a) * sp, OpenZFxCatalog.BlastEmberUp + Rand() * OpenZFxCatalog.BlastEmberUpRand, Mathf.Sin(a) * sp);
                EmitP(ch, pos, vel, OpenZFxCatalog.BlastEmberLife + Rand() * OpenZFxCatalog.BlastEmberLifeRand, (OpenZFxCatalog.BlastEmberPx + Rand() * 6f) * px,
                    i % 2 == 0 ? OpenZFxCatalog.BlastEmberA : OpenZFxCatalog.BlastEmberB, 1f, Rand() * 0.05f);
            }
        }

        void LegGlow(Vector3 at, float gy, float vr, bool large, bool hero, Color hot)
        {
            float px = OpenZFxCatalog.LegPxToM;
            // glow clouds: 3 (hero 4), delay 18 i ms, (large 285 : 230) + 70 i ms, vr (18 - 2 i) -> vr (30 - 3 i) px
            var cloud = Ch("leg_cloud");
            int clouds = hero ? 4 : 3;
            for (int i = 0; i < clouds; i++)
            {
                var c = i == 1 ? hot : OpenZFxCatalog.LegCloudColors[Mathf.Min(i, 3)];
                EmitP(cloud, new Vector3(at.x, at.y + i * 0.08f, at.z), new Vector3(0f, 0.08f + i * 0.04f, 0f),
                    (large ? 0.285f : 0.23f) + i * 0.07f, vr * (18f - 2f * i) * px, c, 1f, i * 0.018f);
            }
            // sparks
            var spark = Ch("leg_spark");
            int sparks = Mathf.Min(hero ? 44 : large ? 36 : 24, (large ? 18 : 10) + Mathf.RoundToInt(vr * 3f) + (hero ? 8 : 0));
            for (int i = 0; i < sparks; i++)
            {
                float a = Rand() * Mathf.PI * 2f, sp = 2.4f + Rand() * vr * (large ? 2.25f : 2f);
                var pos = new Vector3(at.x, gy + 0.35f + Rand() * 0.55f, at.z);
                var vel = new Vector3(Mathf.Cos(a) * sp, 2f + Rand() * 6.5f, Mathf.Sin(a) * sp);
                var c = i % 3 == 0 ? OpenZFxCatalog.LegSparkA : i % 2 == 1 ? OpenZFxCatalog.LegSparkB : OpenZFxCatalog.LegSparkC;
                EmitP(spark, pos, vel, 0.35f + Rand() * 0.38f, (5f + Rand() * 8f) * px, c, 1f);
            }
            // fire clouds: rise (g -.2), drag .96
            var fire = Ch("leg_fire");
            int fires = hero ? 17 : large ? 13 : 8;
            for (int i = 0; i < fires; i++)
            {
                float a = Rand() * Mathf.PI * 2f;
                var pos = new Vector3(at.x + (Rand() - 0.5f) * vr * 0.38f, gy + 0.25f + Rand() * (large ? 0.85f : 0.5f), at.z + (Rand() - 0.5f) * vr * 0.38f);
                float h = 0.4f + Rand() * 1.5f;
                var vel = new Vector3(Mathf.Cos(a) * h, 0.7f + Rand() * (large ? 2.1f : 1.6f), Mathf.Sin(a) * h);
                var c = i % 4 == 0 ? OpenZFxCatalog.LegFireA : i % 3 == 0 ? hot : OpenZFxCatalog.LegFireC;
                EmitP(fire, pos, vel, (large ? 0.4f : 0.32f) + Rand() * 0.28f, ((large ? 15f : 12f) + Rand() * 11f) * px, c, 1f, Rand() * 0.09f);
            }
        }

        // ------------------------------------------------------------------------------------------------ small M12 effects
        /// <summary>dust(at, s): 5 tan puffs, opacity .32.</summary>
        public void Dust(Vector3 at, float s = 0.5f) => EmitGolden(OpenZFxCatalog.M12Dust, at, s);

        /// <summary>pop(at, s): 9 blue sparks + dust .26 s.</summary>
        public void Pop(Vector3 at, float s = 0.6f) { EmitGolden(OpenZFxCatalog.M12Pop, at, s); Dust(at, 0.26f * s); }

        /// <summary>demolitionDust(at, w, d, s): 8 big lingering puffs, opacity .60.</summary>
        public void DemolitionDust(Vector3 at, float width, float depth, float s = 1f)
        {
            var ch = Ch("m12_dust");
            for (int j = 0; j < OpenZFxCatalog.DemolitionCount; j++)
            {
                float a = j * 2.399f + serial * 0.17f;
                var p = at + new Vector3(Mathf.Cos(a) * width * 0.42f, 1f + j % 3 * 0.3f, Mathf.Sin(a) * depth * 0.42f);
                EmitP(ch, p, new Vector3(Mathf.Cos(a) * 1.3f, 0.65f + j % 3 * 0.12f, Mathf.Sin(a) * 1.3f), 2.8f + j * 0.1f,
                    (5.2f + j % 3 * 1.1f) * s * 2f, OpenZFxCatalog.M12Dust.color, OpenZFxCatalog.DemolitionOpacity);
            }
        }

        /// <summary>splash(at, s): 12 droplets + ripple(at, s). at = the water surface.</summary>
        public void Splash(Vector3 at, float s = 1f)
        {
            if (IsWaterTile(at) && !float.IsNegativeInfinity(WaterY)) at.y = WaterYAt(at);
            if (SoftWater) { SoftSplash(at, s); return; }
            EmitGolden(OpenZFxCatalog.M12Splash, at, s, 1f, waterTint);
            Ripple(at, s);
        }

        /// <summary>A few droplets only (wading footfalls): the M12 splash rule with fewer particles.</summary>
        public void Droplets(Vector3 at, float s, int count)
        {
            var L = OpenZFxCatalog.M12Splash; L.count = Mathf.Clamp(count, 1, 12);
            if (IsWaterTile(at) && !float.IsNegativeInfinity(WaterY)) at.y = WaterYAt(at);
            if (SoftWater) { SoftDrops(at, s, Mathf.Clamp(count, 1, 12), 0.75f); return; }
            EmitGolden(L, at, s, 1f, waterTint);
        }

        /// <summary>ripple(at, s): a flat ring on the water at y + .09, .75 s (.2 + 6 t), fading 1 - smoothstep(.15,1,t).</summary>
        public void Ripple(Vector3 at, float s = 0.5f, float life = OpenZFxCatalog.RippleLife, float lift = OpenZFxCatalog.RippleLift)
        {
            if (IsWaterTile(at) && !float.IsNegativeInfinity(WaterY)) at.y = WaterYAt(at);
            // 4 m L02 river; the ring now ends at the nearest bank (+ .4 m under the wet band)
            float maxR = WaterClearance(at, 4.65f * s) + 0.4f;
            if (4.65f * s > maxR) s = Mathf.Max(0.12f, maxR / 4.65f);
            if (SoftWater) { SoftSwell(at, s, life, lift, 1f); return; }
            EmitP(Ch("m12_ripple"), new Vector3(at.x, at.y + lift, at.z), Vector3.zero, life, 2f * OpenZFxCatalog.RippleScale * s,
                waterTint, 1f);
        }

        // ------------------------------------------------------------------------------------------------ the soft water look
        /// <summary>Level 16: the water's signs are drawn soft: a ring is a few broad swells that spread, thin out and fade (a light
        /// crest, a darker trough, in the water's own tone), a splash is a short crown of round drops with a foam patch that spreads
        /// and dissolves and a little mist, all sized to what made them and lit by the hour. Fewer, better ones: a swell lives a
        /// little longer than the old ring, and rings that would pile up on one spot within SoftSwellGap seconds are left out.</summary>
        public static bool SoftWater;
        public static float SoftSwellAlpha = 0.62f, SoftSwellLife = 1.35f, SoftSwellGap = 0.28f, SoftFoamAlpha = 0.55f, SoftMistAlpha = 0.20f;
        public static Color SoftFoam = new Color(0.80f, 0.77f, 0.66f), SoftDrop = new Color(0.84f, 0.82f, 0.72f);
        public static int SoftSwells, SoftSplashes, SoftSwellsLeftOut;
        Vector3 softLastAt; float softLastT = -10f; int softSerial;

        void SoftSwell(Vector3 at, float s, float life, float lift, float strength)
        {
            float now = Time.time;
            // no pile of rings: a second one on the same spot right after the first adds nothing but lines
            if (now - softLastT < SoftSwellGap && (at - softLastAt).sqrMagnitude < 0.6f * 0.6f) { SoftSwellsLeftOut++; return; }
            softLastAt = at; softLastT = now;
            EmitP(Ch("l16_swell"), new Vector3(at.x, at.y + lift, at.z), Vector3.zero, life * SoftSwellLife, 2f * OpenZFxCatalog.RippleScale * s,
                waterTint, SoftSwellAlpha * strength);
            SoftSwells++;
        }

        void SoftDrops(Vector3 at, float s, int count, float reach)
        {
            var ch = Ch("l16_drop"); if (ch == null) return;
            float k = Mathf.Sqrt(Mathf.Max(0.05f, s));
            for (int j = 0; j < count; j++)
            {
                float a = j * 2.399f + softSerial * 0.61f, r = UnityEngine.Random.value;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var vel = dir * ((0.7f + 1.1f * r) * k * reach * 1.5f) + Vector3.up * ((2.1f + 1.5f * UnityEngine.Random.value) * k);
                EmitP(ch, at + dir * (0.12f * s) + Vector3.up * 0.04f, vel, 0.42f + 0.3f * UnityEngine.Random.value, (0.045f + 0.04f * r) * Mathf.Lerp(0.8f, 1.5f, Mathf.Clamp01(s)), SoftDrop, 0.9f);
            }
        }

        void SoftSplash(Vector3 at, float s)
        {
            softSerial++;
            SoftDrops(at, s, Mathf.Clamp(Mathf.RoundToInt(6f + 8f * s), 5, 18), 1f);
            // the crown's body and its mist: a few soft puffs that rise a hand and are gone
            int puffs = s < 0.45f ? 1 : s < 1f ? 2 : 3;
            for (int j = 0; j < puffs; j++)
                EmitP(Ch("m12_smoke"), at + new Vector3(UnityEngine.Random.Range(-0.15f, 0.15f) * s, 0.12f * s, UnityEngine.Random.Range(-0.15f, 0.15f) * s),
                    Vector3.up * (0.5f + 0.5f * s), 0.38f + 0.14f * j, (0.32f + 0.18f * j) * Mathf.Max(0.35f, s), SoftFoam, SoftMistAlpha + 0.12f);
            EmitP(Ch("l16_foam"), new Vector3(at.x, at.y + OpenZFxCatalog.RippleLift * 0.6f, at.z), Vector3.zero, 1.5f + 0.7f * Mathf.Clamp01(s), 0.85f * Mathf.Max(0.3f, s), SoftFoam, SoftFoamAlpha);
            SoftSplashes++;
            Ripple(at, s);
        }

        /// <summary>How much of the day's glow a calm ring keeps at full night, and its strength then (1 = as by day): after dark
        /// the ring is lit like the water round it and only a little lighter.</summary>
        public static float CalmRingNightGlow = 0.02f, CalmRingNightAlpha = 0.30f;
        Color calmGlow;

        /// <summary>The same ring for quiet signs on the water (a lurking or slowly swimming animal, a floating part), on a channel
        /// of its own whose glow follows the night. By day it is the game's ring exactly.</summary>
        public void RippleCalm(Vector3 at, float s = 0.5f, float life = OpenZFxCatalog.RippleLife, float lift = OpenZFxCatalog.RippleLift)
        {
            if (SoftWater) { Ripple(at, s, life, lift); return; }                                        // the soft swells are lit by the hour themselves
            var ch = Ch("m12_ripple_calm"); if (ch == null) { Ripple(at, s, life, lift); return; }
            if (IsWaterTile(at) && !float.IsNegativeInfinity(WaterY)) at.y = WaterYAt(at);
            float maxR = WaterClearance(at, 4.65f * s) + 0.4f;
            if (4.65f * s > maxR) s = Mathf.Max(0.12f, maxR / 4.65f);
            float night = Mathf.Clamp01(DetailKit.Night);
            SetLinear(ch.mat, "_EmissionColor", calmGlow * Mathf.Lerp(1f, CalmRingNightGlow, night));
            EmitP(ch, new Vector3(at.x, at.y + lift, at.z), Vector3.zero, life, 2f * OpenZFxCatalog.RippleScale * s, waterTint, Mathf.Lerp(1f, CalmRingNightAlpha, night));
        }

        /// <summary>Distance (m) from p to the nearest tile that is not water, searched up to `upTo` m (2 m Zod tiles).</summary>
        static float WaterClearance(Vector3 p, float upTo)
        {
            var g = ZGame.I; if (g == null || g.World == null || g.World.Map == null) return upTo;
            var map = g.World.Map; float best = upTo;
            int r = Mathf.CeilToInt(upTo * 0.5f) + 1, cx = Mathf.FloorToInt(p.x * 0.5f), cy = Mathf.FloorToInt(-p.z * 0.5f);
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    var c = map.ClassAt(cx + dx, cy + dy);
                    if (c == ZClass.Water || c == ZClass.DeepWater) continue;
                    float x0 = (cx + dx) * 2f, x1 = x0 + 2f, z1 = -(cy + dy) * 2f, z0 = z1 - 2f;       // tile rect in world metres
                    float ex = Mathf.Max(0f, Mathf.Max(x0 - p.x, p.x - x1)), ez = Mathf.Max(0f, Mathf.Max(z0 - p.z, p.z - z1));
                    best = Mathf.Min(best, Mathf.Sqrt(ex * ex + ez * ez));
                }
            return best;
        }

        // ------------------------------------------------------------------------------------------------ shots
        /// <summary>Muzzle flash per weapon: M12 core + LEG cone, glow sparks, back smoke, light, M12 ground light.</summary>
        public void Muzzle(Vector3 at, Vector3 dir, FxWeapon w)
        {
            var spec = OpenZFxCatalog.MuzzleOf(w);
            var flatDir = new Vector3(dir.x, 0f, dir.z); flatDir = flatDir.sqrMagnitude > 1e-6f ? flatDir.normalized : Vector3.forward;
            var d3 = dir.sqrMagnitude > 1e-6f ? dir.normalized : flatDir;
            EmitP(Ch("m12_core"), at, Vector3.zero, spec.coreLife, spec.coreScale * 2f, OpenZFxCatalog.M12Core.color, 1f);
            // M12 boltMuzzle disc r .65, #ffad46 x7, .11 s, v heading x .04: on the x12 core channel at 7/12
            if (spec.boltDisc) EmitP(Ch("m12_core"), at, d3 * 0.04f, 0.11f, 0.65f * 2f, OpenZFxCatalog.BoltMuzzleColor, OpenZFxCatalog.BoltMuzzleAlpha);
            if (spec.cone)
            {
                // the direction well defined and drifts < 1 cm over the flash.
                float len = OpenZFxCatalog.MuzzleConeLength * spec.coneSize;
                float life = Mathf.Lerp(OpenZFxCatalog.MuzzleConeLifeMin, OpenZFxCatalog.MuzzleConeLifeMax, Rand());
                EmitP(Ch("leg_cone"), at + d3 * (len * 0.5f), d3 * 0.05f, life, OpenZFxCatalog.MuzzleConeWidth * spec.coneSize,
                    OpenZFxCatalog.MuzzleConeColor, OpenZFxCatalog.MuzzleConeAlpha);
            }
            float px = OpenZFxCatalog.LegPxToM;
            var sparkCh = Ch("leg_spark_light");
            for (int i = 0; i < spec.glowSparks; i++)
            {
                float f = 1.5f + Rand() * 2f;
                var v = new Vector3(flatDir.x * f + (Rand() - 0.5f) * 1.2f, 0.4f + Rand() * 1.1f, flatDir.z * f + (Rand() - 0.5f) * 1.2f);
                EmitP(sparkCh, at, v, 0.18f + Rand() * 0.13f, (5f + Rand() * 5f) * px, i % 2 == 1 ? OpenZFxCatalog.MuzzleSparkA : OpenZFxCatalog.MuzzleSparkB, 1f);
            }
            var smokeCh = Ch("leg_smoke");
            for (int i = 0; i < spec.backSmoke; i++)
            {
                var v = new Vector3(-flatDir.x * (0.5f + Rand()), 0.25f + Rand() * 0.5f, -flatDir.z * (0.5f + Rand()));
                EmitP(smokeCh, at, v, 0.43f + Rand() * 0.22f, 5f * px * OpenZFxCatalog.LegSmokeScale, OpenZFxCatalog.MuzzleSmoke, 0.8f);
            }
            if (pointLights) AddLight(at, spec.lightColor, spec.lightIntensity, spec.lightRange, spec.lightLife, OpenZFxCatalog.LegLightDecayPow, 0f, false);
            if (groundLights && spec.groundLight > 0f) GroundLight(new Vector3(at.x, SurfaceY(at), at.z), spec.groundLight);
        }

        /// <summary>A hitscan shot: muzzle flash, LEG additive tracer + bloom sleeve, impact on arrival (per surface).</summary>
        /// <param name="mps">
        /// 37.5 m/s x gameSpeed (parity P5: EBullet / EFlame / ELaser move 300 px/s, ebullet.cpp:7).</param>
        /// <summary>H0 round 3 (GP3): seconds from the shot until the drawn round reaches `to` (bolt: its head; LEG tracer: its
        /// particle) = the impact frame. Shot() draws with it and BlastWave.BulletDelay throws a robot killed by it with the same
        /// number, so the round, its impact and the throw land on one frame.</summary>
        public static float ShotHeadTime(FxWeapon w, float dist, float mps)
        {
            if (w == FxWeapon.Bolt)
            {
                float half = OpenZFxCatalog.BoltLength * 0.5f, start = Mathf.Min(half, dist * 0.5f);
                float bv = mps > 0f ? mps : OpenZFxCatalog.BoltSpeed;
                return Mathf.Clamp((dist - start - half) / bv, 0.01f, mps > 0f ? 2f : OpenZFxCatalog.BoltLife);
            }
            float speed = mps > 0f ? mps : Mathf.Max(OpenZFxCatalog.TracerMinSpeed, dist / OpenZFxCatalog.TracerLife);
            return Mathf.Max(0.01f, (dist - Mathf.Min(0.4f, dist * 0.5f)) / speed);
        }

        /// <returns>H0 round 3: the round's flight time to `to` (s), 0 when nothing flew.</returns>
        public float Shot(Vector3 from, Vector3 to, FxWeapon w = FxWeapon.Rifle, FxSurface surface = FxSurface.Auto, float mps = 0f)
        {
            var d = to - from; float dist = d.magnitude;
            Muzzle(from, d, w);
            if (dist < 0.05f) return 0f;
            var dir = d / dist;
            float life = ShotHeadTime(w, dist, mps);
            if (w == FxWeapon.Bolt)
            {   // M12 bolt: 80 m/s, the head reaches the target (stretched billboards are centred, so the centre
                // starts and ends half a bolt behind the head), at most .55 s
                float half = OpenZFxCatalog.BoltLength * 0.5f, start = Mathf.Min(half, dist * 0.5f);
                float bv = mps > 0f ? mps : OpenZFxCatalog.BoltSpeed;
                life = EmitRound("m12_bolt", null, from, to, start, bv, life, OpenZFxCatalog.BoltWidth, OpenZFxCatalog.BoltColor, OpenZFxCatalog.BoltAlpha);
            }
            else
            {   // LEG tracer + bloom sleeve over 230 ms, at least M12's 65 m/s
                float speed = mps > 0f ? mps : Mathf.Max(OpenZFxCatalog.TracerMinSpeed, dist / OpenZFxCatalog.TracerLife);
                float start = Mathf.Min(0.4f, dist * 0.5f);
                life = EmitRound("leg_tracer", "leg_tracer_sleeve", from, to, start, speed, life, OpenZFxCatalog.TracerWidth, OpenZFxCatalog.TracerColor, OpenZFxCatalog.TracerAlpha,
                                 OpenZFxCatalog.TracerSleeveWidth, OpenZFxCatalog.TracerSleeveColor, OpenZFxCatalog.TracerSleeveAlpha);
            }
            ShotTraced?.Invoke(from, to, life, w);                                           // vehicle_lights: night glow
            if (surface == FxSurface.Auto && OzShuttleAim.IsSkyMiss(to)) return life;                     // DV118: a miss past the flying shuttle flies on into the sky (no impact)
            if (surface == FxSurface.Auto) surface = SurfaceAt(to);
            if (OpenZHitSparks.Take(Time.time + life, to, dir, w, surface)) return life;     // hitsparks_v1: robots / armour = HDR streak burst + ricochet
            var pi = new PendingImpact { at = Time.time + life, pos = to, surface = surface, bolt = w == FxWeapon.Bolt };
            if (HitSurface.Claim(to, out var anc)) { pi.anchored = true; pi.anc = anc; }                  // IM1: follows the body during the flight
            pendingImpacts.Add(pi);
            return life;
        }

        /// <summary>LEG spawnImpact per surface (+ M12 ground dust, bolt impact, water splash).</summary>
        public void Impact(Vector3 at, FxSurface surface = FxSurface.Auto, bool bolt = false)
        {
            if (surface == FxSurface.Auto) surface = SurfaceAt(at);
            OzSoundV3.GroundImpact(at, surface);                                                        // sound_v3: misses were silent
            var spec = OpenZFxCatalog.ImpactOf(surface);
            if (spec.splash)
            {
                var w = new Vector3(at.x, SurfaceY(at), at.z);
                Droplets(w, OpenZFxCatalog.WaterImpactSplash, 5); Ripple(w, OpenZFxCatalog.WaterImpactSplash * 1.25f);
                return;
            }
            if (bolt)
            {   // M12 boltImpact: core .13 / .40 + 5 sparks + ground light .32
                EmitP(Ch("m12_core"), at, Vector3.zero, OpenZFxCatalog.BoltImpactCoreLife, OpenZFxCatalog.BoltImpactCoreScale * 2f, OpenZFxCatalog.M12Core.color, 1f);
                if (!OpenZHitSparks.Chips(at, surface))                                          // hitsparks_v1: no more 5-point star (road / rock: a few dim chips)
                for (int j = 0; j < 5; j++)
                    EmitP(Ch("m12_spark"), at, new Vector3(Mathf.Sin(j * 2.4f) * 2f, 1f + j % 2, Mathf.Cos(j * 2.4f) * 2f), OpenZFxCatalog.BoltImpactSparkLife,
                        OpenZFxCatalog.BoltImpactSparkScale * 0.07f, OpenZFxCatalog.M12Sparks.color, 1f);
                if (groundLights) GroundLight(new Vector3(at.x, Ground(at.x, at.z), at.z), OpenZFxCatalog.GroundLightBoltImpact);
            }
            float px = OpenZFxCatalog.LegPxToM;
            if (spec.coreRadius > 0f && !bolt) EmitP(Ch("m12_core"), at, Vector3.zero, spec.coreLife, spec.coreRadius * 2f, spec.sparkColor, 1f);   // bolts: M12 core above
            var sp = Ch("leg_impact_spark");
            for (int i = 0; i < spec.sparks; i++)
            {
                float a = Rand() * Mathf.PI * 2f, v = 1.8f + Rand() * 3.5f;
                EmitP(sp, at, new Vector3(Mathf.Cos(a) * v, 0.8f + Rand() * 2.6f, Mathf.Sin(a) * v), 0.13f + Rand() * 0.17f, (3f + Rand() * 4f) * px,
                    i % 3 == 0 ? OpenZFxCatalog.ImpactSparkHot : spec.sparkColor, 1f);
            }
            var sm = Ch("leg_smoke");
            for (int i = 0; i < 2; i++)
                EmitP(sm, at, new Vector3((Rand() - 0.5f) * 0.55f, 0.25f + Rand() * 0.35f, (Rand() - 0.5f) * 0.55f), 0.28f + Rand() * 0.18f, 4f * px * OpenZFxCatalog.LegSmokeScale, spec.smokeColor, 0.8f);
            if (pointLights && spec.lightIntensity > 0f) AddLight(at, spec.sparkColor, spec.lightIntensity, spec.lightRange, spec.lightLife, OpenZFxCatalog.LegLightDecayPow, 0f, false);
            if (spec.dust > 0f) Dust(at, spec.dust);
            if (impactSounds && surface == FxSurface.Metal && !bolt && CombatAudio.I)
                CombatAudio.I.Play("ricochet", at, OpenZFxCatalog.RicochetVolume, OpenZFxCatalog.RicochetThrottle, -1, OpenZFxCatalog.RicochetClips);
        }

        // ------------------------------------------------------------------------------------------------ projectiles
        sealed class Proj
        {
            public bool active; public FxWeapon w; public FxProjectileSpec spec;
            public Vector3 pos, vel, prev; public float age, life, g, smokeRemainder, legTimer, spinX, spinZ; public int legStep, index;
            public bool ease; public Vector3 from, to;
            public double simEnd = -1;
        }

        /// <summary>
        /// An eased rocket
        /// leaves the tube at RocketEaseStart x its top speed, speeds up smoothly (smoothstep ramp) over the first RocketEaseRamp of its
        /// flight, then cruises; it follows the same ballistic curve by progress and lands exactly when the sim's blast does (same total
        /// time). The distance-spaced smoke stays a continuous ribbon; the time-spaced sparkle trail bunches near the tube like the speed.</summary>
        public static bool RocketEasing = true;
        public const float RocketEaseStart = 0.6f, RocketEaseRamp = 0.45f;
        /// <summary>Progress 0..1 along the flight at time fraction tau (see RocketEasing); u(0) = 0, u(1) = 1, u' continuous.</summary>
        public static float EaseProgress(float tau)
        {
            float r = RocketEaseStart, f = RocketEaseRamp, vmax = 1f / (1f - f * (1f - r) * 0.5f);
            tau = Mathf.Clamp01(tau);
            if (tau < f) { float x = tau / f; return vmax * (r * tau + (1f - r) * f * (x * x * x - 0.5f * x * x * x * x)); }
            return vmax * (f * (1f + r) * 0.5f + (tau - f));
        }
        /// <summary>
        /// around LegacyMatch.Launch for the sim's own rocket only (cosmetic salvo rockets keep -1 = end on their own flight time).</summary>
        public static double NextSimEnd = -1;
        /// <summary>Longest a sim rocket waits on its impact point for the sim's blast (paused game, a hitch) before it goes anyway.</summary>
        public const float SimEndGrace = 0.25f;
        /// <summary>Proof hook (harness): per drawn projectile and frame: serial, weapon, age, life, position, eased.</summary>
        public static System.Action<int, FxWeapon, float, float, Vector3, bool> ProjTrace;
        readonly List<Proj> projs = new List<Proj>();
        Material rocketMat, shellMat, tracerMat, exhaustMat, haloMat, grenadeFallbackMat;
        Vector3 rocketNose, shellNose, flameTip; float shellMin, shellMax;
        MaterialPropertyBlock haloBlock;
        // motion smoothing (MOTION BLUR option, OzFxSmooth): the tail quad, its two materials, the sizes of the two projectile meshes
        Mesh blurTailMesh; Material blurTailMat, blurTailHotMat; MaterialPropertyBlock blurTailBlock;
        float rocketLong, rocketWide, shellLong, shellWide; int smoothState = -1;
        static readonly int SmearId = Shader.PropertyToID("_OZFxSmear"), SmearFastId = Shader.PropertyToID("_OZFxSmearFast");
        /// <summary>Proof: tails drawn since the start.</summary>
        public int BlurTails { get; private set; }

        void BuildProjectileMaterials()
        {
            if (litShader)
            {
                rocketMat = new Material(litShader) { name = "FX_RocketBody" }; rocketMat.SetColor("_BaseColor", Color.white); rocketMat.SetColor("_EmissionColor", Color.black);
                shellMat = new Material(litShader) { name = "FX_ShellBody" }; shellMat.SetColor("_BaseColor", OpenZFxCatalog.ShellBody); shellMat.SetColor("_EmissionColor", Color.black);
                grenadeFallbackMat = shellMat;
            }
            tracerMat = FxMaterial("shell_tracer", FxShape.Tracer, 3043, 0f);
            tracerMat.SetFloat("_Intensity", 1f); tracerMat.SetFloat("_UseVertexColor", 0f);
            SetLinear(tracerMat, "_Color", OpenZFxCatalog.ShellTracerLinear);
            exhaustMat = FxMaterial("exhaust", FxShape.FlameMesh, 3043, 0f);
            SetLinear(exhaustMat, "_Color", OpenZFxCatalog.ExhaustColor.linear); exhaustMat.SetFloat("_Intensity", OpenZFxCatalog.ExhaustGain);
            SetLinear(exhaustMat, "_CoreColor", OpenZFxCatalog.Hex("#fff0b0").linear); exhaustMat.SetFloat("_CoreMix", 0.5f);
            exhaustMat.SetFloat("_RimGain", 0.15f); exhaustMat.SetFloat("_UseVertexColor", 0f); exhaustMat.SetFloat("_RingAlpha", 1f);
            haloMat = FxMaterial("rocket_halo", FxShape.Glow, 3049, 0f);
            SetLinear(haloMat, "_CoreColor", new Color(1f, 0.94f, 0.72f).linear); haloMat.SetFloat("_CoreMix", 0.8f); haloMat.SetFloat("_UseVertexColor", 1f);
            if (meshes.TryGetValue("FX_Rocket", out var rm)) NoseAxis(rm, out rocketNose, out _, out _);
            if (meshes.TryGetValue("FX_Shell", out var sm))
            {
                NoseAxis(sm, out shellNose, out shellMin, out shellMax);
                float span = shellMax - shellMin;
                tracerMat.SetVector("_AxisOS", shellNose);
                tracerMat.SetVector("_AxisRange", new Vector4(shellMin + span * OpenZFxCatalog.ShellTracerFrom, shellMin + span * OpenZFxCatalog.ShellTracerTo, 0f, 0f));
                tracerMat.SetFloat("_RingAlpha", 1f);
            }
            if (meshes.TryGetValue("FX_Flame", out var fm)) NoseAxis(fm, out flameTip, out _, out _);
            // motion smoothing: the tail quad and its two materials (the body's own colour, lit, over the scene; additive for a glowing rear)
            if (meshes.TryGetValue("FX_Rocket", out var lm1)) LongWide(lm1, out rocketLong, out rocketWide);
            if (meshes.TryGetValue("FX_Shell", out var lm2)) LongWide(lm2, out shellLong, out shellWide);
            blurTailMesh = BuildBlurTail(); blurTailBlock = new MaterialPropertyBlock();
            blurTailMat = FxMaterial("blur_tail", FxShape.Streak, 3041, 1f);
            blurTailMat.SetFloat("_Squareness", 0f); blurTailMat.SetFloat("_CoreMix", 0f); blurTailMat.SetFloat("_Intensity", 1f);
            blurTailMat.SetFloat("_Lit", 1f); blurTailMat.SetFloat("_Wrap", 0.5f); blurTailMat.SetFloat("_UseVertexColor", 1f);
            blurTailHotMat = FxMaterial("blur_tail_hot", FxShape.Streak, 3044, 0f);
            blurTailHotMat.SetFloat("_Squareness", 0f); blurTailHotMat.SetFloat("_CoreMix", 0.5f); blurTailHotMat.SetFloat("_UseVertexColor", 1f);
            SetLinear(blurTailHotMat, "_CoreColor", OpenZFxCatalog.Hex("#fff0b0").linear);
        }

        /// <summary>A projectile flying over the sim's missile delay (M12 projectile(): v = (target - from) / t, v.y += g t / 2) with
        /// its launch flash. Rockets: continuous exhaust flame x15, M12 smoke puffs every .05 s, LEG halo + sparkle trail. Shells:
        /// dark body, hot rear tracer, tank x2.2. Grenades: spinning grenade.glb x1.55. The sim's detonation calls Blast().</summary>
        public void Launch(Vector3 from, Vector3 to, float duration, FxWeapon w, bool ease = false)
        {
            var spec = OpenZFxCatalog.ProjectileOf(w);
            // the sim's missile delay (ZEvent.value = explodeTime - time) is in sim seconds; ZGame runs the sim at gameSpeed
            // (ZGame.cs:106, the bot match plays at 4x), so the flight must last delay / gameSpeed real seconds to land on the blast
            duration = RealFlight(duration, ZGame.I ? ZGame.I.gameSpeed : 1f);
            float g = OpenZFxCatalog.GravityOf(w);
            g = ShotClearance.LiftGravity(from, to, duration, g, w == FxWeapon.Grenade ? 0.3f : ShotClearance.Margin);
            var dir = to - from; var heading = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
            if (w != FxWeapon.Grenade)
            {
                if (spec.muzzleCore > 0f) Muzzle(from, heading, w == FxWeapon.Cannon ? FxWeapon.Cannon : FxWeapon.TankShell);   // heavy core .46 / .14
                else LaunchFlash(from, heading, spec.launchK, spec.groundLight);
            }
            Proj p = null;
            foreach (var x in projs) if (!x.active) { p = x; break; }
            if (p == null) { if (projs.Count >= OzAirShots.MaxLive) { p = OzAirShots.On ? OldestProjectile() : null; if (p == null) return; } else { p = new Proj(); projs.Add(p); } }   // K337: full = the oldest ends first (was: the new one was not drawn)
            p.active = true; p.w = w; p.spec = spec; p.pos = p.prev = from; p.age = 0f; p.life = duration; p.g = g;
            p.vel = (to - from) / duration; p.vel.y += g * duration * 0.5f;
            p.ease = ease && RocketEasing && w != FxWeapon.Grenade; p.from = from; p.to = to;
            p.simEnd = p.ease ? NextSimEnd : -1; NextSimEnd = -1;
            if (p.ease) p.vel *= RocketEaseStart / (1f - RocketEaseRamp * (1f - RocketEaseStart) * 0.5f);   // out of the tube slower
            p.smokeRemainder = 0f; p.legTimer = 0f; p.legStep = 0; p.spinX = Rand() * 6.28f; p.spinZ = Rand() * 6.28f; p.index = serial++;
            if (OzAirShots.On) OnAirLaunch(p, to);                                                     // K337: bound to its round at the flying shuttle (OpenZFxPlayer.AirShots.cs)
        }

        /// <summary>Real seconds a projectile flies: the sim's missile delay (Zod distance / attack_missile_speed, in sim seconds)
        /// divided by the game speed, so the body lands exactly on the sim's blast at any speed.</summary>
        public static float RealFlight(float simDelay, float gameSpeed) => Mathf.Max(0.05f, simDelay / Mathf.Max(0.05f, gameSpeed));

        /// <summary>M12 launchFlash(at, heading, k) + M12 ground light + a LEG muzzle-style light (130 ms).</summary>
        public void LaunchFlash(Vector3 at, Vector3 heading, float k, float groundLight = 0f)
        {
            heading = heading.sqrMagnitude > 1e-4f ? heading.normalized : Vector3.forward;
            EmitP(Ch("m12_core"), at, Vector3.zero, OpenZFxCatalog.LaunchCoreLife, OpenZFxCatalog.LaunchCoreScale * k * 2f, OpenZFxCatalog.M12Core.color, 1f);
            for (int j = 0; j < 3; j++)
            {
                var p = at - heading * (OpenZFxCatalog.LaunchBack + OpenZFxCatalog.LaunchStep * j);
                EmitP(Ch("m12_flame"), p, -heading * OpenZFxCatalog.LaunchFlameSpeed, OpenZFxCatalog.LaunchFlameLife, 0.37f * OpenZFxCatalog.LaunchFlameScale * k, OpenZFxCatalog.ExhaustColor, 1f);
                EmitP(Ch("m12_smoke"), p, OpenZFxCatalog.LaunchSmokeVelocity, OpenZFxCatalog.LaunchSmokeLife, OpenZFxCatalog.LaunchSmokeScale * k * 2f, OpenZFxCatalog.M12Smoke.color, OpenZFxCatalog.M12Smoke.alpha);
            }
            float size = 1.4f * k;
            if (pointLights) AddLight(at, OpenZFxCatalog.LegLightColor, 7f * size, 5f * size, 0.13f, OpenZFxCatalog.LegLightDecayPow, 0f, true);
            if (groundLights && groundLight > 0f) GroundLight(new Vector3(at.x, SurfaceY(at), at.z), groundLight);
        }

        /// <summary>Tough bazooka back-blast (CombatView.cs:130-135): launch flash out of the rear + dust + pop.</summary>
        public void BackBlast(Vector3 back, Vector3 backwards)
        {
            LaunchFlash(back, backwards, 0.6f);
            Dust(back + Vector3.down * 0.6f, 0.7f);
            Pop(back, 0.4f);
        }

        /// <summary>vehicles_nb_v1 NB6: scale k along the mesh's nose axis n and k x thick across it (the fatter heavy-tank shell).</summary>
        static Matrix4x4 ThickScale(Vector3 n, float k, float thick)
        {
            n = n.sqrMagnitude > 1e-8f ? n.normalized : Vector3.forward; float w = k * thick, d = k - w; var m = Matrix4x4.identity;
            m.m00 = w + d * n.x * n.x; m.m01 = d * n.x * n.y; m.m02 = d * n.x * n.z;
            m.m10 = d * n.y * n.x; m.m11 = w + d * n.y * n.y; m.m12 = d * n.y * n.z;
            m.m20 = d * n.z * n.x; m.m21 = d * n.z * n.y; m.m22 = w + d * n.z * n.z;
            return m;
        }

        void StepProjectiles(float dt)
        {
            float px = OpenZFxCatalog.LegPxToM;
            var camRot = cam ? cam.transform.rotation : Quaternion.identity;
            foreach (var p in projs)
            {
                if (!p.active) continue;
                p.age += dt;
                if (AirShotEnd(p)) continue;                                                           // K337: the hard cap; a sure miss at its cap or a salvo's extra with its round: a small air burst
                if (p.simEnd >= 0 && ZGame.I && ZGame.I.World != null)
                {
                    // frame CombatView draws its blast (it rests on its impact point for at most a frame before), never a frame or two early
                    if (ZGame.I.World.Time >= p.simEnd - 1e-5 || p.age >= p.life + SimEndGrace) { p.active = false; continue; }
                }
                else if (p.age >= p.life) { p.active = false; continue; }                 // the sim's MissileExploded draws the blast
                p.prev = p.pos;
                if (p.ease)
                {
                    float u = EaseProgress(p.age / p.life);
                    var q = Vector3.LerpUnclamped(p.from, p.to, u); q.y += p.g * p.life * p.life * 0.5f * (u - u * u);
                    if (dt > 1e-5f && (q - p.prev).sqrMagnitude > 1e-10f) p.vel = (q - p.prev) / dt;   // resting on the impact point: keep its heading
                    p.pos = q;
                }
                else { p.vel.y -= p.g * dt; p.pos += p.vel * dt; }
                ProjTrace?.Invoke(p.index, p.w, p.age, p.life, p.pos, p.ease);
                var dir = p.vel.sqrMagnitude > 1e-6f ? p.vel.normalized : Vector3.forward;
                var spec = p.spec;
                ProjectileDrawn?.Invoke(p.w, p.pos, p.vel, p.age);                         // vehicle_lights: night glow
                // body
                if (spec.mesh == "grenade")
                {
                    p.spinX += spec.spinX * dt; p.spinZ += spec.spinZ * dt;
                    var rot = Quaternion.Euler(p.spinX * Mathf.Rad2Deg, 0f, p.spinZ * Mathf.Rad2Deg);
                    if (grenadeParts.Count > 0)
                    {
                        var gm = Matrix4x4.TRS(p.pos, rot, Vector3.one * spec.bodyScale);
                        for (int gi = 0; gi < grenadeParts.Count; gi++)
                        {
                            var part = grenadeParts[gi];
                            Graphics.DrawMesh(part.mesh, gm, part.mat, 0, null, part.sub, null, ShadowCastingMode.On, true);
                        }
                    }
                    else if (meshes.TryGetValue("FX_Shell", out var fb) && grenadeFallbackMat)
                        Graphics.DrawMesh(fb, Matrix4x4.TRS(p.pos, rot, Vector3.one * 0.8f), grenadeFallbackMat, 0, null, 0, null, ShadowCastingMode.Off, false);
                }
                else if (meshes.TryGetValue(spec.mesh, out var body))
                {
                    bool rocket = spec.mesh == "FX_Rocket";
                    var rot = Quaternion.FromToRotation(rocket ? rocketNose : shellNose, dir);
                    var m = spec.bodyThick > 1.001f ? Matrix4x4.TRS(p.pos, rot, Vector3.one) * ThickScale(rocket ? rocketNose : shellNose, spec.bodyScale, spec.bodyThick)   // vehicles_nb_v1 NB6
                                                    : Matrix4x4.TRS(p.pos, rot, Vector3.one * spec.bodyScale);
                    var mat = rocket ? rocketMat : shellMat;
                    if (mat) Graphics.DrawMesh(body, m, mat, 0, null, 0, null, ShadowCastingMode.Off, false);   // only grenades cast
                    if (spec.tracer && tracerMat) Graphics.DrawMesh(body, m, tracerMat, 0, null, 0, null, ShadowCastingMode.Off, false);
                    if (OzFxSmooth.On)
                    {   // MOTION BLUR option: the body smears into a soft tail along its flight, its glowing rear / its flame into a hot one
                        float wide = (rocket ? rocketWide : shellWide) * spec.bodyScale * (spec.bodyThick > 1.001f ? spec.bodyThick : 1f);
                        float half = (rocket ? rocketLong : shellLong) * spec.bodyScale * 0.5f;
                        DrawBlurTail(p.pos, p.vel, wide, rocket ? OzFxSmooth.RocketTail : OpenZFxCatalog.ShellBody.linear, 1f, false);
                        if (spec.tracer) DrawBlurTail(p.pos - dir * (half * 0.6f), p.vel, wide * 0.8f, OpenZFxCatalog.ShellTracerLinear, OzFxSmooth.TailTracerGain, true);
                        else if (spec.exhaust) DrawBlurTail(p.pos - dir * (OpenZFxCatalog.ExhaustDistance * spec.exhaustScale), p.vel, wide * 1.2f, OpenZFxCatalog.ExhaustColor.linear, OzFxSmooth.TailExhaustGain, true);
                    }
                }
                // M12 continuous exhaust flame: .35 behind, 1.12 x scale, pulse .94 + .10 sin(8 pi age), never fades
                if (spec.exhaust && meshes.TryGetValue("FX_Flame", out var flame))
                {
                    float es = spec.exhaustScale;
                    float pulse = OpenZFxCatalog.ExhaustPulseA + OpenZFxCatalog.ExhaustPulseB * Mathf.Sin(p.age * Mathf.PI * 8f);
                    var fpos = p.pos - dir * OpenZFxCatalog.ExhaustDistance * es;
                    Graphics.DrawMesh(flame, Matrix4x4.TRS(fpos, Quaternion.FromToRotation(flameTip, -dir), Vector3.one * OpenZFxCatalog.ExhaustBaseScale * es * pulse),
                        exhaustMat, 0, null, 0, null, ShadowCastingMode.Off, false);
                }
                // emits a puff every .05 s, which at our rocket speeds left 0.5 m puffs 1-2 m apart. Now by
                // DISTANCE: a puff every RocketSmokeSpacing x its size along the path (interpolated between frames, jittered size and
                // offset), so the puffs overlap into one soft ribbon at any speed; at most 12 per rocket and frame
                if (spec.m12Smoke)
                {
                    float size = OpenZFxCatalog.RocketSmokeScale * spec.exhaustScale * 2f;
                    float every = Mathf.Max(0.08f, size * OpenZFxCatalog.RocketSmokeSpacing);
                    float seg = Vector3.Distance(p.prev, p.pos);
                    p.smokeRemainder += seg;
                    int guard = 0;
                    while (p.smokeRemainder >= every && guard++ < 12)
                    {
                        p.smokeRemainder -= every;
                        float k = seg > 1e-5f ? Mathf.Clamp01(1f - p.smokeRemainder / seg) : 1f;
                        var sp = Vector3.Lerp(p.prev, p.pos, k) - dir * OpenZFxCatalog.ExhaustDistance * spec.exhaustScale + UnityEngine.Random.insideUnitSphere * size * 0.12f;
                        EmitP(Ch("m12_smoke"), sp, OpenZFxCatalog.RocketSmokeVelocity, OpenZFxCatalog.RocketSmokeLife * UnityEngine.Random.Range(0.85f, 1.1f), size * UnityEngine.Random.Range(0.85f, 1.2f),
                            OpenZFxCatalog.M12Smoke.color, OpenZFxCatalog.M12Smoke.alpha);
                    }
                    if (guard >= 12) p.smokeRemainder = 0f;
                }
                if (spec.legTrail && legGlow)
                {
                    p.legTimer += dt;
                    if (p.legTimer >= OpenZFxCatalog.LegTrailEvery)
                    {
                        p.legTimer -= OpenZFxCatalog.LegTrailEvery;
                        var tail = p.pos - dir * OpenZFxCatalog.LegTrailTail;
                        int step = ++p.legStep;
                        EmitP(Ch("leg_smoke"), tail, new Vector3(-dir.x * 0.12f, 0.18f - dir.y * 0.05f, -dir.z * 0.12f), spec.trailSmokeLife, 5f * px * OpenZFxCatalog.LegSmokeScale,
                            step % 2 == 1 ? OpenZFxCatalog.LegTrailSmokeA : OpenZFxCatalog.LegTrailSmokeB, 0.9f);
                        if (step % 2 == 0)
                            EmitP(Ch("leg_ember"), tail, new Vector3(-dir.x * 0.28f, -dir.y * 0.12f, -dir.z * 0.28f), 0.145f, spec.sparklePx * px,
                                step % 4 != 0 ? OpenZFxCatalog.LegSparkleA : OpenZFxCatalog.LegSparkleB, 1f);
                    }
                }
                // LEG glow halo on the projectile: size (1 + .06 sin(.014 now_ms + 1.7 i)), additive
                if (legGlow && spec.haloPx > 0f && haloMat)
                {
                    float size = spec.haloPx * px * (1f + OpenZFxCatalog.LegHaloPulse * Mathf.Sin(Time.time * 14f + p.index * 1.7f));
                    haloBlock.Clear();
                    SetLinear(haloBlock, "_Color", spec.haloColor.linear);
                    haloBlock.SetFloat("_Intensity", spec.haloAlpha * 1.6f);
                    Graphics.DrawMesh(quad, Matrix4x4.TRS(p.pos, camRot, Vector3.one * size), haloMat, 0, null, 0, haloBlock, ShadowCastingMode.Off, false);
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ wrecks, deaths
        // ------------------------------------------------------------------------------------------------ fx_v2: bodies drawn elsewhere
        /// <summary>fx_v2: one particle on a named channel (nuke missile smoke, celebration tracers).</summary>
        public void EmitChannel(string channel, Vector3 pos, Vector3 vel, float life, float size, Color color, float alpha) => EmitP(Ch(channel), pos, vel, life, size, color, alpha);
        /// <summary></summary>
        public void EmitChannel(string channel, Vector3 pos, Vector3 vel, float life, float size, Color color, float alpha, float delay) => EmitP(Ch(channel), pos, vel, life, size, color, alpha, delay);

        /// <summary>
        /// when the line runs through a bank / a fort (ShotClearance.Bend) it flies two legs over the obstruction (the second leg leaves the
        /// waypoint when the first arrives). Returns the head's time to `to` (headLife when straight).</summary>
        public float EmitRound(string channel, string channel2, Vector3 from, Vector3 to, float start, float speed, float headLife,
                               float size, Color color, float alpha, float size2 = 0f, Color color2 = default, float alpha2 = 0f)
        {
            speed = Mathf.Max(0.5f, speed);
            bool k394Bent = ShotClearance.Bend(from, to, out var w); OzK394RoofShots.Note(from, to, k394Bent, w);   // K394: the roof rounds' check (one null test when it is off)
            if (!k394Bent)
            {
                var dd = to - from; float dist = dd.magnitude; if (dist < 1e-3f) return headLife;
                var sd = dd / dist; var p0 = from + sd * start;
                EmitP(Ch(channel), p0, sd * speed, headLife, size, color, alpha);
                if (channel2 != null) EmitP(Ch(channel2), p0, sd * speed, headLife, size2, color2, alpha2);
                return headLife;
            }
            var d1 = w - from; float l1 = d1.magnitude; var s1 = d1 / Mathf.Max(1e-3f, l1);
            var d2 = to - w; float l2 = d2.magnitude; var s2 = d2 / Mathf.Max(1e-3f, l2);
            float tail = Mathf.Max(0f, (to - from).magnitude - start - headLife * speed);                // the straight round's unused end (half its length)
            float t1 = Mathf.Max(0.01f, (l1 - Mathf.Min(start, l1 * 0.5f)) / speed), t2 = Mathf.Max(0.01f, (l2 - tail) / speed);
            var q0 = from + s1 * Mathf.Min(start, l1 * 0.5f);
            EmitP(Ch(channel), q0, s1 * speed, t1, size, color, alpha);
            EmitP(Ch(channel), w, s2 * speed, t2, size, color, alpha, t1);
            if (channel2 != null) { EmitP(Ch(channel2), q0, s1 * speed, t1, size2, color2, alpha2); EmitP(Ch(channel2), w, s2 * speed, t2, size2, color2, alpha2, t1); }
            return t1 + t2;
        }
        /// <summary></summary>
        public Material CloneChannelMaterial(string channel)
        {
            var c = Ch(channel); if (c == null || !c.mat) return null;
            var m = new Material(c.mat) { name = c.mat.name + " (copy)" };
            m.SetFloat("_Smear", 0f);                                                // the copy's particles do not carry the velocity / size / centre streams
            return m;
        }

        /// <summary>fx_v2: the M12 continuous exhaust flame (FX_Flame x15, pulse .94 + .10 sin(8 pi age)) behind a body drawn by
        /// another script (the strike's nuke), this frame only.</summary>
        public void DrawExhaust(Vector3 tail, Vector3 heading, float scale, float age)
        {
            if (!exhaustMat || !meshes.TryGetValue("FX_Flame", out var flame)) return;
            heading = heading.sqrMagnitude > 1e-6f ? heading.normalized : Vector3.down;
            float pulse = OpenZFxCatalog.ExhaustPulseA + OpenZFxCatalog.ExhaustPulseB * Mathf.Sin(age * Mathf.PI * 8f);
            Graphics.DrawMesh(flame, Matrix4x4.TRS(tail, Quaternion.FromToRotation(flameTip, -heading), Vector3.one * OpenZFxCatalog.ExhaustBaseScale * scale * pulse),
                exhaustMat, 0, null, 0, null, ShadowCastingMode.Off, false);
        }

        /// <summary>fx_v2: an additive camera-facing glow (LEG rocket halo) of sizeM metres, this frame only.</summary>
        public void DrawHalo(Vector3 at, float sizeM, Color color, float intensity)
        {
            if (!haloMat || quad == null) return;
            var rot = cam ? cam.transform.rotation : Quaternion.identity;
            haloBlock.Clear(); SetLinear(haloBlock, "_Color", color.linear); haloBlock.SetFloat("_Intensity", intensity);
            Graphics.DrawMesh(quad, Matrix4x4.TRS(at, rot, Vector3.one * sizeM), haloMat, 0, null, 0, haloBlock, ShadowCastingMode.Off, false);
        }

        // ------------------------------------------------------------------------------------------------ motion smoothing (MOTION BLUR option)
        /// <summary>Longest side and the mean of the two other sides of a mesh's bounds (m): body length and body width.</summary>
        static void LongWide(Mesh m, out float longM, out float wideM)
        {
            var s = m.bounds.size; longM = Mathf.Max(s.x, Mathf.Max(s.y, s.z)); wideM = (s.x + s.y + s.z - longM) * 0.5f;
        }

        /// <summary>Unit tail quad: x 0 (where the tail starts) .. 1 (its end), y -0.5 .. 0.5. uv.x runs .5 .. 1, so the streak shape (a soft
        /// ellipse) is cut in half: full width at the start, closing to a point at the end. Vertex alpha TailAlpha at the start, 0 at the end.</summary>
        static Mesh BuildBlurTail()
        {
            var m = new Mesh { name = "FX_BlurTail" };
            m.SetVertices(new List<Vector3> { new Vector3(0f, -0.5f, 0f), new Vector3(1f, -0.5f, 0f), new Vector3(1f, 0.5f, 0f), new Vector3(0f, 0.5f, 0f) });
            m.SetUVs(0, new List<Vector2> { new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 1f) });
            m.SetNormals(new List<Vector3> { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward });
            var a = new Color(1f, 1f, 1f, OzFxSmooth.TailAlpha); var b = new Color(1f, 1f, 1f, 0f);
            m.SetColors(new List<Color> { a, b, b, a });
            m.SetTriangles(new List<int> { 0, 2, 1, 0, 3, 2 }, 0); m.RecalculateBounds();
            return m;
        }

        /// <summary>MOTION BLUR option: a soft tail behind a fast body drawn this frame. start = where the tail begins (on the body's axis),
        /// vel in m/s, width in m, linear = its colour (linear). Length = speed x OzFxSmooth.Shutter, at most TailMaxWidths widths and
        /// TailMaxM; nothing under TailMinSpeed or when it would be shorter than it is wide. hot = additive (a glowing rear, gain = its
        /// brightness), else the body's colour over the scene, lit like the body (dark at night). Option off: draws nothing.</summary>
        public void DrawBlurTail(Vector3 start, Vector3 vel, float width, Color linear, float gain, bool hot)
        {
            if (!OzFxSmooth.On || blurTailMesh == null || !cam || width <= 0f) return;
            var mat = hot ? blurTailHotMat : blurTailMat; if (!mat) return;
            float speed = vel.magnitude; if (speed < OzFxSmooth.TailMinSpeed) return;
            float len = Mathf.Min(speed * OzFxSmooth.Shutter, Mathf.Min(OzFxSmooth.TailMaxM, width * OzFxSmooth.TailMaxWidths));
            if (len < width) return;
            var dir = vel / speed;
            var side = Vector3.Cross(dir, cam.transform.position - start);           // across the flight, in the screen plane
            if (side.sqrMagnitude < 1e-6f) return;                                   // flying straight at the camera
            side.Normalize(); var n = Vector3.Cross(side, dir);                      // faces the camera
            var trs = new Matrix4x4(new Vector4(-dir.x * len, -dir.y * len, -dir.z * len, 0f), new Vector4(side.x * width, side.y * width, side.z * width, 0f),
                                    new Vector4(n.x, n.y, n.z, 0f), new Vector4(start.x, start.y, start.z, 1f));
            blurTailBlock.Clear(); SetLinear(blurTailBlock, "_Color", linear); blurTailBlock.SetFloat("_Intensity", gain);
            Graphics.DrawMesh(blurTailMesh, trs, mat, 0, null, 0, blurTailBlock, ShadowCastingMode.Off, false);
            BlurTails++;
        }

        /// <summary>LEG spawnWreckFire (vehicle / cannon death): light 620 ms 24 x scale, core r .85 scale, 9 rising glow flames.</summary>
        public void WreckFire(Vector3 at, float scale = 1.35f)
        {
            float px = OpenZFxCatalog.LegPxToM;
            float gy = Ground(at.x, at.z), ch = 0.85f;
            var center = new Vector3(at.x, gy + ch, at.z);
            var L = OpenZFxCatalog.WreckFireLight;
            if (pointLights) AddLight(center, L.color, L.intensityThree * scale, L.range * scale, L.life, L.decayPow, L.flicker, false);
            var wreckCore = Ch("blast_core");                                               // fx_look: heat-ramp flash, not a white disc
            if (wreckCore != null) EmitP(wreckCore, center, Vector3.zero, 0.43f, 0.85f * scale * 2f * OpenZFxCatalog.WreckCoreSizeMul, OpenZFxCatalog.WreckCore, 1f);
            else EmitP(Ch("m12_core"), center, Vector3.zero, 0.43f, 0.85f * scale * 2f, OpenZFxCatalog.WreckCore, 1f);
            var fire = Ch("leg_fire");
            for (int i = 0; i < 9; i++)
            {
                float a = Rand() * Mathf.PI * 2f, spread = (0.18f + Rand() * 0.72f) * scale;
                var pos = new Vector3(at.x + Mathf.Cos(a) * spread, gy + ch * (0.55f + Rand() * 0.7f), at.z + Mathf.Sin(a) * spread);
                var vel = new Vector3(Mathf.Cos(a) * (0.35f + Rand() * 1.1f), 0.9f + Rand() * 2.2f, Mathf.Sin(a) * (0.35f + Rand() * 1.1f));
                var c = i % 3 == 0 ? OpenZFxCatalog.WreckFlameA : i % 2 == 1 ? OpenZFxCatalog.WreckFlameB : OpenZFxCatalog.WreckFlameC;
                EmitP(fire, pos, vel, 0.38f + Rand() * 0.36f, (11f + Rand() * 9f) * scale * px, c, 1f, i * 0.024f + Rand() * 0.06f);
            }
            // LEG vehicle destruction dark smoke: 12 puffs, delay 120-300 ms, 720-1370 ms, 8-16 -> 24-41 px
            var sm = Ch("leg_smoke");
            for (int i = 0; i < 12; i++)
            {
                float a = Rand() * Mathf.PI * 2f, h = 0.45f + Rand() * 1.8f;
                var pos = new Vector3(at.x + (Rand() - 0.5f) * 1.5f, gy + 0.25f + Rand() * 1.1f, at.z + (Rand() - 0.5f) * 1.5f);
                EmitP(sm, pos, new Vector3(Mathf.Cos(a) * h, 0.5f + Rand() * 1.8f, Mathf.Sin(a) * h), 0.72f + Rand() * 0.65f, (8f + Rand() * 8f) * px * OpenZFxCatalog.LegSmokeScale,
                    i % 3 == 0 ? OpenZFxCatalog.WreckSmokeDark : OpenZFxCatalog.WreckSmokeGrey, 0.95f, 0.12f + Rand() * 0.18f);
            }
        }

        /// <summary>Burning wreck, call every ~.25 s (replaces UnitView's tan dust puffs): LEG damaged-vehicle dark smoke, a small
        /// fire and a flickering light (burning actor 8 x (.8 + .2 sin 9t)).</summary>
        public void WreckSmoke(Vector3 at, float scale = 1f, bool burning = true)
        {
            float px = OpenZFxCatalog.LegPxToM, t = Time.time;
            float phase = Mathf.Sin(serial * 1.91f + t * 3.7f);
            EmitP(Ch("leg_smoke"), at + new Vector3(phase * 0.18f, 0f, Mathf.Cos(serial * 2.17f + t * 3.1f) * 0.18f), new Vector3(phase * 0.08f, 0.62f, -phase * 0.06f),
                1.25f, 7f * px * scale * OpenZFxCatalog.LegSmokeScale, OpenZFxCatalog.WreckBurnSmoke, 0.9f);
            if (!burning) return;
            EmitP(Ch("leg_fire"), at + Vector3.down * 0.4f, new Vector3((Rand() - 0.5f) * 0.4f, 0.9f + Rand() * 0.8f, (Rand() - 0.5f) * 0.4f), 0.35f + Rand() * 0.2f,
                (9f + Rand() * 6f) * px * scale, Rand() < 0.5f ? OpenZFxCatalog.WreckFlameB : OpenZFxCatalog.WreckFlameC, 1f);
            if (pointLights) AddLight(at, OpenZFxCatalog.WreckFireLight.color, OpenZFxCatalog.BurnLightThree * scale, OpenZFxCatalog.BurnLightRange * scale * 0.6f, 0.3f, 0.5f, 0.2f, false);
            serial++;
        }

        /// <summary>
        /// WreckSmoke puffs are 0.2-0.4 m): the burning vehicle / cannon wreck sized to read at h 55-70: 2-3 fire tongues over the hull
        /// (1.1-1.9 m), a rising dark smoke column (1.4-2.2 m puffs, 1.3-1.9 m/s up) and a flickering fire light; k = 1 burning .. 0.3
        /// late smoulder (grey, thinner). Called every 0.25-0.8 s by UnitView.Deaths.</summary>
        public static float WreckFlameMin = 2.5f, WreckFlameVar = 0.5f, WreckFlameLift = 0.8f;
        /// <summary>Fire puffs by day. The fire glow channels (the puffs of a burning wreck and of a wreck's first fire, the small
        /// damage, prop, building and ruin fires, the fire clouds of an explosion, the ruin blaze) are additive glow sprites whose
        /// gain only just reaches the day bloom threshold (1.8 against 1.63 linear), so by day they were pale dots without a
        /// glow. Their gain is multiplied by FireGlowDay by day and by FireGlowNight at night (x NightGrade.NightK): the core
        /// passes the threshold and blooms, the halo adds an orange light. Night keeps the catalog's gain.</summary>
        public static float FireGlowDay = 3.0f, FireGlowNight = 1f;
        public static readonly string[] FireGlowChannels = { "leg_fire" };
        float fireGlowNow = 1f;
        /// <summary>The gain on the fire glow channels in this frame (1 = the catalog's).</summary>
        public static float FireGlow => I ? I.fireGlowNow : 1f;
        public void WreckBurn(Vector3 at, float k, bool burning, float spread = 0.7f, Vector3? fireAt = null)
        {
            // depth test hid them; they now rise from the top of the wreck
            float t = Time.time;
            float ph = Mathf.Sin(serial * 1.91f + t * 3.7f);
            EmitP(Ch("leg_smoke"), at + new Vector3(ph * 0.3f, 0.2f, Mathf.Cos(serial * 2.17f + t * 3.1f) * 0.3f),
                new Vector3(ph * 0.25f + 0.15f, 1.3f + Rand() * 0.6f, -ph * 0.2f), 2.4f + Rand() * 0.6f, (1.4f + Rand() * 0.8f) * Mathf.Lerp(0.6f, 1f, k),
                burning ? OpenZFxCatalog.WreckBurnSmoke : OpenZFxCatalog.WreckSmokeGrey, 0.85f * Mathf.Lerp(0.55f, 1f, k));
            if (burning)
            {
                var fire = Ch("leg_fire");
                int n = Rand() < 0.5f ? 3 : 2;
                for (int i = 0; i < n; i++)
                {
                    var o = new Vector3((Rand() - 0.5f) * 2f * spread, Rand() * 0.2f, (Rand() - 0.5f) * 2f * spread);
                    // leg_fire shrinks to 15 % over its life): start size 2.5-3 m, centre lifted 0.8 m so the base sits on the hull top
                    EmitP(fire, (fireAt ?? at + Vector3.down * 0.3f) + o + Vector3.up * WreckFlameLift, new Vector3((Rand() - 0.5f) * 0.4f, 1.2f + Rand(), (Rand() - 0.5f) * 0.4f), 0.45f + Rand() * 0.25f,
                        WreckFlameMin + Rand() * WreckFlameVar, Rand() < 0.5f ? OpenZFxCatalog.WreckFlameB : OpenZFxCatalog.WreckFlameC, 1f);
                }
                if (pointLights) AddLight(at, OpenZFxCatalog.WreckFireLight.color, OpenZFxCatalog.BurnLightThree * 1.3f, OpenZFxCatalog.BurnLightRange * 0.9f, 0.3f, 0.5f, 0.2f, false);
            }
            serial++;
        }

        /// <summary>LEG robot death dust: explosive 10 / else 6 puffs, drifting opposite the killing impulse.</summary>
        public void RobotDeathDust(Vector3 at, Vector3 impulse, bool explosive)
        {
            float px = OpenZFxCatalog.LegPxToM, gy = Ground(at.x, at.z);
            if (WetAt(at, gy))
            {   // OpenZWading already splashes a robot that dies wading (its Dying hook): never two splashes
                if (!OpenZWading.I || !OpenZWading.I.isActiveAndEnabled) Splash(new Vector3(at.x, SurfaceY(at), at.z), explosive ? 0.5f : 0.3f);
                return;
            }
            impulse.y = 0f; float il = impulse.magnitude;
            int n = explosive ? 10 : 6;
            var sm = Ch("leg_smoke");
            for (int i = 0; i < n; i++)
            {
                float a = Rand() * Mathf.PI * 2f, wake = explosive && il > 0f ? 0.22f + Rand() * 0.34f : 0f;
                var pos = new Vector3(at.x + (Rand() - 0.5f) * 0.8f, gy + 0.18f, at.z + (Rand() - 0.5f) * 0.8f);
                var v = new Vector3(Mathf.Cos(a) * Rand() * (explosive ? 1.7f : 1f) - impulse.x * wake, 0.25f + Rand() * (explosive ? 1.15f : 0.65f),
                    Mathf.Sin(a) * Rand() * (explosive ? 1.7f : 1f) - impulse.z * wake);
                EmitP(sm, pos, v, (explosive ? 0.68f : 0.52f) + Rand() * 0.32f, (explosive ? 7f : 5f) * px * OpenZFxCatalog.LegSmokeScale, i % 2 == 1 ? OpenZFxCatalog.DeathDustA : OpenZFxCatalog.DeathDustB, 0.9f);
            }
        }

        // ------------------------------------------------------------------------------------------------ lights
        sealed class FxLight { public Light l; public float age = 99f, life, intensity, pow, flicker, seed; public bool whiteHot; public Color color; }
        readonly List<FxLight> lights = new List<FxLight>();

        void BuildLights()
        {
            for (int i = 0; i < OpenZFxCatalog.MaxLights; i++)
            {
                var go = new GameObject("FxLight" + i); go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>(); l.type = LightType.Point; l.shadows = LightShadows.None; l.enabled = false;
                l.renderMode = LightRenderMode.ForcePixel;
                lights.Add(new FxLight { l = l, seed = i * 13.37f });
            }
        }

        /// <summary>A pooled point light (LEG acquireLight): intensity in three.js units (converted / pi), (1 - l)^pow decay,
        /// optional M12 fire flicker and OpenZ's white-hot first 60 ms. Takes a free or the weakest slot.</summary>
        public void AddLight(Vector3 at, Color color, float intensityThree, float range, float life, float decayPow = 2.2f, float flicker = 0f, bool whiteHot = false)
        {
            float I0 = intensityThree * OpenZFxCatalog.ThreeToUrpLight * OpenZFxCatalog.LightGain;
            FxLight best = null; float bestScore = float.MaxValue;
            foreach (var x in lights)
            {
                float remaining = x.age >= x.life ? -1f : x.intensity * Mathf.Pow(1f - x.age / x.life, x.pow);
                if (remaining < bestScore) { bestScore = remaining; best = x; }
            }
            if (best == null || bestScore > I0 * 1.2f) return;
            if (OzMotion.Reduced && life < 0.15f) { I0 *= 0.55f; life = 0.15f; flicker = 0f; OzCalm.Note("flash light"); }
            best.age = 0f; best.life = Mathf.Max(0.02f, life); best.intensity = I0; best.pow = decayPow; best.flicker = flicker; best.whiteHot = whiteHot; best.color = color;
            best.l.transform.position = at; best.l.range = Mathf.Max(0.5f, range); best.l.color = color; best.l.enabled = true; FireLightStyle(best);
        }

        void StepLights(float dt)
        {
            float t = Time.time; int active = 0;
            foreach (var x in lights)
            {
                if (!x.l.enabled) continue;
                x.age += dt;
                if (x.age >= x.life) { x.l.enabled = false; continue; }
                active++;
                float k = x.age / x.life;
                float env = Mathf.Pow(1f - k, x.pow);
                float fl = x.flicker > 0f ? FireFlicker(x, t) : 1f;                                    // item 400: a fire light's own phase and rate (OpenZFxPlayer.FireBeat.cs)
                bool hot = x.whiteHot && x.age < OpenZFxCatalog.WhiteHotTime;
                x.l.intensity = x.intensity * env * fl * (hot ? OpenZFxCatalog.WhiteHotGain : 1f);
                x.l.color = hot ? Color.Lerp(new Color(1f, 0.95f, 0.85f), x.color, x.age / OpenZFxCatalog.WhiteHotTime) : x.color;
            }
            ActiveLights = active;
        }

        // ------------------------------------------------------------------------------------------------ ground decals
        enum DecalKind { RingM12, RingLeg, Flash, Scorch }
        sealed class Decal
        {
            public Mesh mesh; public bool active; public DecalKind kind; public float age, life, delay, r0, w0, fadeAt, decay; public int priority;
            public Vector3 c; public Color color; public readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock(); public float birth;
        }
        readonly List<Decal> rings = new List<Decal>(), flashes = new List<Decal>(), scorches = new List<Decal>();
        Material ringM12Mat, ringLegMat, flashMat, scorchMat;
        int flashCursor;
        readonly List<Vector3> bv = new List<Vector3>(); readonly List<Color> bc = new List<Color>(); readonly List<Vector3> bn = new List<Vector3>();
        readonly List<Vector2> bu = new List<Vector2>(); readonly List<int> bt = new List<int>();

        void BuildDecalPools()
        {
            ringM12Mat = FxMaterial("ring_m12", FxShape.Band, 3003, OpenZFxCatalog.M12RingOpaqueness);    // fx_look: mostly additive
            ringM12Mat.SetFloat("_BandInner", OpenZFxCatalog.M12RingInner); ringM12Mat.SetFloat("_BandSides", OpenZFxCatalog.M12RingSides); ringM12Mat.SetFloat("_UseVertexColor", 1f);
            ringM12Mat.SetFloat("_BandProfile", 1f);
            ringLegMat = FxMaterial("ring_leg", FxShape.Band, 3003, 0f);
            ringLegMat.SetFloat("_BandInner", OpenZFxCatalog.LegRingInner); ringLegMat.SetFloat("_BandSides", OpenZFxCatalog.LegRingSides); ringLegMat.SetFloat("_UseVertexColor", 1f);
            ringLegMat.SetFloat("_BandProfile", 0.6f);
            flashMat = FxMaterial("ground_light", FxShape.Gauss, 3002, 0f); flashMat.SetFloat("_UseVertexColor", 1f);
            scorchMat = FxMaterial("scorch", FxShape.Scorch, 3001, 1f); scorchMat.SetFloat("_UseVertexColor", 1f); scorchMat.SetFloat("_RingAlpha", 1f);
            SetGroundBand(OpenZFxCatalog.GroundDecalBand);
            for (int i = 0; i < 24; i++) rings.Add(NewDecal());
            for (int i = 0; i < OpenZFxCatalog.GroundLightSlots; i++) flashes.Add(NewDecal());
            for (int i = 0; i < OpenZFxCatalog.ScorchMax; i++) scorches.Add(NewDecal());
            BuildTrackMesh();
        }

        /// <summary>The ground light and the shock rings follow a cached 0.5 m height grid, so raised ground, crater rims and low
        /// props cut them with a hard edge under the depth buffer test. With the camera depth texture they draw without that test
        /// and the shader fades them out only where the scene is more than `band` (m of eye depth) in front of the decal
        /// (OpenZ/FxAdditive _GroundBand). 0, or a pipeline without the depth texture = the plain depth buffer test.</summary>
        public void SetGroundBand(float band)
        {
            bool sceneDepth = true;
            var urp = GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (urp) sceneDepth = urp.supportsCameraDepthTexture;
            bool on = band > 0f && sceneDepth;
            foreach (var m in new[] { flashMat, ringM12Mat, ringLegMat })
            {
                if (!m) continue;
                m.SetFloat("_GroundBand", on ? band : 0f);
                m.SetFloat("_ZTest", (float)(on ? CompareFunction.Always : CompareFunction.LessEqual));
            }
        }

        static Decal NewDecal() { var m = new Mesh { name = "FX_Decal" }; m.MarkDynamic(); return new Decal { mesh = m }; }

        /// <summary>Polar grid conforming to the ground (cached heights) + lift; vertex alpha 0 on water tiles (M12 excludes water).</summary>
        void BuildPolar(Mesh m, Vector3 c, float rMax, float lift, bool maskWater, bool onWaterSurface)
        {
            bv.Clear(); bc.Clear(); bn.Clear(); bu.Clear(); bt.Clear();
            int seg = 48, rows = Mathf.Clamp(Mathf.CeilToInt(rMax / 0.6f), 3, 24);
            void Add(Vector3 p)
            {
                float y = Ground(p.x, p.z);
                bool water = WetAt(p, y);                                                        // open water only: decks keep the ring
                if (onWaterSurface && water && !float.IsNegativeInfinity(WaterY)) y = Mathf.Max(y, WaterYAt(p));
                bv.Add(new Vector3(p.x, y + lift, p.z)); bn.Add(Vector3.up); bu.Add(new Vector2(p.x - c.x, p.z - c.z));
                bc.Add(new Color(1f, 1f, 1f, maskWater && water ? 0f : 1f));
            }
            Add(c);
            for (int r = 1; r <= rows; r++)
                for (int i = 0; i < seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg, rr = rMax * r / rows;
                    Add(c + new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr));
                }
            for (int i = 0; i < seg; i++) { bt.Add(0); bt.Add(1 + (i + 1) % seg); bt.Add(1 + i); }
            for (int r = 1; r < rows; r++)
                for (int i = 0; i < seg; i++)
                {
                    int a0 = 1 + (r - 1) * seg + i, a1 = 1 + (r - 1) * seg + (i + 1) % seg, b0 = a0 + seg, b1 = a1 + seg;
                    bt.Add(a0); bt.Add(a1); bt.Add(b1); bt.Add(a0); bt.Add(b1); bt.Add(b0);
                }
            Upload(m);
        }

        void Upload(Mesh m)
        {
            m.Clear();
            m.SetVertices(bv); m.SetNormals(bn); m.SetColors(bc); m.SetUVs(0, bu); m.SetTriangles(bt, 0);
            m.RecalculateBounds();
        }

        Decal Take(List<Decal> pool)
        {
            Decal best = null;
            foreach (var d in pool) { if (!d.active) return d; if (best == null || d.birth < best.birth) best = d; }
            return best;                                                                        // recycle the oldest (M12 pools)
        }

        /// <summary>M12 shock ring: 32-sided pale-gold annulus (.88-1.0) painted on the ground, .9 s (.2 + 6 t), .55 s, no fade.</summary>
        void RingM12(Vector3 flat, float s)
        {
            var d = Take(rings); float r0 = OpenZFxCatalog.M12RingScale * s;
            d.active = true; d.kind = DecalKind.RingM12; d.age = 0f; d.delay = 0f; d.life = OpenZFxCatalog.M12RingLife; d.r0 = r0; d.c = flat; d.birth = Time.time;
            d.color = OpenZFxCatalog.M12RingTone; d.w0 = OpenZFxCatalog.M12RingWidthOf(s);   // fx_look: band width in metres
            BuildPolar(d.mesh, flat, r0 * 6.2f * 1.02f, OpenZFxCatalog.M12RingLift, true, false);
        }

        /// <summary>LEG outer / echo ring: additive RingGeometry(.82,1,28), radius R (.08 + .92 l), colour x (1 - l).</summary>
        void RingLeg(Vector3 flat, float radius, float life, float delay, Color color)
        {
            var d = Take(rings);
            d.active = true; d.kind = DecalKind.RingLeg; d.age = -delay; d.delay = delay; d.life = life; d.r0 = radius; d.c = flat; d.color = color; d.birth = Time.time;
            BuildPolar(d.mesh, flat, radius * 1.02f, OpenZFxCatalog.LegRingLift, true, false);
        }

        /// <summary>M12 blast ground light: a Gaussian of warm light on the ground, r = 3.5 + 2.3 s,
        /// w = min(1.3, .48 s) exp(-8 age), gone at .48 s. Also fired for every shot (bullet .38 ... MLRS 1.4).</summary>
        public void GroundLight(Vector3 flat, float s, bool electro = false, bool blast = false)
        {
            if (!groundLights) return;
            float r = OpenZFxCatalog.GroundLightRadiusBase + OpenZFxCatalog.GroundLightRadiusPerS * s;
            float w = blast ? Mathf.Min(OpenZFxCatalog.GroundLightMaxWBlast, OpenZFxCatalog.GroundLightWPerSBlast * s)   // fx_look
                            : Mathf.Min(OpenZFxCatalog.GroundLightMaxW, OpenZFxCatalog.GroundLightWPerS * s);
            if (!blast)
                foreach (var f in flashes)   // 10 shots/s guns: refresh a fresh flash close by instead of rebuilding a mesh
                    if (f.active && f.age < 0.08f && (f.c - flat).sqrMagnitude < 2.25f) { if (w > f.w0) { f.w0 = w; f.age = 0f; } return; }
            var d = flashes[flashCursor++ % flashes.Count];
            d.active = true; d.kind = DecalKind.Flash; d.age = 0f; d.life = blast ? OpenZFxCatalog.GroundLightCutBlast : OpenZFxCatalog.GroundLightCut; d.r0 = r; d.w0 = w; d.c = flat; d.birth = Time.time;
            d.decay = blast ? OpenZFxCatalog.GroundLightDecayBlast : OpenZFxCatalog.GroundLightDecay;
            d.color = electro ? OpenZFxCatalog.GroundLightElectroLinear : blast ? OpenZFxCatalog.GroundLightBlastColorLinear : OpenZFxCatalog.GroundLightColorLinear;   // fx_v2
            BuildPolar(d.mesh, flat, r * 1.3f, OpenZFxCatalog.GroundLightLift, false, true);
        }

        /// <summary>M12 scorch mark. priority 0 contact (18-26 s), 1 impact (45-65 s), 2 destruction (90-130 s).</summary>
        public void Scorch(Vector3 at, float s, int priority = 1)
        {
            if (!scorchMarks || s <= 0f) return;
            if (ScorchDecals.Available)
            {   // combatview: ONE scorch system (ScorchDecals: conforming grid, random size x0.7-1.4 / rotation / variant, same M12 lives)
                ScorchDecals.Add(at, Mathf.Max(OpenZFxCatalog.ScorchMinRadius, OpenZFxCatalog.ScorchRadiusPerS * s), (ScorchKind)Mathf.Clamp(priority, 0, 2));
                return;
            }
            float gy = Ground(at.x, at.z);
            if (at.y - gy > OpenZFxCatalog.ScorchMaxHeight || WetAt(at, gy)) return;
            float radius = Mathf.Max(OpenZFxCatalog.ScorchMinRadius, OpenZFxCatalog.ScorchRadiusPerS * s);
            float now = Time.time;
            int contacts = 0, activeCount = 0;
            foreach (var m in scorches)
            {
                if (!m.active) continue;
                activeCount++; if (m.priority == 0) contacts++;
                float dx = m.c.x - at.x, dz = m.c.z - at.z;
                if (m.priority >= priority && Mathf.Sqrt(dx * dx + dz * dz) < Mathf.Max(0.7f, m.r0 * 0.42f) && now - m.birth < (priority == 0 ? 20f : 1f)) return;
            }
            Decal slot = null;
            foreach (var m in scorches) if (!m.active) { slot = m; break; }
            if (slot == null || (priority == 0 && contacts >= OpenZFxCatalog.ScorchContactMax))
            {   // evict the lowest priority, then the faintest
                Decal worst = null;
                foreach (var m in scorches)
                {
                    if (!m.active || m.priority > priority) continue;
                    if (worst == null || m.priority < worst.priority || (m.priority == worst.priority && ScorchOpacity(m) < ScorchOpacity(worst))) worst = m;
                }
                if (worst == null || (priority == 0 && worst.priority != 0)) return;
                slot = worst;
            }
            int id = ++serial;
            float variation = Mathf.Sin(id * 12.9898f) * 43758.5453f; float rnd = variation - Mathf.Floor(variation);
            float life = priority == 2 ? OpenZFxCatalog.ScorchLifeLarge + rnd * OpenZFxCatalog.ScorchLifeLargeRand
                       : priority == 1 ? OpenZFxCatalog.ScorchLifeImpact + rnd * OpenZFxCatalog.ScorchLifeImpactRand
                       : OpenZFxCatalog.ScorchLifeContact + rnd * OpenZFxCatalog.ScorchLifeContactRand;
            slot.active = true; slot.kind = DecalKind.Scorch; slot.age = 0f; slot.life = life; slot.fadeAt = life * (priority == 2 ? OpenZFxCatalog.ScorchFadeLarge : OpenZFxCatalog.ScorchFadeImpact);
            slot.priority = priority; slot.r0 = radius; slot.c = new Vector3(at.x, gy, at.z); slot.birth = now;
            BuildScorch(slot.mesh, slot.c, radius, id * 2.399f, 0.78f + rnd * 0.30f, id);
        }

        static float ScorchOpacity(Decal m) => 1f - Mathf.SmoothStep(0f, 1f, (m.age - m.fadeAt) / Mathf.Max(1e-3f, m.life - m.fadeAt));

        /// <summary>M12 scorch shape: wobbly 16-gon with a radial gradient (.58 / .38 at .56 / 0 at 1.4 of the radius), a soft
        /// outer lip, 7 small streaks at 22 %; rotated by id x 2.399, aspect .78-1.08, conforming to the ground.</summary>
        void BuildScorch(Mesh m, Vector3 c, float radius, float angle, float aspect, int seed)
        {
            bv.Clear(); bc.Clear(); bn.Clear(); bu.Clear(); bt.Clear();
            const int seg = 32, rows = 5;
            float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
            float Burn(float rho) => rho <= 0.56f ? Mathf.Lerp(0.58f, 0.38f, rho / 0.56f) : Mathf.Lerp(0.38f, 0f, (rho - 0.56f) / 0.84f);
            void Add(float lx, float lz, float a)
            {   // local mark space (x, z*aspect) -> rotate -> world
                float x = lx * radius, z = lz * radius * aspect;
                var p = c + new Vector3(x * ca - z * sa, 0f, x * sa + z * ca);
                bv.Add(new Vector3(p.x, Ground(p.x, p.z) + OpenZFxCatalog.ScorchLift, p.z)); bn.Add(Vector3.up); bu.Add(Vector2.zero);
                bc.Add(new Color(1f, 1f, 1f, a * OpenZFxCatalog.ScorchBurnGain));
            }
            Add(0f, 0f, Burn(0f));
            for (int r = 1; r <= rows + 1; r++)
                for (int i = 0; i < seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg, j = a * 8f / Mathf.PI;
                    float edge = 0.84f + 0.19f * Mathf.Sin(j * 2.8f + seed) + 0.05f * Mathf.Cos(j * 5.4f + seed);
                    float rho = r <= rows ? edge * r / rows : edge * 1.08f;
                    Add(Mathf.Cos(a) * rho, Mathf.Sin(a) * rho, r <= rows ? Burn(rho) : 0f);
                }
            for (int i = 0; i < seg; i++) { bt.Add(0); bt.Add(1 + (i + 1) % seg); bt.Add(1 + i); }
            for (int r = 1; r <= rows; r++)
                for (int i = 0; i < seg; i++)
                {
                    int a0 = 1 + (r - 1) * seg + i, a1 = 1 + (r - 1) * seg + (i + 1) % seg, b0 = a0 + seg, b1 = a1 + seg;
                    bt.Add(a0); bt.Add(a1); bt.Add(b1); bt.Add(a0); bt.Add(b1); bt.Add(b0);
                }
            for (int j = 0; j < 7; j++)
            {   // fillRect(cos a r, sin a r, .20, .05) at 22 % alpha
                float a = j * 2.399f + seed, rr = 0.55f + j % 3 * 0.22f, x0 = Mathf.Cos(a) * rr, z0 = Mathf.Sin(a) * rr;
                int b = bv.Count; float al = 0.22f;
                Add(x0, z0, al); Add(x0 + 0.20f, z0, al); Add(x0 + 0.20f, z0 + 0.05f, al); Add(x0, z0 + 0.05f, al);
                bt.Add(b); bt.Add(b + 2); bt.Add(b + 1); bt.Add(b); bt.Add(b + 3); bt.Add(b + 2);
            }
            Upload(m);
        }

        void StepDecals(float dt)
        {
            int drawnRings = 0;
            foreach (var d in rings)
            {
                if (!d.active) continue;
                d.age += dt;
                if (d.age < 0f) continue;                                                   // echo ring delay
                float t = d.age / d.life;
                if (t >= 1f) { d.active = false; continue; }
                if (drawnRings >= OpenZFxCatalog.MaxRingsPerFrame) continue;
                drawnRings++;
                d.mpb.Clear();
                d.mpb.SetVector("_RingCenter", d.c);
                if (d.kind == DecalKind.RingM12)
                {   // full brightness until gone
                    float ringR = d.r0 * (0.2f + 6f * t);
                    d.mpb.SetFloat("_RingRadius", ringR);
                    d.mpb.SetFloat("_BandInner", OpenZFxCatalog.M12RingInnerAt(ringR, d.w0));    // fx_look: thin band, fading
                    d.mpb.SetFloat("_RingAlpha", OpenZFxCatalog.M12RingOpacity * Mathf.Pow(1f - t, OpenZFxCatalog.M12RingFadePow));
                    SetLinear(d.mpb, "_Color", d.color);
                    d.mpb.SetFloat("_Intensity", OpenZFxCatalog.M12RingToneGain);
                    Graphics.DrawMesh(d.mesh, Matrix4x4.identity, ringM12Mat, 0, null, 0, d.mpb, ShadowCastingMode.Off, false);
                }
                else
                {
                    d.mpb.SetFloat("_RingRadius", Mathf.Max(0.01f, d.r0 * (0.08f + 0.92f * t)));
                    d.mpb.SetFloat("_RingAlpha", 1f);
                    SetLinear(d.mpb, "_Color", d.color.linear);
                    d.mpb.SetFloat("_Intensity", OpenZFxCatalog.LegRingGain * (1f - t));
                    Graphics.DrawMesh(d.mesh, Matrix4x4.identity, ringLegMat, 0, null, 0, d.mpb, ShadowCastingMode.Off, false);
                }
            }
            foreach (var d in flashes)
            {
                if (!d.active) continue;
                d.age += dt;
                if (d.age >= d.life) { d.active = false; continue; }
                d.mpb.Clear();
                d.mpb.SetVector("_RingCenter", d.c); d.mpb.SetFloat("_RingRadius", d.r0);
                d.mpb.SetFloat("_RingAlpha", d.w0 * Mathf.Exp(-(d.decay > 0f ? d.decay : OpenZFxCatalog.GroundLightDecay) * d.age));
                SetLinear(d.mpb, "_Color", d.color); d.mpb.SetFloat("_Intensity", 1f);
                Graphics.DrawMesh(d.mesh, Matrix4x4.identity, flashMat, 0, null, 0, d.mpb, ShadowCastingMode.Off, false);
            }
            foreach (var d in scorches)
            {
                if (!d.active) continue;
                d.age += dt;
                if (d.age >= d.life) { d.active = false; continue; }
                d.mpb.Clear(); d.mpb.SetFloat("_RingAlpha", ScorchOpacity(d));
                Graphics.DrawMesh(d.mesh, Matrix4x4.identity, scorchMat, 0, null, 0, d.mpb, ShadowCastingMode.Off, false);
            }
        }

        // ------------------------------------------------------------------------------------------------ tread marks
        Mesh trackMesh; Vector3[] trackV; Color[] trackC; float[] trackBorn, trackAlpha; int trackCursor; float trackClock; bool trackDirty, trackVertsDirty;
        readonly Dictionary<int, Vector3> trackLast = new Dictionary<int, Vector3>();
        sealed class MechFeet { public Transform l, r; public float travel; public int phase; public Vector3 last; public bool init; }
        readonly Dictionary<int, MechFeet> mechFeet = new Dictionary<int, MechFeet>();
        const float StepAlpha = 0.48f;                                                          // 'step' .48, track .36

        void BuildTrackMesh()
        {
            int n = OpenZFxCatalog.TrackSlots;
            trackV = new Vector3[n * 4]; trackC = new Color[n * 4]; trackBorn = new float[n]; trackAlpha = new float[n];
            var tris = new int[n * 6];
            for (int i = 0; i < n; i++)
            {
                int b = i * 4; tris[i * 6] = b; tris[i * 6 + 1] = b + 2; tris[i * 6 + 2] = b + 1; tris[i * 6 + 3] = b; tris[i * 6 + 4] = b + 3; tris[i * 6 + 5] = b + 2;
                trackBorn[i] = -999f;
            }
            trackMesh = new Mesh { name = "FX_TrackMarks" }; trackMesh.MarkDynamic();
            trackMesh.vertices = trackV; trackMesh.colors = trackC; trackMesh.triangles = tris;
            trackMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 5000f);
        }

        /// <summary>Tread marks: call every frame for a moving vehicle; stamps two tracks every .6 m
        /// (wide: tanks / MLRS +-1.15 m, width .5; else +-.8, width .32), only on screen.</summary>
        public void Tracks(int unitId, Vector3 pos, Vector3 forward, bool wide)
        {
            if (!trackMarks || trackMesh == null) return;
            if (!trackLast.TryGetValue(unitId, out var last)) { trackLast[unitId] = pos; return; }
            var d = pos - last; d.y = 0f;
            if (d.sqrMagnitude < OpenZFxCatalog.TrackStep * OpenZFxCatalog.TrackStep) return;
            trackLast[unitId] = pos;
            if (cam)
            {
                var vp = cam.WorldToViewportPoint(pos);
                if (vp.z <= 0f || vp.x < -0.025f || vp.x > 1.025f || vp.y < -0.025f || vp.y > 1.025f) return;
            }
            forward.y = 0f; forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : d.normalized;
            var right = Vector3.Cross(Vector3.up, forward);
            float off = wide ? OpenZFxCatalog.TrackOffsetWide : OpenZFxCatalog.TrackOffsetCar, w = wide ? OpenZFxCatalog.TrackWidthWide : OpenZFxCatalog.TrackWidthCar;
            for (int side = -1; side <= 1; side += 2)
            {
                var c = pos + right * off * side;
                if (WetAt(c)) continue;                                                             // none on water (decks keep them)
                StampMark(c, forward, right, -w * 0.5f, w * 0.5f, -OpenZFxCatalog.TrackLength * 0.5f, OpenZFxCatalog.TrackLength * 0.5f, OpenZFxCatalog.TrackAlpha);
            }
        }

        /// <summary>M12 mech feet: call every frame for a moving walker (the medium tank is the
        /// stamps a footprint (.8 x 1.05, heel + toe, alpha .48) and dust .5; only on screen.</summary>
        public void MechSteps(int unitId, Transform model, Vector3 pos, Vector3 forward)
        {
            if (trackMesh == null || !model) return;
            if (!mechFeet.TryGetValue(unitId, out var m))
            {   // once per mech (allocates the child list once)
                m = new MechFeet();
                foreach (var t in model.GetComponentsInChildren<Transform>(true)) { if (t.name == "MechAnkle_L") m.l = t; else if (t.name == "MechAnkle_R") m.r = t; }
                mechFeet[unitId] = m;
            }
            var d = pos - m.last; d.y = 0f; m.last = pos;
            if (!m.init) { m.init = true; m.travel = 0f; m.phase = 0; return; }
            if (d.sqrMagnitude > 36f) return;                                                        // teleport / respawn: no step
            m.travel += d.magnitude;
            int phase = Mathf.FloorToInt(m.travel * OpenZFxCatalog.MechStepPhase / Mathf.PI);
            if (phase == m.phase) return;
            m.phase = phase;
            if (!OnScreen(pos)) return;
            var foot = ((phase + 1) % 2 + 2) % 2 == 0 ? m.l : m.r;
            var at = foot ? foot.position : pos;
            float gy = Ground(at.x, at.z);
            at.y = gy;
            Dust(at + Vector3.up * 0.1f, OpenZFxCatalog.MechStepDust);
            if (!trackMarks || WetAt(at, gy)) return;
            forward.y = 0f; forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : (d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.forward);
            var right = Vector3.Cross(Vector3.up, forward);
            float w = OpenZFxCatalog.MechStepWidth, l = OpenZFxCatalog.MechStepLength;
            StampMark(at, forward, right, -w * 0.5f, w * 0.5f, -l * 0.5f, -l * 0.5f + l * 0.62f, StepAlpha);   // heel / sole
            StampMark(at, forward, right, -w * 0.43f, w * 0.43f, l * 0.22f, l * 0.5f, StepAlpha);             // toe
        }

        bool OnScreen(Vector3 p)
        {
            if (!cam) return true;
            var vp = cam.WorldToViewportPoint(p);
            return vp.z > 0f && vp.x >= -0.025f && vp.x <= 1.025f && vp.y >= -0.025f && vp.y <= 1.025f;
        }

        /// <summary>One rectangle in the shared mark mesh (local x across, y along forward), conforming to the ground.</summary>
        void StampMark(Vector3 c, Vector3 forward, Vector3 right, float x0, float x1, float y0, float y1, float alpha)
        {
            int i = trackCursor++ % OpenZFxCatalog.TrackSlots, b = i * 4;
            trackV[b] = MarkPoint(c + right * x0 + forward * y0); trackV[b + 1] = MarkPoint(c + right * x1 + forward * y0);
            trackV[b + 2] = MarkPoint(c + right * x1 + forward * y1); trackV[b + 3] = MarkPoint(c + right * x0 + forward * y1);
            trackBorn[i] = Time.time; trackAlpha[i] = alpha;
            trackVertsDirty = trackDirty = true;                                                    // uploaded once per frame in StepTracks
        }

        Vector3 MarkPoint(Vector3 p) => new Vector3(p.x, Ground(p.x, p.z) + OpenZFxCatalog.TrackLift, p.z);

        void StepTracks(float dt)
        {
            if (trackMesh == null) return;
            if (trackVertsDirty) { trackMesh.vertices = trackV; trackVertsDirty = false; }
            trackClock += dt;
            if (trackDirty || trackClock >= 0.25f)
            {   // M12 redraws 4 times a second; the ground darkens by alpha x .58
                trackClock = 0f; trackDirty = false; float now = Time.time;
                for (int i = 0; i < trackBorn.Length; i++)
                {
                    float age = now - trackBorn[i];
                    float a = age >= OpenZFxCatalog.TrackLife ? 0f : trackAlpha[i] * OpenZFxCatalog.ScorchBurnGain * (1f - Mathf.SmoothStep(0f, 1f, (age - OpenZFxCatalog.TrackFadeFrom) / (OpenZFxCatalog.TrackLife - OpenZFxCatalog.TrackFadeFrom)));
                    var col = new Color(1f, 1f, 1f, a);
                    int b = i * 4; trackC[b] = trackC[b + 1] = trackC[b + 2] = trackC[b + 3] = col;
                }
                trackMesh.colors = trackC;
            }
            Graphics.DrawMesh(trackMesh, Matrix4x4.identity, scorchMat, 0, null, 0, null, ShadowCastingMode.Off, false);
        }

        // ------------------------------------------------------------------------------------------------ stage test
        /// <summary>README test step: every blast size and kind in a row 8 m apart, then an MLRS salvo, a bazooka and a tank shell,
        /// a grenade, rifle / MG shots on ground, road and water, a wreck fire. focus = a flat open spot near the camera.</summary>
        public void RunStageTest(Vector3 focus) => StartCoroutine(StageTest(focus));

        System.Collections.IEnumerator StageTest(Vector3 focus)
        {
            var right = Vector3.right; var fwd = Vector3.forward;
            (float r, FxBlastKind k)[] blasts =
            {
                (2.5f, FxBlastKind.Missile), (5f, FxBlastKind.Missile), (5f, FxBlastKind.Grenade), (6.25f, FxBlastKind.Missile),
                (10f, FxBlastKind.Missile), (0f, FxBlastKind.Vehicle), (0f, FxBlastKind.Building), (0f, FxBlastKind.Rock),
            };
            for (int i = 0; i < blasts.Length; i++)
            {
                var p = focus + right * ((i - blasts.Length * 0.5f) * 8f);
                p.y = Ground(p.x, p.z) + 0.4f;
                Blast(p, blasts[i].r, blasts[i].k, 1000 + i);
                yield return new WaitForSeconds(1.1f);
            }
            yield return new WaitForSeconds(1.5f);
            var launcher = focus - fwd * 14f; launcher.y = Ground(launcher.x, launcher.z) + 1.6f;
            for (int i = 0; i < 6; i++)
            {   // MLRS salvo: 6 rockets, 1.2 s flight, the blast where each lands (the sim does this in game)
                var to = focus + fwd * 6f + right * ((i - 2.5f) * 3f); to.y = Ground(to.x, to.z) + 0.25f;
                Launch(launcher + right * (i * 0.2f), to, 1.2f, FxWeapon.Mlrs);
                StartCoroutine(BlastLater(to, 10f, FxBlastKind.Rocket, 1.2f, 2000 + i));
                yield return new WaitForSeconds(0.12f);
            }
            yield return new WaitForSeconds(2f);
            var tgt = focus + fwd * 4f; tgt.y = Ground(tgt.x, tgt.z) + 0.25f;
            Launch(launcher, tgt, 0.9f, FxWeapon.Bazooka); StartCoroutine(BlastLater(tgt, 5f, FxBlastKind.Missile, 0.9f, 3000));
            yield return new WaitForSeconds(1.2f);
            Launch(launcher, tgt + right * 4f, 0.7f, FxWeapon.HeavyShell); StartCoroutine(BlastLater(tgt + right * 4f, 6.25f, FxBlastKind.Missile, 0.7f, 3001));
            yield return new WaitForSeconds(1.0f);
            Launch(launcher, tgt - right * 4f, 1.1f, FxWeapon.Grenade); StartCoroutine(BlastLater(tgt - right * 4f, 5f, FxBlastKind.Grenade, 1.1f, 3002));
            yield return new WaitForSeconds(1.5f);
            for (int i = 0; i < 12; i++)
            {
                var to = tgt + right * ((i % 6) - 2.5f) * 1.5f; to.y = Ground(to.x, to.z) + 0.3f;
                Shot(launcher + right * 0.5f, to, i < 6 ? FxWeapon.Rifle : FxWeapon.MachineGun, i % 3 == 0 ? FxSurface.Road : i % 3 == 1 ? FxSurface.Metal : FxSurface.Ground);
                yield return new WaitForSeconds(0.1f);
            }
            WreckFire(tgt, OpenZFxCatalog.WreckScaleTank);
            for (int i = 0; i < 20; i++) { WreckSmoke(tgt + Vector3.up * 1.2f, 1f, true); yield return new WaitForSeconds(0.25f); }
        }

        System.Collections.IEnumerator BlastLater(Vector3 at, float radiusM, FxBlastKind kind, float delay, int id)
        {
            yield return new WaitForSeconds(delay);
            Blast(at, radiusM, kind, id);
        }

        // ------------------------------------------------------------------------------------------------ per frame
        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (!cam) cam = Camera.main;
            if (Shake != null) Shake.scale = shakeScale;
            float now = Time.time;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var p = pending[i]; if (p.at > now) continue;
                pending.RemoveAt(i);
                if (p.ch != null && p.ch.ps) { p.ch.ps.Emit(p.ep, 1); Emitted++; }
            }
            for (int i = pendingImpacts.Count - 1; i >= 0; i--)
            {
                var p = pendingImpacts[i]; if (p.at > now) continue;
                pendingImpacts.RemoveAt(i);
                var ip = p.pos; if (p.anchored && HitSurface.Now(p.anc, out var np, out _)) ip = np;   // IM1
                Impact(ip, p.surface, p.bolt);
            }
            // motion smoothing (MOTION BLUR option): the shader stretch of smoke / fire quads and the speed stretch of the streak channels
            bool smooth = OzFxSmooth.On;
            Shader.SetGlobalVector(SmearId, smooth ? new Vector4(OzFxSmooth.SmearShutter, OzFxSmooth.SmearMax, OzFxSmooth.SoftBirth, 0f) : Vector4.zero);
            Shader.SetGlobalVector(SmearFastId, new Vector4(OzFxSmooth.SmearFastFrom, OzFxSmooth.SmearFastFull, OzFxSmooth.SmearMaxFast, 0f));
            if (smoothState != (smooth ? 1 : 0))
            {
                smoothState = smooth ? 1 : 0;
                foreach (var kv in channels)                                                 // the catalog's channels; the exp2_ channels keep their own speed stretch
                {
                    var c = kv.Value; if (c == null || !c.r || c.def.mesh) continue;
                    if (System.Array.IndexOf(OzFxSmooth.SparkChannels, kv.Key) >= 0)
                    {   // sparks: each one a streak along its own velocity (a round head, a tail that fades to nothing); off = the channel as the catalog made it
                        bool streak = smooth || c.def.stretch;
                        c.r.renderMode = streak ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
                        if (streak) { c.r.lengthScale = smooth ? OzFxSmooth.SparkRest : c.def.lengthScale; c.r.velocityScale = smooth ? OzFxSmooth.SparkSeconds : 0f; c.r.cameraVelocityScale = 0f; }
                        c.mat.SetFloat("_StreakBlur", smooth ? Mathf.Max(0.1f, OzFxSmooth.SparkFade) : 0f);
                        c.mat.SetFloat("_Smear", streak ? 0f : OzFxSmooth.SmearFire);
                    }
                    else if (c.def.stretch && !kv.Key.StartsWith("exp2_", System.StringComparison.Ordinal))
                        c.r.velocityScale = smooth ? OzFxSmooth.StreakVelocityScale : 0f;   // tracers, bolts, droplets: longer with speed
                }
            }
            // fire puffs by day: the fire glow channels' gain follows the time of day (FireGlowDay by day, FireGlowNight at night)
            float fireGlow = Mathf.Lerp(FireGlowDay, FireGlowNight, Mathf.Clamp01(NightGrade.NightK));
            if (Mathf.Abs(fireGlow - fireGlowNow) > 0.005f)
            {
                fireGlowNow = fireGlow;
                foreach (var n in FireGlowChannels) { var fc = Ch(n); if (fc != null && fc.mat) fc.mat.SetFloat("_Intensity", fc.def.intensity * fireGlow); }
            }
            StepLights(dt);
            if (e2Ready) StepExp2(dt);
            StepProjectiles(dt);
            StepDecals(dt);
            StepTracks(dt);
        }
    }

    /// <summary>
    /// M12 camera impact for the RTS camera: impulses only from strength >= .8 blasts that are on screen
    /// and big on screen; the peak is (4 + 3 s) screen pixels (clamped), energy + .64 s, decaying exp(-5 t), offset
    /// (sin 61.3 t, .62 sin(73.1 t + .7)) along the camera's right / up axes: never toward / away from the view. Applied in
    /// RenderPipelineManager.beginCameraRendering and removed in endCameraRendering, so RtsCamera, picking and UI never see it.
    /// </summary>
    public sealed class OpenZFxShake : IDisposable
    {
        public static OpenZFxShake I { get; private set; }
        public float scale = 1f;
        public int Events { get; private set; }
        public float Energy => energy;
        float energy, amplitude, time, lastImpactTime = -10f, lastImpactS, rumbleAmount, lastSmallTime = -10f; int lastFrame = -1;
        Vector3 offset; bool applied; Camera appliedCam;
        readonly Dictionary<int, (Vector3 at, float s)> rumbles = new Dictionary<int, (Vector3, float)>();
        readonly Queue<float> recent = new Queue<float>();
        readonly List<(Vector3 at, float s, float start, float dur)> timed = new List<(Vector3, float, float, float)>();

        public OpenZFxShake()
        {
            I = this;
            RenderPipelineManager.beginCameraRendering += Begin;
            RenderPipelineManager.beginCameraRendering += KeepMotion;                                // after Begin: it sees the camera where this frame is drawn from
            RenderPipelineManager.endCameraRendering += End;
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= Begin;
            RenderPipelineManager.beginCameraRendering -= KeepMotion; MotionKept = false;
            RenderPipelineManager.endCameraRendering -= End;
            Restore();
            if (I == this) I = null;
        }

        static float Span(Camera c, Vector3 at)
        {
            if (c.orthographic) return 2f * c.orthographicSize;
            return 2f * Vector3.Distance(c.transform.position, at) * Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }

        static bool OnScreen(Camera c, Vector3 at)
        {   // M12: NDC |x| < 1, |y| < 1, in front of the camera
            var vp = c.WorldToViewportPoint(at);
            return vp.z > c.nearClipPlane && vp.z < c.farClipPlane && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f;
        }

        /// <summary>M12 impact(at, s). Returns true when it kicked the camera.</summary>
        public bool Impact(Vector3 at, float s)
        {
            if (!(s >= OpenZFxCatalog.ShakeMinStrength) || scale <= 0f) return false;
            var c = Camera.main; if (!c || !OnScreen(c, at)) return false;
            float span = Span(c, at);
            if (2f * 1.15f * s / Mathf.Max(0.01f, span) < OpenZFxCatalog.ShakeMinScreenFraction) return false;   // not big on screen
            float now = Time.unscaledTime;
            if (now - lastImpactTime < OpenZFxCatalog.ShakeMinInterval && s <= lastImpactS) return false;
            // larger), a few px, one noticeable kick per OzShakeGate.Cooldown s (a clearly stronger blast overrides)
            bool small = s < OpenZFxCatalog.ShakeBigS;
            if (small && now - lastSmallTime < OpenZFxCatalog.ShakeSmallGap) return false;
            float px = Mathf.Min(OpenZFxCatalog.ShakeBasePx + OpenZFxCatalog.ShakePxPerS * s, small ? OpenZFxCatalog.ShakeSmallMaxPx : OpenZFxCatalog.ShakeMaxPx);
            if (!OzShakeGate.Allow(s >= OpenZFxCatalog.ShakeNuke ? "nuke" : s >= OpenZFxCatalog.ShakeCollapse ? "collapse" : s >= 2f ? "building/heavy" : "vehicle", at, px, false)) return false;
            while (recent.Count > 0 && now - recent.Peek() > 1f) recent.Dequeue();
            recent.Enqueue(now);
            float busy = recent.Count > OpenZFxCatalog.ShakeBusyCount ? 1f / Mathf.Sqrt(recent.Count - OpenZFxCatalog.ShakeBusyCount + 1) : 1f;
            if (small) lastSmallTime = now;
            amplitude = Mathf.Min(OpenZFxCatalog.ShakeMaxWorld, span / OpenZFxCatalog.ShakeReferenceHeight * px) * busy * scale;   // latest impact sets it
            energy = Mathf.Min(1f, energy + OpenZFxCatalog.ShakeEnergyPerS * s);
            lastImpactTime = now; lastImpactS = s; Events++;
            return true;
        }

        /// <summary>M12 setRumble: continuous rumble (nuke 4 x (1 - smoothstep(.4, 8.5, age))), max 4 sources; s &lt;= 0 clears.</summary>
        public void SetRumble(int id, Vector3 at, float s)
        {
            if (!(s > 0.001f)) { rumbles.Remove(id); return; }
            if (!rumbles.ContainsKey(id) && rumbles.Count >= OpenZFxCatalog.ShakeMaxRumbles) return;
            rumbles[id] = (at, s);
        }

        /// <summary>
        /// over .15 s and out by <paramref name="seconds"/>; only when <paramref name="at"/> is on screen and OzShakeGate allows it.</summary>
        public bool TimedRumble(Vector3 at, float s, float seconds, string src)
        {
            if (scale <= 0f || !OzShakeGate.Allow(src, at, s * OpenZFxCatalog.ShakeRumblePerS)) return false;
            timed.Add((at, s, Time.unscaledTime, Mathf.Max(0.5f, seconds))); Events++;
            return true;
        }

        /// <summary>A named event kick outside the blast strength table (a building or a bridge blowing up, a rocket salvo landing):
        /// peak <paramref name="px"/> (1080 px reference) at ShakeKickRefSpan or nearer, less the further the camera is (never under
        /// ShakeKickFarK of it), decaying like an impact. Only when <paramref name="at"/> is in the camera view (ShakeKickEdge of the
        /// view size outside the screen edge still counts). always = not held back by the shared cooldown; the SCREEN SHAKE and
        /// CALM SCREEN switches still apply.</summary>
        public bool Kick(Vector3 at, float px, string src, bool always = false)
        {
            if (scale <= 0f || !(px > 0f)) return false;
            var c = Camera.main; if (!c) return false;
            var vp = c.WorldToViewportPoint(at); float edge = OpenZFxCatalog.ShakeKickEdge;
            if (vp.z <= c.nearClipPlane || vp.z >= c.farClipPlane || vp.x < -edge || vp.x > 1f + edge || vp.y < -edge || vp.y > 1f + edge) { OzShakeGate.OffScreen++; return false; }
            float span = Span(c, at);
            px *= Mathf.Clamp(OpenZFxCatalog.ShakeKickRefSpan / Mathf.Max(1f, span), OpenZFxCatalog.ShakeKickFarK, 1f);   // scaled by the distance
            if (!OzShakeGate.Allow(src, at, px, false, always)) return false;
            float a = Mathf.Min(OpenZFxCatalog.ShakeMaxWorld, span / OpenZFxCatalog.ShakeReferenceHeight * px) * scale;
            amplitude = Mathf.Max(a, amplitude * energy); energy = 1f;                                  // never weaker than a kick still playing
            lastImpactTime = Time.unscaledTime; Events++;
            return true;
        }

        public void Reset() { Restore(); energy = 0f; rumbles.Clear(); timed.Clear(); rumbleAmount = 0f; }

        /// <summary>How far the shake moves the camera in this frame, in screen px at the 1080 px reference (0 = not at all). For the
        /// motion blur gate while the shake is not kept out of the motion vectors (KeepMotion): a kick moves every pixel of the
        /// picture, so the blur then waits until it has settled.</summary>
        public float AmountPx(Camera c)
        {
            if (!c || !OzShakeGate.SettingOn) return 0f;
            float amount = Mathf.Max(amplitude * energy, rumbleAmount);
            if (amount < 1e-6f) return 0f;
            float dist = c.transform.position.y / Mathf.Max(0.2f, -c.transform.forward.y);            // to the ground along the view axis
            return amount / Mathf.Max(1e-4f, 2f * dist * Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad) / OpenZFxCatalog.ShakeReferenceHeight);
        }

        // ---------------------------------------------------------------------------------------- motion blur: camera motion kept out
        /// <summary>MOTION BLUR option on: the camera never causes blur. A pan, a zoom, a jump of the view, a kick, a rumble, the RTS
        /// camera's own jitter and the ending's camera move shift every pixel of the picture. Left in the motion vectors they smear the
        /// whole frame, and holding the blur back until the camera rests leaves most blasts without any. So each frame, before the render pipeline stores the camera matrices of this frame, the
        /// matrix it kept from the frame before is carried over to this frame's camera pose (kept matrix x last pose x inverse of
        /// this pose). A point that stands still in the world then has no motion at all, whatever the camera did; what moves in the
        /// world keeps its own motion and blurs. The picture, the shake itself and the HUD are untouched. The pipeline has no public
        /// way to set that matrix: its per-camera motion data is reached by field name (render pipeline package 17.3). If a name is
        /// not found nothing is changed, <see cref="MotionKept"/> stays false and the gate waits for the shake to settle, as before.
        /// Set to false for exactly that earlier behaviour.</summary>
        public static bool KeepCameraOutOfMotion = true;
        /// <summary>True when the last drawn frame of the game camera had its camera motion kept out of the motion vectors
        /// (UnitLookMotionGate: a shake and the ending's camera then do not count as a moving camera).</summary>
        public static bool MotionKept { get; private set; }
        static System.Reflection.FieldInfo mvDataField, mvMatrixField; static bool mvBound;
        Camera mvCam; Matrix4x4[] mvMatrix; Vector3 mvPos; Quaternion mvRot; Matrix4x4 mvProj; int mvFrame = -1;

        /// <summary>The pipeline's kept view-projection matrices of this camera (element 0 = the last drawn frame, until the pipeline
        /// stores the new one); null when the camera has no pipeline data or the names are not there.</summary>
        static Matrix4x4[] MotionMatrix(Camera c)
        {
            if (!mvBound)
            {
                mvBound = true;
                const System.Reflection.BindingFlags bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                mvDataField = typeof(UnityEngine.Rendering.Universal.UniversalAdditionalCameraData).GetField("m_MotionVectorsPersistentData", bf);
                mvMatrixField = mvDataField != null ? mvDataField.FieldType.GetField("m_ViewProjection", bf) : null;
                if (mvMatrixField == null || mvMatrixField.FieldType != typeof(Matrix4x4[]))
                {
                    mvMatrixField = null;
                    Debug.LogWarning("[OpenZ] motion blur: the render pipeline's camera motion data was not found; the blur waits for a camera shake to settle");
                }
            }
            if (mvMatrixField == null || !c.TryGetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(out var data)) return null;
            var md = mvDataField.GetValue(data);
            return md != null ? mvMatrixField.GetValue(md) as Matrix4x4[] : null;
        }

        /// <summary>Runs after Begin, so the camera stands where this frame is drawn from (kick and rumble included).</summary>
        void KeepMotion(ScriptableRenderContext ctx, Camera c)
        {
            if (c == null || c.cameraType != CameraType.Game || c != Camera.main) return;
            if (!KeepCameraOutOfMotion || !UnitLookMotionGate.MotionBlurOn) { MotionKept = false; mvCam = null; return; }   // option off: nothing is touched
            int f = Time.frameCount;
            if (mvCam == c && mvFrame == f) return;                                                    // the same frame drawn again: done
            var t = c.transform; Vector3 p = t.position; Quaternion q = t.rotation;
            bool next = mvCam == c && mvFrame == f - 1;                                                // the frame right after the one whose pose is kept
            var proj = GL.GetGPUProjectionMatrix(c.projectionMatrix, true);                             // the lens (field of view, aspect) of this frame
            if (mvCam != c) { mvCam = c; mvMatrix = MotionMatrix(c); }
            bool ok = next && mvMatrix != null && mvMatrix.Length > 0;
            if (ok)
            {
                bool sameRot = q.x == mvRot.x && q.y == mvRot.y && q.z == mvRot.z && q.w == mvRot.w;
                if (!sameRot || p.x != mvPos.x || p.y != mvPos.y || p.z != mvPos.z)
                {
                    var r = sameRot ? Quaternion.identity : mvRot * Quaternion.Inverse(q);
                    mvMatrix[0] = mvMatrix[0] * Matrix4x4.TRS(mvPos - r * p, r, Vector3.one);            // last frame's matrix as if its camera had stood in this frame's pose
                }
                if (proj != mvProj && Mathf.Abs(mvProj.determinant) > 1e-12f) mvMatrix[0] = proj * mvProj.inverse * mvMatrix[0];   // ... and had this frame's lens (a field of view change is camera motion too)
            }
            MotionKept = ok;
            mvPos = p; mvRot = q; mvProj = proj; mvFrame = f;
        }

        void Begin(ScriptableRenderContext ctx, Camera c)
        {
            if (c == null || c.cameraType != CameraType.Game || c != Camera.main) return;
            Restore();
            if (!OzShakeGate.SettingOn) { if (rumbles.Count > 0 || timed.Count > 0 || energy > 0.01f) { if (OzMotion.Reduced) OzCalm.Note("rumble"); rumbles.Clear(); timed.Clear(); energy = 0f; } return; }
            if (lastFrame != Time.frameCount)
            {
                lastFrame = Time.frameCount;
                float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.1f);
                time += dt; energy *= Mathf.Exp(-dt * OpenZFxCatalog.ShakeDecay);
                rumbleAmount = 0f;
                foreach (var kv in rumbles)
                {
                    if (!OnScreen(c, kv.Value.at)) continue;
                    rumbleAmount = Mathf.Max(rumbleAmount, Mathf.Min(OpenZFxCatalog.ShakeMaxWorld,
                        Span(c, kv.Value.at) / OpenZFxCatalog.ShakeReferenceHeight * kv.Value.s * OpenZFxCatalog.ShakeRumblePerS) * scale);
                }
                float tnow = Time.unscaledTime;
                for (int i = timed.Count - 1; i >= 0; i--)
                {
                    var r = timed[i]; float age = tnow - r.start;
                    if (age >= r.dur) { timed.RemoveAt(i); continue; }
                    float k = Mathf.SmoothStep(0f, 1f, age / 0.15f) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, r.dur, age)));
                    rumbleAmount = Mathf.Max(rumbleAmount, Mathf.Min(OpenZFxCatalog.ShakeMaxWorld,
                        Span(c, r.at) / OpenZFxCatalog.ShakeReferenceHeight * r.s * OpenZFxCatalog.ShakeRumblePerS * k) * scale);
                }
            }
            float amount = Mathf.Max(amplitude * energy, rumbleAmount);
            if (amount < 1e-6f) return;
            offset = c.transform.rotation * new Vector3(Mathf.Sin(time * OpenZFxCatalog.ShakeFreqX) * amount,
                Mathf.Sin(time * OpenZFxCatalog.ShakeFreqY + OpenZFxCatalog.ShakePhaseY) * amount * OpenZFxCatalog.ShakeYRatio, 0f);
            c.transform.position += offset; applied = true; appliedCam = c;
        }

        void End(ScriptableRenderContext ctx, Camera c) { if (c == appliedCam) Restore(); }

        void Restore()
        {
            if (!applied) return;
            if (appliedCam) appliedCam.transform.position -= offset;
            applied = false; appliedCam = null; offset = Vector3.zero;
        }
    }
}
