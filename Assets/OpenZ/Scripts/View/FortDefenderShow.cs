using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// On the player's fort, CPU bots removed:
    ///  1. a blue heavy tank (unkillable, its splash cut to 24 px so it never reaches the ring) shells the fort from the east:
    ///     the defender pops out of the east ring's hatch: frames at 0.25x through the lid + climb;
    ///  2. an unarmed, unkillable blue grunt 10 m beyond the ring: he stands in the ring and shoots (grunt bolt, recoil): frames;
    ///  3. a blue grunt squad (armed) beyond the ring: they kill him: frames of the hit and the ragdoll at 0.25x;
    ///  4. the attack ends (everything blue removed): after the calm he goes back down, the lid closes: frames;
    /// each by day (0.08) and by night (0.62), camera 20 m and 34 m over his ring. The game runs 4x while nothing is being shot.
    /// plate, head vs the fort gun's barrel above the ring, body inside the 0.5 m free disc of the nest), shots / hits, the death,
    /// </summary>
    public class FortDefenderShow : MonoBehaviour
    {
        public static string Report = "idle", Tag = "after";
        public static void Run(string tag = "after") { Tag = tag; Report = "running"; new GameObject("FortDefenderShow").AddComponent<FortDefenderShow>(); }
        static string Records => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records"));

        ZGame g; ZWorld W; RtsCamera cam; DayNight dn; string dir; readonly StringBuilder sb = new StringBuilder(); int frames;
        readonly List<ZObject> staged = new List<ZObject>();

        ZObject Unit(string cat, string type, int x, int y, Team t)
        {
            var o = cat == "robot" ? W.CreateRobotGroup(type, x - 8, y - 8, t) : W.CreateObject(cat, type, x - 16, y - 16, t);
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            staged.Add(o); staged.AddRange(o.Minions);
            return o;
        }
        static void Unkillable(ZObject o) { foreach (var m in new List<ZObject>(o.Minions) { o }) { m.MaxHealth = 1000000000; m.SetHealth(m.MaxHealth); } }
        void Kill(ZObject o) { if (o == null) return; foreach (var m in new List<ZObject>(o.Minions) { o }) if (!m.Destroyed && W.Get(m.Id) == m) { m.SetHealth(0); W.UpdateObjectHealth(m); } }

        /// <summary>Nearest robot-passable dry tile centre to (px, py).</summary>
        (int x, int y) Spot(int px, int py)
        {
            int tx = px >> 4, ty = py >> 4;
            for (int r = 0; r <= 8; r++)
                for (int j = ty - r; j <= ty + r; j++)
                    for (int i = tx - r; i <= tx + r; i++)
                    {
                        if (Mathf.Max(Mathf.Abs(i - tx), Mathf.Abs(j - ty)) != r || !W.Path.TilePassable(i, j, true)) continue;
                        var c = W.Map.ClassAt(i, j); if (c == ZClass.Water || c == ZClass.DeepWater) continue;
                        return (i * 16 + 8, j * 16 + 8);
                    }
            return (px, py);
        }

        IEnumerator Shot(string name, Vector3 at, float h, float slow = 1f)
        {
            cam.focus = new Vector3(at.x, 0f, at.z) + Flat(cam.transform.forward).normalized * (at.y * 0.9f);   // the roof, not the fort's foot
            cam.SetHeightImmediate(h);
            if (slow < 1f) Time.timeScale = slow;
            yield return new WaitForSecondsRealtime(0.12f);
            ScreenCapture.CaptureScreenshot($"{dir}/fd_{Tag}_{name}_{h:0}.png"); frames++;
            yield return null;
            Time.timeScale = 1f;
        }
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        IEnumerator WaitSim(double seconds, float speed = 1f)
        {
            float was = g.gameSpeed; g.gameSpeed = speed; double t0 = W.Time;
            while (W.Time < t0 + seconds) yield return null;
            g.gameSpeed = was;
        }

        IEnumerator Start()
        {
            g = ZGame.I; W = g.World; W.Bots.Clear();
            cam = FindFirstObjectByType<RtsCamera>(); dn = FindFirstObjectByType<DayNight>();
            bool run0 = dn && dn.running; if (dn) dn.running = false;
            dir = Records + "/milestones/fort_defender"; System.IO.Directory.CreateDirectory(dir);
            sb.AppendLine($"# FortDefenderShow {Tag} {System.DateTime.Now:yyyy-MM-dd HH:mm} {g.level}\n");
            if (!ZWorld.FortDefenderDeviation || !FortDefenderView.I) { sb.AppendLine("DV41 is off (oz_fort_defender = 0 or 'Zod exact'): nothing to show"); Finish(run0); yield break; }
            var fort = W.Objects.Find(o => o.IsFort && o.Owner == g.player && !o.Destroyed);
            if (fort == null || !g.StaticViews.TryGetValue(fort.Id, out var fvGo) || !fvGo) { sb.AppendLine("no fort of the player's"); Finish(run0); yield break; }
            var fv = fvGo.transform;
            sb.AppendLine($"player fort {fort.Type} #{fort.Id} ({fort.Owner}); FortDefenderView roof colliders {FortDefenderView.RoofColliders}\n");
            sb.AppendLine("| pass | ring | created -> up (sim s) | sim ring vs drawn nest (m) | feet - plate (cm) | body radius (m, free 0.50) | head top / barrel low over the ring (m) | shots / hits | killed after (s) | respawn after (s) | down after calm (s) |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var (tod, tn) in new[] { (0.08f, "day"), (0.62f, "night") })
            {
                if (dn) { dn.timeOfDay = tod; dn.Apply(tod); }
                yield return Pass(fort, fv, tn);
            }
            Finish(run0);
        }

        IEnumerator Pass(ZObject fort, Transform fv, string tn)
        {
            // clear the fort's surroundings (enemy units within 20 tiles), wait until nobody is up
            foreach (var o in new List<ZObject>(W.Objects))
                if (o.IsUnit && !o.Destroyed && o.Owner != fort.Owner && o.Owner != Team.Neutral && Mathf.Abs(o.CenterX - fort.CenterX) < 320 && Mathf.Abs(o.CenterY - fort.CenterY) < 320) Kill(o);
            float guard = Time.realtimeSinceStartup;
            while (fort.FortDefenderMan != null && Time.realtimeSinceStartup - guard < 30f) { g.gameSpeed = 4f; yield return null; }
            g.gameSpeed = 1f;
            // 1. an east attack
            var (hx, hy) = Spot(fort.CenterX + 12 * 16, fort.CenterY);
            var heavy = Unit("vehicle", "heavy", hx, hy, fort.Owner == Team.Red ? Team.Blue : Team.Red); Unkillable(heavy); heavy.DamageRadius = 24;
            var enemy = heavy.Owner;
            W.SendWaypoints(enemy, heavy, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Attack, refId = fort.Id, x = fort.CenterX, y = fort.CenterY } });
            guard = Time.realtimeSinceStartup;
            while (fort.FortDefenderState != FortDefenderPhase.Rising && Time.realtimeSinceStartup - guard < 30f) yield return null;
            var man = fort.FortDefenderMan; int ring = fort.FortDefenderRing; double tCreated = W.Time;
            if (man == null) { sb.AppendLine($"| {tn} | - | no defender within 30 s | | | | | | | | |"); yield break; }
            var ringW = FortDefenderView.RingWorld(fv, ring, FortDefenderView.PlateTop, out _);
            // pop-out frames at 0.25x: lid opening, climbing, almost up
            foreach (var (at, nm) in new[] { (0.18, "pop_lid"), (0.6, "pop_climb"), (1.0, "pop_up") })
            {
                while (W.Time - tCreated < at) { Time.timeScale = 0.25f; yield return null; }
                yield return Shot($"{tn}_{nm}", ringW, 20f, 0.25f);
            }
            while (fort.FortDefenderState == FortDefenderPhase.Rising) yield return null;
            double tUp = W.Time;
            yield return new WaitForSeconds(0.4f);
            var view = g.Views.TryGetValue(man.Id, out var mv) ? mv : null;
            foreach (var h in new[] { 20f, 34f }) yield return Shot($"{tn}_standing", ringW, h);
            string where = Measure(fort, fv, ring, man, view, out string head);
            // 2. a target 10 m beyond the ring: he shoots
            fort.FortDefenderRingCentre(ring, out int rx, out int ry);
            int ox = rx - fort.CenterX, oy = ry - fort.CenterY; float ol = Mathf.Max(1f, Mathf.Sqrt(ox * ox + oy * oy));
            var (tx, ty) = Spot(rx + (int)(80 * ox / ol), ry + (int)(80 * oy / ol));
            var dummy = Unit("robot", "grunt", tx, ty, enemy); dummy.Damage = 0; dummy.MoveSpeed = 0; dummy.RealMoveSpeed = 0; Unkillable(dummy);
            int shots = 0, hits = 0; float t0 = Time.time; int shotFrames = 0; float lastFire = view ? view.LastFireAt : -100f;
            while (Time.time - t0 < 9f)
            {
                foreach (var e in W.Events) if (e.type == ZEventType.Hitscan && e.objId == man.Id) { shots++; if (e.flag) hits++; }
                if (view && view.LastFireAt != lastFire && shotFrames < 3) { lastFire = view.LastFireAt; yield return Shot($"{tn}_shooting{shotFrames++}", ringW, shotFrames == 1 ? 34f : 20f, 0.25f); }
                yield return null;
            }
            Kill(dummy);
            // 3. an armed squad kills him
            var (kx, ky) = Spot(rx + (int)(72 * ox / ol) + 16, ry + (int)(72 * oy / ol));
            var squad = Unit("robot", "grunt", kx, ky, enemy); Unkillable(squad);
            double tSquad = W.Time, tKill = -1; guard = Time.realtimeSinceStartup;
            while (!man.Destroyed && Time.realtimeSinceStartup - guard < 60f) { if (man.Health < man.MaxHealth / 5) Time.timeScale = 0.25f; yield return null; }
            if (man.Destroyed)
            {
                tKill = W.Time - tSquad;
                Time.timeScale = 0.25f;
                yield return Shot($"{tn}_killed_a", ringW, 20f, 0.25f);
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot($"{tn}_killed_b", ringW, 20f, 0.25f);
                yield return new WaitForSecondsRealtime(1.0f);
                yield return Shot($"{tn}_ragdoll", ringW, 34f, 0.25f);
                Time.timeScale = 1f;
            }
            Kill(squad);
            // 4. respawn after the cooldown (4x while waiting), then the attack ends: he goes down
            double tDead = W.Time, tRespawn = -1;
            g.gameSpeed = 4f; guard = Time.realtimeSinceStartup;
            while (fort.FortDefenderState != FortDefenderPhase.Up && Time.realtimeSinceStartup - guard < 30f) yield return null;
            g.gameSpeed = 1f;
            if (fort.FortDefenderMan != null) tRespawn = fort.FortDefenderPhaseAt - ZWorld.FortDefenderPop - tDead;
            Kill(heavy);
            double tLast = fort.LastEnemyHit, tLower = -1; g.gameSpeed = 4f; guard = Time.realtimeSinceStartup;
            while (fort.FortDefenderState != FortDefenderPhase.Lowering && Time.realtimeSinceStartup - guard < 30f) yield return null;
            g.gameSpeed = 1f;
            if (fort.FortDefenderState == FortDefenderPhase.Lowering)
            {
                tLower = W.Time - fort.LastEnemyHit;
                double tl = W.Time;
                while (W.Time - tl < 0.35) yield return null;
                yield return Shot($"{tn}_going_down", ringW, 20f, 0.25f);
                while (W.Time - tl < 0.9) yield return null;
                yield return Shot($"{tn}_lid_closing", ringW, 20f, 0.25f);
            }
            sb.AppendLine($"| {tn} | {ring} | {tUp - tCreated:0.00} | {where} | {head} | {shots} / {hits} | {(tKill >= 0 ? tKill.ToString("0.0") : "not killed")} | {(tRespawn >= 0 ? tRespawn.ToString("0.00") : "-")} | {(tLower >= 0 ? tLower.ToString("0.00") : "-")} |");
        }

        /// <summary>Where he stands: sim ring (px -> world) vs the drawn nest centre, his feet vs the lift plate, his body's radius
        /// round the nest centre (skinned bounds), his head top vs the lowest barrel vertex of the fort gun within 0.7 m of him.</summary>
        string Measure(ZObject fort, Transform fv, int ring, ZObject man, UnitView view, out string head)
        {
            head = "-";
            fort.FortDefenderRingCentre(ring, out int sx, out int sy);
            var sim = ZGame.PxToWorld(sx, sy); var drawn = FortDefenderView.RingWorld(fv, ring, FortDefenderView.PlateTop, out _);
            float off = new Vector2(sim.x - drawn.x, sim.z - drawn.z).magnitude;
            if (!view) return $"{off:0.00} | - | -";
            float feet = (view.transform.position.y - drawn.y) * 100f;
            float rad = 0f, top = float.NegativeInfinity;
            foreach (var r in view.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var b = r.bounds; top = Mathf.Max(top, b.max.y);
                foreach (var c in new[] { new Vector3(b.min.x, 0, b.min.z), new Vector3(b.max.x, 0, b.min.z), new Vector3(b.min.x, 0, b.max.z), new Vector3(b.max.x, 0, b.max.z) })
                    rad = Mathf.Max(rad, new Vector2(c.x - drawn.x, c.z - drawn.z).magnitude / 1.41421f);   // box corner -> ~radius
            }
            float low = float.PositiveInfinity;
            foreach (var mf in fv.GetComponentsInChildren<MeshFilter>())
            {
                if (!(mf.name == "FortGunRecoil" || mf.name == "FortGunPitch") || !mf.sharedMesh || !mf.sharedMesh.isReadable) continue;
                var M = mf.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices) { var w = M.MultiplyPoint3x4(v); if (new Vector2(w.x - drawn.x, w.z - drawn.z).magnitude < 0.7f) low = Mathf.Min(low, w.y); }
            }
            head = $"{top:0.00} / {(float.IsInfinity(low) ? "barrel not over him now" : low.ToString("0.00"))}";
            return $"{off:0.00} | {feet:+0.0;-0.0} | {rad:0.00}";
        }

        void Finish(bool run0)
        {
            foreach (var o in staged) Kill(o);
            Time.timeScale = 1f; if (g) g.gameSpeed = 1f;
            if (dn) { dn.timeOfDay = 0.08f; dn.Apply(0.08f); dn.running = run0; }
            sb.AppendLine($"\npops seen {FortDefenderView.PopsSeen}, pop-up lines {FortDefenderView.LinesSaid}, hatch / ladder sounds {FortDefenderView.LidSounds}; frames {frames} in milestones/fort_defender/");
            System.IO.File.WriteAllText($"{Records}/fort_defender_show.md", sb.ToString());
            Report = $"done: {frames} frames"; Destroy(gameObject);
        }
    }
}
