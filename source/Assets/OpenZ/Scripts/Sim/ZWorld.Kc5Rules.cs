using System;

namespace OpenZ.Sim
{
    /// <summary>
    /// Version 1.1, two small rule changes (each a listed deviation of the parity report, each with its own switch):
    /// <list type="bullet">
    /// <item><b>DV110 a new veteran heals.</b> When a crew becomes a veteran (DV12, ZVeterancy.cs: the kill that promotes it), every unit
    /// of that crew that is still alive (the squad leader and his squad, or the vehicle / cannon the crew drives) gets
    /// <see cref="VeteranHealPct"/> % of its own full health back, if it is not at full health; never above full health. Integer maths
    /// (full health x 15 / 100, rounded down), no random draw, on the promotion's own tick: deterministic. A rank handed on later (a
    /// crew boarding or leaving a vehicle, a repaired vehicle keeping its crew) is not a new promotion and heals nothing. Zod has no
    /// veterancy at all; with DV12 off nobody is promoted and nothing is healed.</item>
    /// <item><b>DV111 the fort's roof defender is a sniper.</b> The man a fort under attack sends up through a roof hatch (DV41,
    /// ZObject.FortDefender.cs) is a sniper of the fort's team (the game's sniper: Zod's sniper settings for health, range, shot,
    /// hit chance and cycle) instead of a grunt with the crew rifle (the grunt's shot x ZWorld.CrewRifleMul). Every other rule of his
    /// stays: when and where he comes up, his walk on the deck, a clear line for every shot, his grenade lobs when no spot has a line
    /// (range = his own range + the roof bonus), never sniping drivers, the sandbag cover, the respawn time, the calm before he goes
    /// down. Off = the grunt of release 1.0.</item>
    /// </list>
    /// Both switches are static like the other deviation switches: on in the game (nothing in the game turns them off; with 'Zod exact'
    /// their parent rules DV12 / DV41 are off, so they do nothing); the parity report's other rows and the version 1.1 counted checks
    /// run with them off (<see cref="Kc5RulesOff"/>, OzV11Kit.GameSwitches); their own probes and checks set them.
    /// </summary>
    public sealed partial class ZWorld
    {
        /// <summary>DV110 switch: a crew that becomes a veteran gets VeteranHealPct % of its full health back.</summary>
        public static bool VeteranHealDeviation = true;
        /// <summary>DV110: share of the full health given back at the promotion (per cent).</summary>
        public const int VeteranHealPct = 15;
        /// <summary>DV111 switch: the fort's roof defender is a sniper (off = the grunt with the crew rifle of release 1.0).</summary>
        public static bool FortSniperDeviation = true;

        /// <summary>DV110 statistics (checks): units healed at a promotion, and the health given in all.</summary>
        public int VeteranHeals { get; private set; }
        public long VeteranHealHp { get; private set; }

        /// <summary>ZVeterancy.cs AddKill, right after the crew became a veteran: the crew's living units get their share back.</summary>
        internal void VeteranHeal(ZObject crew)
        {
            if (!VeteranHealDeviation || crew == null) return;
            VeteranHealOne(crew);
            foreach (var m in crew.Minions) VeteranHealOne(m);
        }

        void VeteranHealOne(ZObject u)
        {
            if (u == null || u.Destroyed || u.KillAt >= 0 || u.MaxHealth <= 0 || u.Health >= u.MaxHealth) return;
            int add = (int)((long)u.MaxHealth * VeteranHealPct / 100);
            int h0 = u.Health;
            u.SetHealth((int)Math.Min((long)u.MaxHealth, (long)u.Health + add));
            VeteranHeals++; VeteranHealHp += u.Health - h0;
        }

        /// <summary>A hold of both switches for checks and reports (off while held, put back on Dispose).</summary>
        public sealed class Kc5Hold : IDisposable
        {
            readonly bool heal, sniper;
            internal Kc5Hold() { heal = VeteranHealDeviation; sniper = FortSniperDeviation; VeteranHealDeviation = false; FortSniperDeviation = false; }
            public void Dispose() { VeteranHealDeviation = heal; FortSniperDeviation = sniper; }
        }
        public static Kc5Hold Kc5RulesOff() => new Kc5Hold();
    }
}
