using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1, items 331 + 332 (the count of the colliders made ahead): the State line said "colliders made 194 of 208" minutes
    /// after the level start because it counted every part ever listed, also the parts of the statics that had died since (their
    /// colliders are dropped on purpose and never made again while they are dead) and the parts taken away from a living static (the
    /// fort's cannon when it is tossed). And a static listed again after the prebuild had passed it (a revived building, a swapped view,
    /// a building's ruin made solid) was never made ahead again, only when a big piece came near. Now:
    /// <list type="bullet">
    /// <item>the count is of the living statics' parts that are there (<see cref="MadeState"/>); the parts of gone statics and the parts
    /// taken away are listed apart, and so are the parts still waiting for the background bake or not reached yet;</item>
    /// <item>a static scanned again sends the prebuild back to the first static (the parts made already are passed over at once), so its
    /// parts are made ahead again;</item>
    /// <item>the time when every living part was made is kept (seconds after the level start: the first time and the latest, after a
    /// static was listed again);</item>
    /// <item>a frame makes a further collider only while the slowest one of the level so far would still fit in <see cref="FrameCapMs"/>
    /// (the first one of a frame always), so no frame does more than about 1 ms of it; the heaviest frame is counted;</item>
    /// <item>the counts of the colliders made start again with each level (they added up over the restarts of one session).</item>
    /// </list>
    /// View only.
    /// </summary>
    public sealed partial class OzStaticSolid
    {
        /// <summary>The making of the colliders ahead starts no further one in a frame once the slowest so far would pass this (ms).</summary>
        public static float FrameCapMs = 1f;
        /// <summary>Seconds after the level start when every living static's part was made: the first time, and the latest (-1 = not yet).</summary>
        public static float FirstAllMadeAt = -1f, AllMadeAt = -1f;
        /// <summary>The heaviest frame of making colliders ahead (ms).</summary>
        public static float MaxMakeFrameMs;
        static int madeNow; static bool madePending = true;

        /// <summary>Clear: the counts of this level start again.</summary>
        static void ResetMade()
        {
            MeshParts = BoxParts = Built = Prebuilt = BuiltLate = 0; MaxBuildMs = 0f; BakeMsTotal = 0f;
            MaxMakeFrameMs = 0f; FirstAllMadeAt = AllMadeAt = -1f; madeNow = 0; madePending = true;
        }

        /// <summary>Scan: a static (re)listed: the prebuild goes back to the first static, so its parts are made ahead too.</summary>
        static void Rewind() { prebuildCursor = 0; prebuildPart = 0; madePending = true; }

        /// <summary>BuildStep's loops: room for one more collider in this frame.</summary>
        static bool Room()
        {
            double e = buildWatch.Elapsed.TotalMilliseconds;
            if (e >= BuildBudgetMs) return false;
            if (madeNow == 0) return true;                                                                // the first of a frame always
            float slowest = Built > 0 ? MaxBuildMs : 0.3f;
            return e + slowest <= FrameCapMs;
        }

        /// <summary>Build: one collider made (its time is in MaxBuildMs already).</summary>
        static void Made(float ms) { madeNow++; }

        /// <summary>The end of a frame with work: the heaviest frame; when the prebuild has passed every static, the time all were made.</summary>
        static void NoteFrame(double ms)
        {
            if (madeNow > 0 && ms > MaxMakeFrameMs) MaxMakeFrameMs = (float)ms;
            madeNow = 0;
            if (!madePending || prebuildCursor < statics.Count || queue.Count > 0 || statics.Count == 0) return;
            Count(out int made, out int all, out _, out _, out _, out _);
            if (made < all) return;
            madePending = false; AllMadeAt = Time.timeSinceLevelLoad;
            if (FirstAllMadeAt < 0f) FirstAllMadeAt = AllMadeAt;
        }

        static void Count(out int made, out int all, out int waiting, out int notYet, out int goneParts, out int taken)
        {
            made = all = waiting = notYet = goneParts = taken = 0;
            foreach (var st in statics)
            {
                if (st.dead) { goneParts += st.parts.Count; continue; }
                foreach (var p in st.parts)
                {
                    if (!p.t) { taken++; continue; }                                                     // taken away from its static (a tossed cannon, a wreck's piece)
                    all++;
                    if (p.built) made++;
                    else if (!Ready(p)) waiting++;
                    else notYet++;
                }
            }
        }

        /// <summary>The colliders made of the living statics' parts, for the State line.</summary>
        public static string MadeState()
        {
            Count(out int made, out int all, out int waiting, out int notYet, out int goneParts, out int taken);
            string when = AllMadeAt < 0f ? "not all made yet" : FirstAllMadeAt == AllMadeAt ? $"all made {AllMadeAt:0.0} s after the level start"
                : $"all made {FirstAllMadeAt:0.0} s after the level start, and again {AllMadeAt:0.0} s after it (a static listed again)";
            return $"{made} of {all} ({when}; waiting for the bake {waiting}, not reached yet {notYet}; left out: parts of gone statics {goneParts}, parts taken away {taken}; " +
                   $"the heaviest frame of making them {MaxMakeFrameMs:0.00} ms)";
        }

        /// <summary>Play check: every living static's part made, all of them within `seconds` of the level start, no frame over the cap.</summary>
        public static bool MadeOk(float seconds, float frameMs, out string line)
        {
            Count(out int made, out int all, out _, out _, out _, out _);
            bool ok = all > 0 && made == all && FirstAllMadeAt >= 0f && FirstAllMadeAt <= seconds && MaxMakeFrameMs <= frameMs;
            line = $"colliders made {MadeState()} (wanted N of N within {seconds:0} s, every frame <= {frameMs:0.0} ms)";
            return ok;
        }
    }
}
