using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// the kick of the unit that fires must read at the RTS camera (~55-70 m, ~20 px per metre at 2560x1600). The authored peaks
    /// were rifle 7.7 cm + body jolt 3.3 cm (tough 13 / 6.6 cm), tank turret 33 cm + barrels 38 cm + hull 2.75 deg: the robots'
    /// 1-2 px did not read. Per weapon class (bigger gun = bigger kick), the gains below scale UnitView.ApplyRecoil's weapon shove,
    /// body jolt and hull tilt, add a body rock about the feet for the robots (the 3.5 m BS-01's head moves ~4-6 px at the peak)
    /// and scale RobotWeaponPose's muzzle climb (OnRound). Same envelopes and timing (UnitView.Recoil / RobotWeaponPose.Kick), so
    /// the kick still starts on the shot frame; no sim change. The tough keeps its own 7 deg body rock (RobotWeaponPose /
    /// BS01Stance), the pyro its near-zero flamer kick (pyro_fire_v2), the crane's hatch rifle never jolts its hull (NB24); tank /
    /// cannon barrel slides (UnitAim) are unchanged (33-38 cm already read).
    /// Peaks (envelope peak 1.13; menu "Recoil: report" prints the live table): grunt rifle 16.2 cm + body 8.8 cm + 3.4 deg rock;
    /// psycho 12.7 / 6.1 cm + 1.8 deg per kick; sniper 17.4 / 10.8 cm + 5.1 deg; laser 12.7 / 6.8 cm + 2.5 deg; tough 19.0 /
    /// 12.2 cm (+ its own rock); light tank hull 14.9 cm + 4.5 deg; medium 14.0 cm + 4.2 deg; heavy 18.4 cm + its own spring rock
    /// x1.15; jeep 12.2 cm + 3.8 deg; MLRS unchanged (its own rack kick); cannons 11.8 cm + 3.5 deg (tower guns unchanged: they
    /// sit on the ring mount).
    /// </summary>
    public static class RecoilFeel
    {
        /// <summary>Options switch (Batch C menu): off = the authored kicks as before.</summary>
        public static bool Enabled = true;
        /// <summary>One knob over every gain above 1 (1 = the table; 0 = the authored kicks).</summary>
        public static float Gain = 1f;

        static float G(float k) => Enabled ? 1f + (k - 1f) * Gain : 1f;

        /// <summary>Robot weapon shove (UnitView.ApplyRecoil: weapon.position).</summary>
        public static float Weapon(string type)
        {
            switch (type)
            {
                case "grunt": return G(2.05f);
                case "psycho": return G(1.6f);
                case "sniper": return G(2.2f);
                case "laser": return G(1.6f);
                case "tough": return G(1.4f);
                default: return 1f;                                                                // pyro, the rest
            }
        }

        /// <summary>Robot body jolt back along the facing (UnitView.ApplyRecoil: transform.position).</summary>
        public static float Body(string type)
        {
            switch (type)
            {
                case "grunt": return G(2.6f);
                case "psycho": return G(1.8f);
                case "sniper": return G(3.2f);
                case "laser": return G(2.0f);
                case "tough": return G(1.8f);
                default: return 1f;
            }
        }

        /// <summary>Robot body rock (deg at envelope 1) about the feet, the top away from the aim. 0 = none.</summary>
        public static float RockDeg(string type)
        {
            if (!Enabled) return 0f;
            switch (type)
            {
                case "grunt": return 3f * Gain;
                case "psycho": return 1.6f * Gain;
                case "sniper": return 4.5f * Gain;
                case "laser": return 2.2f * Gain;
                default: return 0f;                                                                // tough: its own 7 deg; pyro: none
            }
        }

        /// <summary>Muzzle climb (RobotWeaponPose.OnRound).</summary>
        public static float Climb(string type)
        {
            switch (type)
            {
                case "grunt": return G(1.6f);
                case "psycho": return G(1.4f);
                case "sniper": return G(1.5f);
                case "laser": return G(1.3f);
                default: return 1f;
            }
        }

        /// <summary>Vehicle / cannon hull jolt back (UnitView.ApplyRecoil).</summary>
        public static float Hull(string type, ZKind kind)
        {
            switch (type)
            {
                case "light": return G(1.65f);
                case "medium": return G(1.55f);
                case "heavy": return G(1.25f);
                case "jeep": return G(1.35f);
                case "apc": return G(1.3f);
                case "missile_launcher": return 1f;
                default: return kind == ZKind.Cannon ? G(1.3f) : 1f;
            }
        }

        /// <summary>Vehicle / cannon hull tilt (deg multiplier; the heavy's spring rock included).</summary>
        public static float HullTilt(string type, ZKind kind)
        {
            switch (type)
            {
                case "light": return G(1.6f);
                case "medium": return G(1.5f);
                case "heavy": return G(1.15f);
                case "jeep": return G(1.35f);
                case "apc": return G(1.3f);
                case "missile_launcher": return 1f;
                default: return kind == ZKind.Cannon ? G(1.25f) : 1f;
            }
        }

        public static string Report()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"RecoilFeel: enabled {Enabled}, gain {Gain:F2}; peak = envelope 1.13 x authored x gain\n");
            foreach (var t in new[] { "grunt", "psycho", "sniper", "laser", "tough", "pyro" })
            {
                float auW = t == "tough" ? 0.12f : 0.07f, auB = t == "tough" ? 0.06f : t == "pyro" ? 0.01f : 0.03f;
                sb.Append($"  {t,-7} weapon {auW * 1.13f * 100f:F1} -> {auW * 1.13f * Weapon(t) * 100f:F1} cm, body {auB * 1.13f * 100f:F1} -> {auB * 1.13f * Body(t) * 100f:F1} cm, rock {RockDeg(t) * 1.13f:F1} deg, climb x{Climb(t):F2}\n");
            }
            foreach (var t in new[] { "jeep", "light", "medium", "heavy", "apc", "missile_launcher", "gun" })
            {
                var k = t == "gun" ? ZKind.Cannon : ZKind.Vehicle; float j = t == "heavy" ? 0.13f : 0.08f;
                float tilt = t == "missile_launcher" ? 4f : 2.5f;
                sb.Append($"  {t,-16} hull jolt {j * 1.13f * 100f:F1} -> {j * 1.13f * Hull(t, k) * 100f:F1} cm, tilt x{HullTilt(t, k):F2}{(t == "heavy" ? " (spring rock)" : $" ({tilt * 1.13f:F1} -> {tilt * 1.13f * HullTilt(t, k):F1} deg)")}\n");
            }
            return sb.ToString();
        }
    }

    public partial class UnitView
    {
        /// <summary>
        /// a kneeling / prone sniper rocks a third as much.</summary>
        void RecoilRock(float r)
        {
            if (Obj == null) return;
            float deg = RecoilFeel.RockDeg(Obj.Type);
            if (deg <= 0f || r <= 0.001f) return;
            if (stance && stance.PosesLegs) deg *= 0.33f;
            var aim = Quaternion.Euler(0, AimYaw(), 0) * Vector3.forward;
            transform.rotation = Quaternion.AngleAxis(-deg * r, Vector3.Cross(Vector3.up, aim)) * transform.rotation;   // top away from the aim
        }

        /// <summary></summary>
        public void DebugKick() { if (Obj != null && !Dying) OnFire(); }
    }
}
