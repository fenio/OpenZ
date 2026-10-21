using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    public enum FactoryDoorStyle { SplitSlide, RollUp }

    /// <summary>
    /// A rectangle on a imported building's front face, in FRACTIONS of the model's local bounds (Unity axes: x = local min.x
    /// -> max.x, y = bottom -> top, z = back -> front; the door is on the +Z face). z = the surface depth there. Measured
    /// </summary>
    [System.Serializable]
    public struct FacePatch
    {
        public Vector2 x, y; public float z;
        public FacePatch(float x0, float x1, float y0, float y1, float z) { x = new Vector2(x0, x1); y = new Vector2(y0, y1); this.z = z; }
        public bool Valid => x.y > x.x && y.y > y.x;
    }

    /// <summary>Door, lights, vents and ramp of one building model (robot factory, vehicle factory, fort). Tunable in the
    /// FactoryViews inspector: the fractions follow the model whatever its scale; doorNudge / doorGrow are metres.</summary>
    [System.Serializable]
    public class FactoryDoorSpec
    {
        public string key;                                  // "robot_factory", "vehicle_factory", "fort" (fort_front + fort_back)
        public FactoryDoorStyle style;
        public FacePatch door;                              // the door leaf / shutter area (z = the baked door surface)
        [Range(0, 1)] public float split = 0.5f;            // SplitSlide: where the two leaves meet (fraction of the model width)
        public Vector3 doorNudge;                           // metres, rig-local (x across, y up, z out of the wall)
        public Vector2 doorGrow;                            // metres added to the door width / height (split evenly)
        public FacePatch[] beacons;                         // blinking team lamps (the model's red lamps / lamp boxes / slits)
        public FacePatch[] windows;                         // glowing team windows (the model's blue glass)
        public FacePatch strip;                             // production progress strip (on the lintel)
        public Vector3[] vents;                             // smoke / steam vents (fractions x, y, z); [0] = the main one
        public Vector2[] floor;                             // ramp / threshold top in front of the door: (z, y) fractions (tools/floor_profile.py)
        public Color smokeColor = new Color(0.44f, 0.41f, 0.38f, 1f);
        public string doorTexture;                          // Resources path of the baked door texture

        public static string KeyOf(string type) => type == "fort_front" || type == "fort_back" ? "fort" : type;

        /// <summary>Measured on m02_robot_factory / m03_vehicle_factory / m01_fort (README_IMPORT.md, door offset table).</summary>
        public static List<FactoryDoorSpec> Defaults() => new List<FactoryDoorSpec>
        {
            new FactoryDoorSpec
            {
                key = "robot_factory", style = FactoryDoorStyle.SplitSlide, split = 0.4975f,
                door = new FacePatch(0.303f, 0.692f, 0.052f, 0.356f, 0.8667f),              // hazard double door
                beacons = new[] { new FacePatch(0.2215f, 0.2435f, 0.119f, 0.197f, 0.9528f), new FacePatch(0.7555f, 0.7745f, 0.119f, 0.197f, 0.9528f) },   // the red door lamps
                windows = new[] { new FacePatch(0.2061f, 0.312f, 0.5376f, 0.5794f, 0.7917f), new FacePatch(0.6852f, 0.7939f, 0.5376f, 0.5794f, 0.7917f) },
                strip = new FacePatch(0.36f, 0.64f, 0.409f, 0.427f, 0.891f),                // green arch over the door
                vents = new[] { new Vector3(0.269f, 1.0f, 0.223f), new Vector3(0.51f, 0.769f, 0.22f) },   // chimney, roof grate
                floor = new[] { new Vector2(0.872f, 0.055f), new Vector2(0.90f, 0.0481f), new Vector2(0.94f, 0.0389f), new Vector2(0.98f, 0.0296f), new Vector2(1.0f, 0.025f) },   // ramp 0.28 -> 0.13 m
                smokeColor = new Color(0.44f, 0.41f, 0.38f, 1f), doorTexture = "OpenZ/FactoryDoors/door_robot_factory",
            },
            new FactoryDoorSpec
            {
                key = "vehicle_factory", style = FactoryDoorStyle.RollUp,
                door = new FacePatch(0.318f, 0.682f, 0.045f, 0.528f, 0.8567f),              // roller shutter
                beacons = new[] { new FacePatch(0.315f, 0.355f, 0.636f, 0.664f, 0.8801f), new FacePatch(0.645f, 0.685f, 0.636f, 0.664f, 0.8801f) },   // lamp boxes on the lintel
                windows = new[] { new FacePatch(0.0864f, 0.1727f, 0.5933f, 0.6685f, 0.7659f) },
                strip = new FacePatch(0.36f, 0.64f, 0.548f, 0.572f, 0.8833f),
                vents = new[] { new Vector3(0.507f, 0.88f, 0.428f), new Vector3(0.25f, 0.83f, 0.22f) },    // roof fan, vent box
                floor = new[] { new Vector2(0.862f, 0.0335f), new Vector2(0.97f, 0.0319f), new Vector2(1.0f, 0.012f) },   // apron 0.12 m
                smokeColor = new Color(0.55f, 0.53f, 0.50f, 1f), doorTexture = "OpenZ/FactoryDoors/door_vehicle_factory",
            },
            new FactoryDoorSpec
            {
                key = "fort", style = FactoryDoorStyle.SplitSlide, split = 0.5f,
                door = new FacePatch(0.427f, 0.573f, 0.106f, 0.362f, 0.8514f),              // the gate's double door
                beacons = new[] { new FacePatch(0.3482f, 0.3593f, 0.2702f, 0.3454f, 0.8487f), new FacePatch(0.6407f, 0.6518f, 0.2674f, 0.3454f, 0.8497f) },   // blue slits beside the gate
                windows = new[] { new FacePatch(0.117f, 0.1811f, 0.5738f, 0.6072f, 0.8781f), new FacePatch(0.8189f, 0.883f, 0.5738f, 0.6072f, 0.8781f) },   // front tower windows
                strip = new FacePatch(0.43f, 0.57f, 0.434f, 0.452f, 0.8525f),
                vents = new[] { new Vector3(0.25f, 0.53f, 0.5f), new Vector3(0.75f, 0.53f, 0.5f) },       // courtyard grates
                floor = new[] { new Vector2(0.856f, 0.1053f), new Vector2(0.88f, 0.1029f), new Vector2(0.90f, 0.0919f), new Vector2(0.94f, 0.0672f), new Vector2(0.98f, 0.0427f), new Vector2(1.0f, 0.020f) },   // gate ramp 1.28 -> 0.25 m
                smokeColor = new Color(0.50f, 0.48f, 0.45f, 1f), doorTexture = "OpenZ/FactoryDoors/door_fort",
            },
        };
    }

    /// <summary>
    /// View of one producing building (fort, robot factory, vehicle factory), built as a sibling rig over the static model
    /// (so TeamPaint and BuildingBreaker never touch it): a door panel carrying the baked door texture placed over the
    /// model's own door (2-10 cm proud, no co-planar faces) that slides apart (robot factory, fort) or rolls up (vehicle
    /// factory) over a dark opening; team-coloured beacons that blink while the Sim produces, glowing windows, a progress
    /// strip on the lintel driven by ZObject.ProductionProgress, vent smoke, a warm light while the door is open, and the
    /// exit staging: a new unit starts inside the opening and walks / rolls out along its Zod force-move path.
    /// Timing is taken from the Sim only: the door opens doorLead sim-seconds before BFinal (never for cannons or at the
    /// unit cap), stays open until the new squad / vehicle is out, then closes. View only: nothing here changes the Sim.
    /// </summary>
    [DisallowMultipleComponent]
    public class FactoryView : MonoBehaviour
    {
        public ZObject B { get; private set; }
        public FactoryDoorSpec Spec { get; private set; }
        public Transform Building { get; private set; }
        /// <summary>Door openness 0 (closed) .. 1 (open).</summary>
        public float Open { get; private set; }
        /// <summary>The Sim is building a unit here and the owner is below the unit cap.</summary>
        public bool Producing { get; private set; }
        public Bounds ModelBounds => mb;
        /// <summary>Door opening in rig-local metres (rig = the building's position + yaw, +z = out of the door).</summary>
        public Vector3 DoorMin => doorMin;
        public Vector3 DoorMax => doorMax;

        FactoryViews mgr; ZGame game;
        Bounds mb; Vector3 doorMin, doorMax; float panelZ, edgeZ;
        Transform leafA, leafB; Material matA, matB; float splitX;
        Renderer interior; Material interiorMat;
        Mesh lightMesh; MeshRenderer lightRenderer; readonly List<Color> cols = new List<Color>();
        int beaconFirst, beaconCount, windowFirst, windowCount, stripFirst, stripCount;
        Light doorLight;
        Transform bay; Light bayLamp;
        /// <summary>
        /// building model carries it; gateDepth = metres from the door plane back to the lit inner portal (new units start there).</summary>
        GateKitView gate; float gateDepth;
        public GateKitView Gate => gate;
        /// <summary></summary>
        public bool HasBay => bay;
        float[] ventTimer; float flashUnit = -10f, flashCannon = -10f; bool shown = true; float seed;
        bool wasProducing; double lastLeft = -1; float burstLeft, burstNext; float lastUnitOut = -10f;
        /// <summary>FS1 proof counters: chimney bursts started / their sounds.</summary>
        public static int ChimneyBursts, ChimneyBurstSounds;
        Vector3 exitDir = Vector3.forward;
        readonly List<Exit> exits = new List<Exit>();
        readonly List<ZObject> visitors = new List<ZObject>();
        const float InteriorGap = 0.02f, PanelGap = 0.045f, PanelThick = 0.05f, LightGap = 0.025f;

        class Exit { public readonly List<ZObject> units = new List<ZObject>(); public double t0; public float D, T, r, half; }
        /// <summary>Half the width (m) a robot of a new squad is given while it passes the door: its shoulders, arms and held weapon
        /// (0.45 kept only the middle of its body inside the opening, so the outer members walked through the door posts).</summary>
        public const float ExitRobotHalfM = 0.85f;

        Vector3 P(float fx, float fy, float fz) => mb.min + Vector3.Scale(new Vector3(fx, fy, fz), mb.size);

        // ------------------------------------------------------------------ build
        public bool Init(FactoryViews m, ZGame g, ZObject b, Transform building, FactoryDoorSpec spec)
        {
            mgr = m; game = g; B = b; Building = building; Spec = spec;
            seed = (b.Id * 0.618034f) % 1f;
            transform.SetPositionAndRotation(building.position, Quaternion.Euler(0, building.eulerAngles.y, 0));
            mb = LocalBounds(building);
            if (mb.size.x < 0.5f || mb.size.y < 0.5f || mb.size.z < 0.5f) { Debug.LogWarning($"[OpenZ] FactoryView: no model bounds on {building.name}"); return false; }
            var d = spec.door;
            doorMin = P(d.x.x, d.y.x, d.z) + spec.doorNudge - new Vector3(spec.doorGrow.x, spec.doorGrow.y, 0) * 0.5f;
            doorMax = P(d.x.y, d.y.y, d.z) + spec.doorNudge + new Vector3(spec.doorGrow.x, spec.doorGrow.y, 0) * 0.5f;
            splitX = Mathf.Clamp(P(spec.split, 0, 0).x + spec.doorNudge.x, doorMin.x + 0.05f, doorMax.x - 0.05f);
            panelZ = doorMin.z + PanelGap;
            edgeZ = spec.floor != null && spec.floor.Length > 0 ? P(0, 0, spec.floor[spec.floor.Length - 1].x).z : doorMin.z + 1f;
            // exit direction from the Zod anchors (creation point -> force-move exit), rig-local
            if (b.GetCreationPoint(out int cx, out int cy) && b.GetCreationMovePoint(out int ex, out int ey))
            {
                var a = transform.InverseTransformPoint(ZGame.PxToWorld(cx, cy)); var e = transform.InverseTransformPoint(ZGame.PxToWorld(ex, ey));
                var dir = e - a; dir.y = 0; if (dir.sqrMagnitude > 0.01f) exitDir = dir.normalized;
            }
            if (mgr.doors) BuildDoor();
            if (gate)
            {
                Vector3 R(Vector3 p) => transform.InverseTransformPoint(Building.TransformPoint(p));
                var dp = R(gate.DoorPoint); var ip = R(gate.InPoint);
                gateDepth = Mathf.Max(0f, Vector3.Dot(dp - ip, exitDir));
                Debug.Log($"[OpenZ Gates] {b.Type} #{b.Id}: door plane {dp}, portal {ip}, units start {gateDepth:F2} m deeper");
            }
            BuildLights();
            ventTimer = new float[spec.vents != null ? spec.vents.Length : 0];
            for (int i = 0; i < ventTimer.Length; i++) ventTimer[i] = Random.Range(0f, 0.8f);
            if (mgr.doorLight)
            {
                var lg = new GameObject("DoorLight"); lg.transform.SetParent(transform, false);
                lg.transform.localPosition = new Vector3((doorMin.x + doorMax.x) * 0.5f, doorMin.y + Mathf.Min(1.2f, (doorMax.y - doorMin.y) * 0.5f), doorMin.z + 0.8f);
                if (gate && gate.HasLight) lg.transform.position = Building.TransformPoint(gate.LightPoint);   // B7: ONE budgeted light in the hallway (gate_mark_light)
                doorLight = lg.AddComponent<Light>(); doorLight.type = LightType.Point; doorLight.shadows = LightShadows.None;
                doorLight.renderMode = LightRenderMode.ForcePixel; doorLight.color = new Color(1f, 0.78f, 0.5f);
                doorLight.range = Mathf.Clamp((doorMax.x - doorMin.x) * 2.4f, 5f, 11f); doorLight.intensity = 0; doorLight.enabled = false;
            }
            return true;
        }

        /// <summary>Model bounds in the building root's local space (MeshFilter mesh bounds through their transforms).</summary>
        static Bounds LocalBounds(Transform root)
        {
            bool any = false; var b = new Bounds();
            var w2l = root.worldToLocalMatrix;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh; if (!m) continue;
                var mbb = m.bounds; var M = w2l * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = mbb.center + Vector3.Scale(mbb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = M.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        void BuildDoor()
        {
            // quads, no dark opening quad and no code-made FactoryInterior bay on top of it (no double walls, no z-fighting)
            gate = GateKitView.Bind(Building, Spec.key);
            if (gate) { ApplyDoor(0); return; }
            float w = doorMax.x - doorMin.x, h = doorMax.y - doorMin.y;
            float gx = 0.015f * w, gy = 0.01f * h;                                  // the panel hides in the frame (texture baked with the same margins)
            float x0 = doorMin.x - gx, x1 = doorMax.x + gx, y0 = doorMin.y - gy, y1 = doorMax.y + gy;
            // dark opening, 2 cm over the baked door
            var io = new GameObject("DoorOpening"); io.transform.SetParent(transform, false);
            var im = FactoryMeshes.Quad(new Vector3(doorMin.x - 0.004f * w, doorMin.y - 0.004f * h, doorMin.z + InteriorGap), new Vector3(doorMax.x + 0.004f * w, doorMax.y + 0.004f * h, doorMin.z + InteriorGap), Color.white);
            io.AddComponent<MeshFilter>().sharedMesh = im;
            var ir = io.AddComponent<MeshRenderer>(); ir.shadowCastingMode = ShadowCastingMode.Off;
            interiorMat = FactoryMeshes.InteriorMaterial(); ir.sharedMaterial = interiorMat; interior = ir;
            var tex = FactoryMeshes.DoorTexture(Spec);
            var baseMat = BuildingMaterial();
            bay = FactoryInterior.Build(transform, Building, doorMin, doorMax, baseMat, out bayLamp);
            if (bay) ir.enabled = false;
            if (Spec.style == FactoryDoorStyle.RollUp)
            {
                leafA = MakeLeaf("Shutter", new Vector3((x0 + x1) * 0.5f, y1, panelZ), FactoryMeshes.Box(-(x1 - x0) * 0.5f, (x1 - x0) * 0.5f, -(y1 - y0), 0, 0, PanelThick, false), tex, baseMat, out matA);
            }
            else
            {
                float sx = splitX;
                leafA = MakeLeaf("LeafA", new Vector3(x0, (y0 + y1) * 0.5f, panelZ), FactoryMeshes.Box(0, sx - x0, -(y1 - y0) * 0.5f, (y1 - y0) * 0.5f, 0, PanelThick, false), tex, baseMat, out matA);
                leafB = MakeLeaf("LeafB", new Vector3(x1, (y0 + y1) * 0.5f, panelZ), FactoryMeshes.Box(-(x1 - sx), 0, -(y1 - y0) * 0.5f, (y1 - y0) * 0.5f, 0, PanelThick, true), tex, baseMat, out matB);
            }
            ApplyDoor(0);
        }

        Transform MakeLeaf(string name, Vector3 pivot, Mesh mesh, Texture tex, Material baseMat, out Material mat)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false); go.transform.localPosition = pivot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true;
            mat = FactoryMeshes.DoorMaterial(baseMat, tex); r.sharedMaterial = mat;
            return go.transform;
        }

        /// <summary>The building's own TeamLit material (same shading as the model), or null.</summary>
        Material BuildingMaterial()
        {
            foreach (var r in Building.GetComponentsInChildren<MeshRenderer>(true))
                if (r.sharedMaterial && r.sharedMaterial.HasProperty("_BaseMap")) return r.sharedMaterial;
            return null;
        }

        void BuildLights()
        {
            var verts = new List<Vector3>(); var tris = new List<int>(); cols.Clear();
            void Patch(FacePatch p, float shrink, float gap)
            {
                var a = P(p.x.x, p.y.x, p.z); var c = P(p.x.y, p.y.y, p.z);
                float sx = (c.x - a.x) * shrink, sy = (c.y - a.y) * shrink;
                FactoryMeshes.AddQuad(verts, tris, cols, new Vector3(a.x + sx, a.y + sy, a.z + gap), new Vector3(c.x - sx, c.y - sy, a.z + gap), Color.black);
            }
            beaconFirst = cols.Count / 4; beaconCount = Spec.beacons != null ? Spec.beacons.Length : 0;
            for (int i = 0; i < beaconCount; i++) Patch(Spec.beacons[i], 0.02f, LightGap);
            windowFirst = cols.Count / 4; windowCount = Spec.windows != null ? Spec.windows.Length : 0;
            for (int i = 0; i < windowCount; i++) Patch(Spec.windows[i], 0.05f, LightGap);
            stripFirst = cols.Count / 4; stripCount = 0;
            if (Spec.strip.Valid)
            {
                var a = P(Spec.strip.x.x, Spec.strip.y.x, Spec.strip.z); var c = P(Spec.strip.x.y, Spec.strip.y.y, Spec.strip.z);
                FactoryMeshes.AddQuad(verts, tris, cols, new Vector3(a.x, a.y, a.z + 0.02f), new Vector3(c.x, c.y, a.z + 0.02f), new Color(0.02f, 0.02f, 0.025f, 1f));   // dark display backing
                stripFirst = cols.Count / 4;
                const int N = 10; float segW = (c.x - a.x) / N, inY = (c.y - a.y) * 0.2f;
                for (int i = 0; i < N; i++)
                    FactoryMeshes.AddQuad(verts, tris, cols, new Vector3(a.x + segW * (i + 0.12f), a.y + inY, a.z + 0.03f), new Vector3(a.x + segW * (i + 0.88f), c.y - inY, a.z + 0.03f), Color.black);
                stripCount = N;
            }
            if (verts.Count == 0) return;
            lightMesh = new Mesh { name = "FactoryLights" };
            lightMesh.SetVertices(verts); lightMesh.SetColors(cols); lightMesh.SetTriangles(tris, 0); lightMesh.RecalculateBounds();
            lightMesh.MarkDynamic();
            var go = new GameObject("Lights"); go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = lightMesh;
            lightRenderer = go.AddComponent<MeshRenderer>(); lightRenderer.shadowCastingMode = ShadowCastingMode.Off; lightRenderer.receiveShadows = false;
            lightRenderer.sharedMaterial = FactoryMeshes.LightMaterial();
        }

        void OnDestroy()
        {
            if (matA) Destroy(matA); if (matB) Destroy(matB); if (interiorMat) Destroy(interiorMat);
            if (lightMesh) Destroy(lightMesh);
        }

        // ------------------------------------------------------------------ sim hooks (FactoryViews routes the events)
        public void OnUnitBuilt(ZObject u)
        {
            flashUnit = Time.time;
            if (u == null || B.Destroyed) return;
            var ex = new Exit { t0 = game.World.Time };
            ex.units.Add(u); foreach (var m in u.Minions) ex.units.Add(m);
            bool vehicle = u.Kind == ZKind.Vehicle;
            float half = vehicle ? 2.3f : 0.45f;                                      // half length of the model along the exit
            var sp = transform.InverseTransformPoint(ZGame.PxToWorld(u.CenterX, u.CenterY));
            float along = Vector3.Dot(sp - new Vector3(sp.x, sp.y, doorMin.z), exitDir);   // + = already past the door plane
            ex.D = Mathf.Max(vehicle ? 1.6f : 1.4f, along + half + 0.3f) + gateDepth;          // B7: from the lit portal at the end of the hallway
            float speed = Mathf.Max(0.6f, u.MoveSpeed / 8f);                          // px/s -> m/s (grunt 14 px/s = 1.75 m/s, measured)
            // the view catches up with the Sim while the walk / drive animation runs at Sim speed: T = 2.5 D / speed keeps the
            // eased catch-up (peak 1.5 D / T) at <= 0.6x the unit's own speed, so the exit never reads as a 2x slide (the old
            // 0.9 D / speed gave ~2.2x for a Grunt); still done long before the unit reaches the exit point and turns
            ex.T = Mathf.Clamp(2.5f * ex.D / speed, 0.6f, 3.2f);
            ex.r = vehicle ? 1.15f : ExitRobotHalfM; ex.half = half;
            exits.Add(ex);
            var fx = FxManager.I;
            if (fx)
            {
                var doorC = transform.TransformPoint(new Vector3((doorMin.x + doorMax.x) * 0.5f, doorMin.y + 1f, doorMin.z + 1.2f));
                fx.AddLight(doorC, TeamColors.Of(B.Owner), 3f, 7f, 0.7f, 0.2f, false);
            }
            lastUnitOut = Time.time;
            StartChimneyBurst(false);                                                  // FS1: the exhaust burst (the unit-out sound below)
            FactoryViews.Sound("factory_unit_out", transform.TransformPoint((doorMin + doorMax) * 0.5f), B.Id, B, 0f);   // hook only: RulesView plays "built"
        }

        public void OnCannonStored() { flashCannon = Time.time; }

        // ------------------------------------------------------------------ per frame
        /// <summary>Called by FactoryViews.Update (after the Sim ticked this frame): state, door, lights, vents.</summary>
        public void Tick(float dtSim, float dtFx)
        {
            var W = game.World;
            bool destroyed = B.Destroyed;
            SetShown(!destroyed);
            if (destroyed) { Open = 0; exits.Clear(); Producing = false; if (doorLight) doorLight.enabled = false; if (bayLamp) bayLamp.enabled = false; return; }
            bool own = B.Owner != Team.Neutral;
            bool building = own && B.Building && B.CurUnit != null;
            bool cannon = building && ZBuildLists.IsCannon(B.CurUnit);
            bool cap = own && W.UnitLimitReached(B.Owner);
            Producing = building && !cap;
            double left = B.ProductionTimeLeft;
            bool soon = building && !cannon && !cap && left <= mgr.doorLead;
            bool want = soon | UpdateExits() | UpdateVisitors() | gateBusy;
            float prev = Open;
            if (mgr.doors) Open = Mathf.MoveTowards(Open, want ? 1f : 0f, dtSim / Mathf.Max(0.05f, want ? mgr.openTime : mgr.closeTime));
            if (prev <= 0.001f && Open > 0.001f) FactoryViews.Sound("factory_door_open", DoorWorld(), B.Id, B, 0.55f);
            if (prev >= 0.999f && Open < 0.999f) FactoryViews.Sound("factory_door_close", DoorWorld(), B.Id, B, 0.45f);
            if (Open != prev) ApplyDoor(Open);
            UpdateLights(own, building, cannon, cap, left);
            // FS1: a new production starts = the factory starts building (capture, new choice) or the time left jumps up (next unit)
            if (Producing && (!wasProducing || (lastLeft >= 0 && left > lastLeft + 0.5))) StartChimneyBurst(Time.time - lastUnitOut > 0.6f);
            wasProducing = Producing; lastLeft = Producing ? left : -1;
            if (mgr.smoke) UpdateVents(dtFx, Producing);
            if (doorLight)
            {
                float k = Open * Open * (3f - 2f * Open);
                doorLight.enabled = k > 0.01f; doorLight.intensity = 2.4f * k * (0.92f + 0.08f * Mathf.PerlinNoise(seed * 10f, Time.time * 3f));
            }
            if (interiorMat) interiorMat.SetColor(FactoryMeshes.BaseColorId, Color.Lerp(FactoryMeshes.InteriorDark, FactoryMeshes.InteriorLit, Open));
            if (bayLamp)
            {   // the bay's work light comes up with the door (a faint glow stays while it is shut: nobody sees it)
                float k = Mathf.Clamp01(Open * 1.6f);
                bayLamp.enabled = k > 0.01f; bayLamp.intensity = 3.2f * k * (0.95f + 0.05f * Mathf.PerlinNoise(seed * 7f, Time.time * 2f));
            }
        }

        Vector3 DoorWorld() => transform.TransformPoint((doorMin + doorMax) * 0.5f);

        /// <summary>GW1 (DoorWear): the drawn door opening on the ground (world, y = 0), its outward direction (Zod's creation point ->
        /// exit) and its width in metres. False before the rig is built or when the model has no door.</summary>
        public bool DoorOnGround(out Vector3 centre, out Vector3 outward, out float width)
        {
            centre = DoorWorld(); centre.y = 0f;
            outward = transform.TransformDirection(exitDir); outward.y = 0f;
            width = transform.TransformVector(new Vector3(doorMax.x - doorMin.x, 0f, 0f)).magnitude;
            if (outward.sqrMagnitude < 1e-4f || doorMax.x <= doorMin.x) return false;
            outward.Normalize();
            return true;
        }
        Vector3 VentWorld(int i) { var v = Spec.vents[i]; return transform.TransformPoint(P(v.x, v.y, v.z) + Vector3.up * 0.08f); }

        void SetShown(bool on)
        {
            if (on == shown) return;
            shown = on;
            foreach (Transform c in transform) c.gameObject.SetActive(on);
        }

        static float Ease(float k) => k * k * (3f - 2f * k);

        void ApplyDoor(float open)
        {
            if (gate) { gate.Apply(open, Time.time); return; }
            float k = Ease(Mathf.Clamp01(open));
            if (Spec.style == FactoryDoorStyle.RollUp)
            {
                if (!leafA) return;
                float s = Mathf.Max(0.03f, 1f - k);
                leafA.localScale = new Vector3(1f, s, 1f);
                if (matA) { matA.SetTextureScale(FactoryMeshes.BaseMapId, new Vector2(1f, s)); matA.SetTextureOffset(FactoryMeshes.BaseMapId, Vector2.zero); }
            }
            else
            {
                float s = Mathf.Max(0.04f, 1f - k);
                float w = doorMax.x - doorMin.x + 0.03f * (doorMax.x - doorMin.x);
                float sa = Mathf.Clamp01((splitX - (doorMin.x - 0.015f * (doorMax.x - doorMin.x))) / w);   // leaf A share of the texture
                if (leafA) { leafA.localScale = new Vector3(s, 1f, 1f); if (matA) { matA.SetTextureScale(FactoryMeshes.BaseMapId, new Vector2(sa * s, 1f)); matA.SetTextureOffset(FactoryMeshes.BaseMapId, new Vector2(sa * (1f - s), 0f)); } }
                if (leafB) { leafB.localScale = new Vector3(s, 1f, 1f); if (matB) { matB.SetTextureScale(FactoryMeshes.BaseMapId, new Vector2(-(1f - sa) * s, 1f)); matB.SetTextureOffset(FactoryMeshes.BaseMapId, new Vector2(1f - (1f - sa) * (1f - s), 0f)); } }
            }
        }

        // ------------------------------------------------------------------ lights
        static Color Lin(Color srgb, float gain) { var c = srgb.linear * gain; c.a = 1f; return c; }

        void UpdateLights(bool own, bool building, bool cannon, bool cap, double left)
        {
            if (lightRenderer && lightRenderer.enabled != mgr.lights) lightRenderer.enabled = mgr.lights;
            if (!lightMesh || !mgr.lights) return;
            float t = Time.time + seed * 3f;
            var team = TeamColors.Of(B.Owner);
            Color bright = Lin(team, 2.6f), mid = Lin(team, 1.1f), dim = Lin(team, 0.22f), off = new Color(0.035f, 0.035f, 0.04f, 1f);
            bool dmg = B.MaxHealth > 0 && B.Health < B.MaxHealth * 0.4f && Mathf.PerlinNoise(seed * 7f, Time.time * 6f) < 0.3f;   // damaged: lights drop out
            float realLeft = (float)left / Mathf.Max(0.25f, game.gameSpeed);
            // beacons: alternate while producing, fast in the last 5 s, both on while the door is open, slow pulse at the unit cap
            for (int i = 0; i < beaconCount; i++)
            {
                Color c;
                if (!own) c = off;
                else if (Open > 0.05f) c = bright;
                else if (cap && building) c = Color.Lerp(dim, mid, 0.5f + 0.5f * Mathf.Sin(t * 3.1f));
                else if (building)
                {
                    float period = realLeft < 5f ? 0.45f : 1.1f;
                    bool on = Mathf.Repeat(t / period + (i % 2) * 0.5f, 1f) < 0.5f;
                    c = on ? bright : dim;
                }
                else c = dim;
                if (dmg) c = off;
                SetQuad(beaconFirst + i, c);
            }
            for (int i = 0; i < windowCount; i++)
            {
                // neutral: unlit grey glass (the model's baked blue would read as the blue team)
                Color c = !own ? new Color(0.075f, 0.08f, 0.085f, 1f) : building ? Lin(team, 1.25f + 0.25f * Mathf.Sin(t * 1.7f + i)) : Lin(team, 0.45f);
                if (dmg) c *= 0.3f;
                SetQuad(windowFirst + i, c);
            }
            if (stripCount > 0)
            {
                float prog = building ? (float)B.ProductionProgress : 0f;
                int filled = Mathf.FloorToInt(prog * stripCount);
                float unitK = Mathf.Clamp01((Time.time - flashUnit) / 0.5f), canK = Mathf.Clamp01((Time.time - flashCannon) / 0.5f);
                for (int i = 0; i < stripCount; i++)
                {
                    Color c;
                    if (!own) c = off;
                    else if (!building) c = Lin(team, 0.08f);
                    else if (i < filled) c = cap ? Color.Lerp(dim, bright, 0.5f + 0.5f * Mathf.Sin(t * 3.1f)) : Lin(team, 1.9f);
                    else if (i == filled) c = Color.Lerp(dim, bright, 0.5f + 0.5f * Mathf.Sin(t * 7f));
                    else c = Lin(team, 0.12f);
                    if (unitK < 1f) c = Color.Lerp(Lin(team, 3.2f), c, unitK);                              // unit out: the strip flashes
                    if (canK < 1f) c = Color.Lerp(new Color(2.4f, 2.3f, 2.1f, 1f), c, canK);                // cannon stored: white flash
                    SetQuad(stripFirst + i, c);
                }
            }
            lightMesh.SetColors(cols);
        }

        void SetQuad(int q, Color c) { int v = q * 4; if (v + 3 >= cols.Count) return; cols[v] = cols[v + 1] = cols[v + 2] = cols[v + 3] = c; }

        // ------------------------------------------------------------------ smoke
        void UpdateVents(float dt, bool producing)
        {
            if (Spec.vents == null || dt <= 0) return;
            // FS1: the chimney (vent 0) only smokes in the burst (~1.5 s of thick puffs, thinning out)
            if (burstLeft > 0f && Spec.vents.Length > 0)
            {
                burstLeft -= dt; burstNext -= dt;
                if (burstNext <= 0f)
                {
                    float k = Mathf.Clamp01(burstLeft / 1.5f);                                             // 1 at the start -> 0
                    burstNext = Mathf.Lerp(0.26f, 0.1f, k);
                    mgr.EmitPuff(Spec.key, VentWorld(0), Mathf.Lerp(0.3f, 0.5f, k), Mathf.Lerp(1.3f, 2.2f, k), Random.Range(2.6f, 3.6f), Mathf.Lerp(0.9f, 1.9f, k));
                }
            }
            if (!producing) return;
            for (int i = 1; i < Spec.vents.Length; i++)                                                    // side vents: a rare faint wisp
            {
                ventTimer[i] -= dt;
                if (ventTimer[i] > 0) continue;
                ventTimer[i] = Random.Range(2.6f, 4.4f);
                mgr.EmitPuff(Spec.key, VentWorld(i), 0.2f, 0.7f, Random.Range(1.4f, 2f), Random.Range(0.5f, 0.8f));
                OzSoundV3.VentPuff(VentWorld(i), B);                                                       // sound_v3 hiss (rate limited)
            }
        }

        /// <summary>FS1: the chimney burst of a new production: thick puffs for ~1.5 s, a chuff (unless the unit-out exhaust sound plays
        /// at the same moment), and at dusk / night a warm glow from the flue lighting the smoke and the roof.</summary>
        void StartChimneyBurst(bool sound)
        {
            if (Spec.vents == null || Spec.vents.Length == 0 || B.Destroyed) return;
            if (burstLeft > 0.9f) return;                                                                  // already bursting (unit out + next start)
            burstLeft = 1.5f; burstNext = 0f; ChimneyBursts++;
            var at = VentWorld(0);
            if (sound) { AudioDirector.Fire("bld_factory_exhaust", at, B.Id, 0.75f, Random.Range(0.92f, 1.06f)); ChimneyBurstSounds++; }
            float night = NightGrade.NightK;
            if (night > 0.2f && FxManager.I) FxManager.I.AddLight(at + Vector3.up * 0.4f, new Color(1f, 0.55f, 0.25f), 1.6f + 1.6f * night, 6f, 1.6f, 0.35f, false);
        }

        // ------------------------------------------------------------------ exits
        /// <summary>Drops finished exits; true while a new unit still needs the door open.</summary>
        bool UpdateExits()
        {
            bool need = false; var W = game.World;
            for (int i = exits.Count - 1; i >= 0; i--)
            {
                var ex = exits[i]; float age = (float)(W.Time - ex.t0);
                bool allOut = true, near = false;
                float back = age >= ex.T ? 0f : ex.D * (1f - Ease(Mathf.Clamp01(age / ex.T)));   // the view runs this far behind the Sim
                foreach (var u in ex.units)
                {
                    if (u == null || u.Destroyed || W.Get(u.Id) != u) continue;
                    var lp = transform.InverseTransformPoint(ZGame.PxToWorld(u.CenterX, u.CenterY));
                    float along = Vector3.Dot(lp - new Vector3(lp.x, lp.y, doorMin.z), exitDir) - back;
                    if (along < ex.half + 1.0f) near = true;                               // tail still in the doorway
                    if (lp.z < edgeZ + 1.2f) allOut = false;
                }
                if (near && age < 7f) need = true;
                if ((allOut && age > ex.T) || age > 10f) exits.RemoveAt(i);
            }
            return need;
        }

        /// <summary>Fort gate: enemy units on an ENTER_FORT order (zobject.cpp:2774-2913) within 4 tiles of the gate exit point
        /// or already in the gate open it (Z: the unit walks in through the gate and the fort goes up).</summary>
        bool UpdateVisitors()
        {
            visitors.Clear();
            if (!B.IsFort || !B.GetCreationMovePoint(out int gx, out int gy)) return false;
            foreach (var o in game.World.Objects)
            {
                if (!o.IsMobile || o.Waypoints.Count == 0 || o.Destroyed) continue;
                var w = o.Waypoints[0];
                if (w.mode != WpMode.EnterFort || w.refId != B.Id) continue;
                int dx = o.CenterX - gx, dy = o.CenterY - gy;
                bool near = dx * dx + dy * dy < 64 * 64;
                if (!near)
                {
                    var lp = transform.InverseTransformPoint(ZGame.PxToWorld(o.CenterX, o.CenterY));
                    near = lp.z < edgeZ + 1f && lp.x > doorMin.x - 1f && lp.x < doorMax.x + 1f;           // on the plinth / in the gate
                }
                if (near) visitors.Add(o);
            }
            return visitors.Count > 0;
        }

        /// <summary>Fort gate sensor (view only): true while any drawn, living robot or vehicle of either side stands in the gateway:
        /// across the door opening (+ 0.6 m each side), from the corridor inside the fort out to the hold distance UpdateExits uses
        /// (half length + 1 m past the door plane). Covers what the timed exit hold misses: a squad or a vehicle still in the gate
        /// after its 7 s, and units that are pushed or walk back into the gate later. Units up on the roof deck do not count.
        /// Read in SenseGate, after the units were placed for this frame: in Tick a new unit's view still stands at its Sim point,
        /// metres ahead of the body that is drawn walking out.</summary>
        bool DoorwayBusy()
        {
            if (!B.IsFort) return false;
            const float Side = 0.6f, Inside = 9f, Reach = 16f, MaxUp = 3f;
            var c = new Vector3((doorMin.x + doorMax.x) * 0.5f, 0f, doorMin.z);
            float halfW = (doorMax.x - doorMin.x) * 0.5f + Side;
            var wc = transform.TransformPoint(c);
            foreach (var o in game.World.Objects)
            {
                if (o.Destroyed || (o.Kind != ZKind.Robot && o.Kind != ZKind.Vehicle)) continue;
                var sp = ZGame.PxToWorld(o.CenterX, o.CenterY);
                float qx = sp.x - wc.x, qz = sp.z - wc.z;
                if (qx * qx + qz * qz > Reach * Reach) continue;                         // cheap reject before the view lookup
                if (!game.Views.TryGetValue(o.Id, out var v) || !v || !v.isActiveAndEnabled || v.Dying) continue;
                if (v.transform.position.y > transform.position.y + MaxUp) continue;     // up on the roof deck, not in the gate
                var d = transform.InverseTransformPoint(v.transform.position) - c; d.y = 0f;   // the DRAWN position (staged exits included: see SenseGate)
                float along = Vector3.Dot(d, exitDir), side = Mathf.Abs(d.x * exitDir.z - d.z * exitDir.x);
                if (side < halfW && along > -Inside && along < (o.Kind == ZKind.Vehicle ? 2.3f : 0.45f) + 1.0f) return true;
            }
            return false;
        }

        bool gateBusy;
        /// <summary>Called by FactoryViews.LateUpdate after StageExits: the gate sensor looks at the bodies as they are drawn this
        /// frame; Tick uses the answer in the next frame.</summary>
        public void SenseGate() { gateBusy = shown && DoorwayBusy(); }

        /// <summary>Called by FactoryViews.LateUpdate AFTER UnitView (order 60) placed the units: a unit built this moment is
        /// drawn back inside the opening (offset D along its exit path, eased out over T sim-seconds, so it visibly walks /
        /// rolls out of the door), kept inside the door width while it passes, and lifted onto the ramp / plinth in front.</summary>
        public void StageExits()
        {
            if (!mgr.stageExits || !shown || (exits.Count == 0 && visitors.Count == 0)) return;
            double now = game.World.Time;
            foreach (var ex in exits)
            {
                float age = (float)(now - ex.t0);
                float back = age >= ex.T ? 0f : ex.D * (1f - Ease(Mathf.Clamp01(age / ex.T)));
                foreach (var u in ex.units) Place(u, back, ex.r);
            }
            foreach (var u in visitors) Place(u, 0f, u.Kind == ZKind.Vehicle ? 1.15f : 0.45f);
        }

        /// <summary>One unit view: pushed `back` metres into the opening, kept inside the door width while it passes, lifted
        /// onto the ramp / threshold / plinth in front of the door.</summary>
        void Place(ZObject u, float back, float r)
        {
            if (u == null || !game.Views.TryGetValue(u.Id, out var v) || !v || !v.isActiveAndEnabled || v.Dying) return;
            bool straight = Mathf.Abs(exitDir.z) > 0.7f;
            var lp = transform.InverseTransformPoint(v.transform.position);
            lp -= exitDir * back;
            if (straight)
            {
                float cx = (doorMin.x + doorMax.x) * 0.5f, room = (doorMax.x - doorMin.x) * 0.5f - r;
                float w = 1f - Ease(Mathf.Clamp01((lp.z - doorMin.z - 0.6f) / 2.2f));
                if (w > 0f) lp.x = Mathf.Lerp(lp.x, room <= 0f ? cx : Mathf.Clamp(lp.x, cx - room, cx + room), w);
            }
            var wp = transform.TransformPoint(lp);
            if (straight && FloorAt(lp.z, out float fy, out float wr))
            {
                float want = transform.TransformPoint(new Vector3(lp.x, fy, lp.z)).y - 0.03f;
                if (want > wp.y) wp.y = Mathf.Lerp(wp.y, want, wr);
            }
            var k386From = v.transform.position; v.transform.position = wp; BoardingView.FollowHull(v, wp - k386From);   // K386: a crew posed this frame moves with its hull (BoardingView.K386.cs)
        }

        /// <summary>Top of the ramp / threshold in front of the door (rig-local y) and its blend weight (fades out 0.7 m past
        /// the front edge so units step down smoothly).</summary>
        bool FloorAt(float lz, out float ly, out float weight)
        {
            ly = 0; weight = 0;
            var f = Spec.floor; if (f == null || f.Length == 0) return false;
            float fz = (lz - mb.min.z) / mb.size.z;
            float fy;
            if (fz <= f[0].x) fy = f[0].y;
            else if (fz >= f[f.Length - 1].x) fy = f[f.Length - 1].y;
            else
            {
                fy = f[f.Length - 1].y;
                for (int i = 1; i < f.Length; i++)
                    if (fz <= f[i].x) { fy = Mathf.Lerp(f[i - 1].y, f[i].y, (fz - f[i - 1].x) / Mathf.Max(1e-4f, f[i].x - f[i - 1].x)); break; }
            }
            ly = mb.min.y + fy * mb.size.y;
            weight = lz <= edgeZ ? 1f : 1f - Ease(Mathf.Clamp01((lz - edgeZ) / 0.7f));
            return weight > 0f;
        }

        void OnDrawGizmosSelected()
        {
            if (mb.size == Vector3.zero) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Color.yellow; Gizmos.DrawWireCube((doorMin + doorMax) * 0.5f, new Vector3(doorMax.x - doorMin.x, doorMax.y - doorMin.y, 0.02f));
            Gizmos.color = Color.cyan; Gizmos.DrawWireCube(mb.center, mb.size);
            Gizmos.color = Color.green; Gizmos.DrawLine((doorMin + doorMax) * 0.5f, (doorMin + doorMax) * 0.5f + exitDir * 3f);
        }
    }

    /// <summary>Meshes, textures and materials of the factory rigs (existing OpenZ shaders only: TeamLit, FxLit, Overlay).</summary>
    public static class FactoryMeshes
    {
        public static readonly int BaseMapId = Shader.PropertyToID("_BaseMap"), BaseColorId = Shader.PropertyToID("_BaseColor");
        public static readonly Color InteriorDark = new Color(0.06f, 0.05f, 0.045f, 1f), InteriorLit = new Color(0.38f, 0.24f, 0.12f, 1f);   // sRGB; lit = the work lights inside while the door is open
        static Material lightMat; static readonly Dictionary<string, Texture2D> doorTex = new Dictionary<string, Texture2D>();

        /// <summary>Quad facing +z (rig-local), corners a (min) .. b (max) at a.z.</summary>
        public static Mesh Quad(Vector3 a, Vector3 b, Color c)
        {
            var v = new List<Vector3>(); var t = new List<int>(); var col = new List<Color>();
            AddQuad(v, t, col, a, b, c);
            var m = new Mesh { name = "FactoryQuad" }; m.SetVertices(v); m.SetColors(col); m.SetTriangles(t, 0);
            m.SetNormals(new List<Vector3> { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            m.RecalculateBounds(); return m;
        }

        public static void AddQuad(List<Vector3> v, List<int> t, List<Color> col, Vector3 a, Vector3 b, Color c)
        {
            int k = v.Count;
            v.Add(new Vector3(a.x, a.y, a.z)); v.Add(new Vector3(b.x, a.y, a.z)); v.Add(new Vector3(b.x, b.y, a.z)); v.Add(new Vector3(a.x, b.y, a.z));
            col.Add(c); col.Add(c); col.Add(c); col.Add(c);
            Tri(t, v, k, k + 1, k + 2, Vector3.forward); Tri(t, v, k, k + 2, k + 3, Vector3.forward);
        }

        /// <summary>Triangle wound so that its front face points along `n` (Unity: normal = Cross(b - a, c - a)).</summary>
        static void Tri(List<int> t, List<Vector3> v, int a, int b, int c, Vector3 n)
        {
            if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), n) >= 0) { t.Add(a); t.Add(b); t.Add(c); }
            else { t.Add(a); t.Add(c); t.Add(b); }
        }

        /// <summary>Door leaf / shutter box. Front face (+z) UVs: u = 0 at x0 -> 1 at x1, or for a leaf that extends to -x from
        /// its pivot (towardMinusX) u = 0 at the pivot (x = 0) -> 1 at x0; v = 0 bottom -> 1 top; other faces sample an edge.</summary>
        public static Mesh Box(float x0, float x1, float y0, float y1, float z0, float z1, bool towardMinusX)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            float w = x1 - x0, h = y1 - y0;
            Vector2 UV(float x, float y) => new Vector2(towardMinusX ? -x / w : (x - x0) / w, (y - y0) / h);
            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nn, bool front)
            {
                int k = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                for (int i = 0; i < 4; i++) n.Add(nn);
                if (front) { uv.Add(UV(a.x, a.y)); uv.Add(UV(b.x, b.y)); uv.Add(UV(c.x, c.y)); uv.Add(UV(d.x, d.y)); }
                else for (int i = 0; i < 4; i++) uv.Add(new Vector2(0.002f, 0.002f));
                Tri(t, v, k, k + 1, k + 2, nn); Tri(t, v, k, k + 2, k + 3, nn);
            }
            Face(new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.forward, true);
            Face(new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y0, z0), Vector3.back, false);
            Face(new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), Vector3.up, false);
            Face(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), Vector3.down, false);
            Face(new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), Vector3.left, false);
            Face(new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), Vector3.right, false);
            var m = new Mesh { name = "FactoryDoorLeaf" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }

        /// <summary>The baked door texture (Resources, tools/door_textures.py) or a procedural stand-in.</summary>
        public static Texture2D DoorTexture(FactoryDoorSpec s)
        {
            if (doorTex.TryGetValue(s.key, out var t) && t) return t;
            t = string.IsNullOrEmpty(s.doorTexture) ? null : Resources.Load<Texture2D>(s.doorTexture);   // import: Wrap Mode Clamp
            if (!t) { t = Procedural(s.key); t.wrapMode = TextureWrapMode.Clamp; Debug.Log($"[OpenZ] FactoryView: {s.doorTexture} not in Resources, procedural door for {s.key}"); }
            doorTex[s.key] = t; return t;
        }

        static Texture2D Procedural(string key)
        {
            const int W = 128, H = 128;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "door_" + key + "_proc" };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    Color c; int bx = Mathf.Min(x, W - 1 - x), by = Mathf.Min(y, H - 1 - y);
                    if (key == "robot_factory")
                    {
                        bool yel = Mathf.Repeat((x % (W / 2) + y) / 64f * 1.5f, 1f) < 0.5f;
                        c = yel ? new Color32(236, 178, 22, 255) : new Color32(46, 46, 50, 255);
                        if (bx < 3 || by < 3 || Mathf.Abs(x - W / 2) < 2) c = new Color32(58, 58, 62, 255);
                    }
                    else if (key == "vehicle_factory")
                    {
                        float f = Mathf.Repeat(y / (float)H * 10f, 1f);
                        c = f > 0.88f ? new Color32(112, 114, 118, 255) : f < 0.12f ? new Color32(38, 40, 42, 255) : (Color)new Color32(74, 76, 79, 255) * (0.9f + 0.2f * f);
                        if (bx < 3) c = new Color32(48, 50, 53, 255);
                    }
                    else
                    {
                        c = new Color32(66, 67, 71, 255);
                        if (Mathf.Repeat(y / (float)H * 5f, 1f) < 0.08f || bx < 5 || Mathf.Abs(x - W / 2) < 3) c = new Color32(44, 45, 49, 255);
                    }
                    c.a = 1f; px[y * W + x] = c;
                }
            tex.SetPixels32(px); tex.Apply(true);
            return tex;
        }

        /// <summary>Door panel material: a copy of the building's own TeamLit material (identical shading) with the door texture
        /// and no team recolour; else URP Lit. _Degreen keeps the building's value (1): the model de-greens leftover olive
        /// tints on its own door, so the panel must too (the olive PAINT is already recoloured to the neutral paint in the
        /// baked texture, tools/door_neutral.py), else the closed panel shows a green seam the model does not have.</summary>
        public static Material DoorMaterial(Material building, Texture tex)
        {
            Material m;
            if (building) m = new Material(building);
            else
            {
                m = new Material(FindShader("OpenZ/TeamLit", "Universal Render Pipeline/Lit"));
                if (m.HasProperty("_SatScale")) { m.SetFloat("_SatScale", 0.78f); m.SetFloat("_ValScale", 0.88f); }   // imported match (OpenZSetup.UnitMaterial)
            }
            m.name = "FactoryDoor";
            if (m.HasProperty("_BaseMap")) m.SetTexture(BaseMapId, tex); else m.mainTexture = tex;
            if (m.HasProperty("_BaseColor")) m.SetColor(BaseColorId, Color.white);
            if (m.HasProperty("_TeamMode")) m.SetFloat("_TeamMode", 0f);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            // maps (fort v2: emission x2.6 at night, mask, normal), which the door panel's own UVs sampled at random: lit specks
            // all over the dark door. The panel has its own texture only: no atlas emission, mask or normal map.
            if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", null);
            if (m.HasProperty("_EmissionMapColor")) m.SetColor("_EmissionMapColor", Color.black);
            if (m.HasProperty("_MaskMap")) m.SetTexture("_MaskMap", null);
            if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
            m.DisableKeyword("_USEMASKMAP_ON"); m.DisableKeyword("_USENORMALMAP_ON");
            if (m.HasProperty("_UseMaskMap")) m.SetFloat("_UseMaskMap", 0f);
            if (m.HasProperty("_UseNormalMap")) m.SetFloat("_UseNormalMap", 0f);
            m.enableInstancing = false;
            return m;
        }

        /// <summary>Dark opening behind the door: opaque unlit FxLit (depth-writing, so dust in front still draws over it).</summary>
        public static Material InteriorMaterial()
        {
            var m = new Material(FindShader("OpenZ/FxLit", "Universal Render Pipeline/Unlit")) { name = "FactoryOpening" };
            m.SetColor(BaseColorId, InteriorDark);
            if (m.HasProperty("_Unlit")) m.SetFloat("_Unlit", 1f);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            return m;
        }

        /// <summary>Team lights: OpenZ/Overlay (vertex colour, HDR, unlit, depth-tested, pulled 2 cm off the wall), drawn right
        /// after the opaques so smoke and dust in front still blend over them.</summary>
        public static Material LightMaterial()
        {
            if (lightMat) return lightMat;
            lightMat = new Material(FindShader("OpenZ/Overlay", "Sprites/Default")) { name = "FactoryLights", renderQueue = 2460 };
            if (lightMat.HasProperty("_Color")) lightMat.SetColor("_Color", Color.white);
            if (lightMat.HasProperty("_ZTest")) lightMat.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
            return lightMat;
        }

        public static Shader FindShader(params string[] names)
        {
            foreach (var n in names) { var s = Shader.Find(n); if (s) return s; }
            Debug.LogWarning("[OpenZ] FactoryView: shader missing: " + string.Join(" / ", names));
            return Shader.Find("Hidden/InternalErrorShader");
        }
    }
}
