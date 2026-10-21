using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K423, the kickflip travels forward through the whole roll and its long whoosh is heard every time (view only; the plain part:
    /// View/OzShuttleKickflip.Forward.cs; the flip: View/OzShuttleKickflip.Core.cs and View/OzPlayableShuttle.Kickflip.cs).
    /// <list type="bullet">
    /// <item>THE LEAD: each drawn frame the hull is shifted along the flight's own direction just so far that its picture stays level with the
    /// level ship's along the flight's screen line (OzKickflipForward.LeadSolve, from the main camera's picture): the rise and the swing move it
    /// only across that line, so on screen it keeps its cruise pace forward through the whole roll while it swings out, over and across round its
    /// way (the spiral). The room test and the whole-arc-in-view test read the arc with the same lead.</item>
    /// <item>THE WAY LEFT: with a route the start test measures the way to the next middle point (where the ship slows and turns).</item>
    /// <item>THE WHOOSH WATCH: the long whoosh is started through the director with its voice handed back; that voice is watched to its end
    /// (played out, cut short by another sound, or virtual = silenced by the audio engine's voice limit) and the outcome goes into the sound
    /// log and the proof's counts. Its rows are action sounds now (the flip's rows used the lowest foley priority, 176: the first to be
    /// dropped or silenced when many sounds play). While it is at its loudest the music and the ambience bed step back 4 dB (the director's
    /// eased duck).</item>
    /// <item>THE PROOF'S EYE: per drawn frame the hull's on-screen pace along the flight's screen line against the level path's.</item>
    /// </list>
    /// View only: the rules never know about any of it.
    /// </summary>
    public sealed partial class OzPlayableShuttle
    {
        /// <summary>K423 proof counts: the long whooshes started with a voice, started without one (dropped: the reason is in the sound log),
        /// played to their end with a real voice, cut short, frames a whoosh voice was virtual (silenced by the engine's voice limit).</summary>
        public int KickWhooshFired { get; private set; }
        public int KickWhooshDropped { get; private set; }
        public int KickWhooshFull { get; private set; }
        public int KickWhooshCut { get; private set; }
        public int KickWhooshVirtualFrames { get; private set; }
        /// <summary>A whoosh voice is being watched now.</summary>
        public bool KickWhooshWatching => kickWhOn;
        /// <summary>K423 proof: this flip's hull pace on screen along the flight's screen line against the level path's (lowest, highest),
        /// the frames counted, under 50 %, backwards; the largest lead drawn (m).</summary>
        public float KickFwdMinShare { get; private set; } = 9f;
        public float KickFwdMaxShare { get; private set; } = -9f;
        public int KickFwdFrames { get; private set; }
        public int KickFwdStall { get; private set; }
        public int KickFwdBack { get; private set; }
        public float KickLeadMax { get; private set; }
        /// <summary>K423 proof: the drawn hull's largest distance from the level hull along the flight's screen line in a frame's own picture (px).</summary>
        public float KickFwdOffsetPx { get; private set; }

        AudioSource kickWhSrc; AudioClip kickWhClip; float kickWhMaxT; bool kickWhOn; int kickWhVirtual;
        bool kickFwdOn; Vector3 kickFwdPrevD, kickFwdPrevL;

        // ================================================================== the lead
        /// <summary>The camera's picture for the lead (OzKickflipForward.LeadSolve).</summary>
        struct KickScreen : IOzKickScreen
        {
            public Camera C;
            public bool S(double x, double y, double z, out double sx, out double sy)
            {
                var p = C.WorldToScreenPoint(new Vector3((float)x, (float)y, (float)z)); sx = p.x; sy = p.y; return p.z > 0.1f;
            }
        }

        /// <summary>The flight's own direction across the ground (the drawn velocity; the facing when it barely moves).</summary>
        Vector3 KickFlightDir()
        {
            var v = Velocity; v.y = 0f;
            if (v.sqrMagnitude < 0.04f) { v = transform.forward; v.y = 0f; }
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        /// <summary>The lead (world) for a level hull middle b, the flight direction d and the drawn offset o, from this camera's picture.</summary>
        static Vector3 KickLeadAt(Camera cam, Vector3 b, Vector3 d, Vector3 o)
        {
            if (!cam || !OzKickflipForward.LeadOn) return Vector3.zero;
            var sc = new KickScreen { C = cam };
            double L = OzKickflipForward.LeadSolve(ref sc, b.x, b.y, b.z, d.x, d.z, o.x, o.y, o.z);
            return d * (float)L;
        }

        int kickLeadFrame = -1; Vector3 kickLeadNow;
        /// <summary>This frame's lead on the drawn pose (world; solved once a frame from the level pose, the flip's offset and the main camera).</summary>
        Vector3 KickLeadVec(in OzKickflipOut k)
        {
            if (kickLeadFrame == Time.frameCount) return kickLeadNow;
            kickLeadFrame = Time.frameCount;
            var o = Vector3.up * (float)(k.HopM + k.RiseM) + kickLevelRot * new Vector3((float)k.SideM, 0f, 0f);
            kickLeadNow = KickLeadAt(Camera.main, kickLevelPos + kickLevelRot * KickPivot, KickFlightDir(), o);
            float a = kickLeadNow.magnitude; if (a > KickLeadMax) KickLeadMax = a;
            return kickLeadNow;
        }

        /// <summary>The lead at a point of the flip ahead (the room and the whole-arc-in-view tests while armed): the level hull middle c, the
        /// way along v, the drawn rise and swing to the ship's right (m).</summary>
        Vector3 KickLeadAhead(Vector3 c, Vector3 v, double rise, double side)
        {
            if (!OzKickflipForward.LeadOn) return Vector3.zero;
            v.y = 0f; var d = v.sqrMagnitude > 1e-4f ? v.normalized : KickFlightDir();
            var n = transform.right; n.y = 0f; n = n.sqrMagnitude > 1e-6f ? n.normalized : new Vector3(d.z, 0f, -d.x);
            return KickLeadAt(Camera.main, c, d, Vector3.up * (float)rise + n * (float)side);
        }

        // ================================================================== the way left
        /// <summary>The start test's way left (m): to the goal, or to the route's next middle point when there is one.</summary>
        static double KickWayLeftM(ZObject o, double remainPx)
        {
            double nx = o.ShuttleSteerX - o.ShuttleBaseX, ny = o.ShuttleSteerY - o.ShuttleBaseY;
            return OzKickflipForward.WayLeftM(remainPx / 8.0, o.ShuttleViaCount > 0, System.Math.Sqrt(nx * nx + ny * ny) / 8.0);
        }

        // ================================================================== the long whoosh, watched
        /// <summary>The roll begins: the long whoosh (its voice handed back and watched to the end).</summary>
        void KickWhoosh(Vector3 at, int id)
        {
            string row = KickWhooshRow();
            if (!AudioDirector.Has(row)) { OzSoundLog.Add("FLIP", row, null, "MISSING row", 0f, at); return; }
            var src = AudioDirector.FireTracked(row, at, id, 1f);
            KickSounds++; KickLastSound = row; LastSound = row;
            if (!src) { KickWhooshDropped++; OzSoundLog.Add("FLIP", row, null, "whoosh: no voice (dropped, see the line above)", 0f, at); return; }
            KickWhooshFired++; kickWhSrc = src; kickWhClip = src.clip; kickWhMaxT = 0f; kickWhVirtual = 0; kickWhOn = true;
            if (AudioDirector.I) AudioDirector.I.AddDuck(KickWhooshDuckMusicDb, 0f, KickWhooshDuckAmbDb, 0f, KickWhooshDuckSec, 0.2f, 0.8f);   // the music and the ambience bed make room under its loud part
        }

        /// <summary>K423: while the long whoosh is at its loudest, the music and the ambience bed step back a little (dB, s; the director's own
        /// eased duck, as under a voice line): the whoosh is heard over a jungle level's bed. The engines and every other sound stay.</summary>
        public static float KickWhooshDuckMusicDb = -4f, KickWhooshDuckAmbDb = -4f, KickWhooshDuckSec = 1.9f;

        /// <summary>Every frame (KickflipAfterJets' start): the watched whoosh voice; its outcome once it has stopped.</summary>
        void KickWhooshWatchTick()
        {
            if (!kickWhOn) return;
            if (AudioListener.pause) return;                                                             // the game paused: the voice waits with it (no outcome yet, never a false "CUT")
            bool same = kickWhSrc && kickWhSrc.clip == kickWhClip && kickWhClip;
            if (same && kickWhSrc.isPlaying)
            {
                kickWhMaxT = Mathf.Max(kickWhMaxT, kickWhSrc.time);
                if (kickWhSrc.isVirtual) { kickWhVirtual++; KickWhooshVirtualFrames++; }
                return;
            }
            kickWhOn = false;
            float len = kickWhClip ? kickWhClip.length : 0f;
            bool full = same && len > 0f && kickWhMaxT >= len - 0.12f;
            if (full && kickWhVirtual == 0) KickWhooshFull++; else if (!full) KickWhooshCut++;
            OzSoundLog.Add("FLIP", "shuttle_flip_whoosh", kickWhClip ? kickWhClip.name : null,
                full ? (kickWhVirtual == 0 ? $"whoosh played to its end ({len:F2} s), heard all the way" : $"whoosh played to its end, SILENCED (virtual) {kickWhVirtual} frames")
                     : $"whoosh CUT at {kickWhMaxT:F2} s of {len:F2} s{(same ? "" : " (its voice was taken by another sound)")}", 0f, transform.position);
            kickWhSrc = null;
        }

        // ================================================================== the proof's eye: the hull's pace on screen
        /// <summary>KickflipDraw, the picture's pose on: the drawn hull's middle and the level one's, projected with this frame's camera (the
        /// last frame's points too, so a moving camera does not count), the pace along the flight's screen line.</summary>
        void KickForwardMeasure()
        {
            var cam = Camera.main; if (!cam) return;
            var drawn = transform.TransformPoint(KickPivot); var level = kickLevelPos + kickLevelRot * KickPivot;
            if ((KickNow.Events & OzKickflipState.EvStart) != 0 || !kickFwdOn)
            {
                kickFwdOn = true; kickFwdPrevD = drawn; kickFwdPrevL = level;
                KickFwdMinShare = 9f; KickFwdMaxShare = -9f; KickFwdFrames = KickFwdStall = KickFwdBack = 0; KickLeadMax = 0f; KickFwdOffsetPx = 0f;
                return;
            }
            Vector3 a = cam.WorldToScreenPoint(kickFwdPrevL), b = cam.WorldToScreenPoint(level), c0 = cam.WorldToScreenPoint(kickFwdPrevD), c1 = cam.WorldToScreenPoint(drawn);
            kickFwdPrevD = drawn; kickFwdPrevL = level;
            if (a.z <= 0f || b.z <= 0f || c0.z <= 0f || c1.z <= 0f) return;
            var b1 = cam.WorldToScreenPoint(level + KickFlightDir());                                       // this frame's own picture: drawn against level along the flight's screen line
            var w = new Vector2(b1.x - b.x, b1.y - b.y); float wl = w.magnitude;
            if (wl > 1e-3f && b1.z > 0f) KickFwdOffsetPx = Mathf.Max(KickFwdOffsetPx, Mathf.Abs(Vector2.Dot(new Vector2(c1.x - b.x, c1.y - b.y), w / wl)));
            var u = new Vector2(b.x - a.x, b.y - a.y); float ul = u.magnitude;
            if (ul < 0.05f) return;
            u /= ul;
            float sh = Vector2.Dot(new Vector2(c1.x - c0.x, c1.y - c0.y), u) / ul;
            KickFwdMinShare = Mathf.Min(KickFwdMinShare, sh); KickFwdMaxShare = Mathf.Max(KickFwdMaxShare, sh); KickFwdFrames++;
            if (sh < 0.5f) KickFwdStall++;
            if (sh < 0f) KickFwdBack++;
        }
    }
}
