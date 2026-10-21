using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// component of its own on the shuttle's prefab root (a later unit version keeps it and only raises <see cref="strength"/>).
    /// From each engine mouth (the model's markers MK_Exhaust_L, MK_Exhaust_R: the middle of the mouth, 7 cm outside its lip) and
    /// from the two small vents on the roof's rear shoulders (MK_Vent_L, MK_Vent_R) one pale puff every
    /// <see cref="every"/> seconds: slow, sparse, never a column. A puff leaves along the mouth (away from the ship's middle),
    /// rises a little and drifts with the level's wind (the air's own wind _FogWind, the wind the flags and the plants take;
    /// calm: OzJungleAir.CalmWind).
    /// THE GAME'S OWN PARTICLES AND THEIR QUALITY RULES: the puffs are particles of the game's soft smoke channel
    /// (OpenZFxPlayer channel "leg_smoke": the smoke picture sheet of every smoke of the game, its soft edges against the
    /// ground, its sorting, its one shared pool of 640 particles; nothing is made here, no particle system, no material). As the
    /// base props' chimneys do, nothing is emitted while the ship is off screen, while the game is paused or while the effects
    /// player is off.
    /// THE WISP'S SHAPE (round 3: the single pale puffs at the pod tips read as stray grey balls): each point now lets go a thin wisp,
    /// a short upright string of small soft beads (3 at a mouth, 2 at a vent) born together, a little apart in time and rising at
    /// different speeds, so they draw out into one upward-stretched streak about twice as tall as it is wide; the beads are
    /// <see cref="BeadSizeK"/> of the set width, the first at most <see cref="AlphaMax"/> opaque and the ones above fainter; they
    /// leave the mouth slowly (<see cref="PushK"/> of the set push) and rise at least <see cref="WispRise"/> m/s. The set numbers on
    /// the prefab (every, life, sizes, alpha, push, rise) still steer it; these code numbers only make the shape. About 10 beads a
    /// second are born and each lives under 3 s: 30 particles of the pool or fewer.
    /// <see cref="Mode"/>: 0 all four points, 1 damaged (the left engine and the left vent only: the right engine is dead),
    /// 2 none (a destroyed ship has the wreck's own smoke).
    /// View only. Play check: Emitted, Alive, LastWind.
    /// </summary>
    public sealed class OzShuttleExhaust : MonoBehaviour
    {
        [Header("set by the menu OpenZ/L16 Jungle/14 (the model's markers)")]
        [Tooltip("the two engine mouths (left, right)")] public Transform[] mouths = new Transform[0];
        [Tooltip("the two small vents on the roof (left, right)")] public Transform[] vents = new Transform[0];
        [Header("the wisp")]
        public string channel = "leg_smoke";
        [Tooltip("seconds between two puffs of one point (drawn anew for each puff)")] public Vector2 every = new Vector2(0.9f, 1.6f);
        [Tooltip("seconds a puff lives")] public Vector2 life = new Vector2(2.4f, 3.3f);
        [Tooltip("a puff's width when it is born, at an engine mouth (m; the channel lets it grow to 3.2 times that)")] public Vector2 mouthSize = new Vector2(0.34f, 0.50f);
        [Tooltip("the same at a roof vent")] public Vector2 ventSize = new Vector2(0.20f, 0.30f);
        [Tooltip("linear-looking pale grey (the smoke shader lights it)")] public Color colour = new Color(0.80f, 0.82f, 0.84f, 1f);
        [Range(0f, 1f)] public float alpha = 0.24f;
        [Tooltip("m/s away from the ship's middle, and up")] public float push = 0.45f;
        public float rise = 0.35f;
        [Tooltip("share of the level's wind a puff starts with (2.5 m/s calm: 0.3 = 0.75 m/s)")] [Range(0f, 1f)] public float windShare = 0.3f;
        [Tooltip("1 = parked. A unit version raises it with the engines (more puffs, wider, faster)")] [Range(0f, 4f)] public float strength = 1f;

        /// <summary>The wisp: beads at a mouth / a vent, the beads' width (x the set size), the first bead's most opacity (and its share of
        /// the set alpha), each next bead's opacity x, the beads' start apart (s), their rise speeds (x, first to last), the least rise
        /// (m/s), the share of the set push, the share of the set life.</summary>
        public static int MouthBeads = 3, VentBeads = 2;
        public static float BeadSizeK = 0.42f, AlphaMax = 0.18f, AlphaShare = 0.75f, BeadFade = 0.72f, BeadGapSec = 0.15f, RiseFirst = 1.3f, RiseLast = 0.75f, WispRise = 0.55f, PushK = 0.3f, LifeK = 0.85f;
        /// <summary>Proof: beads born, the most opacity a bead was given, the wisp's height over its width at the end of its life (estimate).</summary>
        public static int Beads;
        public static float AlphaUsedMax, StretchEst;

        /// <summary>0 all points, 1 damaged (left side only), 2 none.</summary>
        public int Mode { get; private set; }
        /// <summary>Puffs born since the start, and how many of them are still alive (born less than their life ago).</summary>
        public int Emitted { get; private set; }
        public int Alive { get { Trim(); return alive.Count; } }
        /// <summary>The wind the last puff took (m/s, before the share).</summary>
        public Vector3 LastWind { get; private set; }
        /// <summary>Why nothing was born in the last frame it could have been ("" = it was).</summary>
        public string Held { get; private set; } = "";

        static readonly int FogWindId = Shader.PropertyToID("_FogWind");
        readonly Queue<float> alive = new Queue<float>();
        float[] next = new float[0];

        public void SetMode(int mode) { Mode = Mathf.Clamp(mode, 0, 2); }

        /// <summary>The wisp's height over its width at the end of its life (the channel grows a particle to 3.2 x its birth width): the
        /// beads' spread in height (the rise speeds apart) + one bead, over one bead.</summary>
        public static float Stretch(float rise, float life, float width)
        {
            float end = width * 3.2f, spread = rise * (RiseFirst - RiseLast) * life;
            return end > 1e-4f ? (spread + end) / end : 1f;
        }

        void Trim() { while (alive.Count > 0 && alive.Peek() <= Time.time) alive.Dequeue(); }

        /// <summary>The level's wind (m/s): the air's own, else the calm wind of the level.</summary>
        public static Vector3 Wind()
        {
            Vector4 fw = Shader.GetGlobalVector(FogWindId);
            var w = new Vector3(fw.x, 0f, fw.z);
            return w.sqrMagnitude < 0.01f ? new Vector3(OzJungleAir.CalmWind.x, 0f, OzJungleAir.CalmWind.z) : w;
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            Trim();
            int n = mouths.Length + vents.Length;
            if (next.Length != n) { next = new float[n]; for (int i = 0; i < n; i++) next[i] = Time.time + Random.Range(0f, every.y); }
            if (Mode >= 2 || strength <= 0.01f) { Held = "off"; return; }
            var g = ZGame.I; var fx = OpenZFxPlayer.I;
            if (g && g.paused) { Held = "paused"; return; }
            if (!fx || !fx.isActiveAndEnabled) { Held = "effects off"; return; }
            if (!DetailKit.OnScreen(Camera.main, transform.position + Vector3.up * 2f, 0.15f)) { Held = "off screen"; return; }
            Held = "";
            float now = Time.time;
            for (int i = 0; i < n; i++)
            {
                if (now < next[i]) continue;
                next[i] = now + Random.Range(every.x, every.y) / Mathf.Max(0.25f, strength);
                bool mouth = i < mouths.Length; int side = mouth ? i : i - mouths.Length;
                if (Mode == 1 && side != 0) continue;                                           // damaged: the right engine is dead
                var t = mouth ? mouths[i] : vents[side]; if (!t) continue;
                var p = t.position; var away = p - transform.position; away.y = 0f;
                away = away.sqrMagnitude < 1e-4f ? Vector3.zero : away.normalized;
                var wind = Wind(); LastWind = wind;
                float k = Mathf.Sqrt(Mathf.Max(0.25f, strength));
                var drift = (mouth ? away * push : away * (push * 0.2f)) * (PushK * k) + wind * windShare
                            + new Vector3(Random.Range(-0.05f, 0.05f), 0f, Random.Range(-0.05f, 0.05f));
                float up = Mathf.Max(rise, WispRise) * (mouth ? 1f : 1.15f) * k;
                float l = Random.Range(life.x, life.y) * LifeK;
                var sz = mouth ? mouthSize : ventSize; float w = Random.Range(sz.x, sz.y) * k * BeadSizeK;
                int nb = Mathf.Max(1, mouth ? MouthBeads : VentBeads);
                float a = Mathf.Min(alpha * AlphaShare, AlphaMax);
                var at = p + new Vector3(Random.Range(-0.04f, 0.04f), Random.Range(-0.03f, 0.03f), Random.Range(-0.04f, 0.04f));
                for (int b = 0; b < nb; b++)
                {   // an upright string: born together, a little apart in time, the first rising fastest, the ones above fainter
                    float f = nb > 1 ? b / (float)(nb - 1) : 0f, delay = b * BeadGapSec, rk = Mathf.Lerp(RiseFirst, RiseLast, f);
                    var v = drift + Vector3.up * (up * rk) + new Vector3(Random.Range(-0.02f, 0.02f), Random.Range(-0.02f, 0.03f), Random.Range(-0.02f, 0.02f));
                    float ab = a * Mathf.Pow(BeadFade, b);
                    fx.EmitChannel(channel, at, v, l, w * Mathf.Lerp(1f, 0.85f, f), colour, ab, delay);
                    Beads++; AlphaUsedMax = Mathf.Max(AlphaUsedMax, ab);
                }
                StretchEst = Stretch(up, l, w);
                Emitted++; alive.Enqueue(now + l + (nb - 1) * BeadGapSec);
            }
        }
    }
}
