using System;

namespace OpenZ
{
    /// <summary>
    /// K436: the kickflip's own sounds (the rows shuttle_flip_*) carry over the battle at the RTS camera. On the vehicles' bus every row
    /// falls off on the field curve over 85 m; the flip's rows take the broad curve over 150 m instead (the one of the big collapses):
    /// at the normal 70 m camera the ship flies 10 .. 37 m from the listener, where the field curve took 0 .. -10 dB off and buried the
    /// whoosh under the music, the jungle bed and a fight near the screen's edge. Nothing else changes.
    /// </summary>
    public partial class AudioDirector
    {
        /// <summary>The switch (false = the field curve as before) and the broad curve's range (m at the 70 m camera).</summary>
        public static bool FlipBroad = true;
        public static float FlipBroadRange = 150f;
        public const string FlipRowPrefix = "shuttle_flip_";

        static void K436FlipProfile(SoundEventDef d, ref CombatAudio.Profile prof, ref float range)
        {
            if (!FlipBroad || d == null || d.id == null || !d.id.StartsWith(FlipRowPrefix, StringComparison.Ordinal)) return;
            prof = CombatAudio.Profile.Broad; range = FlipBroadRange;
        }
    }
}
