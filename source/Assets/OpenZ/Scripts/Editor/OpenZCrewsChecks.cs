using UnityEditor;
using UnityEngine;

namespace OpenZ
{
    /// <summary>crews_v2 Play checks (menu): the cannon exit proof on the gun, gatling, howitzer and missile cannon
    /// </summary>
    public static class OpenZCrewsChecks
    {
        [MenuItem("OpenZ/Checks/Cannon exit (4 types, Play)")]
        public static void CannonExit()
        {
            if (!Application.isPlaying) { Debug.LogError("[OpenZ] Cannon exit check: enter Play on L02_Desert or L17_CarPark first"); return; }
            CannonExitCheck.Begin();
        }

        /// <summary>gunner_seats_v1 Play proof: CrewSeatCheck (each gun type crewed on open ground, the turret swung to 8 bearings,
        /// </summary>
        [MenuItem("OpenZ/Checks/Crew seats (4 guns x 8 bearings, grunt, Play)")]
        public static void CrewSeatsGrunt() => CrewSeats("grunt");

        [MenuItem("OpenZ/Checks/Crew seats (fat v3 tough crew, Play)")]
        public static void CrewSeatsTough() => CrewSeats("tough");

        static void CrewSeats(string crew)
        {
            if (!Application.isPlaying) { Debug.LogError("[OpenZ] Crew seat check: enter Play on L02_Desert or L17_CarPark first"); return; }
            CrewSeatCheck.CrewType = crew;
            CrewSeatCheck.Run();
        }
    }
}
