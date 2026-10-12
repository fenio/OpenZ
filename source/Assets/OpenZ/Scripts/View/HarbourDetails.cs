using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// View-side decor on L17 (no collider): the
    /// fixtures and the two channel wrecks have no Zod object; the three harbour wrecks are drawn for their sim objects
    ///   * Sewage outfalls set into the sloped concrete quay embankments: dirty water runs out of a rusty pipe mouth, down the
    ///     slope and into the harbour / canal: a flowing ribbon, a splash, a foam ring, a boiling foam patch and a little mist,
    ///     a wet stain on the concrete and a pouring loop. The rim stands <= 6 cm proud (units walk down that slope into the water).
    ///   * Lit floor grates on the quays (a drain for every outfall + two more): a faint amber glow from the service tunnel
    ///     below, stronger at night, with warm vapour catching the light; at night each grate (and a peeking hatch) offers a
    ///     small amber point light to the same night budget, with the painted ground pool when no real light is free.
    ///   * A line of small red floor lights along the harbour edge (north quay, pier edges, the canal through the pier): flush
    ///     fixtures that blink gently in a slow travelling wave at night; every light is a real URP point light offered to the
    ///     night package's light budget (NightBudgetShare) and lights the ground a little; the ones the budget does not grant
    ///     keep a NightPool ground-pool impostor + halo, exactly like the lamps.
    ///   * Maintenance hatches: now and then one lifts a crack (light from below, a puff, a clunk).
    ///   * Old car wrecks half submerged: three in the open harbour water (HW*, each bound to its DV39 sim object: 1667 HP,
    ///     explosives only, its 2 x 1 tiles blocked like a rock until it is destroyed) and two in the channels that run out to
    ///     the sea past the rock band (W*, off the tile map, view only). Faded toy paint with one or two clean rust patches and
    ///     a waterline band. Water laps around them; they creak now and then.
    /// Everything is destructible: an explosion within its reach (PropBreakables rule 1.2 + 2.4 x strength) breaks a fixture
    /// with chunks, sparks, glass and splashes and the matching sounds. A harbour wreck shows its sim hit points (a panel flies
    /// off at 2/3 and 1/3, sparks and a clang on every hit) and breaks apart when the sim destroys it (CombatView hands the
    /// Death over: <see cref="SimWreckDied"/>); a channel wreck takes 2-3 blasts. Broken: loose parts fly, the hull sinks with
    /// bubbles and a tyre floats a while. Debris that lands eases flat over 0.3 s (never rests on an edge).
    /// Placement: HarbourDetailsData.cs (tools/plan_l17.py + data/L17_details.json, README_IMPORT.md). Boots itself in L17.
    /// </summary>
    [DefaultExecutionOrder(73)]
    public class HarbourDetails : MonoBehaviour
    {
        public static HarbourDetails I { get; private set; }
        public static bool AutoBoot = true;

        [Header("Parts")]
        public bool floorLights = true, outfalls = true, grates = true, hatches = true, wrecks = true;
        [Tooltip("Rust overlay on the car wrecks (0 = the old clean toy paint) and how deep they sit: the share of the car's height under the water (0.55 = the waterline at about mid-door)")]
        [Range(0f, 1f)] public float wreckRust = 1f;
        [Range(0.2f, 0.65f)] public float wreckSubmerge = 0.55f;
        [Header("Red floor lights")]
        public Color lightColor = new Color(1f, 0.1f, 0.06f);
        [Tooltip("real point light: intensity, range (m), height over the lens (m), budget priority (lamps 2, searchlights 5)")]
        public float lightIntensity = 0.9f, lightRange = 2.8f, lightHeight = 0.45f, lightPriority = 1.5f;
        [Tooltip("gentle blink: period (s), phase step per light along its run (rad), depth (0 steady .. 1 off at the trough)")]
        public float blinkPeriod = 3.4f, blinkPhaseStep = 0.6f, blinkDepth = 0.45f;
        public float poolGain = 0.32f, haloGain = 0.4f, haloSize = 0.3f, lensDay = 0.05f, lensNight = 2.4f;
        [Header("Grates / hatches")]
        public Color grateGlow = new Color(1f, 0.55f, 0.2f);
        public float grateDay = 0.07f, grateNight = 1.2f;
        public Vector2 hatchPeekEvery = new Vector2(45f, 100f);
        [Tooltip("small amber point light of a grate / a peeking hatch at night (night budget; ground pool when not granted): intensity, range (m), height (m), budget priority, pool gain")]
        public float grateLightIntensity = 0.8f, grateLightRange = 3.2f, grateLightHeight = 0.35f, grateLightPriority = 1.4f, gratePoolGain = 0.38f;
        [Header("Outfalls")]
        public float flowSpeed = 1.3f;
        [Tooltip("outfall model scale (the kit pipe is 0.47 m inside: 1.25 = a 0.6 m sewer mouth)")] public float outfallScale = 1.25f;
        public bool mist = true;
        public Color water = new Color(0.36f, 0.33f, 0.2f), foam = new Color(0.72f, 0.7f, 0.58f);
        [Header("Misc")]
        public bool playSounds = true, verbose;
        public int seed = 1996;

        ZGame game; Camera cam; RtsCamera rts; float waterY, clock; bool subscribed; System.Random rng;
        readonly DetailKit.Chunks chunks = new DetailKit.Chunks();
        DetailKit.Bits bits;
        Transform root;
        Material toyMetal, toyConcrete, toyWreck, fxRibbon, fxStain, fxFoam;
        Mesh rimMesh, lensMesh; Matrix4x4 rimLocal, lensLocal;
        const byte BRed = 0, BGlass = 1, BRust = 2, BConcrete = 3;
        static readonly int ColorId = Shader.PropertyToID("_Color"), EmissionId = Shader.PropertyToID("_EmissionColor");

        // ------------------------------------------------------------------ boot
        static bool hooked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!hooked) { hooked = true; SceneManager.sceneLoaded += (s, m) => TryAttach(); }
            TryAttach();
        }

        public static void TryAttach()
        {
            if (!AutoBoot || !ZGame.I || ZGame.I.level != "L17" || FindFirstObjectByType<HarbourDetails>()) return;
            new GameObject("HarbourDetails").AddComponent<HarbourDetails>();
        }

        void Awake() { I = this; }

        void Start()
        {
            game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            if (!game || game.World == null || game.level != "L17") { enabled = false; return; }
            cam = Camera.main; rts = cam ? cam.GetComponent<RtsCamera>() : FindFirstObjectByType<RtsCamera>();
            rng = new System.Random(seed);
            waterY = DetailKit.WaterY("L17");
            root = new GameObject("L17Details").transform;
            toyMetal = DetailKit.ToyMat("L17D_Metal", 0.35f, 0.25f, 2f);
            toyConcrete = DetailKit.ToyMat("L17D_Concrete", 0.15f, 0f, 0f);
            toyWreck = DetailKit.ToyMat("L17D_Wreck", 0.22f, 0.1f, 2f);
            // overlay (OpenZRust.hlsl): faded chipped paint, orange-brown rust patches with bare-metal rims, streaks down the sides,
            // more rust low on the body and on the roof, darker grime over the waterline (world y vs the harbour water)
            if (toyWreck) { toyWreck.SetFloat("_Rust", wreckRust); toyWreck.SetFloat("_RustWaterY", waterY); toyWreck.SetFloat("_RustScale", 1f); }
            bits = new DetailKit.Bits(new[] { new Color(0.9f, 0.08f, 0.05f), new Color(0.85f, 0.9f, 0.95f), new Color(0.5f, 0.24f, 0.1f), new Color(0.62f, 0.61f, 0.57f) });
            if (floorLights) BuildLights();
            if (outfalls) BuildOutfalls();
            if (grates) BuildGrates();
            if (hatches) BuildHatches();
            if (wrecks) BuildWrecks();
            FxManager.Exploded += OnExplosion; subscribed = true;
            int simW = 0; foreach (var w in wks) if (w.sim != null) simW++;
            Debug.Log($"[OpenZ] HarbourDetails L17: {lights.Count} red floor lights, {outs.Count} outfalls, {grs.Count} grates, {hts.Count} hatches, " +
                      $"{wks.Count} wrecks ({simW} in the harbour on sim objects, DV39 {(ZWorld.HarbourWrecksDeviation ? "on" : "off")}; water y {waterY:F2}); " +
                      $"real lights via {(NightLights.I ? "the night budget" : "own budget")}");
        }

        void OnDestroy()
        {
            if (subscribed) FxManager.Exploded -= OnExplosion;
            foreach (var f in myFix) NightBudgetShare.Fixtures.Remove(f);
            foreach (var o in outs) if (o.loop) Destroy(o.loop.gameObject);
            chunks.Clear();
            if (root) Destroy(root.gameObject);
            foreach (var o in outs) { if (o.ribbon) Destroy(o.ribbon); if (o.stain) Destroy(o.stain); if (o.foamM) Destroy(o.foamM); }
            DetailKit.Cleanup();                                                                  // run-time materials / meshes of this level
            if (I == this) I = null;
        }

        // ================================================================== red floor lights
        sealed class FLight
        {
            public Vector3 pos; public Quaternion rot; public float phase; public bool broken; public int group;
            public NightFixture fix; public Light light; public MaterialPropertyBlock halo = new MaterialPropertyBlock(), pool = new MaterialPropertyBlock();
            public float haloShown = -1f, poolShown = -1f, k;
        }
        readonly List<FLight> lights = new List<FLight>();
        const int Groups = 8;
        Material[] groupMats; Matrix4x4[] rimM; Matrix4x4[][] lensM; int[] lensN; int rimN; bool instDirty = true;
        Bounds lightBounds; bool ownMode; readonly List<NightFixture> myFix = new List<NightFixture>();

        void BuildLights()
        {
            var tpl = DetailKit.Model("L17D_FloorLight", null);
            if (!tpl) return;
            var rim = DetailKit.Find(tpl.transform, "Rim"); var lens = DetailKit.Find(tpl.transform, "Lens");
            if (!rim || !lens) { Destroy(tpl); return; }
            rimMesh = rim.GetComponent<MeshFilter>().sharedMesh; lensMesh = lens.GetComponent<MeshFilter>().sharedMesh;
            rimLocal = tpl.transform.worldToLocalMatrix * rim.localToWorldMatrix; lensLocal = tpl.transform.worldToLocalMatrix * lens.localToWorldMatrix;
            Destroy(tpl);
            groupMats = new Material[Groups];
            for (int g = 0; g < Groups; g++) { groupMats[g] = DetailKit.ToyMat("L17D_Lens" + g, 0.8f, 0f, 2f); }
            var d = HarbourDetailsData.Lights; var ph = HarbourDetailsData.LightPhase;
            lightBounds = new Bounds(new Vector3(d[0].x, 0, d[0].y), Vector3.one);
            for (int i = 0; i < d.Length; i++)
            {
                var p = new Vector3(d[i].x, 0f, d[i].y);
                p.y = DetailKit.TerrainMax(game, p, 0.11f) - 0.003f;                                  // rim sunk 3 mm, lens top +3 cm
                var n = new Vector3(d[i].z, 0f, d[i].w);
                var l = new FLight { pos = p, rot = Quaternion.LookRotation(n, Vector3.up), phase = ph[i] * blinkPhaseStep };
                float f = Mathf.Repeat(l.phase / (2f * Mathf.PI), 1f); l.group = Mathf.Min(Groups - 1, (int)(f * Groups));
                var lgo = new GameObject("FloorLight_" + i); lgo.transform.SetParent(root, false);
                lgo.transform.position = p + Vector3.up * lightHeight;
                var lt = lgo.AddComponent<Light>();
                lt.type = LightType.Point; lt.range = lightRange; lt.color = lightColor; lt.intensity = 0f; lt.enabled = false;
                lt.shadows = LightShadows.None; lt.renderMode = LightRenderMode.ForcePixel;
                l.light = lt;
                l.fix = new NightFixture
                {
                    Kind = NightKind.Uplight, Id = "l17d_floor_" + i, Light = lt, Col = lightColor, ColLinear = lightColor.linear,
                    Intensity = lightIntensity, Range = lightRange, Priority = lightPriority, ShadowCandidate = false,
                };
                NightBudgetShare.Fixtures.Add(l.fix); myFix.Add(l.fix);
                lights.Add(l);
                lightBounds.Encapsulate(p);
            }
            lightBounds.Expand(new Vector3(2f, 2f, 2f));
            rimM = new Matrix4x4[lights.Count]; lensM = new Matrix4x4[Groups][]; lensN = new int[Groups];
            for (int g = 0; g < Groups; g++) lensM[g] = new Matrix4x4[lights.Count];
        }

        void RebuildInstances()
        {
            instDirty = false; rimN = 0; for (int g = 0; g < Groups; g++) lensN[g] = 0;
            foreach (var l in lights)
            {
                var M = Matrix4x4.TRS(l.pos, l.rot, Vector3.one);
                rimM[rimN++] = M * rimLocal;
                if (!l.broken) lensM[l.group][lensN[l.group]++] = M * lensLocal;
            }
        }

        void StepLights(float now, float dt)
        {
            if (lights.Count == 0) return;
            float night = DetailKit.Night;
            float on = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.4f, night));
            for (int i = 0; i < lights.Count; i++)
            {
                var l = lights[i];
                float b = 1f - blinkDepth * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * now / blinkPeriod - l.phase));
                l.k = l.broken ? 0f : on * b;
                l.fix.Priority = lightPriority; l.fix.Intensity = lightIntensity; l.fix.Range = lightRange;
                l.fix.LightOut = l.k;
            }
            // lens emission per phase group
            for (int g = 0; g < Groups; g++)
            {
                float phase = (g + 0.5f) / Groups * 2f * Mathf.PI;
                float b = 1f - blinkDepth * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * now / blinkPeriod - phase));
                float e = Mathf.Lerp(lensDay, lensNight * b, on);
                var lc = lightColor.linear * e; groupMats[g].SetVector(EmissionId, new Vector4(lc.r, lc.g, lc.b, 1f));   // raw linear HDR (SetColor would re-convert)
            }
        }

        void DrawLights()
        {
            if (lights.Count == 0 || rimMesh == null) return;
            if (instDirty) RebuildInstances();
            var rp = new RenderParams(toyMetal) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true, worldBounds = lightBounds, layer = 0 };
            if (rimN > 0) Graphics.RenderMeshInstanced(rp, rimMesh, 0, rimM, rimN);
            for (int g = 0; g < Groups; g++)
            {
                if (lensN[g] == 0) continue;
                var rl = new RenderParams(groupMats[g]) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, worldBounds = lightBounds };
                Graphics.RenderMeshInstanced(rl, lensMesh, 0, lensM[g], lensN[g]);
            }
            if (DetailKit.Night < 0.03f) return;
            var haloMat = NightMeshes.Halo(); var poolMat = NightMeshes.Pool(); var quad = NightMeshes.Quad(); var cube = NightMeshes.Cube();
            var lin = lightColor.linear;
            for (int i = 0; i < lights.Count; i++)
            {
                var l = lights[i]; if (l.k <= 0.002f) continue;
                if (!DetailKit.OnScreen(cam, l.pos, 0.05f)) continue;
                float realK = l.fix != null ? l.fix.RealK : 0f;
                var hc = lin * (haloGain * l.k);
                l.halo.SetColor(ColorId, hc);
                Graphics.RenderMesh(new RenderParams(haloMat) { matProps = l.halo, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false },
                                    quad, 0, Matrix4x4.TRS(l.pos + Vector3.up * 0.06f, Quaternion.identity, Vector3.one * haloSize));
                float pk = poolGain * l.k * (1f - realK);
                if (pk > 0.004f)
                {
                    l.pool.SetColor(ColorId, lin * pk);
                    Graphics.RenderMesh(new RenderParams(poolMat) { matProps = l.pool, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false },
                                        cube, 0, Matrix4x4.TRS(l.pos + Vector3.up * 0.35f, l.rot, new Vector3(2.2f, 1.3f, 2.2f)));
                }
            }
        }

        void LateUpdate()
        {
            // REVIEW-FIX: the floor lights, grates and hatches all hold budget fixtures; never return early just because one
            // kind is missing (floor lights off / not built) - the grate and hatch lights must still be driven
            if (!game || myFix.Count == 0) return;
            // the night package ranks our fixtures (NightBudgetShare, merged in NightLightBudget.Tick); without it (package not
            // imported) a small own ranking: the 8 burning fixtures that score best near the camera focus
            bool hookOk = NightBudgetShare.HookFrame >= Time.frameCount - 2;
            if (!ownMode && !hookOk && Time.frameCount > 10 && !NightLights.I)
            {
                ownMode = true; foreach (var f in myFix) NightBudgetShare.Fixtures.Remove(f);
                Debug.Log("[OpenZ] HarbourDetails: no night budget hook, floor lights, grates and hatches use their own 8-light budget");
            }
            if (ownMode) OwnBudget(Time.time, Time.deltaTime);
            for (int i = 0; i < lights.Count; i++)
            {
                var l = lights[i];
                float k = l.fix.LightOut * l.fix.RealK;
                bool lit = k > 0.002f;
                if (l.light.enabled != lit) l.light.enabled = lit;
                if (lit) l.light.intensity = lightIntensity * k;
            }
            for (int i = 0; i < amber.Count; i++)
            {
                var a = amber[i];
                float k = a.fix.LightOut * a.fix.RealK;
                bool lit = k > 0.002f;
                if (a.light.enabled != lit) a.light.enabled = lit;
                if (lit) { a.light.intensity = grateLightIntensity * k; a.light.range = grateLightRange; }
            }
        }

        float ownNext;
        void OwnBudget(float now, float dt)
        {
            foreach (var f in myFix) f.RealK = Mathf.MoveTowards(f.RealK, f.Budgeted && f.LightOut > 0.001f ? 1f : 0f, dt / 0.35f);
            if (now < ownNext) return;
            ownNext = now + 0.2f;
            var focus = rts ? rts.focus : (cam ? cam.transform.position : Vector3.zero);
            foreach (var f in myFix) { f.Budgeted = false; f.Score = -1f; }
            for (int n = 0; n < 8; n++)
            {
                NightFixture best = null; float bs = 0f;
                foreach (var f in myFix)
                {
                    if (f.Budgeted || f.LightOut <= 0.02f || !DetailKit.OnScreen(cam, f.LightPos, 0.1f)) continue;
                    float d = Vector2.Distance(new Vector2(f.LightPos.x, f.LightPos.z), new Vector2(focus.x, focus.z));
                    float s = (1f + f.Priority) * f.LightOut / (1f + d * d / 400f);
                    if (s > bs) { bs = s; best = f; }
                }
                if (best == null) break;
                best.Budgeted = true;
            }
        }

        // ================================================================== amber lights (grates, peeking hatches)
        /// <summary>A small amber point light over a grate or a hatch mouth: offered to the night budget like the floor lights;
        /// while no real light is granted the NightPool ground-pool impostor paints its light on the quay.</summary>
        sealed class Amber
        {
            public Vector3 pos; public NightFixture fix; public Light light; public float poolSize;
            public readonly MaterialPropertyBlock pool = new MaterialPropertyBlock();
        }
        readonly List<Amber> amber = new List<Amber>();

        Amber NewAmber(string id, Vector3 groundPos, float poolSize)
        {
            var go = new GameObject("AmberLight_" + id); go.transform.SetParent(root, false);
            go.transform.position = groundPos + Vector3.up * grateLightHeight;
            var lt = go.AddComponent<Light>();
            lt.type = LightType.Point; lt.range = grateLightRange; lt.color = grateGlow; lt.intensity = 0f; lt.enabled = false;
            lt.shadows = LightShadows.None; lt.renderMode = LightRenderMode.ForcePixel;
            var a = new Amber { pos = groundPos, light = lt, poolSize = poolSize };
            a.fix = new NightFixture
            {
                Kind = NightKind.Uplight, Id = "l17d_amber_" + id, Light = lt, Col = grateGlow, ColLinear = grateGlow.linear,
                Intensity = grateLightIntensity, Range = grateLightRange, Priority = grateLightPriority, ShadowCandidate = false,
            };
            if (!ownMode) NightBudgetShare.Fixtures.Add(a.fix);
            myFix.Add(a.fix); amber.Add(a);
            return a;
        }

        /// <summary>Budget offer of an amber light: k = how much it burns (0..1, already night-gated by the caller).</summary>
        void OfferAmber(Amber a, float k)
        {
            if (a == null) return;
            a.fix.LightOut = Mathf.Clamp01(k); a.fix.Intensity = grateLightIntensity; a.fix.Range = grateLightRange; a.fix.Priority = grateLightPriority;
        }

        void DrawAmberPools()
        {
            if (amber.Count == 0 || DetailKit.Night < 0.03f) return;
            var poolMat = NightMeshes.Pool(); var cube = NightMeshes.Cube(); if (!poolMat || !cube) return;
            var lin = grateGlow.linear;
            for (int i = 0; i < amber.Count; i++)
            {
                var a = amber[i];
                float pk = gratePoolGain * a.fix.LightOut * (1f - a.fix.RealK);
                if (pk <= 0.004f || !DetailKit.OnScreen(cam, a.pos, 0.05f)) continue;
                a.pool.SetColor(ColorId, lin * pk);
                Graphics.RenderMesh(new RenderParams(poolMat) { matProps = a.pool, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false },
                                    cube, 0, Matrix4x4.TRS(a.pos + Vector3.up * 0.3f, Quaternion.identity, new Vector3(a.poolSize, 1.3f, a.poolSize)));
            }
        }

        void BreakLight(FLight l, Vector3 from)
        {
            if (l.broken) return;
            l.broken = true; instDirty = true; l.fix.LightOut = 0f;
            var p = l.pos + Vector3.up * 0.03f;
            bits.Burst(p, 6, BRed, 1.6f, 0.035f, rng); bits.Burst(p, 4, BGlass, 1.8f, 0.03f, rng);
            FxManager.I?.Pop(p, 0.35f);
            if (DetailKit.Night > 0.1f) FxManager.I?.AddLight(p + Vector3.up * 0.3f, lightColor, 2f, 3f, 0.2f, 0.6f, true);
            if (playSounds) DetailKit.Sound("ngt_lamp_glass", p, 0.5f, "night_glass", 0.05f, -1, "sd_glass_shatter_a", "sd_glass_shatter_b");
        }

        // ================================================================== outfalls
        sealed class Outfall
        {
            public string id; public Vector3 face, up, down, start, impact; public float len; public GameObject go; public Transform grille;
            public Mesh ribbon, stain, foamM; public Vector3[] rv; public Color[] rc; public List<Vector4> ruv; public float[] rs;
            public float tDrop, tRing, tMist, gush, foamAge; public bool broken; public AudioSource loop; public int idx;
            public Vector4[] fuv = new Vector4[4];
        }
        readonly List<Outfall> outs = new List<Outfall>();
        const int RRows = 18;

        void BuildOutfalls()
        {
            fxRibbon = DetailKit.FxMat("L17D_Pour", 1f, 0.88f, 1f, Color.white);
            fxStain = DetailKit.FxMat("L17D_Stain", 7f, 1f, 0f, Color.white);
            fxFoam = DetailKit.FxMat("L17D_Foam", 2f, 1f, 1f, foam);
            if (fxStain) fxStain.renderQueue = 3001; if (fxFoam) fxFoam.renderQueue = 3003; if (fxRibbon) fxRibbon.renderQueue = 3004;
            if (fxFoam) { fxFoam.SetFloat("_NoiseAmt", 0.55f); fxFoam.SetFloat("_NoiseScale", 2.4f); fxFoam.SetFloat("_Boil", 0.9f); fxFoam.SetFloat("_Spin", 0.5f); fxFoam.SetFloat("_EdgeSoft", 0.3f); fxFoam.SetFloat("_Bump", 0.4f); }
            var d = HarbourDetailsData.Outfalls;
            for (int i = 0; i < d.Length; i++)
            {
                var o = new Outfall { id = HarbourDetailsData.OutfallIds[i], idx = i };
                var n = new Vector3(d[i].z, 0f, d[i].w);                                               // outward (towards the water)
                var p = new Vector3(d[i].x, 0f, d[i].y);
                p.y = game.TerrainY(p.x, p.z);
                float y1 = game.TerrainY(p.x - n.x * 0.3f, p.z - n.z * 0.3f), y2 = game.TerrainY(p.x + n.x * 0.3f, p.z + n.z * 0.3f);
                float slope = Mathf.Clamp((y1 - y2) / 0.6f, 0.4f, 2.2f);                               // rise per m inland
                o.up = new Vector3(n.x * slope, 1f, n.z * slope).normalized;
                o.down = Vector3.ProjectOnPlane(Vector3.down, o.up).normalized;
                o.face = p;
                o.go = DetailKit.Model("L17D_Outfall", root);
                if (o.go)
                {
                    o.go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(o.down, o.up));
                    o.go.transform.localScale = Vector3.one * outfallScale;
                    DetailKit.SetMat(o.go, toyConcrete, true);
                    foreach (var nm in new[] { "Pipe", "Grille", "Bore" })
                    {
                        var t = DetailKit.Find(o.go.transform, nm);
                        if (t) foreach (var r in t.GetComponentsInChildren<Renderer>()) r.sharedMaterial = toyMetal;
                    }
                    o.grille = DetailKit.Find(o.go.transform, "Grille");
                }
                // the flow: from the lower lip down the embankment to just under the water surface
                o.start = p + o.down * (0.2f * outfallScale) + o.up * (0.035f * outfallScale);
                o.len = Mathf.Max(0.3f, (o.start.y - (waterY - 0.06f)) / Mathf.Max(0.2f, -o.down.y));
                o.impact = o.start + o.down * ((o.start.y - waterY) / Mathf.Max(0.2f, -o.down.y)); o.impact.y = waterY;
                BuildRibbon(o);
                BuildStain(o);
                BuildFoam(o);
                o.tDrop = (float)rng.NextDouble() * 0.1f; o.tRing = (float)rng.NextDouble(); o.tMist = (float)rng.NextDouble();
                outs.Add(o);
            }
        }

        void BuildRibbon(Outfall o)
        {
            o.rv = new Vector3[RRows * 3]; o.rc = new Color[RRows * 3]; o.ruv = new List<Vector4>(RRows * 3); o.rs = new float[RRows];
            var tri = new List<int>();
            var side = Vector3.Cross(o.up, o.down).normalized;
            for (int r = 0; r < RRows; r++)
            {
                float t = r / (RRows - 1f), s = t * o.len;
                float w = Mathf.Lerp(0.2f, 0.42f, Mathf.Sqrt(t)) * outfallScale;
                float bulge = 0.05f * Mathf.Clamp01(1f - s / 0.16f);                                  // the tongue over the lip
                var c = o.start + o.down * s + o.up * (0.012f + bulge);
                o.rs[r] = s;
                for (int k = 0; k < 3; k++)
                {
                    float u = k * 0.5f;
                    o.rv[r * 3 + k] = c + side * ((u - 0.5f) * w);
                    o.ruv.Add(new Vector4(u, Mathf.Lerp(0.3f, 0.7f, t), 0f, 0f));
                    o.rc[r * 3 + k] = water.linear;
                }
                if (r > 0)
                    for (int k = 0; k < 2; k++)
                    {
                        int a = (r - 1) * 3 + k, b = a + 1, cc = r * 3 + k, dd = cc + 1;
                        tri.Add(a); tri.Add(cc); tri.Add(b); tri.Add(b); tri.Add(cc); tri.Add(dd);
                    }
            }
            var m = new Mesh { name = "Pour_" + o.id, hideFlags = HideFlags.DontSave };
            m.MarkDynamic();
            m.SetVertices(o.rv); m.SetUVs(0, o.ruv); m.SetColors(o.rc); m.SetTriangles(tri, 0);
            var nrm = new Vector3[o.rv.Length]; for (int i = 0; i < nrm.Length; i++) nrm[i] = o.up; m.normals = nrm;
            m.bounds = new Bounds(o.start + o.down * (o.len * 0.5f), Vector3.one * (o.len + 1f));
            o.ribbon = m;
        }

        void BuildStain(Outfall o)
        {   // dark wet streak on the concrete under the mouth (FxAdditive shape 7: dst x (1 - a))
            var side = Vector3.Cross(o.up, o.down).normalized;
            var v = new List<Vector3>(); var c = new List<Color>(); var uv = new List<Vector4>(); var tri = new List<int>();
            const int N = 8;
            for (int r = 0; r < N; r++)
            {
                float t = r / (N - 1f), s = -0.3f + t * (o.len + 0.4f);
                float w = Mathf.Lerp(0.55f, 0.8f, t) * outfallScale;
                var ctr = o.face + o.down * s + o.up * 0.007f;
                for (int k = 0; k < 3; k++)
                {
                    float u = k * 0.5f;
                    v.Add(ctr + side * ((u - 0.5f) * w));
                    float a = (k == 1 ? 0.42f : 0f) * Mathf.Lerp(1f, 0.6f, t) * Mathf.Clamp01((s + 0.25f) / 0.2f);
                    c.Add(new Color(0f, 0f, 0f, a)); uv.Add(new Vector4(u, t, 0, 0));
                }
                if (r > 0) for (int k = 0; k < 2; k++) { int a0 = (r - 1) * 3 + k; tri.AddRange(new[] { a0, a0 + 3, a0 + 1, a0 + 1, a0 + 3, a0 + 4 }); }
            }
            var m = new Mesh { name = "Stain_" + o.id, hideFlags = HideFlags.DontSave };
            m.SetVertices(v); m.SetColors(c); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
            o.stain = m;
        }

        void BuildFoam(Outfall o)
        {   // one flat boiling foam patch on the water where the flow lands (FxAdditive shape 2 with billow erosion)
            var m = new Mesh { name = "Foam_" + o.id, hideFlags = HideFlags.DontSave };
            m.MarkDynamic();
            float r = 0.62f;
            var ctr = o.impact + new Vector3(o.down.x, 0f, o.down.z).normalized * 0.25f; ctr.y = waterY + 0.03f;
            m.SetVertices(new[] { ctr + new Vector3(-r, 0, -r), ctr + new Vector3(r, 0, -r), ctr + new Vector3(r, 0, r), ctr + new Vector3(-r, 0, r) });
            m.SetColors(new[] { new Color(1, 1, 1, 0.62f), new Color(1, 1, 1, 0.62f), new Color(1, 1, 1, 0.62f), new Color(1, 1, 1, 0.62f) });
            o.fuv[0] = new Vector4(0, 0, 0, 0.3f); o.fuv[1] = new Vector4(1, 0, 0, 0.3f); o.fuv[2] = new Vector4(1, 1, 0, 0.3f); o.fuv[3] = new Vector4(0, 1, 0, 0.3f);
            m.SetUVs(0, o.fuv);
            m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.RecalculateBounds();
            o.foamM = m;
        }

        void StepOutfalls(float now, float dt)
        {
            var fx = OpenZFxPlayer.I;
            var focus = rts ? rts.focus : (cam ? cam.transform.position : Vector3.zero);
            float camH = rts ? rts.height : 70f;
            foreach (var o in outs)
            {
                bool vis = DetailKit.OnScreen(cam, o.impact, 0.1f);
                float spd = flowSpeed * (o.gush > 0f ? 1.8f : 1f);
                if (o.gush > 0f) o.gush -= dt;
                // pour loop for the outfalls near the view centre
                float dist = Vector2.Distance(new Vector2(focus.x, focus.z), new Vector2(o.impact.x, o.impact.z));
                SetLoop(o, playSounds && dist < 55f && camH < 150f, dist);
                if (!vis) continue;
                // ribbon colours: foam streaks scroll down the flow
                for (int r = 0; r < RRows; r++)
                {
                    float s = o.rs[r];
                    for (int k = 0; k < 3; k++)
                    {
                        float x = (k - 1) * 0.9f;
                        float w1 = 0.5f + 0.5f * Mathf.Sin((s - now * spd) * 26f + x * 1.9f + o.idx);
                        float w2 = 0.5f + 0.5f * Mathf.Sin((s - now * spd * 1.3f) * 11f - x * 2.3f + 2.1f);
                        float f = Mathf.Clamp01(w1 * w1 * w1 * 0.7f + w2 * 0.25f + (k == 1 ? 0.08f : 0f));
                        var c = Color.Lerp(water, foam, f).linear;                                   // FxAdditive reads vertex colours as linear
                        c.a = Mathf.Lerp(0.8f, 0.95f, f) * Mathf.Clamp01(1f - Mathf.Max(0f, s - (o.len - 0.12f)) / 0.12f);
                        o.rc[r * 3 + k] = c;
                    }
                }
                o.ribbon.SetColors(o.rc);
                o.foamAge = Mathf.Repeat(o.foamAge + dt * 0.08f, 1f);
                for (int k = 0; k < 4; k++) { var u = o.fuv[k]; u.z = 0.2f + 0.3f * (0.5f + 0.5f * Mathf.Sin(o.foamAge * 2f * Mathf.PI)); u.w = 0.3f + o.idx * 0.17f + o.foamAge * 0.05f; o.fuv[k] = u; }
                o.foamM.SetUVs(0, o.fuv);
                if (!fx || camH > 160f) continue;
                // splash droplets, foam rings, a little mist
                o.tDrop -= dt; o.tRing -= dt; o.tMist -= dt;
                float rate = o.gush > 0f ? 0.5f : 1f;
                while (o.tDrop <= 0f)
                {
                    o.tDrop += 0.07f * rate;
                    var dir = new Vector3(o.down.x, 0f, o.down.z).normalized;
                    var v = dir * (0.5f + (float)rng.NextDouble() * 0.7f) + new Vector3(((float)rng.NextDouble() - 0.5f) * 0.9f, 1.1f + (float)rng.NextDouble() * 1.0f, ((float)rng.NextDouble() - 0.5f) * 0.9f);
                    fx.EmitChannel("m12_splash", o.impact + Vector3.up * 0.03f, v, 0.42f + (float)rng.NextDouble() * 0.15f, 0.045f + (float)rng.NextDouble() * 0.045f, water, 1f);
                }
                if (o.tRing <= 0f)
                {
                    o.tRing += 0.55f + (float)rng.NextDouble() * 0.25f;
                    fx.EmitChannel("m12_ripple", new Vector3(o.impact.x, waterY + 0.09f, o.impact.z), Vector3.zero, 1.5f, 0.9f + (float)rng.NextDouble() * 0.5f, foam, 0.75f);
                }
                if (mist && o.tMist <= 0f)
                {
                    o.tMist += 0.3f + (float)rng.NextDouble() * 0.2f;
                    var mp = o.impact + new Vector3(((float)rng.NextDouble() - 0.5f) * 0.4f, 0.15f, ((float)rng.NextDouble() - 0.5f) * 0.4f);
                    fx.EmitChannel("m12_dust", mp, new Vector3(((float)rng.NextDouble() - 0.5f) * 0.15f, 0.16f, ((float)rng.NextDouble() - 0.5f) * 0.15f),
                                   2.4f, 0.5f + (float)rng.NextDouble() * 0.4f, new Color(0.82f, 0.84f, 0.8f), 0.12f + (float)rng.NextDouble() * 0.06f);
                }
            }
        }

        void DrawOutfalls()
        {
            foreach (var o in outs)
            {
                if (!DetailKit.OnScreen(cam, o.impact, 0.2f)) continue;
                if (fxStain && o.stain) Graphics.RenderMesh(new RenderParams(fxStain) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false }, o.stain, 0, Matrix4x4.identity);
                if (fxRibbon && o.ribbon) Graphics.RenderMesh(new RenderParams(fxRibbon) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false }, o.ribbon, 0, Matrix4x4.identity);
                if (fxFoam && o.foamM) Graphics.RenderMesh(new RenderParams(fxFoam) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false }, o.foamM, 0, Matrix4x4.identity);
            }
        }

        void SetLoop(Outfall o, bool on, float dist)
        {
            if (!on) { if (o.loop && o.loop.isPlaying) o.loop.Stop(); return; }
            if (!o.loop)
            {
                var clip = DetailKit.Pool3D.Clip("OpenZ/Sfx/dtl_outfall_pour_loop");
                float pitch = 1f;
                if (!clip) { clip = DetailKit.Pool3D.Clip("OpenZ/Audio/Ambience/sd_river_flow_loop"); pitch = 1.35f; }
                if (!clip) return;
                var go = new GameObject("PourLoop_" + o.id); go.transform.SetParent(root, false); go.transform.position = o.impact + Vector3.up * 0.3f;
                o.loop = go.AddComponent<AudioSource>();
                o.loop.clip = clip; o.loop.loop = true; o.loop.playOnAwake = false; o.loop.spatialBlend = 1f; o.loop.dopplerLevel = 0f;
                o.loop.rolloffMode = AudioRolloffMode.Linear; o.loop.minDistance = 4f; o.loop.maxDistance = 42f; o.loop.pitch = pitch;
            }
            float sfx = CombatAudio.I ? CombatAudio.I.volume * CombatAudio.BusGain(CombatAudio.Bus.World) : 0.55f;
            o.loop.volume = 0.42f * sfx * (o.gush > 0f ? 1.5f : 1f) * (DetailKit.AudioEventActive ? 0f : 1f);
            if (!o.loop.isPlaying) { o.loop.time = (float)rng.NextDouble() * Mathf.Max(0f, o.loop.clip.length - 0.1f); o.loop.Play(); }
        }

        void BreakOutfall(Outfall o, Vector3 from)
        {
            if (o.broken) return;
            o.broken = true; o.gush = 5f;
            var p = o.face + o.up * 0.05f;
            if (o.grille)
            {
                var v = (o.up * 3.2f + (p - from).normalized * 2f + Vector3.up * 2f);
                chunks.Throw(o.grille, v, new Vector3(380f, 120f, 260f), 12f);
            }
            bits.Burst(p, 8, BConcrete, 2f, 0.07f, rng); bits.Burst(p, 5, BRust, 2.2f, 0.05f, rng);
            FxManager.I?.Pop(p, 0.4f);
            if (playSounds) DetailKit.Sound("dtl_outfall_break", p, 0.7f, "prop", 0.1f, -1, "final-metal-land-a", "final-metal-land-b");
        }

        // ================================================================== grates
        sealed class Grate { public Vector3 pos; public GameObject go; public Renderer glow; public Transform frame; public MaterialPropertyBlock mpb = new MaterialPropertyBlock(); public float shown = -1f, flick = 1f, flickT, wisp; public bool broken; public float dying = -1f; public Amber amber; }
        readonly List<Grate> grs = new List<Grate>();

        void BuildGrates()
        {
            var d = HarbourDetailsData.Grates;
            for (int i = 0; i < d.Length; i++)
            {
                var g = new Grate();
                var p = new Vector3(d[i].x, 0f, d[i].y);
                p.y = DetailKit.TerrainMax(game, p, 0.55f);
                g.pos = p;
                g.go = DetailKit.Model("L17D_Grate", root);
                if (!g.go) continue;
                g.go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, d[i].z, 0f));
                DetailKit.SetMat(g.go, toyMetal, false);
                var gl = DetailKit.Find(g.go.transform, "Glow");
                if (gl) { g.glow = gl.GetComponent<Renderer>(); if (g.glow) { g.glow.sharedMaterial = NightMeshes.Lens(); g.glow.shadowCastingMode = ShadowCastingMode.Off; } }
                g.frame = DetailKit.Find(g.go.transform, "Frame");
                g.flickT = 2f + (float)rng.NextDouble() * 8f; g.wisp = (float)rng.NextDouble() * 2f;
                g.amber = NewAmber("grate_" + i, p, 2.8f);                                        // REVIEW-FIX: the grate lights the quay at night
                grs.Add(g);
            }
        }

        void StepGrates(float now, float dt)
        {
            float night = DetailKit.Night;
            var fx = OpenZFxPlayer.I;
            foreach (var g in grs)
            {
                if (!g.glow) continue;
                // a tired fluorescent tube down in the tunnel: steady, now and then a quick flutter
                g.flickT -= dt;
                if (g.flickT <= 0f) { g.flickT = 6f + (float)rng.NextDouble() * 14f; g.flick = 0.25f; }
                g.flick = Mathf.MoveTowards(g.flick, 1f, dt * (g.flick < 0.6f ? 2.5f : 6f));
                float k = Mathf.Lerp(grateDay, grateNight, night) * g.flick * (0.94f + 0.06f * Mathf.Sin(now * 7.3f + g.pos.x));
                if (g.dying >= 0f) { g.dying += dt; k *= g.dying < 2.2f ? (Mathf.Sin(g.dying * 31f) > 0.2f ? 0.8f : 0.1f) : 0f; }
                // the real light (night only, like the floor lights): follows the tube's flutter and dies with a broken grate
                float on = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.4f, night));
                OfferAmber(g.amber, on * Mathf.Clamp01(k / Mathf.Max(0.01f, grateNight)));
                var c = grateGlow.linear * k;
                float key = c.r + c.g + c.b;
                if (Mathf.Abs(key - g.shown) > 0.004f) { g.shown = key; g.mpb.SetColor(ColorId, c); g.glow.SetPropertyBlock(g.mpb); g.glow.enabled = key > 0.002f; }
                // warm vapour catching the light (night)
                if (fx && night > 0.4f && k > 0.3f && DetailKit.OnScreen(cam, g.pos))
                {
                    g.wisp -= dt;
                    if (g.wisp <= 0f)
                    {
                        g.wisp = 1.4f + (float)rng.NextDouble() * 2.2f;
                        fx.EmitChannel("m12_dust", g.pos + new Vector3(((float)rng.NextDouble() - 0.5f) * 0.6f, 0.12f, ((float)rng.NextDouble() - 0.5f) * 0.35f),
                                       new Vector3(0.12f, 0.32f, 0.06f), 2.6f, 0.45f + (float)rng.NextDouble() * 0.3f, new Color(1f, 0.8f, 0.58f), 0.09f);
                    }
                }
            }
        }

        void BreakGrate(Grate g, Vector3 from)
        {
            if (g.broken) return;
            g.broken = true; g.dying = 0f;
            if (g.frame) chunks.Throw(g.frame, (g.pos - from).normalized * 1.5f + Vector3.up * 4.2f, new Vector3(420f, 90f, 300f), 14f);
            FxManager.I?.Pop(g.pos + Vector3.up * 0.1f, 0.5f);
            bits.Burst(g.pos + Vector3.up * 0.05f, 7, BConcrete, 2f, 0.06f, rng);
            if (playSounds) DetailKit.Sound("dtl_grate_break", g.pos, 0.7f, "prop", 0.1f, -1, "final-metal-land-a", "final-metal-land-b", "final-metal-land-c");
        }

        // ================================================================== hatches
        sealed class Hatch { public Vector3 pos; public GameObject go; public Transform lid; public Renderer glow; public MaterialPropertyBlock mpb = new MaterialPropertyBlock(); public float next, t, ang, open; public int st; public bool broken; public Quaternion lid0; public Amber amber; }
        readonly List<Hatch> hts = new List<Hatch>();

        void BuildHatches()
        {
            var d = HarbourDetailsData.Hatches;
            for (int i = 0; i < d.Length; i++)
            {
                var h = new Hatch();
                var p = new Vector3(d[i].x, 0f, d[i].y);
                p.y = DetailKit.TerrainMax(game, p, 0.46f);
                h.pos = p;
                h.go = DetailKit.Model("L17D_Hatch", root);
                if (!h.go) continue;
                h.go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, d[i].z, 0f));
                DetailKit.SetMat(h.go, toyMetal, false);
                h.lid = DetailKit.Find(h.go.transform, "Lid"); if (h.lid) h.lid0 = h.lid.localRotation;
                var gl = DetailKit.Find(h.go.transform, "Glow");
                if (gl) { h.glow = gl.GetComponent<Renderer>(); if (h.glow) { h.glow.sharedMaterial = NightMeshes.Lens(); h.glow.shadowCastingMode = ShadowCastingMode.Off; h.glow.enabled = false; } }
                h.next = hatchPeekEvery.x * 0.3f + (float)rng.NextDouble() * hatchPeekEvery.y;
                h.amber = NewAmber("hatch_" + i, p, 2.4f);                                        // REVIEW-FIX: a peeking hatch lights the quay
                hts.Add(h);
            }
        }

        void StepHatches(float now, float dt)
        {
            foreach (var h in hts)
            {
                if (!h.lid || h.broken) { Glow(h, h.broken ? 0.6f : 0f); continue; }
                switch (h.st)
                {
                    case 0:
                        h.next -= dt;
                        if (h.next <= 0f)
                        {
                            h.next = hatchPeekEvery.x + (float)rng.NextDouble() * (hatchPeekEvery.y - hatchPeekEvery.x);
                            if (DetailKit.OnScreen(cam, h.pos) && NearestUnitSq(h.pos) > 5.5f * 5.5f) { h.st = 1; h.t = 0f; h.open = 12f + (float)rng.NextDouble() * 8f; }
                        }
                        break;
                    case 1: h.t += dt; h.ang = h.open * Mathf.SmoothStep(0f, 1f, h.t / 0.6f); if (h.t >= 0.6f) { h.st = 2; h.t = 0f; Puff(h); } break;
                    case 2: h.t += dt; h.ang = h.open + Mathf.Sin(h.t * 3f) * 1.5f; if (h.t > 1.6f + (float)rng.NextDouble() * 0.05f || NearestUnitSq(h.pos) < 4f) { h.st = 3; h.t = 0f; } break;
                    case 3:
                        h.t += dt; h.ang = h.open * (1f - Mathf.Clamp01(h.t / 0.22f) * Mathf.Clamp01(h.t / 0.22f));
                        if (h.t >= 0.22f) { h.st = 0; h.ang = 0f; if (playSounds) DetailKit.Sound("dtl_hatch_clunk", h.pos, 0.55f, "prop", 0.2f, -1, "sd_hatch_close_a", "sd_hatch_close_b"); }
                        break;
                }
                h.lid.localRotation = Quaternion.AngleAxis(-h.ang, Vector3.right) * h.lid0;
                Glow(h, h.ang > 0.5f ? Mathf.Clamp01(h.ang / 10f) : 0f);
            }
        }

        void Glow(Hatch h, float k)
        {
            float night = DetailKit.Night;
            OfferAmber(h.amber, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.4f, night)) * k);   // the light from the shaft (night)
            if (!h.glow) return;
            var c = grateGlow.linear * (k * Mathf.Lerp(0.5f, 1.4f, night));
            h.glow.enabled = k > 0.01f;
            if (h.glow.enabled) { h.mpb.SetColor(ColorId, c); h.glow.SetPropertyBlock(h.mpb); }
        }

        void Puff(Hatch h)
        {
            OpenZFxPlayer.I?.EmitChannel("m12_dust", h.pos + Vector3.up * 0.2f, new Vector3(0.1f, 0.5f, 0.05f), 2.2f, 0.7f, new Color(0.85f, 0.86f, 0.84f), 0.18f);
        }

        void BreakHatch(Hatch h, Vector3 from)
        {
            if (h.broken || !h.lid) return;
            h.broken = true;
            var dir = h.pos - from; dir.y = 0f; dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
            chunks.Throw(h.lid, dir * 2.2f + Vector3.up * 6.5f, new Vector3(520f, 140f, 380f), 16f);
            FxManager.I?.Pop(h.pos + Vector3.up * 0.2f, 0.5f);
            Puff(h);
            if (playSounds) DetailKit.Sound("dtl_hatch_blown", h.pos, 0.8f, "prop", 0.1f, -1, "final-metal-land-a", "final-metal-land-b", "final-metal-land-c");
        }

        // ================================================================== wrecks
        sealed class Wreck
        {
            public string id; public GameObject go; public Transform pivot; public Vector3 pos; public Vector2 half; public float hp = 3f, lap, creak, sink = -1f;
            public readonly List<Transform> loose = new List<Transform>(); public Transform body, cabin, tyre; public bool dead; public float tyreT; public Vector3 tyreDrift;
            // DV39 harbour wreck: the sim object it draws (null = a view-only channel wreck), its last hit points and panels lost
            public ZObject sim; public int simId = -1, lastHp, panelsOff;
        }
        readonly List<Wreck> wks = new List<Wreck>();
        static readonly string[] LooseParts = { "Hood", "Trunk", "DoorL", "DoorR", "Bumpers", "Lamps", "WheelFL", "WheelFR", "WheelRL", "WheelRR", "DrumRL" };

        void BuildWrecks()
        {
            var d = HarbourDetailsData.Wrecks; var a = HarbourDetailsData.WreckAngles; var st = HarbourDetailsData.WreckSimTile;
            for (int i = 0; i < d.Length; i++)
            {
                ZObject sim = null;
                if (st[i].x >= 0)
                {   // a harbour wreck is drawn only for its sim object: without it (DV39 off, 'Zod exact', or the sim hooks missing)
                    // robots would wade through the model, so it is not built at all
                    foreach (var o in game.World.Wrecks) if (o.LocX == st[i].x * 16 && o.LocY == st[i].y * 16) { sim = o; break; }
                    if (sim == null) { Debug.Log($"[OpenZ] HarbourDetails: harbour wreck {HarbourDetailsData.WreckIds[i]} has no sim object (DV39 off or the sim hooks not applied): not shown"); continue; }
                }
                string model = "L17D_Wreck_" + HarbourDetailsData.WreckModels[i];
                var w = new Wreck { id = HarbourDetailsData.WreckIds[i], half = new Vector2(d[i].z, d[i].w), sim = sim };
                if (sim != null) { w.simId = sim.Id; w.lastHp = sim.Health; }
                w.pivot = new GameObject("Wreck_" + w.id).transform; w.pivot.SetParent(root, false);
                w.go = DetailKit.Model(model, w.pivot);
                if (!w.go) continue;
                DetailKit.SetMat(w.go, toyWreck, true);
                var rot = Quaternion.Euler(0f, a[i].x, 0f) * Quaternion.Euler(a[i].y, 0f, a[i].z);
                // rest on the bed, settled into the mud; at least 45 cm of the car above the water
                float bed = game.TerrainY(d[i].x, d[i].y);
                if (bed > waterY - 0.3f || bed < waterY - 1.6f) bed = waterY - 0.8f;
                float lowest = 0f, top = float.MinValue;
                foreach (var sx in new[] { -1f, 1f }) foreach (var sz in new[] { -1f, 1f })
                    {
                        var c = rot * new Vector3(sx * w.half.x, 0f, sz * w.half.y); lowest = Mathf.Min(lowest, c.y);
                        var c2 = rot * new Vector3(sx * w.half.x * 0.8f, 1.3f, sz * w.half.y * 0.6f); top = Mathf.Max(top, c2.y);
                    }
                // Wreck waterline (the van used to sit ON the water): the waterline at about mid-door, i.e. wreckSubmerge
                // (0.55) of the model's own height under the surface at the pivot (the base centre); the roof and the upper body stay
                // out; a shallow bed just takes the wheels into the mud (under the murky water). Was: base 0.5 m under (~0.8 of 1.3 m out).
                float hgt = ModelHeight(w.pivot, w.go);
                float y = waterY - wreckSubmerge * hgt;
                if (y + top < waterY + 0.45f) y = waterY + 0.45f - top;
                w.pos = new Vector3(d[i].x, y, d[i].y);
                w.pivot.SetPositionAndRotation(w.pos, rot);
                TintWaterline(w);
                foreach (var nm in LooseParts) { var t = DetailKit.Find(w.go.transform, nm); if (t) w.loose.Add(t); }
                w.body = DetailKit.Find(w.go.transform, "Body"); w.cabin = DetailKit.Find(w.go.transform, "Cabin");
                w.lap = (float)rng.NextDouble() * 2f; w.creak = 15f + (float)rng.NextDouble() * 40f;
                wks.Add(w);
            }
        }

        /// <summary>The model's height from its lowest to its highest vertex, in pivot space (before the pivot is turned).</summary>
        static float ModelHeight(Transform pivot, GameObject go)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh; if (!m) continue;
                var b = m.bounds; var M = pivot.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int k = 0; k < 8; k++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                    float yy = M.MultiplyPoint3x4(c).y; lo = Mathf.Min(lo, yy); hi = Mathf.Max(hi, yy);
                }
            }
            return hi > lo ? hi - Mathf.Max(lo, 0f) : 1.3f;
        }

        static readonly Color WaterBand = new Color(0.36f, 0.37f, 0.24f);

        /// <summary>REVIEW-FIX (clean toy paint): the kit is the faded base colour + one or two clean rust patches; the only
        /// "dirt" is ONE clean olive waterline band painted where the water really meets this wreck (its world height after
        /// placement: the band 13 cm over the surface, a muted wet tone under it). Per-wreck mesh copies (vertex colours).</summary>
        void TintWaterline(Wreck w)
        {
            foreach (var mf in w.go.GetComponentsInChildren<MeshFilter>(true))
            {
                var src = mf.sharedMesh; if (!src || !src.isReadable) continue;
                var cols = src.colors; var v = src.vertices;
                if (cols == null || cols.Length != v.Length) continue;
                var L = mf.transform.localToWorldMatrix;
                bool any = false;
                for (int k = 0; k < v.Length; k++)
                {
                    float y = L.MultiplyPoint3x4(v[k]).y;
                    if (y < waterY - 0.03f) { cols[k] = Color.Lerp(cols[k], WaterBand, 0.45f); any = true; }
                    else if (y < waterY + 0.13f) { cols[k] = Color.Lerp(cols[k], WaterBand, 0.8f); any = true; }
                }
                if (!any) continue;
                var m = DetailKit.Track(Instantiate(src)); m.name = src.name + "_waterline"; m.hideFlags = HideFlags.DontSave;
                m.colors = cols; mf.sharedMesh = m;
            }
        }

        void StepWrecks(float now, float dt)
        {
            var fx = OpenZFxPlayer.I;
            foreach (var w in wks)
            {
                if (!w.go) continue;
                if (w.sim != null && !w.dead) SyncSimWreck(w);
                if (w.sink >= 0f)
                {   // broken: the hull settles and slides under with bubbles, then is gone; a tyre floats on a while
                    w.sink += dt;
                    w.pivot.position = w.pos + Vector3.down * (1.6f * Mathf.SmoothStep(0f, 1f, w.sink / 4.5f));
                    w.pivot.rotation *= Quaternion.Euler(dt * 3f, 0f, dt * 5f);
                    if (fx && w.sink < 5f && rng.NextDouble() < dt * 6f)
                    {
                        var bp = w.pos + w.pivot.rotation * new Vector3(((float)rng.NextDouble() - 0.5f) * w.half.x * 2f, 0f, ((float)rng.NextDouble() - 0.5f) * w.half.y * 2f); bp.y = waterY;
                        fx.EmitChannel("m12_ripple", bp + Vector3.up * 0.09f, Vector3.zero, 1.2f, 0.4f + (float)rng.NextDouble() * 0.4f, foam, 0.7f);
                        fx.Droplets(bp, 0.25f, 3);
                    }
                    if (w.sink > 5f && w.go.activeSelf) w.go.SetActive(false);
                    if (w.tyre)
                    {
                        w.tyreT += dt;
                        var tp = w.tyre.position; tp += w.tyreDrift * dt; tp.y = waterY - 0.08f + Mathf.Sin(now * 1.7f + w.pos.x) * 0.025f; w.tyre.position = tp;
                        w.tyre.rotation = Quaternion.Euler(Mathf.Sin(now * 1.3f) * 6f, w.tyreT * 9f, 90f);           // axle up: the tyre lies flat
                        if (w.tyreT > 60f) { float k = Mathf.Clamp01((65f - w.tyreT) / 5f); w.tyre.localScale = Vector3.one * k; if (k <= 0f) { Destroy(w.tyre.gameObject); w.tyre = null; } }
                    }
                    continue;
                }
                if (!DetailKit.OnScreen(cam, w.pos, 0.1f)) continue;
                w.lap -= dt; w.creak -= dt;
                if (fx && w.lap <= 0f)
                {   // water laps along the hull at the waterline
                    w.lap = 0.9f + (float)rng.NextDouble() * 1.6f;
                    float u = (float)rng.NextDouble() * 2f - 1f, s = rng.NextDouble() < 0.5 ? -1f : 1f;
                    var lp = rng.NextDouble() < 0.6 ? new Vector3(s * (w.half.x + 0.05f), 0f, u * w.half.y) : new Vector3(u * w.half.x, 0f, s * (w.half.y + 0.05f));
                    var p = w.pivot.position + Quaternion.Euler(0f, w.pivot.eulerAngles.y, 0f) * lp; p.y = waterY + 0.09f;
                    fx.EmitChannel("m12_ripple", p, Vector3.zero, 1.8f, 0.45f + (float)rng.NextDouble() * 0.45f, foam, 0.55f);
                    if (rng.NextDouble() < 0.25) fx.Droplets(p, 0.15f, 2);
                }
                if (w.creak <= 0f)
                {
                    w.creak = 25f + (float)rng.NextDouble() * 45f;
                    if (playSounds) DetailKit.Sound("dst_wreck_creak", w.pos, 0.55f, "prop", 1f, -1, "sd_metal_creak_a", "sd_metal_creak_b");
                }
            }
        }

        /// <summary>DV39: the drawn wreck follows its sim object. Hit points lost -> sparks, a clang and a splash (a panel flies at
        /// 2/3 and 1/3 of the hit points); destroyed or deleted -> it breaks apart and sinks (normally CombatView already handed the
        /// Death over through <see cref="SimWreckDied"/>; this is the fallback). Read only: the view never writes the sim.</summary>
        void SyncSimWreck(Wreck w)
        {
            var o = w.sim;
            bool gone = o.Destroyed || o.KillAt >= 0 || game.World.Get(w.simId) == null;
            int hp = gone ? 0 : o.Health;
            if (hp >= w.lastHp) return;
            var from = LastBlastNear(w.pos, out var bf) ? bf : w.pos + (Quaternion.Euler(0f, (w.simId * 97) % 360, 0f) * Vector3.forward) * 3f;
            float frac = o.MaxHealth > 0 ? Mathf.Clamp01(hp / (float)o.MaxHealth) : 0f;
            w.lastHp = hp;
            if (gone || hp <= 0) { BreakWreck(w, from); return; }
            int want = frac <= 1f / 3f ? 2 : frac <= 2f / 3f ? 1 : 0;
            bool panel = want > w.panelsOff; w.panelsOff = Mathf.Max(w.panelsOff, want);
            HitWreckVisual(w, from, panel);
        }

        /// <summary>CombatView.Death (one line apply.py adds): a DV39 wreck died in the sim. True = handled (the caller plays no
        /// generic prop break: no stone chunks and rock sound from a car).</summary>
        public static bool SimWreckDied(ZObject o, Vector3 from)
        {
            if (o == null) return false;
            if (!I) return true;                                                                  // a wreck nobody draws: nothing to break
            foreach (var w in I.wks)
                if (w.simId == o.Id) { if (!w.dead) { w.lastHp = 0; I.BreakWreck(w, from); } return true; }
            return true;
        }

        Vector3 lastBlastAt; float lastBlastTime = -99f;
        bool LastBlastNear(Vector3 p, out Vector3 at)
        {
            at = lastBlastAt;
            return Time.time - lastBlastTime < 0.5f && Flat2(lastBlastAt, p) < 12f * 12f;
        }

        /// <summary>A hit that does not break it: sparks, a clang, a splash; `panel` = a loose panel flies off too.</summary>
        void HitWreckVisual(Wreck w, Vector3 from, bool panel)
        {
            var dir = w.pos - from; dir.y = 0f; dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
            var fx = OpenZFxPlayer.I;
            if (panel && w.loose.Count > 0)
            {
                int k = rng.Next(w.loose.Count); var t = w.loose[k]; w.loose.RemoveAt(k);
                chunks.Throw(t, dir * 3f + Vector3.up * 5f, new Vector3(300f, 200f, 420f), 12f);
            }
            FxManager.I?.Pop(w.pos + Vector3.up * 0.8f, 0.5f);
            bits.Burst(w.pos + Vector3.up * 0.6f, 6, BRust, 2.4f, 0.08f, rng);
            if (fx) fx.Splash(new Vector3(w.pos.x, waterY, w.pos.z), 0.7f);
            if (playSounds) DetailKit.Sound("dtl_wreck_hit", w.pos, 0.8f, "tank_impact", 0.15f, -1, "final-metal-land-a", "final-metal-land-b", "final-metal-land-c");
        }

        /// <summary>A view-only channel wreck hit by a blast (2-3 blasts: its own small hit counter; no sim object).</summary>
        void HitWreck(Wreck w, Vector3 from, float strength)
        {
            if (w.dead || !w.go || w.sim != null) return;
            w.hp -= Mathf.Max(0.8f, strength * 2f);
            if (w.hp > 0f) { HitWreckVisual(w, from, true); return; }
            BreakWreck(w, from);
        }

        void BreakWreck(Wreck w, Vector3 from)
        {
            if (w.dead || !w.go) return;
            w.dead = true;
            var dir = w.pos - from; dir.y = 0f; dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
            var fx = OpenZFxPlayer.I;
            // everything loose flies, the cabin roof pops, the hull sinks
            foreach (var t in w.loose)
            {
                var v = dir * (2f + (float)rng.NextDouble() * 3f) + new Vector3(((float)rng.NextDouble() - 0.5f) * 5f, 5f + (float)rng.NextDouble() * 5f, ((float)rng.NextDouble() - 0.5f) * 5f);
                chunks.Throw(t, v, new Vector3((float)rng.NextDouble() * 700f, (float)rng.NextDouble() * 400f, (float)rng.NextDouble() * 700f), 12f);
            }
            w.loose.Clear();
            if (w.cabin) chunks.Throw(w.cabin, dir * 1.5f + Vector3.up * 7.5f, new Vector3(160f, 60f, 240f), 12f);
            // one tyre floats: a fresh copy from the model, flat on the water
            var tpl = Resources.Load<GameObject>(DetailKit.Res + "L17D_Wreck_A");
            if (tpl)
            {
                var src = DetailKit.Find(tpl.transform, "WheelFL");
                if (src)
                {
                    var t = Instantiate(src.gameObject, root).transform; t.name = "FloatingTyre_" + w.id;
                    t.position = new Vector3(w.pos.x, waterY - 0.08f, w.pos.z) + dir * (w.half.y + 0.8f);
                    foreach (var r in t.GetComponentsInChildren<Renderer>()) r.sharedMaterial = toyWreck;
                    w.tyre = t; w.tyreDrift = dir * 0.12f;
                }
            }
            w.sink = 0f;
            var p = new Vector3(w.pos.x, waterY, w.pos.z);
            if (fx) { fx.Splash(p, 1.6f); fx.Ripple(p, 1.4f); fx.DemolitionDust(p + Vector3.up * 0.3f, 3f, 2f, 0.5f); }
            FxManager.I?.Breakup(p + Vector3.up * 0.6f, "metal", 2.2f, 10, 0.9f);
            bits.Burst(p + Vector3.up * 0.7f, 14, BRust, 3.2f, 0.1f, rng); bits.Burst(p + Vector3.up * 0.8f, 8, BGlass, 3f, 0.05f, rng);
            if (playSounds)
            {
                DetailKit.Sound("dtl_wreck_break", w.pos, 1f, "wreck_crash", 0.1f, -1, "final-metal-land-a", "final-metal-land-b", "final-metal-land-c");
                DetailKit.Sound("dst_debris_splash", w.pos, 0.9f, "debris", 0.05f, -1, "sd_debris_splash_a", "sd_debris_splash_b");
            }
            if (verbose) Debug.Log($"[OpenZ] HarbourDetails: wreck {w.id} broke{(w.sim != null ? " (sim object destroyed: its tiles are open again)" : "")}");
        }

        // ================================================================== destruction
        void OnExplosion(Vector3 at, float strength)
        {
            float reach = 1.2f + 2.4f * strength;
            float r2 = reach * reach;
            lastBlastAt = at; lastBlastTime = Time.time;                                         // the direction a harbour wreck's sim hit came from
            foreach (var l in lights) if (!l.broken && Flat2(l.pos, at) < r2) BreakLight(l, at);
            foreach (var o in outs) if (!o.broken && Flat2(o.face, at) < (reach + 0.3f) * (reach + 0.3f)) BreakOutfall(o, at);
            foreach (var g in grs) if (!g.broken && Flat2(g.pos, at) < (reach + 0.4f) * (reach + 0.4f)) BreakGrate(g, at);
            foreach (var h in hts) if (!h.broken && Flat2(h.pos, at) < (reach + 0.4f) * (reach + 0.4f)) BreakHatch(h, at);
            foreach (var w in wks)
            {
                if (w.dead || !w.go || w.sim != null) continue;                                    // harbour wrecks: hit points come from the sim
                var lp = Quaternion.Inverse(Quaternion.Euler(0f, w.pivot.eulerAngles.y, 0f)) * (at - w.pos);
                float dx = Mathf.Max(0f, Mathf.Abs(lp.x) - w.half.x), dz = Mathf.Max(0f, Mathf.Abs(lp.z) - w.half.y);
                if (dx * dx + dz * dz < r2) HitWreck(w, at, strength);
            }
        }

        static float Flat2(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return dx * dx + dz * dz; }

        float NearestUnitSq(Vector3 p)
        {
            float best = float.MaxValue;
            foreach (var v in game.Views.Values)
            {
                if (!v || v.Dying) continue;
                var q = v.transform.position; float dx = q.x - p.x, dz = q.z - p.z, d = dx * dx + dz * dz;
                if (d < best) best = d;
            }
            return best;
        }

        // ================================================================== frame
        void Update()
        {
            if (!game) return;
            if (!cam) { cam = Camera.main; if (cam && !rts) rts = cam.GetComponent<RtsCamera>(); }
            float dt = game.paused ? 0f : Mathf.Min(Time.deltaTime, 0.05f);
            clock += dt;
            float now = Time.time;
            StepLights(now, dt);
            if (dt > 0f)
            {
                StepOutfalls(clock, dt);
                StepGrates(clock, dt);
                StepHatches(clock, dt);
                StepWrecks(clock, dt);
                chunks.Step(dt, game, waterY);
            }
            bits.StepAndDraw(dt, game);
            DrawLights();
            DrawAmberPools();
            DrawOutfalls();
        }

        // ================================================================== tests / status
        [ContextMenu("Test: blast every detail in view")]
        public void TestBlastInView()
        {
            foreach (var l in lights) if (DetailKit.OnScreen(cam, l.pos, 0f)) BreakLight(l, l.pos + Vector3.forward);
            foreach (var o in outs) if (DetailKit.OnScreen(cam, o.face, 0f)) BreakOutfall(o, o.face + o.up);
            foreach (var g in grs) if (DetailKit.OnScreen(cam, g.pos, 0f)) BreakGrate(g, g.pos + Vector3.back);
            foreach (var h in hts) if (DetailKit.OnScreen(cam, h.pos, 0f)) BreakHatch(h, h.pos + Vector3.back);
            foreach (var w in wks) if (DetailKit.OnScreen(cam, w.pos, 0f)) { if (w.sim != null) TestDamageSim(w, w.sim.MaxHealth); else HitWreck(w, w.pos + Vector3.back * 3f, 2f); }
        }

        /// <summary>Test menu: every channel wreck takes a blast; every harbour wreck loses 40 % of its sim hit points (a TEST-ONLY
        /// write through the sim's own damage path, like a missile splash: DamageHealth + UpdateObjectHealth), so the third call
        /// destroys it and its tiles open.</summary>
        [ContextMenu("Test: hit every wreck once")]
        public void TestHitWrecks()
        {
            foreach (var w in wks)
            {
                if (w.sim != null) TestDamageSim(w, Mathf.CeilToInt(w.sim.MaxHealth * 0.4f));
                else HitWreck(w, w.pos + new Vector3(2f, 0f, -2f), 0.6f);
            }
        }

        void TestDamageSim(Wreck w, int hp)
        {
            if (w.dead || w.sim == null || w.sim.Destroyed || game.World.Get(w.simId) == null) return;
            lastBlastAt = w.pos + new Vector3(2f, 0f, -2f); lastBlastTime = Time.time;
            w.sim.DamageHealth(hp); game.World.UpdateObjectHealth(w.sim);
        }

        [ContextMenu("Test: hatches peek now")]
        public void TestHatches() { foreach (var h in hts) if (!h.broken && h.st == 0) { h.st = 1; h.t = 0f; h.open = 18f; } }

        [ContextMenu("Status (log)")]
        public void Status()
        {
            int granted = 0, broken = 0; foreach (var l in lights) { if (l.light && l.light.enabled) granted++; if (l.broken) broken++; }
            var sb = new System.Text.StringBuilder();
            sb.Append($"[OpenZ] HarbourDetails: night {DetailKit.Night:F2}, floor lights {lights.Count} (real now {granted}, broken {broken}, budget {(ownMode ? "own" : "night")}), ");
            sb.Append($"outfalls {outs.Count}, grates {grs.Count}, hatches {hts.Count}, wrecks {wks.Count}, chunks {chunks.Active}\n");
            foreach (var w in wks)
                sb.Append(w.sim != null
                    ? $"  wreck {w.id} (sim #{w.simId}, tiles {w.sim.LocX / 16},{w.sim.LocY / 16} {w.sim.WidthPix / 16}x{w.sim.HeightPix / 16}): {(w.dead ? "sunk, tiles open" : $"{w.sim.Health} / {w.sim.MaxHealth} HP, robots path round it")} at {w.pos}\n"
                    : $"  wreck {w.id} (channel, view only): {(w.dead ? "sunk" : "hp " + w.hp.ToString("F1"))} at {w.pos}\n");
            foreach (var o in outs) sb.Append($"  outfall {o.id}: face {o.face} impact {o.impact} flow {o.len:F2} m{(o.broken ? " (grille gone)" : "")}\n");
            Debug.Log(sb.ToString());
        }
    }
}
