using System.Collections;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// rules decide who of the file was outside (saved) and who was inside (lost) by two moments, and this view draws the walks so that
    /// they are true:
    ///  * Walking in: from the ramp foot (the rules' moment for each figure) up the ramp and through the doorway takes exactly
    ///    ZWorld.ShuttleBoardDoorSec; the aisle and the bench take the rest of the rules' ShuttleRampInSec.
    ///  * Walking out: from the bench to the doorway takes exactly ZWorld.ShuttleExitDoorSec; the doorway, the ramp and the foot the rest of
    ///    ZWorld.ShuttleRampOutSec (both fit the rules' moments as before).
    ///  * At the kill (the rules' Death event, and again first thing at the break-up, whichever comes first; once per robot): a SAVED
    ///    figure (on the ramp, in the doorway, walking to the ramp) leaves its walk where it is drawn and walks on, down to the ground and
    ///    to its robot standing behind the wreck (the robot's own drawing hidden until it arrives: no double, no pop); a LOST exiter (still
    ///    on its bench or in the aisle) stops where it is and is thrown out with the pieces at the break (BoardingView.ShuttleCrewLost), like
    ///    the boarders already inside. The generic crew fling of a vehicle's death leaves the shuttle's figures to its break-up (no second
    ///    body for the same robot).
    /// View only.
    /// </summary>
    public partial class BoardingView
    {
        /// <summary>Proof: boarding / exit walks timed to the doorway moments, saved figures walked on to their robots, saved robots that had
        /// no figure (drawn at once), lost exiters stopped for the break, lost exiters thrown as bodies, a fallback fling (no break-up came).</summary>
        public static int DoorwayEnterTimed, DoorwayExitTimed, DoorwayWalks, DoorwayWalksDone, DoorwayNoFigure, DoorwayHeld, DoorwayBodies, DoorwayFallbacks, DoorwaySeatedFreed;
        /// <summary>Proof: the last walks (robot id, from, to, seconds) and the worst gap left between a walker and its robot at the end (m).</summary>
        public static readonly List<string> DoorwayLog = new List<string>();
        public static float DoorwayWorstEnd;
        /// <summary>The saved figures walking to their robots now (the Play proof waits for 0).</summary>
        public int DoorwayWalking => doorWalking;
        /// <summary>The walk on to the robot: pace (m/s) and the longest it may take (s; then it is there).</summary>
        public static float DoorwayWalkMps = 2.4f, DoorwayWalkMaxSec = 4f;

        /// <summary>Proof: the ghosts (boarding or exit walks) still drawn for that vehicle, and the figures held for its break.</summary>
        public int ShuttleGhostsFor(int vehicleId) { int n = 0; foreach (var x in ghosts) if (!x.done && x.go && x.rig != null && x.rig.obj != null && x.rig.obj.Id == vehicleId) n++; return n; }
        public int ShuttleHeldFor(int vehicleId) => doorwayLost.TryGetValue(vehicleId, out var l) ? l.Count : 0;

        int doorwaySeq, doorWalking;
        readonly Dictionary<int, List<(GameObject go, float scale)>> doorwayLost = new Dictionary<int, List<(GameObject go, float scale)>>();

        // ------------------------------------------------------------------ the walks timed to the doorway moments
        /// <summary>BuildShuttleEnter hook (after its budget): foot -> tip -> sill -> just inside = ShuttleBoardDoorSec exactly; the aisle and
        /// the bench fit what is left of the budget.</summary>
        void ShuttleDoorwayEnterTiming(List<Key> keys, int footK, float budget)
        {
            if (!ZWorld.ShuttleDoorwayDeviation || keys.Count < footK + 6) return;
            float a = keys[footK + 1].dur + keys[footK + 2].dur + keys[footK + 3].dur, want = (float)ZWorld.ShuttleBoardDoorSec;
            if (a <= 0.01f) return;
            for (int i = footK + 1; i <= footK + 3; i++) keys[i].dur *= want / a;
            float rest = 0f; for (int i = footK + 4; i < keys.Count; i++) rest += keys[i].dur;
            float room = Mathf.Max(0.2f, budget - want);
            if (rest > room) for (int i = footK + 4; i < keys.Count; i++) keys[i].dur = Mathf.Max(0.05f, keys[i].dur * room / rest);
            DoorwayEnterTimed++;
        }

        /// <summary>BuildShuttleExit hook (after its fit to ShuttleRampOutSec): bench -> aisle -> just inside the doorway = ShuttleExitDoorSec
        /// exactly; the doorway, the ramp and the foot the rest.</summary>
        void ShuttleDoorwayExitTiming(List<Key> keys, int footK)
        {
            if (!ZWorld.ShuttleDoorwayDeviation || footK < 5 || keys.Count <= footK) return;
            float a = keys[1].dur + keys[2].dur + keys[3].dur, b = 0f;
            for (int i = 4; i <= footK; i++) b += keys[i].dur;
            float wantA = (float)ZWorld.ShuttleExitDoorSec, wantB = (float)(ZWorld.ShuttleRampOutSec - ZWorld.ShuttleExitDoorSec);
            if (a <= 0.01f || b <= 0.01f) return;
            for (int i = 1; i <= 3; i++) keys[i].dur *= wantA / a;
            for (int i = 4; i <= footK; i++) keys[i].dur *= wantB / b;
            DoorwayExitTimed++;
        }

        // ------------------------------------------------------------------ the kill
        /// <summary>The Death event, CrewDies and ShuttleCrewLost (first thing): the rules' rows of the file not handled yet, each once.</summary>
        void ShuttleDoorwayTake(int shipId)
        {
            if (!game || game.World == null) return;
            var w = game.World;
            foreach (var row in w.ShuttleDoorwayRows)
            {
                if (row.seq <= doorwaySeq) continue;
                doorwaySeq = row.seq;
                if (row.saved) DoorwaySaved(row);
                else DoorwayLostExiter(row);
            }
            DoorwayGroundWalkers(shipId);
            DoorwayFreeSeated(shipId);
        }

        /// <summary>Exit figures of that ship already off the ramp, still walking on the ground to their robots (no row: past the foot):
        /// they walk on to them as the saved do (the hull's drawing going away at the break no longer ends their walk with a jump).</summary>
        void DoorwayGroundWalkers(int shipId)
        {
            List<Ghost> mine = null;
            foreach (var x in ghosts)
                if (!x.done && x.go && !x.enter && !x.demo && x.footT > 0f && x.time >= x.footT && x.rig != null && x.rig.obj != null && x.rig.obj.Id == shipId) (mine ??= new List<Ghost>()).Add(x);
            if (mine == null) return;
            foreach (var g in mine)
            {
                var robot = game.World.Get(g.robotId);
                if (robot == null || !OzV11Alive(robot)) continue;                                             // a lost one: its row holds it for the break
                var live = g.live ? g.live : View(robot.Id);
                if (live) DoorwayWalkOn(g, live, robot.Id, false);
            }
        }

        /// <summary>The boarders already on their benches (held by the ramp, children of the ship) leave the ship's drawing before the
        /// break-up hides every renderer under it; they stay where they sit and are thrown out with the pieces as before (visible).</summary>
        void DoorwayFreeSeated(int shipId)
        {
            if (!OzShuttleBreakup.Enabled) return;
            var v = View(shipId); var d = v ? ShuttleRearDoor.Of(v) : null;
            if (d == null || d.Seated == 0) return;
            foreach (var h in d.TakeHeld()) if (h.go) { Unparent(h.go); d.HoldFigure(h.go, h.id); DoorwaySeatedFreed++; }
        }

        /// <summary>CrewDies hook: a shuttle's figures are its break-up's (true = leave them); the rows are taken first either way.</summary>
        bool ShuttleDoorwayCrewDies(Rig r)
        {
            if (r == null || r.obj == null || !r.obj.IsShuttle) return false;
            ShuttleDoorwayTake(r.obj.Id);
            if (!OzShuttleBreakup.Enabled) { DoorwayFlingHeld(r.obj.Id, r.root ? r.root.position : Vector3.zero); return false; }   // the old whole fall: the generic fling as before
            return true;
        }

        void DoorwaySaved(ZShuttleDoorwayRow row)
        {
            var robot = game.World.Get(row.robotId);
            Ghost g = null;
            foreach (var x in ghosts)
                if (!x.done && x.go && x.enter == row.boarding && x.robotId == (row.boarding ? row.oldId : row.robotId)) { g = x; break; }
            if (robot == null || !OzV11Alive(robot)) { if (g != null && g.go) { var gone = g.go; g.go = null; EndGhost(g, true); Destroy(gone); } return; }
            UnitView live = row.boarding ? game.SpawnUnitView(robot) : (g != null && g.live ? g.live : View(robot.Id));
            if (g == null || !live) { DoorwayNoFigure++; DoorwaySettle(live); return; }   /* K420: it still ends on its rules place (View/BoardingView.Settle.cs) */                                             // nothing drawn for it: its robot shows where it stands
            DoorwayWalkOn(g, live, robot.Id, row.boarding);
        }

        /// <summary>The ghost's figure leaves its scripted walk where it is drawn and walks on to its robot (hidden until it arrives).</summary>
        void DoorwayWalkOn(Ghost g, UnitView live, int robotId, bool boarding)
        {
            var go = g.go; g.go = null;
            var walker = new DoorWalker { go = go, live = live, robotId = robotId, anim = g.anim, hips = g.hips, skin = g.skin, weapon = g.weapon, rot = go.transform.rotation, clipNow = g.clipNow };
            if (!boarding) { walker.hidden.AddRange(g.hidden); g.hidden.Clear(); }                           // the exiter's robot stays hidden until the figure reaches it
            EndGhost(g, true);
            if (boarding) foreach (var rr in live.GetComponentsInChildren<Renderer>(true)) if (rr.enabled) { rr.enabled = false; walker.hidden.Add(rr); }
            Unparent(go);
            walker.s0 = go.transform.localScale.x; walker.s1 = live.transform.localScale.x;
            walker.from = go.transform.position;
            doorWalking++; DoorwayWalks++; ExitRingsWalker(robotId, go);   // K431: its rings and frame walk with it (View/BoardingView.ExitRings.cs)
            StartCoroutine(DoorWalk(walker));
            ExitWalkDoorWalker(robotId, go);                                                                 // K441: its robot's shots are drawn when it is there (View/BoardingView.ExitWalk.cs)
        }

        static bool OzV11Alive(ZObject o) => o != null && !o.Destroyed && o.KillAt < 0;

        void DoorwayLostExiter(ZShuttleDoorwayRow row)
        {
            Ghost g = null;
            foreach (var x in ghosts) if (!x.done && x.go && !x.enter && x.robotId == row.robotId) { g = x; break; }
            if (g == null) return;                                                                             // nothing drawn: the break makes a figure from its row
            var go = g.go; g.go = null; g.hidden.Clear();                                                      // its robot is taken off the map: its drawing stays hidden
            float scale = g.baseScale; var root = g.rig != null ? g.rig.root : null;
            EndGhost(g, true);
            Unparent(go);                                                                                      // held where it is drawn (the ship stands on the ground) until the break; never a child of the ship: the break hides every renderer under it
            string now = g.clipNow; Play(g.anim, ref now, now == "seated_gunner" ? "seated_gunner" : "idle", 1f, 0.15f);
            if (!doorwayLost.TryGetValue(row.shipId, out var list)) doorwayLost[row.shipId] = list = new List<(GameObject go, float scale)>();
            list.Add((go, scale)); DoorwayHeld++;
            StartCoroutine(DoorwayFallback(row.shipId, root ? root.position : go.transform.position));
        }

        /// <summary>ShuttleCrewLost hook (beside the seated ones): the lost exiters held where they were drawn become bodies with the pieces.</summary>
        void ShuttleDoorwayBodies(int shipId, List<(GameObject go, float scale)> bodies)
        {
            if (!doorwayLost.TryGetValue(shipId, out var list)) return;
            foreach (var b in list) if (b.go) { bodies.Add(b); DoorwayBodies++; }
            doorwayLost.Remove(shipId);
        }

        /// <summary>No break-up came for that ship within 3 s (the old whole fall): the held figures are flung as every crew is.</summary>
        IEnumerator DoorwayFallback(int shipId, Vector3 from)
        {
            float t0 = Time.time;
            while (Time.time - t0 < 3f) yield return null;
            DoorwayFlingHeld(shipId, from);
        }

        void DoorwayFlingHeld(int shipId, Vector3 from)
        {
            if (!doorwayLost.TryGetValue(shipId, out var list)) return;
            doorwayLost.Remove(shipId);
            foreach (var b in list) if (b.go) { Fling(b.go, from, 2.4f, b.scale); DoorwayFallbacks++; }
        }

        // ------------------------------------------------------------------ a saved figure walks on to its robot
        sealed class DoorWalker
        {
            public GameObject go; public UnitView live; public int robotId; public Animator anim; public Transform hips, weapon; public SkinnedMeshRenderer skin;
            public readonly List<Renderer> hidden = new List<Renderer>();
            public Vector3 from; public Quaternion rot; public float s0 = 1f, s1 = 1f; public string clipNow;
        }

        IEnumerator DoorWalk(DoorWalker k)
        {
            float t = 0f; var p = k.from; float lift0 = Mathf.Max(0f, p.y - Ground(p).y);
            if (k.weapon) k.weapon.gameObject.SetActive(true);
            while (true)
            {
                yield return null;
                if (!k.go) break;
                var o = k.live ? k.live.Obj : null;
                if (!k.live || !k.live.gameObject.activeInHierarchy || k.live.Dying || o == null || !OzV11Alive(o) || game.World.Get(o.Id) != o) break;   // the robot is gone (killed, picked up): no walk to nothing
                float dt = Time.deltaTime * SimRate; t += dt;
                var target = Ground(k.live.transform.position);
                var flat = target - p; flat.y = 0f; float d = flat.magnitude;
                float step = Mathf.Max(DoorwayWalkMps, d / Mathf.Max(0.25f, DoorwayWalkMaxSec - t)) * dt;
                if (d > 1e-4f) p += flat / d * Mathf.Min(step, d);
                float down = Mathf.Clamp01(t / 0.35f);                                                         // off the ramp: down to the ground in the first steps
                var g = Ground(p); p.y = g.y + lift0 * (1f - Mathf.SmoothStep(0f, 1f, down));
                if (d > 0.05f) { var want = Quaternion.LookRotation(-flat.normalized, Vector3.up); k.rot = Quaternion.Slerp(k.rot, want, 1f - Mathf.Exp(-dt * 14f)); }
                var tr = k.go.transform; tr.SetPositionAndRotation(p, k.rot);
                tr.localScale = Vector3.one * Mathf.Lerp(k.s0, k.s1, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.4f)));   // the bay's fit back to its own size
                float v = dt > 0f ? Mathf.Min(step, d) / dt : 0f;
                string now = k.clipNow; Play(k.anim, ref now, d > 0.05f ? (v > 2.4f ? "run_forward" : "walking") : "idle", d > 0.05f ? Mathf.Clamp(v / (v > 2.4f ? RunClipSpeed : WalkClipSpeed), 0.6f, 2.5f) * SimRate : SimRate); k.clipNow = now;
                Band(k.skin, tr, k.hips, tr.localScale.x);
                if (d <= 0.05f || t >= DoorwayWalkMaxSec) { DoorwayWorstEnd = Mathf.Max(DoorwayWorstEnd, d); break; }
            }
            foreach (var r in k.hidden) if (r) r.enabled = true;
            k.hidden.Clear();
            DoorwaySettle(k.live);                                                                           // K420: the drawn robot ends on its rules place
            if (k.go) Destroy(k.go);
            doorWalking = Mathf.Max(0, doorWalking - 1); DoorwayWalksDone++;
            DoorwayLog.Add($"#{k.robotId} walked {t:F2} s from {k.from} to its robot");
            if (DoorwayLog.Count > 64) DoorwayLog.RemoveAt(0);
        }
    }
}
