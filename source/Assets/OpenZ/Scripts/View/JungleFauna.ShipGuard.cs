// OpenZ (GPL-3.0) - Level 16: no parrot ever flies into the shuttle's keep-out.
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Level 16 (rule DV118, view only): the shuttle's keep-out holds for every bird in the air, on every frame. JungleFauna.Ship.cs
    /// pushes the birds' flight targets out of the capsule, but a bird flies a straight line to its target, so its way could cut through
    /// the capsule (a parrot came 0.61 m inside it while the ship flew through the circuit). Now:
    /// <list type="bullet">
    /// <item>the flight targets and the circling parrots' circles are pushed out to the capsule plus <see cref="ShipMarginM"/>;</item>
    /// <item>after each bird's step (<see cref="ShipGuard"/>), a bird closer than <see cref="ShipMarginM"/> to the capsule slides out along
    /// its surface (to its rim, over its top, or under its bottom when there is room over the ground: the nearest), never faster than
    /// 1.5 times <see cref="JungleFauna.ShipFleeSpeed"/>; a bird that came in during one frame is out again in the same frame (the ship
    /// flies 5.25 m/s, a bird 9-14 m/s: its sideways slide is a few centimetres a frame).</item>
    /// </list>
    /// The capsule itself (<see cref="ShipKeepOut"/>, the band) and every other bird rule are unchanged.
    /// </summary>
    public sealed partial class JungleFauna
    {
        /// <summary>The birds' distance (m) kept outside the ship's keep-out capsule.</summary>
        public static float ShipMarginM = 3.5f;
        /// <summary>Proof: slides out of the margin, and the deepest a bird was in it before its slide (m).</summary>
        public static int ShipGuardSlides; public static float ShipGuardDeepest;

        /// <summary>A bird's new place, slid out of the ship's keep-out + margin if it is in it.</summary>
        Vector3 ShipGuard(Vector3 p, float dt)
        {
            ShipNow(p); if (!shipOn) return p;
            float m = ShipMarginM, c = ShipClearance(p);
            if (c >= m) return p;
            float lo = shipAt.y - ShipBandBelow - m, hi = shipAt.y + ShipBandAbove + m;
            var d = p - shipAt; d.y = 0f; float fl = d.magnitude;
            var dir = fl > 0.05f ? d / fl : Vector3.forward;
            float r = ShipKeepOut + m + 0.05f;
            var best = new Vector3(shipAt.x + dir.x * r, p.y, shipAt.z + dir.z * r);
            var top = new Vector3(p.x, hi + 0.05f, p.z);
            if ((top - p).sqrMagnitude < (best - p).sqrMagnitude) best = top;
            if (lo - 0.05f > Mathf.Max(Ground(p.x, p.z), 0f) + 1.5f) { var bot = new Vector3(p.x, lo - 0.05f, p.z); if ((bot - p).sqrMagnitude < (best - p).sqrMagnitude) best = bot; }
            ShipGuardSlides++; ShipGuardDeepest = Mathf.Max(ShipGuardDeepest, m - c);
            return Vector3.MoveTowards(p, best, 1.5f * ShipFleeSpeed * Mathf.Max(dt, 1f / 120f));
        }

        /// <summary>One line for the checks.</summary>
        public static string ShipGuardStatus() => $"the keep-out's margin {ShipMarginM:F1} m: {ShipGuardSlides} slides out of it, the deepest a bird was in it before its slide {ShipGuardDeepest:F2} m";
    }
}
