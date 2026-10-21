#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K440 Play proof (editor only): the camera never sees into the shuttle (View/OzShuttleCamFade.cs), on level 16 in Play (menu
    /// OpenZ/V1.1/Shuttle: K440 Camera No-Clip Proof, Editor/OzShuttleCamFadeCheck.cs). Set-up: the CPU teams removed, morning (day clock
    /// 0.10, held). The ship flies between its spot A and a free spot B 50 .. 80 m away. For the two flights through the lens the game's
    /// camera is held at the middle of the way, at the ship's drawn height + 2.5 m (its hull's middle), looking at the coming ship 27 deg
    /// down: the free camera's low view at its 14 m floor, put where the ship must pass through it (the game camera's RtsCamera is off for
    /// those two flights and back on after them).
    ///  F1 the fade OFF (today): the near plane cuts the hull or the eye is inside it; pictures k440_f1_off_4m, k440_f2_off_cut.
    ///  F2 the fade ON, the flight back: every frame with a cut or the eye inside is fully cut (the fade's value and every hull part's block
    ///     1.00); it eased in and out (no pop); after it everything is back (cut 0, blocks 0, nothing hidden). Pictures k440_f3_on_dither,
    ///     k440_f4_on_gone, k440_f5_on_after (a moment after the pass the held camera turns round to the ship flying on).
    ///  Z the closest zoom (25 m, following the ship as a player does): the ship drawn as always (cut 0 every frame); k440_z1_25m_follow.
    ///  H the standard 70 m view: k440_h70.
    /// Output: <see cref="Folder"/> (the work tree's records, milestones/shuttle_camera_no_clip_k440, found upward at run time). Test only.
    /// </summary>
    [DefaultExecutionOrder(31000)]   // the held camera is placed after every pose (200) and before the fade measures it (32000)
    public sealed class OzShuttleCamFadeProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "shuttle_camera_no_clip_k440");
            return System.IO.Path.Combine(Application.persistentDataPath, "shuttle_camera_no_clip_k440");
        }
        public static void Run()
        {
            Report = "running";
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
            new GameObject("OzShuttleCamFadeProof").AddComponent<OzShuttleCamFadeProof>();
        }

        const float NearReach = 0.58f, HoldUp = 2.5f, HoldTilt = 27f;
        ZGame g; ZWorld W; RtsCamera rts; Camera cam; Team me = Team.Red;
        ZObject s; OzPlayableShuttle ship;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes; string dir;
        bool hold, on0 = true; Vector3 holdXZ; float holdYaw;
        readonly List<(Renderer r, Transform t, Vector3[] v)> hull = new List<(Renderer, Transform, Vector3[])>();

        sealed class Pass
        {
            public int frames, cutFrames, insideFrames, badFrames, fadeInFrames = -1, fadeOutFrames = -1, blocksShort;
            public float closest = 999f, maxCut, minCutInBad = 1f; public bool shotA, shotB, shotC, shotD, shotE;
            public int firstCutFrame = -1, firstGoneFrame = -1, lastGoneFrame = -1, lastCutFrame = -1;
        }

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; rts = FindFirstObjectByType<RtsCamera>(); cam = rts ? rts.GetComponent<Camera>() : Camera.main;
            dir = Folder(); System.IO.Directory.CreateDirectory(dir); on0 = OzShuttleCamFade.On;
            StartCoroutine(All());
        }

        void LateUpdate()
        {   // the held camera: on the way's middle, at the ship's hull middle, looking at it (the free camera's low view where the ship passes)
            if (!hold || !cam || !ship) return;
            float y = s != null && s.ShuttleAirborne ? ship.transform.position.y + HoldUp : g.GroundY(holdXZ.x, holdXZ.z) + (float)ZWorld.ShuttleCruiseM + HoldUp;
            cam.transform.SetPositionAndRotation(new Vector3(holdXZ.x, y, holdXZ.z), Quaternion.Euler(HoldTilt, holdYaw, 0f));
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleCamFadeProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); Log("   picture " + name + ".png"); }
        bool Move(int x, int y) => W.SendWaypoints(me, s, new[] { new ZWaypoint { mode = WpMode.Move, x = x, y = y, playerGiven = true } });
        bool Landed() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne && s.ShuttleLevelled && s.ShuttleRampPhase == ZShuttleRamp.Shut;

        /// <summary>A free tile middle (the ship can land there now) between minR and maxR px from (cx, cy), the nearest first.</summary>
        int[] Free(int cx, int cy, int minR, int maxR)
        {
            long best = long.MaxValue; int[] r = null; int e = ZWorld.ShuttleEdgeTiles + 2;
            for (int ty = e; ty < W.Map.height - e; ty++)
                for (int tx = e; tx < W.Map.width - e; tx++)
                {
                    int x = tx * 16 + 8, y = ty * 16 + 8; long d = (long)(x - cx) * (x - cx) + (long)(y - cy) * (y - cy);
                    if (d >= best || d < (long)minR * minR || d > (long)maxR * maxR) continue;
                    if (W.ShuttleNeverLands(x, y, s, out _) || !W.ShuttlePadFree(x, y, s, out _)) continue;
                    best = d; r = new[] { x, y };
                }
            return r;
        }

        /// <summary>The drawn hull: every part that takes the fade, with its mesh's vertices.</summary>
        void ReadHull()
        {
            hull.Clear();
            foreach (var r in ship.GetComponentsInChildren<Renderer>(true))
            {
                if (!OzShuttleCamFade.TakesFade(r)) continue;
                var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh || !mf.sharedMesh.isReadable) continue;
                hull.Add((r, r.transform, mf.sharedMesh.vertices));
            }
        }

        /// <summary>This frame's picture against the hull: the nearest drawn vertex (m), whether a drawn vertex lies between the eye and the
        /// near plane inside the frame (the near plane cuts the hull there), whether the eye is inside a hull part's own box.</summary>
        void Measure(out float closest, out bool nearCut, out bool inside)
        {
            closest = 999f; nearCut = false; inside = false;
            var eye = cam.transform.position; var w2c = cam.worldToCameraMatrix; float tv = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), th = tv * cam.aspect, near = cam.nearClipPlane;
            foreach (var (r, t, v) in hull)
            {
                if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                var M = t.localToWorldMatrix;
                var lp = t.InverseTransformPoint(eye); var lb = r.GetComponent<MeshFilter>().sharedMesh.bounds; if (lb.Contains(lp) && (r.name.Contains("Hull") || r.name.Contains("Engine_"))) inside = true;
                for (int i = 0; i < v.Length; i++)
                {
                    var w = M.MultiplyPoint3x4(v[i]); float d = (w - eye).magnitude; if (d < closest) closest = d;
                    if (d > 1.2f) continue;
                    var c = w2c.MultiplyPoint3x4(w); float z = -c.z;                                       // the camera looks down -z in its own space
                    if (z > 1e-4f && z <= near * 1.02f && Mathf.Abs(c.x) <= z * th && Mathf.Abs(c.y) <= z * tv) nearCut = true;
                }
            }
        }

        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || !cam) { Log("no game (level 16 in Play)"); fails++; Finish(); yield break; }
            if (!ZWorld.ShuttlePlayableDeviation || W.PlayableShuttle == null) { Log("no playable shuttle (level 16, the shuttle rule on, the option SHUTTLE on)"); fails++; Finish(); yield break; }
            me = g.player; s = W.PlayableShuttle; ship = OzPlayableShuttle.Of(s);
            if (!ship) { Log("the shuttle has no view"); fails++; Finish(); yield break; }
            Log($"K440 camera no-clip proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}; camera FOV {cam.fieldOfView:0}, near {cam.nearClipPlane:0.00} m; fade {OzShuttleCamFade.ClearM} -> {OzShuttleCamFade.GoneM} m, switch {OzShuttleCamFade.On}");
            int bots = W.Bots.Count; W.Bots.Clear();
            var dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            Log($"set-up: CPU removed {bots}, day 0.10 held");
            yield return WaitFor(Landed, 40f);
            ReadHull();
            int fb0 = OzShuttleCamFade.Fallbacks;
            var sm = Shader.Find("OpenZ/TeamLit"); bool prop = false; foreach (var (r, _, _) in hull) foreach (var m in r.sharedMaterials) if (m && m.HasProperty(OzShuttleCamFade.FadeProperty)) prop = true;
            Log($"   the hull: {hull.Count} parts that take the fade, TeamLit {(sm ? "found" : "MISSING")}");
            var a = new[] { s.CenterX, s.CenterY };
            var b = Free(a[0], a[1], 50 * 8, 80 * 8);
            if (b == null) { Check("a free landing spot 50 .. 80 m away", false); Finish(); yield break; }
            var A = ZGame.PxToWorld(a[0], a[1]); var B = ZGame.PxToWorld(b[0], b[1]); var mid = (A + B) * 0.5f;
            Log($"   the way: A ({A.x:0.0}, {A.z:0.0}) -> B ({B.x:0.0}, {B.z:0.0}), {(B - A).magnitude:0.0} m; the held camera at its middle ({mid.x:0.0}, {mid.z:0.0})");

            // F1: the fade OFF, A -> B, the camera looking back at A
            OzShuttleCamFade.On = false;
            var f1 = new Pass(); yield return Through(f1, "f1", b, mid, A, false);
            OzShuttleCamFade.On = true;                                                                // on for the rest of the proof (put back as it was at the end)
            yield return WaitFor(Landed, 40f);
            // F2: the fade ON, B -> A, the camera looking at B
            var f2 = new Pass(); yield return Through(f2, "f2", a, mid, B, true);
            yield return WaitFor(Landed, 40f);
            hold = false; if (rts) rts.enabled = true;
            yield return null;
            float cutAfter = OzShuttleCamFade.CutOf(ship.transform); int hiddenAfter = OzShuttleCamFade.HiddenNow(); float blockAfter = 0f;
            foreach (var (r, _, _) in hull) blockAfter = Mathf.Max(blockAfter, OzShuttleCamFade.ReadFade(r));

            // Z: the closest zoom, following the ship A -> B as a player does
            float zMax = 0f; int zFrames = 0; bool zShot = false;
            if (rts) { rts.FreeLookOff(); rts.SetHeightImmediate(RtsCamera.ZoomClosest); rts.follow = ship.transform; }
            Move(b[0], b[1]);
            float t0 = Time.time;
            while (Time.time - t0 < 40f && !(s.ShuttleState == ZShuttleState.Landed && Time.time - t0 > 3f))
            {
                yield return new WaitForEndOfFrame();
                zFrames++; zMax = Mathf.Max(zMax, OzShuttleCamFade.CutOf(ship.transform));
                if (!zShot && s.ShuttleState == ZShuttleState.Cruise && s.ShuttleLift >= 0.999) { zShot = true; Shot("k440_z1_25m_follow"); }
            }
            if (rts) rts.follow = null;
            yield return WaitFor(Landed, 40f);
            if (rts) { rts.focus = new Vector3(ship.transform.position.x, 0f, ship.transform.position.z); rts.SetHeightImmediate(70f); }
            yield return Wait(0.5f); Shot("k440_h70"); yield return OzProofPictures.AfterGrab();

            // the checks
            Check($"F1 the fade off (today): the ship's flight through the free camera's low view cuts its hull open: near-plane cut frames {f1.cutFrames}, eye inside {f1.insideFrames} of {f1.frames}, nearest drawn vertex {f1.closest:0.00} m",
                  f1.cutFrames + f1.insideFrames > 0 && f1.closest < NearReach);
            Check($"F2 the fade on: every frame with the near plane in the hull or the eye inside is fully cut: {f2.cutFrames + f2.insideFrames} such frames ({f2.cutFrames} cut, {f2.insideFrames} inside), not fully cut {f2.badFrames} (least {f2.minCutInBad:0.00}), parts whose block lacked the cut {f2.blocksShort}",
                  f2.cutFrames + f2.insideFrames > 0 && f2.badFrames == 0 && f2.blocksShort == 0);
            Check($"F2 it eased in and out (no pop): {f2.fadeInFrames} frames from the first cut to fully cut, {f2.fadeOutFrames} from fully cut back to none (at least 6 each)",
                  f2.fadeInFrames >= 6 && f2.fadeOutFrames >= 6);
            Check($"F2 after the pass everything is back: the cut {cutAfter:0.00}, the largest block value {blockAfter:0.00}, parts hidden {hiddenAfter}",
                  cutAfter == 0f && blockAfter <= 0.0001f && hiddenAfter == 0);
            Check($"Z the closest zoom ({RtsCamera.ZoomClosest} m) following the ship: drawn as always, the largest cut {zMax:0.00} over {zFrames} frames", zFrames > 30 && zMax == 0f);
            Check($"the shader has the cut ({OzShuttleCamFade.FadeProperty} on the hull's material {prop}), the older-shader fallback never used ({OzShuttleCamFade.Fallbacks - fb0})", prop && OzShuttleCamFade.Fallbacks == fb0);
            Log(OzShuttleCamFade.Status());
            Finish();
        }

        /// <summary>One flight through the held camera: to goal (px), the camera at mid looking at from, measured every frame after it is drawn.</summary>
        IEnumerator Through(Pass p, string tag, int[] goal, Vector3 mid, Vector3 from, bool on)
        {
            if (rts) { rts.FreeLookOff(); rts.enabled = false; }
            holdXZ = mid; var look = from - mid; holdYaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg; hold = true;
            yield return null;
            if (!Move(goal[0], goal[1])) { Log($"{tag}: the order was refused"); }
            float t0 = Time.time; bool passed = false; int turnAt = -1;
            while (Time.time - t0 < 45f)
            {
                yield return new WaitForEndOfFrame();
                if (s.ShuttleState == ZShuttleState.Landed && Time.time - t0 > 3f) break;
                Measure(out float closest, out bool nearCut, out bool inside);
                float cut = OzShuttleCamFade.CutOf(ship.transform);
                p.frames++; p.closest = Mathf.Min(p.closest, closest); p.maxCut = Mathf.Max(p.maxCut, cut);
                if (nearCut) p.cutFrames++;
                if (inside) p.insideFrames++;
                bool bad = nearCut || inside || closest < NearReach;
                if (on)
                {
                    if (cut > 0f && p.firstCutFrame < 0) p.firstCutFrame = p.frames;
                    if (cut >= 0.999f) { if (p.firstGoneFrame < 0) p.firstGoneFrame = p.frames; p.lastGoneFrame = p.frames; }
                    if (cut > 0f) p.lastCutFrame = p.frames;
                    if (bad)
                    {
                        if (cut < 0.999f) { p.badFrames++; p.minCutInBad = Mathf.Min(p.minCutInBad, cut); }
                        foreach (var (r, _, _) in hull) if (r && r.enabled && OzShuttleCamFade.ReadFade(r) < 0.999f) p.blocksShort++;
                    }
                    if (!p.shotC && cut >= 0.4f && cut <= 0.7f) { p.shotC = true; Shot("k440_f3_on_dither"); }
                    if (!p.shotD && bad) { p.shotD = true; Shot("k440_f4_on_gone"); }
                    if (p.shotD && !p.shotE && cut == 0f && closest > 8f) { p.shotE = true; Shot("k440_f5_on_after"); }
                }
                else
                {
                    float box = OzShuttleCamFade.BoxDistance(HullRenderers(), cam.transform.position);
                    if (!p.shotA && box <= 4f) { p.shotA = true; Shot("k440_f1_off_4m"); }
                    if (!p.shotB && (nearCut || inside)) { p.shotB = true; Shot("k440_f2_off_cut"); }
                }
                if (closest < 2f && !passed) { passed = true; turnAt = p.frames + 20; }
                if (passed && p.frames == turnAt) holdYaw += 180f;                                    // the camera turns round: the ship flying on is in view again
                if (passed && closest > 25f) break;                                                    // well past the camera
            }
            if (on)
            {
                p.fadeInFrames = p.firstCutFrame >= 0 && p.firstGoneFrame >= 0 ? p.firstGoneFrame - p.firstCutFrame : -1;
                p.fadeOutFrames = p.lastGoneFrame >= 0 && p.lastCutFrame >= 0 ? p.lastCutFrame - p.lastGoneFrame : -1;
            }
            Log($"{tag} ({(on ? "fade on" : "fade off")}): {p.frames} frames, nearest drawn vertex {p.closest:0.00} m, near-plane cut frames {p.cutFrames}, eye inside {p.insideFrames}, largest cut {p.maxCut:0.00}");
            yield return OzProofPictures.AfterGrab();
        }

        readonly List<Renderer> hullR = new List<Renderer>();
        List<Renderer> HullRenderers() { hullR.Clear(); foreach (var (r, _, _) in hull) hullR.Add(r); return hullR; }

        void Finish()
        {
            hold = false; OzShuttleCamFade.On = on0;
            if (rts && !rts.enabled) rts.enabled = true;
            Report = $"K440 PROOF: {passes} PASS, {fails} FAIL";
            Log(Report + " -> " + dir);
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "camera_no_clip_proof_log.txt"), sb.ToString()); } catch (System.Exception e) { Debug.LogWarning("[OzShuttleCamFadeProof] log not written: " + e.Message); }
            Destroy(gameObject);
        }
    }
}
#endif
