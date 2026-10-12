using OpenZ.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    ///
    /// Cause: the fort only explodes on its sim Death event. Zod destroys a team's forts when its LAST unit is destroyed
    /// (zserver.cpp:1925-1965 CheckNoUnitsDestroyFort, called from UpdateObjectHealth), but the game also ends when the last unit is
    /// lost without dying: its driver sniped (the vehicle turns neutral), a crew ejected, a vehicle entering the repair station.
    /// EndGameRequirementsMet (zserver.cpp:337-362) then sees one team left and ends the game with the loser's fort still standing
    /// (ZWorld.CheckEndGame, 1:1) - no Death event, no collapse, no nuke.
    ///
    /// Fix (view only; the sim is untouched and the game is already over): the frame the sim reports GameOver, every fort of a
    /// losing team that still stands gets a Death event put into ZWorld.Events before any reader sees the list (order -48: after
    /// ZGame's tick at -50 and OpenZEnding's scan at -49, before AudioDirector 40 / FortGunView 45 / CombatView 50). Every listener
    /// then plays the fort's fall exactly as for a sim kill: RulesView.BuildingDeath (the nuke strike, or with strikes off the
    /// immediate collapse + NukeFx: the fort always explodes), the fort gun blown off, the fort-lost lines, OpenZVictory's
    /// sequence waits for the strike. A draw (no winner) and a fort the sim already destroyed are left alone.
    /// </summary>
    [DefaultExecutionOrder(-48)]
    public sealed class FortFallAtGameOver : MonoBehaviour
    {
        public static bool Enabled = true;
        /// <summary>Forts exploded by this rule this level (Play proof).</summary>
        public static int Injected { get; private set; }
        ZGame game; bool done;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnLoaded; SceneManager.sceneLoaded += OnLoaded; Attach(); }
        static void OnLoaded(Scene s, LoadSceneMode m) => Attach();
        static void Attach()
        {
            var g = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            if (g && !g.GetComponent<FortFallAtGameOver>()) g.gameObject.AddComponent<FortFallAtGameOver>();
        }

        void Awake() { game = GetComponent<ZGame>(); if (!game) game = ZGame.I; Injected = 0; }

        void Update()
        {
            if (!Enabled || done || game == null || game.World == null || !game.World.GameOver) return;
            done = true;
            var w = game.World;
            if (w.Winner == Team.Neutral) return;                                                     // a draw: nobody's fort falls
            foreach (var f in w.Objects)
            {
                if (!f.IsFort || f.Destroyed || f.Owner == Team.Neutral || f.Owner == w.Winner) continue;
                w.Events.Add(new ZEvent { type = ZEventType.Death, objId = f.Id, otherId = -1, x = f.CenterX, y = f.CenterY });
                Injected++;
                Debug.Log($"[OpenZ] game over with the {f.Owner} fort standing (last unit lost without dying): it falls now (combat_nb_v1 NB30)");
            }
        }
    }
}
