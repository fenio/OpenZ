using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Rule DV118, the shuttle's landing voices: Rachel's landing line (ann_shuttle_landing) and the robots' lines never overlap.
    ///  * While her landing line plays, no robot line starts (alan_main and alan_alert: the select / order lines, "Everybody out!", "All
    ///    aboard!", alerts, the heroes' answers): each is HELD (at most <see cref="ShuttleHeldMax"/> lines, only the latest of a kind) and
    ///    said once she has finished (after <see cref="ShuttleHeldGapSec"/>, one after the other, never over another voice), or dropped
    ///    when it waited longer than <see cref="ShuttleHeldMaxSec"/> (no longer to the point). A held line keeps its own cooldowns.
    ///  * Her landing line waits in the announcer's queue while Alan speaks, but it never blocks the queue: the next line that may speak
    ///    (the queue's own 0.6 s rule) goes first and the landing line stays queued.
    /// Hooks: AudioDirector.cs (SayImpl, UpdateChannels, the robot line gap, the frame tick) and AudioDirector.HeroVoices.cs (HvAnswer).
    /// Nothing changes for any other line while no shuttle landing line plays.
    /// </summary>
    public partial class AudioDirector
    {
        public const string ShuttleLandingLine = "ann_shuttle_landing";
        public const int ShuttleHeldMax = 3;
        public const float ShuttleHeldMaxSec = 5f, ShuttleHeldGapSec = 0.25f;
        /// <summary>Checks: robot lines held behind her landing line, said after it, dropped (waited too long); queue lines that went before
        /// her waiting landing line.</summary>
        public static int ShuttleHeld, ShuttleHeldPlayed, ShuttleHeldDropped, ShuttleQueueSkips;

        struct ShuttleHeldLine { public string id, f; public float t; }
        readonly List<ShuttleHeldLine> shuttleHeld = new List<ShuttleHeldLine>(4);
        bool shuttleReleasing; float shuttleLandingEnd = -100f;

        /// <summary>Rachel's shuttle landing line is playing now.</summary>
        bool ShuttleRachelLanding => rachel != null && rachel.busy && rachel.src && rachel.src.isPlaying && rachel.def != null && rachel.def.id == ShuttleLandingLine;
        public static bool RachelLandingNow => I && I.ShuttleRachelLanding;

        /// <summary>SayImpl hook: a robot line asked while her landing line plays is held (true = do not play it now).</summary>
        bool ShuttleHoldsRobotLine(string id, string filter)
        {
            if (shuttleReleasing || !ShuttleRachelLanding) return false;
            string fam = Family(id);
            for (int i = shuttleHeld.Count - 1; i >= 0; i--) if (Family(shuttleHeld[i].id) == fam) shuttleHeld.RemoveAt(i);   // only the latest of a kind
            if (shuttleHeld.Count >= ShuttleHeldMax) shuttleHeld.RemoveAt(0);
            shuttleHeld.Add(new ShuttleHeldLine { id = id, f = filter, t = Time.unscaledTime });
            ShuttleHeld++; OzSoundLog.Add("dir", id, null, "held (the shuttle landing line speaks)");
            return true;
        }

        /// <summary>Once a frame: after her landing line, the held robot lines one after the other (never over another voice).</summary>
        void ShuttleHeldTick()
        {
            if (rachel == null || alanMain == null || alanAlert == null) return;
            float now = Time.unscaledTime;
            if (ShuttleRachelLanding) { shuttleLandingEnd = now; return; }
            if (shuttleHeld.Count == 0) return;
            if (now - shuttleLandingEnd < ShuttleHeldGapSec || AlanBusy || (rachel.busy && rachel.src && rachel.src.isPlaying) || (ExternalVoiceBusy != null && ExternalVoiceBusy())) return;
            var h = shuttleHeld[0]; shuttleHeld.RemoveAt(0);
            if (now - h.t > ShuttleHeldMaxSec) { ShuttleHeldDropped++; OzSoundLog.Add("dir", h.id, null, "dropped (held too long behind the shuttle landing line)"); return; }
            shuttleReleasing = true;
            try { SayImpl(h.id, h.f); } finally { shuttleReleasing = false; }
            ShuttleHeldPlayed++;
        }

        /// <summary>UpdateChannels hook: her landing line waits for Alan, but the next line that may speak now goes before it (true = play
        /// `next`, now at the queue's head; false = wait).</summary>
        bool ShuttleLandingStepsAside(float now, ref Line next)
        {
            if (next.d == null || next.d.id != ShuttleLandingLine) return false;
            for (int k = 1; k < rachelQueue.Count; k++)
            {
                var q = rachelQueue[k];
                if (q.d == null || q.d.id == ShuttleLandingLine || now - q.t < 0.6f) continue;
                rachelQueue.RemoveAt(k); rachelQueue.Insert(0, q); next = q; ShuttleQueueSkips++;
                return true;
            }
            return false;
        }
    }
}
