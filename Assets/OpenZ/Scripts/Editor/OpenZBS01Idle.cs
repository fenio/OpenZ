using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// look-around with the pelvis pinned in place (idle.anim and LEGACY_Robot_Clips.fbx "Armature|idle" hold the same data): in
    /// its 3.7 s the pelvis twists ~45 deg, the knees bend 34-39 deg, the toes flap 70-80 deg, the head turns ~100 deg and the
    /// feet slide 15-20 cm under the pinned hips. BS-01 retargets it through Humanoid muscle for muscle, so its thin legs and big
    /// boots paddle all the time. This tool builds a calm idle from that take:
    ///  * root (RootT/RootQ), foot IK goals and every leg, foot and toe muscle held at the take's rest frame (frame 0 = its
    ///    loop start): feet planted, no knee or toe motion;
    ///  * spine / chest at 35 %, neck / head at 25 % of their motion around the rest frame: a slow glance, no torso twist;
    ///  * a slight breathing sway (chest / upper chest front-back, shoulders up) once per loop; loop-exact curves + Loop Pose.
    /// RobotBS01.controller's Base Layer state "idle" plays idle_calm_humanoid.anim; squads stay desynced (UnitView's per-robot
    /// transform curves (idle_calm.anim). Checks: menu "Idle legs report (clips)" (EditMode) and "Idle legs check (Play, 5 s)".
    /// the lower (hips) and upper (shoulder-line) body orientation, so held leg muscles alone do not hold the feet: the calmed
    /// chest twist (the take turns the shoulder line up to 14 deg against the pelvis; x0.35 = 5 deg), the breathing and the head
    /// state therefore runs with Foot IK ON: the feet go to the held LeftFootT/Q, RightFootT/Q goals, which are stored relative
    /// to the held body transform. Arm / hand / finger muscles get the 35 % gain as well (constant in this take: its arms are
    /// rigid on the chest). The hold frame stays 0, the take's forward rest pose (its level-sole frames are the look-around:
    /// one, exactly like bazooka_idle and the aim clips (authored stance, static, not jitter).
    /// </summary>
    public static class OpenZBS01Idle
    {
        const string Anim = "Assets/OpenZ/Animation", Clips = "Assets/OpenZ/Models/BS01/LEGACY_Robot_Clips.fbx";
        const string BS01Ctl = Anim + "/RobotBS01.controller", LegacyCtl = Anim + "/Robot.controller", LegacyIdle = Anim + "/Robot/idle.anim";
        public const string HumanoidPath = Anim + "/Robot/idle_calm_humanoid.anim", GenericPath = Anim + "/Robot/idle_calm.anim";
        const float SpineGain = 0.35f, HeadGain = 0.25f, ArmGain = 0.35f, SampleRate = 30f;
        public const float LimitCm = 1f;
        static string LastLog = ""; static int LastGoalCurves;

        [MenuItem("OpenZ/BS-01: 4 Calm idle (feet planted)")]
        public static string Run()
        {
            var log = new List<string>();
            var src = AssetDatabase.LoadAllAssetsAtPath(Clips).OfType<AnimationClip>().FirstOrDefault(c => c.name == "idle");
            var ctl = AssetDatabase.LoadAssetAtPath<AnimatorController>(BS01Ctl);
            if (!src || !ctl) log.Add("BS-01: no humanoid idle / RobotBS01.controller (run BS-01 steps 1 + 2 first)");
            else log.Add(Apply(ctl, src));
            var gsrc = AssetDatabase.LoadAssetAtPath<AnimationClip>(LegacyIdle);
            var actl = AssetDatabase.LoadAssetAtPath<AnimatorController>(LegacyCtl);
            if (gsrc && actl)
            {
                var calm = MakeGeneric(gsrc, GenericPath);
                log.Add($"laser (Robot.controller): {SetIdle(actl, calm, false)} idle state(s) -> {GenericPath}");
            }
            AssetDatabase.SaveAssets();
            var s = string.Join("\n", log); Debug.Log("[OpenZBS01Idle] " + s); return s;
        }

        /// <summary>Builds / refreshes idle_calm_humanoid.anim from the humanoid idle and puts it on the controller's Base Layer
        /// "idle" state (also called by OpenZBS01.Build, so a controller rebuild keeps the calm idle).</summary>
        public static string Apply(AnimatorController ctl, AnimationClip humanoidIdle)
        {
            if (!ctl || !humanoidIdle) return "calm idle: nothing to do (no controller / idle clip)";
            string srcPath = AssetDatabase.GetAssetPath(humanoidIdle);
            if (srcPath == HumanoidPath || srcPath == GenericPath) return "calm idle: already in place";
            var calm = MakeHumanoid(humanoidIdle, HumanoidPath);
            bool ik = LastGoalCurves >= 14;                                                             // verify: Foot IK ON (class summary), only with the 14 held foot-goal curves
            if (!ik) Debug.LogWarning($"[OpenZBS01Idle] {humanoidIdle.name}: only {LastGoalCurves} of 14 foot-goal curves (LeftFootT/Q, RightFootT/Q); Foot IK left OFF, the feet may follow the hips");
            int n = SetIdle(ctl, calm, ik);
            AssetDatabase.SaveAssets();
            return $"BS-01 ({ctl.name}): {n} idle state(s) -> {HumanoidPath}, Foot IK {(ik ? "on" : "OFF")} ({LastLog}; spine x{SpineGain}, head x{HeadGain}, arms x{ArmGain}, breathing)";
        }

        // ------------------------------------------------------------------ humanoid (muscle space)
        /// <summary>Root / body motion (RootT, RootQ, MotionT, MotionQ), the foot IK goals (LeftFootT/Q, RightFootT/Q) and every leg,
        /// foot and toe muscle ("Left Upper Leg Front-Back", "Right Lower Leg Stretch", "Left Toes Up-Down", ...).</summary>
        static bool Held(string p) => p.StartsWith("Root") || p.StartsWith("Motion") || p.Contains("Leg") || p.Contains("Foot") || p.Contains("Toes");
        static float Gain(string p) =>
            p.Contains(" Eye ") || p.StartsWith("Jaw") ? 0f :
            p.StartsWith("Spine") || p.StartsWith("Chest") || p.StartsWith("UpperChest") ? SpineGain :
            p.StartsWith("Neck") || p.StartsWith("Head") ? HeadGain :
            p.StartsWith("Left") || p.StartsWith("Right") ? ArmGain : 1f;                             // verify: shoulders, arms, hands, fingers, hand goals (legs are Held first; constant in this take)
        /// <summary>Breathing amplitude (muscle units, 1 = the muscle's full range) once per loop: chest back, shoulders up.</summary>
        static float Breath(string p) =>
            p == "Chest Front-Back" || p == "UpperChest Front-Back" ? -0.03f : p == "Spine Front-Back" ? -0.015f :
            p == "Left Shoulder Down-Up" || p == "Right Shoulder Down-Up" ? 0.02f : 0f;

        public static AnimationClip MakeHumanoid(AnimationClip src, string path)
        {
            float T = src.length;
            var clip = LoadOrCreate(path, src.frameRate);
            int held = 0, calmed = 0, kept = 0; LastGoalCurves = 0;
            foreach (var b in AnimationUtility.GetCurveBindings(src))
            {
                var c = AnimationUtility.GetEditorCurve(src, b); if (c == null || c.length == 0) continue;
                AnimationCurve outC = c;
                if (b.type == typeof(Animator))
                {
                    string p = b.propertyName;
                    if (Held(p)) { outC = AnimationCurve.Constant(0f, T, c.Evaluate(0f)); held++; if (p.StartsWith("LeftFootT.") || p.StartsWith("LeftFootQ.") || p.StartsWith("RightFootT.") || p.StartsWith("RightFootQ.")) LastGoalCurves++; }
                    else { float g = Gain(p), br = Breath(p); if (g < 0.999f || br != 0f) { outC = Calm(c, g, br, T); calmed++; } else kept++; }
                }
                else kept++;
                AnimationUtility.SetEditorCurve(clip, b, outC);
            }
            Settings(clip, src, T);
            EditorUtility.SetDirty(clip);
            LastLog = $"{held} root / leg / foot-goal curves held (foot goals {LastGoalCurves} of 14), {calmed} spine / head / arm / breathing curves calmed, {kept} kept";
            if (held < 20) Debug.LogWarning($"[OpenZBS01Idle] only {held} leg / root curves found in {src.name}: is it a Humanoid clip?");
            return clip;
        }

        /// <summary>v0 + gain (v - v0) + breath sin(2 pi t / T), sampled at 30 Hz, made loop-exact (end = start).</summary>
        static AnimationCurve Calm(AnimationCurve c, float gain, float breath, float T)
        {
            int n = Mathf.Max(2, Mathf.RoundToInt(T * SampleRate));
            float v0 = c.Evaluate(0f);
            var vals = new float[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float t = T * i / n;
                vals[i] = v0 + gain * (c.Evaluate(t) - v0) + breath * Mathf.Sin(2f * Mathf.PI * t / T);
            }
            float drift = vals[n] - vals[0];
            var keys = new Keyframe[n + 1];
            for (int i = 0; i <= n; i++) keys[i] = new Keyframe(T * i / n, vals[i] - drift * i / n);
            keys[n].value = keys[0].value;
            var curve = new AnimationCurve(keys);
            for (int i = 0; i <= n; i++) curve.SmoothTangents(i, 0f);
            return curve;
        }

        static bool HeldBone(string bone) =>
            bone == "mixamorigHips" || bone.EndsWith("UpLeg") || bone == "mixamorigLeftLeg" || bone == "mixamorigRightLeg" ||
            bone.EndsWith("Foot") || bone.EndsWith("ToeBase") || bone.EndsWith("Toe_End");
        static float BoneGain(string bone) =>
            bone == "mixamorigSpine" || bone == "mixamorigSpine1" || bone == "mixamorigSpine2" ? SpineGain :
            bone == "mixamorigNeck" || bone == "mixamorigHead" ? HeadGain :
            bone.StartsWith("mixamorig") && (bone.Contains("Shoulder") || bone.EndsWith("Arm") || bone.Contains("Hand")) ? ArmGain : 1f;   // verify: calm arms too

        public static AnimationClip MakeGeneric(AnimationClip src, string path)
        {
            float T = src.length; int n = Mathf.Max(2, Mathf.RoundToInt(T * SampleRate));
            var clip = LoadOrCreate(path, src.frameRate);
            var binds = AnimationUtility.GetCurveBindings(src);
            var done = new HashSet<EditorCurveBinding>();
            foreach (var b in binds)
            {
                if (done.Contains(b)) continue;
                string bone = b.path.Split('/').Last();
                bool rot = b.type == typeof(Transform) && b.propertyName.StartsWith("m_LocalRotation.");
                bool pos = b.type == typeof(Transform) && b.propertyName.StartsWith("m_LocalPosition.");
                if (rot)
                {
                    var q = "xyzw".Select(ch => binds.FirstOrDefault(x => x.path == b.path && x.type == b.type && x.propertyName == "m_LocalRotation." + ch)).ToArray();
                    if (q.Any(x => x.propertyName == null)) { AnimationUtility.SetEditorCurve(clip, b, AnimationUtility.GetEditorCurve(src, b)); done.Add(b); continue; }
                    var cs = q.Select(x => AnimationUtility.GetEditorCurve(src, x)).ToArray();
                    foreach (var x in q) done.Add(x);
                    bool held = HeldBone(bone); float g = BoneGain(bone);
                    if (!held && g >= 0.999f) { for (int k = 0; k < 4; k++) AnimationUtility.SetEditorCurve(clip, q[k], cs[k]); continue; }
                    Quaternion Q(float t) => new Quaternion(cs[0].Evaluate(t), cs[1].Evaluate(t), cs[2].Evaluate(t), cs[3].Evaluate(t)).normalized;
                    var q0 = Q(0f);
                    if (held) { var v = new[] { q0.x, q0.y, q0.z, q0.w }; for (int k = 0; k < 4; k++) AnimationUtility.SetEditorCurve(clip, q[k], AnimationCurve.Constant(0f, T, v[k])); continue; }
                    var keys = new Keyframe[4][]; for (int k = 0; k < 4; k++) keys[k] = new Keyframe[n + 1];
                    var prev = q0;
                    for (int i = 0; i <= n; i++)
                    {
                        float t = T * i / n;
                        var qa = i == n ? q0 : Quaternion.Slerp(q0, Q(t), g);
                        if (Quaternion.Dot(prev, qa) < 0f) qa = new Quaternion(-qa.x, -qa.y, -qa.z, -qa.w);   // keep the hemisphere
                        prev = qa;
                        keys[0][i] = new Keyframe(t, qa.x); keys[1][i] = new Keyframe(t, qa.y); keys[2][i] = new Keyframe(t, qa.z); keys[3][i] = new Keyframe(t, qa.w);
                    }
                    for (int k = 0; k < 4; k++)
                    {
                        var curve = new AnimationCurve(keys[k]); for (int i = 0; i <= n; i++) curve.SmoothTangents(i, 0f);
                        AnimationUtility.SetEditorCurve(clip, q[k], curve);
                    }
                    continue;
                }
                var c = AnimationUtility.GetEditorCurve(src, b); done.Add(b);
                if (c == null) continue;
                if (pos && HeldBone(bone)) c = AnimationCurve.Constant(0f, T, c.Evaluate(0f));
                AnimationUtility.SetEditorCurve(clip, b, c);
            }
            Settings(clip, src, T);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        // ------------------------------------------------------------------ shared
        static AnimationClip LoadOrCreate(string path, float fps)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);                          // keep the GUID: controllers keep their link
            if (!clip) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
            clip.ClearCurves(); clip.name = Path.GetFileNameWithoutExtension(path);                  // verify: object name = file name (no name-mismatch warning)
            clip.frameRate = fps > 0 ? fps : 30f; clip.legacy = false;
            return clip;
        }

        static void Settings(AnimationClip clip, AnimationClip src, float T)
        {
            var st = AnimationUtility.GetAnimationClipSettings(src);
            st.startTime = 0f; st.stopTime = T; st.loopTime = true; st.loopBlend = true; st.cycleOffset = 0f; st.mirror = false;
            st.loopBlendOrientation = true; st.loopBlendPositionY = true; st.loopBlendPositionXZ = true;              // bake into pose (in place)
            st.keepOriginalOrientation = true; st.keepOriginalPositionY = true; st.keepOriginalPositionXZ = true; st.heightFromFeet = false;
            st.hasAdditiveReferencePose = false;
            AnimationUtility.SetAnimationClipSettings(clip, st);
        }

        /// <summary>Every state named "idle" (any layer, any sub-machine) plays m. Foot IK: ON for the Humanoid BS-01 (the held
        /// foot goals pin the feet while the centre-of-mass / body-orientation solve moves the hips), off for the Generic laser
        /// robot (Generic ignores it; its hips and legs are held directly).</summary>
        static int SetIdle(AnimatorController ctl, Motion m, bool footIK)
        {
            int n = 0;
            void Walk(AnimatorStateMachine sm)
            {
                foreach (var s in sm.states) if (s.state.name == "idle") { s.state.motion = m; s.state.iKOnFeet = footIK; EditorUtility.SetDirty(s.state); n++; }
                foreach (var c in sm.stateMachines) Walk(c.stateMachine);
            }
            foreach (var l in ctl.layers) Walk(l.stateMachine);
            EditorUtility.SetDirty(ctl);
            return n;
        }

        // ------------------------------------------------------------------ EditMode report (no Play needed)
        static readonly HumanBodyBones[] FeetBones = { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes };

        /// <summary>Samples the original idle and the calm idle on a BS-01 grunt and on the laser robot in a preview scene
        /// (PlayableGraph, 60 Hz over one loop) and reports how far each foot / toe bone moves (cm, largest distance from the
        /// first frame) and the knee swing (deg). PASS = every calm foot bone under 1 cm while the original shows its
        /// </summary>
        [MenuItem("OpenZ/BS-01: Idle legs report (clips)")]
        public static string Report()
        {
            var sb = new StringBuilder("# Idle legs report (EditMode, one loop sampled at 60 Hz)\n\n| robot | clip | feet / toes max move (cm) L foot, R foot, L toes, R toes | knee swing (deg) L, R | hips move (cm) |\n|---|---|---|---|---|\n");
            bool pass = true;
            var grunt = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/OpenZ/Prefabs/Z_bs01_grunt.prefab");
            var hsrc = AssetDatabase.LoadAllAssetsAtPath(Clips).OfType<AnimationClip>().FirstOrDefault(c => c.name == "idle");
            var hcalm = AssetDatabase.LoadAssetAtPath<AnimationClip>(HumanoidPath);
            var laser = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/OpenZ/Prefabs/Z_robot.prefab");
            var gsrc = AssetDatabase.LoadAssetAtPath<AnimationClip>(LegacyIdle);
            var gcalm = AssetDatabase.LoadAssetAtPath<AnimationClip>(GenericPath);
            pass &= Row(sb, "BS-01 grunt", grunt, hsrc, hcalm, true);                                  // calm sampled with Foot IK, as its state plays it
            pass &= Row(sb, "laser (legacy)", laser, gsrc, gcalm, false);
            if (grunt && hcalm) sb.AppendLine("\nInfo (not part of PASS): " + Soles(grunt, hcalm));
            var ctl = AssetDatabase.LoadAssetAtPath<AnimatorController>(BS01Ctl);
            var idleState = ctl ? ctl.layers[0].stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == "idle") : null;
            bool wired = idleState && idleState.motion == hcalm && idleState.iKOnFeet;
            sb.AppendLine($"\nRobotBS01.controller Base Layer idle state -> {(idleState && idleState.motion ? AssetDatabase.GetAssetPath(idleState.motion) + " / " + idleState.motion.name + (idleState.iKOnFeet ? ", Foot IK on" : ", Foot IK OFF") : "none")} ({(wired ? "calm idle wired" : "NOT the calm idle with Foot IK: run OpenZ/BS-01: 4 Calm idle")})");
            pass &= wired;
            sb.AppendLine($"\n**{(pass ? "PASS" : "FAIL")}**: calm idle feet move < {LimitCm} cm over a loop on every robot" + (pass ? "" : " (see rows)"));
            var text = sb.ToString();
            File.WriteAllText(Path.Combine(OpenZPaths.Records, "idle_legs_report.md"), text);
            Debug.Log("[OpenZBS01Idle] " + text);
            return text;
        }

        static bool Row(StringBuilder sb, string label, GameObject prefab, AnimationClip src, AnimationClip calm, bool calmFootIK)
        {
            if (!prefab || !src) { sb.AppendLine($"| {label} | - | prefab or source idle missing | | |"); return false; }
            var a = Measure(prefab, src, false, out var ea);
            sb.AppendLine($"| {label} | {src.name} (original) | {Fmt(a, 0)} | {Fmt(a, 1)} | {Fmt(a, 2)} |" + (ea != null ? " " + ea : ""));
            if (!calm) { sb.AppendLine($"| {label} | calm idle | missing: run OpenZ/BS-01: 4 Calm idle | | |"); return false; }
            var b = Measure(prefab, calm, calmFootIK, out var eb);
            sb.AppendLine($"| {label} | {calm.name} | {Fmt(b, 0)} | {Fmt(b, 1)} | {Fmt(b, 2)} |" + (eb != null ? " " + eb : ""));
            bool sampled = a != null && a[0].Max() > 3f;                                              // the original must paddle, or the sampling failed
            if (!sampled) { sb.AppendLine($"| {label} | | sampling produced no motion on the original: use the Play check | | |"); return false; }
            return b != null && b[0].Max() < LimitCm;
        }

        static string Fmt(float[][] m, int k) => m == null ? "n/a" : string.Join(", ", m[k].Select(x => x.ToString("F1")));

        /// <summary>[0] = foot/toe bones max move (cm), [1] = knee swing (deg), [2] = hips move (cm); model-root space.</summary>
        static float[][] Measure(GameObject prefab, AnimationClip clip, bool footIK, out string err)
        {
            err = null;
            var scene = EditorSceneManager.NewPreviewScene();
            PlayableGraph graph = default;
            try
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var an = go.GetComponentInChildren<Animator>();
                if (!an) { err = "(no Animator)"; return null; }
                an.cullingMode = AnimatorCullingMode.AlwaysAnimate;                                       // verify: nothing renders in a preview scene, a culled Animator never writes the pose
                var feet = new List<Transform>(); Transform kl, kr, hips;
                if (an.isHuman)
                {
                    foreach (var hb in FeetBones) feet.Add(an.GetBoneTransform(hb));
                    kl = an.GetBoneTransform(HumanBodyBones.LeftLowerLeg); kr = an.GetBoneTransform(HumanBodyBones.RightLowerLeg); hips = an.GetBoneTransform(HumanBodyBones.Hips);
                }
                else
                {
                    var all = go.GetComponentsInChildren<Transform>(true);
                    Transform F(string n) => all.FirstOrDefault(t => t.name == "mixamorig" + n);
                    feet.AddRange(new[] { F("LeftFoot"), F("RightFoot"), F("LeftToeBase"), F("RightToeBase") });
                    kl = F("LeftLeg"); kr = F("RightLeg"); hips = F("Hips");
                }
                if (feet.Any(f => !f) || !kl || !kr || !hips) { err = "(bones missing)"; return null; }
                graph = PlayableGraph.Create("OpenZ idle legs report");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "legs", an);
                var cp = AnimationClipPlayable.Create(graph, clip); cp.SetApplyFootIK(footIK); cp.SetApplyPlayableIK(false);
                output.SetSourcePlayable(cp);
                graph.Play();
                var root = go.transform;
                Vector3[] f0 = null; Quaternion kl0 = default, kr0 = default; Vector3 h0 = default;
                var mv = new float[4]; float sl = 0, sr = 0, hm = 0;
                int n = Mathf.Max(2, Mathf.RoundToInt(clip.length * 60f));
                for (int i = 0; i <= n; i++)
                {
                    cp.SetTime(clip.length * i / n); graph.Evaluate(0f);
                    var fp = feet.Select(f => root.InverseTransformPoint(f.position)).ToArray();
                    var h = root.InverseTransformPoint(hips.position);
                    if (i == 0) { f0 = fp; kl0 = kl.localRotation; kr0 = kr.localRotation; h0 = h; continue; }
                    for (int k = 0; k < 4; k++) mv[k] = Mathf.Max(mv[k], (fp[k] - f0[k]).magnitude * root.lossyScale.y * 100f);
                    sl = Mathf.Max(sl, Quaternion.Angle(kl0, kl.localRotation)); sr = Mathf.Max(sr, Quaternion.Angle(kr0, kr.localRotation));
                    hm = Mathf.Max(hm, (h - h0).magnitude * root.lossyScale.y * 100f);
                }
                return new[] { mv, new[] { sl, sr }, new[] { hm } };
            }
            catch (System.Exception e) { err = "(" + e.GetType().Name + ": " + e.Message + ")"; return null; }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>Info: where the BS-01 soles sit in the calm idle pose (UnitView places robots at ground + yOffset -0.08 m, a value
        /// </summary>
        static string Soles(GameObject prefab, AnimationClip clip)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            PlayableGraph graph = default;
            try
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var an = go.GetComponentInChildren<Animator>(); if (!an) return "no Animator";
                an.cullingMode = AnimatorCullingMode.AlwaysAnimate;                                       // verify: see Measure
                graph = PlayableGraph.Create("OpenZ idle soles");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var cp = AnimationClipPlayable.Create(graph, clip); cp.SetApplyFootIK(true);                   // as the idle state plays it
                AnimationPlayableOutput.Create(graph, "soles", an).SetSourcePlayable(cp);
                graph.Play(); cp.SetTime(0); graph.Evaluate(0f);
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var m = new Mesh(); smr.BakeMesh(m, false);
                    foreach (var v in m.vertices) { float y = go.transform.InverseTransformPoint(smr.transform.TransformPoint(v)).y; lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y); }
                    Object.DestroyImmediate(m);
                }
                return $"calm idle pose (Foot IK): lowest vertex (soles) at {lo * 100f:F1} cm, top at {hi:F2} m above the prefab root: for soles on the ground UnitView.Bind's BS-01 yOffset = {-lo:F2} m (the legacy laser keeps its -0.08)";
            }
            catch (System.Exception e) { return "soles: " + e.Message; }
            finally { if (graph.IsValid()) graph.Destroy(); EditorSceneManager.ClosePreviewScene(scene); }
        }

        // ------------------------------------------------------------------ Play-mode check
        /// <summary>Play mode: measures, for 5 s, every live robot that stands idle in the sim (feet / toes world motion, root
        /// </summary>
        [MenuItem("OpenZ/BS-01: Idle legs check (Play, 5 s)")]
        public static void PlayCheck()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[OpenZBS01Idle] enter Play mode (a level with ZGame) first"); return; }
            IdleLegsCheck.Begin();
        }
    }
}
