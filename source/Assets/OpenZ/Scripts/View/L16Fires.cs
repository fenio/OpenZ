using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// View only: nothing here is read by the rules, a fire cannot hurt a unit.
    /// <list type="bullet">
    /// <item><b>Data</b>: Resources/OpenZ/Night/L16_fires.json (the 20 fires: kind, clock values, flame / light / smoke points; the
    /// light table of the four kinds). The models stand in the scene under this object: Fire_&lt;id&gt; with the children Out (the
    /// cold state, shown by day) and Lit (the glowing state: embers, glowing ash; for a bowl its glowing coals).</item>
    /// <item><b>Clock</b>: the level's own day and night clock, the switch test of the street lamps (NightGrade.LightsOn). Campfires
    /// catch first, then the bowls, then the torches site after site from west to east; at dawn the torches burn down first, the
    /// campfires last. Catching takes catchSeconds, going out dieSeconds.</item>
    /// <item><b>Light</b>: each fire is one fixture of the night light budget (NightBudgetShare, the same list the vehicle headlights
    /// and the base lamps are in), so the fires come out of the game's budget of real lights, not on top of it. Of the fires the
    /// budget grants, never more than maxReal (8) carry a real light at one moment, a light that is fading out counted in (Cap:
    /// own ranking without the flicker, a holder keeps its light until another leads by 25 %, the newcomer waits until the old
    /// light is dark); the others keep flame, embers, halo and the ground pool the lamps use. Flicker = fast noise + a slow
    /// breath, own seed per fire; steady with the calm-screen option.</item>
    /// <item><b>Flame</b>: the ground fire's own flame tongues (OpenZPyroFire.Tongue), drawn only, only for fires on screen.</item>
    /// <item><b>Smoke and sparks</b>: a thin column over a campfire and a bowl, a spark pop when a fire catches; a pale wisp over a
    /// campfire for the first 40 s after dawn.</item>
    /// <item><b>Sound</b> (own 3D sources like the level's waterfalls, every request in the sound log): amb_fire_lightup_big /
    /// amb_fire_lightup_torch when a fire catches (at most 3 at once), amb_fire_campfire_loop for the four burning campfires and
    /// bowls nearest the listener, amb_fire_torch_loop for the four nearest torches, amb_fire_dying when one goes out.</item>
    /// </list>
    /// Play check: Readout(), LitNow, BurningNow, RealNow, MaxRealSeen, TonguesDrawn.
    /// </summary>
    public sealed class L16Fires : MonoBehaviour
    {
        public static L16Fires I { get; private set; }
        public static bool Enabled = true;
        public const string DataPath = "OpenZ/Night/L16_fires";
        public const string ResLoopCamp = "OpenZ/Audio/Sfx/L16/sd_campfire_crackle_loop", ResLoopTorch = "OpenZ/Audio/Sfx/L16/sd_torch_flame_loop",
                            ResUpBig = "OpenZ/Audio/Sfx/L16/sd_fire_lightup_big", ResUpTorch = "OpenZ/Audio/Sfx/L16/sd_fire_lightup_torch", ResDying = "OpenZ/Audio/Sfx/L16/sd_fire_dying_hiss";

        [Tooltip("Real-light gain of a fire x the night lights' own gain (tuned by eye in the game: a pool on stone and grass, never a white blob)")]
        [Range(0f, 4f)] public float fireLightGain = 1f;
        [Range(0f, 3f)] public float haloK = 1.6f, poolK = 1f;
        [Tooltip("Above this camera height a fire draws one flame tongue instead of its bundle (a flame is a few pixels there)")]
        public float bundleBelowHeight = 110f;
        public float campLoopVol = 0.25f, torchLoopVol = 0.16f, upVol = 0.4f, dyingVol = 0.2f, hearRange = 45f;

        [Serializable] public class KindRow { public string kind, color; public float intensity, range, flicker, speed, priority, flame_h, flame_w, light_up; public int tongues; }
        [Serializable] public class FireRow { public string id, kind, name, host; public float onAt, offAt, scale; public float[] flame, light, smoke; }
        [Serializable] public class Doc { public string level; public int maxReal = 8; public float catchSeconds = 1.2f, dieSeconds = 2.5f; public KindRow[] kinds; public FireRow[] fires; }

        public sealed class Fire
        {
            public FireRow row; public KindRow k; public bool big, campfire;
            public GameObject outGo, litGo; public Vector3 flame, lightAt, smoke; public float scale = 1f, seed;
            public float level; public bool want, dead, shownLit, capOn; public float capK, capScore, real; public int ord;
            public NightFixture fix; public Light lamp; public Renderer halo, pool; public float haloShown = -1f, poolShown = -1f, poolE;
            public Color colLin; public float nextSmoke, wispUntil; public bool visible;
            public bool Lit => level > 0.5f;
        }

        readonly List<Fire> fires = new List<Fire>();
        public IReadOnlyList<Fire> Fires => fires;
        Doc doc; DayNight dayNight; Camera cam; RtsCamera rts; MaterialPropertyBlock mpb; float nextCap, nextLoops;
        readonly List<Fire> rank = new List<Fire>(24);
        AudioSource[] campSrc, torchSrc; Fire[] campFire, torchFire;
        readonly float[] upTimes = new float[3], dyTimes = new float[2];

        public static int Count => I ? I.fires.Count : 0;
        public static int LitNow { get; private set; }
        public static int BurningNow { get; private set; }
        public static int RealNow { get; private set; }
        public static int MaxRealSeen;
        public static int TonguesDrawn { get; private set; }
        public int MaxReal => doc != null ? doc.maxReal : 8;

        void Awake()
        {
            if (I && I != this) { enabled = false; return; }
            I = this; mpb = new MaterialPropertyBlock(); MaxRealSeen = 0;
            var ta = Resources.Load<TextAsset>(DataPath);
            doc = ta ? JsonUtility.FromJson<Doc>(ta.text) : null;
            if (doc == null || doc.fires == null || doc.kinds == null) { Debug.LogWarning("[OpenZ] L16 fires: Resources/" + DataPath + ".json missing or unreadable: no fire burns"); enabled = false; return; }
            var kinds = new Dictionary<string, KindRow>(); foreach (var k in doc.kinds) if (k != null && k.kind != null) kinds[k.kind] = k;
            int missing = 0;
            foreach (var r in doc.fires)
            {
                if (r == null || r.flame == null || r.flame.Length < 3 || !kinds.TryGetValue(r.kind ?? "", out var k)) continue;
                var root = transform.Find("Fire_" + r.id);
                if (!root) { missing++; continue; }
                var f = new Fire { row = r, k = k, big = r.kind == "campfire" || r.kind == "bowl", campfire = r.kind == "campfire", scale = r.scale > 0f ? r.scale : 1f,
                                   flame = V(r.flame), lightAt = V(r.light), smoke = V(r.smoke), seed = Hash(r.id), ord = fires.Count };
                var o = root.Find("Out"); var l = root.Find("Lit"); f.outGo = o ? o.gameObject : null; f.litGo = l ? l.gameObject : null;
                if (f.litGo) f.litGo.SetActive(false);
                if (f.outGo) f.outGo.SetActive(true);
                Color c; if (!ColorUtility.TryParseHtmlString(k.color, out c)) c = new Color(1f, 0.64f, 0.3f);
                f.colLin = c.linear;
                var lg = new GameObject("FireLight_" + r.id); lg.transform.SetParent(transform, false); lg.transform.position = f.lightAt;
                f.lamp = lg.AddComponent<Light>(); f.lamp.type = LightType.Point; f.lamp.range = k.range; f.lamp.color = c; f.lamp.intensity = 0f; f.lamp.enabled = false;
                f.lamp.shadows = LightShadows.None; f.lamp.renderMode = LightRenderMode.ForcePixel;
                f.fix = new NightFixture { Kind = NightKind.Uplight, Id = "l16fire_" + r.id, Light = f.lamp, Col = c, ColLinear = f.colLin, Intensity = k.intensity, Range = k.range, Priority = k.priority, ShadowCandidate = false };
                NightBudgetShare.Fixtures.Add(f.fix);
                float hr = Mathf.Lerp(0.5f, 1.3f, Mathf.InverseLerp(6f, 14f, k.intensity)) * f.scale;
                f.halo = NightMeshes.Part("Halo", lg.transform, NightMeshes.Quad(), NightMeshes.Halo(), false, new Vector3(0f, -k.light_up * 0.6f, 0f), Quaternion.identity, Vector3.one * hr).GetComponent<Renderer>();
                f.halo.enabled = false;
                var pg = NightMeshes.Part("FirePool_" + r.id, transform, NightMeshes.Cube(), NightMeshes.Pool(), false, Vector3.zero, Quaternion.identity, Vector3.one);
                f.pool = pg.GetComponent<Renderer>(); f.pool.enabled = false;
                fires.Add(f);
            }
            campSrc = new AudioSource[4]; torchSrc = new AudioSource[4]; campFire = new Fire[4]; torchFire = new Fire[4];
            for (int i = 0; i < 4; i++) { campSrc[i] = Loop("L16_FireLoop_camp" + i, ResLoopCamp, 4f, 30f); torchSrc[i] = Loop("L16_FireLoop_torch" + i, ResLoopTorch, 2.5f, 16f); }
            Debug.Log($"[OpenZ] L16 fires: {fires.Count} of {doc.fires.Length} fires ({missing} without their model in the scene), at most {doc.maxReal} real lights at once, " +
                      $"loop clips {(campSrc[0] ? "campfire ok" : "campfire MISSING")} / {(torchSrc[0] ? "torch ok" : "torch MISSING")}");
        }

        void Start()
        {
            dayNight = FindFirstObjectByType<DayNight>(); cam = Camera.main; rts = cam ? cam.GetComponent<RtsCamera>() : null; if (!rts) rts = FindFirstObjectByType<RtsCamera>();
            var g = ZGame.I;
            foreach (var f in fires)
            {   // the painted ground pool of a fire that holds no real light: on the ground under its light
                float gy = g ? g.GroundY(f.lightAt.x, f.lightAt.z) : f.flame.y; float h = Mathf.Max(0.8f, f.lightAt.y - gy), w = Mathf.Clamp(f.k.range * 0.9f, 1.2f, f.k.range * 1.3f);
                f.pool.transform.SetPositionAndRotation(new Vector3(f.lightAt.x, gy + 0.35f, f.lightAt.z), Quaternion.identity);
                f.pool.transform.localScale = new Vector3(w, 3.8f, w); f.poolE = f.k.intensity / (h * h);
            }
        }

        void OnDestroy()
        {
            foreach (var f in fires) if (f.fix != null) NightBudgetShare.Fixtures.Remove(f.fix);
            if (I == this) I = null;
        }

        static Vector3 V(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
        static float Hash(string s) { unchecked { uint h = 2166136261u; foreach (char c in s) { h ^= c; h *= 16777619u; } return (h & 0xFFFFFF) / 16777215f; } }
        static float Bus() => CombatAudio.I ? CombatAudio.I.volume * CombatAudio.BusGain(CombatAudio.Bus.World) : 0.55f;

        AudioSource Loop(string name, string res, float min, float max)
        {
            var c = DetailKit.Pool3D.Clip(res); if (!c) return null;
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>(); s.clip = c; s.loop = true; s.playOnAwake = false; s.spatialBlend = 1f; s.dopplerLevel = 0f;
            s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = min; s.maxDistance = max; s.volume = 0f;
            return s;
        }

        /// <summary>The fire with this id (C1, B3, W2, P4), or null.</summary>
        public Fire Find(string id) { foreach (var f in fires) if (f.row.id == id) return f; return null; }

        /// <summary>A fire goes out for good (its bowl fell with the pyramid's top): the dying hiss plays, it does not catch again.</summary>
        public void Extinguish(string id, string why)
        {
            var f = Find(id); if (f == null || f.dead) return;
            f.dead = true;
            Debug.Log($"[OpenZ] L16 fire {id} is out for good ({why})");
        }

        float Flicker(Fire f, float now)
        {
            if (OzMotion.Reduced) return 1f - f.k.flicker * 0.5f;                                 // calm screen: steady
            float n = Mathf.PerlinNoise(f.seed * 37.1f, now * f.k.speed), breath = Mathf.PerlinNoise(f.seed * 11.3f + 5f, now * 0.4f);
            return (1f - f.k.flicker + f.k.flicker * n) * (0.97f + 0.06f * breath);
        }

        void Update()
        {
            if (doc == null) return;
            float t = dayNight ? dayNight.timeOfDay : NightGrade.TimeOfDay, night = NightGrade.Night(t), dusk = Mathf.Lerp(0.25f, 1f, night);
            float dt = Time.deltaTime, now = Time.time;
            if (!cam) { cam = Camera.main; if (cam && !rts) rts = cam.GetComponent<RtsCamera>(); }
            var nl = NightLights.I; bool hook = NightBudgetShare.HookFrame >= Time.frameCount - 2;
            float gain = (nl ? nl.lightGain : 0.6f) * fireLightGain;
            bool bundle = !rts || rts.height <= bundleBelowHeight, tongues = OpenZPyroFire.TonguesReady;
            var fxp = OpenZFxPlayer.I; bool fx = fxp && fxp.isActiveAndEnabled && dt > 0f;
            int lit = 0, burning = 0, real = 0, drawn = 0;
            for (int i = 0; i < fires.Count; i++)
            {
                var f = fires[i];
                bool want = Enabled && !f.dead && NightGrade.LightsOn(t, f.row.onAt, f.row.offAt);
                float prev = f.level;
                f.level = want ? Mathf.MoveTowards(f.level, 1f, dt / Mathf.Max(0.05f, doc.catchSeconds)) : Mathf.MoveTowards(f.level, 0f, dt / Mathf.Max(0.05f, doc.dieSeconds));
                f.visible = !cam || DetailKit.OnScreen(cam, f.flame, 0.2f);
                if (want && !f.want && prev <= 0.001f) Catch(f);
                if (!want && f.want && prev > 0.2f) Dying(f, now);
                f.want = want;
                float flick = Flicker(f, now), o = f.level * flick;
                f.fix.LightOut = o;
                if (f.level > 0.5f) lit++;
                if (f.level > 0.02f) burning++;
                bool showLit = f.level > 0.02f;
                if (showLit != f.shownLit)
                {
                    f.shownLit = showLit;
                    if (f.litGo) f.litGo.SetActive(showLit);
                    if (f.outGo && f.row.kind != "bowl") f.outGo.SetActive(!showLit);             // a bowl is one shape: the coals come on top of it
                }
                // real light: granted by the night budget AND inside the cap of the fires
                f.capK = Mathf.MoveTowards(f.capK, f.capOn ? 1f : 0f, dt / 0.35f);
                float rk = (hook ? f.fix.RealK : 1f) * f.capK; f.real = rk;
                float k = o * rk; bool on = k > 0.002f;
                if (f.lamp.enabled != on) f.lamp.enabled = on;
                if (on)
                {
                    real++;
                    f.lamp.intensity = f.k.intensity * k * gain; f.lamp.range = f.k.range * (0.98f + 0.04f * flick);
                    if (!OzMotion.Reduced) f.lamp.transform.position = f.lightAt + new Vector3(Mathf.PerlinNoise(f.seed * 3f, now * 2.1f) - 0.5f, 0f, Mathf.PerlinNoise(f.seed * 7f, now * 1.7f) - 0.5f) * 0.16f;
                }
                SetColor(f.halo, f.colLin * ((nl && !nl.halos ? 0f : (nl ? nl.haloGain : 0.35f)) * haloK * dusk * o), ref f.haloShown);
                float pk = (nl && !nl.pools ? 0f : (nl ? nl.poolGain : 0.22f)) * poolK * o * dusk * (1f - rk) * Mathf.Min(4f, f.poolE * 4f);
                SetColor(f.pool, f.colLin * pk, ref f.poolShown);
                if (f.level <= 0.01f && now >= f.wispUntil) continue;
                if (!f.visible) continue;
                // flame: the ground fire's tongues, a bundle near the camera, one tongue from high up
                if (f.level > 0.01f && tongues)
                {
                    int n = bundle ? Mathf.Max(1, f.k.tongues) : 1; float r = 0.22f * f.k.flame_w * f.scale;
                    for (int j = 0; j < n; j++)
                    {
                        float a = f.seed * 40f + j * 2.4f, s = j == 0 ? 1f : 0.85f - 0.07f * j;
                        var p = j == 0 ? f.flame : f.flame + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                        OpenZPyroFire.Tongue(p, Mathf.Repeat(f.seed + 0.37f * j, 1f), s, 0.6f + 0.15f * j, f.level, f.k.flame_h * f.scale * s, f.k.flame_w * f.scale);
                        drawn++;
                    }
                }
                // smoke: a thin column over a campfire or bowl while it burns, a pale wisp over a campfire after dawn
                if (fx && f.big && now >= f.nextSmoke)
                {
                    bool wisp = f.level <= 0.01f;
                    if (wisp && !f.campfire) continue;
                    f.nextSmoke = now + (wisp ? 1.6f : f.campfire ? 0.5f : 1.2f);
                    float ph = Mathf.Sin(f.seed * 6.28f + now * 3.7f);
                    fxp.EmitChannel("leg_smoke", f.smoke + new Vector3(ph * 0.12f, 0f, Mathf.Cos(f.seed * 9f + now * 3.1f) * 0.12f),
                        new Vector3(0.18f + ph * 0.1f, 1.1f + 0.4f * UnityEngine.Random.value, 0.1f), wisp ? 2.2f : 2.8f + 0.6f * UnityEngine.Random.value,
                        (wisp ? 0.7f : f.campfire ? 1.2f : 0.8f) * f.scale * (0.85f + 0.3f * UnityEngine.Random.value),
                        wisp ? new Color(0.74f, 0.74f, 0.7f) : OpenZFxCatalog.WreckSmokeGrey, wisp ? 0.22f : (f.campfire ? 0.5f : 0.4f) * f.level);
                }
            }
            LitNow = lit; BurningNow = burning; RealNow = real; TonguesDrawn = drawn; if (real > MaxRealSeen) MaxRealSeen = real;
            if (now >= nextCap) { nextCap = now + 0.2f; Cap(hook); }
            if (now >= nextLoops) { nextLoops = now + 0.5f; Loops(); }
            float bus = Bus();
            for (int i = 0; i < 4; i++)
            {
                if (campSrc[i] && campFire[i] != null) campSrc[i].volume = campLoopVol * (campFire[i].campfire ? 1f : 0.85f) * campFire[i].level * bus;
                if (torchSrc[i] && torchFire[i] != null) torchSrc[i].volume = torchLoopVol * torchFire[i].level * bus;
            }
        }

        void SetColor(Renderer r, Color c, ref float shown)
        {
            if (!r) return;
            float key = c.r + c.g * 1.7f + c.b * 0.6f;
            if (shown >= 0f && Mathf.Abs(key - shown) <= Mathf.Max(0.004f, shown * 0.012f)) return;
            shown = key; bool on = key > 0.0015f;
            if (r.enabled != on) r.enabled = on;
            if (!on) return;
            mpb.Clear(); mpb.SetColor(NightMeshes.ColorId, c); r.SetPropertyBlock(mpb);
        }

        /// <summary>A fire that holds a real light keeps it until another fire's score leads by this factor.</summary>
        const float CapHold = 1.25f;

        /// <summary>Which burning fires carry a real light: never more than maxReal at one moment, the lights that are still fading
        /// out counted in. Ranked by a score of the fires' own WITHOUT the flicker ((1 + priority) x sqrt(intensity) x how far the
        /// fire has caught, falling off with the distance from the camera focus: the night budget's formula with the flicker taken
        /// out), so two fires of nearly the same score do not trade places with every flicker. A fire that holds a light keeps it
        /// until a challenger leads by 25 %; the fire that wins a place waits until the light it replaces is dark. With the night
        /// package only fires the night budget grants can hold one; without it only fires on screen.</summary>
        void Cap(bool hook)
        {
            rank.Clear();
            Vector3 focus = rts ? rts.focus : cam ? cam.transform.position : Vector3.zero;
            foreach (var f in fires)
            {
                bool can = f.level > 0.02f && (hook ? f.fix.Budgeted : f.visible);
                if (!can) { f.capOn = false; continue; }                                  // its light fades (it still counts until it is dark)
                float dx = f.lightAt.x - focus.x, dz = f.lightAt.z - focus.z;
                f.capScore = (1f + f.k.priority) * Mathf.Sqrt(f.k.intensity) * f.level / (1f + (dx * dx + dz * dz) / 1600f);
                rank.Add(f);
            }
            rank.Sort((a, b) => { int c = (b.capScore * (b.capOn ? CapHold : 1f)).CompareTo(a.capScore * (a.capOn ? CapHold : 1f)); return c != 0 ? c : a.ord.CompareTo(b.ord); });
            int max = Mathf.Max(0, doc.maxReal);
            for (int i = max; i < rank.Count; i++) rank[i].capOn = false;                 // out-ranked by a clear lead: its light fades
            int still = 0;
            foreach (var f in fires) if (f.capOn || f.capK > 0f) still++;                 // lights that are on or not yet dark
            for (int i = 0; i < rank.Count && i < max; i++)
            {
                var f = rank[i];
                if (f.capOn) continue;
                if (f.capK > 0f) f.capOn = true;                                          // still lit from its last turn: it is counted already
                else if (still < max) { f.capOn = true; still++; }                        // a new light only while fewer than maxReal are on
            }
        }

        // ------------------------------------------------------------------ sound
        Vector3 Listener()
        {
            var ca = CombatAudio.I; if (ca && ca.ListenerRig) return ca.ListenerRig.position;
            return rts ? rts.focus : cam ? cam.transform.position : Vector3.zero;
        }

        bool Hear(Fire f) { var l = Listener(); float dx = f.flame.x - l.x, dz = f.flame.z - l.z; return f.visible && dx * dx + dz * dz <= hearRange * hearRange; }

        static bool Voice(float[] times, float now, float window)
        {
            int oldest = 0;
            for (int i = 0; i < times.Length; i++) { if (now - times[i] >= window || times[i] <= 0f) { times[i] = now; return true; } if (times[i] < times[oldest]) oldest = i; }
            return false;
        }

        void Catch(Fire f)
        {
            string ev = f.big ? "amb_fire_lightup_big" : "amb_fire_lightup_torch", res = f.big ? ResUpBig : ResUpTorch;
            var clip = DetailKit.Pool3D.Clip(res); float now = Time.time;
            string outcome = !clip ? "DROP clip missing" : !Hear(f) ? "SKIP out of view or earshot (" + f.row.id + ")" : !Voice(upTimes, now, 0.6f) ? "SKIP three are catching at once (" + f.row.id + ")" : "PLAY World (" + f.row.id + " catches)";
            if (outcome.StartsWith("PLAY")) DetailKit.Pool3D.Play(res, f.flame, upVol, 0.96f + 0.08f * f.seed);
            OzSoundLog.Add("L16", ev, clip ? clip.name : null, outcome, upVol, f.flame);
            if (f.visible) { if (FxManager.I) FxManager.I.Pop(f.flame + Vector3.up * 0.15f, f.big ? 0.35f : 0.2f); }   // the spark burst of catching
        }

        void Dying(Fire f, float now)
        {
            var clip = DetailKit.Pool3D.Clip(ResDying);
            string outcome = !clip ? "DROP clip missing" : !Hear(f) ? "SKIP out of view or earshot (" + f.row.id + ")" : !Voice(dyTimes, now, 1.2f) ? "SKIP two are dying at once (" + f.row.id + ")" : "PLAY World (" + f.row.id + " goes out)";
            if (outcome.StartsWith("PLAY")) DetailKit.Pool3D.Play(ResDying, f.flame, dyingVol, 0.95f + 0.1f * f.seed);
            OzSoundLog.Add("L16", "amb_fire_dying", clip ? clip.name : null, outcome, dyingVol, f.flame);
            if (f.campfire) f.wispUntil = now + doc.dieSeconds + 40f;
        }

        /// <summary>The four burning campfires / bowls and the four burning torches nearest the listener get the loop voices.</summary>
        void Loops()
        {
            Assign(campSrc, campFire, true, "amb_fire_campfire_loop");
            Assign(torchSrc, torchFire, false, "amb_fire_torch_loop");
        }

        void Assign(AudioSource[] src, Fire[] held, bool big, string ev)
        {
            var l = Listener(); rank.Clear();
            foreach (var f in fires) if (f.big == big && f.level > 0.05f && Hear(f)) rank.Add(f);
            rank.Sort((a, b) => (a.flame - l).sqrMagnitude.CompareTo((b.flame - l).sqrMagnitude));
            if (rank.Count > src.Length) rank.RemoveRange(src.Length, rank.Count - src.Length);
            for (int i = 0; i < src.Length; i++)
            {   // a voice whose fire went out or left the nearest four is free again
                if (held[i] != null && !rank.Contains(held[i]))
                {
                    OzSoundLog.Add("L16", ev, null, "LOOP STOP (" + held[i].row.id + ")", 0f, held[i].flame);
                    held[i] = null; if (src[i]) src[i].Stop();
                }
            }
            foreach (var f in rank)
            {
                if (Array.IndexOf(held, f) >= 0) continue;
                int free = Array.IndexOf(held, null); if (free < 0) break;
                held[free] = f;
                var s = src[free];
                if (s) { s.transform.position = f.flame; s.volume = 0f; s.time = UnityEngine.Random.Range(0f, Mathf.Max(0f, s.clip.length - 0.1f)); s.Play(); }
                OzSoundLog.Add("L16", ev, s ? s.clip.name : null, s ? "LOOP World (" + f.row.id + ")" : "DROP clip missing", big ? campLoopVol : torchLoopVol, f.flame);
            }
        }

        /// <summary>Play check, one line.</summary>
        public static string Readout()
        {
            if (!I) return "L16 fires: not in this level";
            var sb = new System.Text.StringBuilder();
            sb.Append("L16 fires: ").Append(I.fires.Count).Append(" fires, lit ").Append(LitNow).Append(", burning ").Append(BurningNow).Append(", real lights ").Append(RealNow)
              .Append(" (cap ").Append(I.MaxReal).Append(", most seen ").Append(MaxRealSeen).Append("), tongues drawn ").Append(TonguesDrawn).Append(", night budget hook ")
              .Append(NightBudgetShare.HookFrame >= Time.frameCount - 2 ? "on" : "OFF");
            foreach (var f in I.fires) if (f.dead) sb.Append(" | ").Append(f.row.id).Append(" out for good");
            return sb.ToString();
        }
    }
}
