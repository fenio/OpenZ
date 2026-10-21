using UnityEngine;

namespace OpenZ
{
    // =====================================================================================================================
    //   M12 = the effects of our earlier projects (explosions, camera impact, blast ground light, terrain rings, scorch marks)
    // Units: metres, seconds, m/s, linear HDR gains. LEG sizes were screen pixels of an orthographic 34 m-high view,
    // converted to metres with LegPxToM. LEG milliseconds are converted to seconds here.
    // OpenZFxPlayer reads only this file, so tuning never touches the player code.
    // =====================================================================================================================

    /// <summary>OpenZ/FxAdditive `_Shape` values (see OpenZFxAdditive.shader).</summary>
    public enum FxShape { Glow = 0, Streak = 1, Puff = 2, Fireball = 3, Band = 4, BandUV = 5, Gauss = 6, Scorch = 7, FlameMesh = 8, Tracer = 9, Swell = 10, Foam = 11 }

    /// <summary>Size over normalised lifetime t (multiplies the emitted size).</summary>
    public enum FxSizeCurve
    {
        Constant,
        SinShrink,      // M12 fire / core: sin(pi t) * (1 - .4 t)
        LegCore,
        SmokeGrow,      // M12 smoke / dust: .5 + 1.8 t
        RingGrow,       // M12 ring / ripple: .2 + 6 t
        Lerp,
        RubbleShrink,   // M12 rubble: 1, shrinks to 0 over the last 20 %
        SinPulse,
        BillowGrow,     // fx_look blast smoke: .45 + 1.85 (1 - (1 - t)^2.2): fast push out, then slow billowing growth
    }

    /// <summary>Alpha over normalised lifetime t (multiplies the emitted alpha).</summary>
    public enum FxAlphaCurve
    {
        One,
        SmokeFade,      // 1 - smoothstep(.22, 1, t)
        PopFade,        // 1 - smoothstep(.15, 1, t) (pop, splash, ripple)
        LegSin,
        Linear,
        BillowFade,     // fx_look blast smoke: fade in over 6 %, hold, 1 - smoothstep(.3, 1, t)
    }

    /// <summary>What blew up. Decides the M12 strength, the LEG kind (large / hero) and the extras.</summary>
    public enum FxBlastKind
    {
        Missile,        // sim MissileExploded: s = radius / 4
        Grenade,        // LEG GRENADE hot colour #ffd45a
        Debris,         // Zod turret-debris missile landing (flag2)
        Rocket,         // local rocket landing s 1.2
        Shell,          // local shell s .85
        HeavyShell,     // local tank shell s 1.65
        Vehicle,        // destruction vehicle 1.7, LEG VEHICLE (large + hero)
        Cannon,         // destruction "other" 1.3
        Building,       // destruction explosive entity 2.15 + collapse camera 3.2
        Rock,           // destruction "other" 1.3
        Barrel,         // destruction barrel 1.2
        Crate,          // destruction crate .65
        TurretPop,      // tank turret blown off (OpenZ UnitView.TossTurret)
        Nuke,           // major-effects nuke 4.5 (camera 6, ground light 5, scorch 10)
        Legacy,         // FxManager.Explosion(at, strength) callers: s = OpenZ strength * LegacyToM12
    }

    public enum FxSurface { Auto, Ground, Road, Water, Metal, Rock, Unit }

    public enum FxWeapon { Rifle, MachineGun, Sniper, Bolt, TankShell, HeavyShell, Cannon, Bazooka, Rocket, Mlrs, Grenade }

    /// <summary>M12 per-particle rule "base + step * (j % mod)" (mod 0 = plain j).</summary>
    public struct FxLin
    {
        public float b, k; public int mod;
        public FxLin(float b, float k = 0f, int mod = 0) { this.b = b; this.k = k; this.mod = mod; }
        public float At(int j) => b + k * (mod > 0 ? j % mod : j);
    }

    /// <summary>One pooled ParticleSystem (built from code by OpenZFxPlayer).</summary>
    public struct FxChannelDef
    {
        public string name;
        public FxShape shape;
        public int capacity;
        public int queue;                 // render queue (water is Transparent-10 = 2990; all FX draw after it)
        public float opaqueness;          // 0 additive, 1 alpha-over (premultiplied)
        public Color tint;                // multiplies the particle colour (linear-space gain applied by the player)
        public float intensity;           // HDR gain (linear). Bloom threshold: 1.5 recommended (inventory 1)
        public Color coreColor; public float coreMix;
        public float lit, wrap; public Color emission; public float emissionGain;
        public float rimGain, rimPower, squareness, edgeSoft, softFade;
        public float bandInner, bandSides;
        public bool stretch; public float lengthScale;   // stretched billboard: length = width * lengthScale
        public bool flat;                 // world-aligned quad lying on the XZ plane (ripples)
        public bool sortByDistance;       // M12 sorts fading pools far to near
        public float gravity;             // m/s^2 downwards (negative = rises)
        public float drag;                // LEG per-60-Hz factor on x/z (1 = none); converted to a linear drag coefficient
        public bool collide; public float bounce, dampen;
        public FxSizeCurve sizeCurve; public float sizeEnd;     // sizeEnd: Lerp end ratio
        public FxAlphaCurve alphaCurve;
        public float colorFade;           // colour * (1 - colorFade * t) (LEG core 1 - .7 l)
        public float minWidth;            // RTS readability floor for thin streaks (m); 0 = none
        public bool mesh; public string meshName; public bool meshLit;   // mesh particles (rubble) drawn with OpenZ/FxLit
        public Color meshColor;
        // fx_look (OpenZFxAdditive shapes 2 / 3; particle age + stable random come in through custom vertex streams)
        public bool fireRamp; public Color fireCore, fireHot, fireMid, fireRim, fireSoot; public float cooling, heatBias;   // linear
        public float noiseAmt, noiseScale, boil, spin, bump, selfShadow;                                                  // billows
        public bool heatOpacity; public float opacityRim, opacityCore;   // fx_v2: shape 3 opacity follows the heat (rim occludes, heart additive)
        public Color ageColor, heatColor; public float heatTime;   // smoke: albedo x ageColor at the end (a 0 = none), heat glow at birth
    }

    /// <summary>M12 golden-angle burst layer (explosion / dust / splash / pop): j = 0..count-1, a = j * angleStep.</summary>
    public struct FxGoldenLayer
    {
        public string channel, altChannel; public int altEvery; public bool altWhenZero; // alt when j % altEvery == 0 (or != 0)
        public int count; public float angleStep;
        public FxLin posRadial, posUp;     // spawn offset (x/z along the angle, y up)
        public FxLin radial, up;           // velocity
        public float radialTimesStrength;  // 1 = radial speed * s (M12 splash)
        public FxLin life, scale;
        public bool scaleTimesStrength;
        public float sizePerScale;         // particle size (m) per unit of M12 scale (sphere radius 1 -> 2 m diameter)
        public Color color, altColor; public float alpha;
    }

    /// <summary></summary>
    public struct FxLightSpec { public Color color; public float intensityThree, range, life, decayPow, flicker, whiteHotTime, whiteHotGain; }

    /// <summary></summary>
    public struct FxProjectileSpec
    {
        public string mesh;               // combat_fx.glb mesh (FX_Rocket / FX_Shell) or "grenade"
        public float bodyScale; public Color bodyColor; public bool tracer;
        public float bodyThick;           // vehicles_nb_v1 NB6: x width across the shell's axis (0 / 1 = uniform bodyScale)
        public bool exhaust; public float exhaustScale;          // M12 continuous flame (x15), nuke 3.2
        public bool m12Smoke;                                     // smoke puff every .05 s
        public Color haloColor; public float haloPx, haloAlpha;  // LEG glow halo
        public bool legTrail; public float trailSmokeLife, sparklePx;
        public float launchK;             // M12 launchFlash strength k (MLRS 1.2, bazooka .7, others .65)
        public float groundLight;         // M12 ground light strength on launch
        public float muzzleCore, muzzleCoreLife;   // heavy shell muzzle core .46 / .14
        public float spinX, spinZ;        // grenade spin rad/s
        public bool castShadow;
    }

    public struct FxMuzzleSpec
    {
        public float coreScale, coreLife;          // M12 muzzleFlash core
        public bool cone; public float coneSize;   // LEG cone (size .7 default)
        public int glowSparks, backSmoke;
        public float lightLife, lightIntensity, lightRange; public Color lightColor;
        public float groundLight;                  // M12 ground light strength
        public bool boltDisc;                      // M12 boltMuzzle disc #ffad46 x7
    }

    public struct FxImpactSpec
    {
        public Color sparkColor, smokeColor; public int sparks; public float dust; public bool splash;
        public float lightIntensity, lightRange, lightLife; public float coreRadius, coreLife;
    }

    public static class OpenZFxCatalog
    {
        // ---------------------------------------------------------------------------------------------- global units
        /// <summary>
        /// </summary>
        public const float LegPxToM = 34f / 1188f;
        /// <summary>One knob for every LEG soft-smoke puff (muzzle back-puffs, impacts, rocket trails, wreck and death smoke). 1 =
        /// the inventory's LEG pixel sizes. The RTS camera sees ~1.4x more ground than LEG's 34 m view at the default 70 m zoom,
        /// so raise it (1.3-1.6) if these puffs read too small in the Game view.</summary>
        public static float LegSmokeScale = 1f;
        /// <summary>three.js r185 (package.json) lights are physical: diffuse = albedo / pi * I / d^2. URP diffuse has no 1/pi
        /// (Lighting.hlsl:54-56) and the same 1/d^2 * (1-(d/r)^4)^2 falloff (RealtimeLights.hlsl:47-60), so URP intensity =
        /// three intensity / pi. LightGain is the one visual knob on top.</summary>
        public const float ThreeToUrpLight = 1f / Mathf.PI;
        /// <summary>
        /// light lifted the ground under it by 0.84x the day sand for 0.13 s, a wreck fire reached 2 m at night.</summary>
        public static float LightGain = 2f;
        /// <summary>Old OpenZ FxManager strength (CombatView.Strength = .2 + .02 r_px) to M12 s (= r_px / 32): 1.25 at r 40.</summary>
        public const float LegacyToM12 = 1.25f;
        /// <summary>Zod pixels per metre (ZGame.PxToWorld: x = px / 8).</summary>
        public const float PxPerMetre = 8f;
        /// <summary></summary>
        public const float HeavySoundRadiusM = 5.6f;

        // ---------------------------------------------------------------------------------------------- colours
        public static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
        static Color Rgb(float r, float g, float b, float a = 1f) => new Color(r, g, b, a);

        // ---------------------------------------------------------------------------------------------- strengths
        /// <summary>M12 strength s per blast kind (-1 = from the radius: s = radius / 4).</summary>
        public static float StrengthOf(FxBlastKind k, float radiusM, float legacy)
        {
            switch (k)
            {
                case FxBlastKind.Rocket: return 1.2f;
                case FxBlastKind.Shell: return 0.85f;
                case FxBlastKind.HeavyShell: return 1.65f;
                case FxBlastKind.Vehicle: return 1.7f;
                case FxBlastKind.Cannon: return 1.3f;
                case FxBlastKind.Building: return 2.15f;
                case FxBlastKind.Rock: return 1.3f;
                case FxBlastKind.Barrel: return 1.2f;
                case FxBlastKind.Crate: return 0.65f;
                case FxBlastKind.TurretPop: return 1.4f;        // breakup(): vehicle 1.4
                case FxBlastKind.Nuke: return 4.5f;
                case FxBlastKind.Legacy: return Mathf.Max(0.2f, legacy * LegacyToM12);
                case FxBlastKind.Debris: return Mathf.Max(0.3f, radiusM / 4f) * 0.7f;   // OpenZ: the turret crash is a lighter blast
                default: return Mathf.Max(0.2f, radiusM / 4f);
            }
        }

        // ---------------------------------------------------------------------------------------------- channels
        static readonly Color White = Color.white;
        static readonly Color HotCore = Rgb(1f, 0.94f, 0.72f);
        /// <summary>fx_look fire ramp, LINEAR scene values tuned through URP's ACES fit + OpenZ_PostFX (+.2 EV, sat +12), see
        /// body (239,168,0) orange, rim (179,52,0) red, soot (60,52,44). Only core / hot pass the 1.25 bloom threshold.
        /// </summary>
        public static readonly Color FireCore = new Color(3.2f, 1.6f, 0.32f), FireHot = new Color(2.2f, 0.86f, 0.10f),
            FireMid = new Color(1.45f, 0.42f, 0.03f), FireRim = new Color(0.55f, 0.075f, 0.012f), FireSoot = new Color(0.09f, 0.075f, 0.06f);
        /// <summary>fx_v2 fire ramp (linear HDR). URP's Bloom threshold is gamma: 1.25 = LINEAR 1.63 (PostProcessPass.cs:1346), knee
        /// .82, prefilter on max(r,g,b); the fx_look body (1.45, .42, .03) sent 7 % to the bloom, the rim nothing. Here the heart and
        /// body are 2-5x over it (bloom share core .80, hot .69, mid .46) and stay hue-saturated through ACES (G/R .12-.37, no blue):
        /// over the day sand, with the heat-weighted opacity (FireOpacityRim .92 -> FireOpacityCore .10): core (255,245,213) white-hot,
        /// hot (255,232,133), body (255,201,65) orange-yellow, rim (251,120,0) orange-red (tools/out/fxv2_colour_table.txt).
        /// blooms at full colour): a white-hot flash in a wide warm glow; fx_v2 keeps that glow and keeps the fire orange under ACES.</summary>
        public static readonly Color FireV2Core = new Color(8.0f, 3.0f, 0.70f), FireV2Hot = new Color(5.2f, 1.2f, 0.08f),
            FireV2Mid = new Color(3.0f, 0.40f, 0f), FireV2Rim = new Color(1.6f, 0.14f, 0f), FireV2Soot = new Color(0.12f, 0.05f, 0.03f);
        public const float FireOpacityRim = 0.92f, FireOpacityCore = 0.10f, CoreOpacityRim = 0.35f, CoreOpacityCore = 0f;

        /// <summary>Every pooled ParticleSystem. Capacities: M12 pools scaled for Zod's 10 shots/s guns and
        /// </summary>
        public static readonly FxChannelDef[] Channels =
        {
            // ---- M12 layers. Hot layers keep their HDR gains (spark / core x12, fire x7, flame x15).
            // fx_look: heat-ramp fireball (was #ff8f32 x7 with a #ffe6ae core = white under ACES): billowy edge, soft ground contact
            // fx_v2: HDR ramp over the bloom threshold, heat-weighted opacity (sooty rim occludes, hot heart is additive)
            new FxChannelDef { name = "m12_fire", shape = FxShape.Fireball, capacity = 192, queue = 3020, opaqueness = 1f, tint = White,
                intensity = 1f, edgeSoft = 0.26f, softFade = 0.5f, fireRamp = true,
                fireCore = FireV2Core, fireHot = FireV2Hot, fireMid = FireV2Mid, fireRim = FireV2Rim, fireSoot = FireV2Soot, cooling = 0.8f, heatBias = 0f,
                heatOpacity = true, opacityRim = FireOpacityRim, opacityCore = FireOpacityCore,
                noiseAmt = 0.30f, noiseScale = 2.1f, boil = 1.1f, spin = 0.6f,
                sizeCurve = FxSizeCurve.SinShrink, alphaCurve = FxAlphaCurve.One },
            // fx_look: the blast core flash on the same ramp (heat bias: yellow-white centre, orange edge), part additive, small
            new FxChannelDef { name = "blast_core", shape = FxShape.Fireball, capacity = 64, queue = 3040, opaqueness = 1f, tint = White,
                intensity = 1f, edgeSoft = 0.5f, softFade = 0.5f, fireRamp = true,
                fireCore = FireV2Core, fireHot = FireV2Hot, fireMid = FireV2Mid, fireRim = FireV2Rim, fireSoot = FireV2Rim, cooling = 0.5f, heatBias = 0.65f,
                heatOpacity = true, opacityRim = CoreOpacityRim, opacityCore = CoreOpacityCore,
                noiseAmt = 0.12f, noiseScale = 1.6f, boil = 0.8f, spin = 0.3f,
                sizeCurve = FxSizeCurve.SinShrink, alphaCurve = FxAlphaCurve.One },
            new FxChannelDef { name = "m12_core", shape = FxShape.Glow, capacity = 64, queue = 3040, opaqueness = 0f, tint = White,
                intensity = 12f, coreColor = White, coreMix = 0.8f,
                sizeCurve = FxSizeCurve.SinShrink, alphaCurve = FxAlphaCurve.One, colorFade = 0.7f },      // #ffe6ae x12, LEG additive, colour*(1-.7l)
            new FxChannelDef { name = "m12_smoke", shape = FxShape.Puff, capacity = 1500, queue = 3010, opaqueness = 1f, tint = White,
                intensity = 1f, lit = 1f, wrap = 0.35f, edgeSoft = 0.3f, softFade = 1.2f, sortByDistance = true,
                noiseAmt = 0.3f, noiseScale = 2f, boil = 0.6f, spin = 0.4f, bump = 0.35f, selfShadow = 0.6f,               // fx_look: billows
                sizeCurve = FxSizeCurve.SmokeGrow, alphaCurve = FxAlphaCurve.SmokeFade },                   // #cec7b6 / dark #817c73, .54
            // fx_look: explosion smoke column (M12BlastSmoke): billowing, self-shadowed, lit by the blast light, heat glow at birth,
            // darkening to grey-brown, pushed out then slowed by drag while it rises (buoyancy)
            new FxChannelDef { name = "blast_smoke", shape = FxShape.Puff, capacity = 480, queue = 3012, opaqueness = 1f, tint = White,
                intensity = 1f, lit = 1f, wrap = 0.3f, edgeSoft = 0.32f, softFade = 1.2f, sortByDistance = true,
                noiseAmt = 0.4f, noiseScale = 1.7f, boil = 0.9f, spin = 0.4f, bump = 0.3f, selfShadow = 0.8f,
                ageColor = new Color(0.62f, 0.58f, 0.53f, 1f), heatColor = new Color(1.1f, 0.36f, 0.06f, 1f), heatTime = 0.22f,
                gravity = -0.35f, drag = 0.985f,
                sizeCurve = FxSizeCurve.BillowGrow, alphaCurve = FxAlphaCurve.BillowFade },
            // boiling, counter-spinning, sootier, more buoyant; _c smooth rounded puffs, fast spin, dustier, lazier rise
            new FxChannelDef { name = "blast_smoke_b", shape = FxShape.Puff, capacity = 240, queue = 3012, opaqueness = 1f, tint = White,
                intensity = 1f, lit = 1f, wrap = 0.26f, edgeSoft = 0.26f, softFade = 1.2f, sortByDistance = true,
                noiseAmt = 0.58f, noiseScale = 2.6f, boil = 1.35f, spin = -0.55f, bump = 0.42f, selfShadow = 0.88f,
                ageColor = new Color(0.55f, 0.52f, 0.5f, 1f), heatColor = new Color(1.1f, 0.34f, 0.05f, 1f), heatTime = 0.18f,
                gravity = -0.45f, drag = 0.982f,
                sizeCurve = FxSizeCurve.BillowGrow, alphaCurve = FxAlphaCurve.BillowFade },
            new FxChannelDef { name = "blast_smoke_c", shape = FxShape.Puff, capacity = 240, queue = 3012, opaqueness = 1f, tint = White,
                intensity = 1f, lit = 1f, wrap = 0.36f, edgeSoft = 0.42f, softFade = 1.2f, sortByDistance = true,
                noiseAmt = 0.24f, noiseScale = 1.2f, boil = 0.55f, spin = 0.85f, bump = 0.22f, selfShadow = 0.7f,
                ageColor = new Color(0.68f, 0.63f, 0.56f, 1f), heatColor = new Color(1.1f, 0.4f, 0.08f, 1f), heatTime = 0.26f,
                gravity = -0.26f, drag = 0.99f,
                sizeCurve = FxSizeCurve.BillowGrow, alphaCurve = FxAlphaCurve.BillowFade },
            new FxChannelDef { name = "m12_dust", shape = FxShape.Puff, capacity = 320, queue = 3008, opaqueness = 1f, tint = White,
                intensity = 1f, lit = 1f, wrap = 0.3f, edgeSoft = 0.5f, softFade = 1.2f, sortByDistance = true,
                noiseAmt = 0.3f, noiseScale = 1.8f, boil = 0.5f, spin = 0.3f, bump = 0.3f, selfShadow = 0.5f,               // fx_look: billows
                ageColor = new Color(0.82f, 0.78f, 0.74f, 1f),
                sizeCurve = FxSizeCurve.SmokeGrow, alphaCurve = FxAlphaCurve.SmokeFade },                   // #c89a66 .32 (demolition .60), soft edge .02-.72
            new FxChannelDef { name = "m12_spark", shape = FxShape.Streak, capacity = 640, queue = 3045, opaqueness = 0f, tint = White,
                intensity = 6f, coreColor = Hex("#ffd98a"), coreMix = 0.5f, squareness = 0.75f, stretch = true, lengthScale = 0.80f / 0.07f,   // fx_v2: x6 (was 4.5)
                gravity = 9.81f, minWidth = 0.085f, sizeCurve = FxSizeCurve.Constant, alphaCurve = FxAlphaCurve.One },   // FX_Spark .07 x .80, #ff8f32 (fx_look x4.5: yellow-orange, not white; .085 m floor)
            new FxChannelDef { name = "m12_flame", shape = FxShape.Streak, capacity = 192, queue = 3042, opaqueness = 0f, tint = White,
                intensity = 15f, coreColor = Hex("#fff0b0"), coreMix = 0.5f, squareness = 0.2f, stretch = true, lengthScale = 0.70f / 0.37f,
                sizeCurve = FxSizeCurve.Constant, alphaCurve = FxAlphaCurve.One },                         // FX_Flame cone .37 x .70, #ffb13b x15 (launch flash)
            new FxChannelDef { name = "m12_splash", shape = FxShape.Streak, capacity = 480, queue = 3006, opaqueness = 1f, tint = White,
                intensity = 1f, lit = 1f, emission = Hex("#7bc1d0"), emissionGain = 0.6f, squareness = 0.4f, stretch = true,
                lengthScale = 0.80f / 0.07f, gravity = 9.81f, minWidth = 0.05f,
                sizeCurve = FxSizeCurve.Constant, alphaCurve = FxAlphaCurve.PopFade },                     // #82b6b9, emissive #7bc1d0 x.6
            new FxChannelDef { name = "m12_ripple", shape = FxShape.BandUV, capacity = 256, queue = 3004, opaqueness = 1f, tint = White,
                intensity = 1f, lit = 1f, emission = Hex("#7bc1d0"), emissionGain = 0.6f, bandInner = 0.88f, bandSides = 32f, flat = true,
                sizeCurve = FxSizeCurve.RingGrow, alphaCurve = FxAlphaCurve.PopFade },                     // FX_ShockRing on the water, 1.35 s
            new FxChannelDef { name = "m12_ripple_calm", shape = FxShape.BandUV, capacity = 128, queue = 3004, opaqueness = 1f, tint = White,
                intensity = 1f, lit = 1f, emission = Hex("#7bc1d0"), emissionGain = 0.6f, bandInner = 0.88f, bandSides = 32f, flat = true,
                sizeCurve = FxSizeCurve.RingGrow, alphaCurve = FxAlphaCurve.PopFade },                     // the same ring for quiet signs on the water: its glow follows the night (OpenZFxPlayer.RippleCalm)
            // the soft water look (level 16, OpenZFxPlayer.SoftWater): broad swells that spread and fade, round drops, a foam patch; all lit by the hour, no glow of their own
            new FxChannelDef { name = "l16_swell", shape = FxShape.Swell, capacity = 224, queue = 3004, opaqueness = 1f, tint = White,
                intensity = 1.3f, lit = 1f, coreMix = 0.6f, bandInner = 0.09f, bandSides = 2.6f, flat = true,
                sizeCurve = FxSizeCurve.RingGrow, alphaCurve = FxAlphaCurve.PopFade },
            new FxChannelDef { name = "l16_foam", shape = FxShape.Foam, capacity = 96, queue = 3005, opaqueness = 1f, tint = White,
                intensity = 1f, lit = 1f, flat = true, sizeCurve = FxSizeCurve.Lerp, sizeEnd = 2.3f, alphaCurve = FxAlphaCurve.PopFade },
            new FxChannelDef { name = "l16_drop", shape = FxShape.Streak, capacity = 360, queue = 3006, opaqueness = 1f, tint = White,
                intensity = 1f, lit = 1f, squareness = 0f, stretch = true, lengthScale = 2.4f, gravity = 9.81f, minWidth = 0.04f,
                sizeCurve = FxSizeCurve.Constant, alphaCurve = FxAlphaCurve.PopFade },
            new FxChannelDef { name = "m12_pop", shape = FxShape.Streak, capacity = 160, queue = 3044, opaqueness = 0f, tint = White,
                intensity = 5f, coreColor = Hex("#abd9ff"), coreMix = 0.5f, squareness = 0.75f, stretch = true, lengthScale = 0.80f / 0.07f,
                minWidth = 0.05f, sizeCurve = FxSizeCurve.Constant, alphaCurve = FxAlphaCurve.PopFade },   // #83c4ff x5
            // rubble: gravity 9.81 until the floor, then "stops dead" (velocity.set(0,0,0), grounded): bounce 0, dampen 1
            new FxChannelDef { name = "m12_stone", mesh = true, meshName = "FX_RubbleStone", meshLit = true, meshColor = Hex("#6e5236"),   // fx_look: darker for the sand
                capacity = 320, queue = 2000, gravity = 9.81f, collide = true, bounce = 0f, dampen = 1f,
                sizeCurve = FxSizeCurve.RubbleShrink, alphaCurve = FxAlphaCurve.One },                      // M12 #e8d9b9 read as confetti on sand: OpenZ #9a7650
            new FxChannelDef { name = "m12_metal", mesh = true, meshName = "FX_RubbleMetal", meshLit = true, meshColor = Hex("#424941"),
                capacity = 160, queue = 2000, gravity = 9.81f, collide = true, bounce = 0f, dampen = 1f,
                sizeCurve = FxSizeCurve.RubbleShrink, alphaCurve = FxAlphaCurve.One },
            // M12 jeep / turret bolt: FX_Bolt .34 x 1.8 crossed quads, additive, emission mix((1,.25,.015), white,
            // pow(1-d,8)) x 5 with a pow(1-d,2) glow, so the visible body is ~.2 m wide; 80 m/s. Particle colour = BoltColor (sRGB).
            new FxChannelDef { name = "m12_bolt", shape = FxShape.Streak, capacity = 256, queue = 3047, opaqueness = 0f, tint = White,
                intensity = 5f, coreColor = White, coreMix = 0.85f, squareness = 0.1f, stretch = true, lengthScale = 1.8f / 0.2f,
                sizeCurve = FxSizeCurve.Constant, alphaCurve = FxAlphaCurve.One },

            new FxChannelDef { name = "leg_spark", shape = FxShape.Glow, capacity = 900, queue = 3050, opaqueness = 0f, tint = White,
                intensity = 4f, coreColor = Hex("#ffe08a"), coreMix = 0.6f, gravity = 11f, drag = 0.992f, collide = true, bounce = 0.18f, dampen = 0.35f,   // fx_look: warm core; fx_v2 x4 (was 3)
                sizeCurve = FxSizeCurve.Lerp, sizeEnd = 1f / 9f, alphaCurve = FxAlphaCurve.LegSin },       // 5-13 px -> 1 px, g 11, bounce .18
            new FxChannelDef { name = "leg_spark_light", shape = FxShape.Glow, capacity = 700, queue = 3050, opaqueness = 0f, tint = White,
                intensity = 2.5f, coreColor = HotCore, coreMix = 0.8f, gravity = 5f, drag = 0.98f,
                sizeCurve = FxSizeCurve.Lerp, sizeEnd = 1f / 7.5f, alphaCurve = FxAlphaCurve.LegSin },     // muzzle sparks 5-10 -> 1 px, g 5
            new FxChannelDef { name = "leg_impact_spark", shape = FxShape.Glow, capacity = 900, queue = 3050, opaqueness = 0f, tint = White,
                intensity = 2.5f, coreColor = HotCore, coreMix = 0.8f, gravity = 10f, drag = 0.985f, collide = true, bounce = 0.12f, dampen = 0.35f,
                sizeCurve = FxSizeCurve.Lerp, sizeEnd = 0.5f / 5f, alphaCurve = FxAlphaCurve.LegSin },     // 3-7 -> .5 px, g 10
            new FxChannelDef { name = "leg_cloud", shape = FxShape.Glow, capacity = 96, queue = 3030, opaqueness = 0f, tint = White,
                intensity = 1.25f, coreColor = Hex("#ffa84a"), coreMix = 0.25f,                              // fx_v2: the additive glow halo (was .6)
                sizeCurve = FxSizeCurve.Lerp, sizeEnd = 30f / 18f, alphaCurve = FxAlphaCurve.LegSin },      // glow clouds vr*18 -> vr*30 px
            new FxChannelDef { name = "leg_fire", shape = FxShape.Glow, capacity = 480, queue = 3032, opaqueness = 0f, tint = White,
                intensity = 1.8f, coreColor = Hex("#ffb850"), coreMix = 0.3f, gravity = -0.2f, drag = 0.96f,    // fx_v2: glowing fire clouds (was 1.1)
                sizeCurve = FxSizeCurve.Lerp, sizeEnd = 3f / 20f, alphaCurve = FxAlphaCurve.LegSin },       // fire clouds 12-26 -> 3 px, rises
            new FxChannelDef { name = "leg_ember", shape = FxShape.Glow, capacity = 400, queue = 3050, opaqueness = 0f, tint = White,
                intensity = 2.5f, coreColor = HotCore, coreMix = 0.8f, drag = 0.96f,
                sizeCurve = FxSizeCurve.Lerp, sizeEnd = 1f / 12f, alphaCurve = FxAlphaCurve.LegSin },       // rocket sparkle trail 9-15 -> 1 px
            new FxChannelDef { name = "leg_smoke", shape = FxShape.Puff, capacity = 640, queue = 3009, opaqueness = 1f, tint = White,
                intensity = 1f, lit = 0.35f, wrap = 0.6f, edgeSoft = 0.6f, softFade = 0.8f, gravity = -0.12f, drag = 0.965f, sortByDistance = true,
                noiseAmt = 0.28f, noiseScale = 1.9f, boil = 0.6f, spin = 0.5f, bump = 0.3f, selfShadow = 0.5f,              // fx_look: billows
                sizeCurve = FxSizeCurve.Lerp, sizeEnd = 3.2f, alphaCurve = FxAlphaCurve.LegSin },          // LEG soft smoke (normal blend) 5 -> 17 px
            new FxChannelDef { name = "leg_cone", shape = FxShape.Streak, capacity = 160, queue = 3046, opaqueness = 0f, tint = White,
                intensity = 2f, coreColor = HotCore, coreMix = 0.6f, squareness = 0.1f, stretch = true, lengthScale = (0.85f * 1.45f) / 0.24f,
                sizeCurve = FxSizeCurve.SinPulse, alphaCurve = FxAlphaCurve.One },                         // 6-sided cone .24 x .85, (p,p,1.45p), #ffc04d .9
            new FxChannelDef { name = "leg_tracer", shape = FxShape.Streak, capacity = 400, queue = 3048, opaqueness = 0f, tint = White,
                intensity = 2f, coreColor = White, coreMix = 0.6f, squareness = 0.6f, stretch = true, lengthScale = 1.35f / 0.055f,
                sizeCurve = FxSizeCurve.Constant, alphaCurve = FxAlphaCurve.One },                         // box .055 x streak <= 1.35, #fff1a1 .95
            new FxChannelDef { name = "leg_tracer_sleeve", shape = FxShape.Streak, capacity = 400, queue = 3047, opaqueness = 0f, tint = White,
                intensity = 1f, coreColor = White, coreMix = 0f, squareness = 0.3f, stretch = true, lengthScale = (1.35f * 1.08f) / (0.055f * 3.4f),
                sizeCurve = FxSizeCurve.Constant, alphaCurve = FxAlphaCurve.One },                         // bloom sleeve 3.4x wide #ffad43 .17
            //      brass casings, the sniper's crack tracer, zap / impact sparks, soft glows (laser muzzle + zap flash, sniper dot + glint)
            new FxChannelDef { name = "wv2_laser", shape = FxShape.Streak, capacity = 256, queue = 3049, opaqueness = 0f, tint = White,
                intensity = 9f, coreColor = White, coreMix = 0.6f, squareness = 0.15f, stretch = true, lengthScale = 1.7f / 0.13f,
                minWidth = 0.09f, sizeCurve = FxSizeCurve.Constant, alphaCurve = FxAlphaCurve.One },
            new FxChannelDef { name = "wv2_laser_glow", shape = FxShape.Streak, capacity = 256, queue = 3048, opaqueness = 0f, tint = White,
                intensity = 3.2f, coreColor = White, coreMix = 0f, squareness = 0f, stretch = true, lengthScale = 2.1f / 0.5f,
                sizeCurve = FxSizeCurve.Constant, alphaCurve = FxAlphaCurve.One },
            new FxChannelDef { name = "wv2_glow", shape = FxShape.Glow, capacity = 384, queue = 3049, opaqueness = 0f, tint = White,
                intensity = 4.5f, coreColor = White, coreMix = 0.35f, sizeCurve = FxSizeCurve.Constant, alphaCurve = FxAlphaCurve.PopFade },
            new FxChannelDef { name = "wv2_spark", shape = FxShape.Streak, capacity = 512, queue = 3050, opaqueness = 0f, tint = White,
                intensity = 6f, coreColor = White, coreMix = 0.5f, squareness = 0.7f, stretch = true, lengthScale = 0.45f / 0.055f,
                gravity = 7f, drag = 0.985f, collide = true, bounce = 0.25f, dampen = 0.4f, minWidth = 0.045f,
                sizeCurve = FxSizeCurve.Constant, alphaCurve = FxAlphaCurve.PopFade },
            new FxChannelDef { name = "wv2_mg_tracer", shape = FxShape.Streak, capacity = 640, queue = 3048, opaqueness = 0f, tint = White,
                intensity = 4.5f, coreColor = White, coreMix = 0.6f, squareness = 0.6f, stretch = true, lengthScale = 1.1f / 0.075f,
                minWidth = 0.06f, sizeCurve = FxSizeCurve.Constant, alphaCurve = FxAlphaCurve.One },
            new FxChannelDef { name = "wv2_sniper_tracer", shape = FxShape.Streak, capacity = 96, queue = 3049, opaqueness = 0f, tint = White,
                intensity = 10f, coreColor = White, coreMix = 0.8f, squareness = 0.8f, stretch = true, lengthScale = 2.6f / 0.07f,
                minWidth = 0.06f, sizeCurve = FxSizeCurve.Constant, alphaCurve = FxAlphaCurve.One },
            new FxChannelDef { name = "wv2_casing", mesh = true, meshName = "FX_Shell", meshLit = true, meshColor = Hex("#c9a246"),
                capacity = 400, queue = 2000, gravity = 9.81f, collide = true, bounce = 0.35f, dampen = 0.45f,
                sizeCurve = FxSizeCurve.RubbleShrink, alphaCurve = FxAlphaCurve.One },
        };

        // ---------------------------------------------------------------------------------------------- M12 explosion
        /// <summary>explosion(at, s), in emission order. Floor clamp: at.y = max(at.y, ground + .20).
        /// Sphere meshes have radius 1, so a particle's diameter is 2 x scale (sizePerScale 2). Sparks: FX_Spark .07 m wide x
        /// .80 m (width = .07 x scale). Rubble mesh particles use the mesh's own size (sizePerScale 1).</summary>
        public const float M12FloorClamp = 0.20f;
        public static readonly FxGoldenLayer M12Dust = new FxGoldenLayer
        {   // dust(at, s): 5 puffs, #c89a66, opacity .32
            channel = "m12_dust", count = 5, angleStep = 2.399f, radial = new FxLin(1.7f), up = new FxLin(0.4f, 0.08f),
            life = new FxLin(1.1f, 0.11f), scale = new FxLin(0.4f, 0.09f), scaleTimesStrength = true, sizePerScale = 2f,
            color = Hex("#c89a66"), alpha = 0.32f,
        };
        public static readonly FxGoldenLayer M12Rubble = new FxGoldenLayer
        {   // 9 rubble: stone x6 / metal x3 (j%3 == 0 -> metal), not x s
            channel = "m12_stone", altChannel = "m12_metal", altEvery = 3, altWhenZero = true, count = 9, angleStep = 2.399f,
            radial = new FxLin(2f, 1f, 4), up = new FxLin(3f, 1f, 3), life = new FxLin(1.7f, 0.18f, 3), scale = new FxLin(0.20f, 0.08f, 3),
            sizePerScale = 1f, color = White, alpha = 1f,
        };
        public static readonly FxGoldenLayer M12Fire = new FxGoldenLayer
        {   // 5 fireballs, angle 2.4 j, offset .38, up .25 + .09 j, v (cos*1.6, 1.8, sin*1.6)
            channel = "m12_fire", count = 5, angleStep = 2.4f, posRadial = new FxLin(0.38f), posUp = new FxLin(0.25f, 0.09f),
            radial = new FxLin(1.6f), up = new FxLin(1.8f), life = new FxLin(0.45f, 0.07f), scale = new FxLin(0.8f, 0.12f),
            scaleTimesStrength = true, sizePerScale = 2.6f, color = Hex("#ff8f32"), alpha = 1f,   // fx_look x1.2 -> fx_v2 x1.3 (engulfs the wreck)
        };
        public static readonly FxGoldenLayer M12Core = new FxGoldenLayer
        {   // 1 core flash, v (0,.3,0), .22 s, 1.15 s
            channel = "m12_core", count = 1, angleStep = 0f, radial = new FxLin(0f), up = new FxLin(0.3f), life = new FxLin(0.22f),
            scale = new FxLin(1.15f), scaleTimesStrength = true, sizePerScale = 2f, color = Hex("#ffe6ae"), alpha = 1f,
        };
        public static readonly FxGoldenLayer M12Smoke = new FxGoldenLayer
        {   // 12 smoke (j%4 == 0 -> dark), v (cos a (1+j%3) .7, 1.3 + .4 (j%4), ...), 1.5 + .23 (j%4) s, (.55 + .15 (j%3)) s
            channel = "m12_smoke", altChannel = "m12_smoke", altEvery = 4, altWhenZero = true, count = 12, angleStep = 2.399f,
            radial = new FxLin(0.7f, 0.7f, 3), up = new FxLin(1.3f, 0.4f, 4), life = new FxLin(1.5f, 0.23f, 4), scale = new FxLin(0.55f, 0.15f, 3),
            scaleTimesStrength = true, sizePerScale = 2f, color = Hex("#cec7b6"), altColor = Hex("#817c73"), alpha = 0.54f,
        };
        /// <summary>fx_look: the M12 core flash on the heat-ramp channel at .55 of the M12 size (M12 1.15 s spheres = 3.9 m white
        /// discs for a light tank), .26 s.</summary>
        public static readonly FxGoldenLayer M12BlastCore = new FxGoldenLayer
        {
            channel = "blast_core", count = 1, angleStep = 0f, radial = new FxLin(0f), up = new FxLin(0.3f), life = new FxLin(0.26f),
            scale = new FxLin(1.15f * 0.9f), scaleTimesStrength = true, sizePerScale = 2f, color = Hex("#ffe6ae"), alpha = 1f,
        };
        /// <summary>fx_look: the M12 smoke column (same 12 golden-angle puffs, velocities and sizes x s) on blast_smoke: warm grey
        /// #9c9486 / soot #625d56 (the M12 #cec7b6 lit by the 1.9 sun is (225,222,215) = the flat beige discs), lives .2-.4 s
        /// longer, alpha .66 (the eroded billows cover less than a sphere).</summary>
        public static readonly FxGoldenLayer M12BlastSmoke = new FxGoldenLayer
        {
            channel = "blast_smoke", altChannel = "blast_smoke", altEvery = 4, altWhenZero = true, count = 12, angleStep = 2.399f,
            radial = new FxLin(0.7f, 0.7f, 3), up = new FxLin(1.3f, 0.4f, 4), life = new FxLin(1.7f, 0.3f, 4), scale = new FxLin(0.55f, 0.15f, 3),
            scaleTimesStrength = true, sizePerScale = 2f, color = Hex("#9c9486"), altColor = Hex("#625d56"), alpha = 0.66f,
        };
        /// <summary>fx_look: chunks thrown high for big blasts (s >= RubbleHighMinS): 6 pieces, up 5.5-7.5 m/s.</summary>
        public static readonly FxGoldenLayer M12RubbleHigh = new FxGoldenLayer
        {
            channel = "m12_stone", altChannel = "m12_metal", altEvery = 2, altWhenZero = true, count = 6, angleStep = 2.399f,
            radial = new FxLin(1.2f, 0.6f, 4), up = new FxLin(5.5f, 1f, 3), life = new FxLin(2.2f, 0.2f, 3), scale = new FxLin(0.26f, 0.08f, 3),
            sizePerScale = 1f, color = White, alpha = 1f,
        };
        /// <summary>fx_look: rubble sized for the RTS camera (M12 pieces are 10-17 cm: invisible at 36 m), x (.7 + .3 s) clamped.</summary>
        public const float RubbleSizeBase = 1.6f, RubbleHighMinS = 1.2f;
        public static float RubbleSizeOf(float s) => RubbleSizeBase * Mathf.Clamp(0.7f + 0.3f * s, 0.8f, 1.5f);
        /// <summary>fx_look: embers / burning bits thrown high on leg_spark (gravity 11, bounce): 7 per unit of s, max 24.</summary>
        public const float BlastEmberPerS = 7f, BlastEmberSpeed = 2.5f, BlastEmberSpeedRand = 3.5f, BlastEmberUp = 4f, BlastEmberUpRand = 5f,
            BlastEmberLife = 0.7f, BlastEmberLifeRand = 0.8f, BlastEmberPx = 10f;
        public const int BlastEmberMax = 24;
        public static readonly Color BlastEmberA = Hex("#ffc24a"), BlastEmberB = Hex("#ff7a26");
        /// <summary>fx_look: the wreck-fire core (LEG r .85 x scale) drawn on blast_core at this fraction.</summary>
        public const float WreckCoreSizeMul = 0.65f;
        public static readonly FxGoldenLayer M12Sparks = new FxGoldenLayer
        {   // 20 sparks ("squares"), v (cos a (3 + j%4), 2 + j%5, ...), .45 + .12 (j%5) s, .55 s, gravity 9.81
            channel = "m12_spark", count = 20, angleStep = 2.399f, radial = new FxLin(3f, 1f, 4), up = new FxLin(2f, 1f, 5),
            life = new FxLin(0.45f, 0.12f, 5), scale = new FxLin(0.55f), scaleTimesStrength = true, sizePerScale = 0.07f,
            color = Hex("#ff8f32"), alpha = 1f,
        };
        public static readonly FxGoldenLayer M12Splash = new FxGoldenLayer
        {   // splash(at, s): 12 droplets v (cos a 2 s, 2.4 + .4 (j%3), sin a 2 s), .65 + .08 (j%3) s, .19 s
            channel = "m12_splash", count = 12, angleStep = 2.399f, radial = new FxLin(2f), radialTimesStrength = 1f, up = new FxLin(2.4f, 0.4f, 3),
            life = new FxLin(0.65f, 0.08f, 3), scale = new FxLin(0.19f), scaleTimesStrength = true, sizePerScale = 0.07f,
            color = Hex("#82b6b9"), alpha = 1f,
        };
        public static readonly FxGoldenLayer M12Pop = new FxGoldenLayer
        {   // pop(at, s): 9 sparks #abd9ff, v (cos a 1.4, 1.1 + .4 (j%3), ...), .40 s, .16 s
            channel = "m12_pop", count = 9, angleStep = 2.399f, radial = new FxLin(1.4f), up = new FxLin(1.1f, 0.4f, 3),
            life = new FxLin(0.40f), scale = new FxLin(0.16f), scaleTimesStrength = true, sizePerScale = 0.07f, color = Hex("#83c4ff"), alpha = 1f,
        };
        /// <summary>M12 demolitionDust(at, w, d, s): 8 puffs at +(cos a w .42, 1 + .3 (j%3), sin a d .42),
        /// v (cos a 1.3, .65 + .12 (j%3), sin a 1.3), 2.8 + .1 j s, (5.2 + 1.1 (j%3)) s, opacity .60.</summary>
        public const int DemolitionCount = 8; public const float DemolitionOpacity = 0.60f;

        /// <summary>M12 shock ring: terrain-painted 32-sided annulus .88-1.0 (FX_ShockRing), scale .9 s (.2 + 6 t), .55 s, no fade,
        /// tone (1,.87,.43) x 6 at opacity .6.</summary>
        /// fx_look: M12's tone x6 at .6 over (and the LEG outer + echo rings on the same radius) = a thick opaque white band under
        /// ACES. Now: one thin warm shock front (width .14 + .07 s m, max 12 % of the radius), mostly additive (opaqueness .12),
        /// fading (1 - t)^1.4, bright leading edge (_BandProfile). Radius law and life unchanged (.9 s (.2 + 6 t), .55 s).
        public static readonly Color M12RingTone = Rgb(1f, 0.56f, 0.22f);
        public const float M12RingToneGain = 2.6f,   // fx_v2: 2.6 (was 1.25): the warm shock front glows and blooms a little, still thin
       /* fx_look */ M12RingToneGainFxLook = 1.25f, M12RingOpacity = 0.55f, M12RingLife = 0.55f, M12RingScale = 0.9f, M12RingInner = 0.88f, M12RingSides = 32f;
        public const float M12RingOpaqueness = 0.12f, M12RingFadePow = 1.4f, M12RingWidthBase = 0.14f, M12RingWidthPerS = 0.07f;
        public static float M12RingWidthOf(float s) => M12RingWidthBase + M12RingWidthPerS * s;
        public static float M12RingInnerAt(float radius, float width) => Mathf.Clamp(1f - width / Mathf.Max(radius, 0.01f), M12RingInner, 0.995f);
        public const float M12RingLift = 0.07f;
        public const int MaxRingsPerFrame = 16;                                                                 // TERRAIN_RING_CAPACITY

        // ---------------------------------------------------------------------------------------------- LEG explosion
        /// <summary>
        /// large = TNT || VEHICLE || radius >= 3.8, hero = TNT || VEHICLE || (large && !catapult && id % 4 == 0).</summary>
        public const float LegLargeRadius = 3.8f, LegVehicleMul = 1.15f, LegLargeMul = 1.08f, LegHeroMul = 1.24f;
        /// <summary>LEG radius for blasts that have no Zod radius (vehicle / building / rock deaths, legacy callers): 4 s, capped at
        /// </summary>
        public const float LegFixedKindMaxRadius = 5f;
        static readonly Color LegHotGrenade = Hex("#ffd45a"), LegHotVehicle = Hex("#ff5a32"), LegHotTnt = Hex("#ff4226"), LegHotDefault = Hex("#ff8a37");
        public static Color LegHotColor(FxBlastKind k) =>
            k == FxBlastKind.Grenade ? LegHotGrenade : k == FxBlastKind.Vehicle ? LegHotVehicle : k == FxBlastKind.Building || k == FxBlastKind.Barrel ? LegHotTnt : LegHotDefault;
        // rings (RingGeometry(.82, 1, 28), additive, scale vr (.08 + .92 l), colour * (1 - l))
        public const float LegRingInner = 0.955f, LegRingSides = 28f, LegRingGain = 0.35f;   // fx_look: thin + faint (was .82 / 1.6)
        /// <summary>fx_look: the LEG echo ring only for TNT kinds (building, barrel, nuke); on vehicles it stacked on the M12 ring.</summary>
        public static bool LegEchoRingTntOnly = true;
        public const float LegMainRingLife = 0.33f, LegMainRingLifeLarge = 0.47f, LegMainRingLifeHero = 0.55f;   // replaced by the M12 ring; kept for reference
        public const float LegOuterRingLife = 0.54f, LegOuterRingLifeTnt = 0.62f, LegOuterRingLifeHero = 0.68f;
        public const float LegOuterRingMul = 1.32f, LegOuterRingMulTnt = 1.46f, LegOuterRingMulHero = 1.55f;
        public static readonly Color LegOuterRingColor = Hex("#ff9a45"), LegOuterRingColorTnt = Hex("#ffc766");
        public const float LegEchoRingDelay = 0.055f, LegEchoRingEnd = 0.76f, LegEchoRingMul = 1.82f;
        public static readonly Color LegEchoRingColor = Hex("#ffd990");
        public const float LegRingLift = 0.10f;
        // point light
        public const float LegLightBase = 14f, LegLightBaseLarge = 24f, LegLightPerVr = 4.2f, LegLightHeroMul = 1.2f;
        public const float LegLightRange = 4.5f, LegLightRangeLarge = 5.8f, LegLightRangeHeroMul = 1.16f;
        public const float LegLightLife = 0.36f, LegLightLifeLarge = 0.52f, LegLightLifeHero = 0.61f, LegLightDecayPow = 2.2f;
        // glow clouds: 3 (hero 4), v (0, .08 + .04 i, 0), delay 18 i ms, (large 285 : 230) + 70 i ms, vr (18 - 2 i) -> vr (30 - 3 i) px
        public static readonly Color[] LegCloudColors = { Hex("#ff7a22"), Color.clear /* hot */, Hex("#e8380c"), Hex("#e8380c") };   // fx_v2: deeper, so the additive halo stays orange on the sand   // fx_look: #fff2b0 -> #ffb54a
        // sparks: min(hero 44 / large 36 / 24, (large 18 : 10) + round(3 vr) + (hero 8)), speed 2.4 + rnd vr (large 2.25 : 2),
        // y .35 + rnd .55, up 2 + rnd 6.5, 350 + rnd 380 ms, 5 + rnd 8 px -> 1, #fff1a1 / #ff9b3d / #ff5a2c
        public static readonly Color LegSparkA = Hex("#fff1a1"), LegSparkB = Hex("#ff9b3d"), LegSparkC = Hex("#ff5a2c");
        // fire clouds: hero 17 / large 13 / 8, spread vr .38, y .25 + rnd (large .85 : .5), v (cos (0.4 + rnd 1.5), .7 + rnd (large 2.1 : 1.6)),
        // (large 400 : 320) + rnd 280 ms, delay rnd 90 ms, (large 15 : 12) + rnd 11 px -> 3, #ffd66b / hot / #ff6c2f
        public static readonly Color LegFireA = Hex("#ffd66b"), LegFireC = Hex("#ff6c2f");
        // LEG smoke (hero 20 / large 16 / 9) is NOT emitted: the M12 lit smoke column covers it (inventory 17.1).

        // ---------------------------------------------------------------------------------------------- M12 ground light
        /// <summary>Gaussian colour * exp(-5 (d/r)^2) * w, r = 3.5 + 2.3 s, w = min(1.3, .48 s) * exp(-8 age),
        /// </summary>
        public const float GroundLightRadiusBase = 3.5f, GroundLightRadiusPerS = 2.3f, GroundLightMaxW = 1.3f, GroundLightWPerS = 0.48f,
            GroundLightDecay = 8f, GroundLightCut = 0.48f, GroundLightLift = 0.05f;
        /// <summary>Ground light and shock rings: scene surfaces up to this far (m of eye depth) in front of the decal mesh still
        /// receive it (full up to 0.6 x, fading out to 1 x): raised ground, crater rims and low props no longer cut the glow.</summary>
        public const float GroundDecalBand = 1.6f;
        public static readonly Color GroundLightColorLinear = new Color(1f, 0.38f, 0.12f), GroundLightElectroLinear = new Color(0.18f, 0.8f, 2.5f);
        public const int GroundLightSlots = 8;
        /// <summary>fx_look: explosions keep the Gaussian longer and stronger (M12 exp(-8 age) was gone before .3 s): w = min(1.6,
        /// .62 s) x exp(-3.4 age), cut at .85 s. Shots keep the M12 values.</summary>
        public const float GroundLightMaxWBlast = 2.6f, GroundLightWPerSBlast = 1.0f, GroundLightDecayBlast = 2.4f, GroundLightCutBlast = 1.2f;   // fx_v2 (was 1.6 / .62 / 3.4 / .85)
        /// <summary>fx_v2: the blast ground light is deeper orange (additive on bright sand, (1,.38,.12) went pale yellow).</summary>
        public static readonly Color GroundLightBlastColorLinear = new Color(1f, 0.27f, 0.05f);
        /// <summary>fx_v2 explosion point light: LEG intensity x BlastLightGain, lifted BlastLightLift m above the blast so it pools
        /// on the ground and lights the units / walls around (at .6 m it only grazed the sand), range and life x the muls.</summary>
        public const float BlastLightGain = 2.6f, BlastLightLift = 2.2f, BlastLightRangeMul = 2.6f, BlastLightLifeMul = 1.6f;
        /// <summary>Ground light strength per shot.</summary>
        public const float GroundLightBullet = 0.38f, GroundLightBolt = 0.55f, GroundLightRocket = 0.8f, GroundLightBazooka = 0.7f,
            GroundLightTank = 1.0f, GroundLightMlrs = 1.4f, GroundLightPickup = 0.6f, GroundLightNuke = 5f, GroundLightBoltImpact = 0.32f;

        // ---------------------------------------------------------------------------------------------- lights
        public static readonly Color LegLightColor = Hex("#ff9a45");
        public const int MaxLights = 16;
        /// <summary>OpenZ's white-hot first 60 ms on explosion lights (FxManager.cs:443-445), kept because the core is white-yellow.</summary>
        public const float WhiteHotTime = 0.06f, WhiteHotGain = 1.35f;
        public static readonly FxLightSpec WreckFireLight = new FxLightSpec
        {
            color = Hex("#ff672d"), intensityThree = 24f, range = 11f, life = 0.62f, decayPow = LegLightDecayPow, flicker = 0.18f,
        };
        public static readonly FxLightSpec ImpactLight = new FxLightSpec
        {
            color = Hex("#ffb34f"), intensityThree = 5.5f, range = 3.2f, life = 0.11f, decayPow = LegLightDecayPow,
        };
        /// <summary>Burning wreck light after the blast: burning actor 8 x (.8 + .2 sin 9t), range 10.</summary>
        public const float BurnLightThree = 8f, BurnLightRange = 10f;

        // ---------------------------------------------------------------------------------------------- camera shake (M12)
        /// <summary>ignored under s .8 or off screen; amplitude = min(1.35 m, span / viewportH x (4 + 3 s)),
        /// energy = min(1, energy + .64 s), exp(-5 dt), offset (sin 61.3 t, .62 sin (73.1 t + .7)) along camera right / up.
        /// Large collapses 3.2, vehicle landing .65 + .7 force, vehicle into water .9, nuke 6 + rumble 4.</summary>
        /// deaths 1.7 and larger only; shells, robots, rockets, zaps never shake), peak (4 + 3 s) px -> (1 + 1.5 s) px clamped to MaxPx 5
        /// (was 14; ~0.16-0.23 m at the default 70 m height), rumble 2.4 -> 1.2 px per s; the global cooldown is OzShakeGate.
        public const float ShakeMinStrength = 1.6f, ShakeBasePx = 1f, ShakePxPerS = 1.8f, ShakeMaxWorld = 1.35f, ShakeEnergyPerS = 0.64f,
            ShakeDecay = 5f, ShakeFreqX = 61.3f, ShakeFreqY = 73.1f, ShakePhaseY = 0.7f, ShakeYRatio = 0.62f, ShakeRumblePerS = 1.2f;
        /// <summary>Fort destruction: ONE subtle rumble (strength x RumblePerS px) that eases in over .15 s and out by Seconds.</summary>
        public const float ShakeFortRumble = 5.5f * 1.3f, ShakeFortRumbleSeconds = 2.6f + 0.8f;
        /// <summary>
        /// The old fort rumble 3 x 1.2 = 3.6 px and the 5 px impact cap read as no shake at all
        /// at 2560x1600. Now (1080 px reference): nuke rumble 9 x 1.2 = 10.8 px for ~1 s, eased out by 2.8 s; fort fall 6.6 px; impacts
        /// (1 + 1.8 s) px capped at MaxPx 12: vehicle deaths 1.7 -> 4.1 px (small cap 4.5), buildings 2.15 -> 4.9, collapses 3.2 -> 6.8,
        /// nuke 6 -> 11.8; shells / gunfire / robots never shake (MinStrength 1.6); OzShakeGate keeps on-screen + the 2.5 s cooldown.</summary>
        public const float ShakeNukeRumble = 9f * 1.3f, ShakeNukeRumbleSeconds = 2.8f + 0.8f;
        public const float ShakeCollapse = 3.2f, ShakeNuke = 6f, ShakeWaterPlunge = 0.9f, ShakeTurretLand = 0.9f;
        /// <summary>
        /// cover this fraction of the screen height; peak clamped to MaxPx at a 1080 px reference height; repeated weaker hits within
        /// MinInterval do not re-kick; more than BusyCount hits per second scale down by 1/sqrt(n).</summary>
        public const float ShakeMinScreenFraction = 0.05f, ShakeMaxPx = 12f, ShakeReferenceHeight = 1080f, ShakeMinInterval = 0.12f;
        public const int ShakeBusyCount = 3, ShakeMaxRumbles = 4;
        /// <summary>
        /// SmallGap s, peak SmallMaxPx (1080 px reference).</summary>
        public const float ShakeBigS = 1.6f, ShakeSmallGap = 1.4f, ShakeSmallMaxPx = 4.5f;
        /// <summary>Event kicks (OpenZFxShake.Kick): a building or a bridge blowing up in view always kicks (short, clearly felt); an
        /// MLRS salvo landing in view kicks now and then (a chance, small, behind the shared cooldown). Peaks in px at the 1080 px
        /// reference with the camera at its default height or nearer (ShakeKickRefSpan m of ground across the view height);
        /// further away they scale down with the distance, never under ShakeKickFarK. ShakeKickEdge: this part of the view size
        /// outside the screen edge still counts as in view (a big building half on screen).</summary>
        public const float ShakeBuildingPx = 9f, ShakeBridgePx = 8f, ShakeMlrsPx = 6f, ShakeMlrsChance = 0.6f,
            ShakeKickRefSpan = 50f, ShakeKickFarK = 0.4f, ShakeKickEdge = 0.1f;
        /// <summary>The heaviest weapons in view (a howitzer, the fort's big cannon): a small kick when the gun fires and a stronger
        /// one where its shell lands (OpenZHeavyShells), the fort's cannon most. Event kicks as above: scaled by the distance, only in
        /// view, behind the shared pause between kicks and the SCREEN SHAKE / CALM SCREEN switches. The landing kick also carries the
        /// camera's own short jitter (OpenZHeavyShells.ShakeAt), which no longer depends on being let through a second time.
        /// ShakeFortImpactPx stays over OzShakeGate.OverrideRatio x the fort shell blast's own kick (4.2 px, asked first in the same
        /// frame) at every camera height at which that one plays (up to 80 m of ground across the view): 10.5 x 50 / 80 = 6.6 > 6.3.</summary>
        public const float ShakeHowitzerShotPx = 4f, ShakeFortShotPx = 5f, ShakeHowitzerImpactPx = 8f, ShakeFortImpactPx = 10.5f;

        // ---------------------------------------------------------------------------------------------- scorch (M12 + LEG)
        /// <summary>radius max(.18, 1.3 s), wobbly 16-gon r = .84 + .19 sin(2.8 j + seed) + .05 cos(5.4 j + seed),
        /// radial gradient .58 (0) / .38 (.40 of 1.4) / 0 (1.4), 7 streaks .20 x .05 at r .55 + .22 (j%3), 22 % alpha, aspect .78-1.08,
        /// angle id x 2.399; diffuse *= 1 - burn x .58. Life: explosion 45-65 s (fade from 20 %), vehicle / building 90-130 s (30 %),
        /// debris contact 18-26 s (20 %). Max 32 (8 contacts), skipped more than 3.5 m above the ground; dedupe radius max(.7, .42 r).</summary>
        public const float ScorchRadiusPerS = 1.3f, ScorchMinRadius = 0.18f, ScorchBurnGain = 0.58f, ScorchLift = 0.04f, ScorchMaxHeight = 3.5f;
        public const int ScorchMax = 32, ScorchContactMax = 8;
        public const float ScorchLifeImpact = 45f, ScorchLifeImpactRand = 20f, ScorchFadeImpact = 0.2f;
        public const float ScorchLifeLarge = 90f, ScorchLifeLargeRand = 40f, ScorchFadeLarge = 0.3f;
        public const float ScorchLifeContact = 18f, ScorchLifeContactRand = 8f;
        /// <summary>Tread marks (trails): every .6 m, tank width .5 / car .32, length
        /// 1.05, alpha .36, fade 3 -> 10 s, 768 slots, contact offsets +-1.15 (wide) / +-.8.</summary>
        public const float TrackStep = 0.6f, TrackWidthWide = 0.5f, TrackWidthCar = 0.32f, TrackLength = 1.05f, TrackAlpha = 0.36f,
            TrackFadeFrom = 3f, TrackLife = 10f, TrackOffsetWide = 1.15f, TrackOffsetCar = 0.8f, TrackLift = 0.03f;
        public const int TrackSlots = 768;
        /// <summary>Mech feet: a step on every half stride, phase = floor(travel x 3.2 / pi)
        /// (a step every ~.98 m), under the ankle MechAnkle_L / _R ((phase + 1) % 2), mark .8 wide x 1.05 long, dust .5. The
        /// </summary>
        public const float MechStepPhase = 3.2f, MechStepWidth = 0.8f, MechStepLength = 1.05f, MechStepDust = 0.5f;

        // ---------------------------------------------------------------------------------------------- rockets / projectiles
        public static readonly Color ExhaustColor = Hex("#ffb13b");
        public const float ExhaustGain = 15f, ExhaustDistance = 0.35f, ExhaustBaseScale = 1.12f, ExhaustPulseA = 0.94f, ExhaustPulseB = 0.10f;
        public const float RocketSmokeEvery = 0.05f, RocketSmokeLife = 1.05f, RocketSmokeScale = 0.28f;
        public const float RocketSmokeSpacing = 0.45f;
        public static readonly Vector3 RocketSmokeVelocity = new Vector3(0.10f, 0.42f, 0.06f);
        public const float LegTrailEvery = 0.06f, LegTrailTail = 0.43f, LegHaloPulse = 0.06f;
        public static readonly Color LegTrailSmokeA = Hex("#454943"), LegTrailSmokeB = Hex("#68675d"), LegSparkleA = Hex("#ff612c"), LegSparkleB = Hex("#ffd05a");
        public static readonly Color ShellBody = Hex("#34454f");
        public static readonly Color ShellTracerLinear = new Color(5f, 0.95f, 0.12f);
        public const float ShellTracerFrom = 0.05f, ShellTracerTo = 0.22f;             // fraction of the length from the rear

        // Specs are built once (Hex parses a string: never per shot at Zod's 10 shots/s).
        static FxProjectileSpec[] projectileSpecs; static FxMuzzleSpec[] muzzleSpecs; static FxImpactSpec[] impactSpecs;
        static T[] BuildTable<T>(int n, System.Func<int, T> make) { var a = new T[n]; for (int i = 0; i < n; i++) a[i] = make(i); return a; }
        public static FxProjectileSpec ProjectileOf(FxWeapon w) =>
            (projectileSpecs ??= BuildTable(System.Enum.GetValues(typeof(FxWeapon)).Length, i => BuildProjectile((FxWeapon)i)))[(int)w];
        public static FxMuzzleSpec MuzzleOf(FxWeapon w) =>
            (muzzleSpecs ??= BuildTable(System.Enum.GetValues(typeof(FxWeapon)).Length, i => BuildMuzzle((FxWeapon)i)))[(int)w];
        public static FxImpactSpec ImpactOf(FxSurface s) =>
            (impactSpecs ??= BuildTable(System.Enum.GetValues(typeof(FxSurface)).Length, i => BuildImpact((FxSurface)i)))[(int)s];

        static FxProjectileSpec BuildProjectile(FxWeapon w)
        {
            switch (w)
            {
                case FxWeapon.Bazooka: return new FxProjectileSpec { mesh = "FX_Rocket", bodyScale = 1f, bodyColor = White, exhaust = true, exhaustScale = 1f,
                    m12Smoke = true, haloColor = Hex("#ff7430"), haloPx = 23f, haloAlpha = 0.58f, legTrail = true, trailSmokeLife = 0.54f, sparklePx = 9f,
                    launchK = 0.7f, groundLight = GroundLightBazooka };
                case FxWeapon.Rocket: return new FxProjectileSpec { mesh = "FX_Rocket", bodyScale = 1f, bodyColor = White, exhaust = true, exhaustScale = 1f,
                    m12Smoke = true, haloColor = Hex("#ff5c28"), haloPx = 25f, haloAlpha = 0.58f, legTrail = true, trailSmokeLife = 0.54f, sparklePx = 9f,
                    launchK = 0.65f, groundLight = GroundLightRocket };                                                 // HELI_ROCKET look
                case FxWeapon.Mlrs: return new FxProjectileSpec { mesh = "FX_Rocket", bodyScale = 1f, bodyColor = White, exhaust = true, exhaustScale = 1f,
                    m12Smoke = true, haloColor = Hex("#ff2518"), haloPx = 31f, haloAlpha = 0.76f, legTrail = true, trailSmokeLife = 0.76f, sparklePx = 15f,
                    launchK = 1.2f, groundLight = GroundLightMlrs };
                case FxWeapon.HeavyShell: return new FxProjectileSpec { mesh = "FX_Shell", bodyScale = 2.125f /* 15 % smaller on screen (was 2.5) */, bodyThick = 1.45f /* vehicles_nb_v1 NB6: thicker, heavier (was 2.2 round) */, bodyColor = ShellBody, tracer = true,
                    haloColor = Hex("#a45f32"), haloPx = 12.75f /* with the body (was 15) */, haloAlpha = 0.26f, launchK = 0.65f, groundLight = GroundLightTank, muzzleCore = 0.46f, muzzleCoreLife = 0.14f };
                case FxWeapon.TankShell: return new FxProjectileSpec { mesh = "FX_Shell", bodyScale = 2.2f, bodyColor = ShellBody, tracer = true,
                    haloColor = Hex("#a45f32"), haloPx = 15f, haloAlpha = 0.26f, launchK = 0.65f, groundLight = GroundLightTank, muzzleCore = 0.46f, muzzleCoreLife = 0.14f };
                case FxWeapon.Cannon: return new FxProjectileSpec { mesh = "FX_Shell", bodyScale = 1.5f, bodyColor = ShellBody, tracer = true,
                    haloColor = Hex("#a45f32"), haloPx = 17f, haloAlpha = 0.28f, launchK = 0.75f, groundLight = GroundLightTank, muzzleCore = 0.38f, muzzleCoreLife = 0.12f };
                case FxWeapon.Grenade: return new FxProjectileSpec { mesh = "grenade", bodyScale = 1.55f, bodyColor = Hex("#34454f"),
                    haloColor = Hex("#87915e"), haloPx = 11f, haloAlpha = 0.2f, spinX = 7f, spinZ = 3f, castShadow = true };
                default: return new FxProjectileSpec { mesh = "FX_Shell", bodyScale = 1f, bodyColor = ShellBody, tracer = true, haloColor = Hex("#a45f32"), haloPx = 15f, haloAlpha = 0.26f };
            }
        }
        /// <summary>M12 flight: gravity rocket 6 / shell 9.8; grenade 9.8.</summary>
        public static float GravityOf(FxWeapon w) => w == FxWeapon.Bazooka || w == FxWeapon.Rocket || w == FxWeapon.Mlrs ? 6f : 9.8f;
        /// <summary>M12 launchFlash: core .11 s .42 k; 3 x (flame -dir 3.2, .16 s, .75 k; smoke (.2,.75,.1), .7 s, .35 k) at -.3 - .18 j.</summary>
        public const float LaunchCoreLife = 0.11f, LaunchCoreScale = 0.42f, LaunchFlameSpeed = 3.2f, LaunchFlameLife = 0.16f, LaunchFlameScale = 0.75f,
            LaunchSmokeLife = 0.7f, LaunchSmokeScale = 0.35f, LaunchBack = 0.3f, LaunchStep = 0.18f;
        public static readonly Vector3 LaunchSmokeVelocity = new Vector3(0.2f, 0.75f, 0.1f);

        // ---------------------------------------------------------------------------------------------- muzzle flashes
        static FxMuzzleSpec BuildMuzzle(FxWeapon w)
        {
            switch (w)
            {
                case FxWeapon.MachineGun:
                case FxWeapon.Rifle:
                case FxWeapon.Sniper:
                    return new FxMuzzleSpec { coreScale = 0.30f, coreLife = 0.10f, cone = true, coneSize = 0.7f, glowSparks = 4, backSmoke = 3,
                        lightLife = 0.13f, lightIntensity = 7f * 0.7f, lightRange = 5f * 0.7f, lightColor = LegLightColor, groundLight = GroundLightBullet };
                case FxWeapon.Bolt:
                    return new FxMuzzleSpec { coreScale = 0.30f, coreLife = 0.10f, cone = true, coneSize = 0.7f, glowSparks = 4, backSmoke = 0, boltDisc = true,
                        lightLife = 0.13f, lightIntensity = 7f * 0.7f, lightRange = 5f * 0.7f, lightColor = Hex("#ffad46"), groundLight = GroundLightBolt };
                case FxWeapon.TankShell:
                case FxWeapon.HeavyShell:
                    return new FxMuzzleSpec { coreScale = 0.46f, coreLife = 0.14f, cone = true, coneSize = 1.2f, glowSparks = 6, backSmoke = 4,
                        lightLife = 0.13f, lightIntensity = 7f * 1.2f, lightRange = 5f * 1.2f, lightColor = LegLightColor, groundLight = GroundLightTank };
                case FxWeapon.Cannon:
                    return new FxMuzzleSpec { coreScale = 0.30f, coreLife = 0.10f, cone = true, coneSize = 1f, glowSparks = 5, backSmoke = 4,
                        lightLife = 0.13f, lightIntensity = 7f, lightRange = 5f, lightColor = LegLightColor, groundLight = GroundLightTank };
                default:
                    return new FxMuzzleSpec { coreScale = 0.30f, coreLife = 0.10f, lightLife = 0.13f, lightIntensity = 4.9f, lightRange = 3.5f, lightColor = LegLightColor, groundLight = GroundLightRocket };
            }
        }
        /// <summary></summary>
        public static readonly Color MuzzleConeColor = Hex("#ffc04d");
        public const float MuzzleConeAlpha = 0.9f, MuzzleConeLifeMin = 0.04f, MuzzleConeLifeMax = 0.18f, MuzzleConeWidth = 0.24f, MuzzleConeLength = 0.85f * 1.45f;
        public static readonly Color MuzzleSparkA = Hex("#ffdf71"), MuzzleSparkB = Hex("#ff7b32"), MuzzleSmoke = Hex("#817d69");
        public static readonly Color BoltMuzzleColor = Hex("#ffad46");

        // ---------------------------------------------------------------------------------------------- tracers
        /// <summary>
        /// box .055 #fff1a1 .95 + sleeve 3.4x #ffad43 .17. M12 bullet 65 m/s: the LEG timing gives 65 m/s at 15 m.</summary>
        public const float TracerLife = 0.23f, TracerMinSpeed = 65f, TracerWidth = 0.055f, TracerAlpha = 0.95f, TracerSleeveWidth = 0.055f * 3.4f, TracerSleeveAlpha = 0.17f;
        public static readonly Color TracerColor = Hex("#fff1a1"), TracerSleeveColor = Hex("#ffad43");
        /// <summary>M12 bolt: 80 m/s, life .55 (or until it reaches the target), head at the particle front,
        /// orange (1,.25,.015) linear with a white core, x5 (channel m12_bolt). Muzzle: boltMuzzle disc #ffad46 x7 .11 s
        /// (drawn on m12_core x12 at alpha 7/12).</summary>
        public const float BoltSpeed = 80f, BoltLife = 0.55f, BoltWidth = 0.2f, BoltLength = 1.8f, BoltAlpha = 1f, BoltMuzzleAlpha = 7f / 12f;
        public static readonly Color BoltColor = new Color(1f, 0.537f, 0.128f);                           // sRGB of linear (1,.25,.015): particle colours are sRGB

        // ---------------------------------------------------------------------------------------------- impact sounds
        /// <summary>
        /// (CombatView already plays it for snipes). Throttled globally: Zod guns fire up to 10 shots/s.</summary>
        public static readonly string[] RicochetClips = { "v3-armor-ricochet-a" };
        public const float RicochetVolume = 0.3f, RicochetThrottle = 0.2f;

        // ---------------------------------------------------------------------------------------------- impacts per surface
        static FxImpactSpec BuildImpact(FxSurface s)
        {
            // light 110 ms 5.5 / 3.2, core 115 ms r .18; M12 ground IMPACT -> dust .45
            switch (s)
            {
                case FxSurface.Metal: case FxSurface.Unit:
                    return new FxImpactSpec { sparkColor = Hex("#ffb34f"), smokeColor = Hex("#55554d"), sparks = 8, dust = 0f,
                        lightIntensity = 5.5f, lightRange = 3.2f, lightLife = 0.11f, coreRadius = 0.18f, coreLife = 0.115f };
                case FxSurface.Road:
                    return new FxImpactSpec { sparkColor = Hex("#ffb34f"), smokeColor = Hex("#55554d"), sparks = 8, dust = 0.25f,
                        lightIntensity = 5.5f, lightRange = 3.2f, lightLife = 0.11f, coreRadius = 0.18f, coreLife = 0.115f };
                case FxSurface.Water:
                    return new FxImpactSpec { splash = true, sparks = 0, dust = 0f, coreRadius = 0f };
                case FxSurface.Rock:
                    return new FxImpactSpec { sparkColor = Hex("#e9d19a"), smokeColor = Hex("#887861"), sparks = 8, dust = 0.35f,
                        lightIntensity = 5.5f, lightRange = 3.2f, lightLife = 0.11f, coreRadius = 0.18f, coreLife = 0.115f };
                default:
                    return new FxImpactSpec { sparkColor = Hex("#e9d19a"), smokeColor = Hex("#887861"), sparks = 8, dust = 0.45f,
                        lightIntensity = 5.5f, lightRange = 3.2f, lightLife = 0.11f, coreRadius = 0.18f, coreLife = 0.115f };
            }
        }
        public static readonly Color ImpactSparkHot = Hex("#fff0a2");
        /// <summary>M12 bolt impact: core .13 / .40, 5 sparks v (sin(2.4 j) 2, 1 + j%2, cos(2.4 j) 2), .24 s, .24.</summary>
        public const float BoltImpactCoreLife = 0.13f, BoltImpactCoreScale = 0.40f, BoltImpactSparkLife = 0.24f, BoltImpactSparkScale = 0.24f;
        /// <summary>Water impacts: splash .2 + ripple .25 (M12 swimmer / small splash scale).</summary>
        public const float WaterImpactSplash = 0.2f;

        // ---------------------------------------------------------------------------------------------- water
        public const float RippleLife = 1.35f, RippleScale = 0.75f, RippleLift = 0.09f;
        /// <summary>
        /// </summary>
        public static Color RippleColorFor(string level) => level == "L02" ? Hex("#a9b6c6") : level == "L17" ? Hex("#9c9c80") : level == "L16" ? Hex("#a59c7a") : Hex("#82b6b9");
        public static Color RippleEmissionFor(string level) => level == "L02" ? Hex("#8fa3b8") : level == "L17" ? Hex("#86866a") : level == "L16" ? Hex("#5c573f") : Hex("#7bc1d0");
        public const float BlastWaterSplash = 1f;                                                          // blast centred on water: splash s
        /// <summary></summary>
        public const float WadeSinkFraction = 0.45f, WadeBlendTime = 0.3f, WadeMinDepth = 0.05f, WadeRampDepth = 0.35f;
        public const float WadeRingMoveEvery = 0.55f, WadeRingMoveStrength = 0.42f, WadeRingIdleEvery = 1.2f, WadeRingIdleStrength = 0.15f;
        public const float WadeRingLift = 0.03f, WadeWakeEvery = 0.25f, WadeWakeStrength = 0.22f, WadeWakeBack = 0.6f, WadeWakeSide = 0.35f, WadeWakeLife = 1.0f;
        public const float WadeStepSplash = 0.2f, WadeStepRipple = 0.18f, WadeStepMinInterval = 0.18f, WadeStepCadenceM = 0.8f;
        public const float WadeEnterSplash = 0.6f, WadeEnterRipple = 0.5f, WadeSwimVolume = 0.3f;
        public const int WadeStepDroplets = 5;                                                             // 4-6 droplets per footfall
        public const int WadeShaderPoints = 16;

        // ---------------------------------------------------------------------------------------------- wrecks, deaths
        /// <summary>
        /// 380-740 ms, (11-20) x scale px -> 2 px, g -.18, drag .96, #ffef9b / #ff9a35 / #ff4d25. Scale: tank 1.35, car 1.1, mech 1.6.</summary>
        public static readonly Color WreckCore = Hex("#ff7b31"), WreckFlameA = Hex("#ffef9b"), WreckFlameB = Hex("#ff9a35"), WreckFlameC = Hex("#ff4d25");
        public const float WreckScaleTank = 1.35f, WreckScaleCar = 1.1f, WreckScaleCannon = 1.2f, WreckScaleMech = 1.6f;
        /// <summary>
        /// </summary>
        public static readonly Color WreckSmokeDark = Hex("#242824"), WreckSmokeGrey = Hex("#55564d"), WreckBurnSmoke = Hex("#343733");
        /// <summary>
        /// #716b59 / #95866a, drift opposite the impulse.</summary>
        public static readonly Color DeathDustA = Hex("#716b59"), DeathDustB = Hex("#95866a");
    }
}
