using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The shot-down shuttle's crew bodies are blown UP first, then tumble down and land with their thuds (rule DV118, view only). Every
    /// body thrown by <see cref="Throw"/> (the walkers on the ramp, the ones waiting on the benches, the seated, the doorway's lost exiters,
    /// the rest of the lost on the bay seats: BoardingView.ShuttleCrewLost) gets its own kick from <see cref="OzCrewFlyPlan"/> (up 6-11 m/s,
    /// out 3-7 m/s along its throw's flat way, a tumble of 2.5-6 rad/s, seeded by its throw), in the air and on the ground:
    /// <list type="bullet">
    /// <item>a ragdoll: every bone gets the kick as one tumbling body (the kick + the spin about the body's middle), on top of the ship's own
    /// velocity; the chest's own hit (RobotRagdoll.Launch, <see cref="ChestHitN"/>) is counted in the out kick. For its first
    /// <see cref="FlyPassSec"/> s it passes through the breaking hull's pieces round it (blown out through the hull, not stopped under a roof
    /// panel), then it meets them again (a piece it is still inside is held apart until they part, as at the start). The rocks / statics rule
    /// (items 331 + 332, K331b / K331d) is asked again with the new throw's reach when its first ask found no rock near;</item>
    /// <item>the scripted fall (the ragdoll budget used up): the same kick and tumble rate on its arc;</item>
    /// <item>heard: a body that got no whoosh at its launch (on the ground the old shove was too soft for one) gets the thrown body's
    /// whoosh now; the landing thuds stay K256k's (OzShuttleCrewFall.Settle.cs), the flail RagdollFlail's.</item>
    /// </list>
    /// After every break, once its bodies have landed (at most 9 s), one console line sums the kicks and what the bodies did (measured).
    /// </summary>
    public sealed partial class OzShuttleCrewFall
    {
        /// <summary>RobotRagdoll.Launch's chest hit (N s per unit of strength; the whole body gets this / its mass along the push).</summary>
        public const float ChestHitN = 55f;
        /// <summary>A body passes through the hull's pieces this long after its kick (s); only pieces within <see cref="FlyPassNearM"/> m of it.</summary>
        public static float FlyPassSec = 0.2f, FlyPassNearM = 3f;

        /// <summary>Proof: bodies kicked (all, scripted), kicks with the up / out lowered, whooshes asked for kicked bodies, rock rule asked
        /// again and taken, piece pairs passed through at the start (and still inside at the end of the pass: held apart).</summary>
        public static int FlyKicks, FlyScripted, FlyUpCapped, FlyOutCapped, FlyWhooshes, FlyRockArmed, FlyPassPairs, FlyPassHeld;
        /// <summary>One kicked body of the last break: its kick, its planned flight, its height at the throw.</summary>
        public struct FlyRow { public float up, outward, spin, flightSec, apexM, reachM, heightM; public bool ragdoll, upCapped, outCapped; }
        public static readonly List<FlyRow> LastFly = new List<FlyRow>(12);
        /// <summary>The last break's summary (measured once its bodies landed): bodies, kicked, landed, their landings heard; how high they rose
        /// over their start (m: min / median / max) and the farthest at rest from its start (m, flat).</summary>
        public static int LastFlyBodies, LastFlyKicked, LastFlyLanded, LastFlyHeard, LastFlyWhooshAsked;
        public static float LastFlyRoseMin, LastFlyRoseMedian, LastFlyRoseMax, LastFlyReachMax;
        public static string LastFlyText = "-";

        static float flyBreak = -99f; static int flyWhoosh0;
        OzCrewFlyPlan.Plan fly; bool flew, flyPassPending; float flyPassAt = -1f, flyHipsY;
        readonly List<Pair> flyPass = new List<Pair>();

        /// <summary>It was blown up by the kick.</summary>
        public bool FlewUp => flew;
        /// <summary>How high it rose over where it was thrown from (m): measured (the top of its hips' flight over its hips at the kick) for a
        /// ragdoll; the planned top of the arc for the scripted fall.</summary>
        public float RoseM => scripted ? (flew ? fly.apexM : 0f) : (flew && topY > float.MinValue ? Mathf.Max(0f, topY - flyHipsY) : 0f);
        /// <summary>Its kick (zero when not kicked).</summary>
        public OzCrewFlyPlan.Plan Fly => fly;

        /// <summary>Throw, a ragdoll just launched (its hips known): the kick on every bone, the ship's velocity included (false = not
        /// kicked: the caller adds the ship's velocity as before).</summary>
        bool FlyUpKick(Vector3 push, float strength, Vector3 shipVel, bool air, uint seed)
        {
            if (!OzCrewFlyPlan.On || !rd) return false;
            var rbs = GetComponentsInChildren<Rigidbody>();
            float mass = 0f; var com = Vector3.zero;
            foreach (var rb in rbs) if (rb && !rb.isKinematic) { mass += rb.mass; com += rb.worldCenterOfMass * rb.mass; }
            if (mass <= 0f) return false;
            com /= mass;
            bool launchWhoosh = RobotRagdoll.LastSpeed >= OzBodySounds.WhooshFromMps;                         // RobotRagdoll.Launch asked its whoosh already
            var at = BodyPos; float h = at.y - GroundY(at); flyHipsY = at.y;
            var flat = new Vector3(shipVel.x, 0f, shipVel.z); float along = Vector3.Dot(flat, push);
            fly = OzCrewFlyPlan.Make(seed, h, along, (flat - push * along).magnitude, shipVel.y, ChestHitN * Mathf.Max(0f, strength) / mass);
            var v = shipVel + push * fly.outSet + Vector3.up * fly.up; var w = fly.axis * fly.spin;
            foreach (var rb in rbs) if (rb && !rb.isKinematic) { rb.linearVelocity = v + Vector3.Cross(w, rb.worldCenterOfMass - com); rb.angularVelocity = w; }
            flew = true; flyPassPending = true;
            FlyNote(h, true, launchWhoosh);
            if (!GetComponent<OzRockBody>() && OzRockBody.Ragdoll(gameObject, rd)) FlyRockArmed++;           // items 331 + 332: the rocks and statics within the new throw's reach
            if (!air && !launchWhoosh)
            {   // the old shove got no whoosh on the ground: the thrown body's whoosh now (in the air the caller's fallback asks it)
                var land = at + (flat + push * fly.outward) * fly.flightSec; land.y = GroundY(land);
                OzBodySounds.FlightThrown(at, land, fly.flightSec, fly.apexM, emitter); FlyWhooshes++;
            }
            return true;
        }

        /// <summary>Throw, the scripted fall (after its arc's numbers are set): the same kick and tumble rate (its turn axis stays across its way).</summary>
        void FlyUpScripted(Vector3 push, Vector3 shipVel, uint seed)
        {
            if (!OzCrewFlyPlan.On) return;
            float h = pos.y - GroundY(pos);
            var flat = new Vector3(shipVel.x, 0f, shipVel.z); float along = Vector3.Dot(flat, push);
            fly = OzCrewFlyPlan.Make(seed, h, along, (flat - push * along).magnitude, shipVel.y, 0f);
            vel = shipVel + push * fly.outSet + Vector3.up * fly.up;
            spin = fly.spin * Mathf.Rad2Deg;
            flew = true; FlyScripted++;
            FlyNote(h, false, false);
            FlyWhooshes++;                                                                                       // the caller's FlightThrown asks it with these numbers
        }

        void FlyNote(float h, bool ragdoll, bool launchWhoosh)
        {
            if (flyBreak != LastThrowAt)
            {   // the first body of a break: the rows, the whoosh count from here, the watch that sums the break once the bodies landed
                flyBreak = LastThrowAt; LastFly.Clear(); LastFlyText = "-";
                flyWhoosh0 = OzBodySounds.WhooshAsked - (launchWhoosh ? 1 : 0);
                var go = new GameObject("OzShuttleCrewFlyWatch") { hideFlags = HideFlags.HideInHierarchy };
                go.AddComponent<FlyWatch>().at = LastThrowAt;
            }
            FlyKicks++; if (fly.upCapped) FlyUpCapped++; if (fly.outCapped) FlyOutCapped++;
            if (LastFly.Count < 32)
                LastFly.Add(new FlyRow { up = fly.up, outward = fly.outward, spin = fly.spin, flightSec = fly.flightSec, apexM = fly.apexM, reachM = fly.reachM, heightM = h, ragdoll = ragdoll, upCapped = fly.upCapped, outCapped = fly.outCapped });
        }

        /// <summary>Before the first physics step after the throw (the break has listed its pieces by then): the body passes through the
        /// pieces round it for FlyPassSec; then each pair meets again, or, still inside each other, is held apart as at the start.</summary>
        void FixedUpdate()
        {
            if (flyPassPending)
            {
                flyPassPending = false; flyPassAt = 0f;
                if (cols.Count == 0) return;
                var bb = new Bounds(BodyPos, Vector3.zero); foreach (var c in cols) if (c) bb.Encapsulate(c.bounds);
                bb.Expand(2f * FlyPassNearM);
                foreach (var p in OzShuttleBreakup.LastPieces)
                {
                    var pc = p ? p.PieceCollider : null; if (!pc || !pc.enabled || !bb.Intersects(pc.bounds)) continue;
                    foreach (var c in cols) { if (!c) continue; Physics.IgnoreCollision(c, pc, true); flyPass.Add(new Pair { a = c, b = pc }); FlyPassPairs++; }
                }
                return;
            }
            if (flyPassAt < 0f || flyPass.Count == 0) return;
            flyPassAt += Time.fixedDeltaTime;
            if (flyPassAt < FlyPassSec) return;
            foreach (var q in flyPass)
            {
                if (!q.a || !q.b) continue;
                bool inside = q.a.enabled && q.b.enabled && q.a.bounds.Intersects(q.b.bounds)
                              && Physics.ComputePenetration(q.a, q.a.transform.position, q.a.transform.rotation, q.b, q.b.transform.position, q.b.transform.rotation, out _, out float d) && d > 0.03f;
                if (inside) { held.Add(q); FlyPassHeld++; }                                                   // Update lets them meet once apart
                else Physics.IgnoreCollision(q.a, q.b, false);
            }
            flyPass.Clear();
        }

        // ------------------------------------------------------------------ the break's summary
        sealed class FlyWatch : MonoBehaviour
        {
            internal float at; float t, allAt = -1f;

            void Update()
            {
                t += Time.deltaTime;
                if (flyBreak != at) { Destroy(gameObject); return; }                                            // a newer break: its own watch sums it
                bool all = Last.Count > 0; foreach (var b in Last) if (b && !b.Landed) { all = false; break; }
                if (all && allAt < 0f) allAt = t;
                if ((allAt < 0f || t - allAt < 1f) && t < 9f) return;                                           // landed, then a moment to settle
                Sum();
                Destroy(gameObject);
            }

            static void Sum()
            {
                var rose = new List<float>(); int n = 0, kicked = 0, landed = 0, heard = 0, rag = 0; float far = 0f;
                foreach (var b in Last)
                {
                    if (!b) continue; n++; if (b.Ragdoll) rag++;
                    if (b.flew) { kicked++; rose.Add(b.RoseM); }
                    if (b.Landed) landed++;
                    var l = b.GetComponent<OzBodyLanding>();
                    if (b.Landed && ((l && l.landed && l.thud != "") || (!b.Ragdoll && b.scripted))) heard++;   // the scripted fall asks its thud on its landing frame
                    var d = b.BodyPos - b.from; d.y = 0f; far = Mathf.Max(far, d.magnitude);
                }
                rose.Sort();
                LastFlyBodies = n; LastFlyKicked = kicked; LastFlyLanded = landed; LastFlyHeard = heard; LastFlyReachMax = far;
                LastFlyRoseMin = rose.Count > 0 ? rose[0] : 0f; LastFlyRoseMax = rose.Count > 0 ? rose[rose.Count - 1] : 0f;
                LastFlyRoseMedian = rose.Count > 0 ? (rose.Count % 2 == 1 ? rose[rose.Count / 2] : 0.5f * (rose[rose.Count / 2 - 1] + rose[rose.Count / 2])) : 0f;
                LastFlyWhooshAsked = OzBodySounds.WhooshAsked - flyWhoosh0;
                float u0 = 99f, u1 = 0f, o0 = 99f, o1 = 0f, s0 = 99f, s1 = 0f, f0 = 99f, f1 = 0f; int uc = 0, oc = 0;
                foreach (var r in LastFly)
                {
                    u0 = Mathf.Min(u0, r.up); u1 = Mathf.Max(u1, r.up); o0 = Mathf.Min(o0, r.outward); o1 = Mathf.Max(o1, r.outward);
                    s0 = Mathf.Min(s0, r.spin); s1 = Mathf.Max(s1, r.spin); f0 = Mathf.Min(f0, r.flightSec); f1 = Mathf.Max(f1, r.flightSec);
                    if (r.upCapped) uc++; if (r.outCapped) oc++;
                }
                LastFlyText = LastFly.Count == 0 ? "no body kicked" :
                    $"{n} bodies ({rag} ragdolls, {n - rag} scripted), {kicked} kicked: up {u0:F1}-{u1:F1} m/s (lowered {uc}), out {o0:F1}-{o1:F1} m/s (lowered {oc}), spin {s0:F1}-{s1:F1} rad/s, " +
                    $"flight {f0:F1}-{f1:F1} s planned; rose over their start {LastFlyRoseMin:F1} / {LastFlyRoseMedian:F1} / {LastFlyRoseMax:F1} m (min / median / max); landed {landed} of {n}, " +
                    $"landings heard {heard} of {landed}, whooshes asked +{LastFlyWhooshAsked}; the farthest at rest {far:F1} m from where it was thrown";
                Debug.Log("[OpenZ] shuttle crew blown up first: " + LastFlyText);
            }
        }
    }
}
