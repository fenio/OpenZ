using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted checks of the level 16 shuttle, round 2 (editor only, not in the build). Each prints its lines and a last line
    ///   a  scene and rules     edit mode, level 16 scene open: the ship's prefab and parts, its size against the fort, where it
    ///                          stands, the searchlight beside it, the hut, the rule on and off, the sixteen tiles, the exit
    ///                          lanes, the routes with the game's path finder, levels 2 and 17
    ///   b  lights              Play on level 16: the lamps' and the neon strips' periods on the game clock, the band, the
    ///                          engines, the exhaust wisp, day against night, the searchlight on from dusk to dawn and its beam
    ///                          over the ship; four pictures
    ///   c  stages              Play on level 16, a fresh start: a heavy tank with the player's attack order shoots the ship to
    ///                          its damaged stage and then destroys it; the wreck, the lights, the smoke, the searchlight, the
    ///                          sounds, the tiles; four pictures
    ///   d  pictures            Play on level 16, a fresh start: the ship beside the fort by day and at night with the
    ///                          searchlight, red and blue
    /// </summary>
    public static class OzL16ShuttleCheck
    {
        static string OutDir { get { var p = Path.Combine(OpenZPaths.Records, "milestones", "v1_1_20261012", "J14_shuttle"); Directory.CreateDirectory(p); return p; } }
        sealed class Tally
        {
            public readonly List<string> lines = new List<string>(); public int ok, all; readonly string title;
            public Tally(string t) { title = t; }
            public void Line(bool pass, string text) { all++; if (pass) ok++; lines.Add((pass ? "PASS  " : "FAIL  ") + text); }
            public void Note(string text) { lines.Add("      " + text); }
            public string End(string file)
            {
                string verdict = (ok == all ? "PASS " : "FAIL ") + ok + "/" + all;
                string text = title + "\n" + string.Join("\n", lines) + "\n" + verdict;
                File.WriteAllText(Path.Combine(OutDir, file), text.Replace("\n", "\r\n"));
                if (ok == all) Debug.Log("[OpenZ L16] " + text); else Debug.LogWarning("[OpenZ L16] " + text);
                return verdict;
            }
        }
        static bool VehFree(ZWorld w, int x, int y) => w.Path.TilePassable(x, y, false) && w.Path.TilePassable(x + 1, y, false) && w.Path.TilePassable(x, y + 1, false) && w.Path.TilePassable(x + 1, y + 1, false);
        static (int x, int y)? Ring(ZWorld w, int cx, int cy, bool robot, int rmin, int rmax)
        {
            for (int r = rmin; r <= rmax; r++) for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue; int x = cx + dx, y = cy + dy;
                if (robot ? w.Path.TilePassable(x, y, true) : VehFree(w, x, y)) return (x, y);
            }
            return null;
        }
        static Renderer[] ShipRenderers(Transform root)
        {
            var all = root.GetComponentsInChildren<MeshRenderer>(true);
            return OpenZL16Shuttle.Ship.Select(n => (Renderer)all.FirstOrDefault(r => r.name == n)).ToArray();
        }

        // ------------------------------------------------------------------------------------------------ a: scene and rules
        [MenuItem("OpenZ/L16 Jungle/Checks/J14 a Shuttle in the scene and in the rules (edit mode)")]
        public static void SceneMenu() => Scene();

        public static string Scene()
        {
            var T = new Tally("J14 a: level 16, the shuttle beside the red fort (round 2: a third of the fort, searchlight, neon, exhaust): scene and rules");
            var env = GameObject.Find("Environment"); var root = env ? env.transform.Find(ZWorld.L16ShuttleUid) : null;
            var ls = root ? root.GetComponent<L16Shuttle>() : null;
            if (!root || !ls) { T.Line(false, "the level 16 scene is not open, or menu 14 was not run"); return T.End("check_a_scene.txt"); }
            var lights = root.GetComponent<OzShuttleLights>(); var exhaust = root.GetComponent<OzShuttleExhaust>();
            if (!lights || !exhaust) { T.Line(false, "the object in the scene is the first version's (no lights and exhaust components): run menu 14 of round 2"); return T.End("check_a_scene.txt"); }
            var rs = ShipRenderers(root);
            long ship = rs.Sum(r => OpenZL16Shuttle.Tris(r));
            T.Line(rs.All(r => r) && ship >= 6000 && ship <= 12000, $"the ship's parts, separate and named (hull, two engines, four legs; steady lights, lamp sets A and B, engine glow, three running neon sets, the blinking neon): {rs.Count(r => r)} of {rs.Length}, {ship} triangles together (wanted 6,000 to 12,000)");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(OpenZL16Shuttle.MatPath);
            var all = root.GetComponentsInChildren<MeshRenderer>(true);
            int oneMat = all.Count(r => r.sharedMaterials.Length == 1 && r.sharedMaterial == mat);
            bool team = mat && mat.shader && mat.shader.name == "OpenZ/TeamLit" && Mathf.Approximately(mat.GetFloat("_TeamMode"), 3f) && mat.GetTexture("_BaseMap") && mat.GetTexture("_EmissionMap") && mat.GetTexture("_BumpMap") && mat.GetTexture("_MaskMap");
            T.Line(oneMat == all.Length && team, $"one material on every part: {oneMat} of {all.Length} renderers; the game's team shader with the team mask in the colour picture's alpha (team mode 3), colour, emission, normal and metal pictures set: {(team ? "yes" : "NO")}");
            // one prefab, no place in it, the lights and the exhaust as components on its root
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OpenZL16Shuttle.PrefabPath);
            bool inst = PrefabUtility.IsPartOfPrefabInstance(root.gameObject) && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root.gameObject) == OpenZL16Shuttle.PrefabPath;
            bool clean = prefab && prefab.transform.localPosition == Vector3.zero && prefab.transform.localRotation == Quaternion.identity && prefab.transform.localScale == Vector3.one;
            var pf = prefab ? prefab.transform.Find("Model/MK_Front") : null; float pYaw = 999f;
            if (pf) { var d0 = pf.position - prefab.transform.position; d0.y = 0f; pYaw = Mathf.Atan2(d0.x, d0.z) * Mathf.Rad2Deg; }
            bool comps = prefab && prefab.GetComponent<OzShuttleLights>() && prefab.GetComponent<OzShuttleExhaust>() && prefab.GetComponent<TeamPaint>() && prefab.GetComponent<DetailProp>() && prefab.GetComponent<L16Shuttle>();
            T.Line(prefab && inst && clean && comps && Mathf.Abs(Mathf.DeltaAngle(pYaw, 0f)) <= 2f,
                   $"ONE PREFAB for a later unit version: {OpenZL16Shuttle.PrefabPath} {(prefab ? "exists" : "MISSING")}, the scene's ship is its instance: {(inst ? "yes" : "NO")}; its root at 0 0 0, not turned, scale 1 (no place baked in): {(clean ? "yes" : "NO")}; " +
                   $"the dome looks along the root's forward (off by {Mathf.Abs(Mathf.DeltaAngle(pYaw, 0f)):0.0} degrees); the lights, the exhaust, the team paint, the detail and the decoration's stages are components on its root: {(comps ? "yes" : "NO")}");
            var wp = (ls.pieces ?? new Transform[0]).Where(p => p).Concat(ls.wreck ? ls.wreck.Cast<Transform>() : Enumerable.Empty<Transform>()).ToList();
            int fromModel = 0; long wtris = 0;
            foreach (var p in wp)
            {
                var mf = p.GetComponent<MeshFilter>(); var me = mf ? mf.sharedMesh : null; if (!me) continue;
                if (AssetDatabase.GetAssetPath(me) == OpenZL16Shuttle.Model && me.name.StartsWith("WR_") && me.triangles.Length / 3 >= 200) fromModel++;
                wtris += OpenZL16Shuttle.Tris(p.GetComponent<Renderer>());
            }
            int flyOff = (ls.pieces ?? new Transform[0]).Count(p => p && !p.GetComponent<Renderer>().enabled);
            T.Line(wp.Count == 9 && fromModel == 9 && flyOff == 6 && ls.wreck && !ls.wreck.gameObject.activeSelf,
                   $"wreck pieces cut from the ship's own model (both dome halves, both engines, four legs, the rear body): {fromModel} of {wp.Count} meshes of {OpenZL16Shuttle.Model} with 200 triangles or more ({wtris} together); not drawn while the ship stands: {flyOff} of 6 flying pieces off, the wreck {(ls.wreck && !ls.wreck.gameObject.activeSelf ? "off" : "ON")}");
            // THE SIZE against the fort as the scene draws it
            var box = OpenZL16Shuttle.TightBox(rs, out bool exactBox);
            if (!exactBox) T.Note("the ship's mesh points could not be read: its box is taken from the renderers and is too wide (run menu 14 again: it imports the model readable)");
            var sides = OpenZL16Shuttle.Sides(root, rs);
            bool fortOk = OpenZL16Shuttle.FortBox(root.position, out var fort, out string fortName);
            float fortSide = fortOk ? Mathf.Max(fort.size.x, fort.size.z) : 0f, share = fortSide > 0f ? sides.x / fortSide : 0f, clear = fortOk ? box.min.x - fort.max.x : -1f;
            T.Line(fortOk && Mathf.Abs(share / OpenZL16Shuttle.ShareOfFort - 1f) <= 0.05f,
                   $"SIZE, \"about 1/3 of fort size\": the fort {fortName} is {(fortOk ? fort.size.x : 0f):0.00} x {(fortOk ? fort.size.z : 0f):0.00} m on the ground (its side {fortSide:0.00} m), the ship is {sides.x:0.00} m long, {sides.y:0.00} m across the engines, {box.size.y:0.00} m high: " +
                   $"its long side is {share:0.000} of the fort's side (a third within 5 %: 0.317 to 0.350)");
            var q = ZWorld.L16ShuttleSquare; int nT = q[2] * q[3];
            float sx0 = q[0] * 2f, sx1 = (q[0] + q[2]) * 2f, sz1 = -q[1] * 2f, sz0 = -(q[1] + q[3]) * 2f;
            float outSq = Mathf.Max(0f, Mathf.Max(Mathf.Max(sx0 - box.min.x, box.max.x - sx1), Mathf.Max(sz0 - box.min.z, box.max.z - sz1)));
            bool lowerHalf = fortOk && box.center.z < fort.center.z && box.min.z > fort.min.z - 1f && box.min.x > fort.max.x;
            T.Line(outSq <= 0.1f && fortOk && clear >= 3f && lowerHalf,
                   $"PLACE: right of the fort and level with its lower half: {(lowerHalf ? "yes" : "NO")}; {clear:0.00} m clear of the fort's east face (3 or more); inside its {q[2]} x {q[3]} tile square (out by {outSq:0.00} m): " +
                   $"the ship's box {box.min.x:0.00} to {box.max.x:0.00} east, {box.min.z:0.00} to {box.max.z:0.00} north ({box.size.x:0.00} x {box.size.z:0.00} m as it stands, turned)");
            var fr = ls.transform.Find("Model/MK_Front"); float yaw = 999f;
            if (fr) { var d = fr.position - root.position; d.y = 0f; yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }
            T.Line(Mathf.Abs(Mathf.DeltaAngle(yaw, OpenZL16Shuttle.Heading)) <= 3f, $"the dome and the window band look to {yaw:0} degrees (wanted {OpenZL16Shuttle.Heading:0}: towards the camera's side, 30 degrees to the right, as in the reference)");
            var col = GameObject.Find("COL_Terrain")?.GetComponent<Collider>(); Physics.SyncTransforms();
            var soles = OpenZL16Shuttle.PadsAgainstGround(root, col, out int feetN);
            float worstFoot = feetN == 4 ? soles.Max(v => Mathf.Abs(v)) : -1f, tiltNow = Vector3.Angle(Vector3.up, root.up);
            T.Line(feetN == 4 && worstFoot <= 0.05f && tiltNow <= 8f, $"it stands on the ground: each pad's sole against the ground under that pad (FL, FR, BL, BR; minus = in the ground): " +
                   $"{string.Join(", ", soles.Select(v => float.IsNaN(v) ? "?" : v.ToString("+0.00;-0.00")))} m, largest {worstFoot:0.000} m (0.05 allowed); the ship is tilted {tiltNow:0.0} degrees to the ground under its pads");
            var zod = GameObject.Find("ZodObjects"); var hut = zod ? zod.transform.Find(OpenZL16Shuttle.HutName) : null;
            T.Line(hut && !hut.gameObject.activeSelf && ls.replacedHut == hut.gameObject, $"the hut it replaces ({OpenZL16Shuttle.HutName}): {(hut ? (hut.gameObject.activeSelf ? "STILL DRAWN" : "kept, switched off") : "not found")}");
            var dp = root.GetComponent<DetailProp>(); var tp = root.GetComponent<TeamPaint>();
            T.Line(dp && dp.blastOnly && dp.breakEvent == "exp_vehicle_death" && tp && root.name == ZWorld.L16ShuttleUid, $"bound by its name {ZWorld.L16ShuttleUid}; explosives only; break sound exp_vehicle_death; team paint {(tp ? tp.Team.ToString() : "MISSING")}");
            // the lamps' ground glows, the neon parts, the exhaust points, the flying pieces' outlines, no idle sound
            float gx0 = q[0] * 2f - 3f, gx1 = (q[0] + q[2]) * 2f + 3f, gz1 = -q[1] * 2f + 3f, gz0 = -(q[1] + q[3]) * 2f - 3f;
            bool GlowOk(Transform t) => t && t.position.x > gx0 && t.position.x < gx1 && t.position.z > gz0 && t.position.z < gz1;
            float glowApart = lights.glowAtA && lights.glowAtB ? Vector3.Distance(lights.glowAtA.position, lights.glowAtB.position) : 0f;
            int shapes = (ls.pieceShapes ?? new L16Shuttle.PieceShape[0]).Count(s => s.points != null && s.points.Length >= 8);
            bool noLoop = dp && string.IsNullOrEmpty(dp.loopEvent) && string.IsNullOrEmpty(dp.loopRes);
            T.Line(GlowOk(lights.glowAtA) && GlowOk(lights.glowAtB) && glowApart > 5f && shapes == 6 && noLoop && ls.lights == lights && ls.exhaust == exhaust,
                   $"the two ground glows of the amber lamps have their places beside the ship's two sides ({glowApart:0.0} m apart): {(GlowOk(lights.glowAtA) && GlowOk(lights.glowAtB) ? "yes" : "NO")}; outlines of the flying wreck pieces: {shapes} of 6; idle sound: {(noLoop ? "none" : "ONE IS SET")}");
            var nr = (lights.neonRun ?? new Renderer[0]).Where(r => r).ToArray(); long[] nTris = nr.Select(r => OpenZL16Shuttle.Tris(r)).ToArray(); long nbTris = OpenZL16Shuttle.Tris(lights.neonBlink);
            T.Line(nr.Length == 3 && nTris.All(n => n >= 40) && nbTris >= 100 && lights.neonGain.x >= 1.63f * 1.5f && lights.neonGain.y > lights.neonGain.x,
                   $"NEON STRIPS as parts of their own: three running sets ({string.Join(", ", nTris)} triangles: a hoop round each engine behind its mouth, an arc over the roof behind the dome, a line under the window band) and the blinking set ({nbTris}: a ring on the roof, a ring round each pad); " +
                   $"a lit strip is written at {lights.neonGain.x:0.0} by day and {lights.neonGain.y:0.0} at night x its colour (the bloom takes what is over 1.63 by day, 0.89 at night)");
            bool mouths = exhaust.mouths != null && exhaust.mouths.Length == 2 && exhaust.mouths.All(m => m), vents = exhaust.vents != null && exhaust.vents.Length == 2 && exhaust.vents.All(m => m);
            float mouthsApart = mouths ? Vector3.Distance(exhaust.mouths[0].position, exhaust.mouths[1].position) : 0f;
            T.Line(mouths && vents && Mathf.Abs(mouthsApart / Mathf.Max(0.1f, sides.y) - 1f) < 0.12f && exhaust.channel == "leg_smoke",
                   $"EXHAUST points: the two engine mouths ({(mouths ? "set" : "MISSING")}, {mouthsApart:0.00} m apart: the ship is {sides.y:0.00} m across) and the two roof vents ({(vents ? "set" : "MISSING")}); the game's soft smoke channel {exhaust.channel}, a puff every {exhaust.every.x:0.0} to {exhaust.every.y:0.0} s from each");
            // the searchlight beside it
            var lampT = env.transform.Find(OpenZL16Shuttle.SearchlightName); var fl = lampT ? lampT.GetComponent<OzFieldSearchlight>() : null; var ldp = lampT ? lampT.GetComponent<DetailProp>() : null;
            if (!OpenZL16Shuttle.WithSearchlight) T.Line(!lampT && !ls.searchlight, "the searchlight beside the ship is REMOVED from the level (the owner's item 173): " + (lampT ? "STILL IN THE SCENE: " + lampT.name : "no " + OpenZL16Shuttle.SearchlightName + " in the scene") + ", the ship holds " + (ls.searchlight ? "ONE" : "none"));
            else if (!lampT || !fl || !ldp) T.Line(false, "the searchlight " + OpenZL16Shuttle.SearchlightName + " is not in the scene (menu 14)");
            else
            {
                float toHull = OpenZL16Shuttle.FlatDistance(rs, lampT.position); var lt = ZWorld.L16ShuttleSearchlightTile;
                bool onTile = Mathf.FloorToInt(lampT.position.x / 2f) == lt[0] && Mathf.FloorToInt(-lampT.position.z / 2f) == lt[1];
                bool onGround = OpenZL16Shuttle.GroundAt(col, lampT.position.x, lampT.position.z, out float lgy) && Mathf.Abs(lampT.position.y - lgy) <= 0.08f;
                var stand = lampT.Find("Model/SL_Stand"); var ruin = lampT.Find("Model/RUIN_Stand"); long st = stand ? OpenZL16Shuttle.Tris(stand.GetComponent<Renderer>()) : 0;
                bool nameOk = lampT.name.IndexOf("lamp", StringComparison.OrdinalIgnoreCase) < 0;                 // the night lights take every child of Environment with "lamp" in its name for a lamp post
                T.Line(toHull >= 3f && toHull <= 5f && onTile && onGround && fl.headMount && fl.aimAt == root && ls.searchlight == fl && ldp.blastOnly && stand && ruin && !ruin.gameObject.activeSelf && st >= 200 && st <= 1200 && nameOk,
                       $"THE SEARCHLIGHT {lampT.name}: an army trailer ({st} triangles) with the game's own searchlight head put on in Play; {toHull:0.00} m from the ship's hull (3 to 5); on tile {lt[0]},{lt[1]}: {(onTile ? "yes" : "NO")}; on the ground: {(onGround ? "yes" : "NO")}; " +
                       $"it watches the ship: {(fl.aimAt == root ? "yes" : "NO")}; on from {fl.onAt:0.000} to {fl.offAt:0.000} of the day clock; a prop of its own, explosives only, the burnt stand kept for its break: {(ruin && !ruin.gameObject.activeSelf ? "yes" : "NO")}; the ship takes it along: {(ls.searchlight == fl ? "yes" : "NO")}");
            }

            // the rules
            bool was = ZWorld.L16ShuttleDeviation;
            try
            {
                string settings = Resources.Load<TextAsset>("OpenZ/default_settings").text, m16 = Resources.Load<TextAsset>("OpenZ/Maps/L16_grid").text;
                int hx = ZWorld.L16ShuttleHutTile[0], hy = ZWorld.L16ShuttleHutTile[1];
                ZWorld.L16ShuttleDeviation = true; var w = ZWorld.Create(settings, m16, 1);
                ZWorld.L16ShuttleDeviation = false; var z = ZWorld.Create(settings, m16, 1);
                var s = w.L16Shuttle; int hp = w.L16ShuttleHitPoints;
                bool HutAt(ZWorld ww) => ww.Objects.Any(o => o.Type == "hut" && !o.Destroyed && o.LocX == hx * 16 && o.LocY == hy * 16);
                T.Line(s != null && s.MaxHealth == hp && s.AttackedByExplosives && s.Owner == Team.Neutral && s.IsDestroyableImpass, $"rule on: the shuttle is a neutral map item of {hp} hit points ({ZWorld.L16ShuttleHutsOfHealth} x a hut's {hp / ZWorld.L16ShuttleHutsOfHealth}: a hut's for each of its {nT} tiles), explosives only, a destroyable barrier");
                T.Line(w.L16ShuttleTilesClosed == nT && w.L16ShuttleOpenTiles() == 0, $"tiles of its {q[2]} x {q[3]} square it closed: {w.L16ShuttleTilesClosed} of {nT}; tiles under it a unit can enter: {w.L16ShuttleOpenTiles()}");
                T.Line(!HutAt(w) && HutAt(z) && w.L16ShuttleHutRemoved, $"the hut of the map on tile {hx},{hy}: {(HutAt(w) ? "STILL in the rules" : "gone")} with the rule on, {(HutAt(z) ? "standing" : "MISSING")} with the rule off");
                // the footprint against the rules' square: every tile the ship's box covers by a third or more is closed
                int covered = 0, coveredClosed = 0;
                for (int ty = (int)Mathf.Floor(-box.max.z / 2f); ty <= (int)Mathf.Floor(-box.min.z / 2f); ty++)
                    for (int tx = (int)Mathf.Floor(box.min.x / 2f); tx <= (int)Mathf.Floor(box.max.x / 2f); tx++)
                    {
                        float ax = Mathf.Min(box.max.x, tx * 2f + 2f) - Mathf.Max(box.min.x, tx * 2f), az = Mathf.Min(box.max.z, -ty * 2f) - Mathf.Max(box.min.z, -ty * 2f - 2f);
                        if (ax <= 0f || az <= 0f || ax * az < 4f / 3f) continue;
                        covered++; if (!w.Path.TilePassable(tx, ty, true) && !w.Path.TilePassable(tx, ty, false)) coveredClosed++;
                    }
                T.Line(covered > 0 && covered == coveredClosed, $"tiles the drawn ship covers by a third or more: {covered}, closed for robots and vehicles: {coveredClosed}");
                // lanes, roads, start units, the searchlight's tile
                var lt2 = ZWorld.L16ShuttleSearchlightTile; int lanes = 0, roads = 0, starts = 0;
                var spots = new List<(int x, int y)>(); for (int y = q[1]; y < q[1] + q[3]; y++) for (int x = q[0]; x < q[0] + q[2]; x++) spots.Add((x, y)); spots.Add((lt2[0], lt2[1]));
                foreach (var (x, y) in spots)
                {
                    if (z.InExitLane(x * 16 + 8, y * 16 + 8, 16)) lanes++;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) if (z.Map.ClassAt(x + dx, y + dy) == ZClass.Road) { roads++; dy = 2; break; }
                    foreach (var o in z.Objects) if ((o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle || o.Kind == ZKind.Cannon) && (o.CenterX >> 4) == x && (o.CenterY >> 4) == y) starts++;
                }
                bool gate = z.NearGateFace(q[0] * 16, q[1] * 16, (q[0] + q[2]) * 16, (q[1] + q[3]) * 16, 16) || z.NearGateFace(lt2[0] * 16, lt2[1] * 16, lt2[0] * 16 + 16, lt2[1] * 16 + 16, 16);
                bool lampOpen = w.Path.TilePassable(lt2[0], lt2[1], true) && w.Path.TilePassable(lt2[0], lt2[1], false) && w.Map.ClassAt(lt2[0], lt2[1]) == ZClass.Ground;
                T.Line(lanes == 0 && !gate && roads == 0 && starts == 0 && lampOpen,
                       $"clear of the lanes: of the ship's {nT} tiles and the searchlight's tile, in a building's exit lane (the rules' own test, a tile's width added): {lanes}; within a tile of a fort's gate face: {(gate ? "YES" : "no")}; on a road or its verge (a road tile beside it): {roads}; a start unit on them: {starts}; " +
                       $"the searchlight's tile {lt2[0]},{lt2[1]} stays open ground for robots and vehicles (it closes nothing): {(lampOpen ? "yes" : "NO")}");
                // routes with the game's own path finder: rule off (the map with its hut) against rule on (the ship)
                var map = w.Map;
                foreach (bool robot in new[] { true, false })
                {
                    var keys = map.objects.Where(o => o.kind == "map_item:flag" || (o.kind.StartsWith("building:") && !o.kind.Contains("bridge"))).ToList();
                    (int, int)? Spot(ZWorld ww, ZMapData.Obj o)
                    {
                        for (int r = 0; r <= 7; r++) for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++)
                        {
                            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue; int x = o.x + dx, y = o.y + dy;
                            if (robot ? ww.Path.TilePassable(x, y, true) : VehFree(ww, x, y)) return (x, y);
                        }
                        return null;
                    }
                    int Route(ZWorld ww, (int x, int y) a, (int x, int y) b)
                    {
                        int h2 = robot ? 8 : 16; ww.Path.ForceSearch = true;
                        int id = ww.Path.FindPath(a.x * 16 + h2, a.y * 16 + h2, b.x * 16 + h2, b.y * 16 + h2, robot, false, out var pts);
                        if (id == 0 || pts == null || pts.Count == 0) return -1;
                        double len = 0; int px = a.x * 16 + h2, py = a.y * 16 + h2;
                        foreach (var p in pts) { len += Math.Sqrt((p.x - px) * (double)(p.x - px) + (p.y - py) * (double)(p.y - py)); px = p.x; py = p.y; }
                        return (int)len;
                    }
                    int pairs = 0, had = 0, kept = 0, longer = 0; double worst = 0;
                    for (int i = 0; i < keys.Count; i++) for (int j = i + 1; j < keys.Count; j++)
                    {
                        var a0 = Spot(z, keys[i]); var b0 = Spot(z, keys[j]); if (a0 == null || b0 == null) continue;
                        pairs++; int l0 = Route(z, a0.Value, b0.Value); if (l0 < 0) continue;
                        had++;
                        var a1 = Spot(w, keys[i]); var b1 = Spot(w, keys[j]); if (a1 == null || b1 == null) continue;
                        int l1 = Route(w, a1.Value, b1.Value); if (l1 < 0) continue;
                        kept++; if (l1 > l0 * 1.02 + 32) { longer++; worst = Math.Max(worst, (l1 - l0) / 8.0); }
                    }
                    T.Line(kept == had && had > 0, $"{(robot ? "robot" : "vehicle")} routes between the {keys.Count} flags and buildings with the game's path finder: {had} of {pairs} pairs had a route with the hut, {kept} still have one with the ship, {had - kept} cut; longer by more than 2 % + 4 m: {longer} (the longest detour {worst:0} m)");
                }
                ZWorld.L16ShuttleDeviation = true; var k = ZWorld.Create(settings, m16, 1); var p0 = k.L16Shuttle;
                if (p0 != null) { p0.DamageHealth(p0.Health); k.UpdateObjectHealth(p0); for (int i = 0; i < 100; i++) k.Tick(ZConst.TICK); }
                int sightStanding = w.L16ShuttleSightBlocked();
                bool across = w.Sight.ClearUncached((q[0] - 2) * 16 + 16, q[1] * 16 + 16, 0, (q[0] + q[2] + 1) * 16 + 16, q[1] * 16 + 16, 0);
                bool atShip = s != null && w.Sight.ClearUncached((q[0] - 2) * 16 + 16, q[1] * 16 + 16, 0, s.CenterX, s.CenterY, w.Sight.ExemptOf(s));
                T.Line(p0 != null && k.Get(p0.Id) == null && k.L16ShuttleOpenTiles() == nT, $"destroyed by the rules: the object is {(p0 != null && k.Get(p0.Id) == null ? "gone" : "STILL THERE")}, tiles of its square open again: {k.L16ShuttleOpenTiles()} of {nT}");
                bool acrossAfter = k.Sight.ClearUncached((q[0] - 2) * 16 + 16, q[1] * 16 + 16, 0, (q[0] + q[2] + 1) * 16 + 16, q[1] * 16 + 16, 0);
                T.Line(sightStanding == nT && !across && atShip && k.L16ShuttleSightBlocked() == 0 && acrossAfter,
                       $"direct fire and sight (as the hut it replaces on its tile): the standing ship blocks them on {sightStanding} of its {nT} tiles, a line across it from the lane to the far side is {(across ? "CLEAR" : "blocked")}, " +
                       $"the ship itself can be fired at: {(atShip ? "yes" : "NO")}; destroyed: {k.L16ShuttleSightBlocked()} tiles block, the line is {(acrossAfter ? "clear" : "STILL BLOCKED")}");
                bool others = true; string ot = "";
                foreach (var lv in new[] { "L02", "L17" })
                {
                    string mj = Resources.Load<TextAsset>($"OpenZ/Maps/{lv}_grid").text;
                    ZWorld.L16ShuttleDeviation = true; var on = ZWorld.Create(settings, mj, 1, lv == "L17");
                    ZWorld.L16ShuttleDeviation = false; var off = ZWorld.Create(settings, mj, 1, lv == "L17");
                    bool eq = on.L16Shuttle == null && on.Objects.Count == off.Objects.Count && on.Objects.Zip(off.Objects, (a, b) => a.Id == b.Id && a.Type == b.Type && a.LocX == b.LocX && a.LocY == b.LocY).All(x => x);
                    others &= eq; ot += $" level {lv.Substring(1)}: {on.Objects.Count} / {off.Objects.Count} objects {(eq ? "identical" : "DIFFERENT")};";
                }
                T.Line(others, "levels 2 and 17 with the rule on and off:" + ot.TrimEnd(';'));
            }
            catch (Exception ex) { T.Line(false, "the rules part threw " + ex.GetType().Name + ": " + ex.Message); }
            finally { ZWorld.L16ShuttleDeviation = was; }
            return T.End("check_a_scene.txt");
        }

        // ------------------------------------------------------------------------------------------------ the step runner of the Play checks
        static IEnumerator<float> routine; static double wakeAt;
        static void Run(IEnumerator<float> r) { routine = r; wakeAt = 0; EditorApplication.update -= Tick; EditorApplication.update += Tick; }
        static void Tick()
        {
            if (routine == null || !Application.isPlaying) { EditorApplication.update -= Tick; routine = null; return; }
            if (EditorApplication.timeSinceStartup < wakeAt) return;
            bool more; try { more = routine.MoveNext(); } catch (Exception ex) { Debug.LogError("[OpenZ L16] check stopped: " + ex); more = false; }
            if (!more) { EditorApplication.update -= Tick; routine = null; return; }
            wakeAt = EditorApplication.timeSinceStartup + routine.Current;
        }
        static bool Ready(out string why)
        {
            why = !Application.isPlaying ? "enter Play on level 16 first" : !ZGame.I || ZGame.I.World == null || ZGame.I.level != "L16" ? "this is not a running level 16"
                : !L16Shuttle.I ? "the shuttle is not in the scene (menu 14, save, Play again)" : L16Shuttle.I.RuleOff ? "the rule is off ('Zod exact'): switch it on and start again"
                : !L16Shuttle.I.lights || !L16Shuttle.I.exhaust ? "the ship in the scene is the first version's (menu 14 of round 2, save, Play again)" : null;
            if (why != null) Debug.LogWarning("[OpenZ L16] " + why);
            return why == null;
        }
        static int Logged(float since, string key) => OzSoundLog.Since(since).Count(e => e.key == key);
        /// <summary>The owner's place picture, as the game shows it: the red fort and the ship from the normal camera at the standard zoom.</summary>
        const float ViewX = 114f, ViewZ = -227f, ViewH = 70f;
        const float DayT = 0.08f, NightT = 0.62f, DawnT = 0.93f;

        // ------------------------------------------------------------------------------------------------ the lights in the picture
        static Color32[] Grab(Camera cam, RenderTexture rt, Texture2D read)
        {
            var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { var old = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = old; }
            var prev = RenderTexture.active; RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false); read.Apply(false);
            RenderTexture.active = prev;
            return read.GetPixels32();
        }
        static int Differ(Color32[] x, Color32[] y)
        {
            int n = 0;
            for (int i = 0; i < x.Length && i < y.Length; i++) if (Mathf.Abs(x[i].r - y[i].r) >= 8 || Mathf.Abs(x[i].g - y[i].g) >= 8 || Mathf.Abs(x[i].b - y[i].b) >= 8) n++;
            return n;
        }
        /// <summary>The game camera's own picture, drawn again in the same moment with no lamp set lit, with set A lit, with set B lit
        /// (OzShuttleLights.ShowLamps: lenses and ground glows), with the neon strips dark and with all of them lit (ShowNeon): how
        /// many pixels change by 8 of 255 or more for each, and how many differ between two pictures of the same state.</summary>
        static (int a, int b, int neon, int same, int w, int h) LightPixels(OzShuttleLights L)
        {
            var cam = Camera.main; if (!cam || !L) return (0, 0, 0, 0, 0, 0);
            int W = Mathf.Max(16, cam.pixelWidth), H = Mathf.Max(16, cam.pixelHeight);
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32); var read = new Texture2D(W, H, TextureFormat.RGBA32, false);
            try
            {
                L.ShowNeon(0f, 0f); L.ShowLamps(0f, 0f); Grab(cam, rt, read); var d1 = Grab(cam, rt, read); var d2 = Grab(cam, rt, read);
                L.ShowLamps(1f, 0f); Grab(cam, rt, read); var pa = Grab(cam, rt, read);
                L.ShowLamps(0f, 1f); Grab(cam, rt, read); var pb = Grab(cam, rt, read);
                L.ShowLamps(0f, 0f); L.ShowNeon(1f, 1f); Grab(cam, rt, read); var pn = Grab(cam, rt, read);
                L.ShowNeon(0f, 0f); Grab(cam, rt, read);
                return (Differ(pa, d2), Differ(pb, d2), Differ(pn, d2), Differ(d1, d2), W, H);
            }
            finally { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(read); }
        }
        static List<double> Rises(List<double> ts, List<float> v, float at)
        {
            var r = new List<double>(); for (int i = 1; i < v.Count; i++) if (v[i - 1] < at && v[i] >= at) r.Add(ts[i]); return r;
        }
        static double MeanGap(List<double> r) { if (r.Count < 2) return -1; double s = 0; for (int i = 1; i < r.Count; i++) s += r[i] - r[i - 1]; return s / (r.Count - 1); }

        // ------------------------------------------------------------------------------------------------ b: lights
        [MenuItem("OpenZ/L16 Jungle/Checks/J14 b Shuttle lights, neon, exhaust and searchlight on the game clock, day and night (Play mode, about 2 minutes)")]
        public static void LightsMenu() { if (Ready(out _)) Run(LightsCheck()); }

        static IEnumerator<float> LightsCheck()
        {
            var T = new Tally("J14 b: level 16, the shuttle's lights (emission only), its neon strips, its exhaust wisp and the searchlight beside it");
            var ls = L16Shuttle.I; var L = ls.lights; var ex = ls.exhaust; var fl = ls.searchlight; var g = ZGame.I;
            var dn = UnityEngine.Object.FindFirstObjectByType<DayNight>(); float tod0 = dn ? dn.timeOfDay : 0f; bool ran = dn && dn.running;
            if (dn) { dn.running = false; dn.timeOfDay = DayT; dn.Apply(DayT); }
            OzL16Proof.Cam(ViewX, ViewZ, ViewH);
            yield return 4f;
            {   // in the running game: the size against the fort as it is drawn now, the tiles of the rules, the pads on the terrain
                var rs = ShipRenderers(ls.transform); var box = OpenZL16Shuttle.TightBox(rs, out _); var sides = OpenZL16Shuttle.Sides(ls.transform, rs);
                bool fortOk = OpenZL16Shuttle.FortBox(ls.transform.position, out var fort, out string fortName);
                float fortSide = fortOk ? Mathf.Max(fort.size.x, fort.size.z) : 0f, share = fortSide > 0f ? sides.x / fortSide : 0f;
                var q = ZWorld.L16ShuttleSquare; float sx0 = q[0] * 2f, sx1 = (q[0] + q[2]) * 2f, sz1 = -q[1] * 2f, sz0 = -(q[1] + q[3]) * 2f;
                float outSq = Mathf.Max(0f, Mathf.Max(Mathf.Max(sx0 - box.min.x, box.max.x - sx1), Mathf.Max(sz0 - box.min.z, box.max.z - sz1)));
                float worst = 0f; int pads = 0; var all = ls.GetComponentsInChildren<Transform>(true);
                foreach (var n in OpenZL16Shuttle.Pads) { var t = all.FirstOrDefault(x => x.name == n); if (!t) continue; pads++; worst = Mathf.Max(worst, Mathf.Abs(t.position.y - g.TerrainY(t.position.x, t.position.z))); }
                int closed = g.World.L16ShuttleTilesClosed, open = g.World.L16ShuttleOpenTiles();
                T.Line(fortOk && Mathf.Abs(share / OpenZL16Shuttle.ShareOfFort - 1f) <= 0.05f && outSq <= 0.1f && pads == 4 && worst <= 0.06f && closed == ZWorld.L16ShuttleTiles && open == 0,
                       $"in the running game: the ship is {sides.x:0.00} m long, the fort {fortName} {fortSide:0.00} m: {share:0.000} of the fort's side (a third within 5 %: 0.317 to 0.350); outside its {q[2]} x {q[3]} tiles by {outSq:0.00} m; " +
                       $"the rules closed {closed} of {ZWorld.L16ShuttleTiles} tiles under it ({open} open); its four pads against the game's terrain: the largest gap {worst:0.000} m (0.06 allowed)");
            }
            float tStart = Time.unscaledTime; int puffs0 = ex.Emitted;
            // sample the levels against the game clock for a little over four lamp periods
            var ts = new List<double>(); var la = new List<float>(); var lb = new List<float>(); var st = new List<float>(); var en = new List<float>();
            var n0 = new List<float>(); var n1 = new List<float>(); var n2 = new List<float>(); var nb = new List<float>();
            double t0 = OzShuttleLights.GameClock(), span = Math.Max(L.lampPeriod * 4.5 + L.enginePeriod, Math.Max(L.neonRunPeriod, L.neonBlinkPeriod) * 4.2); double real0 = EditorApplication.timeSinceStartup;
            int glowWrongA = 0, glowWrongB = 0, glowBoth = 0, glowLitA = 0, glowLitB = 0, aliveMost = 0;
            while (OzShuttleLights.GameClock() - t0 < span && EditorApplication.timeSinceStartup - real0 < 120.0)
            {
                ts.Add(OzShuttleLights.GameClock()); la.Add(L.LampA); lb.Add(L.LampB); st.Add(L.Steady); en.Add(L.Engine);
                n0.Add(L.NeonRun(0)); n1.Add(L.NeonRun(1)); n2.Add(L.NeonRun(2)); nb.Add(L.NeonBlink);
                bool ga = L.GlowA && L.GlowA.enabled && L.GroundGlowA > 0.004f, gb = L.GlowB && L.GlowB.enabled && L.GroundGlowB > 0.004f;
                if ((L.LampA <= 0f && ga) || (L.LampA >= 0.05f && !ga)) glowWrongA++;          // (between 0 and 0.05 the lamp is fading in or out)
                if ((L.LampB <= 0f && gb) || (L.LampB >= 0.05f && !gb)) glowWrongB++;
                if (ga) glowLitA++; if (gb) glowLitB++; if (ga && gb) glowBoth++;
                aliveMost = Math.Max(aliveMost, ex.Alive);
                yield return 0.02f;
            }
            var ra = Rises(ts, la, 0.5f); var rb = Rises(ts, lb, 0.5f);
            double pa = MeanGap(ra), pb = MeanGap(rb);
            int both = 0; for (int i = 0; i < la.Count; i++) if (la[i] > 0.5f && lb[i] > 0.5f) both++;
            double shift = ra.Count > 0 && rb.Count > 0 ? Math.Abs((((rb[0] - ra[0]) % L.lampPeriod) + L.lampPeriod) % L.lampPeriod - L.lampPeriod * 0.5) : 99;
            float litShare = la.Count > 0 ? la.Count(v => v > 0.5f) / (float)la.Count : 0f;
            T.Line(ts.Count > 100 && Math.Abs(pa - L.lampPeriod) < 0.15 && Math.Abs(pb - L.lampPeriod) < 0.15, $"the amber position lamps blink once every {L.lampPeriod:0.0} s of the game clock: set A {pa:0.00} s, set B {pb:0.00} s ({ra.Count} and {rb.Count} flashes in {ts.Count} samples over {span:0.0} s)");
            T.Line(both == 0 && shift < 0.2, $"in turn, never together: samples with both sets lit {both}; set B comes half a period after set A (off by {shift:0.00} s); a set is lit {litShare * L.lampPeriod:0.00} s of each period (wanted about {L.lampLit - L.lampFade:0.00})");
            bool glowsMade = L.GlowA && L.GlowB && L.GlowA.GetComponent<Light>() == null && L.GlowB.GetComponent<Light>() == null;
            T.Line(glowsMade && glowWrongA == 0 && glowWrongB == 0 && glowBoth == 0 && glowLitA > 5 && glowLitB > 5,
                   $"each lamp set throws its amber glow on the grass beside its side of the ship (the game's ground-pool picture, no real light): made {(glowsMade ? "2 of 2" : "NOT BOTH")}; lit with its set and dark with it: " +
                   $"samples that disagree {glowWrongA} (set A) and {glowWrongB} (set B) of {ts.Count}; lit in {glowLitA} and {glowLitB} samples; both at once {glowBoth}");
            T.Line(st.Count > 0 && st.Min() >= 0.88f && st.Max() <= 1.01f && st.Max() - st.Min() > 0.01f, $"the window band, the ports and the landing lights glow steadily with a faint flicker: level {st.Min():0.00} to {st.Max():0.00}");
            int enCross = 0; float enMid = en.Count > 0 ? (en.Min() + en.Max()) * 0.5f : 0f; double firstC = -1, lastC = -1;
            for (int i = 1; i < en.Count; i++) if (en[i - 1] < enMid && en[i] >= enMid) { enCross++; if (firstC < 0) firstC = ts[i]; lastC = ts[i]; }
            double pe = enCross > 1 ? (lastC - firstC) / (enCross - 1) : -1;
            T.Line(en.Count > 0 && en.Min() > 0.2f && en.Max() < 0.8f && Math.Abs(pe - L.enginePeriod) < 0.25, $"the engine mouths pulse softly (the ship is parked): level {en.Min():0.00} to {en.Max():0.00}, one pulse every {pe:0.00} s (wanted {L.enginePeriod:0.0})");
            // THE NEON STRIPS: the three running sets one after another, the blinking set
            float mid = (1f + L.neonRunFloor) * 0.5f;
            var r0 = Rises(ts, n0, mid); var r1 = Rises(ts, n1, mid); var r2 = Rises(ts, n2, mid);
            double p0 = MeanGap(r0), p1 = MeanGap(r1), p2 = MeanGap(r2), third = L.neonRunPeriod / 3.0;
            double Gap(List<double> x, List<double> y) => x.Count > 0 && y.Count > 0 ? ((((y[0] - x[0]) % L.neonRunPeriod) + L.neonRunPeriod) % L.neonRunPeriod) : -99;
            double g01 = Gap(r0, r1), g12 = Gap(r1, r2);
            int allDark = 0, allLit = 0; for (int i = 0; i < n0.Count; i++) { float mx = Mathf.Max(n0[i], Mathf.Max(n1[i], n2[i])), mn = Mathf.Min(n0[i], Mathf.Min(n1[i], n2[i])); if (mx < 0.5f) allDark++; if (mn > 0.9f) allLit++; }
            T.Line(Math.Abs(p0 - L.neonRunPeriod) < 0.15 && Math.Abs(p1 - L.neonRunPeriod) < 0.15 && Math.Abs(p2 - L.neonRunPeriod) < 0.15 && Math.Abs(g01 - third) < 0.15 && Math.Abs(g12 - third) < 0.15 && allDark == 0 && allLit == 0,
                   $"THE RUNNING NEON (engine hoops, roof arc, the line under the window band): each of its three sets comes round every {L.neonRunPeriod:0.0} s of the game clock ({p0:0.00}, {p1:0.00}, {p2:0.00} s), one after another {third:0.00} s apart (measured {g01:0.00} and {g12:0.00}); " +
                   $"samples with no set lit {allDark}, with all three lit {allLit} of {ts.Count}: the light runs, it neither stops nor stands");
            var rbk = Rises(ts, nb, (1f + L.neonBlinkFloor) * 0.5f); double pbk = MeanGap(rbk);
            T.Line(Math.Abs(pbk - L.neonBlinkPeriod) < 0.2 && nb.Count > 0 && nb.Max() > 0.95f && nb.Min() < L.neonBlinkFloor + 0.05f,
                   $"THE BLINKING NEON (roof ring, the four pads' rings): once every {L.neonBlinkPeriod:0.0} s ({pbk:0.00} s measured), level {nb.Min():0.00} to {nb.Max():0.00}");
            var rc = L.NeonRunColour; var bc = L.NeonBlinkColour; bool coolWarm = (rc.b > rc.r) != (bc.b > bc.r);
            T.Line(coolWarm && Mathf.Max(rc.r, Mathf.Max(rc.g, rc.b)) > 0.9f && Mathf.Max(bc.r, Mathf.Max(bc.g, bc.b)) > 0.9f,
                   $"two colours for the team the ship is drawn in ({TeamColors.Name(L.NeonTeam)}), a cool one and a warm one: the running strips {rc.r:0.00} {rc.g:0.00} {rc.b:0.00} ({(rc.b > rc.r ? "cool" : "warm")}), the blinking strips {bc.r:0.00} {bc.g:0.00} {bc.b:0.00} ({(bc.b > bc.r ? "cool" : "warm")})");
            // the exhaust wisp
            int puffs = ex.Emitted - puffs0; float watched = Time.unscaledTime - tStart;
            T.Line(puffs >= 6 && aliveMost >= 2 && puffs <= watched * 5f + 4f && OpenZFxPlayer.I && ex.Mode == 0,
                   $"THE EXHAUST WISP: {puffs} pale puffs born in {watched:0} s from the two engine mouths and the two roof vents (sparse: {puffs / Mathf.Max(1f, watched):0.0} a second, 5 or fewer allowed), up to {aliveMost} alive at once; " +
                   $"they drift with the level's wind ({ex.LastWind.x:0.0}, {ex.LastWind.z:0.0} m/s, a share of {ex.windShare:0.0}); particles of the game's smoke channel {ex.channel}{(string.IsNullOrEmpty(ex.Held) ? "" : " [held now: " + ex.Held + "]")}");
            // paused: the clock stands, the lamps and the neon hold
            bool wasPaused = g.paused; g.paused = true; yield return 0.6f;
            float a1 = L.LampA, b1 = L.LampB, k1 = L.NeonRun(0); double c1 = OzShuttleLights.GameClock(); int e1 = ex.Emitted; yield return 1.2f;
            bool held = Mathf.Approximately(a1, L.LampA) && Mathf.Approximately(b1, L.LampB) && Mathf.Approximately(k1, L.NeonRun(0)) && Math.Abs(OzShuttleLights.GameClock() - c1) < 1e-6 && ex.Emitted == e1;
            g.paused = wasPaused;
            T.Line(held, $"game paused for 1.2 s: the clock, the lamps and the neon stand still, no puff is born: {(held ? "yes" : "NO")}");
            // day: the searchlight is off
            yield return 1.0f;
            bool dayOff = fl && !fl.On && fl.Level <= 0.001f && fl.Light && !fl.Light.enabled && fl.Beam && !fl.Beam.enabled && fl.Pool && !fl.Pool.enabled && fl.Lens && !fl.Lens.enabled;
            if (fl) T.Line(dayOff, $"THE SEARCHLIGHT by day (clock {DayT:0.00}): off: {(dayOff ? "yes: no lens glow, no beam, no pool, no light" : "NO")}; its head is the game's own searchlight drum and yoke on the trailer: {(fl && fl.Head && fl.Head.GetComponentsInChildren<MeshFilter>(true).Any(m => m.sharedMesh && m.sharedMesh.name == "Night_drum") ? "yes" : "NO")}");
            float dS = L.EmissionSteady, dE = L.EmissionEngine, dNight = L.Night;
            float neonDay = Mathf.Lerp(L.neonGain.x, L.neonGain.y, dNight);
            OzL16Proof.Cam(ViewX, ViewZ, ViewH); yield return 1.5f;
            var dayPx = LightPixels(L); yield return 0.3f;
            OzL16Proof.Shot(Path.Combine(OutDir, "shuttle_day_h70.png")); yield return 0.6f;
            OzL16Proof.Cam(OpenZL16Shuttle.PlaceX + 1f, OpenZL16Shuttle.PlaceZ - 1f, 22f); yield return 1.5f;
            OzL16Proof.Shot(Path.Combine(OutDir, "shuttle_day_closest_h22.png")); yield return 0.6f;
            // night
            float tNight = Time.unscaledTime; int on0 = fl ? fl.SwitchOns : 0;
            if (dn) { dn.timeOfDay = NightT; dn.Apply(NightT); }
            yield return 6f;
            float nS = L.EmissionSteady, nE = L.EmissionEngine, nNight = L.Night;
            float lampDay = Mathf.Lerp(L.lampGain.x, L.lampGain.y, dNight), lampNight = Mathf.Lerp(L.lampGain.x, L.lampGain.y, nNight), neonNight = Mathf.Lerp(L.neonGain.x, L.neonGain.y, nNight);
            T.Line(dS > 0.5f && lampDay > 1f && dE > 0.2f && neonDay >= 1.63f * 1.5f, $"visible by day (night share {dNight:0.00}): emission x the picture, band {dS:0.00}, a lit lamp {lampDay:0.00}, engines {dE:0.00}, a lit neon strip {neonDay:0.00} (the day bloom takes what is over 1.63: {neonDay / 1.63f:0.0} times that)");
            T.Line(nNight > dNight + 0.5f && nS > dS * 1.5f && lampNight > lampDay * 1.3f && neonNight > neonDay * 1.3f && neonNight >= 0.89f * 4f,
                   $"stronger at night (night share {nNight:0.00}): band {nS:0.00}, a lit lamp {lampNight:0.00}, engines {nE:0.00}, a lit neon strip {neonNight:0.00} (the night bloom takes what is over 0.89: {neonNight / 0.89f:0.0} times that)");
            int lightsOnShip = ls.GetComponentsInChildren<Light>(true).Length + (L.GlowA ? L.GlowA.transform.parent.GetComponentsInChildren<Light>(true).Length : 0);
            T.Line(lightsOnShip == 0, $"the ship's lamps, neon and ground glows are emission only: real lights on the ship and its glows {lightsOnShip}");
            // the searchlight at night: on, its sound, one light in the night budget, the beam over the ship for one period
            if (!fl) T.Note("no searchlight: removed from the level (the item 173)");
            else
            {
                float yMin = 999f, yMax = -999f, offMin = 999f, offMax = 0f; int samples = 0, onShip = 0, beamOn = 0; double cs = OzShuttleLights.GameClock(), realS = EditorApplication.timeSinceStartup;
                var lens = fl.Light ? fl.Light.transform.position : fl.transform.position; float distShip = Vector3.Distance(new Vector3(lens.x, 0f, lens.z), new Vector3(ls.transform.position.x, 0f, ls.transform.position.z));
                float shipHalf = Mathf.Atan2(OpenZL16Shuttle.LongSide * 0.5f, Mathf.Max(1f, distShip)) * Mathf.Rad2Deg;
                while (OzShuttleLights.GameClock() - cs < fl.period * 1.1 && EditorApplication.timeSinceStartup - realS < 60.0)
                {
                    samples++; yMin = Mathf.Min(yMin, fl.YawOff); yMax = Mathf.Max(yMax, fl.YawOff); offMin = Mathf.Min(offMin, fl.AxisOffTarget); offMax = Mathf.Max(offMax, fl.AxisOffTarget);
                    if (fl.AxisOffTarget <= shipHalf + 1f) onShip++; if (fl.Beam && fl.Beam.enabled) beamOn++;
                    yield return 0.05f;
                }
                int sound = Logged(tNight, fl.onEvent); var nl = NightLights.I;
                bool inBudget = fl.Light && fl.GetComponentsInChildren<Light>(true).Length == 1 && NightBudgetShare.Fixtures.Any(f => f != null && f.Light == fl.Light);
                int granted = nl ? nl.Budget.Granted : -1, cap = nl ? nl.maxRealLights : -1;
                T.Line(fl.On && fl.Level > 0.95f && fl.SwitchOns > on0 && sound >= 1, $"THE SEARCHLIGHT at night (clock {NightT:0.00}): on (level {fl.Level:0.00}), switched on {fl.SwitchOns - on0} time(s) since dusk was set; the game's own switch-on sound {fl.onEvent} in the sound log: {sound}");
                T.Line(samples > 50 && beamOn == samples && offMin <= 6f && yMax - yMin >= fl.sweep * 1.5f && onShip >= samples * 0.9f,
                       $"its beam sweeps slowly across the ship: over one sweep of {fl.period:0} s the yoke turned from {yMin:0} to {yMax:0} degrees round the line to the ship (wanted about {-fl.sweep:0} to {fl.sweep:0}); the beam's middle passed {offMin:0.0} degrees from the ship's middle at the nearest and {offMax:0.0} at the farthest; " +
                       $"it lay on the ship (the ship reaches {shipHalf:0} degrees to each side of its middle, seen from the lamp {distShip:0.0} m away) in {onShip} of {samples} samples; the beam cone was drawn in {beamOn} of them");
                T.Line(inBudget && granted <= cap,
                       $"no real light beyond the night budget: the searchlight owns 1 light, offered to the night lights' budget: {(inBudget ? "yes" : "NO")}; the budget grants {granted} real lights now (its cap {cap}); this one's share of a real light now {fl.RealShare:0.00} " +
                       $"(0 = drawn as lens, halo, beam cone and ground pool only); the pool picture {(fl.Pool && fl.Pool.enabled ? "is drawn" : "is not drawn")}");
            }
            OzL16Proof.Cam(ViewX, ViewZ, ViewH); yield return 1.5f;
            var nightPx = LightPixels(L); yield return 0.3f;
            int want = Mathf.Max(30, Mathf.RoundToInt(200f * dayPx.w * dayPx.h / (2560f * 1600f)));
            bool seen = dayPx.a > want && dayPx.b > want && nightPx.a > want && nightPx.b > want && dayPx.a > 3 * dayPx.same + 30 && dayPx.b > 3 * dayPx.same + 30 && nightPx.a > 3 * nightPx.same + 30 && nightPx.b > 3 * nightPx.same + 30;
            T.Line(seen, $"THE BLINK IS SEEN FROM THE NORMAL CAMERA (height {ViewH:0}, the picture {dayPx.w} x {dayPx.h} drawn again with no set, set A and set B lit; pixels that change by 8 of 255 or more): " +
                         $"by day set A {dayPx.a}, set B {dayPx.b} (the same picture twice: {dayPx.same}); at night set A {nightPx.a}, set B {nightPx.b} (twice: {nightPx.same}); wanted more than {want} each and more than three times the picture's own change");
            bool neonSeen = dayPx.neon > want && nightPx.neon > want && dayPx.neon > 3 * dayPx.same + 30 && nightPx.neon > 3 * nightPx.same + 30 && nightPx.neon > dayPx.neon;
            T.Line(neonSeen, $"THE NEON IS SEEN FROM THE NORMAL CAMERA, by day and more at night (the same picture with the strips dark and with all of them lit; the glow round them is the game's bloom): pixels that change by day {dayPx.neon}, at night {nightPx.neon} (wanted more than {want} each, and more at night)");
            OzL16Proof.Shot(Path.Combine(OutDir, "shuttle_night_h70.png")); yield return 0.6f;
            OzL16Proof.Cam(OpenZL16Shuttle.PlaceX + 1f, OpenZL16Shuttle.PlaceZ - 1f, 22f); yield return 1.5f;
            OzL16Proof.Shot(Path.Combine(OutDir, "shuttle_night_closest_h22.png")); yield return 0.6f;
            // dawn: off again
            if (dn) { dn.timeOfDay = DawnT; dn.Apply(DawnT); }
            yield return 2.5f;
            bool dawnOff = fl && !fl.On && fl.Level <= 0.001f && fl.Light && !fl.Light.enabled && fl.Beam && !fl.Beam.enabled;
            if (fl) T.Line(dawnOff, $"THE SEARCHLIGHT after dawn (clock {DawnT:0.00}): off again: {(dawnOff ? "yes" : "NO")} (on only between {(fl ? fl.onAt : 0f):0.000} and {(fl ? fl.offAt : 0f):0.000}, as the level's lamp posts)");
            if (dn) { dn.timeOfDay = tod0; dn.Apply(tod0); dn.running = ran; }
            T.Note("pictures: shuttle_day_h70.png, shuttle_day_closest_h22.png, shuttle_night_h70.png, shuttle_night_closest_h22.png in " + OutDir + " : LOOK AT EACH ONE");
            T.End("check_b_lights.txt");
        }

        // ------------------------------------------------------------------------------------------------ c: stages by a tank's fire
        [MenuItem("OpenZ/L16 Jungle/Checks/J14 c Shuttle damaged and destroyed by a heavy tank (Play mode, fresh start, about 3 minutes)")]
        public static void StagesMenu() { if (Ready(out _)) Run(Stages()); }

        static IEnumerator<float> Stages()
        {
            var T = new Tally("J14 c: level 16, the shuttle through its stages by a heavy tank's fire (the player's attack order)");
            var g = ZGame.I; var w = g.World; var ls = L16Shuttle.I; var L = ls.lights; var ex = ls.exhaust; var fl = ls.searchlight; var dp = ls.GetComponent<DetailProp>(); var o = w.L16Shuttle;
            int nT = ZWorld.L16ShuttleTiles;
            T.Line(o != null && dp.SimBacked && dp.SimId == o.Id && ls.Stage == 0, $"the drawn ship is bound to its map item: {(o != null && dp.SimBacked && dp.SimId == o.Id ? "yes" : "NO")}; {(o != null ? o.Health : 0)} of {(o != null ? o.MaxHealth : 0)} hit points; stage {ls.Stage}");
            if (o == null) { T.End("check_c_stages.txt"); yield break; }
            var fortTeam = ls.FortTeam(); var tp = ls.GetComponent<TeamPaint>();
            T.Line(tp && tp.Team == fortTeam && fortTeam != Team.Neutral && L.NeonTeam == TeamColors.Shown(fortTeam), $"team colour: the fort beside it belongs to {fortTeam}, the ship is painted {(tp ? tp.Team.ToString() : "?")}, drawn {TeamColors.Name(fortTeam)} (the player's colour option {(TeamColors.Swap ? "swapped" : "as the map")}); the neon strips take that team's two colours: {(L.NeonTeam == TeamColors.Shown(fortTeam) ? "yes" : "NO")}");
            OzL16Proof.Cam(ViewX, ViewZ, ViewH); yield return 1.5f;
            var q = ZWorld.L16ShuttleSquare;
            var at = Ring(w, q[0] + q[2] + 5, q[1] - 2, false, 0, 6);                  // open ground north-east of the ship, away from the fort's own start units and from the searchlight
            if (at == null) { T.Line(false, "no free ground for a tank near the ship"); T.End("check_c_stages.txt"); yield break; }
            // the tank is the fort's own side's (the fort's guns leave it alone); the ship is neutral in the rules, so the attack order is taken
            var u = w.Get(OzL16Proof.Spawn("heavy", fortTeam == Team.Red, at.Value.x, at.Value.y));
            yield return 1f;
            float t0u = Time.unscaledTime; int hp0 = o.Health; double c0 = w.Time, realEnd = EditorApplication.timeSinceStartup + 300.0;
            bool sent = u != null && w.SendWaypoints(u.Owner, u, new[] { new ZWaypoint { mode = WpMode.Attack, refId = o.Id, x = o.CenterX, y = o.CenterY } }) && u.Waypoints.Count > 0 && u.Waypoints[0].mode == WpMode.Attack;
            while (o.Health > o.MaxHealth / 2 && !o.Destroyed && w.Time - c0 < 120 && EditorApplication.timeSinceStartup < realEnd) yield return 0.1f;
            double tHalf = w.Time - c0; int hpHalf = o.Health;
            if (u != null) w.SendWaypoints(u.Owner, u, new List<ZWaypoint>());               // hold fire for the picture
            yield return 2.5f;
            T.Line(sent && hpHalf < hp0 && hpHalf <= o.MaxHealth / 2, $"a heavy tank from tile {at.Value.x},{at.Value.y}: order {(sent ? "taken" : "REFUSED")}; half the hit points gone after {tHalf:0.0} s of fire ({hp0} -> {hpHalf})");
            // one lamp period and one blink period watched: the second set and the blinking neon stay dark
            float maxB = 0f, maxA = 0f, maxGlowB = 0f, maxGlowA = 0f, maxBlink = 0f, maxRun = 0f; double cw = w.Time; double watch = Math.Max(L.lampPeriod * 1.2, L.neonBlinkPeriod * 1.1);
            while (w.Time - cw < watch && EditorApplication.timeSinceStartup < realEnd)
            {
                maxB = Mathf.Max(maxB, L.LampB); maxA = Mathf.Max(maxA, L.LampA); maxBlink = Mathf.Max(maxBlink, L.NeonBlink); maxRun = Mathf.Max(maxRun, Mathf.Max(L.NeonRun(0), Mathf.Max(L.NeonRun(1), L.NeonRun(2))));
                maxGlowB = Mathf.Max(maxGlowB, L.GlowB && L.GlowB.enabled ? L.GroundGlowB : 0f); maxGlowA = Mathf.Max(maxGlowA, L.GlowA && L.GlowA.enabled ? L.GroundGlowA : 0f);
                yield return 0.03f;
            }
            bool char1 = tp && tp.Charred > 0.3f && tp.Charred < 0.6f;
            T.Line(ls.Stage == 1 && !o.Destroyed && maxB <= 0.001f && maxA > 0.9f && maxGlowB <= 0.001f && maxGlowA > 0.3f && char1 && maxBlink <= 0.001f && maxRun > 0.2f && maxRun < 0.75f && ex.Mode == 1,
                   $"damaged stage: stage {ls.Stage}; lamp set B dark for a whole period (highest level {maxB:0.00}), set A still blinking ({maxA:0.00}); scorched (char {(tp ? tp.Charred : 0f):0.00}); band level now {L.Steady:0.00}; ground glow of set B dark ({maxGlowB:0.00}), of set A still blinking ({maxGlowA:0.00}); " +
                   $"the blinking neon dead ({maxBlink:0.00}), the running neon stuttering (highest {maxRun:0.00}); the exhaust from the left engine only (mode {ex.Mode})");
            int groan = Logged(t0u, "dst_structure_groan"), glass1 = Logged(t0u, "dst_glass"), ignite = Logged(t0u, "dst_fire_ignite");
            T.Line(groan >= 1 && glass1 >= 1 && ignite >= 1, $"sound log at the damaged stage: dst_structure_groan {groan}, dst_glass {glass1}, dst_fire_ignite {ignite}");
            OzL16Proof.Shot(Path.Combine(OutDir, "shuttle_damaged_h70.png")); yield return 0.6f;
            // on to the end
            float t1u = Time.unscaledTime; double c1 = w.Time;
            if (u != null) w.SendWaypoints(u.Owner, u, new[] { new ZWaypoint { mode = WpMode.Attack, refId = o.Id, x = o.CenterX, y = o.CenterY } });
            // every flying piece's lowest drawn point (all its mesh points) against the ground under it, at every sample of its flight
            var pieceVerts = new Dictionary<Transform, Vector3[]>(); float deepest = 0f; int flightSamples = 0; string deepestOf = "none";
            void Sample()
            {
                foreach (var p in ls.pieces)
                {
                    if (!p || !p.gameObject.activeInHierarchy || p.localScale.x < 0.9f) continue;
                    var pr = p.GetComponent<Renderer>(); if (!pr || !pr.enabled) continue;
                    if (!pieceVerts.TryGetValue(p, out var vs)) { vs = OpenZL16Shuttle.Points(p); pieceVerts[p] = vs; }
                    if (vs.Length == 0) continue;
                    var M = p.localToWorldMatrix; float low = float.PositiveInfinity; Vector3 lowAt = p.position;
                    for (int i = 0; i < vs.Length; i++) { var v2 = M.MultiplyPoint3x4(vs[i]); if (v2.y < low) { low = v2.y; lowAt = v2; } }
                    float d = low - g.TerrainY(lowAt.x, lowAt.z); flightSamples++;
                    if (d < deepest) { deepest = d; deepestOf = p.name; }
                }
            }
            IEnumerable<float> Watch(float seconds) { double end = EditorApplication.timeSinceStartup + seconds; while (EditorApplication.timeSinceStartup < end) { Sample(); yield return 0.03f; } }
            while (!o.Destroyed && w.Get(o.Id) != null && w.Time - c1 < 120 && EditorApplication.timeSinceStartup < realEnd) yield return 0.03f;
            double tDead = w.Time - c0; int puffsAtDeath = ex.Emitted;
            foreach (var y in Watch(1.0f)) yield return y;
            bool dark = L.EmissionLampA == 0f && L.EmissionLampB == 0f && L.EmissionSteady == 0f && L.EmissionEngine == 0f && L.EmissionNeonRun == 0f && L.EmissionNeonBlink == 0f && L.Mode == 2
                        && !(L.GlowA && L.GlowA.enabled) && !(L.GlowB && L.GlowB.enabled);
            T.Line(ls.Stage == 2 && dp.Broken && w.Get(o.Id) == null, $"destroyed by the tank's fire after {tDead:0.0} s in all: stage {ls.Stage}, the map item is {(w.Get(o.Id) == null ? "gone" : "STILL THERE")}");
            T.Line(dark && ls.hull && !ls.hull.enabled, $"every light and the neon off (emission written: lamps {L.EmissionLampA:0.00} {L.EmissionLampB:0.00}, band {L.EmissionSteady:0.00}, engines {L.EmissionEngine:0.00}, neon {L.EmissionNeonRun:0.00} {L.EmissionNeonBlink:0.00}), both ground glows off; the standing ship is no longer drawn: {(ls.hull && !ls.hull.enabled ? "yes" : "NO")}");
            int fromModel = 0; foreach (var p in ls.pieces) { var mf = p ? p.GetComponent<MeshFilter>() : null; if (mf && mf.sharedMesh && mf.sharedMesh.name.StartsWith("WR_") && AssetDatabase.GetAssetPath(mf.sharedMesh) == OpenZL16Shuttle.Model && p.GetComponent<Renderer>().enabled) fromModel++; }
            bool wreckOn = ls.wreck && ls.wreck.gameObject.activeInHierarchy;
            T.Line(ls.PiecesThrown == 6 && fromModel == 6 && wreckOn, $"the wreck: {ls.PiecesThrown} pieces thrown (both dome halves, both engines, two legs), {fromModel} of 6 are meshes of the ship's own model and drawn; the rear body on its rear legs: {(wreckOn ? "shown" : "MISSING")}");
            int boom = Logged(t1u, "exp_vehicle_death"), coll = Logged(t1u, "dst_collapse_medium") + Logged(t1u, "collapse"), metal = Logged(t1u, "dst_debris_metal"), glass2 = Logged(t1u, "dst_glass");
            T.Line(boom >= 1 && coll >= 1 && metal >= 1, $"sound log at the destruction: exp_vehicle_death {boom}, dst_collapse_medium {coll}, dst_debris_metal {metal}, dst_glass {glass2}");
            T.Line(w.L16ShuttleOpenTiles() == nT, $"tiles of its square open again: {w.L16ShuttleOpenTiles()} of {nT}");
            var ruin = fl ? fl.transform.Find("Model/RUIN_Stand") : null;
            bool lampGone = fl && fl.Broken && fl.Light && !fl.Light.enabled && !(fl.Beam && fl.Beam.enabled) && !(fl.Pool && fl.Pool.enabled) && ruin && ruin.gameObject.activeInHierarchy && !NightBudgetShare.Fixtures.Any(f => f != null && f.Light == fl.Light);
            if (fl) T.Line(lampGone, $"the searchlight beside it went with the ship: broken {(fl && fl.Broken ? "yes" : "NO")}, its light, beam and pool off, out of the night budget's list, the burnt trailer left: {(ruin && ruin.gameObject.activeInHierarchy ? "yes" : "NO")}");
            foreach (var y in Watch(1.5f)) yield return y;
            OzL16Proof.Shot(Path.Combine(OutDir, "shuttle_destroyed_h70.png")); foreach (var y in Watch(0.6f)) yield return y;
            OzL16Proof.Cam(OpenZL16Shuttle.PlaceX + 1f, OpenZL16Shuttle.PlaceZ - 1f, 30f); foreach (var y in Watch(1.5f)) yield return y;
            OzL16Proof.Shot(Path.Combine(OutDir, "shuttle_wreck_h30.png")); foreach (var y in Watch(0.6f)) yield return y;
            double flyEnd = EditorApplication.timeSinceStartup + 6.0; while (ls.PiecesInFlight > 0 && EditorApplication.timeSinceStartup < flyEnd) { Sample(); yield return 0.03f; }
            Sample();
            T.Line(flightSamples >= 200 && deepest >= -0.10f && ls.PiecesInFlight == 0,
                   $"the six flying pieces never sink into the ground: each piece's lowest point against the ground under it at {flightSamples} samples of their flight (every 0.03 s until all lie still): the deepest {deepest:0.00} m ({deepestOf}; 0.10 m allowed); pieces still moving at the end: {ls.PiecesInFlight}");
            T.Line(ex.Emitted == puffsAtDeath && ex.Alive == 0 && ex.Mode == 2, $"no exhaust from the wreck: puffs born since the destruction {ex.Emitted - puffsAtDeath}, puffs of the ship's exhaust still alive {ex.Alive} (the wreck has the game's own wreck smoke)");
            // the wreck goes as the game's debris goes
            double cg = w.Time; while (w.Time - cg < ls.wreckSeconds + 6f && EditorApplication.timeSinceStartup < realEnd + 60.0) yield return 0.5f;
            bool goneAll = (!ls.wreck || !ls.wreck.gameObject.activeInHierarchy || ls.wreck.localScale.x < 0.05f) && ls.pieces.All(p => !p || !p.gameObject.activeInHierarchy);
            T.Line(goneAll, $"{ls.wreckSeconds + 6f:0} s later nothing tall is left on the open tiles (the wreck went as the game's debris goes; the scorch stays): {(goneAll ? "yes" : "NO")}");
            OzL16Proof.Cam(ViewX, ViewZ, ViewH); yield return 1.5f;
            OzL16Proof.Shot(Path.Combine(OutDir, "shuttle_scorch_after_h70.png")); yield return 0.6f;
            T.Note("pictures: shuttle_damaged_h70.png, shuttle_destroyed_h70.png, shuttle_wreck_h30.png, shuttle_scorch_after_h70.png in " + OutDir + " : LOOK AT EACH ONE");
            T.End("check_c_stages.txt");
        }

        // ------------------------------------------------------------------------------------------------ d: the pictures for the owner
        [MenuItem("OpenZ/L16 Jungle/Checks/J14 d Shuttle pictures: beside the fort by day and at night, red and blue (Play mode, fresh start, about 1 minute)")]
        public static void PicturesMenu() { if (Ready(out _)) Run(Pictures()); }

        static IEnumerator<float> Pictures()
        {
            var dn = UnityEngine.Object.FindFirstObjectByType<DayNight>(); float tod0 = dn ? dn.timeOfDay : 0f; bool ran = dn && dn.running;
            bool swap0 = TeamColors.Swap; int n = 0;
            // red and blue by the game's own option: every painted object is painted again, the fort and the ship change together
            foreach (bool sw in new[] { false, true })
            {
                string tag = sw ? "team_colour_swapped" : "team_colour_as_the_map";
                TeamColors.Swap = sw;
                foreach (var p in UnityEngine.Object.FindObjectsByType<TeamPaint>(FindObjectsInactive.Include, FindObjectsSortMode.None)) p.Apply();
                if (dn) { dn.running = false; dn.timeOfDay = DayT; dn.Apply(DayT); }
                OzL16Proof.Cam(ViewX, ViewZ, ViewH); yield return 4.5f;                                  // (the ship looks at its fort's colour again every 2 s: the neon follows)
                OzL16Proof.Shot(Path.Combine(OutDir, tag + "_day_h70.png")); n++; yield return 0.6f;
                if (!sw) { OzL16Proof.Shot(Path.Combine(OutDir, "owner_view_shuttle_beside_red_fort_h70.png")); n++; yield return 0.6f; }
                OzL16Proof.Cam(OpenZL16Shuttle.PlaceX + 1f, OpenZL16Shuttle.PlaceZ - 1f, 30f); yield return 1.5f;
                OzL16Proof.Shot(Path.Combine(OutDir, tag + "_day_h30.png")); n++; yield return 0.6f;
                if (dn) { dn.timeOfDay = NightT; dn.Apply(NightT); }
                OzL16Proof.Cam(ViewX, ViewZ, ViewH); yield return 7f;                                    // the night grade, the searchlight on and into its sweep
                OzL16Proof.Shot(Path.Combine(OutDir, tag + "_night_h70.png")); n++; yield return 0.6f;
                OzL16Proof.Cam(OpenZL16Shuttle.PlaceX + 1f, OpenZL16Shuttle.PlaceZ - 1f, 30f); yield return 1.5f;
                OzL16Proof.Shot(Path.Combine(OutDir, tag + "_night_h30.png")); n++; yield return 0.6f;
            }
            TeamColors.Swap = swap0;
            foreach (var p in UnityEngine.Object.FindObjectsByType<TeamPaint>(FindObjectsInactive.Include, FindObjectsSortMode.None)) p.Apply();
            if (dn) { dn.timeOfDay = tod0; dn.Apply(tod0); dn.running = ran; }
            Debug.Log($"[OpenZ L16] J14 pictures: {n} written to {OutDir} (2560 x 1600 when the Game view is set so): owner_view_shuttle_beside_red_fort_h70.png beside the two pictures; team_colour_as_the_map and team_colour_swapped by day and at night with the searchlight, at heights 70 and 30. " +
                      "The colour option and the clock are put back as they were. LOOK AT EACH ONE.");
        }
    }
}
