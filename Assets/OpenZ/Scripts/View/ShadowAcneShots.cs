using System.Collections;
using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// SF1 step 4 (shadow_fix DIAGNOSIS section 6): acne look after the receiver-side normal offset. Game view close-ups at h 22 of a
    /// robot squad, a light tank, the fort wall, a cliff face and the radar at t 0.08, 0.30 and 0.42 (low sun); then the NB44 day
    /// wrap (t 0.995 -> through 0 for 10 s at h 70) and ShadowFlickerProbe cliff at t 0.998. Frames
    /// </summary>
    public class ShadowAcneShots : MonoBehaviour
    {
        public static string Status = "idle";
        public static void Run() { Status = "running"; new GameObject("ShadowAcneShots").AddComponent<ShadowAcneShots>(); }

        IEnumerator Start()
        {
            var g = ZGame.I; var W = g.World; W.Bots.Clear();
            var dn = FindFirstObjectByType<DayNight>(); var cam = FindFirstObjectByType<RtsCamera>();
            bool run0 = dn.running; dn.running = false;
            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records/milestones/shadow_fix"));
            System.IO.Directory.CreateDirectory(dir);
            foreach (var o in new List<ZObject>(W.Objects))
                if (o.IsUnit && !o.Destroyed && o.Kind != ZKind.Cannon && Mathf.Abs(o.CenterX / 16 - 45) < 8 && Mathf.Abs(o.CenterY / 16 - 17) < 6) { o.SetHealth(0); W.UpdateObjectHealth(o); }
            var squad = W.CreateRobotGroup("grunt", 43 * 16, 17 * 16, Team.Red); g.SpawnUnitView(squad); foreach (var m in squad.Minions) g.SpawnUnitView(m);
            var tank = W.CreateObject("vehicle", "light", 48 * 16, 17 * 16, Team.Red); g.SpawnUnitView(tank);
            yield return new WaitForSeconds(2f);
            var subjects = new List<(string n, Vector3 p)>
            {
                ("squad", ZGame.PxToWorld(squad.CenterX, squad.CenterY)), ("tank", ZGame.PxToWorld(tank.CenterX, tank.CenterY)),
                ("cliff", new Vector3(30f, 0f, -90f)),
            };
            var fort = W.Objects.FirstOrDefault(o => o.IsFort && o.Owner == Team.Red);
            if (fort != null) subjects.Add(("fortwall", ZGame.PxToWorld(fort.LocX + fort.WidthPix / 2, fort.LocY + fort.HeightPix + 8)));
            var radar = W.Objects.FirstOrDefault(o => o.Type == "radar");
            if (radar != null) subjects.Add(("radar", ZGame.PxToWorld(radar.CenterX, radar.CenterY)));
            foreach (var t in new[] { 0.08f, 0.30f, 0.42f })
            {
                dn.timeOfDay = t; dn.Apply(t); yield return new WaitForSeconds(1.2f);
                foreach (var s in subjects)
                {
                    cam.focus = new Vector3(s.p.x, 0f, s.p.z); cam.SetHeightImmediate(22f);
                    yield return new WaitForSeconds(0.5f);
                    ScreenCapture.CaptureScreenshot($"{dir}/acne_{s.n}_{t:0.00}.png"); yield return null;
                }
            }
            // NB44 day wrap: through t = 0 at h 70, frames every 2 s
            cam.focus = new Vector3(30f, 0f, -90f); cam.SetHeightImmediate(70f);
            dn.timeOfDay = 0.995f; dn.Apply(0.995f); dn.running = true;
            for (int i = 0; i < 5; i++) { yield return new WaitForSeconds(2f); ScreenCapture.CaptureScreenshot($"{dir}/wrap_{i}_{dn.timeOfDay:0.000}.png"); yield return null; }
            dn.running = false;
            ShadowFlickerProbe.Begin("cliff", 35f, false, 0.998f);
            while (ShadowFlickerProbe.Status == "running" || ShadowFlickerProbe.Status.StartsWith("running")) yield return null;
            dn.running = run0;
            Status = "done: wrap probe " + ShadowFlickerProbe.Status;
            Destroy(gameObject);
        }
    }
}
