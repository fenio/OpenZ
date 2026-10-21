using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Dead robots read as dead, also zoomed out (items 135 and 228: the colour should "slowly fade ... not so bright, intensive ...
    /// more grey, dead colour, still reading as team colour, not darker"). A dead robot's WHOLE figure, team paint and textured body
    /// alike (the heroes have no team paint), is drawn greyer by the unit shader (TeamLit): every pixel's lit colour is mixed towards
    /// the grey of its own brightness, lifted a little (_OZDead per renderer: 0 alive .. 1 dead; Grey = how far towards that grey at 1,
    /// Lift = the grey's brightness over the colour's own). Hue kept, saturation about 45 % of the live one, brightness kept (never
    /// darker: the lift makes it a touch lighter). The fade is SLOW: Seconds after the death the figure shows the full dead colour, in
    /// Steps steps. The body (a full ragdoll, a melted robot, a thrown crew body, a hero): OzGlowFade raises TeamPaint.DeadLevel, which
    /// sets _OZDead on the body's renderers; a figure without a TeamPaint gets Set on its renderers. A robot shot to bits: its thrown
    /// parts, chunks and the pop's own pieces are stepped by RobotPopDeath from their age (PartBegin, Set). Vehicle, gun and building
    /// wrecks are not touched (they keep their charring). Off = the old look (checks only).
    /// The colour maths here (Paint) is the shader's, for the checks, the log and the previews.
    /// </summary>
    public static class OzDeadTint
    {
        public static bool On = true;
        /// <summary>Seconds from the death to the full dead colour, and the steps of the fade.</summary>
        public static float Seconds = 2.5f;
        public static int Steps = 16;
        /// <summary>How far the lit colour goes towards its own grey at full death (0.55: saturation about 45 % of the live one) and that
        /// grey's brightness as a multiple of the colour's own (1.15: a touch lighter, never darker). The shader has the same two numbers.</summary>
        public const float Grey = 0.55f, Lift = 1.15f;
        /// <summary>The old rule's numbers (sRGB HSV saturation x 0.45, value kept), kept for the log of the K18 check only.</summary>
        public static float Saturation = 0.45f, Value = 1f;

        /// <summary>TeamLit: _OZDead, 0 alive .. 1 dead, per renderer.</summary>
        public static readonly int DeadId = Shader.PropertyToID("_OZDead");
        static MaterialPropertyBlock mpb;
        /// <summary>Thrown robot parts / chunks / pieces given the fade, and those that reached the full dead colour (for the checks).</summary>
        public static int PartsTinted, PartsFaded;
        /// <summary>Pops whose team-coloured pieces (shoulder domes, the team share of the armour plates) got the fade, and the last such
        /// colour live and as it looks dead (sRGB, for the checks).</summary>
        public static int PopColoursTinted;
        public static Color LastPieceLive, LastPieceDead;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { PartsTinted = PartsFaded = PopColoursTinted = 0; }

        /// <summary>A linear colour as the dead figure shows it at dead 0 .. 1: the shader's maths (its luminance x Lift as the grey,
        /// mixed Grey x dead of the way).</summary>
        public static Color Paint(Color linear, float dead)
        {
            if (!On || dead <= 0f) return linear;
            dead = Mathf.Clamp01(dead);
            float y = 0.2126f * linear.r + 0.7152f * linear.g + 0.0722f * linear.b;
            float t = dead * Grey, g = y * Lift;
            return new Color(Mathf.Lerp(linear.r, g, t), Mathf.Lerp(linear.g, g, t), Mathf.Lerp(linear.b, g, t), linear.a);
        }

        /// <summary>The same turn on a colour given as it is drawn (sRGB).</summary>
        public static Color PaintSrgb(Color srgb, float dead)
        {
            if (!On || dead <= 0f) return srgb;
            var o = Paint(srgb.linear, dead).gamma;
            o.a = srgb.a;
            return o;
        }

        /// <summary>RobotPopDeath: the team colour of the pop's own pieces (teamC, sRGB) is set LIVE on the pieces; their fade comes from
        /// _OZDead on their renderers (Set, from the piece's age). Counts the pop and keeps the colour live / dead for the checks.</summary>
        public static Color PopPieceLive(Color srgb)
        {
            LastPieceLive = srgb; LastPieceDead = PaintSrgb(srgb, 1f); PopColoursTinted++;
            return srgb;
        }

        /// <summary>The vivid accent colour (linear xyz, w = the owned flag) as the dead figure shows it (the shader does this on the
        /// final colour; here for the log).</summary>
        public static Vector4 Vivid(Vector4 viv, float dead)
        {
            if (!On || dead <= 0f || (viv.x <= 0f && viv.y <= 0f && viv.z <= 0f)) return viv;
            var c = Paint(new Color(viv.x, viv.y, viv.z, 1f), dead);
            return new Vector4(c.r, c.g, c.b, viv.w);
        }

        /// <summary>The live team colour of a robot body as TeamPaint draws it (linear, its robot gain and its charring).</summary>
        public static Color LiveRobotPaint(TeamPaint tp)
        {
            Color c = TeamColors.Paint(tp.Team).linear;
            if (tp.Team == Team.Neutral) { c *= 3.0f; c.a = 1; }
            float k = 1f - 0.78f * tp.Charred, gain = TeamPaint.RobotTeamGain;
            return new Color(c.r * k * gain, c.g * k * gain, c.b * k * gain, c.a * k);
        }

        /// <summary>Sets the dead level (0 alive .. 1 dead) on one renderer: in its own property block and in every per-material block it
        /// has, keeping what else is in them (a thrown part's copied team paint, a piece's colour); a renderer without any block gets one.</summary>
        public static void Set(Renderer r, float dead)
        {
            if (!r) return;
            mpb ??= new MaterialPropertyBlock();
            float d = On ? Mathf.Clamp01(dead) : 0f;
            bool any = false;
            r.GetPropertyBlock(mpb);
            if (!mpb.isEmpty) { mpb.SetFloat(DeadId, d); r.SetPropertyBlock(mpb); any = true; }
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                r.GetPropertyBlock(mpb, i);
                if (mpb.isEmpty) continue;
                mpb.SetFloat(DeadId, d); r.SetPropertyBlock(mpb, i); any = true;
            }
            if (!any) { mpb.Clear(); mpb.SetFloat(DeadId, d); r.SetPropertyBlock(mpb); }
        }

        /// <summary>RobotPopDeath: a thrown part / chunk copied from a robot body starts its fade at the live colour (its blocks are the
        /// body's copies, level 0 now); RobotPopDeath steps it with Set from the part's age.</summary>
        public static void PartBegin(Renderer part)
        {
            if (!part) return;
            Set(part, 0f);
            PartsTinted++;
        }

        /// <summary>Saturation and value (sRGB HSV) of a linear colour, for the checks and the log.</summary>
        public static Vector2 SatVal(Color linear) { Color.RGBToHSV(linear.gamma, out _, out float s, out float v); return new Vector2(s, v); }
        /// <summary>Saturation and value (HSV) of a colour given as drawn (sRGB), for the checks.</summary>
        public static Vector2 SatValSrgb(Color srgb) { Color.RGBToHSV(srgb, out _, out float s, out float v); return new Vector2(s, v); }
    }
}
