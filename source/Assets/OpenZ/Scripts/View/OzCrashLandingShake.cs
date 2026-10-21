using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K402: the playable shuttle's CRASH landing (K256e, OzPlayableShuttle.Crash.cs) shakes the camera when it happens on screen. View only.
    /// It is the game's own explosion shake (OpenZFxShake.Kick, the event kick of a building or a heavy shell landing): its strength falls
    /// with the camera's distance (zoomed out = less, never under ShakeKickFarK), it decays like every impact (to a tenth in about 0.46 s),
    /// it passes the one shake gate (OzShakeGate: the CAMERA SHAKE ON EXPLOSIONS option off = nothing, CALM MODE = nothing, the shared pause
    /// between kicks) and it is drawn only while the crash is in the camera view (ShakeKickEdge of the view size outside the screen edge
    /// still counts; further out = no shake at all). On top of that the strength falls with the crash's distance from the screen centre:
    /// full at the centre, EdgeK of it at the screen edge and in that margin.
    /// <list type="bullet">
    /// <item>THE SLAM (the first ground contact, the low side's pads): a medium kick, SlamPx (1080 px reference), and with it the RTS
    /// camera's own short jitter that the crash always had (OzShuttleTouchdown.Kick), scaled by the same screen factor, as the second
    /// part of the same kick.</item>
    /// <item>THE BOUNCE (the hull rebounds off the slam and its other side bangs down, 0.33 s later): a smaller kick, BouncePx, as the
    /// second part of the slam's kick (not held back by the pause between kicks when the slam itself kicked; the switches and the view
    /// test still apply).</item>
    /// </list>
    /// A normal landing never asks. Every request is counted and logged (Log, and OzShakeGate's own shake log) for the proof.
    /// </summary>
    public static class OzCrashLandingShake
    {
        /// <summary>The slam's and the bounce's peak (screen px at the 1080 px reference, the camera at its default height or nearer, the
        /// crash at the screen centre); the strength left at the screen edge (x of the centre's).</summary>
        public static float SlamPx = 7f, BouncePx = 3f, EdgeK = 0.6f;
        /// <summary>Proof: requests, kicks, refused by the gate (switch, calm mode, the pause between kicks), off screen; the last request's
        /// screen factor, zoom factor and peak px (after both factors; 0 when it did not kick) and its verdict.</summary>
        public static int Asked, Kicks, Refused, OffScreen, Slams, Bounces;
        public static float LastScreenK, LastZoomK, LastPx;
        public static string LastVerdict = "none";
        public static bool LogToConsole;
        public static readonly List<string> Log = new List<string>();
        static bool slamKicked;

        /// <summary>The screen factor for a point at viewport position vp (z = its depth): 1 at the screen centre, falling straight to EdgeK
        /// at the screen edge (by the distance from the centre, the half screen = 1), EdgeK in the ShakeKickEdge margin outside the edge,
        /// 0 beyond it or behind the camera (off screen: no shake).</summary>
        public static float ScreenK(Vector3 vp, float near, float far)
        {
            float e = OpenZFxCatalog.ShakeKickEdge;
            if (vp.z <= near || vp.z >= far || vp.x < -e || vp.x > 1f + e || vp.y < -e || vp.y > 1f + e) return 0f;
            float dx = (vp.x - 0.5f) * 2f, dy = (vp.y - 0.5f) * 2f;
            return Mathf.Lerp(1f, EdgeK, Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy)));
        }

        /// <summary>The kick's distance factor as OpenZFxShake.Kick applies it (ShakeKickRefSpan m of ground across the view height or
        /// nearer = 1, further = less, never under ShakeKickFarK).</summary>
        public static float ZoomK(Camera c, Vector3 at)
        {
            float span = c.orthographic ? 2f * c.orthographicSize : 2f * Vector3.Distance(c.transform.position, at) * Mathf.Tan(c.fieldOfView * 0.5f * Mathf.Deg2Rad);
            return Mathf.Clamp(OpenZFxCatalog.ShakeKickRefSpan / Mathf.Max(1f, span), OpenZFxCatalog.ShakeKickFarK, 1f);
        }

        /// <summary>The crash's first ground contact (the hull's middle, the slam point for the camera's own jitter).</summary>
        public static bool Slam(Vector3 hull, Vector3 slamPoint)
        {
            Slams++;
            var rc = Object.FindFirstObjectByType<RtsCamera>();
            if (OpenZFxShake.I == null)
            {   // no effect shake in this scene: the crash's old camera kick alone (the gate still decides)
                slamKicked = rc && rc.Shake(OzShuttleTouchdown.Kick, slamPoint, "wreck_slam");
                Note("slam", "no effect shake: the camera's own kick only -> " + (slamKicked ? "KICK" : "refused"));
                return slamKicked;
            }
            slamKicked = Ask(hull, SlamPx, "crash_landing", false, "slam");
            if (slamKicked && rc) rc.Shake(OzShuttleTouchdown.Kick * LastScreenK, slamPoint, "wreck_slam", true, true);   // the camera's own short jitter: the second part of this kick
            return slamKicked;
        }

        /// <summary>The bounce: the hull's other side bangs down after the slam (a smaller kick, the second part of the slam's).</summary>
        public static bool Bounce(Vector3 hull)
        {
            Bounces++;
            if (OpenZFxShake.I == null) { Note("bounce", "no effect shake: none"); return false; }
            return Ask(hull, BouncePx, "crash_bounce", slamKicked, "bounce");
        }

        static bool Ask(Vector3 at, float px0, string src, bool always, string what)
        {
            Asked++;
            var sh = OpenZFxShake.I; var c = Camera.main;
            LastScreenK = 0f; LastZoomK = 0f; LastPx = 0f;
            if (!c) { LastVerdict = "no camera"; Refused++; Note(what, LastVerdict); return false; }
            var vp = c.WorldToViewportPoint(at);
            LastScreenK = ScreenK(vp, c.nearClipPlane, c.farClipPlane);
            if (LastScreenK <= 0f)
            {
                OffScreen++; OzShakeGate.OffScreen++; LastVerdict = "off-screen";
                Note(what, string.Format("at view {0:0.00}, {1:0.00} -> off-screen, no shake", vp.x, vp.y));
                return false;
            }
            LastZoomK = ZoomK(c, at);
            bool ok = sh.Kick(at, px0 * LastScreenK, src, always);
            if (ok) { Kicks++; LastPx = px0 * LastScreenK * LastZoomK; LastVerdict = "KICK"; }
            else
            {
                Refused++;
                LastVerdict = sh.scale <= 0f ? "effect shake scaled to 0" : !OzShakeGate.SettingOn ? (OzMotion.Reduced ? "calm mode" : "shake off") : "held (the pause between kicks)";
            }
            Note(what, string.Format("at view {0:0.00}, {1:0.00}: screen k {2:0.00} x zoom k {3:0.00} x {4:0.0} px = {5:0.0} px{6} -> {7}",
                vp.x, vp.y, LastScreenK, LastZoomK, px0, px0 * LastScreenK * LastZoomK, always ? " (second part)" : "", LastVerdict));
            return ok;
        }

        static void Note(string what, string text)
        {
            string line = string.Format("[crash shake] t={0:0.00} {1}: {2}", Time.unscaledTime, what, text);
            if (Log.Count >= 200) Log.RemoveAt(0);
            Log.Add(line);
            if (LogToConsole) Debug.Log(line);
        }

        public static void ResetCounts() { Asked = Kicks = Refused = OffScreen = Slams = Bounces = 0; LastScreenK = LastZoomK = LastPx = 0f; LastVerdict = "none"; Log.Clear(); }
    }
}
