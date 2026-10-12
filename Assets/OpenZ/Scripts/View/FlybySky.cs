// OpenZ (GPL-3.0)
// While a low shot can show the horizon (ZodFlyover.Active and its goal is not "home", or a CaptureKit cine camera / recording runs)
// the game camera gets a sky dome made from the painted panorama of the level (sky_panoramas_v1: Resources/OpenZ/Sky, loaded on
// demand, unloaded after the fade), a sun disc + glow on the DayNight sun direction (the side the shadows fall away from), and the
// haze is driven to the painting's own horizon colour (option B: far ground, skirt edge and painted horizon meet in one colour).
// Fade in / out over FadeSeconds (eased); at the "home" shot it fades out and is gone before the player gets control. Weight 0 =
// gameplay: no dome draw, no texture resident, every global back to what Atmosphere / DayNight set (zero cost, zero change).
using UnityEngine;
using UnityEngine.Rendering;
// Flyby sky: the sun sits LOW, just above the far horizon, centre-right, with a big warm bloom halo, the painted dawn sky with lit cloud streaks over a
// warm haze. So: (1) the drawn sun keeps the DayNight key light's azimuth (the shadows at the start of play fall away from where the sun
// was) but is held at most SunVisualMaxDeg over the horizon (the key light itself is 9-16 deg up during the briefing); (2) the disc is an
// HDR emissive (DiscGain) the URP bloom catches, drawn THROUGH the haze in the atmosphere pass (it was lost in the dawn haze); (3) over
// the haze band the far fog thins out on the sky (SkyClear) so the painting reads; (4) the fog's sun in-scatter peaks on the drawn sun
// during the flyby; (5) Unload logs the freed MB and drops the run material + dome when the RTS camera has the view.

namespace OpenZ
{
    [DefaultExecutionOrder(5000)]                                                                     // LateUpdate after DayNight / Atmosphere
    public sealed class FlybySky : MonoBehaviour
    {
        public static FlybySky I { get; private set; }
        /// <summary>Fade time in and out (s). The row: "fades out over ~1 s before the hand-back".</summary>
        public static float FadeSeconds = 1f;
        /// <summary>After the hand-back (no flyby, no cine) any rest of the fade finishes this fast (s).</summary>
        public static float LateFadeSeconds = 0.25f;
        /// <summary>Proofs / capture recipes: hold the sky on (it still fades).</summary>
        public static bool ForceOn;
        /// <summary>Sun disc radius and soft edge (deg) and the glow shape. Tunables for the Play look (statics, no rebuild).</summary>
        public static float DiscRadiusDeg = 1.1f, DiscEdgeDeg = 0.25f, DiscGain = 9f, GlowTightExp = 360f, GlowWideExp = 10f,
            GlowWide = 0.45f, GlowGain = 1.3f;
        /// <summary>
        /// the far fog clears over the haze band on sky pixels (0 = all haze as before, 1 = the painting), the haze band height as a multiple
        /// of the painting's horizon blend, and how much of the HDR disc shows through the haze.</summary>
        public static float SunVisualMaxDeg = 1.4f, SkyClear = 0.92f, HazeBandK = 1.4f, SunThroughHaze = 0.9f;
        /// <summary>Last unload (Status / proofs).</summary>
        public static string LastUnload = "none yet";
        /// <summary>Dome radius (m): inside the near / far planes; the shader puts every pixel on the far plane.</summary>
        const float DomeRadius = 50f;
        public const string JsonPath = "OpenZ/Sky/sky_panoramas", MaterialPath = "OpenZ/Sky/M_FlybySky", ShaderName = "OpenZ/FlybySky";

        public float Weight { get; private set; }
        public string LoadedLevel => loadedLevel;
        public Texture2D Panorama => tex;

        static readonly int IdW = Shader.PropertyToID("_FlybySkyW"), IdSunDir = Shader.PropertyToID("_FlybySunDir"),
            IdGlow = Shader.PropertyToID("_FlybySunGlow"), IdShape = Shader.PropertyToID("_FlybySunShape"),
            IdDisc = Shader.PropertyToID("_FlybySunDisc"), IdSeaY = Shader.PropertyToID("_FlybySeaY"),
            IdBlend = Shader.PropertyToID("_FlybyHorizonBlend"), IdHorizon = Shader.PropertyToID("_HorizonColor"),
            IdTex = Shader.PropertyToID("_FlybySkyTex"), IdTop = Shader.PropertyToID("_SkyTop"), IdHor = Shader.PropertyToID("_Horizon"),
            IdExp = Shader.PropertyToID("_Exponent"), IdGainH = Shader.PropertyToID("_PanoGainH"), IdGainZ = Shader.PropertyToID("_PanoGainZ"),
            IdAway = Shader.PropertyToID("_Away"), IdClear = Shader.PropertyToID("_FlybySkyClear"),
            IdBandK = Shader.PropertyToID("_FlybyHazeBandK"), IdThrough = Shader.PropertyToID("_FlybySunThrough");

        [System.Serializable] public class SkyLevel { public string texture, textureHi; public float[] horizonSRGB, zenithSRGB, sky15SRGB; public float corridorWidthDeg, corridorClearFromDeg, horizonBlendDeg = 3f, hazeToPanoramaHorizon = 1f; }
        [System.Serializable] public class SkyLevels { public SkyLevel L02, L17; }
        [System.Serializable] public class SkyJson { public int version; public SkyLevels levels; }

        static SkyJson json; static bool jsonTried;
        static Mesh dome;
        Material mat; Texture2D tex; ResourceRequest req; string loadedLevel, failedLevel; float offSince = -1f;
        DayNight dayNight; float seaY = -1.5f; int sceneSeen = int.MinValue;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I) return;
            var go = new GameObject("FlybySky"); DontDestroyOnLoad(go); I = go.AddComponent<FlybySky>();
        }

        public static FlybySky Ensure() { if (!I) Boot(); return I; }

        void OnDisable() { Shader.SetGlobalFloat(IdW, 0f); Weight = 0f; }
        void OnDestroy() { Unload(); if (mat) Destroy(mat); if (I == this) I = null; }

        public static SkyLevel Entry(string level)
        {
            if (!jsonTried)
            {
                jsonTried = true;
                var ta = Resources.Load<TextAsset>(JsonPath);
                if (ta) { try { json = JsonUtility.FromJson<SkyJson>(ta.text); } catch (System.Exception e) { Debug.LogWarning("[OpenZ FlybySky] " + JsonPath + ": " + e.Message); } }
            }
            if (json == null || json.levels == null) return null;
            return level == "L17" ? json.levels.L17 : level == "L02" ? json.levels.L02 : null;
        }

        static string Level() => ZGame.I ? ZGame.I.level : null;

        void LateUpdate()
        {
            var fly = ZodFlyover.Live;
            bool flying = fly != null && fly.Active, cine = CaptureKit.CineActive && !CaptureKit.NoFlybySky;
            bool want = !string.IsNullOrEmpty(Level()) && ((flying && fly.SkyWanted) || cine || ForceOn);    // only in a level scene
            float dt = Time.captureFramerate > 0 ? 1f / Time.captureFramerate : Time.unscaledDeltaTime;   // fixed-step captures stay deterministic
            if (want) EnsureLoaded();
            PollLoad();
            bool ready = tex && Mat();
            float target = want && ready ? 1f : 0f;
            if (!ready) Weight = 0f;
            float secs = target > Weight || flying || cine || ForceOn ? FadeSeconds : LateFadeSeconds;
            Weight = Mathf.MoveTowards(Weight, target, dt / Mathf.Max(0.01f, secs));
            float w = Mathf.SmoothStep(0f, 1f, Weight);
            Shader.SetGlobalFloat(IdW, w);
            if (w <= 0f)
            {
                if (want) offSince = -1f;
                else if (offSince < 0f) offSince = Time.unscaledTime;
                else if ((tex || req != null) && Time.unscaledTime - offSince > 2f) Unload();               // gameplay: nothing resident
                return;
            }
            offSince = -1f;
            Drive(w);
        }

        void Drive(float w)
        {
            var cam = Camera.main; if (!cam) return;
            if (sceneSeen != UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle) LevelRefs();
            var e = Entry(loadedLevel);
            Light sun = RenderSettings.sun ? RenderSettings.sun : dayNight ? dayNight.sun : null;
            Vector3 s = VisualSun(sun ? -sun.transform.forward : new Vector3(0.94f, 0.16f, -0.3f).normalized);
            float t = dayNight ? dayNight.timeOfDay : DayNight.SunriseStart;
            // the paintings are sunrise-graded (README note 3): full painting around the sunrise start, graded to DayNight's colours away from it
            float fromSunrise = Mathf.Abs(Mathf.DeltaAngle(t * 360f, 0.93f * 360f)) / 360f;
            float away = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.07f, 0.15f, fromSunrise));
            float elev = Mathf.Asin(Mathf.Clamp(s.y, -1f, 1f)) * Mathf.Rad2Deg;
            float sunK = (1f - away) * Mathf.Clamp01((elev + 0.5f) / 3f);                                   // 0.63 at the 1.4 deg drawn sun (the tuned look)
            Color sunLin = sun ? sun.color.linear * Mathf.Min(sun.intensity, 1.2f) : new Color(1f, 0.72f, 0.6f);

            Shader.SetGlobalVector(IdSunDir, new Vector4(s.x, s.y, s.z, sunK));
            Shader.SetGlobalVector(IdGlow, new Vector4(sunLin.r * GlowGain, sunLin.g * GlowGain, sunLin.b * GlowGain, GlowWide));
            Shader.SetGlobalVector(IdShape, new Vector4(GlowTightExp, GlowWideExp, Mathf.Cos(DiscRadiusDeg * Mathf.Deg2Rad), Mathf.Cos((DiscRadiusDeg + DiscEdgeDeg) * Mathf.Deg2Rad)));
            var disc = new Color(1f, 0.86f, 0.7f).linear * DiscGain;
            Shader.SetGlobalVector(IdDisc, new Vector4(disc.r, disc.g, disc.b, 0f));
            Shader.SetGlobalFloat(IdSeaY, seaY);
            Shader.SetGlobalFloat(IdBlend, Mathf.Sin(Mathf.Max(0.5f, e != null ? e.horizonBlendDeg : 3f) * Mathf.Deg2Rad));
            Shader.SetGlobalFloat(IdClear, Mathf.Clamp01(SkyClear)); Shader.SetGlobalFloat(IdBandK, Mathf.Max(0.5f, HazeBandK));
            Shader.SetGlobalFloat(IdThrough, Mathf.Clamp01(SunThroughHaze));

            // option B: the haze (atmosphere horizon colour + camera background) goes to the painting's horizon colour with the same weight.
            // Raw shader values (Get/SetGlobalVector, no colour-space conversion): the start is exactly what Atmosphere.Update set this
            // frame, so w -> 0 lands on it with no step whatever SetGlobalColor converts; the target is the painting's horizon in linear.
            Color fogLin = (Color)Shader.GetGlobalVector(IdHorizon);
            Color panoH = e != null ? Lin(e.horizonSRGB, fogLin) : fogLin, panoZ = e != null ? Lin(e.zenithSRGB, fogLin) : fogLin;
            Color hTarget = Color.Lerp(panoH, fogLin, away);
            Color hz = Color.Lerp(fogLin, hTarget, w * (e != null ? Mathf.Clamp01(e.hazeToPanoramaHorizon) : 1f));
            Shader.SetGlobalVector(IdHorizon, hz);                                                            // after Atmosphere.Update (LateUpdate)
            cam.backgroundColor = new Color(hz.r, hz.g, hz.b, 1f).gamma;                                      // DayNight sets it again next frame

            var m = Mat();
            m.SetTexture(IdTex, tex);
            var gsky = RenderSettings.skybox;                                                                 // the gameplay gradient (DayNight colours)
            if (gsky && gsky != m && gsky.HasProperty(IdTop))
            {
                m.SetColor(IdTop, gsky.GetColor(IdTop)); m.SetColor(IdHor, gsky.GetColor(IdHor));
                if (gsky.HasProperty(IdExp)) m.SetFloat(IdExp, gsky.GetFloat(IdExp));
            }
            else { m.SetColor(IdTop, RenderSettings.ambientSkyColor); m.SetColor(IdHor, RenderSettings.fogColor); }
            Color skyLin = (gsky && gsky.HasProperty(IdTop) ? gsky.GetColor(IdTop) : RenderSettings.ambientSkyColor).linear;
            m.SetVector(IdGainH, (Vector4)Div(fogLin, panoH)); m.SetVector(IdGainZ, (Vector4)Div(skyLin, panoZ)); m.SetFloat(IdAway, away);   // raw gains

            var rp = new RenderParams(m)
            {
                camera = cam, layer = 0, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off, reflectionProbeUsage = ReflectionProbeUsage.Off,
                motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
                worldBounds = new Bounds(cam.transform.position, Vector3.one * (DomeRadius * 2.2f)),
            };
            Graphics.RenderMesh(rp, Dome(), 0, Matrix4x4.TRS(cam.transform.position, Quaternion.identity, Vector3.one * DomeRadius));
        }

        /// <summary></summary>
        public static Vector3 VisualSun(Vector3 toSun)
        {
            float el = Mathf.Asin(Mathf.Clamp(toSun.y, -1f, 1f)) * Mathf.Rad2Deg;
            var h = new Vector3(toSun.x, 0f, toSun.z);
            if (el <= SunVisualMaxDeg || h.sqrMagnitude < 1e-6f) return toSun;
            h.Normalize(); float e = SunVisualMaxDeg * Mathf.Deg2Rad;
            return h * Mathf.Cos(e) + Vector3.up * Mathf.Sin(e);
        }

        static Color Lin(float[] srgb, Color fallback) => srgb != null && srgb.Length >= 3 ? new Color(srgb[0], srgb[1], srgb[2]).linear : fallback;
        static Color Div(Color a, Color b) => new Color(a.r / Mathf.Max(1e-3f, b.r), a.g / Mathf.Max(1e-3f, b.g), a.b / Mathf.Max(1e-3f, b.b), 1f);

        void LevelRefs()
        {
            sceneSeen = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            dayNight = FindAnyObjectByType<DayNight>();
            var skirt = GameObject.Find("SKIRT");                                                             // OpenZSetup: the sea / sand plane
            seaY = skirt ? skirt.transform.position.y : -1.5f;
        }

        Material Mat()
        {
            if (mat) return mat;
            var asset = Resources.Load<Material>(MaterialPath);                                               // keeps the shader in the player build
            var sh = asset ? asset.shader : Shader.Find(ShaderName);
            if (!sh || !sh.isSupported) return null;
            mat = asset ? new Material(asset) : new Material(sh);
            mat.name = "M_FlybySky_Run"; mat.hideFlags = HideFlags.DontSave;
            return mat;
        }

        void EnsureLoaded()
        {
            string lv = Level(); if (string.IsNullOrEmpty(lv)) return;
            if (lv != loadedLevel) { Unload(); loadedLevel = lv; }
            if (tex || req != null || failedLevel == lv) return;
            var e = Entry(lv);
            if (e == null || string.IsNullOrEmpty(e.textureHi)) { Fail(lv, "no entry in " + JsonPath); return; }
            req = Resources.LoadAsync<Texture2D>(e.textureHi);                                               // 32 MiB BC7: async, the first shot is steep
        }

        void PollLoad()
        {
            if (req == null || !req.isDone) return;
            tex = req.asset as Texture2D; req = null;
            var e = Entry(loadedLevel);
            if (!tex && e != null && !string.IsNullOrEmpty(e.texture)) tex = Resources.Load<Texture2D>(e.texture);   // the 4k fallback, if imported
            if (!tex) { Fail(loadedLevel, "panorama missing in Resources/OpenZ/Sky (run OpenZ > Flyby Sky > Import + Check)"); return; }
            if (!Mat()) { Fail(loadedLevel, "shader " + ShaderName + " missing / unsupported"); return; }
            Debug.Log($"[OpenZ FlybySky] {loadedLevel}: {tex.name} {tex.width}x{tex.height} {tex.format} mips {tex.mipmapCount} wrap {tex.wrapModeU}/{tex.wrapModeV}");
        }

        void Fail(string lv, string why)
        {
            failedLevel = lv;
            Debug.LogWarning($"[OpenZ FlybySky] {lv}: {why}. The flyby keeps the gradient sky (no painted sky, no sun disc) until this is fixed.");
        }

        void Unload()
        {
            if (tex)
            {
                long b = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex);
                if (b <= 0) b = (long)tex.width * tex.height * (tex.format == TextureFormat.DXT1 || tex.format == TextureFormat.BC4 ? 1 : tex.format == TextureFormat.BC7 || tex.format == TextureFormat.DXT5 ? 2 : 8) / 2;
                LastUnload = $"{loadedLevel}: {tex.name} {tex.width}x{tex.height} {tex.format} unloaded, freed {b / 1048576f:F1} MB (+ sky material, dome mesh)";
                Resources.UnloadAsset(tex);
                Debug.Log("[OpenZ FlybySky] " + LastUnload + " - the RTS camera has the view, no sky / sun resident");
            }
            tex = null; req = null;
            if (mat) { if (Application.isPlaying) Destroy(mat); else DestroyImmediate(mat); mat = null; }
            if (dome) { if (Application.isPlaying) Destroy(dome); else DestroyImmediate(dome); dome = null; }
        }

        /// <summary>A UV sphere (48 x 24); the shader takes the exact view direction per pixel, so the tessellation never shows.</summary>
        static Mesh Dome()
        {
            if (dome) return dome;
            const int Lon = 48, Lat = 24;
            var v = new Vector3[(Lon + 1) * (Lat + 1)]; var idx = new int[Lon * Lat * 6]; int k = 0;
            for (int y = 0; y <= Lat; y++)
                for (int x = 0; x <= Lon; x++)
                {
                    float th = Mathf.PI * y / Lat, ph = 2f * Mathf.PI * x / Lon;
                    v[y * (Lon + 1) + x] = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                }
            for (int y = 0; y < Lat; y++)
                for (int x = 0; x < Lon; x++)
                {
                    int a = y * (Lon + 1) + x, b = a + Lon + 1;
                    idx[k++] = a; idx[k++] = b; idx[k++] = a + 1; idx[k++] = a + 1; idx[k++] = b; idx[k++] = b + 1;
                }
            dome = new Mesh { name = "FlybySkyDome", hideFlags = HideFlags.DontSave, vertices = v, triangles = idx };
            dome.bounds = new Bounds(Vector3.zero, Vector3.one * 2.2f);
            return dome;
        }

        /// <summary>Play check (an editor script): one status line.</summary>
        public static string Status()
        {
            if (!I) return "FlybySky: not booted";
            var fly = ZodFlyover.Live;
            return $"FlybySky: weight {I.Weight:F2}, level {I.loadedLevel ?? "-"}, panorama {(I.tex ? I.tex.name + " " + I.tex.width + "x" + I.tex.height + " " + I.tex.format : "not resident")}, "
                 + $"flyover {(fly != null && fly.Active ? fly.Current + (fly.SkyWanted ? " (sky wanted)" : "") : "off")}, cine {CaptureKit.CineActive}, force {ForceOn}; last unload: {LastUnload}";
        }
    }
}
