using UnityEngine;

namespace OpenZ
{
    public partial class TeamPaint
    {
        Team easeFrom = Team.Neutral;
        float easeK = 1f;
        static readonly int NeutralPaintId = Shader.PropertyToID("_NeutralPaint");
        static readonly System.Collections.Generic.Dictionary<Material, Vector3> neutralAccent = new System.Collections.Generic.Dictionary<Material, Vector3>();

        /// <summary>K389: 1 = no ease (the paint of <see cref="Team"/>); below 1 an owned paint is drawn this far from the paint of
        /// <see cref="EaseFrom"/> to its own (View/OzShuttleCaptureFx.cs: the shuttle's capture, 0 to 1 over 0.6 s).</summary>
        public float EaseK => easeK;
        public Team EaseFrom => easeFrom;

        /// <summary>K389: draw this paint k of the way from the paint of team `from` to its own (1 = done; repainted at once).</summary>
        public void SetEase(Team from, float k)
        {
            k = Mathf.Clamp01(k);
            if (from == easeFrom && Mathf.Abs(k - easeK) < 1e-4f) return;
            easeFrom = from; easeK = k;
            Apply();
        }

        bool Easing => easeK < 1f && easeFrom != team && team != Team.Neutral;

        /// <summary>The owner colour (_TeamColor, linear, the neutral lift included) between the two teams.</summary>
        Color EasedPaint(Color c)
        {
            if (!Easing) return c;
            Color f = TeamColors.Paint(easeFrom).linear;
            if (easeFrom == Team.Neutral) f *= 3.0f;
            var r = Color.Lerp(f, c, easeK); r.a = c.a;
            return r;
        }

        /// <summary>The vivid accent colour (_OZTeamVivid): its owned flag (w) stays the target's; its colour goes from the accent the
        /// shader draws for the old team (neutral: the material's empty grey, TeamLit's lum(neutral paint x base colour) x 0.35) to the
        /// new one.</summary>
        Vector4 EasedVivid(Vector4 v, Material m)
        {
            if (!Easing) return v;
            Vector3 f = easeFrom == Team.Neutral ? NeutralAccent(m) : (Vector3)TeamColors.VividLinear(easeFrom);
            var c = Vector3.Lerp(f, new Vector3(v.x, v.y, v.z), easeK);
            return new Vector4(c.x, c.y, c.z, v.w);
        }

        /// <summary>The team rim / night glow (_OZTeamGlow) between the two teams (neutral: none).</summary>
        Vector4 EasedGlow(Vector4 g) => Easing ? Vector4.Lerp(TeamColors.GlowLinear(easeFrom), g, easeK) : g;

        static Vector3 NeutralAccent(Material m)
        {
            if (!m) return new Vector3(0.108f, 0.108f, 0.108f);
            if (neutralAccent.TryGetValue(m, out var v)) return v;
            // TeamLit: npaint = SRGBToLinear(_NeutralPaint.rgb) * _BaseColor.rgb (the colour arrives linear and is converted once more);
            // the empty accent = its luminance x 0.35
            Color np = m.HasProperty(NeutralPaintId) ? m.GetColor(NeutralPaintId).linear : new Color(0.70f, 0.68f, 0.62f).linear;
            Color bc = m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId).linear : Color.white;
            float r = Mathf.GammaToLinearSpace(np.r) * bc.r, g = Mathf.GammaToLinearSpace(np.g) * bc.g, b = Mathf.GammaToLinearSpace(np.b) * bc.b;
            float l = (0.2126f * r + 0.7152f * g + 0.0722f * b) * 0.35f;
            v = new Vector3(l, l, l); neutralAccent[m] = v;
            return v;
        }
    }
}
