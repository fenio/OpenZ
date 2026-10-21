using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K419a (view only): two faults of the shuttle's break-up found in the BATCH 3 Play checks.
    /// <list type="bullet">
    /// <item><b>The light grey panel (K397's ground-kill rest crop):</b> not the inside. It was the PAINT of the pieces' upper part drawn in
    /// the hull material's neutral accent paint (_NeutralPaint, a light warm grey) instead of the team colour. The hull material is in
    /// accent mode (_Accent 1): the team colour shows only between _AccentLo and _AccentHi of the object's own height, and TeamPaint sets
    /// that band from the hull mesh's OWN space, whose y is the ship's length (the model is turned -90 degrees about x): -3.97 .. 3.73.
    /// The pieces copy the hull's paint block, but their meshes are in the ship's ROOT space, whose y is up (0.6 .. 5.1 m): every piece's
    /// paint above 3.73 m (the roof panels' tops, the dome halves' tops, the ramp leaf in places) lost its red and showed the albedo's
    /// light grey panels (its tiles, its ring). In every break kind; a ground kill leaves the roof panel upright in the middle, so it was
    /// seen there. Now each piece's paint gets the band over its whole height (<see cref="PieceBand"/>), as the intact hull has (its band
    /// is -10 % .. 110 % of its own mesh): the pieces keep exactly the hull's red / black paint. The insides stay the dark torn steel.</item>
    /// <item><b>The burning pockets' count (K396):</b> the pockets did burst; the Play proof took its count's starting value a frame or
    /// more after the break (after a strip picture), when the first pocket (due 0.12-0.45 s after the blast) had already burst: "+1 of 2",
    /// "+2 of 3". The blast now counts its own pockets and column puffs from zero (<see cref="LastSecondariesFired"/>,
    /// <see cref="LastColumnPuffs"/>) and the proof reads those.</item>
    /// </list>
    /// </summary>
    public sealed partial class OzShuttleBreakup
    {
        /// <summary>Switch: the pieces' paint keeps the team colour over its whole height (on).</summary>
        public static bool PieceBandOn = true;
        /// <summary>Proof: piece sub-meshes whose paint band was widened.</summary>
        public static int PieceBands;
        /// <summary>The last air blast's own counts: burning pockets burst, column puffs laid (from zero at the blast).</summary>
        public static int LastSecondariesFired, LastColumnPuffs;
        static readonly int AccentLoK419 = Shader.PropertyToID("_AccentLo"), AccentHiK419 = Shader.PropertyToID("_AccentHi");
        static MaterialPropertyBlock bandBlock;

        /// <summary>Build, after the paint: every painted sub-mesh of the piece (not its inside) shows the team colour over its whole height.</summary>
        static void PieceBand(Renderer r, int nSub, bool capped)
        {
            if (!PieceBandOn || !r) return;
            bandBlock ??= new MaterialPropertyBlock();
            var mats = r.sharedMaterials;
            for (int i = 0; i < nSub; i++)
            {
                if (capped && i == nSub - 1) continue;
                if (i >= mats.Length || !mats[i] || !mats[i].HasProperty(AccentLoK419)) continue;
                r.GetPropertyBlock(bandBlock, i);
                bandBlock.SetFloat(AccentLoK419, -1e6f); bandBlock.SetFloat(AccentHiK419, 1e6f);
                r.SetPropertyBlock(bandBlock, i);
                PieceBands++;
            }
        }
    }
}
