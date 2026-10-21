using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// The light of the shuttle's downwash (view only). The fog shader (OpenZ/BlastFog) lights a puff by the main light (the sun, the
    /// moon at night) with a wide wrap x _SunK and by the sky x _AmbK, nothing else: no lamps, no thruster lights, not the jets' warm
    /// ground pool. By day on bright sand that read darker than the lit ground under it, and at night over the jets' pool (which
    /// multiplies the lit ground under a low ship many times over) the dust was black smoke balls on an orange ground.
    /// This system's OWN copy of the material (the fort's is never touched) gets both factors raised every frame:
    /// <list type="bullet">
    /// <item>the shader's light is mirrored here from the same sources (the main light's linear colour x intensity, its direction with the
    /// shader's wrap, the scene's ambient probe straight up);</item>
    /// <item>the ENVIRONMENT raise Uenv lifts it to at least the light the ground under it takes (the main light x its elevation + the sky)
    /// and to at least its own light + a floor of <see cref="FloorK"/> x NightGrade.NightK (the night floor: "at least 0.28 of the base
    /// colour x NightK"); never below 1, so the day dust keeps all of its own light;</item>
    /// <item>the material's light is that x a fixed HEADROOM (<see cref="HeadroomDay"/> by day, <see cref="HeadroomNight"/> at night, eased
    /// with NightK, at most <see cref="UMax"/> in all), and every puff carries its own light in its colour relative to it: the
    /// environment's share, the two thruster lights at its place (their colour, intensity x the throttle and range, the falloff of the
    /// game's point lights) and the jets' ground pool (OzShuttleJets: the same strength, colour and 8 x 7 m footprint as the NightPool
    /// picture), so a puff over the pool is lit like the pool-lit ground under it and a puff at the ring's edge by the moon and the floor.
    /// The headroom never follows the jets, so a puff once born never brightens or darkens with them; the colours stay under 1 even in the
    /// pool's glow;</item>
    /// <item>at night a part of the moon's blue cast is taken out of the dust's colour (<see cref="NightWarmK"/>), so it reads light
    /// tan-grey, never blue or black.</item>
    /// </list>
    /// The numbers were set with an off-engine twin of this light against the game's day and night keys. The proof (OzShuttleDownwashProof) measures the dust against the ground
    /// on screen: by day never darker, at night at least 10 of 255 lighter.
    /// </summary>
    public sealed partial class OzShuttleDownwash
    {
        /// <summary>The night floor (x NightK), the share of the ground's light the dust takes at least, the headroom by day / at night,
        /// the most the material's light is raised in all, the share of a thruster light a puff takes, the share of the moon's colour cast
        /// taken out at night.</summary>
        public static float FloorK = 1.0f, GroundMatch = 1.08f, HeadroomDay = 3f, HeadroomNight = 16f, UMax = 64f, ThrusterDustK = 0.7f, NightWarmK = 0.25f;   // run 27D: measured in Play (night dust was darker than the ground; by day 1-2 / 255 under it)
        /// <summary>The share of the jets' ground pool a puff over it takes (1 = lit like the pool-lit ground under it), and the pool's
        /// footprint (m, along the ship's right and nose: OzShuttleJets places it 8 x 7 m) and its edge softness (OpenZ/NightPool _Softness).</summary>
        public static float PoolDustK = 1f, PoolAcrossM = 8f, PoolAlongM = 7f, PoolSoft = 0.55f;
        /// <summary>The fog shader's own numbers (OpenZ/BlastFog: _SunK 0.75, _AmbK 0.85, _Wrap 0.6), read from the material when it has them.</summary>
        public static float SunK0 = 0.75f, AmbK0 = 0.85f, Wrap0 = 0.6f;
        /// <summary>Proof: the material's last raise, the environment's share of it, the shader's light before it (luminance), the ground's,
        /// the floor, the night share; frames lit.</summary>
        public static float LastU = 1f, LastUEnv = 1f, LastFogLum, LastGroundLum, LastFloor, LastNight;
        public static int LightFrames;

        static readonly int SunKId = Shader.PropertyToID("_SunK"), AmbKId = Shader.PropertyToID("_AmbK"), WrapId = Shader.PropertyToID("_Wrap");
        static readonly Vector3[] UpDir = { Vector3.up };
        static readonly Color[] ShOut = new Color[1];
        static Light sunCache; static float sunSeekAt = -10f;
        bool kRead; float sunK0 = -1f, ambK0 = -1f, wrap0 = -1f;
        Color fogEnv = Color.white, fogLit = Color.white;                                                    // the environment's light and the material's (linear)
        Color poolLin = Color.black; Vector3 poolAt; bool poolOn;                                           // the jets' ground pool this frame (linear, x its strength)
        float uNow = 1f;
        /// <summary>This frame's raise of this system's light (the environment's x the headroom).</summary>
        public float U => uNow;

        static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>The main light the shader sees: the sun source when set and on, else the brightest directional light (looked up at most
        /// twice a second).</summary>
        static Light MainLight()
        {
            var s = RenderSettings.sun;
            if (s && s.isActiveAndEnabled && s.type == LightType.Directional) return s;
            if (sunCache && sunCache.isActiveAndEnabled) return sunCache;
            if (Time.unscaledTime - sunSeekAt < 0.5f) return null;
            sunSeekAt = Time.unscaledTime; sunCache = null; float best = -1f;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l && l.isActiveAndEnabled && l.type == LightType.Directional && l.intensity > best) { best = l.intensity; sunCache = l; }
            return sunCache;
        }

        static Color LightColour(Light l)
        {
            var c = l.color.linear * l.intensity;
            if (l.useColorTemperature && GraphicsSettings.lightsUseColorTemperature) c *= Mathf.CorrelatedColorTemperatureToRGB(l.colorTemperature);
            return c;
        }

        /// <summary>The fog shader's light and the ground's light now (linear), from the same sources the shader reads.</summary>
        public static void SceneLight(float sunK, float ambK, float wrap, out Color fog, out Color ground)
        {
            var sun = MainLight(); Color sc = Color.black; float up = 0f;
            if (sun) { sc = LightColour(sun); up = -sun.transform.forward.y; }
            RenderSettings.ambientProbe.Evaluate(UpDir, ShOut);
            var sh = ShOut[0];
            float ndlFog = Mathf.Clamp01((up + wrap) / (1f + wrap)), ndlGround = Mathf.Clamp01(up);
            fog = sc * (ndlFog * sunK) + sh * ambK; fog.a = 1f;
            ground = sc * ndlGround + sh; ground.a = 1f;
        }

        /// <summary>The environment raise Uenv (>= 1) for a shader light fogLum, a ground light groundLum and the night share.</summary>
        public static float Raise(float fogLum, float groundLum, float night)
        {
            float target = Mathf.Max(fogLum + FloorK * night, groundLum * GroundMatch);
            return Mathf.Max(1f, target / Mathf.Max(fogLum, 1e-3f));
        }

        /// <summary>The headroom for the night share.</summary>
        public static float Headroom(float night) => Mathf.Lerp(HeadroomDay, HeadroomNight, Mathf.Clamp01(night));

        void LightStep(float throttle)
        {
            if (!mat) return;
            if (!kRead)
            {
                kRead = true;
                sunK0 = mat.HasProperty(SunKId) ? mat.GetFloat(SunKId) : SunK0; ambK0 = mat.HasProperty(AmbKId) ? mat.GetFloat(AmbKId) : AmbK0;
                wrap0 = mat.HasProperty(WrapId) ? mat.GetFloat(WrapId) : Wrap0;
            }
            SceneLight(sunK0, ambK0, wrap0, out var fog, out var ground);
            float night = NightGrade.NightK, fl = Lum(fog), gl = Lum(ground);
            float uEnv = Mathf.Min(Raise(fl, gl, night), UMax);
            uNow = Mathf.Min(uEnv * Headroom(night), UMax);
            mat.SetFloat(SunKId, sunK0 * uNow); mat.SetFloat(AmbKId, ambK0 * uNow);
            fogEnv = fog * uEnv; fogLit = fog * uNow;
            if (fl < 1e-3f)
            {   // no light at all (no main light, a black sky): the colours still get a sane share
                fogEnv = new Color(FloorK, FloorK, FloorK, 1f) * Mathf.Max(0.05f, night); fogLit = fogEnv * Headroom(night);
            }
            LastU = uNow; LastUEnv = uEnv; LastFogLum = fl; LastGroundLum = gl; LastFloor = FloorK * night; LastNight = night; LightFrames++;
            // the jets' ground pool, as OzShuttleJets draws it (its strength, its switch, its place under the two mouths)
            float T = Mathf.Clamp01(throttle), h = Mathf.Max(0f, lastFeet);
            float pk = OzShuttleJets.PoolGain * T / (1f + (h / 4f) * (h / 4f)) * Mathf.Max(night, OzShuttleJets.PoolDayLow * OzShuttleJets.LowK(lastFeet));
            poolOn = pk > 0.01f && (!NightLights.I || NightLights.I.pools);
            poolLin = poolOn ? (new Color(1f, 0.55f, 0.22f) * pk).linear : Color.black;                    // a colour property is turned linear per channel
            var nl = root.TransformPoint(door ? door.NozzleL : new Vector3(2.45f, 2.26f, -0.2f)); var nr = root.TransformPoint(door ? door.NozzleR : new Vector3(-2.45f, 2.26f, -0.2f));
            poolAt = (nl + nr) * 0.5f;
            GroundStep();
        }

        /// <summary>The ground pool's share at a place (1 in its middle, soft to 0 at its 8 x 7 m edge, as OpenZ/NightPool falls off).</summary>
        float PoolShare(Vector3 at)
        {
            if (!poolOn) return 0f;
            var d = at - poolAt; float yaw = root.eulerAngles.y * Mathf.Deg2Rad;
            float ax = d.x * Mathf.Cos(yaw) - d.z * Mathf.Sin(yaw), az = d.x * Mathf.Sin(yaw) + d.z * Mathf.Cos(yaw);   // into the pool's own axes
            float r = 2f * Mathf.Sqrt((ax / PoolAcrossM) * (ax / PoolAcrossM) + (az / PoolAlongM) * (az / PoolAlongM));
            float f = Mathf.Clamp01((1f - r) / Mathf.Max(1e-3f, PoolSoft));
            return f * f * (3f - 2f * f);
        }

        /// <summary>The two thruster lights at a place (linear light, as the game's point lights fall off), x the throttle.</summary>
        Color Thrusters(Vector3 at)
        {
            float T = Mathf.Clamp01(lastThrottle); if (T <= 0.01f) return Color.black;
            var col = OzShuttleJets.LightColour.linear * (OzShuttleJets.LightIntensity * T);
            float r2 = OzShuttleJets.LightRange * OzShuttleJets.LightRange; var sum = Color.black;
            for (int s = 0; s < 2; s++)
            {
                var m = root.TransformPoint(door ? (s == 0 ? door.NozzleL : door.NozzleR) : new Vector3(s == 0 ? 2.45f : -2.45f, 2.26f, -0.2f)) - root.up * 0.8f;   // where OzShuttleJets puts the light
                float d2 = Mathf.Max(1f, (at - m).sqrMagnitude), f = d2 / Mathf.Max(0.01f, r2), smooth = Mathf.Clamp01(1f - f * f);
                sum += col * (smooth * smooth / d2);
            }
            return sum;
        }

        /// <summary>A puff's colour (sRGB) for a ground dust colour (sRGB) that spends its middle of life at `at`. Without the own fog system
        /// (the fallback puffs) the plain dust colour.</summary>
        Color Lit(Color srgb, Vector3 at)
        {
            if (!ps) return srgb;
            if (ground) return PoolMul(at, srgb.a);                                                           // the ground dust: its colour comes from the ground on screen
            var thr = Thrusters(at) * ThrusterDustK; var extra = thr;
            float share = PoolShare(at) * PoolDustK;
            if (share > 0f)
            {   // the pool multiplies all the light there (the moon's, the sky's and the thrusters'), as it does on the ground
                var all = fogEnv + thr;
                extra += new Color(all.r * poolLin.r, all.g * poolLin.g, all.b * poolLin.b) * share;
            }
            return Lit(srgb, fogEnv, fogLit, extra, LastNight);
        }

        /// <summary>Pure numbers (the proof and the off-engine twin): the colour for a dust colour, the environment's light, the material's
        /// light (the environment's x the headroom), the extra light at the puff (thrusters, pool) and the night share.</summary>
        public static Color Lit(Color srgb, Color env, Color material, Color extra, float night)
        {
            var lin = srgb.linear; float el = Mathf.Max(1e-3f, Lum(env));
            var o = Color.black;
            for (int c = 0; c < 3; c++)
            {
                float need = env[c] + extra[c], have = Mathf.Max(1e-4f, material[c]);
                float cast = env[c] / el, warm = cast > 1e-3f ? Mathf.Pow(cast, -NightWarmK * Mathf.Clamp01(night)) : 1f;
                o[c] = Mathf.Clamp01(lin[c] * need / have * warm);
            }
            o = o.gamma; o.a = srgb.a;
            return o;
        }
    }
}
