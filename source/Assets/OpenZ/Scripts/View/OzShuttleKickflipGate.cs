using UnityEngine;

namespace OpenZ
{
    /// <summary>K344: puts every flipped shuttle back to its level pose first thing in the frame (order -47: after the rules' tick at -50,
    /// before every reader of the ship's pose: the click box, the aim, the overlays, the ship's own placement). Made by the first flip,
    /// one per scene.</summary>
    [DefaultExecutionOrder(-47)]
    public sealed class OzShuttleKickflipGate : MonoBehaviour
    {
        static OzShuttleKickflipGate me;
        public static void Ensure()
        {
            if (me) return;
            var go = new GameObject("OZ_ShuttleKickflipGate");
            me = go.AddComponent<OzShuttleKickflipGate>();
        }

        void Update()
        {
            for (int i = 0; i < OzPlayableShuttle.Count; i++) { var s = OzPlayableShuttle.At(i); if (s) s.KickflipUndo(); }
        }

        void OnDestroy() { if (me == this) me = null; }
    }
}
