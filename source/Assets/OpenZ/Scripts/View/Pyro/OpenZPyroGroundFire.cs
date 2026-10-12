using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// Scaled from its 5.4 m napalm pool to a flamer patch of 1.3-2.6 m (readable at the RTS zoom):
    /// <list type="bullet">
    /// <item>the burning surface (v3NapalmSurface): a seeded three-lobe footprint, near-black char, hot flowing fBm spots, glowing
    /// cracks and an HDR orange heart, emissive x5.2, diffuse x (1 - a); drawn as a ground-conforming grid 4 cm over the terrain
    /// the light budget gives it no real light);</item>
    /// <item>upright flame tongues (NAPALM_POINT_VERTEX / FRAGMENT): camera-right x world-up quads rooted in the ground, each with
    /// its own life cycle (fract(seed + t speed .52)), height mix(1.1, 3.6) x the patch scale, sway, feathered tongue shape,
    /// HDR yellow core; embers rising 1.55 life^2 and shrinking;</item>
    /// <item>fade in .25 s, out over the last .75 s, rise .55 -> 1, flicker .82 + .14 sin(1.85 t) + .04 sin(7.9 t) (update());</item>
    /// <item>dark smoke every .5 s off the patch ('dark', v (.12, 1.6, .06), 1.6 s, 1.1).</item>
    /// </list>
    /// A new hit within reach of a live patch refreshes it and lets it grow (up to MaxRadius) instead of stacking patches.
    /// </summary>
    public sealed class OpenZPyroGroundFire
    {
        public const int MaxPatches = 24, Flames = 26, Embers = 6, PerPatch = Flames + Embers;
        /// <summary>Single flame tongues outside any patch (the few flames on and beside a burning wreck): their owner hands them in
        /// every frame (Loose), they are drawn in the same mesh as the patches' tongues and are gone the frame the owner stops.</summary>
        public const int MaxLoose = 192;   // 96 for burning wrecks as before, up to 72 for the ground fires of burning ruins (OzRuinFires), the rest spare
        public const float MinRadius = 4.0f, MaxRadius = 5.4f;
        public const float Life = 7f, NapalmRadius = 5.4f;
        /// <summary>
        /// A patch is pinned where it was lit (no drift, no growth: a refresh only extends its life),
        /// fades in over FadeIn s and out over FadeOut s (was .25 / .75: popped in and out).</summary>
        public const float FadeIn = 0.6f, FadeOut = 1.6f;
        /// <summary>Off = the old refresh (drift 12 % toward the new hit, grow 12 cm).</summary>
        public static bool Pinned = true;

        public sealed class Patch
        {
            public bool active; public int id; public Vector3 c, builtC; public float r, builtR, start, life, seed, lastSmoke, lastRefresh, fadeZ, fadeX, rise;
            public Mesh surf; public readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            public readonly Vector2[] off = new Vector2[PerPatch]; public readonly float[] tSeed = new float[PerPatch], tSize = new float[PerPatch], tSpeed = new float[PerPatch];
            public readonly float[] tY = new float[PerPatch]; public bool[] tWet = new bool[PerPatch];
            public float halo;   // 0..1: how much of its own ground glow the patch shows (1 when it has no real light)
            public float Age(float now) => now - start;
            public float Remaining(float now) => life - (now - start);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct TVert { public Vector3 pos; public Vector4 d0, d1, d2; }   // pos = tongue root; d0 uv.xy seed size; d1 speed kind fade rise; d2 heightScale sizeScale 0 0

        public readonly List<Patch> patches = new List<Patch>(MaxPatches);
        readonly TVert[] tverts = new TVert[(MaxPatches * PerPatch + MaxLoose) * 4];
        struct LooseTongue { public Vector3 root; public float seed, size, speed, fade, hs, ss; }
        readonly LooseTongue[] loose = new LooseTongue[MaxLoose]; int looseN;
        /// <summary>Loose tongues drawn by the last Step (proof readout).</summary>
        public int LooseDrawn { get; private set; }
        /// <summary>One upright tongue rooted at `root` for the next drawn frame: seed 0..1 (its cycle, height and sway), size = its
        /// width factor (a patch uses .55-1.5), speed = its cycle speed (a patch uses .42-1.14), fade 0..1, hs / ss = height /
        /// width scale (a patch uses .85 / .8). Over MaxLoose the rest is dropped.</summary>
        public void Loose(Vector3 root, float seed, float size, float speed, float fade, float hs, float ss)
        {
            if (looseN >= MaxLoose || fade <= 0.001f) return;
            loose[looseN++] = new LooseTongue { root = root, seed = seed, size = size, speed = speed, fade = fade, hs = hs, ss = ss };
        }
        readonly Mesh tongueMesh;

        public float Time { get; private set; }                 // time.value (accumulated, pauses with the game)
        const MeshUpdateFlags Quiet = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds;
        static readonly Vector2[] Quad = { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };   // uv; position = (uv.x - .5, uv.y)

        public OpenZPyroGroundFire()
        {
            for (int i = 0; i < MaxPatches; i++) patches.Add(new Patch { id = i });
            tongueMesh = new Mesh { name = "OZ_PyroTongues" }; tongueMesh.MarkDynamic();
            int quads = MaxPatches * PerPatch + MaxLoose;
            tongueMesh.SetVertexBufferParams(quads * 4,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4));
            var idx = new ushort[quads * 6];
            for (int i = 0; i < quads; i++) { int v = i * 4, k = i * 6; idx[k] = (ushort)v; idx[k + 1] = (ushort)(v + 2); idx[k + 2] = (ushort)(v + 1); idx[k + 3] = (ushort)v; idx[k + 4] = (ushort)(v + 3); idx[k + 5] = (ushort)(v + 2); }
            tongueMesh.SetIndexBufferParams(idx.Length, IndexFormat.UInt16);
            tongueMesh.SetIndexBufferData(idx, 0, 0, idx.Length, Quiet);
            tongueMesh.subMeshCount = 1;
            tongueMesh.SetSubMesh(0, new SubMeshDescriptor(0, 0, MeshTopology.Triangles), Quiet);
            tongueMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e4f);
        }

        public int ActiveCount { get { int n = 0; foreach (var p in patches) if (p.active) n++; return n; } }

        // ------------------------------------------------------------------------------------------------ the seeded footprint
        /// <summary>napalmShape: three Gaussian lobes, rotated by seed x 2.399.</summary>
        public static float Shape(float x, float z, float seed)
        {
            float a = seed * 2.399f, c = Mathf.Cos(a), s = Mathf.Sin(a), px = (x * c + z * s) * 0.92f, pz = (-x * s + z * c) * 1.14f;
            return Blob(px, pz, -0.34f, -0.05f, 0.55f, 0.42f) + Blob(px, pz, 0.17f, 0.12f, 0.55f, 0.46f) + Blob(px, pz, 0.43f, -0.2f, 0.4f, 0.32f);
        }
        static float Blob(float px, float pz, float cx, float cz, float sx, float sz) { float qx = (px - cx) / sx, qz = (pz - cz) / sz; return Mathf.Exp(-(qx * qx + qz * qz) * 2.25f); }

        uint rng = 0x2545F491u;
        float Rand() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / 16777216f; }

        /// <summary>Burning ground at `at` (on the ground already). Returns the patch (new, or the live one it merged into) and
        /// whether it is new. A patch refreshed within its reach grows by `grow` m (up to MaxRadius) and drifts a little toward
        /// the new point.</summary>
        public Patch Ignite(Vector3 at, float radius, float now, System.Func<float, float, float> ground, System.Func<Vector3, bool> wet, out bool created, float grow = 0.12f)
        {
            created = false;
            Patch best = null; float bestD = float.MaxValue;
            foreach (var p in patches)
            {
                if (!p.active) continue;
                float dx = p.c.x - at.x, dz = p.c.z - at.z, d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d < p.r * 0.95f + 0.4f && d < bestD) { best = p; bestD = d; }
            }
            if (best != null)
            {   // refresh: remaining back to the full life (keep the fade-in done), grow, drift
                float age = now - best.start;
                best.life = Mathf.Max(best.life, age + Life * (0.9f + 0.2f * Rand()));
                if (!Pinned)
                {
                    best.r = Mathf.Min(MaxRadius, best.r + grow);
                    best.c = Vector3.Lerp(best.c, new Vector3(at.x, best.c.y, at.z), 0.12f);
                }
                best.lastRefresh = now;
                return best;
            }
            Patch slot = null;
            foreach (var p in patches) if (!p.active) { slot = p; break; }
            if (slot == null) { slot = patches[0]; foreach (var p in patches) if (p.Remaining(now) < slot.Remaining(now)) slot = p; }   // replace the one closest to its end
            created = true;
            slot.active = true; slot.c = at; slot.r = Mathf.Clamp(radius, MinRadius, MaxRadius); slot.builtR = -1f; slot.start = now; slot.lastRefresh = now;
            slot.life = Life * (0.9f + 0.2f * Rand()); slot.seed = Rand() * 10f; slot.lastSmoke = now - Rand() * 0.5f; slot.halo = 1f;
            // tongue roots: rejection-sampled inside the seeded footprint, embers anywhere in it
            for (int j = 0; j < PerPatch; j++)
            {
                float dx = 0, dz = 0; bool ok = false;
                for (int k = 0; k < 32; k++) { dx = Rand() * 2f - 1f; dz = Rand() * 2f - 1f; if (Shape(dx, dz, slot.seed) > 0.3f) { ok = true; break; } }
                if (!ok) { dx = 0; dz = 0; }
                slot.off[j] = new Vector2(dx, dz);
                slot.tSeed[j] = Rand(); slot.tSize[j] = 0.55f + Rand() * 0.95f; slot.tSpeed[j] = 0.42f + Rand() * 0.72f;
            }
            Resample(slot, ground, wet);
            return slot;
        }

        /// <summary>Ground heights of the tongue roots (the patch radius changed or it was just made).</summary>
        public void Resample(Patch p, System.Func<float, float, float> ground, System.Func<Vector3, bool> wet)
        {
            float R = p.r * 1.12f;
            for (int j = 0; j < PerPatch; j++)
            {
                float x = p.c.x + p.off[j].x * R, z = p.c.z + p.off[j].y * R;
                p.tY[j] = ground(x, z);
                p.tWet[j] = wet(new Vector3(x, p.tY[j], z));
            }
        }

        /// <summary>update(): fades, rise, flicker and the tongue vertex stream.</summary>
        public int Step(float dt, float now, float heightScaleK)
        {
            Time += Mathf.Max(0f, dt);
            int q = 0;
            foreach (var p in patches)
            {
                if (!p.active) continue;
                float age = now - p.start, remaining = p.life - age;
                if (remaining <= 0f) { p.active = false; continue; }
                float fade = Mathf.Min(1f, remaining / FadeOut) * Mathf.Min(1f, age / FadeIn);
                float rise = 0.55f + 0.45f * Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, remaining / FadeOut));
                float flicker = 0.82f + 0.14f * Mathf.Sin(Time * 1.85f + p.seed * 0.37f) + 0.04f * Mathf.Sin(Time * 7.9f + p.seed);
                p.rise = rise;
                fade = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, remaining / FadeOut)) * Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, age / FadeIn));
                p.fadeX = fade * (0.90f + 0.10f * Mathf.Sin(Time * 2f + p.seed));
                p.fadeZ = fade * flicker;
                float hs = Mathf.Clamp(p.r / 2.6f, 0.5f, 0.85f) * heightScaleK, ss = Mathf.Clamp(p.r / 2.9f, 0.45f, 0.8f);
                float R = p.r * 1.12f;
                for (int j = 0; j < PerPatch; j++)
                {
                    if (p.tWet[j]) continue;
                    var root = new Vector3(p.c.x + p.off[j].x * R, p.tY[j] + (j >= Flames ? 0.2f : 0.05f + p.tSeed[j] * 0.09f), p.c.z + p.off[j].y * R);
                    float kind = j >= Flames ? 1f : 0f;
                    var d1 = new Vector4(p.tSpeed[j], kind, p.fadeX, p.rise);
                    var d2 = new Vector4(hs, ss, 0f, 0f);
                    int vb = q * 4;
                    for (int c = 0; c < 4; c++)
                        tverts[vb + c] = new TVert { pos = root, d0 = new Vector4(Quad[c].x, Quad[c].y, p.tSeed[j], p.tSize[j]), d1 = d1, d2 = d2 };
                    q++;
                }
            }
            // loose tongues (burning wrecks): the same quads, after the patches' own
            var looseBox = new Bounds();
            for (int i = 0; i < looseN; i++)
            {
                var t = loose[i];
                var ld1 = new Vector4(t.speed, 0f, t.fade, 0.55f + 0.45f * t.fade);
                var ld2 = new Vector4(t.hs * heightScaleK, t.ss, 0f, 0f);
                int lvb = q * 4;
                for (int k = 0; k < 4; k++)
                    tverts[lvb + k] = new TVert { pos = t.root, d0 = new Vector4(Quad[k].x, Quad[k].y, t.seed, t.size), d1 = ld1, d2 = ld2 };
                q++;
                var tb = new Bounds(t.root + Vector3.up * 1.6f, new Vector3(4f, 5f, 4f));
                if (i == 0) looseBox = tb; else looseBox.Encapsulate(tb);
            }
            bool anyLoose = looseN > 0; LooseDrawn = looseN; looseN = 0;
            if (q > 0)
            {
                tongueMesh.SetVertexBufferData(tverts, 0, 0, q * 4, 0, Quiet);
                tongueMesh.SetSubMesh(0, new SubMeshDescriptor(0, q * 6, MeshTopology.Triangles) { firstVertex = 0, vertexCount = q * 4 }, Quiet);
                var box = Bounds();                                                             // the patches (empty when none burns)
                if (anyLoose) { if (ActiveCount > 0) box.Encapsulate(looseBox); else box = looseBox; }
                tongueMesh.bounds = box;
            }
            tongueQuads = q;
            return q;
        }
        int tongueQuads;

        Bounds Bounds()
        {
            bool any = false; var b = new Bounds();
            foreach (var p in patches)
            {
                if (!p.active) continue;
                var pb = new Bounds(p.c + Vector3.up * 1.6f, new Vector3(p.r * 3f + 2f, 5f, p.r * 3f + 2f));
                if (!any) { b = pb; any = true; } else b.Encapsulate(pb);
            }
            return b;
        }

        // ------------------------------------------------------------------------------------------------ the burning surface
        readonly List<Vector3> bv = new List<Vector3>(); readonly List<Vector2> bu = new List<Vector2>(); readonly List<Color> bc = new List<Color>(); readonly List<int> bt = new List<int>();
        const int Seg = 32;

        /// <summary>(Re)builds a patch's ground grid when it is new or grew by 10 cm: polar rings out to the halo, heights from the
        /// r x 1.12; UV2.x = distance / halo radius for the glow.</summary>
        public void BuildSurface(Patch p, float haloMul, System.Func<float, float, float> ground, System.Func<Vector3, bool> wet)
        {
            if (p.surf != null && !NeedsRebuild(p)) return;
            p.builtR = p.r; p.builtC = p.c;
            if (p.surf == null) { p.surf = new Mesh { name = "OZ_PyroGround" }; p.surf.MarkDynamic(); }
            bv.Clear(); bu.Clear(); bc.Clear(); bt.Clear();
            float pz = p.r * 1.12f, rOuter = Mathf.Max(pz * 1.35f, p.r * haloMul);
            int rings = Mathf.Clamp(Mathf.CeilToInt(rOuter / 0.35f), 6, 16);
            void Add(float x, float z)
            {
                float y = ground(x, z);
                bool w = wet(new Vector3(x, y, z));
                bv.Add(new Vector3(x, y + 0.04f, z));
                bu.Add(new Vector2((x - p.c.x) / (pz * 2f) + 0.5f, (z - p.c.z) / (pz * 2f) + 0.5f));
                float d = Mathf.Sqrt((x - p.c.x) * (x - p.c.x) + (z - p.c.z) * (z - p.c.z));
                bc.Add(new Color(d / rOuter, 0f, 0f, w ? 0f : 1f));
            }
            Add(p.c.x, p.c.z);
            for (int r = 1; r <= rings; r++)
            {
                float rr = rOuter * Mathf.Pow(r / (float)rings, 1.25f);                        // denser near the fire
                for (int i = 0; i < Seg; i++) { float a = i * Mathf.PI * 2f / Seg; Add(p.c.x + Mathf.Cos(a) * rr, p.c.z + Mathf.Sin(a) * rr); }
            }
            for (int i = 0; i < Seg; i++) { bt.Add(0); bt.Add(1 + (i + 1) % Seg); bt.Add(1 + i); }
            for (int r = 1; r < rings; r++)
                for (int i = 0; i < Seg; i++)
                {
                    int a0 = 1 + (r - 1) * Seg + i, a1 = 1 + (r - 1) * Seg + (i + 1) % Seg, b0 = a0 + Seg, b1 = a1 + Seg;
                    bt.Add(a0); bt.Add(a1); bt.Add(b1); bt.Add(a0); bt.Add(b1); bt.Add(b0);
                }
            p.surf.Clear();
            p.surf.SetVertices(bv); p.surf.SetUVs(0, bu); p.surf.SetColors(bc); p.surf.SetTriangles(bt, 0);
            p.surf.RecalculateBounds();
        }

        /// <summary>Grown by 10 cm or drifted by 15 cm since the grid / tongue roots were built.</summary>
        public static bool NeedsRebuild(Patch p) => p.builtR < 0f || Mathf.Abs(p.r - p.builtR) >= 0.1f || (p.c - p.builtC).sqrMagnitude > 0.0225f;

        static readonly int PatchId = Shader.PropertyToID("_OZPatch");

        public void Draw(Material surfaceMat, Material tongueMat)
        {
            if (surfaceMat)
                foreach (var p in patches)
                {
                    if (!p.active || p.surf == null) continue;
                    p.mpb.SetVector(PatchId, new Vector4(p.fadeZ, p.seed, p.halo * p.fadeX, p.fadeX));
                    var rp = new RenderParams(surfaceMat) { layer = 0, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, matProps = p.mpb, worldBounds = p.surf.bounds };
                    Graphics.RenderMesh(rp, p.surf, 0, Matrix4x4.identity);
                }
            if (tongueMat && tongueQuads > 0)
            {
                var rp = new RenderParams(tongueMat) { layer = 0, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, worldBounds = tongueMesh.bounds };
                Graphics.RenderMesh(rp, tongueMesh, 0, Matrix4x4.identity);
            }
        }

        public void Clear() { foreach (var p in patches) p.active = false; tongueQuads = 0; looseN = 0; LooseDrawn = 0; }

        public void Dispose()
        {
            if (tongueMesh) Object.Destroy(tongueMesh);
            foreach (var p in patches) if (p.surf) Object.Destroy(p.surf);
        }
    }
}
