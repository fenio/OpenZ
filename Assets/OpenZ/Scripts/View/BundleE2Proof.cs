using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// every vehicle / gun with NoWreck (each test vehicle and dummy removed without a wreck / smoke, so the frames are clean);
    /// (2) VX87 hatch_v1 close-ups: a red light / medium (mech) / heavy tank with a grunt commander vs a harmless blue sniper:
    /// lid closed, lid open with the driver up (3/4 + side panel glow + a 4-frame moving-camera orbit for flicker), then the
    /// sniper is allowed to snipe: time to the Sniped event, frame of the popped driver flying out, sound keys per phase.
    /// </summary>
    [DefaultExecutionOrder(1003)]
    public class BundleE2Proof : MonoBehaviour
    {
        public static string Report = "idle";
        public static bool DoCt1 = true, DoHatch = true;
        public static void Run() { Report = "running"; new GameObject("BundleE2Proof").AddComponent<BundleE2Proof>(); }

        ZGame g; ZWorld W; readonly StringBuilder sb = new StringBuilder(); int fails; string dir;
        Camera cam; RenderTexture rt; Texture2D tex;
        UnitView shotView; string shotPath; float shotYaw, shotDist, shotPitch; int shotPending;

        void Start()
        {
            g = ZGame.I; W = g.World;
            dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records/milestones/bundle_main_E2"));
            System.IO.Directory.CreateDirectory(dir);
            cam = gameObject.AddComponent<Camera>(); cam.enabled = false; cam.fieldOfView = 30f; cam.nearClipPlane = 0.1f;
            var main = Camera.main; if (main) { cam.clearFlags = main.clearFlags; cam.backgroundColor = main.backgroundColor; cam.cullingMask = main.cullingMask; }
            rt = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; tex = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            StartCoroutine(All());
        }

        void Log(string s) { sb.AppendLine(s); Debug.Log("[BundleE2Proof] " + s); }
        void Check(string what, bool ok) { if (!ok) fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        static string Keys(float t0)
        {
            var d = new SortedDictionary<string, int>();
            foreach (var e in OzSoundLog.Since(t0)) if (e.outcome != null && e.outcome.StartsWith("PLAY")) { d.TryGetValue(e.key, out int n); d[e.key] = n + 1; }
            var s = new StringBuilder(); foreach (var kv in d) s.Append(kv.Key).Append(" x").Append(kv.Value).Append(' ');
            return s.ToString().Trim();
        }

        void Remove(ZObject o)
        {
            if (o == null || o.Destroyed || W.Get(o.Id) == null) return;
            o.AttackObject = null; o.KillAt = W.Time;
            foreach (var m in o.Minions) if (m != null && !m.Destroyed) { m.KillAt = W.Time; if (g.Views.TryGetValue(m.Id, out var mv) && mv) mv.Hide(); }
            if (g.Views.TryGetValue(o.Id, out var v) && v) v.Hide();
        }

        Vector2Int Field()
        {
            int wt = W.Path.WidthPix >> 4, ht = W.Path.HeightPix >> 4; var best = new Vector2Int(wt / 2, ht / 2); float bd = float.MaxValue;
            for (int y = 10; y < ht - 10; y += 2)
                for (int x = 10; x < wt - 10; x += 2)
                {
                    bool ok = true;
                    for (int j = -4; j <= 4 && ok; j++) for (int i = -6; i <= 8 && ok; i++) ok = W.Path.TilePassable(x + i, y + j, false);
                    if (!ok) continue;
                    foreach (var o in W.Objects) if (!o.Destroyed && Mathf.Abs((o.CenterX >> 4) - x) < 14 && Mathf.Abs((o.CenterY >> 4) - y) < 12) { ok = false; break; }
                    if (!ok) continue;
                    float d = (x - wt / 2) * (x - wt / 2) + (y - ht / 2) * (y - ht / 2);
                    if (d < bd) { bd = d; best = new Vector2Int(x, y); }
                }
            var rc = FindFirstObjectByType<RtsCamera>(); if (rc) rc.FlyTo(ZGame.PxToWorld(best.x * 16 + 16, best.y * 16 + 16));
            return best;
        }

        IEnumerator Shot(UnitView v, string name, float yaw, float dist, float pitch)
        {
            shotView = v; shotPath = System.IO.Path.Combine(dir, name + ".jpg"); shotYaw = yaw; shotDist = dist; shotPitch = pitch; shotPending = 1;
            while (shotPending > 0) yield return null;
        }

        IEnumerator All()
        {
            float T0 = Time.unscaledTime;
            var dn = FindFirstObjectByType<DayNight>(); float tod = dn ? dn.timeOfDay : 0.08f; bool dnRun = dn && dn.running;
            if (dn) { dn.running = false; NightLights.JumpTo(0.08f); }
            var bots = W.Bots.ToList(); W.Bots.Clear();
            Log($"bundle_main_E2 proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, daylight 0.08 held, CPU bots paused");
            yield return new WaitForSeconds(1f);

            // ---- (1) CT1 re-run, no wrecks
            if (DoCt1)
            {
                CrewSeatCheck.Crews = new[] { "grunt", "psycho", "tough", "sniper", "pyro", "laser", "electrobot" };
                CrewSeatCheck.Types = new[] { "jeep", "light", "medium", "heavy", "apc", "crane", "missile_launcher", "gun", "gatling", "howitzer", "missile_cannon" };
                CrewSeatCheck.Bearings = 1; CrewSeatCheck.IdleShot = true; CrewSeatCheck.EngageWaitVehicle = 3f; CrewSeatCheck.NoWreck = true;
                CrewSeatCheck.SubDir = "bundle_main_E2/ct1"; CrewSeatCheck.ReportName = "milestones/bundle_main_E2/ct1_crew_matrix.md";
                float c0 = Time.unscaledTime;
                CrewSeatCheck.Run();
                while (CrewSeatCheck.Report == "running") yield return null;
                Log($"CT1 CrewSeatCheck (NoWreck, daylight): {CrewSeatCheck.Report.Split('\n')[0]} ({CrewSeatCheck.Crews.Length} crews x {CrewSeatCheck.Types.Length} types, {Time.unscaledTime - c0:F0} s, table ct1_crew_matrix.md, frames ct1/)");
                int wrecks = FindObjectsByType<UnitView>(FindObjectsSortMode.None).Count(v => v && v.Dying && v.gameObject.activeInHierarchy && v.Obj != null && (v.Obj.Kind == ZKind.Vehicle || v.Obj.Kind == ZKind.Cannon));
                Check($"CT1 left no wreck on the field ({wrecks} dead vehicles / guns)", wrecks == 0);
                CrewSeatCheck.Crews = null; CrewSeatCheck.Bearings = 8; CrewSeatCheck.IdleShot = false; CrewSeatCheck.NoWreck = false;
                CrewSeatCheck.SubDir = "crew_seats"; CrewSeatCheck.ReportName = "crew_seat_check.md"; CrewSeatCheck.EngageWaitVehicle = 1.6f;
                CrewSeatCheck.Types = new[] { "gun", "gatling", "howitzer", "missile_cannon" };
                yield return new WaitForSeconds(0.5f);
            }

            // ---- (2) VX87 hatch close-ups + snipe
            if (DoHatch)
            {
                var f = Field(); Log($"hatch test field tile {f.x},{f.y}");
                yield return new WaitForSeconds(1f);
                foreach (var type in new[] { "light", "medium", "heavy" })
                {
                    OzCheatSpawner.FreeTile(W, f.x, f.y, 2, false, out int tx, out int ty);
                    var tank = W.CreateObject("vehicle", type, tx * 16, ty * 16, Team.Red); tank.DriverType = "grunt"; g.SpawnUnitView(tank);
                    yield return new WaitForSeconds(1.2f);
                    g.Views.TryGetValue(tank.Id, out var tv);
                    bool hasHatch = tv && tv.GetComponentInChildren<RoundHatch>(true) != null;
                    yield return Shot(tv, $"vx87_{type}_1_closed", 35f, 11f, 0.55f);
                    Log($"{type}: round hatch {(hasHatch ? "built" : "MISSING")}, lid closed {(!tank.LidOpen)}");
                    Check($"{type} round hatch built", hasHatch);
                    float t0 = Time.unscaledTime;
                    OzCheatSpawner.FreeTile(W, tx + 6, ty, 1, true, out int sx, out int sy);
                    var sn = W.CreateRobotGroup("sniper", sx * 16, sy * 16, Team.Blue, 1);
                    sn.MaxHealth *= 50; sn.SetHealth(sn.MaxHealth); sn.MoveSpeed = 0; int dmg = sn.Damage; sn.Damage = 0; bool canSnipe = sn.CanSnipe; sn.CanSnipe = false;
                    g.SpawnUnitView(sn);
                    tank.AttackObject = sn;
                    float tw = 0f; while (tw < 8f && !tank.LidOpen) { tw += Time.deltaTime; yield return null; }
                    yield return new WaitForSeconds(0.7f);                                   // lid swing 0.4 s + driver rise
                    Log($"{type}: lid open {tank.LidOpen} after {Time.unscaledTime - t0:F1} s, sounds: {Keys(t0)}");
                    Check($"{type} lid opened when the tank fired", tank.LidOpen);
                    yield return Shot(tv, $"vx87_{type}_2_open_34", 35f, 9f, 0.5f);
                    yield return Shot(tv, $"vx87_{type}_3_side_glow", 90f, 10f, 0.22f);
                    // moving camera: 4 consecutive frames at different yaw / distance (flicker check inside the well)
                    var orbit = new[] { (0f, 8f, 0.6f), (120f, 10f, 0.5f), (200f, 12f, 0.7f), (280f, 9f, 0.45f) };
                    for (int k = 0; k < orbit.Length; k++) yield return Shot(tv, $"vx87_{type}_4_orbit{k}", orbit[k].Item1, orbit[k].Item2, orbit[k].Item3);
                    // snipe the popped driver
                    float s0 = Time.unscaledTime; sn.CanSnipe = canSnipe; sn.Damage = dmg; sn.AttackObject = tank;
                    tw = 0f; while (tw < 30f && tank.Owner != Team.Neutral) { tw += Time.deltaTime; yield return null; }
                    bool sniped = tank.Owner == Team.Neutral;
                    tank.AttackObject = null; sn.AttackObject = null; sn.Damage = 0;
                    yield return new WaitForSeconds(0.45f);
                    yield return Shot(tv, $"vx87_{type}_5_sniped", 35f, 11f, 0.55f);
                    Log($"{type}: driver sniped {sniped} in {Time.unscaledTime - s0 - 0.45f:F1} s (lid {(tank.LidOpen ? "open" : "closed")} while empty), sounds: {Keys(s0)}");
                    Check($"{type} popped driver sniped", sniped);
                    Remove(sn); Remove(tank);
                    yield return new WaitForSeconds(0.6f);
                }
            }

            W.Bots.AddRange(bots);
            if (dn) { NightLights.JumpTo(tod); dn.running = dnRun; }
            Report = (fails == 0 ? "PASS" : $"FAIL ({fails})") + $" in {Time.unscaledTime - T0:F0} s";
            Log("RESULT: " + Report);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "proof_log.txt"), sb.ToString());
            if (rt) rt.Release();
            Destroy(gameObject);
        }

        void LateUpdate()
        {
            if (shotPending == 0) return;
            if (shotView)
            {
                var t = shotView.transform; var focus = t.position + Vector3.up * 1.7f;
                var dirv = Quaternion.Euler(0, shotYaw, 0) * new Vector3(0, shotPitch, -1f).normalized;
                cam.transform.position = focus + dirv * shotDist; cam.transform.LookAt(focus);
                cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
                var prev = RenderTexture.active; RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); tex.Apply(); RenderTexture.active = prev;
                System.IO.File.WriteAllBytes(shotPath, tex.EncodeToJPG(90));
            }
            shotPending = 0;
        }
    }
}
