using System;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    // ------------------------------------------------------------------------------------------------ data (JSON)
    // real meshes of the Z_* vehicle prefabs (prefab root frame: +y up, front = -z).

    /// <summary>One lamp socket: pos / nrm in the mount node's local frame (root / rootNrm = the prefab root frame, the
    /// fallback when the node is missing), sizes in metres (nodeScale = the node's scale relative to the root).</summary>
    [Serializable]
    public class VLSocket
    {
        public string id, kind, node, colour, surface;
        public float[] pos, nrm, root, rootNrm, rgb;
        public float nodeScale = 1f, size, halo;
        public bool directional;
    }

    [Serializable] public class VLPool { public float z, width, length, height; }

    [Serializable]
    public class VLHeadlight
    {
        public string node, colour; public string[] sockets; public float[] root, rgb;
        public float pitch, spotAngle, range, intensity, beamLength, beamAngle, pitchBeam; public VLPool pool;
    }

    [Serializable] public class VLEmissive { public string renderer, role; }

    [Serializable]
    public class VLVehicle
    {
        public string type, prefab, label; public float[] boundsMin, boundsMax;
        public VLSocket[] sockets; public VLHeadlight headlight; public VLEmissive[] emissive; public bool brake; public string[] mounts;
    }

    [Serializable] public class VLFile { public int version; public string note; public VLVehicle[] vehicles; }

    /// <summary>
    /// View side only, the Sim is never touched. Auto-boots in every level scene (ZGame), next to NightLights.
    /// <list type="bullet">
    /// <item><b>Every vehicle</b> (light / medium mech / heavy / MLRS / Atom jeep / APC / crane, sockets measured on the meshes):
    /// headlight lenses + hot cores + halos, soft haze cones (NightBeam) and a projected light pool on the ground ahead
    /// (NightPool impostor); red tail lights, brighter while braking (a stop keeps them lit ~1 s); team LED beacons blinking
    /// on turrets / antennas / cab roofs; engine / exhaust glow pulsing with the throttle; the mech's cyan eyes, team chest
    /// beacons and back heat vents; the crane's amber rotating work beacon and red boom-tip light. All of it is ONE additive
    /// draw per mount node (OpenZ/VehicleGlow) whose channels this script drives every frame; lamps that are off cost no fill.</item>
    /// <item><b>Real headlights</b>: a spot light (no shadows) only for the few crewed vehicles nearest the camera focus
    /// (maxRealLights, 8), offered to the night package's NightLightBudget through NightBudgetShare, so they share its
    /// light cap, heat levels (74 / 77 C), frame-time and combat-FX scaling; the pool impostor fades out where the real
    /// light fades in. ground_lights_fix: the heat level only trims the shared budget (and drops the haze cones at level 2);
    /// it never switches the real headlights or their pools off any more.</item>
    /// <item><b>Emissive details</b>: the models' own glow parts (Tank / MLRS headlamps, owner lenses, the mech's beacons and
    /// rocket-pod glows) get their TeamLit _EmissionColor per renderer (the same per-material MaterialPropertyBlock TeamPaint
    /// uses; TeamPaint keeps the value): HDR, scaled up at night by DayNight (NightGrade.NightK), team coloured (a blue tank's
    /// owner lens glowed red before), dark when the vehicle is empty (neutral).</item>
    /// <item><b>State</b>: lights need a crew (neutral = empty vehicle = dark); boarding / capture = an engine-start flicker; a
    /// sniped driver = lights die; destroyed = a spark and out; badly damaged = one lamp dead or flickering (HeadlightFaults). Headlights switch on at
    /// dusk (t 0.34 + a per-vehicle delay) and off at dawn (0.895), like the street lamps.</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(80)]   // Update before NightLights (90) ticks the budget; LateUpdate after UnitView (60) placed the vehicles
    public class VehicleLights : MonoBehaviour
    {
        public static VehicleLights I { get; private set; }
        /// <summary>Set false (e.g. in ZGame.Awake) to add VehicleLights by hand.</summary>
        public static bool AutoBoot = true;

        [Header("Real headlights (night budget)")]
        [Tooltip("Real spot lights offered to the night budget: the crewed vehicles nearest the camera focus")]
        [Range(0, 8)] public int maxRealLights = 8;                        // ground_lights_fix: was 4 (the shared budget is 16-40)
        [Range(10f, 150f)] public float realLightRadius = 60f;
        [Range(0f, 3f)] public float lightGain = 1.0f;
        [Range(0f, 8f)] public float priority = 3f;
        // ground_lights_fix: the lens-mounted spot (0.8-2.0 m up, pitched 12 deg, 26) grazed the ground: URP falls off as 1/d^2 x N.L,
        // so it made a small hot spot 1-3 m ahead (0.8 m lenses) or almost nothing (jeep 2.0 m: 2.4x the moonlit sand at best, mech
        // eyes 1.0x). The real light is now a virtual source above the lens (the cone points forward, so the hull behind it is
        // never lit) aimed at the ground poolAim m ahead: a 5-6x pool from ~2 to 7 m ahead for every vehicle type.
        [Tooltip("Real headlight source at least this high above the vehicle root (m): the pool fills 2-7 m ahead instead of a grazing hot spot")]
        [Range(0.5f, 4f)] public float poolSourceHeight = 2f;
        [Tooltip("The real headlight's axis meets the ground this far ahead of the lens (m)")]
        [Range(3f, 16f)] public float poolAim = 8f;
        [Range(20f, 120f)] public float realSpotAngle = 80f;
        [Range(0.05f, 0.95f)] public float realInnerFraction = 0.3f;
        [Tooltip("x vehicle_lights.json intensity (26 -> 78)")]
        [Range(0.5f, 6f)] public float realIntensityScale = 3f;
        [Range(5f, 40f)] public float realRange = 24f;
        [Tooltip("Budget used when the night package is absent (or its budget hook is missing)")]
        [Range(4, 8)] public int fallbackMaxLights = 4;                    // NightLightBudget never grants fewer than 4

        [Header("Beams and pools (every lit vehicle near the camera)")]
        public bool beams = true, pools = true;
        [Range(0f, 3f)] public float beamGain = 0.45f, poolGain = 1.2f;   // ground_lights_fix: impostor pool was 0.5 (<= +50 % at its centre)
        [Range(20f, 250f)] public float effectRadius = 90f;
        [Tooltip("Haze cones and ground pools only for this many lit vehicles nearest the camera focus (fill cost)")]
        [Range(0, 32)] public int maxEffectVehicles = 8;

        [Header("Lamp glows: channel intensity at full night")]
        [Range(0f, 12f)] public float head = 4.5f, tail = 1.3f, brake = 4f, beacon = 4f, engine = 1.6f, eye = 3.2f, work = 6f;
        [Header("By day (share of the night value)")]
        [Range(0f, 1f)] public float dayBrake = 0.55f, dayBeacon = 0.3f, dayEngine = 0.06f, dayEye = 0.3f, dayWork = 0.4f;
        [Range(0f, 3f)] public float glowGain = 1f;

        [Header("Emissive details (TeamLit _EmissionColor)")]
        public bool emissiveDetails = true;
        [Tooltip("Owner lenses / glow strips take the owner's colour (off = keep the material's own emission colour)")]
        public bool teamTintOwnerGlow = true;
        [Range(0f, 8f)] public float ownerDay = 0.9f, ownerNight = 3.2f, headlampNight = 4f;

        [Header("Debug")]
        public bool debugKeys = true;

        public int RigCount => rigs.Count;
        public int RealLightsLit { get; private set; }
        public int MissingNodes { get; private set; }

        ZGame game; Camera cam; RtsCamera rts;
        readonly Dictionary<string, VLVehicle> specs = new Dictionary<string, VLVehicle>();
        readonly List<Rig> rigs = new List<Rig>();
        readonly Dictionary<UnitView, Rig> rigOf = new Dictionary<UnitView, Rig>();
        readonly HashSet<string> unknownTypes = new HashSet<string>();
        readonly NightLightBudget fallback = new NightLightBudget();
        readonly List<Rig> pick = new List<Rig>(64);
        // an IComparer instance (List.Sort(Comparison) allocates a comparer wrapper on every call under Mono, like NightLightBudget)
        sealed class DistAsc : IComparer<Rig> { public int Compare(Rig a, Rig b) => a.Dist2.CompareTo(b.Dist2); }
        static readonly DistAsc ByDist = new DistAsc();
        Material glowMat, beamMat, poolMat; Mesh poolMesh; MaterialPropertyBlock mpb;
        float nextScan, nextPick; bool warnedHook;

        static readonly int ChAId = Shader.PropertyToID("_ChA"), ChBId = Shader.PropertyToID("_ChB"), TeamId = Shader.PropertyToID("_Team"),
            EmissionId = Shader.PropertyToID("_EmissionColor");

        // ------------------------------------------------------------------ boot
        static bool hooked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Early() { if (!hooked) { hooked = true; SceneManager.sceneLoaded += (s, m) => TryAttach(); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => TryAttach();

        /// <summary>Level scenes only (a ZGame is present).</summary>
        public static void TryAttach()
        {
            if (!AutoBoot) return;
            var g = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            if (!g || FindFirstObjectByType<VehicleLights>()) return;
            var go = new GameObject("VehicleLights");
            go.AddComponent<VehicleLights>();
            go.AddComponent<NightProjectileGlow>();
        }

        void Awake()
        {
            if (I && I != this) { Destroy(this); return; }
            I = this;
            mpb = new MaterialPropertyBlock();
            game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            cam = Camera.main; rts = cam ? cam.GetComponent<RtsCamera>() : null; if (!rts) rts = FindFirstObjectByType<RtsCamera>();
            var ta = Resources.Load<TextAsset>("OpenZ/VehicleLights/vehicle_lights");
            var file = ta ? JsonUtility.FromJson<VLFile>(ta.text) : null;
            if (file?.vehicles != null)
            {
                foreach (var v in file.vehicles) if (v != null && !string.IsNullOrEmpty(v.type)) specs[v.type] = v;
            }
            else Debug.LogWarning("[OpenZ] VehicleLights: Resources/OpenZ/VehicleLights/vehicle_lights.json missing (README_IMPORT.md step 2)");
            glowMat = VehicleLightMeshes.GlowMaterial("VehicleGlow (vehicles)");
            beamMat = NightMeshes.Beam(); poolMat = NightMeshes.Pool(); poolMesh = NightMeshes.Cube();   // the night package's look
            if (!game) { Debug.LogWarning("[OpenZ] VehicleLights: no ZGame"); enabled = false; return; }
        }

        void Start()
        {
            Scan();
            int lamps = 0; foreach (var r in rigs) lamps += r.Spec.sockets?.Length ?? 0;
            Debug.Log($"[OpenZ] VehicleLights {game.level}: {rigs.Count} vehicles rigged ({lamps} lamps, {specs.Count} vehicle types), " +
                      $"real headlights <= {maxRealLights} via {(NightLights.I ? "the night budget" : "the fallback budget")}" +
                      (MissingNodes > 0 ? $", {MissingNodes} sockets on the root (mount node missing: re-run vl_sockets.py)" : ""));
        }

        void OnDestroy()
        {
            foreach (var r in rigs) if (r.Fix != null) NightBudgetShare.Fixtures.Remove(r.Fix);
            if (I == this) I = null;
            if (glowMat) Destroy(glowMat);
        }

        // ------------------------------------------------------------------ rigs
        sealed class Emis { public Renderer R; public int Index; public string Role; public Color Base; public float Shown = -1f;
                            public bool FaultSide; }                                                 // lights_tail_v1 review fix 3: the headlamp glass of the lamp that can fail

        sealed class Rig
        {
            public UnitView View; public ZObject Obj; public VLVehicle Spec; public Transform Root;
            // drawn with Graphics.DrawMesh at the mount's matrix: no renderer inside the vehicle hierarchy, so TeamPaint,
            // BoardingView's LocalBox, PopOut, OpenZWading and every other renderer scan of a unit never see the lights
            public readonly List<Transform> GlowMounts = new List<Transform>(); public readonly List<Mesh> GlowMeshes = new List<Mesh>();
            public Mesh BeamMesh; public Matrix4x4 PoolLocal; public bool HasPool;
            public MaterialPropertyBlock GlowBlock, BeamBlock, PoolBlock; public bool BeamOn, PoolOn;
            public Light Spot; public NightFixture Fix;
            public readonly List<Emis> Emissive = new List<Emis>();
            public Transform HeadNode; public Vector3 HeadLocal; public Color HeadLin = new Color(1f, 0.84f, 0.6f);
            public Transform WorkNode; public Vector3 WorkLocal; public bool HasWork;
            public float Power, Head, BrakeK, SpeedS, Accel, BrakeHold, Throttle, StartAt = -10f, Seed, Dist2, HeadNorm, P, Night, EffK;
            public float Offer;                  // 0..1: how much of the headlight is offered to the budget (fades with Candidate)
            public bool WasCrewed, WasMoving, Dead, Candidate, Effects, Crewed; public Team Owner;
            public Vector4 ChA, ChB, ShownA = new Vector4(-1, 0, 0, 0), ShownB; public Color ShownTeam = Color.clear; public bool GlowOn;
            public float BeamShown = -1f, PoolShown = -1f;
            // vehicles_nb_v1 NB10: one headlight of a damaged vehicle can fail (HeadlightFaults): its own glow mesh + haze cone
            public HeadlightFaults.Plan Fault; public Transform FaultMount; public Vector3 FaultLocal; public Mesh FaultMesh, FaultBeam;
            public MaterialPropertyBlock FaultBlock, FaultBeamBlock; public float FaultK = 1f, FaultBeamShown = -1f; public bool FaultDead;
            public Vector4 FaultShownA = new Vector4(-1, 0, 0, 0), FaultShownB;
            // lights_tail_v1: the per-vehicle look (HeadlightVariety) and the fault extras (flicker burst sound, the ground light follows)
            public HeadlightVariety.Look Var = HeadlightVariety.Plain; public int LastBurst = int.MinValue, Bursts, FlickSounds; public bool FaultSplit;
            public bool FaultEye;                                                                    // review fix: the fault lamp is a mech eye (it glows by day too: dayEye)
            public Vector3 SpotBase, SpotShift, PoolShift;
            public float FaultGlass, KeepGlass; public int FaultGlassN;                                // review fix 3: the model's headlamp glass (key) on the fault / the other side
            public Vector3 HeadWorld => HeadNode ? HeadNode.TransformPoint(HeadLocal) : Root ? Root.position + Vector3.up : Vector3.zero;
        }

        void Scan()
        {
            if (!game) return;
            for (int i = rigs.Count - 1; i >= 0; i--)
                if (!rigs[i].View)
                {
                    if (rigs[i].Fix != null) NightBudgetShare.Fixtures.Remove(rigs[i].Fix);
                    rigOf.Remove(rigs[i].View); rigs.RemoveAt(i);
                }
            foreach (var kv in game.Views)
            {
                var v = kv.Value;
                if (!v || v.Obj == null || v.Obj.Kind != ZKind.Vehicle || rigOf.ContainsKey(v)) continue;
                if (!specs.TryGetValue(v.Obj.Type, out var spec))
                {
                    if (unknownTypes.Add(v.Obj.Type)) Debug.LogWarning($"[OpenZ] VehicleLights: no sockets for vehicle type '{v.Obj.Type}' (vehicle_lights.json)");
                    rigOf[v] = null; continue;
                }
                var r = Build(v, spec);
                rigOf[v] = r; if (r != null) rigs.Add(r);
            }
        }

        static Vector3 V3(float[] a, Vector3 fallback) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : fallback;

        Rig Build(UnitView view, VLVehicle spec)
        {
            var root = view.transform;
            var r = new Rig { View = view, Obj = view.Obj, Spec = spec, Root = root, Owner = view.Obj.Owner, Seed = Hash(view.Obj.Id * 7919 + 13),
                              GlowBlock = new MaterialPropertyBlock(), BeamBlock = new MaterialPropertyBlock(), PoolBlock = new MaterialPropertyBlock() };
            r.WasCrewed = r.Crewed = view.Obj.Owner != Team.Neutral && !view.Obj.Destroyed;       // crewed at load: lit at once, no start flicker
            r.Power = r.WasCrewed ? 1f : 0f;
            var nodes = new Dictionary<string, Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (!nodes.ContainsKey(t.name)) nodes[t.name] = t;

            // sockets grouped by mount node; a missing node puts the socket on the root with its root-frame coordinates
            var groups = new Dictionary<Transform, List<VehicleLightMeshes.Elem>>();
            var headPts = new List<Vector3>(); var headNrm = Vector3.zero;
            var keepPts = new List<Vector3>(); var faultPts = new List<Vector3>(); List<VehicleLightMeshes.Elem> faultElems = null;   // vehicles_nb_v1 NB10
            r.Fault = HeadlightFaults.Enabled ? HeadlightFaults.For(view.Obj.Id) : default;
            r.Var = HeadlightVariety.For(view.Obj.Id);                                             // lights_tail_v1: subtle per-vehicle look
            var headMounts = new HashSet<Transform>();                                             // lights_tail_v1: mounts whose glow mesh carries the look
            string faultSurface = null;                                                            // review fix 3: the renderer the fault lamp's socket sits on (its glass)
            if (spec.sockets != null)
                foreach (var s in spec.sockets)
                {
                    if (s == null) continue;
                    Transform m = root; bool nodeFrame = false;
                    if (!string.IsNullOrEmpty(s.node))
                    {
                        if (nodes.TryGetValue(s.node, out var t)) { m = t; nodeFrame = true; }
                        else MissingNodes++;
                    }
                    var pos = nodeFrame ? V3(s.pos, Vector3.zero) : V3(s.root, Vector3.zero);
                    var nrm = nodeFrame ? V3(s.nrm, Vector3.forward) : V3(s.rootNrm, Vector3.forward);
                    float unit = nodeFrame ? 1f / Mathf.Max(0.05f, s.nodeScale) : 1f;
                    if (!groups.TryGetValue(m, out var list)) groups[m] = list = new List<VehicleLightMeshes.Elem>();
                    bool faultLamp = r.Fault.mode != HeadlightFaults.Mode.None && (s.kind == "head" || s.kind == "eye") && Mathf.Sign(V3(s.root, Vector3.zero).x) == r.Fault.side;   // vehicles_nb_v1 NB10
                    var sv = HeadlightVariety.Enabled && (s.kind == "head" || s.kind == "eye") ? Varied(s, r.Var) : s;   // lights_tail_v1: glow size / colour temperature
                    if (sv != s && !faultLamp) headMounts.Add(m);
                    if (faultLamp && s.kind == "eye") r.FaultEye = true;                                   // review fix: a mech eye glows by day (dayEye), so its fault sounds by day
                    if (faultLamp && !string.IsNullOrEmpty(s.surface)) faultSurface = s.surface;           // review fix 3: e.g. head_L on Tank_headlamp (vl_sockets 'lamp' probe)
                    if (faultLamp) { r.FaultMount = m; r.FaultLocal = pos; faultElems ??= new List<VehicleLightMeshes.Elem>(); VehicleLightMeshes.AddSocket(faultElems, sv, pos, nrm, unit); }
                    else VehicleLightMeshes.AddSocket(list, sv, pos, nrm, unit);
                    if (s.kind == "head" || s.kind == "eye")
                    {
                        if (!r.HeadNode) r.HeadNode = m;
                        if (m == r.HeadNode) { headPts.Add(pos); headNrm += nrm; if (faultLamp) faultPts.Add(pos); else keepPts.Add(pos); }   // vehicles_nb_v1 NB10
                    }
                    if (s.kind == "work") { r.WorkNode = m; r.WorkLocal = pos; r.HasWork = true; }
                }
            foreach (var kv in groups)
            {
                string key = $"{spec.type}|{kv.Key.name}|{(kv.Key == root ? "root" : "node")}" + (faultElems != null && kv.Key == r.FaultMount ? "|nf" + r.Fault.side : "")   // vehicles_nb_v1 NB10: shared meshes per lamp set
                             + (headMounts.Contains(kv.Key) ? "|" + r.Var.Key : "");                                                   // lights_tail_v1: + the look step
                var mesh = VehicleLightMeshes.GlowMesh(key, kv.Value);
                r.GlowMounts.Add(kv.Key); r.GlowMeshes.Add(mesh);
            }
            if (faultElems != null && r.FaultMount)
            {   // vehicles_nb_v1 NB10: the lamp that can fail, drawn on its own (its head / eye channels scaled by FaultK)
                r.FaultMesh = VehicleLightMeshes.GlowMesh($"{spec.type}|{r.FaultMount.name}|{(r.FaultMount == root ? "root" : "node")}|f{r.Fault.side}|{r.Var.Key}", faultElems);   // lights_tail_v1: + the look step
                r.FaultBlock = new MaterialPropertyBlock();
            }

            // headlight: real spot (budget), haze cones, ground pool
            var hl = spec.headlight;
            if (hl != null && headPts.Count > 0 && r.HeadNode)
            {
                var c = Vector3.zero; foreach (var p in headPts) c += p; c /= headPts.Count;
                r.HeadLocal = c;
                if (hl.rgb != null && hl.rgb.Length >= 3) r.HeadLin = new Color(hl.rgb[0], hl.rgb[1], hl.rgb[2]);
                r.HeadLin = new Color(r.HeadLin.r * r.Var.tr, r.HeadLin.g * r.Var.tg, r.HeadLin.b * r.Var.tb);                 // lights_tail_v1: a bit warmer / cooler white (spot, cone, pool)
                var fwd = headNrm.sqrMagnitude > 1e-4f ? headNrm.normalized : Vector3.back;
                var flat = new Vector3(fwd.x, 0f, fwd.z); flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.back;
                float nodeUnit = r.HeadNode == root ? 1f : 1f / Mathf.Max(0.05f, NodeScale(spec, r.HeadNode.name));
                var spotGo = new GameObject("VL_Headlight"); spotGo.transform.SetParent(r.HeadNode, false);
                // ground_lights_fix: a virtual source above the lens (at least poolSourceHeight over the vehicle root, which sits on
                // the ground), aimed so its axis meets the ground poolAim m ahead; still parented to the mount node, so it follows the
                // suspension / waist yaw. Worked out in the root frame (+y up, front = -z) and mapped root -> node, so a spawn pop /
                // death squash scale on the root at build time cancels out. vehicle_lights.json keeps the lens sockets, pitch and
                // intensity for the glows, cones and the impostor pool.
                var rootFromNode = root.worldToLocalMatrix * r.HeadNode.localToWorldMatrix;
                var nodeFromRoot = rootFromNode.inverse;
                var lensR = rootFromNode.MultiplyPoint3x4(c);
                var fwdR = rootFromNode.MultiplyVector(flat); fwdR.y = 0f;
                fwdR = fwdR.sqrMagnitude > 1e-6f ? fwdR.normalized : Vector3.back;
                float srcH = Mathf.Max(lensR.y, poolSourceHeight);
                float pr = Mathf.Atan2(srcH, poolAim);
                var srcR = new Vector3(lensR.x, srcH, lensR.z) + fwdR * 0.12f;
                var dirR = fwdR * Mathf.Cos(pr) + Vector3.down * Mathf.Sin(pr);
                spotGo.transform.localPosition = nodeFromRoot.MultiplyPoint3x4(srcR);
                spotGo.transform.localRotation = Quaternion.LookRotation(nodeFromRoot.MultiplyVector(dirR), nodeFromRoot.MultiplyVector(Vector3.up));
                var l = spotGo.AddComponent<Light>();
                l.type = LightType.Spot; l.spotAngle = Mathf.Clamp(realSpotAngle, 10f, 170f); l.innerSpotAngle = l.spotAngle * realInnerFraction;
                l.range = Mathf.Max(1f, realRange * r.Var.reach); l.color = r.HeadLin.gamma; l.intensity = 0f; l.enabled = false;   // lights_tail_v1: reach x look
                l.shadows = LightShadows.None; l.renderMode = LightRenderMode.ForcePixel;
                r.Spot = l; r.SpotBase = spotGo.transform.localPosition;                                  // lights_tail_v1: SpotBase (the fault shift)
                r.Fix = new NightFixture
                {
                    Kind = NightKind.Flood, Id = $"vehicle_{view.Obj.Id}", Light = l, Col = l.color, ColLinear = r.HeadLin,
                    Intensity = hl.intensity * realIntensityScale * r.Var.power, Range = l.range, Angle = l.spotAngle, Priority = priority, ShadowCandidate = false,   // lights_tail_v1: x power
                };
                NightBudgetShare.Fixtures.Add(r.Fix);

                bool splitBeam = faultPts.Count > 0 && keepPts.Count > 0;                                   // vehicles_nb_v1 NB10: one cone per lamp set
                string rk = "|r" + r.Var.reachStep; float beamUnit = nodeUnit * r.Var.reach;                  // lights_tail_v1: cone reach x look (5 steps, shared meshes)
                var beamMesh = VehicleLightMeshes.BeamMesh($"{spec.type}|{r.HeadNode.name}" + (splitBeam ? "|nf" + r.Fault.side : "") + rk, splitBeam ? keepPts : headPts, fwd, hl, beamUnit);
                if (splitBeam) { r.FaultBeam = VehicleLightMeshes.BeamMesh($"{spec.type}|{r.HeadNode.name}|f{r.Fault.side}" + rk, faultPts, fwd, hl, beamUnit); r.FaultBeamBlock = new MaterialPropertyBlock(); }
                if (splitBeam)
                {   // lights_tail_v1: with one lamp out the light comes from the other one: the real spot and the pool move towards it
                    var kc = Vector3.zero; foreach (var kp in keepPts) kc += kp; kc /= keepPts.Count;
                    r.FaultSplit = true; r.SpotShift = kc - c;
                    var sh = rootFromNode.MultiplyVector(kc - c); sh.y = 0f; r.PoolShift = sh;
                }
                r.BeamMesh = beamMesh;
                if (hl.pool != null && hl.pool.width > 0.1f)
                {   // the pool volume in the root frame, from just ahead of the bumper (the ellipse edge) to ~8.5 m ahead
                    float pl = hl.pool.length * r.Var.reach, pz = hl.pool.z - 0.5f * (hl.pool.length - pl) * Mathf.Sign(hl.pool.z);   // lights_tail_v1: reach x look, the near edge stays
                    r.PoolLocal = Matrix4x4.TRS(new Vector3(0f, 0.5f, pz), Quaternion.identity,
                                                new Vector3(hl.pool.width * r.Var.size, Mathf.Max(1f, hl.pool.height), pl));
                    r.HasPool = true;
                }
            }

            // the model's own emissive parts
            if (spec.emissive != null)
                foreach (var e in spec.emissive)
                {
                    if (e == null || !nodes.TryGetValue(e.renderer, out var t) || !t.TryGetComponent<Renderer>(out var rr)) continue;
                    var mats = rr.sharedMaterials;
                    // lights_tail_v1 review fix 3: the glass on the fault lamp's side = the renderer its socket sits on (s.surface), else by side
                    bool fside = e.role == "head" && faultElems != null;
                    if (fside && faultSurface != null) fside = e.renderer == faultSurface;
                    else if (fside) { float gx = root.InverseTransformPoint(rr.bounds.center).x; fside = Mathf.Abs(gx) > 0.05f && Mathf.Sign(gx) == r.Fault.side; }
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] && mats[i].HasProperty(EmissionId))
                        {
                            r.Emissive.Add(new Emis { R = rr, Index = i, Role = e.role, Base = mats[i].GetColor(EmissionId), FaultSide = fside });
                            if (fside) r.FaultGlassN++;
                        }
                }
            return r;
        }

        static float NodeScale(VLVehicle spec, string node)
        {
            if (spec.sockets != null) foreach (var s in spec.sockets) if (s != null && s.node == node) return s.nodeScale;
            return 1f;
        }

        static float Hash(int n) { unchecked { uint x = (uint)n * 747796405u + 2891336453u; x = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u; return ((x >> 22) ^ x) / 4294967295f; } }

        /// <summary>Glow colour of a team (linear HDR): the warm Z red / royal blue, lifted so a small LED reads.</summary>
        public static Color TeamGlow(Team t) => (t = TeamColors.Shown(t)) == Team.Red ? new Color(1f, 0.2f, 0.11f) : t == Team.Blue ? new Color(0.26f, 0.48f, 1f) : new Color(0.55f, 0.53f, 0.5f);

        // ------------------------------------------------------------------ frame
        int HeatLevel => NightLights.I && NightLights.I.isActiveAndEnabled ? NightLights.I.Budget.HeatLevel : fallback.HeatLevel;

        void Update()
        {
            if (!game) return;
            float now = Time.time, dt = Time.deltaTime;
            if (now >= nextScan) { nextScan = now + 0.5f; Scan(); }
            float t = NightGrade.TimeOfDay, night = NightGrade.NightK;
            for (int i = 0; i < rigs.Count; i++) Step(rigs[i], t, night, now, dt);
            if (now >= nextPick) { nextPick = now + 0.2f; PickRealLights(); }
            for (int i = 0; i < rigs.Count; i++)
            {
                var r = rigs[i]; if (r.Fix == null) continue;
                r.Fix.Priority = priority;
                // the nearest-N set lost its real light in one frame while the pool came back over 0.35 s: a pop on every re-rank)
                r.Offer = Mathf.MoveTowards(r.Offer, r.Candidate ? 1f : 0f, dt / 0.35f);
                r.Fix.LightOut = r.HeadNorm * r.Offer;
            }
            if (debugKeys && !global::OpenZ.OzRelease.IsRelease) DebugKeys();
        }

        void Step(Rig r, float t, float night, float now, float dt)
        {
            var o = r.Obj;
            bool alive = r.View && r.View.isActiveAndEnabled && o != null && !o.Destroyed && !r.View.Dying;
            var shownOwner = CaptureHold.Shown(o);                                                     // boarding_nb_v1 NB17: dark until the robot sits inside
            bool crewed = alive && shownOwner != Team.Neutral;
            if (!alive && !r.Dead)
            {
                r.Dead = true;
                if (r.Power > 0.3f && r.View) FxManager.I?.Pop(r.HeadWorld, 0.45f);                     // lights die with a spark
                r.Power = 0f;
            }
            if (alive) r.Dead = false;
            if (crewed && (!r.WasCrewed || shownOwner != r.Owner)) r.StartAt = now;                    // boarding / capture: engine start
            if (o != null) r.Owner = shownOwner;
            r.WasCrewed = crewed; r.Crewed = crewed;
            float target = crewed ? 1f : 0f;
            r.Power = Mathf.MoveTowards(r.Power, target, dt / (target > r.Power ? 0.2f : 0.4f));
            float p = r.Power;
            if (now - r.StartAt < 0.8f) p *= Mathf.PerlinNoise(r.Seed * 13f, now * 24f) > 0.42f ? 1f : 0.12f;
            // lights_tail_v1 review fix: the old whole-vehicle flicker below 40 % hp is retired (ShowDamaged: both lamps, tails and beacons
            // dipped together, silent, and it blurred the one-flickering-light look): HeadlightFaults gives every vehicle below ~40 %
            // ONE dead or flickering lamp, each burst with its ngt_lamp_flicker crackle. Was: ShowDamaged && Perlin(7, 3 t) > 0.78: p x 0.1 / 1
            r.P = p; r.Night = night;
            if (r.FaultMesh || r.FaultBeam)
            {   // vehicles_nb_v1 NB10: the faulty lamp (health below its threshold): dead (a spark as it goes) or a loose contact
                float hp = alive && o.MaxHealth > 0 ? (float)o.Health / o.MaxHealth : 1f;
                bool dead = alive && HeadlightFaults.IsDead(r.Fault, hp);
                // lights_tail_v1 review fix: the sound follows the lamp's glow. A head lamp glows only with the headlights on (r.Head); a
                // mech eye glows by day too (ChB.z = eye x p x Lerp(dayEye 0.3, 1, night), no r.Head gate), so its flicker bursts and its
                // death are visible by day and sound by day as well
                bool lampLit = r.P > 0.3f && (r.Head > 0.5f || r.FaultEye);
                if (dead && !r.FaultDead && lampLit) FaultPop(r);
                r.FaultDead = dead;
                float ft = now + r.Seed * 13f;
                r.FaultK = (alive ? HeadlightFaults.Level(r.Fault, hp, ft) : 1f) * r.Var.weak;          // lights_tail_v1: x the weaker lamp
                if (alive && !dead && HeadlightFaults.Burst(r.Fault, hp, ft, out int burst) && burst != r.LastBurst)
                {   // lights_tail_v1: every flicker burst of a lit lamp crackles (ngt_lamp_flicker), near the camera
                    r.LastBurst = burst; r.Bursts++;
                    if (lampLit) FlickerSound(r, burst);
                }
            }

            // motion: braking = slowing down, and a moment after every stop
            float speed = alive && o.IsMoving ? Mathf.Sqrt(o.Dx * o.Dx + o.Dy * o.Dy) / 8f : 0f;
            float prev = r.SpeedS; r.SpeedS = Mathf.Lerp(r.SpeedS, speed, 1f - Mathf.Exp(-8f * dt));
            float a = dt > 1e-5f ? (r.SpeedS - prev) / dt : 0f;
            r.Accel = Mathf.Lerp(r.Accel, a, 1f - Mathf.Exp(-10f * dt));
            bool moving = speed > 0.05f;
            if (r.WasMoving && !moving) r.BrakeHold = now + 1.1f;
            if (r.Accel < -0.6f) r.BrakeHold = Mathf.Max(r.BrakeHold, now + 0.3f);
            r.WasMoving = moving;
            bool braking = now < r.BrakeHold;
            r.BrakeK = Mathf.MoveTowards(r.BrakeK, braking ? 1f : 0f, dt / (braking ? 0.06f : 0.25f));
            r.Throttle = Mathf.Clamp01(Mathf.Max(0f, r.Accel) / 1.2f) * 0.6f + Mathf.Clamp01(r.SpeedS / 3f) * 0.4f;

            // headlights switch on at dusk like the lamps (a per-vehicle delay), off at dawn
            bool on = NightGrade.LightsOn(t, 0.34f + 0.012f * r.Seed, 0.895f + 0.008f * r.Seed);
            r.Head = Mathf.MoveTowards(r.Head, on ? 1f : 0f, dt / 0.35f);
            r.HeadNorm = p * r.Head * (moving ? 1f : 0.8f);

            float ph = now / 1.3f + r.Seed;
            float blinkA = Mathf.Repeat(ph, 1f) < 0.14f ? 1f : 0.04f, blinkB = Mathf.Repeat(ph + 0.5f, 1f) < 0.14f ? 1f : 0.04f;
            float g = glowGain;
            r.ChA = new Vector4(head * r.HeadNorm, tail * p * r.Head, brake * p * r.BrakeK * Mathf.Lerp(dayBrake, 1f, night),
                                beacon * p * blinkA * Mathf.Lerp(dayBeacon, 1f, night)) * g;
            float flick = 0.85f + 0.3f * Mathf.PerlinNoise(r.Seed * 5f, now * 11f);
            r.ChB = new Vector4(beacon * p * blinkB * Mathf.Lerp(dayBeacon, 1f, night),
                                engine * p * (0.35f + 0.65f * r.Throttle) * flick * Mathf.Lerp(dayEngine, 1f, night),
                                eye * p * Mathf.Lerp(dayEye, 1f, night),
                                r.HasWork && (moving || night > 0.2f) ? work * p * Mathf.Lerp(dayWork, 1f, night) : 0f) * g;
            r.ChA.x *= r.Var.power; r.ChB.z *= r.Var.power;                                           // lights_tail_v1: lamp glow power x look
        }

        void PickRealLights()
        {
            // real headlights off. In the editor HeatLevel follows GpuGuard's nvidia-smi reading (NightLightsMenu), a full-look Play
            // run sits at 77-89 C (heat level 2) and the level only drops back below 70 C, so max was 0 for whole runs: no real
            // light, no pool (Apply), no cone, only the lens glow. NightLightBudget already trims the shared total by heat and frame
            // time (never below 16 lights) and sheds the shadows first; headlights are unshadowed Forward+ lights.
            int max = maxRealLights;
            var f = rts ? rts.focus : cam ? cam.transform.position : Vector3.zero;
            pick.Clear();
            float R2 = realLightRadius * realLightRadius, E2 = effectRadius * effectRadius, M2 = Mathf.Max(R2, E2);
            foreach (var r in rigs)
            {
                r.Candidate = false; r.Effects = false;
                if (r.Fix == null || !r.Crewed || r.HeadNorm < 0.05f || !r.Root) continue;
                var p = r.Root.position; r.Dist2 = (p.x - f.x) * (p.x - f.x) + (p.z - f.z) * (p.z - f.z);
                if (r.Dist2 <= M2) pick.Add(r);
            }
            pick.Sort(ByDist);
            for (int i = 0; i < pick.Count; i++)
            {   // nearest first: real spots for the first few, haze cones + pools for the first maxEffectVehicles
                var r = pick[i];
                r.Candidate = i < max && r.Dist2 <= R2;
                r.Effects = i < maxEffectVehicles && r.Dist2 <= E2;
            }
        }

        void LateUpdate()
        {
            if (!game) return;
            float now = Time.time, dt = Time.deltaTime;
            // the night budget ranks the headlights (NightLights, order 90, merged them this frame); else our own small budget
            bool nightLive = NightLights.I && NightLights.I.isActiveAndEnabled;
            bool hookOk = NightBudgetShare.HookFrame >= Time.frameCount - 2;
            if (!nightLive || !hookOk)
            {
                if (nightLive && !hookOk && !warnedHook && Time.frameCount > 10)
                {
                    warnedHook = true;
                    Debug.LogWarning("[OpenZ] VehicleLights: NightLightBudget has no NightBudgetShare hook (night package re-imported after vehicle_lights?): " +
                                     "headlights use their own small budget. Import the vehicle lights again.");
                }
                fallback.MaxLights = fallbackMaxLights; fallback.MaxShadowed = 0;
                fallback.Tick(NightBudgetShare.Fixtures, cam, rts ? rts.focus : Vector3.zero, now, dt);
            }
            var camPos = cam ? cam.transform.position : Vector3.zero;
            var focus = rts ? rts.focus : camPos;
            int heat = HeatLevel, lit = 0;
            foreach (var r in rigs)
            {
                r.EffK = Mathf.MoveTowards(r.EffK, r.Effects ? 1f : 0f, dt / 0.4f);                   // cones / pools fade in and out
                Apply(r, now, camPos, heat); if (r.Spot && r.Spot.enabled) lit++;
            }
            RealLightsLit = lit;
        }

        void Apply(Rig r, float now, Vector3 camPos, int heat)
        {
            if (!r.View) return;
            if (ZodBriefing.EndingHides(r.View)) { if (r.Spot && r.Spot.enabled) r.Spot.enabled = false; return; }   // version 1.1: not in the level 16 ending's picture (ZodBriefing.EndingEnemies.cs): glows, beams, pools, the real light
            // crane work beacon: a rotating reflector flashes when it points at the camera
            var chB = r.ChB;
            if (r.HasWork && chB.w > 0f && r.WorkNode)
            {
                var wp = r.WorkNode.TransformPoint(r.WorkLocal);
                var toCam = camPos - wp; toCam.y = 0f;
                float ang = now * Mathf.PI * 2f / 0.9f + r.Seed * 6.283f;
                float d = toCam.sqrMagnitude > 1e-4f ? Vector3.Dot(new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)), toCam.normalized) : 0f;
                chB.w *= 0.2f + 0.8f * Mathf.Pow(Mathf.Abs(d), 12f);
            }
            var team = TeamGlow(r.Owner);
            bool any = Mathf.Max(Mathf.Max(r.ChA.x, r.ChA.y), Mathf.Max(r.ChA.z, r.ChA.w)) > 0.002f ||
                       Mathf.Max(Mathf.Max(chB.x, chB.y), Mathf.Max(chB.z, chB.w)) > 0.002f;
            r.GlowOn = any;
            if (any)
            {
                if (Changed(r.ChA, r.ShownA) || Changed(chB, r.ShownB) || team != r.ShownTeam)
                {
                    r.ShownA = r.ChA; r.ShownB = chB; r.ShownTeam = team;
                    r.GlowBlock.SetVector(ChAId, r.ChA); r.GlowBlock.SetVector(ChBId, chB); r.GlowBlock.SetColor(TeamId, team);
                }
                for (int i = 0; i < r.GlowMounts.Count; i++)
                    if (r.GlowMounts[i]) Draw(r.GlowMeshes[i], r.GlowMounts[i].localToWorldMatrix, glowMat, r.GlowBlock);
                if (r.FaultMesh && r.FaultMount) DrawFault(r, chB, team);                                // vehicles_nb_v1 NB10
            }

            // haze cones and ground pool (the maxEffectVehicles lit vehicles nearest the camera focus)
            float dusk = Mathf.Lerp(0.25f, 1f, r.Night);
            r.BeamOn = false; r.PoolOn = false;
            if (r.BeamMesh && r.HeadNode)
            {
                float bk = beams && heat < 2 ? beamGain * r.EffK * r.HeadNorm * Mathf.Pow(r.Night, 0.8f) * r.Var.power : 0f;   // ground_lights_fix: was heat < 1; lights_tail_v1: cone x power
                if (SetColor(r.BeamBlock, r.HeadLin * bk, ref r.BeamShown)) { r.BeamOn = true; Draw(r.BeamMesh, r.HeadNode.localToWorldMatrix, beamMat, r.BeamBlock); }
                if (r.FaultBeam && SetColor(r.FaultBeamBlock, r.HeadLin * (bk * r.FaultK), ref r.FaultBeamShown)) Draw(r.FaultBeam, r.HeadNode.localToWorldMatrix, beamMat, r.FaultBeamBlock);   // vehicles_nb_v1 NB10
            }
            if (r.HasPool)
            {
                float realK = r.Fix != null ? r.Fix.RealK * r.Offer : 0f;              // the real light's share: the pool cross-fades with it
                float pk = pools ? poolGain * r.EffK * r.HeadNorm * dusk * (1f - realK) * LampShare(r) * r.Var.power : 0f;   // vehicles_nb_v1 NB10: x lamps lit;   // ground_lights_fix: no heat gate (was heat < 2)
                if (SetColor(r.PoolBlock, r.HeadLin * pk, ref r.PoolShown)) { r.PoolOn = true; Draw(poolMesh, r.Root.localToWorldMatrix * PoolMatrix(r), poolMat, r.PoolBlock); }   // lights_tail_v1: the pool follows a failing lamp
            }
            if (r.Spot)
            {
                float k = r.Fix != null ? r.Fix.LightOut * r.Fix.RealK : 0f;
                bool on = k > 0.002f && r.Spot.gameObject.activeInHierarchy;
                if (r.Spot.enabled != on) r.Spot.enabled = on;
                if (on) r.Spot.intensity = r.Fix.Intensity * k * lightGain * LampShare(r);                // vehicles_nb_v1 NB10: one lamp out = half the light
                if (r.FaultSplit)
                {   // lights_tail_v1: the light comes from the lamp that still burns (the spot slides towards it as the other one dies / flickers)
                    var lp = r.SpotBase + r.SpotShift * (1f - LampLevel(r));
                    if ((r.Spot.transform.localPosition - lp).sqrMagnitude > 1e-6f) r.Spot.transform.localPosition = lp;
                }
            }
            if (emissiveDetails && r.Emissive.Count > 0) ApplyEmissive(r, now, team);
        }

        // ------------------------------------------------------------------ vehicles_nb_v1 NB10: a failing headlight
        /// <summary>Share of the headlight still lit: 1 = both lamps, 0.5 = one lamp out.</summary>
        static float LampShare(Rig r) => r.FaultMesh || r.FaultBeam ? 0.5f + 0.5f * r.FaultK : 1f;

        /// <summary>The faulty lamp's glow: only its head / eye channels, scaled by FaultK (0 dead, flicker, 1 fine).</summary>
        void DrawFault(Rig r, Vector4 chB, Color team)
        {
            var a = new Vector4(r.ChA.x * r.FaultK, 0f, 0f, 0f); var b = new Vector4(0f, 0f, chB.z * r.FaultK, 0f);
            if (a.x + b.z <= 0.002f) return;
            if (Changed(a, r.FaultShownA) || Changed(b, r.FaultShownB)) { r.FaultShownA = a; r.FaultShownB = b; r.FaultBlock.SetVector(ChAId, a); r.FaultBlock.SetVector(ChBId, b); r.FaultBlock.SetColor(TeamId, team); }
            Draw(r.FaultMesh, r.FaultMount.localToWorldMatrix, glowMat, r.FaultBlock);
        }

        // ------------------------------------------------------------------ lights_tail_v1: the per-vehicle look + faults that read
        /// <summary>lights_tail_v1: a head / eye socket with this vehicle's look (glow size, colour temperature); a copy, the JSON spec stays.</summary>
        static VLSocket Varied(VLSocket s, HeadlightVariety.Look v)
        {
            var rgb = s.rgb != null && s.rgb.Length >= 3 ? new[] { s.rgb[0] * v.tr, s.rgb[1] * v.tg, s.rgb[2] * v.tb } : new[] { v.tr, v.tg, v.tb };
            return new VLSocket
            {
                id = s.id, kind = s.kind, node = s.node, colour = s.colour, surface = s.surface, pos = s.pos, nrm = s.nrm, root = s.root, rootNrm = s.rootNrm,
                rgb = rgb, nodeScale = s.nodeScale, size = s.size * v.size, halo = s.halo * v.size, directional = s.directional,
            };
        }

        /// <summary>lights_tail_v1: light level of the lamp that can fail (0 out .. 1 burning, x the weaker lamp).</summary>
        static float LampLevel(Rig r) => Mathf.Clamp01(r.FaultK);

        /// <summary>lights_tail_v1: the impostor pool of a vehicle whose one lamp is out / flickering: narrower and shifted towards the
        /// lamp that still burns (the real spot slides the same way in Apply).</summary>
        static Matrix4x4 PoolMatrix(Rig r)
        {
            if (!r.FaultSplit) return r.PoolLocal;
            float off = 1f - LampLevel(r);
            if (off < 0.01f) return r.PoolLocal;
            return Matrix4x4.Translate(r.PoolShift * off) * r.PoolLocal * Matrix4x4.Scale(new Vector3(1f - 0.3f * off, 1f, 1f));
        }

        const float FlickerSoundRadius = 45f;
        float nextFlickSound;
        /// <summary>lights_tail_v1: a flicker burst of a lit lamp near the camera focus crackles (ngt_lamp_flicker; one emitter slot per
        /// burst so the event's 6 s cooldown never mutes the next burst of the same lamp; at most one crackle per 0.35 s overall). Each
        /// crackle logs "VL ngt_lamp_flicker BURST #id n" right before its AD line (the street / fort lamps use the same key).</summary>
        void FlickerSound(Rig r, int burst)
        {
            if (!r.FaultMount || r.Obj == null) return;
            var p = r.FaultMount.TransformPoint(r.FaultLocal);
            var f = rts ? rts.focus : cam ? cam.transform.position : p;
            float dx = p.x - f.x, dz = p.z - f.z;
            if (dx * dx + dz * dz > FlickerSoundRadius * FlickerSoundRadius) return;                  // far from the view: nobody sees it
            float now = Time.time;
            if (now < nextFlickSound) { OzSoundLog.Add("VL", "ngt_lamp_flicker", null, "DROP limiter (another lamp crackled < 0.35 s ago)", 0f, p); return; }
            nextFlickSound = now + 0.35f;
            bool has = AudioDirector.Has("ngt_lamp_flicker");
            if (has || AudioDirector.Has("ngt_light_fizzle"))
            {   // review fix: this lamp's own line; the Fire's AD PLAY / DROP line follows it directly (LightsTailProof pairs the two)
                r.FlickSounds++;
                OzSoundLog.Add("VL", "ngt_lamp_flicker", null, $"BURST #{r.Obj.Id} {burst}", 0f, p);
                if (has) AudioDirector.Fire("ngt_lamp_flicker", p, r.Obj.Id * 8 + (burst & 7), 0.5f);
                else AudioDirector.Fire("ngt_light_fizzle", p, r.Obj.Id, 0.35f);
            }
            else OzSoundLog.Add("VL", "ngt_lamp_flicker", null, "DROP no AudioDirector event (sound table not imported?)", 0f, p);
        }

        /// <summary>lights_tail_v1: one vehicle's lamp state (LightsTailProof, Status).</summary>
        public struct LampProbe
        {
            public bool Ok, Split, Dead, Crewed; public float FaultK, Share, SpotIntensity, SpotRange, HeadNorm, PoolShiftM, SpotShiftM;   // shifts in world metres
            public float Power;                                                                        // review fix: the whole vehicle's lamp power (r.P)
            public float FaultGlass, KeepGlass; public int FaultGlassN;                                // review fix 3: headlamp glass emission key, fault side / other side (+ fault-side glass count)
            public int Bursts, FlickSounds; public HeadlightFaults.Plan Plan; public HeadlightVariety.Look Look; public string Type;
        }

        public LampProbe Probe(int id)
        {
            foreach (var r in rigs)
            {
                if (r.Obj == null || r.Obj.Id != id) continue;
                float off = r.FaultSplit ? 1f - LampLevel(r) : 0f;
                return new LampProbe
                {
                    Ok = true, Split = r.FaultSplit, Dead = r.FaultDead, Crewed = r.Crewed, FaultK = r.FaultK, Share = LampShare(r), Power = r.P,
                    SpotIntensity = r.Spot && r.Spot.enabled ? r.Spot.intensity : 0f, SpotRange = r.Spot ? r.Spot.range : 0f, HeadNorm = r.HeadNorm,
                    PoolShiftM = r.Root ? r.Root.TransformVector(r.PoolShift).magnitude * off : 0f, SpotShiftM = r.HeadNode ? r.HeadNode.TransformVector(r.SpotShift).magnitude * off : 0f, Bursts = r.Bursts, FlickSounds = r.FlickSounds,
                    Plan = r.Fault, Look = r.Var, Type = r.Spec != null ? r.Spec.type : "?",
                    FaultGlass = r.FaultGlass, KeepGlass = r.KeepGlass, FaultGlassN = r.FaultGlassN,
                };
            }
            return default;
        }

        string FaultStatus()
        {
            int split = 0, dead = 0, dim = 0, low = 0, weak = 0;
            foreach (var r in rigs)
            {
                if (r.FaultSplit) split++;
                if (r.FaultDead) dead++; else if (r.FaultK < 0.7f) dim++;
                if (r.Var.weak < 1f) weak++;
                if (r.Obj != null && r.Obj.MaxHealth > 0 && (float)r.Obj.Health / r.Obj.MaxHealth < HeadlightFaults.AllBelow) low++;
            }
            return $"\n  lights_tail_v1: variety {(HeadlightVariety.Enabled ? "on" : "off")} ({weak} with a weaker lamp); faults: {split} split rigs, " +
                   $"{low} below {HeadlightFaults.AllBelow * 100f:F0} % hp, {dead} lamps out, {dim} dimmed / flickering now; whole-vehicle damage flicker retired (one fault lamp)";
        }

        /// <summary>The lamp dies (health crossed its threshold, lights on): a spark and a glass tinkle at the lens.</summary>
        static void FaultPop(Rig r)
        {
            if (!r.FaultMount || r.Obj == null) return;
            var p = r.FaultMount.TransformPoint(r.FaultLocal);
            FxManager.I?.Pop(p, 0.3f);
            if (AudioDirector.Has("ngt_lamp_spark")) AudioDirector.Fire("ngt_lamp_spark", p, r.Obj.Id, 0.55f);          // lights_tail_v1: the spark crackles too
            if (AudioDirector.Has("ngt_lamp_glass")) AudioDirector.Fire("ngt_lamp_glass", p, r.Obj.Id, 0.6f);
            else CombatAudio.I?.Play("night_glass", p, 0.35f, 0.05f, r.Obj.Id, "sd_glass_shatter_a", "sd_glass_shatter_b");
        }

        static bool Changed(Vector4 a, Vector4 b)
        {
            var d = a - b;
            return Mathf.Abs(d.x) + Mathf.Abs(d.y) + Mathf.Abs(d.z) + Mathf.Abs(d.w) > 0.01f + 0.01f * (Mathf.Abs(a.x) + Mathf.Abs(a.y) + Mathf.Abs(a.z) + Mathf.Abs(a.w));
        }

        /// <summary>Updates the block's _Color when it changed noticeably; false = off (nothing to draw).</summary>
        static bool SetColor(MaterialPropertyBlock b, Color c, ref float shown)
        {
            float key = c.r + c.g * 1.7f + c.b * 0.6f;
            if (key <= 0.0015f) { shown = 0f; return false; }
            if (shown <= 0f || Mathf.Abs(key - shown) > Mathf.Max(0.004f, shown * 0.012f)) { shown = key; b.SetColor(NightMeshes.ColorId, c); }
            return true;
        }

        static void Draw(Mesh m, Matrix4x4 M, Material mat, MaterialPropertyBlock b)
        {
            if (m && mat) Graphics.DrawMesh(m, M, mat, 0, null, 0, b, UnityEngine.Rendering.ShadowCastingMode.Off, false, null, UnityEngine.Rendering.LightProbeUsage.Off);
        }

        void ApplyEmissive(Rig r, float now, Color team)
        {
            float p = r.P, night = r.Night;
            float ph = now / 1.3f + r.Seed;
            foreach (var e in r.Emissive)
            {
                if (!e.R) continue;
                Color c;
                switch (e.Role)
                {
                    case "head":   // the model's headlamp glass: a warm glass glow by day (was team red on the MLRS), warm white HDR when the headlights burn
                    {   // lights_tail_v1 review fix 3: the night glass follows the fault (dead = dark, loose contact = flickers, x the weaker lamp) and
                        // the per-vehicle power look; the day glass (HeadNorm 0) is unchanged
                        float lk = r.Var.power * (e.FaultSide ? r.FaultK : 1f);
                        c = Color.Lerp(r.HeadLin * (Mathf.Max(0.35f, e.Base.maxColorComponent) * Mathf.Lerp(0.15f, 1f, p)), r.HeadLin * (headlampNight * lk), r.HeadNorm);
                        float gk = c.r + c.g * 1.7f + c.b * 0.6f;
                        if (e.FaultSide) r.FaultGlass = gk; else r.KeepGlass = gk;
                    }
                        break;
                    case "beaconA":
                    case "beaconB":
                    {
                        float blink = Mathf.Repeat(ph + (e.Role == "beaconB" ? 0.5f : 0f), 1f) < 0.14f ? 1f : 0.12f;
                        c = team * (p * blink * Mathf.Lerp(ownerDay, ownerNight * 1.3f, night));
                        break;
                    }
                    default:       // owner lens / glow strip: team colour, brighter at night, dark when empty
                        c = teamTintOwnerGlow ? team * (Mathf.Lerp(ownerDay, ownerNight, night) * Mathf.Lerp(0.04f, 1f, p))
                                              : e.Base * (Mathf.Lerp(1f, ownerNight / Mathf.Max(0.1f, ownerDay), night) * Mathf.Lerp(0.04f, 1f, p));
                        break;
                }
                float key = c.r + c.g * 1.7f + c.b * 0.6f;
                if (e.Shown >= 0f && Mathf.Abs(key - e.Shown) <= Mathf.Max(0.004f, e.Shown * 0.015f)) continue;
                e.Shown = key;
                e.R.GetPropertyBlock(mpb, e.Index);                      // keep TeamPaint's colours in the same block
                mpb.SetColor(EmissionId, c);
                e.R.SetPropertyBlock(mpb, e.Index);
            }
        }

        // ------------------------------------------------------------------ debug
        void DebugKeys()
        {
            var kb = Keyboard.current; if (kb == null || !kb.f9Key.wasPressedThisFrame || !kb.altKey.isPressed || !kb.ctrlKey.isPressed) return;   // Ctrl+Alt+F9 (Ctrl+F9 alone = NightLights' status, which also prints)
            Debug.Log(Status());
        }

        public string Status()
        {
            int crewed = 0, lit = 0, braking = 0, beams = 0, poolsOn = 0, glows = 0, emis = 0;
            foreach (var r in rigs)
            {
                if (r.Crewed) crewed++;
                if (r.HeadNorm > 0.05f) lit++;
                if (r.BrakeK > 0.5f) braking++;
                if (r.BeamOn) beams++;
                if (r.PoolOn) poolsOn++;
                if (r.GlowOn) glows += r.GlowMounts.Count;
                emis += r.Emissive.Count;
            }
            var nb = NightLights.I ? NightLights.I.Budget : fallback;
            return $"[OpenZ] VehicleLights {(game ? game.level : "?")}  t {NightGrade.TimeOfDay:F3} night {NightGrade.NightK:F2}\n" +
                   $"  vehicles {rigs.Count} (crewed {crewed}), headlights on {lit}, braking {braking}; glow draws {glows}, haze cones {beams}, pools {poolsOn}, emissive parts {emis}\n" +
                   $"  real headlights lit {RealLightsLit} (offered <= {maxRealLights}, radius {realLightRadius:F0} m) via {(NightLights.I && NightBudgetShare.HookFrame >= Time.frameCount - 2 ? "night budget" : "fallback budget")}: " +
                   $"budget real {nb.Granted}/{nb.EffectiveMax}, heat {nb.HeatLevel} (GPU {(nb.Temperature >= 0 ? nb.Temperature + " C" : "n/a")}), frame {nb.FrameMs:F1} ms" +
                   (MissingNodes > 0 ? $"\n  {MissingNodes} sockets fell back to the vehicle root (mount node missing)" : "") +
                   (NightProjectileGlow.I ? "\n  " + NightProjectileGlow.I.Status() : "") + FaultStatus();   // lights_tail_v1: + faults / look
        }
    }
}
