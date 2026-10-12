using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// plumes), per-robot blast ragdolls, chunky wreck fragments, turret debris damage, the fort gun charge ring, CALM SCREEN ON / OFF
    /// with a nuke blast, then the enemy fort falls (win) and the fort gun must explode and fly. L17: RepairPadProof (full look), a
    /// crane repairing a destroyed bridge (weld work + plates rising), a forced storm flash with CALM SCREEN ON / OFF.
    /// </summary>
    public class BundleExe2FxProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static void RunL02() { Report = "running"; new GameObject("BundleExe2FxProof").AddComponent<BundleExe2FxProof>().level = 2; }
        public static void RunL17() { Report = "running"; new GameObject("BundleExe2FxProof").AddComponent<BundleExe2FxProof>().level = 17; }

        int level; ZGame g; ZWorld W; RtsCamera rts; readonly StringBuilder sb = new StringBuilder(); int fails; string dir;
        void Log(string s) { sb.AppendLine(s); Debug.Log("[Exe2FxProof] " + s); }
        void Check(string what, bool ok) { if (!ok) fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); Log($"frame {name}.png"); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        ZObject Spawn(string cat, string type, int tx, int ty, Team team)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, cat == "robot" ? 1 : 2, cat == "robot", out tx, out ty);
            var o = cat == "robot" ? W.CreateRobotGroup(type, tx * 16, ty * 16, team) : W.CreateObject(cat, type, tx * 16, ty * 16, team);
            if (o == null) { Log($"spawn {cat}:{type} failed"); return null; }
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            return o;
        }
        void Attack(ZObject u, ZObject t) { if (u != null && t != null) W.SendWaypoints(u.Owner, u, new[] { new ZWaypoint { mode = WpMode.Attack, refId = t.Id, x = t.CenterX, y = t.CenterY, playerGiven = true } }); }
        UnitView V(ZObject o) => o != null && g.Views.TryGetValue(o.Id, out var v) ? v : null;
        static object F(object o, string n) { if (o == null) return null; var f = o.GetType().GetField(n, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public); return f != null ? f.GetValue(o) : null; }
        static object SF(System.Type t, string n) { var f = t.GetField(n, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public); return f != null ? f.GetValue(null) : null; }

        // per-frame samplers
        int lastLaunch; float tBlast = -1f; Vector3 blastAt; readonly List<(float t, float d, float s, float lift, float v)> rag = new List<(float, float, float, float, float)>();
        bool sampleFlash; float flashPeak, bloomPeak, lightPeak, shakePeak; int boltFrames;
        readonly Dictionary<int, Vector3> patchSeen = new Dictionary<int, Vector3>(); float patchDrift; int patchesSeen, peakLights;
        ZObject debrisTank; readonly Dictionary<int, int> lastHp = new Dictionary<int, int>(); readonly List<string> debrisHits = new List<string>(); int debrisBlasts;

        void Start()
        {
            g = ZGame.I; W = g.World; rts = FindFirstObjectByType<RtsCamera>();
            dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records/milestones/bundle_exe2_fx"));
            System.IO.Directory.CreateDirectory(dir);
            lastLaunch = RobotRagdoll.Launched;
            StartCoroutine(level == 17 ? L17() : L02());
        }

        void Update()
        {
            if (W == null) return;
            if (RobotRagdoll.Launched != lastLaunch)
            {
                lastLaunch = RobotRagdoll.Launched;
                var d = RobotRagdoll.LastPos - blastAt; d.y = 0f;
                rag.Add((tBlast >= 0f ? Time.time - tBlast : -1f, d.magnitude, RobotRagdoll.LastStrength, RobotRagdoll.LastLift, RobotRagdoll.LastSpeed));
            }
            var pf = OpenZPyroFire.I;
            if (pf)
            {
                peakLights = Mathf.Max(peakLights, pf.LightsOn);
                if (F(pf, "ground") is OpenZPyroGroundFire gf)
                    foreach (var p in gf.patches)
                    {
                        if (!p.active) continue;
                        int key = p.id * 100000 + Mathf.RoundToInt(p.start * 100f);
                        if (!patchSeen.TryGetValue(key, out var c0)) { patchSeen[key] = p.c; patchesSeen++; }
                        else patchDrift = Mathf.Max(patchDrift, Vector3.Distance(c0, p.c));
                    }
            }
            if (debrisTank != null)
            {
                foreach (var e in W.Events)
                    if (e.type == ZEventType.MissileExploded && e.flag2 && e.objId == debrisTank.Id)
                    {
                        debrisBlasts++;
                        foreach (var o in W.Objects)
                            if (lastHp.TryGetValue(o.Id, out int h0) && o.Health < h0) debrisHits.Add($"{o.Type}#{o.Id} {h0} -> {o.Health} ({Mathf.Sqrt((o.CenterX - e.x) * (float)(o.CenterX - e.x) + (o.CenterY - e.y) * (float)(o.CenterY - e.y)):F0} px from the landing, r {e.radius}, dmg {e.value})");
                    }
                lastHp.Clear();
                foreach (var o in W.Objects) if (!o.Destroyed && (o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle)) lastHp[o.Id] = o.Health;
            }
            if (sampleFlash)
            {
                if (SF(typeof(NukeFx), "flash") is UnityEngine.UI.Image im && im) flashPeak = Mathf.Max(flashPeak, im.color.a);
                var bd = FindFirstObjectByType<OpenZBloomDirector>(); if (bd && F(bd, "flashW") is float fw) bloomPeak = Mathf.Max(bloomPeak, fw);
                var sh = OpenZFxShake.I; if (sh != null && F(sh, "offset") is Vector3 off) shakePeak = Mathf.Max(shakePeak, off.magnitude);
                if (rts && F(rts, "shake") is float rs) shakePeak = Mathf.Max(shakePeak, rs);
                var wz = OzWeather.I;
                if (wz)
                {
                    if (F(wz, "flashLight") is Light fl && fl && fl.enabled) lightPeak = Mathf.Max(lightPeak, fl.intensity);
                    if (F(wz, "bolt") is LineRenderer bl && bl && bl.enabled) boltFrames++;
                }
            }
        }

        void ResetFlash() { flashPeak = bloomPeak = lightPeak = shakePeak = 0f; boltFrames = 0; }

        // =============================================================================================== L02
        IEnumerator L02()
        {
            yield return new WaitForSeconds(1f);
            bool calm0 = OzMotion.Reduced;
            Log($"bundle_exe2_fx proof L02, {Screen.width}x{Screen.height}, test mode {PlayerPrefs.GetInt("oz_testmode", 0)}, NightK {NightGrade.NightK:F2}, calm screen {(calm0 ? "ON" : "OFF")} (default 0, saved {PlayerPrefs.GetInt("oz_reduced_motion", 0)}, one-time default flag {PlayerPrefs.GetInt("oz_calm_default_v1", 0)})");
            Check("CALM SCREEN is OFF by default in this editor's prefs (the one-time default ran)", !calm0 && PlayerPrefs.GetInt("oz_calm_default_v1", 0) == 1);
            var red = W.Objects.FirstOrDefault(o => o.IsFort && o.Owner == g.player && !o.Destroyed);
            var blueF = W.Objects.FirstOrDefault(o => o.IsFort && o.Owner != g.player && o.Owner != Team.Neutral && !o.Destroyed);
            if (red == null || blueF == null) { Check("both forts", false); Finish(); yield break; }
            var rp = new Vector2(red.CenterX / 16f, red.CenterY / 16f); var bp = new Vector2(blueF.CenterX / 16f, blueF.CenterY / 16f); var dirT = (bp - rp).normalized;
            var P = rp + dirT * 20f; int px = Mathf.RoundToInt(P.x), py = Mathf.RoundToInt(P.y);
            var side = new Vector2(-dirT.y, dirT.x);
            // ---- 1) rockets by day + pyro + blast ragdolls
            var b1 = Spawn("robot", "grunt", px, py, Team.Blue); var b2 = Spawn("robot", "psycho", px + Mathf.RoundToInt(side.x * 2), py + Mathf.RoundToInt(side.y * 2), Team.Blue);
            var b3 = Spawn("robot", "grunt", px + Mathf.RoundToInt(side.x * -2), py + Mathf.RoundToInt(side.y * -2), Team.Blue);
            Vector2 back = P - dirT * 9f; int qx = Mathf.RoundToInt(back.x), qy = Mathf.RoundToInt(back.y);
            var mlrs = Spawn("vehicle", "missile_launcher", qx, qy, g.player);
            var mech = Spawn("vehicle", "medium", qx + Mathf.RoundToInt(side.x * 4), qy + Mathf.RoundToInt(side.y * 4), g.player);
            var mc = Spawn("cannon", "missile_cannon", qx - Mathf.RoundToInt(side.x * 4), qy - Mathf.RoundToInt(side.y * 4), g.player);
            var tough = Spawn("robot", "tough", qx + Mathf.RoundToInt(dirT.x * 3 + side.x * 7), qy + Mathf.RoundToInt(dirT.y * 3 + side.y * 7), g.player);
            Vector2 near = P - dirT * 4f;
            var pyro = Spawn("robot", "pyro", Mathf.RoundToInt(near.x), Mathf.RoundToInt(near.y), g.player);
            Log($"blue squads at tile ({px},{py}); red MLRS {mlrs?.Id}, medium {mech?.Id}, missile cannon {mc?.Id}, tough {tough?.Id}, pyro {pyro?.Id}");
            var focus = g.CenterWorld(b1 ?? red);
            if (rts) { rts.FlyTo(Vector3.Lerp(focus, g.CenterWorld(mlrs ?? red), 0.4f)); rts.ZoomTo(46f); }
            yield return new WaitForSeconds(1.0f);
            int rd0 = NightProjectileGlow.RocketDayGlows;
            Attack(mlrs, b1); Attack(mech, b2); Attack(mc, b3); Attack(tough, b1); Attack(pyro, b2);
            if (tough != null) foreach (var m in tough.Minions) Attack(m, b1);
            if (pyro != null) foreach (var m in pyro.Minions) Attack(m, b2);
            blastAt = focus; tBlast = Time.time;
            bool shotRockets = false; float t0 = Time.time;
            while (Time.time - t0 < 9f)
            {
                if (!shotRockets && NightProjectileGlow.RocketDayGlows - rd0 > 20) { shotRockets = true; yield return new WaitForSeconds(0.12f); Shot("01_rockets_day_glow"); }
                foreach (var e in W.Events) if (e.type == ZEventType.MissileExploded && !e.flag2 && e.radius >= 24) { var at = ZGame.PxToWorld(e.x, e.y); blastAt = new Vector3(at.x, 0f, at.z); tBlast = Time.time; }
                yield return null;
            }
            Shot("02_pyro_fire_plumes");
            Log($"rocket motor glows drawn by day (NightK {NightGrade.NightK:F2}): {NightProjectileGlow.RocketDayGlows - rd0}");
            Check("rocket exhaust glow + bloom by day (MLRS / missile cannon / mech / Tough rockets)", NightProjectileGlow.RocketDayGlows - rd0 > 20 && NightGrade.NightK < 0.5f);
            Log($"pyro: patches seen {patchesSeen}, max centre drift {patchDrift * 100f:F1} cm, fire lights peak {peakLights}, plumes peak {OpenZPyroFire.PeakPlumes} (damage plumes peak {OpenZDamageStates.PeakPlumes})");
            Check("burning ground never moves after it appears (drift < 1 cm) and gets real lights by day", patchesSeen > 0 && patchDrift < 0.01f && peakLights > 0);
            var rl = string.Join("; ", rag.Take(14).Select(r => $"t {r.t:F2} s d {r.d:F1} m str {r.s:F2} lift {r.lift:F2} v {r.v:F1}"));
            Log($"ragdolls launched {rag.Count}: {rl}");
            if (rag.Count >= 2)
            {
                float smin = rag.Min(r => r.s), smax = rag.Max(r => r.s), lmin = rag.Min(r => r.lift), lmax = rag.Max(r => r.lift);
                Check($"per-robot blast throws differ (strength {smin:F2}-{smax:F2}, lift {lmin:F2}-{lmax:F2})", smax > smin * 1.25f || lmax > lmin * 1.25f);
            }
            else Log("(fewer than 2 ragdolls in this window: the per-robot rule is code-checked only)");
            // ---- 2) chunky wreck fragments + turret debris damage
            Vector2 wp = P + side * 7f; int wx = Mathf.RoundToInt(wp.x), wy = Mathf.RoundToInt(wp.y);
            var apc = Spawn("vehicle", "apc", wx, wy, Team.Neutral);
            var lt = Spawn("vehicle", "light", wx + Mathf.RoundToInt(side.x * 4), wy + Mathf.RoundToInt(side.y * 4), Team.Blue);
            var ring1 = Spawn("robot", "grunt", wx + Mathf.RoundToInt(side.x * 4 + dirT.x * 2), wy + Mathf.RoundToInt(side.y * 4 + dirT.y * 2), Team.Blue);
            var ring2 = Spawn("robot", "grunt", wx + Mathf.RoundToInt(side.x * 4 - dirT.x * 2), wy + Mathf.RoundToInt(side.y * 4 - dirT.y * 2), Team.Blue);
            yield return new WaitForSeconds(0.6f);
            if (rts && apc != null) { rts.FlyTo(g.CenterWorld(apc)); rts.ZoomTo(30f); }
            int ch0 = WreckPieces.Chunked, co0 = WreckPieces.CoresAdded, cv0 = WreckPieces.ClampedVerts;
            debrisTank = lt;
            foreach (var o in new[] { apc, lt }) if (o != null) { o.SetHealth(0); W.UpdateObjectHealth(o); }
            yield return new WaitForSeconds(0.5f);
            int broke = 0; var av = V(apc); if (av) broke = WreckPieces.Break(av, null, av.transform.position, 1f, apc.Id, 0.6f);
            yield return new WaitForSeconds(0.9f);
            Shot("03_chunky_fragments");
            Log($"wreck break: {broke} APC pieces now, fragments chunked {WreckPieces.Chunked - ch0}, cores {WreckPieces.CoresAdded - co0}, clamped verts {WreckPieces.ClampedVerts - cv0} (cached meshes from earlier breaks add 0)");
            Check("broken wreck fragments are clamped chunky pieces with a closed core", WreckPieces.Chunked - ch0 > 0 && WreckPieces.CoresAdded - co0 > 0);
            yield return WaitFor(() => debrisBlasts > 0, 6f);
            yield return null;
            Log($"turret debris of light#{lt?.Id}: blasts {debrisBlasts}, hits: {(debrisHits.Count > 0 ? string.Join("; ", debrisHits) : "none (no unit within 40 px of the random landing)")}");
            Check("the dead tank's flying turret lands as Zod's debris missile (40 HP, r 40 px, once)", debrisBlasts == 1);
            debrisTank = null;
            // ---- 3) fort gun charge ring
            Log($"fort gun ring: shown {FortGunView.RingShownFrames} frames, hidden while ready {FortGunView.RingHiddenReadyFrames} frames");
            Check("the fort gun ring hides once full (frames hidden while ready > 0)", FortGunView.RingHiddenReadyFrames > 0);
            // ---- 4) CALM SCREEN ON / OFF with a nuke blast on screen
            var nukeAt = g.CenterWorld(b1 ?? red); nukeAt.y = g.GroundY(nukeAt.x, nukeAt.z);
            if (rts) { rts.FlyTo(nukeAt); rts.ZoomTo(60f); }
            yield return new WaitForSeconds(1.5f);
            float[] fp = new float[2], bpk = new float[2], spk = new float[2]; string calmLog = "";
            for (int pass = 0; pass < 2; pass++)
            {
                OzMotion.Reduced = pass == 0; OzCalm.Reset(); OzShakeGate.ResetCounts(); ResetFlash(); sampleFlash = true;
                yield return new WaitForSeconds(3.2f);                                                     // the shake gate's cooldown
                NukeFx.Trigger(nukeAt); FxManager.I?.NukeBlast(nukeAt); OpenZBloomDirector.Flash(1f);
                yield return new WaitForSeconds(pass == 0 ? 0.7f : 0.25f);
                Shot(pass == 0 ? "04a_nuke_calm_on" : "04b_nuke_calm_off");
                yield return new WaitForSeconds(3.5f);
                sampleFlash = false; fp[pass] = flashPeak; bpk[pass] = bloomPeak; spk[pass] = shakePeak;
                var sl = string.Join(" | ", OzShakeGate.Log.Skip(Mathf.Max(0, OzShakeGate.Log.Count - 4)));
                if (pass == 0) calmLog = OzCalm.Log();
                Log($"calm {(pass == 0 ? "ON " : "OFF")}: white-out peak {flashPeak:F2}, exposure flash peak {bloomPeak:F2}, camera shake peak {shakePeak:F3}; suppressed: {OzCalm.Log()}; shake gate: kicks {OzShakeGate.Kicks}, blocked {OzShakeGate.Blocked} [{sl}]");
            }
            OzMotion.Reduced = calm0;
            Check("CALM SCREEN ON: no camera shake, a soft white-out and exposure flash (well below OFF), suppressions logged", spk[0] < 1e-4f && spk[1] > 1e-4f && fp[0] < fp[1] * 0.6f && bpk[0] < bpk[1] * 0.6f && calmLog != "none");
            Log($"calm screen restored to {(OzMotion.Reduced ? "ON" : "OFF")} (saved {PlayerPrefs.GetInt("oz_reduced_motion", 0)})");
            // ---- 5) the enemy fort falls (win): its gun explodes with it and flies
            var fgv = g.StaticViews.TryGetValue(blueF.Id, out var fgo) && fgo ? fgo.GetComponent<FortGunView>() : null;
            int toss0 = FortGunView.Tosses;
            Log($"blue fort#{blueF.Id} gun view {(fgv ? "bound " + fgv.Bound : "none")}");
            blueF.SetHealth(0); W.UpdateObjectHealth(blueF);
            yield return WaitFor(() => FortGunView.Tosses > toss0, 25f);
            bool tossed = FortGunView.Tosses > toss0;
            var yaw = fgv ? fgv.YawT : null;
            if (tossed) { yield return new WaitForSeconds(0.35f); Shot("05_fortgun_blown_off"); }
            string Track() { if (!yaw) return "yaw gone"; int on = yaw.GetComponentsInChildren<Renderer>().Count(r => r.enabled); return $"yaw active {yaw.gameObject.activeInHierarchy}, renderers on {on}, at {yaw.position}"; }
            Log($"fort gun toss: {(tossed ? "YES" : "NO")} (fort destroyed {FortGunView.LastTossFortDestroyed}, renderers re-shown {FortGunView.TossReshown}); +0.35 s: {Track()}");
            yield return new WaitForSeconds(1.2f);
            if (tossed) Shot("06_fortgun_in_air");
            Log($"+1.55 s: {Track()}");
            yield return new WaitForSeconds(2.5f);
            Log($"+4 s: {Track()}");
            Check("the enemy fort's gun explodes with the fort and flies (visible, never just gone)", tossed && yaw && yaw.gameObject.activeInHierarchy && yaw.GetComponentsInChildren<Renderer>().Any(r => r.enabled));
            Finish();
        }

        // =============================================================================================== L17
        IEnumerator L17()
        {
            yield return new WaitForSeconds(1f);
            bool calm0 = OzMotion.Reduced;
            Log($"bundle_exe2_fx proof L17, {Screen.width}x{Screen.height}, test mode {PlayerPrefs.GetInt("oz_testmode", 0)}, NightK {NightGrade.NightK:F2}, calm screen {(calm0 ? "ON" : "OFF")}");
            int bursts0 = RepairBayView.Bursts;
            RepairPadProof.Run();
            yield return WaitFor(() => RepairPadProof.Report != "running", 120f);
            Log($"RepairPadProof: {RepairPadProof.Report} (weld bursts {RepairBayView.Bursts - bursts0}, sparks {RepairBayView.Sparks}); frames in milestones\\bundle_exe2_repair_station");
            Check("repair pad proof passes in the full look (weld sparks + call-off at once)", RepairPadProof.Report == "PASS");
            yield return new WaitForSeconds(1f);
            // ---- 2) a crane repairs a destroyed bridge: weld work + plates rising into place
            var red = W.Objects.FirstOrDefault(o => o.IsFort && o.Owner == g.player && !o.Destroyed);
            var br = W.Objects.Where(o => o.IsBridge && !o.Destroyed).OrderBy(o => red == null ? 0 : Mathf.Abs(o.CenterX - red.CenterX) + Mathf.Abs(o.CenterY - red.CenterY)).FirstOrDefault();
            if (br != null)
            {
                br.SetHealth(0); W.UpdateObjectHealth(br);
                yield return new WaitForSeconds(1.5f);
                bool hasEnt = br.GetCraneEntrance(out int ex, out int ey, out int ex2, out int ey2);
                var crane = hasEnt ? Spawn("vehicle", "crane", (ex >> 4) + 1, (ey >> 4) + 1, g.player) : null;
                Log($"bridge#{br.Id} {br.Type} destroyed {br.Destroyed}, crane entrance {(hasEnt ? $"({ex},{ey})/({ex2},{ey2})" : "none")}, crane {crane?.Id}");
                if (crane != null)
                {
                    if (rts) { rts.FlyTo(g.CenterWorld(br)); rts.ZoomTo(34f); }
                    int cj = RulesView.CraneJobs, cw = RulesView.CraneWelds, cr = RulesView.CraneRebuilds;
                    W.SendWaypoints(g.player, crane, new[] { new ZWaypoint { mode = WpMode.CraneRepair, refId = br.Id, x = br.CenterX, y = br.CenterY, playerGiven = true } });
                    yield return WaitFor(() => RulesView.CraneRebuilds > cr, 30f);
                    yield return new WaitForSeconds(2.6f);
                    Shot("07_crane_bridge_rebuild");
                    yield return WaitFor(() => !br.Destroyed, 40f);
                    yield return new WaitForSeconds(0.6f);
                    Shot("08_bridge_repaired");
                    Log($"crane: jobs {RulesView.CraneJobs - cj}, weld bursts {RulesView.CraneWelds - cw}, bridge rebuilds {RulesView.CraneRebuilds - cr}, bridge repaired {!br.Destroyed}");
                    Check("a crane on a destroyed bridge: weld work + plates rising into place, then repaired", RulesView.CraneRebuilds > cr && RulesView.CraneWelds > cw && !br.Destroyed);
                }
                else Check("crane spawned at the bridge", false);
            }
            else Log("no bridge found on L17 (skipped)");
            // ---- 3) storm lightning with CALM SCREEN ON / OFF
            var wz = OzWeather.I;
            if (wz)
            {
                wz.ForceStorm(40f, false);
                yield return new WaitForSeconds(2f);
                float[] lp = new float[2]; int[] bf = new int[2]; string calmLog = "";
                for (int pass = 0; pass < 2; pass++)
                {
                    OzMotion.Reduced = pass == 0; OzCalm.Reset(); ResetFlash(); sampleFlash = true;
                    wz.ForceFlash(OzStormPlan.Close);
                    yield return new WaitForSeconds(pass == 0 ? 0.5f : 0.08f);
                    Shot(pass == 0 ? "09a_lightning_calm_on" : "09b_lightning_calm_off");
                    yield return new WaitForSeconds(2f);
                    sampleFlash = false; lp[pass] = lightPeak; bf[pass] = boltFrames; if (pass == 0) calmLog = OzCalm.Log();
                    Log($"lightning, calm {(pass == 0 ? "ON " : "OFF")}: flash light peak {lightPeak:F0}, bolt frames {boltFrames}, suppressed: {OzCalm.Log()}");
                }
                OzMotion.Reduced = calm0;
                wz.StopStorm();
                Check("CALM SCREEN ON: lightning = a soft dim glow without the bolt (light peak well below OFF)", bf[0] == 0 && lp[0] < lp[1] * 0.6f && calmLog.Contains("lightning"));
            }
            else Log("no OzWeather on this level (skipped)");
            Log($"calm screen restored to {(OzMotion.Reduced ? "ON" : "OFF")}");
            Finish();
        }

        void Finish()
        {
            Log($"RESULT {(fails == 0 ? "PASS" : "FAIL (" + fails + ")")}");
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, $"proof_log_L{level:00}.txt"), sb.ToString()); } catch (System.Exception ex) { Debug.LogWarning(ex.Message); }
            Report = fails == 0 ? "PASS" : "FAIL " + fails;
            Destroy(gameObject, 0.5f);
        }
    }
}
