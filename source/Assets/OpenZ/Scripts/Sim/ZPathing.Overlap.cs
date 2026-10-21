namespace OpenZ.Sim
{
    /// <summary>
    /// The option FRIENDLY UNITS OVERLAP (Options, GAME tab; saved as oz_overlap, default OFF = NO). Zod has no unit collision at all
    /// (research 7.4): its units walk and drive through each other. OpenZ's spacing layer (this class) keeps units apart; with the
    /// option ON, two units of the SAME team of which at least one is moving take no account of each other: no spacing gate, no queue
    /// or wait behind a team-mate, no side-step round one, no push, no "make way" and no "vehicles first" step aside (ZYield.cs), so
    /// they go along their paths and may pass through each other, as in the original game. What stays exactly as with the option off:
    /// paths round obstacles, buildings, rocks, trees, water and closed tiles; everything towards ENEMY units (spacing, crushing,
    /// blocking); weapons and hits; units that both STAND keep apart (the push of the layer parts them), so a group does not end as
    /// one heap. It holds for every team alike (the computer's units among themselves too). Not read in 'Zod exact' and in the parity runs.
    /// </summary>
    public sealed partial class ZPathing
    {
        /// <summary>The pair may overlap under the option: same team, at least one of the two moving.</summary>
        public static bool OverlapPair(ZObject a, ZObject b) =>
            ZWorld.FriendsOverlap && a.Owner == b.Owner && a.Owner != Team.Neutral && (Moving(a) || Moving(b));
    }

    public sealed partial class ZWorld
    {
        /// <summary>FRIENDLY UNITS OVERLAP (off in the rule file; View/ZGame.cs and the Options row set it from oz_overlap, never in
        /// 'Zod exact').</summary>
        public static bool FriendsOverlap = false;
    }
}
