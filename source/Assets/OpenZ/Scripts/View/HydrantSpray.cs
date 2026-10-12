using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// details_v2 (L17 C06-C08 fire hydrants): broken, the cap flies off (LOOSE_ part, DetailProp) and a water column sprays out
    /// of the NOZZLE_ empty (splash droplets + white spray puffs leaning toward FRONT_ and with the wind, a ripple ring where it
    /// lands) with a hiss loop, for `sprayTime` seconds, then it dwindles to a trickle and stops. The hydrant stump (RUIN_)
    /// stays. Every stage has its sound: the burst (DetailProp break sound + a gush), the hiss loop, the last gurgle.
    /// </summary>
    [RequireComponent(typeof(DetailProp))]
    public class HydrantSpray : MonoBehaviour
    {
        public float sprayTime = 22f, fadeTime = 5f, height = 3.6f;
        public string hissEvent = "dtl_hydrant_spray_loop";
        public string hissRes = "OpenZ/Audio/Sfx/SoundV3/s3_dtl_hydrant_loop";
        public float hissVol = 0.55f;
        public string[] gushClips = { "sd_splash_enter_a", "sd_splash_enter_b" };
        public string[] endClips = { "sd_debris_splash_a", "sd_debris_splash_b" };

        DetailProp prop; Transform nozzle; float t0 = -1f; string key; AudioSource own; bool ended;
        Vector3 lean;

        void Start()
        {
            prop = GetComponent<DetailProp>();
            prop.BrokenBy += OnBreak;
        }

        void OnBreak(DetailProp p, Vector3 from)
        {
            nozzle = p.FindPrefix("NOZZLE_");
            var front = p.FindPrefix("FRONT_");
            lean = front ? (front.position - transform.position) : Vector3.back; lean.y = 0f;
            lean = lean.sqrMagnitude > 1e-4f ? lean.normalized : Vector3.back;
            t0 = Time.time;
            var at = Origin();
            DetailProp.Play("", gushClips, at, 0.6f);
            if (AudioDirector.I && AudioDirector.Has(hissEvent)) { key = "dv2_hyd_" + GetInstanceID(); AudioDirector.I.StartLoop(key, hissEvent, at, null, hissVol); }
            else
            {
                var c = DetailKit.Pool3D.Clip(hissRes);
                if (c)
                {
                    var go = new GameObject("DV2_Hiss"); go.transform.position = at; go.transform.SetParent(transform, true);
                    own = go.AddComponent<AudioSource>(); own.clip = c; own.loop = true; own.spatialBlend = 1f; own.dopplerLevel = 0f;
                    own.rolloffMode = AudioRolloffMode.Linear; own.minDistance = 5f; own.maxDistance = 40f;
                    own.volume = hissVol * (CombatAudio.I ? CombatAudio.I.volume * CombatAudio.BusGain(CombatAudio.Bus.World) : 0.55f); own.Play();
                }
            }
            OzSoundLog.Add("DetailsV2", hissEvent, hissRes, "hydrant spray loop start", hissVol, at);
        }

        Vector3 Origin() { return nozzle ? nozzle.position : transform.position + Vector3.up * 0.5f; }

        void Update()
        {
            if (t0 < 0f || ended) return;
            float age = Time.time - t0;
            float k = age < sprayTime ? 1f : 1f - Mathf.Clamp01((age - sprayTime) / fadeTime);
            float vol = hissVol * k;
            if (key != null && AudioDirector.I) AudioDirector.I.SetLoop(key, vol, 0.85f + 0.15f * k);
            if (own) { own.volume = vol * (CombatAudio.I ? CombatAudio.I.volume * CombatAudio.BusGain(CombatAudio.Bus.World) : 0.55f); own.pitch = 0.85f + 0.15f * k; }
            if (k <= 0f)
            {
                ended = true;
                if (key != null && AudioDirector.I) AudioDirector.I.StopLoop(key, 0.4f);
                if (own) Destroy(own.gameObject);
                DetailProp.Play("", endClips, Origin(), 0.35f);
                return;
            }
            var fx = OpenZFxPlayer.I; if (!fx) return;
            var at = Origin();
            var wind = new Vector3(0.6f, 0f, 0.3f);
            float up = Mathf.Sqrt(2f * 9.81f * height * k);
            int n = Mathf.RoundToInt(Mathf.Lerp(1f, 4f, k));
            for (int i = 0; i < n; i++)
            {
                var v = Vector3.up * up * Random.Range(0.85f, 1.05f) + lean * Random.Range(0.3f, 0.9f) * k + Random.insideUnitSphere * 0.35f + wind * 0.4f;
                fx.EmitChannel("m12_splash", at, v, Random.Range(0.6f, 1.0f), Random.Range(0.05f, 0.1f), new Color(0.85f, 0.93f, 1f), 0.9f);
            }
            if (Random.value < 0.35f * k)
                fx.EmitChannel("m12_smoke", at + Vector3.up * height * k * Random.Range(0.6f, 1f), wind + Vector3.up * 0.4f, Random.Range(0.9f, 1.4f), Random.Range(0.35f, 0.6f) * k, new Color(0.92f, 0.96f, 1f), 0.35f);
            if (Random.value < 0.08f * k)
            {   // where the column comes down: a splash on the pavement / in the puddle
                var land = at + lean * (0.6f + 0.8f * k); land.y = ZGame.I ? ZGame.I.GroundY(land.x, land.z) : at.y - 0.5f;
                fx.Droplets(land, 0.4f * k, 4); fx.Ripple(land, 0.35f * k);
            }
        }

        void OnDisable() { if (key != null && AudioDirector.I) AudioDirector.I.StopLoop(key, 0.2f); key = null; }
    }
}
