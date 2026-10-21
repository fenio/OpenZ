using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1, items 331 + 332: the one manager of the big bodies against the rocks (made by OzRockSolid.All.cs at the level's start,
    /// kept across levels). Every frame: the queued rock parts are built in a small time budget, and the parts of the level not built yet
    /// (OzRockSolid.Prebuild); while the shuttle drops whole (a rifle-class kill) the rocks round it are switched on, so its fall meets a
    /// rock top or side and it breaks there. Before each physics step: the big bodies made since the last one are checked for a start
    /// inside a rock (OzRockBody). View only.
    /// </summary>
    [DefaultExecutionOrder(-38)]
    public sealed class OzRockDebris : MonoBehaviour
    {
        void FixedUpdate()
        {
            if (!OzRockSolid.AllOn) return;
            OzRockBody.StartChecks();
        }

        void Update()
        {
            if (!OzRockSolid.AllOn) return;
            OzRockSolid.BuildStep();
            if (OzShuttleBreakup.Falling)
                for (int i = 0; i < OzPlayableShuttle.Count; i++)
                {
                    var s = OzPlayableShuttle.At(i);
                    if (!s || !s.BrokenUp || !s.gameObject.activeInHierarchy) continue;
                    OzRockSolid.ArmAround(s.transform.position, 16f, 1.5f, 10f);
                }
        }
    }
}
