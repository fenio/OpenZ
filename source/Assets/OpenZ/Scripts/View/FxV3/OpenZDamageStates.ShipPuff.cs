using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K393 round 3 (view only): the shuttle's damage smoke reads at the normal 70 m camera. The BATCH 4 pictures still showed it hard to
    /// see: the ship's puffs were the other vehicles' damage puff (OpenZFxPlayer.WreckSmoke: the leg_smoke channel, 7 effect px x the scale,
    /// growing x3.2 over 1.25 s), at the ship's scale 2.16 only 0.43 m wide when born and 1.4 m at the end: about 14 to 45 px at 70 m, a
    /// thin dotted line behind an 11 m ship (run 28 K393_air_smoke_70m).
    /// Now every shuttle smoke puff is the SAME effect (the leg_smoke channel: its flipbook, billows, light and soft edges, its sorting)
    /// sized to the ship: x<see cref="ShipPuffK"/> wider (1.1 m born, 3.6 m at the end: 37 to 117 px at 70 m), x<see cref="ShipPuffLifeK"/>
    /// longer (1.8 s), a darker grey (battered <see cref="ShipSmokeBattered"/>, burning the wreck fire's darkest smoke
    /// OpenZFxCatalog.WreckSmokeDark), rising a little faster. On the beat the others' call stays (its small puff, the flame tongue and the
    /// fire's flicker light when burning) and the big puff is added on top; the flight's trail is laid with the big puffs only (one each
    /// <see cref="TrailSpacingM"/> of a point's way, as before): a dense grey ribbon that widens behind the ship instead of a dotted line.
    /// The thresholds, points, sounds and LOW thinning are unchanged.
    /// </summary>
    public partial class OpenZDamageStates
    {
        /// <summary>The ship's smoke puff against the others' (x size, x life), its opacity and rise (m/s).</summary>
        public static float ShipPuffK = 2.6f, ShipPuffLifeK = 1.45f, ShipPuffAlpha = 0.9f, ShipPuffRise = 0.75f;
        /// <summary>The battered ship's smoke (sRGB): a darker grey than the others' #343733.</summary>
        public static Color ShipSmokeBattered = new Color(0.173f, 0.180f, 0.165f);
        /// <summary>Proof: the ship's big puffs laid (beat + trail).</summary>
        public static int ShipBigPuffs;

        /// <summary>One shuttle smoke puff of the others' damage smoke, sized to the ship.</summary>
        static void ShipPuff(OpenZFxPlayer fx, Vector3 at, float scale, bool burning)
        {
            if (!fx) return;
            float px = OpenZFxCatalog.LegPxToM, size = 7f * px * scale * OpenZFxCatalog.LegSmokeScale * ShipPuffK;
            var j = new Vector3(Random.Range(-0.2f, 0.2f), 0f, Random.Range(-0.2f, 0.2f)) * size;
            var v = new Vector3(Random.Range(-0.12f, 0.12f), ShipPuffRise * Random.Range(0.85f, 1.15f), Random.Range(-0.12f, 0.12f)) + ShipPuffCarry();   // K432: with the hull's velocity
            fx.EmitChannel("leg_smoke", at + j, v, 1.25f * ShipPuffLifeK * Random.Range(0.9f, 1.1f), size * Random.Range(0.9f, 1.1f),
                           burning ? OpenZFxCatalog.WreckSmokeDark : ShipSmokeBattered, ShipPuffAlpha);
            ShipBigPuffs++;
        }
    }
}
