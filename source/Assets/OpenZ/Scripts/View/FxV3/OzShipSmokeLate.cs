using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K413: the shuttle's damage smoke step after the ship is drawn this frame. LateUpdate at order 205: after OzPlayableShuttle's pose
    /// (Update 200) and its effects and kickflip turn (LateUpdate 200), so every point OpenZDamageStates reads is on the drawn hull at its
    /// height (View/FxV3/OpenZDamageStates.ShipLate.cs). Added by OpenZDamageStates, on its own object, the first time a shuttle waits.
    /// </summary>
    [DefaultExecutionOrder(205)]
    public sealed class OzShipSmokeLate : MonoBehaviour
    {
        void LateUpdate()
        {
            var d = OpenZDamageStates.I;
            if (d) d.ShipLate();
        }
    }
}
