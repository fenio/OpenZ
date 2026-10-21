#if !OPENZ_HARNESS
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// The K256b menus (rule DV118, the shuttle polish round): the Play proofs on level 16 (View/OzShuttlePolishProof.cs: A = every
    /// point ending with the air death, B = after a fresh level start the ground death with a squad aboard, G = the weapons gallery
    /// (every enemy weapon in turn fires at the flying ship, every drawn shot counted) and then four kills by different weapons, each
    /// after a fresh level start; A, B and G at clip pace = the same staging at the natural pace without pictures, waiting for a go.txt
    /// written after its ready.txt, for a recording) and the edit-mode counts: the plants' downwash
    /// twin (the five trees of the sway sheet: their crown top's movement with the shuttle's downwash 5 m from them at strength 1.6 is at
    /// least 2.5 times the calm one, and strength 0 gives exactly the numbers without it) and the door kit's numbers.
    /// P = the testing overlay (P): a shuttle of each side dragged onto the map, drawn in full, the player's one flown and landed, on the
    /// level that is open (level 16, or level 2: a level without a shuttle of its own).
    /// The proofs' output folder (pictures, logs, ready / go / done) is OzShuttlePolishProof.Folder(): built at run time from the
    /// project's place (the work tree's records, milestones/shuttle_polish_proof), nothing named here.
    /// </summary>
    public static class OzShuttlePolishMenu
    {

        [MenuItem("OpenZ/V1.1/Shuttle: K256b Play Proof A (Level 16, Play)")]
        static void ProofA() => Start("A");
        [MenuItem("OpenZ/V1.1/Shuttle: K256b Play Proof B (fresh Level 16 start)")]
        static void ProofB() => Start("B");
        [MenuItem("OpenZ/V1.1/Shuttle: K256b Play Proof A (clip pace)")]
        static void ProofClip() => Start("A_clip");
        [MenuItem("OpenZ/V1.1/Shuttle: K256b Play Proof B (clip pace, fresh Level 16 start)")]
        static void ProofBClip() => Start("B_clip");
        [MenuItem("OpenZ/V1.1/Shuttle: K256b Play Proof G (weapons gallery + kills, Level 16, Play)")]
        static void ProofG() => Start("G");
        [MenuItem("OpenZ/V1.1/Shuttle: K256b Play Proof G (clip pace)")]
        static void ProofGClip() => Start("G_clip");
        [MenuItem("OpenZ/V1.1/Shuttle: K256c Play Proof P (testing overlay shuttles, Level 16 or Level 2, Play)")]
        static void ProofP() => Start("P");

        static void Start(string mode)
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K256b proof: enter Play on level 16 first"); return; }
            OzShuttlePolishProof.OutDir = "";
            OzShuttlePolishProof.Run(mode);
            Debug.Log("[OpenZ] K256b proof " + mode + " started; its report: OzShuttlePolishProof.Report, the logs in " + OzShuttlePolishProof.Folder());
        }

        [MenuItem("OpenZ/V1.1/Shuttle: K256b Counts (edit mode)")]
        static void CountsMenu()
        {
            var text = Counts();
            Debug.Log("[OpenZ] K256b counts\n" + text);
            try
            {
                var dir = OzShuttlePolishProof.Folder();
                System.IO.Directory.CreateDirectory(dir); System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "counts_edit_mode.txt"), text);
            }
            catch (System.Exception ex) { Debug.LogWarning("[OpenZ] K256b counts: " + ex.Message); }
        }

        /// <summary>The flora twin count and the kit's numbers (pure numbers, no scene).</summary>
        public static string Counts()
        {
            var sb = new StringBuilder(); int fails = 0;
            sb.Append(FloraTwin(ref fails));
            var kit = Resources.Load<GameObject>(ShuttleRearDoor.KitPath);
            if (!kit) { sb.AppendLine("FAIL  the door kit " + ShuttleRearDoor.KitPath + " is not in the project"); fails++; }
            else
            {
                int tris = 0, parts = 0, markers = 0;
                foreach (var t in kit.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name.StartsWith("MK_")) markers++;
                    var mf = t.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
                    parts++; for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) tris += (int)mf.sharedMesh.GetIndexCount(s) / 3;
                    sb.AppendLine($"   kit part {t.name}: {mf.sharedMesh.vertexCount} vertices, readable {mf.sharedMesh.isReadable}, {mf.sharedMesh.subMeshCount} sub-meshes");
                }
                bool ok = parts >= 11 && markers >= 26;
                if (!ok) fails++;
                sb.AppendLine($"{(ok ? "PASS" : "FAIL")}  the door kit: {parts} parts ({tris} triangles with the hull and the wreck body), {markers} markers");
            }
            return (fails == 0 ? "PASS" : "FAIL " + fails) + "\n" + sb;
        }

        /// <summary>The plants' downwash in plain numbers (OzFloraWindMath.Displace with Wind.Dw*): each of the five trees' crown top over
        /// 10 s (5 samples a second), calm wind; with the downwash 5 m from its foot (k 1.6, radius 13 m) and with k 0.</summary>
        public static string FloraTwin(ref int fails)
        {
            var sb = new StringBuilder();
            foreach (var r in OzV11SwayCount.Five)
            {
                float footX = 0f, footY = 0f, footZ = 0f, topX = 0f, topY = r.PivotY, topZ = 0f;
                float px = 0f, py = r.Height, pz = 0f;                                                      // the crown's top point
                float calm = 0f, wash = 0f; bool same = true;
                for (int k = 0; k < 50; k++)
                {
                    float t = 20f + 0.2f * k;
                    var w = new OzFloraWindMath.Wind { Wx = OzV11SwayCount.WindX, Wz = OzV11SwayCount.WindZ, S = OzFloraWindMath.CalmStrength, Storm = 0f, T = t, Gust = OzFloraWindMath.CalmGust(t) };
                    OzFloraWindMath.Displace(r.Kind, w, footX, footY, footZ, topX, topY, topZ, 1f, r.Radius, r.Height, 1f, px, py, pz, out float ax, out float ay, out float az);
                    var w0 = w; w0.DwX = 5f; w0.DwY = 0f; w0.DwZ = 0f; w0.DwR = 13f; w0.DwK = 0f; w0.DwT = t;
                    OzFloraWindMath.Displace(r.Kind, w0, footX, footY, footZ, topX, topY, topZ, 1f, r.Radius, r.Height, 1f, px, py, pz, out float bx, out float by, out float bz);
                    same &= ax == bx && ay == by && az == bz;
                    var w1 = w0; w1.DwK = 1.6f;
                    OzFloraWindMath.Displace(r.Kind, w1, footX, footY, footZ, topX, topY, topZ, 1f, r.Radius, r.Height, 1f, px, py, pz, out float cx, out float cy, out float cz);
                    calm = Mathf.Max(calm, Mathf.Sqrt(ax * ax + ay * ay + az * az));
                    wash = Mathf.Max(wash, Mathf.Sqrt(cx * cx + cy * cy + cz * cz));
                }
                bool ok = same && wash >= 2.5f * calm;
                if (!ok) fails++;
                sb.AppendLine($"{(ok ? "PASS" : "FAIL")}  {r.Label}: the crown top moves {calm:F3} m calm, {wash:F3} m in the shuttle's downwash 5 m away (x{(calm > 0f ? wash / calm : 0f):F1}); strength 0 = the numbers without it: {(same ? "exactly" : "NO")}");
            }
            return sb.ToString();
        }
    }
}
#endif
