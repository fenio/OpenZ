using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1 item 360 (rule DV124, Sim/ZWorld.HeavyFall.cs): a big faller that comes down on a tree destroys it, at the moment it
    /// hits, the way the game's trees already fall (the break, the stump, the fall away from the hit with its creak, the crown's thud,
    /// leaves and dust or the swamp's splash), with a heavy wood crack and splinters where it hit. View side:
    /// <list type="bullet">
    /// <item>a tree of the RULES that a faller fells: the rules destroy it when the faller lands (their Death event); its drawn break
    /// waits (CombatView's one-line hook, <see cref="HoldsDeath"/>) until the drawn faller really reaches it: a big piece enters its trunk
    /// (a trigger round the drawn trunk), the fort cannon's landing frame (it lies on it), the dropped ship's ground hit; at the latest
    /// <see cref="HoldMaxSec"/> after the drawn break (counted: <see cref="ReleasedByTimeout"/>, the trees felled without a touch). A
    /// blown-off turret lands on its blast's frame: no wait;</item>
    /// <item>the shuttle's pieces are steered onto the rules' trees: the whole break's horizontal throw is shifted so its middle comes
    /// down where the rules put the hull (<see cref="ShuttleBurst"/>, <see cref="ShuttleDrop"/>), and one big piece flies into each tree
    /// the rules fell (homing on its trunk until it reaches it);</item>
    /// <item>the fort cannon shot to pieces while its fort stands flies on the rules' toss (<see cref="GunTossLanding"/>: FortGunView's
    /// landing, the same seeded numbers, the rules' heading), so it lands exactly on the trees the rules fell;</item>
    /// <item>a landing turret lies on the trees it fells and never through a standing one: <see cref="TurretRest"/> turns it about the
    /// vertical (24 ways) to the way that covers every tree it fells and touches no standing trunk;</item>
    /// <item>round every crash (the shuttle's break, the fort cannon's landing, a vehicle or gun wreck near trees) the trunks are solid
    /// for the big pieces (OzTreeCrush.Trunks.cs): a standing tree of the rules stops them (they bounce off or lie beside it, with a knock
    /// and a few leaves), a drawn tree with no rules (the jungle's other trees, the desert's view palms) falls when a big piece touches
    /// it; small chips pass as before.</item>
    /// </list>
    /// Switch: <see cref="Enabled"/> (off = no hold, no steering, no trunks: the trees the rules destroy fall at once, as any other).
    /// Created by ZGame with the rule's switch.
    /// </summary>
    public partial class OzTreeCrush : MonoBehaviour
    {
        public static bool Enabled = true;
        public static OzTreeCrush I { get; private set; }
        /// <summary>A piece this heavy (kg) and this long (m) or more is big: it fells a drawn tree it touches and breaks a held one.</summary>
        public static float BigPieceKg = 50f, BigPieceM = 1.0f;
        /// <summary>A held tree breaks at the latest this long (s) after its faller broke apart / landed, or after the hold began when none is seen.</summary>
        public static float HoldMaxSec = 3.5f, HoldNoBreakSec = 8f;
        /// <summary>The shuttle's whole throw is shifted at most this much (m/s) to come down on the rules' place; an aimed piece flies at most this fast (m/s).</summary>
        public static float CarryMax = 9f, AimMaxSpeed = 8f, HomingMaxDv = 2.5f;
        static readonly string[] CrushClips = { "L16/sd_bridge_collapse_wood_a", "L16/sd_bridge_collapse_wood_b" };

        // ------------------------------------------------------------------ counts (the Play check prints them)
        public static int Held, ReleasedByTouch, ReleasedByLanding, ReleasedByTimeout, ReleasedEarly, ReleasedAtOnce, TurretCrushes, FloraFelled, PropsFelled, GameOverFelled,
                          ShuttleCarried, ShuttleCarrySkipped, DropCarried, ShuttleAimed, Rehomed, GunTossesFromRules, GunLandings, TurretLandings, TurretTurned,
                          TurretStandingUnder, TurretTargetsMissed, CrushSounds;
        public static string LastLog = "-";

        public static void Ensure()
        {
            if (I || !Application.isPlaying) return;
            new GameObject("OzTreeCrush").AddComponent<OzTreeCrush>();
        }

        static void ResetCounts()
        {
            Held = ReleasedByTouch = ReleasedByLanding = ReleasedByTimeout = ReleasedEarly = ReleasedAtOnce = TurretCrushes = FloraFelled = PropsFelled = GameOverFelled = 0;
            ShuttleCarried = ShuttleCarrySkipped = DropCarried = ShuttleAimed = Rehomed = GunTossesFromRules = GunLandings = TurretLandings = TurretTurned = 0;
            TurretStandingUnder = TurretTargetsMissed = CrushSounds = 0; LastLog = "-";
            ResetTrunkCounts();
        }

        ZGame game; ZWorld world;
        int lastShuttleBreaks;

        sealed class Held1 { public ZObject o; public ZHeavyFall f; public float since; public Vector3 from; }
        sealed class Seen { public float brokeAt = -1f, landAt = -1f; public bool down; }
        sealed class Aim { public Rigidbody rb; public Collider col; public Vector3 foot; public float yHit; public int simId; public float until; }
        readonly List<Held1> held = new List<Held1>();
        readonly HashSet<int> earlyBroken = new HashSet<int>();
        readonly Dictionary<int, Seen> seen = new Dictionary<int, Seen>();
        readonly List<Aim> aims = new List<Aim>();

        void Awake()
        {
            if (I && I != this) { Destroy(gameObject); return; }
            I = this; ResetCounts();
            OzFortGunBreakup.Broke += OnGunBroke;
        }

        void OnDestroy()
        {
            OzFortGunBreakup.Broke -= OnGunBroke;
            ClearTrunks();
            if (I == this) I = null;
        }

        Seen SeenOf(ZHeavyFall f) { if (!seen.TryGetValue(f.seq, out var s)) seen[f.seq] = s = new Seen(); return s; }
        static Vector3 World(ZGame g, double px, double py) { var p = ZGame.PxToWorld(px, py); if (g) p.y = g.GroundY(p.x, p.z); return p; }

        void Update()
        {
            if (!Enabled) return;
            if (!game) game = ZGame.I;
            var w = game ? game.World : null;
            if (w == null) return;
            if (w != world)
            {   // a new match: nothing of the last one is held
                world = w; held.Clear(); seen.Clear(); aims.Clear(); earlyBroken.Clear(); ClearTrunks(); lastShuttleBreaks = OzShuttleBreakup.Breaks;
            }
            if (game.paused) return;
            ScanDeaths(w);                                                                          // vehicle / gun wrecks near trees: their trunks solid for the big parts
            if (OzShuttleBreakup.Breaks != lastShuttleBreaks) { lastShuttleBreaks = OzShuttleBreakup.Breaks; OnShuttleBroke(w); }
            Homing(w);
            StepHolds(w);
            StepTrunks(w);
        }

        // ------------------------------------------------------------------ the hold (CombatView.Death hook)
        /// <summary>CombatView.Death, a detail prop destroyed by the rules: true = this view takes care of its drawn break (a tree a big
        /// faller fells breaks when the drawn faller reaches it); false = the usual break now.</summary>
        public static bool HoldsDeath(ZGame g, ZEvent e, ZObject o)
        {
            if (!Enabled || !I || !g || g.World == null || o == null) return false;
            var f = g.World.HeavyFallOfTree(o.Id);
            if (f == null || !f.landed || !f.trees.Contains(o.Id)) return false;                    // not felled by a big faller
            return I.Hold(g, e, o, f);
        }

        bool Hold(ZGame g, ZEvent e, ZObject o, ZHeavyFall f)
        {
            if (earlyBroken.Remove(o.Id)) { ReleasedEarly++; return true; }                        // a big piece reached it first: its drawn break came already
            var foot = TreeFoot(g, o);
            var from = World(g, f.fromX, f.fromY);
            if (f.kind == ZHeavyFallKind.Turret)
            {   // the turret lands on its blast's frame (UnitView.TossTurret): it is on the tree now
                TurretCrushes++; Extras(foot + Vector3.up * 0.8f, o.Id);
                return false;
            }
            var s = SeenOf(f);
            if (s.down) { Break(g, o, from, foot + Vector3.up * 0.8f); ReleasedAtOnce++; return true; }   // the cannon / the dropped hull lies on it already
            held.Add(new Held1 { o = o, f = f, since = Time.time, from = from }); Held++;
            LastLog = $"tree #{o.Id} held for the {f.kind} #{f.fallerId} (rules land {f.landTime:0.00})";
            return true;
        }

        void Release(int i, string how, Vector3 from, Vector3 hit)
        {
            var h = held[i]; held.RemoveAt(i);
            if (how == "touch") ReleasedByTouch++; else if (how == "landing") ReleasedByLanding++; else ReleasedByTimeout++;
            Break(game, h.o, from, hit);
            LastLog = $"tree #{h.o.Id} breaks: {how} ({h.f.kind} #{h.f.fallerId})";
        }

        void ReleaseAll(ZHeavyFall f, string how)
        {
            for (int i = held.Count - 1; i >= 0; i--)
                if (held[i].f == f) { var foot = TreeFoot(game, held[i].o); Release(i, how, held[i].from, foot + Vector3.up * 0.8f); }
        }

        /// <summary>The drawn break of a tree the rules destroyed: its own break (CombatView's path) from `from`, the heavy crack and splinters at `hit`.</summary>
        void Break(ZGame g, ZObject o, Vector3 from, Vector3 hit)
        {
            Extras(hit, o.Id);
            if (!DetailPropHub.SimDied(o, from)) { var dp = DetailPropHub.OfSim(o.Id); if (dp) dp.Break(from); }
            TrunkDown(o.Id);
        }

        /// <summary>The heavy wood crack, splinters, a puff of dust (one line in the sound log: dtl_tree_crush).</summary>
        internal static void Extras(Vector3 hit, int seed)
        {
            DetailProp.Play("dtl_tree_crush", CrushClips, hit, 0.75f, Random.Range(0.9f, 1.05f), -1, 0f); CrushSounds++;
            OzTreeFall.Splinters(hit, seed * 31 + Time.frameCount);
            var fx = OpenZFxPlayer.I; if (fx && fx.isActiveAndEnabled) fx.Dust(hit, 0.7f);
        }

        void StepHolds(ZWorld w)
        {
            for (int i = held.Count - 1; i >= 0; i--)
            {
                var h = held[i]; var s = SeenOf(h.f);
                if (!s.down && s.landAt >= 0f && Time.time >= s.landAt) { s.down = true; ReleaseAll(h.f, "landing"); i = Mathf.Min(i, held.Count); continue; }   // the tossed cannon is down there (also whole, in water)
                float due = s.brokeAt >= 0f ? s.brokeAt + HoldMaxSec : h.since + HoldNoBreakSec;
                if (Time.time >= due) { var foot = TreeFoot(game, h.o); Release(i, "timeout", h.from, foot + Vector3.up * 0.8f); }
            }
        }

        /// <summary>The drawn trunk's foot of a tree of the rules (its drawn tree, else the rules' foot).</summary>
        Vector3 TreeFoot(ZGame g, ZObject o)
        {
            var w = g ? g.World : null;
            if (w != null && w.IsTreeTile(o) && OzTreeFall.RulesTree(o.LocX >> 4, o.LocY >> 4, out var ft, out _, out _)) return ft;
            var dp = DetailPropHub.OfSim(o.Id); if (dp) return dp.transform.position;
            if (w != null)
                for (int i = 0; i < w.HeavyTreeCount; i++) { var t = w.HeavyTreeAt(i, out double fx, out double fy); if (t == o) return World(g, fx, fy); }
            return g ? g.CenterWorld(o) : Vector3.zero;
        }

        // ------------------------------------------------------------------ the shuttle (OzShuttleBreakup hooks)
        /// <summary>OzShuttleBreakup.StartFall hook (the rifle-class drop): its way on is set so the dropped ship comes down where the rules
        /// put its hull (the drop's own drag kept: x(T) = v (1 - e^-0.5T) / 0.5).</summary>
        public static void ShuttleDrop(ZObject o, ref Vector3 vel, Vector3 pivot, float T)
        {
            var g = ZGame.I; var w = g ? g.World : null;
            if (!Enabled || !I || w == null || o == null) return;
            var f = w.HeavyFallOf(o.Id); if (f == null || f.kind != ZHeavyFallKind.Shuttle) return;
            float k = (1f - Mathf.Exp(-0.5f * Mathf.Max(0.05f, T))) / 0.5f;
            var P = ZGame.PxToWorld(f.x, f.y);
            var need = new Vector3((P.x - pivot.x) / k, vel.y, (P.z - pivot.z) / k);
            var d = need - vel; d.y = 0f;
            if (d.magnitude > CarryMax) { ShuttleCarrySkipped++; return; }
            vel = need; DropCarried++;
        }

        /// <summary>OzShuttleBreakup.Break hook, right after the launch: in the air the whole throw is shifted so its middle comes down where
        /// the rules put the hull; then one big piece is sent into each tree the rules fell (and kept on it, OzTreeCrush.Homing).</summary>
        public static void ShuttleBurst(ZObject o, List<OzShuttleBreakup> pieces, Vector3 centre, bool air, string how)
        {
            var g = ZGame.I; var w = g ? g.World : null;
            if (!Enabled || !I || w == null || o == null || pieces == null) return;
            var f = w.HeavyFallOf(o.Id); if (f == null || f.kind != ZHeavyFallKind.Shuttle) return;
            var P = World(g, f.x, f.y);
            if (air && how != "drop") I.Carry(pieces, P);
            I.AimPieces(g, w, f, pieces, how == "drop" || !air);
        }

        static float Damping(Rigidbody rb) => rb ? rb.linearDamping : 0f;

        /// <summary>Flight of a body from height y0 with vertical speed vy down to height y1 under gravity and the body's damping (Unity:
        /// v *= 1 / (1 + d dt) a step): the time, and how far a horizontal speed of 1 m/s carries it in that time.</summary>
        static float Flight(float y0, float vy, float y1, float damp, out float carry)
        {
            float dt = Mathf.Max(0.005f, Time.fixedDeltaTime), g = -Physics.gravity.y, t = 0f, y = y0, v = vy, h = 1f; carry = 0f;
            for (int i = 0; i < 1000; i++)
            {
                v -= g * dt; float k = 1f / (1f + damp * dt); v *= k; h *= k;
                float yn = y + v * dt;
                if (yn <= y1 && v <= 0f) { float a = Mathf.Abs(y - yn) > 1e-5f ? (y - y1) / (y - yn) : 1f; t += dt * a; carry += h * dt * a; return t; }
                y = yn; t += dt; carry += h * dt;
            }
            return t;
        }

        void Carry(List<OzShuttleBreakup> pieces, Vector3 P)
        {
            Vector3 m = Vector3.zero, v = Vector3.zero; float total = 0f, damp = 0f;
            foreach (var pc in pieces)
            {
                var rb = pc && pc.PieceCollider ? pc.PieceCollider.attachedRigidbody : null; if (!rb || rb.isKinematic) continue;
                m += rb.worldCenterOfMass * rb.mass; v += rb.linearVelocity * rb.mass; total += rb.mass; damp = Mathf.Max(damp, Damping(rb));
            }
            if (total <= 0f) return;
            m /= total; v /= total;
            Flight(m.y, v.y, P.y + 0.6f, damp, out float carry);
            if (carry <= 0.05f) return;
            var natural = new Vector3(m.x + v.x * carry, 0f, m.z + v.z * carry);
            var delta = new Vector3(P.x - natural.x, 0f, P.z - natural.z) / carry;
            if (delta.magnitude > CarryMax) { ShuttleCarrySkipped++; return; }
            foreach (var pc in pieces)
            {
                var rb = pc && pc.PieceCollider ? pc.PieceCollider.attachedRigidbody : null; if (!rb || rb.isKinematic) continue;
                rb.linearVelocity += delta;
            }
            ShuttleCarried++;
        }

        bool BigPiece(OzShuttleBreakup pc) => pc && pc.PieceCollider && pc.PieceCollider.attachedRigidbody && pc.MassKg >= BigPieceKg && pc.Size >= BigPieceM;

        void AimPieces(ZGame g, ZWorld w, ZHeavyFall f, List<OzShuttleBreakup> pieces, bool low)
        {
            var pool = new List<OzShuttleBreakup>();
            foreach (var pc in pieces) if (BigPiece(pc)) pool.Add(pc);
            pool.Sort((a, b) => b.MassKg.CompareTo(a.MassKg));
            var P = World(g, f.x, f.y);
            var targets = new List<(int id, Vector3 foot, float h)>();
            for (int i = 0; i < w.HeavyTreeCount; i++)
            {
                var o = w.HeavyTreeAt(i, out double fx, out double fy);
                if (!f.targets.Contains(o.Id)) continue;
                var foot = TreeFoot(g, o); float height = 4f;
                if (w.IsTreeTile(o) && OzTreeFall.RulesTree(o.LocX >> 4, o.LocY >> 4, out var ft, out var hh, out bool down)) { if (down) continue; foot = ft; height = hh; }
                targets.Add((o.Id, foot, height));
            }
            targets.Sort((a, b) => (a.foot - P).sqrMagnitude.CompareTo((b.foot - P).sqrMagnitude));
            foreach (var t in targets)
            {
                if (pool.Count == 0) break;
                float yHit = t.foot.y + (low ? 0.7f : Mathf.Clamp(t.h * 0.35f, 1f, 3.5f));
                int best = -1; float bestD = float.MaxValue;
                for (int i = 0; i < pool.Count; i++)
                {
                    var rb = pool[i].PieceCollider.attachedRigidbody; var c = rb.worldCenterOfMass;
                    Flight(c.y, rb.linearVelocity.y, yHit, Damping(rb), out float carry);
                    var land = new Vector3(c.x + rb.linearVelocity.x * carry, 0f, c.z + rb.linearVelocity.z * carry);
                    float d = (new Vector3(t.foot.x, 0f, t.foot.z) - land).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = i; }
                }
                var pc = pool[best]; pool.RemoveAt(best);
                var body = pc.PieceCollider.attachedRigidbody;
                if (SteerTo(body, t.foot, yHit, true)) { aims.Add(new Aim { rb = body, col = pc.PieceCollider, foot = t.foot, yHit = yHit, simId = t.id, until = Time.time + 6f }); ShuttleAimed++; }
            }
        }

        /// <summary>Sets the body's way so it reaches the trunk at (foot, yHit): its own fall time, longer (a hop) when the trunk is far for it.</summary>
        static bool SteerTo(Rigidbody rb, Vector3 foot, float yHit, bool first)
        {
            if (!rb || rb.isKinematic) return false;
            var c = rb.worldCenterOfMass; var v = rb.linearVelocity; float damp = Damping(rb);
            var dh = new Vector3(foot.x - c.x, 0f, foot.z - c.z); float dist = dh.magnitude;
            float t = Flight(c.y, v.y, yHit, damp, out float carry);
            if (first && (carry <= 0.05f || dist / Mathf.Max(0.05f, carry) > AimMaxSpeed))
            {   // too far for its fall: a higher, longer flight (vy so that it reaches yHit after dist / AimMaxSpeed s)
                float tt = Mathf.Max(0.3f, dist / AimMaxSpeed), gr = -Physics.gravity.y;
                v.y = (yHit - c.y + 0.5f * gr * tt * tt) / tt;
                t = Flight(c.y, v.y, yHit, damp, out carry);
            }
            if (carry <= 0.05f) return false;
            var need = dh / carry;
            if (!first) { var d = need - new Vector3(v.x, 0f, v.z); if (d.magnitude > HomingMaxDv) need = new Vector3(v.x, 0f, v.z) + d.normalized * HomingMaxDv; }
            rb.linearVelocity = new Vector3(need.x, v.y, need.z);
            return true;
        }

        /// <summary>Every frame: an aimed piece is kept on its trunk (small corrections, e.g. after the heavy shell's second blast) until
        /// it reaches it, lands, or its tree is down.</summary>
        void Homing(ZWorld w)
        {
            for (int i = aims.Count - 1; i >= 0; i--)
            {
                var a = aims[i];
                if (!a.rb || a.rb.isKinematic || Time.time > a.until || TrunkIsDown(a.simId)) { aims.RemoveAt(i); continue; }
                var c = a.rb.worldCenterOfMass;
                if (c.y < a.yHit - 0.2f) { aims.RemoveAt(i); continue; }
                if (SteerTo(a.rb, a.foot, a.yHit, false)) Rehomed++;
            }
        }

        void OnShuttleBroke(ZWorld w)
        {
            var g = game; var at = OzShuttleBreakup.LastBreakGround;
            ZHeavyFall f = null; float bd = 16f;
            for (int i = w.HeavyFalls.Count - 1; i >= 0; i--)
            {
                var x = w.HeavyFalls[i]; if (x.kind != ZHeavyFallKind.Shuttle || w.Time - x.startTime > 12.0 || SeenOf(x).brokeAt >= 0f) continue;
                var p = ZGame.PxToWorld(x.x, x.y); float d = new Vector2(p.x - at.x, p.z - at.z).magnitude;
                if (d < bd) { bd = d; f = x; }
            }
            var cols = new List<Collider>();
            foreach (var pc in OzShuttleBreakup.LastPieces) if (pc && pc.PieceCollider && pc.PieceCollider.attachedRigidbody) { Include(pc.PieceCollider); cols.Add(pc.PieceCollider); }
            var centre = f != null ? World(g, f.x, f.y) : at;
            if (f != null)
            {
                var s = SeenOf(f); s.brokeAt = Time.time;
                if (OzShuttleBreakup.LastHow == "drop") { s.down = true; ReleaseAll(f, "landing"); }    // the dropped hull is on the ground there
            }
            Arm(w, centre, ArmRadius, CrashArmSeconds, f);
            StartOverlaps(cols, centre);
            LastLog = $"shuttle broke ({OzShuttleBreakup.LastHow}) at {at.x:0.0}, {at.z:0.0}{(f != null ? $": the rules' fall #{f.seq}, {f.targets.Count} trees under it, {bd:0.0} m from the rules' place" : ": no fall of the rules")}";
        }

        // ------------------------------------------------------------------ the fort cannon
        /// <summary>FortGunView.StartToss hook (after its landing place): a cannon the rules tossed (shot to pieces while its fort stands)
        /// flies on the rules' heading; the same seeded offset, distance and landing turn as before, so it lands where the rules fell the
        /// trees. Any other toss (with its fort, at the ending; or one the rules left to the view) is drawn as before.</summary>
        public static void GunTossLanding(ZObject fort, ref float hdg, ref Vector3 land, float tossSeconds)
        {
            var g = ZGame.I; var w = g ? g.World : null;
            if (!Enabled || w == null || fort == null) return;
            var f = w.HeavyFallGunToss(fort.Id); if (f == null) return;
            hdg = (float)f.headingDeg;
            var p = ZGame.PxToWorld(f.x, f.y); land.x = p.x; land.z = p.z;
            if (I) I.SeenOf(f).landAt = Time.time + Mathf.Max(0.1f, tossSeconds);
            GunTossesFromRules++;
            LastLog = $"fort #{fort.Id} cannon tossed on the rules' heading {hdg:0.0} to {p.x:0.0}, {p.z:0.0}: {f.targets.Count} trees under its landing";
        }

        void OnGunBroke(Vector3 impact)
        {
            if (!Enabled) return;
            if (!game) game = ZGame.I;
            var w = game ? game.World : null; if (w == null) return;
            GunLandings++;
            ZHeavyFall f = null; float bd = 7f;
            for (int i = w.HeavyFalls.Count - 1; i >= 0; i--)
            {
                var x = w.HeavyFalls[i]; if (x.kind != ZHeavyFallKind.FortGun || SeenOf(x).down || w.Time - x.startTime > 60.0) continue;
                var p = ZGame.PxToWorld(x.x, x.y); float d = new Vector2(p.x - impact.x, p.z - impact.z).magnitude;
                if (d < bd) { bd = d; f = x; }
            }
            if (f != null) { var s = SeenOf(f); s.down = true; s.brokeAt = Time.time; ReleaseAll(f, "landing"); }
            var cols = new List<Collider>();
            foreach (var pc in OzFortGunBreakup.LastPieces)
                if (pc) foreach (var c in pc.cols) if (c && c.attachedRigidbody) { Include(c); cols.Add(c); }
            Arm(w, impact, ArmRadius, CrashArmSeconds, f);
            StartOverlaps(cols, impact);
            LastLog = $"fort cannon landed at {impact.x:0.0}, {impact.z:0.0}{(f != null ? $": the rules' toss #{f.seq}, {f.trees.Count} trees felled under it" : w.GameOver ? ": with its fort (the game is over: the view fells what it lands on)" : ": a toss the rules left to the view")}";
        }

        // ------------------------------------------------------------------ the turret (UnitView.UpdateToss hook)
        static readonly Dictionary<int, List<Vector3[]>> turretShape = new Dictionary<int, List<Vector3[]>>();

        /// <summary>The landed turret's shape in its own space: per renderer the corners and the middles of its box (for its outline on the ground).</summary>
        static List<Vector3[]> ShapeOf(Transform piece)
        {
            int key = piece.GetInstanceID();
            if (turretShape.TryGetValue(key, out var s)) return s;
            s = new List<Vector3[]>();
            foreach (var r in piece.GetComponentsInChildren<Renderer>())
            {
                if (!r || !r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                var b = r.localBounds; var pts = new Vector3[27]; int n = 0;
                for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) for (int k = 0; k < 3; k++)
                {
                    var q = new Vector3(b.min.x + b.size.x * i * 0.5f, b.min.y + b.size.y * j * 0.5f, b.min.z + b.size.z * k * 0.5f);
                    var wq = r.transform.TransformPoint(q);
                    var lq = piece.InverseTransformPoint(wq);
                    pts[n++] = Vector3.Scale(lq, piece.lossyScale);
                }
                s.Add(pts);
            }
            if (turretShape.Count > 64) turretShape.Clear();
            turretShape[key] = s;
            return s;
        }

        /// <summary>Is the trunk at foot (radius r) under the turret lying at p, turned q? (per renderer: its outline on the ground)</summary>
        static bool Under(List<Vector3[]> shape, Vector3 p, Quaternion q, Vector3 foot, float r)
        {
            var flat = new List<Vector2>(27);
            foreach (var pts in shape)
            {
                flat.Clear();
                foreach (var l in pts) { var wq = p + q * l; flat.Add(new Vector2(wq.x, wq.z)); }
                if (NearHull(flat, new Vector2(foot.x, foot.z), r)) return true;
            }
            return false;
        }

        static bool NearHull(List<Vector2> pts, Vector2 x, float r)
        {
            pts.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var h = new List<Vector2>(pts.Count + 1);
            float Cr(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            for (int i = 0; i < pts.Count; i++) { while (h.Count >= 2 && Cr(h[h.Count - 2], h[h.Count - 1], pts[i]) <= 0f) h.RemoveAt(h.Count - 1); h.Add(pts[i]); }
            for (int i = pts.Count - 2, lo = h.Count + 1; i >= 0; i--) { while (h.Count >= lo && Cr(h[h.Count - 2], h[h.Count - 1], pts[i]) <= 0f) h.RemoveAt(h.Count - 1); h.Add(pts[i]); }
            if (h.Count > 1) h.RemoveAt(h.Count - 1);
            if (h.Count < 3) { foreach (var q in h) if ((q - x).sqrMagnitude <= r * r) return true; return false; }
            bool inside = true;
            for (int i = 0; i < h.Count; i++)
            {
                var a = h[i]; var b = h[(i + 1) % h.Count];
                if (Cr(a, b, x) < 0f) inside = false;
                var e = b - a; float l = e.sqrMagnitude; float t = l > 1e-8f ? Mathf.Clamp01(Vector2.Dot(x - a, e) / l) : 0f;
                if ((a + e * t - x).sqrMagnitude <= r * r) return true;
            }
            return inside;
        }

        /// <summary>The rules' trees this turret fells (its fall's record; one frame early: the same rule on its debris point).</summary>
        static HashSet<int> TurretTargets(ZWorld w, ZObject unit, Vector3 p, float cover)
        {
            var ids = new HashSet<int>();
            if (!ZWorld.HeavyFallDeviation) return ids;
            var f = w.HeavyFallOf(unit.Id);
            if (f != null && f.kind == ZHeavyFallKind.Turret) { foreach (int id in f.trees) ids.Add(id); return ids; }
            ZMissile m = null;
            foreach (var x in w.PendingMissiles) if (x.debris && x.attackerId == unit.Id) { m = x; break; }
            if (m == null) return ids;                                                                  // no debris point any more: it fells nothing
            var pre = new ZHeavyFall { kind = ZHeavyFallKind.Turret, x = m.x, y = m.y, radiusPx = cover * 8.0 };
            if (ZWorld.HeavyTurretRockClearM > 0 && w.HeavyTurretByRock(m.x, m.y, unit.CenterX, unit.CenterY)) return ids;
            foreach (int id in w.HeavyTreesUnder(pre)) ids.Add(id);
            return ids;
        }

        /// <summary>UnitView.UpdateToss hook, the turret's landing frame (p, its resting turn): the turn about the vertical that lies on
        /// every tree it fells (the rules' trees within ZWorld.TurretCoverM of its blast) and on no standing trunk; the drawn trees with no
        /// rules it then lies on fall now.</summary>
        public static Quaternion TurretRest(Transform piece, Vector3 p, Quaternion rest, ZObject unit)
        {
            var g = ZGame.I; var w = g ? g.World : null;
            if (!Enabled || !I || !piece || unit == null || w == null) return rest;
            float cover = (float)ZWorld.TurretCoverM(unit.Type); if (cover <= 0f) return rest;
            var near = I.TreesNear(w, p, 7f);
            if (near.Count == 0) return rest;
            TurretLandings++;
            var shape = ShapeOf(piece);
            if (shape.Count == 0) return rest;
            var targets = TurretTargets(w, unit, p, cover);
            float best = float.MaxValue; Quaternion pick = rest; int pickK = 0;
            for (int k = 0; k < 24; k++)
            {
                float deg = (k % 2 == 0 ? 1 : -1) * 15f * ((k + 1) / 2);
                var q = Quaternion.AngleAxis(deg, Vector3.up) * rest;
                float cost = 0f;
                foreach (var t in near)
                {
                    bool target = t.rules && targets.Contains(t.simId);
                    bool under = Under(shape, p, q, t.foot, t.r);
                    if (target && !under) cost += 3f;
                    else if (!target && t.rules && under) cost += 10f;                                // a standing tree of the rules: never under it
                }
                if (cost < best - 0.01f) { best = cost; pick = q; pickK = k; }
            }
            if (pickK != 0) TurretTurned++;
            var from = g.CenterWorld(unit);
            foreach (var t in near)
            {
                bool target = t.rules && targets.Contains(t.simId);
                bool under = Under(shape, p, pick, t.foot, t.r);
                if (t.rules) { if (target && !under) TurretTargetsMissed++; else if (!target && under) TurretStandingUnder++; continue; }
                if (!under) continue;
                var hit = t.foot + Vector3.up * 0.6f;
                if (I.FellDrawn(t, from, hit)) Extras(hit, t.key);
            }
            return pick;
        }

        /// <summary>ZGame (with the rule's switch): how near a rock (m) a turret fells no tree. Above 0 only when the view moves turrets off
        /// the rocks (the rocks solid for every big piece: OzRockSolid.AllBigDebris, found by name so this file compiles without it).</summary>
        public static double TurretRockClear()
        {
            var fi = typeof(OzRockSolid).GetField("AllBigDebris", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            bool moves = fi != null && fi.FieldType == typeof(bool) && (bool)fi.GetValue(null) && OzRockSolid.Enabled;
            return moves ? TurretRockClearM : 0.0;
        }
        /// <summary>A turret's landed disc (at most 2 m), its 0.3 m clear air and 0.6 m skid, a rock's drawn reach past its tile (1.4 m).</summary>
        public const double TurretRockClearM = 4.3;
    }
}
