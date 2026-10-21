#if UNITY_EDITOR
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K420, the kickflip proof's close stage (K344 k2, camera 35 m; test only). The flip starts only with its whole arc in view (round 2:
    /// the centre, the end of the flip 13 m ahead, the top 5.6 m up, both sides; OzPlayableShuttle.KickArcOnScreen). At 35 m a camera
    /// FOLLOWING the ship (centred on the ground under it) never holds that arc: the ship at its cruise height already sits near the top
    /// edge and the end point is off screen, so the armed flip waited until the ship had landed ("state Landed", run 28). The off-engine
    /// count of the gate (out/twin_flip_gate.txt): at 35 m it opens only with the camera aimed at the flip's airborne middle and the
    /// flight across the screen. The rule is as designed; the stage was wrong.
    /// <list type="bullet">
    /// <item><see cref="Across"/>: the far spot (as OzProofFarSpot.Frame: free and landable, 42-60 m, framed with the ship from 70 m and
    /// up for the click) only along the screen's width (within 30 deg of the camera's right or left).</item>
    /// <item><see cref="For"/>: an aim point the camera follows (RtsCamera.follow): the ground point under the view ray through the flip's
    /// middle in the air (the ship's pivot + its velocity x half the flip's time, half the arc's rise up), updated every frame before the
    /// camera places itself.</item>
    /// </list>
    /// </summary>
    public sealed class OzProofFlipAim : MonoBehaviour
    {
        OzPlayableShuttle ship; RtsCamera rts; Transform aim;
        /// <summary>Proof: frames the aim was moved.</summary>
        public static int Frames;

        /// <summary>The aim point's transform for that ship (made on the proof's object once; follow it with rts.follow).</summary>
        public static Transform For(GameObject host, OzPlayableShuttle ship, RtsCamera rts)
        {
            var a = host.GetComponent<OzProofFlipAim>(); if (!a) a = host.AddComponent<OzProofFlipAim>();
            a.ship = ship; a.rts = rts;
            if (!a.aim) a.aim = new GameObject("K420_FlipAim").transform;
            a.Move();
            return a.aim;
        }

        void Update() => Move();
        void OnDestroy() { if (aim) Destroy(aim.gameObject); }

        void Move()
        {
            if (!ship || !aim || !rts || ship.Obj == null) return;
            var c = ship.transform.TransformPoint(OzPlayableShuttle.KickPivot);
            var v = ship.Velocity; v.y = 0f;
            if (v.magnitude < 1f)
            {   // not cruising yet: toward its goal at the cruise speed
                var o = ship.Obj; var goal = ZGame.PxToWorld(o.ShuttleGoalX, o.ShuttleGoalY); var d = goal - c; d.y = 0f;
                v = d.sqrMagnitude > 1e-3f ? d.normalized * (float)(o.RealMoveSpeed / 8.0) : Vector3.zero;
            }
            var mid = c + v * (0.5f * (float)OzKickflipCurve.TotalSec) + Vector3.up * (0.5f * (float)(OzKickflipCurve.ArcRiseM + OzKickflipCurve.HopPeakM));
            var g = ZGame.I; float gy = g ? g.GroundY(mid.x, mid.z) : 0f;
            var fwd = Quaternion.Euler(0f, rts.yaw, 0f) * Vector3.forward;
            float back = Mathf.Max(0f, mid.y - gy) / Mathf.Tan(Mathf.Max(10f, rts.pitch) * Mathf.Deg2Rad);
            aim.position = new Vector3(mid.x, gy, mid.z) + fwd * back;
            Frames++;
        }

        /// <summary>OzProofFarSpot.Frame with the flight across the screen: a free landable spot minM..maxM from the landed ship within
        /// 30 deg of the camera's right / left, framed with the ship (70 m and up) for the click.</summary>
        public static bool Across(ZWorld W, ZGame g, ZObject s, RtsCamera rts, Camera cam, float minM, float maxM, out Vector2Int px, out Vector2 screen, System.Action<string> log)
        {
            px = default; screen = default;
            if (W == null || !g || s == null || !rts || !cam) { log?.Invoke("far spot across (K420): no world, ship or camera"); return false; }
            var shipW = ZGame.PxToWorld(s.CenterX, s.CenterY); shipW.y = g.GroundY(shipW.x, shipW.z);
            var right = Quaternion.Euler(0f, rts.yaw, 0f) * Vector3.right;
            var cands = new List<(Vector2Int q, Vector3 wp, float d)>(); var seen = new HashSet<long>();
            for (int ring = 0; ring < 9; ring++)
                for (int a = 0; a < 48; a++)
                {
                    float r = Mathf.Lerp(minM, maxM, ring / 8f) * 8f, t = a * Mathf.PI / 24f;
                    var c = W.ShuttleClamp(s.CenterX + (int)(Mathf.Cos(t) * r), s.CenterY + (int)(Mathf.Sin(t) * r));
                    if (!seen.Add(((long)c.x << 32) ^ (uint)c.y)) continue;
                    float d = Vector2.Distance(new Vector2(c.x, c.y), new Vector2(s.CenterX, s.CenterY)) / 8f;
                    if (d < minM - 0.5f || d > maxM + 0.5f) continue;
                    var wp = ZGame.PxToWorld(c.x, c.y); wp.y = g.GroundY(wp.x, wp.z);
                    var dir = wp - shipW; dir.y = 0f;
                    if (dir.sqrMagnitude < 1e-3f || Mathf.Abs(Vector3.Dot(dir.normalized, right)) < 0.866f) continue;   // across the screen only
                    if (!W.ShuttlePadFree(c.x, c.y, s, out _) || !OzPlayableShuttle.LandableAt(W, s, wp, out _)) continue;
                    cands.Add((new Vector2Int(c.x, c.y), wp, d));
                }
            if (cands.Count == 0) { log?.Invoke($"far spot across (K420): no free landable spot {minM:F0} .. {maxM:F0} m across the screen from the ship"); return false; }
            rts.FreeLookOff(); rts.follow = null;
            foreach (var h in OzProofFarSpot.Heights)
                foreach (var c in cands)
                {
                    rts.focus = new Vector3((shipW.x + c.wp.x) * 0.5f, 0f, (shipW.z + c.wp.z) * 0.5f);
                    rts.SetHeightImmediate(h);
                    var vs = cam.WorldToViewportPoint(c.wp); var vh = cam.WorldToViewportPoint(shipW);
                    if (vs.z <= 0f || vs.x < OzProofFarSpot.SpotX0 || vs.x > OzProofFarSpot.SpotX1 || vs.y < OzProofFarSpot.SpotY0 || vs.y > OzProofFarSpot.SpotY1) continue;
                    if (vh.z <= 0f || vh.x < OzProofFarSpot.ShipX0 || vh.x > OzProofFarSpot.ShipX1 || vh.y < OzProofFarSpot.ShipY0 || vh.y > OzProofFarSpot.ShipY1) continue;
                    var sp = cam.WorldToScreenPoint(c.wp);
                    px = c.q; screen = new Vector2(sp.x, sp.y);
                    log?.Invoke($"far spot across (K420): px ({c.q.x}, {c.q.y}), {c.d:F1} m from the ship across the screen, framed with it from {h:F0} m ({cands.Count} candidates)");
                    return true;
                }
            rts.focus = new Vector3(shipW.x, 0f, shipW.z); rts.SetHeightImmediate(70f);
            log?.Invoke($"far spot across (K420): {cands.Count} spots across the screen, none framed with the ship up to {OzProofFarSpot.Heights[OzProofFarSpot.Heights.Length - 1]:F0} m");
            return false;
        }
    }
}
#endif
