using System.Collections;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace OpenZ
{
    /// <summary>
    /// firing rockets (trails, blasts), building-size smoke and a blast column; at the RTS camera and close, by day and by night:
    /// each pair is the SAME particles drawn with the old procedural puff (a) and the new flipbook + detail puff (b), switched
    /// live. Logs the biggest puff on screen in pixels and its screen pixels per atlas / detail texel, live puff counts, soft
    /// particles (camera depth texture). L02 also records a 5 s burning-wreck clip (20 fps JPGs, ffmpeg afterwards).
    /// </summary>
    public class SmokeQualityProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static void RunL02() { Report = "running"; new GameObject("SmokeQualityProof").AddComponent<SmokeQualityProof>().level = 2; }
        public static void RunL02Close() { Report = "running"; var p = new GameObject("SmokeQualityProof").AddComponent<SmokeQualityProof>(); p.level = 2; p.quick = true; }   // smoke fix: one day close frame
        public static void RunL17() { Report = "running"; new GameObject("SmokeQualityProof").AddComponent<SmokeQualityProof>().level = 17; }

        int level; bool quick; ZGame g; ZWorld W; RtsCamera rts; Camera cam; readonly StringBuilder sb = new StringBuilder(); int fails; string dir;
        void Log(string s) { sb.AppendLine(s); Debug.Log("[SmokeProof] " + s); }
        void Check(string what, bool ok) { if (!ok) fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, $"L{level:00}_{name}.png"));
            Log($"frame L{level:00}_{name}.png");
            yield return null; yield return null;
        }
        ZObject Spawn(string cat, string type, int tx, int ty, Team team)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, cat == "robot" ? 1 : 2, cat == "robot", out tx, out ty);
            var o = cat == "robot" ? W.CreateRobotGroup(type, tx * 16, ty * 16, team) : W.CreateObject(cat, type, tx * 16, ty * 16, team);
            if (o == null) { Log($"spawn {cat}:{type} failed"); return null; }
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            return o;
        }
        void Attack(ZObject u, ZObject t) { if (u != null && t != null) W.SendWaypoints(u.Owner, u, new[] { new ZWaypoint { mode = WpMode.Attack, refId = t.Id, x = t.CenterX, y = t.CenterY, playerGiven = true } }); }

        float peakPx; string peakCh = "-"; int peakLive; bool sampling;

        void Start()
        {
            g = ZGame.I; W = g.World; rts = FindFirstObjectByType<RtsCamera>(); cam = Camera.main;
            dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records/milestones/task_smoke_quality"));
            System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(Run());
        }

        void Update()
        {
            if (!sampling || !OpenZFxPlayer.I) return;
            float px = OpenZFxPlayer.I.BiggestPuffPx(cam, out var ch, out int live);
            if (px > peakPx) { peakPx = px; peakCh = ch; }
            peakLive = Mathf.Max(peakLive, live);
        }

        void LogDensity(string phase)
        {
            float atlasTexel = peakPx / 512f;                                         // one 512 px frame across the puff
            float ds = 3f;
            var fx = OpenZFxPlayer.I;
            if (peakCh != "-" && fx)
            {
                var r = fx.transform.Find("FxCh_" + peakCh); var pr = r ? r.GetComponent<ParticleSystemRenderer>() : null;
                if (pr && pr.sharedMaterial && pr.sharedMaterial.HasProperty("_DetailScale")) ds = pr.sharedMaterial.GetFloat("_DetailScale");
            }
            float detailTexel = peakPx / (1024f * ds);
            Log($"{phase}: biggest puff on screen {peakPx:F0} px ({peakCh}), live puffs peak {peakLive}; screen px per atlas texel {atlasTexel:F2}, per detail texel {detailTexel:F2} (2nd detail octave {detailTexel / 2.37f:F2})");
            Check($"{phase}: no smoke texel larger than ~2 screen px at the edge detail (detail texel {detailTexel:F2} px)", detailTexel <= 2f);
            peakPx = 0f; peakCh = "-"; peakLive = 0;
        }

        IEnumerator Pair(string name)
        {   // the same particles: old procedural puff, then the flipbook
            var fx = OpenZFxPlayer.I;
            fx.SetSmokeAtlas(false); yield return null;
            yield return Shot(name + "_a_before");
            int n = fx.SetSmokeAtlas(true); yield return null;
            yield return Shot(name + "_b_after");
            Log($"{name}: puff channels switched {n}");
        }

        IEnumerator Run()
        {
            yield return new WaitForSeconds(1f);
            var fx = OpenZFxPlayer.I;
            if (!fx) { Check("OpenZFxPlayer present", false); Finish(); yield break; }
            var urp = UniversalRenderPipeline.asset;
            Log($"task_smoke_quality proof L{level:00}, {Screen.width}x{Screen.height}, test mode {PlayerPrefs.GetInt("oz_testmode", 0)}, NightK {NightGrade.NightK:F2}");
            Log($"puff channels on the flipbook: {OpenZFxPlayer.SmokeAtlasChannels}; camera depth texture (soft particles) {(urp ? urp.supportsCameraDepthTexture.ToString() : "?")}");
            Check("every puff channel uses the smoke flipbook + detail textures (6 channels)", OpenZFxPlayer.SmokeAtlasChannels >= 6);
            Check("soft particles work (camera depth texture on)", urp && urp.supportsCameraDepthTexture);
            var atlas = Resources.Load<Texture2D>("OpenZ/FX/Smoke/OpenZ_SmokeAtlas"); var det = Resources.Load<Texture2D>("OpenZ/FX/Smoke/OpenZ_SmokeDetail");
            if (atlas && det) Log($"atlas {atlas.width}x{atlas.height} {atlas.format} mips {atlas.mipmapCount} aniso {atlas.anisoLevel} {atlas.filterMode}; detail {det.width}x{det.height} {det.format} mips {det.mipmapCount} wrap {det.wrapMode}");
            Check("smoke atlas 4096 + detail 1024 with mipmaps", atlas && det && atlas.width >= 4096 && atlas.mipmapCount > 1 && det.mipmapCount > 1);

            var red = W.Objects.FirstOrDefault(o => o.IsFort && o.Owner == g.player && !o.Destroyed);
            var blueF = W.Objects.FirstOrDefault(o => o.IsFort && o.Owner != g.player && o.Owner != Team.Neutral && !o.Destroyed);
            if (red == null || blueF == null) { Check("both forts", false); Finish(); yield break; }
            var rp = new Vector2(red.CenterX / 16f, red.CenterY / 16f); var bp = new Vector2(blueF.CenterX / 16f, blueF.CenterY / 16f); var dirT = (bp - rp).normalized;
            var side = new Vector2(-dirT.y, dirT.x);
            var dn = FindFirstObjectByType<DayNight>();
            float tod0 = dn ? dn.timeOfDay : 0f; bool run0 = dn && dn.running;
            if (dn) { dn.running = false; dn.timeOfDay = 0.12f; dn.Apply(0.12f); }

            for (int pass = 0; pass < (quick ? 1 : 2); pass++)
            {
                string tag = pass == 0 ? "day" : "night";
                if (pass == 1 && dn) { dn.timeOfDay = 0.66f; dn.Apply(0.66f); yield return new WaitForSeconds(0.5f); Log($"night: NightK {NightGrade.NightK:F2}"); }
                var P = rp + dirT * (16f + pass * 6f) + side * (pass == 0 ? 3f : -3f);
                int px = Mathf.RoundToInt(P.x), py = Mathf.RoundToInt(P.y);
                var tank = Spawn("vehicle", "light", px, py, Team.Blue);
                var jeep = Spawn("vehicle", "jeep", px + Mathf.RoundToInt(side.x * 4), py + Mathf.RoundToInt(side.y * 4), Team.Blue);
                var mlrs = Spawn("vehicle", "missile_launcher", px - Mathf.RoundToInt(dirT.x * 9), py - Mathf.RoundToInt(dirT.y * 9), g.player);
                var grunts = Spawn("robot", "grunt", px - Mathf.RoundToInt(side.x * 4), py - Mathf.RoundToInt(side.y * 4), Team.Blue);
                var focus = tank != null ? g.CenterWorld(tank) : g.CenterWorld(red);
                var mid = focus + new Vector3(side.x * 2f + dirT.x * 1.5f, 0f, side.y * 2f + dirT.y * 1.5f);   // between the blast and the smoke
                if (rts) { rts.FlyTo(mid); rts.ZoomTo(44f); }
                yield return new WaitForSeconds(0.8f);
                if (tank != null) { tank.SetHealth(Mathf.Max(1, tank.MaxHealth / 5)); W.UpdateObjectHealth(tank); }      // burning damage stage
                if (jeep != null) { jeep.SetHealth(0); W.UpdateObjectHealth(jeep); }                                     // burning wreck
                Attack(mlrs, grunts);
                // building-size and fort-ruin smoke (the same leg_smoke path as OpenZDamageStates / BuildingBreaker.Ruin) + a blast column
                var bAt = focus + new Vector3(side.x, 0f, side.y) * 4f; bAt.y = g.GroundY(bAt.x, bAt.z) + 1.5f;
                var blastAt = focus + new Vector3(dirT.x, 0f, dirT.y) * 3f; blastAt.y = g.GroundY(blastAt.x, blastAt.z);
                fx.BlastLegacy(blastAt, 1.6f);
                if (FxManager.I) FxManager.I.Dust(blastAt + new Vector3(3f, 0.3f, 0f), 0.8f);
                sampling = true;
                float t0 = Time.time;
                while (Time.time - t0 < 3.5f) { fx.WreckSmoke(bAt + Random.insideUnitSphere * 0.8f, 2.4f, Random.value < 0.4f); yield return new WaitForSeconds(0.3f); }
                fx.BlastLegacy(blastAt + new Vector3(-1f, 0f, 0.5f), 1.6f);                                           // the pair at the column's biggest
                t0 = Time.time;
                while (Time.time - t0 < 1.1f) { fx.WreckSmoke(bAt + Random.insideUnitSphere * 0.8f, 2.4f, Random.value < 0.4f); yield return new WaitForSeconds(0.3f); }
                if (!quick) yield return Pair($"01_{tag}_rts");
                LogDensity($"{tag} RTS zoom 44");
                if (rts) { rts.FlyTo(mid); rts.ZoomTo(22f); }
                yield return new WaitForSeconds(1.2f);                                                                // camera settles first
                fx.BlastLegacy(blastAt + new Vector3(1f, 0f, 1f), 1.8f);
                t0 = Time.time;
                while (Time.time - t0 < 1.1f) { fx.WreckSmoke(bAt + Random.insideUnitSphere * 0.8f, 2.4f, Random.value < 0.4f); yield return new WaitForSeconds(0.3f); }
                if (quick) yield return Shot($"02_{tag}_close_c_fix"); else yield return Pair($"02_{tag}_close");
                LogDensity($"{tag} close zoom 22");
                sampling = false;
                if (pass == 0 && level == 2 && !quick)
                {   // 5 s clip of the burning wreck (jeep) at close zoom, 20 fps JPGs, flipbook on
                    var wreckAt = jeep != null ? g.CenterWorld(jeep) : focus;
                    if (rts) { rts.FlyTo(wreckAt); rts.ZoomTo(20f); }
                    yield return new WaitForSeconds(1f);
                    string cdir = System.IO.Path.Combine(dir, "clip_wreck"); System.IO.Directory.CreateDirectory(cdir);
                    int frames = 0; float next = Time.time; t0 = Time.time;
                    while (Time.time - t0 < 5f)
                    {
                        yield return new WaitForEndOfFrame();
                        if (Time.time >= next)
                        {
                            next += 0.05f;
                            ProofCapture.SaveJpg(System.IO.Path.Combine(cdir, $"f{frames:000}.jpg"), 0, 88);
                            frames++;
                        }
                    }
                    Log($"clip: {frames} frames over 5 s of the burning wreck (jeep#{jeep?.Id}) -> clip_wreck/");
                    Check("5 s burning-wreck clip recorded", frames >= 60);
                }
                foreach (var o in new[] { tank, jeep, mlrs, grunts }) if (o != null && !o.Destroyed) { o.SetHealth(0); W.UpdateObjectHealth(o); }
                yield return new WaitForSeconds(0.5f);
            }
            if (dn) { dn.timeOfDay = tod0; dn.Apply(tod0); dn.running = run0; }
            fx.SetSmokeAtlas(true);
            Finish();
        }

        void Finish()
        {
            Log($"RESULT {(fails == 0 ? "PASS" : "FAIL (" + fails + ")")}");
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, $"proof_log_L{level:00}{(quick ? "_fix" : "")}.txt"), sb.ToString()); } catch (System.Exception ex) { Debug.LogWarning(ex.Message); }
            Report = fails == 0 ? "PASS" : "FAIL " + fails;
            Destroy(gameObject, 0.5f);
        }
    }
}
