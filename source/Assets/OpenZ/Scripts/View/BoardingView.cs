using System;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace OpenZ
{
    /// <summary>
    /// Boarding animations, view side only: the Zod sim decides WHEN a robot enters or leaves (zserver.cpp:1375-1421
    /// RobotEnterObject, zserver_events.cpp:631-731 exit_vehicle_event); this component only shows HOW (crew timing and boarding
    /// anchors as in our earlier projects):
    ///   roof hatch (light / heavy / missile_launcher; medium from its bounds): walk to *_Board_Approach, step to
    ///     *_Board_LadderBottom, climb_ladder to *_Board_LadderTop, the *_HatchHinge swings open, walk to *_Board_HatchTop,
    ///   jeep: step to the side next to DriverSeat and hop in (jump_takeoff -> seated_driver); the driver stays visible.
    ///   cannons: walk behind CannonGunnerSeat and sit (seated_gunner); the gunner stays visible while the gun is crewed.
    ///   APC / crane (imported, no anchors): rear door / side cab derived from the model bounds; the APC takes the whole squad.
    ///   exits (APC and ejectable cannons, the only Zod ejections) run the same path backwards to the new robot.
    /// Zod enters and ejects in the same tick and never waits, so nothing here blocks the sim: the boarding robot is a
    /// ghost copy (the real UnitView is hidden by CombatView as before), the ejected robot's real view stays in place (only
    /// its renderers hide) while a ghost climbs out to it, every animation fits a short budget (enter 0.7-1.3 s, about the
    /// 1.1 s a grunt would need to walk the last 2 m from the footprint edge Zod enters at to the seat) and is squeezed into
    /// <see cref="catchUp"/> as soon as the sim makes the vehicle act (moves, fires) or the new robot moves or fires.
    /// Robots walk round the hull corners instead of through it (up to <see cref="detourTime"/> longer), vault up a flank
    /// (never over the barrel), hop onto a raised turret hatch, keep their chest / shoulder team band and follow ZGame's
    /// game speed (4x bot match, pause).
    /// Extras from Zod: the tank lid (ZObject.LidOpen, zvehicle.cpp:198-240) opens the hatch and the commander pops out
    /// (only then can a tank be sniped); a sniped driver's body is thrown out (ERobotTurrent, zplayer_events.cpp:1117-1134).
    /// Reads World.Events in Update (after ZGame ticks, before CombatView clears them in LateUpdate) and poses everything in
    /// LateUpdate after UnitView (order 60), so anchors follow the moving, tilting, recoiling vehicles.
    /// </summary>
    [DefaultExecutionOrder(70)]
    public partial class BoardingView : MonoBehaviour                                    // DV87: + BoardingView.DriverFire.cs (the driver's own shots)
    {
        public static BoardingView I { get; private set; }

        /// <summary>vehicles_nb_v1 NB24: the rifle muzzle of vehicle `id`'s hatch driver while he is up (the crane's DV31 shots leave it).</summary>
        public static bool TryCrewMuzzle(int id, out Vector3 p)
        {
            p = default;
            if (!I || I.game == null || !I.game.Views.TryGetValue(id, out var v) || !v || !I.rigs.TryGetValue(v, out var r)) return false;
            if (!r.crew || !r.crew.activeInHierarchy || r.crewPop < 0.5f) return false;
            if (!r.crewMuzzle) r.crewMuzzle = FindMuzzle(r.crew);
            if (!r.crewMuzzle) return false;
            p = r.crewMuzzle.position; return true;
        }
        /// <summary>
        /// commander at least half out of the hatch), where small-arms rounds land (CrewAim). False = no visible crew figure.</summary>
        public static bool TryCrewChest(int id, out Vector3 p)
        {
            p = default;
            if (!I || I.game == null || !I.game.Views.TryGetValue(id, out var v) || !v || !I.rigs.TryGetValue(v, out var r)) return false;
            if (!r.crew || !r.crew.activeInHierarchy || (r.lid && r.crewPop < 0.5f)) return false;
            if (r.crewSkin && r.crewSkin.enabled)
            {
                var b = r.crewSkin.bounds;
                if (b.size.y > 0.2f && b.size.y < 4f) { p = new Vector3(b.center.x, b.max.y - 0.3f * b.size.y, b.center.z); return true; }
            }
            if (!r.crewHips) return false;
            p = r.crewHips.position + Vector3.up * 0.3f * Mathf.Max(0.3f, r.crewBase); return true;
        }
        /// <summary>Adds itself to the ZGame object of every loaded scene (set false to add it by hand).</summary>
        public static bool AutoAttach = true;
        /// <summary>crews_v2 counters for CannonExitCheck: exit animations started / played to the end (not aborted).</summary>
        public int ExitsStarted, ExitsDone;

        [Header("Budgets, seconds (Zod boards / ejects in one tick: the view never blocks the sim)")]
        public float roofEnter = 1.3f;
        public float roofExit = 1.1f, seatEnter = 0.8f, seatExit = 0.7f, cannonEnter = 0.7f, cannonExit = 0.6f, doorEnter = 0.9f, doorExit = 0.8f;
        [Tooltip("once the sim makes the vehicle (or an ejected robot) act, whatever is left is squeezed into this")]
        public float catchUp = 0.2f;
        [Tooltip("APC squads board / leave one after the other")] public float crewStagger = 0.15f;
        [Tooltip("at most this much extra time for walking round the hull (never through it) when the robot stands on the far side")] public float detourTime = 1f;

        [Header("Motion (natural speeds; a path longer than its budget plays faster)")]
        public float runSpeed = 3.2f;
        public float climbSpeed = 5f, deckSpeed = 3f, dropTime = 0.22f, hopTime = 0.34f, sitTime = 0.26f;
        [Tooltip("Legacy rig: hatch.rotation.x -> -1.65 rad; the sign is picked per model so the lid swings up")] public float hatchOpenDeg = 95f;
        public float hatchOpenRate = 6f, hatchCloseRate = 4f;
        [Tooltip("fallback only (the hips bone is measured live): seated hips above the Z_robot root = other-bot.glb crewPelvisOffset 1.132 + prefab model lift 0.379")]
        public float pelvisHeight = 1.51f;
        [Tooltip("fallback only: standing hips above the Z_robot root (bind pose 1.78)")] public float hipHeight = 1.78f;

        [Header("Crew visibility")]
        [Tooltip("Jeep driver and cannon gunners stay visible while crewed")] public bool showSeatedCrew = true;
        [Tooltip("Zod tank lid: the commander pops out of the hatch while the lid is open")] public bool showLidCommander = true;
        [Tooltip("where the hatch rim cuts the commander, 0 = hips, 1 = shoulder joints (the driver visible from the chest up with his weapon)")]
        [Range(0f, 1.2f)] public float commanderCut = 0.5f;
        [Tooltip("Zod ZVehicle::ProcessLid (zvehicle.cpp:243-264, 0.2 s steps): lid frames 0 -> 2 = 0.4 s, then the robot shows; closing hides the robot first, then the lid 2 -> 0 = 0.4 s")]
        public float lidOpenTime = 0.4f;
        public float lidCloseTime = 0.4f, commanderRiseTime = 0.2f, commanderSinkTime = 0.15f;
        [Tooltip("the popped-out commander fires his own weapon with each shot the vehicle shows (view only, the sim's shot is the vehicle's)")] public bool commanderFires = true;
        [Tooltip("after the vehicle's shot (s)")] public float commanderShotDelay = 0.07f;
        [Tooltip("his shot sound under the vehicle's own")] public float commanderShotGain = 0.55f;
        [Tooltip("Zod ERobotTurrent: a sniped driver's body is thrown out")] public bool bodyOnSnipe = true;
        [Tooltip("jeep crew sits behind the gun (GunnerSeat, seated_gunner) instead of at the wheel")] public bool jeepGunnerSeat;
        [Tooltip("hatch clunks and a seat / door clank once the crew is in (clips already in Resources/OpenZ/Sfx; CombatView plays the capture and engine start)")] public bool sounds = true;

        const float RobotY = -0.08f;                 // UnitView.yOffset for robots
        const float WalkClipSpeed = 1.75f;           // walking clip at speed 1 = grunt 14 px/s (UnitView SpeedOffsetPercent)
        const float RunClipSpeed = 3.15f;            // run_forward at speed 1 = 14 px/s x run 1.8

        enum Mode { Roof, Seat, Cannon, Door }
        enum Face { Move, Hull, Seat, Keep, Aisle }

        class Rig
        {
            public UnitView view; public ZObject obj; public Transform root;
            public Mode mode; public bool rearDoor = true, visible, lid; public float crewScale = 0.72f;
            public Transform approach, ladderBottom, ladderTop, hatchTop, boardSeat, driverSeat, gunnerSeat, cabin, hinge, turret;
            public Bounds box;                                   // model bounds in root space (front = -z)
            public Transform model; public Bounds mbox; public GenSpots gen; public bool standGunner;   // imported models: spots in model space
            public Quaternion hingeRest; public float hingeSign = -1f, hatch; public bool hatchWant;
            public bool hatchOnTurret; public Vector3 hatchInTurret;          // heavy / medium: the hatch sits on the turret, the anchor on the hull
            public GameObject crew; public Animator crewAnim; public Transform crewWeapon, crewHips; public SkinnedMeshRenderer crewSkin; public float crewBase = 1f, crewPop, flinch;
            public string crewClip; public Team crewTeam; public string crewType = "grunt";
            public int boarding; public bool acted; public int seenFrame;
            public int apcSeat; public float apcLastFoot = -10f;
            // ui_combat_v1 hatch commander: body heights (own units), aim, recoil, shot sync with the vehicle's visible shots
            public float cmdHips = -1f, cmdShoulder, cmdTop, kick, lastFireSeen = -100f, shotAt = -1f;
            public Quaternion crewAim; public bool crewAimSet; public Transform crewMuzzle; public WeaponHold crewHold; public string crewKey;
            // crews_v1: split imported cannons carry the gunner with the turret (seat + facing in turret space, the prefab's rest pose);
            // a standing gunner keeps his feet on the base plate (MeshTopSampler); guns placed at run time are manned animated
            public bool seatOnTurret, bornLive, everCrewed, feetInit; public Vector3 seatInTurret, frontInTurret;
            public bool turretRestInit; public Vector3 turretRestPos; public Quaternion turretRestRot;
            public RoundHatch round; public bool lidAjar, restLid; public bool lidSeated = true, lidSeatOk;           // hatch_v1: the round hatch (its collar top = the rim); a sniped tank keeps its lid open while empty
            public Transform baseT; public MeshTopSampler baseTop; public float seatBaseY, feetY;
            public TeamPaint gunPaint, crewPaint; public Team gunPaintTeam; public float gunPaintCharred = -1f;   // gunner parented under the gun
            public bool groundStand;                                                               // crews_v2: howitzer gunner on the ground / tower top
        }

        class Key
        {
            public Func<Vector3> at; public float dur; public string clip, clipLate; public float clipSpeed = 1f, arc;
            public Face face; public bool ground, ease, hatch, walk;
        }

        class Ghost
        {
            public GameObject go; public Animator anim; public Transform weapon, hips; public SkinnedMeshRenderer skin; public Rig rig; public UnitView live;
            public readonly List<Renderer> hidden = new List<Renderer>();
            public readonly List<Key> keys = new List<Key>();
            public bool enter, hurry, done, demo; public int robotId = -1;
            public float detour;                                                  // metres added by walking round the hull
            public float delay, time, total, speedMul = 1f, baseScale = 1f, scaleA = 1f, scaleB = 1f, scaleSpan = 1f, backAt = -1f, backSpan = 1f;
            public float fitA = 1f, fitB = 1f, fitT0, fitT1 = -1f, fitBackT0 = -1f, fitBackT1, stowA, stowB;   // electrobot_v1: the type's crew fit window (scale x, coil stow)
            public Quaternion rot; public string clipNow;
            public float scaleT0, footT = -1f; public int seatK = -1;
        }

        /// <summary>Boarding spots of the imported models (no anchors), as fractions of the model's own mesh bounds in the "Model"
        /// child's space, so they stay on the geometry if the Model child is re-rotated or re-scaled. imported models face model +X.
        /// </summary>
        public class GenSpots
        {
            public bool hasHatch; public Vector3 hatch; public float deck = -1f;                    // medium tank: round lid on the turret, hull deck
            public bool hasSeat, stand; public Vector3 seat;                                     // cannons: hips on the chair, or the feet of a standing gunner
            public float ground; public bool saddle;                                              // crews_v2: stand on the ground `ground` m from the pivot / build a GunnerSaddle
            // gunner_seats_v1: `seat` is the hips socket in TurretYaw space (prefab units), he faces the turret's +x (the barrel); the
            // seat + platform are modelled on the TurretYaw mesh; deck top `deckDrop` under the hips; he climbs on / off along `stepOut`;
            public bool turretSeat; public float deckDrop; public Vector3 stepOut; public Vector3 face = Vector3.right;
            public bool hasDoor, sideDoor; public Vector3 door, doorNormal, inside;              // APC rear door, crane cab door (the other side mirrors z)
        }

        public static readonly Dictionary<string, GenSpots> Gen = new Dictionary<string, GenSpots>
        {
            ["medium"] = new GenSpots { hasHatch = true, hatch = new Vector3(0.333f, 0.732f, 0.536f), deck = 0.394f },         // roof 2.6 m (antenna tip 3.55 m)
            ["apc"] = new GenSpots { hasDoor = true, door = new Vector3(0f, 0f, 0.5f), doorNormal = Vector3.left, inside = new Vector3(0.279f, 0.25f, 0.5f) },
            ["crane"] = new GenSpots { hasDoor = true, sideDoor = true, door = new Vector3(0.667f, 0f, 0f), doorNormal = Vector3.back, inside = new Vector3(0.667f, 0.02f, 0.386f),
                                          hasHatch = true, hatch = new Vector3(0.689f, 0.537f, 0.702f) },   // vehicles_nb_v1 NB24: cab roof hatch beside the boom (RoundHatch "crane")
            // socket below in TurretYaw space. Checked offline with all six seated crews (grunt + the fat v3 psycho / tough / pyro / sniper /
            // laser): >= 4.9 cm from the static base at every turret yaw (10 deg sweep), >= 10 cm from the barrel over its pitch range (+kick,
            // howitzer recoil), >= 10 cm from the turret, everything above the gun root (tower plinth). Replaces the crews_v2 outrigger saddle
            // (gatling), the ground stand 2.45 m out (howitzer) and the stand on the static base plate (missile cannon).
            ["gatling"] = new GenSpots { hasSeat = true, turretSeat = true, seat = new Vector3(-0.75f, 0.90f, 1.36f), deckDrop = 0.715f, stepOut = new Vector3(0f, 0f, 1f) },          // beside the drum, levers at the yoke
            ["howitzer"] = new GenSpots { hasSeat = true, turretSeat = true, seat = new Vector3(0.10f, 0.95f, 1.62f), deckDrop = 0.715f, stepOut = new Vector3(0f, 0f, 1f) },         // the layer's seat on the carriage side at the trunnion
            ["missile_cannon"] = new GenSpots { hasSeat = true, turretSeat = true, seat = new Vector3(-2.42f, 0.15f, 0f), deckDrop = 0.715f, stepOut = new Vector3(-1f, 0f, 0f) },    // on a boom platform behind the pods
        };

        /// <summary>
        /// seated crews: >= 9.9 cm from the turret, >= 20 cm from the barrel over -15..+40 deg (+kick, recoil), the static pedestal
        /// >= 57 cm at any yaw, everything >= 1.05 m over the gun root (the tower plinth).</summary>
        static readonly GenSpots LegacyTowerPerch = new GenSpots { hasSeat = true, turretSeat = true, seat = new Vector3(0f, 1.90f, 1.56f), face = new Vector3(0f, 0f, -1f), deckDrop = 0.715f, stepOut = new Vector3(0f, 0f, 1f) };

        class Corpse { public GameObject go; public RobotRagdoll rd; public Animator anim; public float age, T; public Vector3 a, b, axis; public Quaternion r0; }

        ZGame game;
        Transform box;                                                       // container for ghosts, crews and bodies
        readonly Dictionary<UnitView, Rig> rigs = new Dictionary<UnitView, Rig>();
        readonly List<Ghost> ghosts = new List<Ghost>();
        readonly List<Corpse> corpses = new List<Corpse>();
        readonly HashSet<int> boarded = new HashSet<int>();
        readonly HashSet<int> exited = new HashSet<int>();                  // an event list that was not cleared never replays an exit
        readonly HashSet<int> enteredNow = new HashSet<int>();
        /// <summary>
        /// (AudioDirector.UpdateVehicles leaves them alone; it used the sim's LidOpen, so a snipe played the close while the emptied
        /// tank's lid stayed open).</summary>
        public static readonly HashSet<int> LidVoiced = new HashSet<int>();
        /// <summary>DV75 idle rest: the gain of the lid and hatch sounds when the hatch opens or shuts for a resting crew (quiet),
        /// and the resting driver's slow look-around (degrees to each side, radians per second).</summary>
        public static float RestLidGain = 0.45f, RestLookDeg = 35f, RestLookRate = 0.45f;
        static readonly int AccentLoId = Shader.PropertyToID("_AccentLo"), AccentHiId = Shader.PropertyToID("_AccentHi");
        MaterialPropertyBlock bandBlock;

        /// <summary>Zod time: the sim runs at ZGame.gameSpeed (4x in the bot match) and stops when paused; the crew keeps pace.</summary>
        float SimRate => !game ? 1f : game.paused ? 0f : Mathf.Max(0.05f, game.gameSpeed);
        readonly List<(ZObject robot, ZObject vehicle)> exitsNow = new List<(ZObject, ZObject)>();
        readonly List<UnitView> dropList = new List<UnitView>();

        // ------------------------------------------------------------------ install
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnScene; SceneManager.sceneLoaded += OnScene; Attach(); }
        static void OnScene(Scene s, LoadSceneMode m) => Attach();
        static void Attach()
        {
            if (!AutoAttach) return;
            var g = ZGame.I ? ZGame.I : Object.FindFirstObjectByType<ZGame>();
            if (g && !g.GetComponent<BoardingView>()) g.gameObject.AddComponent<BoardingView>();
        }

        void Awake()
        {
            I = this;
            game = GetComponent<ZGame>(); if (!game) game = ZGame.I;
            box = new GameObject("OZ_BoardingCrew").transform;
        }

        void OnDestroy()
        {
            foreach (var g in ghosts) ShowLive(g);
            foreach (var r in rigs.Values) if (r.crew) Destroy(r.crew);                                    // crews_v1: gunners parented under their guns
            LidVoiced.Clear();
            if (box) Destroy(box.gameObject);
            if (I == this) I = null;
        }

        UnitView View(int id) => id >= 0 && game.Views.TryGetValue(id, out var v) ? v : null;

        void Sfx(string key, Vector3 pos, float vol, int emitter, params string[] clips)
        {
            if (sounds && CombatAudio.I) CombatAudio.I.Play(key, pos, vol, 0.3f, emitter, clips);
        }

        // ------------------------------------------------------------------ sim events (read only, CombatView clears them)
        void Update()
        {
            if (!game) game = ZGame.I;
            if (!game || game.World == null) return;
            var ev = game.World.Events;
            enteredNow.Clear(); exitsNow.Clear();
            for (int i = 0; i < ev.Count; i++) if (ev[i].type == ZEventType.Entered) enteredNow.Add(ev[i].objId);
            for (int i = 0; i < ev.Count; i++)
            {
                var e = ev[i];
                switch (e.type)
                {
                    case ZEventType.Entered:
                        if (!e.flag) { var cv = game.World.GetAny(e.otherId); if (cv != null) CaptureHold.Begin(cv, Team.Neutral); }   // boarding_nb_v1 NB17 (a DV15 passenger boarding is no capture)
                        OnEntered(e.objId, e.otherId); break;
                    case ZEventType.Created:
                    {
                        var a = game.World.GetAny(e.objId); var b = game.World.GetAny(e.otherId);
                        if (a != null && b != null && a.Kind == ZKind.Robot && (b.Kind == ZKind.Vehicle || b.Kind == ZKind.Cannon)) exitsNow.Add((a, b));
                        break;
                    }
                    case ZEventType.Sniped: { var r = RigOf(View(e.objId)); if (r != null) Snipe(r, e.otherId); break; }   // otherId = the sniper
                    case ZEventType.DriverHit: { var r = RigOf(View(e.objId)); if (r != null) r.flinch = 0.25f; break; }
                    case ZEventType.Death:
                    {
                        var v = View(e.objId);
                        if (v && v.Obj != null && (v.Obj.Kind == ZKind.Vehicle || v.Obj.Kind == ZKind.Cannon)) { if (v.Obj.IsShuttle) ShuttleDoorwayTake(v.Obj.Id); var r = RigOf(v); if (r != null && !CrewDiesLater(e, v, r)) CrewDies(r); }   /* K418: the ramp's file first: saved figures walk on, lost ones are held (BoardingView.ShuttleDoorway.cs) */   /* version 1.1 item 202: with its held explosion (BoardingView.Kc5b.cs) */
                        break;
                    }
                    case ZEventType.DriverShot: DriverShotSeen(e); break;                                       // DV87: the hatch driver of a tank fired his own weapon (BoardingView.DriverFire.cs)
                    case ZEventType.Hitscan: case ZEventType.MissileFired: Acted(e.objId, true); Acted(e.otherId, false); break;
                }
            }
            // ejected crews: one ghost per robot, the squad leaves one after the other
            for (int i = 0, n = 0; i < exitsNow.Count; i++)
            {
                n = i > 0 && exitsNow[i].vehicle == exitsNow[i - 1].vehicle ? n + 1 : 0;
                var r = RigOf(View(exitsNow[i].vehicle.Id));
                if (r != null) StartExit(r, exitsNow[i].robot, n);
            }
        }

        /// <summary>A shot by (or at) an ejected robot, or by a vehicle being boarded: the sim is acting, catch up.</summary>
        void Acted(int id, bool shooter)
        {
            var v = View(id); if (!v || v.Obj == null) return;
            if (v.Obj.Kind == ZKind.Robot) { foreach (var g in ghosts) if (g.live == v && !g.demo) g.hurry = true; return; }
            if (shooter && rigs.TryGetValue(v, out var r)) r.acted = true;
        }

        void OnEntered(int robotId, int vehicleId)
        {
            var r = RigOf(View(vehicleId)); if (r == null) return;
            var robot = game.World.GetAny(robotId);
            StartEnter(r, robotId, robot, 0f);
            // Zod: an APC takes the whole squad wherever it stands (zserver.cpp:1399-1411); they are KillAt == 0 like the leader
            if ((r.obj.Type != "apc" && !r.obj.IsShuttle) || robot == null) return;   // DV118: the shuttle takes the whole squad up its ramp too
            int k = 1;
            foreach (var kv in game.Views)
            {
                var rv = kv.Value; if (!rv || rv.Obj == null || !rv.gameObject.activeInHierarchy) continue;
                var o = rv.Obj;
                if (o.Kind != ZKind.Robot || o.KillAt != 0 || o.Id == robotId || o.Owner != robot.Owner || boarded.Contains(o.Id) || enteredNow.Contains(o.Id)) continue;
                StartEnter(r, o.Id, o, crewStagger * k++);
            }
        }

        // ------------------------------------------------------------------ rigs: anchors by name, fallbacks from the bounds
        Rig RigOf(UnitView v)
        {
            if (!v || v.Obj == null) return null;
            if (rigs.TryGetValue(v, out var r)) return r;
            var o = v.Obj;
            if (o.Kind != ZKind.Vehicle && o.Kind != ZKind.Cannon) return null;
            r = new Rig { view = v, obj = o, root = v.transform, crewTeam = o.Owner, crewType = string.IsNullOrEmpty(o.DriverType) ? "grunt" : o.DriverType };
            r.round = RoundHatch.Ensure(v.transform, o.Type);                                // hatch_v1: round hatch + lid on the hinge before the hinge is read
            UnitLookMotionGate.KeepUnitCrisp(v.gameObject);                                              // the round hatch and its lid are made here, after the bind: out of the motion blur at once (a new tank drives out of the factory door in these frames)
            foreach (var t in v.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (Is(n, "Board_Approach")) r.approach = t;
                else if (Is(n, "Board_LadderBottom")) r.ladderBottom = t;
                else if (Is(n, "Board_LadderTop")) r.ladderTop = t;
                else if (Is(n, "Board_HatchTop")) r.hatchTop = t;
                else if (Is(n, "Board_Seat")) r.boardSeat = t;
                else if (Is(n, "HatchHinge")) r.hinge = t;
                else if (Is(n, "CabinFloor")) r.cabin = t;
                else if (!r.driverSeat && (n.EndsWith("DriverSeat") || n.EndsWith("PilotSeat"))) r.driverSeat = t;
                else if (!r.gunnerSeat && n.EndsWith("GunnerSeat")) r.gunnerSeat = t;
                else if (!r.turret && n.Contains("TurretYaw") && !n.Contains("_Surface")) r.turret = t;
            }
            r.box = LocalBox(v.transform);
            switch (o.Type)
            {
                case "light": r.mode = Mode.Roof; r.crewScale = 0.67f; r.lid = true; break;
                case "medium": r.mode = Mode.Roof; r.crewScale = 0.7f; r.lid = true; break;
                case "heavy": r.mode = Mode.Roof; r.crewScale = 0.62f; r.lid = true; break;
                case "missile_launcher": r.mode = Mode.Roof; r.crewScale = 0.62f; break;
                case "jeep": r.mode = Mode.Seat; r.crewScale = 0.62f; r.visible = true; break;
                case "apc": r.mode = Mode.Door; r.rearDoor = true; r.crewScale = 0.85f; r.lid = true; break;                   // DV87: the roof hatch its lone driver comes up through (RoundHatch "apc")
                case "crane": r.mode = Mode.Door; r.rearDoor = false; r.crewScale = 0.8f; r.lid = true; break;   // vehicles_nb_v1 NB24: the cab hatch driver (DV31)
                case ZWorld.ShuttleType: r.mode = Mode.Door; r.rearDoor = true; r.crewScale = 1f; break;   // DV118: the shuttle's rear ramp (ShuttleRearDoor; the bay's own fit)
                default:
                    if (o.Kind == ZKind.Cannon) { r.mode = Mode.Cannon; r.crewScale = 0.72f; r.visible = true; }
                    else { r.mode = r.hatchTop ? Mode.Roof : r.driverSeat ? Mode.Seat : Mode.Door; r.crewScale = 0.72f; r.visible = r.mode == Mode.Seat; }
                    break;
            }
            if (r.mode == Mode.Seat && !r.driverSeat && !r.gunnerSeat) r.mode = Mode.Door;   // a jeep model without seats boards like a cab
            // imported models (no anchors): measured spots in the Model child's space
            r.model = v.transform.Find("Model"); if (!r.model) r.model = v.transform;
            // (top 0.24 m under the gun top) is hidden whenever the gun faces the camera. There he rides a raised perch over that chair
            // (GunnerPerch: seat, deck, grips, CannonTurretYaw-local), head 0.8 m (x0.72) over the housing. Ground guns keep the chair.
            if (o.Type == "gun" && o.Kind == ZKind.Cannon && v.OnTower && r.turret && GunnerPerch.Build(r.turret, v))
            { r.gunnerSeat = null; LegacyTowerPerch.seat = GunnerPerch.Seat; r.gen = LegacyTowerPerch; }
            if (Gen.TryGetValue(o.Type, out var ms) && !r.hatchTop && !r.gunnerSeat && !r.driverSeat)
            {
                r.gen = ms; r.mbox = LocalBox(r.model);
                r.standGunner = r.mode == Mode.Cannon && ms.hasSeat && ms.stand;
            }
            // crews_v1: the gunner fits the gun (tower cannons are drawn at 0.72 of the prefab) and turns with a split imported turret;
            // a cannon first seen after the start was placed at run time (fort store, zserver_events.cpp:557-598): its crew mans it
            if (o.Kind == ZKind.Cannon) { r.crewScale *= ScaleRatio(v, o); r.bornLive = game.World.Time > 2.0; }
            if (r.mode == Mode.Cannon && r.gen != null && r.gen.hasSeat && r.turret) CarrySeat(r, o);
            if (r.hinge) PickHingeSign(r);
            // heavy (HatchHinge on the turret, Board_HatchTop on the hull) and the split medium (its lid is on the TurretYaw
            // mesh): the hatch turns with the turret, so the lid commander and the drop point are kept in turret space
            // (captured here, the first frame the vehicle is seen, while its turret still sits at rest)
            bool engaged = o.AttackObject != null && !o.AttackObject.Destroyed;
            if (r.turret && !engaged && (r.gen != null ? r.gen.hasHatch : r.hatchTop && r.hinge && r.hinge.IsChildOf(r.turret) && !r.hatchTop.IsChildOf(r.turret)))
            { r.hatchInTurret = r.turret.InverseTransformPoint(Wp(r, HatchTopL(r))); r.hatchOnTurret = true; }
            rigs[v] = r;
            return r;
        }

        static bool Is(string n, string key) => n == key || n.EndsWith("_" + key);

        // ------------------------------------------------------------------ electrobot_v1 (model track): the per-type crew fit
        /// <summary>
        /// A figure's size in a vehicle is the vehicle's crewScale x its type's fit. The Electrobot (1.13 x a grunt, big
        /// triangle through the turret, the collar or the lid above the rim nor through the collar / roof beside the well, his visible
        /// height at most 1.15 x the grunt's; the jeep: his head under the windscreen frame, only the hands on the wheel; the APC door.
        /// Cannons, the crane cab and the MLRS roof: ElectrobotFitDefault, proven in Play (OpenZ/Electrobot: 5 Crew Fit Check).
        /// Every other type: 1.</summary>
        public static readonly Dictionary<string, float> ElectrobotFit = new Dictionary<string, float>
        { { "light", 0.69f }, { "medium", 0.66f }, { "heavy", 0.69f }, { "jeep", 0.81f }, { "apc", 0.87f } };
        public static float ElectrobotFitDefault = 0.87f;
        /// <summary>Vehicle / cannon types where his coil masts stow (ElectrobotCoils: x0.5 about their bases) while he sits; empty: at
        /// the fits above no seat needs it (measured); kept for tuning after the Play check.</summary>
        public static readonly HashSet<string> ElectrobotStowIn = new HashSet<string>();
        /// <summary>The fit changes over the last two keys of a boarding (the first two of a leave), never faster than this per frame
        /// at 60 fps (a longer window when the keys are short): no pop.</summary>
        public const float FitMaxPerFrame = 0.02f;

        static string FigureType(GameObject go) { var mk = go ? go.GetComponent<RobotV3>() : null; return mk ? mk.type : null; }

        static float CrewFit(Rig r, string type)
        {
            if (type != "electrobot" || r == null || r.obj == null) return 1f;
            return ElectrobotFit.TryGetValue(r.obj.Type, out var f) ? f : ElectrobotFitDefault;
        }
        static float CrewFit(Rig r, GameObject go) => CrewFit(r, FigureType(go));
        static float CrewStow(Rig r, GameObject go) =>
            FigureType(go) == "electrobot" && r != null && r.obj != null && (r.mode == Mode.Seat || r.mode == Mode.Cannon) && ElectrobotStowIn.Contains(r.obj.Type) ? 1f : 0f;
        /// <summary>The hatch commander's clip: the Electrobot rests his forearms on the collar (electro_hatch, no gun shown).</summary>
        static string CommanderClip(Rig r, bool engaged) => FigureType(r.crew) == "electrobot" ? "electro_hatch" : engaged ? "shooting" : "idle";
        static float FitWindow(float from, float to) => Mathf.Abs(to - from) * 1.5f / (FitMaxPerFrame * 60f);   // SmoothStep's peak slope 1.5

        /// <summary>BuildEnter: 1 -> fit over the last two keys (ladder top to the hatch drop, the hop into the seat, the cab door).</summary>
        void FitEnter(Ghost g)
        {
            var r = g.rig; float fit = CrewFit(r, g.go); var keys = g.keys; int n = keys.Count;
            float t0 = 0f; for (int i = 1; i < n - 2; i++) t0 += keys[i].dur;
            float t1 = g.total;
            t0 = Mathf.Max(0f, Mathf.Min(t0, t1 - FitWindow(1f, fit)));
            g.fitA = 1f; g.fitB = fit; g.fitT0 = t0; g.fitT1 = Mathf.Max(t1, t0 + 0.01f);
            g.stowA = 0f; g.stowB = CrewStow(r, g.go);
        }

        /// <summary>BuildExit: fit -> 1 over the first two keys after the seat / hatch (append: the preview's way out after backAt).</summary>
        void FitExit(Ghost g, bool append, int k0)
        {
            var r = g.rig; float fit = CrewFit(r, g.go); var keys = g.keys;
            float t0 = 0f; for (int i = 1; i < k0 && i < keys.Count; i++) t0 += keys[i].dur;
            float t1 = t0; for (int i = k0; i < k0 + 2 && i < keys.Count; i++) t1 += keys[i].dur;
            t1 = Mathf.Max(t1, t0 + FitWindow(fit, 1f), t0 + 0.01f);
            if (append) { g.fitBackT0 = t0; g.fitBackT1 = t1; }
            else { g.fitA = fit; g.fitB = 1f; g.fitT0 = t0; g.fitT1 = t1; g.stowA = CrewStow(r, g.go); g.stowB = 0f; }
        }

        /// <summary>The ghost's type fit now (1 for every other type) and its coil stow.</summary>
        static float FitNow(Ghost g, out float stow)
        {
            float k = g.fitT1 > g.fitT0 ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((g.time - g.fitT0) / (g.fitT1 - g.fitT0))) : (g.time >= g.fitT1 ? 1f : 0f);
            float fit = Mathf.Lerp(g.fitA, g.fitB, k); stow = Mathf.Lerp(g.stowA, g.stowB, k);
            if (g.fitBackT0 >= 0f && g.time > g.fitBackT0)
            {
                float b = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((g.time - g.fitBackT0) / Mathf.Max(0.01f, g.fitBackT1 - g.fitBackT0)));
                fit = Mathf.Lerp(g.fitB, 1f, b); stow = Mathf.Lerp(g.stowB, 0f, b);
            }
            return fit;
        }
        static float FitNow(Ghost g) => FitNow(g, out _);

        static Bounds LocalBox(Transform root)
        {
            var w2l = root.worldToLocalMatrix; bool any = false; var b = new Bounds();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                Bounds lb; Matrix4x4 m;
                if (r is SkinnedMeshRenderer s) { lb = s.localBounds; m = w2l * (s.rootBone ? s.rootBone : s.transform).localToWorldMatrix; }
                else { var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue; lb = mf.sharedMesh.bounds; m = w2l * r.transform.localToWorldMatrix; }
                for (int i = 0; i < 8; i++)
                {
                    var c = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return any ? b : new Bounds(new Vector3(0, 1f, 0), new Vector3(3f, 2f, 4f));
        }

        /// <summary>Open the hatch the way that lifts the lid (the importer may flip the hinge axis).</summary>
        void PickHingeSign(Rig r)
        {
            r.hingeRest = r.hinge.localRotation;
            var mfs = r.hinge.GetComponentsInChildren<MeshFilter>(true);
            if (mfs.Length == 0) return;
            float Lift(float s)
            {
                r.hinge.localRotation = r.hingeRest * Quaternion.AngleAxis(s * hatchOpenDeg, Vector3.right);
                float y = 0; foreach (var mf in mfs) if (mf.sharedMesh) y += r.root.InverseTransformPoint(mf.transform.TransformPoint(mf.sharedMesh.bounds.center)).y;
                return y;
            }
            float up = Lift(1f), down = Lift(-1f);
            r.hinge.localRotation = r.hingeRest;
            r.hingeSign = up >= down ? 1f : -1f;
        }

        // points in root space (recomputed every frame: turret-mounted anchors turn with the turret)
        Vector3 Lp(Rig r, Transform t) => r.root.InverseTransformPoint(t.position);
        Vector3 Wp(Rig r, Vector3 local) => r.root.TransformPoint(local);
        Vector3 C(Rig r) => r.box.center;

        /// <summary>World point of a imported spot (fractions of the model-space mesh bounds).</summary>
        Vector3 MP(Rig r, Vector3 frac) => r.model.TransformPoint(r.mbox.min + Vector3.Scale(frac, r.mbox.size));

        /// <summary>Horizontal world direction the crew faces: the seat anchor's front (-z), a imported model's +X, else the root's front.</summary>
        Vector3 Front(Rig r)
        {
            var up = r.root.up;
            var f = r.seatOnTurret && r.turret ? r.turret.TransformDirection(r.frontInTurret) : r.gen != null ? r.model.right : -r.root.forward;   // crews_v1: turns with the turret
            f = Vector3.ProjectOnPlane(f, up);
            return f.sqrMagnitude > 1e-4f ? f.normalized : -r.root.forward;
        }
        static Quaternion Facing(Vector3 front, Vector3 up) => Quaternion.LookRotation(-front, up);            // robot front = -z

        float DeckY(Rig r)
        {
            if (r.ladderTop) return Lp(r, r.ladderTop).y;
            if (r.gen != null && r.gen.deck > 0f) return r.root.InverseTransformPoint(MP(r, new Vector3(0.5f, r.gen.deck, 0.5f))).y;
            return r.box.min.y + 0.45f * r.box.size.y;
        }

        Vector3 HatchTopL(Rig r)
        {
            if (r.hatchOnTurret && r.turret) return r.root.InverseTransformPoint(r.turret.TransformPoint(r.hatchInTurret));
            Vector3 p;
            if (r.hatchTop) p = Lp(r, r.hatchTop);
            else if (r.gen != null && r.gen.hasHatch) p = r.root.InverseTransformPoint(MP(r, r.gen.hatch));
            else if (r.round && r.round.Frame) p = r.root.InverseTransformPoint(r.round.Frame.position);         // DV87: the APC: the centre of its built roof hatch itself
            else p = new Vector3(C(r).x, r.box.min.y + 0.72f * r.box.size.y, C(r).z + 0.12f * r.box.size.z);   // turret roof, not the antenna tip
            if (r.hinge) p.y = Mathf.Max(p.y, Lp(r, r.hinge).y + 0.05f);                                    // heavy: HatchTop 1.85 sits under its turret hatch (2.31)
            return p;
        }

        /// <summary>Seat anchor (world) + facing for the crew in this vehicle.</summary>
        void SeatPose(Rig r, out Vector3 pos, out Quaternion rot, out string clip)
        {
            Transform s = null; clip = "seated_gunner"; var up = r.root.up;
            Vector3 world = Vector3.zero; bool haveWorld = false; Vector3 local = Vector3.zero;
            switch (r.mode)
            {
                case Mode.Seat:
                    s = jeepGunnerSeat && r.gunnerSeat ? r.gunnerSeat : r.driverSeat ? r.driverSeat : r.gunnerSeat;
                    if (s == r.driverSeat) clip = "seated_driver";
                    local = new Vector3(C(r).x, r.box.min.y + 0.4f * r.box.size.y, C(r).z);
                    break;
                case Mode.Cannon:
                    s = r.gunnerSeat ? r.gunnerSeat : r.driverSeat;
                    if (!s && r.gen != null && r.gen.hasSeat) { world = r.seatOnTurret ? CarriedSeat(r) : MP(r, r.gen.seat); haveWorld = true; if (r.standGunner) clip = "idle"; }   // crews_v1: carried
                    local = new Vector3(C(r).x, r.box.min.y + Mathf.Clamp(0.28f * r.box.size.y, 0.4f, 1f), r.box.max.z - 0.2f * r.box.size.z);
                    break;
                case Mode.Roof:
                    s = r.boardSeat ? r.boardSeat : r.cabin ? r.cabin : r.driverSeat;
                    local = HatchTopL(r) - new Vector3(0, Mathf.Min(1f, 0.45f * r.box.size.y), 0);
                    clip = "seated_driver";
                    break;
                default:
                    world = DoorInside(r, 1f); haveWorld = true;
                    break;
            }
            if (s) { pos = s.position; var f = Vector3.ProjectOnPlane(s.forward, up); rot = f.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(f, up) : r.root.rotation; }
            else { pos = haveWorld ? world : Wp(r, local); rot = Facing(Front(r), up); }
        }

        /// <summary>
        /// Measured from the animated bones every frame, so it holds for any robot size and any seated clip. A standing
        /// gunner (imported howitzer / missile cannon, no seat) has its feet on the spot instead.</summary>
        Vector3 SeatRoot(Rig r, Ghost g)
        {
            SeatPose(r, out var p, out _, out _);
            if (r.standGunner) return p;
            return p - HipsOffset(g.go, g.hips, r.root.up, pelvisHeight * g.baseScale * r.crewScale * FitNow(g));   // electrobot_v1: + the type's fit
        }

        /// <summary>gunner_seats_v1: the gun has a seat on a platform on its turret (GenSpots.turretSeat) he climbs up to.</summary>
        static bool HighSeat(Rig r) => r.mode == Mode.Cannon && r.gen != null && r.gen.turretSeat && r.gen.deckDrop > 0f;

        /// <summary>gunner_seats_v1: where a boarding gunner stands on the platform, `outward` m from under his hips toward the edge he
        /// climbs over (feet on the deck top), world, turning with the turret's yaw.</summary>
        const float EdgeOut = 0.45f;
        Vector3 DeckPoint(Rig r, float outward)
        {
            var l = r.gen.seat - Vector3.up * r.gen.deckDrop + r.gen.stepOut * outward;
            return TurretYawPoint(r, l);
        }

        /// <summary>gunner_seats_v1: ground point beyond the platform's edge, outside the gun's footprint (he climbs on / off there).</summary>
        Vector3 StepGround(Rig r)
        {
            var edge = DeckPoint(r, EdgeOut);
            var dir = Vector3.ProjectOnPlane(DeckPoint(r, EdgeOut + 1f) - edge, r.root.up);
            if (dir.sqrMagnitude < 1e-4f) dir = -r.root.forward; dir.Normalize();
            var p = r.root.InverseTransformPoint(edge); var d = r.root.InverseTransformDirection(dir);
            float t = 0.3f;
            for (float k = 0f; k < 4f; k += 0.05f)
            {
                var q = p + d * k;
                if (Mathf.Abs(q.x - C(r).x) > r.box.extents.x + 0.25f || Mathf.Abs(q.z - C(r).z) > r.box.extents.z + 0.25f) { t = Mathf.Max(0.3f, k + 0.1f); break; }
            }
            return Ground(edge + dir * t);
        }

        /// <summary>Ground point behind the gunner's spot, clear of the gun's bounds (where a cannon crew steps on / off).</summary>
        Vector3 Behind(Rig r)
        {
            if (HighSeat(r)) return StepGround(r);                                                         // gunner_seats_v1
            SeatPose(r, out var sp, out var rot, out _);
            var back = -Vector3.ProjectOnPlane(rot * Vector3.back, r.root.up);
            if (back.sqrMagnitude < 1e-4f) back = r.root.forward; back.Normalize();
            var p = r.root.InverseTransformPoint(sp); var d = r.root.InverseTransformDirection(back);
            float t = 0.6f;
            for (float k = 0f; k < 4f; k += 0.05f)
            {
                var q = p + d * k;
                if (Mathf.Abs(q.x - C(r).x) > r.box.extents.x + 0.25f || Mathf.Abs(q.z - C(r).z) > r.box.extents.z + 0.25f) { t = Mathf.Max(0.6f, k + 0.1f); break; }
            }
            return Ground(sp + back * t);
        }

        static Vector3 HipsOffset(GameObject go, Transform hips, Vector3 up, float fallback) =>
            go && hips ? hips.position - go.transform.position : up * fallback;

        static Transform FindHips(GameObject go)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name.EndsWith("Hips")) return t;
            return null;
        }

        /// <summary>Which side a robot at this world point uses (crane cab doors: either side; the APC rear door: one).</summary>
        float DoorSide(Rig r, Vector3 world)
        {
            float s = r.gen != null && r.gen.hasDoor
                ? r.model.InverseTransformPoint(world).z - r.mbox.center.z
                : r.root.InverseTransformPoint(world).x - C(r).x;
            return s >= 0f ? 1f : -1f;
        }

        static Vector3 Side(Vector3 f, float side) => side > 0f ? new Vector3(f.x, f.y, 1f - f.z) : f;

        /// <summary>World point just outside the door, on the ground.</summary>
        Vector3 DoorOut(Rig r, float side)
        {
            if (r.gen != null && r.gen.hasDoor)
            {
                var n = r.gen.doorNormal; if (r.gen.sideDoor && side > 0f) n.z = -n.z;
                var on = MP(r, r.gen.sideDoor ? Side(r.gen.door, side) : r.gen.door);
                return Ground(on + Vector3.ProjectOnPlane(r.model.TransformDirection(n), r.root.up).normalized * 0.55f);
            }
            var c = C(r); var e = r.box.extents;
            if (r.rearDoor) return Ground(Wp(r, new Vector3(c.x, 0, r.box.max.z + 0.55f)));
            return Ground(Wp(r, new Vector3(c.x + side * (e.x + 0.55f), 0, c.z - 0.2f * r.box.size.z)));    // cab door in the front half
        }

        /// <summary>World point inside the hull where a boarding robot disappears (and an ejected one appears).</summary>
        Vector3 DoorInside(Rig r, float side)
        {
            if (r.gen != null && r.gen.hasDoor) return MP(r, r.gen.sideDoor ? Side(r.gen.inside, side) : r.gen.inside);
            var c = C(r);
            if (r.rearDoor) return Wp(r, new Vector3(c.x, r.box.min.y + 0.25f * r.box.size.y, r.box.max.z - 0.28f * r.box.size.z));
            return Wp(r, new Vector3(c.x + side * 0.12f * r.box.size.x, r.box.min.y + 0.1f * r.box.size.y, c.z - 0.2f * r.box.size.z));
        }

        Vector3 Ground(Vector3 w) { w.y = game.GroundY(w.x, w.z) + RobotY; return w; }

        bool LadderSide(Rig r, Vector3 world)
        {
            if (!r.ladderBottom || !r.ladderTop) return false;
            var c = C(r); var lb = Lp(r, r.ladderBottom); var lp = r.root.InverseTransformPoint(world);
            // the ladder hangs on one flank (tank, heavy, MLRS: +x): from that half of the hull the robot walks round to it,
            // from the other half it vaults up its own flank (never over the barrel or the engine deck)
            if (Mathf.Abs(lb.x - c.x) > 0.25f * r.box.extents.x) return (lb.x - c.x) * (lp.x - c.x) > 0f;
            var a = new Vector2(lb.x - c.x, lb.z - c.z); var b = new Vector2(lp.x - c.x, lp.z - c.z);
            return a.sqrMagnitude > 1e-4f && b.sqrMagnitude > 1e-4f && Vector2.Dot(a.normalized, b.normalized) > 0.35f;
        }

        /// <summary>Vault spot on the flank (x side) nearest to a world point, root space, y = 0, plus its outward normal. Only
        /// the middle half of the flank: the deck is there (front and rear ends carry barrels, engine decks, empty space).</summary>
        Vector3 FlankPoint(Rig r, Vector3 world, float margin, out Vector3 normal)
        {
            var lp = r.root.InverseTransformPoint(world); var c = C(r);
            float sx = lp.x - c.x >= 0f ? 1f : -1f, z = Mathf.Clamp(lp.z - c.z, -0.5f * r.box.extents.z, 0.5f * r.box.extents.z);
            normal = new Vector3(sx, 0, 0);
            return new Vector3(c.x + sx * (r.box.extents.x + margin), 0, c.z + z);
        }

        /// <summary>
        /// y = 0, 0.45 m outside the footprint) a straight walk from a to b has to pass, shortest way round; empty when the
        /// way is clear or one end stands inside the footprint.</summary>
        List<Vector3> Around(Rig r, Vector3 aWorld, Vector3 bWorld)
        {
            var res = new List<Vector3>();
            var c = C(r); float hx = r.box.extents.x - 0.05f, hz = r.box.extents.z - 0.05f;
            var la = r.root.InverseTransformPoint(aWorld); var lb = r.root.InverseTransformPoint(bWorld);
            var A = new Vector2(la.x - c.x, la.z - c.z); var B = new Vector2(lb.x - c.x, lb.z - c.z);
            if (!Crosses(A, B, hx, hz)) return res;
            float gx = r.box.extents.x + 0.45f, gz = r.box.extents.z + 0.45f;
            var k = new[] { new Vector2(gx, gz), new Vector2(gx, -gz), new Vector2(-gx, -gz), new Vector2(-gx, gz) };   // ring order
            float best = float.MaxValue; int bi = -1, bj = -1;
            for (int i = 0; i < 4; i++)
            {
                if (Crosses(A, k[i], hx, hz)) continue;
                if (!Crosses(k[i], B, hx, hz)) { float d = (k[i] - A).magnitude + (B - k[i]).magnitude; if (d < best) { best = d; bi = i; bj = -1; } }
                for (int s = 1; s <= 3; s += 2)
                {
                    int j = (i + s) % 4;
                    if (Crosses(k[j], B, hx, hz)) continue;
                    float d = (k[i] - A).magnitude + (k[j] - k[i]).magnitude + (B - k[j]).magnitude;
                    if (d < best) { best = d; bi = i; bj = j; }
                }
            }
            if (bi < 0) return res;
            res.Add(new Vector3(c.x + k[bi].x, 0, c.z + k[bi].y));
            if (bj >= 0) res.Add(new Vector3(c.x + k[bj].x, 0, c.z + k[bj].y));
            return res;
        }

        /// <summary>Does the segment a-b pass through the box |x| &lt; hx, |z| &lt; hz (2D slab test)?</summary>
        static bool Crosses(Vector2 a, Vector2 b, float hx, float hz)
        {
            float t0 = 0f, t1 = 1f; var d = b - a;
            for (int ax = 0; ax < 2; ax++)
            {
                float p = ax == 0 ? a.x : a.y, dd = ax == 0 ? d.x : d.y, h = ax == 0 ? hx : hz;
                if (Mathf.Abs(dd) < 1e-6f) { if (p <= -h || p >= h) return false; continue; }
                float u0 = (-h - p) / dd, u1 = (h - p) / dd; if (u0 > u1) { var tt = u0; u0 = u1; u1 = tt; }
                t0 = Mathf.Max(t0, u0); t1 = Mathf.Min(t1, u1);
                if (t0 >= t1) return false;
            }
            return true;
        }

        /// <summary>Walk keys from `from` to `to` on the ground, round the hull corners when the straight line would cut through it.</summary>
        void WalkTo(Ghost g, Func<Vector3> from, Func<Vector3> to, Face last = Face.Move, float minLast = 0f)
        {
            var r = g.rig; Func<Vector3> prev = from; float path = 0f;
            foreach (var corner in Around(r, from(), to()))
            {
                var cl = corner; Func<Vector3> cp = () => Ground(Wp(r, cl));
                float d = Dist(prev, cp); path += d;
                g.keys.Add(K(cp, d / runSpeed, "walk", Face.Move, ground: true));
                prev = cp;
            }
            if (path > 0f) g.detour += Mathf.Max(0f, path + Dist(prev, to) - Dist(from, to));
            g.keys.Add(K(to, Mathf.Max(minLast, Dist(prev, to) / runSpeed), "walk", last, ground: true));
        }

        // ------------------------------------------------------------------ figures
        GameObject CloneView(UnitView v)
        {
            var go = Instantiate(v.gameObject, v.transform.position, v.transform.rotation, box);
            go.name = "crew_" + (v.Obj != null ? v.Obj.Id.ToString() : "x");
            var uv = go.GetComponent<UnitView>(); if (uv) DestroyImmediate(uv);
            foreach (var rd in go.GetComponentsInChildren<RobotRagdoll>(true)) DestroyImmediate(rd);
            go.SetActive(true);
            return go;
        }

        GameObject SpawnFigure(string type, Team team, Vector3 pos, Quaternion rot)
        {
            // crew = the same model as the unit (BS-01 per type, the Laser keeps the chubby robot); old prefabs as fallback
            var prefab = game.Prefab(type == "laser" && !game.Prefab("Z_bs01_laser") ? "Z_robot" : "Z_bs01_" + type);   // robots_v3: the v3 laser when present
            if (!prefab) prefab = game.Prefab(type == "tough" ? "Z_tough" : "Z_robot");
            if (!prefab) return null;
            var go = Instantiate(prefab, pos, rot, box);
            go.name = "crew_" + type;
            UnitLookMotionGate.KeepUnitCrisp(go);                                                        // a crew figure rides a living vehicle: body and rigid kit stay sharp
            var tp = go.GetComponent<TeamPaint>(); if (tp) tp.Team = team;
            var an = go.GetComponentInChildren<Animator>(); if (an) an.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            return go;
        }

        static Transform FindWeapon(GameObject go)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name == "Rifle_Heavy" || t.name == "Bazooka" || t.name.StartsWith("BS01_Weapon_")) return t;   // ui_combat_v1: + the BS-01 guns
            return null;
        }

        static string Clip(Animator a, string want)
        {
            if (!a || !a.runtimeAnimatorController) return null;
            string[] chain;
            switch (want)
            {
                case "climb_ladder": chain = new[] { "climb_ladder", "walking" }; break;
                case "seated_driver": chain = new[] { "seated_driver", "seated_gunner", "idle" }; break;
                case "seated_gunner": chain = new[] { "seated_gunner", "seated_driver", "idle" }; break;
                case "jump_takeoff": chain = new[] { "jump_takeoff", "jump_air", "walking" }; break;
                case "jump_air": chain = new[] { "jump_air", "landing", "idle" }; break;
                case "landing": chain = new[] { "landing", "idle" }; break;
                case "run_forward": chain = new[] { "run_forward", "walking" }; break;
                case "blast_airborne": chain = new[] { "blast_airborne", "jump_air", "idle" }; break;
                case "death_back": chain = new[] { "death_back", "death_side", "idle" }; break;
                default: chain = new[] { want, "idle" }; break;
            }
            foreach (var c in chain)
                if (a.HasState(0, Animator.StringToHash(c)) || a.HasState(0, Animator.StringToHash("Base Layer." + c))) return c;   // short or full path hash
            return null;
        }

        static void Play(Animator a, ref string now, string want, float speed, float fade = 0.1f)
        {
            if (!a || !a.isActiveAndEnabled) return;
            if (want != now) { var c = Clip(a, want); if (c != null) a.CrossFadeInFixedTime(c, fade, 0); now = want; }
            a.speed = speed;
            if (a.layerCount > 1) a.SetLayerWeight(1, 0f);                                              // no upper-body aim layer while boarding
        }

        /// <summary>Robot team colour band (TeamLit _BandWorld: chest + shoulders in world Y). UnitView sets it every frame at
        /// root + 1.95..3.4 m, but these figures have no UnitView and TeamPaint alone paints the band from the mesh bounds (the
        /// whole armour or none), so every ghost, crew and body gets the same band here, measured from its hips bone (seated,
        /// climbing, popped out of a hatch) and scaled with the figure.</summary>
        void Band(SkinnedMeshRenderer skin, Transform root, Transform hips, float scale)
        {
            if (!skin || !root) return;
            float baseY = hips ? hips.position.y - hipHeight * scale : root.position.y;
            bandBlock ??= new MaterialPropertyBlock();
            skin.GetPropertyBlock(bandBlock, 0);
            bandBlock.SetFloat(AccentLoId, baseY + 1.95f * scale); bandBlock.SetFloat(AccentHiId, baseY + 3.4f * scale);
            skin.SetPropertyBlock(bandBlock, 0);
        }

        void HideLive(Ghost g)
        {
            if (!g.live) return;
            foreach (var r in g.live.GetComponentsInChildren<Renderer>(true)) if (r.enabled) { r.enabled = false; g.hidden.Add(r); }
        }
        static void ShowLive(Ghost g)
        {
            foreach (var r in g.hidden) if (r) r.enabled = true;
            g.hidden.Clear();
        }

        Ghost NewGhost(GameObject go, Rig r, bool enter)
        {
            var g = new Ghost { go = go, rig = r, enter = enter, anim = go.GetComponentInChildren<Animator>(), weapon = FindWeapon(go), hips = FindHips(go), skin = go.GetComponentInChildren<SkinnedMeshRenderer>(true) };
            g.baseScale = go.transform.localScale.x; g.rot = go.transform.rotation;
            if (g.anim) { g.anim.applyRootMotion = false; if (g.anim.layerCount > 1) g.anim.SetLayerWeight(1, 0f); }
            return g;
        }

        // ------------------------------------------------------------------ paths
        Key K(Func<Vector3> at, float dur, string clip, Face face, bool ground = false, bool ease = false, float arc = 0f, bool hatch = false, string late = null, float clipSpeed = 1f)
            => new Key { at = at, dur = Mathf.Max(0.02f, dur), clip = clip, face = face, ground = ground, ease = ease, arc = arc, hatch = hatch, clipLate = late, clipSpeed = clipSpeed, walk = clip == "walk" };

        static float Dist(Func<Vector3> a, Func<Vector3> b) { var d = b() - a(); d.y = 0; return d.magnitude; }

        /// <summary>Deck walk between the ladder top / vault spot and the hatch. A hatch well above the deck (heavy: turret roof
        /// +0.5 m, medium: +1.2 m) is a hop in an arc, so the legs never pass through the turret side.</summary>
        Key DeckKey(Rig r, Func<Vector3> from, Func<Vector3> to, bool up)
        {
            float rise = Mathf.Abs(Vector3.Dot(to() - from(), r.root.up));
            bool hop = rise > 0.3f;
            float dur = Mathf.Clamp(Dist(from, to) / deckSpeed, hop ? 0.3f : 0.1f, 0.45f);
            return hop ? K(to, dur, up ? "jump_takeoff" : "jump_air", Face.Move, ease: true, arc: 0.3f + 0.7f * rise, hatch: true, late: "landing")
                       : K(to, dur, "walk", Face.Move, ease: true, hatch: true);
        }

        void BuildEnter(Ghost g, Vector3 start)
        {
            var r = g.rig; float s = r.crewScale;
            Func<Vector3> S = () => start; var keys = g.keys; keys.Clear();
            keys.Add(K(S, 0, "idle", Face.Keep));
            Func<Vector3> seat = () => SeatRoot(r, g);
            switch (r.mode)
            {
                case Mode.Roof:
                {
                    Func<Vector3> baseP, topP, onDeck = null;
                    if (LadderSide(r, start))
                    {
                        baseP = () => Ground(r.ladderBottom.position);
                        if (r.approach) { Func<Vector3> ap = () => Ground(r.approach.position); WalkTo(g, S, ap); }
                        else WalkTo(g, S, baseP);
                        keys.Add(K(baseP, Mathf.Max(0.08f, Dist(keys[keys.Count - 1].at, baseP) / runSpeed), "walk", Face.Hull, ground: true));
                        topP = () => r.ladderTop.position;
                    }
                    else
                    {   // vault: climb straight up just outside the flank, then step onto the deck edge (not diagonally through the tracks)
                        var side = FlankPoint(r, start, 0.35f, out var nrm);
                        baseP = () => Ground(Wp(r, side));
                        WalkTo(g, S, baseP);
                        Vector3 edge = side - nrm * 0.25f, deck = side - nrm * 0.7f;
                        topP = () => { var l = edge; l.y = DeckY(r) + 0.05f; return Wp(r, l); };
                        onDeck = () => { var l = deck; l.y = DeckY(r); return Wp(r, l); };
                    }
                    float h = Mathf.Abs(topP().y - baseP().y);
                    keys.Add(K(topP, Mathf.Max(0.25f, h / climbSpeed), "climb_ladder", Face.Hull, clipSpeed: Mathf.Clamp(climbSpeed / 2f, 1f, 3f)));
                    if (onDeck != null) { keys.Add(K(onDeck, 0.16f, "walking", Face.Move, ease: true, clipSpeed: 1.5f)); topP = onDeck; }
                    Func<Vector3> hatchP = () => Wp(r, HatchTopL(r));
                    keys.Add(DeckKey(r, topP, hatchP, true));
                    keys.Add(K(seat, dropTime, "jump_air", Face.Seat, ease: true, hatch: true));
                    Finish(g, roofEnter);
                    break;
                }
                case Mode.Seat:
                {
                    float side = Mathf.Sign(r.root.InverseTransformPoint(start).x - C(r).x); if (side == 0) side = 1;
                    Func<Vector3> door = () =>
                    {
                        SeatPose(r, out var sp, out _, out _); var l = r.root.InverseTransformPoint(sp);
                        return Ground(Wp(r, new Vector3(C(r).x + side * (r.box.extents.x + 0.4f), 0, l.z)));
                    };
                    WalkTo(g, S, door);
                    SeatPose(r, out _, out _, out var sc);
                    keys.Add(K(seat, hopTime, "jump_takeoff", Face.Seat, ease: true, arc: 0.45f, late: sc));
                    Finish(g, seatEnter);
                    break;
                }
                case Mode.Cannon:
                {
                    Func<Vector3> behind = () => Behind(r);
                    WalkTo(g, S, behind);
                    if (HighSeat(r))
                    {   // gunner_seats_v1: up over the platform's edge, a step across the deck, sit down (the roof-hatch budget)
                        Func<Vector3> edge = () => DeckPoint(r, EdgeOut), deck = () => DeckPoint(r, 0f);
                        keys.Add(K(edge, Mathf.Max(0.25f, Mathf.Abs(Vector3.Dot(edge() - behind(), r.root.up)) / climbSpeed), "climb_ladder", Face.Hull, arc: 0.3f, clipSpeed: Mathf.Clamp(climbSpeed / 2f, 1f, 3f)));
                        keys.Add(K(deck, 0.2f, "walking", Face.Seat, ease: true));
                        keys.Add(K(seat, sitTime, "seated_gunner", Face.Seat, ease: true));
                        Finish(g, roofEnter);
                        break;
                    }
                    keys.Add(K(seat, sitTime, r.standGunner ? "idle" : "seated_gunner", Face.Seat, ease: true, arc: 0.15f));
                    Finish(g, cannonEnter);
                    break;
                }
                default:
                {
                    if (r.obj.IsShuttle && ShuttleRearDoor.Of(r.view)) { BuildShuttleEnter(g, S, ShuttleRearDoor.Of(r.view)); return; }   // DV118: to the back, up the ramp to a bench (BoardingView.Shuttle.cs)
                    var apcDoor = r.obj.Type == "apc" ? ApcRearDoor.Of(r.view) : null;
                    if (apcDoor) { BuildApcEnter(g, S, apcDoor); return; }
                    float side = DoorSide(r, start);
                    Func<Vector3> door = () => DoorOut(r, side);
                    Func<Vector3> inside = () => DoorInside(r, side);
                    WalkTo(g, S, door);
                    keys.Add(K(inside, Mathf.Max(0.25f, Dist(door, inside) / runSpeed), r.rearDoor ? "walking" : "jump_takeoff", Face.Move, ease: true, arc: r.rearDoor ? 0f : 0.3f));
                    Finish(g, doorEnter);
                    break;
                }
            }
            g.scaleA = 1f; g.scaleB = r.mode == Mode.Door ? 0.6f : s; g.scaleSpan = g.total;              // ducks into the cab / hull
            FitEnter(g);                                                                                   // electrobot_v1: the type's crew fit
        }

        void BuildExit(Ghost g, bool append = false)
        {
            var r = g.rig; float s = r.crewScale; var keys = g.keys;
            int fitK0 = append ? keys.Count : 1;                                                          // electrobot_v1: the first key of the way out
            var live = g.live;
            Func<Vector3> to = () => live ? live.transform.position : Wp(r, new Vector3(C(r).x + r.box.extents.x + 1.2f, 0, C(r).z));
            Func<Vector3> seat = () => SeatRoot(r, g);
            float side = r.mode == Mode.Door ? DoorSide(r, to()) : Mathf.Sign(r.root.InverseTransformPoint(to()).x - C(r).x); if (side == 0) side = 1;
            if (!append) { keys.Clear(); keys.Add(K(r.mode == Mode.Door ? () => DoorInside(r, side) : seat, 0, "idle", Face.Seat)); }
            int walkAt = 0;
            switch (r.mode)
            {
                case Mode.Roof:
                {
                    Func<Vector3> hatchP = () => Wp(r, HatchTopL(r));
                    keys.Add(K(hatchP, Mathf.Max(0.2f, (hatchP() - seat()).magnitude / climbSpeed), "climb_ladder", Face.Seat, hatch: true, clipSpeed: 2f));
                    Func<Vector3> topP, baseP, edgeP = null;
                    if (LadderSide(r, to())) { topP = () => r.ladderTop.position; baseP = () => Ground(r.ladderBottom.position); }
                    else
                    {   // reverse vault: deck edge, step to just outside the flank, straight down
                        var sp = FlankPoint(r, to(), 0.35f, out var nrm); Vector3 deck = sp - nrm * 0.7f, edge = sp - nrm * 0.25f;
                        topP = () => { var l = deck; l.y = DeckY(r); return Wp(r, l); };
                        edgeP = () => { var l = edge; l.y = DeckY(r) + 0.05f; return Wp(r, l); };
                        baseP = () => Ground(Wp(r, sp));
                    }
                    keys.Add(DeckKey(r, hatchP, topP, false));
                    if (edgeP != null) { keys.Add(K(edgeP, 0.16f, "walking", Face.Move, ease: true, clipSpeed: 1.5f)); topP = edgeP; }
                    keys.Add(K(baseP, Mathf.Max(0.22f, Mathf.Abs(topP().y - baseP().y) / climbSpeed), "climb_ladder", Face.Hull, clipSpeed: 2f));
                    walkAt = keys.Count;
                    WalkTo(g, baseP, to, minLast: 0.15f);
                    Finish(g, append ? 99f : roofExit);
                    break;
                }
                case Mode.Seat: case Mode.Cannon:
                {
                    Func<Vector3> step = () =>
                    {
                        if (r.mode == Mode.Cannon) return Behind(r);
                        SeatPose(r, out var sp, out _, out _); var l = r.root.InverseTransformPoint(sp);
                        return Ground(Wp(r, new Vector3(C(r).x + side * (r.box.extents.x + 0.4f), 0, l.z)));
                    };
                    if (HighSeat(r))
                    {   // gunner_seats_v1: stand up on the deck, walk to its edge, climb down (the roof-hatch exit budget)
                        Func<Vector3> deck = () => DeckPoint(r, 0f), edge = () => DeckPoint(r, EdgeOut);
                        keys.Add(K(deck, 0.2f, "idle", Face.Seat, ease: true));
                        keys.Add(K(edge, 0.18f, "walking", Face.Move, ease: true));
                        keys.Add(K(step, Mathf.Max(0.25f, Mathf.Abs(Vector3.Dot(edge() - step(), r.root.up)) / climbSpeed), "climb_ladder", Face.Hull, arc: 0.3f, clipSpeed: 2f, late: "landing"));
                    }
                    else keys.Add(K(step, r.mode == Mode.Seat ? hopTime : sitTime, r.mode == Mode.Seat ? "jump_air" : "landing", Face.Move, ease: true, arc: r.mode == Mode.Seat ? 0.4f : 0.15f, late: "landing"));
                    walkAt = keys.Count;
                    WalkTo(g, step, to, minLast: 0.15f);
                    Finish(g, append ? 99f : r.mode == Mode.Seat ? seatExit : HighSeat(r) ? roofExit : cannonExit);
                    break;
                }
                default:
                {
                    if (!append && r.obj.IsShuttle && ShuttleRearDoor.Of(r.view)) { BuildShuttleExit(g, to, ShuttleRearDoor.Of(r.view), fitK0); return; }   // DV118: bench, aisle, down the ramp (BoardingView.Shuttle.cs)
                    var apcDoor = !append && r.obj.Type == "apc" ? ApcRearDoor.Of(r.view) : null;
                    if (apcDoor) { BuildApcExit(g, to, apcDoor, fitK0); return; }
                    Func<Vector3> door = () => DoorOut(r, side);
                    keys.Add(K(door, 0.3f, r.rearDoor ? "walking" : "jump_air", Face.Move, ease: true, arc: r.rearDoor ? 0f : 0.3f, late: r.rearDoor ? null : "landing"));
                    walkAt = keys.Count;
                    WalkTo(g, door, to, minLast: 0.15f);
                    Finish(g, append ? 99f : doorExit);
                    break;
                }
            }
            float pre = 0f; for (int i = 1; i < walkAt && i < keys.Count; i++) pre += keys[i].dur;          // time until the final walk (after budget fit)
            if (!append) { g.scaleA = r.mode == Mode.Door ? 0.6f : s; g.scaleB = 1f; g.scaleSpan = Mathf.Max(0.05f, pre); }
            else g.backSpan = Mathf.Max(0.05f, pre - g.backAt);
            FitExit(g, append, fitK0);                                                                     // electrobot_v1: the type's crew fit
        }

        public const float ApcWalk = 1.5f, ApcGap = 0.55f, ApcEnterBudget = 3.4f, ApcExitBudget = 3.2f;
        static readonly Dictionary<string, float> apcFit = new Dictionary<string, float>();
        /// <summary>Per robot type: height at full size (m) and the scale that fits it in the bay (proof log).</summary>
        public static readonly Dictionary<string, Vector2> ApcFitLog = new Dictionary<string, Vector2>();

        /// <summary>The scale (x the robot's own) that makes this type ApcRearDoor.FitHeight tall: through the 1.45 m doorway and
        /// under the 1.45 m bay roof, measured once per type on the figure's renderers at full size.</summary>
        float ApcFit(Ghost g)
        {
            var o = game.World.GetAny(g.robotId); string type = o != null ? o.Type : "grunt";
            if (apcFit.TryGetValue(type, out var f)) return f;
            Bounds b = default; bool any = false;
            foreach (var rr in g.go.GetComponentsInChildren<Renderer>()) { if (!rr.enabled || rr is ParticleSystemRenderer) continue; if (!any) { b = rr.bounds; any = true; } else b.Encapsulate(rr.bounds); }
            float h = any ? b.size.y : 2.4f;
            f = Mathf.Clamp(ApcRearDoor.FitHeight / Mathf.Max(0.3f, h), 0.3f, 1f);
            apcFit[type] = f; ApcFitLog[type] = new Vector2(h, f);
            return f;
        }

        void BuildApcEnter(Ghost g, Func<Vector3> S, ApcRearDoor d)
        {
            var r = g.rig; var keys = g.keys;
            int k = g.seatK = r.apcSeat++ % 6;
            g.scaleA = 1f; g.scaleB = ApcFit(g);
            Func<Vector3> foot = () => Ground(d.FootPoint()), tip = () => d.RampPoint(1f, RobotY), sill = () => d.RampPoint(0f, RobotY),
                          inDoor = () => d.AislePoint(-1, RobotY), aisle = () => d.AislePoint(k, RobotY);
            Func<Vector3> seat = () => d.SeatPoint(k) - HipsOffset(g.go, g.hips, r.root.up, pelvisHeight * g.baseScale * g.scaleB);
            WalkTo(g, S, foot);
            int footK = keys.Count - 1;
            keys.Add(K(tip, 0.3f, "walk", Face.Move));
            keys.Add(K(sill, ApcRearDoor.Len / ApcWalk, "walk", Face.Move));
            keys.Add(K(inDoor, 0.3f, "walk", Face.Move));
            keys.Add(K(aisle, Mathf.Max(0.15f, Dist(inDoor, aisle) / ApcWalk), "walk", Face.Move));
            keys.Add(K(seat, 0.45f, "seated_gunner", Face.Aisle, ease: true));
            Finish(g, ApcEnterBudget);
            float t = 0f; for (int i = 1; i <= footK; i++) t += keys[i].dur;
            g.footT = t;
            float ramp0 = t + keys[footK + 1].dur, rampDur = keys[footK + 2].dur;
            g.scaleT0 = ramp0 + 0.3f * rampDur; g.scaleSpan = Mathf.Max(0.1f, 0.7f * rampDur + 0.5f * keys[footK + 3].dur);   // shrinks on the upper ramp, fits at the doorway
            FitEnter(g);
        }

        void BuildApcExit(Ghost g, Func<Vector3> to, ApcRearDoor d, int fitK0)
        {
            var r = g.rig; var keys = g.keys; int k = Mathf.Max(0, g.seatK);
            g.scaleA = ApcFit(g); g.scaleB = 1f;
            Func<Vector3> seat = () => d.SeatPoint(k) - HipsOffset(g.go, g.hips, r.root.up, pelvisHeight * g.baseScale * g.scaleA),
                          aisle = () => d.AislePoint(k, RobotY), inDoor = () => d.AislePoint(-1, RobotY), sill = () => d.RampPoint(0f, RobotY),
                          tip = () => d.RampPoint(1f, RobotY), foot = () => Ground(d.FootPoint());
            keys.Clear();
            keys.Add(K(seat, 0, "seated_gunner", Face.Aisle));
            keys.Add(K(seat, 0.2f, "seated_gunner", Face.Aisle));
            keys.Add(K(aisle, 0.35f, "idle", Face.Aisle, ease: true));
            keys.Add(K(inDoor, Mathf.Max(0.15f, Dist(aisle, inDoor) / ApcWalk), "walk", Face.Move));
            keys.Add(K(sill, 0.3f, "walk", Face.Move));
            int rampK = keys.Count;
            keys.Add(K(tip, ApcRearDoor.Len / ApcWalk, "walk", Face.Move));
            keys.Add(K(foot, 0.3f, "walk", Face.Move));
            WalkTo(g, foot, to, minLast: 0.15f);
            Finish(g, ApcExitBudget);
            float t = 0f; for (int i = 1; i < rampK; i++) t += keys[i].dur;
            g.scaleT0 = t; g.scaleSpan = Mathf.Max(0.1f, 0.7f * keys[rampK].dur);                          // grows back on the ramp
            FitExit(g, false, fitK0);
        }

        static float Sum(List<Key> keys) { float t = 0; for (int i = 1; i < keys.Count; i++) t += keys[i].dur; return t; }

        /// <summary>Fit the path into its budget (a long walk plays faster, never slower than natural). Walking round the hull
        /// may take up to detourTime longer; the catch-up still ends it within 0.2 s once the sim makes anything happen.</summary>
        void Finish(Ghost g, float budget)
        {
            budget += Mathf.Min(detourTime, g.detour / runSpeed);
            float t = Sum(g.keys);
            if (t > budget) { float k = budget / t; for (int i = 1; i < g.keys.Count; i++) g.keys[i].dur *= k; t = budget; }
            g.total = t;
        }

        // ------------------------------------------------------------------ start / end
        void StartEnter(Rig r, int robotId, ZObject robot, float delay)
        {
            if (!boarded.Add(robotId)) return;
            var rv = View(robotId);
            // a robot still climbing out (its real renderers hidden) that boards again at once: drop that exit ghost first,
            // so the clone below is not made of switched-off renderers
            foreach (var og in ghosts) if (og.live == rv && rv && !og.done) { ShowLive(og); if (og.go) Destroy(og.go); og.go = null; }
            GameObject go;
            if (rv && rv.gameObject.activeInHierarchy && !rv.Dying) go = CloneView(rv);
            else
            {
                var p = robot != null ? Ground(game.CenterWorld(robot, false)) : Ground(Wp(r, new Vector3(C(r).x + r.box.extents.x + 1f, 0, C(r).z)));
                go = SpawnFigure(robot != null ? robot.Type : r.crewType, robot != null ? robot.Owner : r.obj.Owner, p, r.root.rotation);
            }
            if (!go) return;
            var g = NewGhost(go, r, true); g.robotId = robotId; g.delay = delay;
            if (Tower(r)) { var h = TowerHatch(r, true); go.transform.position = h; BuildTowerMan(g, h); }   // crews_v1: a tower gun is manned from inside the fort
            else BuildEnter(g, go.transform.position);
            if (g.seatK >= 0 && r.obj.IsShuttle) ShuttleFootDelay(g, r);                          // DV118: at the ramp foot at the rules' moment (BoardingView.Shuttle.cs)
            else if (g.seatK >= 0)
            {
                float arrive = Time.time + g.delay + g.footT, min = Mathf.Max(Time.time + ApcRearDoor.OpenTime + ApcRearDoor.Lead, r.apcLastFoot + ApcGap);
                if (arrive < min) { g.delay += min - arrive; arrive = min; }
                r.apcLastFoot = arrive;
            }
            r.boarding++; r.acted = false;
            ghosts.Add(g);
            CaptureHold.GhostStarted(r.obj.Id);                                                           // boarding_nb_v1 NB17
            if (boarded.Count > 256) boarded.Clear();                                                  // ids are never reused; keep the set small
        }

        void StartExit(Rig r, ZObject robot, int index)
        {
            if (!exited.Add(robot.Id)) return;
            if (exited.Count > 256) { exited.Clear(); exited.Add(robot.Id); }
            var live = game.SpawnUnitView(robot);                                                       // CombatView's call returns this one
            if (!live) return;
            GameObject go;
            if (index == 0 && r.crew && r.visible) { go = r.crew; r.crew = null; Unparent(go); }     // the visible gunner / driver gets up (crews_v1: off the turret)
            else go = CloneView(live);
            var g = NewGhost(go, r, false); g.live = live; g.robotId = robot.Id; g.delay = index * crewStagger;
            g.baseScale = live.transform.localScale.x;
            if (r.obj.IsShuttle && ShuttleRearDoor.Of(r.view)) ShuttleExitDelay(g, r, robot, index);   // DV118: seated by the doorway, out at the rules' moment (BoardingView.Shuttle.cs)
            else if (r.obj.Type == "apc" && ApcRearDoor.Of(r.view))
            {
                if (index == 0) r.apcSeat = 0;
                g.seatK = index % 6; g.delay = ApcRearDoor.OpenTime + 0.1f + index * ApcGap;
            }
            HideLive(g);
            BuildExit(g);
            ghosts.Add(g);
            Place(g, 0f);
            ExitsStarted++;                                                                                // crews_v2 (CannonExitCheck)
        }

        void EndGhost(Ghost g, bool abort)
        {
            g.done = true;
            var r = g.rig;
            if (!g.enter && !g.demo && !abort) ExitsDone++;                                                // crews_v2 (CannonExitCheck)
            if (g.enter && !g.demo) r.boarding = Mathf.Max(0, r.boarding - 1);
            if (g.enter && !g.demo && r.boarding == 0) CaptureHold.Boarded(r.obj.Id);                    // boarding_nb_v1 NB17: repaint, engine, voices now
            // CombatView.EnterSounds already plays capture + engine start at the Entered event; the crew landing in the seat
            // (or the cab / hull door) adds a clank here, roof hatches clunk shut in StepRig
            if (!abort && g.enter && !g.demo && r.boarding == 0 && r.view && (r.mode != Mode.Roof || !r.hinge))
            {
                Sfx("crew_in", r.root.position, 0.3f, r.obj.Id, OzSoundV3.BankCrewIn);   // sound_v3
                if (r.obj.Kind == ZKind.Vehicle && !r.obj.IsShuttle && !(r.obj.Type == "apc" && ApcRearDoor.Of(r.view))) StartCoroutine(LatchLater(0.14f, r.root.position, r.obj.Id));
            }
            ShowLive(g);
            ExitWalkEnded(g, abort);                                                                     // K441: the hand-over measured, the shots held meanwhile drawn (View/BoardingView.ExitWalk.cs)
            if (!g.go) return;
            if (!abort && g.enter && !g.demo && r.visible && showSeatedCrew && !r.crew && Crewed(r))
            {
                r.crew = g.go; r.crewAnim = g.anim; r.crewWeapon = g.weapon; r.crewHips = g.hips; r.crewSkin = g.skin; r.crewBase = g.baseScale; r.crewClip = null;
                r.everCrewed = true;                                                                       // crews_v1
                if (r.crewWeapon) r.crewWeapon.gameObject.SetActive(false);
                return;
            }
            if (!abort && g.enter && !g.demo && g.seatK >= 0 && r.view && ShuttleRearDoor.Of(r.view))
            { g.go.transform.SetParent(r.root, true); ShuttleRearDoor.Of(r.view).HoldFigure(g.go, g.robotId); return; }   // DV118: on its bench until the ramp shuts
            if (!abort && g.enter && !g.demo && g.seatK >= 0 && r.view && ApcRearDoor.Of(r.view))
            { g.go.transform.SetParent(r.root, true); ApcRearDoor.Of(r.view).HoldFigure(g.go); return; }
            Destroy(g.go);
        }

        bool Crewed(Rig r) => r.view && !r.view.Dying && r.obj.Drivers.Count > 0 && r.obj.Owner != Team.Neutral && game.World.Get(r.obj.Id) == r.obj;

        // ------------------------------------------------------------------ snipes and deaths
        void Snipe(Rig r, int sniperId = -1)
        {
            GameObject body = null;
            float scale = 1f;
            // hatch_v1: the commander up in the hatch IS the body that flies (his own size, straight out of the hatch, no pop to
            // full robot size); thrown away from the sniper; the emptied tank keeps its lid open (lidAjar) until it is crewed again
            if (r.crew && r.crew.activeSelf) { body = r.crew; scale = r.lid ? r.crewBase * r.crewScale * CrewFit(r, r.crew) : r.crewBase; r.crew = null; r.crewPop = 0f; }
            else if (bodyOnSnipe)
            {
                SeatPose(r, out var sp, out var sr, out _);
                var at = r.mode == Mode.Roof ? Wp(r, HatchTopL(r)) : sp;
                body = SpawnFigure(r.crewType, r.crewTeam, at, sr);
                if (body && r.lid) scale = body.transform.localScale.x * r.crewScale * CrewFit(r, body);        // electrobot_v1: + the type's fit
            }
            if (r.lid) r.lidAjar = true;
            var sv = View(sniperId);
            var from = sv && sv.gameObject.activeInHierarchy ? sv.transform.position : r.root.position;
            foreach (var g in ghosts) if (g.rig == r && g.enter && !g.demo && !g.done && g.go) { var go = g.go; g.go = null; EndGhost(g, true); Fling(go, from, 1.6f, g.baseScale); }
            if (body) Fling(body, from, 1.6f, scale);
        }

        void CrewDies(Rig r)
        {
            if (ShuttleDoorwayCrewDies(r)) return;   // K418: a shuttle's figures are thrown by its break-up (BoardingView.ShuttleCrewLost), never twice
            if (r.crew && r.root) AudioDirector.CrewDeath(r.root.position, r.obj != null ? r.obj.Id : -1);   // sound_v3: the flung crew dies audibly
            // crews_v1: a tower gunner is thrown outward from the fort centre, over the wall (the fort has no colliders: straight
            // down, the ragdoll would drop through the roof into the fort); ground guns as before
            bool tower = Tower(r) && r.view.TowerFort;
            var from = tower ? r.view.TowerFort.position : r.root.position; float reach = tower ? 8f : 0f;
            if (r.crew) { var c = r.crew; r.crew = null; if (c.activeSelf) Fling(c, from, 2.4f, r.crewBase, reach); else Destroy(c); }
            foreach (var g in ghosts) if (g.rig == r && g.enter && !g.demo && !g.done && g.go) { var go = g.go; g.go = null; EndGhost(g, true); Fling(go, from, 2.4f, g.baseScale, reach); }
        }

        /// <summary>Throw a crew body out of the vehicle: a real ragdoll (RobotRagdoll, capped) or the Zod flip arc.</summary>
        void Fling(GameObject go, Vector3 from, float strength, float baseScale = 1f, float reach = 0f)
        {
            if (!go) return;
            Unparent(go);                                                                                  // crews_v1: off the gun's turret first
            var t = go.transform;
            t.localScale = Vector3.one * baseScale;
            t.position += Vector3.up * 0.4f;
            var weapon = FindWeapon(go); if (weapon) weapon.gameObject.SetActive(true);
            var hold = go.GetComponent<WeaponHold>(); if (hold) hold.Suspended = true;                          // ui_combat_v1: no grip IK on a flying body (like UnitView's dead)
            OzGlowFade.Begin(go);                                                                               // a thrown crew body's glows go out
            Band(go.GetComponentInChildren<SkinnedMeshRenderer>(true), t, FindHips(go), baseScale);                 // frozen at the fling, like UnitView's dead
            var push = t.position - from; push.y = 0;
            if (push.sqrMagnitude < 0.01f) push = UnityEngine.Random.insideUnitSphere; push.y = 0;
            push = push.sqrMagnitude > 1e-4f ? push.normalized : Vector3.forward;
            RobotRagdoll rd = null;
            if (RobotRagdoll.CanStart()) rd = RobotRagdoll.Launch(go, push, strength);
            var c = new Corpse { go = go, rd = rd, anim = go.GetComponentInChildren<Animator>() };
            if (!rd)
            {
                c.a = t.position; c.T = reach > 0f ? 1.7f : 1.3f; c.r0 = Quaternion.Euler(0, t.eulerAngles.y, 0);   // crews_v1: off a tower: longer
                c.b = t.position + push * (reach > 0f ? reach : UnityEngine.Random.Range(2.5f, 5f)); c.b.y = game.GroundY(c.b.x, c.b.z) - 0.05f;
                c.axis = Vector3.Cross(Vector3.up, push);
                string now = null; Play(c.anim, ref now, "blast_airborne", 1f, 0.05f);
            }
            corpses.Add(c);
        }

        void StepCorpse(Corpse c, float dt)
        {
            c.age += dt;
            if (!c.go) return;
            var t = c.go.transform;
            if (!c.rd)
            {
                float k = Mathf.Clamp01(c.age / c.T);
                if (k < 1f)
                {
                    var p = Vector3.Lerp(c.a, c.b, k); p.y += Mathf.Sin(k * Mathf.PI) * 3.2f;
                    t.position = p; t.rotation = Quaternion.AngleAxis(Mathf.SmoothStep(0, 1, k) * 100f, c.axis) * c.r0;
                }
                else if (c.age - dt < c.T) { string now = null; Play(c.anim, ref now, "death_back", 1f, 0.05f); }
                if (c.age > c.T + 3.5f) t.position += Vector3.down * 1.6f * dt / 3f;
                if (c.age > c.T + 6.5f) Destroy(c.go);
                return;
            }
            if (c.age > 10f)                                                                              // like UnitView: lie, freeze, sink
            {
                if (!c.rd.Frozen) c.rd.Freeze();
                t.position += Vector3.down * 1.8f * dt / 3f;
                if (c.age > 13f) Destroy(c.go);
            }
        }

        // ------------------------------------------------------------------ per frame
        void LateUpdate()
        {
            if (!game || game.World == null) return;
            float dt = Time.deltaTime; int frame = Time.frameCount;
            foreach (var kv in game.Views)
            {
                var v = kv.Value; if (!v || v.Obj == null) continue;
                if (v.Obj.Kind != ZKind.Vehicle && v.Obj.Kind != ZKind.Cannon) continue;
                if (!v.gameObject.activeInHierarchy) continue;
                var r = RigOf(v); if (r != null) { r.seenFrame = frame; K386Grow(r); }   // K386: the crew's size follows the hull's exit grow (BoardingView.K386.cs)
            }
            float sdt = dt * SimRate;                                                                     // ghosts keep Zod's pace (4x bot match, pause)
            for (int i = 0; i < ghosts.Count; i++) StepGhost(ghosts[i], sdt);
            ghosts.RemoveAll(g => g.done);
            foreach (var g in ghosts)
            {
                if (g.done || g.seatK < 0) continue;
                var ad = ApcRearDoor.Of(g.rig.view); if (!ad) continue;
                if (!g.enter || g.delay + (g.footT - g.time) / Mathf.Max(1f, g.speedMul) <= ApcRearDoor.OpenTime + ApcRearDoor.Lead) ad.Request();
            }
            dropList.Clear();
            foreach (var kv in rigs) { var r = kv.Value; if (!r.view || r.seenFrame != frame) dropList.Add(kv.Key); else StepRig(r, dt); }
            foreach (var v in dropList) DropRig(v);
            for (int i = 0; i < corpses.Count; i++) StepCorpse(corpses[i], dt);
            corpses.RemoveAll(c => !c.go);
        }

        void DropRig(UnitView v)
        {
            if (!rigs.TryGetValue(v, out var r)) return;
            if (r.crew) Destroy(r.crew);                                           // repair station took it, or the wreck is gone
            foreach (var g in ghosts) if (g.rig == r && !g.done) EndGhost(g, true);
            rigs.Remove(v);
        }

        void StepGhost(Ghost g, float dt)
        {
            if (g.done) return;
            var r = g.rig;
            if (!g.go || !r.view || !r.view.gameObject.activeInHierarchy || (g.live && (g.live.Dying || !g.live.gameObject.activeInHierarchy)))
            { EndGhost(g, true); return; }
            if (g.delay > 0f)
            {
                g.delay -= dt;
                if (!g.enter && g.go.activeSelf && r.crew != g.go && g.time == 0f && !g.demo && g.seatK < 0) g.go.SetActive(false);   // still inside
                if (!g.enter && g.seatK >= 0) Place(g, 0f);
                if (g.enter) { string now = g.clipNow; Play(g.anim, ref now, "idle", SimRate); g.clipNow = now; Band(g.skin, g.go.transform, g.hips, g.go.transform.localScale.x); }
                if (g.delay > 0f) return;
            }
            if (!g.go.activeSelf) { g.go.SetActive(true); g.clipNow = null; }
            // Zod never waits: once the sim makes things happen, finish now
            ShuttleGhostStep(g, r);                                                                      // DV118: the shuttle's ramp-foot steps (BoardingView.Shuttle.cs)
            bool act = g.enter ? (r.acted || r.obj.IsMoving || r.obj.ShuttleAirborne || (r.obj.HasLid && r.obj.LidOpen)) : ExitWalkActs(g, g.hurry || (g.live && g.live.Obj != null && g.live.Obj.IsMoving));   // K441 (item 443): an exit walk is never squeezed by its robot's fight (View/BoardingView.ExitWalk.cs)
            float remaining = g.total - g.time;
            if (act && remaining > catchUp) g.speedMul = Mathf.Max(g.speedMul, remaining / catchUp);
            g.time += dt * g.speedMul;
            var k441From = g.go.transform.position;                                                     // K441: where the figure stood last frame
            Place(g, dt);
            ExitWalkSmooth(g, k441From, dt);                                                             // K441: the walk on to the robot follows it and ends on it, turned to its facing
            if (g.time >= g.total && ExitWalkReached(g)) EndGhost(g, false);
        }

        void Place(Ghost g, float dt)
        {
            var r = g.rig; var keys = g.keys; if (keys.Count < 2) return;
            float acc = 0; int i = 1;
            while (i < keys.Count - 1 && g.time > acc + keys[i].dur) { acc += keys[i].dur; i++; }
            var k = keys[i];
            float u = Mathf.Clamp01((g.time - acc) / k.dur), e = k.ease ? Mathf.SmoothStep(0, 1, u) : u;
            Vector3 a = keys[i - 1].at(), b = k.at();
            var p = Vector3.LerpUnclamped(a, b, e);
            if (k.ground) p = Ground(p);
            if (k.arc != 0f) p += r.root.up * k.arc * Mathf.Sin(u * Mathf.PI);
            // facing (robot front = -z)
            Quaternion want = g.rot; Vector3 dir = Vector3.zero;
            switch (k.face)
            {
                case Face.Move: dir = b - a; break;
                case Face.Hull: dir = r.root.TransformPoint(C(r)) - p; break;
                case Face.Seat: SeatPose(r, out _, out want, out _); break;
                case Face.Aisle: { var ad = ApcRearDoor.Of(r.view); if (ad) dir = ad.AisleDir(g.seatK); else { var sd = ShuttleRearDoor.Of(r.view); if (sd) dir = sd.AisleDir(g.seatK); } break; }
            }
            dir.y = 0; if (dir.sqrMagnitude > 1e-4f) want = Quaternion.LookRotation(-dir.normalized, Vector3.up);
            g.rot = dt <= 0f ? want : Quaternion.Slerp(g.rot, want, 1f - Mathf.Exp(-dt * 14f));
            var t = g.go.transform;
            t.SetPositionAndRotation(p, g.rot);
            float f = Mathf.SmoothStep(0, 1, Mathf.Clamp01((g.time - g.scaleT0) / Mathf.Max(0.01f, g.scaleSpan)));
            float sc = Mathf.Lerp(g.scaleA, g.scaleB, f);
            if (g.backAt >= 0f && g.time > g.backAt) sc = Mathf.Lerp(g.scaleB, 1f, Mathf.SmoothStep(0, 1, Mathf.Clamp01((g.time - g.backAt) / g.backSpan)));
            t.localScale = Vector3.one * g.baseScale * sc * FitNow(g, out float stowNow);                    // electrobot_v1: the type's crew fit
            ElectrobotCoils.Set(g.go, stowNow);
            // clip
            string clip = u > 0.65f && k.clipLate != null ? k.clipLate : k.clip; float spd = k.clipSpeed;
            if (k.walk)
            {
                float v = (b - a).magnitude / Mathf.Max(0.05f, k.dur);
                bool run = v > 2.4f; clip = run ? "run_forward" : "walking"; spd = Mathf.Clamp(v / (run ? RunClipSpeed : WalkClipSpeed), 0.6f, 2.5f);
            }
            else if (clip == "jump_takeoff" || clip == "jump_air" || clip == "landing") spd = Mathf.Clamp(0.45f / Mathf.Max(0.1f, k.dur), 0.8f, 2.5f);
            string now = g.clipNow; Play(g.anim, ref now, clip, spd * g.speedMul * SimRate); g.clipNow = now;
            Band(g.skin, t, g.hips, t.localScale.x);
            float frac = g.total > 0 ? g.time / g.total : 1f;
            bool show = g.enter ? frac < 0.12f : frac > 0.86f;
            if (g.demo) show = frac < 0.06f || frac > 0.94f;
            if (g.weapon && g.weapon.gameObject.activeSelf != show) g.weapon.gameObject.SetActive(show);
            if (k.hatch || (i + 1 < keys.Count && keys[i + 1].hatch && g.time > acc + k.dur - 0.15f)) r.hatchWant = true;
        }

        void StepRig(Rig r, float dt)
        {
            bool crewed = Crewed(r);
            if (crewed) { r.crewTeam = r.obj.Owner; if (!string.IsNullOrEmpty(r.obj.DriverType)) r.crewType = r.obj.DriverType; }
            if (r.boarding == 0) r.acted = false;
            r.flinch = Mathf.Max(0f, r.flinch - dt);
            if (r.seatOnTurret && r.standGunner && (r.baseT || r.groundStand))                          // crews_v2: + the ground stand
            {   // crews_v1: the standing gunner's feet follow the base plate under his turret-carried spot, stepping (2.5 m/s), never snapping
                float fy = CarriedSeatRaw(r).y;
                r.feetY = r.feetInit ? Mathf.MoveTowards(r.feetY, fy, 2.5f * dt) : fy; r.feetInit = true;
            }
            if (r.visible)
            {
                if (showSeatedCrew && crewed && !r.crew && r.boarding == 0)
                {   // crews_v1: a gun placed at run time is manned with an animation (walk up and sit / climb onto the tower), not a pop-in
                    if (r.bornLive && !r.everCrewed && r.obj.Kind == ZKind.Cannon) StartMan(r); else SpawnCrew(r);
                }
                if ((!crewed || !showSeatedCrew) && r.crew && r.boarding == 0) { Destroy(r.crew); r.crew = null; }
                if (r.crew) PlaceSeated(r);
            }
            else if (r.lid && showLidCommander)
            {
                // ui_combat_v1: Zod ZVehicle::ProcessLid (zvehicle.cpp:243-264, 0.2 s steps of game time): the lid opens first (frames
                // 0 -> 2), the robot shows once it is fully open (show_robot); on closing the robot drops at once and the lid follows.
                // The sim's LidOpen (zvehicle.cpp:198-240) is also the only time a light / medium / heavy tank can be sniped (:178-186).
                float ldt = dt * SimRate;
                bool up = crewed && ShowHatchDriver(r.obj) && r.boarding == 0;
                if (r.crew && !r.crew.activeSelf && r.crewKey != null && r.crewKey != CrewKey(r)) { Destroy(r.crew); r.crew = null; }   // recaptured: the new driver's BS-01 and team
                if (up && !r.crew) SpawnCrew(r);
                if (up) r.hatchWant = true;
                if (up) r.restLid = r.obj.Resting && !r.obj.LidOpen && !r.obj.CraneGunUp;                // DV75 idle rest: this opening is the rest's own (quiet lid); kept for its close
                if (crewed) r.lidAjar = false;
                else if (r.lidAjar && r.boarding == 0 && !r.view.Dying) r.hatchWant = true;              // hatch_v1: sniped and empty: lid stays open
                bool rise = up && r.hatch > 0.97f;
                r.crewPop = Mathf.MoveTowards(r.crewPop, rise ? 1f : 0f, ldt / Mathf.Max(0.01f, rise ? commanderRiseTime : commanderSinkTime));
                if (r.crewPop > 0f) r.hatchWant = true;
                if (r.crew)
                {
                    bool vis = r.crewPop > 0.001f;
                    if (r.crew.activeSelf != vis)
                    {
                        r.crew.SetActive(vis); r.crewClip = null;
                        if (vis) { r.crewAimSet = false; r.lastFireSeen = r.view.LastFireAt; r.shotAt = -1f; r.kick = 0f; }   // only shots from now on
                    }
                    if (vis) { PlaceCommander(r, dt); if (commanderFires) CommanderFire(r); }
                    if (!crewed && !vis) { Destroy(r.crew); r.crew = null; }
                }
            }
            bool lidTiming = r.lid && showLidCommander && r.boarding == 0;
            bool wasOpen = r.hatch > 0.05f;
            float openRate = lidTiming ? 1f / Mathf.Max(0.01f, lidOpenTime) : hatchOpenRate, closeRate = lidTiming ? 1f / Mathf.Max(0.01f, lidCloseTime) : hatchCloseRate;
            r.hatch = Mathf.MoveTowards(r.hatch, r.hatchWant ? 1f : 0f, (lidTiming ? dt * SimRate : dt) * (r.hatchWant ? openRate : closeRate));
            if (r.lid && showLidCommander && r.hinge && r.obj.HasLid)
            {
                // frame it seats (as FortDefenderView does); a sniped, emptied tank keeps its lid open (lidAjar), so no close plays
                LidVoiced.Add(r.obj.Id);
                bool seatedNow = r.hatch <= 0f;
                if (r.lidSeatOk && seatedNow != r.lidSeated && lidTiming && !r.view.Dying && AudioDirector.I) AudioDirector.I.LidSound(!seatedNow, r.hinge.position, r.obj.Id, r.restLid ? RestLidGain : 1f);
                r.lidSeated = seatedNow; r.lidSeatOk = true;
            }
            if (r.hinge && wasOpen != (r.hatch > 0.05f))
            {
                if (r.restLid) OzSoundLog.Add("BV", "hatch", null, "REST " + (wasOpen ? "close " : "open ") + r.obj.Type + " #" + r.obj.Id, RestLidGain, r.hinge.position);   // DV75 idle rest: its own line in the sound log
                Sfx("hatch", r.hinge.position, (wasOpen ? 0.45f : 0.3f) * (r.restLid ? RestLidGain : 1f), r.obj.Id, wasOpen ? OzSoundV3.BankHatchShut : OzSoundV3.BankHatchOpen);   // sound_v3
                if (wasOpen && r.obj.Kind == ZKind.Vehicle) StartCoroutine(LatchLater(0.12f, r.hinge.position, r.obj.Id));
            }
            if (r.hinge && !r.view.Dying) r.hinge.localRotation = r.hingeRest * Quaternion.AngleAxis(r.hingeSign * hatchOpenDeg * Mathf.SmoothStep(0, 1, r.hatch), Vector3.right);
            r.hatchWant = false;
        }

        void SpawnCrew(Rig r)
        {
            SeatPose(r, out var p, out var q, out _);
            var go = SpawnFigure(r.crewType, r.crewTeam, p, q);
            if (!go) return;
            r.crew = go; r.crewAnim = go.GetComponentInChildren<Animator>(); r.crewWeapon = FindWeapon(go); r.crewHips = FindHips(go); r.crewSkin = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
            r.crewBase = go.transform.localScale.x; r.crewClip = null; r.crewPop = 0f; r.everCrewed = true;   // crews_v1: everCrewed
            if (r.crewWeapon) r.crewWeapon.gameObject.SetActive(!r.visible && FigureType(go) != "electrobot");   // the lid commander shoots (electrobot_v1: the
                                                                                                           // Electrobot shows no gun in a hatch: at his hip it would hang through the turret)
            if (r.lid)
            {   // ui_combat_v1 hatch commander: the gun in both hands along his aim (WeaponHold), body heights for the pop-out
                r.crewHold = WeaponHold.Attach(go);
                r.crewMuzzle = FindMuzzle(go);
                r.cmdHips = -1f; MeasureCommander(r);
                r.crewKey = CrewKey(r); r.crewAimSet = false; r.kick = 0f; r.shotAt = -1f;
            }
        }

        static string CrewKey(Rig r) => r.crewType + "|" + (int)r.crewTeam;

        void PlaceSeated(Rig r)
        {
            SeatPose(r, out var p, out var q, out var clip);
            float s = r.crewBase * r.crewScale * CrewFit(r, r.crew);                                      // electrobot_v1: the type's crew fit
            ElectrobotCoils.Set(r.crew, CrewStow(r, r.crew));
            var t = r.crew.transform;
            HoldOnTurret(r, t);                                                                            // crews_v1: a child of the gun's yaw node
            t.SetPositionAndRotation(p, q); SetWorldScale(t, s);
            if (!r.standGunner) p -= HipsOffset(r.crew, r.crewHips, r.root.up, pelvisHeight * s);  // hips on the seat
            if (r.flinch > 0f) p -= (q * Vector3.back) * 0.08f * Mathf.Sin(r.flinch / 0.25f * Mathf.PI);   // driver hit: a jolt
            t.position = p;
            Play(r.crewAnim, ref r.crewClip, clip, 1f, 0.2f);
            Band(r.crewSkin, t, r.crewHips, s);
            if (r.crewWeapon && r.crewWeapon.gameObject.activeSelf) r.crewWeapon.gameObject.SetActive(false);
        }

        /// <summary>hatch_v1: the ONE decision point for the hatch driver popping out (view only: reads the sim lid state, never
        /// writes sim state; the deterministic sim is untouched). Extra view guards (e.g. veteran crews stay buttoned up) go here.</summary>
        bool ShowHatchDriver(ZObject v) => v != null && (v.LidOpen || v.CraneGunUp || v.Resting || v.ApcDriverUp) && !VeteranMarks.ButtonedUp(v);   // vehicles_nb_v1 NB24: + the crane's driver while he shoots back (DV31); veterancy_v1 DV12: veterans stay inside; DV87: + the APC's lone driver while it fights

        /// <summary>ui_combat_v1 hatch commander (Zod ZVehicle::RenderLid, zvehicle.cpp:268-330: the "tank_fire" robot sprite over
        /// and shoulders (commanderCut); he faces and aims at the vehicle's target while engaged (else along the barrel), kicks back
        /// on each of his shots and jolts when the driver is hit.</summary>
        void PlaceCommander(Rig r, float dt)
        {
            float s = r.crewBase * r.crewScale * CrewFit(r, r.crew); var up = r.root.up;                   // electrobot_v1: the type's crew fit
            var top = Wp(r, HatchTopL(r));
            if (r.round) top += up * Vector3.Dot(r.round.RimWorld - top, up);                              // hatch_v1: the collar top is the rim
            var tgt = r.obj.DriverFireTarget ?? r.obj.AttackObject ?? r.obj.CraneGunTarget; bool engaged = tgt != null && !tgt.Destroyed;   // vehicles_nb_v1 NB24: the crane driver aims at his attacker; DV87: a tank driver at his own target first
            var front = engaged ? AimPoint(tgt) - top : (r.turret ? -r.turret.forward : -r.root.forward);   // vehicles and robots face -z
            front = Vector3.ProjectOnPlane(front, up);
            if (!engaged && r.obj.Resting) front = Quaternion.AngleAxis(RestLookDeg * Mathf.Sin(Time.time * RestLookRate + r.obj.Id * 1.7f), up) * front;   // DV75 idle rest: he looks around slowly
            var want = front.sqrMagnitude > 1e-4f ? Facing(front.normalized, up) : r.root.rotation;
            r.crewAim = r.crewAimSet && dt > 0f ? Quaternion.Slerp(r.crewAim, want, 1f - Mathf.Exp(-dt * 12f)) : want;
            r.crewAimSet = true;
            var t = r.crew.transform;
            t.SetPositionAndRotation(top, r.crewAim); t.localScale = Vector3.one * s;
            if (r.cmdHips < 0f) MeasureCommander(r);
            float cut = r.cmdHips + commanderCut * (r.cmdShoulder - r.cmdHips);                               // body height at the rim
            var shown = top + up * (0.03f * s) - up * (cut * s);
            var hidden = shown - up * ((r.cmdTop - cut + 0.35f) * s);                                         // the helmet clears the rim going down
            if (r.flinch > 0f) shown -= up * 0.08f * Mathf.Sin(r.flinch / 0.25f * Mathf.PI);                  // driver hit: a jolt
            r.kick = Mathf.MoveTowards(r.kick, 0f, dt / 0.14f);
            if (r.kick > 0f) shown += (r.crewAim * Vector3.forward) * (0.07f * s * r.kick * r.kick);          // recoil: back, away from the aim
            t.position = Vector3.Lerp(hidden, shown, Mathf.SmoothStep(0, 1, r.crewPop));
            Band(r.crewSkin, t, r.crewHips, s);
            Play(r.crewAnim, ref r.crewClip, CommanderClip(r, engaged), 1f, 0.15f);                          // electrobot_v1: his forearms on the collar
        }

        /// <summary>Hips, shoulder-joint and helmet-top heights of the crew figure above its root, in its own (unscaled) units: the
        /// </summary>
        void MeasureCommander(Rig r)
        {
            var go = r.crew; if (!go) return;
            var root = go.transform; var up = root.up; float sc = Mathf.Max(1e-4f, root.lossyScale.y);
            float H(Transform x) => Vector3.Dot(x.position - root.position, up) / sc;
            Transform hips = r.crewHips, la = null, ra = null, head = null, headTop = null;
            var an = r.crewAnim;
            if (an && an.isHuman)
            {
                la = an.GetBoneTransform(HumanBodyBones.LeftUpperArm); ra = an.GetBoneTransform(HumanBodyBones.RightUpperArm);
                head = an.GetBoneTransform(HumanBodyBones.Head);
                var hb = an.GetBoneTransform(HumanBodyBones.Hips); if (hb) hips = hb;
            }
            foreach (var x in go.GetComponentsInChildren<Transform>(true))
            {
                string n = x.name;
                if (!la && n.EndsWith("LeftArm")) la = x;
                else if (!ra && n.EndsWith("RightArm")) ra = x;
                else if (!head && n.EndsWith("mixamorigHead")) head = x;
                else if (!headTop && n.EndsWith("HeadTop_End")) headTop = x;
            }
            float hipsH = hips ? H(hips) : hipHeight;
            float sh = la && ra ? 0.5f * (H(la) + H(ra)) : la ? H(la) : ra ? H(ra) : hipsH + 1.4f;
            if (sh < hipsH + 0.2f) sh = hipsH + 1.4f;                                                          // odd rig: fallback proportions
            float topH = headTop ? H(headTop) : head ? H(head) + 0.8f * (sh - hipsH) : sh + 1.2f;
            r.cmdHips = hipsH; r.cmdShoulder = sh; r.cmdTop = Mathf.Max(topH, sh + 0.3f);
        }

        /// <summary>Where a shot at this object aims (body height of the target).</summary>
        Vector3 AimPoint(ZObject t)
        {
            var v = View(t.Id);
            var p = v && v.gameObject.activeInHierarchy ? v.transform.position : game.CenterWorld(t);
            return p + Vector3.up * (t.Kind == ZKind.Robot ? 1.1f : t.Kind == ZKind.Building ? 2.5f : 1.3f);
        }

        static Transform FindMuzzle(GameObject go)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.name == "Muzzle" || t.name == "RifleMuzzle" || t.name == "BazookaMuzzle") return t;
            return null;
        }

        const int CommanderEmitter = 7000000;                                                                // own sound throttle slot, never the vehicle's

        /// <summary>The commander fires with each shot the vehicle SHOWS (UnitView.LastFireAt: after UnitAim put the turret on the
        /// target), a beat later, while he is up and the vehicle is engaged.</summary>
        void CommanderFire(Rig r)
        {
            float fired = r.view.LastFireAt;
            if (fired > r.lastFireSeen)
            {
                r.lastFireSeen = fired;
                var t = r.obj.AttackObject;
                if (r.obj.Type == "crane" || r.obj.Type == "apc") { if (r.crewPop > 0.5f) r.kick = 1f; }     // vehicles_nb_v1 NB24: his own sim shot (DV31) left his muzzle: recoil only; DV87: the APC's lone driver too
                else if (!ZWorld.DriverFireDeviation && r.crewPop > 0.85f && t != null && !t.Destroyed) r.shotAt = Time.time + commanderShotDelay;   // DV87: with the rule on a tank driver's shots are the sim's own (DriverShotSeen), no longer a picture with the tank's gun
            }
            if (r.shotAt > 0f && Time.time >= r.shotAt) { r.shotAt = -1f; CommanderShot(r); }
        }

        /// <summary></summary>
        public static int CommanderShots, CommanderRockets, CommanderRocketsLanded, CommanderFlames, CommanderLasers, Latches;
        /// <summary>
        /// (the Tiger-style breech clank takes, pitched up a little: a lighter lock bolt).</summary>
        public static readonly string[] BankLatch = { "sd_tank_breech_clank_a", "sd_tank_breech_clank_b" };
        public const float GainLatch = 0.55f, PitchLatch = 1.15f;

        public static void Latch(Vector3 pos, int emitter)
        {
            var au = CombatAudio.I; if (!au) return;
            au.PlayPitched("latch", pos, GainLatch, 0.2f, emitter, PitchLatch, BankLatch); Latches++;
        }

        System.Collections.IEnumerator LatchLater(float s, Vector3 pos, int emitter)
        {
            yield return new WaitForSeconds(s);
            if (sounds) Latch(pos, emitter);
        }

        /// <summary>HW1: the Tough commander's bazooka rocket lands (view only: the vehicle's sim shot does the damage), a small quiet
        /// blast (no Exploded listeners) with its boom.</summary>
        System.Collections.IEnumerator CommanderRocketLands(Vector3 at, float secs, int emitter, int id)
        {
            yield return new WaitForSeconds(secs);
            var d = LegacyMatch.Bazooka; var z = OpenZFxPlayer.I;
            if (z && z.isActiveAndEnabled)
            {
                OpenZFxPlayer.QuietBlast = true;
                try { z.BlastSized(at + Vector3.up * 0.2f, d.s * 0.7f, d.legRadius * 0.6f, d.kind, id); }
                finally { OpenZFxPlayer.QuietBlast = false; }
            }
            else if (FxManager.I) FxManager.I.Pop(at, 0.6f);
            var au = CombatAudio.I;
            if (sounds && au && d.clips != null) au.PlayPitched(d.key, at, d.gain * 0.7f, 0.05f, emitter, d.pitch, d.clips);
            CommanderRocketsLanded++;
        }

        /// <summary>
        /// bazooka rocket (back-blast, launch + flight roar, a small blast), the Pyro a flame burst (short range), the Laser his green
        /// beam with its zip and zap; the others a muzzle flash. All a little quieter than the vehicle's own gun.</summary>
        void CommanderShot(Rig r)
        {
            var t = r.obj.AttackObject;
            if (r.crewWeapon && !r.crewWeapon.gameObject.activeSelf) return;                               // electrobot_v1: a commander without his gun
            if (t == null || t.Destroyed || !r.crew || !r.crew.activeInHierarchy) return;
            if (!r.crewMuzzle) r.crewMuzzle = FindMuzzle(r.crew);
            float s = r.crewBase * r.crewScale * CrewFit(r, r.crew);
            var from = r.crewMuzzle ? r.crewMuzzle.position : r.crew.transform.position + r.root.up * (r.cmdShoulder * s) + (r.crewAim * Vector3.back) * (0.6f * s);
            var to = AimPoint(t) + UnityEngine.Random.insideUnitSphere * 0.3f;
            { Transform body = game.Views.TryGetValue(t.Id, out var tv) && tv ? tv.transform : game.StaticViews.TryGetValue(t.Id, out var sgo) && sgo ? sgo.transform : null;   // IM1: on the real surface
              if (body && HitSurface.OnBody(body, from, to, out var hp, out _)) to = hp; }
            r.kick = 1f;
            string type = r.crewType;
            CommanderShots++;
            if (type == "tough" || type == "pyro" || type == "laser") { CommanderOwnWeapon(r, type, from, to); return; }   // HW1
            bool tracer = type == "grunt" || type == "psycho" || type == "sniper";
            var w = tracer ? LegacyMatch.ShotWeapon(type) : FxWeapon.Rifle;
            var z = OpenZFxPlayer.I;
            if (z && z.isActiveAndEnabled)
            {
                if (tracer) z.Shot(from, to, w, LegacyMatch.SurfaceOf(t, true), LegacyMatch.ShotSpeed(game ? game.gameSpeed : 1f));
                else z.Muzzle(from, to - from, w);
            }
            else if (FxManager.I) FxManager.I.MuzzleFlash(from, to - from, false);
            var au = CombatAudio.I;
            if (!sounds || !tracer || !au) return;
            int em = r.obj.Id + CommanderEmitter;
            if (w == FxWeapon.Bolt) au.PlayPitched("bolt", from, LegacyMatch.GainBolt * commanderShotGain, 0.15f, em, 1f, LegacyMatch.BankBolt);
            else if (w == FxWeapon.Sniper) au.PlayPitched("sniper", from, LegacyMatch.GainSniper * commanderShotGain, 0.15f, em, 0f, LegacyMatch.BankSniper);
            else au.PlayPitched("rifle", from, LegacyMatch.GainDefault * commanderShotGain, 0.15f, em, 1f, LegacyMatch.BankRifleSingle);
        }

        /// <summary>HW1: the Tough / Pyro / Laser commander's own weapon from the hatch (view only).</summary>
        void CommanderOwnWeapon(Rig r, string type, Vector3 from, Vector3 to)
        {
            var au = sounds ? CombatAudio.I : null; var fx = FxManager.I; var z = OpenZFxPlayer.I;
            int em = r.obj.Id + CommanderEmitter;
            var d = to - from; float dist = d.magnitude; if (dist < 0.3f) return;
            var dir = d / dist; var flat = new Vector3(dir.x, 0f, dir.z); flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
            switch (type)
            {
                case "tough":
                {
                    float gs = game ? Mathf.Max(0.05f, game.gameSpeed) : 1f;
                    float secs = Mathf.Clamp(dist / 24f, 0.25f, 1.6f);                                     // real seconds (~ the Tough's rocket)
                    if (z && z.isActiveAndEnabled) z.Launch(from, to, secs * gs, FxWeapon.Bazooka, true);  // Launch divides by the game speed
                    else if (fx) fx.Projectile(from, to, "rocket", secs, 6f);
                    LegacyMatch.BackBlast(from - dir * 0.8f, flat, fx);
                    if (au) { LegacyMatch.LaunchSound("tough", from, em, au); LegacyMatch.RocketFlight(from, to, secs * gs, em, au); }
                    StartCoroutine(CommanderRocketLands(to, secs, em, (r.obj.Id * 7919 + CommanderRockets * 131 + 23) & 0x3fffffff));
                    CommanderRockets++;
                    break;
                }
                case "pyro":
                {
                    var end = dist > 7f ? from + dir * 7f : to;                                              // a flamethrower's reach
                    if (fx) fx.Flame(from, end);
                    if (z && z.isActiveAndEnabled) z.Muzzle(from, dir, FxWeapon.Rifle);
                    if (au) au.Play("flame", from, 0.75f * commanderShotGain * 1.4f, 0.6f, em, "flame_loop1");
                    CommanderFlames++;
                    break;
                }
                case "laser":
                {
                    if (fx) fx.Laser(from, to);
                    if (au)
                    {
                        au.PlayPitched("laser", from, WeaponsV2.GainLaserFire * commanderShotGain * 1.3f, 0.15f, em, 1f, WeaponsV2.BankLaserFire);
                        au.PlayPitched("laser_hit", to, WeaponsV2.GainLaserZap * commanderShotGain * 1.3f, 0.15f, em, 1f, WeaponsV2.BankLaserZap);
                    }
                    CommanderLasers++;
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ crews_v1: gunners on every cannon type
        bool Tower(Rig r) => r.view && r.view.OnTower;

        /// <summary>The cannon's drawn size against its prefab (tower cannons 0.72): the gunner is scaled with the gun.</summary>
        float ScaleRatio(UnitView v, ZObject o)
        {
            var pf = game.Prefab("Z_" + o.Type);
            float b = pf ? Mathf.Abs(pf.transform.localScale.x) : 1f;
            return Mathf.Clamp(Mathf.Abs(v.transform.lossyScale.x) / Mathf.Max(0.01f, b), 0.3f, 1.5f);
        }

        static Transform FindChild(Transform root, string name)
        {
            foreach (Transform c in root) if (c.name == name) return c;
            foreach (var c in root.GetComponentsInChildren<Transform>(true)) if (c.name == name) return c;
            return null;
        }

        /// <summary>Split imported cannons (gatling / howitzer / missile cannon: Base + TurretYaw, the whole Model hidden): the measured
        /// seat (GenSpots, fractions of the Model bounds) belongs to the turret, which UnitAim turns: the gatling's chair is part of
        /// the yaw mesh (gatling_yaw has 325 vertices in the seat column, the base 141 below 1.21 m), and the missile cannon's pods
        /// would sweep through a gunner left on the base. Seat and facing are kept in turret space, from the prefab's rest pose.</summary>
        void CarrySeat(Rig r, ZObject o)
        {
            if (r.gen.turretSeat)
            {   // gunner_seats_v1: the seat is part of the TurretYaw mesh, the socket is in its space already; he faces the barrel (+x)
                r.seatInTurret = r.gen.seat; r.frontInTurret = r.gen.face; r.seatOnTurret = true;
                return;
            }
            var rest = r.root.worldToLocalMatrix * r.turret.localToWorldMatrix;                            // fallback: the pose right now
            var pf = game.Prefab("Z_" + o.Type);
            var pt = pf ? FindChild(pf.transform, r.turret.name) : null;
            if (pt) rest = pf.transform.worldToLocalMatrix * pt.localToWorldMatrix;                         // rest pose (UnitAim may be scanning already)
            var inv = rest.inverse;
            var seatW = MP(r, r.gen.seat);
            if (r.gen.ground > 0f)
            {   // crews_v2 howitzer: on the ground `ground` m from the turret pivot, along the measured spot's bearing (clear of the base
                // plate and the turret at all 72 turret angles: tools/howitzer_ground.py)
                var pv = r.root.InverseTransformPoint(r.turret.position); var sl = r.root.InverseTransformPoint(seatW);
                var dg = new Vector3(sl.x - pv.x, 0f, sl.z - pv.z); dg = dg.sqrMagnitude > 1e-6f ? dg.normalized : Vector3.left;
                seatW = r.root.TransformPoint(new Vector3(pv.x, 0f, pv.z) + dg * r.gen.ground);
                r.groundStand = true;
            }
            r.seatInTurret = inv.MultiplyPoint3x4(r.root.InverseTransformPoint(seatW));
            if (r.gen.saddle) GunnerSaddle.Build(r.turret, inv, r.root.InverseTransformPoint(seatW));   // crews_v2: the gatling's outrigger seat
            var fr = Vector3.ProjectOnPlane(r.model.right, r.root.up);
            r.frontInTurret = inv.MultiplyVector(r.root.InverseTransformDirection(fr.sqrMagnitude > 1e-6f ? fr.normalized : -r.root.forward)).normalized;
            r.seatOnTurret = true;
            if (!r.standGunner || r.groundStand) return;
            r.baseT = FindChild(r.root, "Base");
            var mf = r.baseT ? r.baseT.GetComponent<MeshFilter>() : null;
            if (mf && mf.sharedMesh) { r.baseTop = MeshTopSampler.For(mf.sharedMesh); r.seatBaseY = r.baseT.InverseTransformPoint(seatW).y; }
            else r.baseT = null;
        }

        /// <summary>Seat (hips) or standing spot (feet) carried by the turret; a standing gunner stands on the base plate under it
        /// (sampled), else on the mount / pad of a tower gun, else on the ground beside the gun.</summary>
        Vector3 CarriedSeatRaw(Rig r)
        {
            var w = TurretYawPoint(r, r.seatInTurret);
            if (r.groundStand) return OnSurface(r, w);                                                     // crews_v2: howitzer gunner on the ground / tower top
            if (!r.standGunner || !r.baseT) return w;
            var l = r.baseT.InverseTransformPoint(w);
            if (r.baseTop != null)
            {
                // the plate's edge and his feet flipped between the plate and the ground every few frames. The spot now slides in toward
                // the turret's axis until BOTH boots (0.18 m either side) stand on the plate: one stable footing per bearing.
                var c = r.turretRestInit && r.turret && r.turret.parent ? r.baseT.InverseTransformPoint(r.turret.parent.TransformPoint(r.turretRestPos)) : Vector3.zero;
                var side = Vector3.Cross(Vector3.up, new Vector3(l.x - c.x, 0f, l.z - c.z)).normalized * (0.18f / Mathf.Max(1e-4f, r.baseT.lossyScale.x));
                for (float f = 1f; f >= 0.35f; f -= 0.05f)
                {
                    float qx = c.x + (l.x - c.x) * f, qz = c.z + (l.z - c.z) * f;
                    if (r.baseTop.Top(qx, qz, r.seatBaseY + 0.45f, out var y) && r.baseTop.Top(qx + side.x, qz + side.z, r.seatBaseY + 0.45f, out var y1)
                        && r.baseTop.Top(qx - side.x, qz - side.z, r.seatBaseY + 0.45f, out var y2)
                        && Mathf.Max(y, Mathf.Max(y1, y2)) - Mathf.Min(y, Mathf.Min(y1, y2)) < 0.04f / Mathf.Max(1e-4f, r.baseT.lossyScale.y))   // flat footing: never astride a step
                    { l.x = qx; l.z = qz; l.y = Mathf.Max(y, Mathf.Max(y1, y2)); return r.baseT.TransformPoint(l); }
                }
            }
            return OnSurface(r, w);                                                                        // off the plate
        }

        /// <summary>
        /// missile-cannon gunner drifted 6-45 cm per 0.5 s while his turret stood, the gatling's 5-7 cm): the carried seat followed the
        /// turret node's FULL transform, so every recoil kick and pitch jolt threw the gunner around. The seat now turns with the
        /// turret's yaw only, about the node's rest pivot: still exactly the old point at rest.</summary>
        Vector3 TurretYawPoint(Rig r, Vector3 local)
        {
            var t = r.turret; var p = t ? t.parent : null;
            if (!t || !p) return t ? t.TransformPoint(local) : Vector3.zero;
            if (!r.turretRestInit) { r.turretRestPos = t.localPosition; r.turretRestRot = t.localRotation; r.turretRestInit = true; }
            var up = p.up; var restWorld = p.rotation * r.turretRestRot;
            var axis = Mathf.Abs(Vector3.Dot(restWorld * Vector3.forward, up)) < 0.7f ? Vector3.forward : Vector3.right;   // a local axis that lies flat at rest
            float yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(restWorld * axis, up), Vector3.ProjectOnPlane(t.rotation * axis, up), up);
            var rot = Quaternion.AngleAxis(yaw, up) * restWorld;
            return p.TransformPoint(r.turretRestPos) + rot * Vector3.Scale(local, t.lossyScale);
        }

        /// <summary>crews_v2: feet on what is really under a point: on a tower the mount top, else the fort's own mesh (flat top,
        /// ledge ring, dome: TowerMounts.FortSurface), else the ledge height; on the ground GroundY (terrain, bridge decks).</summary>
        Vector3 OnSurface(Rig r, Vector3 w)
        {
            if (Tower(r))
            {
                var c = MountCentre(r); var d = w - c; d.y = 0f;
                if (d.magnitude <= TowerMounts.MountRadius) { w.y = c.y; return w; }
                float best = float.NegativeInfinity;                                                       // the highest fort surface under the boot footprint:
                for (int k = 0; k < 5; k++)                                                                // never sunk into the dome's slope (tools/check_crews_v2.py)
                {
                    var q = w + (k == 0 ? Vector3.zero : Quaternion.Euler(0f, k * 90f, 0f) * new Vector3(0.15f, 0f, 0f));
                    if (TowerMounts.FortSurface(r.view.TowerFort, q, c.y + 0.3f, out var fy)) best = Mathf.Max(best, fy);
                }
                if (!float.IsNegativeInfinity(best)) { w.y = best + RobotY; return w; }
                w.y = c.y - (TowerMounts.MountHeight - TowerMounts.SeatSink) - TowerMounts.LedgeDrop + RobotY;
                return w;
            }
            w.y = game.GroundY(w.x, w.z) + RobotY;
            return w;
        }

        Vector3 CarriedSeat(Rig r)
        {
            var w = CarriedSeatRaw(r);
            if (r.standGunner && r.feetInit) w.y = r.feetY;
            return w;
        }

        /// <summary>Centre of a tower gun's ring mount at the gun's root height: the turret's turn axis (UnitView puts it on the pad centre).</summary>
        Vector3 MountCentre(Rig r)
        {
            if (r.view && r.view.OnTower) return TowerMounts.SeatWorld(r.view.TowerFort, r.view.TowerPad);   // crews_v2: the mount's own centre (the base plate, not the turn axis, is on the pad)
            var c = r.turret ? r.turret.position : r.root.position; var up = r.root.up;
            return c - up * Vector3.Dot(c - r.root.position, up);
        }

        /// <summary>
        /// crews rise to the platform: seatTransform / platformHeight). below = head under the roof.</summary>
        Vector3 TowerHatch(Rig r, bool below)
        {
            var up = r.root.up; var c = MountCentre(r);
            SeatPose(r, out var sp, out var sq, out _);
            var d = Vector3.ProjectOnPlane(sp - c, up);
            if (d.sqrMagnitude < 0.04f) d = Vector3.ProjectOnPlane(sq * Vector3.forward, up);                    // robot front = -z: +z is behind him
            d = d.sqrMagnitude > 1e-4f ? d.normalized : -r.root.forward;
            var p = c + d * TowerMounts.LedgeRadius - up * (TowerMounts.MountHeight - TowerMounts.SeatSink + TowerMounts.LedgeDrop);   // crews_v2: on the ledge ring
            if (r.view.TowerFort && TowerMounts.FortSurface(r.view.TowerFort, p, p.y + TowerMounts.LedgeDrop + 0.3f, out var fy)) p.y = fy;
            p += up * RobotY;
            if (below) p -= up * (0.5f + 3.6f * Mathf.Max(0.3f, r.crewScale));
            return p;
        }

        /// <summary></summary>
        void BuildTowerMan(Ghost g, Vector3 start)
        {
            var r = g.rig; var keys = g.keys; keys.Clear();
            Func<Vector3> S = () => start;
            Func<Vector3> rim = () => TowerHatch(r, false);
            Func<Vector3> seat = () => SeatRoot(r, g);
            keys.Add(K(S, 0, "idle", Face.Keep));
            float h = Mathf.Abs(Vector3.Dot(rim() - start, r.root.up));
            keys.Add(K(rim, Mathf.Max(0.35f, h / climbSpeed), "climb_ladder", Face.Hull, clipSpeed: Mathf.Clamp(climbSpeed / 2f, 1f, 3f)));
            if (HighSeat(r))
            {   // gunner_seats_v1: up onto the gun's platform, a step, sit
                Func<Vector3> edge = () => DeckPoint(r, EdgeOut), deck = () => DeckPoint(r, 0f);
                keys.Add(K(edge, 0.3f, "climb_ladder", Face.Hull, arc: 0.2f, clipSpeed: 2f));
                keys.Add(K(deck, 0.18f, "walking", Face.Seat, ease: true));
                keys.Add(K(seat, sitTime, "seated_gunner", Face.Seat, ease: true));
                Finish(g, 1.5f);
            }
            else
            {
                keys.Add(K(seat, r.standGunner ? 0.3f : sitTime + 0.1f, r.standGunner ? "walking" : "seated_gunner", Face.Seat, ease: true, arc: r.standGunner ? 0f : 0.2f));
                Finish(g, 1.1f);
            }
            g.scaleA = g.scaleB = r.crewScale; g.scaleSpan = Mathf.Max(0.05f, g.total);                       // already crew size under the roof
            g.fitA = g.fitB = CrewFit(r, g.go); g.fitT0 = 0f; g.fitT1 = -1f; g.stowA = g.stowB = CrewStow(r, g.go);   // electrobot_v1: already fitted
        }

        /// <summary>
        /// A cannon's seated / standing gunner is a child of the yaw node UnitAim turns (TurretYaw on the imported
        /// whatever order the scripts run in; his local seat offset stays (PlaceSeated re-poses him from the turret-space seat).
        /// Unparent() puts him back under OZ_BoardingCrew before a snipe / death fling or when he gets up to leave.</summary>
        void HoldOnTurret(Rig r, Transform t)
        {
            var holder = r.mode == Mode.Cannon && r.turret ? r.turret : null;
            if (!holder) return;
            if (t.parent != holder)
            {
                t.SetParent(holder, true);
                r.gunPaint = r.view ? r.view.GetComponent<TeamPaint>() : null; r.crewPaint = r.crew ? r.crew.GetComponent<TeamPaint>() : null;
                r.gunPaintCharred = -1f;
            }
            SyncCrewPaint(r);
        }

        /// <summary>The gun's TeamPaint paints every renderer under it (the parented gunner too) whenever its owner or char level
        /// changes: the gunner gets his own paint back at once (his colours never change here).</summary>
        void SyncCrewPaint(Rig r)
        {
            if (!r.gunPaint || !r.crewPaint) return;
            if (r.gunPaintCharred >= 0f && r.gunPaint.Team == r.gunPaintTeam && Mathf.Approximately(r.gunPaint.Charred, r.gunPaintCharred)) return;
            bool first = r.gunPaintCharred < 0f;
            r.gunPaintTeam = r.gunPaint.Team; r.gunPaintCharred = r.gunPaint.Charred;
            if (!first) r.crewPaint.Apply();
        }

        void Unparent(GameObject go) { if (go && box && go.transform.parent != box) go.transform.SetParent(box, true); }

        static void SetWorldScale(Transform t, float s)
        {
            float ps = t.parent ? Mathf.Abs(t.parent.lossyScale.x) : 1f;
            t.localScale = Vector3.one * (s / Mathf.Max(1e-4f, ps));
        }

        /// <summary>A gun placed at run time (Zod: placed with a Grunt driver in the same tick, zserver_events.cpp:557-598): the
        /// view shows the crew man it instead of popping in: from 2 m behind the gun he walks up and sits (the cannon boarding
        /// path), on a tower he climbs up through the roof. Squeezed into catchUp as soon as the gun fires, like every boarding.</summary>
        void StartMan(Rig r)
        {
            r.everCrewed = true;
            SeatPose(r, out var sp, out var sq, out _);
            bool tower = Tower(r);
            Vector3 start;
            if (tower) start = TowerHatch(r, true);
            else
            {
                var b = Behind(r); var d = b - sp; d.y = 0f;
                if (d.sqrMagnitude < 1e-4f) { d = sq * Vector3.forward; d.y = 0f; }
                start = Ground(b + (d.sqrMagnitude > 1e-4f ? d.normalized : -r.root.forward) * 1.8f);
            }
            var face = sp - start; face.y = 0f;
            var go = SpawnFigure(r.crewType, r.crewTeam, start, face.sqrMagnitude > 1e-4f ? Facing(face.normalized, Vector3.up) : r.root.rotation);
            if (!go) { SpawnCrew(r); return; }
            var g = NewGhost(go, r, true);
            if (tower) BuildTowerMan(g, start); else BuildEnter(g, start);
            r.boarding++; r.acted = false;
            ghosts.Add(g);
            Place(g, 0f);
        }

        // ------------------------------------------------------------------ preview (no sim involved)
        /// <summary>Plays a full board-then-leave cycle of this robot on this vehicle, visual only (the robot is hidden
        /// meanwhile and shown again after). For checking anchors in play mode without touching the Zod state.</summary>
        public void Preview(UnitView robot, UnitView vehicle)
        {
            var r = RigOf(vehicle); if (r == null || !robot || robot.Obj == null || robot.Obj.Kind != ZKind.Robot) return;
            var go = CloneView(robot);
            var g = NewGhost(go, r, true); g.live = robot; g.demo = true;
            HideLive(g);
            BuildEnter(g, robot.transform.position);
            float enterTime = g.total;
            SeatPose(r, out _, out _, out var seated);
            g.keys.Add(K(g.keys[g.keys.Count - 1].at, 0.6f, r.mode == Mode.Seat || r.mode == Mode.Cannon ? seated : "idle", Face.Seat, hatch: r.mode == Mode.Roof));
            g.backAt = enterTime + 0.6f;                                                                  // then out again, back to full size
            BuildExit(g, true);
            g.total = Sum(g.keys); g.scaleSpan = enterTime;
            ghosts.Add(g);
        }

        [ContextMenu("Preview boarding on every vehicle and cannon")]
        public void PreviewAll()
        {
            if (!game) game = ZGame.I; if (!game) return;
            var robots = new List<UnitView>();
            foreach (var kv in game.Views) if (kv.Value && kv.Value.Obj != null && kv.Value.Obj.Kind == ZKind.Robot && kv.Value.gameObject.activeInHierarchy && !kv.Value.Dying) robots.Add(kv.Value);
            var used = new HashSet<UnitView>();
            foreach (var kv in game.Views)
            {
                var v = kv.Value; if (!v || v.Obj == null || !v.gameObject.activeInHierarchy || v.Dying) continue;
                if (v.Obj.Kind != ZKind.Vehicle && v.Obj.Kind != ZKind.Cannon) continue;
                if (v.OnTower) continue;                                                                   // crews_v1: tower guns are manned from inside the fort
                UnitView best = null; float bd = 14f * 14f;
                foreach (var rv in robots) { if (used.Contains(rv)) continue; float d = (rv.transform.position - v.transform.position).sqrMagnitude; if (d < bd) { bd = d; best = rv; } }
                if (best) { used.Add(best); Preview(best, v); }
            }
        }
    }
}
