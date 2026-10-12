using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// build carries them: Resources/OpenZ/Vehicles/ApcCraneKit.asset (made by OpenZ/Vehicles/APC Crane Kit v1). See ApcCraneKit.</summary>
    public class ApcCraneKitData : ScriptableObject
    {
        public GameObject apcLive, craneLive;
        public GameObject[] apcWreck = new GameObject[0], craneWreck = new GameObject[0];
    }
}
