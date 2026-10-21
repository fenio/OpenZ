using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K419a (K401's night line, view only): at night the mark on dark sand read only 12.4 % darker (wanted 14 % or more). The decal
    /// draws out = ground x (1 - a) + tint x a (unlit). Its dark tint was dimmed at night to x(1 - <see cref="NightDim"/>) = x0.12, about
    /// 0.004 in the frame's light: on night sand of about 0.006 that is some 60 % of the ground's own light, so the mark gave most of its
    /// darkening back (the night grade's lift, added after the tone map, dilutes the rest; tools/twin_mark_night.py meets the Play reading
    /// with it). On darker night grass it hardly darkened at all. By day the tint is a few percent of the ground's light and the mark reads
    /// as asked (16-21 %, unchanged).
    /// Now the DARK tint goes all the way to black at night (<see cref="DarkNightDim"/> 1): the mark is then exactly its share of the
    /// ground's light on every ground (sand, grass, road, water), as K401 wanted :
    /// on night sand about 24 % darker at cruise (the twin; 20-28 % with the lift's spread), about 30 % while selected. The red "cannot land" tint keeps <see cref="NightDim"/> (it must stay red at
    /// night), and by day nothing changes (night 0: both tints as before).
    /// </summary>
    public sealed partial class OzShuttleShadow
    {
        /// <summary>The dark mark's tint x(1 - DarkNightDim x night): 1 = black at full night (the mark is then purely the share of the
        /// ground's light it takes away).</summary>
        public static float DarkNightDim = 1f;

        /// <summary>The mark's tint now: the dark one dimmed by <see cref="DarkNightDim"/>, the red one by <see cref="NightDim"/>, blended by rk.</summary>
        public static Color NightTint(float rk, float night)
        {
            float kd = 1f - Mathf.Clamp01(DarkNightDim) * night, kr = 1f - NightDim * night;
            var d = new Color(Dark.r * kd, Dark.g * kd, Dark.b * kd, 1f); var r = new Color(Red.r * kr, Red.g * kr, Red.b * kr, 1f);
            return Color.Lerp(d, r, rk);
        }
    }
}
