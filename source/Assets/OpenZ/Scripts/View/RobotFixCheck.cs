using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// units hold; (2) blast deaths fly at real speed with flailing limbs (air time, apex); (3) squad spacing in the widened V
    /// (closest pair of squad members while walking, per type); (4) the pyro pilot flame (drawn, frame at 70 m).
    /// </summary>
    public class RobotFixCheck : MonoBehaviour
    {
        public static string Report = "idle";
        ZGame g; ZWorld W; readonly StringBuilder sb = new StringBuilder(); int pass, total;
        static string Records => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records"));
        static string Frames => Records + "/milestones/robot_fix";
        public static void Run() { Report = "running"; new GameObject("RobotFixCheck").AddComponent<RobotFixCheck>(); }
        void Start() { g = ZGame.I; W = g.World; System.IO.Directory.CreateDirectory(Frames); StartCoroutine(All()); }
        void Row(string t, bool ok, string m) { total++; if (ok) pass++; sb.AppendLine($"| {t} | {(ok ? "PASS" : "FAIL")} | {m} |"); }
        ZObject Squad(string type, Team t, int tx, int ty) { OzCheatSpawner.FreeTile(W, tx, ty, 1, true, out tx, out ty); var o = W.CreateRobotGroup(type, tx * 16, ty * 16, t); g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m); return o; }
        void Kill(ZObject o) { if (o == null || o.Destroyed) return; foreach (var m in new List<ZObject>(o.Minions)) { m.SetHealth(0); W.UpdateObjectHealth(m); } o.SetHealth(0); W.UpdateObjectHealth(o); }
        void Look(Vector3 p, float h) { var c = FindFirstObjectByType<RtsCamera>(); if (c) { c.focus = new Vector3(p.x, 0, p.z); c.SetHeightImmediate(h); } }
        void Shot(string n) => ScreenCapture.CaptureScreenshot(Frames + "/" + n + ".png");

        IEnumerator All()
        {
            yield return null; W.Bots.Clear();
            sb.AppendLine($"# RobotFixCheck {System.DateTime.Now:yyyy-MM-dd HH:mm} {g.level}\n\n| test | result | measured |\n|---|---|---|");

            // ---- (5) auto-grab: three idle red squads near a neutral flag (spawned like factory output: no player order)
            var flag = W.Flags.Where(f => f.Owner == Team.Neutral).OrderBy(f => Mathf.Abs(f.CenterX - 36 * 16) + Mathf.Abs(f.CenterY - 45 * 16)).FirstOrDefault();
            if (flag != null)
            {
                int fx = flag.CenterX / 16, fy = flag.CenterY / 16;
                var sq = new List<ZObject> { Squad("grunt", Team.Red, fx + 6, fy), Squad("grunt", Team.Red, fx - 6, fy), Squad("grunt", Team.Red, fx, fy + 6) };
                Look(ZGame.PxToWorld(flag.CenterX, flag.CenterY), 45f);
                yield return new WaitForSeconds(2.5f);
                int going = sq.Count(s => s.Waypoints.Any(w => w.refId == flag.Id));
                Row("DV38 idle squads near a neutral flag: only one runs for it", going == 1, $"flag #{flag.Id} at {fx},{fy}: {going} of 3 idle squads heading for it");
                // an ordered squad holds: send one next to another neutral flag, it must not grab it after arriving
                var f2 = W.Flags.Where(f => f.Owner == Team.Neutral && f != flag).OrderBy(f => Mathf.Abs(f.CenterX - flag.CenterX) + Mathf.Abs(f.CenterY - flag.CenterY)).FirstOrDefault();
                if (f2 != null)
                {
                    var ordered = sq[1];
                    W.SendWaypoints(Team.Red, ordered, new List<ZWaypoint> { ZWaypoint.Move(f2.CenterX + 4 * 16, f2.CenterY) });
                    float t0 = Time.time; while (Time.time - t0 < 25f && ordered.Waypoints.Count > 0) yield return null;
                    yield return new WaitForSeconds(3f);
                    bool grabbed = ordered.Waypoints.Any(w => w.refId == f2.Id);
                    Row("DV38 / DV55 a squad the player moved next to a flag takes it after arriving", (grabbed || f2.Owner == Team.Red) && ordered.PlayerHold, $"arrived {(ordered.Waypoints.Count == 0 ? "yes" : "no")}, hold {ordered.PlayerHold}, went for flag #{f2.Id}: {grabbed}");
                }
                Shot("dv38_one_per_flag");
                foreach (var s in sq) Kill(s);
                yield return new WaitForSeconds(1f);
            }

            // ---- (3) spacing while walking, per type
            foreach (var o in new List<ZObject>(W.Objects))                                                       // clear the ground of other units (no fights)
                if (o.IsUnit && !o.Destroyed && o.Kind != ZKind.Cannon && Mathf.Abs(o.CenterX / 16 - 44) < 14 && Mathf.Abs(o.CenterY / 16 - 20) < 12) { o.SetHealth(0); W.UpdateObjectHealth(o); }
            yield return new WaitForSeconds(1f);
            var types = new[] { "grunt", "psycho", "tough", "pyro", "laser", "sniper" };
            var squads = new List<ZObject>(); int i = 0;
            foreach (var t in types) squads.Add(Squad(t, Team.Red, 38 + (i % 3) * 6, 13 + (i++ / 3) * 6));   // open ground north-middle (x 37-51, y 13-25: no rocks), 12 m apart
            yield return new WaitForSeconds(3f);                                                                // the squads unstack from their spawn pixel standing (as SpacingTrace), then walk
            foreach (var s in squads) W.SendWaypoints(Team.Red, s, new List<ZWaypoint> { ZWaypoint.Move(s.CenterX, s.CenterY + 5 * 16) });
            Look(ZGame.PxToWorld(45 * 16, 19 * 16), 34f);
            var minD = types.ToDictionary(t => t, t => 999f); var minAll = types.ToDictionary(t => t, t => 999f); var why = types.ToDictionary(t => t, t => ""); float tw = Time.time; bool framed = false;
            while (Time.time - tw < 8f)
            {
                if (Time.time - tw < 1f) { yield return null; continue; }                             // the V opens as the leader starts
                foreach (var s in squads)
                {
                    var mem = new List<ZObject> { s }; mem.AddRange(s.Minions.Where(m => !m.Destroyed));
                    if (s.Waypoints.Count == 0 || s.Minions.Any(m => !m.Destroyed && m.InFormation && m.FormSqueeze < 0.99)) continue;   // walking, not squeezed (squeezes past buildings file up by design); a member on its own path (slot blocked) or closing into its slot counts too
                    for (int a = 0; a < mem.Count; a++) for (int b = a + 1; b < mem.Count; b++)
                        {
                            if (!g.Views.TryGetValue(mem[a].Id, out var va) || !g.Views.TryGetValue(mem[b].Id, out var vb) || !va || !vb) continue;
                            var d = va.transform.position - vb.transform.position; d.y = 0f;
                            minAll[s.Type] = Mathf.Min(minAll[s.Type], d.magnitude);
                            if (Time.time - tw >= 2.5f && d.magnitude < minD[s.Type])
                            {
                                minD[s.Type] = d.magnitude;
                                var ds = g.CenterWorldSmooth(mem[a]) - g.CenterWorldSmooth(mem[b]); ds.y = 0f;
                                why[s.Type] = $"t {Time.time - tw:0.0} s, members {a}/{b}, V {(a == 0 || mem[a].InFormation ? 1 : 0)}/{(mem[b].InFormation ? 1 : 0)}, sim {ds.magnitude:0.00} m";
                            }
                        }
                }
                if (!framed && Time.time - tw > 4f) { Shot("squads_walking"); framed = true; }
                yield return null;
            }
            foreach (var t in types) sb.AppendLine($"|   spacing {t} closest | | {why[t]} |");
            Row("Squads walk spaced in the open V (>= 1.6 m once open, never touching: >= 1.1 m while it opens)", minD.Values.All(d => d >= 1.6f && d < 900f) && minAll.Values.All(d => d >= 1.1f), "open V " + string.Join(", ", minD.Select(kv => $"{kv.Key} {kv.Value:0.00}")) + " m; while opening " + string.Join(", ", minAll.Select(kv => $"{kv.Key} {kv.Value:0.00}")) + " m");

            // ---- (4) pilot flames on the pyros, seen from 70 m
            var pyro = squads.First(s => s.Type == "pyro");
            Look(ZGame.PxToWorld(pyro.CenterX, pyro.CenterY), 70f); yield return new WaitForSeconds(0.5f);
            Shot("pyro_pilot_70m"); yield return null;
            Look(ZGame.PxToWorld(pyro.CenterX, pyro.CenterY), 24f); yield return new WaitForSeconds(0.5f);
            Shot("pyro_pilot_24m"); yield return null;
            Row("Pyro pilot flames drawn", PyroPilotFlame.Drawn >= 3, $"{PyroPilotFlame.Drawn} pilot flames this frame (3 pyros alive)");
            foreach (var s in squads) Kill(s);
            yield return new WaitForSeconds(1f);

            // ---- (2) blast deaths: a red tough squad kills a blue grunt squad; air time / apex of the first ragdolls
            var blue = Squad("grunt", Team.Blue, 46, 16); foreach (var m in new[] { blue }.Concat(blue.Minions)) m.Damage = 0;
            var tough = Squad("tough", Team.Red, 46, 23);
            W.SendWaypoints(Team.Red, tough, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Attack, refId = blue.Id, x = blue.CenterX, y = blue.CenterY } });
            Look(ZGame.PxToWorld(blue.CenterX, blue.CenterY), 26f);
            var watch = new List<UnitView>(); foreach (var m in new[] { blue }.Concat(blue.Minions)) if (g.Views.TryGetValue(m.Id, out var v) && v) watch.Add(v);
            var air = new Dictionary<UnitView, (float start, float end, float apex)>(); float tb = Time.time; int fr = 0;
            int flails0 = RagdollFlail.Started;
            while (Time.time - tb < 25f)
            {
                foreach (var v in watch)
                {
                    if (!v || !v.Dying) continue;
                    var hips = v.GetComponentsInChildren<Rigidbody>().FirstOrDefault();
                    if (!hips) continue;
                    float h = hips.position.y - g.GroundY(hips.position.x, hips.position.z);
                    if (!air.TryGetValue(v, out var a)) a = (Time.time, -1f, 0f);
                    if (a.end < 0f) { a.apex = Mathf.Max(a.apex, h); if (Time.time - a.start > 0.25f && h < 0.6f) a.end = Time.time; }
                    air[v] = a;
                    if (fr < 6 && Time.time - a.start > 0.2f && a.end < 0f) { Shot($"blast_flight_{fr:00}"); fr++; }
                }
                if (air.Count > 0 && air.Values.All(x => x.end > 0f) && Time.time - air.Values.Min(x => x.start) > 3f) break;
                yield return null;
            }
            var flights = air.Values.Where(x => x.end > 0f).Select(x => x.end - x.start).ToList();
            Row("Blast deaths fly at real speed with flailing limbs", flights.Count > 0 && flights.Max() < 2.2f && RagdollFlail.Started > flails0,
                $"{air.Count} ragdolls, air time {(flights.Count > 0 ? $"{flights.Min():0.00}-{flights.Max():0.00} s" : "-")} (was 3.5-4.4 s), apex {(air.Count > 0 ? air.Values.Max(x => x.apex) : 0):0.0} m, flails started {RagdollFlail.Started - flails0}; frames blast_flight_*.png");
            Kill(tough); Kill(blue);

            sb.AppendLine($"\n**{pass}/{total} PASS**");
            System.IO.File.WriteAllText(Records + "/robot_fix_check.md", sb.ToString());
            Report = $"{pass}/{total}"; Destroy(gameObject);
        }
    }
}
