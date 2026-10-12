using System.Collections;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// tank for 14 s: salvos (sim missiles), twins launched side by side, small blasts landed (expect 3 per salvo), the largest pod
    /// kick seen (m / deg), launch voices started; slow-motion frames of a launch (two rockets side by side) and an impact (3 blasts).
    /// </summary>
    public class MissileCannonCheck : MonoBehaviour
    {
        public static string Report = "idle";
        public static void Run() { Report = "running"; new GameObject("MissileCannonCheck").AddComponent<MissileCannonCheck>(); }

        IEnumerator Start()
        {
            var g = ZGame.I; var W = g.World; W.Bots.Clear();
            var cam = FindFirstObjectByType<RtsCamera>(); var dn = FindFirstObjectByType<DayNight>(); dn.running = false; dn.timeOfDay = 0.08f; dn.Apply(0.08f);
            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records/milestones/missile_cannon"));
            System.IO.Directory.CreateDirectory(dir);
            foreach (var o in new List<ZObject>(W.Objects))
                if (o.IsUnit && !o.Destroyed && Mathf.Abs(o.CenterX / 16 - 49) < 9 && Mathf.Abs(o.CenterY / 16 - 15) < 5) { o.SetHealth(0); W.UpdateObjectHealth(o); }
            yield return new WaitForSeconds(1f);
            var mc = W.CreateObject("cannon", "missile_cannon", 46 * 16, 14 * 16, Team.Red); g.SpawnUnitView(mc);
            var tank = W.CreateObject("vehicle", "light", 52 * 16, 14 * 16, Team.Blue); g.SpawnUnitView(tank);   // 7 tiles: inside the 144 px attack radius
            tank.Damage = 0; tank.MaxHealth = 100000000; tank.SetHealth(tank.MaxHealth);
            mc.MaxHealth = 100000000; mc.SetHealth(mc.MaxHealth);
            yield return new WaitForSeconds(2f);
            W.SendWaypoints(Team.Red, mc, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Attack, refId = tank.Id, x = tank.CenterX, y = tank.CenterY } });
            int nv0 = MissileCannonSalvo.NoView, salv0 = MissileCannonSalvo.Salvos, fired0 = 0, twins0 = MissileCannonSalvo.Twins, blasts0 = MissileCannonSalvo.Blasts;
            Transform pod = null; if (g.Views.TryGetValue(mc.Id, out var mv)) foreach (var t in mv.GetComponentsInChildren<Transform>()) if (t.name == "BarrelPitch") pod = t;
            Vector3 p0 = pod ? pod.localPosition : Vector3.zero; float kickM = 0f;
            float t0 = Time.time; int launchShots = 0, impactShots = 0; var mcw = ZGame.PxToWorld(mc.CenterX, mc.CenterY); var tw = ZGame.PxToWorld(tank.CenterX, tank.CenterY);
            var mid = (mcw + tw) * 0.5f; cam.focus = new Vector3(mid.x, 0f, mid.z); cam.SetHeightImmediate(22f);   // both in one still frame
            while (Time.time - t0 < 14f)
            {
                fired0 = MissileCannonSalvo.Salvos - salv0;
                if (pod) kickM = Mathf.Max(kickM, (pod.localPosition - p0).magnitude);
                if (launchShots < 3 && MissileCannonSalvo.Twins > twins0 + launchShots)
                {   // right after a twin left: both rockets in the air side by side
                    Time.timeScale = 0.2f; yield return new WaitForSecondsRealtime(0.12f);
                    ScreenCapture.CaptureScreenshot($"{dir}/launch_{launchShots}.png"); yield return null; Time.timeScale = 1f; launchShots++;
                }
                if (impactShots < 3 && MissileCannonSalvo.Blasts >= blasts0 + 2 + impactShots * 3)
                {
                    Time.timeScale = 0.2f; yield return new WaitForSecondsRealtime(0.1f);
                    ScreenCapture.CaptureScreenshot($"{dir}/impact_{impactShots}.png"); yield return null; Time.timeScale = 1f; impactShots++;
                }
                yield return null;
            }
            Time.timeScale = 1f;
            // comparison: the mech's twin rockets from the same spot at the same target, same framing
            mc.SetHealth(0); W.UpdateObjectHealth(mc); yield return new WaitForSeconds(1.5f);
            var mech = W.CreateObject("vehicle", "medium", 46 * 16, 16 * 16, Team.Red); g.SpawnUnitView(mech); mech.MaxHealth = 100000000; mech.SetHealth(mech.MaxHealth);
            yield return new WaitForSeconds(1.5f);
            W.SendWaypoints(Team.Red, mech, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Attack, refId = tank.Id, x = tank.CenterX, y = tank.CenterY } });
            int mt0 = MechRockets.Twins, mechShots = 0; float tm = Time.time;
            while (Time.time - tm < 10f && mechShots < 2)
            {
                if (MechRockets.Twins > mt0 + mechShots) { Time.timeScale = 0.2f; yield return new WaitForSecondsRealtime(0.12f); ScreenCapture.CaptureScreenshot($"{dir}/mech_launch_{mechShots}.png"); yield return null; Time.timeScale = 1f; mechShots++; }
                yield return null;
            }
            Time.timeScale = 1f; mech.SetHealth(0); W.UpdateObjectHealth(mech);
            int twins = MissileCannonSalvo.Twins - twins0, blasts = MissileCannonSalvo.Blasts - blasts0;
            var sb = new System.Text.StringBuilder($"# MissileCannonCheck {System.DateTime.Now:yyyy-MM-dd HH:mm}\n\n");
            sb.AppendLine($"- salvos (sim missiles): {fired0}");
            sb.AppendLine($"- twin rockets launched side by side: {twins} (expect = salvos)");
            sb.AppendLine($"- cosmetic small blasts: {blasts} (+ {fired0} sim blasts = {blasts + fired0}; expect 3 per salvo = {fired0 * 3})");
            sb.AppendLine($"- largest pod kick: {kickM * 100f:0.0} cm");
            sb.AppendLine($"- test cannon #{mc.Id}, last salvo shooter #{MissileCannonSalvo.LastShooter}, salvos without a live shooter view {MissileCannonSalvo.NoView - nv0}");
            bool pass = fired0 > 0 && twins >= fired0 - 1 && blasts + fired0 >= (fired0 - 1) * 3 && kickM > 0.05f;
            sb.AppendLine($"\n**{(pass ? "PASS" : "FAIL")}**; frames launch_*.png, impact_*.png");
            System.IO.File.WriteAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records/missile_cannon_check.md")), sb.ToString());
            tank.SetHealth(0); W.UpdateObjectHealth(tank);
            dn.running = true; Report = pass ? "PASS" : "FAIL"; Destroy(gameObject);
        }
    }
}
