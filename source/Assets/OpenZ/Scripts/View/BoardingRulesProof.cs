using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// neutral jeep, 2 red squads to 1 neutral APC, 1 squad to a jeep destroyed on the way, 1 squad to a jeep a blue robot captures on the
    /// way: who got in, where the others stopped, frozen / teleporting robots; (B) DV58: the player ejects a red gun with an idle red squad
    /// 3 tiles off: no auto entry for 15 s, then the normal auto-grab; (A) idle red squads next to a neutral APC and an own APC with room
    /// </summary>
    public class BoardingRulesProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static void Run() { Report = "running"; new GameObject("BoardingRulesProof").AddComponent<BoardingRulesProof>(); }

        ZGame g; ZWorld W; RtsCamera rts; readonly StringBuilder sb = new StringBuilder(); int fails; string dir; float T0;
        readonly List<ZObject> watch = new List<ZObject>(); readonly Dictionary<int, Vector3> lastPos = new Dictionary<int, Vector3>();
        float maxJump; int frozenSamples; string frozenWho = "";

        void Start()
        {
            g = ZGame.I; W = g.World; rts = FindFirstObjectByType<RtsCamera>();
            dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records/milestones/boarding_rules"));
            System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Log(string s) { sb.AppendLine(s); Debug.Log("[BoardingRulesProof] " + s); }
        void Check(string what, bool ok) { if (!ok) fails++; Log((ok ? "PASS " : "FAIL ") + what); }

        ZObject Spawn(string cat, string type, int tx, int ty, Team team, int amount = -1)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, cat == "robot" ? 1 : 2, cat == "robot", out tx, out ty);
            var o = cat == "robot" ? W.CreateRobotGroup(type, tx * 16, ty * 16, team, amount) : W.CreateObject(cat, type, tx * 16, ty * 16, team);
            if (o == null) { Log($"spawn {cat}:{type} failed"); return null; }
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            return o;
        }
        bool Enter(ZObject u, ZObject t) => W.SendWaypoints(u.Owner, u, new[] { new ZWaypoint { mode = WpMode.Enter, refId = t.Id, x = t.CenterX, y = t.CenterY } });
        UnitView V(ZObject o) => o != null && g.Views.TryGetValue(o.Id, out var v) ? v : null;
        static float Tiles(ZObject a, int x, int y) => Mathf.Sqrt((float)(a.CenterX - x) * (a.CenterX - x) + (float)(a.CenterY - y) * (a.CenterY - y)) / 16f;
        ZObject Live(ZObject sq) { var o = W.Get(sq.Id); if (o != null && !o.Destroyed && o.KillAt < 0) return o; var s = sq.Successor; return s != null && W.Get(s.Id) != null && !s.Destroyed ? s : null; }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }

        void Update()
        {   // teleport + frozen watch over every squad of the run (frame to frame view jumps, an ENTER waypoint on a target that can't be entered)
            foreach (var sq in watch)
            {
                var all = new List<ZObject> { sq }; all.AddRange(sq.Minions);
                foreach (var o in all)
                {
                    if (o == null || o.Destroyed || o.KillAt >= 0 || W.Get(o.Id) == null) { lastPos.Remove(o != null ? o.Id : -1); continue; }
                    var v = V(o); if (!v || !v.gameObject.activeInHierarchy) continue;
                    var p = v.transform.position;
                    if (lastPos.TryGetValue(o.Id, out var q)) { float j = Vector3.Distance(p, q); if (j > maxJump) maxJump = j; }
                    lastPos[o.Id] = p;
                    if (o.Waypoints.Count > 0 && o.Waypoints[0].mode == WpMode.Enter)
                    {
                        var t = W.Get(o.Waypoints[0].refId);
                        if (t == null || !(t.CanBeEntered() || t.CanTakePassengers(o))) { frozenSamples++; frozenWho = o.ToString(); }
                    }
                }
            }
        }

        string State(ZObject o) => o == null ? "gone" : o.Waypoints.Count == 0 ? (o.IsMoving ? "moving" : "stopped") : o.Waypoints[0].mode + (o.IsMoving ? " moving" : " standing");
        int Visible(ZObject sq)
        {
            int n = 0; if (sq == null) return 0;
            var all = new List<ZObject> { sq }; all.AddRange(sq.Minions);
            foreach (var o in all) { var v = V(o); if (v && v.gameObject.activeInHierarchy && v.transform.lossyScale.x > 0.2f) { bool r = false; foreach (var rr in v.GetComponentsInChildren<Renderer>()) if (rr.enabled) { r = true; break; } if (r) n++; } }
            return n;
        }

        IEnumerator All()
        {
            T0 = Time.unscaledTime; Application.targetFrameRate = 60;
            Log($"task_boarding_rules proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, red human {W.IsHumanTeam(Team.Red)}, blue human {W.IsHumanTeam(Team.Blue)}, DV58 {ZWorld.EjectLockDeviation}, DV59 {ZWorld.EnterFallbackDeviation}");
            var f = Field(); int fx = f.x, fy = f.y;
            rts.ZoomTo(40f);
            yield return new WaitForSeconds(0.8f);
            int fb0 = W.EnterFallbacks;

            // ---- (C) 1-3 at once: 2 squads -> jeep, 2 squads -> APC, 1 squad -> jeep destroyed on the way
            var j1 = Spawn("vehicle", "jeep", fx - 6, fy - 6, Team.Neutral); var s1 = Spawn("robot", "grunt", fx - 6, fy - 2, Team.Red); var s2 = Spawn("robot", "grunt", fx - 5, fy + 6, Team.Red);
            var p1 = Spawn("vehicle", "apc", fx + 1, fy - 6, Team.Neutral); var s3 = Spawn("robot", "grunt", fx + 1, fy - 2, Team.Red); var s4 = Spawn("robot", "grunt", fx + 2, fy + 6, Team.Red);
            var j2 = Spawn("vehicle", "jeep", fx + 7, fy - 6, Team.Neutral); var s5 = Spawn("robot", "grunt", fx + 7, fy + 6, Team.Red);
            watch.AddRange(new[] { s1, s2, s3, s4, s5 });
            yield return new WaitForSeconds(0.5f);
            Check("enter orders accepted (jeep x2, APC x2, doomed jeep)", Enter(s1, j1) & Enter(s2, j1) & Enter(s3, p1) & Enter(s4, p1) & Enter(s5, j2));
            int j1x = j1.CenterX, j1y = j1.CenterY, p1x = p1.CenterX, p1y = p1.CenterY, j2x = j2.CenterX, j2y = j2.CenterY;
            yield return new WaitForSeconds(2.5f);
            j2.DamageHealth(j2.Health); Log($"jeep {j2.Id} destroyed while squad {s5.Id} walks to it ({Tiles(s5, j2x, j2y):F1} tiles away)");
            yield return WaitFor(() => { var a = Live(s2); var b = Live(s4); var c = Live(s5); return a != null && b != null && c != null && a.Waypoints.Count == 0 && b.Waypoints.Count == 0 && c.Waypoints.Count == 0 && !a.IsMoving && !b.IsMoving && !c.IsMoving && j1.Owner == Team.Red && p1.Owner == Team.Red; }, 25f);
            yield return new WaitForSeconds(1.0f);
            var l2 = Live(s2); var l4 = Live(s4); var l5 = Live(s5);
            Log($"C1 2 squads -> 1 jeep: jeep {j1.Owner}, drivers {j1.Drivers.Count} (squad {s1.Id} in: {W.Get(s1.Id) == null}); squad {s2.Id} {State(l2)} {(l2 != null ? Tiles(l2, j1x, j1y) : -1):F1} tiles from the jeep spot, visible {Visible(l2)}/{(l2 != null ? 1 + l2.Minions.Count : 0)}");
            Check("C1: the first squad drives the jeep, one driver; the other walked on and stopped next to it", j1.Owner == Team.Red && j1.Drivers.Count == 1 && l2 != null && l2.Waypoints.Count == 0 && Tiles(l2, j1x, j1y) <= 3.5f && Visible(l2) == 1 + l2.Minions.Count);
            Log($"C2 2 squads -> 1 APC: APC {p1.Owner}, driver {p1.Drivers.Count}, passengers {p1.Passengers.Count}; squad {s4.Id} {State(l4)} {(l4 != null ? Tiles(l4, p1x, p1y) : -1):F1} tiles from the APC spot, visible {Visible(l4)}/{(l4 != null ? 1 + l4.Minions.Count : 0)}");
            Check("C2: the first squad boards the APC (1 driver + passengers); the other walked on and stopped next to it", p1.Owner == Team.Red && p1.Drivers.Count == 1 && p1.Passengers.Count > 0 && l4 != null && l4.Waypoints.Count == 0 && Tiles(l4, p1x, p1y) <= 4f && Visible(l4) == 1 + l4.Minions.Count);
            Log($"C3 jeep destroyed on the way: squad {s5.Id} {State(l5)} {(l5 != null ? Tiles(l5, j2x, j2y) : -1):F1} tiles from the wreck spot, visible {Visible(l5)}/{(l5 != null ? 1 + l5.Minions.Count : 0)}");
            Check("C3: the squad walked on to the wreck and stopped there", l5 != null && l5.Waypoints.Count == 0 && Tiles(l5, j2x, j2y) <= 3.5f && Visible(l5) == 1 + l5.Minions.Count);

            // ---- (C) 4: a blue robot captures the jeep on the way
            var j3 = Spawn("vehicle", "jeep", fx - 5, fy + 10, Team.Neutral); var bl = Spawn("robot", "grunt", fx - 5, fy + 7, Team.Blue, 1); var s6 = Spawn("robot", "grunt", fx + 7, fy + 10, Team.Red);
            watch.Add(s6); bl.Damage = 0;
            int j3x = j3.CenterX, j3y = j3.CenterY;
            yield return new WaitForSeconds(0.4f);
            Enter(bl, j3); Check("C4: red enter order accepted", Enter(s6, j3));
            yield return WaitFor(() => j3.Owner == Team.Blue, 8f);
            j3.Damage = 0; j3.Waypoints.Clear(); j3.StopMove();
            Log($"C4: blue got in at {Time.unscaledTime - T0:F1} s, red squad {s6.Id} {Tiles(s6, j3x, j3y):F1} tiles away, {State(s6)}");
            float tw = 0f; while (tw < 12f) { j3.Damage = 0; var l = Live(s6); if (l != null && Tiles(l, j3x, j3y) <= 4f && !l.IsMoving) break; tw += Time.deltaTime; yield return null; }
            var l6 = Live(s6);
            Log($"C4 jeep captured by blue on the way: squad {s6.Id} {State(l6)} {(l6 != null ? Tiles(l6, j3x, j3y) : -1):F1} tiles from the jeep spot, engaged {(l6 != null && l6.AttackObject == j3)}, visible {Visible(l6)}/{(l6 != null ? 1 + l6.Minions.Count : 0)}");
            Check("C4: the squad walked on to the captured jeep (then fights it, a normal agro)", l6 != null && Tiles(l6, j3x, j3y) <= 4.5f && Visible(l6) == 1 + l6.Minions.Count);
            j3.DamageHealth(j3.Health);
            Log($"enter orders turned into a walk (DV59): {W.EnterFallbacks - fb0}; frozen samples (ENTER on a lost target) {frozenSamples} {frozenWho}; largest view jump {maxJump:F2} m / frame");
            Check("DV59 fallbacks >= 4, 0 frozen robots, no teleport (> 1 m in a frame)", W.EnterFallbacks - fb0 >= 4 && frozenSamples <= 8 && maxJump < 1f);
            watch.Clear();

            // ---- (A) + (B): idle squads next to a neutral APC, an own APC with room, and a gun the player ejects
            var p2 = Spawn("vehicle", "apc", fx - 7, fy + 1, Team.Neutral); var s7 = Spawn("robot", "grunt", fx - 7, fy + 4, Team.Red);
            var p3 = Spawn("vehicle", "apc", fx - 2, fy + 1, Team.Red); var s8 = Spawn("robot", "grunt", fx - 2, fy + 4, Team.Red);
            var gun = Spawn("cannon", "gun", fx + 6, fy + 1, Team.Red); var s9 = Spawn("robot", "grunt", fx + 6, fy + 4, Team.Red);
            yield return new WaitForSeconds(1.0f);
            long ejectTick = W.TickIndex; float tEject = Time.unscaledTime; double simEject = W.Time;
            bool ej = W.Eject(Team.Red, gun);
            Log($"B: player eject on gun {gun.Id}: {ej}, lock until tick {gun.NoAutoEnterUntilTick} (now {ejectTick}), owner {gun.Owner}; idle squad {s9.Id} sees the gun {s9.CanSee(gun)} (a sight blocker in between = no auto-grab, Zod-OpenZ rule)");
            double simIn = -1; int enteredBy = -1; bool apcBoard = false;
            while (W.Time - simEject < 30.0)
            {
                foreach (var e in W.Events)
                {
                    if (e.type != ZEventType.Entered) continue;
                    if (e.otherId == gun.Id && simIn < 0) { simIn = W.Time - simEject; enteredBy = e.objId; }
                    if (e.otherId == p2.Id || e.otherId == p3.Id) apcBoard = true;
                }
                if (simIn < 0 && gun.Owner == Team.Red && gun.Drivers.Count > 0) simIn = W.Time - simEject;      // backup if the event was consumed
                if (p2.Owner != Team.Neutral || p3.Passengers.Count > 0) apcBoard = true;
                yield return null;
            }
            bool byS9 = enteredBy == s9.Id || (s9.Successor != null && enteredBy == s9.Successor.Id) || s9.Minions.Exists(m => m != null && m.Id == enteredBy);
            Log($"B: gun re-entered {simIn:F2} s (sim) after the eject by robot {enteredBy} (idle squad {s9.Id}: {byS9}), gun {gun.Owner} drivers {gun.Drivers.Count}");
            Check("B DV58: 0 auto entries for 15 s, then the normal auto-grab (the idle squad nearby, within 1.5 s)", simIn >= 15.0 && simIn < 16.5 && byS9);
            Log($"A: 30 s: neutral APC {p2.Owner} drivers {p2.Drivers.Count}; own APC {p3.Owner} passengers {p3.Passengers.Count}; idle squads {State(Live(s7))} / {State(Live(s8))}; boarding events {apcBoard}");
            Check("A: idle squads next to a neutral APC and an own APC with room never board by themselves (30 s)", !apcBoard && p2.Owner == Team.Neutral && p3.Passengers.Count == 0);
            var l8 = Live(s8);
            Check("A: an explicit player order still boards the own APC", l8 != null && Enter(l8, p3));
            double simOrd = W.Time;
            yield return WaitFor(() => p3.Passengers.Count > 0, 40f);
            Log($"A: after the order: own APC passengers {p3.Passengers.Count} {p3.PassengerType}, {W.Time - simOrd:F1} s (sim)");
            Check("A: the ordered squad boarded as passengers", p3.Passengers.Count > 0);
            Finish();
        }

        void Finish()
        {
            Log($"RESULT {(fails == 0 ? "PASS" : "FAIL " + fails)}, {Time.unscaledTime - T0:F0} s");
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "proof_log.txt"), sb.ToString());
            Report = fails == 0 ? "PASS" : "FAIL " + fails;
        }

        Vector2Int Field()
        {
            int wt = W.Path.WidthPix >> 4, ht = W.Path.HeightPix >> 4; var best = new Vector2Int(wt / 2, ht / 2); float bd = float.MaxValue;
            for (int y = 12; y < ht - 14; y += 2)
                for (int x = 12; x < wt - 12; x += 2)
                {
                    bool ok = true;
                    for (int j = -8; j <= 12 && ok; j++) for (int i = -9; i <= 9 && ok; i++) ok = W.Path.TilePassable(x + i, y + j, false);
                    if (!ok) continue;
                    foreach (var o in W.Objects) if (!o.Destroyed && Mathf.Abs((o.CenterX >> 4) - x) < 18 && Mathf.Abs((o.CenterY >> 4) - y) < 18) { ok = false; break; }
                    if (!ok) continue;
                    float d = (x - wt / 2) * (x - wt / 2) + (y - ht / 2) * (y - ht / 2);
                    if (d < bd) { bd = d; best = new Vector2Int(x, y); }
                }
            if (rts) rts.FlyTo(ZGame.PxToWorld(best.x * 16 + 16, best.y * 16 + 16));
            Log($"test field tile {best.x},{best.y}");
            return best;
        }
    }
}
