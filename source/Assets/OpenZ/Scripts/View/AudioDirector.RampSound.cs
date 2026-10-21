using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// part-way into its clip (with a short fade-in, no click), and a fade-out of a one-shot that is still playing (a ramp that has stopped
    /// moving stops its hydraulic). Everything else of the voice is the event's own (bus, level, 3D, pool, sound log). Nothing else uses them.
    /// </summary>
    public partial class AudioDirector
    {
        /// <summary>A started one-shot: its pool slot, start time and event id (the slot may be reused later: checked before every use).</summary>
        public struct RampVoice { public int slot; public float start; public string id; public bool Valid => slot >= 0; }

        sealed class RampFade { public Voice v; public float start, target, speed; }
        readonly List<RampFade> rampFades = new List<RampFade>();
        int rampFadeFrame = -1;
        /// <summary>Proof: one-shots started part-way in, fades out begun / finished.</summary>
        public static int RampSkips, RampFadeOuts, RampFadeDone;

        /// <summary>Fire, starting `skip` seconds into the clip (clip seconds) with a fade-in of `fadeIn` seconds.</summary>
        public static RampVoice FireFrom(string id, Vector3 pos, int emitter, float volMul, float pitchMul, float skip, float fadeIn)
        {
            var none = new RampVoice { slot = -1 };
            if (!I) return none;
            I.lastStarted = null;
            if (!I.Play(id, pos, emitter, volMul, pitchMul)) return none;
            var v = I.lastStarted;
            if (v == null || v.src == null || v.def == null) return none;
            if (skip > 0f && v.src.clip) { v.src.time = Mathf.Clamp(skip, 0f, Mathf.Max(0f, v.src.clip.length - 0.05f)); RampSkips++; }
            if (fadeIn > 0f) { v.fade = 0f; I.rampFades.Add(new RampFade { v = v, start = v.start, target = 1f, speed = 1f / fadeIn }); I.Apply(v); }
            return new RampVoice { slot = I.pool.IndexOf(v), start = v.start, id = v.def.id };
        }

        /// <summary>Fades a started one-shot out over `seconds` (then stops it); nothing when it has ended or its slot plays another sound.</summary>
        public static void FadeOut(RampVoice r, float seconds)
        {
            if (!I || !r.Valid || r.slot >= I.pool.Count) return;
            var v = I.pool[r.slot];
            if (v == null || !v.busy || v.def == null || v.def.id != r.id || Mathf.Abs(v.start - r.start) > 1e-4f || !v.src || !v.src.isPlaying) return;
            I.rampFades.RemoveAll(f => f.v == v);
            I.rampFades.Add(new RampFade { v = v, start = v.start, target = 0f, speed = 1f / Mathf.Max(0.02f, seconds) });
            RampFadeOuts++;
        }

        /// <summary>Once a frame (OzShuttleRampSound.Tick): the fades move on; a voice faded out is stopped and freed.</summary>
        public static void StepRampFades()
        {
            if (!I || I.rampFades.Count == 0 || I.rampFadeFrame == Time.frameCount) return;
            I.rampFadeFrame = Time.frameCount;
            float dt = Time.unscaledDeltaTime;
            for (int i = I.rampFades.Count - 1; i >= 0; i--)
            {
                var f = I.rampFades[i]; var v = f.v;
                if (v == null || !v.busy || !v.src || Mathf.Abs(v.start - f.start) > 1e-4f) { I.rampFades.RemoveAt(i); continue; }   // ended, or the slot plays another sound now
                v.fade = Mathf.MoveTowards(v.fade, f.target, f.speed * dt);
                I.Apply(v);
                if (f.target <= 0f && v.fade <= 0.001f) { v.src.Stop(); v.busy = false; I.rampFades.RemoveAt(i); RampFadeDone++; }
                else if (f.target > 0f && v.fade >= f.target - 1e-4f) I.rampFades.RemoveAt(i);
            }
        }
    }
}
