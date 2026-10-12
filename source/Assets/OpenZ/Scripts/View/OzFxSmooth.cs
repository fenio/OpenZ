using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Motion smoothing of the game's own effects, switched by the MOTION BLUR option (UnitLookMotionGate.MotionBlurOn).
    /// The engine's motion blur only sees objects that write motion vectors. Shells, rockets, tracers, smoke and fire are drawn
    /// by the effect code (direct mesh draws and particle quads) and never do, so while the option is on the effect code smooths
    /// them itself:
    /// <list type="bullet">
    /// <item>shells and rockets (OpenZFxPlayer.DrawBlurTail): a soft tail along the flight in the body's own colour plus a
    /// longer glowing one from the hot rear; length = speed x <see cref="Shutter"/>, capped</item>
    /// <item>tracers and bolts (the stretched particle channels): longer with speed (<see cref="StreakVelocityScale"/>)</item>
    /// <item>sparks (<see cref="SparkChannels"/> and the hit sparks): each one a streak along its own velocity with a round head
    /// and a tail that fades to nothing (<see cref="SparkSeconds"/>, <see cref="SparkFade"/>); a spark at rest stays a point</item>
    /// <item>smoke and fire particles (OpenZ/FxAdditive, global _OZFxSmear): each quad is drawn out along its own velocity and
    /// thinned by the same share; smoke fades in at birth instead of popping</item>
    /// <item>the big puffs of blasts (<see cref="PathBlurChannels"/> and the mushroom cloud of a fort's death): not stretched but
    /// blurred along their own path by the puff shader (_SmearPath): soft leading and trailing edges, nothing behind them touched</item>
    /// <item>flame sprites of the flame pool (OpenZ/PyroFlame): the flipbook blends from frame to frame instead of stepping</item>
    /// </list>
    /// Option off: none of this runs and the picture is the same as without it. Nothing here is a full-screen pass and nothing
    /// touches units or the ground.
    /// </summary>
    public static class OzFxSmooth
    {
        /// <summary>The one switch: the MOTION BLUR option.</summary>
        public static bool On => UnitLookMotionGate.MotionBlurOn;

        // ---- shells and rockets: the tail
        /// <summary>Tail length in frames of flight (5 frames at 60 fps = 0.083 s: a 30 m/s shell gets 2.5 m, a 15 m/s rocket 1.25 m).</summary>
        public const float TailFrames = 5f;
        /// <summary>Longest tail (m) and longest tail in body widths; slower than TailMinSpeed (m/s) = no tail.</summary>
        public const float TailMaxM = 4f, TailMaxWidths = 12f, TailMinSpeed = 6f;
        /// <summary>Opacity of the body-coloured tail where it starts (it fades to nothing at its end).</summary>
        public const float TailAlpha = 0.9f;
        /// <summary>Brightness of the glowing tail behind a shell's hot rear / behind a rocket's flame (x the tracer / flame colour).</summary>
        public const float TailTracerGain = 1f, TailExhaustGain = 2f;
        /// <summary>Tail colour behind a rocket body (linear, lit like the body).</summary>
        public static readonly Color RocketTail = new Color(0.6f, 0.6f, 0.58f, 1f);
        /// <summary>Fort gun and howitzer shells: body width as a share of the body length (3.2 to 3.7 calibres long).</summary>
        public const float HeavyShellWide = 0.29f;

        // ---- tracers, bolts, sparks: stretched particle channels
        /// <summary>Extra length per speed (s): a 60 m/s tracer gets 1.2 m more, a 10 m/s spark 0.2 m.</summary>
        public const float StreakVelocityScale = 0.02f;

        // ---- sparks: the effect player's spark channels and the hit sparks
        /// <summary>The effect player's spark channels (explosion sparks and embers, muzzle, impact and track sparks, the sparks
        /// of lightning, lasers, lamps and burnt-out wrecks). With the option on each spark is a stretched billboard along its
        /// own velocity, drawn by OpenZ/FxAdditive with _StreakBlur: a round head, a tail that fades to nothing.</summary>
        public static readonly string[] SparkChannels = { "m12_spark", "wv2_spark", "leg_spark", "leg_spark_light", "leg_impact_spark", "leg_ember" };
        /// <summary>Streak length per speed (s): 0.11 s = 6.6 frames at 60 fps. A 10 m/s spark gets 1.1 m, a 3 m/s one 0.33 m, a
        /// spark at rest stays a point; the shader ends a streak at 24 widths.</summary>
        public static float SparkSeconds = 0.11f;
        /// <summary>How fast a streak fades from its head to its tail end (power: 1 = evenly, higher = a shorter bright part).</summary>
        public static float SparkFade = 1.5f;
        /// <summary>Length of a spark at rest in widths (1 = a round point).</summary>
        public const float SparkRest = 1f;
        /// <summary>Welding and repair sparks while the option is on: their colour (sRGB) and strength on the spark channel.</summary>
        public static readonly Color WeldSpark = new Color(1f, 0.54f, 0.11f);
        public const float WeldSparkAlpha = 0.6f;

        // ---- smoke and fire particles (shader)
        /// <summary>Shutter (s) for particle quads; x SmearSmoke for smoke and dust, x SmearFire for fire, glow and embers.</summary>
        public const float SmearShutter = 0.07f, SmearSmoke = 2f, SmearFire = 4f;
        /// <summary>The big puffs of blasts are blurred along the way they travel during an exposure, at most 0.3 of their
        /// size (the puff shader's own limit); a puff that stands still is drawn as without it. The exposure is SmearShutter x SmearBigPuff for the mushroom
        /// cloud of a fort's death (0.28 s: a puff at 5 m/s is blurred over 1.4 m) and SmearShutter x SmearBlastPuff for the
        /// slower blast puffs of the channels below (0.42 s: a puff at 2 m/s is blurred over 0.84 m).</summary>
        public const float SmearBigPuff = 4f, SmearBlastPuff = 6f;
        /// <summary>The effect player's puff channels that are blurred along their path instead of stretched: blast smoke columns,
        /// demolition and blast dust, the smoke and the dust of the newer explosions.</summary>
        public static readonly string[] PathBlurChannels = { "blast_smoke", "blast_smoke_b", "blast_smoke_c", "m12_dust", "e2_smoke", "e2_dust" };
        /// <summary>A quad is never drawn out by more than this share of its own size.</summary>
        public const float SmearMax = 1f;
        /// <summary>Fast particles (the fireballs and puffs of a big blast in their first half second): from SmearFastFrom m/s the
        /// longest stretch grows, at SmearFastFull m/s it is SmearMaxFast of the particle's size. Slow smoke stays on SmearMax.</summary>
        public const float SmearFastFrom = 5f, SmearFastFull = 14f, SmearMaxFast = 2.5f;
        /// <summary>Smoke fades in over this share of its life (0 = pops in as with the option off).</summary>
        public const float SoftBirth = 0.06f;

        /// <summary>Proof only: a fixed frame time (s) for the tail length, so a pair of pictures does not depend on the stall a
        /// screenshot causes. 0 = the live frame time.</summary>
        public static float FrameTimeOverride = 0f;

        /// <summary>Seconds of flight a tail covers this frame (frame time clamped to 1/120 .. 1/30 s, so a hitch or a pause
        /// never throws a long streak).</summary>
        public static float Shutter => (FrameTimeOverride > 0f ? FrameTimeOverride : Mathf.Clamp(Time.unscaledDeltaTime, 1f / 120f, 1f / 30f)) * TailFrames;
    }
}
