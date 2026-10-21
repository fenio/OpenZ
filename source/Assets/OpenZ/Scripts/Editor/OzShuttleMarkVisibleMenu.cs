#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The readable landing mark (rule DV118, View/OzShuttleShadow.Visible.cs): a counted check out of Play and the Play proof
    /// View/OzShuttleMarkVisibleProof.cs on level 16. Output folder: OzShuttleMarkVisibleProof.Folder().</summary>
    public static class OzShuttleMarkVisibleMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: Mark Visible Check (out of Play)")]
        static void Count() { var r = Run(); Debug.Log("[OpenZ] mark visible check: " + r); }

        [MenuItem("OpenZ/V1.1/Shuttle: Mark Visible Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] mark visible proof: start level 16 in Play first"); return; }
            OzShuttleMarkVisibleProof.Run();
            Debug.Log("[OpenZ] mark visible proof started; its report: OzShuttleMarkVisibleProof.Report, the log and pictures in " + OzShuttleMarkVisibleProof.Folder());
        }

        static int pass, fail;
        static void Check(string what, bool ok) { if (ok) pass++; else fail++; Debug.Log("[OpenZ] mark visible check: " + (ok ? "PASS " : "FAIL ") + what); }

        /// <summary>The numbers and the hooks (the strengths the off-engine twin measured: 18 / 20 / 22 % darker by day, +5 % selected).</summary>
        public static string Run()
        {
            pass = 0; fail = 0;
            float fast = OzShuttleShadow.WantFor(0f, 1f, 0f, 0f), hover = OzShuttleShadow.WantFor(0f, 0f, 0f, 0f), near = OzShuttleShadow.WantFor(1f, 0f, 0f, 0f);
            Check($"C1 not selected by day: cruise 0.26 .. 0.30, hover 0.29 .. 0.33, near a landing 0.32 .. 0.36 ({fast:F3} / {hover:F3} / {near:F3})",
                fast >= 0.26f && fast <= 0.30f && hover >= 0.29f && hover <= 0.33f && near >= 0.32f && near <= 0.36f);
            float add = OzShuttleShadow.WantFor(0f, 0f, 1f, 0f) - hover;
            Check($"C2 selected: 0.06 .. 0.08 stronger, eased over 0.15 s or more ({add:F3}, {OzShuttleShadow.SelectEaseSec:F2} s)", add >= 0.06f && add <= 0.08f && OzShuttleShadow.SelectEaseSec >= 0.15f);
            float nightK = OzShuttleShadow.WantFor(0f, 0f, 0f, 1f) / Mathf.Max(1e-4f, hover);
            Check($"C3 at night the same share of the ground's light or a little more (x1.0 .. x1.15: {nightK:F2})", nightK >= 0.999f && nightK <= 1.15f);
            Check($"C4 a soft disc: fully dark out to 0.35 .. 0.45 of the radius at cruise height ({OzShuttleShadow.CoreFar:F2}), no ring", OzShuttleShadow.CoreFar >= 0.35f && OzShuttleShadow.CoreFar <= 0.45f);
            string path = System.IO.Path.Combine(Application.dataPath, "OpenZ/Scripts/View/OzShuttleShadow.cs");
            string src = System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : "";
            int cores = 0; for (int i = src.IndexOf("Mathf.Lerp(0.5f, CoreFar,"); i >= 0; i = src.IndexOf("Mathf.Lerp(0.5f, CoreFar,", i + 1)) cores++;
            Check("C5 the mark's step uses the readable strengths: the selection, the ease span, the red never lighter, the disc's core (decal and fallback)",
                src.Contains("WantFor(nearK, fastK, SelectedStep(o, dt), night)") && src.Contains("MarkEaseSpan()") && src.Contains("redA = Mathf.Max(redA, markNow);") && cores == 2);
            return fail == 0 ? $"{pass} PASS" : $"{fail} FAIL of {pass + fail}";
        }
    }
}
#endif
