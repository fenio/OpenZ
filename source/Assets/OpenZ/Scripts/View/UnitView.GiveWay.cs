using UnityEngine;

namespace OpenZ
{
    /// <summary>Version 1.1, items 331 + 332 (OzPieceGiveWay): a flipped hull lying in a unit's way gives way the way the wreck always
    /// leaves (its own fade: it sinks into the ground with a puff of dust and hides), now instead of at the end of its smoulder, and
    /// quicker (in OzPieceGiveWay.SettleSeconds instead of WreckFx.FadeSeconds), so it is gone before the unit gets there.</summary>
    public partial class UnitView
    {
        /// <summary>True = the dead vehicle's flipped hull began its fade now.</summary>
        internal bool OzGiveWay()
        {
            if (fading || !flipWreck || Obj == null || !Obj.Destroyed) return false;
            BeginFade();
            gameObject.AddComponent<OzGiveWayHullFade>().view = this;
            return true;
        }

        /// <summary>OzGiveWayHullFade, every frame of the fade: its time runs faster.</summary>
        internal void OzFadeFaster(float dt)
        {
            if (!fading) return;
            fadeT += dt * Mathf.Max(0f, WreckFx.FadeSeconds / Mathf.Max(0.2f, OzPieceGiveWay.SettleSeconds) - 1f);
        }
    }

    /// <summary>A flipped hull that gives way: its own fade, quicker.</summary>
    public sealed class OzGiveWayHullFade : MonoBehaviour
    {
        public UnitView view;
        void Update() { if (view) view.OzFadeFaster(Time.deltaTime); else enabled = false; }
    }
}
