using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// View only: the sim is untouched (Zod's
    /// fort death deals no extra damage; OpenZEnding's held deaths already die as this same front passes them).
    /// <list type="bullet">
    /// <item><b>Timing</b>: the front radius is <see cref="RadiusAt"/> = the inverse of OpenZEnding.Delay (4 -> 95 m in 1.8 s,
    /// 4 + 91 (1 - (1 - t/1.8)^3), then OpenZEnding.WaveOuterSpeed m/s), so the ring, the dust wall, the shove, the endings
    /// death wave and the boom are one wave. It dies out at <see cref="MaxRadius"/>.</item>
    /// <item><b>Ring</b>: a ground-conforming annulus mesh (OpenZFxPlayer's cached ground heights, water surface included)
    /// drawn twice with OpenZ/FxAdditive shape 4 (world band, _BandProfile 1 = bright leading edge): a thin white-hot front
    /// and a wider orange trailing glow, both HDR (linear SetVector) so they bloom, fading with the radius.</item>
    /// <item><b>Refraction</b>: a 7 m band leaning outward on the front with OpenZ/HeatDistort mode 1 (the opaque texture
    /// pushed along the screen-space normal, faint brightening): the air visibly bends as the wave passes.</item>
    /// <item><b>Dust wall</b>: every <see cref="DustEvery"/> s, puffs on the front (on screen only, not on water) on the
    /// m12_smoke channel in the level's dust colour, thrown outward with the front and up.</item>
    /// <item><b>Shove</b>: every non-kinematic Rigidbody (fort chunks, debris, ragdolls) gets a velocity change outward + up
    /// when the front reaches it; unit views (robots, vehicles, cannons; not dying or held by OpenZEnding) are jolted outward
    /// and tilted away, springing back (added after UnitView posed them, removed before the next pose); DEC_ props under
    /// Environment rock on their base and settle.</item>
    /// <item><b>Camera + boom</b>: when the front reaches the camera focus: RtsCamera.Shake scaled by distance, an M12 impact
    /// </item>
    /// </list>
    /// NukeFx.Sequence calls <see cref="Fire"/> (strike and no-strike paths) a few frames after the blast with the blast's time;
    /// a second call within 3 s and 60 m (fort front + back) is merged. Fire() only adds the component (the fort-fall frame stays
    /// light): materials and meshes are built in Start, the rigidbodies scanned in the first Update, the DEC_ props come from the
    /// level-load cache (<see cref="CacheProps"/>, OpenZNukePrewarm). <see cref="ShakeScale"/> = 0 while NukeGlowCheck measures.
    /// </summary>
    [DefaultExecutionOrder(900)]   // LateUpdate after UnitView (0), FortGunView (45), OpenZEnding (-49): the jolt sits on top of the pose
    public sealed class OpenZFortShockwave : MonoBehaviour
    {
        public static bool Enabled = true;
        public static float ShakeScale = 1f;
        public static float MaxRadius = 150f;
        // ring look (linear HDR x gain)
        // v3b (first real gate frames: the 8-13 m wide flat orange trail read as a sharp-edged polygon): narrower trail, vertex-alpha
        // fade from its inner edge (0) to the front (1), trail fading faster than the front
        public static float FrontGain = 6f, TrailGain = 1.5f, FrontInner = 0.96f, TrailInner = 0.86f, RingLift = 0.55f, RingSoftFade = 0.5f;
        // refraction wave
        public static float DistortStrength = 0.045f, WaveHeight = 9f;
        // a ground-hugging refraction ring riding the front (OpenZ/HeatDistort mode 1 on a conforming annulus, outward normals: the
        // ground image is pushed outward as the air front passes) + a short full-area shimmer behind it (mode 0 on a conforming
        // disc). CALM SCREEN: the ring at CalmDistort x, no area shimmer.
        public static bool GroundDistort = true;
        public static float GroundRingStrength = 0.075f, GroundRingInner = 0.60f,   GroundRingOuter = 1.035f, AreaShimmerStrength = 0.014f, AreaShimmerTau = 1.1f, CalmDistort = 0.35f;
        public static float LastGroundRing { get; private set; }
        public static float CliffFadeLo = 0.9f, CliffFadeHi = 2.6f;
        /// <summary>Play proof: ring segments faded on raised ground (cliffs) so far.</summary>
        public static int RingCliffFaded { get; private set; }
        public static float LastAreaShimmer { get; private set; }
        // dust wall
        // v3b: soft low dust (m12_dust channel, 5-7.5 m, low alpha) instead of 3-4 m lit smoke balls (the gate frames' "grey discs")
        public static float DustEvery = 0.12f, DustMaxRadius = 115f; public static int DustPerRing = 22;
        // shove
        public static float PushSpeed = 13f, PushUp = 6f, PushSpin = 4f;
        /// <summary></summary>
        public static float OwnDebrisPush = 0.2f;
        public static float RobotShove = 0.65f, VehicleShove = 0.3f, RobotTilt = 11f, VehicleTilt = 4.5f, PropTilt = 7f, JoltSeconds = 1.1f;
        // camera + boom
        public static float CameraShakeNear = 1.5f, CameraShakeFar = 0.35f, BoomVolume = 1f;
        /// <summary></summary>
        public static bool BoomShake = false;

        static OpenZFortShockwave last;
        public static float LastFireTime { get; private set; } = -100f;
        public static Vector3 LastCentre { get; private set; }

        /// <summary>Front radius (m) t game seconds after the blast: the inverse of OpenZEnding.Delay.</summary>
        public static float RadiusAt(float t)
        {
            if (t <= 0f) return 4f;
            if (t <= 1.8f) { float k = 1f - t / 1.8f; return 4f + 91f * (1f - k * k * k); }
            return 95f + (t - 1.8f) * Mathf.Max(1f, OpenZEnding.WaveOuterSpeed);
        }

        /// <summary>Game seconds until the front reaches `dist` (RadiusAt inverse).</summary>
        public static float TimeAt(float dist) => OpenZEnding.Delay(dist);

        /// <summary>Starts the wave at `at`; blastTime = Time.time of the blast (NukeFx spreads its first frames), -1 = now.</summary>
        public static void Fire(Vector3 at, float blastTime = -1f)
        {
            if (!Enabled) return;
            if (last && Time.time - LastFireTime < 3f && (LastCentre - at).sqrMagnitude < 60f * 60f) return;   // fort front + back = one wave
            var go = new GameObject("OpenZFortShockwave");
            var w = go.AddComponent<OpenZFortShockwave>();
            last = w; LastFireTime = Time.time; LastCentre = at;
            w.pending = at; w.t0 = blastTime >= 0f ? Mathf.Min(blastTime, Time.time) : Time.time;
        }

        // ------------------------------------------------------------------------------------------------ level-load caches
        struct PropRef { public Transform t; public Vector3 pivot, centre; }
        static readonly List<PropRef> propCache = new List<PropRef>();
        static int propCacheScene = -1;
        static RtsCamera rtsCache;

        /// <summary>Caches the rockable DEC_ props of the open level (OpenZNukePrewarm at load; else the first wave builds it).</summary>
        public static int CacheProps()
        {
            var sc = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            if (propCacheScene == sc) return propCache.Count;
            propCache.Clear(); propCacheScene = sc;
            var env = GameObject.Find("Environment"); if (!env) return 0;
            foreach (Transform t in env.transform)
            {
                if (!t.name.StartsWith("DEC_") || !t.gameObject.activeInHierarchy || t.gameObject.isStatic) continue;
                var rs = t.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                if (b.size.x > 7f || b.size.z > 7f || b.size.y > 12f) continue;                            // same size rule as PropBreakables
                if (PropBreakables.ClaimBreak != null && PropBreakables.ClaimBreak(t.gameObject)) continue;  // NightLights owns its lamps
                propCache.Add(new PropRef { t = t, pivot = new Vector3(b.center.x, b.min.y, b.center.z), centre = b.center });
            }
            return propCache.Count;
        }

        static RtsCamera Rts() { if (!rtsCache) rtsCache = FindFirstObjectByType<RtsCamera>(); return rtsCache; }

        // ================================================================================================ state
        Vector3 centre, pending; float t0, age, radius, lastDust, nextBodyScan, boomAt, camDist; bool boomed, built, faded;
        Mesh ringMesh, waveMesh; Material frontMat, trailMat, waveMat;
        Mesh gRingMesh, areaMesh; Material gRingMat, areaMat; Vector3[] gRingV, areaV; const int GSeg = 128, GRows = 5, ARings = 7, ASeg = 96;
        const int Seg = 144, WaveSeg = 120;
        Vector3[] ringV, waveV; Color[] ringC; Vector3[] waveN; Vector2[] waveUv;
        readonly List<Rigidbody> bodies = new List<Rigidbody>(); readonly HashSet<Rigidbody> pushed = new HashSet<Rigidbody>();
        sealed class Jolt { public Transform t; public UnitView v; public float start, amp, tilt; public Vector3 dir, appliedPos, baseProp; public Quaternion appliedRot, baseRot, preRot; public Vector3 prePos; public bool prop, has; }
        readonly Dictionary<int, Jolt> jolts = new Dictionary<int, Jolt>();
        readonly List<Jolt> props = new List<Jolt>();
        readonly HashSet<int> hitViews = new HashSet<int>();
        Color dustColor; int rumbleId;
        static Mesh quad;

        void Start() => Build(pending);

        void Build(Vector3 at)
        {
            var fxp = OpenZFxPlayer.I;
            centre = new Vector3(at.x, fxp ? fxp.SurfaceY(at) : (ZGame.I ? ZGame.I.GroundY(at.x, at.z) : at.y), at.z);
            rumbleId = 91000 + (int)(t0 * 10f) % 1000;
            var g = ZGame.I;
            dustColor = g != null && g.level == "L17" ? new Color(0.62f, 0.57f, 0.52f) : OpenZFxCatalog.M12Dust.color;
            var add = Resources.Load<Material>("OpenZ/FX/M_FxAdditive"); var addShader = add && add.shader ? add.shader : Shader.Find("OpenZ/FxAdditive");
            if (addShader)
            {
                frontMat = RingMat(addShader, "ShockFront", FrontInner, 3072);
                trailMat = RingMat(addShader, "ShockTrail", TrailInner, 3071);
            }
            var dm = Resources.Load<Material>("OpenZ/FX/M_HeatDistort");
            if (dm && dm.shader && dm.shader.isSupported) { waveMat = new Material(dm) { name = "ShockWaveDistort (runtime)" }; waveMat.SetFloat("_Mode", 1f); waveMat.SetFloat("_Bright", 0.1f); }
            if (GroundDistort && dm && dm.shader && dm.shader.isSupported)
            {
                gRingMat = new Material(dm) { name = "ShockGroundDistort (runtime)" }; gRingMat.SetFloat("_Mode", 1f); gRingMat.SetFloat("_Bright", 0.06f); gRingMat.SetFloat("_Strength", 0f);
                areaMat = new Material(dm) { name = "ShockAreaShimmer (runtime)" }; areaMat.SetFloat("_Mode", 0f); areaMat.SetFloat("_Inner", 0.04f); areaMat.SetFloat("_Speed", 3.2f); areaMat.SetFloat("_Strength", 0f);
            }
            BuildMeshes();
            BuildGroundDistort();
            ScanProps();                                                                             // rigidbodies: first Update (nextBodyScan 0)
            var rts = Rts();
            var focus = rts ? rts.focus : (Camera.main ? Camera.main.transform.position : centre);
            camDist = new Vector2(focus.x - centre.x, focus.z - centre.z).magnitude;
            boomAt = Mathf.Min(TimeAt(camDist), 5f);
            built = true;
        }

        static Material RingMat(Shader s, string name, float inner, int queue)
        {
            var m = new Material(s) { name = name + " (runtime)", renderQueue = queue };
            m.SetFloat("_Shape", 4f); m.SetFloat("_UseVertexColor", 1f);                                  // v3b: the ring mesh's vertex alpha fades the band m.SetFloat("_Intensity", 1f); m.SetFloat("_Opaqueness", 0f);
            m.SetFloat("_BandInner", inner); m.SetFloat("_BandSides", 0f); m.SetFloat("_BandProfile", 1f); m.SetFloat("_RingAlpha", 1f);
            m.SetFloat("_SoftFade", RingSoftFade); m.SetFloat("_Lit", 0f);
            return m;
        }

        void BuildMeshes()
        {
            ringMesh = new Mesh { name = "ShockRing (runtime)" }; ringMesh.MarkDynamic();
            ringV = new Vector3[(Seg + 1) * 3]; ringC = new Color[ringV.Length];
            var tri = new List<int>();
            for (int i = 0; i < Seg; i++)
                for (int j = 0; j < 2; j++)
                {
                    int a0 = i * 3 + j, a1 = a0 + 1, b0 = a0 + 3, b1 = b0 + 1;
                    tri.Add(a0); tri.Add(b0); tri.Add(a1); tri.Add(a1); tri.Add(b0); tri.Add(b1);
                }
            for (int i = 0; i < ringC.Length; i++) ringC[i] = new Color(1f, 1f, 1f, (i % 3) == 0 ? 0f : (i % 3) == 1 ? 0.55f : 1f);   // v3b: inner edge 0 -> front 1
            ringMesh.vertices = ringV; ringMesh.colors = ringC; ringMesh.SetTriangles(tri, 0);
            if (frontMat) AddRenderer("ShockFront", ringMesh, frontMat);
            if (trailMat) AddRenderer("ShockTrail", ringMesh, trailMat);

            waveMesh = new Mesh { name = "ShockWave (runtime)" }; waveMesh.MarkDynamic();
            waveV = new Vector3[(WaveSeg + 1) * 2]; waveN = new Vector3[waveV.Length]; waveUv = new Vector2[waveV.Length];
            var wt = new List<int>();
            for (int i = 0; i < WaveSeg; i++) { int a = i * 2, b = a + 2; wt.Add(a); wt.Add(b); wt.Add(a + 1); wt.Add(a + 1); wt.Add(b); wt.Add(b + 1); }
            for (int i = 0; i <= WaveSeg; i++) { waveUv[i * 2] = new Vector2(i / (float)WaveSeg, 0f); waveUv[i * 2 + 1] = new Vector2(i / (float)WaveSeg, 1f); }
            waveMesh.vertices = waveV; waveMesh.normals = waveN; waveMesh.uv = waveUv; waveMesh.SetTriangles(wt, 0);
            if (waveMat) AddRenderer("ShockWaveDistort", waveMesh, waveMat);
        }

        void AddRenderer(string name, Mesh m, Material mat)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        float Ground(float x, float z)
        {
            var fxp = OpenZFxPlayer.I;
            return fxp ? fxp.SurfaceY(new Vector3(x, 0f, z)) : ZGame.I ? ZGame.I.GroundY(x, z) : centre.y;
        }

        // ================================================================================================ frame
        void Update()
        {
            if (!built) return;
            age = Time.time - t0;
            radius = RadiusAt(age);
            float k = Mathf.Clamp01(1f - radius / MaxRadius);                                         // strength left in the front
            if (!boomed && age >= boomAt) Boom();
            if (radius >= MaxRadius || age > 8f)
            {   // the front has died out: keep going only for a far camera's boom and the last jolts
                if (!faded) { faded = true; foreach (var r in GetComponentsInChildren<MeshRenderer>()) r.enabled = false; }
                if ((boomed && age > boomAt + JoltSeconds * 1.5f) || age > 10f) Finish();
                return;
            }
            UpdateRing(k);
            UpdateWave(k);
            UpdateGroundDistort(k);
            if (radius <= DustMaxRadius && age - lastDust >= DustEvery) { lastDust = age; DustWall(k); }
            if (Time.unscaledTime >= nextBodyScan) { nextBodyScan = Time.unscaledTime + 0.5f; ScanBodies(); }
            PushBodies(k);
        }

        void UpdateRing(float k)
        {
            if (!frontMat && !trailMat) return;
            float r0 = radius * (TrailInner - 0.02f), r1 = radius * 1.02f, rm = (r0 + r1) * 0.5f;
            for (int i = 0; i <= Seg; i++)
            {
                float a = i * Mathf.PI * 2f / Seg, c = Mathf.Cos(a), s = Mathf.Sin(a);
                ringV[i * 3] = P(c, s, r0); ringV[i * 3 + 1] = P(c, s, rm); ringV[i * 3 + 2] = P(c, s, r1);
                // where the ground under it rises CliffFadeLo..CliffFadeHi m over the blast floor, so it runs along the floor and dies at
                // the cliff foot instead of breaking up on the walls
                float rise = Mathf.Max(ringV[i * 3].y, Mathf.Max(ringV[i * 3 + 1].y, ringV[i * 3 + 2].y)) - RingLift - centre.y;
                float ck = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(CliffFadeLo, CliffFadeHi, rise));
                ringC[i * 3].a = 0f; ringC[i * 3 + 1].a = 0.55f * ck; ringC[i * 3 + 2].a = ck;
                if (ck < 0.5f) RingCliffFaded++;
            }
            ringMesh.vertices = ringV; ringMesh.colors = ringC; ringMesh.RecalculateBounds();
            float fadeIn = Mathf.Clamp01(age / 0.06f);
            float gf = FrontGain * Mathf.Pow(k, 1.3f) * fadeIn, gt = TrailGain * Mathf.Pow(k, 2.2f) * fadeIn;
            if (frontMat) { frontMat.SetVector("_Color", new Vector4(1f, 0.82f, 0.58f, 1f) * gf); frontMat.SetVector("_RingCenter", centre); frontMat.SetFloat("_RingRadius", radius); }
            if (trailMat) { trailMat.SetVector("_Color", new Vector4(1f, 0.34f, 0.07f, 1f) * gt); trailMat.SetVector("_RingCenter", centre); trailMat.SetFloat("_RingRadius", radius); }
        }

        Vector3 P(float c, float s, float r) { float x = centre.x + c * r, z = centre.z + s * r; return new Vector3(x, Ground(x, z) + RingLift, z); }

        void UpdateWave(float k)
        {
            if (!waveMat) return;
            for (int i = 0; i <= WaveSeg; i++)
            {
                float a = i * Mathf.PI * 2f / WaveSeg, c = Mathf.Cos(a), s = Mathf.Sin(a);
                float rb = radius * 0.99f, rt = radius * 1.03f + 1.5f;
                float xb = centre.x + c * rb, zb = centre.z + s * rb, y = Ground(xb, zb);
                waveV[i * 2] = new Vector3(xb, y + 0.1f, zb);
                waveV[i * 2 + 1] = new Vector3(centre.x + c * rt, y + WaveHeight * (0.6f + 0.4f * k), centre.z + s * rt);
                var n = new Vector3(c, 0.35f, s).normalized; waveN[i * 2] = n; waveN[i * 2 + 1] = n;
            }
            waveMesh.vertices = waveV; waveMesh.normals = waveN; waveMesh.RecalculateBounds();
            waveMat.SetFloat("_Strength", DistortStrength * Mathf.Pow(k, 0.8f) * Mathf.Clamp01(age / 0.05f) * (OzMotion.Reduced ? 0.5f : 1f));
        }

        void BuildGroundDistort()
        {
            if (gRingMat)
            {   // annulus of GRows rows from the inner (uv.y 0) to the outer edge (uv.y 1); normals point outward along the ground
                gRingMesh = new Mesh { name = "ShockGroundRing (runtime)" }; gRingMesh.MarkDynamic();
                gRingV = new Vector3[(GSeg + 1) * GRows]; var n = new Vector3[gRingV.Length]; var uv = new Vector2[gRingV.Length]; var tri = new List<int>();
                for (int i = 0; i <= GSeg; i++)
                {
                    float a = i * Mathf.PI * 2f / GSeg;
                    for (int j = 0; j < GRows; j++) { n[i * GRows + j] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)); uv[i * GRows + j] = new Vector2(i / (float)GSeg, j / (float)(GRows - 1)); }
                }
                for (int i = 0; i < GSeg; i++)
                    for (int j = 0; j < GRows - 1; j++)
                    {
                        int a0 = i * GRows + j, a1 = a0 + 1, b0 = a0 + GRows, b1 = b0 + 1;
                        tri.Add(a0); tri.Add(a1); tri.Add(b0); tri.Add(a1); tri.Add(b1); tri.Add(b0);
                    }
                gRingMesh.vertices = gRingV; gRingMesh.normals = n; gRingMesh.uv = uv; gRingMesh.SetTriangles(tri, 0);
                AddRenderer("ShockGroundDistort", gRingMesh, gRingMat);
            }
            if (areaMat)
            {   // disc: a centre + ARings rings; uv planar over the disc (the mode 0 round mask = the whole area, soft at the edge)
                areaMesh = new Mesh { name = "ShockAreaShimmer (runtime)" }; areaMesh.MarkDynamic();
                areaV = new Vector3[1 + ARings * (ASeg + 1)]; var n = new Vector3[areaV.Length]; var uv = new Vector2[areaV.Length]; var tri = new List<int>();
                uv[0] = new Vector2(0.5f, 0.5f); n[0] = Vector3.up;
                for (int r = 0; r < ARings; r++)
                    for (int i = 0; i <= ASeg; i++)
                    {
                        float a = i * Mathf.PI * 2f / ASeg, f = (r + 1) / (float)ARings; int id = 1 + r * (ASeg + 1) + i;
                        uv[id] = new Vector2(0.5f + 0.5f * f * Mathf.Cos(a), 0.5f + 0.5f * f * Mathf.Sin(a)); n[id] = Vector3.up;
                    }
                for (int i = 0; i < ASeg; i++) { tri.Add(0); tri.Add(1 + i + 1); tri.Add(1 + i); }
                for (int r = 0; r < ARings - 1; r++)
                    for (int i = 0; i < ASeg; i++)
                    {
                        int a0 = 1 + r * (ASeg + 1) + i, a1 = a0 + 1, b0 = a0 + ASeg + 1, b1 = b0 + 1;
                        tri.Add(a0); tri.Add(a1); tri.Add(b0); tri.Add(a1); tri.Add(b1); tri.Add(b0);
                    }
                areaMesh.vertices = areaV; areaMesh.normals = n; areaMesh.uv = uv; areaMesh.SetTriangles(tri, 0);
                AddRenderer("ShockAreaShimmer", areaMesh, areaMat);
            }
        }

        void UpdateGroundDistort(float k)
        {
            bool calm = OzMotion.Reduced;
            if (gRingMat && gRingMesh)
            {
                float r0 = radius * GroundRingInner, r1 = radius * GroundRingOuter + 1f;
                for (int i = 0; i <= GSeg; i++)
                {
                    float a = i * Mathf.PI * 2f / GSeg, c = Mathf.Cos(a), s = Mathf.Sin(a);
                    for (int j = 0; j < GRows; j++)
                    {
                        float rr = Mathf.Lerp(r0, r1, j / (float)(GRows - 1)), x = centre.x + c * rr, z = centre.z + s * rr;
                        gRingV[i * GRows + j] = new Vector3(x, Ground(x, z) + 0.25f, z);
                    }
                }
                gRingMesh.vertices = gRingV; gRingMesh.RecalculateBounds();
                float g = GroundRingStrength * Mathf.Pow(k, 0.7f) * Mathf.Clamp01(age / 0.05f) * (calm ? CalmDistort : 1f);
                gRingMat.SetFloat("_Strength", g); LastGroundRing = g;
            }
            if (areaMat && areaMesh)
            {
                float rd = Mathf.Max(2f, radius * GroundRingInner * 1.02f);
                areaV[0] = new Vector3(centre.x, Ground(centre.x, centre.z) + 0.3f, centre.z);
                for (int r = 0; r < ARings; r++)
                    for (int i = 0; i <= ASeg; i++)
                    {
                        float a = i * Mathf.PI * 2f / ASeg, f = (r + 1) / (float)ARings, x = centre.x + Mathf.Cos(a) * rd * f, z = centre.z + Mathf.Sin(a) * rd * f;
                        areaV[1 + r * (ASeg + 1) + i] = new Vector3(x, Ground(x, z) + 0.3f, z);
                    }
                areaMesh.vertices = areaV; areaMesh.RecalculateBounds();
                areaMat.SetFloat("_NoiseScale", Mathf.Clamp(rd / 2.2f, 6f, 70f));                          // ~4-5 m shimmer cells at any radius
                float g = calm ? 0f : AreaShimmerStrength * Mathf.Exp(-age / AreaShimmerTau) * Mathf.Clamp01(age / 0.12f);
                areaMat.SetFloat("_Strength", g); LastAreaShimmer = g;
            }
        }

        void DustWall(float k)
        {
            var fx = OpenZFxPlayer.I; var cam = Camera.main; if (!fx) return;
            float frontSpeed = age < 1.8f ? 91f * 3f * (1f - age / 1.8f) * (1f - age / 1.8f) / 1.8f : Mathf.Max(1f, OpenZEnding.WaveOuterSpeed);
            float outV = Mathf.Min(14f, 0.3f * frontSpeed + 3f);
            float phase = age * 1.7f;
            int count = Mathf.Clamp(Mathf.CeilToInt(2f * Mathf.PI * radius / 5f), DustPerRing, 60);          // v3b: overlapping puffs (~5 m apart), not discs
            for (int j = 0; j < count; j++)
            {
                float a = phase + j * Mathf.PI * 2f / count + Random.Range(-0.08f, 0.08f);
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                var p = new Vector3(centre.x + c * radius, 0f, centre.z + s * radius);
                if (cam)
                {
                    var vp = cam.WorldToViewportPoint(new Vector3(p.x, Ground(p.x, p.z), p.z));
                    if (vp.z < 0f || vp.x < -0.2f || vp.x > 1.2f || vp.y < -0.2f || vp.y > 1.2f) continue;   // off screen: no puff
                }
                if (fx.WetAt(p)) continue;                                                             // no dust on water
                p.y = Ground(p.x, p.z) + 0.5f;
                var v = new Vector3(c, 0f, s) * outV * Random.Range(0.8f, 1.1f) + Vector3.up * Random.Range(0.8f, 1.8f);
                float size = Random.Range(5f, 7.5f) * (0.6f + 0.4f * k);
                fx.EmitChannel("m12_dust", p, v, Random.Range(1.4f, 2.0f), size, dustColor, 0.22f + 0.18f * k);
            }
        }

        // ------------------------------------------------------------------------------------------------ shove
        void ScanBodies()
        {
            bodies.Clear();
            foreach (var rb in FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
                if (rb && !rb.isKinematic && !pushed.Contains(rb)) bodies.Add(rb);
        }

        void PushBodies(float k)
        {
            float r2 = radius * radius;
            for (int i = bodies.Count - 1; i >= 0; i--)
            {
                var rb = bodies[i];
                if (!rb || rb.isKinematic) { bodies.RemoveAt(i); continue; }
                var d = rb.worldCenterOfMass - centre; d.y = 0f;
                if (d.sqrMagnitude > r2) continue;
                bodies.RemoveAt(i); pushed.Add(rb);
                float f = Mathf.Pow(Mathf.Clamp01(1f - d.magnitude / MaxRadius), 0.7f);
                // already has its collapse launch; the full shove threw it 30-40 m onto the L02 cliff plateau. It only gets a nudge.
                if (rb.GetComponent<BuildingBreaker.Piece>() || rb.GetComponent<BuildingBreaker.BigPart>()) f *= OwnDebrisPush;
                var dir = d.sqrMagnitude > 1e-4f ? d.normalized : Random.insideUnitSphere.normalized;
                rb.WakeUp();
                rb.AddForce(dir * (PushSpeed * f) + Vector3.up * (PushUp * f), ForceMode.VelocityChange);
                rb.AddTorque(Random.insideUnitSphere * (PushSpin * f), ForceMode.VelocityChange);
            }
        }

        void ScanProps()
        {
            CacheProps();
            foreach (var pr in propCache)
            {
                var t = pr.t; if (!t || !t.gameObject.activeInHierarchy) continue;                        // broken since (PropBreakables hides them)
                var flat = new Vector3(pr.centre.x - centre.x, 0f, pr.centre.z - centre.z);
                if (flat.magnitude > MaxRadius) continue;
                props.Add(new Jolt { t = t, prop = true, baseProp = t.position, baseRot = t.rotation, dir = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward,
                                     start = -1f, amp = 0f, tilt = PropTilt * Mathf.Clamp01(1f - flat.magnitude / MaxRadius), prePos = pr.pivot });
            }
        }

        void LateUpdate()
        {
            if (!built) return;
            // unit views: the front reached them -> jolt (added on top of UnitView's pose, removed if UnitView did not re-pose)
            var g = ZGame.I;
            if (g != null && radius < MaxRadius)
            {
                float r2 = radius * radius;
                foreach (var kv in g.Views)
                {
                    var v = kv.Value; if (!v || hitViews.Contains(kv.Key)) continue;
                    var d = v.transform.position - centre; d.y = 0f;
                    if (d.sqrMagnitude > r2) continue;
                    hitViews.Add(kv.Key);
                    if (v.Dying || OpenZEnding.Holds(kv.Key) || v.Obj == null) continue;
                    float f = Mathf.Pow(Mathf.Clamp01(1f - d.magnitude / MaxRadius), 0.8f);
                    bool robot = v.Obj.Kind == OpenZ.Sim.ZKind.Robot;
                    jolts[kv.Key] = new Jolt { t = v.transform, v = v, start = Time.time, dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward,
                                               amp = (robot ? RobotShove : VehicleShove) * f, tilt = (robot ? RobotTilt : VehicleTilt) * f };
                }
            }
            List<int> done = null;
            foreach (var kv in jolts)
            {
                var j = kv.Value;
                if (!j.t || !j.v || j.v.Dying) { if (j.t && j.has) Undo(j); (done ??= new List<int>()).Add(kv.Key); continue; }
                if (j.has && (j.t.position - j.appliedPos).sqrMagnitude < 1e-10f && Quaternion.Angle(j.t.rotation, j.appliedRot) < 0.01f) Undo(j);   // not re-posed this frame
                float s = Pulse(Time.time - j.start);
                if (Time.time - j.start > JoltSeconds) { j.has = false; (done ??= new List<int>()).Add(kv.Key); continue; }
                j.prePos = j.t.position; j.preRot = j.t.rotation;
                var axis = Vector3.Cross(Vector3.up, j.dir);
                j.t.position = j.prePos + j.dir * (j.amp * s);
                j.t.rotation = Quaternion.AngleAxis(j.tilt * s, axis) * j.preRot;
                j.appliedPos = j.t.position; j.appliedRot = j.t.rotation; j.has = true;
            }
            if (done != null) foreach (var id in done) jolts.Remove(id);
            // props rock on their base
            for (int i = 0; i < props.Count; i++)
            {
                var p = props[i]; if (!p.t) continue;
                if (p.start < 0f)
                {
                    var d = p.prePos - centre; d.y = 0f;
                    if (d.sqrMagnitude > radius * radius) continue;
                    p.start = Time.time;
                }
                float el = Time.time - p.start;
                if (el > JoltSeconds * 1.4f) { if (p.has) { p.t.SetPositionAndRotation(p.baseProp, p.baseRot); p.has = false; } continue; }
                float s = Pulse(el * 0.8f);
                var q = Quaternion.AngleAxis(p.tilt * s, Vector3.Cross(Vector3.up, p.dir));
                p.t.SetPositionAndRotation(p.prePos + q * (p.baseProp - p.prePos), q * p.baseRot);         // pivot = bottom centre of the prop
                p.has = true;
            }
        }

        static void Undo(Jolt j) { j.t.SetPositionAndRotation(j.prePos, j.preRot); j.has = false; }

        /// <summary>Shove pulse: out in 0.07 s, then a damped spring back (1 -> 0 over ~1 s, one small overshoot).</summary>
        static float Pulse(float t)
        {
            if (t < 0f) return 0f;
            if (t < 0.07f) return t / 0.07f;
            float u = t - 0.07f;
            return Mathf.Exp(-u * 4.2f) * Mathf.Cos(u * 9f);
        }

        // ------------------------------------------------------------------------------------------------ camera + boom
        void Boom()
        {
            boomed = true;
            float f = Mathf.Clamp01(camDist / MaxRadius);
            if (ShakeScale > 0f && BoomShake)
            {
                var rts = Rts();
                if (rts) rts.Shake(Mathf.Lerp(CameraShakeNear, CameraShakeFar, f) * ShakeScale);
                var sh = OpenZFxPlayer.I ? OpenZFxPlayer.I.Shake : null;
                if (sh != null)
                {
                    var focus = rts ? rts.focus : centre;
                    sh.Impact(new Vector3(focus.x, Ground(focus.x, focus.z), focus.z), OpenZFxCatalog.ShakeCollapse * (1f - 0.6f * f));
                    StartCoroutine(Rumble(sh, focus, 1.6f * (1f - 0.6f * f)));
                }
            }
            var ca = CombatAudio.I; if (!ca || BoomVolume <= 0f) return;
            var at = ca.ListenerRig ? ca.ListenerRig.position : (Camera.main ? Camera.main.transform.position : centre);
            float vol = BoomVolume * (1f - 0.5f * f);
            Deep(ca, ca.Clip("heavy") ?? ca.Clip("explosion-v2-20260909"), at, vol, 0.5f);
            Deep(ca, Resources.Load<AudioClip>("OpenZ/Audio/Sfx/sd_nuke_shockwave"), at, vol * 0.8f, 0.8f);
        }

        System.Collections.IEnumerator Rumble(OpenZFxShake sh, Vector3 at, float s)
        {
            float start = Time.time;
            while (Time.time - start < 0.9f && sh != null)
            {
                sh.SetRumble(rumbleId, at, s * (1f - (Time.time - start) / 0.9f) * ShakeScale);
                yield return null;
            }
            sh?.SetRumble(rumbleId, at, 0f);
        }

        void Deep(CombatAudio ca, AudioClip clip, Vector3 at, float vol, float pitch)
        {
            if (!clip) return;
            var go = new GameObject("ShockBoom_" + clip.name); go.transform.position = at;
            var a = go.AddComponent<AudioSource>(); a.playOnAwake = false; a.dopplerLevel = 0f; a.priority = 8; a.spread = 90f;
            ca.Configure(a, null, CombatAudio.Profile.Broad, 200f);
            a.clip = clip; a.pitch = pitch;
            a.volume = Mathf.Clamp01(vol * ca.volume * CombatAudio.BusGain(CombatAudio.Bus.Explosions) * 2.2f * ca.DuckOf(CombatAudio.Bus.Explosions));
            a.Play();
            Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.3f);
        }

        void Finish()
        {
            foreach (var kv in jolts) if (kv.Value.t && kv.Value.has) Undo(kv.Value);
            foreach (var p in props) if (p.t && p.has) p.t.SetPositionAndRotation(p.baseProp, p.baseRot);
            jolts.Clear(); props.Clear();
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            foreach (var p in props) if (p.t && p.has) p.t.SetPositionAndRotation(p.baseProp, p.baseRot);
            foreach (var m in new[] { frontMat, trailMat, waveMat, gRingMat, areaMat }) if (m) Destroy(m);
            if (ringMesh) Destroy(ringMesh); if (waveMesh) Destroy(waveMesh); if (gRingMesh) Destroy(gRingMesh); if (areaMesh) Destroy(areaMesh);
            if (last == this) last = null;
        }

        /// <summary>Unit quad in XY (uv 0-1), facing -Z: the heat-shimmer billboards (NukeFx, OpenZHeavyShells).</summary>
        public static Mesh Quad()
        {
            if (quad) return quad;
            quad = new Mesh { name = "OZ_ShimmerQuad" };
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            quad.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            quad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            quad.RecalculateBounds();
            return quad;
        }

        /// <summary>an editor script: OpenZ.OpenZFortShockwave.Status()</summary>
        public static string Status()
        {
            var s = last ? $"shockwave: age {last.age:F2} s, radius {last.radius:F1} m, dist to camera {last.camDist:F0} m (boom at {last.boomAt:F2} s, {(last.boomed ? "played" : "pending")}), " +
                           $"bodies pushed {last.pushed.Count}, views jolted {last.jolts.Count}/{last.hitViews.Count}, props {last.props.Count}"
                         : $"shockwave: none (last fired {Time.time - LastFireTime:F1} s ago)";
            Debug.Log("[OpenZ] " + s);
            return s;
        }
    }
}
