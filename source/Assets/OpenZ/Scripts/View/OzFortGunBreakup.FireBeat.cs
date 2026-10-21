using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// beat (0.28 s burning, 0.6 s smouldering) and all three started on the same frame, so their fire lights blinked together.
    /// Each piece now keeps its own beat (rate x 0.75-1.25, every beat x 0.8-1.2) and its second burst comes 0-0.6 s later, drawn
    /// once per death from the cannon's wreck time, the piece's place and its name. Hook: Burn() (OzFortGunBreakup.cs).
    /// </summary>
    public partial class OzFortGunBreakup
    {
        OzFireDesync.Beat fireBeat; uint fireBeatS; bool fireBeatOn;

        float FireBeatGap(bool burning)
        {
            float gap = burning ? 0.28f : 0.6f;
            if (!OzFireDesync.On) return gap;
            float extra = 0f;
            if (!fireBeatOn)
            {
                fireBeatOn = true;
                int idx = grp != null ? grp.pieces.IndexOf(this) : 0, death = grp != null ? Mathf.RoundToInt(grp.wreckAt * 10000f) : 0;
                fireBeatS = OzFireDesync.Seed(death, idx, OzFireDesync.NameHash(PieceName));
                fireBeat = OzFireDesync.Draw(ref fireBeatS);
                extra = fireBeat.start; OzFireDesync.FortPieceStarts++;
            }
            return OzFireDesync.Gap(ref fireBeatS, gap, fireBeat.rate) + extra;
        }
    }
}
