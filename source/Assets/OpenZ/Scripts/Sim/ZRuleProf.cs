using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace OpenZ.Sim
{
    /// <summary>b109 (log only, the rules are untouched): where the time of one rule tick goes. One clock, charged to the part that
    /// runs: ZWorld.Tick starts every tick as "other"; the hooks switch to orders (the waypoint step of a unit), path (way search
    /// ZPathFinder.FindPath and the pathing layer's per-tick and per-unit set-up), targets (a unit's passive search for an enemy, a
    /// vehicle, a flag, a crate), bot (the computer player and the idle sweep), move (a unit's step and the pathing layer's spacing
    /// push), combat (a unit's shots and the blasts of the tick) and back. A nested part (a way search inside an order or inside the
    /// computer player) is charged to itself only, so the seven parts add up to the whole tick. No allocation per tick: two
    /// Stopwatch time stamps per switch into fixed arrays. Off (the default, every headless check): each hook is one flag test.
    /// ZGame turns it on and writes one line a minute of play; the ending profile adds the same split once a second.</summary>
    public static class ZRuleProf
    {
        public const int Orders = 0, Path = 1, Targets = 2, Bot = 3, Move = 4, Combat = 5, Other = 6, Parts = 7;
        static readonly string[] Names = { "orders", "path", "targets", "bot", "move", "combat", "other" };

        /// <summary>On in the game only (ZGame.Awake).</summary>
        public static bool On;
        static bool inTick;
        static int cur = Other;
        static long stamp, ticks;
        static readonly long[] total = new long[Parts];
        static int minute;

        /// <summary>ZWorld.Tick, first line.</summary>
        public static void TickBegin()
        {
            if (!On) return;
            inTick = true; cur = Other; stamp = Stopwatch.GetTimestamp();
        }

        /// <summary>ZWorld.Tick, last line.</summary>
        public static void TickEnd()
        {
            if (!inTick) return;
            long now = Stopwatch.GetTimestamp();
            total[cur] += now - stamp;
            inTick = false; cur = Other; ticks++;
        }

        /// <summary>Switches the clock to <paramref name="part"/>; returns the part to go back to (pass it to Exit).</summary>
        public static int Enter(int part)
        {
            if (!inTick) return Other;
            long now = Stopwatch.GetTimestamp();
            total[cur] += now - stamp; stamp = now;
            int prev = cur; cur = part;
            return prev;
        }

        public static void Exit(int prev)
        {
            if (!inTick) return;
            long now = Stopwatch.GetTimestamp();
            total[cur] += now - stamp; stamp = now;
            cur = prev;
        }

        /// <summary>A stretch of ticks: Mark at its start, Take at its end ("ms per tick X (orders a, ..., other g)").</summary>
        public sealed class Window
        {
            readonly long[] at = new long[Parts];
            long ticksAt;
            public long Ticks => ticks - ticksAt;

            public void Mark()
            {
                for (int i = 0; i < Parts; i++) at[i] = total[i];
                ticksAt = ticks;
            }

            public string Take()
            {
                double f = 1000.0 / Stopwatch.Frequency / Math.Max(1, ticks - ticksAt);
                long sum = 0;
                for (int i = 0; i < Parts; i++) sum += total[i] - at[i];
                var sb = new StringBuilder(160);
                sb.Append("ms per tick ").Append((sum * f).ToString("F2", CultureInfo.InvariantCulture)).Append(" (");
                for (int i = 0; i < Parts; i++)
                    sb.Append(i == 0 ? "" : ", ").Append(Names[i]).Append(' ').Append(((total[i] - at[i]) * f).ToString("F2", CultureInfo.InvariantCulture));
                sb.Append(')');
                Mark();
                return sb.ToString();
            }
        }

        public static readonly Window Minute = new Window(), Ending = new Window();

        /// <summary>A new level (ZGame.Awake): minute 0, both windows start now.</summary>
        public static void Reset()
        {
            inTick = false; cur = Other; minute = 0;
            Minute.Mark(); Ending.Mark();
        }

        /// <summary>Once a frame after the rule ticks: the line of the minute of play (game time) that has just ended, else null.</summary>
        public static string MinuteLine(ZWorld w)
        {
            if (!On || w == null || w.Time < (minute + 1) * 60.0) return null;
            minute = (int)(w.Time / 60.0);
            long t = Minute.Ticks;
            return "[OpenZ RuleTick] minute " + minute + ": ticks " + t + ", " + Minute.Take() + ", units " + Units(w);
        }

        /// <summary>Robots, vehicles and guns on the map (alive).</summary>
        public static int Units(ZWorld w)
        {
            int n = 0;
            foreach (var o in w.Objects) if (o.IsUnit && !o.Destroyed && o.KillAt < 0) n++;
            return n;
        }
    }
}
