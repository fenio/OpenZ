// OpenZ (GPL-3.0) - cinematic briefing camera: the camera-only preview of a level's shot list (no Zod window, no voice). The sim
// waits while it flies, like in the real briefing; the timing is the real briefing's (GENERAL joke + window open + line + tail) and
// its cuts snap on the level's briefing line breaks, so the shots land where they will in the game. At the end the last shot eases into
// the RTS start camera and the player has the camera back. ZodFlyover.HoldShot / HoldU freeze one shot for a frame check.
// Play check: return OpenZ.ZodCinePreview.Play();   return OpenZ.ZodCinePreview.Hold(2);   return OpenZ.ZodCinePreview.Stop();
using UnityEngine;

namespace OpenZ
{
    public sealed class ZodCinePreview : MonoBehaviour
    {
        public static ZodCinePreview I { get; private set; }
        [System.Serializable] class Ink { public float duration_s; }
        [System.Serializable] class Lettering { public Ink general, strike; }

        ZodFlyover fly; ZGame game; bool simWas, handing; float total, handAt;

        /// <summary>Starts the preview on the open level. seconds &gt; 0: that length, nominal cuts; 0: the real briefing timing.</summary>
        public static string Play(float seconds = 0f)
        {
            if (ZodBriefing.BriefingActive) return "a briefing is running: its own shot list flies (" + ZodFlyover.Status() + ")";
            var game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            var rts = FindFirstObjectByType<RtsCamera>();
            if (!game || !rts || game.World == null) return "no level (ZGame + RtsCamera) in this scene";
            Stop();
            var go = new GameObject("ZodCinePreview");
            var p = I = go.AddComponent<ZodCinePreview>();
            p.game = game; p.fly = new ZodFlyover(rts, game);
            if (!p.fly.Begin("overview") || !p.fly.Cine)
            {
                p.fly.End(); Destroy(go); I = null;
                return "the shot list is off (ZodFlyover.UseShotList = false) or the level is not ready";
            }
            if (seconds > 0f) p.fly.PlanCine(0f, seconds, null, null);
            else
            {
                string id = "zod_brief_" + game.level;
                var lib = ZodLibrary.Shared; var line = lib.Line(id); var clip = lib.Clip(id);
                float voiceEnd = clip && line != null ? Mathf.Min(clip.length, line.duration > 0f ? line.duration : clip.length) : 13f;
                float tail = lib.Beats(id).tail;
                float preroll = (ZodBriefing.I ? ZodBriefing.I.briefingPreroll : 0.55f) + JokeLead(lib);
                p.fly.PlanCine(preroll, voiceEnd + tail, line, null);
            }
            p.simWas = game.enabled; game.enabled = false;                                          // the sim waits, as in the briefing
            foreach (var s in p.fly.CineShots) p.total = Mathf.Max(p.total, s.start + s.dur);
            return $"preview {game.level}: {p.total:F1} s; {ZodFlyover.LastPlan}";
        }

        /// <summary>The GENERAL joke's lead before Zod's first word (the same sum as ZodBriefing.JokeLead).</summary>
        static float JokeLead(ZodLibrary lib)
        {
            if (!ZodBriefing.JokeEnabled || ZodComms.General) return 0f;
            var ta = Resources.Load<TextAsset>("OpenZ/Zod/Joke/lettering"); var ahem = lib.Clip(ZodBriefing.AhemId);
            if (!ta || !ahem) return 0f;
            var L = JsonUtility.FromJson<Lettering>(ta.text);
            if (L?.strike == null || L.general == null) return 0f;
            return ZodBriefing.JokeGloveIn + ZodBriefing.JokeEnterS + L.strike.duration_s + ZodBriefing.JokeHopS + L.general.duration_s
                 + ZodBriefing.JokeExitS + ZodBriefing.JokeAhemGap + ahem.length + ZodBriefing.JokeAfterAhem;
        }

        /// <summary>Freezes shot `shot` (1-based, as in the plan) at `u` of its move; starts the preview if nothing flies.</summary>
        public static string Hold(int shot, float u = 0.5f)
        {
            ZodFlyover.HoldShot = Mathf.Max(0, shot - 1); ZodFlyover.HoldU = Mathf.Clamp01(u);
            string r = ZodFlyover.Live != null && ZodFlyover.Live.Active ? "holding" : Play();
            return $"hold shot {shot} at {u:F2}: {r}";
        }

        public static string Release() { ZodFlyover.HoldShot = -1; return "hold released"; }

        public static string Stop()
        {
            ZodFlyover.HoldShot = -1;
            if (!I) return "no preview";
            I.Finish(); return "preview stopped";
        }

        void LateUpdate()
        {
            if (fly == null || !fly.Active) { Finish(); return; }
            if (AudioListener.pause) return;
            fly.Tick(Mathf.Min(Time.unscaledDeltaTime, 0.1f));
            if (!handing && ZodFlyover.HoldShot < 0 && fly.CineTime >= total) { handing = true; handAt = Time.unscaledTime; fly.HandBack(false); }
            if (handing && (fly.HomeError <= 0.04f || Time.unscaledTime - handAt > 1.6f)) Finish();
        }

        void Finish()
        {
            if (fly != null && fly.Active) fly.End();
            fly = null;
            if (game) game.enabled = simWas || game.enabled;
            game = null;
            if (I == this) I = null;
            if (this) Destroy(gameObject);
        }

        void OnDestroy() { if (fly != null && fly.Active) fly.End(); if (game) game.enabled = simWas || game.enabled; if (I == this) I = null; }
    }
}
