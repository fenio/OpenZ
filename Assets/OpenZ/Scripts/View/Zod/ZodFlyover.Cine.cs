// OpenZ (GPL-3.0)
// The level-start briefing camera is a SHOT LIST now:
//  - ZodShotList: one small data class per level (L02, L17; any other level uses the L02 list, every pose is relative to the map).
//    A shot = subject + start / end framing (heading, pitch, distance or height), fov, nominal seconds, ease, and how it is cut in.
//  - ZodFlyover resolves the list on the map at Begin (the map rect, both forts, the DayNight sun at the start time, the RTS start
//    camera) into a start pose and an end pose per shot, and PlanCine fits it to Zod's transmission: the nominal lengths are scaled to
//    the briefing length (GENERAL joke + line + tail), then every cut snaps to the nearest line break (a subtitle cue start, CutLead s
//    before his next sentence) within CutSnap s.
//  - Inside a shot there is ONE slow eased move (a push-in, a crane, an orbit of at most ~12 deg); shots are joined by hard cuts or a
//    short dip to black (under Zod's window and the letterbox), never by a pan across the map.
//  - The last shot eases into the RTS start camera exactly, so the hand-back has no jump; a skip (or a watchdog end) dips to black and
//    cuts to it. The painted flyby sky (FlybySky) is wanted until the last shot starts, so it has faded and unloads as before.
//  - The json beats' "shots" are ignored while the list flies. UseShotList = false brings the old orbit flight back.
// Play checks: ZodFlyover.Status(), ZodFlyover.LastPlan, HoldShot / HoldU (freeze one shot for a frame), ZodCinePreview (camera only).
using System;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>What a briefing shot looks at. WholeMap: the map centre, framed so the whole map fills the frame between the letterbox
    /// bars (dist = a multiple of that fit distance, yaw = absolute heading). Sunrise: a low camera 6 m inside the map edge looking
    /// across `point` (0,0 = the map centre) towards the sun (yaw = offset from the sun heading, which already keeps the sun
    /// LowSunOffsetDeg right of centre; pitch = degrees UNDER the horizon; dist = camera height in m, at least 28.5 m over the ground).
    /// EnemyFort: an orbit around the enemy fort (yaw = offset from the heading map centre -> fort, so the camera stays over the map).
    /// OwnBaseHome: starts on an orbit around the player's fort (yaw = offset from the RTS heading) and ends EXACTLY on the RTS start
    /// camera (its B values are not used). Point: an orbit around `point` (x, z) with absolute yaw.</summary>
    /// (world metres; the film-frame shots: foreground parallax, the car park glide, the low sunrise), floor = its own lowest height over
    /// the ground (m; below 0 = the default CineFloor).
    public enum ZodCineSubject { WholeMap, Sunrise, EnemyFort, OwnBaseHome, Point, Free }

    /// <summary>Soft = half linear, half smoothstep (moving at the cut, slower at the ends); InOut = smoothstep (starts and lands at
    /// rest: the hand-back shot); Linear.</summary>
    public enum ZodCineEase { Soft, InOut, Linear }

    /// <summary>One shot of a level's briefing shot list (plain data; tune in Play, e.g. ZodShotList.L02.shots[2].pitchB = 15).</summary>
    [Serializable]
    public sealed class ZodCineShotDef
    {
        public string name;
        public ZodCineSubject subject;
        /// <summary>Nominal length (s). The list is scaled to the briefing, then the cuts snap to Zod's line breaks.</summary>
        public float seconds = 5f;
        /// <summary>Heading (deg) at the start / end of the shot (absolute or an offset, see ZodCineSubject).</summary>
        public float yawA, yawB;
        /// <summary>Camera pitch (deg down) at the start / end.</summary>
        public float pitchA = 40f, pitchB = 40f;
        /// <summary>Distance to the subject (m), the fit multiple (WholeMap) or the camera height (Sunrise) at the start / end.</summary>
        public float distA = 1f, distB = 1f;
        /// <summary>Vertical field of view (deg) at the start / end (the game camera is 32).</summary>
        public float fovA = 32f, fovB = 32f;
        /// <summary>The view turns this many degrees left of the subject, so it sits right of centre (Zod's window is top-left).</summary>
        public float frameYaw;
        /// <summary>Forts: the aim point this many m over the fort's ground centre.</summary>
        public float aimUp = 4f;
        /// <summary>Sunrise: the camera dollies this many m forward over the shot.</summary>
        public float slide;
        /// <summary>Sunrise: the map point (x, z) the low camera looks across ((0,0) = the map centre). Point: the subject.</summary>
        public Vector2 point;
        /// <summary>Length (s) of a dip to black centred on the cut INTO this shot; 0 = a hard cut.</summary>
        public float dip;
        /// <summary>The soft wind swell (ter_flyby_dip_whoosh) rises into the cut into this shot.</summary>
        public bool whoosh;
        public ZodCineEase ease = ZodCineEase.Soft;
        /// <summary>Free: camera position and look point (world m) at the start / end of the shot.</summary>
        public Vector3 camA, camB, lookA, lookB;
        /// <summary>Lowest camera height over the ground for this shot (m); below 0 = the default CineFloor (8 m inside the map).</summary>
        public float floor = -1f;
    }

    /// <summary>
    /// rebuild): the level start time of day, the city's warm sunrise, the briefing grade and both shot lists. A missing or empty part keeps
    /// the code defaults.</summary>
    [Serializable]
    public sealed class ZodBriefingLookFile
    {
        /// <summary>DayNight time of day every level starts at (below 0 = DayNight.SunriseStart's default).</summary>
        public float startTod = -1f;
        /// <summary>DayNight.MorningWarmth (below 0 = keep the code default).</summary>
        public float morningWarmth = -1f;
        /// <summary>ZodFlyover.BriefExposure (EV) and BriefTemperature (white balance) while the briefing shots fly (eased out over the last shot).</summary>
        public float briefExposure, briefTemperature;
        /// <summary>DayNight.MorningSunLift / MorningAmbientLift (below 0 = keep the code defaults).</summary>
        public float morningSunLift = -1f, morningAmbientLift = -1f;
        public ZodCineShotDef[] L02, L17;
    }

    /// <summary>The briefing shot list of one level (data). Shot order: the whole map, the sunrise, the enemy
    /// fort, then our base easing into the RTS start camera (the hand-back).</summary>
    public sealed class ZodShotList
    {
        public string level;
        public ZodCineShotDef[] shots;

        /// <summary>L02 desert, 144 x 180 m. Enemy (blue) fort top-left (28, -14), ours (red) bottom (96, -144), the river and its
        /// bridges across the middle, mesas in the centre. Sun at the 0.915 start: ESE (heading ~109 deg), 9 deg up (drawn at 1.4).</summary>
        public static ZodShotList L02 = new ZodShotList
        {
            level = "L02",
            shots = new[]
            {
                // 1. the whole map from the east, high, front-lit by the sunrise behind the camera: a slow push and a 6 deg drift
                //    sits ~40 % closer (less haze, real perspective over the mesas)
                new ZodCineShotDef { name = "whole_map", subject = ZodCineSubject.WholeMap, seconds = 5f, yawA = -84f, yawB = -78f,
                    pitchA = 40f, pitchB = 40f, distA = 1.05f, distB = 0.94f, fovA = 50f, fovB = 50f, frameYaw = 2f },
                // 2. low over the mesas into the low sun (bloom, painted dawn sky): a slow crane up, a short dip + wind swell into it
                new ZodCineShotDef { name = "sunrise", subject = ZodCineSubject.Sunrise, seconds = 5f, yawA = 0f, yawB = -2f,
                    pitchA = 2f, pitchB = 5.5f, distA = 30f, distB = 52f, slide = 10f, fovA = 30f, fovB = 30f, dip = 0.35f, whoosh = true },
                // 3. the enemy fort from a low three-quarter angle across the river: a slow push-in with a 12 deg orbit
                new ZodCineShotDef { name = "enemy_fort", subject = ZodCineSubject.EnemyFort, seconds = 5.2f, yawA = 34f, yawB = 22f,
                    pitchA = 18f, pitchB = 14f, distA = 96f, distB = 74f, fovA = 28f, fovB = 28f, frameYaw = 5f, aimUp = 4f },
                // 4. our fort from behind, craning up into the RTS start camera (lands at rest: no jump at the hand-back)
                new ZodCineShotDef { name = "our_base_home", subject = ZodCineSubject.OwnBaseHome, seconds = 5.3f, yawA = 16f,
                    pitchA = 32f, distA = 76f, fovA = 30f, aimUp = 3f, ease = ZodCineEase.InOut },
            }
        };

        /// <summary>L17 city, 220 x 256 m. Enemy (blue) fort top-left (38, -40), ours (red) right (162, -72), the canal down the
        /// middle, the harbour water across the south (centre ~ (112, -209)). Same sunrise sun as L02 (ESE).</summary>
        public static ZodShotList L17 = new ZodShotList
        {
            level = "L17",
            shots = new[]
            {
                // 1. the whole city from the east (the sun behind the camera): a slow push and a 6 deg drift. fov 40: the city fits at
                //    ~375 m (at fov 32 it needs ~450 m, ~42% into the 220-760 m linear fog at the centre, ~60% at the far corners)
                new ZodCineShotDef { name = "whole_city", subject = ZodCineSubject.WholeMap, seconds = 5f, yawA = -86f, yawB = -80f,
                    pitchA = 40f, pitchB = 40f, distA = 1.05f, distB = 0.94f, fovA = 50f, fovB = 50f, frameYaw = 2f },
                // 2. the dawn sky over the harbour: low from the west edge across the harbour water into the sun, a slow crane up
                new ZodCineShotDef { name = "harbour_dawn", subject = ZodCineSubject.Sunrise, seconds = 5f, point = new Vector2(112f, -209f),
                    yawA = 0f, yawB = -2f, pitchA = 2f, pitchB = 5.5f, distA = 32f, distB = 54f, slide = 10f, fovA = 30f, fovB = 30f,
                    dip = 0.35f, whoosh = true },
                // 3. the enemy fort from a low three-quarter angle over the streets: a slow push-in with a 12 deg orbit
                new ZodCineShotDef { name = "enemy_fort", subject = ZodCineSubject.EnemyFort, seconds = 5.2f, yawA = 34f, yawB = 22f,
                    pitchA = 20f, pitchB = 16f, distA = 100f, distB = 78f, fovA = 28f, fovB = 28f, frameYaw = 5f, aimUp = 4f },
                // 4. our fort, craning up into the RTS start camera
                new ZodCineShotDef { name = "our_base_home", subject = ZodCineSubject.OwnBaseHome, seconds = 5.3f, yawA = 16f,
                    pitchA = 32f, distA = 76f, fovA = 30f, aimUp = 3f, ease = ZodCineEase.InOut },
            }
        };

        public static ZodShotList For(string level) { EnsureFile(); return level == "L17" ? L17 : L02; }

        // ---------------------------------------------------------------------------------------- the tuned data file
        public const string JsonPath = "OpenZ/Zod/briefing_shots";
        static bool fileTried;
        /// <summary>The loaded data file (null = none); its shot lists replace L02 / L17 once per session (Play tuning edits those).</summary>
        public static ZodBriefingLookFile File { get; private set; }

        /// <summary>Loads Resources/OpenZ/Zod/briefing_shots.json once (ZGame.Awake and the first For): the start time, the morning warmth,
        /// the briefing grade and the shot lists. force = read it again (Play tuning after the json changed).</summary>
        public static void EnsureFile(bool force = false)
        {
            if (fileTried && !force) return;
            fileTried = true;
            var ta = Resources.Load<TextAsset>(JsonPath);
            if (!ta) return;
            try
            {
                var f = JsonUtility.FromJson<ZodBriefingLookFile>(ta.text);
                if (f == null) return;
                File = f;
                if (f.L02 != null && f.L02.Length > 0) L02.shots = f.L02;
                if (f.L17 != null && f.L17.Length > 0) L17.shots = f.L17;
                if (f.startTod >= 0f) DayNight.SunriseStart = f.startTod;
                if (f.morningWarmth >= 0f) DayNight.MorningWarmth = f.morningWarmth;
                ZodFlyover.BriefExposure = f.briefExposure; ZodFlyover.BriefTemperature = f.briefTemperature;
                if (f.morningSunLift >= 0f) DayNight.MorningSunLift = f.morningSunLift;
                if (f.morningAmbientLift >= 0f) DayNight.MorningAmbientLift = f.morningAmbientLift;
                Debug.Log($"[ZodFlyover] briefing data {JsonPath}.json: start t {DayNight.SunriseStart:F3}, morning warmth {DayNight.MorningWarmth:F2}, grade {f.briefExposure:+0.00;-0.00} EV / {f.briefTemperature:+0;-0}, shots L02 {L02.shots.Length} L17 {L17.shots.Length}");
            }
            catch (Exception e) { Debug.LogWarning($"[ZodFlyover] {JsonPath}.json unreadable, the code shot lists fly: {e.Message}"); }
        }

        /// <summary>Play tuning: the live data as json (write it to Assets/OpenZ/Resources/OpenZ/Zod/briefing_shots.json).</summary>
        public static string ToJson()
        {
            var f = new ZodBriefingLookFile { startTod = DayNight.SunriseStart, morningWarmth = DayNight.MorningWarmth,
                briefExposure = ZodFlyover.BriefExposure, briefTemperature = ZodFlyover.BriefTemperature,
                morningSunLift = DayNight.MorningSunLift, morningAmbientLift = DayNight.MorningAmbientLift, L02 = L02.shots, L17 = L17.shots };
            return JsonUtility.ToJson(f, true);
        }

        public float NominalSeconds { get { float s = 0f; if (shots != null) foreach (var d in shots) if (d != null) s += Mathf.Max(0.5f, d.seconds); return s; } }
    }

    public sealed partial class ZodFlyover
    {
        /// <summary>The shot list flies the briefing (false = the old orbit flight driven by the json beats).</summary>
        public static bool UseShotList = true;
        /// <summary>A cut snaps to a line break within CutSnap s, CutLead s before Zod's next sentence, if both shots keep MinShot s.</summary>
        public static float CutSnap = 1.2f, CutLead = 0.15f, MinShot = 2.6f;
        /// <summary>The camera clock follows Zod's line clock smoothly: correction rate (1/s), most lead (s), a gap that snaps (s).</summary>
        public static float ClockEase = 4f, ClockLead = 0.05f, ClockJump = 0.3f;
        /// <summary>Skip hand-back: dip to black (s), cut to the RTS camera, back from black (s). HandEase: an early normal end eases home (s).</summary>
        public static float SkipDipOut = 0.15f, SkipDipIn = 0.22f, HandEase = 0.8f;
        /// <summary>WholeMap fit: the map corners inside these NDC bounds (the letterbox: 64 u top, 112 u bottom of 1080).</summary>
        public static float FitX = 0.98f, FitTop = 0.86f, FitBottom = -0.78f;
        /// <summary>The dip overlay's canvas order: under Zod's window and the letterbox (the briefing comms canvas is 103).</summary>
        public static int DipSorting = 100;
        /// <summary>Frame check: freeze the camera on shot HoldShot (0-based) at HoldU of its move (-1 = play).</summary>
        public static int HoldShot = -1; public static float HoldU = 0.5f;
        /// <summary>The last fitted plan (shot, start, length, poses) for the Play check.</summary>
        public static string LastPlan = "no shot list yet";
        /// <summary>
        /// shot so the hand-back lands on the game's own look. 0 / 0 = no grade volume.</summary>
        public static float BriefExposure, BriefTemperature;

        struct CinePose { public Vector3 pos, look, focus; public float fov; }
        sealed class CineShot { public string name; public CinePose a, b; public float nominal, start, dur, dip, floor = -1f; public ZodCineEase ease; public bool whoosh, home; }

        List<CineShot> cine; float cineT, homeFov = 32f, dipA, handT = -1f, handStartA; int cineIdx = -1, whooshIdx;
        Func<float> cineClock; bool cineHanding, handDip; CinePose handFrom, homePose; string cineLevel;
        GameObject dipGo; Image dipImg;

        /// <summary>The shot list owns the camera (set at Begin).</summary>
        public bool Cine => cine != null;
        /// <summary>Seconds on the briefing clock (0 = the briefing start).</summary>
        public float CineTime => cineT;
        /// <summary>Index of the shot on screen (-1 = none).</summary>
        public int CineShotIndex => cineIdx;
        /// <summary>Fitted shots: name, start (s), length (s).</summary>
        public IEnumerable<(string name, float start, float dur)> CineShots { get { if (cine != null) foreach (var s in cine) yield return (s.name, s.start, s.dur); } }
        string CineCurrent => cine == null ? null : cineHanding ? "home (hand-back)" : cineIdx >= 0 && cineIdx < cine.Count ? cine[cineIdx].name : "-";
        bool CineSkyWanted => cine != null && !cineHanding && cineIdx < cine.Count - 1;
        bool CineHomeBusy => cine != null && cineHanding && handT >= 0f;

        // ------------------------------------------------------------------------------------------------ begin / plan
        /// <summary>Begin: builds the level's shot list and puts the camera on the first frame. False = the orbit flight flies.</summary>
        bool CineBegin()
        {
            cine = null; cineHanding = false; handT = -1f; cineIdx = -1; whooshIdx = 0; cineT = 0f; cineClock = null;
            if (!UseShotList) return false;
            try
            {
                var list = ZodShotList.For(game.level);
                cineLevel = list.level;
                cine = BuildCine(list);
            }
            catch (Exception e) { Debug.LogWarning("[ZodFlyover] shot list failed, the orbit flight flies instead: " + e.Message); cine = null; }
            if (cine == null || cine.Count == 0) { cine = null; return false; }
            Fit(Nominal(), null);                                                                     // nominal timing until PlanCine
            CineApply(0f);
            return true;
        }

        /// <summary>ZodBriefing, after the line started: fits the shots to the briefing (preroll = GENERAL joke + window open, end = the
        /// line + tail) with the cuts on Zod's line breaks, and follows his clock (NaN = keep counting real time).</summary>
        public void PlanCine(float preroll, float end, ZodLine line, Func<float> clock)
        {
            if (cine == null) return;
            cineClock = clock;
            var breaks = new List<float>();
            if (line?.cues != null) foreach (var c in line.cues) if (c != null) breaks.Add(preroll + c.start - CutLead);
            Fit(Mathf.Max(4f, preroll + end), breaks);
            Debug.Log($"[ZodFlyover] shot list {cineLevel} ({game.level}): {LastPlan}");
        }

        float Nominal() { float s = 0f; foreach (var c in cine) s += c.nominal; return Mathf.Max(0.5f, s); }

        void Fit(float total, List<float> breaks)
        {
            int n = cine.Count; float k = total / Nominal(), acc = 0f;
            var cuts = new float[n + 1];
            for (int i = 0; i < n; i++) { acc += cine[i].nominal * k; cuts[i + 1] = acc; }
            cuts[0] = 0f; cuts[n] = total;
            if (breaks != null)
                for (int i = 1; i < n; i++)
                {
                    float best = cuts[i], bd = CutSnap;
                    foreach (var b in breaks)
                    {
                        float d = Mathf.Abs(b - cuts[i]);
                        if (d < bd && b - cuts[i - 1] >= MinShot && cuts[i + 1] - b >= MinShot) { bd = d; best = b; }
                    }
                    cuts[i] = best;
                }
            var sb = new StringBuilder();
            sb.Append($"{total:F2} s, {n} shots:");
            for (int i = 0; i < n; i++)
            {
                var s = cine[i]; s.start = cuts[i]; s.dur = Mathf.Max(0.05f, cuts[i + 1] - cuts[i]);
                sb.Append($" | {i + 1} {s.name} {s.start:F2}+{s.dur:F2}{(s.dip > 0f ? " dip" : " cut")} A{V(s.a.pos)}->B{V(s.b.pos)} fov {s.a.fov:F0}->{s.b.fov:F0}");
            }
            LastPlan = sb.ToString();
        }

        static string V(Vector3 v) => $"({v.x:F0},{v.y:F0},{v.z:F0})";

        // ------------------------------------------------------------------------------------------------ shots from the map
        List<CineShot> BuildCine(ZodShotList list)
        {
            if (list?.shots == null) return null;
            var me = game.player; var enemy = me == Team.Red ? Team.Blue : Team.Red;
            Vector2 size = rts.mapSizeMeters; mapSize = size;
            Vector3 centre = new Vector3(size.x * 0.5f, 0f, -size.y * 0.5f);
            Vector3 myFort = FortCentre(me, centre), theirFort = FortCentre(enemy, centre);
            if (!camC) camC = camT.GetComponent<Camera>();
            homeFov = camC ? camC.fieldOfView : 32f;
            float aspect = camC && camC.aspect > 0.1f ? camC.aspect : 16f / 9f;
            float homeDist = homeHeight / Mathf.Max(0.1f, Mathf.Sin(rts.pitch * Mathf.Deg2Rad));
            homePose = new CinePose { pos = homePos, look = homePos + homeRot * Vector3.forward * homeDist, focus = homeFocus, fov = homeFov };
            float homeYaw = homeRot.eulerAngles.y;
            Vector3 sun = SunFlat(); float sunYaw = Mathf.Atan2(sun.x, sun.z) * Mathf.Rad2Deg;
            var outList = new List<CineShot>();
            foreach (var d in list.shots)
            {
                if (d == null) continue;
                var s = new CineShot { name = string.IsNullOrEmpty(d.name) ? d.subject.ToString() : d.name, nominal = Mathf.Max(0.5f, d.seconds),
                                       dip = Mathf.Max(0f, d.dip), ease = d.ease, whoosh = d.whoosh, floor = d.floor };
                switch (d.subject)
                {
                    case ZodCineSubject.WholeMap:
                        s.a = Orbit(centre, d.yawA, d.pitchA, d.distA * FitDistance(centre, d.yawA, d.pitchA, d.fovA, aspect, size), d.fovA, d.frameYaw, centre);
                        s.b = Orbit(centre, d.yawB, d.pitchB, d.distB * FitDistance(centre, d.yawB, d.pitchB, d.fovB, aspect, size), d.fovB, d.frameYaw, centre);
                        break;
                    case ZodCineSubject.Sunrise:
                        {
                            Vector3 through = d.point == Vector2.zero ? centre : new Vector3(d.point.x, 0f, d.point.y);
                            float y0 = sunYaw - LowSunOffsetDeg;
                            s.a = Low(through, y0 + d.yawA, d.pitchA, d.distA, 0f, d.fovA, size);
                            s.b = Low(through, y0 + d.yawB, d.pitchB, d.distB, d.slide, d.fovB, size);
                            break;
                        }
                    case ZodCineSubject.EnemyFort:
                    case ZodCineSubject.Point:
                        {
                            bool fort = d.subject == ZodCineSubject.EnemyFort;
                            Vector3 subj = fort ? theirFort : new Vector3(d.point.x, 0f, d.point.y);
                            Vector3 rel = subj - centre; rel.y = 0f;
                            float baseYaw = !fort ? 0f : rel.sqrMagnitude > 1f ? Mathf.Atan2(rel.x, rel.z) * Mathf.Rad2Deg : homeYaw;
                            Vector3 aim = new Vector3(subj.x, GroundAt(subj) + d.aimUp, subj.z);
                            // the camera stays over the map: if the start or end camera of this side is outside (8 m margin), the mirrored side
                            float side = 1f;
                            if (fort && !(Inside(Orbit(aim, baseYaw + d.yawA, d.pitchA, d.distA, 0f, 0f, subj).pos, 8f) && Inside(Orbit(aim, baseYaw + d.yawB, d.pitchB, d.distB, 0f, 0f, subj).pos, 8f))
                                && Inside(Orbit(aim, baseYaw - d.yawA, d.pitchA, d.distA, 0f, 0f, subj).pos, 8f) && Inside(Orbit(aim, baseYaw - d.yawB, d.pitchB, d.distB, 0f, 0f, subj).pos, 8f))
                                side = -1f;
                            s.a = Orbit(aim, baseYaw + side * d.yawA, d.pitchA, d.distA, d.fovA, d.frameYaw, subj);
                            s.b = Orbit(aim, baseYaw + side * d.yawB, d.pitchB, d.distB, d.fovB, d.frameYaw, subj);
                            break;
                        }
                    case ZodCineSubject.Free:
                        s.a = new CinePose { pos = d.camA, look = d.lookA, focus = new Vector3(d.lookA.x, 0f, d.lookA.z), fov = d.fovA };
                        s.b = new CinePose { pos = d.camB, look = d.lookB, focus = new Vector3(d.lookB.x, 0f, d.lookB.z), fov = d.fovB };
                        break;
                    case ZodCineSubject.OwnBaseHome:
                        {
                            Vector3 aim = new Vector3(myFort.x, GroundAt(myFort) + d.aimUp, myFort.z);
                            s.a = Orbit(aim, homeYaw + d.yawA, d.pitchA, d.distA, d.fovA, d.frameYaw, myFort);
                            s.b = homePose; s.home = true;
                            break;
                        }
                }
                outList.Add(s);
            }
            // the hand-back must end on the RTS start camera: if the list has no home shot, the last shot becomes one (it eases home)
            if (outList.Count > 0 && !outList[outList.Count - 1].home) { var last = outList[outList.Count - 1]; last.b = homePose; last.home = true; last.ease = ZodCineEase.InOut; }
            return outList;
        }

        static CinePose Orbit(Vector3 aim, float yaw, float pitch, float dist, float fov, float frameYaw, Vector3 focus)
        {
            var pos = aim - Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward * dist;
            var look = Mathf.Abs(frameYaw) > 1e-3f ? pos + Quaternion.Euler(0f, -frameYaw, 0f) * (aim - pos) : aim;
            return new CinePose { pos = pos, look = look, focus = new Vector3(focus.x, 0f, focus.z), fov = fov };
        }

        /// <summary>The low sunrise camera (the approved horizon shot's placement): LowMargin m inside the map edge behind `through`,
        /// at `height` m (at least 28.5 m over the ground there: the cliff floor), `down` deg under the horizon, `slide` m dollied in.</summary>
        CinePose Low(Vector3 through, float yaw, float down, float height, float slide, float fov, Vector2 size)
        {
            var fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            float back = EdgeDistance(through, -fwd, size, LowMargin);
            Vector3 cam = through - fwd * back + fwd * slide; cam.y = 0f;
            float ahead = EdgeDistance(cam, fwd, size, LowMargin);
            float aimD = Mathf.Clamp(Mathf.Min(LowAim, ahead - 4f), 30f, LowAim);
            var pos = new Vector3(cam.x, Mathf.Max(height, GroundAt(cam) + 28.5f), cam.z);
            var look = pos + Quaternion.Euler(down, yaw, 0f) * Vector3.forward * 200f;
            return new CinePose { pos = pos, look = look, focus = cam + fwd * aimD, fov = fov };
        }

        /// <summary>The distance at which the whole map rect (y = 0) fits the frame between the letterbox bars (FitX / FitTop / FitBottom).</summary>
        static float FitDistance(Vector3 centre, float yaw, float pitch, float fov, float aspect, Vector2 size)
        {
            float tv = Mathf.Tan(Mathf.Clamp(fov, 5f, 120f) * 0.5f * Mathf.Deg2Rad), th = tv * aspect;
            var rot = Quaternion.Euler(Mathf.Clamp(pitch, 10f, 89f), yaw, 0f); var inv = Quaternion.Inverse(rot); var fwd = rot * Vector3.forward;
            var corners = new[] { new Vector3(0f, 0f, 0f), new Vector3(size.x, 0f, 0f), new Vector3(0f, 0f, -size.y), new Vector3(size.x, 0f, -size.y) };
            float lo = 20f, hi = 3000f;
            for (int it = 0; it < 36; it++)
            {
                float mid = 0.5f * (lo + hi); var pos = centre - fwd * mid; bool ok = true;
                foreach (var c in corners)
                {
                    var v = inv * (c - pos);
                    if (v.z <= 1f) { ok = false; break; }
                    float x = v.x / (v.z * th), y = v.y / (v.z * tv);
                    if (Mathf.Abs(x) > FitX || y > FitTop || y < FitBottom) { ok = false; break; }
                }
                if (ok) hi = mid; else lo = mid;
            }
            return hi;
        }

        bool Inside(Vector3 p, float margin) => p.x >= margin && p.x <= mapSize.x - margin && p.z <= -margin && p.z >= -mapSize.y + margin;

        /// <summary>The lowest camera height at p: 8 m over the ground well inside the map, rising to 28 m (the cliff floor) at the edge
        /// and outside it.</summary>
        float CineFloor(Vector3 p)
        {
            float inX = Mathf.Min(p.x, mapSize.x - p.x), inZ = Mathf.Min(-p.z, mapSize.y + p.z);
            float k = Mathf.Clamp01(Mathf.Min(inX, inZ) / 20f);
            return GroundAt(p) + Mathf.Lerp(28f, 8f, k);
        }

        // ------------------------------------------------------------------------------------------------ play
        void CineTick(float dt)
        {
            if (cineHanding) { HandTick(dt); return; }
            float c = cineClock != null ? cineClock() : float.NaN;
            if (float.IsNaN(c)) cineT += dt;
            else
            {   // Zod's voice clock (timeSamples) steps in mix buffers (1024 samples = 21 ms at 48 kHz): used directly, the camera would
                // stand still on about one 60 fps frame in five (judder). It runs on real time and eases towards his clock instead, never
                // more than ClockLead s ahead of it (a pause freezes it with him) and jumps to it on a gap over ClockJump s.
                float run = cineT + dt, err = c - run;
                cineT = Mathf.Abs(err) > ClockJump ? c : Mathf.Min(run + err * Mathf.Clamp01(dt * ClockEase), c + ClockLead);
            }
            CineApply(cineT);
        }

        void CineApply(float t)
        {
            bool hold = HoldShot >= 0 && HoldShot < cine.Count;
            int i = 0;
            while (i < cine.Count - 1 && t >= cine[i + 1].start) i++;
            if (hold) { i = HoldShot; t = cine[i].start + Mathf.Clamp01(HoldU) * cine[i].dur; }
            if (i != cineIdx) { cineIdx = i; Debug.Log($"[ZodFlyover] shot {i + 1}/{cine.Count} {cine[i].name} at {t:F2} s ({cine[i].dur:F1} s{(hold ? ", held" : "")})"); }
            if (!hold)
                while (whooshIdx < cine.Count && t >= cine[whooshIdx].start - cine[whooshIdx].dip * 0.5f - 0.1f)
                {   // the wind swell rises into the cut (dip) into a shot that asks for it
                    if (whooshIdx > 0 && cine[whooshIdx].whoosh) PlayDip();
                    whooshIdx++;
                }
            var s = cine[i];
            float u = Mathf.Clamp01((t - s.start) / Mathf.Max(0.05f, s.dur));
            Put(Eval(s, u), s.home && u >= 0.9999f, s.floor);
            BriefGrade(i == cine.Count - 1 ? 1f - CineEase(ZodCineEase.InOut, u) : 1f);
            float a = 0f;
            if (!hold)
                for (int k = 1; k < cine.Count; k++)
                {
                    float h = cine[k].dip * 0.5f;
                    if (h > 0f) a = Mathf.Max(a, 1f - Mathf.Abs(t - cine[k].start) / h);
                }
            SetDip(a);
        }

        static CinePose Eval(CineShot s, float u)
        {
            float e = CineEase(s.ease, u);
            return new CinePose
            {
                pos = Vector3.Lerp(s.a.pos, s.b.pos, e), look = Vector3.Lerp(s.a.look, s.b.look, e),
                focus = Vector3.Lerp(s.a.focus, s.b.focus, e), fov = Mathf.Lerp(s.a.fov, s.b.fov, e)
            };
        }

        static float CineEase(ZodCineEase ease, float u)
        {
            u = Mathf.Clamp01(u); float smooth = u * u * (3f - 2f * u);
            return ease == ZodCineEase.Linear ? u : ease == ZodCineEase.InOut ? smooth : 0.5f * (u + smooth);
        }

        void Put(CinePose p, bool exactHome, float floor = -1f)
        {
            if (!camT) return;
            if (exactHome) { camT.position = homePos; camT.rotation = homeRot; }
            else
            {
                var pos = p.pos; float fl = floor >= 0f ? GroundAt(pos) + floor : CineFloor(pos); if (pos.y < fl) pos.y = fl;
                camT.position = pos;
                var dir = p.look - pos; if (dir.sqrMagnitude > 1e-4f) camT.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }
            if (camC) camC.fieldOfView = exactHome ? homeFov : p.fov;
            if (rts)
            {   // the audio listener rig and the height-based LOD / footstep rules follow the shot (restored exactly on hand-back)
                rts.focus = new Vector3(p.focus.x, 0f, p.focus.z);
                rts.height = Mathf.Clamp(camT.position.y, rts.minHeight, rts.maxHeight);
            }
        }

        // ------------------------------------------------------------------------------------------------ hand-back
        /// <summary>ZodBriefing's hand-back. The last shot has eased into the RTS start camera: the last millimetres snap. After a skip,
        /// a watchdog end or an end before the last shot: a short dip to black and a cut to it. HomeError stays high until it is done.</summary>
        public void HandBack(bool skipped)
        {
            if (!Active) return;
            if (cine == null) { SetShot("home", skipped ? 0.22f : 0.55f); return; }
            if (cineHanding) return;
            cineHanding = true; HoldShot = -1;
            bool lastShot = cineIdx == cine.Count - 1;
            float err = camT ? Vector3.Distance(camT.position, homePos) + Quaternion.Angle(camT.rotation, homeRot) * 0.1f : 0f;
            handDip = skipped || !lastShot;
            handStartA = dipA; handT = 0f;
            handFrom = new CinePose { pos = camT ? camT.position : homePos, look = camT ? camT.position + camT.forward * 100f : homePose.look,
                                      focus = rts ? rts.focus : homeFocus, fov = camC ? camC.fieldOfView : homeFov };
            if (!handDip && err < 0.5f) { Put(homePose, true); SetDip(0f); handT = -1f; }
            Debug.Log($"[ZodFlyover] hand-back ({(skipped ? "skipped" : "complete")}) at {cineT:F2} s, shot {cineIdx + 1}/{cine.Count}, {(handT < 0f ? "on the RTS camera" : handDip ? "dip + cut" : "easing in")} (error {err:F2})");
        }

        void HandTick(float dt)
        {
            if (handT < 0f) { Put(homePose, true); SetDip(0f); return; }                            // done: hold the RTS camera until End
            handT += dt;
            if (handDip)
            {
                if (handT < SkipDipOut) { SetDip(Mathf.Lerp(handStartA, 1f, handT / Mathf.Max(0.01f, SkipDipOut))); return; }   // the frame holds
                float k = (handT - SkipDipOut) / Mathf.Max(0.01f, SkipDipIn);
                Put(homePose, true); SetDip(1f - k);
                if (k >= 1f) { SetDip(0f); handT = -1f; }
            }
            else
            {
                float k = Mathf.Clamp01(handT / Mathf.Max(0.05f, HandEase)), e = k * k * (3f - 2f * k);
                Put(new CinePose { pos = Vector3.Lerp(handFrom.pos, homePose.pos, e), look = Vector3.Lerp(handFrom.look, homePose.look, e),
                                   focus = Vector3.Lerp(handFrom.focus, homePose.focus, e), fov = Mathf.Lerp(handFrom.fov, homeFov, e) }, k >= 1f);
                if (k >= 1f) handT = -1f;
            }
        }

        /// <summary>End: the camera's field of view back to the game's, the dip overlay gone (also on an error hand-back).</summary>
        void CineEnd()
        {
            if (cine != null && camC) camC.fieldOfView = homeFov;
            if (gradeGo) UnityEngine.Object.Destroy(gradeGo);
            gradeGo = null; gradeVol = null;
            if (dipGo) UnityEngine.Object.Destroy(dipGo);
            dipGo = null; dipImg = null; dipA = 0f; cineHanding = false; handT = -1f; cineClock = null;
        }

        // ------------------------------------------------------------------------------------------------ briefing grade
        GameObject gradeGo; UnityEngine.Rendering.Volume gradeVol; UnityEngine.Rendering.Universal.ColorAdjustments gradeCa; UnityEngine.Rendering.Universal.WhiteBalance gradeWb;

        /// <summary>The briefing grade at weight w (a global volume over the level's own grade; none while BriefExposure / BriefTemperature are 0).</summary>
        void BriefGrade(float w)
        {
            bool want = Mathf.Abs(BriefExposure) > 1e-3f || Mathf.Abs(BriefTemperature) > 1e-3f;
            if (!want) { if (gradeVol) gradeVol.weight = 0f; return; }
            if (!gradeVol)
            {
                var prof = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>(); prof.name = "OZ_BriefingGrade (runtime)"; prof.hideFlags = HideFlags.DontSave;
                gradeCa = prof.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(false); gradeWb = prof.Add<UnityEngine.Rendering.Universal.WhiteBalance>(false);
                gradeGo = new GameObject("OZ_BriefingGradeVolume");
                gradeVol = gradeGo.AddComponent<UnityEngine.Rendering.Volume>(); gradeVol.isGlobal = true; gradeVol.priority = 30; gradeVol.sharedProfile = prof;
            }
            gradeCa.postExposure.Override(BriefExposure); gradeWb.temperature.Override(BriefTemperature);
            gradeVol.weight = Mathf.Clamp01(w);
        }

        // ------------------------------------------------------------------------------------------------ dip to black
        void SetDip(float a)
        {
            a = Mathf.Clamp01(a); dipA = a;
            if (a <= 0.001f) { if (dipImg) dipImg.enabled = false; return; }
            if (!dipImg)
            {
                dipGo = new GameObject("ZodBriefingDip");
                var cv = dipGo.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = DipSorting;
                var go = new GameObject("Black", typeof(RectTransform)); go.transform.SetParent(dipGo.transform, false);
                var rt = (RectTransform)go.transform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
                dipImg = go.AddComponent<Image>(); dipImg.raycastTarget = false; dipImg.color = Color.black;
            }
            dipImg.enabled = true;
            var c = dipImg.color; c.a = a; dipImg.color = c;
        }

        // ------------------------------------------------------------------------------------------------ checks
        /// <summary>Play check: the live shot list (clock, shot on screen, hand-back) and the last plan.</summary>
        public static string Status()
        {
            var f = Live;
            if (f == null || !f.Active) return "ZodFlyover: not flying; last plan: " + LastPlan;
            if (f.cine == null) return $"ZodFlyover: orbit flight, shot {f.goalName}";
            return $"ZodFlyover: shot list {f.cineLevel}, t {f.cineT:F2} s, shot {f.cineIdx + 1}/{f.cine.Count} {f.CineCurrent}, dip {f.dipA:F2}, "
                 + $"fov {(f.camC ? f.camC.fieldOfView : 0f):F1}, hold {HoldShot}; plan: {LastPlan}";
        }
    }
}
