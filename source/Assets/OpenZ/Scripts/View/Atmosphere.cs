using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Level atmosphere (plan/04 4.1c), driven by the DayNight cycle. Feeds OpenZ/AtmosphereFullscreen (volumetric fog,
    /// god rays, lamp-lit night fog, heat shimmer) through shader globals and runs the desert sand/dust particles.
    /// Desert: dusty haze, god rays at sunrise/sunset, midday heat shimmer, blowing sand, dust devils, blue-hour river mist.
    /// City: light harbour haze by day, drifting low mystery fog at dusk/night (denser over water, lit by the street lamps),
    /// wet asphalt sheen at night. Units stay readable: the fog can hide at most (1 - minTransmittance).
    /// </summary>
    [ExecuteAlways]
    public class Atmosphere : MonoBehaviour
    {
        public enum Preset { Desert, City }
        public Preset preset = Preset.Desert;
        public DayNight dayNight;
        [Tooltip("Parent of DEC_*lamp* objects (the environment root)")] public Transform lampRoot;
        [Tooltip("Level id for the water mask (Resources/OpenZ/Maps/<id>_grid.json)")] public string level = "L02";
        public Material groundMaterial;
        [Range(0.3f, 1f)] public float minTransmittance = 0.78f;   // units must stay readable at the RTS camera
        public bool particles = true;
        [Tooltip("City fog banks: the water value assumed beyond the map edge (0 land .. 1 sea), eased in over 40 m so the banks never stop in a straight line at the border. -1 = 0.6")]
        public float outsideWater = -1f;
        /// <summary>Night package: NightLights fills the fog lamps (xyz + range, linear colour) of the lit lights nearest the
        /// camera focus (street lamps, floods, searchlight heads, with their flicker and damage) and returns the count.
        /// Null = the lamp props of lampRoot (the original behaviour).</summary>
        public static System.Func<Vector3, Vector4[], Vector4[], int> FogLampSource;

        static readonly int IdEnabled = Shader.PropertyToID("_AtmoEnabled"), IdColor = Shader.PropertyToID("_FogColor"),
            IdAmbient = Shader.PropertyToID("_FogAmbient"), IdDensity = Shader.PropertyToID("_FogDensity"),
            IdFalloff = Shader.PropertyToID("_FogHeightFalloff"), IdBase = Shader.PropertyToID("_FogBase"),
            IdNoiseScale = Shader.PropertyToID("_FogNoiseScale"), IdNoiseAmount = Shader.PropertyToID("_FogNoiseAmount"),
            IdWind = Shader.PropertyToID("_FogWind"), IdMaxDist = Shader.PropertyToID("_FogMaxDistance"),
            IdSun = Shader.PropertyToID("_FogSunScatter"), IdG = Shader.PropertyToID("_FogG"),
            IdMinT = Shader.PropertyToID("_MinTransmittance"), IdLow = Shader.PropertyToID("_LowFogDensity"),
            IdLowH = Shader.PropertyToID("_LowFogHeight"), IdLowW = Shader.PropertyToID("_LowFogWaterBoost"),
            IdRect = Shader.PropertyToID("_FogMapRect"), IdMask = Shader.PropertyToID("_FogWaterMask"),
            IdLamps = Shader.PropertyToID("_FogLamps"), IdLampCols = Shader.PropertyToID("_FogLampColors"),
            IdLampCount = Shader.PropertyToID("_FogLampCount"), IdShimmer = Shader.PropertyToID("_ShimmerAmount");
        static readonly int IdLowMode = Shader.PropertyToID("_LowFogMode"), IdLowBanks = Shader.PropertyToID("_LowFogBanks"),
            IdLowShape = Shader.PropertyToID("_LowFogShape");
        static readonly int IdLowBanks2 = Shader.PropertyToID("_LowFogBanks2");
        public static float BankDensity = 0.075f;      // x mystery (night 1, dawn / dusk less): extinction per metre at the floor
        public static float BankLand = 0.45f, BankWater = 2.0f;           // density factor over land (car park, streets) / over water (canal, harbour)
        public static float BankLandDepth = 2.4f, BankWaterDepth = 4.0f;  // bank top in the cores (m); units stand out of the land banks
        public static float BankScale = 0.052f, BankThreshold = 0.53f, BankSoftness = 0.14f;
        public static float BankDrift = 0.7f, BankDetail = 0.36f, BankDetailScale = 2.31f;  // drift x wind (~0.9 m/s), wisp octave share / scale
        public static float NightHaze = 0.00045f;     // City haze at night (was 0.0012: a uniform blue-grey veil); the banks carry the night fog
        public static float SkyFloorY = -1.7f;        // the depthless L17 sea skirt plane (SKIRT y in L17_CarPark.unity = OpenZSetup city -1.7)
        /// <summary>A/B reference only (FogReadCheck): 0 normal, 1 banks off (haze on), 2 the whole atmosphere pass off. Not a shipped switch.</summary>
        public static int DebugFog = 0;
        const int MaxLamps = 8;
        static readonly int IdHorizonColor = Shader.PropertyToID("_HorizonColor"), IdHorizonStart = Shader.PropertyToID("_HorizonStart"),
            IdHorizonEnd = Shader.PropertyToID("_HorizonEnd"), IdSmoothness = Shader.PropertyToID("_Smoothness");
        readonly float[] nearD = new float[MaxLamps]; readonly int[] nearI = new int[MaxLamps];

        Texture2D waterMask;
        readonly List<Vector3> lamps = new List<Vector3>();
        readonly Vector4[] lampPos = new Vector4[16], lampCol = new Vector4[16];
        ParticleSystem sand; readonly List<ParticleSystem> devils = new List<ParticleSystem>(); readonly List<Vector3> devilDir = new List<Vector3>();

        [System.Serializable] class MapJson { public int width, height; public int[] classes; }

        void OnEnable()
        {
            BuildWaterMask();
            lamps.Clear();
            if (lampRoot)
                foreach (Transform t in lampRoot)
                    if (t.gameObject.activeInHierarchy && t.name.ToLowerInvariant().Contains("lamp")) lamps.Add(t.position + Vector3.up * 3.6f);
            if (Application.isPlaying && particles && preset == Preset.Desert) BuildDesertParticles();
        }

        void OnDisable() { Shader.SetGlobalFloat(IdEnabled, 0); }

        void BuildWaterMask()
        {
            var ta = Resources.Load<TextAsset>($"OpenZ/Maps/{level}_grid");
            if (!ta) return;
            var m = JsonUtility.FromJson<MapJson>(ta.text);
            // x 2.5, so the L17 fog banks thin out smoothly from the canal / harbour instead of ending at the quay line
            waterMask = new Texture2D(m.width, m.height, TextureFormat.RG16, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            int W = m.width, H = m.height;
            var wat = new float[W * H];
            for (int ty = 0; ty < H; ty++)
                for (int tx = 0; tx < W; tx++)
                    wat[(H - 1 - ty) * W + tx] = m.classes[ty * W + tx] == 2 ? 1f : 0f;
            var near = BoxBlur(BoxBlur(wat, W, H, 5), W, H, 5);
            var px = new Color32[W * H];
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color32((byte)(wat[i] > 0.5f ? 255 : 0), (byte)(255 * Mathf.Clamp01(Mathf.Max(wat[i], near[i] * 2.5f))), 0, 255);
            waterMask.SetPixels32(px); waterMask.Apply();
            Shader.SetGlobalTexture(IdMask, waterMask);
            Shader.SetGlobalVector(IdRect, new Vector4(0, -m.height * 2, m.width * 2, m.height * 2));
        }

        /// <summary></summary>
        static float[] BoxBlur(float[] src, int w, int h, int r)
        {
            var tmp = new float[w * h]; var dst = new float[w * h]; float n = 2 * r + 1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float s = 0; for (int k = -r; k <= r; k++) s += src[y * w + Mathf.Clamp(x + k, 0, w - 1)];
                    tmp[y * w + x] = s / n;
                }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float s = 0; for (int k = -r; k <= r; k++) s += tmp[Mathf.Clamp(y + k, 0, h - 1) * w + x];
                    dst[y * w + x] = s / n;
                }
            return dst;
        }

        // day phases from the DayNight time (0 noon, 0.3 golden sunset, 0.42 blue hour, 0.55-0.8 night, 0.9 dawn)
        static float Bump(float t, float c, float w) => Mathf.Clamp01(1 - Mathf.Abs(t - c) / w);

        void Update()
        {
            float t = dayNight ? dayNight.timeOfDay : 0.08f;
            float night = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.40f, 0.52f, t)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.84f, 0.92f, t)));
            float golden = Mathf.Max(Bump(t, 0.31f, 0.09f), Bump(t, 0.91f, 0.07f));
            float blue = Mathf.Max(Bump(t, 0.43f, 0.07f), Bump(t, 0.87f, 0.04f));
            float midday = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.10f, 0.22f, Mathf.Min(t, 1 - t)));
            Color amb = RenderSettings.ambientEquatorColor.linear * 0.8f;

            Shader.SetGlobalFloat(IdEnabled, 1);
            Shader.SetGlobalColor(IdAmbient, amb);
            Shader.SetGlobalFloat(IdBase, 0f);
            Shader.SetGlobalFloat(IdMaxDist, 480f);
            Shader.SetGlobalFloat(IdMinT, minTransmittance);
            Shader.SetGlobalColor(IdHorizonColor, RenderSettings.fogColor.linear);   // = camera background (DayNight)
            Shader.SetGlobalFloat(IdHorizonStart, 250f); Shader.SetGlobalFloat(IdHorizonEnd, 1100f);
            float lampK = 0;
            if (preset == Preset.Desert)
            {
                Color dust = new Color(1.0f, 0.86f, 0.66f), purple = new Color(0.78f, 0.66f, 1.0f);
                Shader.SetGlobalColor(IdColor, Color.Lerp(dust, purple, blue * 0.8f));
                Shader.SetGlobalFloat(IdDensity, Mathf.Lerp(0.0008f, 0.0012f, golden) * Mathf.Lerp(1f, 0.6f, night));
                Shader.SetGlobalFloat(IdFalloff, 30f);
                Shader.SetGlobalFloat(IdNoiseScale, 0.02f); Shader.SetGlobalFloat(IdNoiseAmount, 0.5f);
                Shader.SetGlobalVector(IdWind, new Vector4(3f, 0, 1.5f, 0));
                Shader.SetGlobalFloat(IdSun, Mathf.Lerp(1.0f, 1.9f, golden));            // god rays through the canyon
                Shader.SetGlobalFloat(IdG, Mathf.Lerp(0.35f, 0.65f, golden));
                Shader.SetGlobalFloat(IdLow, 0.05f * blue); Shader.SetGlobalFloat(IdLowH, 2.5f); Shader.SetGlobalFloat(IdLowW, 3f);
                Shader.SetGlobalFloat(IdLowMode, 0f);
                Shader.SetGlobalFloat(IdShimmer, 0f);
                lampK = night * 0.6f;
                UpdateDesertParticles(1 - night * 0.7f);
            }
            else
            {
                Shader.SetGlobalColor(IdColor, Color.Lerp(new Color(0.86f, 0.88f, 0.9f), new Color(0.62f, 0.68f, 0.8f), night));
                Shader.SetGlobalFloat(IdDensity, Mathf.Lerp(0.0007f, NightHaze, night));        // l17_night_fog_read: the night fog is the banks, not a thicker veil
                Shader.SetGlobalFloat(IdFalloff, 25f);
                Shader.SetGlobalFloat(IdNoiseScale, 0.035f); Shader.SetGlobalFloat(IdNoiseAmount, 0.8f);
                Shader.SetGlobalVector(IdWind, new Vector4(1.2f, 0, 0.6f, 0));             // slow drift over the canal
                Shader.SetGlobalFloat(IdSun, 1.0f); Shader.SetGlobalFloat(IdG, 0.4f);
                float mystery = Mathf.Max(night, golden * 0.35f + blue * 0.7f);
                // old 0.14 to 0.4 is the banned milky wash). Wind-scrolled FBM patches (~45 m) with clear gaps; ~5.5 m deep over the canal /
                // harbour with a soft falloff from the water (mask G), thin (1.6 m, 0.35x) over the car park and streets; lit by the moon
                // in-scatter and the FogLampSource lamps; capped by _MinTransmittance. Same night / dawn curve as before: no pop.
                // l17_night_fog_read: readable banks at the RTS camera (see AtmosphereFullscreen LowBankStep for the why). Same mystery curve:
                // continuous in the time of day (no pop), 0 by day.
                Shader.SetGlobalFloat(IdLow, BankDensity * mystery); Shader.SetGlobalFloat(IdLowH, BankLandDepth); Shader.SetGlobalFloat(IdLowW, BankWater);
                Shader.SetGlobalFloat(IdLowMode, 1f);
                Shader.SetGlobalVector(IdLowBanks, new Vector4(BankScale, BankThreshold, BankSoftness, BankWaterDepth));   // bank scale 1/m, cover threshold, softness, depth over water (m)
                Shader.SetGlobalVector(IdLowShape, new Vector4(BankLand, outsideWater >= 0f ? outsideWater : 0.6f, 0.06f, 0.5f));   // land density, outside water, water cover bonus, band step share
                Shader.SetGlobalVector(IdLowBanks2, new Vector4(BankDrift, BankDetail, SkyFloorY, BankDetailScale));
                Shader.SetGlobalFloat(IdShimmer, 0);
                lampK = night;
                if (groundMaterial) groundMaterial.SetFloat(IdSmoothness, Mathf.Lerp(0.05f, 0.5f, night));   // wet asphalt sheen at night
            }
            // up to 8 lamps nearest to the camera light the fog
            var cam = Camera.main; Vector3 c = cam ? cam.transform.position : Vector3.zero;
            int n = 0;
            if (FogLampSource != null) n = Mathf.Min(FogLampSource(c, lampPos, lampCol), lampPos.Length);   // night package
            else if (lampK > 0.01f)
            {
                // same lamps in the same order (OrderBy is stable: equal distances keep the list order, and so does this)
                int nk = 0;
                for (int li = 0; li < lamps.Count; li++)
                {
                    float ld = (lamps[li] - c).sqrMagnitude;
                    if (nk == MaxLamps && ld >= nearD[nk - 1]) continue;
                    int nj = nk < MaxLamps ? nk++ : nk - 1;
                    while (nj > 0 && nearD[nj - 1] > ld) { nearD[nj] = nearD[nj - 1]; nearI[nj] = nearI[nj - 1]; nj--; }
                    nearD[nj] = ld; nearI[nj] = li;
                }
                for (int q = 0; q < nk; q++)
                {
                    var p = lamps[nearI[q]];
                    lampPos[n] = new Vector4(p.x, p.y, p.z, 9f);
                    lampCol[n] = new Vector4(1f, 0.66f, 0.36f, 0) * (0.22f * lampK); n++;
                }
            }
            Shader.SetGlobalVectorArray(IdLamps, lampPos); Shader.SetGlobalVectorArray(IdLampCols, lampCol);
            Shader.SetGlobalFloat(IdLampCount, n);
            if (DebugFog == 1) Shader.SetGlobalFloat(IdLow, 0f);                          // l17_night_fog_read A/B: banks off (OzWeather's rain x keeps 0)
            else if (DebugFog >= 2) Shader.SetGlobalFloat(IdEnabled, 0f);                  // A/B: the whole atmosphere pass off
        }

        // ---------------------------------------------------------------- desert particles (play mode only)
        static Material particleMat;
        static Material ParticleMat()
        {
            if (particleMat) return particleMat;
            particleMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { hideFlags = HideFlags.DontSave };
            particleMat.SetFloat("_Surface", 1); particleMat.SetFloat("_Blend", 0);
            particleMat.SetOverrideTag("RenderType", "Transparent"); particleMat.renderQueue = 3000;
            particleMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); particleMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            particleMat.SetFloat("_ZWrite", 0); particleMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            // soft round puff: without a texture every particle is a hard square and a dust devil reads as a pixel blob
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            var px = new Color32[64 * 64];
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float dx = (x + 0.5f) / 32f - 1, dy = (y + 0.5f) / 32f - 1, a = Mathf.Clamp01(1 - Mathf.Sqrt(dx * dx + dy * dy));
                    px[y * 64 + x] = new Color32(255, 255, 255, (byte)(255 * a * a * (3 - 2 * a)));
                }
            tex.SetPixels32(px); tex.Apply();
            particleMat.SetTexture("_BaseMap", tex);
            return particleMat;
        }

        void BuildDesertParticles()
        {
            // blowing sand: thin stretched streaks hugging the ground around the camera focus
            var go = new GameObject("SandStreaks") { hideFlags = HideFlags.DontSave }; go.transform.SetParent(transform, false);
            sand = go.AddComponent<ParticleSystem>();
            var main = sand.main; main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(9f, 15f); main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
            main.startColor = new Color(0.96f, 0.86f, 0.68f, 0.22f); main.maxParticles = 300; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = sand.emission; em.rateOverTime = 60;
            var sh = sand.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(140, 0.8f, 140);
            go.transform.rotation = Quaternion.LookRotation(new Vector3(3f, 0, 1.5f).normalized);
            var col = sand.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                                              new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.3f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.lengthScale = 14; r.velocityScale = 0.06f;
            r.sharedMaterial = ParticleMat(); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // two dust devils wandering across the map
            for (int i = 0; i < 2; i++)
            {
                var d = new GameObject("DustDevil" + i) { hideFlags = HideFlags.DontSave }; d.transform.SetParent(transform, false);
                d.transform.position = new Vector3(40 + 60 * i, 0, -60 - 50 * i);
                var ps = d.AddComponent<ParticleSystem>();
                var m = ps.main; m.startLifetime = 3.5f; m.startSpeed = 0; m.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
                m.startColor = new Color(0.9f, 0.76f, 0.55f, 0.22f); m.maxParticles = 220; m.simulationSpace = ParticleSystemSimulationSpace.Local;
                var e = ps.emission; e.rateOverTime = 50;
                var s = ps.shape; s.shapeType = ParticleSystemShapeType.Circle; s.radius = 0.8f; s.rotation = new Vector3(90, 0, 0);
                var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.Local;
                v.orbitalY = new ParticleSystem.MinMaxCurve(4f); v.radial = new ParticleSystem.MinMaxCurve(0.35f); v.y = new ParticleSystem.MinMaxCurve(2.2f);
                var c2 = ps.colorOverLifetime; c2.enabled = true; c2.color = g;
                var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 0.6f, 1, 2.2f));
                var pr = d.GetComponent<ParticleSystemRenderer>(); pr.sharedMaterial = ParticleMat(); pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                devils.Add(ps); devilDir.Add(Quaternion.Euler(0, Random.Range(0, 360), 0) * Vector3.forward);
            }
        }

        void UpdateDesertParticles(float strength)
        {
            if (!Application.isPlaying) return;
            var rts = Camera.main ? Camera.main.GetComponent<RtsCamera>() : null;
            if (sand)
            {
                if (rts) sand.transform.position = rts.focus + Vector3.up * 0.4f;
                var em = sand.emission; em.rateOverTime = 60 * strength;
            }
            for (int i = 0; i < devils.Count; i++)
            {
                var d = devils[i]; if (!d) continue;
                var p = d.transform.position + devilDir[i] * (2.5f * Time.deltaTime);
                if (p.x < 10 || p.x > 134 || p.z > -10 || p.z < -170) devilDir[i] = -devilDir[i];
                if (Physics.Raycast(new Vector3(p.x, 200, p.z), Vector3.down, out var hit, 400)) p.y = hit.point.y;
                d.transform.position = p;
                var e = d.emission; e.rateOverTime = 50 * strength;
            }
        }
    }
}
