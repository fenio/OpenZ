using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The squad aboard the playable shuttle is lost with it (rule DV118); here their bodies fall out of the breaking ship among its
    /// pieces (view only). Each body is the game's own blast-thrown robot body: a real ragdoll (RobotRagdoll.Launch, the limbs flailing
    /// with RagdollFlail, the soft whoosh as it leaves and the metal thud where it lands: OzBodySounds), thrown with the ship's own
    /// velocity and the break's push (away from the killing hit); when the ragdoll budget is used up, the game's scripted blast fall
    /// (the airborne pose, tumbling under gravity, the same whoosh and thud). The ragdoll's bones are moved onto the heavy debris layer
    /// so a body hits the pieces and the pieces hit it (it can land on a piece and slide off it); a body that starts inside a piece (the
    /// seats are inside the hull) ignores that piece until they are apart, and a body that comes to rest inside one is pushed out of
    /// it before it freezes. Over the river the shared water rule splashes it. The bodies lie on the ground with the wreck and sink
    /// away with it (the break-up's wreck time).
    /// </summary>
    public sealed partial class OzShuttleCrewFall : MonoBehaviour
    {
        /// <summary>Proof: bodies thrown (as ragdolls / scripted), landed, resting on a piece, pushed out of a piece, held apart at the start.</summary>
        public static int Thrown, Ragdolls, Scripted, LandedCount, OnPiece, PushedOut, HeldPairs, ReleasedPairs, InWater;
        public static readonly List<OzShuttleCrewFall> Last = new List<OzShuttleCrewFall>();
        public static float LastThrowAt = -1f;

        public bool Ragdoll => rd;
        public bool Landed { get; private set; }
        public float LandedAt { get; private set; } = -1f;
        public float FellM { get; private set; }
        /// <summary>The lowest point of the body over the ground once it rests (m).</summary>
        public float RestGap { get; private set; } = 99f;
        public string RestOn { get; private set; } = "-";

        RobotRagdoll rd; Animator anim; System.Action<GameObject> landPose; Transform hips;
        /// <summary>Where the body is (its hips: a ragdoll's root stays where it was thrown from).</summary>
        public Vector3 BodyPos => hips ? hips.position : transform.position;
        readonly List<Collider> cols = new List<Collider>();
        sealed class Pair { public Collider a, b; }
        readonly List<Pair> held = new List<Pair>();
        float age, wreckAt, sinkT = -1f, sinkDepth = 1.6f, calm, nextCheck = 0.2f, startY;
        bool air, checkedRest; int emitter;
        // the scripted fall
        bool scripted; Vector3 pos, vel, axis, from; float spin; Quaternion rot0;

        static int HeavyLayer { get { int l = LayerMask.NameToLayer("DebrisHeavy"); return l >= 0 ? l : OzShuttleBreakup.HeavyLayerFallback; } }
        static float GroundY(Vector3 p) { var g = ZGame.I; return g ? g.GroundY(p.x, p.z) : 0f; }

        public static void BeginBreak() { Last.Clear(); LastThrowAt = Time.time; }

        /// <summary>BoardingView.ShuttleCrewLost: throws one prepared body (unparented, at its scale, its glows fading, its weapon shown).
        /// push = the flat throw direction, strength / lift as RobotRagdoll.Launch (1 = the domes' and engines' throw), shipVel = the drawn
        /// ship's velocity (m/s), air = it broke in the air; landPose = the corpse pose of the scripted fall's landing.</summary>
        public static OzShuttleCrewFall Throw(GameObject go, Vector3 push, float strength, float lift, Vector3 shipVel, bool air, uint seed, int emitter, System.Action<GameObject> landPose)
        {
            if (!go) return null;
            var me = go.AddComponent<OzShuttleCrewFall>();
            me.air = air; me.emitter = emitter; me.landPose = landPose; me.startY = go.transform.position.y; me.from = go.transform.position;
            me.wreckAt = OzShuttleBreakup.LastWreckAt > 0f ? OzShuttleBreakup.LastWreckAt : OzShuttleBreakup.WreckSeconds;
            me.anim = go.GetComponentInChildren<Animator>();
            push.y = 0f; if (push.sqrMagnitude < 1e-4f) push = Vector3.forward; push.Normalize();
            Thrown++; Last.Add(me);
            if (RobotRagdoll.CanStart()) me.rd = RobotRagdoll.Launch(go, push, strength, lift, seed);
            if (me.rd)
            {
                Ragdolls++;
                var rbs = go.GetComponentsInChildren<Rigidbody>(); if (rbs.Length > 0) me.hips = rbs[0].transform;   // RobotRagdoll makes the hips first
                if (!me.FlyUpKick(push, strength, shipVel, air, seed))   // K425: blown up first, each body its own kick, the ship's velocity in it (OzShuttleCrewFall.FlyUp.cs)
                    foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) if (rb && !rb.isKinematic) rb.linearVelocity += shipVel;
                RagdollFlail.Begin(go, air ? 1f : 0.55f, seed);                                               // the limbs flail in the air (as a blast death)
                me.OnHeavyLayer();
                if (air && RobotRagdoll.LastSpeed < OzBodySounds.WhooshFromMps)
                {   // thrown softly by the break but falling from up high: the whoosh of a thrown body all the same
                    float h = Mathf.Max(0.5f, go.transform.position.y - GroundY(go.transform.position)), T = Mathf.Sqrt(2f * h / 9.81f) + 0.3f;
                    var land = go.transform.position + (shipVel + push * 2.8f * strength) * T; land.y = GroundY(land);
                    OzBodySounds.FlightThrown(go.transform.position, land, T, 0.5f, emitter);
                }
                return me;
            }
            // the ragdoll budget is used up: the scripted blast fall
            Scripted++; me.scripted = true;
            uint u = seed | 1u; float r0 = (u & 1023u) / 1023f, r1 = ((u >> 10) & 1023u) / 1023f;
            me.vel = shipVel + push * Mathf.Lerp(2.4f, 3.3f, r0) * strength + Vector3.up * Mathf.Lerp(1.7f, 2.5f, r1) * strength * lift;
            me.pos = go.transform.position; me.axis = Vector3.Cross(Vector3.up, push); me.spin = Mathf.Lerp(2f, 4f, r1) * Mathf.Rad2Deg;
            me.rot0 = Quaternion.Euler(0f, go.transform.eulerAngles.y, 0f);
            me.FlyUpScripted(push, shipVel, seed);   // K425: the scripted fall gets the same kick (OzShuttleCrewFall.FlyUp.cs)
            float hh = Mathf.Max(0.5f, me.pos.y - GroundY(me.pos)), vy = me.vel.y, TT = (vy + Mathf.Sqrt(vy * vy + 2f * 9.81f * hh)) / 9.81f;
            var to = me.pos + new Vector3(me.vel.x, 0f, me.vel.z) * TT; to.y = GroundY(to);
            OzBodySounds.FlightThrown(me.pos, to, TT, Mathf.Max(0f, vy * vy / 19.62f), emitter);
            if (me.anim) { me.anim.enabled = true; me.anim.speed = 1f; }
            return me;
        }

        /// <summary>The ragdoll's colliders onto the heavy debris layer (they hit the pieces), never against each other; the pieces it
        /// starts inside are ignored until they are apart.</summary>
        void OnHeavyLayer()
        {
            int layer = HeavyLayer;
            GetComponentsInChildren(cols);
            cols.RemoveAll(c => !c || c.isTrigger || !c.attachedRigidbody);                                   // the ragdoll's own bodies only (never a figure's root collider)
            foreach (var c in cols) c.gameObject.layer = layer;
            for (int i = 0; i < cols.Count; i++) for (int j = i + 1; j < cols.Count; j++) Physics.IgnoreCollision(cols[i], cols[j], true);
            foreach (var p in OzShuttleBreakup.LastPieces)
            {
                var pc = p ? p.PieceCollider : null; if (!pc) continue;
                foreach (var c in cols)
                    if (Physics.ComputePenetration(c, c.transform.position, c.transform.rotation, pc, pc.transform.position, pc.transform.rotation, out _, out float d) && d > 0.01f)
                    { Physics.IgnoreCollision(c, pc, true); held.Add(new Pair { a = c, b = pc }); HeldPairs++; }
            }
        }

        void Update()
        {
            var g = ZGame.I; float dt = g && g.paused ? 0f : Time.deltaTime;
            if (dt <= 0f) return;
            age += dt;
            if (sinkT >= 0f) { sinkT += dt; transform.position += Vector3.down * (sinkDepth / 3f) * dt; if (sinkT >= 3f) Destroy(gameObject); return; }
            if (age >= wreckAt) { Retire(); return; }
            if (scripted) { ScriptedStep(dt); return; }
            if (!rd) return;
            if (held.Count > 0 && age >= nextCheck)
            {
                nextCheck = age + 0.2f;
                for (int i = held.Count - 1; i >= 0; i--)
                {
                    var q = held[i];
                    if (!q.a || !q.b || !q.b.enabled) { held.RemoveAt(i); continue; }
                    if (!(Physics.ComputePenetration(q.a, q.a.transform.position, q.a.transform.rotation, q.b, q.b.transform.position, q.b.transform.rotation, out _, out float d) && d > 0.03f))
                    { Physics.IgnoreCollision(q.a, q.b, false); held.RemoveAt(i); ReleasedPairs++; }
                }
            }
            if (!Landed)
            {
                var land = GetComponent<OzBodyLanding>();
                if ((land && land.landed) || (age > 0.3f && LowGap() < 0.25f)) { Landed = true; LandedAt = age; LandedCount++; FellM = startY - BodyPos.y; LandedNow(land); }   // K256k: its numbers, its thud if no contact asked one
            }
            DropWatch();                                                                                        // K256k: the top of its flight; a drop after a high landing heard
            if (!checkedRest && !rd.Frozen)
            {   // just before RobotRagdoll freezes it (calm, or after 6 s): out of any piece it lies inside
                float mv = 0f; foreach (var c in cols) { var rb = c ? c.attachedRigidbody : null; if (rb) mv = Mathf.Max(mv, rb.linearVelocity.magnitude); }
                calm = mv < 0.2f ? calm + dt : 0f;
                if ((calm > 0.45f && age > 1f) || age > 5.6f) RestCheck();
            }
            else if (!checkedRest) RestCheck();
        }

        float LowGap()
        {
            float best = 99f;
            foreach (var c in cols) { if (!c) continue; var b = c.bounds; best = Mathf.Min(best, b.min.y - GroundY(b.center)); }
            return best;
        }

        void RestCheck()
        {
            checkedRest = true;
            if (!Landed) { Landed = true; LandedAt = age; LandedCount++; FellM = startY - BodyPos.y; LandedNow(GetComponent<OzBodyLanding>()); }
            if (OzWaterLanding.UnderSurface(BodyPos + Vector3.up * 0.2f)) { RestOn = "water"; InWater++; return; }
            // pushed out of a piece it lies inside (never left in the wreck's metal)
            Vector3 shift = Vector3.zero;
            foreach (var p in OzShuttleBreakup.LastPieces)
            {
                var pc = p ? p.PieceCollider : null; if (!pc || !pc.enabled) continue;
                foreach (var c in cols)
                    if (c && c.enabled && Physics.ComputePenetration(c, c.transform.position + shift, c.transform.rotation, pc, pc.transform.position, pc.transform.rotation, out var dir, out float d) && d > 0.03f)
                        shift += dir * (d + 0.01f);
            }
            if (shift.sqrMagnitude > 1e-6f) { transform.position += shift; Physics.SyncTransforms(); PushedOut++; }
            SettleInHollow();                                                                                   // K256k: never resting on a hull panel's collider over its hollow
            bool live = cols.Count > 0 && cols[0] && cols[0].enabled;                                         // frozen already: its colliders are off
            RestGap = live ? LowGap() : Mathf.Max(0f, BodyPos.y - GroundY(BodyPos) - 0.3f);
            RestOn = "ground";
            var hs = Physics.RaycastAll(BodyPos + Vector3.up * 1.5f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hs) if (h.collider && h.collider.GetComponentInParent<OzShuttleBreakup>()) { RestOn = "on a piece"; OnPiece++; break; }
        }

        void ScriptedStep(float dt)
        {
            if (Landed) return;
            vel += Vector3.down * 9.81f * dt; pos += vel * dt;
            float gy = GroundY(pos);
            bool wet = OzWaterLanding.OpenWaterAt(pos, out float wy);
            float floor = wet ? wy - 0.2f : OzRockBody.FloorAt(gameObject, ref pos, ref vel, dt, gy);            // items 331 + 332: it lands on a rock top or stops at its side, never in the rock
            transform.position = pos;
            transform.rotation = Quaternion.AngleAxis(spin * age, axis) * rot0;
            if (pos.y > floor + 0.05f) return;
            Landed = true; LandedAt = age; LandedCount++; FellM = startY - floor;
            pos.y = floor; transform.position = pos;
            transform.rotation = Quaternion.AngleAxis(90f, axis) * rot0;                                       // on its back
            landPose?.Invoke(gameObject);
            var to = pos;
            if (wet) { OzWaterLanding.Splash(pos, OzWaterSize.Small, "robot body"); RestOn = "water"; InWater++; }
            else
            {
                OzBodySounds.FlightLanded(pos, from, to, Mathf.Max(0.2f, age), 0.5f, emitter);
                var fx = OpenZFxPlayer.I; if (fx && fx.isActiveAndEnabled) fx.Dust(pos, 0.5f);
                RestOn = "ground";
            }
            RestGap = 0f; checkedRest = true;
        }

        void Retire()
        {
            if (sinkT >= 0f) return;
            if (rd && !rd.Frozen) rd.Freeze();
            foreach (var c in cols) if (c) c.enabled = false;
            sinkT = 0f;
            if (OzRockBody.PopCorpseIfOnRock(gameObject, BodyPos)) sinkDepth = 0f;                                // items 331 + 332: on a rock it pops away, never sinks through it
        }

        void OnDestroy() { Last.Remove(this); }

        // ------------------------------------------------------------------ checks
        public static int LandedNow() { int n = 0; foreach (var b in Last) if (b && b.Landed) n++; return n; }
        /// <summary>Bodies lying more than `maxGap` over the ground and not on a piece (floating), and their worst gap.</summary>
        public static int Floating(float maxGap, out float worst)
        {
            int n = 0; worst = 0f;
            foreach (var b in Last) { if (!b || !b.checkedRest || b.RestOn != "ground") continue; if (b.RestGap > maxGap) { n++; worst = Mathf.Max(worst, b.RestGap); } }
            return n;
        }
        /// <summary>The farthest body from the pieces' middle on the ground (m): the bodies lie among the pieces.</summary>
        public static float FarthestFromPieces()
        {
            Vector3 c = Vector3.zero; int n = 0;
            foreach (var p in OzShuttleBreakup.LastPieces) if (p) { c += p.transform.position; n++; }
            if (n == 0) return 0f;
            c /= n; float far = 0f;
            foreach (var b in Last) if (b) { var d = b.BodyPos - c; d.y = 0f; far = Mathf.Max(far, d.magnitude); }
            return far;
        }
    }
}
