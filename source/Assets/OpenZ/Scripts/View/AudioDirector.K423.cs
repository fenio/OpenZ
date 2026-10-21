using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K423: one event played as <see cref="Fire"/> does, with the voice that plays it handed back (null = not played: dropped for a reason
    /// the sound log names). The shuttle's kickflip watches its long whoosh to the end with it (View/OzPlayableShuttle.KickForward.cs). Nothing
    /// else changes: the same Play, the same rules for every event.
    /// </summary>
    public partial class AudioDirector
    {
        public static AudioSource FireTracked(string id, Vector3 pos, int emitter = -1, float volMul = 1f, float pitchMul = 1f)
        {
            if (!I) return null;
            I.lastStarted = null;
            bool played = I.Play(id, pos, emitter, volMul, pitchMul);
            var v = I.lastStarted;
            return played && v != null && v.src ? v.src : null;
        }
    }
}
