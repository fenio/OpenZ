using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// details_v2 (L02 D01 waterfall, intact): the WATER_ ribbons scroll their streak texture along the flow (_BaseMap_ST
    /// through a property block: WATER_Upper 1.6 m/s, WATER_Lower 3.5 m/s, WATER_Stream 0.9 m/s, WATER_Pond 0.25 m/s, a second
    /// wobble so it never looks like a belt); white foam boils at FX_Foam (puffs + a ripple ring every ~0.7 s), mist drifts
    /// ENE with the wind from FX_Mist, spray droplets jump at FX_Spray. The loop sound (dtl_waterfall_loop, 3D, min 6 max 55 m)
    /// plays at SND_. Night-lit: the water and foam keep a moonlit sheen (emission x night) and a soft cool light at the plunge
    /// </summary>
    public class WaterfallFx : MonoBehaviour
    {
        public float upperSpeed = 1.6f, lowerSpeed = 3.5f, streamSpeed = 0.9f, pondSpeed = 0.25f;
        public Color sheen = new Color(0.55f, 0.8f, 0.95f);
        public float daySheen = 0.05f, nightSheen = 0.55f;
        const float NightSheenMax = 0.16f;
        public Vector3 windDrift = new Vector3(0.7f, 0.05f, 0.35f);
        public string loopEvent = "dtl_waterfall_loop";
        public string loopRes = "OpenZ/Audio/Sfx/SoundV3/s3_dtl_waterfall_loop";
        public float loopVol = 0.6f, loopMin = 6f, loopMax = 55f;
        public string[] splashClips = { "sd_splash_enter_a", "sd_splash_enter_b" };
        public float lightRange = 6f, lightIntensity = 0.6f;
        [Tooltip("Waterfall v3: water / foam albedo at full night (the modelled WF3 fall carries more foam than the v2 strip and read as a bright blue ribbon at night; 0.66 brings it back to the approved v2 night level). Used only while the WF3 set is installed")]
        public float nightAlbedo = 0.66f;
        /// <summary>
        /// v2 waterfall again. The v3-only behaviour below (inactive kit pieces skipped, smaller lip / break spray, night albedo dim)
        /// applies only while a WF3 set is installed under this waterfall (OpenZ/Details v2/Waterfall v3 install), so v2 plays
        /// exactly as before the final gate.</summary>
        bool wf3;

        sealed class Rib { public Renderer r; public float speed, v; public Color baseCol = Color.white, foamCol = Color.white; public bool cols; }
        readonly List<Rib> ribs = new List<Rib>();
        readonly List<Transform> foam = new List<Transform>(), mist = new List<Transform>(), spray = new List<Transform>();
        readonly List<float> sprayScale = new List<float>();                 // waterfall v3: the cascade breaks / the lip spray less than the plunge
        MaterialPropertyBlock mpb; string key; AudioSource own; float nextRipple, nextSplash; DetailLight glow; Camera cam;

        void Start()
        {
            mpb = new MaterialPropertyBlock();
            wf3 = false;
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == "WF3_L02" && t.gameObject.activeInHierarchy) { wf3 = true; break; }
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (wf3 && !t.gameObject.activeInHierarchy) continue;           // waterfall v3: the switched-off D01 kit pieces are not driven
                string n = DetailProp.Base(t.name);
                if (n.StartsWith("FX_Foam")) foam.Add(t); else if (n.StartsWith("FX_Mist")) mist.Add(t);
                else if (n.StartsWith("FX_Spray")) { spray.Add(t); sprayScale.Add(!wf3 ? 1f : n.Contains("Lip") || n.Contains("Top") ? 0.35f : n.Contains("Break") ? 0.5f : 1f); }
                var r = t.GetComponent<Renderer>();
                if (r && n.StartsWith("WATER_"))
                {
                    var rb = new Rib { r = r, speed = n.StartsWith("WATER_Upper") ? upperSpeed : n.StartsWith("WATER_Lower") ? lowerSpeed : n.StartsWith("WATER_Pond") ? pondSpeed : streamSpeed, v = Random.value };
                    var sm = r.sharedMaterial;
                    if (wf3 && sm && sm.HasProperty("_BaseColor") && sm.HasProperty("_FoamColor")) { rb.baseCol = sm.GetColor("_BaseColor"); rb.foamCol = sm.GetColor("_FoamColor"); rb.cols = true; }
                    ribs.Add(rb);
                }
            }
            var snd = DetailKit.Find(transform, "SND_Waterfall");
            if (wf3 && snd && !snd.gameObject.activeInHierarchy) snd = null;
            if (!snd) foreach (var t in GetComponentsInChildren<Transform>(true)) if ((!wf3 || t.gameObject.activeInHierarchy) && DetailProp.Base(t.name).StartsWith("SND_")) { snd = t; break; }
            var at = snd ? snd.position : (foam.Count > 0 ? foam[0].position : transform.position);
            if (AudioDirector.I && AudioDirector.Has(loopEvent)) { key = "dv2_wf_" + GetInstanceID(); AudioDirector.I.StartLoop(key, loopEvent, at, null, loopVol); }
            else
            {
                var c = DetailKit.Pool3D.Clip(loopRes);
                if (c)
                {
                    var go = new GameObject("DV2_WaterfallLoop"); go.transform.SetParent(transform, false); go.transform.position = at;
                    own = go.AddComponent<AudioSource>(); own.clip = c; own.loop = true; own.spatialBlend = 1f; own.dopplerLevel = 0f;
                    own.rolloffMode = AudioRolloffMode.Linear; own.minDistance = loopMin; own.maxDistance = loopMax;
                    own.volume = loopVol * (CombatAudio.I ? CombatAudio.I.volume * CombatAudio.BusGain(CombatAudio.Bus.World) : 0.55f);
                    own.time = Random.Range(0f, Mathf.Max(0f, c.length - 0.1f)); own.Play();
                }
            }
            OzSoundLog.Add("DetailsV2", loopEvent, loopRes, key != null ? "waterfall loop (AudioDirector)" : own ? "waterfall loop (own 3D source)" : "waterfall loop clip missing", loopVol, at);
            Transform lt = null;
            foreach (var t in GetComponentsInChildren<Transform>(true)) if ((!wf3 || t.gameObject.activeInHierarchy) && DetailProp.Base(t.name).StartsWith("LIGHT_")) { lt = t; break; }
            if (lt) glow = new DetailLight(lt, sheen, lightRange, lightIntensity, 1.2f, "dtl_waterfall_" + GetInstanceID());
        }

        void OnDisable()
        {
            if (key != null && AudioDirector.I) AudioDirector.I.StopLoop(key, 0.3f);
            key = null;
            if (glow != null) glow.Kill();
        }

        void Update()
        {
            float dt = Time.deltaTime, t = Time.time;
            float night = DetailKit.Night;
            var e = Color.Lerp(sheen, new Color(0.78f, 0.82f, 0.86f), night * 0.65f).linear * Mathf.Lerp(daySheen, Mathf.Min(nightSheen, NightSheenMax), night);
            foreach (var rb in ribs)
            {
                if (!rb.r) continue;
                rb.v = Mathf.Repeat(rb.v + rb.speed * dt * (1f + 0.08f * Mathf.Sin(t * 1.7f + rb.speed)), 1000f);
                rb.r.GetPropertyBlock(mpb);
                mpb.SetVector("_BaseMap_ST", new Vector4(1f, 1f, 0.03f * Mathf.Sin(t * 0.9f + rb.speed), -rb.v));            // v runs downstream: a falling offset moves the streaks down
                mpb.SetColor("_EmissionColor", e);
                if (rb.cols)
                {
                    float k = Mathf.Lerp(1f, nightAlbedo, night);
                    mpb.SetColor("_BaseColor", new Color(rb.baseCol.r * k, rb.baseCol.g * k, rb.baseCol.b * k, rb.baseCol.a));
                    mpb.SetColor("_FoamColor", new Color(rb.foamCol.r * k, rb.foamCol.g * k, rb.foamCol.b * k, rb.foamCol.a));
                }
                rb.r.SetPropertyBlock(mpb);
            }
            if (glow != null) glow.Drive(0.6f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.45f, night)), Color.Lerp(sheen, new Color(0.8f, 0.84f, 0.88f), 0.5f));   // softer, less blue plunge light
            if (!cam) cam = Camera.main;
            var fx = OpenZFxPlayer.I;
            if (!fx || (cam && !DetailKit.OnScreen(cam, transform.position, 0.6f) && foam.Count > 0 && !DetailKit.OnScreen(cam, foam[0].position, 0.4f))) return;
            var white = Color.Lerp(new Color(1f, 1f, 1f), new Color(0.75f, 0.85f, 1f), night);
            foreach (var f in foam)
            {   // boiling white foam at the plunge
                if (Random.value < dt * 14f)
                    fx.EmitChannel("m12_smoke", f.position + Random.insideUnitSphere * 0.6f, Vector3.up * Random.Range(0.3f, 0.8f) + windDrift * 0.3f, Random.Range(0.6f, 1.1f), Random.Range(0.35f, 0.7f), white, 0.75f);
                if (t >= nextRipple) { fx.Ripple(f.position, 0.6f); }
            }
            if (t >= nextRipple) nextRipple = t + Random.Range(0.55f, 0.9f);
            foreach (var m in mist)
                if (Random.value < dt * 5f)
                    fx.EmitChannel("m12_dust", m.position + Random.insideUnitSphere * 1.2f, windDrift * Random.Range(0.8f, 1.4f) + Vector3.up * 0.25f, Random.Range(2.2f, 3.4f), Random.Range(0.9f, 1.6f), white, 0.22f);
            for (int i = 0; i < spray.Count; i++)
            {
                var s = spray[i]; float k = i < sprayScale.Count ? sprayScale[i] : 1f;
                if (Random.value < dt * 18f * k)
                    fx.EmitChannel("m12_splash", s.position + Random.insideUnitSphere * 0.4f * k, Random.insideUnitSphere * 1.6f * k + Vector3.up * Random.Range(1.2f, 2.6f) * Mathf.Lerp(0.6f, 1f, k), Random.Range(0.4f, 0.8f), Random.Range(0.04f, 0.08f), white, 0.9f);
            }
            if (t >= nextSplash && foam.Count > 0)
            {   // an extra splash in the loop now and then (the plan's fallback layer)
                nextSplash = t + Random.Range(3f, 5f);
                if (key == null && own == null) DetailProp.Play("", splashClips, foam[0].position, 0.25f);
            }
        }
    }
}
