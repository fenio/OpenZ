// OpenZ (GPL-3.0) - Level 16: the fort blast's shock wave THROWS the two robots' shuttle (version 1.1 item 230).
// The shuttle stands beside the red fort (L16Shuttle.cs, rule DV95). When that fort is the one that blows up at the end of the game (the
// LOSE ending of a red player), the ship stood untouched while the wave threw the robots and the trees round it. Now, view only:
//  * when the blast's wave starts (OpenZFortShockwave.Fire) and the game is over (OpenZEnding.Running: the rules decide nothing any
//    more), a ship within Reach of the blast is thrown when the wave's front reaches it (OpenZFortShockwave.TimeAt: the same front as
//    the ring, the dust wall, the robots and the trees);
//  * the blow: every light, the neon and the exhaust go dark (the ship's own destroyed look, OzShuttleLights / OzShuttleExhaust mode 2),
//    the hull is scorched (TeamPaint.Charred), earth and dust burst at its feet; a whoosh, a metal groan and breaking glass;
//  * it is LIFTED and TUMBLES: its middle flies an arc away from the blast (Travel m, apex from ArcGravity), the ship rolls over about its
//    long axis onto a side (the top tips away from the blast) and turns YawTurn degrees; the roll it lands in is the nearest to
//    RollPrefer at which it rests STABLY on its own outline (its lowest points, from its real mesh points, hold its middle with
//    StableMargin to spare), so it never balances on one engine;
//  * it LANDS HARD on the game's heavy landing (OzHeavyLanding.FortGun, the fort cannon's: a wide dust ring, puffs, chips, the heavy
//    impact sound, a short bounce, a skid along its flight, a damped rock, resting on its real lowest vertex) with a heavy metal crash
//    (dst_fortgun_break_metal), a metal knock when the bounce comes down, then it burns and smokes for SmokeSeconds;
//  * the flight lasts as long as lets it land LandBeforeCut s before the cut to the endings' scene (the impact + the end's own
//    PostImpactDelay), between FlightMin and FlightMax (FlightFree with no strike);
//  * keep-out: its way (flight + skid, the ship's radius) keeps CampClear + ShipRadius from every hero campfire (the robots' start camp
//    beside the ship) and SceneClear + ShipRadius from the endings' set, its camera spots and the lines between them
//    (OzL16LoungerSet.SceneSpots); it never lands in open water, in a standing building's square or more than RiseMost m up a slope.
//    The way is turned off the straight line from the blast by the least angle (steps of DeviateStep, at most DeviateMost) and
//    shortened (down to TravelMin) until it is clear; with no clear way the one with the most room is taken (Status says so).
// A ship already destroyed by the rules is thrown as what is left of it (its wreck); a ship out of the reach (the other fort's
// blast: the blue fort stands about 205 m away) is not touched. Nothing of the rules is read or changed.
// Switch: Enabled (off = the ship stands through the blast as before). Check tool: ThrowNow (the editor menu of this kit).
// Play check: return OpenZ.OzShuttleBlastThrow.Report();
// WRITTEN BY READING ONLY: compiled off-line with the project's code, never run in the engine. Remove this line once it runs.
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    [DefaultExecutionOrder(910)]   // LateUpdate after the shock wave's jolts (900): the flight pose is the last word of its frame
    public sealed class OzShuttleBlastThrow : MonoBehaviour
    {
        public static bool Enabled = true;
        /// <summary>Flat metres from the blast within which the ship is thrown.</summary>
        public static float Reach = 60f;
        /// <summary>The flight's flat length (m) and the shortest it may be made to keep clear; the heavy landing's skid adds up to 1.2 m.</summary>
        public static float Travel = 11f, TravelMin = 5f, SkidGuess = 1.2f;
        /// <summary>Flight seconds: least, most, without a strike; how long before the cut to the endings' scene it lands; the arc's
        /// gravity (apex = ArcGravity x T^2 / 8, 0.8 to 3.2 m).</summary>
        public static float FlightMin = 0.8f, FlightMax = 1.4f, FlightFree = 1.15f, LandBeforeCut = 0.3f, ArcGravity = 20f;
        /// <summary>The tumble: the turn about the vertical (deg), the roll it should land in (deg), the rolls tried, and the room (m) its
        /// middle must have inside its lowest points to rest.</summary>
        public static float YawTurn = 25f, RollPrefer = 95f, RollMin = 60f, RollMax = 200f, StableMargin = 0.3f, SupportBand = 0.12f;
        /// <summary>Keep-out: the ship's radius, the room to a campfire's middle, to the set / its cameras, the most turn off the
        /// blast's line and its step (deg), the most climb of the landing over the ship's ground (m).</summary>
        public static float ShipRadius = 3.4f, CampClear = 3f, SceneClear = 8f, DeviateMost = 60f, DeviateStep = 6f, RiseMost = 2.5f;
        /// <summary>The scorch of the blow (TeamPaint.Charred), the smoke after the landing (s).</summary>
        public static float Charred = 0.8f, SmokeSeconds = 14f;

        /// <summary>Play checks: the last blast in words, ships thrown / landed, sounds started, and the throw's numbers.</summary>
        public static string Status = "no blast yet", LastSounds = "";
        public static int Thrown, Landed, SoundsStarted, SoundsSent, LastPoints;
        public static float LastDist, LastReachAfter, LastLaunchAt = -1f, LastLandAt = -1f, LastFlight, LastApex, LastTravel, LastDeviation, LastRoll,
                            LastRollMargin, LastOut, LastUp, LastSpin, LastCampRoom = -1f, LastSceneRoom = -1f, LastLowestInFlight = 9f, LastLiftFrames;
        public static bool LastClearWay;
        public static Vector3 LastFrom, LastTo;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Status = "no blast yet"; LastSounds = ""; Thrown = Landed = SoundsStarted = SoundsSent = LastPoints = 0;
            LastDist = LastReachAfter = LastFlight = LastApex = LastTravel = LastDeviation = LastRoll = LastRollMargin = LastOut = LastUp = LastSpin = LastLiftFrames = 0f;
            LastLaunchAt = LastLandAt = LastCampRoom = LastSceneRoom = -1f; LastLowestInFlight = 9f; LastClearWay = false; LastFrom = LastTo = Vector3.zero;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded; }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            foreach (var s in FindObjectsByType<L16Shuttle>(FindObjectsSortMode.None))
                if (s && !s.GetComponent<OzShuttleBlastThrow>()) s.gameObject.AddComponent<OzShuttleBlastThrow>();
        }

        enum Step { Idle, Waiting, Flying, Down }
        L16Shuttle ship; DetailProp dp; TeamPaint paint;
        Step step; int pointsStage = -1;
        float seen = -100f, reachAt, t, flight, apex, roll, yaw, smokeUntil, nextSmoke, knockAt; bool knocked;
        Vector3 blast, comFrom, comTo, offRel, rollAxis, comLocal; Quaternion rot0;
        readonly List<Vector3> pts = new List<Vector3>();                                                  // the ship's outline in its own space
        static readonly List<Vector3> keepFires = new List<Vector3>();
        static readonly Vector3[] spots = new Vector3[4]; static int spotCount;
        static readonly List<Vector2> support = new List<Vector2>();
        static Vector3[] dirs;

        void Awake() { ship = GetComponent<L16Shuttle>(); dp = GetComponent<DetailProp>(); paint = GetComponent<TeamPaint>(); }

        void Start() { if (Application.isPlaying && ship) Collect(); }                                    // the mesh points are read at the level start, not in the blast's frame

        void Update()
        {
            if (!ship) return;
            if (step == Step.Idle)
            {
                float lf = OpenZFortShockwave.LastFireTime;
                if (lf > seen + 0.01f && Time.time - lf < 5f) { seen = lf; OnBlast(OpenZFortShockwave.LastCentre, lf, false); }
            }
            else if (step == Step.Waiting && Time.time >= reachAt) Launch();
        }

        void LateUpdate()
        {
            if (step == Step.Flying) Fly();
            else if (step == Step.Down) Burn();
        }

        // ------------------------------------------------------------------------------------------------ the blast
        void OnBlast(Vector3 at, float fireTime, bool check)
        {
            blast = at;
            Vector3 d = transform.position - at; d.y = 0f; float dist = d.magnitude; LastDist = dist;
            string where = $"blast at ({at.x:F0}, {at.z:F0}), the shuttle {dist:F0} m from it";
            if (!Enabled) { Status = where + ": the throw is switched off"; return; }
            if (!check && !OpenZEnding.Running) { Status = where + ": the game is still on, the view does not throw the ship"; return; }
            if (ship.RuleOff) { Status = where + ": 'Zod exact': no ship"; return; }
            if (dist > Reach) { Status = where + $": out of the throw's reach ({Reach:F0} m), it stays as it stands"; Debug.Log("[OpenZ] L16 shuttle blast: " + Status); return; }
            if (!Drawn()) { Status = where + ": nothing of the ship is drawn any more"; return; }
            reachAt = fireTime + OpenZFortShockwave.TimeAt(dist); LastReachAfter = reachAt - fireTime;
            step = Step.Waiting;
            Status = where + $": thrown when the wave's front reaches it, {LastReachAfter:F2} s after the blast";
            Debug.Log("[OpenZ] L16 shuttle blast: " + Status);
        }

        bool Drawn()
        {
            foreach (var r in GetComponentsInChildren<MeshRenderer>())
                if (r && r.enabled && r.gameObject.activeInHierarchy) return true;
            return false;
        }

        /// <summary>Check tool (the kit's editor menu, in Play): throws the ship now from the middle of the fort nearest to it, as the
        /// blast would (the game need not be over). Returns what it did.</summary>
        public static string ThrowNow()
        {
            var s = L16Shuttle.I; var g = ZGame.I;
            if (!s || !g || g.World == null) return "no shuttle or no game (level 16, in Play)";
            var me = s.GetComponent<OzShuttleBlastThrow>(); if (!me) me = s.gameObject.AddComponent<OzShuttleBlastThrow>();
            if (me.step != Step.Idle) return "the ship has been thrown already in this level";
            Vector3 best = s.transform.position + Vector3.left * 18f; float bd = float.MaxValue;
            foreach (var o in g.World.Objects)
            {
                if (o == null || o.Kind != ZKind.Building || !o.IsFort) continue;
                var c = g.CenterWorld(o); float dd = (c - s.transform.position).sqrMagnitude;
                if (dd < bd) { bd = dd; best = c; }
            }
            if (me.pts.Count == 0) me.Collect();
            me.OnBlast(best, Time.time, true);
            return Status;
        }

        // ------------------------------------------------------------------------------------------------ the ship's outline
        /// <summary>The outermost mesh points of every drawn part (96 directions each), in the ship's own space; a part whose mesh cannot
        /// be read gives its box corners.</summary>
        void Collect()
        {
            pts.Clear(); pointsStage = ship ? ship.Stage : -1;
            if (dirs == null)
            {
                dirs = new Vector3[96]; float ga = Mathf.PI * (3f - Mathf.Sqrt(5f));
                for (int i = 0; i < dirs.Length; i++)
                {
                    float y = 1f - 2f * (i + 0.5f) / dirs.Length, r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y)), a = ga * i;
                    dirs[i] = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                }
            }
            var inv = transform.worldToLocalMatrix;
            foreach (var r in GetComponentsInChildren<MeshRenderer>())
            {
                if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                var mf = r.GetComponent<MeshFilter>(); var mesh = mf ? mf.sharedMesh : null; if (!mesh) continue;
                var m = inv * r.transform.localToWorldMatrix;
                if (mesh.isReadable)
                {
                    var v = mesh.vertices; if (v.Length == 0) continue;
                    var seenIdx = new HashSet<int>();
                    foreach (var dir in dirs)
                    {
                        var dl = r.transform.InverseTransformDirection(transform.TransformDirection(dir));     // the direction in the part's own space
                        int best = 0; float bv = float.MinValue;
                        for (int j = 0; j < v.Length; j++) { float x = Vector3.Dot(v[j], dl); if (x > bv) { bv = x; best = j; } }
                        if (seenIdx.Add(best)) pts.Add(m.MultiplyPoint3x4(v[best]));
                    }
                }
                else
                {
                    var b = mesh.bounds;
                    for (int k = 0; k < 8; k++) pts.Add(m.MultiplyPoint3x4(new Vector3((k & 1) == 0 ? b.min.x : b.max.x, (k & 2) == 0 ? b.min.y : b.max.y, (k & 4) == 0 ? b.min.z : b.max.z)));
                }
            }
            LastPoints = pts.Count;
            if (pts.Count == 0) { comLocal = Vector3.up * 2.5f; return; }
            Vector3 lo = pts[0], hi = pts[0];
            foreach (var p in pts) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
            comLocal = (lo + hi) * 0.5f;                                                                   // its middle: the middle of its outline's box
        }

        // ------------------------------------------------------------------------------------------------ the launch
        void Launch()
        {
            var g = ZGame.I;
            if (ship.Stage != pointsStage || pts.Count == 0) Collect();                                     // destroyed by the rules since the level start: what is left of it
            if (!g || pts.Count < 4) { step = Step.Idle; Status += "; NOT thrown: " + (!g ? "no game" : "no outline of the ship"); return; }
            rot0 = transform.rotation;
            Vector3 com = transform.TransformPoint(comLocal);
            offRel = Quaternion.Inverse(rot0) * (com - transform.position);
            var rel = new Vector3[pts.Count];
            for (int i = 0; i < pts.Count; i++) rel[i] = transform.TransformPoint(pts[i]) - com;
            Vector3 away = transform.position - blast; away.y = 0f;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Flat(transform.forward);
            rollAxis = Flat(rot0 * Vector3.forward); if (rollAxis.sqrMagnitude < 0.5f) rollAxis = Vector3.Cross(Vector3.up, away).normalized;
            float sign = Vector3.Dot(Quaternion.AngleAxis(10f, rollAxis) * Vector3.up, away) >= 0f ? 1f : -1f;   // the top tips away from the blast
            float rollAbs = PickRoll(rel, sign, out float margin, out float low);
            roll = sign * rollAbs;
            Vector3 dir = PickWay(away, com, out float dev, out float travel, out float campRoom, out float sceneRoom, out bool clear);
            float cut = OpenZNukeStrike.LastImpactTime + OpenZVictory.PostImpactDelay;
            flight = Time.time - OpenZNukeStrike.LastImpactTime < 5f ? Mathf.Clamp(cut - LandBeforeCut - Time.time, FlightMin, FlightMax) : FlightFree;
            apex = Mathf.Clamp(ArcGravity * flight * flight / 8f, 0.8f, 3.2f);
            Vector3 land = com + dir * travel; land.y = g.GroundY(land.x, land.z) + low - OzHeavyLanding.Sink;
            comFrom = com; comTo = land;
            yaw = YawTurn * (dev >= 0f ? 1f : -1f);
            t = 0f; step = Step.Flying; LastLowestInFlight = 9f; LastLiftFrames = 0;
            // the blow: dark, scorched, earth and dust at its feet, the sounds of the hit
            if (ship.lights) ship.lights.SetMode(2);
            if (ship.exhaust) ship.exhaust.SetMode(2);
            if (paint) paint.Charred = Mathf.Max(paint.Charred, Charred);
            if (dp) { dp.StopLoop(); dp.enabled = false; }                                                  // no knock wobble or idle loop on a thrown ship
            var foot = transform.position;
            if (FxManager.I) { FxManager.I.Dust(foot + Vector3.up * 0.3f, 1.4f); FxManager.I.Breakup(foot + Vector3.up * 0.3f, "stone", 4f, 10, 1.1f); }
            var fxp = OpenZFxPlayer.I; if (fxp && fxp.isActiveAndEnabled) fxp.Dust(foot, 1.6f);
            LastSounds = "";
            Sound("dst_turret_toss", com, 1f, 0.75f); Sound("dst_structure_groan", com, 0.9f, 0.9f); Sound("dst_glass", com, 0.8f, 0.9f);
            // the numbers
            Thrown++; LastLaunchAt = Time.time; LastFlight = flight; LastApex = apex; LastTravel = travel; LastDeviation = dev; LastRoll = rollAbs; LastRollMargin = margin;
            LastOut = travel / flight; LastUp = ((comTo.y - comFrom.y) + 4f * apex) / flight; LastSpin = rollAbs / flight; LastCampRoom = campRoom; LastSceneRoom = sceneRoom; LastClearWay = clear;
            LastFrom = comFrom; LastTo = comTo;
            Status = $"thrown {LastReachAfter:F2} s after the blast: {travel:F1} m {(Mathf.Abs(dev) < 0.5f ? "straight away from the blast" : $"turned {dev:+0;-0} deg off the blast's line")}, flight {flight:F2} s, apex {apex:F2} m, "
                   + $"launch {LastOut:F1} m/s out + {LastUp:F1} m/s up, roll {rollAbs:F0} deg ({LastSpin:F0} deg/s, rests with {margin:F2} m to spare), turn {yaw:+0;-0} deg; "
                   + $"room to the camp {(campRoom > 900f ? "no campfire" : campRoom.ToString("F1") + " m")}, to the endings' set / camera {(sceneRoom > 900f ? "no set" : sceneRoom.ToString("F1") + " m")}{(clear ? "" : " (NO clear way: the one with the most room)")}";
            Debug.Log("[OpenZ] L16 shuttle blast: " + Status);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.zero; }

        /// <summary>The roll (deg, RollMin..RollMax) it lands in: the nearest to RollPrefer at which its lowest points (within SupportBand
        /// of the lowest) hold its middle with StableMargin to spare; none: the one with the most room. low = its middle's height over
        /// its lowest point in that roll.</summary>
        float PickRoll(Vector3[] rel, float sign, out float margin, out float low)
        {
            float best = RollPrefer, bestScore = float.MaxValue; margin = -9f; low = 2.5f;
            for (float a = RollMin; a <= RollMax + 0.01f; a += 2f)
            {
                var q = Quaternion.AngleAxis(sign * a, rollAxis);
                float minY = float.MaxValue;
                for (int i = 0; i < rel.Length; i++) { float y = (q * rel[i]).y; if (y < minY) minY = y; }
                support.Clear();
                for (int i = 0; i < rel.Length; i++) { var v = q * rel[i]; if (v.y < minY + SupportBand) support.Add(new Vector2(v.x, v.z)); }
                float mg = HullRoom(support);
                float score = Mathf.Abs(a - RollPrefer) + (mg >= StableMargin ? 0f : 1000f - mg * 100f);
                if (score < bestScore) { bestScore = score; best = a; margin = mg; low = -minY; }
            }
            return best;
        }

        /// <summary>How far the origin (the ship's middle, seen from above) lies inside the convex hull of the points (m; under 0 = outside,
        /// fewer than three points = never inside).</summary>
        static float HullRoom(List<Vector2> p)
        {
            if (p.Count == 0) return -9f;
            p.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var h = new List<Vector2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = h.Count;
                for (int k = 0; k < p.Count; k++)
                {
                    var c = pass == 0 ? p[k] : p[p.Count - 1 - k];
                    while (h.Count >= start + 2 && Cross(h[h.Count - 2], h[h.Count - 1], c) <= 0f) h.RemoveAt(h.Count - 1);
                    h.Add(c);
                }
                h.RemoveAt(h.Count - 1);
            }
            if (h.Count < 3)
            {   // a point or a line: it would tip over; the room is minus the distance of the middle from it
                float d = float.MaxValue;
                for (int i = 0; i < h.Count; i++) d = Mathf.Min(d, h.Count == 1 ? h[0].magnitude : PtSeg(Vector2.zero, h[i], h[(i + 1) % h.Count]));
                return -Mathf.Max(0.01f, d == float.MaxValue ? 9f : d);
            }
            float room = float.MaxValue;
            for (int i = 0; i < h.Count; i++)
            {
                Vector2 a = h[i], b = h[(i + 1) % h.Count], e = b - a; float len = e.magnitude; if (len < 1e-5f) continue;
                room = Mathf.Min(room, (e.x * (0f - a.y) - e.y * (0f - a.x)) / len);                         // counter-clockwise hull: inside = positive
            }
            return room;
        }

        static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        static float PtSeg(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a; float l = ab.sqrMagnitude;
            float k = l > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l) : 0f;
            return (p - (a + ab * k)).magnitude;
        }

        static float SegSeg(Vector2 p0, Vector2 p1, Vector2 q0, Vector2 q1)
        {
            float d = Mathf.Min(Mathf.Min(PtSeg(p0, q0, q1), PtSeg(p1, q0, q1)), Mathf.Min(PtSeg(q0, p0, p1), PtSeg(q1, p0, p1)));
            Vector2 r = p1 - p0, s = q1 - q0; float den = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(den) > 1e-6f)
            {
                Vector2 w = q0 - p0; float u = (w.x * s.y - w.y * s.x) / den, v = (w.x * r.y - w.y * r.x) / den;
                if (u >= 0f && u <= 1f && v >= 0f && v <= 1f) return 0f;
            }
            return d;
        }

        static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);

        /// <summary>The way it flies: straight away from the blast, else turned by the least angle and shortened until its way (flight +
        /// skid) keeps clear of the hero campfires and the endings' set / camera, lands on ground (no open water, no standing building, no
        /// climb over RiseMost). camp / scene = the room left (m; 999 = none of them on this level).</summary>
        Vector3 PickWay(Vector3 away, Vector3 from, out float dev, out float travel, out float campRoom, out float sceneRoom, out bool clear)
        {
            keepFires.Clear();
            foreach (var f in FindObjectsByType<OzHeroCampfire>(FindObjectsSortMode.None)) if (f && f.gameObject.activeInHierarchy) keepFires.Add(f.transform.position);
            spotCount = OzL16LoungerSet.SceneSpots(blast, spots);
            Vector3 bestDir = away; float bestScore = float.MinValue; dev = 0f; travel = TravelMin; campRoom = sceneRoom = 999f; clear = false;
            for (int i = 0; ; i++)
            {
                float dv = i == 0 ? 0f : ((i + 1) / 2) * DeviateStep * (i % 2 == 1 ? 1f : -1f);
                if (Mathf.Abs(dv) > DeviateMost + 0.01f) break;
                var dir = Quaternion.AngleAxis(dv, Vector3.up) * away;
                for (float tr = Travel; tr >= TravelMin - 0.01f; tr -= 1.5f)
                {
                    var end = from + dir * (tr + SkidGuess);
                    Room(from, end, out float cr, out float sr);
                    bool ground = GroundOk(from, end, out _);
                    float worst = Mathf.Min(cr, sr);
                    if (worst >= 0f && ground) { dev = dv; travel = tr; campRoom = cr; sceneRoom = sr; clear = true; return dir; }
                    float score = (ground ? 0f : -100f) + Mathf.Min(worst, 50f) - Mathf.Abs(dv) * 0.01f;
                    if (score > bestScore) { bestScore = score; bestDir = dir; dev = dv; travel = tr; campRoom = cr; sceneRoom = sr; }
                }
            }
            return bestDir;
        }

        /// <summary>The room (m, + = clear) of the way [a, b] to the campfires and to the endings' set, its camera spots and the lines
        /// between them (999 = none there).</summary>
        static void Room(Vector3 a, Vector3 b, out float camp, out float scene)
        {
            camp = scene = 999f;
            Vector2 p0 = XZ(a), p1 = XZ(b);
            foreach (var f in keepFires) camp = Mathf.Min(camp, PtSeg(XZ(f), p0, p1) - (ShipRadius + CampClear));
            for (int i = 0; i < spotCount; i++) scene = Mathf.Min(scene, SegSeg(p0, p1, XZ(spots[0]), XZ(spots[i])) - (ShipRadius + SceneClear));
            var cam = ZodBriefing.EndingHolding ? Camera.main : null;                                       // the scene's own camera where it stands now
            if (cam) scene = Mathf.Min(scene, PtSeg(XZ(cam.transform.position), p0, p1) - (ShipRadius + SceneClear));
        }

        /// <summary>Ground under the way: no open water and no standing building where it ends, no climb over RiseMost along it.</summary>
        static bool GroundOk(Vector3 a, Vector3 b, out string why)
        {
            why = ""; var g = ZGame.I; if (!g) { why = "no game"; return false; }
            float y0 = g.GroundY(a.x, a.z);
            for (int k = 1; k <= 6; k++)
            {
                var p = Vector3.Lerp(a, b, k / 6f); float y = g.GroundY(p.x, p.z);
                if (y > y0 + RiseMost) { why = "a slope over " + RiseMost.ToString("F1") + " m"; return false; }
                if (InBuilding(p, k == 6 ? ShipRadius : ShipRadius * 0.5f)) { why = "a standing building"; return false; }
            }
            float ye = g.GroundY(b.x, b.z);
            if (OzWaterLanding.OpenWaterAt(new Vector3(b.x, ye, b.z), ye, out _)) { why = "open water"; return false; }
            return true;
        }

        /// <summary>True when the point lies inside a standing building's square of the rules, widened by `margin` m (bridges excluded).</summary>
        static bool InBuilding(Vector3 p, float margin)
        {
            var g = ZGame.I; if (!g || g.World == null) return false;
            float px = p.x * 8f, py = -p.z * 8f, m = margin * 8f;                                              // 1 m = 8 px of the rules
            foreach (var o in g.World.Objects)
            {
                if (o == null || o.Kind != ZKind.Building || o.Destroyed || o.IsBridge) continue;
                if (px > o.LocX - m && px < o.LocX + o.WidthPix + m && py > o.LocY - m && py < o.LocY + o.HeightPix + m) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------------------------------------ the flight
        void Fly()
        {
            var g = ZGame.I;
            float dt = g && g.paused ? 0f : Mathf.Min(Time.deltaTime, 0.05f);
            t += dt; float k = Mathf.Clamp01(t / Mathf.Max(0.05f, flight));
            Vector3 com = Vector3.Lerp(comFrom, comTo, k); com.y += 4f * apex * k * (1f - k);
            var r = Quaternion.AngleAxis(yaw * k, Vector3.up) * Quaternion.AngleAxis(roll * k, rollAxis) * rot0;
            transform.SetPositionAndRotation(com - r * offRel, r);
            if (k < 1f)
            {   // a corner swinging down never cuts into the ground on the way
                float under = Lowest(g);
                if (under < LastLowestInFlight) LastLowestInFlight = under;
                if (under < 0.02f) { transform.position += Vector3.up * (0.02f - under); LastLiftFrames++; }
                return;
            }
            Land(com);
        }

        /// <summary>The smallest height of the ship's outline over the ground under it (m; the points near the lowest are tested).</summary>
        float Lowest(ZGame g)
        {
            if (!g || pts.Count == 0) return 9f;
            var m = transform.localToWorldMatrix; float minY = float.MaxValue;
            for (int i = 0; i < pts.Count; i++) { float y = m.MultiplyPoint3x4(pts[i]).y; if (y < minY) minY = y; }
            float best = float.MaxValue; int tested = 0;
            for (int i = 0; i < pts.Count && tested < 32; i++)
            {
                var q = m.MultiplyPoint3x4(pts[i]); if (q.y > minY + 0.8f) continue;
                tested++; float e = q.y - g.GroundY(q.x, q.z); if (e < best) best = e;
            }
            return best == float.MaxValue ? 9f : best;
        }

        void Land(Vector3 com)
        {
            step = Step.Down; Landed++; LastLandAt = Time.time;
            var g = ZGame.I; float gy = g ? g.GroundY(com.x, com.z) : com.y;
            if (OzWaterLanding.OpenWaterAt(new Vector3(com.x, gy, com.z), gy, out _))
            {   // (never planned: the way keeps off open water) it stays where it came down
                Sound("dst_debris_splash", com, 1f, 0.8f);
                Status += "; it came down over open water";
                return;
            }
            OzHeavyLanding.FortGun(transform, com, comFrom, comTo, apex, flight, dp ? dp.SimId : -1);       // dust ring, puffs, chips, the heavy impact, bounce, skid, rock, on its real lowest point
            Sound("dst_fortgun_break_metal", com, 1f, 1f);                                                 // the heavy metal crash
            Sound("dst_debris_metal", com, 0.9f, 0.7f);
            if (FxManager.I) FxManager.I.Breakup(com, "metal", 4f, 14, 1.3f);
            knockAt = Time.time + 0.3f; knocked = false;
            smokeUntil = Time.time + SmokeSeconds; nextSmoke = 0f;
            Sound("dst_fire_ignite", com, 0.9f, 1f);
            Status += $"; landed {Time.time - LastLaunchAt:F2} s after the launch at ({com.x:F1}, {com.z:F1})";
            Debug.Log("[OpenZ] L16 shuttle blast: landed at " + com.ToString("F1") + ", " + (Time.time - LastLaunchAt).ToString("F2") + " s after the launch");
        }

        void Burn()
        {
            var at = transform.TransformPoint(comLocal);
            if (!knocked && Time.time >= knockAt) { knocked = true; Sound("dst_debris_metal", at, 0.7f, 0.75f); }   // the bounce comes down
            if (ship && ship.lights && ship.lights.Mode != 2) ship.lights.SetMode(2);                      // a later hit of the rules does not light it again
            if (ship && ship.exhaust && ship.exhaust.Mode != 2) ship.exhaust.SetMode(2);
            var g = ZGame.I; if (g && g.paused) return;
            if (Time.time < smokeUntil && Time.time >= nextSmoke)
            {
                nextSmoke = Time.time + 0.25f;
                var fx = OpenZFxPlayer.I; if (fx) fx.WreckSmoke(at + Vector3.up * 1.2f, 3.2f, true);
            }
        }

        void Sound(string ev, Vector3 at, float vol, float pitch)
        {
            bool has = AudioDirector.Has(ev);
            bool ok = has && AudioDirector.I && AudioDirector.I.Play(ev, at, dp ? dp.SimId : -1, vol, pitch);
            SoundsSent++; if (ok) SoundsStarted++;
            LastSounds += (LastSounds.Length > 0 ? ", " : "") + ev + (ok ? "" : has ? " (not started)" : " (missing)");
            OzSoundLog.Add("L16", "shuttle_blast_" + ev, ev, ok ? "sent, started" : has ? "sent, not started" : "missing event", vol, at);
        }

        /// <summary>Play check: the shuttle's blast throw in one line.</summary>
        public static string Report() =>
            $"shuttle blast throw: {(Enabled ? "on" : "OFF")}, reach {Reach:F0} m; {Status}; thrown {Thrown}, landed {Landed}, outline points {LastPoints}, "
            + $"lowest point in flight {(LastLowestInFlight > 8f ? "-" : LastLowestInFlight.ToString("F2") + " m")} (frames lifted off the ground {LastLiftFrames:F0}), from {LastFrom.ToString("F1")} to {LastTo.ToString("F1")}, "
            + $"launch at {LastLaunchAt:F2} s, landing at {LastLandAt:F2} s game time; sounds started {SoundsStarted} of {SoundsSent} ({LastSounds}); "
            + $"heavy landing: {OzHeavyLanding.LastKind}, fit {OzHeavyLanding.LastFit}, gap {OzHeavyLanding.LastGap:F3} m, skid {OzHeavyLanding.LastSkid:F2} m";
    }
}
