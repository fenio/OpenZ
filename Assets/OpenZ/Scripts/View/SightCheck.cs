using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Play proof for los_clearance_v1 (DV22 building clearance, DV23 line of sight, DV24 squad V formation), run on L02 in the live game:
    ///  1. LOS: a red light tank west of the red fort, a blue grunt squad north of it (the fort between them). Idle 8 s: every direct
    ///     shot is checked against the sim's own sight grid (ClearUncached); then an attack order: time to the first shot, distance moved.
    ///  2. Arcing fire: a red howitzer behind the fort fires over it (shots counted).
    ///  3. Clearance: a grunt squad and a heavy tank are sent onto a tile covered by the fort mesh; after 15 s no unit may stand on a
    ///     mesh tile (ZClearance.MeshOwner).
    ///  4. V formation: a grunt squad crosses open ground; mid-run every minion must be behind the leader (in its heading frame), both
    ///     sides filled, and no two members closer than 6 px.
    /// </summary>
    [DefaultExecutionOrder(40)]
    public class SightCheck : MonoBehaviour
    {
        public static string Report = "idle";
        ZGame g; ZWorld W; readonly StringBuilder sb = new StringBuilder(); int pass, total;
        readonly List<ZEvent> shots = new List<ZEvent>();
        readonly List<bool> shotClear = new List<bool>();                  // the line at the moment of the shot (sim's own sight grid)
        static string Records => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records"));

        public static void Run() { Report = "running"; new GameObject("SightCheck").AddComponent<SightCheck>(); }

        void Start() { g = ZGame.I; W = g.World; System.IO.Directory.CreateDirectory(Records + "/milestones/sight_check"); StartCoroutine(All()); }

        ZObject keepAlive;
        void Update()
        {
            if (W == null) return;
            if (keepAlive != null && !keepAlive.Destroyed) { keepAlive.SetHealth(keepAlive.MaxHealth); foreach (var m in keepAlive.Minions) if (!m.Destroyed) m.SetHealth(m.MaxHealth); }
            foreach (var e in W.Events)
            {
                if (e.type != ZEventType.MissileFired && e.type != ZEventType.Hitscan) continue;
                var a = W.Get(e.objId); var b = W.Get(e.otherId);
                shots.Add(e); shotClear.Add(a == null || b == null || a.ArcingFire || W.Sight.ClearUncached(a.CenterX, a.CenterY, W.Sight.ExemptOf(a), b.CenterX, b.CenterY, W.Sight.ExemptOf(b)));
            }
        }

        ZObject Squad(string type, Team t, int tx, int ty)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, 1, true, out tx, out ty);
            var o = W.CreateRobotGroup(type, tx * 16, ty * 16, t); g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            return o;
        }
        ZObject Thing(string cat, string type, Team t, int tx, int ty)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, 2, false, out tx, out ty);
            var o = W.CreateObject(cat, type, tx * 16, ty * 16, t); g.SpawnUnitView(o); return o;
        }
        void Kill(ZObject o)
        {
            if (o == null || o.Destroyed) return;
            foreach (var m in new List<ZObject>(o.Minions)) { m.SetHealth(0); W.UpdateObjectHealth(m); }
            o.SetHealth(0); W.UpdateObjectHealth(o);
        }
        void Tough(ZObject o) { o.MaxHealth *= 50; o.SetHealth(o.MaxHealth); foreach (var m in o.Minions) { m.MaxHealth *= 50; m.SetHealth(m.MaxHealth); } }
        void Look(ZObject o, float h) { var cam = FindFirstObjectByType<RtsCamera>(); if (cam) { cam.FlyTo(ZGame.PxToWorld(o.CenterX, o.CenterY)); cam.SetHeightImmediate(h); } }
        void Shot(string name) => ScreenCapture.CaptureScreenshot(Records + "/milestones/sight_check/" + name + ".png");
        void Row(string test, bool ok, string measured) { total++; if (ok) pass++; sb.AppendLine($"| {test} | {(ok ? "PASS" : "FAIL")} | {measured} |"); }

        int ShotsBy(int id, int from, out int blocked)
        {
            int n = 0; blocked = 0;
            for (int i = from; i < shots.Count; i++)
            {
                var e = shots[i]; if (e.objId != id) continue; n++;
                if (!shotClear[i]) blocked++;
            }
            return n;
        }

        IEnumerator All()
        {
            yield return null;
            sb.AppendLine("# SightCheck (los_clearance_v1: DV22 clearance, DV23 line of sight, DV24 V formation)\n");
            sb.AppendLine($"{System.DateTime.Now:yyyy-MM-dd HH:mm}, level {g.level}, LOS {ZWorld.LosDeviation}, clearance {ZWorld.ClearanceDeviation}\n");
            sb.AppendLine("| test | result | measured |\n|---|---|---|");
            ZObject fort = null; foreach (var o in W.Objects) if (o.IsFort && o.Owner == Team.Red && !o.Destroyed && o.IsProducer) { fort = o; break; }
            if (fort == null) { Report = "no red fort"; yield break; }
            int fx = fort.LocX / 16, fy = fort.LocY / 16;

            // ---- 1. line of sight, across a neutral building mid-map (away from both bases' defences, which killed the target at the fort)
            ZObject wall = null; int best = int.MaxValue; int cx = W.Map.WidthPix / 2, cy = W.Map.HeightPix / 2;
            foreach (var o in W.Objects)
                if (o.Kind == ZKind.Building && !o.Destroyed && !o.IsFort && !o.IsBridge && o.Owner == Team.Neutral && o.Type.Contains("factory"))
                { int d = System.Math.Abs(o.CenterX - cx) + System.Math.Abs(o.CenterY - cy); if (d < best) { best = d; wall = o; } }
            if (wall == null) wall = fort;
            int wx = wall.CenterX / 16, wy = wall.CenterY / 16;
            sb.AppendLine($"| LOS scene | info | wall {wall.Type}@{wall.LocX / 16},{wall.LocY / 16}; tank west, squad east |");
            var tank = Thing("vehicle", "light", Team.Red, wx - 5, wy); Tough(tank);
            var blues = Squad("grunt", Team.Blue, wx + 4, wy); Tough(blues);
            foreach (var m in blues.Minions) m.Damage = 0; blues.Damage = 0;
            keepAlive = blues;                                               // healed every frame (Damage 0 would stop a unit attacking at all)
            bool lineAtStart = W.Sight.Clear(tank, blues);
            Look(wall, 40f);
            int s0 = shots.Count; float t0 = Time.time;
            while (Time.time - t0 < 8f) yield return null;
            int idleShots = ShotsBy(tank.Id, s0, out int idleBlocked);
            Shot("01_los_idle");
            Row("LOS idle 8 s: tank, fort between, no direct shot through walls", idleBlocked == 0,
                $"line at start {lineAtStart}; tank shots {idleShots}, through walls {idleBlocked}; held {tank.LosHeldShots}");
            int s1 = shots.Count; int x0 = tank.CenterX, y0 = tank.CenterY; float t1 = Time.time, first = -1f;
            W.SendWaypoints(Team.Red, tank, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Attack, refId = blues.Id, x = blues.CenterX, y = blues.CenterY } });
            while (Time.time - t1 < 15f)
            {
                if (first < 0f) for (int i = s1; i < shots.Count; i++) if (shots[i].objId == tank.Id) { first = Time.time - t1; break; }
                yield return null;
            }
            int atkShots = ShotsBy(tank.Id, s1, out int atkBlocked);
            float moved = Mathf.Sqrt((tank.CenterX - x0) * (tank.CenterX - x0) + (tank.CenterY - y0) * (tank.CenterY - y0));
            Shot("02_los_attack_order");
            Row("LOS attack order: drives to a clear line and fires, never through walls", first >= 0f && atkBlocked == 0,
                $"first shot after {first:F1} s, moved {moved:F0} px, shots {atkShots}, through walls {atkBlocked}, spot plans {tank.LosSpotPlans}, dropped {tank.LosDropped}");

            // ---- 2. arcing fire over the fort
            var how = Thing("cannon", "howitzer", Team.Red, wx - 6, wy + 2);
            int s2 = shots.Count; float t2 = Time.time; bool wallBetween = !W.Sight.Clear(how, blues);
            while (Time.time - t2 < 20f) yield return null;
            int howShots = ShotsBy(how.Id, s2, out _);
            float hd = Mathf.Sqrt(Mathf.Pow(how.CenterX - blues.CenterX, 2) + Mathf.Pow(how.CenterY - blues.CenterY, 2));
            Row("Arcing fire: howitzer fires over the fort", howShots > 0,
                $"howitzer shots {howShots}; wall between at start {wallBetween}; arcing {how.ArcingFire}; target alive {!blues.Destroyed}, distance {hd:F0} px, range {how.AttackRadius}, attacking {(how.AttackObject != null ? how.AttackObject.Type + "#" + how.AttackObject.Id : "none")}, owner {how.Owner}, crew {how.DriverType}");
            keepAlive = null; Kill(tank); Kill(blues); Kill(how);
            yield return new WaitForSeconds(1f);

            // ---- 3. clearance: order onto a fort mesh tile (south wall)
            var C = W.Clearance; int mx = -1, my = -1;
            if (C != null)
                for (int ty = fy + 14; ty >= fy && mx < 0; ty--)
                    for (int tx = fx; tx < fx + 10; tx++) if (C.MeshOwner(tx, ty) != 0 && !C.KeptOpenTile(tx, ty)) { mx = tx; my = ty; break; }
            if (mx < 0) Row("Clearance: fort mesh tile found", false, "none (Clearance null or no mesh tile)");
            else
            {
                var sq = Squad("grunt", Team.Red, mx - 1, my + 4); var hv = Thing("vehicle", "heavy", Team.Red, mx + 3, my + 5);
                foreach (var u in new[] { sq, hv }) W.SendWaypoints(Team.Red, u, new List<ZWaypoint> { ZWaypoint.Move(mx * 16 + 8, my * 16 + 8) });
                Look(sq, 30f);
                yield return new WaitForSeconds(15f);
                var inMesh = new List<string>(); var all = new List<ZObject> { sq, hv }; all.AddRange(sq.Minions);
                foreach (var u in all)
                {
                    int ctx = u.CenterX / 16, cty = u.CenterY / 16;
                    if (C.MeshOwner(ctx, cty) != 0 && !C.KeptOpenTile(ctx, cty)) inMesh.Add($"{u.Type}#{u.Id}@{ctx},{cty}");
                }
                Shot("03_clearance_fort_wall");
                Row("Clearance: units ordered onto the fort wall stop outside the mesh", inMesh.Count == 0,
                    $"target tile {mx},{my}; units {all.Count}; standing in mesh: {(inMesh.Count == 0 ? "none" : string.Join(" ", inMesh))}; eases {W.Pathing.ClearanceEases}, walk-outs {W.Pathing.ClearanceWalkOuts}");
                Kill(sq); Kill(hv);
            }
            yield return new WaitForSeconds(1f);

            // ---- 4. V formation on open ground (west of the fort, heading north)
            var v = Squad("grunt", Team.Red, fx - 12, fy + 6);
            W.SendWaypoints(Team.Red, v, new List<ZWaypoint> { ZWaypoint.Move((fx - 12) * 16 + 8, (fy - 10) * 16 + 8) });
            Look(v, 26f);
            int behind = 0, left = 0, right = 0, samples = 0, overlaps = 0; float minGap = 999f;
            int px = v.CenterX, py = v.CenterY; float t4 = Time.time; bool shot = false;
            while (Time.time - t4 < 14f)
            {
                yield return new WaitForSeconds(0.5f);
                float hx = v.CenterX - px, hy = v.CenterY - py; px = v.CenterX; py = v.CenterY;
                float hl = Mathf.Sqrt(hx * hx + hy * hy); if (hl < 3f || v.Minions.Count == 0) continue;
                hx /= hl; hy /= hl; samples++;
                foreach (var m in v.Minions)
                {
                    float dx = m.CenterX - v.CenterX, dy = m.CenterY - v.CenterY;
                    float along = dx * hx + dy * hy, side = dx * -hy + dy * hx;
                    if (along < -2f) behind++;
                    if (side < -3f) left++; else if (side > 3f) right++;
                }
                var mem = new List<ZObject> { v }; mem.AddRange(v.Minions);
                for (int i = 0; i < mem.Count; i++) for (int j = i + 1; j < mem.Count; j++)
                {
                    float d = Mathf.Sqrt(Mathf.Pow(mem[i].CenterX - mem[j].CenterX, 2) + Mathf.Pow(mem[i].CenterY - mem[j].CenterY, 2));
                    minGap = Mathf.Min(minGap, d); if (d < 6f) overlaps++;
                }
                if (!shot && samples == 6) { Shot("04_v_formation"); shot = true; }
            }
            int per = samples * Mathf.Max(1, v.Minions.Count);
            Row("V formation: minions behind the leader on both sides, no overlap", samples > 0 && behind >= per * 0.8f && left > 0 && right > 0 && overlaps == 0,
                $"samples {samples}, minion-samples {per}: behind {behind}, left {left}, right {right}; min gap {minGap:F1} px, overlaps {overlaps}");
            Kill(v);

            sb.AppendLine($"\n**{pass}/{total} PASS**");
            System.IO.File.WriteAllText(Records + "/sight_check.md", sb.ToString());
            Report = $"{pass}/{total}";
            Destroy(gameObject);
        }
    }
}
