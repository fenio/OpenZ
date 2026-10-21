// OpenZ (GPL-3.0) - Level 16 briefing, the two robots' own script.
//  - No announcer start line on a briefing that the two robots hold themselves (a line with "host": "boys", Level 16): that briefing has
//    its own opening (the window opens on the two of them, the first word comes after the opening static), so the line about the
//    general's announcement does not fit it. Levels 2 and 17 (no host) keep it as before.
//  - The whole screen does not shake or crash-zoom any more on Level 16: the close chase and its hand-held danger camera live in the
//    briefing window (OzL16LoungerSet.Window.cs); the whole screen flies high (ZodFlyover.FullScreenDanger, off).
// Play check: return OpenZ.ZodBriefing.DebugStartLine();  ("not played: the two robots hold this briefing")
namespace OpenZ
{
    public sealed partial class ZodBriefing
    {
        public static int StartLinesSkipped { get; private set; }

        /// <summary>StartLineBegin: true = this briefing plays no announcer start line.</summary>
        bool StartLineSkipped(Perf p)
        {
            if (p?.line == null || p.line.host != "boys") return false;
            StartLinesSkipped++;
            lastStartLine = "not played: the two robots hold this briefing (" + p.id + ")";
            UnityEngine.Debug.Log("[ZodCameo] briefing start line " + lastStartLine);
            return true;
        }
    }

    public sealed partial class ZodFlyover
    {
        /// <summary>The hand-held danger camera and crash zooms of the whole screen in the crocodile shots (off: the chase is close only in
        /// the briefing window now; the whole screen shows it from far).</summary>
        public static bool FullScreenDanger = false;
    }
}
