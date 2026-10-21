using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// K342: the repair station's crane is carried by two telescopic hydraulic rams (view only). When a shuttle calls the
    /// station, the gantry (the beam, its west end block over the tower, the east post) rolls 5 m north and settles, or rolls to its rail's
    /// end and lifts (<see cref="Move"/>). Before this, nothing visibly held the beam up out there. Now every repair station has one
    /// ram at each end of the gantry, in a vertical plane just outside the station's side walls (x = -5.05 west, +5.05 east, station frame;
    /// the station reaches x -4.75 .. +4.74 and the ship turning over its deck x -3.39 .. +4.39), so a ram never meets the deck, the tower
    /// or the ship. Only its two mounts reach in:
    ///  * the base mount, fixed to the station: a console bolted to the base slab's side, two ears and a pin with nuts;
    ///  * the ram: a barrel in the station's team paint (the beam's own texel), a chrome stage and a chrome rod sliding out of it, dark
    ///    glands, eyes on both pins, a hose with two port blocks on the barrel's upper side. Fixed lengths, never stretched: each stage
    ///    takes half of the stroke and keeps at least <see cref="RamOvMin"/> inside the one below;
    ///  * the top mount, riding the gantry: a dark arm bolted to the beam's west end block / the east post, two ears and a pin with nuts.
    /// Each frame (right after the gantry is stepped) the top mounts move by the gantry's own offset (<see cref="Move"/>, so the rams are in
    /// step with it by construction) and the rams swing about their pins and extend. A hiss when a move starts, a pressure release when
    /// it stops. The painted parts use the station's own material with every vertex's UV on one texel of the station's atlas (the texel a
    /// big face of the station shows: the post's team paint, the tower's cream, a dark rail), found on the body mesh at bind; the chrome is
    /// the project's ram steel (View/FortGunView.cs). The rams hide with the station (destroyed, hidden) and come back with it.
    /// K342b: the parts are baked into seven merged meshes by how they move (View/OzCraneRamParts.cs: 7 renderers per station instead of
    /// 50, every part still casting its shadow, the look the same), and the west top mount is bolted to the beam's own end block
    /// (the tower's top stays on the tower now: View/OzCraneCut.cs). The station's clean cut is worked out here too, when the rams are
    /// built (the level's first frame), so a shuttle's call has no hitch.
    /// </summary>
    public static partial class OzStationCrane
    {
        /// <summary>The least length of a stage inside the one below it (m).</summary>
        public const float RamOvMin = OzCraneRamParts.OvMin;
        // ---- the paints: a face of the station that shows the paint (station frame) and that face's UV (the fallback: Unity's UV)
        static readonly Vector3[] PaintAt = { new Vector3(3.86f, 1.99f, 1.91f), new Vector3(-4.51f, 2.38f, -2.35f), new Vector3(-2.86f, 2.86f, -0.83f) };
        static readonly Vector2[] PaintUv = { new Vector2(0.66889f, 0.69307f), new Vector2(0.60316f, 0.46138f), new Vector2(0.23578f, 0.40336f) };

        /// <summary>Proof: rams built (stations), poses set, the largest pin-to-pin length (m), the smallest stage overlap (m), the
        /// largest gap between a top mount and the gantry it rides (m: 0 = in step), hisses at a start / releases at a stop; K342b: the
        /// renderers of the last rig built (7) and the shadow casters among them (7).</summary>
        public static int RamRigs, RamPoses, RamHisses, RamReleases, RamOverReach, RamRenderers, RamShadowCasters;
        static bool ramReachSaid, ramSideSaid; static int ramPaintFound;
        public static float RamMaxLength, RamMinOverlap = 9f, RamLagMax;
        public static string RamLog = "-";

        sealed class RamRig
        {
            public int id; public GameObject go; public Transform frameT, station, top, barrels, stages, rods; public MeshRenderer body;
            public readonly MeshRenderer[] r = new MeshRenderer[OzCraneRamParts.Groups];
            public readonly Transform[] groupT = new Transform[OzCraneRamParts.Groups];
            public readonly Mesh[] meshes = new Mesh[OzCraneRamParts.Groups];
            public List<OzCraneRamParts.Shape> shapes;
            public double lastMoveFrom = -2; public bool wasMoving; public Vector3 off;
        }
        static readonly Dictionary<int, RamRig> ramRigs = new Dictionary<int, RamRig>();
        static ZGame ramGame; static int ramScanAt = -1;
        static Material ramSteel;
        static readonly Dictionary<int, Vector2[]> paintUvOf = new Dictionary<int, Vector2[]>();
        static MaterialPropertyBlock ramMpb;
        static readonly int AccentLoId = Shader.PropertyToID("_AccentLo"), AccentHiId = Shader.PropertyToID("_AccentHi"),
                            PanelId = Shader.PropertyToID("_OZPanel"), PanelYId = Shader.PropertyToID("_OZPanelY");

        /// <summary>Once a frame from <see cref="Tick"/>, after the gantries are stepped: every repair station's rams built (once) and posed.</summary>
        static void RamsTick(ZGame g)
        {
            if (g != ramGame) { RamsDispose(); ramGame = g; ramScanAt = -1; }
            var w = g.World;
            if (Time.frameCount >= ramScanAt)
            {   // new stations (and the first frame): a rig for each repair station that has a static view
                ramScanAt = Time.frameCount + 120;
                foreach (var o in w.Objects)
                {
                    if (o.Kind != ZKind.Building || o.Type != "repair" || ramRigs.ContainsKey(o.Id)) continue;
                    if (!g.StaticViews.TryGetValue(o.Id, out var sgo) || !sgo) continue;
                    var rig = BuildRams(o.Id, sgo);
                    if (rig != null) ramRigs[o.Id] = rig;
                }
            }
            foreach (var rig in ramRigs.Values) PoseRams(g, w, rig);
        }

        static void RamsDispose()
        {
            foreach (var r in ramRigs.Values)
            {
                if (r.go) Object.Destroy(r.go);
                foreach (var m in r.meshes) if (m) Object.Destroy(m);
            }
            ramRigs.Clear();
        }

        // ------------------------------------------------------------------ build
        static RamRig BuildRams(int id, GameObject sgo)
        {
            var mf = Body(sgo); var mr = mf ? mf.GetComponent<MeshRenderer>() : null;
            if (!mf || !mr || !mr.sharedMaterial) { RamLog = "station #" + id + ": no body mesh, no rams"; return null; }
            if (!ramSteel)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit"); if (!sh) { RamLog = "no URP Lit shader: no rams"; return null; }
                ramSteel = new Material(sh) { name = "OzCraneRam_Steel" };                              // the project's ram steel (View/FortGunView.cs)
                ramSteel.SetColor("_BaseColor", new Color(0.70f, 0.71f, 0.72f)); ramSteel.SetFloat("_Metallic", 0.9f); ramSteel.SetFloat("_Smoothness", 0.78f);
            }
            var uv = PaintUvs(mf, sgo.transform);
            CutFor(mf, sgo.transform);                                                                // K342b: the station's clean cut, worked out now (no hitch at a call)
            var rig = new RamRig { id = id, station = sgo.transform, body = mr };
            rig.go = new GameObject("OZ_CraneRams_" + sgo.name) { layer = mf.gameObject.layer };
            rig.go.transform.SetPositionAndRotation(sgo.transform.position, sgo.transform.rotation); rig.go.transform.localScale = sgo.transform.lossyScale;
            var fr = new GameObject("Frame").transform; fr.SetParent(rig.go.transform, false); fr.localRotation = Quaternion.Euler(0f, 180f, 0f);   // station frame = (-x, y, -z) of the root
            rig.frameT = fr;
            Transform Group(string name) { var t = new GameObject(name).transform; t.SetParent(fr, false); return t; }
            rig.top = Group("TopMounts"); rig.barrels = Group("Barrels"); rig.stages = Group("Stages"); rig.rods = Group("Rods");
            var mat = mr.sharedMaterial;
            var data = OzCraneRamParts.Bake(uv);
            rig.shapes = OzCraneRamParts.Shapes();
            int casters = 0;
            for (int g = 0; g < OzCraneRamParts.Groups; g++)
            {
                var d = data[g];
                var parent = g == OzCraneRamParts.TopBig || g == OzCraneRamParts.TopSmall ? rig.top : g == OzCraneRamParts.Barrels ? rig.barrels :
                             g == OzCraneRamParts.Stages ? rig.stages : g == OzCraneRamParts.Rods ? rig.rods : fr;
                var go = new GameObject("Ram_" + OzCraneRamParts.GroupNames[g]) { layer = rig.go.layer };
                go.transform.SetParent(parent, false);
                var m = new Mesh { name = "OzCraneRam_" + OzCraneRamParts.GroupNames[g] };
                m.SetVertices(d.v); m.SetNormals(d.n); m.SetTangents(d.t); m.SetUVs(0, d.uv);
                bool two = d.sub[1].Count > 0; m.subMeshCount = two ? 2 : 1;
                m.SetTriangles(d.sub[0], 0, false); if (two) m.SetTriangles(d.sub[1], 1, false);
                m.RecalculateBounds();
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var r = go.AddComponent<MeshRenderer>();
                var m0 = OzCraneRamParts.Sub0[g] == OzCraneRamParts.PaintSteel ? ramSteel : mat;
                r.sharedMaterials = two ? new[] { m0, mat } : new[] { m0 };
                r.shadowCastingMode = OzCraneRamParts.Shadows[g] ? ShadowCastingMode.On : ShadowCastingMode.Off; r.receiveShadows = true;
                if (OzCraneRamParts.Shadows[g]) casters++;
                if (rig.body) { r.lightProbeUsage = rig.body.lightProbeUsage; r.reflectionProbeUsage = rig.body.reflectionProbeUsage; }
                rig.r[g] = r; rig.groupT[g] = go.transform; rig.meshes[g] = m;
            }
            RamRigs++; RamRenderers = OzCraneRamParts.Groups; RamShadowCasters = casters;
            RamLog = $"station #{id}: rams built ({rig.shapes.Count} parts in {OzCraneRamParts.Groups} meshes, {casters} casting shadows), {ramPaintFound} of the 3 paints read off the body's own faces (else the kit's texels)";
            Debug.Log("[OpenZ] crane rams (K342b): " + RamLog);
            return rig;
        }

        /// <summary>The UVs of the three paints on this body mesh: the face nearest each reference point (within 3 cm), else the kit's.</summary>
        static Vector2[] PaintUvs(MeshFilter mf, Transform root)
        {
            var mesh = mf.sharedMesh; int key = mesh.GetInstanceID();
            if (paintUvOf.TryGetValue(key, out var have)) return have;
            var res = (Vector2[])PaintUv.Clone();
            if (mesh.isReadable)
            {
                var vs = mesh.vertices; var uvs = mesh.uv; var tr = mesh.triangles; var m = mf.transform.localToWorldMatrix;
                if (uvs != null && uvs.Length == vs.Length)
                {
                    var best = new float[] { 9f, 9f, 9f }; ramPaintFound = 0;
                    for (int i = 0; i + 2 < tr.Length; i += 3)
                    {
                        Vector3 c = (vs[tr[i]] + vs[tr[i + 1]] + vs[tr[i + 2]]) / 3f;
                        var p = root.InverseTransformPoint(m.MultiplyPoint3x4(c)); var f = new Vector3(-p.x, p.y, -p.z);
                        for (int k = 0; k < 3; k++)
                        {
                            float d = (f - PaintAt[k]).sqrMagnitude;
                            if (d < best[k]) { if (d < 0.03f * 0.03f) { if (best[k] >= 0.03f * 0.03f) ramPaintFound++; res[k] = (uvs[tr[i]] + uvs[tr[i + 1]] + uvs[tr[i + 2]]) / 3f; } best[k] = d; }
                        }
                    }
                    paintUvOf[key] = res;
                }
            }
            return res;
        }

        // ------------------------------------------------------------------ pose (every frame)
        static void PoseRams(ZGame g, ZWorld w, RamRig rig)
        {
            if (!rig.go) return;
            var b = w.Get(rig.id);
            bool show = b != null && !b.Destroyed && rig.station && rig.station.gameObject.activeInHierarchy;
            if (rig.go.activeSelf != show) rig.go.SetActive(show);
            st.TryGetValue(rig.id, out var s);
            if (!show) { rig.wasMoving = false; return; }
            rig.go.transform.SetPositionAndRotation(rig.station.position, rig.station.rotation); rig.go.transform.localScale = rig.station.lossyScale;
            // the gantry's offset now (station frame), the same Move the gantry is drawn with
            var off = Vector3.zero;
            if (s != null && s.gantry) { var mv = Move(s, s.k); off = new Vector3(-mv.x, mv.y, -mv.z); }
            if (Mathf.Abs(off.x) > 1e-4f && !ramSideSaid) { ramSideSaid = true; Debug.LogWarning("[OpenZ] crane rams (K342b): the gantry moved sideways; the rams follow its north / up move only"); }
            rig.off = off;
            OzCraneRamParts.Pose(off, out var u, out float L, out float e, out bool clamped);
            if (clamped)
            {   // the crane moves farther (or nearer) than the rams reach (a changed SlideM / LiftM): held at their end, said once
                if (!ramReachSaid) { ramReachSaid = true; Debug.LogWarning($"[OpenZ] crane rams (K342): {L:F2} m pin to pin is outside their reach (the crane's move changed?)"); }
                RamOverReach++;
            }
            RamMaxLength = Mathf.Max(RamMaxLength, L); RamMinOverlap = Mathf.Min(RamMinOverlap, OzCraneRamParts.LS - e);
            var rot = Quaternion.LookRotation(Vector3.Cross(Vector3.right, u), u);                    // local y = the rams' axis, local x = the pins' axis
            rig.top.localPosition = off; rig.top.localRotation = Quaternion.identity;
            rig.barrels.localPosition = OzCraneRamParts.Pivot; rig.barrels.localRotation = rot;
            rig.stages.localPosition = OzCraneRamParts.Pivot + u * e; rig.stages.localRotation = rot;
            rig.rods.localPosition = OzCraneRamParts.Pivot + u * (2f * e); rig.rods.localRotation = rot;
            RamPoses++;
            // in step: the top mount has moved exactly as far as the gantry (world)
            if (s != null && s.gantry && s.mf)
            {
                var gDelta = s.gantry.transform.position - s.mf.transform.TransformPoint(s.restLocal);
                var mDelta = rig.top.position - rig.frameT.TransformPoint(Vector3.zero);
                RamLagMax = Mathf.Max(RamLagMax, (gDelta - mDelta).magnitude);
            }
            // the station's paint (team colour, scorch: the body's own block, View/TeamPaint.cs) on the painted parts, with the team rule
            // of their own: the barrels' olive is all team paint (a paint-mode panel over the whole part, TeamLit), the cream and dark
            // parts none; the body's accent band (a height of ITS mesh) is not theirs
            ramMpb ??= new MaterialPropertyBlock();
            if (rig.body)
            {
                rig.body.GetPropertyBlock(ramMpb, 0);
                ramMpb.SetFloat(AccentLoId, 1e6f); ramMpb.SetFloat(AccentHiId, -1e6f);
                ramMpb.SetVector(PanelId, new Vector4(0f, 0f, 10f, 10f)); ramMpb.SetVector(PanelYId, new Vector4(-10f, 0.01f, 0f, -1f));
                var br = rig.r[OzCraneRamParts.Barrels]; if (br) br.SetPropertyBlock(ramMpb, 0);
                ramMpb.SetVector(PanelYId, new Vector4(1e6f, 0.01f, 0f, -1f));
                for (int gi = 0; gi < OzCraneRamParts.Groups; gi++)
                {
                    var r = rig.r[gi]; if (!r) continue;
                    if (gi < OzCraneRamParts.Barrels) r.SetPropertyBlock(ramMpb, 0);                    // the mounts: station paint only
                    else r.SetPropertyBlock(ramMpb, 1);                                                // the tubes' dark parts (submesh 1)
                }
            }
            // the sounds: a hiss as a move starts (also a turn back mid-way), the pressure let go as it stops
            bool moving = s != null && s.moving && !s.failed;
            if (moving && s.moveFrom != rig.lastMoveFrom) { rig.lastMoveFrom = s.moveFrom; RamSound(rig, true); }
            if (!moving && rig.wasMoving) RamSound(rig, false);
            rig.wasMoving = moving;
        }

        static void RamSound(RamRig rig, bool start)
        {
            var at = rig.go.transform.position + Vector3.up * 3f;
            if (rig.barrels) at = rig.barrels.TransformPoint(new Vector3(0f, 2f, 0f));               // between the two barrels
            string id = start ? "station_crane_ram_hiss" : "station_crane_ram_release";
            string fallback = start ? "bld_factory_vent" : "veh_brake";
            if (AudioDirector.Has(id)) AudioDirector.Fire(id, at, rig.id, 1f);
            else if (AudioDirector.Has(fallback)) AudioDirector.Fire(fallback, at, rig.id, 1f);
            if (start) RamHisses++; else RamReleases++;
        }

        // ------------------------------------------------------------------ proof (Play): the same test as the kit's tools/ram_check.py
        /// <summary>Proof: vertices of the station's body, the gantry, the hoist and the ship (its SH_ parts; null = none) strictly inside a
        /// ram part now (shrunk 5 mm; the bolted-on ends excepted). -1 = no rams on that station.</summary>
        public static int RamHits(int stationId, Transform ship, out string where)
        {
            where = "-";
            if (!ramRigs.TryGetValue(stationId, out var rig) || !rig.go || !rig.go.activeInHierarchy) return -1;
            var pts = new List<Vector3>();
            void AddMesh(MeshFilter f)
            {
                if (!f || !f.sharedMesh || !f.sharedMesh.isReadable) return;
                var r = f.GetComponent<Renderer>(); if (r && !r.enabled) return;
                var m = f.transform.localToWorldMatrix; foreach (var v in f.sharedMesh.vertices) pts.Add(m.MultiplyPoint3x4(v));
            }
            foreach (var f in rig.station.GetComponentsInChildren<MeshFilter>()) AddMesh(f);              // the body (rest), the gantry, the hoist
            if (ship) foreach (var f in ship.GetComponentsInChildren<MeshFilter>()) if (f.name.StartsWith("SH_")) AddMesh(f);
            int hits = 0;
            foreach (var sh in rig.shapes)
            {
                var gt = rig.groupT[sh.group]; if (!gt) continue;
                var toLocal = gt.worldToLocalMatrix; var hs = sh.size * 0.5f;
                foreach (var p in pts)
                {
                    var q = toLocal.MultiplyPoint3x4(p) - sh.c;                                         // in the part's mesh space (metres)
                    float lx = Vector3.Dot(q, sh.ax), ly = Vector3.Dot(q, sh.ay), lz = Vector3.Dot(q, sh.az);
                    if (sh.cyl)
                    {
                        if (Mathf.Abs(ly) >= hs.y - 0.005f) continue;
                        float rr = hs.x - 0.005f; if (lx * lx + lz * lz >= rr * rr) continue;
                    }
                    else
                    {
                        if (Mathf.Abs(lx) >= hs.x - 0.005f || Mathf.Abs(ly) >= hs.y - 0.005f || Mathf.Abs(lz) >= hs.z - 0.005f) continue;
                        if (sh.bolted && Mathf.Abs(q.x + sh.c.x - sh.endX) < sh.embed + 0.005f) continue;    // the bolted-in end
                    }
                    hits++; if (where == "-") where = $"{sh.name} at {p}";
                }
            }
            return hits;
        }

        /// <summary>Proof: the rams' pin-to-pin length now (m) on a station (west, east); false = no rams there.</summary>
        public static bool RamLengths(int stationId, out float west, out float east)
        {
            west = east = 0f;
            if (!ramRigs.TryGetValue(stationId, out var rig) || !rig.go) return false;
            OzCraneRamParts.Pose(rig.off, out _, out float L, out _, out _);
            west = east = L;                                                                         // mirror twins: one pose for both
            return true;
        }

        /// <summary>Proof: the world middle of the rams of a station (for the camera), false = none.</summary>
        public static bool RamCentre(int stationId, out Vector3 at)
        {
            at = Vector3.zero;
            if (!ramRigs.TryGetValue(stationId, out var rig) || !rig.go) return false;
            at = rig.frameT.TransformPoint(new Vector3(0f, 3f, 1f + rig.off.z * 0.5f)); return true;
        }
    }
}
