using UnityEngine.InputSystem;

namespace OpenZ
{
    /// <summary>K327: which key presses came from the touch layer or the gamepad (View/OzRallyKey.cs keeps their old meaning).</summary>
    public sealed partial class OzTouch
    {
        /// <summary>The key was sent by this layer in the last LateUpdate (a touch key, the gamepad's Y falling back to NEXT ROBOT,
        /// a held gamepad modifier): its press reaches the scripts in this frame.</summary>
        public static bool SentByTouch(Key k) => inst && inst.sentKeys.Contains(k);

        /// <summary>Checks: the touch / gamepad layer runs in this scene.</summary>
        public static bool Running => inst;
    }
}
