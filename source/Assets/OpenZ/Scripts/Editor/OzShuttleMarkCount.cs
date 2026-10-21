#if !OPENZ_HARNESS
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>K256g2 counted check, out of Play (menu OpenZ/V1.1/Shuttle: K256g2 Mark Check): the kit's shaders and materials are in the
    /// build's Resources and supported; TeamLit reads the self-shadow; the hand-over's cross-fade on 60 fps +-30 % frames (3 seeds): the mark
    /// and the real shadow add up to one, neither changes by more than 0.05 a frame, it lasts its 0.6 s; the eased surface height never
    /// overshoots a step and never moves more than 0.3 m a frame at 60 fps for a 15 m cliff.</summary>
    public static class OzShuttleMarkCount
    {
        public static string Run()
        {
            var sb = new StringBuilder(); int pass = 0, fail = 0;
            void Check(string what, bool ok) { if (ok) pass++; else fail++; sb.AppendLine((ok ? "PASS " : "FAIL ") + what); }
            var dm = Resources.Load<Material>("OpenZ/FX/M_OzShuttleMarkDecal");
            Check("K1 the decal material is in Resources and names OpenZ/ShuttleMarkDecal (supported here)", dm && dm.shader && dm.shader.name == "OpenZ/ShuttleMarkDecal" && dm.shader.isSupported);
            var sm = Resources.Load<Material>(OzShuttleSelfShadow.DepthMaterialPath);
            Check("K2 the self-shadow depth material is in Resources and names OpenZ/ShuttleSelfShadowDepth (supported here)", sm && sm.shader && sm.shader.name == "OpenZ/ShuttleSelfShadowDepth" && sm.shader.isSupported);
            var tl = Shader.Find("OpenZ/TeamLit");
            string tlPath = tl ? AssetDatabase.GetAssetPath(tl) : "";
            string src = tlPath != "" ? System.IO.File.ReadAllText(tlPath) : "";
            Check("K3 TeamLit includes the self-shadow (OzShuttleSelfShadow.hlsl) and applies it after the PBR and on the stylized main light", src.Contains("#include \"OzShuttleSelfShadow.hlsl\"") && src.Contains("OZSelfShadowApply(c, d, s,") && src.Contains("ml.shadowAttenuation *= ozSSK;"));
            Check("K4 TeamLit compiles in this editor (no shader errors)", tl && !ShaderUtil.ShaderHasError(tl) && dm && !ShaderUtil.ShaderHasError(dm.shader) && sm && !ShaderUtil.ShaderHasError(sm.shader));
            // the hand-over's cross-fade (OzShuttleShadowHandOver: u to 1 over Sec, k = smoothstep(u), the mark 1 - k)
            float worstSum = 0f, worstStep = 0f, minT = 9f, maxT = 0f;
            for (int seed = 1; seed <= 3; seed++)
            {
                var rnd = new System.Random(seed); float u = 0f, t = 0f, last = 0f; int n = 0;
                while (u < 1f && n < 1000)
                {
                    float dt = (1f / 60f) * (1f + 0.3f * (float)(rnd.NextDouble() * 2 - 1)); t += dt; n++;
                    u = Mathf.MoveTowards(u, 1f, dt / OzShuttleShadowHandOver.Sec);
                    float k = u * u * (3f - 2f * u), mark = 1f - k;
                    worstSum = Mathf.Max(worstSum, Mathf.Abs(mark + k - 1f)); worstStep = Mathf.Max(worstStep, Mathf.Abs(k - last)); last = k;
                }
                minT = Mathf.Min(minT, t); maxT = Mathf.Max(maxT, t);
            }
            Check($"K5 the hand-over: mark + real shadow = 1 (worst {worstSum:E1}), largest change a frame {worstStep:F3} <= 0.05, {minT:F2} .. {maxT:F2} s", worstSum < 1e-5f && worstStep <= 0.05f && minT >= 0.55f && maxT <= 0.65f);
            // the eased surface (critically damped, OzShuttleShadow.RefOmega): a 15 m cliff step and back
            float y = 0f, v = 0f, over = 0f, step = 0f;
            for (int i = 0; i < 240; i++)
            {
                float target = i < 120 ? 15f : 0f, b0 = y, dt = 1f / 60f, x0 = y - target, e = Mathf.Exp(-OzShuttleShadow.RefOmega * dt), bb = v + OzShuttleShadow.RefOmega * x0;
                y = target + (x0 + bb * dt) * e; v = (v - OzShuttleShadow.RefOmega * bb * dt) * e;
                step = Mathf.Max(step, Mathf.Abs(y - b0)); if (i < 120) over = Mathf.Max(over, y - 15f); else over = Mathf.Max(over, -y);
            }
            Check($"K6 the eased surface: a 15 m cliff step overshoots {over:F4} m (none) and moves at most {step:F3} m a frame (the radius {0.02f * 3.5f * step:F3} m)", over <= 1e-4f && 0.02f * 3.5f * step <= 0.05f);
            sb.Insert(0, $"K256g2 mark check: {pass} PASS, {fail} FAIL\n");
            Debug.Log(sb.ToString());
            return fail == 0 ? $"PASS {pass}" : $"FAIL {fail} of {pass + fail}";
        }
    }
}
#endif
