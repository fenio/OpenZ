using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// Real detail at the mushroom's size, wrapped around NukeFx's glowing cap and stem meshes (they stay as the
    /// a copy of the blast_smoke channel material: sun + point lights, self shadow, detail erosion, soft particles), placed and
    /// moved procedurally every frame on the same timeline as NukeFx (rise 27 m, cap radius 10 m, x1.4 fort scale):
    /// <list type="bullet">
    /// <item><b>Crown</b>: two layered shells of puffs on the dome (outer + darker inner layer for depth), each boiling (radial pulse).</item>
    /// <item><b>Toroidal vortex</b>: puffs on a torus around the cap edge travelling around the tube (up the inside, out over the
    /// top, down the outside, back in underneath: the cap rolls inward), hottest underneath.</item>
    /// <item><b>Stem</b>: five rising columns that twist up into the cap, wider at the base, plus small dark debris swirling up.</item>
    /// <item><b>Skirt</b>: a pale condensation ring on the stem (1.5-9 s). <b>Base surge</b>: tan dust rolling out over the ground.</item>
    /// </list>
    /// Colour: fiery (birth heat glow, per puff temperature = the mushroom's heat x the puff's place: underside / core hotter) cooling
    /// to dark grey-brown over ~6 s; lit by the NukeFx fire light from inside and the sun outside; a cloud-scale self shadow per puff
    /// (away from the sun / underneath darker). Two particle systems so the puffs on the far side draw BEFORE the glowing mesh and
    /// the near side AFTER it (sorted back to front on the CPU). Lives Life s (longer than NukeFx's 8.5 s), thinning into wisps.
    /// </summary>
    public sealed partial class OpenZNukeMushroom : MonoBehaviour
    {
        public static bool Enabled = true;
        public static float Life = 14f, PuffScale = 1.4f, AlphaScale = 0.85f,
             GlowTau = 3f, HeatGain = 1f, FrontFadeIn = 1.0f;
        /// <summary>Linear HDR heat glow of the hottest puffs (blast_smoke's is (1.1, .36, .06)).</summary>
        public static Vector4 HeatColor = new Vector4(4.2f, 1.0f, 0.14f, 1f);
        public static int CrownCount = 60, InnerCount = 30, TorusCount = 80, StemCount = 66, DebrisCount = 8, SkirtCount = 34, DustCount = 64;
        /// <summary>Smoke albedo (sRGB) while the fire burns (dark warm brown: the orange fire light + bloom washed a paler one to salmon) and cooled.</summary>
        public static Color HotSmoke = new Color(0.44f, 0.31f, 0.23f), CoolSmoke = new Color(0.6f, 0.56f, 0.52f);
        /// <summary>
        /// outside shade (darker rolling ring) and the stem's extra heat.</summary>
        public static Color StemBright = new Color(0.93f, 0.8f, 0.62f), UnderLit = new Color(0.86f, 0.42f, 0.16f);
        public static float RingDark = 0.58f, StemHeat = 1.25f;
        /// <summary>The whole cloud times this about its foot (rise and cap radius; the stem, the ring and every puff follow them). 1 = the
        /// game's own. Only the level 16 ending scene sets it, at its cut to the loungers (ZodBriefing.EndingMushroomScale); every new cloud starts at 1.</summary>
        public static float SceneScale = 1f;
        public static OpenZNukeMushroom Current { get; private set; }
        public int PuffCount => puffs != null ? puffs.Length : 0;
        public float Age => t;
        public int FrontLive { get; private set; }
        public int BackLive { get; private set; }
        /// <summary>Check: the mean and the highest speed (m/s) of the puffs this frame (what the puff shader's motion blur follows).</summary>
        public float PuffSpeedMean { get; private set; }
        public float PuffSpeedMax { get; private set; }

        enum Kind { Crown, Inner, Torus, Stem, Debris, Skirt, Dust }
        struct Puff { public Kind k; public float a, b, c, size, rnd, heat, alpha; public uint seed; }
        Puff[] puffs;
        ParticleSystem front, back; ParticleSystemRenderer frontR, backR; Material frontMat, backMat;
        ParticleSystem.Particle[] fArr, bArr; float[] fKey, bKey;
        Vector3 origin; float t, groundY; Color dust;
        Vector3[] prevPos, lastVel; bool[] seen;                              // each puff's last place and its velocity from it (m/s)

        static readonly List<ParticleSystemVertexStream> Streams = new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV, ParticleSystemVertexStream.AgePercent, ParticleSystemVertexStream.StableRandomX,
            ParticleSystemVertexStream.Velocity, ParticleSystemVertexStream.SizeX, ParticleSystemVertexStream.Center,   // the puff shader's motion blur: TEXCOORD1 = velocity xyz + size, TEXCOORD2.xyz = centre (the same slots as the effect player's channels)
        };

        public static OpenZNukeMushroom Spawn(Vector3 at, float groundY)
        {
            if (!Enabled) return null;
            var fxp = OpenZFxPlayer.I;
            var mat = fxp ? fxp.CloneChannelMaterial("blast_smoke") : null;
            if (!mat) { Debug.LogWarning("[OpenZ] nuke mushroom: no blast_smoke channel material (OpenZFxPlayer): the mesh mushroom only"); return null; }
            SceneScale = 1f;
            if (Current) Destroy(Current.gameObject);
            var go = new GameObject("NukeMushroom");
            var m = go.AddComponent<OpenZNukeMushroom>();
            m.origin = new Vector3(at.x, groundY, at.z); m.groundY = groundY;
            m.Build(mat);
            Current = m;
            return m;
        }

        void Build(Material mat)
        {
            var g = ZGame.I;
            dust = g != null && g.level == "L17" ? new Color(0.6f, 0.56f, 0.52f) : OpenZFxCatalog.M12Dust.color;
            backMat = mat; frontMat = new Material(mat) { name = "NukeMushroom front" }; backMat.name = "NukeMushroom back";
            foreach (var m in new[] { backMat, frontMat })
            {
                m.SetFloat("_HeatTime", 0.62f); m.SetVector("_AgeColor", new Vector4(0.74f, 0.7f, 0.66f, 1f));
                m.SetFloat("_SoftFade", 2.6f); m.SetFloat("_Spin", 0.35f); m.SetFloat("_Boil", 0.55f); m.SetFloat("_Lit", 1f);
                m.SetFloat("_Wrap", 0.32f); m.SetFloat("_SelfShadow", 0.85f); m.SetFloat("_FlipRate", 1f);
                m.SetFloat("_Smear", Smear /* item 210: 0.42 s, OpenZNukeMushroom.Kd.cs */); m.SetFloat("_SmearPath", 2f);   // MOTION BLUR option: each puff is blurred along its own path (the copy came with _Smear 0); 2 = no fade-in at birth, a puff's age here is its heat
            }
            backMat.renderQueue = 3006;   // before the NukeFx meshes (OpenZ/Nuke, Transparent+10 = 3010)
            frontMat.renderQueue = 3021;  // after them, before the additive glow layers (3100)
            back = MakeSystem("NukeMushroom_Back", backMat, out backR);
            front = MakeSystem("NukeMushroom_Front", frontMat, out frontR);
            KdBuild();                                                                    // item 211: the base surge as ground fog (OpenZNukeMushroom.Kd.cs)
            var list = new List<Puff>();
            uint seed = (uint)(Mathf.Abs(origin.x * 13.1f + origin.z * 7.7f) * 10f) + 1u;
            System.Func<float> R = () => { seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5; return (seed & 0xFFFFFF) / 16777216f; };
            for (int i = 0; i < CrownCount; i++)
            {   // golden spiral over the dome: a = azimuth, b = polar (0 top .. 1 the torus top)
                float f = (i + 0.5f) / CrownCount;
                list.Add(new Puff { k = Kind.Crown, a = i * 2.39996f + R() * 0.3f, b = Mathf.Sqrt(f) * 0.92f, c = R(), size = Mathf.Lerp(0.16f, 0.6f, Mathf.Clamp01(R() * R() + 0.3f * R())), rnd = R(), heat = Mathf.Lerp(0.55f, 0.85f, R()), alpha = 0.9f });
            }
            for (int i = 0; i < InnerCount; i++)
                list.Add(new Puff { k = Kind.Inner, a = i * 2.39996f + 1.1f, b = Mathf.Sqrt((i + 0.5f) / InnerCount) * 0.8f, c = R(), size = Mathf.Lerp(0.42f, 0.55f, R()), rnd = R(), heat = Mathf.Lerp(0.9f, 1.15f, R()), alpha = 0.95f });
            for (int i = 0; i < TorusCount; i++)
                list.Add(new Puff { k = Kind.Torus, a = i * 2.39996f, b = R() * 6.2831853f, c = R(), size = Mathf.Lerp(0.42f, 1.55f, R() * 0.7f + 0.3f * R() * R()), rnd = R(), heat = 1f, alpha = 0.94f });
            for (int i = 0; i < StemCount; i++)
                list.Add(new Puff { k = Kind.Stem, a = (i % 5) * 1.2566f + (R() - 0.5f) * 1.1f, b = R(), c = R(), size = Mathf.Lerp(0.5f, 1.8f, R()), rnd = R(), heat = Mathf.Lerp(0.85f, 1.2f, R()), alpha = 0.85f });
            for (int i = 0; i < DebrisCount; i++)
                list.Add(new Puff { k = Kind.Debris, a = R() * 6.2831853f, b = R(), c = R(), size = Mathf.Lerp(0.6f, 1.1f, R()), rnd = R(), heat = 0.4f, alpha = 0.8f });
            for (int i = 0; i < SkirtCount; i++)
                list.Add(new Puff { k = Kind.Skirt, a = (i + R() * 0.4f) * 6.2831853f / SkirtCount, b = R(), c = R(), size = Mathf.Lerp(0.85f, 1.2f, R()), rnd = R(), heat = 0f, alpha = 0.42f });
            for (int i = 0; i < DustCount; i++)
                list.Add(new Puff { k = Kind.Dust, a = (i + (R() - 0.5f) * 1.9f) * 6.2831853f / DustCount, b = R(), c = R(), size = Mathf.Lerp(0.5f, 1.65f, R() * R() + 0.3f * R()), rnd = R(), heat = 0f, alpha = Mathf.Lerp(0.4f, 0.62f, R()) });
            puffs = list.ToArray();
            for (int i = 0; i < puffs.Length; i++) puffs[i].seed = (uint)(i * 2654435761u + 12345u);
            fArr = new ParticleSystem.Particle[puffs.Length]; bArr = new ParticleSystem.Particle[puffs.Length];
            fKey = new float[puffs.Length]; bKey = new float[puffs.Length];
            prevPos = new Vector3[puffs.Length]; lastVel = new Vector3[puffs.Length]; seen = new bool[puffs.Length];
        }

        ParticleSystem MakeSystem(string name, Material mat, out ParticleSystemRenderer r)
        {
            var go = new GameObject(name); go.SetActive(false); go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>(); r = go.GetComponent<ParticleSystemRenderer>();
            var main = ps.main; main.playOnAwake = false; main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1024; main.startSpeed = 0f; main.startLifetime = 1000f; main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var em = ps.emission; em.enabled = false; var sh = ps.shape; sh.enabled = false;
            r.renderMode = ParticleSystemRenderMode.Billboard; r.alignment = ParticleSystemRenderSpace.View; r.sortMode = ParticleSystemSortMode.None;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.minParticleSize = 0f; r.maxParticleSize = 6f; r.applyActiveColorSpace = true;
            r.sharedMaterial = mat; r.SetActiveVertexStreams(Streams);
            go.SetActive(true); ps.Play();                                  // playing (bounds update); lifetimes 1000 s, reset every frame
            return ps;
        }

        // NukeFx timeline (rise / expand / cap base / stem radius), continued slowly after its 8.5 s
        static float P(float t) => Mathf.Min(t / NukeFx.Duration, 1f);
        static float Rise(float t) { float p = P(t); return (1f - (1f - p) * (1f - p)) * 27f + Mathf.Max(0f, t - NukeFx.Duration) * 0.6f; }
        static float CapR(float t) { float p = P(t); return 10f * (1f - Mathf.Pow(1f - p, 4f)) + Mathf.Max(0f, t - NukeFx.Duration) * 0.35f; }

        void LateUpdate()
        {
            t += Time.deltaTime;
            if (t >= Life || puffs == null) { Destroy(gameObject); return; }
            var cam = Camera.main; if (!cam) return;
            Vector3 camPos = cam.transform.position, up = Vector3.up;
            var sun = RenderSettings.sun; Vector3 toSun = sun ? -sun.transform.forward : new Vector3(0.3f, 0.8f, 0.2f).normalized;
            float night = NightGrade.NightK;
            float capR = Mathf.Max(0.6f, CapR(t)) * SceneScale, rise = Mathf.Max(1.2f, Rise(t)) * SceneScale;
            float torR = capR * 0.62f, torA = capR * 0.40f;
            Vector3 capC = origin + up * rise;                                                        // the cap centre (NukeFx cap position)
            float stemR = capR * 0.24f, stemTop = rise - torA * 0.6f;
            float tg = Mathf.Exp(-t / GlowTau) * HeatGain;                                           // the mushroom's heat
            float endFade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Life - 4.5f, Life, t));
            float dissolve = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Life - 5f, Life, t));
            float frontIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, FrontFadeIn, t));
            Color hotSmoke = HotSmoke, coolSmoke = CoolSmoke;
            Color smoke = Color.Lerp(coolSmoke, hotSmoke, Mathf.Clamp01(tg * 1.4f));
            float spinBase = t * 0.05f;
            int nf = 0, nb = 0;
            float dtv = Time.deltaTime, spdSum = 0f, spdMax = 0f; bool blurOn = OzFxSmooth.On;
            for (int i = 0; i < puffs.Length; i++)
            {
                var q = puffs[i];
                Vector3 pos; Vector3 axisPt; float size, temp, alpha = q.alpha, shadeK = 1f; Color col = smoke; bool fireLit = true;
                switch (q.k)
                {
                    case Kind.Crown:
                    case Kind.Inner:
                    {
                        float layer = q.k == Kind.Inner ? 0.72f : 1f;
                        float boil = 1f + 0.06f * Mathf.Sin(t * (1.1f + q.rnd) + q.c * 6.28f);
                        float ang = q.a + spinBase * (0.6f + q.rnd * 0.8f), pol = KdCrownB(q, ref alpha) * 1.45f;     // 0 top .. ~83 deg
                        float rx = (torR + torA * 0.55f) * layer * boil, ry = capR * 0.5f * layer * boil;
                        axisPt = capC + up * (capR * 0.06f);
                        pos = axisPt + new Vector3(Mathf.Sin(pol) * Mathf.Cos(ang) * rx, Mathf.Cos(pol) * ry, Mathf.Sin(pol) * Mathf.Sin(ang) * rx);
                        size = capR * q.size * PuffScale;
                        temp = q.heat * (q.k == Kind.Inner ? 1.1f : 0.8f);
                        if (q.k == Kind.Inner) shadeK = 0.78f;
                        break;
                    }
                    case Kind.Torus:
                    {   // the vortex: tube angle decreases = up the inside, out over the top, down the outside, back in underneath
                        float ang = q.a + spinBase * 0.7f, psi = q.b - t * 0.55f;
                        Vector3 dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                        axisPt = capC - up * (capR * 0.05f) + dir * torR;
                        pos = axisPt + dir * (torA * Mathf.Cos(psi)) + up * (torA * 0.82f * Mathf.Sin(psi));
                        size = torA * q.size * PuffScale;
                        float under = Mathf.Clamp01(-Mathf.Sin(psi));
                        temp = 0.65f + 0.6f * under + 0.15f * Mathf.Clamp01(-Mathf.Cos(psi));                // hottest underneath / inside
                        // dark and sooty, its underside stays lit orange by the fire below (warm albedo + extra heat there)
                        shadeK = Mathf.Lerp(RingDark, 1f, under);
                        col = Color.Lerp(smoke, UnderLit, under * 0.75f);
                        temp += 0.35f * under;
                        break;
                    }
                    case Kind.Stem:
                    {   // rising columns twisting up into the cap, wider at the base; wrap with a fade at both ends
                        float h = Mathf.Repeat(q.b + t * (0.07f + 0.04f * q.rnd), 1f);
                        float H = Mathf.Max(1f, stemTop - 0.5f);
                        float ang = q.a + t * (0.3f + 0.25f * q.rnd) + h * 2.2f;
                        float rs = stemR * (0.45f + 0.5f * q.c) * (1f + 0.9f * (1f - h) * (1f - h));
                        axisPt = origin + up * (0.6f + h * H);
                        pos = axisPt + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * rs;
                        size = stemR * q.size * PuffScale * (1f + 0.6f * (1f - h));
                        temp = q.heat * (0.8f + 0.5f * (1f - h)) * StemHeat;
                        col = Color.Lerp(smoke, StemBright, 0.6f); shadeK = 1.12f;
                        alpha *= Mathf.SmoothStep(0f, 1f, h / 0.1f) * (1f - Mathf.SmoothStep(0f, 1f, (h - 0.86f) / 0.14f)) * Mathf.Clamp01((t - 0.25f) / 0.6f);
                        break;
                    }
                    case Kind.Debris:
                    {   // small dark puffs spiralling up around the base
                        float h = Mathf.Repeat(q.b + t * 0.13f, 1f);
                        float ang = q.a + t * (0.8f + 0.4f * q.rnd);
                        float rr = stemR * (1.6f + 1.6f * q.c) * (1f - 0.4f * h);
                        axisPt = origin + up * (0.8f + h * Mathf.Min(10f, stemTop * 0.4f));
                        pos = axisPt + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * rr;
                        size = Mathf.Max(0.8f, stemR * 0.55f) * q.size * PuffScale;
                        temp = 0.2f; col = Color.Lerp(dust, coolSmoke, 0.5f);
                        alpha *= Mathf.SmoothStep(0f, 1f, h / 0.15f) * (1f - Mathf.SmoothStep(0f, 1f, (h - 0.75f) / 0.25f)) * Mathf.Clamp01((t - 0.4f) / 0.5f);
                        break;
                    }
                    case Kind.Skirt:
                    {   // condensation ring around the stem
                        float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.5f, 6f, t));
                        float rr = stemR * (1.9f + 1.6f * k) * (0.92f + 0.16f * q.c);
                        axisPt = origin + up * (stemTop * 0.56f + (q.b - 0.5f) * 0.8f);
                        pos = axisPt + new Vector3(Mathf.Cos(q.a + t * 0.04f), 0f, Mathf.Sin(q.a + t * 0.04f)) * rr;
                        size = Mathf.Max(1.2f, stemR * 0.95f) * q.size * PuffScale;
                        temp = 0f; col = new Color(0.86f, 0.85f, 0.83f); fireLit = false;
                        alpha *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.3f, 2.4f, t)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(6.5f, 9.5f, t)));
                        break;
                    }
                    default:
                    {   // base surge: tan dust rolling outward over the ground
                        // own pace and reach (0.55-1.3 x, tau 1.5-3.4 s), drifts sideways a little, so the surge breaks into lumps, never a ring
                        float rr = (4f + 25f * (1f - Mathf.Exp(-t / (1.5f + 1.9f * q.rnd)))) * (0.55f + 0.75f * q.c);
                        float ang = q.a + t * (0.02f + 0.05f * (q.rnd - 0.5f));
                        float x = origin.x + Mathf.Cos(ang) * rr, z = origin.z + Mathf.Sin(ang) * rr;
                        float gy = ZGame.I ? ZGame.I.GroundY(x, z) : groundY;
                        axisPt = new Vector3(origin.x, gy, origin.z);
                        pos = new Vector3(x, gy + 0.9f + 3.6f * q.b * q.b * Mathf.Clamp01(t / 2f), z);
                        size = (4.5f + 3.5f * Mathf.Clamp01(t / (3.5f + 3f * q.c))) * q.size * PuffScale;
                        temp = 0f; col = dust; fireLit = false;
                        alpha *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.7f, t));
                        break;
                    }
                }
                if (q.k == Kind.Crown || q.k == Kind.Inner || q.k == Kind.Torus) pos += KdChurn(q, size, i);   // item 210: the cap rolls (KdCrownB above) and each puff churns: real motion the blur follows
                // the puff's own velocity from its last place (m/s): the puff shader blurs it along that path (MOTION BLUR option). A puff
                // that jumps (a stem puff wrapping from the top to the base, faded out there) gets none; a frozen frame keeps the last one
                if (dtv > 1e-5f)
                {
                    var mv = seen[i] ? (pos - prevPos[i]) / dtv : Vector3.zero;
                    lastVel[i] = mv.sqrMagnitude < 1600f ? mv : Vector3.zero;
                }
                prevPos[i] = pos; seen[i] = true;
                float spd = lastVel[i].magnitude; spdSum += spd; if (spd > spdMax) spdMax = spd;
                // cloud-scale self shadow: away from the sun and underneath darker (the shader adds the per-puff terminator)
                Vector3 nrm = pos - axisPt; nrm = nrm.sqrMagnitude > 1e-4f ? nrm.normalized : up;
                float sunK = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.65f, 0.75f, Vector3.Dot(nrm, toSun)));
                float underK = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.7f, 0.25f, nrm.y));
                float shade = Mathf.Lerp(0.42f, 1f, sunK) * Mathf.Lerp(0.72f, 1f, underK) * shadeK;
                if (!fireLit) shade = Mathf.Lerp(0.7f, 1f, sunK);
                float tau = Mathf.Clamp01(tg * temp * (0.75f + 0.5f * q.rnd));
                float age = Mathf.Lerp((1f - tau) * 0.62f, 1f, dissolve);                                // age drives the heat glow, flipbook frame, erosion
                alpha *= endFade * AlphaScale;
                if (alpha < 0.01f || size < 0.05f) continue;
                bool isFront = Vector3.Dot(pos - axisPt, camPos - axisPt) > 0f || q.k == Kind.Dust;
                if (isFront && q.k != Kind.Dust && q.k != Kind.Skirt) alpha *= Mathf.Lerp(0.25f, 1f, frontIn);
                float tone = 0.88f + 0.24f * q.c, warm = (q.rnd - 0.5f) * 0.08f;
                var c = new Color(col.r * shade * tone * (1f + warm), col.g * shade * tone, col.b * shade * tone * (1f - warm), Mathf.Clamp01(alpha));
                var pt = new ParticleSystem.Particle
                {
                    position = pos, velocity = blurOn ? lastVel[i] : Vector3.zero, startSize = size, startLifetime = 1000f, remainingLifetime = Mathf.Max(0.01f, 1000f * (1f - Mathf.Clamp01(age))),
                    rotation = q.rnd * 360f + t * (q.rnd - 0.5f) * 14f, angularVelocity = 0f, randomSeed = q.seed, startColor = (Color32)c,
                };
                float key = -(pos - camPos).sqrMagnitude;                                                 // back to front
                if (q.k == Kind.Dust && KdToFog(pt)) { } else if (isFront) { fArr[nf] = pt; fKey[nf] = key; nf++; } else { bArr[nb] = pt; bKey[nb] = key; nb++; }
            }
            System.Array.Sort(fKey, fArr, 0, nf); System.Array.Sort(bKey, bArr, 0, nb);
            front.SetParticles(fArr, nf); back.SetParticles(bArr, nb);
            KdFlush();                                                                    // item 211: this frame's base surge as fog
            FrontLive = nf; BackLive = nb;
            PuffSpeedMean = puffs.Length > 0 ? spdSum / puffs.Length : 0f; PuffSpeedMax = spdMax;
            var heat = HeatColor * Mathf.Lerp(1f, NukeFx.NightGainMul, night);
            frontMat.SetVector("_HeatColor", heat); backMat.SetVector("_HeatColor", heat * 0.8f);
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            if (frontMat) Destroy(frontMat); if (backMat) Destroy(backMat); KdDestroy();
        }

        /// <summary>Proof: puff counts, size range and the biggest puff on screen (px) for camera c.</summary>
        public string Status(Camera c)
        {
            float big = 0f;
            if (c && puffs != null)
            {
                var arr = new ParticleSystem.Particle[1024];
                foreach (var ps in new[] { front, back })
                {
                    int n = ps.GetParticles(arr);
                    for (int i = 0; i < n; i++)
                    {
                        var v = c.WorldToScreenPoint(arr[i].position); if (v.z <= 0.1f) continue;
                        float px = arr[i].startSize / (2f * v.z * Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad)) * c.pixelHeight;
                        if (px > big) big = px;
                    }
                }
            }
            return "mushroom t " + t.ToString("F1") + " s, puffs " + PuffCount + " (front " + FrontLive + ", back " + BackLive + "), cap R " + CapR(t).ToString("F1") + " m, rise " + Rise(t).ToString("F1") +
                   " m, heat " + Mathf.Exp(-t / GlowTau).ToString("F2") + ", biggest puff " + big.ToString("F0") + " px";
        }
    }
}
