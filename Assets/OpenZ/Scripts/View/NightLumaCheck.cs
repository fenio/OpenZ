using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenZ
{
    /// <summary>
    /// Night v2 luminance check (Play mode, run it at night): does every object class darken with the night like the ground?
    /// For each class (ground, water, cliffs v3, old cliffs, rock pillars, rock props, bridges, props, buildings, map items,
    /// robots, vehicles, cannons) it renders ONLY that class from the game camera's current view (plus a close-up of the
    /// nearest instances when the class is not in view) into a small linear HDR target, post-processing off, twice (black and
    /// white clear: the pixels that do not change are the class's own opaque pixels), and averages their luminance. Lights,
    /// the night lamps' glows / pools / washes and the night package's beams stay on; every other renderer is off for that
    /// render (synchronously, restored before any other script runs), so shadows between classes do not count. It does this
    /// at night (the current time when it is night, else 0.62) and at day (0.08), then restores the clock, and reports per
    /// class night / day and that ratio relative to the ground's: an environment class that keeps more of its daylight than
    /// lift the buildings at night. Start: Ctrl+Shift+F9, menu OpenZ/Night/Night v2 Luminance Check, or NightLumaCheck.Run().
    /// Takes ~8 s; the day/night clock jumps and comes back.
    /// </summary>
    public class NightLumaCheck : MonoBehaviour
    {
        public static bool Running { get; private set; }
        public static string LastReport { get; private set; }
        public static int W = 480, H = 300;

        enum Rule { Ground, Env, Building, Item, Unit, Info }

        sealed class Cat
        {
            public string Name; public Rule Rule; public string[] Match; public string ShaderMatch; public bool Transparent;
            public readonly List<Renderer> Rs = new List<Renderer>();
            public double DayL = -1, NightL = -1, NightNoLamps = -1, NightNoUnitRim = -1, NightAsUnit = -1; public int DayPx, NightPx; public string Classes = "";
        }

        static List<Cat> Cats() => new List<Cat>
        {
            new Cat { Name = "ground", Rule = Rule.Ground, ShaderMatch = "OpenZ/Sand", Match = new[] { "M_L17_Ground", "M_L02_Ground", "M_Skirt_Sand", "GroundPaint" } },
            new Cat { Name = "water", Rule = Rule.Info, ShaderMatch = "OpenZ/WaterStylized", Match = new string[0], Transparent = true },
            new Cat { Name = "cliffs_v3", Rule = Rule.Env, Match = new[] { "M_CliffV3_" } },
            new Cat { Name = "cliffs_old", Rule = Rule.Env, Match = new[] { "M_Cliff_Sandstone", "M_Cliff_CityRock" } },
            new Cat { Name = "rock_pillars", Rule = Rule.Env, Match = new[] { "M_RockZ_" } },
            new Cat { Name = "rock_props", Rule = Rule.Env, Match = new[] { "DV3_Sandstone_Textured", "CITY_BoulderRock" } },
            new Cat { Name = "bridges", Rule = Rule.Env, Match = new[] { "M_BRGZ_", "M_DeckUnderlay" } },
            new Cat { Name = "props", Rule = Rule.Env, Match = new[] { "DV3_BatchedPalette", "city_block_concrete", "STOP_sign", "Street_lamp_deep_teal", "Street_lamp_warm_brass", "VC13_metal", "DV3_metal", "DV3_olive", "DV3_wood", "M_QuayWall" } },
            new Cat { Name = "buildings", Rule = Rule.Building, Match = new[] { "m01_fort_mat", "m02_robot_factory_mat", "m03_vehicle_factory_mat", "m04_repair_station_mat", "m05_radar_mat", "FactoryDoor" } },
            new Cat { Name = "map_items", Rule = Rule.Item, Match = new[] { "M_Crate", "M_Hut", "M_Grenade" } },
            new Cat { Name = "robots", Rule = Rule.Unit, Match = new[] { "M_BS01_", "other-bot_" } },
            new Cat { Name = "vehicles", Rule = Rule.Unit, Match = new[] { "tank_DV3", "jeep_DV3", "mech_DV3", "mlrs_DV3", "heavy_tank_", "M_AtomJeep", "M_AtomGun", "m06_apc", "m07_crane", "m08_medium" } },
            new Cat { Name = "cannons", Rule = Rule.Unit, Match = new[] { "cannon_DV3", "m09_gatling", "m10_howitzer", "m11_missile" } },
        };

        /// <summary>Start the check (Play mode). dayReference false = night only (no clock jump; ratios need a day pass).</summary>
        public static void Run(bool dayReference = true)
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] NightLumaCheck: Play mode only"); return; }
            if (Running) { Debug.Log("[OpenZ] NightLumaCheck: already running"); return; }
            var go = new GameObject("NightLumaCheck");
            go.AddComponent<NightLumaCheck>().StartCoroutine(go.GetComponent<NightLumaCheck>().Check(dayReference));
        }

        Camera probe; RenderTexture rt; Texture2D read; List<Cat> cats;
        Renderer[] all; bool[] was; readonly HashSet<Renderer> helpers = new HashSet<Renderer>();

        IEnumerator Check(bool dayReference)
        {
            Running = true;
            var main = Camera.main;
            dn = FindFirstObjectByType<DayNight>();
            if (!main) { Debug.LogWarning("[OpenZ] NightLumaCheck: no main camera"); Finish(); yield break; }
            t0 = dn ? dn.timeOfDay : NightGrade.TimeOfDay; run0 = dn && dn.running; clockTouched = dn;
            Setup(main);
            bool collected = false;
            try { Collect(); collected = true; } catch (System.Exception e) { Debug.LogException(e); }
            if (!collected) { Finish(); yield break; }
            Debug.Log($"[OpenZ] NightLumaCheck: {all.Length} renderers, {helpers.Count} light helpers; classes: " + string.Join(", ", cats.ConvertAll(c => $"{c.Name} {c.Rs.Count}")));

            // ---- night
            float tn = NightGrade.Night(t0) > 0.9f ? t0 : 0.62f;
            if (dn) { dn.running = false; if (Mathf.Abs(dn.timeOfDay - tn) > 1e-4f) { dn.timeOfDay = tn; dn.Apply(tn); } }
            yield return new WaitForSecondsRealtime(tn == t0 ? 0.3f : 3f);          // night lights / lamps switch on and settle
            Collect();                                                                  // renderers as they are now (night)
            foreach (var c in cats) { yield return null; Measure(main, c, true, false); }
            var bld = cats.Find(c => c.Name == "buildings");
            if (bld != null && NightV2.I) { yield return null; Measure(main, bld, true, true); }
            // measure the unit classes once more with the unit rim / fill set to 0 and report the lift
            if (NightV2.I)
            {
                yield return null;
                var v2 = NightV2.I; Vector4 rk = v2.rimK, fk = v2.fillK;
                v2.rimK = new Vector4(rk.x, 0f, rk.z, rk.w); v2.fillK = new Vector4(fk.x, 0f, fk.z, fk.w); v2.PushNow();
                try { foreach (var c in cats) if (c.Rule == Rule.Unit) Measure(main, c, true, false, true); }
                catch (System.Exception e) { Debug.LogException(e); }
                finally { v2.rimK = rk; v2.fillK = fk; v2.PushNow(); }
            }
            // unit rim + fill for all, day-sky reflection kept): shows in-engine what a class would read if it still got the
            // unit night glow, so the verdict can be trusted (reported as "with unit night terms x..").
            if (NightV2.I)
            {
                yield return null;
                var v2 = NightV2.I; Vector4 rk = v2.rimK, fk = v2.fillK; bool ef = v2.envFix;
                v2.rimK = Vector4.one; v2.fillK = Vector4.one; v2.envFix = false; v2.PushNow();
                try { foreach (var c in cats) if (c.Rule == Rule.Env || c.Rule == Rule.Building || c.Rule == Rule.Item) Measure(main, c, true, false, false, true); }
                catch (System.Exception e) { Debug.LogException(e); }
                finally { v2.rimK = rk; v2.fillK = fk; v2.envFix = ef; v2.PushNow(); }
            }
            // ---- day
            if (dayReference && dn)
            {
                dn.timeOfDay = 0.08f; dn.Apply(0.08f);
                yield return new WaitForSecondsRealtime(2.5f);
                Collect();                                                              // renderers as they are now (day)
                foreach (var c in cats) { yield return null; Measure(main, c, false, false); }
            }
            RestoreClock();
            Report(tn);
            Finish();
        }

        void Setup(Camera main)
        {
            float aspect = main.aspect > 0.1f ? main.aspect : 1.6f;
            H = Mathf.RoundToInt(W / aspect);
            rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { name = "NightLumaRT", antiAliasing = 1 };
            rt.Create();
            read = new Texture2D(W, H, TextureFormat.RGBAHalf, false, true);
            var go = new GameObject("NightLumaProbe"); go.transform.SetParent(transform, false);
            probe = go.AddComponent<Camera>(); probe.CopyFrom(main); probe.enabled = false;
            probe.allowHDR = true; probe.allowMSAA = false; probe.clearFlags = CameraClearFlags.SolidColor; probe.targetTexture = null;
            var ad = probe.GetUniversalAdditionalCameraData();
            ad.renderPostProcessing = false; ad.antialiasing = AntialiasingMode.None; ad.requiresDepthTexture = true; ad.renderShadows = true;
            cats = Cats();
        }

        static bool IsHelper(Renderer r)
        {
            var m = r.sharedMaterial; var s = m ? m.shader : null;
            return s && (s.name.StartsWith("OpenZ/Night") || s.name == "OpenZ/NightWash");
        }

        void Collect()
        {
            all = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            // checker: Atmosphere's dust particles are HideFlags.DontSave (FindObjectsByType skips them): add them so they are
            // switched off in the class renders like every other renderer (unlit streaks over a class biased its luminance)
            var dust = new List<Renderer>(); NightV2.CollectUnlitDust(dust);
            if (dust.Count > 0) { var set = new HashSet<Renderer>(all); foreach (var r in dust) if (set.Add(r)) { System.Array.Resize(ref all, all.Length + 1); all[all.Length - 1] = r; } }
            was = new bool[all.Length];
            helpers.Clear(); foreach (var c in cats) c.Rs.Clear();
            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i]; was[i] = r.enabled;
                if (IsHelper(r)) { helpers.Add(r); continue; }
                if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                var mats = r.sharedMaterials;
                bool unitOwned = r.GetComponentInParent<UnitView>() != null;                        // H14: vehicle parts (mlrs_DV3_metal ...) were counted as props
                foreach (var c in cats)
                {
                    if (unitOwned && c.Rule != Rule.Unit) continue;
                    bool hit = false;
                    foreach (var m in mats)
                    {
                        if (!m) continue;
                        if (c.ShaderMatch != null && m.shader && m.shader.name == c.ShaderMatch) { hit = true; break; }
                        foreach (var k in c.Match) if (m.name.Contains(k)) { hit = true; break; }
                        if (hit) break;
                    }
                    if (hit) { c.Rs.Add(r); break; }
                }
            }
            foreach (var c in cats)
            {
                var cls = new SortedDictionary<string, int>();
                foreach (var r in c.Rs) foreach (var m in r.sharedMaterials) { if (!m) continue; int k = NightV2.ClassOf(m); string key = k < 0 ? (m.shader ? m.shader.name : "?") : NightV2.ClassNames[k]; cls[key] = cls.TryGetValue(key, out var n) ? n + 1 : 1; }
                var sb = new StringBuilder(); foreach (var kv in cls) sb.Append(sb.Length > 0 ? ", " : "").Append(kv.Key).Append(' ').Append(kv.Value);
                c.Classes = sb.ToString();
            }
        }

        // ------------------------------------------------------------------ rendering
        void RenderOnly(HashSet<Renderer> keep, Color clear)
        {
            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i]; if (!r) continue;
                bool on = was[i] && (keep.Contains(r) || helpers.Contains(r));
                if (r.enabled != on) r.enabled = on;
            }
            probe.backgroundColor = clear;
            // PC_Renderer (FullScreenPass, BeforeRenderingPostProcessing), so "post off" does not remove them from the probe:
            // they added fog light onto the class pixels (lamp glows around the buildings, low fog over the bridges / quays).
            // Off for the probe render only (the same globals CaptureKit / GameBoot use), restored right after.
            float atmo = Shader.GetGlobalFloat(IdAtmo), shimmer = Shader.GetGlobalFloat(IdShimmer);
            Shader.SetGlobalFloat(IdAtmo, 0f); Shader.SetGlobalFloat(IdShimmer, 0f);
            try
            {
                var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(probe, req)) RenderPipeline.SubmitRenderRequest(probe, req);
                else { probe.targetTexture = rt; probe.Render(); probe.targetTexture = null; }
            }
            finally { Shader.SetGlobalFloat(IdAtmo, atmo); Shader.SetGlobalFloat(IdShimmer, shimmer); }
        }

        static readonly int IdAtmo = Shader.PropertyToID("_AtmoEnabled"), IdShimmer = Shader.PropertyToID("_ShimmerAmount");

        void Restore()
        {
            for (int i = 0; i < all.Length; i++) { var r = all[i]; if (r && r.enabled != was[i]) r.enabled = was[i]; }
        }

        Color[] Grab()
        {
            var prev = RenderTexture.active; RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, W, H), 0, 0, false); read.Apply(false);
            RenderTexture.active = prev;
            return read.GetPixels();
        }

        static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>Mean luminance of the class's own pixels (unchanged between a black and a white clear); for transparent
        /// classes (water) the pixels that change when the class is drawn over the ground.</summary>
        (double mean, int px) Shot(Cat c, Cat ground, bool lampsOff)
        {
            var keep = new HashSet<Renderer>(c.Rs);
            for (int i = 0; i < all.Length; i++) was[i] = all[i] && all[i].enabled;      // snapshot now: other scripts toggle renderers between frames
            var lampR = lampsOff && NightV2.I ? new List<Renderer>(NightV2.I.LampRenderers()) : null;
            var lampWas = new List<bool>();
            if (lampR != null) foreach (var r in lampR) { lampWas.Add(r && r.enabled); if (r) helpers.Remove(r); }
            try
            {
                Color[] a, b;
                if (c.Transparent && ground != null)
                {
                    var both = new HashSet<Renderer>(ground.Rs); both.UnionWith(c.Rs);
                    RenderOnly(both, Color.black); a = Grab();
                    RenderOnly(new HashSet<Renderer>(ground.Rs), Color.black); b = Grab();
                    double s = 0; int n = 0;
                    for (int i = 0; i < a.Length; i++) { var d = a[i] - b[i]; if (Mathf.Abs(d.r) + Mathf.Abs(d.g) + Mathf.Abs(d.b) > 0.002f) { s += Lum(a[i]); n++; } }
                    return (n > 0 ? s / n : -1, n);
                }
                RenderOnly(keep, Color.black); a = Grab();
                RenderOnly(keep, Color.white); b = Grab();
                double sum = 0; int cnt = 0;
                for (int i = 0; i < a.Length; i++)
                {
                    var d = b[i] - a[i];
                    if (Mathf.Abs(d.r) + Mathf.Abs(d.g) + Mathf.Abs(d.b) < 0.003f) { sum += Lum(a[i]); cnt++; }
                }
                return (cnt > 0 ? sum / cnt : -1, cnt);
            }
            finally
            {
                Restore();
                if (lampR != null) foreach (var r in lampR) if (r) helpers.Add(r);
            }
        }

        void Measure(Camera main, Cat c, bool night, bool lampsOff, bool noUnitRim = false, bool asUnit = false)
        {
            if (c.Rs.Count == 0) return;
            var ground = cats.Find(x => x.Name == "ground");
            // 1) the game camera's own view
            probe.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
            probe.fieldOfView = main.fieldOfView; probe.orthographic = main.orthographic; probe.orthographicSize = main.orthographicSize;
            var (m, px) = Shot(c, ground, lampsOff);
            // 2) not (enough) in view: close-ups of up to 2 instances nearest the view centre (small objects only)
            if (px < 400)
            {
                double s = m > 0 ? m * px : 0; int n = px > 0 ? px : 0;
                var focus = main.transform.position + main.transform.forward * 45f;
                var picks = new List<Renderer>();
                var sorted = new List<Renderer>(c.Rs); sorted.RemoveAll(r => !r || !r.enabled || !r.gameObject.activeInHierarchy || r.bounds.size.magnitude > 40f);
                sorted.Sort((x, y) => (x.bounds.center - focus).sqrMagnitude.CompareTo((y.bounds.center - focus).sqrMagnitude));
                foreach (var r in sorted)
                {
                    bool near = false; foreach (var p in picks) if ((p.bounds.center - r.bounds.center).sqrMagnitude < 400f) near = true;
                    if (!near) picks.Add(r);
                    if (picks.Count >= 2) break;
                }
                foreach (var r in picks)
                {
                    var bc = r.bounds.center; float ext = Mathf.Max(1f, r.bounds.extents.magnitude);
                    float dist = 38f;
                    probe.transform.SetPositionAndRotation(bc - main.transform.forward * dist, main.transform.rotation);
                    probe.orthographic = false;
                    probe.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(ext * 1.3f / dist) * Mathf.Rad2Deg, 4f, 50f);
                    var (m2, px2) = Shot(c, ground, lampsOff);
                    if (px2 > 0 && m2 >= 0) { s += m2 * px2; n += px2; }
                }
                m = n > 0 ? s / n : -1; px = n;
            }
            if (lampsOff) { c.NightNoLamps = m; return; }
            if (noUnitRim) { c.NightNoUnitRim = m; return; }
            if (asUnit) { c.NightAsUnit = m; return; }
            if (night) { c.NightL = m; c.NightPx = px; } else { c.DayL = m; c.DayPx = px; }
        }

        // ------------------------------------------------------------------ report
        void Report(float tn)
        {
            var g = cats.Find(c => c.Name == "ground");
            double gr = g != null && g.DayL > 1e-6 && g.NightL >= 0 ? g.NightL / g.DayL : -1;
            var sb = new StringBuilder(); var csv = new StringBuilder("class,rule,materials,day_px,day_lum,night_px,night_lum,night_over_day,relative_to_ground,verdict,note\n");
            string level = ZGame.I ? ZGame.I.level : "?";
            sb.Append($"[OpenZ] NightLumaCheck {level}  night t {tn:F3}  (linear scene luminance of each class's own pixels, no post; ground night/day = {(gr > 0 ? gr.ToString("F3") : "n/a")})\n");
            sb.Append($"  {"class",-13} {"day",8} {"night",8} {"n/d",6} {"x ground",8}  verdict  materials (TeamLit night class)\n");
            int fails = 0, warns = 0;
            foreach (var c in cats)
            {
                double r = c.DayL > 1e-6 && c.NightL >= 0 ? c.NightL / c.DayL : -1;
                double rel = r > 0 && gr > 0 ? r / gr : -1;
                string v, note = "";
                if (c.Rs.Count == 0) { v = "absent"; }
                else if (c.NightL < 0 || (c.DayL < 0 && gr > 0)) { v = "no px"; note = "not visible from the view or the close-ups"; }
                else if (rel < 0) { v = "INFO"; note = "night only (run with the day reference for ratios)"; }
                else switch (c.Rule)
                {
                    case Rule.Ground: v = "REF"; break;
                    case Rule.Env: v = rel <= 1.6 ? "PASS" : rel <= 2.2 ? "WARN" : "FAIL"; if (rel < 0.35) { v = "WARN"; note = "darker than the ground at night"; } else if (v != "PASS") note = "keeps more daylight than the ground (bright as day)"; break;
                    case Rule.Building: v = rel <= 2.2 ? "PASS" : rel <= 3.0 ? "WARN" : "FAIL"; if (v != "PASS") note = "whole building glows (lamps should be spots)"; break;
                    case Rule.Item: v = rel <= 2.6 ? "PASS" : rel <= 3.5 ? "WARN" : "FAIL"; break;
                    case Rule.Unit: v = rel <= 5 ? "INFO" : "WARN"; note = "moonlit rim + fill by design (readable at 70 m)"; break;
                    default: v = "INFO"; break;
                }
                if (v == "FAIL") fails++; if (v == "WARN") warns++;
                if (c.Name == "buildings" && c.NightNoLamps > 0 && c.NightL > 0) note += (note.Length > 0 ? "; " : "") + $"wall lamps lift the buildings x{c.NightL / c.NightNoLamps:F2} at night";
                if (c.NightAsUnit > 0 && c.DayL > 1e-6 && gr > 0)
                    note += (note.Length > 0 ? "; " : "") + $"with unit night terms x{c.NightAsUnit / c.DayL / gr:F2} ground";
                if (c.Rule == Rule.Unit && c.NightNoUnitRim > 0 && c.NightL > 0)
                {
                    double lift = c.NightL / c.NightNoUnitRim;
                    note += (note.Length > 0 ? "; " : "") + $"moonlit rim + fill lift x{lift:F2}";
                    if (lift < 1.03) { if (v == "WARN") warns--; if (v != "FAIL") { v = "FAIL"; fails++; } note += " <- units get NO night rim / fill (auto night class -> environment?)"; }
                }
                sb.Append($"  {c.Name,-13} {Fmt(c.DayL),8} {Fmt(c.NightL),8} {(r > 0 ? r.ToString("F3") : "-"),6} {(rel > 0 ? rel.ToString("F2") : "-"),8}  {v,-7}  {c.Classes}{(note.Length > 0 ? "  <- " + note : "")}\n");
                csv.Append($"{c.Name},{c.Rule},\"{c.Classes}\",{c.DayPx},{Fmt(c.DayL)},{c.NightPx},{Fmt(c.NightL)},{(r > 0 ? r.ToString("F4") : "")},{(rel > 0 ? rel.ToString("F3") : "")},{v},\"{note}\"\n");
            }
            if (NightV2.I) sb.Append("  ").Append(NightV2.I.Status().Replace("\n", "\n  "));
            sb.Append($"\nNIGHT LUMA CHECK {(fails > 0 ? "FAILED" : "OK")} ({fails} fail, {warns} warn)");
            LastReport = sb.ToString();
            if (fails > 0) Debug.LogWarning(LastReport); else Debug.Log(LastReport);
            if (Application.isEditor)
            {
                try
                {
                    var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "..", "work", "records", "night_v2"));
                    Directory.CreateDirectory(dir);
                    var path = Path.Combine(dir, $"luma_{level}_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv");
                    File.WriteAllText(path, csv.ToString());
                    Debug.Log("[OpenZ] NightLumaCheck: " + path);
                }
                catch (System.Exception e) { Debug.LogWarning("[OpenZ] NightLumaCheck: CSV not written: " + e.Message); }
            }
        }

        static string Fmt(double v) => v < 0 ? "-" : v.ToString("F4");

        DayNight dn; float t0; bool run0, clockTouched;

        void RestoreClock()
        {
            if (!clockTouched) return;
            clockTouched = false;
            if (dn) { dn.timeOfDay = t0; dn.Apply(t0); dn.running = run0; }
        }

        void OnDestroy()
        {
            if (all != null) Restore();
            RestoreClock();
            Running = false;
        }

        void Finish()
        {
            RestoreClock();
            Running = false;
            if (rt) { rt.Release(); Destroy(rt); }
            if (read) Destroy(read);
            Destroy(gameObject);
        }
    }
}
