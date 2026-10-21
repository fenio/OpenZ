using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Level 16 weather, the counted Play check (menu OpenZ/Checks/Level 16 weather (Play mode)). On level 16 it runs for
    /// about five minutes and proves, with numbers: the objects are there, the frame cost of the calm level beside the
    /// numbers of the plant step, the plants are in the wind (each in its cell), the calm wind, the midday air against the
    /// air the level was approved with (over open grass and over a swamp pool), a forced rain spell (wind first, rain, wet
    /// ground, light, haze, sounds in the sound log, thunder), the lean of five trees calm and in rain (the sway's plain
    /// number twin with the live wind, and one tree measured on its drawn outline), the frame cost in rain, the dry ground
    /// coming back exactly (a picture of open grass before the rain against the same view after drying), drips and steam
    /// after the rain, the WEATHER option off, a wind that turns without a snap when the spell blows against the calm wind,
    /// and a felled tree that stands still at once. It writes full-screen pictures from the normal game camera for the eye.
    /// On the two other levels it proves in a few seconds that nothing of this step is there. The rules stand still while
    /// it runs, except at the start (the frame cost is taken as the plant step took it: in a running game) and for the
    /// moment at the end in which a test blast fells one tree: that tree stays felled, so start the level again before
    /// anything else is judged in that match. The clock, the camera, the pause and the player's WEATHER setting are put back.
    /// Second menu: the match fingerprint (a computer match with the weather on, then off: the same rules state at the same
    /// rule tick).
    /// </summary>
    public static class OzL16WeatherCheck
    {
        const string Tag = "[OpenZ L16 weather]";
        public static string Report = "not run";
        static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        static int waitFrame, ok, bad; static float waitUntil;
        static readonly StringBuilder text = new StringBuilder();
        static string dir;
        static bool kept, weather0, paused0, running0; static float clock0, camH0; static Vector3 camF0;

        [MenuItem("OpenZ/Checks/Level 16 weather (Play mode)")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying || !ZGame.I || ZGame.I.World == null) { Debug.LogWarning(Tag + " enter Play mode in a level first"); return; }
            if (stack.Count > 0) { Debug.LogWarning(Tag + " a run is still going"); return; }
            ok = bad = 0; text.Clear(); Report = "running"; dir = OzL16Weather.OutDir();
            stack.Push(ZGame.I.level == "L16" ? Jungle() : Other());
            waitFrame = 0; waitUntil = 0f; EditorApplication.update += Step;
        }

        static void Step()
        {
            if (stack.Count == 0 || !EditorApplication.isPlaying) { stack.Clear(); PutBack(); EditorApplication.update -= Step; return; }
            if (Time.frameCount < waitFrame || Time.realtimeSinceStartup < waitUntil) return;
            var top = stack.Peek(); bool more;
            try { more = top.MoveNext(); }
            catch (System.Exception ex) { Debug.LogException(ex); stack.Clear(); PutBack(); Report = Tag + " FAIL: exception " + ex.Message; Debug.LogError(Report); EditorApplication.update -= Step; return; }
            if (!more) { stack.Pop(); if (stack.Count == 0) EditorApplication.update -= Step; return; }
            if (top.Current is IEnumerator child) stack.Push(child);
            else if (top.Current is float s) waitUntil = Time.realtimeSinceStartup + s;       // seconds, real time
            else waitFrame = Time.frameCount + (top.Current is int n ? n : 1);                // frames
        }

        static void Line(bool pass, string what) { if (pass) ok++; else bad++; text.Append(pass ? "PASS  " : "FAIL  ").Append(what).Append('\n'); }
        static void Note(string what) { text.Append("      ").Append(what).Append('\n'); }

        static void Finish(string where)
        {
            PutBack();
            Report = (bad == 0 ? "PASS " : "FAIL ") + ok + "/" + (ok + bad) + " lines: " + where + ", " + Screen.width + "x" + Screen.height + "\n" + text;
            File.WriteAllText(Path.Combine(dir, "OzL16WeatherCheck_" + (ZGame.I ? ZGame.I.level : "level") + ".txt"), Report);
            if (bad == 0) Debug.Log(Tag + " " + Report); else Debug.LogError(Tag + " " + Report);
        }

        static void Keep()
        {
            var g = ZGame.I; var dn = Object.FindFirstObjectByType<DayNight>(); var rts = Object.FindFirstObjectByType<RtsCamera>();
            weather0 = OzWeather.Enabled; paused0 = g.paused; running0 = dn && dn.running; clock0 = dn ? dn.timeOfDay : 0f;
            camF0 = rts ? rts.focus : Vector3.zero; camH0 = rts ? rts.height : 70f; kept = true;
        }

        static void PutBack()
        {
            FreeRun(false);
            if (!kept) return; kept = false;
            if (OzWeather.Enabled != weather0) OzWeather.Enabled = weather0;
            if (OzJungleWeather.I) OzJungleWeather.I.StopSpell(1f);
            var g = ZGame.I; if (g) g.paused = paused0;
            var dn = Object.FindFirstObjectByType<DayNight>(); if (dn) { dn.running = running0; dn.timeOfDay = clock0; }
            var rts = Object.FindFirstObjectByType<RtsCamera>(); if (rts) { rts.focus = camF0; rts.SetHeightImmediate(camH0); }
            var at = Object.FindFirstObjectByType<Atmosphere>(); if (at && g && g.level == "L16" && OzJungleWeather.I) at.preset = Atmosphere.Preset.Jungle;
        }

        // ------------------------------------------------------------------ small tools
        static void Cam(float x, float z, float h) { var rts = Object.FindFirstObjectByType<RtsCamera>(); if (rts) { rts.focus = new Vector3(x, 0f, z); rts.SetHeightImmediate(h); } }
        static void Shot(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
        static float G(string name) => Shader.GetGlobalFloat(name);
        static Vector4 V(string name) => Shader.GetGlobalVector(name);

        static Texture2D grabbed; static bool grabbing;
        static IEnumerator GrabCo() { yield return new WaitForEndOfFrame(); grabbed = ScreenCapture.CaptureScreenshotAsTexture(); grabbing = false; }
        /// <summary>The Game view of this frame as a texture (in `grabbed`).</summary>
        static IEnumerator Grab() { grabbing = true; grabbed = null; ZGame.I.StartCoroutine(GrabCo()); int guard = 0; while (grabbing && guard++ < 120) yield return 1; }

        /// <summary>Mean difference per colour value (0..255) between two frames inside the middle box (share of the frame), and the box's mean colour.</summary>
        static float BoxDiff(Texture2D a, Texture2D b, float share, out Color meanA, out Color meanB)
        {
            meanA = meanB = Color.black;
            if (!a || !b || a.width != b.width || a.height != b.height) return 999f;
            int w = a.width, h = a.height, x0 = (int)(w * (0.5f - share * 0.5f)), x1 = (int)(w * (0.5f + share * 0.5f)), y0 = (int)(h * (0.5f - share * 0.5f)), y1 = (int)(h * (0.5f + share * 0.5f));
            var pa = a.GetPixels32(); var pb = b.GetPixels32(); double sum = 0, ra = 0, ga = 0, ba = 0, rb = 0, gb = 0, bb = 0; long n = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    var p = pa[y * w + x]; var q = pb[y * w + x];
                    sum += Mathf.Abs(p.r - q.r) + Mathf.Abs(p.g - q.g) + Mathf.Abs(p.b - q.b);
                    ra += p.r; ga += p.g; ba += p.b; rb += q.r; gb += q.g; bb += q.b; n++;
                }
            if (n == 0) return 999f;
            meanA = new Color((float)(ra / n), (float)(ga / n), (float)(ba / n)); meanB = new Color((float)(rb / n), (float)(gb / n), (float)(bb / n));
            return (float)(sum / (3.0 * n));
        }

        static string Rgb(Color c) => Mathf.RoundToInt(c.r) + " " + Mathf.RoundToInt(c.g) + " " + Mathf.RoundToInt(c.b);

        static int SoundCount(float since, string key, string outcomeStarts)
        {
            int n = 0;
            foreach (var e in OzSoundLog.Since(since)) if (e.key == key && e.outcome != null && e.outcome.StartsWith(outcomeStarts)) n++;
            return n;
        }

        /// <summary>Open grass with nothing on it and nothing near: the place for the dry / wet / dry pictures. clear = metres to the nearest thing.</summary>
        public static bool FindGrass(out Vector3 spot, out float clear)
        {
            spot = Vector3.zero; clear = 0f;
            var g = ZGame.I; var map = g.World.Map; int W = map.width, H = map.height;
            var occ = new List<Vector2>();
            var env = GameObject.Find("Environment"); var flora = env ? env.transform.Find("L16_Flora") : null;
            if (flora) foreach (Transform c in flora) foreach (Transform p in c) if (p.name != "FAR") occ.Add(new Vector2(p.position.x, p.position.z));
            foreach (var o in g.World.Objects) if (!o.Destroyed) { var p = g.CenterWorld(o, false); occ.Add(new Vector2(p.x, p.z)); }
            float best = -1f;
            for (int ty = 6; ty < H - 6; ty += 2)
                for (int tx = 6; tx < W - 6; tx += 2)
                {
                    bool grass = true;
                    for (int dy = -4; dy <= 4 && grass; dy++) for (int dx = -5; dx <= 5; dx++) if (map.ClassAt(tx + dx, ty + dy) != ZClass.Ground) { grass = false; break; }
                    if (!grass) continue;
                    var c = new Vector2(tx * 2f + 1f, -(ty * 2f + 1f)); float near = 60f;
                    for (int i = 0; i < occ.Count; i++) { float d = (occ[i] - c).sqrMagnitude; if (d < near * near) near = Mathf.Sqrt(d); }
                    if (near > best) { best = near; spot = new Vector3(c.x, 0f, c.y); }
                }
            clear = best;
            return best > 0f;
        }

        // ------------------------------------------------------------------ the trees that are measured
        sealed class Tree
        {
            public OzFloraWind.Plant P; public string Label; public Vector3 Foot, Top, Crown; public float Sc, SumDown, MaxDown; public int Samples;
            public float LeanDeg => Mathf.Atan2(Samples > 0 ? SumDown / Samples : 0f, Mathf.Max(0.1f, Crown.y - Foot.y)) * Mathf.Rad2Deg;
            public void Clear() { SumDown = 0f; MaxDown = 0f; Samples = 0; }
        }

        static List<Tree> PickTrees(Vector3 near)
        {
            var list = new List<Tree>(); var w = OzFloraWind.I; if (!w) return list;
            foreach (var f in OzV11SwayCount.Five)
            {
                OzFloraWind.Plant best = null; float bd = float.MaxValue;
                foreach (var p in w.All)
                {
                    if (!p.R) continue; var mf = p.R.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh || mf.sharedMesh.name.IndexOf(f.Part, System.StringComparison.Ordinal) < 0) continue;
                    float d = (p.R.transform.position - near).sqrMagnitude; if (d < bd) { bd = d; best = p; }
                }
                if (best == null) continue;
                var t = best.R.transform; var mesh = best.R.GetComponent<MeshFilter>().sharedMesh; var vs = mesh.vertices;
                var crown = t.position; foreach (var v in vs) { var q = t.TransformPoint(v); if (q.y > crown.y) crown = q; }
                list.Add(new Tree { P = best, Label = f.Label, Foot = t.position, Top = t.TransformPoint(best.PivotMesh), Crown = crown, Sc = Mathf.Abs(t.lossyScale.x) });
            }
            return list;
        }

        /// <summary>The trees' crown tops through the sway's plain number twin with the wind of this frame (as near the camera: full movement).</summary>
        static void Sample(List<Tree> trees)
        {
            var w = OzFloraWind.I; if (!w) return; var wind = w.CurrentWind();
            foreach (var t in trees)
            {
                OzFloraWindMath.Displace(t.P.Kind, wind, t.Foot.x, t.Foot.y, t.Foot.z, t.Top.x, t.Top.y, t.Top.z, t.Sc, t.P.Radius * t.Sc, t.P.Height * t.Sc, 1f,
                    t.Crown.x, t.Crown.y, t.Crown.z, out float dx, out _, out float dz);
                float down = dx * wind.Wx + dz * wind.Wz; t.SumDown += down; t.MaxDown = Mathf.Max(t.MaxDown, down); t.Samples++;
            }
        }

        static IEnumerator SampleFor(List<Tree> trees, float seconds)
        {
            foreach (var t in trees) t.Clear();
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) { Sample(trees); yield return 1; }
        }

        // ---- one tree on its drawn outline: its depth pass (the same vertex code as the picture) drawn from the side, across the wind
        static bool Outline(MeshRenderer r, Vector2 wind, out float crownX, out int lit)
        {
            crownX = 0f; lit = 0;
            var mf = r.GetComponent<MeshFilter>(); var mesh = mf ? mf.sharedMesh : null; if (!mesh) return false;
            var mats = r.sharedMaterials; var b = r.bounds;
            float half = Mathf.Max(b.extents.x, b.extents.z) + 4f, tall = b.size.y + 4f;
            var W = new Vector3(wind.x, 0f, wind.y); var fwd = Vector3.Cross(W, Vector3.up).normalized;       // screen right = downwind
            var pos = new Vector3(b.center.x, b.min.y + tall * 0.5f - 1f, b.center.z) - fwd * 80f;
            var view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(pos, Quaternion.LookRotation(fwd, Vector3.up), Vector3.one).inverse;
            var proj = GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-half, half, -tall * 0.5f, tall * 0.5f, 1f, 200f), true);
            const int N = 384;
            var rt = RenderTexture.GetTemporary(N, N, 24, RenderTextureFormat.ARGB32);
            var cb = new CommandBuffer { name = "OzL16WeatherCheck outline" };
            cb.SetRenderTarget(rt); cb.ClearRenderTarget(true, true, Color.clear);
            cb.SetViewProjectionMatrices(view, proj);
            int drawn = 0;
            for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
            {
                if (!mats[s]) continue; int pass = mats[s].FindPass("DepthOnly"); if (pass < 0) continue;
                cb.SetInvertCulling(false); cb.DrawRenderer(r, mats[s], s, pass);
                cb.SetInvertCulling(true); cb.DrawRenderer(r, mats[s], s, pass);                             // both face sides: an outline, whatever the winding
                drawn++;
            }
            cb.SetInvertCulling(false);
            Graphics.ExecuteCommandBuffer(cb); cb.Release();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false); tex.ReadPixels(new Rect(0, 0, N, N), 0, 0); tex.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels32(); Object.DestroyImmediate(tex);
            if (drawn == 0) return false;
            int topRow = -1, lowRow = N;
            for (int y = 0; y < N; y++) { int c = 0; for (int x = 0; x < N; x++) if (px[y * N + x].r > 2) c++; if (c >= 2) { if (y > topRow) topRow = y; if (y < lowRow) lowRow = y; lit += c; } }
            if (topRow < 0 || topRow - lowRow < 8) return false;
            // which way is up in the read-back differs by platform: the crown is the wide end
            int span = topRow - lowRow, band = Mathf.Max(3, span / 5); double wA = 0, wB = 0;
            for (int y = lowRow; y < lowRow + band; y++) for (int x = 0; x < N; x++) if (px[y * N + x].r > 2) wA++;
            for (int y = topRow - band + 1; y <= topRow; y++) for (int x = 0; x < N; x++) if (px[y * N + x].r > 2) wB++;
            int from = wB >= wA ? topRow - band + 1 : lowRow, to = wB >= wA ? topRow : lowRow + band - 1;
            double sx = 0; long n = 0;
            for (int y = from; y <= to; y++) for (int x = 0; x < N; x++) if (px[y * N + x].r > 2) { sx += x + 0.5; n++; }
            if (n == 0) return false;
            crownX = ((float)(sx / n) / N - 0.5f) * 2f * half;
            return true;
        }

        /// <summary>The same band of the same tree through the plain number twin: mean downwind place of the crown's top fifth, in metres from the tree's rest place.</summary>
        static float TwinCrown(Tree t)
        {
            var w = OzFloraWind.I; var wind = w.CurrentWind(); var tr = t.P.R.transform; var vs = t.P.R.GetComponent<MeshFilter>().sharedMesh.vertices;
            float lo = float.MaxValue, hi = float.MinValue; foreach (var v in vs) { float y = tr.TransformPoint(v).y; lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y); }
            float cut = hi - (hi - lo) * 0.2f; double sum = 0; int n = 0;
            float ax = tr.right.x, az = tr.right.z; float al = Mathf.Sqrt(ax * ax + az * az); if (al > 1e-4f) { ax /= al; az /= al; } else { ax = 1f; az = 0f; }
            foreach (var v in vs)
            {
                var q = tr.TransformPoint(v); if (q.y < cut) continue;
                OzFloraWindMath.Displace(t.P.Kind, wind, t.Foot.x, t.Foot.y, t.Foot.z, t.Top.x, t.Top.y, t.Top.z, t.Sc, t.P.Radius * t.Sc, t.P.Height * t.Sc, w.FadeAt(t.Foot, t.P.Family),
                    q.x, q.y, q.z, out float dx, out _, out float dz, ax, az);
                sum += dx * wind.Wx + dz * wind.Wz; n++;
            }
            return n > 0 ? (float)(sum / n) : 0f;
        }

        static IEnumerator OutlineMean(Tree t, int shots, float[] result)
        {   // result[0] = drawn crown place (m, downwind), [1] = the twin's, [2] = shots that drew an outline
            double a = 0, b = 0; int n = 0;
            for (int i = 0; i < shots; i++)
            {
                var wind = OzFloraWind.I.CurrentWind();
                if (Outline(t.P.R, new Vector2(wind.Wx, wind.Wz), out float cx, out _)) { a += cx; b += TwinCrown(t); n++; }
                yield return 0.45f;
            }
            result[0] = n > 0 ? (float)(a / n) : 0f; result[1] = n > 0 ? (float)(b / n) : 0f; result[2] = n;
        }

        static float frameMs; static bool freeOn; static int vsync0, fps0;
        /// <summary>The frame rate runs free while a frame cost is taken (as the numbers of the plant step were taken), and only then.</summary>
        static void FreeRun(bool on)
        {
            if (on == freeOn) return; freeOn = on;
            if (on) { vsync0 = QualitySettings.vSyncCount; fps0 = Application.targetFrameRate; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; }
            else { QualitySettings.vSyncCount = vsync0; Application.targetFrameRate = fps0; }
        }

        static IEnumerator FrameCost(float seconds)
        {
            FreeRun(true); yield return 30;
            int f0 = Time.frameCount; float t0 = Time.realtimeSinceStartup;
            yield return seconds;
            int df = Time.frameCount - f0; frameMs = df > 0 ? (Time.realtimeSinceStartup - t0) * 1000f / df : 0f;
            FreeRun(false);
        }

        /// <summary>Mean frame time over `frames` frames after `settle` frames, the frame rate running free.</summary>
        static IEnumerator FrameCostFrames(int settle, int frames)
        {
            FreeRun(true); yield return settle;
            int f0 = Time.frameCount; float t0 = Time.realtimeSinceStartup;
            while (Time.frameCount - f0 < frames) yield return 1;
            int df = Time.frameCount - f0; frameMs = df > 0 ? (Time.realtimeSinceStartup - t0) * 1000f / df : 0f;
            FreeRun(false);
        }

        // ---- the level wind from frame to frame: how far it turns in one frame, how slow it gets
        static float turnMax, speedMin; static int turnFrames, turnBad; static Vector2 turnPrev; static bool turnHas;
        static void TurnClear() { turnMax = 0f; speedMin = float.MaxValue; turnFrames = turnBad = 0; turnHas = false; }
        static IEnumerator WatchWind(float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds; int last = -1;
            while (Time.realtimeSinceStartup < until)
            {
                if (Time.frameCount != last)
                {
                    last = Time.frameCount;
                    var f = V("_FogWind"); var w = new Vector2(f.x, f.z); float sp = w.magnitude;
                    speedMin = Mathf.Min(speedMin, sp);
                    if (turnHas)
                    {
                        float turn = sp > 1e-4f && turnPrev.sqrMagnitude > 1e-8f ? Vector2.Angle(turnPrev, w) : 180f;   // a dead calm has no direction: the worst
                        turnMax = Mathf.Max(turnMax, turn); turnFrames++;
                        float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
                        if (turn >= 10f && (dt <= 0.1f || turn / dt > 100f)) turnBad++;
                    }
                    turnPrev = w; turnHas = true;
                }
                yield return 1;
            }
        }

        /// <summary>The plant step's own frame cost (mean ms of its three views over the map's middle: from 70 m, from 120 m,
        /// the whole map from 230 m), read from its record in the folder beside this step's; the numbers as that record had
        /// them when this check was written if the file is not there.</summary>
        static float[] PlantStepCost(out string from)
        {
            var ms = new[] { 13.95f, 15.43f, 18.10f }; from = "its record was not found: the numbers it had when this check was written";
            string file = Path.Combine(Path.GetDirectoryName(dir.TrimEnd('/', '\\')) ?? "", "J6", "frame_cost_J6_fresh_start_near_halved.txt");
            if (!File.Exists(file)) return ms;
            string[] keys = { "centre h70:", "centre h120:", "whole map h230:" }; int found = 0;
            foreach (var row in File.ReadAllLines(file))
                for (int i = 0; i < 3; i++)
                {
                    if (!row.StartsWith(keys[i], System.StringComparison.Ordinal)) continue;
                    int a = row.IndexOf("avg ", System.StringComparison.Ordinal), b = row.IndexOf(" ms", System.StringComparison.Ordinal);
                    if (a >= 0 && b > a && float.TryParse(row.Substring(a + 4, b - a - 4), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) { ms[i] = v; found++; }
                }
            if (found == 3) from = "read from " + file.Replace('\\', '/');
            return ms;
        }

        /// <summary>Renderers under root that are drawn with a plant wind material (a material that carries a plant kind).</summary>
        static int WindMats(Transform root)
        {
            int n = 0; if (!root) return 0;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m && m.HasProperty("_OzWindPalm") && m.GetVector("_OzWindPalm").w < -1.5f) { n++; break; }
            return n;
        }

        // ================================================================== level 16
        static IEnumerator Jungle()
        {
            Keep();
            var g = ZGame.I; var wx = OzJungleWeather.I; var fw = OzFloraWind.I; var dn = Object.FindFirstObjectByType<DayNight>(); var at = Object.FindFirstObjectByType<Atmosphere>();
            var map = g.World.Map; float cx = map.width, cz = -map.height;                              // the middle of the map (tiles of 2 m)
            Line(wx && fw && at && at.preset == Atmosphere.Preset.Jungle, $"level 16 has its weather object ({(wx ? "yes" : "NO")}), its plant wind ({(fw ? "yes" : "NO")}) and the Jungle air ({(at ? at.preset.ToString() : "no air object")})");
            Line(Mathf.Abs(DayNight.LevelStartTod - DayNight.SunriseStart) < 0.0005f && Mathf.Abs(DayNight.SunriseStart - 0.935f) < 0.0005f, $"the level started at sunrise like the others: day clock {DayNight.LevelStartTod:F3} (wanted {DayNight.SunriseStart:F3} = 0.935)");
            if (!wx || !fw) { Finish("level 16 weather"); yield break; }
            OzWeather.Enabled = true;
            if (wx.SpellActive) { wx.StopSpell(1f); yield return 2.5f; }
            if (dn) { dn.running = false; dn.timeOfDay = 0.03f; dn.Apply(0.03f); }                         // the light of early afternoon, held
            OzJungleWeather.HoldLowPuddles(-1f);                                                          // the rain puddles of the low grass and the paths follow the rain again (a proof picture's hold let go)
            float wait = 0f; while ((wx.Wetness > 0f || wx.LowPuddleLevel > 0f) && wait < 160f) { wait += 1f; yield return 1f; }   // the last rain's puddles outlast the damp ground: dry first

            // The two accepted limits (decided on the numbers measured with structures, animals and weather in the level): the calm
            // level from 120 m may cost CalmFrameMore ms more a frame than at the plant step (measured 16.70 against 15.43), and the
            // pool's picture may differ by PoolDiffMore more than two frames of the same air do (measured 1.44 against 0.39).
            const float CalmFrameMore = 1.27f, PoolDiffMore = 1.05f;
            // ---- 0. the frame cost of the calm level beside the numbers of the plant step (J6), taken its way: a running game,
            //         midday, the map's middle, 200 frames after 60, the frame rate running free. The plant wind re-set the
            //         change-over of every plant cell at level start: this is where a cost of that shows.
            bool rulesRan = !g.paused; var j6h = new[] { 70f, 120f, 230f }; var j6ms = PlantStepCost(out string j6from); var nowMs = new float[3];
            for (int i = 0; i < 3; i++) { Cam(cx, cz, j6h[i]); yield return FrameCostFrames(60, 200); nowMs[i] = frameMs; }
            bool sameView = Screen.width == 2560 && Screen.height == 1600 && OzQuality.Name == "HIGH";
            Line(sameView && nowMs[1] <= j6ms[1] + CalmFrameMore && nowMs[2] <= j6ms[2] + 1f,
                $"frame cost of the calm level beside the plant step's own numbers ({j6from}): the map's middle from 70 m {nowMs[0]:0.00} ms (then {j6ms[0]:0.00}), from 120 m {nowMs[1]:0.00} ms (then {j6ms[1]:0.00}; wanted at most {j6ms[1] + CalmFrameMore:0.00}), the whole map from 230 m {nowMs[2]:0.00} ms (then {j6ms[2]:0.00}; wanted at most {j6ms[2] + 1f:0.00}); Game view {Screen.width} x {Screen.height} (then 2560 x 1600), quality {OzQuality.Name} (then HIGH), the rules {(rulesRan ? "running" : "PAUSED when the check began: the numbers are a little low")}, game speed {g.gameSpeed:0.##}x, 200 frames after 60{(sameView ? "" : "; NOT COMPARABLE: set the Game view to 2560 x 1600 and the quality to HIGH and run again")}");
            Note($"the plant cells before they were made even: tree cells of size {fw.TreeSizesBefore.x:0.0} to {fw.TreeSizesBefore.y:0.0} m changed to their far mesh at {fw.ChangeOverOf(fw.TreeSizesBefore.x, true):0} to {fw.ChangeOverOf(fw.TreeSizesBefore.y, true):0} m from their own middle point, small plant cells of size {fw.PlantSizesBefore.x:0.0} to {fw.PlantSizesBefore.y:0.0} m at {fw.ChangeOverOf(fw.PlantSizesBefore.x, false):0} to {fw.ChangeOverOf(fw.PlantSizesBefore.y, false):0} m; now every tree cell has size {fw.TreeSize:0.0} m = {fw.TreeChangeOver:0} m from the cell's middle, every small plant cell {fw.PlantSize:0.0} m = {fw.PlantChangeOver:0} m (quality bias {QualitySettings.lodBias:0.##})");
            g.paused = true;                                                                              // from here the rules stand still: nothing walks into the pictures
            yield return 1.5f;

            // ---- 1. the plants are in the wind
            Note(OzFloraWind.Status());
            Line(OzFloraWind.Plants >= 6500 && OzFloraWind.Unknown == 0 && OzFloraWind.Batched == 0 && OzFloraWind.NoWindMaterial == 0 && OzFloraWind.OutOfCell == 0 && OzFloraWind.NoCell == 0,
                $"plants in the wind: {OzFloraWind.Plants} on {OzFloraWind.Copies} wind materials of {OzFloraWind.Shapes} shapes (the plant list of step J6 gives 6,873; wanted at least 6,500), unknown names {OzFloraWind.Unknown}, in a static batch {OzFloraWind.Batched}, without a wind material {OzFloraWind.NoWindMaterial}, outside their cell's square {OzFloraWind.OutOfCell}, under the plant root without a cell {OzFloraWind.NoCell} (each wanted 0; a tree that can be felled stands in a holder inside its cell: {OzTreeFall.Bound} such trees are bound); still by the table {OzFloraWind.StillByTable}, tipped {OzFloraWind.Tilted}");
            Line(OzFloraWind.Cells > 400 && fw.TreeChangeOver > 60f && fw.Fade.y < fw.TreeChangeOver && fw.Fade.w < fw.PlantChangeOver,
                $"{OzFloraWind.Cells} plant cells change to their far mesh at {fw.TreeChangeOver:0} m (trees) and {fw.PlantChangeOver:0} m (small plants) from the camera to the cell's middle; the movement is full to {fw.Fade.x:0} / {fw.Fade.z:0} m and none from {fw.Fade.y:0} / {fw.Fade.w:0} m: it has ended before the change");

            // ---- 2. calm
            var fog = V("_FogWind"); float speed = new Vector2(fog.x, fog.z).magnitude; var flag = V("_OZFlagWind");
            Line(Mathf.Abs(speed - 2.5f) <= 0.1f && Mathf.Abs(fw.Strength - 0.75f) <= 0.05f && fw.Storm == 0f && (flag.w < 0.01f || Mathf.Abs(flag.x - 0.75f) <= 0.05f),
                $"calm: the level wind is {speed:0.00} m/s (wanted 2.5; the wind fallbacks of flags, building parts, fort guns and palms are not reached), flags read strength {flag.x:0.00}, plants {fw.Strength:0.00} (wanted 0.75), rain wind {fw.Storm:0.00}");
            Line(G("_OzWetness") == 0f && G("_OzRain") == 0f && wx.Drops == 0 && !(AudioDirector.I && AudioDirector.I.LoopActive("wx16:bed")) && G("_LowFogDensity") == 0f && G("_LowFogMode") > 0.5f,
                $"dry: wetness {G("_OzWetness")}, rain {G("_OzRain")}, {wx.Drops} drops, rain bed {(AudioDirector.I && AudioDirector.I.LoopActive("wx16:bed") ? "PLAYING" : "silent")}; midday: low mist {G("_LowFogDensity")} (wanted exactly 0, as the air of today has by day) on the fog pass's branch {G("_LowFogMode"):0} (wanted 1, as today)");

            // the five trees of the design's sheet, the nearest of each to the map's middle
            var trees = PickTrees(new Vector3(cx, 0f, cz));
            yield return SampleFor(trees, 8f);
            var calmLean = new float[trees.Count]; for (int i = 0; i < trees.Count; i++) calmLean[i] = trees[i].LeanDeg;
            Tree seen = trees.Count > 1 ? trees[1] : trees.Count > 0 ? trees[0] : null;                    // the kapok: drawn outline against the twin
            var calmOutline = new float[3];
            if (seen != null) { Cam(seen.Foot.x, seen.Foot.z, 30f); yield return 20; yield return OutlineMean(seen, 8, calmOutline); }

            // pictures and frame cost, calm
            bool grassFound = FindGrass(out var grass, out float clear);
            Cam(cx, cz, 70f); yield return 30; yield return FrameCost(4f); float calmMs = frameMs; Shot("J9_calm_default_h70"); yield return 5;
            if (seen != null) { Cam(seen.Foot.x, seen.Foot.z, 22f); yield return 30; Shot("J9_calm_trees_h22"); yield return 5; }
            // the approved dry look and the air: the same grass from the default camera height under the Jungle air and under
            // the air the level had when its grass was approved (the city's preset)
            Cam(grass.x, grass.z, 70f); yield return 40; yield return Grab(); var airJ = grabbed;
            at.preset = Atmosphere.Preset.City; yield return 12; yield return Grab(); var airC = grabbed;
            at.preset = Atmosphere.Preset.Jungle; yield return 12;
            float airDiff = BoxDiff(airJ, airC, 0.4f, out var mJ, out var mC);
            Line(airDiff <= 2f, $"the approved dry ground keeps its look under the new air: default camera height over open grass at {grass.x:0}, {grass.z:0}, the middle 40 % of the frame under the Jungle air {Rgb(mJ)} and under the air it was approved with {Rgb(mC)}: mean difference {airDiff:0.00} of 255 a colour value (wanted at most 2.00: the level's own air moves the approved grass by about 1.1, not visible, accepted)");
            if (airJ) Object.Destroy(airJ); if (airC) Object.Destroy(airC);
            // the same over a swamp pool (the matched water), closest zoom. The water moves by itself: two frames under the same
            // air give the floor of the picture difference; the box's mean colour is what must not move.
            if (FindTile(ZClass.Water, out var calmPool))
            {
                Cam(calmPool.x, calmPool.z, 22f); yield return 40; Shot("J9_calm_swamp_pool_h22"); yield return 5;
                yield return Grab(); var pJ1 = grabbed; yield return 12; yield return Grab(); var pJ2 = grabbed;
                at.preset = Atmosphere.Preset.City; yield return 12; yield return Grab(); var pC = grabbed;
                at.preset = Atmosphere.Preset.Jungle; yield return 12;
                float floorDiff = BoxDiff(pJ1, pJ2, 0.4f, out _, out _), poolDiff = BoxDiff(pJ2, pC, 0.4f, out var pmJ, out var pmC);
                float meanMove = Mathf.Max(Mathf.Abs(pmJ.r - pmC.r), Mathf.Max(Mathf.Abs(pmJ.g - pmC.g), Mathf.Abs(pmJ.b - pmC.b)));
                Line(meanMove <= 2f && poolDiff <= floorDiff + PoolDiffMore, $"the matched water keeps its look under the new air: closest zoom over the pool at {calmPool.x:0}, {calmPool.z:0}, calm, midday, the middle 40 % of the frame: mean colour under the Jungle air {Rgb(pmJ)}, under the air it was matched with {Rgb(pmC)} (largest move of a colour value {meanMove:0.00}, wanted at most 2.00: the level's own air moves the pool by about 1.9, not visible, accepted); picture difference {poolDiff:0.00} against {floorDiff:0.00} between two frames of the same air (the water's own movement; wanted at most {PoolDiffMore:0.00} more)");
                if (pJ1) Object.Destroy(pJ1); if (pJ2) Object.Destroy(pJ2); if (pC) Object.Destroy(pC);
            }
            else Line(false, "the matched water under the new air: no water tile was found on the map");
            Cam(grass.x, grass.z, 22f); yield return 40; yield return Grab(); var dry0 = grabbed; Shot("J9_grass_dry_before_h22"); yield return 5;
            float sunI0 = dn && dn.sun ? dn.sun.intensity : 0f, sh0 = dn && dn.sun ? dn.sun.shadowStrength : 0f, fog0 = G("_FogDensity"), low0 = G("_LowFogDensity");

            // ---- 3. a forced spell, wind to the east
            float log0 = Time.unscaledTime; int spells0 = wx.SpellsStarted, rumbles0 = wx.RumblesPlayed;
            wx.ForceSpell(90f, 90f, false);                                                               // 15 s in, 50 s of full rain, 25 s out
            yield return 4f;
            Line(wx.SpellActive && wx.Rain == 0f && wx.Drops == 0 && wx.WindK > 0.15f && wx.WindK < 0.95f, $"wind first: 4 s into the spell the rain wind is at {wx.WindK:0.00} and no drop has fallen (rain {wx.Rain:0.00}, {wx.Drops} drops)");
            yield return 26f;                                                                              // 30 s in = 22 s of rain
            fog = V("_FogWind"); speed = new Vector2(fog.x, fog.z).magnitude; flag = V("_OZFlagWind");
            Line(wx.Rain >= 0.79f && wx.Drops >= 1000 && wx.Drops <= wx.maxDrops && wx.SplashesEmitted > 0 && G("_OzRain") >= 0.79f,
                $"rain: {wx.Rain:0.00} (wanted 0.80 to 1.00), {wx.Drops} drops alive (at most {wx.maxDrops}), {wx.SplashesEmitted} ground splashes so far, {wx.SplashesOnWater} left out because they fell on water (the water draws its own rings from the rain strength {G("_OzRain"):0.00})");
            Line(wx.Wetness >= 0.85f && G("_OzWetness") >= 0.85f && V("_OzWetParams").y > 0.2f && V("_OzWetParams").y < 0.3f, $"wet ground: wetness {G("_OzWetness"):0.00} after 22 s of rain (wanted at least 0.85: wet in 12 s), puddle cover {V("_OzWetParams").y:0.00} (a quarter, on road and bare soil only), grass takes {V("_OzJungleWet").x:0.00} of the darkening");
            Line(Mathf.Abs(speed - 5f) <= 0.3f && fog.x / Mathf.Max(0.01f, speed) > 0.95f && fw.Storm >= 0.99f && fw.Strength >= 1.3f && fw.Strength <= 1.65f,
                $"rain wind: {speed:0.00} m/s (wanted 5) to the east ({fog.x / Mathf.Max(0.01f, speed):0.00}, {fog.z / Mathf.Max(0.01f, speed):0.00}), flags read strength {flag.x:0.00}, plants {fw.Strength:0.00} (wanted about 1.5), rain wind {fw.Storm:0.00}");
            if (dn && dn.sun)
            {
                float wantI = sunI0 * (1f - wx.sunDim * wx.Rain), wantS = sh0 * (1f - wx.shadowSoften * wx.Rain), wantF = fog0 * (1f + OzJungleAir.RainFog * wx.Rain);
                Line(Mathf.Abs(dn.sun.intensity - wantI) <= 0.03f * sunI0 && Mathf.Abs(dn.sun.shadowStrength - wantS) <= 0.03f && Mathf.Abs(G("_FogDensity") - wantF) <= 0.08f * wantF,
                    $"rain light: sun {dn.sun.intensity / Mathf.Max(0.001f, sunI0) * 100f:0} % of its dry strength (wanted 65 % at full rain), shadows {dn.sun.shadowStrength / Mathf.Max(0.001f, sh0) * 100f:0} % (wanted 50 %), haze {G("_FogDensity") / Mathf.Max(1e-6f, fog0):0.00} times (wanted 1.8)");
            }
            // lean in rain
            yield return SampleFor(trees, 8f);
            int leanOk = 0; var sbLean = new StringBuilder();
            for (int i = 0; i < trees.Count; i++)
            {
                float r = trees[i].LeanDeg, c = calmLean[i]; bool good = r >= 2.5f && r >= 4f * Mathf.Max(c, 0.05f) && c <= 1.5f; if (good) leanOk++;
                sbLean.Append($"{trees[i].Label} (x{trees[i].Sc:0.00}): {c:0.00} -> {r:0.00} deg; ");
            }
            Line(trees.Count == 5 && leanOk == 5, $"lean of five trees, calm -> rain, by the sway's plain number twin with the live wind ({leanOk} of {trees.Count} lean at least 2.5 deg in rain, at least 4 times their calm lean, calm at most 1.5): {sbLean}");
            var rainOutline = new float[3];
            if (seen != null)
            {
                Cam(seen.Foot.x, seen.Foot.z, 30f); yield return 20; yield return OutlineMean(seen, 8, rainOutline);
                float drawn = rainOutline[0] - calmOutline[0], twin = rainOutline[1] - calmOutline[1];
                Line(calmOutline[2] >= 6 && rainOutline[2] >= 6 && drawn > 0.15f && Mathf.Abs(drawn - twin) <= Mathf.Max(0.15f, 0.3f * Mathf.Abs(twin)),
                    $"the same on the picture: the drawn outline of the {seen.Label} (its depth pass from the side, {calmOutline[2]:0} + {rainOutline[2]:0} outlines) has its crown {drawn:0.00} m further downwind in rain than in calm; the twin says {twin:0.00} m (wanted within 30 % or 0.15 m)");
            }
            // sounds so far
            int bed = SoundCount(log0, OzJungleWeather.BedId, "LOOP"), leaves = SoundCount(log0, OzJungleWeather.LeavesId, "LOOP"), firstSilent = SoundCount(log0, OzJungleWeather.FirstDropsId, "SILENT") + SoundCount(log0, OzJungleWeather.FirstDropsId, "PLAY"),
                windSilent = SoundCount(log0, OzJungleWeather.WindLoopId, "SILENT") + SoundCount(log0, OzJungleWeather.WindLoopId, "LOOP");
            Line(bed >= 1 && leaves >= 1 && firstSilent >= 1 && windSilent >= 1, $"sound log: rain bed loop started {bed}, rain on leaves loop {leaves}; the first drops {firstSilent} and the steady wind in the trees {windSilent} are in the log as named events (silent until their files exist)");
            // pictures and frame cost in rain
            Cam(cx, cz, 70f); yield return 30; yield return FrameCost(4f); float rainMs = frameMs; Shot("J9_rain_default_h70"); yield return 5;
            Line(rainMs <= 16.6f || rainMs - calmMs <= 2f, $"frame cost at the default camera over the map's middle: calm {calmMs:0.0} ms, in rain {rainMs:0.0} ms ({rainMs - calmMs:+0.0;-0.0} ms; wanted under 16.6 ms or at most 2.0 ms over calm)");
            if (seen != null) { Cam(seen.Foot.x, seen.Foot.z, 22f); yield return 25; Shot("J9_rain_trees_h22"); yield return 5; }
            if (FindTile(ZClass.Water, out var pool)) { Cam(pool.x, pool.z, 30f); yield return 25; Shot("J9_rain_swamp_rings_h30"); yield return 5; }
            if (FindTile(ZClass.Road, out var road)) { Cam(road.x, road.z, 22f); yield return 25; Shot("J9_rain_road_puddles_h22"); yield return 5; }
            Cam(grass.x, grass.z, 22f); yield return 25; Shot("J9_grass_wet_h22"); yield return 5;

            // ---- 4. the end of the spell: thunder, drips, steam, and the dry ground exactly as before
            if (seen != null) Cam(seen.Foot.x, seen.Foot.z, 30f);                                         // trees in view: the drips fall under crowns
            wait = 0f; while (wx.Rain > 0f && wait < 80f) { wait += 0.5f; yield return 0.5f; }
            float rainEnd = Time.realtimeSinceStartup; int drips0 = wx.DripsEmitted;
            int rum = wx.RumblesPlayed - rumbles0, rumLog = SoundCount(log0, OzJungleWeather.ThunderId, "PLAY");
            Line(rum >= 2 && rum <= 4 && rumLog == rum, $"thunder: {rum} soft far rumbles in the spell (wanted 2 to 4), {rumLog} in the sound log; no flash exists in this weather");
            yield return 6f;
            int dripLoop = SoundCount(log0, OzJungleWeather.DripsId, "LOOP");
            int dripsIn6 = wx.DripsEmitted - drips0;
            Line(dripsIn6 >= 100 && wx.CrownsInView > 0, $"after the rain: {dripsIn6} drip rings under the {wx.CrownsInView} tree crowns in view in the 6 s after the last drop (wanted at least 100: 60 a second x the wetness, now {wx.Wetness:0.00}; {wx.DripsEmitted} since the level began)");
            Line(OzJungleAir.SteamLand > 0.02f && OzJungleAir.SteamWater > 0.01f && G("_LowFogDensity") > low0 + 0.02f && G("_LowFogMode") > 0.5f && dripLoop >= 1,
                $"steam and drips are on while the ground dries: steam {OzJungleAir.SteamLand:0.000} per metre over land and {OzJungleAir.SteamWater:0.000} over water (dry: {low0:0.000}), on the fog pass's exactly integrated branch (mode {G("_LowFogMode"):0}: no grain), drips loop started {dripLoop}");
            wait = 0f; while (wx.Wetness > 0f && wait < 90f) { wait += 0.5f; yield return 0.5f; }
            float dryAfter = Time.realtimeSinceStartup - rainEnd;
            wait = 0f; while (wx.SpellActive && wait < 30f) { wait += 0.5f; yield return 0.5f; }          // the wind's last 15 s
            yield return 3f;
            // the rain puddles of the low grass and the gravel paths dry slowly after the damp ground (and keep the wet look's sky colour while they last)
            wait = 0f; while (wx.LowPuddleLevel > 0f && wait < 170f) { wait += 0.5f; yield return 0.5f; }
            float lowDry = Time.realtimeSinceStartup - rainEnd;
            Line(wx.LowPuddleLevel == 0f && V("_OzLowPuddle").x == 0f && lowDry >= 110f && lowDry <= 130f,
                $"the rain puddles of the low grass and the gravel paths dried {lowDry:0} s after the rain's last drop (this 90 s spell fills them to about 0.85 and they empty in 150 s from full: wanted 110 to 130 s with fill 80 s and dry 150 s; set now: fill {wx.lowFillSeconds:0} s, dry {wx.lowDrySeconds:0} s, amount {wx.lowAmount:0.00}): level {wx.LowPuddleLevel:0.00}, in the shader {V("_OzLowPuddle").x:0.00}");
            Line(wx.Wetness == 0f && G("_OzWetness") == 0f && G("_OzRain") == 0f && V("_OzWetSky") == Vector4.zero && !wx.SpellActive && dryAfter >= 25f && dryAfter <= 45f,
                $"dry again {dryAfter:0} s after the rain's last drop (the ground dries in 60 s from the moment the rain weakens, 25 s before its last drop: wanted 30 to 40): wetness {G("_OzWetness")}, rain {G("_OzRain")} (both exactly 0: the ground shader does not enter its wet part)");
            if (dn && dn.sun) Line(Mathf.Abs(dn.sun.intensity - sunI0) <= 0.001f && Mathf.Abs(dn.sun.shadowStrength - sh0) <= 0.001f && Mathf.Abs(G("_FogDensity") - fog0) <= 1e-6f && Mathf.Abs(G("_LowFogDensity") - low0) <= 1e-6f,
                $"light and air are the dry ones again: sun {dn.sun.intensity:0.000} ({sunI0:0.000}), shadows {dn.sun.shadowStrength:0.000} ({sh0:0.000}), haze {G("_FogDensity"):0.00000} ({fog0:0.00000}), ground mist {G("_LowFogDensity"):0.0000} ({low0:0.0000})");
            Cam(grass.x, grass.z, 22f); yield return 40; yield return Grab(); var dry1 = grabbed; Shot("J9_grass_dry_after_h22"); yield return 5;
            float diff = BoxDiff(dry0, dry1, 0.4f, out var m0, out var m1);
            Line(grassFound && diff <= 1f, $"the dry look came back: open grass at {grass.x:0}, {grass.z:0} (nothing within {clear:0} m), closest zoom, the middle 40 % of the frame before the rain {Rgb(m0)} and after drying {Rgb(m1)}: mean difference {diff:0.00} of 255 a colour value (wanted at most 1.00)");
            if (dry0) Object.Destroy(dry0); if (dry1) Object.Destroy(dry1);

            // ---- 5. the WEATHER option off
            float against = Mathf.Repeat(Mathf.Atan2(OzJungleAir.CalmWind.x, OzJungleAir.CalmWind.z) * Mathf.Rad2Deg + 177f, 360f);   // 3 degrees off straight against the calm wind
            TurnClear();
            wx.ForceSpell(45f, against, false);
            yield return WatchWind(26f);
            float rainBefore = wx.Rain; int started = wx.SpellsStarted;
            OzWeather.Enabled = false;
            yield return WatchWind(5f);
            fog = V("_FogWind"); speed = new Vector2(fog.x, fog.z).magnitude;
            Line(rainBefore > 0.5f && wx.Rain == 0f && wx.WindK == 0f && wx.Wetness == 0f && G("_OzRain") == 0f && G("_OzWetness") == 0f && !(AudioDirector.I && AudioDirector.I.LoopActive("wx16:bed")),
                $"WEATHER off in the middle of a spell (rain was {rainBefore:0.00}): 5 s later rain {wx.Rain:0.00}, rain wind {wx.WindK:0.00}, wetness {G("_OzWetness")}, rain bed {(AudioDirector.I && AudioDirector.I.LoopActive("wx16:bed") ? "PLAYING" : "silent")}");
            yield return WatchWind(4f);
            fog = V("_FogWind"); speed = new Vector2(fog.x, fog.z).magnitude;
            Line(wx.Drops == 0 && Mathf.Abs(speed - 2.5f) <= 0.15f && fw.Storm == 0f, $"and 9 s later: {wx.Drops} drops, the level wind back at {speed:0.00} m/s, the plants' rain wind {fw.Storm:0.00} (their quiet sway goes on: strength {fw.Strength:0.00})");
            Line(turnBad == 0 && turnFrames > 200 && speedMin >= 2.4f, $"the wind turns, it does not snap: this spell blew against the calm wind (to {against:0} deg); over {turnFrames} frames, from its first stir through the rain and the option's 3 s fade back to calm, the level wind turned by {turnMax:0.00} deg in one frame at the most (wanted under 10; a frame longer than 0.1 s is judged by its rate, at most 100 deg a second: {turnBad} frames over) and never fell under {speedMin:0.00} m/s (wanted at least 2.4: never a calm)");
            wx.ForceSpell(45f, 90f, false);
            yield return 3f;
            Line(wx.SpellsStarted == started && !wx.SpellActive && wx.Rain == 0f, $"with the option off no spell starts, not even a forced one: spells started {wx.SpellsStarted - started}, rain {wx.Rain:0.00}");
            OzWeather.Enabled = true;

            // ---- 6. a felled tree stands still at once, its stump is a still thing, nothing new is hooked
            {
                var W = g.World; ZObject pick = null; float room = -1f;
                foreach (var tr in W.TreeTiles)
                {   // the standing tree with the most room round it: the test blast must hurt nothing else
                    int x = tr.LocX >> 4, y = tr.LocY >> 4;
                    if (tr.Destroyed || tr.KillAt >= 0 || OzTreeFall.StateOf(x, y) != 0 || !GameObject.Find("TreeTile_" + x + "_" + y)) continue;
                    float near = float.MaxValue;
                    foreach (var o in W.Objects) { if (o == tr || o.Destroyed) continue; float ex = o.CenterX - tr.CenterX, ey = o.CenterY - tr.CenterY; near = Mathf.Min(near, ex * ex + ey * ey); }
                    if (near > room) { room = near; pick = tr; }
                }
                if (pick == null) Line(false, $"a felled tree: no standing tree of the rules with its drawn tree was found ({W.TreeTiles.Count} trees in the rules, {OzTreeFall.Bound} bound to a drawn tree)");
                else
                {
                    int x = pick.LocX >> 4, y = pick.LocY >> 4; var holder = GameObject.Find("TreeTile_" + x + "_" + y).transform;
                    Cam(holder.position.x, holder.position.z, 30f); yield return 20;
                    fw.Rescan();
                    int onBefore = WindMats(holder), plants0 = OzFloraWind.Plants, released0 = OzFloraWind.Released, unknown0 = OzFloraWind.Unknown, still0 = OzFloraWind.StillByTable;
                    g.paused = false;                                                                      // the rules run for the moment the blast needs
                    W.AddMissileForTest(new ZMissile { x = pick.CenterX, y = pick.CenterY, damage = (int)(W.S.grenade_damage * ZConst.MAX_UNIT_HEALTH), radius = W.S.grenade_damage_radius, team = Team.Neutral, grenade = true, explodeTime = W.Time + 0.3 });
                    float until = Time.realtimeSinceStartup + 8f;
                    while (OzTreeFall.StateOf(x, y) < 2 && Time.realtimeSinceStartup < until) yield return 1;
                    int state = OzTreeFall.StateOf(x, y), onTree = WindMats(holder);                       // in the frame the tree broke
                    var stump = GameObject.Find("TreeStump_" + x + "_" + y); int onStump = stump ? WindMats(stump.transform) : 0;
                    fw.Rescan();
                    int newPlants = OzFloraWind.Plants - plants0, released = OzFloraWind.Released - released0, newStill = OzFloraWind.StillByTable - still0;
                    until = Time.realtimeSinceStartup + 8f;                                                // it comes down (a paused game would hold the fall)
                    while (OzTreeFall.StateOf(x, y) == 2 && Time.realtimeSinceStartup < until) yield return 1;
                    yield return 20; Shot("J9_felled_tree_h30"); yield return 5;
                    g.paused = true;
                    Line(state >= 2 && onBefore > 0 && onTree == 0 && onStump == 0 && (stump || !OzTreeFall.HasStump(x, y)) && newPlants == 0 && OzFloraWind.Unknown == unknown0 && released >= onBefore,
                        $"a felled tree stands still at once: the tree of tile {x},{y} (nothing of the rules within {Mathf.Sqrt(room) / 16f:0.0} tiles) was felled by a test blast (state {state}, 2 = falling); plants on a wind material under its holder before {onBefore}, in the frame it broke {onTree} (wanted 0), on its stump {(stump ? onStump.ToString() : "no stump was made")} (wanted 0); a new search hooked {newPlants} new plants (wanted 0) and counted {newStill} more still thing(s) (the stump), unknown names {OzFloraWind.Unknown - unknown0}; released {released}. This tree stays felled: start the level again before anything else is judged in this match");
                }
            }

            Note(wx.Status());
            Note("pictures (full screen, normal game camera) in " + dir.Replace('\\', '/') + " : J9_calm_default_h70, J9_rain_default_h70, J9_calm_trees_h22, J9_rain_trees_h22, J9_calm_swamp_pool_h22, J9_rain_swamp_rings_h30, J9_rain_road_puddles_h22, J9_grass_dry_before_h22, J9_grass_wet_h22, J9_grass_dry_after_h22, J9_felled_tree_h30");
            Finish("level 16 weather");
        }

        /// <summary>The middle of the largest patch of one tile class near the map's middle (a pool, a road).</summary>
        static bool FindTile(ZClass cls, out Vector3 at)
        {
            at = Vector3.zero; var map = ZGame.I.World.Map; int W = map.width, H = map.height; float best = -1f;
            for (int ty = 4; ty < H - 4; ty++)
                for (int tx = 4; tx < W - 4; tx++)
                {
                    if (map.ClassAt(tx, ty) != cls) continue;
                    int n = 0; for (int dy = -3; dy <= 3; dy++) for (int dx = -3; dx <= 3; dx++) if (map.ClassAt(tx + dx, ty + dy) == cls) n++;
                    float score = n - 0.02f * (Mathf.Abs(tx - W / 2) + Mathf.Abs(ty - H / 2));
                    if (score > best) { best = score; at = new Vector3(tx * 2f + 1f, 0f, -(ty * 2f + 1f)); }
                }
            return best > 0f;
        }

        // ================================================================== the two other levels: nothing of this step is there
        static IEnumerator Other()
        {
            var g = ZGame.I; var at = Object.FindFirstObjectByType<Atmosphere>();
            yield return 30;
            Line(!OzJungleWeather.I && !OzFloraWind.I && !Object.FindFirstObjectByType<OzJungleWeather>() && !Object.FindFirstObjectByType<OzFloraWind>(), $"{g.level}: no level 16 weather object and no plant wind in the scene");
            Line(at && at.preset == (g.level == "L17" ? Atmosphere.Preset.City : Atmosphere.Preset.Desert), $"{g.level}: the air preset is {(at ? at.preset.ToString() : "none")} (as before)");
            Line(V("_OZFogShift") == Vector4.zero && V("_OZFloraWind") == Vector4.zero && V("_OzJungleWet") == Vector4.zero && !OzJungleAir.Live && OzJungleAir.RainWindK == 0f,
                $"{g.level}: the globals of this step are all zero (fog shift {V("_OZFogShift")}, plant wind {V("_OZFloraWind")}, jungle wet {V("_OzJungleWet")})");
            int plantMats = 0, rs = 0; var seenMats = new HashSet<Material>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                rs++;
                foreach (var m in r.sharedMaterials) if (m && seenMats.Add(m) && m.HasProperty("_OzWindPalm") && m.GetVector("_OzWindPalm").w < -1.5f) plantMats++;
            }
            Line(plantMats == 0, $"{g.level}: {plantMats} materials carry a plant kind among the {seenMats.Count} materials of {rs} renderers (the desert palms keep kind 1: {PalmWind.Status()})");
            bool storm = OzWeather.I; var fog = V("_FogWind"); float speed = new Vector2(fog.x, fog.z).magnitude;
            Line(g.level == "L17" ? storm : !storm, $"{g.level}: the city's night storm object is {(storm ? "there" : "not there")} (as before); the level wind is {speed:0.00} m/s (desert 3.35, city 1.34)");
            Line(g.level == "L17" ? Mathf.Abs(speed - 1.342f) < 0.01f : Mathf.Abs(speed - 3.354f) < 0.01f, $"{g.level}: the level wind is exactly its own ({fog.x:0.000}, {fog.z:0.000})");
            Finish(g.level + ", untouched by the level 16 weather");
        }

        // ================================================================== the match fingerprint
        const string FpMode = "oz_j9_fp_mode", FpOld = "oz_j9_fp_old";
        const double FpGameSeconds = 420.0;                                                                // the rules state is read at this game time (rule tick 42000)

        [MenuItem("OpenZ/Checks/Level 16 weather: match fingerprint 1 of 3, set up WEATHER ON (then press Play on level 16)")]
        static void FpOn() => FpSetup(1);
        [MenuItem("OpenZ/Checks/Level 16 weather: match fingerprint 2 of 3, set up WEATHER OFF (then press Play on level 16)")]
        static void FpOff() => FpSetup(2);
        [MenuItem("OpenZ/Checks/Level 16 weather: match fingerprint 3 of 3, compare and put the settings back")]
        public static void FpCompare()
        {
            FpRestore();
            string d = OzL16Weather.OutDir(), a = Path.Combine(d, "fingerprint_weather_on.txt"), b = Path.Combine(d, "fingerprint_weather_off.txt");
            if (!File.Exists(a) || !File.Exists(b)) { Debug.LogWarning(Tag + " run both set-ups first (each: the menu, then Play on level 16 until the log says the fingerprint is written)"); return; }
            string la = File.ReadAllLines(a)[0], lb = File.ReadAllLines(b)[0];
            bool same = la == lb;
            string rep = (same ? "PASS 1/1" : "FAIL 0/1") + " lines: computer match on level 16, 4x, the rules state at rule tick " + (long)(FpGameSeconds * 100) + "\n" + (same ? "PASS  " : "FAIL  ")
                         + "weather ON (a forced rain spell from game second 8) and weather OFF give " + (same ? "the same" : "DIFFERENT") + " rules state\n      on:  " + la + "\n      off: " + lb
                         + (same ? "" : "\n      if they differ: run set-up 1 twice; if two runs with the same setting differ as well, a match in the editor is not repeatable and this test cannot tell (the weather code reads the rules only: OzJungleWeather.cs, OzFloraWind.cs)") + "\n";
            File.WriteAllText(Path.Combine(d, "OzL16WeatherFingerprint.txt"), rep);
            if (same) Debug.Log(Tag + " " + rep); else Debug.LogError(Tag + " " + rep);
        }

        static void FpSetup(int mode)
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning(Tag + " leave Play mode first"); return; }
            if (!SessionState.GetBool(FpOld + "_kept", false))
            {
                SessionState.SetString(FpOld, PlayerPrefs.GetInt("oz_botmatch", 0) + "|" + PlayerPrefs.GetFloat("oz_demo_speed", 1f).ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + PlayerPrefs.GetInt(OzWeather.Pref, 1) + "|"
                                              + (PlayerPrefs.HasKey(OzWeather.SaltPref) ? PlayerPrefs.GetInt(OzWeather.SaltPref).ToString() : "none"));
                SessionState.SetBool(FpOld + "_kept", true);
            }
            PlayerPrefs.SetInt("oz_botmatch", 1); PlayerPrefs.SetFloat("oz_demo_speed", 4f); PlayerPrefs.SetInt(OzWeather.Pref, mode == 1 ? 1 : 0); PlayerPrefs.SetInt(OzWeather.SaltPref, 1616); PlayerPrefs.Save();
            SessionState.SetInt(FpMode, mode);
            Debug.Log(Tag + " fingerprint run set up: computer match at 4x, WEATHER " + (mode == 1 ? "ON" : "OFF") + ". Open the level 16 scene and press Play; leave it alone until the log says the fingerprint is written (about 2 minutes).");
        }

        static void FpRestore()
        {
            SessionState.SetInt(FpMode, 0);
            if (!SessionState.GetBool(FpOld + "_kept", false)) return;
            var p = SessionState.GetString(FpOld, "0|1|1|none").Split('|');
            PlayerPrefs.SetInt("oz_botmatch", int.Parse(p[0])); PlayerPrefs.SetFloat("oz_demo_speed", float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture)); PlayerPrefs.SetInt(OzWeather.Pref, int.Parse(p[2]));
            if (p[3] == "none") PlayerPrefs.DeleteKey(OzWeather.SaltPref); else PlayerPrefs.SetInt(OzWeather.SaltPref, int.Parse(p[3]));
            PlayerPrefs.Save(); SessionState.SetBool(FpOld + "_kept", false);
            Debug.Log(Tag + " settings put back (computer match " + p[0] + ", speed " + p[1] + ", WEATHER " + p[2] + ", weather salt " + p[3] + ")");
        }

        [InitializeOnLoadMethod]
        static void FpBoot() { EditorApplication.playModeStateChanged -= FpState; EditorApplication.playModeStateChanged += FpState; if (EditorApplication.isPlaying && SessionState.GetInt(FpMode, 0) != 0) { fpDone = false; fpForced = false; EditorApplication.update += FpTick; } }
        static void FpState(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetInt(FpMode, 0) != 0) { fpDone = false; fpForced = false; EditorApplication.update -= FpTick; EditorApplication.update += FpTick; }
            if (s == PlayModeStateChange.ExitingPlayMode) EditorApplication.update -= FpTick;
        }

        static bool fpDone, fpForced;
        static void FpTick()
        {
            if (!EditorApplication.isPlaying) { EditorApplication.update -= FpTick; return; }
            var g = ZGame.I; if (fpDone || !g || g.World == null || g.level != "L16") return;
            int mode = SessionState.GetInt(FpMode, 0); if (mode == 0) return;
            var W = g.World;
            if (!fpForced && W.Time >= 8.0) { fpForced = true; if (mode == 1 && OzJungleWeather.I) OzJungleWeather.I.ForceSpell(60f, 90f, true); }
            if (W.Time < FpGameSeconds - 1.0) return;
            // the last second tick by tick, so that both runs are read at exactly the same rule tick
            g.paused = true;
            long want = (long)System.Math.Round(FpGameSeconds * 100.0); int guard = 0;
            while ((long)System.Math.Round(W.Time * 100.0) < want && guard++ < 400) W.Tick(ZConst.TICK);
            fpDone = true;
            ulong h = 14695981039346656037UL; int alive = 0, red = 0, blue = 0; long health = 0;
            void Mix(long v) { unchecked { for (int i = 0; i < 8; i++) { h ^= (byte)(v >> (i * 8)); h *= 1099511628211UL; } } }
            foreach (var o in W.Objects)
            {
                Mix(o.Id); Mix((int)o.Kind); Mix((int)o.Owner); Mix(o.LocX); Mix(o.LocY); Mix(o.Health); Mix(o.Destroyed ? 1 : 0);
                if (o.Type != null) foreach (char c in o.Type) Mix(c);
                if (!o.Destroyed && (o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle)) { alive++; if (o.Owner == Team.Red) red++; else if (o.Owner == Team.Blue) blue++; health += o.Health; }
            }
            string line = "rules state " + h.ToString("X16") + " at rule tick " + (long)System.Math.Round(W.Time * 100.0) + ": " + W.Objects.Count + " objects, " + alive + " robots and vehicles alive (red " + red + ", blue " + blue + "), their health summed " + health;
            var wx = OzJungleWeather.I;
            string file = Path.Combine(OzL16Weather.OutDir(), mode == 1 ? "fingerprint_weather_on.txt" : "fingerprint_weather_off.txt");
            File.WriteAllText(file, line + "\nweather option " + (OzWeather.Enabled ? "ON" : "OFF") + ", spells started " + (wx ? wx.SpellsStarted : 0) + ", ground splashes " + (wx ? wx.SplashesEmitted : 0) + ", game speed " + g.gameSpeed + ", both sides the computer: " + (g.redBot && g.blueBot) + "\n");
            Debug.Log(Tag + " fingerprint written (" + (mode == 1 ? "weather ON" : "weather OFF") + "): " + line + " -> " + file.Replace('\\', '/') + " . Leave Play mode now.");
        }
    }
}
