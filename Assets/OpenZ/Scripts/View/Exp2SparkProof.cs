using System.Collections;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// ground at the RTS camera in slow motion (timeScale 0.25), a frame crop of +-1.5U around each blast at 0.2 / 0.5 / 1.0 / 1.5 s
    /// (the side-by-side strips with the reference are made from these offline) and the live spark stats (count, distance, speed,
    /// streak length in U, how many leave 1 / 2 / 3U, HDR head) at 0.2 / 0.4 / 0.5 / 0.8 / 1.0 / 1.2 / 1.5 s. RunFight: a real
    /// red-vs-blue vehicle fight at the neutral radar with Exp2Force 1 (session only) for a review clip; OBS records it from outside.
    /// </summary>
    public class Exp2SparkProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static void RunStrips() { Report = "running strips"; new GameObject("Exp2SparkProof").AddComponent<Exp2SparkProof>().mode = 0; }
        public static void RunFight() { Report = "running fight"; new GameObject("Exp2SparkProof").AddComponent<Exp2SparkProof>().mode = 1; }

        int mode; ZGame g; ZWorld W; RtsCamera rts; OpenZFxPlayer fx; string dir; readonly StringBuilder sb = new StringBuilder();
        void Log(string s) { sb.AppendLine(s); Debug.Log("[Exp2SparkProof] " + s); }

        ZObject Spawn(string cat, string type, int tx, int ty, Team team)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, cat == "robot" ? 1 : 2, cat == "robot", out tx, out ty);
            var o = cat == "robot" ? W.CreateRobotGroup(type, tx * 16, ty * 16, team) : W.CreateObject(cat, type, tx * 16, ty * 16, team);
            if (o == null) { Log($"spawn {cat}:{type} failed"); return null; }
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            return o;
        }
        void Attack(ZObject u, ZObject t) { if (u != null && t != null) W.SendWaypoints(u.Owner, u, new[] { new ZWaypoint { mode = WpMode.Attack, refId = t.Id, x = t.CenterX, y = t.CenterY, playerGiven = true } }); }

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; rts = FindFirstObjectByType<RtsCamera>(); fx = OpenZFxPlayer.I;
            dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records/milestones/exp2_sparks_fix"));
            System.IO.Directory.CreateDirectory(dir);
            if (W == null || !fx) { Report = "FAIL no world / fx"; Destroy(gameObject); return; }
            StartCoroutine(mode == 1 ? Fight() : Strips());
        }

        void Save(string name)
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, name), sb.ToString());
        }

        // ============================================================================================ strips
        IEnumerator Strips()
        {
            var red = W.Objects.FirstOrDefault(o => o.IsFort && o.Owner == g.player && !o.Destroyed);
            var blue = W.Objects.FirstOrDefault(o => o.IsFort && o.Owner != g.player && o.Owner != Team.Neutral && !o.Destroyed);
            if (red == null || blue == null) { Report = "FAIL forts"; yield break; }
            Vector3 rw = g.CenterWorld(red), bw = g.CenterWorld(blue), dirT = (bw - rw); dirT.y = 0f; dirT.Normalize();
            int forceWas = OpenZFxPlayer.Exp2Force; OpenZFxPlayer.Exp2Force = 0;      // no other exp2 while measuring (session only)
            Log($"exp2_sparks_fix strips, L02, {Screen.width}x{Screen.height}, test mode {PlayerPrefs.GetInt("oz_testmode", 0)}, NightK {NightGrade.NightK:F2}, taper {OpenZFxPlayer.Exp2SparkTaper}");
            var kinds = new[] { Exp2Kind.Medium, Exp2Kind.Big };
            float[] Us = { 5.1f, 4.83f }, Ss = { 1.7f, 2.1f }, cH = { 0.54f, 0.9f };
            float[] shots = { 0.2f, 0.5f, 1.0f, 1.5f }, stats = { 0.2f, 0.4f, 0.5f, 0.8f, 1.0f, 1.2f, 1.5f, 2.0f };
            for (int k = 0; k < kinds.Length; k++)
            {
                Vector3 spot = rw + dirT * (34f + 16f * k) + new Vector3(dirT.z, 0f, -dirT.x) * 6f;
                for (int tries = 0; tries < 40; tries++)
                {   // dry open ground: no water tile within 1.5U (the first run hit the river)
                    bool wet = false;
                    for (int a = 0; a < 8 && !wet; a++) { var o = new Vector3(Mathf.Cos(a * 0.785f), 0f, Mathf.Sin(a * 0.785f)) * (1.5f * Us[k]); wet = OpenZFxPlayer.IsWaterTile(spot + o) || OpenZFxPlayer.IsWaterTile(spot); }
                    if (!wet) break;
                    spot += new Vector3(Mathf.Cos(tries * 2.4f), 0f, Mathf.Sin(tries * 2.4f)) * (4f + tries);
                }
                spot.y = g.GroundY(spot.x, spot.z);
                if (rts) { rts.FlyTo(spot); rts.SetHeightImmediate(34f); }
                yield return new WaitForSecondsRealtime(2.5f);
                Time.timeScale = 0.25f;
                var at = spot + Vector3.up * 0.6f;
                int id = 900001 + k * 17;
                float t0 = Time.time;
                fx.Exp2(at, Us[k], kinds[k], id, Ss[k], FxBlastKind.Vehicle, false, true);
                float U = Us[k];
                string last = OpenZFxPlayer.Exp2Log.Count > 0 ? OpenZFxPlayer.Exp2Log[OpenZFxPlayer.Exp2Log.Count - 1] : "";
                int ui = last.IndexOf("U="); if (ui >= 0) { var tok = last.Substring(ui + 2).Split(' ')[0]; float.TryParse(tok, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out U); }
                var centre = new Vector3(spot.x, spot.y + 0.05f + cH[k] * U, spot.z);
                Log($"--- {kinds[k]} at ({spot.x:0.0},{spot.y:0.0},{spot.z:0.0}) U {U:0.00} m (realised), centre {cH[k]:0.00}U above ground; log: {last}");
                int si = 0, fi = 0;
                while (si < stats.Length || fi < shots.Length)
                {
                    yield return new WaitForEndOfFrame();
                    float age = Time.time - t0;
                    if (si < stats.Length && age >= stats[si]) { Log($"{kinds[k]} +{age * 1000f:0} ms: {fx.Exp2SparkStats(centre, U)}"); si++; }
                    if (fi < shots.Length && age >= shots[fi])
                    {
                        var cam = Camera.main;
                        var tex = ScreenCapture.CaptureScreenshotAsTexture();
                        Vector3 sp = cam.WorldToScreenPoint(centre), sr = cam.WorldToScreenPoint(centre + cam.transform.right * U);
                        float pxU = Mathf.Abs(sr.x - sp.x);
                        int half = Mathf.RoundToInt(1.5f * pxU), cx = Mathf.RoundToInt(sp.x), cy = Mathf.RoundToInt(sp.y);
                        int x0 = Mathf.Clamp(cx - half, 0, tex.width - 1), y0 = Mathf.Clamp(cy - half, 0, tex.height - 1);
                        int w = Mathf.Clamp(cx + half, 0, tex.width) - x0, h = Mathf.Clamp(cy + half, 0, tex.height) - y0;
                        var crop = new Texture2D(w, h, TextureFormat.RGB24, false);
                        crop.SetPixels(tex.GetPixels(x0, y0, w, h)); crop.Apply();
                        string name = $"ours_{kinds[k]}_{Mathf.RoundToInt(shots[fi] * 1000f):0000}ms.png";
                        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name), crop.EncodeToPNG());
                        if (fi == 1) System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"full_{kinds[k]}_{Mathf.RoundToInt(shots[fi] * 1000f):0000}ms.png"), tex.EncodeToPNG());
                        Log($"frame {name} at +{age * 1000f:0} ms (U = {pxU:0} px, crop +-1.5U = {w}x{h} px, centre px {cx},{cy}, clipped {(w < 2 * half || h < 2 * half ? "yes" : "no")})");
                        Destroy(tex); Destroy(crop);
                        fi++;
                    }
                }
                Time.timeScale = 1f;
                yield return new WaitForSecondsRealtime(1.5f);
                Log(fx.Exp2Census());
            }
            Time.timeScale = 1f;
            OpenZFxPlayer.Exp2Force = forceWas;
            Save("proof_strips.txt");
            Report = "strips done";
            Destroy(gameObject);
        }

        IEnumerator Fight()
        {
            var red = W.Objects.FirstOrDefault(o => o.IsFort && o.Owner == g.player && !o.Destroyed);
            var radar = W.Objects.FirstOrDefault(o => o.Kind == ZKind.Building && o.Type == "radar" && !o.Destroyed);
            if (red == null) { Report = "FAIL fort"; yield break; }
            var rp = new Vector2(red.CenterX / 16f, red.CenterY / 16f);
            Vector2 tgt = radar != null ? new Vector2(radar.CenterX / 16f, radar.CenterY / 16f) : rp + new Vector2(-20f, 14f);
            var dirT = (rp - tgt).normalized; var side = new Vector2(-dirT.y, dirT.x);
            Vector2 B = tgt + dirT * 5f, R = tgt + dirT * 15f;
            int bx = Mathf.RoundToInt(B.x), by = Mathf.RoundToInt(B.y), rx = Mathf.RoundToInt(R.x), ry = Mathf.RoundToInt(R.y);
            int S(float v) => Mathf.RoundToInt(v);
            var b1 = Spawn("vehicle", "light", bx, by, Team.Blue);
            var b2 = Spawn("vehicle", "jeep", bx + S(side.x * 4), by + S(side.y * 4), Team.Blue);
            var b3 = Spawn("vehicle", "heavy", bx - S(side.x * 4), by - S(side.y * 4), Team.Blue);
            var b4 = Spawn("vehicle", "apc", bx + S(side.x * 2 - dirT.x * 3), by + S(side.y * 2 - dirT.y * 3), Team.Blue);
            var bg = Spawn("robot", "grunt", bx - S(side.x * 2 + dirT.x * 2), by - S(side.y * 2 + dirT.y * 2), Team.Blue);
            var r1 = Spawn("vehicle", "heavy", rx, ry, g.player);
            var r2 = Spawn("vehicle", "light", rx + S(side.x * 4), ry + S(side.y * 4), g.player);
            var r3 = Spawn("vehicle", "light", rx - S(side.x * 4), ry - S(side.y * 4), g.player);
            var r4 = Spawn("vehicle", "missile_launcher", rx + S(dirT.x * 4), ry + S(dirT.y * 4), g.player);
            foreach (var o in new[] { b1, b2, b3, b4 }) if (o != null) o.Health = Mathf.Max(1, o.MaxHealth / 4);
            if (b3 != null) b3.Health = Mathf.Max(1, b3.MaxHealth / 3);
            OpenZFxPlayer.Exp2Force = 1; OpenZFxPlayer.Exp2LogConsole = true;                  // session only: every eligible blast is exp2
            int e0 = OpenZFxPlayer.Exp2Total;
            var mid = Vector3.Lerp(g.CenterWorld(b1 ?? red), g.CenterWorld(r1 ?? red), 0.45f);
            if (rts) { rts.FlyTo(mid); rts.ZoomTo(44f); }
            Log($"fight at the radar {(radar != null ? radar.Id.ToString() : "none")}: blue light/jeep/heavy/apc (1/4 hp) + grunts at ({bx},{by}), red heavy/2 light/MLRS at ({rx},{ry})");
            yield return new WaitForSecondsRealtime(2.5f);
            Report = "fight started";
            Attack(r1, b3); Attack(r2, b1); Attack(r3, radar != null ? radar : b2); Attack(r4, b4);   // r3 shells the radar (BuildingHit)
            Attack(b1, r2); Attack(b3, r1); Attack(b2, r3);
            if (bg != null) { Attack(bg, r2); foreach (var m in bg.Minions) Attack(m, r2); }
            float t = 0f;
            while (t < 22f) { t += Time.unscaledDeltaTime; yield return null; }
            Log($"fight: {OpenZFxPlayer.Exp2Total - e0} exp2 in 22 s ({fx.Exp2Census()})");
            OpenZFxPlayer.Exp2Force = -1; OpenZFxPlayer.Exp2LogConsole = false;
            Save("proof_fight.txt");
            Report = "fight done";
            Destroy(gameObject);
        }
    }
}
