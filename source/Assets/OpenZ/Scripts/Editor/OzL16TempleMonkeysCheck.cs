using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted Play checks of the monkeys on level 16's two pyramids (JungleFauna.Temple.cs), editor only. Each menu runs by itself
    /// while level 16 plays (by day), moves the normal game camera and saves its report and pictures to
    ///   Temple monkeys 1: placed, on stone, apart, pictures   7 monkeys (4 + 3); feet 0 to 3 cm over the pyramid's own mesh, nothing of
    ///                                                          the stone inside a monkey, the laid tail on the stone, 1 m apart, not
    ///                                                          on the top; pictures of each pyramid at 70 m and 35 m
    ///   Temple monkeys 2: night, away and back                 the clock sped to the night: all 7 leave and are not drawn; the clock
    ///                                                          put back to the day: all 7 sit on their seats again
    ///   Temple monkeys 3: a blast, they run and come back      a blast 8 m from the big pyramid: its 4 stand up, run their ways on the
    ///                                                          stone (sampled every frame), hide; the small one's 3 stay; back later
    /// The stone is measured with a temporary collider of the pyramid's drawn mesh (made for the check, removed at its end).
    /// Game speed 1 while a check runs (the animals run on the frame clock), put back at its end.
    /// </summary>
    public static class OzL16TempleMonkeysCheck
    {
        static string Folder { get { var p = Path.Combine(OpenZPaths.Records, "milestones", "v1_1_20261012", "k417"); Directory.CreateDirectory(p); return p; } }
        static IEnumerator run; static string running = ""; static float speedWas = -1f; static int lastFrame = -1;
        static readonly List<GameObject> probes = new List<GameObject>();

        static void Begin(string name, IEnumerator e)
        {
            if (!Application.isPlaying || !ZGame.I || ZGame.I.World == null || ZGame.I.level != "L16") { Debug.LogWarning("[OpenZ k417] " + name + ": start Play on level 16 first"); return; }
            if (run != null) { Debug.LogWarning("[OpenZ k417] " + running + " is still running: one check at a time"); return; }
            run = e; running = name; speedWas = ZGame.I.gameSpeed; ZGame.I.gameSpeed = 1f;
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Debug.Log("[OpenZ k417] " + name + ": started");
        }

        static void Tick()
        {
            if (!Application.isPlaying) { run = null; EditorApplication.update -= Tick; speedWas = -1f; probes.Clear(); return; }
            if (Time.frameCount == lastFrame) return; lastFrame = Time.frameCount;
            bool more;
            try { more = run != null && run.MoveNext(); }
            catch (System.Exception ex) { Debug.LogError("[OpenZ k417] " + running + " threw " + ex); more = false; }
            if (!more)
            {
                run = null; EditorApplication.update -= Tick;
                if (speedWas > 0f && ZGame.I) ZGame.I.gameSpeed = speedWas; speedWas = -1f;
                foreach (var p in probes) if (p) Object.Destroy(p); probes.Clear();
            }
        }

        sealed class Report
        {
            readonly System.Text.StringBuilder sb = new System.Text.StringBuilder(); int pass, lines; readonly string name;
            public Report(string name) { this.name = name; sb.AppendLine(name + " on L16, " + Screen.width + " x " + Screen.height + ", night " + DetailKit.Night.ToString("0.00") + ", " + JungleFauna.TempleStatus()); }
            public void V(bool ok, string what) { lines++; if (ok) pass++; sb.AppendLine((ok ? "PASS  " : "FAIL  ") + what); }
            public void Note(string what) => sb.AppendLine(what);
            public void End()
            {
                sb.AppendLine(JungleFauna.TempleStatus());
                sb.Append(pass == lines ? $"PASS {pass}/{lines}" : $"FAIL {pass}/{lines}");
                string path = Path.Combine(Folder, name.Replace(' ', '_').Replace(':', '_').Replace(',', '_') + ".txt");
                File.WriteAllText(path, sb.ToString());
                if (pass == lines) Debug.Log("[OpenZ k417] " + sb + "\n-> " + path); else Debug.LogError("[OpenZ k417] " + sb + "\n-> " + path);
            }
        }

        static IEnumerable WaitReal(float s) { float end = Time.unscaledTime + s; while (Time.unscaledTime < end) yield return null; }
        static void Shot(string n) => ScreenCapture.CaptureScreenshot(Path.Combine(Folder, n + ".png"));
        static void Cam(Vector3 at, float h) { var rts = Object.FindFirstObjectByType<RtsCamera>(); if (rts) { rts.focus = new Vector3(at.x, 0f, at.z); rts.SetHeightImmediate(h); } }

        static L16Pyramid Temple(int k)
        {
            foreach (var p in L16Pyramid.All) if (p && p.name == JungleTempleMonkeysData.Uids[k]) return p;
            return null;
        }

        /// <summary>A temporary collider of the pyramid's drawn intact mesh (the check measures the stone on it).</summary>
        static MeshCollider Probe(L16Pyramid p)
        {
            var lod = p ? p.transform.Find("Stages/Intact/LOD0") : null; var mf = lod ? lod.GetComponent<MeshFilter>() : null;
            if (!mf || !mf.sharedMesh) return null;
            var go = new GameObject("k417_stone_probe") { hideFlags = HideFlags.DontSave };
            go.transform.SetPositionAndRotation(lod.position, lod.rotation); go.transform.localScale = lod.lossyScale;
            var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; probes.Add(go);
            Physics.SyncTransforms();
            return mc;
        }

        static float Stone(MeshCollider mc, Vector3 p)
        {
            if (!mc) return float.NaN;
            return mc.Raycast(new Ray(new Vector3(p.x, p.y + 2.5f, p.z), Vector3.down), out var hit, 6f) ? hit.point.y : float.NaN;
        }

        static readonly List<Vector3> pos = new List<Vector3>(), seat = new List<Vector3>(); static readonly List<int> st = new List<int>(), tk = new List<int>(); static readonly List<bool> drawn = new List<bool>();
        static void Read() => JungleFauna.TempleMonkeyList(pos, st, tk, drawn, seat);

        // ================================================================== 1
        [MenuItem("OpenZ/L16 Jungle/Checks/Temple monkeys 1: placed, on stone, apart, pictures")]
        static void One() => Begin("Temple monkeys 1 placed on stone apart pictures", OneBody());

        static IEnumerator OneBody()
        {
            var r = new Report("Temple monkeys 1 placed on stone apart pictures");
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 8f) { Read(); int s = 0; foreach (var x in st) if (x == 0) s++; if (s == pos.Count && pos.Count > 0) break; yield return null; }
            Read();
            r.V(!OzJungleLife.Hushed, "it is day and dry (the monkeys are away at night and in rain): night " + DetailKit.Night.ToString("0.00"));
            int big = 0, small = 0; foreach (var k in tk) if (k == 0) big++; else small++;
            r.V(pos.Count == 7 && big == 4 && small == 3, $"monkeys placed: {pos.Count} (big pyramid {big}, small pyramid {small}; want 7 = 4 + 3)");
            int sit = 0, dr = 0; for (int i = 0; i < st.Count; i++) { if (st[i] == 0) sit++; if (drawn[i]) dr++; }
            r.V(sit == pos.Count && dr == pos.Count, $"sitting {sit}, drawn {dr} of {pos.Count}");
            var mc = new[] { Probe(Temple(0)), Probe(Temple(1)) };
            r.V(mc[0] && mc[1], "the stone probes of both pyramids made (their drawn intact mesh)");
            int feetOk = 0, inside = 0, tailOk = 0, topOk = 0; float gMin = 9f, gMax = -9f, worstIn = 0f;
            for (int i = 0; i < pos.Count; i++)
            {
                var root = JungleFauna.TempleMonkeyRoot(i); if (!root) continue;
                float lo = 9f, hi = -9f; bool okIn = true;
                foreach (var c in JungleTempleMonkeysData.Underside)
                {
                    var wp = root.TransformPoint(c); float s = Stone(mc[tk[i]], wp); if (float.IsNaN(s)) continue;
                    float gap = wp.y - s;
                    if (gap < -0.01f) { okIn = false; worstIn = Mathf.Min(worstIn, gap); }
                    if (c.y <= 0.012f && c.z > 0f && Mathf.Abs(c.x) < 0.3f) { lo = Mathf.Min(lo, gap); hi = Mathf.Max(hi, gap); }
                }
                bool feet = lo >= -0.005f && hi <= 0.03f; if (feet) feetOk++; if (!okIn) inside++;
                gMin = Mathf.Min(gMin, lo); gMax = Mathf.Max(gMax, hi);
                var t5 = L16AnimalKit.Find(root, "tail_5"); var t3 = L16AnimalKit.Find(root, "tail_3");
                float h5 = t5 ? t5.position.y - Stone(mc[tk[i]], t5.position) : 9f, h3 = t3 ? t3.position.y - Stone(mc[tk[i]], t3.position) : 9f;
                bool tail = h5 > -0.01f && h5 < 0.10f && h3 > -0.01f && h3 < 0.12f; if (tail) tailOk++;
                var tp = Temple(tk[i]); float ly = tp ? tp.transform.InverseTransformPoint(pos[i]).y : 99f, scale = tk[i] == 0 ? 1f : 0.75f;
                bool low = ly < 5.0f * scale; if (low) topOk++;
                r.Note($"  {JungleTempleMonkeysData.Names[i]}: pyramid {(tk[i] == 0 ? "big" : "small")}, tier {JungleTempleMonkeysData.Tier[i]}, at ({pos[i].x:0.00}, {pos[i].y:0.000}, {pos[i].z:0.00}), {ly:0.00} m over the pyramid's foot; feet over the stone {lo * 100f:0.0} to {hi * 100f:0.0} cm; tail bones 3 / 5 over the stone {h3 * 100f:0} / {h5 * 100f:0} cm");
            }
            r.V(feetOk == pos.Count, $"feet on the stone (0 to 3 cm, measured on the drawn mesh) for {feetOk} of {pos.Count} (all: {gMin * 100f:0.0} to {gMax * 100f:0.0} cm)");
            r.V(inside == 0, $"nothing of the stone inside a monkey (its underside, 196 cells, 1 cm allowed): {inside} with stone inside (worst {worstIn * 100f:0.0} cm)");
            r.V(tailOk == pos.Count, $"the tail lies on the stone (bones 3 and 5 within 10 to 12 cm over it, not in it) for {tailOk} of {pos.Count}");
            float minD = 99f; for (int i = 0; i < pos.Count; i++) for (int j = i + 1; j < pos.Count; j++) minD = Mathf.Min(minD, Vector3.Distance(pos[i], pos[j]));
            r.V(minD >= 1f, $"at least 1 m apart: the nearest two {minD:0.00} m");
            r.V(topOk == pos.Count, $"not on the top (tiers 1 to 3, under 5 m x the pyramid's size over its foot; the platform is at 5.9): {topOk} of {pos.Count}");
            // props and plants of the scene near a seat (the pyramid's own plants were measured on its mesh): renderer boxes within 1 m
            int near = 0; var names = new System.Text.StringBuilder();
            foreach (var rd in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!rd.enabled || rd.bounds.size.magnitude > 12f) continue;                         // ground, water and the pyramids themselves
                var n = rd.transform; bool skip = false;
                for (var q = n; q; q = q.parent) if (q.name.StartsWith("L16_Pyramid") || q.name == "JungleFauna" || q.name.StartsWith("TempleMonkey")) { skip = true; break; }
                if (skip) continue;
                for (int i = 0; i < pos.Count; i++)
                    if (rd.bounds.SqrDistance(pos[i]) < 1f) { near++; if (names.Length < 400) names.Append(rd.name).Append(" (").Append(JungleFauna.TempleMonkeyRoot(i) ? JungleTempleMonkeysData.Names[i] : "?").Append(") "); }
            }
            r.V(near == 0, $"no prop or plant of the scene within 1 m of a monkey (renderer boxes): {near} {names}");
            foreach (var p in probes) if (p) Object.Destroy(p); probes.Clear();
            // pictures: the standard zoom (70 m) and 35 m, each pyramid
            for (int k = 0; k < 2; k++)
            {
                var tp = Temple(k); if (!tp) continue;
                foreach (var h in new[] { 70f, 35f })
                {
                    Cam(tp.transform.position, h); foreach (var _ in WaitReal(0.8f)) yield return null;
                    Shot($"k417_{(k == 0 ? "big" : "small")}_pyramid_h{h:0}_day"); foreach (var _ in WaitReal(0.4f)) yield return null;
                }
            }
            r.Note("pictures: k417_big_pyramid_h70_day, _h35_day, k417_small_pyramid_h70_day, _h35_day (look at each: monkeys on the steps, not on top, not floating, tail on the stone)");
            r.End();
        }

        // ================================================================== 2
        [MenuItem("OpenZ/L16 Jungle/Checks/Temple monkeys 2: night, away and back")]
        static void Two() => Begin("Temple monkeys 2 night away and back", TwoBody());

        static IEnumerator TwoBody()
        {
            var r = new Report("Temple monkeys 2 night away and back");
            var dn = Object.FindFirstObjectByType<DayNight>();
            if (!dn) { r.V(false, "the day clock (DayNight) is in the scene"); r.End(); yield break; }
            bool run0 = dn.running; float cyc0 = dn.cycleSeconds, clock0 = dn.timeOfDay;
            Read(); int n = pos.Count; int leaves0 = JungleFauna.TempleQuietLeaves;
            r.V(n == 7 && !OzJungleLife.Hushed, $"it starts by day with {n} monkeys on the pyramids");
            dn.running = true; dn.cycleSeconds = 36f;
            float t0 = Time.unscaledTime; while (!OzJungleLife.Hushed && Time.unscaledTime - t0 < 45f) yield return null;
            dn.cycleSeconds = cyc0;
            r.V(OzJungleLife.Hushed, $"the clock sped up reached the night (night {DetailKit.Night:0.00}) after {Time.unscaledTime - t0:0.0} s");
            t0 = Time.unscaledTime; int gone = 0;
            while (Time.unscaledTime - t0 < 30f) { Read(); gone = 0; for (int i = 0; i < n; i++) if (st[i] == 3 && !drawn[i]) gone++; if (gone == n) break; yield return null; }
            r.V(gone == n, $"at night all leave along their ways and are not drawn: {gone} of {n} after {Time.unscaledTime - t0:0.0} s; quiet leaves {JungleFauna.TempleQuietLeaves - leaves0}");
            for (int k = 0; k < 2; k++) { var tp = Temple(k); if (!tp) continue; Cam(tp.transform.position, 70f); foreach (var _ in WaitReal(0.8f)) yield return null; Shot($"k417_{(k == 0 ? "big" : "small")}_pyramid_h70_night_empty"); foreach (var _ in WaitReal(0.4f)) yield return null; }
            dn.timeOfDay = clock0; dn.running = run0;
            t0 = Time.unscaledTime; while (OzJungleLife.Hushed && Time.unscaledTime - t0 < 30f) yield return null;
            r.V(!OzJungleLife.Hushed, $"the clock put back to the day (t {clock0:0.000}): day again after {Time.unscaledTime - t0:0.0} s");
            t0 = Time.unscaledTime; int back = 0; float worst = 0f;
            while (Time.unscaledTime - t0 < 60f) { Read(); back = 0; for (int i = 0; i < n; i++) if (st[i] == 0 && drawn[i]) back++; if (back == n) break; yield return null; }
            Read(); for (int i = 0; i < n; i++) worst = Mathf.Max(worst, Vector3.Distance(pos[i], seat[i]));
            r.V(back == n && worst < 0.02f, $"by day all come back and sit on their own seats: {back} of {n} after {Time.unscaledTime - t0:0.0} s, farthest from its seat {worst * 100f:0.0} cm");
            dn.running = run0; dn.cycleSeconds = cyc0;
            r.End();
        }

        // ================================================================== 3
        [MenuItem("OpenZ/L16 Jungle/Checks/Temple monkeys 3: a blast, they run and come back")]
        static void Three() => Begin("Temple monkeys 3 a blast they run and come back", ThreeBody());

        static IEnumerator ThreeBody()
        {
            var r = new Report("Temple monkeys 3 a blast they run and come back");
            var big = Temple(0); Read(); int n = pos.Count;
            if (!big || n != 7) { r.V(false, $"the big pyramid and 7 monkeys are there ({n})"); r.End(); yield break; }
            Cam(big.transform.position, 35f); foreach (var _ in WaitReal(0.8f)) yield return null;
            var mc = Probe(big);
            var at = big.transform.TransformPoint(new Vector3(-15f, 0f, 0.5f)); at.y = big.transform.position.y;   // 8 m west of the west seats, over the pool
            int flights0 = JungleFauna.TempleFlights, hops0 = JungleFauna.TempleHops;
            JungleFauna.TestBlast(at);
            float t0 = Time.unscaledTime; int samples = 0, bad = 0; float worst = 0f; bool shot = false; int gone = 0, alarmed = 0;
            var seenAlarm = new HashSet<int>();
            while (Time.unscaledTime - t0 < 25f)
            {
                Read(); gone = 0;
                for (int i = 0; i < n; i++)
                {
                    if (tk[i] != 0) continue;
                    if (st[i] == 1 || st[i] == 2) seenAlarm.Add(i);
                    if (st[i] == 3 && !drawn[i]) gone++;
                    if (st[i] == 2 && drawn[i])
                    {   // running: feet on the stone (the run take stands on its root); a hop: over it
                        float s = Stone(mc, pos[i]); if (float.IsNaN(s)) continue;
                        float g = pos[i].y - s; samples++;
                        if (g < -0.03f) { bad++; worst = Mathf.Min(worst, g); }
                    }
                }
                if (!shot && Time.unscaledTime - t0 > 1.6f) { Shot("k417_big_pyramid_h35_fleeing"); shot = true; }
                if (gone == 4) break;
                yield return null;
            }
            alarmed = seenAlarm.Count;
            r.V(alarmed == 4 && JungleFauna.TempleFlights > flights0, $"a blast 8 m west of the big pyramid's seats: {alarmed} of 4 stood up and ran (frights {JungleFauna.TempleFlights - flights0})");
            r.V(samples > 20 && bad == 0, $"on their ways never inside the stone: {samples} samples, {bad} more than 3 cm in (worst {worst * 100f:0.0} cm); hops {JungleFauna.TempleHops - hops0}");
            r.V(gone == 4, $"all 4 hid behind the pyramid and are not drawn after {Time.unscaledTime - t0:0.0} s");
            int smallSit = 0; for (int i = 0; i < n; i++) if (tk[i] == 1 && st[i] == 0) smallSit++;
            r.V(smallSit == 3, $"the small pyramid's 3 (far from the blast) stay sitting: {smallSit}");
            t0 = Time.unscaledTime; int back = 0; float worstSeat = 0f;
            while (Time.unscaledTime - t0 < 120f) { Read(); back = 0; for (int i = 0; i < n; i++) if (tk[i] == 0 && st[i] == 0 && drawn[i]) back++; if (back == 4) break; yield return null; }
            Read(); for (int i = 0; i < n; i++) if (tk[i] == 0) worstSeat = Mathf.Max(worstSeat, Vector3.Distance(pos[i], seat[i]));
            r.V(back == 4 && worstSeat < 0.02f, $"they come back the same way after the quiet time (40 to 70 s) and sit on their seats: {back} of 4 after {Time.unscaledTime - t0:0.0} s, farthest {worstSeat * 100f:0.0} cm");
            r.Note("a unit of the running match within 14 m of a seat keeps them away longer: note it if the last line waited out its 120 s");
            r.End();
        }
    }
}
