using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary></summary>
    public enum Exp2Kind { None = 0, Small = 1, Medium = 2, Big = 3, Salvo = 4, BuildingHit = 5 }

    /// <summary>
    /// blown-out white core at 40-50 % of its peak inside an orange body at 70 %+ with a red halo, no pre-flash, no fade-in; the
    /// core keeps growing 200-600 ms, holds, drops sharply; the fireball NEVER rises (bottom pivot, it shrinks toward its base)
    /// and cools white, gold, saturated orange, red-orange, crimson; hundreds of long velocity-stretched sparks with yellow-white
    /// heads and red bodies, light drag + gravity, sliding on the ground, outliving the fireball by ~1 s; a strong orange point
    /// light for ~1-2 s; one small camera kick through the existing gated shake. Per-variant layer timelines are keyed in real ms
    /// (spec sections 3, 4, 5, 6, 7) and baked into each channel's size / colour over lifetime; every instance gets its own
    /// deterministic variation (size, timing, spark count / speed, light, billow layout, rotation) so no two read the same.
    /// Picked per event alongside the current blast (LegacyMatch / MlrsSalvo, spec section 9) with Exp2Chance; Exp2Force is a
    /// session-only QC override (-1 mix, 0 all current, 1 all exp2). Budget: 3 Big + 6 Medium + 12 salvo balls alive, over it
    /// the current explosion plays. Sounds = our existing banks layered per spec section 8.
    /// </summary>
    public partial class OpenZFxPlayer
    {
        // ------------------------------------------------------------------------------------------------ knobs (session only)
        /// <summary>Spec section 9 default 0.45: scales every per-event chance (vehicle 0.45, salvo 0.5, building 0.4, ...).</summary>
        public static float Exp2Chance = 0.45f;
        /// <summary>QC override, session only (never saved): -1 = the normal mix, 0 = every event current, 1 = every event exp2.</summary>
        public static int Exp2Force = -1;
        public static bool Exp2LogConsole;
        public static int Exp2Total, Exp2Fallbacks;
        public static readonly int[] Exp2ByKind = new int[6];
        public static readonly List<string> Exp2Log = new List<string>();
        public const int Exp2MaxBig = 3, Exp2MaxMedium = 6, Exp2MaxSalvo = 12;

        // ------------------------------------------------------------------------------------------------ timelines (ms, U)
        // Medium = E2 (spec 3.1 / 6), Big = E3 (4 / 7), Small = S3 (7), Salvo = E1 A-C (5.1 / 7, decay borrowed from D)
        static readonly float[] M_Core = { 0, .28f, 33, .30f, 67, .37f, 100, .40f, 133, .49f, 200, .58f, 300, .67f, 467, .78f, 633, .80f, 667, .61f, 800, .54f, 1000, .50f, 1100, 0f };
        static readonly float[] M_Rim = { 0, .43f, 67, .52f, 133, .64f, 200, .73f, 300, .82f, 467, .93f, 633, .95f, 667, .76f, 800, .69f, 1000, .65f, 1100, .55f, 1250, .40f };
        static readonly float[] M_Body = { 0, .81f, 133, 1.07f, 200, 1.20f, 633, 1.22f, 667, 1.04f, 833, .88f, 967, .79f, 1133, .64f, 1267, .49f, 1367, .33f, 1467, .14f, 1533, 0f };
        static readonly float[] M_Halo = { 0, 1.2f, 500, 2.0f, 1000, 1.4f, 1400, .7f, 1900, 0f };
        static readonly float[] B_Core = { 0, .58f, 33, .60f, 67, .80f, 167, 1.11f, 200, 1.24f, 267, 1.18f, 333, 1.03f, 367, .79f, 433, .62f, 533, .54f, 700, .48f, 767, .40f, 833, .30f, 900, .25f, 967, .12f, 1000, 0f };
        static readonly float[] B_Rim = { 0, .74f, 33, .76f, 200, 1.45f, 267, 1.78f, 333, 1.30f, 433, .85f, 533, .72f, 700, .62f, 833, .42f, 967, .20f, 1067, 0f };
        static readonly float[] B_Body = { 0, 1.17f, 33, 1.24f, 200, 1.90f, 300, 2.24f, 400, 2.02f, 500, 1.56f, 567, 1.32f, 667, 1.17f, 767, 1.03f, 833, .89f, 900, .80f, 1000, .64f, 1067, .55f, 1300, .30f, 1500, 0f };
        static readonly float[] B_Halo = { 0, 2.0f, 300, 2.8f, 600, 3.0f, 1000, 2.5f, 1300, 1.5f, 1600, 0f };
        static readonly float[] S_Core = { 0, .14f, 133, .28f, 250, .22f, 633, .18f, 700, 0f };
        static readonly float[] S_Rim = { 0, .23f, 133, .37f, 600, .24f, 800, 0f };
        static readonly float[] S_Body = { 0, .43f, 133, .58f, 633, .35f, 867, 0f };
        static readonly float[] S_Halo = { 0, .6f, 133, .9f, 500, .9f, 1000, 0f };
        // salvo: the core is emitted at +33 (no white on the first frame), times below are from its own emission
        static readonly float[] V_Core = { 0, .76f, 67, .96f, 134, 1.10f, 270, 1.18f, 420, 1.30f, 600, 0f };
        static readonly float[] V_Rim = { 0, .34f, 33, 1.10f, 100, 1.37f, 167, 1.50f, 300, 1.60f, 467, 1.70f, 633, .60f, 967, 0f };
        static readonly float[] V_Body = { 0, .91f, 33, 1.96f, 67, 2.05f, 100, 2.33f, 300, 2.40f, 467, 2.40f, 633, 1.70f, 900, 1.20f, 1100, .80f, 1250, .95f, 1400, .55f, 1633, 0f };
        static readonly float[] V_Halo = { 0, 1.8f, 100, 2.4f, 500, 2.6f, 1000, 1.6f, 1400, 1.0f, 1900, 0f };
        // L11 light, normalised (spec 6 Medium, 7 Big / Small)
        static readonly float[] M_Light = { 0, .55f, 67, .55f, 167, .65f, 267, .85f, 367, .97f, 467, 1f, 633, 1f, 767, .97f, 867, .92f, 1133, .45f, 1500, .12f, 2000, 0f };
        static readonly float[] B_Light = { 0, .6f, 67, .75f, 200, 1f, 300, 1f, 500, .9f, 700, .65f, 1000, .4f, 1400, .15f, 1800, 0f };
        static readonly float[] S_Light = { 0, .5f, 133, 1f, 400, 1f, 900, 0f };

        static float Peak(float[] kv) { float m = 0f; for (int i = 1; i < kv.Length; i += 2) m = Mathf.Max(m, kv[i]); return m; }
        static float LifeMs(float[] kv) => kv[kv.Length - 2];
        static float KeyAt(float[] kv, float ms)
        {
            if (ms <= kv[0]) return kv[1];
            for (int i = 2; i < kv.Length; i += 2)
                if (ms <= kv[i]) { float t = (ms - kv[i - 2]) / Mathf.Max(1e-3f, kv[i] - kv[i - 2]); return Mathf.Lerp(kv[i - 1], kv[i + 1], t); }
            return kv[kv.Length - 1];
        }

        /// <summary>Piecewise-linear (ms, value) keys -> a normalised curve over the layer's life (max 1); mul = the peak.</summary>
        static AnimationCurve KeyCurve(float[] kv, out float mul)
        {
            float life = LifeMs(kv); mul = Mathf.Max(1e-4f, Peak(kv));
            int n = kv.Length / 2; var keys = new Keyframe[n];
            for (int i = 0; i < n; i++)
            {
                float t = kv[2 * i] / life, v = kv[2 * i + 1] / mul;
                float inS = 0f, outS = 0f;
                if (i > 0) { float dt = (kv[2 * i] - kv[2 * i - 2]) / life; inS = dt > 1e-5f ? (v - kv[2 * i - 1] / mul) / dt : 0f; }
                if (i < n - 1) { float dt = (kv[2 * i + 2] - kv[2 * i]) / life; outS = dt > 1e-5f ? (kv[2 * i + 3] / mul - v) / dt : 0f; }
                keys[i] = new Keyframe(t, v, inS, outS);
            }
            return new AnimationCurve(keys);
        }

        static Color Hx(string h) => OpenZFxCatalog.Hex(h);
        /// <summary>An sRGB colour scaled in linear light (the renderer converts particle colours to linear).</summary>
        static Color LinK(Color srgb, float k) { var l = srgb.linear * k; l.a = 1f; return l.gamma; }
        static Color Lin(float r, float g, float b, float k) { var l = new Color(r * k, g * k, b * k, 1f); return l.gamma; }
        static Color LinHdr(string h, float k) { var l = Hx(h).linear * k; l.a = 1f; return l; }

        static Gradient Grad(Color[] cols, float[] ct, float[] alphas, float[] at)
        {
            var ck = new GradientColorKey[cols.Length]; for (int i = 0; i < cols.Length; i++) ck[i] = new GradientColorKey(cols[i], ct[i]);
            var ak = new GradientAlphaKey[alphas.Length]; for (int i = 0; i < alphas.Length; i++) ak[i] = new GradientAlphaKey(alphas[i], at[i]);
            var g = new Gradient(); g.SetKeys(ck, ak); return g;
        }

        /// <summary>Spec section 6 spark ramp (linear RGB x HDR, normalised by the x6 head; green of the 15-75 % keys lowered
        /// per the spec's tuning rule so the body fringe reads red, R-G >= 150).</summary>
        static Gradient SparkRamp(float alphaFrom)
        {
            // sand); the white-hot read now comes from the comet head (shader _CoreColor), the body cools orange -> red -> crimson
            return Grad(new[] { Lin(1f, .42f, .12f, 1f), Lin(1f, .16f, .03f, .6f), Lin(1f, .06f, .012f, .42f), Lin(.9f, .025f, .02f, .27f), Lin(.7f, .01f, .03f, .12f) },
                        new[] { 0f, .1f, .35f, .7f, 1f }, new[] { 1f, 1f, 0f }, new[] { 0f, alphaFrom, 1f });
        }

        // ------------------------------------------------------------------------------------------------ channels
        bool e2Ready;
        readonly Channel[] e2Core = new Channel[5], e2Rim = new Channel[5], e2Body = new Channel[5], e2Halo = new Channel[5];
        Channel e2SparkS, e2SparkM, e2SparkB, e2SparkFast, e2Crown, e2Spike, e2Ember, e2Ring, e2FireRing, e2Roll, e2Smoke, e2Dust, e2Flash;
        static int VIdx(Exp2Kind k) => k == Exp2Kind.BuildingHit ? (int)Exp2Kind.Medium : (int)k;

        Channel E2Ch(FxChannelDef d, AnimationCurve size, float sizeMul, Gradient grad, float pivotY)
        {
            var c = CreateChannel(d); if (c == null) return null;
            channels["exp2_" + d.name] = c;
            if (size != null) { var sol = c.ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(sizeMul, size); }
            if (grad != null) { var col = c.ps.colorOverLifetime; col.enabled = true; col.color = new ParticleSystem.MinMaxGradient(grad); }
            if (pivotY != 0f) c.r.pivot = new Vector3(0f, pivotY, 0f);
            return c;
        }

        static FxChannelDef BallDef(string name, int queue, float intensity, Color coreColor, float coreMix, float rimGain, float edgeSoft, float opq) =>
            new FxChannelDef { name = name, shape = FxShape.Fireball, capacity = 160, queue = queue, opaqueness = opq, tint = Color.white,
                intensity = intensity, coreColor = coreColor, coreMix = coreMix, rimGain = rimGain, rimPower = 1.1f, edgeSoft = edgeSoft, softFade = 0.6f,
                sizeCurve = FxSizeCurve.Constant, alphaCurve = FxAlphaCurve.One };

        static FxChannelDef BodyDef(string name, Color core, Color hot, Color mid, Color rim, Color soot, float cooling, float opRim, float opCore, float noise) =>
            new FxChannelDef { name = name, shape = FxShape.Fireball, capacity = 240, queue = 3030, opaqueness = 1f, tint = Color.white,
                intensity = 1f, edgeSoft = 0.3f, softFade = 0.6f, fireRamp = true, fireCore = core, fireHot = hot, fireMid = mid, fireRim = rim, fireSoot = soot,
                cooling = cooling, heatBias = 0f, heatOpacity = true, opacityRim = opRim, opacityCore = opCore,
                noiseAmt = noise, noiseScale = 2.1f, boil = 1.2f, spin = 0.35f, sizeCurve = FxSizeCurve.Constant, alphaCurve = FxAlphaCurve.One };

        /// <summary>
        /// The old def was a 0.6 alpha-over streak at x2.5 (head 2.5, body ~1.2 = under the 1.25 bloom
        /// threshold, and it darkened the sand behind it) drawn as a uniform box; now pure additive at the spec's x6 head, drawn as a
        /// comet (round white-hot HDR head + tapered dimming tail, shader _StreakTaper) on a short base length.</summary>
        static FxChannelDef SparkDef(string name, int cap, float gravity) =>
            new FxChannelDef { name = name, shape = FxShape.Streak, capacity = cap, queue = 3049, opaqueness = 0f, tint = Color.white,
                intensity = 6f, coreColor = Hx("#FFF4D8"), coreMix = 0.7f, squareness = 0.55f, stretch = true, lengthScale = 3f,
                gravity = gravity, collide = true, bounce = 0.2f, dampen = 0.12f, minWidth = 0.15f,   // a 0.15 m glowing comet head (bloom)
                sizeCurve = FxSizeCurve.Constant, alphaCurve = FxAlphaCurve.One };

        /// <summary>Spark taper: head (1 + x) brighter than the body front.</summary>
        public static float Exp2SparkTaper = 0.6f;

        /// <summary>
        /// drag 2.5 takes 10 m/s to 8.75 m/s in 0.5 s, 1 m/s to 0 in 0.4 s), not the exponential 1/s rate the spec's spark model
        /// (and this code) assumed, so the old 0.6-1/s "drag" left 15-50 m/s sparks practically ballistic: they flew 3-10U and fell
        /// like rain. The spark channels now get a real exponential drag v *= exp(-k dt) per particle in StepExp2 (k in
        /// [dragLo, dragHi] by the particle's random seed); gravity stays Unity's, so falling sparks reach g / k, then slide.</summary>
        void SparkPhysics(Channel c, float velScale, float dragLo, float dragHi)
        {
            if (c == null) return;
            c.mat.SetFloat("_StreakTaper", Exp2SparkTaper);
            c.r.velocityScale = velScale;
            var lv = c.ps.limitVelocityOverLifetime; lv.enabled = false;
            e2Drag.Add((c, dragLo, dragHi));
        }

        readonly List<(Channel c, float lo, float hi)> e2Drag = new List<(Channel, float, float)>();
        ParticleSystem.Particle[] e2DragBuf;
        void StepSparkDrag(float dt)
        {
            if (dt <= 0f) return;
            foreach (var (c, lo, hi) in e2Drag)
            {
                int n = c.ps.particleCount; if (n == 0) continue;
                if (e2DragBuf == null || e2DragBuf.Length < n) e2DragBuf = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(n)];
                int got = c.ps.GetParticles(e2DragBuf, n);
                for (int i = 0; i < got; i++)
                {
                    float k = lo + (hi - lo) * ((e2DragBuf[i].randomSeed % 1024u) / 1023f);
                    e2DragBuf[i].velocity *= Mathf.Exp(-k * dt);
                }
                c.ps.SetParticles(e2DragBuf, got);
            }
        }

        void EnsureExp2()
        {
            if (e2Ready || !fxShader) return;
            e2Ready = true;
            var W = Color.white;
            // ---- white cores (L2): additive, HDR white x8, the last share yellowing (spec 3.2 / 4 / 7)
            var gM = Grad(new[] { Hx("#FDFDFA"), Hx("#FDFDFA"), LinK(Hx("#FEF7C7"), 3f / 8f) }, new[] { 0f, .82f, 1f }, new[] { 1f, 1f, 0f }, new[] { 0f, .9f, 1f });
            var gB = Grad(new[] { Hx("#FDFDFD"), Hx("#FDF9D8"), LinK(Hx("#F9F5C2"), .55f) }, new[] { 0f, .5f, 1f }, new[] { 1f, 1f, 0f }, new[] { 0f, .9f, 1f });
            var gS = Grad(new[] { Hx("#FDFDFA"), Hx("#FDFDFA"), LinK(Hx("#FDF8B7"), .6f) }, new[] { 0f, .71f, 1f }, new[] { 1f, 1f, 0f }, new[] { 0f, .9f, 1f });
            var gV = Grad(new[] { Hx("#FDFBE9"), Hx("#FDFDFD"), LinK(Hx("#FDFCF0"), .6f) }, new[] { 0f, .5f, 1f }, new[] { 1f, 1f, 0f }, new[] { 0f, .85f, 1f });
            float mul;
            e2Core[1] = E2Ch(BallDef("e2s_core", 3040, 1.5f, W, 1f, .85f, .6f, 0f), KeyCurve(S_Core, out mul), mul, gS, .5f);
            e2Core[2] = E2Ch(BallDef("e2m_core", 3040, 1.5f, W, 1f, .85f, .6f, 0f), KeyCurve(M_Core, out mul), mul, gM, .5f);
            e2Core[3] = E2Ch(BallDef("e2b_core", 3040, 1.2f, W, 1f, .85f, .6f, 0f), KeyCurve(B_Core, out mul), mul, gB, .3f);
            e2Core[4] = E2Ch(BallDef("e2v_core", 3040, 1.5f, W, 1f, .85f, .6f, 0f), KeyCurve(V_Core, out mul), mul, gV, .5f);
            // ---- hot rims (L3): thin peach / gold band just outside the core, x4
            var rM = Grad(new[] { Hx("#FFCE88"), Hx("#FFCB8A"), LinK(Hx("#FFD485"), .5f) }, new[] { 0f, .6f, 1f }, new[] { 1f, 1f, 0f }, new[] { 0f, .86f, 1f });
            var rB = Grad(new[] { Hx("#FCD178"), Hx("#FBCD5F"), Hx("#FFC97C"), LinK(Hx("#FFCC93"), .6f) }, new[] { 0f, .1f, .5f, 1f }, new[] { 1f, 1f, 0f }, new[] { 0f, .85f, 1f });
            var rS = Grad(new[] { Hx("#FFCF83"), Hx("#FFD795"), LinK(Hx("#FFC672"), .6f) }, new[] { 0f, .5f, 1f }, new[] { 1f, 1f, 0f }, new[] { 0f, .85f, 1f });
            var rV = Grad(new[] { Hx("#FFCF91"), Hx("#FDCC7D"), LinK(Hx("#FECE8B"), .6f) }, new[] { 0f, .4f, 1f }, new[] { 1f, 1f, 0f }, new[] { 0f, .85f, 1f });
            e2Rim[1] = E2Ch(BallDef("e2s_rim", 3036, 0.5f, W, 0f, .6f, .7f, 0f), KeyCurve(S_Rim, out mul), mul, rS, .5f);
            e2Rim[2] = E2Ch(BallDef("e2m_rim", 3036, 0.5f, W, 0f, .6f, .7f, 0f), KeyCurve(M_Rim, out mul), mul, rM, .5f);
            e2Rim[3] = E2Ch(BallDef("e2b_rim", 3036, 0.4f, W, 0f, .6f, .7f, 0f), KeyCurve(B_Rim, out mul), mul, rB, .3f);
            e2Rim[4] = E2Ch(BallDef("e2v_rim", 3036, 0.5f, W, 0f, .6f, .7f, 0f), KeyCurve(V_Rim, out mul), mul, rV, .5f);
            // ---- orange bodies (L4): noise-eroded billows on a per-variant heat ramp that cools orange -> red-orange -> crimson
            // (Big -> dark maroon smoke, Salvo -> crimson, never grey); the cool shell occludes, the hot heart is additive
            var aBody = Grad(new[] { W, W }, new[] { 0f, 1f }, new[] { 1f, 1f, .75f, 0f }, new[] { 0f, .6f, .85f, 1f });
            // (G / B too high); the ramps are authored in linear with low green / no blue (like fx_v2's FireV2*) so the screen shows
            // the spec's saturated orange #EF7B34-#F97B3C, red-orange edge and crimson end
            Color L(float r, float g, float b) => new Color(r, g, b, 1f);
            e2Body[1] = E2Ch(BodyDef("e2s_body", L(2.4f, .62f, .1f), L(2.0f, .36f, .03f), L(1.6f, .18f, .01f), L(.9f, .06f, .01f), L(.25f, .03f, .02f), .7f, .9f, .35f, .28f),
                KeyCurve(S_Body, out mul), mul, aBody, .5f);
            e2Body[2] = E2Ch(BodyDef("e2m_body", L(2.4f, .62f, .1f), L(2.0f, .36f, .03f), L(1.6f, .18f, .01f), L(.9f, .06f, .01f), L(.25f, .03f, .02f), .7f, .9f, .35f, .30f),
                KeyCurve(M_Body, out mul), mul, aBody, .5f);
            e2Body[3] = E2Ch(BodyDef("e2b_body", L(2.4f, .62f, .1f), L(2.0f, .36f, .03f), L(1.6f, .18f, .01f), L(.9f, .06f, .01f), L(.09f, .025f, .03f), .95f, .92f, .3f, .34f),
                KeyCurve(B_Body, out mul), mul, aBody, .3f);
            e2Body[4] = E2Ch(BodyDef("e2v_body", L(2.4f, .62f, .1f), L(2.0f, .34f, .03f), L(1.6f, .16f, .01f), L(.9f, .05f, .02f), L(.45f, .03f, .02f), .75f, .7f, .25f, .30f),
                KeyCurve(V_Body, out mul), mul, aBody, .5f);
            for (int i = 1; i < 5; i++) if (e2Rim[i] != null) e2Rim[i].mat.SetVector("_Color", i == 3 ? new Vector4(1f, .5f, .08f, 1f) : new Vector4(1f, .45f, .1f, 1f));   // gold / peach rim, linear
            // ---- red halos (L5): radial inner #F03C0C -> outer #B73713, x1.2 -> x1.5 (267-500) -> 0 (spec C9); salvo = crimson afterglow
            var hA = Grad(new[] { W, W }, new[] { 0f, 1f }, new[] { .8f, 1f, 1f, .667f, .4f, 0f }, new[] { 0f, .14f, .26f, .53f, .74f, 1f });
            var hAB = Grad(new[] { W, W }, new[] { 0f, 1f }, new[] { .7f, 1f, .7f, .35f, 0f }, new[] { 0f, .3f, .62f, .8f, 1f });
            var hV = Grad(new[] { Hx("#FFFFFF"), Hx("#E0B0A8"), Hx("#D08A8A") }, new[] { 0f, .5f, 1f }, new[] { .8f, 1f, .7f, 0f }, new[] { 0f, .25f, .7f, 1f });
            FxChannelDef HaloDef(string n, string outer, string inner, float inten, float opq)
            {
                var d = BallDef(n, 3028, inten, Hx(inner), 1f, 1f, 1f, opq); d.tint = Hx(outer); d.rimPower = 1f; d.capacity = 64; return d;
            }
            e2Halo[1] = E2Ch(HaloDef("e2s_halo", "#B73713", "#F03C0C", 1.0f, .55f), KeyCurve(S_Halo, out mul), mul, hA, .4f);
            e2Halo[2] = E2Ch(HaloDef("e2m_halo", "#B73713", "#F03C0C", 1.0f, .55f), KeyCurve(M_Halo, out mul), mul, hA, .4f);
            e2Halo[3] = E2Ch(HaloDef("e2b_halo", "#B73713", "#DB4A0F", 0.9f, .5f), KeyCurve(B_Halo, out mul), mul, hAB, .35f);
            e2Halo[4] = E2Ch(HaloDef("e2v_halo", "#BA3839", "#D04917", 1.2f, .25f), KeyCurve(V_Halo, out mul), mul, hV, .4f);
            for (int i = 1; i < 5; i++)
                if (e2Halo[i] != null)
                {   // saturated red halo in linear (outer -> inner), salvo a deeper crimson afterglow
                    e2Halo[i].mat.SetVector("_Color", i == 4 ? new Vector4(.7f, .03f, .02f, 1f) : new Vector4(.85f, .06f, 0f, 1f));
                    e2Halo[i].mat.SetVector("_CoreColor", i == 4 ? new Vector4(1.3f, .08f, .02f, 1f) : new Vector4(1.5f, .13f, 0f, 1f));
                }
            // ---- sparks (L6 crown, L7 ballistic + fast 15 %, Big starburst, L8 embers): spark ramp, light drag, gravity 2.5U/s^2, slide
            // ~2.5/s drag (the old 0.6-1/s drag + 6-10U/s fast ones flew 3-10U = "rain going too far"); stretch 0.12 s (was 0.16 s on
            // 6-10U/s = 1-1.6U lines), gravity ~1.8-2.2U/s^2 at the usual U 5 m (was up to 16 m/s^2 = falling rain)
            // stretch 0.06-0.07 s: a short comet (head + 0.1-0.3U tail early, ~0.13U late), not a line
            var sDef = SparkDef("e2_spark_s", 700, 4.5f); sDef.minWidth = .12f;
            e2SparkS = E2Ch(sDef, null, 1f, SparkRamp(.7f), 0f); SparkPhysics(e2SparkS, .06f, 2.4f, 3f);
            e2SparkM = E2Ch(SparkDef("e2_spark_m", 2600, 9f), null, 1f, SparkRamp(.7f), 0f); SparkPhysics(e2SparkM, .07f, 2.2f, 2.8f);
            e2SparkB = E2Ch(SparkDef("e2_spark_b", 2000, 11f), null, 1f, SparkRamp(.7f), 0f); SparkPhysics(e2SparkB, .07f, 2.2f, 2.8f);
            e2SparkFast = E2Ch(SparkDef("e2_spark_fast", 900, 9f), null, 1f, SparkRamp(.7f), 0f); SparkPhysics(e2SparkFast, .06f, 1.7f, 2.1f);
            var crown = SparkDef("e2_crown", 1800, 0f); crown.collide = false; crown.lengthScale = 2.5f; crown.minWidth = .12f;
            e2Crown = E2Ch(crown, null, 1f, SparkRamp(.6f), 0f); SparkPhysics(e2Crown, .04f, 3.5f, 4f);
            var spike = SparkDef("e2_spike", 320, 0f); spike.collide = false; spike.lengthScale = 8f; spike.intensity = 5f; spike.coreMix = .5f; spike.minWidth = .08f;
            e2Spike = E2Ch(spike, null, 1f, Grad(new[] { Hx("#FFF0C0"), Hx("#FFE0A0") }, new[] { 0f, 1f }, new[] { 1f, 1f, 0f }, new[] { 0f, .4f, 1f }), 0f);
            if (e2Spike != null) { e2Spike.r.velocityScale = 0f; e2Spike.mat.SetFloat("_StreakTaper", Exp2SparkTaper); }
            e2Ember = E2Ch(new FxChannelDef { name = "e2_ember", shape = FxShape.Glow, capacity = 700, queue = 3050, opaqueness = 0f, tint = Color.white,
                intensity = 3f, coreColor = Hx("#FFE08A"), coreMix = .6f, gravity = -1.2f, drag = 0.985f, sizeCurve = FxSizeCurve.Lerp, sizeEnd = .5f, alphaCurve = FxAlphaCurve.One },
                null, 1f, Grad(new[] { Lin(1f, .36f, .09f, .75f), Lin(1f, .16f, .045f, .5f), Lin(.75f, .05f, .07f, .25f) }, new[] { 0f, .5f, 1f }, new[] { 0f, 1f, 1f, 0f }, new[] { 0f, .05f, .8f, 1f }), 0f);
            // ---- flat ring (Small + every Salvo impact), Big ground fire ring + rolling billows, Big flash
            e2Ring = E2Ch(new FxChannelDef { name = "e2_ring", shape = FxShape.BandUV, capacity = 200, queue = 3046, opaqueness = 0f, tint = Hx("#FFF0B0"),
                intensity = 4f, bandInner = .92f, bandSides = 0f, flat = true, sizeCurve = FxSizeCurve.Lerp, sizeEnd = .65f / .3f, alphaCurve = FxAlphaCurve.PopFade }, null, 1f, null, 0f);
            e2FireRing = E2Ch(new FxChannelDef { name = "e2_firering", shape = FxShape.BandUV, capacity = 40, queue = 3031, opaqueness = .6f, tint = Color.white,
                intensity = 1.4f, bandInner = .72f, bandSides = 0f, flat = true, sizeCurve = FxSizeCurve.Lerp, sizeEnd = 1.15f, alphaCurve = FxAlphaCurve.One },
                null, 1f, Grad(new[] { Hx("#FFC97C"), Hx("#F48325") }, new[] { 0f, 1f }, new[] { 1f, 1f, 0f }, new[] { 0f, .7f, 1f }), 0f);
            if (e2FireRing != null) e2FireRing.mat.SetVector("_Color", new Vector4(1f, .25f, .02f, 1f));
            var roll =BodyDef("e2_roll", LinHdr("#FFC97C", 2.8f), LinHdr("#F48325", 2.3f), LinHdr("#E12401", 1.6f), LinHdr("#9D170C", 1.0f), LinHdr("#5C1C1D", .8f), 1f, .9f, .15f, .35f);
            roll.capacity = 120; roll.sizeCurve = FxSizeCurve.Lerp; roll.sizeEnd = 1.6f; roll.alphaCurve = FxAlphaCurve.BillowFade;
            e2Roll = E2Ch(roll, null, 1f, null, .4f);
            e2Flash = E2Ch(new FxChannelDef { name = "e2_flash", shape = FxShape.Glow, capacity = 24, queue = 3052, opaqueness = 0f, tint = Color.white,
                intensity = 4f, coreColor = Color.white, coreMix = 1f, sizeCurve = FxSizeCurve.Constant, alphaCurve = FxAlphaCurve.Linear }, null, 1f, null, .3f);
            // ---- L10 dark maroon vehicle smoke (hands off to the wreck column) and the BuildingHit pale concrete dust (6-7 s)
            e2Smoke = E2Ch(new FxChannelDef { name = "e2_smoke", shape = FxShape.Puff, capacity = 320, queue = 3012, opaqueness = 1f, tint = Color.white,
                intensity = 1f, lit = 1f, wrap = .3f, edgeSoft = .32f, softFade = 1.2f, sortByDistance = true,
                noiseAmt = .4f, noiseScale = 1.7f, boil = .9f, spin = .4f, bump = .3f, selfShadow = .8f,
                ageColor = new Color(.62f, .58f, .6f, 1f), heatColor = new Color(.9f, .22f, .05f, 1f), heatTime = .25f,
                gravity = -.3f, drag = .985f, sizeCurve = FxSizeCurve.Lerp, sizeEnd = 2f, alphaCurve = FxAlphaCurve.BillowFade }, null, 1f, null, 0f);
            var dustSize = KeyCurve(new float[] { 0, .55f, 700, 1.0f, 2500, 1.65f, 3200, 1.85f, 4700, 2.0f, 6500, 2.05f }, out float dMul);
            e2Dust = E2Ch(new FxChannelDef { name = "e2_dust", shape = FxShape.Puff, capacity = 220, queue = 3008, opaqueness = 1f, tint = Color.white,
                intensity = 1f, lit = 1f, wrap = .35f, edgeSoft = .5f, softFade = 1.2f, sortByDistance = true,
                noiseAmt = .3f, noiseScale = 1.8f, boil = .5f, spin = .25f, bump = .3f, selfShadow = .5f, ageColor = new Color(.92f, .9f, .88f, 1f),
                gravity = -.05f, drag = .99f, sizeCurve = FxSizeCurve.Constant, alphaCurve = FxAlphaCurve.One },
                dustSize, dMul, Grad(new[] { W, W }, new[] { 0f, 1f }, new[] { 0f, 1f, 1f, 0f }, new[] { 0f, .06f, .6f, 1f }), 0f);
            BuildExp2Lights();
            Debug.Log("[exp2] channels built: " + CountExp2Channels() + " (fireball 16, sparks 6, rings 2, roll, flash, smoke, dust)");
        }

        int CountExp2Channels() { int n = 0; foreach (var k in channels.Keys) if (k.StartsWith("exp2_")) n++; return n; }

        // ------------------------------------------------------------------------------------------------ L11 lights
        sealed class E2Light { public Light l; public float age = 99f, life, peak, refreshAt = -1f; public float[] curve; }
        readonly List<E2Light> e2Lights = new List<E2Light>();
        void BuildExp2Lights()
        {
            for (int i = 0; i < 10; i++)
            {
                var go = new GameObject("Exp2Light" + i); go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>(); l.type = LightType.Point; l.shadows = LightShadows.None; l.enabled = false; l.renderMode = LightRenderMode.ForcePixel;
                e2Lights.Add(new E2Light { l = l });
            }
        }

        E2Light Exp2Light(Vector3 at, float peak, float range, float[] curve)
        {
            E2Light best = null; float score = float.MaxValue;
            foreach (var x in e2Lights)
            {
                float rem = x.age >= x.life ? -1f : x.peak * KeyAt(x.curve, x.age * 1000f);
                if (rem < score) { score = rem; best = x; }
            }
            if (best == null) return null;
            best.age = 0f; best.curve = curve; best.life = LifeMs(curve) / 1000f; best.peak = peak;
            best.l.transform.position = at; best.l.range = Mathf.Max(0.5f, range); best.l.enabled = true;
            return best;
        }

        // ------------------------------------------------------------------------------------------------ pick (spec section 9)
        struct E2Cell { public int n; public float t; }
        readonly Dictionary<long, E2Cell> e2Cells = new Dictionary<long, E2Cell>();
        readonly List<(float end, Exp2Kind k)> e2Alive = new List<(float, Exp2Kind)>();
        static readonly Dictionary<int, (bool on, float last)> e2Volleys = new Dictionary<int, (bool, float)>();

        static float Hash01(int id)
        {
            uint h = (uint)id * 2654435761u ^ 0x5bd1e995u; h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; h *= 0x297a2d39u; h ^= h >> 15;
            return (h & 0xFFFFFF) / 16777216f;
        }

        static long CellKey(Vector3 p) => ((long)Mathf.FloorToInt(p.x / 10f) << 32) ^ (uint)Mathf.FloorToInt(p.z / 10f);

        int AliveOf(Exp2Kind k)
        {
            float now = Time.time; int n = 0;
            for (int i = e2Alive.Count - 1; i >= 0; i--) { if (e2Alive[i].end < now) { e2Alive.RemoveAt(i); continue; } if (e2Alive[i].k == k) n++; }
            return n;
        }

        bool BudgetOk(Exp2Kind k)
        {
            switch (k)
            {
                case Exp2Kind.Big: return AliveOf(Exp2Kind.Big) < Exp2MaxBig;
                case Exp2Kind.Salvo: return AliveOf(Exp2Kind.Salvo) < Exp2MaxSalvo;
                case Exp2Kind.Small: return AliveOf(Exp2Kind.Small) < 16;
                default: return AliveOf(Exp2Kind.Medium) + AliveOf(Exp2Kind.BuildingHit) < Exp2MaxMedium;
            }
        }

        /// <summary>Spec section 9: does this event play exp2 (want) instead of the current blast? chance = the event's table value
        /// x Exp2Chance / 0.45, deterministic per event id; water, budget and the 10 m cell rule (never more than 2 exp2 in a row in
        /// one cell within 1 s) fall back to the current explosion.</summary>
        public static Exp2Kind Pick(Exp2Kind want, float chance, Vector3 at, int id)
        {
            var z = I; if (want == Exp2Kind.None || !z || !z.isActiveAndEnabled || !z.fxShader || Exp2Force == 0) return Exp2Kind.None;
            long key = CellKey(at); float now = Time.time;
            z.e2Cells.TryGetValue(key, out var cell);
            bool roll = Exp2Force == 1 || Hash01(id) < chance * Exp2Chance / 0.45f;
            if (!roll) { z.e2Cells[key] = new E2Cell { n = 0, t = now }; return Exp2Kind.None; }
            if (IsWaterTile(at) && z.WetAt(at)) return Exp2Kind.None;
            if (Exp2Force != 1 && cell.n >= 2 && now - cell.t < 1f) { z.e2Cells[key] = new E2Cell { n = 0, t = now }; return Exp2Kind.None; }
            if (!z.BudgetOk(want)) { Exp2Fallbacks++; return Exp2Kind.None; }
            z.e2Cells[key] = new E2Cell { n = cell.n + 1, t = now };
            if (z.e2Cells.Count > 512) z.e2Cells.Clear();
            return want;
        }

        /// <summary>Rocket volleys (MLRS ripple + the sim rocket, Tough rockets): decided ONCE per volley (shooter, 3.5 s since its
        /// last rocket), never current and exp2 mixed inside one volley.</summary>
        public static bool VolleyExp2(int shooter, Vector3 at, int id)
        {
            float now = Time.time;
            if (e2Volleys.TryGetValue(shooter, out var v) && now - v.last < 3.5f) { e2Volleys[shooter] = (v.on, now); return v.on && I && I.isActiveAndEnabled && Exp2Force != 0; }
            bool on = Pick(Exp2Kind.Salvo, 0.5f, at, id ^ (shooter * 92821)) != Exp2Kind.None;
            e2Volleys[shooter] = (on, now);
            if (e2Volleys.Count > 256) e2Volleys.Clear();
            return on;
        }

        // ------------------------------------------------------------------------------------------------ the blast
        struct E2Sub { public float at; public Vector3 pos; public float U; public Exp2Kind k; public int id; public bool vehicle; }
        readonly List<E2Sub> e2Subs = new List<E2Sub>();
        sealed class SalvoBall { public Vector3 pos; public float U, last; public E2Light light; }
        readonly List<SalvoBall> salvoBalls = new List<SalvoBall>();

        /// <summary>One exp2 explosion. U = the spec's size unit in metres (LegacyMatch derives it from the event, spec 7 scale guide);
        /// s = the event's M12 strength (camera kick, ground light, scorch use the current gates with it); overlay = drawn on top of
        /// the current blast (building destruction): no events / shake / scorch of its own. vehicle = dark metal debris + maroon smoke.</summary>
        public bool Exp2(Vector3 at, float U, Exp2Kind k, int id, float s, FxBlastKind kind, bool overlay = false, bool vehicle = false)
        {
            if (!enabled || k == Exp2Kind.None) return false;
            EnsureExp2();
            if (e2Core[2] == null) return false;
            if (id < 0) id = ++serial;
            float gy = Ground(at.x, at.z);
            at.y = Mathf.Max(at.y, gy + OpenZFxCatalog.M12FloorClamp);
            var flat = new Vector3(at.x, gy, at.z);
            if (!overlay)
            {   // the current blast's non-visual duties (BlastInternal): listeners, camera (same gates), ground light, scorch, debris
                if (!QuietBlast) Exploded?.Invoke(at, s);
                if (k != Exp2Kind.Small) Shake.Impact(at, s);
                if (groundLights) GroundLight(flat, k == Exp2Kind.Small ? s * 0.6f : s, false, true);
                if (scorchMarks && at.y - gy <= OpenZFxCatalog.ScorchMaxHeight) Scorch(flat, s, kind == FxBlastKind.Vehicle || kind == FxBlastKind.Building ? 2 : 1);
                DebrisRequested?.Invoke(at, s, kind);
            }
            Seed(id * 31 + serial * 7 + 0x2E2); serial++;
            float sz = 1f + (Rand() - 0.5f) * 0.36f, tj = 1f + (Rand() - 0.5f) * 0.2f, sk = 0.8f + 0.4f * Rand(), sv = 0.8f + 0.4f * Rand(), lk = 0.85f + 0.3f * Rand();
            U = Mathf.Max(0.5f, U * sz);
            if (k == Exp2Kind.Salvo && SalvoRefresh(at, U, id, tj, sk, sv, lk)) { Note(k, at, U, id, true); return true; }
            Fireball(at, gy, U, k, tj, sk, sv, lk, vehicle, false, out var light);
            if (k == Exp2Kind.Salvo) salvoBalls.Add(new SalvoBall { pos = at, U = U, last = Time.time, light = light });
            e2Alive.Add((Time.time + 2.4f * tj, k == Exp2Kind.BuildingHit ? Exp2Kind.Medium : k));
            Sounds(at, U, k, vehicle, tj, overlay);
            Note(k, at, U, id, false);
            return true;
        }

        void Note(Exp2Kind k, Vector3 at, float U, int id, bool refresh)
        {
            Exp2Total++; Exp2ByKind[(int)k]++;
            string line = string.Format("[exp2] t={0:0.00} {1}{2} U={3:0.00} m at=({4:0.0},{5:0.0},{6:0.0}) id={7} alive B{8} M{9} V{10}", Time.time, k, refresh ? " REFRESH" : "", U, at.x, at.y, at.z, id,
                AliveOf(Exp2Kind.Big), AliveOf(Exp2Kind.Medium), AliveOf(Exp2Kind.Salvo));
            if (Exp2Log.Count >= 400) Exp2Log.RemoveAt(0);
            Exp2Log.Add(line);
            if (Exp2LogConsole) Debug.Log(line);
        }

        /// <summary>Salvo: a hit within 0.5U and 300 ms of the last one on a spot refreshes that ball (new core / body set + a fresh
        /// crown, its light back to the hold) instead of stacking a new ball (spec 7 Exp2_Salvo).</summary>
        bool SalvoRefresh(Vector3 at, float U, int id, float tj, float sk, float sv, float lk)
        {
            float now = Time.time;
            for (int i = salvoBalls.Count - 1; i >= 0; i--)
            {
                var b = salvoBalls[i];
                if (now - b.last > 0.3f) continue;
                if ((b.pos - at).sqrMagnitude > (0.5f * b.U) * (0.5f * b.U)) continue;
                b.last = now;
                float gy = Ground(b.pos.x, b.pos.z);
                Fireball(b.pos, gy, b.U, Exp2Kind.Salvo, tj, sk * 0.7f, sv, lk, false, true, out _);
                if (b.light != null && b.light.age < b.light.life) b.light.age = Mathf.Min(b.light.age, 0.2f);
                Sounds(b.pos, b.U, Exp2Kind.Salvo, false, tj, true);
                return true;
            }
            return false;
        }

        void Fireball(Vector3 at, float gy, float U, Exp2Kind k, float tj, float sk, float sv, float lk, bool vehicle, bool refresh, out E2Light light)
        {
            light = null;
            int v = VIdx(k);
            float[] body = v == 1 ? S_Body : v == 3 ? B_Body : v == 4 ? V_Body : M_Body;
            float[] core = v == 1 ? S_Core : v == 3 ? B_Core : v == 4 ? V_Core : M_Core;
            float[] rim = v == 1 ? S_Rim : v == 3 ? B_Rim : v == 4 ? V_Rim : M_Rim;
            float[] halo = v == 1 ? S_Halo : v == 3 ? B_Halo : v == 4 ? V_Halo : M_Halo;
            float pBody = Peak(body), pCore = Peak(core);
            float baseDrop = v == 1 ? 0.15f : v == 3 ? 0.45f : 0.3f;
            var b = new Vector3(at.x, Mathf.Max(gy + 0.05f, at.y - baseDrop * U), at.z);
            var centre = b + Vector3.up * (0.4f * pBody * U);
            var W = Color.white;
            // L2 white cores: 3-4 overlapping spheres (salvo: from +33, the first frame is an orange ball)
            float coreDelay = v == 4 ? 0.033f : 0f;
            int nc = 3 + (Rand() < 0.5f ? 1 : 0);
            for (int i = 0; i < nc; i++)
            {
                var o = new Vector3(Rand() - 0.5f, (Rand() - 0.5f) * 0.6f, Rand() - 0.5f) * (0.12f * pCore * U);
                EmitP(e2Core[v], b + o, Vector3.zero, LifeMs(core) / 1000f * tj, U * (i == 0 ? 0.66f : 0.56f), W, 1f, coreDelay);
            }
            // L3 hot rim
            for (int i = 0; i < 5; i++)
            {
                var o = new Vector3(Rand() - 0.5f, (Rand() - 0.5f) * 0.5f, Rand() - 0.5f) * (0.1f * Peak(rim) * U);
                EmitP(e2Rim[v], b + o, Vector3.zero, LifeMs(rim) / 1000f * tj, U * (i == 0 ? 1f : 0.9f), W, 1f);
            }
            // L4 orange billows: 70 % at 0 ms, the rest refreshed at 100-400 ms (sustained boil), laid out per blast
            int nb = v == 1 ? 6 : v == 3 ? 12 : 10;
            float off = 0.22f * pBody * U, a0 = Rand() * 6.2831853f;
            for (int i = 0; i < nb; i++)
            {
                bool late = i >= Mathf.CeilToInt(nb * 0.7f);
                float a = a0 + i * 2.39996f + (Rand() - 0.5f) * 0.8f, r = off * Mathf.Sqrt(0.15f + 0.85f * Rand());
                var o = new Vector3(Mathf.Cos(a) * r, Rand() * 0.18f * pBody * U, Mathf.Sin(a) * r);
                float delay = late ? 0.1f + 0.3f * Rand() : 0f, life = LifeMs(body) / 1000f * tj;
                EmitP(e2Body[v], b + o, Vector3.zero, Mathf.Max(0.2f, life - delay), U * (late ? 0.5f : 0.6f) * (0.9f + 0.2f * Rand()), W, 1f, delay);
            }
            // L5 red halo
            if (!refresh || v == 4) EmitP(e2Halo[v], b + Vector3.down * (0.1f * U), Vector3.zero, LifeMs(halo) / 1000f * tj, U * (refresh ? 0.8f : 1f), W, 1f);
            // Big L1 flash (never larger than the core at that moment; CALM SCREEN: none)
            if (v == 3 && !OzMotion.Reduced) EmitP(e2Flash, b + Vector3.up * (0.25f * U), Vector3.zero, 0.1f, 0.6f * U, W, 1f);
            // ---- sparks
            Sparks(centre, gy, U, v, sk, sv, tj, pCore, refresh);
            if (refresh) return;
            // ---- light (L11) + per-variant extras
            float peakI = (v == 1 ? 3f : v == 3 ? 16f : v == 4 ? 7f : 10f) * lk;
            float range = (v == 1 ? 1.5f : v == 3 ? 4f : 3f) * U;
            light = Exp2Light(centre, peakI, range, v == 1 ? S_Light : v == 3 ? B_Light : M_Light);
            if (v == 1 || v == 4)
            {   // flat ground-parallel ring 0.3U -> 0.65U over +167..+333 (S3 f382-f387), gone by +367
                EmitP(e2Ring, new Vector3(at.x, gy + 0.08f, at.z), Vector3.zero, 0.2f * tj, 0.3f * U, W, 1f, 0.167f * tj);
            }
            if (k == Exp2Kind.Medium || k == Exp2Kind.BuildingHit)
            {   // L16: 1-2 satellite Exp2_Small bursts at 0.5 size, 0.3-0.5U from the centre, +33..+100 ms
                int ns = 1 + (Rand() < 0.5f ? 1 : 0);
                for (int i = 0; i < ns; i++)
                {
                    float a = Rand() * 6.2831853f, r = (0.3f + 0.2f * Rand()) * U;
                    e2Subs.Add(new E2Sub { at = Time.time + (0.033f + 0.067f * Rand()) * tj, pos = at + new Vector3(Mathf.Cos(a) * r, (0.05f + 0.15f * Rand()) * U, Mathf.Sin(a) * r), U = U * 0.5f, k = Exp2Kind.Small, id = serial * 3 + i });
                }
            }
            if (v == 3)
            {   // starburst spikes +100..+233, ground fire ring +267..+500 + rolling billows, 2-3 burning secondaries +600..+1000
                int nsp = Mathf.RoundToInt((26 + 12 * Rand()));
                for (int i = 0; i < nsp; i++)
                {
                    var d = RandDir(0.55f); var p = b + Vector3.up * (0.45f * pCore * U) + d * (0.5f * pCore * U);
                    EmitP(e2Spike, p, d * (0.4f * U), 0.133f * tj, 0.03f * U * (0.67f + 0.66f * Rand()), W, 1f, (0.1f + 0.06f * Rand()) * tj);
                }
                EmitP(e2FireRing, new Vector3(at.x, gy + 0.1f, at.z), Vector3.zero, 0.36f * tj, 2.2f * U, W, 1f, 0.267f * tj);
                int nr = 6 + (Rand() < 0.5f ? 1 : 0) + (Rand() < 0.5f ? 1 : 0);
                for (int i = 0; i < nr; i++)
                {
                    float a = a0 + i * 6.2831853f / nr + (Rand() - 0.5f) * 0.5f; var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    EmitP(e2Roll, new Vector3(at.x, gy + 0.15f, at.z) + dir * (1.05f * U), dir * U + Vector3.up * 0.15f * U, 0.42f * tj, 0.55f * U * (0.85f + 0.3f * Rand()), W, 1f, 0.47f * tj);
                }
                int n2 = 2 + (Rand() < 0.5f ? 1 : 0);
                for (int i = 0; i < n2; i++)
                {
                    float a = Rand() * 6.2831853f, r = (1.0f + 0.8f * Rand()) * U;
                    var p = new Vector3(at.x + Mathf.Cos(a) * r, 0f, at.z + Mathf.Sin(a) * r); p.y = Ground(p.x, p.z) + 0.15f;
                    e2Subs.Add(new E2Sub { at = Time.time + (0.6f + 0.4f * Rand()) * tj, pos = p, U = U * (0.3f + 0.2f * Rand()) * 2f, k = Exp2Kind.Small, id = -1 - i, vehicle = true });
                }
            }
            // L9 debris
            if (v != 1 && v != 4 && rubble)
            {
                bool stone = k == Exp2Kind.BuildingHit || !vehicle;
                var ch = Ch(stone ? "m12_stone" : "m12_metal");
                int nd = stone ? 6 + Mathf.RoundToInt(4 * Rand()) : 4 + Mathf.RoundToInt(2 * Rand());
                if (v == 3) nd += 2;
                for (int i = 0; i < nd; i++)
                {
                    float a = Rand() * 6.2831853f, h = 2f + 3f * Rand();
                    var vel = new Vector3(Mathf.Cos(a) * h, 4.5f + 3f * Rand(), Mathf.Sin(a) * h) * (v == 3 ? 1.15f : 1f);
                    EmitP(ch, centre, vel, (i < 2 ? 7f : 3f) * tj, Mathf.Clamp((0.05f + 0.1f * Rand()) * U, 0.18f, 0.9f), W, 1f, 0.02f + 0.1f * Rand());
                }
            }
            // L10 smoke: vehicles = dark maroon puffs inside the fire from +700 ms; BuildingHit = pale concrete dust from +300 (6-7 s)
            if (vehicle && v != 1)
            {
                int n = v == 3 ? 7 : 5;
                for (int i = 0; i < n; i++)
                {
                    var o = new Vector3(Rand() - 0.5f, Rand() * 0.4f, Rand() - 0.5f) * (0.5f * pBody * U);
                    EmitP(e2Smoke, b + Vector3.up * (0.3f * U) + o, new Vector3((Rand() - 0.5f) * 0.2f * U, 0.3f * U, (Rand() - 0.5f) * 0.2f * U), 2.5f * tj,
                        0.6f * U * (0.85f + 0.3f * Rand()), i % 2 == 0 ? Hx("#5C1C1D") : Hx("#381B21"), 0.9f, (0.7f + 0.25f * Rand()) * tj);
                }
            }
            if (k == Exp2Kind.BuildingHit)
            {
                int n = 6 + Mathf.RoundToInt(4 * Rand());
                for (int i = 0; i < n; i++)
                {
                    float a = a0 + i * 2.39996f; var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    var p = new Vector3(at.x, Mathf.Max(gy + 0.3f, b.y), at.z) + dir * (0.25f * U * Rand());
                    var col = Color.Lerp(Hx("#A69993"), Hx("#B7ACA1"), Rand());
                    EmitP(e2Dust, p, dir * (0.15f * U) + Vector3.up * (0.08f + 0.04f * Rand()) * U, (6f + Rand()) * tj, 0.8f * U * (0.85f + 0.3f * Rand()), col, 0.7f, (0.3f + 0.2f * Rand()) * tj);
                }
            }
        }

        Vector3 RandDir(float upShare)
        {
            float y = Rand() < upShare ? 0.05f + 0.95f * Rand() : -0.6f * Rand();
            float a = Rand() * 6.2831853f, h = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            return new Vector3(Mathf.Cos(a) * h, y, Mathf.Sin(a) * h);
        }

        void Sparks(Vector3 centre, float gy, float U, int v, float sk, float sv, float tj, float pCore, bool refresh)
        {
            var W = Color.white;
            float width = Mathf.Clamp(0.02f * U, 0.09f, 0.2f);
            // L6 crown: short streaks from the core rim at +100..+300, uneven (2-3 sectors favoured), drag 3/s, no gravity
            int crown = Mathf.RoundToInt((v == 1 ? 35 : v == 4 ? 100 : 100) * sk);
            if (v != 3)
            {
                float s0 = Rand() * 6.2831853f;
                for (int i = 0; i < crown; i++)
                {
                    var d = RandDir(0.7f);
                    if (Rand() < 0.45f) { float a = s0 + (Rand() < 0.5f ? 0f : 2.4f) + (Rand() - 0.5f) * 1.1f; d = new Vector3(Mathf.Cos(a) * 0.8f, d.y, Mathf.Sin(a) * 0.8f).normalized; }
                    var p = centre + d * (0.3f * pCore * U);
                    EmitP(e2Crown, p, d * ((2.5f + 1.5f * Rand()) * U * sv), (0.15f + 0.2f * Rand()) * tj, width, W, 1f, (0.1f + 0.2f * Rand()) * tj);
                }
            }
            if (refresh && v == 4) { BallisticSparks(centre, U, v, Mathf.RoundToInt(30 * sk), sv, tj, 0.067f, 0.3f, width); return; }
            // L7 ballistic (the signature layer): Medium 200-260 (60 % at 67-200 ms, 40 % at 300-600), Big 400-500 in 3 bursts,
            // Small 10-15, Salvo 60-100 per ball
            if (v == 2) { int n = Mathf.RoundToInt(190 * sk); BallisticSparks(centre, U, v, Mathf.RoundToInt(n * 0.6f), sv, tj, 0.067f, 0.2f, width); BallisticSparks(centre, U, v, n - Mathf.RoundToInt(n * 0.6f), sv, tj, 0.3f, 0.6f, width); }
            else if (v == 3)
            {
                int n = Mathf.RoundToInt(330 * sk);
                BallisticSparks(centre, U, v, n / 3, sv, tj, 0f, 0.15f, width); BallisticSparks(centre, U, v, n / 3, sv, tj, 0.15f, 0.35f, width); BallisticSparks(centre, U, v, n - 2 * (n / 3), sv, tj, 0.4f, 0.7f, width);
            }
            else if (v == 1) BallisticSparks(centre, U, v, Mathf.RoundToInt(12 * sk), sv, tj, 0.067f, 0.3f, width);
            else BallisticSparks(centre, U, v, Mathf.RoundToInt(70 * sk), sv, tj, 0.067f, 0.3f, width);
            // L8 embers: tiny rising points 100-600 ms, 1.5-3 s
            int ne = Mathf.RoundToInt((v == 1 ? 6 : v == 3 ? 55 : v == 4 ? 15 : 40) * sk);
            for (int i = 0; i < ne; i++)
            {
                var d = RandDir(0.75f);
                EmitP(e2Ember, centre + d * (0.2f * U), d * ((0.5f + Rand()) * U), (1.5f + 1.5f * Rand()) * tj, Mathf.Max(0.06f, (0.01f + 0.01f * Rand()) * U * 2f), W, 1f, (0.1f + 0.5f * Rand()) * tj);
            }
        }

        /// <summary>
        /// rest all round; 75 % at 7-38 deg elevation, 25 % steeper (to ~70 deg), never straight up; skidders leave near-flat and slide.</summary>
        Vector3 FanDir(float s0, bool skid)
        {
            float a = Rand() < 0.4f ? s0 + (Rand() < 0.5f ? 0f : 2.6f) + (Rand() - 0.5f) * 1.2f : Rand() * 6.2831853f;
            float el = skid ? -0.12f + 0.24f * Rand() : (Rand() < 0.75f ? 0.12f + 0.5f * Rand() : 0.62f + 0.32f * Rand());   // sin(elevation)
            float h = Mathf.Sqrt(Mathf.Max(0f, 1f - el * el));
            return new Vector3(Mathf.Cos(a) * h, el, Mathf.Sin(a) * h);
        }

        void BallisticSparks(Vector3 centre, float U, int v, int n, float sv, float tj, float t0, float t1, float width)
        {
            var ch = v == 1 ? e2SparkS : v == 3 ? e2SparkB : e2SparkM;
            float s0 = Rand() * 6.2831853f;
            for (int i = 0; i < n; i++)
            {
                // a few (Medium 10 %, Big 12 %) faster skidders that hit the ground and slide out to ~3U; the rest 3.2-4.6U/s (Big
                // 3.5-5) under the 2.2-2.8/s drag: they stop within ~1.3-2U of the blast, then arc down / slide and die (no rain)
                bool fast = v != 1 && Rand() < (v == 3 ? 0.12f : 0.1f);
                var d = FanDir(s0, fast);
                float sp = v == 3 ? (fast ? 6f + 1f * Rand() : 3.5f + 1.5f * Rand()) : (fast ? 5.5f + 1f * Rand() : 3.2f + 1.4f * Rand());
                if (v == 1) sp *= 0.8f;
                float life = v == 1 ? 0.3f + 0.4f * Rand() : 0.7f + 1.3f * Mathf.Pow(Rand(), 1.6f);
                EmitP(fast ? e2SparkFast : ch, centre + d * (0.15f * U), d * (sp * U * sv), life * tj, width * (0.8f + 0.5f * Rand()), Color.white, 1f, (t0 + (t1 - t0) * Rand()) * tj);
            }
        }

        // ------------------------------------------------------------------------------------------------ proof helper
        ParticleSystem.Particle[] e2Buf;
        /// <summary>
        /// drawn = width x lengthScale + speed x velocityScale), how many left 1 / 2 / 3U, and the hottest HDR head value.</summary>
        public string Exp2SparkStats(Vector3 c, float U)
        {
            if (!e2Ready) return "exp2 not built";
            var dist = new List<float>(); var spd = new List<float>(); var len = new List<float>();
            int b1 = 0, b2 = 0, b3 = 0, ground = 0; float head = 0f;
            foreach (var kv in channels)
            {
                if (!kv.Key.StartsWith("exp2_") || !(kv.Key.Contains("spark") || kv.Key.Contains("crown"))) continue;
                var ch = kv.Value; int cnt = ch.ps.particleCount; if (cnt == 0) continue;
                if (e2Buf == null || e2Buf.Length < cnt) e2Buf = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(cnt)];
                int got = ch.ps.GetParticles(e2Buf, cnt);
                var grad = ch.ps.colorOverLifetime.color.gradient;
                for (int i = 0; i < got; i++)
                {
                    var p = e2Buf[i]; float dd = (p.position - c).magnitude / U, vv = p.velocity.magnitude;
                    float L = (p.GetCurrentSize(ch.ps) * ch.r.lengthScale + vv * ch.r.velocityScale) / U;
                    dist.Add(dd); spd.Add(vv / U); len.Add(L);
                    if (dd > 1f) b1++;
                    if (dd > 2f) b2++;
                    if (dd > 3f) b3++;
                    if (p.position.y - Ground(p.position.x, p.position.z) < 0.25f) ground++;
                    float age = 1f - p.remainingLifetime / Mathf.Max(1e-3f, p.startLifetime);
                    head = Mathf.Max(head, grad.Evaluate(age).linear.maxColorComponent * ch.def.intensity * (1f + Exp2SparkTaper));
                }
            }
            int n = dist.Count; if (n == 0) return "sparks 0";
            dist.Sort(); spd.Sort(); len.Sort();
            return string.Format("sparks {0} | dist/U p50 {1:0.00} p90 {2:0.00} max {3:0.00} | beyond 1U {4} 2U {5} ({6:0}%) 3U {7} | on ground {8} | speed U/s p50 {9:0.00} p90 {10:0.00} | streak U p50 {11:0.00} p90 {12:0.00} | head HDR max {13:0.0}",
                n, Qt(dist, .5f), Qt(dist, .9f), dist[n - 1], b1, b2, 100f * b2 / n, b3, ground, Qt(spd, .5f), Qt(spd, .9f), Qt(len, .5f), Qt(len, .9f), head);
        }
        static float Qt(List<float> l, float q) => l[Mathf.Clamp(Mathf.RoundToInt(q * (l.Count - 1)), 0, l.Count - 1)];

        // ------------------------------------------------------------------------------------------------ sound (spec section 8)
        void Sounds(Vector3 at, float U, Exp2Kind k, bool vehicle, float tj, bool quiet)
        {
            var ad = AudioDirector.I; var au = CombatAudio.I;
            void Later(float s, System.Action a) { if (ad && s > 0f) ad.Schedule(s, a); else a(); }
            void Fire(string id, Vector3 p, float delay, float vol, float pitch = 1f) { if (AudioDirector.Has(id)) Later(delay * tj, () => AudioDirector.Fire(id, p, -1, vol, pitch)); }
            int tinks = k == Exp2Kind.Small ? 1 + (Rand() < 0.5f ? 1 : 0) : k == Exp2Kind.Big ? 6 + (Rand() < 0.5f ? 1 : 0) + (Rand() < 0.5f ? 1 : 0) : k == Exp2Kind.Salvo ? 2 + (Rand() < 0.5f ? 1 : 0) : 3 + Mathf.RoundToInt(3 * Rand());
            for (int i = 0; i < tinks; i++)
            {   // the L7 sparks hitting and sliding: densest 300-900 ms, the last at ~2 s
                float t = i == tinks - 1 && k != Exp2Kind.Small ? 1.8f + 0.4f * Rand() : 0.15f + (Rand() < 0.75f ? 0.15f + 0.6f * Rand() : 0.9f + 0.8f * Rand());
                float a = Rand() * 6.2831853f, r = (0.4f + Rand()) * U;
                Fire("imp_shrapnel_tink", at + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r), t, 0.45f + 0.2f * Rand(), 0.9f + 0.25f * Rand());
            }
            if (quiet || k == Exp2Kind.Small || k == Exp2Kind.Salvo) return;
            // fire roar under the fireball (0-1300, Big 1100, BuildingHit 1500)
            Fire("dst_fire_ignite", at, 0.05f, k == Exp2Kind.Big ? 0.75f : 0.55f, k == Exp2Kind.Big ? 0.9f : 1f);
            if (k == Exp2Kind.Medium || k == Exp2Kind.BuildingHit)
            {   // the L16 satellite burst: a quiet second explosion take (-10 dB) in its own slot
                if (au) Later((0.033f + 0.05f * Rand()) * tj, () => au.PlayPitched("exp2_sat", at, LegacyMatch.GainExplosion * 0.32f, 0.03f, -1, 1.08f + 0.1f * Rand(), LegacyMatch.BankExplosion));
            }
            if (k == Exp2Kind.Big)
            {   // heavy layer at -3 dB +15 ms (vehicle deaths already play the heavy bank), the debris rain with the starburst
                if (au && !vehicle) Later(0.015f, () => au.PlayPitched("exp2_heavy", at, LegacyMatch.GainHeavy * 0.7f, 0.05f, -1, 0.95f, LegacyMatch.BankHeavy));
                Fire("exp_debris_rain", at, 0.25f, 0.8f);
            }
            // L9 landings (2-4, Big 3-5)
            string land = k == Exp2Kind.BuildingHit || !vehicle ? "dst_debris_concrete" : "dst_debris_metal";
            int nl = (k == Exp2Kind.Big ? 3 : 2) + Mathf.RoundToInt(2 * Rand());
            for (int i = 0; i < nl; i++) { float a = Rand() * 6.2831853f, r = (0.5f + Rand()) * U; Fire(land, at + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r), 0.9f + 0.7f * Rand(), 0.6f + 0.3f * Rand(), 0.9f + 0.2f * Rand()); }
            if (k == Exp2Kind.BuildingHit)
            {   // the long rubble / dust hiss under the dust cloud (+300..+3000)
                Fire("exp_debris_rain", at, 0.3f, 0.55f, 0.9f);
                Fire("dst_debris_concrete", at, 1.4f, 0.5f, 0.85f);
                Fire("dst_debris_concrete", at, 2.4f, 0.4f, 0.8f);
            }
        }

        // ------------------------------------------------------------------------------------------------ per frame
        void StepExp2(float dt)
        {
            StepSparkDrag(dt);
            float now = Time.time;
            for (int i = e2Subs.Count - 1; i >= 0; i--)
            {
                var s = e2Subs[i]; if (s.at > now) continue;
                e2Subs.RemoveAt(i);
                float gy = Ground(s.pos.x, s.pos.z);
                Seed(serial++ * 13 + 77);
                Fireball(s.pos, gy, s.U, Exp2Kind.Small, 1f + (Rand() - 0.5f) * 0.2f, 0.8f + 0.4f * Rand(), 0.8f + 0.4f * Rand(), 0.85f + 0.3f * Rand(), false, false, out _);
                if (s.vehicle && AudioDirector.Has("dst_debris_metal")) AudioDirector.Fire("dst_debris_metal", s.pos, -1, 0.7f, 0.95f + 0.15f * Rand());   // the burning piece lands
            }
            for (int i = salvoBalls.Count - 1; i >= 0; i--) if (now - salvoBalls[i].last > 2f) salvoBalls.RemoveAt(i);
            foreach (var x in e2Lights)
            {
                if (!x.l.enabled) continue;
                x.age += dt;
                if (x.age >= x.life) { x.l.enabled = false; continue; }
                float ms = x.age * 1000f, k = KeyAt(x.curve, ms);
                x.l.intensity = x.peak * k * OpenZFxCatalog.LightGain * 0.5f;
                var c = ms < 60f ? Hx("#FFD9A0") : Color.Lerp(Hx("#FF8A3D"), Hx("#E0502A"), Mathf.Clamp01((ms - 60f) / Mathf.Max(1f, x.life * 1000f - 60f)));
                x.l.color = c;
            }
        }

        /// <summary>Proof helpers: live exp2 particles per layer group and the active exp2 lights.</summary>
        public string Exp2Census()
        {
            if (!e2Ready) return "exp2 not built";
            int fire = 0, sparks = 0, other = 0, lightsOn = 0;
            foreach (var kv in channels)
            {
                if (!kv.Key.StartsWith("exp2_")) continue;
                int n = kv.Value.ps.particleCount;
                if (kv.Key.Contains("spark") || kv.Key.Contains("crown") || kv.Key.Contains("spike") || kv.Key.Contains("ember")) sparks += n;
                else if (kv.Key.Contains("core") || kv.Key.Contains("rim") || kv.Key.Contains("body") || kv.Key.Contains("halo")) fire += n; else other += n;
            }
            foreach (var x in e2Lights) if (x.l.enabled) lightsOn++;
            return $"exp2 live: fire {fire}, sparks {sparks}, other {other}, lights {lightsOn}, total {Exp2Total} (S {Exp2ByKind[1]} M {Exp2ByKind[2]} B {Exp2ByKind[3]} V {Exp2ByKind[4]} BH {Exp2ByKind[5]}), fallbacks {Exp2Fallbacks}";
        }
    }
}
