using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Version 1.1 kit K331e checks (menu OpenZ/V1.1/K331e ...):
    /// <list type="bullet">
    /// <item>
    /// piece's numbers and mesh compared, for several seeds and landing turns, the fracture look on and off), and the cutting time of the
    /// latest break per frame;</item>
    /// <item>items 331 + 332: the statics' colliders made ahead, counted right (the living statics' parts), all within 60 s of the level
    /// start, no frame over 1 ms; with a fresh start of the level and the report at 60 s.</item>
    /// </list>
    /// </summary>
    public static class OzK331eCheck
    {
        const string M = "OpenZ/V1.1/K331e ";
        const float MadeWithinS = 60f, MadeFrameMs = 1f;
        static string Out { get { var p = Path.Combine(OpenZPaths.Records, "milestones", "k331e_check"); Directory.CreateDirectory(p); return p; } }

        static void Write(string file, string text)
        {
            Debug.Log(text);
            try { File.WriteAllText(Path.Combine(Out, file), text); } catch (System.Exception e) { Debug.LogWarning("[OpenZ K331e] could not write " + file + ": " + e.Message); }
        }

        [MenuItem(M + "Fort Cannon: Cut On A Worker Thread Gives The Same Pieces (Play)")]
        static void SamePieces()
        {
            var g = ZGame.I;
            if (!Application.isPlaying || !g) { Debug.LogWarning("[OpenZ K331e] enter Play first, the camera on a fort"); return; }
            var cam = Camera.main; var c = cam && g.GroundPoint(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out var gp) ? gp : Vector3.zero;
            var gun = Object.FindObjectsByType<FortGunView>(FindObjectsSortMode.None).Where(v => v && v.Bound && !v.Tossed && v.YawT && v.YawT.gameObject.activeInHierarchy)
                .OrderBy(v => Vector3.Distance(v.transform.position, c)).FirstOrDefault();
            if (!gun) { Debug.LogWarning("[OpenZ K331e] no standing fort cannon in the scene"); return; }
            int id = gun.Fort != null ? gun.Fort.Id : 0;
            var seeds = new[] { id, id + 1, id + 17, id * 31 + 7, 1000 + id };
            var lands = new[] { Quaternion.identity, Quaternion.Euler(0f, 0f, 90f), Quaternion.Euler(0f, 0f, -90f) };
            var lines = new List<string>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool same = OzFortGunBreakup.SamePieces(gun.YawT, seeds, lands, lines, out int compared, out int pieces);
            var sb = new StringBuilder($"[OpenZ K331e] fort cannon #{id}: the cuts on a worker thread against the cutting one step per frame (both run now, {sw.Elapsed.TotalSeconds:0.0} s)\n");
            sb.AppendLine(OzV11Kit.Verdict(same && compared == seeds.Length * lands.Length * 2,
                $"the same pieces both ways: {lines.Count(l => l.StartsWith("same"))} of {compared} cuts (wanted {seeds.Length * lands.Length * 2}: {seeds.Length} seeds x {lands.Length} landing turns x the fracture look on / off), {pieces} pieces compared (names, kinds, outward, mass scale, bore, triangles, every vertex / normal / uv / colour / tangent / index)"));
            foreach (var l in lines) sb.AppendLine("      " + l);
            Write("k331e_same_pieces.txt", sb.ToString());
        }

        [MenuItem(M + "Fort Cannon: Cutting Time Of The Latest Break (Play)")]
        static void CutTime()
        {
            if (OzFortGunBreakup.Breaks == 0) { Debug.LogWarning("[OpenZ K331e] no fort cannon has broken apart yet: toss one first (OpenZ/V1.1/K331c Toss The Nearest Fort Cannon ...)"); return; }
            var text = "[OpenZ K331e] the latest fort cannon break\n" + OzV11Kit.Verdict(OzFortGunBreakup.LastCutOk, OzFortGunBreakup.LastCutText) + "\n      plan: " + OzFortGunBreakup.LastPlan + "\n";
            Write("k331e_cut_time.txt", text);
        }

        [MenuItem(M + "Statics: Colliders Made (Play)")]
        static void Made()
        {
            if (!Application.isPlaying || !ZGame.I) { Debug.LogWarning("[OpenZ K331e] enter Play first (a level running)"); return; }
            Report("now");
        }

        static float reportAt = -1f; static int scene0 = -1; static float wallUntil;

        [MenuItem(M + "Statics: Colliders Made, Fresh Start Of The Level + Report At 60 s (Play)")]
        static void MadeFresh()
        {
            if (!Application.isPlaying || !ZGame.I) { Debug.LogWarning("[OpenZ K331e] enter Play first (a level running)"); return; }
            scene0 = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle; reportAt = MadeWithinS; wallUntil = Time.realtimeSinceStartup + 240f;
            OzScenes.Restart();
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Debug.Log("[OpenZ K331e] the level starts afresh; the colliders' report comes 60 s after its start");
        }

        static void Tick()
        {
            if (!Application.isPlaying) { EditorApplication.update -= Tick; return; }
            if (Time.realtimeSinceStartup > wallUntil) { EditorApplication.update -= Tick; Debug.LogWarning("[OpenZ K331e] the level did not run 60 s within 4 minutes: no report"); return; }
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle == scene0 || !ZGame.I || Time.timeSinceLevelLoad < reportAt) return;
            EditorApplication.update -= Tick;
            Report($"{Time.timeSinceLevelLoad:0.0} s after a fresh start");
        }

        static void Report(string when)
        {
            bool ok = OzStaticSolid.MadeOk(MadeWithinS, MadeFrameMs, out var line);
            string lvl = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            var text = $"[OpenZ K331e] statics on {lvl} ({when}, {Time.timeSinceLevelLoad:0.0} s into the level)\n" + OzV11Kit.Verdict(ok, line) + "\n      " + OzStaticSolid.State + "\n";
            Write("k331e_made_" + lvl + ".txt", text);
        }
    }
}
