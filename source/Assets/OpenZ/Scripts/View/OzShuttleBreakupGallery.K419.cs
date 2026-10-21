namespace OpenZ
{
    /// <summary>
    /// K419b, the break-up proof's Proof K limits after K396 (test only). The split's line "wider than K1's burst" asked for x1.4. K396
    /// (the big blast) throws every break's pieces harder but the split's own out-throw only x0.9 (its second blast already widens it,
    /// and so it stays in its 26-48 m field), while a burst's goes up x1.3-1.9: the gap between the two shrinks by design to about
    /// the old gap x 0.9 / 1.5 (run 28: K1's burst 27.8 m, the splits 34.5 / 32.9 m = x1.24 / x1.18; the old game 23-28 m against
    /// 12-15 m). With the big blast on the split must still be wider than the burst: at least x1.1; with it off the old x1.4.
    /// </summary>
    public sealed partial class OzShuttleBreakupGallery
    {
        /// <summary>The split's body panels' field against K1's burst: at least this many times as wide.</summary>
        static float SplitOverBurst => OzShuttleBreakup.BigBlast ? 1.1f : 1.4f;
    }
}
