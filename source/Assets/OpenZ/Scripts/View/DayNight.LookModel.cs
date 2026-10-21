using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// DayNight's day keys as a brightness measure (OzDayLook) for the travel back to day of the option NIGHT &amp; RAIN (OzDayOnlyClock):
    /// the table is read from Keys once, the level's morning lift and the night tunables are brought up to date on every call.
    /// </summary>
    public partial class DayNight
    {
        OzDayLook lookModel;

        /// <summary>The brightness measure of this level's day keys (cached).</summary>
        public OzDayLook LookModel()
        {
            if (lookModel == null)
            {
                var table = new float[Keys.Length * OzDayLook.Row];
                for (int i = 0; i < Keys.Length; i++)
                {
                    var key = Keys[i]; int o = i * OzDayLook.Row;
                    table[o] = key.t; table[o + 1] = key.elevation; table[o + 2] = key.intensity;
                    Put(table, o + 3, key.sun); Put(table, o + 6, key.sky); Put(table, o + 9, key.equator); Put(table, o + 12, key.ground);
                }
                lookModel = new OzDayLook(table, NightGrade.Night, MorningLiftK);
            }
            bool cool = warmth < 0.999f;
            lookModel.SunLift = cool ? MorningSunLift : 0f;
            lookModel.AmbientLift = cool && MorningAmbientLift > 0f ? MorningAmbientLift : 0f;
            lookModel.MoonScale = NightGrade.MoonScale; lookModel.AmbientScale = NightGrade.AmbientScale; lookModel.Exposure = NightGrade.Exposure;
            return lookModel;
        }

        static void Put(float[] t, int o, Color c) { t[o] = c.r; t[o + 1] = c.g; t[o + 2] = c.b; }
    }
}
