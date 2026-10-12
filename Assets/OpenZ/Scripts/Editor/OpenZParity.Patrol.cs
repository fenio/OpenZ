using System;
using System.Linq;
using OpenZ.Sim;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// on L02: a red robot squad patrols 30 m out and back (legs counted, both ends reached), an enemy robot dropped on its beat is engaged
    /// and the patrol goes on afterwards, STOP empties the list, HOLD keeps the unit in place while an enemy walks by out of range
    /// (no chase), and a determinism replay (two worlds, the same orders, bots on, 900 ticks, identical object state).
    /// </summary>
    public static partial class OpenZParity
    {
        static Deviation DevPatrol(Func<string, int, int> getInt)
        {
            bool was = ZWorld.PatrolDeviation;
            try
            {
                ZWorld.PatrolDeviation = true;
                ZWorld Make(int seed, bool bots)
                {
                    var w = ZWorld.Create(SettingsText, MapText("L02"), seed, false);
                    if (bots) { w.Bots.Add(new ZBot(w, Team.Blue, 108)); }
                    return w;
                }
                ZObject Leader(ZWorld w) => w.Objects.Where(o => o.Owner == Team.Red && o.Kind == ZKind.Robot && !o.IsMinion && o.CanMove && !o.Destroyed)
                                               .OrderBy(o => o.Id).FirstOrDefault();
                // 1) patrol legs
                var w1 = Make(3, false); var u = Leader(w1);
                if (u == null) return new Deviation { id = "DV68", change = "Patrol / stop / hold", zod = "-", openz = "-", measured = "no red robot on L02", toggle = "-", status = DevCheck };
                int ax = u.CenterX, ay = u.CenterY, bx = ax - 240, by = ay - 60;
                bool sent = w1.SendPatrol(Team.Red, u, bx, by);
                bool reachedB = false, backA = false; int legs0 = u.PatrolLegs;
                for (int i = 0; i < 20000 && !(reachedB && backA); i++)   // ZConst.TICK = 0.01 s: up to 200 s
                {
                    w1.Tick(ZConst.TICK); w1.Events.Clear();
                    long db = (long)(u.CenterX - bx) * (u.CenterX - bx) + (long)(u.CenterY - by) * (u.CenterY - by);
                    long da = (long)(u.CenterX - ax) * (u.CenterX - ax) + (long)(u.CenterY - ay) * (u.CenterY - ay);
                    if (db < 24 * 24) reachedB = true;
                    if (reachedB && da < 24 * 24) backA = true;
                }
                int legs = u.PatrolLegs - legs0;
                // 2) an intruder on the beat: engaged, then the patrol resumes
                var e = w1.CreateObject("robot", "grunt", (ax + bx) / 2 - 8, (ay + by) / 2 - 8, Team.Blue);
                bool engaged = false; int legsBefore = u.PatrolLegs;
                for (int i = 0; i < 15000; i++)
                {
                    w1.Tick(ZConst.TICK); w1.Events.Clear();
                    if (u.AttackObject == e || (u.Waypoints.Count > 0 && u.Waypoints[0].mode == WpMode.Attack && u.Waypoints[0].refId == e.Id)) engaged = true;
                    if (engaged && (e.Destroyed || u.Destroyed) && u.PatrolLegs > legsBefore) break;
                }
                bool resumed = u.Destroyed || (u.Patrolling && u.PatrolLegs > legsBefore);
                // 3) stop + hold
                var w2 = Make(5, false); var h = Leader(w2);
                w2.SendWaypoints(Team.Red, h, new[] { ZWaypoint.Move(h.CenterX - 160, h.CenterY) });
                for (int i = 0; i < 20; i++) { w2.Tick(ZConst.TICK); w2.Events.Clear(); }
                bool stopped = w2.SendStop(Team.Red, h) && h.Waypoints.Count == 0;
                bool held = w2.SendHold(Team.Red, h); int hx = h.CenterX, hy = h.CenterY;
                var walker = w2.CreateObject("robot", "grunt", hx - 8 + h.AttackRadius + 60, hy - 8, Team.Blue);   // inside agro range, outside fire range
                for (int i = 0; i < 2000; i++) { w2.Tick(ZConst.TICK); w2.Events.Clear(); }
                bool noChase = Math.Abs(h.CenterX - hx) <= 8 && Math.Abs(h.CenterY - hy) <= 8 && h.HoldPosition;
                // 4) determinism
                string Run()
                {
                    var r = Make(11, true); var l = Leader(r);
                    r.SendPatrol(Team.Red, l, l.CenterX - 200, l.CenterY + 40);
                    for (int i = 0; i < 3000; i++) { r.Tick(ZConst.TICK); r.Events.Clear(); }
                    return string.Join(";", r.Objects.Select(o => $"{o.Id}:{o.LocX},{o.LocY},{o.Health},{(o.Destroyed ? 1 : 0)}"));
                }
                bool det = Run() == Run();
                bool ok = sent && reachedB && backA && legs >= 1 && engaged && resumed && stopped && held && noChase && det;
                return new Deviation
                {
                    id = "DV68", change = "Modern RTS orders Z lacks: PATROL (looping attack-move between where the unit stood and the clicked point), STOP, HOLD POSITION",
                    zod = "no patrol, no stop key, no hold position (attack-move, Shift-queue, groups exist)",
                    openz = "ZObject.Patrol.cs: a patrol record re-issues Zod's own attack-move leg (MOVE + attackTo, CheckAttackTo engages intruders, the leg resumes) " +
                            "toward the other end whenever a leg ends; STOP = Zod's empty SEND_WAYPOINTS; HOLD = stop + no AGRO / auto-grab / help-call movement (fires in range " +
                            "only). Any new order ends a patrol / hold. Keys Q / X / H (OzBindings, rebindable, F1 help lists them); commands go through ZWorld like every order",
                    measured = $"patrol sent {sent}, reached B {reachedB}, back at A {backA}, legs {legs}; intruder engaged {engaged}, patrol resumed {resumed}; " +
                               $"stop {stopped}, hold {held}, no chase while an enemy walked in agro range {noChase}; determinism (3000 ticks, blue bot) {(det ? "identical" : "DIFFERENT")}",
                    toggle = "ZWorld.PatrolDeviation (off = the orders are refused)",
                    status = ok ? DevActive : DevCheck,
                };
            }
            finally { ZWorld.PatrolDeviation = was; }
        }
    }
}
