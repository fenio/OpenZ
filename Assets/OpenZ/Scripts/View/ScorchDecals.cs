using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary></summary>
    public enum ScorchKind { Contact = 0, Explosion = 1, Wreck = 2 }

    /// <summary>
    /// Replaces FxManager's flat 24-gon disc, which sat at the ground
    /// height of its centre and half sank into every slope.
    ///
    /// Why a mesh decal and not a URP DecalProjector: URP 17.3 has DecalProjector, but PC_Renderer.asset has no Decal renderer
    /// feature (only SSAO + the inactive Atmosphere pass) and OpenZ's own ground / unit shaders (Sand, TeamLit) have no DBuffer
    /// support, so a DBuffer decal would not show and a screen-space decal would also paint the units standing in it. This
    /// conforms to the real walk surface instead: a grid (7-21 vertices a side, <= ~0.4 m cells) sampled with ZGame.GroundY
    /// (terrain + bridge decks), lifted 3.5 cm + a depth Offset, vertices fading out over open water (bridge decks keep their
    /// marks), at the grid rim and where the ground drops or rises .9-1.6 m from the centre (cliff edges, deck ends): nothing
    ///
    /// there, OpenZ/ScorchDecal here), radial soot with a wobbly edge and 5-16 streaks, 4 procedural variants (atlas built
    /// once at start), random rotation, random size x0.7-1.4, soft edges; opacity = 1 - smoothstep(age, fadeAt, life):
    /// explosions 45-65 s (fade from 20 %), wrecks / buildings 90-130 s (30 %), contacts 18-26 s; a mark is skipped when a
    /// stronger one lies within max(.7, r x .42) and is younger than 1 s (contacts 20 s); max 40 marks (10 contacts), the
    /// oldest / faintest of the lowest priority is recycled first; air bursts (> 3.5 m above the ground) leave none.
    /// </summary>
    [DefaultExecutionOrder(105)]
    public class ScorchDecals : MonoBehaviour
    {
        public const int MaxMarks = 40, MaxContacts = 10;
        public const float Lift = 0.035f;                                                   // the project rules: decals >= 2-3 cm above their surface
        const int Tile = 256;

        public static ScorchDecals I { get; private set; }

        sealed class Mark
        {
            public Vector3 at; public float radius, born, life, fadeAt, strength = 1f; public ScorchKind kind;
            public Mesh mesh; public Matrix4x4 matrix; public MaterialPropertyBlock mpb;
        }

        readonly List<Mark> marks = new List<Mark>();
        readonly Stack<Mark> spare = new Stack<Mark>();
        Material mat; Texture2D atlas;
        static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        public int Count => marks.Count;

        /// <summary>Creates the manager (and bakes the atlas) if needed. FxManager.Awake calls it so the first blast never hitches.</summary>
        public static ScorchDecals Ensure()
        {
            if (I) return I;
            I = FindFirstObjectByType<ScorchDecals>();
            if (!I) I = new GameObject("ScorchDecals").AddComponent<ScorchDecals>();
            return I;
        }

        /// <summary>Leave a scorch mark of about `radius` metres (x random 0.7-1.4) at `at` (x, z; the height is resampled).</summary>
        public static bool Add(Vector3 at, float radius, ScorchKind kind = ScorchKind.Explosion) => Ensure() && I.AddMark(at, radius, kind);

        /// <summary>
        /// `strength` (0..1) of a scorch's darkening, for `life` s (fades over the last 65 %).</summary>
        public static bool AddSand(Vector3 at, float radius, float life, float strength)
        {
            if (!Ensure() || !I.AddMark(at, radius, ScorchKind.Contact)) return false;
            var m = I.marks[I.marks.Count - 1]; m.life = life; m.fadeAt = life * 0.35f; m.strength = Mathf.Clamp01(strength);
            SandMarks++;
            return true;
        }
        /// <summary>Proof: disturbed-sand marks laid.</summary>
        public static int SandMarks;

        /// <summary>
        /// (combatview patch OpenZFxPlayer.patch), so there is ONE scorch system; otherwise it keeps its own marks.</summary>
        public static bool Available => Ensure() && I.enabled && I.mat;

        void Awake()
        {
            if (I && I != this) { Destroy(this); return; }
            I = this;
            waterY = float.NaN;                                                               // re-measured for this level
            var sh = Shader.Find("OpenZ/ScorchDecal");
            if (!sh) { Debug.LogError("ScorchDecals: shader OpenZ/ScorchDecal missing"); enabled = false; return; }
            atlas = BuildAtlas();
            mat = new Material(sh) { name = "ScorchDecal", mainTexture = atlas };
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            foreach (var m in marks) if (m.mesh) Destroy(m.mesh);
            foreach (var m in spare) if (m.mesh) Destroy(m.mesh);
            if (mat) Destroy(mat);
            if (atlas) Destroy(atlas);
        }

        /// <summary>Open water at (x, z) with walk surface height h: a water tile whose surface (river bed, or a bridge deck) is
        /// not above the water. Bridge decks over the river sit on Water tiles, so the tile class alone would refuse every
        /// mark on a bridge (the fx import found the same, OpenZFxPlayer.WetAt).</summary>
        static bool IsWater(float x, float z, float h)
        {
            var g = ZGame.I; if (g == null || g.World == null) return false;
            var p = ZGame.WorldToPx(new Vector3(x, 0, z));
            var c = g.World.Map.ClassAt(p.x >> 4, p.y >> 4);
            if (c != ZClass.Water && c != ZClass.DeepWater) return false;
            float wy = WaterY();
            return float.IsNegativeInfinity(wy) || h < wy + 0.1f;
        }

        static float waterY = float.NaN;
        /// <summary>The flat river / harbour surface: the lowest WAT_* renderer bounds + 2 cm (same rule as ZoneMarkers.cs:27-29),
        /// measured once per level; -infinity when the level has no water meshes (then every water tile counts as water).</summary>
        static float WaterY()
        {
            if (!float.IsNaN(waterY)) return waterY;
            waterY = float.NegativeInfinity;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.name.StartsWith("WAT_") && r.gameObject.activeInHierarchy) waterY = Mathf.Max(waterY, r.bounds.min.y + 0.02f);
            return waterY;
        }

        /// <summary>
        /// banks and dune rims, so part of the grid sat under the visible surface. The rendered TER_ height (TrackSurface, exact
        /// per half-metre cell) where known; bridge decks and the rest from the walk surface as before.</summary>
        static float Ground(float x, float z)
        {
            var s = TrackMarks.I ? TrackMarks.I.Surface : null;
            if (s != null && s.Sample(x, z, out float y, out bool deck)) return y + (deck ? 0f : CraterField.FinalMissing(x, z));
            return (ZGame.I ? ZGame.I.GroundY(x, z) : 0f) + CraterField.FinalMissing(x, z);
        }

        bool AddMark(Vector3 at, float radius, ScorchKind kind)
        {
            if (!enabled || !mat || !(radius > 0.05f) || float.IsNaN(at.x) || float.IsNaN(at.z)) return false;
            float ground = Ground(at.x, at.z);
            if (at.y - ground > 3.5f) return false;                                          // air bursts
            if (IsWater(at.x, at.z, ground)) return false;
            float now = Time.time; int pri = (int)kind;
            foreach (var m in marks)
            {   // an equal or stronger fresh mark already covers this spot
                if ((int)m.kind < pri) continue;
                float dx = m.at.x - at.x, dz = m.at.z - at.z;
                if (dx * dx + dz * dz < Sq(Mathf.Max(0.7f, m.radius * 0.42f)) && now - m.born < (pri == 0 ? 20f : 1f)) return false;
            }
            int contacts = 0; foreach (var m in marks) if (m.kind == ScorchKind.Contact) contacts++;
            if (marks.Count >= MaxMarks || (kind == ScorchKind.Contact && contacts >= MaxContacts))
            {   // recycle the lowest priority, then the faintest (= oldest)
                int best = -1; float bestOp = float.MaxValue;
                for (int i = 0; i < marks.Count; i++)
                {
                    var m = marks[i]; if ((int)m.kind > pri) continue;
                    if (kind == ScorchKind.Contact && m.kind != ScorchKind.Contact) continue;
                    float op = Opacity(m, now) + (int)m.kind * 10f;
                    if (op < bestOp) { bestOp = op; best = i; }
                }
                if (best < 0) return false;
                Recycle(best);
            }
            float rnd = Random.value;
            var mk = spare.Count > 0 ? spare.Pop() : new Mark();
            mk.at = new Vector3(at.x, ground, at.z); mk.kind = kind; mk.born = now; mk.strength = 1f;
            mk.radius = radius * Random.Range(0.7f, 1.4f);
            mk.life = kind == ScorchKind.Wreck ? 90f + rnd * 40f : kind == ScorchKind.Explosion ? 45f + rnd * 20f : 18f + rnd * 8f;
            mk.fadeAt = mk.life * (kind == ScorchKind.Wreck ? 0.3f : 0.2f);
            if (!mk.mesh) { mk.mesh = new Mesh { name = "ScorchDecal" }; mk.mesh.MarkDynamic(); }
            mk.mpb ??= new MaterialPropertyBlock();
            if (kind == ScorchKind.Explosion) mk.strength = Random.Range(0.82f, 1f);                  // ZF14: not every mark equally black
            float stretch = Random.Range(0.8f, 1.22f);                                              // ZF14: oval, not always round
            Build(mk, Random.value * 360f, PickVariant(kind, mk.radius), stretch, 1f / Mathf.Sqrt(stretch * stretch * 0.5f + 0.5f));
            marks.Add(mk);
            return true;
        }

        static float Sq(float v) => v * v;

        /// <summary>
        /// variants; small marks (contacts, grenades, robots) get the soot splashes, medium blasts any soot shape plus the small
        /// crater and the burnt ring, big blasts (heavy shells, vehicles, buildings) the craters with ejecta.</summary>
        static readonly int[] SmallPool = { 0, 1, 3, 5, 6 }, MidPool = { 0, 1, 2, 3, 4, 6, 7 }, BigPool = { 2, 4, 7, 8, 4, 8 };
        static int PickVariant(ScorchKind kind, float r)
        {
            var pool = kind == ScorchKind.Contact || r < 1.6f ? SmallPool : r < 3.2f ? MidPool : BigPool;
            int v = pool[Random.Range(0, pool.Length)];
            Variants[v]++;
            return v;
        }
        /// <summary>Proof: marks laid per variant.</summary>
        public static readonly int[] Variants = new int[Kinds];
        const int Kinds = 9, Cols = 3;

        void Recycle(int i) { spare.Push(marks[i]); marks.RemoveAt(i); }

        /// <summary></summary>
        public static int ClearIn(Vector3 c, float r)
        {
            if (!I) return 0;
            int k = 0;
            for (int i = I.marks.Count - 1; i >= 0; i--)
            {
                var m = I.marks[i]; float dx = m.at.x - c.x, dz = m.at.z - c.z;
                if (dx * dx + dz * dz < r * r) { I.Recycle(i); k++; }
            }
            return k;
        }

        /// <summary>
        /// Every mark inside the world rect [min, max] (x / z) that sits on a deck over water
        /// (a water tile, more than 0.3 m above the water surface) fades out over 0.4 s, under the bridge's own blast. Returns the
        /// marks cleared. CombatView calls it when a bridge dies.</summary>
        public static int ClearDeck(Vector3 min, Vector3 max)
        {
            if (!I) return 0;
            float now = Time.time, wy = WaterY(); int n = 0;
            float x0 = Mathf.Min(min.x, max.x), x1 = Mathf.Max(min.x, max.x), z0 = Mathf.Min(min.z, max.z), z1 = Mathf.Max(min.z, max.z);
            foreach (var m in I.marks)
            {
                if (m.at.x < x0 || m.at.x > x1 || m.at.z < z0 || m.at.z > z1) continue;
                var g = ZGame.I; if (g == null || g.World == null) continue;
                var p = ZGame.WorldToPx(new Vector3(m.at.x, 0, m.at.z));
                var c = g.World.Map.ClassAt(p.x >> 4, p.y >> 4);
                if (c != ZClass.Water && c != ZClass.DeepWater) continue;
                if (!float.IsNegativeInfinity(wy) && m.at.y < wy + 0.3f) continue;
                float age = now - m.born; m.fadeAt = Mathf.Min(m.fadeAt, age); m.life = Mathf.Min(m.life, age + 0.4f);
                n++;
            }
            DeckCleared += n;
            return n;
        }
        public static int DeckCleared;

        /// <summary>scorchOpacity + a .15 s fade-in under the fireball.</summary>
        static float Opacity(Mark m, float now)
        {
            float age = now - m.born;
            return m.strength * PopOut.Smooth(age, 0f, 0.15f) * (1f - PopOut.Smooth(age, m.fadeAt, m.life));
        }

        // build buffers, reused for every mark (no allocation per blast)
        static readonly List<Vector3> bv = new List<Vector3>(441); static readonly List<Vector2> buv = new List<Vector2>(441);
        static readonly List<Color32> bcol = new List<Color32>(441); static readonly List<int> btri = new List<int>(2400);

        /// <summary>Terrain-conforming grid around the mark centre (mesh space = centre at the ground height).</summary>
        void Build(Mark mk, float rotDeg, int variant, float sx = 1f, float sz = 1f)
        {
            float half = mk.radius * 1.3f;                                                     // streaks reach past the soot disc
            int n = Mathf.Clamp(Mathf.CeilToInt(2f * half * Mathf.Max(sx, sz) / 0.4f) + 1, 7, 21);
            var v = bv; var uv = buv; var col = bcol; var tri = btri;
            v.Clear(); uv.Clear(); col.Clear(); tri.Clear();
            var rot = Quaternion.Euler(0, rotDeg, 0);
            const float span = 1f / Cols;
            float h0 = mk.at.y, u0 = (variant % Cols) * span, v0 = (variant / Cols) * span, pad = 2f / (Cols * Tile);
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float fx = i / (n - 1f) * 2f - 1f, fz = j / (n - 1f) * 2f - 1f;
                    var local = rot * new Vector3(fx * half * sx, 0, fz * half * sz);
                    float x = mk.at.x + local.x, z = mk.at.z + local.z, h = Ground(x, z);
                    bool step = Mathf.Abs(h - h0) > 1.6f, water = IsWater(x, z, h);
                    float rim = Mathf.Max(Mathf.Abs(fx), Mathf.Abs(fz));
                    // soft rim; fades out where the ground drops / rises > .9 m from the centre (a cliff edge, a deck end), so
                    // the mark never drapes down a cliff face and a cell never stretches across a step
                    float a = step || water ? 0f : (1f - PopOut.Smooth(rim, 0.86f, 1f)) * (1f - PopOut.Smooth(Mathf.Abs(h - h0), 0.9f, 1.6f));
                    v.Add(new Vector3(local.x, (step ? h0 : h) - h0 + Lift, local.z));
                    uv.Add(new Vector2(u0 + pad + (fx * 0.5f + 0.5f) * (span - 2 * pad), v0 + pad + (fz * 0.5f + 0.5f) * (span - 2 * pad)));
                    col.Add(new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f)));
                }
            for (int j = 0; j < n - 1; j++)
                for (int i = 0; i < n - 1; i++)
                {
                    int k = j * n + i;
                    // split each cell along the diagonal that follows the ground (the shorter height jump)
                    if (Mathf.Abs(v[k].y - v[k + n + 1].y) <= Mathf.Abs(v[k + 1].y - v[k + n].y))
                    { tri.Add(k); tri.Add(k + n); tri.Add(k + n + 1); tri.Add(k); tri.Add(k + n + 1); tri.Add(k + 1); }
                    else { tri.Add(k); tri.Add(k + n); tri.Add(k + 1); tri.Add(k + 1); tri.Add(k + n); tri.Add(k + n + 1); }
                }
            var m = mk.mesh; m.Clear();
            m.SetVertices(v); m.SetUVs(0, uv); m.SetColors(col); m.SetTriangles(tri, 0); m.RecalculateBounds();
            mk.matrix = Matrix4x4.Translate(mk.at);
        }

        void LateUpdate()
        {
            if (!mat) return;
            float now = Time.time;
            for (int i = marks.Count - 1; i >= 0; i--)
            {
                var m = marks[i];
                if (now - m.born >= m.life) { Recycle(i); continue; }
                m.mpb.SetFloat(OpacityId, Opacity(m, now));
                Graphics.DrawMesh(m.mesh, m.matrix, mat, 0, null, 0, m.mpb, ShadowCastingMode.Off, false);
            }
        }

        // ------------------------------------------------------------------ procedural atlas (4 variants, 2 x 2 tiles)
        static float Hash(int x, int y, int s)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + s * 144665);
                h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
                return (h & 0xffffff) / 16777215f;
            }
        }

        static float Noise(float x, float y, int s)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y); float xf = x - xi, yf = y - yi;
            float u = xf * xf * (3 - 2 * xf), w = yf * yf * (3 - 2 * yf);
            float a = Hash(xi, yi, s), b = Hash(xi + 1, yi, s), c = Hash(xi, yi + 1, s), d = Hash(xi + 1, yi + 1, s);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), w);
        }

        static float Fbm(float x, float y, int s) => Noise(x, y, s) * 0.55f + Noise(x * 2.1f, y * 2.1f, s + 7) * 0.3f + Noise(x * 4.3f, y * 4.3f, s + 13) * 0.15f;

        /// <summary>Burn amount (0..1) of variant `k` at mark-space point (px, py): a charcoal core, a charred ring that
        /// streaks thrown out of the blast, a few char specks and mottling; per variant another outline / streak pattern.</summary>
        static float Burn(int k, float px, float py)
        {
            if (k >= 4) return BurnV2(k, px, py);
            int seed = 11 + k * 37;
            float aspect = k == 1 ? 0.78f : k == 2 ? 0.92f : 1f;
            py /= aspect;
            float r = Mathf.Sqrt(px * px + py * py), ang = Mathf.Atan2(py, px);
            float wob = 0.88f + 0.12f * Mathf.Sin(3f * ang + seed) + 0.06f * Mathf.Sin(5f * ang + 2f * seed) + 0.04f * Mathf.Sin(9f * ang + 3f * seed)
                        + 0.10f * (Noise(ang * 2.2f + 10f, seed, seed) - 0.5f);
            float rn = r / wob;
            float g = rn < 0.35f ? 1f : rn < 0.7f ? Mathf.Lerp(1f, 0.72f, (rn - 0.35f) / 0.35f) : 0.72f * (1f - PopOut.Smooth(rn, 0.7f, 1f));
            if (k == 2)
            {   // double blotch: a second, smaller lobe off-centre
                float qx = px - 0.42f, qy = py + 0.18f, r2 = Mathf.Sqrt(qx * qx + qy * qy) / 0.55f;
                g = Mathf.Max(g, 0.8f * (1f - PopOut.Smooth(r2, 0.3f, 1f)));
            }
            int streaks = k == 0 ? 7 : k == 1 ? 11 : k == 2 ? 6 : 16;
            float sw = k == 3 ? 0.06f : 0.1f;
            for (int j = 0; j < streaks; j++)
            {   // 7 small streaks; here soft rays thrown past the edge
                float a = j * 2.399f + seed * 0.37f, len = 1.05f + 0.3f * Hash(j, 3, seed);
                float dx = Mathf.Cos(a), dy = Mathf.Sin(a), along = px * dx + py * dy;
                if (along < 0.3f || along > len) continue;
                float across = Mathf.Abs(-px * dy + py * dx), wdt = sw * (1.3f - 0.8f * (along - 0.3f) / (len - 0.3f));
                float s = (1f - PopOut.Smooth(across, 0f, wdt)) * (1f - PopOut.Smooth(along, 0.5f, len)) * (0.45f + 0.3f * Hash(j, 5, seed));
                g = Mathf.Max(g, s);
            }
            int specks = k == 3 ? 22 : 14;
            for (int j = 0; j < specks; j++)
            {   // char specks: small burnt debris around the blast
                float a = j * 2.399f + seed * 0.53f, rr = 0.55f + 0.6f * Hash(j, 7, seed), size = 0.025f + 0.035f * Hash(j, 9, seed);
                float qx = px - Mathf.Cos(a) * rr, qy = py - Mathf.Sin(a) * rr;
                g = Mathf.Max(g, (1f - PopOut.Smooth(Mathf.Sqrt(qx * qx + qy * qy), size * 0.4f, size)) * 0.6f);
            }
            g *= 0.8f + 0.3f * Fbm(px * 3.1f + seed, py * 3.1f - seed, seed);                            // mottled soot, not a flat stamp
            return Mathf.Clamp01(g);
        }

        /// <summary>ZF14 variants 4-8: 4 small crater (dark pit, pale ejecta ring, charred skirt, rays), 5 spotty cluster, 6 directional
        /// splash (oval, thrown to one side), 7 burnt ring (paler centre), 8 big crater (double rim, many ejecta specks).</summary>
        static float BurnV2(int k, float px, float py)
        {
            int seed = 11 + k * 37;
            if (k == 6) { px = px * 0.8f - 0.15f; py /= 0.62f; }
            float r = Mathf.Sqrt(px * px + py * py), ang = Mathf.Atan2(py, px);
            float wob = 0.9f + 0.1f * Mathf.Sin(4f * ang + seed) + 0.06f * Mathf.Sin(7f * ang + 2f * seed) + 0.12f * (Noise(ang * 2.6f + 5f, seed, seed) - 0.5f);
            float rn = r / wob, g = 0f;
            int rays = 0, specks = 12; float rayW = 0.09f, rayLen = 1.1f;
            switch (k)
            {
                case 4:
                case 8:
                {
                    float pit = k == 8 ? 0.26f : 0.22f;
                    g = rn < pit ? 1f : rn < pit + 0.08f ? Mathf.Lerp(1f, 0.35f, (rn - pit) / 0.08f)            // the pit
                      : rn < pit + 0.2f ? 0.35f + 0.1f * Fbm(px * 6f, py * 6f, seed)                          // pale ejecta ring
                      : 0.75f * (1f - PopOut.Smooth(rn, pit + 0.2f, 1f));                                       // charred skirt
                    if (k == 8) g = Mathf.Max(g, 0.62f * (1f - PopOut.Smooth(Mathf.Abs(rn - 0.62f), 0f, 0.07f)));   // second rim
                    rays = k == 8 ? 18 : 12; specks = k == 8 ? 34 : 20; rayW = 0.07f; rayLen = k == 8 ? 1.25f : 1.1f;
                    break;
                }
                case 5:
                    for (int j = 0; j < 7; j++)
                    {
                        float a = j * 2.399f + seed, rr = j == 0 ? 0f : 0.25f + 0.45f * Hash(j, 2, seed), sz = j == 0 ? 0.42f : 0.14f + 0.2f * Hash(j, 4, seed);
                        float qx = px - Mathf.Cos(a) * rr, qy = py - Mathf.Sin(a) * rr;
                        g = Mathf.Max(g, (j == 0 ? 0.9f : 0.75f) * (1f - PopOut.Smooth(Mathf.Sqrt(qx * qx + qy * qy) / (sz * wob), 0.35f, 1f)));
                    }
                    specks = 26;
                    break;
                case 6:
                    g = rn < 0.3f ? 1f : 0.8f * (1f - PopOut.Smooth(rn, 0.3f, 0.95f));
                    rays = 9; rayW = 0.12f; rayLen = 1.35f;
                    break;
                default:   // 7
                    g = 0.42f + 0.1f * Fbm(px * 4f, py * 4f, seed);
                    g = Mathf.Lerp(g, 0.95f, 1f - PopOut.Smooth(Mathf.Abs(rn - 0.6f), 0f, 0.22f));
                    g *= 1f - PopOut.Smooth(rn, 0.78f, 1f);
                    rays = 6; specks = 18;
                    break;
            }
            for (int j = 0; j < rays; j++)
            {   // rays thrown out of the blast (k 6: all to one side)
                float a = k == 6 ? (Hash(j, 3, seed) - 0.5f) * 1.9f : j * 2.399f + seed * 0.37f, len = rayLen + 0.3f * Hash(j, 3, seed);
                float dx = Mathf.Cos(a), dy = Mathf.Sin(a), along = px * dx + py * dy;
                if (along < 0.25f || along > len) continue;
                float across = Mathf.Abs(-px * dy + py * dx), wdt = rayW * (1.3f - 0.8f * (along - 0.25f) / (len - 0.25f));
                g = Mathf.Max(g, (1f - PopOut.Smooth(across, 0f, wdt)) * (1f - PopOut.Smooth(along, 0.5f, len)) * (0.45f + 0.3f * Hash(j, 5, seed)));
            }
            for (int j = 0; j < specks; j++)
            {   // ejecta / char specks
                float a = j * 2.399f + seed * 0.53f, rr = 0.45f + 0.7f * Hash(j, 7, seed), sz = 0.02f + 0.04f * Hash(j, 9, seed);
                float qx = px - Mathf.Cos(a) * rr, qy = py - Mathf.Sin(a) * rr;
                g = Mathf.Max(g, (1f - PopOut.Smooth(Mathf.Sqrt(qx * qx + qy * qy), sz * 0.4f, sz)) * 0.62f);
            }
            g *= 0.8f + 0.3f * Fbm(px * 3.1f + seed, py * 3.1f - seed, seed);
            return Mathf.Clamp01(g);
        }

        static Texture2D BuildAtlas()
        {
            int W = Tile * Cols;
            var px = new Color32[W * W];
            var soot = new Color(0.10f, 0.08f, 0.065f); var rim = new Color(0.42f, 0.32f, 0.22f);
            for (int k = 0; k < Kinds; k++)
            {
                int ox = (k % Cols) * Tile, oy = (k / Cols) * Tile;
                for (int y = 0; y < Tile; y++)
                    for (int x = 0; x < Tile; x++)
                    {
                        float fx = (x + 0.5f) / Tile * 2f - 1f, fy = (y + 0.5f) / Tile * 2f - 1f;
                        float b = Burn(k, fx * 1.3f, fy * 1.3f);                                      // mark edge at 1/1.3 of the tile
                        b *= 1f - PopOut.Smooth(Mathf.Max(Mathf.Abs(fx), Mathf.Abs(fy)), 0.9f, 0.99f); // clean transparent tile border
                        var c = Color.Lerp(rim, soot, PopOut.Smooth(b, 0.15f, 0.85f));
                        px[(oy + y) * W + ox + x] = new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), (byte)Mathf.RoundToInt(b * 255f));
                    }
            }
            // linear texture: rgb is the multiply factor the ground gets at full burn (soot .10 = 10 % of its brightness)
            var tex = new Texture2D(W, W, TextureFormat.RGBA32, true, true) { name = "ScorchAtlas", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }
    }
}
