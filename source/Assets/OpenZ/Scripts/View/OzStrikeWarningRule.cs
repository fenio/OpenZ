using System;
using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ
{
    /// <summary>
    /// The announcer's strike group warning (DV107, view only; no engine type here, so the counted check runs it outside the
    /// engine). One of twenty lines in the announcer's voice (sound event ann_strike_group) is said when an enemy strike group
    /// STARTS TO MARCH (the computer's record says Formed: it leaves its fort), never while it forms, and only when:
    /// <list type="bullet">
    /// <item>the human player owns a radar at that moment (and still owns one when the line is said);</item>
    /// <item>no warning was said in the last <see cref="GapSec"/> s of game time;</item>
    /// <item>no other voice line is playing or waiting (the announcer, a robot, the commander): the warning waits for it to end and
    /// is dropped when it would be more than <see cref="LateSec"/> s late.</item>
    /// </list>
    /// The line is random, never the one said last, and none again before all twenty have been said (a shuffled bag). Its own
    /// random numbers, never the rules'. Counters for the checks.
    /// </summary>
    public sealed class OzStrikeWarningRule
    {
        public const double GapSec = 300.0, LateSec = 6.0;
        readonly int count;
        readonly Random rng;
        readonly List<int> bag = new List<int>();
        int last = -1;
        double pendingSince = -1, lastSaid = double.NegativeInfinity;

        /// <summary>Marches started (requests), lines said, dropped: no radar, inside the 5-minute gap, too late (a voice was busy).</summary>
        public int Requests, Said, NoRadar, InGap, TooLate;
        /// <summary>The last line said (index) and when (game time).</summary>
        public int LastLine => last;
        public double LastSaidAt => lastSaid;
        public bool Waiting => pendingSince >= 0;

        public OzStrikeWarningRule(int lines, int seed) { count = Math.Max(1, lines); rng = new Random(seed); }

        /// <summary>The computer's record: true only for the moment a strike group starts to march.</summary>
        public static bool MarchStart(ZBot.StrikeEventKind kind) => kind == ZBot.StrikeEventKind.Formed;

        /// <summary>One event of an enemy computer's strike group record (game time, real time, does the human player own a radar now).</summary>
        public void Note(ZBot.StrikeEventKind kind, double gameTime, double realTime, bool radar)
        {
            if (!MarchStart(kind)) return;
            Requests++;
            if (!radar) { NoRadar++; return; }
            if (gameTime - lastSaid < GapSec) { InGap++; return; }
            if (pendingSince < 0) pendingSince = realTime;
        }

        /// <summary>Every frame. Returns the line to say now (0 .. lines-1), or -1.</summary>
        public int Tick(double gameTime, double realTime, bool radar, bool voiceBusy)
        {
            if (pendingSince < 0) return -1;
            if (!radar) { pendingSince = -1; NoRadar++; return -1; }                       // the radar was lost while it waited
            if (realTime - pendingSince > LateSec) { pendingSince = -1; TooLate++; return -1; }
            if (voiceBusy) return -1;
            pendingSince = -1; lastSaid = gameTime; Said++;
            if (bag.Count == 0) Refill();
            last = bag[0]; bag.RemoveAt(0);
            return last;
        }

        void Refill()
        {
            for (int i = 0; i < count; i++) bag.Add(i);
            for (int i = count - 1; i > 0; i--) { int j = rng.Next(i + 1); (bag[i], bag[j]) = (bag[j], bag[i]); }
            if (count > 1 && bag[0] == last) { int j = 1 + rng.Next(count - 1); (bag[0], bag[j]) = (bag[j], bag[0]); }   // never the same line twice in a row
        }
    }
}
