using System;
using System.Collections.Generic;

namespace OpenZ
{
    /// <summary>
    /// DV108 hero squads, the idle antic rule of the ALAN &amp; BRAD squad (the view runs it: View/OzHeroAntics.cs). Their standing idle
    /// animation stays a grunt's; their ANTIC is only the drinking antic (a rocket fuel can), every <see cref="Period"/> s while a member
    /// stands idle with no enemy around, each member on his own: the first one <see cref="Period"/> s after he became idle (Brad first,
    /// Alan <see cref="MemberStagger"/> s later), then one every <see cref="Period"/> s (start to start, plus or minus
    /// <see cref="Jitter"/> s) so the two never sip in lockstep. An order, a hit, a selection or an enemy coming near ends the antic at
    /// once (the game's own cancel) and the idle time starts again when he stands idle again.
    /// Plain code (no engine calls): the headless check counts it (<see cref="Simulate"/>, Editor/OzV11HeroSquadCount.cs).
    /// </summary>
    public static class OzHeroAnticPlan
    {
        /// <summary>Seconds of idle before a drink, and between two drinks (start to start).</summary>
        public const double Period = 30.0;
        /// <summary>The member's own delay on top (member index x this): Brad 0 s, Alan 4 s.</summary>
        public const double MemberStagger = 4.0;
        /// <summary>The random part of each next start (+- s).</summary>
        public const double Jitter = 1.0;

        /// <summary>When the member's next drink is due: <paramref name="idleSince"/> = when he last became idle (an order, a hit or
        /// an enemy near resets it; his own drink does not), <paramref name="lastStart"/> = his last drink's start (negative = none
        /// yet), <paramref name="jitter01"/> = a draw in 0..1 for this cycle.</summary>
        public static double NextAt(double idleSince, double lastStart, int member, double jitter01)
        {
            double first = idleSince + Period + Math.Max(0, member) * MemberStagger;
            if (lastStart < 0 || lastStart < idleSince) return first;
            return Math.Max(first, lastStart + Period + (Clamp01(jitter01) * 2.0 - 1.0) * Jitter);
        }

        static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;

        /// <summary>One counted run of the rule (the view's scan every <paramref name="scan"/> s): <paramref name="members"/> robots
        /// stand idle from 0 s for <paramref name="secs"/> s; a drink lasts <paramref name="drink"/> s; <paramref name="busy"/> = time
        /// spans in which every member is busy (an order, an enemy near: a running drink ends, the idle time starts again after it).
        /// Returns the drink start times per member.</summary>
        public static List<double>[] Simulate(double secs, double drink, int members, double scan, Func<double> rnd, IList<(double from, double to)> busy = null)
        {
            var starts = new List<double>[members];
            var idle = new double[members]; var last = new double[members]; var jit = new double[members]; var endAt = new double[members];
            for (int m = 0; m < members; m++) { starts[m] = new List<double>(); idle[m] = 0; last[m] = -1; jit[m] = rnd(); endAt[m] = -1; }
            for (double t = 0; t <= secs + 1e-9; t += scan)
            {
                bool isBusy = false;
                if (busy != null) foreach (var b in busy) if (t >= b.from && t < b.to) { isBusy = true; break; }
                for (int m = 0; m < members; m++)
                {
                    if (isBusy) { idle[m] = t; endAt[m] = -1; continue; }                      // the game's cancel: the drink ends at once
                    if (endAt[m] >= 0 && t < endAt[m]) continue;                                // drinking
                    if (t + 1e-9 >= NextAt(idle[m], last[m], m, jit[m]))
                    {
                        starts[m].Add(t); last[m] = t; endAt[m] = t + drink; jit[m] = rnd();
                    }
                }
            }
            return starts;
        }
    }
}
