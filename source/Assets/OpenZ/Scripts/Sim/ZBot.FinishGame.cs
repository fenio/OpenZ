using System;
using System.Collections.Generic;

namespace OpenZ.Sim
{
    /// <summary>
    /// switch (default on, ZGame sets them at every start from PlayerPrefs; off in 'Zod exact' and in every parity run):
    /// <list type="bullet">
    /// <item><b>DV60 the CPU finishes the game</b>.
    /// Zod's bot gives its order rounds to a random
    /// 15-35 % (+1) of its idle units every 4-12 s and matches each target to ONE mutually nearest unit, so an enemy fort is one target
    /// among the flags, crates and vehicles and a group parked beside it can wait minutes (headless: a blue squad 7.8 tiles from the
    /// red fort stood 180 s). With DV60, every 2 s after Zod's round, each of the bot's own IDLE squad leaders and vehicles (no
    /// waypoint, not fighting, not stunned, can move) within <see cref="ZWorld.BotFinishTiles"/> tiles of a live enemy fort (or
    /// <see cref="ZWorld.BotFinishTilesAllOut"/> while Zod's all-out tier says the bot is ahead on flags) and in the same region gets
    /// the ENTER FORT order through the same command path as Zod's own orders (SEND_WAYPOINTS; the Easy CPU's attacker cap does not hold it back, it still counts toward it).
    /// Killing every enemy unit still ends the game as in Zod (zserver.cpp:1925-1965).</item>
    /// <item><b>DV61 the crane mends damaged buildings</b>.
    /// Zod's crane only rebuilds DESTROYED buildings (zobject.cpp:4494-4504). With DV61 an own or neutral
    /// building (not a fort) that still stands but lost health is a crane target too (cursor, order, CPU): the crane drives to its
    /// crane entrance, stops there (never into the walls), works <see cref="ZWorld.CraneFixSeconds"/> s with the crane animation and
    /// sparks, then the building is back to full health (Zod's AutoRepair). Destroyed buildings: Zod's drive in / out as before.</item>
    /// </list>
    /// Deterministic: sim time, object order, no random draw, no view state.
    /// </summary>
    public sealed partial class ZBot
    {
        double nextFinish;
        /// <summary>DV60 statistics: fort-entry orders the finisher gave.</summary>
        public int FinishOrders { get; private set; }

        /// <summary>DV60, once per AI tick after Zod's order round and DV56.</summary>
        void FinishGame()
        {
            if (!ZWorld.BotFinishDeviation || W.GameOver) return;
            if (W.Time + TIME_EPS < nextFinish) return;
            nextFinish = W.Time + ZWorld.BotFinishInterval;
            bool anyFort = false;
            foreach (var o in W.Objects) if (o.IsFort && !o.Destroyed && o.Owner != Team && o.Owner != Team.Neutral) { anyFort = true; break; }
            if (!anyFort) return;
            int lim = (LastAllOut ? ZWorld.BotFinishTilesAllOut : ZWorld.BotFinishTiles) * 16;
            var sent = new List<ZObject>();
            foreach (var u in W.Objects)
            {
                if (!u.IsMobile || u.Owner != Team || Gone(u) || u.IsFortDefender || u.IsMinion || u.Destroyed || u.KillAt >= 0) continue;
                if (!(u.Kind == ZKind.Robot || u.Kind == ZKind.Vehicle) || !u.CanMove || u.Stunned) continue;
                if (u.Kind == ZKind.Vehicle && u.Type == "crane") continue;                                  // the crane mends, it does not storm forts
                if (u.Waypoints.Count > 0 || u.IsMoving) continue;                                           // busy (Zod's orders, a grab, a dodge)
                if (u.AttackObject != null && InReach(u, u.AttackObject)) continue;                            // really fighting (target in its range)
                ZObject f = null; double fd = double.MaxValue;
                foreach (var o in W.Objects)
                {
                    if (!o.IsFort || o.Destroyed || o.Owner == Team || o.Owner == Team.Neutral || !o.CanEnterFort(Team)) continue;
                    double dx = o.CenterX - u.CenterX, dy = o.CenterY - u.CenterY, d = Math.Sqrt(dx * dx + dy * dy);
                    if (d < fd) { fd = d; f = o; }
                }
                if (f == null || fd > lim) continue;
                TargetPoint(f, out int tx, out int ty);
                if (!InSameRegion(u.LocX + 8, u.LocY + 8, tx, ty, u.Kind == ZKind.Robot)) continue;
                sent.Add(u); FortOf[u.Id] = f;
            }
            foreach (var u in sent)
            {
                int before = Orders;
                Command(u, FortOf[u.Id], true);
                if (Orders > before)
                {
                    FinishOrders++;
                    Log.Add($"{W.Time:F0}s {Team} DV60 {u.Type}#{u.Id} -> enter fort {FortOf[u.Id].Type}#{FortOf[u.Id].Id}");
                }
            }
            FortOf.Clear();
        }
        readonly Dictionary<int, ZObject> FortOf = new Dictionary<int, ZObject>();

        /// <summary>DV60: a unit that only "attacks" a target beyond its range stands still and never fires (the headless L17 case:
        /// a grunt squad holding a sniper 7 tiles off as its attack object for minutes, no clear line so it never fired): that counts as idle.</summary>
        static bool InReach(ZObject u, ZObject t)
        {
            if (t.Destroyed || t.KillAt >= 0) return false;
            double r = u.AttackRadius + 16, dx = t.CenterX - u.CenterX, dy = t.CenterY - u.CenterY;
            return dx * dx + dy * dy <= r * r && u.HasLineOfSight(t);                                  // DV23: no line = it holds its fire and stands
        }
    }

    public sealed partial class ZWorld
    {
        /// <summary>
        /// PlayerPrefs oz_bot_finish, oz_zod_exact.</summary>
        public static bool BotFinishDeviation = true;
        /// <summary>DV60: tiles from the enemy fort centre within which an idle CPU unit storms it; while Zod's all-out tier is on.</summary>
        public const int BotFinishTiles = 14, BotFinishTilesAllOut = 30;
        /// <summary>DV60: check interval (game s).</summary>
        public const double BotFinishInterval = 2.0;

        /// <summary>
        /// PlayerPrefs oz_crane_fix, oz_zod_exact.</summary>
        public static bool CraneFixDeviation = true;
        /// <summary>DV61: game seconds the crane works at the entrance of a damaged building.</summary>
        public const double CraneFixSeconds = 4.0;

        /// <summary>OpenZ deviation DV74 "a crane rebuilds any destroyed building or bridge". Zod's crane may only rebuild a destroyed
        /// building whose zone is neutral or its own team's (zobject.cpp:4494-4504), so a ruin in a zone the other side holds gave no
        /// repair cursor and no order. With DV74 every destroyed building that is not a fort, and every destroyed bridge, is a crane
        /// target for every team, whoever owns its zone (ZObject.CanBeRepairedByCrane: cursor, order, the command check). Nothing else
        /// changes: the rebuilt building still belongs to its zone's owner, forts are never rebuilt, mending a damaged standing building
        /// (DV61) stays own / neutral. The CPU follows the same rule but only picks bridges and ruins of its own or a neutral zone
        /// (ZBot.CraneJob): a ruin rebuilt in the other side's zone would be that side's building.
        /// ZGame turns it on at every start (off in 'Zod exact'); headless checks keep Zod's rule unless they switch it on.</summary>
        public static bool CraneAnyRuinDeviation = false;
    }
}
