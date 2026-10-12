using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// zbuildlist.cpp:22-236: which units each producing building may build at each blevel (0..5). Entry 0 is the default
    /// production. FORT_BACK uses the FORT_FRONT list. Level only gates the list; it never changes the build time.
    /// </summary>
    public static class ZBuildLists
    {
        public static readonly string[] Cannons = { "gatling", "gun", "howitzer", "missile_cannon" };
        public static bool IsCannon(string t) => System.Array.IndexOf(Cannons, t) >= 0;
        public static bool IsRobot(string t) => t == "grunt" || t == "psycho" || t == "sniper" || t == "tough" || t == "pyro" || t == "laser" || t == ZElectro.Type;   // electrobot_v1 DV45

        static readonly string[][] Fort =
        {
            new[] { "grunt", "jeep", "crane", "gatling" },
            new[] { "grunt", "psycho", "jeep", "light", "crane", "gatling", "gun" },
            new[] { "grunt", "psycho", "sniper", "tough", "jeep", "light", "medium", "crane", "gatling", "gun", "howitzer" },
            new[] { "grunt", "psycho", "sniper", "tough", "pyro", "jeep", "light", "medium", "apc", "crane", "gatling", "gun", "howitzer" },
            new[] { "grunt", "psycho", "sniper", "tough", "pyro", "laser", "jeep", "light", "medium", "heavy", "apc", "crane", "gatling", "gun", "howitzer", "missile_cannon" },
            new[] { "grunt", "psycho", "sniper", "tough", "pyro", "laser", "jeep", "light", "medium", "heavy", "apc", "missile_launcher", "crane", "gatling", "gun", "howitzer", "missile_cannon" },
        };
        static readonly string[][] Robot =
        {
            new[] { "grunt", "gatling" },
            new[] { "grunt", "psycho", "gatling" },
            new[] { "grunt", "psycho", "sniper", "tough", "gatling", "gun" },
            new[] { "grunt", "psycho", "sniper", "tough", "pyro", "gatling", "gun", "howitzer" },
            new[] { "grunt", "psycho", "sniper", "tough", "pyro", "laser", "gatling", "gun", "howitzer" },
            new[] { "grunt", "psycho", "sniper", "tough", "pyro", "laser", "gatling", "gun", "howitzer", "missile_cannon" },
        };
        static readonly string[][] Vehicle =
        {
            new[] { "jeep", "gatling" },
            new[] { "jeep", "light", "gatling", "gun" },
            new[] { "jeep", "light", "medium", "gatling", "gun" },
            new[] { "jeep", "light", "medium", "apc", "gatling", "gun", "howitzer" },
            new[] { "jeep", "light", "medium", "heavy", "apc", "gatling", "gun", "howitzer" },
            new[] { "jeep", "light", "medium", "heavy", "apc", "missile_launcher", "gatling", "gun", "howitzer", "missile_cannon" },
        };

        /// <summary>
        /// ONLY a fort builds him: Zod's fort list of EVERY level with the Electrobot right after the Laser, else after the last robot
        /// of the list (level 0: grunt, electrobot, jeep, crane, gatling); entry 0, the default production, is unchanged; the fort's
        /// every-level toggle is retired: ZWorld.ElectrobotAllLevels is ignored). Chosen at call time by ZWorld.ElectrobotDeviation,
        /// so the parity report's Zod rows (switch off) read Zod's own tables.</summary>
        static readonly string[][] FortE = WithElectrobot(Fort);
        static string[][] WithElectrobot(string[][] t)
        {
            var r = new string[t.Length][];
            for (int i = 0; i < t.Length; i++)
            {
                var l = new List<string>(t[i]);
                int k = l.IndexOf("laser");
                if (k < 0) k = l.FindLastIndex(IsRobot);
                if (k >= 0) l.Insert(k + 1, ZElectro.Type);
                r[i] = l.ToArray();
            }
            return r;
        }

        public static bool IsProducer(string buildingType) =>
            buildingType == "fort_front" || buildingType == "fort_back" || buildingType == "robot_factory" || buildingType == "vehicle_factory";

        public static IReadOnlyList<string> For(string buildingType, int level)
        {
            if (level < 0 || level > 5) return System.Array.Empty<string>();
            switch (buildingType)
            {
                case "fort_front": case "fort_back": return ZWorld.ElectrobotDeviation ? FortE[level] : Fort[level];   // DV45: forts only, every level (TECH VII)
                case "robot_factory": return Robot[level];
                case "vehicle_factory": return Vehicle[level];
            }
            return System.Array.Empty<string>();
        }

        /// <summary>
        /// Electrobot (DV45) is on (only forts build him), every other producer BuildLevel + 1 (Zod's blevel 0..5 = I..VI).</summary>
        public static int DisplayTech(string buildingType, int buildLevel) =>
            ZWorld.ElectrobotDeviation && (buildingType == "fort_front" || buildingType == "fort_back") ? 7 : System.Math.Max(0, System.Math.Min(5, buildLevel)) + 1;

        static readonly string[] romans = { "I", "I", "II", "III", "IV", "V", "VI", "VII" };
        /// <summary></summary>
        public static string Roman(int n) => romans[System.Math.Max(1, System.Math.Min(7, n))];

        public static bool InList(string buildingType, int level, string unit)
        {
            foreach (var u in For(buildingType, level)) if (u == unit) return true;
            return false;
        }
    }
}
