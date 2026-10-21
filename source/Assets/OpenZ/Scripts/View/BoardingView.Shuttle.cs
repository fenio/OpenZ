using System;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The playable shuttle's squad on its rear ramp (rule DV118; the ramp is a rule state, ShuttleRearDoor draws it). The rules walk
    /// the squad to its back slots and board it at the ramp foot in one tick, with the moment each figure reaches the foot
    /// (ZObject.ShuttleFootTimes, sim seconds, squad order); the view walks each figure (a ghost, as for the APC) from where it stood to
    /// the foot so that it arrives exactly then, up the ramp in single file (0.55 s apart), through the doorway, along the aisle and
    /// onto its bench (filled from the front), shrinking on the upper ramp to the bay's fit (ShuttleRearDoor.FitHeight). The seated
    /// figures stay on their benches until the ramp has shut (the rules keep it down until the last one sits). EXIT: the passengers sit on
    /// the benches nearest the doorway while the ramp drops, then stand up one after the other (each leaves its seat
    /// ShuttleRampOutSec before the moment the rules give for it to step off the foot), walk down the ramp and on to their own robot
    /// standing on its back slot. Every figure that steps onto or off the ramp's foot gives a small metal step (the crew clank bank,
    /// quiet). Destroyed with the squad aboard: their bodies tumble out with the pieces (<see cref="ShuttleCrewLost"/>). View only.
    /// </summary>
    public partial class BoardingView
    {
        /// <summary>Proof: ramp step clanks, figures flung at a break-up, seated waiting figures put on the benches for an exit.</summary>
        public static int ShuttleSteps, ShuttleBodies, ShuttleWaiting, ShuttleFootLate;
        /// <summary>Per robot type: full height (m) and the fit scale in the shuttle's bay.</summary>
        public static readonly Dictionary<string, Vector2> ShuttleFitLog = new Dictionary<string, Vector2>();
        static readonly Dictionary<string, float> shuttleFit = new Dictionary<string, float>();
        readonly Dictionary<Ghost, float> ghostLast = new Dictionary<Ghost, float>();
        readonly HashSet<int> shuttleWalking = new HashSet<int>(); int walkingFrame = -1;
        readonly Dictionary<int, GameObject> waitingSeats = new Dictionary<int, GameObject>();

        float ShuttleFit(Ghost g)
        {
            var o = game.World.GetAny(g.robotId); string type = o != null ? o.Type : "grunt";
            if (shuttleFit.TryGetValue(type, out var f)) return f;
            Bounds b = default; bool any = false;
            foreach (var rr in g.go.GetComponentsInChildren<Renderer>()) { if (!rr.enabled || rr is ParticleSystemRenderer) continue; if (!any) { b = rr.bounds; any = true; } else b.Encapsulate(rr.bounds); }
            float h = any ? b.size.y / Mathf.Max(0.01f, g.go.transform.localScale.x) : 2.4f;
            f = Mathf.Clamp(ShuttleRearDoor.FitHeight / Mathf.Max(0.3f, h), 0.3f, 1f);
            shuttleFit[type] = f; ShuttleFitLog[type] = new Vector2(h, f);
            return f;
        }

        static float Sum(List<Key> keys, int from, int to) { float t = 0f; for (int i = Mathf.Max(1, from); i <= to && i < keys.Count; i++) t += keys[i].dur; return t; }

        /// <summary>BuildEnter, the shuttle: the walk to the ramp foot, up the ramp, the doorway, the aisle, the bench.</summary>
        void BuildShuttleEnter(Ghost g, Func<Vector3> S, ShuttleRearDoor d)
        {
            var r = g.rig; var keys = g.keys;
            int idx = r.obj.ShuttleFootIds.IndexOf(g.robotId);
            int k = g.seatK = ShuttleRearDoor.FrontSeat(idx >= 0 ? idx : r.apcSeat++);
            float fit = ShuttleFit(g); g.scaleA = 1f; g.scaleB = fit;
            Func<Vector3> foot = () => Ground(d.FootPoint()), tip = () => d.RampPoint(1f, RobotY), sill = () => d.RampPoint(0f, RobotY),
                          inDoor = () => d.AislePoint(-1, RobotY), aisle = () => d.AislePoint(k, RobotY);
            Func<Vector3> seat = () => d.SeatPoint(k) - HipsOffset(g.go, g.hips, r.root.up, pelvisHeight * g.baseScale * g.scaleB);
            int walk0 = keys.Count;
            WalkTo(g, S, foot);
            int footK = keys.Count - 1;
            ShuttleApproachPace(keys, walk0, footK);                                                         // K422: to the foot at the rules' ZWorld.ShuttleWalkMps (BoardingView.ShuttleQuick.cs)
            float walk = ShuttleAisleWalk, rampWalk = Mathf.Max(walk, ShuttleRampWalk);                       // K422: the aisle 1.5 -> 2.0 m/s              // K256j (M6): the file keeps its spacing up the ramp (BoardingView.ShuttleFile.cs)
            keys.Add(K(tip, Mathf.Max(0.12f, (tip() - foot()).magnitude / rampWalk), "walk", Face.Move));
            keys.Add(K(sill, d.Len / rampWalk, "walk", Face.Move));
            keys.Add(K(inDoor, 0.3f, "walk", Face.Move));
            keys.Add(K(aisle, Mathf.Max(0.15f, Dist(inDoor, aisle) / walk), "walk", Face.Move));
            keys.Add(K(seat, ShuttleSitInSec, "seated_gunner", Face.Aisle, ease: true));                     // K422: 0.45 -> 0.35 s
            // the part from the foot to the seat fits the rules' ShuttleRampInSec (never slower than natural)
            float ramp = Sum(keys, footK + 1, keys.Count - 1), budget = (float)ZWorld.ShuttleRampInSec - 0.05f;
            if (ramp > budget) { float s = budget / ramp; for (int i = footK + 1; i < keys.Count; i++) keys[i].dur *= s; }
            ShuttleDoorwayEnterTiming(keys, footK, budget); ShuttleQuickNote(keys, footK, true);   // K418: through the doorway exactly ZWorld.ShuttleBoardDoorSec after the foot (DV126: the rules' moment)
            g.footT = Sum(keys, 1, footK);
            g.total = Sum(keys, 1, keys.Count - 1);
            float ramp0 = g.footT + keys[footK + 1].dur, rampDur = keys[footK + 2].dur;
            g.scaleT0 = ramp0 + 0.3f * rampDur; g.scaleSpan = Mathf.Max(0.1f, 0.7f * rampDur + 0.5f * keys[footK + 3].dur);
            FitEnter(g);
        }

        /// <summary>StartEnter, the shuttle: the ghost reaches the ramp foot exactly at the rules' moment for this robot (its walk there
        /// plays faster when it cannot make it at its natural pace).</summary>
        void ShuttleFootDelay(Ghost g, Rig r)
        {
            var o = r.obj; int idx = o.ShuttleFootIds.IndexOf(g.robotId);
            double now = game.World.Time;
            double due = idx >= 0 && idx < o.ShuttleFootTimes.Count ? o.ShuttleFootTimes[idx] : now + g.footT + g.delay;
            float left = (float)(due - now);
            if (left >= g.footT) { g.delay = left - g.footT; return; }
            // late: the walk to the foot is squeezed into what is left (at least 0.1 s)
            ShuttleFootLate++;
            int footK = 1; float acc = 0f; for (int i = 1; i < g.keys.Count; i++) { acc += g.keys[i].dur; if (acc >= g.footT - 1e-4f) { footK = i; break; } }
            float s = Mathf.Max(0.1f, left) / Mathf.Max(0.05f, g.footT);
            for (int i = 1; i <= footK && i < g.keys.Count; i++) g.keys[i].dur *= s;
            float d0 = g.footT; g.footT *= s; g.total -= d0 - g.footT; g.scaleT0 -= d0 - g.footT; g.delay = 0f;
        }

        /// <summary>BuildExit, the shuttle: up from the bench, the aisle, the doorway, down the ramp (ShuttleRampOutSec from the seat to the
        /// foot, the rules' time), then on to its own robot.</summary>
        void BuildShuttleExit(Ghost g, Func<Vector3> to, ShuttleRearDoor d, int fitK0)
        {
            var r = g.rig; var keys = g.keys; int k = Mathf.Max(0, g.seatK);
            float fit = ShuttleFit(g); g.scaleA = fit; g.scaleB = 1f;
            Func<Vector3> seat = () => d.SeatPoint(k) - HipsOffset(g.go, g.hips, r.root.up, pelvisHeight * g.baseScale * g.scaleA),
                          aisle = () => d.AislePoint(k, RobotY), inDoor = () => d.AislePoint(-1, RobotY), sill = () => d.RampPoint(0f, RobotY),
                          tip = () => d.RampPoint(1f, RobotY), foot = () => Ground(d.FootPoint());
            float walk = ShuttleAisleWalk;                                                                     // K422: the aisle 1.5 -> 2.0 m/s, down the ramp at its 2.35 m/s walk (was the aisle's 1.5)
            keys.Clear();
            keys.Add(K(seat, 0, "seated_gunner", Face.Aisle));
            keys.Add(K(seat, ShuttleSitOutSec, "seated_gunner", Face.Aisle));
            keys.Add(K(aisle, ShuttleStandSec, "idle", Face.Aisle, ease: true));
            keys.Add(K(inDoor, Mathf.Max(0.15f, Dist(aisle, inDoor) / walk), "walk", Face.Move));
            keys.Add(K(sill, ShuttleSillSec, "walk", Face.Move));
            int rampK = keys.Count;
            keys.Add(K(tip, d.Len / Mathf.Max(walk, ShuttleRampWalk), "walk", Face.Move));
            keys.Add(K(foot, ShuttleSillSec, "walk", Face.Move));
            int footK = keys.Count - 1;
            float pre = Sum(keys, 1, footK), want = (float)ZWorld.ShuttleRampOutSec;
            if (pre > 0.05f) { float s = want / pre; for (int i = 1; i <= footK; i++) keys[i].dur *= s; }
            ShuttleDoorwayExitTiming(keys, footK); ShuttleQuickNote(keys, footK, false);   // K418: at the doorway exactly ZWorld.ShuttleExitDoorSec after the seat (DV126: the rules' moment)
            WalkTo(g, foot, to, minLast: 0.15f);
            g.footT = Sum(keys, 1, footK);
            g.total = Sum(keys, 1, keys.Count - 1);
            float t = Sum(keys, 1, rampK - 1);
            g.scaleT0 = t; g.scaleSpan = Mathf.Max(0.1f, 0.7f * keys[rampK].dur);
            FitExit(g, false, fitK0);
        }

        /// <summary>StartExit, the shuttle: the passenger sits on a bench by the doorway and leaves it ShuttleRampOutSec before the rules'
        /// moment for it to step off the ramp foot.</summary>
        void ShuttleExitDelay(Ghost g, Rig r, ZObject robot, int index)
        {
            var o = r.obj; int idx = o.ShuttleFootIds.IndexOf(robot.Id); if (idx < 0) idx = index;
            g.seatK = ShuttleRearDoor.DoorSeat(idx);
            double now = game.World.Time;
            double due = idx < o.ShuttleFootTimes.Count ? o.ShuttleFootTimes[idx] : now + ZWorld.ShuttleRampOutSec + idx * ZWorld.ShuttleFileGapSec;
            g.delay = Mathf.Max(0f, (float)(due - ZWorld.ShuttleRampOutSec - now));
            if (waitingSeats.TryGetValue(g.seatK, out var wait)) { if (wait) { waitingHips.Remove(wait); Destroy(wait); } waitingSeats.Remove(g.seatK); }   // the waiting figure on that seat becomes this ghost
        }

        /// <summary>StepGhost hook (before the ghost's time moves on): the ramp foot's step clank as a figure steps onto it (boarding) or
        /// off it (exit), and the shuttle's walking figures this frame (for <see cref="ShuttleCrewLost"/>).</summary>
        void ShuttleGhostStep(Ghost g, Rig r)
        {
            if (r == null || r.obj == null || !r.obj.IsShuttle || g.seatK < 0) return;
            if (walkingFrame != Time.frameCount) { walkingFrame = Time.frameCount; shuttleWalking.Clear(); }
            if (g.enter && !g.done) shuttleWalking.Add(g.robotId);
            ghostLast.TryGetValue(g, out float last);
            if (g.footT > 0f && last < g.footT && g.time >= g.footT && r.view && sounds && CombatAudio.I)
            {
                var at = g.go ? g.go.transform.position : r.root.position;
                CombatAudio.I.PlayPitched("crew_step", at, 0.35f, 0.12f, r.obj.Id, 1.3f, OzSoundV3.BankCrewIn); ShuttleSteps++;   // K256e (review M6e): 0.15 -> 0.35, heard at 7 m (>= 0.12 in the sound log)
            }
            ghostLast[g] = g.time;
            if (ghostLast.Count > 64) { var dead = new List<Ghost>(); foreach (var kv in ghostLast) if (kv.Key.done) dead.Add(kv.Key); foreach (var x in dead) ghostLast.Remove(x); }
        }

        /// <summary>ShuttleRearDoor, the EXIT order: the passengers sit on the benches nearest the doorway while the ramp comes down
        /// (they become the exit ghosts when the rules let them out).</summary>
        public void ShuttleSeatWaiting(UnitView v, ShuttleRearDoor d)
        {
            var o = v ? v.Obj : null; if (o == null || d == null) return;
            ClearWaiting();
            int n = Mathf.Min(o.Passengers.Count, ShuttleRearDoor.Seats); string type = string.IsNullOrEmpty(o.PassengerType) ? "grunt" : o.PassengerType;
            for (int i = 0; i < n; i++)
            {
                int k = ShuttleRearDoor.DoorSeat(i);
                var go = SpawnFigure(type, o.Owner, d.SeatPoint(k), Quaternion.LookRotation(-d.AisleDir(k), v.transform.up));
                if (!go) continue;
                float f = shuttleFit.TryGetValue(type, out var ff) ? ff : ShuttleFitOf(go, type);
                go.transform.localScale = Vector3.one * (go.transform.localScale.x * f);
                var an = go.GetComponentInChildren<Animator>(); string now = null; Play(an, ref now, "seated_gunner", 1f, 0.01f);
                if (an) an.applyRootMotion = false;
                var w = FindWeapon(go); if (w) w.gameObject.SetActive(false);
                waitingSeats[k] = go; waitingHips[go] = FindHips(go); ShuttleWaiting++;
                PlaceWaiting(go, k, d, v);
            }
        }
        readonly Dictionary<GameObject, Transform> waitingHips = new Dictionary<GameObject, Transform>();

        void PlaceWaiting(GameObject go, int k, ShuttleRearDoor d, UnitView v)
        {
            waitingHips.TryGetValue(go, out var hips);
            var up = v.transform.up; var off = HipsOffset(go, hips, up, pelvisHeight * go.transform.localScale.x);
            go.transform.SetPositionAndRotation(d.SeatPoint(k) - off, Quaternion.LookRotation(-d.AisleDir(k), up));
            Band(go.GetComponentInChildren<SkinnedMeshRenderer>(true), go.transform, hips, go.transform.localScale.x);
        }

        /// <summary>ShuttleRearDoor, every frame while passengers wait on the benches for the exit: they ride with the ship.</summary>
        public void ShuttleSeatTick(UnitView v, ShuttleRearDoor d)
        {
            if (waitingSeats.Count == 0 || !v || d == null) return;
            foreach (var kv in waitingSeats) if (kv.Value) PlaceWaiting(kv.Value, kv.Key, d, v);
        }
        public int ShuttleWaitingNow => waitingSeats.Count;

        float ShuttleFitOf(GameObject go, string type)
        {
            Bounds b = default; bool any = false;
            foreach (var rr in go.GetComponentsInChildren<Renderer>()) { if (!rr.enabled || rr is ParticleSystemRenderer) continue; if (!any) { b = rr.bounds; any = true; } else b.Encapsulate(rr.bounds); }
            float h = any ? b.size.y / Mathf.Max(0.01f, go.transform.localScale.x) : 2.4f;
            float f = Mathf.Clamp(ShuttleRearDoor.FitHeight / Mathf.Max(0.3f, h), 0.3f, 1f);
            shuttleFit[type] = f; ShuttleFitLog[type] = new Vector2(h, f);
            return f;
        }

        /// <summary>ShuttleRearDoor, the ramp shut: no waiting figure is left on a bench.</summary>
        public void ShuttleClearWaiting() => ClearWaiting();
        void ClearWaiting() { foreach (var kv in waitingSeats) if (kv.Value) Destroy(kv.Value); waitingSeats.Clear(); waitingHips.Clear(); }

        /// <summary>OzShuttleBreakup: the squad aboard is lost with the ship (rule). Their bodies fall out of the breaking ship among its
        /// pieces as the game's blast-thrown robot bodies (OzShuttleCrewFall: the ragdoll, its flailing limbs, the whoosh as it leaves and
        /// the metal thud where it lands, now and then a death cry): the figures walking up the ramp, the ones waiting on the benches for
        /// an exit, the seated ones, and for the rest of the `count` lost a figure on each bay seat (filled from the front). Each is
        /// thrown out of the hull's middle with the ship's own velocity, away from the killing hit (push: its flat direction, zero for
        /// none; power: how hard it was hit, rifle 0.55 .. fort gun 1.6); destroyed on the ground the throw is lower.</summary>
        public void ShuttleCrewLost(ZObject s, int count, string type, Team team, Vector3 vel, Vector3 from, bool air, Vector3 push, float power)
        {
            if (s == null || !game) return;
            OzShuttleCrewFall.BeginBreak();
            ShuttleDoorwayTake(s.Id);   // K418: the ramp's file first: saved figures walk on to their robots, lost exiters are held (BoardingView.ShuttleDoorway.cs)
            UnitView v = View(s.Id); var r = v ? RigOf(v) : null; var d = v ? ShuttleRearDoor.Of(v) : null;
            float fit = shuttleFit.TryGetValue(type, out var ff) ? Mathf.Max(0.05f, ff) : 1f;
            // the rules' list of the lost (one row per robot, squad order: its type, a hero's member name); without it `count` of the type
            var lost = new List<ZShuttleLost>(); var w = game.World;
            if (w != null) foreach (var row in w.ShuttleLostCrew) if (row.shipId == s.Id && row.seq > shuttleLostSeq) lost.Add(row);
            if (lost.Count > 0) { shuttleLostSeq = lost[lost.Count - 1].seq; count = lost.Count; }
            var bodies = new List<(GameObject go, float scale)>();
            if (r != null)
                foreach (var g in ghosts)
                    if (g.rig == r && g.enter && !g.demo && !g.done && g.go) { var go = g.go; g.go = null; bodies.Add((go, g.baseScale)); EndGhost(g, true); }   // on the ramp: already aboard
            foreach (var kv in waitingSeats) if (kv.Value) bodies.Add((kv.Value, kv.Value.transform.localScale.x / fit));                  // waiting on a bench for the exit
            waitingSeats.Clear(); waitingHips.Clear();
            if (d != null) foreach (var h in d.TakeHeld()) if (h.go) bodies.Add((h.go, h.go.transform.lossyScale.x / fit));                // seated
            ShuttleDoorwayBodies(s.Id, bodies);   // K418: the exiters still inside when it died (DV126), held where they were drawn
            int had = bodies.Count, more = Mathf.Max(0, count - had);
            for (int i = 0; i < more; i++)
            {
                int k = ShuttleRearDoor.FrontSeat(i);
                var at = d != null ? d.SeatPoint(k) : from + UnityEngine.Random.insideUnitSphere * 0.8f;
                var rot = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
                var go = had + i < lost.Count ? SpawnLostFigure(lost[had + i], at, rot) : SpawnFigure(type, team, at, rot);
                if (go) bodies.Add((go, go.transform.localScale.x));
            }
            push.y = 0f; float pk = Mathf.Clamp01(power);
            if (push.sqrMagnitude > 1e-4f) push.Normalize();
            int n = 0;
            foreach (var (go, scale) in bodies)
            {
                if (!go) continue;
                var p = go.transform.position; var outward = p - from; outward.y = 0f;
                outward = outward.sqrMagnitude > 1e-3f ? outward.normalized : new Vector3(UnityEngine.Random.Range(-1f, 1f), 0f, UnityEngine.Random.Range(-1f, 1f)).normalized;
                var dir = outward * 0.7f + push * 0.8f * pk; dir = Quaternion.Euler(0f, UnityEngine.Random.Range(-30f, 30f), 0f) * (dir.sqrMagnitude > 1e-4f ? dir.normalized : outward);
                float strength = (air ? 0.85f + 0.25f * power : 0.5f + 0.15f * power) * OzShuttleBreakup.CrewThrowK(air), lift = air ? 0.9f : 0.5f;   // K396: thrown with the pieces by the big blast (OzShuttleBreakup.BigBlast.cs)
                uint seed = unchecked((uint)(s.Id * 73856093) ^ (uint)((n + 1) * 19349663) ^ 0x9e3779b9u);
                strength *= OzShuttleBreakup.CrewVary(s.Id, n);   // K399: each body its own throw, x0.9-1.1 (OzShuttleBreakup.ThrowVary.cs)
                PrepareBody(go, scale);
                string now = null;
                if (!RobotRagdoll.CanStart()) Play(go.GetComponentInChildren<Animator>(), ref now, "blast_airborne", 1f, 0.05f);   // the scripted fall's pose
                if (OzShuttleCrewFall.Throw(go, dir, strength, lift, vel, air, seed, s.Id * 31 + n, LandPose)) n++;
                AudioDirector.CrewDeath(p, s.Id);                                                                  // a cry or a body sound now and then (the death pools)
            }
            ShuttleBodies += n;
        }

        /// <summary>A crew figure becomes a body (as Fling does, without the corpse list: OzShuttleCrewFall keeps it with the wreck).</summary>
        void PrepareBody(GameObject go, float baseScale)
        {
            Unparent(go);
            var t = go.transform;
            t.localScale = Vector3.one * baseScale;                                                              // the bay's fit no longer
            t.position += Vector3.up * 0.1f;
            var weapon = FindWeapon(go); if (weapon) weapon.gameObject.SetActive(true);
            var hold = go.GetComponent<WeaponHold>(); if (hold) hold.Suspended = true;
            OzGlowFade.Begin(go);
            Band(go.GetComponentInChildren<SkinnedMeshRenderer>(true), t, FindHips(go), baseScale);
            var an = go.GetComponentInChildren<Animator>(); if (an) an.applyRootMotion = false;
        }

        int shuttleLostSeq;                                                                                  // the last lost-crew row thrown (this level's world)

        /// <summary>A lost robot's figure as it was: a hero member in his own model and colours, else the type's crew figure.</summary>
        GameObject SpawnLostFigure(ZShuttleLost row, Vector3 at, Quaternion rot)
        {
            var prefab = string.IsNullOrEmpty(row.member) ? null : Resources.Load<GameObject>(OzHeroSquadView.PrefabDir + "Z_hero_" + row.member);
            if (!prefab) return SpawnFigure(string.IsNullOrEmpty(row.type) ? "grunt" : row.type, row.team, at, rot);
            var go = Instantiate(prefab, at, rot, box);
            go.name = "crew_" + row.member;
            UnitLookMotionGate.KeepUnitCrisp(go);
            var tp = go.GetComponent<TeamPaint>(); if (tp) tp.Team = row.team;
            var look = go.GetComponent<OzHeroLook>(); if (look) look.Apply(row.team);
            var an = go.GetComponentInChildren<Animator>(); if (an) an.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            return go;
        }

        static void LandPose(GameObject go) { string now = null; Play(go ? go.GetComponentInChildren<Animator>() : null, ref now, "death_back", 1f, 0.05f); }
    }
}
