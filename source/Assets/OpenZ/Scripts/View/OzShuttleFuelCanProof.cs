using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The Play proof of the rocket fuel cans that fly out of the dying shuttle (OzFuelCan.cs, OzShuttleBreakup.FuelCans.cs; menu
    /// OpenZ/V1.1/Shuttle: K397 Fuel Cans Proof, Editor/OzShuttleFuelCanMenu.cs), level 16 in Play. It runs the shuttle death proof's
    /// own kills (OzShuttleDeathAltitudeProof, fresh level starts; default H2 = hovering, a rocket: the air burst, and G1 = landed,
    /// destroyed on the ground) and watches every death's cans without touching that proof's camera: pictures from a second camera
    /// 35 m over the ground at the game camera's own angle (the flight 0.3 / 0.6 / 1.0 s after the break, the rest), each with a crop
    /// round the cans. Counted lines per death: 5-10 thrown, every can landed, every can lies on the ground (or on something under it)
    /// or went into water, every dry landing heard (its row in the sound log), never more than two can sounds in 0.1 s, the can's
    /// drawn size. Output: milestones/shuttle_fuel_cans (proof_log_K397.txt + the pictures). Test only.
    /// </summary>
    public sealed class OzShuttleFuelCanProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder() => OzRelease.Milestones("shuttle_fuel_cans");
        public const float PictureHeightM = 35f;
        /// <summary>K424: the game camera's own height (m, its start height) and the shortest a full can may be drawn there on a 1080 px screen (px).</summary>
        public const float GameHeightM = 70f, MinCanPx = 9f;

        public static void Run(string kills)
        {
            Report = "running " + kills;
            var go = new GameObject("OzShuttleFuelCanProof"); var p = go.AddComponent<OzShuttleFuelCanProof>(); p.kills = kills;
        }

        string kills = "H2,G1", dir;
        readonly StringBuilder sb = new StringBuilder();
        int fails, passes, watching, deathsSeen, pictures;

        void Start()
        {
            DontDestroyOnLoad(gameObject);                                                              // the death proof starts the level afresh for every kill
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleFuelCanProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        static IEnumerator Wait(float s) { float t = 0f; while (t < s) { t += Time.deltaTime; yield return null; } }

        IEnumerator All()
        {
            Log($"K397 rocket fuel cans proof, kills '{kills}', {Screen.width}x{Screen.height}; cans {OzFuelCan.MinCount}-{OzFuelCan.MaxCount}, height {OzFuelCan.CanHeightM:F2} m, crushed share {OzFuelCan.CrushedShare:F2}, sound spacing {OzFuelCan.SoundSpacing:F2} s; land event {(AudioDirector.Has(OzFuelCan.LandEvent) ? "in the table" : "MISSING (the fallback plays)")}, roll event {(AudioDirector.Has(OzFuelCan.RollEvent) ? "in the table" : "MISSING (the fallback plays)")}");
            int seen = OzFuelCan.Deaths;
            OzShuttleDeathAltitudeProof.Run(kills);
            yield return null;
            float t = 0f;
            while (t < 900f && OzShuttleDeathAltitudeProof.Report.StartsWith("running"))
            {
                if (OzFuelCan.Deaths > seen) { seen = OzFuelCan.Deaths; deathsSeen++; StartCoroutine(Watch(OzFuelCan.LastBatch, deathsSeen)); }
                t += Time.unscaledDeltaTime; yield return null;
            }
            float w = 0f; while (watching > 0 && w < 20f) { w += Time.unscaledDeltaTime; yield return null; }
            Log("the death proof: " + OzShuttleDeathAltitudeProof.Report);
            Check($"one rocket fuel can batch per shuttle death ({deathsSeen} deaths with cans)", (kills == "all" ? deathsSeen >= 7 : deathsSeen >= 1 && deathsSeen == kills.Split(',').Length));
            Log(OzFuelCan.Status());
            Log($"RESULT {(fails == 0 ? "PASS" : "FAIL " + fails)} ({passes} PASS), {pictures} pictures in {dir}");
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "proof_log_K397_" + kills.Replace(',', '_') + ".txt"), sb.ToString()); }
            catch (System.Exception ex) { Debug.LogError("[OzShuttleFuelCanProof] could not write the log: " + ex.Message); }
            Report = fails == 0 ? "PASS" : "FAIL " + fails;
            Destroy(gameObject, 0.5f);
        }

        IEnumerator Watch(OzFuelCan.Batch b, int k)
        {
            if (b == null) yield break;
            watching++;
            string tag = $"D{k}_{(b.air ? "air" : "ground")}_{b.how}";
            Log($"{tag}: shuttle #{b.ship} broke ({b.how}, {(b.air ? "in the air" : "on the ground")}): {b.count} cans ({b.crushed} crushed), seed {b.seed}");
            Check($"{tag} the cans thrown out: {b.count} (5-10)", b.count >= 5 && b.count <= 10);
            float t0 = Time.time;
            foreach (float at in new[] { 0.3f, 0.6f, 1.0f })
            {
                while (Time.time - t0 < at) yield return null;
                Picture(b, $"{tag}_flight_{at * 10f:00}_h35");
                Picture(b, $"{tag}_flight_{at * 10f:00}_h70", GameHeightM);                             // K424: also from the game camera's own height
            }
            float w = 0f; while (w < 12f && !b.AllDone && b.cans.Count > 0 && b.cans[0]) { w += Time.deltaTime; yield return null; }
            yield return Wait(0.3f);                                                                    // the last glides onto the ground
            int alive = 0; foreach (var c in b.cans) if (c && c.Of == b) alive++;
            if (alive < b.count) { Log($"{tag}: the level was started afresh before all cans lay ({alive}/{b.count} left): measured from the record only"); }
            Picture(b, $"{tag}_rest_h35");
            Picture(b, $"{tag}_rest_h70", GameHeightM);
            Log($"{tag} " + OzFuelCan.Line(b));
            Check($"{tag} every can landed ({b.Landed}/{b.count}: ground {b.landedGround}, on something {b.landedOther}, water {b.inWater})", b.Landed == b.count);
            // the rest: each lies on the ground (its lowest point within 6 cm over it, at most 3 cm in) or on something under it, or went into water
            int bad = 0, lying = 0; float lo = 9f, hi = -9f; string worst = "-"; float hMin = 9f, hMax = 0f;
            var g = ZGame.I;
            foreach (var c in b.cans)
            {
                if (!c || c.Of != b) continue;
                var col = c.Body;
                if (c.state == OzFuelCan.State.Rest && col && col.enabled)
                {
                    var bd = col.bounds; float gy = g ? g.GroundY(bd.center.x, bd.center.z) : bd.min.y; float gap = bd.min.y - gy;
                    bool on = c.RestOn.StartsWith("on ");
                    if (!on) { lo = Mathf.Min(lo, gap); hi = Mathf.Max(hi, gap); }
                    if (!on && (gap > 0.06f || gap < -0.03f)) { bad++; worst = $"{c.name} {gap * 100f:F1} cm ({c.RestOn})"; }
                    else lying++;
                    if (!c.Crushed) { float tall = Mathf.Max(bd.size.x, Mathf.Max(bd.size.y, bd.size.z)); hMin = Mathf.Min(hMin, tall); hMax = Mathf.Max(hMax, tall); }   // the full cans (a crushed one is shorter)
                }
                else if (c.state == OzFuelCan.State.Water || c.RestOn == "water") lying++;
                else if (c.state == OzFuelCan.State.Off && c.RestOn == "water") lying++;
                else { bad++; worst = $"{c.name} {c.state}"; }
            }
            if (alive == b.count)
            {
                Check($"{tag} every can lies on the ground or on something under it, or went into water ({lying}/{b.count}; the lowest point {(lo < 9f ? (lo * 100f).ToString("F1") : "-")}..{(hi > -9f ? (hi * 100f).ToString("F1") : "-")} cm over the ground; glided down {b.floatFixed}, up {b.sunkFixed}; wrong {bad} {worst})", bad == 0 && lying == b.count);
                Check($"{tag} the full cans are drawn at their shown size (their longest side {hMin:F2}-{hMax:F2} m; the can {OzFuelCan.DrawnHeightM:F2} m = the real {OzFuelCan.CanHeightM:F2} m x{OzFuelCan.ShowScale:F1})", hMax <= 0 || (hMin >= OzFuelCan.DrawnHeightM * 0.6f && hMax <= OzFuelCan.DrawnHeightM * 1.25f));
                K424Checks(b, tag, hMax);                                                                // K424: seen from the game camera; in the air: they tumble out with the wreck
            }
            // the sounds: every dry landing heard, its row in the sound log; spread out
            int rows = 0, rollRows = 0;
            foreach (var e in OzSoundLog.Since(b.bornU))
            {
                if (e.outcome == null || !e.outcome.StartsWith("PLAY")) continue;
                if (e.key == OzFuelCan.LandEvent || e.key == OzFuelCan.FallbackEvent) rows++;
                else if (e.key == OzFuelCan.RollEvent) rollRows++;
            }
            Log($"{tag} sound log rows since the break: {OzFuelCan.LandEvent} PLAY {rows}, {OzFuelCan.RollEvent} PLAY {rollRows}");
            Check($"{tag} every dry landing heard ({b.firstHeard}/{b.dryFirst}; the land rows in the sound log {rows}, bounces {b.bounceHeard}, rolls {b.rollHeard})", b.firstHeard == b.dryFirst && rows >= b.firstHeard);
            Check($"{tag} the can sounds come one after another, not all at once (most in 0.1 s: {b.maxIn100ms}, 2 allowed)", b.maxIn100ms <= 2);
            watching--;
        }

        /// <summary>A picture from a second camera PictureHeightM over the ground at the game camera's own angle, aimed at the cans'
        /// middle (the game camera is not moved), and a crop round the cans.</summary>
        void Picture(OzFuelCan.Batch b, string name) => Picture(b, name, PictureHeightM);

        /// <summary>K424: a full can's drawn length on a 1080 px screen from the game camera's own height, angle and field of view; in the
        /// air the cans' throw out of the ship (their own small tumble) and how far they lie from the break (the fall from their height,
        /// their throw and the ship's own way (its climb too), + 9 m for the bounces and rolls).</summary>
        void K424Checks(OzFuelCan.Batch b, string tag, float hMax)
        {
            var main = Camera.main;
            float fov = main ? main.fieldOfView : 32f, down = main ? Mathf.Max(0.25f, -main.transform.forward.y) : Mathf.Sin(55f * Mathf.Deg2Rad);
            float dist = GameHeightM / down, len = hMax > 0f ? hMax : OzFuelCan.DrawnHeightM;
            float px = len * 1080f / (2f * dist * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));
            Check($"{tag} a full can reads from the game camera: {px:F1} px long at {GameHeightM:F0} m over the ground ({dist:F1} m away, field of view {fov:F0} deg, 1080 px tall; wanted >= {MinCanPx:F0} px)", px >= MinCanPx);
            if (!b.air) return;
            float vh = new Vector2(b.shipVel.x, b.shipVel.z).magnitude, upMax = Mathf.Max(0f, b.relUpMax + b.shipVel.y);
            float tFall = (upMax + Mathf.Sqrt(upMax * upMax + 2f * 9.81f * Mathf.Max(0.5f, b.dropH))) / 9.81f;
            float outMax = OzShuttleBreakup.CanAirOut1 + 1f, upLo = OzShuttleBreakup.CanAirUp0 * OzShuttleBreakup.CanAirCrackK - 0.05f, upHi = OzShuttleBreakup.CanAirUp1 + 1f;
            float field = 9f + (OzShuttleBreakup.CanAirOut1 + 1f + vh) * tFall * 1.25f;
            Check($"{tag} the cans tumble out of the hull (their own throw {(b.ownAirThrow ? "on" : "OFF")}: out of the ship at most {b.relOutMax:F1} m/s, wanted <= {outMax:F1}; up {b.relUpMin:F2}..{b.relUpMax:F2} m/s, wanted {upLo:F2}..{upHi:F2})", b.ownAirThrow && b.relOutMax <= outMax && b.relUpMin >= upLo && b.relUpMax <= upHi);
            Check($"{tag} the cans land with the wreck, where the camera looks: the farthest {b.farthest:F1} m from the break (wanted <= {field:F1} m: {b.dropH:F1} m up, falling {tFall:F2} s, the ship's own way {vh:F1} m/s, climbing {b.shipVel.y:F1} m/s)", b.farthest <= field);
        }

        void Picture(OzFuelCan.Batch b, string name, float height)
        {
            var main = Camera.main; if (!main || b == null) return;
            var mid = OzFuelCan.Middle(b); var g = ZGame.I;
            var aim = new Vector3(mid.x, g ? g.GroundY(mid.x, mid.z) : mid.y, mid.z);
            var go = new GameObject("OzFuelCanProofCam"); var cam = go.AddComponent<Camera>();
            RenderTexture rt = null; Texture2D tex = null;
            try
            {
                cam.CopyFrom(main);
                var fwd = main.transform.forward; float down = Mathf.Max(0.25f, -fwd.y);
                cam.transform.SetPositionAndRotation(aim - fwd * (height / down), main.transform.rotation);
                const int W = 1920, H = 1080;
                cam.aspect = W / (float)H;
                rt = RenderTexture.GetTemporary(W, H, 24);
                cam.targetTexture = rt; cam.Render(); cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt;
                tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                RenderTexture.active = prev;
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                // the crop: 640 x 360 round the cans' middle (x3 bigger on screen when opened 1:1 beside the full picture)
                var sp = cam.WorldToScreenPoint(mid);
                int cw = 640, ch = 360, x0 = Mathf.Clamp(Mathf.RoundToInt(sp.x) - cw / 2, 0, W - cw), y0 = Mathf.Clamp(Mathf.RoundToInt(sp.y) - ch / 2, 0, H - ch);
                var crop = new Texture2D(cw, ch, TextureFormat.RGB24, false); crop.SetPixels(tex.GetPixels(x0, y0, cw, ch)); crop.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, name + "_crop.png"), crop.EncodeToPNG());
                Destroy(crop);
                pictures += 2;
                Log($"picture {name}.png (+ _crop) {OzFuelCan.Drawn(b)} cans drawn, the cans' middle {(mid - b.centre).magnitude:F1} m from the break's");
            }
            catch (System.Exception ex) { Log("picture " + name + " failed: " + ex.Message); }
            finally
            {
                cam.targetTexture = null;
                if (rt) RenderTexture.ReleaseTemporary(rt);
                if (tex) Destroy(tex);
                Destroy(go);
            }
        }
    }
}
