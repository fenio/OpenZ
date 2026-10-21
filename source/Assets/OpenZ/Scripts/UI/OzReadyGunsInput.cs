using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// k252: the ready-gun tiles' input (OzReadyGuns.TickInput): it runs after OpenZHud (-45) took this frame's press and before
    /// PlayerInput (0) reads the release, so a drag that ends over the HUD is cancelled before PlayerInput could place the gun there.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class OzReadyGunsInput : MonoBehaviour
    {
        OzReadyGuns view;
        void Update()
        {
            if (!view) view = GetComponent<OzReadyGuns>();
            if (view) view.TickInput();
        }
    }
}
