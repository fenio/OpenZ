using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// the sim centre (CenterWorldSmooth), the view's basePos, sepOffset, sepCorr, disp and the transform, plus the closest drawn
    /// </summary>
    public class SpacingTrace : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Type = "tough";
        static readonly BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        public static void Run(string type) { Type = type; Report = "running"; new GameObject("SpacingTrace").AddComponent<SpacingTrace>(); }

        static Vector3 V(UnitView v, string f) => (Vector3)typeof(UnitView).GetField(f, F).GetValue(v);

        IEnumerator Start()
        {
            var g = ZGame.I; var W = g.World; W.Bots.Clear();
            foreach (var o in new List<ZObject>(W.Objects))
                if (o.IsUnit && !o.Destroyed && o.Kind != ZKind.Cannon && Mathf.Abs(o.CenterX / 16 - 44) < 14 && Mathf.Abs(o.CenterY / 16 - 20) < 12) { o.SetHealth(0); W.UpdateObjectHealth(o); }
            yield return new WaitForSeconds(1f);
            OzCheatSpawner.FreeTile(W, 43, 14, 1, true, out int tx, out int ty);
            var s = W.CreateRobotGroup(Type, tx * 16, ty * 16, Team.Red); g.SpawnUnitView(s); foreach (var m in s.Minions) g.SpawnUnitView(m);
            var cam = FindFirstObjectByType<RtsCamera>(); if (cam) { var c = ZGame.PxToWorld(45 * 16, 18 * 16); cam.focus = new Vector3(c.x, 0, c.z); cam.SetHeightImmediate(30f); }
            yield return new WaitForSeconds(2.5f);
            W.SendWaypoints(Team.Red, s, new List<ZWaypoint> { ZWaypoint.Move(s.CenterX, s.CenterY + 8 * 16) });
            var mem = new List<ZObject> { s }; mem.AddRange(s.Minions);
            var sb = new StringBuilder("t");
            for (int i = 0; i < mem.Count; i++) sb.Append($",m{i}_simx,m{i}_simz,m{i}_basex,m{i}_basez,m{i}_sepx,m{i}_sepz,m{i}_corx,m{i}_corz,m{i}_dispx,m{i}_dispz,m{i}_tx,m{i}_tz,m{i}_inForm,m{i}_sq");
            sb.AppendLine(",minView,minSim");
            float t0 = Time.time, minView = 99f, minSim = 99f;
            while (Time.time - t0 < 9f)
            {
                sb.Append((Time.time - t0).ToString("0.000"));
                var tp = new List<Vector3>(); var sp = new List<Vector3>();
                foreach (var o in mem)
                {
                    if (o.Destroyed || !g.Views.TryGetValue(o.Id, out var v) || !v) { sb.Append(",,,,,,,,,,,,,"); continue; }
                    var sim = g.CenterWorldSmooth(o); var b = V(v, "basePos"); var so = V(v, "sepOffset"); var sc = V(v, "sepCorr"); var d = V(v, "disp"); var p = v.transform.position;
                    sb.Append($",{sim.x:0.000},{sim.z:0.000},{b.x:0.000},{b.z:0.000},{so.x:0.000},{so.z:0.000},{sc.x:0.000},{sc.z:0.000},{d.x:0.000},{d.z:0.000},{p.x:0.000},{p.z:0.000},{(o.InFormation ? 1 : 0)},{o.FormSqueeze:0.00}");
                    tp.Add(p); sp.Add(sim);
                }
                float mv = 99f, ms = 99f;
                for (int a = 0; a < tp.Count; a++) for (int c = a + 1; c < tp.Count; c++)
                    {
                        var dv = tp[a] - tp[c]; dv.y = 0; mv = Mathf.Min(mv, dv.magnitude);
                        var ds = sp[a] - sp[c]; ds.y = 0; ms = Mathf.Min(ms, ds.magnitude);
                    }
                if (Time.time - t0 > 1.5f && s.Waypoints.Count > 0) { minView = Mathf.Min(minView, mv); minSim = Mathf.Min(minSim, ms); }
                sb.AppendLine($",{mv:0.000},{ms:0.000}");
                yield return null;
            }
            string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, $"../../../../work/records/spacing_trace_{Type}.csv"));
            System.IO.File.WriteAllText(path, sb.ToString());
            foreach (var o in mem) if (!o.Destroyed) { o.SetHealth(0); W.UpdateObjectHealth(o); }
            Report = $"done {Type}: walking min drawn {minView:0.00} m, min sim {minSim:0.00} m";
            Destroy(gameObject);
        }
    }
}
