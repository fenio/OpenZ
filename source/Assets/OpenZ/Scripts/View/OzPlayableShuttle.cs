using System.Collections;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The view of a playable shuttle (rule DV118, Sim/ZWorld.Shuttle.cs and its flight / ramp partials). Level 16's own ship (the
    /// scene's prefab L16_Shuttle with its lights, exhaust and team colour) becomes the unit's view: ZGame binds a UnitView to it and this
    /// component draws it. More shuttles (the testing panel, any level) get the same view from a copy of that prefab
    /// (<see cref="SpawnView"/>): every one is drawn, sounded and lit the same way. ONE POSE PER FRAME: the whole placement runs in Update
    /// at order 200, after UnitView's own placement (60) and before every LateUpdate, so the selection ring, the frame, the health bar and
    /// its name chip, the boarding figures, the birds and the ground marks all see the ship where it is drawn (landed or 12 m up).
    ///  * WHERE: the rules' middle (moved on by the part of the tick already elapsed); the facing = the rules' heading (they own it at
    ///    all times, so the ramp the rules use is the ramp that is drawn), eased in 0.12 s, exact when it stands.
    ///  * HEIGHT: the rules' lift eased into 0 .. 12 m over the ground (over a repair station's deck: over the deck); while it flies it also
    ///    clears what stands under its way (the fort, the pyramids, trees, cliffs: OzShuttleSkyTop; up at most 4 m/s, down at most 2 m/s)
    ///    and bobs (0.7 m: a slow 3 s wave and a quicker one, with a roll and pitch drift of 1.5 degrees; the clearances carry the bob;
    ///    calmer with CALM SCREEN: OzPlayableShuttle.Flight.cs); coming down, any
    ///    extra height is gone by the last 30 % of the descent, so it touches down exactly when the rules land it. Standing, it sits on its
    ///    four pads (a plane through the ground, or the station's deck, under them).
    ///  * TILT: it leans into its turns from the rules' turn rate (up to 20 degrees, never faster than 26 deg/s), but only while it
    ///    really flies (scaled by its speed:
    ///    turning on the spot it stays level), the inner side down; into a sideways strafe it leans with the glide (its sideways speed
    ///    and a little of its acceleration, eased and rate-limited, at most 10 degrees: one smooth lean, no rocking); its nose dips (up to
    ///    7 degrees) while it speeds up and lifts while it brakes (the rules' eased acceleration: level while it cruises), a little nose-up
    ///    while it climbs.
    ///  * THRUSTERS, DUST, SHADOW, SEARCHLIGHTS, TREES, BIRDS: OzShuttleJets (two downward rocket jets, always on in flight),
    ///    OzShuttleDownwash (a soft dust cloud rolling out over the ground, spray over water), OzShuttleShadow (the dark mark under it in
    ///    the air, red and bouncing where it cannot land), OzShuttleSearchlights (two belly lamps at night), OzFloraDownwash (the
    ///    plants lean out under it), JungleFauna.Ship (the parrots keep out of its way).
    ///  * THE REAR RAMP: ShuttleRearDoor (the modelled door kit; the ramp follows the rules' ramp state).
    ///  * LANDINGS: about 1 in 4 is rough, as the rules decide it (a crash landing, OzPlayableShuttle.Crash.cs: a scripted pose on the
    ///    rules' clock: it wobbles in, slams down on the side the rules name, that side rebounds, the other side bangs down, the first
    ///    side's struts give, it slumps onto them and slides with a scrape, rests crooked 13.7 degrees, then the hydraulic rights it on the
    ///    rules' clock; the ramp, EXIT and a take-off wait for that in the rules); never on a repair station. The landing lamps and
    ///    the pads' neon rings at the feet ride on their legs (they never sink into the ground when a strut gives).
    ///  * THE REPAIR STATION: it lands ON the deck where the rules put it, turned on the rules' deck heading, while the station's crane
    ///    rolls out of its way (OzStationCrane).
    ///  * SOUNDS (the sound table rows shuttle_*): spool-up and take-off at the take-off, the hover loop and the jet burn loop while it
    ///    is up, a thruster whoosh when it starts a strafe (softer as it drifts back), the landing thump, the fall when it is shot down;
    ///    the door's sounds belong to the ramp.
    ///  * VOICES (Alan): "All aboard!" as the ramp closes after a boarding, "Everybody out!" when EXIT is given, "Touchdown!" /
    ///    "Nice landing!" ("Coming in hot!" when it was hit in the last 5 s; a crash landing: "Coming in hot!" about 1 s before it hits,
    ///    never the landing line), "Can't land here!" with the error blip. RACHEL (the
    ///    announcer): K407, ONE landing voice per landing, the robot's line OR hers, never both (OzPlayableShuttle.LandingVoice.cs: a
    ///    seeded coin, never 3 in a row the same, hers only on screen and at most once per 15 s), through the announcer's queue;
    ///    never on a repair landing. While her line plays no robot line starts
    ///    (AudioDirector.ShuttleVoice.cs holds them).
    ///  * DESTROYED: it falls apart into its modelled pieces (OzShuttleBreakup, at the drawn kill moment).
    ///  * The option SHUTTLE off, or no playable shuttle: <see cref="TakeOver"/> hides the scene's ship (off) or leaves the decoration
    ///    as it was ('Zod exact').
    /// View only: nothing here changes the rules.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed partial class OzPlayableShuttle : MonoBehaviour
    {
        /// <summary>Level 16's own ship (else the first one drawn); <see cref="Of"/> finds the view of any shuttle.</summary>
        public static OzPlayableShuttle I { get; private set; }
        static readonly List<OzPlayableShuttle> all = new List<OzPlayableShuttle>();
        /// <summary>Every drawn shuttle (level 16's own ship and the ones the testing panel drops).</summary>
        public static int Count => all.Count;
        public static OzPlayableShuttle At(int i) => all[i];
        /// <summary>The view drawing shuttle o (null: none).</summary>
        public static OzPlayableShuttle Of(ZObject o)
        {
            if (o == null || !o.IsShuttle) return null;
            for (int i = 0; i < all.Count; i++) { var s = all[i]; if (s && s.Obj == o) return s; }
            return null;
        }
        /// <summary>The renderer is part of a drawn shuttle.</summary>
        public static bool OfRenderer(Renderer r)
        {
            if (!r) return false;
            for (int i = 0; i < all.Count; i++) { var s = all[i]; if (s && r.transform.IsChildOf(s.transform)) return true; }
            return false;
        }
        public static OzPlayableShuttle OfId(int id)
        {
            for (int i = 0; i < all.Count; i++) { var s = all[i]; if (s && s.Obj != null && s.Obj.Id == id) return s; }
            return null;
        }
        /// <summary>Proof switches (tests only): Rachel's landing line on every landing; the next landing rough.</summary>
        public static bool ProofRachelEvery;
        /// <summary>The next landing is a rough one (the rules' proof switch, ZWorld.ShuttleRoughProofNext).</summary>
        public static bool ProofRoughNext { get => ZWorld.ShuttleRoughProofNext; set => ZWorld.ShuttleRoughProofNext = value; }
        /// <summary>The unit prefab for more shuttles (a copy of the level 16 ship's prefab, in Resources so any level can draw one).</summary>
        public const string UnitPrefabPath = "OpenZ/Vehicles/L16_ShuttleUnit";
        /// <summary>Proof: shuttle views made from the unit prefab; frames a ship stood under 7.5 m over a station's deck before its crane was away.</summary>
        public static int Spawned, SpawnFailures;

        [Tooltip("degrees from the root's forward to the ship's nose (the dome looks along the root's forward: 0)")] public float noseYaw = 0f;
        [Tooltip("hover bob (m), nose-down at full acceleration and nose-up at full braking (deg; level while cruising), the most it leans into a turn (deg), the most it leans into a strafe (deg)")] public float bobM = 0.7f, pitchDeg = 7f, bankMax = 20f, strafeLeanMax = 10f;
        [Tooltip("the lean into a strafe: degrees per m/s of sideways speed and per m/s^2 of sideways acceleration; its fastest change (deg/s)")] public float strafeLeanPerV = 1.3f, strafeLeanPerA = 0.2f, strafeLeanRate = 22f;
        [Tooltip("the two thruster lights: range (m) and strength at full throttle")] public float thrusterRange = 11f, thrusterPower = 4.5f;

        // read by the Play checks
        public ZObject Obj { get; private set; }
        /// <summary>The drawn root (the pad soles) over the ground under its middle (m); CombatView aims at the drawn hull with it.</summary>
        public float ShownHeight { get; private set; }
        /// <summary>The pads' height over what they would stand on (the ground, or the station's deck) (m) and the drawn velocity (m/s, real time).</summary>
        public float FeetHeight { get; private set; }
        public Vector3 Velocity { get; private set; }
        public float Throttle => jets != null ? jets.Throttle : 0f;
        public float BankDeg { get; private set; }
        /// <summary>The part of the bank that leans into the turn (deg; the rest is the strafe's lean).</summary>
        public float TurnBankDeg { get; private set; }
        /// <summary>The part of the bank that leans into the rules' sideways strafe (deg; + = the +x side up).</summary>
        public float StrafeLeanDeg { get; private set; }
        /// <summary>Proof: frames the rules' ramp moved while a rough landing was still being drawn (0: it opens only once level).</summary>
        public static int RampWhileRighting;
        /// <summary>Proof: frames a shuttle stood within 7.5 m over a station's deck before that station's crane had rolled fully away.</summary>
        public static int CraneInTheWay;
        public float PitchNowDeg { get; private set; }
        public float YawDeg => yaw;
        public float BobNow { get; private set; }
        public float ClimbRate { get; private set; }
        public int Landings { get; private set; }
        public int RoughLandings { get; private set; }
        public int RachelLines { get; private set; }
        public int StrafeWhooshes { get; private set; }
        public string LastSound { get; private set; } = "";
        public string LastVoice { get; private set; } = "";
        public bool BrokenUp { get; private set; }
        public bool RoughArmed => roughArmed;
        /// <summary>The repair station whose deck it stands on or comes down to (-1 = none) and the deck's height under its middle (world y).</summary>
        public int DeckStation { get; private set; } = -1;
        public float DeckTopY { get; private set; }
        public ShuttleRearDoor Door => door;
        public OzShuttleJets Jets => jets;
        public OzShuttleDownwash Wash => wash;
        public OzShuttleShadow Shadow => shadow;
        public OzShuttleSearchlights Searchlights => search;
        public OzShuttleTouchdown Touch => touch;
        public OzShuttleSkyTop Sky => sky;
        /// <summary>Per landing: Alan's line and Rachel's line with their start times (unscaled) for the voice check.</summary>
        public float LastAlanAt { get; private set; } = -1f;
        public float LastRachelAt { get; private set; } = -1f;
        public string LastAlanId { get; private set; } = "";
        /// <summary>The pad-level lamps and neon rings moved onto their legs (they ride with the strut strokes).</summary>
        public int FootParts { get; private set; }
        /// <summary>Level 16's own scene ship (false: drawn from the unit prefab).</summary>
        public bool SceneShip => deco;

        ZGame game; UnitView view; L16Shuttle deco; OzShuttleExhaust exhaust; Light[] glow = new Light[0]; OzShuttleLights lights;
        ShuttleRearDoor door; OzShuttleJets jets; OzShuttleDownwash wash; OzShuttleShadow shadow; OzShuttleSearchlights search; OzShuttleTouchdown touch; OzShuttleSkyTop sky;
        int lastSeq, lastStrafes, lastReturns; float yaw, yawVel, bank, bankVel, pitch, pitchVel, t, drawnY, yVel, descendOff, lastY, roughSide, strafeLean, deckK; bool loopOn, jetLoopOn, roughArmed, haveY, seenPending, dying;
        float enginePeriod0 = 3.2f; Vector2 engineGain0 = new Vector2(1.2f, 2.6f); bool skyStarted;
        ZShuttleState lastState; ZShuttleRamp lastRamp; double lastRachel = -1000; float dyingT; Vector3 dyingPos, dyingVel, lastPos, smoothVel;
        string loopKey = "shuttle:hover", jetKey = "shuttle:jet";
        static readonly string[] LegNames = { "SH_Leg_FL", "SH_Leg_FR", "SH_Leg_BL", "SH_Leg_BR" };       // the pads' order (the _L parts sit at root +x)
        readonly Transform[] legs = new Transform[4]; readonly Vector3[] legRest = new Vector3[4];
        static Transform FindDeep(Transform t, string n) { if (t.name == n) return t; foreach (Transform c in t) { var r = FindDeep(c, n); if (r) return r; } return null; }
        static readonly Vector3[] PadSoles = { new Vector3(2.228f, 0f, 1.029f), new Vector3(-2.168f, 0f, 1.079f), new Vector3(1.829f, 0f, -2.843f), new Vector3(-1.771f, 0f, -2.838f) };
        readonly Vector3[] fitP = new Vector3[4]; readonly float[] fitG = new float[4];                        // FitY's work arrays (no garbage per frame)

        /// <summary>ZGame.Awake, after the scene's units are bound: the scene's ship becomes the playable shuttle's view.</summary>
        public static void Spawn(ZGame g)
        {
            var s = g != null && g.World != null ? g.World.PlayableShuttle : null;
            if (s == null) return;
            var ship = Object.FindFirstObjectByType<L16Shuttle>(FindObjectsInactive.Include);
            if (!ship) { if (ZWorld.L17DrawnFromPrefab(g.World)) return; Debug.LogWarning("[OpenZ] shuttle: no ship in the scene to draw the playable shuttle with"); return; }   // K444: level 17's ships are drawn from the unit prefab (OzShuttleEmptyLook.SpawnMapShuttles)
            var go = ship.gameObject;
            var v = go.GetComponent<UnitView>(); if (!v) v = go.AddComponent<UnitView>();
            v.Bind(g, s); g.Views[s.Id] = v;
            var me = go.GetComponent<OzPlayableShuttle>(); if (!me) me = go.AddComponent<OzPlayableShuttle>();
            me.Bind(g, s, ship, v);
            Debug.Log($"[OpenZ] shuttle (DV118): the scene's ship draws the playable shuttle #{s.Id} of {s.Owner} ({s.MaxHealth} HP)");
        }

        /// <summary>Another shuttle of the rules (the testing panel, any level): its full view from the unit prefab (the level 16 ship's
        /// own parts: hull, pods, legs, lamps, neon, exhaust, team colour; the decoration's part is taken off), placed where the rules have
        /// it. The same component then draws it as level 16's own ship. Null (logged) when the prefab is missing.</summary>
        public static OzPlayableShuttle SpawnView(ZGame g, ZObject s)
        {
            if (g == null || s == null || !s.IsShuttle) return null;
            var have = Of(s); if (have) return have;
            var prefab = Resources.Load<GameObject>(UnitPrefabPath);
            if (!prefab) { SpawnFailures++; Debug.LogError("[OpenZ] shuttle: the unit prefab Resources/" + UnitPrefabPath + " is missing: shuttle #" + s.Id + " is not drawn"); return null; }
            var hold = new GameObject("OZ_ShuttleSpawn"); hold.SetActive(false);                             // nothing of the copy wakes up before it is made a unit
            var go = Instantiate(prefab, hold.transform);
            var dec = go.GetComponent<L16Shuttle>();
            if (dec)
            {   // the decoration's own part goes: its wreck pieces stay hidden (the break-up uses the unit's own kit pieces)
                foreach (var p in dec.pieces) if (p) foreach (var r in p.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                if (dec.wreck) dec.wreck.gameObject.SetActive(false);
                DestroyImmediate(dec);
            }
            var dp = go.GetComponent<DetailProp>(); if (dp) DestroyImmediate(dp);
            go.name = $"{s.Type}_rt{s.Id}";
            var zod = GameObject.Find("ZodObjects");
            var at = g.CenterWorldSmooth(s); at.y = g.GroundY(at.x, at.z);
            go.transform.SetParent(zod ? zod.transform : null, false);
            go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, s.ShuttleHeadingDeg + 90f, 0f));
            Destroy(hold);
            var tp = go.GetComponent<TeamPaint>(); if (tp) tp.Team = s.Owner;
            var lt = go.GetComponent<OzShuttleLights>(); if (lt) lt.SetTeam(TeamColors.Shown(s.Owner));
            var v = go.GetComponent<UnitView>(); if (!v) v = go.AddComponent<UnitView>();
            v.Bind(g, s); g.Views[s.Id] = v;
            var me = go.GetComponent<OzPlayableShuttle>(); if (!me) me = go.AddComponent<OzPlayableShuttle>();
            me.Bind(g, s, null, v); Spawned++;
            Debug.Log($"[OpenZ] shuttle (DV118): shuttle #{s.Id} of {s.Owner} drawn from the unit prefab ({s.MaxHealth} HP)");
            return me;
        }

        /// <summary>L16Shuttle.KnowRule hook: true = the decoration's own part steps aside (the playable shuttle, or the option off).</summary>
        public static bool TakeOver(L16Shuttle ship, ZGame g)
        {
            if (!ship || !g || g.World == null) return false;
            var w = g.World;
            if (w.PlayableShuttle == null && !w.L16ShuttleOptionOff) return false;            // the decoration as before ('Zod exact': the hut)
            if (w.L16CampHutRemoved)
            {   // the hut beside the start camp is out of the rules in both cases: not drawn
                var zod = GameObject.Find("ZodObjects"); var ch = zod ? zod.transform.Find("hut_" + ZWorld.L16CampHutTile[0] + "_" + ZWorld.L16CampHutTile[1]) : null;
                if (ch) ch.gameObject.SetActive(false);
            }
            if (ship.searchlight) ship.searchlight.gameObject.SetActive(false);                   // the field searchlight stays removed (the ship carries its own lamps)
            var dp = ship.GetComponent<DetailProp>(); if (dp) { dp.StopLoop(); dp.enabled = false; }
            if (w.L16ShuttleOptionOff)
            {
                ship.gameObject.SetActive(false);
                Debug.Log("[OpenZ] shuttle: the option SHUTTLE is off: no shuttle on this level");
                return true;
            }
            ship.enabled = false;                                                                 // the playable shuttle's view drives the ship
            return true;
        }

        void Bind(ZGame g, ZObject s, L16Shuttle ship, UnitView v)
        {
            game = g; Obj = s; deco = ship; view = v;
            all.RemoveAll(x => !x); if (!all.Contains(this)) all.Add(this);
            if (!I || s == g.World.PlayableShuttle) I = this;
            loopKey = "shuttle:hover:" + s.Id; jetKey = "shuttle:jet:" + s.Id;                    // each ship its own loops
            exhaust = GetComponent<OzShuttleExhaust>();
            yaw = s.ShuttleHeadingDeg + 90f + noseYaw;                                             // the rules own the facing (the scene's 150 at map load)
            lights = GetComponent<OzShuttleLights>(); if (lights) { enginePeriod0 = lights.enginePeriod; engineGain0 = lights.engineGain; }
            lastSeq = g.World.ShuttleNoteSeq; lastState = s.ShuttleState; lastRamp = s.ShuttleRampPhase; lastStrafes = s.ShuttleStrafes; lastReturns = s.ShuttleStrafeReturns;
            glow = new Light[2];
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject("ThrusterGlow" + i); go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>(); l.type = LightType.Point; l.range = thrusterRange; l.intensity = 0f; l.shadows = LightShadows.None;
                glow[i] = l;
            }
            door = ShuttleRearDoor.Ensure(v);
            if (!door) Debug.LogError("[OpenZ] shuttle: the rear door kit could not be fitted (" + ShuttleRearDoor.KitLog + "): no ramp, no nozzles");
            OzShuttleJets.LightIntensity = thrusterPower; OzShuttleJets.LightRange = thrusterRange;
            jets = new OzShuttleJets(transform, door, glow);
            wash = new OzShuttleDownwash(transform, door);
            shadow = new OzShuttleShadow(transform);
            search = new OzShuttleSearchlights(transform, door, s.Id);
            search.Night = new OzShuttleNightLights(this, door, search);                           // the landing lights' rule, the nose searchlight, the ramp light
            touch = new OzShuttleTouchdown(transform, s.Id);
            for (int i = 0; i < 4; i++) { legs[i] = FindDeep(transform, LegNames[i]); if (legs[i]) legRest[i] = legs[i].localPosition; }   // a crash landing pushes struts up into their pods
            if (!legs[0] || !legs[3]) Debug.LogWarning("[OpenZ] shuttle: its legs were not found by name: a crash landing's struts are not drawn");
            FootParts = SplitFootParts();
            sky = new OzShuttleSkyTop(g, transform);                                                 // its clearance grid: built over frames once the level has settled (LateUpdate)
            OzShuttleBreakup.BindShip(transform);                                                  // the break-up plan is cut from now on, one cut a frame
            lastPos = transform.position;
        }

        /// <summary>The landing lamps and the pads' neon rings at the feet (the pad-level triangles of SH_LampsA / SH_LampsB / SH_NeonBlink)
        /// become parts of their own on the nearest leg, so a strut that gives takes them along; the lamps' glow is copied onto them every
        /// frame (OzShuttleLights drives the original renderers). Returns the parts made (0 = the model is not as measured: nothing moved).</summary>
        int SplitFootParts()
        {
            if (!legs[0] || !legs[1] || !legs[2] || !legs[3]) return 0;
            int made = 0;
            foreach (var n in new[] { "SH_LampsA", "SH_LampsB", "SH_NeonBlink" })
            {
                var t = FindDeep(transform, n); var mf = t ? t.GetComponent<MeshFilter>() : null; var mr = t ? t.GetComponent<MeshRenderer>() : null;
                if (!mf || !mr || !mf.sharedMesh || !mf.sharedMesh.isReadable) continue;
                var mesh = mf.sharedMesh; var vs = mesh.vertices; var toRoot = transform.worldToLocalMatrix * t.localToWorldMatrix;
                var R = new Vector3[vs.Length]; for (int i = 0; i < vs.Length; i++) R[i] = toRoot.MultiplyPoint3x4(vs[i]);
                int subs = mesh.subMeshCount; var keep = new List<int>[subs]; var foot = new List<int>[4, subs]; bool mixed = false; int nFoot = 0;
                for (int k = 0; k < subs; k++) { keep[k] = new List<int>(); for (int l = 0; l < 4; l++) foot[l, k] = new List<int>(); }
                var legC = new Vector3[4]; for (int l = 0; l < 4; l++) legC[l] = transform.InverseTransformPoint(legs[l].GetComponent<Renderer>() ? legs[l].GetComponent<Renderer>().bounds.center : legs[l].position);
                for (int k = 0; k < subs; k++)
                {
                    var tri = mesh.GetTriangles(k);
                    for (int i = 0; i + 2 < tri.Length; i += 3)
                    {
                        int low = (R[tri[i]].y < 0.6f ? 1 : 0) + (R[tri[i + 1]].y < 0.6f ? 1 : 0) + (R[tri[i + 2]].y < 0.6f ? 1 : 0);
                        if (low == 0) { keep[k].Add(tri[i]); keep[k].Add(tri[i + 1]); keep[k].Add(tri[i + 2]); continue; }
                        if (low < 3) { mixed = true; break; }
                        var c = (R[tri[i]] + R[tri[i + 1]] + R[tri[i + 2]]) / 3f; int best = 0; float bd = float.MaxValue;
                        for (int l = 0; l < 4; l++) { float d = (c.x - legC[l].x) * (c.x - legC[l].x) + (c.z - legC[l].z) * (c.z - legC[l].z); if (d < bd) { bd = d; best = l; } }
                        foot[best, k].Add(tri[i]); foot[best, k].Add(tri[i + 1]); foot[best, k].Add(tri[i + 2]); nFoot++;
                    }
                    if (mixed) break;
                }
                if (mixed || nFoot == 0) continue;                                                            // not the measured model: left whole
                Mesh Make(List<int>[] T, string name)
                {
                    var r = Instantiate(mesh); r.name = name; r.subMeshCount = subs;
                    for (int k = 0; k < subs; k++) r.SetTriangles(T[k], k, false);
                    r.RecalculateBounds(); return r;
                }
                mf.sharedMesh = Make(keep, mesh.name + "_upper");
                for (int l = 0; l < 4; l++)
                {
                    var T = new List<int>[subs]; int cnt = 0; for (int k = 0; k < subs; k++) { T[k] = foot[l, k]; cnt += T[k].Count; }
                    if (cnt == 0) continue;
                    var go = new GameObject(n + "_Foot_" + LegNames[l].Substring(7)) { layer = t.gameObject.layer };
                    go.transform.SetParent(t.parent, false); go.transform.SetPositionAndRotation(t.position, t.rotation); go.transform.localScale = t.localScale;
                    go.transform.SetParent(legs[l], true);                                                    // rides with the leg's stroke
                    go.AddComponent<MeshFilter>().sharedMesh = Make(T, mesh.name + "_foot");
                    var r = go.AddComponent<MeshRenderer>(); r.sharedMaterials = mr.sharedMaterials; r.shadowCastingMode = mr.shadowCastingMode; r.receiveShadows = mr.receiveShadows;
                    footParts.Add((mr, r)); made++;
                }
            }
            return made;
        }
        readonly List<(MeshRenderer src, MeshRenderer dst)> footParts = new List<(MeshRenderer, MeshRenderer)>();
        MaterialPropertyBlock footMpb;
        void FootGlow()
        {   // the lamps' and the neon's glow as OzShuttleLights set it this frame (it drives the original renderers)
            if (footParts.Count == 0) return;
            footMpb ??= new MaterialPropertyBlock();
            foreach (var (src, dst) in footParts)
            {
                if (!src || !dst) continue;
                if (dst.enabled != src.enabled) dst.enabled = src.enabled;
                src.GetPropertyBlock(footMpb, 0); dst.SetPropertyBlock(footMpb, 0);                         // OzShuttleLights sets the first sub-mesh's block
            }
        }

        void OnDisable()
        {
            if (I == this) { I = null; foreach (var o in all) if (o && o != this && o.isActiveAndEnabled) { I = o; break; } }
            var ad = AudioDirector.I;
            if (ad) { if (loopOn) ad.StopLoop(loopKey, 0.4f); if (jetLoopOn) ad.StopLoop(jetKey, 0.3f); }
            loopOn = jetLoopOn = false;
        }

        void OnDestroy() { all.Remove(this); jets?.Dispose(); wash?.Dispose(); shadow?.Dispose(); search?.Dispose(); TouchesStop(); }

        static int Hash(int a, int b) { unchecked { uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return (int)(h & 0x7fffffff); } }

        // ================================================================== Update 200: the notes and the one pose
        void Update()
        {
            if (game == null || game.World == null || Obj == null) return;
            var w = game.World; float rdt = Mathf.Min(Time.deltaTime, 0.05f), dt = game.paused ? 0f : rdt * Mathf.Max(0.05f, game.gameSpeed);
            ReadNotes(w);                                                                            // first, also once it is in pieces (a kill drawn at once: the fall's sound)
            ScrapeTick();                                                                            // K256e: the crash's scrape marks fade (OzPlayableShuttle.Crash.cs)
            if (BrokenUp) return;
            t += dt;
            if (dying) { DyingPose(rdt); return; }
            if (Obj.Destroyed || w.Get(Obj.Id) != Obj) return;
            Pose(dt, rdt);
        }

        /// <summary>The station whose deck it comes down to or stands on (the rules' crane call names it), and how much of the deck's height
        /// counts at its place (1 over the deck, 0 more than 6 m from it).</summary>
        float DeckBase(Vector3 p, float gy)
        {
            DeckStation = Obj.ShuttleCraneStation;
            float want = 0f;
            if (DeckStation >= 0 && OzStationCrane.DeckY(DeckStation, p, out float dy) && dy > gy) { DeckTopY = dy; want = 1f; }
            else if (DeckStation >= 0 && game.World.Get(DeckStation) is ZObject st && Obj.ShuttleDeckPoint(st, out int rx, out int ry))
            {
                var c = ZGame.PxToWorld(rx, ry); float d = new Vector2(p.x - c.x, p.z - c.z).magnitude;
                if (d < 8f && OzStationCrane.DeckY(DeckStation, c, out float cy)) { DeckTopY = cy; want = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4f, 8f, d)); }
            }
            deckK = Mathf.MoveTowards(deckK, want, 2f * Time.deltaTime);
            return Mathf.Lerp(gy, Mathf.Max(gy, DeckTopY), deckK);
        }

        void Pose(float dt, float rdt)
        {
            var st = Obj.ShuttleState; float lift = (float)Obj.ShuttleLift;
            var p = game.CenterWorldSmooth(Obj);
            float gy = game.GroundY(p.x, p.z), by = DeckBase(p, gy);
            float ruleY = by + ((float)ZWorld.ShuttleCruiseM + TrafficAddM(dt, lift)) * Mathf.SmoothStep(0f, 1f, lift);   // K338: each ship's own cruise step and its giving way in height (OzPlayableShuttle.Traffic.cs; 0 for level 16's own ship alone)
            bool reduced = OzMotion.Reduced;
            // the start of a descent: the rough landing is known now, the extra height above the rules is let go by 30 % of the way
            if (st == ZShuttleState.Descend && lastState != ZShuttleState.Descend)
            {
                roughArmed = Obj.ShuttleRoughLanding;                                                   // the rules decide (never on a repair station)
                hotSaid = false;                                                                        // K256e: "Coming in hot!" once per rough descent
                if (roughArmed) { roughSide = Obj.ShuttleRoughSide >= 0 ? 1f : -1f; touch.Arm(roughSide); }
                descendOff = haveY ? Mathf.Max(0f, drawnY - ruleY) : 0f;
            }
            if (st != ZShuttleState.Descend && st != ZShuttleState.Landed) roughArmed = roughArmed && st == ZShuttleState.Descend;
            // the height
            float y; float rollAdd = 0f, pitchAdd = 0f, yawAdd = 0f; Quaternion tilt = Quaternion.identity;
            var vel = new Vector3(Obj.Dx / 8f, 0f, -Obj.Dy / 8f);                                       // the rules' flight velocity (m per sim second)
            switch (st)
            {
                case ZShuttleState.Cruise: case ZShuttleState.Hover:
                {
                    float skyY = sky.Wanted(p, vel, true, out _, out _);
                    float target = Mathf.Max(ruleY, skyY);
                    if (!haveY) drawnY = target;
                    if (target > drawnY) drawnY = Mathf.SmoothDamp(drawnY, target, ref yVel, 0.5f, climbMaxMps, dt);   // K256j (M11): 3.6 -> climbMaxMps 3.2 m/s (OzPlayableShuttle.Climb.cs)
                    else drawnY = Mathf.SmoothDamp(drawnY, target, ref yVel, 1.2f, 2f, dt);
                    y = drawnY; break;
                }
                case ZShuttleState.Descend:
                {
                    float off = descendOff * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 1f, lift));
                    float tau = lift * (float)ZWorld.ShuttleDescendSec;
                    float h = ruleY - by;
                    if (roughArmed) h = CrashDescent(tau, h, out rollAdd, out pitchAdd);               // K256e: the wobble, "Coming in hot!", the fast last 0.5 s (OzPlayableShuttle.Crash.cs)
                    y = by + h + off; drawnY = y; yVel = 0f; break;
                }
                case ZShuttleState.Climb:
                {   // the rules' climb, raised when something high stands round its pad or under its way
                    float skyY = sky.Wanted(p, vel, true, out _, out _);
                    if (haveY && skyY > ruleY) drawnY = Mathf.Max(ruleY, Mathf.SmoothDamp(Mathf.Max(drawnY, ruleY), skyY, ref yVel, 0.5f, 6f, dt));
                    else { drawnY = ruleY; yVel = 0f; }
                    y = drawnY; break;
                }
                default:
                    y = FitY(p, out tilt); drawnY = y; yVel = 0f; break;                                  // standing: the four-pad fit, once a frame
            }
            haveY = true;
            // the bob and the drift, only well off the ground
            float feet = y - by, bw = Mathf.Clamp01((feet - 1f) / 2f) * (reduced ? 0.5f : 1f) * CrashDropBobK;   // K408: no bob in a crash's drop (OzPlayableShuttle.CrashDrop.cs)
            ClimbBob(bw, dt, y, out float rollDrift, out float pitchDrift);                         // K256e: 0.7 m, 3 s, drifts 1.5 deg (OzPlayableShuttle.Flight.cs); K256j (M11): its clock slower while it climbs (OzPlayableShuttle.Climb.cs)
            y += BobNow;
            // the facing: the rules' heading (eased; exact while it stands)
            float want = Obj.ShuttleHeadingDeg + 90f + noseYaw;
            yaw = Mathf.SmoothDampAngle(yaw, want, ref yawVel, 0.12f, Mathf.Infinity, rdt);
            if (st == ZShuttleState.Landed && lift <= 0f && Mathf.Abs(Mathf.DeltaAngle(yaw, want)) < 0.5f) { yaw = want; yawVel = 0f; }
            float up2 = Mathf.Clamp01(lift * 2f);
            float vmax = Mathf.Max(1f, (float)Obj.RealMoveSpeed), acc = vmax / (float)ZWorld.ShuttleAccelSec;
            // the lean into turns: only while it really flies (turning on the spot, climbing or over the station it yaws level), the inner side down
            float spd = (float)Obj.ShuttleSpeedPx, flyK = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(5f, 0.45f * vmax, spd));
            float turnWant = Mathf.Clamp(-Obj.ShuttleTurnRateDeg * bankPerTurn * up2 * flyK, -bankMax, bankMax);   // K256e: 0.22 -> 0.28
            // the lean into the strafe: with the glide (its sideways speed, a little of its acceleration), eased and rate-limited: one smooth
            // lean out and back, never a rock; + strafe = to the right of the facing = the right side down = a negative lean
            float sv = (float)(Obj.ShuttleStrafeVelPx + Obj.ShuttleAvoidLatVelPx) / 8f, sa = (float)(Obj.ShuttleStrafeAccelPx + Obj.ShuttleAvoidLatAccelPx) / 8f;   // K338: and into its giving way to another ship (the rules' avoid offset)
            if (float.IsNaN(sv) || float.IsNaN(sa)) { sv = 0f; sa = 0f; }
            float leanWant = -Mathf.Clamp(strafeLeanPerV * sv + strafeLeanPerA * sa, -strafeLeanMax, strafeLeanMax) * up2;
            strafeLean = Mathf.MoveTowards(strafeLean, leanWant, strafeLeanRate * dt);
            StrafeLeanDeg = strafeLean;
            float bankWant = Mathf.Clamp(turnWant + strafeLean, -bankMax, bankMax);
            bank = Mathf.SmoothDamp(bank, bankWant, ref bankVel, 0.3f, bankRateMax, dt); BankNote(dt);   // K256e: never faster than bankRateMax deg/s
            TurnBankDeg = bank - strafeLean;
            float pitchWant = pitchDeg * Mathf.Clamp((float)Obj.ShuttleAccelPx / acc, -1f, 1f) * up2;              // nose down speeding up, up braking, level cruising
            if (st == ZShuttleState.Descend) pitchWant = -2f * up2;
            ClimbRate = dt > 0f ? (y - lastY) / dt : 0f;
            if (st == ZShuttleState.Cruise || st == ZShuttleState.Hover) pitchWant -= 3f * Mathf.Clamp(ClimbRate / 4f, 0f, 1f);
            pitch = Mathf.SmoothDamp(pitch, pitchWant, ref pitchVel, 0.4f, Mathf.Infinity, dt);
            BankDeg = bank; PitchNowDeg = pitch;
            // the touch-down's own pose on top (height, roll, pitch, slide, slew)
            var pos = new Vector3(p.x, y, p.z);
            if (crashOn) CrashPoseStep(dt, ref pos, ref rollAdd, ref pitchAdd, ref yawAdd);       // K256e: the scripted crash on the rules' clock (OzPlayableShuttle.Crash.cs)
            var rot = tilt * Quaternion.Euler(0f, yaw + yawAdd, 0f) * Quaternion.Euler(pitch + pitchDrift + pitchAdd, 0f, bank + rollDrift + rollAdd);
            SuspPose(dt, st, by, ref pos, ref rot); LegsFoldStep(dt, st, pos.y + springDipNow - by);   /* K388: the legs fold in flight (OzPlayableShuttle.LegsFold.cs) */                                                     // K256g3: the hull on its legs' suspension (OzPlayableShuttle.Suspension.cs; K256g's dip when the rig is not fitted)
            if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z)) pos = lastPos;                // never a broken pose
            transform.SetPositionAndRotation(pos, rot);
            AfterPose();                                                                                // K413: the puffs laid inside the pose come out on the drawn ship (OzPlayableShuttle.PosedPuffs.cs)
            for (int i = 0; i < 4; i++)
            {   // the strut stroke of a crash landing (0 otherwise): the leg slides up into its pod along the hull's up
                var lg = legs[i]; if (!lg) continue;
                float c = CrashStrut(i);                                                                 // K256e: the crash's strut stroke
                SuspLeg(i, lg, c); LegsFoldLeg(i);   /* K388: the leg folds flat under the belly (OzPlayableShuttle.LegsFold.cs) */                                                                          // K256g3: the planted foot, the strut on its hip, the chrome piston (OzPlayableShuttle.Suspension.cs; K256g's LegPose when the rig is not fitted)
            }
            ShownHeight = pos.y + springDipNow - gy; FeetHeight = pos.y + springDipNow - by;            // K256g: the pads stay on the ground while the cabin springs
            if (DeckStation >= 0 && FeetHeight < 7.5f && OzStationCrane.SlideK(DeckStation) < 0.999f) CraneInTheWay++;
            // the drawn velocity (real time): the rules' flight (x the game speed) and the height change
            float gs = game.paused ? 0f : Mathf.Max(0.05f, game.gameSpeed);
            float vy = rdt > 0f ? (pos.y - lastPos.y) / rdt : 0f;
            smoothVel = Vector3.Lerp(smoothVel, new Vector3(vel.x * gs, vy, vel.z * gs), 1f - Mathf.Exp(-rdt * 12f));
            Velocity = smoothVel; lastPos = pos; lastY = y;
            lastState = st;
        }

        /// <summary>The root's height standing on its four pads: a plane through what is under the pad soles (the ground, or the station's
        /// deck) and its tilt.</summary>
        float FitY(Vector3 p, out Quaternion tilt)
        {
            var yr = Quaternion.Euler(0f, yaw, 0f);
            float mx = 0f, mz = 0f, mg = 0f; var P = fitP; var G = fitG;
            for (int i = 0; i < 4; i++)
            {
                var w = p + yr * PadSoles[i]; P[i] = w; G[i] = game.GroundY(w.x, w.z);
                if (DeckStation >= 0 && deckK > 0.5f && OzStationCrane.DeckY(DeckStation, w, out float dy) && dy > G[i]) G[i] = dy;   // on the station's deck
                mx += w.x; mz += w.z; mg += G[i];
            }
            mx *= 0.25f; mz *= 0.25f; mg *= 0.25f;
            float sxx = 0f, szz = 0f, sxz = 0f, sxg = 0f, szg = 0f;
            for (int i = 0; i < 4; i++) { float dx = P[i].x - mx, dz = P[i].z - mz, dg = G[i] - mg; sxx += dx * dx; szz += dz * dz; sxz += dx * dz; sxg += dx * dg; szg += dz * dg; }
            float det = sxx * szz - sxz * sxz, a = 0f, b = 0f;
            if (Mathf.Abs(det) > 1e-6f) { a = (sxg * szz - szg * sxz) / det; b = (szg * sxx - sxg * sxz) / det; }
            var nrm = new Vector3(-a, 1f, -b).normalized;
            if (Vector3.Angle(Vector3.up, nrm) > 8f) nrm = Vector3.up;                               // too steep to read as a plane: level
            tilt = Quaternion.FromToRotation(Vector3.up, nrm);
            return mg + a * (p.x - mx) + b * (p.z - mz);
        }

        /// <summary>Shot down: until the drawn kill moment (OzShuttleBreakup) it drifts on with its last velocity, slowing down.</summary>
        void DyingPose(float dt)
        {
            dyingT += dt;
            float k = Mathf.Clamp01(1f - dyingT / 1.6f);
            dyingPos += dyingVel * (k * dt);
            var gy = game.GroundY(dyingPos.x, dyingPos.z);
            if (dyingPos.y < gy) dyingPos.y = gy;
            transform.position = dyingPos;
            if (dyingT > 2.5f && !BrokenUp)
            {   // the break-up never came (no pieces): the old whole fall, and the proof reads the error
                Debug.LogError("[OpenZ] shuttle: destroyed but no break-up within 2.5 s: the whole ship falls");
                dying = false; crashFrom = transform.position; crashT = 0f; crashing = true;
            }
        }

        // ================================================================== LateUpdate 200: effects, sounds, voices
        void LateUpdate()
        {
            if (game == null || game.World == null || Obj == null) return;
            var w = game.World; float rdt = Mathf.Min(Time.deltaTime, 0.05f), dt = game.paused ? 0f : rdt * Mathf.Max(0.05f, game.gameSpeed);
            KickflipDeathBake();                                                                            // K420: destroyed after the kickflip gate: the flip ends, its drawn pose kept
            if (crashing) { CrashStep(rdt); return; }
            if (BrokenUp) return;
            if (dying) { TouchesDying(rdt, dt); return; }                                                 // K256g2: the mark follows the drifting ship (OzPlayableShuttle.Touches.cs)
            if (Obj.Destroyed || w.Get(Obj.Id) != Obj) return;
            OzStationCrane.Tick(game);                                                             // the repair stations' cranes (once a frame for every ship)
            if (!skyStarted && Time.timeSinceLevelLoad > 1f) { skyStarted = true; sky.BeginBuild(); }   // the clearance grid over the next frames (no hitch at the first flight)
            sky.BuildStep();
            bool air = Obj.ShuttleAirborne; float lift = (float)Obj.ShuttleLift;
            KickflipBeforeJets(dt);                                                                // K344: the double right-click's kickflip: this frame's click, one step of the flip, its pose for the jets (OzPlayableShuttle.Kickflip.cs)
            jets.Step(dt, Obj.ShuttleState, lift, air, FeetHeight, Velocity);
            KickflipAfterJets(dt);                                                                 // K344: the main jets' flare and the attitude puffs (on screen only), the flip's sounds; level again for all below
            wash.Step(dt, FeetHeight, Velocity, jets.Throttle);
            TouchesShadows(rdt, dt);                                                                     // K256g: the ground share eased; the ship's own shadow (none in the air) and the landing mark crossed on it (OzPlayableShuttle.Touches.cs)
            search.Night?.Step(dt);                                                                // first: the landing lights' aim and rule, the nose searchlight, the ramp light
            search.Step(dt, air, FeetHeight);
            if (air || jets.Throttle > 0.01f)
            {
                OzFloraDownwash.ForShip(FeetHeight, out float fr, out float fk);
                var c = transform.position; c.y = game.GroundY(c.x, c.z);
                OzFloraDownwash.Set(c, fr, fk * Mathf.Clamp01(jets.Throttle / 0.7f), Obj.Id);
            }
            if (exhaust && exhaust.enabled == air) exhaust.enabled = !air;                         // the parked wisp only while it stands
            if (lights)
            {
                float upk = Mathf.Clamp01(lift * 3f);
                lights.enginePeriod = Mathf.Lerp(enginePeriod0, 0.35f, upk); lights.engineGain = engineGain0 * Mathf.Lerp(1f, 2.2f, upk);
                lights.groundGlowScale = 1f / (1f + (FeetHeight / 2.5f) * (FeetHeight / 2.5f));          // the grass does not glow under a ship in the air
            }
            FootGlow();
            OzShuttleBreakup.PrepareStep(transform, Obj.Id);                                       // one cut of the shared break-up plan a frame, from the start
            Strafe();
            Ramp();
            Loops(air);
            KickflipDraw();                                                                        // K344: the flipped pose for the picture (OzShuttleKickflipGate puts the level pose back first thing next frame)
        }

        /// <summary>The strafe's sound: a thruster whoosh when a glide out begins (the rules' ShuttleStrafes counts up), a softer one as it
        /// drifts back (ShuttleStrafeReturns).</summary>
        void Strafe()
        {
            GlideWatch();                                                                            // K256e: each glide's metres, seconds, peak speed (OzPlayableShuttle.Flight.cs)
            if (Obj.ShuttleStrafes != lastStrafes)
            {
                if (Obj.ShuttleStrafes > lastStrafes && AudioDirector.Has("shuttle_strafe_whoosh")) { AudioDirector.Fire("shuttle_strafe_whoosh", transform.position, Obj.Id, 1f); LastSound = "shuttle_strafe_whoosh"; StrafeWhooshes++; }
                lastStrafes = Obj.ShuttleStrafes;
            }
            if (Obj.ShuttleStrafeReturns != lastReturns)
            {
                if (Obj.ShuttleStrafeReturns > lastReturns && AudioDirector.Has("shuttle_strafe_whoosh")) AudioDirector.Fire("shuttle_strafe_whoosh", transform.position, Obj.Id, 0.7f, 0.9f);   // K256e: 0.5 -> 0.7 (the drift back over the 0.12 floor)
                lastReturns = Obj.ShuttleStrafeReturns;
            }
        }

        void Loops(bool air)
        {
            var ad = AudioDirector.I; if (!ad) return;
            float T = jets.Throttle; bool on = air || T > 0.05f;
            if (on && !loopOn) { ad.StartLoop(loopKey, "shuttle_hover_loop", transform.position, transform); loopOn = true; }
            else if (!on && loopOn) { ad.StopLoop(loopKey, 0.6f); loopOn = false; }
            if (loopOn) ad.SetLoop(loopKey, 0.6f + 0.4f * Mathf.Clamp01(T), Obj.ShuttleState == ZShuttleState.Cruise ? 1.08f : 1f);
            if (on && !jetLoopOn && AudioDirector.Has("shuttle_jet_burn_loop")) { ad.StartLoop(jetKey, "shuttle_jet_burn_loop", transform.position, transform); jetLoopOn = true; }
            else if (!on && jetLoopOn) { ad.StopLoop(jetKey, 0.6f); jetLoopOn = false; }
            if (jetLoopOn) ad.SetLoop(jetKey, 0.35f + 0.65f * T, 0.9f + 0.15f * T);
        }

        /// <summary>The ramp's voices (its sounds are the door's): "All aboard!" as it closes after a boarding, "Everybody out!" on EXIT.</summary>
        void Ramp()
        {
            var ph = Obj.ShuttleRampPhase;
            if (ph == ZShuttleRamp.Closing && lastRamp != ZShuttleRamp.Closing && Obj.ShuttleLastRampUse == ZShuttleRampUse.Boarded) Voice("vo_shuttle_board");
            lastRamp = ph;
            bool pend = Obj.ShuttleExitPending;
            if (pend && !seenPending) Voice("vo_shuttle_unload");
            seenPending = pend;
        }

        void ReadNotes(ZWorld w)
        {
            foreach (var n in w.ShuttleNotes)
            {
                if (n.seq <= lastSeq) continue;
                lastSeq = n.seq;
                if (n.id != Obj.Id) continue;
                switch (n.kind)
                {
                    case ZShuttleNoteKind.TakeOff: if (BrokenUp) break; Sound("shuttle_spool_up"); Sound("shuttle_takeoff"); jets.TakeOff(); wash.Burst(1f); break;
                    case ZShuttleNoteKind.CannotLand: if (BrokenUp) break; Voice("vo_shuttle_no_land"); AudioDirector.UI("ui_error"); LastSound = "ui_error"; break;
                    case ZShuttleNoteKind.Landed: if (!BrokenUp && !Obj.Destroyed) Landed(w, n); break;   // K418: destroyed at the touchdown: no landing sound, spring or voice for it
                    case ZShuttleNoteKind.Aborted: roughArmed = false; break;
                    case ZShuttleNoteKind.Crashed: case ZShuttleNoteKind.Wrecked:
                        Down(n.kind == ZShuttleNoteKind.Crashed);
                        break;
                }
            }
        }

        /// <summary>Destroyed (in the air or on the ground): the loops and lights stop now; the pieces come at the drawn kill moment (or
        /// came already: then only the fall's sound).</summary>
        void Down(bool air)
        {
            if (air) Sound("shuttle_crash_fall");
            if (BrokenUp) return;                                                                  // OnBrokenUp has stopped the rest already
            var ad = AudioDirector.I;
            if (ad) { if (loopOn) ad.StopLoop(loopKey, 0.2f); if (jetLoopOn) ad.StopLoop(jetKey, 0.15f); }
            loopOn = jetLoopOn = false;
            jets.Out(); search.Kill();
            if (lights) lights.SetMode(2);
            if (exhaust) exhaust.SetMode(2);
            KickflipDeathBake();                                                                            // K420: a flip on the ship ends here, its drawn pose kept (View/OzPlayableShuttle.KickDeath.cs)
            dying = true; dyingT = 0f; dyingPos = transform.position; dyingVel = Velocity;
        }

        /// <summary>OzShuttleBreakup, the drawn kill moment: the ship is in pieces now.</summary>
        public void OnBrokenUp(bool inAir)
        {
            BrokenUp = true; dying = false;
            var ad = AudioDirector.I;
            if (ad) { if (loopOn) ad.StopLoop(loopKey, 0.2f); if (jetLoopOn) ad.StopLoop(jetKey, 0.15f); }
            loopOn = jetLoopOn = false;
            jets.Out(); search.Kill(); wash.Dispose(); jets.Dispose(); TouchesHandOver(inAir);           // K256g2: the mark and the real shadow hand over to what falls (OzPlayableShuttle.Touches.cs)
            if (lights) lights.SetMode(2);
            if (exhaust) exhaust.SetMode(2);
            if (touch.Active) touch.Stop();
            CrashStop();                                                                             // K256e
        }

        void Landed(ZWorld w, ZShuttleNote n)
        {
            Landings++;
            Sound("shuttle_landing");
            jets.Touchdown();
            bool pad = n.why == ZWorld.ShuttleWhyRepairPad;
            bool rough = Obj.ShuttleRoughLanding && !pad;                                               // the rules' crash landing
            if (!SuspLanded(rough)) SpringLanded(rough);                                                 // K256g3: the touchdown on the visible suspension (OzPlayableShuttle.Suspension.cs); K256g's spring when the rig is not fitted
            wash.Burst(rough ? 2.2f : 1f); wash.Pressed();
            if (rough && !roughArmed) { roughSide = Obj.ShuttleRoughSide >= 0 ? 1f : -1f; touch.Arm(roughSide); }   // its descent was not seen: the rules' side
            roughArmed = false;
            if (rough)
            {
                RoughLandings++;
                var dir = new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
                CrashBegin(dir, (float)(Obj.ShuttleLevelStartAt - Obj.ShuttleTouchdownAt), (float)(Obj.ShuttleLevelledAt - Obj.ShuttleTouchdownAt));   // K256e (OzPlayableShuttle.Crash.cs)
            }
            if (pad) { hotSaid = false; return; }                                                    // on the repair station: no landing line, no joke
            LandingOneVoice(w, n, rough);                                                            // K407: ONE landing voice, the robot's OR Rachel's, never both (View/OzPlayableShuttle.LandingVoice.cs)
        }

        /// <summary>Rachel's landing line: one frame, then while any voice speaks (at most 3.5 s), then 0.3 s, then the announcer's queue
        /// (its ANNOUNCER setting and the row's cooldown apply; the queue holds this line while Alan speaks, and no robot line starts while
        /// it plays).</summary>
        IEnumerator Announce(string line)
        {
            yield return null;
            float t0 = Time.unscaledTime;
            while (AudioDirector.VoiceBusy && Time.unscaledTime - t0 < 3.5f) yield return null;
            float t1 = Time.unscaledTime; while (Time.unscaledTime - t1 < 0.3f) yield return null;
            AudioDirector.Announce("ann_shuttle_landing", line);
            RachelLines++; LastVoice = "ann_shuttle_landing " + line; LastRachelAt = Time.unscaledTime;
        }

        // ------------------------------------------------------------------ the old whole fall: only when the break-up could not be made
        bool crashing; float crashT; Vector3 crashFrom;
        void CrashStep(float dt)
        {
            crashT += dt;
            float k = Mathf.Clamp01(crashT / 1.4f);
            var p = crashFrom; float gy = game.GroundY(p.x, p.z);
            transform.position = new Vector3(p.x, Mathf.Lerp(crashFrom.y, gy, k * k), p.z);
            transform.rotation = Quaternion.AngleAxis(240f * dt, transform.forward) * Quaternion.AngleAxis(120f * dt, Vector3.up) * transform.rotation;
            if (FxManager.I && ((int)(crashT * 20f)) % 2 == 0) FxManager.I.Burn(transform.position, 0.6f);
            if (k >= 1f)
            {
                if (FxManager.I) { FxManager.I.Explosion(transform.position, 2.2f); FxManager.I.DemolitionDust(transform.position, 6f, 6f); }
                crashing = false;
                gameObject.SetActive(false);
            }
        }

        void Sound(string id) { LastSound = id; AudioDirector.Fire(id, transform.position, Obj != null ? Obj.Id : -1); }
        void Voice(string id) { LastVoice = id; if (id == "vo_shuttle_landed" && (crashOn || roughArmed)) CrashLandedLines++; AudioDirector.Say(id); }

        /// <summary>For the cursor (the "cannot land here" mark): can the selected shuttle land at this world point, and why not.</summary>
        public static bool LandableAt(ZWorld w, ZObject s, Vector3 world, out string why)
        {
            why = "";
            if (w == null || s == null || !s.IsShuttle) return true;
            var (x, y) = w.ShuttleClamp(Mathf.RoundToInt(world.x * 8f), Mathf.RoundToInt(-world.z * 8f));
            return w.ShuttlePadFree(x, y, s, out why);
        }
    }
}
