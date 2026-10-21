using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K432 (a flying, burning shuttle's fire and smoke puffs showed up far outside its body). View only, the rules are untouched.
    /// <list type="bullet">
    /// <item><b>Why:</b> the ship's damage puffs (OpenZDamageStates.Shuttle.cs, ShipPuff.cs) are the ground vehicles' own puffs: leg_smoke
    /// (the beat's small puff, the ship's big puff and the flight trail), leg_spark (the embers) and leg_fire (the flame tongue). These
    /// channels FADE IN (alpha sin(pi t): 0 at birth, one half at 15 % of the life, full at 46 %), which is right for a tank that stands or
    /// crawls. The ship laid them at rest in the world while it flew on, so a puff was still invisible on the hull and showed only once
    /// the ship had left it behind: the smoke 2.6 m (9 m/s) to 3.5 m (12 m/s) from its damage point when it appeared and 7-10 m at its
    /// brightest, to the side when the ship glides away from fire; the embers 1-1.7 m away, falling. The flame tongue took the pod's
    /// frame-to-frame speed instead, which a kickflip's spin throws to 40 m/s: tongues flung 4-8 m off the hull.</item>
    /// <item><b>Now:</b> every puff the ship lays while it moves leaves with the hull's own drawn velocity (<see cref="OzPlayableShuttle.Velocity"/>:
    /// its flight, glide, climb and sink, never its spin), and the channel's own air drag lets it fall back. It appears at its damage
    /// point on the hull and streams back into the trail at the ship's speed. The flame tongue rides on the hull's velocity x FlameCarry
    /// (1.3) as before, never on a spin. Standing (under <see cref="ShipCarryMinMps"/>) every call is exactly the old one. Looks, sizes,
    /// colours, counts, beats, points, the LOW thinning and every sound are unchanged.</item>
    /// <item><b>Test switch:</b> <see cref="ShipCarryOn"/> = false = the old puffs (the Play proof's BEFORE run).</item>
    /// </list>
    /// Hooks: OpenZDamageStates.Shuttle.cs (ShipTick reads the velocity once; the embers, the flame tongue and the beat's small puff
    /// take it) and ShipPuff.cs (the big puff and the trail take it). Play proof: View/OzShipPuffCarryProof.cs.
    /// </summary>
    public partial class OpenZDamageStates
    {
        /// <summary>On: the ship's puffs leave with the hull's velocity. Off: the old puffs (a test switch).</summary>
        public static bool ShipCarryOn = true;
        /// <summary>The share of the hull's velocity a smoke puff (small, big, trail) and an ember leave with.</summary>
        public static float ShipSmokeCarry = 1f, ShipEmberCarry = 1f;
        /// <summary>Under this hull speed (m/s) the ship counts as standing: the old calls exactly. Above this the velocity is capped.</summary>
        public const float ShipCarryMinMps = 0.25f, ShipCarryMaxMps = 40f;
        /// <summary>This tick's hull velocity (m/s, world; zero standing or with the switch off) and what a smoke puff / an ember takes.</summary>
        public static Vector3 ShipHullVel { get; private set; }
        public static Vector3 ShipCarryVel { get; private set; }
        public static Vector3 ShipEmberCarryVel { get; private set; }
        /// <summary>Proof: big puffs, small puffs and embers laid with the hull's velocity; flame tongues on the hull's velocity; the top speed carried.</summary>
        public static int ShipCarriedPuffs, ShipCarriedSmallPuffs, ShipCarriedEmbers, ShipHullFlames;
        public static float ShipCarrySpeedMax;
        public static void ShipCarryProofReset() { ShipCarriedPuffs = ShipCarriedSmallPuffs = ShipCarriedEmbers = ShipHullFlames = 0; ShipCarrySpeedMax = 0f; }
        static int shipWreckSerial;

        /// <summary>ShipTick, first: the hull's drawn velocity for this ship's puffs in this tick.</summary>
        static void ShipCarryBegin(int id)
        {
            var v = Vector3.zero;
            if (ShipCarryOn) { var s = OzPlayableShuttle.OfId(id); if (s) v = s.Velocity; }
            if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || v.sqrMagnitude < ShipCarryMinMps * ShipCarryMinMps) v = Vector3.zero;
            v = Vector3.ClampMagnitude(v, ShipCarryMaxMps);
            ShipHullVel = v; ShipCarryVel = v * ShipSmokeCarry; ShipEmberCarryVel = v * ShipEmberCarry;
            if (v.sqrMagnitude > 0f) ShipCarrySpeedMax = Mathf.Max(ShipCarrySpeedMax, v.magnitude);
        }

        /// <summary>ShipPuff: the velocity its big puff takes (counted when it moves).</summary>
        static Vector3 ShipPuffCarry() { if (ShipCarryVel.sqrMagnitude > 0f) ShipCarriedPuffs++; return ShipCarryVel; }

        /// <summary>The embers: the velocity they take (counted when it moves).</summary>
        static Vector3 ShipEmberVel() { if (ShipEmberCarryVel.sqrMagnitude > 0f) ShipCarriedEmbers++; return ShipEmberCarryVel; }

        /// <summary>The flame tongue's speed (and the standing / flying choice): the hull's velocity, never the pod's frame-to-frame speed
        /// (a spin is not a flight). The switch off: the pod's, as before.</summary>
        static Vector3 ShipFlameVel(Vector3 podVel)
        {
            if (!ShipCarryOn) return podVel;
            if (ShipHullVel.sqrMagnitude >= 2.25f) ShipHullFlames++;
            return ShipHullVel;
        }

        /// <summary>The beat's small puff: standing, or with the fire (the slow path), the others' own call; moving, the same puff
        /// (OpenZFxPlayer.WreckSmoke's leg_smoke: +-0.18 m, up 0.62 m/s, 1.25 s, 7 effect px x the scale, the wreck smoke colour, 0.9)
        /// leaving with the hull's velocity.</summary>
        static void ShipWreckSmoke(OpenZFxPlayer fx, Vector3 at, float scale, bool burning)
        {
            if (burning || ShipCarryVel.sqrMagnitude <= 0f) { fx.WreckSmoke(at, scale, burning); return; }
            float px = OpenZFxCatalog.LegPxToM, t = Time.time, ph = Mathf.Sin(shipWreckSerial * 1.91f + t * 3.7f);
            fx.EmitChannel("leg_smoke", at + new Vector3(ph * 0.18f, 0f, Mathf.Cos(shipWreckSerial * 2.17f + t * 3.1f) * 0.18f),
                           new Vector3(ph * 0.08f, 0.62f, -ph * 0.06f) + ShipCarryVel, 1.25f, 7f * px * scale * OpenZFxCatalog.LegSmokeScale,
                           OpenZFxCatalog.WreckBurnSmoke, 0.9f);
            shipWreckSerial++; ShipCarriedSmallPuffs++;
        }
    }
}
