using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// pyro_fire_v2 staged show (Play mode, menu OpenZ/FX/Pyro Fire v2/Stage Fire Show): near the camera focus a red Pyro squad
    /// burns, for real through the sim (Zod damage, rolls, hit chance): a blue grunt squad (they melt), a blue light tank, then a
    /// and in the sim, ZObject.CanAttackObject: a Pyro never flames one, the step showed nothing). The targets do not shoot back.
    /// critic round 2: then a blue squad next to the nearest thing that can burn (decor prop / hut / crate / enemy building within
    /// 30 m): the drawn stream passes through it, so it must catch (visual) sticky fire (OpenZPyroFire.TouchAlong).
    /// By day, then again at night (NightLights.JumpTo .62), then the
    /// clock is put back. Every .25 s it samples OpenZPyroFire (jets, sprites, burning things, ground patches, real lights, sound
    /// </summary>
    public sealed class OpenZPyroFireShow : MonoBehaviour
    {
        public const float Duration = 64f;
        public static OpenZPyroFireShow Begin()
        {
            if (!ZGame.I || ZGame.I.World == null) return null;
            var s = ZGame.I.GetComponent<OpenZPyroFireShow>();
            if (s) return s;
            return ZGame.I.gameObject.AddComponent<OpenZPyroFireShow>();
        }

        ZGame g; ZWorld W;
        readonly StringBuilder log = new StringBuilder();
        int maxJets, maxSprites, maxBurning, maxPatches, maxLights, maxLoops, samples, touchBefore = -1, touchAfter = -1;
        float worstMs, sumMs; int frames;
        string root;

        void Start() { StartCoroutine(Run()); }

        ZObject Squad(string type, int tx, int ty, Team t) { var o = W.CreateRobotGroup(type, tx * 16, ty * 16, t); g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m); return o; }
        ZObject Unit(string cat, string type, int tx, int ty, Team t) { var o = W.CreateObject(cat, type, tx * 16, ty * 16, t); g.SpawnUnitView(o); return o; }
        static void Passive(ZObject o) { o.AttackRadius = 0; o.Damage = 0; foreach (var m in o.Minions) { m.AttackRadius = 0; m.Damage = 0; } }
        void Order(ZObject u, ZObject target)
        {
            if (u == null || target == null) return;
            var wp = new ZWaypoint { mode = WpMode.Attack, refId = target.Id, x = target.CenterX, y = target.CenterY, attackTo = true };
            bool ok = W.SendWaypoints(u.Owner, u, new[] { wp });
            log.AppendLine($"- {Time.time:F1} s: order {u} attack {target} -> {(ok ? "ok" : "refused")}");
        }
        (int, int) Spot(int tx, int ty, bool vehicle)
        {
            for (int r = 0; r < 7; r++)
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                    {
                        int x = tx + dx, y = ty + dy;
                        if (!W.Path.TilePassable(x, y, false)) continue;
                        if (vehicle && !(W.Path.TilePassable(x + 1, y, false) && W.Path.TilePassable(x, y + 1, false) && W.Path.TilePassable(x + 1, y + 1, false))) continue;
                        return (x, y);
                    }
            return (tx, ty);
        }

        IEnumerator Watch(float seconds)
        {
            float t = 0f, next = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime; float ms = Time.unscaledDeltaTime * 1000f; worstMs = Mathf.Max(worstMs, t > 0.5f ? ms : 0f); sumMs += ms; frames++;
                var p = OpenZPyroFire.I;
                if (p && t >= next)
                {
                    next = t + 0.25f; samples++;
                    maxJets = Mathf.Max(maxJets, p.Jets); maxSprites = Mathf.Max(maxSprites, p.Sprites); maxBurning = Mathf.Max(maxBurning, p.Burning);
                    maxPatches = Mathf.Max(maxPatches, p.Patches); maxLights = Mathf.Max(maxLights, p.LightsOn); maxLoops = Mathf.Max(maxLoops, p.LoopsOn);
                }
                yield return null;
            }
        }

        void Shot(string name)
        {
            try
            {
                var dir = System.IO.Path.Combine(root, "work", "records", "milestones"); System.IO.Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, $"pyro_fire_v2_{name}_{g.level}_{System.DateTime.Now:HHmm}.png");
                ScreenCapture.CaptureScreenshot(path);
                log.AppendLine($"- {Time.time:F1} s: shot {path}");
            }
            catch (System.Exception ex) { log.AppendLine("- shot failed: " + ex.Message); }
        }

        IEnumerator Run()
        {
            g = ZGame.I; W = g.World;
            root = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../.."));
            float t0 = Time.unscaledTime;
            var rts = FindFirstObjectByType<RtsCamera>();
            var focus = rts ? rts.focus : Vector3.zero;
            if (rts) { rts.SetHeightImmediate(34f); rts.Place(); }
            int fx = Mathf.RoundToInt(focus.x * 8f / 16f), fy = Mathf.RoundToInt(-focus.z * 8f / 16f);
            var dn = FindFirstObjectByType<DayNight>(); float tod = dn ? dn.timeOfDay : 0.08f; bool running = dn && dn.running;
            if (dn) { dn.running = false; NightLights.JumpTo(0.08f); }
            log.AppendLine($"# pyro_fire_v2 show, {g.level}, {System.DateTime.Now:yyyy-MM-dd HH:mm}");
            log.AppendLine($"focus tile ({fx},{fy}), camera 34 m, day (t .08) then night (t .62); the targets do not shoot back.");
            log.AppendLine();
            var (px, py) = Spot(fx - 4, fy, false);
            var pyros = Squad("pyro", px, py, Team.Red);
            var (gx, gy) = Spot(fx + 2, fy + 1, false);
            var grunts = Squad("grunt", gx, gy, Team.Blue); Passive(grunts);
            var (vx, vy) = Spot(fx + 2, fy - 3, true);
            var tank = Unit("vehicle", "light", vx, vy, Team.Blue); Passive(tank);
            var (cx, cy) = Spot(fx + 3, fy + 4, true);
            var gat = Unit("cannon", "gatling", cx, cy, Team.Blue); Passive(gat);                // rigid-box sticky fire on a gun (+ its seated gunner)
            yield return new WaitForSeconds(1.5f);
            Order(pyros, grunts);
            yield return Watch(4f); Shot("a_grunts_day");
            yield return Watch(5f);
            if (W.Get(tank.Id) != null) Order(pyros, tank);
            yield return Watch(5f); Shot("b_tank_day");
            yield return Watch(4f);
            // a blue gun: sticky fire on a rigid hull that is not a vehicle (buildings cannot be flamed in Zod: explosive-only)
            if (W.Get(gat.Id) != null) { Order(pyros, gat); yield return Watch(7f); Shot("b2_cannon_day"); yield return Watch(2f); }
            else log.AppendLine("- the gatling was gone before its turn: cannon case skipped");
            var pf = OpenZPyroFire.I;
            if (pf && pf.NearestTouchable(focus, 30f, out var tp))
            {
                // only, so a prop north or south of the Pyros was not in the stream and the check could fail with a working feature)
                var pyroAt = g.Views.TryGetValue(pyros.Id, out var pview) && pview ? pview.transform.position : ZGame.PxToWorld(px * 16 + 8, py * 16 + 8);
                var away = tp - pyroAt; away.y = 0f; away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.right;
                var beyond = tp + away * 2.6f;
                var (tx, ty) = Spot(Mathf.RoundToInt(beyond.x / 2f), Mathf.RoundToInt(-beyond.z / 2f), false);   // on the far side from the Pyros
                var grunts3 = Squad("grunt", tx, ty, Team.Blue); Passive(grunts3);
                log.AppendLine($"- {Time.time:F1} s: touch case: flammable thing at ({tp.x:F1}, {tp.z:F1}), blue grunts at tile ({tx},{ty})");
                touchBefore = pf.TouchIgnites;
                yield return new WaitForSeconds(0.5f);
                Order(pyros, grunts3);
                yield return Watch(8f); Shot("b3_touch_day"); yield return Watch(2f);
                touchAfter = pf.TouchIgnites;
            }
            else log.AppendLine("- no flammable prop / building within 30 m of the focus: touch case skipped");
            // night
            if (dn) NightLights.JumpTo(0.62f);
            var (gx2, gy2) = Spot(fx + 1, fy + 3, false);
            var grunts2 = Squad("grunt", gx2, gy2, Team.Blue); Passive(grunts2);
            yield return new WaitForSeconds(0.5f);
            Order(pyros, grunts2);
            yield return Watch(5f); Shot("c_night");
            yield return Watch(6f);
            if (dn) { dn.timeOfDay = tod; dn.Apply(tod); dn.running = running; }

            var p = OpenZPyroFire.I;
            var rep = new StringBuilder();
            int pass = 0, fail = 0;
            void Check(string name, bool ok, string detail) { rep.AppendLine($"- {(ok ? "PASS" : "FAIL")} {name}: {detail}"); if (ok) pass++; else fail++; }
            Check("system live", p && p.Status == "ok", p ? p.Status : "OpenZPyroFire missing (no pyro shot reached it)");
            Check("jets drawn", maxJets >= 1 && p && p.Shots > 0, $"max {maxJets} jets at once, {(p ? p.Shots : 0)} drawn Zod shots");
            Check("flame sprites", maxSprites >= 40, $"peak {maxSprites} (pool {(p ? p.maxSprites : 0)}, dropped {(p ? p.SpritesDropped : 0)})");
            Check("sticky fire on targets", maxBurning >= 1, $"max {maxBurning} burning things at once");
            Check("burning ground", maxPatches >= 1, $"max {maxPatches} ground patches at once");
            if (touchBefore >= 0) Check("sticky fire on things it touches", touchAfter > touchBefore, $"{touchAfter - touchBefore} props / buildings set alight by the stream ({(p ? p.TouchRegistered : 0)} can burn on this level)");
            else rep.AppendLine("- INFO sticky fire on things it touches: no flammable thing near the focus, not staged (move the camera next to cacti / crates / sandbags and run again)");
            Check("real lights on the fire", maxLights >= 1, $"max {maxLights} point lights on");
            Check("flame loop through AudioDirector", p && p.LoopsStarted > 0, $"{(p ? p.LoopsStarted : 0)} loops started, max {maxLoops} fire loops at once");
            var sl = OzSoundLog.Since(t0);
            int starts = 0, stops = 0, pyLoops = 0; var lines = new List<OzSoundLog.Entry>();
            foreach (var e in sl)
            {
                if (e.key == "wpn_pyro_flame_start" && e.outcome.StartsWith("PLAY")) starts++;
                if (e.key == "wpn_pyro_flame_stop" && e.outcome.StartsWith("PLAY")) stops++;
                if (e.src == "PY" && e.outcome.StartsWith("PLAY")) pyLoops++;
                if (e.key != null && (e.key.Contains("pyro") || e.key.Contains("flame") || e.key == "dst_fire_loop")) lines.Add(e);
            }
            Check("ignite + stop sounds", starts > 0 && stops > 0, $"{starts} ignites, {stops} stops played");
            float avg = frames > 0 ? sumMs / frames : 0f;
            Check("frame time", avg < 20f, $"average {avg:F1} ms, worst {worstMs:F1} ms over {frames} frames (60 fps cap = 16.7)");
            var outp = new StringBuilder();
            outp.AppendLine($"RESULT: {(fail == 0 ? "PASS" : "FAIL")} ({pass} pass, {fail} fail)").AppendLine();
            outp.Append(rep).AppendLine();
            outp.AppendLine("## Timeline").Append(log).AppendLine();
            outp.AppendLine("## End state").AppendLine(p ? p.Diagnostics() : "-").AppendLine();
            outp.AppendLine("## Pyro sound log").AppendLine("```").Append(OzSoundLog.Format(lines)).AppendLine("```");
            var dir = System.IO.Path.Combine(root, "work", "records", "qc"); System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, $"pyro_fire_v2_show_{g.level}_{System.DateTime.Now:HHmm}.md");
            System.IO.File.WriteAllText(path, outp.ToString());
            Debug.Log($"[PyroFireShow] {(fail == 0 ? "PASS" : "FAIL")} {pass}/{pass + fail} -> {path}");
            Destroy(this);
        }
    }
}
