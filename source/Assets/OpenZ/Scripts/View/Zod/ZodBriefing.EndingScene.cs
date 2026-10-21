// OpenZ (GPL-3.0) - level 16 endings: the two robots on the sun loungers.
// Win: they lie on the loungers with their rocket fuel cans, far behind them the mushroom of the enemy fort; the small one points,
// the big one never looks: "See? I told you it'd be a nice little holiday for us. Cheers!", the cans meet.
// Loss: the same loungers, the mushroom behind them is their OWN fort; they missed all the action and notice only now.
//
// How it sits in the ending the game already plays (nothing of that is changed):
//   fort falls -> (win only: the announcer's line + the commander's red button) -> bomber -> impact, slow motion, mushroom, death wave
//   -> OpenZVictory.Run waits PostImpactDelay after the impact -> THIS SCENE in place of the announcer / robot lines of the other levels
//   -> OpenZVictory.Release -> the end screen with its score, over this scene's last picture.
//   WIN  (EndingSceneCommanderAfterWin, on): he follows the toast as in the original, with the line of EndingWinCommanderLine
//        (zod_win_02, "Now, boys, this is no time to celebrate! ..."), at the game's usual 1.3 s after the end screen shows;
//   LOSS (EndingSceneCommanderAfterLoss, off): he stays silent: his defeat line is followed by zod_lose_push, which opens with the
//        small robot's "What do we do now, sir?", the question the scene has just ended on.
// Any other level, a bot match, test mode, an automated check, the cameo switched off, a match that ended with NO fort
// down (one side lost all its units: no strike, no mushroom), a missing clip or cue sheet, or no bound set: EndingSceneWanted answers
// false and the level ends exactly like the others (his usual line included).
//
// The picture (loungers, parasol, the two robots, their poses, the glow of the blast on them) is NOT built here: the sun lounger set
// of the level's briefing binds itself to EndingSet. This file owns the clock, the voice, the subtitles, the sounds, the camera, the
// ears (the listener rig goes to the set with the picture), the horizon sky switch, the ducking and the mushroom's life.
// Data: Resources/OpenZ/Zod/ending_L16_cues.json (times from the cut to the loungers), the voice clips zod_end_L16_win / _lose and
// their entries in zod_lines.json (subtitles) and Boys/alan_mouth_curves.json (jaws).
// Play checks: return OpenZ.ZodBriefing.EndingStatus;   return OpenZ.ZodBriefing.EndingBar;   OpenZ.OpenZVictory.DebugFortFalls(true) /
// (false) stage a win / a loss.
// Uses four small members of ZodComms: LetterboxCue, LetterboxSpeaker, LetterboxCueText, LetterboxSpeakerText.
//
// WRITTEN BY READING ONLY: never run in the engine. Every call into the game was looked up in the code; compiled off-line with the
using System;
using System.Collections;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>The picture of the level 16 ending scene. Set space: origin on the ground in the middle between the two loungers'
    /// folds, +z from the foot ends to the head ends, +x to the big robot's lounger (screen right), +y up.</summary>
    public interface IOzEndingSet
    {
        /// <summary>Models, clips and the place are there.</summary>
        bool Ready { get; }
        /// <summary>Stand the set at its place, turned with ZodBriefing.EndingSetRotation(place, fort) (the fort then lies
        /// EndingFortLeftDeg left of +z, over the small robot's shoulder), both robots lying, a can in the hand nearest the other
        /// one. Returns the set's root (null = could not).</summary>
        Transform Begin(Vector3 fort, bool won);
        /// <summary>A beat of the cue sheet: who = alan / brad / both / set / light / camera, beat = its name.</summary>
        void Beat(string who, string beat);
        /// <summary>Every frame: how far that robot's jaw is open, 0..1.</summary>
        void Jaw(string who, float open01);
        /// <summary>World place of "alan", "brad" or "between" (the cans' meeting point) for the scene's sounds.</summary>
        Vector3 Point(string at);
    }

    [Serializable] public class OzEndLine { public string who, text; public float start, end; }
    [Serializable] public class OzEndBeat { public float t; public string who, beat, note; }
    [Serializable] public class OzEndSound { public float t; public string ev, at, note; public float vol = 1f; }
    [Serializable] public class OzEndScene { public string id, voice; public float voice_at, length, end_screen_at; public OzEndLine[] lines; public OzEndBeat[] beats; public OzEndSound[] sounds; }
    [Serializable] public class OzEndCueFile { public int version; public string level; public OzEndScene win, lose; }

    public sealed partial class ZodBriefing
    {
        public const string EndingLevel = "L16", EndingCuesPath = "OpenZ/Zod/ending_L16_cues";
        public static bool EndingSceneEnabled = true;
        /// <summary>The commander over the end screen AFTER the scene, one switch per outcome (the game's usual endDelay, 1.3 s, after
        /// the end screen shows). Win: on, he follows the toast with EndingWinCommanderLine. Loss: off, he stays silent (his defeat
        /// line would be followed by zod_lose_push, which opens with the small robot's "What do we do now, sir?": the scene has just
        /// ended on that question). true on the loss = his usual defeat line and the push, as on the other levels.</summary>
        public static bool EndingSceneCommanderAfterWin = true, EndingSceneCommanderAfterLoss = false;
        /// <summary>The line he says after a level 16 WIN scene: the one that follows the toast in the original ("Now, boys, this is no
        /// time to celebrate! ..."). An id of zod_lines.json; empty, or a line / clip that is missing = his usual victory pick.</summary>
        public static string EndingWinCommanderLine = "zod_win_02";
        /// <summary>Bound by the sun lounger set of the level (null = level 16 ends like the other levels).</summary>
        public static IOzEndingSet EndingSet;
        /// <summary>
        /// side, 90 degrees off the way their feet point, so nobody looks back over a shoulder (was 24: behind them). The level 16 set
        /// writes this and the camera's numbers per ending from its data file (endWin / endLose) when an ending begins.</summary>
        public static float EndingFortLeftDeg = 90f;
        /// <summary>Camera in set space (m), turned left / up (deg), lens, push-in over the scene (m).</summary>
        public static Vector3 EndingCamPos = new Vector3(5.6f, 1.4f, -3.4f);
        public static float EndingCamLeftDeg = 72.5f, EndingCamUpDeg = 6.5f, EndingCamFov = 54f, EndingPushWin = 0.2f, EndingPushLose = 0.5f;
        /// <summary>The scene camera's rumble (the set's blast beats call EndingShake): degrees and metres at full strength.</summary>
        public static float EndingShakeDeg = 0.32f, EndingShakeShift = 0.012f;
        /// <summary>Checks: rumbles begun in this Play session.</summary>
        public static int EndingShakes;
        static float endShakeAt = -100f, endShakeFor = 0.5f, endShakeAmp;

        /// <summary>A short low rumble of the scene's frame. Nothing when SCREEN SHAKE is off or the calm screen is on.</summary>
        public static void EndingShake(float amp, float seconds)
        {
            if (!OzShakeGate.SettingOn) return;
            endShakeAt = Time.unscaledTime; endShakeFor = Mathf.Max(0.1f, seconds); endShakeAmp = amp; EndingShakes++;
        }

        static void EndingShakeApply(Transform cam)
        {
            float t = Time.unscaledTime, age = t - endShakeAt;
            if (!cam || age < 0f || age >= endShakeFor) return;
            float k = 1f - age / endShakeFor; k = k * k * endShakeAmp;
            cam.rotation = cam.rotation * Quaternion.Euler((Mathf.PerlinNoise(t * 11f, 0.3f) - 0.5f) * 2f * EndingShakeDeg * k,
                (Mathf.PerlinNoise(0.7f, t * 10f) - 0.5f) * 2f * EndingShakeDeg * k, (Mathf.PerlinNoise(t * 8f, 5.1f) - 0.5f) * EndingShakeDeg * k);
            cam.position += (cam.right * (Mathf.PerlinNoise(t * 9f, 2.9f) - 0.5f) + cam.up * (Mathf.PerlinNoise(8.8f, t * 10f) - 0.5f)) * (2f * EndingShakeShift * k);
        }
        /// <summary>The mushroom lives this long (s) while the scene's level is open (the game's 14 s would thin it out from 9 s).</summary>
        public static float EndingMushroomLife = 30f;
        /// <summary>
        /// loungers, back to 1 when the level is left; the strike seen at the fort itself, before the cut, is the game's own.</summary>
        public static float EndingMushroomScale = 1f;
        /// <summary>Combat / world mix under the two voices (the commander's own lines use -6 / -2).</summary>
        public static float EndingDuckCombatDb = -9f, EndingDuckWorldDb = -4f;
        /// <summary>Unit pictures nearer than this to the set are not drawn during the scene (m).</summary>
        public static float EndingClearRadius = 9f;
        /// <summary>The scene's shot shows the FAR blast only: bodies and parts the fort's blast threw (robot ragdolls, wreck pieces,
        /// building chunks: free rigid bodies) are not drawn in it when they are nearer the scene's camera than EndingFlyShare x the
        /// fort's distance, or fly toward it faster than EndingFlyClosing m/s from inside the fort's distance + 15 m. Checked at the cut
        /// (before its first frame) and on every frame of the scene; the throw itself in the game view is untouched; drawn again when
        /// the level is left.</summary>
        public static float EndingFlyShare = 0.8f, EndingFlyClosing = 2f;
        /// <summary>Checks: thrown robot bodies and other thrown parts not drawn in this ending's shot.</summary>
        public static int EndingFlyBodies, EndingFlyParts;
        static readonly List<Renderer> endingFlyOff = new List<Renderer>();
        static readonly HashSet<int> endingFlyDone = new HashSet<int>();
        public static string EndingStatus = "no ending scene yet";

        static void EndingFlyClear(Vector3 cam, Vector3 fort, Transform set)
        {
            float fortD = Vector2.Distance(new Vector2(cam.x, cam.z), new Vector2(fort.x, fort.z));
            float near = EndingFlyShare * fortD, reach = fortD + 15f;
            foreach (var rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
            {
                if (!rb || rb.isKinematic) continue;
                var t = rb.transform; if (set && t.IsChildOf(set)) continue;
                Vector3 p = rb.position, to = cam - p; to.y = 0f; float d = to.magnitude;
                if (d > reach) continue;
                Vector3 v = rb.linearVelocity; v.y = 0f;
                float closing = d > 0.01f ? Vector3.Dot(v, to / d) : 0f;
                if (d > near && closing < EndingFlyClosing) continue;
                var uv = rb.GetComponentInParent<UnitView>();
                if (uv && !uv.Dying) continue;                                                          // a living unit is never hidden here
                Transform owner = uv ? uv.transform : t;
                if (!endingFlyDone.Add(owner.GetInstanceID())) continue;
                if (uv) EndingFlyBodies++; else EndingFlyParts++;
                foreach (var r in owner.GetComponentsInChildren<Renderer>(true)) if (r.enabled) { r.enabled = false; endingFlyOff.Add(r); }
            }
            foreach (var r in endingFlyOff) if (r && r.enabled) r.enabled = false;                    // drawn again by something else: off for the scene
        }

        static void EndingFlyRestore()
        {
            foreach (var r in endingFlyOff) if (r) r.enabled = true;
            endingFlyOff.Clear(); endingFlyDone.Clear();
        }

        /// <summary>Play check: the bars' speaker label and subtitle as they stand now ("ALAN: Look!"; empty outside the scene).</summary>
        public static string EndingBar => I && I.comms != null ? I.comms.LetterboxSpeakerText + ": " + I.comms.LetterboxCueText : "";

        bool endingScenePlayed; AudioSource endingSfx; readonly float[] endingJawBuf = new float[256];
        static OzEndCueFile endingCues; static bool endingCuesTried, endingNoSetLogged; static float endingLifeBefore = -1f;
        static bool endingRestoreHooked, endingSkyForced, endingSkyBefore;

        static OzEndScene EndingCue(bool won)
        {
            if (!endingCuesTried)
            {
                endingCuesTried = true;
                try { var ta = Resources.Load<TextAsset>(EndingCuesPath); if (ta) endingCues = JsonUtility.FromJson<OzEndCueFile>(ta.text); }
                catch (Exception e) { Debug.LogWarning("[ZodCameo] ending cue sheet not readable: " + e.Message); }
            }
            var sc = endingCues == null ? null : won ? endingCues.win : endingCues.lose;
            return sc != null && sc.lines != null && sc.lines.Length > 0 && sc.length > 1f ? sc : null;
        }

        /// <summary>The set's rotation for a place and the fallen fort: +z points EndingFortLeftDeg right of the fort.</summary>
        public static Quaternion EndingSetRotation(Vector3 place, Vector3 fort)
        {
            Vector3 to = fort - place; to.y = 0f;
            if (to.sqrMagnitude < 1f) to = Vector3.forward;
            return Quaternion.LookRotation(to.normalized, Vector3.up) * Quaternion.Euler(0f, EndingFortLeftDeg, 0f);
        }

        /// <summary>OpenZVictory.Run: this level, this match and the data allow the scene.</summary>
        public static bool EndingSceneWanted(bool won)
        {
            var z = I;
            if (!EndingSceneEnabled || !z || z.mode != Mode.Level || !z.game || z.game.level != EndingLevel) return false;
            if (z.Bots || GameBoot.TestMode || z.Automated) return false;
            if (!OpenZEnding.Running)
            {   // the sim also ends a match when one side has no robot, vehicle or gun left (ZWorld.CheckEndGame): no fort falls, no strike,
                // no mushroom, so there is nothing to point at. OpenZEnding runs only from a TeamEliminated event = a fort went down.
                Debug.Log("[ZodCameo] ending scene: no fort fell (one side lost all its units): the level ends like the others");
                return false;
            }
            var sc = EndingCue(won);
            if (sc == null || z.lib.Line(sc.voice) == null || !z.lib.Clip(sc.voice)) { Debug.LogWarning("[ZodCameo] ending scene: cue sheet, line or clip missing: the level ends like the others"); return false; }
            if (EndingSet == null || !EndingSet.Ready)
            {
                if (!endingNoSetLogged) { endingNoSetLogged = true; Debug.Log("[ZodCameo] ending scene: no sun lounger set bound (ZodBriefing.EndingSet): the level ends like the others"); }
                return false;
            }
            return true;
        }

        /// <summary>LevelRules (ZodBriefing.cs): after this level's scene the commander says nothing over the end screen for this outcome.
        /// False when no scene was played (another level, no set bound, no fort down): his line comes as on every level.</summary>
        bool EndingCommanderSilent(bool won) => endingScenePlayed && !(won ? EndingSceneCommanderAfterWin : EndingSceneCommanderAfterLoss);

        /// <summary>LevelRules (ZodBriefing.cs): the line the commander says over the end screen after the scene. Null = his usual pick
        /// (no scene was played, a loss, no line named, the named line or its clip missing, or a line that names a team colour while
        /// the team colours are swapped: the rule of his other picks).</summary>
        string EndingCommanderLine(bool won)
        {
            if (!endingScenePlayed || !won || string.IsNullOrEmpty(EndingWinCommanderLine)) return null;
            var l = lib.Line(EndingWinCommanderLine);
            if (l == null || !lib.Clip(EndingWinCommanderLine))
            {
                Debug.LogWarning("[ZodCameo] ending scene: the commander's line " + EndingWinCommanderLine + " is missing (lines file or clip): his usual victory line instead");
                return null;
            }
            if (TeamColors.Swap && TeamColors.NamesColour(l.text)) return null;
            if (EndingWinCommanderLine == AlanOutroId) alanOutroPlayed = true;                          // this session has had the celebration line
            PlayerPrefs.SetString("oz_zod_last_victory", EndingWinCommanderLine);                       // the next victory's random pick does not repeat it at once
            Debug.Log("[ZodCameo] after the ending scene: the commander follows the toast with " + EndingWinCommanderLine);   // not "ending scene L16 win": that text is the scene's own one status line
            return EndingWinCommanderLine;
        }

        /// <summary>OpenZVictory.Run: plays the scene; ends when it is over or when skipped() turns true (the end screen is up).</summary>
        public static IEnumerator EndingScene(bool won, Func<bool> skipped) { return I.EndingRoutine(won, skipped); }

        /// <summary>The level is left: the two statics the scene changed get their values back (the mushroom's life, the forced sky).</summary>
        static void EndingRestore(Scene s)
        {
            if (endingLifeBefore >= 0f) { OpenZNukeMushroom.Life = endingLifeBefore; endingLifeBefore = -1f; }
            OpenZNukeMushroom.SceneScale = 1f;
            EndingFlyRestore();                                                                         // the thrown bodies the scene's shot left out
            EndingEnemiesRestore();                                                                     // version 1.1: the other side's units drawn again
            EndingHoldEnd();                                                                            // round 4: the world shake and the haze as before
            if (endingSkyForced) { FlybySky.ForceOn = endingSkyBefore; endingSkyForced = false; }
            SceneManager.sceneUnloaded -= EndingRestore; endingRestoreHooked = false;
        }

        /// <summary>One cooldown slot per place of the set. The sound table keeps its cooldown per (event, emitter) and emitter -1 is ONE
        /// shared slot (AudioDirector.Play): the two robots drinking 0.25 s apart would lose the second glug. Negative = never an object id.</summary>
        static int EndingEmitter(string at) => at == "alan" ? -1601 : at == "brad" ? -1602 : at == "between" ? -1603 : -1600;

        IEnumerator EndingRoutine(bool won, Func<bool> skipped)
        {
            // Enter / Space is live from the bomb's impact, the cut comes 1.25 game s later: a skip in between has put the end screen up
            // already (OpenZVictory.Release) while OpenZEnding.Running still says true. No set, no bars, no camera, and endingScenePlayed
            // stays false, so the commander's end-screen line comes (and is not cut off) exactly as on every other level.
            if (skipped != null && skipped()) { Debug.Log("[ZodCameo] ending scene: skipped before the cut (the end screen is up): the level ends like the others"); yield break; }
            var sc = EndingCue(won); var line = lib.Line(sc.voice); var clip = lib.Clip(sc.voice);
            // the fort whose mushroom stands behind them: the one that just fell
            Team loser = won ? (game.player == Team.Red ? Team.Blue : Team.Red) : game.player;
            Vector3 fort = OpenZNukeStrike.LastImpactPoint; bool haveFort = Time.time - OpenZNukeStrike.LastImpactTime < 20f;
            if (!haveFort)
                foreach (var o in game.World.Objects)
                    if (o != null && o.IsFort && o.Owner == loser) { fort = game.CenterWorld(o); haveFort = true; break; }
            Transform root = null;
            try { root = EndingSet.Begin(fort, won); } catch (Exception e) { Debug.LogException(e); }
            if (!root) { Debug.LogWarning("[ZodCameo] ending scene: the set did not start: straight to the end screen"); yield break; }
            endingScenePlayed = true;
            EndingHoldBegin(fort);                                                                      // round 4: the world shake off this camera, the haze stops at the cloud (ZodBriefing.EndingHold.cs)
            if (perf != null && !perf.done) perf.Abort(0.15f);                                          // a transmission still closing makes room
            // the mushroom outlives the scene; the game's own value comes back when this level is left
            if (!endingRestoreHooked) { endingRestoreHooked = true; SceneManager.sceneUnloaded += EndingRestore; }
            if (endingLifeBefore < 0f) endingLifeBefore = OpenZNukeMushroom.Life;
            OpenZNukeMushroom.Life = Mathf.Max(OpenZNukeMushroom.Life, EndingMushroomLife);
            OpenZNukeMushroom.SceneScale = EndingMushroomScale;                                           // round 2: the hard cut hides the change of size
            if (NukeFx.Current) NukeFx.Current.transform.localScale = Vector3.one * EndingMushroomScale;  // its cap, stem, fireball and shell are its children, its foot is the fort
            // the horizon sky: this camera stands 1.25 m over the ground and looks 5 degrees UP, so everything over the tree line is sky. In
            // play the painted sky is drawn only for a fly-over, a capture take or ForceOn (FlybySky.LateUpdate), and only for a level
            // with a sky entry (FlybySky.Entry). Its own fade is 1 s and the panorama loads in the background: a hard cut needs it loaded
            // BEFORE the cut and at full weight on the first frame; these lines only switch it on and, when the level is left, back.
            if (!endingSkyForced) { endingSkyForced = true; endingSkyBefore = FlybySky.ForceOn; }
            FlybySky.Ensure(); FlybySky.ForceOn = true;
            float ageAtCut = OpenZNukeMushroom.Current ? OpenZNukeMushroom.Current.Age : -1f;
            // picture: HUD off, bars in, units at the set not drawn, the camera is the scene's
            if (hud) hud.SetVisible(false);
            // the bars draw the subtitle themselves: after an in-game transmission (the red button before every win, an idle barb) the last
            // window layout has its own subtitle place and the bars' text would never be drawn (ZodComms.TickLetterbox)
            EnsureComms(1f); comms.Letterbox(true, "", ""); comms.LetterboxCue = true;
            int hidden = 0;
            foreach (var kv in game.Views)
            {
                var v = kv.Value; if (!v) continue;
                Vector3 d = v.transform.position - root.position; d.y = 0f;
                if (d.sqrMagnitude > EndingClearRadius * EndingClearRadius) continue;
                foreach (var r in v.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                hidden++;
            }
            var camT = rts ? rts.transform : Camera.main ? Camera.main.transform : null;
            var camC = camT ? camT.GetComponent<Camera>() : null;
            if (rts) rts.enabled = false;
            EndingFlyRestore(); EndingFlyBodies = EndingFlyParts = 0;
            EndingEnemiesBegin();                                                                       // version 1.1: the other side's units out of the scene's picture (ZodBriefing.EndingEnemies.cs)
            OzFortInferno.DuckAll(-24f, 2.5f, "ending scene");
            voice.Stop(); voice.clip = clip; voice.volume = VoiceGain();
            float dist = Vector3.Distance(new Vector3(root.position.x, 0f, root.position.z), new Vector3(fort.x, 0f, fort.z));
            EndingStatus = $"ending scene {EndingLevel} {(won ? "win" : "lose")}: cut {Time.time - OpenZNukeStrike.LastImpactTime:F2} s after the impact, set at {root.position}, fort {dist:F0} m away, "
                         + $"mushroom age {ageAtCut:F2} s, life {OpenZNukeMushroom.Life:F0} s, {hidden} unit pictures hidden, clip {clip.name} {clip.length:F2} s, scene {sc.length:F2} s";
            Debug.Log("[ZodCameo] " + EndingStatus);

            float t = 0f, jawA = 0f, jawB = 0f, duckAt = 0f; int bi = 0, si = 0, li = 0, lb = 0, frames = 0; bool voiceOn = false, wasSkipped = false;
            while (t < sc.length)
            {
                if (skipped != null && skipped()) { wasSkipped = true; break; }
                if (frames++ == 1)
                {   // what the cut's own picture was drawn with (FlybySky and CombatAudio ran their LateUpdate after the cut)
                    var ca = CombatAudio.I; var rig = ca ? ca.ListenerRig : null;
                    Debug.Log($"[ZodCameo] ending first frame: sky weight {(FlybySky.I ? FlybySky.I.Weight : 0f):F2} (level sky entry {(FlybySky.Entry(EndingLevel) != null ? "yes" : "NO")}), "
                            + $"listener {(rig ? Vector3.Distance(rig.position, root.position) : -1f):F1} m from the set");
                }
                bool paused = Paused;
                float dt = paused ? 0f : Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                // the clock: the voice's own sample position while it plays (never drifts), real time otherwise
                if (voiceOn && voice.isPlaying) t = sc.voice_at + voice.timeSamples / (float)Mathf.Max(1, clip.frequency);
                else t += dt;
                if (!voiceOn && t >= sc.voice_at && !paused) { voice.volume = VoiceGain(); voice.Play(); voiceOn = true; }
                while (bi < sc.beats.Length && sc.beats[bi].t <= t)
                {
                    var b = sc.beats[bi++];
                    try { EndingSet.Beat(b.who, b.beat); } catch (Exception e) { Debug.LogException(e); }
                    Debug.Log($"[ZodCameo] ending beat {t:F2} s: {b.who} {b.beat}");
                }
                while (sc.sounds != null && si < sc.sounds.Length && sc.sounds[si].t <= t) EndingSound(sc.sounds[si++]);
                while (li < sc.lines.Length && sc.lines[li].start <= t) { Debug.Log($"[ZodCameo] ending line {t:F2} s: {sc.lines[li].who} \"{sc.lines[li].text}\""); li++; }
                // 0.3 s into each line: what the lower bar really shows (the label and the text the bars drew last frame)
                while (lb < sc.lines.Length && sc.lines[lb].start + 0.3f <= t) { Debug.Log($"[ZodCameo] ending bar {t:F2} s: {comms.LetterboxSpeakerText} \"{comms.LetterboxCueText}\""); lb++; }
                // jaws: the clip's curve inside each robot's own lines, the live level without a curve
                float ct = voiceOn && voice.isPlaying ? voice.timeSamples / (float)Mathf.Max(1, clip.frequency) : -1f;
                float a = 0f, bj = 0f;
                if (ct >= 0f)
                    foreach (var l in sc.lines)
                    {
                        if (ct < l.start - sc.voice_at - 0.12f || ct > l.end - sc.voice_at + 0.2f) continue;
                        float j = ZodStage.CurveJaw(clip, ct); if (j < 0f) j = EndingLiveJaw();
                        if (l.who == "alan") a = j; else bj = j;
                    }
                if (dt > 0f)
                {
                    jawA += (a - jawA) * (1f - Mathf.Exp(-dt / (a > jawA ? 0.025f : 0.045f)));
                    jawB += (bj - jawB) * (1f - Mathf.Exp(-dt / (bj > jawB ? 0.025f : 0.045f)));
                }
                EndingSet.Jaw("alan", Mathf.Clamp01(jawA)); EndingSet.Jaw("brad", Mathf.Clamp01(jawB));
                string cue = ct >= 0f ? ZodLibrary.CueAt(line, ct) : null;
                if (cue != null)
                {   // the cues carry "Alan: " / "Brad: " (the lines file's way to mark a robot); here the bars' label names the speaker instead
                    bool al = cue.StartsWith("Alan: "), br = cue.StartsWith("Brad: ");
                    if (al || br) { comms.LetterboxSpeaker(al ? "ALAN" : "BRAD"); cue = cue.Substring(6); }
                }
                comms.SetCue(cue);
                comms.SetVu(Mathf.Max(jawA, jawB));
                voice.volume = VoiceGain();
                if (!paused && Time.unscaledTime >= duckAt)
                {   // the whole scene: music, ambience and the unit voices down, the blast's rumble and the battle under the two voices
                    duckAt = Time.unscaledTime + 0.3f;
                    if (AudioDirector.I) AudioDirector.I.AddDuck(duckMusicDb, 0f, duckAmbienceDb, duckAlanBusDb, 0.55f, 0.2f, 1.0f);   // this is no commander line: the music director's own duck for him does not run, so the plain one
                    if (CombatAudio.I) CombatAudio.I.DuckFor(0.55f, EndingDuckCombatDb, EndingDuckWorldDb);
                }
                if (camT)
                {   // one slow push-in, eased; the mushroom stays over the small robot's shoulder
                    float u = Mathf.Clamp01(t / sc.length), e = u * u * (3f - 2f * u);
                    var yaw = Quaternion.Euler(0f, -EndingCamLeftDeg, 0f);
                    Vector3 local = EndingCamPos + yaw * Vector3.forward * ((won ? EndingPushWin : EndingPushLose) * e);
                    camT.SetPositionAndRotation(root.TransformPoint(local), root.rotation * Quaternion.Euler(-EndingCamUpDeg, -EndingCamLeftDeg, 0f));
                    EndingShakeApply(camT);
                    EndingHoldPose(camT);                                                                   // round 4: the pose this frame is drawn from (the check)
                    EndingFlyClear(camT.position, fort, root);                                              // the far blast only: no thrown body past the lens
                    if (camC) camC.fieldOfView = EndingCamFov;
                }
                EndingEnemiesTick(camC, root);                                                              // version 1.1: the test of this frame, after the camera is placed
                if (rts)
                {   // the ears go with the picture. The listener is not on the camera: its rig stands over rts.focus at a height taken from
                    // rts.height (CombatAudio.LateUpdate), and both still say "the fallen fort", 110 to 156 m away, where the loungers' 3D sounds
                    // (20 m range) are not heard. The RTS camera is off, so nobody else writes them; exactly what ZodFlyover.Apply does.
                    rts.focus = new Vector3(root.position.x, 0f, root.position.z);
                    rts.height = rts.minHeight;
                }
                yield return null;
            }
            // the beats that fall on the scene's last instant (the hand-over to the end screen); none after a skip
            while (!wasSkipped && bi < sc.beats.Length)
            {
                var b = sc.beats[bi++];
                try { EndingSet.Beat(b.who, b.beat); } catch (Exception e) { Debug.LogException(e); }
                Debug.Log($"[ZodCameo] ending beat {t:F2} s: {b.who} {b.beat}");
            }
            // over (or skipped): bars out, the subtitle off; the camera and the set stay under the end screen until the level is left
            comms.SetCue(null); comms.SetVu(0f); comms.Letterbox(false);
            EndingSet.Jaw("alan", 0f); EndingSet.Jaw("brad", 0f);
            if (wasSkipped && voice.isPlaying)
            {
                float v0 = voice.volume, f = 0f;
                while (f < 0.15f) { f += Time.unscaledDeltaTime; voice.volume = v0 * Mathf.Clamp01(1f - f / 0.15f); yield return null; }
                voice.Stop();
            }
            Debug.Log($"[ZodCameo] ending scene over at {t:F2} s ({(wasSkipped ? "skipped" : "complete")}), mushroom age {(OpenZNukeMushroom.Current ? OpenZNukeMushroom.Current.Age : -1f):F2} s, "
                    + $"thrown bodies left out of the shot {EndingFlyBodies}, other thrown parts {EndingFlyParts}");
        }

        /// <summary>One sound of the cue sheet: a sound table event at its place, or the two robots' own servo.</summary>
        void EndingSound(OzEndSound s)
        {
            float vol = s.vol > 0f ? s.vol : 1f;
            if (s.ev == "zod_boys_servo")
            {   // the servo set of the two robots in the commander's HQ (ZodStage.Boys.cs: sd_robot_servo_a/b/c, -22 dB under the voice)
                if (!endingSfx) { endingSfx = gameObject.AddComponent<AudioSource>(); endingSfx.playOnAwake = false; endingSfx.spatialBlend = 0f; endingSfx.priority = 40; }
                string res = "OpenZ/Audio/Sfx/sd_robot_servo_" + "abc"[UnityEngine.Random.Range(0, 3)];
                var c = Resources.Load<AudioClip>(res);
                float g = Mathf.Clamp01(VoiceGain() * Mathf.Pow(10f, -22f / 20f) * vol);
                if (c) { endingSfx.pitch = UnityEngine.Random.Range(0.95f, 1.05f); endingSfx.PlayOneShot(c, g); OzSoundLog.Add("ZodCameo", "ending_" + s.at + "_servo", c.name, "played", g); }
                else OzSoundLog.Add("ZodCameo", "ending_" + s.at + "_servo", res, "missing clip");
                return;
            }
            if (!AudioDirector.Has(s.ev)) { OzSoundLog.Add("ZodCameo", "ending_" + s.ev, s.ev, "missing event"); Debug.LogWarning("[ZodCameo] ending scene: sound event " + s.ev + " missing"); return; }
            if (s.at == "2d") AudioDirector.UI(s.ev, vol);
            else AudioDirector.Fire(s.ev, EndingSet.Point(s.at), EndingEmitter(s.at), vol);
        }

        float EndingLiveJaw()
        {
            voice.GetOutputData(endingJawBuf, 0);
            double sum = 0; for (int i = 0; i < endingJawBuf.Length; i++) sum += endingJawBuf[i] * endingJawBuf[i];
            float rms = Mathf.Sqrt((float)(sum / endingJawBuf.Length)) / Mathf.Max(0.05f, voice.volume);
            float k = Mathf.Clamp01((20f * Mathf.Log10(rms + 1e-6f) + 40f) / 24f);
            return k * k;
        }
    }
}
