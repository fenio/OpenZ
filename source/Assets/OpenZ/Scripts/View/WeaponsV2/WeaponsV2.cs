using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// One static entry point for the few lines patched into the live views:
    /// <list type="bullet">
    /// <item>CombatView.DrawShot: SkipShot (the sniper's aimed cadence), OwnsRecoil, Shot (grunt / psycho / laser / sniper are drawn by
    /// WeaponsV2Runner at execution order 96, after WeaponHold (80) and BS01TypeKit (85) posed the weapon, so every round leaves the
    /// real muzzle socket of the drawn gun).</item>
    /// <item>CombatView.MissileFired: HoldMissile (only with deviation DV11 off = Zod exact: the Tough raises and settles before the first
    /// rocket, which then flies the remaining time so it still lands on the sim's blast; with DV11 on, the default, the SIM gives the
    /// first rocket from the carry 0.8 s of prepare, ZObject.ToughPrepare.cs, and the view is steady by then) and LaunchTough (rocket
    /// from the tube's Muzzle socket, back-blast from its BackBlast socket, after WeaponHold).</item>
    /// <item>WeaponHold: HoldAim / HoldKick (the Tough's carry / raise / ballistic aim / recoil climb and shove, the MG and rifle kicks,
    /// the sniper's steady aim) via RobotWeaponPose.</item>
    /// <item>UnitAim.BindRobot: Bs01TorsoAim (the BS-01 weapon's socket is called Muzzle, so the torso aim and the ballistic aim never
    /// bound for grunt / psycho / sniper / tough / pyro).</item>
    /// </list>
    /// The sim is untouched except the listed deviation DV11 (Tough prepare time, parity report Deviations block, toggle oz_tough_prepare):
    /// damage, range, hit chance, cooldowns are Zod's; everything else here only changes what is drawn and heard.
    /// </summary>
    public static class WeaponsV2
    {
        /// <summary>Master switch (off = the pre-weapons_v2 look everywhere).</summary>
        public static bool Enabled = true;
        /// <summary>UnitAim binds the BS-01 weapon's `Muzzle` socket (torso aim, ballistic bazooka aim, turn-before-fire on the gun).</summary>
        public static bool Bs01TorsoAim = true;
        internal static bool replaying;

        // ------------------------------------------------------------------------------------------ audio (CombatAudio keys + banks)
        public static readonly string[] BankMg = { "wv2_mg_1", "wv2_mg_2", "wv2_mg_3", "wv2_mg_4", "wv2_mg_5", "wv2_mg_6" };
        public static readonly string[] BankSniper = { "wv2_sniper_a", "wv2_sniper_b", "wv2_sniper_c" };
        public static readonly string[] BankSniperEcho = { "wv2_sniper_echo_a", "wv2_sniper_echo_b", "wv2_sniper_echo_c" };
        public static readonly string[] BankLaserFire = { "wv2_laser_fire_a", "wv2_laser_fire_b", "wv2_laser_fire_c", "wv2_laser_fire_d", "electro-zip-3", "s3_laser_gun", "s3_lazer_1", "s3_lazer_2", "s3_lazer_3" };
        public static readonly string[] BankLaserZap = { "wv2_laser_zap_1", "wv2_laser_zap_2", "wv2_laser_zap_3", "wv2_laser_zap_4", "wv2_laser_zap_5",
                                                         "laser-impact-electric-a", "laser-impact-electric-b" };
        public static readonly string[] BankBackblast = { "wv2_backblast_a", "wv2_backblast_b", "wv2_backblast_c" };
        public static readonly string[] BankServo = { "sd_robot_servo_a", "sd_robot_servo_b", "sd_robot_servo_c" };
        public static readonly string[] BankThud = { "sd_seat_thud_a", "sd_seat_thud_b" };
        /// <summary>
        /// </summary>
        public static readonly string[] BankRocket = { "rocket", "s3_rocket_b", "s3_rocket_c", "s3_rocket_d", "s3_rocket_e" };

        // ------------------------------------------------------------------------------------------ no immediate repeats
        static readonly Dictionary<string[], int> lastPick = new Dictionary<string[], int>();
        static readonly Dictionary<string, string[]> single = new Dictionary<string, string[]>();
        /// <summary>One clip of the bank as a cached one-name array, never the one picked last time (CombatAudio then pitch-varies it
        /// +-3.5 %).</summary>
        public static string[] Pick(string[] bank)
        {
            if (bank == null || bank.Length <= 1) return bank;
            int last = lastPick.TryGetValue(bank, out var l) ? l : -1;
            int i = UnityEngine.Random.Range(0, last < 0 ? bank.Length : bank.Length - 1);
            if (last >= 0 && i >= last) i++;
            lastPick[bank] = i;
            var n = bank[i];
            if (!single.TryGetValue(n, out var arr)) single[n] = arr = new[] { n };
            return arr;
        }
        /// <summary>
        /// hear the psycho apart from the grunt; the echo sits well under the crack).</summary>
        public const float GainMg = 0.62f, GainSniper = 0.8f, GainSniperEcho = 0.42f, GainLaserFire = 0.72f, GainLaserZap = 0.6f,
            GainBackblast = 0.55f, GainServo = 0.32f, GainThud = 0.4f;
        /// <summary>
        /// that fix (it played x1 = 6 dB under the live tuning).</summary>
        public const float GainRocket = LegacyMatch.GainDefault * 2f;

        /// <summary>Integrated LUFS of every new clip (tools/clips.json, ffmpeg ebur128 looped x4 like CombatAudio.ClipLufs).</summary>
        static readonly Dictionary<string, float> Lufs = new Dictionary<string, float>
        {
            ["wv2_mg_1"] = -15.0f, ["wv2_mg_2"] = -13.4f, ["wv2_mg_3"] = -14.4f, ["wv2_mg_4"] = -13.6f, ["wv2_mg_5"] = -15.1f, ["wv2_mg_6"] = -13.7f,
            ["wv2_sniper_a"] = -17.8f, ["wv2_sniper_b"] = -13.4f, ["wv2_sniper_c"] = -10.6f,
            ["wv2_sniper_echo_a"] = -16.5f, ["wv2_sniper_echo_b"] = -20.1f, ["wv2_sniper_echo_c"] = -19.4f,
            ["wv2_laser_fire_a"] = -9.3f, ["wv2_laser_fire_b"] = -9.2f, ["wv2_laser_fire_c"] = -9.3f, ["wv2_laser_fire_d"] = -9.3f,
            ["wv2_laser_zap_1"] = -10.1f, ["wv2_laser_zap_2"] = -13.4f, ["wv2_laser_zap_3"] = -10.7f, ["wv2_laser_zap_4"] = -13.6f, ["wv2_laser_zap_5"] = -12.1f,
            ["wv2_backblast_a"] = -15.4f, ["wv2_backblast_b"] = -8.3f, ["wv2_backblast_c"] = -15.4f,
        };
        /// <summary>Sound-pack clips (Resources/OpenZ/Audio/Sfx) CombatAudio does not load by itself.</summary>
        static readonly Dictionary<string, float> PackLufs = new Dictionary<string, float>
        {
            ["sd_robot_servo_a"] = -11.4f, ["sd_robot_servo_b"] = -12.4f, ["sd_robot_servo_c"] = -14.2f, ["sd_seat_thud_a"] = -17.6f, ["sd_seat_thud_b"] = -22.4f,
            ["laser-hit-zap-1"] = -13.3f, ["laser-hit-zap-2"] = -11.4f, ["rocket-2"] = -8.3f, ["mech-rocket-2"] = -14.8f,
        };

        /// <summary>
        /// one bank still differed by up to 5.4 dB after it: the Tough bank played rocket at -10.0, rocket-2 at -12.2 and mech-rocket-2
        /// at -15.4 LUFS effective, so every third rocket fell back under the x2 loudness fix; the sniper cracks spread 4.5 dB,
        /// the zaps 4.9 dB, the back-blasts 3.3 dB. EvenGain = the extra gain that brings the picked take to the bank's reference
        /// (refClip, else the mean of the bank), clamped to +-6 dB; 1 when a clip's loudness is unknown.</summary>
        public static float EvenGain(string[] pick, string[] bank, string refClip = null)
        {
            if (pick == null || pick.Length == 0 || bank == null || bank.Length < 2) return 1f;
            float e = Effective(pick[0]); if (float.IsNaN(e)) return 1f;
            float r;
            if (refClip != null) r = Effective(refClip);
            else
            {
                float sum = 0f; int n = 0;
                foreach (var c in bank) { float x = Effective(c); if (!float.IsNaN(x)) { sum += x; n++; } }
                r = n > 0 ? sum / n : float.NaN;
            }
            if (float.IsNaN(r)) return 1f;
            return Mathf.Clamp(Mathf.Pow(10f, (r - e) / 20f), 0.5f, 2f);
        }

        /// <summary>Integrated LUFS of a clip after CombatAudio.LevelTrim (NaN = unknown).</summary>
        static float Effective(string clip)
        {
            if (clip == null) return float.NaN;
            if (!CombatAudio.ClipLufs.TryGetValue(clip, out float l) && !Lufs.TryGetValue(clip, out l) && !PackLufs.TryGetValue(clip, out l)) return float.NaN;
            var au = CombatAudio.I;
            float refL = au ? au.levelRefLufs : -16f, ratio = au ? Mathf.Max(1f, au.levelRatio) : 2f;
            return l <= refL ? l : refL + (l - refL) / ratio;
        }

        /// <summary>Before the first scene: CombatAudio.Awake loads CombatAudio.Lifts (res, LUFS) into its clip table, so every
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterClips()
        {
            foreach (var kv in Lufs) CombatAudio.Lifts[kv.Key] = ("OpenZ/Audio/Sfx/WeaponsV2/" + kv.Key, kv.Value);
            foreach (var kv in PackLufs) if (!CombatAudio.Lifts.ContainsKey(kv.Key)) CombatAudio.Lifts[kv.Key] = ("OpenZ/Audio/Sfx/" + kv.Key, kv.Value);
        }

        /// <summary>True when every weapons_v2 clip is loaded in the live CombatAudio (WeaponsV2Check).</summary>
        public static int MissingClips(CombatAudio au, List<string> into = null)
        {
            if (!au) return -1;
            int n = 0;
            foreach (var k in Lufs.Keys) if (!au.HasClip(k)) { n++; into?.Add(k); }
            foreach (var k in PackLufs.Keys) if (!au.HasClip(k)) { n++; into?.Add(k); }
            return n;
        }

        // ------------------------------------------------------------------------------------------ hooks (CombatView)
        static bool Robot(ZObject a) => a != null && a.Kind == ZKind.Robot;

        /// <summary>
        /// cannon driver (e.flag2 = a snipe roll, the driver pool is empty now; ZWorld.UpdateObjectDriverHealth disengages every
        /// attacker that same tick, so Zod's client fire clock often never fell due again and the snipe had no visible shot, and the
        /// sniper's aimed cadence made that worse). CombatView.ArmFireClock (edit CV5), the sniper gate and the MG use it.</summary>
        public static bool KillRoll(ZEvent e, ZObject t) =>
            t != null && (t.Destroyed || (Enabled && e.flag && e.flag2 && t.Kind != ZKind.Robot && t.Drivers.Count == 0));

        /// <summary>CombatView.DrawShot, before a visible hitscan shot is counted: true = the sniper is still aiming, draw nothing
        /// (no tracer, no sound, no recoil). Zod's damage rolls go on every 0.4 s in the sim; the killing roll always shows.</summary>
        public static bool SkipShot(ZEvent e, ZObject a, ZObject t, UnitView av)
        {
            if (!Enabled || !Robot(a) || a.Type != "sniper" || !av) return false;
            var r = WeaponsV2Runner.Ensure(); if (!r) return false;
            return !r.SniperAllow(av, e, t);
        }

        /// <summary>CombatView.DrawShot: the runner calls UnitView.OnFire itself when the round actually leaves (robots of these types).</summary>
        public static bool OwnsRecoil(ZObject a) => Enabled && Robot(a) && (a.Type == "grunt" || a.Type == "psycho" || a.Type == "sniper" || a.Type == "laser")
                                                  && WeaponsV2Runner.Ensure();

        /// <summary>CombatView.DrawShot: grunt bolts, psycho MG bursts, laser bolts, sniper cracks are drawn by the runner this frame
        /// after WeaponHold (true = handled). APC crews of these types keep their type's look from the APC gun.</summary>
        public static bool Shot(CombatView cv, ZEvent e, ZObject a, ZObject t, string type, UnitView av, Vector3 from, Vector3 to)
        {
            if (!Enabled || a == null || t == null) return false;
            if (type != "grunt" && type != "psycho" && type != "laser" && type != "sniper") return false;
            var r = WeaponsV2Runner.Ensure(cv); if (!r) return false;
            r.QueueShot(e, a, t, type, av, from, to);
            return true;
        }

        /// <summary>CombatView.MissileFired, before UnitAim's hold: the Tough's first rocket waits until the bazooka is raised and
        /// settled (at most 0.6 x the flight; CombatView.Replay then launches it with the remaining flight time). With DV11 on the sim's
        /// rocket already comes 0.8 s after the engage and the tube is ready: nothing is held (WeaponsV2Check: 0 holds).</summary>
        public static bool HoldMissile(CombatView cv, UnitView av, ZEvent e, ZObject a, ZObject t)
        {
            if (!Enabled || replaying || e.flag || !Robot(a) || a.Type != "tough" || !av) return false;
            var r = WeaponsV2Runner.Ensure(cv); return r && r.HoldTough(av, e, a);
        }

        /// <summary>CombatView.MissileFired: the Tough's rocket + back-blast + recoil leave the tube sockets after WeaponHold (true =
        /// handled; APC Tough crews keep the old launch).</summary>
        public static bool LaunchTough(UnitView av, ZObject a, ZEvent e, Vector3 to, float delay)
        {
            if (!Enabled || !Robot(a) || !av) return false;
            var r = WeaponsV2Runner.Ensure(); return r && r.QueueLaunch(av, e, a, to, delay);
        }

        // ------------------------------------------------------------------------------------------ hooks (WeaponHold)
        /// <summary>WeaponHold.LateUpdate: the weapon's aim direction after RobotWeaponPose (Tough carry / raise / ballistic aim /
        /// recoil climb, MG and rifle kicks, sniper sway).</summary>
        public static Vector3 HoldAim(WeaponHold h, Vector3 aim)
        {
            if (!Enabled || !h || !h.TryGetComponent(out RobotWeaponPose p) || !p.isActiveAndEnabled) return aim;
            return p.ModifyAim(aim);
        }

        /// <summary>WeaponHold.ShoulderHold: world offset of the bazooka's rest point (recoil shove along the tube).</summary>
        public static Vector3 HoldKick(WeaponHold h, Vector3 aim)
        {
            if (!Enabled || !h || !h.TryGetComponent(out RobotWeaponPose p) || !p.isActiveAndEnabled) return Vector3.zero;
            return p.KickOffset(aim);
        }
    }
}
