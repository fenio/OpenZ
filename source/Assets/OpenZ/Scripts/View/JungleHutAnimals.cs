using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// Level 16: the animals of the small huts on stilts (16: the robots' shuttle stands in the place of one, the robots' start camp took another). The huts are Z's hut-animal houses (Zod ahutanimal.cpp: on the jungle
    /// planet an ostrich, a rat or a turtle; 3 to 5 come out of a hut, walk and look round within 7 tiles and go home again). The game
    /// has none of those models; the desert's hares do the same from their burrows (DesertCritters), so the jungle huts get the
    /// game's own hare model in a darker jungle-brown coat and the hares' own ways and sounds:
    /// <list type="bullet">
    /// <item>Two hares per hut. Now and then one comes out from under the hut (sooner while the camera looks at the hut), sits,
    /// nibbles, twitches its ears, looks round and hops about within 10 m, and after a while hops home and goes in under the hut.</item>
    /// <item>A walking robot within 7 m or a moving vehicle within 12 m, a shot passing or a blast makes it sit up, thump and flee in
    /// quick zigzag hops, home if home is not toward the threat, else away. It comes out only when it is calm round the hut.</item>
    /// <item>A blast within reach, a shot through it or a moving vehicle running it over: a small puff with fur bits and a pop, gone;
    /// a new one comes out of the same hut minutes later. A broken hut lets none out any more.</item>
    /// <item>Sounds: the hares' thump and hop patter (the same events as the desert's). Like the desert's hares they are out by night
    /// too.</item>
    /// </list>
    /// They hop on walkable ground only: map ground or road tiles the rules call free (not water, rock, ruins, trees, buildings),
    /// never into a parked vehicle or a standing robot. View only: no sim object, no collider, never a target. Pooled and cheap: the
    /// hidden ones are switched off; a hare off the screen is not posed. Each one has its own seeded random source.
    /// The lounger set's rule holds for them too (JungleFauna.KeepOut): while the briefing or an ending plays, no hare is drawn at the
    /// set or in the scene camera's picture; it goes on as before and is drawn again the moment it is outside or the scene is over.
    /// </summary>
    [DefaultExecutionOrder(76)]
    public sealed class JungleHutAnimals : MonoBehaviour
    {
        public static JungleHutAnimals I { get; private set; }
        public static bool AutoBoot = true;
        public bool hares = true, playSounds = true;
        public int perHut = 2, seed = 1996;
        [Tooltip("model scale (the desert's hares are 1.3)")] public float scale = 1.25f;
        [Tooltip("coat tint on the model's own colours: the desert sand to a jungle brown")] public Color coat = new Color(0.72f, 0.6f, 0.5f, 1f);
        public float roam = 10f, fleeRobot = 7f, fleeVehicle = 12f;
        [Tooltip("seconds a hare stays in (random in range); x 1/3 while the camera looks at its hut")] public Vector2 inside = new Vector2(10f, 35f);
        public Vector2 respawn = new Vector2(100f, 200f);
        /// <summary>Proof: hares out now, came out, went home, fled, killed.</summary>
        public static int Out, CameOut, WentHome, Fled, Killed, KeptOutNow, KeptOutTotal;

        ZGame game; Camera cam; RtsCamera rts; Transform root; Material fur; DetailKit.Bits bits; float clock, unitsT; bool subscribed;
        readonly List<Vector3> unitPos = new List<Vector3>(); readonly List<bool> unitVeh = new List<bool>(); readonly List<Vector3> obs = new List<Vector3>();
        Vector3 lastBlast; float lastBlastT = -99f; Vector3 shotA, shotB; float shotT = -99f;

        sealed class Hut { public int id; public Vector3 pos; public int tx, ty; public bool broken; }
        readonly List<Hut> huts = new List<Hut>();

        static bool hooked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!hooked) { hooked = true; SceneManager.sceneLoaded += (s, m) => TryAttach(); }
            TryAttach();
        }

        public static void TryAttach()
        {
            if (!AutoBoot || !ZGame.I || ZGame.I.level != "L16" || FindFirstObjectByType<JungleHutAnimals>()) return;
            new GameObject("JungleHutAnimals").AddComponent<JungleHutAnimals>();
        }

        void Awake() { I = this; }

        void Start()
        {
            game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            if (!game || game.World == null || game.level != "L16") { enabled = false; return; }
            cam = Camera.main; rts = cam ? cam.GetComponent<RtsCamera>() : FindFirstObjectByType<RtsCamera>();
            root = new GameObject("JungleHutAnimals_Root").transform; root.SetParent(transform, false);
            Out = CameOut = WentHome = Fled = Killed = KeptOutNow = KeptOutTotal = 0;
            fur = DetailKit.ToyMat("L16_HutHare", 0.12f, 0f, 0f);
            DetailKit.C(fur, "_BaseColor", coat);
            bits = new DetailKit.Bits(new[] { new Color(0.5f, 0.4f, 0.3f), new Color(0.8f, 0.74f, 0.64f), new Color(0.36f, 0.3f, 0.2f) });
            foreach (var o in game.World.Objects)
            {
                if (o.Kind != ZKind.MapItem || o.Type != "hut" || o.Destroyed) continue;
                var h = new Hut { id = o.Id, tx = o.LocX >> 4, ty = o.LocY >> 4 };
                h.pos = new Vector3(h.tx * 2f + 1f, 0f, -(h.ty * 2f + 1f)); h.pos.y = game.GroundY(h.pos.x, h.pos.z);
                huts.Add(h);
            }
            if (hares) for (int i = 0; i < huts.Count; i++) for (int k = 0; k < perHut; k++) { var r = NewHare(i, rabs.Count); if (r != null) rabs.Add(r); }
            FxManager.Exploded += OnExplosion; subscribed = true;
            Debug.Log($"[OpenZ] JungleHutAnimals L16: {rabs.Count} hares in {huts.Count} huts");
        }

        void OnDestroy()
        {
            if (subscribed) FxManager.Exploded -= OnExplosion;
            if (root) Destroy(root.gameObject);
            if (fur) Destroy(fur);
            if (I == this) I = null;
        }

        static float Rf(System.Random g, float a, float b) => a + (float)g.NextDouble() * (b - a);
        static float Flat(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return dx * dx + dz * dz; }
        float Ground(float x, float z) => game.GroundY(x, z);
        bool Visible(Vector3 p) => DetailKit.OnScreen(cam, p, 0.1f) && (!rts || rts.height < 140f);

        // ------------------------------------------------------------------ where a hare may be
        /// <summary>Walkable ground: a ground or road tile the rules call free for robots, or the hut's own tile (it lives under it).</summary>
        bool Walk(Hut h, float x, float z)
        {
            int tx = Mathf.FloorToInt(x / 2f), ty = Mathf.FloorToInt(-z / 2f);
            if (tx == h.tx && ty == h.ty) return true;
            var map = game.World.Map;
            if (tx < 0 || ty < 0 || tx >= map.width || ty >= map.height) return false;
            var c = map.ClassAt(tx, ty);
            if (c != ZClass.Ground && c != ZClass.Road) return false;
            if (!game.World.Path.TilePassable(tx, ty, true)) return false;
            if (Flat(new Vector3(x, 0f, z), h.pos) > roam * roam) return false;
            for (int i = 0; i < obs.Count; i++) if (Flat(obs[i], new Vector3(x, 0f, z)) < 1.6f * 1.6f) return false;
            return true;
        }

        bool WalkLine(Hut h, Vector3 a, Vector3 b)
        {
            var d = b - a; d.y = 0f; int n = Mathf.Max(1, Mathf.CeilToInt(d.magnitude / 0.25f));
            for (int k = 1; k <= n; k++) { var p = a + d * (k / (float)n); if (!Walk(h, p.x, p.z)) return false; }
            return true;
        }

        // ------------------------------------------------------------------ hares (the desert's hare, DesertCritters.cs)
        enum RS { Hidden, Emerge, Idle, Hop, Alert, Flee, Enter, Dead }
        sealed class Hare
        {
            public int hut, idx; public System.Random rng; public RS st; public float t, dur, rise;
            public Vector3 pos, goal, hopA, hopB, threat; public float yaw, drawYaw, hopT, hopDur, hopH, pauseT, lookYaw, lookTarget, lookT, earTw, earTwSide, nibble, alertK; public int hopsLeft; public bool fleeHome;
            public GameObject go; public Transform tr, body, head, earL, earR, legBL, legBR, legFL, legFR;
            public Quaternion qBody, qHead, qEarL, qEarR, qBL, qBR, qFL, qFR; public Vector3 bodyScale0;
            public Renderer[] rends; public bool kept;
        }
        readonly List<Hare> rabs = new List<Hare>();
        const float EnterRadius = 0.7f;

        Hare NewHare(int hut, int idx)
        {
            var go = DetailKit.Model("L02C_Rabbit", root);
            if (!go) return null;
            DetailKit.SetMat(go, fur, true);
            var r = new Hare { hut = hut, idx = idx, rng = new System.Random(seed * 37 + idx * 7919 + 23), go = go, tr = go.transform };
            r.body = DetailKit.Find(r.tr, "Body"); r.head = DetailKit.Find(r.tr, "Head");
            r.earL = DetailKit.Find(r.tr, "EarL"); r.earR = DetailKit.Find(r.tr, "EarR");
            r.legBL = DetailKit.Find(r.tr, "LegBL"); r.legBR = DetailKit.Find(r.tr, "LegBR"); r.legFL = DetailKit.Find(r.tr, "LegFL"); r.legFR = DetailKit.Find(r.tr, "LegFR");
            if (r.body) { r.qBody = r.body.localRotation; r.bodyScale0 = r.body.localScale; }
            if (r.head) r.qHead = r.head.localRotation; if (r.earL) r.qEarL = r.earL.localRotation; if (r.earR) r.qEarR = r.earR.localRotation;
            if (r.legBL) r.qBL = r.legBL.localRotation; if (r.legBR) r.qBR = r.legBR.localRotation; if (r.legFL) r.qFL = r.legFL.localRotation; if (r.legFR) r.qFR = r.legFR.localRotation;
            r.tr.localScale = Vector3.one * scale; r.rends = go.GetComponentsInChildren<Renderer>(true);
            r.st = RS.Hidden; r.t = 2f + (float)r.rng.NextDouble() * 20f;
            r.pos = huts[hut].pos; go.SetActive(false);
            return r;
        }

        bool HutGone(Hut h)
        {
            if (h.broken) return true;
            var o = game.World.Get(h.id);
            if (o == null || o.Destroyed) h.broken = true;
            return h.broken;
        }

        bool ThreatNear(Vector3 p, out Vector3 at)
        {
            at = Vector3.zero; float best = float.MaxValue; bool hit = false;
            for (int i = 0; i < unitPos.Count; i++)
            {
                float d = Flat(unitPos[i], p), r = unitVeh[i] ? fleeVehicle : fleeRobot;
                if (d < r * r && d < best) { best = d; at = unitPos[i]; hit = true; }
            }
            return hit;
        }

        void StepHare(Hare r, float dt)
        {
            var h = huts[r.hut]; var home = h.pos;
            switch (r.st)
            {
                case RS.Dead: r.t -= dt; if (r.t <= 0f) { r.st = RS.Hidden; r.t = 1f; } return;
                case RS.Hidden:
                    r.t -= dt * (Visible(home) ? 3f : 1f);                                         // sooner while the camera looks at the hut
                    if (r.t <= 0f)
                    {
                        if (HutGone(h)) { r.t = 9999f; return; }
                        if (ThreatNear(home, out _) || clock - lastBlastT < 8f && Flat(lastBlast, home) < 22f * 22f) { r.t = 4f + Rf(r.rng, 0f, 6f); return; }
                        int outNow = 0; foreach (var o in rabs) if (o.hut == r.hut && o.st != RS.Hidden && o.st != RS.Dead) outNow++;
                        if (outNow >= perHut) { r.t = 5f; return; }
                        r.st = RS.Emerge; r.t = 0f; r.pos = home; r.yaw = Rf(r.rng, 0f, 360f); r.drawYaw = r.yaw; r.go.SetActive(true); r.rise = 0f; CameOut++;
                    }
                    return;
                case RS.Emerge:
                    r.t += dt; r.rise = Mathf.Clamp01(r.t / 0.5f);
                    if (r.t >= 0.5f) { r.rise = 1f; StartWander(r, h, true); }
                    break;
                case RS.Enter:
                    r.t += dt; r.rise = 1f - Mathf.Clamp01(r.t / 0.35f);
                    if (r.t >= 0.35f) { r.st = RS.Hidden; r.t = Rf(r.rng, inside.x, inside.y); r.go.SetActive(false); WentHome++; }
                    break;
            }
            if (r.st == RS.Idle || r.st == RS.Hop || r.st == RS.Alert)
            {
                bool byUnit = ThreatNear(r.pos, out var th);
                if (byUnit || (clock - lastBlastT < 0.3f && Flat(lastBlast, r.pos) < 16f * 16f) || (clock - shotT < 0.3f && DetailKit.DistPointSegment(r.pos + Vector3.up * 0.2f, shotA, shotB) < 3.5f))
                {
                    if (!byUnit) th = clock - lastBlastT < 0.3f ? lastBlast : shotA;
                    r.threat = th;
                    if (r.st != RS.Alert) { r.st = RS.Alert; r.t = 0f; r.dur = Rf(r.rng, 0.25f, 0.6f); if (playSounds && Visible(r.pos)) Thump(r.pos); }
                }
            }
            switch (r.st)
            {
                case RS.Idle:
                    r.t += dt;
                    r.nibble = Mathf.MoveTowards(r.nibble, (Mathf.Sin(clock * 0.9f + r.idx) > 0.35f) ? 1f : 0f, dt * 2.5f);
                    if (r.t >= r.dur) StartWander(r, h, false);
                    break;
                case RS.Alert:
                    r.t += dt; r.alertK = Mathf.MoveTowards(r.alertK, 1f, dt * 6f); r.nibble = 0f;
                    { var d = r.threat - r.pos; d.y = 0f; if (d.sqrMagnitude > 0.01f) r.lookTarget = Mathf.Clamp(Mathf.DeltaAngle(r.drawYaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg), -70f, 70f); }
                    if (r.t >= r.dur) StartFlee(r, h);
                    break;
                case RS.Hop:
                case RS.Flee:
                    r.alertK = Mathf.MoveTowards(r.alertK, 0f, dt * 3f);
                    if (r.pauseT > 0f) { r.pauseT -= dt; if (r.pauseT <= 0f && !NextHop(r, h, false)) Arrive(r, h); break; }
                    r.hopT += dt;
                    float u = Mathf.Clamp01(r.hopT / r.hopDur);
                    var p = Vector3.Lerp(r.hopA, r.hopB, u); p.y = Ground(p.x, p.z) + r.hopH * 4f * u * (1f - u);
                    r.pos = p;
                    if (u >= 1f)
                    {
                        if (playSounds && r.st == RS.Hop && Visible(r.pos) && r.rng.NextDouble() < 0.25) Patter(r.pos);
                        if (r.fleeHome && Flat(r.pos, home) < EnterRadius * EnterRadius) { r.st = RS.Enter; r.t = 0f; break; }
                        if (r.st == RS.Hop && r.hopsLeft > 1 && r.rng.NextDouble() < 0.3) { r.hopsLeft--; r.pauseT = Rf(r.rng, 0.3f, 1.1f); break; }
                        if (--r.hopsLeft <= 0 || !NextHop(r, h, r.st == RS.Flee)) Arrive(r, h);
                    }
                    break;
            }
        }

        void Arrive(Hare r, Hut h)
        {
            if (r.fleeHome && Flat(r.pos, h.pos) < EnterRadius * EnterRadius) { r.st = RS.Enter; r.t = 0f; }
            else { r.st = RS.Idle; r.t = 0f; r.dur = Rf(r.rng, 2f, 6.5f); r.fleeHome = false; r.lookT = 0f; r.pauseT = 0f; }
        }

        int HopsFor(float dist, bool flee) => Mathf.CeilToInt(dist / ((flee ? 0.85f : 0.32f) * scale)) + 6;

        void StartWander(Hare r, Hut h, bool first)
        {
            // fresh out: away from the hut first; later one time in four home for a while, else a point within the roam
            if (!first && r.rng.NextDouble() < 0.25 && !HutGone(h)) { r.goal = h.pos; r.fleeHome = true; }
            else
            {
                r.fleeHome = false; r.goal = r.pos;
                for (int k = 0; k < 12; k++)
                {
                    float a = Rf(r.rng, 0f, Mathf.PI * 2f), d = Rf(r.rng, first ? 2.5f : 2f, roam - 1f);
                    var g = h.pos + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                    if (Walk(h, g.x, g.z)) { r.goal = g; break; }
                }
            }
            r.st = RS.Hop; r.hopsLeft = r.fleeHome ? HopsFor(Mathf.Sqrt(Flat(r.pos, h.pos)), false) : 12;
            if (r.fleeHome && Flat(r.pos, h.pos) < EnterRadius * EnterRadius) { r.st = RS.Enter; r.t = 0f; return; }
            if (!NextHop(r, h, false)) Arrive(r, h);
        }

        void StartFlee(Hare r, Hut h)
        {
            var away = r.pos - r.threat; away.y = 0f; away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
            var toHome = h.pos - r.pos; toHome.y = 0f;
            r.fleeHome = !HutGone(h) && toHome.magnitude < 14f && (toHome.magnitude < 1f || Vector3.Dot(toHome.normalized, away) > -0.2f);
            r.goal = r.fleeHome ? h.pos : r.pos + away * 9f;
            r.st = RS.Flee; r.hopsLeft = r.fleeHome ? HopsFor(toHome.magnitude, true) : 16; r.pauseT = 0f; Fled++;
            if (r.fleeHome && toHome.magnitude < EnterRadius) { r.st = RS.Enter; r.t = 0f; return; }
            if (!NextHop(r, h, true)) Arrive(r, h);
        }

        bool NextHop(Hare r, Hut h, bool flee)
        {
            var to = r.goal - r.pos; to.y = 0f; float dist = to.magnitude;
            if (dist < 0.3f) return false;
            float len = Mathf.Min((flee ? Rf(r.rng, 0.85f, 1.25f) : Rf(r.rng, 0.32f, 0.5f)) * scale, dist);
            float zig = dist < 1.5f ? 0f : flee ? Rf(r.rng, -28f, 28f) : Rf(r.rng, -10f, 10f);
            for (int k = 0; k < 7; k++)
            {
                float turn = zig + (k == 0 ? 0f : (k % 2 == 1 ? 1 : -1) * ((k + 1) / 2) * 30f);
                var dir = Quaternion.Euler(0f, turn, 0f) * (to / dist);
                var b = r.pos + dir * len;
                if (!WalkLine(h, r.pos, b)) continue;
                r.hopA = r.pos; r.hopB = b; r.hopB.y = Ground(b.x, b.z);
                r.hopT = 0f; r.hopDur = flee ? Rf(r.rng, 0.26f, 0.3f) : Rf(r.rng, 0.3f, 0.36f) + Rf(r.rng, 0f, 0.12f);
                r.hopH = (flee ? Rf(r.rng, 0.17f, 0.24f) : Rf(r.rng, 0.08f, 0.13f)) * scale;
                r.yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                return true;
            }
            return false;
        }

        void PoseHare(Hare r, float dt)
        {
            if (!r.go.activeSelf) return;
            r.drawYaw = Mathf.MoveTowardsAngle(r.drawYaw, r.yaw, 900f * dt);
            float bury = (1f - r.rise) * 0.42f * scale;
            r.tr.SetPositionAndRotation(r.pos + Vector3.down * bury, Quaternion.Euler(0f, r.drawYaw, 0f));
            KeepOutOfScene(r);
            if (!Visible(r.pos)) return;
            bool hopping = (r.st == RS.Hop && r.pauseT <= 0f) || r.st == RS.Flee;
            float u = hopping ? Mathf.Clamp01(r.hopT / Mathf.Max(0.01f, r.hopDur)) : 0f, flee = r.st == RS.Flee ? 1f : 0f;
            float pitch = hopping ? Mathf.Lerp(-16f - 6f * flee, 12f, Mathf.SmoothStep(0.15f, 0.85f, u)) : -r.alertK * 32f + r.nibble * 6f;
            float squash = hopping ? (u < 0.12f ? Mathf.Lerp(1.06f, 1f, u / 0.12f) : (u > 0.88f ? Mathf.Lerp(1f, 0.93f, (u - 0.88f) / 0.12f) : 1f)) : 1f + 0.012f * Mathf.Sin(clock * 5.5f + r.idx);
            if (r.body) { r.body.localRotation = Quaternion.Euler(pitch, 0f, 0f) * r.qBody; r.body.localScale = new Vector3(r.bodyScale0.x * (2f - squash), r.bodyScale0.y * squash, r.bodyScale0.z * (2f - squash)); }
            if (r.st == RS.Idle || (r.st == RS.Hop && r.pauseT > 0f)) { r.lookT -= dt; if (r.lookT <= 0f) { r.lookT = Rf(r.rng, 0.6f, 2.2f); r.lookTarget = Rf(r.rng, -55f, 55f); } }
            else if (r.st != RS.Alert) r.lookTarget = 0f;
            r.lookYaw = Mathf.MoveTowards(r.lookYaw, r.lookTarget, dt * 220f);
            if (r.head) r.head.localRotation = Quaternion.Euler(r.nibble * 28f + Mathf.Sin(clock * 14f) * r.nibble * 3f + r.alertK * 18f, r.lookYaw, 0f) * r.qHead;
            r.earTw -= dt; if (r.earTw < -Rf(r.rng, 1.5f, 4f)) { r.earTw = 0.18f; r.earTwSide = r.rng.NextDouble() < 0.5 ? -1f : 1f; }
            float tw = r.earTw > 0f ? Mathf.Sin(r.earTw / 0.18f * Mathf.PI) * 22f : 0f;
            float earBack = hopping ? -(22f + 22f * flee) * Mathf.Sin(u * Mathf.PI) - 8f : -6f + r.alertK * 10f;
            if (r.earL) r.earL.localRotation = Quaternion.Euler(earBack + (r.earTwSide < 0 ? -tw : 0f), 0f, -6f) * r.qEarL;
            if (r.earR) r.earR.localRotation = Quaternion.Euler(earBack + (r.earTwSide > 0 ? -tw : 0f), 0f, 6f) * r.qEarR;
            float hind = hopping ? (u < 0.35f ? Mathf.Lerp(10f, 55f, u / 0.35f) : Mathf.Lerp(55f, -10f, Mathf.SmoothStep(0.35f, 0.9f, u))) : r.alertK * 12f;
            float front = hopping ? (u < 0.5f ? Mathf.Lerp(25f, -10f, u / 0.5f) : Mathf.Lerp(-10f, -40f, Mathf.SmoothStep(0.5f, 0.95f, u))) : r.alertK * -25f + r.nibble * -8f;
            if (r.legBL) r.legBL.localRotation = Quaternion.Euler(hind, 0f, 0f) * r.qBL;
            if (r.legBR) r.legBR.localRotation = Quaternion.Euler(hind, 0f, 0f) * r.qBR;
            if (r.legFL) r.legFL.localRotation = Quaternion.Euler(front, 0f, 0f) * r.qFL;
            if (r.legFR) r.legFR.localRotation = Quaternion.Euler(front, 0f, 0f) * r.qFR;
        }

        /// <summary>The lounger set's rule (JungleFauna.KeepOut, the same test as JungleFauna.Keep): within KeepOutRadius m of the set,
        /// or inside the scene camera's picture closer than KeepOutCamReach m, the hare is not drawn. View only.</summary>
        void KeepOutOfScene(Hare r)
        {
            bool keep = false;
            if (JungleFauna.KeepOut)
            {
                var d = r.pos - JungleFauna.KeepOutAt; d.y = 0f;
                keep = d.sqrMagnitude < JungleFauna.KeepOutRadius * JungleFauna.KeepOutRadius;
                var kc = JungleFauna.KeepOutCam;
                if (!keep && kc && kc.isActiveAndEnabled)
                {
                    var v = kc.WorldToViewportPoint(r.pos + Vector3.up * 0.4f);
                    keep = v.z > 0.2f && v.z < JungleFauna.KeepOutCamReach && v.x > -0.05f && v.x < 1.05f && v.y > -0.05f && v.y < 1.05f;
                }
            }
            if (keep == r.kept) return;
            r.kept = keep; if (keep) KeptOutTotal++;
            if (r.rends != null) for (int i = 0; i < r.rends.Length; i++) if (r.rends[i]) r.rends[i].enabled = !keep;
        }

        void Kill(Hare r)
        {
            if (r.st == RS.Dead || r.st == RS.Hidden) return;
            var p = r.pos + Vector3.up * 0.18f;
            var fx = OpenZFxPlayer.I; if (fx) fx.Dust(p, 0.4f); else FxManager.I?.Dust(p, 0.4f);
            bits.Burst(p, 9, 0, 1.8f, 0.05f, r.rng); bits.Burst(p, 5, 1, 1.8f, 0.045f, r.rng);
            if (playSounds) DetailKit.Sound("critter_small_death", p, 0.6f, "critter", 0.05f, -1, "rp_pop_a", "rp_pop_b", "rp_pop_c");
            r.st = RS.Dead; r.t = Rf(r.rng, respawn.x, respawn.y); r.go.SetActive(false); Killed++;
        }

        void Thump(Vector3 p) { DetailKit.Sound("amb_l02_rabbit_thump", p, 0.55f, "critter", 0.4f, -1, "dtl_rabbit_thump_a", "dtl_rabbit_thump_b"); }
        void Patter(Vector3 p) { DetailKit.Sound("amb_l02_rabbit_hop", p, 0.3f, "critter", 0.8f, -1, "dtl_rabbit_hop_a", "dtl_rabbit_hop_b"); }

        void OnExplosion(Vector3 at, float strength)
        {
            lastBlast = at; lastBlastT = clock;
            float reach = 1.2f + 2.4f * strength + 0.3f;
            foreach (var r in rabs) if (r.st != RS.Dead && r.st != RS.Hidden && Flat(r.pos, at) < reach * reach) Kill(r);
        }

        void RefreshUnits()
        {
            unitPos.Clear(); unitVeh.Clear(); obs.Clear();
            foreach (var kv in game.Views)
            {
                var v = kv.Value; if (!v || v.Dying || !v.gameObject.activeInHierarchy || v.Obj == null) continue;
                var o = v.Obj; if (o.Kind != ZKind.Robot && o.Kind != ZKind.Vehicle && o.Kind != ZKind.Cannon) continue;
                var p = v.transform.position;
                if (o.IsMoving && o.Kind != ZKind.Cannon) { unitPos.Add(p); unitVeh.Add(o.Kind == ZKind.Vehicle); }
                else obs.Add(p);
            }
            for (int i = 0; i < unitPos.Count; i++)                                                // a moving vehicle rolling over a hare
            {
                if (!unitVeh[i]) continue;
                foreach (var r in rabs) if ((r.st == RS.Idle || r.st == RS.Hop || r.st == RS.Flee || r.st == RS.Alert) && Flat(r.pos, unitPos[i]) < 1.1f * 1.1f) Kill(r);
            }
        }

        void ReadShots()
        {
            var ev = game.World.Events;
            for (int i = 0; i < ev.Count; i++)
            {
                var e = ev[i];
                if (e.type != ZEventType.Hitscan || !DetailKit.ShotSegment(game, e, out var a, out var b)) continue;
                shotA = a; shotB = b; shotT = clock;
                foreach (var r in rabs) if (r.st != RS.Dead && r.st != RS.Hidden && DetailKit.DistPointSegment(r.pos + Vector3.up * 0.2f * scale, a, b) < 0.28f) Kill(r);
            }
        }

        void Update()
        {
            if (!game || game.World == null) return;
            if (!cam) { cam = Camera.main; if (cam && !rts) rts = cam.GetComponent<RtsCamera>(); }
            float dt = game.paused ? 0f : Mathf.Min(Time.deltaTime, 0.05f) * Mathf.Clamp(game.gameSpeed, 0.25f, 2f);
            clock += dt;
            unitsT -= dt; if (unitsT <= 0f) { unitsT = 0.2f; RefreshUnits(); }
            ReadShots();                                                                          // before CombatView (LateUpdate) clears the events
            int n = 0;
            if (dt > 0f) foreach (var r in rabs) StepHare(r, dt);
            int kept = 0;
            foreach (var r in rabs) { PoseHare(r, dt); if (r.st != RS.Hidden && r.st != RS.Dead) { n++; if (r.kept) kept++; } }
            Out = n; KeptOutNow = kept;
            bits.StepAndDraw(dt, game);
        }

        [ContextMenu("Test: every hare out now")]
        public void TestOut() { foreach (var r in rabs) if (r.st == RS.Hidden) r.t = 0.01f; }

        public static string Status()
        {
            if (!I) return "JungleHutAnimals not running (not level 16)";
            return $"[JungleHutAnimals] {I.rabs.Count} hares in {I.huts.Count} huts: {Out} out now; came out {CameOut}, went home {WentHome}, fled {Fled}, killed {Killed}; kept out of the scene's picture {KeptOutNow} now ({KeptOutTotal} in all)";
        }
    }
}
