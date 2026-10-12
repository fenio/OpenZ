using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Checks of the camera's lower pan limit (OzCamEdge). Play mode, a level loaded, the Game view at the size under test.
    ///
    /// 1 "rays and clear count": at every HUD scale preset and at the widest and the closest zoom the camera is put on its lower
    ///   limit all along the lower map edge; rays from the bottom screen row (corners, quarter points, middle) and from the row 5 %
    ///   above it must each meet a drawn piece of the surroundings (not the far flat plane under the scene, not nothing). Then every
    ///   building, flag and gun is tested with its real drawn bounds against the real HUD (the HUD's own "is this point over a
    ///   panel" test): can the camera, inside its limits, show all of it clear of every panel?
    /// 2 "picture matrix": screenshots from the game camera on the lower limit (left corner, middle, right corner; widest and
    ///   closest zoom; the HUD scale in use and the largest one), plus one building near the lower edge covered and then moved clear.
    ///
    /// The surroundings have no colliders of their own: for the rays this check gives every drawn mesh under "Environment" (and the
    /// </summary>
    public static class OpenZCamEdgeCheck
    {
        static IEnumerator run;
        static int waitFrame;
        static string OutDir => Path.Combine(OpenZPaths.RepoRoot, "work", "proof", "camera_edge");
        static readonly Vector2[] RayPoints =
        {
            new Vector2(0f, 0f), new Vector2(0.25f, 0f), new Vector2(0.5f, 0f), new Vector2(0.75f, 0f), new Vector2(1f, 0f),
            new Vector2(0f, 0.05f), new Vector2(0.5f, 0.05f), new Vector2(1f, 0.05f),
        };

        [MenuItem("OpenZ/Checks/Camera edge 1: rays and clear count (Play mode)")]
        public static void RunCheck() { Begin(Check()); }

        [MenuItem("OpenZ/Checks/Camera edge 2: picture matrix (Play mode)")]
        public static void RunPictures() { Begin(Pictures()); }

        static void Begin(IEnumerator e)
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[OpenZ CamEdge] enter Play mode in a level first"); return; }
            if (run != null) { Debug.LogWarning("[OpenZ CamEdge] a run is still going"); return; }
            run = e; waitFrame = 0; EditorApplication.update += Step;
        }

        static void Step()
        {
            if (run == null || !EditorApplication.isPlaying) { run = null; EditorApplication.update -= Step; return; }
            if (Time.frameCount < waitFrame) return;
            bool more;
            try { more = run.MoveNext(); }
            catch (System.Exception ex) { Debug.LogException(ex); more = false; }
            if (!more) { run = null; EditorApplication.update -= Step; return; }
            waitFrame = Time.frameCount + (run.Current is int n ? n : 1);
        }

        class Subject { public string name; public Bounds box; public ZObject obj; }

        static List<Subject> Subjects(ZGame g)
        {
            var list = new List<Subject>();
            foreach (var o in g.World.Objects)
            {
                if (o == null || o.Type == null) continue;
                bool site = (o.Kind == ZKind.Building && !o.Type.StartsWith("bridge")) || (o.Kind == ZKind.Cannon && !o.Destroyed) || (o.Kind == ZKind.MapItem && o.Type == "flag");
                if (!site) continue;
                var fmin = new Vector3(o.LocX / 8f, 0f, -(o.LocY + o.HeightPix) / 8f);
                var fmax = new Vector3((o.LocX + o.WidthPix) / 8f, 3f, -o.LocY / 8f);
                var box = new Bounds((fmin + fmax) * 0.5f, fmax - fmin);
                // the drawn bounds, kept to the footprint + 2 m and 25 m of height (effects and long attachments are not the building)
                if (g.Views.TryGetValue(o.Id, out var v) && v)
                {
                    bool has = false; var rb = new Bounds();
                    foreach (var r in v.GetComponentsInChildren<Renderer>())
                    {
                        if (!r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                        if (!has) { rb = r.bounds; has = true; } else rb.Encapsulate(r.bounds);
                    }
                    if (has)
                    {
                        var lo = Vector3.Max(rb.min, new Vector3(fmin.x - 2f, 0f, fmin.z - 2f));
                        var hi = Vector3.Min(rb.max, new Vector3(fmax.x + 2f, 25f, fmax.z + 2f));
                        if (hi.x > lo.x && hi.y > lo.y && hi.z > lo.z) { box = new Bounds((lo + hi) * 0.5f, hi - lo); box.Encapsulate(new Bounds((fmin + fmax) * 0.5f, new Vector3(fmax.x - fmin.x, 0.1f, fmax.z - fmin.z))); }
                    }
                }
                list.Add(new Subject { name = $"{o.Owner} {o.Type} ({o.LocX / 16},{o.LocY / 16})", box = box, obj = o });
            }
            return list;
        }

        /// <summary>The whole box on screen and no sampled point of its screen rectangle over a HUD panel.</summary>
        static bool ClearNow(Camera cam, OpenZHud hud, Bounds b)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var p = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                var s = cam.WorldToScreenPoint(p);
                if (s.z <= 0f) return false;
                x0 = Mathf.Min(x0, s.x); x1 = Mathf.Max(x1, s.x); y0 = Mathf.Min(y0, s.y); y1 = Mathf.Max(y1, s.y);
            }
            if (x0 < 0f || y0 < 0f || x1 > cam.pixelWidth || y1 > cam.pixelHeight) return false;
            const int N = 6;
            for (int ix = 0; ix <= N; ix++)
                for (int iy = 0; iy <= N; iy++)
                    if (hud.IsOverUi(new Vector2(Mathf.Lerp(x0, x1, ix / (float)N), Mathf.Lerp(y0, y1, iy / (float)N)))) return false;
            return true;
        }

        static void Pose(RtsCamera rts, float x, float z, float h) { rts.focus = new Vector3(x, 0f, z); rts.SetHeightImmediate(h); }

        /// <summary>0 clear inside the limits; 1 clear only past the lower limit (east-west inside the limits); 2 clear only past another
        /// limit; 3 clear nowhere (it does not fit the free part of the screen at this zoom). pose = a camera focus that shows it clear.</summary>
        static int Classify(RtsCamera rts, Camera cam, OpenZHud hud, Bounds b, float h, out Vector3 pose)
        {
            const int N = 28;
            float cx = b.center.x, zLo = b.min.z - 0.56f * h, zHi = b.max.z + 0.38f * h, xLo = cx - 0.75f * h, xHi = cx + 0.75f * h;
            rts.unclamped = false;
            Pose(rts, cx, b.center.z, h); pose = rts.focus;
            if (ClearNow(cam, hud, b)) return 0;
            for (int iz = 0; iz <= N; iz++)
                for (int ix = 0; ix <= N; ix++)
                {
                    Pose(rts, Mathf.Lerp(xLo, xHi, ix / (float)N), Mathf.Lerp(zHi, zLo, iz / (float)N), h);
                    if (ClearNow(cam, hud, b)) { pose = rts.focus; return 0; }
                }
            // the limits of this zoom, read back from the camera itself
            Pose(rts, -99999f, -99999f, h); float X0 = rts.focus.x;
            Pose(rts, 99999f, 99999f, h); float X1 = rts.focus.x, Z1 = rts.focus.z;
            int result = 3;
            rts.unclamped = true;
            for (int iz = 0; iz <= N && result != 1; iz++)
                for (int ix = 0; ix <= N; ix++)
                {
                    float x = Mathf.Lerp(xLo, xHi, ix / (float)N), z = Mathf.Lerp(zHi, zLo, iz / (float)N);
                    Pose(rts, x, z, h);
                    if (!ClearNow(cam, hud, b)) continue;
                    if (x >= X0 - 0.01f && x <= X1 + 0.01f && z <= Z1 + 0.01f && z >= b.min.z) { result = 1; pose = rts.focus; break; }
                    if (result == 3) { result = 2; pose = rts.focus; }
                }
            rts.unclamped = false;
            return result;
        }

        /// <summary>Name of the temporary objects that carry the scene's combined meshes (the statically batched scenery).</summary>
        const string BatchName = "STATIC_SCENERY";
        static readonly List<Mesh> copies = new List<Mesh>();

        /// <summary>A mesh the physics engine can cook here: the mesh itself, or a readable copy of it (the asset is not touched).</summary>
        static Mesh Readable(Mesh m)
        {
            if (m.isReadable) return m;
            var copy = Object.Instantiate(m);
            var so = new SerializedObject(copy); var p = so.FindProperty("m_IsReadable");
            if (p != null) { p.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); }
            copies.Add(copy);
            return copy;
        }

        static List<MeshCollider> AddColliders()
        {
            var made = new List<MeshCollider>();
            var env = GameObject.Find("Environment");
            var combined = new HashSet<Mesh>();
            if (env)
                foreach (var mf in env.GetComponentsInChildren<MeshFilter>(false))
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (!mr || !mr.enabled || !mf.sharedMesh || mf.GetComponent<Collider>()) continue;
                    if (mr.isPartOfStaticBatch)
                    {   // in Play mode the filter of a statically batched piece holds the scene's combined mesh, which is in world
                        // space: one collider per combined mesh on an object of its own (on the piece it would be turned and moved)
                        if (!combined.Add(mf.sharedMesh)) continue;
                        var go = new GameObject(BatchName) { hideFlags = HideFlags.HideAndDontSave };
                        var bc = go.AddComponent<MeshCollider>(); bc.sharedMesh = Readable(mf.sharedMesh); made.Add(bc);
                        continue;
                    }
                    var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = Readable(mf.sharedMesh); made.Add(mc);
                }
            var skirt = GameObject.Find("SKIRT");
            if (skirt && !skirt.GetComponent<Collider>())
            {
                var mf = skirt.GetComponent<MeshFilter>();
                if (mf && mf.sharedMesh) { var mc = skirt.AddComponent<MeshCollider>(); mc.sharedMesh = Readable(mf.sharedMesh); made.Add(mc); }
            }
            Physics.SyncTransforms();
            return made;
        }

        /// <summary>Takes the temporary colliders, their objects and the mesh copies away again.</summary>
        static void RemoveColliders(List<MeshCollider> made)
        {
            foreach (var mc in made)
            {
                if (!mc) continue;
                if (mc.gameObject.name == BatchName) Object.Destroy(mc.gameObject); else Object.Destroy(mc);
            }
            foreach (var m in copies) if (m) Object.Destroy(m);
            copies.Clear();
        }

        /// <summary>What the ray meets first among DRAWN things: "" = nothing, else the object's name.</summary>
        static string FirstDrawn(Ray ray)
        {
            var hits = Physics.RaycastAll(ray, 5000f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; string name = "";
            foreach (var h in hits)
            {
                if (h.distance >= best) continue;
                if (h.collider.name != BatchName)
                {
                    var mr = h.collider.GetComponent<MeshRenderer>();
                    if (!mr || !mr.enabled) continue;
                }
                best = h.distance; name = h.collider.name;
            }
            return name;
        }

        static IEnumerator Check()
        {
            var g = ZGame.I; var hud = OpenZHud.I; var rts = Object.FindFirstObjectByType<RtsCamera>();
            if (g == null || g.World == null || hud == null || rts == null) { Debug.LogWarning("[OpenZ CamEdge] no game / HUD / camera in this scene"); yield break; }
            var cam = rts.GetComponent<Camera>();
            Directory.CreateDirectory(OutDir);
            float scale0 = OzText.UiScale, h0 = rts.height; Vector3 focus0 = rts.focus; var follow0 = rts.follow; rts.follow = null;
            var temp = AddColliders();
            var subjects = Subjects(g);
            var csv = new StringBuilder("level;screen;hud scale;zoom height;what;name;result;detail\n");
            string screen = cam.pixelWidth + "x" + cam.pixelHeight;
            int rays = 0, voidRays = 0, planeRays = 0, clear = 0, noFit = 0, capped = 0, southFail = 0, otherEdge = 0;
            var met = new Dictionary<string, int>();
            float mapX = rts.mapSizeMeters.x;
            foreach (float s in OzText.ScalePresets)
            {
                OzText.UiScale = s; hud.ApplyUiScale();
                yield return 3;
                Canvas.ForceUpdateCanvases();
                foreach (float h in new[] { rts.maxHeight, rts.minHeight })
                {
                    float side = Mathf.Min(h * 0.3f, mapX * 0.45f);
                    float farthest = -999f;
                    for (int i = 0; i <= 12; i++)
                    {
                        OzCamEdge.Snap(); rts.unclamped = false;
                        Pose(rts, Mathf.Lerp(side, mapX - side, i / 12f), -99999f, h);
                        farthest = Mathf.Max(farthest, OzCamEdge.Reach);
                        foreach (var vp in RayPoints)
                        {
                            rays++;
                            string name = FirstDrawn(cam.ViewportPointToRay(new Vector3(vp.x, vp.y, 0f)));
                            if (name == "") { voidRays++; csv.Append($"{g.level};{screen};{s:0.00};{h:0};ray;x {rts.focus.x:0.0} view {vp.x:0.00},{vp.y:0.00};NOTHING;\n"); }
                            else if (name == "SKIRT") { planeRays++; csv.Append($"{g.level};{screen};{s:0.00};{h:0};ray;x {rts.focus.x:0.0} view {vp.x:0.00},{vp.y:0.00};FAR PLANE;\n"); }
                            else { met.TryGetValue(name, out int c); met[name] = c + 1; }
                        }
                    }
                    csv.Append($"{g.level};{screen};{s:0.00};{h:0};limit;;south {OzCamEdge.SouthNow:0.0} m;bottom row {farthest:0.0} m past the lower edge, cap holds: {OzCamEdge.CapHolds}\n");
                    foreach (var sub in subjects)
                    {
                        OzCamEdge.Snap();
                        int r = Classify(rts, cam, hud, sub.box, h, out _);
                        string word;
                        if (r == 0) { clear++; word = "clear"; }
                        else if (r == 3) { noFit++; word = "does not fit the free screen at this zoom"; }
                        else if (r == 2) { otherEdge++; word = "another edge"; }
                        else
                        {
                            OzCamEdge.Snap(); Pose(rts, sub.box.center.x, -99999f, h);
                            if (OzCamEdge.CapHolds) { capped++; word = "lower edge: held by the scenery cap (clear at a closer zoom)"; }
                            else { southFail++; word = "LOWER EDGE: NOT FREED"; }
                        }
                        csv.Append($"{g.level};{screen};{s:0.00};{h:0};object;{sub.name};{word};\n");
                    }
                    yield return 1;
                }
            }
            RemoveColliders(temp);
            OzText.UiScale = scale0; hud.ApplyUiScale(); rts.unclamped = false; OzCamEdge.Snap(); rts.follow = follow0; Pose(rts, focus0.x, focus0.z, h0);
            var sb = new StringBuilder();
            foreach (var kv in met) sb.Append(kv.Key).Append(' ').Append(kv.Value).Append(", ");
            bool pass = voidRays == 0 && planeRays == 0 && southFail == 0;
            string summary = $"[OpenZ CamEdge] {g.level} {screen}: {(pass ? "PASS" : "FAIL")} | rays {rays}: into nothing {voidRays}, onto the far plane {planeRays} | objects x scales x zooms: clear {clear}, "
                             + $"do not fit at that zoom {noFit}, held by the scenery cap {capped}, another edge {otherEdge}, LOWER EDGE NOT FREED {southFail} | the rays met: {sb}";
            csv.Append(summary).Append('\n');
            string path = Path.Combine(OutDir, $"camera_edge_{g.level}_{screen}.csv");
            File.WriteAllText(path, csv.ToString());
            Debug.Log(summary + "\n" + path);
        }

        static IEnumerator Pictures()
        {
            var g = ZGame.I; var hud = OpenZHud.I; var rts = Object.FindFirstObjectByType<RtsCamera>();
            if (g == null || g.World == null || hud == null || rts == null) { Debug.LogWarning("[OpenZ CamEdge] no game / HUD / camera in this scene"); yield break; }
            var cam = rts.GetComponent<Camera>();
            Directory.CreateDirectory(OutDir);
            float scale0 = OzText.UiScale, h0 = rts.height; Vector3 focus0 = rts.focus; var follow0 = rts.follow; rts.follow = null;
            var dn = Object.FindFirstObjectByType<DayNight>();
            string light = dn ? "t" + Mathf.RoundToInt(dn.timeOfDay * 100f).ToString("000") : "t";
            string screen = cam.pixelWidth + "x" + cam.pixelHeight;
            float mapX = rts.mapSizeMeters.x; int shots = 0;
            foreach (float s in new[] { scale0, OzText.ScaleMax })
            {
                OzText.UiScale = s; hud.ApplyUiScale();
                yield return 3;
                foreach (float h in new[] { rts.maxHeight, rts.minHeight })
                {
                    float side = Mathf.Min(h * 0.3f, mapX * 0.45f);
                    string[] spot = { "left", "middle", "right" }; float[] xs = { side, mapX * 0.5f, mapX - side };
                    for (int i = 0; i < 3; i++)
                    {
                        OzCamEdge.Snap(); rts.unclamped = false; Pose(rts, xs[i], -99999f, h);
                        yield return 4;
                        ScreenCapture.CaptureScreenshot(Path.Combine(OutDir, $"{g.level}_{light}_{screen}_scale{Mathf.RoundToInt(s * 100)}_h{h:0}_{spot[i]}.png")); shots++;
                        yield return 3;
                    }
                }
            }
            // a building near the lower edge: first as the old limit leaves it at the widest zoom (the camera on the old limit), then moved clear
            OzText.UiScale = Mathf.Clamp(1.15f, OzText.ScaleMin, OzText.ScaleMax); hud.ApplyUiScale();
            yield return 3;
            Subject pick = null; float bestZ = float.MaxValue;
            foreach (var sub in Subjects(g))
                if (sub.obj.Kind == ZKind.Building && sub.obj.Type.EndsWith("factory") && sub.box.center.x > mapX * 0.5f && sub.box.min.z < bestZ) { bestZ = sub.box.min.z; pick = sub; }
            if (pick != null)
            {
                float h = rts.maxHeight;
                OzCamEdge.Snap(); OzCamEdge.Enabled = false; Pose(rts, pick.box.center.x, -99999f, h);
                yield return 4;
                ScreenCapture.CaptureScreenshot(Path.Combine(OutDir, $"{g.level}_{light}_{screen}_scale115_factory_old_limit.png")); shots++;
                yield return 3;
                OzCamEdge.Enabled = true; OzCamEdge.Snap();
                int r = Classify(rts, cam, hud, pick.box, h, out var pose);
                rts.unclamped = false; OzCamEdge.Snap(); Pose(rts, pose.x, pose.z, h);
                yield return 4;
                ScreenCapture.CaptureScreenshot(Path.Combine(OutDir, $"{g.level}_{light}_{screen}_scale115_factory_moved_clear_result{r}.png")); shots++;
                yield return 3;
            }
            OzText.UiScale = scale0; hud.ApplyUiScale(); rts.unclamped = false; OzCamEdge.Enabled = true; OzCamEdge.Snap(); rts.follow = follow0; Pose(rts, focus0.x, focus0.z, h0);
            Debug.Log($"[OpenZ CamEdge] {g.level}: {shots} pictures in {OutDir}");
        }
    }
}
