using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1: a grey neutral flag on grey concrete is hard to find; a very subtle cue on the ground helps, never strong, never
    /// in the ground. NEUTRAL zone flags only, and only while the flag is on
    /// camera: now and then (one wave every 6 to 11 s per flag, at random, never all the time) a very soft ring of pale light
    /// spreads on the ground from the foot of the pole (0.55 m -> 3.2 m in 2.6 s, easing out) and fades. Every level.
    /// The ring is drawn ON the ground's surface: a polar grid around the pole whose every vertex sits on the rendered ground
    /// (the track surface where known, else the walk surface + the final crater bowl, as the burn marks: ScorchDecals), lifted
    /// 4 cm, with the overlay shader's depth offset; heights are sampled again at the start of every wave (craters), cells over
    /// open water or down a step of more than 1.2 m are not drawn. So it follows slopes, roads, concrete and bridge decks and
    /// never cuts into them. A flag that gets an owner starts no wave and its running wave fades out in 0.4 s.
    /// Where the older capture beacon (FlagBeacons.cs: a breathing ring under every flag the player does not own) really draws in
    /// the loaded level (a live, enabled beacon and its level switch on), this wave is not added on top of it (one cue per flag,
    /// never two). The beacon boots once, after the first scene of a session, and is not kept across scene loads: in a level
    /// loaded from the main menu it is not there, and there the wave is the flags' cue (the Car Park included).
    /// View only: reads the rules' flags and owners, writes nothing back; its own random numbers (never the rules').
    /// </summary>
    public sealed class OzNeutralFlagWaves : MonoBehaviour
    {
        public static bool AutoAttach = true;
        public static OzNeutralFlagWaves I { get; private set; }

        // the look (all subtle by intent: peak opacity about a fifth, a soft band, no glow)
        public const float R0 = 0.55f, R1 = 3.2f, Band = 0.3f, Lift = 0.04f, WaveSeconds = 2.6f, GapMin = 6f, GapMax = 11f, FirstMin = 1f, FirstMax = 4f;
        public const float PeakDay = 0.2f, PeakNight = 0.14f, OwnedFade = 0.4f, StepMax = 1.2f;
        public static readonly Color Tint = new Color(1f, 0.95f, 0.82f, 1f);
        const int Seg = 48; const float Cell = 0.2f;

        /// <summary>For the checks: waves started since the level began, waves started on a flag that was not neutral or not on
        /// camera (must stay 0), and the last wave's flag and peak opacity.</summary>
        public static int Started, StartedWrong, LastFlagId = -1;
        public static float LastPeak;

        sealed class Wave
        {
            public int id; public Vector3 foot; public Mesh mesh; public Vector3[] v; public Color[] c; public bool[] skip; public float[] r;
            public float next = -1f, t0 = -1f, kill = 1f; public bool seen; public System.Random rng;
        }
        readonly Dictionary<int, Wave> waves = new Dictionary<int, Wave>();
        Material mat; ZGame game; Camera cam; FlagBeacons beacon;

        // ------------------------------------------------------------------ install (as the other view layers: on the game object, every level)
        static bool hooked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!hooked) { hooked = true; SceneManager.sceneLoaded += (s, m) => Attach(); }
            Attach();
        }
        public static void Attach()
        {
            if (!AutoAttach || !ZGame.I || ZGame.I.GetComponent<OzNeutralFlagWaves>()) return;
            ZGame.I.gameObject.AddComponent<OzNeutralFlagWaves>();
        }

        void Awake() { I = this; Started = StartedWrong = 0; LastFlagId = -1; LastPeak = 0f; }

        void Start()
        {
            game = GetComponent<ZGame>();
            beacon = FindFirstObjectByType<FlagBeacons>();   // once per level: this layer is attached anew on every level load
            var sh = Shader.Find("OpenZ/Overlay");
            if (!sh || !game) { enabled = false; Debug.Log("[OpenZ] neutral flag waves: off (no overlay shader or no game)"); return; }
            mat = new Material(sh) { name = "OZ_NeutralFlagWave" };
            mat.SetColor("_Color", Tint); mat.SetFloat("_ZTest", 4);
            Debug.Log($"[OpenZ] neutral flag waves: on ({game.level}; {(BeaconHere ? "the capture beacon marks the flags here: no wave on top of it" : "waves on neutral flags while on camera")})");
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            foreach (var w in waves.Values) if (w.mesh) Destroy(w.mesh);
            if (mat) Destroy(mat);
        }

        /// <summary>The older capture beacon really draws in this level (FlagBeacons.cs): a live, enabled beacon in the loaded level
        /// and its own level switch on (its own test). Then it is the flags' one cue. Not there (a level loaded from the menu, as in
        /// the game) or switched off: the wave runs.</summary>
        public bool BeaconHere => game && beacon && beacon.isActiveAndEnabled && (game.level == "L17" ? FlagBeacons.OnL17 : FlagBeacons.OnL02);

        void LateUpdate()
        {
            if (!game || game.World == null || !mat) return;
            if (!cam || !cam.isActiveAndEnabled) cam = Camera.main;
            if (!cam) return;
            float now = Time.time, dt = Time.deltaTime, night = Mathf.Clamp01(NightGrade.NightK);
            bool beaconHere = BeaconHere;
            foreach (var f in game.World.Flags)
            {
                if (f == null || f.Destroyed) continue;
                bool neutral = f.Owner == Team.Neutral;
                waves.TryGetValue(f.Id, out var w);
                if (w == null)
                {
                    if (!neutral || beaconHere) continue;
                    w = waves[f.Id] = new Wave { id = f.Id, foot = game.CenterWorld(f), rng = new System.Random(f.Id * 7919 + 17) };
                }
                bool onCam = OnCamera(w.foot);
                if (w.t0 < 0f)
                {   // between waves: wait for the next one; only a neutral flag on camera starts one
                    if (!neutral || beaconHere || !onCam) { if (!onCam) w.seen = false; continue; }
                    if (!w.seen) { w.seen = true; if (w.next < now) w.next = now + Lerp(w.rng, FirstMin, FirstMax); }
                    if (now < w.next) continue;
                    StartWave(w, f, now, onCam, neutral);
                }
                float t = (now - w.t0) / WaveSeconds;
                if (!neutral || beaconHere) w.kill = Mathf.MoveTowards(w.kill, 0f, dt / OwnedFade);
                if (t >= 1f || w.kill <= 0f) { w.t0 = -1f; w.next = now + Lerp(w.rng, GapMin, GapMax); continue; }
                if (!onCam) continue;
                float peak = Mathf.Lerp(PeakDay, PeakNight, night) * w.kill;
                Paint(w, t, peak);
                Graphics.DrawMesh(w.mesh, Matrix4x4.identity, mat, 0, null, 0, null, ShadowCastingMode.Off, false);
            }
        }

        static float Lerp(System.Random r, float a, float b) => a + (b - a) * (float)r.NextDouble();

        bool OnCamera(Vector3 p)
        {
            var v = cam.WorldToViewportPoint(p);
            return v.z > 0f && v.x > -0.05f && v.x < 1.05f && v.y > -0.05f && v.y < 1.05f;
        }

        void StartWave(Wave w, ZObject f, float now, bool onCam, bool neutral)
        {
            if (!w.mesh) Build(w); else Drape(w);
            w.t0 = now; w.kill = 1f;
            Started++; LastFlagId = f.Id; if (!onCam || !neutral) StartedWrong++;
        }

        // ------------------------------------------------------------------ the draped polar grid
        void Build(Wave w)
        {
            int rings = Mathf.CeilToInt((R1 + 2f * Band - (R0 - 2f * Band)) / Cell) + 1, n = (Seg + 1) * rings;
            w.v = new Vector3[n]; w.c = new Color[n]; w.skip = new bool[n]; w.r = new float[n];
            var tri = new List<int>(Seg * (rings - 1) * 6);
            for (int i = 0; i <= Seg; i++)
                for (int k = 0; k < rings; k++)
                {
                    int q = i * rings + k; w.r[q] = Mathf.Max(0.05f, R0 - 2f * Band) + k * Cell;
                    if (i < Seg && k < rings - 1) tri.AddRange(new[] { q, q + 1, q + rings + 1, q, q + rings + 1, q + rings });
                }
            w.mesh = new Mesh { name = "OZ_NeutralFlagWave_" + w.id };
            w.mesh.MarkDynamic();
            Drape(w);
            w.mesh.SetTriangles(tri, 0);
            w.mesh.RecalculateBounds();
        }

        /// <summary>Every vertex on the rendered ground (sampled now: craters change it), cells over water or down a step left out.</summary>
        void Drape(Wave w)
        {
            int rings = w.v.Length / (Seg + 1);
            float footY = Ground(w.foot.x, w.foot.z);
            for (int i = 0; i <= Seg; i++)
            {
                float a = i * Mathf.PI * 2f / Seg, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                for (int k = 0; k < rings; k++)
                {
                    int q = i * rings + k; float x = w.foot.x + ca * w.r[q], z = w.foot.z + sa * w.r[q];
                    float y = Ground(x, z, out bool deck);
                    w.v[q] = new Vector3(x, y + Lift, z);
                    w.skip[q] = Mathf.Abs(y - footY) > StepMax || (!deck && Water(x, z));
                }
            }
            w.mesh.vertices = w.v;
            w.mesh.RecalculateBounds();
        }

        /// <summary>One frame of the wave: a soft band at the wave's radius, rising fast, fading as it spreads.</summary>
        void Paint(Wave w, float t, float peak)
        {
            float e = 1f - (1f - t) * (1f - t), rad = Mathf.Lerp(R0, R1, e);
            float a = peak * Mathf.SmoothStep(0f, 1f, t / 0.15f) * Mathf.Pow(1f - t, 1.5f);
            LastPeak = Mathf.Max(LastPeak, a);
            for (int q = 0; q < w.c.Length; q++)
            {
                float d = (w.r[q] - rad) / Band, k = w.skip[q] ? 0f : Mathf.Exp(-d * d * 2f);
                if (w.r[q] < R0) k *= Mathf.Clamp01((w.r[q] - (R0 - 2f * Band)) / (2f * Band));   // nothing on the pole's own foot
                w.c[q] = new Color(1f, 1f, 1f, a * k);
            }
            w.mesh.colors = w.c;
        }

        // ------------------------------------------------------------------ the ground (the burn marks' rule, ScorchDecals.Ground)
        public static float Ground(float x, float z) => Ground(x, z, out _);
        public static float Ground(float x, float z, out bool deck)
        {
            deck = false;
            var s = TrackMarks.I ? TrackMarks.I.Surface : null;
            if (s != null && s.Sample(x, z, out float y, out deck)) return y + (deck ? 0f : CraterField.FinalMissing(x, z));
            return (ZGame.I ? ZGame.I.GroundY(x, z) : 0f) + CraterField.FinalMissing(x, z);
        }

        static bool Water(float x, float z)
        {
            var g = ZGame.I; if (g == null || g.World == null) return false;
            var p = ZGame.WorldToPx(new Vector3(x, 0, z));
            var c = g.World.Map.ClassAt(p.x >> 4, p.y >> 4);
            return c == ZClass.Water || c == ZClass.DeepWater;
        }

        // ------------------------------------------------------------------ for the checks and the pictures
        /// <summary>Starts a wave on flag id now (a neutral flag; the pictures). False if it is not a neutral flag of this level.</summary>
        public bool Force(int id)
        {
            if (!game || game.World == null || !mat) return false;
            foreach (var f in game.World.Flags)
                if (f != null && f.Id == id && f.Owner == Team.Neutral)
                {
                    if (!waves.TryGetValue(id, out var w)) w = waves[id] = new Wave { id = id, foot = game.CenterWorld(f), rng = new System.Random(id * 7919 + 17) };
                    if (!cam) cam = Camera.main;
                    StartWave(w, f, Time.time, cam && OnCamera(w.foot), true); w.seen = true;
                    return true;
                }
            return false;
        }

        /// <summary>The flags with a built ring: id, foot, whether a wave runs now.</summary>
        public IEnumerable<(int id, Vector3 foot, bool running)> Rings() { foreach (var w in waves.Values) if (w.mesh) yield return (w.id, w.foot, w.t0 >= 0f); }

        /// <summary>How far the drawn ring of flag id lies under the ground, worst over every cell centre and edge middle that is
        /// drawn (m; 0 or less = never under it), and how far above at most: the ring sampled against the ground again now.</summary>
        public bool Clearance(int id, out float under, out float over, out int samples)
        {
            under = float.NegativeInfinity; over = 0f; samples = 0;
            if (!waves.TryGetValue(id, out var w) || !w.mesh) return false;
            int rings = w.v.Length / (Seg + 1);
            for (int i = 0; i < Seg; i++)
                for (int k = 0; k < rings - 1; k++)
                {
                    int q = i * rings + k;
                    if (w.skip[q] || w.skip[q + 1] || w.skip[q + rings] || w.skip[q + rings + 1]) continue;
                    foreach (var p in new[] { (w.v[q] + w.v[q + 1] + w.v[q + rings] + w.v[q + rings + 1]) * 0.25f, (w.v[q] + w.v[q + 1]) * 0.5f, (w.v[q] + w.v[q + rings]) * 0.5f, w.v[q] })
                    {
                        float gy = Ground(p.x, p.z), d = gy - p.y;                                  // > 0: the ring is under the ground there
                        under = Mathf.Max(under, d); over = Mathf.Max(over, -d); samples++;
                    }
                }
            return samples > 0;
        }
    }
}
