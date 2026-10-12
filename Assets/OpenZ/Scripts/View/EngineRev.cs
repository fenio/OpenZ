using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Per vehicle at most once every Gap seconds. Pure view (reads the order the player just gave).
    /// </summary>
    public class EngineRev : MonoBehaviour
    {
        public const float Gap = 2.5f, Length = 1.8f, Gain = 0.9f;
        sealed class Rev { public AudioSource a; public AudioLowPassFilter lp; public Transform follow; public float t, p0, p1, gain; public bool busy; }
        static EngineRev inst;
        readonly List<Rev> pool = new List<Rev>();
        static readonly Dictionary<int, float> last = new Dictionary<int, float>();
        public static int Played { get; private set; }

        /// <summary>AudioDirector: the player just ordered these units (move or attack).</summary>
        public static void Ordered(IEnumerable<ZObject> units)
        {
            var g = ZGame.I; var au = CombatAudio.I; if (!g || !au) return;
            int n = 0;
            foreach (var u in units)
            {
                if (u == null || u.Destroyed || u.Kind != ZKind.Vehicle || n >= 3) continue;
                string loop = LegacyMatch.EngineLoop(u); if (loop == null) continue;           // the mech walks: no engine
                if (last.TryGetValue(u.Id, out var t) && Time.unscaledTime - t < Gap) continue;
                if (!g.Views.TryGetValue(u.Id, out var v) || !v) continue;
                var clip = au.Clip(loop); if (!clip) continue;
                last[u.Id] = Time.unscaledTime; n++;
                float p0, p1;
                switch (u.Type) { case "jeep": p0 = 0.95f; p1 = 1.45f; break; case "heavy": p0 = 0.58f; p1 = 0.92f; break; default: p0 = 0.72f; p1 = 1.12f; break; }
                if (!inst) inst = new GameObject("OZ_EngineRev").AddComponent<EngineRev>();
                inst.Play(clip, v.transform, p0 * Random.Range(0.96f, 1.04f), p1 * Random.Range(0.96f, 1.04f), u.Type == "heavy" ? 1.1f : 1f);
            }
        }

        void Play(AudioClip clip, Transform follow, float p0, float p1, float gainMul)
        {
            Rev r = null; foreach (var x in pool) if (!x.busy) { r = x; break; }
            if (r == null)
            {
                if (pool.Count >= 6) return;
                var go = new GameObject("Rev" + pool.Count); go.transform.SetParent(transform, false);
                r = new Rev { a = go.AddComponent<AudioSource>(), lp = go.AddComponent<AudioLowPassFilter>() };
                r.a.playOnAwake = false; r.a.loop = true; r.a.dopplerLevel = 0f; r.a.spread = 40f; r.a.priority = 64;
                pool.Add(r);
            }
            CombatAudio.I.Configure(r.a, r.lp, CombatAudio.Profile.Field, 90f * CombatAudio.I.RangeScale);
            r.follow = follow; r.t = 0f; r.p0 = p0; r.p1 = p1; r.gain = Gain * gainMul * CombatAudio.BusGain(CombatAudio.Bus.Vehicles); r.busy = true;
            r.a.clip = clip; r.a.time = Random.Range(0f, Mathf.Max(0f, clip.length - 0.1f)); r.a.pitch = p0; r.a.volume = 0f;
            r.a.transform.position = follow.position; r.a.Play();
            Played++;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            foreach (var r in pool)
            {
                if (!r.busy) continue;
                r.t += dt; float u = r.t / Length;
                if (u >= 1f || !r.follow) { r.a.Stop(); r.busy = false; continue; }
                r.a.transform.position = r.follow.position;
                r.a.pitch = Mathf.Lerp(r.p0, r.p1, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.45f)));      // rev up to pull-away
                float env = u < 0.12f ? u / 0.12f : Mathf.Pow(1f - (u - 0.12f) / 0.88f, 1.6f);                  // swell, then fade out
                r.a.volume = r.gain * env;
            }
        }
    }
}
