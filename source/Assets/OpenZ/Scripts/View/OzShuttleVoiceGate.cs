// OpenZ (GPL-3.0) - the playable shuttle's voices (rule DV118, K256e): Alan's shuttle lines and Rachel's lines never overlap. Plain numbers
// only (nothing of the engine), so the same rule is also counted outside the editor; AudioDirector.ShuttleRound3.cs feeds it once a frame.
using System;

namespace OpenZ
{
    /// <summary>
    /// The rule (FIX_LIST M13, the owner's "Alan's lines are still overlapped by Rachel"):
    /// <list type="bullet">
    /// <item>Every Rachel line (every ann_*, whatever its rank) waits while one of Alan's shuttle lines (vo_shuttle_*: "Nice landing!",
    /// "Touchdown!", "Hot landing!", "Can't land here!", "All aboard!", "Everybody out!") plays, and <see cref="GapSec"/> after it; at most
    /// <see cref="RachelWaitMaxSec"/> (then she speaks: her queue's own rules drop what is too old).</item>
    /// <item>A shuttle line asked while Rachel speaks ANY line (or within <see cref="GapSec"/> after her) is HELD, never played under her and
    /// never ducked: it is said once she has finished (+ <see cref="GapSec"/>), or dropped when it waited longer than
    /// <see cref="ShuttleHoldMaxSec"/> (no longer to the point). Only the latest held line is kept.</item>
    /// </list>
    /// Times are real seconds (the voices' own clock). Nothing changes for any other line.
    /// </summary>
    public sealed class OzShuttleVoiceGate
    {
        public const string ShuttlePrefix = "vo_shuttle_";
        public static float GapSec = 0.3f, RachelWaitMaxSec = 3.5f, ShuttleHoldMaxSec = 3f;

        public static bool IsShuttleLine(string id) => id != null && id.StartsWith(ShuttlePrefix, StringComparison.Ordinal);

        bool shuttleOn, rachelOn; float shuttleEnd = -100f, rachelEnd = -100f, holdFrom = -1f;
        /// <summary>The held shuttle line (null = none), its line filter and the time it was asked.</summary>
        public string HeldId { get; private set; }
        public string HeldFilter { get; private set; }
        public float HeldAt { get; private set; }
        /// <summary>Counts: Rachel's frames held back behind a shuttle line, her lines let go by the cap, shuttle lines held, said after her,
        /// dropped (waited too long).</summary>
        public int RachelHeldFrames, RachelCapped, Held, HeldPlayed, HeldDropped;

        /// <summary>Once a frame, first: is one of Alan's shuttle lines playing now, is Rachel speaking now.</summary>
        public void Tick(float now, bool shuttlePlaying, bool rachelPlaying)
        {
            if (shuttleOn && !shuttlePlaying) shuttleEnd = now;
            if (rachelOn && !rachelPlaying) rachelEnd = now;
            shuttleOn = shuttlePlaying; rachelOn = rachelPlaying;
        }

        public bool ShuttleSpeaking => shuttleOn;
        public bool RachelSpeaking => rachelOn;

        /// <summary>May Rachel's next line start now? False = it waits (a shuttle line plays, or ended less than GapSec ago); after
        /// RachelWaitMaxSec of waiting it may.</summary>
        public bool RachelMayStart(float now)
        {
            bool blocked = shuttleOn || now - shuttleEnd < GapSec;
            if (!blocked) { holdFrom = -1f; return true; }
            if (holdFrom < 0f) holdFrom = now;
            if (now - holdFrom >= RachelWaitMaxSec) { RachelCapped++; holdFrom = -1f; return true; }
            RachelHeldFrames++;
            return false;
        }

        /// <summary>Must a shuttle line asked now wait for Rachel (she speaks, or finished less than GapSec ago)?</summary>
        public bool ShuttleMustWait(float now) => rachelOn || now - rachelEnd < GapSec;

        /// <summary>Holds a shuttle line (the latest one replaces an older held one, which is then dropped).</summary>
        public void Hold(string id, string filter, float now)
        {
            if (HeldId != null) HeldDropped++;
            HeldId = id; HeldFilter = filter; HeldAt = now; Held++;
        }

        /// <summary>Once a frame after Tick: 0 = nothing to do, 1 = say the held line now (id, filter handed out, the slot emptied), 2 = it
        /// waited too long and is dropped (id handed out for the log).</summary>
        public int Poll(float now, out string id, out string filter)
        {
            id = HeldId; filter = HeldFilter;
            if (HeldId == null) return 0;
            if (now - HeldAt > ShuttleHoldMaxSec) { HeldId = null; HeldFilter = null; HeldDropped++; return 2; }
            if (ShuttleMustWait(now)) return 0;
            HeldId = null; HeldFilter = null; HeldPlayed++;
            return 1;
        }
    }
}
