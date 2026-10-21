using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// Level 16: the level's own animals serve the mission-start flyover (the stage's interface IOzL16BriefingAnimals): the crocodile
    /// it borrows is the pool's own animal as CrocodileView draws it (its model, its takes, its V wake by the level's wake rule), the
    /// parrots it borrows are the level's parrot model from JungleFauna. Registered while level 16 runs, taken back when it goes.
    /// This file needs the flyover kit's OzL16BriefingStage.cs: the animals kit copies it only when that file is in the project.
    /// </summary>
    public sealed class OzL16AnimalsForBriefing : IOzL16BriefingAnimals
    {
        static OzL16AnimalsForBriefing me; static bool hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!hooked) { hooked = true; SceneManager.sceneLoaded += (s, m) => Register(); }
            Register();
        }

        static void Register()
        {
            bool l16 = ZGame.I && ZGame.I.level == "L16";
            if (l16) { if (me == null) me = new OzL16AnimalsForBriefing(); OzL16BriefingStage.Animals = me; }
            else if (me != null && ReferenceEquals(OzL16BriefingStage.Animals, me)) OzL16BriefingStage.Animals = null;
        }

        public Transform CrocBorrow(Vector3 near) => CrocodileView.Lend(near);
        public void CrocPose(Transform croc, string take, float seconds, float speed) => CrocodileView.LendPose(croc, take, seconds, speed);
        public void CrocPoseMix(Transform croc, string takeA, float secondsA, string takeB, float secondsB, float shareB, float speed) => CrocodileView.LendPoseMix(croc, takeA, secondsA, takeB, secondsB, shareB, speed);
        public void CrocReturn(Transform croc) => CrocodileView.LendReturn(croc);
        public Transform ParrotBorrow() => JungleFauna.LendParrot();
        public void ParrotPose(Transform parrot, float flapSeconds) => JungleFauna.LendParrotPose(parrot, flapSeconds);
        public void ParrotReturn(Transform parrot) => JungleFauna.LendParrotReturn(parrot);
    }
}
