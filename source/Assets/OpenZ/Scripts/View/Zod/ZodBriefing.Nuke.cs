// OpenZ (GPL-3.0)
// The nuke transmission.
//  - OpenZVoiceV3 (after Rachel's explanation) calls PlayNuke(onSlam): one random line of the group "nuke" (never the same as last
//    time), in the in-game corner window, the same live field transmission as the v2 lines (static open, hand-held camera, bursts).
//  - The beat fx "slam" (zod_beats.json, SlamHit s before the last word) cuts the feed to the insert of his fist on the big red
//    button (ZodStage.Slam); SlamHit s later the button bottoms out: the clunk (Resources/OpenZ/Zod/Sfx/zod_button_slam) plays and
//    onSlam launches the strike (OpenZNukeStrike.LaunchNow), so the bomber appears because Zod pressed the button.
//  - Never in bot matches, test mode, capture takes or automated checks, never over another Zod line (CanPlayNuke).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    public sealed partial class ZodBriefing
    {
        [Header("voice_lines_v3: nuke transmission")]
        [Tooltip("The red button's clunk, dB relative to Zod's voice gain (the clip is -19 LUFS, true peak -1 dBTP)")] public float slamDb = 0f;

        Action nukeSlam; float slamSoundAt = -1f, slamReleaseAt = -1f;
        [Tooltip("The cap springing back up after the press, dB under the clunk")] public float slamReleaseDb = -10f;

        /// <summary>OpenZVoiceV3.PlanNuke: Zod can open a nuke transmission now (a level in play, the cameo on, no bots / test mode /
        /// capture take / automated check, no other Zod line up, and at least one nuke line with its clip).</summary>
        public static bool CanPlayNuke => I && I.mode == Mode.Level && I.phase == Phase.Play && Enabled && !I.Bots && !GameBoot.TestMode
                                          && !I.Automated && I.perf == null && (I.comms == null || I.comms.Closed) && I.lib.HasGroup("nuke");

        /// <summary>Seconds from PlayNuke to the button hit at most (window preroll + the latest slam beat + the wind-up).</summary>
        public static float NukeLeadSeconds => I ? I.linePreroll + I.MaxSlamTime() : 3.5f;

        /// <summary>Opens the nuke transmission; onSlam runs when the fist hits the button (or never, if the line has no slam beat:
        /// the caller launches when Zod stops speaking). avoidWord (REVIEW): skip lines containing it when another line is left
        /// (Rachel just said "Launching the nuke": Zod never says it again). False = Zod cannot talk now.</summary>
        public static bool PlayNuke(Action onSlam, string avoidWord = null)
        {
            if (!CanPlayNuke) return false;
            string id = I.PickNuke(avoidWord);
            if (id == null) return false;
            I.EnsureComms(1f);
            I.nukeSlam = onSlam; I.slamSoundAt = -1f;
            bool ok = I.StartPerf(Kind.Nuke, id, ZodLayout.InGame(), I.linePreroll);
            if (!ok) I.nukeSlam = null;
            return ok;
        }

        /// <summary>REVIEW: ZodLibrary.Pick("nuke") (never the last line, same PlayerPrefs key) without the lines that contain avoidWord.</summary>
        string PickNuke(string avoidWord)
        {
            if (string.IsNullOrEmpty(avoidWord)) return lib.Pick("nuke");
            var all = new List<string>();
            foreach (var id in lib.Group("nuke"))
            {
                var l = lib.Line(id);
                if (l != null && lib.Clip(id) && (l.text ?? "").IndexOf(avoidWord, StringComparison.OrdinalIgnoreCase) < 0 && !(TeamColors.Swap && TeamColors.NamesColour(l.text))) all.Add(id);
            }
            if (all.Count == 0) return lib.Pick("nuke");
            const string key = "oz_zod_last_nuke";
            if (all.Count > 1) all.Remove(PlayerPrefs.GetString(key, ""));
            var pick = all[UnityEngine.Random.Range(0, all.Count)];
            PlayerPrefs.SetString(key, pick);
            return pick;
        }

        float MaxSlamTime()
        {
            float m = 0f;
            foreach (var id in lib.Group("nuke"))
            {
                var line = lib.Line(id); var b = lib.Beats(id);
                if (line == null || b?.fx == null) continue;
                foreach (var f in b.fx)
                    if (f != null && f.fx == "slam") { float t = ZodLibrary.Resolve(line, f); if (!float.IsNaN(t)) m = Mathf.Max(m, t + ZodStage.SlamHit); }
                if (m <= 0f) m = Mathf.Max(m, line.duration);
            }
            return m > 0f ? m : 3f;
        }

        /// <summary>DoFx "slam": the cut to the insert (in a signal burst) and the fist; the clunk + launch come SlamHit s later.</summary>
        void DoSlam(ZodFx f)
        {
            comms.Glitch(0.55f, 0.12f);                                                                     // the cut hides in a burst
            stage.GazeCut();
            stage.FistSwing = SwingWhoosh;
            stage.Slam(f.value);
            // buffer behind) only backs it up 0.2 s later if the stage never got there
            stage.SlamContact = () => { if (perf != null && slamSoundAt >= 0f) SlamHitNow(perf); };
            slamSoundAt = perf != null ? perf.t + ZodStage.SlamHit + 0.2f : -1f;
            SlamPreload();
        }

        /// <summary></summary>
        void SlamPreload()
        {
            foreach (var res in new[] { "OpenZ/Zod/Sfx/zod_button_slam", "OpenZ/Audio/Sfx/sd_seat_thud_a", "OpenZ/Audio/Sfx/sd_door_heavy_close_b", "OpenZ/Audio/Sfx/heavy" })
            {
                string key = res.EndsWith("zod_button_slam") ? "slam" : res;
                if (!statics.TryGetValue(key, out var c) || !c) statics[key] = c = Resources.Load<AudioClip>(res);
                if (c && c.loadState == AudioDataLoadState.Unloaded) c.LoadAudioData();
            }
        }

        /// <summary>
        /// pitched up so its swell lands on the hit), 2D on Zod's voice level.</summary>
        AudioSource whooshSrc;
        void SwingWhoosh()
        {
            if (!whooshSrc) { whooshSrc = gameObject.AddComponent<AudioSource>(); whooshSrc.playOnAwake = false; whooshSrc.spatialBlend = 0f; whooshSrc.priority = 20; if (staticMid) whooshSrc.outputAudioMixerGroup = staticMid.outputAudioMixerGroup; }
            const string res = "OpenZ/Audio/Sfx/v3-ramp-whoosh";
            if (!statics.TryGetValue(res, out var clip)) statics[res] = clip = Resources.Load<AudioClip>(res);
            if (!clip) { OzSoundLog.Add("ZodCameo", "fist_swing", res, "missing clip"); return; }
            float g = Mathf.Clamp01(VoiceGain() * Mathf.Pow(10f, slamDb / 20f)) * 0.55f;
            whooshSrc.pitch = 1.55f; whooshSrc.PlayOneShot(clip, g);
            OzSoundLog.Add("ZodCameo", "fist_swing", clip.name, "played", g);
        }

        /// <summary>TickPerf: the button bottoms out: the clunk, and the strike launches.</summary>
        void TickSlam(Perf p)
        {
            if (slamReleaseAt >= 0f && p != null && p.t >= slamReleaseAt)
            {
                slamReleaseAt = -1f;
                AudioClip rc; if (!statics.TryGetValue("slam", out rc)) statics["slam"] = rc = Resources.Load<AudioClip>("OpenZ/Zod/Sfx/zod_button_slam");
                float rv = Mathf.Clamp01(VoiceGain() * Mathf.Pow(10f, (slamDb + slamReleaseDb) / 20f));
                if (rc && staticMid) { staticMid.PlayOneShot(rc, rv / Mathf.Max(0.01f, staticMid.volume)); OzSoundLog.Add("ZodCameo", "button_release", rc.name, "played", rv); }
                else OzSoundLog.Add("ZodCameo", "button_release", "zod_button_slam", "missing clip");
            }
            if (slamSoundAt < 0f || p == null || p.t < slamSoundAt) return;
            SlamHitNow(p);
        }

        /// <summary>The fist meets the cap: the clunk, the thumps, the strike launches (once per press).</summary>
        void SlamHitNow(Perf p)
        {
            if (slamSoundAt < 0f || p == null) return;
            slamSoundAt = -1f;
            if (stage) stage.SlamContact = null;
            slamReleaseAt = p.t + (ZodStage.SlamRelease - ZodStage.SlamHit) + 0.06f;
            if (!statics.TryGetValue("slam", out var c)) statics["slam"] = c = Resources.Load<AudioClip>("OpenZ/Zod/Sfx/zod_button_slam");
            if (c && staticMid) { staticMid.Stop(); staticMid.clip = c; staticMid.pitch = 1f; staticMid.volume = Mathf.Clamp01(VoiceGain() * Mathf.Pow(10f, slamDb / 20f)); staticMid.Play(); OzSoundLog.Add("ZodCameo", "button_slam", c.name, "played", staticMid.volume); }
            SlamThump();
            var cb = nukeSlam; nukeSlam = null;
            try { cb?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
            Debug.Log($"[ZodCameo] nuke: the fist hit the red button (clunk frame {Time.frameCount}, cap-contact frame {ZodStage.ContactFrame})");
        }

        /// <summary>
        /// heavy table-bang thud (sd_seat_thud_a), a metal clunk (sd_door_heavy_close_b) and a short low boom ("heavy", pitched 0.8),
        /// 2D on Zod's voice level, and a small stage kick on the impact (ZodStage.Kick: CALM SCREEN tones it down).</summary>
        AudioSource thumpSrc;
        void SlamThump()
        {
            if (!thumpSrc) { thumpSrc = gameObject.AddComponent<AudioSource>(); thumpSrc.playOnAwake = false; thumpSrc.spatialBlend = 0f; thumpSrc.priority = 20; if (staticMid) thumpSrc.outputAudioMixerGroup = staticMid.outputAudioMixerGroup; }
            float g = Mathf.Clamp01(VoiceGain() * Mathf.Pow(10f, slamDb / 20f));
            foreach (var (res, vol, pitch) in new[] { ("OpenZ/Audio/Sfx/sd_seat_thud_a", 1f, 0.85f), ("OpenZ/Audio/Sfx/sd_door_heavy_close_b", 0.7f, 1.05f), ("OpenZ/Audio/Sfx/heavy", 0.45f, 0.8f) })
            {
                if (!statics.TryGetValue(res, out var clip)) statics[res] = clip = Resources.Load<AudioClip>(res);
                if (!clip) { OzSoundLog.Add("ZodCameo", "button_thump", res, "missing clip"); continue; }
                thumpSrc.pitch = pitch; thumpSrc.PlayOneShot(clip, Mathf.Clamp01(g * vol));
                OzSoundLog.Add("ZodCameo", "button_thump", clip.name, "played", g * vol);
            }
            if (stage) stage.Kick(0.6f);
        }

        /// <summary>The line closed or was cut: no pending clunk / launch from it (OpenZVoiceV3 launches by itself).</summary>
        void EndSlam() { slamSoundAt = -1f; slamReleaseAt = -1f; nukeSlam = null; if (stage) stage.SlamContact = null; }

        /// <summary>Play check: the nuke transmission now (the slam only logs; no strike is launched).
        /// <c>return OpenZ.ZodBriefing.DebugNuke();</c></summary>
        public static string DebugNuke()
        {
            if (!I) return "no ZodBriefing in this scene";
            if (I.perf != null) return "busy: " + I.perf.id;
            if (!I.lib.HasGroup("nuke")) return "no nuke lines (voice_lines_v3 not imported)";
            string id = I.lib.Pick("nuke");
            I.EnsureComms(1f);
            I.nukeSlam = () => Debug.Log("[ZodCameo] DebugNuke: slam (no strike launched)");
            return I.StartPerf(Kind.Nuke, id, ZodLayout.InGame(), I.linePreroll) ? "playing " + id : "failed " + id;
        }
    }
}
