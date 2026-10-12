using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenZ
{
    /// <summary>
    /// one level loaded; the clock is jumped to night (0.62) when it is not night. Two stages:
    /// <list type="number">
    /// <item><b>Materials</b> (catches shader regressions): every ground-class material of the level (OpenZ/Sand ground, skirt,
    /// cliffs v3, rock pillars, bridge decks / underlays, quay walls, env palette) goes on a 14 x 14 m test tile far outside the
    /// map (x, z = -600), lit by the level's own current moon + ambient. A probe camera (55 deg like RtsCamera) renders it into a
    /// linear HDR target (post off, atmosphere off) with a reference headlight spot (2 m up, aimed 8 m ahead, 78) and a reference
    /// lamp point light (3.5 m up, 20), each on and off. ratio = mean luminance of the pool window lit / unlit. PASS >= 2.0. A shader
    /// that stops looping Forward+ additional lights (custom lighting, missing _CLUSTER_LIGHT_LOOP, unlit, baked only) reads ~1.0.</item>
    /// <item><b>Live</b> (catches budget / heat gating / weak intensities: what the player sees): time frozen (timeScale 0, DayNight
    /// stopped); haze cones, halos and HUD overlay canvases off in both frames. Frame ON = every night light as the game drives it;
    /// frame OFF = the probed lights forced off + every pool impostor off. For each crewed vehicle's headlight (4 m ahead), lamp
    /// (under it, and a 2.5 m ring), flood / searchlight (aim point) and two FX lights spawned in view (explosion light as
    /// FxManager.LightPulse, a FxManager.Burn fire) the display-linear luminance ratio ON / OFF at the ground window. PASS:
    /// headlight and lamp >= 1.8, flood / searchlight >= 1.6, FX >= 1.8 (FX are measured at their flash peak: time is frozen).</item>
    /// </list>
    /// persistentDataPath (build). Start: OzGroundLightGate.Run() (menu OpenZ/QC/Ground Light Gate).
    /// </summary>
    [DefaultExecutionOrder(1000)]   // LateUpdate after VehicleLights (80), NightLights (90) and FxManager: forced-off lights stay off
    public class OzGroundLightGate : MonoBehaviour
    {
        public static bool Running { get; private set; }
        public static string LastReport { get; private set; }
        public const float PassMaterial = 2f, PassHead = 1.8f, PassLamp = 1.8f, PassSpot = 1.6f, PassFx = 1.8f;
        const int W = 256, H = 192;

        static readonly string[] GroundNames =
        {
            "M_L02_Ground", "M_L17_Ground", "M_Skirt_Sand", "GroundPaint", "M_CliffV3_", "M_Cliff_", "M_RockZ_", "M_BRGZ_",
            "M_DeckUnderlay", "M_QuayWall", "DV3_BatchedPalette", "CITY_Concrete", "CITY_BoulderRock", "city_block_concrete",
        };

        sealed class Probe
        {
            public string Kind, Name; public Light L; public Vector3 Ground; public float Ring; public bool Real; public float Intensity;
            public float On = -1f, Off = -1f, RingOn = -1f, RingOff = -1f, Pass; public string Note = "";
            public float Ratio => On > 0f && Off > 0f ? On / Off : -1f;
            public float RingRatio => RingOn > 0f && RingOff > 0f ? RingOn / RingOff : -1f;
        }

        readonly List<Probe> probes = new List<Probe>();
        readonly List<Light> forcedOff = new List<Light>();
        bool offFrame;

        public static void Run()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] Ground light gate: Play mode only (a level scene)"); return; }
            if (Running) { Debug.Log("[OpenZ] Ground light gate: already running"); return; }
            var go = new GameObject("OzGroundLightGate");
            var g = go.AddComponent<OzGroundLightGate>();
            g.StartCoroutine(g.Gate());
        }

        void LateUpdate()
        {
            if (!offFrame) return;
            for (int i = 0; i < forcedOff.Count; i++) if (forcedOff[i]) forcedOff[i].enabled = false;
        }

        IEnumerator Gate()
        {
            Running = true;
            var sb = new StringBuilder(); var csv = new StringBuilder("stage,kind,name,real,intensity,on,off,ratio,ring_ratio,pass,note\n");
            var game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            string level = game ? game.level : "?";
            var dn = FindFirstObjectByType<DayNight>();
            bool dnRunning = dn && dn.running; float ts = Time.timeScale;
            if (dn && NightGrade.NightK < 0.95f)
            {   // night, then let lamps warm up (3.2 s), headlights and the budget cross-fade (0.35 s) at normal speed
                dn.timeOfDay = 0.62f; dn.Apply(0.62f);
                float until = Time.realtimeSinceStartup + 4.5f;
                while (Time.realtimeSinceStartup < until) yield return null;
            }
            if (dn) dn.running = false;
            int fails = 0;
            // ------------------------------------------------------------------ stage 1: materials
            fails += StageMaterials(sb, csv);
            // ------------------------------------------------------------------ stage 2: live
            var vl = VehicleLights.I; var nl = NightLights.I;
            bool vb = vl && vl.beams, vp = vl && vl.pools, nb = nl && nl.beams, nh = nl && nl.halos, np = nl && nl.pools;
            if (vl) vl.beams = false;
            if (nl) { nl.beams = false; nl.halos = false; }
            var canvases = new List<Canvas>();
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.enabled && c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay) { c.enabled = false; canvases.Add(c); }
            var cam = Camera.main;
            if (game && cam) CollectLive(game, cam);
            var fxLights = SpawnFx(game, cam);
            Time.timeScale = 0f;
            for (int i = 0; i < 3; i++) yield return null;
            yield return new WaitForEndOfFrame();
            var on = ScreenCapture.CaptureScreenshotAsTexture();
            foreach (var p in probes) { if (p.L) forcedOff.Add(p.L); }
            forcedOff.AddRange(fxLights);
            if (vl) vl.pools = false;
            if (nl) nl.pools = false;
            offFrame = true;
            for (int i = 0; i < 3; i++) yield return null;
            yield return new WaitForEndOfFrame();
            var off = ScreenCapture.CaptureScreenshotAsTexture();
            offFrame = false;
            // restore (probe lights: the owners switch them back next frame; FX lights are re-enabled so they age out of their pools)
            foreach (var l in fxLights) if (l) l.enabled = true;
            if (vl) { vl.beams = vb; vl.pools = vp; }
            if (nl) { nl.beams = nb; nl.halos = nh; nl.pools = np; }
            foreach (var c in canvases) if (c) c.enabled = true;
            Time.timeScale = ts; if (dn) dn.running = dnRunning;
            fails += Score(cam, on, off, sb, csv);
            Destroy(on); Destroy(off);
            // context: why a light was (not) real
            var bud = nl ? nl.Budget : null;
            string ctx = $"night {NightGrade.NightK:F2}, heat level {(bud != null ? bud.HeatLevel : -1)} (GPU {(bud != null ? bud.Temperature : -1)} C), " +
                         $"budget real {(bud != null ? bud.Granted : -1)}/{(bud != null ? bud.EffectiveMax : -1)}, real headlights lit {(vl ? vl.RealLightsLit : -1)}";
            LastReport = $"[OpenZ] Ground light gate {level}: {(fails == 0 ? "PASS" : fails + " FAIL")}  ({ctx})\n" + sb;
            if (fails == 0) Debug.Log(LastReport); else Debug.LogWarning(LastReport);
            WriteCsv(level, csv);
            Running = false;
            Destroy(gameObject);
        }

        // ------------------------------------------------------------------ stage 1
        int StageMaterials(StringBuilder sb, StringBuilder csv)
        {
            var mats = new List<Material>();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                foreach (var m in r.sharedMaterials)
                    if (m && !mats.Contains(m) && IsGround(m)) mats.Add(m);
            var origin = new Vector3(-600f, 0f, -600f);
            var camGo = new GameObject("OZ_GLG_Cam"); var cam = camGo.AddComponent<Camera>();
            cam.enabled = false; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            cam.fieldOfView = 40f; cam.nearClipPlane = 0.3f; cam.farClipPlane = 80f; cam.allowMSAA = false; cam.allowHDR = true;
            var cd = camGo.AddComponent<UniversalAdditionalCameraData>(); cd.renderPostProcessing = false; cd.antialiasing = AntialiasingMode.None;
            cam.transform.SetPositionAndRotation(origin + new Vector3(0f, 16f, -11.2f), Quaternion.Euler(55f, 0f, 0f));
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            var read = new Texture2D(W, H, TextureFormat.RGBAHalf, false, true);
            var tile = GameObject.CreatePrimitive(PrimitiveType.Plane); Destroy(tile.GetComponent<Collider>());
            tile.name = "OZ_GLG_Tile"; tile.transform.position = origin; tile.transform.localScale = new Vector3(1.4f, 1f, 1.4f);
            var tr = tile.GetComponent<MeshRenderer>(); tr.shadowCastingMode = ShadowCastingMode.Off;
            var spot = TestLight("OZ_GLG_Spot", LightType.Spot, origin + new Vector3(0f, 2f, -4f), Quaternion.LookRotation(new Vector3(0f, -2f, 8f)), 78f, 24f, new Color(1f, 0.84f, 0.6f));
            spot.spotAngle = 80f; spot.innerSpotAngle = 24f;
            var point = TestLight("OZ_GLG_Point", LightType.Point, origin + new Vector3(3f, 3.5f, 3f), Quaternion.identity, 20f, 12f, new Color(1f, 0.8f, 0.52f));
            int IdAtmo = Shader.PropertyToID("_AtmoEnabled"); float atmo = Shader.GetGlobalFloat(IdAtmo);
            Shader.SetGlobalFloat(IdAtmo, 0f);
            int fails = 0;
            try
            {
                foreach (var m in mats)
                {
                    if (m.renderQueue >= 2500) { Line(sb, csv, "material", "-", m.name, false, 0f, -1, -1, -1, -1, true, $"transparent ({m.shader.name}): not tested here"); continue; }
                    tr.sharedMaterial = m;
                    spot.enabled = false; point.enabled = false; var offPx = Render(cam, rt, read);
                    spot.enabled = true; var sPx = Render(cam, rt, read); spot.enabled = false;
                    point.enabled = true; var pPx = Render(cam, rt, read); point.enabled = false;
                    float sOn = Window(cam, sPx, origin, 0.8f, W, H), sOff = Window(cam, offPx, origin, 0.8f, W, H);
                    var pg = origin + new Vector3(3f, 0f, 3f);
                    float pOn = Window(cam, pPx, pg, 0.8f, W, H), pOff = Window(cam, offPx, pg, 0.8f, W, H);
                    float rs = sOff > 0f ? sOn / sOff : -1f, rp = pOff > 0f ? pOn / pOff : -1f;
                    bool pass = rs >= PassMaterial && rp >= PassMaterial;
                    if (!pass) fails++;
                    Line(sb, csv, "material", "spot+point", $"{m.name} ({m.shader.name})", true, 78f, sOn, sOff, rs, rp, pass,
                         pass ? "" : "additional lights barely reach this shader");
                }
            }
            finally
            {
                Shader.SetGlobalFloat(IdAtmo, atmo);
                Destroy(tile); Destroy(spot.gameObject); Destroy(point.gameObject); Destroy(camGo); rt.Release(); Destroy(rt); Destroy(read);
            }
            return fails;
        }

        static bool IsGround(Material m)
        {
            if (m.shader && m.shader.name == "OpenZ/Sand") return true;
            foreach (var n in GroundNames) if (m.name.StartsWith(n)) return true;
            return false;
        }

        static Light TestLight(string name, LightType type, Vector3 pos, Quaternion rot, float intensity, float range, Color col)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.transform.SetPositionAndRotation(pos, rot);
            var l = go.AddComponent<Light>();
            l.type = type; l.intensity = intensity; l.range = range; l.color = col; l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel; l.enabled = false;
            return l;
        }

        static Color[] Render(Camera cam, RenderTexture rt, Texture2D read)
        {
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            var prev = RenderTexture.active; RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false); read.Apply(false);
            RenderTexture.active = prev;
            return read.GetPixels();
        }

        // ------------------------------------------------------------------ stage 2
        void CollectLive(ZGame game, Camera cam)
        {
            foreach (var kv in game.Views)
            {
                var v = kv.Value;
                if (!v || v.Obj == null || v.Obj.Kind != ZKind.Vehicle || v.Obj.Destroyed || v.Obj.Owner == Team.Neutral) continue;
                foreach (var l in v.GetComponentsInChildren<Light>(true))
                {
                    if (l.name != "VL_Headlight") continue;
                    var fwd = Vector3.ProjectOnPlane(l.transform.forward, Vector3.up); if (fwd.sqrMagnitude < 1e-4f) continue;
                    var p = l.transform.position + fwd.normalized * 4f; p.y = game.GroundY(p.x, p.z);
                    Add("headlight", $"{v.Obj.Type} #{v.Obj.Id}", l, p, 0f, PassHead, cam);
                }
            }
            if (NightLights.I)
                foreach (var f in NightLights.I.Fixtures)
                {
                    if (f == null || !f.Light || f.Out < 0.5f) continue;
                    var l = f.Light; Vector3 p;
                    if (f.IsLamp) { p = l.transform.position; p.y = game.GroundY(p.x, p.z); Add("lamp", f.Id, l, p, 2.5f, PassLamp, cam); continue; }
                    if (f.Kind != NightKind.Flood && f.Kind != NightKind.Searchlight && f.Kind != NightKind.WorkLight) continue;
                    if (!AimPoint(game, l.transform.position, l.transform.forward, Mathf.Max(4f, f.Range), out p)) continue;
                    Add(f.Kind == NightKind.Searchlight ? "searchlight" : "flood", f.Id, l, p, 0f, PassSpot, cam);
                }
        }

        void Add(string kind, string name, Light l, Vector3 ground, float ring, float pass, Camera cam)
        {
            var vp = cam.WorldToViewportPoint(ground);
            if (vp.z <= 0f || vp.x < 0.04f || vp.x > 0.96f || vp.y < 0.22f || vp.y > 0.88f) return;   // off screen / under the HUD bands
            probes.Add(new Probe { Kind = kind, Name = name, L = l, Ground = ground, Ring = ring, Real = l.enabled && l.intensity > 0.01f, Intensity = l.enabled ? l.intensity : 0f, Pass = pass });
        }

        static bool AimPoint(ZGame game, Vector3 o, Vector3 d, float range, out Vector3 p)
        {
            p = o;
            if (d.y > -0.03f) return false;
            for (float s = 0.5f; s <= range; s += 0.25f)
            {
                var q = o + d * s;
                if (q.y <= game.GroundY(q.x, q.z)) { p = q; p.y = game.GroundY(q.x, q.z); return true; }
            }
            return false;
        }

        List<Light> SpawnFx(ZGame game, Camera cam)
        {
            var list = new List<Light>();
            if (!game || !cam) return list;
            var rts = cam.GetComponent<RtsCamera>(); var f = rts ? rts.focus : cam.transform.position;
            if (FxManager.I)
            {
                var before = new HashSet<Light>();
                foreach (var l in FxManager.I.GetComponentsInChildren<Light>(true)) if (l.enabled) before.Add(l);
                var a = new Vector3(f.x - 7f, 0f, f.z + 3f); a.y = game.GroundY(a.x, a.z);
                FxManager.I.AddLight(a + Vector3.up * 1.2f, new Color(1f, 0.58f, 0.26f), 16f, 22f, 1f, 0.35f, true);   // = FxManager.LightPulse(at, 1)
                var b = new Vector3(f.x + 7f, 0f, f.z + 3f); b.y = game.GroundY(b.x, b.z);
                FxManager.I.Burn(b, 1f);                                                                                   // the real burning-rubble call
                foreach (var l in FxManager.I.GetComponentsInChildren<Light>(true))
                {
                    if (!l.enabled || before.Contains(l)) continue;
                    list.Add(l);
                    bool isA = (l.transform.position - a).sqrMagnitude < (l.transform.position - b).sqrMagnitude;
                    Add(isA ? "fx_explosion" : "fx_fire", isA ? "FxManager.LightPulse" : "FxManager.Burn", l, isA ? a : b, 2.5f, PassFx, cam);
                }
            }
            return list;
        }

        int Score(Camera cam, Texture2D on, Texture2D off, StringBuilder sb, StringBuilder csv)
        {
            int fails = 0;
            if (!cam) { sb.AppendLine("  live: no main camera"); return 1; }
            var a = on.GetPixels(); var b = off.GetPixels(); int w = on.width, h = on.height;
            for (int i = 0; i < a.Length; i++) { a[i] = a[i].linear; b[i] = b[i].linear; }
            if (probes.Count == 0) sb.AppendLine("  live: nothing burning in view (look at vehicles / lamps / a fort at night)");
            foreach (var p in probes)
            {
                p.On = Window(cam, a, p.Ground, 0.8f, w, h); p.Off = Window(cam, b, p.Ground, 0.8f, w, h);
                if (p.Ring > 0f)
                {
                    float so = 0f, sf = 0f;
                    for (int k = 0; k < 4; k++)
                    {
                        var q = p.Ground + Quaternion.Euler(0f, 45f + 90f * k, 0f) * Vector3.forward * p.Ring;
                        so += Window(cam, a, q, 0.6f, w, h); sf += Window(cam, b, q, 0.6f, w, h);
                    }
                    p.RingOn = so; p.RingOff = sf;
                }
                bool pass = p.Ratio >= p.Pass;
                if (!pass) fails++;
                string note = p.Real ? "" : "no real light this frame (budget / heat gate / not a candidate): impostor only";
                Line(sb, csv, "live", p.Kind, p.Name, p.Real, p.Intensity, p.On, p.Off, p.Ratio, p.RingRatio, pass, note);
            }
            return fails;
        }

        /// <summary>Mean luminance of the pixels within radius (m) of a ground point (projected with the camera; linear colours).</summary>
        static float Window(Camera cam, Color[] px, Vector3 world, float radius, int w, int h)
        {
            var c = cam.WorldToViewportPoint(world); if (c.z <= 0f) return -1f;
            var e = cam.WorldToViewportPoint(world + cam.transform.right * radius);
            float cx = c.x * w, cy = c.y * h, r = Mathf.Max(2f, Mathf.Abs(e.x - c.x) * w);
            double sum = 0; int n = 0;
            for (int y = Mathf.Max(0, (int)(cy - r)); y <= Mathf.Min(h - 1, (int)(cy + r)); y++)
                for (int x = Mathf.Max(0, (int)(cx - r)); x <= Mathf.Min(w - 1, (int)(cx + r)); x++)
                {
                    float dx = x - cx, dy = y - cy; if (dx * dx + dy * dy > r * r) continue;
                    var p = px[y * w + x]; sum += 0.2126f * p.r + 0.7152f * p.g + 0.0722f * p.b; n++;
                }
            return n > 0 ? (float)(sum / n) : -1f;
        }

        static void Line(StringBuilder sb, StringBuilder csv, string stage, string kind, string name, bool real, float intensity,
                         float on, float off, float ratio, float ring, bool pass, string note)
        {
            sb.AppendLine($"  {(pass ? "PASS" : "FAIL")} {stage,-8} {kind,-12} {name,-44} real {(real ? "yes" : "no "),-3} I {intensity,6:F1}  " +
                          $"on {on,7:F4} off {off,7:F4} x{ratio,5:F2}{(ring > 0f ? $" ring x{ring:F2}" : "")}{(note.Length > 0 ? "  " + note : "")}");
            csv.AppendLine($"{stage},{kind},\"{name}\",{(real ? 1 : 0)},{intensity:F2},{on:F5},{off:F5},{ratio:F3},{ring:F3},{(pass ? "PASS" : "FAIL")},\"{note}\"");
        }

        static void WriteCsv(string level, StringBuilder csv)
        {
            try
            {
                string dir = Application.isEditor
                    ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "..", "work", "records", "qc"))
                    : Application.persistentDataPath;
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"ground_light_gate_{level}_{System.DateTime.Now:MMdd_HHmmss}.csv");
                File.WriteAllText(path, csv.ToString());
                Debug.Log("[OpenZ] Ground light gate CSV: " + path);
            }
            catch (System.Exception e) { Debug.LogWarning("[OpenZ] Ground light gate: CSV not written: " + e.Message); }
        }
    }
}
