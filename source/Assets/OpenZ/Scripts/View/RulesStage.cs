using System.Collections;
using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Staged rules demo on L02 (for the video and the U4 check). Every order goes through the normal command layer; the
    /// stage only spawns the extra units it needs and speeds the game up while waiting for production:
    /// 1 a red squad runs onto the Z7 flag (zone, factory and radar turn red); 2 the red fort builds a Gatling and places
    /// it on a fort tower; 3 Toughs blow up the vertical bridge in Z5 and a red crane repairs it; 4 a red strike force
    /// fights up to the blue fort and a jeep drives into the gate: the fort goes up in the nuke, blue is eliminated.
    /// </summary>
    public class RulesStage : MonoBehaviour
    {
        public readonly List<string> Log = new List<string>();
        public string Phase = "setup";
        public float PhaseTime;
        ZGame game; ZWorld W => game.World; RtsCamera cam; bool running;

        public static RulesStage Begin()
        {
            var s = FindFirstObjectByType<RulesStage>() ?? ZGame.I.gameObject.AddComponent<RulesStage>();
            if (s.running) return s;
            s.running = true; s.StartCoroutine(s.Run());
            return s;
        }

        void Set(string p) { Phase = p; PhaseTime = Time.time; Log.Add($"{W.Time:F1}s {p}"); }
        ZObject At(string type, int tx, int ty) => W.Objects.FirstOrDefault(o => o.Type == type && o.LocX == tx * 16 && o.LocY == ty * 16);
        ZObject Squad(string type, int tx, int ty, Team t) { var o = W.CreateRobotGroup(type, tx * 16, ty * 16, t); game.SpawnUnitView(o); foreach (var m in o.Minions) game.SpawnUnitView(m); return o; }
        ZObject Unit(string cat, string type, int tx, int ty, Team t) { var o = W.CreateObject(cat, type, tx * 16, ty * 16, t); game.SpawnUnitView(o); return o; }
        void Order(ZObject u, WpMode m, ZObject target, int x = 0, int y = 0, bool attackTo = false)
        {
            var wp = new ZWaypoint { mode = m, refId = target?.Id ?? -1, x = target?.CenterX ?? x, y = target?.CenterY ?? y, attackTo = attackTo };
            bool ok = W.SendWaypoints(u.Owner, u, new[] { wp });
            Log.Add($"{W.Time:F1}s order {u.Type}#{u.Id} {m} {(target != null ? target.Type : $"({x / 16},{y / 16})")} -> {(ok && u.Waypoints.Count > 0 ? "ok" : "REFUSED")}");
        }
        void Fly(ZObject o, float h) { if (cam) { cam.FlyTo(game.CenterWorld(o)); cam.ZoomTo(h); } }
        void Fly(Vector3 p, float h) { if (cam) { cam.FlyTo(p); cam.ZoomTo(h); } }
        IEnumerator WaitSim(double seconds, System.Func<bool> until = null)
        {
            double end = W.Time + seconds;
            while (W.Time < end && (until == null || !until())) yield return null;
        }

        // parity P2: the stage speeds up its production waits (2x/4x). The fast speed lives only inside WaitSimFast, and its
        // finally restores Zod speed (an exception inside the wait unwinds through it); OnDisable covers Play stop / removal.
        IEnumerator WaitSimFast(float speed, double seconds, System.Func<bool> until)
        {
            game.gameSpeed = speed;
            try { yield return WaitSim(seconds, until); }
            finally { if (game != null) game.gameSpeed = 1f; }
        }

        void OnDisable() { if (game != null) game.gameSpeed = 1f; }

        IEnumerator Run()
        {
            game = ZGame.I; cam = FindFirstObjectByType<RtsCamera>();
            var redFort = At("fort_back", 48, 72); var blueFort = At("fort_front", 14, 7);
            var flag7 = W.Flags.First(f => f.LocX == 28 * 16 && f.LocY == 67 * 16);

            // 1 ---- capture Z7: the red squad nearest to the flag runs onto it
            Set("1 capture Z7");
            var squad = W.Objects.Where(o => o.Owner == Team.Red && o.Kind == ZKind.Robot && !o.IsMinion)
                                 .OrderBy(o => (o.CenterX - flag7.CenterX) * (o.CenterX - flag7.CenterX) + (o.CenterY - flag7.CenterY) * (o.CenterY - flag7.CenterY)).First();
            Fly(ZGame.PxToWorld(flag7.CenterX + 60, flag7.CenterY), 55f);
            Order(squad, WpMode.Move, flag7);
            yield return WaitSim(40, () => flag7.Owner == Team.Red);
            yield return new WaitForSeconds(2.5f);                                                          // pulse + markers on screen

            // 2 ---- production: fort output, then a Gatling stored and placed on a fort tower
            Set("2 production");
            Fly(redFort, 60f);
            yield return WaitSimFast(4f, 90, () => redFort.CurUnit == "grunt" && redFort.BInit > 1 && W.Time - redFort.BInit < 0.5);   // first squad out
            yield return new WaitForSeconds(3f);
            W.StopBuilding(Team.Red, redFort); W.StartBuilding(Team.Red, redFort, "gatling");
            Log.Add($"{W.Time:F1}s fort: Gatling, {redFort.BFinal - redFort.BInit:F2} s (flags {W.TeamFlagFraction(Team.Red):F3})");

            // 3 ---- meanwhile: toughs blow up the Z5 bridge, a crane repairs it
            Set("3 bridge");
            var bridge = At("bridge_vert", 51, 58);
            var t1 = Squad("tough", 49, 55, Team.Red); var t2 = Squad("tough", 55, 55, Team.Red);
            Fly(bridge, 50f);
            Order(t1, WpMode.Attack, bridge); Order(t2, WpMode.Attack, bridge);
            yield return WaitSimFast(2f, 120, () => bridge.Destroyed);
            Log.Add($"{W.Time:F1}s bridge destroyed");
            yield return new WaitForSeconds(3.5f);
            var crane = Unit("vehicle", "crane", 53, 54, Team.Red);
            Order(crane, WpMode.CraneRepair, bridge);
            yield return WaitSimFast(2f, 60, () => !bridge.Destroyed);
            Log.Add($"{W.Time:F1}s bridge repaired by the crane");
            yield return new WaitForSeconds(2.5f);

            // 2b ---- the Gatling: wait for storage, place it on the fort's free tower spot
            Set("2b cannon");
            Fly(redFort, 55f);
            yield return WaitSimFast(4f, 120, () => redFort.StoredCannons.Count > 0);
            ZObject cannon = null;
            foreach (var (tx, ty) in new[] { (55, 72), (49, 75), (55, 75) })
                if ((cannon = W.PlaceCannon(Team.Red, redFort, tx, ty)) != null) { Log.Add($"{W.Time:F1}s Gatling placed on the fort tower ({tx},{ty}), ejectable {cannon.Ejectable}"); break; }
            W.StopBuilding(Team.Red, redFort); W.StartBuilding(Team.Red, redFort, "grunt");
            yield return new WaitForSeconds(3f);

            // 4 ---- assault on the blue fort: strike force attack-moves the gate, then a jeep drives in
            Set("4 assault");
            blueFort.GetCreationMovePoint(out int gx, out int gy);
            var force = new List<ZObject> { Unit("vehicle", "light", 15, 32, Team.Red), Unit("vehicle", "light", 22, 32, Team.Red), Unit("vehicle", "medium", 18, 34, Team.Red),
                                            Squad("tough", 16, 34, Team.Red), Squad("tough", 21, 34, Team.Red), Squad("grunt", 20, 35, Team.Red) };
            Fly(ZGame.PxToWorld(gx, gy + 60), 70f);
            foreach (var u in force) Order(u, WpMode.Move, null, gx, gy + 24, true);
            yield return WaitSim(45, () => !W.Objects.Any(o => o.Owner == Team.Blue && o.IsMobile && !o.Destroyed && ZObject.Within(o.CenterX, o.CenterY, gx, gy, 260)));
            var jeep = Unit("vehicle", "jeep", 18, 32, Team.Red);
            Order(jeep, WpMode.EnterFort, blueFort);
            Fly(ZGame.PxToWorld(gx, gy + 20), 55f);
            yield return WaitSim(40, () => blueFort.Destroyed || W.Get(jeep.Id) == null);
            if (!blueFort.Destroyed)                                                                            // jeep lost: the grunts go in
            {
                var g = force.Last(u => W.Get(u.Id) != null);
                Order(g, WpMode.EnterFort, blueFort);
                yield return WaitSim(40, () => blueFort.Destroyed);
            }
            Set(blueFort.Destroyed ? "5 fort destroyed" : "5 fort still standing");
            yield return new WaitForSeconds(12f);
            Set("done");
        }
    }
}
