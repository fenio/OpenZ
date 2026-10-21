using System.Collections;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// The end of the game as ONE sequence, before the end screen.
    /// <list type="number">
    /// <item>The sim's game over no longer pops the end screen at once: OpenZEndScreen waits while HoldEndScreen is true
    /// (fx_v2 patch), so the bomber, the nuke and the celebration are seen. Safety release after MaxHold game seconds; Enter / Space /
    /// click after the blast skips to the screen (Esc stays the pause menu, which freezes the sequence).</item>
    /// <item>AudioDirector's own game-over voices (Rachel +0.8 s, an Alan face every 1.2 s: they cut and overlapped each other)
    /// and the sting are skipped while OwnsEndSequence (fx_v2 patch); the music still fades out.</item>
    /// <item>After the nuke impact (OpenZNukeStrike.Impacted) + PostImpactDelay: the winner's robots cheer (OpenZCheer: hops,
    /// a twirl, rifles fired into the sky, desynced) and the lines play strictly one after another (each waits until no Alan /
    /// Rachel line is playing or queued, + Gap): victory = Rachel "Enemy eliminated." -> Alan "Yee-haa!" -> Alan "Nice one!"
    /// -> Alan "We won!" -> Rachel "Mission completed."; defeat = Rachel "Mission failed." -> two Alan lose lines. Missing new
    /// events fall back to the existing ones (ann_enemy_fort_destroyed, vo_win_lines "We've done it!", ann_victory).</item>
    /// <item>Then the end screen and its sting (ter_victory_sfx / ter_defeat_sfx).</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(-44)]
    public sealed class OpenZVictory : MonoBehaviour
    {
        public static OpenZVictory I { get; private set; }
        public static bool Enabled = true, AutoCreate = true;
        public static float PostImpactDelay = 1.25f, Gap = 0.22f, MaxHold = 24f, NoStrikeDelay = 1.2f, LineTimeout = 5f;
        /// <summary>endings_v1: pause after the nuke's death wave before the first line; the end screen comes at least this many real
        /// </summary>
        public static float WaveGap = 0.35f, MinTotalVictory = 15.5f, MinTotalDefeat = 15f, HoldCap = 6f;

        /// <summary>OpenZEndScreen (fx_v2 patch): true while the end sequence runs after the sim's game over.</summary>
        public static bool HoldEndScreen => Enabled && I && I.isActiveAndEnabled && I.game && I.game.World != null && I.game.World.GameOver && !I.released;
        /// <summary>AudioDirector (fx_v2 patch): the game-over voices and sting belong to this sequence.</summary>
        public static bool OwnsEndSequence => Enabled && I && I.isActiveAndEnabled;
        /// <summary>True only while the end sequence actually runs (from the sim's game over). OwnsEndSequence is true all game
        /// </summary>
        public static bool EndSequenceRunning => Enabled && I && I.isActiveAndEnabled && I.started;
        /// <summary>fx_v2 CHECK (AudioDirector.SayImpl / AnnounceImpl): while the end lines play (impact + PostImpactDelay until the end
        /// screen) no other Alan / Rachel line may start: selection acks on Alan's main channel, barks, production and territory
        /// announcements. Only the line this sequence is starting passes. Lines queued before (nuke launched...) still play first.</summary>
        public static bool MutesVoice(string id) => Enabled && I && id != I.allowId && (I.released ? I.started : (I.speaking || OpenZEnding.QuietVoices(id)));   // endings_v1: quiet from the fort's fall (nuke lines pass); endgame_ui_v1: and for good once the end screen is up (no routine / combat lines after the win)
        /// <summary>endgame_ui_v1: longest wait for the crowd's playing cheers before one of Rachel's end lines starts.</summary>
        public static float CrowdWait = 2.5f;
        /// <summary></summary>
        static readonly string[] LossFilters = { "blown it", "crap", "shambles", "commander", "stuffed", "disaster", "useless", "Rubbish", "It's over" };

        ZGame game; bool started, released, skippable; float overAt;
        float extraHold;                                                                     // voice_lines_v3: the nuke intro's wait
        bool speaking; string allowId;                                                     // fx_v2 CHECK: MutesVoice
        readonly List<OpenZCheer> cheers = new List<OpenZCheer>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded; }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!AutoCreate || I) return;
            var g = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            if (!g) return;
            g.gameObject.AddComponent<OpenZVictory>();
        }

        void Awake()
        {
            if (I && I != this) { Destroy(this); return; }
            I = this; game = GetComponent<ZGame>(); if (!game) game = ZGame.I;
        }

        void OnDestroy() { if (I == this) I = null; }

        void Update()
        {
            if (!game) game = ZGame.I;
            if (!Enabled || game == null || game.World == null) return;
            if (!started && game.World.GameOver) { started = true; overAt = Time.time; StartCoroutine(Run(game.World.Winner)); }
            if (started && !released)
            {
                extraHold = Mathf.Max(extraHold, OpenZNukeStrike.HoldSeconds);                              // voice_lines_v3
                if (Time.time - overAt > MaxHold + extraHold) Release("safety timeout");
                else if (skippable && SkipPressed()) Release("skipped");
            }
        }

        static bool SkipPressed()
        {
            // fx_v2 CHECK: keyboard only. The world stays clickable during the celebration (selecting a cheering robot is natural), and
            var kb = Keyboard.current;
            return kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame);
        }

        void Release(string why)
        {
            if (released) return;
            released = true;
            allowId = null; OpenZCelebrate.End();                                            // endgame_ui_v1: no line passes after the screen; the crowd fades out
            OpenZEnding.OnEndScreen();                                                       // endings_v1: camera back to the player, held deaths finish
            OzFortInferno.DuckAll(-14f, 1f, "end screen");
            bool won = game && game.World != null && game.World.Winner == game.player;
            bool draw = game && game.World != null && game.World.Winner == Team.Neutral;
            if (!draw) AudioDirector.UI(won ? "ter_victory_sfx" : "ter_defeat_sfx");
            if (!draw) AudioDirector.UI(won ? "ui_victory_hit" : "ui_defeat_hit");                         // sound_v3: the hit also with music off            // the sting with the screen
            Debug.Log($"[OpenZ] end sequence: end screen ({why}) {Time.time - overAt:F1} s after the game over");
        }

        IEnumerator Run(Team winner)
        {
            bool draw = winner == Team.Neutral, won = winner == game.player;
            // 1) the strike: wait for its impact (it may still be in the air: the fort fell up to ~1 s before the game over)
            float t0 = Time.time;                                                            // game time: the pause menu freezes the sequence
            while (Time.time - t0 < NoStrikeDelay && !OpenZNukeStrike.Active && OpenZNukeStrike.LastImpactTime < overAt - 8f) yield return null;
            while (OpenZNukeStrike.Active && OpenZNukeStrike.SecondsToImpact >= 0f && Time.time - t0 < 10f + OpenZNukeStrike.HoldSeconds) yield return null;           // voice_lines_v3: + the nuke intro's hold
            bool hadStrike = OpenZNukeStrike.LastImpactTime > overAt - 8f;
            float from = hadStrike ? OpenZNukeStrike.LastImpactTime + PostImpactDelay : Time.time + 0.4f;
            skippable = true;                                                                // endings_v1: Enter / Space skip from the impact on
            while (Time.time < from) yield return null;
            // level 16 endings: the two robots on the sun loungers, the fallen fort's mushroom far behind them (ZodBriefing.EndingScene.cs);
            // any other level, or no set bound: false, and everything below runs as before
            if (!draw && ZodBriefing.EndingSceneWanted(won))
            {
                speaking = true;                                                             // no other robot / announcer line starts under the scene
                yield return ZodBriefing.EndingScene(won, () => released);
                if (!released) Release("ending scene done");
                yield break;
            }
            // endings_v1: the lines wait until the nuke's death wave is over (OpenZEnding: the eliminated team dies outward from the bomb)
            float w0 = Time.time;
            while (OpenZEnding.WaveRunning && Time.time - w0 < 7f) yield return null;
            while (Time.time < OpenZEnding.WaveEndTime + WaveGap && Time.time - w0 < 7.5f) yield return null;
            speaking = true;                                                                 // fx_v2 CHECK: other voices muted from here
            if (draw) { yield return WaitVoices(); Release("draw"); yield break; }

            // 2) the lines, strictly one after another; the winner's robots start cheering with the first Alan line
            if (won)
            {
                yield return Line(true, "ann_enemy_eliminated", null, "ann_enemy_fort_destroyed", null);
                StartCheer(winner);
                // vo_victory_cheer lines, a whoop first, random, never the lines of the previous victory (OpenZVoiceV3.VictoryPicks)
                var cheer = OpenZVoiceV3.VictoryPicks(3, winner == Team.Red);
                if (cheer.Count == 3)
                {
                    yield return Line(false, "vo_victory_cheer", "=" + cheer[0], "vo_win_lines", "Yeee");
                    yield return Line(false, "vo_victory_cheer", "=" + cheer[1], "vo_win_lines", "Nice one");
                    yield return Line(false, "vo_victory_cheer", "=" + cheer[2], "vo_we_won", null);
                }
                else
                {
                    yield return Line(false, "vo_win_lines", "Yeee", null, null);
                    yield return Line(false, "vo_win_lines", "Nice one", null, null);
                    yield return Line(false, "vo_we_won", null, "vo_win_lines", "done it");
                }
                yield return Line(true, "ann_mission_completed", null, "ann_victory", null);
            }
            else
            {
                yield return Line(false, "vo_lose_lines", "lost it", null, null);
                StartCheer(winner);
                // A second, different loss line
                yield return Line(false, "vo_lose_lines", LossFilters[Random.Range(0, LossFilters.Length)], "vo_lose_lines", "blown it");
                yield return Line(true, "ann_defeat", "Mission failed", "ann_defeat", null);
            }
            yield return WaitVoices();
            yield return new WaitForSeconds(0.25f);
            // endings_v1: a last look at the ruin (the camera pushes in) until 15-20 s after the fort fell, pause excluded
            float held = 0f;
            while (!released && OpenZEnding.Running && OpenZEnding.RealSinceFall < (won ? MinTotalVictory : MinTotalDefeat) && held < HoldCap)
            { if (Time.timeScale > 0f) held += Time.unscaledDeltaTime; yield return null; }
            Release("sequence done");
        }

        /// <summary>One voice line after the previous one has finished (+ Gap). rachel = announcer queue, else Alan (end channel).</summary>
        IEnumerator Line(bool rachel, string id, string filter, string fallbackId, string fallbackFilter)
        {
            if (released) yield break;                                                        // skipped: the screen is up, no more lines
            yield return WaitVoices();
            string use = id, f = filter;
            if (!AudioDirector.Has(use)) { use = fallbackId; f = fallbackFilter; }
            if (use == null || !AudioDirector.Has(use)) yield break;
            if (rachel)
            {   // endgame_ui_v1: the crowd never talks over Rachel: no new cheer from here, the playing ones end first
                OpenZCelebrate.HoldForRachel = true;
                float c0 = Time.time;
                while (OpenZCelebrate.Busy && Time.time - c0 < CrowdWait) yield return null;
            }
            yield return new WaitForSeconds(Gap);
            if (released) { OpenZCelebrate.HoldForRachel = false; yield break; }
            allowId = use;                                                                   // fx_v2 CHECK: MutesVoice lets this one through
            if (rachel) AudioDirector.Announce(use, f); else AudioDirector.Say(use, f);          // endings_v1: Rachel lines take a filter too
            // let it start (the Rachel queue plays it on the next UpdateChannels)
            float s = Time.time;
            while (!AudioDirector.VoiceBusy && Time.time - s < 0.5f) yield return null;
            if (rachel) StartCoroutine(CrowdAfterRachel());                                  // endgame_ui_v1
        }

        /// <summary>endgame_ui_v1: the crowd may cheer again 0.2 s after Rachel's line has ended.</summary>
        IEnumerator CrowdAfterRachel()
        {
            float s = Time.time;
            while (AudioDirector.VoiceBusy && Time.time - s < 8f) yield return null;
            yield return new WaitForSeconds(0.2f);
            if (!released) OpenZCelebrate.HoldForRachel = false;
        }

        IEnumerator WaitVoices()
        {
            float s = Time.time;
            while (AudioDirector.VoiceBusy && Time.time - s < LineTimeout) yield return null;
        }

        /// <summary>Play-mode check (README): every fort part of the other team (or ours with enemy false) falls the way the gate
        /// battery's "fort_falls" stage kills it (GateBattery.Kill), so the real chain runs: RulesView -> strike -> nuke,
        /// TeamEliminated, GameOver -> this sequence -> end screen. an editor script: OpenZ.OpenZVictory.DebugFortFalls();</summary>
        public static int DebugFortFalls(bool enemy = true)
        {
            var g = ZGame.I; if (!g || g.World == null) return 0;
            var W = g.World; int n = 0;
            foreach (var o in new List<ZObject>(W.Objects))
            {
                if (o == null || !o.IsFort || o.Destroyed) continue;
                if (enemy ? (o.Owner == g.player || o.Owner == Team.Neutral) : o.Owner != g.player) continue;
                o.DamageHealth(o.Health + 1); W.UpdateObjectHealth(o, -1); n++;
            }
            Debug.Log($"[OpenZ] DebugFortFalls: {n} {(enemy ? "enemy" : "own")} fort part(s) destroyed");
            return n;
        }

        /// <summary>Winner robots (alive, on foot, standing) cheer, desynced; the ones on screen also fire into the sky (max 14).</summary>
        void StartCheer(Team team)
        {
            var cam = Camera.main; int shooters = 0;
            foreach (var kv in game.Views)
            {
                var v = kv.Value; if (!v || !v.isActiveAndEnabled) continue;
                var o = v.Obj; if (o == null || o.Kind != ZKind.Robot || o.Owner != team || o.Destroyed || v.Dying) continue;
                bool onScreen = false;
                if (cam) { var vp = cam.WorldToViewportPoint(v.transform.position); onScreen = vp.z > 0f && vp.x > -0.05f && vp.x < 1.05f && vp.y > -0.05f && vp.y < 1.05f; }
                var c = v.GetComponent<OpenZCheer>(); if (!c) c = v.gameObject.AddComponent<OpenZCheer>();
                c.Begin(v, onScreen && shooters < 14, o.Id);
                if (onScreen) shooters++;
                cheers.Add(c);
            }
            Debug.Log($"[OpenZ] end sequence: {cheers.Count} {team} robots cheer ({shooters} on screen fire into the sky)");
            var crowd = new List<UnitView>();
            foreach (var c in cheers) if (c) { var v = c.GetComponent<UnitView>(); if (v && !crowd.Contains(v)) crowd.Add(v); }
            OpenZCelebrate.Begin(game, team, crowd);
        }
    }
}
