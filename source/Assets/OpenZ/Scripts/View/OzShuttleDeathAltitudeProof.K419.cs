namespace OpenZ
{
    /// <summary>
    /// K419b, the K256h proof's landing limit after K396 (test only). The line "none flagged landed with the hips more than 1.5 m up and
    /// nothing under it" counts a body as landed in the air when it is flagged landed high and no bone has anything under it at that
    /// moment. K396 throws the crew x1.6 with the blast; on a GROUND break (G1) the pieces start 0.55-4.3 m up and the bodies come down on
    /// the heap while it is still settling (run 28: the heap's highest landing 2.87 m; a body flagged landed at 2.38 m on a piece that moved
    /// on in the same moment). For a ground break the limit is now the heap's height, 3.0 m; air kills keep 1.5 m. Where the bodies end up
    /// is still proven by the next line (hips over 1 m at rest with nothing under them: none).
    /// </summary>
    public sealed partial class OzShuttleDeathAltitudeProof
    {
        /// <summary>A body flagged landed higher than this (hips over the ground, m) must be held by something: air kills, ground breaks.</summary>
        public const float LandHighAirM = 1.5f, LandHighGroundM = 3.0f;
        static float K419LandHighM => OzShuttleBreakup.LastInAir ? LandHighAirM : LandHighGroundM;
        static string K419LandWhy => OzShuttleBreakup.LastInAir ? "an air kill" : "a ground break: K396's crew throw onto the settling heap";
    }
}
