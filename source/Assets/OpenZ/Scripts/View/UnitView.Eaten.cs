using System.Collections;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Level 16: the "eaten" death of a robot a crocodile took (Sim/ZWorld.Crocodiles.cs sets ZObject.Eaten and kills it; CombatView.Death
    /// calls this instead of the usual death). Slapstick, nothing gory: no pop into parts, no ragdoll, no wreck: the robot keeps its
    /// body, kicks in the jaws (CrocodileView hangs it on the animal's prey bone and hides it as it goes under), and its helmet
    /// bobs up afterwards. Without a crocodile view (the model is not imported) it goes under by itself with a splash.
    /// </summary>
    public partial class UnitView
    {
        /// <summary>Taken by a crocodile: the view no longer places this robot (CrocodileView does, until it hides it).</summary>
        public bool EatenNow { get; private set; }

        public void EatenByCrocodile()
        {
            if (death != Death.None) return;
            AnticCancel("death", true);                                                   // props off, the weapon back
            if (TryGetComponent(out RobotWeaponPose wpose)) wpose.BeforeDeath();
            if (hold) hold.Suspended = true;
            if (reactKind != 0) EndReaction();
            deathFrom = transform.position;
            deathT = float.NegativeInfinity;                                              // this death is ended by Hide(), never by the usual pop of a corpse
            death = Death.Anim;
            EatenNow = true;
            if (anim) { anim.speed = 1.6f; anim.CrossFadeInFixedTime("blast_airborne", 0.08f); }   // arms up, legs kicking
            if (!CrocodileView.Take(this)) StartCoroutine(EatenAlone());
        }

        IEnumerator EatenAlone()
        {
            var from = transform.position;
            if (OpenZFxPlayer.I) OpenZFxPlayer.I.Splash(from, 1.2f);
            for (float t = 0f; t < 1.1f; t += Time.deltaTime)
            {
                transform.position = from + Vector3.down * (2.6f * t / 1.1f);
                yield return null;
            }
            if (OpenZFxPlayer.I) OpenZFxPlayer.I.Ripple(from, 0.6f);
            Hide();
        }
    }
}
