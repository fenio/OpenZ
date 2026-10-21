using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Version 1.1 item 202 checks (menu OpenZ/V1.1/KC5b ..., Play): vehicles and guns explode on the frame the shot that kills them is
    /// drawn hitting them (VehicleDeathSync, MlrsSalvo.Marked.cs).
    /// <list type="bullet">
    /// <item>BEFORE / AFTER: the two rows that set VehicleDeathSync.Enabled and MlrsSalvo.MarkVehicles together.</item>
    /// <item>MLRS salvo on light tanks: a MLRS of yours 15 m left of the view centre, four enemy light tanks round the view centre (the
    /// target, and three at 5.5 / 7 / 8.5 m from it, 400 HP each so the shot's splash kills all four), the MLRS ordered onto the target.
    /// The 5.5 m tank is ordered to drive about 8 m across the splash at the same time, so it is moving during the rockets' flight (the
    /// item's case: a light tank driving off its rocket's point). When the shot is in the air, two enemy grunts are put 1-2 m beside the
    /// 8.5 m tank, where the rocket marked for that tank is the nearest landing (outside rocket 1's 5 m core, inside its splash).
    /// When all are dead (12 s at most): one line per tank (the frame the sim killed it, the frame it exploded, the frame a drawn rocket
    /// hit it); per marked rocket whether its drawn body was steered onto its tank every frame and how far the body's last drawn point
    /// is from its blast; how far the moving tank's rocket followed it; per grunt the frame it was thrown against the marked rocket's.</item>
    /// <item>Vehicle death report: the last 20 vehicle / gun deaths of the session (a real fight): why, wait, and for a round kill the
    /// frame its drawn round arrived (noted by the drawing code) against the frame it exploded; a round kill whose drawn round was not
    /// noted fails.</item>
    /// </list>
    /// Test tools only: the units go through the sim's own creation and orders; nothing is saved.
    /// </summary>
    public static class OzKC5bCheck
    {
        const string M = "OpenZ/V1.1/KC5b ";
        /// <summary>The drawn body's last point (or that point one frame on along its last step) must be this close to the blast (m).</summary>
        const float BodyOnBlastM = 0.5f;
        /// <summary>The moving tank's rocket must have followed it at least this far (m) for the steering to be measured.</summary>
        const float FollowMinM = 1f;

        [MenuItem(M + "Vehicle Deaths: BEFORE (sim frame, plain ripple, Play)")]
        static void Before() { VehicleDeathSync.Enabled = false; MlrsSalvo.MarkVehicles = false; Debug.Log("[OpenZ KC5b] BEFORE: vehicle deaths on the sim's frame, the MLRS ripple as in 1.0 (no rocket follows a vehicle)"); }

        [MenuItem(M + "Vehicle Deaths: AFTER (on the drawn hit, Play)")]
        static void After() { VehicleDeathSync.Enabled = true; MlrsSalvo.MarkVehicles = true; Debug.Log("[OpenZ KC5b] AFTER: a vehicle explodes on the frame its killing round / MLRS rocket is drawn hitting it"); }

        // ------------------------------------------------------------------ MLRS salvo on light tanks
        static readonly List<ZObject> tanks = new List<ZObject>();
        static readonly List<ZObject> grunts = new List<ZObject>();
        static readonly List<(int frame, Vector3 at)> rocket1 = new List<(int, Vector3)>();
        /// <summary>Drawn projectile serial -> its last drawn point and its last frame's step (OpenZFxPlayer.ProjTrace).</summary>
        static readonly Dictionary<int, (Vector3 last, Vector3 step)> bodies = new Dictionary<int, (Vector3, Vector3)>();
        static ZObject mlrs, mover, far; static Team enemyTeam; static float until; static int frame0; static bool gruntsTried; static string gruntNote = "", moveNote = "";

        [MenuItem(M + "MLRS Salvo On Light Tanks (Play)")]
        static void Salvo()
        {
            var g = ZGame.I;
            if (!Application.isPlaying || !g || g.World == null) { Debug.LogWarning("[OpenZ KC5b] enter Play first, the camera on open ground"); return; }
            var w = g.World;
            var cam = Camera.main; var c = cam && g.GroundPoint(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out var gp) ? gp : g.CenterWorld(w.Objects.First(o => o.IsUnit));
            var cp = ZGame.WorldToPx(c);
            Team enemy = g.player == Team.Red ? Team.Blue : Team.Red; enemyTeam = enemy;
            ZObject Make(string type, Team team, int px, int py)
            {
                for (int r = 0; r < 64; r += 8)                                                           // the nearest open 2 x 2 tiles
                    for (int k = 0; k < 8; k++)
                    {
                        int x = px + (int)(Mathf.Cos(k * 0.785f) * r) - 16, y = py + (int)(Mathf.Sin(k * 0.785f) * r) - 16;
                        if (!Open(w, x >> 4, y >> 4)) continue;
                        var o = w.CreateObject("vehicle", type, x, y, team);
                        g.SpawnUnitView(o);
                        return o;
                    }
                return null;
            }
            tanks.Clear(); rocket1.Clear(); grunts.Clear(); bodies.Clear(); MlrsSalvo.WatchClear();
            mover = null; far = null; gruntsTried = false; gruntNote = ""; moveNote = "";
            mlrs = Make("missile_launcher", g.player, cp.x - 120, cp.y);
            var target = Make("light", enemy, cp.x, cp.y);
            if (mlrs == null || target == null) { Debug.LogWarning("[OpenZ KC5b] no open ground at the view centre"); return; }
            tanks.Add(target);
            ZObject near = null;
            foreach (var (ang, dm) in new[] { (60f, 5.5f), (170f, 7f), (290f, 8.5f) })
            {
                var t = Make("light", enemy, target.CenterX + (int)(Mathf.Cos(ang * Mathf.Deg2Rad) * dm * 8f), target.CenterY + (int)(Mathf.Sin(ang * Mathf.Deg2Rad) * dm * 8f));
                if (t == null) continue;
                tanks.Add(t);
                if (dm < 6f) near = t; else if (dm > 8f) far = t;
            }
            foreach (var t in tanks) { t.Damage = 0; t.SetHealth(400); }
            w.SendWaypoints(g.player, mlrs, new[] { new ZWaypoint { mode = WpMode.Attack, refId = target.Id, x = target.CenterX, y = target.CenterY } });
            if (near != null)
            {   // the item's case: a light tank that drives during the flight. The 5.5 m tank drives ~8 m across the splash: to a point 5 m from
                // the target, turned ~95 degrees round it (away from the 7 m tank), so it stays inside the splash all the way
                float th = Mathf.Atan2(near.CenterY - target.CenterY, near.CenterX - target.CenterX);
                foreach (var turn in new[] { -95f, -80f, -110f, -65f })
                {
                    float a2 = th + turn * Mathf.Deg2Rad;
                    int dx = target.CenterX + (int)(Mathf.Cos(a2) * 40f), dy = target.CenterY + (int)(Mathf.Sin(a2) * 40f);
                    if (!Open(w, (dx - 16) >> 4, (dy - 16) >> 4)) continue;
                    if (!w.SendWaypoints(enemy, near, new[] { new ZWaypoint { mode = WpMode.Move, refId = -1, x = dx, y = dy } }) || near.Waypoints.Count == 0)
                    { moveNote = "the sim did not take the move order"; continue; }
                    mover = near;
                    moveNote = $"ordered to drive {Mathf.Sqrt((dx - near.CenterX) * (dx - near.CenterX) + (dy - near.CenterY) * (dy - near.CenterY)) / 8f:0.0} m";
                    break;
                }
                if (mover == null && moveNote == "") moveNote = "no open point to drive to";
            }
            OzDeathTrace.BlastDrawn -= OnBlast; OzDeathTrace.BlastDrawn += OnBlast;
            OpenZFxPlayer.ProjTrace -= OnBody; OpenZFxPlayer.ProjTrace += OnBody;
            frame0 = Time.frameCount;
            until = Time.realtimeSinceStartup + 12f;
            EditorApplication.update -= SalvoTick; EditorApplication.update += SalvoTick;
            Debug.Log($"[OpenZ KC5b] MLRS #{mlrs.Id} ordered onto light tank #{target.Id}; {tanks.Count} light tanks at 400 HP; moving tank #{(mover != null ? mover.Id : -1)} ({moveNote}) " +
                      $"(VehicleDeathSync {VehicleDeathSync.Enabled}, marked rockets {MlrsSalvo.MarkVehicles})");
        }

        static bool Open(ZWorld w, int tx, int ty)
        {
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) if (!w.Path.TilePassable(tx + x, ty + y, false)) return false;
            return !w.Objects.Any(o => o.IsUnit && Mathf.Abs(o.LocX - tx * 16) < 32 && Mathf.Abs(o.LocY - ty * 16) < 32);
        }

        static void OnBlast(ZEvent e, Vector3 at) { if (mlrs != null && e.objId == mlrs.Id) rocket1.Add((Time.frameCount, at)); }

        static void OnBody(int index, FxWeapon w, float age, float life, Vector3 pos, bool ease)
        {
            bodies[index] = bodies.TryGetValue(index, out var b) ? (pos, pos - b.last) : (pos, Vector3.zero);
        }

        static void Unhook()
        {
            EditorApplication.update -= SalvoTick; OzDeathTrace.BlastDrawn -= OnBlast; OpenZFxPlayer.ProjTrace -= OnBody;
        }

        /// <summary>Once the MLRS's shot is in the air (its marked rockets are queued): two enemy grunts 1-2 m beside the 8.5 m tank, inside
        /// rocket 1's splash but outside its 5 m core, on spots where the rocket marked for that tank is the nearest landing (a nearer filler
        /// rocket would throw them by the item 169 rule, rightly).</summary>
        static void PlaceGrunts(ZGame g)
        {
            var w = g.World; ZMissile shot = null;
            foreach (var m in w.PendingMissiles) if (m.attackerId == mlrs.Id && !m.debris && !m.grenade) shot = m;
            foreach (var m in w.QueuedMissiles) if (m.attackerId == mlrs.Id && !m.debris && !m.grenade) shot = m;
            if (shot == null) { if (Time.realtimeSinceStartup > until - 6f) { gruntsTried = true; gruntNote = "the MLRS did not fire within 6 s"; } return; }
            gruntsTried = true;
            if (far == null || far.Destroyed) { gruntNote = "no 8.5 m tank"; return; }
            var farAt = g.Views.TryGetValue(far.Id, out var fv) && fv ? fv.transform.position : g.CenterWorld(far);
            var spots = new List<Vector2Int>();
            for (int k = 0; k < 16 && spots.Count < 2; k++)
            {
                float ang = k * 22.5f * Mathf.Deg2Rad;
                int cx = far.CenterX + (int)(Mathf.Cos(ang) * 26f), cy = far.CenterY + (int)(Mathf.Sin(ang) * 26f);   // 3.25 m from its centre: 1.25 m off its hull
                float r1 = Mathf.Sqrt((cx - shot.x) * (cx - shot.x) + (cy - shot.y) * (cy - shot.y));
                if (r1 > shot.radius - 12 || r1 < 44f) continue;                                           // the sim's splash kills it; not in rocket 1's core
                if (!w.Path.TilePassable(cx >> 4, cy >> 4, true)) continue;
                if (w.Objects.Any(o => o.IsUnit && !o.Destroyed && o != far && Mathf.Abs(o.CenterX - cx) < 20 && Mathf.Abs(o.CenterY - cy) < 20)) continue;
                if (spots.Any(s => Mathf.Abs(s.x - cx) < 20 && Mathf.Abs(s.y - cy) < 20)) continue;
                if (MlrsSalvo.MarkVehicles)
                {
                    if (!MlrsSalvo.NearestLanding(g, mlrs.Id, ZGame.PxToWorld(cx, cy), 12f, 3f, out _, out var land)) continue;
                    var dl = land - farAt; dl.y = 0f;
                    if (dl.magnitude > 0.6f) continue;                                                     // a filler rocket lands nearer to this spot
                }
                spots.Add(new Vector2Int(cx, cy));
            }
            foreach (var s in spots)
            {
                var r = w.CreateObject("robot", "grunt", s.x - 8, s.y - 8, enemyTeam);
                g.SpawnUnitView(r);
                r.Damage = 0; r.SetHealth(30);
                grunts.Add(r); MlrsSalvo.Watch(r.Id);
            }
            if (spots.Count < 2) gruntNote = $"only {spots.Count} open spot(s) beside tank #{far.Id} where its marked rocket is the nearest landing";
            Debug.Log($"[OpenZ KC5b] {grunts.Count} grunt(s) put beside tank #{far.Id} (rocket 1 at {shot.x},{shot.y} px)");
        }

        static void SalvoTick()
        {
            if (!Application.isPlaying) { Unhook(); return; }
            var g = ZGame.I;
            if (!gruntsTried && g && g.World != null && mlrs != null) PlaceGrunts(g);
            bool allDead = tanks.All(t => t.Destroyed);
            bool played = allDead && tanks.All(t => VehicleDeathSync.Last.Any(r => r.id == t.Id && r.playedFrame >= 0));
            bool thrown = gruntsTried && grunts.All(r => MlrsSalvo.ThrownFrame(r.Id) >= 0);
            if (!(allDead && played && thrown && Time.realtimeSinceStartup > until - 9f) && Time.realtimeSinceStartup < until) return;
            Unhook();
            var sb = new StringBuilder("[OpenZ KC5b] MLRS salvo on light tanks\n"); int ok = 0, outside = 0;
            foreach (var t in tanks)
            {
                var rec = VehicleDeathSync.Last.LastOrDefault(r => r.id == t.Id);
                string tag = t == mover ? " (moving)" : "";
                if (!t.Destroyed || rec.id != t.Id) { sb.AppendLine(OzV11Kit.Verdict(false, $"light tank #{t.Id}{tag}: {(t.Destroyed ? "no death record" : "survived")}")); continue; }
                var boom = rec.playedFrame >= 0 ? rec.playedFrame : rec.frame;
                if (rec.why != "mlrs rocket 1") outside++;
                if (!VehicleDeathSync.Enabled)
                {   // BEFORE: it exploded on the sim's frame; what was drawn near it
                    string seen = rec.why == "mlrs rocket 1" ? "rocket 1's blast reached it on that frame" : rec.why == "mlrs ripple rocket" ? $"NO rocket on it then; the ripple rocket that comes down on it lands {rec.wait * 1000f:0} ms LATER"
                                : "NO drawn rocket lands on it at all";
                    bool okB = rec.why == "mlrs rocket 1"; if (okB) ok++;
                    sb.AppendLine(OzV11Kit.Verdict(okB, $"light tank #{t.Id}{tag}: exploded on the sim frame {rec.frame}; {seen}"));
                    continue;
                }
                int hit = -1; string how = rec.why;
                if (rec.why == "mlrs marked rocket") hit = MlrsSalvo.LastHitFrame(t.Id);
                else if (rec.why == "mlrs rocket 1" && rocket1.Count > 0) hit = rocket1.Where(b => b.frame <= boom).Select(b => b.frame).DefaultIfEmpty(-1).Max();
                else if (rec.why == "mlrs ripple rocket") hit = boom;                                     // it waited for the ripple rocket that lands on it
                bool pass = hit >= 0 && Mathf.Abs(boom - hit) <= 1;
                if (pass) ok++;
                sb.AppendLine(OzV11Kit.Verdict(pass, $"light tank #{t.Id}{tag}: sim death frame {rec.frame}, exploded frame {boom} (+{(rec.playedFrame >= 0 ? rec.playedAt - rec.time : 0f) * 1000f:0} ms), " +
                    $"drawn hit ({how}) frame {(hit >= 0 ? hit.ToString() : "NONE")}, explosion - hit {(hit >= 0 ? (boom - hit).ToString() : "-")} frames"));
            }
            sb.AppendLine(OzV11Kit.Verdict(outside >= 1, $"{outside} of {tanks.Count} tanks stood outside rocket 1's drawn blast (the item's case); marked rockets this session {MlrsSalvo.MarkedLandedOn}"));

            // the marked rockets of this salvo: the drawn body steered onto its tank every frame and ending on the blast
            var lands = MlrsSalvo.MarkedLog.Where(l => mlrs != null && l.shooter == mlrs.Id && l.frame >= frame0).ToList();
            bool bodiesOk = lands.Count > 0;
            foreach (var l in lands)
            {
                (Vector3 last, Vector3 step) b = default;
                bool has = l.proj >= 0 && bodies.TryGetValue(l.proj, out b);
                float raw = has ? Vector3.Distance(b.last, l.at) : -1f, ext = has ? Vector3.Distance(b.last + b.step, l.at) : -1f;
                bool pass = l.steered && has && Mathf.Min(raw, ext) <= BodyOnBlastM;
                bodiesOk &= pass;
                sb.AppendLine(OzV11Kit.Verdict(pass, $"marked rocket on tank #{l.target}{(mover != null && l.target == mover.Id ? " (moving)" : "")}: body steered onto it every frame {(l.steered ? "yes" : "NO")}, " +
                    $"its end point followed the tank {l.follow:0.0} m; drawn body's last point {(has ? raw.ToString("0.00") : "NONE")} m from the blast " +
                    $"({(has ? ext.ToString("0.00") : "-")} m one frame on along its last step; needs <= {BodyOnBlastM:0.0} m)"));
            }
            if (lands.Count == 0) sb.AppendLine(OzV11Kit.Verdict(false, "no marked rocket of this MLRS landed"));
            var ml = lands.LastOrDefault(l => mover != null && l.target == mover.Id);
            bool moved = mover != null && ml.target == mover.Id && ml.follow >= FollowMinM && MlrsSalvo.MarkedFollowMax >= FollowMinM;
            sb.AppendLine(OzV11Kit.Verdict(moved, mover == null ? $"no moving tank ({moveNote})" : ml.target == mover.Id
                ? $"moving tank #{mover.Id} ({moveNote}): the rocket marked for it followed it {ml.follow:0.0} m in flight (needs >= {FollowMinM:0} m); farthest this session {MlrsSalvo.MarkedFollowMax:0.0} m"
                : $"moving tank #{mover.Id} ({moveNote}): NO rocket marked for it landed"));

            // robots beside a marked tank fly with that tank's rocket (not with a filler rocket landing later, farther away)
            int hitFar = far != null ? MlrsSalvo.LastHitFrame(far.Id) : -1; bool gruntsOk = grunts.Count == 2;
            foreach (var r in grunts)
            {
                int th = MlrsSalvo.ThrownFrame(r.Id);
                bool pass = r.Destroyed && th >= 0 && hitFar >= 0 && Mathf.Abs(th - hitFar) <= 1;
                gruntsOk &= pass;
                sb.AppendLine(OzV11Kit.Verdict(pass, $"grunt #{r.Id} beside the 8.5 m tank #{far.Id}: {(r.Destroyed ? "" : "SURVIVED, ")}thrown frame {(th >= 0 ? th.ToString() : "NONE")}, " +
                    $"the rocket marked for that tank landed frame {(hitFar >= 0 ? hitFar.ToString() : "NONE")}: thrown - landed {(th >= 0 && hitFar >= 0 ? (th - hitFar).ToString() : "-")} frames (needs -1..1)"));
            }
            if (grunts.Count < 2) sb.AppendLine(OzV11Kit.Verdict(false, "grunts beside the 8.5 m tank: " + gruntNote));
            MlrsSalvo.WatchClear();
            bool all = ok == tanks.Count && outside >= 1 && bodiesOk && moved && gruntsOk;
            sb.AppendLine(all ? "KC5b MLRS PASS" : "KC5b MLRS FAIL (BEFORE gives FAIL lines: that is the old behaviour)");
            Debug.Log(sb.ToString());
        }

        // ------------------------------------------------------------------ a real fight
        [MenuItem(M + "Vehicle Death Report (Play)")]
        static void Report()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ KC5b] enter Play first and let vehicles die"); return; }
            var sb = new StringBuilder($"[OpenZ KC5b] vehicle / gun deaths: on the sim frame {VehicleDeathSync.OnFrame} (of them an MLRS rocket marked for it {VehicleDeathSync.MarkedHits}), " +
                $"held for the drawn round {VehicleDeathSync.HeldForRound}, held for a ripple rocket {VehicleDeathSync.HeldForRipple}, longest hold {VehicleDeathSync.MaxHeld * 1000f:0} ms, " +
                $"farthest a held vehicle rolled on {VehicleDeathSync.MaxGlide:0.00} m\n");
            int fails = 0, unnoted = 0;
            foreach (var r in VehicleDeathSync.Last.Skip(System.Math.Max(0, VehicleDeathSync.Last.Count - 20)))
            {
                string line = $"#{r.id} {r.type}: {r.why}, wait {r.wait * 1000f:0} ms, sim frame {r.frame} -> exploded frame {(r.playedFrame >= 0 ? r.playedFrame.ToString() : "?")}";
                bool pass = r.playedFrame >= 0 && r.why != "mlrs, no rocket on it";
                if (r.why.StartsWith("round"))
                {   // the drawn round's arrival as the drawing code noted it (kept with the record when it was held; else the last 64 noted kills)
                    bool noted = r.noted; float arrived = r.drawnAt;
                    if (!noted)
                    {
                        var d = BlastWave.DrawnKills.LastOrDefault(k => k.victim == r.id && k.frame == r.frame);
                        if (d.victim == r.id && d.frame == r.frame) { noted = true; arrived = d.at; }
                    }
                    if (noted) { float late = r.playedAt - arrived; line += $", drawn round arrived {(arrived - r.time) * 1000f:0} ms after the kill, exploded {late * 1000f:0} ms after it, rolled on {r.glide:0.00} m"; pass &= late >= -0.001f && late <= 0.034f; }
                    else { line += ", drawn round NOT noted (its arrival cannot be checked)"; pass = false; unnoted++; }
                }
                if (!pass) fails++;
                sb.AppendLine(OzV11Kit.Verdict(pass, line));
            }
            sb.AppendLine($"round kills whose drawn round was not noted: {unnoted} (must be 0)");
            sb.AppendLine(fails == 0 ? "KC5b REPORT PASS" : "KC5b REPORT FAIL " + fails);
            Debug.Log(sb.ToString());
        }
    }
}
