using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>
    /// FX_NukeCap + FX_NukeStem from combat_fx.glb with OpenZ/Nuke, 8.5 s timeline (rise (1-(1-p)^2) x 18 m, expand
    /// 1-(1-p)^4, base 13.5, cap flatten lerp(1, .45, min(4p, 1)), cap spin .2 rad/s, stem after .15 s at half the rise with
    /// radius .3 x cap, opacity (cap .96 / stem .9) x (1 - smoothstep(.68, 1, p))), scaled x1.4 for our 20 m fort.
    /// Around it: a white flash, the big ground shockwave, a large scorch, heavy shake, the fort charring with Zod's
    /// 12-17 side explosions and debris (bfort.cpp:419-481). Visual only: Zod's fort death deals no extra damage.
    /// <para>
    /// per-pixel TEMPERATURE (OpenZ/Nuke v3: white-hot centre -> hot -> orange -> deep orange -> ember -> smoke) at an HDR gain
    /// that stays 2-4x above the bloom threshold for the whole mushroom (Gain(t): 7 at the blast, ~3 while it rises, 1.9 at
    /// the end), not only for the first 1.6-3.2 s; hot fire is emissive and unlit (no clay shading), the white-hot core is
    /// additive; a glow envelope, a sustained fire light that lights the ground, a heat-shimmer quad, a bloom sustain
    /// (OpenZBloomDirector.Sustain) and the fort shockwave (OpenZFortShockwave). Colours are LINEAR SetVector values picked
    /// through URP ACES (tools/nuke_model.py). Materials are built here at run time (no asset for a bulk pass to repaint).
    /// The Play check OpenZ/Checks/Nuke Glow (NukeGlowCheck) measures the rendered result day and night.</para>
    /// </summary>
    public class NukeFx : MonoBehaviour
    {
        public const float Duration = 8.5f, Scale = 1.4f;
        /// <summary>fx_v2: set by OpenZNukeStrike just before Trigger at the impact: the descent whistle already played while the
        /// bomb fell, so the blast does not play it again.</summary>
        public static bool StrikeDelivered;
        // cap was 38 m wide and hid the stem from above), gentler displacement, rolling bands in the shader, a dust skirt
        const float CapBase = 10f, RiseMax = 27f, StemRadius = 0.24f;

        // ============================================================================================ nuke_fx_v3 glow contract
        // The fireball's brightness and its colour are separate knobs. A "too white" complaint is fixed by the RAMP / the heat
        // (HeatTau, HeatEnd, the white band), NEVER by lowering the gain: every earlier fix lowered one shared gain (fx_v2:
        // .45 -> .8 with a 1.6-3.2 s surge) and the mushroom went back to matte clay. MinHoldGain is the floor: Build() logs an
        // error below it and the Nuke Glow gate measures the frame.
        /// <summary>HDR gain on the fire ramp (linear): Start at the blast, easing (GainTau s) to Hold while the mushroom rises,
        /// End at the fade-out (from 60 % of the timeline). Night x NightGainMul (the night bloom threshold is lower).</summary>
        public static float GainStart = 7f, GainHold = 3.0f, GainEnd = 1.9f, GainTau = 0.9f, NightGainMul = 0.62f;   // v3d: .62 (v3c .68: L17 night 3 s white blob 52 % > 45 %; was .8: 69 %)
        public const float MinHoldGain = 2.4f;
        /// <summary>Timeline heat (shader _Heat): 1 at the blast -> HeatEnd with HeatTau: the white-hot centre shrinks, orange grows.</summary>
        public static float HeatEnd = 0.30f, HeatTau = 1.2f;
        /// <summary>Extra bloom intensity / threshold drop (gamma) while the fireball burns (OpenZBloomDirector.Sustain).</summary>
        public static float BloomSustain = 0.6f, BloomThresholdDrop = 0.1f;   // v3c: .6 (was 1.0: the bloom washed the rim to peach)
        /// <summary>Fire light that keeps lighting the ground while the fireball burns (URP intensity, range m).</summary>
        public static float FireLightPeak = 120f, FireLightRange = 95f;   // v3c: 120 (was 70: night ground x1.15 < 1.2 at 3 s)
        /// <summary>v3d: the fire light x (1 + this) at night (L17 night ground x1.43 &lt; 1.5 at 0.6 s, x1.16 &lt; 1.2 at 3 s: the lamps lift the baseline).</summary>
        public static float FireLightNightBoost = 1.0f, FlashNightSlow = 0.3f;
        /// <summary>Heat shimmer around the fireball (OpenZ/HeatDistort mode 0, fraction of the screen height).</summary>
        public static float ShimmerStrength = 0.009f;
        /// <summary>Glow envelope (additive shell 1.12 x the cap) gain as a fraction of the fire gain.</summary>
        public static float EnvelopeGain = 0.22f;
        /// <summary>
        /// additive shell pushed the cooling rim into the ACES shoulder + bloom (Play A/B at 5 s, L02 day: rim orange 4 % -> 24 % without it).</summary>
        public static float EnvelopeLate = 0.35f, EnvelopeEaseFrom = 2f, EnvelopeEaseTo = 5f;
        public static float EnvelopeK(float t) => Mathf.Lerp(1f, EnvelopeLate, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(EnvelopeEaseFrom, EnvelopeEaseTo, t)));
        /// <summary>nuke_fx_v3b: the explicit white-hot core (shader _CoreRadius, cap object xz) shrinks CoreStart -> CoreEnd (tau
        /// CoreTau s) and adds CoreBoost to the temperature; the white band is (1,.88,.7) x WhiteGain (first real gate run: the
        /// noise alone never reached white after ~1 s, and the warm white x3 tone-mapped to luma 240 &lt; 245).</summary>
        public static float CoreStart = 0.9f, CoreEnd = 0.28f, CoreTau = 2.6f, CoreBoost = 0.45f, WhiteGain = 2.2f;   // v3c: larger, slower-shrinking core (the ramp below lost some near-white)
        /// <summary>v3c: the cap's limb (object xz radius .55-.95) cools by LimbCool in temperature and dims to LimbDim (G falls faster):
        /// a deep-orange rim around the white core; without it the bloom washed the rim to peach (gate: rim orange 1 % at 5 s).</summary>
        public static float LimbCool = 0.2f, LimbDim = 0.5f;
        public static float CoreRadius(float t) => CoreEnd + (CoreStart - CoreEnd) * Mathf.Exp(-t / CoreTau);
        // temperature ramp (linear, OpenZ/Nuke v3; v3c deeper), chosen through URP ACES + the real gate HDR crops
        static readonly Vector4 CWhite = new Vector4(1f, 0.88f, 0.7f, 1f), CHot = new Vector4(1f, 0.13f, 0.014f, 1f), CFire = new Vector4(1f, 0.05f, 0.004f, 1f),
            CDeep = new Vector4(1f, 0.025f, 0.002f, 1f), CEmber = new Vector4(0.6f, 0.018f, 0.002f, 1f),
            CSmoke = new Vector4(0.10f, 0.085f, 0.07f, 1f),   // v3c: G x .5, B x .3 below white (gate HDR crops: the body's G/B + the bloom whitened it under ACES)
            // v3d: fire-lit smoke while the fireball burns (lerped to CSmoke as it dies): the grey smoke's G/B in the cooled rim read peach
            // under ACES + bloom (Play A/B at 5 s, L02 day: rim orange 4 % -> 16 % with this alone, 35-45 % with the envelope ease)
            CSmokeFire = new Vector4(0.09f, 0.045f, 0.02f, 1f);

        /// <summary>The nuke on screen (NukeGlowCheck, OpenZFortShockwave); null when none.</summary>
        public static NukeFx Current { get; private set; }
        public float Age => age;
        public float GlowGain => gain;
        public float HeatNow => heat;
        public bool Burning => active;
        /// <summary>World centre of the fireball (the cap) and its radius (cap, fireball layer or heart, whichever is largest).</summary>
        public Vector3 FireballCentre => cap ? cap.position : transform.position + Vector3.up * 4f;
        public float FireballRadius
        {
            get
            {
                float r = cap ? cap.localScale.x : 1f;                                           // FX_NukeCap is a unit sphere
                if (ball && ball.gameObject.activeSelf) r = Mathf.Max(r, ball.localScale.x * 0.5f);
                if (core && core.gameObject.activeSelf) r = Mathf.Max(r, core.localScale.x * 0.5f);
                return Mathf.Max(1f, r);
            }
        }

        public static float Heat(float t)
        {
            float p = Mathf.Min(t / Duration, 1f);
            return (HeatEnd + (1f - HeatEnd) * Mathf.Exp(-t / HeatTau)) * (1f - 0.45f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.62f, 1f, p)));
        }

        public static float Gain(float t, float night)
        {
            float p = Mathf.Min(t / Duration, 1f);
            float g = GainHold + (GainStart - GainHold) * Mathf.Exp(-t / GainTau);
            g = Mathf.Lerp(g, GainEnd, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1f, p)));
            return g * Mathf.Lerp(1f, NightGainMul, night);
        }

        Transform cap, stem; Material capMat, stemMat; bool mushroom;
        /// <summary></summary>
        public static float MeshLate = 0.3f, StemLate = 0.18f, MeshLateFrom = 1.0f, MeshLateTo = 3.2f, ShellWithMushroom = 0.45f;
        /// <summary>
        /// </summary>
        public static float BallLate = 0.25f, ShellLate = 0.55f, BloomLate = 0.5f;
        static float LateK(float age) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(MeshLateFrom, MeshLateTo, age));
        float age, gain, heat, smokeLight = 0.9f, nextSmokeLight; bool active, loggedContract;
        Transform core, ball, ring; Material coreMat, ballMat, ringMat; Light flashLight; float groundY;
        // nuke_fx_v3: glow envelope around the cap, sustained fire light, heat shimmer quad
        Transform shell, shimmer; Material shellMat, shimmerMat; Light fireLight;
        static Image flash;

        public static NukeFx Trigger(Vector3 at)
        {
            var go = new GameObject("NukeFx");
            var n = go.AddComponent<NukeFx>();
            go.transform.position = at;
            n.Build();
            n.StartCoroutine(n.Sequence(at));
            return n;
        }

        void Build()
        {
            var lib = Resources.Load<GameObject>("OpenZ/FX/combat_fx");
            var shader = Shader.Find("OpenZ/Nuke");
            if (!lib || !shader) { Debug.LogError("NukeFx: combat_fx or OpenZ/Nuke missing"); enabled = false; return; }
            Current = this;
            gain = Gain(0f, NightGrade.NightK); heat = 1f; smokeLight = SceneLight();
            if (GainHold < MinHoldGain || GainEnd < 1.2f)
                Debug.LogError($"[OpenZ] NukeFx glow contract broken: GainHold {GainHold:F2} (min {MinHoldGain}) / GainEnd {GainEnd:F2}: the mushroom will read as matte clay. " +
                               "Fix a white look with HeatTau / HeatEnd / the ramp, not the gain (nuke_fx_v3 README).");
            cap = Part(lib, "FX_NukeCap", shader, 0, out capMat);
            stem = Part(lib, "FX_NukeStem", shader, 1, out stemMat);
            stem.gameObject.SetActive(false);
            BuildGlow();
            mushroom = OpenZNukeMushroom.Spawn(transform.position, groundY) != null;
            active = true; age = 0;
        }

        Transform Part(GameObject lib, string name, Shader shader, int shape, out Material mat)
        {
            MeshFilter src = null;
            foreach (var mf in lib.GetComponentsInChildren<MeshFilter>(true)) if (mf.name == name) { src = mf; break; }
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var m = Instantiate(src.sharedMesh);                                       // geometry in library space (like FxManager)
            var M = lib.transform.worldToLocalMatrix * src.transform.localToWorldMatrix;
            var v = m.vertices; var nrm = m.normals;
            for (int i = 0; i < v.Length; i++) { v[i] = M.MultiplyPoint3x4(v[i]); if (i < nrm.Length) nrm[i] = M.MultiplyVector(nrm[i]).normalized; }
            m.vertices = v; if (nrm.Length == v.Length) m.normals = nrm; else m.RecalculateNormals(); m.RecalculateBounds();
            m.bounds = new Bounds(m.bounds.center, m.bounds.size * 2f);               // displaced vertices must not be culled
            go.AddComponent<MeshFilter>().sharedMesh = m;
            mat = new Material(shader) { name = "NukeFx_" + name + " (runtime)" };
            // nuke_fx_v3: temperature ramp in LINEAR HDR (SetVector: SetColor would gamma-decode, the fx_v2 "pale white ball" bug)
            mat.SetVector("_ColorWhite", CWhite); mat.SetVector("_ColorHot", CHot); mat.SetVector("_ColorFire", CFire);
            mat.SetVector("_ColorDeep", CDeep); mat.SetVector("_ColorEmber", CEmber); mat.SetVector("_ColorSmoke", CSmoke);
            mat.SetFloat("_NoiseTemp", 1.6f); mat.SetFloat("_CentreTemp", shape == 0 ? 0.9f : 0.8f);
            mat.SetFloat("_OccCool", 0.95f); mat.SetFloat("_OccHot", 0.3f); mat.SetFloat("_RimGlow", 0.9f);
            mat.SetFloat("_NoiseScale", shape == 0 ? 1.25f : 1.1f); mat.SetFloat("_Distortion", 0.26f); mat.SetFloat("_ShapeId", shape);
            mat.SetFloat("_GlowGain", gain); mat.SetFloat("_Heat", 1f); mat.SetFloat("_SmokeLight", smokeLight);
            mat.SetFloat("_WhiteGain", WhiteGain); mat.SetFloat("_CoreBoost", CoreBoost); mat.SetFloat("_CoreRadius", CoreRadius(0f));   // v3b
            mat.SetFloat("_LimbCool", shape == 0 ? LimbCool : 0f); mat.SetFloat("_LimbDim", shape == 0 ? LimbDim : 1f);                   // v3c: cap only
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return go.transform;
        }

        void Update()
        {
            if (!active) return;
            age += Time.deltaTime;
            float p = Mathf.Min(age / Duration, 1f);
            float rise = (1 - (1 - p) * (1 - p)) * RiseMax, expand = 1 - Mathf.Pow(1 - p, 4), b = CapBase;
            float flat = Mathf.Lerp(1f, 0.45f, Mathf.Min(p * 4f, 1f));
            cap.localPosition = new Vector3(0, rise, 0);
            cap.localScale = new Vector3(b * expand, b * expand * flat, b * expand);
            cap.localRotation = Quaternion.Euler(0, age * 0.2f * Mathf.Rad2Deg, 0);
            stem.gameObject.SetActive(age > 0.15f);
            stem.localPosition = new Vector3(0, rise * 0.5f, 0);
            stem.localScale = new Vector3(b * expand * StemRadius, Mathf.Max(0.1f, rise), b * expand * StemRadius);
            float fade = 1f - Mathf.SmoothStep(0, 1, (p - 0.68f) / 0.32f);
            // nuke_fx_v3: the fire burns for the whole mushroom (Gain / Heat), white-hot centre fading to orange
            float night = NightGrade.NightK;
            heat = Heat(age); gain = Gain(age, night);
            if (Time.unscaledTime >= nextSmokeLight) { nextSmokeLight = Time.unscaledTime + 0.5f; smokeLight = SceneLight(); }
            // silhouette and the detail, the meshes stay as the glowing interior
            float lateK = LateK(age);
            float meshK = mushroom ? Mathf.Lerp(1f, MeshLate, lateK) : 1f, stemK = mushroom ? Mathf.Lerp(1f, StemLate, lateK) : 1f;   // stem mesh lower still (no thin white-hot line)
            Set(capMat, age * 0.5f, p, 0.96f * fade * meshK, heat); Set(stemMat, age * 0.8f, p, 0.9f * fade * stemK, Mathf.Min(1f, heat + 0.05f));
            float burn = Mathf.Clamp01((gain / Mathf.Lerp(1f, NightGainMul, night) - 1f) / 3f) * fade;
            var smoke = Vector4.Lerp(CSmoke, CSmokeFire, Mathf.Clamp01(burn * 1.5f));                // v3d: fire-lit smoke while burning
            capMat.SetVector("_ColorSmoke", smoke); stemMat.SetVector("_ColorSmoke", smoke);
            float bloomK = mushroom ? Mathf.Lerp(1f, BloomLate, lateK) : 1f;
            OpenZBloomDirector.Sustain(BloomSustain * burn * bloomK * (1f - 0.7f * night), BloomThresholdDrop * burn * bloomK * (1f - night));   // v3c: mostly by day (the night bloom is strong already)
            UpdateGlow();
            UpdateFireLight(fade);
            UpdateShimmer(fade);
            var fc = FireballCentre;                                                            // no ground-distance fog painted over the fireball
            Shader.SetGlobalVector(AtmoHoleId, active ? new Vector4(fc.x, fc.y, fc.z, FireballRadius * 0.9f) : Vector4.zero);
            if (!loggedContract && age >= 3f)
            {
                loggedContract = true;
                Debug.Log($"[OpenZ] nuke glow at +3 s: gain {gain:F2} (hold {GainHold:F2}, min {MinHoldGain}), heat {heat:F2}, bloom sustain {BloomSustain * burn:F2}, " +
                          $"fire light {(fireLight ? fireLight.intensity : 0f):F0}, night {night:F2}");
            }
            if (age >= Duration) { active = false; Shader.SetGlobalVector(AtmoHoleId, Vector4.zero); Destroy(gameObject, 0.1f); }
        }

        static readonly int AtmoHoleId = Shader.PropertyToID("_AtmoHole");

        void OnDestroy()
        {
            if (Current == this) { Current = null; Shader.SetGlobalVector(AtmoHoleId, Vector4.zero); }
            foreach (var m in new[] { capMat, stemMat, coreMat, ballMat, ringMat, shellMat, shimmerMat }) if (m) Destroy(m);
        }

        void Set(Material m, float t, float p, float op, float h)
        {
            m.SetFloat("_NukeTime", t); m.SetFloat("_Progress", p); m.SetFloat("_Opacity", Mathf.Max(0, op));
            m.SetFloat("_GlowGain", gain); m.SetFloat("_Heat", h); m.SetFloat("_SmokeLight", smokeLight);
            m.SetFloat("_CoreRadius", CoreRadius(age)); m.SetFloat("_WhiteGain", WhiteGain); m.SetFloat("_CoreBoost", CoreBoost);   // v3b: shrinking white-hot core
        }

        /// <summary>Smoke brightness 0.12-1 from the sun and the ambient (the smoke is lit; the fire is not).</summary>
        static float SceneLight()
        {
            var sun = RenderSettings.sun;
            float s = sun && sun.isActiveAndEnabled ? sun.intensity * Mathf.Clamp01(-sun.transform.forward.y) : 0f;
            var a = RenderSettings.ambientSkyColor; float amb = 0.2126f * a.r + 0.7152f * a.g + 0.0722f * a.b;
            return Mathf.Clamp(0.2f + amb * 0.8f + s * 0.35f, 0.12f, 1f);
        }

        IEnumerator Sequence(Vector3 at)
        {
            var fx = FxManager.I; var au = CombatAudio.I;
            float born = Time.time;
            if (!StrikeDelivered) au?.PlayWide("nuke", at, 1.2f, "nuke-descent-20260909");   // fx_v2: the strike played it during the fall
            StrikeDelivered = false;
            Flash(1f);
            au?.PlayWide("nuke", at, 1.4f, "legacy-nuke-long");
            // nuke_fx_v3 (fort-fall hitch: the impact frame was 102 ms, NukeFx.Sequence 27 ms + Mono JIT 21 ms, qc/fort_fall_profile_sim.txt):
            // the heavy first-frame work is spread over the next frames (the fireball is on screen from Build; OpenZNukePrewarm JITs it all at load)
            yield return null;
            fx?.NukeBlast(at);
            yield return null;
            OpenZFxPlayer.I?.Blast(at + Vector3.up * 2f, 0f, FxBlastKind.Nuke);
            yield return null;
            OpenZFortShockwave.Fire(at, born);                                                  // nuke_fx_v3: ground ring, refraction wave, dust wall, shove, shake, boom (timed from the blast)
            StartCoroutine(Rumble(at));
            StartCoroutine(Skirt(at));
            yield return null;
            // Zod fort death (bfort.cpp:419-481): 12-17 side explosions in the fort box, debris flying out for 3-5 s
            int n = 12 + Random.Range(0, 6);
            for (int i = 0; i < n && fx; i++)
            {
                var p = at + new Vector3(Random.Range(-9f, 9f), 0, Random.Range(-9f, 9f));
                p.y = ZGame.I ? ZGame.I.GroundY(p.x, p.z) + Random.Range(1f, 5f) : at.y + 2f;
                fx.Explosion(p, 1.3f);
                if (i % 3 == 0 && !BuildingBreaker.RuinV1) fx.Breakup(p, "metal", 4f);   // endings_v1: the fort's own chunks + big parts (grey chip confetti gone)
                if (i % 4 == 0) au?.Play("fortboom", p, 0.8f, 0.15f, -1, "explosion-v1-20260909", "explosion-v2-20260909", "explosion-v3-20260909", "v3-collapse-large-a");
                yield return new WaitForSeconds(Random.Range(0.12f, 0.3f));
            }
        }

        void BuildGlow()
        {
            var add = Shader.Find("OpenZ/FxAdditive");
            groundY = ZGame.I ? ZGame.I.GroundY(transform.position.x, transform.position.z) : transform.position.y;
            var sphere = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
            if (add && sphere)
            {
                core = Glow("NukeCore", sphere, add, out coreMat); Mode(coreMat, 8, 0.3f, 0.12f);          // mesh heart: hot centre (fx_v2: core mix .3)
                coreMat.SetVector("_CoreColor", new Vector4(1f, 0.7f, 0.32f, 1f));                        // fx_v2: linear warm-yellow heart, not white
                ball = Glow("NukeFireball", sphere, add, out ballMat); Mode(ballMat, 8, 0.3f, 0.15f);
                ring = Glow("NukeShockRing", RingMesh(64, 0.8f), add, out ringMat); Mode(ringMat, 4, 0f, 0f);   // world band
                ringMat.SetFloat("_BandInner", 0.93f); ringMat.SetFloat("_BandSides", 0f);
                ring.position = new Vector3(transform.position.x, groundY + 0.35f, transform.position.z);
                shell = Glow("NukeGlowShell", sphere, add, out shellMat); Mode(shellMat, 8, 0f, 0.35f);   // nuke_fx_v3: additive envelope, no white mix
            }
            var lf = new GameObject("NukeFireLight"); lf.transform.SetParent(transform, false);
            fireLight = lf.AddComponent<Light>(); fireLight.type = LightType.Point; fireLight.range = FireLightRange; fireLight.shadows = LightShadows.None;
            fireLight.renderMode = LightRenderMode.ForcePixel; fireLight.color = new Color(1f, 0.5f, 0.2f); fireLight.intensity = 0f;
            var sm = Resources.Load<Material>("OpenZ/FX/M_HeatDistort");
            if (sm && sm.shader && sm.shader.isSupported)
            {
                shimmerMat = new Material(sm) { name = "NukeShimmer (runtime)" };
                shimmerMat.SetFloat("_Mode", 0f); shimmerMat.SetFloat("_Inner", 0.12f); shimmerMat.SetFloat("_NoiseScale", 7f); shimmerMat.SetFloat("_Speed", 2.6f);
                var sg = new GameObject("NukeShimmer"); sg.transform.SetParent(transform, false);
                sg.AddComponent<MeshFilter>().sharedMesh = OpenZFortShockwave.Quad();
                var sr = sg.AddComponent<MeshRenderer>(); sr.sharedMaterial = shimmerMat; sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; sr.receiveShadows = false;
                shimmer = sg.transform;
            }
            var lg = new GameObject("NukeFlashLight"); lg.transform.SetParent(transform, false); lg.transform.localPosition = Vector3.up * 14f;
            flashLight = lg.AddComponent<Light>(); flashLight.type = LightType.Point; flashLight.range = 170f; flashLight.shadows = LightShadows.None;
            flashLight.renderMode = LightRenderMode.ForcePixel; flashLight.intensity = 0f;
            UpdateGlow();
        }

        Transform Glow(string name, Mesh mesh, Shader add, out Material mat)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            mat = new Material(add); mat.renderQueue = 3100;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return go.transform;
        }

        static void Mode(Material m, int shape, float coreMix, float rim)
        {
            m.SetFloat("_Shape", shape); m.SetFloat("_UseVertexColor", 0f); m.SetFloat("_Intensity", 1f); m.SetFloat("_Opaqueness", 0f);
            m.SetColor("_CoreColor", new Color(1f, 0.97f, 0.9f)); m.SetFloat("_CoreMix", coreMix); m.SetFloat("_RimGain", rim); m.SetFloat("_RingAlpha", 1f);
        }

        static Color Hdr(Color c, float gain) => new Color(c.r * gain, c.g * gain, c.b * gain, 1f);

        /// <summary>Timeline (s): core white-hot 0-0.35 grows to 22 m, cools to orange and fades by 1.8; fireball rides the cap
        /// for 3 s; shock ring runs out to 95 m in 1.8 s; the light flashes the whole area (170 m) and flickers down by 4 s.</summary>
        void UpdateGlow()
        {
            float t = age;
            var white = new Color(1f, 0.88f, 0.55f); var orange = new Color(1f, 0.45f, 0.12f);          // ACES clips pure white to a flat plate
            // fx_v2: the glow layers take LINEAR colours (SetVector). SetColor gamma-decoded Hdr(white, 2.2) into (5.7, 4.3, 1.5): the 22 m
            // bright warm shock ring that blooms, and a flash light strong enough to light the whole base.
            var heartHot = new Vector4(1f, 0.85f, 0.62f, 1f); var heartCool = new Vector4(1f, 0.2f, 0.02f, 1f);   // v3c: white-hot heart (the gate's 0.6 s dip)
            if (core)
            {
                float k = Mathf.Clamp01(t / 0.35f), sz = Mathf.Lerp(3f, 13f, 1 - (1 - k) * (1 - k));
                core.localPosition = Vector3.up * (4f + t * 3f); core.localScale = Vector3.one * sz;
                // v3c: stays white-hot through the gap between the flash and the cap's own core (was orange by 0.6 s: core 31 % < 50 %)
                float heat = Mathf.Clamp01(1f - t / 2.2f), g = 7f * heat * Mathf.Sqrt(heat);
                coreMat.SetVector("_Color", Vector4.Lerp(heartCool, heartHot, Mathf.Clamp01(1.5f - t)) * g); core.gameObject.SetActive(g > 0.03f);
            }
            float fadeAll = 1f - Mathf.SmoothStep(0, 1, (Mathf.Min(t / Duration, 1f) - 0.68f) / 0.32f);
            if (ball)
            {
                float p = Mathf.Min(t / Duration, 1f), rise = (1 - (1 - p) * (1 - p)) * RiseMax;
                ball.localPosition = Vector3.up * Mathf.Max(3f, rise * 0.9f);
                ball.localScale = new Vector3(1f, 0.7f, 1f) * (6f + 10f * Mathf.Clamp01(t / 1.2f));
                // nuke_fx_v3: the fireball layer keeps glowing (was gone at 3.2 s) and loses its white mix after the flash
                float g = 3.2f * Mathf.Exp(-t / 2.6f) * Mathf.Clamp01(t / 0.2f) * fadeAll * (mushroom ? Mathf.Lerp(1f, BallLate, LateK(t)) : 1f);
                ballMat.SetFloat("_CoreMix", Mathf.Lerp(0.3f, 0.05f, Mathf.Clamp01(t / 1.5f)));
                ballMat.SetVector("_Color", new Vector4(1f, 0.16f, 0.01f, 1f) * g); ball.gameObject.SetActive(g > 0.03f);
            }
            if (shell && cap)
            {   // nuke_fx_v3: additive glow envelope hugging the cap (built-in sphere radius .5 -> x2), orange, the whole life
                shell.localPosition = cap.localPosition; shell.localRotation = cap.localRotation;
                shell.localScale = cap.localScale * (2f * 1.12f);
                float g = gain * EnvelopeGain * EnvelopeK(t) * fadeAll * (mushroom ? ShellWithMushroom * Mathf.Lerp(1f, ShellLate, LateK(t)) : 1f);
                shellMat.SetVector("_Color", CFire * g); shell.gameObject.SetActive(g > 0.03f && cap.localScale.x > 0.3f);
            }
            if (ring && OpenZFortShockwave.Enabled) ring.gameObject.SetActive(false);         // nuke_fx_v3: the shockwave draws a ground-hugging ring
            else if (ring)
            {
                float k = Mathf.Clamp01(t / 1.8f), r = Mathf.Lerp(4f, 95f, 1 - Mathf.Pow(1 - k, 3));
                ring.localScale = new Vector3(r, 1f, r);
                ringMat.SetVector("_RingCenter", ring.position); ringMat.SetFloat("_RingRadius", r);
                float g = 3.0f * (1f - k) * (1f - k) * Mathf.Clamp01(t / 0.08f);
                ringMat.SetVector("_Color", new Vector4(1f, 0.52f, 0.2f, 1f) * g); ring.gameObject.SetActive(g > 0.03f);
            }
            if (flashLight)
            {
                // v3d: slower tail at night (decay 1.5 -> 1.05): the L17 lamps lift the ground baseline, so the blast light must last
                // (Play A/B 3.6 s L17 night: the fire light adds only ~5 % to the ground ring 1.6-3 R, the flash from 14 m lights it best)
                float peak = t < 0.06f ? t / 0.06f : Mathf.Exp(-(t - 0.06f) * 1.5f * (1f - FlashNightSlow * NightGrade.NightK));
                float flick = 1f + 0.18f * Mathf.Sin(t * 37f) * Mathf.Sin(t * 23f + 1.3f);
                if (OzMotion.Reduced) { peak = 0.55f * (t < 0.7f ? Mathf.SmoothStep(0f, 1f, t / 0.7f) : Mathf.Exp(-(t - 0.7f) * 0.9f)); flick = 1f; }
                flashLight.intensity = 380f * peak * flick;                                           // fx_v2: 380 (was 70: E = 70 / 14^2 under it)
                flashLight.color = Color.Lerp(new Color(1f, 0.58f, 0.3f), white, Mathf.Clamp01(1.2f - t));
                flashLight.enabled = flashLight.intensity > 0.05f;
            }
        }

        /// <summary>nuke_fx_v3: the fire keeps lighting the ground and the fort around it while it burns (after the flash).</summary>
        void UpdateFireLight(float fade)
        {
            if (!fireLight) return;
            fireLight.transform.position = FireballCentre + Vector3.down * 3f;
            float rise = Mathf.Clamp01(age / 0.25f);
            float flick = 1f + 0.12f * Mathf.Sin(age * 29f) * Mathf.Sin(age * 17f + 0.7f);
            fireLight.intensity = FireLightPeak * (1f + FireLightNightBoost * NightGrade.NightK) * rise * Mathf.Clamp01(Gain(age, 0f) / Mathf.Max(0.1f, GainHold)) * fade * flick;   // v3c: not dimmed by the night gain; v3d: night boost
            fireLight.color = Color.Lerp(new Color(1f, 0.42f, 0.14f), new Color(1f, 0.62f, 0.32f), Mathf.Clamp01(heat));
            fireLight.enabled = fireLight.intensity > 0.05f;
        }

        /// <summary>nuke_fx_v3: heat shimmer: a camera-facing distortion quad over the fireball (3.2 x its radius).</summary>
        void UpdateShimmer(float fade)
        {
            if (!shimmer) return;
            var c = Camera.main; if (!c) return;
            float r = FireballRadius;
            shimmer.position = FireballCentre + Vector3.up * (r * 0.35f);
            shimmer.rotation = Quaternion.LookRotation(shimmer.position - c.transform.position, c.transform.up);
            shimmer.localScale = Vector3.one * (r * 3.2f);
            float s = ShimmerStrength * Mathf.Clamp01(age / 0.3f) * (0.45f + 0.55f * Mathf.Clamp01(heat)) * fade;
            shimmerMat.SetFloat("_Strength", s); shimmer.gameObject.SetActive(s > 1e-4f);
        }

        /// <summary>Flat annulus (unit outer radius) in XZ; vertex alpha soft on both edges.</summary>
        static Mesh RingMesh(int seg, float inner)
        {
            var v = new System.Collections.Generic.List<Vector3>(); var c = new System.Collections.Generic.List<Color>(); var tri = new System.Collections.Generic.List<int>();
            float[] radii = { inner, (inner + 1f) * 0.5f, 1f }; float[] alpha = { 0f, 1f, 0f };
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                for (int j = 0; j < 3; j++) { v.Add(new Vector3(Mathf.Cos(a) * radii[j], 0, Mathf.Sin(a) * radii[j])); c.Add(new Color(1, 1, 1, alpha[j])); }
            }
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < 2; j++)
                {
                    int a0 = i * 3 + j, a1 = a0 + 1, b0 = a0 + 3, b1 = b0 + 1;
                    tri.AddRange(new[] { a0, b0, a1, a1, b0, b1 });
                }
            var m = new Mesh { name = "NukeRing" }; m.SetVertices(v); m.SetColors(c); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
            m.bounds = new Bounds(Vector3.zero, new Vector3(2.2f, 1f, 2.2f));
            return m;
        }

        /// <summary>Delayed low rumble rolling in after the blast (the collapse bank, pitched down).</summary>
        IEnumerator Rumble(Vector3 at)
        {
            yield return new WaitForSeconds(1.1f);
            CombatAudio.I?.PlayPitched("collapse", at, 1.2f, 0f, -1, 0.55f, "v3-collapse-large-a");
            yield return new WaitForSeconds(1.6f);
            CombatAudio.I?.PlayPitched("collapse", at, 0.9f, 0f, -1, 0.5f, "v3-collapse-large-a");
        }

        /// <summary>The base surge: rolling dust spreading out around the stem for the first ~3 s.</summary>
        IEnumerator Skirt(Vector3 at)
        {
            var fx = FxManager.I; if (!fx) yield break;
            for (int i = 0; i < 10; i++)
            {   // dust wall rolling out behind the shock ring
                float r = 6f + i * 5f;
                if (!OzBlastFog.Skirt(at, r, i)) fx.DemolitionDust(at, r * 2f, r * 2f, 1.5f + i * 0.18f);   // item 211: a ring of soft ground fog (OzBlastFog.cs); off = the old puffs
                yield return new WaitForSeconds(0.3f);
            }
        }

        /// <summary>nuke_fx_v3: level-load warm-up (OpenZNukePrewarm): the flash overlay canvas is built now (alpha 0), not in the impact frame.</summary>
        public static void Prewarm() => Flash(0f);

        /// <summary>Blinding white flash (full-screen overlay fading over ~0.9 s).</summary>
        static void Flash(float strength)
        {
            if (!flash)
            {
                var cgo = new GameObject("NukeFlash"); var c = cgo.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 500;
                var ig = new GameObject("White"); ig.transform.SetParent(cgo.transform, false);
                flash = ig.AddComponent<Image>(); flash.raycastTarget = false;
                var rt = flash.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
                cgo.AddComponent<FlashFader>();
            }
            if (OzMotion.Reduced) { calmTarget = Mathf.Max(calmTarget, 0.14f * strength); OzCalm.Note("nuke white-out"); return; }
            flash.color = new Color(1f, 0.9f, 0.72f, 0.38f * strength);  // fx_v2: warmer, lighter (OpenZBloomDirector's exposure flash carries the blinding part)
        }
        static float calmTarget;

        class FlashFader : MonoBehaviour
        {
            void Update()
            {
                if (!flash) return; var c = flash.color;
                if (calmTarget > 0f) { c = new Color(1f, 0.9f, 0.72f, Mathf.MoveTowards(c.a, calmTarget, Time.unscaledDeltaTime * 0.25f)); if (c.a >= calmTarget - 0.001f) calmTarget = 0f; flash.color = c; return; }   // calm: ~0.6 s rise
                c.a = Mathf.Max(0, c.a - (OzMotion.Reduced ? Time.deltaTime * 0.12f : Time.unscaledDeltaTime * 1.9f)); flash.color = c;
            }
        }
    }
}
