using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// The view of the sim's DV45 events (ZObject.Electro.cs), all procedural, our own work:
    /// <list type="bullet">
    /// <item><b>Windup</b> (ElectroCharge, 0.35 s): ElectrobotView spins the barrels up, flares the cells and grows a charge ball at the
    /// emitter with ring arcs converging on it; a charge whine; a light swelling at the gun.</item>
    /// <item><b>Bolt</b> (ElectroBolt): two main bolts twisting around each other from the emitter ring to the target's surface (17
    /// jagged points each, 2-4 forks, crackle filaments, re-randomised every 33 ms: leader 35 ms, return stroke, re-strike, afterglow
    /// to 0.45 s; a 0.6 m ribbon with a white-hot core (8 cm, HDR 9) in a cyan glow (HDR 2.6), inside a wide soft blue aura (3.6 m,
    /// HDR 1.5, its own slower fade): over the bloom threshold by day and at night), a blue energy sphere at the emitter (1.9 m) and
    /// at the impact (3.4 m, to 0.45 s), 8 arcs bursting round the ring, the coils discharging into the gun, 3-5 ground strikes and a
    /// radial burst of 10 short arcs from the hit point; the impact: a white flash, a blue flash disc on the target, a shock ring
    /// on the ground (0 -> 4 m, visual only: no splash), a blue ground wash (a conforming additive disc, reads blue on sand by day), 40-60
    /// bouncing sparks, an ozone puff, a scorch + a Lichtenberg fern that glows then cools, the target flashes; REAL lights (muzzle,
    /// travelling, impact) that light the ground, cliffs, water and units, day x1.6 like the pyro, the range x0.75 at night like the
    /// pyro (nightRangeScale); a small camera kick (on screen only), a bloom sustain (no full-screen flash); recoil (gun climb + body
    /// rock); the fire crack at the gun, the zap at the target, the recharge whine from +0.25 s whose ready ping lands on
    /// ElectroReady.</item>
    /// <item><b>Stun</b> (StunStart / Refresh / Pulse / End): the hit moment shakes the target smoothly (a damped 6 Hz swing along the
    /// bolt + 5 Hz across it, 2-3 visible cycles with the first swing the largest, never a per-frame random offset; the head snaps
    /// back), then for the whole stun 3-6 arcs crawl over its body (bones / hull), sparks drip, the body glows a flat pulsing cyan
    /// (TeamLit _EmissionColor written as LINEAR values through SetVector, no fresnel rim), a real blue light and the buzz loop on the
    /// 3 nearest, a draining timer ring on the ground; the last 2 s sputter; the end: a discharge puff, a fizzle and a shake-off.
    /// Cannons and the fort defender (never stunned, D5) get a 1.5 s jolt with arcs.</item>
    /// <item><b>Deaths</b>: a victim killed within 1.5 s of a bolt keeps crawling arcs for 1.5 s, smokes and chars; the Electrobot's
    /// own death blows his coils (short-circuit burst); a new Electrobot leaving the factory gets a lightning strike (D11).</item>
    /// <item><b>Voices</b> (Alan, player team only): zap / kill barks of our Electrobot, his spawn line, our stunned units (the line
    /// by kind and in the unit's own voice colour: vo_electro_stunned_&lt;type&gt;, robots "Legs offline!", vehicles "Engine's fried!").</item>
    /// </list>
    /// Colours: every HDR colour here is LINEAR. Vertex colours (the arc mesh) reach the shader as they are; material colours go through
    /// ElectroColour.Shader (MaterialPropertyBlock.SetVector), never SetColor, which gamma-decodes each channel in this linear project
    /// (the NukeFx "pale white ball" / NightV2 lamp bug: 2.4 x Glow would become (0.02, 1.2, 7.6), royal blue).
    /// Budgets: at most 6 bolts, 24 stun arcs, 6 real lights (priority pool, like the pyro's), one draw call for every arc.
    /// Menu: OpenZ/FX/Electro FX (A/B switch, Show, Report).
    /// </summary>
    [DefaultExecutionOrder(97)]
    public sealed class OpenZElectroFx : MonoBehaviour
    {
        public static OpenZElectroFx I { get; private set; }
        public static bool Enabled = true;
        public static bool AutoCreate = true;

        // ---- colours (LINEAR, used as they are: vertex colours, SetVector): glow #58C8FF-ish cyan (G/B 0.45), a colder ice for the
        //      stun, a deeper electric blue for the wide aura / spheres / ground wash (G/B 0.34: blue-dominant, far brighter than the team paint), white-hot cores; the same for both
        //      teams and never team blue: the team royal blue (47, 79, 168) is linear (.03, .08, .39), G/B 0.2 and 3-8x darker.
        public static readonly Color Glow = new Color(0.06f, 0.45f, 1f);
        public static readonly Color Aura = new Color(0.03f, 0.34f, 1f);
        public static readonly Color Ice = new Color(0.30f, 0.78f, 1f);
        /// <summary>Light.color is an sRGB colour like every light of the project (Unity linearises it); linear (0.07, 0.40, 1).</summary>
        public static readonly Color LightColor = new Color(0.30f, 0.66f, 1f);

        [Header("Look")]
        [Tooltip("The jagged bolt ribbon, full width (m); was 0.42")] [Range(0.2f, 3f)] public float boltWidth = 0.72f;
        /// <summary>
        /// 1.63 linear, x1.35) and ACES: the SPEC's core 12-20 / glow 3-5 on a 0.6 m ribbon turned the whole bolt into one white column
        /// and hid the blue, so the core stays thin and white (8 cm, HDR 9) and the SIZE and the BLUE come from the aura and spheres.</summary>
        [Tooltip("the white-hot core's share of the ribbon (0.14 x 0.6 = 8 cm)")] [Range(0.05f, 0.5f)] public float coreShare = 0.14f;
        [Tooltip("HDR of the core")] [Range(2f, 30f)] public float coreGain = 9f;
        [Tooltip("HDR of the ribbon's cyan glow")] [Range(0.5f, 8f)] public float glowGain = 2.6f;
        [Tooltip("The share of coreGain / glowGain / auraGain kept by full day (1 = night values)")] [Range(0.3f, 1f)] public float dayCoreK = 0.6f, dayGlowK = 0.75f, dayAuraK = 0.55f;
        [Tooltip("the wide soft blue aura on each main bolt: its width (x the bolt: 6 x 0.6 = 3.6 m, was 1.26 m) and HDR glow (was 0.9)")]
        [Range(0f, 8f)] public float auraWidth = 6f, auraGain = 1.5f;
        [Tooltip("the whole strike incl. the afterglow (s); the jagged ribbons fade faster (k^2), the aura and spheres slower (k); was 0.28")]
        [Range(0.1f, 0.8f)] public float strikeSeconds = 0.45f;
        [Tooltip("the blue energy spheres: at the emitter (m), at the impact (m), their HDR glow")] [Range(0f, 4f)] public float emitterSphere = 1.9f;
        [Range(0f, 6f)] public float impactSphere = 3.4f;
        [Range(0f, 6f)] public float sphereGain = 1.6f;
        [Tooltip("the blue ground wash at the impact: radius (m) and HDR (an additive conforming disc: blue on sand even by day)")]
        [Range(0f, 8f)] public float groundWash = 3.6f;
        [Range(0f, 4f)] public float groundWashGain = 1.3f;
        [Range(0, 16)] public int radialArcs = 10;
        [Range(0.01f, 0.1f)] public float restrikeEvery = 0.033f;
        [Range(0f, 1f)] public float cameraKick = 1f;
        [Range(0f, 2f)] public float bloomSustain = 0.35f;
        public bool lichtenberg = true, groundStrikes = true, targetFlash = true;
        [Header("Stun")]
        [Range(0, 8)] public int stunArcsMax = 6;
        [Range(0, 64)] public int stunArcBudget = 24;
        /// <summary>The flat emissive tint's peak (linear x Ice), x stunGlowNight at full night. Set on the default-camera previews: 0.35
        /// turned a stunned unit into a pale cyan ghost (its team paint lost) by day and a glowing silhouette at night; 0.18 reads as
        /// "charged" on the unit and keeps its paint (the earlier gamma-decoded value, ~0.02, was invisible).</summary>
        [Range(0f, 1f)] public float stunGlow = 0.18f;
        [Range(0f, 1f)] public float stunGlowNight = 0.5f;
        [Tooltip("the hit shake's first swing (m): robots, vehicles (+ a 1.5 deg roll); 6 Hz, 2-3 visible cycles (OpenZElectroJolts)")]
        [Range(0f, 0.2f)] public float shakeRobot = 0.09f, shakeVehicle = 0.045f;
        public bool stunRing = true;
        [Header("Lights")]
        [Range(0, 12)] public int maxLights = 6;
        /// <summary>
        /// three-units put ~15x the sun on the sand 2 m under the hit (a white-out to ~6 m that hid the blue); these sit with the pyro jet
        /// (22 at 10 m, the fitted day look): a bright blue-white pool under the hit fading by ~6 m. The BLUE of the ground comes from the
        /// additive ground wash (groundWash) and the GroundLight decal, not from over-driving a light.</summary>
        [Tooltip("three.js units (x 1/pi x LightGain 2 x day 1.6 / night 1.0): the muzzle flash, the travelling bolt, the impact, his death")]
        [Range(0f, 120f)] public float muzzleLight = 20f, boltLight = 28f, impactLight = 36f, deathLight = 40f;
        [Range(0f, 3f)] public float lightGain = 1f;
        [Range(0.5f, 3f)] public float dayLightBoost = 1.6f;
        [Range(0.5f, 3f)] public float nightLightBoost = 1.0f;
        /// <summary>
        /// range reads farther, so every electro light's range shrinks toward this share at full night (OpenZPyroFire.nightRangeScale).</summary>
        [Range(0.3f, 1f)] public float nightRangeScale = 0.75f;
        [Header("Sound / voice")]
        public bool sounds = true, voices = true;
        [Range(0f, 1f)] public float zapBarkChance = 0.3f, stunnedLineChance = 0.4f;

        // ---- diagnostics (menu Report, checks)
        public int Bolts { get; private set; }
        public int Charges { get; private set; }
        public int Fizzles { get; private set; }
        public int Stuns { get; private set; }
        public int StunsActive => stuns.Count;
        public int LightsOn { get; private set; }
        public int PeakLights { get; private set; }
        public int ActiveBolts => bolts.Count;
        public int ElectrobotViews { get; private set; }
        public string Status { get; private set; } = "not started";
        public ElectroArcs Arcs { get; private set; }
        public ElectroScorch Scorch { get; private set; }

        Material arcMat;
        Camera cam; RtsCamera rts; float nextRtsSearch;
        uint rngSeed = 0x51A3B2C1u;
        ElectroBoltShape.Rng rng;
        bool ready;

        // ------------------------------------------------------------------------------------------------ entry (CombatView)
        /// <summary>CombatView.LateUpdate hands every event of these types in (before its own switch).</summary>
        public static bool Wants(ZEventType t) => t >= ZEventType.ElectroCharge || t == ZEventType.Death || t == ZEventType.UnitBuilt;

        public static void Handle(ZEvent e, ZObject a, ZObject b)
        {
            if (!Enabled) return;
            if (!I)
            {
                if (!AutoCreate || !ZGame.I) return;
                new GameObject("OpenZElectroFx").AddComponent<OpenZElectroFx>();
            }
            if (!I || !I.ready || !I.isActiveAndEnabled) return;
            I.OnEvent(e, a, b);
        }

        /// <summary>DV87 (BoardingView.DriverFire.cs): the tesla bolt of an Electrobot who drives a tank, fired from its open hatch (the sim's
        /// DriverShot, Sim/ZObject.DriverFire.cs). The same bolt as on foot with its sparks, light and sounds, from the point `from`
        /// between his arms: he holds no gun in a hatch, so no gun recoils and the hull does not jolt. e carries the ElectroBolt fields
        /// (value = HP removed, flag = stunned, flag2 = killed).</summary>
        public static void DriverBolt(Vector3 from, ZEvent e, ZObject vehicle, ZObject target)
        {
            if (!Enabled) return;
            if (!I)
            {
                if (!AutoCreate || !ZGame.I) return;
                new GameObject("OpenZElectroFx").AddComponent<OpenZElectroFx>();
            }
            if (!I || !I.ready || !I.isActiveAndEnabled) return;
            I.driverBolt = true; I.driverFrom = from;
            try { I.Bolt(e, vehicle, target); }
            finally { I.driverBolt = false; }
        }
        bool driverBolt; Vector3 driverFrom;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded; }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!AutoCreate || !Enabled || I) return;
            if (!ZGame.I && !FindFirstObjectByType<ZGame>()) return;                    // game levels only
            new GameObject("OpenZElectroFx").AddComponent<OpenZElectroFx>();
        }

        void Awake()
        {
            if (I && I != this) { Destroy(gameObject); return; }
            I = this;
            rng = new ElectroBoltShape.Rng(rngSeed);
            var m = Resources.Load<Material>("OpenZ/FX/Electro/M_ElectroArc");               // keeps the shader in player builds
            if (m) arcMat = new Material(m) { name = "ElectroArc (runtime)" };
            else { var s = Shader.Find("OpenZ/ElectroArc"); if (s) arcMat = new Material(s) { name = "ElectroArc (runtime)" }; }
            if (!arcMat || Broken(arcMat))
            {
                Status = "OpenZ/ElectroArc missing or not compiled (Resources/OpenZ/FX/Electro/M_ElectroArc, Shaders/OpenZElectroArc.shader)";
                Debug.LogError("[OpenZElectroFx] " + Status);
                enabled = false; return;
            }
            Arcs = new ElectroArcs(arcMat);
            Scorch = new ElectroScorch(arcMat);
            BuildLights();
            gameObject.AddComponent<OpenZElectroJolts>();
            ready = true; Status = "ok";
        }

        static bool Broken(Material m)
        {
            var s = m ? m.shader : null;
            if (!s || !s.isSupported) return true;
#if UNITY_EDITOR
            if (UnityEditor.ShaderUtil.ShaderHasError(s)) return true;
#endif
            return false;
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            var ad = AudioDirector.I;
            if (ad) { foreach (var s in stuns.Values) if (s.loopOn) ad.StopLoop(s.loopKey, 0.1f); foreach (var k in humKeys) ad.StopLoop(k, 0.1f); }
            foreach (var s in stuns.Values) s.ClearGlow();
            Arcs?.Dispose(); Scorch?.Dispose();
            if (arcMat) Destroy(arcMat);
        }

        // ------------------------------------------------------------------------------------------------ helpers
        static ZGame G => ZGame.I;
        static float GameSpeed => ZGame.I ? Mathf.Clamp(ZGame.I.gameSpeed, 0.25f, 4f) : 1f;
        /// <summary>The RENDERED ground where known (TrackMarks' TER_ surface, like ScorchDecals: the collider can sit under the drawn terrain),
        /// else the walk surface (terrain + bridge decks).</summary>
        public static float Ground(float x, float z)
        {
            var s = TrackMarks.I ? TrackMarks.I.Surface : null;
            if (s != null && s.Sample(x, z, out float y, out _)) return y;
            return ZGame.I ? ZGame.I.GroundY(x, z) : 0f;
        }
        static readonly System.Func<float, float, float> GroundF = Ground;
        UnitView View(int id) => id >= 0 && G && G.Views.TryGetValue(id, out var v) && v ? v : null;
        Vector3 Focus => rts ? rts.focus : cam ? cam.transform.position : Vector3.zero;
        public bool OnScreen(Vector3 p, float margin)
        {
            if (!cam) return true;
            var v = cam.WorldToViewportPoint(p);
            return v.z > 0f && v.x > -margin && v.x < 1f + margin && v.y > -margin && v.y < 1f + margin;
        }
        float R() => rng.Next();
        float R(float a, float b) => rng.Range(a, b);
        uint Seed() => (uint)(rng.Next() * 16777215f) * 2654435761u + 1u;
        static bool Player(ZObject o) => o != null && G && o.Owner == G.player;

        /// <summary>The emitter of an electro weapon: ElectrobotView's ring / charge ball, else the unit's muzzle (an APC with his crew).</summary>
        Vector3 Emitter(UnitView v, ZObject o, out Vector3 dir, out ElectrobotView ev)
        {
            ev = v ? v.GetComponent<ElectrobotView>() : null;
            if (ev && ev.Bound) { dir = ev.AimDir; return ev.EmitterPos; }
            var p = v ? v.MuzzlePos() : o != null && G ? G.CenterWorld(o) + Vector3.up * 1.4f : Vector3.zero;
            var t = o?.AttackObject;
            dir = t != null && G ? (G.CenterWorld(t) + Vector3.up - p) : (v ? -v.transform.forward : Vector3.forward);
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            return p;
        }

        /// <summary>Where the bolt lands on the target: HitSurface on its drawn meshes (a point on the body, a little random), else its aim point.</summary>
        Vector3 HitPoint(ZObject t, UnitView tv, Vector3 from, out Transform follow, out Vector3 local)
        {
            follow = null; local = default;
            if (tv)
            {
                var aim = OzShuttleAim.HitAim(t, tv.AimPos()) + new Vector3(R(-0.3f, 0.3f), R(-0.2f, 0.4f), R(-0.3f, 0.3f));   // DV118: the flying ship's hull
                Vector3 p = aim;
                if (HitSurface.OnTarget(tv, from, aim, out var hp, out _)) p = hp;
                follow = tv.transform; local = follow.InverseTransformPoint(p);
                return p;
            }
            if (t != null && G)
            {
                if (G.StaticViews.TryGetValue(t.Id, out var go) && go) { follow = go.transform; var c = G.CenterWorld(t) + Vector3.up * 1.2f; local = follow.InverseTransformPoint(c); return c; }
                return G.CenterWorld(t) + Vector3.up * 1f;
            }
            return from + Vector3.forward;
        }

        // ================================================================================================ events
        readonly Dictionary<int, float> lastZapBark = new Dictionary<int, float>();
        float lastKillBark = -99f, lastStunnedLine = -99f;

        void OnEvent(ZEvent e, ZObject a, ZObject b)
        {
            if (!cam) cam = Camera.main;
            switch (e.type)
            {
                case ZEventType.ElectroCharge: Charge(e, a, b); break;
                case ZEventType.ElectroBolt: Bolt(e, a, b); break;
                case ZEventType.ElectroFizzle: Fizzle(a); break;
                case ZEventType.ElectroReady: { var v = View(e.objId); var ev = v ? v.GetComponent<ElectrobotView>() : null; if (ev) ev.OnReady(); break; }
                case ZEventType.StunStart: StunBegin(a, b, true); break;
                case ZEventType.StunRefresh: StunBegin(a, b, false); break;
                case ZEventType.StunPulse: if (a != null && stuns.TryGetValue(a.Id, out var sp)) sp.Pulse(this, e.value); break;
                case ZEventType.StunEnd: if (a != null && stuns.TryGetValue(a.Id, out var se)) se.End(this, e.flag); break;
                case ZEventType.Death: OnDeath(a); break;
                case ZEventType.UnitBuilt: if (a != null && a.Kind == ZKind.Robot && a.Type == ZElectro.Type) deploys.Add(new Deploy { id = a.Id, at = Time.time + 0.35f, player = Player(a) }); break;
            }
        }

        // ------------------------------------------------------------------------------------------------ windup
        sealed class Windup { public int id; public float start, dur; public Vector3 at; public bool viewOwned; }
        readonly Dictionary<int, Windup> windups = new Dictionary<int, Windup>();

        void Charge(ZEvent e, ZObject a, ZObject t)
        {
            if (a == null) return;
            Charges++;
            var v = View(a.Id);
            float dur = Mathf.Max(0.05f, e.value / GameSpeed);
            var at = Emitter(v, a, out _, out var ev);
            if (ev && ev.Bound) ev.OnCharge(dur);
            windups[a.Id] = new Windup { id = a.Id, start = Time.time, dur = dur, at = at, viewOwned = ev && ev.Bound };
            if (sounds && OnScreen(at, 0.35f)) AudioDirector.Fire("wpn_electro_charge", at, a.Id, 1f, GameSpeed);
        }

        void Fizzle(ZObject a)
        {
            if (a == null) return;
            Fizzles++;
            windups.Remove(a.Id);
            var v = View(a.Id);
            var at = Emitter(v, a, out var dir, out var ev);
            if (ev && ev.Bound) ev.OnFizzle();
            var fx = OpenZFxPlayer.I;
            if (fx && OnScreen(at, 0.2f))
            {
                for (int i = 0; i < 6; i++) fx.EmitChannel("wv2_spark", at, (Random.onUnitSphere + dir * 0.6f) * R(2f, 4.5f), R(0.18f, 0.35f), 0.05f, Ice, 1f);
                fx.EmitChannel("wv2_glow", at, Vector3.zero, 0.12f, 0.7f, Ice, 0.9f);
                fx.EmitChannel("m12_smoke", at, new Vector3(0f, 0.6f, 0f), 0.9f, 0.5f, new Color(0.62f, 0.66f, 0.74f), 0.35f);
            }
            if (sounds && OnScreen(at, 0.35f)) AudioDirector.Fire("electro_fizzle", at, a.Id);
        }

        // ------------------------------------------------------------------------------------------------ bolts
        const int Seg = 17, SmallSeg = 9;
        sealed class Strand
        {
            public float[] t = new float[Seg]; public Vector2[] off = new Vector2[Seg]; public int n;
            public float helix, phase, turns, width, strength, forkAt; public Vector3 forkDir; public float forkLen; public bool fork; public int parent = -1;
            public Vector3 groundTo;                                                              // ground strikes: the ground end
            public int kind;                                                                     // 0 main, 1 fork, 2 ground, 3 ring, 4 coil, 5 crackle, 6 radial burst
            public int ringA, ringB;
        }
        sealed class BoltFx
        {
            public int attacker, target; public UnitView av; public ElectrobotView ev; public Transform follow; public Vector3 local, endWorld, startWorld, dir;
            public float born, nextShape, restrike; public int step; public uint seed; public bool impacted, stunned, killed, deploy;
            public readonly List<Strand> strands = new List<Strand>(24);
            public float lightSeed;
        }
        readonly List<BoltFx> bolts = new List<BoltFx>(8);
        readonly Vector3[] pts = new Vector3[Seg + 2], main0 = new Vector3[Seg], main1 = new Vector3[Seg];

        void Bolt(ZEvent e, ZObject a, ZObject t)
        {
            if (a == null || !G) return;
            Bolts++;
            windups.Remove(a.Id);
            var av = View(a.Id); var tv = t != null ? View(t.Id) : null;
            var from = Emitter(av, a, out var dir, out var ev);
            if (driverBolt) { from = driverFrom; av = null; ev = null; }                             // DV87: from the hatch driver's hands: no gun and no hull to kick
            var end = HitPoint(t, tv, from, out var follow, out var local); if (t == null) SniperCoverView.ZapMissAt(e, ref end);   // DV122: a zap that missed a kneeling / lying sniper strikes the ground beside him (View/SniperCoverView.cs)
            var d = end - from; if (d.sqrMagnitude > 1e-4f) dir = d.normalized;
            OzShuttleAim.NoteBolt(a, t, from, end);                                                  // DV118: the gallery's counts, the last hit on the ship
            if (bolts.Count >= 6) bolts.RemoveAt(0);                                               // budget: the oldest goes first
            var b = new BoltFx { attacker = a.Id, target = t != null ? t.Id : -1, av = av, ev = ev && ev.Bound ? ev : null, follow = follow, local = local, endWorld = end, startWorld = from,
                                 dir = dir, born = Time.time, seed = Seed(), stunned = e.flag, killed = e.flag2, lightSeed = R(0f, 10f), restrike = R(0.11f, 0.16f) };
            Shape(b, true);
            bolts.Add(b);
            // the Electrobot: recoil (gun climb + body rock; UnitView's jolt and gun kick), cells emptied, gauge to 0, barrels spin down
            if (b.ev) b.ev.OnFire(dir);
            if (av) { av.OnFire(); if (av.TryGetComponent(out RobotWeaponPose pose)) pose.OnRound(4f, 0.12f); OpenZElectroJolts.Recoil(av, dir); }
            bool vis = OnScreen(from, 0.3f) || OnScreen(end, 0.3f);
            var fx = OpenZFxPlayer.I;
            if (fx && vis)
            {   // muzzle: flash, ring sparks
                for (int i = 0; i < 10; i++) fx.EmitChannel("wv2_spark", from, (dir * R(3f, 7f) + Random.insideUnitSphere * 3f), R(0.15f, 0.3f), 0.05f, Ice, 1f);
                fx.GroundLight(new Vector3(from.x, Ground(from.x, from.z), from.z), 0.7f, true);
            }
            AddFlash(from, 0.9f, 0.2f, muzzleLight, 8f, 8);                                          // muzzle light 0.2 s
            if (groundWash > 0f) rings.Add(new GroundRing { c = new Vector3(from.x, Ground(from.x, from.z), from.z), born = Time.time, life = 0.25f, r1 = groundWash * 0.5f, disc = true, gain = groundWashGain * 0.7f });
            if (sounds && vis)
            {
                AudioDirector.Fire("wpn_electro_fire", from, a.Id);
                sched.Add(new Sched { at = Time.time + 0.25f / GameSpeed, id = "wpn_electro_recharge", pos = from, emitter = a.Id, pitch = GameSpeed });
            }
            // voices: our Electrobot's barks
            if (voices && Player(a) && a.IsElectrobot)
            {
                if (e.flag2 && Time.time - lastKillBark > 8f && R() < 0.6f) { lastKillBark = Time.time; SayLater("vo_electro_kill", 0.45f); }
                else if (!e.flag2 && (!lastZapBark.TryGetValue(a.Id, out var lz) || Time.time - lz > 8f) && R() < zapBarkChance) { lastZapBark[a.Id] = Time.time; SayLater("vo_electro_zap", 0.35f); }
            }
            // a cannon / the fort defender / another Electrobot: never stunned, a 1.5 s jolt with arcs (D5, D4)
            if (t != null && !e.flag && !e.flag2 && tv && !t.Destroyed)
                StunBegin(t, a, true, (float)ZElectro.CannonJoltSeconds);
        }

        void Shape(BoltFx b, bool first)
        {
            var r = new ElectroBoltShape.Rng(ElectroBoltShape.Hash((int)b.seed, b.step, 7));
            b.step++; b.nextShape = Time.time + restrikeEvery * r.Range(0.8f, 1.25f);
            float len = Vector3.Distance(b.startWorld, b.endWorld);
            if (first)
            {
                b.strands.Clear();
                if (b.deploy)
                {
                    b.strands.Add(new Strand { kind = 0, width = 1.25f, strength = 1f });
                    b.strands.Add(new Strand { kind = 0, width = 0.55f, strength = 0.7f, helix = 0.6f, turns = 1.3f, phase = r.Range(0f, 6.28f) });
                }
                else
                {
                    b.strands.Add(new Strand { kind = 0, width = 1f, strength = 1f, helix = Mathf.Clamp(len * 0.025f, 0.12f, 0.38f), turns = r.Range(1.2f, 1.9f), phase = r.Range(0f, 6.28f) });
                    b.strands.Add(new Strand { kind = 0, width = 0.85f, strength = 0.9f, helix = b.strands[0].helix, turns = b.strands[0].turns, phase = b.strands[0].phase + Mathf.PI });
                }
                for (int m = 0; m < 2; m++)
                {
                    int forks = 2 + r.Int(3);
                    for (int f = 0; f < forks; f++)
                    {
                        var s = new Strand { kind = 1, parent = m, forkAt = r.Range(0.18f, 0.78f), forkLen = r.Range(0.12f, 0.3f), width = r.Range(0.35f, 0.55f), strength = r.Range(0.5f, 0.75f) };
                        ElectroBoltShape.Frame(b.endWorld - b.startWorld, out var u, out var v);
                        float ang = r.Range(20f, 55f) * Mathf.Deg2Rad, rot = r.Range(0f, 6.283f);
                        s.forkDir = Mathf.Cos(ang) * (b.endWorld - b.startWorld).normalized + Mathf.Sin(ang) * (u * Mathf.Cos(rot) + v * Mathf.Sin(rot));
                        b.strands.Add(s);
                    }
                    // the crackle: thin short filaments leaping off the bolt (re-placed every 33 ms, the Electro Wizard's sizzle)
                    for (int f = 0; f < (b.deploy ? 4 : 5); f++) b.strands.Add(new Strand { kind = 5, parent = m, width = r.Range(0.09f, 0.15f), strength = r.Range(0.5f, 0.9f) });
                }
                if (groundStrikes && !OzShuttleAim.AirId(b.target))                                       // DV118: no ground strikes from the flying shuttle
                {
                    int gs = b.deploy ? 6 : 3 + r.Int(3);
                    for (int g = 0; g < gs; g++)
                    {
                        float a = r.Range(0f, 6.283f), rad = r.Range(0.6f, b.deploy ? 3f : 2f);
                        var p = b.endWorld + new Vector3(Mathf.Cos(a) * rad, 0f, Mathf.Sin(a) * rad); p.y = Ground(p.x, p.z);
                        b.strands.Add(new Strand { kind = 2, groundTo = p, width = r.Range(0.28f, 0.45f), strength = r.Range(0.55f, 0.85f) });
                    }
                }
                if (b.ev)
                {
                    for (int k = 0; k < 8; k++) b.strands.Add(new Strand { kind = 3, ringA = k, ringB = (k + 1 + r.Int(2)) % 8, width = 0.14f, strength = r.Range(0.6f, 1f) });
                    for (int k = 0; k < 2; k++) b.strands.Add(new Strand { kind = 4, ringA = k, width = 0.22f, strength = 0.9f });
                }
                if (!b.deploy) for (int k = 0; k < radialArcs; k++) b.strands.Add(new Strand { kind = 6, width = r.Range(0.16f, 0.26f), strength = r.Range(0.6f, 1f) });
            }
            ElectroBoltShape.Frame(b.endWorld - b.startWorld, out var fu, out var fv);
            var fd = (b.endWorld - b.startWorld).sqrMagnitude > 1e-6f ? (b.endWorld - b.startWorld).normalized : Vector3.forward;
            foreach (var s in b.strands)
            {
                if (s.kind == 5)
                {
                    float a5 = r.Range(0f, 6.283f);
                    s.forkAt = r.Range(0.08f, 0.92f); s.forkLen = r.Range(0.4f, 1.4f);
                    s.forkDir = (fu * Mathf.Cos(a5) + fv * Mathf.Sin(a5) + fd * r.Range(-0.4f, 0.6f)).normalized;
                }
                else if (s.kind == 6)
                {   // radial burst: out of the hit point, away from the shooter and up (never into the ground), 0.8-1.9 m
                    var d6 = new Vector3(r.Range(-1f, 1f), r.Range(0.05f, 1f), r.Range(-1f, 1f)) + fd * 0.45f;
                    s.forkDir = d6.sqrMagnitude > 1e-4f ? d6.normalized : Vector3.up; s.forkLen = r.Range(0.8f, 1.9f);
                }
                int lv = s.kind == 0 ? 4 : 3;
                float amp = s.kind == 0 ? (b.deploy ? 0.07f : 0.11f) : s.kind == 1 ? 0.13f : s.kind == 2 ? 0.14f : s.kind == 5 ? 0.22f : s.kind == 6 ? 0.2f : 0.18f;
                s.n = ElectroBoltShape.Jag(ref r, lv, amp, s.kind == 0 ? 0.62f : 0.6f, s.t, s.off);
            }
        }

        /// <summary>Strike envelope (0..1): leader 35 ms, return stroke to 90 ms, a re-strike, afterglow with a flicker, out at
        /// strikeSeconds (0.45 s). The return value drives the jagged ribbons (k^2.2: bright to ~0.2 s, gone by ~0.35 s); `slow` drives
        /// the aura, the energy spheres and the travelling light (a linear fade with a light flicker: the afterglow to 0.45 s).</summary>
        float Envelope(BoltFx b, float age, out float reach, out float slow)
        {
            float lead = 0.035f;
            reach = Mathf.Clamp01(age / lead);
            if (age < lead) { slow = 0.6f; return 0.55f; }
            if (age < 0.09f) { slow = 1f; return 1f; }
            float T = strikeSeconds * (b.deploy ? 1.2f : 1f);
            if (age >= T) { slow = 0f; return 0f; }
            float k = 1f - (age - 0.09f) / (T - 0.09f);
            float flick = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin((age + b.lightSeed) * 71f));
            float re = Mathf.Exp(-Mathf.Pow((age - b.restrike) / 0.02f, 2f)) * 0.9f;                  // the re-strike
            slow = Mathf.Max(k * (0.8f + 0.2f * flick), re * k);
            return Mathf.Max(Mathf.Pow(k, 2.2f) * flick, re * k);
        }

        void StepBolts(float now)
        {
            for (int i = bolts.Count - 1; i >= 0; i--)
            {
                var b = bolts[i];
                float age = now - b.born;
                float T = strikeSeconds * (b.deploy ? 1.2f : 1f);
                if (age > T + 0.05f) { bolts.RemoveAt(i); continue; }
                // follow the ends: the emitter (recoil) and the target (shake), unless gone
                if (b.ev && b.ev.Bound && b.ev.isActiveAndEnabled) b.startWorld = b.ev.EmitterPos;
                else if (b.av && b.av.isActiveAndEnabled && !b.deploy) b.startWorld = b.av.MuzzlePos();
                if (b.follow && b.follow.gameObject.activeInHierarchy) b.endWorld = b.follow.TransformPoint(b.local);
                if (now >= b.nextShape) Shape(b, false);
                float env = Envelope(b, age, out float reach, out float slow);
                if (!b.impacted && reach >= 1f) { b.impacted = true; Impact(b); }
                DrawBolt(b, env, slow, reach, age);
                // lights: the one that travels the bolt, then the impact's (AddFlash) and the muzzle's
                float lk = Mathf.Max(env, 0.6f * slow);
                if (lk > 0.01f)
                {
                    var mid = Vector3.Lerp(b.startWorld, b.endWorld, 0.55f * Mathf.Min(1f, reach));
                    AddCand(8_000 + i, mid + Vector3.up * 0.4f, boltLight * lk, 12f, 9f, true);
                }
            }
        }

        void DrawBolt(BoltFx b, float env, float slow, float reach, float age)
        {
            if (env <= 0.002f && slow <= 0.002f) return;
            var A = b.startWorld; var B = b.endWorld;
            float len = Vector3.Distance(A, B);
            float w = boltWidth * (age < 0.09f && age > 0.035f ? 1.15f : 1f);
            // tonemapper keeps the electric blue around the white core; at night (NightK 1) the fx review's values stand
            float dk = 1f - Mathf.Clamp01(NightGrade.NightK);
            float cg = coreGain * Mathf.Lerp(1f, dayCoreK, dk), gg = glowGain * Mathf.Lerp(1f, dayGlowK, dk), ag = auraGain * Mathf.Lerp(1f, dayAuraK, dk);
            for (int si = 0; si < b.strands.Count; si++)
            {
                var s = b.strands[si];
                int n = s.n; if (n < 2) continue;
                switch (s.kind)
                {
                    case 0:
                    {
                        // the twin bolts leave opposite sides of the ring and meet at the target
                        var a0 = A;
                        if (b.ev && b.ev.Bound) a0 = Vector3.Lerp(b.ev.EmitterPos, b.ev.RingPoint(si == 0 ? 0 : 4), 0.55f);
                        ElectroBoltShape.Place(a0, B, s.t, s.off, n, pts, s.helix, s.turns, s.phase);
                        int shown = Mathf.Clamp(Mathf.CeilToInt(reach * (n - 1)) + 1, 2, n);
                        if (si < 2) System.Array.Copy(pts, si == 0 ? main0 : main1, n);
                        // the wide soft blue aura (mode 4: a Gaussian across, few beads; blooms blue by day), the slow envelope
                        if (ag > 0f) Arcs.Ribbon(pts, shown, w * s.width * auraWidth, 0.02f, 0f, ag, Aura, slow * s.strength, s.phase, 0.5f, 0.75f, 14f, 4f);
                        Arcs.Ribbon(pts, shown, w * s.width, coreShare, cg, gg, Glow, env * s.strength, s.phase, 0.5f, 0.75f, 5f);                     // the jagged white-hot bolt
                        break;
                    }
                    case 1:
                    {
                        if (reach < s.forkAt) break;
                        var basePts = s.parent == 0 ? main0 : main1;
                        int k = Mathf.Clamp(Mathf.RoundToInt(s.forkAt * (Seg - 1)), 1, Seg - 2);
                        var p0 = basePts[k];
                        var p1 = p0 + s.forkDir * (len * s.forkLen);
                        ElectroBoltShape.Place(p0, p1, s.t, s.off, n, pts);
                        Arcs.Ribbon(pts, n, w * s.width, coreShare * 1.3f, cg * 0.8f, gg * 0.9f, Glow, env * s.strength, si * 1.7f, 1f, 0.05f, 3f);
                        break;
                    }
                    case 5:
                    {
                        if (reach < s.forkAt) break;
                        var basePts = s.parent == 0 ? main0 : main1;
                        int k = Mathf.Clamp(Mathf.RoundToInt(s.forkAt * (Seg - 1)), 1, Seg - 2);
                        var p0 = basePts[k];
                        ElectroBoltShape.Place(p0, p0 + s.forkDir * s.forkLen, s.t, s.off, n, pts);
                        Arcs.Ribbon(pts, n, s.width, 0.24f, cg * 0.75f, gg * 0.8f, Glow, env * s.strength, si * 0.9f, 1f, 0.05f, 2f);
                        break;
                    }
                    case 2:
                    {
                        if (!b.impacted) break;
                        ElectroBoltShape.Place(B, s.groundTo, s.t, s.off, n, pts);
                        Arcs.Ribbon(pts, n, w * s.width, coreShare, cg * 0.8f, gg * 0.9f, Glow, env * s.strength, si * 2.3f, 0.8f, 0.4f, 3f);
                        break;
                    }
                    case 3:
                    {
                        if (!b.ev || age > 0.2f) break;
                        ElectroBoltShape.Place(b.ev.RingPoint(s.ringA), b.ev.RingPoint(s.ringB), s.t, s.off, n, pts, 0f, 0f, 0f, 0.25f, b.dir);
                        Arcs.Ribbon(pts, n, s.width, 0.22f, 8f, 1.6f, Glow, Mathf.Clamp01(1f - age / 0.2f) * s.strength, si, 0.6f, 0.6f, 2f);
                        break;
                    }
                    case 4:
                    {
                        if (!b.ev || age > 0.15f) break;
                        ElectroBoltShape.Place(b.ev.CoilTop(s.ringA), b.ev.CellsPos, s.t, s.off, n, pts, 0f, 0f, 0f, 0.15f, Vector3.up);
                        Arcs.Ribbon(pts, n, s.width, 0.22f, 8f, 1.6f, Glow, Mathf.Clamp01(1f - age / 0.15f) * s.strength, si, 0.6f, 0.6f, 2f);
                        break;
                    }
                    case 6:
                    {   // the radial burst on the target (0.3 s from the impact)
                        if (!b.impacted) break;
                        float ia6 = age - 0.035f; if (ia6 > 0.3f) break;
                        ElectroBoltShape.Place(B, B + s.forkDir * s.forkLen, s.t, s.off, n, pts);
                        Arcs.Ribbon(pts, n, s.width, 0.22f, cg * 0.7f, gg * 0.9f, Glow, Mathf.Clamp01(1f - ia6 / 0.3f) * s.strength, si * 1.3f, 1f, 0.05f, 2f);
                        break;
                    }
                }
            }
            // glows: the emitter's white flash + its blue energy sphere; the impact's white flash, the blue flash disc on the target
            if (age < 0.12f) Arcs.Glow(A, 1.3f * (1f - age / 0.12f) + 0.4f, 0.3f, 12f, 2.5f, Glow, 1f);
            if (!b.deploy && emitterSphere > 0f && slow > 0.002f) Arcs.Glow(A, emitterSphere * (0.8f + 0.2f * slow), 0.18f, 2.5f, sphereGain, Aura, slow);
            if (b.impacted)
            {
                float ia = age - 0.035f;
                if (ia < 0.12f) Arcs.Glow(B, 1.6f, 0.35f, 12f, 2f, Glow, 1f - ia / 0.12f);
                if (ia < 0.2f) Arcs.Glow(B, 2.4f, 0.55f, 5f, 1.8f, Glow, Mathf.Pow(1f - ia / 0.2f, 1.2f));
                if (impactSphere > 0f && slow > 0.002f) Arcs.Glow(B, impactSphere * (0.85f + 0.15f * slow), 0.15f, 2f, sphereGain, Aura, Mathf.Pow(slow, 1.1f));
            }
        }

        void Impact(BoltFx b)
        {
            var at = b.endWorld; var flat = new Vector3(at.x, Ground(at.x, at.z), at.z);
            var fx = OpenZFxPlayer.I;
            bool vis = OnScreen(at, 0.25f);
            if (fx && vis)
            {
                int n = 40 + (int)(R() * 20f);
                for (int i = 0; i < n; i++)
                {
                    var v = Random.onUnitSphere; v.y = Mathf.Abs(v.y) * 0.9f + 0.25f;
                    fx.EmitChannel("wv2_spark", at, (v - b.dir * 0.3f) * R(3.5f, 9f), R(0.25f, 0.7f), R(0.04f, 0.07f), i % 3 == 0 ? Color.white : Ice, 1f);
                }
                for (int i = 0; i < 6; i++) fx.EmitChannel("wv2_glow", at + Random.insideUnitSphere * 0.4f, Random.insideUnitSphere * 1.5f, R(0.1f, 0.22f), R(0.4f, 0.9f), Ice, 1f);
                fx.EmitChannel("m12_smoke", at + Vector3.up * 0.2f, new Vector3(0.05f, 0.7f, 0.03f), 1.4f, 1.1f, new Color(0.60f, 0.66f, 0.76f), 0.42f);   // ozone puff
                if (!OzShuttleAim.AirId(b.target)) fx.GroundLight(flat, 1.2f, true);                                                    // the blue ground light (+ our ground wash)
                if (cameraKick > 0f && !b.deploy) fx.Shake?.Impact(at, 0.9f * cameraKick);
                if (b.deploy) fx.Shake?.Impact(at, 0.7f * cameraKick);
            }
            if (bloomSustain > 0f && vis) sustainUntil = Time.time + 0.22f;
            if (!OzShuttleAim.AirId(b.target)) rings.Add(new GroundRing { c = flat, born = Time.time, life = 0.35f, r1 = b.deploy ? 5f : 4f });   // DV118: no ground ring under the flying shuttle
            if (groundWash > 0f && !OzShuttleAim.AirId(b.target)) rings.Add(new GroundRing { c = flat, born = Time.time, life = strikeSeconds, r1 = groundWash * (b.deploy ? 1.3f : 1f), disc = true, gain = groundWashGain });
            if (at.y - flat.y < 3f)
            {
                ScorchDecals.Add(flat, 0.8f, ScorchKind.Contact);
                if (lichtenberg) Scorch?.Add(flat, b.deploy ? 2.2f : 1.4f, b.seed);
            }
            AddFlash(at, 1.2f, 0.45f, impactLight, 11f, 10);
            if (sounds && vis && !b.deploy) AudioDirector.Fire("imp_electro_hit", at, b.target);
            // the target: the zap shake on the hit moment (smooth damped, never per-frame random), the bright silhouette flash
            var tv = View(b.target);
            if (tv && !tv.Dying)
            {
                OpenZElectroJolts.Zap(tv, b.dir, tv.Obj != null && tv.Obj.Kind == ZKind.Robot ? shakeRobot : shakeVehicle, tv.Obj != null && tv.Obj.Kind != ZKind.Robot);
                if (targetFlash && stuns.TryGetValue(b.target, out var st)) st.flashUntil = Time.time + 0.045f;
            }
        }

        // ------------------------------------------------------------------------------------------------ ground rings (shock ring)
        /// <summary>A shock ring on the ground, or (disc) the blue ground wash: an additive conforming disc (ElectroArc mode 5).</summary>
        struct GroundRing { public Vector3 c; public float born, life, r1, gain; public bool disc; }
        readonly List<GroundRing> rings = new List<GroundRing>(8);
        float sustainUntil;

        void StepRings(float now)
        {
            for (int i = rings.Count - 1; i >= 0; i--)
            {
                var g = rings[i]; float k = (now - g.born) / g.life;
                if (k >= 1f) { rings.RemoveAt(i); continue; }
                float e = 1f - (1f - k) * (1f - k);                                                   // ease-out
                if (g.disc)
                {   // the ground wash: grows to its radius in 60 ms, fades over its life with a light flicker (reads blue on sand by day)
                    float R = g.r1 * Mathf.Lerp(0.55f, 1f, Mathf.Clamp01((now - g.born) / 0.06f));
                    float fl = 0.85f + 0.15f * Mathf.Sin((now + g.c.x) * 53f);
                    Arcs.Ring(g.c, R * 0.5f, R, 0.35f, 0.6f * g.gain, g.gain, Aura, Mathf.Pow(1f - k, 1.3f) * fl, 1f, GroundF, 0.1f, 40, 5f);
                }
                else Arcs.Ring(g.c, Mathf.Lerp(0.3f, g.r1, e), Mathf.Lerp(0.5f, 0.18f, e), 0.25f, 4f, 1.5f, Glow, (1f - k) * (1f - k), 1f, GroundF, 0.12f);
            }
        }

        // ------------------------------------------------------------------------------------------------ stun (per unit)
        readonly Dictionary<int, StunFx> stuns = new Dictionary<int, StunFx>();
        readonly List<int> stunKeys = new List<int>();

        void StunBegin(ZObject o, ZObject by, bool fresh, float joltSeconds = -1f)
        {
            if (o == null) return;
            var v = View(o.Id); if (!v || v.Dying) return;
            bool jolt = joltSeconds > 0f;
            if (!stuns.TryGetValue(o.Id, out var s) || s.view != v || s.jolt != jolt)
            {
                if (s != null) s.ClearGlow();
                s = new StunFx(this, o, v, jolt);
                stuns[o.Id] = s;
                if (!jolt) Stuns++;
            }
            s.Refresh(this, jolt ? joltSeconds : -1f, fresh);
            if (!jolt && fresh && voices && Player(o) && Time.time - lastStunnedLine > 6f && R() < stunnedLineChance)
            {
                var line = StunnedLine(o);
                if (line != null) { lastStunnedLine = Time.time; SayLater(line, 0.6f); }
            }
        }

        /// <summary>
        /// tingle): vo_electro_stunned_&lt;type&gt; ("Legs offline!" for a grunt in the grunt's voice, "Engine's fried!" for a light tank in the
        /// light tank's); else the kind's generic pool (robots vo_electro_stunned, vehicles vo_electro_stunned_vehicle); else nothing.</summary>
        public static string StunnedLine(ZObject o)
        {
            if (o == null) return null;
            string own = "vo_electro_stunned_" + o.Type;
            if (AudioDirector.Has(own)) return own;
            string kind = o.Kind == ZKind.Robot ? "vo_electro_stunned" : o.Kind == ZKind.Vehicle ? "vo_electro_stunned_vehicle" : null;
            return kind != null && AudioDirector.Has(kind) ? kind : null;
        }

        /// <summary>One stunned (or jolted) unit: crawling arcs on its body, drips of sparks, the cyan emission, the timer ring, light and buzz.</summary>
        sealed class StunFx
        {
            public readonly int id; public readonly ZObject obj; public readonly UnitView view; public readonly bool jolt;
            public OpenZPyroBurnSurface surf;
            readonly List<(Transform t, Vector3 local)> pts = new List<(Transform, Vector3)>(20);
            struct ArcSlot { public int a, b; public float until, seed, str; }
            readonly ArcSlot[] slots = new ArcSlot[8];
            public float born, joltUntil = -1f, nextSpark, flashUntil, ending = -1f, lastRefresh;
            public bool loopOn, audible; public string loopKey;
            /// <summary>(renderer, material index, the emission the shader sees without us: linear, ElectroColour.OfMaterial)</summary>
            readonly List<(Renderer r, int i, Vector4 base0)> glowTargets = new List<(Renderer, int, Vector4)>();
            readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            public float radius = 0.9f;
            /// <summary>Show menu: a view-only jolt drawn as a full stun (timer ring, buzz, end puff).</summary>
            public bool demo;
            bool Full => !jolt || demo;
            public bool Done;
            static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
            readonly float[] t = new float[SmallSeg]; readonly Vector2[] off = new Vector2[SmallSeg]; readonly Vector3[] p = new Vector3[SmallSeg];

            public StunFx(OpenZElectroFx fx, ZObject o, UnitView v, bool jolt)
            {
                id = o.Id; obj = o; view = v; this.jolt = jolt; born = Time.time; loopKey = "eb_stun:" + o.Id;
                surf = OpenZPyroBurnSurface.Build(v.gameObject, false);
                radius = o.Kind == ZKind.Robot ? 1.2f : Mathf.Clamp(surf != null ? surf.Size * 1.05f : 2.3f, 2.3f, 3.4f);   // just outside the selection ring (robots .95, vehicles 1.9)
                Resample(fx);
                foreach (var r in v.GetComponentsInChildren<Renderer>(true))
                {
                    if (!r || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                    var n = r.name.ToLowerInvariant();
                    if (n.Contains("light") || n.Contains("lamp") || n.Contains("glow") || n.Contains("beacon") || n.Contains("blink") || n.Contains("select")) continue;
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        if (!m || !m.HasProperty(EmissionId) || !m.shader || !m.shader.name.StartsWith("OpenZ/TeamLit")) continue;
                        if (m.name.Contains("Glow")) continue;                                  // the Electrobot's own glow slots, lamps
                        glowTargets.Add((r, i, ElectroColour.OfMaterial(m, EmissionId)));
                    }
                }
            }

            void Resample(OpenZElectroFx fx)
            {
                pts.Clear();
                if (surf == null) return;
                var cp = fx.cam ? fx.cam.transform.position : view.transform.position + Vector3.up * 50f;
                for (int k = 0; k < 18; k++)
                    if (surf.Sample(cp, GroundF, Time.time, out var w, out var nrm, out var anchor) && anchor)
                        pts.Add((anchor, anchor.InverseTransformPoint(w + nrm * 0.04f)));
            }

            public void Refresh(OpenZElectroFx fx, float joltSeconds, bool fresh)
            {
                lastRefresh = Time.time; ending = -1f; Done = false;
                if (joltSeconds > 0f) joltUntil = Mathf.Max(joltUntil, Time.time + joltSeconds);
                for (int i = 0; i < slots.Length; i++) slots[i].until = 0f;                    // a burst of new arcs
                if (!fresh) flashUntil = Time.time + 0.03f;
            }

            public float SecondsLeft => jolt ? Mathf.Max(0f, joltUntil - Time.time) : obj != null && obj.Stunned ? (float)(obj.StunTicksLeft * ZConst.TICK) / GameSpeed : 0f;

            public void Pulse(OpenZElectroFx fx, float secsLeft)
            {
                if (view && fx.OnScreen(view.transform.position, 0.1f) && fx.sounds && secsLeft > 2f && fx.R() < 0.35f)
                    AudioDirector.Fire("electro_spark", view.transform.position + Vector3.up, id, 0.8f, fx.R(0.9f, 1.2f));
            }

            public void End(OpenZElectroFx fx, bool early)
            {
                if (ending >= 0f) return;
                ending = Time.time;
                if (!view || !view.isActiveAndEnabled || view.Dying) return;
                var c = surf != null ? surf.CenterWorld : view.transform.position + Vector3.up;
                var f = OpenZFxPlayer.I;
                if (f && fx.OnScreen(c, 0.15f))
                {
                    for (int i = 0; i < 12; i++) f.EmitChannel("wv2_spark", c + Random.insideUnitSphere * 0.4f, Random.onUnitSphere * fx.R(1.5f, 4f) + Vector3.up, fx.R(0.2f, 0.4f), 0.045f, Ice, 1f);
                    f.EmitChannel("m12_smoke", c, new Vector3(0f, 0.8f, 0f), 1.1f, 0.9f, new Color(0.62f, 0.68f, 0.78f), 0.32f);
                    f.EmitChannel("wv2_glow", c, Vector3.zero, 0.16f, 1.4f, Ice, 0.8f);
                }
                if (Full && fx.sounds && fx.OnScreen(c, 0.3f)) AudioDirector.Fire("stun_end", c, id);
                if (Full) OpenZElectroJolts.Zap(view, view.transform.right, obj != null && obj.Kind == ZKind.Robot ? 0.025f : 0.015f, obj != null && obj.Kind != ZKind.Robot, 0.4f);   // shake-off
            }

            public void ClearGlow()
            {
                foreach (var (r, i, c0) in glowTargets)
                {
                    if (!r) continue;
                    r.GetPropertyBlock(mpb, i); mpb.SetVector(EmissionId, c0); r.SetPropertyBlock(mpb, i);   // linear, as the material has it
                }
            }

            /// <summary>false = finished (remove).</summary>
            public bool Step(OpenZElectroFx fx, float now, int arcBudget, bool nearLoop)
            {
                bool alive = view && view.gameObject.activeInHierarchy && !view.Dying && obj != null && !obj.Destroyed;
                float left = SecondsLeft;
                bool active = alive && (jolt ? left > 0f : (obj.Stunned || now - lastRefresh < 0.15f)) && ending < 0f;
                float fadeOut = ending >= 0f ? 1f - (now - ending) / 0.25f : active ? 1f : 0f;
                if (!alive || fadeOut <= 0f)
                {
                    ClearGlow();
                    if (loopOn && AudioDirector.I) AudioDirector.I.StopLoop(loopKey, 0.2f);
                    loopOn = false; Done = true; return false;
                }
                var c = surf != null ? surf.CenterWorld : view.transform.position + Vector3.up;
                bool vis = fx.OnScreen(c, 0.15f);
                float last2 = Full && left < 2f ? Mathf.Clamp01(left / 2f) : 1f;                // the last 2 s sputter out
                float sput = last2 < 1f ? (Mathf.Sin(now * 23f + id) > 0.2f ? 1f : 0.25f) * (0.35f + 0.65f * last2) : 1f;
                float strength = fadeOut * sput * (jolt && !demo ? Mathf.Clamp01(left / 0.5f) * 0.9f : 1f);
                // ---- arcs crawling on the body (re-picked every 0.12-0.25 s)
                if (vis && pts.Count >= 2)
                {
                    int want = Mathf.Min(arcBudget, Mathf.RoundToInt(Mathf.Lerp(1f, fx.stunArcsMax, last2)) + (now < flashUntil + 0.2f ? 2 : 0));
                    want = Mathf.Min(want, slots.Length);
                    float size = obj.Kind == ZKind.Robot ? 1f : 1.5f;
                    for (int k = 0; k < want; k++)
                    {
                        ref var s = ref slots[k];
                        if (now >= s.until)
                        {
                            s.a = fx.rng.Int(pts.Count); s.b = fx.rng.Int(pts.Count);
                            for (int tries = 0; tries < 4 && (s.a == s.b || Dist(s.a, s.b) > 1.8f * size || Dist(s.a, s.b) < 0.25f * size); tries++) s.b = fx.rng.Int(pts.Count);
                            s.until = now + fx.R(0.12f, 0.25f); s.seed = fx.R(0f, 100f); s.str = fx.R(0.6f, 1f);
                            if (fx.R() < 0.08f) Resample(fx);
                        }
                        if (s.a == s.b) continue;
                        var A = W(s.a); var B = W(s.b); var mid = (A + B) * 0.5f;
                        var outDir = (mid - c); outDir = outDir.sqrMagnitude > 1e-4f ? outDir.normalized : Vector3.up;
                        var r = new ElectroBoltShape.Rng(ElectroBoltShape.Hash((int)(s.seed * 1000f), Mathf.FloorToInt(now * 30f), k));   // crackles at 30 Hz, ends ride the body
                        int n = ElectroBoltShape.Jag(ref r, 3, 0.16f, 0.6f, t, off);
                        ElectroBoltShape.Place(A, B, t, off, n, p, 0f, 0f, 0f, 0.22f, outDir);
                        float w = (obj.Kind == ZKind.Robot ? 0.2f : 0.28f) * (0.8f + 0.2f * Mathf.Sin(now * 40f + s.seed));
                        fx.Arcs.Ribbon(p, n, w, 0.2f, 7f, 1.4f, Ice, strength * s.str, s.seed, 0.6f, 0.6f, 2f);
                    }
                    // a glow at a random arc end now and then (the crackle)
                    if (fx.R() < 0.25f) fx.Arcs.Glow(W(fx.rng.Int(pts.Count)), fx.R(0.25f, 0.5f) * size, 0.3f, 6f, 1.4f, Ice, strength * 0.8f);
                }
                // ---- sparks dripping to the ground
                if (vis && now >= nextSpark && OpenZFxPlayer.I && pts.Count > 0)
                {
                    nextSpark = now + fx.R(0.3f, 0.6f) / Mathf.Max(0.3f, last2);
                    var sp = W(fx.rng.Int(pts.Count));
                    for (int i = 0; i < 3; i++) OpenZFxPlayer.I.EmitChannel("wv2_spark", sp, new Vector3(fx.R(-1f, 1f), fx.R(0.2f, 1.5f), fx.R(-1f, 1f)), fx.R(0.3f, 0.6f), 0.04f, Ice, 1f);
                }
                // ---- emission tint on the body (flat, smooth; the hit flash = 2-3 frames bright). LINEAR values, handed to TeamLit's
                //      _EmissionColor unchanged (SetVector): peak 0.18 x Ice = (0.05, 0.14, 0.18) linear (half at night), the flash (0.42, 1.09, 1.4)
                float glow = now < flashUntil ? 1.4f : fx.stunGlow * Mathf.Lerp(1f, fx.stunGlowNight, Mathf.Clamp01(NightGrade.NightK))
                             * (0.6f + 0.25f * Mathf.Sin(now * 6.1f + id) + 0.15f * Mathf.Sin(now * 17.3f + id * 2f)) * strength;
                var e = ElectroColour.Shader(new Color(Ice.r * glow, Ice.g * glow, Ice.b * glow, 0f));
                foreach (var (r, i, c0) in glowTargets)
                {
                    if (!r) continue;
                    var sum = c0 + e; sum.w = c0.w;
                    r.GetPropertyBlock(mpb, i); mpb.SetVector(EmissionId, sum); r.SetPropertyBlock(mpb, i);
                }
                // ---- the timer ring on the ground (draining), light, buzz
                if (Full && fx.stunRing && vis && !OzShuttleAim.Air(obj))                       // K256e: no stun timer ring on the ground under the flying shuttle
                {
                    var g = view.transform.position; g.y = Ground(g.x, g.z);
                    fx.Arcs.Ring(g, radius, 0.09f, 0.3f, 2.4f, 1.3f, Ice, 0.85f * fadeOut, Mathf.Clamp01(left / (float)ZElectro.Seconds(ZElectro.StunTicks) * GameSpeed), GroundF, 0.12f, 40);
                }
                float fl = 0.7f + 0.3f * Mathf.Sin(now * 31f + id) * Mathf.Sin(now * 13f);
                fx.AddCand(20_000 + id, c + Vector3.up * 0.3f, 7f * fl * strength, 6f, 3f, false);
                audible = vis;
                var ad = AudioDirector.I;
                if (ad && fx.sounds && Full)
                {
                    bool want = nearLoop && vis && ending < 0f;
                    if (want && !loopOn) { ad.StartLoop(loopKey, "stun_loop", c, view.transform); loopOn = true; }
                    else if (!want && loopOn) { ad.StopLoop(loopKey, 0.2f); loopOn = false; }
                    if (loopOn) ad.SetLoop(loopKey, Mathf.Lerp(0.35f, 1f, last2), 1f);
                }
                return true;
            }

            Vector3 W(int k) { var q = pts[Mathf.Clamp(k, 0, pts.Count - 1)]; return q.t ? q.t.TransformPoint(q.local) : view.transform.position + Vector3.up; }
            float Dist(int a, int b) => Vector3.Distance(W(a), W(b));
        }

        void StepStuns(float now)
        {
            stunKeys.Clear(); foreach (var k in stuns.Keys) stunKeys.Add(k);
            // nearest 3 to the camera focus get the buzz loop; the arc budget goes nearest first
            byDist ??= (x, y) => Dist2(x).CompareTo(Dist2(y));
            stunKeys.Sort(byDist);
            int budget = stunArcBudget, rank = 0;
            foreach (var k in stunKeys)
            {
                var s = stuns[k];
                int give = Mathf.Min(stunArcsMax + 2, budget);
                if (!s.Step(this, now, give, rank < 3)) { stuns.Remove(k); continue; }
                budget = Mathf.Max(0, budget - Mathf.Min(give, stunArcsMax));
                if (s.audible) rank++;
            }
        }

        System.Comparison<int> byDist;
        float Dist2(int id)
        {
            var v = View(id); if (!v) return float.MaxValue;
            var d = v.transform.position - Focus; d.y = 0f; return d.sqrMagnitude;
        }

        // ------------------------------------------------------------------------------------------------ deaths
        void OnDeath(ZObject o)
        {
            if (o == null || !G || G.World == null) return;
            var v = View(o.Id);
            if (stuns.TryGetValue(o.Id, out var s)) { s.ClearGlow(); if (s.loopOn && AudioDirector.I) AudioDirector.I.StopLoop(s.loopKey, 0.15f); stuns.Remove(o.Id); }
            if (!v) return;
            bool zapped = G.World.Time - o.LastZapHit < ZElectro.ZapDeathWindow;
            if (o.Kind == ZKind.Robot && o.Type == ZElectro.Type) ShortCircuit(v);
            if (zapped) zapDeaths.Add(new ZapDeath { view = v, id = o.Id, born = Time.time, surf = OpenZPyroBurnSurface.Build(v.gameObject, false), paint = v.GetComponent<TeamPaint>() });
        }

        sealed class ZapDeath { public UnitView view; public int id; public float born, nextSmoke; public OpenZPyroBurnSurface surf; public TeamPaint paint; public bool charred; }
        readonly List<ZapDeath> zapDeaths = new List<ZapDeath>(8);
        readonly float[] zt = new float[SmallSeg]; readonly Vector2[] zo = new Vector2[SmallSeg]; readonly Vector3[] zp = new Vector3[SmallSeg];

        void StepZapDeaths(float now)
        {
            var camPos = cam ? cam.transform.position : Vector3.zero;
            for (int i = zapDeaths.Count - 1; i >= 0; i--)
            {
                var z = zapDeaths[i]; float age = now - z.born;
                if (age > 1.5f || !z.view || !z.view.gameObject.activeInHierarchy || z.surf == null) { zapDeaths.RemoveAt(i); continue; }
                if (!z.charred && z.paint) { z.charred = true; z.paint.Charred = Mathf.Max(z.paint.Charred, 0.35f); }
                float k = 1f - age / 1.5f;
                int n = age < 0.5f ? 4 : 2;
                for (int a = 0; a < n; a++)
                {
                    if (!z.surf.Sample(camPos, GroundF, now, out var p0, out _, out _)) continue;
                    if (!z.surf.Sample(camPos, GroundF, now, out var p1, out _, out _)) continue;
                    if ((p1 - p0).sqrMagnitude > 3.2f) continue;
                    var r = new ElectroBoltShape.Rng(ElectroBoltShape.Hash(z.id, Mathf.FloorToInt(now * 30f), a));
                    int m = ElectroBoltShape.Jag(ref r, 3, 0.16f, 0.6f, zt, zo);
                    ElectroBoltShape.Place(p0, p1, zt, zo, m, zp, 0f, 0f, 0f, 0.2f, Vector3.up);
                    Arcs.Ribbon(zp, m, 0.18f, 0.2f, 7f, 1.4f, Ice, k * R(0.6f, 1f), a, 0.6f, 0.6f, 2f);
                }
                if (now >= z.nextSmoke && OpenZFxPlayer.I)
                {
                    z.nextSmoke = now + 0.18f;
                    var top = z.surf.CenterWorld + Vector3.up * 0.7f;
                    OpenZFxPlayer.I.EmitChannel("m12_smoke", top, new Vector3(R(-0.1f, 0.1f), 1.2f, R(-0.1f, 0.1f)), 1.3f, 0.7f, new Color(0.30f, 0.30f, 0.32f), 0.5f);
                }
            }
        }

        /// <summary>The Electrobot's own death: his coils blow (arcs flying out to the ground, a flash, sparks, the short-circuit bang).</summary>
        void ShortCircuit(UnitView v)
        {
            var ev = v.GetComponent<ElectrobotView>();
            var c = ev && ev.Bound ? (ev.CoilTop(0) + ev.CoilTop(1)) * 0.5f : v.transform.position + Vector3.up * 2.2f;
            var b = new BoltFx { attacker = -1, target = -1, born = Time.time, seed = Seed(), startWorld = c, endWorld = c, dir = Vector3.up, lightSeed = R(0f, 10f), restrike = 0.12f, impacted = true };
            var r = new ElectroBoltShape.Rng(b.seed);
            for (int k = 0; k < 8; k++)
            {
                float a = r.Range(0f, 6.283f), rad = r.Range(1.5f, 3.5f);
                var g = c + new Vector3(Mathf.Cos(a) * rad, 0f, Mathf.Sin(a) * rad); g.y = Ground(g.x, g.z);
                var s = new Strand { kind = 2, groundTo = g, width = r.Range(0.3f, 0.5f), strength = r.Range(0.7f, 1f) };
                s.n = ElectroBoltShape.Jag(ref r, 3, 0.16f, 0.6f, s.t, s.off);
                b.strands.Add(s);
            }
            b.endWorld = c; bolts.Add(b);
            var fx = OpenZFxPlayer.I;
            if (fx && OnScreen(c, 0.2f))
            {
                for (int i = 0; i < 60; i++) fx.EmitChannel("wv2_spark", c, Random.onUnitSphere * R(3f, 10f) + Vector3.up * 2f, R(0.3f, 0.8f), R(0.04f, 0.07f), i % 3 == 0 ? Color.white : Ice, 1f);
                fx.EmitChannel("m12_smoke", c, new Vector3(0f, 1.2f, 0f), 1.8f, 1.6f, new Color(0.32f, 0.33f, 0.36f), 0.6f);
                fx.GroundLight(new Vector3(c.x, Ground(c.x, c.z), c.z), 1.8f, true);
                fx.Shake?.Impact(c, 1.1f * cameraKick);
            }
            AddFlash(c, 1.4f, 0.5f, deathLight, 12f, 10);
            if (sounds && OnScreen(c, 0.3f)) AudioDirector.Fire("electro_death", c, v.Obj != null ? v.Obj.Id : -1);
        }

        // ------------------------------------------------------------------------------------------------ deploy strike (D11)
        struct Deploy { public int id; public float at; public bool player; }
        readonly List<Deploy> deploys = new List<Deploy>(4);

        void StepDeploys(float now)
        {
            for (int i = deploys.Count - 1; i >= 0; i--)
            {
                var d = deploys[i];
                if (now < d.at) continue;
                deploys.RemoveAt(i);
                var v = View(d.id);
                if (!v || !v.gameObject.activeInHierarchy || now - d.at > 3f) continue;
                if (d.player && voices) SayLater("vo_electro_spawn", 2.2f);
                var g = v.transform.position; g.y = Ground(g.x, g.z);
                if (!OnScreen(g, 0.1f)) continue;
                var top = g + new Vector3(R(-1.5f, 1.5f), 26f, R(-1.5f, 1.5f));
                var b = new BoltFx { attacker = -1, target = -1, born = now, seed = Seed(), startWorld = top, endWorld = g + Vector3.up * 0.05f, dir = Vector3.down, deploy = true, lightSeed = R(0f, 10f), restrike = 0.14f };
                Shape(b, true); bolts.Add(b);
                if (sounds) AudioDirector.Fire("electro_deploy", g, d.id);
            }
        }

        // ------------------------------------------------------------------------------------------------ windup glows (units without ElectrobotView)
        void StepWindups(float now)
        {
            List<int> gone = null;
            foreach (var kv in windups)
            {
                var w = kv.Value; float k = (now - w.start) / w.dur;
                if (k > 1.6f) { (gone ??= new List<int>()).Add(kv.Key); continue; }
                var v = View(w.id);
                if (v) { var ev = w.viewOwned ? v.GetComponent<ElectrobotView>() : null; w.at = ev && ev.Bound ? ev.EmitterPos : v.MuzzlePos(); }
                float g = Mathf.Clamp01(k);
                if (!w.viewOwned) Arcs.Glow(w.at, Mathf.Lerp(0.2f, 0.8f, Mathf.Pow(g, 0.7f)), 0.3f, 9f, 2f, Glow, Mathf.Clamp01(k * 3f));
                AddCand(12_000 + w.id, w.at, 14f * g, 7f, 5f, false);
            }
            if (gone != null) foreach (var id in gone) windups.Remove(id);
        }

        // ------------------------------------------------------------------------------------------------ idle: ElectrobotView attach + hum
        readonly List<string> humKeys = new List<string>(4);
        readonly List<ElectrobotView> evs = new List<ElectrobotView>(8);
        float nextScan;

        void ScanElectrobots(float now)
        {
            if (now < nextScan || !G) return;
            nextScan = now + 0.5f;
            evs.Clear();
            foreach (var kv in G.Views)
            {
                var v = kv.Value; if (!v || v.Obj == null || v.Obj.Kind != ZKind.Robot || v.Obj.Type != ZElectro.Type) continue;
                var ev = v.GetComponent<ElectrobotView>(); if (!ev) ev = v.gameObject.AddComponent<ElectrobotView>();
                evs.Add(ev);
            }
            ElectrobotViews = evs.Count;
            // the hum on the 2 nearest on screen
            var ad = AudioDirector.I; if (!ad || !sounds) return;
            evs.Sort((a, b) => (a.transform.position - Focus).sqrMagnitude.CompareTo((b.transform.position - Focus).sqrMagnitude));
            var want = new List<string>(2);
            foreach (var ev in evs)
            {
                if (want.Count >= 2) break;
                if (!ev || !ev.isActiveAndEnabled || ev.Dying || !OnScreen(ev.transform.position, 0.1f)) continue;
                string key = "eb_hum:" + ev.Id; want.Add(key);
                if (!ad.LoopActive(key)) ad.StartLoop(key, "electro_idle_hum", ev.transform.position, ev.transform);
            }
            foreach (var k in humKeys) if (!want.Contains(k)) ad.StopLoop(k, 0.4f);
            humKeys.Clear(); humKeys.AddRange(want);
        }

        // ------------------------------------------------------------------------------------------------ voices
        struct Sched { public float at; public string id; public Vector3 pos; public int emitter; public float pitch; public bool voice; }
        readonly List<Sched> sched = new List<Sched>(8);
        void SayLater(string id, float delay) { if (AudioDirector.Has(id)) sched.Add(new Sched { at = Time.time + delay, id = id, voice = true }); }

        void StepSched(float now)
        {
            for (int i = sched.Count - 1; i >= 0; i--)
            {
                var s = sched[i]; if (now < s.at) continue;
                sched.RemoveAt(i);
                // voices go through AudioDirector's SayImpl on Alan's channel: the speaking line always finishes, another event waits for
                // it (<= 1.1 s) or is dropped, so a bark never cuts a line mid-word (unit_voices_v1 rule)
                if (s.voice) AudioDirector.Say(s.id);
                else AudioDirector.Fire(s.id, s.pos, s.emitter, 1f, s.pitch);
            }
        }

        // ------------------------------------------------------------------------------------------------ lights (priority pool, as the pyro)
        struct LightCand { public int key; public Vector3 pos; public float three, range, prio, score; public bool instant; }
        readonly List<LightCand> cands = new List<LightCand>(32);
        sealed class LightSlot { public Light l; public int key = -1; public float k; public bool wanted, instant; public Vector3 pos; public float three, range; }
        readonly List<LightSlot> slots = new List<LightSlot>();
        struct Flash { public Vector3 at; public float born, life, three, range; public int prio; public float seed; }
        readonly List<Flash> flashes = new List<Flash>(16);

        void BuildLights()
        {
            for (int i = 0; i < 12; i++)
            {
                var go = new GameObject("ElectroLight" + i); go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>(); l.type = LightType.Point; l.shadows = LightShadows.None; l.enabled = false;
                l.renderMode = LightRenderMode.ForcePixel; l.color = LightColor;
                slots.Add(new LightSlot { l = l });
            }
        }

        /// <summary>A short flash light (muzzle, impact, deaths): three-units peak, (1 - t)^1.6 decay with a lightning flicker.</summary>
        void AddFlash(Vector3 at, float lift, float life, float three, float range, int prio) =>
            flashes.Add(new Flash { at = at + Vector3.up * lift * 0.5f, born = Time.time, life = life, three = three, range = range, prio = prio, seed = R(0f, 10f) });

        public void AddCand(int key, Vector3 pos, float three, float range, float prio, bool instant)
        {
            if (maxLights <= 0 || three <= 0.05f) return;
            float vis = OnScreen(pos, 0.1f) ? 1f : OnScreen(pos, 0.45f) ? 0.35f : 0f;
            if (vis <= 0f) return;
            float d = Vector3.Distance(pos, Focus);
            cands.Add(new LightCand { key = key, pos = pos, three = three, range = range, prio = prio, instant = instant, score = three * prio * vis / (1f + d / 35f) });
        }

        sealed class ScoreDesc : IComparer<LightCand> { public int Compare(LightCand a, LightCand b) => b.score.CompareTo(a.score); }
        static readonly ScoreDesc ScoreCmp = new ScoreDesc();

        void StepLights(float now, float dt)
        {
            for (int i = flashes.Count - 1; i >= 0; i--)
            {
                var f = flashes[i]; float k = (now - f.born) / f.life;
                if (k >= 1f) { flashes.RemoveAt(i); continue; }
                float fl = 0.72f + 0.28f * Mathf.Sin((now + f.seed) * 63f) * Mathf.Sin((now + f.seed) * 29f);
                AddCand(40_000 + i, f.at, f.three * Mathf.Pow(1f - k, 1.6f) * fl, f.range, f.prio, true);
            }
            foreach (var s in slots) s.wanted = false;
            cands.Sort(ScoreCmp);
            int n = Mathf.Min(Mathf.Min(maxLights, slots.Count), cands.Count);
            for (int i = 0; i < n; i++)
            {
                var c = cands[i];
                LightSlot slot = null;
                foreach (var s in slots) if (s.key == c.key) { slot = s; break; }
                if (slot == null) foreach (var s in slots) if (s.key < 0 || (!s.wanted && s.k <= 0.001f)) { slot = s; slot.key = c.key; slot.k = 0f; break; }
                if (slot == null) continue;
                slot.wanted = true; slot.pos = c.pos; slot.three = c.three; slot.range = c.range; slot.instant = c.instant;
            }
            int on = 0;
            float nk = Mathf.Clamp01(NightGrade.NightK);
            float gain = OpenZFxCatalog.ThreeToUrpLight * OpenZFxCatalog.LightGain * lightGain * Mathf.Lerp(dayLightBoost, nightLightBoost, nk);
            float rangeK = Mathf.Lerp(1f, nightRangeScale, nk);                                        // the pyro's rule: no wider pool at night
            foreach (var s in slots)
            {
                s.k = s.wanted && s.instant ? 1f : Mathf.MoveTowards(s.k, s.wanted ? 1f : 0f, dt / (s.wanted ? 0.06f : 0.08f));
                if (s.k <= 0.001f && !s.wanted) { if (s.l.enabled) s.l.enabled = false; s.key = -1; continue; }
                s.l.transform.position = s.pos; s.l.range = s.range * rangeK; s.l.intensity = s.three * gain * s.k;
                if (!s.l.enabled) s.l.enabled = true;
                on++;
            }
            LightsOn = on; PeakLights = Mathf.Max(PeakLights, on);
            cands.Clear();
        }

        // ------------------------------------------------------------------------------------------------ frame
        void LateUpdate()
        {
            if (!ready) return;
            float now = Time.time, dt = Time.deltaTime;
            if (!cam) cam = Camera.main;
            if (!rts && now >= nextRtsSearch) { rts = FindFirstObjectByType<RtsCamera>(); nextRtsSearch = now + 2f; }
            ScanElectrobots(now);
            StepSched(now);
            StepWindups(now);
            StepBolts(now);
            StepRings(now);
            StepStuns(now);
            StepZapDeaths(now);
            StepDeploys(now);
            if (now < sustainUntil) OpenZBloomDirector.Sustain(bloomSustain * (sustainUntil - now) / 0.22f);
            Scorch?.Draw();
            Arcs.Render();
            StepLights(now, dt);
        }

        // ------------------------------------------------------------------------------------------------ show / checks
        /// <summary>Menu "Electro FX Show" (Play): the full bolt + stun look from one unit view to another with no Electrobot in the sim
        /// (the sim is not touched: the stun FX runs as a view-only jolt of `stunSeconds`).</summary>
        public void DemoBolt(UnitView from, UnitView to, float stunSeconds = 15f)
        {
            if (!ready || !from || !to || from.Obj == null || to.Obj == null) return;
            Charge(new ZEvent { type = ZEventType.ElectroCharge, objId = from.Obj.Id, otherId = to.Obj.Id, value = 0.35f }, from.Obj, to.Obj);
            StartCoroutine(DemoRelease(from, to, stunSeconds));
        }

        System.Collections.IEnumerator DemoRelease(UnitView from, UnitView to, float stunSeconds)
        {
            yield return new WaitForSeconds(0.35f / GameSpeed);
            if (!from || !to) yield break;
            Bolt(new ZEvent { type = ZEventType.ElectroBolt, objId = from.Obj.Id, otherId = to.Obj.Id, value = 1666, flag = false, flag2 = false }, from.Obj, to.Obj);
            if (stuns.TryGetValue(to.Obj.Id, out var s)) { s.demo = true; s.Refresh(this, stunSeconds, true); }
        }

        /// <summary>Menu "Electro FX Show": `cycles` full cycles (windup, bolt, 3.65 s recharge) from one view onto another.</summary>
        public void DemoCycle(UnitView from, UnitView to, int cycles, float stunSeconds = 15f) { if (ready) StartCoroutine(DemoLoop(from, to, cycles, stunSeconds)); }

        System.Collections.IEnumerator DemoLoop(UnitView from, UnitView to, int cycles, float stunSeconds)
        {
            for (int i = 0; i < cycles && from && to; i++)
            {
                DemoBolt(from, to, stunSeconds);
                var ev = from.GetComponent<ElectrobotView>();
                yield return new WaitForSeconds(4f / GameSpeed);
                if (ev) ev.OnReady();
            }
        }

        public string Report() =>
            $"[OpenZElectroFx] {Status}: bolts {Bolts} (active {bolts.Count}), charges {Charges}, fizzles {Fizzles}, stuns {Stuns} (active {stuns.Count}), " +
            $"Electrobot views {ElectrobotViews}, lights {LightsOn} (peak {PeakLights} / {maxLights}), arc verts peak {Arcs?.PeakVerts} (dropped {Arcs?.Dropped}), " +
            $"Lichtenberg {Scorch?.Active}";

        public IEnumerable<ElectrobotView> Electrobots => evs;
        internal Camera CamRef => cam;
    }
}
