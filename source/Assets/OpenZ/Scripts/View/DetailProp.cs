using System;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// View side only: a detail has no Zod object, the sim never sees it.
    /// It takes damage from the same two view paths the other destructible decor uses:
    ///   * shots: the sim's Hitscan segments in World.Events (DetailKit.ShotSegment, as DesertCritters reads them) passing
    ///     within hitRadius of the prop's axis (1 point each; blastOnly props ignore them);
    ///   * splash: FxManager.Exploded (PropBreakables' reach rule 1.2 + 2.4 x strength, + hitRadius) breaks it; up to 1.6 x the
    ///     reach it takes half its health.
    /// A hit wobbles the prop with a knock sound and a few bits; the break hides the body, throws the LOOSE_ parts (DetailKit
    /// Chunks), bursts bits in the prop's own colours (DetailKit.Bits), raises dust, leaves a small scorch and shows the RUIN_
    /// stump, plays the break sound and stops the idle loop. GLOW_ lamps glow at night and LIGHT_ empties are real night lights
    /// offered to the night light budget (NightBudgetShare, as HarbourDetails does).
    /// </summary>
    [DefaultExecutionOrder(74)]
    public class DetailProp : MonoBehaviour
    {
        public static readonly List<DetailProp> All = new List<DetailProp>();

        [Header("Identity")]
        public string id = "";
        public string kind = "sign";
        [Header("Damage")]
        public float health = 3f;
        [Tooltip("radius (m) of the hit cylinder round the prop root (shots, splash)")] public float hitRadius = 0.6f;
        [Tooltip("height (m) of the hit cylinder")] public float hitHeight = 2f;
        public bool blastOnly;
        [Tooltip("cave / waterfall: never breaks")] public bool intact;
        [Header("Break look")]
        [Tooltip("wood, metal, stone, plant, glass")] public string mat = "wood";
        public Color[] debris = { new Color(0.55f, 0.38f, 0.22f), new Color(0.36f, 0.25f, 0.15f) };
        public int debrisCount = 16;
        public float scorch = 0.7f;
        [Header("Sounds (AudioDirector event if it exists, else CombatAudio clips)")]
        public string breakEvent = "";
        public string[] breakClips = new string[0];
        public string hitEvent = "";
        public string[] hitClips = new string[0];
        [Tooltip("Extra clips layered on the break (each plays once): glass, electric fizz, ...")]
        public string[] breakLayers = new string[0];
        public string loopEvent = "";
        [Tooltip("Resources path of the idle loop clip (own 3D source when the event is not in the sound bible)")] public string loopRes = "";
        public float loopMin = 4f, loopMax = 30f, loopVol = 0.5f;
        public string oneShotEvent = "";
        public string[] oneShotClips = new string[0];
        public Vector2 oneShotEvery = new Vector2(6f, 14f);
        [Tooltip("degrees the LOOSE_ parts (board, slats, lantern) swing in the gusts; the creak / rattle one-shot plays on the big swings")]
        public float sway = 0f;
        public float oneShotVol = 0.35f, oneShotPitch = 1f;
        [Header("Night")]
        public Color glowColor = new Color(1f, 0.72f, 0.36f);
        public Color lightColor = new Color(1f, 0.72f, 0.4f);
        public float lightRange = 3f, lightIntensity = 0.8f, lightPriority = 1.3f;
        [Tooltip("GLOW_ lamps of this prop glow at night only (lanterns, bus-stop light); traffic lights / neon drive their own")]
        public bool glowAtNight = true;
        [Tooltip("GLOW_ lamp emission at full night (+0.05 by day); below the bloom threshold 0.95 (NightGrade), as BaseProp: a lit lamp, no blob")]
        public float glowNight = 0.8f;
        [Tooltip("Scale of the hit wobble of the whole prop (a ballasted banner frame only shudders: 0.15)")]
        public float knockWobble = 1f;
        /// <summary>fuel_banner_v1_street: a component can own the hit tests and the HP of its prop (the OVERHEAD street banner: only rounds
        /// that really cross its print hit it, a blast is measured to the print, and FuelBanner runs its own hole / tear / shred / break
        /// ladder while Damage only knocks). Null / false = the usual hit cylinder and HP.</summary>
        [NonSerialized] public Func<Vector3, Vector3, bool> ShotTest;
        [NonSerialized] public Func<Vector3, float> BlastDist;
        [NonSerialized] public bool HpOwned;
        /// <summary>DV88: many props of one look (the trees of level 16) share one pool of bits instead of one each. Null = its own pool, as before.</summary>
        [NonSerialized] public DetailKit.Bits SharedBits;

        public bool Broken { get; private set; }
        public float Hp { get; private set; }
        /// <summary>
        /// the tumbleweeds). A sim-backed prop takes no view damage: shots and blasts only knock it (hit sound, bits), the sim's
        /// hit points decide and its Death event breaks it (CombatView -> DetailPropHub.SimDied), so view and sim never disagree.</summary>
        [NonSerialized] public int SimId;
        public bool SimBacked => SimId > 0;
        public event Action<DetailProp, Vector3> Hit, BrokenBy;
        /// <summary>
        /// banner's holes, tear and shred) follow shots and blasts on a sim-backed prop too. Never changes Hp.</summary>
        public event Action<DetailProp, float, Vector3, bool> Damaged;
        /// <summary>
        /// fly (the banner's strips) or keep one back (<see cref="ReleaseLoose"/>).</summary>
        public event Action<DetailProp, Vector3> Breaking;
        /// <summary></summary>
        public void ReleaseLoose(Transform t)
        {
            int i = loose.IndexOf(t);
            if (i >= 0) { loose.RemoveAt(i); looseRest.RemoveAt(i); }
            body.RemoveAll(r => !r || r.transform.IsChildOf(t));                                            // and it stays visible after the break
        }

        [NonSerialized] public readonly List<Renderer> Glows = new List<Renderer>();
        [NonSerialized] public readonly List<DetailLight> Lights = new List<DetailLight>();
        readonly List<Renderer> body = new List<Renderer>();
        readonly List<Transform> loose = new List<Transform>(), ruin = new List<Transform>(), hinged = new List<Transform>();
        readonly List<Quaternion> looseRest = new List<Quaternion>();
        MaterialPropertyBlock mpb;
        Vector3 wobbleAxis; float wobble, wobbleVel; Quaternion rest; bool started;
        float nextOneShot; string loopKey; AudioSource ownLoop; DetailKit.Bits bits;
        static int serial;

        public static string Base(string n) { return DetailKit.Base(n); }

        /// <summary>Flat range (m) of the quiet near-listener one-shots (creaks, rattles, relay clicks, rustles).</summary>
        public const float HearRange = 40f;
        static RtsCamera rtsCam; static float rtsRetry;

        static RtsCamera Rts()
        {
            if (!rtsCam && Time.unscaledTime >= rtsRetry) { rtsCam = UnityEngine.Object.FindFirstObjectByType<RtsCamera>(); rtsRetry = Time.unscaledTime + 1f; }
            return rtsCam;
        }

        /// <summary>
        /// RtsCamera.focus, not the camera (which sits 1.22 x height away: 85 m at the default 70 m), so the gate is the flat
        /// distance to the rig (else the RTS focus, else the camera) under r, with p on screen. At h 70 a prop at the focus is
        /// 0 m away (the old camera gates measured 85 m and stayed silent).</summary>
        public static bool NearListener(Vector3 p, float r)
        {
            Vector3 l;
            var ca = CombatAudio.I;
            if (ca && ca.ListenerRig) l = ca.ListenerRig.position;
            else
            {
                var rts = Rts();
                if (rts) l = rts.focus;
                else { var c = Camera.main; if (!c) return true; l = c.transform.position; }
            }
            float dx = p.x - l.x, dz = p.z - l.z;
            if (dx * dx + dz * dz > r * r) return false;
            return DetailKit.OnScreen(Camera.main, p, 0.1f);
        }

        /// <summary>RTS camera height (m) for the sound log; -1 without an RtsCamera.</summary>
        public static float CamHeight() { var rts = Rts(); return rts ? rts.height : -1f; }
        public Vector3 Foot { get { return transform.position; } }
        public Vector3 Centre { get { return transform.position + Vector3.up * (hitHeight * 0.5f); } }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); DetailPropHub.Ensure(); }
        void OnDisable() { All.Remove(this); StopLoop(); }

        void Start()
        {
            started = true;
            Hp = Mathf.Max(0.5f, health);
            var ls = transform.lossyScale;                                                  // the placement scales the signs up
            hitRadius *= Mathf.Max(ls.x, ls.z); hitHeight *= ls.y;
            rest = transform.localRotation;
            mpb = new MaterialPropertyBlock();
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                string n = Base(t.name);
                if (n.StartsWith("RUIN_")) { ruin.Add(t); t.gameObject.SetActive(false); }
                else if (n.StartsWith("LOOSE_"))
                {   // dv2_windmill_break_v1: a LOOSE_ part with a HINGE_ child topples about its base (the wind-pump tower), it does not fly
                    bool hinge = false;
                    foreach (Transform c in t) if (Base(c.name).StartsWith("HINGE_")) { hinge = true; break; }
                    if (hinge) hinged.Add(t); else { loose.Add(t); looseRest.Add(t.localRotation); }
                }
            }
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (IsUnder(r.transform, ruin)) continue;
                body.Add(r);
                if (Base(r.name).StartsWith("GLOW_")) Glows.Add(r);
            }
            foreach (var t in GetComponentsInChildren<Transform>(true))       // all LIGHT_ empties (glowAtNight only says who drives them)
                if (Base(t.name).StartsWith("LIGHT_")) Lights.Add(new DetailLight(t, lightColor, lightRange, lightIntensity, lightPriority, (id ?? "dtl") + "_" + (serial++)));
            nextOneShot = Time.time + UnityEngine.Random.Range(oneShotEvery.x, oneShotEvery.y);
            StartLoop();
        }

        static bool IsUnder(Transform t, List<Transform> roots)
        {
            for (; t; t = t.parent) if (roots.Contains(t)) return true;
            return false;
        }

        public Transform Find(string baseName) { return DetailKit.Find(transform, baseName); }

        /// <summary>First child whose base name starts with the prefix.</summary>
        public Transform FindPrefix(string prefix)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (Base(t.name).StartsWith(prefix)) return t;
            return null;
        }

        // ------------------------------------------------------------------ idle sounds
        Vector3 SoundPos()
        {
            var s = FindPrefix("SND_");
            return s ? s.position : transform.position + Vector3.up * Mathf.Min(1.5f, hitHeight * 0.5f);
        }

        void StartLoop()
        {
            if (Broken || (string.IsNullOrEmpty(loopEvent) && string.IsNullOrEmpty(loopRes))) return;
            var pos = SoundPos();
            if (!string.IsNullOrEmpty(loopEvent) && AudioDirector.I && AudioDirector.Has(loopEvent))
            {
                loopKey = "dv2_" + GetInstanceID();
                AudioDirector.I.StartLoop(loopKey, loopEvent, pos, transform, loopVol);
                OzSoundLog.Add("DetailsV2", loopEvent, loopEvent, "loop start (AudioDirector)", loopVol, pos);
                return;
            }
            var clip = DetailKit.Pool3D.Clip(loopRes);
            if (!clip) { OzSoundLog.Add("DetailsV2", loopEvent, loopRes, "loop clip missing", 0f, pos); return; }
            var go = new GameObject("DV2_Loop"); go.transform.SetParent(transform, false); go.transform.position = pos;
            ownLoop = go.AddComponent<AudioSource>();
            ownLoop.clip = clip; ownLoop.loop = true; ownLoop.playOnAwake = false; ownLoop.spatialBlend = 1f; ownLoop.dopplerLevel = 0f;
            ownLoop.rolloffMode = AudioRolloffMode.Linear; ownLoop.minDistance = loopMin; ownLoop.maxDistance = loopMax;
            ownLoop.time = UnityEngine.Random.Range(0f, Mathf.Max(0f, clip.length - 0.1f));
            ownLoop.volume = LoopGain() * loopVol;
            ownLoop.Play();
            OzSoundLog.Add("DetailsV2", loopEvent, clip.name, "loop start (own 3D source)", ownLoop.volume, pos);
        }

        static float LoopGain() { return CombatAudio.I ? CombatAudio.I.volume * CombatAudio.BusGain(CombatAudio.Bus.World) : 0.55f; }

        /// <summary>Loop volume 0-1 x loopVol (components modulate it: neon flicker, windmill speed, hydrant spray).</summary>
        public void SetLoopLevel(float k)
        {
            k = Mathf.Clamp01(k);
            if (loopKey != null && AudioDirector.I) AudioDirector.I.SetLoop(loopKey, loopVol * k, 1f);
            if (ownLoop) ownLoop.volume = LoopGain() * loopVol * k;
        }

        public void StopLoop()
        {
            if (loopKey != null && AudioDirector.I) AudioDirector.I.StopLoop(loopKey, 0.3f);
            loopKey = null;
            if (ownLoop) { Destroy(ownLoop.gameObject); ownLoop = null; }
        }

        /// <summary>A one-shot at the prop: the event when it exists, else the clips (CombatAudio, sub folders of OpenZ/Audio/Sfx
        /// allowed), else the Resources path through DetailKit's 3D pool. Logged in the sound log.</summary>
        public static void Play(string ev, string[] clips, Vector3 at, float vol, float pitch = 1f, int emitter = -1, float throttle = 0.05f)
        {
            if (!string.IsNullOrEmpty(ev) && (DetailKit.AudioEventActive || AudioDirector.Has(ev)))
            {
                DetailKit.Sound(ev, at, vol, "prop", 0.05f, -1);
                OzSoundLog.Add("DetailsV2", ev, ev, "event", vol, at);
                return;
            }
            if (clips == null || clips.Length == 0) { OzSoundLog.Add("DetailsV2", ev, "-", "no clip", 0f, at); return; }
            var ca = CombatAudio.I;
            if (ca && Mathf.Abs(pitch - 1f) < 0.01f)
            {
                bool any = false; foreach (var c in clips) if (ca.HasClip(c)) { any = true; break; }
                if (any) { ca.Play("prop", at, vol, throttle, emitter, clips); OzSoundLog.Add("DetailsV2", ev, clips[0], "CombatAudio", vol, at); return; }
            }
            var pick = clips[UnityEngine.Random.Range(0, clips.Length)];
            DetailKit.Pool3D.Play("OpenZ/Audio/Sfx/" + (pick.StartsWith("s3_") && pick.IndexOf('/') < 0 ? "SoundV3/" : "") + pick, at, vol, pitch);   // SoundV3 takes live in their own folder
            OzSoundLog.Add("DetailsV2", ev, pick, "pool3D", vol, at);
        }

        // ------------------------------------------------------------------ damage
        /// <summary>Distance (m, flat) from p to the hit cylinder, and whether p is within its height band (+margin).</summary>
        public float FlatDist(Vector3 p)
        {
            if (BlastDist != null) return BlastDist(p);                                                   // fuel_banner_v1_street
            var d = p - transform.position; d.y = 0f;
            return Mathf.Max(0f, d.magnitude - hitRadius);
        }

        public bool ShotHits(Vector3 a, Vector3 b)
        {
            if (ShotTest != null) return ShotTest(a, b);                                                  // fuel_banner_v1_street
            var p0 = transform.position; var p1 = p0 + Vector3.up * hitHeight;
            return SegSeg(a, b, p0, p1) <= hitRadius;
        }

        static float SegSeg(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {   // closest distance between two segments (Ericson 5.1.9)
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r), s, t;
            if (a <= 1e-6f && e <= 1e-6f) return (p1 - p2).magnitude;
            if (a <= 1e-6f) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= 1e-6f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), den = a * e - b * b;
                    s = den != 0f ? Mathf.Clamp01((b * f - c * e) / den) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            return ((p1 + d1 * s) - (p2 + d2 * t)).magnitude;
        }

        /// <summary>Damage from a shot (1) or a blast (see the hub). from = where it came from (the break throws away from it).</summary>
        public void Damage(float amount, Vector3 from, bool blast)
        {
            if (intact || Broken || !started || amount <= 0f) return;
            if (Damaged != null) Damaged(this, amount, from, blast);
            if (SimBacked || HpOwned) { Knock(from, blast ? 1f : blastOnly ? 0.35f : 0.6f); return; }     // fuel_banner_v1_street: HpOwned                   // DV67: the sim decides (CombatView death -> Break)
            if (blastOnly && !blast) { Knock(from, 0.35f); return; }
            Hp -= amount;
            if (Hit != null) Hit(this, from);
            if (Hp <= 0f) { Break(from); return; }
            Knock(from, blast ? 1f : 0.6f);
        }

        void Knock(Vector3 from, float k)
        {
            var away = transform.position - from; away.y = 0f;
            if (away.sqrMagnitude < 1e-4f) away = Vector3.forward;
            wobbleAxis = Vector3.Cross(Vector3.up, away.normalized);
            wobbleVel += 40f * k * knockWobble;
            var at = Centre;
            Play(hitEvent, hitClips != null && hitClips.Length > 0 ? hitClips : DefaultHit(mat), at, 0.35f * Mathf.Clamp01(0.5f + k), 1f, GetInstanceID());
            Bits().Burst(at, 3 + Mathf.RoundToInt(3 * k), 0, 1.6f, 0.05f, new System.Random(GetInstanceID() ^ Time.frameCount));
        }

        public DetailKit.Bits Bits()
        {
            if (bits == null) { bits = SharedBits ?? new DetailKit.Bits(debris != null && debris.Length > 0 ? debris : new[] { Color.gray }); DetailPropHub.Track(bits); }   // DV88: SharedBits (null for every prop but the trees of level 16)
            return bits;
        }

        public static string[] DefaultBreak(string m)
        {
            switch (m)
            {
                case "metal": return new[] { "s3_metal_heavy_a", "s3_metal_heavy_b", "s3_metal_heavy_c" };
                case "stone": return new[] { "v3-rock-break-a", "v3-rock-break-b" };
                case "plant": return new[] { "s3_cactus_a", "s3_cactus_b" };
                case "glass": return new[] { "sd_glass_shatter_a", "sd_glass_shatter_b" };
                default: return new[] { "sd_debris_wood_a", "sd_debris_wood_b", "sd_debris_wood_c" };
            }
        }

        public static string[] DefaultHit(string m)
        {
            switch (m)
            {
                case "metal": return new[] { "s3_ricochet_a", "s3_ricochet_b", "s3_ricochet_c" };
                case "stone": return new[] { "sd_debris_plop_small_a", "sd_debris_plop_small_b" };
                case "plant": return new[] { "s3_cactus_a" };
                default: return new[] { "sd_debris_wood_c", "sd_step_robot_wood_03" };
            }
        }

        /// <summary>The break (also callable by tools / checks). Components listen to BrokenBy (sparks, spray, wheel off).</summary>
        public void Break(Vector3 from)
        {
            if (Broken || intact) return;
            Broken = true;
            StopLoop();
            if (Breaking != null) Breaking(this, from);
            var c = Centre; var foot = transform.position;
            var away = foot - from; away.y = 0f; if (away.sqrMagnitude < 1e-4f) away = Vector3.forward; away.Normalize();
            var rng = new System.Random(GetInstanceID() * 31 + Time.frameCount);
            // loose parts fly (DetailKit.Chunks: tumble, bounce, settle flat, pop out); the rest of the body goes
            var hub = DetailPropHub.I;
            foreach (var t in loose)
            {
                if (!t) continue;
                var v = away * UnityEngine.Random.Range(2.5f, 5f) + Vector3.up * UnityEngine.Random.Range(3.5f, 6.5f) + UnityEngine.Random.insideUnitSphere * 1.2f;
                var w = UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(240f, 720f);
                if (hub) hub.Chunks.Throw(t, v, w, UnityEngine.Random.Range(9f, 14f)); else t.gameObject.SetActive(false);
            }
            foreach (var r in body) if (r && !IsUnder(r.transform, loose) && !IsUnder(r.transform, hinged)) r.enabled = false;
            foreach (var t in ruin) if (t) t.gameObject.SetActive(true);
            foreach (var l in Lights) l.Kill();
            SetGlow(Glows, Color.black, 0f);                                                  // lamps / lenses go dark
            // bits in the prop's own colours, dust, a small scorch
            var b = Bits();
            int n = Mathf.Max(6, debrisCount);
            for (int i = 0; i < (debris != null ? debris.Length : 1); i++)
                b.Burst(c, Mathf.Max(2, n / Mathf.Max(1, debris != null ? debris.Length : 1)), (byte)i, 2.6f + hitHeight * 0.25f, 0.06f + hitRadius * 0.05f, rng);
            var fx = OpenZFxPlayer.I;
            if (fx)
            {
                fx.Dust(foot + Vector3.up * 0.2f, Mathf.Clamp(0.25f + hitRadius * 0.4f, 0.3f, 1.1f));
                if (scorch > 0.01f) fx.Scorch(foot, scorch, 0);
            }
            else if (FxManager.I) FxManager.I.Dust(foot + Vector3.up * 0.3f, Mathf.Clamp(hitRadius * 0.4f, 0.3f, 1f));
            Play(breakEvent, breakClips != null && breakClips.Length > 0 ? breakClips : DefaultBreak(mat), c, 0.75f, 1f, GetInstanceID(), 0f);   // never throttled by its own killing hit
            if (breakLayers != null) foreach (var l in breakLayers) if (!string.IsNullOrEmpty(l)) Play("", new[] { l }, c, 0.6f, 1f, GetInstanceID(), 0f);   // glass / fizz layered on the break frame
            foreach (var h in hinged) if (h) StartCoroutine(Topple(h, away));                               // dv2_windmill_break_v1: the tower falls as one piece
            if (glowAtNight && (Glows.Count > 0 || Lights.Count > 0))                           // a lantern / lamp: its glass goes too
                Play("", new[] { "sd_lamp_glass_a", "sd_lamp_glass_b" }, Glows.Count > 0 && Glows[0] ? Glows[0].bounds.center : c, 0.5f);
            if (BrokenBy != null) BrokenBy(this, from);
        }

        public static int Topples; public static string LastTopple = "";
        /// <summary>The hinged LOOSE_ part falls away from the shot as one rigid piece about the outer bottom edge of its farthest leg
        /// foot (README: hingeR = max over the 4 legs of 0.688 (sx away.x + sz away.z) + 0.06 (|away.x| + |away.z|), prop axes):
        /// th'' = 1.71 sin th (3g / 2L, L 8.6 m), kick 6 deg + 0.5 rad/s (contact ~1.5 s). The contact angle is found before the fall
        /// from the parts' world boxes against the ground (ZGame.GroundY), the vane boom on its own (it can touch first); on the
        /// contact frame: s3_metal_heavy crash + dust along the tower line, then the SECT_ sections, the head and the rod are released
        /// as chunks (DetailKit.Chunks: they separate a little, settle flat, pop out).</summary>
        System.Collections.IEnumerator Topple(Transform tower, Vector3 away)
        {
            var game = ZGame.I; var hub = DetailPropHub.I;
            var awayL = transform.InverseTransformDirection(away); awayL.y = 0f; awayL = awayL.sqrMagnitude > 1e-6f ? awayL.normalized : Vector3.forward;
            float hingeR = float.MinValue;
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
                hingeR = Mathf.Max(hingeR, 0.688f * (sx * awayL.x + sz * awayL.z) + 0.06f * (Mathf.Abs(awayL.x) + Mathf.Abs(awayL.z)));
            hingeR *= Mathf.Abs(transform.lossyScale.x);
            var pivot = tower.position + away * hingeR;
            var axis = Vector3.Cross(Vector3.up, away).normalized;                                            // +angle tips the top toward `away`
            var p0 = tower.position; var r0 = tower.rotation;
            // the parts that land: sections, head, rod (the wheel already flew); the vane separately
            var parts = new List<Transform>(); Transform vane = null;
            foreach (var t in tower.GetComponentsInChildren<Transform>(true))
            {
                string n = Base(t.name);
                if (n.StartsWith("SECT_Vane")) { vane = t; continue; }
                if (t.parent == tower && (n.StartsWith("SECT_") || n.StartsWith("HEAD_") || n.StartsWith("BOB_"))) parts.Add(t);
            }
            float main = 100f;
            foreach (var t in parts) main = Mathf.Min(main, ToppleContact(t, vane, pivot, axis, game, p0.y));
            float vaneAt = vane ? ToppleContact(vane, null, pivot, axis, game, p0.y) : 999f;
            float th = 6f * Mathf.Deg2Rad, w = 0.5f, t0 = Time.time; bool vaneOff = false;
            while (true)
            {
                float dt = game && game.paused ? 0f : Mathf.Min(Time.deltaTime, 0.05f);
                w += 1.71f * Mathf.Sin(th) * dt; th += w * dt;
                float deg = th * Mathf.Rad2Deg;
                if (vane && !vaneOff && deg >= vaneAt)
                {   // the vane boom hits first (falls N / NE / NW): it snaps off and lands on its own
                    vaneOff = true;
                    Play("", new[] { "s3_metal_heavy_a", "s3_metal_heavy_b" }, vane.position, 0.5f, 1.15f);
                    if (hub) hub.Chunks.Throw(vane, away * UnityEngine.Random.Range(0.5f, 1.2f) + Vector3.up * UnityEngine.Random.Range(0.8f, 1.4f), UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(30f, 90f), UnityEngine.Random.Range(10f, 14f));
                }
                bool hit = deg >= main;
                if (hit) deg = main;
                var q = Quaternion.AngleAxis(deg, axis);
                tower.SetPositionAndRotation(pivot + q * (p0 - pivot), q * r0);
                if (hit) break;
                yield return null;
            }
            // contact frame: crash + dust along the tower line, then the sections come apart
            var top = pivot + Quaternion.AngleAxis(main, axis) * (Vector3.up * 8.6f * Mathf.Abs(transform.lossyScale.y));
            Play("", new[] { "s3_metal_heavy_a", "s3_metal_heavy_b", "s3_metal_heavy_c" }, Vector3.Lerp(pivot, top, 0.6f), 0.85f, 0.9f);
            var fx = OpenZFxPlayer.I;
            for (int i = 1; i <= 4; i++)
            {
                var d = Vector3.Lerp(pivot, top, i / 4.5f); d.y = game ? game.GroundY(d.x, d.z) + 0.2f : d.y;
                if (fx) fx.Dust(d, 0.55f + 0.1f * i); else if (FxManager.I) FxManager.I.Dust(d, 0.6f);
            }
            int released = 0;
            foreach (var t in parts)
            {
                if (!t) continue;
                var v = away * UnityEngine.Random.Range(0.5f, 1.5f) + Vector3.up * UnityEngine.Random.Range(0.8f, 1.5f);
                if (hub) { hub.Chunks.Throw(t, v, UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(30f, 90f), UnityEngine.Random.Range(11f, 16f)); released++; }
            }
            Topples++;
            LastTopple = $"{name}: fell {(Quaternion.Inverse(transform.rotation) * away).ToString("F2")} (prop axes), hinge r {hingeR:F2} m, contact {main:F1} deg after {Time.time - t0:F2} s" +
                         $"{(vane ? (vaneOff ? $", vane snapped at {vaneAt:F1} deg" : ", vane landed with the tower") : "")}, {released} sections released";
            Debug.Log("[OpenZ] details v2 topple " + LastTopple);
            OzSoundLog.Add("DetailsV2", "windmill_topple_crash", "s3_metal_heavy", "contact frame " + LastTopple, 0.85f, Vector3.Lerp(pivot, top, 0.6f));
        }

        /// <summary>First angle (deg, 0.5 steps from 20) at which a corner of root's renderer boxes (minus skip), turned about the hinge,
        /// reaches the ground.</summary>
        static float ToppleContact(Transform root, Transform skip, Vector3 pivot, Vector3 axis, ZGame game, float fallbackY)
        {
            var corners = new List<Vector3>();
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || (skip && r.transform.IsChildOf(skip))) continue;
                var b = r.bounds;
                for (int k = 0; k < 8; k++) corners.Add(new Vector3((k & 1) == 0 ? b.min.x : b.max.x, (k & 2) == 0 ? b.min.y : b.max.y, (k & 4) == 0 ? b.min.z : b.max.z));
            }
            for (float a = 20f; a <= 115f; a += 0.5f)
            {
                var q = Quaternion.AngleAxis(a, axis);
                foreach (var c in corners)
                {
                    var p = pivot + q * (c - pivot);
                    float gy = game ? game.GroundY(p.x, p.z) : fallbackY;
                    if (p.y <= gy + 0.03f) return a;
                }
            }
            return 100f;
        }

        // ------------------------------------------------------------------ frame
        void Update()
        {
            float dt = Time.deltaTime;
            if (Mathf.Abs(wobble) > 1e-3f || Mathf.Abs(wobbleVel) > 1e-3f)
            {   // spring: the post rocks and settles (hit reaction)
                wobbleVel += (-wobble * 90f - wobbleVel * 9f) * dt;
                wobble += wobbleVel * dt;
                if (!Broken) transform.localRotation = Quaternion.AngleAxis(Mathf.Clamp(wobble, -9f, 9f), transform.parent ? transform.parent.InverseTransformDirection(wobbleAxis) : wobbleAxis) * rest;
            }
            if (Broken) return;
            if (sway > 0f)
            {   // gusts: boards / slats / lantern swing a little on their nails (the creak lands on the swings)
                float g = Mathf.PerlinNoise(Time.time * 0.35f, transform.position.x * 0.1f) * 2f - 1f;
                for (int i = 0; i < loose.Count; i++)
                    if (loose[i]) loose[i].localRotation = looseRest[i] * Quaternion.AngleAxis(sway * g * (1f + 0.3f * i) * Mathf.Sin(Time.time * (2.1f + 0.4f * i)), Vector3.right);
            }
            if (oneShotClips != null && oneShotClips.Length > 0 && Time.time >= nextOneShot)
            {
                nextOneShot = Time.time + UnityEngine.Random.Range(oneShotEvery.x, oneShotEvery.y);
                var sp = SoundPos();
                if (NearListener(sp, HearRange))                                               // review fix: the listener, not the camera
                    Play(oneShotEvent, oneShotClips, sp, oneShotVol, oneShotPitch * UnityEngine.Random.Range(0.9f, 1.1f));
            }
            if (glowAtNight && Glows.Count > 0) SetGlow(Glows, glowColor, 0.05f + glowNight * DetailKit.Night);
            if (glowAtNight) foreach (var l in Lights) l.Drive(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.45f, DetailKit.Night)), lightColor);
        }

        /// <summary>Emission of GLOW_ renderers (TeamLit _EmissionColor, linear HDR) through a property block.</summary>
        public void SetGlow(List<Renderer> rs, Color c, float k)
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();
            var e = c.linear * Mathf.Max(0f, k);
            foreach (var r in rs)
            {
                if (!r) continue;
                r.GetPropertyBlock(mpb); mpb.SetColor("_EmissionColor", e); r.SetPropertyBlock(mpb);
            }
        }

        public void SetGlow(Renderer r, Color c, float k)
        {
            if (!r) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb); mpb.SetColor("_EmissionColor", c.linear * Mathf.Max(0f, k)); r.SetPropertyBlock(mpb);
        }

        public static Color Hex(string h, Color fallback)
        {
            Color c;
            if (!string.IsNullOrEmpty(h) && ColorUtility.TryParseHtmlString(h.StartsWith("#") ? h : "#" + h, out c)) return c;
            return fallback;
        }

        /// <summary>Screen-rect hover helper for a later PlayerInput hook (a view-only detail cannot be a sim attack target).</summary>
        public bool UnderPointer(Camera cam, Vector2 mp)
        {
            if (!cam || Broken || intact) return false;
            var a = cam.WorldToScreenPoint(transform.position); var b = cam.WorldToScreenPoint(transform.position + Vector3.up * hitHeight);
            if (a.z < 0f || b.z < 0f) return false;
            float r = Mathf.Max(12f, Mathf.Abs(cam.WorldToScreenPoint(transform.position + cam.transform.right * hitRadius).x - a.x));
            return mp.x > Mathf.Min(a.x, b.x) - r && mp.x < Mathf.Max(a.x, b.x) + r && mp.y > Mathf.Min(a.y, b.y) - 4f && mp.y < Mathf.Max(a.y, b.y) + 4f;
        }
    }

    // asset, so L02_Desert.unity embedded one and the Windows player crashed loading L02 ("level1 is corrupted").
    /// <summary>One real night light of a detail (LIGHT_ empty): a point light offered to the night light budget
    /// (NightBudgetShare, merged by NightLightBudget.Tick); without the night package a small own ranking near the camera
    /// focus (DetailPropHub). Drive(k) sets how much it wants to shine (0-1).</summary>
    public sealed class DetailLight
    {
        public readonly Light Light; public readonly NightFixture Fix; public readonly float Intensity;
        public float Want; public bool Dead;
        public DetailLight(Transform at, Color c, float range, float intensity, float priority, string fid)
        {
            var go = new GameObject("DV2_Light"); go.transform.SetParent(at, false);
            Light = go.AddComponent<Light>();
            Light.type = LightType.Point; Light.range = range; Light.color = c; Light.intensity = 0f; Light.enabled = false;
            Light.shadows = LightShadows.None; Light.renderMode = LightRenderMode.ForcePixel;
            Intensity = intensity;
            Fix = new NightFixture { Kind = NightKind.Uplight, Id = fid, Light = Light, Col = c, ColLinear = c.linear, Intensity = intensity, Range = range, Priority = priority, ShadowCandidate = false };
            NightBudgetShare.Fixtures.Add(Fix);
            DetailPropHub.Ensure(); DetailPropHub.Register(this);
        }
        public void Drive(float k, Color c)
        {
            if (Dead) k = 0f;
            Want = k; Fix.LightOut = k;
            if (Light) { Light.color = c; Fix.Col = c; Fix.ColLinear = c.linear; }
        }
        public void Kill() { Dead = true; Drive(0f, Light ? Light.color : Color.black); NightBudgetShare.Fixtures.Remove(Fix); if (Light) Light.enabled = false; }
        public void Apply(float realK)
        {
            if (!Light) return;
            float k = Want * realK;
            bool lit = !Dead && k > 0.002f;
            if (Light.enabled != lit) Light.enabled = lit;
            if (lit) Light.intensity = Intensity * k;
        }
    }

    /// <summary>One hub for all details: reads the sim's Hitscan segments once per frame, listens to FxManager.Exploded, steps
    /// the thrown chunks and bits, and runs the fallback light budget. Created by the first DetailProp / DetailLight.</summary>
    [DefaultExecutionOrder(72)]
    public class DetailPropHub : MonoBehaviour
    {
        public static DetailPropHub I { get; private set; }
        public readonly DetailKit.Chunks Chunks = new DetailKit.Chunks();
        static readonly List<DetailKit.Bits> bits = new List<DetailKit.Bits>();
        static readonly List<DetailLight> lights = new List<DetailLight>();
        /// <summary>Other listeners (TumbleweedRoll): every shot segment of the frame.</summary>
        public static event Action<Vector3, Vector3> Shot;
        /// <summary>Other listeners: every blast (position, legacy strength) after the props took it.</summary>
        public static event Action<Vector3, float> Blast;
        ZGame game; float waterY = -0.35f; bool ownBudget; Camera cam; RtsCamera rts;

        static readonly Dictionary<int, DetailProp> bySim = new Dictionary<int, DetailProp>();
        static ZWorld boundWorld;
        public static int SimBound, SimUnbound, SimBreaks;
        public static DetailProp OfSim(int id) => bySim.TryGetValue(id, out var p) && p ? p : null;
        /// <summary>CombatView: the sim destroyed a detail prop; the drawn prop breaks away from `from` (true = handled).</summary>
        public static bool SimDied(ZObject o, Vector3 from)
        {
            var p = o != null ? OfSim(o.Id) : null;
            if (!p) return false;
            SimBreaks++;
            p.Break(from);
            Debug.Log($"[OpenZ] DV67 detail prop {p.name} (sim #{o.Id}) destroyed in the sim, break from {from.ToString("F1")}");
            return true;
        }
        void BindSim()
        {
            var w = game ? game.World : null;
            if (w == null || w == boundWorld) return;
            boundWorld = w; bySim.Clear(); SimBound = SimUnbound = 0;
            var names = new Dictionary<string, DetailProp>();
            foreach (var p in DetailProp.All) if (p && !names.ContainsKey(p.name)) names[p.name] = p;
            var miss = new List<string>();
            foreach (var o in w.DetailProps)
            {
                if (!w.DetailPropUid.TryGetValue(o.Id, out var uid)) continue;
                if (names.TryGetValue(uid, out var p)) { p.SimId = o.Id; bySim[o.Id] = p; SimBound++; }
                else { SimUnbound++; miss.Add(uid); }
            }
            Debug.Log($"[OpenZ] DV67 detail props: {SimBound} drawn props bound to sim map items, {SimUnbound} without a drawn prop{(miss.Count > 0 ? " (" + string.Join(", ", miss.ToArray()) + ")" : "")}, {w.DetailPropBlockedTiles} tile(s) blocked");
        }

        public static void Ensure()
        {
            if (I || !Application.isPlaying) return;
            var go = new GameObject("DetailsV2_Hub");
            I = go.AddComponent<DetailPropHub>();
        }
        public static void Track(DetailKit.Bits b) { if (b != null && !bits.Contains(b)) bits.Add(b); }
        public static void Register(DetailLight l) { if (l != null && !lights.Contains(l)) lights.Add(l); }

        void Awake() { I = this; }
        void Start()
        {
            game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            waterY = DetailKit.WaterY(game ? game.level : "L02");
            FxManager.Exploded += OnBlast;
        }
        void OnDestroy()
        {
            FxManager.Exploded -= OnBlast;
            foreach (var l in lights) NightBudgetShare.Fixtures.Remove(l.Fix);
            lights.Clear(); bits.Clear(); Chunks.Clear();
            if (I == this) I = null;
        }

        void OnBlast(Vector3 at, float strength)
        {
            float reach = 1.2f + 2.4f * strength;
            var list = DetailProp.All;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var p = list[i];
                if (!p || p.Broken || p.intact) continue;
                if (at.y > p.transform.position.y + p.hitHeight + 3f || at.y < p.transform.position.y - 4f) continue;   // a blast on the plateau vs one below the rim
                float d = p.FlatDist(at);
                if (d <= reach) p.Damage(p.health + 1f, at, true);
                else if (d <= reach * 1.6f) p.Damage(p.health * 0.5f, at, true);
            }
            if (Blast != null) Blast(at, strength);
        }

        void Update()
        {
            if (!game) { game = ZGame.I; if (!game) return; }
            if (!cam) { cam = Camera.main; if (cam) rts = cam.GetComponent<RtsCamera>(); }
            float dt = game.paused ? 0f : Mathf.Min(Time.deltaTime, 0.05f) * Mathf.Clamp(game.gameSpeed, 0.25f, 2f);
            BindSim();                                                                                       // DV67 (once per world)
            ReadShots();
            Chunks.Step(dt, game, waterY);
            for (int i = 0; i < bits.Count; i++) bits[i].StepAndDraw(dt, game);
            Budget(dt);
        }

        /// <summary>
        /// Shot listeners such as the banner holes and the tumbleweeds told).</summary>
        public static void TestShot(Vector3 a, Vector3 b)
        {
            var list = DetailProp.All;
            for (int k = list.Count - 1; k >= 0; k--)
            {
                var p = list[k];
                if (!p || p.Broken || p.intact) continue;
                if (p.ShotHits(a, b)) p.Damage(1f, a, false);
            }
            if (Shot != null) Shot(a, b);
        }

        void ReadShots()
        {
            if (game.World == null) return;
            var ev = game.World.Events;
            for (int i = 0; i < ev.Count; i++)
            {
                var e = ev[i];
                if (e.type != ZEventType.Hitscan) continue;
                Vector3 a, b;
                if (!DetailKit.ShotSegment(game, e, out a, out b)) continue;
                var list = DetailProp.All;
                for (int k = list.Count - 1; k >= 0; k--)
                {
                    var p = list[k];
                    if (!p || p.Broken || p.intact) continue;
                    if (p.ShotHits(a, b)) p.Damage(1f, a, false);
                }
                if (Shot != null) Shot(a, b);
            }
        }

        float ownNext;
        void Budget(float dt)
        {
            if (lights.Count == 0) return;
            bool hookOk = NightBudgetShare.HookFrame >= Time.frameCount - 2;
            if (!ownBudget && !hookOk && Time.frameCount > 10 && !NightLights.I)
            {
                ownBudget = true; foreach (var l in lights) NightBudgetShare.Fixtures.Remove(l.Fix);
                Debug.Log("[OpenZ] details v2: no night budget hook, detail lights use their own 6-light budget");
            }
            if (ownBudget)
            {
                foreach (var l in lights) l.Fix.RealK = Mathf.MoveTowards(l.Fix.RealK, l.Fix.Budgeted && l.Want > 0.001f ? 1f : 0f, dt / 0.35f);
                if (Time.time >= ownNext)
                {
                    ownNext = Time.time + 0.25f;
                    var focus = rts ? rts.focus : (cam ? cam.transform.position : Vector3.zero);
                    foreach (var l in lights) l.Fix.Budgeted = false;
                    for (int n = 0; n < 6; n++)
                    {
                        DetailLight best = null; float bs = 0f;
                        foreach (var l in lights)
                        {
                            if (l.Dead || l.Fix.Budgeted || l.Want <= 0.02f || !l.Light || !DetailKit.OnScreen(cam, l.Light.transform.position, 0.1f)) continue;
                            var lp = l.Light.transform.position;
                            float d = Vector2.Distance(new Vector2(lp.x, lp.z), new Vector2(focus.x, focus.z));
                            float s = (1f + l.Fix.Priority) * l.Want / (1f + d * d / 400f);
                            if (s > bs) { bs = s; best = l; }
                        }
                        if (best == null) break;
                        best.Fix.Budgeted = true;
                    }
                }
            }
            foreach (var l in lights) l.Apply(l.Fix.RealK);
        }
    }
}
