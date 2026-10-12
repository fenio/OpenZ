using System.Collections;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Red psycho + grunt + sniper squads fire at (1) a walking blue medium tank (the mech), (2) a blue grunt squad, (3) the
    /// blue fort wall (targets unarmed and unkillable). Counts HitSurface results per stage (mesh / bone / bounds hits, fallbacks to
    /// the old aim point, misses on the ground) and saves close frames at 16 m (and slow-motion 0.25x ones, so the bursts are caught
    /// </summary>
    public class ImpactCheck : MonoBehaviour
    {
        public static string Report = "idle";
        public static void Run() { Report = "running"; new GameObject("ImpactCheck").AddComponent<ImpactCheck>(); }
        static string Records => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records"));

        ZGame g; ZWorld W;
        ZObject Squad(string type, Team t, int tx, int ty) { var o = W.CreateRobotGroup(type, tx * 16, ty * 16, t); g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m); return o; }
        static void Tough(ZObject o, bool unarmed = true) { foreach (var m in new List<ZObject>(o.Minions) { o }) { if (unarmed) m.Damage = 0; m.MaxHealth = 100000000; m.SetHealth(m.MaxHealth); } }
        void Clear() { foreach (var o in new List<ZObject>(W.Objects)) if (o.IsUnit && !o.Destroyed && o.Kind != ZKind.Cannon && Mathf.Abs(o.CenterX / 16 - 45) < 12 && Mathf.Abs(o.CenterY / 16 - 18) < 9) { o.SetHealth(0); W.UpdateObjectHealth(o); } }
        void Kill(ZObject o) { if (o == null) return; foreach (var m in new List<ZObject>(o.Minions) { o }) if (!m.Destroyed) { m.SetHealth(0); W.UpdateObjectHealth(m); } }

        IEnumerator Start()
        {
            g = ZGame.I; W = g.World; W.Bots.Clear();
            var cam = FindFirstObjectByType<RtsCamera>(); var dn = FindFirstObjectByType<DayNight>(); dn.running = false; dn.timeOfDay = 0.08f; dn.Apply(0.08f);
            string dir = Records + "/milestones/impact_check"; System.IO.Directory.CreateDirectory(dir);
            var sb = new System.Text.StringBuilder($"# ImpactCheck {System.DateTime.Now:yyyy-MM-dd HH:mm} {g.level}\n\n| stage | resolved on mesh | on robot bones | on bounds | fallback (old aim point) | misses on the ground |\n|---|---|---|---|---|---|\n");
            Clear(); yield return new WaitForSeconds(1f);
            var shooters = new List<ZObject> { Squad("psycho", Team.Red, 40, 14), Squad("grunt", Team.Red, 40, 18), Squad("sniper", Team.Red, 40, 22) };
            foreach (var s in shooters) Tough(s, false);                                                    // shooters keep their weapons
            ZObject mech = W.CreateObject("vehicle", "medium", 50 * 16, 18 * 16, Team.Blue); g.SpawnUnitView(mech); Tough(mech);
            ZObject blues = Squad("grunt", Team.Blue, 49, 14); Tough(blues);
            foreach (var (name, tgt) in new[] { ("mech", mech), ("robots", blues) })
            {
                foreach (var o in new List<ZObject>(W.Objects)) if (o.IsUnit && !o.Destroyed && o.Owner == Team.Blue && o != tgt && o != mech && o != blues && !blues.Minions.Contains(o) && o.Kind != ZKind.Cannon) { o.SetHealth(0); W.UpdateObjectHealth(o); }   // only this stage's shots count
                int m0 = HitSurface.MeshHits, b0 = HitSurface.BoneHits, x0 = HitSurface.BoundsHits, f0 = HitSurface.Fallbacks, gm0 = HitSurface.GroundMisses;
                foreach (var s in shooters) W.SendWaypoints(Team.Red, s, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Attack, refId = tgt.Id, x = tgt.CenterX, y = tgt.CenterY } });
                float t0 = Time.time; int fr = 0;
                while (Time.time - t0 < 10f)
                {
                    var p = ZGame.PxToWorld(tgt.CenterX, tgt.CenterY); cam.focus = new Vector3(p.x - 2f, 0f, p.z); cam.SetHeightImmediate(16f);
                    if (Time.time - t0 > 2f + fr * 0.8f && fr < 9)
                    {
                        Time.timeScale = 0.25f; yield return new WaitForSecondsRealtime(0.15f);
                        ScreenCapture.CaptureScreenshot($"{dir}/{name}_{fr}.png"); yield return null; Time.timeScale = 1f; fr++;
                    }
                    yield return null;
                }
                sb.AppendLine($"| shots at the {name} | {HitSurface.MeshHits - m0} | {HitSurface.BoneHits - b0} | {HitSurface.BoundsHits - x0} | {HitSurface.Fallbacks - f0} | {HitSurface.GroundMisses - gm0} |");
            }
            // fort wall
            var fort = W.Objects.Find(o => o.IsFort && o.Owner == Team.Blue);
            if (fort != null)
            {
                Kill(mech); Kill(blues);
                int m0 = HitSurface.MeshHits, b0 = HitSurface.BoneHits, x0 = HitSurface.BoundsHits, f0 = HitSurface.Fallbacks, gm0 = HitSurface.GroundMisses;
                int fx = (fort.LocX + fort.WidthPix / 2) / 16, fy = (fort.LocY + fort.HeightPix) / 16 + 5;
                var gs = Squad("grunt", Team.Red, fx, fy); Tough(gs, false);
                W.SendWaypoints(Team.Red, gs, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Attack, refId = fort.Id, x = fort.CenterX, y = fort.CenterY } });
                fort.MaxHealth = 100000000; fort.SetHealth(fort.MaxHealth);
                float t0 = Time.time; int fr = 0;
                while (Time.time - t0 < 10f)
                {
                    var p = ZGame.PxToWorld(fx * 16, fy * 16 - 40); cam.focus = new Vector3(p.x, 0f, p.z); cam.SetHeightImmediate(20f);
                    if (Time.time - t0 > 3f + fr * 1.5f && fr < 4) { Time.timeScale = 0.25f; yield return new WaitForSecondsRealtime(0.15f); ScreenCapture.CaptureScreenshot($"{dir}/fort_{fr}.png"); yield return null; Time.timeScale = 1f; fr++; }
                    yield return null;
                }
                sb.AppendLine($"| shots at the blue fort wall | {HitSurface.MeshHits - m0} | {HitSurface.BoneHits - b0} | {HitSurface.BoundsHits - x0} | {HitSurface.Fallbacks - f0} | {HitSurface.GroundMisses - gm0} |");
                Kill(gs);
            }
            Time.timeScale = 1f;
            foreach (var s in shooters) Kill(s);
            System.IO.File.WriteAllText(Records + "/impact_check.md", sb.ToString());
            Report = "done"; dn.running = true; Destroy(gameObject);
        }
    }
}
