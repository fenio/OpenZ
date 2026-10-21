using System;
using System.Collections.Generic;
using System.Reflection;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// The big gun on top of every fort (view only, Zod parity untouched).
    ///
    /// Z rule (plan/research/zod_buildings_production_territory.md, "Fort guns: forts do not shoot"): a fort never fires. Its
    /// guns are the ordinary cannons on its 4 turret spots (local px (16,0) (16,48) (112,0) (112,48), bfort.cpp:534-548,
    /// ZWorld.MarkFortTurretCannons ZWorld.cs:160-166 / ZWorld.Rules.cs:305-320). So the central gun of the imported fort is a
    /// view of THOSE cannons: it tracks what they shoot at (their AttackObject), or, while they are idle, the nearest enemy
    /// inside their range, and it fires a heavy shell every time they really fire (their MissileFired / Hitscan events),
    /// at most once per <see cref="minShotInterval"/>. The shell is cosmetic (no damage, no sim call); it lands on the same
    /// target the real cannon is hitting. A fort with no turret cannon (L02 blue fort) only tracks, it never fires.
    ///
    /// Look: heavy slew (smooth yaw + pitch, capped speeds), a ballistic lob (barrel elevates for far targets), big muzzle
    /// blast (flash light, forward fire, smoke ring, roof dust), barrel recoil (fast kick, slow return), cradle shove + nod +
    /// a damped turret twist, a visible heavy shell with a hot tracer and a smoke trail, a large impact explosion (the fx
    /// helper's OpenZFxPlayer when installed, else FxManager), a light camera shake when on screen, AudioDirector sound events
    /// (soft-bound; CombatAudio fallback). The barrel never swings through the fort flag (keep-out sector, slews the long way
    /// round). When the fort dies the turret is blown off, spins through the air and crashes down beside the ruin (classic Z,
    /// like UnitView.TossTurret), burns, then sinks.
    ///
    /// fortgun_v2: every shell (the DV7 real one and the cosmetic one) leaves ALONG THE BARREL and lands on its impact at its
    /// flight time: the arc's gravity follows from the launch elevation (<see cref="LobArc"/>), and the elevation is the lob style
    /// for the range, raised until the arc clears the fort (tower pads 9.25 m, the cannons standing on them by their bounds,
    /// walls 8.76 m, the flag pole and its cloth flying downwind) by <see cref="arcClearance"/>; the barrel lays onto that
    /// elevation before the shot (max 26 deg, still inside the mantlet sleeve). With the sim's DV7 envelope (64 px from the
    /// walls, no fire into the rear +-34 deg arc behind the flag and its cloth) every arc clears with the barrel's own elevation
    /// Recoil: 0.95 m barrel run-back, 0.14 m cradle shove, muzzle climb, a turret rock on the rear foot of its ring, a smoke
    ///
    /// With ZWorld.FortMortarDeviation the barrel lays at mortarElevation (60 deg,
    /// the sim's lob) and the shell flies the sim's lob time (ZObject.FortMortarFlight, 2.9-4.2 s) at where the target stood (the sim
    /// does not lead: it can be dodged); a flat amber ring round the turret fills over the 30 s reload (ReloadRing); every shot adds a
    /// deep second boom, the cannon crack and the shell whoosh (MortarSounds); a landing on screen shakes the camera with a heavy boom
    /// (ShellLanded). A gun destroyed in the sim (every enemy shell that damages the fort damages its gun) is blown off its ring
    /// (StartToss, the classic Z turret toss) and the fort stays silent.
    ///
    /// of the turret-ring band, v2), FortGunPitch (trunnion, + a hidden sleeve into the housing), FortGunRecoil (bore rear), FortGunMuzzle (tip), found by name anywhere under the fort
    /// view; a flat FBX import is re-chained Yaw > Pitch > Recoil > Muzzle at bind. Axes are measured from the rest pose, so
    /// any importer axis convention works. Installs itself on every fort view that has these nodes (AfterSceneLoad).
    /// </summary>
    [DefaultExecutionOrder(45)]   // LateUpdate reads ZWorld.Events BEFORE CombatView (order 50) clears them (like AudioDirector, 40)
    public class FortGunView : MonoBehaviour
    {
        [Header("Nodes (found by name under the fort view)")]
        public string yawNode = "FortGunYaw";
        public string pitchNode = "FortGunPitch", recoilNode = "FortGunRecoil", muzzleNode = "FortGunMuzzle";

        [Header("Heavy slew")]
        [Tooltip("Max yaw speed (deg/s)")] public float yawMaxSpeed = 38f;
        [Tooltip("Yaw smoothing time (s): accelerates and settles like a heavy turret")] public float yawSmooth = 0.55f;
        public float pitchMaxSpeed = 16f, pitchSmooth = 0.4f;
        [Tooltip("Barrel elevation limits (deg above horizontal); fortgun_v2: +26 (the 0.40 m mantlet sleeve covers the housing joint to ~24 deg, 3 cm at 26 on the underside)")] public float minElevation = -6f, maxElevation = 26f;
        [Tooltip("fortgun_v2: catch-up slew cap (deg/s) while a new DV7 target is being laid (the sim fires 1.4 s after it picks one)")] public float catchUpMaxSpeed = 160f;
        [Tooltip("Fire only when yaw and pitch are within this many degrees of the aim")] public float aimTolerance = 3f;

        [Header("Firing (cosmetic heavy shell)")]
        [Tooltip("At most one heavy shot per this many seconds (the fort cannons may fire faster)")] public float minShotInterval = 2.6f;
        [Tooltip("A fire request not served within this time (turret still slewing) is dropped")] public float pendingTimeout = 2.5f;
        [Tooltip("Launch elevation for near / far targets (deg); the flight time follows (ballistic lob)")] public float elevNear = -4f, elevFar = 16f;
        public float minFlight = 0.7f, maxFlight = 3.2f;
        [Tooltip("Impact explosion strength (FxManager.Explosion; the Z tank shell uses ~1.0)")] public float impactStrength = 1.9f;
        [Tooltip("With the fx helper's OpenZFxPlayer: blast radius (m) of FxBlastKind.Missile (M12 strength = r / 4; a Howitzer hit is 12.5 m)")] public float fxImpactRadius = 9f;
        [Tooltip("Camera shake when the gun fires on screen (RtsCamera.Shake)")] public float fireShake = 0.2f;

        [Header("Recoil")]
        [Tooltip("Barrel slide (m) along the bore; fortgun_v2 0.75 -> 0.95 (the muzzle sleeve stops 0.12 m short of the mantlet)")] public float recoilDistance = 0.95f;
        [Tooltip("Kick (s), hold (s), return (s)")] public float recoilKick = 0.05f, recoilHold = 0.10f, recoilReturn = 1.25f;
        [Tooltip("Cradle shove (m): the mantlet is pushed back into the housing (never the turret off its ring; its 0.40 m sleeve hides the joint), muzzle climb (deg)")] public float joltDistance = 0.14f, joltNod = 3.2f;
        [Tooltip("Turret jolt: a damped twist about the yaw axis (deg); keeps the ring seam closed")] public float joltTwist = 1.1f;
        [Tooltip("fortgun_v2 body kick: the turret rocks back on the rear foot of its ring (deg; the ring front lifts ~4 cm per deg for ~0.2 s)")] public float turretRock = 1.0f;
        [Tooltip("Rear foot of the turret ring behind the yaw axis (m; ring mean radius 2.16, Fort_Split v2)")] public float rockPivotBack = 2.1f;

        [Header("Head tilt (the whole head pitches up with the shot, not only the nose)")]
        [Tooltip("Head pitch (deg) while a shot is laid and fired; the barrel's own elevation in the head shrinks by the same amount, so the lob angle is unchanged")] public float headTilt = 30f;
        [Tooltip("The head starts to rise this long (real s) before the sim's shot (the lay / lob wind-up)")] public float tiltWindUp = 1.1f;
        [Tooltip("Rise / settle smoothing (s)")] public float tiltRiseSmooth = 0.28f, tiltFallSmooth = 0.6f;
        [Tooltip("Held after the kick (s), then the head eases back down")] public float tiltHold = 0.8f;
        [Tooltip("Hinge line (FortGunYaw mesh space, FBX units): the head rests on its ring at the rear-centre, so it lifts at the front and never dips into the ring or the roof (checked offline: <= 3 cm at 12 deg, every heading)")] public Vector3 tiltPivot = new Vector3(0f, 0.045f, -0.25f);
        [Tooltip("Split height (FortGunYaw mesh space): triangles above it are the head that tilts, below it the ring that only turns")] public float tiltSplitY = 0.043f;
        /// <summary></summary>
        public static bool HeadTiltEnabled = true;
        [Header("Reload drop")]
        [Tooltip("Between mortar shots the barrel drops to the reload angle and climbs back before the next shot")] public bool reloadDrop = true;
        [Tooltip("Reload barrel angle above the rest elevation (deg)")] public float reloadElev = 8f;
        [Tooltip("Real seconds before the next shot the barrel starts its climb onto the lob")] public float reloadRaiseLead = 5f;
        bool reloadDown;
        /// <summary>Proof counters: reload drops / climbs back onto the lob (all fort guns).</summary>
        public static int ReloadDrops, ReloadRaises;

        [Header("Arc (fortgun_v2): the shell leaves along the barrel and clears the fort")]
        [Tooltip("Lob elevation (deg) for near (14 m) / far (26 m) impacts; raised per shot until the arc clears the fort")] public float lobNear = 10f, lobFar = 20f;
        [Tooltip("Air between the shell and the towers, tower cannons, walls, flag pole and cloth (m)")] public float arcClearance = 0.45f;
        [Tooltip("Extra air while laying the barrel (m): the sim scatters the impact +-16 px after the lay, so the laid elevation keeps a margin and the real shot still leaves along the barrel")] public float aimMargin = 0.8f;
        [Tooltip("Steepest launch (deg) when even the barrel's limit cannot clear (a tower cannon in the way of a scattered impact)")] public float maxLaunch = 45f;
        [Tooltip("Visual gravity range (m/s^2) the arc may use")] public float arcGravityMin = 3f, arcGravityMax = 60f;

        [Header("Keep-out (the fort flag stands 6 m behind the turret, inside the barrel's sweep)")]
        [Tooltip("Never swing the barrel through the fort flag: slew the long way round, park at the edge of the flag sector")] public bool avoidFlag = true;
        [Tooltip("Clearance (m) between the barrel and the flag pole / cloth")] public float flagClearance = 0.35f;

        [Header("Idle / targeting")]
        [Tooltip("Slow scan around the rest heading when nothing is in range")] public bool idleScan = true;
        public float idleScanAmplitude = 26f, idleScanPeriod = 24f;
        [Tooltip("Without turret cannons: still track enemies near the fort (never fires; Zod 'fort in danger' 250 px)")] public bool trackWithoutCannons = true;
        public int dangerRadiusPx = 250;

        [Header("Sound: AudioDirector event ids")]
        public string sndFire = "wpn_fortgun";
        public string sndFireAlt = "wpn_heavy_tank", sndBreech = "wpn_breech_clank", sndWhistle = "proj_shell_whistle", sndImpact = "exp_large",
                      sndTraverse = "veh_turret_traverse", sndToss = "dst_turret_toss", sndCrash = "dst_turret_crash";

        [Header("Death")]
        public float tossDuration = 3.0f;
        public float tossApex = 9f, tossSpin = 330f, wreckSeconds = 12f;

        [Header("Optional materials (built from OpenZ/FxLit, OpenZ/Beam, Sprites/Default when empty)")]
        public Material smokeMaterial;
        public Material shellMaterial, tracerMaterial;

        /// <summary>Muzzle override (muzzle, bore): return true when handled. Without it: OpenZFxPlayer (soft-bound) or FxManager.</summary>
        public static Func<Vector3, Vector3, bool> MuzzleBlastOverride;
        /// <summary>Impact override (point, strength) -> handled. Without it: OpenZFxPlayer.Blast (soft-bound) or FxManager.Explosion.</summary>
        public static Func<Vector3, float, bool> ImpactOverride;
        /// <summary>Raised on every heavy shot (view, muzzle, aim point).</summary>
        public static event Action<FortGunView, Vector3, Vector3> Fired;

        public bool Bound { get; private set; }
        public ZObject Fort { get; private set; }
        public ZObject Target => target;
        static readonly HashSet<int> bound = new HashSet<int>();
        /// <summary>DV7: this fort's own howitzer shots are drawn by its FortGunView (CombatView skips them).</summary>
        public static bool Handles(int fortId) => bound.Contains(fortId);
        static readonly List<FortGunView> all = new List<FortGunView>();
        /// <summary>
        /// The world height of the
        /// fort body under p (walls / roof, the 4 tower pads, the cannons standing on them, the big gun's turret, the flag pole), or
        /// float.MinValue when p is not over a live fort. The pad under `muzzle` and the cannon standing on it (the shooter itself) are
        /// left out. ShotClearance raises shells / rockets / rounds over it.</summary>
        public static float FortTopAt(Vector3 p, Vector3 muzzle)
        {
            float best = float.MinValue;
            for (int k = 0; k < all.Count; k++)
            {
                var v = all[k];
                if (!v || !v.Bound || v.Fort == null || v.Fort.Destroyed || v.tossed) continue;
                float t = v.TopAt(p, muzzle); if (t > best) best = t;
            }
            return best;
        }
        Renderer[] turretRs; Bounds turretB; float turretAt = -1f;
        float TopAt(Vector3 p, Vector3 muzzle)
        {
            var l = transform.InverseTransformPoint(p);
            if (Mathf.Abs(l.x) > FortHalfX + 0.3f || Mathf.Abs(l.z) > FortHalfZ + 0.3f) return float.MinValue;
            var lm = transform.InverseTransformPoint(muzzle);
            if (OzK394RoofShots.OnRoofDeck(lm) || OzK421RoofDefenders.OwnFort(transform, muzzle)) return float.MinValue;   // K394: a roof man's own fort is no block for his rounds (its parapet: OzK394RoofShots.OverParapet); K421: the roof defender known by who he is
            int own = -1; float od = 4.5f * 4.5f;
            for (int i = 0; i < TowerPads.Length; i++) { float ax = lm.x - TowerPads[i].x, az = lm.z - TowerPads[i].y, dd = ax * ax + az * az; if (dd < od) { od = dd; own = i; } }
            float top = WallTop; float r2 = (TowerR + 0.3f) * (TowerR + 0.3f);
            for (int i = 0; i < TowerPads.Length; i++)
            {
                if (i == own) continue;
                float ax = l.x - TowerPads[i].x, az = l.z - TowerPads[i].y;
                if (ax * ax + az * az < r2) top = Mathf.Max(top, TowerTop);
            }
            float y = transform.TransformPoint(new Vector3(l.x, top, l.z)).y;
            Vector3 ownW = own >= 0 ? transform.TransformPoint(new Vector3(TowerPads[own].x, TowerTop, TowerPads[own].y)) : new Vector3(1e6f, 0f, 1e6f);
            foreach (var b in padCannonBounds)
            {
                float ox = b.center.x - ownW.x, oz = b.center.z - ownW.z;
                if (ox * ox + oz * oz < 4.5f * 4.5f) continue;                                          // the shooter's own gun
                if (p.x > b.min.x - 0.3f && p.x < b.max.x + 0.3f && p.z > b.min.z - 0.3f && p.z < b.max.z + 0.3f) y = Mathf.Max(y, b.max.y);
            }
            if (yaw)
            {
                if (turretAt < 0f || Time.time - turretAt > 0.3f)
                {
                    turretAt = Time.time;
                    if (turretRs == null) turretRs = yaw.GetComponentsInChildren<Renderer>(true);
                    bool any = false;
                    foreach (var r in turretRs) { if (!r || !r.enabled || !(r is MeshRenderer)) continue; if (!any) { turretB = r.bounds; any = true; } else turretB.Encapsulate(r.bounds); }
                    if (!any) turretB = new Bounds(yaw.position, Vector3.zero);
                }
                if (p.x > turretB.min.x - 0.3f && p.x < turretB.max.x + 0.3f && p.z > turretB.min.z - 0.3f && p.z < turretB.max.z + 0.3f) y = Mathf.Max(y, turretB.max.y);
            }
            if (hasFlagPole) { float fx = p.x - flagPoleW.x, fz = p.z - flagPoleW.z; if (fx * fx + fz * fz < 0.7f * 0.7f) y = Mathf.Max(y, flagPoleW.y + FlagPoleH + 0.2f); }
            return y;
        }
        public float Heading => heading;
        public float Elevation => elev;
        public int CannonCount => cannons.Count;
        public int Shots { get; private set; }
        public bool Tossed => tossed;
        /// <summary>Head tilt now (deg), the largest reached, and the tilt cycles (rise over half the tilt) since bind.</summary>
        public float HeadTiltNow => head ? headTilt * Mathf.Clamp01(tilt01) : 0f;
        public float HeadTiltMax { get; private set; }
        public int HeadTiltCycles { get; private set; }
        public bool HasHead => head;
        /// <summary>World elevation (deg) of the bore right now (the head tilt included): the check that the lob angle is kept.</summary>
        public float BoreElevationNow => recoil ? ElevOf(BoreW(), transform.up) : 0f;

        const float G = 9.81f;
        ZGame game;
        Transform yaw, pitch, recoil, muzzle, head;
        float tilt01, tiltVel; bool tiltUp;
        sealed class Ram { public Transform sleeve, rod; public Vector3 a, b; }
        readonly List<Ram> rams = new List<Ram>();
        static readonly Dictionary<Mesh, (Mesh head, Mesh ring)> splitCache = new Dictionary<Mesh, (Mesh, Mesh)>();
        static Material sRamDark, sRamSteel;
        FortGunNeck neck; bool neckShown;                                                    // K255: the hydraulic neck (FortGunNeck.cs); the old two rams while it is not shown
        Quaternion yawRest, pitchRest; Vector3 yawRestPos, pitchRestPos, recoilRestPos;
        Vector3 yawAxisP, pitchAxisP, boreP; float yawSign = 1, pitchSign = 1, restHeading, restElev;
        float heading, headingVel, elev, elevVel, wantHeading, wantElev, aimHeading;
        ZObject target; float lostTargetAt = -99;
        readonly List<ZObject> cannons = new List<ZObject>(); readonly HashSet<int> cannonIds = new HashSet<int>(); float nextCannonScan, nextScan;
        ZObject scanPick;
        bool pending; int pendingTarget = -1; Vector3 pendingPoint; float pendingSince;
        float lastShot = -99f, shotAge = 99f;
        readonly List<(float at, Action a)> later = new List<(float, Action)>();
        bool traversing; float servoCool;
        RtsCamera rts;
        // flag keep-out: heading (deg, world) of the fort flag pole seen from the yaw axis and the half-width of the blocked sector
        bool hasKeepOut; float keepCentre, keepHalf, barrelReach, barrelRadius, nextFlagLook; int flagLooks; string keepSource = "none";
        // fortgun_v2: shell-arc obstacles (flag pole world position, cannons on the 4 turret spots) and verification stats
        Vector3 flagPoleW; bool hasFlagPole; readonly List<Bounds> padCannonBounds = new List<Bounds>(); readonly Vector3[] clothDirs = new Vector3[3];
        float simShellSpeed = 95f;
        /// <summary>fortgun_v2 stats (Play check): shots through LobArc, how many needed more than the barrel's own elevation, the
        /// largest launch-vs-barrel elevation gap and heading gap (deg), and shots whose arc found no clear path.</summary>
        public int Arcs { get; private set; }
        public int ArcsSteeperThanBarrel { get; private set; }
        public int ArcsBlocked { get; private set; }
        public float MaxElevGap { get; private set; }
        public float MaxHeadingGap { get; private set; }
        public float LastLaunchElev { get; private set; }
        [Header("combat_nb_v1 NB27: fort mortar (DV25)")]
        [Tooltip("Barrel elevation of the mortar lob (deg); the sim's flight time assumes a 60 deg lob")] public float mortarElevation = 60f;
        [Tooltip("Camera shake when a fort shell lands on screen (RtsCamera.Shake); the main cannon shakes most")] public float landShake = 0.95f;
        [Tooltip("Reload ring round the turret: radius (m), width (m)")] public float ringRadius = 3.4f, ringWidth = 0.22f;
        /// <summary>NB27 stats (Play proof): real shells fired, landings drawn, landings that shook the camera.</summary>
        public int MortarShots { get; private set; }
        public int MortarLandings { get; private set; }
        public int MortarShakes { get; private set; }
        float twistSign = 1f;
        // team accent band (TeamLit _AccentLo/_AccentHi, object Y) of the original single mesh, kept on every part
        readonly List<(Renderer r, float off)> bandRenderers = new List<(Renderer, float)>(); float bandLo = 1e6f, bandHi = -1e6f; bool hasBand;
        MaterialPropertyBlock mpb;
        static readonly int AccentLoId = Shader.PropertyToID("_AccentLo"), AccentHiId = Shader.PropertyToID("_AccentHi"),
                            AccentFromId = Shader.PropertyToID("_AccentFrom"), AccentToId = Shader.PropertyToID("_AccentTo");
        // death
        bool tossed, landed; float tossT, landedAt, burnT; Vector3 tossFrom, tossTo, tossAxis; Quaternion tossStartRot, tossEndRot;

        // ================================================================== install
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoInstall()
        {
            Install();
            SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded;
        }
        static void OnSceneLoaded(Scene s, LoadSceneMode m) => Install();

        /// <summary>Adds a bound FortGunView to every fort view that carries the split gun nodes. Returns how many were added.</summary>
        public static int Install()
        {
            if (PlayerPrefs.GetInt("oz_fortgun", 1) == 0) return 0;
            var g = ZGame.I; if (!g || g.World == null) return 0;
            int n = 0;
            foreach (var o in g.World.Objects)
            {
                if (!o.IsFort || !g.StaticViews.TryGetValue(o.Id, out var go) || !go) continue;
                if (go.GetComponent<FortGunView>() || !FindDeep(go.transform, "FortGunYaw")) continue;
                if (go.AddComponent<FortGunView>().Bind(g, o)) n++;
            }
            if (n > 0) Debug.Log($"[OpenZ] FortGunView: {n} fort gun(s) installed");
            return n;
        }

        void Start()
        {
            if (Bound) return;                                                  // placed by hand on the prefab: find our fort
            var g = ZGame.I; if (!g || g.World == null) return;
            foreach (var o in g.World.Objects)
                if (o.IsFort && g.StaticViews.TryGetValue(o.Id, out var go) && go == gameObject) { Bind(g, o); break; }
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var r = FindDeep(c, name); if (r) return r; }
            return null;
        }

        // ================================================================== bind: measure the rest pose
        public bool Bind(ZGame g, ZObject fortObj)
        {
            game = g; Fort = fortObj; bound.Add(fortObj.Id);
            FortGunWarPaint.Attach(gameObject, fortObj);
            yaw = FindDeep(transform, yawNode); pitch = FindDeep(transform, pitchNode);
            recoil = FindDeep(transform, recoilNode); muzzle = FindDeep(transform, muzzleNode);
            if (!yaw || !pitch || !recoil || !muzzle)
            {
                Debug.LogWarning($"[OpenZ] FortGunView {name}: split gun nodes missing (yaw {(bool)yaw} pitch {(bool)pitch} recoil {(bool)recoil} muzzle {(bool)muzzle}); import Fort_Split");
                enabled = false; return false;
            }
            SplitHead();
            // a flat FBX import: rebuild the chain, world poses kept (Fort_Split.fbx is written flat, see README_IMPORT.md)
            var pitchParent = head ? head : yaw;
            if (pitch.parent != pitchParent) pitch.SetParent(pitchParent, true);
            if (recoil.parent != pitch) recoil.SetParent(pitch, true);
            if (muzzle.parent != recoil) muzzle.SetParent(recoil, true);
            yawRest = yaw.localRotation; pitchRest = pitch.localRotation;
            yawRestPos = yaw.localPosition; pitchRestPos = pitch.localPosition; recoilRestPos = recoil.localPosition;

            Vector3 up = transform.up;
            Vector3 bore = (muzzle.position - recoil.position).normalized;
            Vector3 fwdH = Vector3.ProjectOnPlane(bore, up).normalized;
            restHeading = HeadingOf(fwdH); restElev = ElevOf(bore, up);
            yawAxisP = yaw.parent.InverseTransformDirection(up).normalized;
            pitchAxisP = pitch.parent.InverseTransformDirection(Vector3.Cross(up, fwdH).normalized).normalized;
            boreP = recoil.parent.InverseTransformDirection(bore).normalized;
            // rotation signs, measured (never assume an importer's handedness)
            yaw.localRotation = Quaternion.AngleAxis(10f, yawAxisP) * yawRest;
            yawSign = Mathf.Sign(Mathf.DeltaAngle(restHeading, HeadingOf(Vector3.ProjectOnPlane(BoreW(), up))));
            yaw.localRotation = yawRest;
            pitch.localRotation = Quaternion.AngleAxis(10f, pitchAxisP) * pitchRest;
            pitchSign = Mathf.Sign(ElevOf(BoreW(), up) - restElev);
            pitch.localRotation = pitchRest;
            heading = wantHeading = aimHeading = restHeading; elev = wantElev = restElev;
            rts = FindFirstObjectByType<RtsCamera>();
            mpb = new MaterialPropertyBlock();
            MeasureBand();
            MeasureBarrel();
            UpdateKeepOut();
            BindAudio(); BindFx();
            var hu = g.World.S.Unit("howitzer"); if (hu != null && hu.attack_missile_speed > 0) simShellSpeed = hu.attack_missile_speed;   // fortgun_v2: DV7 shell speed (px/s)
            if (ZWorld.FortMortarDeviation)
            {   // combat_nb_v1 NB27: the barrel points up and lobs (the sim's flight is a 60 deg lob under 6 m/s^2 from the roof)
                maxElevation = Mathf.Max(maxElevation, mortarElevation + 2f); lobNear = lobFar = mortarElevation;
                maxLaunch = Mathf.Max(maxLaunch, mortarElevation + 20f); arcGravityMin = Mathf.Min(arcGravityMin, 1f);
                pitchMaxSpeed = Mathf.Max(pitchMaxSpeed, 24f);
            }
            Bound = true; enabled = true;
            if (!all.Contains(this)) all.Add(this);
            Debug.Log($"[OpenZ] FortGunView {name}: fortgun_v2 arc lob {lobNear:0}..{lobFar:0} deg, barrel max {maxElevation:0} deg, clearance {arcClearance:0.00} m; DV7 envelope {ZObject.FortGunMinEdgePx} px from the walls, rear arc +-{ZObject.FortGunRearDeg} deg");
            Debug.Log($"[OpenZ] FortGunView {name}: bound ({Fort}), rest heading {restHeading:F1}, elevation {restElev:F1}, yaw sign {yawSign}, pitch sign {pitchSign}, band {(hasBand ? $"{bandLo:F3}..{bandHi:F3}" : "none")}, " +
                      $"barrel reach {barrelReach:F2} m r {barrelRadius:F2} m, flag keep-out {(hasKeepOut ? $"{keepCentre:F0} +-{keepHalf:F1} deg ({keepSource})" : "none")}, fx {(fxBlast != null ? "OpenZFxPlayer" : "FxManager")}");
            return true;
        }

        /// <summary>Horizontal reach of the barrel from the yaw axis (muzzle node) and the barrel's radius (FortGunRecoil mesh bounds
        /// across the bore, world metres).</summary>
        void MeasureBarrel()
        {
            Vector3 up = transform.up, bore = BoreW();
            var toMuzzle = Vector3.ProjectOnPlane(muzzle.position - yaw.position, up);
            barrelReach = toMuzzle.magnitude + 0.05f;
            barrelRadius = 0.8f;
            var mf = recoil.GetComponent<MeshFilter>();
            if (mf && mf.sharedMesh)
            {
                var b = mf.sharedMesh.bounds; var boreL = recoil.InverseTransformDirection(bore).normalized; var s = recoil.lossyScale;
                float r = 0f;
                for (int i = 0; i < 3; i++)
                    if (Mathf.Abs(boreL[i]) < 0.5f) r = Mathf.Max(r, b.extents[i] * Mathf.Abs(s[i]));      // the two axes across the bore
                if (r > 0.05f) barrelRadius = r;
            }
        }

        /// <summary>The fort flag stands on the roof ~6 m behind the turret (FlagView's cloth flag on the baked pole socket,
        /// FlagView.BakedPoleBase = FortBase mesh space (-0.0024, 0.2125, -0.6263)): with the barrel reaching ~6 m the turret would
        /// swing the barrel through the pole when it aims backwards (verify_split.py clip sweep: yaw 171-192 deg). A sector around
        /// the pole direction is blocked; the slew goes the long way round and parks at its edge.</summary>
        void UpdateKeepOut()
        {
            hasKeepOut = false;
            if (!avoidFlag) return;
            Vector3 pole; var fg = GameObject.Find("FortFlag_" + Fort.Id);                              // FlagView.NewFlag (root at the pole base)
            if (fg) { pole = fg.transform.position; keepSource = "FlagView"; }
            else
            {
                var fb = FindDeep(transform, "FortBase"); if (!fb) return;
                pole = fb.TransformPoint(new Vector3(-0.0024f, 0.2125f, -0.6263f)); keepSource = "baked pole";
            }
            flagPoleW = pole; hasFlagPole = true;                                                          // fortgun_v2: arc obstacle
            var d = Vector3.ProjectOnPlane(pole - yaw.position, transform.up); float dist = d.magnitude;
            if (dist > 0.5f)
            {   // fortgun_v2: the sim's DV7 rear arc assumes the flag stands north of a fort_front and south of a fort_back
                float simRear = Fort.Type == "fort_front" ? 0f : 180f;                                     // world heading of -py / +py (z = -py / 8)
                if (Mathf.Abs(Mathf.DeltaAngle(simRear, HeadingOf(d))) > 12f)
                    Debug.LogWarning($"[OpenZ] FortGunView {name}: the fort flag is at heading {HeadingOf(d):0} but the DV7 rear arc is centred on {simRear:0} ({Fort.Type}); check the fort's yaw");
            }
            const float poleR = 0.25f;                                                                     // FlagView fortPoleRadius 0.2 (+ cloth hoist)
            if (dist < 0.5f || dist > barrelReach + poleR + flagClearance) { keepSource += " (out of reach)"; return; }
            keepCentre = HeadingOf(d);
            keepHalf = Mathf.Asin(Mathf.Clamp01((barrelRadius + poleR + flagClearance) / dist)) * Mathf.Rad2Deg + joltTwist + 1f;
            hasKeepOut = keepHalf < 170f;
        }

        static float HeadingOf(Vector3 d) => Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        static float ElevOf(Vector3 d, Vector3 up) => Mathf.Asin(Mathf.Clamp(Vector3.Dot(d.normalized, up), -1f, 1f)) * Mathf.Rad2Deg;
        Vector3 BoreW() => recoil.parent.TransformDirection(boreP).normalized;

        /// <summary>TeamPaint (TeamPaint.cs:45-80) sets each renderer's accent band from its OWN mesh bounds; the split parts
        /// would get their own bands. Keep the single-mesh band: union bounds of all fort parts in the FortBase object space
        /// (= m01_fort.glb mesh space) x the material's _AccentFrom/_AccentTo, shifted into each part's object space.</summary>
        void MeasureBand()
        {
            bandRenderers.Clear(); hasBand = false;
            var fbT = FindDeep(transform, "FortBase");
            var baseR = fbT ? fbT.GetComponent<Renderer>() : null;
            // only the four split parts (other helpers may hang their own renderers under the model: doors, beacons, lights)
            var all = new List<Renderer>();
            foreach (var t in new[] { fbT, yaw, head, pitch, recoil })
                if (t && t.GetComponent<MeshFilter>() && t.GetComponent<MeshRenderer>() is MeshRenderer mr) all.Add(mr);
            if (!baseR || !baseR.sharedMaterial || !baseR.sharedMaterial.HasProperty(AccentFromId)) return;
            var bt = baseR.transform;
            Bounds u = default; bool init = false;
            foreach (var r in all)
            {
                var mesh = r.GetComponent<MeshFilter>().sharedMesh; if (!mesh) continue;
                var b = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    var p = bt.InverseTransformPoint(r.transform.TransformPoint(c));
                    if (!init) { u = new Bounds(p, Vector3.zero); init = true; } else u.Encapsulate(p);
                }
                bandRenderers.Add((r, bt.InverseTransformPoint(r.transform.position).y));
            }
            float from = baseR.sharedMaterial.GetFloat(AccentFromId), to = baseR.sharedMaterial.GetFloat(AccentToId);
            if (!init || from >= 1.5f) return;
            bandLo = u.min.y + from * u.size.y; bandHi = u.min.y + to * u.size.y; hasBand = true;
        }

        void ApplyBand()
        {
            SyncHead();
            if (!hasBand) return;
            foreach (var (r, off) in bandRenderers)
            {
                if (!r) continue;
                int n = r.sharedMaterials.Length;
                for (int i = 0; i < n; i++)
                {
                    r.GetPropertyBlock(mpb, i);
                    mpb.SetFloat(AccentLoId, bandLo - off); mpb.SetFloat(AccentHiId, bandHi - off);
                    r.SetPropertyBlock(mpb, i);
                }
            }
        }

        // ================================================================== per frame
        void LateUpdate()
        {
            if (!Bound || game == null || game.World == null) return;
            ReadEvents();
            float dt = game.paused ? 0f : Time.deltaTime;
            RunLater();
            StepShells(dt);
            ReloadRing();                                                                            // combat_nb_v1 NB27: the 30 s reload, filling
            if (tossed) { UpdateToss(dt); ApplyBand(); return; }
            if (Fort.FortGunDestroyed) { StartToss(); ApplyBand(); return; }                         // combat_nb_v1 NB27: shot to pieces in the sim: blown off its ring
            if (Fort.Destroyed) { if (!OpenZEnding.HoldFortToss) StartToss(); ApplyBand(); return; }   // endings_v1: the gun flies with the bomb, not 3 s before it
            if (dt <= 0f) { ApplyBand(); return; }
            if (Time.time >= nextCannonScan) { nextCannonScan = Time.time + 0.5f; ScanCannons(); }
            if (avoidFlag && flagLooks < 6 && !keepSource.StartsWith("FlagView") && Time.time >= nextFlagLook)
            { nextFlagLook = Time.time + 1f; flagLooks++; UpdateKeepOut(); }                        // FlagView may install after us
            ChooseTarget();
            Aim(dt);
            TryFire();
            Pose(dt);
            ApplyBand();
            TraverseSound();
            OzSoundV3.FortGunMotion(Fort, yaw, headingVel, elevVel, tilt01, dt, target != null);                      // sound_v3: heavy turn start / loop / stop, barrel lift, head tilt
        }

        /// <summary>This frame's sim events (CombatView clears them after us): our cannons' shots and the fort's death.</summary>
        void ReadEvents()
        {
            var ev = game.World.Events;
            for (int i = 0; i < ev.Count; i++)
            {
                var e = ev[i];
                if (e.type == ZEventType.Death && e.objId == Fort.Id) { if (!OpenZEnding.HoldFortToss) StartToss(); continue; }   // endings_v1
                if (e.type == ZEventType.MissileExploded && e.objId == Fort.Id && !e.flag2) { OzAirShots.FortLanding(e, Fort, game.World.GetAny(e.otherId)); ShellLanded(OzShuttleAim.AirAim(game.World.GetAny(e.otherId), PxGround(e.x, e.y), 0f)); continue; }   // combat_nb_v1 NB27 (CombatView draws the blast)
                if (!tossed && e.type == ZEventType.MissileFired && e.objId == Fort.Id)
                {   // DV7: the fort's own howitzer shell (real damage in the sim): fire now on the real flight time; the sim's
                    // MissileExploded draws the blast (CombatView), so this shell lands without a cosmetic one
                    var airTo = OzShuttleAim.AirAim(game.World.GetAny(e.otherId), PxGround(e.x, e.y), e.value);   // DV118: up to the flying shuttle
                    OzShuttleAim.NoteFired(e, Fort, game.World.GetAny(e.otherId), muzzle ? muzzle.position : airTo, airTo);
                    FireReal(airTo, Mathf.Max(0.3f, e.value));
                    continue;
                }
                if (ZWorld.FortGunDeviation) continue;                                          // DV7: the big gun fires its own real shots only
                if (tossed || (e.type != ZEventType.MissileFired && e.type != ZEventType.Hitscan) || !cannonIds.Contains(e.objId)) continue;
                if (e.type == ZEventType.MissileFired && e.flag) continue;                      // grenades (cannons have none)
                var t = game.World.GetAny(e.otherId);
                pending = true; pendingSince = Time.time; pendingTarget = e.otherId;
                pendingPoint = t != null ? AimPoint(t) : PxGround(e.x, e.y);
                if (e.type == ZEventType.MissileFired) pendingPoint = Vector3.Lerp(pendingPoint, PxGround(e.x, e.y) + Vector3.up * 0.6f, 0.5f);   // toward the real shell's impact
            }
        }

        Vector3 PxGround(int x, int y) { var p = ZGame.PxToWorld(x, y); p.y = game.GroundY(p.x, p.z); return p; }

        Vector3 AimPoint(ZObject t)
        {
            if (game.Views.TryGetValue(t.Id, out var v) && v && !v.Dying) return v.AimPos();
            return game.CenterWorld(t) + Vector3.up * 0.9f;
        }

        static Vector3 SimVelocity(ZObject t) => new Vector3(t.Dx / 8f, 0f, -t.Dy / 8f);   // px/s -> m/s (ZGame.PxToWorld: x = px/8, z = -py/8)

        /// <summary>The fort's own cannons = cannons of the fort's team standing on its 4 turret spots.</summary>
        void ScanCannons()
        {
            cannons.Clear(); cannonIds.Clear();
            foreach (var o in game.World.Objects)
            {
                if (o.Kind != ZKind.Cannon || o.Destroyed || o.KillAt >= 0 || o.Owner == Team.Neutral || o.Owner != Fort.Owner) continue;
                int dx = o.LocX - Fort.LocX, dy = o.LocY - Fort.LocY;
                if ((dx == 16 || dx == 112) && (dy == 0 || dy == 48)) { cannons.Add(o); cannonIds.Add(o.Id); }
            }
            // fortgun_v2: every cannon standing on a turret spot (any owner) is an obstacle for the shell arc: the bounds of each of
            // its part renderers (plinth, body, barrel: tighter than one box around a 42 deg howitzer tube), refreshed every 0.5 s
            padCannonBounds.Clear();
            foreach (var o in game.World.Objects)
            {
                if (o.Kind != ZKind.Cannon || o.Destroyed) continue;
                int dx = o.LocX - Fort.LocX, dy = o.LocY - Fort.LocY;
                if (!((dx == 16 || dx == 112) && (dy == 0 || dy == 48)) || !game.Views.TryGetValue(o.Id, out var v) || !v) continue;
                foreach (var r in v.GetComponentsInChildren<Renderer>())
                    if ((r is MeshRenderer || r is SkinnedMeshRenderer) && r.enabled && r.bounds.size.y >= 0.25f)   // no flat rings / discs
                        padCannonBounds.Add(r.bounds);
            }
        }

        static bool IsEnemy(ZObject o, Team us) => o != null && o.IsUnit && o.Owner != Team.Neutral && o.Owner != us && !o.Destroyed && o.KillAt < 0;

        void ChooseTarget()
        {
            ZObject pick = null;
            // 0. DV7: the fort's own howitzer target (the sim lays it 1.4 s before the first shot)
            if (IsEnemy(Fort.FortGunTarget, Fort.Owner)) pick = Fort.FortGunTarget;
            else if (Committed) pick = target;
            // 1. what the fort cannons are shooting at (stick to the current one while any cannon still engages it)
            if (pick == null) foreach (var c in cannons) { var a = c.AttackObject; if (a != null && a == target && IsEnemy(a, Fort.Owner)) { pick = a; break; } }
            if (pick == null) foreach (var c in cannons) { var a = c.AttackObject; if (IsEnemy(a, Fort.Owner)) { pick = a; break; } }
            // 2. a pending shot's target
            if (pick == null && pending) { var t = game.World.Get(pendingTarget); if (IsEnemy(t, Fort.Owner)) pick = t; }
            // 3. nearest enemy inside the fort cannons' range (or the fort-danger radius without cannons), rescanned 4x/s
            if (pick == null)
            {
                if (Time.time >= nextScan) { nextScan = Time.time + 0.25f; scanPick = NearestInRange(); }
                if (IsEnemy(scanPick, Fort.Owner)) pick = scanPick;
            }
            if (pick != target) { if (pick == null) lostTargetAt = Time.time; else if (yaw) OzSoundV3.FortGunLock(Fort, yaw.position); target = pick; }   // sound_v3: lock beep
        }

        ZObject NearestInRange()
        {
            ZObject best = null; double bd = double.MaxValue;
            foreach (var o in game.World.Objects)
            {
                if (!IsEnemy(o, Fort.Owner)) continue;
                bool inRange = false;
                foreach (var c in cannons) { double dx = o.CenterX - c.CenterX, dy = o.CenterY - c.CenterY; if (dx * dx + dy * dy <= (double)c.AttackRadius * c.AttackRadius) { inRange = true; break; } }
                double fx = o.CenterX - Fort.CenterX, fy = o.CenterY - Fort.CenterY, fd = fx * fx + fy * fy;
                if (!inRange && cannons.Count == 0 && trackWithoutCannons && fd <= (double)dangerRadiusPx * dangerRadiusPx) inRange = true;
                if (inRange && fd < bd) { bd = fd; best = o; }
            }
            return best;
        }

        /// <summary>Ballistic lob from `from` to `to`: launch elevation eased from elevNear (close) to elevFar (far), flight time
        /// from it, clamped. Returns the launch velocity and its elevation.</summary>
        bool Solve(Vector3 from, Vector3 to, out Vector3 v0, out float T, out float elevDeg)
        {
            var d = to - from; float h = Mathf.Sqrt(d.x * d.x + d.z * d.z), drop = -d.y;
            float e = Mathf.Lerp(elevNear, elevFar, Mathf.InverseLerp(12f, 42f, h));
            float num = Mathf.Max(Mathf.Tan(e * Mathf.Deg2Rad) * h + drop, 0.5f * G * minFlight * minFlight);   // = g T^2 / 2
            T = Mathf.Clamp(Mathf.Sqrt(2f * num / G), minFlight, maxFlight);
            var hv = h > 1e-3f ? new Vector3(d.x, 0f, d.z) / T : Vector3.zero;
            float vy = d.y / T + 0.5f * G * T;
            v0 = hv + Vector3.up * vy;
            elevDeg = Mathf.Atan2(vy, hv.magnitude) * Mathf.Rad2Deg;
            return h > 0.5f;
        }

        Vector3 LeadPoint(ZObject t, Vector3 from)
        {
            var p = AimPoint(t);
            if (!Solve(from, p, out _, out float T, out _)) return p;
            p += SimVelocity(t) * T;
            p.y = Mathf.Max(p.y, game.GroundY(p.x, p.z) + 0.3f);
            return p;
        }

        void Aim(float dt)
        {
            Vector3 pivot = yaw.position, aim;
            bool committed = Committed;
            bool has = target != null && (!committed || IsEnemy(target, Fort.Owner));
            if (committed) aim = PxGround(Fort.FortGunAimX, Fort.FortGunAimY);
            else if (has) aim = LeadPoint(target, muzzle.position);
            else if (pending) aim = pendingPoint;
            else aim = Vector3.zero;
            if (has || pending || committed)
            {
                var d = aim - pivot; d.y = 0;
                if (d.sqrMagnitude > 0.25f) wantHeading = HeadingOf(d);
                if (committed || (has && Dv7(target)))
                {   // fortgun_v2: the sim's own shell (flight = distance / shell speed) on the ground point it will hit, laid on the
                    // elevation the shot will leave with (the lob that clears the fort)
                    aim = Dv7Aim(target, out float Tf);
                    var d2 = aim - pivot; d2.y = 0;
                    if (d2.sqrMagnitude > 0.25f) wantHeading = HeadingOf(d2);
                    LobArc(muzzle.position, aim, Tf, out _, out _, out float le, float.NaN, aimMargin);
                    wantElev = le;
                    // lob for the whole 30 s reload, so a shot read as nothing moving; now the barrel drops to the reload angle after
                    // each shot and climbs back onto the lob reloadRaiseLead s before the next one (every shot = a visible tilt cycle)
                    if (ZWorld.FortMortarDeviation && reloadDrop)
                    {
                        float left = (float)(Fort.FortGunReadyAt - game.World.Time) / Gs;
                        bool settled = shotAge > recoilKick + recoilHold + tiltHold + 0.8f;
                        if (settled && (left > reloadRaiseLead || !committed)) { wantElev = Mathf.Min(le, restElev + reloadElev); if (!reloadDown) { reloadDown = true; ReloadDrops++; } }
                        else if (reloadDown && left <= reloadRaiseLead && committed) { reloadDown = false; ReloadRaises++; }   // the move itself plays veh_fort_elevate (OzSoundV3 elevation watcher)
                    }
                }
                else if (ZWorld.FortGunDeviation) wantElev = restElev + (ZWorld.FortMortarDeviation && reloadDrop ? reloadElev : 3f);
                else if (Solve(muzzle.position, aim, out _, out _, out float e)) wantElev = e;
            }
            else if (Time.time - lostTargetAt > 2f)
            {   // idle: settle, then a slow scan around the rest heading (a quiet, living fort)
                wantHeading = restHeading + (idleScan ? idleScanAmplitude * Mathf.Sin((Time.time + Fort.Id * 7.3f) * 2f * Mathf.PI / Mathf.Max(4f, idleScanPeriod)) : 0f);
                wantElev = restElev + 3f;
            }
            wantElev = Mathf.Clamp(wantElev, minElevation, maxElevation);
            aimHeading = wantHeading;                                                              // before the keep-out clamp
            if (hasKeepOut)
            {   // unwrap around the flag sector: u in [keepHalf, 360 - keepHalf] is free, so a plain SmoothDamp on u never crosses the
                // flag (a target behind it makes the turret turn the long way round and park at the sector edge; Aligned then holds there)
                float uH = Mathf.Clamp(Mathf.Repeat(heading - keepCentre, 360f), keepHalf, 360f - keepHalf);
                float uRaw = Mathf.Repeat(wantHeading - keepCentre, 360f);
                // a target inside the sector: park at the edge on the side the turret is already on (no 330 deg swings when the
                // target jitters across the middle of the sector)
                float uW = uRaw < keepHalf || uRaw > 360f - keepHalf ? (uH < 180f ? keepHalf : 360f - keepHalf) : uRaw;
                float leftK = CatchUpLeft();                                                            // fortgun_v2: on target before the sim fires
                uH = Mathf.SmoothDamp(uH, uW, ref headingVel, CatchSmooth(yawSmooth, leftK), CatchSpeed(Mathf.Abs(uW - uH), yawMaxSpeed, leftK), dt);
                wantHeading = keepCentre + uW; heading = keepCentre + uH;
            }
            else
            {
                float leftF = CatchUpLeft();                                                            // fortgun_v2: on target before the sim fires
                heading = Mathf.SmoothDampAngle(heading, wantHeading, ref headingVel, CatchSmooth(yawSmooth, leftF), CatchSpeed(Mathf.Abs(Mathf.DeltaAngle(heading, wantHeading)), yawMaxSpeed, leftF), dt);
            }
            float leftP = CatchUpLeft();
            elev = Mathf.SmoothDamp(elev, wantElev, ref elevVel, CatchSmooth(pitchSmooth, leftP), CatchSpeed(Mathf.Abs(wantElev - elev), pitchMaxSpeed, leftP), dt);
        }

        /// <summary>On the real aim (not the keep-out edge): a target hidden behind the fort flag is tracked to the sector edge but
        /// not shot at by the big gun (the fort's own cannons still fire).</summary>
        bool Aligned => Mathf.Abs(Mathf.DeltaAngle(heading, aimHeading)) < aimTolerance && Mathf.Abs(elev - wantElev) < aimTolerance;

        void TryFire()
        {
            if (!pending) return;
            if (Time.time - pendingSince > pendingTimeout) { pending = false; return; }
            if (Time.time - lastShot < minShotInterval || !Aligned) return;
            pending = false;
            var t = game.World.Get(pendingTarget);
            Fire(IsEnemy(t, Fort.Owner) ? LeadPoint(t, muzzle.position) : pendingPoint);
        }

        // ================================================================== pose
        static float Kick(float t, float kick, float hold, float ret)
        {
            if (t < 0f) return 0f;
            if (t < kick) { float u = t / kick; return 1f - (1f - u) * (1f - u); }                 // fast ease-out kick
            t -= kick; if (t < hold) return 1f; t -= hold;
            if (t >= ret) return 0f;
            float v = t / ret; return 1f - v * v * (3f - 2f * v);                                  // slow smooth return
        }

        void Pose(float dt)
        {
            shotAge += dt;
            float k = Kick(shotAge, recoilKick, recoilHold, recoilReturn);
            float shove = shotAge < 0.6f ? Mathf.Min(1f, shotAge / 0.03f) * Mathf.Exp(-shotAge / 0.12f) : 0f;
            float nod = shotAge < 1.2f ? joltNod * Mathf.Min(1f, shotAge / 0.04f) * Mathf.Exp(-shotAge / 0.22f) * Mathf.Cos(shotAge * 2f * Mathf.PI * 2.4f) : 0f;
            // turret jolt = a damped twist about the yaw axis: the ring (an ellipse on the flat flange, Fort_Split v2) stays seated
            float twist = shotAge < 1.0f ? twistSign * joltTwist * Mathf.Min(1f, shotAge / 0.03f) * Mathf.Exp(-shotAge / 0.16f) * Mathf.Cos(shotAge * 2f * Mathf.PI * 3.1f) : 0f;
            yaw.localRotation = Quaternion.AngleAxis(yawSign * (Mathf.DeltaAngle(restHeading, heading) + twist), yawAxisP) * yawRest;
            yaw.localPosition = yawRestPos;
            // fortgun_v2 body kick: the whole turret rocks back on the rear foot of its ring (muzzle climbs, front of the ring lifts a
            // few cm for ~0.2 s over the flat flange cap), a damped bounce; the local pose above is rebuilt from rest every frame
            float rock = shotAge < 0.9f ? turretRock * Mathf.Min(1f, shotAge / 0.035f) * Mathf.Exp(-shotAge / 0.14f) * Mathf.Max(0f, Mathf.Cos(shotAge * 2f * Mathf.PI * 2.2f)) : 0f;   // never below its seat
            if (Mathf.Abs(rock) > 1e-3f)
            {
                Vector3 upW = transform.up, boreH = Vector3.ProjectOnPlane(BoreW(), upW);
                if (boreH.sqrMagnitude > 1e-4f)
                {
                    boreH.Normalize();
                    var q = Quaternion.AngleAxis(-rock, Vector3.Cross(upW, boreH));                     // negative about "right" = nose up (Unity)
                    var pivotW = yaw.position - boreH * rockPivotBack;
                    yaw.SetPositionAndRotation(pivotW + q * (yaw.position - pivotW), q * yaw.rotation);
                }
            }
            // head shrinks by the same amount, so the bore's world elevation (the lob) is exactly elev (same axis: the rotations add)
            float tgt = TiltTarget();
            tilt01 = Mathf.SmoothDamp(tilt01, tgt, ref tiltVel, tgt > tilt01 ? tiltRiseSmooth : tiltFallSmooth, Mathf.Infinity, dt);
            float tiltDeg = head ? headTilt * Mathf.Clamp01(tilt01) : 0f;
            // block, the barrel never droops into the housing); the mortar lob (60 deg) = head 30 + barrel 30 in the head
            if (head) tiltDeg = Mathf.Min(tiltDeg, Mathf.Max(0f, elev + nod - restElev));
            if (head)
            {
                var hq = Quaternion.AngleAxis(pitchSign * tiltDeg, pitchAxisP);
                head.localRotation = hq; head.localPosition = tiltPivot - hq * tiltPivot;
                PoseRams();
                HeadTiltMax = Mathf.Max(HeadTiltMax, tiltDeg);
                if (!tiltUp && tilt01 > 0.5f) { tiltUp = true; HeadTiltCycles++; } else if (tiltUp && tilt01 < 0.1f) tiltUp = false;
            }
            pitch.localRotation = Quaternion.AngleAxis(pitchSign * (elev + nod - restElev - tiltDeg), pitchAxisP) * pitchRest;
            var bore = BoreW();
            // cradle shove: the mantlet slides back into the housing (its sleeve hides the joint), then the barrel runs back inside it
            pitch.localPosition = pitchRestPos + pitch.parent.InverseTransformVector(-bore * joltDistance * shove);
            recoil.localPosition = recoilRestPos + recoil.parent.InverseTransformVector(-bore * recoilDistance * k);
        }

        /// <summary>The head wears exactly what the ring (the original yaw renderer) wears: its materials (FortGunWarPaint A / B) and its
        /// property blocks (TeamPaint's team colour, vivid accent, panels, fort paint, char), copied every frame before the band.</summary>
        void SyncHead()
        {
            if (!headR || !yawR) return;
            if (headR.sharedMaterial != yawR.sharedMaterial) headR.sharedMaterials = yawR.sharedMaterials;
            for (int i = 0; i < headMatCount; i++) { yawR.GetPropertyBlock(mpb, i); headR.SetPropertyBlock(mpb, i); }
        }
        MeshRenderer headR, yawR; int headMatCount;

        /// <summary>1 while a shot is being laid (the sim fires within tiltWindUp), a cosmetic shot waits for the lay, or the gun has
        /// just fired (kick + hold + tiltHold); else 0 (idle, tracking, reloading: the head sits flat on its ring).</summary>
        float TiltTarget()
        {
            if (!head || !HeadTiltEnabled || headTilt <= 0f) return 0f;
            if (shotAge < recoilKick + recoilHold + tiltHold) return 1f;
            if (pending) return 1f;
            var ft = Fort.FortGunTarget;
            if (ZWorld.FortGunDeviation && Committed)
            {
                float left = (float)(Fort.FortGunReadyAt - game.World.Time) / Gs;
                if (left <= tiltWindUp) return 1f;
            }
            return 0f;
        }

        /// <summary>Splits the FortGunYaw mesh (one imported piece: the ring band + the head box) at tiltSplitY into the ring (stays on
        /// the yaw node: turns only) and the head (new child FortGunHead at the identity pose: tilts about tiltPivot). Both keep the
        /// full vertex data (only the index lists differ, cached per source mesh), the same materials and the same accent band. Two
        /// hydraulic rams join the ring top to the head's underside in front of the hinge (hidden under the head at rest, they
        /// extend when it lifts). Without a readable mesh the head does not tilt (old look).</summary>
        void SplitHead()
        {
            if (head) return;
            var mf = yaw.GetComponent<MeshFilter>(); var mr = yaw.GetComponent<MeshRenderer>();
            if (!mf || !mr || !mf.sharedMesh || !mf.sharedMesh.isReadable) { Debug.LogWarning($"[OpenZ] FortGunView {name}: yaw mesh not readable, the head does not tilt"); return; }
            var src = mf.sharedMesh;
            if (!splitCache.TryGetValue(src, out var parts))
            {
                var v = src.vertices;
                var hm = Instantiate(src); hm.name = src.name + "_Head";
                var rm = Instantiate(src); rm.name = src.name + "_Ring";
                var ht = new List<int>(); var rt = new List<int>();
                for (int s = 0; s < src.subMeshCount; s++)
                {
                    var tr = src.GetTriangles(s); ht.Clear(); rt.Clear();
                    for (int i = 0; i + 2 < tr.Length; i += 3)
                    {
                        float cy = (v[tr[i]].y + v[tr[i + 1]].y + v[tr[i + 2]].y) / 3f;
                        var dst = cy > tiltSplitY ? ht : rt; dst.Add(tr[i]); dst.Add(tr[i + 1]); dst.Add(tr[i + 2]);
                    }
                    hm.SetTriangles(ht, s, true); rm.SetTriangles(rt, s, true);
                }
                parts = (hm, rm); splitCache[src] = parts;
            }
            var go = new GameObject("FortGunHead");                                            // "FortGun*": BuildingRuins leaves it to the gun toss
            go.layer = yaw.gameObject.layer;
            head = go.transform; head.SetParent(yaw, false);
            head.localPosition = Vector3.zero; head.localRotation = Quaternion.identity; head.localScale = Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = parts.head;
            var hr = go.AddComponent<MeshRenderer>();
            hr.sharedMaterials = mr.sharedMaterials; hr.shadowCastingMode = mr.shadowCastingMode; hr.receiveShadows = mr.receiveShadows;
            hr.lightProbeUsage = mr.lightProbeUsage; hr.reflectionProbeUsage = mr.reflectionProbeUsage;
            headR = hr; yawR = mr; headMatCount = mr.sharedMaterials.Length;
            mf.sharedMesh = parts.ring;
            BuildRams();
        }

        void BuildRams()
        {
            if (!sRamDark)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit");
                if (!sh) return;
                sRamDark = new Material(sh) { name = "FortGunRam_Dark" };
                sRamDark.SetColor("_BaseColor", new Color(0.23f, 0.24f, 0.26f)); sRamDark.SetFloat("_Metallic", 0.55f); sRamDark.SetFloat("_Smoothness", 0.45f);
                sRamSteel = new Material(sh) { name = "FortGunRam_Steel" };
                sRamSteel.SetColor("_BaseColor", new Color(0.70f, 0.71f, 0.72f)); sRamSteel.SetFloat("_Metallic", 0.9f); sRamSteel.SetFloat("_Smoothness", 0.78f);
            }
            // mesh space (FBX units, x 10.53 in game): ring top y 0.042 at (+-0.10, 0.10), head underside there ~0.066 at rest
            foreach (float x in new[] { -0.10f, 0.10f })
            {
                var r = new Ram { a = new Vector3(x, 0.041f, 0.10f), b = new Vector3(x, 0.078f, 0.10f) };
                r.sleeve = RamPart($"FortGunRam_{(x < 0 ? "L" : "R")}_Sleeve", sRamDark);
                r.rod = RamPart($"FortGunRam_{(x < 0 ? "L" : "R")}_Rod", sRamSteel);
                rams.Add(r);
            }
            neck = FortGunNeck.Build(yaw, head, sRamDark, sRamSteel);                         // K255: heavy telescopic rams, clevis, gland, lines
            PoseRams();
        }

        Transform RamPart(string n, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var col = go.GetComponent<Collider>(); if (col) DestroyImmediate(col);
            go.name = n; go.layer = yaw.gameObject.layer;
            go.transform.SetParent(yaw, false);
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.On;
            return go.transform;
        }

        /// <summary>Each ram: a dark sleeve standing on the ring top, a steel rod up to the head's underside (anchor b moves with the head).</summary>
        void PoseRams()
        {
            const float sleeveLen = 0.021f, sleeveR = 0.0105f, rodR = 0.0058f;
            bool nn = neck != null && neck.Pose();                                               // K255: the new neck from a low camera, the old rams from the RTS camera
            if (nn != neckShown) { neckShown = nn; foreach (var q in rams) { if (q.sleeve) q.sleeve.gameObject.SetActive(!nn); if (q.rod) q.rod.gameObject.SetActive(!nn); } }
            foreach (var r in rams)
            {
                if (!r.sleeve || !r.rod) continue;
                Vector3 b = head.localPosition + head.localRotation * r.b, d = b - r.a;
                float len = Mathf.Max(1e-4f, d.magnitude); var dir = d / len; var rot = Quaternion.FromToRotation(Vector3.up, dir);
                float sl = Mathf.Min(sleeveLen, len);
                r.sleeve.localRotation = rot; r.sleeve.localPosition = r.a + dir * (sl * 0.5f); r.sleeve.localScale = new Vector3(sleeveR * 2f, sl * 0.5f, sleeveR * 2f);
                float r0 = sl * 0.6f, rl = Mathf.Max(1e-4f, len - r0);
                r.rod.localRotation = rot; r.rod.localPosition = r.a + dir * (r0 + rl * 0.5f); r.rod.localScale = new Vector3(rodR * 2f, rl * 0.5f, rodR * 2f);
            }
        }

        // ================================================================== fire
        [ContextMenu("Test: fire at 25 m ahead")]
        public void TestFire()
        {
            if (!Bound) return;
            var p = yaw.position + Vector3.ProjectOnPlane(BoreW(), Vector3.up).normalized * 25f; p.y = game.GroundY(p.x, p.z) + 0.5f;
            Fire(p);
        }

        void Fire(Vector3 aim)
        {
            Vector3 m0 = muzzle.position;
            if (!Solve(m0, aim, out var v00, out float T0, out _)) { v00 = BoreW() * 30f; T0 = 1f; FireAlong(m0, v00, T0, aim); return; }
            LobArc(m0, aim, T0, out v00, out float g0, out float e0, elev);                        // fortgun_v2: along the barrel, over the fort
            NoteArc(e0, aim);
            FireAlong(m0, v00, T0, aim, g0);
        }

        void FireAlong(Vector3 m, Vector3 v0, float T, Vector3 aim, float g = G)
        {
            var fx = FxManager.I;
            Vector3 dir = BoreW();
            if (MuzzleBlastOverride == null || !MuzzleBlastOverride(m, dir))
            {
                bool zfx = FxMuzzle(m, dir);                                                         // fx helper: OpenZFxPlayer.Muzzle(.., FxWeapon.HeavyShell)
                if (fx)
                {
                    if (!zfx) fx.MuzzleFlash(m, dir, true);                                          // FxManager.cs:297
                    fx.LaunchFlash(m + dir * 0.4f, -dir, 1.8f);                                     // FxManager.cs:390 throws flame + smoke along -heading = forward
                    fx.AddLight(m + dir * 0.8f, new Color(1f, 0.62f, 0.3f), 16f, 18f, 0.22f, 0.25f, true);   // FxManager.cs:423: the flash lights roof and ground
                    // blast dust kicked off the roof: turret-platform height (the yaw pivot sits on the ring flange, y 7.26 in the prefab)
                    var roof = new Vector3(m.x + dir.x * 1.2f, yaw.position.y - 0.04f, m.z + dir.z * 1.2f);
                    fx.Dust(roof, 0.9f);
                }
                SmokeRing(m + dir * 0.5f, dir);
                MuzzleWisps();                                                                       // fortgun_v2: smoke curls off the muzzle as the barrel runs out
            }
            LaunchShell(m, v0, T, g);
            OpenZHeavyShells.MuzzleBlast(m, dir, true, false);                                      // nuke_fx_v3: shock ring, bore smoke, deep layer, shake
            shotAge = 0f; lastShot = Time.time; Shots++; twistSign = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            // sound: boom, breech clank as the barrel runs back, incoming whistle for long lobs (sound_events.json triggers)
            if (!Sfx(sndFire, m, 1f) && !Sfx(sndFireAlt, m, 1f)) Legacy("fortgun", m, 1f, LegacyMatch.BankHeavyTankShot);
            Legacy("fortgun_body", m, 0.7f, "v3-tank-shot-b");
            Later(0.35f, () => { if (this && muzzle && !Sfx(sndBreech, muzzle.position, 1f)) Legacy("fortgun_breech", muzzle.position, 0.5f, "mech-servo-2"); });
            var impact = v0 * T + 0.5f * T * T * Vector3.down * g + m;
            if (T >= 1.2f && !OpenZHeavyShells.Enabled) Later(T - 1.1f, () => { if (!Sfx(sndWhistle, impact, 1f)) Legacy("fortgun_whistle", impact, 0.45f, "nuke-descent-20260909"); });   // nuke_fx_v3: the heavy shell whistles itself
            Fired?.Invoke(this, m, aim);
        }

        /// <summary>DV7: a real fort-gun shot: muzzle blast, recoil and sound as Fire, the shell on the sim's own flight time to
        /// the sim's impact point (no cosmetic blast at the end: the sim's MissileExploded draws it).</summary>
        void FireReal(Vector3 impact, float T)
        {
            Vector3 m = muzzle.position;
            T = Mathf.Max(0.05f, T / Gs);                                                            // fortgun_v2: sim seconds -> real seconds
            LobArc(m, impact, T, out var v0, out float g, out float e, elev);                       // fortgun_v2: along the barrel, over the fort
            NoteArc(e, impact);
            lastShot = Time.time - minShotInterval;                                                  // never throttled: the sim paces it
            realNext = true;
            FireAlong(m, v0, T, impact, g);
            if (ZWorld.FortMortarDeviation) { MortarShots++; MortarSounds(m); }                       // combat_nb_v1 NB27: a heavier boom + the whoosh
            CombatAudio.I?.PlayPitched("fortgun_boom", m, LegacyMatch.GainHeavyTankShot, 0f, Emitter, 0.94f, BankFortBoom);
        }
        bool realNext;
        /// <summary></summary>
        public static readonly string[] BankFortBoom = { "oz_boom_fort_a", "oz_boom_fort_b", "oz_boom_fort_c" };

        bool OnScreen(Vector3 p)
        {
            var cam = Camera.main; if (!cam) return false;
            var vp = cam.WorldToViewportPoint(p);
            return vp.z > 0f && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f;
        }

        void Impact(Vector3 p)
        {
            if (ImpactOverride == null || !ImpactOverride(p, impactStrength))
            {
                var fx = FxManager.I;
                if (FxBlast(p + Vector3.up * 0.3f)) { if (fx) fx.Dust(p, 1.4f); }                  // fx helper: OpenZFxPlayer.Blast (large, cosmetic: no prop breaking)
                else if (fx) { fx.Explosion(p + Vector3.up * 0.3f, impactStrength); fx.Dust(p, 1.4f); }   // FxManager.cs:212 (fireball, smoke, debris, ring, scorch, light, shake)
            }
            if (!Sfx(sndImpact, p, 1f)) Legacy("fortgun_impact", p, 1f, "explosion-v1-20260909", "explosion-v2-20260909", "explosion-v3-20260909");
            OpenZHeavyShells.Impact(p, true);                                                        // nuke_fx_v3: crater, debris, shake by distance
        }

        // ================================================================== combat_nb_v1 NB27: reload ring, landing, mortar sounds
        LineRenderer ringBack, ringFill; Vector3[] ringPts; float readySince = -1f, ringVis;
        /// <summary></summary>
        public static int RingShownFrames, RingHiddenReadyFrames;
        static Material sRing;
        const int RingSegs = 72;

        /// <summary>A flat ring round the turret on the roof: a dim full circle and an amber arc that fills clockwise from the barrel's
        /// heading over the sim's reload (ZObject.FortGunReload01); when ready it pulses for 1.5 s, then stays a faint full ring. Hidden
        /// without DV25, on a neutral / destroyed fort and once the gun is gone.</summary>
        void ReloadRing()
        {
            bool show = Bound && !tossed && Fort != null && !Fort.Destroyed && !Fort.FortGunDestroyed && Fort.Owner != Team.Neutral &&
                        ZWorld.FortGunDeviation && ZWorld.FortMortarDeviation && yaw && yaw.parent;
            if (!show) { if (ringBack) { ringBack.enabled = false; ringFill.enabled = false; } ringVis = 0f; return; }
            if (!ringBack && !BuildRing()) return;
            var up = transform.up; var c = yaw.parent.TransformPoint(yawRestPos) + up * 0.3f;
            float k = (float)Fort.FortGunReload01;
            if (k >= 0.999f) { if (readySince < 0f) readySince = Time.time; } else readySince = -1f;
            OzSoundV3.FortGunReload(Fort, c, k);                                                         // sound_v3: reloaded
            // Shown while charging (fades in over .35 s), a short ready
            // pulse once full, then it fades out over .7 s and stays hidden until the next shot starts the next charge
            bool want = k < 0.999f || (readySince >= 0f && Time.time - readySince < 1.1f);
            ringVis = Mathf.MoveTowards(ringVis, want ? 1f : 0f, Time.deltaTime / (want ? 0.35f : 0.7f));
            bool vis = ringVis > 0.005f;
            ringBack.enabled = ringFill.enabled = vis;
            if (vis) RingShownFrames++; else if (k >= 0.999f) RingHiddenReadyFrames++;
            if (!vis) return;
            float ve = Mathf.SmoothStep(0f, 1f, ringVis);
            ringBack.startColor = ringBack.endColor = new Color(0.08f, 0.08f, 0.08f, 0.5f * ve);
            float start = heading * Mathf.Deg2Rad, sweep = (k >= 0.999f ? 1f : k) * 2f * Mathf.PI;
            int n = Mathf.Max(2, Mathf.CeilToInt(RingSegs * (k >= 0.999f ? 1f : k)) + 1);
            for (int i = 0; i <= RingSegs; i++) { float a = i * 2f * Mathf.PI / RingSegs; ringPts[i] = c + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * ringRadius; }
            ringBack.positionCount = RingSegs + 1; ringBack.SetPositions(ringPts);
            ringFill.positionCount = n;
            for (int i = 0; i < n; i++) { float a = start + sweep * i / (n - 1); ringFill.SetPosition(i, c + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * ringRadius + up * 0.02f); }
            float alpha = (k >= 0.999f ? 0.55f + 0.4f * Mathf.Sin((Time.time - readySince) * 9f) : 0.95f) * ve;
            var amber = new Color(1f, 0.72f, 0.24f, alpha);
            ringFill.startColor = ringFill.endColor = amber;
        }

        bool BuildRing()
        {
            if (!sRing) { var sh = Shader.Find("Sprites/Default"); if (!sh) return false; sRing = new Material(sh) { name = "FortGunReloadRing" }; }
            ringPts = new Vector3[RingSegs + 1];
            LineRenderer Make(string n, float w, Color col)
            {
                var go = new GameObject(n); go.transform.SetParent(FxRoot(), false);
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);                               // TransformZ alignment: the ribbon lies flat
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = sRing; lr.useWorldSpace = true; lr.loop = false; lr.widthMultiplier = w; lr.numCapVertices = 2; lr.numCornerVertices = 2;
                lr.alignment = LineAlignment.TransformZ; lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false;
                lr.startColor = lr.endColor = col; lr.enabled = false;
                return lr;
            }
            ringBack = Make("FortGunReloadRingBack_" + Fort.Id, ringWidth * 1.35f, new Color(0.08f, 0.08f, 0.08f, 0.5f));
            ringFill = Make("FortGunReloadRing_" + Fort.Id, ringWidth, new Color(1f, 0.72f, 0.24f, 0.95f));
            return true;
        }

        /// <summary>A real fort shell landed (the sim's MissileExploded, drawn by CombatView): on screen the camera shakes, and a deep boom
        /// and a heavy impact sit under the blast.</summary>
        void ShellLanded(Vector3 at)
        {
            if (!ZWorld.FortMortarDeviation) return;
            MortarLandings++;
            // in view the landing's kick is made once, by the heavy shell's impact (OpenZHeavyShells.Impact, drawn later in this frame):
            // asked here first, this smaller one took the pause and held the impact's own kick back
            if (rts && OpenZHeavyShells.Enabled && OnScreen(at)) { if (OzShakeGate.SettingOn) MortarShakes++; }
            else if (rts && rts.Shake(landShake, at, "fort_shell")) MortarShakes++;                 // heavy shells off: the old rule (OzShakeGate: on screen + cooldown)
            else if (rts && !OnScreen(at)) rts.Shake(landShake * 0.22f, at, "fort_shell_far", false);
            var au = CombatAudio.I; if (OzAirShots.TakeQuietLanding() || !au) return;                // K337: a miss at the flying shuttle: its small air burst plays its own pop
            au.PlayPitched("heavy", at, LegacyMatch.GainHeavy, 0f, Emitter, 0.8f, LegacyMatch.BankHeavy);
            au.PlayPitched("tank_impact", at, LegacyMatch.GainTankImpact, 0f, Emitter, 0.75f, LegacyMatch.BankTankImpact);
        }

        /// <summary>The mortar shot: under wpn_fortgun a deeper second heavy-tank boom and the cannon crack, then the shell's whoosh as
        /// it climbs (the heavy shell itself whistles down, OpenZHeavyShells).</summary>
        void MortarSounds(Vector3 m)
        {
            var au = CombatAudio.I; if (!au) return;
            au.PlayPitched("fortgun", m, LegacyMatch.GainHeavyTankShot, 0f, Emitter, 0.72f, LegacyMatch.BankHeavyTankShot);
            au.PlayPitched("gunshot", m, LegacyMatch.GainDefault * 1.6f, 0f, Emitter, 0.8f, LegacyMatch.BankCannon);
            Later(0.12f, () => { if (this && muzzle) CombatAudio.I?.PlayPitched("shell_whoosh", muzzle.position, LegacyMatch.GainDefault * 1.6f, 0f, Emitter, 0.72f, LegacyMatch.BankShellWhoosh); });
        }

        // ================================================================== fortgun_v2: an arc that clears the fort
        // fort_heightfield.py): 4 flat tower pads (top 9.25 m, radius 2.05 m), walls / roof <= 8.76 m inside +-10.05 x +-9.85 m.
        // Plus the cannons standing on the pads (their bounds, ScanCannons) and FlagView's fort flag (4.3 m pole on the roof socket,
        // 3.0 x 1.9 m cloth flying downwind, never into the 62 deg cone toward the turret, +-9 deg sway).
        static readonly Vector2[] TowerPads = { new Vector2(-6.92f, -6.58f), new Vector2(6.92f, -6.58f), new Vector2(-6.93f, 5.34f), new Vector2(6.93f, 5.34f) };
        const float TowerTop = 9.25f, TowerR = 2.1f, WallTop = 8.76f, FortHalfX = 10.05f, FortHalfZ = 9.85f;
        const float FlagPoleH = 4.3f, ClothLen = 3.0f, ClothDrop = 1.9f;
        static readonly int FogWindId = Shader.PropertyToID("_FogWind");
        float Gs => Mathf.Max(0.05f, game.gameSpeed);

        /// <summary>The shell leaves `from` at elevation e along the horizontal line to `to` and lands there after T s: the visual
        /// gravity follows (g = 2 (h tan e - dy) / T^2). e = `prefer` (the barrel's own elevation at fire time) when that arc
        /// clears the fort, else the lob style for the range (lobNear..lobFar) raised in 1 deg steps until it clears, at most
        /// maxLaunch. Returns false when nothing clears (the steepest arc is used).</summary>
        bool LobArc(Vector3 from, Vector3 to, float T, out Vector3 v0, out float g, out float elevDeg, float prefer = float.NaN, float extraClear = 0f)
        {
            var d = to - from; float h = Mathf.Sqrt(d.x * d.x + d.z * d.z);
            T = Mathf.Max(0.2f, T);
            UpdateCloth();
            float eStyle = Mathf.Clamp(Mathf.Lerp(lobNear, lobFar, Mathf.InverseLerp(14f, 26f, h)), minElevation, maxElevation);
            if (h < 0.5f) { g = G; v0 = (d + 0.5f * G * T * T * Vector3.up) / T; elevDeg = 90f; return false; }
            if (!float.IsNaN(prefer) && TryArc(from, to, d, h, T, Mathf.Clamp(prefer, minElevation, maxLaunch), extraClear, out v0, out g)) { elevDeg = Mathf.Clamp(prefer, minElevation, maxLaunch); return true; }
            for (float e = eStyle; e <= maxLaunch + 0.01f; e += 1f)
                if (TryArc(from, to, d, h, T, e, extraClear, out v0, out g)) { elevDeg = e; return true; }
            elevDeg = maxLaunch; ArcFor(d, h, T, maxLaunch, out v0, out g);
            return false;
        }

        static void ArcFor(Vector3 d, float h, float T, float e, out Vector3 v0, out float g)
        {
            g = 2f * (h * Mathf.Tan(e * Mathf.Deg2Rad) - d.y) / (T * T);
            v0 = new Vector3(d.x / T, (d.y + 0.5f * g * T * T) / T, d.z / T);
        }

        bool TryArc(Vector3 from, Vector3 to, Vector3 d, float h, float T, float e, float extraClear, out Vector3 v0, out float g)
        {
            ArcFor(d, h, T, e, out v0, out g);
            return g >= arcGravityMin && g <= arcGravityMax && ArcClear(from, v0, g, T, to, arcClearance + extraClear);
        }

        /// <summary>36 samples along the arc (the last 1.5 m around the impact excluded) against the fort's walls, tower pads,
        /// the cannons on them (each renderer's bounds) and the flag, each with c metres of air.</summary>
        bool ArcClear(Vector3 from, Vector3 v0, float g, float T, Vector3 to, float c)
        {
            const int N = 36;
            float tr2 = (TowerR + c) * (TowerR + c);
            for (int i = 1; i < N; i++)
            {
                float t = T * i / N;
                var p = from + v0 * t + 0.5f * g * t * t * Vector3.down;
                float lx = p.x - to.x, lz = p.z - to.z; if (lx * lx + lz * lz < 2.25f) continue;          // the landing zone
                var l = transform.InverseTransformPoint(p);                                               // Z_fort prefab-root metres
                if (Mathf.Abs(l.x) < FortHalfX + c && Mathf.Abs(l.z) < FortHalfZ + c && l.y < WallTop + c) return false;
                foreach (var tp in TowerPads) { float ax = l.x - tp.x, az = l.z - tp.y; if (ax * ax + az * az < tr2 && l.y < TowerTop + c) return false; }
                foreach (var b in padCannonBounds) { var bb = b; bb.Expand(2f * c); if (bb.Contains(p)) return false; }
                if (hasFlagPole && FlagBlocks(p, c)) return false;
            }
            return true;
        }

        bool FlagBlocks(Vector3 p, float c)
        {
            Vector3 upW = transform.up, rel = p - flagPoleW; float ry = Vector3.Dot(rel, upW); var rh = rel - upW * ry;
            if (ry > FlagPoleH + 0.2f + c || ry < -0.5f) return false;                                     // above the finial / under the roof
            if (rh.magnitude < 0.35f + c) return true;                                                    // the pole
            if (ry < FlagPoleH - ClothDrop - 0.2f - c) return false;                                      // below the cloth
            foreach (var dir in clothDirs)
            {
                float s = Vector3.Dot(rh, dir); if (s < -0.3f || s > ClothLen + 0.3f) continue;
                if ((rh - dir * s).magnitude < 0.6f + c) return true;                                     // the cloth (+-0.6 m flutter)
            }
            return false;
        }

        /// <summary>Downwind cloth directions as FlagView flies them (FlagView.UpdateWind / PoseCloth): the Atmosphere's _FogWind (or
        /// FlagView's fallback), +-9 deg sway, never inside the 62 deg cone toward the turret.</summary>
        void UpdateCloth()
        {
            if (!hasFlagPole) return;
            Vector4 fw = Shader.GetGlobalVector(FogWindId);
            var w = new Vector3(fw.x, 0f, fw.z);
            if (w.sqrMagnitude < 0.01f) w = game.level == "L17" ? new Vector3(1.2f, 0f, 0.6f) : new Vector3(3f, 0f, 1.5f);
            w.Normalize();
            var toTurret = yaw.position - flagPoleW; toTurret.y = 0f;
            bool block = toTurret.sqrMagnitude > 0.01f; if (block) toTurret.Normalize();
            float cos62 = Mathf.Cos(62f * Mathf.Deg2Rad);
            for (int k = 0; k < 3; k++)
            {
                var dir = Quaternion.Euler(0f, (k - 1) * 9f, 0f) * w;
                if (block && Vector3.Dot(dir, toTurret) > cos62)
                    dir = Quaternion.Euler(0f, (Vector3.Cross(toTurret, dir).y >= 0f ? 1f : -1f) * 62f, 0f) * toTurret;
                clothDirs[k] = dir;
            }
        }

        bool Dv7(ZObject t) => ZWorld.FortGunDeviation && t != null && t == Fort.FortGunTarget;
        /// <summary></summary>
        bool Committed => ZWorld.FortGunDeviation && Fort != null && Fort.FortGunCommitted;

        /// <summary>DV7: the ground point the sim's shell will hit (target led by its velocity over the flight, like the sim's
        /// EstimateMissileTarget) and the flight time in real seconds (distance from the fort centre / shell speed).</summary>
        Vector3 Dv7Aim(ZObject t, out float T)
        {
            if (Committed)
            {
                double cx = Fort.FortGunAimX - Fort.CenterX, cy = Fort.FortGunAimY - Fort.CenterY, cd = Math.Sqrt(cx * cx + cy * cy);
                T = (float)(ZWorld.FortMortarDeviation ? ZObject.FortMortarFlight(cd) : cd / simShellSpeed) / Gs;
                return OzShuttleAim.AirAim(Fort.FortGunTarget, PxGround(Fort.FortGunAimX, Fort.FortGunAimY), T * Gs);   // DV118: the flying shuttle's height
            }
            double dx = t.CenterX - Fort.CenterX, dy = t.CenterY - Fort.CenterY;
            if (ZWorld.FortMortarDeviation)
            {   // combat_nb_v1 NB27: the sim lobs at where the target stands (no lead) on the lob's own flight time
                T = (float)ZObject.FortMortarFlight(Math.Sqrt(dx * dx + dy * dy)) / Gs;
                var q = AimPoint(t); q.y = game.GroundY(q.x, q.z); q = OzShuttleAim.AirAim(t, q, T * Gs);   // DV118: the flying shuttle's height
                return q;
            }
            T = (float)(Math.Sqrt(dx * dx + dy * dy) / simShellSpeed) / Gs;
            var p = AimPoint(t) + SimVelocity(t) * (T * Gs);
            p.y = game.GroundY(p.x, p.z);
            return p;
        }

        /// <summary>A new DV7 target is laid by the sim in FortGunLay (1.4 s): the time left (real s) before its shot, or -1.</summary>
        float CatchUpLeft()
        {
            if (!Committed && !Dv7(target)) return -1f;
            return (float)(Fort.FortGunReadyAt - game.World.Time) / Gs - 0.15f;
        }
        float CatchSpeed(float need, float baseSpeed, float left) => left > 0f ? Mathf.Clamp(need / Mathf.Max(0.25f, left) * 1.8f, baseSpeed, catchUpMaxSpeed) : baseSpeed;
        static float CatchSmooth(float baseSmooth, float left) => left > 0f ? Mathf.Clamp(left * 0.3f, 0.08f, baseSmooth) : baseSmooth;

        void NoteArc(float launchElev, Vector3 impact)
        {
            Arcs++; LastLaunchElev = launchElev;
            float gap = launchElev - elev;
            if (gap > 0.5f) ArcsSteeperThanBarrel++;
            if (launchElev >= maxLaunch - 0.01f) ArcsBlocked++;
            MaxElevGap = Mathf.Max(MaxElevGap, Mathf.Abs(gap));
            var d = impact - yaw.position; d.y = 0f;
            if (d.sqrMagnitude > 1f) MaxHeadingGap = Mathf.Max(MaxHeadingGap, Mathf.Abs(Mathf.DeltaAngle(heading, HeadingOf(d))));
        }

        /// <summary>fortgun_v2: smoke keeps curling off the muzzle for ~1.3 s while the barrel runs out again.</summary>
        void MuzzleWisps()
        {
            for (int k = 0; k < 6; k++)
                Later(0.2f + k * 0.2f, () =>
                {
                    if (!this || !muzzle || !smokePs) return;
                    float lit = SceneLight(); var bore = BoreW(); var ep = new ParticleSystem.EmitParams();
                    for (int j = 0; j < 2; j++)
                    {
                        ep.position = muzzle.position + bore * 0.15f + UnityEngine.Random.insideUnitSphere * 0.15f;
                        ep.velocity = Vector3.up * UnityEngine.Random.Range(0.5f, 1.0f) + bore * UnityEngine.Random.Range(0.1f, 0.4f) + UnityEngine.Random.insideUnitSphere * 0.15f;
                        ep.startSize = UnityEngine.Random.Range(0.45f, 0.75f); ep.startLifetime = UnityEngine.Random.Range(1.6f, 2.4f);
                        ep.rotation = UnityEngine.Random.Range(0f, 360f);
                        ep.startColor = new Color(0.72f * lit, 0.70f * lit, 0.66f * lit, 0.5f);
                        smokePs.Emit(ep, 1);
                    }
                });
        }

        void Later(float delay, Action a) => later.Add((Time.time + delay, a));
        void RunLater()
        {
            for (int i = later.Count - 1; i >= 0; i--)
                if (Time.time >= later[i].at) { var a = later[i].a; later.RemoveAt(i); a?.Invoke(); }
        }

        void TraverseSound()
        {
            bool moving = Mathf.Abs(headingVel) > 20f;                                              // sound_events.json: yaw rate > 20 deg/s
            if (moving == traversing) return;
            traversing = moving;
            if (moving)
            {
                if (!StartLoop("fortgun_" + Fort.Id, sndTraverse, yaw) && Time.time > servoCool)
                { servoCool = Time.time + 1.5f; Legacy("fortgun_servo", yaw.position, 0.55f, "mech-servo-1", "mech-servo-2"); }
            }
            else StopLoop("fortgun_" + Fort.Id);
        }

        // ================================================================== shells, trails, smoke (pooled, outside the fort hierarchy)
        class Shell
        {
            public GameObject go; public Transform body, hot; public TrailRenderer smoke, tracer; public Renderer[] rs;
            public Vector3 p0, v0; public float t, T, dieAt, g = G; public bool flying, busy, real;   // fortgun_v2: g = the arc's gravity
        }
        readonly List<Shell> shells = new List<Shell>();
        ParticleSystem smokePs;
        static Transform fxRoot;
        static Material sSmoke, sTrail, sShell, sHot, sTracer; static Mesh sCapsule, sSphere;

        static Transform FxRoot() { if (!fxRoot) fxRoot = new GameObject("OZ_FortGunFx").transform; return fxRoot; }

        static Color Hdr(Color c, float gain) { var l = c.linear * gain; var g = l.gamma; g.a = 1f; return g; }   // as FxManager.Lin: gain in linear space

        void EnsureShared()
        {
            if (!sCapsule)
            {
                var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule); sCapsule = cap.GetComponent<MeshFilter>().sharedMesh; DestroyImmediate(cap);
                var sph = GameObject.CreatePrimitive(PrimitiveType.Sphere); sSphere = sph.GetComponent<MeshFilter>().sharedMesh; DestroyImmediate(sph);
            }
            var fxLit = Shader.Find("OpenZ/FxLit"); var beam = Shader.Find("OpenZ/Beam"); var sprite = Shader.Find("Sprites/Default");
            // base and a short ember-red tracer (was gunmetal-blue + an amber x3.2 core and an x3.5 tracer that bloomed white)
            if (!sShell) sShell = shellMaterial ? shellMaterial : fxLit ? NewFxLit(fxLit, new Color(0.17f, 0.14f, 0.11f), Hdr(new Color(1f, 0.45f, 0.14f), 0.12f), 0.1f) : null;
            if (!sHot && fxLit) sHot = NewFxLit(fxLit, Color.black, Hdr(new Color(1f, 0.38f, 0.08f), 1.5f), 1f);
            if (!sTracer) { sTracer = tracerMaterial; if (!sTracer && beam) { sTracer = new Material(beam) { name = "FortGunTracer" }; sTracer.SetColor("_Color", Hdr(new Color(1f, 0.34f, 0.08f), 1.7f)); } }
            if (!sTrail && (smokeMaterial || sprite)) { sTrail = smokeMaterial ? new Material(smokeMaterial) : new Material(sprite); sTrail.name = "FortGunSmokeTrail"; sTrail.mainTexture = SoftTexture(true); }
            if (!sSmoke && (smokeMaterial || sprite)) { sSmoke = smokeMaterial ? new Material(smokeMaterial) : new Material(sprite); sSmoke.name = "FortGunSmokePuff"; sSmoke.mainTexture = SoftTexture(false); }
        }

        static Material NewFxLit(Shader s, Color baseC, Color emis, float unlit)
        {
            var m = new Material(s) { name = "FortGunFxLit", enableInstancing = false };                // single renderers (FxManager instances its pools)
            m.SetColor("_BaseColor", baseC); m.SetColor("_EmissionColor", emis); m.SetFloat("_Unlit", unlit); m.SetFloat("_Alpha", 1f);
            return m;
        }

        /// <summary>Soft white alpha textures: trail = soft across (v) with a puffy Perlin break-up along (u); puff = round.</summary>
        static Texture2D SoftTexture(bool trail)
        {
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = trail ? "FortGunTrailTex" : "FortGunPuffTex" };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N, v = (y + 0.5f) / N, a;
                    if (trail) a = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(v * 2f - 1f)), 1.5f) * (0.72f + 0.28f * Mathf.PerlinNoise(u * 7f, v * 3f));
                    else { float r = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f; a = Mathf.Pow(Mathf.Clamp01(1f - r), 1.3f) * (0.8f + 0.2f * Mathf.PerlinNoise(u * 5f, v * 5f)); }
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            return tex;
        }

        Shell GetShell()
        {
            foreach (var s in shells) if (!s.busy) return s;
            if (shells.Count >= 5) return null;
            EnsureShared();
            var sh = new Shell { go = new GameObject("FortGunShell") };
            sh.go.transform.SetParent(FxRoot(), false);
            var body = new GameObject("Body"); body.transform.SetParent(sh.go.transform, false);
            body.transform.localScale = new Vector3(0.34f, 0.46f, 0.34f);                          // capsule 2 m tall -> a 0.92 m heavy shell
            body.AddComponent<MeshFilter>().sharedMesh = sCapsule;
            var br = body.AddComponent<MeshRenderer>(); br.sharedMaterial = sShell; br.shadowCastingMode = ShadowCastingMode.On;
            sh.body = body.transform;
            var hot = new GameObject("Hot"); hot.transform.SetParent(sh.go.transform, false);
            hot.transform.localScale = Vector3.one * 0.3f;
            hot.AddComponent<MeshFilter>().sharedMesh = sSphere;
            var hr = hot.AddComponent<MeshRenderer>(); hr.sharedMaterial = sHot; hr.shadowCastingMode = ShadowCastingMode.Off;
            sh.hot = hot.transform;
            if (sTracer)
            {
                sh.tracer = hot.AddComponent<TrailRenderer>();
                sh.tracer.sharedMaterial = sTracer; sh.tracer.time = 0.16f; sh.tracer.minVertexDistance = 0.15f;
                sh.tracer.widthCurve = new AnimationCurve(new Keyframe(0f, 0.42f), new Keyframe(1f, 0.02f));
                sh.tracer.colorGradient = Grad(new Color(1f, 0.62f, 0.4f), 0.85f, 0f);
                sh.tracer.shadowCastingMode = ShadowCastingMode.Off; sh.tracer.receiveShadows = false; sh.tracer.alignment = LineAlignment.View;
                sh.tracer.textureMode = LineTextureMode.Stretch; sh.tracer.emitting = false;
            }
            if (sTrail)
            {
                sh.smoke = sh.go.AddComponent<TrailRenderer>();
                sh.smoke.sharedMaterial = sTrail; sh.smoke.time = 1.5f; sh.smoke.minVertexDistance = 0.3f;
                sh.smoke.widthCurve = new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.25f, 0.75f), new Keyframe(1f, 1.7f));
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(new Color(0.80f, 0.77f, 0.72f), 0f), new GradientColorKey(new Color(0.62f, 0.60f, 0.57f), 1f) },
                          new[] { new GradientAlphaKey(0.62f, 0f), new GradientAlphaKey(0.4f, 0.35f), new GradientAlphaKey(0f, 1f) });
                sh.smoke.colorGradient = g;
                sh.smoke.shadowCastingMode = ShadowCastingMode.Off; sh.smoke.receiveShadows = false; sh.smoke.alignment = LineAlignment.View;
                sh.smoke.textureMode = LineTextureMode.Stretch; sh.smoke.numCapVertices = 3; sh.smoke.emitting = false;
            }
            sh.rs = new Renderer[] { br, hr };
            sh.go.SetActive(false);
            shells.Add(sh);
            return sh;
        }

        static Gradient Grad(Color c, float a0, float a1)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) }, new[] { new GradientAlphaKey(a0, 0f), new GradientAlphaKey(a1, 1f) });
            return g;
        }

        void LaunchShell(Vector3 from, Vector3 v0, float T, float g = G)
        {
            if (OpenZHeavyShells.Enabled)
            {   // nuke_fx_v3: the big glowing shell (smoke trail, shimmer, light, whistle) on the same arc and flight time; a cosmetic
                // shell still lands on Impact(), a DV7 real one on the sim's MissileExploded
                bool real = realNext; realNext = false;
                if (OpenZHeavyShells.Fly(from, v0, g, T, true, real ? null : (Action<Vector3>)(p => Impact(p)))) return;   // fortgun_v2: the arc's own gravity
                realNext = real;
            }
            var s = GetShell(); if (s == null) { bool rr = realNext; realNext = false; if (!rr) Later(T, () => Impact(from + v0 * T + 0.5f * T * T * Vector3.down * g)); return; }
            s.busy = true; s.flying = true; s.p0 = from; s.v0 = v0; s.t = 0f; s.T = T; s.g = g; s.real = realNext; realNext = false;
            if (sTrail) { float lit = SceneLight(); sTrail.color = new Color(lit, lit, lit, 1f); }
            s.go.transform.position = from; Orient(s, v0);
            s.go.SetActive(true);
            foreach (var r in s.rs) if (r) r.enabled = true;
            if (s.smoke) { s.smoke.Clear(); s.smoke.emitting = true; }
            if (s.tracer) { s.tracer.Clear(); s.tracer.emitting = true; }
        }

        static void Orient(Shell s, Vector3 vel)
        {
            if (vel.sqrMagnitude < 1e-4f) return;
            var dir = vel.normalized;
            s.body.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(90f, 0f, 0f);         // capsule long axis (Y) along the flight
            s.hot.position = s.go.transform.position - dir * 0.42f;                                // hot base of the shell
        }

        void StepShells(float dt)
        {
            foreach (var s in shells)
            {
                if (!s.busy) continue;
                if (s.flying)
                {
                    s.t = Mathf.Min(s.t + dt, s.T);
                    var p = s.p0 + s.v0 * s.t + 0.5f * s.t * s.t * Vector3.down * s.g;
                    s.go.transform.position = p; Orient(s, s.v0 + Vector3.down * s.g * s.t);
                    if (s.t >= s.T)
                    {
                        s.flying = false; if (!s.real) Impact(p);                                  // DV7 real shells: the sim draws the blast
                        foreach (var r in s.rs) if (r) r.enabled = false;
                        if (s.smoke) s.smoke.emitting = false;
                        if (s.tracer) s.tracer.emitting = false;
                        s.dieAt = Time.time + (s.smoke ? s.smoke.time : 0.2f) + 0.1f;
                    }
                }
                else if (Time.time >= s.dieAt) { s.busy = false; s.go.SetActive(false); }
            }
        }

        /// <summary>The smoke materials are unlit: dim them with the day/night light (DayNight drives the sun and the trilight
        /// ambient, DayNight.cs:75-92) so smoke never glows at night.</summary>
        static float SceneLight()
        {
            var sun = RenderSettings.sun;
            if (!sun) { var dn = FindFirstObjectByType<DayNight>(); if (dn) sun = dn.sun; }
            float s = sun && sun.isActiveAndEnabled ? sun.intensity * Mathf.Clamp01(-sun.transform.forward.y) : 0f;
            var a = RenderSettings.ambientSkyColor; float amb = 0.2126f * a.r + 0.7152f * a.g + 0.0722f * a.b;
            return Mathf.Clamp(0.25f + amb * 0.9f + s * 0.3f, 0.3f, 1f);
        }

        /// <summary>Muzzle smoke: a ring of puffs expanding around the bore plus a forward blast cone (a pooled ParticleSystem).</summary>
        void SmokeRing(Vector3 at, Vector3 dir)
        {
            EnsureShared();
            if (!sSmoke) return;
            if (!smokePs)
            {
                var go = new GameObject("FortGunSmoke"); go.transform.SetParent(FxRoot(), false);
                smokePs = go.AddComponent<ParticleSystem>();
                smokePs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = smokePs.main; main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 240; main.startLifetime = 1.8f; main.startSize = 1f; main.startSpeed = 0f; main.gravityModifier = -0.03f;
                var em = smokePs.emission; em.enabled = false;
                var sh = smokePs.shape; sh.enabled = false;
                var sol = smokePs.sizeOverLifetime; sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.3f, 1.35f), new Keyframe(1f, 2.3f)));
                var col = smokePs.colorOverLifetime; col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.35f), new GradientAlphaKey(0f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(g);
                var lv = smokePs.limitVelocityOverLifetime; lv.enabled = true; lv.limit = 1000f; lv.drag = 2.2f;
                lv.multiplyDragByParticleSize = false; lv.multiplyDragByParticleVelocity = false;
                var rot = smokePs.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
                var pr = go.GetComponent<ParticleSystemRenderer>();
                pr.sharedMaterial = sSmoke; pr.renderMode = ParticleSystemRenderMode.Billboard; pr.sortMode = ParticleSystemSortMode.Distance;
                pr.shadowCastingMode = ShadowCastingMode.Off; pr.receiveShadows = false;
                smokePs.Play();
            }
            float lit = SceneLight();
            var right = Vector3.Cross(Vector3.up, dir).normalized; if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
            var upv = Vector3.Cross(dir, right).normalized;
            var ep = new ParticleSystem.EmitParams();
            const int ring = 26;
            for (int i = 0; i < ring; i++)
            {
                float a = i * Mathf.PI * 2f / ring;
                var radial = right * Mathf.Cos(a) + upv * Mathf.Sin(a);
                ep.position = at + radial * 0.4f;
                ep.velocity = radial * UnityEngine.Random.Range(3.4f, 4.6f) + dir * UnityEngine.Random.Range(1.2f, 2.4f);
                ep.startSize = UnityEngine.Random.Range(0.75f, 1.05f); ep.startLifetime = UnityEngine.Random.Range(1.5f, 2.2f);
                ep.rotation = UnityEngine.Random.Range(0f, 360f);
                ep.startColor = new Color(0.80f * lit, 0.77f * lit, 0.72f * lit, 0.72f);
                smokePs.Emit(ep, 1);
            }
            for (int j = 0; j < 10; j++)
            {
                ep.position = at + dir * (0.3f + j * 0.35f);
                ep.velocity = dir * UnityEngine.Random.Range(4f, 9f) + UnityEngine.Random.insideUnitSphere * 0.7f;
                ep.startSize = UnityEngine.Random.Range(1.1f, 1.7f); ep.startLifetime = UnityEngine.Random.Range(1.2f, 1.9f);
                ep.rotation = UnityEngine.Random.Range(0f, 360f);
                ep.startColor = new Color(0.58f * lit, 0.55f * lit, 0.51f * lit, 0.7f);
                smokePs.Emit(ep, 1);
            }
        }

        void OnDestroy()
        {
            all.Remove(this);
            if (Fort != null) bound.Remove(Fort.Id);
            StopLoop("fortgun_" + (Fort != null ? Fort.Id : 0));
            foreach (var s in shells) if (s.go) Destroy(s.go);
            if (smokePs) Destroy(smokePs.gameObject);
            if (tossed && yaw) Destroy(yaw.gameObject);
            if (ringBack) Destroy(ringBack.gameObject);                                              // combat_nb_v1 NB27
            if (ringFill) Destroy(ringFill.gameObject);
        }

        // ================================================================== death: the turret is blown off (classic Z)
        [ContextMenu("Test: toss turret")]
        public void TestToss() { if (Bound) StartToss(); }

        /// <summary></summary>
        public static int Tosses, TossReshown; public static float LastTossAt = -1f; public static bool LastTossFortDestroyed;
        public Transform YawT => yaw;
        public Quaternion TossEndRot => tossEndRot;                                                  // item 205: the landing rotation (OzFortGunBreakup takes the armour plates off the side that lands facing up)
        void StartToss()
        {
            if (tossed || !Bound) return;
            tossed = true; pending = false; target = null;
            StopLoop("fortgun_" + Fort.Id);
            if (!Fort.Destroyed) FortGunMount.Build(Fort, transform);   // shot off while the fort stands: the modelled wreck takes the pedestal's place (FortGunMount.cs)
            // detach BEFORE CombatView (order 50) lets BuildingBreaker hide the fort's renderers and collapse it
            yaw.SetParent(FxRoot(), true);
            var tp = yaw.GetComponent<TeamPaint>(); if (!tp) tp = yaw.gameObject.AddComponent<TeamPaint>();
            tp.Team = Fort.Owner; tp.Charred = 0.75f; OzFortGunWreckLook.Killed(yaw, tp);   // item 212: the killed cannon's wreck paint, its toned-down burn (OzFortGunWreckLook.cs)
            foreach (var r in yaw.GetComponentsInChildren<Renderer>()) r.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            // land beside the ruin: along the barrel heading +-70 deg, just outside the fort footprint
            var rnd = new System.Random(Fort.Id * 7919 + 17);
            float half = Mathf.Max(Fort.WidthPix, Fort.HeightPix) / 16f;                           // px -> m, half the long side (fort 20 x 24 m)
            float hdg = heading + (float)(rnd.NextDouble() * 140.0 - 70.0);
            var c = game.CenterWorld(Fort, false);
            var land = c + Quaternion.Euler(0f, hdg, 0f) * Vector3.forward * (half + 3f + (float)rnd.NextDouble() * 3f);
            OzTreeCrush.GunTossLanding(Fort, ref hdg, ref land, tossDuration);                     // item 360 (DV124): shot to pieces while its fort stands, it flies on the rules' toss (the trees they fell are under it)
            float gy = game.GroundY(land.x, land.z);
            tossEndRot = Quaternion.Euler((float)(rnd.NextDouble() * 50.0 - 25.0), (float)(rnd.NextDouble() * 360.0), (rnd.Next(2) == 0 ? -1f : 1f) * (80f + (float)(rnd.NextDouble() * 25.0)));
            tossStartRot = yaw.rotation; tossFrom = yaw.position;
            // resting height: put the lowest point of the rotated turret 0.2 m into the ground
            yaw.SetPositionAndRotation(new Vector3(land.x, gy, land.z), tossEndRot);
            var b = WorldBounds(yaw);
            OzRockSolid.FreeLanding(Fort, c, half, hdg, tossFrom, tossApex, ref land, ref gy, ref b);   // item 331: on free ground (no rock, building or gun under it, the flight's end clear of rocks); the rocks there solid for its pieces
            tossTo = new Vector3(land.x, gy + (gy - b.min.y) - 0.2f, land.z);
            yaw.SetPositionAndRotation(tossFrom, tossStartRot);
            tossAxis = new Vector3((float)rnd.NextDouble() - 0.5f, 0.3f, (float)rnd.NextDouble() - 0.5f).normalized;
            tossT = 0f; landed = false;
            foreach (var r in yaw.GetComponentsInChildren<Renderer>(true)) if (r is MeshRenderer && !r.enabled && !r.name.StartsWith("OZ_")) { r.enabled = true; TossReshown++; }
            if (!yaw.gameObject.activeSelf) yaw.gameObject.SetActive(true);
            Tosses++; LastTossAt = Time.time; LastTossFortDestroyed = Fort.Destroyed;
            FxManager.I?.Explosion(tossFrom + Vector3.up * 0.5f, Fort.Destroyed ? 2.4f : 1.6f);
            if (!Sfx(sndToss, tossFrom, 1f)) Legacy("fortgun_toss", tossFrom, 0.9f, "explosion-v3-20260909");
        }

        static Bounds WorldBounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return new Bounds(t.position, Vector3.zero);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        void UpdateToss(float dt)
        {
            if (!yaw || !yaw.gameObject.activeSelf) return;
            if (!landed)
            {
                tossT += dt; float k = Mathf.Clamp01(tossT / tossDuration);
                var p = Vector3.Lerp(tossFrom, tossTo, k); p.y += 4f * tossApex * k * (1f - k);
                var spin = Quaternion.AngleAxis(tossSpin * tossT, tossAxis) * tossStartRot;
                yaw.SetPositionAndRotation(p, Quaternion.Slerp(spin, tossEndRot, OzHeavyLanding.FallBlend(k)));
                if (k >= 1f)
                {
                    landed = true; landedAt = Time.time;
                    var fx = FxManager.I;
                    if (fx) { fx.Explosion(p, 1.1f); fx.Dust(p, 2f); fx.Breakup(p + Vector3.up * 0.5f, "metal", 4f, 16, 1.4f); }
                    if (rts) rts.Shake(0.5f, p, "fortgun_toss");
                    if (!Sfx(sndCrash, p, 1f)) Legacy("fortgun_crash", p, 1f, "v3-tank-impact-a", "v3-collapse-medium-b");
                    OzHeavyLanding.FortGun(yaw, p, tossFrom, tossTo, tossApex, tossDuration, Emitter);    // item 172: heavy: concrete thud, dust burst, bounce, skid, rock, lies on the ground
                    OzFortGunBreakup.Break(this, yaw, p, tossFrom, tossTo, tossApex, tossDuration, Emitter);   // item 205: on this frame it falls apart into its parts (OzFortGunBreakup.cs); off / open water = it lies whole as above
                }
                return;
            }
            float age = Time.time - landedAt;
            if (age < 8f && FxManager.I && (burnT -= dt) <= 0f)
            {   // the wreck burns for a while
                burnT = 0.28f;
                FxManager.I.Burn(yaw.position + Vector3.up * 1.2f + UnityEngine.Random.insideUnitSphere * 1.2f, Mathf.Lerp(0.9f, 0.3f, age / 8f));
            }
            if (age > wreckSeconds)
            {   // then sinks away like the tank wrecks (UnitView.Sink): nothing big lingers where units walk
                yaw.position += Vector3.down * 1.8f * dt / 3f;
                if (age > wreckSeconds + 3f) yaw.gameObject.SetActive(false);                  // shells in flight keep updating
            }
        }

        static bool fxBound; static PropertyInfo fxI; static MethodInfo fxMuzzleM, fxBlast; static object fxWeaponHeavy, fxKindMissile;

        /// <summary>
        /// float, FxBlastKind, int, float); OpenZFxCatalog.cs:42 FxBlastKind.Missile, :63 FxWeapon.HeavyShell), found by reflection so
        /// this file compiles with or without it. Without it: FxManager (which the fx import redirects to the player anyway).</summary>
        static void BindFx()
        {
            if (fxBound) return; fxBound = true;
            Type t = null, w = null, k = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t ??= asm.GetType("OpenZ.OpenZFxPlayer"); w ??= asm.GetType("OpenZ.FxWeapon"); k ??= asm.GetType("OpenZ.FxBlastKind");
            }
            if (t == null || w == null || k == null) return;
            try
            {
                fxWeaponHeavy = Enum.Parse(w, "HeavyShell"); fxKindMissile = Enum.Parse(k, "Missile");
                fxI = t.GetProperty("I", BindingFlags.Public | BindingFlags.Static);
                fxMuzzleM = t.GetMethod("Muzzle", new[] { typeof(Vector3), typeof(Vector3), w });
                fxBlast = t.GetMethod("Blast", new[] { typeof(Vector3), typeof(float), k, typeof(int), typeof(float) });
            }
            catch (Exception) { fxMuzzleM = fxBlast = null; }
        }

        static Behaviour FxPlayer() { var p = fxI != null ? fxI.GetValue(null) as Behaviour : null; return p && p.isActiveAndEnabled ? p : null; }

        static bool FxMuzzle(Vector3 at, Vector3 dir)
        {
            var p = FxPlayer(); if (!p || fxMuzzleM == null) return false;
            try { fxMuzzleM.Invoke(p, new object[] { at, dir, fxWeaponHeavy }); return true; } catch (Exception) { return false; }
        }

        bool FxBlast(Vector3 at)
        {
            var p = FxPlayer(); if (!p || fxBlast == null) return false;
            try { fxBlast.Invoke(p, new object[] { at, fxImpactRadius, fxKindMissile, Fort != null ? 910000 + Fort.Id * 131 + Shots : -1, 1f }); return true; }
            catch (Exception) { return false; }
        }

        // ================================================================== sound: AudioDirector (soft-bound) or CombatAudio
        static bool adBound; static PropertyInfo adI; static MethodInfo adPlay, adStartLoop, adStopLoop;

        /// <summary>
        /// :208 StartLoop(string, string, Vector3, Transform, float, float), :235 StopLoop(string, float)), found by reflection so
        /// this file compiles with or without it.</summary>
        static void BindAudio()
        {
            if (adBound) return; adBound = true;
            Type t = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) { t = asm.GetType("OpenZ.AudioDirector"); if (t != null) break; }
            if (t == null) return;
            adI = t.GetProperty("I", BindingFlags.Public | BindingFlags.Static);
            adPlay = t.GetMethod("Play", new[] { typeof(string), typeof(Vector3), typeof(int), typeof(float), typeof(float), typeof(string) });
            adStartLoop = t.GetMethod("StartLoop", new[] { typeof(string), typeof(string), typeof(Vector3), typeof(Transform), typeof(float), typeof(float) });
            adStopLoop = t.GetMethod("StopLoop", new[] { typeof(string), typeof(float) });
        }

        static UnityEngine.Object Director() => adI != null ? adI.GetValue(null) as UnityEngine.Object : null;
        int Emitter => 900000 + (Fort != null ? Fort.Id : 0);

        bool Sfx(string id, Vector3 pos, float vol)
        {
            if (string.IsNullOrEmpty(id)) return false;
            var d = Director(); if (!d || adPlay == null) return false;
            try { return adPlay.Invoke(d, new object[] { id, pos, Emitter, vol, 1f, null }) is bool ok && ok; }
            catch (Exception) { return false; }
        }

        bool StartLoop(string key, string id, Transform follow)
        {
            var d = Director(); if (!d || adStartLoop == null || string.IsNullOrEmpty(id)) return false;
            try { var r = adStartLoop.Invoke(d, new object[] { key, id, follow.position, follow, 0.8f, 0.9f }); return r is int i && i > 0; }                            // AudioDirector.StartLoop: 1 = playing, 0 = no clip
            catch (Exception) { return false; }
        }

        void StopLoop(string key)
        {
            var d = Director(); if (!d || adStopLoop == null) return;
            try { adStopLoop.Invoke(d, new object[] { key, 0.3f }); } catch (Exception) { }
        }

        /// <summary>Fallback when no AudioDirector: legacy CombatAudio clips (Resources/OpenZ/Sfx, CombatAudio.cs:95).</summary>
        void Legacy(string key, Vector3 pos, float vol, params string[] clips)
        {
            if (Director()) return;                                                                  // the director mutes CombatAudio anyway
            CombatAudio.I?.Play(key, pos, vol, 0f, Emitter, clips);
        }
    }
}
