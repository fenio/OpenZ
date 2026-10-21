using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// DV108 (View/OzHeroSquadView.cs): the hero squad's level-start lounger. A run with <see cref="StartLying"/> begins in the lying
    /// loop at once: the weapon is away and the lounger and parasol simply stand there (no pop, no creak: nothing "happens" at the
    /// level start). With <see cref="Hold"/> the lying loop never ends by itself; a click, an order, a hit or a threat cancels it
    /// through the antic's own cancel (UnitView.AnticCancel -> Finish): the lounger and the parasol pop out with the antic's own puff
    /// and pop sound, the weapon pops back, exactly as for any sun-lounger antic. The team-lead rule of resting does not end a held
    /// lie (AnticDirector). View only.
    /// </summary>
    public sealed partial class AnticRun
    {
        /// <summary>The lying loop holds until the run is cancelled.</summary>
        public bool Hold;
        /// <summary>The run starts in the lying loop (props in place without their pop).</summary>
        public bool StartLying;
        bool quiet;

        /// <summary>Tick's first step: true when this run started lying (else the normal first clip is entered).</summary>
        bool HeroStart(Animator anim)
        {
            if (!StartLying || Kind != AnticKind.Sunbed || steps.Count < 2) return false;
            quiet = true;
            try
            {
                Stow();
                PlaceBed();
            }
            finally { quiet = false; }
            while (evNext < evs.Count && evs[evNext].step == 0) evNext++;                          // the lie-down's own events (stow, bed, creaks) are done
            if (!float.IsNaN(faceYaw)) View.AnticFace(faceYaw, 1000f);                            // already turned to the lounger
            Enter(anim, 1, 0f);
            Note("level start: lying");
            return true;
        }

        /// <summary>A prop of the lounger appears: popped in (the antic), or simply standing at its size (the level start).</summary>
        void Appear(Transform t, Vector3 scale, string what)
        {
            if (!t) return;
            if (quiet) { t.localScale = scale; foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.enabled = true; }
            else AnticFx.PopIn(t, scale, true, what, id);
        }
    }
}
