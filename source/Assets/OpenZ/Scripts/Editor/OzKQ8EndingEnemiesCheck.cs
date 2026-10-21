// OpenZ (GPL-3.0) - version 1.1, the level 16 endings leave the other side's units out of the scene's picture: the checks in Play.
//  * OpenZ/V1.1/KQ8 Ending Enemies: Stage The Loss (Play) / Stage The Win (Play): the player's (or the other) fort falls now
//    (OpenZVictory.DebugFortFalls); from the cut to the loungers every look of the editor counts the other side's units that stand in the
//    scene camera's frame or within the radius of the set and still have a part drawn (must stay 0), and the ones the drawers are not told
//    to leave out (EndingHides, must stay 0); at the cut, one pass of the test is run between two fingerprints of the rules' whole state
//    in the same frame (they must be equal: the test writes nothing in the rules); 14 s after the cut: PASS / FAIL lines.
//  * ... Put Enemy Units In The Shot (Play, during the scene): THE CHECK's own units of the other side (two grunt squads and a light tank)
//    at 8, 16 and 24 m in front of the scene camera: they must be left out from their first frame (this check's spawns, not the kit's).
//  * ... Restore Now (Play): the restore of leaving the level, now, with the proof that every part and light has its own state back.
//  * ... Status: the test's counts.
// WRITTEN BY READING ONLY: compiled off-line with the project's code, never run in the engine. Remove this line once it runs.
using System;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    public static class OzKQ8EndingEnemiesCheck
    {
        static bool watching, cutSeen, printed; static float cutAt; static int looks, drawnLooks, missLooks, mostDrawn, ownDrawnMost;
        static string simBefore = "", simAfter = "", lastDrawn = "";

        [MenuItem("OpenZ/V1.1/KQ8 Ending Enemies: Stage The Loss (Play)")]
        static void StageLoss() => Stage(false);

        [MenuItem("OpenZ/V1.1/KQ8 Ending Enemies: Stage The Win (Play)")]
        static void StageWin() => Stage(true);

        [MenuItem("OpenZ/V1.1/KQ8 Ending Enemies: Put Enemy Units In The Shot (Play, during the scene)")]
        static void PutInShot()
        {
            var g = ZGame.I; var cam = Camera.main;
            if (!Application.isPlaying || !g || g.World == null || !cam || !ZodBriefing.EndingHolding) { Debug.LogWarning("[KQ8] only while the level 16 ending scene runs"); return; }
            var W = g.World; Team other = g.player == Team.Red ? Team.Blue : Team.Red;
            Vector3 f = cam.transform.forward; f.y = 0f; f = f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
            int n = 0;
            foreach (var (d, type) in new[] { (8f, "grunt"), (16f, "light"), (24f, "grunt") })
            {
                var px = ZGame.WorldToPx(cam.transform.position + f * d);
                int x = (px.x >> 4) * 16, y = (px.y >> 4) * 16;
                ZObject o = ZBuildLists.IsRobot(type) ? W.CreateRobotGroup(type, x, y, other) : W.CreateObject("vehicle", type, x, y, other);
                if (o == null) { Debug.LogWarning($"[KQ8] no room for a {type} at {d:F0} m"); continue; }
                g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
                n++; Debug.Log($"[KQ8] the check's own {other} {type} id {o.Id} at {d:F0} m in front of the scene camera (tile {x >> 4},{y >> 4})");
            }
            Debug.Log($"[KQ8] {n} of 3 put in the shot: they must be left out from their first frame (the counts go on)");
        }

        [MenuItem("OpenZ/V1.1/KQ8 Ending Enemies: Restore Now (Play)")]
        static void RestoreNow()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[KQ8] enter Play first"); return; }
            var g = ZGame.I; string a = g && g.World != null ? Print(g.World) : "";
            string r = ZodBriefing.EndingEnemiesRestoreNow();
            string b = g && g.World != null ? Print(g.World) : "";
            Debug.Log("[KQ8] " + r);
            Line("the restore writes nothing in the rules (same frame, fingerprint before and after)", a == b, a + " / " + b);
        }

        [MenuItem("OpenZ/V1.1/KQ8 Ending Enemies: Status")]
        static void Status() => Debug.Log("[KQ8] " + ZodBriefing.EndingEnemyStatus());

        static void Stage(bool win)
        {
            var g = ZGame.I;
            if (!Application.isPlaying || !g || g.World == null || g.level != ZodBriefing.EndingLevel) { Debug.LogWarning("[KQ8] enter Play on level 16 first"); return; }
            int forts = OpenZVictory.DebugFortFalls(win);
            Debug.Log($"[KQ8] staged the {(win ? "win" : "loss")}: {forts} fort(s) down; watching for the cut to the loungers");
            watching = true; cutSeen = printed = false; looks = drawnLooks = missLooks = mostDrawn = ownDrawnMost = 0; simBefore = simAfter = lastDrawn = "";
            EditorApplication.update -= Watch; EditorApplication.update += Watch;
        }

        static void Watch()
        {
            if (!Application.isPlaying || !watching) { EditorApplication.update -= Watch; watching = false; return; }
            var g = ZGame.I; var cam = Camera.main;
            if (!g || g.World == null || !cam) return;
            if (!ZodBriefing.EndingHolding) return;
            if (!cutSeen)
            {
                cutSeen = true; cutAt = Time.realtimeSinceStartup;
                simBefore = Print(g.World); ZodBriefing.EndingEnemiesProbe(); simAfter = Print(g.World);
                Debug.Log("[KQ8] the cut: " + ZodBriefing.EndingStatus);
            }
            if (EditorApplication.isPaused) return;
            looks++;
            Count(g, cam, out int drawn, out int miss, out int own, out string which);
            if (drawn > 0) { drawnLooks++; lastDrawn = which; }
            if (miss > 0) missLooks++;
            mostDrawn = Mathf.Max(mostDrawn, drawn); ownDrawnMost = Mathf.Max(ownDrawnMost, own);
            if (printed || Time.realtimeSinceStartup - cutAt < 14f) return;
            printed = true; watching = false; EditorApplication.update -= Watch;
            Debug.Log("[KQ8] " + ZodBriefing.EndingEnemyStatus());
            Line("the scene ran and the test ran in it", looks > 0 && ZodBriefing.EndingEnemyPasses > 0, $"looks {looks}, passes {ZodBriefing.EndingEnemyPasses}");
            Line($"no unit of the other side drawn in the frame or within {ZodBriefing.EndingEnemyRadius:F0} m of the set", drawnLooks == 0, $"{drawnLooks} of {looks} looks had one (most {mostDrawn}){(lastDrawn.Length > 0 ? ", last: " + lastDrawn : "")}");
            Line("each of them is left out for the ground discs, blinkers, headlights, pilot flames, tracks and damage fire", missLooks == 0, $"{missLooks} of {looks} looks had one the drawers were not told about");
            Line("the test writes nothing in the rules (one pass between two fingerprints, same frame)", simBefore.Length > 0 && simBefore == simAfter, simBefore + " / " + simAfter);
            Debug.Log($"[KQ8] for the record: the player's own units drawn in the frame at the most {ownDrawnMost} (not this kit's: the set's own 9 m clearing is the only rule for them)");
        }

        /// <summary>The other side's units in the camera's frame (their middle, 1 m round it) or within the radius of the set (less 1.5 m): how many
        /// still have a part drawn, how many EndingHides does not name; the player's own units with a part drawn in the frame.</summary>
        static void Count(ZGame g, Camera cam, out int drawn, out int miss, out int own, out string which)
        {
            drawn = miss = own = 0; which = "";
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            Vector3 set = ZodBriefing.EndingSet != null ? ZodBriefing.EndingSet.Point("between") : cam.transform.position;
            float rr = Mathf.Max(0f, ZodBriefing.EndingEnemyRadius - 1.5f), r2 = rr * rr;                        // 1.5 m inside the rule's own radius: its centre is the set's root, this one the cans' meeting point
            foreach (var kv in g.Views)
            {
                var v = kv.Value; var o = v ? v.Obj : null;
                if (o == null || !o.IsUnit || o.Owner == Team.Neutral) continue;
                Vector3 p = v.transform.position; float dx = p.x - set.x, dz = p.z - set.z;
                bool inFrame = GeometryUtility.TestPlanesAABB(planes, new Bounds(p + Vector3.up, Vector3.one * 2f));
                bool near = dx * dx + dz * dz <= r2;
                bool shown = false;
                foreach (var r in v.GetComponentsInChildren<Renderer>(true)) if (r && r.enabled && !r.forceRenderingOff && r.gameObject.activeInHierarchy) { shown = true; break; }
                if (o.Owner == g.player) { if (inFrame && shown) own++; continue; }
                if (!inFrame && !near) continue;
                if (shown) { drawn++; which = $"{o.Type} id {o.Id} at {Vector3.Distance(set, p):F0} m"; }
                if (!ZodBriefing.EndingHides(v)) miss++;
            }
        }

        /// <summary>The rules' whole state that a view could disturb: every object's id, side, place, motion, health and end, the clock,
        /// the counts of objects and events (64-bit FNV).</summary>
        static string Print(ZWorld W)
        {
            ulong h = 1469598103934665603UL;
            void Mix(long x) { unchecked { h ^= (ulong)x; h *= 1099511628211UL; } }
            void MixD(double d) => Mix(BitConverter.DoubleToInt64Bits(d));
            Mix(W.Objects.Count); Mix(W.Events.Count); MixD(W.Time);
            foreach (var o in W.Objects)
            {
                if (o == null) { Mix(-1); continue; }
                Mix(o.Id); Mix((int)o.Owner); Mix(o.LocX); Mix(o.LocY); MixD(o.Dx); MixD(o.Dy); MixD(o.XOver); MixD(o.YOver);
                Mix(o.Health); Mix(o.MaxHealth); Mix(o.Destroyed ? 1 : 0); MixD(o.KillAt);
            }
            return $"{W.Objects.Count} objects, time {W.Time:F3}, {h:x16}";
        }

        static void Line(string what, bool ok, string detail) => Debug.Log($"[KQ8] {(ok ? "PASS" : "FAIL")} {what}: {detail}");
    }
}
