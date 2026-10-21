using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Version 1.1 kit K331 checks (menu OpenZ/V1.1/K331 ...), item 331: the fort cannon, its pieces and the rocks.
    /// <list type="bullet">
    /// <item>Toss Into The Rocks (Play): the standing fort cannon nearest the view centre is turned so that its drawn landing place (the
    /// toss's own numbers: its heading + the fort's fixed offset and distance) lies on the nearest rock round its fort, then tossed (view
    /// only). Prints where it really landed (moved to free ground) and, when every piece rests (or after 40 s): each piece (kind, size,
    /// where it rests, its rock contacts), the pieces lying in a rock (a collider more than 5 cm in a rock, or its middle inside a rock's
    /// drawn shape: must be 0), the rock contacts, and the rock solid's state. Pictures from the game camera's angle and distance: the
    /// </item>
    /// <item>Rock Under A Resting Piece Shows Its Stump (Play, after the toss): the rock a piece rests on or leans against shows its stump
    /// (RockPillar.Damage, view only); the piece must wake, fall and rest again, never in the rock. Pictures before and after.</item>
    /// <item>State: one line of the rock solid.</item>
    /// </list>
    /// Test tools only: nothing is saved.
    /// </summary>
    public static class OzK331RockCheck
    {
        const string M = "OpenZ/V1.1/K331 ";
        /// <summary>A resting piece may lie at most this much (m) in a rock collider (the contact skin of the physics).</summary>
        const float MaxDepth = 0.05f;
        static string Out { get { var p = Path.Combine(OpenZPaths.Records, "milestones", "k331_rocks_check"); Directory.CreateDirectory(p); return p; } }
        enum Mode { None, Toss, Swap }
        static Mode mode; static float until, shotAt1 = -1f, restAt = -1f, swapAt = -1f;
        static int breaks0, touches0, woken0, moved0, kept0, clanks0, crashes0, step;
        static Vector3 impact; static string target = "-", lead = "";
        static float camDist = 45f, camPitch = 55f, camYaw;

        [MenuItem(M + "Toss The Nearest Fort Cannon Into The Rocks (Play)")]
        static void Toss()
        {
            var g = ZGame.I;
            if (!Application.isPlaying || !g) { Debug.LogWarning("[OpenZ K331] enter Play first, the camera on a fort beside rocks"); return; }
            var cam = Camera.main;
            var c0 = cam && g.GroundPoint(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out var gp) ? gp : Vector3.zero;
            if (cam) { camDist = Mathf.Clamp(Vector3.Distance(cam.transform.position, c0), 25f, 90f); camPitch = cam.transform.eulerAngles.x; camYaw = cam.transform.eulerAngles.y; }
            var gun = Object.FindObjectsByType<FortGunView>(FindObjectsSortMode.None).Where(v => v && v.Bound && !v.Tossed && v.Fort != null).OrderBy(v => Vector3.Distance(v.transform.position, c0)).FirstOrDefault();
            if (!gun) { Debug.LogWarning("[OpenZ K331] no standing fort cannon in the scene"); return; }
            var fort = gun.Fort; var c = g.CenterWorld(fort, false);
            float half = Mathf.Max(fort.WidthPix, fort.HeightPix) / 16f;
            var rnd = new System.Random(fort.Id * 7919 + 17);                                           // FortGunView.StartToss's own numbers for this fort
            float off = (float)(rnd.NextDouble() * 140.0 - 70.0), dist = half + 3f + (float)rnd.NextDouble() * 3f;
            RockPillar best = null; float bestErr = float.MaxValue;
            foreach (var rp in Object.FindObjectsByType<RockPillar>(FindObjectsSortMode.None))
            {
                if (!rp || !rp.pillar || !rp.pillar.activeInHierarchy) continue;
                var d = rp.transform.position - c; d.y = 0f;
                float err = Mathf.Abs(d.magnitude - dist);
                if (err < bestErr) { bestErr = err; best = rp; }
            }
            if (!best) { Debug.LogWarning("[OpenZ K331] no rock in this level"); return; }
            var to = best.transform.position - c; to.y = 0f;
            float aim = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg - off;
            var f = typeof(FortGunView).GetField("heading", BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) { Debug.LogWarning("[OpenZ K331] FortGunView has no heading field: cannot aim the drawn landing at a rock"); return; }
            f.SetValue(gun, aim);
            target = $"{best.name} (sim #{best.simId}) at {best.transform.position.x:0.0}, {best.transform.position.z:0.0}: {bestErr:0.0} m off the drawn landing ring ({dist:0.0} m from the fort's middle)";
            Snapshot();
            gun.TestToss();
            lead = $"fort {fort.Type} #{fort.Id}: the cannon's drawn landing turned onto rock {target}; landing: {OzRockSolid.LastLanding}";
            Debug.Log("[OpenZ K331] " + lead);
            Begin(Mode.Toss, 40f);
        }

        [MenuItem(M + "Rock Under A Resting Piece Shows Its Stump (Play, after the toss)")]
        static void Swap()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ K331] enter Play and toss first"); return; }
            var pieces = OzFortGunBreakup.LastPieces.Where(p => p && p.Rested).ToList();
            RockPillar rock = null; OzFortGunBreakup on = null; float bd = float.MaxValue;
            foreach (var p in pieces)
                foreach (var rp in Object.FindObjectsByType<RockPillar>(FindObjectsSortMode.None))
                {
                    if (!rp || !rp.pillar || !rp.pillar.activeInHierarchy) continue;
                    var r = rp.pillar.GetComponent<Renderer>(); if (!r) continue;
                    var b = r.bounds; b.Expand(0.6f);
                    var pb = Bounds(p);
                    if (!b.Intersects(pb)) continue;
                    float d = Vector3.Distance(b.center, pb.center) - (p.RestOn == "on rock" ? 100f : 0f);
                    if (d < bd) { bd = d; rock = rp; on = p; }
                }
            if (!rock) { Debug.LogWarning("[OpenZ K331] no resting piece on or beside a standing rock: toss into the rocks first"); return; }
            Snapshot();
            target = $"{rock.name} (sim #{rock.simId}) under / beside {on.PieceName} ({on.RestOn})";
            Shot("k331_swap_before");
            rock.Damage();                                                                              // view only: the stump (after its own 0-0.25 s delay)
            lead = "the rock " + target + " shows its stump now";
            Debug.Log("[OpenZ K331] " + lead);
            Begin(Mode.Swap, 20f);
        }

        [MenuItem(M + "State")]
        static void State() => Debug.Log("[OpenZ K331] " + OzRockSolid.State + $"; pieces: rock contacts {OzFortGunBreakup.RockTouches}, moved out at the start {OzFortGunBreakup.RockStartMoves}, woken {OzFortGunBreakup.RockWoken}, popped on a rock {OzFortGunBreakup.RockPops}");

        static void Snapshot()
        {
            breaks0 = OzFortGunBreakup.Breaks; touches0 = OzFortGunBreakup.RockTouches; woken0 = OzFortGunBreakup.RockWoken;
            moved0 = OzRockSolid.LandingsMoved; kept0 = OzRockSolid.LandingsKept; clanks0 = OzFortGunBreakup.Clanks; crashes0 = OzFortGunBreakup.Crashes;
        }

        static void Begin(Mode m, float seconds)
        {
            mode = m; step = 0; shotAt1 = -1f; restAt = -1f; swapAt = Time.time;
            until = Time.realtimeSinceStartup + seconds;
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
        }

        static Bounds Bounds(Component c)
        {
            var rs = c.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return new Bounds(c.transform.position, Vector3.zero);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        static void Shot(string name)
        {
            var at = impact != Vector3.zero ? impact : (OzFortGunBreakup.LastPieces.FirstOrDefault(p => p) is OzFortGunBreakup p0 ? p0.transform.position : Vector3.zero);
            try { var path = OpenZShot.Shot(at, camDist, camPitch, camYaw, Path.Combine(Out, name + ".png"), 1920, 1080); Debug.Log("[OpenZ K331] picture " + path); }
            catch (System.Exception e) { Debug.LogWarning("[OpenZ K331] picture " + name + " failed: " + e.Message); }
        }

        static void Tick()
        {
            if (!Application.isPlaying) { EditorApplication.update -= Tick; mode = Mode.None; return; }
            if (mode == Mode.Toss)
            {
                bool broke = OzFortGunBreakup.Breaks > breaks0;
                if (broke && step == 0) { impact = OzFortGunBreakup.LastImpact; Shot("k331_toss_1_landing"); shotAt1 = Time.time + 1f; step = 1; }
                if (step == 1 && Time.time >= shotAt1) { Shot("k331_toss_2_plus1s"); step = 2; }
                bool done = broke && step >= 2 && OzFortGunBreakup.LastAllRested();
                if (done && restAt < 0f) restAt = Time.time + 0.5f;
                if ((restAt >= 0f && Time.time >= restAt) || Time.realtimeSinceStartup > until)
                {
                    EditorApplication.update -= Tick; mode = Mode.None;
                    Shot("k331_toss_3_rest");
                    Report("toss", broke);
                }
                return;
            }
            if (mode == Mode.Swap)
            {
                bool woke = OzFortGunBreakup.RockWoken > woken0;
                bool rested = Time.time - swapAt > 1.5f && OzFortGunBreakup.LastAllRested();
                if ((woke && rested && restAt < 0f) || (!woke && Time.time - swapAt > 3f && restAt < 0f)) restAt = Time.time + 0.5f;
                if ((restAt >= 0f && Time.time >= restAt) || Time.realtimeSinceStartup > until)
                {
                    EditorApplication.update -= Tick; mode = Mode.None;
                    Shot("k331_swap_after");
                    Report("swap", true);
                }
            }
        }

        static void Report(string what, bool broke)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[OpenZ K331] " + lead);
            var ps = OzFortGunBreakup.LastPieces.Where(p => p).ToList();
            int inRock = 0, middles = 0; float worst = 0f; string worstWhich = "-";
            foreach (var p in ps)
            {
                float deep = 0f; string which = "-";
                foreach (var c in p.GetComponentsInChildren<Collider>()) { float d = OzRockSolid.DepthInRocks(c, out var w); if (d > deep) { deep = d; which = w; } }
                bool mid = OzRockSolid.InsideRock(Bounds(p).center, out var wm);
                if (deep > MaxDepth) inRock++;
                if (mid) middles++;
                if (deep > worst) { worst = deep; worstWhich = p.PieceName + " in " + which; }
                sb.AppendLine($"      {p.PieceName,-14} {p.Kind,-6} {p.Size:0.00} m, rests {p.RestOn} (gap {p.RestGap * 100f:0} cm), moved {p.Moved:0.0} m, hits {p.Hits} (heard {p.HeardHits}), in a rock {deep * 100f:0.0} cm{(mid ? ", its middle INSIDE " + wm : "")}");
            }
            if (what == "toss")
            {
                bool movedNow = OzRockSolid.LandingsMoved > moved0;
                sb.AppendLine(OzV11Kit.Verdict(broke && ps.Count >= 8, $"the cannon broke apart into {ps.Count} pieces on its landing"));
                sb.AppendLine(OzV11Kit.Verdict(movedNow || OzRockSolid.LastLanding.StartsWith("free"), $"its landing place is free ground: {OzRockSolid.LastLanding} (from {OzRockSolid.LastLandingFrom.x:0.0}, {OzRockSolid.LastLandingFrom.z:0.0} to {OzRockSolid.LastLandingTo.x:0.0}, {OzRockSolid.LastLandingTo.z:0.0})"));
                sb.AppendLine(OzV11Kit.Verdict(OzFortGunBreakup.RockStartMoves == 0, $"no piece started in a rock on the landing frame ({OzFortGunBreakup.LastRockStart})"));
            }
            else
            {
                int w = OzFortGunBreakup.RockWoken - woken0;
                sb.AppendLine(OzV11Kit.Verdict(w > 0, $"the piece(s) on or beside the rock woke when it showed its stump: {w}; all rest again: {OzFortGunBreakup.LastAllRested()}"));
            }
            sb.AppendLine(OzV11Kit.Verdict(inRock == 0, $"pieces lying in a rock at rest (a collider deeper than {MaxDepth * 100f:0} cm): {inRock} (deepest {worst * 100f:0.0} cm, {worstWhich})"));
            sb.AppendLine(OzV11Kit.Verdict(middles == 0, $"piece middles inside a rock's drawn shape: {middles}"));
            sb.AppendLine($"      rock contacts of the pieces +{OzFortGunBreakup.RockTouches - touches0} (the pieces' hits heard: clanks +{OzFortGunBreakup.Clanks - clanks0}, crashes +{OzFortGunBreakup.Crashes - crashes0}); popped away on a rock so far {OzFortGunBreakup.RockPops}");
            sb.AppendLine("      " + OzRockSolid.State);
            sb.AppendLine($"      pictures: {Out} (k331_{what}_*.png)");
            var text = sb.ToString();
            Debug.Log(text);
            try { File.WriteAllText(Path.Combine(Out, "k331_" + what + "_report.txt"), text); } catch (System.Exception) { }
        }
    }
}
