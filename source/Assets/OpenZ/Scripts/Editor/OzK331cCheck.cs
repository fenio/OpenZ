using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Version 1.1 kit K331c check (menu OpenZ/V1.1/K331c ...), item 375: the fort cannon's pieces as real broken metal in its own paint.
    /// <list type="bullet">
    /// <item>Toss + report + pictures (Play): tosses the fort cannon nearest the view centre (FortGunView.TestToss, view only); when every
    /// piece rests (40 s at most) prints the fracture numbers (17 pieces, each wearing the fracture look, no plain break-face material,
    /// no added rims / ribs / rebar, triangles, the cutting time per frame, fallbacks) and takes pictures of the pieces at the RTS
    /// camera (70 m, pitch 55) and closer (35 m), from two sides.</item>
    /// <item>Report now (Play): the same numbers for the latest break, no toss.</item>
    /// </list>
    /// Test tools only: nothing is saved but the pictures.
    /// </summary>
    public static class OzK331cCheck
    {
        const string M = "OpenZ/V1.1/K331c ";
        const int WantPieces = 17, MaxTris = 20000;
        const float StepMs = 10f;
        static int breaks0; static float until; static bool shots;
        static string Out { get { var p = Path.Combine(OpenZPaths.Records, "milestones", "k331c_pieces_check"); Directory.CreateDirectory(p); return p; } }

        [MenuItem(M + "Toss The Nearest Fort Cannon + Report + Pictures (Play)")]
        static void Toss()
        {
            var g = ZGame.I;
            if (!Application.isPlaying || !g) { Debug.LogWarning("[OpenZ K331c] enter Play first, the camera on a fort"); return; }
            var cam = Camera.main; var c = cam && g.GroundPoint(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out var gp) ? gp : Vector3.zero;
            var gun = UnityEngine.Object.FindObjectsByType<FortGunView>(FindObjectsSortMode.None).Where(v => v && v.Bound && !v.Tossed).OrderBy(v => Vector3.Distance(v.transform.position, c)).FirstOrDefault();
            if (!gun) { Debug.LogWarning("[OpenZ K331c] no standing fort cannon in the scene"); return; }
            breaks0 = OzFortGunBreakup.Breaks; shots = true;
            try   // the living cannon at 35 m before the toss: the paint's colour reference for the pieces (SPEC part 0, check 2)
            {
                var path = OpenZShot.Shot(gun.transform.position, 35f, 45f, cam ? cam.transform.eulerAngles.y : 0f, Path.Combine(Out, "k331c_living_35m.png"), 1920, 1080);
                Debug.Log("[OpenZ K331c] picture " + path);
            }
            catch (System.Exception e) { Debug.LogWarning("[OpenZ K331c] picture k331c_living_35m failed: " + e.Message); }
            gun.TestToss();
            until = Time.realtimeSinceStartup + 40f;
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Debug.Log($"[OpenZ K331c] fort cannon tossed (fracture look {(OzFortGunBreakup.FractureLook ? "ON" : "OFF")}); the report and pictures when its pieces rest");
        }

        [MenuItem(M + "Report The Latest Break Now (Play)")]
        static void ReportNow() => Debug.Log(Report());

        static void Tick()
        {
            if (!Application.isPlaying) { EditorApplication.update -= Tick; return; }
            bool broke = OzFortGunBreakup.Breaks > breaks0;
            bool done = broke && OzFortGunBreakup.LastAllRested();
            if (!done && Time.realtimeSinceStartup < until) return;
            EditorApplication.update -= Tick;
            Debug.Log(Report());
            if (shots && broke) Pictures();
        }

        static void Pictures()
        {
            var at = OzFortGunBreakup.LastImpact; var ps = OzFortGunBreakup.LastPieces.Where(p => p).ToList();
            if (ps.Count > 0) { var s = Vector3.zero; foreach (var p in ps) s += p.transform.position; at = s / ps.Count; }
            var cam = Camera.main; float yaw = cam ? cam.transform.eulerAngles.y : 0f;
            foreach (var (name, dist, pitch, dy) in new[] { ("k331c_rts_70m", 70f, 55f, 0f), ("k331c_rts_70m_side", 70f, 55f, 120f), ("k331c_35m", 35f, 45f, 0f), ("k331c_35m_side", 35f, 45f, 200f) })
            {
                try { var path = OpenZShot.Shot(at, dist, pitch, yaw + dy, Path.Combine(Out, name + ".png"), 1920, 1080); Debug.Log("[OpenZ K331c] picture " + path); }
                catch (System.Exception e) { Debug.LogWarning("[OpenZ K331c] picture " + name + " failed: " + e.Message); }
            }
        }

        public static string Report()
        {
            var sb = new StringBuilder("[OpenZ K331c] the fort cannon's pieces: real broken metal in its own paint\n");
            var ps = OzFortGunBreakup.LastPieces.Where(p => p).ToList();
            sb.AppendLine(OzV11Kit.Verdict(OzFortGunBreakup.FractureLook && ps.Count == WantPieces,
                $"fracture look {(OzFortGunBreakup.FractureLook ? "on" : "OFF")}: {ps.Count} pieces (wanted {WantPieces}: the housing in two shell halves {ps.Count(p => p.PieceName.StartsWith("head"))}, the barrel in two sections {ps.Count(p => p.Kind == "barrel")}); plan: {OzFortGunBreakup.LastPlan}"));
            int frac = ps.Count(p => p.Fracture);
            sb.AppendLine(OzV11Kit.Verdict(frac == ps.Count && ps.Count > 0, $"every piece wears the fracture look: {frac} of {ps.Count} ({OzFortGunBreakup.FractureMatState}; paint copies {OzFortGunBreakup.PaintSwapped}, broken faces {OzFortGunBreakup.FractureFaces})"));
            int plainFaces = 0, dress = 0, tris = 0;
            foreach (var p in ps)
            {
                foreach (var r in p.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var m in r.sharedMaterials) if (m && (m.name.StartsWith("FortGun_BreakFace") || m.name == "FortGun_Rebar")) plainFaces++;
                    if (r.name == "OZ_WreckDress") dress++;
                }
                tris += p.Tris;
            }
            sb.AppendLine(OzV11Kit.Verdict(plainFaces == 0 && dress == 0, $"no plain break-face (tiled) material on the pieces: {plainFaces}; no added rims / ribs / rebar: {dress}"));
            sb.AppendLine(OzV11Kit.Verdict(tris <= MaxTris, $"triangles in all the pieces {tris} (wanted <= {MaxTris})"));
            sb.AppendLine(OzV11Kit.Verdict(OzFractureCut.Fallbacks == 0, "the cutting: " + OzFractureCut.State));
            sb.AppendLine(OzV11Kit.Verdict(OzFortGunBreakup.LastPrepareMaxMs <= StepMs && OzFortGunBreakup.LastLandingPrepareMs <= 1f,
                $"cut in the air: {OzFortGunBreakup.LastPrepareSteps} steps, the heaviest {OzFortGunBreakup.LastPrepareMaxMs:0.0} ms (wanted <= {StepMs:0}), left for the landing frame {OzFortGunBreakup.LastLandingPrepareMs:0.00} ms (wanted <= 1), the landing frame {OzFortGunBreakup.LastBreakMs:0.0} ms (info: today's + 2 ms at most)"));
            sb.AppendLine(OzV11Kit.Verdict(OzFortGunBreakup.LastCutOk, OzFortGunBreakup.LastCutText));
            sb.AppendLine($"      rest look: {OzFortGunRestLook.State}");
            foreach (var p in ps.OrderByDescending(p => p.MassKg))
            {
                var mats = string.Join(", ", p.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m).Select(m => m.name).Distinct());
                sb.AppendLine($"      {p.PieceName,-14} {p.Kind,-7} {p.Tris,5} tris {p.BoxSize,4:0.00} m {p.MassKg,6:0} kg  rests {p.RestOn} {(p.Rested ? $"{p.RestGap * 100f:0.0} cm" : "NOT at rest")}  materials: {mats}");
            }
            sb.AppendLine("      the look (white, tiling, colour, broken edges) is judged on the pictures beside the reference crops of item 375; the levels by the KD3 check (pieces 0.60-0.72 of the concrete chunks)");
            return sb.ToString();
        }
    }
}
