using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The readable landing mark's Play proof (rule DV118, OzShuttleShadow.Visible.cs), on level 16 in Play (menu OpenZ/V1.1/Shuttle:
    /// Mark Visible Proof, Editor/OzShuttleMarkVisibleMenu.cs). Test only.
    /// <list type="bullet">
    /// <item>Four stretches of ground are found on the level by the drawn ground's own classes (TrackMarks.Surface): open GRASS, a ROAD,
    /// loose SAND / dirt and WATER, each 12 m long with that ground all round, away from forts.</item>
    /// <item>By day (clock 0.10) and at night (0.68) the ship flies each stretch at cruise from a run-up, the normal RTS camera at 70 m,
    /// once NOT SELECTED and once SELECTED. Over the stretch's middle: a picture with the mark and one without it the next frame; the
    /// mark's darkening = 1 - the mean luminance with / without inside the disc of 0.45 of its radius (the K256g2 proof's measure).</item>
    /// <item>PASS lines: not selected by day 14 .. 27 % darker; selected by day 18 .. 32 % and at least 2 % darker than not selected on
    /// the same stretch; at night 14 .. 40 % (dark ground: the same share reads as more); the red "cannot land" never lighter than the
    /// dark mark (the strengths); NEVER GONE: every frame in the air (the ground share under 0.5, the proof's own hidden frames left
    /// out) the mark is shown with a strength over 0.1.</item>
    /// </list>
    /// The ship cannot be shot down while it flies (its hit points raised and filled up, the other teams hold their fire: all given back
    /// at the end). Output: <see cref="Folder"/> (mark_visible_log.txt and one full-screen 70 m picture per run).
    /// </summary>
    [DefaultExecutionOrder(32100)]                                                                       // after the ships and their marks: the never-gone count reads this frame's mark
    public sealed partial class OzShuttleMarkVisibleProof : MonoBehaviour                                     // K419b: + View/OzShuttleMarkVisibleProof.K419.cs (the stretches)
    {
        public static string Report = "idle";
        /// <summary>The limits (darkening share of the ground's luminance).</summary>
        public static float DayLo = 0.14f, DayHi = 0.27f, SelLo = 0.18f, SelHi = 0.32f, SelMore = 0.02f, NightLo = 0.14f, NightHi = 0.40f, ShownMin = 0.1f;

        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "shuttle_mark_visible_k401");
            return System.IO.Path.Combine(Application.persistentDataPath, "shuttle_mark_visible_k401");
        }

        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzShuttleMarkVisibleProof").AddComponent<OzShuttleMarkVisibleProof>();
        }

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; DayNight dn; Team me = Team.Red; ZObject s; OzPlayableShuttle ship;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes, frames; string dir; Color32[] grab; int gw, gh;
        bool hiding, watch; int airFrames, goneFrames, guardMax0 = -1; float holdT, clock0 = -1f; bool running0;
        readonly Dictionary<ZObject, int> heldFire = new Dictionary<ZObject, int>(); readonly List<ZObject> sel0 = new List<ZObject>();
        readonly Dictionary<string, float> unselDark = new Dictionary<string, float>();

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleMarkVisibleProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); frames++; Log($"frame {name}.png"); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Cam(Vector3 focus, float h) { if (!rts) return; rts.FreeLookOff(); rts.focus = new Vector3(focus.x, 0f, focus.z); rts.SetHeightImmediate(h); }
        Camera CamNow => rts ? rts.GetComponent<Camera>() : Camera.main;
        float PxPerM(Vector3 at) { var c = CamNow; if (!c) return 0f; var a = c.WorldToScreenPoint(at); var b = c.WorldToScreenPoint(at + c.transform.right); return new Vector2(b.x - a.x, b.y - a.y).magnitude; }
        static int Px(float x) => Mathf.Clamp(Mathf.FloorToInt(x * 8f / 16f), 1, 400) * 16 + 8;
        bool MoveTo(Vector2 w) => W.SendWaypoints(me, s, new[] { new ZWaypoint { mode = WpMode.Move, x = Px(w.x), y = Px(-w.y), playerGiven = true } });
        IEnumerator Grab() { yield return new WaitForEndOfFrame(); var tex = ScreenCapture.CaptureScreenshotAsTexture(); grab = tex.GetPixels32(); gw = tex.width; gh = tex.height; Destroy(tex); }
        static float Luma(Color32 c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
        static float MeanLuma(Color32[] px, int w, int h, Vector2 c, float r)
        {
            double sum = 0; int n = 0; int x0 = Mathf.Max(0, (int)(c.x - r)), x1 = Mathf.Min(w - 1, (int)(c.x + r)), y0 = Mathf.Max(0, (int)(c.y - r)), y1 = Mathf.Min(h - 1, (int)(c.y + r));
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) if ((x - c.x) * (x - c.x) + (y - c.y) * (y - c.y) <= r * r) { sum += Luma(px[y * w + x]); n++; }
            return n > 0 ? (float)(sum / n) : 0f;
        }

        // ------------------------------------------------------------------------------------------- the ground stretches
        /// <summary>A ground kind at (x, z) from the drawn ground's classes: 0 grass, 1 road, 2 sand / dirt, 3 water, -1 mixed.</summary>
        static int KindAt(TrackSurface sf, float x, float z)
        {
            sf.Classes(x, z, out float road, out float water, out float rock);
            if (water > 0.85f) return 3;
            if (water > 0.1f || rock > 0.15f) return -1;
            if (road > 0.85f) return 1;
            if (road > 0.1f) return -1;
            float dust = sf.DustStrength(x, z);
            if (dust > 0.6f) return 2;
            if (dust < 0.25f) return 0;
            return -1;
        }

        static readonly Vector3 FarAway = new Vector3(1e6f, 0f, 1e6f);
        /// <summary>The middle of a 12 m (along x) by 5 m stretch of one kind, nearest the level's middle, away from forts; false: none.</summary>
        static bool FindStretch(TrackSurface sf, int kind, out Vector2 mid)
        {
            mid = default; float best = float.MaxValue; var c0 = new Vector2(112f, -100f);
            for (float z = -170f; z <= -28f; z += 2f)
                for (float x = 26f; x <= 196f; x += 2f)
                {
                    bool ok = true;
                    for (float dz = -2.5f; dz <= 2.5f && ok; dz += 2.5f)
                        for (float dx = -6f; dx <= 6f && ok; dx += 1.5f)
                            if (KindAt(sf, x + dx, z + dz) != kind || FortGunView.FortTopAt(new Vector3(x + dx, 0f, z + dz), FarAway) > float.MinValue) ok = false;
                    if (!ok) continue;
                    float d = (new Vector2(x, z) - c0).sqrMagnitude;
                    if (d < best) { best = d; mid = new Vector2(x, z); }
                }
            return best < float.MaxValue;
        }

        // ------------------------------------------------------------------------------------------- the run
        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || input == null) { Log("no game (level 16 in Play)"); fails++; Finish(); yield break; }
            if (!ZWorld.ShuttlePlayableDeviation || W.PlayableShuttle == null) { Log("no playable shuttle (level 16, the shuttle rule on)"); fails++; Finish(); yield break; }
            me = g.player; s = W.PlayableShuttle; ship = OzPlayableShuttle.Of(s);
            if (!ship) { Log("the shuttle has no view"); fails++; Finish(); yield break; }
            var sf = TrackMarks.I ? TrackMarks.I.Surface : null;
            if (sf == null) { Log("no drawn ground classes (TrackMarks.Surface)"); fails++; Finish(); yield break; }
            sel0.AddRange(input.Selection);
            int bots = W.Bots.Count; W.Bots.Clear();
            dn = FindFirstObjectByType<DayNight>(); if (dn) { clock0 = dn.timeOfDay; running0 = dn.running; dn.running = false; }
            Guard();
            Log($"K401 mark visible proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}; CPU removed {bots}; strengths: day {OzShuttleShadow.MarkDay:F3}, near {OzShuttleShadow.MarkNear:F3}, fast x{OzShuttleShadow.MarkFastK:F2}, selected +{OzShuttleShadow.SelectedAdd:F3}, night x{OzShuttleShadow.MarkNightK:F2}");
            string[] names = { "grass", "road", "sand", "water" };
            var mids = new Vector2?[4];
            for (int k = 0; k < 4; k++)
            {
                if (FindStretchK419(sf, k, out var m)) { mids[k] = m; Log($"stretch {names[k]}: middle ({m.x:F0}, {m.y:F0}); {sf.Describe(m.x, m.y)}"); }
                else Log($"stretch {names[k]}: none found on this level");
            }
            Check("V0 a stretch of grass, road and water found (sand where the level has loose sand)", mids[0].HasValue && mids[1].HasValue && mids[3].HasValue);
            watch = true;
            foreach (var night in new[] { false, true })
            {
                float clock = night ? 0.68f : 0.10f; if (dn) { dn.timeOfDay = clock; dn.Apply(clock); }
                Log($"--- {(night ? "NIGHT" : "DAY")} (clock {clock:F2}, night share {NightGrade.NightK:F2})");
                for (int k = 0; k < 4; k++)
                {
                    if (!mids[k].HasValue) continue;
                    foreach (var selected in new[] { false, true })
                        yield return Pass(names[k], mids[k].Value, selected, night);
                }
            }
            watch = false;
            Check($"V9 never gone in the air: shown with a strength over {ShownMin:F1} on every frame in the air ({goneFrames} of {airFrames} frames without)", airFrames > 300 && goneFrames == 0);
            Finish();
        }

        IEnumerator Pass(string name, Vector2 mid, bool selected, bool night)
        {
            input.Selection.Clear(); if (selected) input.Selection.Add(s);
            var hd = StretchDir(mid); var a = mid - hd * 20f; var b = mid + hd * 20f; var run = a - hd * 15f; var over = b + hd * 10f;   // K419b: along the stretch's heading
            MoveTo(run);
            yield return WaitFor(() => s.ShuttleAirborne && new Vector2(ship.transform.position.x - run.x, ship.transform.position.z - run.y).magnitude < 3f, 60f);
            yield return Wait(1.0f);
            Cam(new Vector3(mid.x, 0f, mid.y), 70f);
            MoveTo(over);
            float t = 0f; bool done = false; float dk = -1f, str = 0f;
            while (t < 40f && !done)
            {
                var p = ship.transform.position;
                if (Vector2.Dot(new Vector2(p.x - mid.x, p.z - mid.y), StretchDir(mid)) >= 0f)                       // K419b: past the middle along the heading
                {
                    var sh = ship.Shadow; var c = CamNow;
                    if (sh != null && c)
                    {
                        yield return Grab(); var A = grab; int w = gw, h = gh;
                        var cen = c.WorldToScreenPoint(new Vector3(sh.Centre.x, sh.RefY, sh.Centre.z)); float r = 0.45f * OzShuttleShadow.AlongM * sh.GrowNow * PxPerM(sh.Centre);
                        str = sh.StrengthNow;
                        Shot($"{(night ? "night" : "day")}_{name}_{(selected ? "selected" : "not_selected")}_h70");
                        hiding = true; OzShuttleShadow.ProofHide = true; yield return null; yield return Grab(); var B = grab; OzShuttleShadow.ProofHide = false;
                        yield return null; hiding = false;                                                       // the hidden frame's own Step is left out of the never-gone count
                        float la = MeanLuma(A, w, h, cen, r), lb = MeanLuma(B, w, h, cen, r); dk = lb > 1f ? 1f - la / lb : 0f;
                        Log($"{(night ? "night" : "day")} {name} {(selected ? "SELECTED" : "not selected")}: feet {ship.FeetHeight:F1} m, strength {str:F3}, selected share {sh.SelectedNow:F2}, plane {sh.PlaneKind}, ground luma {lb:F1} -> {la:F1}: {dk * 100f:F1} % darker (disc {r:F0} px)");
                    }
                    done = true;
                }
                t += Time.deltaTime; yield return null;
            }
            string tag = $"{(night ? "night" : "day")} {name} {(selected ? "selected" : "not selected")}";
            if (!done || dk < 0f) { Check($"V {tag}: measured over the stretch", false); yield break; }
            string key = (night ? "n" : "d") + name;
            if (!selected) unselDark[key] = dk;
            if (night) Check($"V {tag}: {NightLo * 100f:F0} .. {NightHi * 100f:F0} % darker at night ({dk * 100f:F1})", dk >= NightLo && dk <= NightHi);
            else if (!selected) Check($"V {tag}: {DayLo * 100f:F0} .. {DayHi * 100f:F0} % darker ({dk * 100f:F1})", dk >= DayLo && dk <= DayHi);
            else
            {
                float u = unselDark.TryGetValue(key, out var v) ? v : -1f;
                Check($"V {tag}: {SelLo * 100f:F0} .. {SelHi * 100f:F0} % darker and at least {SelMore * 100f:F0} % more than not selected ({dk * 100f:F1} vs {u * 100f:F1})", dk >= SelLo && dk <= SelHi && u >= 0f && dk >= u + SelMore);
            }
        }

        void LateUpdate()
        {
            if (s != null && !s.Destroyed && guardMax0 > 0 && s.Health < s.MaxHealth) s.SetHealth(s.MaxHealth);
            if (heldFire.Count > 0 && (holdT -= Time.unscaledDeltaTime) <= 0f) { holdT = 1f; HoldFire(); }
            if (!watch || hiding || !ship || ship.Shadow == null || s == null || !s.ShuttleAirborne || ship.GroundShare >= 0.5f || OzShuttleShadow.ProofHide) return;
            airFrames++;
            if (!ship.Shadow.Shown || ship.Shadow.StrengthNow <= ShownMin) { goneFrames++; if (goneFrames <= 3) Log($"   gone in the air: shown {ship.Shadow.Shown}, strength {ship.Shadow.StrengthNow:F3}, decal {ship.Shadow.DecalOn}"); }
        }

        // ------------------------------------------------------------------------------------------- the guard (test only)
        void Guard()
        {
            if (s == null || s.Destroyed) return;
            if (guardMax0 < 0) { guardMax0 = s.MaxHealth; s.MaxHealth = 100000000; s.SetHealth(s.MaxHealth); }
            HoldFire();
        }
        void HoldFire()
        {
            if (W == null) return;
            foreach (var o in W.Objects)
            {
                if (o == null || o.Destroyed || o.Owner == me || o.Owner == Team.Neutral || o.AttackRadius <= 0 || heldFire.ContainsKey(o)) continue;
                heldFire[o] = o.AttackRadius; o.AttackRadius = 0;
            }
        }
        void Release()
        {
            if (guardMax0 > 0 && s != null && !s.Destroyed) { s.MaxHealth = guardMax0; s.SetHealth(guardMax0); }
            guardMax0 = -1;
            foreach (var kv in heldFire) if (kv.Key != null && kv.Key.AttackRadius == 0) kv.Key.AttackRadius = kv.Value;
            heldFire.Clear();
            if (dn && clock0 >= 0f) { dn.timeOfDay = clock0; dn.Apply(clock0); dn.running = running0; clock0 = -1f; }
            if (input) { input.Selection.Clear(); foreach (var o in sel0) if (o != null && !o.Destroyed) input.Selection.Add(o); }
            OzShuttleShadow.ProofHide = false;
        }
        void OnDestroy() { Release(); }

        void Finish()
        {
            Log($"K401 mark visible proof: {passes} PASS, {fails} FAIL, {frames} pictures");
            Report = fails == 0 ? $"PASS {passes}" : $"FAIL {fails} of {passes + fails}";
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "mark_visible_log.txt"), sb.ToString());
            Destroy(gameObject);
        }
    }
}
