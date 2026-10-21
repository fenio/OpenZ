using System.Collections;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted check of the testing mode's free look (RtsCamera.FreeTurn) and of the minimap's view frame.
    /// Play mode, a level loaded, the HUD shown, no menu and no production window open, a quiet moment (no blast shaking the view),
    /// the pointer inside the Game view and away from its edges, hands off the keys.
    /// Menu: OpenZ/Checks/Free camera (Play mode), or OpenZ.EditorTools.OzFreeCamProof.Run().
    /// The result is one log line that starts "[OpenZ free camera] PASS 30/30" or "[OpenZ free camera] FAIL" (also in Report).
    ///
    /// A  the fixed view is untouched while the testing overlay is hidden: FreeTurn is refused, the camera stands where the fixed
    ///    rule puts it, the minimap frame is the fixed view's frame (3 zoom levels x 3 checks = 9).
    /// B  with the overlay shown: a quarter turn, a half turn, the lowest and the steepest tilt at 3 zoom levels: the view centre
    ///    stays on the point the camera looks at, the camera never flips, never stands under its floor, the top screen row meets
    ///    the ground inside the far plane (12); at the closest zoom and the lowest tilt at all four map borders the camera keeps
    ///    the fixed view's closest height over the outer scenery (4).
    /// C  the minimap frame is the view's footprint on the ground: 3 zoom levels x 6 views x 4 corners = 72 corners, each within
    ///    1 map pixel of its corner ray's meeting point with the ground plane (1).
    /// D  with the view turned 90 degrees a scroll moves along the view's heading, and a minimap click lands the view centre on the
    ///    clicked point with the turn kept (2).
    /// E  testing overlay hidden: the fixed view is back within 1 s, the minimap frame is the fixed view's frame again (2).
    /// Not counted: the real middle button through queued input (only when the Game view has the focus).
    /// The camera, the zoom and the overlay are put back when it ends.
    /// </summary>
    public static class OzFreeCamProof
    {
        public static string Report = "not run";
        static IEnumerator run;
        static int waitFrame, ok, bad;
        static readonly StringBuilder fails = new StringBuilder();
        static readonly Vector2[] Corners = { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };   // the minimap's own order

        [MenuItem("OpenZ/Checks/Free camera (Play mode)")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[OpenZ free camera] enter Play mode in a level first"); return; }
            if (run != null) { Debug.LogWarning("[OpenZ free camera] a run is still going"); return; }
            ok = bad = 0; fails.Clear(); Report = "running";
            run = Proof(); waitFrame = 0; EditorApplication.update += Step;
        }

        static void Step()
        {
            if (run == null || !EditorApplication.isPlaying) { run = null; EditorApplication.update -= Step; return; }
            if (Time.frameCount < waitFrame) return;
            bool more;
            try { more = run.MoveNext(); }
            catch (System.Exception ex) { Debug.LogException(ex); Report = "[OpenZ free camera] FAIL: exception " + ex.Message; more = false; }
            if (!more) { run = null; EditorApplication.update -= Step; return; }
            waitFrame = Time.frameCount + (run.Current is int n ? n : 1);
        }

        static void Check(bool pass, string what) { if (pass) ok++; else { bad++; fails.Append(" | ").Append(what); } }

        static void Stop(string why) { Report = "[OpenZ free camera] FAIL: " + why; Debug.LogError(Report); }

        static bool PlaneHit(Ray r, out Vector3 p)
        {
            p = default;
            if (r.direction.y > -1e-5f) return false;
            p = r.GetPoint(-r.origin.y / r.direction.y);
            return true;
        }

        static void Pose(RtsCamera rts, Vector3 at, float h) { rts.focus = new Vector3(at.x, 0f, at.z); rts.SetHeightImmediate(h); rts.TakeShake(); }

        /// <summary>The view through the same call the middle button makes: heading and tilt in degrees.</summary>
        static bool View(RtsCamera rts, float yaw, float tilt)
        {
            rts.FreeLookOff();
            return rts.FreeTurn(new Vector2(yaw / RtsCamera.FreeLookSpeed, (rts.pitch - tilt) / RtsCamera.FreeLookSpeed));
        }

        /// <summary>The camera stands where the fixed rule puts it: height h over the focus, h / tan(pitch) behind it, north up.</summary>
        static bool FixedPose(RtsCamera rts, Camera cam, out string why)
        {
            float h = Mathf.Clamp(rts.height, rts.minHeight, rts.maxHeight), back = h / Mathf.Tan(rts.pitch * Mathf.Deg2Rad);
            var pos = rts.focus + new Vector3(0f, h, -back) + rts.ShakeOffset;
            var rot = Quaternion.Euler(rts.pitch, 0f, 0f);
            float dp = Vector3.Distance(cam.transform.position, pos);
            float dr = Mathf.Max((cam.transform.forward - rot * Vector3.forward).magnitude, (cam.transform.up - rot * Vector3.up).magnitude) * Mathf.Rad2Deg;
            why = $"camera {dp:0.#####} m and {dr:0.####} deg off the fixed pose";
            return dp <= 1e-3f && dr <= 2e-3f;
        }

        /// <summary>Corners of the minimap frame that equal the fixed view's rule: corner rays on the real ground, the lower line kept on the map.</summary>
        static int FrameAsFixed(ZGame g, OpenZMinimap mm, Camera cam)
        {
            var want = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                var ray = cam.ViewportPointToRay(new Vector3(Corners[i].x, Corners[i].y, 0));
                if (!g.GroundPoint(ray, out var p)) p = ray.GetPoint(300);
                var px = ZGame.WorldToPx(p);
                want[i] = mm.PxToLocal(px.x, px.y);
            }
            OzCamEdge.KeepFrameBottom(want, OzPx.Line(3f) * 0.5f);
            int same = 0;
            for (int i = 0; i < 4; i++) if ((want[i] - mm.Frame[i]).magnitude <= 0.01f) same++;
            return same;
        }

        /// <summary>The view centre lies on the point the camera looks at (the focus), and the camera is `d` m from it (d under 0 = not asked).</summary>
        static bool Orbit(RtsCamera rts, Camera cam, float d, out string why)
        {
            bool hit = PlaneHit(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out var c);
            float off = hit ? Vector2.Distance(new Vector2(c.x, c.z), new Vector2(rts.focus.x, rts.focus.z)) : 999f;
            float dist = Vector3.Distance(cam.transform.position - rts.ShakeOffset, rts.focus);
            why = $"view centre {off:0.###} m off the focus, camera {dist:0.##} m from it (zoom gives {d:0.##})";
            return off <= 0.05f && (d < 0f || Mathf.Abs(dist - d) <= 0.02f);
        }

        /// <summary>At a tilt limit: upright, looking down at the focus, over its floor; at the lowest tilt the top screen row meets the ground inside the far plane.</summary>
        static bool Limit(RtsCamera rts, Camera cam, ZGame g, float h, bool low, out string why)
        {
            var t = cam.transform; var p = t.position - rts.ShakeOffset;
            bool upright = t.up.y > 0.01f && t.forward.y < 0f;
            bool centre = Orbit(rts, cam, -1f, out string o);
            float floor = Mathf.Min(h, Mathf.Max(RtsCamera.FreeFloor, g.TerrainY(p.x, p.z) + RtsCamera.FreeClear));
            bool over = !low || p.y >= floor - 0.01f;
            bool ground = true; float far = 0f;
            if (low)
                foreach (var vp in new[] { new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 0f) })
                {
                    var ray = cam.ViewportPointToRay(vp);
                    if (!PlaneHit(ray, out var q)) { ground = false; continue; }
                    float dq = Vector3.Distance(ray.origin, q); far = Mathf.Max(far, dq);
                    if (dq >= cam.farClipPlane) ground = false;
                }
            why = $"upright {upright}, {o}, camera y {p.y:0.##} (floor {floor:0.##}), top row meets the ground {(low ? far.ToString("0") + " m out" : "n/a")}";
            return upright && centre && over && ground;
        }

        static IEnumerator Proof()
        {
            var g = ZGame.I; var hud = OpenZHud.I; var cheat = OzCheatSpawner.I;
            var rts = hud ? hud.rts : null; var mm = hud ? hud.Minimap : null;
            if (g == null || g.World == null || !hud || !rts || !mm || !mm.MapRect) { Stop("needs Play mode in a level with the HUD built"); yield break; }
            if (!cheat) { Stop("the testing overlay is not installed (oz_dev is 0)"); yield break; }
            if (hud.Modal || !hud.hudVisible || RtsCamera.InputLocked || !rts.enabled || !mm.MapRect.gameObject.activeInHierarchy || (hud.Production && hud.Production.IsOpen))
            { Stop("show the HUD and close the menus and the production window first (no ending running)"); yield break; }
            var cam = rts.GetComponent<Camera>(); var map = g.World.Map;
            float speed = RtsCamera.FreeLookSpeed, lo = Mathf.Max(22f, RtsCamera.FreeTiltMin), hi = Mathf.Min(89f, RtsCamera.FreeTiltMax);
            Vector3 focus0 = rts.focus; float h0 = rts.height; var follow0 = rts.follow; Vector2 drift0 = rts.autoDrift; bool shown0 = OzCheatSpawner.Visible;
            rts.follow = null; rts.autoDrift = Vector2.zero;
            var centre = new Vector3(rts.mapSizeMeters.x * 0.5f, 0f, -rts.mapSizeMeters.y * 0.5f);
            float mid = Mathf.Clamp(70f, rts.minHeight, rts.maxHeight);
            float[] zooms = { rts.minHeight, mid, rts.maxHeight };
            string why;

            // ---------------------------------------------------------------- A: overlay hidden, the fixed view
            if (OzCheatSpawner.Visible) cheat.Show(false);
            for (int i = 0; i < 240 && rts.FreeLook; i++) yield return 1;
            foreach (float h in zooms)
            {
                Pose(rts, centre, h); yield return 3;
                bool refused = !rts.FreeTurn(new Vector2(300f, 200f));
                Check(refused && rts.yaw == 0f && rts.Tilt == rts.pitch && !rts.FreeLook, $"A zoom {h:0}: a turn outside the testing mode must be refused (yaw {rts.yaw}, tilt {rts.Tilt})");
                Check(FixedPose(rts, cam, out why), $"A zoom {h:0}: {why}");
                int same = FrameAsFixed(g, mm, cam);
                Check(same == 4, $"A zoom {h:0}: minimap frame {same}/4 corners as the fixed view's rule");
            }

            // ---------------------------------------------------------------- B: overlay shown, turn and tilt
            cheat.Show(true); yield return 2;
            if (!OzCheatSpawner.Visible)
            {
                rts.autoDrift = drift0; rts.follow = follow0; Pose(rts, focus0, h0);
                Stop("the testing overlay did not stay open"); yield break;
            }
            foreach (float h in zooms)
            {
                Pose(rts, centre, h); rts.FreeLookOff(); yield return 1;
                float d = h / Mathf.Sin(rts.pitch * Mathf.Deg2Rad);
                bool took = rts.FreeTurn(new Vector2(90f / speed, 0f)); yield return 2;
                bool good = Orbit(rts, cam, d, out why);
                Check(took && good && Mathf.Abs(Mathf.DeltaAngle(rts.yaw, 90f)) <= 0.01f, $"B zoom {h:0} turn 90: accepted {took}, yaw {rts.yaw:0.###}, {why}");
                rts.FreeTurn(new Vector2(90f / speed, 0f)); yield return 2;
                good = Orbit(rts, cam, d, out why);
                Check(good && Mathf.Abs(Mathf.DeltaAngle(rts.yaw, 180f)) <= 0.01f, $"B zoom {h:0} turn 180: yaw {rts.yaw:0.###}, {why}");
                rts.FreeTurn(new Vector2(0f, 100000f)); yield return 2;                                   // mouse up = look up: the lowest tilt, the clamp holds
                good = Limit(rts, cam, g, h, true, out why);
                Check(good && Mathf.Abs(rts.Tilt - lo) <= 1e-3f, $"B zoom {h:0} lowest tilt: asked {rts.Tilt:0.###} (limit {lo:0.#}), {why}");
                rts.FreeTurn(new Vector2(0f, -100000f)); yield return 2;                                  // mouse down = look down: the steepest tilt
                good = Limit(rts, cam, g, h, false, out why);
                Check(good && Mathf.Abs(rts.Tilt - hi) <= 1e-3f, $"B zoom {h:0} steepest tilt: asked {rts.Tilt:0.###} (limit {hi:0.#}), {why}");
            }
            {   // the closest zoom, the lowest tilt, the camera standing outside each map border: the fixed view's closest height
                float h = rts.minHeight;
                var border = new (string name, Vector3 at, float yaw)[]
                {
                    ("lower", new Vector3(centre.x, 0f, -99999f), 0f), ("upper", new Vector3(centre.x, 0f, 99999f), 180f),
                    ("left", new Vector3(-99999f, 0f, centre.z), 90f), ("right", new Vector3(99999f, 0f, centre.z), -90f),
                };
                foreach (var b in border)
                {
                    OzCamEdge.Snap(); Pose(rts, b.at, h); View(rts, b.yaw, lo); yield return 3;
                    var p = cam.transform.position - rts.ShakeOffset;
                    bool outside = p.x < 0f || p.x > rts.mapSizeMeters.x || p.z > 0f || p.z < -rts.mapSizeMeters.y;
                    bool good = Orbit(rts, cam, -1f, out why);
                    Check(good && outside && p.y >= RtsCamera.FreeEdgeHeight - 0.01f, $"B {b.name} border: camera outside the map {outside}, y {p.y:0.##} (needs {RtsCamera.FreeEdgeHeight:0.#}), {why}");   // the border floor: the old closest height (RtsCamera.ZoomLimits.cs)
                }
                OzCamEdge.Snap();
            }

            // ---------------------------------------------------------------- C: the minimap frame = the footprint on the ground
            int cOk = 0, cBad = 0; float worst = 0f;
            var views = new (float yaw, float tilt)[] { (0f, 40f), (37f, rts.pitch), (90f, lo), (180f, 70f), (-120f, 30f), (45f, hi) };
            foreach (float h in zooms)
                foreach (var v in views)
                {
                    Pose(rts, centre, h); View(rts, v.yaw, v.tilt); yield return 3;
                    var r = mm.MapRect.rect;
                    for (int i = 0; i < 4; i++)
                    {
                        bool hit = PlaneHit(cam.ViewportPointToRay(new Vector3(Corners[i].x, Corners[i].y, 0)), out var p);
                        var want = new Vector2(p.x * 8f, -p.z * 8f);
                        var got = new Vector2(mm.Frame[i].x / r.width * map.WidthPix, (1f - mm.Frame[i].y / r.height) * map.HeightPix);
                        float e = hit ? Mathf.Max(Mathf.Abs(want.x - got.x), Mathf.Abs(want.y - got.y)) : 999f;
                        worst = Mathf.Max(worst, e);
                        if (e <= 1f) cOk++; else cBad++;
                    }
                }
            Check(cBad == 0, $"C minimap frame: {cOk}/{cOk + cBad} corners within 1 map pixel, worst {worst:0.00}");

            // ---------------------------------------------------------------- D: scroll and minimap click with a turned view
            Pose(rts, centre, mid); View(rts, 90f, 40f); yield return 2;
            var f0 = rts.focus; rts.autoDrift = new Vector2(0f, 20f);                                     // "up", through the same lines as the keys and the screen edge
            yield return 15;
            rts.autoDrift = Vector2.zero; yield return 1;
            var moved = rts.focus - f0; var fwd = cam.transform.forward; fwd.y = 0f; fwd.Normalize();
            float cos = moved.magnitude > 0.2f ? Vector3.Dot(moved.normalized, fwd) : 0f;
            Check(cos >= 0.999f, $"D scroll up with the view turned 90: moved {moved.magnitude:0.0} m, cosine to the view's heading {cos:0.0000} (needs 0.999)");
            float flyOff = 999f;
            {
                var r = mm.MapRect.rect;
                Vector2 sp = RectTransformUtility.WorldToScreenPoint(null, mm.MapRect.TransformPoint(new Vector3(Mathf.Lerp(r.xMin, r.xMax, 0.42f), Mathf.Lerp(r.yMin, r.yMax, 0.55f), 0f)));
                if (mm.ScreenToPx(sp, out var click))
                {
                    var target = ZGame.PxToWorld(click.x, click.y);
                    rts.FlyTo(target);                                                                    // what a left click on the minimap does
                    float t0 = Time.unscaledTime;
                    while ((rts.focus - target).sqrMagnitude > 1e-4f && Time.unscaledTime - t0 < RtsCamera.FlyTimeout + 0.5f) yield return 1;
                    yield return 2;
                    if (PlaneHit(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out var c)) flyOff = Vector2.Distance(new Vector2(c.x, c.z), new Vector2(target.x, target.z));
                }
                Check(flyOff <= 0.1f && Mathf.Abs(Mathf.DeltaAngle(rts.yaw, 90f)) <= 0.01f, $"D minimap click with the view turned 90: view centre {flyOff:0.###} m off the clicked point (needs 0.1), yaw {rts.yaw:0.##}");
            }

            // ---------------------------------------------------------------- E: overlay hidden, back to the fixed view
            Pose(rts, centre, mid); View(rts, 180f, lo); yield return 3;
            float tOff = Time.unscaledTime; cheat.Show(false); yield return 1;
            while (rts.FreeLook && Time.unscaledTime - tOff < 3f) yield return 1;
            float backS = Time.unscaledTime - tOff;
            yield return 3;
            bool home = FixedPose(rts, cam, out why);
            Check(home && backS <= 1f && !rts.FreeLook && rts.yaw == 0f && rts.Tilt == rts.pitch, $"E back to the fixed view: {backS:0.00} s (needs 1.00), yaw {rts.yaw}, tilt {rts.Tilt}, {why}");
            int again = FrameAsFixed(g, mm, cam);
            Check(again == 4, $"E minimap frame {again}/4 corners as the fixed view's rule");

            // ---------------------------------------------------------------- not counted: the real middle button
            string real = "not checked here (the Game view has no focus): do it by hand";
            if (Application.isFocused && Mouse.current != null)
            {
                cheat.Show(true); yield return 2;
                Pose(rts, centre, mid); rts.FreeLookOff(); yield return 2;
                var at = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = at, delta = new Vector2(100f, 0f) }.WithButton(MouseButton.Middle));
                yield return 3;
                float got = rts.yaw;
                InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = at });
                yield return 2;
                real = $"100 px to the right with the middle button held turned the view {got:0.0} deg (expected {100f * speed:0.0}; a real mouse moving at that moment adds to it)";
            }

            // ---------------------------------------------------------------- put back
            rts.FreeLookOff(); rts.autoDrift = drift0; rts.follow = follow0; OzCamEdge.Snap(); Pose(rts, focus0, h0);
            if (OzCheatSpawner.Visible != shown0) cheat.Show(shown0);
            Report = $"[OpenZ free camera] {(bad == 0 ? "PASS" : "FAIL")} {ok}/{ok + bad} checks on {g.level} at {Screen.width}x{Screen.height}: minimap frame {cOk}/{cOk + cBad} corners within 1 map pixel (worst {worst:0.00}); "
                   + $"scroll cosine {cos:0.0000}; minimap click {flyOff:0.###} m; fixed view back in {backS:0.00} s; tilt limits {lo:0.#} / {hi:0.#} deg, {speed:0.##} deg per pixel. Real middle button: {real}.{fails}";
            if (bad == 0) Debug.Log(Report); else Debug.LogError(Report);
        }
    }
}
