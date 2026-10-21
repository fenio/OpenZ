using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K256f2 (rule DV118, view only): what the player sees and hears of a HERO pilot (Sim/ZWorld.ShuttleHeroes.cs). The unit panel names
    /// him ("PILOT BRAD": he gets out on EXIT, "EXIT READY" with him alone aboard too, UI/OzShuttleHud.cs), and when the crew has left and
    /// the ship goes dark (View/OzShuttleEmptyLook.cs: lit to dark, which only happens when a hero pilot got out with everyone) its engine
    /// dies with the game's own engine shut-down cue, as every vehicle whose crew is gone.
    /// </summary>
    public static class OzShuttleHeroesView
    {
        /// <summary>The sound table row of the power-down (the vehicles' engine shut-down).</summary>
        public static string PowerDownEvent = "veh_engine_shutdown";
        public const float PowerDownPitch = 0.9f;
        /// <summary>Proof counters: power-downs played, and asked for while the row was missing.</summary>
        public static int PowerDowns, PowerDownsMissing;

        /// <summary>The unit panel's word for the pilot: "PILOT", or "PILOT BRAD" (the hero's name) when he gets out on EXIT.</summary>
        public static string PilotWord(ZObject u) => u != null && u.ShuttleHeroPilot ? "PILOT " + OzHeroSquadView.CrewNameOr(u, false, "HERO") : "PILOT";

        /// <summary>OzShuttleEmptyLook: the lit ship has just gone dark (its crew left): the engine dies.</summary>
        public static void PowerDown(ZObject o, Vector3 at)
        {
            if (o == null || o.Destroyed) return;
            if (!AudioDirector.Has(PowerDownEvent)) { PowerDownsMissing++; OzSoundLog.Add("SHIP", PowerDownEvent, null, "DROP no event", 0f, at); return; }
            AudioDirector.Fire(PowerDownEvent, at, o.Id, 1f, PowerDownPitch);
            PowerDowns++;
        }
    }
}
