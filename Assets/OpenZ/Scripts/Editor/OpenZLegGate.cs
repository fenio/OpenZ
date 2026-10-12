using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Clip gate (EditMode, no Play): every gait and standing state on RobotBS01.controller's Base Layer must carry the 14 foot
    /// goal curves (LeftFootT/Q, RightFootT/Q) that RobotLegIK pins the feet to, with a smooth goal path (second difference per
    /// 60 fps frame, human-scale units: walking <= 0.012, others <= 0.035), no sign-flipped FootQ keys (consecutive 60 fps samples
    /// dot >= 0.9) and closed loops (<= 0.01); the Base Layer IK Pass must be on and RobotLegIK compiled. The leg muscle twist
    /// range is reported: above 1.2 the clip carries the degenerate knee decomposition of the straight-legged avatars (the root
    /// Play gate: LegJitterGate (menu below; enter Play in the full look at the 60 fps cap first).
    /// than every file that can bring the jitter back.
    /// </summary>
    public static class OpenZLegGate
    {
        const string Ctl = "Assets/OpenZ/Animation/RobotBS01.controller";
        static readonly string[] Gait = { "walking", "run_forward", "run_backward", "strafe_left", "strafe_right" };
        static readonly string[] Standing = { "idle", "shooting", "bazooka_idle", "bazooka_fire", "grenade_throw" };
        public const float GoalJerkWalkHu = 0.012f, GoalJerkHu = 0.035f, LoopGapHu = 0.01f, TwistDegenerate = 1.2f, FootQMinDot = 0.9f;
        static readonly string[] Sides = { "Left", "Right" };

        [MenuItem("OpenZ/Checks/Legs gate: clips (EditMode)")]
        public static void ClipGateMenu() => Debug.Log("[LegGate] " + ClipGate());

        [MenuItem("OpenZ/Checks/Legs gate: Play (8 s, walks squads)")]
        public static void PlayGateMenu()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[LegGate] enter Play first (full look, 60 fps cap, camera on player squads)"); return; }
            LegJitterGate.Begin(8f, true);
        }

        [MenuItem("OpenZ/Checks/Legs gate: Play (8 s, watch only)")]
        public static void PlayWatchMenu()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[LegGate] enter Play first"); return; }
            LegJitterGate.Begin(8f, false);
        }

        public static string ClipGate()
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(Ctl);
            if (!ac) return "FAIL: no " + Ctl;
            var fails = new List<string>(); var sb = new StringBuilder();
            var layers = ac.layers;
            bool ikPass = layers.Length > 0 && layers[0].iKPass;
            bool legIK = System.AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetType("OpenZ.RobotLegIK") != null);
            if (!ikPass) fails.Add("Base Layer IK Pass is off (RobotLegIK.OnAnimatorIK never runs)");
            if (!legIK) fails.Add("OpenZ.RobotLegIK is not compiled");
            sb.AppendLine("| state | clip | loop | goal curves | goal jerk (hu / 60 fps) | FootQ min dot | loop gap hu | upper twist max | lower twist max | verdict |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (var cs in layers[0].stateMachine.states)
            {
                var st = cs.state; bool gait = Gait.Contains(st.name);
                if (!gait && !Standing.Contains(st.name)) continue;
                if (!(st.motion is AnimationClip clip)) { fails.Add(st.name + ": no clip"); continue; }
                var curves = new Dictionary<string, AnimationCurve>();
                foreach (var b in AnimationUtility.GetCurveBindings(clip)) curves[b.propertyName] = AnimationUtility.GetEditorCurve(clip, b);
                bool loop = AnimationUtility.GetAnimationClipSettings(clip).loopTime;
                int goals = 0; foreach (var s in Sides) foreach (var c in new[] { "T.x", "T.y", "T.z", "Q.x", "Q.y", "Q.z", "Q.w" }) if (curves.ContainsKey(s + "Foot" + c)) goals++;
                float len = Mathf.Max(clip.length, 1f / 60f); int n = Mathf.Max(3, Mathf.CeilToInt(len * 60f));
                float jerk = 0f, minDot = 1f, gap = 0f, up = 0f, lo = 0f;
                if (goals == 14)
                    foreach (var s in Sides)
                    {
                        Vector3 P(float t) => new Vector3(curves[s + "FootT.x"].Evaluate(t), curves[s + "FootT.y"].Evaluate(t), curves[s + "FootT.z"].Evaluate(t));
                        Quaternion Q(float t) => new Quaternion(curves[s + "FootQ.x"].Evaluate(t), curves[s + "FootQ.y"].Evaluate(t), curves[s + "FootQ.z"].Evaluate(t), curves[s + "FootQ.w"].Evaluate(t));
                        for (int i = 0; i + 2 <= n; i++)
                        {
                            float t0 = len * i / n, t1 = len * (i + 1) / n, t2 = len * (i + 2) / n;
                            if (!loop && t2 > len) break;
                            jerk = Mathf.Max(jerk, (P(Wrap(t2, len)) - 2f * P(t1) + P(t0)).magnitude);
                            Vector4 a = Norm(Q(t0)), b = Norm(Q(t1));
                            minDot = Mathf.Min(minDot, Mathf.Abs(Vector4.Dot(a, b)));
                        }
                        if (loop) gap = Mathf.Max(gap, (P(len) - P(0f)).magnitude);
                    }
                foreach (var s in Sides)
                    for (int i = 0; i <= n; i++)
                    {
                        float t = len * i / n;
                        if (curves.TryGetValue(s + " Upper Leg Twist In-Out", out var u)) up = Mathf.Max(up, Mathf.Abs(u.Evaluate(t)));
                        if (curves.TryGetValue(s + " Lower Leg Twist In-Out", out var l)) lo = Mathf.Max(lo, Mathf.Abs(l.Evaluate(t)));
                    }
                var why = new List<string>();
                if (goals != 14) why.Add($"{goals}/14 foot goal curves");
                else
                {
                    float lim = st.name == "walking" ? GoalJerkWalkHu : GoalJerkHu;
                    if (jerk > lim) why.Add($"goal jerk {jerk:0.000} > {lim}");
                    if (minDot < FootQMinDot) why.Add($"FootQ sign flip (dot {minDot:0.00})");
                    if (loop && gait && gap > LoopGapHu) why.Add($"loop gap {gap:0.000}");
                }
                bool degenerate = up > TwistDegenerate || lo > TwistDegenerate;
                if (degenerate && (!ikPass || !legIK)) why.Add("degenerate leg twist without RobotLegIK");
                string verdict = why.Count == 0 ? (degenerate ? "ok (degenerate twist, neutralised by RobotLegIK)" : "ok") : "FAIL: " + string.Join(", ", why);
                if (why.Count > 0) fails.Add(st.name + " " + verdict);
                sb.AppendLine($"| {st.name} | {AssetDatabase.GetAssetPath(clip).Replace("Assets/OpenZ/", "")}:{clip.name} | {(loop ? "yes" : "no")} | {goals} | {jerk:0.0000} | {minDot:0.000} | {gap:0.0000} | {up:0.00} | {lo:0.00} | {verdict} |");
            }
            string result = fails.Count == 0 ? "PASS" : "FAIL: " + string.Join("; ", fails);
            var head = new StringBuilder();
            head.AppendLine($"RESULT: {result}\n");
            head.AppendLine($"# Legs gate (clips, EditMode) {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            head.AppendLine($"Base Layer IK Pass {(ikPass ? "on" : "OFF")}, RobotLegIK {(legIK ? "compiled" : "MISSING")}; thresholds: goal jerk walking <= {GoalJerkWalkHu} hu, others <= {GoalJerkHu} hu, FootQ dot >= {FootQMinDot}, loop gap <= {LoopGapHu} hu, twist > {TwistDegenerate} = degenerate\n");
            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records"));
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "leg_clip_gate.md"), head.ToString() + sb.ToString());
            return result;
        }

        static float Wrap(float t, float len) => t > len ? t - len : t;
        static Vector4 Norm(Quaternion q) { var v = new Vector4(q.x, q.y, q.z, q.w); float m = v.magnitude; return m > 1e-6f ? v / m : v; }
    }
}
