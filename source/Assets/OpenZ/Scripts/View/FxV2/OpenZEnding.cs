using System.Collections;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// sim eliminates a team (its fort died) to the end screen. The sim stays 1:1 (Zod kills the whole team in the fort's tick);
    /// only what is SHOWN is staged.
    /// <list type="number">
    /// <item><b>Camera</b>: glides (eased, GlideSeconds) to the LOSING fort and frames it at FortZoom while the bomber comes in;
    /// after the death wave it pushes in slowly until the end screen. RtsCamera.follow on a moving target, released at the end
    /// screen (or when the gate battery drives the camera).</item>
    /// <item><b>Death wave</b>: every Death event of the eliminated team in that tick (all robots, vehicles, cannons, buildings,
    /// the fort itself excluded) is taken out of ZWorld.Events before any reader sees it (Update order -49, right after ZGame's
    /// tick; LateUpdate -49 catches coroutine kills) and held. The doomed robots panic: most run away from the fort (run clip,
    /// facing the run, only over ground / road / shallow water, never into a building or rock footprint), the rest slump; vehicles
    /// and cannons stand. When the nuke lands (OpenZNukeStrike.Impacted), each one dies as the shock ring passes it (NukeFx ring
    /// 4 -> 95 m in 1.8 s, then 35 m/s): robots thrown away from the bomb (ragdoll / blast arc), then the held event is put back
    /// into ZWorld.Events so CombatView, AudioDirector, the HUD stats and the end screen handle it as a normal death. At most
    /// MaxDeathsPerFrame per frame (a building counts 3): no death storm in one frame. The turret-debris missiles of wave-killed
    /// vehicles and cannons are held until their turret has had time to fly.</item>
    /// <item><b>Quiet</b>: from the fort's fall only the nuke lines pass (AudioDirector's voice gate via OpenZVictory.MutesVoice);
    /// OpenZVictory then plays its lines strictly one after another once the wave is over.</item>
    /// <item><b>Fort gun</b>: FortGunView keeps its gun on the fort until the bomb lands (HoldFortToss), so the gun flies with the
    /// blast instead of 3 s before it.</item>
    /// <item><b>Prewarm</b> (the 0.8 s hitch): at level load the strike's plane, bomb and FX library are loaded, every building's
    /// fracture set, palette, debris materials, ruin and big parts are prepared (BuildingBreaker.Prepare, one building per frame),
    /// the strike / nuke / debris materials are drawn once at sub-pixel size (shader + pipeline-state warm-up, shadow and motion
    /// vector passes included) and the strike's audio clips are loaded.</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(-49)]
    public sealed class OpenZEnding : MonoBehaviour
    {
        public static OpenZEnding I { get; private set; }
        public static bool Enabled = true, AutoCreate = true, PrewarmOnLoad = true, DriveCamera = true, DeathWave = true, Flee = true;
        public static float GlideSeconds = 1.8f, FortZoom = 78f, PushInZoom = 62f, PushInSeconds = 10f, CamNorth = 9f;
        /// <summary>
        /// From the nuke's impact the view turns OrbitDegrees round the fort over OrbitSeconds (smootherstep), the fort kept where
        /// the glide framed it. The turn goes the way whose end frame shows less ground outside the map (OrbitSign: the four frame corners
        /// on the ground, at FortZoom); the camera stays 62-78 m up (the push-in), far above every cliff top. Released (end screen, the
        /// player pans), the view eases back to north-up.</summary>
        public static bool Orbit = true;
        public static float OrbitDegrees = 40f, OrbitSeconds = 10.5f;
        /// <summary>Last orbit (Play proof): its sign and the off-map metres of both candidate end frames.</summary>
        public static string OrbitInfo = "";
        public static float WaveOuterSpeed = 35f, WaveMax = 4.5f, WaveSafety = 8f, NoStrikeAfter = 0.6f;
        public static int MaxDeathsPerFrame = 5, BuildingCost = 3;
        public static float FleeShare = 0.72f, FleeSpeedMin = 2.6f, FleeSpeedMax = 3.7f;

        // ------------------------------------------------------------------------------------------------ public state
        /// <summary>UnitView (endings_v1 patch): this unit is dead in the sim but waits for the nuke's wave: keep it drawn.</summary>
        public static bool Holds(int id) => I && I.doomedIds.Contains(id);
        /// <summary>From the fort's fall until the last held death was released.</summary>
        public static bool WaveRunning => I && I.running && I.doomed.Count > 0;
        /// <summary>Game time of the last wave death (-100 before).</summary>
        public static float WaveEndTime => I ? I.waveEnd : -100f;
        /// <summary>Real seconds since the fort fell (pause excluded, slow motion counted as real time).</summary>
        public static float RealSinceFall => I && I.running ? I.realSinceFall : 0f;
        public static bool Running => I && I.running;
        /// <summary>
        /// sequence starts (TeamEliminated) until this scene is left; Replay / a new level / the menu load a new scene with a fresh
        /// OpenZEnding (running = false), so the lock lifts by itself. RtsCamera reads it. Test batteries (GateBattery) are exempt.</summary>
        public static bool LockCamera = true;
        public static bool LocksCamera => LockCamera && Enabled && I && I.running && !GateBattery.Running;
        /// <summary>FortGunView (endings_v1 patch): the gun stays on the fort until the bomb lands.</summary>
        public static bool HoldFortToss => Enabled && I && I.running && !I.impacted && OpenZNukeStrike.Enabled && I.realSinceFall < 12f + OpenZNukeStrike.HoldSeconds;                      // voice_lines_v3: + the nuke intro's hold
        /// <summary>OpenZVictory.MutesVoice (endings_v1 patch): from the fort's fall only the nuke / fort-lost lines may start.</summary>
        public static bool QuietVoices(string id) => Enabled && I && I.running && !(id != null && (id.Contains("nuke") || id == "ann_fort_lost"));
        /// <summary>OpenZVictory.Release: the end screen is up: the camera is the player's again, held deaths finish quickly.</summary>
        public static void OnEndScreen() { if (I) I.EndScreen(); }

        // ------------------------------------------------------------------------------------------------ internals
        sealed class Doomed
        {
            public ZEvent e; public ZObject o; public UnitView v; public Animator anim; public float dist0, jitter;
            public bool fleeInit, fleeing, slump, slumped, blocked; public Vector3 dir; public float speed, start, runFor, slumpAt;
            public int insideRect = -1;
        }

        ZGame game; CombatView combatView; bool lookedCv;
        bool running, impacted, flushing, endScreen; Team loser = Team.Neutral;
        Vector3 fortCentre, origin; float startTime, waveT0 = -1f, waveEnd = -100f, realSinceFall;
        readonly List<Doomed> doomed = new List<Doomed>();
        readonly HashSet<int> doomedIds = new HashSet<int>(), releasedIds = new HashSet<int>();
        readonly Dictionary<int, float> releasedAt = new Dictionary<int, float>();
        readonly List<ZEvent> heldDebris = new List<ZEvent>();
        readonly List<RectInt> blockRects = new List<RectInt>();
        int scannedUpTo, scanFrame = -1;
        float worstMs, worstAt, impactReal = -1f; bool worstLogged;                                  // hitch meter (README: verify)
        // ending profile (log only): one line a second for the first ProfileSeconds after the fort fell, so a slow ending can be read
        // from the player log: frame times, fixed updates and rule ticks per frame with the time they took, moving bodies, particles
        const float ProfileSeconds = 30f;
        long probeFixedStamp; double probeFixedMs, probeRuleMs, probeCpuMs, probeGpuMs; int probeFixed, probeFrames, probeTicks, probeTimed;
        float probeWorst, probeSum, probeNextLine = 1f; readonly FrameTiming[] probeTimings = new FrameTiming[1];
        // camera
        RtsCamera cam; Transform camTarget, camPrevFollow; Vector3 camFrom, camTo; float glide; bool camActive;
        float orbitT = -1f, orbitSign = 1f; Vector3 orbitCentre;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded; }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!AutoCreate || I) return;
            var g = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            if (!g) return;
            g.gameObject.AddComponent<OpenZEnding>();
        }

        void Awake()
        {
            if (I && I != this) { Destroy(this); return; }
            I = this; game = GetComponent<ZGame>(); if (!game) game = ZGame.I;
        }

        void OnEnable() { OpenZNukeStrike.Impacted -= OnImpact; OpenZNukeStrike.Impacted += OnImpact; }
        void OnDisable() { OpenZNukeStrike.Impacted -= OnImpact; ReleaseCamera(); }
        void OnDestroy() { if (I == this) I = null; if (camTarget) Destroy(camTarget.gameObject); }

        void Start() { if (PrewarmOnLoad) StartCoroutine(Prewarm()); }

        // ================================================================================================ frame
        void Update()
        {
            if (!game) game = ZGame.I;
            if (!Enabled || game == null || game.World == null) return;
            if (running && Time.timeScale > 0f) realSinceFall += Time.unscaledDeltaTime;
            if (running && realSinceFall > 0f && realSinceFall < 20f && Time.unscaledDeltaTime * 1000f > worstMs) { worstMs = Time.unscaledDeltaTime * 1000f; worstAt = realSinceFall; }
            if (running && !worstLogged && realSinceFall >= 12f) { worstLogged = true; LogWorst(); }
            if (running) StepProfile();
            Scan(true);
            if (!running) return;
            if (!impacted && waveT0 < 0f)
            {   // no strike (off, or it never began): the wave starts from the fort itself
                bool noStrike = Time.time - startTime > NoStrikeAfter && !OpenZNukeStrike.Active && OpenZNukeStrike.LastImpactTime < startTime;
                if (noStrike || Time.time - startTime > WaveSafety + OpenZNukeStrike.HoldSeconds) StartWave(fortCentre);   // voice_lines_v3: + the nuke intro's hold
            }
            float dt = Time.deltaTime;
            if (Flee && dt > 0f) StepFlee(dt);
            if (waveT0 >= 0f || flushing) ReleaseDue();
            ReleaseDebris();
            scannedUpTo = game.World.Events.Count;                                                // our re-injected events are never re-held
            StepCamera();
        }

        void LateUpdate()
        {
            if (!Enabled || game == null || game.World == null) return;
            Scan(false);                                                                          // kills made by coroutines this frame
        }

        // ================================================================================================ event interception
        void Scan(bool fromUpdate)
        {
            var ev = game.World.Events;
            if (!lookedCv) { lookedCv = true; combatView = FindFirstObjectByType<CombatView>(); }
            int from;
            if (fromUpdate) from = combatView && combatView.enabled ? 0 : (ev.Count < scannedUpTo ? 0 : scannedUpTo);   // CombatView clears the list every frame
            else from = scanFrame == Time.frameCount ? Mathf.Min(scannedUpTo, ev.Count) : 0;
            List<int> remove = null;
            for (int i = from; i < ev.Count; i++)
            {
                var e = ev[i];
                if (e.type == ZEventType.TeamEliminated)
                {
                    var team = (Team)(int)e.value;
                    if (!running && team != Team.Neutral) Begin(team, e);
                    continue;
                }
                if (!running || !DeathWave) continue;
                if (e.type == ZEventType.Death)
                {
                    var o = game.World.GetAny(e.objId);
                    if (o == null || o.Owner != loser || o.IsFort || releasedIds.Contains(o.Id) || doomedIds.Contains(o.Id) || waveEnd > 0f) continue;
                    if (o.Kind == ZKind.MapItem) continue;
                    Hold(e, o); (remove ??= new List<int>()).Add(i);
                }
                else if (e.type == ZEventType.MissileExploded && e.flag2 && (doomedIds.Contains(e.objId) || (releasedAt.TryGetValue(e.objId, out var ra) && Time.time - ra < 3f)))
                {   // a wave-killed vehicle / cannon's turret debris (sim: 3-4 s / 7-10 s after the death): lands after its turret flew
                    heldDebris.Add(e); (remove ??= new List<int>()).Add(i);
                }
            }
            if (remove != null) for (int k = remove.Count - 1; k >= 0; k--) ev.RemoveAt(remove[k]);
            scannedUpTo = ev.Count; scanFrame = Time.frameCount;
        }

        void Hold(ZEvent e, ZObject o)
        {
            var d = new Doomed { e = e, o = o, v = game.Views.TryGetValue(o.Id, out var v) ? v : null };
            d.jitter = ((o.Id * 0.6180339f) % 1f - 0.5f) * 0.12f;
            doomed.Add(d); doomedIds.Add(o.Id);
        }

        void Begin(Team team, ZEvent e)
        {
            running = true; loser = team; impacted = false; flushing = false; endScreen = false;
            startTime = Time.time; realSinceFall = 0f; waveT0 = -1f; waveEnd = -100f;
            worstMs = Time.unscaledDeltaTime * 1000f; worstAt = 0f; worstLogged = false; impactReal = -1f;
            probeNextLine = 1f; probeFixedStamp = 0; probeFrames = 0; probeSum = 0f; probeWorst = 0f; probeFixed = 0; probeFixedMs = 0; probeTicks = 0; probeRuleMs = 0; probeCpuMs = 0; probeGpuMs = 0; probeTimed = 0;
            var c = ZGame.PxToWorld(e.x, e.y); c.y = game.GroundY(c.x, c.z); fortCentre = c;
            var fort = game.World.GetAny(e.objId); if (fort != null) { fortCentre = game.CenterWorld(fort); }
            // footprints the fleeing robots never run into: buildings and rocks
            blockRects.Clear();
            foreach (var o in game.World.Objects)
                if (o.Kind == ZKind.Building || (o.Kind == ZKind.MapItem && o.Type == "rock" && !o.Destroyed))
                    blockRects.Add(new RectInt(o.LocX, o.LocY, Mathf.Max(8, o.WidthPix), Mathf.Max(8, o.HeightPix)));
            BeginCamera();
            Debug.Log($"[OpenZ] ending: {team} eliminated at {fortCentre}; camera {(camActive ? "glides to the fort" : "untouched")}");
        }

        void OnImpact(Vector3 at, bool ours)
        {
            if (!running || impacted) return;
            impacted = true; impactReal = realSinceFall;
            if (waveT0 < 0f) StartWave(at);
        }

        void StartWave(Vector3 at)
        {
            origin = at; waveT0 = Time.time;
            foreach (var d in doomed) d.dist0 = Flat(PosOf(d) - origin).magnitude;
            doomed.Sort((a, b) => a.dist0.CompareTo(b.dist0));
            Debug.Log($"[OpenZ] ending: death wave from {at}, {doomed.Count} held deaths (nearest {(doomed.Count > 0 ? doomed[0].dist0 : 0f):F0} m, farthest {(doomed.Count > 0 ? doomed[doomed.Count - 1].dist0 : 0f):F0} m)");
            if (doomed.Count == 0) waveEnd = Time.time;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        Vector3 PosOf(Doomed d)
        {
            if (d.v && d.v.gameObject.activeInHierarchy) return d.v.transform.position;
            return game.CenterWorld(d.o);
        }

        /// <summary>Seconds after the impact at which the shock ring passes `dist` (NukeFx ring: 4 + 91 (1 - (1 - t/1.8)^3)).</summary>
        public static float Delay(float dist)
        {
            if (dist <= 4f) return 0.02f + dist * 0.005f;
            if (dist <= 95f) { float x = (dist - 4f) / 91f; return 1.8f * (1f - Mathf.Pow(1f - x, 1f / 3f)); }
            return Mathf.Min(WaveMax, 1.8f + (dist - 95f) / Mathf.Max(1f, WaveOuterSpeed));
        }

        void ReleaseDue()
        {
            float el = waveT0 >= 0f ? Time.time - waveT0 : 0f;
            int budget = flushing ? 14 : MaxDeathsPerFrame, done = 0;
            for (int i = 0; i < doomed.Count; i++)
            {
                var d = doomed[i];
                if (!flushing)
                {
                    float dist = Flat(PosOf(d) - origin).magnitude;
                    if (el < Mathf.Max(0f, Delay(dist) + d.jitter)) continue;
                }
                int cost = d.o.Kind == ZKind.Building ? BuildingCost : 1;
                if (done > 0 && cost > budget) break;
                Release(d); doomed.RemoveAt(i); i--; done++; budget -= cost;
                if (budget <= 0) break;
            }
            if (doomed.Count == 0 && waveEnd < 0f) { waveEnd = Time.time; Debug.Log($"[OpenZ] ending: death wave over {Time.time - waveT0:F2} s after the impact"); }
        }

        void Release(Doomed d)
        {
            int id = d.o.Id;
            doomedIds.Remove(id); releasedIds.Add(id); releasedAt[id] = Time.time;
            if (d.o.Kind == ZKind.Robot && d.v && d.v.isActiveAndEnabled && !d.v.Dying)
            {
                var anim = d.v.GetComponentInChildren<Animator>(); if (anim) anim.speed = 1f;
                var p = d.v.transform.position;
                var blast = origin + Random.insideUnitSphere * 0.8f; blast.y = p.y - 2.5f;       // thrown out and up, away from the bomb
                d.v.Die(false, true, blast);
            }
            else if ((d.o.Kind == ZKind.Vehicle || d.o.Kind == ZKind.Cannon) && d.v && d.v.isActiveAndEnabled && !d.v.Dying)
                d.v.Die(false, true, d.v.transform.position);                                    // = CombatView's own call (idempotent), even if the event were lost
            var e = d.e; e.flag = false; e.flag2 = true;                                          // a blast death (CombatView / LegacyMatch / sounds)
            game.World.Events.Add(e);
        }

        void ReleaseDebris()
        {
            for (int i = heldDebris.Count - 1; i >= 0; i--)
            {
                var e = heldDebris[i];
                bool due = flushing || (!doomedIds.Contains(e.objId) && releasedAt.TryGetValue(e.objId, out var t) && Time.time - t >= 3.2f);
                if (!due) continue;
                game.World.Events.Add(e); heldDebris.RemoveAt(i);
            }
        }

        void EndScreen()
        {
            endScreen = true;
            ReleaseCamera();
            if (doomed.Count > 0 || heldDebris.Count > 0) flushing = true;                           // skipped mid-wave: finish quickly
        }

        // ================================================================================================ losers flee or slump
        void StepFlee(float dt)
        {
            float since = Time.time - startTime;
            foreach (var d in doomed)
            {
                if (d.o.Kind != ZKind.Robot || !d.v || !d.v.isActiveAndEnabled || d.v.Dying) continue;
                var tr = d.v.transform;
                if (!d.fleeInit) InitFlee(d, tr);
                if (since < d.start) continue;
                var anim = d.anim;
                if (d.slump || d.blocked || since - d.start > d.runFor)
                {
                    if (!d.slumped) { d.slumped = true; d.slumpAt = Time.time; Play(anim, d, "landing", "idle", 0.25f, 0.45f); }
                    else if (anim && Time.time - d.slumpAt > 0.5f && anim.speed > 0.06f) anim.speed = 0.04f;   // holds a cowering crouch
                    continue;
                }
                if (!d.fleeing) { d.fleeing = true; Play(anim, d, "run_forward", "walking", 0.15f, 1.15f); if (anim && anim.layerCount > 1) anim.SetLayerWeight(1, 0f); }
                var pos = tr.position;
                var px = ZGame.WorldToPx(pos);
                var cls = game.World.Map.ClassAt(px.x >> 4, px.y >> 4);
                float speed = d.speed * (cls == ZClass.Water ? 0.55f : 1f);
                var next = pos + d.dir * speed * dt;
                if (!Walkable(next, d)) { d.blocked = true; continue; }
                next.y = game.GroundY(next.x, next.z);
                tr.position = next;
                float yaw = Mathf.Atan2(d.dir.x, d.dir.z) * Mathf.Rad2Deg + UnitAim.ModelYaw(d.v.gameObject, d.o, 180f);
                tr.rotation = Quaternion.Slerp(tr.rotation, Quaternion.Euler(0f, yaw, 0f), 1f - Mathf.Exp(-dt * 10f));
            }
        }

        void InitFlee(Doomed d, Transform tr)
        {
            d.fleeInit = true;
            d.anim = d.v.GetComponentInChildren<Animator>();
            float h1 = (d.o.Id * 0.7548776f) % 1f, h2 = (d.o.Id * 0.5698403f + 0.17f) % 1f, h3 = (d.o.Id * 0.3819660f + 0.61f) % 1f;
            var away = Flat(tr.position - fortCentre);
            if (away.sqrMagnitude < 0.25f) away = new Vector3(Mathf.Cos(h1 * 6.283f), 0f, Mathf.Sin(h1 * 6.283f));
            d.dir = Quaternion.Euler(0f, (h2 - 0.5f) * 60f, 0f) * away.normalized;
            d.speed = Mathf.Lerp(FleeSpeedMin, FleeSpeedMax, h3);
            d.start = 0.15f + 0.75f * h2;                                                          // reaction time: never all at once
            d.runFor = 1.8f + 2.4f * h1;
            d.slump = h3 > FleeShare;
            var px = ZGame.WorldToPx(tr.position);
            for (int i = 0; i < blockRects.Count; i++) if (blockRects[i].Contains(new Vector2Int(px.x, px.y))) { d.insideRect = i; break; }
        }

        bool Walkable(Vector3 p, Doomed d)
        {
            var px = ZGame.WorldToPx(p);
            var cls = game.World.Map.ClassAt(px.x >> 4, px.y >> 4);
            if (cls == ZClass.Impassable || cls == ZClass.DeepWater) return false;
            var q = new Vector2Int(px.x, px.y);
            for (int i = 0; i < blockRects.Count; i++) if (i != d.insideRect && blockRects[i].Contains(q)) return false;
            return true;
        }

        static void Play(Animator anim, Doomed d, string want, string fallback, float fade, float speed)
        {
            if (!anim || !anim.isActiveAndEnabled) return;
            bool tough = d.o.Type == "tough";
            string s = Has(anim, want) ? want : Has(anim, fallback) ? fallback : tough && Has(anim, "bazooka_idle") ? "bazooka_idle" : null;
            if (s == null) return;
            anim.CrossFadeInFixedTime(s, fade, 0, ((d.o.Id * 0.137f) % 1f) * 0.3f);
            anim.speed = speed;
        }

        static bool Has(Animator a, string s) => a.HasState(0, Animator.StringToHash(s)) || a.HasState(0, Animator.StringToHash("Base Layer." + s));

        // ================================================================================================ camera
        void BeginCamera()
        {
            if (!DriveCamera || GateBattery.Running) return;
            cam = FindFirstObjectByType<RtsCamera>(); if (!cam) return;
            if (!camTarget) camTarget = new GameObject("OpenZ_EndingCamTarget").transform;
            camPrevFollow = cam.follow;
            camFrom = new Vector3(cam.focus.x, 0f, cam.focus.z);
            camTo = new Vector3(fortCentre.x, 0f, fortCentre.z + CamNorth);                            // the fort sits low in the frame, the mushroom above
            camTarget.position = camFrom; cam.follow = camTarget;
            cam.ZoomTo(FortZoom);
            glide = 0f; camActive = true; orbitT = -1f;
        }

        void StepCamera()
        {
            if (!camActive || !cam || !camTarget) return;
            if (GateBattery.Running || endScreen) { ReleaseCamera(); return; }
            if (orbitT >= 0f && cam.follow != camTarget) { ReleaseCamera(); return; }                // the player panned away: the view is theirs again
            if (Time.timeScale > 0f) glide += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(glide / Mathf.Max(0.1f, GlideSeconds));
            float e = k * k * k * (k * (k * 6f - 15f) + 10f);                                        // smootherstep: eases in and out
            camTarget.position = Vector3.Lerp(camFrom, camTo, e);
            if (Orbit && orbitT < 0f && waveT0 >= 0f && k >= 1f)
            {
                orbitT = 0f;
                orbitCentre = new Vector3(cam.focus.x, 0f, cam.focus.z - CamNorth);                    // the fort as framed (the clamp's offset kept)
                orbitSign = OrbitSign(orbitCentre);
                cam.yawHeld = true; cam.unclamped = true;
            }
            if (orbitT >= 0f)
            {
                if (Time.timeScale > 0f) orbitT += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(orbitT / Mathf.Max(1f, OrbitSeconds));
                float eo = u * u * u * (u * (u * 6f - 15f) + 10f);                                     // smootherstep: eased in and out
                float yaw = orbitSign * OrbitDegrees * eo;
                cam.yaw = yaw;
                camTarget.position = orbitCentre + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, CamNorth);
            }
            if (waveEnd > 0f)
            {   // the aftermath: a slow push-in on the ruin while the lines play
                float p = Mathf.Clamp01((Time.time - waveEnd) / Mathf.Max(0.5f, PushInSeconds));
                cam.ZoomTo(Mathf.Lerp(FortZoom, PushInZoom, p * p * (3f - 2f * p)));
            }
        }

        /// <summary>
        /// the map rectangle.</summary>
        float OrbitSign(Vector3 centre)
        {
            var c = cam ? cam.GetComponent<Camera>() : null;
            float fov = c ? c.fieldOfView : 32f, aspect = c ? c.aspect : 16f / 9f;
            float plus = OffMap(centre, OrbitDegrees, fov, aspect), minus = OffMap(centre, -OrbitDegrees, fov, aspect);
            float sign = minus + 0.5f < plus ? -1f : 1f;
            OrbitInfo = $"orbit {(sign > 0 ? "+" : "-")}{OrbitDegrees:0} deg over {OrbitSeconds:0} s round {centre}: off-map corner metres +{plus:0.0} / -{minus:0.0}";
            Debug.Log("[OpenZ] ending: " + OrbitInfo);
            return sign;
        }

        float OffMap(Vector3 centre, float yaw, float fov, float aspect)
        {
            float h = FortZoom, back = h / Mathf.Tan(cam.pitch * Mathf.Deg2Rad);
            var rot = Quaternion.Euler(cam.pitch, yaw, 0f);
            var focus = centre + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, CamNorth);
            var pos = focus + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, h, -back);
            float ty = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad), tx = ty * aspect, sum = 0f;
            foreach (var (u, v) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f), (0f, 1f) })
            {
                var d = rot * new Vector3(u * tx, v * ty, 1f);
                if (d.y > -1e-3f) { sum += 999f; continue; }
                var g = pos + d * (-pos.y / d.y);
                float ox = Mathf.Max(0f, Mathf.Max(-g.x, g.x - cam.mapSizeMeters.x)), oz = Mathf.Max(0f, Mathf.Max(g.z, -cam.mapSizeMeters.y - g.z));
                sum += Mathf.Sqrt(ox * ox + oz * oz);
            }
            return sum;
        }

        void ReleaseCamera()
        {
            if (cam) { cam.yawHeld = false; }
            if (!camActive) return;
            camActive = false;
            if (cam && cam.follow == camTarget) cam.follow = camPrevFollow;
        }

        // ================================================================================================ prewarm (the fort-death hitch)
        static readonly string[] WarmClips =
        {
            "v3-aircraft-engine-a", "legacy-nuclear-launch", "demo-flyby-whoosh", "v3-bomb-release-a", "nuke-descent-20260909",
            "legacy-nuke", "legacy-nuke-long", "explosion-v1-20260909", "explosion-v2-20260909", "explosion-v3-20260909", "v3-collapse-large-a",
            "v3-collapse-large-b", "building-collapse-20260909", "building-settle-20260909", "final-metal-land-a", "final-metal-land-b",
            "v3-collapse-small-a", "v3-collapse-small-b", "v3-collapse-medium-a", "v3-collapse-medium-b",
        };

        IEnumerator Prewarm()
        {
            yield return null; yield return null;
            while (game == null || game.World == null) { if (!game) game = ZGame.I; yield return null; }
            float t0 = Time.realtimeSinceStartup; int buildings = 0;
            // 1) the strike's and the nuke's assets (Resources.Load in the fort's death frame loaded the plane + bomb models and textures)
            var plane = Resources.Load<GameObject>("OpenZ/FX/airstrike_plane");
            var bomb = Resources.Load<GameObject>("OpenZ/FX/legacy_nuke");
            Resources.Load<GameObject>("OpenZ/FX/combat_fx");
            var nukeShader = Shader.Find("OpenZ/Nuke"); var addShader = Shader.Find("OpenZ/FxAdditive");
            yield return null;
            // 2) every building: fracture set, palette read-back, debris materials, ruin + big parts (one per frame)
            foreach (var kv in new List<KeyValuePair<int, GameObject>>(game.StaticViews))
            {
                if (!kv.Value || game.World == null) continue;
                var o = game.World.Get(kv.Key);
                if (o == null || o.Kind != ZKind.Building || o.IsBridge || o.Destroyed) continue;
                var br = BuildingBreaker.For(kv.Value, o);
                if (!br.IsPrepared) { br.Prepare(); buildings++; yield return null; }
            }
            // 3) audio data of the strike / collapse clips
            var ca = CombatAudio.I;
            if (ca) foreach (var n in WarmClips) { var c = ca.Clip(n); if (c && c.loadState == AudioDataLoadState.Unloaded) c.LoadAudioData(); }
            // 4) draw everything once, sub-pixel, in front of the camera (shader variants + pipeline states, shadow + motion passes)
            yield return WarmRender(plane, bomb, nukeShader, addShader);
            Debug.Log($"[OpenZ] ending prewarm: {buildings} building debris sets, strike assets, warm render, {WarmClips.Length} clips in {(Time.realtimeSinceStartup - t0) * 1000f:F0} ms (spread over frames)");
        }

        IEnumerator WarmRender(GameObject plane, GameObject bomb, Shader nukeShader, Shader addShader)
        {
            var c = Camera.main; if (!c) yield break;
            var root = new GameObject("OpenZ_Warmup").transform;
            var mats = new List<Material>();
            try
            {
                if (plane) Instantiate(plane, root, false);
                if (bomb) Instantiate(bomb, root, false);
                var sphere = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
                void Ball(Material m, bool shadows)
                {
                    if (!m || !sphere) return;
                    var g = new GameObject("warm_" + m.name); g.transform.SetParent(root, false);
                    g.AddComponent<MeshFilter>().sharedMesh = sphere;
                    var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
                    r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                }
                if (nukeShader) { var m = new Material(nukeShader); mats.Add(m); Ball(m, false); }
                if (addShader) { var m = new Material(addShader) { renderQueue = 3100 }; mats.Add(m); Ball(m, false); }
                foreach (var m in BuildingBreaker.WarmMaterials()) Ball(m, true);
                foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            }
            catch (System.Exception e) { Debug.LogWarning("[OpenZ] ending warm render: " + e.Message); }
            for (int f = 0; f < 4; f++)
            {
                if (!c) break;
                // 1e-5 scale, 0.6 m past the near plane: a few hundredths of a pixel, still submitted (and moving: motion vectors)
                root.SetPositionAndRotation(c.transform.position + c.transform.forward * (c.nearClipPlane + 0.6f) + c.transform.right * (f * 1e-4f), c.transform.rotation);
                root.localScale = Vector3.one * 1e-5f;
                yield return null;
            }
            Destroy(root.gameObject);
            foreach (var m in mats) Destroy(m);
        }

        void LogWorst() => Debug.Log($"[OpenZ] ending hitch meter: worst frame {worstMs:F0} ms at +{worstAt:F2} s after the fort fell (impact at +{impactReal:F2} s)");

        // ================================================================================================ ending profile (log only)
        void FixedUpdate()
        {
            if (!running || realSinceFall >= ProfileSeconds) return;
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (probeFixedStamp != 0) probeFixedMs += (now - probeFixedStamp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;   // one whole fixed update: scripts + physics step
            probeFixedStamp = now; probeFixed++;
        }

        /// <summary>Once a frame while the ending runs (after ZGame's rule ticks of the frame): adds the frame up and writes one line a second.</summary>
        void StepProfile()
        {
            if (realSinceFall >= ProfileSeconds + 1f) return;
            if (probeFixedStamp != 0)
            {   // the last fixed update of the frame ends where this Update begins; ZGame's rule ticks ran in between
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - probeFixedStamp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency - ZGame.RuleMsLastFrame;
                if (ms > 0) probeFixedMs += ms;
                probeFixedStamp = 0;
            }
            float frameMs = Time.unscaledDeltaTime * 1000f;
            probeFrames++; probeSum += frameMs; if (frameMs > probeWorst) probeWorst = frameMs;
            probeTicks += ZGame.RuleTicksLastFrame; probeRuleMs += ZGame.RuleMsLastFrame;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, probeTimings) > 0 && probeTimings[0].cpuFrameTime > 0)
            { probeCpuMs += probeTimings[0].cpuMainThreadFrameTime; probeGpuMs += probeTimings[0].gpuFrameTime; probeTimed++; }
            if (realSinceFall < probeNextLine) return;
            int bodies = 0, awake = 0, parts = 0;
            foreach (var b in FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)) { if (b.isKinematic) continue; bodies++; if (!b.IsSleeping()) awake++; }
            foreach (var p in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) parts += p.particleCount;
            float n = Mathf.Max(1, probeFrames);
            Debug.Log($"[OpenZ] ending profile +{probeNextLine:F0} s: {probeFrames} frames, average {probeSum / n:F0} ms, worst {probeWorst:F0} ms; per frame {probeFixed / n:F1} fixed updates taking {probeFixedMs / n:F1} ms and {probeTicks / n:F1} rule ticks taking {probeRuleMs / n:F1} ms"
                      + (probeTimed > 0 ? $"; main thread {probeCpuMs / probeTimed:F1} ms, graphics card {probeGpuMs / probeTimed:F1} ms" : "")
                      + $"; moving bodies {awake} of {bodies}, particles {parts}, largest time step {Time.maximumDeltaTime:F2} s");
            probeNextLine += 1f; probeFrames = 0; probeSum = 0f; probeWorst = 0f; probeFixed = 0; probeFixedMs = 0; probeTicks = 0; probeRuleMs = 0; probeCpuMs = 0; probeGpuMs = 0; probeTimed = 0;
        }

        /// <summary>an editor script: OpenZ.OpenZEnding.Status()</summary>
        public static string Status()
        {
            if (!I) return "OpenZEnding: none";
            var s = $"OpenZEnding: running {I.running}, loser {I.loser}, impacted {I.impacted}, held {I.doomed.Count}, released {I.releasedIds.Count}, debris held {I.heldDebris.Count}, wave end {I.waveEnd:F1}, real since fall {I.realSinceFall:F1} s, camera {(I.camActive ? "driven" : "free")}, worst frame {I.worstMs:F0} ms at +{I.worstAt:F2} s (impact +{I.impactReal:F2} s)";
            Debug.Log("[OpenZ] " + s);
            return s;
        }
    }
}
