using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// retargeted to BS-01 (LEGACY_Robot_Clips.fbx) carry a broken LEFT leg in the locomotion loops: in `walking` the left foot
    /// whips +-30 cm back and forth between 80 % and 97 % of every cycle (max jerk 4.5 cm per 1/400 cycle; the right foot is
    /// clean), `run_forward` / `run_backward` show the same left-leg spikes, and the loops do not close (a 3-7 cm pop at each
    /// wrap). Nothing in the view code writes leg bones (checked), so the pose comes straight from the clip.
    /// Fix: rebuild each locomotion loop as a clean asset: the right leg (clean) is made seamless (its end-to-start drift spread
    /// linearly over the cycle), and the left leg muscles + IK goal are the right leg's shifted by half a cycle (a walk / run is
    /// left-right symmetric in muscle space). Everything else (root, spine, arms, head) is copied unchanged. The controller
    /// states then point at the rebuilt clips. Menu: OpenZ/BS-01/Rebuild Leg Clips (walk, run).
    /// </summary>
    public static class OpenZLegClipFix
    {
        const string Controller = "Assets/OpenZ/Animation/RobotBS01.controller";
        const string Fbx = "Assets/OpenZ/Models/BS01/LEGACY_Robot_Clips.fbx";
        const string OutDir = "Assets/OpenZ/Animation/Robot/LegFix";
        static readonly string[] Targets = { "walking", "run_forward", "run_backward", "climb_ladder" };
        static readonly HashSet<string> Mirror = new HashSet<string> { "walking", "run_forward", "run_backward" };
        /// <summary>
        /// retargeted to BS-01 store the left foot's IK goal ~0.086-0.11 humanoid units higher than the right one in every standing clip
        /// (the muscles are fine, so this only shows with foot IK). These clips are copied unchanged except that the higher foot goal
        /// is lowered to the other foot's stance height. (idle_calm_humanoid.anim was fixed in place the same way.)</summary>
        static readonly string[] LevelOnly = { "bazooka_idle", "bazooka_fire", "grenade_throw" };
        /// <summary>Circular Gaussian low-pass on the leg curves, sigma in cycles: kills the 0.03-cycle toe-off wobble of both legs
        /// (gain ~0.03 there) and keeps the stride (4th harmonic gain ~0.93).</summary>
        public const float SigmaCycles = 0.015f, SigmaWalk = 0.025f;   // walking: a second 0.06-cycle wobble at toe-off (+-8 cm) needs 0.025 (gain 0.03; stride harmonics >= 0.82)
        static float sigma = SigmaCycles;
        static readonly string[] LegParts = { "Upper Leg", "Lower Leg", "Foot", "Toes" };

        [MenuItem("OpenZ/BS-01/Rebuild Leg Clips (walk, run)")]
        public static void RunMenu() => Debug.Log("[LegClipFix] " + Run());

        public static string Run()
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
            if (!ac) return "no controller";
            var src = new Dictionary<string, AnimationClip>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Fbx)) if (o is AnimationClip c && !c.name.StartsWith("__preview")) src[c.name] = c;
            if (!AssetDatabase.IsValidFolder(OutDir)) AssetDatabase.CreateFolder("Assets/OpenZ/Animation/Robot", "LegFix");
            var sb = new StringBuilder();
            foreach (var name in LevelOnly)
            {
                if (!src.TryGetValue(name, out var clip)) { sb.Append(name + ": missing; "); continue; }
                var lvl = LevelFeet(clip);
                var path = $"{OutDir}/{name}_legfix.anim";
                var old = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (old) { EditorUtility.CopySerialized(lvl, old); lvl = old; } else AssetDatabase.CreateAsset(lvl, path);
                int swapped = 0;
                foreach (var l in ac.layers) foreach (var s in l.stateMachine.states) if (s.state.motion == clip) { s.state.motion = lvl; swapped++; }
                sb.Append($"{name}: feet levelled -> {path}, {swapped} state(s); ");
            }
            foreach (var name in Targets)
            {
                if (!src.TryGetValue(name, out var clip)) { sb.Append(name + ": missing; "); continue; }
                sigma = name == "walking" ? SigmaWalk : SigmaCycles;
                var fixedClip = Rebuild(clip, Mirror.Contains(name));
                var path = $"{OutDir}/{name}_legfix.anim";
                var old = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (old) { EditorUtility.CopySerialized(fixedClip, old); fixedClip = old; }
                else AssetDatabase.CreateAsset(fixedClip, path);
                int swapped = 0;
                foreach (var l in ac.layers)
                    foreach (var s in l.stateMachine.states)
                        if (s.state.motion == clip) { s.state.motion = fixedClip; swapped++; }
                sb.Append($"{name}: rebuilt -> {path}, {swapped} state(s); ");
            }
            EditorUtility.SetDirty(ac);
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        static bool IsLeg(string prop, string side)
        {
            if (prop.StartsWith(side + "Foot")) return true;                                      // IK goal: LeftFootT.x / LeftFootQ.w
            foreach (var p in LegParts) if (prop.StartsWith(side + " " + p)) return true;         // muscles: "Left Upper Leg Front-Back"
            return false;
        }

        static AnimationClip LevelFeet(AnimationClip clip)
        {
            var outClip = new AnimationClip { name = clip.name + "_legfix", frameRate = clip.frameRate };
            var bindings = AnimationUtility.GetCurveBindings(clip);
            AnimationCurve l = null, r = null; EditorCurveBinding lb = default, rb = default;
            foreach (var b in bindings) { if (b.propertyName == "LeftFootT.y") { l = AnimationUtility.GetEditorCurve(clip, b); lb = b; } else if (b.propertyName == "RightFootT.y") { r = AnimationUtility.GetEditorCurve(clip, b); rb = b; } }
            float Min(AnimationCurve c) { float m = float.MaxValue; for (int i = 0; i <= 60; i++) m = Mathf.Min(m, c.Evaluate(clip.length * i / 60f)); return m; }
            float dl = 0f, dr = 0f;
            if (l != null && r != null) { float ml = Min(l), mr = Min(r); if (ml > mr) dl = mr - ml; else dr = ml - mr; }
            foreach (var b in bindings)
            {
                var c = AnimationUtility.GetEditorCurve(clip, b);
                float d = b.propertyName == "LeftFootT.y" ? dl : b.propertyName == "RightFootT.y" ? dr : 0f;
                if (d != 0f) { var keys = c.keys; for (int i = 0; i < keys.Length; i++) keys[i].value += d; c.keys = keys; }
                AnimationUtility.SetEditorCurve(outClip, b, c);
            }
            AnimationUtility.SetAnimationClipSettings(outClip, AnimationUtility.GetAnimationClipSettings(clip));
            return outClip;
        }

        static AnimationClip Rebuild(AnimationClip clip, bool mirror)
        {
            var outClip = new AnimationClip { name = clip.name + "_legfix", frameRate = clip.frameRate };
            float len = clip.length;
            int n = Mathf.Max(96, Mathf.RoundToInt(len * 240f));
            var bindings = AnimationUtility.GetCurveBindings(clip);
            var byProp = new Dictionary<string, AnimationCurve>();
            foreach (var b in bindings) byProp[b.propertyName] = AnimationUtility.GetEditorCurve(clip, b);
            foreach (var b in bindings)
            {
                string prop = b.propertyName;
                AnimationCurve curve;
                if (IsLeg(prop, "Right") || (!mirror && IsLeg(prop, "Left"))) curve = Seamless(byProp[prop], len, n, 0f, 1f);
                else if (mirror && IsLeg(prop, "Left"))
                {
                    var rightProp = "Right" + prop.Substring(4);
                    if (!byProp.TryGetValue(rightProp, out var right)) { curve = byProp[prop]; }
                    else
                    {
                        // IK goal positions mirror across the body's YZ plane (x flips); IK goal rotations mirror as (x, -y, -z, w)
                        float sign = prop.EndsWith("T.x") ? -1f : 1f;
                        curve = Seamless(right, len, n, len * 0.5f, sign);
                    }
                }
                else curve = byProp[prop];
                AnimationUtility.SetEditorCurve(outClip, b, curve);
            }
            var set = AnimationUtility.GetAnimationClipSettings(clip);
            set.loopTime = true; set.loopBlend = true;
            AnimationUtility.SetAnimationClipSettings(outClip, set);
            return outClip;
        }

        /// <summary>Resample `c` over one cycle starting at `offset` (wrapping), with its end-to-start drift spread linearly so the
        /// loop closes exactly; values multiplied by `sign`.</summary>
        static AnimationCurve Seamless(AnimationCurve c, float len, int n, float offset, float sign)
        {
            float v0 = c.Evaluate(0f), vEnd = c.Evaluate(len), drift = vEnd - v0;
            float At(float t) { t = Mathf.Repeat(t, len); return c.Evaluate(t) - drift * (t / len); }
            var raw = new float[n];
            for (int i = 0; i < n; i++) raw[i] = sign * At(len * i / n + offset);
            float sig = sigma * n; int rad = Mathf.CeilToInt(sig * 3f);
            var keys = new Keyframe[n + 1];
            for (int i = 0; i < n; i++)
            {
                float sum = 0f, wsum = 0f;
                for (int k = -rad; k <= rad; k++) { float w = Mathf.Exp(-0.5f * k * k / (sig * sig)); sum += w * raw[((i + k) % n + n) % n]; wsum += w; }
                keys[i] = new Keyframe(len * i / n, sum / wsum);
            }
            keys[n] = new Keyframe(len, keys[0].value);
            var outC = new AnimationCurve(keys);
            for (int i = 0; i <= n; i++) outC.SmoothTangents(i, 0f);
            return outC;
        }
    }
}
