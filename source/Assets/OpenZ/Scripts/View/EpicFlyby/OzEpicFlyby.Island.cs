#if UNITY_EDITOR
// OpenZ (GPL-3.0) - epic flybys, the island steps (editor only; the game's rules and normal play are untouched):
//  - set-up "lounge_at": the scenario places a lounger ITSELF: a robot of its own (tag, team, type, hp_k) stands so that the antic's
//    lounger lies with its middle at x z, turned so the lying robot's feet point to `face` (a campfire; without it, to the camera),
//    the parasol at `parasol` (else the antic's own layout). Free flat land is checked as the antic checks it (no water, no blocked
//    tile, no map object, no campfire of the take, 0.12 m of slope, nothing standing there); a place that fails moves to the nearest
//    one that passes within nudge_m (default 1 m). In the pre-roll the robot begins LYING at once with the antic's own lounger model,
//    parasol and lie clip (the hero squad's level-start lie: AnticRun Hold + StartLying) and keeps lying until the antic's own cancel
//    gets it up: an enemy within lounge_wake_m, its own target (an enemy within its weapon's reach, 15 m for a grunt), a hit, a
//    missile landing near, or the cue "wake"; then the lounger and the parasol pop off the antic's way and it fights as any unit.
//  - cue "croc_catch": from t (for window_s seconds) the robot of squad `tag` that wades where a crocodile can come up beside it
//    (pick: camera = the one nearest the middle of the frame at the bite, default; longest = the one that waded longest; near = the
//    one nearest x z) is CAUGHT by the nearest free crocodile of its water: the animal goes under first when it is up (approach
//    "dive", default: the rule's dive, 1.2 s; "direct": it comes up at once), then comes up beside the robot and the rule's own rise,
//    lunge and bite take it (ZWorld.CrocScenarioCatch: a normal crocodile death). kind: roll (the slow death roll), fast (the fast
//    dive) or any (the view's own pick). A miss tries again while the window lasts.
//  - cue "wake": the lying robots of squad `tag` get up now (the antic's own cancel).
//  - "via" on units and order cues: order:move / order:attack_move through these points first (x, z pairs), each leg a plain move.
//  - start / order "hold": the game's own HOLD POSITION order (the player's H key): the squad stays where it stands and fires at
//    what comes within its reach, never chases. lounge_at "hold": true gives the lounger's robot the same order.
//  - what the camera sees: a catch and the blue robots at frame 0 are logged "seen" only when nothing stands between the lens and
//    the robot (a collider of the scene, or the drawn body of a gun or vehicle); the catch's pick prefers a robot the lens will see.
//  - cue "log_landing": who is alive and who stands ashore in `box` [x0, x1, z0, z1] (default the island), per squad.
using System;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    public sealed partial class OzEpicFlyby
    {
        static string XZ(Vector3 p) => p.x.ToString("F1") + "," + p.z.ToString("F1");

        // ================================================================== orders through waypoints, hold position
        /// <summary>The take's orders (a unit's start and the order cues): "hold" = the game's hold-position order; with `via` the
        /// waypoints first; anything else the flyby's own Order.</summary>
        string OrderIsland(ZObject u, string mode, float x, float z, float[] via)
        {
            if (mode == "hold")
            {
                if (u == null || u.Destroyed) return "gone";
                return "hold position " + (W.SendHold(u.Owner, u) ? "ok (fires at what comes within its reach, never chases)" : "REFUSED");
            }
            return via != null && via.Length >= 2 ? OrderVia(u, mode, x, z, via) : Order(u, mode, x, z);
        }

        /// <summary>order:move / order:attack_move through the `via` points (x, z pairs) first: each leg a plain move, the last one the
        /// order's own mode (attack_move: the unit fights what it meets on that last leg). Other modes ignore `via`.</summary>
        string OrderVia(ZObject u, string mode, float x, float z, float[] via)
        {
            if (u == null || u.Destroyed) return "gone";
            if (mode != "move" && mode != "attack_move") return Order(u, mode, x, z) + " (via ignored: only move / attack_move)";
            var wl = new List<ZWaypoint>();
            for (int i = 0; i + 1 < via.Length; i += 2) { var p = ZGame.WorldToPx(new Vector3(via[i], 0f, via[i + 1])); wl.Add(ZWaypoint.Move(p.x, p.y)); }
            var e = ZGame.WorldToPx(new Vector3(x, 0f, z)); wl.Add(ZWaypoint.Move(e.x, e.y, mode == "attack_move"));
            bool ok = W.SendWaypoints(u.Owner, u, wl);
            return mode + " via " + (via.Length / 2) + " point(s) " + (ok ? "ok (" + u.Waypoints.Count + " of " + wl.Count + " waypoints kept)" : "REFUSED");
        }

        // ================================================================== lounge_at
        sealed class Lounger
        {
            public Setup s; public Placed p; public ZObject o; public float yaw; public Vector3 bed; public Vector3? parasolWant;
            public AnticRun run; public string state = "placed", why = "", spotText = ""; public float upAt = -1f;
        }
        readonly List<Lounger> loungers = new List<Lounger>();
        bool loungersAtZero;

        /// <summary>Set-up lounge_at: the robot and its lounger's place (it lies down in the pre-roll: LoungeAtStep).</summary>
        string LoungeAt(Setup s)
        {
            var team = TeamOf(s.team); if (team == Team.Neutral) return "lounge_at needs team r or b";
            string type = string.IsNullOrEmpty(s.type) ? "grunt" : s.type;
            if (!ZBuildLists.IsRobot(type)) return "lounge_at: " + type + " is not a robot type";
            var want = new Vector3(s.x, 0f, s.z);
            var front = s.face != null && s.face.Length >= 2 ? new Vector3(s.face[0] - s.x, 0f, s.face[1] - s.z) : -KeyForwardNear(want);
            front.y = 0f; if (front.sqrMagnitude < 1e-4f) front = Vector3.forward; front.Normalize();
            float yaw = Mathf.Atan2(front.x, front.z) * Mathf.Rad2Deg;
            // the place: the given middle, else the nearest within nudge_m (0.5 m rings, 8 sides) where the lounger and the robot fit
            float nudge = s.nudge_m > 0f ? s.nudge_m : 1f;
            var bed = want; string first = BedGround(want, yaw, 1f, 1f);
            if (first != null)
            {
                bool found = false;
                for (float r = 0.5f; r <= nudge + 1e-3f && !found; r += 0.5f)
                    for (int k = 0; k < 8 && !found; k++)
                    {
                        var c = want + Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward * r;
                        if (BedGround(c, yaw, 1f, 1f) == null) { bed = c; found = true; }
                    }
                Note(found ? "set-up lounge_at " + s.tag + ": at the given middle " + XZ(want) + " " + first + ": moved " + (bed - want).magnitude.ToString("F1") + " m to " + XZ(bed)
                           : "WARNING lounge_at " + s.tag + ": at " + XZ(want) + " " + first + ", and no place within " + nudge + " m is better: placed there anyway");
            }
            var stand = bed + front * AnticSockets.BedBack;                                           // world scale 1 here; the robot's own scale is used when it lies
            var px = ZGame.WorldToPx(stand);
            var o = W.CreateRobotGroup(type, px.x - 8, px.y - 8, team, 1);
            if (o == null) return "no robot made";
            if (s.hp_k > 1f) { o.MaxHealth = Mathf.RoundToInt(o.MaxHealth * s.hp_k); o.SetHealth(o.MaxHealth); }
            string held = s.hold ? (W.SendHold(team, o) ? ", hold position" : ", hold position REFUSED") : "";      // no walking off the lounger before an enemy comes within its reach
            g.SpawnUnitView(o);
            var u = new Unit { tag = s.tag, team = s.team, type = type, start = "lounge_at", x = stand.x, z = stand.z, hp_k = s.hp_k };
            var pl = new Placed { u = u, lead = o }; pl.ids.Add(o.Id); placed.Add(pl);
            stagedIds.Add(o.Id); stagedTeam.Add(team);
            Vector3? pw = s.parasol != null && s.parasol.Length >= 2 ? new Vector3(s.parasol[0], 0f, s.parasol[1]) : (Vector3?)null;
            loungers.Add(new Lounger { s = s, p = pl, o = o, yaw = yaw, bed = bed, parasolWant = pw });
            return type + " #" + o.Id + " " + o.Owner + " (tag " + s.tag + ") at " + Where(o) + ", its lounger's middle " + XZ(bed) + ", feet toward " +
                   (s.face != null && s.face.Length >= 2 ? s.face[0] + "," + s.face[1] : "the camera") + " (yaw " + yaw.ToString("F0") + ")" + held + "; it lies down in the pre-roll";
        }

        /// <summary>The ground direction of the camera keys at the key that looks closest to this point (before the path is built).</summary>
        Vector3 KeyForwardNear(Vector3 c)
        {
            Vector3 best = Vector3.forward; float bd = float.MaxValue;
            foreach (var k in sc.camera)
            {
                if (k.look == null || k.look.Length < 3 || k.pos == null || k.pos.Length < 3) continue;
                float d = new Vector2(k.look[0] - c.x, k.look[2] - c.z).sqrMagnitude;
                if (d < bd) { bd = d; best = new Vector3(k.look[0] - k.pos[0], 0f, k.look[2] - k.pos[2]); }
            }
            return best.sqrMagnitude > 0.01f ? best.normalized : Vector3.forward;
        }

        /// <summary>Null when a lounger with its middle at c, turned to yaw (k = the robot's world scale, wf = its width factor), and the
        /// robot standing in front of it are on free flat land (the antic's own test), else why not.</summary>
        string BedGround(Vector3 c, float yaw, float k, float wf)
        {
            var front = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward; var right = Vector3.Cross(Vector3.up, front);
            float hw = 0.55f * k * wf, hl = 1.3f * k, lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < 15; i++)
            {
                var q = c + right * hw * ((i % 3) - 1) + front * hl * ((i / 3) * 0.5f - 1f);
                string w = Blocked(q, true); if (w != null) return "the lounger " + w;
                float h = g.GroundY(q.x, q.z); lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
            }
            if (hi - lo > 0.12f * k) return "the lounger stands on a slope (" + (hi - lo).ToString("F2") + " m, the antic allows " + (0.12f * k).ToString("F2") + ")";
            var stand = c + front * AnticSockets.BedBack * k;
            string ws = Blocked(stand, true); if (ws != null) return "the robot's place " + ws;
            foreach (var l in loungers) if ((l.bed - c).sqrMagnitude < 2.2f * 2.2f) return "is within 2.2 m of the lounger of #" + (l.o != null ? l.o.Id : -1);
            return null;
        }

        /// <summary>Why the point q is no place for a lounger or a parasol (null = free): water, a blocked tile, a map object (flag, gun,
        /// hut, rock), a campfire of the take (1.3 m round it), something standing there (the antic's own ray). ground = false: only the
        /// map objects (the ground under a parasol's canopy may be water).</summary>
        string Blocked(Vector3 q, bool ray, bool ground = true)
        {
            if (ground && OpenZFxPlayer.IsWaterTile(q)) return "is in the water";
            if (ground && OpenZFxPlayer.ClassAt(q) == ZClass.Impassable) return "is on a blocked tile";
            int px = Mathf.RoundToInt(q.x * 8f), py = Mathf.RoundToInt(-q.z * 8f);
            foreach (var o in W.Objects)
            {
                if (o == null || o.Destroyed || o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle) continue;
                if (px >= o.LocX - 2 && px < o.LocX + o.WidthPix + 2 && py >= o.LocY - 2 && py < o.LocY + o.HeightPix + 2) return "touches the " + o.Type + " #" + o.Id;
            }
            if (ground) foreach (var pr in sc.props) if (pr.kind == "campfire" && new Vector2(q.x - pr.x, q.z - pr.z).sqrMagnitude < 1.3f * 1.3f) return "is in the campfire at " + pr.x + "," + pr.z;
            if (ray)
            {
                float h = g.GroundY(q.x, q.z);
                if (Physics.Raycast(new Vector3(q.x, h + 6f, q.z), Vector3.down, out var hit, 5.85f, ~OzRockSolid.Mask, QueryTriggerInteraction.Ignore) && hit.point.y > h + 0.15f
                    && !hit.collider.GetComponentInParent<UnitView>()) return "has something standing there (" + hit.collider.name + ")";
            }
            return null;
        }

        /// <summary>The pre-roll, every step until each scenario lounger lies (the first steps: its view must be there and idle).</summary>
        void LoungeAtStep(int k, int pre)
        {
            foreach (var l in loungers)
            {
                if (l.run != null || l.state == "failed") continue;
                string why = TryLie(l);
                if (why == null) { l.state = "lying"; Note("loungers " + l.s.tag + ": #" + l.o.Id + " lying from pre-roll step " + k + " of " + pre + " (" + l.spotText + ")"); continue; }
                if (why != l.why) Note("loungers " + l.s.tag + ": #" + l.o.Id + " not lying yet at pre-roll step " + k + ": " + why + " (tried again every step of the pre-roll)");
                l.why = why;
                if (k >= pre - 1) { l.state = "failed"; Note("WARNING loungers " + l.s.tag + ": #" + l.o.Id + " NOT lying at the end of the pre-roll: " + why); }
            }
        }

        string TryLie(Lounger l)
        {
            if (l.o == null || l.o.Destroyed) return "dead";
            if (!g.Views.TryGetValue(l.o.Id, out var v) || !v) return "no view yet";
            if (!v.CanAntic) return "no antic clips (" + l.o.Type + ")";
            var kit = AnticDirector.Kit; if (!kit || !kit.HasSunbed) return "no lounger kit";
            if (v.AnticActive) return "another antic runs";
            var busy = v.AnticBusyReason(); if (busy != null) return busy;
            float k = AnticSockets.WorldScale(v), wf = AnticSockets.BedWidthFor(v);
            var front = Quaternion.Euler(0f, l.yaw, 0f) * Vector3.forward; var right = Vector3.Cross(Vector3.up, front);
            var p = ZGame.PxToWorld(l.o.CenterX, l.o.CenterY);
            var c = p - front * (AnticSockets.BedBack * k);
            // the lounger's height as the antic sets it: its middle's ground, never above the lowest corner + 2 cm
            float gy = g.GroundY(c.x, c.z), lo = gy;
            for (int i = 0; i < 9; i++) { var q = c + right * (0.55f * k * wf) * ((i % 3) - 1) + front * (1.3f * k) * ((i / 3) - 1); lo = Mathf.Min(lo, g.GroundY(q.x, q.z)); }
            var bedRot = Quaternion.LookRotation(front, Vector3.up);
            var spot = new AnticSpot { yaw = l.yaw, bed = new Vector3(c.x, Mathf.Min(gy, lo + 0.02f), c.z), bedRot = bedRot, bedWidth = wf };
            string ptext = Parasol(spot, l, c, front, k, wf);
            var run = new AnticRun(v, AnticKind.Sunbed, spot) { Hold = true, StartLying = true };
            if (!v.AnticBegin(run)) return "refused by the robot's view";
            AnticDirector.Ensure().Started(run);
            l.run = run;
            l.spotText = "lounger " + XZ(c) + " yaw " + l.yaw.ToString("F0") + ", world scale " + k.ToString("F2") + ", width x" + wf.ToString("F2") + ", parasol " + ptext;
            return null;
        }

        /// <summary>The parasol: at the scenario's place when it is free, else the antic's own layout (past the head end of the
        /// lounger away from the camera, shifted sideways for the canopy's shadow), leaning away from the camera at its closest look.</summary>
        string Parasol(AnticSpot spot, Lounger l, Vector3 c, Vector3 front, float k, float wf)
        {
            var camF = shots.Count > 0 ? CameraForwardNear(c) : KeyForwardNear(c); var camR = Vector3.Cross(Vector3.up, camF);
            float ta = AnticSpot.ParasolTilt * Mathf.Deg2Rad;
            var tilt = Quaternion.FromToRotation(Vector3.up, (Vector3.up * Mathf.Cos(ta) + camF * Mathf.Sin(ta)).normalized);
            spot.parasolRot = tilt * spot.bedRot;
            float lean = AnticSpot.ParasolCanopyY * k * Mathf.Sin(ta);
            string why = null;
            if (l.parasolWant.HasValue)
            {   // the scenario's place, tested as the antic's layout tests its own: the pole's foot on free land (no water, no blocked
                // tile, no map object, no campfire, nothing standing there), the canopy's lean clear of map objects
                var w = l.parasolWant.Value; why = Blocked(w, true) ?? Blocked(w + camF * lean, false, false);
                if (why == null) { spot.parasol = new Vector3(w.x, g.GroundY(w.x, w.z), w.z); return XZ(w) + " (the scenario's place)"; }
            }
            var right = Vector3.Cross(Vector3.up, front);
            var head = c - front * (1.2f * k);
            float far = Vector3.Dot(c, camF) + 1.2f * k * Mathf.Abs(Vector3.Dot(front, camF)) + 0.55f * k * wf * Mathf.Abs(Vector3.Dot(right, camF));
            var baseP = head + camF * (far + AnticSpot.ParasolGap * k - Vector3.Dot(head, camF));
            float shade = 0f; var sun = RenderSettings.sun;
            if (sun && sun.isActiveAndEnabled && sun.transform.forward.y < -0.2f)
            {
                float hc = AnticSpot.ParasolCanopyY * k * Mathf.Cos(ta); var L = sun.transform.forward; var off = new Vector3(L.x, 0f, L.z) * (hc / -L.y);
                shade = Mathf.Clamp(-Vector3.Dot(off, camR), -AnticSpot.ParasolShadeShift * k, AnticSpot.ParasolShadeShift * k);
            }
            foreach (float s in new[] { shade, 0f, 0.9f * k, -0.9f * k, 1.8f * k, -1.8f * k })
            {
                var pc = baseP + camR * s;
                if (Blocked(pc, true) != null || Blocked(pc + camF * lean, false, false) != null) continue;
                spot.parasol = new Vector3(pc.x, g.GroundY(pc.x, pc.z), pc.z);
                return XZ(pc) + " (the antic's layout" + (why != null ? "; the scenario's place " + why : "") + ")";
            }
            spot.parasol = new Vector3(baseP.x, g.GroundY(baseP.x, baseP.z), baseP.z);
            Note("WARNING loungers " + l.s.tag + ": no free place for the parasol of #" + l.o.Id + (why != null ? " (the scenario's place " + why + ")" : "") + ": it stands at " + XZ(baseP));
            return XZ(baseP) + " (NOT free)";
        }

        /// <summary>Every captured frame: the loungers at frame 0 and the moment each gets up (with the antic's own reason).</summary>
        void LoungerWatch(float t, int frame)
        {
            if (loungers.Count == 0) return;
            if (!loungersAtZero)
            {
                loungersAtZero = true; int n = 0; var sb = new StringBuilder();
                foreach (var l in loungers)
                {
                    bool lying = l.run != null && !l.run.Finished; if (lying) n++;
                    sb.Append(l.s.tag).Append(" #").Append(l.o.Id).Append(lying ? " lying" : " NOT lying (" + (l.run != null ? "got up in the pre-roll: " + LastCancel(l.run) : l.why) + ")").Append("; ");
                }
                Note("loungers at frame " + frame + ": " + n + " of " + loungers.Count + " lying (" + sb + ")");
            }
            foreach (var l in loungers)
            {
                if (l.state != "lying" || (l.run != null && !l.run.Finished)) continue;
                l.state = "up"; l.upAt = t;
                Note("loungers " + l.s.tag + ": #" + l.o.Id + " got up at " + t.ToString("F2") + " s (frame " + frame + "): " + LastCancel(l.run) + "; the nearest enemy " + NearestEnemyM(l.o) + " m");
            }
        }

        static string LastCancel(AnticRun r)
        {
            if (r == null) return "-";
            for (int i = r.Trace.Count - 1; i >= 0; i--) { var s = r.Trace[i]; int c = s.IndexOf("cancel: ", StringComparison.Ordinal); if (c >= 0) return s.Substring(c + 8); int e = s.IndexOf("end: ", StringComparison.Ordinal); if (e >= 0) return s.Substring(e); }
            return "ended";
        }

        string NearestEnemyM(ZObject u)
        {
            if (u == null) return "-";
            var e = NearestEnemy(u); if (e == null) return "-";
            return (Mathf.Sqrt((float)(e.CenterX - u.CenterX) * (e.CenterX - u.CenterX) + (float)(e.CenterY - u.CenterY) * (e.CenterY - u.CenterY)) / 8f).ToString("F1");
        }

        /// <summary>Cue wake: the lying robots of squad `tag` get up now (the antic's own cancel: the lounger pops off, the weapon back).</summary>
        string WakeCue(string tag)
        {
            int n = 0, m = 0;
            foreach (var p in placed)
            {
                if (p.u.tag != tag) continue;
                foreach (int id in p.ids) { m++; if (g.Views.TryGetValue(id, out var v) && v && v.AnticActive) { v.AnticCancel("scenario cue: wake", false); n++; } }
            }
            return m == 0 ? "no squad " + tag : n + " of " + m + " robot(s) of " + tag + " got up";
        }

        // ================================================================== croc_catch
        sealed class CatchJob
        {
            public Cue q; public int no, kind; public float until; public bool direct;
            public string state = "wait", why = "no robot of the squad wades where a crocodile can come up beside it";
            public ZCroc c; public ZObject prey; public float since, bitAt = -1f; public int tries;
            public bool fits, seen; public string where = "";
        }
        readonly List<CatchJob> catchJobs = new List<CatchJob>();
        readonly HashSet<int> catchCrocs = new HashSet<int>();
        int forceSave; bool forceSet;

        /// <summary>Cue croc_catch: queued; the catch runs frame by frame (StepCatches).</summary>
        string CrocCatchCue(Cue q, float t)
        {
            if (W.Crocs.Count == 0) return "no crocodiles on this level";
            if (!forceSet) { forceSave = CrocodileView.ForceCatch; forceSet = true; }
            var j = new CatchJob { q = q, no = catchJobs.Count + 1, until = q.t + Mathf.Max(0.5f, q.window_s > 0f ? q.window_s : 4f), direct = q.approach == "direct",
                                   kind = q.kind == "roll" ? 1 : q.kind == "fast" ? 2 : 0 };
            catchJobs.Add(j);
            return "catch " + j.no + " on " + q.tag + " queued (" + (j.kind == 1 ? "the slow roll" : j.kind == 2 ? "the fast dive" : "either catch") + ", " +
                   (j.direct ? "the crocodile comes up at once" : "the crocodile goes under first when it is up") + ", pick " + (string.IsNullOrEmpty(q.pick) ? "camera" : q.pick) +
                   ", tried until " + j.until.ToString("F1") + " s)";
        }

        /// <summary>A robot a crocodile may take now: alive, on a swamp tile of a crocodile's water, no animal after it already.</summary>
        bool Catchable(ZObject r, CatchJob self)
        {
            if (r == null || r.Destroyed || r.Kind != ZKind.Robot || r.KillAt >= 0 || r.Health <= 0 || r.Eaten) return false;
            if (W.Map.ClassAt(r.CenterX >> 4, r.CenterY >> 4) != ZClass.Water || W.CrocPoolAt(r.CenterX, r.CenterY) < 0) return false;
            foreach (var c in W.Crocs) if (c.PreyId == r.Id && ZWorld.CrocInCatch(c)) return false;
            foreach (var j in catchJobs) if (j != self && j.prey == r && (j.state == "diving" || j.state == "rising")) return false;
            return true;
        }

        ZCroc NearestFreeCroc(ZObject r, CatchJob self)
        {
            int pool = W.CrocPoolAt(r.CenterX, r.CenterY); ZCroc best = null; double bd = double.MaxValue;
            foreach (var c in W.Crocs)
            {
                if (c.Pool != pool || ZWorld.CrocInCatch(c) || (catchCrocs.Contains(c.Index) && (self.c == null || self.c.Index != c.Index))) continue;
                double d = (c.X - r.CenterX) * (c.X - r.CenterX) + (c.Y - r.CenterY) * (c.Y - r.CenterY);
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        /// <summary>Where the robot stands in the frame at second t of the path: 0 = the middle, 1 = the edge (more = outside).</summary>
        float FrameScore(float t, Vector3 w)
        {
            Pose(Mathf.Clamp(t, 0f, sc.seconds), out var cp, out var cl, out var cf, out _);
            var f = cl - cp; if (f.sqrMagnitude < 1e-4f) return 50f;
            var l = Quaternion.Inverse(Quaternion.LookRotation(f.normalized, Vector3.up)) * (w - cp);
            if (l.z < 1f) return 50f + (1f - l.z);
            float ty = Mathf.Tan(cf * 0.5f * Mathf.Deg2Rad), tx = ty * W4 / (float)Mathf.Max(1, H4);
            return Mathf.Max(Mathf.Abs(l.x / l.z / tx), Mathf.Abs(l.y / l.z / ty)) + l.z * 0.002f;
        }

        Vector3 RobotWorld(ZObject r) { var p = ZGame.PxToWorld(r.CenterX, r.CenterY); p.y = g.GroundY(p.x, p.z) + 0.6f; return p; }

        /// <summary>The robot of the job's squad to take now and the nearest free crocodile of its water (null robot = none yet).</summary>
        bool PickPrey(CatchJob j, float t, ZCroc only)
        {
            ZObject best = null; ZCroc bestC = null; float bs = float.MaxValue;
            float bite = t + (j.direct || (only != null && !Surface(only)) ? 0f : (float)ZWorld.CrocDiveSeconds) + (float)(ZWorld.CrocRiseSeconds + ZWorld.CrocBiteSeconds);
            foreach (var p in placed)
            {
                if (p.u.tag != j.q.tag) continue;
                foreach (int id in p.ids)
                {
                    var r = W.Get(id); if (!Catchable(r, j)) continue;
                    var c = only ?? NearestFreeCroc(r, j);
                    if (c == null || c.Pool != W.CrocPoolAt(r.CenterX, r.CenterY)) { j.why = "no crocodile free in the water of the squad's waders"; continue; }
                    if (!W.CrocScenarioSide(r, 0, 0, out _, out _, out _)) { j.why = "the squad's waders stand too near a bank for a crocodile to come up beside them"; continue; }
                    float s = j.q.pick == "longest" ? (float)r.CrocWetSince
                            : j.q.pick == "near" ? new Vector2(r.CenterX / 8f - j.q.x, -r.CenterY / 8f - j.q.z).magnitude
                            : FrameScore(bite, RobotWorld(r)) + (Hides(LensAt(bite), Upper(r), r) != null ? 3f : 0f);   // a robot the lens will not see (a gun, a tree, the bank in front) only when no other is there
                    if (s < bs) { bs = s; best = r; bestC = c; }
                }
            }
            if (best == null) return false;
            j.prey = best; j.c = bestC;
            return true;
        }

        /// <summary>The animal shows on the water (it dives first, unless the cue says "direct").</summary>
        static bool Surface(ZCroc c) => c.State != ZCrocState.Under && c.State != ZCrocState.Dive;

        /// <summary>Every captured frame: the queued catches, step by step; the view's catch (roll / fast dive) for the next bite.</summary>
        void StepCatches(float t, int frame)
        {
            foreach (var j in catchJobs)
            {
                switch (j.state)
                {
                    case "wait":
                        if (t > j.until) { j.state = "failed"; Note("croc_catch " + j.q.tag + " (catch " + j.no + "): NOT caught by " + j.until.ToString("F1") + " s: " + j.why); break; }
                        if (!PickPrey(j, t, null)) break;
                        catchCrocs.Add(j.c.Index); j.since = t; j.tries++;
                        if (!j.direct && Surface(j.c))
                        {
                            var st = j.c.State; W.CrocScenarioDive(j.c); j.state = "diving";
                            Note("croc_catch " + j.q.tag + " (catch " + j.no + "): crocodile " + j.c.Index + " (" + st + ") goes under at " + t.ToString("F2") + " s (frame " + frame + ") for " + j.prey.Type + " #" + j.prey.Id + " at " + Where(j.prey));
                        }
                        else if (j.c.State == ZCrocState.Dive) j.state = "diving";
                        else Rise(j, t, frame);
                        break;
                    case "diving":
                        if (j.c.State == ZCrocState.Under) Rise(j, t, frame);
                        else if (j.c.State != ZCrocState.Dive || t - j.since > 3f) Release(j, "crocodile " + j.c.Index + " did not go under (" + j.c.State + ")");
                        break;
                    case "rising":
                        if (j.c.Took && j.prey != null && j.prey.Eaten && j.prey.EatenBy == j.c.Index)
                        {
                            j.state = "caught"; j.bitAt = t; catchCrocs.Remove(j.c.Index);
                            var up = Upper(j.prey);
                            bool inFrame = InFrameNow(t, frame, up, out float vx, out float vy);
                            string hid = inFrame ? Hides(LensNow(t, frame), up, j.prey) : null;
                            j.seen = inFrame && hid == null;
                            Note("croc_catch " + j.q.tag + " (catch " + j.no + "): CAUGHT " + j.prey.Type + " #" + j.prey.Id + " by crocodile " + j.c.Index + " at " + Where(j.prey) + ", the bite at " +
                                 t.ToString("F2") + " s (frame " + frame + "), " + (j.kind == 1 ? "the slow roll" : j.kind == 2 ? "the fast dive" : "the view's pick") + "; " +
                                 (!inFrame ? "OUTSIDE the frame (" + vx.ToString("F2") + ", " + vy.ToString("F2") + ")"
                                  : "in the frame at " + vx.ToString("F2") + ", " + vy.ToString("F2") + (hid == null ? " and seen" : " but HIDDEN behind " + hid)));
                        }
                        else if (!ZWorld.CrocInCatch(j.c) || j.c.PreyId != j.prey.Id || t - j.since > 3f)
                            Release(j, "missed: crocodile " + j.c.Index + " " + j.c.State + ", " + j.prey.Type + " #" + j.prey.Id + (j.prey.Destroyed || j.prey.Health <= 0 ? " fell to a shot first" : " out of its jaws"));
                        break;
                }
            }
            // the view's catch for the next bite: the kind of the catch that rises now, else of one that bit in the last half second
            if (!forceSet) return;
            int want = forceSave; bool any = false;
            foreach (var j in catchJobs) if (j.state == "rising") { want = j.kind; any = true; break; }
            if (!any) foreach (var j in catchJobs) if (j.state == "caught" && t - j.bitAt < 0.5f) { want = j.kind; break; }
            CrocodileView.ForceCatch = want;
        }

        /// <summary>The animal comes up beside the job's robot (or another wader of the squad in its water when that one went).</summary>
        void Rise(CatchJob j, float t, int frame)
        {
            if (!Catchable(j.prey, j) && !PickPrey(j, t, j.c)) { Release(j, "the robot left the crocodile's water before it came up"); return; }
            Pose(Mathf.Min(sc.seconds, t + (float)(ZWorld.CrocRiseSeconds + ZWorld.CrocBiteSeconds)), out var cp, out var cl, out _, out _);
            var f = cl - cp;
            if (!W.CrocScenarioSide(j.prey, f.x, -f.z, out double x, out double y, out bool fits)) { Release(j, "no room beside " + j.prey.Type + " #" + j.prey.Id); return; }
            string err = W.CrocScenarioCatch(j.c, j.prey, x, y);
            if (err != null) { Release(j, err); return; }
            j.state = "rising"; j.since = t; j.fits = fits;
            Note("croc_catch " + j.q.tag + " (catch " + j.no + "): crocodile " + j.c.Index + " comes up beside " + j.prey.Type + " #" + j.prey.Id + " at " + Where(j.prey) + " at " + t.ToString("F2") +
                 " s (frame " + frame + "), from " + (x / 8.0).ToString("F1") + "," + (-y / 8.0).ToString("F1") + (fits ? "" : " (the whole attack box is NOT on open water there)"));
        }

        void Release(CatchJob j, string why)
        {
            if (j.c != null) catchCrocs.Remove(j.c.Index);
            j.why = why; j.state = "wait"; j.c = null; j.prey = null;
            Note("croc_catch " + j.q.tag + " (catch " + j.no + "): " + why + (j.tries < 4 ? "; tried again while the window lasts" : ""));
            if (j.tries >= 4) { j.state = "failed"; Note("croc_catch " + j.q.tag + " (catch " + j.no + "): NOT caught after " + j.tries + " tries"); }
        }

        // ================================================================== what the lens sees
        /// <summary>The lens at clip second t as the path puts it (the floor lift included; not the handheld drift or a dodge).</summary>
        Vector3 LensAt(float t)
        {
            Pose(Mathf.Clamp(t, 0f, sc.seconds), out var p, out _, out _, out _);
            if (g) p.y = Mathf.Max(p.y, g.GroundY(p.x, p.z) + sc.floor_m);
            return p;
        }

        /// <summary>The lens of this frame: the camera itself once the take has placed it (frame 1 on: the last pose), else the path.</summary>
        Vector3 LensNow(float t, int frame) => frame > 0 && cam ? cam.transform.position : LensAt(t);

        /// <summary>Whether w is inside this frame (vx, vy: 0..1 inside).</summary>
        bool InFrameNow(float t, int frame, Vector3 w, out float vx, out float vy)
        {
            if (frame > 0 && cam)
            {
                var vp = cam.WorldToViewportPoint(w); vx = vp.x; vy = vp.y;
                return vp.z > 0f && vx > 0f && vx < 1f && vy > 0f && vy < 1f;
            }
            Pose(Mathf.Clamp(t, 0f, sc.seconds), out var cp, out var cl, out var cf, out _);
            cp = LensAt(t);
            vx = vy = -1f; var f = cl - cp; if (f.sqrMagnitude < 1e-4f) return false;
            var l = Quaternion.Inverse(Quaternion.LookRotation(f.normalized, Vector3.up)) * (w - cp);
            if (l.z < 0.5f) return false;
            float ty = Mathf.Tan(cf * 0.5f * Mathf.Deg2Rad), tx = ty * W4 / (float)Mathf.Max(1, H4);
            vx = 0.5f + 0.5f * l.x / l.z / tx; vy = 0.5f + 0.5f * l.y / l.z / ty;
            return vx > 0f && vx < 1f && vy > 0f && vy < 1f;
        }

        /// <summary>The robot's upper body (its drawn place 0.9 m up; the Sim's place when it has no view): the part a wading robot
        /// shows over the water.</summary>
        Vector3 Upper(ZObject r)
        {
            if (r != null && g.Views.TryGetValue(r.Id, out var v) && v) return v.transform.position + Vector3.up * 0.9f;
            return RobotWorld(r) + Vector3.up * 0.4f;
        }

        /// <summary>What stands between the lens and the point w (null = nothing): a collider of the scene (the ground of a bank, a
        /// gun, a tree, a hut, a rock) or the drawn body of a gun or vehicle (its meshes' box). Robots (thin), crocodiles, the water,
        /// triggers and the last 0.4 m before the point never count.</summary>
        string Hides(Vector3 lens, Vector3 w, ZObject self)
        {
            var d = w - lens; float len = d.magnitude; if (len < 1f) return null;
            var dir = d / len; float far = len - 0.4f;
            var hits = Physics.RaycastAll(lens, dir, far, ~OzRockSolid.Mask, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                var c = h.collider; if (!c) continue;
                var uv = c.GetComponentInParent<UnitView>();
                if (uv && (uv.Obj == null || uv.Obj.Kind == ZKind.Robot || uv.Obj == self)) continue;
                if (c.GetComponentInParent<CrocodileView>()) continue;
                if (c.gameObject.layer == 4 || c.name.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                var fx = OpenZFxPlayer.I;                                                                 // the water's surface itself (the level's water table), whatever its collider is called
                if (fx && OpenZFxPlayer.IsWaterTile(h.point) && !float.IsNegativeInfinity(fx.WaterY) && Mathf.Abs(h.point.y - fx.WaterYAt(h.point)) < 0.25f) continue;
                return c.name + (uv && uv.Obj != null ? " (" + uv.Obj.Type + " #" + uv.Obj.Id + ")" : "") + " at " + h.distance.ToString("F1") + " m";
            }
            var ray = new Ray(lens, dir);
            foreach (var kv in g.Views)
            {
                var v = kv.Value; if (!Living(v) || v.Obj.Kind == ZKind.Robot || v.Obj == self) continue;
                var vp = v.transform.position; if (new Vector2(vp.x - lens.x, vp.z - lens.z).sqrMagnitude > (len + 6f) * (len + 6f)) continue;
                bool any = false; var b = default(Bounds);
                foreach (var r in v.GetComponentsInChildren<Renderer>())
                {
                    if (!r || !r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                if (any && b.IntersectRay(ray, out float at) && at < far) return v.Obj.Type + " #" + v.Obj.Id + " (its drawn body) at " + at.ToString("F1") + " m";
            }
            return null;
        }

        /// <summary>Frame 0: how many blue robots the lens sees (in the frame and not hidden), squad by squad.</summary>
        void BlueAtZero(float t, int frame)
        {
            int all = 0, inF = 0, seen = 0; var per = new StringBuilder(); var hidden = new StringBuilder(); var lens = LensNow(t, frame);
            foreach (var p in placed)
            {
                if (TeamOf(p.u.team) != Team.Blue) continue;
                int a = 0, s = 0;
                foreach (int id in p.ids)
                {
                    var r = W.Get(id); if (r == null || r.Destroyed || r.KillAt >= 0 || r.Kind != ZKind.Robot) continue;
                    a++; all++; var up = Upper(r);
                    if (!InFrameNow(t, frame, up, out float vx, out float vy)) { hidden.Append(p.u.tag).Append(" #").Append(id).Append(" outside the frame; "); continue; }
                    inF++; string h = Hides(lens, up, r);
                    if (h == null) { s++; seen++; } else hidden.Append(p.u.tag).Append(" #").Append(id).Append(" behind ").Append(h).Append("; ");
                }
                per.Append(p.u.tag).Append(" ").Append(s).Append("/").Append(a).Append(", ");
            }
            if (all > 0) Note("blue at frame " + frame + ": " + inF + " of " + all + " in the frame, " + seen + " seen (" + per.ToString().TrimEnd(',', ' ') + ")" + (hidden.Length > 0 ? "; not seen: " + hidden : ""));
        }

        // ================================================================== log_landing
        /// <summary>Cue log_landing: per squad the robots alive and the robots ashore in `box` [x0, x1, z0, z1] (land tiles only;
        /// default the island x 50..64.5, z -140..-121), the squads with a robot ashore, the guns' owners.</summary>
        string LandingCue(Cue q)
        {
            float x0 = 50f, x1 = 64.5f, z0 = -140f, z1 = -121f;
            if (q.box != null && q.box.Length >= 4) { x0 = Mathf.Min(q.box[0], q.box[1]); x1 = Mathf.Max(q.box[0], q.box[1]); z0 = Mathf.Min(q.box[2], q.box[3]); z1 = Mathf.Max(q.box[2], q.box[3]); }
            int blue = 0, red = 0, blueSq = 0, blueAshore = 0; var sb = new StringBuilder(); var ash = new StringBuilder();
            foreach (var p in placed)
            {
                var team = TeamOf(p.u.team); if (team == Team.Neutral) continue;
                int a = 0, on = 0;
                foreach (int id in p.ids)
                {
                    var o = W.Get(id); if (o == null || o.Destroyed || o.KillAt >= 0 || o.Eaten || o.Health <= 0) continue;
                    a++;
                    float x = o.CenterX / 8f, z = -o.CenterY / 8f;
                    if (x >= x0 && x <= x1 && z >= z0 && z <= z1 && W.Map.ClassAt(o.CenterX >> 4, o.CenterY >> 4) != ZClass.Water) on++;
                }
                if (team == Team.Blue) { blue += a; blueSq++; if (on > 0) { blueAshore++; ash.Append(p.u.tag).Append(" "); } } else red += a;
                sb.Append(p.u.tag).Append(" ").Append(a).Append("/").Append(p.ids.Count).Append(on > 0 ? " (" + on + " ashore)" : "").Append("; ");
            }
            var guns = new StringBuilder();
            foreach (var o in W.Objects) if (o != null && !o.Destroyed && o.Kind == ZKind.Cannon && new Vector2(o.CenterX / 8f - (x0 + x1) * 0.5f, -o.CenterY / 8f - (z0 + z1) * 0.5f).magnitude < 15f) guns.Append(o.Type).Append(" #").Append(o.Id).Append(" ").Append(o.Owner).Append("; ");
            return "blue " + blue + " alive, " + blueAshore + " of " + blueSq + " blue squads with a robot ashore (" + ash.ToString().Trim() + "); red " + red + " alive; " + sb + (guns.Length > 0 ? "guns: " + guns : "");
        }

        // ================================================================== the frame hook, the restore, the summary
        bool blueAtZero;

        /// <summary>OnCaptureFrame, after the cues: the loungers, frame 0's blue robots and the catches.</summary>
        void IslandFrame(float t, int frame)
        {
            try
            {
                LoungerWatch(t, frame);
                if (!blueAtZero) { blueAtZero = true; BlueAtZero(t, frame); }
                StepCatches(t, frame);
            }
            catch (Exception e) { Note("WARNING island step: " + e.GetType().Name + " " + e.Message); }
        }

        void IslandRestore() { if (forceSet) { CrocodileView.ForceCatch = forceSave; forceSet = false; } }

        void IslandSummary()
        {
            if (loungers.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var l in loungers) sb.Append(l.s.tag).Append(" #").Append(l.o.Id).Append(l.upAt >= 0f ? " got up at " + l.upAt.ToString("F1") + " s" : l.state == "lying" ? " still lying at the end" : " " + l.state).Append("; ");
                Note("loungers (lounge_at): " + sb);
            }
            if (catchJobs.Count > 0)
            {
                int ok = 0, seen = 0; var sb = new StringBuilder();
                foreach (var j in catchJobs)
                {
                    if (j.state == "caught") { ok++; if (j.seen) seen++; }
                    sb.Append(j.no).Append(" ").Append(j.q.tag).Append(" ").Append(j.state == "caught" ? "caught at " + j.bitAt.ToString("F1") + " s" + (j.seen ? ", seen" : ", NOT seen") : j.state + " (" + j.why + ")").Append("; ");
                }
                Note("crocodile catches of the scenario: " + ok + " of " + catchJobs.Count + ", in the frame and seen " + seen + " (" + sb + "); scenario catches started in the rule " + W.CrocScenarioCatches);
            }
        }
    }
}
#endif
