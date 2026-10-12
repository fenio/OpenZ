using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// legs_gate_v2 review fix (EditMode, no Play): RobotLegIK's phase windows (SwingBump: where ProcSwing lifts a boot; PhaseStance:
    /// where a boot is locked) were measured on the BS-01 LegFix clips of RobotBS01.controller. The v3 walks / runs of the
    /// RobotV3_*.overrideController files (tough_walk, sniper_walk / run / back, electro walk / run / back) are retargeted from the
    /// Blender BS-01 'walking' action, not made from walking_legfix, so their step phase is measured here, the same way on both:
    /// per foot, from the clip's own foot goal curves (LeftFootT / RightFootT, body space) and root curves (RootT / RootQ):
    ///   low   = centre of the phase band where the boot is within 10 % (of its height range) of its lowest;
    ///   back  = the phase window around the lowest point where the boot moves back relative to the body (the stance);
    ///   slow  = the phase window around it where the boot's ground speed (root + body-space goal) is below 25 % of the root speed.
    /// A v3 clip FAILS when any of these edges is more than MaxShift (0.05 of a cycle) away from the BS-01 clip it replaces (the
    /// clip the windows were measured on); the distance to SwingBump's stance and PhaseStance's windows is printed for both.
    /// Fallback for a FAIL: RobotLegIK.ProcSwingSkip.Add("[type]") (EditMode, then re-run this gate: that type's rows read "skipped",
    /// no fail, RESULT: PASS (skipped by ProcSwingSkip: ...)); the type then keeps its clip boot height and gets no phase lock in game.
    /// The set is static: add it again after entering Play (domain reload), or make it permanent in RobotLegIK's initializer.
    /// </summary>
    public static class OpenZLegPhaseGate
    {
        const string Ctl = "Assets/OpenZ/Animation/RobotBS01.controller";
        public static readonly string[] States = { "walking", "run_forward", "run_backward" };
        public const float MaxShift = 0.05f;
        const int N = 240;
        static readonly string[] Sides = { "Left", "Right" };
        // RobotLegIK (legs_gate_v2 LIK1 SwingBump: swing a -> b, stance b -> a; PhaseStance: in-game lock windows), per foot L, R
        static readonly Dictionary<string, float[]> SwingStance = new Dictionary<string, float[]>
        {
            ["walking"] = new[] { 0.150f, 0.680f, 0.650f, 0.180f },
            ["run_forward"] = new[] { 0.155f, 0.662f, 0.655f, 0.163f },
            ["run_backward"] = new[] { 0.323f, 0.853f, 0.823f, 0.353f },
        };
        static readonly Dictionary<string, float[]> PhaseStanceWin = new Dictionary<string, float[]>
        {
            ["walking"] = new[] { 0.300f, 0.700f, 0.650f, 0.240f },
            ["run_forward"] = new[] { 0.155f, 0.662f, 0.655f, 0.163f },
            ["run_backward"] = new[] { 0.323f, 0.853f, 0.823f, 0.353f },
        };

        sealed class M { public float lowC, backA, backB, slowA = -1f, slowB = -1f, len, rootSpeed; public string err; }

        // menu: OpenZ/Checks/Legs gate: phase windows per type (EditMode) (OpenZLegGateTypes.PhaseGate)
        public static string Run()
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(Ctl);
            if (!ac) return "FAIL: no " + Ctl;
            var baseClip = new Dictionary<string, AnimationClip>();
            foreach (var cs in ac.layers[0].stateMachine.states)
                if (States.Contains(cs.state.name) && cs.state.motion is AnimationClip c) baseClip[cs.state.name] = c;
            var fails = new List<string>(); var sb = new StringBuilder();
            sb.AppendLine("| type | state | clip | foot | length s | low | back (stance) | slow (ground) | shift vs BS-01 low / back / slow | vs RobotLegIK SwingBump stance / PhaseStance | verdict |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            var baseM = new Dictionary<string, M>();
            foreach (var st in States)
            {
                if (!baseClip.TryGetValue(st, out var bc)) { fails.Add($"BS-01 {st}: no clip on the Base Layer"); continue; }
                for (int f = 0; f < 2; f++)
                {
                    var m = Measure(bc, Sides[f]); baseM[st + f] = m;
                    Row(sb, "BS-01 (grunt, laser)", st, bc, f, m, null, fails);
                }
            }
            var types = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("RobotV3_ t:AnimatorOverrideController", new[] { "Assets/OpenZ/Animation" }))
            {
                var aoc = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(AssetDatabase.GUIDToAssetPath(guid));
                if (!aoc || !aoc.name.StartsWith("RobotV3_")) continue;
                string type = aoc.name.Substring("RobotV3_".Length).ToLowerInvariant(); types.Add(type);
                var ov = new List<KeyValuePair<AnimationClip, AnimationClip>>(aoc.overridesCount); aoc.GetOverrides(ov);
                foreach (var st in States)
                {
                    if (!baseClip.TryGetValue(st, out var bc)) continue;
                    var clip = ov.Where(kv => kv.Key == bc && kv.Value).Select(kv => kv.Value).FirstOrDefault();
                    if (!clip) { sb.AppendLine($"| {type} | {st} | (BS-01 clip, same windows) | - | - | - | - | - | - | - | ok |"); continue; }
                    for (int f = 0; f < 2; f++) Row(sb, type, st, clip, f, Measure(clip, Sides[f]), baseM.TryGetValue(st + f, out var bm) ? bm : null, fails);
                }
            }
            var skipped = types.Where(t => RobotLegIK.ProcSwingSkip.Contains(t)).ToList();
            string result = (fails.Count == 0 ? "PASS" : "FAIL: " + string.Join("; ", fails.Take(12)))
                          + (skipped.Count > 0 ? $" (skipped by ProcSwingSkip: {string.Join(", ", skipped)})" : "");
            var head = new StringBuilder();
            head.AppendLine($"RESULT: {result}\n");
            head.AppendLine($"# Legs gate: phase windows per type (EditMode) {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            head.AppendLine($"Override controllers: {string.Join(", ", types)} (grunt and laser play RobotBS01.controller's own clips). A v3 clip passes when its low / back / slow edges are within {MaxShift} of a cycle of the BS-01 clip it replaces (the clip RobotLegIK's SwingBump / PhaseStance windows were measured on). " +
                            $"Fallback for a FAIL: OpenZ.RobotLegIK.ProcSwingSkip.Add(\"[type]\") and re-run (README 4.2 / section 6): a skipped type keeps its clip boot height, gets no phase lock and is not a fail. RobotLegIK.ProcSwingSkip now: [{string.Join(", ", RobotLegIK.ProcSwingSkip)}]\n");
            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records"));
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "leg_phase_gate.md"), head.ToString() + sb.ToString());
            return result;
        }

        /// <summary>
        /// PhaseStance lock), so its shift is printed but is not a fail.</summary>
        public const string SkipVerdict = "skipped: ProcSwingSkip (clip boot height, no phase locks)";

        static void Row(StringBuilder sb, string type, string st, AnimationClip clip, int f, M m, M bm, List<string> fails)
        {
            string foot = f == 0 ? "L" : "R";
            bool skip = bm != null && RobotLegIK.ProcSwingSkip.Contains(type);
            if (m.err != null)
            {
                if (!skip) fails.Add($"{type} {st} {foot}: {m.err}");
                sb.AppendLine($"| {type} | {st} | {clip.name} | {foot} | - | - | - | - | - | - | {(skip ? SkipVerdict + "; " + m.err : "FAIL: " + m.err)} |"); return;
            }
            var sw = SwingStance[st]; var ps = PhaseStanceWin[st];
            float dSwing = Mathf.Max(Cyc(m.backA, sw[f * 2]), Cyc(m.backB, sw[f * 2 + 1]));
            string dPhase = m.slowA >= 0f ? Mathf.Max(Cyc(m.slowA, ps[f * 2]), Cyc(m.slowB, ps[f * 2 + 1])).ToString("0.000") : "-";
            string shift = "-", verdict = "reference";
            if (bm != null)
            {
                float dl = Cyc(m.lowC, bm.lowC), db = Mathf.Max(Cyc(m.backA, bm.backA), Cyc(m.backB, bm.backB));
                float ds = m.slowA >= 0f && bm.slowA >= 0f ? Mathf.Max(Cyc(m.slowA, bm.slowA), Cyc(m.slowB, bm.slowB)) : 0f;
                shift = $"{dl:0.000} / {db:0.000} / {(m.slowA >= 0f && bm.slowA >= 0f ? ds.ToString("0.000") : "-")}";
                float worst = Mathf.Max(dl, Mathf.Max(db, ds));
                verdict = skip ? $"{SkipVerdict}; shift {worst:0.000}" : worst <= MaxShift ? "ok" : $"FAIL: shift {worst:0.000} > {MaxShift}";
                if (worst > MaxShift && !skip) fails.Add($"{type} {st} {foot} shift {worst:0.000} (ProcSwingSkip.Add(\"{type}\"))");
            }
            sb.AppendLine($"| {type} | {st} | {clip.name} | {foot} | {m.len:0.000} | {m.lowC:0.000} | {m.backA:0.000}-{m.backB:0.000} | {(m.slowA >= 0f ? $"{m.slowA:0.000}-{m.slowB:0.000}" : $"- (root speed {m.rootSpeed:0.000})")} | {shift} | {dSwing:0.000} / {dPhase} | {verdict} |");
        }

        static float Cyc(float a, float b) { float d = Mathf.Repeat(a - b, 1f); return Mathf.Min(d, 1f - d); }

        /// <summary>Longest cyclic run of `mask` that contains sample c: start sample a, length n (n = N when all are set).</summary>
        static bool RunAround(bool[] mask, int c, out int a, out int n)
        {
            int len = mask.Length; a = c; n = 0;
            if (!mask[c]) return false;
            if (mask.All(x => x)) { a = 0; n = len; return true; }
            while (mask[(a - 1 + len) % len]) a = (a - 1 + len) % len;
            int b = c; while (mask[(b + 1) % len]) b = (b + 1) % len;
            n = (b - a + len) % len + 1;
            return true;
        }

        static int Nearest(bool[] mask, int c)
        {
            int len = mask.Length;
            for (int d = 0; d < len / 2; d++) { if (mask[(c + d) % len]) return (c + d) % len; if (mask[(c - d + len) % len]) return (c - d + len) % len; }
            return -1;
        }

        static M Measure(AnimationClip clip, string side)
        {
            var m = new M(); var c = new Dictionary<string, AnimationCurve>();
            foreach (var b in AnimationUtility.GetCurveBindings(clip)) c[b.propertyName] = AnimationUtility.GetEditorCurve(clip, b);
            string k = side + "FootT.";
            if (!c.ContainsKey(k + "x") || !c.ContainsKey(k + "y") || !c.ContainsKey(k + "z")) { m.err = $"no {side}FootT curves"; return m; }
            float len = Mathf.Max(clip.length, 1e-3f); m.len = len; float dt = len / N;
            bool root = c.ContainsKey("RootT.x") && c.ContainsKey("RootT.z");
            bool rq = c.ContainsKey("RootQ.x") && c.ContainsKey("RootQ.y") && c.ContainsKey("RootQ.z") && c.ContainsKey("RootQ.w");
            Vector3 Foot(float t) => new Vector3(c[k + "x"].Evaluate(t), c[k + "y"].Evaluate(t), c[k + "z"].Evaluate(t));
            Vector3 RootT(float t) => root ? new Vector3(c["RootT.x"].Evaluate(t), c.TryGetValue("RootT.y", out var y) ? y.Evaluate(t) : 0f, c["RootT.z"].Evaluate(t)) : Vector3.zero;
            Quaternion RootQ(float t) { if (!rq) return Quaternion.identity; var q = new Quaternion(c["RootQ.x"].Evaluate(t), c["RootQ.y"].Evaluate(t), c["RootQ.z"].Evaluate(t), c["RootQ.w"].Evaluate(t)); float n = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w); return n > 1e-6f ? new Quaternion(q.x / n, q.y / n, q.z / n, q.w / n) : Quaternion.identity; }
            var cycle = RootT(len) - RootT(0f);
            Vector3 World(float t)
            {   // the boot over the ground: root + body-space goal, continued over the loop by the root's cycle displacement
                Vector3 off = Vector3.zero;
                while (t > len) { t -= len; off += cycle; }
                while (t < 0f) { t += len; off -= cycle; }
                return RootT(t) + RootQ(t) * Foot(t) + off;
            }
            var P = new Vector3[N];
            for (int i = 0; i < N; i++) P[i] = Foot(len * i / N);
            int im = 0; float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < N; i++) { if (P[i].y < lo) { lo = P[i].y; im = i; } hi = Mathf.Max(hi, P[i].y); }
            var low = P.Select(p => p.y < lo + 0.1f * (hi - lo)).ToArray();
            RunAround(low, im, out int la, out int ln);
            m.lowC = Mathf.Repeat((la + ln * 0.5f) / N, 1f);
            var vz = new float[N]; for (int i = 0; i < N; i++) vz[i] = P[(i + 1) % N].z - P[(i - 1 + N) % N].z;
            float sgn = 0f; for (int d = -3; d <= 3; d++) sgn += vz[(im + d + N) % N];
            var back = vz.Select(v => v * sgn > 0f).ToArray();
            int bc = back[im] ? im : Nearest(back, im);
            if (bc < 0 || !RunAround(back, bc, out int ba, out int bn)) { m.err = "no stance (back-moving) window"; return m; }
            m.backA = (float)ba / N; m.backB = Mathf.Repeat((float)(ba + bn) / N, 1f);
            var xz = new Vector2(cycle.x, cycle.z); m.rootSpeed = xz.magnitude / len;
            if (root && m.rootSpeed > 1e-3f)
            {
                var sp = new float[N]; int ismin = 0;
                for (int i = 0; i < N; i++)
                {
                    float t = len * i / N; var d = World(t + dt) - World(t - dt);
                    sp[i] = new Vector2(d.x, d.z).magnitude / (2f * dt); if (sp[i] < sp[ismin]) ismin = i;
                }
                var slow = sp.Select(s => s < 0.25f * m.rootSpeed).ToArray();
                int sc = slow[im] ? im : slow[ismin] ? ismin : -1;
                if (sc >= 0 && RunAround(slow, sc, out int sa, out int sn)) { m.slowA = (float)sa / N; m.slowB = Mathf.Repeat((float)(sa + sn) / N, 1f); }
            }
            return m;
        }
    }
}
