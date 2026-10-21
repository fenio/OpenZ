using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Version 1.1 kit K378 checks (menu OpenZ/V1.1/K378 ...), item 378: a tank wreck breaks into its own model's broken pieces, not into
    /// black boxes. Each check makes an enemy light or heavy tank on a free spot near the view's middle, kills it 1 s later and waits for
    /// its break (at once, or after its burn-out: the cook-off the Car Park flyby showed), then until the pieces rest (at most 30 s), and
    /// camera's angle: 70 m (pitch 55) at the break + 1.5 s and at rest, 35 m (pitch 45) at rest from two sides. Test tools only: nothing
    /// is saved; the forced outcome is put back.
    /// </summary>
    public static class OzK378Check
    {
        const string M = "OpenZ/V1.1/K378 ";
        static string Out { get { var p = Path.Combine(OpenZPaths.Records, "milestones", "k378_tank_wreck_check"); Directory.CreateDirectory(p); return p; } }
        static string name = "-", lead = "";
        static float until, shotAt = -1f, restAt = -1f, brokeAt = -1f, killAt;
        static int step, force0, breaks0, pieces0, chunked0, cores0, added0, fracCuts0, fracFall0, notReady0, timeouts0;
        static bool atDeath0;
        static Vector3 at; static float camYaw, camPitch = 55f;
        static ZObject unit;

        static bool Ready(out ZGame g)
        {
            g = ZGame.I;
            if (!Application.isPlaying || !g || g.World == null) { Debug.LogWarning("[OpenZ K378] enter Play first (a level running, L02 or L17)"); return false; }
            return true;
        }

        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        static Team Enemy(ZGame g) { var me = g.World.Objects.FirstOrDefault(o => o != null && o.IsFort && o.Owner != Team.Neutral); return me != null && me.Owner == Team.Red ? Team.Blue : Team.Red; }

        /// <summary>A free vehicle spot (3 x 3 tiles, nothing standing there) near the view's middle.</summary>
        static bool Spot(ZGame g, Vector3 c, out int px, out int py)
        {
            px = py = 0; var W = g.World;
            for (float d = 0f; d <= 30f; d += 2f)
                for (int s = 0; s < 16; s++)
                {
                    var p = c + Quaternion.Euler(0f, s * 22.5f, 0f) * Vector3.forward * d;
                    var q = ZGame.WorldToPx(p); int tx = q.x >> 4, ty = q.y >> 4; bool ok = true;
                    for (int j = -2; j <= 2 && ok; j++) for (int i = -2; i <= 2 && ok; i++) ok = W.Path.TilePassable(tx + i, ty + j, false);
                    if (!ok) continue;
                    if (W.Objects.Any(o => o != null && !o.Destroyed && Flat(ZGame.PxToWorld(o.CenterX, o.CenterY), p) < 7f)) continue;
                    px = tx * 16; py = ty * 16; return true;
                }
            return false;
        }

        [MenuItem(M + "Light Tank: Kill One, Its Wreck Breaks Apart At Once + Report + Pictures (Play)")]
        static void LightNow() => Run("light_now", "light", false);
        [MenuItem(M + "Heavy Tank: Kill One, Its Wreck Breaks Apart At Once + Report + Pictures (Play)")]
        static void HeavyNow() => Run("heavy_now", "heavy", false);
        [MenuItem(M + "Light Tank: Kill One, It Burns Out And Cooks Off (the flyby's case) + Report + Pictures (Play)")]
        static void LightCook() => Run("light_cookoff", "light", true);
        [MenuItem(M + "Heavy Tank: Kill One, It Burns Out And Cooks Off + Report + Pictures (Play)")]
        static void HeavyCook() => Run("heavy_cookoff", "heavy", true);

        [MenuItem(M + "Switch The Look On Or Off (Play, A/B: off = the old black boxes)")]
        static void Toggle() { OzTankWreck.On = !OzTankWreck.On; Debug.Log("[OpenZ K378] tank wreck look " + (OzTankWreck.On ? "ON" : "OFF (the old break)")); }

        [MenuItem(M + "State")]
        static void State() => Debug.Log("[OpenZ K378] " + OzTankWreck.State + "\n" + OzTankWreck.SetsText());

        static void Run(string check, string type, bool cook)
        {
            if (!Ready(out var g)) return;
            var cam = Camera.main;
            if (cam) { camPitch = cam.transform.eulerAngles.x; camYaw = cam.transform.eulerAngles.y; }
            var c = cam && g.GroundPoint(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out var gp) ? gp : Vector3.zero;
            if (!Spot(g, c, out int px, out int py)) { Debug.LogWarning("[OpenZ K378] no free spot near the view's middle"); return; }
            var o = g.World.CreateObject("vehicle", type, px, py, Enemy(g));
            if (o == null) { Debug.LogWarning("[OpenZ K378] could not make a " + type); return; }
            g.SpawnUnitView(o); unit = o;
            at = ZGame.PxToWorld(o.CenterX, o.CenterY); at.y = g.GroundY(at.x, at.z);
            name = check; lead = $"an enemy {type} tank #{o.Id} killed near the view's middle; {(cook ? "it burns out and cooks off (5-7 s)" : "its wreck breaks apart at once")}";
            breaks0 = OzTankWreck.Breaks; pieces0 = OzTankWreck.PiecesMade; chunked0 = WreckPieces.Chunked; cores0 = WreckPieces.CoresAdded;
            added0 = OzRockBody.Added[(int)OzRockBody.Kind.WreckPiece]; fracCuts0 = OzFractureCut.Cuts; fracFall0 = OzFractureCut.Fallbacks;
            notReady0 = OzTankWreck.NotReady; timeouts0 = OzTankWreck.HoldTimeouts;
            force0 = WreckFx.Force; atDeath0 = WreckFx.BreakAtDeath; WreckFx.Force = 1; WreckFx.BreakAtDeath = !cook;
            step = -1; brokeAt = -1f; restAt = -1f; shotAt = -1f; killAt = Time.realtimeSinceStartup + 1f; until = Time.realtimeSinceStartup + 40f;
            Debug.Log("[OpenZ K378] " + check + ": " + lead);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
        }

        static void PutBack() { WreckFx.Force = force0; WreckFx.BreakAtDeath = atDeath0; }

        static void Shot(string tag, float dist, float pitch, float dyaw)
        {
            try { var path = OpenZShot.Shot(Middle(), dist, pitch, camYaw + dyaw, Path.Combine(Out, "k378_" + name + "_" + tag + ".png"), 1920, 1080); Debug.Log("[OpenZ K378] picture " + path); }
            catch (System.Exception e) { Debug.LogWarning("[OpenZ K378] picture " + tag + " failed: " + e.Message); }
        }

        static Vector3 Middle()
        {
            var ps = OzTankWreck.LastPieces.Where(p => p).ToList();
            if (ps.Count == 0) return at;
            var s = Vector3.zero; foreach (var p in ps) s += p.position; s /= ps.Count; s.y = at.y; return s;
        }

        static List<Rigidbody> Bodies() => OzTankWreck.LastPieces.Where(p => p && p.parent).Select(p => p.parent.GetComponent<Rigidbody>()).Where(b => b).Distinct().ToList();

        static void Tick()
        {
            if (!Application.isPlaying) { EditorApplication.update -= Tick; PutBack(); return; }
            float now = Time.realtimeSinceStartup;
            if (step == -1 && now >= killAt)
            {
                var g = ZGame.I; if (g && unit != null && !unit.Destroyed) { unit.DamageHealth(unit.Health); g.World.UpdateObjectHealth(unit); }
                step = 0;
            }
            if (step == 0 && OzTankWreck.Breaks > breaks0) { step = 1; brokeAt = now; shotAt = now + 1.5f; PutBack(); }
            if (step == 0 && WreckPieces.Chunked > chunked0) { step = 1; brokeAt = now; shotAt = now + 1.5f; PutBack(); }        // it broke the old way: report it
            if (step == 1 && now >= shotAt) { Shot("70m_1_5s", 70f, 55f, 0f); step = 2; }
            if (step == 2)
            {
                var bodies = Bodies();
                bool rested = bodies.Count == 0 || bodies.All(b => b.isKinematic || b.IsSleeping());
                if ((rested && now - brokeAt > 4f) || now - brokeAt > 14f) { restAt = now; step = 3; }
            }
            if (step == 3 || now > until)
            {
                EditorApplication.update -= Tick; PutBack();
                Shot("70m_rest", 70f, 55f, 0f); Shot("35m_rest", 35f, 45f, 0f); Shot("35m_side_rest", 35f, 45f, 150f);
                Report();
            }
        }

        static void Report()
        {
            var sb = new StringBuilder("[OpenZ K378] " + name + ": " + lead + "\n");
            int made = OzTankWreck.PiecesMade - pieces0, breaks = OzTankWreck.Breaks - breaks0;
            sb.AppendLine(OzV11Kit.Verdict(OzTankWreck.On && breaks == 1 && made >= 6, $"the wreck broke into its own broken pieces: {made} pieces in {breaks} break(s) (wanted >= 6 in 1); {OzTankWreck.LastBreak}"));
            sb.AppendLine(OzV11Kit.Verdict(WreckPieces.Chunked == chunked0 && WreckPieces.CoresAdded == cores0, $"no baked bins and no dark core blocks in this break: bins +{WreckPieces.Chunked - chunked0}, cores +{WreckPieces.CoresAdded - cores0}"));
            var bodies = Bodies();
            sb.AppendLine(OzV11Kit.Verdict(bodies.Count >= 4 && bodies.Count <= WreckPieces.MaxGroups, $"bodies: {bodies.Count} (wanted 4-{WreckPieces.MaxGroups}; the torn track lengths, wheels and lamps ride on the pieces they sit in, one wheel loose)"));
            int capped = 0, steel = 0, coreMats = 0, tris = 0;
            foreach (var b in bodies)
                foreach (var r in b.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mf = r.GetComponent<MeshFilter>(); var m = mf ? mf.sharedMesh : null; if (!m) continue;
                    for (int s = 0; s < m.subMeshCount; s++) tris += (int)(m.GetIndexCount(s) / 3);
                    var mats = r.sharedMaterials;
                    if (r.name.StartsWith("OZ_TankPiece_") && m.subMeshCount > 1) { capped++; if (mats.Length > 0 && mats[mats.Length - 1] && mats[mats.Length - 1].name.StartsWith("OZ_TankWreckFracture")) steel++; }
                    foreach (var mat in mats) if (mat && mat.name.StartsWith("OZ_WreckChunkCore")) coreMats++;
                    if (r.name == "OZ_ChunkCore") coreMats++;
                }
            sb.AppendLine(OzV11Kit.Verdict(capped > 0 && steel == capped && coreMats == 0, $"broken faces in dark broken steel: {steel} of {capped} capped pieces ({OzTankWreck.MatState}); core-block materials {coreMats}"));
            sb.AppendLine(OzV11Kit.Verdict(tris <= 26000, $"triangles in all the bodies {tris} (wanted <= 26000)"));
            sb.AppendLine(OzV11Kit.Verdict(OzTankWreck.FrameMsMax <= 6f, $"the main thread's heaviest frame of tank-wreck work this session {OzTankWreck.FrameMsMax:0.0} ms (wanted <= 6; the cuts on a worker thread, the slowest set {OzTankWreck.WorkerMsMax:0} ms there)"));
            sb.AppendLine(OzV11Kit.Verdict(OzTankWreck.HoldTimeouts == timeouts0 && OzTankWreck.NotReady == notReady0, $"the break waited for its set at most {OzTankWreck.LastHoldWait:0.00} s (timed out +{OzTankWreck.HoldTimeouts - timeouts0}, broke before a set was ready +{OzTankWreck.NotReady - notReady0})"));
            sb.AppendLine(OzV11Kit.Verdict(OzRockBody.Added[(int)OzRockBody.Kind.WreckPiece] - added0 >= bodies.Count, $"every body is made solid against the rocks and statics (items 331 / 332): +{OzRockBody.Added[(int)OzRockBody.Kind.WreckPiece] - added0} for {bodies.Count}"));
            sb.AppendLine(OzV11Kit.Verdict(OzFractureCut.Cuts == fracCuts0 && OzFractureCut.Fallbacks == fracFall0, $"the cannon's fracture counters untouched by the tank cuts: cuts +{OzFractureCut.Cuts - fracCuts0}, fallbacks +{OzFractureCut.Fallbacks - fracFall0}"));
            foreach (var b in bodies.OrderByDescending(b => b.mass))
            {
                var parts = string.Join(", ", b.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.name));
                var bb = b.GetComponentInChildren<BoxCollider>();
                sb.AppendLine($"      body {b.name} {b.mass:0} kg, {(b.isKinematic ? "rests" : b.IsSleeping() ? "asleep" : "MOVING")}, box {(bb ? bb.size.ToString("F2") : "-")}: {parts}");
            }
            sb.AppendLine("      " + OzTankWreck.State);
            sb.Append(OzTankWreck.SetsText());
            sb.AppendLine($"      LOOK at the pictures {Out}/k378_{name}_*.png beside the reference crops of item 378 (bugs/k378_vehicle_wreck_not_boxes/owner_refs) and out/sbs_owner_vs_new_*.jpg: no clean boxes, no black block, broken edges with thickness, the tank's own paint charred, nothing white, no grid");
            var text = sb.ToString();
            Debug.Log(text);
            try { File.WriteAllText(Path.Combine(Out, "k378_" + name + "_report.txt"), text); } catch (System.Exception) { }
        }
    }
}
