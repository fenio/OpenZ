using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Version 1.1 kit K331d checks (menu OpenZ/V1.1/K331d ...), items 331 + 332 for the guns and the buildings: every big falling piece
    /// and the statics (guns, buildings, huts, level 16 ruins). Each check picks the static nearest the view's middle, makes the death
    /// right beside it (on the camera's side), waits until everything rests (at most 34 s) and writes its report (console +
    /// distance (at least 45 m): the break, +1.5 s, at rest.
    /// Wanted (PASS lines): big bodies lying in a gun or a building at rest (a point 5 cm inside their own collider shape inside a static's
    /// drawn shape) 0; body middles inside one 0; tossed turrets lying in one 0; units drawn standing in a resting piece 0.
    /// Test tools only: nothing is saved.
    /// </summary>
    public static class OzK331dStaticCheck
    {
        const string M = "OpenZ/V1.1/K331d ";
        static string Out { get { var p = Path.Combine(OpenZPaths.Records, "milestones", "k331d_statics_check"); Directory.CreateDirectory(p); return p; } }
        static string name = "-", lead = "";
        static float until, shotAt = -1f, restAt = -1f, startAt;
        static int step, force0, staticId;
        static Vector3 at; static Bounds sbox; static string swhat = "-";
        static float camDist = 55f, camPitch = 55f, camYaw;
        static readonly List<ZObject> made = new List<ZObject>();
        static int gave0, gone0, woken0, disc0, ball0, turretMoved0, turretKept0;
        static System.Func<bool> doneWhen; static System.Action after;
        static int unitsInPieceMax; static float nextOverlap;
        static readonly HashSet<string> overlapNow = new HashSet<string>(), overlapPrev = new HashSet<string>();

        // ------------------------------------------------------------------ helpers
        static bool Ready(out ZGame g)
        {
            g = ZGame.I;
            if (!Application.isPlaying || !g || g.World == null) { Debug.LogWarning("[OpenZ K331d] enter Play first (a level running), the camera on guns or buildings"); return false; }
            return true;
        }

        static Vector3 ViewMiddle(ZGame g)
        {
            var cam = Camera.main;
            if (cam) { camPitch = cam.transform.eulerAngles.x; camYaw = cam.transform.eulerAngles.y; }
            var c = cam && g.GroundPoint(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out var gp) ? gp : Vector3.zero;
            if (cam) camDist = Mathf.Clamp(Vector3.Distance(cam.transform.position, c), 45f, 80f);              // the RTS distance: high and wide
            return c;
        }

        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        static bool Pick(ZGame g, bool guns, int skip = 0)
        {
            var c = ViewMiddle(g);
            if (!OzStaticSolid.Nearest(c, guns, out sbox, out staticId, out swhat, skip)) { Debug.LogWarning("[OpenZ K331d] no living " + (guns ? "gun" : "building") + " in this level"); return false; }
            return true;
        }

        /// <summary>A free tile (vehicles: `robot` false) 2.5-8 m from the static's side, on the side the camera looks from first.</summary>
        static bool SpotBeside(ZGame g, Bounds b, bool robot, out int px, out int py)
        {
            px = py = 0; var W = g.World;
            var cam = Camera.main; var toCam = cam ? cam.transform.position - b.center : Vector3.back; toCam.y = 0f; toCam.Normalize();
            for (float d = 2.5f; d <= 8f; d += 0.5f)
                for (int s = 0; s < 16; s++)
                {
                    int turn = (s + 1) / 2 * ((s & 1) == 0 ? 1 : -1);
                    var dir = Quaternion.Euler(0f, turn * 22.5f, 0f) * toCam;
                    float side = Mathf.Abs(dir.x) * b.extents.x + Mathf.Abs(dir.z) * b.extents.z;
                    var p = b.center + dir * (side + d);
                    var q = ZGame.WorldToPx(p); int tx = q.x >> 4, ty = q.y >> 4;
                    bool ok = true;
                    for (int j = -1; j <= 1 && ok; j++) for (int i = -1; i <= 1 && ok; i++) ok = W.Path.TilePassable(tx + i, ty + j, robot);
                    if (!ok) continue;
                    if (W.Objects.Any(o => o != null && !o.Destroyed && o.Kind != ZKind.Robot && o.Contains(tx * 16 + 8, ty * 16 + 8))) continue;
                    px = tx * 16; py = ty * 16; return true;
                }
            return false;
        }

        static Team Enemy(ZGame g) { var me = g.World.Objects.FirstOrDefault(o => o != null && o.IsFort && o.Owner != Team.Neutral); return me != null && me.Owner == Team.Red ? Team.Blue : Team.Red; }
        static Team Mine(ZGame g) => Enemy(g) == Team.Red ? Team.Blue : Team.Red;

        static int Gave() { int s = 0; foreach (var n in OzPieceGiveWay.Gave) s += n; return s; }

        static void Snapshot()
        {
            overlapPrev.Clear(); nextOverlap = 0f;
            gave0 = Gave(); gone0 = OzStaticSolid.GoneN; woken0 = OzStaticSolid.Woken; disc0 = OzStaticSolid.DiscHits; ball0 = OzStaticSolid.BallHits;
            turretMoved0 = OzRockSolid.TurretMoved; turretKept0 = OzRockSolid.TurretKept; unitsInPieceMax = 0;
        }

        static void Shot(string tag)
        {
            try { var path = OpenZShot.Shot(at, camDist, camPitch, camYaw, Path.Combine(Out, "k331d_" + name + "_" + tag + ".png"), 1920, 1080); Debug.Log("[OpenZ K331d] picture " + path); }
            catch (System.Exception e) { Debug.LogWarning("[OpenZ K331d] picture " + tag + " failed: " + e.Message); }
        }

        static void Begin(string check, string what, float seconds, System.Func<bool> done, System.Action then = null)
        {
            name = check; lead = what; doneWhen = done; after = then; step = 0; shotAt = -1f; restAt = -1f; startAt = Time.time;
            until = Time.realtimeSinceStartup + seconds;
            Debug.Log("[OpenZ K331d] " + check + ": " + what);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
        }

        static bool AllRested(float radius)
        {
            foreach (var b in OzRockBody.All) if (b && Flat(b.transform.position, at) < radius && !b.Rested && b.gameObject.activeInHierarchy) return false;
            return Time.time - startAt > 3f;
        }

        static void Tick()
        {
            if (!Application.isPlaying) { EditorApplication.update -= Tick; return; }
            if (Time.time >= nextOverlap)
            {   // a unit drawn in a resting piece that is still there 0.35 s later (a piece just come to rest gives way at its next look)
                nextOverlap = Time.time + 0.35f;
                UnitsInPieces(out _, overlapNow);
                int persist = 0; foreach (var k in overlapNow) if (overlapPrev.Contains(k)) persist++;
                unitsInPieceMax = Mathf.Max(unitsInPieceMax, persist);
                overlapPrev.Clear(); foreach (var k in overlapNow) overlapPrev.Add(k);
            }
            if (step == 0) { Shot("1_break"); shotAt = Time.time + 1.5f; step = 1; }
            if (step == 1 && Time.time >= shotAt) { Shot("2_plus1_5s"); step = 2; }
            bool done = step >= 2 && (doneWhen == null || doneWhen());
            if (done && restAt < 0f) restAt = Time.time + 0.8f;
            if ((restAt >= 0f && Time.time >= restAt) || Time.realtimeSinceStartup > until)
            {
                EditorApplication.update -= Tick;
                Shot("3_rest");
                Report();
                after?.Invoke(); after = null;
            }
        }

        static void KillLater(ZObject o, float delay)
        {
            float t0 = Time.realtimeSinceStartup;
            void Kill()
            {
                if (!Application.isPlaying) { EditorApplication.update -= Kill; return; }
                if (Time.realtimeSinceStartup - t0 < delay) return;
                EditorApplication.update -= Kill;
                var g = ZGame.I; if (!g || o == null || o.Destroyed) return;
                o.DamageHealth(o.Health); g.World.UpdateObjectHealth(o);
                startAt = Time.time; step = 0;
            }
            EditorApplication.update += Kill;
        }

        // ------------------------------------------------------------------ the checks
        [MenuItem(M + "Kill A Tank Beside The Nearest Building (Play)")]
        static void Tank() => Vehicle("tank", "medium", 1, false);

        [MenuItem(M + "Kill A Tank Beside The Nearest Gun (Play)")]
        static void TankGun() => Vehicle("tank_gun", "medium", 1, true);

        [MenuItem(M + "Kill A Jeep Beside The Nearest Building: It Flips (Play)")]
        static void Jeep() => Vehicle("jeep", "jeep", 1, false);

        static void Vehicle(string check, string type, int force, bool guns)
        {
            if (!Ready(out var g) || !Pick(g, guns)) return;
            if (!SpotBeside(g, sbox, false, out int px, out int py)) { Debug.LogWarning("[OpenZ K331d] no free spot beside " + swhat); return; }
            var W = g.World; var o = W.CreateObject("vehicle", type, px, py, Enemy(g));
            if (o == null) { Debug.LogWarning("[OpenZ K331d] could not make a " + type); return; }
            g.SpawnUnitView(o); made.Add(o);
            at = ZGame.PxToWorld(o.CenterX, o.CenterY); at.y = g.GroundY(at.x, at.z);
            Snapshot(); force0 = WreckFx.Force; WreckFx.Force = force;
            EditorApplication.delayCall += () => KillLater(o, 1.0f);
            Begin(check, $"a {type} killed {GapTo(at):0.0} m from the side of {swhat}; outcomes forced (break-up / flip)", 34f,
                  () => Time.time - startAt > 9f && AllRested(40f), () => WreckFx.Force = force0);
        }

        static float GapTo(Vector3 p)
        {
            float dx = Mathf.Max(0f, Mathf.Max(sbox.min.x - p.x, p.x - sbox.max.x)), dz = Mathf.Max(0f, Mathf.Max(sbox.min.z - p.z, p.z - sbox.max.z));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        [MenuItem(M + "Blast A Squad Beside The Nearest Building Into It (Play)")]
        static void Squad()
        {
            if (!Ready(out var g) || !Pick(g, false)) return;
            if (!SpotBeside(g, sbox, true, out int px, out int py)) { Debug.LogWarning("[OpenZ K331d] no free spot beside " + swhat); return; }
            var W = g.World; var sq = W.CreateRobotGroup("grunt", px, py, Enemy(g));
            if (sq == null) { Debug.LogWarning("[OpenZ K331d] could not make a squad"); return; }
            var all = new List<ZObject> { sq }; all.AddRange(sq.Minions);
            foreach (var o in all) g.SpawnUnitView(o);
            made.AddRange(all);
            at = ZGame.PxToWorld(sq.CenterX, sq.CenterY); at.y = g.GroundY(at.x, at.z);
            var blast = at + (at - sbox.center).normalized * 2.5f; blast.y = at.y;                                // on the far side: they fly at the building
            Snapshot();
            EditorApplication.delayCall += () =>
            {
                float t0 = Time.realtimeSinceStartup;
                void Throw()
                {
                    if (!Application.isPlaying) { EditorApplication.update -= Throw; return; }
                    if (Time.realtimeSinceStartup - t0 < 1.0f) return;
                    EditorApplication.update -= Throw;
                    foreach (var o in all) if (g.Views.TryGetValue(o.Id, out var v) && v) v.Die(false, true, blast, -1, 1.5f);   // view only: the sim's robots are taken out below
                    startAt = Time.time; step = 0;
                }
                EditorApplication.update += Throw;
            };
            Begin("squad", $"{all.Count} robots {GapTo(at):0.0} m from the side of {swhat} thrown by a blast on their far side (view only)", 30f,
                  () => Time.time - startAt > 7f && AllRested(40f), () => { foreach (var o in all) if (o != null) o.KillAt = 0; });
        }

        [MenuItem(M + "Destroy The Building Nearest Another Building Or Gun (Play)")]
        static void Building()
        {
            if (!Ready(out var g)) return;
            ViewMiddle(g);
            ZObject best = null; float bd = float.MaxValue; Bounds nb = default; string nwhat = "-";
            foreach (var o in g.World.Objects)
            {
                if (o == null || o.Destroyed || o.Kind != ZKind.Building || o.IsFort || o.IsBridge) continue;
                if (!g.StaticViews.TryGetValue(o.Id, out var go) || !go || !go.GetComponent<BuildingBreaker>()) continue;
                var c = g.CenterWorld(o, true); float half = Mathf.Max(o.WidthPix, o.HeightPix) / 16f;
                for (int k = 0; k < 2; k++)
                    if (OzStaticSolid.Nearest(c, k == 0, out var b2, out int id2, out var w2, o.Id))
                    {
                        float dx = Mathf.Max(0f, Mathf.Max(b2.min.x - c.x, c.x - b2.max.x)), dz = Mathf.Max(0f, Mathf.Max(b2.min.z - c.z, c.z - b2.max.z));
                        float d = Mathf.Sqrt(dx * dx + dz * dz) - half;
                        if (d < bd) { bd = d; best = o; nb = b2; nwhat = w2; }
                    }
            }
            if (best == null) { Debug.LogWarning("[OpenZ K331d] no building (not a fort) beside another static in this level"); return; }
            at = g.CenterWorld(best, true); sbox = nb; swhat = nwhat;
            Snapshot();
            best.DamageHealth(best.Health); g.World.UpdateObjectHealth(best);
            Begin("building", $"{best.Type} #{best.Id} destroyed, {nwhat} {bd:0.0} m from its side", 34f, () => Time.time - startAt > 10f && AllRested(45f));
        }

        [MenuItem(M + "Destroy The Gun Nearest A Building (Play)")]
        static void Gun()
        {
            if (!Ready(out var g)) return;
            var c = ViewMiddle(g);
            ZObject best = null; float bd = float.MaxValue; Bounds nb = default; string nwhat = "-";
            foreach (var o in g.World.Objects)
            {
                if (o == null || o.Destroyed || o.Kind != ZKind.Cannon || !g.Views.ContainsKey(o.Id)) continue;
                var gc = g.CenterWorld(o, true);
                if (!OzStaticSolid.Nearest(gc, false, out var b2, out _, out var w2, o.Id)) continue;
                float dx = Mathf.Max(0f, Mathf.Max(b2.min.x - gc.x, gc.x - b2.max.x)), dz = Mathf.Max(0f, Mathf.Max(b2.min.z - gc.z, gc.z - b2.max.z));
                float d = Mathf.Sqrt(dx * dx + dz * dz) + 0.05f * Flat(gc, c);                                     // the nearest pair, the one in view first
                if (d < bd) { bd = d; best = o; nb = b2; nwhat = w2; }
            }
            if (best == null) { Debug.LogWarning("[OpenZ K331d] no gun beside a building in this level"); return; }
            at = g.CenterWorld(best, true); sbox = nb; swhat = nwhat;
            Snapshot(); force0 = WreckFx.Force; WreckFx.Force = 1;
            best.DamageHealth(best.Health); g.World.UpdateObjectHealth(best);
            Begin("gun", $"{best.Type} #{best.Id} destroyed beside {nwhat} (its base pieces and its turret toss)", 34f, () => Time.time - startAt > 10f && AllRested(45f), () => WreckFx.Force = force0);
        }

        [MenuItem(M + "Shuttle: Fly It Over The Nearest Building And Burst It There (Play, level 16)")]
        static void ShuttleAir() => Shuttle(false);

        [MenuItem(M + "Shuttle: Fly It Over The Nearest Building And Shoot It Down There By Rifle Fire (Play, level 16)")]
        static void ShuttleRifle() => Shuttle(true);

        static void Shuttle(bool rifle)
        {
            if (!Ready(out var g)) return;
            var ps = OzPlayableShuttle.I; var s = ps ? ps.Obj : null;
            if (!ps || s == null || s.Destroyed || ps.BrokenUp) { Debug.LogWarning("[OpenZ K331d] no playable shuttle here (level 16)"); return; }
            ViewMiddle(g);
            if (!OzStaticSolid.Nearest(ps.transform.position, false, out sbox, out staticId, out swhat)) { Debug.LogWarning("[OpenZ K331d] no living building in this level"); return; }
            var tp = sbox.center; var q = ZGame.WorldToPx(tp);
            var me = s.Owner; var W = g.World;
            W.SendWaypoints(me, s, new[] { new ZWaypoint { mode = WpMode.Move, x = q.x, y = q.y, playerGiven = true } });
            int hp0 = s.MaxHealth; s.MaxHealth = 1000000; s.SetHealth(s.MaxHealth);
            at = tp; Snapshot();
            float t0 = Time.realtimeSinceStartup; bool shot = false;
            void Fly()
            {
                if (!Application.isPlaying || s == null) { EditorApplication.update -= Fly; return; }
                bool there = Flat(ps.transform.position, tp) < 5f && ps.FeetHeight > 5f;
                if (!there && Time.realtimeSinceStartup - t0 < 70f) return;
                if (!shot)
                {
                    shot = true; s.MaxHealth = hp0; s.SetHealth(rifle ? 1 : s.Health);
                    at = ps.transform.position; at.y = g.GroundY(at.x, at.z);
                    if (rifle)
                    {
                        var sq = SpotBeside(g, sbox, true, out int px, out int py) ? W.CreateRobotGroup("grunt", px, py, me == Team.Red ? Team.Blue : Team.Red) : null;
                        if (sq != null) { var list = new List<ZObject> { sq }; list.AddRange(sq.Minions); foreach (var o in list) g.SpawnUnitView(o); made.AddRange(list); W.SendWaypoints(sq.Owner, sq, new[] { new ZWaypoint { mode = WpMode.Attack, refId = s.Id, x = s.CenterX, y = s.CenterY, attackTo = true } }); }
                        else { s.DamageHealth(s.Health); W.UpdateObjectHealth(s); }
                    }
                    else { s.DamageHealth(s.Health); W.UpdateObjectHealth(s); }
                }
                if (!ps.BrokenUp && Time.realtimeSinceStartup - t0 < 120f) return;
                EditorApplication.update -= Fly;
                startAt = Time.time; step = 0;
                Begin(rifle ? "shuttle_rifle" : "shuttle_air", $"the ship over {swhat}, {(rifle ? "rifle fire" : "destroyed in the air: a burst")}; break: {OzShuttleBreakup.LastHow}", 34f,
                      () => !OzShuttleBreakup.Falling && OzShuttleBreakup.LastAllRested() && AllRested(50f),
                      () => { foreach (var o in made) if (o != null && o.Kind == ZKind.Robot) o.KillAt = 0; });
            }
            Debug.Log($"[OpenZ K331d] the shuttle flies over {swhat}; it is destroyed there");
            EditorApplication.update -= Fly; EditorApplication.update += Fly;
        }

        [MenuItem(M + "Pieces Give Way: Drive A Tank And Walk A Squad Through Resting Pieces (Play, after a check)")]
        static void GiveWay()
        {
            if (!Ready(out var g)) return;
            var rest = OzRockBody.All.Where(b => b && b.Rested && b.kind != OzRockBody.Kind.RobotBody && b.gameObject.activeInHierarchy && !b.GetComponent<OzGiveWaySettle>() && !b.GetComponent<OzGiveWayHullFade>()).ToList();
            if (rest.Count == 0) { Debug.LogWarning("[OpenZ K331d] no resting big piece: run a check first"); return; }
            ViewMiddle(g);
            var c = rest.OrderBy(b => Flat(b.transform.position, at)).First().transform.position;
            var mid = Vector3.zero; int n = 0; foreach (var b in rest) if (Flat(b.transform.position, c) < 14f) { mid += b.transform.position; n++; }
            mid /= Mathf.Max(1, n); at = mid; at.y = g.GroundY(at.x, at.z);
            var W = g.World; var side = Mine(g);
            var from = at + new Vector3(-16f, 0f, -6f); var to = at + new Vector3(16f, 0f, 6f);
            var pf = ZGame.WorldToPx(from); var pt = ZGame.WorldToPx(to);
            var tank = W.CreateObject("vehicle", "medium", pf.x & ~15, pf.y & ~15, side);
            var sq = W.CreateRobotGroup("grunt", (pf.x & ~15) + 32, (pf.y & ~15) + 48, side);
            var list = new List<ZObject>(); if (tank != null) list.Add(tank); if (sq != null) { list.Add(sq); list.AddRange(sq.Minions); }
            foreach (var o in list) g.SpawnUnitView(o);
            made.AddRange(list);
            if (tank != null) W.SendWaypoints(side, tank, new[] { new ZWaypoint { mode = WpMode.ForceMove, x = pt.x, y = pt.y, playerGiven = true } });
            if (sq != null) W.SendWaypoints(side, sq, new[] { new ZWaypoint { mode = WpMode.ForceMove, x = pt.x, y = pt.y + 32, playerGiven = true } });
            Snapshot();
            Begin("giveway", $"a tank and a squad sent across {n} resting pieces", 30f, () => Time.time - startAt > 18f);
        }

        [MenuItem(M + "A Static Under A Resting Piece Is Destroyed: The Piece Falls (Play, after a check)")]
        static void SupportGone()
        {
            if (!Ready(out var g)) return;
            OzRockBody on = null; ZObject victim = null;
            foreach (var b in OzRockBody.All)
            {
                if (!b || !b.Rested || b.TouchCount == 0) continue;
                var p = b.transform.position;
                foreach (var o in g.World.Objects)
                {
                    if (o == null || o.Destroyed || (o.Kind != ZKind.Building && o.Kind != ZKind.Cannon) || o.IsFort || o.IsBridge) continue;
                    var cw = g.CenterWorld(o, true); float half = Mathf.Max(o.WidthPix, o.HeightPix) / 16f + 2f;
                    if (Flat(cw, p) < half) { on = b; victim = o; break; }
                }
                if (on) break;
            }
            if (!on) { Debug.LogWarning("[OpenZ K331d] no big piece resting on or against a gun or a building: run a check first"); return; }
            at = on.transform.position; ViewMiddle(g);
            Snapshot();
            Shot("0_before");
            victim.DamageHealth(victim.Health); g.World.UpdateObjectHealth(victim);
            Begin("support", $"{victim.Type} #{victim.Id} under / beside a resting {OzRockBody.KindNames[(int)on.kind]} destroyed", 24f, () => Time.time - startAt > 6f && AllRested(30f));
        }

        [MenuItem(M + "State")]
        static void State() => Debug.Log("[OpenZ K331d] " + OzStaticSolid.State + " | give way: " + GiveWayLine() + " | " + OzRockSolid.LandingState);

        static string GiveWayLine()
        {
            var sb = new StringBuilder();
            sb.Append($"pieces that gave way {Gave()} (to robots {OzPieceGiveWay.ToRobot}, to vehicles {OzPieceGiveWay.ToVehicle}; ");
            for (int k = 0; k < OzRockBody.Kinds; k++) if (OzPieceGiveWay.Gave[k] > 0) sb.Append(OzRockBody.KindNames[k]).Append(' ').Append(OzPieceGiveWay.Gave[k]).Append(", ");
            sb.Append("looks ").Append(OzPieceGiveWay.Looks).Append(')');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ measures
        /// <summary>Units (live robots and vehicles) whose disc overlaps a resting big piece still drawn (not settling).</summary>
        static int UnitsInPieces(out string which, HashSet<string> keys = null)
        {
            which = "-"; keys?.Clear(); var g = ZGame.I; if (!g) return 0;
            int n = 0;
            foreach (var b in OzRockBody.All)
            {
                if (!b || !b.Rested || b.kind == OzRockBody.Kind.RobotBody || !b.gameObject.activeInHierarchy || b.GetComponent<OzGiveWaySettle>() || b.GetComponent<OzGiveWayHullFade>() || PopOut.IsPopping(b.transform)) continue;
                var bb = Box(b.gameObject); if (bb.size.sqrMagnitude < 0.01f) continue;
                foreach (var kv in g.Views)
                {
                    var v = kv.Value; if (!v || !v.gameObject.activeInHierarchy || v.Obj == null || v.Obj.Destroyed || (v.Obj.Kind != ZKind.Robot && v.Obj.Kind != ZKind.Vehicle)) continue;
                    var p = v.transform.position; if (p.y > bb.max.y + 1.5f || p.y < bb.min.y - 2.5f) continue;
                    float r = v.Obj.Kind == ZKind.Robot ? 0.35f : Mathf.Max(1f, Mathf.Max(v.Obj.WidthPix, v.Obj.HeightPix) / 8f * 0.35f);
                    float dx = Mathf.Max(0f, Mathf.Max(bb.min.x - p.x, p.x - bb.max.x)), dz = Mathf.Max(0f, Mathf.Max(bb.min.z - p.z, p.z - bb.max.z));
                    if (dx * dx + dz * dz < r * r) { n++; which = $"{v.Obj.Type} #{v.Obj.Id} in {OzRockBody.KindNames[(int)b.kind]} {b.name}"; keys?.Add(v.Obj.Id + "/" + b.GetInstanceID()); }
                }
            }
            return n;
        }

        static Bounds Box(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(); var b = new Bounds(go.transform.position, Vector3.zero); bool any = false;
            foreach (var r in rs) { if (!r || !r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue; if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; } }
            return b;
        }

        static readonly List<Vector3> pts = new List<Vector3>(32);
        /// <summary>Points 5 cm inside the body's own collider shapes (27 per shape, its corners, edges, faces and middle).</summary>
        static List<Vector3> Samples(GameObject go)
        {
            pts.Clear();
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
            {
                if (!c || c.isTrigger || OzRockSolid.IsRock(c)) continue;
                var t = c.transform; Vector3 ctr, half;
                if (c is BoxCollider bc) { ctr = bc.center; half = bc.size * 0.5f; }
                else if (c is MeshCollider mc && mc.sharedMesh) { ctr = mc.sharedMesh.bounds.center; half = mc.sharedMesh.bounds.extents * 0.8f; }
                else if (c is CapsuleCollider cc) { ctr = cc.center; float r = cc.radius * 0.7f; half = new Vector3(r, r, r); half[cc.direction] = Mathf.Max(r, cc.height * 0.5f - cc.radius * 0.3f); }
                else if (c is SphereCollider sc) { ctr = sc.center; half = Vector3.one * sc.radius * 0.7f; }
                else continue;
                for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                {
                    var w = t.TransformPoint(ctr + new Vector3(x * half.x, y * half.y, z * half.z)); var toC = t.TransformPoint(ctr) - w; float m = toC.magnitude;
                    pts.Add(m > 0.05f ? w + toC / m * 0.05f : w);
                }
            }
            return pts;
        }

        // ------------------------------------------------------------------ the report
        static void Report()
        {
            var g = ZGame.I; var sb = new StringBuilder();
            sb.AppendLine("[OpenZ K331d] " + name + ": " + lead);
            OzStaticSolid.ArmAround(at, 60f, 3f, 60f);                                                       // the statics round it on for the measuring
            Physics.SyncTransforms();
            int big = 0, inStatic = 0, mids = 0; string worst = "-";
            foreach (var b in OzRockBody.All)
            {
                if (!b || !b.gameObject.activeInHierarchy || Flat(b.transform.position, at) > 60f || b.GetComponent<OzGiveWaySettle>() || b.GetComponent<OzGiveWayHullFade>()) continue;
                big++;
                bool deep = false; string which = "-";
                foreach (var p in Samples(b.gameObject)) if (OzStaticSolid.InsideStatic(p, out which)) { deep = true; break; }
                var bb = Box(b.gameObject);
                bool mid = OzStaticSolid.InsideStatic(bb.center, out var wm);
                if (deep || mid) { inStatic++; worst = $"{OzRockBody.KindNames[(int)b.kind]} {b.name} in {(deep ? which : wm)}"; }
                if (mid) mids++;
                sb.AppendLine($"      {OzRockBody.KindNames[(int)b.kind],-15} {b.name,-28} {b.Size:0.0} m, {(b.Rested ? "rests" : "MOVING")}{(b.OnRock ? " on a rock / static" : b.TouchCount > 0 ? " against a rock / static" : "")}{(deep || mid ? "  IN A STATIC" : "")}");
            }
            int scripted = 0;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {   // the tossed turrets lying near (scripted, no body)
                if (!t || t.parent || !t.GetComponentInChildren<Renderer>() || Flat(t.position, at) > 60f || t.GetComponent<OzRockBody>() || t.GetComponentInChildren<UnitView>() || !t.name.ToLowerInvariant().Contains("turret")) continue;
                var bb = Box(t.gameObject); bool inside = false;
                for (int i = 0; i < 9 && !inside; i++)
                {
                    var p = bb.center + Vector3.Scale(bb.extents * 0.8f, new Vector3(i % 3 - 1, 0f, i / 3 - 1)); p.y = bb.center.y;
                    inside = OzStaticSolid.InsideStatic(p, out _);
                }
                if (inside) { scripted++; worst = "turret " + t.name; }
            }
            UnitsInPieces(out var uw, overlapNow);
            int now = 0; foreach (var k in overlapNow) if (overlapPrev.Contains(k)) now++;
            sb.AppendLine(OzV11Kit.Verdict(big > 0 || name == "giveway", $"big bodies near it: {big}"));
            sb.AppendLine(OzV11Kit.Verdict(inStatic == 0, $"big bodies lying in a gun or a building (a point 5 cm inside their own shape inside a static's drawn shape): {inStatic}{(inStatic > 0 ? " (" + worst + ")" : "")}"));
            sb.AppendLine(OzV11Kit.Verdict(mids == 0, $"body middles inside a gun or a building: {mids}"));
            sb.AppendLine(OzV11Kit.Verdict(scripted == 0, $"tossed turrets lying in a gun or a building: {scripted}"));
            sb.AppendLine(OzV11Kit.Verdict(unitsInPieceMax == 0 && now == 0, $"units drawn standing in a resting piece (at any frame of the check): {unitsInPieceMax}{(now > 0 ? " (now: " + uw + ")" : "")}"));
            if (name == "giveway") sb.AppendLine(OzV11Kit.Verdict(Gave() > gave0, $"pieces that gave way to the tank and the squad: +{Gave() - gave0}"));
            if (name == "support") sb.AppendLine(OzV11Kit.Verdict(OzStaticSolid.Woken > woken0, $"the piece on / against the static woke when it was destroyed: +{OzStaticSolid.Woken - woken0} (statics gone +{OzStaticSolid.GoneN - gone0})"));
            if (name.StartsWith("tank") || name == "gun") sb.AppendLine(OzV11Kit.Verdict(OzRockSolid.TurretKept == turretKept0, $"the turret's landing: {OzRockSolid.LastTurret} (moved +{OzRockSolid.TurretMoved - turretMoved0}, kept +{OzRockSolid.TurretKept - turretKept0}; scripted tests that met a static: discs +{OzStaticSolid.DiscHits - disc0}, flights +{OzStaticSolid.BallHits - ball0})"));
            sb.AppendLine("      " + OzStaticSolid.State);
            sb.AppendLine("      give way: " + GiveWayLine());
            sb.AppendLine("      " + OzRockBody.State);
            sb.AppendLine($"      pictures: {Out} (k331d_{name}_*.png)");
            var text = sb.ToString();
            Debug.Log(text);
            try { File.WriteAllText(Path.Combine(Out, "k331d_" + name + "_report.txt"), text); } catch (System.Exception) { }
        }
    }
}
