using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// details_v2 (L17 C05 'FRESH ROBOTS - OPEN 24/7'): the GLOW_Neon* tubes glow (a bit by day, bright at night) and now and
    /// then stutter: a few quick drop-outs with a crackle, the buzz loop dipping with them. The colour comes from the tube name
    /// (GLOW_NeonPink / Cyan / Green / Red / Yellow / Blue / White) else DetailProp.glowColor. At half health the sign sparks
    /// and goes dark (it keeps standing and sputters a few seconds); at zero it breaks (DetailProp). The LIGHT_ empties throw
    /// a little coloured light on the pavement at night.
    /// </summary>
    [RequireComponent(typeof(DetailProp))]
    public class NeonFlicker : MonoBehaviour
    {
        [Tooltip("tube emission by day / at night, below the bloom threshold (NightGrade 0.95 at night, 1.25 by day): the letters read, no bloom blob")]
        public float dayGlow = 0.75f, nightGlow = 0.85f;
        [Tooltip("seconds between stutters (min, max)")] public Vector2 stutterEvery = new Vector2(4f, 11f);
        public string crackleEvent = "";
        public string[] crackleClips = { "s3_dtl_neon_spark" };
        public float crackleVol = 0.16f;
        public string sparkEvent = "dtl_neon_spark";
        public string[] sparkClips = { "s3_dtl_neon_spark", "sd_lamp_spark_a", "sd_lamp_spark_b", "sd_lamp_spark_c" };

        DetailProp prop;
        readonly List<Renderer> tubes = new List<Renderer>(); readonly List<Color> cols = new List<Color>();
        float next, stutterUntil, sputterUntil, nextSputter; bool dark;

        public static Color TubeColour(string n, Color fallback)
        {
            if (n.Contains("Pink")) return new Color(1f, 0.25f, 0.62f);
            if (n.Contains("Cyan")) return new Color(0.25f, 0.95f, 1f);
            if (n.Contains("Green")) return new Color(0.35f, 1f, 0.35f);
            if (n.Contains("Red")) return new Color(1f, 0.16f, 0.12f);
            if (n.Contains("Yellow")) return new Color(1f, 0.86f, 0.25f);
            if (n.Contains("Blue")) return new Color(0.3f, 0.5f, 1f);
            if (n.Contains("White")) return new Color(0.95f, 0.95f, 1f);
            return fallback;
        }

        void Start()
        {
            prop = GetComponent<DetailProp>();
            prop.glowAtNight = false;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                string n = DetailProp.Base(r.name);
                if (!n.StartsWith("GLOW_")) continue;
                tubes.Add(r); cols.Add(TubeColour(n, prop.glowColor));
            }
            next = Time.time + Random.Range(stutterEvery.x, stutterEvery.y);
            prop.Hit += OnHit;
            prop.BrokenBy += (p, from) => { GoDark(); };
        }

        void OnHit(DetailProp p, Vector3 from)
        {
            if (!dark && p.Hp <= p.health * 0.5f) { GoDark(); sputterUntil = Time.time + 3.5f; }
        }

        void GoDark()
        {
            if (dark) return;
            dark = true;
            for (int i = 0; i < tubes.Count; i++) prop.SetGlow(tubes[i], cols[i], 0f);
            foreach (var l in prop.Lights) l.Drive(0f, prop.lightColor);
            prop.StopLoop();
            Sparks(22, 1f);
            DetailProp.Play("", new[] { "sd_glass_shatter_a", "sd_glass_shatter_b" }, SparkAt(), 0.45f);
        }

        Vector3 SparkAt()
        {
            var s = prop.FindPrefix("SPARK_");
            if (s) return s.position;
            if (tubes.Count > 0 && tubes[0]) return tubes[Random.Range(0, tubes.Count)].bounds.center;
            return transform.position + Vector3.up * prop.hitHeight * 0.8f;
        }

        void Sparks(int n, float vol)
        {
            var at = SparkAt(); var fx = OpenZFxPlayer.I;
            if (fx)
            {
                for (int i = 0; i < n; i++)
                    fx.EmitChannel("m12_spark", at + Random.insideUnitSphere * 0.25f, Random.onUnitSphere * Random.Range(2f, 5f) + Vector3.up * 1.5f, Random.Range(0.25f, 0.7f), Random.Range(0.05f, 0.1f), new Color(1f, 0.85f, 0.5f), 1f);
                fx.AddLight(at, new Color(0.8f, 0.85f, 1f), 3f * vol, 5f, 0.2f);
            }
            DetailProp.Play(sparkEvent, sparkClips, at, 0.6f * vol);
        }

        void Update()
        {
            if (!prop || prop.Broken) return;
            if (dark)
            {   // sputtering wires after the neon went dark
                if (Time.time < sputterUntil && Time.time >= nextSputter) { nextSputter = Time.time + Random.Range(0.35f, 0.9f); Sparks(5, 0.4f); }
                return;
            }
            float night = DetailKit.Night;
            float k = Mathf.Lerp(dayGlow, nightGlow, night);
            if (Time.time >= next)
            {
                next = Time.time + Random.Range(stutterEvery.x, stutterEvery.y);
                stutterUntil = Time.time + Random.Range(0.35f, 0.8f);
                DetailProp.Play(crackleEvent, crackleClips, SparkAt(), crackleVol, Random.Range(1.2f, 1.5f));
            }
            float f = 1f;
            if (OzMotion.Reduced) f = 0.96f;
            else if (Time.time < stutterUntil) f = Mathf.PerlinNoise(Time.time * 38f, 0.37f) > 0.45f ? 1f : 0.05f;
            else f = 0.93f + 0.07f * Mathf.PerlinNoise(Time.time * 6f, 0.1f);                // the hum wavers a little
            for (int i = 0; i < tubes.Count; i++) prop.SetGlow(tubes[i], cols[i], k * f);
            prop.SetLoopLevel(0.35f + 0.65f * f);
            float lk = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.45f, night)) * f;
            var lc = cols.Count > 0 ? cols[0] : prop.lightColor;
            foreach (var l in prop.Lights) l.Drive(lk, lc);
        }
    }
}
