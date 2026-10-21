using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Version 1.1 kit K256k check (menu OpenZ/V1.1/K256k ...): the shot-down shuttle's pieces as broken metal, at rest out of the
    /// buildings, and its crew lying on something. Run it in Play on level 16 after a shuttle break whose pieces lie still (the K331b
    /// the wreck from the game camera's angle (pitch 55 degrees): the camera 70 m and 35 m up. Test tools only: nothing is saved.
    /// </summary>
    public static class OzK256kCheck
    {
        const string M = "OpenZ/V1.1/K256k ";
        static string Out { get { var p = Path.Combine(OpenZPaths.Records, "milestones", "k256k_check"); Directory.CreateDirectory(p); return p; } }

        [MenuItem(M + "Shuttle Pieces: Look, Rest And Crew Report + Pictures (Play, after a shuttle break)")]
        static void Report()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ K256k] Play first (level 16, after a shuttle break)"); return; }
            var sb = new StringBuilder();
            int pieces = 0, rested = 0; var mid = Vector3.zero;
            foreach (var p in OzShuttleBreakup.LastPieces) if (p) { pieces++; if (p.Rested) rested++; mid += p.transform.position; }
            if (pieces == 0) { Debug.LogWarning("[OpenZ K256k] no shuttle break to report (break one first)"); return; }
            mid /= pieces;
            sb.AppendLine($"[OpenZ K256k] the last shuttle break ({OzShuttleBreakup.LastHow}): {pieces} pieces, {rested} at rest; plan: {OzShuttleBreakup.LastPlan}");
            sb.AppendLine("      " + OzShuttleBreakup.ShellState);
            // ---- the look
            OzShuttleBreakup.LookCounts(out int capped, out int steel, out int ribbed, out int vcMeshes, out int vcShown, out int tris);
            sb.AppendLine(OzV11Kit.Verdict(OzShuttleBreakup.ShellLook && OzFractureCut.ShellPlain == 0 && OzFractureCut.ReliefPlain == 0 && OzShuttleBreakup.ShellPlates >= 8,
                $"the hull is torn, not cut straight: torn cuts {OzFractureCut.ShellTears} (plain {OzFractureCut.ShellPlain}), plates {OzShuttleBreakup.ShellPlates} (8 panels), dome relief cuts {OzFractureCut.ReliefCuts} (plain {OzFractureCut.ReliefPlain})"));
            sb.AppendLine(OzV11Kit.Verdict(capped > 0 && steel == capped && ribbed == 0,
                $"every inside / broken face is dark broken steel: {steel} of {capped} pieces with an inside wear OZ_ShuttleWreckFracture, the ribbed grey grid inside on {ribbed}"));
            sb.AppendLine(OzV11Kit.Verdict(vcMeshes > 0 && vcShown == vcMeshes, $"the torn pieces' paint shows the soot at the tears: {vcShown} of {vcMeshes} pieces with vertex colours"));
            sb.AppendLine(OzV11Kit.Verdict(tris <= 40000, $"triangles of all pieces {tris} (40000 allowed)"));
            sb.AppendLine(OzV11Kit.Verdict(OzShuttleBreakup.ShellStepMs <= 6f, $"the torn panels' heaviest main-thread step {OzShuttleBreakup.ShellStepMs:0.0} ms (6 allowed; one a frame from the level's start), their worker {OzShuttleBreakup.ShellWorkerMs:0} ms (frames waiting {OzShuttleBreakup.ShellWaitFrames}, waits on a kill frame {OzShuttleBreakup.ShellForcedWaits})"));
            // ---- the rest against the buildings, guns and rocks
            float deep = OzShuttleBreakup.RestDepthInStatics(out string dw, out int sampleIn, out string sw);
            sb.AppendLine(OzV11Kit.Verdict(deep <= 0.021f, $"no piece rests inside a building, gun or rock: the deepest {deep * 100f:F1} cm {dw} (2 cm allowed)"));
            sb.AppendLine(OzV11Kit.Verdict(sampleIn == 0, $"no point of a piece's drawn shape (5 cm in) inside a building, gun or rock: {sampleIn} {sw}"));
            sb.AppendLine($"      moved out of a static or rock as they came to rest: {OzShuttleBreakup.StaticClears} (largest {OzShuttleBreakup.StaticClearLargest * 100f:F1} cm; last: {OzShuttleBreakup.StaticClearLast})");
            // ---- the crew
            int bodies = 0, air = 0, lid = 0; float worst = 0f; var held = new StringBuilder();
            foreach (var b in OzShuttleCrewFall.Last)
            {
                if (!b) continue;
                bodies++;
                float h = OzShuttleBreakup.OverSurfaceOf(b.BodyPos);
                bool ok = OzShuttleCrewFall.Held(b, out string by, out bool onLid);
                if (onLid && !ok) lid++;
                if (h > 1f && !ok) { air++; worst = Mathf.Max(worst, h); }
                held.Append($"{h:F2} m {(ok ? "on " + by : onLid ? "ON A PANEL'S LID" : h > 1f ? "ON NOTHING" : "low")}; ");
            }
            sb.AppendLine($"      the crew's bodies ({bodies}): {held}");
            sb.AppendLine(OzV11Kit.Verdict(air == 0, $"no crew body lies in the air (hips over 1 m with no ground, metal, scenery or body under a bone): {air}, worst {worst:F2} m"));
            sb.AppendLine(OzV11Kit.Verdict(lid == 0, $"no crew body lies on a hull panel's collider over its hollow: {lid} (settled onto the metal so far: {OzShuttleCrewFall.HollowSettles}, the largest {OzShuttleCrewFall.HollowLargest:F2} m)"));
            int asked = 0; foreach (var b in OzShuttleCrewFall.Last) { var l = b ? b.GetComponent<OzBodyLanding>() : null; if (l && l.landed && l.thud != "") asked++; }
            sb.AppendLine(OzV11Kit.Verdict(bodies == 0 || asked == bodies, $"every crew landing is heard: {asked} of {bodies} bodies asked their thud (thuds for landings without a contact {OzShuttleCrewFall.LandThuds}, drops after a high landing {OzShuttleCrewFall.DropThuds}); knocks on tumbling pieces that were no landing {OzShuttleBreakup.CrewKnocks}"));
            // ---- pictures
            var cam = Camera.main; float yaw = cam ? cam.transform.eulerAngles.y : 0f;
            foreach (var (tag, height) in new[] { ("70m", 70f), ("35m", 35f) })
            {
                try { var path = OpenZShot.Shot(mid, height / Mathf.Sin(55f * Mathf.Deg2Rad), 55f, yaw, Path.Combine(Out, "k256k_wreck_" + tag + ".png"), 2560, 1600); sb.AppendLine("      picture " + path); }
                catch (System.Exception e) { sb.AppendLine("      picture " + tag + " failed: " + e.Message); }
            }
            var text = sb.ToString();
            Debug.Log(text);
            try { File.WriteAllText(Path.Combine(Out, "k256k_report.txt"), text); } catch (System.Exception) { }
        }
    }
}
