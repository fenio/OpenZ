using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Beside the two robots' shuttle of level 16).
    /// THE LAMP IS THE GAME'S OWN SEARCHLIGHT: the drum and the yoke of the searchlights on the fort's towers
    /// (NightMeshes.SearchDrum, NightMeshes.SearchYoke, the gunmetal NightMeshes.Housing), their dark glass, lens glow, halo, beam
    /// cone (NightMeshes.Cone with the OpenZ/NightBeam material) and ground pool (NightMeshes.Cube with OpenZ/NightPool), put on
    /// the stand's pedestal (<see cref="headMount"/>) at the start of Play at <see cref="headScale"/> times their size, lit with
    /// the night lights' own numbers (lens x 7, halo, beam and pool gains read from NightLights). The stand is the kit's trailer
    /// model (the game has wall brackets and roof posts only).
    /// <list type="bullet">
    /// <item>ON AT DUSK, OFF AT DAWN on the day clock, as the level's lamp posts are (<see cref="onAt"/>, <see cref="offAt"/>:
    /// the lamps switch on between 0.335 and 0.357 from west to east and off from 0.893); off, the drum looks down as the
    /// fort's do. The switch-on is the game's own sound of a searchlight coming on (<see cref="onEvent"/>, quiet).</item>
    /// <item>THE BEAM SWEEPS SLOWLY across what it watches (<see cref="aimAt"/>: the ship) and the ground before it: the yoke
    /// turns <see cref="sweep"/> degrees to each side of it once every <see cref="period"/> s of the game clock, the drum nods
    /// a little (<see cref="nod"/>).</item>
    /// <item>NO REAL LIGHT BEYOND THE NIGHT BUDGET: it owns one unshadowed spot light that it offers to the night lights' budget
    /// (NightBudgetShare, as the base props, the harbour lights and the alarm beacons do): the budget ranks it with every lamp,
    /// flood and searchlight of the level and grants it a real light only inside its own count (NightLights.maxRealLights);
    /// not granted, it is drawn as every unbudgeted night light is: lens, halo, beam cone and the ground pool picture, which
    /// drapes over the grass and over the ship's lower hull. The two cross-fade (the budget's 0.35 s).</item>
    /// <item>A SMALL PROP OF ITS OWN, DESTRUCTIBLE AS THE LEVEL'S LAMP POSTS ARE (view only: no map item, no closed tile): the
    /// detail on the same object (DetailProp, explosives only) breaks it when a blast reaches it; a vehicle that drives into
    /// it knocks it down; and <see cref="BreakWith"/> lets its ship take it along. Broken: light, beam and pool off at once,
    /// the head flies (the detail's loose part), the burnt stand is left (RUIN_Stand). Robots step round it
    /// (UnitView.Obstacles, as round a lamp post).</item>
    /// </list>
    /// View only. Play check: On, Level, Out, YawOff, PitchNow, AxisOffTarget, RealShare, Broken, SwitchOns, Light, Beam, Pool.
    /// </summary>
    [RequireComponent(typeof(DetailProp))]
    [DefaultExecutionOrder(60)]
    public sealed class OzFieldSearchlight : MonoBehaviour
    {
        public static readonly List<OzFieldSearchlight> All = new List<OzFieldSearchlight>();

        [Header("set by the menu OpenZ/L16 Jungle/14")]
        [Tooltip("the top of the stand's pedestal (the model's marker MK_Head): the yoke's turntable sits here")] public Transform headMount;
        [Tooltip("what it watches: its beam sweeps across this (the ship's root)")] public Transform aimAt;
        [Tooltip("the beam's middle passes this high over aimAt (m)")] public float aimHeight = 0.9f;
        [Tooltip("the game's searchlight head (0.64 m lens) x this")] public float headScale = 1.5f;
        [Header("the light (the fort searchlights' white)")]
        public Color colour = new Color(0.86f, 0.92f, 1f, 1f);
        [Tooltip("cone angle (degrees), reach (m), real-light strength, rank in the night budget")] public float angle = 28f;
        public float range = 16f, intensity = 55f, priority = 3f;
        [Header("the sweep (seconds on the game clock)")]
        [Tooltip("degrees to each side of the watched thing")] public float sweep = 22f;
        public float period = 14f;
        [Tooltip("degrees the drum nods up and down while it sweeps")] public float nod = 2f;
        [Header("day clock (0 noon, 0.5 midnight): on at dusk, off at dawn, as the level's lamp posts")]
        public float onAt = 0.348f;
        public float offAt = 0.895f;
        public string onEvent = "ngt_searchlight_on";
        [Range(0f, 1f)] public float onVolume = 0.4f;
        [Header("prop")]
        [Tooltip("a moving vehicle whose middle comes this near knocks it down (m)")] public float knockRadius = 1.3f;
        [Tooltip("robots step round a disc of this radius (m)")] public float obstacleRadius = 0.7f;

        /// <summary>The day clock says it should burn (and it is not broken).</summary>
        public bool On { get; private set; }
        /// <summary>The switch level (0 to 1) and the light it gives out (with the faint lamp flicker).</summary>
        public float Level { get; private set; }
        public float Out { get; private set; }
        /// <summary>Where the yoke stands against the line to the watched thing (degrees, minus = left of it), and the drum's pitch (degrees below level).</summary>
        public float YawOff { get; private set; }
        public float PitchNow { get; private set; } = 62f;
        /// <summary>The angle between the beam's middle and the line from the lens to the watched point (degrees): under half the cone = the beam is on it.</summary>
        public float AxisOffTarget { get; private set; } = 180f;
        /// <summary>How much of its light is a real light now (0 = drawn as lens, beam and pool only; 1 = the budget granted it).</summary>
        public float RealShare { get; private set; }
        public bool Broken { get; private set; }
        public int SwitchOns { get; private set; }
        public Light Light { get; private set; }
        public Renderer Beam { get; private set; }
        public Renderer Lens { get; private set; }
        public Renderer Halo { get; private set; }
        public Renderer Pool { get; private set; }
        public Transform Head { get; private set; }

        DetailProp dp; Transform yaw, pitch, beamT, poolT, poolHolder; NightFixture fix; MaterialPropertyBlock mpb;
        float lensShown = -1f, haloShown = -1f, beamShown = -1f, poolShown = -1f, yawBase, pitchBase, seed, nextKnock;
        int obstacle = -1; bool built;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); Dark(); }

        void Awake()
        {
            dp = GetComponent<DetailProp>();
            if (!Application.isPlaying) return;
            mpb = new MaterialPropertyBlock(); seed = Mathf.Abs(transform.position.x * 0.37f + transform.position.z * 0.11f) % 1f;
            dp.Breaking += OnBreaking;
            Build();                                                   // before the detail's Start reads its parts: the head is its loose part
        }

        void OnDestroy()
        {
            if (dp) dp.Breaking -= OnBreaking;
            if (fix != null) NightBudgetShare.Fixtures.Remove(fix);
            if (poolHolder) Destroy(poolHolder.gameObject);
            Release();
        }

        void Start()
        {
            if (!Application.isPlaying || Broken) return;
            // robots step round it, as round a lamp post (the night lights cleared the list when the level was built)
            obstacle = UnitView.Obstacles.Count; UnitView.Obstacles.Add(new Vector3(transform.position.x, transform.position.z, obstacleRadius));
        }

        void Release() { if (obstacle >= 0 && obstacle < UnitView.Obstacles.Count) UnitView.Obstacles[obstacle] = Vector3.zero; obstacle = -1; }

        /// <summary>The game's searchlight head on the pedestal: yoke (turns), drum (nods), dark glass, lens glow, halo, beam cone,
        /// the spot light; the ground pool beside the object (it lies on the ground, it is no part of the prop).</summary>
        void Build()
        {
            if (built) return; built = true;
            var mount = headMount ? headMount : transform; var H = NightMeshes.Housing();
            yaw = new GameObject("LOOSE_Head").transform; yaw.SetParent(mount, false);
            yaw.localPosition = new Vector3(0f, 0.33f * headScale / Mathf.Max(0.01f, mount.lossyScale.y), 0f);      // the turntable's underside (0.33 under the trunnions) on the pedestal
            yaw.localRotation = Quaternion.identity; yaw.localScale = Vector3.one * (headScale / Mathf.Max(0.01f, mount.lossyScale.y));
            yaw.gameObject.layer = gameObject.layer; Head = yaw;
            NightMeshes.Part("Yoke", yaw, NightMeshes.SearchYoke(), H, true, Vector3.zero, Quaternion.identity, Vector3.one);
            pitch = new GameObject("Pitch").transform; pitch.SetParent(yaw, false); pitch.localPosition = new Vector3(0f, 0.02f, 0f);
            NightMeshes.Part("Drum", pitch, NightMeshes.SearchDrum(), H, true, Vector3.zero, Quaternion.identity, Vector3.one);
            NightMeshes.Part("Glass", pitch, NightMeshes.Disc(), NightMeshes.DarkGlass(), false, new Vector3(0f, 0f, 0.335f), Quaternion.identity, new Vector3(0.31f, 0.31f, 1f));
            Lens = NightMeshes.Part("Lens", pitch, NightMeshes.Disc(), NightMeshes.Lens(), false, new Vector3(0f, 0f, 0.345f), Quaternion.identity, new Vector3(0.3f, 0.3f, 1f)).GetComponent<Renderer>();
            Halo = NightMeshes.Part("Halo", pitch, NightMeshes.Quad(), NightMeshes.Halo(), false, new Vector3(0f, 0f, 0.5f), Quaternion.identity, Vector3.one * 1.3f).GetComponent<Renderer>();
            var b = NightMeshes.Part("Beam", pitch, NightMeshes.Cone(), NightMeshes.Beam(), false, new Vector3(0f, 0f, 0.36f), Quaternion.identity, Vector3.one);
            Beam = b.GetComponent<Renderer>(); beamT = b.transform;
            foreach (var t in yaw.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = gameObject.layer;
            Lens.enabled = Halo.enabled = Beam.enabled = false;
            // the one real light: unshadowed by itself, offered to the night budget
            var lg = new GameObject("Light"); lg.transform.SetParent(pitch, false); lg.transform.localPosition = new Vector3(0f, 0f, 0.38f);
            Light = lg.AddComponent<Light>();
            Light.type = LightType.Spot; Light.range = Mathf.Max(0.5f, range); Light.color = colour; Light.intensity = 0f; Light.enabled = false;
            Light.shadows = LightShadows.None; Light.shadowStrength = 0.88f; Light.renderMode = LightRenderMode.ForcePixel;
            Light.spotAngle = Mathf.Clamp(angle, 1f, 179f); Light.innerSpotAngle = Light.spotAngle * 0.35f; Light.shadowNearPlane = 1.5f * headScale;
            fix = new NightFixture
            {
                Kind = NightKind.Searchlight, Id = "field_searchlight_" + name, Light = Light, Root = transform, Col = colour, ColLinear = colour.linear,
                Intensity = intensity, Range = range, Angle = angle, Priority = priority, ShadowCandidate = false, BeamK = 1f, PoolK = 1f,
            };
            NightBudgetShare.Fixtures.Add(fix);
            poolHolder = new GameObject(name + "_Pool").transform; poolHolder.SetParent(transform.parent, false);
            var pg = NightMeshes.Part("Pool", poolHolder, NightMeshes.Cube(), NightMeshes.Pool(), false, Vector3.zero, Quaternion.identity, Vector3.one);
            pg.layer = gameObject.layer; poolT = pg.transform; Pool = pg.GetComponent<Renderer>(); Pool.enabled = false;
            pitch.localRotation = Quaternion.Euler(PitchNow, 0f, 0f);
        }

        /// <summary>Its ship is destroyed: the blast takes the searchlight along (the detail's own break, away from `from`).</summary>
        public void BreakWith(Vector3 from) { if (dp && !Broken) dp.Break(from); }

        void OnBreaking(DetailProp d, Vector3 from)
        {
            if (Broken) return;
            Broken = true; On = false; Level = 0f; Out = 0f;
            Dark();
            if (fix != null) { fix.LightOut = 0f; NightBudgetShare.Fixtures.Remove(fix); }
            Release();
            if (AudioDirector.Has("ngt_light_fizzle")) AudioDirector.Fire("ngt_light_fizzle", transform.position + Vector3.up * 1.4f, -1, 0.6f);
        }

        void Dark()
        {
            if (Lens) Lens.enabled = false; if (Halo) Halo.enabled = false; if (Beam) Beam.enabled = false; if (Pool) Pool.enabled = false;
            if (Light) Light.enabled = false;
            lensShown = haloShown = beamShown = poolShown = -1f; RealShare = 0f;
        }

        void SetColor(Renderer r, Color c, ref float shown)
        {
            if (!r) return;
            float key = c.r + c.g * 1.7f + c.b * 0.6f;
            if (shown >= 0f && Mathf.Abs(key - shown) <= Mathf.Max(0.004f, shown * 0.012f)) return;
            shown = key;
            bool on = key > 0.0015f;
            if (r.enabled != on) r.enabled = on;
            if (!on) return;
            mpb.Clear(); mpb.SetColor(NightMeshes.ColorId, c); r.SetPropertyBlock(mpb);
        }

        void Update()
        {
            if (!Application.isPlaying || !built || Broken) return;
            var g = ZGame.I; float dt = g && g.paused ? 0f : Time.deltaTime;
            float t = NightGrade.TimeOfDay, night = NightGrade.NightK;
            On = NightGrade.LightsOn(t, onAt, offAt);
            float prev = Level;
            Level = On ? Mathf.MoveTowards(Level, 1f, dt / 0.35f) : Mathf.MoveTowards(Level, 0f, dt / 0.6f);
            if (prev <= 0f && Level > 0f)
            {
                SwitchOns++;
                var at = pitch.position;
                if (AudioDirector.Has(onEvent)) AudioDirector.Fire(onEvent, at, -1, onVolume);
                else OzSoundLog.Add("L16", onEvent, null, "DROP no event", 0f, at);
            }
            Out = Level * (OzMotion.Reduced ? 0.96f : 0.975f + 0.025f * Mathf.PerlinNoise(seed * 3.3f, Time.time * 8f));
            Aim(dt);
            // drawn with the night lights' own numbers (NightLights.Visuals): lens x 7, halo, beam, pool
            var nl = NightLights.I;
            float dusk = Mathf.Lerp(0.25f, 1f, night); var c = colour.linear;
            bool halos = !nl || nl.halos, beams = !nl || nl.beams, pools = !nl || nl.pools;
            float haloGain = nl ? nl.haloGain : 0.35f, beamGain = nl ? nl.beamGain : 1f, poolGain = nl ? nl.poolGain : 0.22f;
            float lightGain = nl ? nl.lightGain * nl.searchLightGain : 3f;
            SetColor(Lens, c * (7f * Out), ref lensShown);
            SetColor(Halo, c * (halos ? haloGain * dusk * Out * 2.6f : 0f), ref haloShown);
            SetColor(Beam, c * (beams ? beamGain * Out * Mathf.Pow(night, 0.8f) * 0.42f : 0f), ref beamShown);
            // the real light, when the night budget grants one; else the pool picture carries the light on the ground and the hull
            fix.LightOut = Out; fix.Col = colour; fix.ColLinear = c;
            RealShare = NightBudgetShare.HookFrame >= Time.frameCount - 2 ? fix.RealK : 0f;
            float k = Out * RealShare; bool lit = k > 0.002f;
            if (Light.enabled != lit) Light.enabled = lit;
            if (lit)
            {
                Light.intensity = intensity * k * lightGain;
                var sh = fix.ShadowK > 0.001f ? LightShadows.Soft : LightShadows.None;      // the NIGHT LIGHT SHADOWS option's nearby slots, as for a lamp or a flood
                if (Light.shadows != sh) Light.shadows = sh;
                if (sh != LightShadows.None) { Light.shadowStrength = NightShadows.Strength * fix.ShadowK; NightShadows.Configure(Light, fix.ShadowHigh); }
            }
            float e = intensity / Mathf.Max(1f, poolDist * poolDist);
            SetColor(Pool, c * (pools ? poolGain * Out * dusk * (1f - RealShare) * Mathf.Min(10f, e * 4f) : 0f), ref poolShown);
            // a vehicle drives into it: down it goes, as a lamp post does
            if (g && g.World != null && Time.time >= nextKnock)
            {
                nextKnock = Time.time + 0.1f;
                var objs = g.World.Objects; var p0 = transform.position;
                for (int i = 0; i < objs.Count; i++)
                {
                    var o = objs[i];
                    if (o.Kind != ZKind.Vehicle || o.Destroyed || !o.IsMoving) continue;
                    var w = ZGame.PxToWorld(o.CenterX, o.CenterY);
                    float dx = w.x - p0.x, dz = w.z - p0.z;
                    if (dx * dx + dz * dz < knockRadius * knockRadius) { dp.Break(new Vector3(w.x, p0.y, w.z)); break; }
                }
            }
        }

        float poolDist = 6f;

        /// <summary>The yoke and the drum: on, they follow the slow sweep across the watched thing; off, the drum looks down.
        /// Then the beam cone's length and the pool's place on the ground between the lamp and the watched thing.</summary>
        void Aim(float dt)
        {
            var mount = yaw.parent; var lens = pitch.position;
            var target = aimAt ? aimAt.position + Vector3.up * aimHeight : lens + transform.forward * 7f;
            var d = mount.InverseTransformDirection(target - lens);
            yawBase = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float flat = Mathf.Sqrt(d.x * d.x + d.z * d.z);
            pitchBase = Mathf.Atan2(-d.y, Mathf.Max(0.01f, flat)) * Mathf.Rad2Deg;
            float tYaw = 0f, tPitch = 62f;
            if (On || Level > 0.01f)
            {
                float ph = (float)(OzShuttleLights.GameClock() * 2.0 * Mathf.PI / Mathf.Max(4f, period)) + seed * 6.283f;
                tYaw = sweep * Mathf.Sin(ph); tPitch = pitchBase + nod * Mathf.Sin(ph * 2.3f);
            }
            float a = 1f - Mathf.Exp(-2.4f * dt);
            YawOff = Mathf.LerpAngle(YawOff, tYaw, a);
            PitchNow = Mathf.Lerp(PitchNow, Mathf.Clamp(tPitch, -10f, 75f), a);
            yaw.localRotation = Quaternion.Euler(0f, yawBase + YawOff, 0f);
            pitch.localRotation = Quaternion.Euler(PitchNow, 0f, 0f);
            var dir = pitch.forward; var to = target - lens;
            AxisOffTarget = to.sqrMagnitude > 1e-4f ? Vector3.Angle(dir, to) : 0f;
            if (Out <= 0.001f) return;
            // the beam ends in what it watches; the pool lies on the ground from before the lamp to the watched thing's middle
            float dist = new Vector3(to.x, 0f, to.z).magnitude;
            float ta = Mathf.Tan(Mathf.Clamp(angle * 0.5f, 2f, 40f) * Mathf.Deg2Rad) * 1.2f, len = Mathf.Min(range, dist + 2f);
            float inv = 1f / Mathf.Max(0.01f, pitch.lossyScale.x);
            beamT.localScale = new Vector3(len * ta * inv, len * ta * inv, (len + 0.8f) * inv);
            var hz = new Vector3(dir.x, 0f, dir.z); if (hz.sqrMagnitude < 1e-4f) hz = transform.forward; hz.Normalize();
            float near = Mathf.Max(1.2f, dist * 0.2f), far = Mathf.Min(range, dist + 1.5f), mid = (near + far) * 0.5f;
            var centre = lens + hz * mid; var gm = ZGame.I;
            float gy = gm ? gm.TerrainY(centre.x, centre.z) : transform.position.y;
            poolT.SetPositionAndRotation(new Vector3(centre.x, gy + 0.35f, centre.z), Quaternion.LookRotation(hz, Vector3.up));
            float w = Mathf.Clamp(2f * dist * Mathf.Tan(Mathf.Clamp(angle * 0.5f, 5f, 65f) * Mathf.Deg2Rad) * 1.2f, 1.2f, range * 1.3f);
            poolT.localScale = new Vector3(w, 3.8f, Mathf.Max(w, far - near));
            poolDist = Mathf.Max(1f, mid);
        }
    }
}
