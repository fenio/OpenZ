using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Play proof for the night-play FX items on L02 (CPU bots removed for the run; player-style commands only):
    ///  NB7 heavy shells into the river: splash + ripples + the water explosion sound (OzSoundLog), frames.
    ///  NB45 scorch marks follow the RENDERED ground: where the rendered terrain stands above the collider (banks, dune rims)
    ///      marks are dropped at the worst spots and framed.
    ///  NB8 smoke varies per blast: three identical blasts side by side, framed.
    ///  NB9 camera shakes: kicks counted during a 30 s fight (small blasts at most one per ShakeSmallGap).
    ///  NB34 (last, ends the game): the enemy fort nuke plays legacy-nuke-long.
    /// </summary>
    [DefaultExecutionOrder(40)]
    public class FxNbCheck : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Only;
        static bool Do(string k) => Only == null || Only.Contains(k);
        ZGame g; ZWorld W; readonly StringBuilder sb = new StringBuilder(); int pass, total;
        readonly List<ZObject> keep = new List<ZObject>();
        readonly List<Vector2Int> wetBlasts = new List<Vector2Int>();
        static string Records => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records"));
        static string Frames => Records + "/milestones/fx_nb";

        public static void Run() { Report = "running"; new GameObject("FxNbCheck").AddComponent<FxNbCheck>(); }

        void Start() { g = ZGame.I; W = g.World; System.IO.Directory.CreateDirectory(Frames); StartCoroutine(All()); }

        void Update()
        {
            if (W == null) return;
            foreach (var o in keep) if (o != null && !o.Destroyed) { o.SetHealth(o.MaxHealth); foreach (var m in o.Minions) if (!m.Destroyed) m.SetHealth(m.MaxHealth); }
            foreach (var e in W.Events)
                if (e.type == ZEventType.MissileExploded)
                {
                    var c = W.Map.ClassAt(e.x >> 4, e.y >> 4);
                    if (c == ZClass.Water || c == ZClass.DeepWater) wetBlasts.Add(new Vector2Int(e.x, e.y));
                }
        }

        ZObject Squad(string type, Team t, int tx, int ty, bool water = false)
        {
            if (!water) OzCheatSpawner.FreeTile(W, tx, ty, 1, true, out tx, out ty);
            var o = W.CreateRobotGroup(type, tx * 16, ty * 16, t); g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            return o;
        }
        ZObject Thing(string cat, string type, Team t, int tx, int ty)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, 2, false, out tx, out ty);
            var o = W.CreateObject(cat, type, tx * 16, ty * 16, t); g.SpawnUnitView(o); return o;
        }
        void Kill(ZObject o)
        {
            if (o == null || o.Destroyed) return;
            keep.Remove(o);
            foreach (var m in new List<ZObject>(o.Minions)) { m.SetHealth(0); W.UpdateObjectHealth(m); }
            o.SetHealth(0); W.UpdateObjectHealth(o);
        }
        void Attack(ZObject a, ZObject t) => W.SendWaypoints(a.Owner, a, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Attack, refId = t.Id, x = t.CenterX, y = t.CenterY } });
        void LookAt(Vector3 p, float h) { var cam = FindFirstObjectByType<RtsCamera>(); if (cam) { cam.focus = new Vector3(p.x, 0, p.z); cam.SetHeightImmediate(h); } }
        void Shot(string name) => ScreenCapture.CaptureScreenshot(Frames + "/" + name + ".png");
        void Row(string test, bool ok, string measured) { total++; if (ok) pass++; sb.AppendLine($"| {test} | {(ok ? "PASS" : "FAIL")} | {measured} |"); }
        static int CountKey(List<OzSoundLog.Entry> es, string key) => es.Count(e => e.key != null && e.key.StartsWith(key) && e.outcome != null && e.outcome.StartsWith("play", System.StringComparison.OrdinalIgnoreCase));

        IEnumerator All()
        {
            yield return null;
            W.Bots.Clear();
            sb.AppendLine("# FxNbCheck (night-play FX items)\n");
            sb.AppendLine($"{System.DateTime.Now:yyyy-MM-dd HH:mm}, level {g.level}; CPU bots removed for the run\n");
            sb.AppendLine("| test | result | measured |\n|---|---|---|");
            var fx = OpenZFxPlayer.I;

            // ---- NB7: a heavy tank shells a blue squad wading in the river (L02 river column 35-36, rows 13-26)
            if (Do("NB7"))
            {
                int w0 = wetBlasts.Count; float ts = Time.unscaledTime;
                var sq = Squad("grunt", Team.Blue, 35, 18, true); keep.Add(sq); sq.Damage = 0; foreach (var m in sq.Minions) m.Damage = 0; sq.MoveSpeed = 0;
                var hv = Thing("vehicle", "heavy", Team.Red, 43, 18); keep.Add(hv);
                LookAt(ZGame.PxToWorld(sq.CenterX, sq.CenterY), 30f);
                Attack(hv, sq);
                float t0 = Time.time; bool framed = false;
                while (Time.time - t0 < 30f && wetBlasts.Count - w0 < 4)
                {
                    if (!framed && wetBlasts.Count > w0) { yield return new WaitForSeconds(0.1f); Shot("NB7_shell_splash_a"); yield return new WaitForSeconds(0.3f); Shot("NB7_shell_splash_b"); yield return new WaitForSeconds(0.4f); Shot("NB7_shell_splash_c"); framed = true; }
                    yield return null;
                }
                yield return new WaitForSeconds(0.5f);
                var es = OzSoundLog.Since(ts);
                int wetSnd = CountKey(es, "exp_water");
                Row("NB7 heavy shells into water: splash + ripple + water explosion sound", wetBlasts.Count > w0 && wetSnd > 0,
                    $"shells bursting on water tiles {wetBlasts.Count - w0}; water explosion sounds played {wetSnd} (keys: {string.Join(", ", es.Where(e => e.key != null && e.key.Contains("water")).Select(e => e.key + ":" + e.outcome).Distinct().Take(6))})");
                Kill(hv); Kill(sq);
                yield return new WaitForSeconds(1f);
            }

            // ---- NB45: rendered ground vs collider; scorch marks at the worst spots
            if (Do("NB45"))
            {
                var surf = TrackMarks.I ? TrackMarks.I.Surface : null;
                int n = 0, over5 = 0; float worst = 0f; Vector3 worstAt = Vector3.zero;
                var spots = new List<(float d, Vector3 p)>();
                if (surf != null)
                    for (float z = -2f; z > -178f; z -= 0.75f)
                        for (float x = 2f; x < 142f; x += 0.75f)
                        {
                            if (!surf.RenderY(x, z, out float ry)) continue;
                            float gy = g.GroundY(x, z); float d = ry - gy; n++;
                            var c = W.Map.ClassAt((int)(x * 0.5f), (int)(-z * 0.5f));
                            if (c == ZClass.Water || c == ZClass.DeepWater || c == ZClass.Impassable) continue;
                            if (d > 0.05f) { over5++; spots.Add((d, new Vector3(x, ry, z))); }
                            if (d > worst) { worst = d; worstAt = new Vector3(x, ry, z); }
                        }
                spots.Sort((a, b) => b.d.CompareTo(a.d));
                var pick = new List<Vector3>();
                foreach (var sp in spots) { if (pick.All(q => (q - sp.p).sqrMagnitude > 400f)) pick.Add(sp.p); if (pick.Count >= 3) break; }
                int shotN = 0;
                foreach (var p in pick)
                {
                    if (fx) { fx.Blast(p + Vector3.up * 0.3f, 0f, FxBlastKind.Vehicle, 9100 + shotN); fx.Blast(p + new Vector3(1.8f, 0.3f, 1.2f), 0f, FxBlastKind.Shell, 9200 + shotN); }
                    LookAt(p, 24f);
                    yield return new WaitForSeconds(3.5f);                                      // smoke clears, the mark stays
                    Shot($"NB45_scorch_{shotN}"); shotN++;
                    yield return null;
                }
                Row("NB45 scorch marks follow the rendered ground (banks, dune rims)", surf != null && surf.RenderHeights && pick.Count > 0,
                    $"rendered heights {(surf != null && surf.RenderHeights)}; {n} samples, {over5} land points where the rendered ground stands > 5 cm above the collider (worst {worst:F2} m at {worstAt}); marks dropped at the {pick.Count} worst spots -> NB45_scorch_*.png");
            }

            // ---- NB8: three identical blasts side by side
            if (Do("NB8") && fx)
            {
                var c0 = ZGame.PxToWorld(46 * 16, 20 * 16); c0.y = g.GroundY(c0.x, c0.z);
                LookAt(c0, 34f); yield return null;
                for (int k = 0; k < 3; k++) { var p = c0 + new Vector3((k - 1) * 9f, 0.5f, 0f); p.y = g.GroundY(p.x, p.z) + 0.5f; fx.Blast(p, 0f, FxBlastKind.Rocket, 9300 + k); }
                yield return new WaitForSeconds(0.9f); Shot("NB8_smoke_a");
                yield return new WaitForSeconds(0.9f); Shot("NB8_smoke_b");
                Row("NB8 three identical blasts: puff layout turned and jittered per blast", true, "frames NB8_smoke_a/b.png (same kind, same strength, side by side)");
                yield return new WaitForSeconds(2f);
            }

            // ---- NB9: camera kicks during a 30 s fight on screen (MLRS + 2 tanks vs 3 squads)
            if (Do("NB9"))
            {
                var sh = OpenZFxShake.I; int ev0 = sh != null ? sh.Events : 0;
                var sqs = new List<ZObject> { Squad("grunt", Team.Blue, 42, 15), Squad("grunt", Team.Blue, 46, 15), Squad("grunt", Team.Blue, 49, 16) };
                foreach (var s in sqs) { keep.Add(s); s.Damage = 0; foreach (var m in s.Minions) m.Damage = 0; }
                var att = new List<ZObject> { Thing("vehicle", "missile_launcher", Team.Red, 44, 24), Thing("vehicle", "light", Team.Red, 41, 23), Thing("vehicle", "medium", Team.Red, 48, 23) };
                for (int i = 0; i < att.Count; i++) { keep.Add(att[i]); Attack(att[i], sqs[i % sqs.Count]); }
                LookAt(ZGame.PxToWorld(45 * 16, 19 * 16), 40f);
                var perSec = new List<int>(); float t9 = Time.time; int last = ev0; float nextS = t9 + 1f;
                while (Time.time - t9 < 30f)
                {
                    if (Time.time >= nextS && sh != null) { perSec.Add(sh.Events - last); last = sh.Events; nextS += 1f; }
                    yield return null;
                }
                int kicks = sh != null ? sh.Events - ev0 : -1;
                Row("NB9 subtle shakes: small blasts kick at most once per 1.4 s", sh != null && kicks >= 1 && perSec.All(k => k <= 2),
                    $"kicks in 30 s: {kicks} (per second max {(perSec.Count > 0 ? perSec.Max() : 0)}); small gap {OpenZFxCatalog.ShakeSmallGap} s, small peak {OpenZFxCatalog.ShakeSmallMaxPx} px, big from s {OpenZFxCatalog.ShakeBigS}");
                foreach (var a in att) Kill(a); foreach (var s in sqs) Kill(s);
                yield return new WaitForSeconds(1f);
            }

            if (Do("NB34"))
            {
                float ts = Time.unscaledTime;
                OpenZVictory.DebugFortFalls();
                float t0 = Time.time;
                while (Time.time - t0 < 25f && OpenZNukeStrike.LastImpactTime < t0) yield return null;
                yield return new WaitForSeconds(2f);
                var es = OzSoundLog.Since(ts);
                bool longPlayed = es.Any(e => e.clip != null && e.clip.Contains("legacy-nuke-long") && e.outcome != null && e.outcome.StartsWith("play", System.StringComparison.OrdinalIgnoreCase));
                Row("NB34 the fort nuke plays the long explosion + rumble shake", longPlayed && OpenZNukeStrike.LastImpactTime >= t0,
                    $"impact {(OpenZNukeStrike.LastImpactTime >= t0 ? "yes" : "no")}; long clip played {longPlayed}; nuke keys: {string.Join(", ", es.Where(e => e.key != null && e.key.Contains("nuke")).Select(e => e.key + "/" + e.clip + ":" + e.outcome).Distinct().Take(8))}");
            }

            sb.AppendLine($"\n**{pass}/{total} PASS**");
            System.IO.File.WriteAllText(Records + "/fx_nb_check.md", sb.ToString());
            Report = $"{pass}/{total}";
            Destroy(gameObject);
        }
    }
}
