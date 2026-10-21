using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Since the ground-matched dust (OzShuttleDownwash.Ground.cs: the look, the shader and why) the numbers below are its own; the
    /// round-3 text that follows keeps the rules (rings, burst, haze, deck, water, lifetimes).
    /// The playable shuttle's downwash on the ground (rule DV118, view only), driven by OzPlayableShuttle every frame: ONE soft rolling
    /// ring of dust that pours OUTWARD all round each of the two jets, in the ground's own colour, strong near the ground and gone by
    /// 11 m; a double ring of dust at the lift-off and at the touchdown with a faint low haze under it; a faint pressed patch left on the
    /// grass where it sat (never on a station deck); over water no dust but swells, spray and white mist. Drawn by its OWN particle
    /// system on a copy of the fort blast's soft fog material (OzBlastFog: the game's smoke flipbook, soft against the ground and the
    /// hull), so a fort blast never loses its fog to the shuttle and the other way round; the fort's own numbers are never changed.
    /// Round 3 (the second round's dust read as separate round tan discs with rims, "o2" again): many small, thin, spinning puffs that
    /// overlap into one cloud: born 1.4-2.6 m wide (spreading quickly at first), growing to 2.5 x as they thin, 0.28-0.38 at their peak, in over 0.15 s and out over
    /// the second half of their life, a softer radial edge and more billow erosion on this copy of the material. Every puff lives 2.4 s
    /// at most, so nothing is left lying on the ground: 2.5 s after the jets stop, the dust is gone.
    /// Strength k = 1 - smoothstep(3, 11, the ship's height) x the throttle's share: 220 x k puffs a second (x the graphics preset's
    /// share: HIGH 1, MEDIUM 0.75, LOW 0.5; the pool 520 x the share), each jet its own even ring 0.8-3.0 m round its ground point
    /// (all 360 degrees), thrown out at 6-9 m/s with 30 % of the ship's speed and 30 % of the wind, slowed by drag 2.5, rising 0.4-1.0
    /// m/s, spinning 20-40 degrees a second. The ring at the take-off / touchdown: 48 x the share puffs on two rings (1.5 m, 3.0 m) out at
    /// 6-9 m/s with its gust sound (shuttle_downwash_gust), and 6 x the share wide faint haze puffs (a 9 m cloud at 0.12): the crash
    /// landing (strength 2.2) throws the same puffs wider and faster. On a repair station's deck (the ship's base well over the ground)
    /// the dust comes off the deck plate itself, a third as much and shorter.
    /// The light (OzShuttleDownwash.Light.cs): the fog shader is lit by the sun and the sky only; this copy's light is raised so the dust
    /// is lit at least like the ground under it and never under a night floor (0.28 x NightK over its own light), and each puff takes the
    /// two thruster lights and the jets' warm ground pool at its place, so the dust at night reads light tan-grey in the jets' glow, never a
    /// black smoke ball on an orange ground. Round 2's tan discs were the 8 demolition-dust balls (the m12_dust channel, tan, opacity 0.6,
    /// 2.5 m round the middle, the north ones behind the hull: only the south arc showed) and its grey-green see-through disc the fog puffs
    /// tinted by the green grass: both are gone (the haze is these soft puffs; a planted ground gives the soil's tan).
    /// Nothing is emitted off screen or while paused.
    /// </summary>
    public sealed partial class OzShuttleDownwash
    {
        // ---- the rolling ring under the two jets
        public static float Rate = 90f, RingIn = 0.4f, RingOut = 1.8f, SpeedMin = 6f, SpeedMax = 8.5f, LifeMin = 1.8f, LifeMax = 2.4f, SizeMin = 2.8f, SizeMax = 4.2f;
        public static float AlphaMin = 0.27f, AlphaMax = 0.36f, Drag = 1.9f, FullBelowM = 3f, NoneAboveM = 11f, RiseMin = 0.1f, RiseMax = 0.35f;
        /// <summary>Spin (deg/s, either way), the size at the end of life (x the birth size), the size share reached at a fifth of the life
        /// (a quick first spread), the fade-in (s).</summary>
        public static float SpinMin = 10f, SpinMax = 20f, GrowTo = 2.3f, SizeEarly = 0.6f, FadeInSec = 0.45f;
        /// <summary>A puff is born BornLiftK x its width over the ground (+ up to BornLiftJitter m): the fog's quad faces the camera, and where
        /// the ground passes in front of a quad that sits too low the depth test cuts the puff along a hard line (the off-engine twin's count).</summary>
        public static float BornLiftK = 0.1f, BornLiftJitter = 0.25f;
        /// <summary>The ground's dust strength (0 roads / water .. 1 loose sand) scales a puff from this share to full.</summary>
        public static float HardGroundShare = 0.6f;
        // ---- the take-off / touchdown burst: two rings round the ship's middle
        public static int BurstPuffs = 24;
        public static float BurstRingA = 1.5f, BurstRingB = 3.0f, BurstJitter = 0.3f, BurstAlpha = 0.24f, BurstSpeedMin = 6f, BurstSpeedMax = 9f, BurstLifeMin = 1.8f, BurstLifeMax = 2.4f;
        /// <summary>The burst's low haze: the cloud's width (m) and its peak opacity, drawn with the same soft puffs (6 x the share).</summary>
        public static float RingCloudM = 12f, RingCloudS = 0.09f;
        public static int HazePuffs = 6;
        // ---- this system's copy of the fog material (the fort's own material is never touched)
        public static float EdgeSoft = 1.0f, DetailAmt = 0.40f, DetailScale = 1.2f, Dissolve = 0.9f;
        // ---- a station deck: a clean metal plate raises little dust
        public static float DeckAboveM = 0.5f, DeckShare = 0.35f, DeckLifeK = 0.75f, DeckAlphaK = 0.6f;
        /// <summary>The dust off a deck plate (sRGB): a light metal grey, not the ground's colour under the deck.</summary>
        public static Color DeckDust = new Color(0.66f, 0.64f, 0.60f);
        /// <summary>The ground's colour is pulled this far toward Pale; a green (planted) ground gives the soil's tan under it.</summary>
        public static float PaleK = 0.3f;
        public static Color Pale = new Color(0.78f, 0.74f, 0.66f), Soil = new Color(0.62f, 0.55f, 0.40f);
        public const int PoolMax = 520;
        /// <summary>No puff outlives this (s): its alpha is 0 at the end of a life of at most GoneBySec - 0.1.</summary>
        public const float GoneBySec = 2.5f;
        /// <summary>Proof: puffs emitted, the most alive at once, burst rings, fallback (m12_dust) puffs, water frames, pressed patches,
        /// haze puffs, frames over a station deck, pressed patches left out on a deck.</summary>
        public static int Puffs, PeakAlive, Bursts, FallbackPuffs, WaterFrames, Patches, HazeEmitted, DeckFrames, PatchesSkipped;

        static readonly List<OzShuttleDownwash> live = new List<OzShuttleDownwash>();
        static readonly int EdgeSoftId = Shader.PropertyToID("_EdgeSoft"), DetailAmtId = Shader.PropertyToID("_DetailAmt"), DetailScaleId = Shader.PropertyToID("_DetailScale"), DissolveId = Shader.PropertyToID("_Dissolve");

        readonly Transform root; readonly ShuttleRearDoor door;
        ParticleSystem ps; Material mat; float ripT, age, lastThrottle, lastFeet, baseY; bool haveBase;
        readonly float[] acc = new float[2]; readonly int[] seq = new int[2];
        ParticleSystem.Particle[] snap;
        public int Alive => ps ? ps.particleCount : 0;
        public bool OwnSystem => ps;
        /// <summary>This system's own copy of the fog material (null without the fog shader).</summary>
        public Material Mat => mat;

        public OzShuttleDownwash(Transform shipRoot, ShuttleRearDoor d)
        {
            root = shipRoot; door = d;
            if (OzBlastFog.Enabled) mat = MakeMaterial();                                                    // the ground-matched dust, else the fog shader's copy
            if (mat)
            {
                mat.SetFloat(EdgeSoftId, EdgeSoft); mat.SetFloat(DetailAmtId, DetailAmt); mat.SetFloat(DetailScaleId, DetailScale); mat.SetFloat(DissolveId, Dissolve);
                ps = OzBlastFog.MakeSystem("OZ_FxShuttleDownwash", null, mat, true, out var rend); SetUpRenderer(rend);
                var main = ps.main; main.maxParticles = Mathf.RoundToInt(PoolMax * Share);
                var lv = ps.limitVelocityOverLifetime; lv.drag = Drag;                                         // its own drag (the fort's stays)
                var col = ps.colorOverLifetime; col.color = new ParticleSystem.MinMaxGradient(AlphaOverLife());
                var sol = ps.sizeOverLifetime; sol.size = new ParticleSystem.MinMaxCurve(GrowTo, SizeOverLife());
            }
            live.Add(this);
        }

        public void Dispose() { live.Remove(this); if (ps) Object.Destroy(ps.gameObject); if (mat) Object.Destroy(mat); }

        /// <summary>The downwash of the ship whose root this is (the crash touchdown and the proofs throw their dust through it), or null.</summary>
        public static OzShuttleDownwash Of(Transform shipRoot)
        {
            for (int i = live.Count - 1; i >= 0; i--) if (live[i].root == shipRoot) return live[i];
            return null;
        }

        /// <summary>Opacity over life: in over FadeInSec (as a share of the mean life), held, out over the second half of the life.</summary>
        public static Gradient AlphaOverLife()
        {
            float fin = Mathf.Clamp(FadeInSec / (0.5f * (LifeMin + LifeMax)), 0.02f, 0.3f);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fin), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0.55f, 0.7f),
                              new GradientAlphaKey(0.2f, 0.85f), new GradientAlphaKey(0.05f, 0.95f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        /// <summary>Size over life (x GrowTo): 1 / GrowTo at birth, SizeEarly at a fifth of the life (a quick first spread), the full GrowTo at
        /// the end. Every tangent is given (a Keyframe without them is flat).</summary>
        public static AnimationCurve SizeOverLife()
        {
            float a = 1f / GrowTo, mid = (1f - a) / 1f;                                                       // the middle key's slope: its neighbours'
            return new AnimationCurve(new Keyframe(0f, a, 0f, 2.4f), new Keyframe(0.2f, SizeEarly, mid, mid), new Keyframe(1f, 1f, 0.3f, 0f));
        }
        /// <summary>The birth height over the ground for a puff of this width (m).</summary>
        static float Lift(float size) => BornLiftK * size + Random.Range(0f, BornLiftJitter);

        /// <summary>The graphics preset's particle share (HIGH 1, MEDIUM 0.75, LOW 0.5); ShareOverride >= 0 replaces it (the proof's LOW take-off).</summary>
        public static float ShareOverride = -1f;
        static float Share => ShareOverride >= 0f ? ShareOverride : OzQuality.Level == 0 ? 1f : OzQuality.Level == 1 ? 0.75f : 0.5f;
        public static float K(float feetM) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FullBelowM, NoneAboveM, feetM));
        /// <summary>The way a puff thrown at v m/s goes in t s with the drag (m).</summary>
        public static float Travel(float v, float t) => v / Mathf.Max(0.01f, Drag) * (1f - Mathf.Exp(-Drag * t));
        static float Spin() => Random.Range(SpinMin, SpinMax) * (Random.value < 0.5f ? -1f : 1f);

        /// <summary>True while the ship stands on (or comes down onto) a raised deck: its base is well over the ground under its middle.</summary>
        bool OnDeck(out float by)
        {
            var g = ZGame.I; var mid = root.position;
            float gy = g ? g.GroundY(mid.x, mid.z) : mid.y;
            by = haveBase ? baseY : gy;
            return haveBase && by - gy > DeckAboveM;
        }

        Vector3 JetGround(int side, out float gy, bool deck, float by)
        {
            var m = root.TransformPoint(door ? (side == 0 ? door.NozzleL : door.NozzleR) : new Vector3(side == 0 ? 2.45f : -2.45f, 2.26f, -0.2f));
            var a = m - root.up * 20f;                                                                       // straight down the jet's axis
            var g = ZGame.I; gy = deck ? by : g ? g.GroundY(m.x, m.z) : 0f;
            float t = Mathf.Abs(m.y - a.y) > 1e-3f ? Mathf.Clamp01((m.y - gy) / (m.y - a.y)) : 0f;
            var p = Vector3.Lerp(m, a, t); gy = deck ? by : g ? g.GroundY(p.x, p.z) : gy; p.y = gy;
            return p;
        }

        /// <summary>One frame: feetM = the ship's root over the ground (m), vel = its drawn velocity (m/s), throttle = the jets'.</summary>
        public void Step(float dt, float feetM, Vector3 vel, float throttle)
        {
            age += dt; lastThrottle = throttle; lastFeet = feetM;
            baseY = root.position.y - feetM; haveBase = true;                                               // the ground (or deck) the rules stand it on
            int alive = ps ? ps.particleCount : 0;
            if (ps) PeakAlive = Mathf.Max(PeakAlive, alive);
            float k = K(feetM) * Mathf.Clamp01(throttle / 0.7f);
            if (ps && (alive > 0 || k > 0.001f)) LightStep(throttle);                                           // the light of this frame (while there is dust)
            if (k <= 0.001f) return;
            var g = ZGame.I; var fx = OpenZFxPlayer.I;
            if (g && g.paused) return;
            var mid = root.position; if (!DetailKit.OnScreen(Camera.main, mid, 0.3f)) return;
            var wind = OzShuttleExhaust.Wind();
            float share = Share;
            bool deck = OnDeck(out float by); if (deck) DeckFrames++;
            for (int side = 0; side < 2; side++)
            {
                var jp = JetGround(side, out float gy, deck, by);
                bool wet = !deck && fx && fx.isActiveAndEnabled && fx.WetAt(jp);
                if (wet) { WaterFrames++; Water(dt, jp, k, fx); continue; }
                acc[side] += dt * Rate * 0.5f * k * share * (deck ? DeckShare : 1f);                          // each jet its own count and its own even ring
                while (acc[side] >= 1f) { acc[side] -= 1f; Puff(side, jp, gy, k, vel, wind, deck); }
            }
        }

        void Puff(int side, Vector3 jp, float gy, float k, Vector3 vel, Vector3 wind, bool deck)
        {
            float a = (seq[side]++ * 2.39996f + Random.Range(-0.2f, 0.2f)) % (Mathf.PI * 2f); var o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            float r = Random.Range(RingIn, RingOut), size = Random.Range(SizeMin, SizeMax);
            var p = new Vector3(jp.x + o.x * r, gy + Lift(size), jp.z + o.z * r);
            float sp = Random.Range(SpeedMin, SpeedMax) * (0.5f + 0.5f * k);
            var v = o * sp + Vector3.up * Random.Range(RiseMin, RiseMax) + new Vector3(vel.x, 0f, vel.z) * 0.3f + wind * 0.3f;
            float life = Random.Range(LifeMin, LifeMax) * (deck ? DeckLifeK : 1f);
            float strength = 1f; var c = deck ? DeckDust : GroundColour(p, out strength);
            float al = Random.Range(AlphaMin, AlphaMax) * k * Mathf.Lerp(HardGroundShare, 1f, strength) * (deck ? DeckAlphaK : 1f);
            Emit(p, v, life, size, Lit(c, p + o * Travel(sp, life * 0.4f)), al);
        }

        /// <summary>The ground's dust colour (sRGB): sand and dirt give their own colour pulled 30 % toward Pale; a planted (green) ground
        /// gives the soil's tan under it at the ground's own brightness; never a green, blue or near-black dust.</summary>
        public static Color GroundColour(Vector3 p, out float strength)
        {
            var tm = TrackMarks.I; Color c = OpenZFxCatalog.M12Dust.color; strength = 0.6f;
            if (tm && tm.Surface != null) { c = tm.Surface.DustColor(p.x, p.z); strength = tm.Surface.DustStrength(p.x, p.z); }
            return DustOf(c);
        }

        /// <summary>The dust a ground of colour c (sRGB) raises (pure numbers: the proof and the off-engine twin use the same rule).</summary>
        public static Color DustOf(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            float green = s < 0.12f ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.22f, h)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.48f, 0.56f, h)));
            var soil = Soil * Mathf.Clamp(v / 0.45f, 0.85f, 1.15f);
            var d = Color.Lerp(Color.Lerp(c, Pale, PaleK), Color.Lerp(soil, Pale, PaleK * 0.5f), green);
            Color.RGBToHSV(d, out h, out s, out v);
            float vv = Mathf.Clamp(v, 0.52f, 0.86f);
            if (green > 0.5f || (h > 0.15f && h < 0.6f && s > 0.12f)) h = Mathf.Lerp(h, 0.10f, 0.8f);         // never a green or a blue dust
            var o = Color.HSVToRGB(h, Mathf.Min(s, 0.45f), vv); o.a = 1f;
            return o;
        }

        void Emit(Vector3 p, Vector3 v, float life, float size, Color c, float alpha)
        {
            Puffs++;
            life = Mathf.Min(life, GoneBySec - 0.1f);
            if (ps)
            {
                var ep = new ParticleSystem.EmitParams
                {
                    position = p, velocity = v, startLifetime = life, startSize = size, startColor = new Color(c.r, c.g, c.b, Mathf.Clamp01(alpha)),
                    rotation = Random.Range(0f, 360f), angularVelocity = Spin(), randomSeed = (uint)Random.Range(1, int.MaxValue), applyShapeToPosition = false,
                };
                ps.Emit(ep, 1);
                return;
            }
            var fx = OpenZFxPlayer.I; if (!fx || !fx.isActiveAndEnabled) return;                             // no fog shader: the soft dust puffs
            fx.EmitChannel("m12_dust", p, v * 0.6f, life, size * 0.8f, c, Mathf.Clamp01(alpha * 1.3f)); FallbackPuffs++;
        }

        void Water(float dt, Vector3 jp, float k, OpenZFxPlayer fx)
        {
            if ((ripT -= dt) > 0f) return;
            ripT = 0.25f;
            float a = Random.Range(0f, Mathf.PI * 2f), r = Random.Range(2f, 6f);
            var at = jp + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            fx.Ripple(at, 0.35f + 0.35f * k);
            fx.Droplets(jp + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)), 0.6f + 0.4f * k, Mathf.RoundToInt(3 + 5 * k));
            var wind = OzShuttleExhaust.Wind(); var o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            fx.EmitChannel("m12_dust", jp + Vector3.up * 0.4f + o * 1.2f, o * (2.5f * k) + Vector3.up * 0.3f + wind * 0.3f, Random.Range(1.6f, GoneBySec - 0.1f), Random.Range(1.2f, 2.2f), Color.white, 0.2f * k);
        }

        /// <summary>The take-off / touchdown ring (strength 1; a crash landing 2.2): 48 x the share puffs on two rings round the ship's
        /// middle thrown out at 6-9 m/s (faster and wider with the strength), 6 x the share faint haze puffs under them, its gust.
        /// throttle: the jets' throttle that lights the puffs (below 0 = the jets are burning: at least the ignition's 0.7).</summary>
        public void Burst(float strength = 1f, float throttle = -1f)
        {
            var g = ZGame.I; var c0 = root.position;
            if (g && g.paused) return;
            Bursts++;
            if (AudioDirector.Has("shuttle_downwash_gust")) AudioDirector.Fire("shuttle_downwash_gust", c0, -1, Mathf.Clamp01(0.7f + 0.3f * strength));
            if (!DetailKit.OnScreen(Camera.main, c0, 0.3f)) return;
            lastThrottle = throttle < 0f ? Mathf.Max(lastThrottle, 0.7f) : Mathf.Clamp01(throttle);                // the jets light at the ignition / still burn at the touchdown
            if (ps) LightStep(lastThrottle);
            bool deck = OnDeck(out float by);
            float share = Share * (deck ? DeckShare : 1f);
            int n = Mathf.Max(2, Mathf.RoundToInt(BurstPuffs * share)); int half = (n + 1) / 2;
            float sq = Mathf.Sqrt(Mathf.Max(0.1f, strength)), phase = Random.Range(0f, Mathf.PI * 2f);
            for (int i = 0; i < n; i++)
            {
                bool outer = (i & 1) == 1; int j = i >> 1;
                float a = phase + (j + (outer ? 0.5f : 0f)) / half * Mathf.PI * 2f + Random.Range(-BurstJitter, BurstJitter);
                var o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var p = c0 + o * ((outer ? BurstRingB : BurstRingA) * Mathf.Min(1.4f, sq));
                RingPuff(p, o, Random.Range(BurstSpeedMin, BurstSpeedMax) * Mathf.Min(1.6f, sq), Random.Range(BurstLifeMin, BurstLifeMax), Random.Range(SizeMin, SizeMax) * sq * BurstSizeK,
                         BurstAlpha * Mathf.Min(1f, strength), deck, by);
            }
            Haze(c0, strength, share, deck, by);
        }

        /// <summary>A ring of the same soft puffs round any point (a pad's slam, a skid: the crash touchdown calls it), at that point's
        /// ground: count x the share puffs radiusM out, thrown outward at about 6 m/s x strength. Returns the puffs drawn.</summary>
        public int Ring(Vector3 at, float radiusM, int count, float strength)
        {
            var g = ZGame.I; if (g && g.paused) return 0;
            if (!DetailKit.OnScreen(Camera.main, at, 0.3f)) return 0;
            if (ps) LightStep(Mathf.Max(lastThrottle, 0.7f));
            int n = Mathf.Max(1, Mathf.RoundToInt(count * Share)), made = 0; float sq = Mathf.Sqrt(Mathf.Max(0.1f, strength)), phase = Random.Range(0f, Mathf.PI * 2f);
            for (int i = 0; i < n; i++)
            {
                float a = phase + i / (float)n * Mathf.PI * 2f + Random.Range(-BurstJitter, BurstJitter); var o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var p = at + o * radiusM; p.y = g ? g.GroundY(p.x, p.z) : at.y;
                if (RingPuff(p, o, Random.Range(BurstSpeedMin, BurstSpeedMax) * 0.8f * Mathf.Min(1.6f, sq), Random.Range(BurstLifeMin, BurstLifeMax), Random.Range(SizeMin, SizeMax) * Mathf.Min(1.4f, sq),
                             BurstAlpha * Mathf.Min(1f, strength), false, 0f)) made++;
            }
            return made;
        }

        /// <summary>A short low streak of the same puffs thrown along dir from at (a skid's outward ground streak). Returns the puffs drawn.</summary>
        public int Streak(Vector3 at, Vector3 dir, float k)
        {
            var g = ZGame.I; if (g && g.paused) return 0;
            if (!DetailKit.OnScreen(Camera.main, at, 0.3f)) return 0;
            dir.y = 0f; if (dir.sqrMagnitude < 1e-4f) return 0; dir.Normalize();
            int made = 0;
            for (int i = 0; i < 4; i++)
            {
                var p = at + dir * (0.6f * i); p.y = g ? g.GroundY(p.x, p.z) : at.y;
                if (RingPuff(p, dir, (5f + 1.5f * i) * Mathf.Clamp(k, 0.3f, 1.6f), Random.Range(1.2f, 1.8f), Random.Range(0.9f, 1.4f), BurstAlpha * 0.8f * Mathf.Clamp01(k), false, 0f)) made++;
            }
            return made;
        }

        bool RingPuff(Vector3 p, Vector3 o, float speed, float life, float size, float alpha, bool deck, float by)
        {
            var g = ZGame.I; var fx = OpenZFxPlayer.I;
            float gy = deck ? by : g ? g.GroundY(p.x, p.z) : p.y; p.y = gy + Lift(size);
            if (!deck && fx && fx.isActiveAndEnabled && fx.WetAt(p)) { fx.Ripple(p, 0.6f); return false; }
            float st = 1f; var col = deck ? DeckDust : GroundColour(p, out st);
            life *= deck ? DeckLifeK : 1f;
            Emit(p, o * speed + Vector3.up * Random.Range(RiseMin, RiseMax), life, size, Lit(col, p + o * Travel(speed, life * 0.4f)),
                 alpha * Mathf.Lerp(HardGroundShare, 1f, st) * (deck ? DeckAlphaK : 1f));
            return true;
        }

        void Haze(Vector3 c0, float strength, float share, bool deck, float by)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(HazePuffs * share)); float sq = Mathf.Sqrt(Mathf.Max(0.1f, strength)), phase = Random.Range(0f, Mathf.PI * 2f);
            for (int i = 0; i < n; i++)
            {
                float a = phase + i / (float)n * Mathf.PI * 2f + Random.Range(-0.25f, 0.25f); var o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var p = c0 + o * (RingCloudM * 0.22f);
                if (RingPuff(p, o, 1.6f * Mathf.Min(1.6f, sq), Random.Range(GoneBySec - 0.5f, GoneBySec - 0.1f), RingCloudM * 0.25f * Mathf.Min(1.4f, sq), RingCloudS * Mathf.Min(1f, strength), deck, by)) HazeEmitted++;
            }
        }

        /// <summary>At each landing: pressed, drier grass that stays where it sat and fades (a ground mark; not on water, not on a deck).</summary>
        public void Pressed()
        {
            var fx = OpenZFxPlayer.I; var c = root.position; var g = ZGame.I; if (g) c.y = g.GroundY(c.x, c.z);
            if (fx && fx.isActiveAndEnabled && fx.WetAt(c)) return;
            if (root.position.y - c.y > DeckAboveM) { PatchesSkipped++; return; }                              // on a station's deck: no mark on the ground beside it
            if (ScorchDecals.AddSand(c, 3.6f, 45f, 0.22f)) Patches++;
        }

        /// <summary>Proof: every puff alive now (its place, its size now, its opacity now, its whole life); returns how many.</summary>
        public int Snapshot(List<Vector3> pos, List<float> size, List<float> alpha, List<float> life)
        {
            pos?.Clear(); size?.Clear(); alpha?.Clear(); life?.Clear();
            if (!ps) return 0;
            if (snap == null || snap.Length < ps.main.maxParticles) snap = new ParticleSystem.Particle[ps.main.maxParticles];
            int n = ps.GetParticles(snap);
            for (int i = 0; i < n; i++)
            {
                pos?.Add(snap[i].position); size?.Add(snap[i].GetCurrentSize(ps)); alpha?.Add(snap[i].GetCurrentColor(ps).a / 255f); life?.Add(snap[i].startLifetime);
            }
            return n;
        }
    }
}
