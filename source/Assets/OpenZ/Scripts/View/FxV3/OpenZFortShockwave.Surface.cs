// OpenZ (GPL-3.0) - version 1.1 item 215
// the fort blast's wave runs OVER the ground.
// Why it went inside: the ring was 144 straight pieces of 3 points across its 15 m width, laid on the effects' ground cache (which
// stops 2 m past the map's edge), and it faded out wherever the ground under it rose 0.9 to 2.6 m over the blast's floor (made so
// the band would not paint white slabs up the cliff faces). So it died at the foot of every ridge, never ran up a slope or over a
// top, and past the map's edge it lay inside the rims.
// Now (SurfaceRing on): per direction (SurfaceAngles round the blast) the ground is read every SurfaceStep metres outward as the
// front needs it (OzBlastGround: the cache in the map, the ground collider on Level 16's apron, nothing past its end) and the
// distance ALONG the ground is summed (a rise under SurfaceDead per step counts as flat; the lag a climb gives the front fades again
// over SurfaceRelax metres, so banks and ditches leave it round while a ridge holds it back as it climbs). The front stands where that distance equals
// the wave's radius (RadiusAt: the same clock): on flat ground exactly where it stood, on a slope it climbs at the wave's speed, it
// runs over a top and down the far side, and at the end of the level's ground it fades out. The bright front and the orange trail
// are two meshes of rows across the band (the band's brightness profile, its bright leading edge and the trail's fade, carried by
// the rows as vertex alpha), lifted off the ground along the slope's normal (more on a steep face, so the band stands in front of
// it), and drawn with the ground band test (OpenZ/FxAdditive _GroundBand: the visible ground up to 0.6 x RingGroundBand in front of
// the band still receives it, fading out to 1 x), so a fold of the ground between two rows never cuts it. The cliff fade is not
// needed any more (the band crawls up a face at the wave's speed instead of lighting all of it at once). The refraction wall, the
// ground shimmer ring, the area shimmer and the dust wall follow the same front (the dust puffs also take their height from the level's
// ground past the map's edge, DustHeight). Robots, bodies and props are shoved as before (by
// the flat distance, unchanged). SurfaceLag gives the trees of the blast (OzTreeFall) the same front.
// Off (SurfaceRing false before the blast) = the ring as before, for the before / after pictures.
// Play check: return OpenZ.OpenZFortShockwave.SurfaceStatus();
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    public sealed partial class OpenZFortShockwave
    {
        /// <summary>On = the wave follows the ground (item 215); off = the flat ring with the cliff fade, as before.</summary>
        public static bool SurfaceRing = true;
        /// <summary>Directions round the blast, metres between two ground points outward, the rise per step that still counts as flat (m),
        /// the extra lift on a steep face (x the ring's lift at a vertical face), the ground band of the front and the trail (m of eye
        /// depth; 0 = the plain depth test), and the rows across the front and across the trail.</summary>
        public static int SurfaceAngles = 240;
        public static float SurfaceStep = 0.5f, SurfaceDead = 0.03f, SlopeLift = 0.8f, RingGroundBand = 2f;
        /// <summary>The metres over which the front's lag from a climb fades again (small banks and ditches leave the front round), and the
        /// most it catches up per metre (a share: the front along the ground always moves on).</summary>
        public static float SurfaceRelax = 10f, SurfaceCatchUp = 0.6f;
        public static int FrontRowsN = 5, TrailRowsN = 8;
        /// <summary>For the checks: ground points read, directions that reached the end of the level's ground, the most points read in
        /// one frame and the most milliseconds one frame spent reading them; the share of directions the front is drawn on now.</summary>
        public static int SurfaceSamples, SurfaceEnded, SurfaceMostSamplesInFrame;
        public static float SurfaceMsMost, SurfaceShownShare = 1f;
        /// <summary>For the checks: the most directions whose front stood on raised ground (0.9 m or more over the blast's floor) in one
        /// frame, and the highest the front climbed (m over the floor).</summary>
        public static int SurfaceUpMost; public static float SurfaceTopMost;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSurface() { SurfaceSamples = SurfaceEnded = SurfaceMostSamplesInFrame = SurfaceUpMost = 0; SurfaceMsMost = SurfaceTopMost = 0f; SurfaceShownShare = 1f; }

        // the ground profile: per direction a its heights pH and the distance along the ground pS, every pStep metres outward
        int pN, pMax; float pStep, pAng; float[] pH, pS, pG, pC, pSn; int[] pCount; byte[] pEnd; bool pReady;   // pG: the slope outward; pEnd: 1 = past the ground, 2 = full
        Mesh sFront, sTrail; Vector3[] sFV, sTV; Color[] sFC, sTC; float[] sFU, sTU; bool sMeshes;
        int sampFrame = -1, sampN; readonly System.Diagnostics.Stopwatch sampSw = new System.Diagnostics.Stopwatch();

        bool SurfaceReady()
        {
            if (pReady) return true;
            if (!built) return false;
            pN = Mathf.Clamp(SurfaceAngles, 32, 1024); pStep = Mathf.Clamp(SurfaceStep, 0.25f, 2f); pAng = Mathf.PI * 2f / pN;
            pMax = Mathf.CeilToInt((MaxRadius * 1.08f + 8f) / pStep) + 2;
            pH = new float[pN * pMax]; pS = new float[pN * pMax]; pG = new float[pN * pMax]; pCount = new int[pN]; pEnd = new byte[pN]; pC = new float[pN]; pSn = new float[pN];
            for (int a = 0; a < pN; a++) { pC[a] = Mathf.Cos(a * pAng); pSn[a] = Mathf.Sin(a * pAng); }
            pReady = true;
            return true;
        }

        void Clock(bool start)
        {
            if (Time.frameCount != sampFrame) { sampFrame = Time.frameCount; sampSw.Reset(); sampN = 0; }
            if (start) { sampSw.Start(); return; }
            sampSw.Stop();
            float ms = (float)sampSw.Elapsed.TotalMilliseconds; if (ms > SurfaceMsMost) SurfaceMsMost = ms;
            if (sampN > SurfaceMostSamplesInFrame) SurfaceMostSamplesInFrame = sampN;
        }

        /// <summary>Reads the ground of direction a outward until the distance along it reaches sNeed and the flat distance xNeed.</summary>
        void Extend(int a, float sNeed, float xNeed)
        {
            if (pEnd[a] != 0) return;
            int n = pCount[a], o = a * pMax;
            if (n >= 2 && pS[o + n - 1] >= sNeed && (n - 1) * pStep >= xNeed) return;
            Clock(true);
            while (n < pMax && (n < 2 || pS[o + n - 1] < sNeed || (n - 1) * pStep < xNeed))
            {
                float x = n * pStep;
                if (!OzBlastGround.Y(centre.x + pC[a] * x, centre.z + pSn[a] * x, out float y)) { pEnd[a] = 1; SurfaceEnded++; break; }
                if (n == 0) { pS[o] = 0f; pG[o] = 0f; }
                else
                {   // the lag behind the flat front grows with every rise and fall of the ground and fades again over SurfaceRelax metres
                    float dh = Mathf.Max(0f, Mathf.Abs(y - pH[o + n - 1]) - SurfaceDead), lag = pS[o + n - 1] - (n - 1) * pStep;
                    lag = Mathf.Max(0f, lag + Mathf.Sqrt(pStep * pStep + dh * dh) - pStep - Mathf.Min(lag * pStep / Mathf.Max(1f, SurfaceRelax), SurfaceCatchUp * pStep));
                    pS[o + n] = x + lag; pG[o + n] = (y - pH[o + n - 1]) / pStep;
                }
                pH[o + n] = y; n++; SurfaceSamples++; sampN++;
            }
            pCount[a] = n; if (n >= pMax && pEnd[a] == 0) pEnd[a] = 2;
            Clock(false);
        }

        /// <summary>Direction a at the distance s along the ground: its flat distance x and height h; false past the end of the ground.</summary>
        bool At(int a, float s, out float x, out float h) { int lo = -1; return At(a, s, ref lo, out x, out h); }

        /// <summary>As At; lo = the point found last on this direction (-1 = none): a larger s is found by stepping on from it.</summary>
        bool At(int a, float s, ref int lo, out float x, out float h)
        {
            Extend(a, s + pStep, 0f);
            int n = pCount[a], o = a * pMax;
            if (n < 2) { x = 0f; h = centre.y; return false; }
            if (s >= pS[o + n - 1]) { x = (n - 1) * pStep; h = pH[o + n - 1]; lo = n - 2; return false; }
            if (lo < 0 || lo > n - 2 || pS[o + lo] > s)
            {
                lo = 0; int hi = n - 1;
                while (hi - lo > 1) { int m = (lo + hi) >> 1; if (pS[o + m] <= s) lo = m; else hi = m; }
            }
            else while (lo < n - 2 && pS[o + lo + 1] <= s) lo++;
            float f = (s - pS[o + lo]) / Mathf.Max(1e-5f, pS[o + lo + 1] - pS[o + lo]);
            x = (lo + f) * pStep; h = Mathf.Lerp(pH[o + lo], pH[o + lo + 1], f);
            return true;
        }

        /// <summary>The ground's normal in direction a at flat distance x, from its slope outward (the segment there and its two
        /// neighbours): a face the wave climbs turns the normal towards the blast, so the band is lifted off that face.</summary>
        Vector3 SurfNormal(int a, float x)
        {
            int n = pCount[a], o = a * pMax; if (n < 3) return Vector3.up;
            int i = Mathf.Clamp((int)(x / pStep) + 1, 1, n - 1);
            float g = 0.5f * pG[o + i] + 0.25f * pG[o + Mathf.Max(1, i - 1)] + 0.25f * pG[o + Mathf.Min(n - 1, i + 1)];
            return new Vector3(-g * pC[a], 1f, -g * pSn[a]).normalized;
        }

        Vector3 RowPoint(int a, float s, float lift, bool alongNormal, out bool ok)
        {
            ok = At(a, s, out float x, out float h);
            var p = new Vector3(centre.x + pC[a] * x, h, centre.z + pSn[a] * x);
            if (lift != 0f)
            {
                if (alongNormal) { var n = SurfNormal(a, x); p += n * (lift * (1f + SlopeLift * (1f - n.y))); }
                else p.y += lift;
            }
            return p;
        }

        /// <summary>The point of the front at angle ang (radians, x towards +x, turning towards +z) at the distance s along the ground.</summary>
        Vector3 FrontAt(float ang, float s, float lift, bool alongNormal, out bool ok)
        {
            float fa = Mathf.Repeat(ang, Mathf.PI * 2f) / pAng; int a0 = Mathf.FloorToInt(fa); float f = fa - a0; a0 %= pN; int a1 = (a0 + 1) % pN;
            var p0 = RowPoint(a0, s, lift, alongNormal, out bool ok0);
            if (f < 1e-3f) { ok = ok0; return p0; }
            var p1 = RowPoint(a1, s, lift, alongNormal, out bool ok1);
            ok = ok0 && ok1;
            return Vector3.LerpUnclamped(p0, p1, f);
        }

        float AlongX(int a, float x)
        {
            Extend(a, 0f, x + pStep);
            int n = pCount[a], o = a * pMax; float fi = x / pStep; int i = (int)fi;
            if (n < 2 || i >= n - 1) return -1f;
            return Mathf.Lerp(pS[o + i], pS[o + i + 1], fi - i);
        }

        /// <summary>OzTreeFall: seconds the front along the ground reaches p after the flat front (0 on flat ground, before the wave is
        /// built, past the end of the ground, or with SurfaceRing off).</summary>
        public static float SurfaceLag(Vector3 p)
        {
            var w = last;
            if (!SurfaceRing || !w || !w.SurfaceReady()) return 0f;
            float dx = p.x - w.centre.x, dz = p.z - w.centre.z, d = Mathf.Sqrt(dx * dx + dz * dz);
            if (d < 1f) return 0f;
            float fa = Mathf.Repeat(Mathf.Atan2(dz, dx), Mathf.PI * 2f) / w.pAng; int a0 = Mathf.FloorToInt(fa); float f = fa - a0; a0 %= w.pN; int a1 = (a0 + 1) % w.pN;
            float s0 = w.AlongX(a0, d), s1 = w.AlongX(a1, d);
            if (s0 < 0f || s1 < 0f) return 0f;
            float s = Mathf.Lerp(s0, s1, f);
            return s > d + 0.05f ? Mathf.Max(0f, TimeAt(s) - TimeAt(d)) : 0f;
        }

        // ------------------------------------------------------------------------------------------------ the ring
        static float[] Rows(float inner, int n)
        {
            n = Mathf.Max(2, n); var u = new float[n];
            for (int i = 0; i < n; i++) u[i] = inner + (1f - inner) * i / (n - 1);
            return u;
        }

        Mesh Grid(string name, int rows, out Vector3[] v, out Color[] c)
        {
            var m = new Mesh { name = name }; m.MarkDynamic();
            v = new Vector3[(pN + 1) * rows]; c = new Color[v.Length];
            var tri = new int[pN * (rows - 1) * 6]; int k = 0;
            for (int a = 0; a < pN; a++)
                for (int j = 0; j < rows - 1; j++)
                {
                    int a0 = a * rows + j, a1 = a0 + 1, b0 = a0 + rows, b1 = b0 + 1;
                    tri[k++] = a0; tri[k++] = b0; tri[k++] = a1; tri[k++] = a1; tri[k++] = b0; tri[k++] = b1;
                }
            for (int i = 0; i < c.Length; i++) c[i] = new Color(1f, 1f, 1f, 0f);
            m.vertices = v; m.colors = c; m.triangles = tri;
            return m;
        }

        void SurfaceMeshes()
        {
            if (sMeshes) return;
            sMeshes = true;
            sFU = Rows(FrontInner, FrontRowsN); sTU = Rows(TrailInner, TrailRowsN);
            sFront = Grid("ShockFrontSurface (runtime)", sFU.Length + 2, out sFV, out sFC);
            sTrail = Grid("ShockTrailSurface (runtime)", sTU.Length + 2, out sTV, out sTC);
            var f = transform.Find("ShockFront"); if (f && f.TryGetComponent<MeshFilter>(out var ff)) ff.sharedMesh = sFront;
            var t = transform.Find("ShockTrail"); if (t && t.TryGetComponent<MeshFilter>(out var tf)) tf.sharedMesh = sTrail;
            bool depth = true;
            var urp = GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (urp) depth = urp.supportsCameraDepthTexture;
            foreach (var m in new[] { frontMat, trailMat })
            {
                if (!m) continue;
                // the band's shape now lives in the rows (vertex alpha): the shader's own band covers the whole mesh
                m.SetFloat("_BandInner", 0f); m.SetFloat("_BandProfile", 0f); m.SetFloat("_BandSides", 0f); m.SetFloat("_RingRadius", MaxRadius * 4f);
                if (depth && RingGroundBand > 0f) { m.SetFloat("_GroundBand", RingGroundBand); m.SetFloat("_ZTest", (float)CompareFunction.Always); m.SetFloat("_SoftFade", 0f); }
            }
        }

        static float VtxA(float u, float r0, float rm, float r1) => u <= r0 ? 0f : u <= rm ? 0.55f * (u - r0) / (rm - r0) : u <= r1 ? 0.55f + 0.45f * (u - rm) / (r1 - rm) : 1f;
        static float BandA(float u, float inner)
        {
            if (u < inner - 1e-4f || u > 1f + 1e-4f) return 0f;
            float f = Mathf.Clamp01((u - inner) / Mathf.Max(1e-3f, 1f - inner));
            return 0.3f + 0.7f * f * f;
        }

        /// <summary>One ring mesh: rows u (shares of the radius along the ground) plus a zero row just inside and just outside, so the
        /// band's edges stay sharp; the brightness of the old band (its profile x the old rows' vertex alpha 0 / 0.55 / 1) per row.</summary>
        int FillRing(Vector3[] v, Color[] c, float[] u, float inner, float R, float eps, bool count)
        {
            int rows = u.Length + 2, shown = 0, up = 0;
            float r0 = TrailInner - 0.02f, r1 = 1.02f, rm = (r0 + r1) * 0.5f;
            for (int a = 0; a <= pN; a++)
            {
                int aa = a == pN ? 0 : a, lo = -1;
                for (int j = 0; j < rows; j++)
                {
                    float uu = j == 0 ? u[0] - eps : j == rows - 1 ? u[u.Length - 1] + eps : u[j - 1];
                    bool ok = At(aa, uu * R, ref lo, out float x, out float h);
                    var n = SurfNormal(aa, x); float lift = RingLift * (1f + SlopeLift * (1f - n.y));
                    v[a * rows + j] = new Vector3(centre.x + pC[aa] * x, h, centre.z + pSn[aa] * x) + n * lift;
                    c[a * rows + j] = new Color(1f, 1f, 1f, (j == 0 || j == rows - 1 || !ok) ? 0f : BandA(uu, inner) * VtxA(uu, r0, rm, r1));
                    if (count && a < pN && j == rows - 2 && ok)
                    {
                        shown++; float rise = h - centre.y;
                        if (rise > 0.9f) up++;
                        if (rise > SurfaceTopMost) SurfaceTopMost = rise;
                    }
                }
            }
            if (count && up > SurfaceUpMost) SurfaceUpMost = up;
            return shown;
        }

        /// <summary>UpdateRing's replacement while SurfaceRing is on (false = not ready: the old ring draws this frame).</summary>
        bool SurfaceRingUpdate(float k)
        {
            if (!frontMat && !trailMat) return true;
            if (!SurfaceReady()) return false;
            SurfaceMeshes();
            float R = radius, eps = Mathf.Max(0.12f / Mathf.Max(R, 1f), 0.0015f);
            int shown = FillRing(sFV, sFC, sFU, FrontInner, R, eps, true);
            FillRing(sTV, sTC, sTU, TrailInner, R, eps, false);
            sFront.vertices = sFV; sFront.colors = sFC; sFront.RecalculateBounds();
            sTrail.vertices = sTV; sTrail.colors = sTC; sTrail.RecalculateBounds();
            SurfaceShownShare = shown / (float)pN;
            float fadeIn = Mathf.Clamp01(age / 0.06f);
            float gf = FrontGain * Mathf.Pow(k, 1.3f) * fadeIn, gt = TrailGain * Mathf.Pow(k, 2.2f) * fadeIn;
            if (frontMat) { frontMat.SetVector("_Color", new Vector4(1f, 0.82f, 0.58f, 1f) * gf); frontMat.SetVector("_RingCenter", centre); }
            if (trailMat) { trailMat.SetVector("_Color", new Vector4(1f, 0.34f, 0.07f, 1f) * gt); trailMat.SetVector("_RingCenter", centre); }
            return true;
        }

        /// <summary>UpdateWave's replacement: the refraction wall stands on the front along the ground.</summary>
        bool SurfaceWaveUpdate(float k)
        {
            if (!waveMat) return true;
            if (!SurfaceReady()) return false;
            float lean = radius * 0.04f + 1.5f, top = WaveHeight * (0.6f + 0.4f * k);
            for (int i = 0; i <= WaveSeg; i++)
            {
                float a = i * Mathf.PI * 2f / WaveSeg, c = Mathf.Cos(a), s = Mathf.Sin(a);
                var b = FrontAt(a, radius * 0.99f, 0f, false, out bool ok);
                waveV[i * 2] = b + Vector3.up * 0.1f;
                waveV[i * 2 + 1] = ok ? new Vector3(b.x + c * lean, b.y + top, b.z + s * lean) : waveV[i * 2];   // past the end of the ground: no wall
                var n = new Vector3(c, 0.35f, s).normalized; waveN[i * 2] = n; waveN[i * 2 + 1] = n;
            }
            waveMesh.vertices = waveV; waveMesh.normals = waveN; waveMesh.RecalculateBounds();
            waveMat.SetFloat("_Strength", DistortStrength * Mathf.Pow(k, 0.8f) * Mathf.Clamp01(age / 0.05f) * (OzMotion.Reduced ? 0.5f : 1f));
            return true;
        }

        /// <summary>UpdateGroundDistort's replacement: the shimmer ring and the area shimmer lie on the ground the front ran over.</summary>
        bool SurfaceGroundDistortUpdate(float k)
        {
            if (!SurfaceReady()) return false;
            bool calm = OzMotion.Reduced;
            if (gRingMat && gRingMesh)
            {
                float r0 = radius * GroundRingInner, r1 = radius * GroundRingOuter + 1f;
                for (int i = 0; i <= GSeg; i++)
                {
                    float a = i * Mathf.PI * 2f / GSeg;
                    for (int j = 0; j < GRows; j++) gRingV[i * GRows + j] = FrontAt(a, Mathf.Lerp(r0, r1, j / (float)(GRows - 1)), 0.25f, true, out _);
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
                        float a = i * Mathf.PI * 2f / ASeg, f = (r + 1) / (float)ARings;
                        areaV[1 + r * (ASeg + 1) + i] = FrontAt(a, rd * f, 0.3f, false, out _);
                    }
                areaMesh.vertices = areaV; areaMesh.RecalculateBounds();
                areaMat.SetFloat("_NoiseScale", Mathf.Clamp(rd / 2.2f, 6f, 70f));
                float g = calm ? 0f : AreaShimmerStrength * Mathf.Exp(-age / AreaShimmerTau) * Mathf.Clamp01(age / 0.12f);
                areaMat.SetFloat("_Strength", g); LastAreaShimmer = g;
            }
            return true;
        }

        /// <summary>DustWall: the puff at angle a stands on the front along the ground (p's x, z); false = past the end of the ground.</summary>
        bool DustOnSurface(float a, ref Vector3 p)
        {
            if (!SurfaceReady()) return true;
            var q = FrontAt(a, radius, 0f, false, out bool ok);
            if (!ok) return false;
            p.x = q.x; p.z = q.z;
            return true;
        }

        /// <summary>DustWall, after the puff's height line: the puff stands 0.5 m over the level's ground past the map's edge too (the effects'
        /// cache answers its edge height out there, so on Level 16's rims the puffs sat inside them); false = past the end of the ground
        /// (no puff).</summary>
        bool DustHeight(ref Vector3 p)
        {
            if (!OzBlastGround.Y(p.x, p.z, out float y)) return false;
            p.y = y + 0.5f;
            return true;
        }

        void SurfaceDestroy()
        {
            if (sFront) Destroy(sFront);
            if (sTrail) Destroy(sTrail);
        }

        /// <summary>Play check: the wave over the ground in one line.</summary>
        public static string SurfaceStatus()
        {
            var w = last;
            string s = $"wave over the ground: {(SurfaceRing ? "on" : "OFF (the old flat ring)")}, {SurfaceAngles} directions, a ground point every {SurfaceStep:0.##} m, ground band {RingGroundBand:0.#} m; " +
                       $"ground points read {SurfaceSamples} (rays past the map {OzBlastGround.Rays}), directions that reached the end of the level's ground {SurfaceEnded}, " +
                       $"the most read in one frame {SurfaceMostSamplesInFrame} ({SurfaceMsMost:0.00} ms); the most directions on raised ground at once {SurfaceUpMost}, the highest the front climbed {SurfaceTopMost:0.0} m";
            if (w && w.pReady && w.radius < MaxRadius)
            {
                int up = 0, end = 0; float top = 0f;
                for (int a = 0; a < w.pN; a++)
                {
                    if (!w.At(a, w.radius, out _, out float h)) { end++; continue; }
                    float rise = h - w.centre.y; if (rise > 0.9f) up++; if (rise > top) top = rise;
                }
                s += $"; now: radius {w.radius:0.0} m, the front drawn on {(w.pN - end) * 100f / w.pN:0} % of its directions ({up} on raised ground, {end} past the end of the ground), its highest point {top:0.0} m over the blast's floor";
            }
            return s;
        }
    }
}
