#if UNITY_EDITOR
// OpenZ (GPL-3.0) - epic flybys: saved, replayable flyby scenarios recorded frame by frame with their world sound (editor only).
// The take is the menu flybys' way (CaptureKit.RecordCine: fixed step through the Game view at the take's size, JPG frames, the heat
// hold every 30 frames) with the sound rendered offline in step with the frames (AudioRenderer, 48 kHz float WAV). New here:
//  - the data: one scenario file per flyby (OzEpicFlyby.Data.cs): clock, weather, seed, units and their start states, timed orders,
//    the camera keys with seconds, lens and hard cuts, the mix;
//  - NEARLY THE SAME BATTLE every run: fixed steps from the moment the units are placed (the pre-roll runs settle_s x fps steps,
//    then the world is held while the capture starts), the game's random streams seeded from the file. Not bit for bit: the
//    crocodiles' hunt starts and cool-downs hash the level's tick count (it differs with the real-time briefing before the take) and
//    the crocodiles stand elsewhere when the units are placed, so a catch may come, go or move between runs (the recorder compares
//    the 4K take log with the preview's and says DIFFERS);
//  - every take starts from a fresh level: a take runs only in a Play session that has not staged one (out of Play the request
//    enters Play itself; the recorder leaves Play after each take), so no squad, lounger, flag or gun of an earlier take is left;
//  - the camera runs this file's own spline (OzEpicFlyby.Camera.cs) on top of the capture kit's cine mode (the painted flyby sky
//    and its sun disc, HUD off), and steps aside / up when a living unit comes within 3 m of the lens (4.5 m for a vehicle);
//  - a 1920 x 1080 preview of the same take (same steps, nearly the same battle) to check a path cheaply before the 3840 x 2160 pass.
// Session-only: the mix, the voice gate, the clock, the lounge wake distance and the campfire burn time are put back when the take
// ends; the game's rules and looks for normal play are untouched. Run: menu OpenZ/Epic Flybys, or OzEpicFlyby.Request(...).
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    public sealed partial class OzEpicFlyby : MonoBehaviour
    {
        public const string PendingKey = "oz_epic_flyby_pending", OutDirPref = "oz_epic_flyby_out";
        public static OzEpicFlyby I { get; private set; }
        /// <summary>Where the take is: idle, starting, loading ..., waiting ..., staging, pre-roll, recording, writing, done, failed: ...</summary>
        public static string Phase = "idle";
        public static string CurrentName = "", CurrentMode = "", CurrentDir = "", LastResult = "none";
        public static int FrameNow = -1, FramesWanted;
        static readonly List<string> log = new List<string>();

        Scenario sc; string mode = "4k"; string outRoot = "", dir = "", stem = "";
        int W4 = 3840, H4 = 2160, fps = 60, n;
        ZGame g; ZWorld W; CaptureKit ck; Camera cam; RtsCamera rts;
        bool frozen;

        static void Note(string s) { log.Add(s); Debug.Log("[OpenZ EpicFlyby] " + s); }

        /// <summary>The folder the frames, the sound and the logs go to: the editor setting oz_epic_flyby_out, else the project's Temp.</summary>
        public static string DefaultOutDir()
        {
            var p = UnityEditor.EditorPrefs.GetString(OutDirPref, "");
            return !string.IsNullOrEmpty(p) ? p : Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp", "EpicFlybys").Replace('\\', '/');
        }

        /// <summary>Runs a scenario (mode "preview" = 1920 x 1080, "4k" = 3840 x 2160) from a fresh level: only out of Play mode. It
        /// enters Play, the take starts by itself, loads Level 16 if needed and waits for the level-start briefing to end. In Play mode
        /// it is refused (leave Play first): an earlier take's squads, loungers, flag and gun, or a played match, would be in the take.</summary>
        public static string Request(string name, string mode, string outDir = null)
        {
            if (string.IsNullOrEmpty(outDir)) outDir = DefaultOutDir();
            var err = Load(name, out _); if (err != null) return err;
            if (Application.isPlaying)
                return Busy ? "a take is running: " + CurrentName + " (" + Phase + ")"
                            : "leave Play mode first: every take starts from a fresh level (out of Play mode the take enters Play by itself; the recorder leaves Play for you)";
            UnityEditor.SessionState.SetString(PendingKey, name + "|" + mode + "|" + outDir);
            Phase = "entering Play"; CurrentName = name; CurrentMode = mode == "preview" ? "preview" : "4k"; CurrentDir = ""; FrameNow = -1; LastResult = "none";
            UnityEditor.EditorApplication.EnterPlaymode();
            return "entering Play mode: the take " + name + " (" + mode + ") starts by itself when the level is ready";
        }

        /// <summary>True once a take has placed its units in this Play session (a second take would run on top of them).</summary>
        static bool stagedThisPlay;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void NewPlaySession() { stagedThisPlay = false; }                                     // also when the editor keeps the scripts loaded between Play sessions

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Pending()
        {
            var p = UnityEditor.SessionState.GetString(PendingKey, "");
            if (string.IsNullOrEmpty(p)) return;
            UnityEditor.SessionState.EraseString(PendingKey);
            var f = p.Split('|');
            if (f.Length >= 3) Debug.Log("[OpenZ EpicFlyby] " + Begin(f[0], f[1], f[2]));
        }

        public static bool Busy => I && Phase != "idle" && Phase != "done" && Phase != "aborted" && !Phase.StartsWith("failed") && !Phase.StartsWith("refused");

        /// <summary>Starts the take in Play mode (called by Request through the Play-mode entry; see Request).</summary>
        public static string Begin(string name, string mode, string outDir)
        {
            if (!Application.isPlaying) return "not in Play mode (use Request)";
            if (Busy) return "a take is running: " + CurrentName + " (" + Phase + ")";
            if (stagedThisPlay)
            {
                Phase = "refused: a take already ran in this Play session"; LastResult = Phase; CurrentName = name; CurrentMode = mode == "preview" ? "preview" : "4k";
                return "refused: a take already placed its units in this Play session (its squads, loungers, flag and gun are still there): leave Play mode first, then run it again";
            }
            var err = Load(name, out var s); if (err != null) return err;
            if (I) Destroy(I.gameObject);
            var go = new GameObject("OzEpicFlyby"); DontDestroyOnLoad(go);
            I = go.AddComponent<OzEpicFlyby>();
            I.sc = s; I.mode = mode == "preview" ? "preview" : "4k";
            var size = I.mode == "preview" ? s.preview : s.full;
            I.W4 = size.width; I.H4 = size.height; I.fps = size.fps; I.n = Mathf.RoundToInt(s.seconds * I.fps);
            I.outRoot = (string.IsNullOrEmpty(outDir) ? DefaultOutDir() : outDir).Replace('\\', '/').TrimEnd('/');
            I.stem = s.output + (I.mode == "preview" ? "_preview" : "");
            I.dir = I.outRoot + "/" + I.stem;
            log.Clear(); Phase = "starting"; CurrentName = s.name; CurrentMode = I.mode; CurrentDir = I.dir; FrameNow = -1; FramesWanted = I.n;
            foreach (var f in new[] { I.dir + ".done", I.dir + ".wav", I.dir + "_takelog.txt", I.dir + "_soundlog.txt" }) if (File.Exists(f)) File.Delete(f);
            I.StartCoroutine(I.Run());
            return "take " + s.name + " (" + I.mode + ", " + I.W4 + "x" + I.H4 + " " + I.fps + " fps, " + s.seconds + " s) started: frames to " + I.dir;
        }

        /// <summary>One line for the recorder: phase, frame, the take's folder.</summary>
        public static string Status() =>
            "phase=" + Phase + ";name=" + CurrentName + ";mode=" + CurrentMode + ";frame=" + FrameNow + ";frames=" + FramesWanted + ";dir=" + CurrentDir +
            ";play=" + Application.isPlaying + ";level=" + (ZGame.I ? ZGame.I.level : "-") + ";brief=" + (Application.isPlaying && ZodBriefing.BriefingActive) +
            ";screen=" + Screen.width + "x" + Screen.height + ";cfr=" + Time.captureFramerate + ";result=" + LastResult;

        /// <summary>Stops a running take (frames so far stay on disk, no .done marker) and puts the session back.</summary>
        public static string Abort()
        {
            if (!I) return "no take";
            if (!Busy) return "no take running (" + Phase + "; " + LastResult + ")";                    // a finished take keeps its result
            I.StopAllCoroutines();
            if (CaptureKit.I) CaptureKit.I.StopAllCoroutines();
            Time.captureFramerate = 0; Time.timeScale = 1f;
            I.Restore();
            Phase = "aborted"; LastResult = "aborted at frame " + FrameNow;
            return LastResult;
        }

        void Fail(string why) { Restore(); Phase = "failed: " + why; LastResult = Phase; Note(Phase); }

        IEnumerator Run()
        {
            float t0 = Time.realtimeSinceStartup;
            // ---- the level
            g = ZGame.I;
            if (!g || g.World == null || g.level != sc.level)
            {
                Phase = "loading " + sc.scene; Time.captureFramerate = 0; Time.timeScale = 1f;
                UnityEngine.SceneManagement.SceneManager.LoadScene(sc.scene);
                yield return null; yield return null;
            }
            Phase = "waiting for the level";
            while (true)
            {
                g = ZGame.I;
                if (g && g.World != null && g.level == sc.level && Time.timeSinceLevelLoad > 3f) break;
                if (Time.realtimeSinceStartup - t0 > 240f) { Fail("the level " + sc.level + " did not come up in 240 s"); yield break; }
                yield return null;
            }
            Phase = "waiting for the briefing to end";
            while (ZodBriefing.BriefingActive)
            {
                if (Time.realtimeSinceStartup - t0 > 400f) { Fail("the level-start briefing did not end"); yield break; }
                yield return null;
            }
            yield return new WaitForSecondsRealtime(1f);
            W = g.World; ck = CaptureKit.I;
            if (!ck) { Fail("no capture kit"); yield break; }
            rts = FindFirstObjectByType<RtsCamera>(FindObjectsInactive.Include); cam = rts ? rts.GetComponent<Camera>() : Camera.main;
            if (!cam) { Fail("no game camera"); yield break; }
            // ---- the picture size: the Game view at the take's size (that path keeps the bloom of the sun disc and the night fires)
            Phase = "game view";
            OzFinalGateView.SetFixed(W4, H4, "OpenZ epic flyby " + W4 + "x" + H4, out var info); Note(info);
            float tw = Time.realtimeSinceStartup;
            while ((Screen.width != W4 || Screen.height != H4) && Time.realtimeSinceStartup - tw < 8f) yield return null;
            Note("screen " + Screen.width + "x" + Screen.height + (Screen.width == W4 && Screen.height == H4 ? " = the take's size" :
                 " (NOT the take's size: the frames are rendered into a texture of " + W4 + "x" + H4 + ", without the Game view's bloom)"));
            // ---- fixed steps from here on: the same file gives the same battle
            Directory.CreateDirectory(dir);
            Time.captureFramerate = fps; Time.timeScale = 1f;
            Phase = "staging"; stagedThisPlay = true;
            try { ApplySession(); Stage(); BuildPath(); Hook(); }
            catch (Exception e) { Fail("staging: " + e.GetType().Name + " " + e.Message + "\n" + e.StackTrace); yield break; }
            Phase = "pre-roll";
            int pre = Mathf.Max(2, Mathf.RoundToInt(sc.settle_s * fps)), later = Mathf.Min(pre - 1, Mathf.RoundToInt(0.6f * fps));
            for (int k = 0; k < pre; k++)
            {
                if (k == later || k == later + fps / 2 || k == later + fps)                              // the loungers: tried three times, half a second apart
                { try { Loungers(); Props(); } catch (Exception e) { Note("WARNING loungers / props: " + e.Message); } }
                try { LoungeAtStep(k, pre); } catch (Exception e) { Note("WARNING lounge_at: " + e.Message); }     // the scenario's own loungers (OzEpicFlyby.Island.cs)
                yield return null;
            }
            // the world holds (no game time) while the capture kit settles its 1 real second; frame 0 lets it go again
            Time.timeScale = 0f; frozen = true;
            Phase = "recording";
            Note("take " + sc.name + " (" + mode + "): " + W4 + "x" + H4 + ", " + n + " frames at " + fps + " fps, pre-roll " + pre + " steps, clock " + sc.time_of_day +
                 ", seed " + sc.seed + ", " + sc.camera.Length + " camera keys in " + shots.Count + " shot(s)");
            yield return ck.StartCoroutine(ck.RecordCine(dir, sc.seconds, fps, sc.time_of_day, W4, H4, SplineHint(true), SplineHint(false), 1f));
            Phase = "writing";
            try { Finish(); }
            catch (Exception e) { Fail("writing: " + e.Message); yield break; }
        }

        // ------------------------------------------------------------------ the session (all put back by Restore)
        Dictionary<string, float> busSave; Dictionary<string, float> busTable; bool voiceGateSet; float wakeSave = -1f, burnSave = -1f;
        System.Reflection.FieldInfo speakingF;

        void ApplySession()
        {
            // seeds: the game's own streams and the view's
            W.Rng = new ZRng(sc.seed);
            var df = typeof(ZWorld).GetField("DuelRng", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (df != null && df.FieldType == typeof(ZRng)) df.SetValue(W, new ZRng(unchecked(sc.seed * 7919 + 104729)));
            UnityEngine.Random.InitState(sc.seed);
            // the clock (held) and the weather
            var dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.running = false; dn.timeOfDay = sc.time_of_day; dn.Apply(sc.time_of_day); }
            if (OzWeather.I)
            {
                if (sc.weather == "storm") OzWeather.I.ForceStorm(sc.seconds + sc.settle_s + 30f, false);
                else if (OzWeather.I.StormActive) OzWeather.I.StopStorm(0.5f);
            }
            CaptureKit.NoFlybySky = !sc.flyby_sky; FlybySky.ForceOn = sc.flyby_sky;
            // the mix: music, UI and every voice bus at -80 dB, the voice gate, the music off (world sounds stay)
            var ad = AudioDirector.I;
            if (sc.audio.music_off) AudioDirector.SessionMusicOff = true;
            var bf = typeof(AudioDirector).GetField("busDb", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            busTable = bf != null && ad ? bf.GetValue(ad) as Dictionary<string, float> : null;
            if (busTable != null)
            {
                busSave = new Dictionary<string, float>(busTable);
                foreach (var b in sc.audio.mute_buses) busTable[b] = -80f;
                Note("muted buses: " + string.Join(", ", sc.audio.mute_buses));
            }
            else Note("WARNING no bus table: the buses are NOT muted");
            var mt = typeof(AudioDirector).GetMethod("OnMusicToggle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (sc.audio.music_off && mt != null && ad) mt.Invoke(ad, new object[] { false });
            speakingF = typeof(OpenZVictory).GetField("speaking", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (sc.audio.voice_gate && OpenZVictory.I && speakingF != null) { speakingF.SetValue(OpenZVictory.I, true); voiceGateSet = true; Note("voice gate on: no voice line starts"); }
            else if (sc.audio.voice_gate) Note("WARNING the voice gate was not found (the voice buses are still at -80 dB)");
            // the CPU players out of the take (the staged units fight by their orders and the rules)
            W.Bots.Clear();
            // loungers: the robots on them get up when an enemy comes this close (session only)
            if (sc.lounge_wake_m > 0f) { wakeSave = AnticDirector.CancelThreatM; AnticDirector.CancelThreatM = sc.lounge_wake_m; }
            burnSave = OzHeroCampfire.StartBurnSeconds; OzHeroCampfire.StartBurnSeconds = 1e6f;      // the take's campfires burn by day too
            rtsSave = new Vector4(rts ? rts.focus.x : 0f, rts ? rts.focus.y : 0f, rts ? rts.focus.z : 0f, rts ? rts.height : 70f); fovSave = cam.fieldOfView;
            Note("clock " + sc.time_of_day + " held, weather " + (string.IsNullOrEmpty(sc.weather) ? "clear" : sc.weather) + ", flyby sky " + sc.flyby_sky + ", seed " + sc.seed);
        }

        Vector4 rtsSave; float fovSave; bool restored;

        void Restore()
        {
            if (restored) return; restored = true;
            Unhook();
            if (busTable != null && busSave != null) foreach (var kv in busSave) busTable[kv.Key] = kv.Value;
            AudioDirector.SessionMusicOff = false;
            if (voiceGateSet && OpenZVictory.I && speakingF != null) speakingF.SetValue(OpenZVictory.I, false);
            if (wakeSave >= 0f) AnticDirector.CancelThreatM = wakeSave;
            if (burnSave >= 0f) OzHeroCampfire.StartBurnSeconds = burnSave;
            CaptureKit.NoFlybySky = false; FlybySky.ForceOn = false;
            if (cam) cam.fieldOfView = fovSave > 1f ? fovSave : cam.fieldOfView;
            if (rts) { rts.focus = new Vector3(rtsSave.x, rtsSave.y, rtsSave.z); rts.SetHeightImmediate(rtsSave.w); }
            Time.captureFramerate = 0; Time.timeScale = 1f;
            ReleaseBirds(); ReleaseSfx(); IslandRestore(); Kf4Restore();
        }

        void OnDestroy() { if (I == this) { Restore(); } }

        // ------------------------------------------------------------------ the end of the take: sound file, logs, the marker
        void Finish()
        {
            if (audioStarted) AudioRenderer.Stop();
            Drain(true);
            Time.captureFramerate = 0; Time.timeScale = 1f;
            int want = n * per * ch;
            Note("sound: " + samples.Count / Mathf.Max(1, ch) + " sample frames for " + n + " frames x " + per + " (render fails " + renderFails + ", count mismatches " + countMismatch + ", dropped after heat holds " + dropped + ")");
            while (samples.Count < want) samples.Add(0f);
            if (samples.Count > want) samples.RemoveRange(want, samples.Count - want);
            using (var fs = new FileStream(dir + ".wav", FileMode.Create))
            using (var bw = new BinaryWriter(fs))
            {
                int bytes = samples.Count * 4;
                bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); bw.Write(36 + bytes); bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt ")); bw.Write(16); bw.Write((short)3); bw.Write((short)ch); bw.Write(rate); bw.Write(rate * ch * 4); bw.Write((short)(ch * 4)); bw.Write((short)32);
                bw.Write(System.Text.Encoding.ASCII.GetBytes("data")); bw.Write(bytes);
                foreach (var s in samples) bw.Write(s);
            }
            var sb = new System.Text.StringBuilder(); int total = 0;
            foreach (var kv in keyCount)
            {
                total += kv.Value;
                sb.Append(kv.Value.ToString().PadLeft(6)).Append("  maxvol ").Append(keyVol[kv.Key].ToString("F3")).Append("  ").Append(kv.Key).Append("\n");
            }
            File.WriteAllText(dir + "_soundlog.txt", "sound log of " + stem + ": " + total + " entries, " + keyCount.Count + " distinct (count, loudest volume, source | key | outcome)\n" + sb);
            int nf = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.jpg").Length : 0;
            int voices = AudibleVoices();
            Note("frames on disk " + nf + " of " + n + "; peak " + audioPeak.ToString("F3") + "; crocodile hunts " + (W.CrocHunts - crocHunts0) + ", robots taken " + (W.CrocKills - crocKills0));
            Note("lens clearance: closest living unit to the real lens " + (minLens < float.MaxValue ? minLens.ToString("F1") + " m (" + minLensWho + ", frame " + minLensFrame + ")" : "- (no unit)") +
                 "; the lens stepped aside / up on " + dodgeFrames + " frames (at most " + dodgeMax.ToString("F1") + " m)" +
                 "; the capture kit's own figure on its helper spline " + CaptureKit.CineMinUnit.ToString("F1") + " m (not this take's path)");
            LogFires("the end");
            Note("alive red/blue (staged) " + alive + "; robot voices stopped " + silenced);
            Note("audible robot / announcer voices: " + voices + " (voice-bus plays heard " + busVoices + ", robot voice clips of the animal pool played " + poolVoices + ", stopped " + poolStops + ")");
            IslandSummary();
            Kf4Summary();                                                                          // round 4: RTS distance, flock at frame 0, croc_show, lasers (OzEpicFlyby.Kf4.cs)
            ReleaseBirds();
            File.WriteAllText(dir + "_takelog.txt", string.Join("\n", log.ToArray()) + "\n");
            Restore();
            File.WriteAllText(dir + ".done", nf == n ? "ok" : "frames " + nf + " of " + n);
            Phase = "done"; LastResult = stem + ": " + nf + " of " + n + " frames, sound " + (samples.Count / Mathf.Max(1, ch)) + " sample frames, audible robot / announcer voices " + voices;
        }
    }
}
#endif
