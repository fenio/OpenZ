using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// The ground dust of a fort's blast as soft low fog. It replaces the look (never the timing or the reach) of three dust sources of
    /// the fort's death, which drew as plain round balls that popped on and off and cut the ground with a hard edge:
    /// <list type="bullet">
    /// <item><b>the dust wall</b> (OpenZFortShockwave.DustWall): every 0.12 s puffs on the shock front, out to 115 m, thrown outward
    /// with the front: here each one is a wider, lower fog puff that fades in over a quarter of its life, drifts outward, slows,
    /// grows and thins away over the rest (life 2.6-3.6 s instead of 1.4-2.0; every second puff, 1.35 x wider);</item>
    /// <item><b>the base skirt</b> (NukeFx.Skirt): ten rings, one every 0.3 s, out to 51 m: here a ring of overlapping fog puffs that
    /// roll outward and linger 4-6 s;</item>
    /// <item><b>the mushroom's base surge</b> (OpenZNukeMushroom, its 64 ground puffs): drawn by the same fog shader
    /// (OpenZNukeMushroom.Kd.cs).</item>
    /// </list>
    /// The shader OpenZ/BlastFog (Shaders/OzBlastFog.shader, kept in builds by Resources/OpenZ/FX/M_OzBlastFog) takes the game's own
    /// smoke flipbook and billow detail, a wide soft radial falloff and a volume soft particle (the share of each fog ball that lies in
    /// front of the ground is the share drawn), so where fog meets the terrain it thins out smoothly. Fades over life come from the
    /// particle colour curve (<see cref="AlphaKeys"/>). One particle system for the whole level, built on first use.
    /// Switch: <see cref="Enabled"/> (on). Off, or the shader missing = the old m12_dust puffs exactly as before.
    /// </summary>
    public sealed class OzBlastFog : MonoBehaviour
    {
        public static bool Enabled = true;
        /// <summary>Dust wall puffs: size x (the old 5-7.5 m), life (s), height over the ground (m), outward speed x, alpha x.</summary>
        public static float WallSize = 1.35f, WallLifeMin = 2.6f, WallLifeMax = 3.6f, WallLift = 0.9f, WallSpeed = 0.75f, WallAlpha = 0.9f;
        /// <summary>Skirt puffs per ring, size (m), life (s), alpha.</summary>
        public static int SkirtPerRing = 9;
        public static float SkirtSizeMin = 11f, SkirtSizeMax = 17f, SkirtLifeMin = 4f, SkirtLifeMax = 6f, SkirtAlpha = 0.42f;
        /// <summary>Opacity over life (time share, alpha): in over the first quarter, held, then a long thin-out to nothing.</summary>
        public static readonly Vector2[] AlphaKeys = { new Vector2(0f, 0f), new Vector2(0.22f, 1f), new Vector2(0.45f, 0.9f), new Vector2(0.75f, 0.42f), new Vector2(1f, 0f) };
        /// <summary>Size over life: x1 at birth, x <see cref="GrowTo"/> at the end (fog spreads as it thins).</summary>
        public static float GrowTo = 1.6f;
        /// <summary>Linear drag (1/s): the puffs slow down as they spread.</summary>
        public static float Drag = 0.9f;
        /// <summary>The pool: the dust wall at the widest zoom keeps up to ~775 puffs alive (about 250 a second x 3.1 s) and the skirt
        /// up to ~110; the wall stops adding puffs while fewer than <see cref="SkirtReserve"/> places are free, so the skirt always has room
        /// (a full pool would drop new puffs silently: gaps in the wall, missing skirt rings).</summary>
        public const int Capacity = 900, SkirtReserve = 130;
        /// <summary>Every Nth dust-wall puff is drawn (the fog puffs are bigger and live longer: the same fog with fewer of them).</summary>
        public static int WallEvery = 2;
        static int wallSeq;

        public static OzBlastFog I { get; private set; }
        /// <summary>Proof: fog puffs emitted (wall, skirt), the most alive at once, and the old puffs they replaced.</summary>
        public static int WallPuffs, SkirtPuffs, PeakAlive, WallHeld;
        public static string LastShader = "-";

        ParticleSystem ps; ParticleSystemRenderer pr; Material mat;
        static Material shared;
        static bool tried;

        static readonly List<ParticleSystemVertexStream> Streams = new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
            ParticleSystemVertexStream.AgePercent, ParticleSystemVertexStream.StableRandomX,
            ParticleSystemVertexStream.Velocity, ParticleSystemVertexStream.SizeX, ParticleSystemVertexStream.Center,
        };

        /// <summary>The fog material (a copy per user), or null when the shader is not there (then the callers keep the old puffs).</summary>
        public static Material NewMaterial(string name)
        {
            if (!tried)
            {
                tried = true;
                var m = Resources.Load<Material>("OpenZ/FX/M_OzBlastFog");
                var sh = m && m.shader ? m.shader : Shader.Find("OpenZ/BlastFog");
                if (sh && sh.isSupported)
                {
                    shared = new Material(sh) { name = "OzBlastFog", renderQueue = 3007 };   // with the dust channel (3008), before the smoke
                    var atlas = Resources.Load<Texture2D>("OpenZ/FX/Smoke/OpenZ_SmokeAtlas");
                    var detail = Resources.Load<Texture2D>("OpenZ/FX/Smoke/OpenZ_SmokeDetail");
                    if (atlas && detail) { shared.SetTexture("_SmokeAtlas", atlas); shared.SetTexture("_SmokeDetail", detail); }
                    else { Debug.LogWarning("[OpenZ] blast fog: the smoke flipbook is missing: the old dust puffs"); shared = null; }
                }
                else Debug.LogWarning("[OpenZ] blast fog: shader OpenZ/BlastFog not found or not supported: the old dust puffs");
                LastShader = shared ? "OpenZ/BlastFog" : "missing (old puffs)";
            }
            return shared ? new Material(shared) { name = name } : null;
        }

        /// <summary>A particle system drawn with the fog material (code-placed or emitted), its vertex streams set.</summary>
        public static ParticleSystem MakeSystem(string name, Transform parent, Material m, bool emitted, out ParticleSystemRenderer r)
        {
            var go = new GameObject(name); go.SetActive(false);
            if (parent) go.transform.SetParent(parent, false);
            var p = go.AddComponent<ParticleSystem>(); r = go.GetComponent<ParticleSystemRenderer>();
            var main = p.main; main.playOnAwake = false; main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Capacity; main.startSpeed = 0f; main.startLifetime = emitted ? 3f : 1000f; main.startSize = 1f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; main.scalingMode = ParticleSystemScalingMode.Shape;
            var em = p.emission; em.enabled = false; var sh = p.shape; sh.enabled = false;
            if (emitted)
            {
                var col = p.colorOverLifetime; col.enabled = true;
                var g = new Gradient();
                var ak = new GradientAlphaKey[AlphaKeys.Length]; for (int i = 0; i < ak.Length; i++) ak[i] = new GradientAlphaKey(AlphaKeys[i].y, AlphaKeys[i].x);
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, ak);
                col.color = new ParticleSystem.MinMaxGradient(g);
                var sol = p.sizeOverLifetime; sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(GrowTo, new AnimationCurve(new Keyframe(0f, 1f / GrowTo, 0f, 0f), new Keyframe(0.35f, 0.72f), new Keyframe(1f, 1f, 0f, 0f)));
                var lv = p.limitVelocityOverLifetime; lv.enabled = true; lv.limit = 1000f; lv.dampen = 0f; lv.drag = Drag;
                lv.multiplyDragByParticleSize = false; lv.multiplyDragByParticleVelocity = false;
            }
            r.renderMode = ParticleSystemRenderMode.Billboard; r.alignment = ParticleSystemRenderSpace.View;
            r.sortMode = emitted ? ParticleSystemSortMode.Distance : ParticleSystemSortMode.None;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            r.minParticleSize = 0f; r.maxParticleSize = 6f; r.applyActiveColorSpace = true;
            r.sharedMaterial = m; r.SetActiveVertexStreams(Streams);
            go.SetActive(true); p.Play();
            return p;
        }

        static OzBlastFog Get()
        {
            if (!Enabled) return null;
            if (I) return I;
            var m = NewMaterial("OzBlastFog emitted"); if (!m) return null;
            var go = new GameObject("OZ_BlastFog");
            var f = go.AddComponent<OzBlastFog>();
            f.mat = m; f.ps = MakeSystem("OZ_BlastFog_Puffs", go.transform, m, true, out f.pr);
            I = f;
            return f;
        }

        void OnDestroy() { if (I == this) I = null; if (mat) Destroy(mat); }

        void LateUpdate() { if (ps) PeakAlive = Mathf.Max(PeakAlive, ps.particleCount); }

        static float GroundY(float x, float z, float fallback) => ZGame.I ? ZGame.I.GroundY(x, z) : fallback;

        void EmitOne(Vector3 p, Vector3 v, float life, float size, Color c, float alpha)
        {
            var ep = new ParticleSystem.EmitParams
            {
                position = p, velocity = v, startLifetime = life, startSize = size, startColor = new Color(c.r, c.g, c.b, Mathf.Clamp01(alpha)),
                rotation = Random.Range(0f, 360f), randomSeed = (uint)Random.Range(1, int.MaxValue), applyShapeToPosition = false,
            };
            ps.Emit(ep, 1);
        }

        /// <summary>One dust-wall puff on the shock front (OpenZFortShockwave.DustWall's place, velocity, size, colour and strength k);
        /// false = not handled (switch off / no shader): the caller draws its old puff.</summary>
        public static bool Wall(Vector3 p, Vector3 v, float size, Color c, float k)
        {
            var f = Get(); if (!f) return false;
            if (WallEvery > 1 && (wallSeq++ % WallEvery) != 0) return true;                       // handled: thinned, no old puff either
            if (f.ps.particleCount >= Capacity - SkirtReserve) { WallHeld++; return true; }          // the pool is nearly full: room kept for the skirt
            float gy = GroundY(p.x, p.z, p.y - 0.5f);
            var pos = new Vector3(p.x, gy + WallLift + Random.Range(-0.2f, 0.4f), p.z);
            var vel = new Vector3(v.x * WallSpeed, Mathf.Min(v.y, 0.6f) * 0.35f, v.z * WallSpeed);
            f.EmitOne(pos, vel, Random.Range(WallLifeMin, WallLifeMax), size * WallSize, c, (0.22f + 0.18f * k) * WallAlpha * 1.6f);
            WallPuffs++;
            return true;
        }

        /// <summary>One skirt ring (NukeFx.Skirt ring i of 10, radius r m) at the blast `at`; false = not handled (the old demolition dust).</summary>
        public static bool Skirt(Vector3 at, float r, int ring)
        {
            var f = Get(); if (!f) return false;
            var g = ZGame.I;
            Color c = g != null && g.level == "L17" ? new Color(0.62f, 0.57f, 0.52f) : OpenZFxCatalog.M12Dust.color;
            int n = SkirtPerRing + ring / 2;
            float phase = Random.Range(0f, Mathf.PI * 2f);
            for (int j = 0; j < n; j++)
            {
                float a = phase + j * Mathf.PI * 2f / n + Random.Range(-0.15f, 0.15f);
                float rr = r * Random.Range(0.75f, 1.05f);
                float x = at.x + Mathf.Cos(a) * rr, z = at.z + Mathf.Sin(a) * rr;
                var fx = OpenZFxPlayer.I; if (fx && fx.WetAt(new Vector3(x, 0f, z))) continue;      // no dust on water
                float gy = GroundY(x, z, at.y);
                var pos = new Vector3(x, gy + Random.Range(0.6f, 1.6f), z);
                var vel = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(1.2f, 2.4f) + Vector3.up * Random.Range(0.05f, 0.25f);
                float size = Random.Range(SkirtSizeMin, SkirtSizeMax) * (0.8f + 0.04f * ring);
                f.EmitOne(pos, vel, Random.Range(SkirtLifeMin, SkirtLifeMax), size, c, SkirtAlpha * Random.Range(0.8f, 1.1f));
                SkirtPuffs++;
            }
            return true;
        }

        /// <summary>Proof: puffs alive now and the biggest on screen (px) for camera c.</summary>
        public static string Status(Camera c)
        {
            var f = I; if (!f || !f.ps) return "blast fog: not built (" + LastShader + ")";
            var arr = new ParticleSystem.Particle[Capacity]; int n = f.ps.GetParticles(arr); float big = 0f;
            for (int i = 0; i < n && c; i++)
            {
                var v = c.WorldToScreenPoint(arr[i].position); if (v.z <= 0.1f) continue;
                float px = arr[i].GetCurrentSize(f.ps) / (2f * v.z * Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad)) * c.pixelHeight;
                if (px > big) big = px;
            }
            return $"blast fog: shader {LastShader}, alive {n} (peak {PeakAlive} of {Capacity}), wall puffs {WallPuffs} (held back for the skirt's room {WallHeld}), skirt puffs {SkirtPuffs}, biggest {big:0} px";
        }
    }
}
