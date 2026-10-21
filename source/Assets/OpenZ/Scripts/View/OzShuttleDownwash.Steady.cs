using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The ground dust's steady colour (view only; OzShuttleDownwash.Ground.cs has the look and the shader). The dust flickered at the
    /// take-off and the landing: every puff took its colour each frame from 13 single pixels of the camera's opaque copy round its own
    /// screen point, and as the puffs move a few pixels a frame those taps landed on new pixels every frame (one bright lamp or glow
    /// pixel could take a whole puff). The shader now reads the ground through a smooth window over a lattice fixed in the world, and
    /// takes the jets' ground pool light at each puff's place NOW from four material numbers set here every frame with dust (the pool's
    /// light, its centre and yaw, its footprint and share; the window's width and the brightest ground a read may count).
    /// The particle colour then carries no pool light of its own (<see cref="ShaderPool"/>): before, the light was baked in at the birth
    /// for a guessed mid-life place, so two overlapping puffs could carry very different light and a swap of the distance sort popped.
    /// </summary>
    public sealed partial class OzShuttleDownwash
    {
        /// <summary>On: the shader lights each puff by the jets' pool where it is now (the particle colour neutral). Off: the pool light
        /// is baked into the particle colour at its birth (the K256i way).</summary>
        public static bool ShaderPool = true;
        /// <summary>The ground window (x the taps' ring) and the brightest ground a read may count (linear luminance).</summary>
        public static float GroundWinK = 1.4f, GroundTapMaxLum = 1f;
        /// <summary>The steady dust's night lift over <see cref="GroundNightLift"/>: the flickering dust's single bright taps lifted its
        /// night brightness as the eye reads it (the off-engine twin: the per-pixel median over time 2-5 of 255 above the steady dust);
        /// this keeps the night dust as light as it looked. By day nothing changes (the twin: within 1 of 255).</summary>
        public static float SteadyNightK = 1.12f;
        static readonly int PoolLinId = Shader.PropertyToID("_PoolLin"), PoolCentreId = Shader.PropertyToID("_PoolCentre"),
            PoolShapeId = Shader.PropertyToID("_PoolShape"), WinKId = Shader.PropertyToID("_WinK"), TapMaxLumId = Shader.PropertyToID("_TapMaxLum");

        /// <summary>Every frame with dust (GroundStep, after the light step): the pool's numbers and the window's.</summary>
        void SteadyStep()
        {
            if (!ground || !mat) return;
            mat.SetFloat(WinKId, GroundWinK); mat.SetFloat(TapMaxLumId, GroundTapMaxLum);
            mat.SetFloat(NightLiftId, GroundNightLift * SteadyNightK);
            bool on = ShaderPool && poolOn;
            mat.SetVector(PoolLinId, on ? new Vector4(poolLin.r, poolLin.g, poolLin.b, 1f) : Vector4.zero);
            mat.SetVector(PoolCentreId, new Vector4(poolAt.x, poolAt.y, poolAt.z, root.eulerAngles.y * Mathf.Deg2Rad));
            mat.SetVector(PoolShapeId, new Vector4(PoolAcrossM, PoolAlongM, PoolSoft, PoolDustK));
        }

        /// <summary>The particle colour's multiplier while the shader takes the pool itself: 1 (/ MulScale); srgbAlpha kept.</summary>
        static Color NeutralMul(float srgbAlpha) => new Color(1f / MulScale, 1f / MulScale, 1f / MulScale, srgbAlpha);
    }
}
