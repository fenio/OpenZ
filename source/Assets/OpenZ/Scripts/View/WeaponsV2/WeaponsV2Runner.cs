using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using static OpenZ.WeaponsV2Logic;

namespace OpenZ
{
    /// <summary>
    /// BS01TypeKit (85) posed the gun, so every bolt, tracer, laser bolt, crack and rocket leaves the drawn muzzle socket and every
    /// back-blast the drawn tube's BackBlast socket. CombatView still decides WHEN (Zod's client fire clock on the sim's rolls, the
    /// <list type="bullet">
    /// <item>
    /// bolt1-4) from the real muzzle, the metal ping on vehicles, a small rifle kick.</item>
    /// <item>PSYCHO: a heavy machine gun. MG bursts (WeaponsV2Logic.MgBurst) with a tracer, a muzzle flash, a brass casing and a kick on
    /// EVERY round, sitting on the round onsets of the burst clip being heard; strobing muzzle light, impacts per round (sparks on
    /// hits, dust / splashes on misses, ricochets on armour).</item>
    /// <item>LASER: Z's moving laser dashes (elaser.cpp: 2 m dash at 300 px/s) as a Star Wars double tap: two 1.7 m HDR green bolts with
    /// white cores that bloom by day and night, a green muzzle glow + light, and a zap where they hit: green-white sparks, jagged
    /// </item>
    /// <item>SNIPER: aims deliberately (laser sight on the target + a scope glint for 0.6 s+ before each aimed shot, WeaponsV2Logic.
    /// SniperCadence), then a sharp crack: a big muzzle blast with a muzzle-brake side flash (dust kicked up when kneeling / prone),
    /// echo tail.</item>
    /// <item>
    /// + a smoke cone, dust where the jet hits the ground, ground light, back-blast whoosh), the smoking tube during Zod's aim hold,
    /// the first rocket held until the bazooka is raised and settled (RobotWeaponPose).</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(96)]
    public sealed class WeaponsV2Runner : MonoBehaviour
    {
        public static WeaponsV2Runner I { get; private set; }
        ZGame game; CombatView combat;

        // ------------------------------------------------------------------------------------------ tuning
        public static float LaserSpeedMul = 1f;          // x Zod's 37.5 m/s (elaser.cpp:9)
        public static float SniperTracerMps = CombatView.ZodBulletMps;
        public static float MgTracerMul = 1f;            // x Zod's 37.5 m/s (ebullet.cpp:7)
        public static readonly Color LaserCore = new Color(0.62f, 1f, 0.55f), LaserGlow = new Color(0.16f, 1f, 0.22f), LaserLight = new Color(0.35f, 1f, 0.4f);
        public static readonly Color MgTracer = new Color(1f, 0.86f, 0.42f), MgSleeve = new Color(1f, 0.55f, 0.18f);
        public static readonly Color SightRed = new Color(1f, 0.12f, 0.08f), GlintWhite = new Color(0.85f, 0.95f, 1f), SniperTrail = new Color(1f, 0.96f, 0.86f);
        static readonly Color SmokeGrey = new Color(0.62f, 0.60f, 0.56f);

        // ------------------------------------------------------------------------------------------ stats (WeaponsV2Check)
        public int GruntShots, MgBursts, MgRounds, Casings, LaserShots, LaserBolts, Zaps, SniperShots, SniperSkipped, SniperKillShots, Launches, Holds, HoldsReleased;
        public float MaxHoldMs, MaxOldSpawnError, SumOldSpawnError, SightFrames;
        /// <summary>H0 round 2 (GP3 Play proof): the speed each drawn round / bolt / crack actually flew at, per weapon (x = sum m/s, y = count, z = max).</summary>
        public readonly Dictionary<string, Vector3> DrawnMps = new Dictionary<string, Vector3>();
        public void NoteMps(string k, float v) { DrawnMps.TryGetValue(k, out var a); DrawnMps[k] = new Vector3(a.x + v, a.y + 1f, Mathf.Max(a.z, v)); }

        // ------------------------------------------------------------------------------------------ round flight (H0 round 3, GP3)
        /// <summary>The hitscan types whose rounds this runner draws (WeaponsV2.Shot).</summary>
        public static bool Draws(string type) => type == "grunt" || type == "psycho" || type == "laser" || type == "sniper";
        /// <summary>The speed (m/s) this runner draws a type's round at (Zod's 37.5 m/s x game speed x the per-weapon multiplier).</summary>
        public static float RoundSpeed(string type, float gameSpeed)
        {
            float gs = Mathf.Max(0.25f, gameSpeed), s = LegacyMatch.ShotSpeed(gs);
            return type == "sniper" ? SniperTracerMps * gs : type == "psycho" ? s * MgTracerMul : type == "laser" ? s * LaserSpeedMul : s;
        }
        /// <summary>Seconds from the shot until the drawn round's head reaches a point `dist` m from the muzzle = its impact frame.
        /// The draws below use exactly this, and BlastWave.BulletDelay throws a robot killed by the round with it.</summary>
        public static float HeadTime(string type, float dist, float gameSpeed) => HeadTimeAt(type, dist, RoundSpeed(type, gameSpeed));
        static float HeadTimeAt(string type, float dist, float speed)
        {
            switch (type)
            {
                case "sniper": { const float len = 2.6f; float start = Mathf.Min(len * 0.5f + 0.2f, dist * 0.5f); return Mathf.Max(0.02f, (dist - start - len * 0.5f) / speed); }
                case "psycho": { const float len = 1.1f; float start = Mathf.Min(len * 0.5f + 0.1f, dist * 0.5f); return Mathf.Max(0.02f, (dist - start - len * 0.5f) / speed); }
                case "laser": { float half = LaserBoltLength * 0.5f, start = Mathf.Min(half + 0.15f, dist * 0.5f); return Mathf.Max(0.02f, (dist - start - half) / speed); }
                default: return OpenZFxPlayer.ShotHeadTime(FxWeapon.Bolt, dist, speed);
            }
        }
        /// <summary>Where a unit's round leaves this frame: the drawn gun's Muzzle socket (RobotWeaponPose, as MuzzleNow), else the view's muzzle.</summary>
        public static Vector3 DrawnMuzzle(ZGame g, ZObject a)
        {
            if (g == null || a == null) return Vector3.zero;
            if (g.Views.TryGetValue(a.Id, out var v) && v && v.isActiveAndEnabled)
            {
                var pose = v.GetComponent<RobotWeaponPose>();
                if (pose && pose.Muzzle && pose.Weapon) return pose.Muzzle.position;
                return v.MuzzlePos();
            }
            return g.CenterWorld(a) + Vector3.up;
        }
        /// <summary>retry: rockets that left before this package's tube was raised onto the target (DV11 on: must stay 0).</summary>
        public int UnsteadyLaunches;
        public readonly Dictionary<int, float> FirstLaunchAt = new Dictionary<int, float>();   // unit id -> Time.time of its first rocket

        // ------------------------------------------------------------------------------------------ state
        sealed class Rig
        {
            public int id; public UnitView view; public RobotWeaponPose pose; public bool robot;
            public MgBurst mg; public SniperCadence snipe; public int snipeTarget = -1;
            public int lastTarget = -1; public Vector3 lastTo, lastOffset; public bool lastHit, lastDriver; public ZEvent lastEvent;
            public Vector3 fallbackFrom; public float smokeUntil = -100f, smokeNext, glintNext, dotNext, lastRicochet = -100f;
            public int sight = -1; public int round;
            public float engagedAt = -1f, releasedAt = -100f;
        }
        readonly Dictionary<int, Rig> rigs = new Dictionary<int, Rig>();
        struct PendingShot { public ZEvent e; public int a, t; public string type; public Vector3 to, oldFrom; public bool kill; }
        readonly List<PendingShot> shots = new List<PendingShot>(32);
        enum HitKind { Mg, LaserStrong, LaserWeak, Sniper }
        struct Hit { public float at; public Vector3 pos, dir; public HitKind kind; public int target, shooter; public bool hit, driver; public FxSurface surf; public bool anchored; public HitSurface.Anchor anc; }   // IM1: anchored on the body
        static Hit Anc(Hit h) { if (HitSurface.Claim(h.pos, out var a)) { h.anchored = true; h.anc = a; } return h; }
        Transform BodyOf(ZObject t) => t == null ? null : game.Views.TryGetValue(t.Id, out var v) && v ? v.transform : game.StaticViews.TryGetValue(t.Id, out var go) && go && go.activeInHierarchy ? go.transform : null;
        readonly List<Hit> hits = new List<Hit>(256);
        struct Volley { public float at; public int a; public Vector3 to; public bool hit; public int target; }
        readonly List<Volley> volleys = new List<Volley>(32);
        struct HeldMissile { public ZEvent e; public float at, maxWait; public int a; }
        readonly List<HeldMissile> held = new List<HeldMissile>(8);
        struct PendingLaunch { public ZEvent e; public int a; public Vector3 to, oldFrom; public float delay; }
        readonly List<PendingLaunch> launches = new List<PendingLaunch>(8);
        struct DelayedSound { public float at; public string key; public Vector3 pos; public float vol; public int emitter; public string[] bank; public float throttle; }
        readonly List<DelayedSound> sounds = new List<DelayedSound>(16);
        float nextAttach, nextSweep;
        bool channelsReady;
        static readonly string[][] MgClipNames = BuildMgNames();
        static string[][] BuildMgNames() { var a = new string[MgClips.Length][]; for (int i = 0; i < a.Length; i++) a[i] = new[] { MgClips[i].name }; return a; }
        static readonly string[] MetalPingBank = { "v3-armor-ricochet-a", "impact-1", "impact-2" };

        // ------------------------------------------------------------------------------------------ bootstrap
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded; }
        static void OnSceneLoaded(Scene s, LoadSceneMode m) { if (WeaponsV2.Enabled && (ZGame.I || FindFirstObjectByType<ZGame>())) Ensure(); }

        /// <summary>The runner of the ZGame scene (created on demand).</summary>
        public static WeaponsV2Runner Ensure(CombatView cv = null)
        {
            if (I && I.isActiveAndEnabled) { if (cv && !I.combat) I.combat = cv; return I; }
            if (!WeaponsV2.Enabled) return null;
            var g = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            if (!g) return null;
            var go = new GameObject("WeaponsV2Runner");
            if (g.gameObject.scene.IsValid() && go.scene != g.gameObject.scene) SceneManager.MoveGameObjectToScene(go, g.gameObject.scene);
            var r = go.AddComponent<WeaponsV2Runner>();
            r.game = g; r.combat = cv ? cv : g.GetComponent<CombatView>();
            return r;
        }

        void Awake()
        {
            if (I && I != this) { Destroy(gameObject); return; }
            I = this;
            if (!game) game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            channelsReady = System.Array.Exists(OpenZFxCatalog.Channels, c => c.name == "wv2_laser");
            if (!channelsReady) Debug.LogWarning("[weapons_v2] OpenZFxCatalog has no wv2_* channels (catalog edit C1 not applied): using the legacy fallback channels");
            BuildBeams();
        }

        void OnDestroy() { if (I == this) I = null; }

        float Speed => game ? Mathf.Max(0.25f, game.gameSpeed) : 1f;
        static bool Live(out OpenZFxPlayer z) { z = OpenZFxPlayer.I; return z && z.isActiveAndEnabled; }
        string Ch(string wv2, string fallback) => channelsReady ? wv2 : fallback;

        Rig RigFor(ZObject a, UnitView av)
        {
            if (!rigs.TryGetValue(a.Id, out var r)) { r = new Rig { id = a.Id, robot = a.Kind == ZKind.Robot }; rigs[a.Id] = r; }
            if (!r.view && av) r.view = av;
            if (!r.pose && r.robot && r.view) { r.pose = r.view.GetComponent<RobotWeaponPose>(); if (!r.pose) r.pose = RobotWeaponPose.Attach(r.view); }
            return r;
        }

        Vector3 AimPosOf(ZObject t)
        {
            if (t == null) return Vector3.zero;
            if (game.Views.TryGetValue(t.Id, out var v) && v && v.isActiveAndEnabled) return v.AimPos();
            return game.CenterWorld(t) + Vector3.up * (t.Kind == ZKind.Building ? 1.5f : 1f);
        }

        /// <summary>The muzzle of the drawn gun right now (after WeaponHold) and the barrel direction.</summary>
        Vector3 MuzzleNow(Rig r, Vector3 toward, out Vector3 dir)
        {
            Vector3 p;
            if (r.pose && r.pose.Muzzle && r.pose.Weapon && r.view && r.view.isActiveAndEnabled)
            {
                p = r.pose.Muzzle.position; dir = -r.pose.Space.forward;                           // WeaponHold: weapon.rotation = LookRotation(-aim)
                var want = toward - p;
                if (want.sqrMagnitude > 0.01f && Vector3.Dot(dir, want.normalized) < 0.2f) dir = want.normalized;   // grenade throw / odd clip frame
                return p;
            }
            p = r.view && r.view.isActiveAndEnabled ? r.view.MuzzlePos() : r.fallbackFrom;
            var d = toward - p; dir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward;
            return p;
        }

        /// <summary>A miss (CombatView scatters it around the target in the air) continues along the shot until it meets the ground.</summary>
        Vector3 GroundHit(Vector3 from, Vector3 to)
        {
            float gy = game.GroundY(to.x, to.z);
            if (to.y <= gy + 0.15f) return new Vector3(to.x, gy + 0.05f, to.z);
            var d = to - from;
            if (d.y < -0.02f)
            {
                var dir = d.normalized; var p = to;
                for (int i = 0; i < 20; i++) { p += dir * 0.5f; float g = game.GroundY(p.x, p.z); if (p.y <= g + 0.05f) return new Vector3(p.x, g + 0.05f, p.z); }
            }
            return new Vector3(to.x, gy + 0.05f, to.z);
        }

        FxSurface SurfaceOf(ZObject t, bool hit, Vector3 at, OpenZFxPlayer z)
        {
            if (hit && t != null) return t.Kind == ZKind.Robot ? FxSurface.Unit : t.Kind == ZKind.MapItem ? FxSurface.Rock : FxSurface.Metal;
            return z ? z.SurfaceAt(at) : FxSurface.Ground;
        }

        // ------------------------------------------------------------------------------------------ CombatView hooks (via WeaponsV2)
        public void QueueShot(ZEvent e, ZObject a, ZObject t, string type, UnitView av, Vector3 from, Vector3 to)
        {
            var r = RigFor(a, av); r.fallbackFrom = from;
            shots.Add(new PendingShot { e = e, a = a.Id, t = t.Id, type = type, to = to, oldFrom = from, kill = WeaponsV2.KillRoll(e, t) });
        }

        public bool SniperAllow(UnitView av, ZEvent e, ZObject t)
        {
            var a = av.Obj; if (a == null || t == null) return true;
            var r = RigFor(a, av);
            if (r.snipe == null) r.snipe = new SniperCadence(a.Id * 131 + 5);
            float now = Time.time;
            if (r.snipeTarget != t.Id) { r.snipeTarget = t.Id; r.snipe.Engage(now); }
            bool settling = r.pose && r.pose.GoingProne && (!r.pose.External || now - r.snipe.EngagedAt < SniperProneFirstAim);   // retry: lying down first
            bool kill = WeaponsV2.KillRoll(e, t);                                                      // review: the driver snipe shows at once too
            bool ok = r.snipe.Allow(now, kill, settling);
            if (ok) { if (kill) SniperKillShots++; } else SniperSkipped++;
            return ok;
        }

        public bool HoldTough(UnitView av, ZEvent e, ZObject a)
        {
            var r = RigFor(a, av);
            if (!r.pose || r.pose.Tough == null) return false;
            if (Time.time - r.releasedAt < 1f) return false;                                           // this rocket was held once already (UnitAim replays it)
            if (r.pose.External) return false;                                                         // review + retry: with robot_models_v2's BS01Stance, UnitAim holds it for tough_prepare: never two holds on one rocket
            float ready = r.pose.ToughReadyIn();
            if (ready < 0.03f) return false;
            if (ZWorld.ToughPrepareDeviation && r.pose.Tough.Raise01 >= 0.99f) return false;              // retry, DV11: the tube is on target (only the settle wobble left, 2-4x speed): never hold
            float flightReal = Mathf.Max(0.05f, e.value) / Speed, maxWait = Mathf.Min(ready, 0.6f * flightReal);
            if (maxWait < 0.05f) return false;
            held.Add(new HeldMissile { e = e, at = Time.time, maxWait = maxWait, a = a.Id });
            r.pose.Tough.Hurry(Time.time + maxWait * 0.85f);                                          // close target: the tube is on it when the rocket leaves
            Holds++;
            return true;
        }

        public bool QueueLaunch(UnitView av, ZEvent e, ZObject a, Vector3 to, float delay)
        {
            var r = RigFor(a, av);
            if (!r.pose || !r.pose.Muzzle || !r.pose.Weapon) return false;
            launches.Add(new PendingLaunch { e = e, a = a.Id, to = to, delay = delay, oldFrom = av.MuzzlePos() });
            return true;
        }

        // ------------------------------------------------------------------------------------------ frame
        void LateUpdate()
        {
            if (!game) { game = ZGame.I; if (!game) return; }
            if (game.World == null) return;
            if (!combat) combat = game.GetComponent<CombatView>();
            float now = Time.time;
            AttachPoses(now);
            ReleaseHeld(now);
            for (int i = 0; i < launches.Count; i++) DoLaunch(launches[i]);
            launches.Clear();
            for (int i = 0; i < shots.Count; i++) DoShot(shots[i]);
            shots.Clear();
            StepMg(now);
            StepVolleys(now);
            StepHits(now);
            StepSounds(now);
            StepSniperAim(now);
            StepToughSmoke(now);
            StepBeams(Time.deltaTime);
            Sweep(now);
        }

        void AttachPoses(float now)
        {
            if (now < nextAttach) return;
            nextAttach = now + 0.5f;
            foreach (var v in game.Views.Values)
            {
                if (!v || v.Obj == null || v.Obj.Kind != ZKind.Robot || !v.isActiveAndEnabled) continue;
                var ty = v.Obj.Type;
                if (ty != "grunt" && ty != "psycho" && ty != "sniper" && ty != "tough" && ty != "laser") continue;
                if (!v.GetComponent<RobotWeaponPose>()) RobotWeaponPose.Attach(v);
            }
        }

        void Sweep(float now)
        {
            if (now < nextSweep) return;
            nextSweep = now + 2f;
            List<int> gone = null;
            foreach (var kv in rigs)
            {
                var o = game.World.Get(kv.Key);
                bool busy = kv.Value.mg != null && (kv.Value.mg.InBurst || kv.Value.mg.Armed(now));
                if ((o == null || o.Destroyed) && !busy) (gone ??= new List<int>()).Add(kv.Key);
            }
            if (gone != null) foreach (var id in gone) { ReleaseSight(rigs[id]); rigs.Remove(id); }
        }

        // ------------------------------------------------------------------------------------------ tough
        void ReleaseHeld(float now)
        {
            for (int i = 0; i < held.Count; i++)
            {
                var h = held[i];
                float waited = now - h.at;
                rigs.TryGetValue(h.a, out var r);
                bool gone = r == null || !r.view || r.view.Dying || !r.view.isActiveAndEnabled;
                bool ready = !gone && r.pose && r.pose.ToughReadyIn() <= 0.01f && !r.pose.ExternalHoldsFire;   // review: also BS01Stance's tough_prepare (one combined hold)
                if (!(gone || ready || waited >= h.maxWait)) continue;
                held.RemoveAt(i--);
                if (r != null) r.releasedAt = now;
                var e = h.e; e.value = Mathf.Max(0.05f, e.value - waited * Speed);                    // the sim's detonation time is untouched
                MaxHoldMs = Mathf.Max(MaxHoldMs, waited * 1000f); HoldsReleased++;
                if (!combat) continue;
                WeaponsV2.replaying = true;
                try { combat.Replay(e); } finally { WeaponsV2.replaying = false; }
            }
        }

        void DoLaunch(PendingLaunch L)
        {
            if (!rigs.TryGetValue(L.a, out var r) || !r.pose || !r.view)
            {   // sound_v3 (audit: a Tough killed / despawned right as it fires gave a blast from nowhere): rocket + launch + flight from the old spawn
                var a0 = game.World.GetAny(L.a);
                if (a0 != null && OzSoundV3.Enabled) { LegacyMatch.Launch(a0, "tough", L.oldFrom, L.to, L.delay, FxManager.I); LegacyMatch.LaunchSound("tough", L.oldFrom, L.a, CombatAudio.I); LegacyMatch.RocketFlight(L.oldFrom, L.to, L.delay, L.a, CombatAudio.I); }
                return;
            }
            var a = game.World.GetAny(L.a); if (a == null) return;
            var pose = r.pose; float s = pose.Scale;
            var muzzle = pose.Muzzle.position;
            var back = pose.BackBlast ? pose.BackBlast.position : muzzle + pose.Space.forward * (1.45f * s);
            var axis = muzzle - back; var dir = axis.sqrMagnitude > 1e-4f ? axis.normalized : -pose.Space.forward;
            var from = muzzle + dir * (0.1f * s);
            float err = Vector3.Distance(L.oldFrom, from); MaxOldSpawnError = Mathf.Max(MaxOldSpawnError, err); SumOldSpawnError += err;
            if (!pose.External && pose.Tough != null && pose.Tough.Raise01 < 0.99f) UnsteadyLaunches++;
            if (!FirstLaunchAt.ContainsKey(L.a)) FirstLaunchAt[L.a] = Time.time;
            if (!r.view.Dying)
            {   // review: a rocket released after its Tough died (held / flushed) still flies, but never re-animates the corpse
                r.view.OnFire();                                                                      // bazooka_fire + UnitView recoil + UnitAim
                pose.OnToughFire();                                                                   // tube climb + shove, body thrown back
            }
            var fx = FxManager.I; var au = CombatAudio.I;
            OpenZFxPlayer.NextSimEnd = L.e.end;
            try { LegacyMatch.Launch(a, "tough", from, L.to, L.delay, fx); }
            finally { OpenZFxPlayer.NextSimEnd = -1; }
            var rp = WeaponsV2.Pick(WeaponsV2.BankRocket);
            if (au) au.PlayPitched("rocket", from, WeaponsV2.GainRocket * WeaponsV2.EvenGain(rp, WeaponsV2.BankRocket, "rocket"), 0.2f, L.a, 1f, rp);
            LegacyMatch.RocketFlight(from, L.to, L.delay, L.a, au);
            if (r.view.Dying || !ToughWarhead.Fired(r.view, a)) OzSoundV3.ToughReload(from, L.a);
            BackBlast(back, -dir, s);
            var bp = WeaponsV2.Pick(WeaponsV2.BankBackblast);
            if (au) au.PlayPitched("backblast", back, WeaponsV2.GainBackblast * WeaponsV2.EvenGain(bp, WeaponsV2.BankBackblast), 0.3f, L.a, 1f, bp);
            r.smokeUntil = Time.time + 1.0f; r.smokeNext = Time.time + 0.12f;
            Launches++;
        }

        void BackBlast(Vector3 back, Vector3 backwards, float s)
        {
            LegacyMatch.BackBlast(back, -backwards, FxManager.I);
            if (!Live(out var z)) return;
            for (int i = 0; i < 7; i++)
            {
                var v = backwards * Random.Range(3f, 6.5f) + Random.insideUnitSphere * 1.3f + Vector3.up * Random.Range(0.2f, 0.8f);
                z.EmitChannel("m12_smoke", back + backwards * (0.15f * i * s), v, Random.Range(0.8f, 1.3f), Random.Range(0.55f, 0.95f) * s, SmokeGrey, 0.54f);
            }
            for (int i = 0; i < 2; i++) z.EmitChannel("m12_flame", back + backwards * 0.2f * s, backwards * Random.Range(4f, 6f), 0.12f, 0.45f * s, OpenZFxCatalog.ExhaustColor, 1f);
            z.AddLight(back + backwards * 0.4f * s, new Color(1f, 0.55f, 0.2f), 7f, 5.5f, 0.14f, 2.2f, 0f, true);
            float gy = game.GroundY(back.x, back.z);
            if (back.y - gy < 3.2f * s)
            {   // the jet hits the ground behind the Tough
                var gp = back + backwards * (1.3f * s); gp.y = game.GroundY(gp.x, gp.z) + 0.1f;
                if (z.WetAt(gp)) z.Droplets(gp, 0.35f, 6); else z.Dust(gp, 0.6f);
                z.GroundLight(new Vector3(gp.x, z.SurfaceY(gp), gp.z), 0.55f);
            }
        }

        void StepToughSmoke(float now)
        {
            if (!Live(out var z)) return;
            foreach (var r in rigs.Values)
            {
                if (now > r.smokeUntil || now < r.smokeNext || !r.pose || !r.pose.Muzzle) continue;
                r.smokeNext = now + 0.07f;
                float k = Mathf.Clamp01((r.smokeUntil - now) / 1.0f), s = r.pose.Scale;
                z.EmitChannel("leg_smoke", r.pose.Muzzle.position, Vector3.up * Random.Range(0.4f, 0.8f) + Random.insideUnitSphere * 0.15f,
                    Random.Range(0.7f, 1.1f), (0.16f + 0.1f * k) * s, OpenZFxCatalog.MuzzleSmoke, 0.45f + 0.35f * k);
            }
        }

        // ------------------------------------------------------------------------------------------ hitscan
        void DoShot(PendingShot S)
        {
            var a = game.World.GetAny(S.a); var t = game.World.GetAny(S.t);
            if (a == null || !rigs.TryGetValue(S.a, out var r)) return;
            if (r.robot && r.engagedAt < 0f) r.engagedAt = Time.time;
            switch (S.type)
            {
                case "grunt": Grunt(r, S, a, t); break;
                case "psycho": ArmMg(r, S, a, t); break;
                case "laser": Laser(r, S, a, t); break;
                case "sniper": Sniper(r, S, a, t); break;
            }
        }

        void Grunt(Rig r, PendingShot S, ZObject a, ZObject t)
        {
            var from = MuzzleNow(r, S.to, out _);
            if (r.robot && r.view) { r.view.OnFire(); if (r.pose) r.pose.OnRound(2.5f, 0f); }
            NoteMps("grunt bolt", LegacyMatch.ShotSpeed(Speed));
            if (Live(out var z)) { float fl = z.Shot(from, S.to, FxWeapon.Bolt, LegacyMatch.SurfaceOf(t, S.e.flag), LegacyMatch.ShotSpeed(Speed)); if (S.kill) BlastWave.NoteDrawn(S.a, t, fl); }
            else FxManager.I?.Bullet(from, S.to);
            if (CombatAudio.I) CombatAudio.I.PlayPitched("bolt", from, LegacyMatch.GainBolt, 0.2f, S.a, 1f, WeaponsV2.Pick(LegacyMatch.BankBolt));
            MetalPing(S, t, from);
            GruntShots++;
        }

        /// <summary></summary>
        void MetalPing(PendingShot S, ZObject t, Vector3 from)
        {
            if (OpenZHitSparks.Active) return;
            if (t == null || !S.e.flag || S.e.flag2 || t.Kind == ZKind.Robot || t.Kind == ZKind.MapItem || Random.value >= 0.3f) return;
            float delay = Vector3.Distance(from, S.to) / LegacyMatch.ShotSpeed(Speed);
            sounds.Add(new DelayedSound { at = Time.time + Mathf.Min(delay, 1.5f), key = "metal_ping", pos = S.to, vol = 0.32f, emitter = t.Id, bank = MetalPingBank, throttle = 0.7f });
        }

        // ---- psycho machine gun
        void ArmMg(Rig r, PendingShot S, ZObject a, ZObject t)
        {
            float now = Time.time;
            if (r.mg == null) r.mg = new MgBurst(a.Id * 97 + 3);
            if (!Live(out _))
            {
                LegacyMatch.Shot(S.e, t, "psycho", MuzzleNow(r, S.to, out _), S.to, FxManager.I, CombatAudio.I); return;
            }
            bool newTarget = r.lastTarget != S.t;                                                     // review: read before it is overwritten (was always false)
            r.mg.Arm(now); r.lastEvent = S.e; r.lastTarget = S.t; r.lastHit = S.e.flag; r.lastDriver = S.e.flag2; r.lastTo = S.to;
            if (S.e.flag && t != null && !t.Destroyed) r.lastOffset = S.to - AimPosOf(t);           // the hit point on the body (misses keep the last one)
            else if (newTarget) r.lastOffset = Vector3.zero;                                           // a miss on a new target: aim at its centre, not the old body's hit point
            if (S.kill) MgRound(r, now, true);                                                        // the killing roll shows at once, as a HIT (H0 round 3: it was drawn as a miss beside the dead target, and not at all inside a burst)
        }

        void StepMg(float now)
        {
            if (!Live(out _)) return;
            foreach (var r in rigs.Values)
            {
                if (r.mg == null) continue;
                if (r.view && (r.view.Dying || r.view.Reacting)) { r.mg.ArmedUntil = -1f; r.mg.Clip = -1; continue; }   // review: a dead psycho stops firing (rounds came out of the ragdoll up to ~1 s); rebase: so does a knocked-down one (deaths_mech_v1 draws its held shots when it is up)
                int started = r.mg.Step(now, out int due);
                if (started >= 0)
                {
                    MgBursts++;
                    var au = CombatAudio.I;
                    if (au) au.PlayPitched("mg", MuzzleNow(r, r.lastTo, out _), WeaponsV2.GainMg, 0f, r.id, 1f, MgClipNames[started]);
                }
                for (int i = 0; i < due; i++) MgRound(r, now);
            }
        }

        void MgRound(Rig r, float now, bool kill = false)
        {
            if (!Live(out var z)) return;
            var t = game.World.GetAny(r.lastTarget);
            bool alive = t != null && !t.Destroyed && t.KillAt < 0;
            Vector3 chest = default; bool crew = !kill && alive && CrewAim.Point(t, out chest);
            Vector3 aimAt = crew ? chest : alive ? AimPosOf(t) + r.lastOffset : r.lastTo;
            var from = MuzzleNow(r, aimAt, out var dir);
            if ((r.round & 1) == 1 && r.pose && r.pose.MuzzleB && r.pose.Muzzle && r.view && r.view.isActiveAndEnabled)
                from += r.pose.MuzzleB.position - r.pose.Muzzle.position;                               // review: twin-barrel MG, every 2nd round from the lower barrel (Muzzle_B)
            float s = r.pose ? r.pose.Scale : 1f;
            bool hit = kill || (alive && Random.value < 0.65f);                                       // psycho damage_chance .65 (zsettings); cosmetic rounds; the killing round hits
            Vector3 to;
            if (crew) to = CrewAim.Impact(from, chest, hit);                                          // on his chest from any side (no hull test), misses fly past him
            else if (kill) to = r.lastTo;                                                              // CombatView's point on the body (the sim just killed it)
            else if (hit) { to = aimAt + Random.insideUnitSphere * 0.28f; var body = BodyOf(t); if (body && HitSurface.OnBody(body, from, to, out var hp, out _)) to = hp; }   // IM1: on the real surface
            else
            {
                var off = Random.insideUnitCircle.normalized * Random.Range(0.7f, 2.2f);
                to = HitSurface.MissGround(from, aimAt + new Vector3(off.x, 0f, off.y));                // IM1: where the line lands
            }
            var d = to - from; float dist = d.magnitude; if (dist < 0.1f) return;
            var sd = d / dist;
            float speed = LegacyMatch.ShotSpeed(Speed) * MgTracerMul; NoteMps("psycho tracer", speed);
            float len = 1.1f, start = Mathf.Min(len * 0.5f + 0.1f, dist * 0.5f), life = HeadTimeAt("psycho", dist, speed);
            life = z.EmitRound(Ch("wv2_mg_tracer", "leg_tracer"), "leg_tracer_sleeve", from, to, start, speed, life, 0.075f, MgTracer, 1f, 0.2f, MgSleeve, 0.22f);
            if (kill) BlastWave.NoteDrawn(r.id, t, life);
            // muzzle: a short hot flash + cone every round, sparks on every 2nd, a strobing light every 3rd
            // small dot next to the grunt's 1.3 m bolt disc, so the heavier gun looked like it fired the weaker flash. Now a bigger,
            // flickering flash (0.5 m +-15 %), a longer cone and the muzzle-brake side flares of the v2 MG on every round.
            float flick = Random.Range(0.85f, 1.15f);
            z.EmitChannel("m12_core", from, Vector3.zero, 0.05f, 0.5f * s * flick, OpenZFxCatalog.M12Core.color, 1f);
            z.EmitChannel("leg_cone", from + dir * 0.35f * s, dir * 0.05f, Random.Range(0.035f, 0.07f), 0.3f * s * flick, OpenZFxCatalog.MuzzleConeColor, 0.9f);
            var brake = Vector3.Cross(Vector3.up, dir); brake = brake.sqrMagnitude > 1e-6f ? brake.normalized : Vector3.right;
            z.EmitChannel("leg_cone", from + brake * 0.14f * s, brake * 0.05f, 0.04f, 0.14f * s, OpenZFxCatalog.MuzzleConeColor, 0.8f);
            z.EmitChannel("leg_cone", from - brake * 0.14f * s, -brake * 0.05f, 0.04f, 0.14f * s, OpenZFxCatalog.MuzzleConeColor, 0.8f);
            r.round++;
            if ((r.round & 1) == 0)
                for (int i = 0; i < 2; i++)
                    z.EmitChannel("leg_spark_light", from, dir * Random.Range(1.5f, 3.2f) + Random.insideUnitSphere * 1.1f + Vector3.up * 0.6f, Random.Range(0.12f, 0.24f), 0.2f, OpenZFxCatalog.MuzzleSparkA, 1f);
            if (r.round % 3 == 0)
            {
                z.AddLight(from + dir * 0.2f, OpenZFxCatalog.LegLightColor, 4.2f, 4f, 0.07f);
                z.GroundLight(new Vector3(from.x, z.SurfaceY(from), from.z), 0.3f);
            }
            if (r.round % 4 == 0) z.EmitChannel("leg_smoke", from, -dir * 0.3f + Vector3.up * 0.5f, 0.5f, 0.14f * s, OpenZFxCatalog.MuzzleSmoke, 0.6f);
            // a brass casing out of the right side of the receiver on every round
            if (r.pose && r.pose.Weapon)
            {
                var w = r.pose.Space; var port = w.TransformPoint(r.pose.EjectLocal);
                // barrel direction), never assumed from the weapon node's axes (a v2 FBX node with another basis would throw it forward)
                var bore = r.pose.Muzzle ? r.pose.Muzzle.position : from;
                var gunRight = Vector3.Cross(Vector3.up, dir); gunRight = gunRight.sqrMagnitude > 1e-6f ? gunRight.normalized : -w.right;
                float sideSign = Vector3.Dot(port - bore, gunRight) < -0.01f ? -1f : 1f;
                var right = gunRight * sideSign;
                var v = right * Random.Range(1.6f, 2.6f) + Vector3.up * Random.Range(1.4f, 2.4f) - dir * Random.Range(0.1f, 0.6f) + Random.insideUnitSphere * 0.3f;
                z.EmitChannel(Ch("wv2_casing", "m12_metal"), port, v, Random.Range(1.8f, 2.6f), 0.26f * s, Color.white, 1f);
                Casings++;
            }
            // jolt that restarts from 0 on each call) only on every 2nd round, ~6 / s = the live Zod client cadence it had before: at
            // 11.5 / s the whole robot, feet included, sawtoothed 3 cm at 12 Hz (a skating shiver the legs gate would flag)
            if (r.robot && r.view) { if ((r.round & 1) == 1) r.view.OnFire(); if (r.pose) r.pose.OnRound(1.8f, 0f); }
            hits.Add(Anc(new Hit { at = now + life, pos = to, dir = sd, kind = HitKind.Mg, target = hit && t != null ? t.Id : -1, shooter = r.id, hit = hit, surf = SurfaceOf(t, hit, to, z) }));
            MgRounds++;
        }

        // ---- laser
        void Laser(Rig r, PendingShot S, ZObject a, ZObject t)
        {
            if (!Live(out var z))
            {
                if (FxManager.I) FxManager.I.Laser(MuzzleNow(r, S.to, out _), S.to);
                return;
            }
            var from = MuzzleNow(r, S.to, out var dir);
            var to0 = S.e.flag ? S.to : GroundHit(from, S.to);
            if (r.robot && r.view) { r.view.OnFire(); if (r.pose) r.pose.OnRound(1.4f, 0f); }
            float s = r.pose ? r.pose.Scale : 1f;
            // green muzzle glow + light (lights the robot and the ground)
            z.EmitChannel(Ch("wv2_glow", "leg_cloud"), from + dir * 0.1f, dir * 0.3f, 0.14f, 1.0f * s, LaserGlow, 1f);
            z.EmitChannel("m12_core", from, Vector3.zero, 0.07f, 0.32f * s, LaserCore, 1f);
            z.AddLight(from + dir * 0.3f, LaserLight, 6f, 4.8f, 0.14f);
            LaserBolt(r, from, dir, to0, S.e.flag, S.t, true, S.kill);
            for (int k = 1; k < LaserBoltsPerShot; k++)
                volleys.Add(new Volley { at = Time.time + LaserBoltGap * k, a = r.id, to = S.to + Random.insideUnitSphere * 0.25f, hit = S.e.flag, target = S.t });
            var au = CombatAudio.I;
            var lp = WeaponsV2.Pick(WeaponsV2.BankLaserFire);
            if (au) au.PlayPitched("laser", from, WeaponsV2.GainLaserFire * WeaponsV2.EvenGain(lp, WeaponsV2.BankLaserFire), 0.1f, a.Id, 1f, lp);
            LaserShots++;
        }

        void StepVolleys(float now)
        {
            for (int i = 0; i < volleys.Count; i++)
            {
                var v = volleys[i]; if (now < v.at) continue;
                volleys.RemoveAt(i--);
                if (!rigs.TryGetValue(v.a, out var r) || !Live(out var z)) continue;
                if (r.view && (r.view.Dying || r.view.Reacting)) continue;                             // rebase: no second laser bolt out of a knocked-down robot
                var t = game.World.GetAny(v.target);
                // (halfway to a tank's centre put the second zap up to ~0.75 m inside the hull, hidden) and a miss stays a miss
                var to = v.hit && t != null && !t.Destroyed && t.Kind == ZKind.Robot && game.Views.TryGetValue(t.Id, out var tv) && tv ? v.to + (AimPosOf(t) - v.to) * 0.5f : v.to;
                var from = MuzzleNow(r, to, out var dir);
                if (!v.hit) to = GroundHit(from, to);
                z.EmitChannel(Ch("wv2_glow", "leg_cloud"), from + dir * 0.1f, dir * 0.3f, 0.1f, 0.7f * (r.pose ? r.pose.Scale : 1f), LaserGlow, 0.8f);
                LaserBolt(r, from, dir, to, v.hit, v.target, false);
            }
        }

        void LaserBolt(Rig r, Vector3 from, Vector3 dir, Vector3 to, bool hit, int target, bool first, bool kill = false)
        {
            if (!Live(out var z)) return;
            var d = to - from; float dist = d.magnitude; if (dist < 0.2f) return;
            var sd = d / dist;
            float speed = LegacyMatch.ShotSpeed(Speed) * LaserSpeedMul, len = LaserBoltLength, half = len * 0.5f; NoteMps("laser bolt", speed);
            float start = Mathf.Min(half + 0.15f, dist * 0.5f), life = HeadTimeAt("laser", dist, speed);
            if (kill) BlastWave.NoteDrawn(r.id, game.World.GetAny(target), life);
            if (channelsReady) life = z.EmitRound("wv2_laser", "wv2_laser_glow", from, to, start, speed, life, 0.13f, LaserCore, 1f, 0.5f, LaserGlow, 0.9f);
            else life = z.EmitRound("m12_bolt", null, from, to, start, speed, life, OpenZFxCatalog.BoltWidth, LaserGlow, 1f);
            var t = game.World.GetAny(target);
            hits.Add(Anc(new Hit { at = Time.time + life, pos = to, kind = first ? HitKind.LaserStrong : HitKind.LaserWeak, target = target, shooter = r.id, hit = hit, surf = SurfaceOf(t, hit, to, z) }));
            LaserBolts++;
        }

        void Zap(Vector3 at, bool strong, bool hit, int target, FxSurface surf)
        {
            if (!Live(out var z)) return;
            if (surf == FxSurface.Water) { var w = new Vector3(at.x, z.SurfaceY(at), at.z); z.Droplets(w, 0.25f, 5); z.Ripple(w, 0.3f); }
            float k = strong ? 1f : 0.55f;
            z.EmitChannel(Ch("wv2_glow", "leg_cloud"), at, Vector3.up * 0.2f, 0.18f, 1.5f * k, LaserGlow, 1f);
            z.EmitChannel("m12_core", at, Vector3.zero, 0.08f, 0.4f * k, LaserCore, 1f);
            int n = strong ? 11 : 5;
            for (int i = 0; i < n; i++)
            {
                var v = Random.onUnitSphere * Random.Range(2f, 5.5f); v.y = Mathf.Abs(v.y) * 0.8f + 1.2f;
                z.EmitChannel(Ch("wv2_spark", "leg_impact_spark"), at, v, Random.Range(0.22f, 0.45f), 0.055f, i % 3 == 0 ? Color.white : LaserCore, 1f);
            }
            if (strong && hit) { Arc(at, 1.1f); Arc(at, 0.8f); if (Random.value < 0.6f) Arc(at, 0.6f); }
            else if (strong) Arc(at, 0.6f);
            z.AddLight(at + Vector3.up * 0.4f, LaserLight, strong ? 7.5f : 4f, strong ? 5.5f : 3.5f, strong ? 0.2f : 0.12f, 2.2f, 0.8f);
            if (!hit && surf != FxSurface.Water) { z.Dust(at, 0.14f); if (strong) z.Scorch(at, 0.22f, 0); }
            var au = CombatAudio.I;
            if (au && strong) { var zp = WeaponsV2.Pick(WeaponsV2.BankLaserZap); au.PlayPitched("laser_hit", at, WeaponsV2.GainLaserZap * WeaponsV2.EvenGain(zp, WeaponsV2.BankLaserZap) * (hit ? 1f : 0.55f), 0.12f, target, 1f, zp); }
            Zaps++;
        }

        // ---- sniper
        void Sniper(Rig r, PendingShot S, ZObject a, ZObject t)
        {
            var au = CombatAudio.I;
            if (!Live(out var z)) { LegacyMatch.Shot(S.e, t, "sniper", MuzzleNow(r, S.to, out _), S.to, FxManager.I, au); return; }
            var from = MuzzleNow(r, S.to, out var dir);
            float s = r.pose ? r.pose.Scale : 1f;
            if (r.robot && r.view) { r.view.OnFire(); if (r.pose) r.pose.OnRound(6.5f, 0f); }
            z.Muzzle(from, dir, FxWeapon.Sniper);
            var side = Vector3.Cross(Vector3.up, dir).normalized;                                    // muzzle brake: side jets
            z.EmitChannel("leg_cone", from + side * 0.25f * s, side * 0.05f, 0.07f, 0.22f * s, OpenZFxCatalog.MuzzleConeColor, 0.9f);
            z.EmitChannel("leg_cone", from - side * 0.25f * s, -side * 0.05f, 0.07f, 0.22f * s, OpenZFxCatalog.MuzzleConeColor, 0.9f);
            z.EmitChannel("m12_core", from + dir * 0.15f, Vector3.zero, 0.07f, 0.55f * s, OpenZFxCatalog.M12Core.color, 1f);
            z.AddLight(from + dir * 0.3f, OpenZFxCatalog.LegLightColor, 9f, 6f, 0.11f, 2.2f, 0f, true);
            if (r.pose && r.pose.Current != Stance.Stand && r.pose.PoseWeight > 0.5f)
            {   // kneeling / prone: the blast kicks dust off the ground under the muzzle
                var gp = from + dir * 0.6f * s; gp.y = game.GroundY(gp.x, gp.z) + 0.05f;
                if (!z.WetAt(gp)) z.Dust(gp, 0.5f);
            }
            var to = S.e.flag ? S.to : GroundHit(from, S.to);
            var d = to - from; float dist = d.magnitude;
            if (dist > 0.2f)
            {
                var sd = d / dist;
                float speed = SniperTracerMps * Speed, len = 2.6f, start = Mathf.Min(len * 0.5f + 0.2f, dist * 0.5f), life = HeadTimeAt("sniper", dist, speed);
                bool bent = ShotClearance.Bend(from, to, out var bw);
                life = z.EmitRound(Ch("wv2_sniper_tracer", "leg_tracer"), null, from, to, start, speed, life, 0.07f, SniperTrail, 1f);
                if (S.kill) BlastWave.NoteDrawn(S.a, t, life);
                NoteMps("sniper crack", speed);
                if (bent) { Trail(from + sd * 0.3f, bw, Vector3.Distance(from, bw) / speed, 0.5f, 0.07f * Mathf.Max(1f, s * 0.8f)); Trail(bw, to, life, 0.5f, 0.07f * Mathf.Max(1f, s * 0.8f)); }
                else Trail(from + sd * 0.3f, to, dist / speed, 0.5f, 0.07f * Mathf.Max(1f, s * 0.8f));
                // H0 round 3: the impact (sparks, ping) comes when the crack's head arrives (was dist / speed: 0.075 s after the drawn round had gone)
                hits.Add(Anc(new Hit { at = Time.time + life, pos = to, dir = sd, kind = HitKind.Sniper, target = t != null ? t.Id : -1, shooter = r.id, hit = S.e.flag,
                                   driver = S.e.flag && S.e.flag2, surf = SurfaceOf(t, S.e.flag, to, z) }));
            }
            if (au)
            {
                var sp = WeaponsV2.Pick(WeaponsV2.BankSniper); var ep = WeaponsV2.Pick(WeaponsV2.BankSniperEcho);
                au.PlayPitched("sniper", from, WeaponsV2.GainSniper * WeaponsV2.EvenGain(sp, WeaponsV2.BankSniper), 0f, a.Id, 1f, sp);
                OzSoundV3.SniperBolt(from, a.Id);                                                      // sound_v3
                sounds.Add(new DelayedSound { at = Time.time + Random.Range(0.16f, 0.28f), key = "sniper_tail", pos = from, vol = WeaponsV2.GainSniperEcho * WeaponsV2.EvenGain(ep, WeaponsV2.BankSniperEcho), emitter = a.Id, bank = ep });
            }
            if (S.e.flag && t != null && t.Kind != ZKind.Robot && t.Kind != ZKind.MapItem && !S.e.flag2 && !OpenZHitSparks.Active)   // review: hitsparks_v1's sniper burst rings itself
                sounds.Add(new DelayedSound { at = Time.time + HeadTimeAt("sniper", dist, SniperTracerMps * Speed), key = "metal_ping", pos = S.to, vol = 0.4f, emitter = t.Id, bank = MetalPingBank, throttle = 0.5f });
            SniperShots++;
        }

        void SniperImpact(Hit h, OpenZFxPlayer z)
        {
            if (h.surf == FxSurface.Water) { var w = new Vector3(h.pos.x, z.SurfaceY(h.pos), h.pos.z); z.Droplets(w, 0.35f, 7); z.Ripple(w, 0.35f); return; }
            // streaks, bounce, flash, light, ricochet); a driver hit / snipe already got its seat burst from CombatView (never twice)
            if (h.hit && h.driver && OpenZHitSparks.Active) return;
            if (h.hit && (h.surf == FxSurface.Unit || h.surf == FxSurface.Metal) && OpenZHitSparks.Burst(h.pos, h.dir, FxWeapon.Sniper, h.surf)) return;
            z.EmitChannel("m12_core", h.pos, Vector3.zero, 0.09f, 0.5f, OpenZFxCatalog.M12Core.color, 1f);
            for (int i = 0; i < 10; i++)
            {
                var v = Random.onUnitSphere * Random.Range(2.5f, 6f); v.y = Mathf.Abs(v.y) + 0.8f;
                z.EmitChannel(Ch("wv2_spark", "leg_impact_spark"), h.pos, v, Random.Range(0.18f, 0.35f), 0.05f, OpenZFxCatalog.ImpactSparkHot, 1f);
            }
            z.AddLight(h.pos + Vector3.up * 0.3f, OpenZFxCatalog.LegLightColor, 5f, 3.5f, 0.1f);
            if (!h.hit || h.surf == FxSurface.Ground || h.surf == FxSurface.Road || h.surf == FxSurface.Rock) z.Dust(h.pos, 0.35f);
            if (!h.hit) OzSoundV3.GroundImpact(h.pos, h.surf);                                         // sound_v3
        }

        void StepHits(float now)
        {
            if (hits.Count == 0) return;
            Live(out var z);
            for (int i = 0; i < hits.Count; i++)
            {
                var h = hits[i]; if (now < h.at) continue;
                hits.RemoveAt(i--);
                if (h.anchored && HitSurface.Now(h.anc, out var np, out _)) h.pos = np;                       // IM1: the body moved during the flight
                if (!z) continue;
                switch (h.kind)
                {
                    case HitKind.Mg: MgImpact(h, z); break;
                    case HitKind.LaserStrong: Zap(h.pos, true, h.hit, h.target, h.surf); break;
                    case HitKind.LaserWeak: Zap(h.pos, false, h.hit, h.target, h.surf); break;
                    case HitKind.Sniper: SniperImpact(h, z); break;
                }
            }
        }

        void MgImpact(Hit h, OpenZFxPlayer z)
        {
            if (h.surf == FxSurface.Water) { var w = new Vector3(h.pos.x, z.SurfaceY(h.pos), h.pos.z); z.Droplets(w, 0.15f, 3); z.Ripple(w, 0.16f); return; }
            // psycho's rounds get the same new HDR streak bursts as every other bullet (a bit smaller: ~7 hits/s per psycho), and its
            // varied ricochets on about every 3rd hit (hitsparks rate-limits the sounds itself)
            if (OpenZHitSparks.Active)
            {
                if (h.hit && (h.surf == FxSurface.Unit || h.surf == FxSurface.Metal)) { OpenZHitSparks.Burst(h.pos, h.dir, FxWeapon.Bolt, h.surf, 0.8f, Random.value < 0.35f); return; }
                if (!h.hit) OzSoundV3.GroundImpact(h.pos, h.surf);                                     // sound_v3: misses on sand / road / rock
                if (h.surf == FxSurface.Road || h.surf == FxSurface.Rock) OpenZHitSparks.Chips(h.pos, h.surf);   // hard ground: a few dim chips (never the old star)
                z.Dust(h.pos, 0.12f);
                return;
            }
            bool metal = h.surf == FxSurface.Metal;
            int n = h.hit ? (metal ? 4 : 3) : 2;
            for (int i = 0; i < n; i++)
            {
                var v = Random.onUnitSphere * Random.Range(1.5f, 3.8f); v.y = Mathf.Abs(v.y) + 0.7f;
                z.EmitChannel("leg_impact_spark", h.pos, v, Random.Range(0.12f, 0.26f), 0.1f, i == 0 ? OpenZFxCatalog.ImpactSparkHot : new Color(1f, 0.7f, 0.31f), 1f);
            }
            if (h.hit) z.EmitChannel("m12_core", h.pos, Vector3.zero, 0.05f, 0.2f, OpenZFxCatalog.M12Core.color, 1f);
            else z.Dust(h.pos, 0.12f);
            if (h.hit && metal && rigs.TryGetValue(h.shooter, out var r) && Time.time - r.lastRicochet > 0.35f && CombatAudio.I)
            {
                r.lastRicochet = Time.time;
                if (Random.value < 0.5f) CombatAudio.I.PlayPitched("ricochet", h.pos, OpenZFxCatalog.RicochetVolume, OpenZFxCatalog.RicochetThrottle, h.target, 1f, OpenZFxCatalog.RicochetClips);
            }
        }

        void StepSounds(float now)
        {
            var au = CombatAudio.I;
            for (int i = 0; i < sounds.Count; i++)
            {
                var d = sounds[i]; if (now < d.at) continue;
                sounds.RemoveAt(i--);
                if (!au) continue;
                float pitch = d.key == "metal_ping" ? Random.Range(0.92f, 1.12f) : 1f;
                au.PlayPitched(d.key, d.pos, d.vol, d.throttle, d.emitter, pitch, d.key == "metal_ping" ? WeaponsV2.Pick(d.bank) : d.bank);
            }
        }

        // ---- sniper aim: laser sight + scope glint while it aims
        void StepSniperAim(float now)
        {
            Live(out var z);
            foreach (var r in rigs.Values)
            {
                if (r.snipe == null) continue;
                var a = game.World.Get(r.id);
                var t = a != null ? a.AttackObject : null;
                bool can = z && a != null && t != null && !t.Destroyed && r.view && r.view.isActiveAndEnabled && !r.view.Dying && !r.view.Reacting && r.pose && r.pose.Weapon && !a.IsMoving;   // rebase: sight off while knocked down
                float w = can ? r.snipe.Aim(now) : 0f;
                if (w <= 0.02f) { ReleaseSight(r); continue; }
                SightFrames++;
                float s = r.pose.Scale;
                var scope = r.pose.Space.TransformPoint(r.pose.ScopeLocal);
                var aimAt = AimPosOf(t);
                var from = r.pose.Muzzle ? r.pose.Muzzle.position : scope;
                if (r.sight < 0) r.sight = TakeBeam(BeamKind.Sight);
                float pulse = 0.85f + 0.15f * Mathf.Sin(now * 13f + r.id);
                if (r.sight >= 0) SetBeam(r.sight, from, aimAt, 0.05f * Mathf.Max(1f, s * 0.8f), SightRed, 5.5f * w * pulse, 1f);
                if (now >= r.dotNext) { r.dotNext = now + 0.05f; z.EmitChannel(Ch("wv2_glow", "leg_cloud"), aimAt, Vector3.zero, 0.08f, 0.5f * w, SightRed, 1f); }
                if (now >= r.glintNext)
                {   // the scope glass catches the light: a star that flickers toward the camera
                    r.glintNext = now + 0.07f;
                    var cam = Camera.main; var toCam = cam ? (cam.transform.position - scope).normalized : Vector3.up;
                    // rebase critic (RTS line-up, night frames): the glint is SUNLIGHT on the glass; at night (NightGrade.NightK 1, the
                    // night bloom threshold 0.6) it bloomed into a lamp-sized white ball: smaller and dimmer by night (the red sight stays)
                    float night = Mathf.Clamp01(NightGrade.NightK);
                    float g = (0.3f + 0.35f * Mathf.Abs(Mathf.Sin(now * 9f + r.id * 1.3f))) * w * Mathf.Max(1f, s) * (1f - 0.4f * night);
                    z.EmitChannel(Ch("wv2_glow", "leg_cloud"), scope + toCam * 0.12f, Vector3.zero, 0.09f, g, GlintWhite, 1f - 0.65f * night);
                }
            }
        }

        void ReleaseSight(Rig r) { if (r.sight >= 0) { FreeBeam(r.sight); r.sight = -1; } }

        // ------------------------------------------------------------------------------------------ beams (LineRenderer pool, OpenZ/Beam)
        enum BeamKind { Free, Sight, Trail, Arc }
        sealed class Beam { public LineRenderer lr; public BeamKind kind; public float age, life, grow, fade; public Vector3 a, b; public Color c; public float w; public MaterialPropertyBlock mpb; }
        readonly List<Beam> beams = new List<Beam>(32);
        Material beamMat;
        static readonly int ColorId = Shader.PropertyToID("_Color");

        void BuildBeams()
        {
            beamMat = Resources.Load<Material>("OpenZ/WeaponsV2/M_WV2_Beam");
            if (!beamMat) { var sh = Shader.Find("OpenZ/Beam"); if (sh) beamMat = new Material(sh) { name = "M_WV2_Beam (runtime)" }; }
            if (!beamMat) { Debug.LogWarning("[weapons_v2] OpenZ/Beam shader missing: no sniper sight / vapour trail / laser arcs"); return; }
            for (int i = 0; i < 28; i++)
            {
                var go = new GameObject("WV2Beam" + i); go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true; lr.positionCount = 2; lr.textureMode = LineTextureMode.Stretch; lr.alignment = LineAlignment.View;
                lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false; lr.numCapVertices = 0; lr.sharedMaterial = beamMat;
                lr.lightProbeUsage = LightProbeUsage.Off; lr.reflectionProbeUsage = ReflectionProbeUsage.Off; lr.enabled = false;
                beams.Add(new Beam { lr = lr, kind = BeamKind.Free, mpb = new MaterialPropertyBlock() });
            }
        }

        int TakeBeam(BeamKind k)
        {
            int best = -1; float bestAge = -1f;
            for (int i = 0; i < beams.Count; i++)
            {
                var b = beams[i];
                if (b.kind == BeamKind.Free) { best = i; break; }
                if (b.kind != BeamKind.Sight && b.age / Mathf.Max(0.01f, b.life) > bestAge) { bestAge = b.age / Mathf.Max(0.01f, b.life); best = i; }
            }
            if (best < 0) return -1;
            var x = beams[best]; x.kind = k; x.age = 0f; x.lr.enabled = true; x.lr.positionCount = 2;
            return best;
        }
        void FreeBeam(int i) { if (i < 0 || i >= beams.Count) return; beams[i].kind = BeamKind.Free; beams[i].lr.enabled = false; }

        /// <summary>colour = sRGB base, gain = linear HDR multiplier (OpenZ/Beam _Color is set linear through SetVector, as
        /// OpenZFxPlayer.SetLinear: SetColor would gamma-convert the HDR value).</summary>
        void SetBeam(int i, Vector3 a, Vector3 b, float width, Color srgb, float gain, float alpha)
        {
            var x = beams[i]; x.a = a; x.b = b; x.w = width;
            x.lr.positionCount = 2; x.lr.SetPosition(0, a); x.lr.SetPosition(1, b); x.lr.widthMultiplier = width;
            x.lr.startColor = x.lr.endColor = new Color(1f, 1f, 1f, alpha);
            SetBeamColor(x, srgb, gain);
        }

        static void SetBeamColor(Beam x, Color srgb, float gain)
        {
            var l = srgb.linear * gain; x.c = l;
            x.mpb.SetVector(ColorId, new Vector4(l.r, l.g, l.b, 1f)); x.lr.SetPropertyBlock(x.mpb);
        }

        /// <summary>The sniper's vapour trail: grows with the tracer head over `grow` s, then fades over `fade` s.</summary>
        void Trail(Vector3 a, Vector3 b, float grow, float fade, float width)
        {
            int i = TakeBeam(BeamKind.Trail); if (i < 0) return;
            var x = beams[i]; x.life = grow + fade; x.grow = Mathf.Max(0.01f, grow); x.fade = fade;
            SetBeam(i, a, a, width, SniperTrail, 2.2f, 0.9f); x.a = a; x.b = b;
        }

        /// <summary>A jagged electric arc around a laser hit (re-jittered every frame for 0.12 s).</summary>
        void Arc(Vector3 at, float size)
        {
            int i = TakeBeam(BeamKind.Arc); if (i < 0) return;
            var x = beams[i]; x.life = Random.Range(0.1f, 0.16f); x.a = at; x.b = at + Random.onUnitSphere * size + Vector3.up * size * 0.4f; x.w = 0.05f;
            x.lr.positionCount = 6; x.lr.widthMultiplier = x.w; SetBeamColor(x, LaserCore, 7f);
            JitterArc(x);
        }

        static void JitterArc(Beam x)
        {
            int n = x.lr.positionCount; var d = x.b - x.a; float len = d.magnitude;
            for (int k = 0; k < n; k++)
            {
                float u = k / (float)(n - 1);
                var p = x.a + d * u + (k > 0 && k < n - 1 ? Random.insideUnitSphere * len * 0.22f : Vector3.zero);
                x.lr.SetPosition(k, p);
            }
        }

        void StepBeams(float dt)
        {
            for (int i = 0; i < beams.Count; i++)
            {
                var x = beams[i];
                if (x.kind == BeamKind.Free || x.kind == BeamKind.Sight) continue;
                x.age += dt;
                if (x.age >= x.life) { FreeBeam(i); continue; }
                if (x.kind == BeamKind.Trail)
                {
                    float g = Mathf.Clamp01(x.age / x.grow), f = x.age <= x.grow ? 1f : 1f - (x.age - x.grow) / Mathf.Max(0.01f, x.fade);
                    x.lr.SetPosition(0, x.a); x.lr.SetPosition(1, Vector3.Lerp(x.a, x.b, g));
                    x.lr.startColor = new Color(1f, 1f, 1f, 0.35f * f * f); x.lr.endColor = new Color(1f, 1f, 1f, 0.9f * f * f);
                    x.lr.widthMultiplier = x.w * (1f + 0.8f * (1f - f));                              // the trail spreads as it fades
                }
                else if (x.kind == BeamKind.Arc)
                {
                    JitterArc(x);
                    float f = 1f - x.age / x.life;
                    x.lr.startColor = x.lr.endColor = new Color(1f, 1f, 1f, f);
                }
            }
        }
    }
}
