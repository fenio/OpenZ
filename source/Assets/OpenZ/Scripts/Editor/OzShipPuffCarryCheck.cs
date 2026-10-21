#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>K432 (a flying, burning shuttle's puffs appear on its hull), a counted check out of Play: the switch and its numbers, the
    /// hooks, the beat's moving puff against the effect player's own WreckSmoke numbers, and the channel's motion (a big puff at 9 m/s:
    /// where it appears with and without the hull's velocity). About one second.</summary>
    public static class OzShipPuffCarryCheck
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K432 Puffs On The Hull Check (out of Play)")]
        static void Menu() { var r = Run(); Debug.Log("[OpenZ] K432 check: " + r); }

        static int pass, fail;
        static void Check(string what, bool ok) { if (ok) pass++; else fail++; Debug.Log("[OpenZ] K432 check: " + (ok ? "PASS " : "FAIL ") + what); }
        static string Src(string rel) { var p = System.IO.Path.Combine(Application.dataPath, "OpenZ/Scripts/" + rel); return System.IO.File.Exists(p) ? System.IO.File.ReadAllText(p) : ""; }

        /// <summary>One big puff (ShipPuff: leg_smoke, life 1.81 s, rise 0.75 m/s, gravity -0.12, drag 0.965 a 60th) born at the ship's
        /// point while it flies on at `v` m/s: how far behind its point (in the ship's frame) it is when it is half visible (the channel's
        /// alpha sin(pi x 1.08 t) = 0.5 at 15.43 % of its life).</summary>
        static float Appears(float v, float carry)
        {
            const float dt = 1f / 60f, life = 1.25f * 1.45f, g = -0.12f; float k = -60f * Mathf.Log(0.965f);
            Vector3 p = Vector3.zero, vel = new Vector3(0f, 0.75f, v * carry); float t = 0f;
            while (t < life)
            {
                t += dt; vel.y -= g * dt; vel *= Mathf.Max(0f, 1f - k * dt); p += vel * dt;
                if (t / life >= 0.1543f) return (p - new Vector3(0f, 0f, v * t)).magnitude;
            }
            return float.NaN;
        }

        public static string Run()
        {
            pass = fail = 0;
            Check($"K1 the switch on, smoke x{OpenZDamageStates.ShipSmokeCarry:F2}, embers x{OpenZDamageStates.ShipEmberCarry:F2}, standing under {OpenZDamageStates.ShipCarryMinMps:F2} m/s, capped at {OpenZDamageStates.ShipCarryMaxMps:F0} m/s",
                  OpenZDamageStates.ShipCarryOn && Mathf.Abs(OpenZDamageStates.ShipSmokeCarry - 1f) < 1e-4f && Mathf.Abs(OpenZDamageStates.ShipEmberCarry - 1f) < 1e-4f
                  && OpenZDamageStates.ShipCarryMinMps > 0f && OpenZDamageStates.ShipCarryMaxMps >= 20f);
            string sh = Src("View/FxV3/OpenZDamageStates.Shuttle.cs"), pf = Src("View/FxV3/OpenZDamageStates.ShipPuff.cs");
            Check("K2 the hooks: the velocity read first in the tick, the flame tongue on the hull's velocity, the embers, the beat's small puff and the big puff with it",
                  sh.Contains("ShipCarryBegin(id);") && sh.Contains("var shipFv = ShipFlameVel(sm.fireVel);") && sh.Contains("FlyingFlame(fx, ShipLastPoints[0], shipFv); }")
                  && sh.Contains("+ ShipEmberVel(),") && sh.Contains("ShipWreckSmoke(fx, at, scale, burning);") && pf.Contains("+ ShipPuffCarry();"));
            string fx = Src("View/OpenZFxPlayer.cs");
            Check("K3 the beat's moving small puff is the effect player's own (WreckSmoke: +-0.18 m, up 0.62 m/s, 1.25 s, 7 effect px x the scale, the wreck smoke colour, 0.9)",
                  fx.Contains("EmitP(Ch(\"leg_smoke\"), at + new Vector3(phase * 0.18f, 0f, Mathf.Cos(serial * 2.17f + t * 3.1f) * 0.18f), new Vector3(phase * 0.08f, 0.62f, -phase * 0.06f),")
                  && fx.Contains("1.25f, 7f * px * scale * OpenZFxCatalog.LegSmokeScale, OpenZFxCatalog.WreckBurnSmoke, 0.9f);"));
            float before = Appears(9f, 0f), now = Appears(9f, OpenZDamageStates.ShipSmokeCarry), fast = Appears(12f, OpenZDamageStates.ShipSmokeCarry);
            Check($"K4 a big puff at 9 m/s appears {now:F2} m from its damage point (wanted <= 1.0; 12 m/s: {fast:F2}), without the hull's velocity {before:F2} m (the fault)",
                  now <= 1.0f && fast <= 1.0f && before >= 2.0f);
            return fail == 0 ? $"{pass} PASS" : $"{fail} FAIL of {pass + fail}";
        }
    }
}
#endif
