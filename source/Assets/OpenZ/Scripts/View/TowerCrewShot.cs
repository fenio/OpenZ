using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// For every gun on a fort tower, frames at 16 m and 34 m by day and at night, plus a report of
    /// its paint (FortPalette), crew (drivers, gunner renderer active, gunner top above the gun top = seen from the RTS camera) and
    /// milestones/tower_gun_fix/tc_[tag]_*.png. CPU bots removed.
    /// </summary>
    public class TowerCrewShot : MonoBehaviour
    {
        public static string Report = "idle", Tag = "after";
        public static void Run(string tag) { Tag = tag; Report = "running"; new GameObject("TowerCrewShot").AddComponent<TowerCrewShot>(); }
        static string Records => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records"));

        IEnumerator Start()
        {
            var g = ZGame.I; g.World.Bots.Clear();
            var dn = FindFirstObjectByType<DayNight>(); var cam = FindFirstObjectByType<RtsCamera>();
            bool run0 = dn.running; dn.running = false;
            string dir = Records + "/milestones/tower_gun_fix"; System.IO.Directory.CreateDirectory(dir);
            var guns = new List<UnitView>();
            foreach (var kv in g.Views) if (kv.Value && kv.Value.OnTower && !kv.Value.Dying) guns.Add(kv.Value);
            var sb = new StringBuilder($"# TowerCrewShot {Tag} {System.DateTime.Now:yyyy-MM-dd HH:mm} {g.level}\n\n| gun | owner | fort palette | drivers | gunner drawn | gunner top above gun top | gunner from axis |\n|---|---|---|---|---|---|---|\n");
            int ok = 0;
            foreach (var v in guns)
            {
                var o = v.Obj; bool fp = v.TryGetComponent<TeamPaint>(out var tp) && tp.FortPalette;
                SkinnedMeshRenderer crew = null; float gunTop = float.NegativeInfinity;
                foreach (var r in v.GetComponentsInChildren<Renderer>(false))
                {
                    if (r is SkinnedMeshRenderer && (r.name.StartsWith("BS01_Body") || r.name.Contains("Robot"))) { if (!crew) crew = (SkinnedMeshRenderer)r; continue; }
                    if (r.GetComponentInParent<Animator>()) continue;
                    if (r is MeshRenderer) gunTop = Mathf.Max(gunTop, r.bounds.max.y);
                }
                bool drawn = crew && crew.enabled && crew.gameObject.activeInHierarchy;
                float above = drawn ? crew.bounds.max.y - gunTop : -9f;
                var axis = crew ? crew.bounds.center - v.transform.position : Vector3.zero; axis.y = 0;
                bool pass = fp && (o.Drivers.Count == 0 || drawn && above > -0.35f);
                if (pass) ok++;
                sb.AppendLine($"| #{o.Id} {o.Type} | {o.Owner} | {fp} | {o.Drivers.Count} ({o.DriverType}) | {drawn} | {(drawn ? above.ToString("+0.00;-0.00") + " m" : "-")} | {(drawn ? axis.magnitude.ToString("0.00") + " m" : "-")} |");
            }
            foreach (var (tod, tn) in new[] { (0.08f, "day"), (0.62f, "night") })
            {
                dn.timeOfDay = tod; dn.Apply(tod); yield return new WaitForSeconds(1.2f);
                for (int i = 0; i < guns.Count; i++)
                {
                    if (!guns[i]) continue;
                    var p = guns[i].transform.position;
                    foreach (var h in new[] { 16f, 34f })
                    {
                        cam.focus = new Vector3(p.x, 0f, p.z) + TcExt.Flat(cam.transform.forward).normalized * (p.y * 0.9f);   // the tower top, not its foot
                        cam.SetHeightImmediate(h); yield return new WaitForSeconds(0.5f);
                        ScreenCapture.CaptureScreenshot($"{dir}/tc_{Tag}_{guns[i].Obj.Id}_{tn}_{h:0}.png"); yield return null;
                    }
                }
            }
            dn.timeOfDay = 0.08f; dn.Apply(0.08f); dn.running = run0;
            sb.AppendLine($"\n**{ok}/{guns.Count} tower guns in the fort palette with their gunner in view**");
            System.IO.File.WriteAllText($"{Records}/tower_crew_{Tag}.md", sb.ToString());
            Report = $"{ok}/{guns.Count}"; Destroy(gameObject);
        }
    }

    static class TcExt { public static Vector3 Flat(Vector3 v) { v.y = 0f; return v; } }
}
