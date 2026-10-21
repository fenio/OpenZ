using System;
using System.Collections.Generic;
using System.Text;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Level 16 weather, the counted check of the rain days (import plan, step J9:
    /// the game's own schedule object (OzRainSchedule in View/OzRainPlan.cs) on a synthetic day clock, the way the level's
    /// weather object drives it once a frame. No engine, no scene, no rules: the weather is view only.
    /// Menu: OpenZ/V1.1/Count Rain Days On 200 Starts (J9). The first line of the text is the verdict "PASS n/n".
    /// </summary>
    public static class OzV11RainCount
    {
        const float Cycle = 720f, StartClock = 0.935f, Dt = 0.02f;
        const float SecondSunrise = (OzRainDayPlan.SunriseRoll + 1f - StartClock) * Cycle;
        const float FirstDropsEarliest = SecondSunrise + OzRainDayPlan.FirstDropsFrom * Cycle;

        sealed class Spell { public int Day; public float Wind, Drops, End, WindEnd, Roll; public OzRainSpell P; }
        sealed class Start { public readonly List<Spell> Spells = new List<Spell>(); public int Rolls, Skipped; public readonly List<int> RollDays = new List<int>(); public readonly List<bool> RollRain = new List<bool>(); }

        static string Clock(float s) { int r = (int)Math.Round(s); return (r / 60) + ":" + (r % 60).ToString("00"); }

        /// <summary>One start played for `minutes` of play on a synthetic clock. hold = seconds at the start in which the
        /// clock stands (the briefing); jumpDay / jumpTo = on that day, when the clock reaches 0.95, it is set to jumpTo.</summary>
        static Start Play(int gameSeed, int salt, float minutes, bool enabled, float startClock = StartClock, float hold = 0f, int jumpDay = -1, float jumpTo = 0f)
        {
            var sch = new OzRainSchedule(gameSeed, salt, Cycle);
            var st = new Start();
            float clock = startClock, play = 0f, lastRoll = 0f; Spell run = null; int rolls = 0; bool jumped = false;
            int holdSteps = (int)(hold / Dt), steps = (int)(minutes * 60f / Dt);
            for (int i = 0; i < holdSteps + steps; i++)
            {
                float dt = i < holdSteps ? 0f : Dt;
                clock += dt / Cycle; if (clock >= 1f) clock -= 1f;
                if (jumpDay > 0 && !jumped && sch.Day == jumpDay && clock >= 0.95f && clock < 0.96f) { clock = jumpTo; jumped = true; }
                play += dt;
                bool busy = run != null && play <= run.WindEnd;
                var p = sch.Step(clock, dt, enabled, busy);
                if (sch.Rolls != rolls)
                {
                    rolls = sch.Rolls; lastRoll = play;
                    st.RollDays.Add(sch.Day); st.RollRain.Add(sch.Today != null && sch.Today.Rain);
                }
                if (p != null)
                {
                    run = new Spell { Day = sch.Day, P = p, Wind = play, Drops = play + p.WindLead, Roll = lastRoll };
                    run.End = run.Drops + p.Total; run.WindEnd = run.End + p.WindTail;
                    st.Spells.Add(run);
                }
            }
            st.Rolls = sch.Rolls; st.Skipped = sch.Skipped;
            return st;
        }

        static string Sign(Start s)
        {
            var sb = new StringBuilder();
            foreach (var x in s.Spells) sb.Append(x.Day).Append(':').Append(x.Wind.ToString("F2")).Append(':').Append(x.P.Total.ToString("F3")).Append(':').Append(x.P.WindAzimuth.ToString("F2")).Append(':').Append(x.P.Rumbles.Length).Append(' ');
            return sb.ToString();
        }

        public static string Run(int starts = 200)
        {
            var sb = new StringBuilder(); int pass = 0, total = 0;
            void Line(bool ok, string text) { total++; if (ok) pass++; sb.Append(ok ? "PASS  " : "FAIL  ").Append(text).Append('\n'); }
            const float Minutes = 60f; const int LastWholeDay = 5;                                           // 60 min: the days 2 to 5 are complete

            var all = new List<Start>();
            for (int k = 1; k <= starts; k++) all.Add(Play(1, k, Minutes, true));

            // 1. never before the second sunrise
            int early = 0, earlyDrops = 0, spells = 0; float first = float.MaxValue, firstDrops = float.MaxValue;
            foreach (var s in all) foreach (var x in s.Spells)
            {
                spells++; if (x.Wind < SecondSunrise) early++; if (x.Drops < FirstDropsEarliest - 0.25f) earlyDrops++;
                if (x.Wind < first) first = x.Wind; if (x.Drops < firstDrops) firstDrops = x.Drops;
            }
            Line(early == 0, $"{starts} seeded starts of 60 minutes: {early} rain spells begin before the second sunrise ({Clock(SecondSunrise)} of play); {spells} spells in all, the earliest wind rises at {(spells > 0 ? Clock(first) : "-")}");
            Line(earlyDrops == 0 && spells > 0, $"first drops before {Clock(FirstDropsEarliest)} of play: {earlyDrops} (the earliest first drops fall at {(spells > 0 ? Clock(firstDrops) : "-")})");

            // 2. rain on half of the days
            int days = 0, wet = 0, day2 = 0, wet2 = 0;
            foreach (var s in all)
                for (int i = 0; i < s.RollDays.Count; i++)
                {
                    if (s.RollDays[i] > LastWholeDay) continue;
                    days++; if (s.RollRain[i]) wet++;
                    if (s.RollDays[i] == 2) { day2++; if (s.RollRain[i]) wet2++; }
                }
            float share = days > 0 ? 100f * wet / days : 0f, share2 = day2 > 0 ? 100f * wet2 / day2 : 0f;
            Line(days == starts * 4 && Math.Abs(share - 50f) <= 7f, $"rain on {wet} of {days} days (the days 2 to 5 of every start) = {share:0.0} % (wanted 50 % within 7 points)");
            Line(day2 == starts && Math.Abs(share2 - 50f) <= 7f, $"the second day alone: rain on {wet2} of {day2} starts = {share2:0.0} % (wanted 50 % within 7 points)");

            // 3. a planned rain day does rain (nothing lost between the roll and the start), one spell a day at most
            int started = 0, planned = 0, twice = 0;
            foreach (var s in all)
            {
                var perDay = new Dictionary<int, int>();
                foreach (var x in s.Spells) { if (x.Day <= LastWholeDay) started++; perDay[x.Day] = perDay.TryGetValue(x.Day, out int c) ? c + 1 : 1; }
                foreach (var kv in perDay) if (kv.Value > 1) twice++;
                for (int i = 0; i < s.RollDays.Count; i++) if (s.RollDays[i] <= LastWholeDay && s.RollRain[i]) planned++;
            }
            Line(started == planned && twice == 0, $"{started} spells started on {planned} planned rain days; days with two spells: {twice}");

            // 4. the whole spell lies in full daylight
            int badLen = 0, badWindow = 0, badFade = 0; float minLen = float.MaxValue, maxLen = 0f, minAfter = float.MaxValue, maxEnd = 0f;
            foreach (var s in all) foreach (var x in s.Spells)
            {
                float len = x.P.Total; if (len < OzRainDayPlan.MinSeconds - 0.01f || len > OzRainDayPlan.MaxSeconds + 0.01f) badLen++;
                minLen = Math.Min(minLen, len); maxLen = Math.Max(maxLen, len);
                float after = x.Drops - x.Roll, end = x.End - x.Roll;                                         // seconds after that day's sunrise roll
                minAfter = Math.Min(minAfter, after); maxEnd = Math.Max(maxEnd, end);
                if (after < OzRainDayPlan.FirstDropsFrom * Cycle - 0.25f || end > OzRainDayPlan.OverBy * Cycle + 0.25f) badWindow++;
                if (Math.Abs(x.P.FadeIn - 15f) > 0.01f || Math.Abs(x.P.FadeOut - 25f) > 0.01f || Math.Abs(x.P.WindLead - 8f) > 0.01f || Math.Abs(x.P.WindTail - 15f) > 0.01f) badFade++;
            }
            Line(badLen == 0 && spells > 0, $"spell length {minLen:0} to {maxLen:0} s (wanted 90 to 150): {badLen} outside");
            Line(badWindow == 0 && spells > 0, $"first drops {minAfter:0} s after that day's sunrise at the earliest (wanted 50), every spell over {maxEnd:0} s after it at the latest (wanted 230 = clock 0.22): {badWindow} outside");
            Line(badFade == 0 && spells > 0, $"15 s fade in, 25 s fade out, wind 8 s before the first drops and 15 s after the last: {badFade} spells differ");

            // 5. the rain curve
            int badCurve = 0; float lowBody = 1f, highBody = 0f;
            foreach (var s in all) foreach (var x in s.Spells)
            {
                var p = x.P; bool bad = OzRainDayPlan.Intensity(p, -0.5f) != 0f || OzRainDayPlan.Intensity(p, p.Total + 0.5f) != 0f;
                float prev = 0f;
                for (float t = 0f; t <= p.Total; t += 1f)
                {
                    float v = OzRainDayPlan.Intensity(p, t);
                    if (t <= 10f && v < prev - 0.02f) bad = true;                                             // rises through the fade in
                    if (t >= p.FadeIn && t <= p.Total - p.FadeOut) { lowBody = Math.Min(lowBody, v); highBody = Math.Max(highBody, v); }
                    prev = v;
                }
                if (OzRainDayPlan.WindK(p, -p.WindLead - 0.5f) != 0f || OzRainDayPlan.WindK(p, 0f) < 0.999f || OzRainDayPlan.WindK(p, p.Total) < 0.999f || OzRainDayPlan.WindK(p, p.Total + p.WindTail + 0.5f) != 0f) bad = true;
                if (bad) badCurve++;
            }
            Line(badCurve == 0 && lowBody >= 0.795f && highBody <= 1.0001f, $"between the fades the rain stays between {lowBody:0.00} and {highBody:0.00} (wanted 0.80 to 1.00); spells with a wrong start, end or wind curve: {badCurve}");

            // 6. thunder: 2 to 4 soft far rumbles, none in the first 15 s, at least 12 s apart
            int badCount = 0, badQuiet = 0, badGap = 0, badLate = 0, rumbles = 0; float minGap = float.MaxValue, firstRumble = float.MaxValue; var perCount = new int[6];
            foreach (var s in all) foreach (var x in s.Spells)
            {
                var r = x.P.Rumbles; rumbles += r.Length; if (r.Length < 2 || r.Length > 4) badCount++; perCount[Math.Min(5, r.Length)]++;
                for (int i = 0; i < r.Length; i++)
                {
                    firstRumble = Math.Min(firstRumble, r[i].At);
                    if (r[i].At < OzRainDayPlan.RumbleQuiet - 0.001f) badQuiet++;
                    if (r[i].At > x.P.Total) badLate++;
                    if (i > 0) { float gap = r[i].At - r[i - 1].At; minGap = Math.Min(minGap, gap); if (gap < OzRainDayPlan.RumbleGap - 0.001f) badGap++; }
                }
            }
            Line(badCount == 0 && badQuiet == 0 && badGap == 0 && badLate == 0 && spells > 0,
                $"{rumbles} rumbles: spells with 2 / 3 / 4 = {perCount[2]} / {perCount[3]} / {perCount[4]}, other counts {badCount}; in the first 15 s {badQuiet} (earliest {firstRumble:0.0} s); nearer than 12 s to the one before {badGap} (nearest {minGap:0.0} s); after the rain {badLate}");

            // 7. one wind direction per spell, any of 360 degrees
            var sector = new int[8];
            foreach (var s in all) foreach (var x in s.Spells) sector[((int)(x.P.WindAzimuth / 45f)) & 7]++;
            int minSector = int.MaxValue, maxSector = 0; foreach (int c in sector) { minSector = Math.Min(minSector, c); maxSector = Math.Max(maxSector, c); }
            Line(spells > 0 && minSector >= spells / 16, $"wind directions over the eight compass sectors: {string.Join(" / ", sector)} (every sector at least half of an even share: {spells / 16})");

            // 8. the same start gives the same rain; another weather salt gives another
            int same = 0, differ = 0;
            for (int k = 1; k <= starts; k++) { var again = Play(1, k, Minutes, true); if (Sign(again) == Sign(all[k - 1])) same++; }
            for (int k = 1; k < starts; k++) if (Sign(all[k]) != Sign(all[k - 1])) differ++;
            Line(same == starts, $"the same start played twice gives the same spells: {same} of {starts}");
            Line(differ >= (starts - 1) * 9 / 10, $"neighbouring starts differ: {differ} of {starts - 1}");

            // 9. the WEATHER option off
            int offSpells = 0, offRolls = 0;
            for (int k = 1; k <= starts; k++) { var s = Play(1, k, Minutes, false); offSpells += s.Spells.Count; offRolls += s.Rolls; }
            Line(offSpells == 0 && offRolls > 0, $"WEATHER off: {offSpells} spells in {starts} starts ({offRolls} day rolls were made and passed over)");

            // 10. the second lock: a match whose clock passes a sunrise seven seconds in gets no rain before 7:00 of play
            int lockEarly = 0, lockSkipped = 0, lockLater = 0;
            for (int k = 1; k <= starts; k++)
            {
                var s = Play(1, k, 30f, true, 0.89f);
                lockSkipped += s.Skipped;
                foreach (var x in s.Spells) { if (x.Wind < OzRainDayPlan.MinPlaySeconds) lockEarly++; else lockLater++; }
            }
            Line(lockEarly == 0 && lockSkipped > 0, $"clock started just before a sunrise (the roll comes 7 s into play): spells before 7:00 of play {lockEarly}; passed over by the lock {lockSkipped}; spells later in those matches {lockLater}");

            // 11. the briefing holds the clock: play time, not real time, counts
            int holdEarly = 0, holdSame = 0;
            for (int k = 1; k <= 20; k++)
            {
                var s = Play(1, k, Minutes, true, StartClock, 90f);
                foreach (var x in s.Spells) if (x.Wind < SecondSunrise) holdEarly++;
                if (Sign(s) == Sign(all[k - 1])) holdSame++;
            }
            Line(holdEarly == 0 && holdSame == 20, $"a 90 s briefing in front (the clock stands): spells before the second sunrise of play {holdEarly}; the same spells at the same play times in {holdSame} of 20 starts");

            // 12. a clock that is set past the start (a proof jump) skips that day's spell: it is never started late
            int jumpStarted = 0, jumpSkipped = 0, jumpTried = 0;
            for (int k = 1; k <= starts && jumpTried < 20; k++)
            {
                if (all[k - 1].RollRain.Count == 0 || !all[k - 1].RollRain[0]) continue;                      // a start whose second day rains
                jumpTried++;
                var s = Play(1, k, 20f, true, StartClock, 0f, 2, 0.30f);
                foreach (var x in s.Spells) if (x.Day == 2) jumpStarted++;
                jumpSkipped += s.Skipped;
            }
            Line(jumpTried == 20 && jumpStarted == 0 && jumpSkipped >= 20, $"clock set from 0.95 to 0.30 on a rainy second day, 20 starts: spells started that day {jumpStarted}, passed over {jumpSkipped}");

            // 13. the wind TURNS to the spell's direction and back: no snap and no calm, also when the spell blows against the calm wind
            float cwx = OzRainDayPlan.CalmWindX, cwz = OzRainDayPlan.CalmWindZ, calmSpeed = (float)Math.Sqrt(cwx * cwx + cwz * cwz);
            float calmAz = (float)(Math.Atan2(cwx, cwz) * 180.0 / Math.PI);
            float worstTurn = 0f, lowSpeed = float.MaxValue, straightTurn = 0f, straightLow = float.MaxValue; int walked = 0, against = 0;
            float Turn(float ax, float az, float bx, float bz)
            {
                double la = Math.Sqrt(ax * ax + az * az), lb = Math.Sqrt(bx * bx + bz * bz);
                if (la < 1e-6 || lb < 1e-6) return 180f;                                                      // a dead calm has no direction: counted as the worst
                double c = (ax * bx + az * bz) / (la * lb); c = Math.Max(-1.0, Math.Min(1.0, c));
                return (float)(Math.Acos(c) * 180.0 / Math.PI);
            }
            void Walk(OzRainSpell p)
            {
                double az = p.WindAzimuth * Math.PI / 180.0; float rx = (float)Math.Sin(az), rz = (float)Math.Cos(az), rs = OzRainDayPlan.RainWindSpeed;
                float off = Math.Abs(p.WindAzimuth - calmAz - 180f); while (off > 180f) off = Math.Abs(off - 360f);
                walked++; if (off <= 20f) against++;
                float px = cwx, pz = cwz, qx = cwx, qz = cwz;
                int steps = (int)Math.Ceiling(OzRainDayPlan.WholeSeconds(p) / 0.05f);                         // 20 steps a second: a slow frame
                for (int i = 0; i <= steps; i++)
                {
                    float k = OzRainDayPlan.WindK(p, -p.WindLead + i * 0.05f);
                    OzRainDayPlan.BlendWind(cwx, cwz, rx, rz, rs, k, out float x, out float z);
                    worstTurn = Math.Max(worstTurn, Turn(px, pz, x, z)); lowSpeed = Math.Min(lowSpeed, (float)Math.Sqrt(x * x + z * z)); px = x; pz = z;
                    float sx = cwx + (rx * rs - cwx) * k, sz = cwz + (rz * rs - cwz) * k;                     // two arrows blended in a straight line, for comparison
                    straightTurn = Math.Max(straightTurn, Turn(qx, qz, sx, sz)); straightLow = Math.Min(straightLow, (float)Math.Sqrt(sx * sx + sz * sz)); qx = sx; qz = sz;
                }
            }
            foreach (var s in all) foreach (var x in s.Spells) Walk(x.P);
            int planned13 = walked;
            foreach (float d in new[] { -20f, -10f, -4f, -2f, 0f, 2f, 4f, 10f, 20f })
            {
                float a = calmAz + 180f + d; a -= 360f * (float)Math.Floor(a / 360f);
                Walk(OzRainDayPlan.Forced(OzRainDayPlan.DaySeed(1, 1, 9000 + (int)d), 90f, a));
            }
            Line(walked == planned13 + 9 && worstTurn < 10f && lowSpeed >= calmSpeed - 0.001f,
                $"the wind turns, it does not snap: {planned13} planned spells and 9 set against the calm wind (within 20 degrees of it: {against} in all), from the first stir to the last, 20 steps a second: largest turn between two steps {worstTurn:0.00} deg (wanted under 10), lowest speed {lowSpeed:0.00} m/s (wanted the calm wind's {calmSpeed:0.00} at the least); two arrows blended in a straight line would turn {straightTurn:0} deg in one step and drop to {straightLow:0.00} m/s");

            return (pass == total ? "PASS " : "FAIL ") + pass + "/" + total + " lines: level 16 rain days, " + starts + " seeded starts (game seed 1, weather salts 1 to " + starts + "), day of 720 s from clock 0.935, 50 steps a second\n" + sb;
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Count Rain Days On 200 Starts (J9)")]
        static void Menu()
        {
            string text = Run(200);
            System.IO.File.WriteAllText(System.IO.Path.Combine(OzL16Weather.OutDir(), "OzV11RainCount_editor.txt"), text);
            if (text.StartsWith("PASS")) UnityEngine.Debug.Log("[OpenZ J9] " + text); else UnityEngine.Debug.LogError("[OpenZ J9] " + text);
        }
#endif
    }
}
