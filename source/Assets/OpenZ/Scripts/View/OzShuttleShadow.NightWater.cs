using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The landing mark on WATER at night (view only; the decal's water pass, OpenZ/ShuttleMarkDecal _WaterPass 1, queue 2995). By day the
    /// mark reads on water as on the ground (15.9 / 23.8 %). At night it read only 8.5 % (not selected) and 12.8 % (selected) darker on
    /// water against 22-30 % on grass and roads: the night water's screen light is mostly light the decal cannot darken. The level's
    /// fog (OpenZ/AtmosphereFullscreen, a full-screen pass after the transparent queue) marches through the water body down to its bed
    /// (the water writes no depth) and scatters the moon and the ship's lamps into those pixels, and the jets' bloom lies on top; the
    /// decal only darkens what was drawn before it. The off-engine model of the two night water readings (out/twin_night_water.txt)
    /// puts 57-66 % of that light out of the decal's reach (grass and roads 4-10 %).
    /// The water pass now takes more of the light it CAN darken at night: its strength x (1 + (<see cref="WaterNightK"/> - 1) x the night
    /// share), never over <see cref="WaterNightMax"/> and never under the mark's own strength. The ground pass, the day, the red tint's
    /// colour and every timing are unchanged; the mark stays a soft disc (the same shape, the fog still veils it).
    /// </summary>
    public sealed partial class OzShuttleShadow
    {
        /// <summary>The water pass's strength x this at full night (1 = as the ground pass), its most.</summary>
        public static float WaterNightK = 2.2f, WaterNightMax = 0.85f;
        /// <summary>Proof: the water pass's strength in the last frame drawn, frames drawn with the night factor.</summary>
        public static float WaterStrengthNow;
        public static int WaterNightFrames;

        /// <summary>The decal's water pass at night (after the renderer's block): the two passes' own blocks, the water's strength raised.</summary>
        void NightWaterPass(float strength)
        {
            float n = Mathf.Clamp01(NightGrade.NightK);
            float sw = Mathf.Max(strength, Mathf.Min(WaterNightMax, strength * (1f + (WaterNightK - 1f) * n)));
            WaterStrengthNow = sw;
            dr.SetPropertyBlock(dmpb, 0);                                                                       // the drawn surface pass: as the renderer's block
            dmpb.SetFloat(StrengthId, sw); dr.SetPropertyBlock(dmpb, 1);                                       // the water pass
            dmpb.SetFloat(StrengthId, strength);
            if (n > 0.01f) WaterNightFrames++;
        }
    }
}
