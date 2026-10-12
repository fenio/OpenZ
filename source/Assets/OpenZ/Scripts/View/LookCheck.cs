using System.Collections;
using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// day and at night, and the unlit dust tint (NightV2.DustTint) at day / dusk / night with a frame on the nearest dust devil.
    /// </summary>
    public class LookCheck : MonoBehaviour
    {
        public static string Report = "idle";
        static string Frames => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records/milestones/look_check"));
        public static void Run() { Report = "running"; new GameObject("LookCheck").AddComponent<LookCheck>(); }

        IEnumerator Start()
        {
            var g = ZGame.I; var W = g.World; W.Bots.Clear(); System.IO.Directory.CreateDirectory(Frames);
            var dn = FindFirstObjectByType<DayNight>(); var cam = FindFirstObjectByType<RtsCamera>();
            bool run0 = dn.running; dn.running = false;
            // line-up on open ground north-middle (units kept alive and still)
            foreach (var o in new List<ZObject>(W.Objects))
                if (o.IsUnit && !o.Destroyed && o.Kind != ZKind.Cannon && Mathf.Abs(o.CenterX / 16 - 45) < 12 && Mathf.Abs(o.CenterY / 16 - 18) < 9) { o.SetHealth(0); W.UpdateObjectHealth(o); }
            var made = new List<ZObject>(); int x = 39;
            foreach (var t in new[] { "grunt", "psycho", "tough", "sniper", "pyro", "laser" }) { var o = W.CreateRobotGroup(t, x * 16, 15 * 16, Team.Red, 1); g.SpawnUnitView(o); made.Add(o); x += 2; }
            x = 40;
            foreach (var t in new[] { "light", "medium", "heavy" }) { var o = W.CreateObject("vehicle", t, x * 16, 18 * 16, Team.Red); g.SpawnUnitView(o); made.Add(o); x += 4; }
            foreach (var o in made) { o.MaxHealth = 100000000; o.SetHealth(o.MaxHealth); o.Damage = 0; }
            cam.focus = ZGame.PxToWorld(45 * 16, 17 * 16); cam.focus.y = 0; cam.SetHeightImmediate(24f);
            foreach (var (tod, tag) in new[] { (0.08f, "day"), (0.62f, "night") })
            {
                dn.timeOfDay = tod; dn.Apply(tod); yield return new WaitForSeconds(1.2f);
                UnitSheen.Apply(false); yield return new WaitForSeconds(0.3f); ScreenCapture.CaptureScreenshot($"{Frames}/sheen_off_{tag}.png"); yield return null;
                UnitSheen.Apply(true); yield return new WaitForSeconds(0.3f); ScreenCapture.CaptureScreenshot($"{Frames}/sheen_on_{tag}.png"); yield return null;
            }
            // dust tint: day / dusk / night on a dust devil
            var atmo = FindFirstObjectByType<Atmosphere>(); Transform devil = null;
            if (atmo) foreach (Transform c in atmo.transform) if (c.name.StartsWith("DustDevil")) { devil = c; break; }
            var sb = new System.Text.StringBuilder();
            foreach (var (tod, tag) in new[] { (0.08f, "day"), (0.33f, "dusk"), (0.42f, "bluehour"), (0.62f, "night") })
            {
                dn.timeOfDay = tod; dn.Apply(tod); yield return new WaitForSeconds(2.3f);                  // NightV2 re-applies the tint
                if (devil) { cam.focus = new Vector3(devil.position.x, 0, devil.position.z); cam.SetHeightImmediate(45f); }
                yield return new WaitForSeconds(0.4f);
                ScreenCapture.CaptureScreenshot($"{Frames}/dust_{tag}.png"); yield return null;
                sb.Append($"{tag}: tint {NightV2.DustTint.x:0.00}/{NightV2.DustTint.y:0.00}/{NightV2.DustTint.z:0.00}; ");
            }
            dn.timeOfDay = 0.08f; dn.running = run0;
            foreach (var o in made) { o.SetHealth(0); W.UpdateObjectHealth(o); }
            Report = "done: " + sb;
            System.IO.File.WriteAllText(Frames + "/look_check.txt", Report);
            Destroy(gameObject);
        }
    }
}
