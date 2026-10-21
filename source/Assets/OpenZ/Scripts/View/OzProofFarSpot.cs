#if UNITY_EDITOR
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K419b, the kickflip proofs' far landing spot (test only; K344 / K390 Kickflip Proof, K411 Kickflip-by-itself Proof). The far
    /// stages want a free landing spot 42-60 m from the landed ship and click it on screen. From the level-start view (the camera on
    /// the ship at 70 m, FOV 32 deg, pitch 55 deg) the click box (0.18-0.82 across, 0.30-0.82 up: clear of the HUD panels) reaches only
    /// about 25 m to the sides of the ship, so run 27D and run 28 found no spot at all (:
    /// k1, k2, k4, k5, k6 never flew).
    /// <para><see cref="Frame"/>: every free, landable spot 42-60 m round the ship (9 rings x 48 bearings, the rules' own pad test and
    /// the view's landable test, as the proofs did), then the camera is put over the middle between the ship and the spot, at 70 m
    /// first and higher (80, 90, 100, 115 m: RTS heights, high and wide) only when 70 m cannot hold both; the first spot that sits in the
    /// click box with the ship in view is taken and its screen point returned. The camera stays there for the click (the proof sets its
    /// own height and follow right after the order).</para>
    /// </summary>
    public static class OzProofFarSpot
    {
        /// <summary>The click box (viewport) the spot must sit in, and the looser box the ship must sit in.</summary>
        public const float SpotX0 = 0.18f, SpotX1 = 0.82f, SpotY0 = 0.30f, SpotY1 = 0.82f, ShipX0 = 0.06f, ShipX1 = 0.94f, ShipY0 = 0.22f, ShipY1 = 0.94f;
        /// <summary>Camera heights tried (m), lowest first.</summary>
        public static readonly float[] Heights = { 70f, 80f, 90f, 100f, 115f };
        /// <summary>Proof counts: spots framed, searches with no spot; the last framing's height and distance.</summary>
        public static int Framed, NotFound; public static float LastHeight, LastDistM;

        static bool In(Vector3 vp, float x0, float x1, float y0, float y1) => vp.z > 0f && vp.x >= x0 && vp.x <= x1 && vp.y >= y0 && vp.y <= y1;

        /// <summary>A free landing spot minM..maxM from the landed ship s, framed with the ship (the camera moved); px = the spot, screen =
        /// its screen point now. False: none anywhere round it (the camera back on the ship at 70 m).</summary>
        public static bool Frame(ZWorld W, ZGame g, ZObject s, RtsCamera rts, Camera cam, float minM, float maxM, out Vector2Int px, out Vector2 screen, System.Action<string> log)
        {
            px = default; screen = default;
            if (W == null || !g || s == null || !rts || !cam) { log?.Invoke("far spot (K419b): no world, ship or camera"); NotFound++; return false; }
            var shipW = ZGame.PxToWorld(s.CenterX, s.CenterY); shipW.y = g.GroundY(shipW.x, shipW.z);
            var cands = new List<(Vector2Int q, Vector3 wp, float d)>(); var seen = new HashSet<long>();
            int padRefused = 0, viewRefused = 0;
            for (int ring = 0; ring < 9; ring++)
                for (int a = 0; a < 48; a++)
                {
                    float r = Mathf.Lerp(minM, maxM, ring / 8f) * 8f, t = a * Mathf.PI / 24f;
                    var c = W.ShuttleClamp(s.CenterX + (int)(Mathf.Cos(t) * r), s.CenterY + (int)(Mathf.Sin(t) * r));
                    if (!seen.Add(((long)c.x << 32) ^ (uint)c.y)) continue;
                    float d = Vector2.Distance(new Vector2(c.x, c.y), new Vector2(s.CenterX, s.CenterY)) / 8f;
                    if (d < minM - 0.5f || d > maxM + 0.5f) continue;                                          // moved by the map's edge
                    if (!W.ShuttlePadFree(c.x, c.y, s, out _)) { padRefused++; continue; }
                    var wp = ZGame.PxToWorld(c.x, c.y); wp.y = g.GroundY(wp.x, wp.z);
                    if (!OzPlayableShuttle.LandableAt(W, s, wp, out _)) { viewRefused++; continue; }
                    cands.Add((new Vector2Int(c.x, c.y), wp, d));
                }
            if (cands.Count == 0)
            {
                NotFound++;
                log?.Invoke($"far spot (K419b): no free landable spot {minM:F0} .. {maxM:F0} m round the ship anywhere (pad refused {padRefused}, landing refused {viewRefused})");
                return false;
            }
            rts.FreeLookOff(); rts.follow = null;
            foreach (var h in Heights)
                foreach (var c in cands)
                {
                    rts.focus = new Vector3((shipW.x + c.wp.x) * 0.5f, 0f, (shipW.z + c.wp.z) * 0.5f);
                    rts.SetHeightImmediate(h);                                                                  // places the camera now
                    if (!In(cam.WorldToViewportPoint(c.wp), SpotX0, SpotX1, SpotY0, SpotY1) || !In(cam.WorldToViewportPoint(shipW), ShipX0, ShipX1, ShipY0, ShipY1)) continue;
                    var sp = cam.WorldToScreenPoint(c.wp);
                    px = c.q; screen = new Vector2(sp.x, sp.y); LastHeight = h; LastDistM = c.d; Framed++;
                    log?.Invoke($"far spot (K419b): px ({c.q.x}, {c.q.y}), {c.d:F1} m from the ship, framed with it from {h:F0} m (camera over their middle; {cands.Count} free landable spots round the ship)");
                    return true;
                }
            NotFound++;
            rts.focus = new Vector3(shipW.x, 0f, shipW.z); rts.SetHeightImmediate(70f);
            log?.Invoke($"far spot (K419b): {cands.Count} free landable spots {minM:F0} .. {maxM:F0} m round the ship, none framed with it up to {Heights[Heights.Length - 1]:F0} m");
            return false;
        }
    }
}
#endif
