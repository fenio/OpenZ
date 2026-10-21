using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Version 1.1 kit KW, the counted Play check of the option NIGHT &amp; RAIN (menu OpenZ/V1.1/KW Night And Rain Option: Play
    /// Check (this level)). In Play on ONE level (L02, L16 or L17), after the level-start briefing, with the camera where the
    /// pictures should be taken (the normal game camera at the standard zoom). About three minutes of game time:
    /// <list type="bullet">
    /// <item>1. ON: the clock sped up (a day in 36 s) reaches the night; then a forced night storm (city level) or a forced rain
    /// spell (jungle level) at the normal clock speed; picture 1 (night and rain).</item>
    /// <item>2. The option switched OFF in the middle of it (as the menus do it): day again within 4 s with no cut, the rain gone
    /// in 3 s, the ground dry or nearly after 5 s, no flash, thunder or rumble after the switch, the rain sounds gone; picture 2
    /// (5 s after the switch).</item>
    /// <item>3. OFF, the clock sped up (a day in 24 s) for 60 s of game time (two and a half days): no frame with any night shade,
    /// the clock stands at the day time, no storm, spell, flash or thunder, nothing wet; forcing a storm, a spell or a flash is
    /// refused; a jump of the clock to night (the night debug jump) travels back to day in 4 s; picture 3.</item>
    /// <item>4. ON again (a day in 24 s): the night comes back; picture 4.</item>
    /// </list>
    /// Then the clock speed, the clock, the option and the pause are put back. Report and pictures:
    /// </summary>
    public static class OzKwNightRainCheck
    {
        const string Tag = "[OpenZ KW]";
        public static string Report = "not run";
        static IEnumerator run; static float waitUntil, realLimit;
        static readonly StringBuilder text = new StringBuilder();
        static int ok, bad;
        static bool kept, option0, running0, paused0; static float cycle0, clock0;
        static string dir, level;

        [MenuItem("OpenZ/V1.1/KW Night And Rain Option: Play Check (this level)")]
        static void Run()
        {
            var g = ZGame.I;
            if (!EditorApplication.isPlaying || !g || g.World == null) { Debug.LogWarning(Tag + " enter Play on a level first"); return; }
            if (run != null) { Debug.LogWarning(Tag + " a run is still going"); return; }
            if (ZodFlyover.Live != null && ZodFlyover.Live.Active) { Debug.LogWarning(Tag + " the level-start briefing is still flying: run it when the camera is yours"); return; }
            if (!Object.FindFirstObjectByType<DayNight>()) { Debug.LogWarning(Tag + " this scene has no day clock (DayNight)"); return; }
            ok = bad = 0; text.Clear(); Report = "running"; level = g.level; dir = OutDir();
            run = Body(); waitUntil = 0f; realLimit = Time.realtimeSinceStartup + 900f;
            EditorApplication.update -= Step; EditorApplication.update += Step;
            Debug.Log(Tag + " running on " + level + " (about 3 minutes of game time; leave the game alone). Report and pictures -> " + dir.Replace('\\', '/'));
        }

        static void Step()
        {
            if (run == null) { EditorApplication.update -= Step; return; }
            if (!EditorApplication.isPlaying || Time.realtimeSinceStartup > realLimit)
            {
                EditorApplication.update -= Step; run = null;
                if (EditorApplication.isPlaying) { Line(false, "the run took longer than 15 minutes of real time (paused game?)"); Finish(); } else Report = Tag + " stopped: Play was left";
                return;
            }
            if (Time.time < waitUntil) return;
            bool more;
            try { more = run.MoveNext(); }
            catch (System.Exception ex) { Debug.LogException(ex); EditorApplication.update -= Step; run = null; Line(false, "exception: " + ex.Message); Finish(); return; }
            if (!more) { EditorApplication.update -= Step; run = null; return; }
            waitUntil = run.Current is float s ? Time.time + s : 0f;                                    // a float = seconds of GAME time; anything else = the next update
        }

        static string OutDir()
        {
            string all = Path.Combine(OpenZPaths.RepoRoot, "work", "records", "milestones"), stage = "v1_1";
            if (Directory.Exists(all))
            {
                var ds = Directory.GetDirectories(all, "v1_1*");
                System.Array.Sort(ds, System.StringComparer.Ordinal);
                if (ds.Length > 0) stage = Path.GetFileName(ds[ds.Length - 1]);
            }
            string d = Path.Combine(all, stage, "for_owner", "weather_option");
            Directory.CreateDirectory(d);
            return d;
        }

        static void Line(bool pass, string what) { if (pass) ok++; else bad++; text.Append(pass ? "PASS  " : "FAIL  ").Append(what).Append('\n'); }
        static void Note(string what) { text.Append("      ").Append(what).Append('\n'); }
        static void Shot(string name)
        {
            string p = Path.Combine(dir, "kw_" + level + "_" + name + ".png");
            ScreenCapture.CaptureScreenshot(p);
            Note("picture " + p.Replace('\\', '/') + " (" + Screen.width + "x" + Screen.height + ", clock t " + NightGrade.TimeOfDay.ToString("F3") + ")");
        }

        // ------------------------------------------------------------------ what the player sees and hears, read back
        static float RainNow()
        {
            float r = Shader.GetGlobalFloat("_OzRain");
            if (OzWeather.I) r = Mathf.Max(r, OzWeather.I.Rain);
            if (OzJungleWeather.I) r = Mathf.Max(r, OzJungleWeather.I.Rain);
            return r;
        }
        static float WetNow()
        {
            float w = Shader.GetGlobalFloat("_OzWetness");
            if (OzWeather.I) w = Mathf.Max(w, OzWeather.I.Wetness);
            if (OzJungleWeather.I) w = Mathf.Max(w, Mathf.Max(OzJungleWeather.I.Wetness, OzJungleWeather.I.LowPuddleLevel));
            return w;
        }
        static readonly string[] RainLoopKeys = { "weather:rain", "wx16:bed", "wx16:leaves", "wx16:water", "wx16:metal", "wx16:planks", "wx16:wind", "wx16:drips" };
        static string RainLoopsPlaying()
        {
            var ad = AudioDirector.I; if (!ad) return "";
            var sb = new StringBuilder();
            foreach (var k in RainLoopKeys) if (ad.LoopActive(k)) sb.Append(sb.Length > 0 ? ", " : "").Append(k);
            return sb.ToString();
        }
        static int Storms => OzWeather.I ? OzWeather.I.StormsStarted : 0;
        static int Flashes => OzWeather.I ? OzWeather.I.FlashesFired : 0;
        static int Thunders => OzWeather.I ? OzWeather.I.ThundersPlayed : 0;
        static int Spells => OzJungleWeather.I ? OzJungleWeather.I.SpellsStarted : 0;
        static int Rumbles => OzJungleWeather.I ? OzJungleWeather.I.RumblesPlayed : 0;

        // ------------------------------------------------------------------ the run
        static IEnumerator Body()
        {
            var g = ZGame.I; var dn = Object.FindFirstObjectByType<DayNight>();
            var wx = OzWeather.I; var jw = OzJungleWeather.I;
            option0 = OzWeather.Enabled; running0 = dn.running; cycle0 = dn.cycleSeconds; clock0 = dn.timeOfDay; paused0 = g.paused; kept = true;
            g.paused = false;
            Note($"{level}: city night storm {(wx ? "there" : "not on this level")}, jungle day rain {(jw ? "there" : "not on this level")}; option was {(option0 ? "ON" : "OFF")}, clock t {clock0:F3}, a day = {cycle0:0} s");

            // ---- 1. ON: the clock sped up reaches the night; then night rain (forced) at the normal clock speed
            OzWeather.Enabled = true; dn.running = true; dn.cycleSeconds = 36f;
            float t0 = Time.time, maxN = 0f;
            while (Time.time - t0 < 45f && !(NightGrade.NightK > 0.97f && dn.timeOfDay > 0.58f && dn.timeOfDay < 0.72f)) { maxN = Mathf.Max(maxN, NightGrade.NightK); yield return null; }
            maxN = Mathf.Max(maxN, NightGrade.NightK);
            dn.cycleSeconds = cycle0;                                                                // the night now lasts for minutes
            Line(maxN > 0.95f, $"ON: the clock sped up reaches the night (night shade {maxN:F2} at clock t {dn.timeOfDay:F3} after {Time.time - t0:0.0} s of game time)");
            int st0 = Storms, fl0 = Flashes, sp0 = Spells;
            if (wx) wx.ForceStorm(60f, false);
            if (jw) jw.ForceSpell(60f, -1f, false);
            if (wx || jw)
            {
                yield return 26f;
                float tf = Time.time;
                while (wx && Flashes == fl0 && Time.time - tf < 20f) yield return null;              // the storm's first flash
                if (wx) Line(Storms > st0 && wx.Rain > 0.4f && Flashes > fl0 && WetNow() > 0.3f, $"ON, city: a night storm runs (storms {Storms - st0}, rain {wx.Rain:F2}, wet {WetNow():F2}, flashes {Flashes - fl0}, thunders so far {Thunders})");
                if (jw) Line(Spells > sp0 && jw.Rain > 0.4f && WetNow() > 0.3f, $"ON, jungle: a rain spell runs (spells {Spells - sp0}, rain {jw.Rain:F2}, wet {WetNow():F2}, wind {jw.WindK:F2})");
                Note("rain sounds playing: " + RainLoopsPlaying());
            }
            else Note("no weather on this level: only the clock is checked");
            Shot("1_on_night" + (wx || jw ? "_rain" : ""));
            yield return 0.5f;

            // ---- 2. switched OFF in the middle of it
            OzNightRain.ResetCounters();
            int fl1 = Flashes, th1 = Thunders, rm1 = Rumbles; float rainBefore = RainNow(), wetBefore = WetNow(), nBefore = NightGrade.NightK, tBefore = dn.timeOfDay;
            float tFlip = Time.time, dayAt = -1f, dryAt = rainBefore > 0f ? -1f : 0f;
            OzWeather.Enabled = false;
            while (Time.time - tFlip < 5f)
            {
                float s = Time.time - tFlip;
                if (dayAt < 0f && NightGrade.NightK <= 0.001f && !OzNightRain.Day.Easing) dayAt = s;
                if (dryAt < 0f && RainNow() <= 0f) dryAt = s;
                yield return null;
            }
            Shot("2_off_plus5s");
            Line(dayAt >= 0f && dayAt <= OzNightRain.Day.EaseSeconds + 0.5f && NightGrade.NightK <= 0.001f,
                $"switched OFF at night (clock t {tBefore:F3}, night shade {nBefore:F2}): day again after {dayAt:0.00} s (travel {OzNightRain.LastTravelSeconds:0.00} s), now clock t {dn.timeOfDay:F3}, night shade {NightGrade.NightK:F3}");
            Line(OzNightRain.MaxOffStep < 0.05f, $"no cut: the largest change of the clock in one frame was {OzNightRain.MaxOffStep:F4} of a day (a cut would be 0.3 or more)");
            if (wx || jw)
            {
                Line(dryAt >= 0f && dryAt <= 3.6f, $"the rain faded out in {dryAt:0.00} s (rain was {rainBefore:F2}, now {RainNow():F2})");
                Line(WetNow() <= 0.05f, $"the ground 5 s after the switch: wetness {WetNow():F3} (was {wetBefore:F2})");
                Line(Flashes == fl1 && Thunders == th1 && Rumbles == rm1, $"after the switch: flashes {Flashes - fl1}, thunders {Thunders - th1}, rumbles {Rumbles - rm1}");
                yield return 2f;
                string loops = RainLoopsPlaying();
                Line(loops.Length == 0, $"the rain sounds are gone 7 s after the switch{(loops.Length > 0 ? " (still playing: " + loops + ")" : "")}");
            }

            // ---- 3. OFF, the clock sped up: two and a half days
            OzNightRain.ResetCounters(); dn.cycleSeconds = 24f;
            int st3 = Storms, fl3 = Flashes, th3 = Thunders, sp3 = Spells;
            bool refused = !OzWeather.Force(40f) && !OzJungleWeather.Force(40f);
            if (wx) wx.ForceFlash(OzStormPlan.Close);
            t0 = Time.time; float maxRain = 0f, maxWet = 0f;
            while (Time.time - t0 < 30f) { maxRain = Mathf.Max(maxRain, RainNow()); maxWet = Mathf.Max(maxWet, WetNow()); yield return null; }
            int heldFrames = OzNightRain.OffFrames, nightFrames = OzNightRain.OffNightFrames; float heldMaxN = OzNightRain.MaxOffNightK, clockMid = dn.timeOfDay;
            // the night debug jump (menu OpenZ/Night/Jump To Night): it travels back to day
            int tr0 = OzNightRain.TravelsSeen; NightLights.JumpTo(0.62f); float tj = Time.time, backAt = -1f;
            while (Time.time - tj < 6f) { if (backAt < 0f && Time.time > tj && NightGrade.NightK <= 0.001f && !OzNightRain.Day.Easing) backAt = Time.time - tj; yield return null; }
            while (Time.time - t0 < 60f) { maxRain = Mathf.Max(maxRain, RainNow()); maxWet = Mathf.Max(maxWet, WetNow()); yield return null; }
            Shot("3_off_fast_clock");
            Line(heldFrames > 300 && nightFrames == 0 && heldMaxN <= 0.001f,
                $"OFF, a day in 24 s, 30 s of game time: {heldFrames} frames held by the day clock, {nightFrames} with any night shade (most {heldMaxN:F3}); clock t {clockMid:F3}");
            Line(Mathf.Abs(Mathf.DeltaAngle(dn.timeOfDay * 360f, OzNightRain.Day.Hold * 360f)) < 0.01f && OzNightRain.OffNightFrames == 0,
                $"OFF, 60 s of game time (two and a half days): the clock stands at t {dn.timeOfDay:F3} (the day time {OzNightRain.Day.Hold:F3}), frames with night shade outside the debug jump's travel {OzNightRain.OffNightFrames}");
            Line(Storms == st3 && Flashes == fl3 && Thunders == th3 && Spells == sp3 && refused,
                $"OFF: storms {Storms - st3}, spells {Spells - sp3}, flashes {Flashes - fl3}, thunders {Thunders - th3}; a forced storm / spell refused: {(refused ? "yes" : "NO")}, a forced flash fired nothing");
            Line(maxRain <= 0f && maxWet <= 0f, $"OFF: the most rain {maxRain:F3}, the most wetness / puddle water {maxWet:F3}");
            Line(OzNightRain.TravelsSeen == tr0 + 1 && backAt >= 0f && backAt <= OzNightRain.Day.EaseSeconds + 0.5f && OzNightRain.MaxOffStep < 0.05f,
                $"OFF: the clock jumped to night (t 0.620) travels back to day in {backAt:0.00} s (travels {OzNightRain.TravelsSeen - tr0}, largest step in one frame {OzNightRain.MaxOffStep:F4} of a day)");

            // ---- 4. ON again: the cycle comes back
            OzWeather.Enabled = true; dn.cycleSeconds = 24f; t0 = Time.time; maxN = 0f;
            while (Time.time - t0 < 30f && NightGrade.NightK < 0.95f) { maxN = Mathf.Max(maxN, NightGrade.NightK); yield return null; }
            maxN = Mathf.Max(maxN, NightGrade.NightK);
            Shot("4_on_again_night");
            Line(maxN >= 0.95f, $"ON again: the night comes back (night shade {maxN:F2} at clock t {dn.timeOfDay:F3} after {Time.time - t0:0.0} s, a day in 24 s)");
            yield return 0.5f;
            Finish();
        }

        static void PutBack()
        {
            if (!kept) return; kept = false;
            var dn = Object.FindFirstObjectByType<DayNight>();
            if (OzWeather.I) OzWeather.I.StopStorm(1f);
            if (OzJungleWeather.I) OzJungleWeather.I.StopSpell(1f);
            if (OzWeather.Enabled != option0) OzWeather.Enabled = option0;
            if (dn) { dn.cycleSeconds = cycle0; dn.running = running0; dn.timeOfDay = clock0; dn.Apply(clock0); }
            if (ZGame.I) ZGame.I.paused = paused0;
        }

        static void Finish()
        {
            PutBack();
            Report = (bad == 0 ? "PASS " : "FAIL ") + ok + "/" + (ok + bad) + " lines: kit KW, NIGHT & RAIN option, " + level + ", " + Screen.width + "x" + Screen.height + "\n" + text
                     + "      put back: option " + (option0 ? "ON" : "OFF") + ", clock t " + clock0.ToString("F3") + ", a day = " + cycle0.ToString("0") + " s\n";
            string p = Path.Combine(dir, "KW_check_" + level + ".txt");
            File.WriteAllText(p, Report);
            if (bad == 0) Debug.Log(Tag + " " + Report + "-> " + p.Replace('\\', '/')); else Debug.LogError(Tag + " " + Report + "-> " + p.Replace('\\', '/'));
        }
    }
}
