using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// details_v2 (L17 C01 / C02 traffic lights, C03 road-works signal): the lenses (GLOW_Red / GLOW_Amber / GLOW_Green, any
    /// number of heads) run a red - red+amber - green - amber cycle; every change clicks the relay (a quiet 3D one-shot, heard
    /// only near the camera). At night the LIGHT_ empties are real little lights in the colour of the lit lens (night light
    /// budget), so the lamps light the ground. Broken: the lenses go dark with a spark burst and a glass sound (DetailProp does
    /// the rest). View only.
    /// </summary>
    [RequireComponent(typeof(DetailProp))]
    public class TrafficLightCycle : MonoBehaviour
    {
        [Tooltip("seconds: green, amber, red, red+amber (a lens set without amber skips the amber phases)")]
        public float green = 7f, amber = 2.2f, red = 7f, redAmber = 1.2f;
        [Tooltip("seconds into the cycle at the start (C01 / C02 run opposite phases)")] public float phase = 0f;
        public Color redColor = new Color(1f, 0.12f, 0.08f), amberColor = new Color(1f, 0.62f, 0.1f), greenColor = new Color(0.2f, 1f, 0.45f);
        [Tooltip("emission of a lit lens by day / at night; an unlit lens keeps offGlow. Below the bloom threshold (NightGrade 0.95 at night, " +
                 "1.25 by day, as BaseProp's lenses): a clear coloured dot, never a bloom blob; the LIGHT_ lamps light the ground")]
        public float dayGlow = 0.95f, nightGlow = 0.85f, offGlow = 0.03f;
        public string clickEvent = "dtl_traffic_relay";
        public string[] clickClips = { "DetailsV2/dv2_relay_click_a", "DetailsV2/dv2_relay_click_b" };
        public float clickVol = 0.3f;
        public string sparkEvent = "dtl_neon_spark";
        public string[] sparkClips = { "s3_dtl_neon_spark", "sd_lamp_spark_a", "sd_lamp_spark_b" };

        DetailProp prop;
        readonly List<Renderer> reds = new List<Renderer>(), ambers = new List<Renderer>(), greens = new List<Renderer>();
        float t; int state = -1; bool dead;

        void Start()
        {
            prop = GetComponent<DetailProp>();
            prop.glowAtNight = false;                                             // this component drives the lenses and lights
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                string n = DetailProp.Base(r.name);
                if (n.StartsWith("GLOW_Red")) reds.Add(r); else if (n.StartsWith("GLOW_Amber")) ambers.Add(r); else if (n.StartsWith("GLOW_Green")) greens.Add(r);
            }
            t = phase;
            prop.BrokenBy += OnBreak;
        }

        float Cycle { get { return green + red + (ambers.Count > 0 ? amber + redAmber : 0f); } }

        /// <summary>0 green, 1 amber, 2 red, 3 red+amber.</summary>
        int StateAt(float s)
        {
            s = Mathf.Repeat(s, Mathf.Max(0.1f, Cycle));
            if (s < green) return 0; s -= green;
            if (ambers.Count > 0) { if (s < amber) return 1; s -= amber; }
            if (s < red) return 2;
            return 3;
        }

        void OnBreak(DetailProp p, Vector3 from)
        {
            dead = true;
            p.SetGlow(reds, Color.black, 0f); p.SetGlow(ambers, Color.black, 0f); p.SetGlow(greens, Color.black, 0f);
            var at = SparkAt();
            var fx = OpenZFxPlayer.I;
            if (fx)
            {
                for (int i = 0; i < 14; i++)
                    fx.EmitChannel("m12_spark", at, Random.onUnitSphere * Random.Range(2f, 4.5f) + Vector3.up * 2f, Random.Range(0.25f, 0.6f), Random.Range(0.05f, 0.1f), new Color(1f, 0.85f, 0.45f), 1f);
                fx.AddLight(at, new Color(1f, 0.8f, 0.5f), 2.5f, 4f, 0.25f);
            }
            DetailProp.Play(sparkEvent, sparkClips, at, 0.55f);
            DetailProp.Play("", new[] { "sd_lamp_glass_a", "sd_lamp_glass_b" }, at, 0.5f);
        }

        Vector3 SparkAt()
        {
            var s = prop.FindPrefix("SPARK_");
            if (s) return s.position;
            if (reds.Count > 0 && reds[0]) return reds[0].bounds.center;
            return transform.position + Vector3.up * prop.hitHeight * 0.8f;
        }

        void Update()
        {
            if (dead || !prop) return;
            var g = ZGame.I;
            float dt = g && g.paused ? 0f : Time.deltaTime;
            t += dt;
            int s = StateAt(t);
            if (s != state)
            {
                if (state >= 0)
                {
                    var at = transform.position + Vector3.up * prop.hitHeight * 0.8f;
                    if (DetailProp.NearListener(at, DetailProp.HearRange))                  // review fix: the listener rig (focus), not the camera
                        DetailProp.Play(clickEvent, clickClips, at, clickVol, Random.Range(0.95f, 1.05f));
                }
                state = s;
            }
            float night = DetailKit.Night;
            float on = Mathf.Lerp(dayGlow, nightGlow, night);
            bool rOn = s == 2 || s == 3, aOn = s == 1 || s == 3, gOn = s == 0;
            prop.SetGlow(reds, redColor, rOn ? on : offGlow);
            prop.SetGlow(ambers, amberColor, aOn ? on : offGlow);
            prop.SetGlow(greens, greenColor, gOn ? on : offGlow);
            var lc = gOn ? greenColor : aOn && !rOn ? amberColor : rOn && aOn ? Color.Lerp(redColor, amberColor, 0.5f) : redColor;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.45f, night));
            foreach (var l in prop.Lights) l.Drive(k, lc);
        }
    }
}
