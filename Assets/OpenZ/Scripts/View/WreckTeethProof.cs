#if !OZ_RELEASE
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    ///  * Strip(tag, tod, clip, "light,heavy,medium,missile_launcher", lows, lane): one row of vehicles / cannons (red paint, no
    ///    damage), WreckFx.Force = 1 (every one breaks), all killed by the sim (health 0 -> the real view death), frames frozen
    ///    (timeScale 0) at 0.35 s / 3 s / 8 s after the kill from the game camera (h 55, pitch 55), a zoom (h 24) and, with lows,
    ///    a low close frame per wreck (h 7, pitch 30). clip = WreckPieces.ClipAtBinWalls for this strip (false = the old clamp, A/B).
    ///  * Palms(tag): the game camera + a low frame at the red-X palm, the west bridge palm and the spring palm, then a grunt squad and
    ///    a heavy tank sent past the red-X palm (scale + walk-under frames).
    ///  * FogAB(tag): L17 night, the same frozen frame with the fog-lamp pre-cull off (_AtmoLampCullOff 1 = the old path) and on;
    ///    the pixel difference and the GPU frame time of each.
    ///  * Flyby(level, n): the menu flyby re-capture, the same calls as the approved clips (n 2 / 3 = CaptureKit.MenuFlyby, n 1 on
    /// </summary>
    public sealed class WreckTeethProof : MonoBehaviour
    {
        public static string Root = OzRelease.Milestones("task_owner_0720_wreck_teeth");
        public static string Status = "idle";
        public static readonly StringBuilder Log = new StringBuilder();
        public static string LastLane = "";
        static WreckTeethProof host;

        static WreckTeethProof Host()
        {
            if (!host) host = new GameObject("WreckTeethProof").AddComponent<WreckTeethProof>();
            return host;
        }

        static void Note(string s) { Log.AppendLine(System.DateTime.Now.ToString("HH:mm:ss") + " " + s); Debug.Log("[WreckTeethProof] " + s); Status = s; }

        static void Kill(ZWorld W, ZObject o) { o.SetHealth(0); W.UpdateObjectHealth(o, -1); }

        /// <summary>Clears WreckPieces' per-mesh cut cache (after an A/B strip with the old clamp, so the next break re-cuts).</summary>
        public static int ClearChunkCache()
        {
            var f = typeof(WreckPieces).GetField("chunkCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var d = f != null ? f.GetValue(null) as IDictionary : null; if (d == null) return -1;
            int n = d.Count; d.Clear(); return n;
        }

        RtsCamera rts; float minH0, pitch0, h00; Vector3 focus0;
        void CamSave() { rts = FindFirstObjectByType<RtsCamera>(); if (!rts) return; minH0 = rts.minHeight; pitch0 = rts.pitch; h00 = rts.height; focus0 = rts.focus; rts.minHeight = 4f; }
        void CamRestore() { if (!rts) return; rts.pitch = pitch0; rts.minHeight = minH0; rts.focus = focus0; rts.SetHeightImmediate(h00); }
        void Cam(Vector3 at, float h, float pitch) { if (!rts) return; rts.pitch = pitch; rts.focus = new Vector3(at.x, 0f, at.z); rts.SetHeightImmediate(h); }

        IEnumerator Shot(string dir, string name)
        {
            yield return null; yield return null;
            yield return new WaitForEndOfFrame();
            ProofCapture.SaveJpg(Path.Combine(dir, name + ".jpg"), 0, 95);
        }

        /// <summary>A 15 x 7 tile strip of plain ground / road with no living object, nearest to `near` (world).</summary>
        static string Lane(ZWorld W, Vector3 near)
        {
            var m = W.Map; int ftx = (int)(near.x * 0.5f), fty = (int)(-near.z * 0.5f);
            int best = int.MaxValue, bx = -1, by = -1;
            for (int ty = 8; ty < m.height - 8; ty++)
                for (int tx = 10; tx < m.width - 10; tx++)
                {
                    int d = (tx - ftx) * (tx - ftx) + (ty - fty) * (ty - fty); if (d >= best) continue;
                    bool ok = true;
                    for (int y = ty - 3; y <= ty + 3 && ok; y++) for (int x = tx - 8; x <= tx + 8 && ok; x++) { var c = m.ClassAt(x, y); if (c != ZClass.Ground && c != ZClass.Road) ok = false; }
                    if (!ok) continue;
                    int x0 = (tx - 8) * 16, x1 = (tx + 8) * 16, y0 = (ty - 3) * 16, y1 = (ty + 3) * 16;
                    foreach (var o in W.Objects) if (o != null && !o.Destroyed && o.CenterX + o.WidthPix / 2 > x0 && o.CenterX - o.WidthPix / 2 < x1 && o.CenterY + o.HeightPix / 2 > y0 && o.CenterY - o.HeightPix / 2 < y1) { ok = false; break; }
                    if (ok) { best = d; bx = tx; by = ty; }
                }
            return bx < 0 ? "none" : bx + "," + by;
        }

        // ------------------------------------------------------------------ wreck strips
        public static string Strip(string tag, float tod, bool clip, string types, bool lows, string lane)
        {
            var g = ZGame.I; if (!g || g.World == null) return "no game";
            Status = "running strip " + tag;
            Host().StartCoroutine(Host().StripRun(tag, tod, clip, types.Split(','), lows, lane));
            return "strip " + tag + " started on " + g.level;
        }

        IEnumerator StripRun(string tag, float tod, bool clip, string[] types, bool lows, string lane)
        {
            var g = ZGame.I; var W = g.World;
            string dir = Path.Combine(Root, g.level + "_strips", tag); Directory.CreateDirectory(dir);
            CamSave();
            var dn = FindFirstObjectByType<DayNight>(); bool run0 = dn && dn.running;
            if (dn) { dn.running = false; dn.timeOfDay = tod; dn.Apply(tod); }
            int force0 = WreckFx.Force; WreckFx.Force = 1;
            bool clip0 = WreckPieces.ClipAtBinWalls; WreckPieces.ClipAtBinWalls = clip;
            int sets0 = WreckPieces.ClippedSets, pieces0 = WreckPieces.ClippedPieces, own0 = WreckPieces.OwnClips, chunk0 = WreckPieces.Chunked;
            var bots = new List<ZBot>(W.Bots); W.Bots.Clear();
            if (string.IsNullOrEmpty(lane)) lane = Lane(W, rts ? rts.focus : Vector3.zero);
            LastLane = lane;
            var sp = lane.Split(',');
            if (sp.Length != 2) { Note(tag + ": no lane"); Restore(force0, clip0, dn, run0, bots); yield break; }
            int tx = int.Parse(sp[0]), ty = int.Parse(sp[1]);
            var objs = new List<ZObject>(); var views = new List<UnitView>();
            for (int i = 0; i < types.Length; i++)
            {
                string t = types[i].Trim(); if (t.Length == 0) continue;
                int x = tx - 6 + i * (types.Length > 1 ? 12 / (types.Length - 1) : 0);
                var o = W.CreateObject(ZBuildLists.IsCannon(t) ? "cannon" : "vehicle", t, x * 16, ty * 16, Team.Red);
                if (o == null) { Note(tag + ": spawn failed " + t); continue; }
                g.SpawnUnitView(o); o.Damage = 0; o.AttackRadius = 0;
                objs.Add(o);
            }
            yield return new WaitForSeconds(2.5f);
            var pos = new List<Vector3>(); Vector3 c = Vector3.zero;
            foreach (var o in objs) { UnitView v; if (g.Views.TryGetValue(o.Id, out v) && v) { views.Add(v); pos.Add(v.transform.position); c += v.transform.position; } }
            if (pos.Count == 0) { Note(tag + ": no views"); Restore(force0, clip0, dn, run0, bots); yield break; }
            c /= pos.Count;
            Cam(c, 55f, 55f); yield return Shot(dir, tag + "_0_alive_h55");
            foreach (var o in objs) if (!o.Destroyed) Kill(W, o);
            float t0 = Time.time;
            foreach (var at in new[] { 0.35f, 3f, 8f })
            {
                while (Time.time - t0 < at) yield return null;
                float ts = Time.timeScale; Time.timeScale = 0f;
                string m = at.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "s";
                Cam(c, 55f, 55f); yield return Shot(dir, tag + "_" + m + "_h55");
                Cam(c, 24f, 55f); yield return Shot(dir, tag + "_" + m + "_h24");
                if (lows)
                    for (int i = 0; i < pos.Count; i++)
                    {
                        Cam(pos[i], 7f, 30f);
                        yield return Shot(dir, tag + "_" + m + "_low_" + (i < objs.Count ? objs[i].Type : "u" + i));
                    }
                Time.timeScale = ts;
            }
            Note($"{tag} ({g.level}, tod {tod:F2}, ClipAtBinWalls {clip}, lane {lane}): {string.Join("/", types)}; clipped sets +{WreckPieces.ClippedSets - sets0} pieces +{WreckPieces.ClippedPieces - pieces0} own cuts +{WreckPieces.OwnClips - own0} chunked +{WreckPieces.Chunked - chunk0}; ClipLast '{WreckPieces.ClipLast}'");
            CamRestore();
            Restore(force0, clip0, dn, run0, bots);
            Status = "done strip " + tag;
        }

        void Restore(int force0, bool clip0, DayNight dn, bool run0, List<ZBot> bots)
        {
            WreckFx.Force = force0; WreckPieces.ClipAtBinWalls = clip0;
            if (dn) dn.running = run0;
            var W = ZGame.I ? ZGame.I.World : null;
            if (W != null) { W.Bots.Clear(); W.Bots.AddRange(bots); }
        }

        // ------------------------------------------------------------------ palms
        public static string Palms(string tag)
        {
            var g = ZGame.I; if (!g || g.World == null) return "no game";
            Status = "running palms " + tag;
            Host().StartCoroutine(Host().PalmsRun(tag));
            return "palms started";
        }

        IEnumerator PalmsRun(string tag)
        {
            var g = ZGame.I; var W = g.World;
            string dir = Path.Combine(Root, "palms"); Directory.CreateDirectory(dir);
            CamSave();
            var dn = FindFirstObjectByType<DayNight>(); bool run0 = dn && dn.running;
            if (dn) { dn.running = false; dn.timeOfDay = 0.12f; dn.Apply(0.12f); }
            var bots = new List<ZBot>(W.Bots); W.Bots.Clear();
            var spots = new[] { ("redx", new Vector3(79f, 0f, -99f)), ("wbridge", new Vector3(42.75f, 0f, -62.85f)), ("spring", new Vector3(70.35f, 0f, -5.65f)) };
            foreach (var (name, p) in spots)
            {
                Cam(p, 55f, 55f); yield return Shot(dir, tag + "_" + name + "_h55");
                Cam(p, 6f, 22f); yield return Shot(dir, tag + "_" + name + "_low");
            }
            // scale: a grunt squad and a heavy tank beside the red-X palm (tile 39,49), then walking past / under its crown
            var rx = spots[0].Item2; int ptx = Mathf.RoundToInt(rx.x / 2f), pty = Mathf.RoundToInt(-rx.z / 2f);
            int fx, fy; ZObject squad = null, tank = null;
            if (OzCheatSpawner.FreeTile(W, ptx - 3, pty, 1, true, out fx, out fy)) { squad = W.CreateRobotGroup("grunt", fx * 16, fy * 16, Team.Red); }
            if (squad != null) { g.SpawnUnitView(squad); foreach (var mm in squad.Minions) g.SpawnUnitView(mm); }
            if (OzCheatSpawner.FreeTile(W, ptx + 3, pty + 2, 2, false, out fx, out fy)) { tank = W.CreateObject("vehicle", "heavy", fx * 16, fy * 16, Team.Red); if (tank != null) g.SpawnUnitView(tank); }
            yield return new WaitForSeconds(2f);
            Cam(rx, 55f, 55f); yield return Shot(dir, tag + "_redx_scale_h55");
            Cam(rx, 22f, 55f); yield return Shot(dir, tag + "_redx_scale_h22");
            Cam(rx + new Vector3(0f, 0f, -2f), 6f, 22f); yield return Shot(dir, tag + "_redx_scale_low");
            if (squad != null) W.SendWaypoints(Team.Red, squad, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Move, refId = -1, x = (ptx + 3) * 16 + 8, y = pty * 16 + 8 } });
            if (tank != null) W.SendWaypoints(Team.Red, tank, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Move, refId = -1, x = (ptx - 3) * 16 + 8, y = (pty + 1) * 16 + 8 } });
            for (int k = 0; k < 4; k++)
            {
                yield return new WaitForSeconds(1.5f);
                Cam(rx, 22f, 55f); yield return Shot(dir, tag + "_redx_walk_" + k + "_h22");
                Cam(rx + new Vector3(0f, 0f, -2f), 6f, 22f); yield return Shot(dir, tag + "_redx_walk_" + k + "_low");
            }
            Note($"palms {tag}: 3 spots x (h55 + low), red-X scale + walk frames; squad {(squad != null ? squad.Id : -1)}, tank {(tank != null ? tank.Id : -1)}");
            CamRestore();
            if (dn) dn.running = run0;
            W.Bots.Clear(); W.Bots.AddRange(bots);
            Status = "done palms " + tag;
        }

        // ------------------------------------------------------------------ fog-lamp pre-cull A/B
        public static string FogAB(string tag, float x, float z, float h)
        {
            var g = ZGame.I; if (!g || g.World == null) return "no game";
            Status = "running fog " + tag;
            Host().StartCoroutine(Host().FogRun(tag, new Vector3(x, 0f, z), h));
            return "fog A/B started";
        }

        static float GpuMs(int frames, List<float> acc)
        {
            var ft = new FrameTiming[1];
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, ft) > 0 && ft[0].gpuFrameTime > 0) acc.Add((float)ft[0].gpuFrameTime);
            return 0f;
        }

        IEnumerator FogRun(string tag, Vector3 at, float h)
        {
            string dir = Path.Combine(Root, "fog_ab"); Directory.CreateDirectory(dir);
            CamSave();
            var dn = FindFirstObjectByType<DayNight>(); bool run0 = dn && dn.running;
            if (dn) { dn.running = false; dn.timeOfDay = 0.62f; dn.Apply(0.62f); }
            Cam(at, h, 55f);
            yield return new WaitForSeconds(2f);
            float ts = Time.timeScale; Time.timeScale = 0f;
            var gpu = new float[2]; var tex = new Texture2D[2];
            for (int k = 0; k < 2; k++)
            {
                Shader.SetGlobalFloat("_AtmoLampCullOff", k == 0 ? 1f : 0f);
                var acc = new List<float>();
                for (int f = 0; f < 90; f++) { yield return null; GpuMs(1, acc); }
                acc.Sort(); gpu[k] = acc.Count > 0 ? acc[acc.Count / 2] : -1f;
                yield return new WaitForEndOfFrame();
                tex[k] = ProofCapture.Frame();
                File.WriteAllBytes(Path.Combine(dir, tag + (k == 0 ? "_A_old_every_lamp.png" : "_B_precull.png")), tex[k].EncodeToPNG());
            }
            Shader.SetGlobalFloat("_AtmoLampCullOff", 0f);
            Time.timeScale = ts;
            float mae = ProofCapture.Mae(tex[0], tex[1]);
            var pa = tex[0].GetPixels32(); var pb = tex[1].GetPixels32(); int maxd = 0; long over1 = 0;
            for (int i = 0; i < pa.Length; i++)
            {
                int d = Mathf.Max(Mathf.Abs(pa[i].r - pb[i].r), Mathf.Max(Mathf.Abs(pa[i].g - pb[i].g), Mathf.Abs(pa[i].b - pb[i].b)));
                if (d > maxd) maxd = d; if (d > 1) over1++;
            }
            // the difference image, x16
            var diff = new Texture2D(tex[0].width, tex[0].height, TextureFormat.RGB24, false); var pd = new Color32[pa.Length];
            for (int i = 0; i < pa.Length; i++) { int d = Mathf.Min(255, 16 * Mathf.Max(Mathf.Abs(pa[i].r - pb[i].r), Mathf.Max(Mathf.Abs(pa[i].g - pb[i].g), Mathf.Abs(pa[i].b - pb[i].b)))); pd[i] = new Color32((byte)d, (byte)d, (byte)d, 255); }
            diff.SetPixels32(pd); diff.Apply(); File.WriteAllBytes(Path.Combine(dir, tag + "_diff_x16.png"), diff.EncodeToPNG());
            Destroy(diff); Destroy(tex[0]); Destroy(tex[1]);
            Note($"fog A/B {tag} ({ZGame.I.level} night 0.62 at {at.x:0},{at.z:0} h {h:0}, {Screen.width}x{Screen.height}): MAE {mae:F3}, max channel diff {maxd}/255, pixels over 1/255: {over1}; GPU ms median old {gpu[0]:F2} vs pre-cull {gpu[1]:F2}");
            CamRestore();
            if (dn) dn.running = run0;
            Status = "done fog " + tag;
        }

        // ------------------------------------------------------------------ menu flyby re-capture
        public static string Flyby(int n)
        {
            var g = ZGame.I; if (!g || g.World == null || !CaptureKit.I) return "no game / capture kit";
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/tmp/cine_" + g.level + "_" + n));
            Status = "running flyby " + g.level + " #" + n;
            Host().StartCoroutine(Host().FlybyRun(n, dir));
            return "flyby " + g.level + " #" + n + " -> " + dir;
        }

        IEnumerator FlybyRun(int n, string dir)
        {
            var g = ZGame.I; float t0 = Time.realtimeSinceStartup;
            if (n == 1 && g.level == "L17") yield return CaptureKit.I.MenuFlybyV2("storm", dir, 17f, 60, 2560, 1600, -1f, false);
            else if (n == 1) yield return CaptureKit.I.MenuFlybyV2("sunrise", dir, 17f, 60, 2560, 1600, -1f, false);
            else yield return CaptureKit.I.MenuFlyby(n, dir, 17f, 60, 2560, 1600);
            Note($"flyby {g.level} #{n}: {CaptureKit.CineFrames} frames -> {dir} in {Time.realtimeSinceStartup - t0:0} s; closest building {CaptureKit.CineMinBuilding:0.0} m, unit {CaptureKit.CineMinUnit:0.0} m; WreckPieces clipped sets {WreckPieces.ClippedSets}, ClipLast '{WreckPieces.ClipLast}'");
            Status = "done flyby " + g.level + " #" + n;
        }
    }
}
#endif
