using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Zod warns after 3 s of danger, then every 10 s (zplayer.cpp:1087-1096); OpenZ warns
    /// ONCE per attack wave: armed after Arm s of danger, never within MinGap s of the last warning, and a wave only ends after
    /// WaveCalm s without an enemy near the fort. The warning shares the radio limiter (RadioDiscipline): it waits while a cry
    /// has just gone out and counts as a radio line itself, so a cry never lands on top of it.
    /// One decision for everybody: the HUD toast, the director's announcement and FlagView's voice all read the same counter
    /// (Fired) and act once per increment, so the toast and the voice always come together.
    /// </summary>
    public static class FortAlert
    {
        public const float MinGap = 30f, WaveCalm = 20f, Arm = 3f;
        static float dangerSince = -1f, lastDanger = -1000f, lastFire = -1000f;
        static bool waveDone; static int frame = -1;

        /// <summary>Warnings raised this session (consumers act when it changes).</summary>
        public static int Fired { get; private set; }

        /// <summary>A caller reports whether an enemy is near the player's fort; returns the warning counter.</summary>
        /// <summary>
        /// The HUD reports the player's fort health every check; a warning needs a hit in the last HitWindow s.</summary>
        public const float HitWindow = 4f;
        static float lastHit = -1000f; static int lastFortHealth = int.MinValue, lastFortId = -1;
        /// <summary>combat_nb_v1 NB31: kept for callers; a health drop alone no longer counts (a neutral debris blast, the fort's own
        /// cannon wreck or a friendly shell also lower it). Real hits come from NoteFortHit.</summary>
        public static void NoteFortHealth(int fortId, int health)
        {
            if (fortId != lastFortId) { lastFortId = fortId; lastFortHealth = health; return; }
            lastFortHealth = health;
        }
        /// <summary>combat_nb_v1 NB31: the sim's ZObject.LastEnemyHit of one of the player's forts (sim time of the last health loss to an
        /// ENEMY shell or blast; debris and friendly fire never count), polled by the HUD with the sim time. Every fort keeps its own
        /// record (two forts never reset each other).</summary>
        public static void NoteFortHit(int fortId, double lastEnemyHitSim, double simNow)
        {
            if (!seenHit.TryGetValue(fortId, out double seen)) { seenHit[fortId] = lastEnemyHitSim; return; }
            if (lastEnemyHitSim > seen) { seenHit[fortId] = lastEnemyHitSim; if (simNow - lastEnemyHitSim < 1.0) lastHit = Time.unscaledTime; }
        }
        static readonly System.Collections.Generic.Dictionary<int, double> seenHit = new System.Collections.Generic.Dictionary<int, double>();
        /// <summary>combat_nb_v1 NB31: an enemy hit one of the player's forts in the last HitWindow s (whatever its distance).</summary>
        public static bool HitRecently => Time.unscaledTime - lastHit <= HitWindow;

        public static int Report(bool danger)
        {
            float now = Time.unscaledTime;
            danger = now - lastHit <= HitWindow;                                                // combat_nb_v1 NB31: only a real enemy hit on the fort warns (near or far); enemies merely in range never do
            if (danger) { if (dangerSince < 0f || now - lastDanger > WaveCalm) { dangerSince = now; waveDone = false; } lastDanger = now; }
            else if (now - lastDanger > WaveCalm) { dangerSince = -1f; waveDone = false; }
            if (!danger) return Fired;                                                          // a caller without danger never decides
            if (frame == Time.frameCount) return Fired;                                        // one decision per frame
            frame = Time.frameCount;
            if (waveDone || now - dangerSince < Arm || now - lastFire < MinGap) return Fired;
            var r = RadioDiscipline.Shared;
            if (now - r.LastCall < r.MinSpacing) return Fired;                                  // a radio cry just went out: wait
            waveDone = true; lastFire = now; r.Called(now, false); Fired++;
            return Fired;
        }

        /// <summary>A new level: forget the old waves.</summary>
        public static void Reset() { dangerSince = -1f; lastDanger = -1000f; lastFire = -1000f; waveDone = false; lastHit = -1000f; lastFortId = -1; seenHit.Clear(); }   // combat_nb_v1 NB31: + per-fort hit records

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Boot() { Reset(); UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnLoaded; UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnLoaded; }
        static void OnLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m) => Reset();
    }
}
