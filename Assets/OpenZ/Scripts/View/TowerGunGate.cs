using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// centre, all 8 towers, both levels, under 5 cm, in the gate). In the running level, for both forts, all 4 turret spots
    /// (bfort.cpp:534-548) and all 4 cannon types (gun, gatling, howitzer, missile cannon): a cannon is created on the spot
    /// (ZWorld.CreateObject + ZGame.SpawnUnitView, like CrewSeatCheck; the map's own tower cannon is hidden meanwhile), UnitView seats
    /// it, and after 3 frames the drawn transforms are measured against the pad (TowerMounts.PadWorld = the measured plate centre):
    ///   turn axis (…TurretYaw) and base-plate centre (TowerGunSeat.BaseCentre logic: the base part's own geometry) within 5 cm,
    ///   root on the plinth top within 1 cm, rest heading on the tower diagonal within 1 deg, OnTower, a live plinth on the pad
    ///   (OZ_TowerMounts), and the edit-mode copy (OZ_TowerMounts_Baked) hidden. Every case also gets a top-down orthographic capture
    ///   (6 m, pad centre = image centre, cyan cross + 5 cm ring, magenta dot = turn axis) and one 55 deg RTS-angle close-up per type.
    /// </summary>
    [DefaultExecutionOrder(1004)]
    public class TowerGunGate : MonoBehaviour
    {
        public static string Report = "idle";
        public static readonly string[] Types = { "gun", "gatling", "howitzer", "missile_cannon" };
        const int Tile = 320; const float Half = 3f;
        ZGame g; ZWorld W; Camera cam; RenderTexture rt; Texture2D tex;
        readonly StringBuilder sb = new StringBuilder();
        static string Records => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records"));

        public static void Run() { Report = "running"; new GameObject("TowerGunGate").AddComponent<TowerGunGate>(); }

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null;
            if (W == null) { Report = "FAIL: no running level"; Destroy(gameObject); return; }
            cam = gameObject.AddComponent<Camera>(); cam.enabled = false; cam.nearClipPlane = 0.1f; cam.farClipPlane = 200f;
            var main = Camera.main; if (main) { cam.clearFlags = main.clearFlags; cam.backgroundColor = main.backgroundColor; cam.cullingMask = main.cullingMask; }
            rt = new RenderTexture(Tile, Tile, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; tex = new Texture2D(Tile, Tile, TextureFormat.RGB24, false);
            StartCoroutine(All());
        }

        static Transform Axis(Transform u) { foreach (var t in u.GetComponentsInChildren<Transform>(true)) if (t.name.EndsWith("TurretYaw")) return t; return null; }

        static bool BaseCentre(Transform unit, out Vector3 centre, out float bottom)
        {   // same measure as TowerGunSeat.BaseCentre (editor): the largest-footprint part that does not turn with the turret
            centre = default; bottom = 0f; float best = -1f; var axis = Axis(unit);
            foreach (var mf in unit.GetComponentsInChildren<MeshFilter>(false))
            {
                if (!mf.sharedMesh || (axis && mf.transform.IsChildOf(axis))) continue;
                var mr = mf.GetComponent<MeshRenderer>(); if (!mr || !mr.enabled) continue;
                var m = mf.sharedMesh; var b = m.subMeshCount > 0 ? m.GetSubMesh(0).bounds : m.bounds;
                for (int s = 1; s < m.subMeshCount; s++) b.Encapsulate(m.GetSubMesh(s).bounds);
                if (b.size.sqrMagnitude < 1e-8f) b = m.bounds;
                var w = mf.transform.TransformVector(new Vector3(b.size.x, 0, 0)).magnitude * mf.transform.TransformVector(new Vector3(0, 0, b.size.z)).magnitude;
                if (w <= best) continue;
                best = w; centre = mf.transform.TransformPoint(b.center); bottom = mf.transform.TransformPoint(b.center - new Vector3(0, b.extents.y, 0)).y;
            }
            return best > 0f;
        }

        IEnumerator All()
        {
            string level = g.level; int fail = 0, n = 0;
            string shots = System.IO.Path.Combine(Records, "milestones", "tower_guns"); System.IO.Directory.CreateDirectory(shots);
            var forts = W.Objects.Where(o => o.IsFort && !o.Destroyed && g.StaticViews.TryGetValue(o.Id, out var v) && v).ToList();
            var baked = GameObject.Find(TowerMounts.BakedRootName);                                     // Find sees active objects only: must be null
            sb.AppendLine($"| status | fort | spot | pad | type | on tower | turn axis off (cm) | base plate off (cm) | root vs plinth top (cm) | heading off (deg) | plinth |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            var sheets = Types.ToDictionary(t => t, t => new List<Texture2D>());
            foreach (var f in forts)
            {
                var ft = g.StaticViews[f.Id].transform;
                foreach (var (lx, ly) in TowerMounts.TurretSpots)
                {
                    int pad = TowerMounts.PadForSpot(ft, lx, ly); var pw = TowerMounts.PadWorld(ft, pad); var seat = TowerMounts.SeatWorld(ft, pad);
                    var hidden = new List<GameObject>();                                                  // the map's own cannon on this spot
                    foreach (var kv in g.Views)
                        if (kv.Value && kv.Value.OnTower && kv.Value.TowerFort == ft && kv.Value.TowerPad == pad && kv.Value.gameObject.activeSelf) { kv.Value.gameObject.SetActive(false); hidden.Add(kv.Value.gameObject); }
                    foreach (var type in Types)
                    {
                        var c = W.CreateObject("cannon", type, f.LocX + lx, f.LocY + ly, f.Owner); c.DriverType = "grunt";
                        var v = g.SpawnUnitView(c);
                        for (int k = 0; k < 3; k++) yield return null;
                        n++;
                        if (!v) { fail++; sb.AppendLine($"| FAIL | {ft.name} | {lx},{ly} | {pad} | {type} | no view | | | | | |"); c.KillAt = W.Time; continue; }
                        var t = v.transform; var a = Axis(t); var ap = a ? a.position : t.position;
                        float axisOff = new Vector2(ap.x - pw.x, ap.z - pw.z).magnitude;
                        bool hasBase = BaseCentre(t, out var bc, out _);
                        float baseOff = hasBase ? new Vector2(bc.x - pw.x, bc.z - pw.z).magnitude : 99f;
                        float dy = t.position.y - seat.y;
                        float hdg = Mathf.Abs(Mathf.DeltaAngle(t.eulerAngles.y, TowerMounts.SeatYaw(ft, pad, type)));
                        var mounts = GameObject.Find("OZ_TowerMounts");
                        bool plinth = mounts && mounts.transform.Cast<Transform>().Any(m => m.gameObject.activeInHierarchy && (m.position - pw).magnitude < 0.01f);
                        bool ok = v.OnTower && axisOff <= 0.05f && baseOff <= 0.05f && Mathf.Abs(dy) <= 0.01f && hdg <= 1f && plinth && !baked;
                        if (!ok) fail++;
                        sb.AppendLine($"| {(ok ? "PASS" : "FAIL")} | {ft.name} | {lx},{ly} | {pad} | {type} | {(v.OnTower ? "yes" : "NO")} | {axisOff * 100f:F1} | {baseOff * 100f:F1} | {dy * 100f:+0.0;-0.0} | {hdg:F1} | {(plinth ? "yes" : "NO")}{(baked ? ", baked copy still ON" : "")} |");
                        yield return new WaitForEndOfFrame();
                        sheets[type].Add(TopDown(pw, ap, $"{ft.name} {pad}"));
                        if (ft == g.StaticViews[forts[0].Id].transform && pad == TowerMounts.PadForSpot(ft, 16, 0))
                            System.IO.File.WriteAllBytes(System.IO.Path.Combine(shots, $"{level}_{type}_rts55.png"), Rts(pw).EncodeToPNG());
                        c.KillAt = W.Time;                                                                // gone next tick, the view hides
                        for (int k = 0; k < 2; k++) yield return null;
                    }
                    foreach (var h in hidden) if (h) h.SetActive(true);
                }
            }
            foreach (var kv in sheets) System.IO.File.WriteAllBytes(System.IO.Path.Combine(shots, $"{level}_{kv.Key}_topdown.png"), Sheet(kv.Value).EncodeToPNG());
            Report = $"TOWER GUN GATE {level} play: {(fail == 0 && n > 0 ? "PASS" : $"FAIL ({fail})")}, {n} cases ({forts.Count} forts x 4 pads x {Types.Length} types)\n\n" + sb;
            var dir = System.IO.Path.Combine(Records, "qc"); System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, $"tower_gun_gate_{level}_play.md"), "# Tower gun gate (Play, tower_gun_fix)\n\n" + Report);
            Debug.Log("[TowerGunGate] " + Report);
            if (rt) rt.Release();
            Destroy(gameObject);
        }

        Texture2D Grab()
        {
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            var prev = RenderTexture.active; RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, Tile, Tile), 0, 0); tex.Apply(); RenderTexture.active = prev;
            var o = new Texture2D(Tile, Tile, TextureFormat.RGB24, false); o.SetPixels(tex.GetPixels()); return o;
        }

        /// <summary>Top-down orthographic 6 m square on the pad centre (north up): cyan cross + 5 cm ring = cap centre, magenta = axis.</summary>
        Texture2D TopDown(Vector3 pw, Vector3 axis, string label)
        {
            cam.orthographic = true; cam.orthographicSize = Half;
            cam.transform.SetPositionAndRotation(pw + Vector3.up * 40f, Quaternion.Euler(90f, 0f, 0f));
            var o = Grab(); float k = Tile / (2f * Half); int cx = Tile / 2, cy = Tile / 2;
            for (int i = -14; i <= 14; i++) { o.SetPixel(cx + i, cy, Color.cyan); o.SetPixel(cx, cy + i, Color.cyan); }
            for (int d = 0; d < 64; d++) { float an = d * Mathf.PI / 32f; o.SetPixel(cx + Mathf.RoundToInt(Mathf.Cos(an) * 0.05f * k), cy + Mathf.RoundToInt(Mathf.Sin(an) * 0.05f * k), Color.cyan); }
            int ax = cx + Mathf.RoundToInt((axis.x - pw.x) * k), ay = cy + Mathf.RoundToInt((axis.z - pw.z) * k);
            for (int i = -2; i <= 2; i++) for (int j = -2; j <= 2; j++) o.SetPixel(ax + i, ay + j, Color.magenta);
            o.Apply(); return o;
        }

        /// <summary>Review view: RTS pitch 55 deg from the south, FOV 32, close.</summary>
        Texture2D Rts(Vector3 pw)
        {
            cam.orthographic = false; cam.fieldOfView = 32f;
            var dir = Quaternion.Euler(55f, 0f, 0f) * Vector3.forward;
            cam.transform.SetPositionAndRotation(pw - dir * 14f, Quaternion.Euler(55f, 0f, 0f));
            return Grab();
        }

        static Texture2D Sheet(List<Texture2D> tiles)
        {
            int cols = 4, rows = Mathf.Max(1, (tiles.Count + cols - 1) / cols);
            var s = new Texture2D(cols * Tile, rows * Tile, TextureFormat.RGB24, false);
            for (int i = 0; i < tiles.Count; i++) s.SetPixels((i % cols) * Tile, (rows - 1 - i / cols) * Tile, Tile, Tile, tiles[i].GetPixels());
            s.Apply(); return s;
        }
    }
}
