#if !OPENZ_HARNESS
using System.Collections.Generic;
using System.Globalization;

namespace OpenZ.EditorTools
{
    /// <summary>music_v3: the Deviations row of the dramatic fort-attack music (view only: the sim is read, never written).</summary>
    public static partial class OpenZParity
    {
        static void AddMusicV3Deviation(List<Deviation> list)
        {
            var c = new MusicCoreConfig();
            string f(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
            list.Add(new Deviation
            {
                id = "DV50", change = "Dramatic fort-attack music (view only)",
                zod = "FORT music when ONE enemy unit (cannons included) is within 250 px of our fort (zmusic_engine.cpp:258-263); a new danger level needs 3 s (:291-301, 15 s once the fort is destroyed) and FORT holds at least 3 s (:367). " +
                      "OpenZ before music_v3: after a close push line (vo_push) every ATTACKING state also played the fort music (AudioDirector finalPush); the music stopped at the game over",
                openz = $"a BIG attack on EITHER fort brings the dramatic music: >= {c.minUnits} engaged hostile mobile units (within {f(c.rIn / 16f)} tiles and attacking something of that side inside the fort area " +
                        $"({f(c.rCore / 16f)} tiles), heading into it, or in it while the fort takes hits; robot {f(c.wRobot)}, jeep / APC {f(c.wJeep)}, light {f(c.wLight)}, medium / MLRS {f(c.wMedium)}, heavy {f(c.wHeavy)}; " +
                        $"no cannons, cranes, idle or passing units or the fort defender) worth >= {f(c.hotScore)} (>= {f(c.hotScoreUnderFire)} while the fort takes hits) for {f(c.enterHold)} s (leaky hold); " +
                        $"it ends when less than {f(c.warmScore)} stay engaged for {f(c.exitHold)} s ({f(c.exitHold + c.exitHoldChain)} / {f(c.exitHold + 2 * c.exitHoldChain)} / {f(c.exitHoldMax)} s for the next dramas of a siege) with no hit for {f(c.calmAfterHit)} s, " +
                        $"or {f(c.hotLapse)} s after the last big-attack reading, never before {f(c.minDrama)} s; then the regular song plays at least {f(c.cooldown)} s ({f(c.heavyInterlude)} s for a heavy attack) before the next drama; " +
                        "the regular song is paused and resumed at its sample; a drama playing at the fort's fall plays on to the end screen. Zod's CALM / ATTACKING / FORT state is unchanged for the fort alarm, " +
                        "the announcement and the ambience; for the music FORT selects the battle playlist (the finalPush fort music is gone)",
                measured = "headless: the music v3 folder harness (MusicCoreScenarios: 26 timelines + 2 x 200 random 40 min matches; the state-machine reviewer's timelines); bot-vs-bot calibration on the live Sim (reports/botcal_L02.txt, botcal_L17.txt); Play: OpenZ > Music v3 > Status / Force",
                toggle = "AudioDirector.musicV3 (off = music_v2 with Zod's rule)",
                status = DevActive,
            });
        }
    }
}
#endif
