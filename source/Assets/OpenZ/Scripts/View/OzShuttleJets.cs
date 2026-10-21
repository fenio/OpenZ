using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// The playable shuttle's two downward rocket jets (rule DV118, view only), driven by OzPlayableShuttle every frame. One jet
    /// hangs under each engine pod, out of the kit's nozzle (ShuttleRearDoor: SH_LiftNozzle_L / _R, the mouth markers); its axis is
    /// the ship's own down, so the jets tilt with the bank and the pitch. Built from the game's own effects, nothing new is drawn:
    /// <list type="bullet">
    /// <item>the CORE: the missile exhaust flame (OpenZFxPlayer.DrawExhaust, the fx_v2 flame at 15 x HDR: it blooms by day and at
    /// night) plus a second, smaller cone further down; a hot orange HALO round it (DrawHalo);</item>
    /// <item>the BODY: turbulent fire puffs (channel m12_fire, fx_v2 heat ramp) and fast flame streaks (m12_flame) blown down the axis
    /// with the ship's own speed added; near the ground the jet splashes outward along it;</item>
    /// <item>HEAT SHIMMER under each mouth (the heavy shells' M_HeatDistort quad);</item>
    /// <item>the nozzle THROAT glows (the kit's SH_NozzleGlow parts, emission over the day bloom threshold);</item>
    /// <item>LIGHT: the ship's two thruster point lights sit just under the mouths, warm, flickering (steady with CALM SCREEN); at
    /// night a warm glow lies on the ground under a low ship (the night lights' ground pool picture), and by day too while the pads
    /// are under 3 m (at <see cref="PoolDayLow"/> of the night's strength): full thrust lights the ground at the lift-off.</item>
    /// <item>LIFT-OFF: while the pads are under 3 m (eased out by 4 m) the core is drawn at <see cref="CoreScaleLow"/> instead of
    /// <see cref="CoreScale"/> and both cores hang <see cref="CoreDropLowM"/> lower (about 2.1-2.6 m of flame under each mouth against
    /// 1.2-1.5; seen past the pod from the RTS camera 1.6-1.8 m of the nearer jet against 0.8-1.1), and the hot halo is larger, brighter and hangs lower,
    /// under the pod's belly, so the two jets read from the RTS camera at the lift-off and the touchdown, not only in flight.</item>
    /// </list>
    /// THROTTLE: 0 parked; the take-off lights them at 0.7 at once and spools them to full in 0.8 s; full while climbing and coming
    /// down, 0.75 (a slow pulse) cruising, 0.85 hovering; touchdown takes them to 0 over 0.6 s; destroyed: out at once. While the ship
    /// is off the ground the throttle never drops under 0.7. The parked ship's side wisp (OzShuttleExhaust) is off in flight.
    /// QUALITY: the particle rates are shared by the graphics preset (HIGH 1, MEDIUM 0.75, LOW 0.5); at most about 40 particles of
    /// the shared pools are alive; nothing is emitted off screen, while paused or with the effects player off.
    /// </summary>
    public sealed class OzShuttleJets
    {
        public static float CoreScale = 4.0f, HaloM = 4.5f, FireRate = 50f, FlameRate = 30f, SplashRate = 12f, SplashBelowM = 7f;
        public static float ShimmerStrength = 0.010f, ShimmerSize = 2.8f, GlowGain = 8f, LightRange = 9f, LightIntensity = 3.2f, PoolGain = 2.4f;
        public static Color Fire = new Color(1f, 0.56f, 0.20f), LightColour = new Color(1f, 0.58f, 0.26f), Halo = new Color(1f, 0.50f, 0.16f), GlowLinear = new Color(1f, 0.55f, 0.2f);
        /// <summary>Near the ground (the pads under LowBelowM, eased out over LowFadeM): the core's scale, the halo's size (m), how far under
        /// the mouth it hangs (m, 0.4 in flight) and its brightness x; the ground pool's share of its night strength by day.</summary>
        public static float CoreScaleLow = 5.5f, LowBelowM = 3f, LowFadeM = 1f, HaloLowM = 5.2f, HaloLowDrop = 0.9f, HaloLowGain = 1.3f, PoolDayLow = 0.35f;
        /// <summary>Near the ground both cores are drawn this much lower (m, x the near-ground share): a core is centred on its point, so
        /// half of it sits inside the nozzle and the pod; from the RTS camera the pod hides most of a jet that starts at the mouth
        /// (an off-engine count on the ship's own triangles, the nearer jet at the median heading: 0.8-1.1 m seen in round 2, 1.6-1.8 m with this).</summary>
        public static float CoreDropLowM = 0.6f;
        /// <summary>Half the length of the flame mesh (the effects model's FX_Flame is 0.70 m long, drawn x ExhaustBaseScale 1.12 and its
        /// 0.94 mean pulse): the metres of flame under the mouth are 0.35 x 1.12 x 0.94 x the scale.</summary>
        public const float FlameHalfM = 0.35f;
        /// <summary>Proof: frames seen off the ground, of them with the throttle under 0.7; particles emitted; frames drawn near the ground.</summary>
        public static int AirFrames, AirFramesUnder, Emitted, LowFrames;
        public static float LastThrottle;
        /// <summary>Proof: the last frame's core scale (x the throttle), the flame's metres under the mouth, the halo's size (m), the
        /// near-ground share, the ground pool's strength and whether it was drawn.</summary>
        public static float LastCoreScale, LastCoreM, LastHaloM, LastLowK, LastPoolK;
        public static bool LastPoolOn;

        /// <summary>The metres of flame drawn under a mouth for a core scale (x the throttle) drawn drop m lower: the longer of the two cones.</summary>
        public static float CoreMetres(float scaleT, float drop = 0f) =>
            drop + Mathf.Max(FlameHalfM * OpenZFxCatalog.ExhaustBaseScale * 0.94f * scaleT, 0.5f + FlameHalfM * OpenZFxCatalog.ExhaustBaseScale * 0.94f * scaleT * 0.7f);
        /// <summary>1 while the pads are under LowBelowM, 0 from LowBelowM + LowFadeM (smooth between).</summary>
        public static float LowK(float feetM) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LowBelowM, LowBelowM + LowFadeM, feetM));

        public float Throttle { get; private set; }
        readonly Transform root; readonly ShuttleRearDoor door; readonly Light[] glow; readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock(); MaterialPropertyBlock poolMpb;
        static Material shimmer; static bool shimmerTried;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        Renderer pool; float spool = -1f, down = -1f, acc0, acc1, acc2, age, lastGlow = -1f; bool dead;

        public OzShuttleJets(Transform shipRoot, ShuttleRearDoor d, Light[] lights)
        {
            root = shipRoot; door = d; glow = lights ?? new Light[0];
            foreach (var l in glow) if (l) { l.transform.SetParent(root, false); l.color = LightColour; l.range = LightRange; l.intensity = 0f; l.shadows = LightShadows.None; }
            if (!shimmerTried)
            {
                shimmerTried = true;
                var dm = Resources.Load<Material>("OpenZ/FX/M_HeatDistort");
                if (dm && dm.shader && dm.shader.isSupported)
                {
                    shimmer = new Material(dm) { name = "ShuttleJetShimmer (runtime)" };
                    shimmer.SetFloat("_Mode", 0f); shimmer.SetFloat("_Strength", ShimmerStrength); shimmer.SetFloat("_Inner", 0.1f); shimmer.SetFloat("_NoiseScale", 5f); shimmer.SetFloat("_Speed", 3.2f);
                }
            }
            var pm = NightMeshes.Pool();
            if (pm)
            {
                var go = NightMeshes.Part("OZ_FxShuttleJetPool", null, NightMeshes.Cube(), pm, false, Vector3.zero, Quaternion.identity, Vector3.one);
                pool = go.GetComponent<Renderer>(); pool.enabled = false;
            }
        }

        public void TakeOff() { spool = 0f; down = -1f; dead = false; }
        public void Touchdown() { down = 0f; spool = -1f; }
        public void Out() { dead = true; Throttle = 0f; spool = down = -1f; }
        public void Dispose() { if (pool) Object.Destroy(pool.gameObject); }

        /// <summary>The graphics preset's particle share (HIGH 1, MEDIUM 0.75, LOW 0.5); ShareOverride >= 0 replaces it (the proof's LOW take-off).</summary>
        public static float ShareOverride = -1f;
        static float Share => ShareOverride >= 0f ? ShareOverride : OzQuality.Level == 0 ? 1f : OzQuality.Level == 1 ? 0.75f : 0.5f;

        /// <summary>One frame. state / lift / airborne = the rules'; feetM = the ship's root over the ground (m); vel = its drawn velocity (m/s).</summary>
        public void Step(float dt, Sim.ZShuttleState state, float lift, bool airborne, float feetM, Vector3 vel)
        {
            age += dt;
            float want;
            switch (state)
            {
                case Sim.ZShuttleState.Climb: want = 1f; break;
                case Sim.ZShuttleState.Cruise: want = 0.75f + 0.05f * Mathf.Sin(2f * Mathf.PI * 1.3f * age); break;
                case Sim.ZShuttleState.Hover: want = 0.85f; break;
                case Sim.ZShuttleState.Descend: want = 1f; break;
                default: want = 0f; break;
            }
            if (dead) want = 0f;
            float t;
            if (spool >= 0f) { spool += dt; t = Mathf.Lerp(0.7f, 1f, Mathf.Clamp01(spool / 0.8f)); if (spool >= 0.8f) spool = -1f; }     // ignition, then the spool up
            else if (down >= 0f) { down += dt; t = Mathf.Lerp(Throttle, 0f, Mathf.Clamp01(down / 0.6f)); if (down >= 0.6f) { down = -1f; t = 0f; } }
            else t = Mathf.MoveTowards(Throttle, want, dt * 1.5f);
            if (airborne && !dead) t = Mathf.Max(t, 0.7f);
            Throttle = Mathf.Clamp01(t); LastThrottle = Throttle;
            if (airborne && !dead) { AirFrames++; if (Throttle < 0.7f) AirFramesUnder++; }
            Draw(dt, feetM, vel);
        }

        void Draw(float dt, float feetM, Vector3 vel)
        {
            float T = Throttle;
            var axis = -root.up;
            var mL = root.TransformPoint(door ? door.NozzleL : new Vector3(2.45f, 2.26f, -0.2f));
            var mR = root.TransformPoint(door ? door.NozzleR : new Vector3(-2.45f, 2.26f, -0.2f));
            // the throat glow and the lights, also when nothing else is drawn
            float gl = T * GlowGain;
            if (Mathf.Abs(gl - lastGlow) > 0.01f && door)
            {
                lastGlow = gl; var c = GlowLinear * gl;
                for (int s = 0; s < 2; s++) { var r = s == 0 ? door.NozzleGlowL : door.NozzleGlowR; if (!r) continue; r.GetPropertyBlock(mpb, 0); mpb.SetColor(EmissionId, c); r.SetPropertyBlock(mpb, 0); }
            }
            bool calm = OzMotion.Reduced;
            for (int i = 0; i < glow.Length; i++)
            {
                var l = glow[i]; if (!l) continue;
                l.transform.position = (i == 0 ? mL : mR) + axis * 0.8f;
                float fl = OzJetLightSteady.Flicker(age, i, calm, feetM);                                // K420: gentle while the dust is up (View/OzShuttleJetLight.K420.cs)
                l.intensity = LightIntensity * T * fl; l.enabled = T > 0.01f;
            }
            float night = NightGrade.NightK, lowK = LowK(feetM);
            if (lowK > 0.01f && T > 0.01f) LowFrames++;
            LastLowK = lowK;
            if (pool)
            {
                float h = Mathf.Max(0f, feetM), k = PoolGain * T / (1f + (h / 4f) * (h / 4f)) * Mathf.Max(night, PoolDayLow * lowK);   // by day too, near the ground
                bool on = k > 0.01f && (!NightLights.I || NightLights.I.pools);
                LastPoolK = k; LastPoolOn = on;
                if (pool.enabled != on) pool.enabled = on;
                if (on)
                {
                    var c = (mL + mR) * 0.5f; var g = ZGame.I; float gy = g ? g.GroundY(c.x, c.z) : 0f;
                    pool.transform.SetPositionAndRotation(new Vector3(c.x, gy + 0.35f, c.z), Quaternion.Euler(0f, root.eulerAngles.y, 0f));
                    pool.transform.localScale = new Vector3(8f, 3.0f, 7f);
                    var col = new Color(1f, 0.55f, 0.22f) * k; col.a = 1f;
                    poolMpb ??= new MaterialPropertyBlock(); poolMpb.SetColor(NightMeshes.ColorId, col); pool.SetPropertyBlock(poolMpb);   // one block, kept (no native garbage per frame)
                }
            }
            float core = Mathf.Lerp(CoreScale, CoreScaleLow, lowK), haloM = Mathf.Lerp(HaloM, HaloLowM, lowK) * (0.75f + 0.25f * T);
            float drop = CoreDropLowM * lowK;
            LastCoreScale = core * T; LastCoreM = T > 0.01f ? CoreMetres(core * T, drop) : 0f; LastHaloM = T > 0.01f ? haloM : 0f;
            if (T <= 0.01f) return;
            var fx = OpenZFxPlayer.I; var game = ZGame.I;
            if (!fx || !fx.isActiveAndEnabled || (game && game.paused)) return;
            if (!DetailKit.OnScreen(Camera.main, (mL + mR) * 0.5f, 0.2f)) return;
            float share = Share;
            for (int s = 0; s < 2; s++)
            {
                var m = s == 0 ? mL : mR;
                fx.DrawExhaust(m + axis * drop, root.up, core * T, age);
                fx.DrawExhaust(m + axis * (0.5f + drop), root.up, core * 0.7f * T, age + 0.37f);
                fx.DrawHalo(m + axis * Mathf.Lerp(0.4f, HaloLowDrop, lowK), haloM, Halo, (night > 0.5f ? 0.7f : 1.0f) * Mathf.Lerp(1f, HaloLowGain, lowK));
                if (shimmer && Camera.main)
                {
                    var c = Camera.main; var sp = m + axis * 1.2f;
                    Graphics.DrawMesh(OpenZFortShockwave.Quad(), Matrix4x4.TRS(sp, Quaternion.LookRotation(sp - c.transform.position, c.transform.up), Vector3.one * ShimmerSize), shimmer, 0, null, 0, null, ShadowCastingMode.Off, false);
                }
            }
            // fire puffs, flame streaks, the ground splash: rates x the preset's share x the throttle
            acc0 += dt * FireRate * share * T; acc1 += dt * FlameRate * share * T;
            var gm = ZGame.I; float groundL = gm ? gm.GroundY(mL.x, mL.z) : 0f, groundR = gm ? gm.GroundY(mR.x, mR.z) : 0f;
            bool low = Mathf.Min(mL.y - groundL, mR.y - groundR) < SplashBelowM;
            if (low) acc2 += dt * SplashRate * share * T;
            while (acc0 >= 1f)
            {
                acc0 -= 1f;
                for (int s = 0; s < 2; s++)
                {
                    var m = s == 0 ? mL : mR;
                    var v = axis * (8f * (0.8f + 0.2f * T)) + vel + Random.insideUnitSphere * 0.6f;
                    fx.EmitChannel("m12_fire", m + axis * 0.15f, v, Random.Range(0.22f, 0.3f), Random.Range(0.5f, 0.75f) * (0.8f + 0.2f * T), Fire, 0.9f); Emitted++;
                }
            }
            while (acc1 >= 1f)
            {
                acc1 -= 1f;
                for (int s = 0; s < 2; s++) { var m = s == 0 ? mL : mR; fx.EmitChannel("m12_flame", m + axis * 0.2f, axis * 10f + vel, 0.10f, 0.3f, Fire, 1f); Emitted++; }
            }
            while (acc2 >= 1f)
            {
                acc2 -= 1f;
                for (int s = 0; s < 2; s++)
                {
                    var m = s == 0 ? mL : mR; float gy = s == 0 ? groundL : groundR;
                    if (m.y - gy > SplashBelowM) continue;
                    var at = new Vector3(m.x, gy + 0.25f, m.z); if (fx.WetAt(at)) continue;                     // over water the downwash makes the spray
                    float a = Random.Range(0f, Mathf.PI * 2f); var o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    float k = 1f - (m.y - gy) / SplashBelowM;
                    if (OzShuttleDownwash.SplashStreaks) fx.EmitChannel("m12_flame", at + o * 0.3f, o * (7f * k) + Vector3.up * 0.15f, Random.Range(0.08f, 0.12f), Random.Range(0.22f, 0.32f), Fire, 0.5f * k);
                    else fx.EmitChannel("m12_fire", at + o * 0.4f, o * (5f * k) + Vector3.up * 0.4f, Random.Range(0.25f, 0.35f), Random.Range(0.55f, 0.8f), Fire, 0.75f * k);
                    Emitted++;
                }
            }
        }
    }
}
