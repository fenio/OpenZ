// OpenZ (GPL-3.0) - Level 16 endings, round 4: the scene's camera holds still for the world, and the far haze draws no line at the horizon.
//  - From the cut to the loungers until the level is left, the game's world shake (OpenZFxShake: the kicks and rumbles of blasts, made
//    for the high game camera, up to 1.35 m of camera travel per kick; held at 0 through OpenZFxPlayer.shakeScale, which the player
//    writes into it every frame) does not move the scene's camera. The two robots lie 3 to 6 m
//    from its lens: one rumble of the fallen fort or one kick of a far blast threw them about the frame and out of the picture. The
//    scene's own rumble (EndingShake: 1.2 cm and 0.32 degrees on the flash and on the boom) stays the only shake of this camera.
//  - See-through effects (the campfire's glow and smoke, the fort's flames, the blast cloud) write no depth, so the atmosphere pass hazed
//    their pixels by whatever stood far behind them: over the far hills with the hills' haze, over the open sky just above them with the
//    sky's, so a straight line ran across every effect where the hills meet the sky. While the game camera draws the scene, the far land
//    outside the map (from HazeFarFrom to HazeFarTo metres outside it) takes the sky's own treatment at its height: the same fog distance,
//    readability rule, in-scatter, haze band, clearing and dawn grade on both sides of the crest, so nothing in front of it shows the crest.
//  - The blast cloud also keeps the haze of its own distance (110 to 156 m, next to none): two shapes follow it (the same numbers as its
//    own drawing): its stem, an upright column from the fort's ground, and its cap, a ball; inside them the haze is the cloud's own, with
//    soft edges. Nothing changes outside the scene (the shapes and the far land's rule reach the pass only while the game camera draws it).
//  - Counts for the Play check, every frame the game camera draws while the scene holds it: frames, frames whose camera stood more than
//    2 cm off the scene's own pose, the largest offset, robot frames out of the picture, robot frames with no part switched on, and robot
//    frames that no camera drew (none of the robot's parts visible after the frame's culling).
// Play check: return OpenZ.ZodBriefing.EndingHoldStatus();
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    public sealed partial class ZodBriefing
    {
        /// <summary>The haze stops at the blast cloud while the scene shows it (false = the pass as before, for a before / after picture).</summary>
        public static bool EndingHazeColumn = true;
        /// <summary>The far land is hazed like the sky at its height while the scene shows it (false = the pass as before, for a before / after
        /// picture of the line where the hills meet the sky).</summary>
        public static bool EndingHazeFar = true;
        /// <summary>The far land's rule eases in from HazeFarFrom to HazeFarTo metres outside the map (the swamp water next to the plateau keeps
        /// its look; the far hills stand 265 m or more outside the map).</summary>
        public static float HazeFarFrom = 60f, HazeFarTo = 220f;
        /// <summary>The world shake is held off the scene's camera (false = as before, for a before / after count).</summary>
        public static bool EndingHoldShake = true;
        /// <summary>The cloud's two shapes against its own numbers: the stem column's radius as a share of the cap's radius, the ball's
        /// radius as a share of the cap's radius, the column's top as a share of the cap's height.</summary>
        public static float HazeStemK = 0.42f, HazeCapK = 1.0f, HazeTopK = 1.0f;
        /// <summary>Counts of the scene (Play check).</summary>
        public static int HoldFrames, HoldFramesOff, HoldRobotOut, HoldRobotUndrawn, HoldRobotUnseen, HoldShakesHeld;
        public static float HoldMostOff;
        /// <summary>The scene holds the game camera now (the cut to the loungers until the level is left).</summary>
        public static bool EndingHolding => holdOn;

        static bool holdOn, holdHooked; static float holdShakeScale = -1f; static Vector3 holdFort, holdPose; static Quaternion holdRot; static int holdPoseFrame = -1;
        static Vector4 holdMapRect;
        static readonly int IdColumn = Shader.PropertyToID("_AtmoColumn"), IdColumnCap = Shader.PropertyToID("_AtmoColumnCap"), IdColumnTop = Shader.PropertyToID("_AtmoColumnTop"),
            IdFarRect = Shader.PropertyToID("_AtmoFarRect"), IdFarSky = Shader.PropertyToID("_AtmoFarSky");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHold()
        {   // a new Play session starts clean (the counts are per session)
            if (holdHooked) { RenderPipelineManager.beginCameraRendering -= HoldBeginCamera; RenderPipelineManager.endCameraRendering -= HoldEndCamera; }
            holdOn = holdHooked = false; holdShakeScale = -1f; holdPoseFrame = -1;
            HoldFrames = HoldFramesOff = HoldRobotOut = HoldRobotUndrawn = HoldRobotUnseen = HoldShakesHeld = 0; HoldMostOff = 0f;
        }

        /// <summary>EndingRoutine, at the cut: the scene holds the game camera from now until the level is left.</summary>
        static void EndingHoldBegin(Vector3 fort)
        {
            holdOn = true; holdFort = fort; holdPoseFrame = -1;
            HoldFrames = HoldFramesOff = HoldRobotOut = HoldRobotUndrawn = HoldRobotUnseen = 0; HoldMostOff = 0f;
            // the map's ground: x from 0 to its width, z from minus its depth to 0 (as the level's camera and the far land read it)
            var rts = Object.FindFirstObjectByType<RtsCamera>();
            Vector2 map = rts ? rts.mapSizeMeters : new Vector2(208f, 256f);
            holdMapRect = new Vector4(0f, -map.y, map.x, 0f);
            HoldWorldShake();
            if (!holdHooked)
            {
                holdHooked = true;
                RenderPipelineManager.beginCameraRendering += HoldBeginCamera;
                RenderPipelineManager.endCameraRendering += HoldEndCamera;
            }
            Debug.Log($"[ZodCameo] ending hold: the world shake {(EndingHoldShake ? "is held off the scene's camera" : "is NOT held (switch off)")}, the haze {(EndingHazeColumn ? "stops at the blast cloud" : "as before at the cloud (switch off)")}, "
                      + $"the far land {(EndingHazeFar ? $"hazed as the sky ({HazeFarFrom:F0} to {HazeFarTo:F0} m outside the map {map.x:F0} x {map.y:F0} m)" : "as before (switch off)")}");
        }

        /// <summary>The world shake gives the scene's camera nothing: its scale 0 and what it was playing dropped.</summary>
        static void HoldWorldShake()
        {
            if (!EndingHoldShake) return;
            var fx = OpenZFxPlayer.I; var sh = OpenZFxShake.I;
            if (fx)
            {   // the player's own scale is written into the shake every frame (OpenZFxPlayer.LateUpdate): it is the one to hold at 0
                if (holdShakeScale < 0f) holdShakeScale = fx.shakeScale;
                fx.shakeScale = 0f;
            }
            if (sh == null) return;
            if (sh.scale != 0f || sh.Energy > 0.001f) HoldShakesHeld++;
            sh.scale = 0f; sh.Reset();
        }

        /// <summary>EndingRoutine, every frame after the scene placed its camera (its own rumble included): the pose the frame must be drawn from.</summary>
        static void EndingHoldPose(Transform cam)
        {
            if (!cam) return;
            holdPose = cam.position; holdRot = cam.rotation; holdPoseFrame = Time.frameCount;
            if (EndingHoldShake && OpenZFxShake.I != null && OpenZFxShake.I.scale != 0f) HoldWorldShake();   // a shake made anew in the meantime
        }

        /// <summary>The level is left (EndingRestore): the world shake and the haze are as before.</summary>
        static void EndingHoldEnd()
        {
            holdOn = false;
            var fx = OpenZFxPlayer.I; var sh = OpenZFxShake.I;
            if (holdShakeScale >= 0f) { if (fx) fx.shakeScale = holdShakeScale; if (sh != null) sh.scale = holdShakeScale; }
            holdShakeScale = -1f;
            ClearColumn();
            if (holdHooked)
            {
                holdHooked = false;
                RenderPipelineManager.beginCameraRendering -= HoldBeginCamera;
                RenderPipelineManager.endCameraRendering -= HoldEndCamera;
            }
        }

        static void ClearColumn()
        {
            Shader.SetGlobalVector(IdColumn, Vector4.zero); Shader.SetGlobalVector(IdColumnCap, Vector4.zero); Shader.SetGlobalFloat(IdColumnTop, 0f);
            Shader.SetGlobalVector(IdFarSky, Vector4.zero);
        }

        static void HoldBeginCamera(ScriptableRenderContext ctx, Camera c)
        {
            if (!holdOn || c == null || c.cameraType != CameraType.Game || c != Camera.main) return;
            HoldCount(c);
            SetColumn();
            SetFar();
        }

        static void HoldEndCamera(ScriptableRenderContext ctx, Camera c)
        {
            if (!holdOn || c == null || c != Camera.main) return;
            ClearColumn();                                                                              // no other camera of the frame gets the shapes
            HoldSeen();
        }

        static void HoldCount(Camera c)
        {
            HoldFrames++;
            if (holdPoseFrame >= 0)
            {
                float off = Vector3.Distance(c.transform.position, holdPose);
                if (off > HoldMostOff) HoldMostOff = off;
                if (off > 0.02f || Quaternion.Angle(c.transform.rotation, holdRot) > 1f) HoldFramesOff++;
            }
            var set = OzL16LoungerSet.I;
            if (!set || set.Robots == null) return;
            foreach (var r in set.Robots)
            {
                if (r == null || !r.root) continue;
                bool drawn = false;
                if (r.rends != null) foreach (var rr in r.rends) if (rr && rr.enabled && !rr.forceRenderingOff && rr.gameObject.activeInHierarchy) { drawn = true; break; }
                if (!drawn) HoldRobotUndrawn++;
                Vector3 a = c.WorldToViewportPoint(r.head ? r.head.position : r.root.position), b = c.WorldToViewportPoint(r.root.position + Vector3.up * 0.4f);
                bool inA = a.z > c.nearClipPlane && a.x > 0f && a.x < 1f && a.y > 0f && a.y < 1f, inB = b.z > c.nearClipPlane && b.x > 0f && b.x < 1f && b.y > 0f && b.y < 1f;
                if (!inA && !inB) HoldRobotOut++;
            }
        }

        /// <summary>After the game camera drew the frame: a robot none of whose parts the frame's culling found visible was not drawn
        /// (switched on is not enough: a part culled away would still read as on).</summary>
        static void HoldSeen()
        {
            var set = OzL16LoungerSet.I;
            if (!set || set.Robots == null) return;
            foreach (var r in set.Robots)
            {
                if (r == null || !r.root || r.rends == null) continue;
                bool seen = false;
                foreach (var rr in r.rends) if (rr && rr.isVisible) { seen = true; break; }
                if (!seen) HoldRobotUnseen++;
            }
        }

        /// <summary>The cloud's stem and cap as the haze pass reads them (the cloud's own growth: OpenZNukeMushroom's rise and cap radius).</summary>
        static void SetColumn()
        {
            var m = OpenZNukeMushroom.Current;
            if (!EndingHazeColumn || !m)
            {
                Shader.SetGlobalVector(IdColumn, Vector4.zero); Shader.SetGlobalVector(IdColumnCap, Vector4.zero); Shader.SetGlobalFloat(IdColumnTop, 0f);
                return;
            }
            float t = m.Age, s = Mathf.Max(0.1f, OpenZNukeMushroom.SceneScale), p = Mathf.Min(t / NukeFx.Duration, 1f), late = Mathf.Max(0f, t - NukeFx.Duration);
            float rise = Mathf.Max(1.2f, (1f - (1f - p) * (1f - p)) * 27f + late * 0.6f) * s;
            float capR = Mathf.Max(0.6f, 10f * (1f - Mathf.Pow(1f - p, 4f)) + late * 0.35f) * s;
            Vector3 foot = holdFort; var g = ZGame.I; if (g) foot.y = g.GroundY(foot.x, foot.z);
            Shader.SetGlobalVector(IdColumn, new Vector4(foot.x, foot.y, foot.z, Mathf.Max(1.5f, capR * HazeStemK)));
            Shader.SetGlobalFloat(IdColumnTop, rise * HazeTopK);
            Vector3 cap = foot + Vector3.up * rise;
            Shader.SetGlobalVector(IdColumnCap, new Vector4(cap.x, cap.y, cap.z, capR * HazeCapK));
        }

        /// <summary>The far land's rule for the pass: the map's ground and the blend outside it (w = 0: the pass as before).</summary>
        static void SetFar()
        {
            if (!EndingHazeFar) { Shader.SetGlobalVector(IdFarSky, Vector4.zero); return; }
            float from = Mathf.Max(0f, HazeFarFrom), to = Mathf.Max(from + 1f, HazeFarTo);
            Shader.SetGlobalVector(IdFarRect, holdMapRect);
            Shader.SetGlobalVector(IdFarSky, new Vector4(from, to, 0f, 1f));
        }

        /// <summary>Play check: the scene's hold in one line.</summary>
        public static string EndingHoldStatus() =>
            $"ending hold: {(holdOn ? "on" : "off")}, world shake {(EndingHoldShake ? "held" : "NOT held")} (times held {HoldShakesHeld}), haze stops at the cloud {EndingHazeColumn}, far land hazed as the sky {EndingHazeFar}; "
            + $"frames {HoldFrames}, frames off the scene's pose {HoldFramesOff} (most {HoldMostOff * 100f:F1} cm), robot frames out of the picture {HoldRobotOut}, robot frames not drawn {HoldRobotUndrawn}, robot frames no camera drew {HoldRobotUnseen}";
    }
}
