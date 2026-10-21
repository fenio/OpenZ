using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// death from the ship's id, the piece's place in the break and its name, and the death's own wreck time (the break's seeded
    /// numbers): its own start (0-0.6 s after the break's first burst), burn-out, beat rate, beat jitter, flame size, light strength,
    /// light life and flicker. The wreck fire it lights where it comes to rest takes the same style. The ember rim on the cut edges
    /// takes its own two waves (both were shared by every piece). Hooks: Burn() and Freeze() (OzShuttleBreakup.cs), LookStep's rim
    /// flicker (OzShuttleBreakup.Look.cs).
    /// Play check: the main fire measures the fires' light pulses once, ProbeAt s after the break, and logs one line
    /// ("[OpenZ] wreck fires out of step: ..."); OzFireDesync.LastFires / LastR0 / LastLag / LastOk keep it.
    /// </summary>
    public sealed partial class OzShuttleBreakup
    {
        /// <summary>Play check: fires whose start was staggered, beats recorded for the check, checks logged.</summary>
        public static int FireStartsStaggered, BeatsNoted, BeatChecks;
        const int BeatNoteMax = 96;
        OzFireDesync.Beat beat; uint beatS; bool beatOn, beatStarted, beatChecked;
        float[] beatT, beatG, beatL; int beatN;

        void EnsureBeat()
        {
            if (beatOn) return;
            beatOn = true;
            int idx = grp != null ? grp.pieces.IndexOf(this) : 0;
            int death = grp != null ? Mathf.RoundToInt(grp.wreckAt * 10000f) : 0;
            beatS = OzFireDesync.Seed(grp != null ? grp.emitter : 0, idx, OzFireDesync.NameHash(PieceName) ^ death);
            beat = OzFireDesync.Draw(ref beatS);
        }

        /// <summary>Burn() (OzShuttleBreakup.cs): the burst in this fire's own style, then its own next beat. The first call only sets
        /// the fire's own start and burn-out.</summary>
        void BurnBeat()
        {
            EnsureBeat();
            if (!beatStarted)
            {
                beatStarted = true;
                if (OzFireDesync.On && bigFire)
                {
                    burnUntil += OzFireDesync.OutShift(beat, mainFire);
                    if (beat.start > 0.0001f) { burnT = beat.start; FireStartsStaggered++; return; }
                }
            }
            bool burning = age < burnUntil;
            OzFireDesync.Begin(beat);
            try { BurnStep(); }
            finally { OzFireDesync.End(); }
            burnT = OzFireDesync.Gap(ref beatS, burnT, beat.rate);
            if (burning) NoteBeat();
            if (mainFire && !beatChecked && age >= OzFireDesync.ProbeAt) { beatChecked = true; CheckBeats(); }
        }

        /// <summary>Freeze() (OzShuttleBreakup.cs): the wreck fire under a burning piece at rest, in the piece's style.</summary>
        void FireAtRestBeat(Vector3 low)
        {
            EnsureBeat();
            OzFireDesync.Begin(beat);
            try { FireAtRest(low); }
            finally { OzFireDesync.End(); }
        }

        /// <summary>LookStep (OzShuttleBreakup.Look.cs): the ember rim's flicker, its own phases and rates (the old shared waves when off).</summary>
        float EmberBeat()
        {
            if (!OzFireDesync.On) return 0.84f + 0.16f * Mathf.Sin(age * 9.3f + GetInstanceID() * 0.37f) * Mathf.Sin(age * 3.1f + 1.3f);
            EnsureBeat();
            return 0.84f + 0.16f * Mathf.Sin(age * 9.3f * beat.emberRa + beat.emberA) * Mathf.Sin(age * 3.1f * beat.emberRb + beat.emberB);
        }

        void NoteBeat()
        {
            if (beatT == null) { beatT = new float[BeatNoteMax]; beatG = new float[BeatNoteMax]; beatL = new float[BeatNoteMax]; }
            if (beatN >= BeatNoteMax) return;
            bool on = OzFireDesync.On;
            beatT[beatN] = age; beatG[beatN] = on ? beat.light : 1f; beatL[beatN] = 0.3f * (on ? beat.life : 1f);
            beatN++; BeatsNoted++;
        }

        /// <summary>Once per kill: the burning pieces' light pulses, pair by pair (OzFireDesync.Measure), and one log line.</summary>
        void CheckBeats()
        {
            if (grp == null) return;
            int fires = 0;
            foreach (var pc in grp.pieces) if (pc && pc.bigFire && pc.beatN > 1) fires++;
            if (fires < 2) return;
            var t = new float[fires][]; var g = new float[fires][]; var l = new float[fires][]; var n = new int[fires];
            var sb = new System.Text.StringBuilder();
            int f = 0;
            foreach (var pc in grp.pieces)
            {
                if (!pc || !pc.bigFire || pc.beatN <= 1) continue;
                t[f] = pc.beatT; g[f] = pc.beatG; l[f] = pc.beatL; n[f] = pc.beatN;
                sb.Append(f == 0 ? "" : "; ").Append(pc.PieceName).Append(pc.mainFire ? " (main)" : "").Append(": ").Append(pc.beatN).Append(" beats, rate x")
                  .Append(pc.beat.rate.ToString("0.00")).Append(", start ").Append(pc.beat.start.ToString("0.00")).Append(" s, out ").Append(pc.burnUntil.ToString("0.0")).Append(" s");
                f++;
            }
            OzFireDesync.Measure(t, g, l, n, fires, out float r0, out float lag);
            bool ok = lag < OzFireDesync.ProbeLimit;
            OzFireDesync.LastFires = fires; OzFireDesync.LastR0 = r0; OzFireDesync.LastLag = lag; OzFireDesync.LastOk = ok;
            OzFireDesync.LastLine = $"[OpenZ] wreck fires out of step: shuttle #{grp.emitter} ({grp.how}), {fires} fires, strongest pair: correlation {r0:0.00} as they are, {lag:0.00} shifted by up to {OzFireDesync.ProbeLag:0.0} s (limit {OzFireDesync.ProbeLimit:0.00}): {(ok ? "OK" : "TOO MUCH IN STEP")}{(OzFireDesync.On ? "" : " [switch off]")}; {sb}";
            BeatChecks++;
            Debug.Log(OzFireDesync.LastLine);
        }
    }
}
