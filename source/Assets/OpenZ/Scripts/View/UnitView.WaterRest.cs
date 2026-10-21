using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The wreck of a vehicle or a gun and the shared water landing rule (OzWaterLanding). View only.
    /// A wreck that falls into water (a light vehicle that flipped into the river, a wreck whose bridge went) is watched by the
    /// rule from UnitView.BeginWreck; when it goes in, its fire and smoke go out here: the fire loop, the sticky fire on its hull,
    /// the flame tongues and the smoke puffs stop and it throws no spark fountain. What the wreck does afterwards by the existing
    /// rules (it may still break into parts, it fades, its turret lands) is not changed.
    /// </summary>
    public partial class UnitView
    {
        /// <summary>Proof counter: wrecks whose fire went out in water.</summary>
        public static int WrecksDoused { get; private set; }

        /// <summary>This view is a vehicle's or a gun's wreck right now.</summary>
        public bool IsWreckNow => death == Death.Wreck;

        /// <summary>OzWaterLanding: the wreck is in the water. True = it was still burning or smoking.</summary>
        public bool OzDouseWreck()
        {
            if (death != Death.Wreck || Obj == null) return false;
            bool was = !breakDone && !fading && deathT < WreckFx.SmoulderSeconds && smokeT < 1e8f;
            StopWreckFire(0.4f);
            if (wreckPyro) { OpenZPyroFire.StopBurning(Obj.Id); wreckPyro = false; }
            if (burnEnd > deathT) burnEnd = deathT;                                                        // the flame tongues end with the burn
            wreckFlameN = 0; sparked = true; smokeT = 1e9f;                                                // no more puffs, no spark fountain out of the water
            if (was) WrecksDoused++;
            return was;
        }
    }
}
