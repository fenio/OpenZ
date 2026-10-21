namespace OpenZ.Sim
{
    /// <summary>
    /// OpenZ deviation DV115 "the CPU builds a mix of units": heavy tanks and mobile missile launchers later in a game, and a mix of
    /// robot squads from the start. Zod's bot builds the FIRST unit of its preferred list (medium, light, jeep, pyro, tough, sniper,
    /// tough, psycho; zbot.cpp:20-27, :1372-1432) that a producer can make, so a vehicle factory or fort that can make Medium tanks
    /// builds Medium tanks forever (the heavy tank and the missile launcher are not in the list at all), and a robot factory that can
    /// make Pyros builds only Pyros (DV71 adds its Laser squads).
    /// <para>With DV115, after Zod's pick and the Laser rhythm of DV71 (which wins), before the Electrobot claim of DV46:</para>
    /// <list type="bullet">
    /// <item>VEHICLES (<see cref="HeavyPick"/>): when Zod's pick is a Medium tank, Light tank or Jeep and the game clock is at
    /// <see cref="HEAVY_FROM"/> (8:00) or later, of every <see cref="HEAVY_EVERY"/> vehicles the producer starts from 8:00 on the
    /// first one is a heavy tank or a mobile missile launcher, the two in turn (the heavy tank first); a producer that can make only one
    /// of the two makes that one; one that can make neither keeps Zod's pick. A vehicle already in the making at 8:00 is finished as it
    /// is (never thrown away) and not counted. Counted on the building's own vehicles whose build started at 8:00 or later
    /// (<see cref="ZObject.BotLateVehicles"/>), so the rhythm starts fresh at the line.</item>
    /// <item>ROBOTS (<see cref="RobotMixPick"/>): when Zod's pick is a robot squad (not a Laser of DV71, not the Electrobot), every
    /// <see cref="MIX_EVERY"/>nd squad the producer turns out is the next of <see cref="MixRotation"/> (Tough, Psycho, Sniper), skipping
    /// those it cannot make at its build level (Zod's pick when it can make none of them); from the start of the game. Counted on the
    /// building's own robot squads without its Lasers and Electrobots (<see cref="ZObject.BotMixRobots"/>), so the Laser rhythm and
    /// this one never fall on the same squads and the turn goes round all three.</item>
    /// </list>
    /// <para>Every other pick stays Zod's. Normal and Easy alike; no level is named anywhere. Deterministic: two counters per building,
    /// no random draw, the same STOP_BUILDING + START_BUILDING commands. Switch: <see cref="ZWorld.BotHeavyDeviation"/> (the game turns
    /// it on at every start, off in 'Zod exact'; headless default off; with it off every pick is today's).</para>
    /// </summary>
    public sealed partial class ZBot
    {
        /// <summary>DV115: game seconds before the first heavy tank / missile launcher pick (8:00, the strike groups' line too).</summary>
        public const double HEAVY_FROM = 480;
        /// <summary>DV115: of every this many vehicles a producer starts from 8:00 on, the first is a heavy tank or a missile launcher.</summary>
        public const int HEAVY_EVERY = 3;
        /// <summary>DV115: of every this many robot squads a producer turns out (its Lasers and Electrobots not counted), the last is the
        /// next of <see cref="MixRotation"/>.</summary>
        public const int MIX_EVERY = 2;
        /// <summary>DV115: the robot squads of the mix, in turn.</summary>
        public static readonly string[] MixRotation = { "tough", "psycho", "sniper" };

        static bool MixVehicle(string t) => t == "medium" || t == "light" || t == "jeep";
        static bool MixRobot(string t) => t == "pyro" || t == "tough" || t == "sniper" || t == "psycho" || t == "grunt";

        /// <summary>DV115: Zod's pick of this producer, or a heavy tank / missile launcher when its vehicle rhythm says so (public for
        /// the checks).</summary>
        public string HeavyPick(ZObject b, string zodPick)
        {
            if (!ZWorld.BotHeavyDeviation || !MixVehicle(zodPick) || W.Time + TIME_EPS < HEAVY_FROM) return zodPick;
            if (b.Building && b.CurUnit == zodPick && b.BInit + TIME_EPS < HEAVY_FROM) return zodPick;       // the one in the making at 8:00 is finished first
            if (b.BotLateVehicles % HEAVY_EVERY != 0) return zodPick;
            bool heavy = ZBuildLists.InList(b.Type, b.BuildLevel, "heavy"), mlrs = ZBuildLists.InList(b.Type, b.BuildLevel, "missile_launcher");
            if (heavy && mlrs) return (b.BotLateVehicles / HEAVY_EVERY) % 2 == 0 ? "heavy" : "missile_launcher";
            return heavy ? "heavy" : mlrs ? "missile_launcher" : zodPick;
        }

        /// <summary>DV115: Zod's pick of this producer, or the next robot squad of the mix when its robot rhythm says so (public for
        /// the checks).</summary>
        public string RobotMixPick(ZObject b, string zodPick)
        {
            if (!ZWorld.BotHeavyDeviation || !MixRobot(zodPick)) return zodPick;
            if (b.BotMixRobots % MIX_EVERY != MIX_EVERY - 1) return zodPick;
            int turn = b.BotMixRobots / MIX_EVERY;
            for (int i = 0; i < MixRotation.Length; i++)
            {
                string t = MixRotation[(turn + i) % MixRotation.Length];
                if (ZBuildLists.InList(b.Type, b.BuildLevel, t)) return t;
            }
            return zodPick;
        }
    }

    public partial class ZObject
    {
        /// <summary>DV115: vehicles this building has turned out whose build started at 8:00 on the game clock (<see cref="ZBot.HEAVY_FROM"/>)
        /// or later.</summary>
        public int BotLateVehicles;
        /// <summary>DV115: robot squads this building has turned out, its Lasers and Electrobots not counted.</summary>
        public int BotMixRobots;
    }

    public sealed partial class ZWorld
    {
        /// <summary>DV115 the CPU builds a mix of units (ZBot.HeavyMix.cs). ZGame turns it on at every start (off in 'Zod exact');
        /// headless checks keep Zod's picks unless they switch it on.</summary>
        public static bool BotHeavyDeviation = false;

        /// <summary>DV115: a building turned out a unit (BuildingCreateUnit, beside the DV71 count): its two mix counters.</summary>
        void BotMixNoteBuilt(ZObject b, string type)
        {
            if (type == ZElectro.Type || type == "laser") return;
            if (ZBuildLists.IsRobot(type)) b.BotMixRobots++;
            else if (b.BInit + 1e-6 >= ZBot.HEAVY_FROM) b.BotLateVehicles++;                        // BInit: the start of the build that just finished
        }
    }
}
