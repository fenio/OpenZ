// OpenZ (GPL-3.0) - Level 16 briefing, the two robots' own script: the transmission window's camera.
//  - Five window shots (the data file's "shots"): the two on their sunbeds, the bigger robot close, the smaller robot close, the two again
//    while the smaller one sits up and points, and the CHASE (subjects "chase"): the crocodile chasing the blue robot, close, inside the
//    window only. The chase shot's camera stands a few metres over the water at the pool's south end (set space like every shot) and aims at the
//    chase as the stage moves it (between the fleeing robot's chest and the jaws); it is hand-held like a danger camera and makes a crash
//    zoom on every clack of the jaws (no shutter sound) that turns in on the robot and the jaws together and never narrows past them, so
//    both stay in the picture to the last frame.
//  - Clean pictures for the smaller robot: every cut of the window clears the window camera's motion history (no smeared frame across a
//    cut; the motion blur itself stays on); the window camera does not use the main view's baked occlusion and the two robots are never
//    culled by it (a second view low on the ground); a briefing that begins keeps the lying loop that already runs (no pose jump in its
//    first frame); the smaller robot's chin follows his jaw slowly (no nod on every syllable in his close-up).
// Play checks read: WindowCuts, HistoryResets, ChaseZooms, ChaseAimOn, ChinNow.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;

namespace OpenZ
{
    public sealed partial class OzL16LoungerSet
    {
        /// <summary>The chase shot: the lens at a clack (share of the shot's), seconds in / out, seconds before the clack it starts and after
        /// it it holds, how far the aim follows the chase (0..1).</summary>
        public static float ChaseLens = 0.62f, ChaseZoomIn = 0.09f, ChaseZoomOut = 0.18f, ChaseZoomLead = 0.18f, ChaseZoomHold = 0.30f, ChaseAimShare = 0.85f;
        /// <summary>The chase shot keeps the fleeing robot's head and the jaws within this share of the half picture (the hand's drift on
        /// top), and widens to at most this many times the shot's lens for it.</summary>
        public static float ChaseFit = 0.86f, ChaseWidenMax = 1.25f;
        /// <summary>The chase shot's hand: drift (deg) in pitch, yaw, roll, shift (m), a fine tremble (deg); a quarter of it on the calm screen.</summary>
        public static float ChaseHandPitch = 0.55f, ChaseHandYaw = 0.7f, ChaseHandRoll = 0.7f, ChaseHandShift = 0.03f, ChaseTremble = 0.15f;
        /// <summary>Seconds the smaller robot's chin takes to follow his jaw.</summary>
        public static float ChinFollow = 0.18f;
        /// <summary>Play checks: cuts of the window, motion histories cleared, crash zooms of the chase shot, the chase aim live, his chin now.</summary>
        public static int WindowCuts, HistoryResets, ChaseZooms; public static bool ChaseAimOn; public static float ChinNow;
        readonly Dictionary<Robot, float> chin = new Dictionary<Robot, float>();
        int chaseZoomLast = -1; float chaseZoomPrev; Vector3 chaseAim; bool chaseAimHas;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetWindowStatics() { WindowCuts = HistoryResets = ChaseZooms = 0; ChaseAimOn = false; ChinNow = 0f; }

        /// <summary>TickBrief, on every cut of the window: no motion history across the cut.</summary>
        void OnWindowCut()
        {
            WindowCuts++; chaseZoomLast = -1; chaseZoomPrev = 0f; chaseAimHas = false;
            if (!cam) return;
            var cd = cam.GetUniversalAdditionalCameraData();
            if (cd) { cd.resetHistory = true; HistoryResets++; }
        }

        /// <summary>BriefBegin: the window camera is a second view low on the ground: no baked occlusion of the main view for it, and the
        /// two robots are never culled by occlusion.</summary>
        void PrepareWindow()
        {
            if (cam) cam.useOcclusionCulling = false;
            foreach (var r in robots)
                if (r.rends != null)
                    foreach (var rr in r.rends) if (rr) rr.allowOcclusionWhenDynamic = false;
        }

        /// <summary>BriefBegin: each robot lies in the scene's lying loop; a loop that already runs keeps its place (no jump), any other pose
        /// eases into it.</summary>
        void StartLoopsKeep(string sc)
        {
            foreach (var r in robots)
            {
                var b = BeatDef(sc, r.who, "lie_can_loop");
                string want = b != null && !string.IsNullOrEmpty(b.clip) ? b.clip : "lie_can_loop";
                bool running = r.clip != null && r.clip.name == want && r.cur >= 0 && r.slot[r.cur].IsValid();
                if (!running) Play(r, want, r.cur < 0 ? 0f : 0.25f);
                r.jawExternal = -1f;
            }
        }

        /// <summary>LateUpdate: the smaller robot's chin lifts a little with his jaw (the game's own talking overlay), following it slowly.</summary>
        void ChinLift(Robot r, float dt)
        {
            if (r.who != "alan" || !r.head) return;
            chin.TryGetValue(r, out float c);
            if (dt > 0f) c += (r.jaw - c) * (1f - Mathf.Exp(-dt / Mathf.Max(0.02f, ChinFollow)));
            if (c < 0.001f && r.jaw <= 0f) c = 0f;
            chin[r] = c; ChinNow = c;
            if (c > 0.001f) r.head.rotation = Quaternion.AngleAxis(-2f * c, r.head.right) * r.head.rotation;
        }

        /// <summary>PlaceWindowCamera: the chase shot (subjects "chase"). False = not the chase shot.</summary>
        bool ChaseWindow(OzLoungerShot s)
        {
            if (s == null || s.subjects != "chase") { ChaseAimOn = false; return false; }
            Vector3 p = root.TransformPoint(new Vector3(s.px, s.py, s.pz)), at = root.TransformPoint(new Vector3(s.tx, s.ty, s.tz));
            p.y += shotLift; at.y += shotLift;
            var robot = OzL16BriefingStage.RobotNow; bool live = robot && OzL16BriefingStage.ChaseTau >= 0f;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (live)
            {   // the aim follows the chase: between the fleeing robot's chest (its feet are under the water) and the jaws behind it, eased
                Vector3 mid = Vector3.Lerp(OzL16BriefingStage.RobotHeadAt - Vector3.up * 0.45f, OzL16BriefingStage.JawsAt, 0.4f);
                if (!chaseAimHas) { chaseAim = mid; chaseAimHas = true; }
                chaseAim = Vector3.Lerp(chaseAim, mid, 1f - Mathf.Exp(-dt / 0.25f));
                at = Vector3.Lerp(at, chaseAim, ChaseAimShare);
            }
            ChaseAimOn = live;
            float now = Time.unscaledTime, len = Mathf.Max(0.5f, s.t1 - s.t0), u = Mathf.Clamp01((now - shotAt) / len), e = u * u * (3f - 2f * u);
            p = Vector3.Lerp(p, at, ShotPush * e);
            var look = Quaternion.LookRotation(at - p, Vector3.up);
            // a crash zoom on every clack of the jaws (the stage's chase clock), no sound of its own
            float k = 0f; int which = -1; float ct = OzL16BriefingStage.ChaseTau, pool = OzL16BriefingStage.ChasePoolDur;
            if (live && pool > 0f)
                for (int j = 0; j < OzL16BriefingStage.SnapCount; j++)
                {
                    float sn = OzL16BriefingStage.ChaseSnapSecond(j, pool), a = sn - ChaseZoomLead, b = sn + ChaseZoomHold;
                    float kj = Mathf.Clamp01((ct - a) / Mathf.Max(0.02f, ChaseZoomIn)) * Mathf.Clamp01((b + ChaseZoomOut - ct) / Mathf.Max(0.02f, ChaseZoomOut));
                    if (kj > k) { k = kj; which = j; }
                }
            k = k * k * (3f - 2f * k);
            bool calm = OzMotion.Reduced;
            if (calm) k *= 0.5f;
            if (k > 0.02f && chaseZoomPrev <= 0.02f && which != chaseZoomLast) { chaseZoomLast = which; ChaseZooms++; }
            chaseZoomPrev = k;
            if (k > 0.001f && live)
            {   // the zoom turns half way in on the pair: the robot's head and the jaws
                var toPair = (OzL16BriefingStage.JawsAt + OzL16BriefingStage.RobotHeadAt) * 0.5f - p;
                if (toPair.sqrMagnitude > 0.25f) look = Quaternion.Slerp(look, Quaternion.LookRotation(toPair, Vector3.up), k * 0.5f);
            }
            float fov = s.fov * Mathf.Lerp(1f, ChaseLens, k);
            if (live) fov = Mathf.Min(s.fov * ChaseWidenMax, Mathf.Max(fov, LensToHold(p, look, OzL16BriefingStage.RobotHeadAt, OzL16BriefingStage.JawsAt)));
            // hand-held: a drift, a fine tremble, more round a clack
            float amp = (calm ? 0.25f : 1f) * (1f + 0.6f * k);
            float n1 = Mathf.PerlinNoise(now * 0.9f, 1.3f) - 0.5f, n2 = Mathf.PerlinNoise(4.1f, now * 0.8f) - 0.5f, n3 = Mathf.PerlinNoise(now * 0.7f, 9.2f) - 0.5f;
            float t1 = Mathf.PerlinNoise(now * 7.3f, 2.2f) - 0.5f, t2 = Mathf.PerlinNoise(6.6f, now * 6.7f) - 0.5f;
            look = look * Quaternion.Euler((n1 * 2f * ChaseHandPitch + t1 * ChaseTremble) * amp, (n2 * 2f * ChaseHandYaw + t2 * ChaseTremble) * amp, n3 * 2f * ChaseHandRoll * amp);
            p += (look * Vector3.right * (n2 * 2f) + look * Vector3.up * (n1 * 2f)) * (ChaseHandShift * amp);
            cam.transform.SetPositionAndRotation(p, look);
            cam.fieldOfView = fov;
            return true;
        }

        /// <summary>The narrowest lens (vertical degrees) that keeps both points within ChaseFit of the half picture.</summary>
        float LensToHold(Vector3 p, Quaternion look, Vector3 a, Vector3 b)
        {
            var inv = Quaternion.Inverse(look); float need = 0f, aspect = Mathf.Max(0.1f, cam.aspect);
            var la = inv * (a - p); if (la.z > 0.1f) need = Mathf.Max(need, Mathf.Max(Mathf.Abs(la.y) / la.z, Mathf.Abs(la.x) / la.z / aspect));
            var lb = inv * (b - p); if (lb.z > 0.1f) need = Mathf.Max(need, Mathf.Max(Mathf.Abs(lb.y) / lb.z, Mathf.Abs(lb.x) / lb.z / aspect));
            return 2f * Mathf.Atan(need / Mathf.Max(0.3f, ChaseFit)) * Mathf.Rad2Deg;
        }
    }
}
