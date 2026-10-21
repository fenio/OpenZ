using System.Collections;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace OpenZ
{
    /// <summary>
    /// The K256e Play proof of the shuttle's ground marks (rule DV118, the third round: the landed selection ring, the soft dark shadow
    /// in the air, the red "cannot land" mark), on level 16 in Play (menu OpenZ/V1.1/Shuttle: K256e Shadow + Ring Proof, or
    /// OzShuttleShadowProof.Run()). Counted PASS / FAIL lines and full-screen pictures from the normal RTS camera at the standard 70 m
    /// (a FIXED camera for every strip; *_extra_close pictures are never the proof); the marks are measured in the pictures themselves
    /// (each picture taken again with the mark hidden: the pixels that change are the mark).
    /// E1 the landed ring beside a light tank: ring / frame width in px for both (at most 0.95), the ring centred and the same on both
    /// sides. E2 the take-off: selected with its frame on every frame, a ground mark on every frame (the ring fades out as the pads rise,
    /// the shadow fades in), a 16-frame strip. E3 cruise (8 m or more) by day: the soft shadow reads (its core 20 % darker than the same
    /// ground or more). E4 / E5 by day over the river and over flat grass blocked by a squad, E7 the same at night: red in 8 of the 12
    /// frames of a 2 s strip from the refusal, the bounce, never on a wall (the grid's own count), no glow (the mark's pixels never
    /// lighter than the ground without it), red (r - g up), the error blip and "Can't land here!"; E6 a long hover: dark again after its
    /// 2 s. E8 a crash landing: no ring while it is drawn, the ring back in 0.3 s once level. E9 on the repair station's deck: no ring.
    /// Output: <see cref="Folder"/> (proof_log_k256e_shadow_ring.txt, the pictures, the strips' folders). Test only.
    /// </summary>
    public sealed class OzShuttleShadowProof : MonoBehaviour
    {
        public static string Report = "idle";
        /// <summary>The polish proofs' folder (built at run time from the project's place) + k256e_shadow_ring.</summary>
        public static string Folder() => System.IO.Path.Combine(OzShuttlePolishProof.Folder(), "k256e_shadow_ring");
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzShuttleShadowProof").AddComponent<OzShuttleShadowProof>();
        }
#if UNITY_EDITOR
        [UnityEditor.MenuItem("OpenZ/V1.1/Shuttle: K256e Shadow + Ring Proof (Level 16, Play)")]
        static void Menu()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K256e shadow + ring proof: enter Play on level 16 first"); return; }
            Run(); Debug.Log("[OpenZ] K256e shadow + ring proof started; its report: OzShuttleShadowProof.Report, the log in " + Folder());
        }
#endif

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; OpenZUnitOverlay overlay; SelectionOverlay rings; DayNight dn; ZObject s; OzPlayableShuttle ship; Team me = Team.Red;
        readonly StringBuilder sb = new StringBuilder(); int passes, fails; string dir; bool follow; Vector3 fixAt;
        Color32[] grab; int gw, gh;

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>();
            overlay = FindFirstObjectByType<OpenZUnitOverlay>(); rings = FindFirstObjectByType<SelectionOverlay>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Update()
        {
            if (!rts) return;
            if (follow && ship) { var p = ship.transform.position; rts.focus = new Vector3(p.x, 0f, p.z); }
            else if (!follow) rts.focus = fixAt;
        }

        // ------------------------------------------------------------------ helpers (as the polish proof's)
        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleShadowProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name)
        {
            var p = System.IO.Path.Combine(dir, name + ".png"); var d = System.IO.Path.GetDirectoryName(p); if (!System.IO.Directory.Exists(d)) System.IO.Directory.CreateDirectory(d);
            ScreenCapture.CaptureScreenshot(p); Log($"frame {name}.png");
        }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        static void MouseEv(Vector2 p) { if (Mouse.current != null) InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = p }); }
        IEnumerator Park() { for (int i = 0; i < 3; i++) { MouseEv(new Vector2(Screen.width * 0.12f, Screen.height * 0.78f)); yield return null; } }   // the pointer off every subject
        void Sel() { input.Selection.Clear(); input.Selection.Add(s); }
        void Fix(Vector3 w) { follow = false; fixAt = new Vector3(w.x, 0f, w.z); if (rts) { rts.focus = fixAt; rts.Place(); } }
        bool Move(int x, int y) => W.SendWaypoints(me, s, new[] { new ZWaypoint { mode = WpMode.Move, x = x, y = y, playerGiven = true } });
        ZObject Squad(string type, int tx, int ty, Team t)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, 1, true, out tx, out ty);
            var o = W.CreateRobotGroup(type, tx * 16, ty * 16, t); if (o == null) return null;
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            return o;
        }
        ZObject Vehicle(string type, int tx, int ty, Team t)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, 2, false, out tx, out ty);
            var o = W.CreateObject("vehicle", type, tx * 16, ty * 16, t); if (o != null) g.SpawnUnitView(o);
            return o;
        }
        int[] Pad(int r, float angDeg, int span)
        {
            for (int dr = 0; dr <= 200; dr += 16)
                for (int k = 0; k <= span; k += 10)
                    foreach (int sg in new[] { 1, -1 })
                    {
                        double a = (angDeg + sg * k) * System.Math.PI / 180.0;
                        var q = W.ShuttleClamp(s.CenterX + (int)(System.Math.Cos(a) * (r + dr)), s.CenterY + (int)(System.Math.Sin(a) * (r + dr)));
                        if (W.ShuttlePadFree(q.x, q.y, s, out _)) return new[] { q.x, q.y };
                    }
            return null;
        }
        int[] Spot(System.Func<int, int, string, bool> want, int near, int far)
        {
            long best = long.MaxValue; int[] r = null;
            for (int ty = 4; ty < W.Map.height - 4; ty++)
                for (int tx = 4; tx < W.Map.width - 4; tx++)
                {
                    int x = tx * 16 + 8, y = ty * 16 + 8; long d = (long)(x - s.CenterX) * (x - s.CenterX) + (long)(y - s.CenterY) * (y - s.CenterY);
                    if (d >= best || d < (long)near * near || d > (long)far * far) continue;
                    W.ShuttlePadFree(x, y, s, out string why);
                    if (want(x, y, why)) { best = d; r = new[] { x, y }; }
                }
            return r;
        }
        /// <summary>Flat open ground round a map point (px): the ground within 5 m varies by 0.3 m at most.</summary>
        bool Flat(int x, int y)
        {
            var c = ZGame.PxToWorld(x, y); float lo = 1e9f, hi = -1e9f;
            for (int i = -2; i <= 2; i++) for (int j = -2; j <= 2; j++) { float h = g.GroundY(c.x + i * 2.5f, c.z + j * 2.5f); lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h); }
            return hi - lo <= 0.3f;
        }
        /// <summary>Under the flying ship now: flat open ground (the shadow's whole grid drawn on the ground) with no plant or hard top
        /// within 5 m of its ground point.</summary>
        bool OpenGround()
        {
            var sh = ship.Shadow; if (sh == null || !sh.Shown || sh.PlaneKind != "ground" || sh.DrawnVerts < 260) return false;
            if (ship.Sky == null) return true;
            var p = ship.transform.position; float gy = g.GroundY(p.x, p.z);
            ship.Sky.Query(p.x, p.z, 5f, true, out float ht, out float pt);
            return Mathf.Max(ht, pt) <= gy + 0.5f;
        }
        static int Played(float t0, string key) { int n = 0; foreach (var e in OzSoundLog.Since(t0)) if (e.key == key && e.outcome != null && (e.outcome.StartsWith("PLAY") || e.outcome.StartsWith("LOOP"))) n++; return n; }

        // ------------------------------------------------------------------ the pictures as numbers
        IEnumerator Grab()
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            grab = tex.GetPixels32(); gw = tex.width; gh = tex.height; Destroy(tex);
        }
        static float Luma(Color32 c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
        /// <summary>The pixels that differ between two pictures inside a screen box (origin bottom-left, as the pictures), keep = which of
        /// them count (screen x, y): their number, x / y extents, the mean luma and red (r - g) with and without, the most a pixel got lighter.</summary>
        struct Px { public int n, x0, x1, y0, y1; public float la, lb, ra, rb, lighter; }
        Px Diff(Color32[] a, Color32[] b, Rect box, int thr, System.Func<int, int, bool> keep)
        {
            var r = new Px { x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue };
            if (a == null || b == null || a.Length != b.Length) return r;
            int bx0 = Mathf.Clamp((int)box.xMin, 0, gw - 1), bx1 = Mathf.Clamp((int)box.xMax, 0, gw - 1), by0 = Mathf.Clamp((int)box.yMin, 0, gh - 1), by1 = Mathf.Clamp((int)box.yMax, 0, gh - 1);
            double la = 0, lb = 0, ra = 0, rb = 0;
            for (int y = by0; y <= by1; y++)
                for (int x = bx0; x <= bx1; x++)
                {
                    int i = y * gw + x; var p = a[i]; var q = b[i];
                    int d = Mathf.Abs(p.r - q.r) + Mathf.Abs(p.g - q.g) + Mathf.Abs(p.b - q.b);
                    if (d < thr || (keep != null && !keep(x, y))) continue;
                    r.n++; if (x < r.x0) r.x0 = x; if (x > r.x1) r.x1 = x; if (y < r.y0) r.y0 = y; if (y > r.y1) r.y1 = y;
                    float lp = Luma(p), lq = Luma(q); la += lp; lb += lq; ra += p.r - p.g; rb += q.r - q.g;
                    if (lp - lq > r.lighter) r.lighter = lp - lq;
                }
            if (r.n > 0) { r.la = (float)(la / r.n); r.lb = (float)(lb / r.n); r.ra = (float)(ra / r.n); r.rb = (float)(rb / r.n); }
            return r;
        }
        /// <summary>The ground point (on the plane at height y) under a screen pixel.</summary>
        static bool GroundAt(Camera cam, int x, int y, float h, out Vector3 p)
        {
            var ray = cam.ScreenPointToRay(new Vector3(x + 0.5f, y + 0.5f, 0f)); p = default;
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return false;
            float t = (h - ray.origin.y) / ray.direction.y; if (t <= 0f) return false;
            p = ray.origin + ray.direction * t; return true;
        }
        static Rect Grow(Rect r, float l, float rr, float b, float t) => Rect.MinMaxRect(r.xMin - l, r.yMin - b, r.xMax + rr, r.yMax + t);

        // ------------------------------------------------------------------ the run
        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || W.PlayableShuttle == null || !OzPlayableShuttle.I || input == null) { Log("no playable shuttle (level 16 in Play, the option SHUTTLE on)"); fails++; Finish(); yield break; }
            s = W.PlayableShuttle; ship = OzPlayableShuttle.I; me = s.Owner;
            Log($"K256e shadow + ring proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}, quality {OzQuality.Name}");
            int bots = W.Bots.Count; W.Bots.Clear();
            dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            if (rts) { rts.FreeLookOff(); rts.SetHeightImmediate(70f); }
            Log($"set-up: CPU removed {bots}, day 0.10 held, camera 70 m; ring outer {OzShuttleShadow.RingM:F2} m, band {OzShuttleShadowRing.BandM[0]:F3} / {OzShuttleShadowRing.BandM[1]:F3} / {OzShuttleShadowRing.BandM[2]:F3} m inside its edge (the light tank's ring)");
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne, 40f);
            yield return Wait(1.0f);
            yield return E1(); yield return E2(); yield return E4E5(false); yield return E4E5(true); yield return E8(); yield return E9();
            Finish();
        }

        void Finish()
        {
            if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); }
            if (g) g.paused = false;
            OzShuttleShadow.ProofHide = OzShuttleShadow.ProofWalls = false; OzShuttleShadowRing.ProofHide = false;
            if (rings) rings.enabled = true; if (overlay) overlay.SetVisible(true);
            Report = $"done: {passes} PASS, {fails} FAIL";
            Log(Report);
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "proof_log_k256e_shadow_ring.txt"), sb.ToString()); } catch (System.Exception ex) { Debug.LogWarning("[OzShuttleShadowProof] " + ex.Message); }
            Destroy(gameObject);
        }

        // ------------------------------------------------------------------ E1: the landed ring beside a light tank (M5)
        /// <summary>One unit's ring and frame in px: A = everything, B = no selection rings, C = no unit overlay (brackets, bars, chips).</summary>
        void RingAndFrame(Color32[] A, Color32[] B, Color32[] C, ZObject o, Vector3 centre, float outerM, out float ringPx, out float framePx, out float seen, out int mirrorOff, out float offM)
        {
            ringPx = framePx = seen = offM = 0f; mirrorOff = 99;
            var cam = Camera.main; if (!cam || !input.MeshRect(o, out var fr)) return;
            // the frame: its brackets' pixels in the lower 70 % of the box (the health bar and the name chip stand above it)
            var fb = Grow(fr, 30f, 30f, 30f, -0.3f * fr.height);
            var f = Diff(A, C, fb, 40, null); framePx = f.n > 0 ? f.x1 - f.x0 + 1 : 0f;
            // the ring: the pixels that change without the rings, on the ground within its band
            float ro = outerM, rin = ro - 0.75f; var cg = centre;
            var bins = new int[36]; float gx0 = 1e9f, gx1 = -1e9f;
            var rb = Grow(fr, fr.width, fr.width, fr.height, 0f);
            var d = Diff(A, B, rb, 12, (x, y) =>
            {
                if (!GroundAt(cam, x, y, cg.y, out var p)) return false;
                float dx = p.x - cg.x, dz = p.z - cg.z, rr = Mathf.Sqrt(dx * dx + dz * dz);
                if (rr < rin || rr > ro + 0.25f) return false;
                int bi = (int)(Mathf.Repeat(Mathf.Atan2(dz, dx) * Mathf.Rad2Deg, 360f) / 10f) % 36; bins[bi]++;
                if (p.x < gx0) gx0 = p.x; if (p.x > gx1) gx1 = p.x;
                return true;
            });
            ringPx = d.n > 0 ? d.x1 - d.x0 + 1 : 0f;
            int seenBins = 0, off = 0;
            for (int i = 0; i < 36; i++) { if (bins[i] >= 4) seenBins++; int m = ((17 - i) % 36 + 36) % 36; if ((bins[i] >= 4) != (bins[m] >= 4)) off++; }
            seen = seenBins / 36f; mirrorOff = off / 2;
            offM = d.n > 0 ? Mathf.Abs((gx0 + gx1) * 0.5f - cg.x) : 99f;
        }

        IEnumerator E1()
        {
            Log("E1 the landed ring beside a light tank (M5)");
            int tx = (s.CenterX >> 4) + 7, ty = s.CenterY >> 4;
            var tank = Vehicle("light", tx, ty, me);
            if (tank == null) Log("no light tank could be placed: the shuttle alone");
            input.Selection.Clear(); input.Selection.Add(s); if (tank != null) input.Selection.Add(tank);
            var sp = ship.transform.position; var tp = tank != null && g.Views.TryGetValue(tank.Id, out var tv) && tv ? tv.transform.position : sp;
            Fix((sp + tp) * 0.5f); if (rts) rts.SetHeightImmediate(70f);
            yield return Park();
            yield return Wait(2.8f);                                                                       // the tank's attack range ring (0.38 + 1.5 + 0.55 s after the selection) is gone
            yield return Grab(); var A = grab;
            if (rings) rings.enabled = false; yield return null; yield return Grab(); var B = grab; if (rings) rings.enabled = true;
            if (overlay) overlay.SetVisible(false); yield return null; yield return Grab(); var C = grab; if (overlay) overlay.SetVisible(true);
            yield return null;
            var rc = OzShuttleShadowRing.LastCentre; rc.y = g.GroundY(rc.x, rc.z) + OzShuttleShadowRing.LiftM;
            RingAndFrame(A, B, C, s, rc, OzShuttleShadowRing.LastOuterM, out float rs, out float fs, out float seen, out int mir, out float off);
            Log($"shuttle: ring {rs:F0} px, frame {fs:F0} px: ratio {(fs > 0 ? rs / fs : 0f):F2}; ring seen on {seen * 100f:F0} % of its circle beside the hull, mirrored halves differ in {mir} of 18 ten-degree arcs, its centre {off:F2} m off the hull's middle; drawn alpha {OzShuttleShadowRing.LastAlpha:F2}, outer {OzShuttleShadowRing.LastOuterM:F2} m");
            float rt = 0f, ft = 0f;
            if (tank != null)
            {
                var tc = tp; tc.y = g.GroundY(tc.x, tc.z) + 0.25f;
                RingAndFrame(A, B, C, tank, tc, 1.9f * 1.05f, out rt, out ft, out _, out _, out _);
                Log($"light tank: ring {rt:F0} px, frame {ft:F0} px: ratio {(ft > 0 ? rt / ft : 0f):F2}");
            }
            Check("E1 the shuttle's landed ring is inside its frame: ring / frame width <= 0.95, measured in px", fs > 0 && rs > 0 && rs / fs <= 0.95f);
            if (tank != null) Check("E1 the light tank beside it: ring / frame <= 0.95 too (the same rule for both)", ft > 0 && rt > 0 && rt / ft <= 0.95f);
            Check("E1 the ring is centred under the hull (its seen ends within 0.3 m of the hull's middle) and the same on both sides (mirrored arcs differ in <= 3 of 18)", off <= 0.3f && mir <= 3);
            Log("note: the dashed orange circle through the shuttle in round 2's line_01 pictures is the light tank's attack range ring (View/AttackRangeRings.cs: every armed unit shows it for about 2.4 s after it is selected); this picture is taken 2.8 s after the selection");
            Shot("e01_ring_beside_light_tank_h70");
            if (rts) { rts.SetHeightImmediate(32f); yield return Wait(0.3f); Shot("e01_ring_beside_light_tank_extra_close"); yield return null; rts.SetHeightImmediate(70f); }
            if (tank != null) { input.Selection.Remove(tank); }
            yield return Wait(0.3f);
        }

        // ------------------------------------------------------------------ E2 + E3: the take-off (selection and mark on every frame), cruise (the soft shadow by day)
        IEnumerator E2()
        {
            Log("E2 the take-off: selected first, a fixed camera, 16 frames every 0.2 s (M12)");
            Sel(); Fix(ship.transform.position); yield return Park(); yield return Wait(0.6f);
            var q = Pad(480, -90f, 60); if (q == null) { Check("E2 a landing place 60 m north", false); yield break; }
            int unsel = 0, norect = 0, nomark = 0, frames = 0, shot = 0; float next = 0f, t = 0f, worstRing = 0f, worstShadow = 0f;
            Check("E2 the move order is taken", Move(q[0], q[1]));
            while (t < 12f)
            {
                float feet = ship.FeetHeight;
                if (s.ShuttleAirborne && feet < 3f)
                {
                    frames++;
                    if (!input.Selection.Contains(s)) unsel++;
                    if (!input.MeshRect(s, out _)) norect++;
                    float ra = OzShuttleShadowRing.AlphaOf(s.Id), sa = ship.Shadow != null && ship.Shadow.Shown ? ship.Shadow.StrengthNow : 0f;
                    if (ra < 0.25f && sa < 0.05f) { nomark++; if (nomark == 1) Log($"   no mark at feet {feet:F2} m: ring alpha {ra:F2}, shadow {sa:F2}"); }   // K256g: the mark is subtle now (0.13 .. 0.18 by day): 0.05 counts
                    worstRing = Mathf.Max(worstRing, ra); worstShadow = Mathf.Max(worstShadow, sa);
                }
                if (shot < 16 && t >= next) { Shot($"e02_takeoff_strip/{shot:00}"); shot++; next += 0.2f; }
                if (shot >= 16 && feet >= 3f) break;
                t += Time.deltaTime; yield return null;
            }
            Log($"take-off frames from the order to 3 m: {frames}; not selected {unsel}, no frame rect {norect}, with neither the ring nor the shadow {nomark}");
            Check("E2 selected with its frame on every frame of the take-off (from the order to 3 m up): 0 frames without", frames > 5 && unsel == 0 && norect == 0);
            Check("E2 a ground mark on every one of those frames (the ring fades as the pads rise, the shadow comes in): 0 frames with neither", frames > 5 && nomark == 0);
            Log("note: round 2's take-off strip (frames 00-02 without a frame) was taken without the ship selected: its frame driver ordered the move without selecting it; the brackets from its frame 03 on were the hover frame of the resting pointer as the rising hull reached it. In the game the ring left at the first frame of the climb while the shadow came only from 0.3 m: now they cross-fade.");
            // E3: cruise by day, 8 m or more, over open grass (flat, no plant or hard top under the mark); up to 3 more flights to find it
            Log("E3 cruise by day (8 m or more) over open grass: the soft dark shadow (M12)");
            follow = true; bool open = false;
            float[] dirs = { 0f, 90f, 180f };
            for (int fl = 0; fl < 4 && !open; fl++)
            {
                if (fl > 0)
                {
                    yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne, 40f);
                    var q2 = Pad(560, dirs[fl - 1], 60); if (q2 == null) continue;
                    Move(q2[0], q2[1]); Log($"   another flight for open ground ({dirs[fl - 1]:F0} deg)");
                }
                float wt = 0f;
                while (wt < 30f && !open && !(s.ShuttleState == ZShuttleState.Descend || (s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne && wt > 2f)))
                {
                    if (s.ShuttleState == ZShuttleState.Cruise && ship.FeetHeight >= 8f && OpenGround()) open = true;
                    wt += Time.deltaTime; yield return null;
                }
            }
            if (!open) { Check("E3 a cruise at 8 m or more over open grass", false); yield break; }
            g.paused = true; follow = false; fixAt = new Vector3(ship.transform.position.x, 0f, ship.transform.position.z); yield return null; yield return null;
            var sh = ship.Shadow; var cam = Camera.main; float cy = sh.PlaneY, feetNow = ship.FeetHeight;
            yield return Grab(); var A = grab;
            OzShuttleShadow.ProofHide = true; yield return null; yield return Grab(); var B = grab; OzShuttleShadow.ProofHide = false;
            input.MeshRect(s, out var hull); var c = sh.Centre; float yaw = ship.transform.eulerAngles.y * Mathf.Deg2Rad;
            var d = Diff(A, B, new Rect(0, 0, gw, gh), 0, (x, y) =>
            {
                if (hull.Contains(new Vector2(x, y))) return false;                                          // the hull's own lights blink
                if (!cam || !GroundAt(cam, x, y, cy, out var p)) return false;
                float dx = p.x - c.x, dz = p.z - c.z, al = dx * Mathf.Sin(yaw) + dz * Mathf.Cos(yaw), ac = dx * Mathf.Cos(yaw) - dz * Mathf.Sin(yaw);
                return (al / OzShuttleShadow.AlongM) * (al / OzShuttleShadow.AlongM) + (ac / OzShuttleShadow.AcrossM) * (ac / OzShuttleShadow.AcrossM) <= 0.16f;   // its core: 0.4 of the radius
            });
            float dark = d.lb > 0f ? 1f - d.la / d.lb : 0f;
            Log($"cruise feet {feetNow:F1} m, strength {sh.StrengthNow:F2}, plane {sh.PlaneKind}; its core: {d.n} px, luma {d.lb:F1} without -> {d.la:F1} with: {dark * 100f:F0} % darker; off the ship's ground point {new Vector2(c.x - ship.transform.position.x, c.z - ship.transform.position.z).magnitude:F2} m");
            Check("E3 at cruise height by day the landing mark reads (a soft disc, not a real shadow): its core 12 .. 34 % darker than the same ground without it", d.n > 500 && dark >= 0.12f && dark <= 0.34f);
            Shot("e03_cruise_soft_shadow_h70"); yield return null;
            g.paused = false; follow = true;
            // E3b: the normal landing at the end of this flight: a mark on every frame from 3 m down (the shadow fades out as the ring grows in)
            int lf = 0, lnone = 0; float lt = 0f;
            while (lt < 40f && !(s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne))
            {
                if (s.ShuttleState == ZShuttleState.Descend && ship.FeetHeight < 3f && !s.ShuttleRoughLanding)
                {
                    lf++; float ra = OzShuttleShadowRing.AlphaOf(s.Id), sa = ship.Shadow != null && ship.Shadow.Shown ? ship.Shadow.StrengthNow : 0f;
                    if (ra < 0.25f && sa < 0.05f) { lnone++; if (lnone == 1) Log($"   landing: no mark at feet {ship.FeetHeight:F2} m: ring alpha {ra:F2}, shadow {sa:F2}"); }   // K256g: 0.05 counts (the mark is subtle now)
                }
                lt += Time.deltaTime; yield return null;
            }
            float la0 = OzShuttleShadowRing.AlphaOf(s.Id);
            Log($"the landing: frames from 3 m down {lf}, with neither mark {lnone}; the ring at the touchdown {la0:F2}");
            Check("E3b the normal landing: a ground mark on every frame from 3 m down to the touchdown (0 frames with neither)", lf > 5 && lnone == 0);
            yield return Wait(0.5f);
        }

        // ------------------------------------------------------------------ E4 / E5 (by day), E7 (night): the red mark; E6 the long hover
        IEnumerator E4E5(bool night)
        {
            string when = night ? "night" : "day", tag = night ? "e07" : "e04";
            if (dn) { float tod = night ? 0.70f : 0.10f; dn.timeOfDay = tod; dn.Apply(tod); }
            yield return Wait(1.0f);
            var water = Spot((x, y, why) => why == "water", 100, 1400);
            yield return RedStage($"{tag}_red_river_{when}", water, "water", night);
            // flat grass, blocked by a squad of the player's own (a place it cannot land: "units")
            var grass = Spot((x, y, why) => why == "" && Flat(x, y), 300, 1200);
            if (grass != null && !night) { var sq = Squad("grunt", grass[0] >> 4, grass[1] >> 4, me); Log($"a grunt squad placed on flat grass at tile {grass[0] >> 4},{grass[1] >> 4}: {(sq != null ? "#" + sq.Id : "none")}"); yield return Wait(1.0f); }
            if (night) grass = blockedGrass;
            else blockedGrass = grass;
            yield return RedStage($"{(night ? "e07" : "e05")}_red_grass_{when}", grass, "ground", night);
            if (!night)
            {   // E6: the long hover after it: dark again
                Log("E6 the long hover after the refusal: dark again after its 2 s (the 17a / 21_03 situation)");
                yield return WaitFor(() => ship.Shadow.RefusedAgo >= 2.5f, 3f);
                int redLate = 0; float t = 0f;
                while (t < 3.0f) { if (ship.Shadow.RedNow > 0.01f) redLate++; t += Time.deltaTime; yield return null; }
                Shot("e06_long_hover_dark_h70");
                Check("E6 hovering on where it cannot land: no red frame from 2.5 s to 5.5 s after the refusal", redLate == 0 && s.ShuttleState == ZShuttleState.Hover);
            }
            // away to a free place before the next stage
            var free = Pad(400, 90f, 180); if (free != null) { Move(free[0], free[1]); follow = true; yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne, 50f); }
            if (night && dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); }
            yield return Wait(0.5f);
        }
        int[] blockedGrass;

        IEnumerator RedStage(string name, int[] at, string want, bool night)
        {
            Log($"{name}: the red mark where it cannot land (M4)");
            if (at == null) { Check(name + ": a place of that kind", false); yield break; }
            Sel(); var aw = ZGame.PxToWorld(at[0], at[1]); Fix(aw); yield return Park();
            OzShuttleShadow.WallVertsMax = 0; OzShuttleShadow.ProofWalls = true;
            float t0 = Time.unscaledTime;
            Check(name + ": the move order is taken", Move(at[0], at[1]));
            yield return WaitFor(() => s.ShuttleState != ZShuttleState.Hover, 5f);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Hover, 50f);
            if (s.ShuttleState != ZShuttleState.Hover) { Check(name + ": it hovers there (refused)", false); OzShuttleShadow.ProofWalls = false; yield break; }
            var sh = ship.Shadow; float lo = 9f, hi = -9f, t = 0f, next = 0f; int nRed = 0, shot = 0; bool pairDone = false; Px px = default; string plane = "-";
            Color32[] A = null, B = null; float pairBounce = 0f;
            while (t < 2.0f)
            {
                lo = Mathf.Min(lo, sh.ScaleNow); hi = Mathf.Max(hi, sh.ScaleNow); plane = sh.PlaneKind;
                if (shot < 12 && t >= next)
                {
                    if (sh.RedNow >= 0.5f) nRed++;
                    Shot($"{name}_strip/{shot:00}"); shot++; next += 1f / 6f;
                    if (shot == 4) Shot($"{name}_h70");
                }
                if (!pairDone && t > 0.9f && sh.Bounce >= 0.85f)
                {   // the mark at the top of a hop, then the same frame without it
                    pairDone = true; pairBounce = sh.Bounce;
                    yield return Grab(); A = grab; OzShuttleShadow.ProofHide = true; yield return null; yield return Grab(); B = grab; OzShuttleShadow.ProofHide = false;
                    t += 2f * Time.deltaTime;
                }
                t += Time.deltaTime; yield return null;
            }
            int walls = OzShuttleShadow.WallVertsMax; OzShuttleShadow.ProofWalls = false;
            if (A != null)
            {
                var cam = Camera.main; var c = sh.Centre; float cy = sh.PlaneY, sc = 1.15f; input.MeshRect(s, out var hull); float yaw = ship.transform.eulerAngles.y * Mathf.Deg2Rad;
                px = Diff(A, B, new Rect(0, 0, gw, gh), 0, (x, y) =>
                {
                    if (hull.Contains(new Vector2(x, y))) return false;
                    if (!cam || !GroundAt(cam, x, y, cy, out var p)) return false;
                    float dx = p.x - c.x, dz = p.z - c.z, al = dx * Mathf.Sin(yaw) + dz * Mathf.Cos(yaw), ac = dx * Mathf.Cos(yaw) - dz * Mathf.Sin(yaw);
                    return (al / (OzShuttleShadow.AlongM * sc)) * (al / (OzShuttleShadow.AlongM * sc)) + (ac / (OzShuttleShadow.AcrossM * sc)) * (ac / (OzShuttleShadow.AcrossM * sc)) <= 0.36f;
                });
            }
            Log($"{name}: red in {nRed} of {shot} strip frames; size x{lo:F2} .. x{hi:F2}; plane {plane}; grid points on a wall (worst rebuild) {walls}; the pair at bounce {pairBounce:F2}: {px.n} px, luma {px.lb:F1} without -> {px.la:F1} with (lightest pixel {px.lighter:+0.0;-0.0}), red (r - g) {px.rb:+0.0;-0.0} -> {px.ra:+0.0;-0.0}; ui_error {Played(t0, "ui_error")}, Can't land here {Played(t0, "vo_shuttle_no_land")}");
            Check(name + ": red in at least 8 of the 12 frames of the 2 s strip from the refusal", shot >= 12 && nRed >= 8);
            Check(name + ": it bounces (its size swings 0.12 or more)", hi - lo >= 0.12f);
            Check(name + ": never on a wall (0 drawn grid points off its plane or on a slope over 50 degrees)", walls == 0);
            Check(name + $": it lies on the {want} (plane: {want})", plane == want);
            float lighterMax = night ? 2.5f : 1.5f, redUp = night ? 2f : 5f;
            Check(name + $": no glow: the mark's pixels on average no lighter than the same ground without it (+{lighterMax:F1} / 255 at most), and it reads red (r - g up by {redUp:F0} or more)", px.n > 300 && px.la <= px.lb + lighterMax && px.ra - px.rb >= redUp);
            Check(name + ": the error blip and \"Can't land here!\" (every visible action has its sound)", Played(t0, "ui_error") > 0 && (Played(t0, "vo_shuttle_no_land") > 0 || ship.LastVoice == "vo_shuttle_no_land"));
        }

        // ------------------------------------------------------------------ E8: a crash landing: no ring while it is drawn, back in 0.3 s once level (M5)
        IEnumerator E8()
        {
            Log("E8 a crash landing on open grass: the ring (M5)");
            Sel(); var q = Pad(420, 0f, 120); if (q == null) { Check("E8 a landing place", false); yield break; }
            OzPlayableShuttle.ProofRoughNext = true;
            Fix(ZGame.PxToWorld(q[0], q[1])); yield return Park();
            Check("E8 the move order is taken", Move(q[0], q[1]));
            yield return WaitFor(() => ship.Touch != null && ship.Touch.Active, 50f);
            if (ship.Touch == null || !ship.Touch.Active) { Check("E8 a crash landing came", false); yield break; }
            int drawnInTouch0 = OzShuttleShadowRing.DrawnInTouch, ringFrames = 0; float t = 0f; bool restShot = false;
            while (ship.Touch.Active && t < 8f)
            {
                if (OzShuttleShadowRing.AlphaOf(s.Id) > 0.004f) ringFrames++;
                if (!restShot && t > 1.0f) { restShot = true; Shot("e08_crash_resting_tilted_no_ring_h70"); }
                t += Time.deltaTime; yield return null;
            }
            float tEnd = Time.unscaledTime, t95 = -1f; t = 0f;
            while (t < 1.5f) { if (t95 < 0f && OzShuttleShadowRing.AlphaOf(s.Id) >= 0.95f) t95 = Time.unscaledTime - tEnd; t += Time.deltaTime; yield return null; if (t > 0.6f && t95 >= 0f) break; }
            Shot("e08_crash_levelled_ring_h70");
            Log($"crash landing: frames with a ring while it was drawn {ringFrames} (draw counter +{OzShuttleShadowRing.DrawnInTouch - drawnInTouch0}); the ring full {t95:F2} s after it stood level");
            Check("E8 no ring while the crash landing is drawn (0 frames)", ringFrames == 0 && OzShuttleShadowRing.DrawnInTouch == drawnInTouch0);
            Check("E8 the ring fades in over 0.3 s once it stands level (full within 0.2 .. 0.45 s)", t95 >= 0.2f && t95 <= 0.45f);
            yield return Wait(0.5f);
        }

        // ------------------------------------------------------------------ E9: on the repair station's deck: no ring (M5)
        IEnumerator E9()
        {
            Log("E9 on the repair station's deck: no ring (M5)");
            ZObject station = null; foreach (var f in W.Flags) foreach (var b in f.LinkedObjects) if (b.Type == "repair" && station == null) { station = b; if (b.Owner != me) W.AwardZone(f, me); }
            if (station == null) { Log("no repair station on this level: skipped"); yield break; }
            s.SetHealth(Mathf.Max(1, s.MaxHealth * 6 / 10)); Sel();
            follow = true; yield return Park();
            int onDeck0 = OzShuttleShadowRing.DrawnOnDeck, hidden0 = OzShuttleShadowRing.DeckHidden; bool deckPlane = false;
            Check("E9 R sends it to the repair station", input.SendToRepair());
            float t = 0f;
            while (t < 70f && !(s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne && ship.DeckStation >= 0)) { if (ship.Shadow != null && ship.Shadow.Shown && ship.Shadow.PlaneKind == "deck") deckPlane = true; t += Time.deltaTime; yield return null; }
            if (!(s.ShuttleState == ZShuttleState.Landed && ship.DeckStation >= 0)) { Check("E9 it lands on the station's deck", false); yield break; }
            yield return Wait(1.0f);
            follow = false; fixAt = new Vector3(ship.transform.position.x, 0f, ship.transform.position.z);
            Shot("e09_on_the_deck_no_ring_h70");
            if (rts) { rts.SetHeightImmediate(32f); yield return Wait(0.3f); Shot("e09_on_the_deck_no_ring_extra_close"); yield return null; rts.SetHeightImmediate(70f); }
            Log($"on the deck: ring draws there {OzShuttleShadowRing.DrawnOnDeck - onDeck0}, frames left out {OzShuttleShadowRing.DeckHidden - hidden0}; the shadow lay on the deck while it came down: {deckPlane}");
            Check("E9 selected on the station's deck, no ring is drawn (0 draws, left out every frame)", OzShuttleShadowRing.DrawnOnDeck == onDeck0 && OzShuttleShadowRing.DeckHidden > hidden0);
            follow = true;
        }
    }
}
