using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenZ
{
    /// <summary>
    /// The shuttle's downwash as dust raised off the ground it stands on (view only). The round-3 dust was drawn with the fog shader's own
    /// light and a colour worked out from the ground's table colour; on bright sand that light came out at about half the lit sand's
    /// (dark brown blotches), on grass a tan over the green, and its small puffs read as separate discs.
    /// This system draws with OpenZ/GroundDust (Shaders/OzGroundDust.shader, kept in builds by Resources/OpenZ/FX/M_OzGroundDust): the
    /// same smoke flipbook shape as the fort blast fog, but every puff takes its colour from the REAL lit ground round it on screen (the
    /// camera's opaque copy, each tap depth-checked so the hull, a unit or a tree above the puff never counts), pulled toward its soil
    /// when it is planted, a little toward a pale dry tint, lifted over the ground's brightness: light, translucent dust in the ground's own
    /// colour, never darker than the ground under it, by day and at night, on every level, with no light model to keep in step.
    /// What the opaque copy does not hold yet (the jets' ground pool is drawn after it) goes into each puff as a light multiplier, worked
    /// out as the pool lights the ground (<see cref="PoolMul"/>). The puffs are fewer, larger, thinner and lower than round 3's and fade in
    /// later (the middle blown clear, the rolling front thicker), so they overlap into one soft sheet.
    /// Falls back to the fog shader (round 3's way, with these numbers) when the shader, the smoke flipbook or the camera's depth / opaque
    /// copies are missing; <see cref="GroundMatched"/> picks the fog shader on purpose.
    /// LOW graphics: half the puffs (the preset's share) and 7 ground taps a puff instead of 13.
    /// The jets' ground splash (OzShuttleJets) is drawn as short bright blast streaks along the ground instead of sooty fireballs
    /// (<see cref="SplashStreaks"/>): no more small dark-rimmed orange blobs in the dust.
    /// </summary>
    public sealed partial class OzShuttleDownwash
    {
        /// <summary>On: the ground-matched dust (OpenZ/GroundDust). Off: the fog shader (OpenZ/BlastFog) with its own light.</summary>
        public static bool GroundMatched = true;
        /// <summary>On: the jets' ground splash is short bright blast streaks (m12_flame). Off: round 3's fireballs (m12_fire).</summary>
        public static bool SplashStreaks = true;
        /// <summary>The ground-matched dust's look: its brightness over the ground's by day / at night, the pull toward the pale dry tint,
        /// a planted ground's pull toward its soil, the share of the moon's blue cast taken out at night.</summary>
        public static float GroundLift = 1.25f, GroundNightLift = 1.22f, GroundPaleMix = 0.4f, GroundSoilMix = 0.6f, GroundNightWarm = 0.35f;
        /// <summary>The take-off / touchdown burst's puffs are this much wider than the ring's.</summary>
        public static float BurstSizeK = 1.25f;
        /// <summary>The particle colour carries the light multiplier / this scale (it stays under 1 in the colour's 8 bits).</summary>
        public const float MulScale = 8f;
        /// <summary>Proof: the take-off's peak of puffs alive that counts as a full downwash (fewer, larger puffs than round 3's).</summary>
        public const int ProofPeakMin = 80;
        /// <summary>Proof: the system draws the ground-matched dust (else the fog shader: why in <see cref="GroundWhy"/>).</summary>
        public static bool GroundOn;
        public static string GroundWhy = "-";

        static Material groundShared; static bool groundTried;
        static readonly int NightId = Shader.PropertyToID("_Night"), FallbackId = Shader.PropertyToID("_Fallback"), RingsId = Shader.PropertyToID("_Rings"),
            LiftId = Shader.PropertyToID("_Lift"), NightLiftId = Shader.PropertyToID("_NightLift"), PaleMixId = Shader.PropertyToID("_PaleMix"),
            SoilMixId = Shader.PropertyToID("_SoilMix"), NightWarmId = Shader.PropertyToID("_NightWarm"), MulScaleId = Shader.PropertyToID("_MulScale");
        bool ground;

        /// <summary>True when the camera's depth and opaque copies are made (the active pipeline asset, and the main camera's own setting).</summary>
        public static bool SceneCopies(out string why)
        {
            var a = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!a) { why = "no URP asset active"; return false; }
            var cam = Camera.main;
            var data = cam ? cam.GetComponent<UniversalAdditionalCameraData>() : null;
            bool color = data ? data.requiresColorTexture : a.supportsCameraOpaqueTexture;
            bool depth = data ? data.requiresDepthTexture : a.supportsCameraDepthTexture;
            why = color && depth ? "ok" : "the camera's " + (color ? "" : "opaque ") + (color || depth ? "" : "and ") + (depth ? "" : "depth ") + "copy is off (" + a.name + ")";
            return color && depth;
        }

        /// <summary>This system's own copy of the ground dust material, or null (then the fog shader).</summary>
        static Material NewGroundMaterial()
        {
            if (!GroundMatched) { GroundWhy = "switched to the fog shader"; return null; }
            if (!SceneCopies(out var why)) { GroundWhy = why; return null; }
            if (!groundTried)
            {
                groundTried = true;
                var m = Resources.Load<Material>("OpenZ/FX/M_OzGroundDust");
                var sh = m && m.shader ? m.shader : Shader.Find("OpenZ/GroundDust");
                if (sh && sh.isSupported)
                {
                    var atlas = Resources.Load<Texture2D>("OpenZ/FX/Smoke/OpenZ_SmokeAtlas");
                    var detail = Resources.Load<Texture2D>("OpenZ/FX/Smoke/OpenZ_SmokeDetail");
                    if (atlas && detail)
                    {
                        groundShared = new Material(sh) { name = "OzGroundDust", renderQueue = 3007 };           // where the fog dust was drawn
                        groundShared.SetTexture("_SmokeAtlas", atlas); groundShared.SetTexture("_SmokeDetail", detail);
                    }
                    else GroundWhy = "the smoke flipbook is missing";
                }
                else GroundWhy = "shader OpenZ/GroundDust not found or not supported";
                if (!groundShared) Debug.LogWarning("[OpenZ] shuttle dust: " + GroundWhy + ": the fog shader's dust");
            }
            if (!groundShared) return null;
            var mm = new Material(groundShared) { name = "OzShuttleDownwash ground" };
            mm.SetFloat(LiftId, GroundLift); mm.SetFloat(NightLiftId, GroundNightLift); mm.SetFloat(PaleMixId, GroundPaleMix);
            mm.SetFloat(SoilMixId, GroundSoilMix); mm.SetFloat(NightWarmId, GroundNightWarm); mm.SetFloat(MulScaleId, MulScale);
            GroundWhy = "ok";
            return mm;
        }

        /// <summary>The constructor's material: the ground dust when it can be drawn, else the fog shader's copy.</summary>
        Material MakeMaterial()
        {
            var m = NewGroundMaterial(); ground = m; GroundOn = ground;
            return m ? m : OzBlastFog.NewMaterial("OzShuttleDownwash");
        }

        /// <summary>The system's renderer once built: the ground dust's particle colour is a raw multiplier, not an sRGB colour.</summary>
        void SetUpRenderer(ParticleSystemRenderer r)
        {
            if (r && ground) r.applyActiveColorSpace = false;
        }

        /// <summary>Every frame with dust (after the light step): the night share, the fallback ground and the LOW preset's taps.</summary>
        void GroundStep()
        {
            if (!ground || !mat) return;
            mat.SetFloat(NightId, NightGrade.NightK);
            mat.SetFloat(RingsId, OzQuality.Level >= 2 ? 1f : 2f);
            // the lit ground under the ship as the light code sees it (used only where no tap sees the ground)
            SceneLight(sunK0 > 0f ? sunK0 : SunK0, ambK0 > 0f ? ambK0 : AmbK0, wrap0 >= 0f ? wrap0 : Wrap0, out _, out var gl);
            var gc = GroundColour(root.position, out _).linear;
            mat.SetColor(FallbackId, new Color(gc.r * gl.r, gc.g * gl.g, gc.b * gl.b, 1f));
            SteadyStep();                                                                                       // the pool + window numbers (Steady.cs)
        }

        /// <summary>A puff's light multiplier for the ground dust (1 = the opaque copy's light; the jets' pool on top, as it lights the
        /// ground: dst x (1 + light)), / MulScale in the particle colour; srgbAlpha is kept.</summary>
        Color PoolMul(Vector3 at, float srgbAlpha)
        {
            if (ShaderPool) return NeutralMul(srgbAlpha);                                                       // the shader lights it where it is (Steady.cs)
            float share = PoolShare(at) * PoolDustK;
            var m = new Color(1f + poolLin.r * share, 1f + poolLin.g * share, 1f + poolLin.b * share);
            return new Color(Mathf.Min(m.r, MulScale) / MulScale, Mathf.Min(m.g, MulScale) / MulScale, Mathf.Min(m.b, MulScale) / MulScale, srgbAlpha);
        }
    }
}
