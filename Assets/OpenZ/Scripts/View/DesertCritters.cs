using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// rattlesnakes and desert hares, the hut animals of Z (Zod ahutanimal.cpp: 3-5 critters per hut come out, walk tile to
    /// tile preferring to go straight on, stop and look around, stay within 7 tiles of home, go home again).
    ///   * Rabbits live in 3 burrows (2 each, near Z's huts where the sand allows): they pop out, sit, nibble, twitch their
    ///     ears, look around, hop about within 12 m of home; a robot within 7 m or a vehicle within 12 m, a shot passing or a
    ///     blast makes them sit up, thump and flee in quick zigzag hops (home if home is not towards the threat, else away).
    ///     A trip home gets enough hops for the distance and they only go down the burrow within 0.6 m of it; the drawn body
    ///     turns smoothly (700-1100 deg/s) instead of snapping round at every hop.
    ///   * Snakes live in 4 dens near rocks: they slither out and along the sand in a true serpentine S (every body point
    ///     follows the path the head took), rest and flick the tongue; a unit within 6.5 m makes one rear up and rattle, within
    ///     3.8 m (or after a while) it flees. The flee target is checked on the roam mask (a fan of directions), a flight ends
    ///     after 7 s and a roam after 45 s, a blocked snake picks a new target after 0.6 s, and it goes straight down its hole
    ///     once within 0.35 m (no circling, no diving into the sand beside it).
    ///   * Threats (REVIEW-FIX): only robots and vehicles that moved in the last 3 s scare critters; parked vehicles and
    ///     cannons are 2.2 m obstacles (standing robots 0.9 m) that critters go round, and only a moving vehicle runs one over.
    ///   * Killable: a blast within reach, a shot through it or a moving vehicle running it over: a small puff of dust with fur /
    ///     scale bits and a pop, gone; a new one comes out of the same burrow minutes later.
    /// Pure view-side decor: no Zod object, no collider, never blocks a tile (they roam over walkable sand like tracks and
    /// dust do); the roam mask keeps them off rocks, props, sandbags, water, huts, worm holes and steep ground.
    /// Pooled and cheap: 6 rabbits (one FBX hierarchy each, posed only on screen), 4 snakes (one procedural 272-vertex mesh
    /// each, rebuilt only on screen), fixed arrays, no per-frame allocations; each critter has its own seeded System.Random.
    /// Data: DesertCrittersData.cs (tools/plan_l02.py). Boots itself in L02.
    /// </summary>
    [DefaultExecutionOrder(74)]
    public class DesertCritters : MonoBehaviour
    {
        public static DesertCritters I { get; private set; }
        public static bool AutoBoot = true;

        public bool rabbits = true, snakes = true;
        [Tooltip("slightly oversized toys (like the worms and eagles): rabbit 0.78 m long, snake 1.9 m")]
        public float rabbitScale = 1.3f, snakeScale = 1.25f;
        [Tooltip("seconds until a killed critter comes out again (random in range)")]
        public Vector2 rabbitRespawn = new Vector2(100f, 200f), snakeRespawn = new Vector2(120f, 240f);
        [Tooltip("flee distances (m): robots, vehicles; snakes rear up / flee")]
        public float fleeRobot = 7f, fleeVehicle = 12f, snakeAlert = 6.5f, snakeFlee = 3.8f;
        [Tooltip("a unit that moved within this many seconds is a threat; parked ones are obstacles (vehicles / cannons, robots: radius m)")]
        public float movedWithin = 3f, parkedObstacle = 2.2f, robotObstacle = 0.9f;
        [Tooltip("Critters flee from units nearby and from fights: a unit that fired within firedWithin s scares at fightK x the flee distance, a standing one at idleK x; gunfire / blasts within noiseFlee m (last noiseWindow s) scare; critters stay in until noiseCalm s without noise near home")]
        public float firedWithin = 5f, fightK = 1.6f, idleK = 0.5f, noiseFlee = 14f, noiseWindow = 1.5f, noiseCalm = 8f;
        [Tooltip("drawn rabbit body turn rate range (deg/s)")]
        public Vector2 rabbitTurnRate = new Vector2(700f, 1100f);
        [Tooltip("snakes: flight time limit, roam time limit, blocked time before a new target (s), straight-down-the-hole radius (m)")]
        public float snakeFleeMax = 7f, snakeRoamMax = 45f, snakeBlockedRetarget = 0.6f, snakeHoleRadius = 0.35f;
        public bool playSounds = true, verbose;
        public int seed = 1996;

        ZGame game; Camera cam; RtsCamera rts; bool subscribed; float clock;
        bool[] mask; int mw, mh;
        DetailKit.Bits bits;
        Material furMat, snakeMat, burrowMat;
        Transform root;
        const byte BFur = 0, BCream = 1, BScale = 2, BScaleDark = 3, BSand = 4;
        // units (5 Hz): threats = robots / vehicles that moved within movedWithin s; obstacles = parked vehicles, cannons, robots
        Vector3[] unitPos = new Vector3[256]; bool[] unitVeh = new bool[256]; float[] unitK = new float[256]; int unitCount; float unitsT;
        readonly Dictionary<int, float> lastFire = new Dictionary<int, float>(128);
        readonly Vector3[] noisePos = new Vector3[48]; readonly float[] noiseT = new float[48]; int noiseHead;
        /// <summary>Proof counters: flights started by units / by noise.</summary>
        public static int FleesUnit, FleesNoise;
        Vector3[] obsPos = new Vector3[256]; float[] obsR2 = new float[256]; int obsCount;
        struct Track { public Vector3 anchor; public float lastMove; public int seen; }
        readonly Dictionary<int, Track> tracks = new Dictionary<int, Track>(256);
        readonly List<int> staleKeys = new List<int>(64);
        int stamp;
        // recent threats (explosions, shots)
        Vector3 lastBlast; float lastBlastT = -99f; Vector3 lastShotA, lastShotB; float lastShotT = -99f;

        // ------------------------------------------------------------------ boot
        static bool hooked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!hooked) { hooked = true; SceneManager.sceneLoaded += (s, m) => TryAttach(); }
            TryAttach();
        }

        public static void TryAttach()
        {
            if (!AutoBoot || !ZGame.I || ZGame.I.level != "L02" || FindFirstObjectByType<DesertCritters>()) return;
            new GameObject("DesertCritters").AddComponent<DesertCritters>();
        }

        void Awake() { I = this; }

        void Start()
        {
            game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            if (!game || game.World == null || game.level != "L02") { enabled = false; return; }
            cam = Camera.main; rts = cam ? cam.GetComponent<RtsCamera>() : FindFirstObjectByType<RtsCamera>();
            root = new GameObject("DesertCritters_Root").transform;
            DecodeMask();
            furMat = DetailKit.ToyMat("L02C_Fur", 0.12f, 0f, 0f);
            snakeMat = DetailKit.ToyMat("L02C_Snake", 0.4f, 0f, 0f);
            burrowMat = DetailKit.ToyMat("L02C_Burrow", 0.05f, 0f, 0f);
            bits = new DetailKit.Bits(new[] { new Color(0.74f, 0.6f, 0.43f), new Color(0.95f, 0.89f, 0.76f), new Color(0.8f, 0.66f, 0.44f), new Color(0.36f, 0.24f, 0.14f), new Color(0.84f, 0.64f, 0.4f) });
            BuildHomes();
            RefreshUnits();
            FxManager.Exploded += OnExplosion; subscribed = true;
            Debug.Log($"[OpenZ] DesertCritters L02: {rabs.Count} rabbits in {nRabbitHomes} burrows, {snks.Count} snakes in {nSnakeHomes} dens (roam cells {roamCells})");
        }

        void OnDestroy()
        {
            if (subscribed) FxManager.Exploded -= OnExplosion;
            foreach (var s in snks) if (s.mesh) Destroy(s.mesh);
            if (root) Destroy(root.gameObject);
            DetailKit.Cleanup();                                                                  // run-time materials / meshes of this level
            if (I == this) I = null;
        }

        int roamCells, nRabbitHomes, nSnakeHomes;
        void DecodeMask()
        {
            mw = DesertCrittersData.Width; mh = DesertCrittersData.Height;
            var bytes = System.Convert.FromBase64String(DesertCrittersData.MaskB64);
            mask = new bool[mw * mh];
            for (int i = 0; i < mask.Length; i++) { mask[i] = (bytes[i >> 3] & (1 << (i & 7))) != 0; if (mask[i]) roamCells++; }
        }

        bool Mask(float x, float z)
        {
            int i = Mathf.FloorToInt(x), j = Mathf.FloorToInt(-z);
            return i >= 0 && j >= 0 && i < mw && j < mh && mask[j * mw + i];
        }

        /// <summary>A spot a critter may stand on: roam mask and outside every parked-unit obstacle.</summary>
        bool Roam(float x, float z)
        {
            if (!Mask(x, z)) return false;
            for (int i = 0; i < obsCount; i++) { float dx = x - obsPos[i].x, dz = z - obsPos[i].z; if (dx * dx + dz * dz < obsR2[i]) return false; }
            return true;
        }

        /// <summary>A step from `from` to `p`: on the roam mask, and never deeper into an obstacle (a critter a unit parked next to
        /// may still walk out of its radius).</summary>
        bool Free(Vector3 from, Vector3 p)
        {
            if (!Mask(p.x, p.z)) return false;
            for (int i = 0; i < obsCount; i++)
            {
                float d = Flat(p, obsPos[i]);
                if (d < obsR2[i] && d <= Flat(from, obsPos[i])) return false;
            }
            return true;
        }

        bool RoamLine(Vector3 a, Vector3 b)
        {
            var d = b - a; d.y = 0f; float L = d.magnitude; int n = Mathf.Max(1, Mathf.CeilToInt(L / 0.25f));
            var prev = a;
            for (int k = 1; k <= n; k++) { var p = a + d * (k / (float)n); if (!Free(prev, p)) return false; prev = p; }
            return true;
        }

        bool HomeBlocked(Vector3 home)
        {
            for (int i = 0; i < obsCount; i++) if (Flat(home, obsPos[i]) < obsR2[i]) return true;
            return false;
        }

        float Ground(float x, float z) => game.GroundY(x, z);

        // ------------------------------------------------------------------ homes
        struct Home { public Vector3 pos; public bool snake; public float yaw; }
        readonly List<Home> homes = new List<Home>();

        void BuildHomes()
        {
            var h = DesertCrittersData.Homes; var cnt = DesertCrittersData.HomeCount; var kind = DesertCrittersData.HomeIsSnake;
            var rng = new System.Random(seed);
            for (int i = 0; i < h.Length; i++)
            {
                var p = new Vector3(h[i].x, 0f, h[i].y); p.y = Ground(p.x, p.z);
                var hm = new Home { pos = p, snake = kind[i], yaw = (float)rng.NextDouble() * 360f };
                homes.Add(hm);
                var b = DetailKit.Model("L02C_Burrow", root);
                if (b)
                {
                    b.transform.SetPositionAndRotation(p + Vector3.down * 0.005f, Quaternion.Euler(0f, hm.yaw, 0f));
                    b.transform.localScale = Vector3.one * (hm.snake ? 0.7f : 1.1f);          // mound 3.6 cm high at most
                    DetailKit.SetMat(b, burrowMat, false);
                }
                for (int k = 0; k < cnt[i]; k++)
                {
                    if (hm.snake) { if (snakes) snks.Add(NewSnake(i, snks.Count)); }
                    else if (rabbits) { var r = NewRabbit(i, rabs.Count); if (r != null) rabs.Add(r); }
                }
                if (hm.snake) nSnakeHomes++; else nRabbitHomes++;
            }
        }

        // ================================================================== rabbits
        enum RS { Hidden, Emerge, Idle, Hop, Alert, Flee, Enter, Dead }
        sealed class Rabbit
        {
            public int home, idx; public System.Random rng; public RS st; public float t, dur;
            public Vector3 pos, goal, hopA, hopB; public float yaw, drawYaw, turnRate, hopT, hopDur, hopH, pauseT; public int hopsLeft; public bool fleeing, fleeHome;
            public GameObject go; public Transform tr, body, head, earL, earR, legBL, legBR, legFL, legFR, tail;
            public Quaternion qBody, qHead, qEarL, qEarR, qBL, qBR, qFL, qFR; public Vector3 bodyScale0;
            public float lookYaw, lookTarget, lookT, earTw, earTwSide, nibble, alertK, rise; public Vector3 threat;
        }
        readonly List<Rabbit> rabs = new List<Rabbit>();
        const float EnterRadius = 0.6f;                                                           // REVIEW-FIX: down the burrow only this close

        Rabbit NewRabbit(int home, int idx)
        {
            var go = DetailKit.Model("L02C_Rabbit", root);
            if (!go) return null;
            DetailKit.SetMat(go, furMat, true);
            var r = new Rabbit { home = home, idx = idx, rng = new System.Random(seed * 31 + idx * 7919 + 17), go = go, tr = go.transform };
            r.body = DetailKit.Find(r.tr, "Body"); r.head = DetailKit.Find(r.tr, "Head");
            r.earL = DetailKit.Find(r.tr, "EarL"); r.earR = DetailKit.Find(r.tr, "EarR"); r.tail = DetailKit.Find(r.tr, "Tail");
            r.legBL = DetailKit.Find(r.tr, "LegBL"); r.legBR = DetailKit.Find(r.tr, "LegBR"); r.legFL = DetailKit.Find(r.tr, "LegFL"); r.legFR = DetailKit.Find(r.tr, "LegFR");
            if (r.body) { r.qBody = r.body.localRotation; r.bodyScale0 = r.body.localScale; }
            if (r.head) r.qHead = r.head.localRotation; if (r.earL) r.qEarL = r.earL.localRotation; if (r.earR) r.qEarR = r.earR.localRotation;
            if (r.legBL) r.qBL = r.legBL.localRotation; if (r.legBR) r.qBR = r.legBR.localRotation; if (r.legFL) r.qFL = r.legFL.localRotation; if (r.legFR) r.qFR = r.legFR.localRotation;
            r.tr.localScale = Vector3.one * rabbitScale;
            r.turnRate = Rf(r.rng, rabbitTurnRate.x, rabbitTurnRate.y);
            r.st = RS.Hidden; r.t = 1f + (float)r.rng.NextDouble() * 12f;                                    // stagger the first appearance
            r.pos = homes[home].pos; go.SetActive(false);
            return r;
        }

        static float Rf(System.Random g, float a, float b) => a + (float)g.NextDouble() * (b - a);

        void StepRabbit(Rabbit r, float dt, float now)
        {
            var home = homes[r.home].pos;
            switch (r.st)
            {
                case RS.Dead:
                    r.t -= dt; if (r.t <= 0f) { r.st = RS.Hidden; r.t = 0.5f; }
                    return;
                case RS.Hidden:
                    r.t -= dt;
                    if (r.t <= 0f)
                    {
                        if (ThreatNear(home, 12f, 16f, out _) || HomeBlocked(home) || NoiseNear(home, 22f, noiseCalm, out _)) { r.t = 4f + Rf(r.rng, 0f, 6f); return; }   // back out only when calm
                        r.st = RS.Emerge; r.t = 0f; r.pos = home; r.yaw = Rf(r.rng, 0f, 360f); r.drawYaw = r.yaw; r.go.SetActive(true); r.rise = 0f;
                    }
                    return;
                case RS.Emerge:
                    r.t += dt; r.rise = Mathf.Clamp01(r.t / 0.5f);
                    if (r.t >= 0.5f) { r.rise = 1f; ToIdle(r); }
                    break;
                case RS.Enter:
                    r.t += dt; r.rise = 1f - Mathf.Clamp01(r.t / 0.35f);
                    if (r.t >= 0.35f) { r.st = RS.Hidden; r.t = Rf(r.rng, 8f, 22f); r.go.SetActive(false); r.fleeing = false; }
                    break;
            }
            // threats (moving units every 0.2 s, blasts and shots when they happen)
            if (r.st == RS.Idle || r.st == RS.Hop || r.st == RS.Alert)
            {
                bool byUnit = ThreatNear(r.pos, fleeRobot, fleeVehicle, out var th);
                if (byUnit || (now - lastBlastT < 0.3f && Flat(lastBlast, r.pos) < 16f * 16f) || NoiseNear(r.pos, noiseFlee, noiseWindow, out th)
                    || (now - lastShotT < 0.3f && DetailKit.DistPointSegment(r.pos + Vector3.up * 0.2f, lastShotA, lastShotB) < 3.5f))
                {
                    if (th == Vector3.zero) th = now - lastBlastT < 0.3f ? lastBlast : lastShotA;
                    r.threat = th;
                    if (r.st != RS.Alert) { r.st = RS.Alert; r.t = 0f; r.dur = Rf(r.rng, 0.25f, 0.6f); if (playSounds && Visible(r.pos)) Thump(r.pos); if (byUnit) FleesUnit++; else FleesNoise++; }
                }
            }
            switch (r.st)
            {
                case RS.Idle:
                    r.t += dt;
                    r.nibble = Mathf.MoveTowards(r.nibble, (Mathf.Sin(now * 0.9f + r.idx) > 0.35f) ? 1f : 0f, dt * 2.5f);
                    if (r.t >= r.dur) StartWander(r, home);
                    break;
                case RS.Alert:
                    r.t += dt; r.alertK = Mathf.MoveTowards(r.alertK, 1f, dt * 6f); r.nibble = 0f;
                    FaceTowards(r, r.threat, dt, 14f);
                    if (r.t >= r.dur) StartFlee(r, home);
                    break;
                case RS.Hop:
                case RS.Flee:
                    r.alertK = Mathf.MoveTowards(r.alertK, 0f, dt * 3f);
                    if (r.pauseT > 0f)
                    {   // a short sit between calm hops (hop, pause, look, hop on)
                        r.pauseT -= dt;
                        if (r.pauseT <= 0f && !NextHop(r, false)) ArriveOrIdle(r, home);
                        break;
                    }
                    r.hopT += dt;
                    float u = Mathf.Clamp01(r.hopT / r.hopDur);
                    var p = Vector3.Lerp(r.hopA, r.hopB, u); p.y = Ground(p.x, p.z) + r.hopH * 4f * u * (1f - u);
                    r.pos = p;
                    if (u >= 1f)
                    {
                        if (playSounds && r.st == RS.Hop && Visible(r.pos) && r.rng.NextDouble() < 0.25) Patter(r.pos);
                        if (r.fleeHome && Flat(r.pos, home) < EnterRadius * EnterRadius) { r.st = RS.Enter; r.t = 0f; break; }
                        if (r.st == RS.Hop && r.hopsLeft > 1 && r.rng.NextDouble() < 0.3) { r.hopsLeft--; r.pauseT = Rf(r.rng, 0.3f, 1.1f); break; }
                        if (--r.hopsLeft <= 0 || !NextHop(r, r.st == RS.Flee)) ArriveOrIdle(r, home);
                    }
                    break;
            }
        }

        /// <summary>The hops ran out or the next one is blocked: into the burrow only when right at it (<= 0.6 m, REVIEW-FIX: a
        /// hare never sinks into bare sand), else it sits down where it is and tries again later.</summary>
        void ArriveOrIdle(Rabbit r, Vector3 home)
        {
            if (r.fleeHome && Flat(r.pos, home) < EnterRadius * EnterRadius) { r.st = RS.Enter; r.t = 0f; }
            else ToIdle(r);
        }

        void ToIdle(Rabbit r) { r.st = RS.Idle; r.t = 0f; r.dur = Rf(r.rng, 2f, 6.5f); r.fleeing = false; r.fleeHome = false; r.lookT = 0f; r.pauseT = 0f; }

        /// <summary>Hops for a trip of `dist` m: the distance over the shortest hop, plus 6 spare (zigzags, detours).</summary>
        int HopsFor(float dist, bool flee) => Mathf.CeilToInt(dist / ((flee ? 0.85f : 0.32f) * rabbitScale)) + 6;

        void StartWander(Rabbit r, Vector3 home)
        {
            // 20 % chance to go home for a while, else a point within 12 m of home on the roam mask
            if (r.rng.NextDouble() < 0.2 && Flat(r.pos, home) < 14f * 14f && !HomeBlocked(home)) { r.goal = home; r.fleeHome = true; }
            else
            {
                r.fleeHome = false; r.goal = r.pos;
                for (int k = 0; k < 10; k++)
                {
                    float a = Rf(r.rng, 0f, Mathf.PI * 2f), d = Rf(r.rng, 2f, 12f);
                    var g = home + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                    if (Roam(g.x, g.z)) { r.goal = g; break; }
                }
            }
            r.st = RS.Hop; r.hopsLeft = r.fleeHome ? HopsFor(Mathf.Sqrt(Flat(r.pos, home)), false) : 12;
            if (r.fleeHome && Flat(r.pos, home) < EnterRadius * EnterRadius) { r.st = RS.Enter; r.t = 0f; return; }
            if (!NextHop(r, false)) ArriveOrIdle(r, home);
        }

        void StartFlee(Rabbit r, Vector3 home)
        {
            var away = r.pos - r.threat; away.y = 0f; away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
            var toHome = home - r.pos; toHome.y = 0f;
            // home unless home lies towards the threat (or a parked vehicle stands on it)
            r.fleeHome = !HomeBlocked(home) && toHome.magnitude < 16f && (toHome.magnitude < 1f || Vector3.Dot(toHome.normalized, away) > -0.2f);
            r.goal = r.fleeHome ? home : r.pos + away * 14f;
            r.st = RS.Flee; r.hopsLeft = r.fleeHome ? HopsFor(toHome.magnitude, true) : 22; r.fleeing = true; r.pauseT = 0f;
            if (r.fleeHome && toHome.magnitude < EnterRadius) { r.st = RS.Enter; r.t = 0f; return; }
            if (!NextHop(r, true)) ArriveOrIdle(r, home);
        }

        bool NextHop(Rabbit r, bool flee)
        {
            var to = r.goal - r.pos; to.y = 0f; float dist = to.magnitude;
            if (dist < 0.3f) return false;
            float len = (flee ? Rf(r.rng, 0.85f, 1.25f) : Rf(r.rng, 0.32f, 0.5f)) * rabbitScale;
            len = Mathf.Min(len, dist);
            float zig = dist < 1.5f ? 0f : flee ? Rf(r.rng, -28f, 28f) : Rf(r.rng, -10f, 10f);   // the last hops go straight at the goal
            for (int k = 0; k < 7; k++)
            {
                float turn = zig + (k == 0 ? 0f : (k % 2 == 1 ? 1 : -1) * ((k + 1) / 2) * 30f);
                var dir = Quaternion.Euler(0f, turn, 0f) * (to / dist);
                var b = r.pos + dir * len;
                if (!RoamLine(r.pos, b)) continue;
                r.hopA = r.pos; r.hopB = b; r.hopB.y = Ground(b.x, b.z);
                r.hopT = 0f; r.hopDur = flee ? Rf(r.rng, 0.26f, 0.3f) : Rf(r.rng, 0.3f, 0.36f) + Rf(r.rng, 0f, 0.12f);
                r.hopH = (flee ? Rf(r.rng, 0.17f, 0.24f) : Rf(r.rng, 0.08f, 0.13f)) * rabbitScale;
                r.yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                return true;
            }
            return false;
        }

        void FaceTowards(Rabbit r, Vector3 p, float dt, float speed)
        {
            var d = p - r.pos; d.y = 0f; if (d.sqrMagnitude < 0.01f) return;
            float target = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            r.lookTarget = Mathf.Clamp(Mathf.DeltaAngle(r.drawYaw, target), -70f, 70f);
        }

        void PoseRabbit(Rabbit r, float dt, float now)
        {
            if (!r.go.activeSelf) return;
            // REVIEW-FIX: the drawn body turns smoothly to the hop heading (700-1100 deg/s) instead of snapping up to 90 deg a hop
            r.drawYaw = Mathf.MoveTowardsAngle(r.drawYaw, r.yaw, r.turnRate * dt);
            float bury = (1f - r.rise) * 0.42f * rabbitScale;
            r.tr.SetPositionAndRotation(r.pos + Vector3.down * bury, Quaternion.Euler(0f, r.drawYaw, 0f));
            if (!Visible(r.pos)) return;
            bool hopping = (r.st == RS.Hop && r.pauseT <= 0f) || r.st == RS.Flee;
            float u = hopping ? Mathf.Clamp01(r.hopT / Mathf.Max(0.01f, r.hopDur)) : 0f;
            float flee = r.st == RS.Flee ? 1f : 0f;
            // body: nose up at take-off, level, nose down to land; sits up tall when alert; breathing
            float pitch = hopping ? Mathf.Lerp(-16f - 6f * flee, 12f, Mathf.SmoothStep(0.15f, 0.85f, u)) : -r.alertK * 32f + r.nibble * 6f;
            float squash = hopping ? (u < 0.12f ? Mathf.Lerp(1.06f, 1f, u / 0.12f) : (u > 0.88f ? Mathf.Lerp(1f, 0.93f, (u - 0.88f) / 0.12f) : 1f)) : 1f + 0.012f * Mathf.Sin(now * 5.5f + r.idx);
            if (r.body) { r.body.localRotation = Quaternion.Euler(pitch, 0f, 0f) * r.qBody; r.body.localScale = new Vector3(r.bodyScale0.x * (2f - squash), r.bodyScale0.y * squash, r.bodyScale0.z * (2f - squash)); }
            // head: looks around when idle (quick turns), nibbles, faces the threat when alert
            if (r.st == RS.Idle || (r.st == RS.Hop && r.pauseT > 0f)) { r.lookT -= dt; if (r.lookT <= 0f) { r.lookT = Rf(r.rng, 0.6f, 2.2f); r.lookTarget = Rf(r.rng, -55f, 55f); } }
            else if (r.st != RS.Alert) r.lookTarget = 0f;
            r.lookYaw = Mathf.MoveTowards(r.lookYaw, r.lookTarget, dt * 220f);
            if (r.head) r.head.localRotation = Quaternion.Euler(r.nibble * 28f + Mathf.Sin(now * 14f) * r.nibble * 3f + r.alertK * 18f, r.lookYaw, 0f) * r.qHead;
            // ears: back with speed, up and alert, a twitch now and then
            r.earTw -= dt; if (r.earTw < -Rf(r.rng, 1.5f, 4f)) { r.earTw = 0.18f; r.earTwSide = r.rng.NextDouble() < 0.5 ? -1f : 1f; }
            float tw = r.earTw > 0f ? Mathf.Sin(r.earTw / 0.18f * Mathf.PI) * 22f : 0f;
            float earBack = hopping ? -(22f + 22f * flee) * Mathf.Sin(u * Mathf.PI) - 8f : -6f + r.alertK * 10f;
            if (r.earL) r.earL.localRotation = Quaternion.Euler(earBack + (r.earTwSide < 0 ? -tw : 0f), 0f, -6f) * r.qEarL;
            if (r.earR) r.earR.localRotation = Quaternion.Euler(earBack + (r.earTwSide > 0 ? -tw : 0f), 0f, 6f) * r.qEarR;
            // legs: hind legs push back at take-off and tuck, front legs reach to land
            float hind = hopping ? (u < 0.35f ? Mathf.Lerp(10f, 55f, u / 0.35f) : Mathf.Lerp(55f, -10f, Mathf.SmoothStep(0.35f, 0.9f, u))) : r.alertK * 12f;
            float front = hopping ? (u < 0.5f ? Mathf.Lerp(25f, -10f, u / 0.5f) : Mathf.Lerp(-10f, -40f, Mathf.SmoothStep(0.5f, 0.95f, u))) : r.alertK * -25f + r.nibble * -8f;
            if (r.legBL) r.legBL.localRotation = Quaternion.Euler(hind, 0f, 0f) * r.qBL;
            if (r.legBR) r.legBR.localRotation = Quaternion.Euler(hind, 0f, 0f) * r.qBR;
            if (r.legFL) r.legFL.localRotation = Quaternion.Euler(front, 0f, 0f) * r.qFL;
            if (r.legFR) r.legFR.localRotation = Quaternion.Euler(front, 0f, 0f) * r.qFR;
        }

        void KillRabbit(Rabbit r, Vector3 from)
        {
            if (r.st == RS.Dead || r.st == RS.Hidden) return;
            var p = r.pos + Vector3.up * 0.18f;
            Puff(p, 0.45f);
            bits.Burst(p, 9, BFur, 1.8f, 0.05f, r.rng); bits.Burst(p, 5, BCream, 1.8f, 0.045f, r.rng);
            if (playSounds) DetailKit.Sound("critter_small_death", p, 0.6f, "critter", 0.05f, -1, "rp_pop_a", "rp_pop_b", "rp_pop_c");
            r.st = RS.Dead; r.t = Rf(r.rng, rabbitRespawn.x, rabbitRespawn.y); r.go.SetActive(false);
            if (verbose) Debug.Log($"[OpenZ] DesertCritters: rabbit {r.idx} killed, back in {r.t:F0} s");
        }

        // ================================================================== snakes
        enum SS { Hidden, Out, Rest, Alert, Flee, Enter, Dead }
        sealed class Snake
        {
            public int home, idx; public System.Random rng; public SS st; public float t, dur, stateT, blockedT;
            public Vector3 head, goal; public float heading, travel, speed, targetSpeed, alertK, rattleT, tongueT, tongue, enterAt = -1f;
            public Vector3[] trail = new Vector3[160]; public int trailHead, trailCount; public float lastPush; public bool diving;
            public Mesh mesh; public Vector3[] v, n; public bool visible; public Vector3 threat;
        }
        readonly List<Snake> snks = new List<Snake>();
        const int SRings = 30, SSides = 8;
        const float SLen = 1.5f, TrailStep = 0.02f;

        Snake NewSnake(int home, int idx)
        {
            var s = new Snake { home = home, idx = idx, rng = new System.Random(seed * 131 + idx * 104729 + 3) };
            s.st = SS.Hidden; s.t = 3f + (float)s.rng.NextDouble() * 20f;
            BuildSnakeMesh(s);
            return s;
        }

        void SetState(Snake s, SS st) { s.st = st; s.stateT = 0f; s.blockedT = 0f; if (st != SS.Enter) s.diving = false; }

        /// <summary>Profile along the body (s = 0 at the snout .. SLen at the rattle): half width, half height.</summary>
        static Vector2 SnakeR(float s)
        {
            if (s < 0.075f) { float k = s / 0.075f; return new Vector2(Mathf.Lerp(0.018f, 0.045f, Mathf.Sqrt(k)), Mathf.Lerp(0.014f, 0.03f, Mathf.Sqrt(k))); }
            if (s < 0.11f) { float k = (s - 0.075f) / 0.035f; return new Vector2(Mathf.Lerp(0.045f, 0.03f, k), Mathf.Lerp(0.03f, 0.027f, k)); }
            if (s < 0.5f) { float k = (s - 0.11f) / 0.39f; float r = Mathf.Lerp(0.03f, 0.05f, Mathf.Sin(k * Mathf.PI * 0.5f)); return new Vector2(r * 1.12f, r * 0.82f); }
            if (s < 1.36f) { float k = (s - 0.5f) / 0.86f; float r = Mathf.Lerp(0.05f, 0.013f, k * k * 0.6f + k * 0.4f); return new Vector2(r * 1.12f, r * 0.82f); }
            float q = (s - 1.36f) / (SLen - 1.36f);                                                   // rattle: beads
            float bead = 0.017f * (1f - q * 0.4f) * (0.8f + 0.2f * Mathf.Abs(Mathf.Sin(q * Mathf.PI * 4f)));
            return new Vector2(bead * 1.05f, bead * 0.9f);
        }

        void BuildSnakeMesh(Snake s)
        {
            int nv = SRings * SSides + 2 + 6;
            s.v = new Vector3[nv]; s.n = new Vector3[nv]; var col = new Color[nv]; var tri = new List<int>();
            var tan = new Color(0.8f, 0.66f, 0.44f); var dark = new Color(0.36f, 0.24f, 0.14f); var cream = new Color(0.94f, 0.88f, 0.72f);
            var edge = new Color(0.92f, 0.82f, 0.6f); var headC = new Color(0.62f, 0.48f, 0.32f); var rattle = new Color(0.86f, 0.8f, 0.66f);
            for (int i = 0; i < SRings; i++)
            {
                float sl = RingS(i);
                for (int k = 0; k < SSides; k++)
                {
                    float a = k * Mathf.PI * 2f / SSides;                                                  // 0 = right side, pi/2 = top
                    float top = Mathf.Sin(a), side = Mathf.Cos(a);
                    Color c = tan;
                    if (top < -0.35f) c = cream;
                    else if (sl < 0.09f) { c = top > 0.2f ? headC : tan; if (sl > 0.03f && sl < 0.055f && top > 0.25f && Mathf.Abs(side) > 0.55f) c = new Color(0.06f, 0.05f, 0.04f); }
                    else if (sl > 1.36f) c = (Mathf.Repeat((sl - 1.36f) / 0.035f, 1f) < 0.25f) ? dark : rattle;
                    else if (top > -0.1f)
                    {   // dorsal diamonds with a pale edge
                        float p = Mathf.Abs(Mathf.Repeat(sl / 0.12f, 1f) - 0.5f) * 2f;
                        float d = p + (1f - top) * 0.9f;
                        c = d < 0.55f ? dark : (d < 0.75f ? edge : tan);
                        if (sl > 1.15f) c = (Mathf.Repeat(sl / 0.06f, 1f) < 0.5f) ? dark : tan;          // banded tail
                    }
                    col[i * SSides + k] = c;
                    if (i > 0)
                    {
                        int a0 = (i - 1) * SSides + k, a1 = (i - 1) * SSides + (k + 1) % SSides, b0 = i * SSides + k, b1 = i * SSides + (k + 1) % SSides;
                        tri.Add(a0); tri.Add(b0); tri.Add(a1); tri.Add(a1); tri.Add(b0); tri.Add(b1);
                    }
                }
            }
            int cs = SRings * SSides, ce = cs + 1;                                                        // snout cap, tail cap
            col[cs] = headC; col[ce] = rattle;
            for (int k = 0; k < SSides; k++)
            {
                tri.Add(cs); tri.Add(k); tri.Add((k + 1) % SSides);
                int b = (SRings - 1) * SSides; tri.Add(ce); tri.Add(b + (k + 1) % SSides); tri.Add(b + k);
            }
            int tg = ce + 1;                                                                               // forked tongue: 6 verts
            for (int k = 0; k < 6; k++) col[tg + k] = new Color(0.82f, 0.08f, 0.14f);
            tri.AddRange(new[] { tg, tg + 1, tg + 2, tg + 1, tg + 3, tg + 2, tg + 2, tg + 3, tg + 4, tg + 3, tg + 5, tg + 4 });
            tri.AddRange(new[] { tg, tg + 2, tg + 1, tg + 1, tg + 2, tg + 3, tg + 2, tg + 4, tg + 3, tg + 3, tg + 4, tg + 5 });   // double-sided
            var m = new Mesh { name = "Snake" + s.idx, hideFlags = HideFlags.DontSave };
            m.MarkDynamic();
            m.SetVertices(s.v); m.SetNormals(s.n); m.SetColors(col); m.SetTriangles(tri, 0);
            s.mesh = m;
        }

        static float RingS(int i) { float k = i / (SRings - 1f); return SLen * (k < 0.12f ? k * 0.75f : 0.09f + (k - 0.12f) / 0.88f * (1f - 0.06f)); }

        void PushTrail(Snake s, Vector3 p)
        {
            s.trailHead = (s.trailHead + 1) % s.trail.Length; s.trail[s.trailHead] = p;
            if (s.trailCount < s.trail.Length) s.trailCount++;
        }

        /// <summary>Point on the body at arc length sl from the snout (trail samples every TrailStep m, newest first).</summary>
        Vector3 Spine(Snake s, float sl)
        {
            float f = Mathf.Max(0f, sl * snakeScale) / TrailStep;
            int i0 = Mathf.FloorToInt(f); float fr = f - i0;
            if (i0 >= s.trailCount - 1) return s.trail[(s.trailHead - (s.trailCount - 1) + s.trail.Length * 4) % s.trail.Length];
            var a = s.trail[(s.trailHead - i0 + s.trail.Length * 4) % s.trail.Length];
            var b = s.trail[(s.trailHead - i0 - 1 + s.trail.Length * 4) % s.trail.Length];
            return Vector3.LerpUnclamped(a, b, fr);
        }

        void StepSnake(Snake s, float dt, float now)
        {
            var home = homes[s.home].pos;
            s.stateT += dt;
            switch (s.st)
            {
                case SS.Dead: s.t -= dt; if (s.t <= 0f) { SetState(s, SS.Hidden); s.t = 1f; } return;
                case SS.Hidden:
                    s.t -= dt;
                    if (s.t <= 0f)
                    {
                        if (ThreatNear(home, 10f, 14f, out _) || HomeBlocked(home)) { s.t = 5f; return; }
                        // the body starts coiled down the hole: it slithers out of it
                        s.trailCount = 0; s.trailHead = 0;
                        for (int k = s.trail.Length - 1; k >= 0; k--) PushTrail(s, home + Vector3.down * (0.02f + k * TrailStep * 0.5f));
                        s.head = home + Vector3.down * 0.02f; s.heading = Rf(s.rng, 0f, 360f); s.travel = 0f; s.lastPush = 0f;
                        SetState(s, SS.Out); PickGoal(s, home); s.targetSpeed = Rf(s.rng, 0.28f, 0.4f);
                    }
                    return;
            }
            bool alertable = s.st == SS.Out || s.st == SS.Rest;
            if (alertable || s.st == SS.Alert)
            {
                bool near = ThreatNear(s.head, snakeAlert, snakeAlert + 3f, out var th);
                if (near || (now - lastBlastT < 0.3f && Flat(lastBlast, s.head) < 14f * 14f) || NoiseNear(s.head, noiseFlee * 0.75f, noiseWindow, out th)
                    || (now - lastShotT < 0.3f && DetailKit.DistPointSegment(s.head, lastShotA, lastShotB) < 3f))
                {
                    if (th == Vector3.zero) th = now - lastBlastT < 0.3f ? lastBlast : lastShotA;
                    s.threat = th;
                    bool close = Flat(th, s.head) < snakeFlee * snakeFlee || now - lastBlastT < 0.3f;
                    if (alertable && !close) { SetState(s, SS.Alert); s.t = 0f; s.dur = Rf(s.rng, 1.5f, 3f); s.rattleT = 0f; }
                    else if (close) StartSnakeFlee(s, home);
                }
            }
            // REVIEW-FIX time limits: a flight ends after snakeFleeMax s (home if close, else a rest), a roam after snakeRoamMax s
            if (s.st == SS.Flee && s.stateT > snakeFleeMax) { if (Flat(s.head, home) < 1.5f * 1.5f) GoHome(s, home, 0.5f); else { SetState(s, SS.Rest); s.t = 0f; s.dur = Rf(s.rng, 3f, 8f); s.speed = 0f; } }
            else if (s.st == SS.Out && s.stateT > snakeRoamMax) GoHome(s, home, 0.35f);
            switch (s.st)
            {
                case SS.Out:
                case SS.Flee:
                case SS.Enter:
                    MoveSnake(s, home, dt);
                    break;
                case SS.Rest:
                    s.t += dt;
                    if (s.t > s.dur)
                    {
                        if (s.rng.NextDouble() < 0.3 || Flat(s.head, home) > 13f * 13f) GoHome(s, home, 0.35f);
                        else { SetState(s, SS.Out); PickGoal(s, home); s.targetSpeed = Rf(s.rng, 0.26f, 0.4f); }
                    }
                    break;
                case SS.Alert:
                    s.t += dt;
                    s.rattleT -= dt;
                    if (s.rattleT <= 0f) { s.rattleT = 1.1f; if (playSounds && Visible(s.head)) DetailKit.SoundRes("amb_l02_snake_rattle", "amb_l02_hut_critters", s.head, 0.8f, 1f, "OpenZ/Audio/Ambience/sd_snake_rattle"); }
                    if (s.t > s.dur) StartSnakeFlee(s, home);
                    break;
            }
            s.alertK = Mathf.MoveTowards(s.alertK, s.st == SS.Alert ? 1f : 0f, dt * 3f);
            // tongue flicks
            s.tongueT -= dt;
            if (s.tongueT <= 0f) { s.tongueT = Rf(s.rng, 0.9f, 2.6f) * (s.st == SS.Alert ? 0.4f : 1f); s.tongue = 0.22f; }
            if (s.tongue > 0f) s.tongue -= dt;
        }

        void GoHome(Snake s, Vector3 home, float speed)
        {
            SetState(s, SS.Enter); s.goal = home; s.enterAt = -1f; s.targetSpeed = speed;
        }

        /// <summary>Out / Flee / Enter: the head steers to its goal in a serpentine S (every body point follows its path). Within
        /// snakeHoleRadius of the den on the way home it goes straight down the hole; approaching it the S flattens out and the
        /// head turns fast, so it never circles the den or dives into the sand beside it. A blocked step tries the headings
        /// either side; blocked for snakeBlockedRetarget s it picks a new target.</summary>
        void MoveSnake(Snake s, Vector3 home, float dt)
        {
            float dHome = Mathf.Sqrt(Flat(s.head, home));
            if (s.st == SS.Flee && Flat(s.goal, home) < 0.01f && dHome < 1.25f) GoHome(s, home, 0.7f);   // fleeing into the den: the last metre straight in
            bool entering = s.st == SS.Enter;
            if (entering && !s.diving && dHome < snakeHoleRadius) { s.diving = true; if (s.enterAt < 0f) s.enterAt = s.travel; }
            s.speed = Mathf.MoveTowards(s.speed, s.diving ? Mathf.Max(s.targetSpeed, 0.55f) : s.targetSpeed, dt * 1.5f);
            Vector3 next;
            if (s.diving)
            {   // straight down the hole: to the den's centre and down; the body follows the head's path into it
                var bottom = new Vector3(home.x, Ground(home.x, home.z) - 3f, home.z);
                next = Vector3.MoveTowards(s.head, bottom, s.speed * dt);
                var fl = bottom - s.head; fl.y = 0f; if (fl.sqrMagnitude > 1e-6f) s.heading = Mathf.Atan2(fl.x, fl.z) * Mathf.Rad2Deg;
            }
            else
            {
                var to = s.goal - s.head; to.y = 0f;
                float want = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                float near = entering ? Mathf.Clamp01((dHome - snakeHoleRadius) / 0.9f) : 1f;          // 0 at the hole .. 1 from 1.25 m
                float turn = s.st == SS.Flee ? 200f : Mathf.Lerp(420f, 90f, near);
                s.heading = Mathf.MoveTowardsAngle(s.heading, want, dt * turn);
                // serpentine: the head swings about its course; every body point follows the head's path
                float wave = Mathf.Sin(s.travel / 0.56f * Mathf.PI * 2f) * (s.st == SS.Flee ? 30f : 38f) * near;
                bool moved = StepFrom(s, s.heading + wave, dt, out next);
                if (!moved)
                {   // blocked (mask edge, a parked vehicle): try the headings either side, else wait; a new target after a while
                    for (int k = 1; k <= 4 && !moved; k++)
                        foreach (float sg in (s.idx & 1) == 0 ? PosNeg : NegPos)
                            if (StepFrom(s, s.heading + sg * 30f * k, dt, out next)) { moved = true; s.heading += sg * 30f * k * 0.5f; break; }
                    if (!moved)
                    {
                        next = s.head;
                        s.blockedT += dt;
                        if (s.blockedT > snakeBlockedRetarget)
                        {
                            s.blockedT = 0f;
                            if (s.st == SS.Out) PickGoal(s, home);
                            else if (s.st == SS.Flee) { if (!FleeGoal(s, s.head - s.threat, out s.goal)) { SetState(s, SS.Rest); s.t = 0f; s.dur = Rf(s.rng, 2f, 5f); s.speed = 0f; return; } }
                            else { SetState(s, SS.Rest); s.t = 0f; s.dur = Rf(s.rng, 2f, 5f); s.speed = 0f; return; }   // the way home is blocked: wait, then try again
                        }
                    }
                    else s.blockedT = 0f;
                }
                else s.blockedT = 0f;
                next.y = Ground(next.x, next.z);
            }
            float step = (next - s.head).magnitude;                                                    // 3D: the dive counts too
            s.travel += step;
            s.head = next;
            while (s.travel - s.lastPush >= TrailStep) { s.lastPush += TrailStep; PushTrail(s, s.head); }
            if (s.diving)
            {
                if (s.travel - s.enterAt > SLen * snakeScale + 0.1f) { SetState(s, SS.Hidden); s.t = Rf(s.rng, 10f, 30f); s.enterAt = -1f; }
                return;
            }
            var left = s.goal - s.head; left.y = 0f;
            if (left.magnitude < 0.25f && !entering)
            {
                if (s.st == SS.Flee && Flat(s.goal, home) < 0.01f) GoHome(s, home, 0.6f);          // fled to the den: down it goes
                else { bool fled = s.st == SS.Flee; SetState(s, SS.Rest); s.t = 0f; s.dur = fled ? Rf(s.rng, 3f, 8f) : Rf(s.rng, 4f, 12f); s.speed = 0f; }
            }
        }

        static readonly float[] PosNeg = { 1f, -1f }, NegPos = { -1f, 1f };

        /// <summary>The head's next point along `heading`; false when that step is blocked (the den itself is always free on the
        /// way home).</summary>
        bool StepFrom(Snake s, float heading, float dt, out Vector3 p)
        {
            var dir = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
            p = s.head + dir * (s.speed * dt);
            var home = homes[s.home].pos;
            if (s.st == SS.Enter && Flat(p, home) < 1.2f * 1.2f) return true;
            return Free(s.head, p);
        }

        void PickGoal(Snake s, Vector3 home)
        {
            s.enterAt = -1f;
            for (int k = 0; k < 12; k++)
            {
                float a = Rf(s.rng, 0f, Mathf.PI * 2f), d = Rf(s.rng, 3f, 11f);
                var g = home + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                if (Roam(g.x, g.z) && RoamLine(s.head, g)) { s.goal = g; return; }
            }
            s.goal = home;                                                                         // nothing free nearby: back to the den
            SetState(s, SS.Enter); s.targetSpeed = 0.35f;
        }

        /// <summary>REVIEW-FIX: a validated flight target: away from the threat, else a fan of directions either side (25 deg
        /// steps up to 100 deg) at 8, 5.5 and 3.5 m, each on the roam mask with a free straight line.</summary>
        bool FleeGoal(Snake s, Vector3 away, out Vector3 goal)
        {
            away.y = 0f; away = away.sqrMagnitude > 1e-4f ? away.normalized : Quaternion.Euler(0f, s.heading, 0f) * Vector3.forward;
            foreach (float dist in FleeDist)
                foreach (float ang in FleeFan)
                {
                    var g = s.head + Quaternion.Euler(0f, ang, 0f) * away * dist;
                    if (Roam(g.x, g.z) && RoamLine(s.head, g)) { goal = g; return true; }
                }
            goal = s.head; return false;
        }
        static readonly float[] FleeDist = { 8f, 5.5f, 3.5f }, FleeFan = { 0f, 25f, -25f, 50f, -50f, 75f, -75f, 100f, -100f };

        void StartSnakeFlee(Snake s, Vector3 home)
        {
            var toHome = home - s.head; toHome.y = 0f; var away = s.head - s.threat; away.y = 0f;
            bool homeOk = !HomeBlocked(home) && toHome.magnitude < 14f && (toHome.magnitude < 1.5f || Vector3.Dot(toHome.normalized, away.normalized) > -0.3f)
                          && RoamLine(s.head, home);
            Vector3 goal = home;
            if (!homeOk && !FleeGoal(s, away, out goal))
            {   // no free flight line: back into the den if it can get there, else coil up where it is (and rattle again later)
                if (!HomeBlocked(home) && toHome.magnitude < 14f) goal = home;
                else { SetState(s, SS.Rest); s.t = 0f; s.dur = Rf(s.rng, 2f, 4f); s.speed = 0f; return; }
            }
            SetState(s, SS.Flee); s.goal = goal; s.enterAt = -1f;
            s.targetSpeed = 0.95f; s.speed = Mathf.Max(s.speed, 0.4f);
            if (toHome.magnitude < snakeHoleRadius && Flat(goal, home) < 0.01f) GoHome(s, home, 0.7f);
            if (playSounds && Visible(s.head)) DetailKit.Sound("amb_l02_snake_hiss", s.head, 0.55f, "critter", 0.6f, -1, "dtl_snake_hiss_a", "dtl_snake_hiss_b");
        }

        void PoseSnake(Snake s, float now)
        {
            s.visible = s.st != SS.Hidden && s.st != SS.Dead && Visible(s.head);
            if (!s.visible) return;
            float k = s.alertK;
            var up = Vector3.up;
            Vector3 prevC = Vector3.zero;
            for (int i = 0; i < SRings; i++)
            {
                float sl = RingS(i);
                var c = Spine(s, sl);
                var r = SnakeR(sl) * snakeScale;
                // rest on the ground (belly down), rear up the front third when alert, the rattle shakes
                c.y += r.y * 0.92f;
                if (k > 0f)
                {
                    float lift = 0.2f * k * Mathf.Clamp01(1f - sl / 0.55f) * Mathf.Clamp01(1f - sl / 0.55f);
                    c.y += lift;
                    if (sl > 1.3f) { c.y += 0.09f * k * Mathf.Clamp01((sl - 1.3f) / 0.2f); c.x += Mathf.Sin(now * 95f) * 0.012f * k; }
                }
                var nxt = Spine(s, sl + 0.03f); nxt.y = c.y;
                var tan = (i == 0 ? (c - nxt) : (prevC - c)); if (tan.sqrMagnitude < 1e-8f) tan = Vector3.forward; tan.Normalize();
                var side = Vector3.Cross(up, tan); if (side.sqrMagnitude < 1e-6f) side = Vector3.right; side.Normalize();
                var u2 = Vector3.Cross(tan, side);
                for (int q = 0; q < SSides; q++)
                {
                    float a = q * Mathf.PI * 2f / SSides; float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                    s.v[i * SSides + q] = c + side * (ca * r.x) + u2 * (sa * r.y);
                    s.n[i * SSides + q] = (side * (ca * r.y) + u2 * (sa * r.x)).normalized;
                }
                if (i == 0)
                {
                    int cs = SRings * SSides; s.v[cs] = c + tan * (r.x * 0.9f); s.n[cs] = tan;
                    // tongue: two thin forks out of the snout
                    int tg = cs + 2; float out_ = s.tongue > 0f ? Mathf.Sin(Mathf.Clamp01(s.tongue / 0.22f) * Mathf.PI) * 0.07f * snakeScale : 0f;
                    var root0 = c + tan * (r.x * 0.7f) - u2 * 0.004f; var mid = root0 + tan * (out_ * 0.6f); var tipL = mid + tan * (out_ * 0.4f) + side * (out_ * 0.18f); var tipR = mid + tan * (out_ * 0.4f) - side * (out_ * 0.18f);
                    s.v[tg] = root0 + side * 0.003f; s.v[tg + 1] = root0 - side * 0.003f; s.v[tg + 2] = mid + side * 0.003f; s.v[tg + 3] = mid - side * 0.003f; s.v[tg + 4] = tipL; s.v[tg + 5] = tipR;
                    for (int q = 0; q < 6; q++) s.n[tg + q] = u2;
                }
                if (i == SRings - 1) { int ce = SRings * SSides + 1; s.v[ce] = c - tan * (r.x * 0.9f); s.n[ce] = -tan; }
                prevC = c;
            }
            s.mesh.SetVertices(s.v); s.mesh.SetNormals(s.n); s.mesh.RecalculateBounds();
            Graphics.RenderMesh(new RenderParams(snakeMat) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true }, s.mesh, 0, Matrix4x4.identity);
        }

        void KillSnake(Snake s, Vector3 from)
        {
            if (s.st == SS.Dead || s.st == SS.Hidden) return;
            var p = Spine(s, 0.5f) + Vector3.up * 0.06f;
            Puff(p, 0.4f);
            bits.Burst(p, 8, BScale, 1.6f, 0.045f, s.rng); bits.Burst(p, 5, BScaleDark, 1.6f, 0.04f, s.rng);
            if (playSounds)
            {
                DetailKit.Sound("critter_small_death", p, 0.55f, "critter", 0.05f, -1, "rp_pop_a", "rp_pop_b", "rp_pop_c");
                DetailKit.Sound("amb_l02_snake_hiss", p, 0.45f, "critter", 0.3f, -1, "dtl_snake_hiss_a", "dtl_snake_hiss_b");
            }
            SetState(s, SS.Dead); s.t = Rf(s.rng, snakeRespawn.x, snakeRespawn.y);
            if (verbose) Debug.Log($"[OpenZ] DesertCritters: snake {s.idx} killed, back in {s.t:F0} s");
        }

        // ================================================================== threats, obstacles, hits
        /// <summary>REVIEW-FIX (every 0.2 s): each unit view's position is tracked; a robot or vehicle that moved (0.35 m from its
        /// last anchor, or moving in the sim) within the last movedWithin s is a threat (critters flee it, a moving vehicle runs them
        /// over); parked vehicles and cannons are parkedObstacle-m obstacles and standing robots robotObstacle-m ones that critters
        /// go round. Cannons never move, so they never scare anything. No allocation after warm-up.</summary>
        void RefreshUnits()
        {
            unitCount = 0; obsCount = 0; stamp++;
            foreach (var kv in game.Views)
            {
                var v = kv.Value;
                if (!v || v.Dying || !v.gameObject.activeInHierarchy || v.Obj == null) continue;
                var o = v.Obj; var p = v.transform.position;
                if (!tracks.TryGetValue(kv.Key, out var tr)) tr = new Track { anchor = p, lastMove = -99f };
                if (Flat(p, tr.anchor) > 0.35f * 0.35f || o.IsMoving) { tr.anchor = p; tr.lastMove = clock; }
                tr.seen = stamp; tracks[kv.Key] = tr;
                bool moved = o.Kind != ZKind.Cannon && clock - tr.lastMove < movedWithin;
                bool fired = o.Kind != ZKind.Cannon && lastFire.TryGetValue(kv.Key, out var lf) && clock - lf < firedWithin;
                bool standing = o.Kind != ZKind.Cannon && !moved && !fired;
                if (moved || fired || standing)
                {
                    if (unitCount == unitPos.Length) { System.Array.Resize(ref unitPos, unitCount * 2); System.Array.Resize(ref unitVeh, unitCount * 2); System.Array.Resize(ref unitK, unitCount * 2); }
                    unitPos[unitCount] = p; unitVeh[unitCount] = o.Kind != ZKind.Robot; unitK[unitCount] = fired ? fightK : moved ? 1f : idleK; unitCount++;
                }
                if (!moved)
                {
                    if (obsCount == obsPos.Length) { System.Array.Resize(ref obsPos, obsCount * 2); System.Array.Resize(ref obsR2, obsCount * 2); }
                    float r = o.Kind == ZKind.Robot ? robotObstacle : parkedObstacle;
                    obsPos[obsCount] = p; obsR2[obsCount] = r * r; obsCount++;
                }
            }
            if (tracks.Count > game.Views.Count + 64)
            {   // forget units that are gone (no allocation: a reused key list)
                staleKeys.Clear();
                foreach (var kv in tracks) if (kv.Value.seen != stamp) staleKeys.Add(kv.Key);
                for (int i = 0; i < staleKeys.Count; i++) tracks.Remove(staleKeys[i]);
            }
        }

        bool ThreatNear(Vector3 p, float robotR, float vehR, out Vector3 threat)
        {
            threat = Vector3.zero; float best = float.MaxValue; bool hit = false;
            for (int i = 0; i < unitCount; i++)
            {
                float d = Flat(unitPos[i], p), r = (unitVeh[i] ? vehR : robotR) * unitK[i];
                if (d < r * r && d < best) { best = d; threat = unitPos[i]; hit = true; }
            }
            return hit;
        }

        void AddNoise(Vector3 at) { noisePos[noiseHead] = at; noiseT[noiseHead] = clock; noiseHead = (noiseHead + 1) % noisePos.Length; }

        /// <summary></summary>
        bool NoiseNear(Vector3 p, float r, float within, out Vector3 at)
        {
            at = Vector3.zero; float best = r * r; bool hit = false;
            for (int i = 0; i < noisePos.Length; i++)
            {
                if (clock - noiseT[i] > within || noiseT[i] <= 0f) continue;
                float d = Flat(noisePos[i], p); if (d < best) { best = d; at = noisePos[i]; hit = true; }
            }
            return hit;
        }

        void OnExplosion(Vector3 at, float strength)
        {
            lastBlast = at; lastBlastT = clock; AddNoise(at);
            float reach = 1.2f + 2.4f * strength + 0.3f; float r2 = reach * reach;
            foreach (var r in rabs) if (r.st != RS.Dead && r.st != RS.Hidden && Flat(r.pos, at) < r2) KillRabbit(r, at);
            foreach (var s in snks)
            {
                if (s.st == SS.Dead || s.st == SS.Hidden) continue;
                if (Flat(s.head, at) < r2 || Flat(Spine(s, 0.7f), at) < r2 || Flat(Spine(s, 1.3f), at) < r2) KillSnake(s, at);
            }
        }

        void ReadShots()
        {
            var ev = game.World.Events;
            for (int i = 0; i < ev.Count; i++)
            {
                var e = ev[i];
                if (e.type == ZEventType.MissileFired)
                {
                    lastFire[e.objId] = clock;
                    if (game.Views.TryGetValue(e.objId, out var mv) && mv) AddNoise(mv.transform.position);
                    continue;
                }
                if (e.type != ZEventType.Hitscan) continue;
                lastFire[e.objId] = clock;
                if (!DetailKit.ShotSegment(game, e, out var a, out var b)) continue;
                lastShotA = a; lastShotB = b; lastShotT = clock; AddNoise(a); AddNoise(b);
                foreach (var r in rabs) if (r.st != RS.Dead && r.st != RS.Hidden && DetailKit.DistPointSegment(r.pos + Vector3.up * 0.2f * rabbitScale, a, b) < 0.28f) KillRabbit(r, a);
                foreach (var s in snks)
                {
                    if (s.st == SS.Dead || s.st == SS.Hidden) continue;
                    for (int k = 0; k <= 5; k++) if (DetailKit.DistPointSegment(Spine(s, k * 0.28f) + Vector3.up * 0.04f, a, b) < 0.13f) { KillSnake(s, a); break; }
                }
            }
        }

        void RunOver()
        {   // only a MOVING vehicle rolling over a critter squashes it (they flee early, so only the slow or the unlucky)
            for (int i = 0; i < unitCount; i++)
            {
                if (!unitVeh[i]) continue;
                var p = unitPos[i];
                foreach (var r in rabs) if ((r.st == RS.Idle || r.st == RS.Hop || r.st == RS.Flee || r.st == RS.Alert) && Flat(r.pos, p) < 1.1f * 1.1f) KillRabbit(r, p);
                foreach (var s in snks) if ((s.st == SS.Out || s.st == SS.Rest || s.st == SS.Alert || s.st == SS.Flee) && (Flat(s.head, p) < 1.3f * 1.3f || Flat(Spine(s, 0.7f), p) < 1.3f * 1.3f)) KillSnake(s, p);
            }
        }

        void Puff(Vector3 p, float s)
        {
            var fx = OpenZFxPlayer.I;
            if (fx) { fx.Dust(p, s); fx.EmitChannel("m12_dust", p + Vector3.up * 0.1f, new Vector3(0f, 0.45f, 0f), 1.1f, 0.55f, new Color(0.86f, 0.7f, 0.5f), 0.4f); }
            else FxManager.I?.Dust(p, s);
            bits.Burst(p, 5, BSand, 1.4f, 0.04f, new System.Random((int)(p.x * 97 + p.z * 13)));
        }

        void Thump(Vector3 p) { DetailKit.Sound("amb_l02_rabbit_thump", p, 0.55f, "critter", 0.4f, -1, "dtl_rabbit_thump_a", "dtl_rabbit_thump_b"); }
        void Patter(Vector3 p) { DetailKit.Sound("amb_l02_rabbit_hop", p, 0.3f, "critter", 0.8f, -1, "dtl_rabbit_hop_a", "dtl_rabbit_hop_b"); }

        bool Visible(Vector3 p) => DetailKit.OnScreen(cam, p, 0.1f) && (!rts || rts.height < 140f);
        static float Flat(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return dx * dx + dz * dz; }

        // ================================================================== frame
        void Update()
        {
            if (!game || mask == null) return;
            if (!cam) { cam = Camera.main; if (cam && !rts) rts = cam.GetComponent<RtsCamera>(); }
            float dt = game.paused ? 0f : Mathf.Min(Time.deltaTime, 0.05f) * Mathf.Clamp(game.gameSpeed, 0.25f, 2f);
            clock += dt;
            unitsT -= dt; if (unitsT <= 0f) { unitsT = 0.2f; RefreshUnits(); RunOver(); }
            ReadShots();                                                                        // before CombatView (LateUpdate) clears the events
            if (dt > 0f)
            {
                for (int i = 0; i < rabs.Count; i++) StepRabbit(rabs[i], dt, clock);
                for (int i = 0; i < snks.Count; i++) StepSnake(snks[i], dt, clock);
            }
            for (int i = 0; i < rabs.Count; i++) PoseRabbit(rabs[i], dt, clock);
            for (int i = 0; i < snks.Count; i++) PoseSnake(snks[i], clock);
            bits.StepAndDraw(dt, game);
        }

        // ================================================================== tests / status
        [ContextMenu("Test: everyone out now")]
        public void TestOut()
        {
            foreach (var r in rabs) if (r.st == RS.Hidden || r.st == RS.Dead) { r.st = RS.Hidden; r.t = 0.01f; }
            foreach (var s in snks) if (s.st == SS.Hidden || s.st == SS.Dead) { SetState(s, SS.Hidden); s.t = 0.01f; }
        }

        [ContextMenu("Test: scare everyone")]
        public void TestScare() { lastBlast = cam ? cam.transform.position : Vector3.zero; lastBlastT = clock; foreach (var r in rabs) if (r.st == RS.Idle || r.st == RS.Hop) { r.threat = r.pos + Vector3.forward; r.st = RS.Alert; r.t = 0f; r.dur = 0.3f; } }

        [ContextMenu("Test: kill every critter that is out")]
        public void TestKill()
        {
            foreach (var r in rabs) KillRabbit(r, r.pos + Vector3.forward);
            foreach (var s in snks) KillSnake(s, s.head + Vector3.forward);
        }

        [ContextMenu("Status (log)")]
        public void Status()
        {
            var sb = new System.Text.StringBuilder($"[OpenZ] DesertCritters: {unitCount} moving units (threats), {obsCount} parked units (obstacles)\n");
            foreach (var r in rabs) sb.Append($"  rabbit {r.idx} home {r.home}: {r.st} at {r.pos}{(r.fleeHome ? " (going home)" : "")}\n");
            foreach (var s in snks) sb.Append($"  snake {s.idx} home {s.home}: {s.st} for {s.stateT:F1} s, head {s.head} alert {s.alertK:F1}{(s.diving ? " (down the hole)" : "")}\n");
            Debug.Log(sb.ToString());
        }
    }
}
