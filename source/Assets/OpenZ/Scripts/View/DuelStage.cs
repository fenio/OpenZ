using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Every order goes through the normal command layer (ZWorld.SendWaypoints); the rest is the sim: Zod's firing,
    /// Zod's DodgeMissile, the DV9 duel manoeuvres (ZObject.Duel.cs). Call from an editor script while playing:
    ///   OpenZ.DuelStage.Duel("heavy", "heavy");  OpenZ.DuelStage.Duel("jeep", "light");
    ///   OpenZ.DuelStage.RocketAt("heavy");       OpenZ.DuelStage.RocketAt("squad");
    ///   OpenZ.DuelStage.Report();                 (manoeuvre / dodge state of the staged units, also logged)
    /// within 14 tiles; the camera flies there.
    /// </summary>
    public static class DuelStage
    {
        static readonly List<ZObject> staged = new List<ZObject>();

        static ZWorld W => ZGame.I != null ? ZGame.I.World : null;
        static bool IsL17 => W != null && W.Map.width > 100;

        /// <summary>Open-ground centres (tiles): L02 field east of the centre (clear radius 5) and FightStage's south-west field;
        /// L17 west quays / car park (clear radius 8).</summary>
        static (int x, int y)[] Sites => IsL17 ? new[] { (23, 44), (18, 55), (66, 111) } : new[] { (49, 38), (27, 56), (50, 37) };

        static bool VehicleSpot(double px, double py)
        {
            var w = W; int lx = (int)px - 16, ly = (int)py - 16;
            if (!w.Pathing.Reach.BoxFree(lx, ly, 32, 32, false, false) || w.Pathing.DuelLaneAt((int)px, (int)py)) return false;
            foreach (var o in w.Objects)
                if (o.IsUnit && !o.Destroyed && Mathf.Abs(o.CenterX - (float)px) < 40 && Mathf.Abs(o.CenterY - (float)py) < 40) return false;
            return true;
        }

        static (int x, int y) PickSite(int tx, int ty)
        {
            if (tx >= 0 && ty >= 0) return (tx, ty);
            foreach (var s in Sites)
            {
                bool quiet = true;
                foreach (var o in W.Objects)
                    if (o.IsUnit && !o.Destroyed && o.Owner != Team.Neutral && Mathf.Abs(o.CenterX - (s.x * 16 + 8)) < 14 * 16 && Mathf.Abs(o.CenterY - (s.y * 16 + 8)) < 14 * 16) { quiet = false; break; }
                if (quiet) return s;
            }
            return Sites[0];
        }

        /// <summary>Two free vehicle spots `half` px either side of the site centre (angle search in 10 deg steps).</summary>
        static bool Pair(int sx, int sy, double half, out Vector2 a, out Vector2 b)
        {
            double cx = sx * 16 + 8, cy = sy * 16 + 8;
            foreach (double hf in new[] { half, half * 0.85, half * 0.7 })
                for (int k = 0; k < 36; k++)
                {
                    double an = (37 + 10 * k) * Mathf.Deg2Rad;
                    double ax = cx - System.Math.Cos(an) * hf, ay = cy - System.Math.Sin(an) * hf, bx = cx + System.Math.Cos(an) * hf, by = cy + System.Math.Sin(an) * hf;
                    if (VehicleSpot(ax, ay) && VehicleSpot(bx, by)) { a = new Vector2((float)ax, (float)ay); b = new Vector2((float)bx, (float)by); return true; }
                }
            a = b = default; return false;
        }

        static ZObject Vehicle(string type, Team team, Vector2 c)
        {
            var o = W.CreateObject("vehicle", type, Mathf.RoundToInt(c.x) - 16, Mathf.RoundToInt(c.y) - 16, team);
            ZGame.I.SpawnUnitView(o); staged.Add(o); return o;
        }

        static void Attack(ZObject u, ZObject t) =>
            W.SendWaypoints(u.Owner, u, new[] { new ZWaypoint { mode = WpMode.Attack, refId = t.Id, x = t.CenterX, y = t.CenterY } });

        static void Look(Vector2 px, float height)
        {
            var cam = Object.FindFirstObjectByType<RtsCamera>();
            if (!cam) return;
            cam.FlyTo(ZGame.PxToWorld(px.x, px.y)); cam.ZoomTo(height);
        }

        /// <summary>A red `redType` and a blue `blueType` vehicle 14 tiles apart on open ground, attack orders both ways (DV9:
        /// they manoeuvre and fire; Zod: they dodge each other's shells).</summary>
        public static string Duel(string redType, string blueType, int tileX = -1, int tileY = -1, float cameraHeight = 45f)
        {
            if (W == null) return "not playing";
            var (sx, sy) = PickSite(tileX, tileY);
            if (!Pair(sx, sy, 7 * 16, out var a, out var b)) return $"no free pair of vehicle spots around tile ({sx},{sy})";
            var r = Vehicle(redType, Team.Red, a); var bl = Vehicle(blueType, Team.Blue, b);
            Attack(r, bl); Attack(bl, r);
            Look((a + b) * 0.5f, cameraHeight);
            string msg = $"DuelStage: {r} vs {bl} at tile ({sx},{sy}); DV9 duel manoeuvres {(ZWorld.DuelDeviation ? "ON" : "OFF (Zod exact)")}";
            Debug.Log(msg); return msg;
        }

        /// <summary>Slow-rocket dodge demo: a blue Mobile Missile (shell 70 px/s, ~2 s of flight at 150 px) fires at a red heavy
        /// ("heavy") or at a red grunt squad ("squad") that is driving / walking across its front on a plain MOVE order: the
        /// target sidesteps every shell (Zod DodgeMissile: a straight run of 1.0-1.5 x flight time x its speed, x1.8 while it
        /// has stamina) and then carries on with its order. Robots only dodge while they are not shooting (Zod rule).</summary>
        public static string RocketAt(string what = "heavy", int tileX = -1, int tileY = -1, float cameraHeight = 50f)
        {
            if (W == null) return "not playing";
            var (sx, sy) = PickSite(tileX, tileY);
            if (!Pair(sx, sy, 75, out var a, out var b)) return $"no free pair of spots around tile ({sx},{sy})";
            var mlrs = Vehicle("missile_launcher", Team.Blue, b);
            ZObject target;
            if (what == "squad")
            {
                target = W.CreateRobotGroup("grunt", Mathf.RoundToInt(a.x) - 8, Mathf.RoundToInt(a.y) - 8, Team.Red);
                ZGame.I.SpawnUnitView(target); staged.Add(target);
                foreach (var m in target.Minions) { ZGame.I.SpawnUnitView(m); staged.Add(m); }
            }
            else target = Vehicle("heavy", Team.Red, a);
            // walk / drive across the launcher's front: perpendicular to the line launcher -> target, 12 tiles
            var d = (a - b).normalized; var side = new Vector2(-d.y, d.x);
            var goal = a + side * (12 * 16);
            W.SendWaypoints(Team.Red, target, new[] { ZWaypoint.Move(Mathf.RoundToInt(goal.x), Mathf.RoundToInt(goal.y), false) });
            Attack(mlrs, target);
            Look((a + b) * 0.5f, cameraHeight);
            string msg = $"DuelStage: {mlrs} fires at {target} crossing its front (Zod dodge demo)";
            Debug.Log(msg); return msg;
        }

        /// <summary>State of the staged units: order, manoeuvre (DV9), dodging right now (Zod), health.</summary>
        public static string Report()
        {
            if (W == null) return "not playing";
            var sb = new StringBuilder($"DuelStage t {W.Time:0.0}s:\n");
            foreach (var o in staged)
            {
                bool dodge = o.Waypoints.Count > 0 && o.Waypoints[0].mode == WpMode.Dodge;
                var st = o.DuelState;
                sb.Append($"  {o}: hp {o.Health}/{o.MaxHealth} {(o.Waypoints.Count > 0 ? o.Waypoints[0].mode.ToString() : "idle")}{(o.IsMoving ? " moving" : "")}{(o.AttackObject != null ? " firing" : "")}")
                  .Append(st != null ? $" | manoeuvre {o.DuelKind} #{o.DuelSeq} (total {st.Manoeuvres}, expired {st.Expired}, cut {st.ShortAborts}, resumed after dodge {st.Resumes})" : "")
                  .Append(dodge ? " | DODGING" : "").Append('\n');
            }
            Debug.Log(sb.ToString()); return sb.ToString();
        }
    }
}
