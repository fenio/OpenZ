using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K420, the route proofs' landing points (test only; K410 Route Proof, K387 Route Proof). Their fixed points on level 16 can no longer
    /// be landed on: P0 (118, -82) lies over water and C (180, -82) (K387's A) has closed tiles in its footprint ("blocked"); the rules then
    /// hover over the goal as they should, so no landing came and Q2 / Q5 / Q6 / R11 failed (the headless
    /// rules give the same: out/hl_live_k410_1.txt). <see cref="Near"/> moves such a point to the nearest spot the ship can land on (the rules'
    /// ShuttleLandSpot on the ship's heading and the view's landing test, rings of 2 m out to maxM), so each route still ends with a landing.
    /// Points in the air (the middle points) are not moved.
    /// </summary>
    public static class OzProofLandSpot
    {
        /// <summary>The proofs' px of a world point (x, z) (their own Px: the tile's middle).</summary>
        public static int Px(float m) => Mathf.Clamp(Mathf.FloorToInt(m * 8f / 16f), 1, 400) * 16 + 8;

        /// <summary>The nearest landable spot to the world point w (x, z), at most maxM away; w itself when it is landable or none is found.</summary>
        public static Vector2 Near(ZWorld W, ZObject s, Vector2 w, float maxM, System.Action<string> log, string name)
        {
            if (W == null || s == null) return w;
            int x0 = Px(w.x), y0 = Px(-w.y);
            for (float r = 0f; r <= maxM; r += 2f)
                for (int k = 0; k < (r == 0f ? 1 : 24); k++)
                {
                    float a = k * Mathf.PI / 12f;
                    var c = W.ShuttleClamp(x0 + Mathf.RoundToInt(Mathf.Cos(a) * r * 8f), y0 + Mathf.RoundToInt(Mathf.Sin(a) * r * 8f));
                    if (!W.ShuttleLandSpot(c.x, c.y, s, s.ShuttleHeadingDeg, out _, out _)) continue;
                    var g = ZGame.I; var wp = ZGame.PxToWorld(c.x, c.y); if (g) wp.y = g.GroundY(wp.x, wp.z);
                    if (!OzPlayableShuttle.LandableAt(W, s, wp, out _)) continue;
                    var v = new Vector2(c.x / 8f, -c.y / 8f);
                    string why0 = "";
                    if (r > 0f) W.ShuttleLandSpot(x0, y0, s, s.ShuttleHeadingDeg, out _, out why0);
                    log?.Invoke(r == 0f ? $"landing point {name} (K420): ({w.x:F0}, {w.y:F0}) can be landed on as it is"
                                        : $"landing point {name} (K420): ({w.x:F0}, {w.y:F0}) cannot be landed on ('{why0}'): moved {r:F0} m to ({v.x:F1}, {v.y:F1})");
                    return v;
                }
            log?.Invoke($"landing point {name} (K420): no landable spot within {maxM:F0} m of ({w.x:F0}, {w.y:F0}); kept as it is");
            return w;
        }
    }
}
