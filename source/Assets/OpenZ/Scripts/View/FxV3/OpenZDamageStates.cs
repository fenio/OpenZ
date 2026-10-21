using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// <list type="bullet">
    /// <item><b>ZF7 battered / burning vehicles and cannons:</b> at half health the hull darkens (TeamPaint.Charred) and trails grey
    /// smoke with a metal clank; at a quarter it catches fire (dst_fire_ignite + a following dst_fire_loop), dark smoke, flames,
    /// rising embers, a heat plume and an oil stain under it when it stands still. A repair that lifts it past 40 % puts the fire
    /// out: steam hiss + OpenZVoiceV3.SetBurning(false, repaired) = "Fire extinguished!" for the player's units.</item>
    /// <item><b>ZF24 buildings:</b> at half health they smoke, at a quarter they burn on the roof (fire loop, plume); a destroyed
    /// fort keeps a slow pulsing orange glow (a point light breathing over ~3 s, embers, a quiet crackle loop).</item>
    /// <item><b>ZF1 hit flash:</b> a unit that loses health flashes warm white for 0.09 s (TeamLit _EmissionColor through its
    /// </item>
    /// <item><b>HX1 heat shimmer:</b> every OpenZFxPlayer blast gets an OpenZ/HeatDistort mode-0 shimmer that swells and fades;
    /// every fire above (units, buildings, the fort ruin, fresh vehicle wrecks) a mode-2 rising plume. Nearest first, capped.</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(70)]
    public partial class OpenZDamageStates : MonoBehaviour                                                // K393: the shuttle's points and trail in OpenZDamageStates.Shuttle.cs
    {
        public static OpenZDamageStates I { get; private set; }
        public static bool Enabled = true;
        const string PrefFlash = "oz_hit_flash", PrefFlashMigrated = "oz_hit_flash_default_off_v1";
        static int flashPref = -1;
        /// <summary></summary>
        public static bool CaptureNoFlash;
        /// <summary>
        /// migration sets existing players to off once, after that their own choice stays.</summary>
        public static bool HitFlash
        {
            get
            {
                if (flashPref < 0)
                {
                    if (PlayerPrefs.GetInt(PrefFlashMigrated, 0) == 0)
                    {
                        PlayerPrefs.SetInt(PrefFlash, 0); PlayerPrefs.SetInt(PrefFlashMigrated, 1); PlayerPrefs.Save();
                        Debug.Log("[DamageStates] hit flash: default is now OFF (one-time settings migration)");
                    }
                    flashPref = PlayerPrefs.GetInt(PrefFlash, 0);
                }
                return flashPref == 1;
            }
            set { flashPref = value ? 1 : 0; PlayerPrefs.SetInt(PrefFlash, flashPref); PlayerPrefs.Save(); Debug.Log("[DamageStates] hit flash " + (value ? "ON" : "OFF")); }
        }
        public static float SmokeAt = 0.5f, BurnAt = 0.25f, OutAt = 0.4f, BatteredCharred = 0.12f, BurningCharred = 0.19f;
        public static float FlashTime = 0.09f, FlashGap = 0.12f;
        public static Color FlashColor = new Color(0.95f, 0.8f, 0.62f);
        public static int MaxBlastShimmers = 6, MaxPlumes = 10, MaxFireLoops = 4;
        public static float ShimmerStrength = 0.012f, RuinGlowPeriod = 3.2f;
        /// <summary>Proof counters (Play check log).</summary>
        public static int TrackSparks, TrackGrinds, Ignited, Extinguished, Flashes, Battered, BuildingSmoke, BuildingFires, BlastShimmers, PeakShimmers, PeakPlumes, RuinGlows, OilStains;

        ZGame game; Camera cam;
        sealed class U
        {
            public int lastHp = -1, stage; public float nextFx, flashUntil = -1f, lastFlash = -10f, nextStain, still; public bool flashing, loop;
            public Renderer[] rends; public readonly List<(Renderer r, int i, Color prev)> saved = new List<(Renderer, int, Color)>();
            public Vector3 lastPos, deck; public float deadAt = -1f, h = -1f, nextSticky, nextTrack, nextGrind, nextUnchar; public bool sticky, deckDone; public TeamPaint tp; public List<GameObject> gear; public float nextGear;
        }
        sealed class B { public int stage; public float nextFx, nextSticky; public bool sticky; public bool loop, ruin; public Bounds bounds; public bool hasBounds; public Light glow; public Vector3 firePt; public bool hasFirePt; }
        readonly Dictionary<int, U> units = new Dictionary<int, U>();
        readonly Dictionary<int, B> blds = new Dictionary<int, B>();
        readonly List<int> gone = new List<int>();
        MaterialPropertyBlock mpb, shimBlock; Material blastMat, plumeMat; Mesh quad; int loopsOn;
        static readonly int EmId = Shader.PropertyToID("_EmissionColor"), AlphaId = Shader.PropertyToID("_Alpha");
        struct Blast { public Vector3 at; public float size, t0, life; }
        readonly List<Blast> blasts = new List<Blast>();
        struct Plume { public Vector3 at; public float size, alpha, d2; }
        readonly List<Plume> plumes = new List<Plume>(24);

        // ------------------------------------------------------------------------------------------------ install
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded;
            Ignited = Extinguished = Flashes = Battered = BuildingSmoke = BuildingFires = BlastShimmers = PeakShimmers = PeakPlumes = RuinGlows = OilStains = 0;
            flashPref = -1;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void BootFirstScene() => OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        static void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            if (m != LoadSceneMode.Single) return;
            var g = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            if (g && !g.GetComponent<OpenZDamageStates>()) g.gameObject.AddComponent<OpenZDamageStates>();
        }

        void Awake()
        {
            if (I && I != this) { Destroy(this); return; }
            I = this;
            game = GetComponent<ZGame>();
            mpb = new MaterialPropertyBlock(); shimBlock = new MaterialPropertyBlock();
            var hd = Resources.Load<Material>("OpenZ/FX/M_HeatDistort");
            if (hd)
            {
                blastMat = new Material(hd) { name = "BlastShimmer (runtime)" };
                blastMat.SetFloat("_Mode", 0f); blastMat.SetFloat("_Inner", 0.12f); blastMat.SetFloat("_NoiseScale", 5f); blastMat.SetFloat("_Speed", 3.4f); blastMat.SetFloat("_Strength", ShimmerStrength);
                plumeMat = new Material(hd) { name = "DamagePlume (runtime)" };
                plumeMat.SetFloat("_Mode", 2f); plumeMat.SetFloat("_Inner", 0.1f); plumeMat.SetFloat("_NoiseScale", 8f); plumeMat.SetFloat("_Speed", 3.0f);   plumeMat.SetFloat("_Strength", ShimmerStrength);
            }
            quad = BuildQuad();
        }
        void OnEnable() { OpenZFxPlayer.Exploded -= OnBlast; OpenZFxPlayer.Exploded += OnBlast; }
        void OnDisable()
        {
            OpenZFxPlayer.Exploded -= OnBlast;
            foreach (var u in units.Values) if (u.flashing) Unflash(u);
        }
        void OnDestroy() { if (I == this) I = null; }

        // ------------------------------------------------------------------------------------------------ HX1 blasts
        void OnBlast(Vector3 at, float s)
        {
            if (!Enabled || !blastMat) return;
            if (blasts.Count >= 24) blasts.RemoveAt(0);
            blasts.Add(new Blast { at = at, size = 2.2f + Mathf.Clamp(s, 0.2f, 4f) * 2.4f, t0 = Time.time, life = 0.8f + Mathf.Clamp(s, 0.2f, 4f) * 0.25f });
            BlastShimmers++;
        }

        // ------------------------------------------------------------------------------------------------ tick
        void Update()
        {
            if (!Enabled || game == null || game.World == null) return;
            if (!cam || !cam.isActiveAndEnabled) cam = Camera.main;
            float now = Time.time;
            TickUnits(now);
            TickBuildings(now);
            DrawShimmer(now);
        }

        void TickUnits(float now)
        {
            var fx = OpenZFxPlayer.I; bool fxOn = fx && fx.isActiveAndEnabled;
            gone.Clear();
            foreach (var kv in units) if (!game.Views.ContainsKey(kv.Key)) gone.Add(kv.Key);
            foreach (var id in gone) { var u = units[id]; if (u.loop) StopFire(id, u); units.Remove(id); }
            foreach (var kv in game.Views)
            {
                var v = kv.Value; if (!v) continue;
                var o = v.Obj; if (o == null || !o.IsUnit || o.MaxHealth <= 0) continue;
                if (!units.TryGetValue(kv.Key, out var u)) { u = new U { lastHp = o.Health, lastPos = v.transform.position }; units[kv.Key] = u; }
                if (u.flashing && now >= u.flashUntil) Unflash(u);
                bool dead = o.Destroyed || v.Dying || o.KillAt >= 0;
                if (dead)
                {
                    if (u.deadAt < 0f) { u.deadAt = now; if (u.loop) StopFire(kv.Key, u); if (u.stage >= 2) OpenZVoiceV3.SetBurning(o, false); u.stage = 0; u.sticky = false; }
                    if (o.Kind != ZKind.Robot && now - u.deadAt < 12f && v.gameObject.activeInHierarchy && !ZodBriefing.EndingHides(v)) AddPlume(v.transform.position + Vector3.up * 0.9f, 2.2f, Mathf.Clamp01((12f - (now - u.deadAt)) / 4f));   // HX1: the fresh wreck fire
                    continue;
                }
                // ZF1 hit flash: any health drop on a drawn unit
                if (o.Health < u.lastHp && HitFlash && !OzMotion.Reduced && !CaptureNoFlash && now - u.lastFlash >= FlashGap && v.gameObject.activeInHierarchy) Flash(v, u, now);
                u.lastHp = o.Health;
                if (o.Kind == ZKind.Robot) { if (now >= u.nextUnchar) UncharLiving(v, u, now); continue; }   // a living robot's flame char fades like a living vehicle's: clean paint again once the fire on it has ended (robots have no battered stage: stage 0)
                if (now >= u.nextGear) KeepGear(v, o, u, now); if (o.IsShuttle && ShipDefer(kv.Key)) continue;   /* K413: a living shuttle's step after its drawn pose (OpenZDamageStates.ShipLate.cs) */
                float k = o.Health / (float)o.MaxHealth;
                var pos = v.transform.position;
                float moved = (pos - u.lastPos).sqrMagnitude; u.lastPos = pos;
                u.still = moved < 0.0004f ? u.still + Time.deltaTime : 0f;
                // ZF7 stages: 0 fine, 1 battered + smoke, 2 burning; repair (health back over OutAt) puts the fire out
                int want = k <= BurnAt ? 2 : k <= SmokeAt ? 1 : 0;
                if (u.stage == 2 && k <= OutAt) want = 2;                                                   // hysteresis: stays lit until repaired past 40 %
                if (want != u.stage) SetUnitStage(kv.Key, v, o, u, want, pos);
                if (now >= u.nextUnchar) UncharLiving(v, u, now);
                if (u.stage == 0 || !v.gameObject.activeInHierarchy || ZodBriefing.EndingHides(v)) continue;   // version 1.1: not in the level 16 ending's picture (ZodBriefing.EndingEnemies.cs): no smoke, sparks or fire
                if (o.IsShuttle) { ShipTick(kv.Key, v, o, u, now, fx, fxOn); continue; }                  // K393: the same smoke / fire at the shuttle's own points on its top side, trailing in flight (OpenZDamageStates.Shuttle.cs)
                if (u.still == 0f && moved > 0.0004f && fxOn && now >= u.nextTrack && TrackedHull(o.Type) && WreckFx.OnScreen(pos, 0.1f))
                {   // ZF7 track sparks: a battered / burning tank's worn tracks throw sparks while it drives, with a metal grind now and then
                    u.nextTrack = now + Random.Range(0.22f, 0.4f) / u.stage; TrackSparks++;
                    var tf = v.transform; float half = o.Type == "heavy" ? 1.35f : 1.1f;
                    var at = pos + tf.right * (Random.value < 0.5f ? -half : half) + tf.forward * Random.Range(-1.1f, 1.1f) + Vector3.up * 0.25f;
                    for (int i = 0; i < 3; i++)
                        fx.EmitChannel("leg_spark", at + Random.insideUnitSphere * 0.1f, -tf.forward * Random.Range(0.6f, 1.6f) + Vector3.up * Random.Range(0.8f, 1.8f) + Random.insideUnitSphere * 0.4f,
                            Random.Range(0.25f, 0.45f), Random.Range(0.04f, 0.07f), new Color(1f, 0.78f, 0.4f), 1f);
                    if (now >= u.nextGrind)
                    {
                        u.nextGrind = now + Random.Range(1.6f, 2.8f); TrackGrinds++;
                        AudioDirector.Fire(AudioDirector.Has("veh_tracks_squeal") ? "veh_tracks_squeal" : "dst_debris_metal", at, o.Id, 0.35f, Random.Range(1.05f, 1.25f));
                    }
                }
                var top = pos + Vector3.up * TopOf(v, u);
                var deck = v.transform.TransformPoint(DeckOf(v, u));
                bool onScreen = WreckFx.OnScreen(top, 0.1f);
                if (u.stage == 2 && now >= u.nextSticky)
                {
                    // heat plume, no char), re-lit every 2 s; the full body fire only while a real pyro fire burns it (then back to small)
                    u.nextSticky = now + 2f;
                    u.sticky = OpenZPyroFire.KeepBurningSmall(o, v, v.gameObject, false, deck);
                }
                if (u.stage == 2 && !u.sticky) AddPlume(deck, 1.8f, 1f);
                if (!onScreen || !fxOn || now < u.nextFx) continue;
                if (u.stage == 1)
                {
                    u.nextFx = now + Random.Range(0.3f, 0.45f);
                    fx.WreckSmoke(top, 1.1f, false);
                }
                else
                {
                    u.nextFx = now + 0.3f;
                    if (!u.sticky) fx.WreckSmoke(deck, 1.2f, true);                                      // the small fire makes its own thin dark smoke
                    if (Random.value < 0.6f)
                        fx.EmitChannel("leg_spark", deck + Random.insideUnitSphere * 0.2f, new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(1.2f, 2.2f), Random.Range(-0.3f, 0.3f)),
                            Random.Range(0.6f, 1.1f), Random.Range(0.05f, 0.08f), new Color(1f, 0.55f, 0.18f), 1f);   // a few embers
                    if (u.still > 1.5f && now >= u.nextStain)
                    {   // oil / burn stain under a burning hull that stands still (at most one per 10 s)
                        u.nextStain = now + 10f; OilStains++;
                        fx.Scorch(new Vector3(pos.x, game.GroundY(pos.x, pos.z), pos.z), 0.28f, 1);
                    }
                }
            }
        }

        /// <summary></summary>
        static Vector3 DeckOf(UnitView v, U u)
        {
            if (u.deckDone) return u.deck;
            u.deckDone = true;
            if (ShipFireLocal(v, u)) return u.deck;                                                    // K393: the shuttle's small fire on its pod's top (2.4 m was inside its hull)
            float h = TopOf(v, u) / 0.85f, half = 0.6f;
            bool any = false; var bb = new Bounds();
            foreach (var r in v.GetComponentsInChildren<Renderer>())
                if (r is MeshRenderer || r is SkinnedMeshRenderer) { if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds); }
            if (any) half = Mathf.Max(bb.extents.x, bb.extents.z);
            // is not the nose), so the engine deck is +z; the side opposite the headlight when there is one
            float rear = 1f;
            foreach (var l in v.GetComponentsInChildren<Light>(true)) if (l.name.StartsWith("VL_Headlight")) { rear = v.transform.InverseTransformPoint(l.transform.position).z > 0f ? -1f : 1f; break; }
            u.deck = new Vector3(0f, Mathf.Clamp(h * 0.6f, 0.6f, 2.4f), rear * Mathf.Clamp(half * 0.5f, 0.25f, 1.3f));
            return u.deck;
        }

        /// <summary>
        /// damage stage's light battering (the charred look stays for wrecks).</summary>
        void UncharLiving(UnitView v, U u, float now)
        {
            u.nextUnchar = now + 0.25f;
            if (!u.tp) u.tp = v.GetComponent<TeamPaint>();
            if (!u.tp) { u.nextUnchar = now + 5f; return; }
            float want = u.stage == 2 ? BurningCharred : u.stage == 1 ? BatteredCharred : 0f;
            if (u.tp.Charred > want + 0.01f && !OpenZPyroFire.FullBurning(v.Obj.Id)) u.tp.Charred = Mathf.Max(want, u.tp.Charred - 0.03f);   // ~5 s back from pyro-black
        }

        /// <summary>
        /// vehicle / cannon never loses its main gun, barrel, turret, launcher rack or pedestal (heavy hits chip only small parts;
        /// the gun goes only with the wreck). The gear seen at first sight is checked once a second; anything hidden is shown again
        /// and logged with its name (GearRestored), anything torn out of the model is logged (GearLost).</summary>
        void KeepGear(UnitView v, ZObject o, U u, float now)
        {
            u.nextGear = now + 1f;
            if (u.gear == null)
            {
                u.gear = new List<GameObject>();
                foreach (var r in v.GetComponentsInChildren<MeshRenderer>())
                    if (r.enabled && r.gameObject.activeInHierarchy && GearName.IsMatch(r.name) && !NotGear.IsMatch(r.name)) u.gear.Add(r.gameObject);
                return;
            }
            foreach (var g in u.gear)
            {
                if (!g) continue;
                if (!g.transform.IsChildOf(v.transform)) { if (GearLost++ < 20) Debug.LogWarning($"[DamageStates] gear LOST from a living {o.Type} #{o.Id}: {g.name} (hp {o.Health}/{o.MaxHealth}, owner {o.Owner})"); continue; }
                var r = g.GetComponent<MeshRenderer>();
                if (!g.activeSelf || (r && !r.enabled))
                {
                    var why = !g.activeSelf ? "inactive" : "renderer off";
                    g.SetActive(true); if (r) r.enabled = true;
                    if (GearRestored++ < 40) Debug.LogWarning($"[DamageStates] gear restored on a living {o.Type} #{o.Id}: {g.name} was {why} (hp {o.Health}/{o.MaxHealth}, owner {o.Owner}, knock-offs {RobotPopDeath.KnockOffs})");
                }
            }
        }
        static readonly System.Text.RegularExpressions.Regex GearName = new System.Text.RegularExpressions.Regex("barrel|turret|gun|cannon|rack|launcher|pedestal|recoil|weapon|pod", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        static readonly System.Text.RegularExpressions.Regex NotGear = new System.Text.RegularExpressions.Regex("rocket|missile|muzzle|flash|glow|shadow|label|headlight|spotlight|cone|lamp|seat|chair|crew|hatch|ladder|rung|rail|OZ_|frag", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        /// <summary></summary>
        public static int GearRestored, GearLost;

        static bool TrackedHull(string t) => t == "light" || t == "medium" || t == "heavy" || t == "missile_launcher";

        void SetUnitStage(int id, UnitView v, ZObject o, U u, int want, Vector3 pos)
        {
            int was = u.stage; u.stage = want;
            var tp = v.GetComponent<TeamPaint>();
            float charred = want == 2 ? BurningCharred : want == 1 ? BatteredCharred : 0f;
            if (tp && Mathf.Abs(tp.Charred - charred) > 0.01f) tp.Charred = charred;
            var top = pos + Vector3.up * TopOf(v, u);
            if (want >= 1 && was == 0) { Battered++; AudioDirector.Fire("dst_debris_metal", top, id, 0.45f, 1.15f); }   // the hull gives: a metal clank
            if (want == 2 && was < 2)
            {
                Ignited++;
                OzSoundV3.Ignite("dmg_" + id, top, 0.55f);
                if (AudioDirector.I && loopsOn < MaxFireLoops && AudioDirector.Has("dst_fire_loop")) { AudioDirector.I.StartLoop("dmgfire:" + id, "dst_fire_loop", top, v.transform, 0.5f); u.loop = true; loopsOn++; }
                OpenZVoiceV3.SetBurning(o, true);
                Debug.Log($"[DamageStates] {o.Type} #{id} caught fire at {o.Health}/{o.MaxHealth}");
            }
            else if (was == 2 && want < 2)
            {   // repaired past OutAt: the fire goes out
                Extinguished++;
                if (u.loop) StopFire(id, u);
                if (u.sticky) { OpenZPyroFire.StopBurning(id); u.sticky = false; }
                AudioDirector.Fire("dtl_steam_hiss", top, id, 1.3f, 1f);
                var fx = OpenZFxPlayer.I;
                if (fx && fx.isActiveAndEnabled) for (int i = 0; i < 4; i++) fx.WreckSmoke(top + Random.insideUnitSphere * 0.4f, 1.6f, false);   // a last white-grey puff
                OpenZVoiceV3.SetBurning(o, false, true);
                Debug.Log($"[DamageStates] {o.Type} #{id} fire out (repaired to {o.Health}/{o.MaxHealth})");
            }
        }

        /// <summary>Fire / smoke height: 85 % of the drawn model's height (renderer bounds, measured once).</summary>
        static float TopOf(UnitView v, U u)
        {
            if (u.h > 0f) return u.h;
            if (ShipTop(v, u)) return u.h;                                                             // K393: the shuttle's real top (the 4 m cap was inside its 5.1 m hull)
            bool any = false; float top = 0f;
            foreach (var r in v.GetComponentsInChildren<Renderer>())
                if (r is MeshRenderer || r is SkinnedMeshRenderer) { top = any ? Mathf.Max(top, r.bounds.max.y) : r.bounds.max.y; any = true; }
            u.h = any ? Mathf.Clamp((top - v.transform.position.y) * 0.85f, 0.8f, 4f) : 1.25f;
            return u.h;
        }

        void StopFire(int id, U u)
        {
            if (AudioDirector.I) AudioDirector.I.StopLoop("dmgfire:" + id, 0.8f);
            u.loop = false; loopsOn = Mathf.Max(0, loopsOn - 1);
        }

        // ------------------------------------------------------------------------------------------------ ZF1 hit flash
        void Flash(UnitView v, U u, float now)
        {
            if (u.flashing) Unflash(u);
            if (u.rends == null)
            {
                var all = v.GetComponentsInChildren<Renderer>(true); var keep = new List<Renderer>(all.Length);
                foreach (var r in all) if (r is MeshRenderer || r is SkinnedMeshRenderer) keep.Add(r);
                u.rends = keep.ToArray();
            }
            bool seen = false;
            foreach (var r in u.rends)
            {
                if (!r || !r.enabled || !r.isVisible) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i]; if (!m || !m.HasProperty(EmId)) continue;
                    r.GetPropertyBlock(mpb, i);
                    var prev = mpb.HasColor(EmId) ? mpb.GetColor(EmId) : m.GetColor(EmId);
                    u.saved.Add((r, i, prev));
                    mpb.SetColor(EmId, prev + FlashColor);
                    r.SetPropertyBlock(mpb, i);
                    seen = true;
                }
            }
            if (!seen) return;
            u.flashing = true; u.flashUntil = now + FlashTime; u.lastFlash = now; Flashes++;
        }

        void Unflash(U u)
        {
            foreach (var s in u.saved)
            {
                if (!s.r) continue;
                s.r.GetPropertyBlock(mpb, s.i); mpb.SetColor(EmId, s.prev); s.r.SetPropertyBlock(mpb, s.i);
            }
            u.saved.Clear(); u.flashing = false;
        }

        // ------------------------------------------------------------------------------------------------ ZF24 buildings
        float nextBld;
        void TickBuildings(float now)
        {
            var fx = OpenZFxPlayer.I; bool fxOn = fx && fx.isActiveAndEnabled;
            bool scan = now >= nextBld; if (scan) nextBld = now + 0.25f;
            foreach (var o in game.World.Objects)
            {
                if (o.Kind != ZKind.Building || o.MaxHealth <= 0 || o.IsBridge) continue;
                if (!blds.TryGetValue(o.Id, out var b))
                {
                    if (!scan) continue;
                    b = new B(); blds[o.Id] = b;
                }
                if (scan && !b.hasBounds && game.StaticViews.TryGetValue(o.Id, out var go) && go)
                {
                    bool any = false; var bb = new Bounds();
                    foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (!(r is MeshRenderer)) continue; if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds); }
                    if (any) { b.bounds = bb; b.hasBounds = true; }
                }
                var c = game.CenterWorld(o);
                var top = b.hasBounds ? new Vector3(b.bounds.center.x, b.bounds.min.y + b.bounds.size.y * 0.8f, b.bounds.center.z) : c + Vector3.up * 2.5f;
                float span = b.hasBounds ? Mathf.Clamp(Mathf.Min(b.bounds.size.x, b.bounds.size.z) * 0.3f, 0.6f, 3f) : 1.2f;
                if (o.Destroyed)
                {
                    if (b.stage > 0 && b.loop && AudioDirector.I) { AudioDirector.I.StopLoop("bldfire:" + o.Id, 0.8f); b.loop = false; }
                    b.stage = 0;
                    if (o.IsFort) FortRuin(o, b, c, now, fx, fxOn);
                    continue;
                }
                if (b.ruin) { b.ruin = false; if (b.glow) Destroy(b.glow.gameObject); if (AudioDirector.I) AudioDirector.I.StopLoop("ruinglow:" + (int)o.Owner, 1f); }   // rebuilt
                float k = o.Health / (float)o.MaxHealth;
                int want = k <= BurnAt ? 2 : k <= SmokeAt ? 1 : 0;
                if (b.stage == 2 && k <= OutAt) want = 2;
                if (want != b.stage)
                {
                    if (want >= 1 && b.stage == 0) { BuildingSmoke++; AudioDirector.Fire("dst_structure_groan", top, o.Id, 0.5f, 1.1f); }
                    if (want == 2 && b.stage < 2)
                    {
                        BuildingFires++;
                        OzSoundV3.Ignite("bld_" + o.Id, top, 0.7f);
                        if (AudioDirector.I && AudioDirector.Has("dst_fire_loop")) { AudioDirector.I.StartLoop("bldfire:" + o.Id, "dst_fire_loop", top, null, o.IsFort ? 0.7f : 0.55f); b.loop = true; }
                        Debug.Log($"[DamageStates] building {o.Type} #{o.Id} on fire at {o.Health}/{o.MaxHealth}");
                    }
                    else if (b.stage == 2 && want < 2)
                    {
                        if (b.loop && AudioDirector.I) { AudioDirector.I.StopLoop("bldfire:" + o.Id, 0.8f); b.loop = false; }
                        if (b.sticky) { OpenZPyroFire.StopBurning(o.Id); b.sticky = false; }
                        AudioDirector.Fire("dtl_steam_hiss", top, o.Id, 1.3f, 0.9f);
                        if (o.Owner == game.player) OpenZVoiceV3.FireOut(top, true);
                    }
                    b.stage = want;
                }
                if (b.stage == 0) continue;
                if (b.stage < 2) b.hasFirePt = false;
                if (b.stage == 2 && !b.hasFirePt) { b.hasFirePt = true; b.firePt = top + new Vector3(Random.Range(-span, span), 0f, Random.Range(-span, span)) * 0.5f; }
                if (b.stage == 2 && now >= b.nextSticky && game.StaticViews.TryGetValue(o.Id, out var sgo) && sgo)
                {
                    b.nextSticky = now + 2f;
                    b.sticky = OpenZPyroFire.KeepBurningSmall(o, null, sgo, true, b.firePt);
                }
                if (b.stage == 2 && !b.sticky) AddPlume(b.firePt, span * 1.2f, 1f);
                if (!fxOn || now < b.nextFx || !WreckFx.OnScreen(top, 0.1f)) continue;
                b.nextFx = now + (b.stage == 2 ? 0.2f : 0.22f);
                var p = top + new Vector3(Random.Range(-span, span), 0f, Random.Range(-span, span));
                fx.WreckSmoke(p, o.IsFort ? 2.8f : 2.2f, b.stage == 2);
                if (b.stage == 2 && Random.value < 0.5f)
                    fx.EmitChannel("leg_spark", p, new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(1.6f, 3f), Random.Range(-0.5f, 0.5f)), Random.Range(0.8f, 1.5f), 0.08f, new Color(1f, 0.55f, 0.18f), 1f);
            }
        }

        /// <summary>ZF24: the destroyed fort keeps a slow pulsing orange glow, embers and a quiet crackle.</summary>
        void FortRuin(ZObject o, B b, Vector3 c, float now, OpenZFxPlayer fx, bool fxOn)
        {
            var at = new Vector3(c.x, game.GroundY(c.x, c.z) + 1.2f, c.z);
            if (!b.ruin)
            {
                b.ruin = true; RuinGlows++;
                var lg = new GameObject("OZ_FortRuinGlow_" + o.Id); lg.transform.SetParent(transform, false); lg.transform.position = at;
                b.glow = lg.AddComponent<Light>(); b.glow.type = LightType.Point; b.glow.color = new Color(1f, 0.46f, 0.16f); b.glow.range = 11f; b.glow.shadows = LightShadows.None; b.glow.intensity = 0f;
                if (AudioDirector.I && AudioDirector.Has("dst_fire_loop") && !AudioDirector.I.LoopActive("ruinglow:" + (int)o.Owner)) AudioDirector.I.StartLoop("ruinglow:" + (int)o.Owner,   // one crackle per fallen fort (L17's red fort is a fort_back)
                     "dst_fire_loop", at, null, 0.35f, 0.85f);
                Debug.Log($"[DamageStates] fort ruin glow {o.Type} #{o.Id}");
            }
            float breathe = 0.5f + 0.5f * Mathf.Sin((now + o.Id * 0.7f) * Mathf.PI * 2f / RuinGlowPeriod);
            if (b.glow) b.glow.intensity = Mathf.Lerp(1.2f, 4.2f, breathe) * (0.92f + 0.08f * Mathf.PerlinNoise(now * 3f, o.Id));
            AddPlume(at, 3f, 0.45f + 0.4f * breathe);
            if (fxOn && now >= b.nextFx && WreckFx.OnScreen(at, 0.1f))
            {
                b.nextFx = now + 0.3f;
                var p = at + new Vector3(Random.Range(-2.5f, 2.5f), 0f, Random.Range(-2.5f, 2.5f));
                fx.EmitChannel("leg_spark", p, new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(1f, 2.2f), Random.Range(-0.3f, 0.3f)), Random.Range(1f, 1.8f), 0.08f, new Color(1f, 0.5f, 0.15f), 1f);
                if (Random.value < 0.5f) fx.WreckSmoke(p, 2f, Random.value < 0.35f);
            }
        }

        // ------------------------------------------------------------------------------------------------ HX1 drawing
        void AddPlume(Vector3 at, float size, float alpha)
        {
            if (!plumeMat || !cam || plumes.Count >= 24) return;
            plumes.Add(new Plume { at = at, size = size, alpha = alpha, d2 = (at - cam.transform.position).sqrMagnitude });
        }
        sealed class NearFirst : IComparer<Plume> { public int Compare(Plume a, Plume b) => a.d2.CompareTo(b.d2); }
        static readonly NearFirst DistCmp = new NearFirst();

        void DrawShimmer(float now)
        {
            if (!cam || !quad) { plumes.Clear(); return; }
            var cp = cam.transform.position;
            int nb = 0;
            if (blastMat)
                for (int i = blasts.Count - 1; i >= 0; i--)
                {
                    var bl = blasts[i]; float a = (now - bl.t0) / bl.life;
                    if (a >= 1f) { blasts.RemoveAt(i); continue; }
                    if (nb >= MaxBlastShimmers || !WreckFx.OnScreen(bl.at, 0.2f)) continue;
                    float sz = bl.size * (0.7f + 0.5f * a);
                    shimBlock.SetFloat(AlphaId, (1f - a) * (1f - a) * Mathf.Clamp01(a * 8f));
                    var rot = Quaternion.LookRotation(bl.at - cp, Vector3.up);
                    var rp = new RenderParams(blastMat) { layer = 0, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, matProps = shimBlock };
                    Graphics.RenderMesh(rp, quad, 0, Matrix4x4.TRS(bl.at + Vector3.up * (sz * 0.25f), rot, new Vector3(sz, sz, 1f)));
                    nb++;
                }
            PeakShimmers = Mathf.Max(PeakShimmers, nb);
            if (plumeMat && plumes.Count > 0)
            {
                plumes.Sort(DistCmp);
                int n = Mathf.Min(MaxPlumes, plumes.Count), drawn = 0;
                for (int i = 0; i < n; i++)
                {
                    var s = plumes[i]; if (!WreckFx.OnScreen(s.at, 0.2f)) continue;
                    shimBlock.SetFloat(AlphaId, Mathf.Clamp01(s.alpha));
                    var rot = Quaternion.LookRotation(s.at - cp, Vector3.up);
                    float h = s.size * 1.8f;
                    var rp = new RenderParams(plumeMat) { layer = 0, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, matProps = shimBlock };
                    Graphics.RenderMesh(rp, quad, 0, Matrix4x4.TRS(s.at + rot * Vector3.up * (h * 0.5f), rot, new Vector3(s.size, h, 1f)));
                    drawn++;
                }
                PeakPlumes = Mathf.Max(PeakPlumes, drawn);
            }
            plumes.Clear();
        }

        static Mesh BuildQuad()
        {
            var m = new Mesh { name = "OZ_DamageShimmerQuad" };
            m.SetVertices(new List<Vector3> { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            m.SetNormals(new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0); m.RecalculateBounds();
            return m;
        }

        /// <summary>Play check line.</summary>
        public static string Report() =>
            $"[DamageStates] ignited {Ignited}, out {Extinguished}, battered {Battered}, flashes {Flashes}, bld smoke {BuildingSmoke}, bld fires {BuildingFires}, ruin glows {RuinGlows}, oil {OilStains}, blast shimmers {BlastShimmers} (peak {PeakShimmers}), plumes peak {PeakPlumes}, hit flash {(HitFlash ? "ON" : "OFF")}{(CaptureNoFlash ? " (capture: off)" : "")}, gear restored {GearRestored}, gear lost {GearLost}";
    }
}
