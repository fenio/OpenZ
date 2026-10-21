// OpenZ (GPL-3.0) - Level 16 endings, version 1.1: no enemy unit in the scene's picture.
//  From the cut to the loungers until the level is left (the scene's last picture stays under the end screen, which only dims it), the
//  units of the other side (not the player's, not the empty neutral ones) are not drawn when they stand inside the scene camera's view
//  (with EndingEnemyMargin metres of room round each, so a limb or a long shadow never pokes into the frame) or within EndingEnemyRadius
//  metres of the set, seen or not. In the LOSE scene they are the winners standing about; in the WIN scene, the side the blast wave kills.
//  What is left out of the picture:
//   - every renderer under the unit's view (body, weapon, wreck, the effects parented to it, and so their shadows), by
//     Renderer.forceRenderingOff: the game's own code switches units on and off through Renderer.enabled, which is never touched here;
//   - the lights under it;
//   - what the game draws for it without a renderer of its own: its team disc on the ground (TeamReadability), its blinkers (UnitBlinkers),
//     its headlights' glows, beams and pools (VehicleLights), a pyro's pilot flame (PyroPilotFlame), its tracks and dust (TrackMarks),
//     its damage smoke, sparks and fire (OpenZDamageStates): each of them asks EndingHides(view).
//   The health bars, selection brackets and labels belong to the HUD, which the scene hides already; the ground selection rings are
//   only ever drawn for the player's own units.
//  The test runs on every frame of the scene (EndingRoutine, after its camera is placed) and once more right before the game camera
//  draws (a unit that came out of a vehicle or a factory this frame). A unit once left out stays out until the level is left, so
//  nothing pops when it walks out of the frame.
//  Restored exactly: each part gets back the forceRenderingOff it had (or the one its own code gave it during the scene), each light the
//  switch it had. View only: the rules are read for who owns a unit and nothing else; nothing in them is written.
// Play check: return OpenZ.ZodBriefing.EndingEnemyStatus();   the menus OpenZ/V1.1/KQ8 ... (Editor/OzKQ8EndingEnemiesCheck.cs)
// WRITTEN BY READING ONLY: compiled off-line with the project's code, never run in the engine. Remove this line once it runs.
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    public sealed partial class ZodBriefing
    {
        /// <summary>The scene leaves the other side's units out of its picture (false = drawn as before, for a before / after picture).</summary>
        public static bool EndingHideEnemies = true;
        /// <summary>Round the set (m, on the ground): every unit of the other side this near is left out, in the frame or not (its shadow
        /// can fall into it).</summary>
        public static float EndingEnemyRadius = 35f;
        /// <summary>Room round each unit for the view test (m): body, limbs, a long morning shadow.</summary>
        public static float EndingEnemyMargin = 6f;
        /// <summary>How far the view test reaches from the camera (m, on the ground; the whole map by default).</summary>
        public static float EndingEnemyViewReach = 1000f;
        /// <summary>Counts of this scene (Play check): units left out (in the frame / near the set), their parts and lights, parts or
        /// lights their own code switched on again at least once (off again for the scene), units that died while left out (of them in
        /// the frame), passes of the test.</summary>
        public static int EndingEnemyUnits, EndingEnemyInView, EndingEnemyNear, EndingEnemyParts, EndingEnemyLights, EndingEnemyReshown,
            EndingEnemyDied, EndingEnemyDiedInView, EndingEnemyPasses;

        static bool endEnemyOn, endEnemyHooked, endEnemyLogged;
        static Vector3 endEnemySet; static int endEnemyDrawFrame = -10;
        static readonly HashSet<UnitView> endEnemyViews = new HashSet<UnitView>();
        static readonly List<UnitView> endEnemyList = new List<UnitView>();
        static readonly HashSet<UnitView> endEnemyDead = new HashSet<UnitView>();
        static readonly HashSet<UnityEngine.Object> endEnemyAgain = new HashSet<UnityEngine.Object>();
        static readonly Dictionary<Renderer, bool> endEnemyRends = new Dictionary<Renderer, bool>();
        static readonly Dictionary<Light, bool> endEnemyLights = new Dictionary<Light, bool>();
        static readonly List<Renderer> endEnemyR = new List<Renderer>();
        static readonly List<Light> endEnemyL = new List<Light>();
        static readonly Plane[] endEnemyPlanes = new Plane[6];

        /// <summary>The scene leaves this unit out of its picture now: TeamReadability, UnitBlinkers, VehicleLights, PyroPilotFlame,
        /// TrackMarks and OpenZDamageStates draw nothing for it. Always false outside the level 16 scene.</summary>
        public static bool EndingHides(UnitView v) => endEnemyViews.Count > 0 && (object)v != null && endEnemyViews.Contains(v);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetEnemies()
        {   // a new Play session starts clean
            if (endEnemyHooked) RenderPipelineManager.beginCameraRendering -= EnemyBeginCamera;
            endEnemyOn = endEnemyHooked = endEnemyLogged = false;
            endEnemyViews.Clear(); endEnemyList.Clear(); endEnemyDead.Clear(); endEnemyAgain.Clear(); endEnemyRends.Clear(); endEnemyLights.Clear();
            endEnemyDrawFrame = -10;
        }

        /// <summary>EndingRoutine, at the cut (before the scene's first frame): the scene starts with nothing left out.</summary>
        static void EndingEnemiesBegin()
        {
            if (endEnemyRends.Count > 0 || endEnemyLights.Count > 0) EndingEnemiesRestore();
            EndingEnemyUnits = EndingEnemyInView = EndingEnemyNear = EndingEnemyParts = EndingEnemyLights = EndingEnemyReshown = 0;
            EndingEnemyDied = EndingEnemyDiedInView = EndingEnemyPasses = 0;
            endEnemyLogged = false;
            endEnemyOn = EndingHideEnemies;
            if (endEnemyOn && !endEnemyHooked) { endEnemyHooked = true; RenderPipelineManager.beginCameraRendering += EnemyBeginCamera; }
            if (!endEnemyOn) Debug.Log("[ZodCameo] ending enemies: drawn as before (switch off)");
        }

        /// <summary>EndingRoutine, every frame after the scene placed its camera (null = the radius only).</summary>
        static void EndingEnemiesTick(Camera cam, Transform set)
        {
            if (!endEnemyOn) return;
            var g = ZGame.I; if (!g || g.World == null) return;
            if (set) endEnemySet = set.position;
            // the parts of the units already left out are switched off again right before the camera draws (a robot's cable writes
            // its own switch in every LateUpdate, after this): here only the new ones, unless no draw of the game camera came by lately
            EnemyPass(g, cam, Time.frameCount - endEnemyDrawFrame > 2);
        }

        static void EnemyBeginCamera(ScriptableRenderContext ctx, Camera c)
        {
            if (!endEnemyOn || c == null || c.cameraType != CameraType.Game || c != Camera.main) return;
            endEnemyDrawFrame = Time.frameCount;
            var g = ZGame.I; if (g && g.World != null) EnemyPass(g, c, true);
        }

        /// <summary>Play check: one pass of the test now, with the game camera where it stands (nothing when no scene runs).</summary>
        public static void EndingEnemiesProbe()
        {
            var g = ZGame.I; if (!endEnemyOn || !g || g.World == null) return;
            EnemyPass(g, Camera.main, true);
        }

        /// <summary>One pass: the units of the other side not left out yet are tested; the parts of the new ones (all = of every one
        /// left out) are switched off.</summary>
        static void EnemyPass(ZGame g, Camera cam, bool all)
        {
            EndingEnemyPasses++;
            int known = endEnemyList.Count;
            bool planes = cam;
            if (planes) GeometryUtility.CalculateFrustumPlanes(cam, endEnemyPlanes);
            Vector3 cp = planes ? cam.transform.position : endEnemySet;
            float r2 = EndingEnemyRadius * EndingEnemyRadius, reach2 = EndingEnemyViewReach * EndingEnemyViewReach;
            foreach (var kv in g.Views)
            {
                var v = kv.Value;
                if (!v || endEnemyViews.Contains(v)) continue;
                var o = v.Obj;
                if (o == null || !o.IsUnit || o.Owner == Team.Neutral || o.Owner == g.player) continue;
                Vector3 p = v.transform.position;
                float dx = p.x - endEnemySet.x, dz = p.z - endEnemySet.z;
                bool near = dx * dx + dz * dz <= r2, seen = false;
                if (!near && planes)
                {
                    float cx = p.x - cp.x, cz = p.z - cp.z;
                    if (cx * cx + cz * cz <= reach2)
                    {   // the unit's size (a robot about 1.2 m round its middle, a vehicle or a gun 3 m) and the room round it
                        float half = (o.Kind == ZKind.Robot ? 1.2f : 3f) + EndingEnemyMargin;
                        seen = GeometryUtility.TestPlanesAABB(endEnemyPlanes, new Bounds(p + Vector3.up * 1.2f, Vector3.one * (2f * half)));
                    }
                }
                if (!near && !seen) continue;
                endEnemyViews.Add(v); endEnemyList.Add(v); EndingEnemyUnits++;
                if (near) EndingEnemyNear++; else EndingEnemyInView++;
            }
            for (int i = all ? 0 : known; i < endEnemyList.Count; i++)
            {
                var v = endEnemyList[i]; if (!v) continue;
                HideParts(v);
                if (!endEnemyDead.Contains(v) && (v.Dying || v.Obj == null || v.Obj.Destroyed))
                {
                    endEnemyDead.Add(v); EndingEnemyDied++;
                    if (planes && GeometryUtility.TestPlanesAABB(endEnemyPlanes, new Bounds(v.transform.position + Vector3.up, Vector3.one * 4f))) EndingEnemyDiedInView++;
                }
            }
            if (!endEnemyLogged) { endEnemyLogged = true; Debug.Log("[ZodCameo] ending enemies at the cut: " + EndingEnemyStatus()); }
        }

        /// <summary>Every renderer and light under the unit's view off for the scene; what each one was (or what its own code made it
        /// since) is kept for the restore.</summary>
        static void HideParts(UnitView v)
        {
            v.GetComponentsInChildren(true, endEnemyR);
            for (int i = 0; i < endEnemyR.Count; i++)
            {
                var r = endEnemyR[i]; if (!r) continue;
                if (!endEnemyRends.ContainsKey(r)) { endEnemyRends[r] = r.forceRenderingOff; EndingEnemyParts++; }
                else if (!r.forceRenderingOff) { endEnemyRends[r] = false; if (endEnemyAgain.Add(r)) EndingEnemyReshown++; }   // its own code showed it again: that is what it gets after the scene
                if (!r.forceRenderingOff) r.forceRenderingOff = true;
            }
            v.GetComponentsInChildren(true, endEnemyL);
            for (int i = 0; i < endEnemyL.Count; i++)
            {
                var l = endEnemyL[i]; if (!l) continue;
                if (!endEnemyLights.ContainsKey(l)) { endEnemyLights[l] = l.enabled; EndingEnemyLights++; }
                else if (l.enabled) { endEnemyLights[l] = true; if (endEnemyAgain.Add(l)) EndingEnemyReshown++; }
                if (l.enabled) l.enabled = false;
            }
        }

        /// <summary>The level is left (EndingRestore), or a new scene begins: everything left out is drawn as before.</summary>
        static void EndingEnemiesRestore()
        {
            int parts = 0, lights = 0, units = endEnemyViews.Count;
            foreach (var kv in endEnemyRends) if (kv.Key) { if (kv.Key.forceRenderingOff != kv.Value) kv.Key.forceRenderingOff = kv.Value; parts++; }
            foreach (var kv in endEnemyLights) if (kv.Key) { if (kv.Key.enabled != kv.Value) kv.Key.enabled = kv.Value; lights++; }
            if (endEnemyHooked) { endEnemyHooked = false; RenderPipelineManager.beginCameraRendering -= EnemyBeginCamera; }
            endEnemyOn = false;
            endEnemyViews.Clear(); endEnemyList.Clear(); endEnemyDead.Clear(); endEnemyAgain.Clear(); endEnemyRends.Clear(); endEnemyLights.Clear();
            if (units > 0) Debug.Log($"[ZodCameo] ending enemies drawn again: {units} units, {parts} parts and {lights} lights given back their own state");
        }

        /// <summary>Play check: the restore now (as leaving the level does it) and the proof that every part and light has its own state back.</summary>
        public static string EndingEnemiesRestoreNow()
        {
            var want = new List<KeyValuePair<Renderer, bool>>(endEnemyRends);
            var wantL = new List<KeyValuePair<Light, bool>>(endEnemyLights);
            int units = endEnemyViews.Count;
            EndingEnemiesRestore();
            int n = 0, bad = 0;
            foreach (var kv in want) if (kv.Key) { n++; if (kv.Key.forceRenderingOff != kv.Value) bad++; }
            foreach (var kv in wantL) if (kv.Key) { n++; if (kv.Key.enabled != kv.Value) bad++; }
            bool ok = bad == 0 && endEnemyViews.Count == 0 && !EndingHides(null);
            return $"{(ok ? "PASS" : "FAIL")} restore: {units} units, {n} parts and lights checked, {bad} not as they were, units still left out {endEnemyViews.Count}";
        }

        /// <summary>Play check: the test's numbers and counts in one line.</summary>
        public static string EndingEnemyStatus() =>
            $"ending enemies: {(endEnemyOn ? "left out" : EndingHideEnemies ? "off (no scene)" : "drawn (switch off)")}, radius {EndingEnemyRadius:F0} m round the set, room {EndingEnemyMargin:F0} m, view reach {EndingEnemyViewReach:F0} m; "
            + $"units left out {EndingEnemyUnits} (in the frame {EndingEnemyInView}, near the set {EndingEnemyNear}), parts {EndingEnemyParts}, lights {EndingEnemyLights}, switched on again by their own code {EndingEnemyReshown}, "
            + $"died while left out {EndingEnemyDied} (in the frame {EndingEnemyDiedInView}), passes {EndingEnemyPasses}";
    }
}
