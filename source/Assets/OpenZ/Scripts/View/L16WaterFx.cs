using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Level 16 water dressing (the object L16_WaterDressing under Environment): the water tongues of the stream steps and the
    /// sheets of the wall falls scroll down their fall (the sheet shader's own scroll, as on the desert waterfall), foam boils and
    /// rings spread at the foot of each fall (FX_Foam_*), mist drifts from the tall ones (FX_Mist_*), and each fall has its
    /// loop sound (SND_Fall_*: the near loop at the wall falls and stream steps, the far loop at the great fall outside the map).
    /// The water runs DOWN: on every one of the 13 stream steps and the 5 wall falls the sheet's v grows with height (measured on the
    /// model), so the streak layers are moved toward smaller v.
    /// View only. Effects run only for falls on screen.
    /// </summary>
    public sealed class L16WaterFx : MonoBehaviour
    {
        public float stepSpeed = 1.4f, wallSpeed = 3.2f;
        public Color sheen = new Color(0.62f, 0.72f, 0.66f);
        public float daySheen = 0.03f, nightSheen = 0.12f;
        public Vector3 windDrift = new Vector3(0.5f, 0.05f, 0.25f);
        public string nearRes = "OpenZ/Audio/Sfx/L16/sd_waterfall_jungle_near_loop", farRes = "OpenZ/Audio/Sfx/L16/sd_waterfall_jungle_far_loop";
        public float wallVol = 0.5f, stepVol = 0.3f, greatVol = 0.45f;

        sealed class Rib { public Renderer r; public float speed, v; }
        sealed class Spot { public Vector3 at; public float scale; }
        readonly List<Rib> ribs = new List<Rib>();
        readonly List<Spot> foam = new List<Spot>(), mist = new List<Spot>();
        readonly List<AudioSource> loops = new List<AudioSource>();
        readonly List<float> loopVol = new List<float>();
        MaterialPropertyBlock mpb; Camera cam; float nextRipple;
        public static int LoopsStarted;

        void Start()
        {
            mpb = new MaterialPropertyBlock(); LoopsStarted = 0;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                var r = t.GetComponent<Renderer>();
                if (r && n.StartsWith("L16_FallSheets")) ribs.Add(new Rib { r = r, speed = stepSpeed, v = Random.value });
                else if (r && n.StartsWith("L16_WallFallSheets")) ribs.Add(new Rib { r = r, speed = wallSpeed, v = Random.value });
                else if (n.StartsWith("FX_Foam_")) foam.Add(new Spot { at = t.position, scale = n.Contains("_wall_") ? 1f : 0.45f });
                else if (n.StartsWith("FX_Mist_")) mist.Add(new Spot { at = t.position, scale = n.Contains("great") ? 3f : 1f });
                else if (n.StartsWith("SND_Fall_")) Loop(t.position, n);
            }
        }

        void Loop(Vector3 at, string name)
        {
            bool great = name.Contains("great"), wall = name.Contains("_wall_");
            string res = great ? farRes : nearRes; float vol = great ? greatVol : wall ? wallVol : stepVol;
            var c = DetailKit.Pool3D.Clip(res);
            if (c)
            {
                var go = new GameObject("L16_FallLoop_" + name); go.transform.SetParent(transform, false); go.transform.position = at;
                var s = go.AddComponent<AudioSource>(); s.clip = c; s.loop = true; s.spatialBlend = 1f; s.dopplerLevel = 0f; s.playOnAwake = false;
                s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = great ? 40f : wall ? 6f : 4f; s.maxDistance = great ? 190f : wall ? 60f : 38f;
                s.volume = vol * Bus(); s.time = Random.Range(0f, Mathf.Max(0f, c.length - 0.1f)); s.Play();
                loops.Add(s); loopVol.Add(vol); LoopsStarted++;
            }
            OzSoundLog.Add("L16", great ? "dtl_waterfall_l16_far_loop" : "dtl_waterfall_l16_near_loop", c ? c.name : null, c ? "LOOP World (" + name + ")" : "DROP clip missing", vol, at);
        }

        static float Bus() => CombatAudio.I ? CombatAudio.I.volume * CombatAudio.BusGain(CombatAudio.Bus.World) : 0.55f;

        void Update()
        {
            float dt = Time.deltaTime, t = Time.time, night = DetailKit.Night;
            var e = Color.Lerp(sheen, new Color(0.78f, 0.82f, 0.86f), night * 0.65f).linear * Mathf.Lerp(daySheen, nightSheen, night);
            foreach (var rb in ribs)
            {
                if (!rb.r) continue;
                rb.v = Mathf.Repeat(rb.v + rb.speed * dt * (1f + 0.08f * Mathf.Sin(t * 1.7f + rb.speed)), 1000f);
                rb.r.GetPropertyBlock(mpb);
                mpb.SetVector("_BaseMap_ST", new Vector4(1f, 1f, 0.03f * Mathf.Sin(t * 0.9f + rb.speed), rb.v));   // the sheets' v grows with height on this level's model: a growing offset runs the streaks DOWN the fall
                mpb.SetColor("_EmissionColor", e);
                rb.r.SetPropertyBlock(mpb);
            }
            float bus = Bus();
            for (int i = 0; i < loops.Count; i++) if (loops[i]) loops[i].volume = loopVol[i] * bus;
            if (dt <= 0f) return;
            if (!cam) cam = Camera.main;
            var fx = OpenZFxPlayer.I; if (!fx || !cam) return;
            var white = Color.Lerp(new Color(0.94f, 0.94f, 0.86f), new Color(0.7f, 0.78f, 0.86f), night);
            bool ring = t >= nextRipple; if (ring) nextRipple = t + Random.Range(1.1f, 1.7f);              // item 227: fewer rings at the feet (they read as big circles from above)
            foreach (var f in foam)
            {
                if (!DetailKit.OnScreen(cam, f.at, 0.2f)) continue;
                if (Random.value < dt * 8f * f.scale)                                                         // item 227: smaller, fainter, fewer foam puffs (they read as floating bits from above)
                    fx.EmitChannel("m12_smoke", f.at + Random.insideUnitSphere * 0.35f * f.scale + Vector3.up * 0.1f, Vector3.up * Random.Range(0.2f, 0.5f) + windDrift * 0.3f, Random.Range(0.4f, 0.7f), Random.Range(0.18f, 0.36f) * Mathf.Lerp(0.7f, 1f, f.scale), white, 0.4f);
                if (ring) fx.Ripple(f.at + new Vector3(Random.Range(-0.3f, 0.3f), 0.03f, Random.Range(-0.3f, 0.3f)), 0.18f + 0.14f * f.scale);   // item 227: half-size rings
            }
            foreach (var m in mist)
            {
                if (!DetailKit.OnScreen(cam, m.at, 0.3f)) continue;
                if (Random.value < dt * 4f)
                    fx.EmitChannel("m12_dust", m.at + Random.insideUnitSphere * 1.1f * m.scale, windDrift * Random.Range(0.8f, 1.4f) + Vector3.up * 0.25f, Random.Range(2.2f, 3.4f), Random.Range(0.9f, 1.6f) * m.scale, white, 0.2f);
            }
        }
    }
}
