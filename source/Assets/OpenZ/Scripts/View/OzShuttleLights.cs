using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// A later version of the ship may be a unit: this component knows nothing of the map
    /// item, the detail or the level, only its renderers, the game clock and the night share). EMISSION ONLY: no real light,
    /// nothing in the night-light budget. Each light group is its own part of the model and gets its glow through its
    /// renderer's property block (the ship's ONE material has a black emission tint, so the hull and the wreck never glow).
    /// On the GAME clock (the rules' time): the same at any frame rate, still in a pause.
    /// <list type="bullet">
    /// <item>the amber position lamps: two sets in turn (<see cref="lampPeriod"/>, each lit <see cref="lampLit"/> s, set B half a
    /// period after set A; A = the ship's left side, B = its right side), and each set's soft amber glow on the grass beside its
    /// side (the game's ground-pool picture, NightMeshes.Pool: it drapes over the ground, no real light);</item>
    /// <item>window band, flank ports, landing lights: steady with a faint flicker; the two engine mouths: a soft pulse
    /// (<see cref="enginePeriod"/>);</item>
    /// <item>THE NEON STRIPS (white in the emission picture, tinted here): the RUNNING strips (a hoop round each engine nacelle
    /// behind its mouth, an arc over the roof behind the dome, a line under the window band) are cut into three sets of dashes
    /// that light one after another, a full run every <see cref="neonRunPeriod"/> s, each set fading into the next
    /// (<see cref="neonRunFade"/>), never quite dark between its turns (<see cref="neonRunFloor"/>); the BLINKING strips (a ring
    /// on the roof, a ring round each pad) breathe once every <see cref="neonBlinkPeriod"/> s. Two colours chosen by the team
    /// the ship is drawn in, a cool one and a warm one: the running strips take the colour that stands off the team paint (red
    /// ship: ice cyan; blue ship: amber), the blinking strips the colour akin to it (red ship: hot orange-red; blue ship:
    /// electric blue).</item>
    /// <item>BLOOM: the game's bloom takes what is brighter than 1.63 (linear) by day (threshold 1.25 of the post volume) and 0.89
    /// at night (NightGrade.BloomThreshold 0.95). A lit neon strip is written at <see cref="neonGain"/> x its tint (whose
    /// strongest channel is 1): 4.0 by day (2.5 x the day threshold), 6.5 at night (7 x the night threshold), the picture's
    /// white middle of the strip at full strength.</item>
    /// </list>
    /// <see cref="Mode"/>: 0 all lights, 1 damaged (the right side's lamp set and its ground glow dead, the band dim and
    /// stuttering, the engines coughing, the blinking neon dead, the running neon stuttering), 2 dark (everything off).
    /// View only. Play check: the level and emission properties below, ShowLamps.
    /// </summary>
    public sealed partial class OzShuttleLights : MonoBehaviour
    {
        [Header("set by the menu OpenZ/L16 Jungle/14 (the model's light parts)")]
        public Renderer steady;
        public Renderer lampsA, lampsB, engineGlow;
        [Tooltip("the three sets of dashes of the running neon strips (SH_NeonRun0, 1, 2)")] public Renderer[] neonRun = new Renderer[0];
        [Tooltip("the blinking neon strips: the roof ring and the pads' rings (SH_NeonBlink)")] public Renderer neonBlink;
        [Tooltip("on the ground plane beside the ship's two sides: where lamp set A / B throws its glow on the grass")] public Transform glowAtA, glowAtB;
        [Header("lamps, band, engines (seconds on the game clock)")]
        public float lampPeriod = 2.4f;
        public float lampLit = 0.6f, lampFade = 0.12f;
        public float enginePeriod = 3.2f;
        [Tooltip("x the two ground glows: 1 standing; the playable shuttle lowers it with its height (no glow on the grass under a ship in the air)")] public float groundGlowScale = 1f;
        [Tooltip("emission of each group by day and at night (x the emission picture, linear)")] public Vector2 lampGain = new Vector2(2.6f, 4.2f);
        public Vector2 steadyGain = new Vector2(1.1f, 2.6f), engineGain = new Vector2(1.2f, 2.6f);
        public Color lampTint = Color.white, steadyTint = Color.white, engineTint = Color.white;
        [Header("neon strips (seconds on the game clock)")]
        [Tooltip("the three sets light one after another: a full run takes this long")] public float neonRunPeriod = 2.4f;
        [Tooltip("a set fades in and out over this long (it overlaps the next set's fade: the light runs, it does not jump)")] public float neonRunFade = 0.25f;
        [Tooltip("a set's level between its turns (0 = dark)")] [Range(0f, 0.5f)] public float neonRunFloor = 0.12f;
        public float neonBlinkPeriod = 3.6f;
        public float neonBlinkLit = 1.8f, neonBlinkFade = 0.5f;
        [Range(0f, 0.5f)] public float neonBlinkFloor = 0.10f;
        [Tooltip("emission of a lit strip by day and at night (x the emission picture x the tint, linear). The bloom takes what is over 1.63 by day and 0.89 at night")]
        public Vector2 neonGain = new Vector2(4.0f, 6.5f);
        [Tooltip("linear; the strongest channel 1. Red ship: running strips ice cyan, blinking strips hot orange-red")] public Color neonRunRed = new Color(0.10f, 0.80f, 1.00f, 1f);
        public Color neonBlinkRed = new Color(1.00f, 0.20f, 0.06f, 1f);
        [Tooltip("linear; the strongest channel 1. Blue ship: running strips amber, blinking strips electric blue")] public Color neonRunBlue = new Color(1.00f, 0.55f, 0.06f, 1f);
        public Color neonBlinkBlue = new Color(0.12f, 0.45f, 1.00f, 1f);
        [Header("ground glow of the amber lamps (no real light)")]
        [Tooltip("light of a lit lamp set on the grass by day and at night (x glowTint; the ground is drawn x (1 + this))")] public Vector2 glowGain = new Vector2(0.9f, 1.8f);
        [Tooltip("linear")] public Color glowTint = new Color(1f, 0.48f, 0.10f, 1f);
        [Tooltip("across the ship, the height band it may light, along the ship (m)")] public Vector3 glowSize = new Vector3(6.3f, 3.0f, 6.5f);

        /// <summary>0 all lights, 1 damaged, 2 dark.</summary>
        public int Mode { get; private set; }
        /// <summary>The team whose colours the neon strips are tinted for (the team the ship is drawn in).</summary>
        public Team NeonTeam { get; private set; } = Team.Red;
        /// <summary>The light levels set in the last frame (0 to 1, before the day / night gain), and the night share used (0 day, 1 night).</summary>
        public float LampA { get; private set; }
        public float LampB { get; private set; }
        public float Steady { get; private set; }
        public float Engine { get; private set; }
        public float NeonBlink { get; private set; }
        public float Night { get; private set; }
        readonly float[] neonRunLevel = new float[3];
        /// <summary>The level of running set k (0, 1, 2) in the last frame.</summary>
        public float NeonRun(int k) => k >= 0 && k < 3 ? neonRunLevel[k] : 0f;
        /// <summary>The emission multipliers written in the last frame (x the emission picture; the neon ones x the tint's strongest channel).</summary>
        public float EmissionLampA { get; private set; }
        public float EmissionLampB { get; private set; }
        public float EmissionSteady { get; private set; }
        public float EmissionEngine { get; private set; }
        public float EmissionNeonRun { get; private set; }
        public float EmissionNeonBlink { get; private set; }
        /// <summary>The colours the two kinds of strips are lit in now (linear, before the level and the gain).</summary>
        public Color NeonRunColour => NeonTeam == Team.Blue ? neonRunBlue : neonRunRed;
        public Color NeonBlinkColour => NeonTeam == Team.Blue ? neonBlinkBlue : neonBlinkRed;
        /// <summary>The light written to the two ground glows in the last frame (x glowTint), and their renderers (made at the start of Play).</summary>
        public float GroundGlowA { get; private set; }
        public float GroundGlowB { get; private set; }
        public Renderer GlowA { get; private set; }
        public Renderer GlowB { get; private set; }

        static readonly int EmMapColorId = Shader.PropertyToID("_EmissionMapColor"), NightId = Shader.PropertyToID("_OZNight");
        MaterialPropertyBlock mpb, mpbGlow; Transform glowHolder; Vector3 glowsAt;
        readonly float[] last = new float[10];

        void Awake() { mpb = new MaterialPropertyBlock(); mpbGlow = new MaterialPropertyBlock(); Forget(); }
        void Forget() { for (int i = 0; i < last.Length; i++) last[i] = -1f; }
        void OnEnable() { Forget(); }
        void OnDisable() { if (GlowA) GlowA.enabled = false; if (GlowB) GlowB.enabled = false; Forget(); }
        void OnDestroy() { if (glowHolder) Destroy(glowHolder.gameObject); }

        /// <summary>The game clock in seconds (the rules' time: it follows the game speed and stands still in a pause).</summary>
        public static double GameClock() { var g = ZGame.I; return g && g.World != null ? g.World.Time : Time.timeAsDouble; }

        /// <summary>0 all lights, 1 damaged, 2 dark (written at once, so a destroyed ship is dark in the same frame).</summary>
        public void SetMode(int mode)
        {
            Mode = Mathf.Clamp(mode, 0, 2);
            if (Mode == 2) { Forget(); Write(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f); }
        }

        /// <summary>The team the ship is drawn in: the neon strips take their two colours from it.</summary>
        public void SetTeam(Team shown) { if (shown == NeonTeam) return; NeonTeam = shown; last[4] = last[5] = last[6] = last[7] = -1f; }

        /// <summary>A lamp set's level at clock t: lit for lampLit seconds at the start of each period (shifted by `shift` periods), soft edges.</summary>
        public float LampLevel(double t, float shift)
        {
            float p = Mathf.Max(0.2f, lampPeriod);
            float x = (float)((t / p + shift) % 1.0) * p;                                             // seconds into this period
            float f = Mathf.Max(0.01f, lampFade);
            return Mathf.Clamp01(Mathf.Min(x / f, (lampLit - x) / f));
        }
        public static float SteadyLevel(double t) => 0.95f + 0.03f * Mathf.Sin((float)(t * 5.215)) + 0.02f * Mathf.Sin((float)(t * 13.63 + 1.3));
        public float EngineLevel(double t) => 0.5f + 0.22f * Mathf.Sin((float)(t * 2.0 * Mathf.PI / Mathf.Max(0.2f, enginePeriod)));

        /// <summary>Running set k at clock t: its turn is the k-th third of the period; it rises over neonRunFade at the start of its
        /// turn and falls over neonRunFade after its end (while the next set rises), and keeps neonRunFloor between its turns.</summary>
        public float NeonRunLevel(double t, int k)
        {
            float p = Mathf.Max(0.3f, neonRunPeriod), turn = p / 3f, f = Mathf.Clamp(neonRunFade, 0.01f, turn);
            float u = (float)(((t / p - k / 3.0) % 1.0 + 1.0) % 1.0) * p;                              // seconds since this set's turn began
            float up = Mathf.Clamp01(u / f), down = Mathf.Clamp01((turn + f - u) / f);
            if (u > p - 1e-4f) up = 0f;
            return neonRunFloor + (1f - neonRunFloor) * Mathf.Min(up, down);
        }

        /// <summary>The blinking strips at clock t: lit for neonBlinkLit seconds of each period with soft edges, neonBlinkFloor between.</summary>
        public float NeonBlinkLevel(double t)
        {
            float p = Mathf.Max(0.3f, neonBlinkPeriod), f = Mathf.Max(0.01f, neonBlinkFade);
            float u = (float)(((t / p) % 1.0 + 1.0) % 1.0) * p;
            return neonBlinkFloor + (1f - neonBlinkFloor) * Mathf.Clamp01(Mathf.Min(u / f, (neonBlinkLit - u) / f));
        }

        void Update()
        {
            if (!Application.isPlaying || Mode >= 2) return;
            if (!glowHolder) BuildGlows(); else FollowGlows();
            double t = GameClock();
            Night = Mathf.Clamp01(Shader.GetGlobalFloat(NightId));
            float a = LampLevel(t, 0f), b = LampLevel(t, 0.5f), s = SteadyLevel(t), e = EngineLevel(t);
            float n0 = NeonRunLevel(t, 0), n1 = NeonRunLevel(t, 1), n2 = NeonRunLevel(t, 2), nb = NeonBlinkLevel(t);
            if (Mode == 1)
            {   // damaged: the right side's lamp set is dead, the band is dim and stutters, the engines cough, the neon is half dead
                b = 0f;
                float st = Mathf.Sin((float)(t * 23.0)) * Mathf.Sin((float)(t * 7.3 + 0.7));
                s *= st > 0.55f ? 0.15f : 0.55f;
                e *= 0.45f + 0.25f * Mathf.Sin((float)(t * 9.1));
                float nk = st > 0.3f ? 0.08f : 0.6f;
                n0 *= nk; n1 *= nk; n2 *= nk; nb = 0f;
            }
            float pw = power;                                                                         // K389: the capture's start / the crew leaving (OzShuttleLights.Power.cs)
            Write(a * pw, b * pw, s * pw, e * pw, n0 * pw, n1 * pw, n2 * pw, nb * pw);
        }

        /// <summary>The light levels (0 to 1) written to the ship's light parts and to the two ground glows, x the day / night gains.</summary>
        void Write(float a, float b, float s, float e, float n0, float n1, float n2, float nb)
        {
            float lg = Mathf.Lerp(lampGain.x, lampGain.y, Night), gg = Mathf.Lerp(glowGain.x, glowGain.y, Night), ng = Mathf.Lerp(neonGain.x, neonGain.y, Night);
            LampA = a; LampB = b; Steady = s; Engine = e; neonRunLevel[0] = n0; neonRunLevel[1] = n1; neonRunLevel[2] = n2; NeonBlink = nb;
            EmissionLampA = a * lg; EmissionLampB = b * lg;
            EmissionSteady = s * Mathf.Lerp(steadyGain.x, steadyGain.y, Night); EmissionEngine = e * Mathf.Lerp(engineGain.x, engineGain.y, Night);
            EmissionNeonRun = Mathf.Max(n0, Mathf.Max(n1, n2)) * ng; EmissionNeonBlink = nb * ng;
            GroundGlowA = a * gg * groundGlowScale; GroundGlowB = b * gg * groundGlowScale;
            Glow(0, lampsA, lampTint, EmissionLampA); Glow(1, lampsB, lampTint, EmissionLampB);
            Glow(2, steady, steadyTint, EmissionSteady); Glow(3, engineGlow, engineTint, EmissionEngine);
            var rc = NeonRunColour; var bc = NeonBlinkColour;
            for (int k = 0; k < 3 && k < neonRun.Length; k++) Glow(4 + k, neonRun[k], rc, neonRunLevel[k] * ng);
            Glow(7, neonBlink, bc, EmissionNeonBlink);
            GroundGlow(8, GlowA, GroundGlowA); GroundGlow(9, GlowB, GroundGlowB);
        }

        /// <summary>For a check: both lamp sets (lenses and ground glows) shown at these levels at once, until the next frame sets
        /// them by the clock again. Nothing while the lights are dark.</summary>
        public void ShowLamps(float a, float b)
        {
            if (Mode >= 2 || !isActiveAndEnabled) return;
            if (!glowHolder) BuildGlows();
            Night = Mathf.Clamp01(Shader.GetGlobalFloat(NightId));
            Write(Mathf.Clamp01(a), Mathf.Clamp01(b), Steady, Engine, neonRunLevel[0], neonRunLevel[1], neonRunLevel[2], NeonBlink);
        }

        /// <summary>For a check: the neon strips shown at these levels (running sets all at `run`, the blinking strips at `blink`), until the next frame.</summary>
        public void ShowNeon(float run, float blink)
        {
            if (Mode >= 2 || !isActiveAndEnabled) return;
            Night = Mathf.Clamp01(Shader.GetGlobalFloat(NightId));
            run = Mathf.Clamp01(run);
            Write(LampA, LampB, Steady, Engine, run, run, run, Mathf.Clamp01(blink));
        }

        void Glow(int k, Renderer r, Color tint, float level)
        {
            if (!r || Mathf.Abs(last[k] - level) < 0.004f) return;
            last[k] = level;
            r.GetPropertyBlock(mpb, 0);
            mpb.SetVector(EmMapColorId, new Vector4(tint.r * level, tint.g * level, tint.b * level, 1f));     // linear, x the emission picture
            r.SetPropertyBlock(mpb, 0);
        }

        void GroundGlow(int k, Renderer r, float level)
        {
            if (!r) return;
            bool on = level > 0.004f;
            if (Mathf.Abs(last[k] - level) < 0.004f && r.enabled == on) return;
            last[k] = level;
            if (r.enabled != on) r.enabled = on;
            if (!on) return;
            mpbGlow.SetColor(NightMeshes.ColorId, new Color(glowTint.r * level, glowTint.g * level, glowTint.b * level, 1f));
            r.SetPropertyBlock(mpbGlow);
        }

        /// <summary>The two ground glows: the game's ground-pool volume (a unit cube over the lit ellipse; its picture drapes over
        /// the ground inside it), one beside each side of the ship, lying along the ship. Not children of the ship: the detail's
        /// break and the team paint walk the ship's own renderers. They follow the ship when it stands somewhere else.</summary>
        void BuildGlows()
        {
            if (glowHolder || !glowAtA || !glowAtB) return;
            var mat = NightMeshes.Pool(); if (!mat) return;
            glowHolder = new GameObject(name + "_GroundGlow").transform; glowHolder.SetParent(transform.parent, false);
            GlowA = OneGlow("GlowA", mat); GlowB = OneGlow("GlowB", mat);
            PlaceGlows();
        }

        Renderer OneGlow(string n, Material mat)
        {
            var go = NightMeshes.Part(n, glowHolder, NightMeshes.Cube(), mat, false, Vector3.zero, Quaternion.identity, Vector3.one);
            go.layer = gameObject.layer;
            var r = go.GetComponent<Renderer>(); r.enabled = false;
            return r;
        }

        void PlaceGlows()
        {
            var across = glowAtB.position - glowAtA.position; across.y = 0f;
            if (across.sqrMagnitude < 1e-4f) across = transform.right; across.Normalize();
            var rot = Quaternion.LookRotation(Vector3.Cross(Vector3.up, across), Vector3.up);                 // x across the ship, z along it
            var g = ZGame.I;
            foreach (var (r, at) in new[] { (GlowA, glowAtA.position), (GlowB, glowAtB.position) })
            {
                if (!r) continue;
                float gy = g ? g.TerrainY(at.x, at.z) : at.y;
                r.transform.SetPositionAndRotation(new Vector3(at.x, gy + 0.35f, at.z), rot);                 // the pool shader takes the ground 0.35 m under the volume's middle
                r.transform.localScale = glowSize;
            }
            glowsAt = glowAtA.position;
        }

        void FollowGlows() { if ((glowAtA.position - glowsAt).sqrMagnitude > 0.0025f) PlaceGlows(); }
    }
}
